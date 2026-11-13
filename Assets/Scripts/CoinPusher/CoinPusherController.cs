using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// コインプッシャーの画面（2026-11-12）。CoinPusher シーンのルート（CoinPusherRoot）に付いていて、
/// SceneLoader.MergeScene("CoinPusher") でホームに加算マージされて動き出す（CoinPusher.Open）。
/// ホームの Canvas の上に全画面で、台の映像（CoinPusherWorld の RenderTexture）と操作の帯を出す:
///  ・上: 題・閉じる・所持金・今回の獲得（G・赤／青／黄／黒コマ・星のカケラ・惑星回復薬。ピース座が完成している人はピースも）
///  ・下:「コインを入れる」（1回 1G。長押しで続けて入れる。狙っている所に落ちる）。
///    プッシャーの上をタップすると、そこへ落とす（なぞると狙いだけ動く。プッシャーより手前を押したら、前のふちの少し奥へ）
/// 月が変わったら台をはじめの並びに戻す（開いたとき。開いたままなら、その場で。2026-11-13）。管理者には閉じるの左に「リセット」。
/// 閉じるときは、台の並びを保存 → 送っていない増減を送る → 星のカケラが5個以上なら流れ星 → ホームに戻って結果をひとこと。
/// Android の戻るは NewHomeController が Instance.HandleBack を呼ぶ。
/// </summary>
public class CoinPusherController : MonoBehaviour
{
    public static CoinPusherController Instance { get; private set; }

    private const float SIDE = 40f;
    private const float MIN_INTERVAL = 0.2f;     // 続けて入れられる間隔（秒）
    private const float HOLD_INTERVAL = 0.32f;   // 長押しで続けて入れる間隔
    private const float FLUSH_INTERVAL = 6f;     // サーバーに増減を送る間隔
    private const float SAVE_INTERVAL = 2f;      // 台の並びを端末に保存する間隔（変わったとき）
    private const float RESAVE_INTERVAL = 10f;   // 何もしなくても台の並びを保存し直す間隔（プッシャーで動くので）
    private const float MONTH_CHECK_INTERVAL = 10f; // 開いているあいだに月が変わっていないか調べる間隔（アプリに戻ったときはすぐ）
    private const float RESET_NOTICE_LIFE = 3f;  // リセットの知らせを出しておく秒数（ボーナスの知らせより長く。文が長いので）
    private const int MAX_POPUPS = 10;
    private const float CHIP = 88f, CHIP_GOLD = 130f; // 上の帯の「今回の獲得」の1つ分の幅（G だけ広い）
    private const float CHIP_NARROW = 78f;            // ピースも並べて8つにするときの、G 以外の幅（2026-11-13）

    private static readonly Color BG = new Color(0.02f, 0.025f, 0.07f, 1f);
    private static readonly Color STAR_COLOR = SpaceTheme.Hex(0xFFD36E);
    private static readonly Color PLATINUM_COLOR = SpaceTheme.Hex(0xE4ECFF); // プラチナコインの「プラチナ +5G」（青みのある白銀）

    /// <summary>上の帯の「今回の獲得」に並べる物（この順に左から）</summary>
    private static readonly PusherItem[] HUD_ITEMS =
    {
        PusherItem.Coin, PusherItem.MeepleRed, PusherItem.MeepleBlue, PusherItem.MeepleYellow, PusherItem.MeepleBlack,
        PusherItem.StarFragment, PusherItem.PlanetPotion,
    };

