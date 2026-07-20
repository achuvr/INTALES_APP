using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Firebase.Firestore;
using Cysharp.Threading.Tasks;

/// <summary>
/// ガチャ管理シーン（GachaAdminシーン用・管理者向け）。
/// master/gacha の排出テーブル（全プール）を一覧表示して編集する。
///
/// できること:
///  - 各エントリの重み編集（確率＝プール内の重み合計に対する割合。0=排出停止）
///  - イベントガチャの新規作成（例:「ウイングスパンガチャ」「Slay the Spireガチャ」。
///    プールIDを分けることでイベントガチャを何種類でも併設できる）
///  - プールへのエントリ追加（クーポン・装備を検索して選ぶ）・エントリの削除
///  - イベントQRコードの表示（"gacha_event:{プールID}"。来店客が読むと無料で1回引ける）
///  - イベントプールの削除（standard / event は削除不可）
///
/// 保存の仕組み:
///  - 重みの編集とエントリの追加・削除はローカルの編集モデル(_pools)に反映し、
///    「変更を保存」で各プールの entries 配列を丸ごと書き戻す
///  - プールの作成・削除はその場で Firestore に書き込む（エントリ編集とは独立）
///
/// 書き込みは Firestore ルール上、管理者クレーム(admin=true)のアカウントのみ可能。
/// </summary>
public class GachaAdminManager : MonoBehaviour
{
    private static readonly Dictionary<string, string> COUPON_NAMES = new Dictionary<string, string>
    {
        { "atk",    "ATKクーポン" },
        { "drink",  "ドリンククーポン" },
        { "coffee", "コーヒークーポン" },
        { "five",   "5%OFFクーポン" },
        { "seven",  "7%OFFクーポン" },
    };

    private static readonly Dictionary<string, string> JOB_JP = new Dictionary<string, string>
    {
        { "warrior", "戦士" }, { "magician", "魔法使い" }, { "archer", "弓使い" },
        { "gunner", "銃使い" }, { "common", "共通" },
    };

    private static readonly Dictionary<string, string> SLOT_JP = new Dictionary<string, string>
    {
        { "weapon", "武器" }, { "head", "頭" }, { "body", "体" },
        { "feet", "足" }, { "foot", "足" },
        { "skill_book_a", "スキルA" }, { "skillA", "スキルA" },
        { "skill_book_b", "スキルB" }, { "skillB", "スキルB" },
    };

    /// <summary>ピッカーから追加したエントリの初期重み（装備登録シーンの既定値と同じ）</summary>
    private const int DEFAULT_ENTRY_WEIGHT = 10;

    // ---- 1エントリ分のUIと状態 ----
    private class EntryRow
    {
        public Dictionary<string, object> raw; // _pools 内のエントリ辞書への参照（type/id/weight以外のフィールドも保持して書き戻す）
        public string type;
        public string id;
        public TMP_InputField weightInput;
        public TextMeshProUGUI pctLabel;
    }

    // ---- 1プール分のUIと状態 ----
    private class PoolSection
    {
        public string poolId;
        public string name;
        public int cost;
        public TextMeshProUGUI headerLabel;
        public readonly List<EntryRow> rows = new List<EntryRow>();
    }

    // ---- UI 参照 ----
    private Transform _canvasTf;
    private Transform _content;
    private TMP_FontAsset _jp;
    private TextMeshProUGUI _statusLabel;
    private Button _saveBtn;
    private readonly List<PoolSection> _sections = new List<PoolSection>();
    private Dictionary<string, Dictionary<string, object>> _itemsMap
        = new Dictionary<string, Dictionary<string, object>>(); // item_id → fields（名前解決用）
    private Dictionary<string, string> _seriesNames
        = new Dictionary<string, string>(); // series_id → シリーズ名（＝ゲームタイトル）
    private Dictionary<string, object> _pools
        = new Dictionary<string, object>(); // master/gacha の pools（編集モデル。poolId → プール辞書）
    private bool _busy;

    // ---- モーダル（同時に1つだけ開く） ----
    private GameObject _modal;
    private TextMeshProUGUI _modalMsg;
    private Texture2D _qrTex;
    private Transform _pickerContent;   // エントリ追加ピッカーの一覧
    private string _pickerPoolId;
    private TMP_InputField _pickerSearch;

    // ---- 色 ----
    private static readonly Color C_BG       = new Color(0.10f,0.08f,0.06f);
    private static readonly Color C_PANEL    = new Color(0.18f,0.14f,0.10f);
    private static readonly Color C_ROW      = new Color(0.14f,0.11f,0.08f);
    private static readonly Color C_ROW_ITEM = new Color(0.12f,0.14f,0.10f); // 装備エントリの行
    private static readonly Color C_GOLD     = new Color(0.92f,0.72f,0.22f);
    private static readonly Color C_TEXT     = new Color(0.95f,0.90f,0.78f);
    private static readonly Color C_MUTED    = new Color(0.55f,0.48f,0.38f);
    private static readonly Color C_PCT      = new Color(0.62f,0.92f,0.70f);
    private static readonly Color C_BTN_SAVE   = new Color(0.15f,0.45f,0.20f);
    private static readonly Color C_BTN_RELOAD = new Color(0.20f,0.35f,0.55f);
    private static readonly Color C_BTN_CREATE = new Color(0.62f,0.42f,0.12f); // 金茶（イベントガチャ作成）
    private static readonly Color C_BTN_QR     = new Color(0.38f,0.28f,0.58f); // 紫（QR表示）
    private static readonly Color C_BTN_DEL    = new Color(0.55f,0.16f,0.16f); // 暗赤（削除）
    private static readonly Color C_ERR = new Color(0.85f,0.25f,0.25f);
    private static readonly Color C_OK  = new Color(0.28f,0.72f,0.28f);

