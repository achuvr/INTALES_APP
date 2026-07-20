using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 管理者用: イベントガチャQRの表示モーダル（アカウントモーダルの「イベントガチャQR」から開く）。
/// master/gacha のイベントプール（standard以外）を一覧し、選ぶとそのプールの
/// "gacha_event:{プールID}" QRを白背景で大きく表示する（店頭掲示・その場での読み取り用）。
/// 一覧は開くたびにFirestoreから読み直すので、ガチャ管理で作った直後のプールもすぐ出る。
/// プールの作成・排出テーブルの編集はガチャ管理シーン（GachaAdmin）で行う。
/// </summary>
public static class EventGachaQrViewer
{
    // 羊皮紙パレット（アカウントモーダルと同系）
    private static readonly Color C_PARCHMENT = new Color(0.99f, 0.95f, 0.84f, 0.98f);
    private static readonly Color C_BORDER    = new Color(0.84f, 0.66f, 0.18f, 1.00f);
    private static readonly Color C_TITLE     = new Color(0.38f, 0.16f, 0.04f, 1.00f);
    private static readonly Color C_MUTED     = new Color(0.52f, 0.38f, 0.22f, 1.00f);
    private static readonly Color C_ROW       = new Color(0.93f, 0.87f, 0.70f, 0.92f);
    private static readonly Color C_CLOSE     = new Color(0.90f, 0.32f, 0.32f, 1.00f);

    private static GameObject _overlay;
    private static GameObject _panel;
    private static Texture2D _qrTex;
    private static Dictionary<string, GachaPool> _pools; // 開いたときに取得した一覧（戻る用）
    private static TMP_FontAsset _jp;

