using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// お知らせページ（Page_Info）に「募集」ボタンを追加する（コード生成）。
/// アカウントボタン（AccountButton）と横並びの右側に置き、押すと
/// 相席募集の掲示板（RecruitBoardController）を開く。
///
/// 募集ボードは誹謗中傷対策として自由入力を持たない完全選択式。
/// 設計方針の詳細は RecruitBoardController のクラスコメントを参照。
///
/// HomeSceneInitializer から gameObject.AddComponent&lt;RecruitChatButton&gt;() で追加される（シーン編集不要）。
/// </summary>
public class RecruitChatButton : MonoBehaviour
{
    private Canvas _canvas;

    private void Start()
    {
        var canvasGO = GameObject.Find("Canvas");
        _canvas = canvasGO != null ? canvasGO.GetComponent<Canvas>() : FindFirstObjectByType<Canvas>();
        if (_canvas == null) return;
        BuildButton();
    }

    // ================================================================
    // 入口ボタン（Page_Info・アカウントボタンの右）
    // ================================================================
    private void BuildButton()
    {
        var jp = GetJpFont();
        var ink = GachaController.GRID_TEXT_COLOR;

        var btnGO = new GameObject("__RecruitButton");
        btnGO.transform.SetParent(_canvas.transform, false);
        var rt = btnGO.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(AccountButton.BTN_X, AccountButton.BTN_Y + AccountButton.BTN_H / 2f);
        rt.sizeDelta = new Vector2(AccountButton.BTN_W, AccountButton.BTN_H);
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
            Debug.LogWarning("[Recruit] Page_Info が見つからないため、Canvas直下にボタンを置きます");
        }

        // 吹き出しアイコン（角丸の吹き出し＋45°回転のしっぽ＋点3つ）
        BuildBalloonIcon(btnGO.transform, ink, new Vector2(-130, 4));

        MakeLabel(btnGO.transform, "募集", jp, 44, FontStyles.Bold, ink, 250, AccountButton.BTN_H, new Vector2(40, 0));

        var btn = btnGO.AddComponent<Button>();
        btn.targetGraphic = bg;
        btn.onClick.AddListener(OpenBoard);
    }

    /// <summary>募集ボードを開く（多重オープン防止つき）。</summary>
    private void OpenBoard()
    {
        if (FindFirstObjectByType<RecruitBoardController>() != null) return; // 既に開いている
        new GameObject("RecruitBoardRoot").AddComponent<RecruitBoardController>();
    }

    /// <summary>吹き出しアイコン。</summary>
    private static void BuildBalloonIcon(Transform parent, Color ink, Vector2 pos)
    {
        var root = new GameObject("__BalloonIcon");
        root.transform.SetParent(parent, false);
        var rrt = root.AddComponent<RectTransform>();
        rrt.sizeDelta = new Vector2(80, 80);
        rrt.localScale = Vector3.one * (92f / 80f); // 下段ボタンのアイコン(92px)と同じ見た目サイズ
        rrt.anchoredPosition = pos;

        var body = new GameObject("__Body");
        body.transform.SetParent(root.transform, false);
        var brt = body.AddComponent<RectTransform>();
        brt.sizeDelta = new Vector2(64, 44);
        brt.anchoredPosition = new Vector2(0, 6);
        var bimg = body.AddComponent<Image>();
        RoundedRectSprite.Apply(bimg);
        bimg.color = ink;
        bimg.raycastTarget = false;

        // しっぽ（45°回転させた小さな四角）
        var tail = new GameObject("__Tail");
        tail.transform.SetParent(root.transform, false);
        var trt = tail.AddComponent<RectTransform>();
        trt.sizeDelta = new Vector2(18, 18);
        trt.anchoredPosition = new Vector2(-14, -18);
        trt.localRotation = Quaternion.Euler(0, 0, 45f);
        var timg = tail.AddComponent<Image>();
        timg.color = ink;
        timg.raycastTarget = false;

        // 点3つ（発言中の「…」）
        for (int i = 0; i < 3; i++)
        {
            var dot = new GameObject("__Dot" + i);
            dot.transform.SetParent(body.transform, false);
            var drt = dot.AddComponent<RectTransform>();
            drt.sizeDelta = new Vector2(9, 9);
            drt.anchoredPosition = new Vector2(-16 + i * 16, 0);
            var dimg = dot.AddComponent<Image>();
            UICircleSprite.Apply(dimg);
            dimg.color = new Color(0.98f, 0.94f, 0.82f, 1f);
            dimg.raycastTarget = false;
        }
    }

    // ================================================================
    // UI部品ヘルパー
    // ================================================================
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
