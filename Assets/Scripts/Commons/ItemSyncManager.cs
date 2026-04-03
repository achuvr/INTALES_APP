using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Firebase.Firestore;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 毎週土曜 5:00 以降の初回起動時に Firestore の item コレクションを差分同期し、
/// ローカル JSON に職業→装備種類で保存するマネージャー。
///
/// Firestore 構造:
///   item/_metadata          … { last_updated: Timestamp }
///   item/{job}/items/{id}   … アイテムマスターデータ
///
/// ローカル保存先:
///   {persistentDataPath}/ItemDatabase/item_db.json
/// </summary>
public class ItemSyncManager : SingletonBehaviour<ItemSyncManager>
{
    private const string PREFS_LAST_SYNC = "ItemSync_LastSyncUtc";
    private const string DB_FOLDER       = "ItemDatabase";
    private const string DB_FILE         = "item_db.json";

    private static readonly string[] JOBS = { "warrior", "magician", "archer", "gunner" };

    // メモリ上のアイテムDB: key = "{job}/{slot_type}"
    private Dictionary<string, List<LocalItemEntry>> _db = new Dictionary<string, List<LocalItemEntry>>();

    // ================================================================
    // 公開API
    // ================================================================

    /// <summary>起動時に呼び出す。同期が必要なら実行し、ローカルDBをロードする。</summary>
    public async UniTask InitAsync()
    {
        LoadLocalDatabase();
        if (ShouldSync())
        {
            Debug.Log("[ItemSync] 土曜5時以降の初回起動 → 同期チェック開始");
            await SyncAsync();
        }
        else
        {
            Debug.Log("[ItemSync] 同期不要（前回同期後に土曜5時を跨いでいない）");
        }
    }

    /// <summary>職業＋スロットでアイテム一覧を取得</summary>
    public List<LocalItemEntry> GetItems(string job, string slotType)
    {
        var key = $"{job}/{slotType}";
        return _db.TryGetValue(key, out var list) ? list : new List<LocalItemEntry>();
    }

    /// <summary>全アイテムを取得</summary>
    public List<LocalItemEntry> GetAllItems()
    {
        return _db.Values.SelectMany(l => l).ToList();
    }

    /// <summary>アイテム名でローカルDBを検索</summary>
    public LocalItemEntry FindByName(string itemName)
    {
        return _db.Values.SelectMany(l => l)
            .FirstOrDefault(e => e.name == itemName);
    }

    /// <summary>itemId でローカルDBを検索</summary>
    public LocalItemEntry FindById(string itemId)
    {
        return _db.Values.SelectMany(l => l)
            .FirstOrDefault(e => e.itemId == itemId);
    }

    /// <summary>
    /// アイテムを入手し、キャラクターのインベントリに追加して Firestore に保存する。
    /// ローカルDBに存在するアイテムを itemId で指定する。
    /// </summary>
    public async UniTask<bool> AcquireItemAsync(string itemId)
    {
        var entry = FindById(itemId);
        if (entry == null)
        {
            Debug.LogError($"[ItemSync] アイテムが見つかりません: {itemId}");
            return false;
        }
        return await AcquireItemAsync(entry);
    }

    /// <summary>
    /// アイテムを入手し、キャラクターのインベントリに追加して Firestore に保存する。
    /// </summary>
    public async UniTask<bool> AcquireItemAsync(LocalItemEntry entry)
    {
        var manager = UserDataManager.instance;
        if (manager == null || string.IsNullOrEmpty(manager.UID))
        {
            Debug.LogError("[ItemSync] UserDataManager が未初期化です");
            return false;
        }

        int charIdx = manager.CurrentSelectCharacterNumber;
        var chara = manager.UserData.Characters[charIdx];

        // 既に所持していたら追加しない
        if (chara.Inventory.Any(r => r.ItemId == entry.itemId))
        {
            Debug.LogWarning($"[ItemSync] 既に所持しています: {entry.name} ({entry.itemId})");
            return false;
        }

        // ローカルのインベントリに追加
        var inventoryRef = new InventoryRef { Job = entry.job, ItemId = entry.itemId };
        chara.Inventory.Add(inventoryRef);

        // Firestore に保存
        try
        {
            var db = FirebaseFirestore.DefaultInstance;
            var docRef = db.Collection("users")
                           .Document(manager.UID)
                           .Collection("characters")
                           .Document(charIdx.ToString());

            var inventoryData = new List<Dictionary<string, object>>();
            foreach (var r in chara.Inventory)
            {
                inventoryData.Add(new Dictionary<string, object>
                {
                    { "job",     r.Job    },
                    { "item_id", r.ItemId },
                });
            }

            await docRef.SetAsync(
                new Dictionary<string, object> { { "inventory", inventoryData } },
                SetOptions.MergeAll
            ).AsUniTask();

            Debug.Log($"[ItemSync] アイテム入手: {entry.name} ({entry.job}/{entry.itemId})");
            return true;
        }
        catch (Exception ex)
        {
            // 失敗したらローカルからも戻す
            chara.Inventory.Remove(inventoryRef);
            Debug.LogError($"[ItemSync] アイテム入手エラー: {ex.Message}");
            return false;
        }
    }

