using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ポーカー（2026-11-12 ユーザー指定「宝石座が完成されているならば、宝石座をタップすることでポーカーができるように。
/// ポーカーの役によって報酬内容が変わるとして、報酬内容を考えて実装」）。
/// 宝石座が完成した人（管理者は初めから）だけホームの空に宝石座が出て（SpaceBackground）、それをタップすると開く全画面。
///  ・手札（最大7枚）から5枚選ぶ →「この5枚で勝負する」→ 役に応じた報酬（CardRules.Rewards）。使った5枚はなくなる（山札にも戻らない）
///  ・選んだ5枚の役と報酬は、勝負する前から見える（「いちばん強い5枚」で自動で選べる）
///  ・手札が8枚（捨てる前）のとき・山札座で引いて捨てる前は勝負できない（先に捨てるカードを選ぶ）
///  ・山札座が完成していれば、手札の下に「山札座で1枚引いて1枚捨てる」（1日1回。SpaceDeckSwap。2026-11-13）
/// 配色は宝石らしい深いすみれ色（ショップの緑・メニューの紺と分ける）。Android の戻るは NewHomeController が Instance.HandleBack を呼ぶ。
/// </summary>
public class SpacePoker : MonoBehaviour
{
    public const string CONSTELLATION = CardDeck.GEM;

    // ---- 配色（深いすみれ色の地に、桃色と金） ----
    public static readonly Color PINK = SpaceTheme.Hex(0xFF7AC8);
    private static readonly Color LILAC = SpaceTheme.Hex(0xD9C7FF);
    private static readonly Color BG = new Color(0.032f, 0.012f, 0.055f, 0.985f);
    private static readonly Color PANEL = new Color(0.105f, 0.045f, 0.17f, 0.80f);
    private static readonly Color PANEL_ON = new Color(0.30f, 0.16f, 0.30f, 0.85f);
    private static readonly Color BUTTON_DARK = new Color(0.10f, 0.04f, 0.15f, 0.90f);

    private const string T_CAPTION = "POKER";
    private const string T_TITLE = "ポーカー";
    private const string T_INFO = "手札から5枚選んで勝負。役に応じて報酬がもらえます";
    private const string T_BEST = "いちばん強い5枚";
    private const string T_CLEAR = "選び直す";
    private const string T_PLAY = "この5枚で勝負する";
    private const string T_PLAYING = "勝負中…";
    private const string T_SELECT_A = "カードを5枚選んでください（いま ";
    private const string T_SELECT_B = "枚）";
    private const string T_NEED_A = "手札が5枚になると勝負できます（あと ";
    private const string T_NEED_B = "枚）";
    private const string T_HOW = "来店するたびに1枚引けます。ショップでも引けます";
    private const string T_USED = "勝負に使った5枚はなくなります";
    private const string T_REWARD = "報酬　";
    private const string T_MAX = "選べるのは5枚までです";
    private const string T_TABLE = "役と報酬";
    private const string T_CONFIRM_TITLE = "この5枚で勝負しますか？";
    private const string T_CONFIRM_BODY = "勝負に使った5枚のカードはなくなります";
    private const string T_CONFIRM_OK = "勝負する";
    private const string T_LOCKED = "宝石座を完成させると、ポーカーができます";

    /// <summary>この画面で表示しうる文字（NewHome の起動時にまとめてフォントへ登録する）</summary>
    public static string GlyphText
    {
        get
        {
            var sb = new StringBuilder()
                .Append(T_CAPTION).Append(T_TITLE).Append(T_INFO).Append(T_BEST).Append(T_CLEAR).Append(T_PLAY)
                .Append(T_PLAYING).Append(T_SELECT_A).Append(T_SELECT_B).Append(T_NEED_A).Append(T_NEED_B).Append(T_HOW)
                .Append(T_USED).Append(T_REWARD).Append(T_MAX).Append(T_TABLE).Append(T_CONFIRM_TITLE).Append(T_CONFIRM_BODY)
                .Append(T_CONFIRM_OK).Append(T_LOCKED).Append("（）");
            foreach (var r in CardRules.Rewards) sb.Append(r.Name).Append(r.Desc).Append(CardDeck.RewardText(r));
            return sb.ToString();
        }
    }

