using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 営業カレンダー（2026-11-10 ユーザー指定。お知らせの「営業カレンダー」から開く全画面）。
///  ・月のカレンダーに、定休日（月・火・水）・臨時休業・臨時営業・イベントの日を出す（◀▶で月を変える。半年先まで）。
///    臨時休業は定休日と同じ色（数字を薄くして「臨休」）、イベントの日も色は変えず「イベント」と出す（同日ユーザー指定）。
///    臨時営業だけ緑の丸で「営業」
///  ・下に、その月の臨時休業・臨時営業・イベントの一覧（理由・イベントの名前付き。イベントは1件ずつ）
///  ・管理者は日付（か一覧の行）を押すと登録のカード: お休み・営業（臨時休業／定休日なら臨時営業／ふだんどおりに戻す。理由は任意）と、
///    イベント（1日に ShopCalendar.EVENTS_MAX 個まで足せる・1つずつ消せる）。登録してもカードは開いたままなので、続けて足せる。
///    臨時休業を登録すると、1か月パスの期限はそのぶん自動で延びる（StorePasses.ExpiryFor）。
///    前の月にも3か月までさかのぼれる（登録し忘れた臨時休業をあとから入れる）
/// お知らせの子として開く（戻る操作はお知らせの HandleBack から）。データは ShopCalendar（master/calendar）。
/// お知らせの欄に出す2週間ぶんの小さなカレンダー（BuildWeeks）もここで描く。
/// </summary>
public class SpaceShopCalendar : MonoBehaviour
{
    public const string NAME = "__ShopCalendar";
    /// <summary>このカレンダーの色（お知らせの見出しの点・月送り・今日の〇）</summary>
    public static readonly Color ACCENT = SpaceTheme.CYAN;
    private static readonly Color SUNDAY = SpaceTheme.Hex(0xFF8FA8);   // 遊んだ記録のカレンダーと同じ
    private static readonly Color SATURDAY = SpaceTheme.Hex(0x7FC8FF);
    private static readonly Color CLOSED_C = SpaceTheme.TEXT_MUTED;    // 定休日・臨時休業（同じ色）
    private static readonly Color OPEN_C = SpaceTheme.MINT;            // 臨時営業
    private static readonly Color EVENT_C = SpaceTheme.TEXT_SUB;       // イベント（色は変えない）

    public const string T_TITLE = "営業カレンダー";
    private const string T_CAPTION = "CALENDAR";
    private const string T_YEAR = "年";
    private const string T_MONTH = "月";
    private const string T_DAY = "日";
    // マスの下の小さな文字
    private const string L_REGULAR = "休";
    private const string L_CLOSED = "臨休";
    private const string L_OPEN = "営業";
    private const string L_EVENT = "イベント";
    private const string T_LEGEND = "休＝定休日　臨休＝臨時休業　営業＝臨時営業";
    private const string T_LIST = "この月の予定";
    private const string T_LIST_NONE = "この月の臨時休業・イベントはありません";
    public const string T_REGULAR_NOTE = "定休日は毎週 月・火・水です";
    private const string T_ADMIN_HINT = "日付を押すと、臨時休業・臨時営業・イベントを登録できます";
    // お知らせの欄
    private const string T_SEE_ALL = "カレンダーを見る ＞";
    private const string T_INFO_TODAY = "本日は臨時休業です";
    private const string T_INFO_NEXT = "次の臨時休業 ";
    // 管理者の登録
    private const string T_NOW = "いまの設定: ";
    private const string T_NORMAL_OPEN = "営業日";
    private const string T_NORMAL_CLOSED = "定休日";
    private const string T_EVENT_OPEN = "イベントのため営業";
    private const string T_EVENT_COUNT_A = "・イベント ";
    private const string T_EVENT_COUNT_B = "件";
    private const string T_SEC_STATUS = "お休み・営業";
    private const string T_SEC_EVENTS = "イベント";
    private const string T_REASON_PH = "理由（なくてもよい）";
    private const string T_NAME_PH = "イベントの名前（例: ボードゲーム会 19:00〜）";
    private const string T_NO_EVENTS = "この日のイベントはまだありません";
    private const string T_SET_CLOSED = "臨時休業にする";
    private const string T_SET_OPEN = "臨時営業にする";
    private const string T_RESET = "ふだんどおりに戻す";
    private const string T_ADD = "追加";
    private const string T_REMOVE = "消す";
    private const string T_CLOSE = "閉じる";
    private const string T_SAVING = "保存しています…";
    private const string T_NEED_NAME = "イベントの名前を入れてください";
    private const string T_DUPLICATE = "同じ名前のイベントがもうあります";
    private const string T_EVENTS_FULL_A = "1日に登録できるイベントは";
    private const string T_EVENTS_FULL_B = "つまでです";
    private const string T_DONE_CLOSED = "を臨時休業にしました\n1か月パスの期限は自動で延びます";
    private const string T_DONE_OPEN = "を臨時営業にしました";
    private const string T_DONE_RESET = "をふだんどおりに戻しました";
    private const string T_DONE_ADD = "にイベントを追加しました";
    private const string T_DONE_REMOVE = "のイベントを消しました";
    private const string T_FAILED = "保存できませんでした。通信の状態を確かめてください";
    private const string T_NO_EMOJI = "絵文字は使えません";

    private static readonly string[] WEEK = { "日", "月", "火", "水", "木", "金", "土" };