    private Canvas _canvas;
    private RectTransform _ui, _safe, _popLayer;
    private CanvasGroup _group, _dropGroup;
    private RawImage _view;
    private CoinPusherWorld _world;
    private TextMeshProUGUI _wallet;
    private readonly TextMeshProUGUI[] _gotCounts = new TextMeshProUGUI[HUD_ITEMS.Length];
    private TextMeshProUGUI _pieceCount; // 今回の獲得のピース（全種類の合計。ピース座が完成している人だけ。2026-11-13）
    private bool _built, _closing, _holding, _fieldDirty, _towerPending;
    private float _nextDrop, _holdNext, _nextFlush, _nextSave, _nextResave, _nextShortToast, _nextMonthCheck;
    private int _sinceBonus, _sinceTower, _popups; // ボーナスまでに入れた枚数・コインタワーまでにゲットした枚数（台と一緒に保存）
    // 台をはじめの並びに戻すたびに増やす（戻す前に知らせを出して、落とすのを待っていたボーナス・コインタワーを新しい台に持ちこまない）
    private int _fieldGen;
    private GameObject _dialog; // 管理者のリセットの確認
    private string _uid;
    private Vector2 _canvasSize;
    // 今回遊んだ分（使った G と獲得した物。上の帯・終わったときの一言・履歴）
    private readonly CoinPusher.Ledger _session = new CoinPusher.Ledger();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        CoinPusher.MarkLoaded();
    }

    private void Start()
    {
        _canvas = SpaceTheme.MainCanvas();
        var m = UserDataManager.instance;
        if (_canvas == null || m == null || string.IsNullOrEmpty(m.UID) || m.UserData == null)
        {
            Debug.LogWarning("[CoinPusher] ホームの画面かユーザー情報がないので開けません");
            Destroy(gameObject);
            return;
        }
        _uid = m.UID;
        var _ = CoinPusher.Pending; // 前に送れなかった分があれば、手元の数にも足す
        Build();
        CoinPusher.FlushAsync().Forget(); // 前に送れなかった分を送る
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (_dialog != null) Destroy(_dialog); // 確認を出したまま消えたとき（ホームの画面に残さない）
        if (_built && !_closing) SaveField(); // 閉じるボタンを通らずに消えたとき（シーンの切り替えなど）
        CoinPusher.Persist();
        PlayerPrefs.Save();
        if (_world != null) Destroy(_world.gameObject);
        if (_ui != null) Destroy(_ui.gameObject);
    }

    private void OnApplicationPause(bool paused)
    {
        if (!_built || _closing) return;
        if (!paused)
        {
            _nextMonthCheck = 0f; // アプリに戻ったら、すぐに月が変わっていないか調べる（次の Update で）
            return;
        }
        SaveField();
        CoinPusher.FlushAsync().Forget();
    }

    // ================================================================
    // 組み立て
    // ================================================================
    private void Build()
    {
        _canvasSize = SpaceTheme.CanvasSize(_canvas);
        // 台の映像: 画面と同じ縦横比で、長い辺は 1920 まで
        float scale = Mathf.Min(1f, 1920f / Mathf.Max(1, Mathf.Max(Screen.width, Screen.height)));
        int rtW = Mathf.RoundToInt(Screen.width * scale), rtH = Mathf.RoundToInt(Screen.height * scale);
        _world = CoinPusherWorld.Create(rtW, rtH);
        _world.Won += OnWon;
        _world.Lost += OnLost;
        // 保存した台の続きから。月が変わっていたら（expired）、はじめの並びにしてすぐ保存し、組み立てのあとで知らせる
        string month = CoinPusher.MonthKey;
        if (!_world.Load(PlayerPrefs.GetString(CoinPusher.FieldKey(_uid), ""), month, out _sinceBonus, out _sinceTower,
                out bool expired))
        {
            _world.FillInitial(month);
            _sinceBonus = 0;
            _sinceTower = 0;
            if (expired) SaveField();
        }

        _ui = SpaceTheme.Rect("__CoinPusher", _canvas.transform);
        SpaceTheme.Stretch(_ui);
        _ui.SetAsLastSibling();
        SpaceTheme.SubCanvas(_ui.gameObject, true);
        _group = _ui.gameObject.AddComponent<CanvasGroup>();
        _group.alpha = 0f;

        var bg = SpaceTheme.Rect("Bg", _ui);
        SpaceTheme.Stretch(bg);
        SpaceTheme.Img(bg, null, BG, true);

        var viewRt = SpaceTheme.Rect("View", _ui);
        SpaceTheme.Stretch(viewRt);
        _view = viewRt.gameObject.AddComponent<RawImage>();
        _view.texture = _world.Texture;
        _view.raycastTarget = true;
        viewRt.gameObject.AddComponent<CoinPusherInput>().Init(this, viewRt);

        // 上下を少し暗くして、文字とボタンを読みやすく
        Shade(new Vector2(0f, 1f), 520f, true);
        Shade(new Vector2(0f, 0f), 460f, false);

        _popLayer = SpaceTheme.Rect("Popups", _ui);
        SpaceTheme.Stretch(_popLayer);

        _safe = SpaceTheme.Rect("Safe", _ui);
        SpaceTheme.Stretch(_safe);
        _safe.gameObject.AddComponent<SpaceSafeArea>();
        BuildTop();
        BuildBottom();

        _built = true;
        _nextFlush = Time.unscaledTime + FLUSH_INTERVAL;
        _nextResave = Time.unscaledTime + RESAVE_INTERVAL;
        _nextMonthCheck = Time.unscaledTime + MONTH_CHECK_INTERVAL;
        RefreshHud();
        StartCoroutine(SpaceTheme.Tween(0.3f, 0f, k => { if (_group != null) _group.alpha = k; }));
        if (expired)
            ShowBanner(CoinPusher.T_MONTHLY, CoinPusher.T_MONTHLY_SUB, SpaceTheme.CYAN, life: RESET_NOTICE_LIFE);
        MaybeTower(); // 前に開いていたとき、タワーが出る前に閉じていたら
    }

    /// <summary>
    /// 上か下の端から、だんだん透明になる暗い帯。透明度のグラデーションは SpaceAlphaGradient で付ける
    /// （SpaceTheme.Gradient は色だけを変えて透明度は変えないので、それで作ったら帯が不透明になり、台の奥と手前が隠れた。2026-11-12）
    /// </summary>
    private void Shade(Vector2 anchor, float height, bool top)
    {
        var rt = SpaceTheme.Rect(top ? "ShadeTop" : "ShadeBottom", _ui);
        rt.anchorMin = new Vector2(0f, anchor.y);
        rt.anchorMax = new Vector2(1f, anchor.y);
        rt.pivot = new Vector2(0.5f, anchor.y);
        rt.sizeDelta = new Vector2(0f, height);
        rt.anchoredPosition = Vector2.zero;
        SpaceTheme.Img(rt, null, new Color(0.01f, 0.015f, 0.05f, 0.82f));
        rt.gameObject.AddComponent<SpaceAlphaGradient>().Set(top ? 1f : 0f, top ? 0f : 1f, 1f); // 端が濃く、内側へ透明に
    }

    private void BuildTop()
    {
        var caption = SpaceTheme.Text(_safe, "Caption", CoinPusher.T_CAPTION, 24, SpaceTheme.A(SpaceTheme.CYAN, 0.95f),
            new Vector2(0f, 1f), new Vector2(SIDE + 16f, -70f), new Vector2(400f, 36f), FontStyles.Bold,
            TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
        SpaceTheme.Tracking(caption, 18f);
        const float TITLE_W = 560f;
        var title = SpaceTheme.Text(_safe, "Title", CoinPusher.T_TITLE, 50, SpaceTheme.TEXT, new Vector2(0f, 1f),
            new Vector2(SIDE + 16f, -126f), new Vector2(TITLE_W, 64f), FontStyles.Bold, TextAlignmentOptions.Left,
            new Vector2(0f, 0.5f));

        // 題の右に、この遊びをくれた積み木座（管理者で未完成なら完成した姿）。
        // 題が長いので、題の幅を測ってその右に置く（決め打ちの位置では「ー」に重なった。文字を大きくする設定で広がるぶんも見込む）
        var p = Constellations.Progress().Find(x => x.Def.Id == CoinPusher.CONSTELLATION);
        if (p != null)
        {
            float titleW = Mathf.Min(TITLE_W, title.GetPreferredValues(CoinPusher.T_TITLE).x * SpaceTheme.TextScale);
            var fig = SpaceTheme.Rect("Constellation", _safe, new Vector2(0f, 1f),
                new Vector2(SIDE + 16f + titleW + 28f + 55f, -104f), new Vector2(110f, 110f));
            SpaceConstellationPage.Figure(fig, p.Def, Mathf.Max(p.Def.Size, p.Count - p.Partial), 110f);
        }

        // 右上の閉じる
        var close = SpaceTheme.Rect("Close", _safe, new Vector2(1f, 1f), new Vector2(-(SIDE + 52f), -84f), new Vector2(104f, 104f));
        var face = SpaceTheme.Img(close, SpaceSprites.Circle, new Color(0.05f, 0.07f, 0.19f, 0.88f), true);
        var rim = SpaceTheme.Rect("Rim", close);
        SpaceTheme.Stretch(rim);
        SpaceTheme.Img(rim, SpaceSprites.RingSmall, SpaceTheme.A(SpaceTheme.CYAN, 0.55f));
        SpaceTheme.Bar(close, Vector2.zero, 44f, 6f, 45f, SpaceTheme.TEXT);
        SpaceTheme.Bar(close, Vector2.zero, 44f, 6f, -45f, SpaceTheme.TEXT);
        SpaceTheme.Clickable(close.gameObject, face, HandleBack);

        // 管理者だけ: 閉じるの左に「リセット」（2026-11-13 ユーザー指定。確認してから、自分の台をはじめの並びに戻す。
        // 題と積み木座は左にあり、文字を大きくする設定でもここまでは届かない）
        if (AdminClaim.IsAdmin)
        {
            var reset = SpaceTheme.Rect("Reset", _safe, new Vector2(1f, 1f), new Vector2(-(SIDE + 104f + 20f), -84f),
                new Vector2(170f, 66f), new Vector2(1f, 0.5f));
            var resetFace = SpaceTheme.Glass(reset, 33f, new Color(0.07f, 0.09f, 0.24f, 0.85f),
                SpaceTheme.A(SpaceTheme.DANGER, 0.6f), true);
            SpaceTheme.Clickable(reset.gameObject, resetFace, ConfirmReset);
            var resetLabel = SpaceTheme.Text(reset, "Label", CoinPusher.T_RESET, 28, SpaceTheme.DANGER, FontStyles.Bold);
            SpaceTheme.Stretch(resetLabel.rectTransform);
        }

        // 所持金と今回の獲得の帯
        // ピース座が完成している人（管理者は初めから）は、今回の獲得のいちばん右にピース（全種類の合計）も並べる（2026-11-13）
        bool pieces = PuzzleStore.Unlocked;
        float w = Mathf.Min(_canvasSize.x - SIDE * 2f, 1000f);
        var bar = SpaceTheme.Rect("Info", _safe, new Vector2(0.5f, 1f), new Vector2(0f, -238f), new Vector2(w, 116f));
        SpaceTheme.Glass(bar, 40f, new Color(0.05f, 0.07f, 0.19f, 0.78f), SpaceTheme.A(SpaceTheme.GOLD, 0.35f));
        var coin = SpaceTheme.Rect("Coin", bar, new Vector2(0f, 0.5f), new Vector2(26f + 34f, 0f), new Vector2(68f, 68f));
        var coinImg = SpaceTheme.Img(coin, IgCurrency.Icon, Color.white);
        coinImg.preserveAspect = true;
        coinImg.enabled = IgCurrency.Icon != null;
        SpaceTheme.Text(bar, "WalletLabel", CoinPusher.T_WALLET, 22, SpaceTheme.TEXT_SUB, new Vector2(0f, 0.5f),
            new Vector2(104f, 24f), new Vector2(220f, 30f), FontStyles.Bold, TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
        // 右の「今回の獲得」が7つに増えたので（2026-11-12 黒コマ・惑星回復薬を追加）、幅を 240→200 にして、大きな額は縮めて収める。
        // ピースも並べて8つのときは 180（2026-11-13）
        _wallet = SpaceTheme.Text(bar, "Wallet", "", 46, SpaceTheme.GOLD, new Vector2(0f, 0.5f), new Vector2(104f, -16f),
            new Vector2(pieces ? 180f : 200f, 56f), FontStyles.Bold, TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
        _wallet.enableAutoSizing = true;
        _wallet.fontSizeMin = 28f;
        _wallet.fontSizeMax = 46f;

        SpaceTheme.Text(bar, "GotLabel", CoinPusher.T_GOT, 22, SpaceTheme.TEXT_SUB, new Vector2(1f, 0.5f),
            new Vector2(-24f, 30f), new Vector2(560f, 30f), FontStyles.Bold, TextAlignmentOptions.Right, new Vector2(1f, 0.5f));
        // 右から順に並べる。G は 100G を超えて3〜4けたになるので広く（数の枠 80＝4けたまで縮めずに入る）、
        // コマ・星のカケラ・惑星回復薬は2けたまでの幅（2026-11-12 ユーザー指定「100G 以上で右の赤コマのアイコンと数字が被る」。
        // それまでは全部同じ幅で、数の枠が狭く、3けたの G がとなりの赤コマのアイコンに重なっていた）。
        // 130 ＋ 88 × 6 ＋ 右の余白 24 で、帯（幅 1000）の右から 682 まで。左の所持金（右はし 304）とは重ならない。
        // 決めた枠より長くなったら、数の文字を縮めて枠に収める（となりに重ねない）。
        // ピースも並べて8つのときは（2026-11-13）、G 以外の幅を CHIP_NARROW（78）にして 130 ＋ 78 × 7 ＋ 24 ＝ 700
        // （帯の左から 300 まで）。所持金の枠も 180（右はし 284）にして重ならないようにした
        float chipW = pieces ? CHIP_NARROW : CHIP;
        float right = -24f;
        if (pieces)
        {
            _pieceCount = GotChip(bar, "GotPieces", right, chipW, PuzzleArt.Icon(0)); // 絵はショップのランダムと同じ（色の違う4マス）
            right -= chipW;
        }
        for (int i = HUD_ITEMS.Length - 1; i >= 0; i--)
        {
            float cw = HUD_ITEMS[i] == PusherItem.Coin ? CHIP_GOLD : chipW;
            _gotCounts[i] = GotChip(bar, "Got" + i, right, cw, IconOf(HUD_ITEMS[i]));
            right -= cw;
        }
    }

    /// <summary>上の帯の「今回の獲得」の1つ分（右はしの位置 right・幅 cw・絵 sprite）を作って、数の文字を返す</summary>
    private static TextMeshProUGUI GotChip(RectTransform bar, string name, float right, float cw, Sprite sprite)
    {
        var chip = SpaceTheme.Rect(name, bar, new Vector2(1f, 0.5f), new Vector2(right, -18f), new Vector2(cw - 4f, 52f),
            new Vector2(1f, 0.5f));
        var icon = SpaceTheme.Rect("Icon", chip, new Vector2(0f, 0.5f), new Vector2(21f, 0f), new Vector2(42f, 42f));
        var img = SpaceTheme.Img(icon, sprite, Color.white);
        img.preserveAspect = true;
        img.enabled = sprite != null;
        var count = SpaceTheme.Text(chip, "Count", "0", 32, SpaceTheme.TEXT, new Vector2(0f, 0.5f), new Vector2(46f, 0f),
            new Vector2(cw - 50f, 44f), FontStyles.Bold, TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
        count.textWrappingMode = TextWrappingModes.NoWrap; // 折り返さずに、枠の幅に合わせて縮める
        count.enableAutoSizing = true;
        count.fontSizeMin = 18f;
        count.fontSizeMax = 32f;
        return count;
    }

    private void BuildBottom()
    {
        // 決まりとヒント（上の帯を低くして台の奥まで見えるよう、どちらも下に置く）
        float w = Mathf.Min(_canvasSize.x - SIDE * 2f, 1000f);
        var rules = SpaceTheme.Text(_safe, "Rules", CoinPusher.T_RULES, 22, SpaceTheme.A(SpaceTheme.TEXT_SUB, 0.85f),
            new Vector2(0.5f, 0f), new Vector2(0f, 290f), new Vector2(w, 32f), FontStyles.Bold);
        rules.enableAutoSizing = true;
        rules.fontSizeMin = 16f;
        rules.fontSizeMax = 22f;
        var hint = SpaceTheme.Text(_safe, "Hint", CoinPusher.T_HINT, 24, SpaceTheme.TEXT_SUB, new Vector2(0.5f, 0f),
            new Vector2(0f, 250f), new Vector2(w, 36f), FontStyles.Bold);
        hint.enableAutoSizing = true;
        hint.fontSizeMin = 18f;
        hint.fontSizeMax = 24f;

        var drop = SpaceTheme.Rect("Drop", _safe, new Vector2(0.5f, 0f), new Vector2(0f, 60f), new Vector2(660f, 156f),
            new Vector2(0.5f, 0f));
        var face = SpaceTheme.GradientFace(drop, SpaceTheme.Hex(0xFFE3A3), SpaceTheme.Hex(0xE09A3E), SpaceTheme.GOLD, 56f);
        face.raycastTarget = true;
        drop.gameObject.AddComponent<CoinPusherHold>().Init(this);
        _dropGroup = drop.gameObject.AddComponent<CanvasGroup>();
        var label = SpaceTheme.Text(drop, "Label", CoinPusher.T_DROP, 46, SpaceTheme.TEXT_DARK, SpaceTheme.CENTER,
            new Vector2(0f, 20f), new Vector2(620f, 60f), FontStyles.Bold);
        label.enableAutoSizing = true;
        label.fontSizeMin = 30f;
        label.fontSizeMax = 46f;
        var sub = SpaceTheme.Text(drop, "Sub", CoinPusher.T_DROP_SUB, 24, SpaceTheme.A(SpaceTheme.TEXT_DARK, 0.75f),
            SpaceTheme.CENTER, new Vector2(0f, -32f), new Vector2(620f, 34f), FontStyles.Bold);
        sub.enableAutoSizing = true;
        sub.fontSizeMin = 16f;
        sub.fontSizeMax = 24f;
    }

    // ================================================================
    // 毎フレーム
    // ================================================================
    private void Update()
    {
        if (!_built || _closing) return;
        float now = Time.unscaledTime;
        if (_holding && now >= _holdNext)
        {
            _holdNext = now + HOLD_INTERVAL;
            TryDrop();
        }
        if (now >= _nextFlush)
        {
            _nextFlush = now + FLUSH_INTERVAL;
            CoinPusher.FlushAsync().Forget();
        }
        if (now >= _nextResave)
        {
            _nextResave = now + RESAVE_INTERVAL;
            _fieldDirty = true;
        }
        if (_fieldDirty && now >= _nextSave)
        {
            _nextSave = now + SAVE_INTERVAL;
            SaveField();
        }
        if (now >= _nextMonthCheck)
        {
            _nextMonthCheck = now + MONTH_CHECK_INTERVAL;
            if (_world != null && CoinPusher.Expired(_world.Month, CoinPusher.MonthKey)) ResetField(true);
        }
    }

    /// <summary>台の並びと、送っていない増減を端末に保存する</summary>
    private void SaveField()
    {
        if (_world == null || string.IsNullOrEmpty(_uid)) return;
        PlayerPrefs.SetString(CoinPusher.FieldKey(_uid), _world.Save(_sinceBonus, _sinceTower));
        CoinPusher.Persist();
        PlayerPrefs.Save();
        _fieldDirty = false;
    }

    // ================================================================
    // リセット（月に1回・管理者。2026-11-13）
    // ================================================================
    /// <summary>
    /// 台をはじめの並びに戻す（月が変わったとき monthly・管理者のリセット）。ボーナスとコインタワーまでの数も 0 から。
    /// 今回の獲得の表示と、送っていない増減（獲得した物）はそのまま。戻したらすぐ保存する
    /// </summary>
    private void ResetField(bool monthly)
    {
        if (!_built || _closing || _world == null) return;
        _fieldGen++;
        _towerPending = false;
        _world.Clear();
        _world.FillInitial(CoinPusher.MonthKey);
        _sinceBonus = 0;
        _sinceTower = 0;
        SaveField();
        if (monthly) ShowBanner(CoinPusher.T_MONTHLY, CoinPusher.T_MONTHLY_SUB, SpaceTheme.CYAN, life: RESET_NOTICE_LIFE);
        else ShowBanner(CoinPusher.T_RESET, CoinPusher.T_RESET_DONE, SpaceTheme.CYAN, life: RESET_NOTICE_LIFE);
    }

    /// <summary>管理者の「リセット」: 確認してから台をはじめの並びに戻す</summary>
    private void ConfirmReset()
    {
        if (!_built || _closing || _dialog != null) return;
        _holding = false;
        _dialog = SpaceDialog.Confirm(_canvas, CoinPusher.T_RESET_CONFIRM, CoinPusher.T_RESET_CONFIRM_BODY, CoinPusher.T_RESET, true,
            () =>
            {
                _dialog = null;
                if (this != null) ResetField(false);
            },
            () => _dialog = null);
    }

    private void RefreshHud()
    {
        int balance = IgCurrency.Balance;
        if (_wallet != null)
            _wallet.text = balance.ToString("N0", CultureInfo.InvariantCulture) + "<size=60%>" + IgCurrency.UNIT + "</size>";
        for (int i = 0; i < _gotCounts.Length; i++)
        {
            if (_gotCounts[i] == null) continue;
            int got = _session.Count(HUD_ITEMS[i]);
            _gotCounts[i].text = got.ToString(CultureInfo.InvariantCulture);
            _gotCounts[i].color = got > 0 ? SpaceTheme.GOLD : SpaceTheme.TEXT_MUTED;
        }
        if (_pieceCount != null)
        {
            int got = _session.PieceTotal;
            _pieceCount.text = got.ToString(CultureInfo.InvariantCulture);
            _pieceCount.color = got > 0 ? SpaceTheme.GOLD : SpaceTheme.TEXT_MUTED;
        }
        if (_dropGroup != null) _dropGroup.alpha = balance >= CoinPusher.COST ? 1f : 0.5f;
    }

    // ================================================================
    // コインを入れる
    // ================================================================
    /// <summary>「コインを入れる」を押した（離すまで続けて入れる）</summary>
    internal void BeginHold()
    {
        if (!_built || _closing) return;
        _holding = true;
        _holdNext = Time.unscaledTime + HOLD_INTERVAL + 0.15f; // 1枚目のあと、少し待ってから続ける
        TryDrop();
    }

    internal void EndHold() => _holding = false;

    /// <summary>台の映像を押した・なぞった: その所（プッシャーの上）を狙う（drop なら、そこへコインを1枚）</summary>
    internal void AimAt(Vector2 viewport, bool drop)
    {
        if (!_built || _closing || _world == null) return;
        _world.AimAtViewport(viewport);
        if (drop) TryDrop();
    }

    private void TryDrop()
    {
        if (!_built || _closing || _world == null) return;
        float now = Time.unscaledTime;
        if (now < _nextDrop) return;
        if (IgCurrency.Balance < CoinPusher.COST)
        {
            _holding = false;
            if (now >= _nextShortToast)
            {
                _nextShortToast = now + 1.5f;
                SpaceTheme.Toast(CoinPusher.T_SHORT, _canvas);
            }
            return;
        }
        if (_world.ItemCount >= CoinPusherWorld.MAX_ITEMS)
        {
            if (now >= _nextShortToast)
            {
                _nextShortToast = now + 1.5f;
                SpaceTheme.Toast(CoinPusher.T_FULL, _canvas);
            }
            return;
        }
        _nextDrop = now + MIN_INTERVAL;
        CoinPusher.RecordSpend(CoinPusher.COST);
        _session.spent += CoinPusher.COST;
        _world.DropCoin();
        CoinPusherSounds.Clink();
        _fieldDirty = true;
        RefreshHud();
        if (++_sinceBonus >= CoinPusher.BONUS_EVERY)
        {
            _sinceBonus = 0;
            StartCoroutine(DropBonus());
        }
    }

    /// <summary>
    /// ボーナス: 星のカケラ・コマ（色はおまかせ）・惑星回復薬のどれかを、プッシャーの上のどこかへ1つ落とす。
    /// ピース座が完成している人（管理者は初めから）は、ピースパズルのピース（レアのU字も）のこともある
    /// （2026-11-13 ユーザー指定。割合は CoinPusher.BONUS_PIECE・種類は CoinPusher.RollPieceType）
    /// </summary>
    private IEnumerator DropBonus()
    {
        int gen = _fieldGen;
        var kind = CoinPusher.RollBonus(UnityEngine.Random.value, PuzzleStore.Unlocked, UnityEngine.Random.value);
        ShowBanner(CoinPusher.T_BONUS, NameOf(kind) + CoinPusher.T_BONUS_FALL_B, ColorOf(kind));
        CoinPusherSounds.Bonus();
        yield return new WaitForSecondsRealtime(0.6f);
        if (_world == null || _closing || gen != _fieldGen) yield break; // 待っているあいだに台をリセットした
        _world.DropBonus(kind);
        _fieldDirty = true;
    }

    // ================================================================
    // 獲得・没収
    // ================================================================
    private void OnWon(PusherItem kind, Vector3 world)
    {
        CoinPusher.RecordWin(kind);
        _session.Add(kind);
        _fieldDirty = true;
        RefreshHud();
        bool platinum = kind == PusherItem.PlatinumCoin; // プラチナコイン（2026-11-13。5G）
        if (kind == PusherItem.Coin) CoinPusherSounds.Win();
        else if (platinum) CoinPusherSounds.Platinum();
        else CoinPusherSounds.Prize();
        string text = kind == PusherItem.Coin ? "+1" + IgCurrency.UNIT
            : platinum ? CoinPusher.T_PLATINUM + " +" + CoinPusher.PLATINUM_VALUE + IgCurrency.UNIT
            : NameOf(kind) + " +1";
        ShowPopup(text, ColorOf(kind), IconOf(kind), _world != null ? _world.WorldToViewport(world) : new Vector2(0.5f, 0.2f));
        if (CoinPusherWorld.IsCoin(kind)) // プラチナコインも1枚と数える
        {
            _sinceTower++;
            MaybeTower();
        }
    }

    /// <summary>ゲットしたコインが TOWER_EVERY 枚に届いていたら、コインタワーを出す（出している途中なら待つ）</summary>
    private void MaybeTower()
    {
        if (_towerPending || _closing || !_built || _sinceTower < CoinPusher.TOWER_EVERY) return;
        _towerPending = true;
        StartCoroutine(TowerBonus());
    }

    /// <summary>
    /// コインタワー: 知らせを出してから、プッシャーの上にコインを積んだタワーを出し、同時にタワーの横へコマを1つ落とす
    /// （2026-11-12 ユーザー指定。色は赤・青・黄・黒から同じ割合で。CoinPusher.RollTowerMeeple）。タワーとコマは同じ時に出すので、
    /// どちらかだけになることはない。数はタワーが出てから減らす（出る前に閉じたら、数はそのまま保存されて、次に開いたときに出る）
    /// </summary>
    private IEnumerator TowerBonus()
    {
        int gen = _fieldGen;
        var meeple = CoinPusher.RollTowerMeeple(UnityEngine.Random.value);
        ShowBanner(CoinPusher.T_TOWER, CoinPusher.T_TOWER_SUB_A + CoinPusher.TOWER_EVERY + CoinPusher.T_TOWER_SUB_B, SpaceTheme.GOLD,
            NameOf(meeple) + CoinPusher.T_TOWER_ALSO_B, ColorOf(meeple));
        CoinPusherSounds.Bonus();
        yield return new WaitForSecondsRealtime(0.35f);
        if (gen != _fieldGen) yield break; // 待っているあいだに台をリセットした（数は 0 に戻してあるので、減らさない）
        if (_world == null || _closing)
        {
            _towerPending = false;
            yield break;
        }
        float towerX = _world.SpawnTower(CoinPusher.TOWER_COINS);
        _world.DropBonus(meeple, towerX);
        CoinPusherSounds.Prize();
        _sinceTower -= CoinPusher.TOWER_EVERY;
        _fieldDirty = true;
        _towerPending = false;
        MaybeTower();
    }

    /// <summary>
    /// 横から落ちた（没収）。コマ・星のカケラ・惑星回復薬など G 以外のときは、残念な音を鳴らす
    /// （2026-11-12 ユーザー指定「ミープルなどの G 以外のものが落ちてしまったときは、残念感のある音を」）。
    /// プラチナコイン（5G）も惜しいので、残念な音を鳴らす（2026-11-13。ふつうの G のコインだけ鳴らさない）
    /// </summary>
    private void OnLost(PusherItem kind)
    {
        _fieldDirty = true;
        if (kind != PusherItem.Coin && !_closing) CoinPusherSounds.Lost();
    }

    private static string NameOf(PusherItem kind) => kind switch
    {
        PusherItem.MeepleRed => SpaceMeeples.Of(PlanetColor.Red).Name,
        PusherItem.MeepleBlue => SpaceMeeples.Of(PlanetColor.Blue).Name,
        PusherItem.MeepleYellow => SpaceMeeples.Of(PlanetColor.Yellow).Name,
        PusherItem.MeepleBlack => SpaceMeeples.Black.Name,
        PusherItem.StarFragment => SpaceWorld.STAR_FRAGMENT_NAME,
        PusherItem.PlanetPotion => SpacePlanetPotion.NAME,
        PusherItem.PlatinumCoin => CoinPusher.T_PLATINUM_NAME,
        _ when CoinPusherWorld.IsPiece(kind) => PuzzleStore.PieceLabel(CoinPusherWorld.PieceType(kind)), // 「ピース（L字）」
        _ => IgCurrency.UNIT,
    };

    private static Color ColorOf(PusherItem kind) => kind switch
    {
        PusherItem.MeepleRed => SpaceTheme.Hex(0xFF7A7A),
        PusherItem.MeepleBlue => SpaceTheme.Hex(0x7AB0FF),
        PusherItem.MeepleYellow => SpaceTheme.Hex(0xFFE066),
        PusherItem.MeepleBlack => SpaceMeeples.Black.Tint,
        PusherItem.StarFragment => STAR_COLOR,
        PusherItem.PlanetPotion => SpacePlanetPotion.TINT,
        PusherItem.PlatinumCoin => PLATINUM_COLOR,
        _ when CoinPusherWorld.IsPiece(kind) => PuzzleArt.ColorOf(CoinPusherWorld.PieceType(kind)), // パズルの画面と同じ色
        _ => SpaceTheme.GOLD,
    };

    private static Sprite IconOf(PusherItem kind) => kind switch
    {
        PusherItem.MeepleRed => SpaceMeeples.Of(PlanetColor.Red)?.Icon,
        PusherItem.MeepleBlue => SpaceMeeples.Of(PlanetColor.Blue)?.Icon,
        PusherItem.MeepleYellow => SpaceMeeples.Of(PlanetColor.Yellow)?.Icon,
        PusherItem.MeepleBlack => SpaceMeeples.Black.Icon,
        PusherItem.StarFragment => SpaceWorld.StarFragmentIcon,
        PusherItem.PlanetPotion => SpacePlanetPotion.Icon,
        PusherItem.PlatinumCoin => CoinPusher.PlatinumIcon,
        _ when CoinPusherWorld.IsPiece(kind) => PuzzleArt.Icon(CoinPusherWorld.PieceType(kind)), // ショップ・所持品と同じ絵
        _ => IgCurrency.Icon,
    };

    /// <summary>獲得した物の名前が、落ちた所から浮かび上がって消える</summary>
    private void ShowPopup(string text, Color color, Sprite icon, Vector2 viewport)
    {
        if (_popLayer == null || _popups >= MAX_POPUPS) return;
        var size = _canvasSize;
        var pos = new Vector2(Mathf.Clamp(viewport.x, 0.15f, 0.85f) * size.x, Mathf.Clamp(viewport.y, 0.2f, 0.75f) * size.y);
        var rt = SpaceTheme.Rect("Pop", _popLayer, Vector2.zero, pos, new Vector2(420f, 70f));
        var shade = SpaceTheme.Rect("Shade", rt, SpaceTheme.CENTER, Vector2.zero, new Vector2(520f, 170f));
        SpaceTheme.Img(shade, SpaceSprites.Glow, new Color(0.01f, 0.015f, 0.05f, 0.75f)); // 台の上でも読めるように
        var icn = SpaceTheme.Rect("Icon", rt, new Vector2(0f, 0.5f), new Vector2(40f, 0f), new Vector2(60f, 60f));
        var img = SpaceTheme.Img(icn, icon, Color.white);
        img.preserveAspect = true;
        img.enabled = icon != null;
        var label = SpaceTheme.Text(rt, "Text", text, 40, color, new Vector2(0f, 0.5f), new Vector2(80f, 0f), new Vector2(340f, 60f),
            FontStyles.Bold, TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
        // 長い名前（2026-11-13 に足したピースの「ピース（2マス） +1」など）も、折り返さずに枠の幅に合わせて縮める
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.enableAutoSizing = true;
        label.fontSizeMin = 24f;
        label.fontSizeMax = 40f;
        var fl = rt.gameObject.AddComponent<CoinPusherFloat>();
        fl.Init(1.15f, 150f, () => _popups--);
        _popups++;
    }

    /// <summary>
    /// ボーナスの知らせ。奥の壁のあたり（下から 75%）に出す（ボーナスの景品もコインタワーもプッシャーの上に出るので、
    /// そこを隠さないように。2026-11-12 までは 62% で、プッシャーの上と重なっていた）。
    /// sub2 があれば3行目に（コインタワーの「黒コマも落ちてくる」。その色 sub2Color で）。life は出しておく秒数
    /// </summary>
    private void ShowBanner(string title, string sub, Color color, string sub2 = null, Color? sub2Color = null, float life = 1.8f)
    {
        if (_popLayer == null) return;
        var rt = SpaceTheme.Rect("Banner", _popLayer, new Vector2(0.5f, 0.75f), Vector2.zero, new Vector2(900f, 160f));
        var shade = SpaceTheme.Rect("Shade", rt, SpaceTheme.CENTER, Vector2.zero, new Vector2(1000f, sub2 != null ? 360f : 300f));
        SpaceTheme.Img(shade, SpaceSprites.Glow, new Color(0.01f, 0.015f, 0.05f, 0.8f));
        var glow = SpaceTheme.Rect("Glow", rt, SpaceTheme.CENTER, Vector2.zero, new Vector2(900f, 340f));
        SpaceTheme.Img(glow, SpaceSprites.Glow, SpaceTheme.A(color, 0.35f));
        SpaceTheme.Text(rt, "Title", title, 64, SpaceTheme.GOLD, SpaceTheme.CENTER, new Vector2(0f, 30f),
            new Vector2(900f, 80f), FontStyles.Bold);
        SpaceTheme.Text(rt, "Sub", sub, 32, color, SpaceTheme.CENTER, new Vector2(0f, -36f), new Vector2(900f, 46f),
            FontStyles.Bold);
        if (sub2 != null)
            SpaceTheme.Text(rt, "Sub2", sub2, 32, sub2Color ?? color, SpaceTheme.CENTER, new Vector2(0f, -82f),
                new Vector2(900f, 46f), FontStyles.Bold);
        rt.gameObject.AddComponent<CoinPusherFloat>().Init(life, 40f, null);
    }

    // ================================================================
    // 閉じる
    // ================================================================
    /// <summary>閉じる（閉じるボタン・Android の戻る）</summary>
    public void HandleBack()
    {
        if (_closing) return;
        if (_dialog != null) // 管理者のリセットの確認を出しているときは、確認だけ閉じる
        {
            Destroy(_dialog);
            _dialog = null;
            return;
        }
        if (!_built)
        {
            Destroy(gameObject);
            return;
        }
        CloseAsync().Forget();
    }

    private async UniTaskVoid CloseAsync()
    {
        _closing = true;
        _holding = false;
        var busy = SpaceDialog.Busy(_canvas, CoinPusher.T_SAVING);
        SaveField();
        bool ok = await CoinPusher.FlushAsync();
        if (!ok)
        {
            await UniTask.Delay(700, ignoreTimeScale: true);
            ok = await CoinPusher.FlushAsync();
        }
        CoinPusher.StarExchange ex = null;
        if (ok) ex = await CoinPusher.ExchangeStarFragmentsAsync();
        if (busy != null) Destroy(busy);
        if (this == null) return;

        string text = CoinPusher.SessionText(_session);
        if (text != null) LocalHistoryLog.Add(CoinPusher.HISTORY, CoinPusher.T_TITLE + "で " + text);

        float from = _group != null ? _group.alpha : 1f;
        for (float t = 0f; t < 0.25f; t += Time.unscaledDeltaTime)
        {
            if (this == null) return;
            if (_group != null) _group.alpha = from * (1f - t / 0.25f);
            await UniTask.Yield();
        }
        if (this == null) return;

        if (!ok) SpaceTheme.Toast(CoinPusher.T_SAVE_FAILED);
        else if (text != null) SpaceTheme.Toast(CoinPusher.T_SUMMARY_A + text);
        if (ex != null) ShowStars(ex);
        JourneyService.NotifyChanged(); // ホームの所持品の数など
        Destroy(gameObject);
    }

    /// <summary>星のカケラが5個そろった → 流れ星（入店のときと同じ演出）→ ノワールなら新しい惑星</summary>
    private static void ShowStars(CoinPusher.StarExchange ex)
    {
        var steps = new List<Func<GameObject>>();
        int left = ex.Left;
        steps.Add(() => SpaceNotice.StarFragmentGot(CoinPusher.T_TITLE, left, true, CoinPusher.T_SIGN, " を集めました"));
        foreach (var p in ex.Star.Prizes)
        {
            var prize = p;
            steps.Add(() => SpaceGachaMeteor.Play(prize));
        }
        foreach (var id in ex.Star.Unlocked)
        {
            var def = SpacePlanets.Find(id);
            if (def != null) steps.Add(() => SpaceNotice.PlanetUnlocked(def));
        }
        SpaceNoticeQueue.Run(steps, null);
    }
}

/// <summary>台の映像の上の操作: 押した所・なぞった所に投入口を動かし、ほとんど動かさずに離したらコインを1枚落とす</summary>
public class CoinPusherInput : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    private const float TAP_MOVE = 24f; // これ以上動かしたら、なぞった（落とさない）
    private CoinPusherController _owner;
    private RectTransform _rt;
    private Vector2 _downPos;
    private bool _dragged;

    public void Init(CoinPusherController owner, RectTransform rt)
    {
        _owner = owner;
        _rt = rt;
    }

    public void OnPointerDown(PointerEventData e)
    {
        _downPos = e.position;
        _dragged = false;
        if (TryViewport(e, out var vp)) _owner.AimAt(vp, false);
    }

    public void OnDrag(PointerEventData e)
    {
        if ((e.position - _downPos).sqrMagnitude > TAP_MOVE * TAP_MOVE) _dragged = true;
        if (TryViewport(e, out var vp)) _owner.AimAt(vp, false);
    }

    public void OnPointerUp(PointerEventData e)
    {
        if (_dragged) return;
        if (TryViewport(e, out var vp)) _owner.AimAt(vp, true);
    }

    private bool TryViewport(PointerEventData e, out Vector2 vp)
    {
        vp = new Vector2(0.5f, 0.5f);
        if (_owner == null || _rt == null) return false;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_rt, e.position, e.pressEventCamera, out var local))
            return false;
        var r = _rt.rect;
        if (r.width <= 0f || r.height <= 0f) return false;
        vp = new Vector2((local.x - r.xMin) / r.width, (local.y - r.yMin) / r.height);
        return true;
    }
}

/// <summary>「コインを入れる」ボタン: 押したら1枚、押しているあいだは続けて入れる</summary>
public class CoinPusherHold : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    private CoinPusherController _owner;
    private bool _down;

    public void Init(CoinPusherController owner) => _owner = owner;

    public void OnPointerDown(PointerEventData e)
    {
        _down = true;
        transform.localScale = Vector3.one * 0.96f;
        if (_owner != null) _owner.BeginHold();
    }

    public void OnPointerUp(PointerEventData e) => Release();

    public void OnPointerExit(PointerEventData e)
    {
        if (_down) Release();
    }

    private void OnDisable()
    {
        if (_down) Release();
    }

    private void Release()
    {
        _down = false;
        transform.localScale = Vector3.one;
        if (_owner != null) _owner.EndHold();
    }
}

