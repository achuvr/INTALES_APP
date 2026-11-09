using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 遊んだ記録のカレンダー（ボードゲーム図鑑の右上の「記録」から開く全画面。2026-11-09 ユーザー指定）。
///   ・月のカレンダーで、遊んだ記録のある日に〇を付ける（◀▶で月を変える。この先の月には進まない）
///   ・日付を押すと、その日の記録の一覧を下に出す。記録を押すと1件の画面（SpacePlayLog.OpenView。編集・消すもそこから）
/// 図鑑の子として開く（戻る操作は図鑑の HandleBack から Close）。記録は端末の中だけ（PlayLogStore）。
/// </summary>
public class SpacePlayCalendar : MonoBehaviour
{
    public const string NAME = "__PlayCalendar";

    private static readonly Color ACCENT = SpaceBoardGameZukan.ACCENT; // 図鑑の緑（「記録する」と同じ色）
    private static readonly Color SUNDAY = SpaceTheme.Hex(0xFF8FA8);
    private static readonly Color SATURDAY = SpaceTheme.Hex(0x7FC8FF);
    private static readonly Color CARD_FILL = new Color(0.07f, 0.09f, 0.24f, 0.85f);

    private const string T_CAPTION = "PLAY LOG";
    private const string T_TITLE = "遊んだ記録";
    private const string T_YEAR = "年";
    private const string T_MONTH = "月";
    private const string T_SUMMARY_A = "この月に遊んだ日 ";
    private const string T_SUMMARY_B = "日・記録 ";
    private const string T_SUMMARY_C = "件";
    private const string T_SUMMARY_NONE = "この月の記録はありません";
    private const string T_HINT = "記録のある日を押すと、その日の記録が見られます";
    private const string T_NO_LOGS = "まだ記録がありません。図鑑のゲームの詳細の「記録する」から残せます";
    private const string T_DAY_NONE = "この日の記録はありません";
    private const string T_COUNT = "件";
    private const string T_WITH = "一緒に遊んだ人";
    private const string T_ALONE = "なし";
    private static readonly string[] WEEK = { "日", "月", "火", "水", "木", "金", "土" };

    /// <summary>この画面で表示しうる決まった文言（ゲームの名前・プレイヤーの名前・メモは一覧を出す前に登録する）</summary>
    public static string GlyphText => new StringBuilder()
        .Append(T_CAPTION).Append(T_TITLE).Append(T_YEAR).Append(T_MONTH).Append(T_SUMMARY_A).Append(T_SUMMARY_B)
        .Append(T_SUMMARY_C).Append(T_SUMMARY_NONE).Append(T_HINT).Append(T_NO_LOGS).Append(T_DAY_NONE).Append(T_COUNT)
        .Append(T_WITH).Append(T_ALONE).Append(string.Concat(WEEK)).Append("◀▶（）　、?0123456789")
        .Append(StoreCalendar.GlyphText).ToString();

    private const float SIDE = 40f;
    private const float GRID_W = 1000f;                          // カレンダーと一覧の幅
    private const float CELL_W = GRID_W / 7f, CELL_H = 112f;
    private const float RING = 92f;                              // 遊んだ日の〇の直径
    private const float MONTH_Y = 238f, SUMMARY_Y = 308f, WEEK_Y = 364f, GRID_TOP = 392f; // 上からの位置
    private const float ROW_H = 168f, ROW_GAP = 16f, THUMB = 132f, LIST_PAD = 12f;

    private Transform _host;                            // 図鑑（1件の記録の画面はここに重ねる）
    private Dictionary<string, BoardGameEntry> _games;  // ゲームのキー → 図鑑のゲーム
    private Action<BoardGameEntry> _onChanged;          // 記録を直した・消したとき（図鑑の「遊んだ」・サムネイルを新しく）
    private CanvasGroup _group;
    private RectTransform _body;                        // 月の見出し・カレンダー・その日の一覧（作り直す部分）
    private DateTime _month;                            // 表示している月の1日
    private DateTime? _selected;                        // 選んでいる日（null＝まだ選んでいない）
    private readonly List<Texture2D> _textures = new List<Texture2D>();
    private bool _closing;
    private int _version;                               // 作り直すたびに増やす（古い一覧の組み立てを止める）

