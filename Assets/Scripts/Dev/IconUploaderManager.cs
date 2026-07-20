using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using TMPro;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Firebase.Storage;
using Firebase.Firestore;
using Cysharp.Threading.Tasks;

/// <summary>
/// 装備登録・編集シーン（IconUploaderシーン用・管理者向け）。
/// 装備名・装備可能職業・装備部位・アイコン・説明・装備効果・シリーズ・
/// 通常ガチャ排出の有無を指定して、装備をデータベースへ登録する。
/// 「既存の装備を編集」から登録済み装備を読み込んで同じフォームで更新もできる
/// （編集時のアイコンは差し替え任意。ガチャ排出のON/OFF・重みも変更可能）。
///
/// 書き込み先:
///  - Storage: gs://intales-a0459.firebasestorage.app/item/{job}/（アイコン画像）
///  - Firestore master/items … items.{autoId}（装備本体）、series.{autoId}（シリーズ定義）
///  - Firestore master/config.items_version … クライアント差分同期用にインクリメント
///  - Firestore master/gacha … 通常ガチャ排出ONなら pools.standard.entries に {type:"item", id, weight} を追加
///
/// シリーズ: 武器・頭・体・足の4部位を同一シリーズで揃えるとセットスキル（effects）が発動する。
/// このシーンから新規シリーズ（名前＋セットスキル効果）の登録と、既存シリーズの編集ができる
/// （編集はそのシリーズが付いた全装備のセットスキルに反映される）。
/// </summary>
public class IconUploaderManager : MonoBehaviour
{
    // ---- 職業 ----
    private static readonly (string label, string key)[] JOBS =
    {
        ("戦士",    "warrior"),
        ("魔法使い","magician"),
        ("弓使い",  "archer"),
        ("銃使い",  "gunner"),
        ("共通",    "common"),
    };

    // ---- 装備部位 ----
    private static readonly (string label, string key)[] CATS =
    {
        ("武器",    "weapon"),
        ("頭",      "head"),
        ("体",      "body"),
        ("足",      "feet"),
        ("スキルA", "skill_book_a"),
        ("スキルB", "skill_book_b"),
    };

    // ---- 装備効果の選択肢（EffectType と1対1） ----
    private static readonly (EffectType type, string label)[] EFFECT_CHOICES =
    {
        (EffectType.AtkUp,            "攻撃力アップ"),
        (EffectType.DefUp,            "防御力アップ"),
        (EffectType.HpUp,             "HPアップ"),
        (EffectType.SpeedUp,          "速度アップ"),
        (EffectType.CriticalRateUp,   "クリティカル率アップ(%)"),
        (EffectType.CriticalDamageUp, "クリティカルダメージアップ"),
        (EffectType.ProbUp,           "成功確率アップ(%)"),
        (EffectType.BonusExp,         "獲得経験値アップ(%)"),
        (EffectType.GoldBonus,        "獲得ゴールドアップ(%)"),
        (EffectType.SkillSlotUnlock,  "スキルスロット解放"),
        (EffectType.SpecialAbility,   "特殊能力"),
    };

    private const string STORAGE_BASE = "gs://intales-a0459.firebasestorage.app";
    private const int DEFAULT_GACHA_WEIGHT = 10;

    // ---- 効果入力1行分のUI ----
    private class EffectRowUI
    {
        public GameObject go;
        public EffectType type = EffectType.AtkUp;
        public TextMeshProUGUI typeLabel;
        public TMP_InputField valueInput;
    }

    // ---- UI 参照 ----
    private Transform _canvasTf;
    private Transform _content;          // スクロールフォームのコンテンツ
    private TMP_FontAsset _jp;
    private TMP_InputField _itemNameInput;
    private TMP_InputField _gameNameInput;
    private TMP_InputField _descriptionInput;
    private TMP_InputField _filePathInput;
    private RawImage       _previewImage;
    private TextMeshProUGUI _statusLabel;
    private GameObject[]   _jobBtns;
    private GameObject[]   _catBtns;
    private int _selectedJob = 0;
    private int _selectedCat = 0;

    // 装備効果
    private readonly List<EffectRowUI> _itemEffectRows = new List<EffectRowUI>();
    private Transform _itemEffectsContainer;

    // シリーズ
    private readonly List<CachedSeries> _seriesList = new List<CachedSeries>();
    private CachedSeries _selectedSeries;           // null = なし
    private TextMeshProUGUI _seriesBtnLabel;

    // 既存装備の編集
    private string _editingItemId;                              // null = 新規登録モード
    private Dictionary<string, object> _editingFields;          // 編集中アイテムの元データ（Firestoreのmap）
    private readonly List<(string id, Dictionary<string, object> fields)> _allItems
        = new List<(string, Dictionary<string, object>)>();     // master/items の items 全件
    private List<object> _gachaEntriesCache;                    // standardプールのentries（編集時のプリフィル用）
    private TextMeshProUGUI _editBtnLabel;
    private TextMeshProUGUI _uploadBtnLabel;

    // 通常ガチャ排出
    private bool _gachaOn = false;
    private TextMeshProUGUI _gachaToggleLabel;
    private Image _gachaToggleImage;
    private GameObject _gachaWeightRoot;
    private TMP_InputField _gachaWeightInput;

    // ピッカー／シリーズ作成モーダル
    private GameObject _pickerOverlay;
    private GameObject _seriesModal;
    private TMP_InputField _seriesNameInput;
    private TextMeshProUGUI _seriesStatusLabel;
    private readonly List<EffectRowUI> _seriesEffectRows = new List<EffectRowUI>();
    private Transform _seriesEffectsContainer;
    private bool _savingSeries;

    // シリーズのセット発動中キャラ画像（職業別。キャラの職業なので common は無い）
    private static readonly (string label, string key)[] CHAR_JOBS =
    {
        ("戦士", "warrior"), ("魔法使い", "magician"), ("弓使い", "archer"), ("銃使い", "gunner"),
    };

    private class SeriesImageSlot
    {
        public string existingUrl = "";  // 保存済みのURL（編集時）
        public byte[] bytes;             // 新しく選んだ画像（null=変更なし）
        public string fileName;
        public bool cleared;             // クリア指定（保存でURLを空にする）
        public Texture2D tex;            // プレビュー用
    }
    private SeriesImageSlot[] _seriesImgSlots;
    private int _seriesImgJobIdx;
    private GameObject[] _seriesImgTabs;
    private RawImage _seriesImgPreview;
    private TextMeshProUGUI _seriesImgStateLabel;

    // ---- 状態 ----
    private Texture2D _selectedTex;
    private byte[]    _selectedBytes;
    private string    _selectedFileName;
    private bool      _uploading;
    private Button    _uploadBtn;

    // ---- 色 ----
    private static readonly Color C_BG       = new Color(0.10f,0.08f,0.06f);
    private static readonly Color C_PANEL    = new Color(0.18f,0.14f,0.10f);
    private static readonly Color C_ROW      = new Color(0.14f,0.11f,0.08f);
    private static readonly Color C_GOLD     = new Color(0.92f,0.72f,0.22f);
    private static readonly Color C_TEXT     = new Color(0.95f,0.90f,0.78f);
    private static readonly Color C_MUTED    = new Color(0.55f,0.48f,0.38f);
    private static readonly Color C_BTN_PICK   = new Color(0.30f,0.20f,0.50f);
    private static readonly Color C_BTN_UPLOAD = new Color(0.15f,0.45f,0.20f);
    private static readonly Color C_BTN_DEL    = new Color(0.55f,0.20f,0.20f);
    private static readonly Color C_BTN_ADD    = new Color(0.20f,0.35f,0.55f);
    private static readonly Color C_BTN_GRAY   = new Color(0.30f,0.28f,0.24f);
    private static readonly Color C_TOGGLE_ON  = new Color(0.18f,0.50f,0.24f);
    private static readonly Color C_ERR = new Color(0.85f,0.25f,0.25f);
    private static readonly Color C_OK  = new Color(0.28f,0.72f,0.28f);

    void Start()
    {
        // エディターでは直接ロード（シーン新規ロード時にメモリにない場合の対策）
#if UNITY_EDITOR
        _jp = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Fonts/jp.asset");
        if (_jp == null)
            _jp = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Fonts 1/jp.asset");
#endif
        // エディター以外 or 上記で取得できなかった場合のフォールバック
        if (_jp == null)
        {
            var fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
            _jp = fonts.FirstOrDefault(f => f.name.ToLower() == "jp") ?? fonts.FirstOrDefault();
        }
        BuildUI();
        LoadMasterAsync().Forget();
    }

    void ApplyFont(TextMeshProUGUI t) { if (_jp != null) t.font = _jp; }
    void SS(string s, Color c) { if (_statusLabel != null) { _statusLabel.text = s; _statusLabel.color = c; } }

    private void RefreshUploadBtn()
    {
        if (_uploadBtn == null) return;
        // 編集モードではアイコン差し替えは任意（未選択なら既存アイコンを維持）
        bool imageReady = _editingItemId != null
            || (_selectedBytes != null && _selectedBytes.Length > 0
                && !string.IsNullOrWhiteSpace(_filePathInput?.text));
        bool ready = imageReady
                  && !string.IsNullOrWhiteSpace(_itemNameInput?.text)
                  && !string.IsNullOrWhiteSpace(_gameNameInput?.text);
        _uploadBtn.interactable = ready;
        var img = _uploadBtn.GetComponent<Image>();
        if (img != null) img.color = ready ? C_BTN_UPLOAD : new Color(C_BTN_UPLOAD.r, C_BTN_UPLOAD.g, C_BTN_UPLOAD.b, 0.3f);
        var txt = _uploadBtn.GetComponentInChildren<TextMeshProUGUI>();
        if (txt != null) txt.color = ready ? C_TEXT : new Color(C_TEXT.r, C_TEXT.g, C_TEXT.b, 0.3f);
    }

