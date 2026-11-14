using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ミニオンの画面（2026-11-14 ユーザー指定「きずな座をアクティブにすると、自分のためにアイテムを見つけて運んできてくれるミニオンを
/// 扱えるように。ミニオンウィンドウを出して、まだ旅に出ていないミニオンを旅に行かせる。職業4種類（戦士・魔法使い・弓使い・銃使い）と
/// 属性（炎・水・自然・雷）から選んで作る」）の全画面。決まりは MinionRules、保存は MinionStore。
///  ・上: 今日の幸運（成功しやすい職業と属性。一致するごとに成功率 +5%）
///  ・ミニオンのカード（作った順。多ければスクロール）: 名前・Lv.・成功率とレアの割合・今の様子
///    待機中 →「旅に出す」／旅の途中 → 残り時間／帰ってきた →「話しかける」（持ち帰った物を受け取る。SpaceNotice.MinionBack）。
///    管理者だけ、旅の途中のミニオンに「すぐ帰還」（24時間待たずに帰らせて、そのまま受け取る。2026-11-14）。
///    受け取れるのは入店中だけ（2026-11-14 ユーザー指定。入店していなければボタンを淡くして下に「入店中だけ」・押すと知らせ）
///  ・下:「新しいミニオンを作る」（1体目・みんな Lv.150 になったとき。職業と属性を選ぶシート）。作れなければ、どうすれば作れるか
/// 開くたびにサーバーの時刻を読んで残り時間を合わせ、帰ってから1週間すぎたミニオンを片づける（食べてしまったら知らせる）。
/// 開く場所: ホームの空のきずな座（左の列のまん中。SpaceBackground）。配色はきずな座の色（ミント）。
/// Android の戻るは NewHomeController が Instance.HandleBack を呼ぶ（作るシート → 画面）。
/// </summary>
public class SpaceMinions : MonoBehaviour
{
    /// <summary>きずな座の色（見出し・縁・光）</summary>
    public static readonly Color ACCENT = SpaceTheme.MINT;
    private static readonly Color ACCENT_LIGHT = SpaceTheme.Hex(0xB8FFE0);
    private static readonly Color BG = new Color(0.01f, 0.03f, 0.05f, 0.985f);
    private static readonly Color CARD_BG = new Color(0.04f, 0.08f, 0.13f, 0.92f);
    private static readonly Color BUTTON_DARK = new Color(0.04f, 0.09f, 0.14f, 0.92f);

    private const string T_CAPTION = "MINIONS";
    private const string T_TITLE = "ミニオン";
    private const string T_LUCK = "今日の幸運";
    private const string T_LUCK_SUB = "職業・属性が一致するごとに 成功率 +5%";
    private const string T_SUCCESS = "成功率 ";
    private const string T_RARE = "レア ";
    private const string T_LUCK_TAG = "（幸運 +";
    private const string T_IDLE = "待機中";
    private const string T_SEND = "旅に出す";
    private const string T_SENDING = "出発…";
    private const string T_TRAVELING = "旅の途中…";
    private const string T_SOON = "まもなく帰ってきます";
    private const string T_REMAIN_A = "あと ";
    private const string T_HOURS = "時間";
    private const string T_MINUTES = "分";
    private const string T_BACK = "帰ってきた！";
    private const string T_TALK = "話しかける";
    private const string T_TALKING = "…";
    private const string T_INSTANT = "すぐ帰還";     // 管理者だけ（2026-11-14）
    private const string T_INSTANT_SUB = "管理者だけ";
    private const string T_KEEP_A = "あと";
    private const string T_KEEP_DAYS = "日のうちに話しかけてね";
    private const string T_KEEP_HOURS = "時間のうちに話しかけてね";
    private const string T_KEEP_SOON = "もうすぐ1週間！急いで話しかけて";
    private const string T_CREATE = "新しいミニオンを作る";
    private const string T_CREATE_FIRST = "ミニオンを作る";
    private const string T_CREATE_LATER = "ミニオンが Lv.150 になると、新しいミニオンを作れます";
    // 作れる数にお店からもらったミニオンも数えるので（2026-11-14）、2体以上が Lv.150 になるまで作れない・もう作れないこともある
    private const string T_CREATE_LATER_A = "ミニオンがあと ";
    private const string T_CREATE_LATER_B = " 体 Lv.150 になると、新しいミニオンを作れます";
    private const string T_CREATE_BLOCKED = "お店からもらった Lv.150 のミニオンがいるので、これ以上は作れません";
    private const string T_ONLY_IN_STORE = "入店中だけ"; // 話しかける・すぐ帰還のボタンの下（入店していないとき。2026-11-14）
    private const string T_RULES = "旅は24時間。帰ってから1週間のうちに、入店中に話しかけると持ち帰った物を受け取れます";
    private const string T_INTRO =
        "ミニオンは、あなたのために旅に出て、\nアイテムを見つけて運んできてくれる仲間です";
    private const string T_INTRO_1 = "旅に出すと、24時間後に帰ってきます（失敗することもあります）";
    private const string T_INTRO_2 = "帰ってから1週間のうちに、入店中に話しかけると持ち帰った物を受け取れます。すぎると食べてしまいます";
    private const string T_INTRO_3 = "帰ってくるたびにレベルが上がり、旅が成功しやすく・レアな物を持ち帰りやすくなります（Lv.150 まで）";
    private const string T_SHEET_TITLE = "ミニオンを作る";
    private const string T_SHEET_JOB = "職業";
    private const string T_SHEET_ELEMENT = "属性";
    private const string T_SHEET_LUCK_A = "今日の幸運は「";
    private const string T_SHEET_LUCK_B = "」（毎日かわります）";
    private const string T_SHEET_CANCEL = "やめる";
    private const string T_SHEET_OK = "このミニオンを作る";
    private const string T_SHEET_BUSY = "作っています…";
    private const string T_JOINED_B = " が仲間になりました！";
    private const string T_SENT_B = " が旅に出ました。";
    private const string T_SENT_C = "ごろ帰ってきます";
    private const string T_TOMORROW = "明日 ";
    private const string T_TODAY = "今日 ";
    private const string T_OPEN_FAILED = "ミニオンの画面を開けませんでした";
    private const string T_STARS_GOT = " を集めました";
    private const string T_EXPIRED_EMPTY = "1週間たっていたので、旅の報告は終わっていました";

    /// <summary>この画面で表示しうる文字（NewHome の起動時にまとめてフォントへ登録する）</summary>
    public static string GlyphText => new StringBuilder()
        .Append(T_CAPTION).Append(T_TITLE).Append(T_LUCK).Append(T_LUCK_SUB).Append(T_SUCCESS).Append(T_RARE).Append(T_LUCK_TAG)
        .Append(T_IDLE).Append(T_SEND).Append(T_SENDING).Append(T_TRAVELING).Append(T_SOON).Append(T_REMAIN_A).Append(T_HOURS)
        .Append(T_MINUTES).Append(T_BACK).Append(T_TALK).Append(T_TALKING).Append(T_KEEP_A).Append(T_KEEP_DAYS).Append(T_KEEP_HOURS)
        .Append(T_KEEP_SOON).Append(T_CREATE).Append(T_CREATE_FIRST).Append(T_CREATE_LATER).Append(T_RULES).Append(T_INTRO)
        .Append(T_INTRO_1).Append(T_INTRO_2).Append(T_INTRO_3).Append(T_SHEET_TITLE).Append(T_SHEET_JOB).Append(T_SHEET_ELEMENT)
        .Append(T_SHEET_LUCK_A).Append(T_SHEET_LUCK_B).Append(T_SHEET_CANCEL).Append(T_SHEET_OK).Append(T_SHEET_BUSY)
        .Append(T_JOINED_B).Append(T_SENT_B).Append(T_SENT_C).Append(T_TOMORROW).Append(T_TODAY).Append(T_OPEN_FAILED)
        .Append(T_STARS_GOT).Append(T_EXPIRED_EMPTY).Append(T_INSTANT).Append(T_INSTANT_SUB)
        .Append(T_CREATE_LATER_A).Append(T_CREATE_LATER_B).Append(T_CREATE_BLOCKED).Append(T_ONLY_IN_STORE)
        .Append("0123456789:/%・！!（）+Lv.")
        .Append(MinionStore.GlyphText).Append(SpaceNotice.MinionGlyphText).ToString();