/// <summary>浮かび上がりながら消える（獲得・ボーナスの知らせ）</summary>
public class CoinPusherFloat : MonoBehaviour
{
    private float _life, _rise, _t;
    private Vector2 _start;
    private RectTransform _rt;
    private CanvasGroup _group;
    private Action _onDone;

    public void Init(float life, float rise, Action onDone)
    {
        _life = Mathf.Max(0.1f, life);
        _rise = rise;
        _onDone = onDone;
        _rt = (RectTransform)transform;
        _start = _rt.anchoredPosition;
        _group = gameObject.AddComponent<CanvasGroup>();
        _group.blocksRaycasts = false;
        _group.interactable = false;
    }

    private void Update()
    {
        _t += Time.unscaledDeltaTime;
        float k = Mathf.Clamp01(_t / _life);
        float ease = 1f - (1f - k) * (1f - k);
        _rt.anchoredPosition = _start + new Vector2(0f, _rise * ease);
        float pop = k < 0.15f ? Mathf.Lerp(0.6f, 1.08f, k / 0.15f) : Mathf.Lerp(1.08f, 1f, Mathf.Clamp01((k - 0.15f) / 0.2f));
        _rt.localScale = Vector3.one * pop;
        _group.alpha = k < 0.65f ? 1f : 1f - (k - 0.65f) / 0.35f;
        if (k >= 1f) Destroy(gameObject);
    }