    // ================================================================
    // 開く・閉じる
    // ================================================================
    public static SpacePlayCalendar Open(Transform host, IEnumerable<BoardGameEntry> games, Action<BoardGameEntry> onChanged)
    {
        var opened = host.Find(NAME);
        if (opened != null) return opened.GetComponent<SpacePlayCalendar>();
        var root = SpaceTheme.Rect(NAME, host);
        SpaceTheme.Stretch(root);
        root.SetAsLastSibling();
        var page = root.gameObject.AddComponent<SpacePlayCalendar>();
        page._host = host;
        page._onChanged = onChanged;
        page._games = new Dictionary<string, BoardGameEntry>();
        foreach (var g in games)
        {
            string key = BoardGameMarks.KeyOf(g);
            if (!string.IsNullOrEmpty(key) && !page._games.ContainsKey(key)) page._games[key] = g;
        }
        page.Build();
        return page;
    }

    public void Close()
    {
        if (_closing) return;
        _closing = true;
        StartCoroutine(CloseRoutine());
    }

    private IEnumerator CloseRoutine()
    {
        float from = _group.alpha;
        yield return SpaceTheme.Tween(0.2f, 0f, k => _group.alpha = from * (1f - k));
        Destroy(gameObject);
    }

    private void OnDestroy()
    {
        foreach (var t in _textures) if (t != null) Destroy(t);
        _textures.Clear();
    }

    // ================================================================
    // 組み立て
    // ================================================================
    private void Build()
    {
        var rt = (RectTransform)transform;
        _group = gameObject.AddComponent<CanvasGroup>();
        _group.alpha = 0f;

        // 図鑑を覆う暗い宇宙（下の図鑑を押させない）
        var dim = SpaceTheme.Rect("Dim", rt);
        SpaceTheme.Stretch(dim);
        SpaceTheme.Img(dim, null, new Color(0.01f, 0.015f, 0.05f, 0.98f), true);
        var glow = SpaceTheme.Rect("Glow", rt, new Vector2(0.5f, 1f), new Vector2(0f, -160f), new Vector2(1500f, 900f));
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

        StartCoroutine(SpaceTheme.Tween(0.25f, 0f, k => _group.alpha = k));
        ShowMonth(DateTime.Today);
    }

    /// <summary>その月を出す。記録があれば、その月のいちばん最近の日を選んで一覧も出す</summary>
    private void ShowMonth(DateTime month)
    {
        _month = new DateTime(month.Year, month.Month, 1);
        _selected = LatestDayIn(_month, PlayLogStore.All());
        Rebuild();
    }

    private void SelectDay(DateTime day)
    {
        _selected = day.Date;
        Rebuild();
    }

    /// <summary>月の見出し・カレンダー・その日の一覧を作り直す（記録が変わったときも）</summary>
    private void Rebuild()
    {
        int version = ++_version;
        foreach (Transform c in _body) Destroy(c.gameObject);
        foreach (var t in _textures) if (t != null) Destroy(t);
        _textures.Clear();

        var all = PlayLogStore.All();
        var counts = CountsOf(_month, all);
        BuildMonthHeader(counts);
        float gridBottom = BuildGrid(counts);
        StartCoroutine(BuildDay(version, gridBottom + 18f, all, counts.Count > 0));
    }

    // ---------------------------------------------------------------- 月の見出し
    private void BuildMonthHeader(Dictionary<int, int> counts)
    {
        var today = DateTime.Today;
        bool canNext = _month < new DateTime(today.Year, today.Month, 1); // この先の月には記録が無いので進まない

        SpaceTheme.Text(_body, "Month", $"{_month.Year}{T_YEAR}{_month.Month}{T_MONTH}", 46, SpaceTheme.TEXT,
            new Vector2(0.5f, 1f), new Vector2(0f, -MONTH_Y), new Vector2(560f, 64f), FontStyles.Bold);
        StepButton("◀", -(GRID_W * 0.5f - 56f), true, () => ShowMonth(_month.AddMonths(-1)));
        StepButton("▶", GRID_W * 0.5f - 56f, canNext, () => ShowMonth(_month.AddMonths(1)));

        int logs = 0;
        foreach (var n in counts.Values) logs += n;
        string accent = "#" + ColorUtility.ToHtmlStringRGB(ACCENT);
        string summary = counts.Count > 0
            ? $"{T_SUMMARY_A}<color={accent}>{counts.Count}</color>{T_SUMMARY_B}<color={accent}>{logs}</color>{T_SUMMARY_C}"
            : T_SUMMARY_NONE;
        SpaceTheme.Text(_body, "Summary", summary, 26, SpaceTheme.TEXT_SUB, new Vector2(0.5f, 1f), new Vector2(0f, -SUMMARY_Y),
            new Vector2(GRID_W, 40f), FontStyles.Bold);
    }