    void Start()
    {
#if UNITY_EDITOR
        _jp = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Fonts/jp.asset");
        if (_jp == null)
            _jp = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Fonts 1/jp.asset");
#endif
        if (_jp == null)
        {
            var fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
            _jp = fonts.FirstOrDefault(f => f.name.ToLower() == "jp") ?? fonts.FirstOrDefault();
        }
        BuildUI();
        ReloadAsync().Forget();
    }

    void ApplyFont(TextMeshProUGUI t) { if (_jp != null) t.font = _jp; }
    void SS(string s, Color c) { if (_statusLabel != null) { _statusLabel.text = s; _statusLabel.color = c; } }
    /// <summary>モーダル内メッセージ（モーダルが画面全体を覆うため、ステータスバーの代わり）</summary>
    void MS(string s, Color c) { if (_modalMsg != null) { _modalMsg.text = s; _modalMsg.color = c; } }

    /// <summary>辞書から文字列フィールドを安全に取り出す</summary>
    private static string Str(Dictionary<string, object> m, string key)
        => m != null && m.TryGetValue(key, out var v) && v != null ? v.ToString() : "";

    // ================================================================
    // 読み込み（master/gacha と master/items）
    // ================================================================
    private async UniTaskVoid ReloadAsync()
    {
        if (_busy) return;
        _busy = true;
        SS("読み込み中...", C_MUTED);
        try
        {
            var db = FirebaseFirestore.DefaultInstance;
            var itemsTask = db.Collection("master").Document("items").GetSnapshotAsync().AsUniTask();
            var gachaTask = db.Collection("master").Document("gacha").GetSnapshotAsync().AsUniTask();
            var (itemsSnap, gachaSnap) = await UniTask.WhenAll(itemsTask, gachaTask);

            _itemsMap.Clear();
            if (itemsSnap.Exists && itemsSnap.ContainsField("items"))
            {
                foreach (var kv in itemsSnap.GetValue<Dictionary<string, object>>("items"))
                    if (kv.Value is Dictionary<string, object> f)
                        _itemsMap[kv.Key] = f;
            }

            // シリーズ名（＝ゲームタイトル）。装備の表示にどのゲームの装備かを付ける
            _seriesNames.Clear();
            if (itemsSnap.Exists && itemsSnap.ContainsField("series"))
            {
                foreach (var kv in itemsSnap.GetValue<Dictionary<string, object>>("series"))
                    if (kv.Value is Dictionary<string, object> f)
                        _seriesNames[kv.Key] = Str(f, "name");
            }

            if (!gachaSnap.Exists || !gachaSnap.ContainsField("pools"))
            {
                SS("master/gacha の pools がありません", C_ERR);
                return;
            }
            _pools = gachaSnap.GetValue<Dictionary<string, object>>("pools");
            RebuildList();

            int entryCount = _sections.Sum(s => s.rows.Count);
            SS($"プール{_sections.Count}件・エントリ{entryCount}件を読み込みました。重みを編集して保存してください（0=排出停止）", C_MUTED);
        }
        catch (Exception ex)
        {
            SS($"読み込みエラー: {ex.Message}", C_ERR);
            Debug.LogError($"[GachaAdmin] {ex}");
        }
        finally
        {
            _busy = false;
        }
    }

    // ================================================================
    // 保存（各プールの entries 配列を丸ごと書き戻す。重み以外は元の値を保持）
    // ================================================================
    private async UniTaskVoid SaveAsync()
    {
        if (_busy) return;

        // バリデーション（0以上の整数。0は排出停止として許可）
        var updates = new Dictionary<FieldPath, object>();
        foreach (var s in _sections)
        {
            var entries = new List<object>();
            foreach (var r in s.rows)
            {
                if (!int.TryParse(r.weightInput.text.Trim(), out int w) || w < 0)
                {
                    SS($"{s.poolId} / {EntryLabel(r.type, r.id)} の重みが不正です（0以上の整数）", C_ERR);
                    return;
                }
                var m = new Dictionary<string, object>(r.raw) { ["weight"] = w };
                entries.Add(m);
            }
            updates[new FieldPath("pools", s.poolId, "entries")] = entries;
        }
        if (updates.Count == 0) { SS("保存する内容がありません", C_MUTED); return; }

        _busy = true;
        SS("保存中...", C_MUTED);
        try
        {
            await FirebaseFirestore.DefaultInstance
                .Collection("master").Document("gacha")
                .UpdateAsync(updates).AsUniTask();
            GachaService.Invalidate(); // この端末のセッションキャッシュを破棄（次の抽選から新テーブル）
            SS($"保存完了！（{_sections.Count}プールの排出テーブルを更新。他の端末には次回起動から反映）", C_OK);
            Debug.Log("[GachaAdmin] 排出テーブルを保存しました");
        }
        catch (Exception ex)
        {
            SS($"保存エラー: {ex.Message}", C_ERR);
            Debug.LogError($"[GachaAdmin] {ex}");
        }
        finally
        {
            _busy = false;
        }
    }

    // ================================================================
    // 一覧の構築（_pools の編集モデルからUIを組み直す）
    // ================================================================
    private void RebuildList()
    {
        for (int i = _content.childCount - 1; i >= 0; i--)
            Destroy(_content.GetChild(i).gameObject);
        _sections.Clear();

        // イベントガチャの新規作成（一覧の最上部）
        var createRow = Row(104f, "CreatePool");
        BoxButton(createRow.transform, "Create", C_BTN_CREATE, "＋ 新しいイベントガチャを作成", 32, C_TEXT,
            Vector2.zero, Vector2.one, ShowCreatePoolModal);
        Row(20f, "Gap");

        // standard を先頭に、それ以外はID順
        var ordered = _pools.Keys.OrderBy(k => k == GachaService.STANDARD_POOL ? "" : k).ToList();
        foreach (var poolId in ordered)
        {
            if (!(_pools[poolId] is Dictionary<string, object> pool)) continue;
            BuildPoolSection(poolId, pool);
        }
    }