    // ================================================================
    // マスターの読み込み（master/items の items・series と master/gacha の排出テーブル）
    // ================================================================
    private async UniTaskVoid LoadMasterAsync()
    {
        try
        {
            var db = FirebaseFirestore.DefaultInstance;
            var snap = await db.Collection("master").Document("items")
                .GetSnapshotAsync().AsUniTask();

            _seriesList.Clear();
            if (snap.Exists && snap.ContainsField("series"))
            {
                var map = snap.GetValue<Dictionary<string, object>>("series");
                foreach (var kv in map)
                {
                    if (!(kv.Value is Dictionary<string, object> f)) continue;
                    var cs = new CachedSeries
                    {
                        series_id      = kv.Key,
                        name           = GetStr(f, "name"),
                        image_warrior  = GetStr(f, "image_warrior"),
                        image_magician = GetStr(f, "image_magician"),
                        image_archer   = GetStr(f, "image_archer"),
                        image_gunner   = GetStr(f, "image_gunner"),
                    };
                    if (f.TryGetValue("effects", out var fxObj) && fxObj is IEnumerable<object> fxList)
                    {
                        foreach (var fx in fxList)
                        {
                            if (!(fx is Dictionary<string, object> m)) continue;
                            cs.effects.Add(new LocalItemEffect
                            {
                                effectType = GetStr(m, "effect_type"),
                                value      = m.TryGetValue("value", out var v) ? Convert.ToInt32(v) : 0,
                            });
                        }
                    }
                    _seriesList.Add(cs);
                }
            }

            // 既存装備の一覧（編集用）
            _allItems.Clear();
            if (snap.Exists && snap.ContainsField("items"))
            {
                var itemsMap = snap.GetValue<Dictionary<string, object>>("items");
                foreach (var kv in itemsMap)
                {
                    if (!(kv.Value is Dictionary<string, object> f)) continue;
                    _allItems.Add((kv.Key, f));
                }
                _allItems.Sort((a, b) => string.Compare(GetStr(a.fields, "name"), GetStr(b.fields, "name"), StringComparison.Ordinal));
            }

            // 通常ガチャの排出テーブル（編集時のON/OFF・重みプリフィル用）
            try
            {
                var gsnap = await db.Collection("master").Document("gacha")
                    .GetSnapshotAsync().AsUniTask();
                _gachaEntriesCache = null;
                if (gsnap.Exists && gsnap.ContainsField("pools"))
                {
                    var pools = gsnap.GetValue<Dictionary<string, object>>("pools");
                    if (pools.TryGetValue("standard", out var stdObj)
                        && stdObj is Dictionary<string, object> std
                        && std.TryGetValue("entries", out var entObj)
                        && entObj is List<object> entries)
                        _gachaEntriesCache = entries;
                }
            }
            catch (Exception gex)
            {
                Debug.LogWarning($"[EquipRegister] ガチャテーブル読み込み失敗: {gex.Message}");
            }

            SS($"装備{_allItems.Count}件・シリーズ{_seriesList.Count}件を読み込みました。新規登録するか、既存の装備を選んで編集してください", C_MUTED);
        }
        catch (Exception ex)
        {
            SS($"マスター読み込みエラー: {ex.Message}", C_ERR);
            Debug.LogError($"[EquipRegister] {ex}");
        }
    }

    /// <summary>standardプールから指定アイテムの排出エントリを探す（なければnull）。</summary>
    private Dictionary<string, object> FindGachaEntry(string itemId)
    {
        if (_gachaEntriesCache == null) return null;
        foreach (var e in _gachaEntriesCache)
            if (e is Dictionary<string, object> m && GetStr(m, "type") == "item" && GetStr(m, "id") == itemId)
                return m;
        return null;
    }

    private static string GetStr(Dictionary<string, object> d, string key)
        => d.TryGetValue(key, out var v) && v != null ? v.ToString() : "";

    // ================================================================
    // ファイル選択
    // ================================================================
    private void OnPickFile()
    {
#if UNITY_EDITOR
        string path = UnityEditor.EditorUtility.OpenFilePanel(
            "画像を選択", "", "png,jpg,jpeg");
        if (!string.IsNullOrEmpty(path))
        {
            _filePathInput.text = path;
            LoadImageFromPath(path);
        }
#else
        SS("ファイルパスを直接入力してください", C_MUTED);
#endif
    }

    private void OnPathChanged(string path)
    {
        if (!string.IsNullOrEmpty(path) && File.Exists(path))
            LoadImageFromPath(path);
    }

    private void LoadImageFromPath(string path)
    {
        try
        {
            _selectedBytes    = File.ReadAllBytes(path);
            _selectedFileName = Path.GetFileName(path);

            if (_selectedTex != null) Destroy(_selectedTex);
            _selectedTex = new Texture2D(2, 2);
            _selectedTex.LoadImage(_selectedBytes);

            _previewImage.texture = _selectedTex;
            _previewImage.color   = Color.white;
            SS($"画像読み込み完了: {_selectedFileName} ({_selectedBytes.Length/1024f:F1} KB)", C_OK);
            RefreshUploadBtn();
        }
        catch (Exception ex)
        {
            SS($"画像読み込みエラー: {ex.Message}", C_ERR);
        }
    }

    // ================================================================
    // 登録・更新（Storageアップロード → Firestore保存 → ガチャ反映）
    // ================================================================
    private async UniTaskVoid UploadAndSave()
    {
        if (_uploading) return;
        bool editing = _editingItemId != null;

        // バリデーション
        string itemName = _itemNameInput.text.Trim();
        string gameName = _gameNameInput.text.Trim();
        bool hasNewImage = _selectedBytes != null && _selectedBytes.Length > 0;
        if (!editing && !hasNewImage)
        {
            SS("アイコン画像が選択されていません", C_ERR); return;
        }
        if (string.IsNullOrEmpty(itemName))
        {
            SS("装備名を入力してください", C_ERR); return;
        }
        var effects = CollectEffects(_itemEffectRows, out int badEffects);
        if (badEffects > 0)
        {
            SS($"数値が不正な効果行が{badEffects}件あります（整数で入力してください）", C_ERR); return;
        }
        int gachaWeight = DEFAULT_GACHA_WEIGHT;
        if (_gachaOn && (!int.TryParse(_gachaWeightInput.text.Trim(), out gachaWeight) || gachaWeight <= 0))
        {
            SS("ガチャの排出重みは1以上の整数で入力してください", C_ERR); return;
        }

        _uploading = true;

        try
        {
            string jobKey  = JOBS[_selectedJob].key;
            string catKey  = CATS[_selectedCat].key;

            // ---- アイコン（新規は必須。編集は選んだ時だけ差し替え） ----
            string iconUrl     = editing ? GetStr(_editingFields, "icon_url") : "";
            string storagePath = editing ? GetStr(_editingFields, "storage_path") : "";
            if (hasNewImage)
            {
                SS("Firebase Storage にアップロード中...", C_MUTED);
                string ext = Path.GetExtension(_selectedFileName).ToLower();
                if (string.IsNullOrEmpty(ext)) ext = ".png";

                // 保存先パス: item/{jobKey}/{timestamp}_{itemName}{ext}
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string safeItem  = itemName.Replace(" ","_").Replace("/","_");
                storagePath = $"item/{jobKey}/{timestamp}_{safeItem}{ext}";

                var storage    = FirebaseStorage.DefaultInstance;
                var storageRef = storage.GetReferenceFromUrl(STORAGE_BASE);
                var fileRef    = storageRef.Child(storagePath);

                var uploadMeta = new MetadataChange { ContentType = ext == ".png" ? "image/png" : "image/jpeg" };
                await fileRef.PutBytesAsync(_selectedBytes, uploadMeta).AsUniTask();

                SS("ダウンロード URL を取得中...", C_MUTED);
                Uri downloadUri = await fileRef.GetDownloadUrlAsync().AsUniTask();
                iconUrl = downloadUri.ToString();
            }

            SS("Firestore に装備データを保存中...", C_MUTED);

            // ---- Firestore 保存（master/items の items.{id} へマージ） ----
            var db = FirebaseFirestore.DefaultInstance;
            var itemData = new Dictionary<string, object>
            {
                { "name",        itemName },
                { "game",        gameName },
                { "job",         jobKey   },
                { "slot_type",   catKey   },
                { "icon_url",    iconUrl },
                { "storage_path", storagePath },
                { "description", _descriptionInput?.text.Trim() ?? "" },
                { "effects",     effects },
                { "series",      _selectedSeries?.series_id ?? "" },
            };
            // 新規はcreated_at、編集は元のcreated_atを保持してupdated_atを付ける
            itemData[editing ? "updated_at" : "created_at"] = Timestamp.GetCurrentTimestamp();

            string itemId = editing ? _editingItemId : db.Collection("master").Document().Id;
            await db.Collection("master").Document("items")
                .SetAsync(new Dictionary<string, object>
                {
                    { "items", new Dictionary<string, object> { { itemId, itemData } } }
                }, SetOptions.MergeAll)
                .AsUniTask();

            // master/config の items_version を上げる（クライアントの差分同期判定用）
            await db.Collection("master").Document("config")
                .UpdateAsync("items_version", FieldValue.Increment(1))
                .AsUniTask();

            // ---- 通常ガチャ排出の反映 ----
            try
            {
                if (editing)
                {
                    // 編集: 排出テーブルを読み直して エントリの追加/更新/削除 を反映
                    SS("通常ガチャの排出テーブルを更新中...", C_MUTED);
                    await UpdateGachaEntryAsync(db, itemId, _gachaOn, gachaWeight);
                }
                else if (_gachaOn)
                {
                    SS("通常ガチャの排出テーブルに追加中...", C_MUTED);
                    var entry = new Dictionary<string, object>
                    {
                        { "type",   "item" },
                        { "id",     itemId },
                        { "weight", gachaWeight },
                    };
                    await db.Collection("master").Document("gacha")
                        .UpdateAsync(new Dictionary<FieldPath, object>
                        {
                            { new FieldPath("pools", "standard", "entries"), FieldValue.ArrayUnion(entry) }
                        })
                        .AsUniTask();
                    _gachaEntriesCache?.Add(entry);
                }
            }
            catch (Exception gex)
            {
                // 装備本体は保存済みなので、ガチャ反映だけ失敗した旨を伝える
                SS($"装備は保存しましたがガチャ反映に失敗: {gex.Message}", C_ERR);
                Debug.LogError($"[EquipRegister] ガチャ反映エラー: {gex}");
                return;
            }

            // ---- ローカルの一覧キャッシュを更新（続けて編集できるように） ----
            if (editing)
            {
                foreach (var kv in itemData) _editingFields[kv.Key] = kv.Value;
            }
            else
            {
                _allItems.Add((itemId, itemData));
                _allItems.Sort((a, b) => string.Compare(GetStr(a.fields, "name"), GetStr(b.fields, "name"), StringComparison.Ordinal));
            }

            string seriesNote = _selectedSeries != null ? $" / シリーズ「{_selectedSeries.name}」" : "";
            string gachaNote  = _gachaOn ? $" / 通常ガチャ排出（重み{gachaWeight}）" : (editing ? " / ガチャ排出なし" : "");
            SS($"{(editing ? "更新" : "登録")}完了！「{itemName}」 ID: {itemId}{seriesNote}{gachaNote}", C_OK);
            Debug.Log($"[EquipRegister] {(editing ? "更新" : "保存")}完了 id={itemId} series={_selectedSeries?.series_id ?? "-"} gacha={_gachaOn}");
        }
        catch (Exception ex)
        {
            SS($"エラー: {ex.Message}", C_ERR);
            Debug.LogError($"[EquipRegister] {ex}");
        }
        finally
        {
            _uploading = false;
        }
    }

