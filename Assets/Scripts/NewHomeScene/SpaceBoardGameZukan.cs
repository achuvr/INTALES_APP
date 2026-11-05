using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// ボードゲーム図鑑（宇宙デザイン）。新ホームの図鑑の小さな惑星から開く全画面
/// （図鑑をダウンロードした人だけ。SpaceZukan）。できることは旧ホームの BoardGameListController と同じ:
///  ・検索（タイトル・英名・ジャンルの部分一致）、人数・プレイ時間の絞り込み、
///    並び替え（五十音順・プレイ時間が短い順・人気順＝当店のお気に入り数・新着順＝お店が登録した順。開いたときは新着順）
///  ・「遊んだのみ」「お気に入りのみ」。詳細のデザイナー名・ジャンルを押すとその条件で絞り込み（上に解除の札）
///  ・各行の ✓（遊んだ）★（お気に入り）は BoardGameMarks（users/{uid} に保存）
///  ・登録から30日のゲームはタイトルの左に「NEW」（登録日＝毎朝の同期で初めて見つけた日。分からないゲームには出さない）
///  ・詳細: 人数・プレイ時間・発売年・デザイナー・人気度・登録日・ジャンル、遊んだ／お気に入り、
///    「遊んだ記録」の写真（端末ローカルのみ。撮影・新しい順／古い順・全画面で保存／削除。BoardGamePhotoStore）
/// 739件を扱うので、見えている行だけを作って使い回す（仮想化スクロール）。
/// 実機のOOM対策も旧版と同じ: 行は1フレームに3行まで作り、全ゲームの文字を40文字/フレームで先にフォントへ登録する。
/// 注意: 絵文字は使わない（フォントに無い文字が再レイアウトの無限ループ→OOMの引き金になる）。
/// </summary>
public class SpaceBoardGameZukan : MonoBehaviour
{
    /// <summary>図鑑の色（ホームの図鑑の小さな惑星と同じ緑）</summary>
    public static readonly Color ACCENT = SpaceTheme.Hex(0x63F0A8);

    // ---- 文言 ----
    private const string T_CAPTION = "BOARD GAMES";
    private const string T_TITLE = "ボードゲーム図鑑";
    private const string T_SEARCH = "タイトル・ジャンルで検索…";
    private const string T_LOADING = "読み込み中…";
    private const string T_COUNT = "件";
    private const string T_EMPTY = "該当するゲームがありません";
    private const string T_PLAYED_ONLY = "✓ 遊んだのみ";
    private const string T_FAV_ONLY = "★ お気に入りのみ";
    private const string T_NO_NAME = "（名称不明）";
    private const string T_GENRE = "ジャンル";
    private const string T_PLAYERS = "人数";
    private const string T_TIME = "プレイ時間";
    private const string T_YEAR = "発売年";
    private const string T_DESIGNER = "デザイナー";
    private const string T_POPULAR = "人気度";
    private const string T_POPULAR_B = "人がお気に入り";
    private const string T_PLAYED_ON = "✓ 遊んだ！";
    private const string T_PLAYED_OFF = "まだ遊んでない";
    private const string T_FAV_ON = "★ お気に入り";
    private const string T_FAV_OFF = "☆ お気に入り";
    private const string T_RECORD = "遊んだ記録";
    private const string T_NEWEST = "新しい順";
    private const string T_OLDEST = "古い順";
    private const string T_SHOOT = "撮影";
    private const string T_CLOSE = "とじる";
    private const string T_DASH = "—";
    private const string T_NEW = "NEW";
    private const string T_ADDED = "登録日";
    private static readonly string[] PLAYER_LABELS = { "指定なし", "1人", "2人", "3人", "4人", "5人", "6人", "7人", "8人以上" };
    private static readonly int[] PLAYER_VALUES = { 0, 1, 2, 3, 4, 5, 6, 7, 8 };
    private static readonly string[] TIME_LABELS =
        { "指定なし", "15分以内", "30分以内", "45分以内", "60分以内", "90分以内", "60分超", "90分以上" };
    private static readonly int[] TIME_VALUES = { 0, 15, 30, 45, 60, 90, 999, -90 };
    private static readonly string[] SORT_LABELS = { "五十音順", "プレイ時間が短い順", "人気順", "新着順" };
    private static readonly string[] SORT_SHORT = { "五十音順", "時間が短い順", "人気順", "新着順" }; // 並び順のボタンの文字
    private const int DEFAULT_SORT = 3; // 開いたときの並び順＝新着順（2026-11-05 ユーザー指定）
    private static readonly string[] POPUP_TITLES = { "遊ぶ人数で絞り込む", "プレイ時間で絞り込む", "並び順を選ぶ" };

    /// <summary>この画面で表示しうる決まった文言（ゲームの名前などは開いたときに登録する）</summary>
    public static string GlyphText
    {
        get
        {
            var sb = new StringBuilder();
            sb.Append(T_CAPTION).Append(T_TITLE).Append(T_SEARCH).Append(T_LOADING).Append(T_COUNT).Append(T_EMPTY)
              .Append(T_PLAYED_ONLY).Append(T_FAV_ONLY).Append(T_NO_NAME).Append(T_GENRE).Append(T_PLAYERS).Append(T_TIME)
              .Append(T_YEAR).Append(T_DESIGNER).Append(T_POPULAR).Append(T_POPULAR_B).Append(T_PLAYED_ON)
              .Append(T_PLAYED_OFF).Append(T_FAV_ON).Append(T_FAV_OFF).Append(T_RECORD).Append(T_NEWEST)
              .Append(T_OLDEST).Append(T_SHOOT).Append(T_CLOSE).Append(T_DASH).Append(T_NEW).Append(T_ADDED).Append("時間並び順▼✕：: 0123456789月日以上超内分人")
              .Append("撮影日時年保存中端末の写真にしました失敗権限を確認してください本当に削除？もう一度タップすると削除します読み込めませんでした");
            foreach (var s in PLAYER_LABELS) sb.Append(s);
            foreach (var s in TIME_LABELS) sb.Append(s);
            foreach (var s in SORT_LABELS) sb.Append(s);
            foreach (var s in POPUP_TITLES) sb.Append(s);
            return sb.ToString();
        }
    }

    // ---- 配置（セーフエリアの上端からの距離）----
    private const float SIDE = 40f;
    private const float SEARCH_TOP = 188f, SEARCH_H = 96f;
    private const float FILTER_TOP = 300f, FILTER_H = 88f;
    private const float CHECK_TOP = 400f, CHECK_H = 80f;
    private const float TAG_TOP = 492f, TAG_H = 80f;
    private const float LIST_TOP = 492f;              // 絞り込みの札が出ている間は TAG_H + 12 だけ下げる
    private const float LIST_BOTTOM = 24f;
    private const float ROW_H = 176f, ROW_GAP = 16f, ROW_STEP = ROW_H + ROW_GAP;
    private const float PAD_T = 8f, PAD_B = 40f;
    private const int BUFFER = 2;                     // 画面外に余分に出しておく行数
    private const float MARK_SIZE = 72f, THUMB = 128f;
    private const float NEW_W = 84f, NEW_H = 38f;     // 「NEW」の札
    private const int NEW_DAYS = 30;                  // 登録から何日「NEW」を出すか
    private const int THUMB_CACHE_MAX = 64;           // 256pxのサムネイル≒0.2MB/枚 → 上限でも十数MB

    // 詳細の縦の大きさ（情報の量に応じて BASE〜MAX の間で伸ばす）
    private const float DETAIL_W = 940f;
    private const float DETAIL_BASE_H = 1276f, DETAIL_MAX_H = 1560f;
    private const float DETAIL_STACK_TOP = 232f, DETAIL_STACK_BOTTOM = 500f;

    private static readonly Color PILL_FILL = new Color(0.07f, 0.09f, 0.24f, 0.85f);
    private static readonly Color MARK_OFF = SpaceTheme.A(SpaceTheme.LINE, 0.06f);
    private static readonly string LINK_HEX = "#" + ColorUtility.ToHtmlStringRGB(SpaceTheme.CYAN);
    private static readonly string LABEL_HEX = "#" + ColorUtility.ToHtmlStringRGB(SpaceTheme.TEXT_MUTED);
    private static readonly string NEW_HEX = "#" + ColorUtility.ToHtmlStringRGB(SpaceTheme.PINK);

