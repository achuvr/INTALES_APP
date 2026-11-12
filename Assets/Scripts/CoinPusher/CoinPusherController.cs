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
///  ・上: 題・閉じる・所持金・今回の獲得（G・赤／青／黄コマ・星のカケラ）
///  ・下:「コインを入れる」（1回 1G。長押しで続けて入れる。狙っている所に落ちる）。
///    プッシャーの上をタップすると、そこへ落とす（なぞると狙いだけ動く。プッシャーより手前を押したら、前のふちの少し奥へ）
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
    private const int MAX_POPUPS = 10;

    private static readonly Color BG = new Color(0.02f, 0.025f, 0.07f, 1f);
    private static readonly Color STAR_COLOR = SpaceTheme.Hex(0xFFD36E);

    private Canvas _canvas;
    private RectTransform _ui, _safe, _popLayer;
    private CanvasGroup _group, _dropGroup;
    private RawImage _view;
    private CoinPusherWorld _world;
    private TextMeshProUGUI _wallet;
    private readonly TextMeshProUGUI[] _gotCounts = new TextMeshProUGUI[5]; // G・赤・青・黄・星のカケラ
    private bool _built, _closing, _holding, _fieldDirty;
    private float _nextDrop, _holdNext, _nextFlush, _nextSave, _nextResave, _nextShortToast;
    private int _sinceBonus, _popups;
    private string _uid;
    private Vector2 _canvasSize;
    // 今回遊んだ分（終わったときの一言・履歴）
    private int _sSpent, _sGold, _sRed, _sBlue, _sYellow, _sStar;

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
        if (_built && !_closing) SaveField(); // 閉じるボタンを通らずに消えたとき（シーンの切り替えなど）
        CoinPusher.Persist();
        PlayerPrefs.Save();
        if (_world != null) Destroy(_world.gameObject);
        if (_ui != null) Destroy(_ui.gameObject);
    }

    private void OnApplicationPause(bool paused)
    {
        if (!paused || !_built || _closing) return;
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
        if (!_world.Load(PlayerPrefs.GetString(CoinPusher.FieldKey(_uid), ""), out _sinceBonus))
        {
            _world.FillInitial();
            _sinceBonus = 0;
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
        RefreshHud();
        StartCoroutine(SpaceTheme.Tween(0.3f, 0f, k => { if (_group != null) _group.alpha = k; }));
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

        // 所持金と今回の獲得の帯
        float w = Mathf.Min(_canvasSize.x - SIDE * 2f, 1000f);
        var bar = SpaceTheme.Rect("Info", _safe, new Vector2(0.5f, 1f), new Vector2(0f, -238f), new Vector2(w, 116f));
        SpaceTheme.Glass(bar, 40f, new Color(0.05f, 0.07f, 0.19f, 0.78f), SpaceTheme.A(SpaceTheme.GOLD, 0.35f));
        var coin = SpaceTheme.Rect("Coin", bar, new Vector2(0f, 0.5f), new Vector2(26f + 34f, 0f), new Vector2(68f, 68f));
        var coinImg = SpaceTheme.Img(coin, IgCurrency.Icon, Color.white);
        coinImg.preserveAspect = true;
        coinImg.enabled = IgCurrency.Icon != null;
        SpaceTheme.Text(bar, "WalletLabel", CoinPusher.T_WALLET, 22, SpaceTheme.TEXT_SUB, new Vector2(0f, 0.5f),
            new Vector2(104f, 24f), new Vector2(220f, 30f), FontStyles.Bold, TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
        _wallet = SpaceTheme.Text(bar, "Wallet", "", 46, SpaceTheme.GOLD, new Vector2(0f, 0.5f), new Vector2(104f, -16f),
            new Vector2(240f, 56f), FontStyles.Bold, TextAlignmentOptions.Left, new Vector2(0f, 0.5f));

        SpaceTheme.Text(bar, "GotLabel", CoinPusher.T_GOT, 22, SpaceTheme.TEXT_SUB, new Vector2(1f, 0.5f),
            new Vector2(-24f, 30f), new Vector2(560f, 30f), FontStyles.Bold, TextAlignmentOptions.Right, new Vector2(1f, 0.5f));
        var icons = new[]
        {
            (IgCurrency.Icon, SpaceTheme.GOLD),
            (SpaceMeeples.Of(PlanetColor.Red)?.Icon, PlanetColors.Of(PlanetColor.Red)),
            (SpaceMeeples.Of(PlanetColor.Blue)?.Icon, PlanetColors.Of(PlanetColor.Blue)),
            (SpaceMeeples.Of(PlanetColor.Yellow)?.Icon, PlanetColors.Of(PlanetColor.Yellow)),
            (SpaceWorld.StarFragmentIcon, STAR_COLOR),
        };
        const float CHIP = 104f;
        for (int i = 0; i < icons.Length; i++)
        {
            float x = -24f - (icons.Length - 1 - i) * CHIP;
            var chip = SpaceTheme.Rect("Got" + i, bar, new Vector2(1f, 0.5f), new Vector2(x, -18f), new Vector2(CHIP - 6f, 52f),
                new Vector2(1f, 0.5f));
            var icon = SpaceTheme.Rect("Icon", chip, new Vector2(0f, 0.5f), new Vector2(22f, 0f), new Vector2(44f, 44f));
            var img = SpaceTheme.Img(icon, icons[i].Item1, Color.white);
            img.preserveAspect = true;
            img.enabled = icons[i].Item1 != null;
            _gotCounts[i] = SpaceTheme.Text(chip, "Count", "0", 32, SpaceTheme.TEXT, new Vector2(0f, 0.5f), new Vector2(48f, 0f),
                new Vector2(CHIP - 54f, 44f), FontStyles.Bold, TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
        }

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
    }

    /// <summary>台の並びと、送っていない増減を端末に保存する</summary>
    private void SaveField()
    {
        if (_world == null || string.IsNullOrEmpty(_uid)) return;
        PlayerPrefs.SetString(CoinPusher.FieldKey(_uid), _world.Save(_sinceBonus));
        CoinPusher.Persist();
        PlayerPrefs.Save();
        _fieldDirty = false;
    }

    private void RefreshHud()
    {
        int balance = IgCurrency.Balance;
        if (_wallet != null)
            _wallet.text = balance.ToString("N0", CultureInfo.InvariantCulture) + "<size=60%>" + IgCurrency.UNIT + "</size>";
        int[] got = { _sGold, _sRed, _sBlue, _sYellow, _sStar };
        for (int i = 0; i < _gotCounts.Length; i++)
        {
            if (_gotCounts[i] == null) continue;
            _gotCounts[i].text = got[i].ToString(CultureInfo.InvariantCulture);
            _gotCounts[i].color = got[i] > 0 ? SpaceTheme.GOLD : SpaceTheme.TEXT_MUTED;
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
        _sSpent += CoinPusher.COST;
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

    /// <summary>ボーナス: コマ（色はおまかせ）か星のカケラを、プッシャーの上のどこかへ1つ落とす</summary>
    private IEnumerator DropBonus()
    {
        PusherItem kind;
        float r = UnityEngine.Random.value;
        if (r < 0.55f) kind = PusherItem.StarFragment;
        else kind = r < 0.70f ? PusherItem.MeepleRed : r < 0.85f ? PusherItem.MeepleBlue : PusherItem.MeepleYellow;
        string what = kind == PusherItem.StarFragment ? CoinPusher.T_BONUS_STAR : NameOf(kind) + CoinPusher.T_BONUS_MEEPLE_B;
        ShowBanner(CoinPusher.T_BONUS, what, ColorOf(kind));
        CoinPusherSounds.Bonus();
        yield return new WaitForSecondsRealtime(0.6f);
        if (_world == null || _closing) yield break;
        _world.DropBonus(kind);
        _fieldDirty = true;
    }

    // ================================================================
    // 獲得・没収
    // ================================================================
    private void OnWon(PusherItem kind, Vector3 world)
    {
        CoinPusher.RecordWin(kind);
        switch (kind)
        {
            case PusherItem.Coin: _sGold++; break;
            case PusherItem.MeepleRed: _sRed++; break;
            case PusherItem.MeepleBlue: _sBlue++; break;
            case PusherItem.MeepleYellow: _sYellow++; break;
            case PusherItem.StarFragment: _sStar++; break;
        }
        _fieldDirty = true;
        RefreshHud();
        if (kind == PusherItem.Coin) CoinPusherSounds.Win();
        else CoinPusherSounds.Prize();
        string text = kind == PusherItem.Coin ? "+1" + IgCurrency.UNIT : NameOf(kind) + " +1";
        ShowPopup(text, ColorOf(kind), IconOf(kind), _world != null ? _world.WorldToViewport(world) : new Vector2(0.5f, 0.2f));
    }

    private void OnLost(PusherItem kind) => _fieldDirty = true;

    private static string NameOf(PusherItem kind) => kind switch
    {
        PusherItem.MeepleRed => SpaceMeeples.Of(PlanetColor.Red).Name,
        PusherItem.MeepleBlue => SpaceMeeples.Of(PlanetColor.Blue).Name,
        PusherItem.MeepleYellow => SpaceMeeples.Of(PlanetColor.Yellow).Name,
        PusherItem.StarFragment => SpaceWorld.STAR_FRAGMENT_NAME,
        _ => IgCurrency.UNIT,
    };

    private static Color ColorOf(PusherItem kind) => kind switch
    {
        PusherItem.MeepleRed => SpaceTheme.Hex(0xFF7A7A),
        PusherItem.MeepleBlue => SpaceTheme.Hex(0x7AB0FF),
        PusherItem.MeepleYellow => SpaceTheme.Hex(0xFFE066),
        PusherItem.StarFragment => STAR_COLOR,
        _ => SpaceTheme.GOLD,
    };

    private static Sprite IconOf(PusherItem kind) => kind switch
    {
        PusherItem.MeepleRed => SpaceMeeples.Of(PlanetColor.Red)?.Icon,
        PusherItem.MeepleBlue => SpaceMeeples.Of(PlanetColor.Blue)?.Icon,
        PusherItem.MeepleYellow => SpaceMeeples.Of(PlanetColor.Yellow)?.Icon,
        PusherItem.StarFragment => SpaceWorld.StarFragmentIcon,
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
        var fl = rt.gameObject.AddComponent<CoinPusherFloat>();
        fl.Init(1.15f, 150f, () => _popups--);
        _popups++;
    }

    /// <summary>画面のまん中より上に、ボーナスの知らせ</summary>
    private void ShowBanner(string title, string sub, Color color)
    {
        if (_popLayer == null) return;
        var rt = SpaceTheme.Rect("Banner", _popLayer, new Vector2(0.5f, 0.62f), Vector2.zero, new Vector2(900f, 160f));
        var shade = SpaceTheme.Rect("Shade", rt, SpaceTheme.CENTER, Vector2.zero, new Vector2(1000f, 300f));
        SpaceTheme.Img(shade, SpaceSprites.Glow, new Color(0.01f, 0.015f, 0.05f, 0.8f));
        var glow = SpaceTheme.Rect("Glow", rt, SpaceTheme.CENTER, Vector2.zero, new Vector2(900f, 340f));
        SpaceTheme.Img(glow, SpaceSprites.Glow, SpaceTheme.A(color, 0.35f));
        var t = SpaceTheme.Text(rt, "Title", title, 64, SpaceTheme.GOLD, SpaceTheme.CENTER, new Vector2(0f, 30f),
            new Vector2(900f, 80f), FontStyles.Bold);
        var s = SpaceTheme.Text(rt, "Sub", sub, 32, color, SpaceTheme.CENTER, new Vector2(0f, -36f), new Vector2(900f, 46f),
            FontStyles.Bold);
        rt.gameObject.AddComponent<CoinPusherFloat>().Init(1.8f, 40f, null);
    }

    // ================================================================
    // 閉じる
    // ================================================================
    /// <summary>閉じる（閉じるボタン・Android の戻る）</summary>
    public void HandleBack()
    {
        if (_closing) return;
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

        string text = CoinPusher.SessionText(_sSpent, _sGold, _sRed, _sBlue, _sYellow, _sStar);
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
    private static AudioClip _clink, _win, _prize, _bonus;
    private static AudioSource _source;

    /// <summary>コインを入れた（チャリン）</summary>
    public static void Clink() => Play(_clink != null ? _clink : (_clink = Bell("pusher_clink",
        new[] { (2637f, 0f, 0.16f), (3951f, 0.01f, 0.14f) }, 0.32f)), 0.55f);

    /// <summary>コインを獲得（キラン）</summary>
    public static void Win() => Play(_win != null ? _win : (_win = Bell("pusher_win",
        new[] { (1318.5f, 0f, 0.12f), (1975.5f, 0.07f, 0.3f) }, 0.4f)), 0.7f);

    /// <summary>コマ・星のカケラを獲得（ピロリン）</summary>
    public static void Prize() => Play(_prize != null ? _prize : (_prize = Bell("pusher_prize",
        new[] { (1046.5f, 0f, 0.14f), (1318.5f, 0.09f, 0.14f), (1568f, 0.18f, 0.14f), (2093f, 0.27f, 0.45f) }, 0.42f)), 0.8f);

    /// <summary>ボーナスが落ちてくる（ポロロン）</summary>
    public static void Bonus() => Play(_bonus != null ? _bonus : (_bonus = Bell("pusher_bonus",
        new[] { (784f, 0f, 0.12f), (988f, 0.08f, 0.12f), (1175f, 0.16f, 0.12f), (1568f, 0.24f, 0.5f) }, 0.4f)), 0.75f);

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
}