    /// <summary>この画面とお知らせの欄で出しうる決まった文言（理由・イベントの名前は出す前に SafeNow で登録する）</summary>
    public static string GlyphText => new StringBuilder()
        .Append(T_TITLE).Append(T_CAPTION).Append(T_YEAR).Append(T_MONTH).Append(T_DAY).Append(L_REGULAR).Append(L_CLOSED)
        .Append(L_OPEN).Append(L_EVENT).Append(T_LEGEND).Append(T_LIST).Append(T_LIST_NONE).Append(T_REGULAR_NOTE)
        .Append(T_ADMIN_HINT).Append(T_SEE_ALL).Append(T_INFO_TODAY).Append(T_INFO_NEXT).Append(T_NOW).Append(T_NORMAL_OPEN)
        .Append(T_NORMAL_CLOSED).Append(T_EVENT_OPEN).Append(T_EVENT_COUNT_A).Append(T_EVENT_COUNT_B).Append(T_SEC_STATUS)
        .Append(T_SEC_EVENTS).Append(T_REASON_PH).Append(T_NAME_PH).Append(T_NO_EVENTS).Append(T_SET_CLOSED)
        .Append(T_SET_OPEN).Append(T_RESET).Append(T_ADD).Append(T_REMOVE).Append(T_CLOSE).Append(T_SAVING)
        .Append(T_NEED_NAME).Append(T_DUPLICATE).Append(T_EVENTS_FULL_A).Append(T_EVENTS_FULL_B).Append(T_DONE_CLOSED)
        .Append(T_DONE_OPEN).Append(T_DONE_RESET).Append(T_DONE_ADD).Append(T_DONE_REMOVE).Append(T_FAILED)
        .Append(T_NO_EMOJI).Append(string.Concat(WEEK)).Append("◀▶（）/0123456789").Append(ShopCalendar.GlyphText)
        .Append(StoreCalendar.GlyphText).ToString();

    private const float SIDE = 40f;
    private const float GRID_W = 1000f;
    private const float CELL_W = GRID_W / 7f, CELL_H = 112f;
    private const float MARK = 84f;        // 臨時営業の丸の直径
    private const float TODAY_RING = 106f; // 今日の〇の直径（数字と「休」などにかぶらない大きさ。2026-11-10 ユーザー指定で 92 から広げた）
    private const float WEEKS_TODAY_RING = 90f; // お知らせの欄の今日の〇（同じく 80 から）
    private const float MONTH_Y = 238f, LEGEND_Y = 304f, WEEK_Y = 364f, GRID_TOP = 392f; // 上からの位置
    private const float ROW_H = 88f, ROW_GAP = 12f;
    private const int MONTHS_AHEAD = 6;      // 何か月先まで見られるか
    private const int MONTHS_BACK_ADMIN = 3; // 管理者は前の月にもさかのぼれる
    private static readonly Vector2 TOP = new Vector2(0.5f, 1f);

    private CanvasGroup _group;
    private RectTransform _body;
    private DateTime _thisMonth, _month;
    private bool _isAdmin, _closing, _saving;
    private GameObject _editor;
    private float _emojiToastUntil;

    // ================================================================
    // 開く・閉じる
    // ================================================================
    public static SpaceShopCalendar Open(Transform host)
    {
        var opened = host.Find(NAME);
        if (opened != null) return opened.GetComponent<SpaceShopCalendar>();
        var root = SpaceTheme.Rect(NAME, host);
        SpaceTheme.Stretch(root);
        root.SetAsLastSibling();
        var page = root.gameObject.AddComponent<SpaceShopCalendar>();
        page.Build();
        return page;
    }

    /// <summary>戻る操作: 登録のカードを開いていればそれを、なければカレンダーを閉じる（保存している間は何もしない）</summary>
    public void HandleBack()
    {
        if (_saving) return;
        if (_editor != null) CloseEditor();
        else Close();
    }

    public void Close()
    {
        if (_closing) return;
        _closing = true;
        CloseEditor();
        StartCoroutine(CloseRoutine());
    }

    private IEnumerator CloseRoutine()
    {
        float from = _group.alpha;
        yield return SpaceTheme.Tween(0.2f, 0f, k => _group.alpha = from * (1f - k));
        Destroy(gameObject);
    }

    private void OnDestroy() => ShopCalendar.Changed -= OnCalendarChanged;

    private void OnCalendarChanged()
    {
        if (this != null && !_closing) Rebuild();
    }