    private void BuildPoolSection(string poolId, Dictionary<string, object> pool)
    {
        var section = new PoolSection { poolId = poolId };
        section.name = pool.TryGetValue("name", out var n) && n != null ? n.ToString() : poolId;
        int costGp = pool.TryGetValue("cost_gp", out var cg) ? Convert.ToInt32(cg) : 0;
        int costLv = pool.TryGetValue("cost_lv", out var cl) ? Convert.ToInt32(cl) : 0;
        section.cost = costGp > 0 ? costGp : costLv; // GachaPool.Cost と同じ解決順

        // プール見出し（重み合計は RecalcPool が入れる）
        var headRow = Row(96f, $"Head_{poolId}");
        headRow.AddComponent<Image>().color = C_PANEL;
        section.headerLabel = Label(headRow.transform, "", 32, C_GOLD, TextAlignmentOptions.MidlineLeft);
        var hrt = section.headerLabel.GetComponent<RectTransform>();
        hrt.offsetMin = new Vector2(20f, 0f); hrt.offsetMax = new Vector2(-20f, 0f);

        // プール操作バー（エントリ追加・イベントQR・プール削除）
        bool isEvent = poolId != GachaService.STANDARD_POOL;
        bool deletable = isEvent && poolId != GachaService.EVENT_POOL;
        var toolRow = Row(84f, $"Tools_{poolId}");
        BoxButton(toolRow.transform, "AddEntry", C_BTN_RELOAD, "＋エントリ追加", 28, C_TEXT,
            new Vector2(0f, 0f), new Vector2(0.32f, 1f), () => ShowAddEntryModal(poolId));
        if (isEvent)
            BoxButton(toolRow.transform, "Qr", C_BTN_QR, "イベントQR", 28, C_TEXT,
                new Vector2(0.35f, 0f), new Vector2(0.64f, 1f), () => ShowQrModal(poolId));
        if (deletable)
            BoxButton(toolRow.transform, "DelPool", C_BTN_DEL, "プール削除", 28, C_TEXT,
                new Vector2(0.72f, 0f), new Vector2(1f, 1f), () => ShowDeletePoolModal(poolId));

        if (pool.TryGetValue("entries", out var entObj) && entObj is List<object> entries)
        {
            foreach (var e in entries)
            {
                if (!(e is Dictionary<string, object> m)) continue;
                BuildEntryRow(section, m);
            }
        }

        if (section.rows.Count == 0)
        {
            var emptyRow = Row(70f, "Empty");
            Label(emptyRow.transform, "（エントリなし。＋エントリ追加で景品を登録してください）", 26, C_MUTED);
        }

        // プール間の余白
        Row(24f, "Gap");

        _sections.Add(section);
        RecalcPool(section);
    }

    private void BuildEntryRow(PoolSection section, Dictionary<string, object> raw)
    {
        var row = new EntryRow
        {
            raw  = raw,
            type = raw.TryGetValue("type", out var t) && t != null ? t.ToString() : "",
            id   = raw.TryGetValue("id",   out var i) && i != null ? i.ToString() : "",
        };
        int weight = raw.TryGetValue("weight", out var w) ? Convert.ToInt32(w) : 0;

        var rowGO = Row(96f, $"Entry_{row.id}");
        rowGO.AddComponent<Image>().color = row.type == "item" ? C_ROW_ITEM : C_ROW;

        // 名前ラベル（解決できない装備IDは警告色）
        string label = EntryLabel(row.type, row.id);
        bool orphan = row.type == "item" && !_itemsMap.ContainsKey(row.id);
        var nameGO = Child("Name", rowGO.transform, new Vector2(0f, 0f), new Vector2(0.50f, 1f), new Vector2(20f, 0f), Vector2.zero);
        var nameTxt = nameGO.AddComponent<TextMeshProUGUI>();
        nameTxt.text = label;
        nameTxt.fontSize = 28;
        nameTxt.alignment = TextAlignmentOptions.MidlineLeft;
        nameTxt.color = orphan ? C_ERR : C_TEXT;
        nameTxt.overflowMode = TextOverflowModes.Ellipsis;
        nameTxt.enableWordWrapping = false;
        nameTxt.raycastTarget = false;
        ApplyFont(nameTxt);

        // 重み入力
        var wGO = Child("Weight", rowGO.transform, new Vector2(0.52f, 0f), new Vector2(0.68f, 1f), new Vector2(0f, 10f), new Vector2(0f, -10f));
        wGO.AddComponent<Image>().color = C_PANEL;
        row.weightInput = wGO.AddComponent<TMP_InputField>();
        row.weightInput.characterLimit = 5;
        row.weightInput.contentType = TMP_InputField.ContentType.IntegerNumber;
        MakeInputInternal(row.weightInput, wGO, "重み");
        row.weightInput.text = weight.ToString();
        row.weightInput.onValueChanged.AddListener(_ => RecalcPool(section));

        // 確率表示
        var pGO = Child("Pct", rowGO.transform, new Vector2(0.69f, 0f), new Vector2(0.87f, 1f), Vector2.zero, new Vector2(-6f, 0f));
        row.pctLabel = pGO.AddComponent<TextMeshProUGUI>();
        row.pctLabel.fontSize = 30;
        row.pctLabel.fontStyle = FontStyles.Bold;
        row.pctLabel.alignment = TextAlignmentOptions.MidlineRight;
        row.pctLabel.color = C_PCT;
        row.pctLabel.raycastTarget = false;
        ApplyFont(row.pctLabel);

        // 削除ボタン（一覧から取り除く。「変更を保存」で確定）
        var dGO = Child("Del", rowGO.transform, new Vector2(0.89f, 0f), new Vector2(1f, 1f), new Vector2(0f, 16f), new Vector2(-8f, -16f));
        dGO.AddComponent<Image>().color = C_BTN_DEL;
        var dBtn = dGO.AddComponent<Button>();
        dBtn.navigation = new Navigation { mode = Navigation.Mode.None };
        dBtn.onClick.AddListener(() => RemoveEntry(section, row));
        Label(dGO.transform, "✕", 30, C_TEXT);

        section.rows.Add(row);
    }

