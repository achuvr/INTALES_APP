using UnityEngine;
using UnityEngine.Networking;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Firebase.Firestore;
using Cysharp.Threading.Tasks;

/// <summary>
/// アイテムデータベースをローカルにキャッシュ・同期するマネージャー。
///
/// 保存先: Application.persistentDataPath/
///   item_db/item_db.json       - 全アイテムデータ
///   item_db/item_db_meta.json  - 同期済みバージョン（差分判定用）
///   item_icons/{item_id}.png   - アイコン画像（DLキャッシュ）
///
/// 通信量の最適化:
///   アイテムマスターは master/items の1ドキュメントに全件格納されている。
///   master/config（セッション中どのみち読む）の items_version と
///   ローカルのバージョンを比較し、変わっていなければ読み取りゼロ、
///   変わっていれば master/items を1ドキュメントだけ読む。
///   （旧構造では職業ごとのコレクションを全件読んでいた = 約80読み取り/同期）
/// </summary>
public class ItemCacheManager : SingletonBehaviour<ItemCacheManager>
{
    private static readonly string[] JOB_KEYS =
        { "warrior", "magician", "archer", "gunner", "common" };

    private static string DbDir    => Path.Combine(Application.persistentDataPath, "item_db");
    private static string DbPath   => Path.Combine(DbDir, "item_db.json");
    private static string MetaPath => Path.Combine(DbDir, "item_db_meta.json");
    private static string IconDir  => Path.Combine(Application.persistentDataPath, "item_icons");
    private static string IconPath(string itemId) => Path.Combine(IconDir, $"{itemId}.png");

    private ItemDatabase _db   = new ItemDatabase();
    private ItemDbMeta   _meta = new ItemDbMeta();
    public  ItemDatabase Db       => _db;
    public  bool         IsLoaded { get; private set; }
    private bool         _syncing;
    private UniTaskCompletionSource _syncTcs;

    // ================================================================
    // 初期化
    // ================================================================
    private void Start()
    {
        Directory.CreateDirectory(DbDir);
        Directory.CreateDirectory(IconDir);
        LoadLocalCache();

#if UNITY_ANDROID || UNITY_IOS
        // バージョン比較ベースになったため毎起動チェックしても追加コストはほぼゼロ
        // （master/config はセッション中どのみち1回読む。未更新なら読み取り追加なし）
        AutoSyncAsync().Forget();
#endif
    }

    private async UniTaskVoid AutoSyncAsync()
    {
        await SyncAsync(msg => Debug.Log($"[ItemCache] {msg}"));
        await SyncIconsAsync(msg => Debug.Log($"[ItemCache] {msg}"));
        // ボードゲーム在庫も同じバージョン比較方式で同期（未更新なら読み取りゼロ）
        await BoardGameCatalog.SyncAsync(msg => Debug.Log($"[BoardGame] {msg}"));
    }