    // ================================================================
    // 同期判定
    // ================================================================

    /// <summary>前回同期から「直近の土曜5:00」を跨いでいたら true</summary>
    private bool ShouldSync()
    {
        var now = DateTime.Now;
        var lastSaturday5am = GetMostRecentSaturday5AM(now);

        // 現在時刻がまだ土曜5時より前なら、その前の週の土曜5時を使う
        // (GetMostRecentSaturday5AM は now 以前の直近を返す)

        var lastSyncStr = PlayerPrefs.GetString(PREFS_LAST_SYNC, "");
        if (string.IsNullOrEmpty(lastSyncStr))
            return true; // 初回

        if (!DateTime.TryParse(lastSyncStr, null,
                System.Globalization.DateTimeStyles.RoundtripKind, out var lastSync))
            return true;

        // 前回同期が直近の土曜5時より前なら同期が必要
        return lastSync < lastSaturday5am;
    }

    /// <summary>now 以前で最も近い「土曜 05:00」を返す</summary>
    private static DateTime GetMostRecentSaturday5AM(DateTime now)
    {
        // 今日の曜日から直近の土曜を算出
        int daysBack = ((int)now.DayOfWeek - (int)DayOfWeek.Saturday + 7) % 7;
        var saturday = now.Date.AddDays(-daysBack);
        var sat5am = saturday.AddHours(5);

        // 今日が土曜だが5時前なら、先週の土曜5時
        if (sat5am > now)
            sat5am = sat5am.AddDays(-7);

        return sat5am;
    }

    // ================================================================
    // 同期処理
    // ================================================================

    private async UniTask SyncAsync()
    {
        var db = FirebaseFirestore.DefaultInstance;

        // 1) メタデータを1回読み取り、更新有無を判定
        var lastSyncStr = PlayerPrefs.GetString(PREFS_LAST_SYNC, "");
        DateTime lastSyncUtc = DateTime.MinValue;
        if (!string.IsNullOrEmpty(lastSyncStr))
            DateTime.TryParse(lastSyncStr, null,
                System.Globalization.DateTimeStyles.RoundtripKind, out lastSyncUtc);

        try
        {
            var metaRef = db.Collection("item").Document("_metadata");
            var metaSnap = await metaRef.GetSnapshotAsync().AsUniTask();

            if (metaSnap.Exists && metaSnap.ContainsField("last_updated"))
            {
                var serverTs = metaSnap.GetValue<Timestamp>("last_updated");
                var serverUtc = serverTs.ToDateTime();

                if (serverUtc <= lastSyncUtc)
                {
                    Debug.Log("[ItemSync] メタデータ未更新 → 同期スキップ");
                    SaveLastSyncTime();
                    return;
                }
                Debug.Log($"[ItemSync] 更新検出: server={serverUtc:O} > lastSync={lastSyncUtc:O}");
            }
            else
            {
                Debug.Log("[ItemSync] メタデータ未作成 → 全件取得");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ItemSync] メタデータ取得エラー: {ex.Message}");
            return;
        }

        // 2) 各職業の差分を取得
        int totalNew = 0;
        foreach (var job in JOBS)
        {
            try
            {
                Query query = db.Collection("item").Document(job).Collection("items");

                // 前回同期時刻があれば差分だけ取得
                if (lastSyncUtc > DateTime.MinValue)
                {
                    var fromTs = Timestamp.FromDateTime(lastSyncUtc.ToUniversalTime());
                    query = query.WhereGreaterThan("created_at", fromTs);
                }

                var snapshot = await query.GetSnapshotAsync().AsUniTask();
                foreach (var doc in snapshot.Documents)
                {
                    if (!doc.Exists) continue;
                    var entry = DocToEntry(doc, job);
                    AddOrUpdateEntry(entry);
                    totalNew++;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ItemSync] {job} 取得エラー: {ex.Message}");
            }
        }

        Debug.Log($"[ItemSync] 同期完了: {totalNew} 件追加/更新");
        SaveLocalDatabase();
        SaveLastSyncTime();
    }

