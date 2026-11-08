using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Cysharp.Threading.Tasks;
using Firebase.Firestore;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// 相席募集をつくる（新ホームのお知らせページの一番下の「相席募集をする」から開く、下から出るシート）。
/// 旧ホームの募集ボード（RecruitBoardController）の作成フォームの宇宙デザイン版で、選べる項目と書き込む中身は同じ
/// （RecruitBoardController.NewPostData。誹謗中傷対策で自由入力はなく、ゲームはカタログから選ぶ。形はルールで強制）:
///   あそぶゲーム … お店できめる／持ち込みゲーム／カタログのタイトル（検索して選ぶ）
///   日にち … 今日〜90日先（カレンダー）　　集まる時間 … 12:00〜22:30 の30分刻み（今日は過ぎた時間を選べない）
///   募集する人数 … あと1〜9人　　ひとこと … 定型から4つまで
/// 1人1件までの確認は開く側（SpaceNewsPage）が行う。
/// </summary>
public class SpaceRecruitForm : MonoBehaviour
{
    private const string T_CAPTION = "RECRUIT";
    private const string T_TITLE = "相席募集をつくる";
    private const string T_GAME = "あそぶゲーム";
    private const string T_DATE = "日にち";
    private const string T_TIME = "集まる時間";
    private const string T_SEATS = "募集する人数";
    private const string T_TAGS_A = "ひとこと（";
    private const string T_TAGS_B = "つまで）";
    private const string T_TAG_LIMIT_A = "ひとことは";
    private const string T_TAG_LIMIT_B = "つまでです";
    private const string T_CANCEL = "やめる";
    private const string T_SUBMIT = "募集する";
    private const string T_SHOP_PICKS = "お店できめる";
    private const string T_BRING_OWN = "持ち込みゲーム";
    private const string T_PICK_GAME = "あそぶゲームをえらぶ";
    private const string T_SEARCH = "タイトルで検索…";
    private const string T_PICK_DATE = "日にちをえらぶ";
    private const string T_PICK_TIME = "集まる時間をえらぶ";
    private const string T_LEFT_A = "あと";
    private const string T_LEFT_B = "人";
    private const string T_YEAR = "年";
    private const string T_MONTH = "月";
    private const string T_POSTING = "募集を出しています…";
    private const string T_POSTED = "募集を出しました！";
    private const string T_FAILED = "通信に失敗しました\nもう一度お試しください";
    private const string T_PAST = "その時間はもう過ぎています";
    private const string T_NO_NAME = "ユーザー名が登録されていないため、募集できません";
    private const string WEEKDAYS = "日月火水木金土";

    /// <summary>この画面で表示しうる固定の文字（ゲーム名はゲームを選ぶ画面で登録する）</summary>
    public static string GlyphText
    {
        get
        {
            var sb = new StringBuilder();
            sb.Append(T_CAPTION).Append(T_TITLE).Append(T_GAME).Append(T_DATE).Append(T_TIME).Append(T_SEATS)
              .Append(T_TAGS_A).Append(T_TAGS_B).Append(T_TAG_LIMIT_A).Append(T_TAG_LIMIT_B).Append(T_CANCEL)
              .Append(T_SUBMIT).Append(T_SHOP_PICKS).Append(T_BRING_OWN).Append(T_PICK_GAME).Append(T_SEARCH)
              .Append(T_PICK_DATE).Append(T_PICK_TIME).Append(T_LEFT_A).Append(T_LEFT_B).Append(T_YEAR)
              .Append(T_MONTH).Append(T_POSTING).Append(T_POSTED).Append(T_FAILED).Append(T_PAST).Append(T_NO_NAME)
              .Append(WEEKDAYS).Append("0123456789:/()～今日明日");
            foreach (var t in RecruitBoardController.TAGS) sb.Append(t);
            return sb.ToString();
        }
    }

    private const float SIDE = 56f;            // シートの内側の左右の余白
    private const float TOP_MARGIN = 96f, BOTTOM_MARGIN = 32f;
    private const float FIELD_H = 104f;
    private const float CHIP_H = 84f, GAP = 16f;
    private const float FORM_H = 1432f;        // 作成シートの高さ
    private const float CAL_TOP = 284f, CAL_ROW = 104f;
    private const float SLOT_TOP = 150f, SLOT_CHIP_H = 92f;

    private static readonly Color FIELD_FILL = new Color(0.09f, 0.12f, 0.30f, 0.62f);
    private static readonly Color CHIP_OFF = new Color(0.09f, 0.12f, 0.30f, 0.55f);

    /// <summary>選べるマス（ひとこと・時間・お店できめる など）の部品</summary>
    private sealed class Chip
    {
        public RectTransform Rt;
        public Image Fill, Edge;
        public TextMeshProUGUI Label;
    }

