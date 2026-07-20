using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 引いたバフカードを画像付きで表示するモーダル（コード生成・羊皮紙スタイル）。
/// QRで山札から1枚引いたときに「○○カードを引いた！」と画像・効果を見せる。
/// </summary>
public static class BuffCardModal
{
    private static readonly Color C_DIM     = UITheme.DIM; // モーダル暗幕（デザイントークン）
    private static readonly Color C_BORDER  = new Color(0.84f, 0.66f, 0.18f, 1f);
    private static readonly Color C_BG      = new Color(0.99f, 0.95f, 0.84f, 0.98f);
    private static readonly Color C_TITLE   = new Color(0.38f, 0.16f, 0.04f, 1f);
    private static readonly Color C_EFFECT  = new Color(0.30f, 0.18f, 0.04f, 1f);
    private static readonly Color C_BTN     = new Color(0.48f, 0.26f, 0.06f, 1f);

    public static void Show(BuffCardType card)
    {
        var canvasGO = GameObject.Find("Canvas");
        var canvas = canvasGO != null ? canvasGO.GetComponent<Canvas>()
                                      : Object.FindFirstObjectByType<Canvas>();
        if (canvas == null) { Debug.LogWarning("[BuffCardModal] Canvasが見つかりません"); return; }

        var jp = Resources.FindObjectsOfTypeAll<TMP_FontAsset>()
            .FirstOrDefault(f => f.name.ToLower() == "jp");

        var dim = new GameObject("__BuffCardModal");
        dim.transform.SetParent(canvas.transform, false);
        var drt = dim.AddComponent<RectTransform>();
        drt.anchorMin = Vector2.zero; drt.anchorMax = Vector2.one;
        drt.offsetMin = drt.offsetMax = Vector2.zero;
        dim.AddComponent<Image>().color = C_DIM;
        dim.transform.SetAsLastSibling();

        var border = MakeRect("__Border", dim.transform, C_BORDER, 760, 1040);
        var panel  = MakeRect("__Panel", border.transform, C_BG, 744, 1024);
        UITheme.ElevateCard(border, 18f, 10f, 0.35f); // モーダルを浮かせる

        MakeLabel(panel.transform, "カードを引いた！", jp, 46, FontStyles.Bold, C_TITLE,
            700, 70, new Vector2(0, 430));

        // カード画像
        var tex = BuffCard.LoadImage(card);
        var imgGO = new GameObject("__CardImg");
        imgGO.transform.SetParent(panel.transform, false);
        var irt = imgGO.AddComponent<RectTransform>();
        irt.sizeDelta = new Vector2(560, 560);
        irt.anchoredPosition = new Vector2(0, 90);
        var raw = imgGO.AddComponent<RawImage>();
        if (tex != null) { raw.texture = tex; FitRawImage(raw, tex, 560, 560); }
        else raw.color = new Color(0.8f, 0.75f, 0.6f);
        UITheme.ElevateCard(imgGO, 12f, 6f, 0.22f); // カード絵を浮かせる

        MakeLabel(panel.transform, BuffCard.DisplayName(card), jp, 50, FontStyles.Bold, C_TITLE,
            700, 70, new Vector2(0, -230));
        MakeLabel(panel.transform, BuffCard.EffectText(card), jp, 30, FontStyles.Normal, C_EFFECT,
            680, 140, new Vector2(0, -330));
        MakeLabel(panel.transform, "次のボス戦で自動的に発動します", jp, 26, FontStyles.Italic,
            new Color(0.5f, 0.36f, 0.18f), 680, 50, new Vector2(0, -410));

        var btn = MakeRect("__Close", panel.transform, C_BTN, 360, 96);
        btn.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, -460);
        MakeLabel(btn.transform, "とじる", jp, 38, FontStyles.Bold, Color.white, 360, 96, Vector2.zero);
        btn.AddComponent<Button>().onClick.AddListener(() => Object.Destroy(dim));
        UITheme.PolishDarkButton(btn.GetComponent<Image>()); // 濃茶ボタン（r+g+b<2.4）

        var dimBtn = dim.AddComponent<Button>();
        dimBtn.transition = Selectable.Transition.None;
        dimBtn.onClick.AddListener(() => Object.Destroy(dim));
    }

    /// <summary>
    /// RawImage を、テクスチャ本来の縦横比を保ったまま maxW×maxH のボックス内に収まるサイズにする。
    /// （RawImage はそのままだと矩形に引き伸ばされて画像がつぶれるため）
    /// </summary>
    public static void FitRawImage(RawImage raw, Texture tex, float maxW, float maxH)
    {
        if (raw == null || tex == null || tex.height == 0) return;
        float aspect = tex.width / (float)tex.height;
        float w = maxW, h = maxW / aspect;
        if (h > maxH) { h = maxH; w = maxH * aspect; }
        raw.rectTransform.sizeDelta = new Vector2(w, h);
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
        tmp.text = text; tmp.fontSize = size; tmp.fontStyle = style;
        tmp.color = color; tmp.alignment = TextAlignmentOptions.Center;
        tmp.enableWordWrapping = true;
        tmp.raycastTarget = false;
    }
}