    /// <summary>
    /// standardプールの排出エントリを最新状態に合わせて書き換える（編集用）。
    /// 保存直前にドキュメントを読み直し、対象アイテムのエントリを除去してから
    /// ON なら新しい重みで追加し、配列ごと書き戻す（ON/OFF・重み変更のどちらにも対応）。
    /// </summary>
    private async UniTask UpdateGachaEntryAsync(FirebaseFirestore db, string itemId, bool on, int weight)
    {
        var gref = db.Collection("master").Document("gacha");
        var snap = await gref.GetSnapshotAsync().AsUniTask();
        if (!snap.Exists || !snap.ContainsField("pools"))
        {
            if (on) throw new Exception("master/gacha の pools が見つかりません");
            return; // ガチャ未設定でOFFなら何もしない
        }

        var pools = snap.GetValue<Dictionary<string, object>>("pools");
        if (!(pools.TryGetValue("standard", out var stdObj) && stdObj is Dictionary<string, object> std))
        {
            if (on) throw new Exception("standard プールが見つかりません");
            return;
        }

        var entries = (std.TryGetValue("entries", out var entObj) && entObj is List<object> raw)
            ? raw.ToList() : new List<object>();
        entries.RemoveAll(e => e is Dictionary<string, object> m
            && GetStr(m, "type") == "item" && GetStr(m, "id") == itemId);
        if (on)
        {
            entries.Add(new Dictionary<string, object>
            {
                { "type",   "item" },
                { "id",     itemId },
                { "weight", weight },
            });
        }

        await gref.UpdateAsync(new Dictionary<FieldPath, object>
        {
            { new FieldPath("pools", "standard", "entries"), entries }
        }).AsUniTask();

        _gachaEntriesCache = entries; // プリフィル用キャッシュも最新化
    }

    /// <summary>効果入力行を Firestore 用の effects 配列へ変換する。値が整数でない行は badCount に数える。</summary>
    private static List<Dictionary<string, object>> CollectEffects(List<EffectRowUI> rows, out int badCount)
    {
        badCount = 0;
        var list = new List<Dictionary<string, object>>();
        foreach (var r in rows)
        {
            if (r == null || r.go == null) continue;
            if (!int.TryParse(r.valueInput.text.Trim(), out int v)) { badCount++; continue; }
            list.Add(new Dictionary<string, object>
            {
                { "effect_type", r.type.ToString() },
                { "value",       v },
            });
        }
        return list;
    }

    // ================================================================
    // 新規シリーズの保存
    // ================================================================
    private async UniTaskVoid SaveSeriesAsync()
    {
        if (_savingSeries) return;
        var editing = _editingSeries; // CloseSeriesModal でnullになるため先に取っておく

        string name = _seriesNameInput.text.Trim();
        if (string.IsNullOrEmpty(name))
        {
            SeriesStatus("シリーズ名を入力してください", C_ERR); return;
        }
        if (_seriesList.Any(s => s.name == name && (editing == null || s.series_id != editing.series_id)))
        {
            SeriesStatus($"「{name}」は登録済みです", C_ERR); return;
        }
        var effects = CollectEffects(_seriesEffectRows, out int bad);
        if (bad > 0)
        {
            SeriesStatus($"数値が不正な効果行が{bad}件あります", C_ERR); return;
        }
        if (effects.Count == 0)
        {
            SeriesStatus("セットスキルの効果を1つ以上追加してください", C_ERR); return;
        }

        _savingSeries = true;
        SeriesStatus("シリーズを保存中...", C_MUTED);
        try
        {
            var db = FirebaseFirestore.DefaultInstance;
            string id = editing != null ? editing.series_id : db.Collection("master").Document().Id;

            // セット画像（職業別）: 新しく選んだ職業だけStorageへアップロードする
            var imageUrls = new Dictionary<string, string>(); // 職業キー → URL（""=未設定/削除）
            for (int i = 0; i < CHAR_JOBS.Length; i++)
            {
                var slot = _seriesImgSlots[i];
                var (jobLabel, jobKey) = CHAR_JOBS[i];
                if (slot.bytes != null)
                {
                    SeriesStatus($"{jobLabel}のセット画像をアップロード中...", C_MUTED);
                    string ext = Path.GetExtension(slot.fileName ?? "").ToLower();
                    if (string.IsNullOrEmpty(ext)) ext = ".png";
                    string storagePath = $"series/{id}/{jobKey}_{DateTime.Now:yyyyMMdd_HHmmss}{ext}";
                    var fileRef = FirebaseStorage.DefaultInstance
                        .GetReferenceFromUrl(STORAGE_BASE).Child(storagePath);
                    var meta = new MetadataChange { ContentType = ext == ".png" ? "image/png" : "image/jpeg" };
                    await fileRef.PutBytesAsync(slot.bytes, meta).AsUniTask();
                    Uri uri = await fileRef.GetDownloadUrlAsync().AsUniTask();
                    imageUrls[jobKey] = uri.ToString();
                }
                else
                {
                    imageUrls[jobKey] = slot.cleared ? "" : (slot.existingUrl ?? "");
                }
            }
            SeriesStatus("シリーズを保存中...", C_MUTED);

            var data = new Dictionary<string, object>
            {
                { "name",    name },
                { "effects", effects },
                { "image_warrior",  imageUrls["warrior"] },
                { "image_magician", imageUrls["magician"] },
                { "image_archer",   imageUrls["archer"] },
                { "image_gunner",   imageUrls["gunner"] },
            };
            // 新規はcreated_at、編集は元のcreated_atを保持してupdated_atを付ける
            data[editing != null ? "updated_at" : "created_at"] = Timestamp.GetCurrentTimestamp();

            await db.Collection("master").Document("items")
                .SetAsync(new Dictionary<string, object>
                {
                    { "series", new Dictionary<string, object> { { id, data } } }
                }, SetOptions.MergeAll)
                .AsUniTask();

            // シリーズも items_version の差分同期に乗せる
            await db.Collection("master").Document("config")
                .UpdateAsync("items_version", FieldValue.Increment(1))
                .AsUniTask();

            var fxList = effects.Select(e => new LocalItemEffect
            {
                effectType = (string)e["effect_type"],
                value      = (int)e["value"],
            }).ToList();

            if (editing != null)
            {
                // ローカル一覧の同じインスタンスを書き換え（ピッカー表示にも反映される）
                editing.name = name;
                editing.effects = fxList;
                editing.image_warrior  = imageUrls["warrior"];
                editing.image_magician = imageUrls["magician"];
                editing.image_archer   = imageUrls["archer"];
                editing.image_gunner   = imageUrls["gunner"];
                if (_selectedSeries != null && _selectedSeries.series_id == id)
                    SelectSeries(editing); // 選択中表示の効果説明を最新化
                SS($"シリーズ「{name}」を更新しました（このシリーズが付いた全装備のセットスキルに反映）", C_OK);
                Debug.Log($"[EquipRegister] シリーズ更新 id={id} name={name}");
            }
            else
            {
                var cs = new CachedSeries
                {
                    series_id      = id,
                    name           = name,
                    effects        = fxList,
                    image_warrior  = imageUrls["warrior"],
                    image_magician = imageUrls["magician"],
                    image_archer   = imageUrls["archer"],
                    image_gunner   = imageUrls["gunner"],
                };
                _seriesList.Add(cs);
                SelectSeries(cs);
                SS($"シリーズ「{name}」を登録しました（この装備に設定済み）", C_OK);
                Debug.Log($"[EquipRegister] シリーズ登録 id={id} name={name}");
            }
            CloseSeriesModal();
        }
        catch (Exception ex)
        {
            SeriesStatus($"保存エラー: {ex.Message}", C_ERR);
            Debug.LogError($"[EquipRegister] シリーズ保存エラー: {ex}");
        }
        finally
        {
            _savingSeries = false;
        }
    }

