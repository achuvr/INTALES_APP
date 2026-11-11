using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// さいころ座のダイス（2026-11-11 ユーザー指定「サイコロ座を解放すると、様々なダイスを振れる機能が追加される。
/// サイコロ座をタップすると 1D4 や 1D6 など、様々なダイスを数を選んで振れる」）。
/// さいころ座が完成した人だけホームの空にさいころ座が出て（SpaceBackground）、それをタップすると開く全画面。
///  ・ダイス: D4 / D6 / D8 / D10 / D12 / D20 / D100 の7種類
///  ・個数: 1〜10（D100 は十の位と一の位の2個で1回なので 1〜5）
///  ・「振る」かトレイをタップすると、宇宙のトレイに3Dのダイスを投げ込み（Dice3DSimulator。旧ホームの戦闘のダイスと同じもの）、
///    止まった出目と合計を出す。いちばん大きい目は金色、全部がいちばん大きい目なら「すべて最大！」
/// 結果はこの端末の中だけ（記録・通信なし）。最後に選んだ種類と個数は覚えておく（PlayerPrefs）。
/// 上のタブで「ダイスチャレンジ」（2026-11-11 ユーザー指定。DiceChallenge）に切り替えられる:
///  ・1日1回だけ 1D100 を振って、その日のランキング（名前は出さず「ユーザーA」など・上位10位）に載る。毎朝4時にリセット
///  ・振れるのは入店中だけ。1日1回・入店中だけは管理者も同じ（2026-11-11 ユーザー指定）
///  ・その日いちばん大きい目の人（同じ目なら全員）は、次にアプリを開いたとき5%OFFチケットを受け取る
///  ・出目は「振る」を押したときに決まる（3Dのダイスは数字を隠して転がし、止まってからその出目を見せる）
/// 3Dのトレイは開いている間だけ作り、閉じたら消す。Android の戻るは NewHomeController が Instance.HandleBack を呼ぶ。
/// </summary>
public class SpaceDiceRoller : MonoBehaviour
{
    /// <summary>この画面を開ける星座（さいころ座）の ID</summary>
    public const string CONSTELLATION = "dice";

    private const string T_CAPTION = "DICE";
    private const string T_TITLE = "ダイス";
    private const string T_COUNT = "個数";
    private const string T_UNIT = " 個";
    private const string T_TOTAL = " の合計";
    private const string T_ROLL = " を振る";
    private const string T_ROLLING = "ころころ…";
    private const string T_HINT = "「振る」を押すと、ダイスが転がります";
    private const string T_MAX = "最大！";
    private const string T_ALL_MAX = "すべて最大！";
    private const string T_NONE = "—";
    // ダイスチャレンジ（2026-11-11）
    private const string T_TAB_FREE = "フリー";
    private const string T_TAB_CHALLENGE = "ダイスチャレンジ";
    private const string T_RANK_TITLE = "今日のランキング";
    private const string T_RANK_NOTE_A = "いちばん大きい目の人（同じ目なら全員）に";
    private const string T_RANK_NOTE_B = " ・ 毎朝4時にリセット";
    private const string T_RANK_EMPTY = "まだだれも振っていません";
    private const string T_RANK_LOADING = "読み込み中…";
    private const string T_PEOPLE = "人";
    private const string T_RANK_UNIT = "位";
    private const string T_YOU = "あなた";
    private const string T_MY_LINE = "あなたの今日の出目";
    private const string T_MY_NONE = "まだ振っていません";
    private const string T_MY_PENDING = "まだ送れていません";
    private const string T_CH_ROLL = "今日の 1D100 を振る（1日1回）";
    private const string T_CH_SEND = "結果を送る";
    private const string T_CH_SENDING = "送っています…";
    private const string T_CH_DONE = "今日はもう振りました";
    private const string T_CH_HINT = "1日1回の 1D100 で、今日のいちばんを目指そう";
    private const string T_CH_TODAY = "今日の出目";
    private const string T_CH_FAILED = "結果を送れませんでした。通信状況を確かめて「結果を送る」を押してください";
    private const string T_CH_OTHER = "今日はほかの端末で振っています";
    private const string T_CH_NOT_IN = "入店中に振れます";
    private const string T_CH_HINT_OUT = "入店中に、1日1回の 1D100 を振れます";

    /// <summary>この画面で表示しうる文字（NewHome の起動時にまとめてフォントへ登録する）</summary>
    public static string GlyphText => new StringBuilder()
        .Append(T_CAPTION).Append(T_TITLE).Append(T_COUNT).Append(T_UNIT).Append(T_TOTAL).Append(T_ROLL)
        .Append(T_ROLLING).Append(T_HINT).Append(T_MAX).Append(T_ALL_MAX).Append(T_NONE).Append("D0123456789.")
        .Append(T_TAB_FREE).Append(T_TAB_CHALLENGE).Append(T_RANK_TITLE).Append(T_RANK_NOTE_A).Append(T_RANK_NOTE_B)
        .Append(T_RANK_EMPTY).Append(T_RANK_LOADING).Append(T_PEOPLE).Append(T_RANK_UNIT).Append(T_YOU).Append(T_MY_LINE)
        .Append(T_MY_NONE).Append(T_MY_PENDING).Append(T_CH_ROLL).Append(T_CH_SEND).Append(T_CH_SENDING).Append(T_CH_DONE)
        .Append(T_CH_HINT).Append(T_CH_TODAY).Append(T_CH_FAILED).Append(T_CH_OTHER).Append(T_CH_NOT_IN)
        .Append(T_CH_HINT_OUT).Append("月日（）・/★")
        .Append(DiceChallenge.GlyphText)
        .ToString();

    private const string PREF_KIND = "space_dice_kind";
    private const string PREF_COUNT = "space_dice_count";
    private const int MAX_COUNT = 10;
    private const int MAX_COUNT_D100 = 5; // D100 は1回に2個（十の位・一の位）投げるので、トレイに入れるのは5回分まで

    private readonly struct Kind
    {
        public readonly int Sides;
        public readonly string Label;
        public readonly Color Accent;

        public Kind(int sides, Color accent)
        {
            Sides = sides;
            Label = "D" + sides;
            Accent = accent;
        }
    }

    // ダイスの種類ごとの色（ボタン・3Dのダイス・出目の札）。金色はいちばん大きい目の印に使うので、ここでは使わない
    private static readonly Kind[] KINDS =
    {
        new Kind(4, SpaceTheme.Hex(0xFF7AD9)),
        new Kind(6, SpaceTheme.Hex(0x5CE1FF)),
        new Kind(8, SpaceTheme.Hex(0x5CFFB8)),
        new Kind(10, SpaceTheme.Hex(0x7FA6FF)),
        new Kind(12, SpaceTheme.Hex(0xB79BFF)),
        new Kind(20, SpaceTheme.Hex(0xFF9E6B)),
        new Kind(100, SpaceTheme.Hex(0xDDE6FF)),
    };

    // 配置（セーフエリアの中）。上は見出しとタブ。
    // フリー: 下から 振るボタン → 個数 → 種類 → 結果 の順に積み、残りの高さにトレイ
    // チャレンジ: 下から 振るボタン → ランキング の順に積み、残りの高さにトレイ
    private const float SIDE = 40f;
    private const float TABS_TOP = 196f, TABS_H = 84f;
    private const float HEADER = TABS_TOP + TABS_H + 16f; // ここより下に中身を置く
    private const float BOTTOM = 56f, GAP = 22f;
    private const float ROLL_H = 140f, COUNT_H = 112f, KIND_H = 150f, RESULT_H = 220f;
    private const float FREE_TOP = BOTTOM + ROLL_H + GAP + COUNT_H + GAP + KIND_H + GAP + RESULT_H + GAP;
    private const float ROW_H = 50f, MY_H = 64f;
    private const float RANK_H = 112f + DiceChallenge.TOP * ROW_H + 10f + 2f + 10f + MY_H + 18f;
    private const float CHALLENGE_TOP = BOTTOM + ROLL_H + GAP + RANK_H + GAP;
    private const float MAX_W = 1000f;
    private const float RESULT_LEFT = 170f; // 2個以上のとき、合計を出す左の列の中心