    // 配置（セーフエリアの中。上からの距離）
    private const float SIDE = 40f;
    private const float MAX_W = 1000f;
    private const float INFO_TOP = 214f;
    private const float HAND_TOP = 316f;
    private const float LIFT = 30f;           // 選んだカードを持ち上げる高さ
    private const float CARD_GAP = 12f;
    private const float ROW_H = 68f;          // 役と報酬の表の1行（16:9 の端末でもスクロールせずに収まる高さ）

    /// <summary>開いている画面（なければ null）</summary>
    public static SpacePoker Instance { get; private set; }

    private sealed class Slot
    {
        public int Code;
        public RectTransform Holder;
        public GameObject Ring;
        public Image Glow;
        public float BaseY;
    }

    private Canvas _canvas;
    private CanvasGroup _group;
    private RectTransform _content, _handLayer, _glowLayer;
    private float _cw, _cardW, _cardH;
    private bool _closing, _busy;
    private float _nextCheck;
    private string _shownHand = "";
    private readonly List<Slot> _slots = new List<Slot>();
    private readonly List<int> _selected = new List<int>();
    private TextMeshProUGUI _summary, _previewTitle, _previewSub, _playLabel, _swapLabel;
    private CanvasGroup _playGroup, _bestGroup, _clearGroup, _swapGroup;
    private readonly Dictionary<PokerHand, Image> _tableRows = new Dictionary<PokerHand, Image>();

    /// <summary>ポーカーを開く。宝石座が完成していなければ開かない（管理者は初めから開ける）</summary>
    public static SpacePoker Open(Canvas canvas = null)
    {
        if (Instance != null) return Instance;
        if (!CardDeck.PokerUnlocked)
        {
            SpaceTheme.Toast(T_LOCKED);
            return null;
        }
        if (canvas == null) canvas = SpaceTheme.MainCanvas();
        if (canvas == null) return null;
        var root = SpaceTheme.Rect("__SpacePoker", canvas.transform);
        SpaceTheme.Stretch(root);
        root.SetAsLastSibling();
        SpaceTheme.SubCanvas(root.gameObject, true); // 光のまたたきをホームの再描画と分ける
        var page = root.gameObject.AddComponent<SpacePoker>();
        Instance = page;
        page.Build(canvas);
        // 手札が8枚（捨てる前）なら、先に捨てるカードを選ぶ
        if (CardDeck.MustDiscard) SpaceNotice.CardDiscard();
        return page;
    }

    /// <summary>戻る操作（Android の戻るボタン）</summary>
    public void HandleBack()
    {
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
        if (_closing || _busy || Time.unscaledTime < _nextCheck) return;
        _nextCheck = Time.unscaledTime + 0.5f;
        // 手札が変わったら（捨てた・入店で引いた・月が変わった・山札座で引いた など）並べ直す
        if (HandKey(CardDeck.Current) != _shownHand) RebuildHand();
    }

    private static string HandKey(CardState s) =>
        s.Month + ":" + string.Join(",", s.Hand) + ":" + s.SwapDay + (s.SwapPending ? "+" : "");

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
        _cw = Mathf.Min(size.x - SIDE * 2f, MAX_W);
        _cardW = Mathf.Min(134f, (_cw - CARD_GAP * (CardRules.HAND_MAX - 1)) / CardRules.HAND_MAX);
        _cardH = _cardW * CardDeck.ASPECT;

        // 背景（下のホームを押させない）: 深いすみれ色に、上から差す桃色の光・下の淡い光・星
        var dim = SpaceTheme.Rect("Dim", rt);
        SpaceTheme.Stretch(dim);
        SpaceTheme.Img(dim, null, BG, true);
        var glow = SpaceTheme.Rect("Glow", rt, new Vector2(0.5f, 1f), new Vector2(0f, -260f), new Vector2(1700f, 1300f));
        SpaceTheme.Img(glow, SpaceSprites.Glow, SpaceTheme.A(PINK, 0.12f));
        var low = SpaceTheme.Rect("GlowLow", rt, new Vector2(0.5f, 0f), Vector2.zero, new Vector2(1700f, 900f));
        SpaceTheme.Img(low, SpaceSprites.Glow, SpaceTheme.A(CardDeck.ACCENT, 0.12f));
        Sky(rt);

        var safe = SpaceTheme.Rect("Safe", rt);
        SpaceTheme.Stretch(safe);
        safe.gameObject.AddComponent<SpaceSafeArea>();