    /// <summary>シリーズID → シリーズ名（＝ゲームタイトル）。未設定・不明は空文字。</summary>
    private string SeriesName(string seriesId)
        => !string.IsNullOrEmpty(seriesId) && _seriesNames.TryGetValue(seriesId, out var n) ? n : "";

    /// <summary>エントリの表示名（クーポン名 or 【ゲームタイトル】装備名＋職業・部位）。</summary>
    private string EntryLabel(string type, string id)
    {
        if (type == "coupon")
            return COUPON_NAMES.TryGetValue(id, out var cn) ? cn : $"クーポン: {id}";
        if (type == "item")
        {
            if (_itemsMap.TryGetValue(id, out var f))
            {
                string name = f.TryGetValue("name", out var n) && n != null ? n.ToString() : id;
                string job  = f.TryGetValue("job", out var j) && j != null ? j.ToString() : "";
                string slot = f.TryGetValue("slot_type", out var s) && s != null ? s.ToString() : "";
                string jobJp  = JOB_JP.TryGetValue(job, out var jj) ? jj : job;
                string slotJp = SLOT_JP.TryGetValue(slot, out var sj) ? sj : slot;
                // ゲームタイトルは先頭に付ける（行末は省略表示で切れるため）
                string game = SeriesName(Str(f, "series"));
                string prefix = game.Length > 0 ? $"【{game}】" : "";
                return $"{prefix}{name}（{jobJp}・{slotJp}）";
            }
            return $"！存在しない装備ID: {id}";
        }
        return $"{type}: {id}";
    }

    /// <summary>プール内の確率表示と見出しの重み合計をライブ更新する。</summary>
    private void RecalcPool(PoolSection s)
    {
        int total = 0;
        foreach (var r in s.rows)
            if (int.TryParse(r.weightInput.text.Trim(), out int w) && w > 0) total += w;

        foreach (var r in s.rows)
        {
            if (!int.TryParse(r.weightInput.text.Trim(), out int w) || w < 0)
                { r.pctLabel.text = "不正"; r.pctLabel.color = C_ERR; }
            else if (w == 0 || total == 0)
                { r.pctLabel.text = "停止中"; r.pctLabel.color = C_MUTED; }
            else
                { r.pctLabel.text = $"{(w * 100f / total):F1}%"; r.pctLabel.color = C_PCT; }
        }

        string costText = s.cost > 0 ? $"1回{s.cost}GP" : "無料（イベントQR）";
        if (s.headerLabel != null)
            s.headerLabel.text = $"{s.poolId}（{s.name}）　{costText}・重み合計{total}";
    }

    /// <summary>
    /// 画面上の重み入力を編集モデル(_pools)に書き戻す。
    /// RebuildList でUIを組み直す前に呼び、編集途中の重みが消えないようにする。
    /// 不正な入力値（空欄など）は書き戻さず元の値を保つ。
    /// </summary>
    private void SyncWeightsToModel()
    {
        foreach (var s in _sections)
            foreach (var r in s.rows)
                if (int.TryParse(r.weightInput.text.Trim(), out int w) && w >= 0)
                    r.raw["weight"] = w;
    }

    // ================================================================
    // エントリの追加・削除（編集モデルを更新して一覧を組み直す。保存で確定）
    // ================================================================
    private void RemoveEntry(PoolSection section, EntryRow row)
    {
        if (_busy) return;
        SyncWeightsToModel();
        if (_pools.TryGetValue(section.poolId, out var pObj) && pObj is Dictionary<string, object> pool
            && pool.TryGetValue("entries", out var eObj) && eObj is List<object> entries)
            entries.Remove(row.raw);
        RebuildList();
        SS($"「{EntryLabel(row.type, row.id)}」を {section.poolId} から取り除きました（「変更を保存」で確定）", C_MUTED);
    }

    private void AddEntryToPool(string poolId, string type, string id)
    {
        if (!(_pools.TryGetValue(poolId, out var pObj) && pObj is Dictionary<string, object> pool)) return;
        if (!(pool.TryGetValue("entries", out var eObj) && eObj is List<object> entries))
        {
            entries = new List<object>();
            pool["entries"] = entries;
        }

        // 二重追加ガード（同じ type+id は1件まで）
        foreach (var e in entries)
            if (e is Dictionary<string, object> m && Str(m, "type") == type && Str(m, "id") == id)
                return;

        SyncWeightsToModel();
        entries.Add(new Dictionary<string, object>
        {
            { "type", type }, { "id", id }, { "weight", DEFAULT_ENTRY_WEIGHT },
        });
        RebuildList();

        MS($"「{EntryLabel(type, id)}」を追加しました（重み{DEFAULT_ENTRY_WEIGHT}。閉じたあと「変更を保存」で確定）", C_OK);
        // ピッカーからは追加済みとして消す
        if (_pickerContent != null)
            RebuildPicker(_pickerSearch != null ? _pickerSearch.text : "");
    }

