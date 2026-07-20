using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 管理者（店員）用のバフカード山札ビルダー（別シーン・コード生成UI）。
/// 各カードの枚数を ＋／− で決め、「QR生成」で山札QR（"deck:soft=10,..."）を作って表示する。
/// 客がそのQRを読むと、枚数を重みに1枚抽選して手札に持ち、次のボス戦で自動発動する。
/// </summary>
public class BuffCardDeckBuilderManager : MonoBehaviour
{
    private readonly Dictionary<BuffCardType, int> _counts = new Dictionary<BuffCardType, int>();
    private readonly Dictionary<BuffCardType, TextMeshProUGUI> _countLabels = new Dictionary<BuffCardType, TextMeshProUGUI>();
    private TextMeshProUGUI _totalLabel;
    private TextMeshProUGUI _qrStringLabel;
    private RawImage _qrImage;
    private ScrollRect _scroll;
    private TMP_FontAsset _jp;

    private static readonly Color C_BG     = new Color(0.12f, 0.10f, 0.08f);
    private static readonly Color C_PANEL  = new Color(0.20f, 0.16f, 0.12f);
    private static readonly Color C_GOLD   = new Color(0.92f, 0.74f, 0.24f);
    private static readonly Color C_TEXT   = new Color(0.96f, 0.92f, 0.80f);
    private static readonly Color C_MINUS  = new Color(0.55f, 0.25f, 0.22f);
    private static readonly Color C_PLUS   = new Color(0.22f, 0.45f, 0.28f);
    private static readonly Color C_GEN    = new Color(0.24f, 0.36f, 0.58f);

    private void Start()
    {
        _jp = Resources.FindObjectsOfTypeAll<TMP_FontAsset>().FirstOrDefault(f => f.name.ToLower() == "jp")
              ?? Resources.FindObjectsOfTypeAll<TMP_FontAsset>().FirstOrDefault();
        foreach (var t in BuffCard.AllTypes) _counts[t] = 0;
        EnsureEventSystem();
        BuildUI(CreateCanvas());
        RefreshTotals();
    }

    // ================================================================
    // ブートストラップ
    // ================================================================
    private static void EnsureEventSystem()
    {
        if (Object.FindFirstObjectByType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
        }
    }

    private Canvas CreateCanvas()
    {
        var go = new GameObject("__DeckBuilderCanvas");
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        go.AddComponent<GraphicRaycaster>();
        return canvas;
    }

    // ================================================================
    // UI 構築
    // ================================================================
    private void BuildUI(Canvas canvas)
    {
        // 背景
        var bg = MakeRect("__BG", canvas.transform, C_BG, 0, 0);
        var bgRt = bg.GetComponent<RectTransform>();
        bgRt.anchorMin = Vector2.zero; bgRt.anchorMax = Vector2.one;
        bgRt.offsetMin = bgRt.offsetMax = Vector2.zero;

        // タイトル（固定）
        MakeText("__Title", canvas.transform, "バフカード 山札ビルダー", 54, FontStyles.Bold, C_GOLD,
            new Vector2(0, -50), new Vector2(1000, 90), TextAlignmentOptions.Center, anchorTop: true);

        // Homeへ戻る（右上。GachaAdmin と同じ流儀）
        var backGO = MakeButton("__Back", canvas.transform, new Color(0.86f, 0.28f, 0.28f), "✕ Home", 32,
            Vector2.zero, new Vector2(200, 84));
        var backRt = backGO.GetComponent<RectTransform>();
        backRt.anchorMin = backRt.anchorMax = new Vector2(1f, 1f);
        backRt.pivot = new Vector2(1f, 1f);
        backRt.anchoredPosition = new Vector2(-20f, -20f);
        backGO.GetComponent<Button>().onClick.AddListener(
            () => UnityEngine.SceneManagement.SceneManager.LoadScene("Home"));

        // スクロール領域（タイトル下〜画面下）。カードを大きく取り、はみ出しはスクロールで見る。
        var content = CreateScroll(canvas.transform);

        float y = 24f; // contentの上端からの距離（下方向に積む）
        const float rowH = 240f, gap = 20f, rowW = 1000f;
        foreach (var t in BuffCard.AllTypes)
        {
            BuildCardRow(content, t, -y, rowW, rowH);
            y += rowH + gap;
        }

        y += 24f;
        _totalLabel = MakeText("__Total", content, "合計 0 枚", 46, FontStyles.Bold, C_TEXT,
            new Vector2(0, -y), new Vector2(1000, 70), TextAlignmentOptions.Center, anchorTop: true);
        y += 110f;

        var genBtn = MakeButton("__Gen", content, C_GEN, "QRを生成する", 50,
            new Vector2(0, -y), new Vector2(620, 130), anchorTop: true);
        genBtn.GetComponent<Button>().onClick.AddListener(GenerateQR);
        y += 170f;

        var clearBtn = MakeButton("__Clear", content, C_MINUS, "全部0に戻す", 38,
            new Vector2(0, -y), new Vector2(420, 96), anchorTop: true);
        clearBtn.GetComponent<Button>().onClick.AddListener(ClearAll);
        y += 140f;

        // QR表示エリア（生成後に出る）
        var qrGO = new GameObject("__QR");
        qrGO.transform.SetParent(content, false);
        var qrRt = qrGO.AddComponent<RectTransform>();
        qrRt.anchorMin = qrRt.anchorMax = new Vector2(0.5f, 1f);
        qrRt.pivot = new Vector2(0.5f, 1f);
        qrRt.anchoredPosition = new Vector2(0, -y);
        qrRt.sizeDelta = new Vector2(560, 560);
        _qrImage = qrGO.AddComponent<RawImage>();
        _qrImage.color = new Color(1, 1, 1, 0); // 生成までは透明
        y += 600f;

        _qrStringLabel = MakeText("__QRStr", content, "", 26, FontStyles.Normal, new Color(0.7f, 0.7f, 0.7f),
            new Vector2(0, -y), new Vector2(1000, 90), TextAlignmentOptions.Center, anchorTop: true);
        y += 120f;

        // content の高さを確定（これでスクロール範囲が決まる）
        content.GetComponent<RectTransform>().sizeDelta = new Vector2(0, y);
    }