    // ================================================================
    // ローカルキャッシュ読み込み
    // ================================================================
    public void LoadLocalCache()
    {
        try
        {
            if (File.Exists(DbPath))
            {
                _db = JsonUtility.FromJson<ItemDatabase>(File.ReadAllText(DbPath))
                      ?? new ItemDatabase();
                Debug.Log($"[ItemCache] ローカルDB読み込み: {_db.TotalCount}件");
            }
            if (File.Exists(MetaPath))
                _meta = JsonUtility.FromJson<ItemDbMeta>(File.ReadAllText(MetaPath))
                        ?? new ItemDbMeta();
            IsLoaded = true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ItemCache] 読み込みエラー: {ex.Message}");
            _db   = new ItemDatabase();
            _meta = new ItemDbMeta();
        }
    }

    // ================================================================
    // Firestore からアイテムデータを同期（master/items 1ドキュメント）
    // ================================================================
    public async UniTask SyncAsync(Action<string> onProgress = null, bool force = false)
    {
        if (_syncing)
        {
            // 同期中なら完了を待つ（二重読み取り防止）
            if (_syncTcs != null) await _syncTcs.Task;
            return;
        }
        _syncing = true;
        _syncTcs = new UniTaskCompletionSource();

        try
        {
            // 1) バージョン確認（master/config はセッションキャッシュ済みなら読み取りゼロ）
            var config = await MasterData.GetConfigAsync();

            if (!force && _db.TotalCount > 0 && _meta.Version == config.ItemsVersion)
            {
                onProgress?.Invoke($"変更なし (v{_meta.Version}, {_db.TotalCount}件) → 読み取りスキップ");
                return;
            }

            // 2) master/items を1ドキュメントだけ読む
            onProgress?.Invoke($"master/items を取得中... (v{_meta.Version} → v{config.ItemsVersion})");
            var firestore = FirebaseFirestore.DefaultInstance;
            var snap = await firestore.Collection("master").Document("items")
                .GetSnapshotAsync().AsUniTask();

            if (!snap.Exists || !snap.ContainsField("items"))
            {
                onProgress?.Invoke("master/items が存在しません");
                return;
            }

            var itemsMap = snap.GetValue<Dictionary<string, object>>("items");
            var byJob = JOB_KEYS.ToDictionary(j => j, _ => new List<CachedItem>());
            int unknownJob = 0;

            foreach (var kv in itemsMap)
            {
                if (!(kv.Value is Dictionary<string, object> fields)) continue;
                var item = ParseItem(kv.Key, fields);
                if (byJob.TryGetValue(item.job ?? "", out var list)) list.Add(item);
                else unknownJob++;
            }

            foreach (var job in JOB_KEYS)
                _db.SetJobItems(job, byJob[job]);

            // シリーズ定義（セットスキル）も同じドキュメントの series マップから同期する
            _db.series = new List<CachedSeries>();
            if (snap.ContainsField("series"))
            {
                var seriesMap = snap.GetValue<Dictionary<string, object>>("series");
                foreach (var kv in seriesMap)
                {
                    if (!(kv.Value is Dictionary<string, object> fields)) continue;
                    _db.series.Add(new CachedSeries
                    {
                        series_id      = kv.Key,
                        name           = GetString(fields, "name"),
                        effects        = ParseEffectList(fields),
                        image_warrior  = GetString(fields, "image_warrior"),
                        image_magician = GetString(fields, "image_magician"),
                        image_archer   = GetString(fields, "image_archer"),
                        image_gunner   = GetString(fields, "image_gunner"),
                    });
                }
            }

            _meta.Version  = config.ItemsVersion;
            _meta.LastSync = DateTime.UtcNow.ToString("o");
            SaveToFile();

            string warn = unknownJob > 0 ? $"（不明な職業: {unknownJob}件スキップ）" : "";
            onProgress?.Invoke($"同期完了 v{_meta.Version}: {_db.TotalCount}件{warn}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ItemCache] 同期エラー: {ex.Message}");
            onProgress?.Invoke($"エラー: {ex.Message}");
        }
        finally
        {
            _syncing = false;
            IsLoaded = true;
            _syncTcs.TrySetResult();
        }
    }

    /// <summary>master/items のmapエントリを CachedItem に変換</summary>
    private CachedItem ParseItem(string itemId, Dictionary<string, object> d)
    {
        var item = new CachedItem { item_id = itemId };
        item.name         = GetString(d, "name");
        item.job          = GetString(d, "job");
        item.slot_type    = GetString(d, "slot_type");
        item.icon_url     = GetString(d, "icon_url");
        item.description  = GetString(d, "description");
        item.game         = GetString(d, "game");
        item.storage_path = GetString(d, "storage_path");
        item.skill_id     = GetString(d, "skill_id");
        item.series       = GetString(d, "series");

        if (d.TryGetValue("created_at", out var tsObj) && tsObj is Timestamp ts)
            item.created_at = ts.ToDateTime().ToString("o");

        item.effects = ParseEffectList(d);
        return item;
    }

    /// <summary>Firestoreのmapから effects 配列を LocalItemEffect のリストに変換（アイテム・シリーズ共通）。</summary>
    private static List<LocalItemEffect> ParseEffectList(Dictionary<string, object> d)
    {
        var list = new List<LocalItemEffect>();
        if (d.TryGetValue("effects", out var fxObj) && fxObj is IEnumerable<object> fxList)
        {
            foreach (var fx in fxList)
            {
                if (!(fx is Dictionary<string, object> m)) continue;
                list.Add(new LocalItemEffect
                {
                    effectType = GetString(m, "effect_type"),
                    value      = m.TryGetValue("value", out var v) ? Convert.ToInt32(v) : 0,
                });
            }
        }
        return list;
    }

    private static string GetString(Dictionary<string, object> d, string key)
        => d.TryGetValue(key, out var v) && v != null ? v.ToString() : "";

    // ================================================================
    // アイコン画像を同期（差分DL・通信量ログ付き）
    // ================================================================
    public async UniTask SyncIconsAsync(Action<string> onProgress = null)
    {
        Directory.CreateDirectory(IconDir);

        var allItems = _db.AllItems()
            .Where(x => !string.IsNullOrEmpty(x.icon_url) && !string.IsNullOrEmpty(x.item_id))
            .ToList();

        if (allItems.Count == 0)
        {
            onProgress?.Invoke("アイコン対象アイテムなし（先にアイテムデータを同期してください）");
            return;
        }

        long   totalBytes  = 0;
        int    downloaded  = 0;
        int    skipped     = 0;
        int    failed      = 0;

        onProgress?.Invoke($"アイコン同期開始: 対象{allItems.Count}件");
        Debug.Log($"[ItemCache] アイコン同期開始: {allItems.Count}件");

        for (int i = 0; i < allItems.Count; i++)
        {
            var item = allItems[i];
            string path = IconPath(item.item_id);

            // 既にDL済みはスキップ
            if (File.Exists(path))
            {
                skipped++;
                continue;
            }

            onProgress?.Invoke($"({i+1}/{allItems.Count}) {item.name ?? item.item_id} をDL中...");

            try
            {
                using var req = UnityWebRequest.Get(item.icon_url);
                await req.SendWebRequest();

                if (req.result == UnityWebRequest.Result.Success)
                {
                    var data = req.downloadHandler.data;
                    File.WriteAllBytes(path, data);
                    totalBytes += data.LongLength;
                    downloaded++;
                    Debug.Log($"[ItemCache] アイコンDL: {item.item_id} ({FormatBytes(data.LongLength)})");
                }
                else
                {
                    failed++;
                    Debug.LogWarning($"[ItemCache] アイコンDL失敗: {item.item_id} - {req.error}");
                }
            }
            catch (Exception ex)
            {
                failed++;
                Debug.LogWarning($"[ItemCache] アイコンDL例外: {item.item_id} - {ex.Message}");
            }
        }

        string summary =
            $"アイコン同期完了: DL={downloaded}件 ({FormatBytes(totalBytes)}) / " +
            $"スキップ={skipped}件 / 失敗={failed}件";
        onProgress?.Invoke(summary);
        Debug.Log($"[ItemCache] {summary}");

        // 合計通信量をまとめてログ
        Debug.Log($"[ItemCache] ===== アイコン通信量レポート =====");
        Debug.Log($"[ItemCache] 新規DL   : {downloaded}件");
        Debug.Log($"[ItemCache] 合計通信量: {FormatBytes(totalBytes)}");
        Debug.Log($"[ItemCache] スキップ  : {skipped}件 (キャッシュ済み)");
        Debug.Log($"[ItemCache] 失敗      : {failed}件");
        Debug.Log($"[ItemCache] ====================================");
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024)            return $"{bytes} B";
        if (bytes < 1024L * 1024L)   return $"{bytes / 1024f:F1} KB";
        return $"{bytes / (1024f * 1024f):F2} MB";
    }

    // ================================================================
    // アイコン取得 API
    // ================================================================
    /// <summary>生成済みアイコンSpriteのキャッシュ（item_id→Sprite。null=画像なしも記録）。</summary>
    private readonly Dictionary<string, Sprite> _iconCache = new Dictionary<string, Sprite>();

    /// <summary>図鑑アイコンの最大テクスチャ辺（これを超える画像は縮小する）。</summary>
    private const int ICON_MAX_SIZE = 256;

    /// <summary>
    /// item_id からローカルキャッシュの Sprite を返す（なければ null）。
    /// 一度生成した Sprite はキャッシュして使い回し（再生成によるメモリリークを防ぐ）、
    /// テクスチャは ICON_MAX_SIZE 以下へ縮小＋GPU圧縮してメモリを大幅に節約する。
    /// </summary>
    public Sprite GetIconSprite(string itemId)
    {
        if (string.IsNullOrEmpty(itemId)) return null;
        if (_iconCache.TryGetValue(itemId, out var cached)) return cached;

        string path = IconPath(itemId);
        if (!File.Exists(path)) { _iconCache[itemId] = null; return null; }

        Sprite sprite = null;
        try
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false); // mipmapなし
            tex.LoadImage(File.ReadAllBytes(path));
            tex = ShrinkAndCompress(tex, ICON_MAX_SIZE);
            sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[ItemCache] アイコン読み込み失敗: {itemId} - {ex.Message}");
        }
        _iconCache[itemId] = sprite;
        return sprite;
    }

    /// <summary>テクスチャを上限サイズ以下へ縮小し、GPU圧縮してメモリ使用量を削減する。</summary>
    private static Texture2D ShrinkAndCompress(Texture2D src, int max)
    {
        int w = src.width, h = src.height;
        int nw = w, nh = h;
        if (w > max || h > max)
        {
            float s = Mathf.Min((float)max / w, (float)max / h);
            nw = Mathf.Max(4, (Mathf.RoundToInt(w * s) / 4) * 4); // 圧縮できるよう4の倍数に
            nh = Mathf.Max(4, (Mathf.RoundToInt(h * s) / 4) * 4);
        }

        var rt = RenderTexture.GetTemporary(nw, nh, 0, RenderTextureFormat.ARGB32);
        var prev = RenderTexture.active;
        Graphics.Blit(src, rt);
        RenderTexture.active = rt;
        var dst = new Texture2D(nw, nh, TextureFormat.RGBA32, false);
        dst.ReadPixels(new Rect(0, 0, nw, nh), 0, 0);
        dst.Apply(false, false);
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        UnityEngine.Object.Destroy(src); // 元の大テクスチャを解放

        // GPU圧縮（Android=ETC2/ASTC）＋CPUコピー破棄でさらにメモリ削減
        try { dst.Compress(false); dst.Apply(false, true); }
        catch (Exception) { /* 圧縮非対応サイズ/環境はそのまま使う */ }
        return dst;
    }

    /// <summary>アイコンがローカルキャッシュに存在するか</summary>
    public bool HasIcon(string itemId)
        => !string.IsNullOrEmpty(itemId) && File.Exists(IconPath(itemId));

    private void SaveToFile()
    {
        File.WriteAllText(DbPath,   JsonUtility.ToJson(_db,   true));
        File.WriteAllText(MetaPath, JsonUtility.ToJson(_meta, true));
    }

    // ================================================================
    // DB 検索 API
    // ================================================================
    public List<CachedItem> GetAll()               => _db.AllItems();
    public List<CachedItem> GetByJob(string job)   => _db.GetJobItems(job);
    public List<CachedItem> GetBySlot(string slot) => _db.AllItems().Where(x => x.slot_type == slot).ToList();
    public List<CachedItem> GetByJobAndSlot(string job, string slot)
        => _db.GetJobItems(job).Where(x => x.slot_type == slot).ToList();
    public CachedItem FindById(string itemId)
        => _db.AllItems().FirstOrDefault(x => x.item_id == itemId);

    /// <summary>登録済みシリーズ（セットスキル）の一覧。</summary>
    public List<CachedSeries> GetAllSeries() => _db.series;

    /// <summary>シリーズIDから定義を返す（なければnull）。</summary>
    public CachedSeries FindSeriesById(string seriesId)
        => string.IsNullOrEmpty(seriesId) ? null : _db.series.FirstOrDefault(s => s.series_id == seriesId);

    // ================================================================
    // キャッシュクリア
    // ================================================================
    public void ClearCache()
    {
        try
        {
            if (File.Exists(DbPath))   File.Delete(DbPath);
            if (File.Exists(MetaPath)) File.Delete(MetaPath);
            _db      = new ItemDatabase();
            _meta    = new ItemDbMeta();
            IsLoaded = false;
            Debug.Log("[ItemCache] ローカルDBキャッシュをクリアしました");
        }
        catch (Exception ex) { Debug.LogError($"[ItemCache] クリアエラー: {ex.Message}"); }
    }

    public void ClearIconCache()
    {
        try
        {
            if (Directory.Exists(IconDir))
            {
                int cnt = 0;
                foreach (var f in Directory.GetFiles(IconDir, "*.png"))
                { File.Delete(f); cnt++; }
                Debug.Log($"[ItemCache] アイコンキャッシュをクリアしました ({cnt}件)");
            }
        }
        catch (Exception ex) { Debug.LogError($"[ItemCache] アイコンクリアエラー: {ex.Message}"); }
    }

    public string GetSyncInfo()
    {
        int iconCount = Directory.Exists(IconDir)
            ? Directory.GetFiles(IconDir, "*.png").Length : 0;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"ローカルDB: {_db.TotalCount}件 (v{_meta.Version})");
        string last = !string.IsNullOrEmpty(_meta.LastSync) &&
                      DateTime.TryParse(_meta.LastSync, out var dt)
            ? dt.ToLocalTime().ToString("MM/dd HH:mm")
            : "未取得";
        sb.AppendLine($"最終同期: {last}");
        sb.AppendLine($"アイコンキャッシュ: {iconCount}件");
        foreach (var job in JOB_KEYS)
            sb.AppendLine($"  {job}: {_db.GetJobItems(job).Count}件");
        return sb.ToString().TrimEnd();
    }
}

