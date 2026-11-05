using System;
using System.Collections;
using System.Globalization;
using System.Text;
using TMPro;
using UnityEngine;

/// <summary>
/// 1か月遊び放題パスを大きく表示する画面（所持品のパスの行から開く。スタッフに見せる用）。
/// カードには光の帯が走り、下には今の時刻が1秒ごとに進む（画面の写真ではなく、いま表示していることがわかる）。
/// 閉じるのは「とじる」と戻るボタンだけ（誤タップで閉じないように）。
/// </summary>
public class SpacePassViewer : MonoBehaviour
{
    private const string T_CAPTION = "MONTHLY PASS";
    private const string T_ACTIVE_A = "有効期限内です（";
    private const string T_ACTIVE_B = "）";
    private const string T_EXPIRED = "このパスは有効期限が切れています";
    private const string T_SHOW_STAFF = "この画面をスタッフにお見せください";
    private const string T_NOW = "表示時刻　";
    private const string T_CLOSE = "とじる";
    private static readonly string[] WEEK = { "日", "月", "火", "水", "木", "金", "土" };

    public static string GlyphText => new StringBuilder()
        .Append(T_CAPTION).Append(T_ACTIVE_A).Append(T_ACTIVE_B).Append(T_EXPIRED).Append(T_SHOW_STAFF).Append(T_NOW)
        .Append(T_CLOSE).Append("日月火水木金土（）:").ToString();

    private const float CARD_W = 940f;

    private CanvasGroup _group;
    private TextMeshProUGUI _clock;
    private int _shownSecond = -1;
    private Action _onClosed;
    private bool _closing;

    public static SpacePassViewer Open(Canvas canvas, PassInfo pass, Action onClosed)
    {
        var root = SpaceTheme.Rect("__PassViewer", canvas.transform);
        SpaceTheme.Stretch(root);
        root.SetAsLastSibling();
        SpaceTheme.SubCanvas(root.gameObject, true); // カードの光の帯・時計が毎フレーム動くので分ける
        var viewer = root.gameObject.AddComponent<SpacePassViewer>();
        viewer._onClosed = onClosed;
        viewer.Build(pass);
        return viewer;
    }

    public void Close()
    {
        if (_closing) return;
        _closing = true;
        StartCoroutine(CloseRoutine());
    }

    private void Build(PassInfo pass)
    {
        var rt = (RectTransform)transform;
        _group = gameObject.AddComponent<CanvasGroup>();
        _group.alpha = 0f;
        var now = DateTime.Now;
        bool active = pass.IsActive(now);
        var foil = SpacePassCard.FoilColor(pass.Type);

        // 暗幕（下の所持品を押させない。タップでは閉じない）
        var dim = SpaceTheme.Rect("Dim", rt);
        SpaceTheme.Stretch(dim);
        SpaceTheme.Img(dim, null, new Color(0.01f, 0.015f, 0.05f, 0.97f), true);

        var safe = SpaceTheme.Rect("Safe", rt);
        SpaceTheme.Stretch(safe);
        safe.gameObject.AddComponent<SpaceSafeArea>();

        var caption = SpaceTheme.Text(safe, "Caption", T_CAPTION, 24, SpaceTheme.A(foil, 0.95f), new Vector2(0.5f, 1f),
            new Vector2(0f, -86f), new Vector2(700f, 36f), FontStyles.Bold);
        caption.characterSpacing = 18f;
        SpaceTheme.Text(safe, "Title", StorePasses.T_NAME, 44, SpaceTheme.TEXT, new Vector2(0.5f, 1f),
            new Vector2(0f, -140f), new Vector2(900f, 60f), FontStyles.Bold);

        // カード（中央より少し上）
        var stage = SpaceTheme.Rect("Stage", safe, SpaceTheme.CENTER, new Vector2(0f, 90f), new Vector2(CARD_W, CARD_W / SpacePassCard.ASPECT));
        SpacePassCard.Create(stage, pass.Type, pass, CARD_W, true, true);

        float below = 90f - CARD_W / SpacePassCard.ASPECT * 0.5f;
        string status = active
            ? T_ACTIVE_A + (pass.DaysLeft(now) <= 1 ? "本日まで" : "あと" + pass.DaysLeft(now) + "日") + T_ACTIVE_B
            : T_EXPIRED;
        SpaceTheme.Text(safe, "Status", status, 32, active ? SpaceTheme.MINT : SpaceTheme.DANGER, SpaceTheme.CENTER,
            new Vector2(0f, below - 80f), new Vector2(960f, 46f), FontStyles.Bold);
        SpaceTheme.Text(safe, "ShowStaff", T_SHOW_STAFF, 30, SpaceTheme.TEXT, SpaceTheme.CENTER,
            new Vector2(0f, below - 136f), new Vector2(960f, 44f), FontStyles.Bold);
        _clock = SpaceTheme.Text(safe, "Clock", "", 28, SpaceTheme.TEXT_SUB, SpaceTheme.CENTER,
            new Vector2(0f, below - 186f), new Vector2(960f, 40f), FontStyles.Bold);
        TickClock();

        // 下: とじる
        var close = SpaceTheme.Rect("Close", safe, new Vector2(0.5f, 0f), new Vector2(0f, 56f), new Vector2(560f, 112f),
            new Vector2(0.5f, 0f));
        var face = SpaceTheme.Glass(close, 46f, new Color(0.05f, 0.07f, 0.19f, 0.92f), null, true);
        SpaceTheme.Clickable(close.gameObject, face, Close);
        var label = SpaceTheme.Text(close, "Label", T_CLOSE, 34, SpaceTheme.TEXT, FontStyles.Bold);
        SpaceTheme.Stretch(label.rectTransform);

        gameObject.AddComponent<SpacePopIn>().Target = stage;
        StartCoroutine(SpaceTheme.Tween(0.25f, 0f, k => _group.alpha = k));
    }

    private void Update()
    {
        TickClock();
    }

    /// <summary>今の時刻（1秒ごと）。例: 表示時刻 2026/11/05（木）18:42:10</summary>
    private void TickClock()
    {
        var now = DateTime.Now;
        if (_clock == null || now.Second == _shownSecond) return;
        _shownSecond = now.Second;
        _clock.text = T_NOW + now.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture) + "（" + WEEK[(int)now.DayOfWeek]
                      + "）" + now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
    }

    private IEnumerator CloseRoutine()
    {
        yield return SpaceTheme.Tween(0.2f, 0f, k => _group.alpha = 1f - k);
        Destroy(gameObject);
    }

    private void OnDestroy()
    {
        var cb = _onClosed;
        _onClosed = null;
        cb?.Invoke();
    }
}