    private void SeriesStatus(string msg, Color c)
    {
        if (_seriesStatusLabel != null) { _seriesStatusLabel.text = msg; _seriesStatusLabel.color = c; }
    }

    // ================================================================
    // UI 構築（スクロールフォーム）
    // ================================================================
    private void BuildUI()
    {
        var cGO = new GameObject("Canvas");
        var cv  = cGO.AddComponent<Canvas>();
        cv.renderMode = RenderMode.ScreenSpaceOverlay;
        var cs = cGO.AddComponent<CanvasScaler>();
        cs.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        cs.referenceResolution = new Vector2(1080, 1920);
        cGO.AddComponent<GraphicRaycaster>();
        _canvasTf = cGO.transform;
        var sys = new GameObject("EventSystem");
        sys.AddComponent<UnityEngine.EventSystems.EventSystem>();
        sys.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();

        // 背景
        var bg = Go("BG", _canvasTf); Stretch(bg);
        bg.AddComponent<Image>().color = C_BG;

        // ---- スクロールフォーム（下部はステータスバー用に空ける） ----
        var sv = Child("Scroll", _canvasTf, Vector2.zero, Vector2.one, new Vector2(0, 116f), Vector2.zero);
        var scroll = sv.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 40f;

        var vp = Child("Viewport", sv.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        vp.AddComponent<RectMask2D>();
        var vpImg = vp.AddComponent<Image>(); // ドラッグのレイキャスト受け（見えない）
        vpImg.color = new Color(0, 0, 0, 0.001f);

        var ct = Go("Content", vp.transform);
        var ctrt = ct.GetComponent<RectTransform>();
        ctrt.anchorMin = new Vector2(0, 1); ctrt.anchorMax = new Vector2(1, 1);
        ctrt.pivot = new Vector2(0.5f, 1f);
        ctrt.sizeDelta = Vector2.zero;        // デフォルト(100,100)のままだとビューポートより100px広くなり左右がはみ出す
        ctrt.anchoredPosition = Vector2.zero;
        var vlg = ct.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(40, 40, 24, 60);
        vlg.spacing = 14f;
        vlg.childControlWidth = true;  vlg.childForceExpandWidth = true;
        vlg.childControlHeight = true; vlg.childForceExpandHeight = false;
        ct.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.viewport = vp.GetComponent<RectTransform>();
        scroll.content = ctrt;
        _content = ct.transform;

        // ---- タイトル ----
        var titleRow = Row(100f, "Title");
        var tt = Label(titleRow.transform, "装備登録", 52, C_GOLD);

        // ---- 編集モード切替（既存装備の読み込み / 新規に戻す） ----
        var editRow = Row(90f, "EditRow");
        var editBtn = BoxButton(editRow.transform, "EditBtn", C_BTN_PICK, "既存の装備を編集 ▼", 32, C_TEXT,
            new Vector2(0f, 0f), new Vector2(0.64f, 1f), ShowItemPicker);
        _editBtnLabel = editBtn.GetComponentInChildren<TextMeshProUGUI>();
        BoxButton(editRow.transform, "NewBtn", C_BTN_GRAY, "新規モード", 32, C_TEXT,
            new Vector2(0.68f, 0f), new Vector2(1f, 1f), ResetToNewMode);

        // ---- 装備名 ----
        Section("装備名");
        _itemNameInput = InputRow("例: ブロンズソード");
        _itemNameInput.onValueChanged.AddListener(_ => RefreshUploadBtn());

        // ---- ゲーム名 ----
        Section("ゲーム名");
        _gameNameInput = InputRow("例: カタン、人狼");
        _gameNameInput.onValueChanged.AddListener(_ => RefreshUploadBtn());

        // ---- 説明 ----
        Section("説明文章");
        _descriptionInput = InputRow("例: 攻撃力が上がる装備", multiline: true, charLimit: 200);

        // ---- 職業選択 ----
        Section("装備可能職業");
        _jobBtns = MakeSelectGrid(JOBS, 5, 1, SelectJob);

        // ---- 部位選択 ----
        Section("装備部位");
        _catBtns = MakeSelectGrid(CATS, 3, 2, SelectCat);

        // ---- アイコン画像 ----
        Section("アイコン画像");
        var pathRow = Row(90f, "PathRow");
        var pathIn = Child("PathIn", pathRow.transform, new Vector2(0, 0), new Vector2(0.72f, 1f), Vector2.zero, new Vector2(-8f, 0));
        pathIn.AddComponent<Image>().color = C_PANEL;
        var pathTF = pathIn.AddComponent<TMP_InputField>();
        pathTF.characterLimit = 500;
        pathTF.onEndEdit.AddListener(OnPathChanged);
        pathTF.onValueChanged.AddListener(_ => RefreshUploadBtn());
        _filePathInput = pathTF;
        MakeInputInternal(pathTF, pathIn, "画像ファイルのパス...");
        BoxButton(pathRow.transform, "Browse", C_BTN_PICK, "選択", 38, C_TEXT,
            new Vector2(0.74f, 0f), new Vector2(1f, 1f), OnPickFile);

        // プレビュー
        var prevRow = Row(240f, "PrevRow");
        var prevBG = Go("PrevBG", prevRow.transform);
        var pvRT = prevBG.GetComponent<RectTransform>();
        pvRT.anchorMin = pvRT.anchorMax = new Vector2(0.5f, 0.5f);
        pvRT.sizeDelta = new Vector2(230f, 230f);
        prevBG.AddComponent<Image>().color = C_PANEL;
        var pGO = Child("Prev", prevBG.transform, new Vector2(0.06f, 0.06f), new Vector2(0.94f, 0.94f), Vector2.zero, Vector2.zero);
        _previewImage = pGO.AddComponent<RawImage>();
        _previewImage.color = new Color(0.15f, 0.12f, 0.10f);

        // ---- 装備効果 ----
        Section("装備効果（0件でも可）");
        _itemEffectsContainer = MakeEffectListContainer(_content);
        var addFxRow = Row(80f, "AddFx");
        BoxButton(addFxRow.transform, "AddBtn", C_BTN_ADD, "＋ 効果を追加", 34, C_TEXT,
            new Vector2(0.15f, 0f), new Vector2(0.85f, 1f),
            () => AddEffectRow(_itemEffectsContainer, _itemEffectRows));

        // ---- シリーズ ----
        Section("シリーズ（武器・頭・体・足を揃えるとセットスキル発動）");
        var seriesRow = Row(96f, "SeriesRow");
        var sBtn = BoxButton(seriesRow.transform, "SeriesBtn", C_PANEL, "なし ▼", 34, C_TEXT,
            new Vector2(0f, 0f), new Vector2(0.66f, 1f), ShowSeriesPicker);
        _seriesBtnLabel = sBtn.GetComponentInChildren<TextMeshProUGUI>();
        BoxButton(seriesRow.transform, "SeriesEditBtn", C_BTN_PICK, "シリーズ編集", 30, C_TEXT,
            new Vector2(0.70f, 0f), new Vector2(1f, 1f), ShowSeriesEditPicker);

        // ---- 通常ガチャ排出 ----
        Section("通常ガチャ");
        var gachaRow = Row(96f, "GachaRow");
        var gBtn = BoxButton(gachaRow.transform, "GachaToggle", C_BTN_GRAY, "ガチャから出ない: OFF", 32, C_TEXT,
            new Vector2(0f, 0f), new Vector2(0.56f, 1f), ToggleGacha);
        _gachaToggleImage = gBtn.GetComponent<Image>();
        _gachaToggleLabel = gBtn.GetComponentInChildren<TextMeshProUGUI>();

        _gachaWeightRoot = Child("WeightRoot", gachaRow.transform, new Vector2(0.58f, 0f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
        var wLabel = Child("WL", _gachaWeightRoot.transform, new Vector2(0f, 0f), new Vector2(0.4f, 1f), Vector2.zero, Vector2.zero);
        var wTxt = wLabel.AddComponent<TextMeshProUGUI>();
        wTxt.text = "重み"; wTxt.fontSize = 30; wTxt.color = C_MUTED;
        wTxt.alignment = TextAlignmentOptions.Center; wTxt.raycastTarget = false;
        ApplyFont(wTxt);
        var wIn = Child("WIn", _gachaWeightRoot.transform, new Vector2(0.42f, 0f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
        wIn.AddComponent<Image>().color = C_PANEL;
        _gachaWeightInput = wIn.AddComponent<TMP_InputField>();
        _gachaWeightInput.characterLimit = 5;
        _gachaWeightInput.contentType = TMP_InputField.ContentType.IntegerNumber;
        MakeInputInternal(_gachaWeightInput, wIn, "10");
        _gachaWeightInput.text = DEFAULT_GACHA_WEIGHT.ToString();
        _gachaWeightRoot.SetActive(false);

        // ---- 登録ボタン ----
        var upRow = Row(140f, "UploadRow");
        var upBtn = BoxButton(upRow.transform, "UploadBtn", C_BTN_UPLOAD, "装備を登録する", 44, C_TEXT,
            new Vector2(0.08f, 0f), new Vector2(0.92f, 1f), () => UploadAndSave().Forget());
        _uploadBtn = upBtn;
        _uploadBtnLabel = upBtn.GetComponentInChildren<TextMeshProUGUI>();
        RefreshUploadBtn();

        // ---- ステータスバー（固定・スクロール外） ----
        var statusBar = Child("StatusBar", _canvasTf, new Vector2(0, 0), new Vector2(1, 0), Vector2.zero, new Vector2(0, 0));
        var sbRT = statusBar.GetComponent<RectTransform>();
        sbRT.pivot = new Vector2(0.5f, 0f);
        sbRT.sizeDelta = new Vector2(0, 110f);
        statusBar.AddComponent<Image>().color = new Color(0.06f, 0.05f, 0.04f, 0.98f);
        var stGO = Child("Status", statusBar.transform, Vector2.zero, Vector2.one, new Vector2(24f, 6f), new Vector2(-24f, -6f));
        _statusLabel = stGO.AddComponent<TextMeshProUGUI>();
        _statusLabel.text = "読み込み中...";
        _statusLabel.fontSize = 28;
        _statusLabel.alignment = TextAlignmentOptions.Midline;
        _statusLabel.color = C_MUTED;
        ApplyFont(_statusLabel);

        SelectJob(0);
        SelectCat(0);
    }

    // ================================================================
    // 効果入力行
    // ================================================================
    private Transform MakeEffectListContainer(Transform parent)
    {
        var go = Go("EffectList", parent);
        var vlg = go.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 10f;
        vlg.childControlWidth = true;  vlg.childForceExpandWidth = true;
        vlg.childControlHeight = true; vlg.childForceExpandHeight = false;
        return go.transform;
    }

    private EffectRowUI AddEffectRow(Transform container, List<EffectRowUI> list)
    {
        var ui = new EffectRowUI();
        var row = Go("EffectRow", container);
        var le = row.AddComponent<LayoutElement>();
        le.preferredHeight = 92f; le.minHeight = 92f;
        row.AddComponent<Image>().color = C_ROW;

        // 効果タイプ（タップでピッカー）
        var typeBtn = BoxButton(row.transform, "TypeBtn", C_PANEL, EffectLabel(ui.type) + " ▼", 28, C_TEXT,
            new Vector2(0f, 0f), new Vector2(0.54f, 1f), () => ShowEffectTypePicker(ui),
            new Vector2(8f, 8f), new Vector2(-4f, -8f));
        ui.typeLabel = typeBtn.GetComponentInChildren<TextMeshProUGUI>();

        // 値
        var valGO = Child("Val", row.transform, new Vector2(0.56f, 0f), new Vector2(0.80f, 1f), new Vector2(0f, 8f), new Vector2(0f, -8f));
        valGO.AddComponent<Image>().color = C_PANEL;
        ui.valueInput = valGO.AddComponent<TMP_InputField>();
        ui.valueInput.characterLimit = 5;
        ui.valueInput.contentType = TMP_InputField.ContentType.IntegerNumber;
        MakeInputInternal(ui.valueInput, valGO, "値");

        // 削除
        BoxButton(row.transform, "Del", C_BTN_DEL, "削除", 28, C_TEXT,
            new Vector2(0.82f, 0f), new Vector2(1f, 1f),
            () => { list.Remove(ui); Destroy(row); },
            new Vector2(4f, 8f), new Vector2(-8f, -8f));

        ui.go = row;
        list.Add(ui);
        return ui;
    }

    private void ClearEffectRows(List<EffectRowUI> list)
    {
        foreach (var r in list)
            if (r?.go != null) Destroy(r.go);
        list.Clear();
    }

    private static string EffectLabel(EffectType type)
        => EFFECT_CHOICES.FirstOrDefault(c => c.type == type).label ?? type.ToString();

    private void ShowEffectTypePicker(EffectRowUI ui)
    {
        var opts = new List<(string, Action)>();
        foreach (var (type, label) in EFFECT_CHOICES)
        {
            var capType = type; var capLabel = label;
            opts.Add((label, () =>
            {
                ui.type = capType;
                if (ui.typeLabel != null) ui.typeLabel.text = capLabel + " ▼";
            }));
        }
        ShowPicker("効果を選択", opts);
    }

    // ================================================================
    // シリーズ選択・作成
    // ================================================================
    private void ShowSeriesPicker()
    {
        var opts = new List<(string, Action)> { ("なし", () => SelectSeries(null)) };
        foreach (var s in _seriesList)
        {
            var cap = s;
            opts.Add(($"{s.name}｜{SeriesSetBonus.DescribeEffects(s.effects)}", () => SelectSeries(cap)));
        }
        opts.Add(("＋ 新規シリーズを作成", () => ShowSeriesModal(null)));
        ShowPicker("シリーズを選択", opts);
    }

    /// <summary>編集するシリーズを選ぶピッカー。選んだシリーズを編集モーダルで開く。</summary>
    private void ShowSeriesEditPicker()
    {
        if (_seriesList.Count == 0)
        {
            SS("編集できるシリーズがありません（読み込み中の場合は少し待ってください）", C_MUTED);
            return;
        }
        var opts = new List<(string, Action)>();
        foreach (var s in _seriesList)
        {
            var cap = s;
            opts.Add(($"{s.name}｜{SeriesSetBonus.DescribeEffects(s.effects)}", () => ShowSeriesModal(cap)));
        }
        ShowPicker("編集するシリーズを選択", opts);
    }

    private void SelectSeries(CachedSeries s)
    {
        _selectedSeries = s;
        if (_seriesBtnLabel != null)
            _seriesBtnLabel.text = s == null
                ? "なし ▼"
                : $"{s.name}（{SeriesSetBonus.DescribeEffects(s.effects)}） ▼";
    }

    /// <summary>モーダルで編集中のシリーズ（null=新規作成モード）。</summary>
    private CachedSeries _editingSeries;

    /// <summary>シリーズの新規作成／編集モーダル（editing=nullで新規）。</summary>
    private void ShowSeriesModal(CachedSeries editing)
    {
        CloseSeriesModal();
        _editingSeries = editing;
        _seriesEffectRows.Clear();

        _seriesModal = Go("__SeriesModal", _canvasTf);
        Stretch(_seriesModal);
        _seriesModal.AddComponent<Image>().color = new Color(0, 0, 0, 0.75f);
        var dimBtn = _seriesModal.AddComponent<Button>();
        dimBtn.transition = Selectable.Transition.None; // 誤タップで入力を失わないよう外側タップでは閉じない
        _seriesModal.transform.SetAsLastSibling();

        // パネル（子の合計高さに合わせて自動伸縮）
        var panel = Go("Panel", _seriesModal.transform);
        var prt = panel.GetComponent<RectTransform>();
        prt.sizeDelta = new Vector2(960f, 0f);
        panel.AddComponent<Image>().color = C_BG;
        var pvlg = panel.AddComponent<VerticalLayoutGroup>();
        pvlg.padding = new RectOffset(30, 30, 24, 24);
        pvlg.spacing = 14f;
        pvlg.childControlWidth = true;  pvlg.childForceExpandWidth = true;
        pvlg.childControlHeight = true; pvlg.childForceExpandHeight = false;
        panel.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var pBtn = panel.AddComponent<Button>(); // パネル内タップを吸収
        pBtn.transition = Selectable.Transition.None;

        var pt = panel.transform;
        var titleRow = Row(90f, "Title", pt);
        Label(titleRow.transform, editing == null ? "新規シリーズ登録" : "シリーズ編集", 44, C_GOLD);

        Section("シリーズ名", pt);
        _seriesNameInput = InputRow("例: ドラゴンナイト", parent: pt);
        if (editing != null) _seriesNameInput.text = editing.name;

        Section("セットスキル効果（1つ以上）", pt);
        _seriesEffectsContainer = MakeEffectListContainer(pt);
        if (editing != null && editing.effects != null && editing.effects.Count > 0)
        {
            // 既存の効果をプリフィル
            foreach (var fx in editing.effects)
            {
                if (fx == null || !Enum.TryParse(fx.effectType, out EffectType type)) continue;
                var ui = AddEffectRow(_seriesEffectsContainer, _seriesEffectRows);
                ui.type = type;
                if (ui.typeLabel != null) ui.typeLabel.text = EffectLabel(type) + " ▼";
                ui.valueInput.text = fx.value.ToString();
            }
        }
        if (_seriesEffectRows.Count == 0)
            AddEffectRow(_seriesEffectsContainer, _seriesEffectRows);

        var addRow = Row(80f, "Add", pt);
        BoxButton(addRow.transform, "AddBtn", C_BTN_ADD, "＋ 効果を追加", 32, C_TEXT,
            new Vector2(0.15f, 0f), new Vector2(0.85f, 1f),
            () => AddEffectRow(_seriesEffectsContainer, _seriesEffectRows));

        // ---- セット発動中のキャラ画像（職業別・任意） ----
        Section("セット発動中のキャラ画像（職業別・任意）", pt);
        _seriesImgSlots = CHAR_JOBS
            .Select(j => new SeriesImageSlot { existingUrl = editing != null ? editing.GetImageUrl(j.key) : "" })
            .ToArray();

        var tabRow = Row(84f, "ImgTabs", pt);
        _seriesImgTabs = new GameObject[CHAR_JOBS.Length];
        for (int i = 0; i < CHAR_JOBS.Length; i++)
        {
            int cap = i;
            var b = BoxButton(tabRow.transform, $"ImgTab{i}", C_PANEL, CHAR_JOBS[i].label, 28, C_TEXT,
                new Vector2(i / (float)CHAR_JOBS.Length, 0f), new Vector2((i + 1f) / CHAR_JOBS.Length, 1f),
                () => SelectSeriesImgJob(cap), new Vector2(4f, 4f), new Vector2(-4f, -4f));
            _seriesImgTabs[i] = b.gameObject;
        }

        var imgRow = Row(180f, "ImgRow", pt);
        // プレビュー（左・固定幅）
        var pvBg = Child("PvBg", imgRow.transform, new Vector2(0f, 0f), new Vector2(0f, 1f),
            new Vector2(0f, 5f), new Vector2(170f, -5f));
        pvBg.AddComponent<Image>().color = C_ROW;
        var pvGO = Child("Pv", pvBg.transform, new Vector2(0.05f, 0.05f), new Vector2(0.95f, 0.95f), Vector2.zero, Vector2.zero);
        _seriesImgPreview = pvGO.AddComponent<RawImage>();
        _seriesImgPreview.color = new Color(0.15f, 0.12f, 0.10f);
        // 状態表示（右上）
        var imStGO = Child("ImSt", imgRow.transform, new Vector2(0.20f, 0.55f), new Vector2(1f, 1f),
            new Vector2(8f, 0f), new Vector2(-4f, 0f));
        _seriesImgStateLabel = imStGO.AddComponent<TextMeshProUGUI>();
        _seriesImgStateLabel.fontSize = 26;
        _seriesImgStateLabel.alignment = TextAlignmentOptions.MidlineLeft;
        _seriesImgStateLabel.color = C_MUTED;
        _seriesImgStateLabel.raycastTarget = false;
        ApplyFont(_seriesImgStateLabel);
        // 操作ボタン（右下）
        BoxButton(imgRow.transform, "ImgPick", C_BTN_PICK, "画像を選択", 28, C_TEXT,
            new Vector2(0.20f, 0f), new Vector2(0.58f, 0.48f), OnPickSeriesImage);
        BoxButton(imgRow.transform, "ImgClear", C_BTN_GRAY, "クリア", 28, C_TEXT,
            new Vector2(0.62f, 0f), new Vector2(0.96f, 0.48f), OnClearSeriesImage);

        SelectSeriesImgJob(0);

        var stRow = Row(60f, "Status", pt);
        _seriesStatusLabel = Label(stRow.transform, "武器・頭・体・足の4部位を揃えると発動します", 26, C_MUTED);

        var btnRow = Row(120f, "Btns", pt);
        BoxButton(btnRow.transform, "Save", C_BTN_UPLOAD, editing == null ? "シリーズを保存" : "シリーズを更新", 38, C_TEXT,
            new Vector2(0f, 0f), new Vector2(0.48f, 1f), () => SaveSeriesAsync().Forget());
        BoxButton(btnRow.transform, "Cancel", C_BTN_GRAY, "キャンセル", 38, C_TEXT,
            new Vector2(0.52f, 0f), new Vector2(1f, 1f), CloseSeriesModal);
    }

    private void CloseSeriesModal()
    {
        if (_seriesModal != null) { Destroy(_seriesModal); _seriesModal = null; }
        _seriesEffectRows.Clear();
        _editingSeries = null;
        if (_seriesImgSlots != null)
        {
            foreach (var s in _seriesImgSlots)
                if (s?.tex != null) Destroy(s.tex);
            _seriesImgSlots = null;
        }
    }

    // ================================================================
    // シリーズのセット画像（職業タブ・選択・プレビュー）
    // ================================================================
    private void SelectSeriesImgJob(int idx)
    {
        _seriesImgJobIdx = idx;
        for (int i = 0; i < _seriesImgTabs.Length; i++)
        {
            bool sel = i == idx;
            _seriesImgTabs[i].GetComponent<Image>().color = sel ? new Color(.38f, .28f, .60f) : C_PANEL;
            var lbl = _seriesImgTabs[i].GetComponentInChildren<TextMeshProUGUI>();
            if (lbl) lbl.color = sel ? C_GOLD : C_TEXT;
        }
        RefreshSeriesImgPreview();
    }

    private void RefreshSeriesImgPreview()
    {
        if (_seriesImgSlots == null || _seriesImgPreview == null) return;
        var slot = _seriesImgSlots[_seriesImgJobIdx];

        if (slot.tex != null)
        {
            _seriesImgPreview.texture = slot.tex;
            _seriesImgPreview.color = Color.white;
        }
        else
        {
            _seriesImgPreview.texture = null;
            _seriesImgPreview.color = new Color(0.15f, 0.12f, 0.10f);
            // 既存画像があればプレビュー用にDLする
            if (!slot.cleared && !string.IsNullOrEmpty(slot.existingUrl))
                LoadSeriesImgPreviewAsync(slot, _seriesImgJobIdx).Forget();
        }

        _seriesImgStateLabel.text =
              slot.bytes != null                        ? "新しい画像を選択済み"
            : slot.cleared                              ? "クリア（保存すると削除されます）"
            : !string.IsNullOrEmpty(slot.existingUrl)   ? "設定済み（変更する場合のみ選択）"
            :                                             "未設定（デフォルトのシルエットのまま）";
    }

    private async UniTaskVoid LoadSeriesImgPreviewAsync(SeriesImageSlot slot, int jobIdx)
    {
        try
        {
            using var req = UnityWebRequestTexture.GetTexture(slot.existingUrl);
            await req.SendWebRequest();
            // モーダルが閉じた／別の画像を選んだ／クリアされた場合は反映しない
            if (_seriesModal == null || _seriesImgSlots == null) return;
            if (slot.bytes != null || slot.cleared) return;
            if (req.result != UnityWebRequest.Result.Success) return;
            slot.tex = DownloadHandlerTexture.GetContent(req);
            if (_seriesImgJobIdx == jobIdx) RefreshSeriesImgPreview();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[EquipRegister] セット画像プレビューDL失敗: {ex.Message}");
        }
    }

    private void OnPickSeriesImage()
    {
#if UNITY_EDITOR
        string path = UnityEditor.EditorUtility.OpenFilePanel("セット画像を選択", "", "png,jpg,jpeg");
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            var slot = _seriesImgSlots[_seriesImgJobIdx];
            slot.bytes = File.ReadAllBytes(path);
            slot.fileName = Path.GetFileName(path);
            slot.cleared = false;
            if (slot.tex != null) Destroy(slot.tex);
            slot.tex = new Texture2D(2, 2);
            slot.tex.LoadImage(slot.bytes);
            RefreshSeriesImgPreview();
        }
        catch (Exception ex)
        {
            SeriesStatus($"画像読み込みエラー: {ex.Message}", C_ERR);
        }
#else
        SeriesStatus("画像選択はエディタでのみ使えます", C_MUTED);
#endif
    }

    private void OnClearSeriesImage()
    {
        var slot = _seriesImgSlots[_seriesImgJobIdx];
        slot.bytes = null;
        slot.fileName = null;
        if (slot.tex != null) Destroy(slot.tex);
        slot.tex = null;
        slot.cleared = true;
        RefreshSeriesImgPreview();
    }

    // ================================================================
    // ガチャトグル
    // ================================================================
    private void ToggleGacha() => SetGachaOn(!_gachaOn);

    private void SetGachaOn(bool on)
    {
        _gachaOn = on;
        if (_gachaToggleImage != null) _gachaToggleImage.color = on ? C_TOGGLE_ON : C_BTN_GRAY;
        if (_gachaToggleLabel != null) _gachaToggleLabel.text = on ? "ガチャから出る: ON" : "ガチャから出ない: OFF";
        if (_gachaWeightRoot != null) _gachaWeightRoot.SetActive(on);
    }

    // ================================================================
    // 既存装備の編集
    // ================================================================
    private static string JobLabel(string key)
        => JOBS.FirstOrDefault(j => j.key == key).label ?? key;

    /// <summary>部位キー→表示名（旧Firebase登録値 foot/skillA/skillB も吸収）。</summary>
    private static string CatLabel(string key)
        => CATS.FirstOrDefault(c => c.key == NormalizeCatKey(key)).label ?? key;

    private static string NormalizeCatKey(string key) => key switch
    {
        "foot"   => "feet",
        "skillA" => "skill_book_a",
        "skillB" => "skill_book_b",
        _        => key,
    };

    /// <summary>編集ピッカーの職業フィルター（null=全て。セッション中は選択を保持）。</summary>
    private string _editFilterJob;

    /// <summary>編集ピッカーの部位フィルター（正規化済みキー。null=全て。セッション中は選択を保持）。</summary>
    private string _editFilterCat;

    private void ShowItemPicker()
    {
        if (_allItems.Count == 0)
        {
            SS("編集できる装備がありません（読み込み中の場合は少し待ってください）", C_MUTED);
            return;
        }

        var opts = new List<(string, Action)>();
        foreach (var (id, fields) in _allItems)
        {
            if (_editFilterJob != null && GetStr(fields, "job") != _editFilterJob) continue;
            if (_editFilterCat != null && NormalizeCatKey(GetStr(fields, "slot_type")) != _editFilterCat) continue;
            var capId = id; var capFields = fields;
            string label = $"{GetStr(fields, "name")}｜{JobLabel(GetStr(fields, "job"))}・{CatLabel(GetStr(fields, "slot_type"))}｜{GetStr(fields, "game")}";
            opts.Add((label, () => LoadItemForEdit(capId, capFields)));
        }
        if (opts.Count == 0)
            opts.Add(("（条件に合う装備はありません）", () => {}));

        // 職業フィルタータブ（タップでフィルターを切り替えてピッカーを開き直す）
        var tabs = new List<(string label, bool selected, Action onTap)>
        {
            ("全て", _editFilterJob == null, () => { _editFilterJob = null; ShowItemPicker(); }),
        };
        foreach (var (label, key) in JOBS)
        {
            var capKey = key;
            tabs.Add((label, _editFilterJob == key, () => { _editFilterJob = capKey; ShowItemPicker(); }));
        }

        // 部位フィルタータブ（職業タブの下の2段目）
        var catTabs = new List<(string label, bool selected, Action onTap)>
        {
            ("全て", _editFilterCat == null, () => { _editFilterCat = null; ShowItemPicker(); }),
        };
        foreach (var (label, key) in CATS)
        {
            var capKey = key;
            catTabs.Add((label, _editFilterCat == key, () => { _editFilterCat = capKey; ShowItemPicker(); }));
        }

        ShowPicker("編集する装備を選択", opts, tabs, catTabs);
    }

    /// <summary>既存装備をフォームへ読み込み、編集モードに切り替える。</summary>
    private void LoadItemForEdit(string itemId, Dictionary<string, object> fields)
    {
        _editingItemId = itemId;
        _editingFields = fields;

        string name = GetStr(fields, "name");
        _itemNameInput.text    = name;
        _gameNameInput.text    = GetStr(fields, "game");
        _descriptionInput.text = GetStr(fields, "description");

        int jobIdx = Array.FindIndex(JOBS, j => j.key == GetStr(fields, "job"));
        SelectJob(jobIdx >= 0 ? jobIdx : 0);
        int catIdx = Array.FindIndex(CATS, c => c.key == NormalizeCatKey(GetStr(fields, "slot_type")));
        SelectCat(catIdx >= 0 ? catIdx : 0);

        // 効果
        ClearEffectRows(_itemEffectRows);
        if (fields.TryGetValue("effects", out var fxObj) && fxObj is IEnumerable<object> fxList)
        {
            foreach (var fx in fxList)
            {
                if (!(fx is Dictionary<string, object> m)) continue;
                if (!Enum.TryParse(GetStr(m, "effect_type"), out EffectType type)) continue;
                var ui = AddEffectRow(_itemEffectsContainer, _itemEffectRows);
                ui.type = type;
                if (ui.typeLabel != null) ui.typeLabel.text = EffectLabel(type) + " ▼";
                ui.valueInput.text = (m.TryGetValue("value", out var v) ? Convert.ToInt32(v) : 0).ToString();
            }
        }

        // シリーズ
        SelectSeries(_seriesList.FirstOrDefault(s => s.series_id == GetStr(fields, "series")));

        // アイコン（差し替えるまでは既存のまま。プレビューは既存URLからDL表示）
        _selectedBytes = null;
        _selectedFileName = null;
        _filePathInput.text = "";
        _previewImage.texture = null;
        _previewImage.color = new Color(0.15f, 0.12f, 0.10f);
        LoadIconPreviewAsync(GetStr(fields, "icon_url")).Forget();

        // 通常ガチャ（現在の排出テーブルからプリフィル）
        var entry = FindGachaEntry(itemId);
        SetGachaOn(entry != null);
        _gachaWeightInput.text = entry != null && entry.TryGetValue("weight", out var w)
            ? Convert.ToInt32(w).ToString()
            : DEFAULT_GACHA_WEIGHT.ToString();

        if (_editBtnLabel != null) _editBtnLabel.text = $"編集中: {name} ▼";
        if (_uploadBtnLabel != null) _uploadBtnLabel.text = "装備を更新する";
        SS($"「{name}」を編集中（アイコンは選び直した時だけ差し替わります）", C_MUTED);
        RefreshUploadBtn();
    }

    /// <summary>新規登録モードへ戻す（フォームを全クリア）。</summary>
    private void ResetToNewMode()
    {
        _editingItemId = null;
        _editingFields = null;

        _itemNameInput.text = "";
        _gameNameInput.text = "";
        _descriptionInput.text = "";
        SelectJob(0);
        SelectCat(0);
        ClearEffectRows(_itemEffectRows);
        SelectSeries(null);

        _selectedBytes = null;
        _selectedFileName = null;
        _filePathInput.text = "";
        _previewImage.texture = null;
        _previewImage.color = new Color(0.15f, 0.12f, 0.10f);

        SetGachaOn(false);
        _gachaWeightInput.text = DEFAULT_GACHA_WEIGHT.ToString();

        if (_editBtnLabel != null) _editBtnLabel.text = "既存の装備を編集 ▼";
        if (_uploadBtnLabel != null) _uploadBtnLabel.text = "装備を登録する";
        SS("新規登録モードです。装備情報を入力してください", C_MUTED);
        RefreshUploadBtn();
    }

    /// <summary>既存アイコンをURLからダウンロードしてプレビューに表示する（編集モード用）。</summary>
    private async UniTaskVoid LoadIconPreviewAsync(string url)
    {
        if (string.IsNullOrEmpty(url)) return;
        string editingId = _editingItemId;
        try
        {
            using var req = UnityWebRequestTexture.GetTexture(url);
            await req.SendWebRequest();
            // DL中に別のアイテムへ切り替え/新規モードに戻った場合は反映しない
            if (_editingItemId != editingId) return;
            // DL中にユーザーが新しい画像を選んでいたら上書きしない
            if (_selectedBytes != null) return;
            if (req.result == UnityWebRequest.Result.Success)
            {
                if (_selectedTex != null) Destroy(_selectedTex);
                _selectedTex = DownloadHandlerTexture.GetContent(req);
                _previewImage.texture = _selectedTex;
                _previewImage.color = Color.white;
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[EquipRegister] アイコンプレビューDL失敗: {ex.Message}");
        }
    }

    // ================================================================
    // 汎用ピッカー（全画面リスト。filterTabs付きならタイトル下にタブ行を出す。
    // filterTabs2 を渡すと2段目のタブ行を追加する（例: 職業×部位の絞り込み））
    // ================================================================
    private void ShowPicker(string title, List<(string label, Action onPick)> options,
        List<(string label, bool selected, Action onTap)> filterTabs = null,
        List<(string label, bool selected, Action onTap)> filterTabs2 = null)
    {
        ClosePicker();
        _pickerOverlay = Go("__Picker", _canvasTf);
        Stretch(_pickerOverlay);
        _pickerOverlay.AddComponent<Image>().color = new Color(0, 0, 0, 0.75f);
        var dimBtn = _pickerOverlay.AddComponent<Button>();
        dimBtn.transition = Selectable.Transition.None;
        dimBtn.onClick.AddListener(ClosePicker);
        _pickerOverlay.transform.SetAsLastSibling();

        float tabsH = (filterTabs != null ? 100f : 0f) + (filterTabs2 != null ? 100f : 0f);
        float panelH = Mathf.Min(1500f, 140f + tabsH + options.Count * 108f + 30f);
        var panel = Go("Panel", _pickerOverlay.transform);
        var prt = panel.GetComponent<RectTransform>();
        prt.sizeDelta = new Vector2(950f, panelH);
        panel.AddComponent<Image>().color = C_BG;
        var pBtn = panel.AddComponent<Button>(); // パネル内タップを吸収
        pBtn.transition = Selectable.Transition.None;

        var titleGO = Child("T", panel.transform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -110f), Vector2.zero);
        var tTxt = titleGO.AddComponent<TextMeshProUGUI>();
        tTxt.text = title; tTxt.fontSize = 40; tTxt.fontStyle = FontStyles.Bold;
        tTxt.alignment = TextAlignmentOptions.Center; tTxt.color = C_GOLD; tTxt.raycastTarget = false;
        ApplyFont(tTxt);

        // フィルタータブ行（onTap側でピッカーを開き直す想定なのでここでは閉じない）
        void BuildTabRow(string rowName, float top, List<(string label, bool selected, Action onTap)> rowTabs)
        {
            var tabRow = Child(rowName, panel.transform, new Vector2(0, 1), new Vector2(1, 1),
                new Vector2(12f, top - 96f), new Vector2(-12f, top));
            int n = rowTabs.Count;
            for (int i = 0; i < n; i++)
            {
                var (label, selected, onTap) = rowTabs[i];
                var capTap = onTap;
                BoxButton(tabRow.transform, $"{rowName}{i}",
                    selected ? new Color(.38f, .28f, .60f) : C_PANEL,
                    label, 26, selected ? C_GOLD : C_TEXT,
                    new Vector2(i / (float)n, 0f), new Vector2((i + 1f) / n, 1f),
                    () => capTap(),
                    new Vector2(4f, 4f), new Vector2(-4f, -4f));
            }
        }
        if (filterTabs != null)
            BuildTabRow("Tabs", -110f, filterTabs);
        if (filterTabs2 != null)
            BuildTabRow("Tabs2", filterTabs != null ? -210f : -110f, filterTabs2);

        // 選択肢リスト（多くなってもスクロールできる）
        var sv = Child("SV", panel.transform, Vector2.zero, Vector2.one, new Vector2(12f, 12f), new Vector2(-12f, -(116f + tabsH)));
        var scroll = sv.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 40f;
        var vp = Child("VP", sv.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        vp.AddComponent<RectMask2D>();
        var vpImg = vp.AddComponent<Image>();
        vpImg.color = new Color(0, 0, 0, 0.001f);
        var ct = Go("CT", vp.transform);
        var ctrt = ct.GetComponent<RectTransform>();
        ctrt.anchorMin = new Vector2(0, 1); ctrt.anchorMax = new Vector2(1, 1);
        ctrt.pivot = new Vector2(0.5f, 1f);
        ctrt.sizeDelta = Vector2.zero;        // デフォルト(100,100)のままだと選択肢の左右がビューポート外にはみ出す
        ctrt.anchoredPosition = Vector2.zero;
        var vlg = ct.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 10f;
        vlg.childControlWidth = true;  vlg.childForceExpandWidth = true;
        vlg.childControlHeight = true; vlg.childForceExpandHeight = false;
        ct.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.viewport = vp.GetComponent<RectTransform>();
        scroll.content = ctrt;

        foreach (var (label, onPick) in options)
        {
            var capPick = onPick;
            var row = Go("Opt", ct.transform);
            var le = row.AddComponent<LayoutElement>();
            le.preferredHeight = 98f; le.minHeight = 98f;
            row.AddComponent<Image>().color = C_PANEL;
            var b = row.AddComponent<Button>();
            b.navigation = new Navigation { mode = Navigation.Mode.None };
            b.onClick.AddListener(() => { ClosePicker(); capPick(); });
            var lGO = Child("L", row.transform, Vector2.zero, Vector2.one, new Vector2(24f, 0f), new Vector2(-24f, 0f));
            var lTxt = lGO.AddComponent<TextMeshProUGUI>();
            lTxt.text = label; lTxt.fontSize = 30;
            lTxt.alignment = TextAlignmentOptions.MidlineLeft;
            lTxt.color = C_TEXT; lTxt.raycastTarget = false;
            lTxt.overflowMode = TextOverflowModes.Ellipsis;
            lTxt.enableWordWrapping = false;
            ApplyFont(lTxt);
        }
    }

    private void ClosePicker()
    {
        if (_pickerOverlay != null) { Destroy(_pickerOverlay); _pickerOverlay = null; }
    }

    // ================================================================
    // 選択状態（職業・部位）
    // ================================================================
    private void SelectJob(int idx)
    {
        _selectedJob = idx;
        for (int i = 0; i < _jobBtns.Length; i++)
        {
            bool sel = (i == idx);
            _jobBtns[i].GetComponent<Image>().color = sel
                ? new Color(.38f, .28f, .60f) : C_PANEL;
            var lbl = _jobBtns[i].GetComponentInChildren<TextMeshProUGUI>();
            if (lbl) lbl.color = sel ? C_GOLD : C_TEXT;
        }
    }

    private void SelectCat(int idx)
    {
        _selectedCat = idx;
        for (int i = 0; i < _catBtns.Length; i++)
        {
            bool sel = (i == idx);
            _catBtns[i].GetComponent<Image>().color = sel
                ? new Color(.45f, .28f, .10f) : C_PANEL;
            var lbl = _catBtns[i].GetComponentInChildren<TextMeshProUGUI>();
            if (lbl) lbl.color = sel ? C_GOLD : C_TEXT;
        }
    }

    // ================================================================
    // UI ヘルパー
    // ================================================================
    private GameObject Go(string n, Transform p)
    {
        var go = new GameObject(n);
        go.transform.SetParent(p, false);
        go.AddComponent<RectTransform>();
        return go;
    }

    private void Stretch(GameObject go)
    {
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    /// <summary>アンカー指定の子GameObjectを作る。</summary>
    private GameObject Child(string n, Transform p, Vector2 aMin, Vector2 aMax, Vector2 offMin, Vector2 offMax)
    {
        var go = Go(n, p);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = aMin; rt.anchorMax = aMax;
        rt.offsetMin = offMin; rt.offsetMax = offMax;
        return go;
    }

    /// <summary>スクロールフォームの1行（固定高さ）。parent省略時はメインフォーム。</summary>
    private GameObject Row(float h, string n, Transform parent = null)
    {
        var go = Go(n, parent != null ? parent : _content);
        var le = go.AddComponent<LayoutElement>();
        le.preferredHeight = h; le.minHeight = h;
        return go;
    }

    /// <summary>セクション見出しの行。</summary>
    private void Section(string text, Transform parent = null)
    {
        var row = Row(52f, "SL_" + text, parent);
        var t = row.AddComponent<TextMeshProUGUI>();
        t.text = text; t.fontSize = 32; t.fontStyle = FontStyles.Bold;
        t.color = C_MUTED; t.alignment = TextAlignmentOptions.MidlineLeft;
        t.raycastTarget = false;
        ApplyFont(t);
    }

    /// <summary>全面ストレッチのラベル。</summary>
    private TextMeshProUGUI Label(Transform p, string text, float size, Color c,
        TextAlignmentOptions align = TextAlignmentOptions.Center, FontStyles style = FontStyles.Bold)
    {
        var go = Child("L", p, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var t = go.AddComponent<TextMeshProUGUI>();
        t.text = text; t.fontSize = size; t.fontStyle = style;
        t.alignment = align; t.color = c; t.raycastTarget = false;
        ApplyFont(t);
        return t;
    }

    /// <summary>アンカー指定のボタン（背景＋ラベル）。</summary>
    private Button BoxButton(Transform p, string n, Color bg, string label, float fontSize, Color labelColor,
        Vector2 aMin, Vector2 aMax, Action onClick, Vector2? offMin = null, Vector2? offMax = null)
    {
        var go = Child(n, p, aMin, aMax, offMin ?? Vector2.zero, offMax ?? Vector2.zero);
        go.AddComponent<Image>().color = bg;
        var btn = go.AddComponent<Button>();
        btn.navigation = new Navigation { mode = Navigation.Mode.None };
        var cb = ColorBlock.defaultColorBlock;
        cb.highlightedColor = new Color(1f, 1f, 1f, .85f);
        cb.pressedColor = new Color(.65f, .65f, .65f, 1f);
        btn.colors = cb;
        btn.onClick.AddListener(() => onClick());
        Label(go.transform, label, fontSize, labelColor);
        return btn;
    }

    /// <summary>1行テキスト入力の行。</summary>
    private TMP_InputField InputRow(string placeholder, bool multiline = false, int charLimit = 60, Transform parent = null)
    {
        var row = Row(multiline ? 180f : 96f, "Input", parent);
        row.AddComponent<Image>().color = C_PANEL;
        var tf = row.AddComponent<TMP_InputField>();
        tf.characterLimit = charLimit;
        if (multiline) tf.lineType = TMP_InputField.LineType.MultiLineNewline;
        MakeInputInternal(tf, row, placeholder);
        return tf;
    }

    /// <summary>職業・部位などの選択ボタングリッド（1行）。</summary>
    private GameObject[] MakeSelectGrid((string label, string key)[] defs, int cols, int rows, Action<int> onSelect)
    {
        var gridRow = Row(rows * 100f, "Grid");
        var btns = new GameObject[defs.Length];
        for (int i = 0; i < defs.Length; i++)
        {
            int c = i % cols, r = i / cols;
            var go = Child($"Opt{i}", gridRow.transform,
                new Vector2(c / (float)cols, 1f - (r + 1f) / rows),
                new Vector2((c + 1f) / cols, 1f - r / (float)rows),
                new Vector2(6f, 6f), new Vector2(-6f, -6f));
            go.AddComponent<Image>().color = C_PANEL;
            var b = go.AddComponent<Button>();
            b.navigation = new Navigation { mode = Navigation.Mode.None };
            int cap = i;
            b.onClick.AddListener(() => onSelect(cap));
            Label(go.transform, defs[i].label, 34, C_TEXT);
            btns[i] = go;
        }
        return btns;
    }

    private void MakeInputInternal(TMP_InputField tf, GameObject go, string ph)
    {
        var ta = Go("TA", go.transform);
        var tart = ta.GetComponent<RectTransform>();
        tart.anchorMin = Vector2.zero; tart.anchorMax = Vector2.one;
        tart.offsetMin = new Vector2(16f, 4f); tart.offsetMax = new Vector2(-16f, -4f);
        ta.AddComponent<RectMask2D>();

        var phGO = Go("PH", ta.transform);
        var phRT = phGO.GetComponent<RectTransform>();
        phRT.anchorMin = Vector2.zero; phRT.anchorMax = Vector2.one;
        phRT.offsetMin = phRT.offsetMax = Vector2.zero;
        var pht = phGO.AddComponent<TextMeshProUGUI>();
        pht.text = ph; pht.fontSize = 30; pht.color = C_MUTED;
        pht.alignment = TextAlignmentOptions.MidlineLeft; ApplyFont(pht);

        var it = Go("IT", ta.transform);
        var itRT = it.GetComponent<RectTransform>();
        itRT.anchorMin = Vector2.zero; itRT.anchorMax = Vector2.one;
        itRT.offsetMin = itRT.offsetMax = Vector2.zero;
        var itt = it.AddComponent<TextMeshProUGUI>();
        itt.fontSize = 30; itt.color = C_TEXT;
        itt.alignment = TextAlignmentOptions.MidlineLeft; ApplyFont(itt);

        tf.textViewport = tart; tf.placeholder = pht; tf.textComponent = itt;
    }
}