    private void OnDestroy()
    {
        var cb = _onDone;
        _onDone = null;
        cb?.Invoke();
    }
}

/// <summary>コインプッシャーの効果音（実行時に波形を合成。VisitChime と同じやり方）</summary>
public static class CoinPusherSounds
{
    private const int RATE = 44100;
    private static AudioClip _clink, _win, _prize, _bonus, _lost, _platinum;
    private static AudioSource _source;

    /// <summary>コインを入れた（チャリン）</summary>
    public static void Clink() => Play(_clink != null ? _clink : (_clink = Bell("pusher_clink",
        new[] { (2637f, 0f, 0.16f), (3951f, 0.01f, 0.14f) }, 0.32f)), 0.55f);

    /// <summary>コインを獲得（キラン）</summary>
    public static void Win() => Play(_win != null ? _win : (_win = Bell("pusher_win",
        new[] { (1318.5f, 0f, 0.12f), (1975.5f, 0.07f, 0.3f) }, 0.4f)), 0.7f);

    /// <summary>
    /// プラチナコインを獲得（2026-11-13）: 高い音がソ・ド・ミ・ソと速く駆け上がり、最後が長く響く「キラリラリーン」
    /// （コインの「キラン」より明るく、景品の「ピロリン」より高く速い）
    /// </summary>
    public static void Platinum() => Play(_platinum != null ? _platinum : (_platinum = Bell("pusher_platinum",
        new[] { (1568f, 0f, 0.12f), (2093f, 0.055f, 0.12f), (2637f, 0.11f, 0.12f), (3136f, 0.165f, 0.62f) }, 0.34f)), 0.85f);

