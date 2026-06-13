using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 履歴表示UI（コード生成・羊皮紙スタイル）。
/// キャラクターページ右上の「履歴」ボタンから開き、
/// LocalHistoryLog の内容（最大50件）を新しい順にスクロールリストで表示する。
/// </summary>
public class HistoryController : MonoBehaviour
{
    private static readonly Color C_PARCHMENT = new Color(0.99f, 0.95f, 0.84f, 0.98f);
    private static readonly Color C_BORDER    = new Color(0.84f, 0.66f, 0.18f, 1.00f);
    private static readonly Color C_TITLE     = new Color(0.38f, 0.16f, 0.04f, 1.00f);
    private static readonly Color C_DIVIDER   = new Color(0.80f, 0.62f, 0.18f, 0.70f);
    private static readonly Color C_ROW       = new Color(0.93f, 0.87f, 0.70f, 0.90f);
    private static readonly Color C_CLOSE_BTN = new Color(0.90f, 0.32f, 0.32f, 1.00f);
    private static readonly Color C_MUTED     = new Color(0.52f, 0.38f, 0.22f, 0.85f);

    private const float ROW_W = 840f;
    private const float ROW_H = 120f;
    private const float ROW_GAP = 14f;

    private Canvas _canvas;
    private GameObject _overlay;
    private RectTransform _listContent;

    private void Start()
    {
        var canvasGO = GameObject.Find("Canvas");
        _canvas = canvasGO != null ? canvasGO.GetComponent<Canvas>() : FindFirstObjectByType<Canvas>();
        if (_canvas == null) return;

        BuildEntryButton();
        BuildPanel();
    }

    /// <summary>履歴タイプごとの表示名と色</summary>
    private static (string label, Color color) TypeInfo(string type)
    {
        switch (type)
        {
            case "dice":   return ("ダイス",   new Color(0.20f, 0.45f, 0.75f));
            case "gacha":  return ("ガチャ",   new Color(0.72f, 0.28f, 0.55f));
            case "friend": return ("フレンド", new Color(0.18f, 0.55f, 0.28f));
            case "visit":  return ("滞在",     new Color(0.55f, 0.40f, 0.12f));
            case "coupon": return ("クーポン", new Color(0.80f, 0.38f, 0.10f));
            case "group":  return ("グループ", new Color(0.15f, 0.50f, 0.60f));
            default:       return ("その他",   new Color(0.52f, 0.38f, 0.22f));
        }
    }

    // ================================================================
    // 開閉
    // ================================================================
    private void Show()
    {
        RebuildList();
        _overlay.SetActive(true);
    }

    private void Hide()
    {
        _overlay.SetActive(false);
    }

    // ================================================================
    // UI構築
    // ================================================================

    /// <summary>キャラクターページ右上の「履歴」ボタン（下部ボタンと同じ白角丸スタイル）</summary>
    private void BuildEntryButton()
    {
        var jp = GetJpFont();
        var ink = GachaController.GRID_TEXT_COLOR;

        // 位置は画面（Canvas）基準の右上で決める
        var btnGO = new GameObject("__HistoryButton");
        btnGO.transform.SetParent(_canvas.transform, false);
        var rt = btnGO.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(-30, -280); // リロードボタンの下
        rt.sizeDelta = new Vector2(200, 86);
        var bg = btnGO.AddComponent<Image>();
        GachaController.ApplyGridButtonStyle(bg);

        // Page_Characters の子へ移動（キャラクターページ表示中のみ表示される）
        var page = _canvas.transform.Find("Page_Characters");
        if (page != null)
        {
            btnGO.transform.SetParent(page, true);
            btnGO.transform.SetAsLastSibling();
        }
        else
        {
            Debug.LogWarning("[History] Page_Characters が見つからないため、Canvas直下にボタンを置きます");
        }

        // 時計アイコン（濃グレーの文字盤＋羊皮紙色の針）
        var face = MakeCircle("__Clock", btnGO.transform, ink, 44);
        face.GetComponent<RectTransform>().anchoredPosition = new Vector2(-62, 0);
        MakeRect("__HandV", face.transform, C_PARCHMENT, 5, 15)
            .GetComponent<RectTransform>().anchoredPosition = new Vector2(0, 7);
        MakeRect("__HandH", face.transform, C_PARCHMENT, 12, 5)
            .GetComponent<RectTransform>().anchoredPosition = new Vector2(5, 0);

        MakeLabel(btnGO.transform, "履歴", jp, 50, FontStyles.Bold, ink, 120, 86, new Vector2(26, 0));

        var btn = btnGO.AddComponent<Button>();
        btn.targetGraphic = bg;
        btn.onClick.AddListener(Show);
    }

