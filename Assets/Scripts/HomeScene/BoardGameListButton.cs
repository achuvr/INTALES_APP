using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// お知らせページ（Page_Info）に「ゲーム一覧」ボタンを追加する（コード生成）。
/// メニューボタン（MenuButton）と横並びの左側・下部フッターの上に置き、押すと店舗の
/// ボードゲーム一覧（BoardGameListController）を開く。白角丸スタイル＋赤いミープルのアイコン。
///
/// HomeSceneInitializer から gameObject.AddComponent&lt;BoardGameListButton&gt;() で追加される
/// （シーン編集不要）。AccountDeletionController と同じ流儀で Page_Info の子へ移す。
/// </summary>
public class BoardGameListButton : MonoBehaviour
{
    // お知らせページ下部に横並びで置く2ボタン（ゲーム一覧/メニュー）の共通レイアウト。
    // 画面下端アンカー基準。フッター(上端~237)とアカウント削除リンク(260〜330)の上に載せる。
    // MenuButton も同じ値を使って右側に並べる
    public const float INFO_BTN_W = 400f;
    public const float INFO_BTN_H = 130f;
    public const float INFO_BTN_X = 215f;  // 中央からの左右オフセット
    public const float INFO_BTN_Y = 350f;  // 下端からボタン下辺までの高さ

    /// <summary>ミープルの赤（ボードゲームの定番コマ色）。</summary>
    public static readonly Color MEEPLE_RED = new Color(0.85f, 0.20f, 0.17f, 1f);

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
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(-INFO_BTN_X, INFO_BTN_Y + INFO_BTN_H / 2f);
        rt.sizeDelta = new Vector2(INFO_BTN_W, INFO_BTN_H);
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
            Debug.LogWarning("[BoardGameList] Page_Info が見つからないため、Canvas直下にボタンを置きます");
        }

        // 赤いミープルのアイコン（左側）
        var icon = new GameObject("__MeepleIcon");
        icon.transform.SetParent(btnGO.transform, false);
        var irt = icon.AddComponent<RectTransform>();
        irt.sizeDelta = new Vector2(92, 92);
        irt.anchoredPosition = new Vector2(-130, 0);
        var iimg = icon.AddComponent<Image>();
        MeepleSprite.Apply(iimg);
        iimg.color = MEEPLE_RED;
        iimg.raycastTarget = false;

        MakeLabel(btnGO.transform, "ゲーム一覧", jp, 44, FontStyles.Bold, ink, 250, INFO_BTN_H, new Vector2(40, 0));

        var btn = btnGO.AddComponent<Button>();
        btn.targetGraphic = bg;
        btn.onClick.AddListener(OpenList);
    }

    /// <summary>ボードゲーム一覧を開く（多重オープン防止つき）。</summary>
    private void OpenList()
    {
        if (FindFirstObjectByType<BoardGameListController>() != null) return; // 既に開いている
        new GameObject("BoardGameListRoot").AddComponent<BoardGameListController>();
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
