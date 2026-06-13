using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 汎用のお知らせモーダル（コード生成UI・羊皮紙スタイル）。
/// タイトル＋強調テキスト＋補足テキストを表示し、ボタンか背景タップで閉じる。
/// 例: 会員証引き継ぎ完了の通知など。
/// </summary>
public static class InfoModal
{
    private static readonly Color C_PARCHMENT = new Color(0.99f, 0.95f, 0.84f, 0.98f);
    private static readonly Color C_BORDER    = new Color(0.84f, 0.66f, 0.18f, 1.00f);
    private static readonly Color C_TITLE     = new Color(0.38f, 0.16f, 0.04f, 1.00f);
    private static readonly Color C_DIVIDER   = new Color(0.80f, 0.62f, 0.18f, 0.70f);
    private static readonly Color C_STRONG    = new Color(0.55f, 0.30f, 0.02f, 1.00f);
    private static readonly Color C_BTN       = new Color(0.48f, 0.26f, 0.06f, 1.00f);

    /// <summary>
    /// モーダルを表示する。
    /// </summary>
    /// <param name="title">上部タイトル</param>
    /// <param name="strongText">太字・大きめで強調する本文</param>
    /// <param name="subText">補足テキスト（null/空なら非表示）</param>
    public static void Show(string title, string strongText, string subText = null)
    {
        var canvasGO = GameObject.Find("Canvas");
        var canvas = canvasGO != null ? canvasGO.GetComponent<Canvas>()
                                      : Object.FindFirstObjectByType<Canvas>();
        if (canvas == null)
        {
            Debug.LogWarning($"[InfoModal] Canvasが見つかりません: {title} / {strongText}");
            return;
        }

        var jp = Resources.FindObjectsOfTypeAll<TMP_FontAsset>()
            .FirstOrDefault(f => f.name.ToLower() == "jp");

        // 暗幕
        var dim = new GameObject("__InfoModal");
        dim.transform.SetParent(canvas.transform, false);
        var drt = dim.AddComponent<RectTransform>();
        drt.anchorMin = Vector2.zero; drt.anchorMax = Vector2.one;
        drt.offsetMin = drt.offsetMax = Vector2.zero;
        dim.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);

        // パネル
        var border = MakeRect("__Border", dim.transform, C_BORDER, 792, 616);
        var panel  = MakeRect("__Panel", border.transform, C_PARCHMENT, 776, 600);

        MakeLabel(panel.transform, title, jp, 44, FontStyles.Bold, C_TITLE, 700, 80, new Vector2(0, 196));

        MakeRect("__Div", panel.transform, C_DIVIDER, 660, 4)
            .GetComponent<RectTransform>().anchoredPosition = new Vector2(0, 144);

        MakeLabel(panel.transform, strongText, jp, 52, FontStyles.Bold, C_STRONG, 720, 150, new Vector2(0, 10));

        if (!string.IsNullOrEmpty(subText))
            MakeLabel(panel.transform, subText, jp, 32, FontStyles.Normal, C_TITLE, 700, 90, new Vector2(0, -90));

        var btnGO = MakeRect("__Close", panel.transform, C_BTN, 360, 100);
        btnGO.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, -200);
        MakeLabel(btnGO.transform, "とじる", jp, 40, FontStyles.Bold, Color.white, 360, 100, Vector2.zero);
        btnGO.AddComponent<Button>().onClick.AddListener(() => Object.Destroy(dim));

        var dimBtn = dim.AddComponent<Button>();
        dimBtn.transition = Selectable.Transition.None;
        dimBtn.onClick.AddListener(() => Object.Destroy(dim));
    }

    private static GameObject MakeRect(string name, Transform parent, Color color, float w, float h)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>().sizeDelta = new Vector2(w, h);
        go.AddComponent<Image>().color = color;
        return go;
    }

    private static void MakeLabel(Transform parent, string text, TMP_FontAsset font,
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
    }
}