    /// <summary>縦スクロール領域を作り、カード等を載せる content の Transform を返す。</summary>
    private Transform CreateScroll(Transform parent)
    {
        var scrollGO = new GameObject("__Scroll");
        scrollGO.transform.SetParent(parent, false);
        var sRt = scrollGO.AddComponent<RectTransform>();
        sRt.anchorMin = Vector2.zero; sRt.anchorMax = Vector2.one;
        sRt.offsetMin = new Vector2(20, 20);     // 左・下の余白
        sRt.offsetMax = new Vector2(-20, -130);  // 上はタイトルぶん空ける
        _scroll = scrollGO.AddComponent<ScrollRect>();
        _scroll.horizontal = false; _scroll.vertical = true;
        _scroll.movementType = ScrollRect.MovementType.Clamped;
        _scroll.scrollSensitivity = 40f;

        var viewport = new GameObject("__Viewport");
        viewport.transform.SetParent(scrollGO.transform, false);
        var vRt = viewport.AddComponent<RectTransform>();
        vRt.anchorMin = Vector2.zero; vRt.anchorMax = Vector2.one;
        vRt.offsetMin = vRt.offsetMax = Vector2.zero;
        vRt.pivot = new Vector2(0.5f, 1f);
        viewport.AddComponent<Image>().color = new Color(0, 0, 0, 0.0039f); // Maskに必要な極薄背景
        viewport.AddComponent<RectMask2D>();

        var contentGO = new GameObject("__Content");
        contentGO.transform.SetParent(viewport.transform, false);
        var cRt = contentGO.AddComponent<RectTransform>();
        cRt.anchorMin = new Vector2(0, 1); cRt.anchorMax = new Vector2(1, 1);
        cRt.pivot = new Vector2(0.5f, 1f);
        cRt.anchoredPosition = Vector2.zero;
        cRt.sizeDelta = new Vector2(0, 100);

        _scroll.viewport = vRt;
        _scroll.content = cRt;
        return contentGO.transform;
    }

    private void BuildCardRow(Transform parent, BuffCardType t, float y, float w, float h)
    {
        var row = MakeRect($"__Row_{t}", parent, C_PANEL, w, h);
        var rRt = row.GetComponent<RectTransform>();
        rRt.anchorMin = rRt.anchorMax = new Vector2(0.5f, 1f);
        rRt.pivot = new Vector2(0.5f, 1f);
        rRt.anchoredPosition = new Vector2(0, y);

        // サムネイル（大きめ）
        var tex = BuffCard.LoadImage(t);
        var img = new GameObject("__Thumb");
        img.transform.SetParent(row.transform, false);
        var iRt = img.AddComponent<RectTransform>();
        iRt.anchorMin = iRt.anchorMax = new Vector2(0, 0.5f);
        iRt.pivot = new Vector2(0, 0.5f);
        iRt.anchoredPosition = new Vector2(24, 0);
        iRt.sizeDelta = new Vector2(200, 200);
        var raw = img.AddComponent<RawImage>();
        if (tex != null) { raw.texture = tex; BuffCardModal.FitRawImage(raw, tex, 200, 200); }
        else raw.color = new Color(0.4f, 0.36f, 0.3f);

        // 名前（サムネの右、ボタンの左）
        MakeText($"__Name_{t}", row.transform, BuffCard.DisplayName(t), 40, FontStyles.Bold, C_TEXT,
            new Vector2(250, 0), new Vector2(260, h), TextAlignmentOptions.Left, anchorLeftMid: true);

        // − / 数 / ＋
        var minus = MakeButton($"__Minus_{t}", row.transform, C_MINUS, "−", 56,
            new Vector2(-360, 0), new Vector2(120, 120), anchorRightMid: true);
        minus.GetComponent<Button>().onClick.AddListener(() => Add(t, -1));

        _countLabels[t] = MakeText($"__Cnt_{t}", row.transform, "0", 60, FontStyles.Bold, C_GOLD,
            new Vector2(-200, 0), new Vector2(150, h), TextAlignmentOptions.Center, anchorRightMid: true);

        var plus = MakeButton($"__Plus_{t}", row.transform, C_PLUS, "＋", 56,
            new Vector2(-40, 0), new Vector2(120, 120), anchorRightMid: true);
        plus.GetComponent<Button>().onClick.AddListener(() => Add(t, 1));
    }