// ================================================================
// データモデル
// ================================================================

[Serializable]
public class CachedItem
{
    public string item_id;
    public string name;
    public string job;
    public string slot_type;
    public string icon_url;
    public string description;
    public string game;
    public string storage_path;
    public string created_at;
    public string skill_id; // スキルブックの任意発動スキルID（BattleSkillRegistry のキー）
    public string series;   // シリーズID（master/items の series マップのキー。空=シリーズなし）
    public List<LocalItemEffect> effects = new List<LocalItemEffect>();
}

/// <summary>シリーズ（セットスキル）定義。武器・頭・体・足を同一シリーズで揃えると effects が発動する。</summary>
[Serializable]
public class CachedSeries
{
    public string series_id;
    public string name;
    public List<LocalItemEffect> effects = new List<LocalItemEffect>();

    // セット発動中にキャラページのシルエットを差し替える専用画像URL（職業別・空=未設定）
    public string image_warrior;
    public string image_magician;
    public string image_archer;
    public string image_gunner;

    /// <summary>職業キーに対応するセット画像URL（未設定は空文字）。</summary>
    public string GetImageUrl(string job) => job switch
    {
        "warrior"  => image_warrior  ?? "",
        "magician" => image_magician ?? "",
        "archer"   => image_archer   ?? "",
        "gunner"   => image_gunner   ?? "",
        _          => "",
    };
}