    private Canvas _canvas;
    private Action _onPosted, _onClosed;
    private CanvasGroup _group;
    private RectTransform _sheet, _sub;
    private float _innerW;
    private TextMeshProUGUI _gameLabel, _dateLabel, _timeLabel, _seatsLabel;
    private CanvasGroup _minusGroup, _plusGroup;
    private readonly List<(int id, Chip chip)> _tagChips = new List<(int, Chip)>();
    private bool _busy, _closing;

    // 選んでいる内容（書き込む値そのもの）
    private string _game = "";   // "" = お店できめる / GAME_BRING_OWN / カタログのタイトル
    private string _date;        // "yyyy-MM-dd"
    private int _slot;
    private int _seats = 1;
    private readonly HashSet<int> _tags = new HashSet<int>();

    // カレンダー
    private DateTime _calMonth;
    private RectTransform _calGrid;
    private TextMeshProUGUI _calMonthLabel;
    private GameObject _calPrev, _calNext;

    /// <summary>parent（お知らせページ）の上にシートを開く</summary>
    public static SpaceRecruitForm Open(RectTransform parent, Canvas canvas, Action onPosted, Action onClosed)
    {
        var root = SpaceTheme.Rect("RecruitForm", parent);
        SpaceTheme.Stretch(root);
        root.SetAsLastSibling();
        var form = root.gameObject.AddComponent<SpaceRecruitForm>();
        form._canvas = canvas;
        form._onPosted = onPosted;
        form._onClosed = onClosed;
        form.Build();
        return form;
    }

    /// <summary>戻る操作: 選ぶ画面を開いていればそれを、なければフォームを閉じる</summary>
    public void HandleBack()
    {
        if (_sub != null) CloseSub();
        else Close();
    }

    public void Close()
    {
        if (_closing || _busy) return;
        _closing = true;
        var basePos = _sheet.anchoredPosition;
        StartCoroutine(CloseRoutine(basePos));
    }

    private System.Collections.IEnumerator CloseRoutine(Vector2 basePos)
    {
        yield return SpaceTheme.Tween(0.18f, 0f, k =>
        {
            _group.alpha = 1f - k;
            _sheet.anchoredPosition = basePos + new Vector2(0f, -60f * k);
        });
        Destroy(gameObject);
    }

    private void OnDestroy() => _onClosed?.Invoke();