    /// <summary>コマ・星のカケラを獲得（ピロリン）</summary>
    public static void Prize() => Play(_prize != null ? _prize : (_prize = Bell("pusher_prize",
        new[] { (1046.5f, 0f, 0.14f), (1318.5f, 0.09f, 0.14f), (1568f, 0.18f, 0.14f), (2093f, 0.27f, 0.45f) }, 0.42f)), 0.8f);

    /// <summary>ボーナスが落ちてくる（ポロロン）</summary>
    public static void Bonus() => Play(_bonus != null ? _bonus : (_bonus = Bell("pusher_bonus",
        new[] { (784f, 0f, 0.12f), (988f, 0.08f, 0.12f), (1175f, 0.16f, 0.12f), (1568f, 0.24f, 0.5f) }, 0.4f)), 0.75f);

    /// <summary>
    /// コマ・星のカケラなど G 以外が横から落ちた（残念な「ぽわ・ぽわ・ぽわわ〜ん」。2026-11-12）。
    /// ラ → ソ# → ソと半音ずつ下がり、最後の音はゆれながらさらに少し下がって消える
    /// </summary>
    public static void Lost() => Play(_lost != null ? _lost : (_lost = Sad("pusher_lost",
        new[] { (440f, 0f, 0.17f), (415.3f, 0.19f, 0.17f), (392f, 0.38f, 0.62f) }, 0.55f)), 0.8f);