    // 配置（セーフエリアの中。上からの距離）
    private const float SIDE = 40f;
    private const float MAX_W = 1000f;
    private const float LUCK_TOP = 196f, LUCK_H = 140f;
    private const float LIST_TOP = 360f, FOOTER_H = 196f;
    private const float CARD_H = 324f, CARD_GAP = 22f;
    private const float PIC_W = 310f; // カードの左のミニオンの絵の幅

    /// <summary>開いている画面（なければ null）</summary>
    public static SpaceMinions Instance { get; private set; }

    private Canvas _canvas;
    private CanvasGroup _group;
    private RectTransform _safe, _content, _footer;
    private ScrollRect _scroll;
    private float _cw, _viewH;
    private bool _closing, _busy;
    private float _nextTick;
    private string _shownKey;
    private GameObject _sheet;

    private sealed class CardView
    {
        public string Id;
        public MinionState State;
        public DateTime BackAt, EatAt, SentAt;
        public RectTransform Bubble;
        public Image Glow, TripFill;
        public float TripW;
        public TextMeshProUGUI Remain, Keep, ActionLabel;
        public CanvasGroup ActionGroup;
        public float Phase;
    }
    private readonly List<CardView> _cards = new List<CardView>();
    private readonly List<GameObject> _dynamic = new List<GameObject>(); // Refresh で作り直す部品（今日の幸運・一覧・下のボタン）

    /// <summary>ミニオンの画面を開く。きずな座が完成していなければ開かない（管理者は初めから開ける）</summary>
    public static SpaceMinions Open(Canvas canvas = null)
    {
        if (Instance != null) return Instance;
        if (!MinionStore.Unlocked)
        {
            SpaceTheme.Toast(MinionStore.T_LOCKED);
            return null;
        }
        if (canvas == null) canvas = SpaceTheme.MainCanvas();
        if (canvas == null) return null;
        var root = SpaceTheme.Rect("__SpaceMinions", canvas.transform);
        SpaceTheme.Stretch(root);
        root.SetAsLastSibling();
        SpaceTheme.SubCanvas(root.gameObject, true); // 光の明滅・残り時間の更新をホームの再描画と分ける
        var page = root.gameObject.AddComponent<SpaceMinions>();
        Instance = page;
        try
        {
            page.Build(canvas);
        }
        catch (Exception ex)
        {
            // 作る途中で失敗したら、見えないまま画面全体を覆って押せなくなる（アプリが固まったように見える）ので、片づけて知らせる
            Debug.LogError($"[SpaceMinions] ミニオンの画面を作れませんでした: {ex}");
            Instance = null;
            UnityEngine.Object.Destroy(root.gameObject);
            SpaceTheme.Toast(T_OPEN_FAILED);
            return null;
        }
        page.SyncRoutine().Forget();
        return page;
    }

    /// <summary>戻る操作（Android の戻るボタン）。作るシートを開いていたらシートを、そうでなければ画面を閉じる</summary>
    public void HandleBack()
    {
        if (_sheet != null)
        {
            if (!_busy) CloseSheet();
            return;
        }
        if (_busy) return;
        Close();
    }

    public void Close()
    {
        if (_closing || _busy) return;
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
    }

    private void Update()
    {
        if (_closing) return;
        float t = Time.unscaledTime;
        // 帰ってきたミニオン:「!」が弾み、後ろの光が明滅する
        foreach (var c in _cards)
        {
            if (c.State != MinionState.Back) continue;
            if (c.Bubble != null) c.Bubble.anchoredPosition = new Vector2(c.Bubble.anchoredPosition.x, -58f + 8f * Mathf.Abs(Mathf.Sin(t * 3.2f + c.Phase)));
            if (c.Glow != null) c.Glow.color = SpaceTheme.A(c.Glow.color, 0.30f + 0.22f * (0.5f + 0.5f * Mathf.Sin(t * 2.6f + c.Phase)));
        }
        if (t < _nextTick) return;
        _nextTick = t + 0.5f;
        if (_busy || _sheet != null) return;
        // ミニオンが帰ってきた・ほかの端末で変わった・日付が変わった（今日の幸運）なら作り直す。そうでなければ残り時間だけ
        if (StateKey() != _shownKey) Refresh();
        else Tick();
    }

    // ================================================================
    // 画面
    // ================================================================
    private void Build(Canvas canvas)
    {
        _canvas = canvas;
        var rt = (RectTransform)transform;
        _group = gameObject.AddComponent<CanvasGroup>();
        _group.alpha = 0f;

        var size = SpaceTheme.CanvasSize(canvas);
        var insets = SpaceTheme.SafeInsets(size);
        _cw = Mathf.Min(size.x - SIDE * 2f, MAX_W);
        _viewH = size.y - insets.top - insets.bottom - LIST_TOP - FOOTER_H;

        // 背景（下のホームを押させない）: 深い紺に、上から差すきずな座の色の光・星
        var dim = SpaceTheme.Rect("Dim", rt);
        SpaceTheme.Stretch(dim);
        SpaceTheme.Img(dim, null, BG, true);
        var glow = SpaceTheme.Rect("Glow", rt, new Vector2(0.5f, 1f), new Vector2(0f, -300f), new Vector2(1700f, 1300f));
        SpaceTheme.Img(glow, SpaceSprites.Glow, SpaceTheme.A(ACCENT, 0.11f));
        Sky(rt);

        _safe = SpaceTheme.Rect("Safe", rt);
        SpaceTheme.Stretch(_safe);
        _safe.gameObject.AddComponent<SpaceSafeArea>();

        BuildHeader();
        Refresh();
        StartCoroutine(SpaceTheme.Tween(0.25f, 0f, k => _group.alpha = k));
    }

    private void BuildHeader()
    {
        var caption = SpaceTheme.Text(_safe, "Caption", T_CAPTION, 24, SpaceTheme.A(ACCENT, 0.95f), new Vector2(0f, 1f),
            new Vector2(SIDE + 16f, -70f), new Vector2(400f, 36f), FontStyles.Bold, TextAlignmentOptions.Left,
            new Vector2(0f, 0.5f));
        SpaceTheme.Tracking(caption, 18f);
        var title = SpaceTheme.Text(_safe, "Title", T_TITLE, 52, SpaceTheme.TEXT, new Vector2(0f, 1f), new Vector2(SIDE + 16f, -126f),
            new Vector2(420f, 66f), FontStyles.Bold, TextAlignmentOptions.Left, new Vector2(0f, 0.5f));

        // 題の右に、この画面をくれたきずな座（いまのレベルの姿。管理者で未完成なら完成した姿）を小さく
        var p = Constellations.Progress().Find(x => x.Def.Id == MinionStore.CONSTELLATION);
        if (p != null)
        {
            float titleW = Mathf.Min(420f, title.GetPreferredValues(T_TITLE).x * SpaceTheme.TextScale);
            var fig = SpaceTheme.Rect("Constellation", _safe, new Vector2(0f, 1f),
                new Vector2(SIDE + 16f + titleW + 28f + 60f, -104f), new Vector2(120f, 120f));
            SpaceConstellationPage.Figure(fig, p.Def, Mathf.Max(p.Def.Size, p.Count - p.Partial), 120f);
        }

        // 右上の閉じる
        var close = SpaceTheme.Rect("Close", _safe, new Vector2(1f, 1f), new Vector2(-(SIDE + 52f), -84f),
            new Vector2(104f, 104f));
        var face = SpaceTheme.Img(close, SpaceSprites.Circle, BUTTON_DARK, true);
        var rim = SpaceTheme.Rect("Rim", close);
        SpaceTheme.Stretch(rim);
        SpaceTheme.Img(rim, SpaceSprites.RingSmall, SpaceTheme.A(ACCENT, 0.5f));
        SpaceTheme.Bar(close, Vector2.zero, 44f, 6f, 45f, SpaceTheme.TEXT);
        SpaceTheme.Bar(close, Vector2.zero, 44f, 6f, -45f, SpaceTheme.TEXT);
        SpaceTheme.Clickable(close.gameObject, face, Close);
    }