    /// <summary>月を変えるボタン（記録の入力の日付送りと同じ見た目）。押せないときは薄く</summary>
    private void StepButton(string label, float x, bool enabled, Action onClick)
    {
        var b = SpaceTheme.Rect(label, _body, new Vector2(0.5f, 1f), new Vector2(x, -MONTH_Y), new Vector2(112f, 84f));
        var face = SpaceTheme.Rounded(b, SpaceTheme.A(enabled ? ACCENT : SpaceTheme.TEXT_MUTED, enabled ? 0.14f : 0.06f), 42f,
            enabled);
        var t = SpaceTheme.Text(b, "Label", label, 32, enabled ? ACCENT : SpaceTheme.A(SpaceTheme.TEXT_MUTED, 0.45f),
            FontStyles.Bold);
        SpaceTheme.Stretch(t.rectTransform);
        if (enabled) SpaceTheme.Clickable(b.gameObject, face, () => onClick());
    }

    // ---------------------------------------------------------------- カレンダー
    /// <summary>曜日の行と日付のマス（日曜はじまり）。戻り値はマスの下端（上からの位置）</summary>
    private float BuildGrid(Dictionary<int, int> counts)
    {
        for (int i = 0; i < 7; i++)
        {
            float x = -GRID_W * 0.5f + CELL_W * (i + 0.5f);
            SpaceTheme.Text(_body, "Week" + i, WEEK[i], 26, i == 0 ? SUNDAY : i == 6 ? SATURDAY : SpaceTheme.TEXT_MUTED,
                new Vector2(0.5f, 1f), new Vector2(x, -WEEK_Y), new Vector2(CELL_W, 40f), FontStyles.Bold);
        }

        int lead = (int)_month.DayOfWeek; // 1日の前の空きマス
        int days = DateTime.DaysInMonth(_month.Year, _month.Month);
        for (int d = 1; d <= days; d++)
        {
            int index = lead + d - 1;
            DayCell(new DateTime(_month.Year, _month.Month, d), index % 7, index / 7, counts.TryGetValue(d, out var n) ? n : 0);
        }
        int rows = (lead + days + 6) / 7;
        return GRID_TOP + rows * CELL_H;
    }

    /// <summary>
    /// 日付のマス。遊んだ日は〇、選んでいる日はうっすら塗った円、今日は数字の下に点。
    /// この先の日は薄くして押せない（先の日の記録は残せないため）
    /// </summary>
    private void DayCell(DateTime date, int col, int row, int count)
    {
        var today = DateTime.Today;
        bool future = date > today;
        float x = -GRID_W * 0.5f + CELL_W * (col + 0.5f);
        float y = -(GRID_TOP + CELL_H * (row + 0.5f));
        var cell = SpaceTheme.Rect("Day" + date.Day, _body, new Vector2(0.5f, 1f), new Vector2(x, y), new Vector2(CELL_W, CELL_H));

        if (_selected.HasValue && _selected.Value == date)
        {
            var sel = SpaceTheme.Rect("Selected", cell, SpaceTheme.CENTER, Vector2.zero, new Vector2(RING + 6f, RING + 6f));
            SpaceTheme.Img(sel, SpaceSprites.Circle, SpaceTheme.A(ACCENT, 0.24f));
        }
        if (count > 0)
        {
            var ring = SpaceTheme.Rect("Played", cell, SpaceTheme.CENTER, Vector2.zero, new Vector2(RING, RING));
            SpaceTheme.Img(ring, SpaceSprites.RingBold, ACCENT);
        }

        int dow = (int)date.DayOfWeek;
        Color c = future ? SpaceTheme.A(SpaceTheme.TEXT_MUTED, 0.5f)
            : dow == 0 ? SUNDAY : dow == 6 ? SATURDAY : SpaceTheme.TEXT;
        var num = SpaceTheme.Text(cell, "Num", date.Day.ToString(CultureInfo.InvariantCulture), 34, c, FontStyles.Bold);
        num.rectTransform.sizeDelta = new Vector2(CELL_W, 50f);

        if (date == today)
        {
            var dot = SpaceTheme.Rect("Today", cell, SpaceTheme.CENTER, new Vector2(0f, -30f), new Vector2(9f, 9f));
            SpaceTheme.Img(dot, SpaceSprites.Circle, SpaceTheme.TEXT);
        }

        if (!future)
        {
            var hit = SpaceTheme.HitArea(cell);
            SpaceTheme.Clickable(cell.gameObject, hit, () => SelectDay(date));
        }
    }