    // ================================================================
    // 操作
    // ================================================================
    private void Add(BuffCardType t, int delta)
    {
        _counts[t] = Mathf.Max(0, _counts[t] + delta);
        _countLabels[t].text = _counts[t].ToString();
        RefreshTotals();
    }

    private void ClearAll()
    {
        foreach (var t in _counts.Keys.ToList()) { _counts[t] = 0; _countLabels[t].text = "0"; }
        RefreshTotals();
        _qrImage.color = new Color(1, 1, 1, 0);
        _qrStringLabel.text = "";
    }

    private void RefreshTotals()
    {
        int total = _counts.Values.Sum();
        if (_totalLabel != null) _totalLabel.text = $"合計 {total} 枚";
    }

    private void GenerateQR()
    {
        int total = _counts.Values.Sum();
        if (total <= 0) { _qrStringLabel.text = "1枚以上にしてください"; return; }

        string code = BuffCard.EncodeDeck(_counts);
        var tex = QRCodeHelper.CreateQRCode(code, 512, 512);
        _qrImage.texture = tex;
        _qrImage.color = Color.white;
        _qrStringLabel.text = code;
        if (_scroll != null) _scroll.verticalNormalizedPosition = 0f; // 一番下のQRまでスクロール
        Debug.Log($"[DeckBuilder] QR生成: {code}");
    }

    // ================================================================
    // UI ヘルパー
    // ================================================================
    private GameObject MakeRect(string name, Transform parent, Color color, float w, float h)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(w, h);
        go.AddComponent<Image>().color = color;
        return go;
    }

    private TextMeshProUGUI MakeText(string name, Transform parent, string text, float size,
        FontStyles style, Color color, Vector2 pos, Vector2 sd, TextAlignmentOptions align,
        bool anchorTop = false, bool anchorLeftMid = false, bool anchorRightMid = false)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        if (anchorTop)        { rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f); rt.pivot = new Vector2(0.5f, 1f); }
        else if (anchorLeftMid)  { rt.anchorMin = rt.anchorMax = new Vector2(0, 0.5f); rt.pivot = new Vector2(0, 0.5f); }
        else if (anchorRightMid) { rt.anchorMin = rt.anchorMax = new Vector2(1, 0.5f); rt.pivot = new Vector2(1, 0.5f); }
        rt.anchoredPosition = pos;
        rt.sizeDelta = sd;
        var tmp = go.AddComponent<TextMeshProUGUI>();
        if (_jp != null) tmp.font = _jp;
        tmp.text = text; tmp.fontSize = size; tmp.fontStyle = style;
        tmp.color = color; tmp.alignment = align;
        tmp.enableWordWrapping = true;
        tmp.raycastTarget = false;
        return tmp;
    }

    private GameObject MakeButton(string name, Transform parent, Color color, string label, float size,
        Vector2 pos, Vector2 sd, bool anchorTop = false, bool anchorRightMid = false)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        if (anchorTop)           { rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f); rt.pivot = new Vector2(0.5f, 1f); }
        else if (anchorRightMid) { rt.anchorMin = rt.anchorMax = new Vector2(1, 0.5f); rt.pivot = new Vector2(1, 0.5f); }
        rt.anchoredPosition = pos;
        rt.sizeDelta = sd;
        go.AddComponent<Image>().color = color;
        go.AddComponent<Button>();
        var lab = new GameObject("__L");
        lab.transform.SetParent(go.transform, false);
        var lrt = lab.AddComponent<RectTransform>();
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
        lrt.offsetMin = lrt.offsetMax = Vector2.zero;
        var tmp = lab.AddComponent<TextMeshProUGUI>();
        if (_jp != null) tmp.font = _jp;
        tmp.text = label; tmp.fontSize = size; tmp.fontStyle = FontStyles.Bold;
        tmp.color = Color.white; tmp.alignment = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;
        return go;
    }
}
