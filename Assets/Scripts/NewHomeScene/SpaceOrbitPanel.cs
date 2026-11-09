using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 公転（惑星と来店のしくみ）の画面。ホームのユーザーカードの「公転」を押すと下から開く。
/// 中央の惑星のまわりの公転の輪（次の節目まで）、この公転の締め切り、来店済みか、次のごほうび、
/// 流れ星・軌道修正・休みの保険・来店回数・いちばん長い公転を1枚にまとめる。
/// 中身は JourneyService（users/{uid}.journey）から作る。読み取り・書き込みはしない。
/// </summary>
public class SpaceOrbitPanel : MonoBehaviour
{
    private const float TOP_MARGIN = 96f;
    private const float BOTTOM_MARGIN = 32f;
    private const float SIDE = 56f;
    private const float RING = 380f;
    private const float ROW_H = 84f;

    private const string T_CAPTION = "ORBIT";
    private const string T_TITLE = "公転";
    private const string T_UNIT = "公転 連続";
    private const string T_NONE = "まだ公転がありません";
    private const string T_NONE_SUB = "お店の画面の入店QRコードを読み取ると、公転が始まります";
    private const string T_UNTIL_A = "この公転は ";
    private const string T_UNTIL_B = " まで";
    private const string T_LEFT_A = "（あと";
    private const string T_LEFT_B = "日）";
    private const string T_LAST_DAY = "（今日まで）";
    private const string T_VISITED = "この公転は来店済み";
    private const string T_NOT_YET = "この公転はまだ来店していません";
    private const string T_MISSED_RESCUE = "来店しなかった公転は、次の入店で軌道修正して続きます";
    private const string T_MISSED_INSURANCE = "来店しなかった公転は、次の入店で休みの保険を使って続きます";
    private const string T_MISSED_BROKEN = "来店しなかった公転があります。次の入店で公転の数が半分に戻ります";
    private const string T_ROW_NEXT = "次のごほうび";
    private const string T_ROW_STAR = "流れ星";
    private const string T_ROW_RESCUE = "軌道修正";
    private const string T_ROW_INSURANCE = "休みの保険";
    private const string T_ROW_VISITS = "来店回数";
    private const string T_ROW_BEST = "いちばん長い公転";
    private const string T_NEXT_A = "あと";
    private const string T_NEXT_B = "公転で";
    private const string T_NEXT_ALL = "公転の惑星はすべて解放しました";
    private const string T_STAR_DONE = "この公転はもう引きました";
    private const string T_STAR_READY = "次の来店で引けます";
    private const string T_RESCUE_A = "あと";
    private const string T_RESCUE_B = "回（3か月に";
    private const string T_RESCUE_C = "回まで）";
    private const string T_INSURANCE_HAVE = "1つ持っています";
    // 休みの保険は 2026-11-09 から貯まらない（オルビスの能力をやめた）ので、持っている人にだけ行を出す（2026-11-10）
    private const string T_TIMES = "回";
    private const string T_STREAK = " 連続";
    private const string T_FOOTER = "公転は木曜はじまりの2週間です。2週間に1回来店すると続きます";
    // 惑星のパワーの行・説明は 2026-11-10 にやめた

    /// <summary>この画面で表示しうる文字</summary>
    public static string GlyphText => new StringBuilder()
        .Append(T_CAPTION).Append(T_TITLE).Append(T_UNIT).Append(T_NONE).Append(T_NONE_SUB).Append(T_UNTIL_A)
        .Append(T_UNTIL_B).Append(T_LEFT_A).Append(T_LEFT_B).Append(T_LAST_DAY).Append(T_VISITED).Append(T_NOT_YET)
        .Append(T_MISSED_RESCUE).Append(T_MISSED_INSURANCE).Append(T_MISSED_BROKEN).Append(T_ROW_NEXT)
        .Append(T_ROW_STAR).Append(T_ROW_RESCUE).Append(T_ROW_INSURANCE).Append(T_ROW_VISITS).Append(T_ROW_BEST)
        .Append(T_NEXT_A).Append(T_NEXT_B).Append(T_NEXT_ALL).Append(T_STAR_DONE).Append(T_STAR_READY)
        .Append(T_RESCUE_A).Append(T_RESCUE_B).Append(T_RESCUE_C).Append(T_INSURANCE_HAVE)
        .Append(T_TIMES).Append(T_STREAK).Append(T_FOOTER).ToString();

