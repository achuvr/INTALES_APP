using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 履歴（新ホームの中央の惑星の右下にある金色の小さな惑星から開く。所持品と同じ、下から出るシート）。
/// 端末ローカルの行動履歴 LocalHistoryLog（最新50件）を新しい順に、日付ごとに区切って並べる。
/// 記録されるもの: クーポンの入手・使用、来店ボーナス・滞在時間、惑星の解放、フレンド登録、ガチャなど
/// （旧ホームの履歴 HistoryController と同じデータ。Firestore は使わない）。
/// </summary>
public class SpaceHistoryPanel : MonoBehaviour
{
    /// <summary>履歴の色（ホームの金色の小さな惑星の光・見出し）</summary>
    public static readonly Color ACCENT = SpaceTheme.Hex(0xFFC870);

    private const float HEADER_H = 196f;
    private const float FOOTER_H = 128f;
    private const float TOP_MARGIN = 96f;
    private const float BOTTOM_MARGIN = 32f;
    private const float SIDE = 32f;          // シートの内側の左右の余白
    private const float DAY_H = 64f;         // 日付の見出し
    private const float DAY_GAP = 18f;       // 前の日の最後の行と次の日付の間（行の間隔に足す）
    private const float ROW_GAP = 16f;
    private const float ROW_TOP = 70f;       // 行の上段（種類・時刻）
    private const float ROW_BOTTOM = 30f;    // 本文の下の余白
    private const float ROW_PAD = 36f;       // 行の左右の余白
    private const float LINE_Y = -40f;       // 上段の中心（行の上端から）
    private const float TEXT_SIZE = 32f;
    private const float EMPTY_H = 380f;
    private const string DATE_FORMAT = "yyyy/MM/dd HH:mm"; // LocalHistoryLog の保存形式

    private const string T_CAPTION = "HISTORY";
    private const string T_TITLE = "履歴";
    private const string T_EMPTY = "まだ履歴がありません";
    private const string T_EMPTY_SUB = "クーポンの入手や使用、来店などがここに記録されます";
    private const string T_FOOTER_A = "最近の";
    private const string T_FOOTER_B = "件まで、この端末に記録されます";
    private const string T_TODAY = "今日";
    private const string T_YESTERDAY = "昨日";
    private const string T_NO_DATE = "日付なし";
    private static readonly string[] WEEKDAYS = { "日", "月", "火", "水", "木", "金", "土" };

    /// <summary>履歴の種類（LocalHistoryLog.HistoryEntry.type）ごとの表示名と色</summary>
    private struct Kind
    {
        public string Key, Label;
        public Color Color;
    }

    private static readonly Kind[] KINDS =
    {
        new Kind { Key = "coupon", Label = "クーポン", Color = SpaceTheme.GOLD },
        new Kind { Key = "visit",  Label = "来店",     Color = SpaceTheme.MINT },
        new Kind { Key = "planet", Label = "惑星",     Color = SpaceTheme.CYAN },
        new Kind { Key = "meeple", Label = "コマ",     Color = SpaceTheme.Hex(0xFF9C6B) }, // 入店でもらったコマ（2026-11-10）
        new Kind { Key = "fragment", Label = "カケラ", Color = SpaceTheme.Hex(0xFFD36E) }, // INTALES WORLD の星のカケラ（2026-11-11）
        new Kind { Key = "pass",   Label = "パス",     Color = SpaceTheme.Hex(0xF5D58A) }, // 1か月遊び放題パス
        new Kind { Key = "friend", Label = "紹介", Color = SpaceTheme.PINK }, // フレンド機能をやめたので友だち紹介の記録に使う
        new Kind { Key = "gacha",  Label = "ガチャ",   Color = SpaceTheme.VIOLET },
        new Kind { Key = "group",  Label = "グループ", Color = SpaceTheme.Hex(0x7FA8FF) },
        new Kind { Key = "dice",   Label = "バトル",   Color = SpaceTheme.Hex(0xFF9E7A) }, // 旧ホームのボス戦
        new Kind { Key = "certify", Label = "アチーブメント", Color = SpaceTheme.Hex(0xFFE7A8) }, // ボードゲームの公認・星座の完成
    };
    private static readonly Kind OTHER = new Kind { Key = "", Label = "その他", Color = SpaceTheme.TEXT_SUB };