    // ---------------------------------------------------------------- その日の一覧
    private IEnumerator BuildDay(int version, float top, List<PlayLogEntry> all, bool monthHasLogs)
    {
        var line = SpaceTheme.Rect("Divider", _body, new Vector2(0.5f, 1f), new Vector2(0f, -top), new Vector2(GRID_W, 2f));
        SpaceTheme.Img(line, SpaceSprites.FadeLine, SpaceTheme.A(SpaceTheme.LINE, 0.3f));

        if (!_selected.HasValue)
        {
            string msg = all.Count == 0 ? T_NO_LOGS : monthHasLogs ? T_HINT : null;
            if (msg != null) Message(msg, top + 90f);
            yield break;
        }

        var day = _selected.Value;
        var logs = all.FindAll(l => l.Date == day);
        logs.Sort((a, b) => string.CompareOrdinal(a.id, b.id)); // 残した順（その日に遊んだ順のことが多い）

        // 見出し: 「11月9日（月）」と件数
        SpaceTheme.Text(_body, "Day", StoreCalendar.MonthDayW(day), 34, SpaceTheme.TEXT, new Vector2(0.5f, 1f),
            new Vector2(-GRID_W * 0.5f + 12f, -(top + 46f)), new Vector2(640f, 50f), FontStyles.Bold,
            TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
        if (logs.Count == 0)
        {
            Message(T_DAY_NONE, top + 130f);
            yield break;
        }
        SpaceTheme.Text(_body, "Count", logs.Count + T_COUNT, 28, SpaceTheme.TEXT_SUB, new Vector2(0.5f, 1f),
            new Vector2(GRID_W * 0.5f - 12f, -(top + 46f)), new Vector2(240f, 44f), FontStyles.Bold,
            TextAlignmentOptions.Right, new Vector2(1f, 0.5f));

        // ゲーム・プレイヤーの名前やメモのうち、まだフォントに無い文字を少しずつ登録してから並べる
        // （一度に大量に登録すると実機で落ちるため）
        var sb = new StringBuilder();
        foreach (var log in logs)
            sb.Append(GameOf(log.key).title).Append(SpacePlayLog.PlayersText(log)).Append(SpacePlayLog.ResultText(log))
              .Append(FirstLine(log.memo)).Append(Initial(GameOf(log.key).title));
        yield return SpaceTheme.Prewarm(sb.ToString());
        if (this == null || version != _version) yield break;

        var content = BuildScroll(top + 84f, LIST_PAD * 2f + logs.Count * (ROW_H + ROW_GAP) - ROW_GAP);
        float y = LIST_PAD;
        foreach (var log in logs)
        {
            LogRow(content, log, y);
            y += ROW_H + ROW_GAP;
        }
    }

    private void Message(string text, float y)
    {
        var t = SpaceTheme.Text(_body, "Message", text, 28, SpaceTheme.TEXT_MUTED, new Vector2(0.5f, 1f), new Vector2(0f, -y),
            new Vector2(GRID_W - 80f, 100f), FontStyles.Bold);
        t.textWrappingMode = TextWrappingModes.Normal;
    }

    private RectTransform BuildScroll(float top, float contentH)
    {
        var viewport = SpaceTheme.Rect("List", _body);
        viewport.anchorMin = new Vector2(0.5f, 0f);
        viewport.anchorMax = new Vector2(0.5f, 1f);
        viewport.pivot = new Vector2(0.5f, 1f);
        viewport.offsetMin = new Vector2(-(GRID_W * 0.5f + 8f), 24f);
        viewport.offsetMax = new Vector2(GRID_W * 0.5f + 8f, -top);
        SpaceTheme.HitArea(viewport);
        viewport.gameObject.AddComponent<RectMask2D>().softness = new Vector2Int(0, 20);
        var content = SpaceTheme.Rect("Content", viewport, new Vector2(0.5f, 1f), Vector2.zero, new Vector2(GRID_W, contentH),
            new Vector2(0.5f, 1f));
        var scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Elastic;
        scroll.scrollSensitivity = 30f;
        scroll.viewport = viewport;
        scroll.content = content;
        return content;
    }

    /// <summary>記録1件の行: 左に1枚目の写真（無ければゲーム名の頭文字）、右にゲーム名・一緒に遊んだ人・結果（無ければメモ）</summary>
    private void LogRow(RectTransform content, PlayLogEntry log, float y)
    {
        var g = GameOf(log.key);
        var row = SpaceTheme.Rect("Log", content, new Vector2(0.5f, 1f), new Vector2(0f, -y), new Vector2(GRID_W, ROW_H),
            new Vector2(0.5f, 1f));
        var face = SpaceTheme.Glass(row, 36f, CARD_FILL, SpaceTheme.A(ACCENT, 0.32f), true);
        SpaceTheme.Clickable(row.gameObject, face, () => OpenLog(g, log));

        var thumb = SpaceTheme.Rect("Thumb", row, new Vector2(0f, 0.5f), new Vector2(18f + THUMB * 0.5f, 0f),
            new Vector2(THUMB, THUMB));
        SpaceTheme.Rounded(thumb, SpaceTheme.A(ACCENT, 0.14f), 28f);
        thumb.gameObject.AddComponent<Mask>().showMaskGraphic = true; // 角丸に切り抜く
        Texture2D tex = null;
        if (log.photos != null && log.photos.Count > 0)
        {
            string path = BoardGamePhotoStore.PathOf(g, log.photos[0]);
            if (File.Exists(path)) tex = BoardGamePhotoStore.LoadThumbTexture(path);
        }
        if (tex != null)
        {
            _textures.Add(tex);
            float k = THUMB / Mathf.Max(1, Mathf.Min(tex.width, tex.height));
            var img = SpaceTheme.Rect("Img", thumb, SpaceTheme.CENTER, Vector2.zero, new Vector2(tex.width * k, tex.height * k));
            var raw = img.gameObject.AddComponent<RawImage>();
            raw.texture = tex;
            raw.raycastTarget = false;
        }
        else
        {
            var initial = SpaceTheme.Text(thumb, "Initial", Plain(Initial(g.title)), 56, ACCENT, FontStyles.Bold);
            initial.rectTransform.sizeDelta = new Vector2(THUMB, THUMB);
        }

        float left = 18f + THUMB + 28f;
        float textW = GRID_W - left - 72f;
        var title = SpaceTheme.Text(row, "Title", Plain(g.title), 34, SpaceTheme.TEXT, new Vector2(0f, 1f),
            new Vector2(left, -42f), new Vector2(textW, 48f), FontStyles.Bold, TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
        title.overflowMode = TextOverflowModes.Ellipsis;

        string muted = "#" + ColorUtility.ToHtmlStringRGB(SpaceTheme.TEXT_MUTED);
        var who = SpaceTheme.Text(row, "Players", $"<color={muted}>{T_WITH}</color>　" + Plain(SpacePlayLog.PlayersText(log) ?? T_ALONE),
            26, SpaceTheme.TEXT_SUB, new Vector2(0f, 1f), new Vector2(left, -88f), new Vector2(textW, 40f), FontStyles.Bold,
            TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
        who.overflowMode = TextOverflowModes.Ellipsis;

        string result = SpacePlayLog.ResultText(log);
        string third = result ?? FirstLine(log.memo);
        if (!string.IsNullOrEmpty(third))
        {
            Color rc = result == null || log.coop == PlayLogEntry.COOP_FAIL ? SpaceTheme.TEXT_MUTED
                : log.coop == PlayLogEntry.COOP_SUCCESS ? SpaceTheme.MINT : SpaceTheme.GOLD;
            var t = SpaceTheme.Text(row, result != null ? "Result" : "Memo", Plain(third), 26, rc, new Vector2(0f, 1f),
                new Vector2(left, -130f), new Vector2(textW, 40f), result != null ? FontStyles.Bold : FontStyles.Normal,
                TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
            t.overflowMode = TextOverflowModes.Ellipsis;
        }

        // 押すと開くことがわかる「＞」
        var chev = SpaceTheme.Rect("Chevron", row, new Vector2(1f, 0.5f), new Vector2(-40f, 0f), new Vector2(30f, 30f));
        SpaceTheme.Bar(chev, new Vector2(0f, 6f), 18f, 4f, -45f, SpaceTheme.TEXT_MUTED);
        SpaceTheme.Bar(chev, new Vector2(0f, -6f), 18f, 4f, 45f, SpaceTheme.TEXT_MUTED);
    }

    /// <summary>1件の記録の画面を図鑑の上（このカレンダーより手前）に開く。直した・消したらカレンダーも作り直す</summary>
    private void OpenLog(BoardGameEntry g, PlayLogEntry log)
    {
        SpacePlayLog.OpenView(_host, g, log, () =>
        {
            _onChanged?.Invoke(g);
            if (this != null && !_closing) Rebuild();
        });
    }

    // ================================================================
    // 部品・計算
    // ================================================================
    /// <summary>その月の日ごとの記録の数（日 → 件数）</summary>
    private static Dictionary<int, int> CountsOf(DateTime month, List<PlayLogEntry> all)
    {
        var counts = new Dictionary<int, int>();
        foreach (var log in all)
        {
            var d = log.Date;
            if (d.Year != month.Year || d.Month != month.Month) continue;
            counts[d.Day] = counts.TryGetValue(d.Day, out var n) ? n + 1 : 1;
        }
        return counts;
    }

    /// <summary>その月で記録のあるいちばん最近の日（無ければ null）</summary>
    private static DateTime? LatestDayIn(DateTime month, List<PlayLogEntry> all)
    {
        DateTime? best = null;
        foreach (var log in all)
        {
            var d = log.Date;
            if (d.Year != month.Year || d.Month != month.Month) continue;
            if (best == null || d > best.Value) best = d;
        }
        return best;
    }

    /// <summary>記録のゲーム。図鑑から無くなったゲームはキーを名前の代わりにする（写真のフォルダも同じキーで引ける）</summary>
    private BoardGameEntry GameOf(string key)
    {
        key = key ?? "";
        if (_games.TryGetValue(key, out var g)) return g;
        g = new BoardGameEntry { title = key };
        _games[key] = g;
        return g;
    }

    /// <summary>写真の無い記録のサムネイルに出す、ゲーム名の最初の文字（記号は飛ばす）</summary>
    private static string Initial(string title)
    {
        if (string.IsNullOrEmpty(title)) return "?";
        foreach (var c in title)
            if (char.IsLetterOrDigit(c)) return c.ToString();
        return title.Substring(0, 1);
    }

    /// <summary>メモの1行目（無ければ null）</summary>
    private static string FirstLine(string s)
    {
        if (string.IsNullOrEmpty(s)) return null;
        s = s.Trim();
        int nl = s.IndexOfAny(new[] { '\n', '\r' });
        return nl >= 0 ? s.Substring(0, nl) : s;
    }

    private static void CloseButton(RectTransform safe, UnityEngine.Events.UnityAction onClick)
    {
        var rt = SpaceTheme.Rect("Close", safe, new Vector2(1f, 1f), new Vector2(-(SIDE + 56f), -84f), new Vector2(104f, 104f));
        var face = SpaceTheme.Img(rt, SpaceSprites.Circle, new Color(0.05f, 0.07f, 0.19f, 0.85f), true);
        var rim = SpaceTheme.Rect("Rim", rt);
        SpaceTheme.Stretch(rim);
        SpaceTheme.Img(rim, SpaceSprites.RingSmall, SpaceTheme.STROKE);
        SpaceTheme.Bar(rt, Vector2.zero, 44f, 6f, 45f, SpaceTheme.TEXT);
        SpaceTheme.Bar(rt, Vector2.zero, 44f, 6f, -45f, SpaceTheme.TEXT);
        SpaceTheme.Clickable(rt.gameObject, face, onClick);
    }

    /// <summary>ゲームなどの文字をそのまま出す（フォントに無い文字を除き、&lt; などを書式にしない）</summary>
    private static string Plain(string s) => SpaceTheme.NoParse(SpaceTheme.Safe(s ?? ""));
}