    public static void Show()
    {
        Close();

        var canvasGO = GameObject.Find("Canvas");
        var canvas = canvasGO != null ? canvasGO.GetComponent<Canvas>()
                                      : Object.FindFirstObjectByType<Canvas>();
        if (canvas == null) { Debug.LogWarning("[EventQR] Canvasが見つかりません"); return; }

        _jp = Resources.FindObjectsOfTypeAll<TMP_FontAsset>()
            .FirstOrDefault(f => f.name.ToLower() == "jp");

        // 暗幕（誤タップでQRが消えないよう、閉じるのはボタンのみ）
        _overlay = Child("__EventQrViewer", canvas.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        _overlay.AddComponent<Image>().color = UITheme.DIM;
        _overlay.transform.SetAsLastSibling();

        var border = Child("__Border", _overlay.transform,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        var brt = border.GetComponent<RectTransform>();
        brt.sizeDelta = new Vector2(904f, 1424f);
        border.AddComponent<Image>().color = C_BORDER;
        UITheme.ElevateCard(border, 18f, 10f, 0.35f);

        _panel = Child("__Panel", border.transform,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        _panel.GetComponent<RectTransform>().sizeDelta = new Vector2(880f, 1400f);
        _panel.AddComponent<Image>().color = C_PARCHMENT;

        BuildListViewAsync().Forget();
    }

    public static void Close()
    {
        if (_qrTex != null) { Object.Destroy(_qrTex); _qrTex = null; }
        if (_overlay != null) { Object.Destroy(_overlay); _overlay = null; }
        _panel = null;
        _pools = null;
    }

    // ================================================================
    // 一覧ビュー（イベントプールを選ぶ）
    // ================================================================
    private static async UniTaskVoid BuildListViewAsync()
    {
        ClearPanel();
        MakeTitle("イベントガチャQR", C_TITLE);
        var loading = MakeLabel(Child("Loading", _panel.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(0f, -400f), new Vector2(0f, -300f)).transform,
            "読み込み中...", 30, C_MUTED);

        // 開くたびに最新のプール一覧を読み直す（作りたてのイベントガチャもすぐ出す）
        _pools = await GachaService.GetPoolsAsync(forceRefresh: true);

        // 待っている間に閉じられた/開き直された場合は何もしない
        if (_panel == null || loading == null) return;
        BuildListView();
    }

    private static void BuildListView()
    {
        ClearPanel();
        if (_qrTex != null) { Object.Destroy(_qrTex); _qrTex = null; }
        _panel.GetComponent<Image>().color = C_PARCHMENT;

        MakeTitle("イベントガチャQR", C_TITLE);
        MakeLabel(Child("Hint", _panel.transform, new Vector2(0.05f, 1f), new Vector2(0.95f, 1f),
            new Vector2(0f, -200f), new Vector2(0f, -130f)).transform,
            "表示したいイベントガチャを選んでください", 26, C_MUTED, FontStyles.Normal);

        var content = MakeScrollContent(_panel.transform,
            new Vector2(0.04f, 0f), new Vector2(0.96f, 1f), new Vector2(0f, 170f), new Vector2(0f, -210f));

        var eventPools = _pools
            .Where(kv => kv.Key != GachaService.STANDARD_POOL)
            .OrderBy(kv => kv.Key)
            .ToList();

        if (eventPools.Count == 0)
        {
            var row = ListRow(content, 110f, "Empty");
            MakeLabel(row.transform, "イベントガチャがありません\n（ガチャ管理シーンで作成できます）", 26, C_MUTED, FontStyles.Normal);
        }

        foreach (var kv in eventPools)
        {
            string poolId = kv.Key;
            string name = string.IsNullOrEmpty(kv.Value?.Name) ? poolId : kv.Value.Name;
            int entryCount = kv.Value?.Entries?.Count ?? 0;

            var row = ListRow(content, 110f, $"Pool_{poolId}");
            row.AddComponent<Image>().color = C_ROW;
            var btn = row.AddComponent<Button>();
            btn.navigation = new Navigation { mode = Navigation.Mode.None };
            btn.onClick.AddListener(() => BuildQrView(poolId, name));

            var nameLabel = MakeLabel(Child("Name", row.transform, new Vector2(0f, 0f), new Vector2(0.60f, 1f),
                new Vector2(20f, 0f), Vector2.zero).transform,
                name, 30, C_TITLE, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            nameLabel.enableAutoSizing = true;
            nameLabel.fontSizeMax = 30; nameLabel.fontSizeMin = 20;
            nameLabel.enableWordWrapping = false;
            nameLabel.overflowMode = TextOverflowModes.Ellipsis;

            MakeLabel(Child("Sub", row.transform, new Vector2(0.60f, 0f), new Vector2(1f, 1f),
                Vector2.zero, new Vector2(-16f, 0f)).transform,
                $"ID: {poolId}・{entryCount}件", 20, C_MUTED, FontStyles.Normal, TextAlignmentOptions.MidlineRight);
        }

        MakeBottomButton("Close", C_CLOSE, "とじる", Color.white,
            new Vector2(0.30f, 0f), new Vector2(0.70f, 0f), Close);
    }

    // ================================================================
    // QRビュー（白背景で大きく表示）
    // ================================================================
    private static void BuildQrView(string poolId, string poolName)
    {
        ClearPanel();
        _panel.GetComponent<Image>().color = Color.white; // QRの読み取りやすさのため白背景

        var title = MakeTitle(poolName, new Color(0.15f, 0.12f, 0.10f));
        title.enableAutoSizing = true;
        title.fontSizeMax = 44; title.fontSizeMin = 26;

        string payload = $"{CallMethodFromQR.GACHA_EVENT_QR_PREFIX}:{poolId}";
        if (_qrTex != null) Object.Destroy(_qrTex);
        _qrTex = QRCodeHelper.CreateQRCode(payload, 512, 512);
        if (_qrTex != null) _qrTex.filterMode = FilterMode.Point; // 拡大表示してもドットをくっきり保つ

        var qrGO = Child("QR", _panel.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, Vector2.zero);
        var qrt = qrGO.GetComponent<RectTransform>();
        qrt.sizeDelta = new Vector2(720f, 720f);
        qrt.anchoredPosition = new Vector2(0f, -540f);
        qrGO.AddComponent<RawImage>().texture = _qrTex;

        MakeLabel(Child("Payload", _panel.transform, new Vector2(0.04f, 1f), new Vector2(0.96f, 1f),
            new Vector2(0f, -990f), new Vector2(0f, -920f)).transform,
            payload, 26, new Color(0.35f, 0.32f, 0.30f), FontStyles.Normal);

        MakeLabel(Child("Note", _panel.transform, new Vector2(0.06f, 1f), new Vector2(0.94f, 1f),
            new Vector2(0f, -1160f), new Vector2(0f, -1010f)).transform,
            "来店したお客様がアプリのQRカメラで読み取ると\nこのガチャを無料で1回引けます（読むたびに1回）",
            26, new Color(0.35f, 0.32f, 0.30f), FontStyles.Normal);

        MakeBottomButton("Back", C_BORDER, "いちらんへ戻る", C_TITLE,
            new Vector2(0.06f, 0f), new Vector2(0.48f, 0f), BuildListView);
        MakeBottomButton("Close", C_CLOSE, "とじる", Color.white,
            new Vector2(0.52f, 0f), new Vector2(0.94f, 0f), Close);
    }

    // ================================================================
    // UI部品ヘルパー
    // ================================================================
    private static void ClearPanel()
    {
        if (_panel == null) return;
        for (int i = _panel.transform.childCount - 1; i >= 0; i--)
            Object.Destroy(_panel.transform.GetChild(i).gameObject);
    }

    private static TextMeshProUGUI MakeTitle(string text, Color color)
    {
        return MakeLabel(Child("Title", _panel.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(20f, -120f), new Vector2(-20f, -24f)).transform,
            text, 44, color);
    }

    private static void MakeBottomButton(string name, Color bg, string label, Color labelColor,
        Vector2 aMin, Vector2 aMax, UnityEngine.Events.UnityAction onClick)
    {
        var go = Child(name, _panel.transform, aMin, aMax, new Vector2(0f, 36f), new Vector2(0f, 136f));
        go.AddComponent<Image>().color = bg;
        var btn = go.AddComponent<Button>();
        btn.navigation = new Navigation { mode = Navigation.Mode.None };
        btn.targetGraphic = go.GetComponent<Image>();
        btn.onClick.AddListener(onClick);
        MakeLabel(go.transform, label, 32, labelColor);
        if (bg.r + bg.g + bg.b >= 2.4f) UITheme.PolishButton(go.GetComponent<Image>());
        else UITheme.PolishDarkButton(go.GetComponent<Image>());
    }

    private static GameObject Child(string n, Transform p, Vector2 aMin, Vector2 aMax, Vector2 offMin, Vector2 offMax)
    {
        var go = new GameObject(n);
        go.transform.SetParent(p, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = aMin; rt.anchorMax = aMax;
        rt.offsetMin = offMin; rt.offsetMax = offMax;
        return go;
    }

    private static GameObject ListRow(Transform parent, float h, string n)
    {
        var go = new GameObject(n);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        var le = go.AddComponent<LayoutElement>();
        le.preferredHeight = h; le.minHeight = h;
        return go;
    }

    /// <summary>縦スクロールの一覧を作り、行を並べる Content を返す（GachaAdmin と同じ流儀）。</summary>
    private static Transform MakeScrollContent(Transform parent, Vector2 aMin, Vector2 aMax, Vector2 offMin, Vector2 offMax)
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

        var ct = new GameObject("Content");
        ct.transform.SetParent(vp.transform, false);
        var ctrt = ct.AddComponent<RectTransform>();
        ctrt.anchorMin = new Vector2(0, 1); ctrt.anchorMax = new Vector2(1, 1);
        ctrt.pivot = new Vector2(0.5f, 1f);
        ctrt.sizeDelta = Vector2.zero;
        ctrt.anchoredPosition = Vector2.zero;
        var vlg = ct.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(8, 8, 8, 8);
        vlg.spacing = 10f;
        vlg.childControlWidth = true;  vlg.childForceExpandWidth = true;
        vlg.childControlHeight = true; vlg.childForceExpandHeight = false;
        ct.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.viewport = vp.GetComponent<RectTransform>();
        scroll.content = ctrt;
        return ct.transform;
    }

    private static TextMeshProUGUI MakeLabel(Transform p, string text, float size, Color c,
        FontStyles style = FontStyles.Bold, TextAlignmentOptions align = TextAlignmentOptions.Center)
    {
        var go = Child("L", p, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var t = go.AddComponent<TextMeshProUGUI>();
        t.text = text; t.fontSize = size; t.fontStyle = style;
        t.alignment = align; t.color = c; t.raycastTarget = false;
        t.enableWordWrapping = true;
        if (_jp != null) t.font = _jp;
        return t;
    }
}