    /// <summary>この画面で表示しうる固定の文字（履歴の本文は開くときに登録する）</summary>
    public static string GlyphText
    {
        get
        {
            var sb = new StringBuilder();
            sb.Append(T_CAPTION).Append(T_TITLE).Append(T_EMPTY).Append(T_EMPTY_SUB).Append(T_FOOTER_A)
              .Append(T_FOOTER_B).Append(T_TODAY).Append(T_YESTERDAY).Append(T_NO_DATE).Append(OTHER.Label)
              .Append("0123456789:年月日（）");
            foreach (var w in WEEKDAYS) sb.Append(w);
            foreach (var k in KINDS) sb.Append(k.Label);
            return sb.ToString();
        }
    }

    private Canvas _canvas;
    private Action _onClosed;
    private CanvasGroup _group;
    private RectTransform _sheet;
    private Vector2 _sheetBase;
    private Coroutine _openAnim;
    private bool _closing;

    public static SpaceHistoryPanel Open(Canvas canvas, Action onClosed)
    {
        var root = SpaceTheme.Rect("__SpaceHistory", canvas.transform);
        SpaceTheme.Stretch(root);
        root.SetAsLastSibling();
        SpaceTheme.SubCanvas(root.gameObject, true); // 一覧をスクロールしてもホーム全体を描き直さないように
        var panel = root.gameObject.AddComponent<SpaceHistoryPanel>();
        panel._canvas = canvas;
        panel._onClosed = onClosed;
        panel.StartCoroutine(panel.Build());
        return panel;
    }

    /// <summary>戻る操作（Android の戻るボタン）</summary>
    public void HandleBack() => Close();

    public void Close()
    {
        if (_closing) return;
        _closing = true;
        if (_openAnim != null) StopCoroutine(_openAnim);
        StartCoroutine(CloseRoutine());
    }

    private IEnumerator CloseRoutine()
    {
        float from = _group.alpha;
        yield return SpaceTheme.Tween(0.18f, 0f, k =>
        {
            _group.alpha = from * (1f - k);
            if (_sheet != null) _sheet.anchoredPosition = _sheetBase + new Vector2(0f, -60f * k);
        });
        _onClosed?.Invoke();
        Destroy(gameObject);
    }

    // ================================================================
    // 組み立て
    // ================================================================
    private IEnumerator Build()
    {
        var rt = (RectTransform)transform;
        _group = gameObject.AddComponent<CanvasGroup>();
        _group.alpha = 0f;

        // 暗幕（タップで閉じる）
        var dim = SpaceTheme.Rect("Dim", rt);
        SpaceTheme.Stretch(dim);
        var dimImg = SpaceTheme.Img(dim, null, SpaceTheme.DIM, true);
        SpaceTheme.Clickable(dim.gameObject, dimImg, Close, false);

        // 本文には事前に分からない文字（フレンドの名前・ガチャの景品名など）が入るので、
        // 40文字/フレームに分けてフォントへ登録してから組む（TMP動的フォントのOOM対策）
        var entries = LocalHistoryLog.GetAllNewestFirst();
        var texts = new string[entries.Count];
        var all = new StringBuilder();
        for (int i = 0; i < entries.Count; i++)
        {
            texts[i] = Clean(entries[i].text);
            all.Append(texts[i]);
        }
        yield return SpaceTheme.Prewarm(all.ToString());
        if (_closing) yield break;

        BuildSheet(entries, texts);

        // 下から浮き上がって開く
        _openAnim = StartCoroutine(SpaceTheme.Tween(0.32f, 0f, k =>
        {
            _group.alpha = k;
            _sheet.anchoredPosition = _sheetBase + new Vector2(0f, -80f * (1f - k));
        }));
    }