    private static void Play(AudioClip clip, float volume)
    {
        if (clip == null) return;
        if (_source == null)
        {
            var go = new GameObject("__CoinPusherSounds");
            UnityEngine.Object.DontDestroyOnLoad(go);
            _source = go.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0f;
        }
        _source.PlayOneShot(clip, volume);
    }

    /// <summary>ベル風の音（基音＋倍音、5ms で立ち上がって指数で減衰）をつなげた1つの音</summary>
    private static AudioClip Bell(string name, (float freq, float start, float dur)[] notes, float gain)
    {
        float total = 0f;
        foreach (var n in notes) total = Mathf.Max(total, n.start + n.dur);
        int length = (int)((total + 0.05f) * RATE);
        var data = new float[length];
        foreach (var n in notes)
        {
            int offset = (int)(n.start * RATE);
            int count = (int)(n.dur * RATE);
            for (int i = 0; i < count && offset + i < length; i++)
            {
                float t = i / (float)RATE;
                float env = Mathf.Min(1f, t / 0.005f) * Mathf.Exp(-4f * t / n.dur);
                float w = Mathf.Sin(2f * Mathf.PI * n.freq * t)
                          + 0.4f * Mathf.Sin(2f * Mathf.PI * n.freq * 2.76f * t) * Mathf.Exp(-7f * t / n.dur)
                          + 0.15f * Mathf.Sin(2f * Mathf.PI * n.freq * 5.4f * t) * Mathf.Exp(-11f * t / n.dur);
                data[offset + i] += w * env * gain;
            }
        }
        for (int i = 0; i < length; i++) data[i] = Mathf.Clamp(data[i], -1f, 1f);
        var clip = AudioClip.Create(name, length, 1, RATE, false);
        clip.SetData(data, 0);
        return clip;
    }