    private void SaveLastSyncTime()
    {
        PlayerPrefs.SetString(PREFS_LAST_SYNC, DateTime.UtcNow.ToString("O"));
        PlayerPrefs.Save();
    }

    // ================================================================
    // Firestore → LocalItemEntry 変換
    // ================================================================

    private static LocalItemEntry DocToEntry(DocumentSnapshot doc, string job)
    {
        var entry = new LocalItemEntry
        {
            job      = job,
            itemId   = doc.Id,
            name     = doc.ContainsField("name")        ? doc.GetValue<string>("name")        : "",
            slotType = doc.ContainsField("slot_type")    ? doc.GetValue<string>("slot_type")    : "",
            iconUrl  = doc.ContainsField("icon_url")     ? doc.GetValue<string>("icon_url")     : "",
            description = doc.ContainsField("description") ? doc.GetValue<string>("description") : "",
            game     = doc.ContainsField("game")         ? doc.GetValue<string>("game")         : "",
        };

        // effects（任意フィールド）
        if (doc.ContainsField("effects"))
        {
            try
            {
                var rawEffects = doc.GetValue<List<Dictionary<string, object>>>("effects");
                entry.effects = rawEffects?.Select(e => new LocalItemEffect
                {
                    effectType = e.ContainsKey("effect_type") ? e["effect_type"]?.ToString() : "",
                    value      = e.ContainsKey("value") ? Convert.ToInt32(e["value"]) : 0,
                }).ToList() ?? new List<LocalItemEffect>();
            }
            catch
            {
                entry.effects = new List<LocalItemEffect>();
            }
        }
        else
        {
            entry.effects = new List<LocalItemEffect>();
        }

        return entry;
    }

    // ================================================================
    // メモリDB操作
    // ================================================================

    private void AddOrUpdateEntry(LocalItemEntry entry)
    {
        var key = $"{entry.job}/{entry.slotType}";
        if (!_db.ContainsKey(key))
            _db[key] = new List<LocalItemEntry>();

        var list = _db[key];
        var idx = list.FindIndex(e => e.itemId == entry.itemId);
        if (idx >= 0) list[idx] = entry;
        else          list.Add(entry);
    }

    // ================================================================
    // ローカル JSON 読み書き
    // ================================================================

    private string DbPath => Path.Combine(Application.persistentDataPath, DB_FOLDER, DB_FILE);

    private void LoadLocalDatabase()
    {
        _db.Clear();
        var path = DbPath;
        if (!File.Exists(path))
        {
            Debug.Log("[ItemSync] ローカルDBなし → 空で初期化");
            return;
        }

        try
        {
            var json = File.ReadAllText(path);
            var wrapper = JsonUtility.FromJson<LocalItemDatabaseWrapper>(json);
            if (wrapper?.items != null)
            {
                foreach (var entry in wrapper.items)
                {
                    var key = $"{entry.job}/{entry.slotType}";
                    if (!_db.ContainsKey(key))
                        _db[key] = new List<LocalItemEntry>();
                    _db[key].Add(entry);
                }
            }
            Debug.Log($"[ItemSync] ローカルDB読み込み: {wrapper?.items?.Count ?? 0} 件");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ItemSync] ローカルDB読み込みエラー: {ex.Message}");
        }
    }

    private void SaveLocalDatabase()
    {
        var allItems = _db.Values.SelectMany(l => l).ToList();
        var wrapper = new LocalItemDatabaseWrapper { items = allItems };
        var json = JsonUtility.ToJson(wrapper, true);

        var dir = Path.GetDirectoryName(DbPath);
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(DbPath, json);
        Debug.Log($"[ItemSync] ローカルDB保存: {allItems.Count} 件");
    }
}

// ================================================================
// ローカル保存用データクラス（JsonUtility 対応）
// ================================================================

[Serializable]
public class LocalItemDatabaseWrapper
{
    public List<LocalItemEntry> items = new List<LocalItemEntry>();
}

[Serializable]
public class LocalItemEntry
{
    public string job;
    public string itemId;
    public string name;
    public string slotType;
    public string iconUrl;
    public string description;
    public string game;
    public List<LocalItemEffect> effects = new List<LocalItemEffect>();
}

[Serializable]
public class LocalItemEffect
{
    public string effectType;
    public int value;
}