    // ================================================================
    // 組み立て
    // ================================================================
    private void Build()
    {
        var rt = (RectTransform)transform;
        _group = gameObject.AddComponent<CanvasGroup>();
        _group.alpha = 0f;

        var dim = SpaceTheme.Rect("Dim", rt);
        SpaceTheme.Stretch(dim);
        SpaceTheme.Img(dim, null, new Color(0.01f, 0.015f, 0.05f, 0.98f), true);
        var glow = SpaceTheme.Rect("Glow", rt, TOP, new Vector2(0f, -160f), new Vector2(1500f, 900f));
        SpaceTheme.Img(glow, SpaceSprites.Glow, SpaceTheme.A(ACCENT, 0.10f));

        var safe = SpaceTheme.Rect("Safe", rt);
        SpaceTheme.Stretch(safe);
        safe.gameObject.AddComponent<SpaceSafeArea>();

        var caption = SpaceTheme.Text(safe, "Caption", T_CAPTION, 24, SpaceTheme.A(ACCENT, 0.9f), new Vector2(0f, 1f),
            new Vector2(SIDE + 16f, -70f), new Vector2(500f, 36f), FontStyles.Bold, TextAlignmentOptions.Left,
            new Vector2(0f, 0.5f));
        SpaceTheme.Tracking(caption, 18f);
        SpaceTheme.Text(safe, "Title", T_TITLE, 52, SpaceTheme.TEXT, new Vector2(0f, 1f), new Vector2(SIDE + 16f, -126f),
            new Vector2(600f, 66f), FontStyles.Bold, TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
        CloseButton(safe, Close);

        _body = SpaceTheme.Rect("Body", safe);
        SpaceTheme.Stretch(_body);

        _isAdmin = AdminClaim.IsAdmin;
        var today = StoreCalendar.TodayBusiness;
        _thisMonth = new DateTime(today.Year, today.Month, 1);
        _month = _thisMonth;
        ShopCalendar.Changed += OnCalendarChanged;
        StartCoroutine(SpaceTheme.Tween(0.25f, 0f, k => _group.alpha = k));
        Rebuild();
    }

    private void ShowMonth(DateTime month)
    {
        _month = new DateTime(month.Year, month.Month, 1);
        Rebuild();
    }

    private void Rebuild()
    {
        foreach (Transform c in _body) Destroy(c.gameObject);
        BuildMonthHeader();
        SpaceTheme.Text(_body, "Legend", T_LEGEND, 24, SpaceTheme.TEXT_MUTED, TOP, new Vector2(0f, -LEGEND_Y),
            new Vector2(GRID_W, 36f), FontStyles.Bold);
        float gridBottom = BuildGrid();
        BuildList(gridBottom + 18f);
    }

    // ---------------------------------------------------------------- 月の見出し
    private void BuildMonthHeader()
    {
        var min = _thisMonth.AddMonths(_isAdmin ? -MONTHS_BACK_ADMIN : 0);
        var max = _thisMonth.AddMonths(MONTHS_AHEAD);
        SpaceTheme.Text(_body, "Month", $"{_month.Year}{T_YEAR}{_month.Month}{T_MONTH}", 46, SpaceTheme.TEXT, TOP,
            new Vector2(0f, -MONTH_Y), new Vector2(560f, 64f), FontStyles.Bold);
        StepButton("◀", -(GRID_W * 0.5f - 56f), _month > min, () => ShowMonth(_month.AddMonths(-1)));
        StepButton("▶", GRID_W * 0.5f - 56f, _month < max, () => ShowMonth(_month.AddMonths(1)));
    }

    private void StepButton(string label, float x, bool enabled, Action onClick)
    {
        var b = SpaceTheme.Rect(label, _body, TOP, new Vector2(x, -MONTH_Y), new Vector2(112f, 84f));
        var face = SpaceTheme.Rounded(b, SpaceTheme.A(enabled ? ACCENT : SpaceTheme.TEXT_MUTED, enabled ? 0.14f : 0.06f), 42f,
            enabled);
        var t = SpaceTheme.Text(b, "Label", label, 32, enabled ? ACCENT : SpaceTheme.A(SpaceTheme.TEXT_MUTED, 0.45f),
            FontStyles.Bold);
        SpaceTheme.Stretch(t.rectTransform);
        if (enabled) SpaceTheme.Clickable(b.gameObject, face, () => onClick());
    }

    // ---------------------------------------------------------------- カレンダー
    /// <summary>曜日の行と日付のマス（日曜はじまり）。戻り値はマスの下端（上からの位置）</summary>
    private float BuildGrid()
    {
        for (int i = 0; i < 7; i++)
        {
            float x = -GRID_W * 0.5f + CELL_W * (i + 0.5f);
            SpaceTheme.Text(_body, "Week" + i, WEEK[i], 26, WeekColor(i), TOP, new Vector2(x, -WEEK_Y),
                new Vector2(CELL_W, 40f), FontStyles.Bold);
        }

        int lead = (int)_month.DayOfWeek;
        int days = DateTime.DaysInMonth(_month.Year, _month.Month);
        for (int d = 1; d <= days; d++)
        {
            int index = lead + d - 1;
            float x = -GRID_W * 0.5f + CELL_W * (index % 7 + 0.5f);
            float y = -(GRID_TOP + CELL_H * (index / 7 + 0.5f));
            var date = new DateTime(_month.Year, _month.Month, d);
            var cell = SpaceTheme.Rect("Day" + d, _body, TOP, new Vector2(x, y), new Vector2(CELL_W, CELL_H));
            DrawDay(cell, date, MARK, TODAY_RING, 34f, 18f, 12f, -24f);
            if (_isAdmin)
            {
                var hit = SpaceTheme.HitArea(cell);
                SpaceTheme.Clickable(cell.gameObject, hit, () => OpenEditor(date));
            }
        }
        int rows = (lead + days + 6) / 7;
        return GRID_TOP + rows * CELL_H;
    }

    private static Color WeekColor(int i) => i == 0 ? SUNDAY : i == 6 ? SATURDAY
        : StoreCalendar.IsClosed((DayOfWeek)i) ? SpaceTheme.A(SpaceTheme.TEXT_MUTED, 0.7f) : SpaceTheme.TEXT_SUB;

    /// <summary>
    /// 1日のマスの中身（お知らせの小さなカレンダーと共通）: 休みの日（定休日・臨時休業）は薄い数字と「休」「臨休」、
    /// イベントの日は「イベント」、臨時営業は緑の丸と「営業」、今日は水色の〇、過ぎた日は全体を薄く
    /// </summary>
    private static void DrawDay(RectTransform cell, DateTime date, float mark, float ring, float numSize, float labelSize,
        float numY, float labelY)
    {
        var today = StoreCalendar.TodayBusiness;
        var (label, color, filled, closed) = Look(date);
        float markY = (numY + labelY) * 0.5f;
        if (filled)
        {
            var m = SpaceTheme.Rect("Mark", cell, SpaceTheme.CENTER, new Vector2(0f, markY), new Vector2(mark, mark));
            SpaceTheme.Img(m, SpaceSprites.Circle, SpaceTheme.A(color, 0.22f));
        }
        if (date == today)
        {
            var todayRing = SpaceTheme.Rect("Today", cell, SpaceTheme.CENTER, new Vector2(0f, markY),
                new Vector2(ring, ring));
            SpaceTheme.Img(todayRing, SpaceSprites.RingBold, ACCENT);
        }
        int dow = (int)date.DayOfWeek;
        Color numColor = filled ? color
            : closed ? SpaceTheme.A(SpaceTheme.TEXT_MUTED, 0.85f)
            : dow == 0 ? SUNDAY : dow == 6 ? SATURDAY : SpaceTheme.TEXT;
        SpaceTheme.Text(cell, "Num", date.Day.ToString(CultureInfo.InvariantCulture), numSize, numColor, SpaceTheme.CENTER,
            new Vector2(0f, numY), new Vector2(cell.sizeDelta.x, numSize + 12f), FontStyles.Bold);
        // 下の小さな文字は今日の〇の内側の幅まで（〇は下ほど狭いので直径の0.72倍。文字の大きさの設定で大きくしても
        // 「イベント」が〇にかからないよう、SpaceTextScaler がこの幅に収まる大きさまでで止める）
        if (label != null)
            SpaceTheme.Text(cell, "Label", label, labelSize, color, SpaceTheme.CENTER, new Vector2(0f, labelY),
                new Vector2(Mathf.Min(cell.sizeDelta.x, ring * 0.72f), labelSize + 8f), FontStyles.Bold);
        if (date < today) cell.gameObject.AddComponent<CanvasGroup>().alpha = 0.45f;
    }

    /// <summary>その日の見え方: マスの下の小さな文字（ふだんの営業日は null）・その色・緑の丸を付けるか・休みか</summary>
    private static (string label, Color color, bool filled, bool closed) Look(DateTime date)
    {
        if (!ShopCalendar.IsOpen(date))
            return (ShopCalendar.IsTempClosed(date) ? L_CLOSED : L_REGULAR, CLOSED_C, false, true);
        var d = ShopCalendar.Get(date);
        if (d != null && d.Events.Count > 0) return (L_EVENT, EVENT_C, false, false);
        if (ShopCalendar.IsTempOpen(date)) return (L_OPEN, OPEN_C, true, false);
        return (null, SpaceTheme.TEXT, false, false);
    }

    private static string Plain(string s) => SpaceTheme.NoParse(SpaceTheme.SafeNow(s ?? ""));

    // ---------------------------------------------------------------- その月の予定
    private void BuildList(float top)
    {
        var line = SpaceTheme.Rect("Divider", _body, TOP, new Vector2(0f, -top), new Vector2(GRID_W, 2f));
        SpaceTheme.Img(line, SpaceSprites.FadeLine, SpaceTheme.A(SpaceTheme.LINE, 0.3f));
        SpaceTheme.Text(_body, "ListTitle", T_LIST, 30, SpaceTheme.TEXT, TOP, new Vector2(-GRID_W * 0.5f + 12f, -(top + 44f)),
            new Vector2(600f, 46f), FontStyles.Bold, TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
        float listTop = top + 80f;
        if (_isAdmin)
        {
            var hint = SpaceTheme.Text(_body, "AdminHint", T_ADMIN_HINT, 22, SpaceTheme.A(SpaceTheme.GOLD, 0.85f), TOP,
                new Vector2(-GRID_W * 0.5f + 12f, -(top + 84f)), new Vector2(GRID_W - 24f, 34f), FontStyles.Bold,
                TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
            hint.enableAutoSizing = true;
            hint.fontSizeMin = 16f;
            hint.fontSizeMax = 22f;
            listTop += 34f;
        }

        // 1行ずつ: 臨時休業・臨時営業（理由）と、イベント（1件ずつ）
        var rows = new List<(DateTime date, string kind, Color color, string text)>();
        foreach (var d in ShopCalendar.Between(_month, _month.AddMonths(1).AddDays(-1)))
        {
            if (ShopCalendar.IsTempClosed(d.Date)) rows.Add((d.Date, ShopCalendar.T_CLOSED, CLOSED_C, d.Note));
            else if (ShopCalendar.IsTempOpen(d.Date)) rows.Add((d.Date, ShopCalendar.T_OPEN, OPEN_C, d.Note));
            foreach (var e in d.Events) rows.Add((d.Date, ShopCalendar.T_EVENT, EVENT_C, e));
        }

        var viewport = SpaceTheme.Rect("List", _body);
        viewport.anchorMin = new Vector2(0.5f, 0f);
        viewport.anchorMax = new Vector2(0.5f, 1f);
        viewport.pivot = TOP;
        viewport.offsetMin = new Vector2(-(GRID_W * 0.5f + 8f), 24f);
        viewport.offsetMax = new Vector2(GRID_W * 0.5f + 8f, -listTop);
        SpaceTheme.HitArea(viewport);
        viewport.gameObject.AddComponent<RectMask2D>();

        float contentH = rows.Count == 0 ? 120f : rows.Count * (ROW_H + ROW_GAP) + 70f;
        var content = SpaceTheme.Rect("Content", viewport, TOP, Vector2.zero, new Vector2(GRID_W, contentH), TOP);
        var scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Elastic;
        scroll.scrollSensitivity = 40f;

        float y = 8f;
        if (rows.Count == 0)
        {
            SpaceTheme.Text(content, "None", T_LIST_NONE, 28, SpaceTheme.TEXT_SUB, TOP, new Vector2(0f, -(y + 30f)),
                new Vector2(GRID_W - 40f, 44f), FontStyles.Bold);
            y += 64f;
        }
        foreach (var r in rows)
        {
            ListRow(content, y, r.date, r.kind, r.color, r.text);
            y += ROW_H + ROW_GAP;
        }
        SpaceTheme.Text(content, "Regular", T_REGULAR_NOTE, 24, SpaceTheme.TEXT_MUTED, TOP, new Vector2(0f, -(y + 22f)),
            new Vector2(GRID_W - 40f, 36f), FontStyles.Bold);
    }

    /// <summary>1行: 「20日（木）」・種類の札・理由かイベントの名前。管理者は押すとその日の登録のカード</summary>
    private void ListRow(RectTransform content, float y, DateTime date, string kind, Color color, string text)
    {
        var row = SpaceTheme.Rect("Row", content, TOP, new Vector2(0f, -y), new Vector2(GRID_W, ROW_H), TOP);
        var face = SpaceTheme.Glass(row, 30f, new Color(0.07f, 0.09f, 0.24f, 0.78f), SpaceTheme.A(color, 0.3f), true);
        if (_isAdmin) SpaceTheme.Clickable(row.gameObject, face, () => OpenEditor(date));
        if (date < StoreCalendar.TodayBusiness) row.gameObject.AddComponent<CanvasGroup>().alpha = 0.55f;

        string day = $"{date.Day}{T_DAY}（{WEEK[(int)date.DayOfWeek]}）";
        SpaceTheme.Text(row, "Date", day, 30, SpaceTheme.TEXT, new Vector2(0f, 0.5f), new Vector2(28f, 0f),
            new Vector2(190f, 44f), FontStyles.Bold, TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
        var tag = SpaceTheme.Rect("Tag", row, new Vector2(0f, 0.5f), new Vector2(220f, 0f), new Vector2(160f, 50f),
            new Vector2(0f, 0.5f));
        SpaceTheme.Rounded(tag, SpaceTheme.A(color, 0.16f), 25f);
        var tagText = SpaceTheme.Text(tag, "Label", kind, 24, color, FontStyles.Bold);
        SpaceTheme.Stretch(tagText.rectTransform, 8f, 0f, 8f, 0f);
        var note = SpaceTheme.Text(row, "Note", Plain(text), 26, SpaceTheme.TEXT_SUB, new Vector2(0f, 0.5f),
            new Vector2(404f, 0f), new Vector2(GRID_W - 404f - 28f, 44f), FontStyles.Bold, TextAlignmentOptions.Left,
            new Vector2(0f, 0.5f));
        note.overflowMode = TextOverflowModes.Ellipsis;
    }

    // ================================================================
    // 管理者: その日の登録のカード
    // ================================================================
    private const float ED_W = 920f, ED_EVENT_H = 76f, ED_EVENT_GAP = 8f;

    /// <summary>
    /// その日の登録のカード（開いていれば作り直す）。上から「お休み・営業」（理由・臨時休業か臨時営業・ふだんどおりに戻す）、
    /// 「イベント」（名前の入力と追加・登録済みの一覧と消す）、閉じる。入力欄はキーボードで隠れないよう上のほうに置く
    /// </summary>
    private void OpenEditor(DateTime date)
    {
        if (!_isAdmin || _closing || _saving) return;
        CloseEditor();
        var day = ShopCalendar.Get(date);
        bool regularClosed = ShopCalendar.IsRegularClosed(date);
        bool hasStatus = ShopCalendar.IsTempClosed(date) || ShopCalendar.IsTempOpen(date);
        var events = day != null ? new List<string>(day.Events) : new List<string>();

        var root = SpaceTheme.Rect("Editor", transform);
        SpaceTheme.Stretch(root);
        _editor = root.gameObject;
        var dim = SpaceTheme.Rect("Dim", root);
        SpaceTheme.Stretch(dim);
        var dimImg = SpaceTheme.Img(dim, null, SpaceTheme.DIM, true);
        SpaceTheme.Clickable(dim.gameObject, dimImg, CloseEditor, false);
        var safe = SpaceTheme.Rect("Safe", root);
        SpaceTheme.Stretch(safe);
        safe.gameObject.AddComponent<SpaceSafeArea>();

        float listH = events.Count == 0 ? 60f : events.Count * (ED_EVENT_H + ED_EVENT_GAP) - ED_EVENT_GAP;
        float h = 750f + listH;
        var card = SpaceTheme.Rect("Card", safe, TOP, new Vector2(0f, -140f), new Vector2(ED_W, h), TOP);
        SpaceTheme.Glass(card, 56f, SpaceTheme.GLASS_DEEP, SpaceTheme.A(ACCENT, 0.45f), true);
        SpaceTheme.Text(card, "Title", StoreCalendar.MonthDayW(date), 44, SpaceTheme.TEXT, TOP, new Vector2(0f, -64f),
            new Vector2(ED_W - 80f, 60f), FontStyles.Bold);
        var now = SpaceTheme.Text(card, "Now", T_NOW + Describe(date), 26, SpaceTheme.TEXT_SUB, TOP, new Vector2(0f, -116f),
            new Vector2(ED_W - 80f, 40f), FontStyles.Bold);
        now.enableAutoSizing = true;
        now.fontSizeMin = 18f;
        now.fontSizeMax = 26f;

        // お休み・営業
        SectionLabel(card, T_SEC_STATUS, -170f);
        var reason = TextInput(card, new Vector2(0f, -196f), ED_W - 100f, T_REASON_PH, hasStatus ? day.Note : "");
        if (regularClosed)
            EditorButton(card, T_SET_OPEN, new Vector2(-210f, -354f), 400f, OPEN_C,
                () => SaveStatus(date, ShopCalendar.Status.Open, reason.text));
        else
            EditorButton(card, T_SET_CLOSED, new Vector2(-210f, -354f), 400f, SpaceTheme.TEXT,
                () => SaveStatus(date, ShopCalendar.Status.Closed, reason.text));
        EditorButton(card, T_RESET, new Vector2(210f, -354f), 400f, SpaceTheme.TEXT_SUB,
            hasStatus ? (Action)(() => SaveStatus(date, ShopCalendar.Status.None, "")) : null);

        // イベント（名前を入れて追加 → 下に一覧）
        SectionLabel(card, $"{T_SEC_EVENTS}（{events.Count}/{ShopCalendar.EVENTS_MAX}）", -452f);
        bool full = events.Count >= ShopCalendar.EVENTS_MAX;
        var name = TextInput(card, new Vector2(-(ED_W - 100f) * 0.5f + 300f, -478f), 600f, T_NAME_PH, ""); // 左端をカードの内側にそろえる
        EditorButton(card, T_ADD, new Vector2(ED_W * 0.5f - 50f - 100f, -524f), 200f, ACCENT,
            full ? null : (Action)(() => AddEvent(date, name.text)), 92f);
        float y = 586f;
        if (events.Count == 0)
        {
            SpaceTheme.Text(card, "NoEvents", T_NO_EVENTS, 24, SpaceTheme.TEXT_MUTED, TOP, new Vector2(0f, -(y + 30f)),
                new Vector2(ED_W - 100f, 40f), FontStyles.Bold);
        }
        foreach (var e in events)
        {
            EventRow(card, y, e, () => RemoveEvent(date, e));
            y += ED_EVENT_H + ED_EVENT_GAP;
        }

        EditorButton(card, T_CLOSE, new Vector2(0f, -(586f + listH + 28f + 50f)), 400f, SpaceTheme.TEXT_SUB, CloseEditor);
        root.gameObject.AddComponent<SpacePopIn>().Target = card;
    }

    private void CloseEditor()
    {
        if (_editor != null) Destroy(_editor);
        _editor = null;
    }

    /// <summary>いまの設定の説明（「臨時休業（設備点検のため）・イベント 2件」など）</summary>
    private static string Describe(DateTime date)
    {
        var d = ShopCalendar.Get(date);
        int events = d != null ? d.Events.Count : 0;
        string status;
        if (ShopCalendar.IsTempClosed(date))
            status = ShopCalendar.T_CLOSED + (string.IsNullOrEmpty(d.Note) ? "" : "（" + Plain(d.Note) + "）");
        else if (ShopCalendar.IsTempOpen(date))
            status = ShopCalendar.T_OPEN + (string.IsNullOrEmpty(d.Note) ? "" : "（" + Plain(d.Note) + "）");
        else if (ShopCalendar.IsRegularClosed(date))
            status = events > 0 ? T_EVENT_OPEN : T_NORMAL_CLOSED;
        else
            status = T_NORMAL_OPEN;
        return events > 0 ? status + T_EVENT_COUNT_A + events + T_EVENT_COUNT_B : status;
    }

    private static void SectionLabel(RectTransform card, string text, float y) =>
        SpaceTheme.Text(card, "Section_" + text, text, 28, SpaceTheme.TEXT, TOP, new Vector2(-ED_W * 0.5f + 52f, y),
            new Vector2(600f, 40f), FontStyles.Bold, TextAlignmentOptions.Left, new Vector2(0f, 0.5f));

    /// <summary>登録済みのイベント1件（名前と「消す」）</summary>
    private static void EventRow(RectTransform card, float y, string name, Action onRemove)
    {
        var row = SpaceTheme.Rect("Event", card, TOP, new Vector2(0f, -y), new Vector2(ED_W - 100f, ED_EVENT_H), TOP);
        SpaceTheme.Glass(row, 28f, new Color(0.07f, 0.09f, 0.24f, 0.85f), SpaceTheme.A(EVENT_C, 0.3f));
        var t = SpaceTheme.Text(row, "Name", Plain(name), 28, SpaceTheme.TEXT, new Vector2(0f, 0.5f), new Vector2(28f, 0f),
            new Vector2(ED_W - 100f - 28f - 170f, 44f), FontStyles.Bold, TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
        t.overflowMode = TextOverflowModes.Ellipsis;
        var b = SpaceTheme.Rect("Remove", row, new Vector2(1f, 0.5f), new Vector2(-14f, 0f), new Vector2(140f, 56f),
            new Vector2(1f, 0.5f));
        var face = SpaceTheme.Rounded(b, SpaceTheme.A(SpaceTheme.TEXT_SUB, 0.14f), 28f, true);
        var label = SpaceTheme.Text(b, "Label", T_REMOVE, 26, SpaceTheme.TEXT_SUB, FontStyles.Bold);
        SpaceTheme.Stretch(label.rectTransform);
        SpaceTheme.Clickable(b.gameObject, face, () => onRemove());
    }

    /// <summary>登録のカードのボタン（onClick が null なら押せない薄い見た目）。pos はボタンの中心</summary>
    private static void EditorButton(RectTransform card, string label, Vector2 pos, float w, Color color, Action onClick,
        float h = 100f)
    {
        var b = SpaceTheme.Rect(label, card, TOP, pos, new Vector2(w, h));
        var face = SpaceTheme.Glass(b, h * 0.46f, SpaceTheme.A(color, 0.14f), SpaceTheme.A(color, 0.6f), onClick != null);
        var t = SpaceTheme.Text(b, "Label", label, 30, color, FontStyles.Bold);
        SpaceTheme.Stretch(t.rectTransform, 16f, 0f, 16f, 0f);
        t.enableAutoSizing = true;
        t.fontSizeMin = 22f;
        t.fontSizeMax = 30f;
        if (onClick != null) SpaceTheme.Clickable(b.gameObject, face, () => onClick());
        else b.gameObject.AddComponent<CanvasGroup>().alpha = 0.35f;
    }

    /// <summary>1行の入力欄（30文字まで・絵文字は受け付けない）。pos は欄の上端の中心</summary>
    private TMP_InputField TextInput(RectTransform parent, Vector2 pos, float w, string placeholder, string value)
    {
        var bar = SpaceTheme.Rect("Input_" + placeholder, parent, TOP, pos, new Vector2(w, 92f), TOP);
        var face = SpaceTheme.Glass(bar, 30f, new Color(0.03f, 0.04f, 0.12f, 0.9f), SpaceTheme.A(ACCENT, 0.45f), true);
        var area = SpaceTheme.Rect("TextArea", bar);
        SpaceTheme.Stretch(area, 28f, 8f, 28f, 8f);
        area.gameObject.AddComponent<RectMask2D>();
        var ph = SpaceTheme.Text(area, "Placeholder", placeholder, 28, SpaceTheme.TEXT_MUTED, FontStyles.Bold,
            TextAlignmentOptions.Left);
        SpaceTheme.Stretch(ph.rectTransform);
        ph.enableAutoSizing = true;
        ph.fontSizeMin = 18f;
        ph.fontSizeMax = 28f;
        var tx = SpaceTheme.Text(area, "Text", "", 30, SpaceTheme.TEXT, FontStyles.Bold, TextAlignmentOptions.Left);
        SpaceTheme.Stretch(tx.rectTransform);

        var input = bar.gameObject.AddComponent<TMP_InputField>();
        input.targetGraphic = face;
        input.textViewport = area;
        input.textComponent = tx;
        input.placeholder = ph;
        input.fontAsset = SpaceTheme.Font;
        input.pointSize = 30;
        input.richText = false;
        input.characterLimit = ShopCalendar.TEXT_MAX;
        input.lineType = TMP_InputField.LineType.SingleLine;
        input.customCaretColor = true;
        input.caretColor = ACCENT;
        input.selectionColor = SpaceTheme.A(ACCENT, 0.35f);
        input.onValidateInput = RejectEmoji;
        input.text = SpaceTheme.Safe(value ?? "");
        return input;
    }

    private char RejectEmoji(string text, int index, char ch)
    {
        if (!char.IsSurrogate(ch) && ch != '️' && ch != '‍' && ch != '⃣') return ch;
        if (Time.unscaledTime >= _emojiToastUntil)
        {
            _emojiToastUntil = Time.unscaledTime + 2f;
            SpaceTheme.Toast(T_NO_EMOJI, SpaceTheme.MainCanvas());
        }
        return '\0';
    }

    private void SaveStatus(DateTime date, ShopCalendar.Status status, string note)
    {
        string done = status == ShopCalendar.Status.Closed ? T_DONE_CLOSED
            : status == ShopCalendar.Status.Open ? T_DONE_OPEN : T_DONE_RESET;
        WriteAsync(date, () => ShopCalendar.SetStatusAsync(date, status, note), done).Forget();
    }

    private void AddEvent(DateTime date, string name)
    {
        name = ShopCalendar.Clean(name);
        var canvas = SpaceTheme.MainCanvas();
        if (name.Length == 0)
        {
            SpaceTheme.Toast(T_NEED_NAME, canvas);
            return;
        }
        var d = ShopCalendar.Get(date);
        if (d != null && d.Events.Contains(name))
        {
            SpaceTheme.Toast(T_DUPLICATE, canvas);
            return;
        }
        if (d != null && d.Events.Count >= ShopCalendar.EVENTS_MAX)
        {
            SpaceTheme.Toast(T_EVENTS_FULL_A + ShopCalendar.EVENTS_MAX + T_EVENTS_FULL_B, canvas);
            return;
        }
        WriteAsync(date, () => ShopCalendar.AddEventAsync(date, name), T_DONE_ADD).Forget();
    }

    private void RemoveEvent(DateTime date, string name) =>
        WriteAsync(date, () => ShopCalendar.RemoveEventAsync(date, name), T_DONE_REMOVE).Forget();

    /// <summary>
    /// 保存する（処理中の表示を出し、終わったら結果を知らせる）。カレンダーは ShopCalendar.Changed で描き直し、
    /// 登録のカードは開いたまま中身を新しくする（続けてイベントを足せる）
    /// </summary>
    private async UniTaskVoid WriteAsync(DateTime date, Func<UniTask<bool>> write, string done)
    {
        if (_saving) return;
        _saving = true;
        var canvas = SpaceTheme.MainCanvas();
        var busy = SpaceDialog.Busy(canvas, T_SAVING);
        bool ok = await write();
        if (busy != null) Destroy(busy);
        _saving = false;
        SpaceTheme.Toast(ok ? StoreCalendar.MonthDayW(date) + done : T_FAILED, canvas);
        if (this != null && !_closing && _editor != null) OpenEditor(date);
    }

    private static void CloseButton(RectTransform safe, Action onClick)
    {
        var rt = SpaceTheme.Rect("Close", safe, new Vector2(1f, 1f), new Vector2(-(SIDE + 56f), -84f), new Vector2(104f, 104f));
        var face = SpaceTheme.Img(rt, SpaceSprites.Circle, new Color(0.05f, 0.07f, 0.19f, 0.85f), true);
        var rim = SpaceTheme.Rect("Rim", rt);
        SpaceTheme.Stretch(rim);
        SpaceTheme.Img(rim, SpaceSprites.RingSmall, SpaceTheme.STROKE);
        SpaceTheme.Bar(rt, Vector2.zero, 44f, 6f, 45f, SpaceTheme.TEXT);
        SpaceTheme.Bar(rt, Vector2.zero, 44f, 6f, -45f, SpaceTheme.TEXT);
        SpaceTheme.Clickable(rt.gameObject, face, () => onClick());
    }

    // ================================================================
    // お知らせの欄: 今週と来週の2週間（日曜はじまり）
    // ================================================================
    public const float WEEKS_H = 300f;

    /// <summary>
    /// お知らせの「営業カレンダー」の欄に出す小さなカレンダー（今週と来週）を card の中に描く
    /// （card には面の画像を付けるので、作り直すときは card ごと作り直す）。戻り値は押せる面（開くのは呼び出し側）
    /// </summary>
    public static Image BuildWeeks(RectTransform card, float width)
    {
        var face = SpaceTheme.Glass(card, 36f, new Color(0.07f, 0.09f, 0.24f, 0.78f), SpaceTheme.A(ACCENT, 0.35f), true);
        const float pad = 30f, weekY = 38f, cellTop = 64f, cellH = 88f;
        float colW = (width - pad * 2f) / 7f;
        var today = StoreCalendar.TodayBusiness;
        var first = today.AddDays(-(int)today.DayOfWeek); // 今週の日曜

        for (int i = 0; i < 7; i++)
        {
            float x = pad + colW * (i + 0.5f);
            SpaceTheme.Text(card, "Week" + i, WEEK[i], 22, WeekColor(i), new Vector2(0f, 1f), new Vector2(x, -weekY),
                new Vector2(colW, 32f), FontStyles.Bold);
        }
        for (int n = 0; n < 14; n++)
        {
            var date = first.AddDays(n);
            float x = pad + colW * (n % 7 + 0.5f);
            float y = -(cellTop + cellH * (n / 7 + 0.5f));
            var cell = SpaceTheme.Rect("Day" + n, card, new Vector2(0f, 1f), new Vector2(x, y), new Vector2(colW, cellH));
            DrawDay(cell, date, 72f, WEEKS_TODAY_RING, 30f, 16f, 10f, -20f);
        }

        float footY = -(cellTop + cellH * 2f + 28f);
        SpaceTheme.Text(card, "Regular", T_REGULAR_NOTE, 22, SpaceTheme.TEXT_MUTED, new Vector2(0f, 1f),
            new Vector2(pad + 8f, footY), new Vector2(width * 0.55f, 34f), FontStyles.Bold, TextAlignmentOptions.Left,
            new Vector2(0f, 0.5f));
        SpaceTheme.Text(card, "SeeAll", T_SEE_ALL, 24, ACCENT, new Vector2(1f, 1f), new Vector2(-(pad + 8f), footY),
            new Vector2(width * 0.4f, 34f), FontStyles.Bold, TextAlignmentOptions.Right, new Vector2(1f, 0.5f));
        return face;
    }

    /// <summary>お知らせの「営業カレンダー」の見出しの右に出す一言（今日・近いうちの臨時休業）。無ければ空</summary>
    public static string InfoText(out Color color)
    {
        color = SpaceTheme.TEXT_SUB;
        var today = StoreCalendar.TodayBusiness;
        if (ShopCalendar.IsTempClosed(today)) return T_INFO_TODAY;
        var next = ShopCalendar.UpcomingClosures(today.AddDays(1), 60);
        if (next.Count == 0) return "";
        var d = next[0].Date;
        return $"{T_INFO_NEXT}{d.Month}/{d.Day}（{WEEK[(int)d.DayOfWeek]}）";
    }
}