    // ================================================================
    // 作成シート
    // ================================================================
    private void Build()
    {
        InitDefaults();
        var rt = (RectTransform)transform;
        _group = gameObject.AddComponent<CanvasGroup>();

        var dim = SpaceTheme.Rect("Dim", rt);
        SpaceTheme.Stretch(dim);
        var dimImg = SpaceTheme.Img(dim, null, SpaceTheme.DIM, true);
        SpaceTheme.Clickable(dim.gameObject, dimImg, Close, false);

        var safe = SpaceTheme.Rect("Safe", rt);
        SpaceTheme.Stretch(safe);
        safe.gameObject.AddComponent<SpaceSafeArea>();
        _sheet = NewSheet(safe, FORM_H);
        _innerW = SpaceTheme.CanvasSize(_canvas).x - 64f - SIDE * 2f;

        var caption = SpaceTheme.Text(_sheet, "Caption", T_CAPTION, 24, SpaceTheme.A(SpaceTheme.MINT, 0.85f),
            new Vector2(0f, 1f), new Vector2(SIDE, -70f), new Vector2(400f, 36f), FontStyles.Bold,
            TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
        SpaceTheme.Tracking(caption, 18f);
        SpaceTheme.Text(_sheet, "Title", T_TITLE, 52, SpaceTheme.TEXT, new Vector2(0f, 1f), new Vector2(SIDE, -128f),
            new Vector2(760f, 70f), FontStyles.Bold, TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
        CloseButton(_sheet, -96f, Close);

        // あそぶゲーム
        Label(_sheet, T_GAME, SIDE, 222f);
        _gameLabel = Field(_sheet, new Vector2(SIDE, -250f), new Vector2(_innerW, FIELD_H), OpenGamePicker);

        // 日にち・集まる時間（横並び）
        float half = (_innerW - 24f) * 0.5f;
        Label(_sheet, T_DATE, SIDE, 416f);
        Label(_sheet, T_TIME, SIDE + half + 24f, 416f);
        _dateLabel = Field(_sheet, new Vector2(SIDE, -444f), new Vector2(half, FIELD_H), OpenCalendar);
        _timeLabel = Field(_sheet, new Vector2(SIDE + half + 24f, -444f), new Vector2(half, FIELD_H), OpenSlots);

        // 募集する人数（－ あと n人 ＋）
        Label(_sheet, T_SEATS, SIDE, 610f);
        float cx = SIDE + _innerW * 0.5f;
        _minusGroup = StepButton(_sheet, new Vector2(cx - 230f, -690f), false, () => StepSeats(-1));
        _plusGroup = StepButton(_sheet, new Vector2(cx + 230f, -690f), true, () => StepSeats(1));
        _seatsLabel = SpaceTheme.Text(_sheet, "Seats", "", 64, SpaceTheme.TEXT, new Vector2(0f, 1f),
            new Vector2(cx, -690f), new Vector2(320f, 90f), FontStyles.Bold);

        // ひとこと（3列×4行）
        Label(_sheet, T_TAGS_A + RecruitBoardController.MAX_TAGS + T_TAGS_B, SIDE, 804f);
        float chipW = (_innerW - GAP * 2f) / 3f;
        var tags = RecruitBoardController.TAGS;
        for (int i = 0; i < tags.Length; i++)
        {
            int id = i;
            var pos = new Vector2(SIDE + (i % 3) * (chipW + GAP), -(832f + (i / 3) * (CHIP_H + GAP)));
            _tagChips.Add((id, NewChip(_sheet, pos, new Vector2(chipW, CHIP_H), tags[i], 28f, () => ToggleTag(id))));
        }

        // やめる・募集する
        float by = -(1256f + 64f);
        var cancel = SpaceTheme.Rect("Cancel", _sheet, new Vector2(0f, 1f), new Vector2(SIDE + half * 0.5f, by),
            new Vector2(half, 128f));
        var cancelFace = SpaceTheme.Glass(cancel, 46f, SpaceTheme.A(new Color(0.09f, 0.12f, 0.30f), 0.63f), null, true);
        SpaceTheme.Clickable(cancel.gameObject, cancelFace, Close);
        var cancelText = SpaceTheme.Text(cancel, "Label", T_CANCEL, 38, SpaceTheme.TEXT_SUB, FontStyles.Bold);
        SpaceTheme.Stretch(cancelText.rectTransform);
        var submit = SpaceTheme.Rect("Submit", _sheet, new Vector2(0f, 1f),
            new Vector2(SIDE + half + 24f + half * 0.5f, by), new Vector2(half, 128f));
        var submitFace = SpaceTheme.PrimaryFace(submit);
        SpaceTheme.Clickable(submit.gameObject, submitFace, Submit);
        var submitText = SpaceTheme.Text(submit, "Label", T_SUBMIT, 40, SpaceTheme.TEXT_DARK, FontStyles.Bold);
        SpaceTheme.Stretch(submitText.rectTransform);

        RefreshForm();

        // 下から浮き上がって開く
        var basePos = _sheet.anchoredPosition;
        _group.alpha = 0f;
        StartCoroutine(SpaceTheme.Tween(0.32f, 0f, k =>
        {
            _group.alpha = k;
            _sheet.anchoredPosition = basePos + new Vector2(0f, -80f * (1f - k));
        }));
    }

    /// <summary>既定値: 今日のこれからいちばん近い時間（閉店後なら明日の開店時間）・お店できめる・あと1人（旧ホームと同じ）</summary>
    private void InitDefaults()
    {
        var now = DateTime.Now;
        int slot = NextSlotToday();
        _date = RecruitBoardController.DateStr(now);
        if (slot > RecruitBoardController.SLOT_MAX)
        {
            _date = RecruitBoardController.DateStr(now.AddDays(1));
            slot = RecruitBoardController.SLOT_MIN;
        }
        _slot = slot;
    }

    /// <summary>今日まだ選べるいちばん早い時間（いまの時刻を30分刻みに切り上げ。開店前は開店時間）</summary>
    private static int NextSlotToday()
    {
        var now = DateTime.Now;
        int step = RecruitBoardController.SLOT_STEP;
        int slot = Mathf.CeilToInt((now.Hour * 60 + now.Minute) / (float)step) * step;
        return Mathf.Max(slot, RecruitBoardController.SLOT_MIN);
    }

    private static bool IsToday(string date) => date == RecruitBoardController.DateStr(DateTime.Now);

    private void RefreshForm()
    {
        bool title = !string.IsNullOrEmpty(_game) && _game != RecruitBoardController.GAME_BRING_OWN;
        _gameLabel.text = string.IsNullOrEmpty(_game) ? T_SHOP_PICKS
            : _game == RecruitBoardController.GAME_BRING_OWN ? T_BRING_OWN
            : SpaceTheme.NoParse(SpaceTheme.SafeNow(_game));
        _gameLabel.color = title ? SpaceTheme.TEXT : SpaceTheme.TEXT_SUB;
        _dateLabel.text = RecruitBoardController.FormDateDisplay(_date);
        _timeLabel.text = RecruitBoardController.SlotText(_slot) + "～";
        _seatsLabel.text = $"<size=50%>{T_LEFT_A}</size>{_seats}<size=50%>{T_LEFT_B}</size>";
        _minusGroup.alpha = _seats > 1 ? 1f : 0.35f;
        _plusGroup.alpha = _seats < RecruitBoardController.MAX_SEATS ? 1f : 0.35f;
        foreach (var (id, chip) in _tagChips) SetChip(chip, _tags.Contains(id));
    }

    private void StepSeats(int d)
    {
        _seats = Mathf.Clamp(_seats + d, 1, RecruitBoardController.MAX_SEATS);
        RefreshForm();
    }

    private void ToggleTag(int id)
    {
        if (_tags.Contains(id)) _tags.Remove(id);
        else if (_tags.Count >= RecruitBoardController.MAX_TAGS)
        {
            SpaceTheme.Toast(T_TAG_LIMIT_A + RecruitBoardController.MAX_TAGS + T_TAG_LIMIT_B, _canvas);
            return;
        }
        else _tags.Add(id);
        RefreshForm();
    }

    // ================================================================
    // 書き込み
    // ================================================================
    private void Submit()
    {
        if (_busy || _closing) return;
        if (IsToday(_date) && _slot < NextSlotToday())
        {
            SpaceTheme.Toast(T_PAST, _canvas);
            return;
        }
        SubmitAsync().Forget();
    }

    private async UniTaskVoid SubmitAsync()
    {
        var manager = UserDataManager.instance;
        string uid = manager != null ? manager.UID : null;
        string name = manager != null && manager.UserData != null ? manager.UserData.Username : null;
        if (string.IsNullOrEmpty(uid) || string.IsNullOrEmpty(name))
        {
            SpaceTheme.Toast(T_NO_NAME, _canvas);
            return;
        }

        _busy = true;
        var busy = SpaceDialog.Busy(_canvas, T_POSTING);
        bool posted = false;
        try
        {
            await FirebaseFirestore.DefaultInstance.Collection("recruits").AddAsync(
                RecruitBoardController.NewPostData(uid, name, _game, _date, _slot, _seats, _tags)).AsUniTask();
            posted = true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Recruit] 新ホームからの募集の作成に失敗: {ex.Message}");
        }
        finally
        {
            _busy = false;
            if (busy != null) Destroy(busy);
        }

        SpaceTheme.Toast(posted ? T_POSTED : T_FAILED, _canvas);
        if (!posted || this == null) return;
        _onPosted?.Invoke();
        Close();
    }

    // ================================================================
    // 選ぶ画面（ゲーム・日にち・時間）
    // ================================================================
    /// <summary>作成シートの上に重ねる、選ぶためのシート（暗幕のタップで閉じる）</summary>
    private RectTransform OpenSub(string title, float height)
    {
        CloseSub();
        _sub = SpaceTheme.Rect("Picker", transform);
        SpaceTheme.Stretch(_sub);
        var group = _sub.gameObject.AddComponent<CanvasGroup>();
        var dim = SpaceTheme.Rect("Dim", _sub);
        SpaceTheme.Stretch(dim);
        var dimImg = SpaceTheme.Img(dim, null, SpaceTheme.DIM, true);
        SpaceTheme.Clickable(dim.gameObject, dimImg, CloseSub, false);
        var safe = SpaceTheme.Rect("Safe", _sub);
        SpaceTheme.Stretch(safe);
        safe.gameObject.AddComponent<SpaceSafeArea>();
        var sheet = NewSheet(safe, height);
        SpaceTheme.Text(sheet, "Title", title, 44, SpaceTheme.TEXT, new Vector2(0f, 1f), new Vector2(SIDE, -88f),
            new Vector2(760f, 60f), FontStyles.Bold, TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
        CloseButton(sheet, -88f, CloseSub);
        group.alpha = 0f;
        StartCoroutine(SpaceTheme.Tween(0.2f, 0f, k =>
        {
            if (group != null) group.alpha = k;
        }));
        return sheet;
    }

    private void CloseSub()
    {
        if (_sub == null) return;
        Destroy(_sub.gameObject);
        _sub = null;
        _calGrid = null;
    }

    // ---------------------------------------------------------------- ゲーム
    private void OpenGamePicker()
    {
        if (_busy) return;
        float h = Mathf.Min(1560f, AvailableHeight());
        var sheet = OpenSub(T_PICK_GAME, h);

        float half = (_innerW - 24f) * 0.5f;
        var shop = NewChip(sheet, new Vector2(SIDE, -150f), new Vector2(half, 92f), T_SHOP_PICKS, 30f,
            () => PickGame(""));
        var own = NewChip(sheet, new Vector2(SIDE + half + 24f, -150f), new Vector2(half, 92f), T_BRING_OWN, 30f,
            () => PickGame(RecruitBoardController.GAME_BRING_OWN));
        SetChip(shop, string.IsNullOrEmpty(_game));
        SetChip(own, _game == RecruitBoardController.GAME_BRING_OWN);

        var input = SearchInput(sheet, new Vector2(SIDE, -266f), new Vector2(_innerW, 96f), T_SEARCH);

        // 候補（仮想スクロール。全タイトルでも行は十数個しか作らない）
        const float listTop = 390f, listBottom = 40f;
        float listH = h - listTop - listBottom;
        var picker = TitlePickerList.Create(sheet, new Vector2(0f, h * 0.5f - listTop - listH * 0.5f), _innerW, listH,
            SpaceTheme.Font, PickGame, CHIP_OFF, SpaceTheme.TEXT);
        var games = BoardGameCatalog.Load()?.games ?? Array.Empty<BoardGameEntry>();
        var all = games.Select(g => g.title).Where(t => !string.IsNullOrEmpty(t)).Distinct()
            .OrderBy(t => t, StringComparer.Ordinal).ToList();
        picker.PrewarmAsync(all);

        void Refresh(string q)
        {
            q = (q ?? "").Trim().ToLowerInvariant();
            picker.SetItems(string.IsNullOrEmpty(q) ? all
                : games.Where(g =>
                        (g.title != null && g.title.ToLowerInvariant().Contains(q))
                        || (g.title_en != null && g.title_en.ToLowerInvariant().Contains(q)))
                    .Select(g => g.title).Where(t => !string.IsNullOrEmpty(t)).Distinct()
                    .OrderBy(t => t, StringComparer.Ordinal).ToList());
        }
        input.onValueChanged.AddListener(Refresh);
        Refresh("");
    }

    private void PickGame(string game)
    {
        _game = game ?? "";
        CloseSub();
        RefreshForm();
    }

    // ---------------------------------------------------------------- 日にち
    private void OpenCalendar()
    {
        if (_busy) return;
        var sheet = OpenSub(T_PICK_DATE, CAL_TOP + CAL_ROW * 6f + 40f);

        _calPrev = NavButton(sheet, new Vector2(SIDE + 48f, -176f), false, () => StepMonth(-1));
        _calNext = NavButton(sheet, new Vector2(SIDE + _innerW - 48f, -176f), true, () => StepMonth(1));
        _calMonthLabel = SpaceTheme.Text(sheet, "Month", "", 38, SpaceTheme.TEXT, new Vector2(0f, 1f),
            new Vector2(SIDE + _innerW * 0.5f, -176f), new Vector2(500f, 56f), FontStyles.Bold);

        float cw = _innerW / 7f;
        for (int i = 0; i < 7; i++)
        {
            var color = i == 0 ? SpaceTheme.PINK : i == 6 ? SpaceTheme.CYAN : SpaceTheme.TEXT_MUTED;
            SpaceTheme.Text(sheet, "Weekday", WEEKDAYS[i].ToString(), 24, color, new Vector2(0f, 1f),
                new Vector2(SIDE + cw * (i + 0.5f), -252f), new Vector2(cw, 36f), FontStyles.Bold);
        }

        _calGrid = SpaceTheme.Rect("Days", sheet, new Vector2(0f, 1f), new Vector2(SIDE, -CAL_TOP),
            new Vector2(_innerW, CAL_ROW * 6f), new Vector2(0f, 1f));
        var sel = DateTime.TryParseExact(_date, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None,
            out var d) ? d : DateTime.Today;
        _calMonth = new DateTime(sel.Year, sel.Month, 1);
        RefreshCalendar();
    }

    private void StepMonth(int dir)
    {
        _calMonth = _calMonth.AddMonths(dir);
        RefreshCalendar();
    }

    /// <summary>選べるのは今日〜90日先（今日はもう選べる時間がなければ不可）。選んでいる日は明るく、今日は縁取り</summary>
    private void RefreshCalendar()
    {
        if (_calGrid == null) return;
        var today = DateTime.Today;
        var last = today.AddDays(RecruitBoardController.MAX_DAYS_AHEAD);
        var firstMonth = new DateTime(today.Year, today.Month, 1);
        var lastMonth = new DateTime(last.Year, last.Month, 1);
        if (_calMonth < firstMonth) _calMonth = firstMonth;
        if (_calMonth > lastMonth) _calMonth = lastMonth;
        _calMonthLabel.text = $"{_calMonth.Year}{T_YEAR}{_calMonth.Month}{T_MONTH}";
        _calPrev.SetActive(_calMonth > firstMonth);
        _calNext.SetActive(_calMonth < lastMonth);

        for (int i = _calGrid.childCount - 1; i >= 0; i--) Destroy(_calGrid.GetChild(i).gameObject);

        bool todayOpen = NextSlotToday() <= RecruitBoardController.SLOT_MAX;
        float cw = _innerW / 7f;
        int days = DateTime.DaysInMonth(_calMonth.Year, _calMonth.Month);
        int firstDow = (int)_calMonth.DayOfWeek;
        for (int n = 1; n <= days; n++)
        {
            var day = new DateTime(_calMonth.Year, _calMonth.Month, n);
            int cell = firstDow + n - 1;
            var rt = SpaceTheme.Rect("Day", _calGrid, new Vector2(0f, 1f),
                new Vector2(cw * (cell % 7 + 0.5f), -(CAL_ROW * (cell / 7) + CAL_ROW * 0.5f)),
                new Vector2(cw - 12f, CAL_ROW - 12f));
            bool selectable = day >= today && day <= last && (day != today || todayOpen);
            bool selected = RecruitBoardController.DateStr(day) == _date;
            if (selectable)
            {
                var fill = SpaceTheme.Rounded(rt, selected ? SpaceTheme.A(SpaceTheme.MINT, 0.9f) : CHIP_OFF, 24f, true);
                SpaceTheme.Clickable(rt.gameObject, fill, () => PickDate(day));
            }
            if (day == today && !selected)
            {
                var ring = SpaceTheme.Rect("Today", rt);
                SpaceTheme.Stretch(ring);
                SpaceTheme.RoundedStroke(ring, SpaceTheme.A(SpaceTheme.MINT, 0.6f), 24f);
            }
            var label = SpaceTheme.Text(rt, "Label", n.ToString(), 32,
                selected ? SpaceTheme.TEXT_DARK : selectable ? SpaceTheme.TEXT : SpaceTheme.A(SpaceTheme.TEXT_MUTED, 0.45f),
                FontStyles.Bold);
            SpaceTheme.Stretch(label.rectTransform);
        }
    }

    private void PickDate(DateTime day)
    {
        _date = RecruitBoardController.DateStr(day);
        // 今日を選んだのに過ぎた時間だったら、いちばん近い時間へ進める（旧ホームと同じ）
        if (IsToday(_date))
            _slot = Mathf.Clamp(Mathf.Max(_slot, NextSlotToday()), RecruitBoardController.SLOT_MIN,
                RecruitBoardController.SLOT_MAX);
        CloseSub();
        RefreshForm();
    }

    // ---------------------------------------------------------------- 時間
    private void OpenSlots()
    {
        if (_busy) return;
        int count = (RecruitBoardController.SLOT_MAX - RecruitBoardController.SLOT_MIN) / RecruitBoardController.SLOT_STEP + 1;
        int rows = Mathf.CeilToInt(count / 4f);
        var sheet = OpenSub(T_PICK_TIME, SLOT_TOP + rows * SLOT_CHIP_H + (rows - 1) * GAP + 48f);

        int earliest = IsToday(_date) ? NextSlotToday() : RecruitBoardController.SLOT_MIN;
        float cw = (_innerW - GAP * 3f) / 4f;
        for (int i = 0; i < count; i++)
        {
            int slot = RecruitBoardController.SLOT_MIN + i * RecruitBoardController.SLOT_STEP;
            var pos = new Vector2(SIDE + (i % 4) * (cw + GAP), -(SLOT_TOP + (i / 4) * (SLOT_CHIP_H + GAP)));
            var chip = NewChip(sheet, pos, new Vector2(cw, SLOT_CHIP_H), RecruitBoardController.SlotText(slot), 32f,
                () => PickSlot(slot));
            SetChip(chip, slot == _slot);
            if (slot < earliest)
            {
                // 今日のもう過ぎた時間は選べない
                chip.Rt.gameObject.AddComponent<CanvasGroup>().alpha = 0.3f;
                chip.Rt.GetComponent<Button>().interactable = false;
            }
        }
    }

    private void PickSlot(int slot)
    {
        _slot = slot;
        CloseSub();
        RefreshForm();
    }

    // ================================================================
    // 部品
    // ================================================================
    /// <summary>下から出るシート（縦に入りきらない端末では全体を縮める）</summary>
    private RectTransform NewSheet(RectTransform safe, float h)
    {
        var sheet = SpaceTheme.Rect("Sheet", safe);
        sheet.anchorMin = new Vector2(0f, 0f);
        sheet.anchorMax = new Vector2(1f, 0f);
        sheet.pivot = new Vector2(0.5f, 0f);
        sheet.sizeDelta = new Vector2(-64f, h);
        sheet.anchoredPosition = new Vector2(0f, BOTTOM_MARGIN);
        // シート上のタップが暗幕（＝閉じる）へ抜けないよう面で受け止める
        SpaceTheme.Glass(sheet, 56f, SpaceTheme.GLASS_DEEP, null, true);
        float avail = AvailableHeight();
        if (h > avail)
        {
            float s = avail / h;
            sheet.localScale = new Vector3(s, s, 1f);
        }
        return sheet;
    }

    private float AvailableHeight()
    {
        var size = SpaceTheme.CanvasSize(_canvas);
        var insets = SpaceTheme.SafeInsets(size);
        return size.y - insets.top - insets.bottom - TOP_MARGIN - BOTTOM_MARGIN;
    }

    private static void Label(RectTransform sheet, string text, float x, float yCenter)
    {
        SpaceTheme.Text(sheet, "Label", text, 26, SpaceTheme.TEXT_MUTED, new Vector2(0f, 1f), new Vector2(x, -yCenter),
            new Vector2(600f, 38f), FontStyles.Bold, TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
    }

    /// <summary>今の選択を見せて、押すと選ぶ画面を開く枠（右端に ＞）</summary>
    private static TextMeshProUGUI Field(RectTransform parent, Vector2 topLeft, Vector2 size, UnityAction onTap)
    {
        var rt = SpaceTheme.Rect("Field", parent, new Vector2(0f, 1f), topLeft, size, new Vector2(0f, 1f));
        var face = SpaceTheme.Glass(rt, 32f, FIELD_FILL, null, true);
        SpaceTheme.Clickable(rt.gameObject, face, onTap);
        var label = SpaceTheme.Text(rt, "Value", "", 34, SpaceTheme.TEXT, FontStyles.Bold, TextAlignmentOptions.Left);
        SpaceTheme.Stretch(label.rectTransform, 32f, 0f, 76f, 0f);
        label.enableAutoSizing = true;
        label.fontSizeMin = 22f;
        label.fontSizeMax = 34f;
        var chev = SpaceTheme.Rect("Chevron", rt, new Vector2(1f, 0.5f), new Vector2(-44f, 0f), new Vector2(30f, 44f));
        SpaceTheme.Bar(chev, new Vector2(-1f, 8f), 24f, 5f, -45f, SpaceTheme.TEXT_SUB);
        SpaceTheme.Bar(chev, new Vector2(-1f, -8f), 24f, 5f, 45f, SpaceTheme.TEXT_SUB);
        return label;
    }

    private static Chip NewChip(RectTransform parent, Vector2 topLeft, Vector2 size, string text, float fontSize,
        UnityAction onTap)
    {
        var rt = SpaceTheme.Rect("Chip", parent, new Vector2(0f, 1f), topLeft, size, new Vector2(0f, 1f));
        float r = size.y * 0.5f - 1f;
        var fill = SpaceTheme.Rounded(rt, CHIP_OFF, r, true);
        var edgeRt = SpaceTheme.Rect("Edge", rt);
        SpaceTheme.Stretch(edgeRt);
        var edge = SpaceTheme.RoundedStroke(edgeRt, SpaceTheme.STROKE, r);
        var label = SpaceTheme.Text(rt, "Label", text, fontSize, SpaceTheme.TEXT_SUB, FontStyles.Bold);
        SpaceTheme.Stretch(label.rectTransform, 12f, 0f, 12f, 0f);
        label.enableAutoSizing = true;
        label.fontSizeMin = 18f;
        label.fontSizeMax = fontSize;
        SpaceTheme.Clickable(rt.gameObject, fill, onTap);
        return new Chip { Rt = rt, Fill = fill, Edge = edge, Label = label };
    }

    /// <summary>選んでいるマスはミント色（地・縁・文字）</summary>
    private static void SetChip(Chip chip, bool on)
    {
        chip.Fill.color = on ? SpaceTheme.A(SpaceTheme.MINT, 0.16f) : CHIP_OFF;
        chip.Edge.color = on ? SpaceTheme.A(SpaceTheme.MINT, 0.75f) : SpaceTheme.STROKE;
        chip.Label.color = on ? SpaceTheme.MINT : SpaceTheme.TEXT_SUB;
    }

    /// <summary>人数の －／＋（丸ボタン）</summary>
    private static CanvasGroup StepButton(RectTransform parent, Vector2 center, bool plus, UnityAction onTap)
    {
        var rt = SpaceTheme.Rect(plus ? "Plus" : "Minus", parent, new Vector2(0f, 1f), center, new Vector2(104f, 104f));
        var group = rt.gameObject.AddComponent<CanvasGroup>();
        var face = SpaceTheme.Img(rt, SpaceSprites.Circle, new Color(0.09f, 0.12f, 0.30f, 0.8f), true);
        var rim = SpaceTheme.Rect("Rim", rt);
        SpaceTheme.Stretch(rim);
        SpaceTheme.Img(rim, SpaceSprites.RingSmall, SpaceTheme.STROKE);
        SpaceTheme.Bar(rt, Vector2.zero, 40f, 6f, 0f, SpaceTheme.TEXT);
        if (plus) SpaceTheme.Bar(rt, Vector2.zero, 40f, 6f, 90f, SpaceTheme.TEXT);
        SpaceTheme.Clickable(rt.gameObject, face, onTap);
        return group;
    }

    /// <summary>カレンダーの前の月・次の月（丸ボタンに ＜ ＞）</summary>
    private static GameObject NavButton(RectTransform parent, Vector2 center, bool next, UnityAction onTap)
    {
        var rt = SpaceTheme.Rect(next ? "Next" : "Prev", parent, new Vector2(0f, 1f), center, new Vector2(88f, 88f));
        var face = SpaceTheme.Img(rt, SpaceSprites.Circle, new Color(0.09f, 0.12f, 0.30f, 0.8f), true);
        var rim = SpaceTheme.Rect("Rim", rt);
        SpaceTheme.Stretch(rim);
        SpaceTheme.Img(rim, SpaceSprites.RingSmall, SpaceTheme.STROKE);
        float s = next ? 1f : -1f;
        SpaceTheme.Bar(rt, new Vector2(-3f * s, 8f), 24f, 5f, -45f * s, SpaceTheme.TEXT);
        SpaceTheme.Bar(rt, new Vector2(-3f * s, -8f), 24f, 5f, 45f * s, SpaceTheme.TEXT);
        SpaceTheme.Clickable(rt.gameObject, face, onTap);
        return rt.gameObject;
    }

    private static void CloseButton(RectTransform sheet, float y, UnityAction onClick)
    {
        var close = SpaceTheme.Rect("Close", sheet, new Vector2(1f, 1f), new Vector2(-80f, y), new Vector2(96f, 96f));
        var face = SpaceTheme.Img(close, SpaceSprites.Circle, SpaceTheme.A(new Color(0.09f, 0.12f, 0.30f), 0.65f), true);
        var rim = SpaceTheme.Rect("Rim", close);
        SpaceTheme.Stretch(rim);
        SpaceTheme.Img(rim, SpaceSprites.RingSmall, SpaceTheme.STROKE);
        SpaceTheme.Bar(close, Vector2.zero, 44f, 6f, 45f, SpaceTheme.TEXT_SUB);
        SpaceTheme.Bar(close, Vector2.zero, 44f, 6f, -45f, SpaceTheme.TEXT_SUB);
        SpaceTheme.Clickable(close.gameObject, face, onClick);
    }

    /// <summary>ゲーム名の検索欄（入力は絞り込みにだけ使い、書き込みはしない）</summary>
    internal static TMP_InputField SearchInput(RectTransform parent, Vector2 topLeft, Vector2 size, string placeholder)
    {
        var bar = SpaceTheme.Rect("Search", parent, new Vector2(0f, 1f), topLeft, size, new Vector2(0f, 1f));
        SpaceTheme.Glass(bar, 32f, new Color(0.03f, 0.04f, 0.12f, 0.9f), SpaceTheme.A(SpaceTheme.CYAN, 0.35f), true);
        var area = SpaceTheme.Rect("TextArea", bar);
        SpaceTheme.Stretch(area, 32f, 6f, 32f, 6f);
        area.gameObject.AddComponent<RectMask2D>();
        var ph = SpaceTheme.Text(area, "Placeholder", placeholder, 30, SpaceTheme.TEXT_MUTED, FontStyles.Bold,
            TextAlignmentOptions.Left);
        SpaceTheme.Stretch(ph.rectTransform);
        var tx = SpaceTheme.Text(area, "Text", "", 30, SpaceTheme.TEXT, FontStyles.Bold, TextAlignmentOptions.Left);
        SpaceTheme.Stretch(tx.rectTransform);

        var input = bar.gameObject.AddComponent<TMP_InputField>();
        input.textViewport = area;
        input.textComponent = tx;
        input.placeholder = ph;
        input.fontAsset = SpaceTheme.Font;
        input.pointSize = 30;
        input.richText = false;
        input.customCaretColor = true;
        input.caretColor = SpaceTheme.CYAN;
        input.selectionColor = SpaceTheme.A(SpaceTheme.CYAN, 0.35f);
        input.lineType = TMP_InputField.LineType.SingleLine;
        return input;
    }
}