    // ================================================================
    // モーダル: イベントガチャ（プール）の新規作成
    // ================================================================
    private void ShowCreatePoolModal()
    {
        if (_busy) return;
        var ov = ModalOverlay("__CreateModal");
        var panel = ModalPanel(ov, 940f, 900f);

        ModalTitle(panel, "新しいイベントガチャ");

        FormLabel(panel, "名前", -160f);
        var nameInput = MakeTextInput(panel.transform,
            new Vector2(0.26f, 1f), new Vector2(0.96f, 1f), new Vector2(0f, -264f), new Vector2(0f, -160f),
            "例: ウイングスパンガチャ");

        FormLabel(panel, "ID", -310f);
        var idInput = MakeTextInput(panel.transform,
            new Vector2(0.26f, 1f), new Vector2(0.96f, 1f), new Vector2(0f, -414f), new Vector2(0f, -310f),
            "例: wingspan（空欄で自動）");

        var note = Label(Child("Note", panel.transform, new Vector2(0.04f, 1f), new Vector2(0.96f, 1f),
            new Vector2(0f, -600f), new Vector2(0f, -440f)).transform,
            "IDは半角英数字・_・-（QRコードの中身になります）\n作成後に「＋エントリ追加」で景品を登録し、\n「イベントQR」を店頭に掲示してください（読むと無料で1回）",
            24, C_MUTED, TextAlignmentOptions.TopLeft, FontStyles.Normal);
        note.enableWordWrapping = true;

        BuildModalMsg(panel, -620f);

        BoxButton(panel.transform, "Create", C_BTN_CREATE, "作成する", 32, C_TEXT,
            new Vector2(0.04f, 0f), new Vector2(0.56f, 0f), () => CreatePoolAsync(nameInput.text, idInput.text).Forget())
            .GetComponent<RectTransform>().SetBottomBar(24f, 128f);
        BoxButton(panel.transform, "Cancel", C_ROW, "キャンセル", 30, C_MUTED,
            new Vector2(0.60f, 0f), new Vector2(0.96f, 0f), CloseModal)
            .GetComponent<RectTransform>().SetBottomBar(24f, 128f);
    }

    private async UniTaskVoid CreatePoolAsync(string nameRaw, string idRaw)
    {
        if (_busy) return;
        string name = (nameRaw ?? "").Trim();
        if (name.Length == 0) { MS("ガチャの名前を入力してください", C_ERR); return; }

        string id = (idRaw ?? "").Trim().ToLowerInvariant();
        if (id.Length == 0)
        {
            // 空欄なら event2, event3, ... の空き番号を自動採番
            int i = 2;
            while (_pools.ContainsKey($"event{i}")) i++;
            id = $"event{i}";
        }
        if (!Regex.IsMatch(id, "^[a-z0-9_-]{1,32}$"))
            { MS("IDは半角英数字と _ - のみ・32文字以内で入力してください", C_ERR); return; }
        if (id == GachaService.STANDARD_POOL)
            { MS("このIDは常用ガチャ用のため使用できません", C_ERR); return; }
        if (_pools.ContainsKey(id))
            { MS($"ID「{id}」は既に存在します", C_ERR); return; }

        var poolData = new Dictionary<string, object>
        {
            { "name", name },
            { "cost_gp", 0 },   // イベントガチャはQRから無料で引く
            { "entries", new List<object>() },
        };

        _busy = true;
        MS("作成中...", C_MUTED);
        try
        {
            await FirebaseFirestore.DefaultInstance
                .Collection("master").Document("gacha")
                .UpdateAsync(new Dictionary<FieldPath, object> { { new FieldPath("pools", id), poolData } })
                .AsUniTask();
            GachaService.Invalidate();

            SyncWeightsToModel();
            _pools[id] = poolData;
            CloseModal();
            RebuildList();
            SS($"イベントガチャ「{name}」を作成しました（ID: {id}）。＋エントリ追加で景品を登録してください", C_OK);
            Debug.Log($"[GachaAdmin] プール作成: {id}（{name}）");
        }
        catch (Exception ex)
        {
            MS($"作成エラー: {ex.Message}", C_ERR);
            Debug.LogError($"[GachaAdmin] {ex}");
        }
        finally
        {
            _busy = false;
        }
    }

    // ================================================================
    // モーダル: エントリ追加ピッカー（クーポン＋装備を検索して選ぶ）
    // ================================================================
    private void ShowAddEntryModal(string poolId)
    {
        if (_busy) return;
        var ov = ModalOverlay("__AddModal");
        var panel = ModalPanel(ov, 1000f, 1660f);

        string poolName = _pools.TryGetValue(poolId, out var pObj) && pObj is Dictionary<string, object> p
            ? Str(p, "name") : "";
        if (poolName.Length == 0) poolName = poolId;
        ModalTitle(panel, $"エントリ追加：{poolName}");

        // 検索欄
        _pickerSearch = MakeTextInput(panel.transform,
            new Vector2(0.04f, 1f), new Vector2(0.96f, 1f), new Vector2(0f, -256f), new Vector2(0f, -156f),
            "名前で検索");
        _pickerSearch.onValueChanged.AddListener(text => RebuildPicker(text));

        // 一覧（検索欄の下〜メッセージ欄の上）
        _pickerPoolId = poolId;
        _pickerContent = MakeScrollContent(panel.transform,
            new Vector2(0.04f, 0f), new Vector2(0.96f, 1f), new Vector2(0f, 300f), new Vector2(0f, -276f));

        BuildModalMsg(panel, -1400f);

        BoxButton(panel.transform, "Close", C_ROW, "とじる", 30, C_TEXT,
            new Vector2(0.30f, 0f), new Vector2(0.70f, 0f), CloseModal)
            .GetComponent<RectTransform>().SetBottomBar(24f, 128f);

        RebuildPicker("");
    }

    private void RebuildPicker(string filter)
    {
        if (_pickerContent == null) return;
        for (int i = _pickerContent.childCount - 1; i >= 0; i--)
            Destroy(_pickerContent.GetChild(i).gameObject);

        // 追加済みの type:id はピッカーに出さない
        var existing = new HashSet<string>();
        if (_pools.TryGetValue(_pickerPoolId, out var pObj) && pObj is Dictionary<string, object> pool
            && pool.TryGetValue("entries", out var eObj) && eObj is List<object> entries)
            foreach (var e in entries)
                if (e is Dictionary<string, object> m)
                    existing.Add($"{Str(m, "type")}:{Str(m, "id")}");

        filter = (filter ?? "").Trim();
        int added = 0;

        // クーポン
        foreach (var kv in COUPON_NAMES)
        {
            if (existing.Contains($"coupon:{kv.Key}")) continue;
            if (filter.Length > 0 && !kv.Value.Contains(filter) && !kv.Key.Contains(filter)) continue;
            BuildPickerRow("coupon", kv.Key, $"クーポン：{kv.Value}", C_ROW);
            added++;
        }

        // 装備（ゲームタイトル順→名前順。シリーズなしは先頭にまとまる）
        foreach (var kv in _itemsMap
            .OrderBy(x => SeriesName(Str(x.Value, "series")))
            .ThenBy(x => Str(x.Value, "name")))
        {
            if (existing.Contains($"item:{kv.Key}")) continue;
            string label = EntryLabel("item", kv.Key);
            if (filter.Length > 0 && !label.Contains(filter) && !kv.Key.Contains(filter)) continue;
            BuildPickerRow("item", kv.Key, label, C_ROW_ITEM);
            added++;
        }

        if (added == 0)
        {
            var row = ListRow(_pickerContent, 80f, "Empty");
            Label(row.transform, filter.Length > 0 ? "（該当なし）" : "（追加できるエントリがありません）", 26, C_MUTED);
        }
    }

