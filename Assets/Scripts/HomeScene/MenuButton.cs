using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// お知らせページ（Page_Info）に「メニュー」ボタンを追加する（コード生成）。
/// ゲーム一覧ボタン（BoardGameListButton）と横並びの右側に置き、押すと店舗の
/// 食事・ドリンクメニュー（MenuViewerController）を開く。
/// アイコンはフォーク＆ナイフをImageの組み合わせで描く（絵文字はOOMクラッシュの
/// 原因になるため使わない。emoji-tmp-oom-crash 対策）。
///
/// HomeSceneInitializer から gameObject.AddComponent&lt;MenuButton&gt;() で追加される（シーン編集不要）。
/// </summary>
public class MenuButton : MonoBehaviour
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

        var btnGO = new GameObject("__MenuButton");
        btnGO.transform.SetParent(_canvas.transform, false);
        var rt = btnGO.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(BoardGameListButton.INFO_BTN_X,
            BoardGameListButton.INFO_BTN_Y + BoardGameListButton.INFO_BTN_H / 2f);
        rt.sizeDelta = new Vector2(BoardGameListButton.INFO_BTN_W, BoardGameListButton.INFO_BTN_H);
        var bg = btnGO.AddComponent<Image>();
        GachaController.ApplyGridButtonStyle(bg);

        // お知らせページ表示中のみ出す（画面基準で位置を決めてから Page_Info の子へ移動）
        var page = _canvas.transform.Find("Page_Info");
        if (page != null)
        {
            btnGO.transform.SetParent(page, true);
            btnGO.transform.SetAsLastSibling();
        }
        else
        {
            Debug.LogWarning("[Menu] Page_Info が見つからないため、Canvas直下にボタンを置きます");
        }

        BuildForkKnifeIcon(btnGO.transform, ink, new Vector2(-130, 0));

        MakeLabel(btnGO.transform, "メニュー", jp, 44, FontStyles.Bold, ink,
            250, BoardGameListButton.INFO_BTN_H, new Vector2(40, 0));

        var btn = btnGO.AddComponent<Button>();
        btn.targetGraphic = bg;
        btn.onClick.AddListener(Open);
    }

    /// <summary>メニュー画面を開く（多重オープン防止つき）。</summary>
    private void Open()
    {
        if (FindFirstObjectByType<MenuViewerController>() != null) return; // 既に開いている
        new GameObject("MenuViewerRoot").AddComponent<MenuViewerController>();
    }

    /// <summary>フォーク（左）とナイフ（右）のアイコン。角丸矩形の組み合わせで描く。</summary>
    private static void BuildForkKnifeIcon(Transform parent, Color ink, Vector2 pos)
    {
        var root = new GameObject("__ForkKnifeIcon");
        root.transform.SetParent(parent, false);
        var rrt = root.AddComponent<RectTransform>();
        rrt.sizeDelta = new Vector2(92, 92);
        rrt.anchoredPosition = pos;

        void Part(string name, float x, float y, float w, float h)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            var prt = go.AddComponent<RectTransform>();
            prt.sizeDelta = new Vector2(w, h);
            prt.anchoredPosition = new Vector2(x, y);
            var img = go.AddComponent<Image>();
            RoundedRectSprite.Apply(img);
            img.color = ink;
            img.raycastTarget = false;
        }

        // フォーク: 柄 + 3本の刃先
        Part("__ForkHandle", -20, -14, 10, 60);
        Part("__ForkProng1", -30, 26, 7, 28);
        Part("__ForkProng2", -20, 26, 7, 28);
        Part("__ForkProng3", -10, 26, 7, 28);

        // ナイフ: 刃（上側・幅広）+ 柄
        Part("__KnifeBlade", 20, 16, 16, 48);
        Part("__KnifeHandle", 20, -24, 9, 40);
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