    /// <summary>今の状態を見る文字（変わったら作り直す）</summary>
    private static string StateKey()
    {
        var book = MinionStore.Book;
        var now = MinionStore.NowUtc;
        // 管理者かどうかも入れる（判定はあとから届くので、届いたら「すぐ帰還」を出すために作り直す）。
        // 入店中かどうかも（話しかける・すぐ帰還のボタンの淡さと「入店中だけ」が変わる）
        var sb = new StringBuilder(MinionStore.TodayKey).Append('|').Append(book.CanCreate).Append('|').Append(AdminClaim.IsAdmin)
            .Append('|').Append(MinionStore.InStore);
        foreach (var m in book.Items)
            sb.Append('|').Append(m.Id).Append(':').Append(m.Job).Append(m.Element).Append(':').Append(m.Level).Append(':')
                .Append(m.Sent?.Ticks ?? 0).Append(':').Append((int)MinionRules.StateOf(m, now));
        return sb.ToString();
    }

    /// <summary>今日の幸運・ミニオンのカード・下のボタンを作り直す</summary>
    private void Refresh()
    {
        _shownKey = StateKey();
        _cards.Clear();
        // 前に作った部品は、すぐ隠してから消す（Destroy はフレームの終わりなので、同じフレームで作り直しても重ならないように）
        foreach (var old in _dynamic)
        {
            if (old == null) continue;
            old.SetActive(false);
            Destroy(old);
        }
        _dynamic.Clear();
        _scroll = null;
        var book = MinionStore.Book;
        BuildLuck();
        if (book.Items.Count == 0)
        {
            BuildIntro();
            return;
        }
        BuildList(book);
        BuildFooter(book);
        Tick();
    }