    private static readonly Color DIE_LABEL = new Color(0.04f, 0.06f, 0.17f);  // 3Dのダイスの数字（濃い紺）
    private static readonly Color TRAY_BG = new Color(0.01f, 0.015f, 0.045f);  // トレイの外（カメラの背景）
    private static readonly Color CHIP_OFF = new Color(0.06f, 0.08f, 0.22f, 0.78f);

    /// <summary>開いている画面（なければ null）</summary>
    public static SpaceDiceRoller Instance { get; private set; }

    private class KindChip
    {
        public Image Face, Stroke;
        public RectTransform Icon;
        public TextMeshProUGUI Label;
    }

    private CanvasGroup _group;
    private RectTransform _safe;
    private float _cw;
    private Dice3DSimulator _sim;
    private Texture2D _floorTex;
    private RawImage _view;
    private GameObject _hint;
    private int _kind, _count;
    private bool _rolling, _closing, _hasResult;

    private readonly List<KindChip> _kindChips = new List<KindChip>();
    private CanvasGroup _kindGroup, _countGroup, _rollGroup, _minusGroup, _plusGroup;
    private TextMeshProUGUI _countValue, _rollLabel;
    private RectTransform _result, _chips;
    private TextMeshProUGUI _totalLabel, _totalValue, _badge;

    // フリー／ダイスチャレンジの切り替え（2026-11-11）
    private enum Mode { Free, Challenge }
    private Mode _mode;
    private float _safeH;
    private RectTransform _tray, _freeRoot, _challengeRoot;
    private TextMeshProUGUI _hintText;
    private bool _trayHasDice; // このモードで振ったダイスがトレイにある
    private Image _tabFreeFace, _tabChallengeFace;
    private TextMeshProUGUI _tabFreeLabel, _tabChallengeLabel;
    private GameObject _tabDot;
    // ダイスチャレンジ
    private bool _sending;
    private RectTransform _rankRows;
    private TextMeshProUGUI _rankInfo, _rankEmpty, _myValue;
    private CanvasGroup _chRollGroup;
    private TextMeshProUGUI _chRollLabel;
    private GameObject _trayInfo;
    private TextMeshProUGUI _trayInfoValue, _trayInfoSub;
    private float _nextTick;
    private bool _wasInStore; // 入店・退店で振るボタンを切り替える

    public static SpaceDiceRoller Open(Canvas canvas = null)
    {
        if (Instance != null) return Instance;
        // さいころ座が完成していなければ開かない（入口の空のさいころ座も完成するまで出ない）。
        // 管理者だけは初めから使える（2026-11-11 ユーザー指定）
        var p = Constellations.Progress().Find(x => x.Def.Id == CONSTELLATION);
        if (p == null || (!p.Complete && !AdminClaim.IsAdmin)) return null;
        if (canvas == null) canvas = SpaceTheme.MainCanvas();
        if (canvas == null) return null;
        var root = SpaceTheme.Rect("__SpaceDice", canvas.transform);
        SpaceTheme.Stretch(root);
        root.SetAsLastSibling();
        SpaceTheme.SubCanvas(root.gameObject, true); // 出目の札の動きをホームの再描画と分ける
        var page = root.gameObject.AddComponent<SpaceDiceRoller>();
        Instance = page;
        page.Build(canvas);
        return page;
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
        float from = _group.alpha;
        yield return SpaceTheme.Tween(0.2f, 0f, k => _group.alpha = from * (1f - k));
        Destroy(gameObject);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        DiceChallenge.Changed -= OnBoardChanged;
        DiceChallenge.StopListening(); // ランキングの購読はこの画面を開いている間だけ
        if (_sim != null) Destroy(_sim.gameObject); // 3Dのトレイ（カメラ・映像・ダイス）ごと消す
        if (_floorTex != null) Destroy(_floorTex);
    }

    private void Update()
    {
        if (_closing || Time.unscaledTime < _nextTick) return;
        _nextTick = Time.unscaledTime + 1f;
        // 朝4時をまたいだら、新しい日のランキングに切り替える
        if (_mode == Mode.Challenge && DiceChallenge.ListeningDay != DiceChallenge.Today)
        {
            DiceChallenge.StartListening();
            RefreshChallenge();
        }
        // 入店中だけ振れるので、入店・退店（営業終了での自動の退店も）が変わったら振るボタンを切り替える
        bool inStore = DiceChallenge.InStore;
        if (inStore != _wasInStore)
        {
            _wasInStore = inStore;
            if (_mode == Mode.Challenge) RefreshChallenge();
        }
        RefreshTabs();
    }

    private int MaxCount => KINDS[_kind].Sides == 100 ? MAX_COUNT_D100 : MAX_COUNT;

    private static string Notation(Kind kind, int count) => count + kind.Label;

    /// <summary>結果の見出し（1個なら「1D20」、2個以上なら「3D6 の合計」）</summary>
    private static string TotalLabel(Kind kind, int count) => count == 1 ? Notation(kind, 1) : Notation(kind, count) + T_TOTAL;

    // ================================================================
    // 組み立て
    // ================================================================
    private void Build(Canvas canvas)
    {
        var rt = (RectTransform)transform;
        _group = gameObject.AddComponent<CanvasGroup>();
        _group.alpha = 0f;

        _kind = Mathf.Clamp(PlayerPrefs.GetInt(PREF_KIND, 1), 0, KINDS.Length - 1); // はじめは D6
        _count = Mathf.Clamp(PlayerPrefs.GetInt(PREF_COUNT, 1), 1, MaxCount);

        var size = SpaceTheme.CanvasSize(canvas);
        var insets = SpaceTheme.SafeInsets(size);
        _safeH = size.y - insets.top - insets.bottom;
        _cw = Mathf.Min(size.x - SIDE * 2f, MAX_W);

        // 背景（下のホームを押させない）
        var dim = SpaceTheme.Rect("Dim", rt);
        SpaceTheme.Stretch(dim);
        SpaceTheme.Img(dim, null, new Color(0.005f, 0.008f, 0.035f, 0.98f), true);
        var glow = SpaceTheme.Rect("Glow", rt, new Vector2(0.5f, 1f), new Vector2(0f, -260f), new Vector2(1600f, 1200f));
        SpaceTheme.Img(glow, SpaceSprites.Glow, SpaceTheme.A(SpaceTheme.VIOLET, 0.10f));
        Sky(rt);

        _safe = SpaceTheme.Rect("Safe", rt);
        SpaceTheme.Stretch(_safe);
        _safe.gameObject.AddComponent<SpaceSafeArea>();

        BuildHeader();
        BuildTabs();

        // フリー（いろいろなダイス）
        _freeRoot = SpaceTheme.Rect("Free", _safe);
        SpaceTheme.Stretch(_freeRoot);
        float y = BOTTOM;
        BuildRollButton(y);
        y += ROLL_H + GAP;
        BuildCountRow(y);
        y += COUNT_H + GAP;
        BuildKindRow(y);
        y += KIND_H + GAP;
        BuildResult(y);

        // ダイスチャレンジ
        BuildChallenge();
        BuildTray();

        DiceChallenge.Changed += OnBoardChanged;
        _wasInStore = DiceChallenge.InStore;
        RefreshKind();
        SetMode(Mode.Free, true);
        StartCoroutine(SpaceTheme.Tween(0.25f, 0f, k => _group.alpha = k));
    }