    private Canvas _canvas;
    private CanvasGroup _group;
    private RectTransform _sheet;
    private Vector2 _sheetBase;
    private Action _onClosed;
    private bool _closing;

    public static SpaceOrbitPanel Open(Canvas canvas, Action onClosed)
    {
        var root = SpaceTheme.Rect("__SpaceOrbit", canvas.transform);
        SpaceTheme.Stretch(root);
        root.SetAsLastSibling();
        SpaceTheme.SubCanvas(root.gameObject, true); // 惑星が自転し続けるので、メインのキャンバスと分ける
        var panel = root.gameObject.AddComponent<SpaceOrbitPanel>();
        panel._canvas = canvas;
        panel._onClosed = onClosed;
        panel.Build();
        return panel;
    }

    public void HandleBack() => Close();

    public void Close()
    {
        if (_closing) return;
        _closing = true;
        StartCoroutine(CloseRoutine());
    }

    private IEnumerator CloseRoutine()
    {
        yield return SpaceTheme.Tween(0.18f, 0f, k =>
        {
            _group.alpha = 1f - k;
            _sheet.anchoredPosition = _sheetBase + new Vector2(0f, -60f * k);
        });
        _onClosed?.Invoke();
        Destroy(gameObject);
    }

    private void Build()
    {
        var rt = (RectTransform)transform;
        _group = gameObject.AddComponent<CanvasGroup>();

        var dim = SpaceTheme.Rect("Dim", rt);
        SpaceTheme.Stretch(dim);
        var dimImg = SpaceTheme.Img(dim, null, SpaceTheme.DIM, true);
        SpaceTheme.Clickable(dim.gameObject, dimImg, Close, false);

        var safe = SpaceTheme.Rect("Safe", rt);
        SpaceTheme.Stretch(safe);
        safe.gameObject.AddComponent<SpaceSafeArea>();
        var size = SpaceTheme.CanvasSize(_canvas);
        var insets = SpaceTheme.SafeInsets(size);
        float safeH = size.y - insets.top - insets.bottom;

        _sheet = SpaceTheme.Rect("Sheet", safe);
        _sheet.anchorMin = new Vector2(0f, 0f);
        _sheet.anchorMax = new Vector2(1f, 0f);
        _sheet.pivot = new Vector2(0.5f, 0f);
        _sheet.anchoredPosition = new Vector2(0f, BOTTOM_MARGIN);
        _sheetBase = _sheet.anchoredPosition;
        SpaceTheme.Glass(_sheet, 56f, SpaceTheme.GLASS_DEEP, null, true);

        var caption = SpaceTheme.Text(_sheet, "Caption", T_CAPTION, 24, SpaceTheme.A(SpaceTheme.CYAN, 0.85f),
            new Vector2(0f, 1f), new Vector2(SIDE, -70f), new Vector2(400f, 36f), FontStyles.Bold,
            TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
        SpaceTheme.Tracking(caption, 18f);
        SpaceTheme.Text(_sheet, "Title", T_TITLE, 56, SpaceTheme.TEXT, new Vector2(0f, 1f), new Vector2(SIDE, -128f),
            new Vector2(560f, 72f), FontStyles.Bold, TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
        BuildCloseButton();

        // 中身（入りきらない端末ではスクロール）
        var view = SpaceTheme.Rect("View", _sheet);
        SpaceTheme.Stretch(view, 0f, 24f, 0f, 196f);
        SpaceTheme.HitArea(view);
        view.gameObject.AddComponent<RectMask2D>();
        var content = SpaceTheme.Rect("Content", view);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.anchoredPosition = Vector2.zero;
        var scroll = view.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = view;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.scrollSensitivity = 30f;

        float contentH = BuildContent(content);
        content.sizeDelta = new Vector2(0f, contentH);
        float sheetH = Mathf.Min(196f + contentH + 24f, safeH - TOP_MARGIN - BOTTOM_MARGIN);
        _sheet.sizeDelta = new Vector2(-64f, sheetH);
        scroll.vertical = contentH > sheetH - 196f - 24f + 1f;

        _group.alpha = 0f;
        StartCoroutine(SpaceTheme.Tween(0.32f, 0f, k =>
        {
            _group.alpha = k;
            _sheet.anchoredPosition = _sheetBase + new Vector2(0f, -80f * (1f - k));
        }));
    }

    private void BuildCloseButton()
    {
        var close = SpaceTheme.Rect("Close", _sheet, new Vector2(1f, 1f), new Vector2(-80f, -96f), new Vector2(96f, 96f));
        var face = SpaceTheme.Img(close, SpaceSprites.Circle, SpaceTheme.A(new Color(0.09f, 0.12f, 0.30f), 0.65f), true);
        var rim = SpaceTheme.Rect("Rim", close);
        SpaceTheme.Stretch(rim);
        SpaceTheme.Img(rim, SpaceSprites.RingSmall, SpaceTheme.STROKE);
        SpaceTheme.Bar(close, Vector2.zero, 44f, 6f, 45f, SpaceTheme.TEXT_SUB);
        SpaceTheme.Bar(close, Vector2.zero, 44f, 6f, -45f, SpaceTheme.TEXT_SUB);
        SpaceTheme.Clickable(close.gameObject, face, Close);
    }

    /// <summary>中身を上から積み、高さを返す</summary>
    private float BuildContent(RectTransform content)
    {
        var j = JourneyService.Journey;
        var center = SpacePlanets.Current;
        var goal = JourneyService.NextStreakGoal();
        int streak = JourneyService.DisplayStreak;
        float y = 0f;

        // 公転の輪
        var stage = SpaceTheme.Rect("Ring", content, new Vector2(0.5f, 1f), new Vector2(0f, -(y + RING * 0.5f)),
            new Vector2(RING, RING));
        SpaceNotice.OrbitRing(stage, RING, streak, goal?.n ?? Mathf.Max(streak, 26), center);
        y += RING + 12f;

        if (j == null)
        {
            Line(content, ref y, T_NONE, 44, SpaceTheme.TEXT, 64f);
            Line(content, ref y, T_NONE_SUB, 28, SpaceTheme.TEXT_SUB, 44f);
            y += 24f;
            Line(content, ref y, T_FOOTER, 24, SpaceTheme.TEXT_MUTED, 36f);
            return y + 24f;
        }

        Line(content, ref y, $"{streak}<size=50%> {T_UNIT}</size>", 84, SpaceTheme.TEXT, 104f);
        var today = StoreCalendar.TodayBusiness;
        int period = StoreCalendar.CurrentPeriod;
        int left = StoreCalendar.DaysLeft(today);
        string leftText = left <= 0 ? T_LAST_DAY : T_LEFT_A + left + T_LEFT_B;
        Line(content, ref y, T_UNTIL_A + StoreCalendar.MonthDayW(StoreCalendar.PeriodEnd(period)) + T_UNTIL_B + leftText,
            30, SpaceTheme.TEXT_SUB, 46f);

        bool visited = JourneyService.VisitedThisOrbit;
        Chip(content, ref y, visited ? T_VISITED : T_NOT_YET, visited ? SpaceTheme.MINT : SpaceTheme.GOLD);

        int missed = JourneyService.MissedOrbits;
        if (missed > 0)
        {
            int rescues = JourneyService.RescuesLeft(center.Id);
            string note = rescues >= missed ? T_MISSED_RESCUE
                : rescues + j.Insurance >= missed ? T_MISSED_INSURANCE
                : T_MISSED_BROKEN;
            var t = Line(content, ref y, note, 26, rescues + j.Insurance >= missed ? SpaceTheme.MINT : SpaceTheme.PINK, 40f);
            t.textWrappingMode = TextWrappingModes.Normal;
        }
        y += 16f;

        // 一覧
        string next = goal.HasValue
            ? T_NEXT_A + Mathf.Max(1, goal.Value.n - streak) + T_NEXT_B + goal.Value.def.Name
            : T_NEXT_ALL;
        Row(content, ref y, T_ROW_NEXT, next, SpaceTheme.CYAN);
        bool drawn = j.StarPeriod == period;
        Row(content, ref y, T_ROW_STAR, drawn ? T_STAR_DONE : T_STAR_READY, drawn ? SpaceTheme.TEXT_SUB : SpaceTheme.GOLD);
        // 軌道修正の回数は惑星によって変わらない（判定と同じ JourneyRules.RESCUE_ALLOWANCE）
        Row(content, ref y, T_ROW_RESCUE,
            T_RESCUE_A + JourneyService.RescuesLeft(center.Id) + T_RESCUE_B + JourneyRules.RESCUE_ALLOWANCE + T_RESCUE_C,
            SpaceTheme.TEXT);
        // 休みの保険はもう貯まらないので、残っている人にだけ出す（使い道は変わらない）
        if (j.Insurance > 0) Row(content, ref y, T_ROW_INSURANCE, T_INSURANCE_HAVE, SpaceTheme.MINT);
        Row(content, ref y, T_ROW_VISITS, j.Visits + T_TIMES, SpaceTheme.TEXT);
        Row(content, ref y, T_ROW_BEST, j.Best + T_STREAK, SpaceTheme.TEXT);

        y += 20f;
        var foot = Line(content, ref y, T_FOOTER, 24, SpaceTheme.TEXT_MUTED, 36f);
        foot.enableAutoSizing = true;
        foot.fontSizeMin = 18f;
        foot.fontSizeMax = 24f;
        return y + 16f;
    }

    private static TextMeshProUGUI Line(RectTransform content, ref float y, string text, float size, Color color,
        float height)
    {
        var t = SpaceTheme.Text(content, "Line", SpaceTheme.SafeNow(text), size, color, new Vector2(0.5f, 1f),
            new Vector2(0f, -(y + height * 0.5f)), new Vector2(900f, height), FontStyles.Bold);
        y += height;
        return t;
    }

    private static void Chip(RectTransform content, ref float y, string text, Color color)
    {
        y += 10f;
        var chip = SpaceTheme.Rect("Chip", content, new Vector2(0.5f, 1f), new Vector2(0f, -(y + 29f)),
            new Vector2(560f, 58f));
        SpaceTheme.Rounded(chip, SpaceTheme.A(color, 0.14f), 29f);
        var edge = SpaceTheme.Rect("Edge", chip);
        SpaceTheme.Stretch(edge);
        SpaceTheme.RoundedStroke(edge, SpaceTheme.A(color, 0.6f), 29f);
        var label = SpaceTheme.Text(chip, "Label", text, 28, color, FontStyles.Bold);
        SpaceTheme.Stretch(label.rectTransform);
        y += 58f;
    }

    private static void Row(RectTransform content, ref float y, string label, string value, Color valueColor)
    {
        var row = SpaceTheme.Rect("Row", content);
        row.anchorMin = new Vector2(0f, 1f);
        row.anchorMax = new Vector2(1f, 1f);
        row.pivot = new Vector2(0.5f, 1f);
        row.sizeDelta = new Vector2(-64f, ROW_H); // 左右に32ずつあける
        row.anchoredPosition = new Vector2(0f, -y);
        var line = SpaceTheme.Rect("Line", row, new Vector2(0.5f, 0f), Vector2.zero, new Vector2(900f, 2f));
        SpaceTheme.Img(line, SpaceSprites.FadeLine, SpaceTheme.A(SpaceTheme.LINE, 0.2f));
        SpaceTheme.Text(row, "Label", label, 28, SpaceTheme.TEXT_MUTED, new Vector2(0f, 0.5f), new Vector2(24f, 0f),
            new Vector2(300f, 44f), FontStyles.Bold, TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
        var v = SpaceTheme.Text(row, "Value", SpaceTheme.SafeNow(value), 30, valueColor, new Vector2(1f, 0.5f),
            new Vector2(-24f, 0f), new Vector2(620f, 44f), FontStyles.Bold, TextAlignmentOptions.Right,
            new Vector2(1f, 0.5f));
        v.enableAutoSizing = true;
        v.fontSizeMin = 20f;
        v.fontSizeMax = 30f;
        y += ROW_H;
    }
}