    private void BuildPanel()
    {
        var jp = GetJpFont();

        _overlay = new GameObject("__HistoryOverlay");
        _overlay.transform.SetParent(_canvas.transform, false);
        var ort = _overlay.AddComponent<RectTransform>();
        ort.anchorMin = Vector2.zero; ort.anchorMax = Vector2.one;
        ort.offsetMin = ort.offsetMax = Vector2.zero;
        _overlay.AddComponent<Image>().color = new Color(0.08f, 0.04f, 0.20f, 0.68f);
        _overlay.SetActive(false);

        var border = MakeRect("__Border", _overlay.transform, C_BORDER, 936, 1316);
        var panel  = MakeRect("__Panel", border.transform, C_PARCHMENT, 920, 1300);

        MakeLabel(panel.transform, "履歴", jp, 52, FontStyles.Bold, C_TITLE,
            700, 90, new Vector2(0, 585));
        MakeButton("__Close", panel.transform, C_CLOSE_BTN, "✕", jp, 52, Color.white,
            96, 96, new Vector2(396, 590), Hide);
        MakeRect("__Div", panel.transform, C_DIVIDER, 820, 4)
            .GetComponent<RectTransform>().anchoredPosition = new Vector2(0, 525);

        // ---- スクロールリスト ----
        var scrollGO = new GameObject("__ScrollArea");
        scrollGO.transform.SetParent(panel.transform, false);
        var srt = scrollGO.AddComponent<RectTransform>();
        srt.sizeDelta = new Vector2(880, 1040);
        srt.anchoredPosition = new Vector2(0, -35);
        var scroll = scrollGO.AddComponent<ScrollRect>();

        var viewportGO = new GameObject("__Viewport");
        viewportGO.transform.SetParent(scrollGO.transform, false);
        var vrt = viewportGO.AddComponent<RectTransform>();
        vrt.anchorMin = Vector2.zero; vrt.anchorMax = Vector2.one;
        vrt.offsetMin = vrt.offsetMax = Vector2.zero;
        viewportGO.AddComponent<RectMask2D>();

        var contentGO = new GameObject("__Content");
        contentGO.transform.SetParent(viewportGO.transform, false);
        _listContent = contentGO.AddComponent<RectTransform>();
        _listContent.anchorMin = new Vector2(0f, 1f);
        _listContent.anchorMax = new Vector2(1f, 1f);
        _listContent.pivot = new Vector2(0.5f, 1f);
        _listContent.sizeDelta = new Vector2(0, 100);

        scroll.viewport = vrt;
        scroll.content = _listContent;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 30;

        MakeLabel(panel.transform, $"さいきんの{LocalHistoryLog.MAX_ENTRIES}件まで きろくされます",
            jp, 26, FontStyles.Normal, C_MUTED, 800, 40, new Vector2(0, -610));
    }

    /// <summary>履歴リストを最新の保存内容で作り直す（開くたびに呼ぶ）</summary>
    private void RebuildList()
    {
        for (int i = _listContent.childCount - 1; i >= 0; i--)
            Destroy(_listContent.GetChild(i).gameObject);

        var jp = GetJpFont();
        var entries = LocalHistoryLog.GetAllNewestFirst();

        if (entries.Count == 0)
        {
            MakeLabel(_listContent, "まだ履歴がありません", jp, 36, FontStyles.Bold, C_MUTED,
                700, 80, new Vector2(0, -120));
            _listContent.sizeDelta = new Vector2(0, 240);
            return;
        }

        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            var (typeLabel, typeColor) = TypeInfo(e.type);

            var row = MakeRect($"__Row{i}", _listContent, C_ROW, ROW_W, ROW_H);
            RoundedRectSprite.Apply(row.GetComponent<Image>());
            var rrt = row.GetComponent<RectTransform>();
            rrt.anchorMin = rrt.anchorMax = new Vector2(0.5f, 1f);
            rrt.anchoredPosition = new Vector2(0, -(i * (ROW_H + ROW_GAP)) - ROW_H / 2f - 10f);

            MakeLabel(row.transform, $"【{typeLabel}】", jp, 30, FontStyles.Bold, typeColor,
                220, 40, new Vector2(-295, 30));
            MakeLabel(row.transform, e.date, jp, 24, FontStyles.Normal, C_MUTED,
                280, 40, new Vector2(265, 30));

            var text = MakeLabel(row.transform, e.text, jp, 34, FontStyles.Normal, C_TITLE,
                800, 54, new Vector2(0, -25));
            var tmp = text.GetComponent<TextMeshProUGUI>();
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMax = 34;
            tmp.fontSizeMin = 20;
        }

        float height = entries.Count * (ROW_H + ROW_GAP) + 20f;
        _listContent.sizeDelta = new Vector2(0, height);
        _listContent.anchoredPosition = Vector2.zero; // 開くたびに先頭へスクロール
    }

    // ================================================================
    // UI部品ヘルパー（DiceRollControllerと同形式）
    // ================================================================
    private static TMP_FontAsset GetJpFont()
    {
        var fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
        return fonts.FirstOrDefault(f => f.name.ToLower() == "jp") ?? fonts.FirstOrDefault();
    }

    private static GameObject MakeRect(string name, Transform parent, Color color, float w, float h)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>().sizeDelta = new Vector2(w, h);
        go.AddComponent<Image>().color = color;
        return go;
    }

    private static GameObject MakeCircle(string name, Transform parent, Color color, float d)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>().sizeDelta = new Vector2(d, d);
        var img = go.AddComponent<Image>();
        UICircleSprite.Apply(img);
        img.color = color;
        img.raycastTarget = false;
        return go;
    }

    private static GameObject MakeLabel(Transform parent, string text, TMP_FontAsset font,
        float size, FontStyles style, Color color, float w, float h, Vector2 pos)
    {
        var go = new GameObject("__Label");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(w, h);
        rt.anchoredPosition = pos;
        var tmp = go.AddComponent<TextMeshProUGUI>();
        if (font != null) tmp.font = font;
        tmp.text = text;
        tmp.fontSize = size;
        tmp.fontStyle = style;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;
        return go;
    }

    private static GameObject MakeButton(string name, Transform parent, Color bg, string text,
        TMP_FontAsset font, float fontSize, Color textColor, float w, float h, Vector2 pos,
        UnityEngine.Events.UnityAction onClick)
    {
        var go = MakeRect(name, parent, bg, w, h);
        go.GetComponent<RectTransform>().anchoredPosition = pos;
        MakeLabel(go.transform, text, font, fontSize, FontStyles.Bold, textColor, w, h, Vector2.zero);
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = go.GetComponent<Image>();
        btn.onClick.AddListener(onClick);
        return go;
    }
}