    private void BuildPickerRow(string type, string id, string label, Color bg)
    {
        var rowGO = ListRow(_pickerContent, 90f, $"Pick_{id}");
        rowGO.AddComponent<Image>().color = bg;
        var btn = rowGO.AddComponent<Button>();
        btn.navigation = new Navigation { mode = Navigation.Mode.None };
        btn.onClick.AddListener(() => AddEntryToPool(_pickerPoolId, type, id));

        var nameGO = Child("Name", rowGO.transform, new Vector2(0f, 0f), new Vector2(0.82f, 1f), new Vector2(20f, 0f), Vector2.zero);
        var nameTxt = nameGO.AddComponent<TextMeshProUGUI>();
        nameTxt.text = label;
        nameTxt.fontSize = 27;
        nameTxt.alignment = TextAlignmentOptions.MidlineLeft;
        nameTxt.color = C_TEXT;
        nameTxt.overflowMode = TextOverflowModes.Ellipsis;
        nameTxt.enableWordWrapping = false;
        nameTxt.raycastTarget = false;
        ApplyFont(nameTxt);

        var addGO = Child("Add", rowGO.transform, new Vector2(0.84f, 0f), new Vector2(1f, 1f), Vector2.zero, new Vector2(-12f, 0f));
        var addTxt = addGO.AddComponent<TextMeshProUGUI>();
        addTxt.text = "＋追加";
        addTxt.fontSize = 26;
        addTxt.fontStyle = FontStyles.Bold;
        addTxt.alignment = TextAlignmentOptions.MidlineRight;
        addTxt.color = C_PCT;
        addTxt.raycastTarget = false;
        ApplyFont(addTxt);
    }

    // ================================================================
    // モーダル: イベントQRコードの表示（"gacha_event:{プールID}"）
    // ================================================================
    private void ShowQrModal(string poolId)
    {
        var ov = ModalOverlay("__QrModal");
        // QRの読み取りやすさのため白背景
        var panel = ModalPanel(ov, 940f, 1360f);
        panel.GetComponent<Image>().color = Color.white;

        string poolName = _pools.TryGetValue(poolId, out var pObj) && pObj is Dictionary<string, object> p
            ? Str(p, "name") : "";
        if (poolName.Length == 0) poolName = poolId;
        var title = ModalTitle(panel, poolName);
        title.color = new Color(0.15f, 0.12f, 0.10f);

        string payload = $"{CallMethodFromQR.GACHA_EVENT_QR_PREFIX}:{poolId}";
        _qrTex = QRCodeHelper.CreateQRCode(payload, 512, 512);
        if (_qrTex != null) _qrTex.filterMode = FilterMode.Point; // 拡大表示してもドットをくっきり保つ

        var qrGO = Child("QR", panel.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, Vector2.zero);
        var qrt = qrGO.GetComponent<RectTransform>();
        qrt.sizeDelta = new Vector2(760f, 760f);
        qrt.anchoredPosition = new Vector2(0f, -560f);
        var raw = qrGO.AddComponent<RawImage>();
        raw.texture = _qrTex;

        Label(Child("Payload", panel.transform, new Vector2(0.04f, 1f), new Vector2(0.96f, 1f),
            new Vector2(0f, -1030f), new Vector2(0f, -960f)).transform,
            payload, 26, new Color(0.35f, 0.32f, 0.30f), TextAlignmentOptions.Center, FontStyles.Normal);

        var note = Label(Child("Note", panel.transform, new Vector2(0.06f, 1f), new Vector2(0.94f, 1f),
            new Vector2(0f, -1180f), new Vector2(0f, -1040f)).transform,
            "来店したお客様がアプリのQRカメラで読み取ると\nこのガチャを無料で1回引けます（読むたびに1回）",
            26, new Color(0.35f, 0.32f, 0.30f), TextAlignmentOptions.Top, FontStyles.Normal);
        note.enableWordWrapping = true;

        BoxButton(panel.transform, "Close", new Color(0.86f, 0.28f, 0.28f), "とじる", 30, Color.white,
            new Vector2(0.30f, 0f), new Vector2(0.70f, 0f), CloseModal)
            .GetComponent<RectTransform>().SetBottomBar(24f, 128f);
    }