        // 中身はスクロールできるようにしておく（背の低い端末で表がはみ出すとき）
        var viewport = SpaceTheme.Rect("Viewport", safe);
        SpaceTheme.Stretch(viewport);
        SpaceTheme.HitArea(viewport);
        viewport.gameObject.AddComponent<RectMask2D>();
        _content = SpaceTheme.Rect("Content", viewport);
        _content.anchorMin = new Vector2(0f, 1f);
        _content.anchorMax = new Vector2(1f, 1f);
        _content.pivot = new Vector2(0.5f, 1f);
        _content.anchoredPosition = Vector2.zero;
        var scroll = safe.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = _content;
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Elastic;
        scroll.scrollSensitivity = 30f;

        BuildHeader(safe);
        float y = BuildBody();
        _content.sizeDelta = new Vector2(0f, y);
        var insets = SpaceTheme.SafeInsets(size);
        scroll.vertical = y > size.y - insets.top - insets.bottom + 1f;

        RebuildHand();
        StartCoroutine(SpaceTheme.Tween(0.25f, 0f, k => _group.alpha = k));
    }

    private void BuildHeader(RectTransform safe)
    {
        var caption = SpaceTheme.Text(_content, "Caption", T_CAPTION, 24, SpaceTheme.A(PINK, 0.95f),
            new Vector2(0f, 1f), new Vector2(SIDE + 16f, -70f), new Vector2(400f, 36f), FontStyles.Bold,
            TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
        SpaceTheme.Tracking(caption, 18f);
        SpaceTheme.Text(_content, "Title", T_TITLE, 52, SpaceTheme.TEXT, new Vector2(0f, 1f), new Vector2(SIDE + 16f, -126f),
            new Vector2(400f, 66f), FontStyles.Bold, TextAlignmentOptions.Left, new Vector2(0f, 0.5f));

        // 題の右に、この画面をくれた宝石座（いまのレベルの姿。管理者で未完成なら完成した姿）を小さく
        var p = Constellations.Progress().Find(x => x.Def.Id == CONSTELLATION);
        if (p != null)
        {
            var fig = SpaceTheme.Rect("Constellation", _content, new Vector2(0f, 1f), new Vector2(SIDE + 16f + 290f, -104f),
                new Vector2(120f, 120f));
            SpaceConstellationPage.Figure(fig, p.Def, Mathf.Max(p.Def.Size, p.Count - p.Partial), 120f);
        }

        // 右上の閉じる（スクロールしても動かないよう、セーフエリアに直接置く）
        var close = SpaceTheme.Rect("Close", safe, new Vector2(1f, 1f), new Vector2(-(SIDE + 52f), -84f),
            new Vector2(104f, 104f));
        var face = SpaceTheme.Img(close, SpaceSprites.Circle, BUTTON_DARK, true);
        var rim = SpaceTheme.Rect("Rim", close);
        SpaceTheme.Stretch(rim);
        SpaceTheme.Img(rim, SpaceSprites.RingSmall, SpaceTheme.A(PINK, 0.5f));
        SpaceTheme.Bar(close, Vector2.zero, 44f, 6f, 45f, SpaceTheme.TEXT);
        SpaceTheme.Bar(close, Vector2.zero, 44f, 6f, -45f, SpaceTheme.TEXT);
        SpaceTheme.Clickable(close.gameObject, face, Close);
    }

    /// <summary>手札・ボタン・役の見込み・勝負ボタン・役と報酬の表。いちばん下の y を返す</summary>
    private float BuildBody()
    {
        var top = new Vector2(0.5f, 1f);
        _summary = SpaceTheme.Text(_content, "Summary", "", 30, LILAC, top, new Vector2(0f, -INFO_TOP),
            new Vector2(_cw, 42f), FontStyles.Bold);
        var info = SpaceTheme.Text(_content, "Info", T_INFO, 24, SpaceTheme.A(SpaceTheme.TEXT_SUB, 0.9f), top,
            new Vector2(0f, -(INFO_TOP + 44f)), new Vector2(_cw, 36f), FontStyles.Bold);
        info.enableAutoSizing = true;
        info.fontSizeMin = 18f;
        info.fontSizeMax = 24f;

        // 手札の置き場（7枠）
        _handLayer = SpaceTheme.Rect("Hand", _content, top, new Vector2(0f, -HAND_TOP), new Vector2(_cw, LIFT + _cardH + 10f),
            top);
        float rowW = CardRules.HAND_MAX * _cardW + (CardRules.HAND_MAX - 1) * CARD_GAP;
        for (int i = 0; i < CardRules.HAND_MAX; i++)
        {
            var frame = SpaceTheme.Rect("Frame" + i, _handLayer, top,
                new Vector2(-rowW * 0.5f + _cardW * 0.5f + i * (_cardW + CARD_GAP), -(LIFT + _cardH * 0.5f)),
                new Vector2(_cardW, _cardH));
            SpaceTheme.RoundedStroke(frame, SpaceTheme.A(LILAC, 0.18f), _cardW * 0.11f);
        }
        // 選んだカードの後ろの光は、ほかのカードより奥の層にまとめる（隣のカードの上にかぶらないように）
        _glowLayer = SpaceTheme.Rect("Glows", _handLayer);
        SpaceTheme.Stretch(_glowLayer);

        float y = HAND_TOP + LIFT + _cardH + 56f;
        // いちばん強い5枚 / 選び直す
        _bestGroup = SmallButton(new Vector2(-190f, -y), T_BEST, SelectBest);
        _clearGroup = SmallButton(new Vector2(190f, -y), T_CLEAR, ClearSelection);
        y += 42f + 40f;

        // 山札座の「1枚引いて1枚捨てる」（山札座が完成した人。1日1回。2026-11-13）。上の2つのボタンと同じ幅で、縁は山札座の色
        if (CardDeck.SwapUnlocked)
        {
            var swap = SpaceTheme.Rect("Swap", _content, top, new Vector2(0f, -(y + 26f)), new Vector2(730f, 84f));
            var swapFace = SpaceTheme.Glass(swap, 42f, BUTTON_DARK, SpaceTheme.A(SpaceDeckSwap.Tint, 0.7f), true);
            SpaceTheme.Clickable(swap.gameObject, swapFace, () =>
            {
                if (!_busy) SpaceDeckSwap.Start(_canvas);
            });
            _swapLabel = SpaceTheme.Text(swap, "Label", SpaceDeckSwap.T_BUTTON, 30, SpaceTheme.TEXT, FontStyles.Bold);
            SpaceTheme.Stretch(_swapLabel.rectTransform, 12f, 0f, 12f, 0f);
            _swapLabel.enableAutoSizing = true;
            _swapLabel.fontSizeMin = 20f;
            _swapLabel.fontSizeMax = 30f;
            _swapGroup = swap.gameObject.AddComponent<CanvasGroup>();
            y += 26f + 42f + 40f;
        }

        // 役の見込み（選んだ5枚の役と報酬）
        _previewTitle = SpaceTheme.Text(_content, "PreviewTitle", "", 48, SpaceTheme.TEXT, top, new Vector2(0f, -(y + 32f)),
            new Vector2(_cw, 64f), FontStyles.Bold);
        _previewTitle.enableAutoSizing = true;
        _previewTitle.fontSizeMin = 26f;
        _previewTitle.fontSizeMax = 48f;
        _previewSub = SpaceTheme.Text(_content, "PreviewSub", "", 30, SpaceTheme.GOLD, top, new Vector2(0f, -(y + 90f)),
            new Vector2(_cw, 44f), FontStyles.Bold);
        _previewSub.enableAutoSizing = true;
        _previewSub.fontSizeMin = 20f;
        _previewSub.fontSizeMax = 30f;
        y += 130f;

        // この5枚で勝負する
        var play = SpaceTheme.Rect("Play", _content, top, new Vector2(0f, -(y + 66f)), new Vector2(600f, 132f));
        var playFace = SpaceTheme.GradientFace(play, SpaceTheme.Hex(0xFFE3A3), SpaceTheme.Hex(0xE09A3E), SpaceTheme.GOLD);
        SpaceTheme.Clickable(play.gameObject, playFace, OnPlayPressed);
        _playGroup = play.gameObject.AddComponent<CanvasGroup>();
        _playLabel = SpaceTheme.Text(play, "Label", T_PLAY, 40, SpaceTheme.TEXT_DARK, FontStyles.Bold);
        SpaceTheme.Stretch(_playLabel.rectTransform, 16f, 0f, 16f, 0f);
        _playLabel.enableAutoSizing = true;
        _playLabel.fontSizeMin = 26f;
        _playLabel.fontSizeMax = 40f;
        y += 132f + 18f;
        SpaceTheme.Text(_content, "Used", T_USED, 22, SpaceTheme.A(SpaceTheme.TEXT_SUB, 0.75f), top, new Vector2(0f, -(y + 16f)),
            new Vector2(_cw, 32f), FontStyles.Bold);
        y += 32f + 46f;

        // 役と報酬の表（強い役から）
        SpaceTheme.Text(_content, "TableTitle", T_TABLE, 28, SpaceTheme.A(LILAC, 0.85f), new Vector2(0.5f, 1f),
            new Vector2(-_cw * 0.5f + 16f, -(y + 20f)), new Vector2(400f, 40f), FontStyles.Bold, TextAlignmentOptions.Left,
            new Vector2(0f, 0.5f));
        y += 52f;
        var table = SpaceTheme.Rect("Table", _content, top, new Vector2(0f, -y),
            new Vector2(_cw, ROW_H * CardRules.Rewards.Length + 24f), top);
        SpaceTheme.Glass(table, 40f, PANEL, SpaceTheme.A(PINK, 0.28f));
        for (int i = 0; i < CardRules.Rewards.Length; i++)
        {
            var r = CardRules.Rewards[CardRules.Rewards.Length - 1 - i];
            var row = SpaceTheme.Rect("Row_" + r.Hand, table, top, new Vector2(0f, -(12f + ROW_H * (i + 0.5f))),
                new Vector2(_cw - 24f, ROW_H - 6f));
            var bg = SpaceTheme.Rounded(row, PANEL_ON, 28f);
            bg.enabled = false;
            _tableRows[r.Hand] = bg;
            SpaceTheme.Text(row, "Name", r.Name, 28, SpaceTheme.TEXT, new Vector2(0f, 0.5f), new Vector2(24f, 12f),
                new Vector2(560f, 38f), FontStyles.Bold, TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
            var desc = SpaceTheme.Text(row, "Desc", r.Desc, 19, SpaceTheme.A(SpaceTheme.TEXT_SUB, 0.85f), new Vector2(0f, 0.5f),
                new Vector2(24f, -18f), new Vector2(560f, 28f), FontStyles.Bold, TextAlignmentOptions.Left,
                new Vector2(0f, 0.5f));
            desc.enableAutoSizing = true;
            desc.fontSizeMin = 14f;
            desc.fontSizeMax = 19f;
            var reward = SpaceTheme.Text(row, "Reward", CardDeck.RewardText(r), 26,
                r.Hand >= PokerHand.Straight ? SpaceTheme.GOLD : SpaceTheme.A(SpaceTheme.GOLD, 0.85f), new Vector2(1f, 0.5f),
                new Vector2(-24f, 0f), new Vector2(_cw - 24f - 560f - 40f, 40f), FontStyles.Bold,
                TextAlignmentOptions.Right, new Vector2(1f, 0.5f));
            reward.enableAutoSizing = true;
            reward.fontSizeMin = 16f;
            reward.fontSizeMax = 26f;
        }
        y += ROW_H * CardRules.Rewards.Length + 24f + 40f;
        return y;
    }

    private CanvasGroup SmallButton(Vector2 pos, string label, Action onClick)
    {
        var rt = SpaceTheme.Rect("Button_" + label, _content, new Vector2(0.5f, 1f), pos, new Vector2(350f, 84f));
        var face = SpaceTheme.Glass(rt, 42f, BUTTON_DARK, SpaceTheme.A(PINK, 0.45f), true);
        SpaceTheme.Clickable(rt.gameObject, face, () => onClick());
        var t = SpaceTheme.Text(rt, "Label", label, 30, SpaceTheme.TEXT, FontStyles.Bold);
        SpaceTheme.Stretch(t.rectTransform, 12f, 0f, 12f, 0f);
        t.enableAutoSizing = true;
        t.fontSizeMin = 20f;
        t.fontSizeMax = 30f;
        return rt.gameObject.AddComponent<CanvasGroup>();
    }

    /// <summary>手札を今の内容で並べ直す（選んでいたカードは、手札に残っているものだけ選んだまま）</summary>
    private void RebuildHand()
    {
        var s = CardDeck.Current;
        _shownHand = HandKey(s);
        foreach (var slot in _slots)
        {
            if (slot.Holder != null) Destroy(slot.Holder.gameObject);
            if (slot.Glow != null) Destroy(slot.Glow.gameObject);
        }
        _slots.Clear();
        _selected.RemoveAll(c => !s.Hand.Contains(c));

        var top = new Vector2(0.5f, 1f);
        int n = Mathf.Min(s.Hand.Count, CardRules.HAND_MAX + 1);
        float gap = CARD_GAP;
        float w = _cardW;
        if (n > CardRules.HAND_MAX) w = (_cw - gap * (n - 1)) / n; // 捨てる前の8枚は少し小さく
        float h = w * CardDeck.ASPECT;
        float rowW = CardRules.HAND_MAX * _cardW + (CardRules.HAND_MAX - 1) * CARD_GAP;
        if (n > CardRules.HAND_MAX) rowW = n * w + (n - 1) * gap;
        for (int i = 0; i < n; i++)
        {
            int code = s.Hand[i];
            float baseY = -(LIFT + _cardH * 0.5f);
            var holder = SpaceTheme.Rect("Card_" + code, _handLayer, top,
                new Vector2(-rowW * 0.5f + w * 0.5f + i * (w + gap), baseY), new Vector2(w, h));
            var glowRt = SpaceTheme.Rect("Glow_" + code, _glowLayer, top, holder.anchoredPosition,
                new Vector2(w * 1.8f, h * 1.5f));
            var glow = SpaceTheme.Img(glowRt, SpaceSprites.Glow, SpaceTheme.A(SpaceTheme.GOLD, 0.4f));
            glow.enabled = false;
            CardDeck.CreateCard(holder, code, w, SpaceTheme.CENTER, Vector2.zero);
            var ring = SpaceTheme.Rect("Ring", holder);
            SpaceTheme.Stretch(ring, -7f, -7f, -7f, -7f);
            SpaceTheme.RoundedStroke(ring, SpaceTheme.GOLD, w * 0.14f);
            ring.gameObject.SetActive(false);
            var hit = SpaceTheme.HitArea(holder);
            int c = code;
            SpaceTheme.Clickable(holder.gameObject, hit, () => Toggle(c));
            _slots.Add(new Slot { Code = code, Holder = holder, Ring = ring.gameObject, Glow = glow, BaseY = baseY });
        }
        _summary.text = CardDeck.Summary(s);
        UpdateSelection();
    }

    private void Toggle(int code)
    {
        if (_busy) return;
        if (_selected.Contains(code)) _selected.Remove(code);
        else if (_selected.Count >= CardRules.POKER_CARDS)
        {
            SpaceTheme.Toast(T_MAX, _canvas);
            return;
        }
        else _selected.Add(code);
        UpdateSelection();
    }

    private void SelectBest()
    {
        if (_busy) return;
        var best = CardRules.BestFive(CardDeck.Current.Hand, out _);
        if (best == null)
        {
            SpaceTheme.Toast(NeedText(CardDeck.Current.Hand.Count), _canvas);
            return;
        }
        _selected.Clear();
        _selected.AddRange(best);
        UpdateSelection();
    }

    private void ClearSelection()
    {
        if (_busy) return;
        _selected.Clear();
        UpdateSelection();
    }

    private static string NeedText(int hand) => T_NEED_A + Mathf.Max(0, CardRules.POKER_CARDS - hand) + T_NEED_B;

    /// <summary>選んだカードの見た目・役の見込み・ボタンの状態を合わせる</summary>
    private void UpdateSelection()
    {
        foreach (var slot in _slots)
        {
            bool on = _selected.Contains(slot.Code);
            var p = slot.Holder.anchoredPosition;
            slot.Holder.anchoredPosition = new Vector2(p.x, slot.BaseY + (on ? LIFT : 0f));
            slot.Ring.SetActive(on);
            slot.Glow.enabled = on;
            slot.Glow.rectTransform.anchoredPosition = slot.Holder.anchoredPosition;
        }

        var s = CardDeck.Current;
        int hand = s.Hand.Count;
        bool ready = _selected.Count == CardRules.POKER_CARDS && !CardRules.MustDiscard(s);
        PokerHand? preview = null;
        if (hand < CardRules.POKER_CARDS)
        {
            _previewTitle.text = NeedText(hand);
            _previewTitle.color = SpaceTheme.TEXT_SUB;
            SetPreviewSize(32f);
            _previewSub.text = T_HOW;
            _previewSub.color = SpaceTheme.A(SpaceTheme.TEXT_SUB, 0.8f);
        }
        else if (CardRules.MustDiscard(s))
        {
            _previewTitle.text = CardDeck.DiscardFirstText(s);
            _previewTitle.color = SpaceTheme.GOLD;
            SetPreviewSize(30f);
            _previewSub.text = "";
        }
        else if (_selected.Count < CardRules.POKER_CARDS)
        {
            _previewTitle.text = T_SELECT_A + _selected.Count + T_SELECT_B;
            _previewTitle.color = SpaceTheme.TEXT_SUB;
            SetPreviewSize(32f);
            _previewSub.text = "";
        }
        else
        {
            var h = CardRules.Evaluate(_selected);
            preview = h;
            var r = CardRules.RewardOf(h);
            _previewTitle.text = r.Name;
            _previewTitle.color = h >= PokerHand.Straight ? SpaceTheme.GOLD : SpaceTheme.TEXT;
            SetPreviewSize(48f);
            _previewSub.text = T_REWARD + CardDeck.RewardText(r);
            _previewSub.color = SpaceTheme.GOLD;
        }
        foreach (var kv in _tableRows) kv.Value.enabled = preview.HasValue && kv.Key == preview.Value;

        _playGroup.alpha = ready && !_busy ? 1f : 0.4f;
        if (!_busy) _playLabel.text = T_PLAY;
        _bestGroup.alpha = hand >= CardRules.POKER_CARDS ? 1f : 0.45f;
        _clearGroup.alpha = _selected.Count > 0 ? 1f : 0.45f;
        if (_swapGroup != null)
        {
            // 山札座: 引いて捨てる前なら「捨てるカードを選ぶ」、今日もう使ったら淡く「山札座は今日使いました」
            bool done = !s.SwapPending && s.SwapDay == StoreCalendar.TodayKey;
            _swapLabel.text = s.SwapPending ? SpaceDeckSwap.T_BUTTON_PENDING
                : done ? SpaceDeckSwap.T_BUTTON_DONE : SpaceDeckSwap.T_BUTTON;
            _swapGroup.alpha = done || _busy ? 0.45f : 1f;
        }
    }

    /// <summary>役の見込みの見出しの大きさ（自動で縮める上限ごと変える）</summary>
    private void SetPreviewSize(float size)
    {
        _previewTitle.fontSizeMax = size;
        SpaceTextScaler.SetSize(_previewTitle, size);
    }

    private void OnPlayPressed()
    {
        if (_busy) return;
        var s = CardDeck.Current;
        if (CardRules.MustDiscard(s))
        {
            SpaceTheme.Toast(CardDeck.DiscardFirstText(s), _canvas);
            SpaceNotice.CardDiscard();
            return;
        }
        if (s.Hand.Count < CardRules.POKER_CARDS)
        {
            SpaceTheme.Toast(NeedText(s.Hand.Count), _canvas);
            return;
        }
        if (_selected.Count != CardRules.POKER_CARDS)
        {
            SpaceTheme.Toast(T_SELECT_A + _selected.Count + T_SELECT_B, _canvas);
            return;
        }
        var five = new List<int>(_selected);
        var r = CardRules.RewardOf(CardRules.Evaluate(five));
        SpaceDialog.Confirm(_canvas, T_CONFIRM_TITLE,
            r.Name + "（" + CardDeck.RewardText(r) + "）\n" + T_CONFIRM_BODY, T_CONFIRM_OK, false,
            () => PlayRoutine(five).Forget());
    }

    private async UniTaskVoid PlayRoutine(List<int> five)
    {
        _busy = true;
        _playLabel.text = T_PLAYING;
        _playGroup.alpha = 0.6f;
        CardDeck.PokerPlay res;
        try
        {
            res = await CardDeck.PlayPokerAsync(five);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[SpacePoker] 勝負のエラー: {ex.Message}");
            res = new CardDeck.PokerPlay { Error = CardDeck.T_ERR_NETWORK };
        }
        if (this == null) return;
        _busy = false;
        if (res == null || !res.Ok)
        {
            SpaceTheme.Toast(res?.Error ?? CardDeck.T_ERR_NETWORK, _canvas);
            RebuildHand();
            return;
        }
        _selected.Clear();
        RebuildHand();
        AssetsDatabase.instance?.PlayLevelUpSE();
        SpaceNotice.PokerResult(res);
    }

    /// <summary>背景の星（ところどころまたたく。少し桃色がかった白）</summary>
    private static void Sky(RectTransform rt)
    {
        var rnd = new System.Random(20261113);
        var layer = SpaceTheme.Rect("Sky", rt);
        SpaceTheme.Stretch(layer);
        var tint = Color.Lerp(Color.white, PINK, 0.25f);
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