    /// <summary>今日の幸運（成功しやすい職業と属性。その組み合わせのミニオンの絵で見せる）</summary>
    private void BuildLuck()
    {
        MinionRules.Lucky(MinionStore.TodayKey, out int job, out int element);
        var panel = SpaceTheme.Rect("Luck", _safe, new Vector2(0.5f, 1f), new Vector2(0f, -(LUCK_TOP + LUCK_H * 0.5f)),
            new Vector2(_cw, LUCK_H));
        _dynamic.Add(panel.gameObject);
        SpaceTheme.Glass(panel, 36f, new Color(0.04f, 0.10f, 0.12f, 0.85f), SpaceTheme.A(ACCENT, 0.4f));
        var glow = SpaceTheme.Rect("Glow", panel, new Vector2(0f, 0.5f), new Vector2(86f, 0f), new Vector2(190f, 190f));
        SpaceTheme.Img(glow, SpaceSprites.Glow, SpaceTheme.A(MinionArt.ColorOf(element), 0.4f));
        var pic = SpaceTheme.Rect("Minion", panel, new Vector2(0f, 0.5f), new Vector2(86f, 2f), new Vector2(120f, 120f));
        var img = SpaceTheme.Img(pic, MinionArt.Sprite(job, element), Color.white);
        img.preserveAspect = true;
        // 3行（見出し・職業と属性・説明）は、文字をいちばん大きくする設定（1.3倍）でも重ならない間にしてある
        SpaceTheme.Text(panel, "Label", T_LUCK, 24, SpaceTheme.A(ACCENT, 0.95f), new Vector2(0f, 0.5f),
            new Vector2(168f, 38f), new Vector2(_cw - 200f, 32f), FontStyles.Bold, TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
        var value = SpaceTheme.Text(panel, "Value",
            Colored(MinionRules.JOB_NAMES[job], SpaceTheme.TEXT) + "　" + Colored(MinionRules.ELEMENT_NAMES[element], MinionArt.ColorOf(element)),
            38, SpaceTheme.TEXT, new Vector2(0f, 0.5f), new Vector2(168f, -2f), new Vector2(_cw - 290f, 48f), FontStyles.Bold,
            TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
        value.richText = true;
        var icon = SpaceTheme.Rect("Element", panel, new Vector2(1f, 0.5f), new Vector2(-70f, 8f), new Vector2(76f, 76f));
        var iconImg = SpaceTheme.Img(icon, MinionArt.ElementIcon(element), Color.white);
        iconImg.preserveAspect = true;
        var sub = SpaceTheme.Text(panel, "Sub", T_LUCK_SUB, 21, SpaceTheme.TEXT_SUB, new Vector2(0f, 0.5f), new Vector2(168f, -44f),
            new Vector2(_cw - 290f, 30f), FontStyles.Bold, TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
        sub.enableAutoSizing = true;
        sub.fontSizeMin = 15f;
        sub.fontSizeMax = 21f;
    }

    /// <summary>まだミニオンがいないとき: どんな仲間かの説明と「ミニオンを作る」</summary>
    private void BuildIntro()
    {
        var intro = SpaceTheme.Rect("Intro", _safe);
        _dynamic.Add(intro.gameObject);
        SpaceTheme.Stretch(intro, SIDE, 40f, SIDE, LIST_TOP);
        var top = new Vector2(0.5f, 1f);
        // 4つの職業を属性違いで並べる
        float tile = Mathf.Min(220f, _cw / 4f);
        for (int i = 0; i < MinionRules.JOBS; i++)
        {
            int element = (i + 1) % MinionRules.ELEMENTS;
            var pos = new Vector2(-tile * 1.5f + i * tile, -tile * 0.55f);
            var g = SpaceTheme.Rect("Glow" + i, intro, top, pos, new Vector2(tile * 1.3f, tile * 1.3f));
            SpaceTheme.Img(g, SpaceSprites.Glow, SpaceTheme.A(MinionArt.ColorOf(element), 0.3f));
            var pic = SpaceTheme.Rect("Minion" + i, intro, top, pos, new Vector2(tile, tile));
            var img = SpaceTheme.Img(pic, MinionArt.Sprite(i, element), Color.white);
            img.preserveAspect = true;
        }
        float y = tile * 1.15f + 20f;
        var lead = SpaceTheme.Text(intro, "Lead", T_INTRO, 34, SpaceTheme.TEXT, top, new Vector2(0f, -(y + 50f)),
            new Vector2(_cw, 100f), FontStyles.Bold);
        lead.lineSpacing = 6f;
        y += 120f;
        foreach (var line in new[] { T_INTRO_1, T_INTRO_2, T_INTRO_3 })
        {
            var t = SpaceTheme.Text(intro, "Rule", "・" + line, 25, SpaceTheme.TEXT_SUB, top, new Vector2(0f, -(y + 36f)),
                new Vector2(_cw - 40f, 72f), FontStyles.Bold, TextAlignmentOptions.Left);
            t.textWrappingMode = TextWrappingModes.Normal;
            y += 84f;
        }
        var create = SpaceTheme.Rect("Create", intro, top, new Vector2(0f, -(y + 40f + 64f)), new Vector2(Mathf.Min(640f, _cw), 128f));
        var face = SpaceTheme.PrimaryFace(create);
        SpaceTheme.Clickable(create.gameObject, face, OpenSheet);
        var label = SpaceTheme.Text(create, "Label", T_CREATE_FIRST, 42, SpaceTheme.TEXT_DARK, FontStyles.Bold);
        SpaceTheme.Stretch(label.rectTransform, 16f, 0f, 16f, 0f);
    }

    /// <summary>ミニオンのカード（作った順。入りきらなければスクロール）</summary>
    private void BuildList(MinionBook book)
    {
        var list = SpaceTheme.Rect("List", _safe);
        _dynamic.Add(list.gameObject);
        SpaceTheme.Stretch(list, SIDE, FOOTER_H, SIDE, LIST_TOP);
        var viewport = SpaceTheme.Rect("Viewport", list);
        SpaceTheme.Stretch(viewport);
        SpaceTheme.HitArea(viewport); // カードの間の余白でもドラッグできるように
        viewport.gameObject.AddComponent<RectMask2D>();
        _content = SpaceTheme.Rect("Content", viewport);
        _content.anchorMin = new Vector2(0f, 1f);
        _content.anchorMax = new Vector2(1f, 1f);
        _content.pivot = new Vector2(0.5f, 1f);
        _content.anchoredPosition = Vector2.zero;
        _scroll = list.gameObject.AddComponent<ScrollRect>();
        _scroll.viewport = viewport;
        _scroll.content = _content;
        _scroll.horizontal = false;
        _scroll.movementType = ScrollRect.MovementType.Elastic;
        _scroll.scrollSensitivity = 30f;

        const float PAD = 6f;
        var now = MinionStore.NowUtc;
        string today = MinionStore.TodayKey;
        for (int i = 0; i < book.Items.Count; i++)
            BuildCard(book, book.Items[i], -(PAD + i * (CARD_H + CARD_GAP)), now, today, i);
        float h = PAD * 2f + book.Items.Count * CARD_H + Mathf.Max(0, book.Items.Count - 1) * CARD_GAP;
        _content.sizeDelta = new Vector2(0f, h);
        _scroll.vertical = h > _viewH + 1f; // 収まるときはスクロールさせない
    }

    private void BuildCard(MinionBook book, MinionData m, float top, DateTime now, string today, int index)
    {
        var state = MinionRules.StateOf(m, now);
        var color = MinionArt.ColorOf(m.Element);
        bool back = state == MinionState.Back;
        var card = SpaceTheme.Rect("Minion_" + m.Id, _content, new Vector2(0.5f, 1f), new Vector2(0f, top - CARD_H * 0.5f),
            new Vector2(_cw, CARD_H));
        var cardFace = SpaceTheme.Glass(card, 40f, CARD_BG, SpaceTheme.A(back ? ACCENT : color, back ? 0.75f : 0.35f), back);
        var view = new CardView { Id = m.Id, State = state, Phase = index * 1.3f };
        if (m.Sent.HasValue)
        {
            view.SentAt = m.Sent.Value;
            view.BackAt = MinionRules.BackAt(m);
            view.EatAt = MinionRules.EatAt(m);
        }

        // 左: ミニオンの絵（後ろに属性の色の光。左下に属性の印）
        var tl = new Vector2(0f, 1f);
        var glow = SpaceTheme.Rect("Glow", card, tl, new Vector2(PIC_W * 0.5f + 6f, -CARD_H * 0.5f), new Vector2(330f, 330f));
        view.Glow = SpaceTheme.Img(glow, SpaceSprites.Glow, SpaceTheme.A(color, 0.32f));
        var pic = SpaceTheme.Rect("Picture", card, tl, new Vector2(PIC_W * 0.5f + 6f, -CARD_H * 0.5f - 4f), new Vector2(280f, 280f));
        var img = SpaceTheme.Img(pic, MinionArt.Sprite(m), state == MinionState.Traveling ? new Color(1f, 1f, 1f, 0.35f) : Color.white);
        img.preserveAspect = true;
        var el = SpaceTheme.Rect("Element", card, tl, new Vector2(52f, -CARD_H + 56f), new Vector2(72f, 72f));
        var elImg = SpaceTheme.Img(el, MinionArt.ElementIcon(m.Element), Color.white);
        elImg.preserveAspect = true;
        if (back)
        {
            // 帰ってきた:「!」の吹き出し（弾む）
            var bubble = SpaceTheme.Rect("Bubble", card, tl, new Vector2(PIC_W - 44f, -58f), new Vector2(76f, 76f));
            SpaceTheme.Img(bubble, SpaceSprites.Circle, SpaceTheme.GOLD);
            var mark = SpaceTheme.Text(bubble, "Mark", "!", 52, SpaceTheme.TEXT_DARK, FontStyles.Bold);
            SpaceTheme.Stretch(mark.rectTransform);
            SpaceTextScaler.Exclude(mark);
            view.Bubble = bubble;
            string id = m.Id;
            SpaceTheme.Clickable(card.gameObject, cardFace, () => OnTalk(id), false);
        }

        // 右: 名前・Lv.・成功率とレアの割合・今の様子
        float x0 = PIC_W + 24f, rw = _cw - x0 - 32f;
        var name = SpaceTheme.Text(card, "Name", MinionStore.NameOf(m, book), 40, SpaceTheme.TEXT, tl, new Vector2(x0, -52f),
            new Vector2(rw, 52f), FontStyles.Bold, TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
        name.enableAutoSizing = true;
        name.fontSizeMin = 26f;
        name.fontSizeMax = 40f;
        bool max = m.Level >= MinionRules.MAX_LEVEL;
        var level = SpaceTheme.Text(card, "Level",
            Colored("Lv." + m.Level, SpaceTheme.GOLD) + Colored(max ? "  MAX" : "  / " + MinionRules.MAX_LEVEL, max ? SpaceTheme.GOLD : SpaceTheme.TEXT_MUTED, 0.7f),
            34, SpaceTheme.TEXT, tl, new Vector2(x0, -104f), new Vector2(rw, 44f), FontStyles.Bold, TextAlignmentOptions.Left,
            new Vector2(0f, 0.5f));
        level.richText = true;
        var track = SpaceTheme.Rect("LevelTrack", card, tl, new Vector2(x0, -138f), new Vector2(rw, 10f), new Vector2(0f, 0.5f));
        SpaceTheme.Rounded(track, new Color(1f, 1f, 1f, 0.10f), 5f);
        float k = (m.Level - 1) / (float)(MinionRules.MAX_LEVEL - 1);
        if (k > 0f)
        {
            var fill = SpaceTheme.Rect("LevelFill", card, tl, new Vector2(x0, -138f), new Vector2(Mathf.Max(10f, rw * k), 10f),
                new Vector2(0f, 0.5f));
            SpaceTheme.Rounded(fill, max ? SpaceTheme.GOLD : color, 5f);
        }

        // 成功率（待機中は今日の幸運で。旅の途中・帰ってきたら、出した日の幸運で）とレアの割合
        int luck = m.Sent.HasValue ? m.Luck : MinionRules.LuckOf(m.Job, m.Element, today);
        int success = Mathf.RoundToInt((float)MinionRules.SuccessChance(m.Level, luck) * 100f);
        int rare = Mathf.RoundToInt((float)MinionRules.RareChance(m.Level) * 100f);
        string successText = T_SUCCESS + Colored(success + "%", luck > 0 ? ACCENT : SpaceTheme.TEXT)
                             + (luck > 0 ? Colored(T_LUCK_TAG + (luck * 5) + "%）", ACCENT, 0.8f) : "");
        var stats = SpaceTheme.Text(card, "Stats", successText + "　" + T_RARE + Colored(rare + "%", SpaceTheme.GOLD), 27,
            SpaceTheme.TEXT_SUB, tl, new Vector2(x0, -180f), new Vector2(rw, 40f), FontStyles.Bold, TextAlignmentOptions.Left,
            new Vector2(0f, 0.5f));
        stats.richText = true;
        stats.enableAutoSizing = true;
        stats.fontSizeMin = 18f;
        stats.fontSizeMax = 27f;

        // 下: 今の様子とボタン
        const float ACTION_Y = -262f, BTN_W = 270f, BTN_H = 92f;
        float textW = rw - BTN_W - 16f;
        switch (state)
        {
            case MinionState.Idle:
            case MinionState.Expired: // 1週間すぎていた（旅に出すときに片づける。ふつうは開いたときに片づけてある）
            {
                var status = SpaceTheme.Text(card, "Status", T_IDLE, 30, SpaceTheme.TEXT_SUB, tl, new Vector2(x0, ACTION_Y),
                    new Vector2(textW, 44f), FontStyles.Bold, TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
                string id = m.Id;
                ActionButton(card, view, new Vector2(_cw - 32f - BTN_W * 0.5f, ACTION_Y), new Vector2(BTN_W, BTN_H), T_SEND,
                    SpaceTheme.Hex(0xFFE3A3), SpaceTheme.Hex(0xE09A3E), SpaceTheme.GOLD, () => OnSend(id));
                break;
            }
            case MinionState.Traveling:
            {
                // 管理者だけ、右に「すぐ帰還」（2026-11-14 ユーザー指定「管理者だけ、即時帰還させて報酬を貰えるように」）。
                // そのぶん、残り時間と旅の進み具合は左の幅（ほかの様子の文字と同じ幅）に縦に並べる
                bool admin = AdminClaim.IsAdmin;
                float w = admin ? textW : rw;
                var status = SpaceTheme.Text(card, "Status", T_TRAVELING, 30, color, tl, new Vector2(x0, ACTION_Y + (admin ? 24f : 16f)),
                    new Vector2(admin ? w : rw * 0.5f, 44f), FontStyles.Bold, TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
                if (admin)
                {
                    view.Remain = SpaceTheme.Text(card, "Remain", "", 24, SpaceTheme.TEXT, tl, new Vector2(x0, ACTION_Y - 10f),
                        new Vector2(w, 34f), FontStyles.Bold, TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
                    view.Remain.enableAutoSizing = true;
                    view.Remain.fontSizeMin = 16f;
                    view.Remain.fontSizeMax = 24f;
                }
                else
                    view.Remain = SpaceTheme.Text(card, "Remain", "", 28, SpaceTheme.TEXT, tl, new Vector2(x0 + rw, ACTION_Y + 16f),
                        new Vector2(rw * 0.6f, 44f), FontStyles.Bold, TextAlignmentOptions.Right, new Vector2(1f, 0.5f));
                float barY = admin ? ACTION_Y - 42f : ACTION_Y - 26f;
                var trip = SpaceTheme.Rect("TripTrack", card, tl, new Vector2(x0, barY), new Vector2(w, 14f), new Vector2(0f, 0.5f));
                SpaceTheme.Rounded(trip, new Color(1f, 1f, 1f, 0.10f), 7f);
                var tripFill = SpaceTheme.Rect("TripFill", card, tl, new Vector2(x0, barY), new Vector2(14f, 14f),
                    new Vector2(0f, 0.5f));
                view.TripFill = SpaceTheme.Rounded(tripFill, color, 7f);
                view.TripW = w;
                if (admin)
                {
                    string id = m.Id;
                    InstantButton(card, view, new Vector2(_cw - 32f - BTN_W * 0.5f, ACTION_Y), new Vector2(BTN_W, BTN_H),
                        MinionStore.InStore, () => OnTalk(id, true));
                }
                break;
            }
            case MinionState.Back:
            {
                var status = SpaceTheme.Text(card, "Status", T_BACK, 32, ACCENT, tl, new Vector2(x0, ACTION_Y + 22f),
                    new Vector2(textW, 44f), FontStyles.Bold, TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
                // 1週間の期限（1行。入りきらなければ縮める）
                view.Keep = SpaceTheme.Text(card, "Keep", "", 21, SpaceTheme.TEXT_SUB, tl, new Vector2(x0, ACTION_Y - 22f),
                    new Vector2(textW, 36f), FontStyles.Bold, TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
                view.Keep.enableAutoSizing = true;
                view.Keep.fontSizeMin = 14f;
                view.Keep.fontSizeMax = 21f;
                string id = m.Id;
                bool inStore = MinionStore.InStore; // 受け取れるのは入店中だけ（入店していなければ淡く・下に「入店中だけ」）
                ActionButton(card, view, new Vector2(_cw - 32f - BTN_W * 0.5f, ACTION_Y), new Vector2(BTN_W, BTN_H), T_TALK,
                    ACCENT_LIGHT, SpaceTheme.Hex(0x2FD39A), ACCENT, () => OnTalk(id), inStore ? null : T_ONLY_IN_STORE);
                break;
            }
        }
        _cards.Add(view);
    }

    /// <summary>入店していないときのボタンの淡さ（押すと「入店中だけ」の知らせが出るので、押せるまま）</summary>
    private const float OUT_OF_STORE_ALPHA = 0.45f;

    /// <param name="sub">下に小さく出す2行目（入店していないときの「入店中だけ」。null なら1行で、押せる見た目）</param>
    private static void ActionButton(RectTransform card, CardView view, Vector2 pos, Vector2 size, string label, Color top,
        Color bottom, Color glow, Action onClick, string sub = null)
    {
        var btn = SpaceTheme.Rect("Action", card, new Vector2(0f, 1f), pos, size);
        var face = SpaceTheme.GradientFace(btn, top, bottom, glow, size.y * 0.5f);
        SpaceTheme.Clickable(btn.gameObject, face, () => onClick());
        view.ActionGroup = btn.gameObject.AddComponent<CanvasGroup>();
        if (sub == null)
        {
            view.ActionLabel = SpaceTheme.Text(btn, "Label", label, 36, SpaceTheme.TEXT_DARK, FontStyles.Bold);
            SpaceTheme.Stretch(view.ActionLabel.rectTransform, 12f, 0f, 12f, 0f);
            view.ActionLabel.enableAutoSizing = true;
            view.ActionLabel.fontSizeMin = 22f;
            view.ActionLabel.fontSizeMax = 36f;
            return;
        }
        view.ActionGroup.alpha = OUT_OF_STORE_ALPHA;
        TwoLineLabel(btn, view, size, label, 32f, sub);
    }

    /// <summary>
    /// 管理者だけの「すぐ帰還」（2026-11-14）: 旅の途中のミニオンを24時間待たずに帰らせて、そのまま持ち帰った物を受け取る。
    /// ほかのボタンと見分けがつくよう、すみれ色で下に「管理者だけ」（入店していなければ淡くして「入店中だけ」）
    /// </summary>
    private static void InstantButton(RectTransform card, CardView view, Vector2 pos, Vector2 size, bool inStore, Action onClick)
    {
        var btn = SpaceTheme.Rect("Instant", card, new Vector2(0f, 1f), pos, size);
        var face = SpaceTheme.GradientFace(btn, SpaceTheme.Hex(0xE4D9FF), SpaceTheme.Hex(0x9B7BFF), SpaceTheme.VIOLET, size.y * 0.5f);
        SpaceTheme.Clickable(btn.gameObject, face, () => onClick());
        view.ActionGroup = btn.gameObject.AddComponent<CanvasGroup>();
        if (!inStore) view.ActionGroup.alpha = OUT_OF_STORE_ALPHA;
        TwoLineLabel(btn, view, size, T_INSTANT, 32f, inStore ? T_INSTANT_SUB : T_ONLY_IN_STORE);
    }

    /// <summary>ボタンの2行の文字（上に大きく label、下に小さく sub。文字をいちばん大きくする設定（1.3倍）でも重ならない間にしてある）</summary>
    private static void TwoLineLabel(RectTransform btn, CardView view, Vector2 size, string label, float labelSize, string sub)
    {
        view.ActionLabel = SpaceTheme.Text(btn, "Label", label, labelSize, SpaceTheme.TEXT_DARK, SpaceTheme.CENTER, new Vector2(0f, 13f),
            new Vector2(size.x - 24f, 40f), FontStyles.Bold);
        view.ActionLabel.enableAutoSizing = true;
        view.ActionLabel.fontSizeMin = 22f;
        view.ActionLabel.fontSizeMax = labelSize;
        var subText = SpaceTheme.Text(btn, "Sub", sub, 18, SpaceTheme.A(SpaceTheme.TEXT_DARK, 0.72f), SpaceTheme.CENTER,
            new Vector2(0f, -25f), new Vector2(size.x - 24f, 26f), FontStyles.Bold);
        subText.enableAutoSizing = true;
        subText.fontSizeMin = 14f;
        subText.fontSizeMax = 18f;
    }

    /// <summary>下: 決まりの一言と「新しいミニオンを作る」（作れないときは、どうすれば作れるか）</summary>
    private void BuildFooter(MinionBook book)
    {
        _footer = SpaceTheme.Rect("Footer", _safe, new Vector2(0.5f, 0f), new Vector2(0f, FOOTER_H * 0.5f), new Vector2(_cw, FOOTER_H));
        _dynamic.Add(_footer.gameObject);
        var rules = SpaceTheme.Text(_footer, "Rules", T_RULES, 21, SpaceTheme.A(SpaceTheme.TEXT_SUB, 0.85f), new Vector2(0.5f, 1f),
            new Vector2(0f, -26f), new Vector2(_cw, 34f), FontStyles.Bold);
        rules.enableAutoSizing = true;
        rules.fontSizeMin = 15f;
        rules.fontSizeMax = 21f;
        if (book.CanCreate)
        {
            var create = SpaceTheme.Rect("Create", _footer, new Vector2(0.5f, 0f), new Vector2(0f, 40f + 56f),
                new Vector2(Mathf.Min(640f, _cw), 112f));
            var face = SpaceTheme.PrimaryFace(create);
            SpaceTheme.Clickable(create.gameObject, face, OpenSheet);
            var label = SpaceTheme.Text(create, "Label", T_CREATE, 38, SpaceTheme.TEXT_DARK, FontStyles.Bold);
            SpaceTheme.Stretch(label.rectTransform, 16f, 0f, 16f, 0f);
            label.enableAutoSizing = true;
            label.fontSizeMin = 24f;
            label.fontSizeMax = 38f;
        }
        else
        {
            // 作れる数にお店からもらったミニオンも数えるので、みんな Lv.150 になったときに作れる（もう作れないこともある）
            int growing = book.Growing;
            string text = book.CreateBlocked ? T_CREATE_BLOCKED
                : growing >= 2 ? T_CREATE_LATER_A + growing + T_CREATE_LATER_B : T_CREATE_LATER;
            var note = SpaceTheme.Text(_footer, "Note", text, 25, SpaceTheme.TEXT_MUTED, new Vector2(0.5f, 0f),
                new Vector2(0f, 40f + 50f), new Vector2(_cw, 80f), FontStyles.Bold);
            note.textWrappingMode = TextWrappingModes.Normal;
        }
    }

    /// <summary>残り時間・旅の進み具合・1週間の期限の文字を今の時刻に合わせる</summary>
    private void Tick()
    {
        var now = MinionStore.NowUtc;
        foreach (var c in _cards)
        {
            if (c.State == MinionState.Traveling && c.Remain != null)
            {
                var left = c.BackAt - now;
                c.Remain.text = left.TotalMinutes < 1 ? T_SOON : T_REMAIN_A + FormatHm(left);
                float k = Mathf.Clamp01((float)((now - c.SentAt).TotalSeconds / (MinionRules.TRIP_HOURS * 3600.0)));
                if (c.TripFill != null)
                    c.TripFill.rectTransform.sizeDelta = new Vector2(Mathf.Max(14f, c.TripW * k), 14f);
            }
            else if (c.State == MinionState.Back && c.Keep != null)
            {
                var left = c.EatAt - now;
                if (left.TotalHours < 12) c.Keep.text = T_KEEP_SOON;
                else if (left.TotalDays >= 1) c.Keep.text = T_KEEP_A + (int)Math.Floor(left.TotalDays) + T_KEEP_DAYS;
                else c.Keep.text = T_KEEP_A + (int)Math.Floor(left.TotalHours) + T_KEEP_HOURS;
                c.Keep.color = left.TotalDays < 1 ? SpaceTheme.DANGER : SpaceTheme.TEXT_SUB;
            }
        }
    }

    private static string FormatHm(TimeSpan t)
    {
        int h = (int)t.TotalHours, m = Math.Max(0, t.Minutes);
        return h > 0 ? h + T_HOURS + m + T_MINUTES : Math.Max(1, m) + T_MINUTES;
    }

    private static string Colored(string text, Color color, float size = 1f)
    {
        string s = $"<color=#{ColorUtility.ToHtmlStringRGB(color)}>{text}</color>";
        return size < 0.999f ? $"<size={Mathf.RoundToInt(size * 100f)}%>{s}</size>" : s;
    }

    // ================================================================
    // 開いたとき: サーバーの時刻に合わせ、1週間すぎたミニオンを片づける
    // ================================================================
    private async UniTaskVoid SyncRoutine()
    {
        MinionStore.Result r;
        try
        {
            r = await MinionStore.SyncAsync();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[SpaceMinions] 時刻を合わせられませんでした: {ex.Message}");
            return;
        }
        if (this == null || _closing) return;
        if (r != null && r.Ok && r.Ate.Count > 0) SpaceNotice.MinionsAte(r.Ate);
        if (!_busy && _sheet == null) Refresh();
    }

    // ================================================================
    // 旅に出す・話しかける
    // ================================================================
    private void OnSend(string id)
    {
        if (_busy || _closing) return;
        SendRoutine(id).Forget();
    }

    private async UniTaskVoid SendRoutine(string id)
    {
        _busy = true;
        var view = _cards.Find(c => c.Id == id);
        if (view?.ActionLabel != null) view.ActionLabel.text = T_SENDING;
        if (view?.ActionGroup != null) view.ActionGroup.alpha = 0.6f;
        MinionStore.Result r;
        try
        {
            r = await MinionStore.SendAsync(id);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[SpaceMinions] 旅に出せませんでした: {ex.Message}");
            r = new MinionStore.Result { Error = MinionStore.T_ERR_NETWORK };
        }
        if (this == null) return;
        _busy = false;
        if (r == null || !r.Ok)
        {
            SpaceTheme.Toast(r?.Error ?? MinionStore.T_ERR_NETWORK, _canvas);
            Refresh();
            return;
        }
        AssetsDatabase.instance?.PlaySetSE();
        if (r.Ate.Count > 0) SpaceNotice.MinionsAte(r.Ate);
        SpaceTheme.Toast(SpaceTheme.SafeNow(r.Name + T_SENT_B + BackText(r.Minion) + T_SENT_C), _canvas);
        Refresh();
    }

    /// <summary>帰ってくる時刻（この端末の時計で「明日 14:05 」のように）</summary>
    private static string BackText(MinionData m)
    {
        if (m == null || !m.Sent.HasValue) return "";
        var deviceUtc = MinionRules.BackAt(m) - (MinionStore.NowUtc - DateTime.UtcNow);
        var local = DateTime.SpecifyKind(deviceUtc, DateTimeKind.Utc).ToLocalTime();
        string day = local.Date == DateTime.Now.Date ? T_TODAY : local.Date == DateTime.Now.Date.AddDays(1) ? T_TOMORROW : "";
        return day + local.ToString("H:mm", CultureInfo.InvariantCulture) + " ";
    }

    /// <param name="instant">管理者の「すぐ帰還」（旅の途中でも帰らせて、そのまま受け取る）</param>
    private void OnTalk(string id, bool instant = false)
    {
        if (_busy || _closing) return;
        if (!MinionStore.InStore) // 受け取れるのは入店中だけ（2026-11-14 ユーザー指定。管理者のすぐ帰還も）
        {
            SpaceTheme.Toast(MinionStore.T_ERR_NOT_IN_STORE, _canvas);
            return;
        }
        TalkRoutine(id, instant).Forget();
    }

    private async UniTaskVoid TalkRoutine(string id, bool instant)
    {
        _busy = true;
        var view = _cards.Find(c => c.Id == id);
        if (view?.ActionLabel != null) view.ActionLabel.text = T_TALKING;
        if (view?.ActionGroup != null) view.ActionGroup.alpha = 0.6f;
        MinionStore.Result r;
        try
        {
            r = await MinionStore.TalkAsync(id, instant);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[SpaceMinions] 話しかけられませんでした: {ex.Message}");
            r = new MinionStore.Result { Error = MinionStore.T_ERR_NETWORK };
        }
        if (this == null) return;
        _busy = false;
        if (r == null || !r.Ok)
        {
            SpaceTheme.Toast(r?.Error ?? MinionStore.T_ERR_NETWORK, _canvas);
            // トランプの手札がいっぱいで受け取れなかった: 先に捨てるカードを選んでもらう
            if (r != null && r.CardsMap != null && CardDeck.MustDiscard) SpaceNotice.CardDiscard();
            Refresh();
            return;
        }
        Refresh();
        if (r.Expired)
        {
            // 話しかけようとしたら1週間すぎていた: 持ち帰った物があれば食べてしまった知らせ。なければ（失敗した旅）ひとこと
            if (SpaceNotice.MinionsAte(r.Ate) == null) SpaceTheme.Toast(T_EXPIRED_EMPTY, _canvas);
            return;
        }
        if (r.TripOk) AssetsDatabase.instance?.PlayLevelUpSE();
        else AssetsDatabase.instance?.PlaySetSE();
        ShowTalkResult(r).Forget();
    }

    /// <summary>
    /// 話しかけた結果: 持ち帰った物（SpaceNotice.MinionBack）→ トランプならめくって見せる（8枚なら捨てる）→
    /// 星のカケラが5個そろったら流れ星（ショップ・コインプッシャーと同じ）
    /// </summary>
    private async UniTaskVoid ShowTalkResult(MinionStore.Result r)
    {
        var steps = new List<Func<GameObject>> { () => SpaceNotice.MinionBack(r) };
        if (r.Cards.Count > 0)
        {
            var cards = new List<int>(r.Cards);
            bool over = r.CardOverflow;
            steps.Add(() => SpaceNotice.CardDrawn(cards, T_CAPTION, over, ACCENT));
            if (over) steps.Add(() => CardDeck.MustDiscard ? SpaceNotice.CardDiscard() : null);
        }
        bool stars = r.StarFragments >= SpaceWorld.STAR_FRAGMENTS_PER_STAR;
        bool done = false;
        SpaceNoticeQueue.Run(steps, () => done = true);
        if (!stars) return;
        // 星のカケラが5個そろった: 流れ星を引く（書き込み）→ お知らせを閉じたら演出
        CoinPusher.StarExchange ex = null;
        try
        {
            ex = await CoinPusher.ExchangeStarFragmentsAsync();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SpaceMinions] 星のカケラを流れ星にできませんでした: {e.Message}");
        }
        await UniTask.WaitUntil(() => done);
        if (ex == null) return;
        var star = new List<Func<GameObject>>();
        int left = ex.Left;
        star.Add(() => SpaceNotice.StarFragmentGot(T_TITLE, left, true, T_CAPTION, T_STARS_GOT, ACCENT));
        foreach (var p in ex.Star.Prizes)
        {
            var prize = p;
            star.Add(() => SpaceGachaMeteor.Play(prize));
        }
        foreach (var pid in ex.Star.Unlocked)
        {
            var def = SpacePlanets.Find(pid);
            if (def != null) star.Add(() => SpaceNotice.PlanetUnlocked(def));
        }
        SpaceNoticeQueue.Run(star, () =>
        {
            if (this != null && !_closing) Refresh();
        });
    }

    // ================================================================
    // 作る（職業と属性を選ぶシート）
    // ================================================================
    private int _selJob, _selElement;
    private Image _preview;
    private Image _previewGlow;
    private TextMeshProUGUI _previewName, _sheetOkLabel;
    private readonly GameObject[] _jobRings = new GameObject[MinionRules.JOBS];
    private readonly Image[] _jobImages = new Image[MinionRules.JOBS];
    private readonly GameObject[] _elementRings = new GameObject[MinionRules.ELEMENTS];

    private void OpenSheet()
    {
        if (_busy || _sheet != null) return;
        var book = MinionStore.Book;
        if (!book.CanCreate)
        {
            SpaceTheme.Toast(book.CreateBlocked ? MinionStore.T_ERR_LIMIT_BLOCKED : MinionStore.T_ERR_LIMIT, _canvas);
            return;
        }
        // はじめは今日の幸運の組み合わせを選んでおく
        MinionRules.Lucky(MinionStore.TodayKey, out _selJob, out _selElement);

        var root = SpaceTheme.Rect("Sheet", transform);
        SpaceTheme.Stretch(root);
        _sheet = root.gameObject;
        var dim = SpaceTheme.Rect("Dim", root);
        SpaceTheme.Stretch(dim);
        var dimImg = SpaceTheme.Img(dim, null, SpaceTheme.DIM, true);
        SpaceTheme.Clickable(dim.gameObject, dimImg, () =>
        {
            if (!_busy) CloseSheet();
        }, false);

        const float W = 920f, TILE = 200f, TILE_GAP = 16f;
        float rowW = TILE * 4f + TILE_GAP * 3f;
        var card = SpaceTheme.Rect("Card", root, SpaceTheme.CENTER, Vector2.zero, new Vector2(W, 1400f));
        SpaceTheme.Glass(card, 56f, SpaceTheme.GLASS_DEEP, SpaceTheme.A(ACCENT, 0.45f), true);
        var top = new Vector2(0.5f, 1f);
        float y = 52f;
        var title = SpaceTheme.Text(card, "Title", T_SHEET_TITLE, 44, SpaceTheme.TEXT, top, new Vector2(0f, -(y + 30f)),
            new Vector2(W - 80f, 60f), FontStyles.Bold);
        y += 70f;

        // 選んでいるミニオン（大きく）と名前
        var glow = SpaceTheme.Rect("PreviewGlow", card, top, new Vector2(0f, -(y + 150f)), new Vector2(420f, 420f));
        _previewGlow = SpaceTheme.Img(glow, SpaceSprites.Glow, Color.white);
        var pic = SpaceTheme.Rect("Preview", card, top, new Vector2(0f, -(y + 150f)), new Vector2(300f, 300f));
        _preview = SpaceTheme.Img(pic, null, Color.white);
        _preview.preserveAspect = true;
        y += 300f;
        _previewName = SpaceTheme.Text(card, "PreviewName", "", 40, SpaceTheme.TEXT, top, new Vector2(0f, -(y + 28f)),
            new Vector2(W - 80f, 56f), FontStyles.Bold);
        y += 72f;

        // 職業
        var jobLabel = SpaceTheme.Text(card, "JobLabel", T_SHEET_JOB, 26, SpaceTheme.A(ACCENT, 0.95f), top,
            new Vector2(-rowW * 0.5f, -(y + 18f)), new Vector2(rowW, 36f), FontStyles.Bold, TextAlignmentOptions.Left,
            new Vector2(0f, 0.5f));
        y += 44f;
        for (int i = 0; i < MinionRules.JOBS; i++)
        {
            int job = i;
            var tile = SpaceTheme.Rect("Job" + i, card, top, new Vector2(-rowW * 0.5f + TILE * 0.5f + i * (TILE + TILE_GAP), -(y + 115f)),
                new Vector2(TILE, 230f));
            var face = SpaceTheme.Glass(tile, 28f, BUTTON_DARK, SpaceTheme.A(ACCENT, 0.25f), true);
            SpaceTheme.Clickable(tile.gameObject, face, () => SelectJob(job));
            var ring = SpaceTheme.Rect("Selected", tile);
            SpaceTheme.Stretch(ring, -5f, -5f, -5f, -5f);
            SpaceTheme.RoundedStroke(ring, ACCENT_LIGHT, 33f);
            _jobRings[i] = ring.gameObject;
            var lp = SpaceTheme.Rect("Picture", tile, new Vector2(0.5f, 1f), new Vector2(0f, -88f), new Vector2(166f, 166f));
            _jobImages[i] = SpaceTheme.Img(lp, null, Color.white);
            _jobImages[i].preserveAspect = true;
            var ln = SpaceTheme.Text(tile, "Name", MinionRules.JOB_NAMES[i], 26, SpaceTheme.TEXT, new Vector2(0.5f, 0f),
                new Vector2(0f, 26f), new Vector2(TILE - 8f, 36f), FontStyles.Bold);
            ln.enableAutoSizing = true;
            ln.fontSizeMin = 18f;
            ln.fontSizeMax = 26f;
        }
        y += 230f + 26f;

        // 属性
        var elementLabel = SpaceTheme.Text(card, "ElementLabel", T_SHEET_ELEMENT, 26, SpaceTheme.A(ACCENT, 0.95f), top,
            new Vector2(-rowW * 0.5f, -(y + 18f)), new Vector2(rowW, 36f), FontStyles.Bold, TextAlignmentOptions.Left,
            new Vector2(0f, 0.5f));
        y += 44f;
        for (int i = 0; i < MinionRules.ELEMENTS; i++)
        {
            int element = i;
            var tile = SpaceTheme.Rect("Element" + i, card, top, new Vector2(-rowW * 0.5f + TILE * 0.5f + i * (TILE + TILE_GAP), -(y + 80f)),
                new Vector2(TILE, 160f));
            var face = SpaceTheme.Glass(tile, 28f, BUTTON_DARK, SpaceTheme.A(MinionArt.ColorOf(i), 0.35f), true);
            SpaceTheme.Clickable(tile.gameObject, face, () => SelectElement(element));
            var ring = SpaceTheme.Rect("Selected", tile);
            SpaceTheme.Stretch(ring, -5f, -5f, -5f, -5f);
            SpaceTheme.RoundedStroke(ring, ACCENT_LIGHT, 33f);
            _elementRings[i] = ring.gameObject;
            var ic = SpaceTheme.Rect("Icon", tile, new Vector2(0.5f, 1f), new Vector2(0f, -58f), new Vector2(88f, 88f));
            var icImg = SpaceTheme.Img(ic, MinionArt.ElementIcon(i), Color.white);
            icImg.preserveAspect = true;
            SpaceTheme.Text(tile, "Name", MinionRules.ELEMENT_NAMES[i], 28, MinionArt.ColorOf(i), new Vector2(0.5f, 0f),
                new Vector2(0f, 26f), new Vector2(TILE - 8f, 38f), FontStyles.Bold);
        }
        y += 160f + 22f;

        // 今日の幸運
        MinionRules.Lucky(MinionStore.TodayKey, out int ll, out int le);
        var luck = SpaceTheme.Text(card, "Luck",
            T_SHEET_LUCK_A + MinionRules.NameOf(ll, le) + T_SHEET_LUCK_B, 23, SpaceTheme.TEXT_SUB, top, new Vector2(0f, -(y + 20f)),
            new Vector2(W - 80f, 40f), FontStyles.Bold);
        luck.enableAutoSizing = true;
        luck.fontSizeMin = 16f;
        luck.fontSizeMax = 23f;
        y += 54f;

        // やめる・このミニオンを作る
        var cancel = SpaceTheme.Rect("Cancel", card, top, new Vector2(-W * 0.5f + 40f + 150f, -(y + 64f)), new Vector2(300f, 120f));
        var cancelFace = SpaceTheme.Glass(cancel, 46f, SpaceTheme.A(new Color(0.09f, 0.12f, 0.30f), 0.63f), null, true);
        SpaceTheme.Clickable(cancel.gameObject, cancelFace, () =>
        {
            if (!_busy) CloseSheet();
        });
        var cancelText = SpaceTheme.Text(cancel, "Label", T_SHEET_CANCEL, 36, SpaceTheme.TEXT_SUB, FontStyles.Bold);
        SpaceTheme.Stretch(cancelText.rectTransform);
        float okW = W - 80f - 300f - 24f;
        var ok = SpaceTheme.Rect("Ok", card, top, new Vector2(W * 0.5f - 40f - okW * 0.5f, -(y + 64f)), new Vector2(okW, 120f));
        var okFace = SpaceTheme.PrimaryFace(ok);
        SpaceTheme.Clickable(ok.gameObject, okFace, OnCreatePressed);
        _sheetOkLabel = SpaceTheme.Text(ok, "Label", T_SHEET_OK, 36, SpaceTheme.TEXT_DARK, FontStyles.Bold);
        SpaceTheme.Stretch(_sheetOkLabel.rectTransform, 16f, 0f, 16f, 0f);
        _sheetOkLabel.enableAutoSizing = true;
        _sheetOkLabel.fontSizeMin = 22f;
        _sheetOkLabel.fontSizeMax = 36f;
        y += 128f + 48f;

        card.sizeDelta = new Vector2(W, y);
        // 画面の高さに収まらない端末（iPad の横幅の広い比率など）では、まるごと縮める
        var size = SpaceTheme.CanvasSize(_canvas);
        var insets = SpaceTheme.SafeInsets(size);
        float avail = size.y - insets.top - insets.bottom - 40f;
        if (y > avail) card.localScale = Vector3.one * (avail / y);
        root.gameObject.AddComponent<SpacePopIn>().Target = card;
        UpdateSheet();
    }

    private void SelectJob(int job)
    {
        if (_busy) return;
        _selJob = job;
        UpdateSheet();
    }

    private void SelectElement(int element)
    {
        if (_busy) return;
        _selElement = element;
        UpdateSheet();
    }

    private void UpdateSheet()
    {
        if (_sheet == null) return;
        var color = MinionArt.ColorOf(_selElement);
        _preview.sprite = MinionArt.Sprite(_selJob, _selElement);
        _previewGlow.color = SpaceTheme.A(color, 0.4f);
        _previewName.text = MinionRules.NameOf(_selJob, _selElement);
        _previewName.color = color;
        for (int i = 0; i < MinionRules.JOBS; i++)
        {
            _jobRings[i].SetActive(i == _selJob);
            _jobImages[i].sprite = MinionArt.Sprite(i, _selElement);
        }
        for (int i = 0; i < MinionRules.ELEMENTS; i++) _elementRings[i].SetActive(i == _selElement);
    }

    private void CloseSheet()
    {
        if (_sheet != null) Destroy(_sheet);
        _sheet = null;
    }

    private void OnCreatePressed()
    {
        if (_busy) return;
        CreateRoutine(_selJob, _selElement).Forget();
    }

    private async UniTaskVoid CreateRoutine(int job, int element)
    {
        _busy = true;
        if (_sheetOkLabel != null) _sheetOkLabel.text = T_SHEET_BUSY;
        MinionStore.Result r;
        try
        {
            r = await MinionStore.CreateAsync(job, element);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[SpaceMinions] ミニオンを作れませんでした: {ex.Message}");
            r = new MinionStore.Result { Error = MinionStore.T_ERR_NETWORK };
        }
        if (this == null) return;
        _busy = false;
        if (r == null || !r.Ok)
        {
            if (_sheetOkLabel != null) _sheetOkLabel.text = T_SHEET_OK;
            SpaceTheme.Toast(r?.Error ?? MinionStore.T_ERR_NETWORK, _canvas);
            return;
        }
        CloseSheet();
        AssetsDatabase.instance?.PlayLevelUpSE();
        if (r.Ate.Count > 0) SpaceNotice.MinionsAte(r.Ate);
        SpaceTheme.Toast(SpaceTheme.SafeNow(r.Name + T_JOINED_B), _canvas);
        Refresh();
        // 作ったミニオンが見えるように、いちばん下へ
        if (_scroll != null && _scroll.vertical) _scroll.verticalNormalizedPosition = 0f;
    }

    /// <summary>背景の星（ところどころまたたく。少しミントがかった白）</summary>
    private static void Sky(RectTransform rt)
    {
        var rnd = new System.Random(20261115);
        var layer = SpaceTheme.Rect("Sky", rt);
        SpaceTheme.Stretch(layer);
        var tint = Color.Lerp(Color.white, ACCENT, 0.3f);
        for (int i = 0; i < 60; i++)
        {
            var pos = new Vector2((float)rnd.NextDouble(), (float)rnd.NextDouble());
            float s = 3f + (float)rnd.NextDouble() * 5f;
            var star = SpaceTheme.Rect("S", layer, pos, Vector2.zero, new Vector2(s, s));
            var img = SpaceTheme.Img(star, SpaceSprites.Circle, SpaceTheme.A(tint, 0.12f + (float)rnd.NextDouble() * 0.3f));
            if (i % 3 == 0) star.gameObject.AddComponent<SpaceTwinkle>().Init(img, img.color.a);
        }
    }
}