    // ================================================================
    // モーダル: プール削除（standard / event 以外のみ）
    // ================================================================
    private void ShowDeletePoolModal(string poolId)
    {
        if (_busy) return;
        var ov = ModalOverlay("__DeleteModal");
        var panel = ModalPanel(ov, 940f, 760f);

        ModalTitle(panel, "イベントガチャの削除");

        string poolName = poolId;
        int entryCount = 0;
        if (_pools.TryGetValue(poolId, out var pObj) && pObj is Dictionary<string, object> pool)
        {
            poolName = Str(pool, "name");
            if (pool.TryGetValue("entries", out var eObj) && eObj is List<object> entries)
                entryCount = entries.Count;
        }

        var body = Label(Child("Body", panel.transform, new Vector2(0.06f, 1f), new Vector2(0.94f, 1f),
            new Vector2(0f, -400f), new Vector2(0f, -160f)).transform,
            $"「{poolName}」（ID: {poolId}）を削除しますか？\nエントリ{entryCount}件も一緒に削除されます。\nこの操作は取り消せません。",
            28, C_TEXT, TextAlignmentOptions.Top, FontStyles.Normal);
        body.enableWordWrapping = true;

        BuildModalMsg(panel, -470f);

        BoxButton(panel.transform, "Delete", C_BTN_DEL, "削除する", 32, C_TEXT,
            new Vector2(0.04f, 0f), new Vector2(0.48f, 0f), () => DeletePoolAsync(poolId).Forget())
            .GetComponent<RectTransform>().SetBottomBar(24f, 128f);
        BoxButton(panel.transform, "Cancel", C_ROW, "キャンセル", 30, C_MUTED,
            new Vector2(0.52f, 0f), new Vector2(0.96f, 0f), CloseModal)
            .GetComponent<RectTransform>().SetBottomBar(24f, 128f);
    }

    private async UniTaskVoid DeletePoolAsync(string poolId)
    {
        if (_busy) return;
        // 念のため（standard / event はUI上ボタンが出ないが二重ガード）
        if (poolId == GachaService.STANDARD_POOL || poolId == GachaService.EVENT_POOL)
            { MS("このプールは削除できません", C_ERR); return; }

        _busy = true;
        MS("削除中...", C_MUTED);
        try
        {
            await FirebaseFirestore.DefaultInstance
                .Collection("master").Document("gacha")
                .UpdateAsync(new Dictionary<FieldPath, object> { { new FieldPath("pools", poolId), FieldValue.Delete } })
                .AsUniTask();
            GachaService.Invalidate();

            SyncWeightsToModel();
            _pools.Remove(poolId);
            CloseModal();
            RebuildList();
            SS($"プール「{poolId}」を削除しました", C_OK);
            Debug.Log($"[GachaAdmin] プール削除: {poolId}");
        }
        catch (Exception ex)
        {
            MS($"削除エラー: {ex.Message}", C_ERR);
            Debug.LogError($"[GachaAdmin] {ex}");
        }
        finally
        {
            _busy = false;
        }
    }

    // ================================================================
    // モーダル共通部品
    // ================================================================
    /// <summary>全画面を覆う暗幕。既に開いているモーダルは閉じる。</summary>
    private GameObject ModalOverlay(string name)
    {
        CloseModal();
        var ov = Child(name, _canvasTf, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        ov.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.72f); // 背面のクリックも遮断する
        _modal = ov;
        return ov;
    }

    private GameObject ModalPanel(GameObject ov, float w, float h)
    {
        var panel = Child("Panel", ov.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        var rt = panel.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(w, h);
        panel.AddComponent<Image>().color = C_PANEL;
        return panel;
    }

    private TextMeshProUGUI ModalTitle(GameObject panel, string text)
    {
        return Label(Child("Title", panel.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(20f, -130f), new Vector2(-20f, -20f)).transform,
            text, 40, C_GOLD);
    }

    /// <summary>モーダル内のメッセージ欄（バリデーションエラーや完了通知の表示先）</summary>
    private void BuildModalMsg(GameObject panel, float topY)
    {
        var go = Child("Msg", panel.transform, new Vector2(0.04f, 1f), new Vector2(0.96f, 1f),
            new Vector2(0f, topY - 90f), new Vector2(0f, topY));
        _modalMsg = go.AddComponent<TextMeshProUGUI>();
        _modalMsg.fontSize = 24;
        _modalMsg.alignment = TextAlignmentOptions.Midline;
        _modalMsg.color = C_MUTED;
        _modalMsg.enableWordWrapping = true;
        _modalMsg.raycastTarget = false;
        ApplyFont(_modalMsg);
    }

    /// <summary>入力欄の左に置くラベル。topY から入力欄と同じ高さ(104)の帯に配置する。</summary>
    private void FormLabel(GameObject panel, string text, float topY)
    {
        Label(Child("FL", panel.transform, new Vector2(0.04f, 1f), new Vector2(0.24f, 1f),
            new Vector2(0f, topY - 104f), new Vector2(0f, topY)).transform,
            text, 30, C_TEXT, TextAlignmentOptions.MidlineLeft);
    }

    private void CloseModal()
    {
        if (_qrTex != null) { Destroy(_qrTex); _qrTex = null; }
        if (_modal != null) { Destroy(_modal); _modal = null; }
        _modalMsg = null;
        _pickerContent = null;
        _pickerSearch = null;
        _pickerPoolId = null;
    }

    // ================================================================
    // UI 構築（スクロール一覧＋下部固定の保存/再読込/ステータス）
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

        var bg = Go("BG", _canvasTf); Stretch(bg);
        bg.AddComponent<Image>().color = C_BG;

        // タイトル（固定）
        var titleGO = Child("Title", _canvasTf, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -120f), Vector2.zero);
        var tTxt = titleGO.AddComponent<TextMeshProUGUI>();
        tTxt.text = "ガチャ管理";
        tTxt.fontSize = 52; tTxt.fontStyle = FontStyles.Bold;
        tTxt.alignment = TextAlignmentOptions.Center; tTxt.color = C_GOLD; tTxt.raycastTarget = false;
        ApplyFont(tTxt);