[Serializable]
public class ItemDatabase
{
    public List<CachedItem> warrior  = new List<CachedItem>();
    public List<CachedItem> magician = new List<CachedItem>();
    public List<CachedItem> archer   = new List<CachedItem>();
    public List<CachedItem> gunner   = new List<CachedItem>();
    public List<CachedItem> common   = new List<CachedItem>();
    public List<CachedSeries> series = new List<CachedSeries>();

    public int TotalCount =>
        warrior.Count + magician.Count + archer.Count + gunner.Count + common.Count;

    public List<CachedItem> AllItems()
    {
        var all = new List<CachedItem>();
        all.AddRange(warrior); all.AddRange(magician);
        all.AddRange(archer);  all.AddRange(gunner);
        all.AddRange(common);
        return all;
    }

    public List<CachedItem> GetJobItems(string job)
    {
        switch (job)
        {
            case "warrior":  return warrior;
            case "magician": return magician;
            case "archer":   return archer;
            case "gunner":   return gunner;
            case "common":   return common;
            default:         return new List<CachedItem>();
        }
    }

    public void SetJobItems(string job, List<CachedItem> items)
    {
        switch (job)
        {
            case "warrior":  warrior  = items; break;
            case "magician": magician = items; break;
            case "archer":   archer   = items; break;
            case "gunner":   gunner   = items; break;
            case "common":   common   = items; break;
        }
    }
}

[Serializable]
public class ItemDbMeta
{
    /// <summary>同期済みの master/config.items_version。0は未同期</summary>
    public int Version;
    public string LastSync;
}