    /// <summary>
    /// 残念な音（ベルのように「チン」と鳴らず、ラッパのように伸びる、こもった音色）。音ごとに 20ms で立ち上がって
    /// 終わりに向けて弱まり、少しだけ音が下がる。最後の音は 6Hz でゆれながら 6% 下がり、ゆっくり消える
    /// </summary>
    private static AudioClip Sad(string name, (float freq, float start, float dur)[] notes, float gain)
    {
        float total = 0f;
        foreach (var n in notes) total = Mathf.Max(total, n.start + n.dur);
        int length = (int)((total + 0.05f) * RATE);
        var data = new float[length];
        for (int k = 0; k < notes.Length; k++)
        {
            var n = notes[k];
            bool last = k == notes.Length - 1;
            int offset = (int)(n.start * RATE);
            int count = (int)(n.dur * RATE);
            double phase = 0.0;
            for (int i = 0; i < count && offset + i < length; i++)
            {
                float t = i / (float)RATE;
                float u = t / n.dur; // 音の中での進み（0〜1）
                float f = n.freq * (1f - (last ? 0.06f : 0.015f) * u);
                if (last) f *= 1f + 0.012f * Mathf.Sin(2f * Mathf.PI * 6f * t);
                phase += 2.0 * Mathf.PI * f / RATE;
                // 倍音を少し混ぜた、やわらかいラッパのような音色（最大でもおよそ ±1）
                float w = (float)(Math.Sin(phase) + 0.5 * Math.Sin(2.0 * phase) + 0.3 * Math.Sin(3.0 * phase)
                                  + 0.15 * Math.Sin(4.0 * phase)) / 1.95f;
                float env = Mathf.Min(1f, t / 0.02f) * (last ? 1f - u * u * u : 1f - 0.5f * u * u);
                data[offset + i] += w * env * gain;
            }
        }
        for (int i = 0; i < length; i++) data[i] = Mathf.Clamp(data[i], -1f, 1f);
        var clip = AudioClip.Create(name, length, 1, RATE, false);
        clip.SetData(data, 0);
        return clip;
    }
}