        // Homeへ戻る（右上。CardTransfer と同じく LoadScene("Home")）
        var backGO = Child("BackBtn", _canvasTf, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-220f, -104f), new Vector2(-20f, -20f));
        backGO.AddComponent<Image>().color = new Color(0.86f, 0.28f, 0.28f);
        var backBtn = backGO.AddComponent<Button>();
        backBtn.navigation = new Navigation { mode = Navigation.Mode.None };
        backBtn.onClick.AddListener(() => UnityEngine.SceneManagement.SceneManager.LoadScene("Home"));
        Label(backGO.transform, "✕ Home", 32, Color.white);

        // スクロール一覧（タイトル下〜下部バーの間）
        _content = MakeScrollContent(_canvasTf, Vector2.zero, Vector2.one, new Vector2(0, 240f), new Vector2(0, -130f));
        var vlg = _content.GetComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(36, 36, 12, 40);
        vlg.spacing = 10f;

        // 下部固定: ボタン行
        var btnBar = Child("BtnBar", _canvasTf, new Vector2(0, 0), new Vector2(1, 0), new Vector2(36f, 116f), new Vector2(-36f, 230f));
        var saveBtn = BoxButton(btnBar.transform, "Save", C_BTN_SAVE, "変更を保存", 40, C_TEXT,
            new Vector2(0f, 0f), new Vector2(0.62f, 1f), () => SaveAsync().Forget());
        _saveBtn = saveBtn;
        BoxButton(btnBar.transform, "Reload", C_BTN_RELOAD, "再読込", 36, C_TEXT,
            new Vector2(0.66f, 0f), new Vector2(1f, 1f), () => ReloadAsync().Forget());

        // 下部固定: ステータスバー
        var statusBar = Child("StatusBar", _canvasTf, new Vector2(0, 0), new Vector2(1, 0), Vector2.zero, Vector2.zero);
        var sbRT = statusBar.GetComponent<RectTransform>();
        sbRT.pivot = new Vector2(0.5f, 0f);
        sbRT.sizeDelta = new Vector2(0, 106f);
        statusBar.AddComponent<Image>().color = new Color(0.06f, 0.05f, 0.04f, 0.98f);
        var stGO = Child("Status", statusBar.transform, Vector2.zero, Vector2.one, new Vector2(24f, 6f), new Vector2(-24f, -6f));
        _statusLabel = stGO.AddComponent<TextMeshProUGUI>();
        _statusLabel.text = "読み込み中...";
        _statusLabel.fontSize = 26;
        _statusLabel.alignment = TextAlignmentOptions.Midline;
        _statusLabel.color = C_MUTED;
        ApplyFont(_statusLabel);
    }

    // ================================================================
    // UI ヘルパー（装備登録シーンと同じ流儀）
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

    private GameObject Child(string n, Transform p, Vector2 aMin, Vector2 aMax, Vector2 offMin, Vector2 offMax)
    {
        var go = Go(n, p);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = aMin; rt.anchorMax = aMax;
        rt.offsetMin = offMin; rt.offsetMax = offMax;
        return go;
    }

    /// <summary>スクロール一覧の1行（固定高さ）。</summary>
    private GameObject Row(float h, string n) => ListRow(_content, h, n);

    /// <summary>VerticalLayoutGroup 配下の1行（固定高さ）。</summary>
    private GameObject ListRow(Transform parent, float h, string n)
    {
        var go = Go(n, parent);
        var le = go.AddComponent<LayoutElement>();
        le.preferredHeight = h; le.minHeight = h;
        return go;
    }

    /// <summary>縦スクロールの一覧を作り、行を並べる Content を返す。</summary>
    private Transform MakeScrollContent(Transform parent, Vector2 aMin, Vector2 aMax, Vector2 offMin, Vector2 offMax)
    {
        var sv = Child("Scroll", parent, aMin, aMax, offMin, offMax);
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
        ctrt.sizeDelta = Vector2.zero; // デフォルト(100,100)のままだと左右がはみ出す
        ctrt.anchoredPosition = Vector2.zero;
        var vlg = ct.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(8, 8, 8, 8);
        vlg.spacing = 8f;
        vlg.childControlWidth = true;  vlg.childForceExpandWidth = true;
        vlg.childControlHeight = true; vlg.childForceExpandHeight = false;
        ct.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.viewport = vp.GetComponent<RectTransform>();
        scroll.content = ctrt;
        return ct.transform;
    }

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

    private Button BoxButton(Transform p, string n, Color bg, string label, float fontSize, Color labelColor,
        Vector2 aMin, Vector2 aMax, Action onClick)
    {
        var go = Child(n, p, aMin, aMax, Vector2.zero, Vector2.zero);
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

    /// <summary>文字入力欄（背景付き）。</summary>
    private TMP_InputField MakeTextInput(Transform parent, Vector2 aMin, Vector2 aMax, Vector2 offMin, Vector2 offMax, string placeholder)
    {
        var go = Child("Input", parent, aMin, aMax, offMin, offMax);
        go.AddComponent<Image>().color = C_ROW;
        var tf = go.AddComponent<TMP_InputField>();
        MakeInputInternal(tf, go, placeholder);
        return tf;
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
        pht.text = ph; pht.fontSize = 28; pht.color = C_MUTED;
        pht.alignment = TextAlignmentOptions.Midline; ApplyFont(pht);

        var it = Go("IT", ta.transform);
        var itRT = it.GetComponent<RectTransform>();
        itRT.anchorMin = Vector2.zero; itRT.anchorMax = Vector2.one;
        itRT.offsetMin = itRT.offsetMax = Vector2.zero;
        var itt = it.AddComponent<TextMeshProUGUI>();
        itt.fontSize = 28; itt.color = C_TEXT;
        itt.alignment = TextAlignmentOptions.Midline; ApplyFont(itt);

        tf.textViewport = tart; tf.placeholder = pht; tf.textComponent = itt;
    }
}

/// <summary>
/// モーダル下部に横並びで置くボタンの配置ヘルパー。
/// アンカーX（BoxButtonの aMin.x/aMax.x）はそのままに、下端から bottom〜bottom+height の帯に配置する。
/// </summary>
internal static class GachaAdminRectExtensions
{
    public static void SetBottomBar(this RectTransform rt, float bottom, float height)
    {
        rt.anchorMin = new Vector2(rt.anchorMin.x, 0f);
        rt.anchorMax = new Vector2(rt.anchorMax.x, 0f);
        rt.offsetMin = new Vector2(0f, bottom);
        rt.offsetMax = new Vector2(0f, bottom + height);
    }
}