    // ---- 状態 ----
    private Canvas _canvas;
    private CanvasGroup _group;
    private RectTransform _safe, _scrollRoot, _viewport, _listContent;
    private ScrollRect _scroll;
    private TextMeshProUGUI _countLabel;
    private TMP_InputField _searchInput;
    private GameObject _emptyLabel, _detail, _popup, _tagBar;
    private bool _closing;
    private bool _ready; // 全ゲームの文字をフォントへ登録し終えた（それまでは一覧を作らない）

    private readonly List<BoardGameEntry> _all = new List<BoardGameEntry>();
    private readonly List<BoardGameEntry> _items = new List<BoardGameEntry>();
    private readonly Dictionary<BoardGameEntry, int> _orderIndex = new Dictionary<BoardGameEntry, int>();
    private string _query = "";
    // 人数（0=指定なし、1〜7=その人数、8=8人以上）・時間（15〜90=〜分以内、999=60分超、-90=90分以上）・並び順
    private int _playersFilter, _timeFilter, _sortMode = DEFAULT_SORT;
    private bool _playedOnly, _favOnly;
    private string _tagFilter;   // デザイナー/ジャンルでの絞り込み（null=なし）
    private bool _tagIsDesigner;

    private Pill _playersBtn, _timeBtn, _sortBtn, _playedBtn, _favBtn;

    private struct ParsedRange { public int PMin, PMax, TMax; }
    private readonly Dictionary<BoardGameEntry, ParsedRange> _parsed = new Dictionary<BoardGameEntry, ParsedRange>();

    // 使い回す行
    private readonly Dictionary<int, RowView> _active = new Dictionary<int, RowView>();
    private readonly Stack<RowView> _free = new Stack<RowView>();
    private readonly List<int> _scratch = new List<int>();

    private sealed class RowView
    {
        public GameObject Go;
        public RectTransform Rt;
        public TextMeshProUGUI Title, Meta, Genre;
        public GameObject NewBadge;
        public Mark Played, Fav;
        public GameObject ThumbGo;
        public RawImage ThumbRaw;
        public RectTransform ThumbImgRt;
        public BoardGameEntry Entry;
    }

    private sealed class Mark
    {
        public Image Face, Ring;
        public TextMeshProUGUI Label;
    }

    private sealed class Pill
    {
        public RectTransform Rt;
        public Image Face, Edge;
        public TextMeshProUGUI Label;
    }

    // 詳細（開いている間だけ）
    private BoardGameEntry _detailEntry;
    private Pill _detailPlayed, _detailFav, _photoSort;
    private RectTransform _photoContent;
    private readonly List<Texture2D> _detailThumbs = new List<Texture2D>();

    // 行のサムネイル（遊んだ記録の1枚目）
    private readonly Dictionary<BoardGameEntry, string> _firstPhoto = new Dictionary<BoardGameEntry, string>();
    private readonly Dictionary<string, Texture2D> _thumbCache = new Dictionary<string, Texture2D>();
    private readonly List<string> _thumbOrder = new List<string>();

    // ================================================================
    // 開く・閉じる
    // ================================================================
    public static SpaceBoardGameZukan Open(Canvas canvas)
    {
        if (canvas == null) canvas = SpaceTheme.MainCanvas();
        var root = SpaceTheme.Rect("__SpaceBoardGameZukan", canvas.transform);
        SpaceTheme.Stretch(root);
        root.SetAsLastSibling();
        SpaceTheme.SubCanvas(root.gameObject, true); // スクロールの再描画をホームと分ける
        var zukan = root.gameObject.AddComponent<SpaceBoardGameZukan>();
        zukan._canvas = canvas;
        zukan.Build();
        return zukan;
    }

    /// <summary>戻る操作: 手前から閉じる（写真 → 詳細 → 選択肢 → 図鑑）</summary>
    public void HandleBack()
    {
        var viewer = transform.Find("__PhotoViewer");
        if (viewer != null) Destroy(viewer.gameObject);
        else if (_detail != null) CloseDetail();
        else if (_popup != null) ClosePopup();
        else Close();
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
        foreach (var t in _thumbCache.Values) Destroy(t);
        _thumbCache.Clear();
        _thumbOrder.Clear();
        foreach (var t in _detailThumbs) Destroy(t);
        _detailThumbs.Clear();
    }

