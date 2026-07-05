using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Home画面に「ゲーム一覧」ボタンを追加する（コード生成）。
/// キャラクターページ右上、図鑑ボタンのすぐ下に置き、押すと店舗のボードゲーム一覧
/// （BoardGameListController）を開く。図鑑ボタンと同じ白角丸スタイルで統一する。
///
/// HomeSceneInitializer から gameObject.AddComponent&lt;BoardGameListButton&gt;() で追加される
/// （シーン編集不要）。ZukanButton と同じ流儀。
/// </summary>
public class BoardGameListButton : MonoBehaviour
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

        var btnGO = new GameObject("__BoardGameListButton");
        btnGO.transform.SetParent(_canvas.transform, false);
        var rt = btnGO.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(-30, -476); // 図鑑ボタン(-380)の下
        rt.sizeDelta = new Vector2(232, 86);
        var bg = btnGO.AddComponent<Image>();
        GachaController.ApplyGridButtonStyle(bg);

        // キャラクターページ表示中のみ出す（図鑑・履歴・フレンドと同様）
        var page = _canvas.transform.Find("Page_Characters");
        if (page != null)
        {
            btnGO.transform.SetParent(page, true);
            btnGO.transform.SetAsLastSibling();
        }

        // サイコロ風アイコン（左側）
        BuildDiceIcon(btnGO.transform, ink, new Vector2(-78, 0));

        MakeLabel(btnGO.transform, "ゲーム一覧", jp, 36, FontStyles.Bold, ink, 160, 86, new Vector2(24, 0));

        var btn = btnGO.AddComponent<Button>();
        btn.targetGraphic = bg;
        btn.onClick.AddListener(OpenList);
    }

    /// <summary>ボードゲーム一覧を開く（多重オープン防止つき）。</summary>
    private void OpenList()
    {
        if (FindObjectOfType<BoardGameListController>() != null) return; // 既に開いている
        new GameObject("BoardGameListRoot").AddComponent<BoardGameListController>();
    }

    /// <summary>簡易のサイコロアイコン（角丸の白い面＋こげ茶のピップ5つ）。</summary>
    private static void BuildDiceIcon(Transform parent, Color ink, Vector2 pos)
    {
        var die = new GameObject("__DiceIcon");
        die.transform.SetParent(parent, false);
        var drt = die.AddComponent<RectTransform>();
        drt.sizeDelta = new Vector2(56, 56);
        drt.anchoredPosition = pos;
        var dimg = die.AddComponent<Image>();
        RoundedRectSprite.Apply(dimg);
        dimg.color = ink;
        dimg.raycastTarget = false;

        // 面（少し内側の明色）
        var face = new GameObject("__Face");
        face.transform.SetParent(die.transform, false);
        var frt = face.AddComponent<RectTransform>();
        frt.sizeDelta = new Vector2(44, 44);
        var fimg = face.AddComponent<Image>();
        RoundedRectSprite.Apply(fimg);
        fimg.color = new Color(0.98f, 0.94f, 0.82f);
        fimg.raycastTarget = false;

        // 5の目（四隅＋中央）
        Vector2[] pips =
        {
            new Vector2(-11, 11), new Vector2(11, 11),
            new Vector2(0, 0),
            new Vector2(-11, -11), new Vector2(11, -11),
        };
        foreach (var p in pips)
        {
            var pip = new GameObject("__Pip");
            pip.transform.SetParent(face.transform, false);
            var prt = pip.AddComponent<RectTransform>();
            prt.sizeDelta = new Vector2(9, 9);
            prt.anchoredPosition = p;
            var pimg = pip.AddComponent<Image>();
            RoundedRectSprite.Apply(pimg);
            pimg.color = ink;
            pimg.raycastTarget = false;
        }
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
        tmp.enableAutoSizing = true;
        tmp.fontSizeMax = size;
        tmp.fontSizeMin = size * 0.6f;
        return go;
    }
}