    private void BuildHeader()
    {
        var caption = SpaceTheme.Text(_safe, "Caption", T_CAPTION, 24, SpaceTheme.A(SpaceTheme.GOLD, 0.9f),
            new Vector2(0f, 1f), new Vector2(SIDE + 16f, -70f), new Vector2(400f, 36f), FontStyles.Bold,
            TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
        SpaceTheme.Tracking(caption, 18f);
        SpaceTheme.Text(_safe, "Title", T_TITLE, 52, SpaceTheme.TEXT, new Vector2(0f, 1f), new Vector2(SIDE + 16f, -126f),
            new Vector2(400f, 66f), FontStyles.Bold, TextAlignmentOptions.Left, new Vector2(0f, 0.5f));

        // 題の右に、この画面をくれたさいころ座（いまのレベルの姿）を小さく
        var p = Constellations.Progress().Find(x => x.Def.Id == CONSTELLATION);
        if (p != null)
        {
            var fig = SpaceTheme.Rect("Constellation", _safe, new Vector2(0f, 1f), new Vector2(SIDE + 16f + 250f, -104f),
                new Vector2(120f, 120f));
            SpaceConstellationPage.Figure(fig, p.Def, Mathf.Max(p.Def.Size, p.Count - p.Partial), 120f);
        }

        // 右上の閉じる
        var close = SpaceTheme.Rect("Close", _safe, new Vector2(1f, 1f), new Vector2(-(SIDE + 52f), -84f),
            new Vector2(104f, 104f));
        var face = SpaceTheme.Img(close, SpaceSprites.Circle, new Color(0.05f, 0.07f, 0.19f, 0.85f), true);
        var rim = SpaceTheme.Rect("Rim", close);
        SpaceTheme.Stretch(rim);
        SpaceTheme.Img(rim, SpaceSprites.RingSmall, SpaceTheme.STROKE);
        SpaceTheme.Bar(close, Vector2.zero, 44f, 6f, 45f, SpaceTheme.TEXT);
        SpaceTheme.Bar(close, Vector2.zero, 44f, 6f, -45f, SpaceTheme.TEXT);
        SpaceTheme.Clickable(close.gameObject, face, Close);
    }

    /// <summary>上のタブ（フリー／ダイスチャレンジ）。チャレンジがまだの日は、チャレンジの右上に金色の点</summary>
    private void BuildTabs()
    {
        var row = SpaceTheme.Rect("Tabs", _safe, new Vector2(0.5f, 1f), new Vector2(0f, -(TABS_TOP + TABS_H * 0.5f)),
            new Vector2(_cw, TABS_H));
        SpaceTheme.Glass(row, 42f, new Color(0.05f, 0.07f, 0.20f, 0.72f));
        float w = (_cw - 16f) * 0.5f;
        (_tabFreeFace, _tabFreeLabel) = Tab(row, 0, w, T_TAB_FREE, () => SetMode(Mode.Free));
        (_tabChallengeFace, _tabChallengeLabel) = Tab(row, 1, w, T_TAB_CHALLENGE, () => SetMode(Mode.Challenge));
        var dot = SpaceTheme.Rect("Dot", _tabChallengeFace.rectTransform, new Vector2(1f, 1f), new Vector2(-26f, -18f),
            new Vector2(18f, 18f));
        SpaceTheme.Img(dot, SpaceSprites.Circle, SpaceTheme.GOLD);
        _tabDot = dot.gameObject;
    }

    private static (Image, TextMeshProUGUI) Tab(RectTransform row, int index, float w, string label,
        UnityEngine.Events.UnityAction onClick)
    {
        var rt = SpaceTheme.Rect("Tab_" + index, row, new Vector2(0f, 0.5f), new Vector2(8f + w * 0.5f + index * w, 0f),
            new Vector2(w - 8f, TABS_H - 16f));
        var face = SpaceTheme.Rounded(rt, new Color(0f, 0f, 0f, 0f), 34f, true);
        var text = SpaceTheme.Text(rt, "Label", label, 30, SpaceTheme.TEXT_SUB, FontStyles.Bold);
        SpaceTheme.Stretch(text.rectTransform, 12f, 0f, 12f, 0f);
        text.enableAutoSizing = true;
        text.fontSizeMin = 20f;
        text.fontSizeMax = 30f;
        SpaceTheme.Clickable(rt.gameObject, face, onClick, false);
        return (face, text);
    }

    private void RefreshTabs()
    {
        if (_tabFreeFace == null) return;
        bool challenge = _mode == Mode.Challenge;
        _tabFreeFace.color = challenge ? new Color(0f, 0f, 0f, 0f) : SpaceTheme.A(SpaceTheme.CYAN, 0.18f);
        _tabChallengeFace.color = challenge ? SpaceTheme.A(SpaceTheme.GOLD, 0.18f) : new Color(0f, 0f, 0f, 0f);
        _tabFreeLabel.color = challenge ? SpaceTheme.TEXT_SUB : SpaceTheme.TEXT;
        _tabChallengeLabel.color = challenge ? SpaceTheme.GOLD : SpaceTheme.TEXT_SUB;
        bool fresh = DiceChallenge.CanRoll; // 入店中で、今日まだ振っていない
        if (_tabDot.activeSelf != fresh) _tabDot.SetActive(fresh);
    }

    /// <summary>フリーとダイスチャレンジを切り替える（トレイは空にして、それぞれの大きさに合わせる）</summary>
    private void SetMode(Mode mode, bool force = false)
    {
        if (!force && (mode == _mode || _rolling || _sending)) return;
        _mode = mode;
        _freeRoot.gameObject.SetActive(mode == Mode.Free);
        _challengeRoot.gameObject.SetActive(mode == Mode.Challenge);
        if (_sim != null) _sim.ClearDice();
        _trayHasDice = false;
        _hasResult = false;
        LayoutTray();
        if (mode == Mode.Challenge)
        {
            DiceChallenge.StartListening();
            RefreshChallenge();
            if (DiceChallenge.PendingValue > 0 && !DiceChallenge.RolledToday) SendPendingAsync().Forget(); // 途中で閉じた日の続き
        }
        else
        {
            DiceChallenge.StopListening();
            RefreshControls();
        }
        RefreshTray();
        RefreshTabs();
    }

    /// <summary>トレイを、いまのモードの下の部品の上に残る高さいっぱいの正方形にする</summary>
    private void LayoutTray()
    {
        float bottom = _mode == Mode.Free ? FREE_TOP : CHALLENGE_TOP;
        float avail = Mathf.Max(240f, _safeH - HEADER - bottom);
        float s = Mathf.Min(_cw, avail);
        _tray.anchoredPosition = new Vector2(0f, bottom + avail * 0.5f);
        _tray.sizeDelta = new Vector2(s, s);
    }

    /// <summary>トレイの上の案内（まだ振っていないとき）と、チャレンジの今日の出目（もう振ってトレイが空のとき）</summary>
    private void RefreshTray()
    {
        if (_tray == null) return;
        bool challenge = _mode == Mode.Challenge;
        int today = DiceChallenge.TodayValue;
        bool showInfo = challenge && today > 0 && !_trayHasDice && !_rolling;
        bool showHint = !_trayHasDice && !_rolling && !showInfo;
        _hintText.text = !challenge ? T_HINT : DiceChallenge.InStore ? T_CH_HINT : T_CH_HINT_OUT;
        _hint.SetActive(showHint);
        _trayInfo.SetActive(showInfo);
        if (showInfo)
        {
            _trayInfoValue.text = today.ToString();
            var board = DiceChallenge.Current;
            bool ok = board != null && board.Day == DiceChallenge.Today && board.Mine != null;
            _trayInfoSub.text = ok ? $"{board.Mine.Rank}{T_RANK_UNIT} / {board.Count}{T_PEOPLE}" : "";
        }
    }

    /// <summary>トレイ（3Dのダイスの映像）。角を丸く切り抜き、フリーのときは押しても振れる</summary>
    private void BuildTray()
    {
        var tray = SpaceTheme.Rect("Tray", _safe, new Vector2(0.5f, 0f), Vector2.zero, new Vector2(_cw, _cw));
        _tray = tray;
        var glow = SpaceTheme.Rect("Glow", tray);
        SpaceTheme.Stretch(glow, -140f, -140f, -140f, -140f);
        SpaceTheme.Img(glow, SpaceSprites.Glow, SpaceTheme.A(SpaceTheme.CYAN, 0.08f));
        var maskRt = SpaceTheme.Rect("Mask", tray);
        SpaceTheme.Stretch(maskRt);
        var maskImg = SpaceTheme.Rounded(maskRt, Color.white, 48f, true);
        maskRt.gameObject.AddComponent<Mask>().showMaskGraphic = false;
        var view = SpaceTheme.Rect("View", maskRt);
        SpaceTheme.Stretch(view);
        _view = view.gameObject.AddComponent<RawImage>();
        _view.raycastTarget = false;
        var stroke = SpaceTheme.Rect("Stroke", tray);
        SpaceTheme.Stretch(stroke);
        SpaceTheme.RoundedStroke(stroke, SpaceTheme.A(SpaceTheme.LINE, 0.35f), 48f);
        _hintText = SpaceTheme.Text(tray, "Hint", T_HINT, 30, SpaceTheme.TEXT_SUB, FontStyles.Bold);
        SpaceTheme.Stretch(_hintText.rectTransform, 40f, 0f, 40f, 0f);
        _hintText.enableAutoSizing = true;
        _hintText.fontSizeMin = 20f;
        _hintText.fontSizeMax = 30f;
        _hint = _hintText.gameObject;
        // チャレンジでもう振った日: 今日の出目を大きく
        var info = SpaceTheme.Rect("TodayInfo", tray);
        SpaceTheme.Stretch(info);
        _trayInfo = info.gameObject;
        SpaceTheme.Text(info, "Label", T_CH_TODAY, 30, SpaceTheme.TEXT_SUB, SpaceTheme.CENTER, new Vector2(0f, 110f),
            new Vector2(600f, 44f), FontStyles.Bold);
        _trayInfoValue = SpaceTheme.Text(info, "Value", "", 200, SpaceTheme.GOLD, SpaceTheme.CENTER, new Vector2(0f, -4f),
            new Vector2(600f, 230f), FontStyles.Bold);
        SpaceTextScaler.Exclude(_trayInfoValue);
        _trayInfoSub = SpaceTheme.Text(info, "Sub", "", 32, SpaceTheme.TEXT_SUB, SpaceTheme.CENTER, new Vector2(0f, -132f),
            new Vector2(600f, 48f), FontStyles.Bold);
        _trayInfo.SetActive(false);
        SpaceTheme.Clickable(maskRt.gameObject, maskImg, OnTrayPressed, false);

        // 3Dのトレイ（床は宇宙の模様・ダイスがたくさん入っても全部見えるよう少し引いたカメラ）
        _floorTex = MakeFloorTexture();
        _sim = Dice3DSimulator.Create(SpaceTheme.Font, 1024, 4);
        _sim.SetTray(Color.white, TRAY_BG, _floorTex);
        _sim.FrameWholeTray();
        _sim.SetVisible(true);
        _view.texture = _sim.Texture;
    }

    /// <summary>結果（合計と、2個以上なら1個ずつの出目の札）</summary>
    private void BuildResult(float y)
    {
        _result = SpaceTheme.Rect("Result", _freeRoot, new Vector2(0.5f, 0f), new Vector2(0f, y), new Vector2(_cw, RESULT_H),
            new Vector2(0.5f, 0f));
        SpaceTheme.Glass(_result, 40f, new Color(0.05f, 0.07f, 0.20f, 0.72f));
        _totalLabel = SpaceTheme.Text(_result, "TotalLabel", "", 24, SpaceTheme.TEXT_MUTED, new Vector2(0f, 0.5f),
            new Vector2(RESULT_LEFT, 66f), new Vector2(300f, 36f), FontStyles.Bold);
        _totalValue = SpaceTheme.Text(_result, "Total", T_NONE, 96, SpaceTheme.GOLD, new Vector2(0f, 0.5f),
            new Vector2(RESULT_LEFT, -8f), new Vector2(300f, 120f), FontStyles.Bold);
        SpaceTextScaler.Exclude(_totalValue);
        _badge = SpaceTheme.Text(_result, "Badge", "", 24, SpaceTheme.GOLD, new Vector2(0f, 0.5f),
            new Vector2(RESULT_LEFT, -80f), new Vector2(300f, 34f), FontStyles.Bold);
        _chips = SpaceTheme.Rect("Chips", _result);
        _chips.anchorMin = Vector2.zero;
        _chips.anchorMax = Vector2.one;
        _chips.pivot = SpaceTheme.CENTER;
        _chips.offsetMin = new Vector2(RESULT_LEFT * 2f, 14f);
        _chips.offsetMax = new Vector2(-28f, -14f);
    }

    /// <summary>ダイスの種類（7つを1列に）</summary>
    private void BuildKindRow(float y)
    {
        var row = SpaceTheme.Rect("Kinds", _freeRoot, new Vector2(0.5f, 0f), new Vector2(0f, y), new Vector2(_cw, KIND_H),
            new Vector2(0.5f, 0f));
        _kindGroup = row.gameObject.AddComponent<CanvasGroup>();
        const float gap = 12f;
        float w = (_cw - gap * (KINDS.Length - 1)) / KINDS.Length;
        for (int i = 0; i < KINDS.Length; i++)
        {
            int index = i;
            var chip = SpaceTheme.Rect("Kind_" + KINDS[i].Label, row, new Vector2(0f, 0.5f),
                new Vector2(w * 0.5f + i * (w + gap), 0f), new Vector2(w, KIND_H));
            var face = SpaceTheme.Glass(chip, 32f, CHIP_OFF, null, true);
            var stroke = chip.Find("__Stroke");
            var icon = SpaceTheme.Rect("Icon", chip, SpaceTheme.CENTER, new Vector2(0f, 22f), new Vector2(66f, 66f));
            var label = SpaceTheme.Text(chip, "Label", KINDS[i].Label, 30, SpaceTheme.TEXT_SUB, SpaceTheme.CENTER,
                new Vector2(0f, -46f), new Vector2(w - 8f, 40f), FontStyles.Bold);
            label.enableAutoSizing = true;
            label.fontSizeMin = 20f;
            label.fontSizeMax = 30f;
            SpaceTheme.Clickable(chip.gameObject, face, () => SelectKind(index));
            _kindChips.Add(new KindChip
            {
                Face = face, Stroke = stroke != null ? stroke.GetComponent<Image>() : null, Icon = icon, Label = label,
            });
        }
    }

    /// <summary>個数（−・数・＋）</summary>
    private void BuildCountRow(float y)
    {
        var row = SpaceTheme.Rect("Count", _freeRoot, new Vector2(0.5f, 0f), new Vector2(0f, y), new Vector2(_cw, COUNT_H),
            new Vector2(0.5f, 0f));
        _countGroup = row.gameObject.AddComponent<CanvasGroup>();
        SpaceTheme.Glass(row, 40f, new Color(0.05f, 0.07f, 0.20f, 0.72f));
        SpaceTheme.Text(row, "Label", T_COUNT, 32, SpaceTheme.TEXT_SUB, new Vector2(0f, 0.5f), new Vector2(48f, 0f),
            new Vector2(300f, 50f), FontStyles.Bold, TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
        _minusGroup = StepButton(row, new Vector2(-370f, 0f), false, () => ChangeCount(-1));
        _countValue = SpaceTheme.Text(row, "Value", "", 60, SpaceTheme.TEXT, new Vector2(1f, 0.5f), new Vector2(-230f, 0f),
            new Vector2(180f, 90f), FontStyles.Bold);
        _countValue.richText = true;
        SpaceTextScaler.Exclude(_countValue);
        _plusGroup = StepButton(row, new Vector2(-90f, 0f), true, () => ChangeCount(1));
    }

    private static CanvasGroup StepButton(RectTransform row, Vector2 pos, bool plus, UnityEngine.Events.UnityAction onClick)
    {
        var b = SpaceTheme.Rect(plus ? "Plus" : "Minus", row, new Vector2(1f, 0.5f), pos, new Vector2(88f, 88f));
        var g = b.gameObject.AddComponent<CanvasGroup>();
        var face = SpaceTheme.Img(b, SpaceSprites.Circle, new Color(0.07f, 0.09f, 0.24f, 0.95f), true);
        var rim = SpaceTheme.Rect("Rim", b);
        SpaceTheme.Stretch(rim);
        SpaceTheme.Img(rim, SpaceSprites.RingSmall, SpaceTheme.A(SpaceTheme.CYAN, 0.6f));
        SpaceTheme.Bar(b, Vector2.zero, 34f, 6f, 0f, SpaceTheme.TEXT);
        if (plus) SpaceTheme.Bar(b, Vector2.zero, 34f, 6f, 90f, SpaceTheme.TEXT);
        SpaceTheme.Clickable(b.gameObject, face, onClick);
        return g;
    }

    private void BuildRollButton(float y)
    {
        var btn = SpaceTheme.Rect("Roll", _freeRoot, new Vector2(0.5f, 0f), new Vector2(0f, y), new Vector2(_cw, ROLL_H),
            new Vector2(0.5f, 0f));
        _rollGroup = btn.gameObject.AddComponent<CanvasGroup>();
        var face = SpaceTheme.PrimaryFace(btn);
        SpaceTheme.Clickable(btn.gameObject, face, OnRollPressed);
        _rollLabel = SpaceTheme.Text(btn, "Label", "", 44, SpaceTheme.TEXT_DARK, FontStyles.Bold);
        SpaceTheme.Stretch(_rollLabel.rectTransform, 24f, 0f, 24f, 0f);
    }

    // ================================================================
    // ダイスチャレンジ（2026-11-11）
    // ================================================================
    /// <summary>チャレンジの下の部品（ランキングと、1日1回の振るボタン）</summary>
    private void BuildChallenge()
    {
        _challengeRoot = SpaceTheme.Rect("Challenge", _safe);
        SpaceTheme.Stretch(_challengeRoot);

        var btn = SpaceTheme.Rect("ChallengeRoll", _challengeRoot, new Vector2(0.5f, 0f), new Vector2(0f, BOTTOM),
            new Vector2(_cw, ROLL_H), new Vector2(0.5f, 0f));
        _chRollGroup = btn.gameObject.AddComponent<CanvasGroup>();
        var face = SpaceTheme.GradientFace(btn, SpaceTheme.GOLD, SpaceTheme.Hex(0xFF9E6B), SpaceTheme.GOLD);
        SpaceTheme.Clickable(btn.gameObject, face, OnChallengeRollPressed);
        _chRollLabel = SpaceTheme.Text(btn, "Label", "", 40, SpaceTheme.TEXT_DARK, FontStyles.Bold);
        SpaceTheme.Stretch(_chRollLabel.rectTransform, 24f, 0f, 24f, 0f);
        _chRollLabel.enableAutoSizing = true;
        _chRollLabel.fontSizeMin = 26f;
        _chRollLabel.fontSizeMax = 40f;

        var panel = SpaceTheme.Rect("Ranking", _challengeRoot, new Vector2(0.5f, 0f), new Vector2(0f, BOTTOM + ROLL_H + GAP),
            new Vector2(_cw, RANK_H), new Vector2(0.5f, 0f));
        SpaceTheme.Glass(panel, 40f, new Color(0.05f, 0.07f, 0.20f, 0.72f), SpaceTheme.A(SpaceTheme.GOLD, 0.35f));
        SpaceTheme.Text(panel, "Title", T_RANK_TITLE, 32, SpaceTheme.TEXT, new Vector2(0f, 1f), new Vector2(40f, -48f),
            new Vector2(_cw * 0.55f, 46f), FontStyles.Bold, TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
        _rankInfo = SpaceTheme.Text(panel, "Info", "", 24, SpaceTheme.TEXT_MUTED, new Vector2(1f, 1f), new Vector2(-40f, -48f),
            new Vector2(_cw * 0.45f - 40f, 40f), FontStyles.Bold, TextAlignmentOptions.Right, new Vector2(1f, 0.5f));
        _rankInfo.enableAutoSizing = true;
        _rankInfo.fontSizeMin = 18f;
        _rankInfo.fontSizeMax = 24f;
        var note = SpaceTheme.Text(panel, "Note",
            T_RANK_NOTE_A + SpaceCoupons.NameOf(DiceChallenge.REWARD_FIELD) + T_RANK_NOTE_B, 22,
            SpaceTheme.A(SpaceTheme.GOLD, 0.85f), new Vector2(0.5f, 1f), new Vector2(0f, -88f), new Vector2(_cw - 80f, 32f),
            FontStyles.Bold);
        note.enableAutoSizing = true;
        note.fontSizeMin = 16f;
        note.fontSizeMax = 22f;

        _rankRows = SpaceTheme.Rect("Rows", panel, new Vector2(0.5f, 1f), new Vector2(0f, -112f),
            new Vector2(_cw - 40f, DiceChallenge.TOP * ROW_H), new Vector2(0.5f, 1f));
        _rankEmpty = SpaceTheme.Text(panel, "Empty", T_RANK_LOADING, 26, SpaceTheme.TEXT_MUTED, new Vector2(0.5f, 1f),
            new Vector2(0f, -(112f + DiceChallenge.TOP * ROW_H * 0.5f)), new Vector2(_cw - 80f, 44f), FontStyles.Bold);

        var div = SpaceTheme.Rect("Divider", panel, new Vector2(0.5f, 0f), new Vector2(0f, 18f + MY_H + 10f),
            new Vector2(_cw - 80f, 2f));
        SpaceTheme.Img(div, SpaceSprites.FadeLine, SpaceTheme.A(SpaceTheme.LINE, 0.32f));
        SpaceTheme.Text(panel, "MyLabel", T_MY_LINE, 26, SpaceTheme.TEXT_SUB, Vector2.zero, new Vector2(40f, 18f + MY_H * 0.5f),
            new Vector2(_cw * 0.45f, 40f), FontStyles.Bold, TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
        _myValue = SpaceTheme.Text(panel, "MyValue", "", 44, SpaceTheme.GOLD, new Vector2(1f, 0f),
            new Vector2(-40f, 18f + MY_H * 0.5f), new Vector2(_cw * 0.55f - 40f, 60f), FontStyles.Bold,
            TextAlignmentOptions.Right, new Vector2(1f, 0.5f));
        _myValue.richText = true;
        SpaceTextScaler.Exclude(_myValue);
    }

    private void OnBoardChanged()
    {
        if (this == null || _closing || _mode != Mode.Challenge) return;
        RefreshChallenge();
    }

    /// <summary>ランキング・自分の今日の出目・振るボタンを、いまの状態に合わせる</summary>
    private void RefreshChallenge()
    {
        if (_challengeRoot == null) return;
        var board = DiceChallenge.Current;
        bool ok = board != null && board.Day == DiceChallenge.Today;
        var date = StoreCalendar.TodayBusiness;
        _rankInfo.text = $"{date.Month}月{date.Day}日（{StoreCalendar.WeekdayName(date.DayOfWeek)}）"
                         + (ok ? $" ・ {board.Count}{T_PEOPLE}" : "");

        for (int i = _rankRows.childCount - 1; i >= 0; i--) Destroy(_rankRows.GetChild(i).gameObject);
        _rankEmpty.gameObject.SetActive(!ok || board.Count == 0);
        _rankEmpty.text = ok ? T_RANK_EMPTY : T_RANK_LOADING;
        if (ok)
            for (int i = 0; i < board.Top.Count; i++) BuildRankRow(board.Top[i], i);

        int v = DiceChallenge.TodayValue;
        bool done = DiceChallenge.RolledToday;
        bool pending = v > 0 && !done;
        string sub = SpaceTheme.SUB_HEX;
        if (v <= 0) _myValue.text = $"<size=28><color={sub}>{T_MY_NONE}</color></size>";
        else if (pending) _myValue.text = $"{v}  <size=24><color={sub}>{T_MY_PENDING}</color></size>";
        else if (ok && board.Mine != null)
            _myValue.text = $"{v}  <size=26><color={sub}>{board.Mine.Rank}{T_RANK_UNIT} / {board.Count}{T_PEOPLE}</color></size>";
        else _myValue.text = v.ToString();

        string label;
        bool can;
        if (_rolling) { label = T_ROLLING; can = false; }
        else if (_sending) { label = T_CH_SENDING; can = false; }
        else if (done) { label = T_CH_DONE; can = false; }
        else if (pending) { label = T_CH_SEND; can = true; } // 振ったあとに退店していても送れる
        else if (!DiceChallenge.InStore) { label = T_CH_NOT_IN; can = false; }
        else { label = T_CH_ROLL; can = true; }
        _chRollLabel.text = label;
        _chRollGroup.interactable = can;
        _chRollGroup.alpha = can ? 1f : done ? 0.45f : 0.75f;
        RefreshTray();
        RefreshTabs();
    }

    /// <summary>ランキングの1行（順位・ユーザーA などの名前・出目）。1位は金色、自分の行は水色の帯と「あなた」</summary>
    private void BuildRankRow(DiceChallenge.Entry e, int index)
    {
        var row = SpaceTheme.Rect("Row_" + index, _rankRows, new Vector2(0.5f, 1f),
            new Vector2(0f, -(ROW_H * index + ROW_H * 0.5f)), new Vector2(_cw - 40f, ROW_H - 6f));
        if (e.Mine) SpaceTheme.Rounded(row, SpaceTheme.A(SpaceTheme.CYAN, 0.14f), 22f);
        bool first = e.Rank == 1;
        SpaceTheme.Text(row, "Rank", (first ? "★ " : "") + e.Rank + T_RANK_UNIT, 28, first ? SpaceTheme.GOLD : SpaceTheme.TEXT_SUB,
            new Vector2(0f, 0.5f), new Vector2(24f, 0f), new Vector2(130f, 44f), FontStyles.Bold, TextAlignmentOptions.Left,
            new Vector2(0f, 0.5f));
        string name = e.Name;
        if (e.Mine) name += $"  <size=22><color=#{ColorUtility.ToHtmlStringRGB(SpaceTheme.CYAN)}>{T_YOU}</color></size>";
        var n = SpaceTheme.Text(row, "Name", name, 28, SpaceTheme.TEXT, new Vector2(0f, 0.5f), new Vector2(170f, 0f),
            new Vector2(_cw - 40f - 170f - 180f, 44f), FontStyles.Bold, TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
        n.richText = true;
        var value = SpaceTheme.Text(row, "Value", e.Value.ToString(), 36, first ? SpaceTheme.GOLD : SpaceTheme.TEXT,
            new Vector2(1f, 0.5f), new Vector2(-24f, 0f), new Vector2(150f, 48f), FontStyles.Bold, TextAlignmentOptions.Right,
            new Vector2(1f, 0.5f));
        SpaceTextScaler.Exclude(value);
    }

    /// <summary>トレイを押したとき（フリーだけ振る。チャレンジはうっかり1日1回を使わないよう、ボタンだけ）</summary>
    private void OnTrayPressed()
    {
        if (_mode == Mode.Free) OnRollPressed();
    }

    private void OnChallengeRollPressed()
    {
        // 1日1回・入店中だけ（管理者も同じ）
        if (_rolling || _sending || _closing || _sim == null || DiceChallenge.RolledToday) return;
        if (DiceChallenge.PendingValue > 0)
        {
            SendPendingAsync().Forget(); // 振ったが送れていない（やり直しはできない）
            return;
        }
        ChallengeRollAsync().Forget();
    }

    /// <summary>
    /// チャレンジの 1D100。出目は押したときに決めて端末に保存し（途中で閉じても変わらない）、
    /// 3Dのダイス（金色）は数字を隠して転がして、止まったらその出目を見せる。そのあと送る
    /// </summary>
    private async UniTaskVoid ChallengeRollAsync()
    {
        _rolling = true;
        int value = DiceChallenge.Decide(); // 入店していない・今日もう振ったときは 0
        if (value <= 0)
        {
            _rolling = false;
            RefreshChallenge();
            return;
        }
        _trayHasDice = true;
        RefreshChallenge();
        _sim.SetDieColors(Color.Lerp(SpaceTheme.GOLD, Color.white, 0.3f), DIE_LABEL, DIE_LABEL);
        try
        {
            await _sim.RollPercentileFixedAsync(value);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[SpaceDice] チャレンジのダイスを振れませんでした: {ex.Message}");
        }
        if (this == null || _closing) return;
        _rolling = false;
        await SendPendingAsync();
    }

    /// <summary>決めた出目を送る（失敗したら「結果を送る」で送り直せる。出目は変わらない）</summary>
    private async UniTask SendPendingAsync()
    {
        if (_sending || DiceChallenge.PendingValue <= 0)
        {
            RefreshChallenge();
            return;
        }
        _sending = true;
        RefreshChallenge();
        int value = DiceChallenge.PendingValue;
        var result = await DiceChallenge.SubmitAsync();
        if (this == null) return;
        _sending = false;
        if (result == DiceChallenge.SubmitResult.Failed) SpaceTheme.Toast(T_CH_FAILED);
        else if (result == DiceChallenge.SubmitResult.Already) SpaceTheme.Toast(T_CH_OTHER);
        else
        {
            StartCoroutine(Pop(_myValue.rectTransform, 0f, 1.35f));
            if (value == 100) AssetsDatabase.instance?.PlayLevelUpSE();
        }
        RefreshChallenge();
    }

    // ================================================================
    // 選ぶ
    // ================================================================
    private void SelectKind(int index)
    {
        if (_rolling || index == _kind) return;
        _kind = index;
        _count = Mathf.Min(_count, MaxCount);
        Save();
        RefreshKind();
        RefreshControls();
    }

    private void ChangeCount(int delta)
    {
        if (_rolling) return;
        int next = Mathf.Clamp(_count + delta, 1, MaxCount);
        if (next == _count) return;
        _count = next;
        Save();
        RefreshControls();
    }

    private void Save()
    {
        PlayerPrefs.SetInt(PREF_KIND, _kind);
        PlayerPrefs.SetInt(PREF_COUNT, _count);
        PlayerPrefs.Save();
    }

    /// <summary>種類のボタンの見た目（選んでいるものはダイスの色で光る）</summary>
    private void RefreshKind()
    {
        for (int i = 0; i < _kindChips.Count; i++)
        {
            var c = _kindChips[i];
            var k = KINDS[i];
            bool on = i == _kind;
            c.Face.color = on ? SpaceTheme.A(k.Accent, 0.16f) : CHIP_OFF;
            if (c.Stroke != null) c.Stroke.color = on ? SpaceTheme.A(k.Accent, 0.9f) : SpaceTheme.STROKE;
            c.Label.color = on ? SpaceTheme.TEXT : SpaceTheme.TEXT_SUB;
            for (int j = c.Icon.childCount - 1; j >= 0; j--) Destroy(c.Icon.GetChild(j).gameObject);
            DrawDieIcon(c.Icon, k.Sides, 66f, on ? k.Accent : SpaceTheme.A(SpaceTheme.TEXT_SUB, 0.8f));
        }
    }

    /// <summary>振っている間は選べない・押せない。個数の −・＋ は端で押せない</summary>
    private void RefreshControls()
    {
        bool on = !_rolling;
        _kindGroup.interactable = on;
        _kindGroup.alpha = on ? 1f : 0.55f;
        _countGroup.interactable = on;
        _countGroup.alpha = on ? 1f : 0.55f;
        _rollGroup.interactable = on;
        _rollGroup.alpha = on ? 1f : 0.75f;
        _countValue.text = $"{_count}<size=30><color={SpaceTheme.SUB_HEX}>{T_UNIT}</color></size>";
        SetEnabled(_minusGroup, _count > 1);
        SetEnabled(_plusGroup, _count < MaxCount);
        _rollLabel.text = _rolling ? T_ROLLING : Notation(KINDS[_kind], _count) + T_ROLL;
        if (!_hasResult && !_rolling) ShowIdle();
    }

    private static void SetEnabled(CanvasGroup g, bool on)
    {
        g.alpha = on ? 1f : 0.35f;
        g.interactable = on;
        g.blocksRaycasts = on;
    }

    // ================================================================
    // 振る
    // ================================================================
    private void OnRollPressed()
    {
        if (_rolling || _closing || _sim == null) return;
        RollAsync().Forget();
    }

    private async UniTaskVoid RollAsync()
    {
        _rolling = true;
        var kind = KINDS[_kind];
        int count = _count;
        _trayHasDice = true;
        RefreshTray();
        RefreshControls();
        _totalLabel.text = TotalLabel(kind, count);
        _totalValue.text = "…";
        _badge.text = "";
        LayoutResult(count == 1);
        ClearChips();

        // ダイスはその種類の色を白に寄せた明るい色、数字は濃い紺
        _sim.SetDieColors(Color.Lerp(kind.Accent, Color.white, 0.38f), DIE_LABEL, DIE_LABEL);
        List<int> results = null;
        try
        {
            results = kind.Sides == 100 ? await _sim.RollPercentileAsync(count) : await _sim.RollAsync(count, kind.Sides);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[SpaceDice] ダイスを振れませんでした: {ex.Message}");
        }
        if (this == null || _closing) return;

        _rolling = false;
        RefreshControls();
        if (results == null || results.Count == 0)
        {
            _totalValue.text = T_NONE;
            return;
        }
        ShowResult(kind, results);
    }

    /// <summary>まだ振っていないとき: 選んでいるダイスの表記と「—」</summary>
    private void ShowIdle()
    {
        _totalLabel.text = TotalLabel(KINDS[_kind], _count);
        _totalValue.text = T_NONE;
        _badge.text = "";
        LayoutResult(true);
        ClearChips();
    }

    private void ShowResult(Kind kind, List<int> results)
    {
        _hasResult = true;
        int n = results.Count;
        int total = 0;
        bool allMax = true;
        foreach (int r in results)
        {
            total += r;
            if (r != kind.Sides) allMax = false;
        }
        _totalLabel.text = TotalLabel(kind, n);
        LayoutResult(n == 1);
        ClearChips();
        if (n > 1) BuildChips(kind, results);
        // 合計は札が出そろってから、ぽんと出す
        StartCoroutine(RevealTotal(total.ToString(), n > 1 ? 0.05f * n + 0.1f : 0f));
        _badge.text = allMax ? (n == 1 ? T_MAX : T_ALL_MAX) : "";
        if (allMax) AssetsDatabase.instance?.PlayLevelUpSE();
    }

    private IEnumerator RevealTotal(string text, float delay)
    {
        for (float w = 0f; w < delay; w += Time.unscaledDeltaTime) yield return null;
        _totalValue.text = text;
        yield return Pop(_totalValue.rectTransform, 0f, 1.35f);
    }

    /// <summary>1個なら合計だけを真ん中に大きく、2個以上なら左に合計・右に1個ずつの札</summary>
    private void LayoutResult(bool single)
    {
        float x = single ? _cw * 0.5f : RESULT_LEFT;
        _totalLabel.rectTransform.anchoredPosition = new Vector2(x, 66f);
        _totalValue.rectTransform.anchoredPosition = new Vector2(x, -8f);
        _badge.rectTransform.anchoredPosition = new Vector2(x, -80f);
        _chips.gameObject.SetActive(!single);
    }

    private void ClearChips()
    {
        for (int i = _chips.childCount - 1; i >= 0; i--) Destroy(_chips.GetChild(i).gameObject);
    }

    /// <summary>出目の札（5個まで1段、6個からは2段）。いちばん大きい目は金色に光らせる</summary>
    private void BuildChips(Kind kind, List<int> results)
    {
        int n = results.Count;
        int perRow = n <= 5 ? n : Mathf.CeilToInt(n / 2f);
        int rows = Mathf.CeilToInt(n / (float)perRow);
        const float gap = 12f;
        var area = _chips.rect.size;
        float size = Mathf.Min(100f, (area.x - gap * (perRow - 1)) / perRow, (area.y - gap * (rows - 1)) / rows);
        float totalH = rows * size + (rows - 1) * gap;
        for (int i = 0; i < n; i++)
        {
            int row = i / perRow, col = i % perRow;
            int inRow = Mathf.Min(perRow, n - row * perRow);
            float rowW = inRow * size + (inRow - 1) * gap;
            var pos = new Vector2(-rowW * 0.5f + size * 0.5f + col * (size + gap), totalH * 0.5f - size * 0.5f - row * (size + gap));
            var chip = SpaceTheme.Rect("Die_" + i, _chips, SpaceTheme.CENTER, pos, new Vector2(size, size));
            bool max = results[i] == kind.Sides;
            var accent = max ? SpaceTheme.GOLD : kind.Accent;
            if (max)
            {
                var glow = SpaceTheme.Rect("Glow", chip, SpaceTheme.CENTER, Vector2.zero, new Vector2(size * 1.8f, size * 1.8f));
                SpaceTheme.Img(glow, SpaceSprites.Glow, SpaceTheme.A(SpaceTheme.GOLD, 0.35f));
            }
            var face = SpaceTheme.Rect("Face", chip);
            SpaceTheme.Stretch(face);
            SpaceTheme.Rounded(face, SpaceTheme.A(accent, max ? 0.22f : 0.14f), size * 0.26f);
            var edge = SpaceTheme.Rect("Edge", chip);
            SpaceTheme.Stretch(edge);
            SpaceTheme.RoundedStroke(edge, SpaceTheme.A(accent, 0.85f), size * 0.26f);
            string text = results[i].ToString();
            var num = SpaceTheme.Text(chip, "Value", text, size * (text.Length >= 3 ? 0.32f : 0.44f),
                max ? SpaceTheme.GOLD : SpaceTheme.TEXT, FontStyles.Bold);
            SpaceTheme.Stretch(num.rectTransform);
            SpaceTextScaler.Exclude(num);
            chip.localScale = Vector3.zero;
            StartCoroutine(Pop(chip, 0.05f * i, 0.3f));
        }
    }

    /// <summary>ぽんと出る（from の大きさから少し行き過ぎて 1 に落ち着く）</summary>
    private static IEnumerator Pop(RectTransform rt, float delay, float from)
    {
        if (rt == null) yield break;
        if (from < 1f) rt.localScale = Vector3.zero;
        for (float w = 0f; w < delay; w += Time.unscaledDeltaTime) yield return null;
        const float D = 0.32f;
        for (float t = 0f; t < D; t += Time.unscaledDeltaTime)
        {
            if (rt == null) yield break;
            float s = Mathf.LerpUnclamped(from, 1f, EaseOutBack(t / D));
            rt.localScale = new Vector3(s, s, 1f);
            yield return null;
        }
        if (rt != null) rt.localScale = Vector3.one;
    }

    private static float EaseOutBack(float x)
    {
        const float c1 = 1.70158f, c3 = c1 + 1f;
        x = Mathf.Clamp01(x);
        return 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f);
    }

    // ================================================================
    // 絵
    // ================================================================
    /// <summary>
    /// ダイスの形の線画（size 四方）。D4=三角錐 / D6=3の目 / D8=ひし形 / D10=凧形 / D12=五角形 / D20=六角形に三角 / D100=凧形2つ
    /// </summary>
    private static void DrawDieIcon(RectTransform p, int sides, float size, Color c)
    {
        float h = size * 0.5f;
        float thick = Mathf.Max(2f, size * 0.06f);
        float thin = Mathf.Max(1.5f, size * 0.04f);
        var soft = SpaceTheme.A(c, c.a * 0.55f);
        Vector2 P(float x, float y) => new Vector2(x * h, y * h);
        switch (sides)
        {
            case 4:
            {
                Vector2 a = P(0f, 0.92f), b = P(-0.84f, -0.6f), d = P(0.84f, -0.6f), g = P(0f, -0.09f);
                Poly(p, thick, c, a, b, d);
                Line(p, g, a, thin, soft);
                Line(p, g, b, thin, soft);
                Line(p, g, d, thin, soft);
                break;
            }
            case 6:
            {
                Poly(p, thick, c, P(-0.72f, 0.72f), P(0.72f, 0.72f), P(0.72f, -0.72f), P(-0.72f, -0.72f));
                foreach (var pip in new[] { P(-0.36f, 0.36f), P(0f, 0f), P(0.36f, -0.36f) })
                {
                    var dot = SpaceTheme.Rect("Pip", p, SpaceTheme.CENTER, pip, new Vector2(size * 0.16f, size * 0.16f));
                    SpaceTheme.Img(dot, SpaceSprites.Circle, c);
                }
                break;
            }
            case 8:
            {
                Vector2 t = P(0f, 0.95f), r = P(0.72f, 0f), b = P(0f, -0.95f), l = P(-0.72f, 0f);
                Poly(p, thick, c, t, r, b, l);
                Line(p, l, r, thin, soft);
                break;
            }
            case 10:
                Kite(p, Vector2.zero, h, thick, thin, c, soft);
                break;
            case 12:
            {
                var outer = new Vector2[5];
                var inner = new Vector2[5];
                for (int i = 0; i < 5; i++)
                {
                    float ang = (90f + i * 72f) * Mathf.Deg2Rad;
                    outer[i] = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * h * 0.95f;
                    inner[i] = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * h * 0.48f;
                }
                Poly(p, thick, c, outer);
                Poly(p, thin, soft, inner);
                for (int i = 0; i < 5; i++) Line(p, inner[i], outer[i], thin, soft);
                break;
            }
            case 20:
            {
                var hex = new Vector2[6];
                for (int i = 0; i < 6; i++)
                {
                    float ang = (90f + i * 60f) * Mathf.Deg2Rad;
                    hex[i] = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * h * 0.95f;
                }
                // hex: 0=上 1=左上 2=左下 3=下 4=右下 5=右上
                var tri = new Vector2[3];
                for (int i = 0; i < 3; i++)
                {
                    float ang = (90f + i * 120f) * Mathf.Deg2Rad;
                    tri[i] = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * h * 0.5f;
                }
                // tri: 0=上 1=左下 2=右下
                Poly(p, thick, c, hex);
                Poly(p, thin, soft, tri);
                Line(p, tri[0], hex[0], thin, soft);
                Line(p, tri[1], hex[2], thin, soft);
                Line(p, tri[2], hex[4], thin, soft);
                Line(p, tri[0], hex[1], thin, soft);
                Line(p, tri[1], hex[1], thin, soft);
                Line(p, tri[1], hex[3], thin, soft);
                Line(p, tri[2], hex[3], thin, soft);
                Line(p, tri[2], hex[5], thin, soft);
                Line(p, tri[0], hex[5], thin, soft);
                break;
            }
            default: // 100: 十の位と一の位の2つの凧形
                Kite(p, P(-0.48f, 0f), h * 0.54f, thick * 0.8f, thin * 0.8f, c, soft);
                Kite(p, P(0.48f, 0f), h * 0.54f, thick * 0.8f, thin * 0.8f, c, soft);
                break;
        }
    }

    /// <summary>10面ダイスの凧形（外形と、手前の面の線）</summary>
    private static void Kite(RectTransform p, Vector2 center, float h, float thick, float thin, Color c, Color soft)
    {
        Vector2 P(float x, float y) => center + new Vector2(x * h, y * h);
        Vector2 t = P(0f, 0.95f), r = P(0.86f, 0.12f), b = P(0f, -0.95f), l = P(-0.86f, 0.12f), m = P(0f, -0.3f);
        Poly(p, thick, c, t, r, b, l);
        Line(p, l, m, thin, soft);
        Line(p, r, m, thin, soft);
        Line(p, m, b, thin, soft);
    }

    private static void Poly(RectTransform p, float thick, Color c, params Vector2[] pts)
    {
        for (int i = 0; i < pts.Length; i++) Line(p, pts[i], pts[(i + 1) % pts.Length], thick, c);
    }

    private static void Line(RectTransform p, Vector2 a, Vector2 b, float thick, Color c)
    {
        var d = b - a;
        // 端が丸いので、少し長くして角をきれいにつなぐ
        SpaceTheme.Bar(p, (a + b) * 0.5f, d.magnitude + thick * 0.6f, thick, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, c);
    }

    /// <summary>背景のまばらな星（決まった並び）</summary>
    private static void Sky(RectTransform rt)
    {
        var rnd = new System.Random(20261111);
        var layer = SpaceTheme.Rect("Sky", rt);
        SpaceTheme.Stretch(layer);
        for (int i = 0; i < 60; i++)
        {
            var pos = new Vector2((float)rnd.NextDouble(), (float)rnd.NextDouble());
            float s = 3f + (float)rnd.NextDouble() * 5f;
            var star = SpaceTheme.Rect("S", layer, pos, Vector2.zero, new Vector2(s, s));
            var img = SpaceTheme.Img(star, SpaceSprites.Circle, SpaceTheme.A(Color.white, 0.12f + (float)rnd.NextDouble() * 0.3f));
            if (i % 3 == 0) star.gameObject.AddComponent<SpaceTwinkle>().Init(img, img.color.a);
        }
    }

    /// <summary>
    /// トレイの床の模様（宇宙）: 中央がわずかに明るい紺に星を散らし、ダイスが転がる範囲（見えない壁の内側）に
    /// 細い光の縁を引く。床は 8.06 四方・壁の内側は 6.2 四方（Dice3DSimulator の TRAY_HALF）
    /// </summary>
    private static Texture2D MakeFloorTexture()
    {
        const int S = 512;
        const float WALL = 3.1f / 4.03f;
        var center = new Color(0.060f, 0.075f, 0.180f);
        var edge = new Color(0.020f, 0.028f, 0.075f);
        var rim = new Color(0.36f, 0.88f, 1.00f);
        var buf = new Color[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float u = (x + 0.5f) / S * 2f - 1f, v = (y + 0.5f) / S * 2f - 1f;
                float r = Mathf.Sqrt(u * u + v * v);
                var c = Color.Lerp(center, edge, Mathf.SmoothStep(0f, 1f, r / 1.3f));
                float d = SdRoundBox(u, v, WALL, WALL, 0.10f);
                float line = Mathf.Exp(-(d * d) / (0.0045f * 0.0045f)) * 0.42f + Mathf.Exp(-(d * d) / (0.02f * 0.02f)) * 0.10f;
                buf[y * S + x] = c + rim * line;
            }
        var rnd = new System.Random(20261111);
        for (int i = 0; i < 260; i++)
        {
            float sx = (float)rnd.NextDouble() * S, sy = (float)rnd.NextDouble() * S;
            float rad = 0.6f + (float)rnd.NextDouble() * 1.6f;
            float bright = 0.25f + (float)rnd.NextDouble() * 0.75f;
            double t = rnd.NextDouble();
            var tint = t < 0.7 ? Color.white : t < 0.85 ? new Color(0.6f, 0.9f, 1f) : new Color(0.8f, 0.7f, 1f);
            int r0 = Mathf.CeilToInt(rad * 2.5f);
            for (int dy = -r0; dy <= r0; dy++)
                for (int dx = -r0; dx <= r0; dx++)
                {
                    int xx = (int)sx + dx, yy = (int)sy + dy;
                    if (xx < 0 || yy < 0 || xx >= S || yy >= S) continue;
                    float ex = xx + 0.5f - sx, ey = yy + 0.5f - sy;
                    float a = Mathf.Exp(-(ex * ex + ey * ey) / (rad * rad)) * bright;
                    buf[yy * S + xx] += tint * a;
                }
        }
        var px = new Color32[S * S];
        for (int i = 0; i < buf.Length; i++)
        {
            var c = buf[i];
            px[i] = new Color32((byte)(Mathf.Clamp01(c.r) * 255f), (byte)(Mathf.Clamp01(c.g) * 255f),
                (byte)(Mathf.Clamp01(c.b) * 255f), 255);
        }
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, true)
        {
            name = "space_dice_floor",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Trilinear,
            anisoLevel = 4,
        };
        tex.SetPixels32(px);
        tex.Apply(true, true);
        return tex;
    }

    private static float SdRoundBox(float px, float py, float hx, float hy, float r)
    {
        float qx = Mathf.Abs(px) - (hx - r);
        float qy = Mathf.Abs(py) - (hy - r);
        float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f);
        return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
    }
}