    // ================================================================
    // 組み立て
    // ================================================================
    private void Build()
    {
        var rt = (RectTransform)transform;
        _group = gameObject.AddComponent<CanvasGroup>();
        _group.alpha = 0f;

        // ホームを覆う暗い宇宙（下のホームを押させない）
        var dim = SpaceTheme.Rect("Dim", rt);
        SpaceTheme.Stretch(dim);
        SpaceTheme.Img(dim, null, new Color(0.01f, 0.015f, 0.05f, 0.97f), true);
        var glow = SpaceTheme.Rect("Glow", rt, new Vector2(0.5f, 1f), new Vector2(0f, -120f), new Vector2(1500f, 900f));
        SpaceTheme.Img(glow, SpaceSprites.Glow, SpaceTheme.A(ACCENT, 0.10f));

        _safe = SpaceTheme.Rect("Safe", rt);
        SpaceTheme.Stretch(_safe);
        _safe.gameObject.AddComponent<SpaceSafeArea>();

        var caption = SpaceTheme.Text(_safe, "Caption", T_CAPTION, 24, SpaceTheme.A(ACCENT, 0.9f), new Vector2(0f, 1f),
            new Vector2(SIDE + 16f, -70f), new Vector2(500f, 36f), FontStyles.Bold, TextAlignmentOptions.Left,
            new Vector2(0f, 0.5f));
        caption.characterSpacing = 18f;
        SpaceTheme.Text(_safe, "Title", T_TITLE, 52, SpaceTheme.TEXT, new Vector2(0f, 1f), new Vector2(SIDE + 16f, -126f),
            new Vector2(700f, 66f), FontStyles.Bold, TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
        CloseButton(_safe, -84f, Close);

        var data = BoardGameCatalog.Load();
        _all.AddRange(data.games ?? Array.Empty<BoardGameEntry>());
        for (int i = 0; i < _all.Count; i++) _orderIndex[_all[i]] = i;

        StartCoroutine(SpaceTheme.Tween(0.25f, 0f, k => _group.alpha = k));
        StartCoroutine(BuildRoutine());
    }

    /// <summary>
    /// 1フレームに大量のUIを作らないよう、フレームを分けて組み立てる（旧版と同じ実機OOM対策）。
    /// 行は1フレームに3行まで先に作っておき、全ゲームの文字を40文字/フレームでフォントへ登録してから並べる
    /// </summary>
    private IEnumerator BuildRoutine()
    {
        yield return null;
        BuildSearch();
        yield return null;
        BuildFilters();
        BuildList();
        _countLabel.text = T_LOADING;
        yield return null;

        float viewH = _viewport.rect.height > 1f ? _viewport.rect.height : 1400f;
        int need = Mathf.CeilToInt(viewH / ROW_STEP) + BUFFER * 2 + 2;
        while (_free.Count < need)
        {
            for (int i = 0; i < 3 && _free.Count < need; i++) Recycle(CreateRow());
            yield return null;
        }

        var sb = new StringBuilder();
        foreach (var g in _all)
        {
            sb.Append(g.title).Append(g.title_en).Append(g.players).Append(g.time).Append(g.year);
            if (g.genre != null) foreach (var t in g.genre) sb.Append(t);
            if (g.designer != null) foreach (var d in g.designer) sb.Append(d);
        }
        sb.Append('_'); // リンクの下線（TMP は '_' の形で下線を描く）
        yield return SpaceTheme.Prewarm(sb.ToString());

        _ready = true;
        RebuildList(); // 読み込み中に変えた絞り込み・検索もここで反映される
    }

    private void BuildSearch()
    {
        var bar = TopBar("Search", SEARCH_TOP, SEARCH_H);
        var face = SpaceTheme.Glass(bar, 40f, PILL_FILL, SpaceTheme.A(ACCENT, 0.35f), true);

        var area = SpaceTheme.Rect("TextArea", bar);
        SpaceTheme.Stretch(area, 32f, 6f, 200f, 6f);
        area.gameObject.AddComponent<RectMask2D>();
        var placeholder = SpaceTheme.Text(area, "Placeholder", T_SEARCH, 32, SpaceTheme.TEXT_MUTED, FontStyles.Bold,
            TextAlignmentOptions.Left);
        SpaceTheme.Stretch(placeholder.rectTransform);
        var text = SpaceTheme.Text(area, "Text", "", 32, SpaceTheme.TEXT, FontStyles.Bold, TextAlignmentOptions.Left);
        SpaceTheme.Stretch(text.rectTransform);

        var input = bar.gameObject.AddComponent<TMP_InputField>();
        input.textViewport = area;
        input.textComponent = text;
        input.placeholder = placeholder;
        input.fontAsset = SpaceTheme.Font;
        input.pointSize = 32;
        input.customCaretColor = true;
        input.caretColor = ACCENT;
        input.selectionColor = SpaceTheme.A(ACCENT, 0.3f);
        input.lineType = TMP_InputField.LineType.SingleLine;
        input.targetGraphic = face;
        input.onValueChanged.AddListener(q =>
        {
            _query = (q ?? "").Trim();
            RebuildList();
        });
        _searchInput = input;

        _countLabel = SpaceTheme.Text(bar, "Count", "", 30, SpaceTheme.TEXT_MUTED, new Vector2(1f, 0.5f),
            new Vector2(-28f, 0f), new Vector2(180f, 50f), FontStyles.Bold, TextAlignmentOptions.Right,
            new Vector2(1f, 0.5f));
    }

    /// <summary>人数・時間・並び順のボタンと、「遊んだのみ」「お気に入りのみ」</summary>
    private void BuildFilters()
    {
        var bar = TopBar("Filters", FILTER_TOP, FILTER_H);
        _playersBtn = MakePill(bar, "Players", 44f, 30f, () => ShowPopup(0));
        _timeBtn = MakePill(bar, "Time", 44f, 30f, () => ShowPopup(1));
        _sortBtn = MakePill(bar, "Sort", 44f, 30f, () => ShowPopup(2));
        Split(_playersBtn.Rt, 0f, 1f / 3f);
        Split(_timeBtn.Rt, 1f / 3f, 2f / 3f);
        Split(_sortBtn.Rt, 2f / 3f, 1f);

        var checks = TopBar("Checks", CHECK_TOP, CHECK_H);
        _playedBtn = MakePill(checks, "PlayedOnly", 40f, 30f, () =>
        {
            _playedOnly = !_playedOnly;
            UpdateFilterButtons();
            RebuildList();
        });
        _favBtn = MakePill(checks, "FavOnly", 40f, 30f, () =>
        {
            _favOnly = !_favOnly;
            UpdateFilterButtons();
            RebuildList();
        });
        Split(_playedBtn.Rt, 0f, 0.5f);
        Split(_favBtn.Rt, 0.5f, 1f);
        UpdateFilterButtons();
    }

    private void UpdateFilterButtons()
    {
        if (_playersBtn == null) return;
        _playersBtn.Label.text = _playersFilter == 0 ? "人数 ▼" : _playersFilter >= 8 ? "8人以上 ▼" : $"{_playersFilter}人 ▼";
        SetPill(_playersBtn, _playersFilter != 0, ACCENT);
        _timeBtn.Label.text = _timeFilter == 0 ? "時間 ▼"
            : _timeFilter == 999 ? "60分超 ▼"
            : _timeFilter < 0 ? $"{-_timeFilter}分以上 ▼"
            : $"{_timeFilter}分以内 ▼";
        SetPill(_timeBtn, _timeFilter != 0, ACCENT);
        // いまの並び順を出す。開いたときの並び順（新着順）以外を選んでいるときだけ光らせる
        _sortBtn.Label.text = SORT_SHORT[Mathf.Clamp(_sortMode, 0, SORT_SHORT.Length - 1)] + " ▼";
        SetPill(_sortBtn, _sortMode != DEFAULT_SORT, ACCENT);
        _playedBtn.Label.text = T_PLAYED_ONLY;
        SetPill(_playedBtn, _playedOnly, SpaceTheme.MINT);
        _favBtn.Label.text = T_FAV_ONLY;
        SetPill(_favBtn, _favOnly, SpaceTheme.GOLD);
    }

    private void BuildList()
    {
        _scrollRoot = SpaceTheme.Rect("List", _safe);
        _scrollRoot.anchorMin = Vector2.zero;
        _scrollRoot.anchorMax = Vector2.one;
        _scrollRoot.offsetMin = new Vector2(SIDE, LIST_BOTTOM);
        _scrollRoot.offsetMax = new Vector2(-SIDE, -ListTop());
        _scroll = _scrollRoot.gameObject.AddComponent<ScrollRect>();
        _scroll.horizontal = false;
        _scroll.scrollSensitivity = 40f;
        _scroll.movementType = ScrollRect.MovementType.Clamped;

        _viewport = SpaceTheme.Rect("Viewport", _scrollRoot);
        SpaceTheme.Stretch(_viewport);
        SpaceTheme.HitArea(_viewport); // 行の間でもドラッグできるように
        _viewport.gameObject.AddComponent<RectMask2D>().softness = new Vector2Int(0, 24);
        _scroll.viewport = _viewport;

        _listContent = SpaceTheme.Rect("Content", _viewport);
        _listContent.anchorMin = new Vector2(0f, 1f);
        _listContent.anchorMax = new Vector2(1f, 1f);
        _listContent.pivot = new Vector2(0.5f, 1f);
        _listContent.offsetMin = _listContent.offsetMax = Vector2.zero;
        _scroll.content = _listContent;
        _scroll.onValueChanged.AddListener(_ => UpdateVisibleRows());
    }

    private float ListTop() => LIST_TOP + (_tagFilter == null ? 0f : TAG_H + 12f);

    // ================================================================
    // 絞り込み・並び替え
    // ================================================================
    private void RebuildList()
    {
        // 文字の登録が終わるまでは作らない（未登録の文字を1フレームで大量に足すと実機で落ちるため）
        if (_listContent == null || !_ready) return;
        foreach (var kv in _active) Recycle(kv.Value);
        _active.Clear();

        _items.Clear();
        string q = string.IsNullOrEmpty(_query) ? null : _query.ToLowerInvariant();
        foreach (var g in _all)
        {
            if (q != null && !Matches(g, q)) continue;
            if (!MatchesFilters(g)) continue;
            _items.Add(g);
        }

        if (_sortMode == 1)
        {
            _items.Sort((a, b) =>
            {
                int ta = SortTimeKey(a), tb = SortTimeKey(b);
                return ta != tb ? ta.CompareTo(tb) : _orderIndex[a].CompareTo(_orderIndex[b]);
            });
        }
        else if (_sortMode == 2)
        {
            _items.Sort((a, b) =>
            {
                int c = b.popularity.CompareTo(a.popularity);
                return c != 0 ? c : _orderIndex[a].CompareTo(_orderIndex[b]);
            });
        }
        else if (_sortMode == 3)
        {
            // 新着順: お店が登録した順（新しいものが先）。順番が分からないゲームは最後に五十音順
            _items.Sort((a, b) =>
            {
                int ra = a.added_rank > 0 ? a.added_rank : int.MaxValue;
                int rb = b.added_rank > 0 ? b.added_rank : int.MaxValue;
                return ra != rb ? ra.CompareTo(rb) : _orderIndex[a].CompareTo(_orderIndex[b]);
            });
        }

        int n = _items.Count;
        _listContent.sizeDelta = new Vector2(0f, n > 0 ? PAD_T + n * ROW_H + (n - 1) * ROW_GAP + PAD_B : 0f);
        _listContent.anchoredPosition = Vector2.zero;
        _countLabel.text = n + T_COUNT;
        SetEmptyVisible(n == 0);
        Canvas.ForceUpdateCanvases();
        UpdateVisibleRows();
    }

    private static bool Matches(BoardGameEntry g, string q)
    {
        if (g.title != null && g.title.ToLowerInvariant().Contains(q)) return true;
        if (g.title_en != null && g.title_en.ToLowerInvariant().Contains(q)) return true;
        if (g.genre != null)
            foreach (var t in g.genre)
                if (t != null && t.ToLowerInvariant().Contains(q)) return true;
        return false;
    }

    /// <summary>人数・時間・デザイナー/ジャンル・✓★の判定（人数・時間が不明なゲームは、指定したときは出さない）</summary>
    private bool MatchesFilters(BoardGameEntry g)
    {
        if (_playersFilter > 0)
        {
            var r = GetRange(g);
            if (r.PMax <= 0) return false;
            if (_playersFilter >= 8 ? r.PMax < 8 : (_playersFilter < r.PMin || r.PMax < _playersFilter)) return false;
        }
        if (_timeFilter != 0)
        {
            var r = GetRange(g);
            if (r.TMax <= 0) return false;
            bool ok = _timeFilter == 999 ? r.TMax > 60 : _timeFilter < 0 ? r.TMax >= -_timeFilter : r.TMax <= _timeFilter;
            if (!ok) return false;
        }
        if (_tagFilter != null)
        {
            var tags = _tagIsDesigner ? g.designer : g.genre;
            if (tags == null || Array.IndexOf(tags, _tagFilter) < 0) return false;
        }
        if (_playedOnly && !BoardGameMarks.IsPlayed(g)) return false;
        if (_favOnly && !BoardGameMarks.IsFavorite(g)) return false;
        return true;
    }

    private int SortTimeKey(BoardGameEntry g)
    {
        int t = GetRange(g).TMax;
        return t > 0 ? t : int.MaxValue; // 時間不明は最後
    }

    /// <summary>「2人～4人」「15分～30分」を数値の範囲にする（結果は覚えておく）</summary>
    private ParsedRange GetRange(BoardGameEntry g)
    {
        if (_parsed.TryGetValue(g, out var r)) return r;
        r = new ParsedRange();
        var pm = Regex.Match(g.players ?? "", @"([0-9]+)人(?:～([0-9]+)人)?");
        if (pm.Success)
        {
            r.PMin = int.Parse(pm.Groups[1].Value);
            r.PMax = pm.Groups[2].Success ? int.Parse(pm.Groups[2].Value) : r.PMin;
        }
        var tm = Regex.Match(g.time ?? "", @"([0-9]+)分(?:～([0-9]+)分)?");
        if (tm.Success)
        {
            int t1 = int.Parse(tm.Groups[1].Value);
            r.TMax = tm.Groups[2].Success ? int.Parse(tm.Groups[2].Value) : t1;
        }
        _parsed[g] = r;
        return r;
    }

    private void SetEmptyVisible(bool show)
    {
        if (show && _emptyLabel == null)
        {
            var t = SpaceTheme.Text(_viewport, "Empty", T_EMPTY, 32, SpaceTheme.TEXT_MUTED, FontStyles.Bold);
            SpaceTheme.Stretch(t.rectTransform);
            _emptyLabel = t.gameObject;
        }
        if (_emptyLabel != null) _emptyLabel.SetActive(show);
    }

    // ---------------------------------------------------------------- 選択肢（人数・時間・並び順）
    private void ShowPopup(int kind)
    {
        ClosePopup();
        var labels = kind == 0 ? PLAYER_LABELS : kind == 1 ? TIME_LABELS : SORT_LABELS;
        var values = kind == 0 ? PLAYER_VALUES : kind == 1 ? TIME_VALUES : new[] { 0, 1, 2, 3 };
        int current = kind == 0 ? _playersFilter : kind == 1 ? _timeFilter : _sortMode;

        var root = SpaceTheme.Rect("Popup", transform);
        SpaceTheme.Stretch(root);
        _popup = root.gameObject;
        var dim = SpaceTheme.Rect("Dim", root);
        SpaceTheme.Stretch(dim);
        var dimImg = SpaceTheme.Img(dim, null, SpaceTheme.DIM, true);
        SpaceTheme.Clickable(dim.gameObject, dimImg, ClosePopup, false);

        float h = 130f + labels.Length * 102f + 30f;
        var card = SpaceTheme.Rect("Card", root, SpaceTheme.CENTER, Vector2.zero, new Vector2(660f, h));
        SpaceTheme.Glass(card, 48f, SpaceTheme.GLASS_DEEP, SpaceTheme.A(ACCENT, 0.4f), true);
        SpaceTheme.Text(card, "Title", POPUP_TITLES[kind], 38, SpaceTheme.TEXT, new Vector2(0.5f, 1f), new Vector2(0f, -70f),
            new Vector2(600f, 56f), FontStyles.Bold);
        for (int i = 0; i < labels.Length; i++)
        {
            int value = values[i];
            var opt = MakePill(card, "Option" + i, 44f, 32f, () =>
            {
                if (kind == 0) _playersFilter = value;
                else if (kind == 1) _timeFilter = value;
                else _sortMode = value;
                UpdateFilterButtons();
                RebuildList();
                ClosePopup();
            });
            opt.Rt.anchorMin = opt.Rt.anchorMax = new Vector2(0.5f, 1f);
            opt.Rt.pivot = new Vector2(0.5f, 1f);
            opt.Rt.sizeDelta = new Vector2(540f, 88f);
            opt.Rt.anchoredPosition = new Vector2(0f, -(130f + i * 102f));
            opt.Label.text = labels[i];
            SetPill(opt, value == current, ACCENT);
        }
        root.gameObject.AddComponent<SpacePopIn>().Target = card;
    }

    private void ClosePopup()
    {
        if (_popup != null) Destroy(_popup);
        _popup = null;
    }

    // ---------------------------------------------------------------- デザイナー/ジャンルで絞り込む
    private void ApplyTagFilter(bool isDesigner, string value)
    {
        if (string.IsNullOrEmpty(value)) return;
        _tagIsDesigner = isDesigner;
        _tagFilter = value;
        // 検索語は「特定のゲームを探す」操作なので、リンクから絞り込むときは消す（人数・時間・✓★は残す）
        if (_searchInput != null) _searchInput.SetTextWithoutNotify("");
        _query = "";
        CloseDetail();
        UpdateTagBar();
        RebuildList();
    }

    private void ClearTagFilter()
    {
        _tagFilter = null;
        UpdateTagBar();
        RebuildList();
    }

    /// <summary>絞り込みの札（チェックの下）を作り直し、一覧の上端も合わせる。札のどこを押しても解除</summary>
    private void UpdateTagBar()
    {
        if (_tagBar != null) Destroy(_tagBar);
        _tagBar = null;
        if (_scrollRoot != null) _scrollRoot.offsetMax = new Vector2(-SIDE, -ListTop());
        if (_tagFilter == null) return;

        var bar = TopBar("TagFilter", TAG_TOP, TAG_H);
        _tagBar = bar.gameObject;
        var pill = MakePill(bar, "Chip", 40f, 30f, ClearTagFilter);
        SpaceTheme.Stretch(pill.Rt);
        SetPill(pill, true, SpaceTheme.CYAN);
        pill.Label.alignment = TextAlignmentOptions.Left;
        pill.Label.rectTransform.offsetMin = new Vector2(32f, 0f);
        pill.Label.rectTransform.offsetMax = new Vector2(-96f, 0f);
        pill.Label.overflowMode = TextOverflowModes.Ellipsis;
        pill.Label.text = SpaceTheme.NoParse(SpaceTheme.Safe($"{(_tagIsDesigner ? T_DESIGNER : T_GENRE)}: {_tagFilter}"));
        var x = SpaceTheme.Text(pill.Rt, "Clear", "✕", 34, SpaceTheme.CYAN, new Vector2(1f, 0.5f), new Vector2(-40f, 0f),
            new Vector2(60f, 60f), FontStyles.Bold);
        x.rectTransform.pivot = new Vector2(0.5f, 0.5f);
    }

    // ================================================================
    // 仮想化スクロール（見えている行だけを作って使い回す）
    // ================================================================
    private void UpdateVisibleRows()
    {
        if (_listContent == null || _items.Count == 0) return;
        float viewH = _viewport.rect.height > 1f ? _viewport.rect.height : 1400f;
        float scrollY = Mathf.Max(0f, _listContent.anchoredPosition.y);
        int first = Mathf.Max(0, Mathf.FloorToInt((scrollY - PAD_T) / ROW_STEP) - BUFFER);
        int last = Mathf.Min(_items.Count - 1, Mathf.FloorToInt((scrollY + viewH - PAD_T) / ROW_STEP) + BUFFER);

        _scratch.Clear();
        foreach (var kv in _active)
            if (kv.Key < first || kv.Key > last) _scratch.Add(kv.Key);
        foreach (int idx in _scratch)
        {
            Recycle(_active[idx]);
            _active.Remove(idx);
        }
        for (int i = first; i <= last; i++)
            if (!_active.ContainsKey(i)) _active[i] = BindRow(i);
    }

    private void Recycle(RowView row)
    {
        if (row == null) return;
        row.Entry = null;
        row.Go.SetActive(false);
        _free.Push(row);
    }

    private RowView BindRow(int index)
    {
        var row = _free.Count > 0 ? _free.Pop() : CreateRow();
        var g = _items[index];
        row.Entry = g;
        row.Rt.anchoredPosition = new Vector2(0f, -(PAD_T + index * ROW_STEP));
        row.Title.text = Plain(string.IsNullOrEmpty(g.title) ? T_NO_NAME : g.title);
        // 登録から30日は、タイトルの左に「NEW」（タイトルはその右から）
        bool isNew = IsNew(g);
        row.NewBadge.SetActive(isNew);
        var titleRt = row.Title.rectTransform;
        titleRt.offsetMin = new Vector2(isNew ? 40f + NEW_W + 14f : 40f, titleRt.offsetMin.y);
        row.Meta.text = Plain(Meta(g));
        row.Genre.text = Plain(g.genre != null && g.genre.Length > 0
            ? T_GENRE + ": " + string.Join("・", g.genre)
            : T_GENRE + ": " + T_DASH);
        UpdateMarks(row);
        UpdateRowThumb(row);
        row.Go.SetActive(true);
        return row;
    }

    /// <summary>登録日（毎朝の同期でこのゲームを初めて見つけた日）。分からなければ false</summary>
    private static bool TryAddedOn(BoardGameEntry g, out DateTime day) =>
        DateTime.TryParseExact(g.added_on ?? "", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day);

    /// <summary>登録から NEW_DAYS 日のあいだは「NEW」（端末の日付が登録日より前でも NEW のまま）</summary>
    private static bool IsNew(BoardGameEntry g) =>
        TryAddedOn(g, out var day) && (DateTime.Today - day).TotalDays < NEW_DAYS;

    private static string Meta(BoardGameEntry g)
    {
        var parts = new List<string>
        {
            string.IsNullOrEmpty(g.players) ? T_PLAYERS + " " + T_DASH : g.players,
            string.IsNullOrEmpty(g.time) ? "時間 " + T_DASH : g.time,
        };
        if (!string.IsNullOrEmpty(g.year)) parts.Add(g.year);
        return string.Join("　", parts);
    }

    /// <summary>使い回す1行（中身は BindRow で入れ替える）</summary>
    private RowView CreateRow()
    {
        var row = new RowView();
        var rt = SpaceTheme.Rect("Row", _listContent);
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.sizeDelta = new Vector2(0f, ROW_H);
        row.Go = rt.gameObject;
        row.Rt = rt;
        var face = SpaceTheme.Glass(rt, 36f, SpaceTheme.GLASS, null, true);
        SpaceTheme.Clickable(rt.gameObject, face, () => { if (row.Entry != null) ShowDetail(row.Entry); }, false);
        // 左端の細い光の線（図鑑の緑）
        var bar = SpaceTheme.Rect("Accent", rt, new Vector2(0f, 0.5f), new Vector2(14f, 0f), new Vector2(5f, ROW_H - 56f));
        SpaceTheme.Rounded(bar, SpaceTheme.A(ACCENT, 0.55f), 2.4f);

        row.Title = RowLabel(rt, "Title", 36, SpaceTheme.TEXT, -50f, 52f);
        row.Title.enableAutoSizing = true;
        row.Title.fontSizeMin = 26f;
        row.Title.fontSizeMax = 36f;
        row.Meta = RowLabel(rt, "Meta", 26, SpaceTheme.TEXT_SUB, -100f, 40f);
        row.Genre = RowLabel(rt, "Genre", 22, SpaceTheme.TEXT_MUTED, -140f, 34f);

        // 「NEW」の札（主ボタンと同じ系統のピンク→すみれ色。タイトルの左。新しく入ったゲームだけ出す）
        var badge = SpaceTheme.Rect("New", rt, new Vector2(0f, 1f), new Vector2(40f, -50f), new Vector2(NEW_W, NEW_H),
            new Vector2(0f, 0.5f));
        var badgeFace = SpaceTheme.Rounded(badge, Color.white, NEW_H / 2f);
        SpaceTheme.Gradient(badgeFace, SpaceTheme.PINK, SpaceTheme.VIOLET);
        var badgeLabel = SpaceTheme.Text(badge, "Label", T_NEW, 22, SpaceTheme.TEXT_DARK, FontStyles.Bold);
        SpaceTheme.Stretch(badgeLabel.rectTransform);
        badgeLabel.characterSpacing = 6f;
        row.NewBadge = badge.gameObject;
        badge.gameObject.SetActive(false);

        // ✓（遊んだ）と ★（お気に入り）。子のボタンが先に受けるので、ここを押しても詳細は開かない
        row.Played = MakeMark(rt, "✓", 40f, () => { if (row.Entry != null) TogglePlayed(row.Entry); });
        row.Fav = MakeMark(rt, "★", -40f, () => { if (row.Entry != null) ToggleFavorite(row.Entry); });

        // 遊んだ記録の1枚目（✓★の左。写真が無い行は出さない）
        var th = SpaceTheme.Rect("Thumb", rt, new Vector2(1f, 0.5f), new Vector2(-(32f + MARK_SIZE + 20f), 0f),
            new Vector2(THUMB, THUMB), new Vector2(1f, 0.5f));
        SpaceTheme.Rounded(th, SpaceTheme.A(SpaceTheme.LINE, 0.10f), 26f);
        th.gameObject.AddComponent<Mask>().showMaskGraphic = true;
        var img = SpaceTheme.Rect("Img", th, SpaceTheme.CENTER, Vector2.zero, new Vector2(THUMB, THUMB));
        row.ThumbRaw = img.gameObject.AddComponent<RawImage>();
        row.ThumbRaw.raycastTarget = false;
        row.ThumbImgRt = img;
        row.ThumbGo = th.gameObject;
        th.gameObject.SetActive(false);
        return row;
    }

    private static TextMeshProUGUI RowLabel(RectTransform row, string name, float size, Color color, float y, float h)
    {
        var t = SpaceTheme.Text(row, name, "", size, color, FontStyles.Bold, TextAlignmentOptions.Left);
        var rt = t.rectTransform;
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = new Vector2(40f, y - h / 2f);
        rt.offsetMax = new Vector2(-(32f + MARK_SIZE + 24f), y + h / 2f);
        t.overflowMode = TextOverflowModes.Ellipsis;
        return t;
    }

    private static Mark MakeMark(RectTransform row, string mark, float y, Action onTap)
    {
        var rt = SpaceTheme.Rect(mark == "✓" ? "Played" : "Fav", row, new Vector2(1f, 0.5f), new Vector2(-32f, y),
            new Vector2(MARK_SIZE, MARK_SIZE), new Vector2(1f, 0.5f));
        var face = SpaceTheme.Img(rt, SpaceSprites.Circle, MARK_OFF, true);
        var ringRt = SpaceTheme.Rect("Ring", rt);
        SpaceTheme.Stretch(ringRt);
        var ring = SpaceTheme.Img(ringRt, SpaceSprites.RingSmall, SpaceTheme.STROKE);
        var label = SpaceTheme.Text(rt, "Mark", mark, 38, SpaceTheme.TEXT_MUTED, FontStyles.Bold);
        SpaceTheme.Stretch(label.rectTransform);
        SpaceTheme.Clickable(rt.gameObject, face, () => onTap());
        return new Mark { Face = face, Ring = ring, Label = label };
    }

    // ================================================================
    // ✓（遊んだ）★（お気に入り）
    // ================================================================
    private void TogglePlayed(BoardGameEntry g)
    {
        BoardGameMarks.SetPlayed(g, !BoardGameMarks.IsPlayed(g));
        RefreshMarks(g);
        if (_playedOnly) RebuildList(); // 「遊んだのみ」のときは外したゲームをすぐ消す
    }

    private void ToggleFavorite(BoardGameEntry g)
    {
        BoardGameMarks.SetFavorite(g, !BoardGameMarks.IsFavorite(g));
        RefreshMarks(g);
        if (_favOnly) RebuildList();
    }

    private void RefreshMarks(BoardGameEntry g)
    {
        foreach (var kv in _active)
            if (kv.Value.Entry == g) UpdateMarks(kv.Value);
        UpdateDetailMarks();
    }

    private static void UpdateMarks(RowView row)
    {
        if (row.Entry == null) return;
        PaintMark(row.Played, BoardGameMarks.IsPlayed(row.Entry), SpaceTheme.MINT);
        PaintMark(row.Fav, BoardGameMarks.IsFavorite(row.Entry), SpaceTheme.GOLD);
    }

    private static void PaintMark(Mark m, bool on, Color color)
    {
        m.Face.color = on ? SpaceTheme.A(color, 0.20f) : MARK_OFF;
        m.Ring.color = on ? SpaceTheme.A(color, 0.9f) : SpaceTheme.STROKE;
        m.Label.color = on ? color : SpaceTheme.A(SpaceTheme.TEXT_MUTED, 0.6f);
    }

    private void UpdateDetailMarks()
    {
        if (_detailEntry == null || _detailPlayed == null) return;
        bool played = BoardGameMarks.IsPlayed(_detailEntry);
        _detailPlayed.Label.text = played ? T_PLAYED_ON : T_PLAYED_OFF;
        SetPill(_detailPlayed, played, SpaceTheme.MINT);
        bool fav = BoardGameMarks.IsFavorite(_detailEntry);
        _detailFav.Label.text = fav ? T_FAV_ON : T_FAV_OFF;
        SetPill(_detailFav, fav, SpaceTheme.GOLD);
    }

    // ================================================================
    // 詳細
    // ================================================================
    private void ShowDetail(BoardGameEntry g)
    {
        CloseDetail();
        _detailEntry = g;

        var root = SpaceTheme.Rect("Detail", transform);
        SpaceTheme.Stretch(root);
        _detail = root.gameObject;
        var dim = SpaceTheme.Rect("Dim", root);
        SpaceTheme.Stretch(dim);
        var dimImg = SpaceTheme.Img(dim, null, SpaceTheme.DIM, true);
        SpaceTheme.Clickable(dim.gameObject, dimImg, CloseDetail, false);

        var panel = SpaceTheme.Rect("Card", root, SpaceTheme.CENTER, Vector2.zero, new Vector2(DETAIL_W, DETAIL_BASE_H));
        SpaceTheme.Glass(panel, 56f, SpaceTheme.GLASS_DEEP, SpaceTheme.A(ACCENT, 0.4f), true); // 中のタップで閉じない
        var glow = SpaceTheme.Rect("Glow", panel, new Vector2(0.5f, 1f), new Vector2(0f, -90f), new Vector2(900f, 360f));
        SpaceTheme.Img(glow, SpaceSprites.Glow, SpaceTheme.A(ACCENT, 0.10f));

        // 和名・英名・区切り線
        var name = SpaceTheme.Text(panel, "Name", Plain(string.IsNullOrEmpty(g.title) ? T_NO_NAME : g.title), 50,
            SpaceTheme.TEXT, new Vector2(0.5f, 1f), new Vector2(0f, -100f), new Vector2(DETAIL_W - 100f, 120f), FontStyles.Bold);
        name.textWrappingMode = TextWrappingModes.Normal;
        name.enableAutoSizing = true;
        name.fontSizeMin = 30f;
        name.fontSizeMax = 50f;
        if (!string.IsNullOrEmpty(g.title_en))
        {
            var en = SpaceTheme.Text(panel, "En", Plain(g.title_en), 28, SpaceTheme.A(ACCENT, 0.85f), new Vector2(0.5f, 1f),
                new Vector2(0f, -180f), new Vector2(DETAIL_W - 100f, 40f), FontStyles.Bold);
            en.enableAutoSizing = true;
            en.fontSizeMin = 20f;
            en.fontSizeMax = 28f;
        }
        var div = SpaceTheme.Rect("Divider", panel, new Vector2(0.5f, 1f), new Vector2(0f, -214f), new Vector2(DETAIL_W - 140f, 2f));
        SpaceTheme.Img(div, null, SpaceTheme.STROKE);

        // 人数・時間・発売年・デザイナー・人気度・ジャンル
        var stack = SpaceTheme.Rect("Stack", panel);
        stack.anchorMin = Vector2.zero;
        stack.anchorMax = Vector2.one;
        stack.offsetMin = new Vector2(56f, DETAIL_STACK_BOTTOM);
        stack.offsetMax = new Vector2(-56f, -DETAIL_STACK_TOP);
        var vlg = stack.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 20f;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childAlignment = TextAnchor.UpperLeft;

        InfoRow(stack, T_PLAYERS, Plain(string.IsNullOrEmpty(g.players) ? T_DASH : g.players));
        InfoRow(stack, T_TIME, Plain(string.IsNullOrEmpty(g.time) ? T_DASH : g.time));
        if (!string.IsNullOrEmpty(g.year)) InfoRow(stack, T_YEAR, Plain(g.year));
        if (g.designer != null && g.designer.Length > 0)
            InfoRow(stack, T_DESIGNER, Linked(g.designer, "、"), PlainLength(g.designer, "、"),
                i => ApplyTagFilter(true, g.designer[i]));
        if (g.popularity > 0)
            InfoRow(stack, T_POPULAR, $"<color=#{ColorUtility.ToHtmlStringRGB(SpaceTheme.GOLD)}>★</color> {g.popularity}{T_POPULAR_B}");
        if (TryAddedOn(g, out var addedOn))
            InfoRow(stack, T_ADDED, $"{addedOn.Year}年{addedOn.Month}月{addedOn.Day}日" +
                (IsNew(g) ? $"　<color={NEW_HEX}>{T_NEW}</color>" : ""));
        if (g.genre != null && g.genre.Length > 0)
            InfoBlock(stack, T_GENRE, Linked(g.genre, "　/　"), i => ApplyTagFilter(false, g.genre[i]));
        else
            InfoBlock(stack, T_GENRE, T_DASH, null);

        // 情報の量に合わせてカードを縦に伸ばす（最大でも収まらないときは各行が縮み、あふれは省略記号）
        LayoutRebuilder.ForceRebuildLayoutImmediate(stack);
        float needed = 0f;
        for (int i = 0; i < stack.childCount; i++)
        {
            var child = (RectTransform)stack.GetChild(i);
            needed += Mathf.Max(LayoutUtility.GetMinHeight(child), LayoutUtility.GetPreferredHeight(child));
        }
        needed += vlg.spacing * Mathf.Max(0, stack.childCount - 1);
        float panelH = Mathf.Clamp(DETAIL_STACK_TOP + DETAIL_STACK_BOTTOM + needed, DETAIL_BASE_H, DETAIL_MAX_H);
        panel.sizeDelta = new Vector2(DETAIL_W, panelH);

        // 遊んだ／お気に入り（とじるの上に横並び）
        _detailPlayed = BottomPill(panel, "Played", -206f, 140f, 396f, () => TogglePlayed(g));
        _detailFav = BottomPill(panel, "Fav", 206f, 140f, 396f, () => ToggleFavorite(g));
        UpdateDetailMarks();

        BuildPhotoRow(panel);

        var close = BottomPill(panel, "Close", 0f, 24f, 360f, CloseDetail);
        close.Label.text = T_CLOSE;
        close.Label.color = SpaceTheme.TEXT;
        close.Label.fontSizeMax = 36f;

        root.gameObject.AddComponent<SpacePopIn>().Target = panel;
    }

    private static Pill BottomPill(RectTransform panel, string name, float x, float bottom, float w, UnityEngine.Events.UnityAction onClick)
    {
        var p = MakePill(panel, name, 48f, 30f, onClick);
        p.Rt.anchorMin = p.Rt.anchorMax = new Vector2(0.5f, 0f);
        p.Rt.pivot = new Vector2(0.5f, 0f);
        p.Rt.sizeDelta = new Vector2(w, 100f);
        p.Rt.anchoredPosition = new Vector2(x, bottom);
        return p;
    }

    /// <summary>「見出し　値」の1行。値が長いと段階的に文字を小さくし、それでも長ければ折り返して行を高くする</summary>
    private static void InfoRow(RectTransform parent, string label, string value) =>
        InfoRow(parent, label, value, value.Length, null);

    private static void InfoRow(RectTransform parent, string label, string value, int plainLen, Action<int> onLinkTap)
    {
        var t = SpaceTheme.Text(parent, "Row", "", 36, SpaceTheme.TEXT, FontStyles.Bold, TextAlignmentOptions.Left);
        t.gameObject.AddComponent<LayoutElement>().minHeight = 58f;
        int len = label.Length + plainLen;
        string v = len <= 26 ? value : len <= 60 ? $"<size=32>{value}</size>" : $"<size=28>{value}</size>";
        t.richText = true;
        t.text = $"<color={LABEL_HEX}>{label}</color>　{v}";
        t.textWrappingMode = TextWrappingModes.Normal;
        t.overflowMode = TextOverflowModes.Ellipsis;
        t.raycastTarget = onLinkTap != null;
        if (onLinkTap != null) t.gameObject.AddComponent<LinkTap>().OnTap = onLinkTap;
    }

    /// <summary>「見出し」と、折り返す値のかたまり（ジャンル）</summary>
    private static void InfoBlock(RectTransform parent, string label, string value, Action<int> onLinkTap)
    {
        var t = SpaceTheme.Text(parent, "Block", "", 32, SpaceTheme.TEXT, FontStyles.Bold, TextAlignmentOptions.TopLeft);
        var le = t.gameObject.AddComponent<LayoutElement>();
        le.flexibleHeight = 1f;
        le.minHeight = 120f;
        t.richText = true;
        t.text = $"<color={LABEL_HEX}>{label}</color>\n{value}";
        t.textWrappingMode = TextWrappingModes.Normal;
        t.overflowMode = TextOverflowModes.Ellipsis;
        t.raycastTarget = onLinkTap != null;
        if (onLinkTap != null) t.gameObject.AddComponent<LinkTap>().OnTap = onLinkTap;
    }

    /// <summary>それぞれを水色の下線つきリンク（&lt;link="i"&gt;）にしてつなぐ</summary>
    private static string Linked(string[] values, string sep)
    {
        bool underline = SpaceTheme.Safe("_").Length == 1; // 下線の形（'_'）がフォントに無ければ色だけ
        var sb = new StringBuilder();
        for (int i = 0; i < values.Length; i++)
        {
            if (i > 0) sb.Append(sep);
            sb.Append("<link=\"").Append(i).Append("\">");
            if (underline) sb.Append("<u>");
            sb.Append("<color=").Append(LINK_HEX).Append('>').Append(Plain(values[i])).Append("</color>");
            if (underline) sb.Append("</u>");
            sb.Append("</link>");
        }
        return sb.ToString();
    }

    private static int PlainLength(string[] values, string sep)
    {
        int n = sep.Length * Mathf.Max(0, values.Length - 1);
        foreach (var v in values) n += v?.Length ?? 0;
        return n;
    }

    /// <summary>TMP の &lt;link&gt; のタップを拾って、リンクの番号を知らせる</summary>
    private sealed class LinkTap : MonoBehaviour, IPointerClickHandler
    {
        public Action<int> OnTap;

        public void OnPointerClick(PointerEventData e)
        {
            var tmp = GetComponent<TextMeshProUGUI>();
            if (tmp == null || OnTap == null) return;
            var canvas = tmp.canvas;
            var cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            int i = TMP_TextUtilities.FindIntersectingLink(tmp, e.position, cam);
            if (i >= 0 && int.TryParse(tmp.textInfo.linkInfo[i].GetLinkID(), out int idx)) OnTap(idx);
        }
    }

    private void CloseDetail()
    {
        if (_detail != null) Destroy(_detail);
        _detail = null;
        _detailEntry = null;
        _detailPlayed = _detailFav = _photoSort = null;
        _photoContent = null;
        foreach (var t in _detailThumbs) Destroy(t);
        _detailThumbs.Clear();
    }

    // ================================================================
    // 遊んだ記録（写真。端末ローカルのみ）
    // ================================================================
    private void BuildPhotoRow(RectTransform panel)
    {
        var head = SpaceTheme.Rect("PhotoHead", panel);
        head.anchorMin = new Vector2(0f, 0f);
        head.anchorMax = new Vector2(1f, 0f);
        head.offsetMin = new Vector2(56f, 424f);
        head.offsetMax = new Vector2(-56f, 480f);
        var title = SpaceTheme.Text(head, "Title", T_RECORD, 30, SpaceTheme.TEXT_SUB, FontStyles.Bold, TextAlignmentOptions.Left);
        SpaceTheme.Stretch(title.rectTransform);

        // 新しい順⇔古い順（端末に保存。全ゲーム共通）
        _photoSort = MakePill(head, "Sort", 28f, 26f, TogglePhotoSort);
        _photoSort.Rt.anchorMin = new Vector2(1f, 0f);
        _photoSort.Rt.anchorMax = new Vector2(1f, 1f);
        _photoSort.Rt.pivot = new Vector2(1f, 0.5f);
        _photoSort.Rt.offsetMin = new Vector2(-220f, 0f);
        _photoSort.Rt.offsetMax = Vector2.zero;
        UpdatePhotoSortLabel();

        var row = SpaceTheme.Rect("PhotoRow", panel);
        row.anchorMin = new Vector2(0f, 0f);
        row.anchorMax = new Vector2(1f, 0f);
        row.offsetMin = new Vector2(56f, 256f);
        row.offsetMax = new Vector2(-56f, 416f);
        SpaceTheme.HitArea(row);
        row.gameObject.AddComponent<RectMask2D>();
        var sr = row.gameObject.AddComponent<ScrollRect>();
        sr.vertical = false;
        sr.scrollSensitivity = 30f;
        sr.movementType = ScrollRect.MovementType.Clamped;
        sr.viewport = row;

        var content = SpaceTheme.Rect("Content", row);
        content.anchorMin = new Vector2(0f, 0f);
        content.anchorMax = new Vector2(0f, 1f);
        content.pivot = new Vector2(0f, 0.5f);
        content.offsetMin = content.offsetMax = Vector2.zero;
        var hl = content.gameObject.AddComponent<HorizontalLayoutGroup>();
        hl.spacing = 14f;
        hl.childAlignment = TextAnchor.MiddleLeft;
        hl.childForceExpandWidth = false;
        hl.childForceExpandHeight = false;
        hl.childControlWidth = true;
        hl.childControlHeight = true;
        content.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        sr.content = content;
        _photoContent = content;
        RefreshPhotos();
    }

    private void TogglePhotoSort()
    {
        BoardGamePhotoStore.NewestFirst = !BoardGamePhotoStore.NewestFirst;
        UpdatePhotoSortLabel();
        RefreshPhotos();
        _firstPhoto.Clear(); // 一覧の行のサムネイル（1枚目）も新しい並びに合わせる
        foreach (var kv in _active) UpdateRowThumb(kv.Value);
    }

    private void UpdatePhotoSortLabel()
    {
        if (_photoSort == null) return;
        _photoSort.Label.text = BoardGamePhotoStore.NewestFirst ? T_NEWEST : T_OLDEST;
        SetPill(_photoSort, false, ACCENT);
    }

    /// <summary>写真の行（撮影ボタン＋撮った写真）を作り直す</summary>
    private void RefreshPhotos()
    {
        if (_photoContent == null || _detailEntry == null) return;
        foreach (Transform c in _photoContent) Destroy(c.gameObject);
        foreach (var t in _detailThumbs) Destroy(t);
        _detailThumbs.Clear();
        var g = _detailEntry;

        // 撮影ボタン（カメラの絵は画像で描く。絵文字は使わない）
        var cam = SpaceTheme.Rect("Camera", _photoContent);
        var camLe = cam.gameObject.AddComponent<LayoutElement>();
        camLe.preferredWidth = camLe.preferredHeight = 150f;
        var camFace = SpaceTheme.Glass(cam, 30f, PILL_FILL, SpaceTheme.A(ACCENT, 0.45f), true);
        SpaceTheme.Clickable(cam.gameObject, camFace, () => TakePhoto(g));
        var icon = SpaceTheme.Rect("Icon", cam, SpaceTheme.CENTER, new Vector2(0f, 16f), new Vector2(64f, 64f));
        SpaceTheme.Img(icon, SpaceSprites.Camera, ACCENT);
        SpaceTheme.Text(cam, "Label", T_SHOOT, 24, SpaceTheme.TEXT_SUB, new Vector2(0.5f, 0f), new Vector2(0f, 28f),
            new Vector2(140f, 34f), FontStyles.Bold);

        foreach (var p in BoardGamePhotoStore.PhotosOf(g))
        {
            string path = p;
            var cell = SpaceTheme.Rect("Photo", _photoContent);
            var le = cell.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = le.preferredHeight = 150f;
            var bg = SpaceTheme.Rounded(cell, SpaceTheme.A(SpaceTheme.LINE, 0.10f), 30f, true);
            cell.gameObject.AddComponent<Mask>().showMaskGraphic = true; // 角丸に切り抜く（はみ出しは切る）
            SpaceTheme.Clickable(cell.gameObject, bg, () =>
                BoardGamePhotoViewer.Show(transform, path, SpaceTheme.Font, () =>
                {
                    InvalidateRowThumb(g);
                    RefreshPhotos();
                }));

            var tex = BoardGamePhotoStore.LoadThumbTexture(path);
            if (tex == null) continue;
            _detailThumbs.Add(tex);
            var img = SpaceTheme.Rect("Img", cell, SpaceTheme.CENTER, Vector2.zero, Cover(tex, 150f));
            var raw = img.gameObject.AddComponent<RawImage>();
            raw.texture = tex;
            raw.raycastTarget = false;

            var taken = BoardGamePhotoStore.TakenAtOf(path);
            if (taken != DateTime.MinValue)
            {
                var band = SpaceTheme.Rect("Date", cell, new Vector2(0.5f, 1f), new Vector2(0f, -18f), new Vector2(150f, 36f));
                SpaceTheme.Img(band, null, new Color(0.01f, 0.02f, 0.06f, 0.6f));
                SpaceTheme.Text(band, "Label", $"{taken.Month}月{taken.Day}日", 22, SpaceTheme.TEXT, FontStyles.Bold)
                    .rectTransform.sizeDelta = new Vector2(150f, 36f);
            }
        }
    }

    /// <summary>
    /// カメラで撮って、このゲームの「遊んだ記録」として端末に保存する（Firestore には送らない）。
    /// エディタではネイティブカメラが使えないので、ファイルを選んで代わりにする
    /// </summary>
    private void TakePhoto(BoardGameEntry g)
    {
#if UNITY_EDITOR
        string picked = UnityEditor.EditorUtility.OpenFilePanel("テスト用の写真を選択", "", "jpg,jpeg,png");
        if (string.IsNullOrEmpty(picked)) return;
        var editorTex = BoardGamePhotoStore.LoadTexture(picked);
        if (editorTex == null) return;
        BoardGamePhotoStore.AddPhoto(g, editorTex);
        Destroy(editorTex);
        InvalidateRowThumb(g);
        RefreshPhotos();
#else
        NativeCamera.TakePicture(path =>
        {
            if (string.IsNullOrEmpty(path)) return; // 撮影をやめた
            var tex = NativeCamera.LoadImageAtPath(path, 1280, markTextureNonReadable: false);
            if (tex == null)
            {
                Debug.LogError($"[Zukan] 撮った写真を読み込めませんでした: {path}");
                return;
            }
            BoardGamePhotoStore.AddPhoto(g, tex);
            Destroy(tex);
            if (this == null) return;
            InvalidateRowThumb(g);
            RefreshPhotos();
        }, maxSize: 1280);
#endif
    }

    // ---------------------------------------------------------------- 行のサムネイル
    private void UpdateRowThumb(RowView row)
    {
        if (row.Entry == null) return;
        Texture2D tex = null;
        string path = FirstPhotoOf(row.Entry);
        if (path != null) tex = CachedThumb(path);
        bool has = tex != null;
        row.ThumbGo.SetActive(has);
        if (has)
        {
            row.ThumbRaw.texture = tex;
            row.ThumbImgRt.sizeDelta = Cover(tex, THUMB);
        }
        // 写真がある行は文字をサムネイルの左まで、無い行は ✓★ の左まで使う
        float right = has ? -(32f + MARK_SIZE + 20f + THUMB + 20f) : -(32f + MARK_SIZE + 24f);
        foreach (var t in new[] { row.Title, row.Meta, row.Genre })
            t.rectTransform.offsetMax = new Vector2(right, t.rectTransform.offsetMax.y);
    }

    private string FirstPhotoOf(BoardGameEntry g)
    {
        if (!_firstPhoto.TryGetValue(g, out var path))
        {
            var photos = BoardGamePhotoStore.PhotosOf(g);
            path = photos.Count > 0 ? photos[0] : null;
            _firstPhoto[g] = path;
        }
        return path;
    }

    private Texture2D CachedThumb(string path)
    {
        if (_thumbCache.TryGetValue(path, out var tex))
        {
            _thumbOrder.Remove(path);
            _thumbOrder.Add(path);
            return tex;
        }
        tex = BoardGamePhotoStore.LoadThumbTexture(path);
        if (tex == null) return null;
        _thumbCache[path] = tex;
        _thumbOrder.Add(path);
        // 上限を超えたら古い順に捨てる（表示中の行が使っているものは残す）
        int i = 0;
        while (_thumbCache.Count > THUMB_CACHE_MAX && i < _thumbOrder.Count)
        {
            string cand = _thumbOrder[i];
            var t = _thumbCache[cand];
            bool inUse = false;
            foreach (var kv in _active)
                if (kv.Value.ThumbRaw.texture == t) { inUse = true; break; }
            if (inUse) { i++; continue; }
            Destroy(t);
            _thumbCache.Remove(cand);
            _thumbOrder.RemoveAt(i);
        }
        return tex;
    }

    /// <summary>写真を足した・消したあと、このゲームの行のサムネイルを取り直す</summary>
    private void InvalidateRowThumb(BoardGameEntry g)
    {
        if (_firstPhoto.TryGetValue(g, out var old) && old != null && _thumbCache.TryGetValue(old, out var tex))
        {
            _thumbCache.Remove(old);
            _thumbOrder.Remove(old);
            Destroy(tex);
        }
        _firstPhoto.Remove(g);
        foreach (var kv in _active)
            if (kv.Value.Entry == g) UpdateRowThumb(kv.Value);
    }

    /// <summary>正方形の枠いっぱいに収める大きさ（はみ出した分は切る）</summary>
    private static Vector2 Cover(Texture2D tex, float side)
    {
        float aspect = (float)tex.width / Mathf.Max(1, tex.height);
        return aspect >= 1f ? new Vector2(side * aspect, side) : new Vector2(side, side / aspect);
    }

    // ================================================================
    // 部品
    // ================================================================
    /// <summary>セーフエリアの上端から top の位置に、左右いっぱいの帯を作る</summary>
    private RectTransform TopBar(string name, float top, float height)
    {
        var rt = SpaceTheme.Rect(name, _safe);
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.offsetMin = new Vector2(SIDE, -(top + height));
        rt.offsetMax = new Vector2(-SIDE, -top);
        return rt;
    }

    /// <summary>帯の中を横に分ける（a0〜a1 の割合。間に12のすき間）</summary>
    private static void Split(RectTransform rt, float a0, float a1)
    {
        rt.anchorMin = new Vector2(a0, 0f);
        rt.anchorMax = new Vector2(a1, 1f);
        rt.offsetMin = new Vector2(a0 > 0f ? 6f : 0f, 0f);
        rt.offsetMax = new Vector2(a1 < 1f ? -6f : 0f, 0f);
    }

    /// <summary>角丸の札（面＋縁＋文字）。選ぶと色がつく（SetPill）</summary>
    private static Pill MakePill(RectTransform parent, string name, float radius, float fontSize,
        UnityEngine.Events.UnityAction onClick)
    {
        var rt = SpaceTheme.Rect(name, parent);
        var face = SpaceTheme.Rounded(rt, PILL_FILL, radius, true);
        var edgeRt = SpaceTheme.Rect("Edge", rt);
        SpaceTheme.Stretch(edgeRt);
        var edge = SpaceTheme.RoundedStroke(edgeRt, SpaceTheme.STROKE, radius);
        var label = SpaceTheme.Text(rt, "Label", "", fontSize, SpaceTheme.TEXT_SUB, FontStyles.Bold);
        SpaceTheme.Stretch(label.rectTransform, 16f, 0f, 16f, 0f);
        label.enableAutoSizing = true;
        label.fontSizeMin = 18f;
        label.fontSizeMax = fontSize;
        SpaceTheme.Clickable(rt.gameObject, face, onClick);
        return new Pill { Rt = rt, Face = face, Edge = edge, Label = label };
    }

    private static void SetPill(Pill p, bool on, Color accent)
    {
        p.Face.color = on ? SpaceTheme.A(accent, 0.16f) : PILL_FILL;
        p.Edge.color = on ? SpaceTheme.A(accent, 0.75f) : SpaceTheme.STROKE;
        p.Label.color = on ? accent : SpaceTheme.TEXT_SUB;
    }

    private static void CloseButton(RectTransform safe, float y, UnityEngine.Events.UnityAction onClick)
    {
        var rt = SpaceTheme.Rect("Close", safe, new Vector2(1f, 1f), new Vector2(-(SIDE + 56f), y), new Vector2(104f, 104f));
        var face = SpaceTheme.Img(rt, SpaceSprites.Circle, new Color(0.05f, 0.07f, 0.19f, 0.85f), true);
        var rim = SpaceTheme.Rect("Rim", rt);
        SpaceTheme.Stretch(rim);
        SpaceTheme.Img(rim, SpaceSprites.RingSmall, SpaceTheme.STROKE);
        SpaceTheme.Bar(rt, Vector2.zero, 44f, 6f, 45f, SpaceTheme.TEXT);
        SpaceTheme.Bar(rt, Vector2.zero, 44f, 6f, -45f, SpaceTheme.TEXT);
        SpaceTheme.Clickable(rt.gameObject, face, onClick);
    }

    /// <summary>ゲームの文字をそのまま出す（フォントに無い文字を除き、&lt; などを書式にしない）</summary>
    private static string Plain(string s) => SpaceTheme.NoParse(SpaceTheme.Safe(s ?? ""));
}
