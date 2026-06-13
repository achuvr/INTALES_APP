using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Home画面に「図鑑」ボタンを追加する（コード生成）。
/// キャラクターページ右上（履歴ボタンの下）に置き、押すと図鑑シーン（Zukan）を
/// Homeに加算マージして開く。下部ボタンと同じ白角丸スタイルで統一する。
/// </summary>
public class ZukanButton : MonoBehaviour
{
    private Canvas _canvas;

    private void Start()
    {
        var canvasGO = GameObject.Find("Canvas");
        _canvas = canvasGO != null ? canvasGO.GetComponent<Canvas>() : FindFirstObjectByType<Canvas>();
        if (_canvas == null) return;
        BuildButton();
    }

    private void BuildButton()
    {
        var jp = GetJpFont();
        var ink = GachaController.GRID_TEXT_COLOR;

        var btnGO = new GameObject("__ZukanButton");
        btnGO.transform.SetParent(_canvas.transform, false);
        var rt = btnGO.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(-30, -380); // 履歴ボタン(-280)の下
        rt.sizeDelta = new Vector2(200, 86);
        var bg = btnGO.AddComponent<Image>();
        GachaController.ApplyGridButtonStyle(bg);

        // キャラクターページ表示中のみ出す（履歴・フレンドと同様）
        var page = _canvas.transform.Find("Page_Characters");
        if (page != null)
        {
            btnGO.transform.SetParent(page, true);
            btnGO.transform.SetAsLastSibling();
        }

        // 本のアイコン（こげ茶の表紙＋背表紙ライン）
        BuildBookIcon(btnGO.transform, ink, new Vector2(-62, 0));

        MakeLabel(btnGO.transform, "図鑑", jp, 44, FontStyles.Bold, ink, 120, 86, new Vector2(26, 0));

        var btn = btnGO.AddComponent<Button>();
        btn.targetGraphic = bg;
        btn.onClick.AddListener(OpenZukan);
    }

    /// <summary>図鑑を開く（多重オープン防止つき）</summary>
    private void OpenZukan()
    {
        if (FindObjectOfType<ZukanController>() != null) return; // 既に開いている

        if (SceneLoader.instance == null)
            new GameObject("SceneLoader").AddComponent<SceneLoader>();
        SceneLoader.instance.MergeScene("Zukan");
    }

    /// <summary>簡易の本アイコン（角丸の表紙＋中央の背表紙ライン）</summary>
    private static void BuildBookIcon(Transform parent, Color ink, Vector2 pos)
    {
        var cover = new GameObject("__BookIcon");
        cover.transform.SetParent(parent, false);
        var crt = cover.AddComponent<RectTransform>();
        crt.sizeDelta = new Vector2(50, 60);
        crt.anchoredPosition = pos;
        var cimg = cover.AddComponent<Image>();
        RoundedRectSprite.Apply(cimg);
        cimg.color = ink;
        cimg.raycastTarget = false;

        // ページ（表紙より少し小さい明色）
        var page = new GameObject("__Page");
        page.transform.SetParent(cover.transform, false);
        var prt = page.AddComponent<RectTransform>();
        prt.sizeDelta = new Vector2(38, 48);
        prt.anchoredPosition = new Vector2(4, 0);
        var pimg = page.AddComponent<Image>();
        pimg.color = new Color(0.96f, 0.90f, 0.72f);
        pimg.raycastTarget = false;

        // 背表紙（左の縦ライン）
        var spine = new GameObject("__Spine");
        spine.transform.SetParent(cover.transform, false);
        var srt = spine.AddComponent<RectTransform>();
        srt.sizeDelta = new Vector2(10, 60);
        srt.anchoredPosition = new Vector2(-20, 0);
        var simg = spine.AddComponent<Image>();
        simg.color = ink;
        simg.raycastTarget = false;
    }

    private static TMP_FontAsset GetJpFont()
    {
        var fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
        return fonts.FirstOrDefault(f => f.name.ToLower() == "jp") ?? fonts.FirstOrDefault();
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
}