    private void BuildSheet(List<LocalHistoryLog.HistoryEntry> entries, string[] texts)
    {
        var safe = SpaceTheme.Rect("Safe", transform);
        SpaceTheme.Stretch(safe);
        safe.gameObject.AddComponent<SpaceSafeArea>();

        var size = SpaceTheme.CanvasSize(_canvas);
        var insets = SpaceTheme.SafeInsets(size);
        float safeH = size.y - insets.top - insets.bottom;
        float listW = size.x - 64f - SIDE * 2f;

        // シート: 下寄せ・中身に合わせた高さ（入りきらないときは一覧をスクロール）
        _sheet = SpaceTheme.Rect("Sheet", safe);
        _sheet.anchorMin = new Vector2(0f, 0f);
        _sheet.anchorMax = new Vector2(1f, 0f);
        _sheet.pivot = new Vector2(0.5f, 0f);
        _sheet.anchoredPosition = new Vector2(0f, BOTTOM_MARGIN);
        _sheetBase = _sheet.anchoredPosition;
        // シート上のタップが暗幕（＝閉じる）へ抜けないよう面で受け止める
        SpaceTheme.Glass(_sheet, 56f, SpaceTheme.GLASS_DEEP, null, true);

        var caption = SpaceTheme.Text(_sheet, "Caption", T_CAPTION, 24, SpaceTheme.A(ACCENT, 0.9f),
            new Vector2(0f, 1f), new Vector2(56f, -70f), new Vector2(400f, 36f), FontStyles.Bold,
            TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
        SpaceTheme.Tracking(caption, 18f);
        SpaceTheme.Text(_sheet, "Title", T_TITLE, 56, SpaceTheme.TEXT,
            new Vector2(0f, 1f), new Vector2(56f, -128f), new Vector2(560f, 72f), FontStyles.Bold,
            TextAlignmentOptions.Left, new Vector2(0f, 0.5f));

        // 閉じる（右上の丸ボタン。所持品と同じ）
        var close = SpaceTheme.Rect("Close", _sheet, new Vector2(1f, 1f), new Vector2(-80f, -96f), new Vector2(96f, 96f));
        var closeFace = SpaceTheme.Img(close, SpaceSprites.Circle, SpaceTheme.A(new Color(0.09f, 0.12f, 0.30f), 0.65f), true);
        var closeRim = SpaceTheme.Rect("Rim", close);
        SpaceTheme.Stretch(closeRim);
        SpaceTheme.Img(closeRim, SpaceSprites.RingSmall, SpaceTheme.STROKE);
        SpaceTheme.Bar(close, Vector2.zero, 44f, 6f, 45f, SpaceTheme.TEXT_SUB);
        SpaceTheme.Bar(close, Vector2.zero, 44f, 6f, -45f, SpaceTheme.TEXT_SUB);
        SpaceTheme.Clickable(close.gameObject, closeFace, Close);

        // 一覧（スクロール）
        var list = SpaceTheme.Rect("List", _sheet);
        SpaceTheme.Stretch(list, SIDE, FOOTER_H, SIDE, HEADER_H);
        var viewport = SpaceTheme.Rect("Viewport", list);
        SpaceTheme.Stretch(viewport);
        SpaceTheme.HitArea(viewport); // 行の間の余白でもドラッグできるように
        viewport.gameObject.AddComponent<RectMask2D>().softness = new Vector2Int(0, 24);
        var content = SpaceTheme.Rect("Content", viewport);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.anchoredPosition = Vector2.zero;

        float listH = entries.Count == 0 ? BuildEmpty(content) : BuildRows(content, entries, texts, listW);
        content.sizeDelta = new Vector2(0f, listH);

        float sheetH = Mathf.Min(HEADER_H + listH + FOOTER_H, safeH - TOP_MARGIN - BOTTOM_MARGIN);
        _sheet.sizeDelta = new Vector2(-64f, sheetH);

        var scroll = list.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.vertical = listH > sheetH - HEADER_H - FOOTER_H + 1f; // 収まるときはスクロールさせない
        scroll.movementType = ScrollRect.MovementType.Elastic;
        scroll.scrollSensitivity = 30f;

        SpaceTheme.Text(_sheet, "Footer", T_FOOTER_A + LocalHistoryLog.MAX_ENTRIES + T_FOOTER_B, 26,
            SpaceTheme.TEXT_MUTED, new Vector2(0.5f, 0f), new Vector2(0f, 64f), new Vector2(900f, 40f), FontStyles.Bold);
    }

    /// <summary>新しい順の履歴を、日付が変わるところに見出しを入れて並べる。中身の高さを返す</summary>
    private float BuildRows(RectTransform content, List<LocalHistoryLog.HistoryEntry> entries, string[] texts,
        float listW)
    {
        float y = 0f;
        string shownDay = null;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool dated = TryParseDate(e.date, out var at);
            string day = dated ? DayLabel(at) : T_NO_DATE;
            if (day != shownDay)
            {
                if (shownDay != null) y -= DAY_GAP;
                shownDay = day;
                SpaceTheme.Text(content, "Day", day, 28, SpaceTheme.TEXT_SUB, new Vector2(0f, 1f),
                    new Vector2(12f, y - DAY_H * 0.5f), new Vector2(600f, 40f), FontStyles.Bold,
                    TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
                y -= DAY_H;
            }
            string time = dated ? at.ToString("H:mm", CultureInfo.InvariantCulture) : SpaceTheme.Safe(e.date ?? "");
            y -= BuildRow(content, KindOf(e.type), texts[i], time, y, listW) + ROW_GAP;
        }
        return -y - ROW_GAP + 8f;
    }

    /// <summary>履歴1件（上段に種類と時刻、下に本文。長い本文は折り返す）。行の高さを返す</summary>
    private float BuildRow(RectTransform content, Kind kind, string text, string time, float y, float listW)
    {
        var row = SpaceTheme.Rect("Row", content);
        row.anchorMin = new Vector2(0f, 1f);
        row.anchorMax = new Vector2(1f, 1f);
        row.pivot = new Vector2(0.5f, 1f);
        row.anchoredPosition = new Vector2(0f, y);
        SpaceTheme.Glass(row, 32f, SpaceTheme.GLASS);

        // 種類: 光る点＋名前（色分け）
        var glow = SpaceTheme.Rect("DotGlow", row, new Vector2(0f, 1f), new Vector2(ROW_PAD + 8f, LINE_Y),
            new Vector2(44f, 44f));
        SpaceTheme.Img(glow, SpaceSprites.Glow, SpaceTheme.A(kind.Color, 0.7f));
        var dot = SpaceTheme.Rect("Dot", row, new Vector2(0f, 1f), new Vector2(ROW_PAD + 8f, LINE_Y),
            new Vector2(14f, 14f));
        SpaceTheme.Img(dot, SpaceSprites.Circle, kind.Color);
        SpaceTheme.Text(row, "Kind", kind.Label, 26, kind.Color, new Vector2(0f, 1f),
            new Vector2(ROW_PAD + 32f, LINE_Y), new Vector2(400f, 36f), FontStyles.Bold,
            TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
        SpaceTheme.Text(row, "Time", time, 26, SpaceTheme.TEXT_MUTED, new Vector2(1f, 1f),
            new Vector2(-ROW_PAD, LINE_Y), new Vector2(200f, 36f), FontStyles.Bold,
            TextAlignmentOptions.Right, new Vector2(1f, 0.5f));

        // 本文（名前などに < が入っても書式として解釈しない）
        float textW = listW - ROW_PAD * 2f;
        var body = SpaceTheme.Text(row, "Text", SpaceTheme.NoParse(SpaceTheme.Safe(text)), TEXT_SIZE, SpaceTheme.TEXT,
            new Vector2(0f, 1f), new Vector2(ROW_PAD, -ROW_TOP), new Vector2(textW, TEXT_SIZE * 1.4f),
            FontStyles.Bold, TextAlignmentOptions.TopLeft, new Vector2(0f, 1f));
        body.textWrappingMode = TextWrappingModes.Normal;
        float textH = Mathf.Max(TEXT_SIZE * 1.25f, body.GetPreferredValues(body.text, textW, 0f).y);
        body.rectTransform.sizeDelta = new Vector2(textW, textH);

        float h = ROW_TOP + textH + ROW_BOTTOM;
        row.sizeDelta = new Vector2(0f, h);
        return h;
    }

    /// <summary>履歴が1件もないとき（金色の小さな惑星と案内）</summary>
    private float BuildEmpty(RectTransform content)
    {
        var moon = SpaceTheme.Rect("Moon", content, new Vector2(0.5f, 1f), new Vector2(0f, -112f), new Vector2(128f, 128f));
        SpaceTheme.Img(moon, SpaceSprites.GoldMoon, SpaceTheme.A(Color.white, 0.8f));
        SpaceTheme.Text(content, "Empty", T_EMPTY, 36, SpaceTheme.TEXT_SUB, new Vector2(0.5f, 1f),
            new Vector2(0f, -232f), new Vector2(880f, 52f), FontStyles.Bold);
        var sub = SpaceTheme.Text(content, "EmptySub", T_EMPTY_SUB, 26, SpaceTheme.TEXT_MUTED, new Vector2(0.5f, 1f),
            new Vector2(0f, -290f), new Vector2(880f, 40f), FontStyles.Bold);
        sub.enableAutoSizing = true;
        sub.fontSizeMin = 20f;
        sub.fontSizeMax = 26f;
        return EMPTY_H;
    }

    // ================================================================
    // 表示用の変換
    // ================================================================
    private static Kind KindOf(string type)
    {
        foreach (var k in KINDS)
            if (k.Key == type) return k;
        return OTHER;
    }

    /// <summary>
    /// 保存された日時を読む。LocalHistoryLog は端末の地域設定のまま書く（日付の区切りが "/" 以外の地域もある）ので、
    /// 端末の設定 → 区切りが "/" の順に試す
    /// </summary>
    private static bool TryParseDate(string s, out DateTime at) =>
        DateTime.TryParseExact(s, DATE_FORMAT, CultureInfo.CurrentCulture, DateTimeStyles.None, out at)
        || DateTime.TryParseExact(s, DATE_FORMAT, CultureInfo.InvariantCulture, DateTimeStyles.None, out at);

    /// <summary>日付の見出し: 今日 / 昨日 / 11月3日（月） / 年が違えば 2025年12月31日（水）</summary>
    private static string DayLabel(DateTime at)
    {
        var today = DateTime.Today;
        if (at.Date == today) return T_TODAY;
        if (at.Date == today.AddDays(-1)) return T_YESTERDAY;
        string md = $"{at.Month}月{at.Day}日（{WEEKDAYS[(int)at.DayOfWeek]}）";
        return at.Year == today.Year ? md : $"{at.Year}年{md}";
    }

    /// <summary>このフォントで表示できない絵文字の部品と、改行以外の制御文字を取り除く</summary>
    private static string Clean(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            if (char.IsSurrogate(c) || c == '️' || c == '‍') continue;
            if (char.IsControl(c) && c != '\n') continue;
            sb.Append(c);
        }
        return sb.ToString();
    }
}
