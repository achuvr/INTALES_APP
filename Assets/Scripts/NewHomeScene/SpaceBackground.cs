using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 宇宙の背景（コード生成）。キャンバスの最背面に敷く。
///   ・星雲（グラデーション＋雲）と星 … 静止画（RawImage 2枚）
///   ・リング付き惑星・小さな衛星・瞬く星・ときどき流れる流れ星 … 動く層
/// 動く層は専用のサブキャンバスに分け、毎フレームの変化がメインUIの再構築を起こさないようにしている。
/// 小さな衛星（左の中段のピンクの星）は EnableMoonButton で押せるようにできる（新ホームのお知らせ）。
/// お知らせに見ていない内容があるときは、衛星から「New」の吹き出しを出す（SetMoonBadge）。
/// 新ホームでは、完成した星座（Constellations）を空に並べる（EnableConstellations。2026-11-06 ユーザー指定
/// 「宇宙空間に配置」）。星座ごとに決まった場所に、レベルの明るさで薄く出る。新しく完成・レベルアップすると、
/// お知らせが閉じてからその場所に光ってふくらんで現れる。
/// 空の星座をタップするとその星座の詳細が開く。さいころ座は、いろいろなダイスを振れる画面（SpaceDiceRoller）、
/// 金貨座は、アプリ内の通貨（G）で買い物をするショップ（SpaceShop。2026-11-12）、宝石座はポーカー（SpacePoker。同日）、
/// 積み木座はコインプッシャー（CoinPusher。同日。別シーン）、ピース座はピースパズル（SpacePuzzle。2026-11-13）が開く
/// （押せるものだと分かるよう後ろの光が明滅し、ホームの部品に隠れない場所に置いている。管理者には初めから出ている）。
/// 星座の置き場は SpaceSkyLayout が、ホームの部品の空いている所に散らばるように計算する（2026-11-14。NewHomeController が
/// SetSkyFrame で部品の位置を渡す）。
/// </summary>
public class SpaceBackground : MonoBehaviour
{
    /// <summary>吹き出しの文字（新ホームの起動時にフォントへ登録する。SpaceNewsPage.GlyphText に含めている）</summary>
    public const string T_NEW = "New";

    private const int TWINKLE_COUNT = 20;
    private const float STREAK_DURATION = 0.9f;
    // 吹き出し（120x62）は衛星の右上。しっぽ（三角形）の先が衛星のふちの少し外に来るようにし、
    // 吹き出しの基準点をしっぽの先に置く（出るときに衛星からふくらんで見える）
    private static readonly Vector2 BADGE_SIZE = new Vector2(120f, 62f);
    private static readonly Vector2 BADGE_POS = new Vector2(30f, 31f);       // 衛星の中心から見たしっぽの先
    private static readonly Vector2 TAIL_TIP = new Vector2(-46f, -49f);      // 吹き出しの中心から見たしっぽの先
    private static readonly Vector2 TAIL_SIZE = new Vector2(40f, 25f);       // しっぽの画像（BubbleTail）の大きさ
    private static readonly Vector2 BADGE_PIVOT = new Vector2(
        0.5f + TAIL_TIP.x / BADGE_SIZE.x, 0.5f + TAIL_TIP.y / BADGE_SIZE.y); // しっぽの先（吹き出しの外）
    private static readonly Color BADGE_FILL = SpaceTheme.Hex(0xFFF4FB);     // 吹き出し（白に近い桃色）
    private static readonly Color BADGE_TEXT = SpaceTheme.Hex(0xE83E9A);     // 文字（濃い桃色）

    private class Twinkle
    {
        public RectTransform Rt;
        public Image Img;
        public float BaseAlpha, Speed, Phase;
    }

    private readonly List<Twinkle> _twinkles = new List<Twinkle>();
    private readonly System.Random _rnd = new System.Random();
    private RectTransform _planet, _moon, _streak, _anim, _badge;
    private Image _streakImg, _moonGlow;
    private Color _moonGlowColor;
    private Vector2 _planetBase, _moonBase, _size, _streakFrom, _streakTo;
    private float _nextStreak, _streakT = -1f, _badgeShownAt;
    private float _safeTop;

    // ---- 空の星座 ----
    private const float SKY_FIG = 190f;
    private const float SKY_ALPHA = 0.62f;
    /// <summary>
    /// 星座ごとの仮の空の場所（Constellations.All の順。キャンバスの左下を 0,0 とした割合）。2026-11-14 からは
    /// SpaceSkyLayout の置き場（SetSkyFrame で決まる）に置くので、ここはそれが届く前だけの場所
    /// </summary>
    private static readonly Vector2[] SKY_SLOTS =
    {
        new Vector2(0.14f, 0.92f), new Vector2(0.42f, 0.95f), new Vector2(0.10f, 0.73f), new Vector2(0.90f, 0.66f),
        new Vector2(0.12f, 0.53f), new Vector2(0.88f, 0.48f), new Vector2(0.10f, 0.32f), new Vector2(0.90f, 0.30f),
        // さいころ座は押すとダイスの画面が開くので、ホームのどの部品にも隠れない右の中ほど（うたげ座と旗座の間）に置く
        // （2026-11-11 ユーザー指定で移動。以前の 0.17,0.11 は所持品ボタンの裏で押せなかった。
        //   16:9・縦長のiPhone/Android・iPad、INTALES WORLD のボタンのあり・なしのどれでも、押せる部品と重ならない）
        // 金貨座は押すとショップが開くので、さいころ座と左右対称の左の中ほど（仮面座と切り札座の間）に置く
        // （2026-11-12。以前の 0.83,0.11 は所持品ボタンの裏で押せなかったので、ここにいた盤座と入れ替えた。
        //   16:9・縦長のiPhone/Android・iPhone SE・iPad、INTALES WORLD のボタンのあり・なしのどれでも、押せる部品と重ならない）
        new Vector2(0.885f, 0.39f), new Vector2(0.50f, 0.05f), new Vector2(0.12f, 0.42f), new Vector2(0.36f, 0.80f),
        new Vector2(0.30f, 0.21f), new Vector2(0.70f, 0.21f),
        new Vector2(0.83f, 0.11f), new Vector2(0.50f, 0.15f), // 盤座・積み木座（2026-11-07 追加。盤座は 2026-11-12 に金貨座と入れ替え）
        // 2026-11-14: どの星座も SpaceSkyLayout の置き場に置く。ここの値は、それが決まる前（SetSkyFrame が届く前）の仮の場所
    };
    private RectTransform _skyLayer;
    private readonly Dictionary<string, int> _skyLevels = new Dictionary<string, int>(); // 星座ID → 空に出しているレベル
    private readonly Dictionary<string, RectTransform> _skyFigures = new Dictionary<string, RectTransform>();

    /// <summary>
    /// 押すと画面が開く星座（さいころ座＝ダイス、金貨座＝ショップ、宝石座＝ポーカー、積み木座＝コインプッシャー、
    /// ピース座＝ピースパズル、きずな座＝ミニオン）。完成するまで出ない・管理者には初めから出ている
    /// </summary>
    private static readonly string[] TAPPABLE =
    {
        SpaceDiceRoller.CONSTELLATION, IgCurrency.CONSTELLATION, SpacePoker.CONSTELLATION, CoinPusher.CONSTELLATION,
        PuzzleStore.CONSTELLATION, MinionStore.CONSTELLATION,
    };

    /// <summary>
    /// 星座の置き場（2026-11-14 ユーザー指定「名前などが載ってるUIの下など空きスペースが気になるので、良い感じに散らばるように」）。
    /// NewHomeController が SetSkyFrame でホームの部品の位置を渡すと、SpaceSkyLayout が16の星座すべての置き場と大きさを決める
    /// （カードの下の帯・左右の列・下に、左右対称に散らす）。届くまでは null（SKY_SLOTS の仮の場所）。
    /// それまでの置き場（2026-11-12〜14）: 押せる星座は左右の列（お知らせ・料金表〜図鑑・履歴の間）に縦に・のちにジグザグ、
    /// 仮面座・うたげ座・探偵座・切り札座・旗座はカードの下に1列、ほかは SKY_SLOTS（見出し・所持品ボタン・図鑑の名前と重なっていた）
    /// </summary>
    private Dictionary<string, SpaceSkyLayout.Spot> _skySpots;
    /// <summary>左に置く押せる星座（カードの下の帯・列・下の順）。右は RIGHT_TAP の同じ並び（左右対称の位置）</summary>
    private static readonly string[] LEFT_TAP = { IgCurrency.CONSTELLATION, MinionStore.CONSTELLATION, SpacePoker.CONSTELLATION };
    private static readonly string[] RIGHT_TAP = { SpaceDiceRoller.CONSTELLATION, PuzzleStore.CONSTELLATION, CoinPusher.CONSTELLATION };
    /// <summary>帰ってきて話しかけられるのを待っているミニオンがいるときの、きずな座の「!」（2026-11-14）</summary>
    private RectTransform _minionBadge;
    private float _minionBadgeShownAt;
    /// <summary>出てくる演出の途中の星座（大きさは演出が決める）</summary>
    private readonly HashSet<RectTransform> _arriving = new HashSet<RectTransform>();

    /// <summary>その星座を描く大きさの倍率（SpaceSkyLayout の置き場ごとの大きさ。置き場が届く前は 0.75）</summary>
    private float ScaleOf(string id) =>
        _skySpots != null && _skySpots.TryGetValue(id, out var spot) ? spot.Scale : 0.75f;

    /// <summary>押せる星座の後ろの光（押せるものだと分かるよう、ゆっくり明滅させる）</summary>
    private class TapGlow
    {
        public Image Img;
        public Color Color;
        public float Phase;
    }
    private readonly Dictionary<string, TapGlow> _tapGlows = new Dictionary<string, TapGlow>();
    private float _nextSkyCheck;
    private bool _skyFirst = true;

    /// <param name="canvas">ルートキャンバス</param>
    /// <param name="size">キャンバスの実寸</param>
    /// <param name="safeTop">セーフエリア外の上余白（惑星の位置合わせ用）</param>
    public static SpaceBackground Create(RectTransform canvas, Vector2 size, float safeTop)
    {
        var root = SpaceTheme.Rect("__SpaceBackground", canvas);
        SpaceTheme.Stretch(root);
        root.SetAsFirstSibling();
        var bg = root.gameObject.AddComponent<SpaceBackground>();
        bg.Build(size, safeTop);
        return bg;
    }

    private void Build(Vector2 size, float safeTop)
    {
        _safeTop = safeTop;
        _size = size;
        var root = (RectTransform)transform;

        // 星雲: 低解像度（1/6）で作って引き伸ばす
        var neb = SpaceTheme.Rect("Nebula", root);
        SpaceTheme.Stretch(neb);
        var nebImg = neb.gameObject.AddComponent<RawImage>();
        nebImg.texture = SpaceSprites.Nebula(Mathf.Max(32, Mathf.RoundToInt(size.x / 6f)),
                                             Mathf.Max(32, Mathf.RoundToInt(size.y / 6f)));
        nebImg.raycastTarget = false;

        // 星: キャンバスの半分の解像度・同じ縦横比で作るので歪まない
        var stars = SpaceTheme.Rect("Stars", root);
        SpaceTheme.Stretch(stars);
        var starImg = stars.gameObject.AddComponent<RawImage>();
        starImg.texture = SpaceSprites.Stars(Mathf.Max(16, Mathf.RoundToInt(size.x / 2f)),
                                             Mathf.Max(16, Mathf.RoundToInt(size.y / 2f)));
        starImg.raycastTarget = false;

        // ---- 動く層（サブキャンバス）----
        var anim = SpaceTheme.Rect("Animated", root);
        SpaceTheme.Stretch(anim);
        SpaceTheme.SubCanvas(anim.gameObject, false);
        _anim = anim;

        // 瞬く星（位置は固定シードで毎回同じ配置）
        var seeded = new System.Random(3);
        for (int i = 0; i < TWINKLE_COUNT; i++)
        {
            float s = 14f + (float)seeded.NextDouble() * 26f;
            var rt = SpaceTheme.Rect("Twinkle", anim, Vector2.zero,
                new Vector2((float)seeded.NextDouble() * size.x, (float)seeded.NextDouble() * size.y),
                new Vector2(s, s));
            var img = SpaceTheme.Img(rt, SpaceSprites.Sparkle, Color.white);
            _twinkles.Add(new Twinkle
            {
                Rt = rt,
                Img = img,
                BaseAlpha = 0.35f + (float)seeded.NextDouble() * 0.6f,
                Speed = 0.6f + (float)seeded.NextDouble() * 1.6f,
                Phase = (float)seeded.NextDouble() * Mathf.PI * 2f,
            });
        }

        // 小さな衛星（左の中段）。絵は子に置く（押せるようにしたとき、後ろに光を差し込めるように）
        _moon = SpaceTheme.Rect("Moon", anim, new Vector2(0f, 1f), new Vector2(120f, -(safeTop + 760f)),
            new Vector2(92f, 92f));
        var moonBody = SpaceTheme.Rect("Body", _moon);
        SpaceTheme.Stretch(moonBody);
        SpaceTheme.Img(moonBody, SpaceSprites.Moon, Color.white);
        _moonBase = _moon.anchoredPosition;

        // リング付きの惑星（右上・一部は画面外）
        _planet = SpaceTheme.Rect("Planet", anim, new Vector2(1f, 1f), new Vector2(-120f, -(safeTop + 64f)),
            new Vector2(600f, 600f));
        SpaceTheme.Img(_planet, SpaceSprites.Planet, Color.white);
        _planetBase = _planet.anchoredPosition;

        // 流れ星（頭＝右端を基準点にして、進行方向へ回転させる）
        _streak = SpaceTheme.Rect("ShootingStar", anim, Vector2.zero, Vector2.zero, new Vector2(300f, 18f),
            new Vector2(1f, 0.5f));
        _streakImg = SpaceTheme.Img(_streak, SpaceSprites.Streak, new Color(1f, 1f, 1f, 0f));
        _streak.gameObject.SetActive(false);
        _nextStreak = Time.unscaledTime + 3f;
    }

    /// <summary>
    /// 左の小さな衛星の高さを変える（セーフエリアの上端からの距離。ふだんは 760）。
    /// Start画面では、上に置いた入力のカードと重ならないよう、カードと惑星の間へ下げる
    /// </summary>
    public void MoveMoon(float fromSafeTop)
    {
        if (_moon == null) return;
        _moonBase = new Vector2(_moonBase.x, -(_safeTop + fromSafeTop));
        _moon.anchoredPosition = _moonBase;
    }

    /// <summary>
    /// 左の小さな衛星を押せるようにする（新ホームのお知らせ）。後ろの光がゆっくり明滅して、押せるものだと分かるようにする。
    /// 背景の動く層はふだん当たり判定を持たないので、ここで初めて受け付ける（衛星の位置にはホームの部品が重ならない）。
    /// </summary>
    public void EnableMoonButton(UnityEngine.Events.UnityAction onTap, Color glow)
    {
        if (_moonGlow != null) return;
        _anim.gameObject.AddComponent<GraphicRaycaster>();
        var glowRt = SpaceTheme.Rect("Glow", _moon, SpaceTheme.CENTER, Vector2.zero, new Vector2(204f, 204f));
        glowRt.SetAsFirstSibling(); // 衛星の絵の後ろ
        _moonGlowColor = glow;
        _moonGlow = SpaceTheme.Img(glowRt, SpaceSprites.Glow, SpaceTheme.A(glow, 0.25f));
        var hit = SpaceTheme.Rect("Hit", _moon, SpaceTheme.CENTER, Vector2.zero, new Vector2(176f, 176f));
        SpaceTheme.Clickable(_moon.gameObject, SpaceTheme.HitArea(hit), onTap);
    }

    /// <summary>衛星の名前を衛星のすぐ下に出す（新ホームの「お知らせ」。衛星と一緒に漂う）</summary>
    public void SetMoonLabel(string text)
    {
        if (_moon == null) return;
        var old = _moon.Find("Label");
        if (old != null) Destroy(old.gameObject);
        var t = SpaceTheme.Text(_moon, "Label", text, 26, SpaceTheme.A(SpaceTheme.TEXT, 0.92f), SpaceTheme.CENTER,
            new Vector2(0f, -(46f + 26f)), new Vector2(240f, 38f), TMPro.FontStyles.Bold);
        t.enableAutoSizing = true;
        t.fontSizeMin = 20f;
        t.fontSizeMax = 26f;
    }

    /// <summary>
    /// 衛星から「New」の吹き出しを出す／消す（お知らせに見ていない内容があるとき）。
    /// 衛星の子なので一緒に漂い、吹き出しを押しても衛星を押したのと同じ（お知らせが開く）。
    /// </summary>
    public void SetMoonBadge(bool on)
    {
        if (_moon == null) return;
        bool shown = _badge != null && _badge.gameObject.activeSelf;
        if (on == shown) return;
        if (!on)
        {
            _badge.gameObject.SetActive(false);
            return;
        }
        if (_badge == null) BuildBadge();
        _badge.SetAsLastSibling();
        _badge.gameObject.SetActive(true);
        _badgeShownAt = Time.unscaledTime;
        _badge.localScale = Vector3.zero; // ぽんと膨らんで出る（Update）
    }

    /// <summary>白い角丸＋衛星を指す三角形のしっぽ＋「New」。後ろに桃色の光</summary>
    private void BuildBadge()
    {
        _badge = SpaceTheme.Rect("NewBadge", _moon, SpaceTheme.CENTER, BADGE_POS, BADGE_SIZE, BADGE_PIVOT);
        var glow = SpaceTheme.Rect("Glow", _badge, SpaceTheme.CENTER, Vector2.zero, new Vector2(230f, 160f));
        SpaceTheme.Img(glow, SpaceSprites.Glow, SpaceTheme.A(SpaceTheme.PINK, 0.5f));
        // しっぽ: 左下の角（先端）を TAIL_TIP に合わせる。上辺は吹き出しの中に入るので、つなぎ目は見えない
        var tail = SpaceTheme.Rect("Tail", _badge, SpaceTheme.CENTER, TAIL_TIP, TAIL_SIZE, Vector2.zero);
        SpaceTheme.Img(tail, SpaceSprites.BubbleTail, BADGE_FILL);
        var body = SpaceTheme.Rect("Body", _badge);
        SpaceTheme.Stretch(body);
        SpaceTheme.Rounded(body, BADGE_FILL, 30f, true); // 吹き出しを押してもお知らせが開くように
        var label = SpaceTheme.Text(_badge, "Label", T_NEW, 32, BADGE_TEXT, TMPro.FontStyles.Bold);
        SpaceTheme.Stretch(label.rectTransform);
    }

    private static float EaseOutBack(float x)
    {
        const float c1 = 1.70158f, c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f);
    }

    private void Update()
    {
        float t = Time.unscaledTime;

        for (int i = 0; i < _twinkles.Count; i++)
        {
            var tw = _twinkles[i];
            float s = 0.5f + 0.5f * Mathf.Sin(t * tw.Speed + tw.Phase);
            s *= s;
            var c = tw.Img.color;
            c.a = tw.BaseAlpha * (0.25f + 0.75f * s);
            tw.Img.color = c;
            float sc = 0.8f + 0.2f * s;
            tw.Rt.localScale = new Vector3(sc, sc, 1f);
        }

        // 惑星と衛星はゆっくり漂う
        _planet.anchoredPosition = _planetBase + new Vector2(0f, Mathf.Sin(t * 0.25f) * 6f);
        _moon.anchoredPosition = _moonBase + new Vector2(Mathf.Sin(t * 0.21f + 1f) * 5f, Mathf.Sin(t * 0.33f + 1f) * 4f);
        if (_moonGlow != null)
            _moonGlow.color = SpaceTheme.A(_moonGlowColor, 0.18f + 0.14f * (0.5f + 0.5f * Mathf.Sin(t * 1.4f + 1.5f)));
        foreach (var g in _tapGlows.Values)
            if (g.Img != null)
                g.Img.color = SpaceTheme.A(g.Color, 0.10f + 0.14f * (0.5f + 0.5f * Mathf.Sin(t * 1.2f + g.Phase)));

        // New の吹き出し: 出たときにぽんと膨らみ、そのあとはふわふわ上下する
        if (_badge != null && _badge.gameObject.activeSelf)
        {
            float k = Mathf.Clamp01((t - _badgeShownAt) / 0.35f);
            float s = Mathf.LerpUnclamped(0.4f, 1f, EaseOutBack(k));
            _badge.localScale = new Vector3(s, s, 1f);
            _badge.anchoredPosition = BADGE_POS + new Vector2(0f, Mathf.Sin(t * 2.4f) * 3f);
        }

        // ミニオンの「!」（2026-11-14）: 空のきずな座の右上で、ぽんと出てからふわふわ上下する（星座が動いてもついていく）
        if (_minionBadge != null && _minionBadge.gameObject.activeSelf
            && _skyFigures.TryGetValue(MinionStore.CONSTELLATION, out var bondFig) && bondFig != null)
        {
            float k = Mathf.Clamp01((t - _minionBadgeShownAt) / 0.35f);
            float s = Mathf.LerpUnclamped(0.4f, 1f, EaseOutBack(k));
            _minionBadge.localScale = new Vector3(s, s, 1f);
            float sc = bondFig.localScale.x;
            _minionBadge.anchoredPosition = bondFig.anchoredPosition + new Vector2(SKY_FIG * 0.34f, SKY_FIG * 0.34f) * sc
                                            + new Vector2(0f, Mathf.Sin(t * 2.6f) * 4f);
        }

        UpdateStreak(t);
        UpdateSky(t);
    }

    private void UpdateStreak(float now)
    {
        if (_streakT < 0f)
        {
            if (now < _nextStreak) return;
            // 画面上半分の右寄りから左下へ
            var from = new Vector2(Rand(0.35f, 1.05f) * _size.x, Rand(0.55f, 0.95f) * _size.y);
            float ang = Rand(200f, 218f) * Mathf.Deg2Rad;
            var dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
            _streakFrom = from;
            _streakTo = from + dir * (Rand(0.45f, 0.65f) * _size.x);
            _streak.localEulerAngles = new Vector3(0f, 0f, ang * Mathf.Rad2Deg);
            _streak.anchoredPosition = from;
            _streak.gameObject.SetActive(true);
            _streakT = 0f;
        }

        _streakT += Time.unscaledDeltaTime;
        float k = _streakT / STREAK_DURATION;
        if (k >= 1f)
        {
            _streak.gameObject.SetActive(false);
            _streakT = -1f;
            _nextStreak = now + Rand(5f, 10f);
            return;
        }
        float e = 1f - (1f - k) * (1f - k);
        _streak.anchoredPosition = Vector2.Lerp(_streakFrom, _streakTo, e);
        float a = Mathf.Clamp01(k / 0.15f) * Mathf.Clamp01((1f - k) / 0.4f);
        _streakImg.color = new Color(1f, 1f, 1f, a * 0.9f);
    }

    private float Rand(float min, float max) => min + (float)_rnd.NextDouble() * (max - min);

    // ================================================================
    // 空の星座
    // ================================================================
    /// <summary>完成した星座を空に並べる（新ホームだけ）。以後、完成・レベルアップを見て自動で足す</summary>
    public void EnableConstellations()
    {
        if (_skyLayer != null) return;
        _skyLayer = SpaceTheme.Rect("Constellations", _anim);
        SpaceTheme.Stretch(_skyLayer);
        _skyLayer.SetAsFirstSibling(); // 瞬く星・衛星・惑星より奥
        _nextSkyCheck = 0f;
    }

    private void UpdateSky(float now)
    {
        if (_skyLayer == null || now < _nextSkyCheck) return;
        _nextSkyCheck = now + 1f;
        // お知らせ（完成の演出など）が出ている間は待つ。閉じてから空に現れるのが見えるように
        if (!_skyFirst && SpaceNotice.AnyOpen) return;

        var all = Constellations.All;
        var progress = Constellations.Progress();
        for (int i = 0; i < progress.Count && i < SKY_SLOTS.Length; i++)
        {
            var p = progress[i];
            bool dice = p.Def.Id == SpaceDiceRoller.CONSTELLATION;
            bool tappable = Array.IndexOf(TAPPABLE, p.Def.Id) >= 0;
            // 完成した星座だけ空に出す。押すと画面が開く星座（さいころ座・金貨座）も、お客様は完成するまで出ない。
            // 管理者だけは初めから出ている（未完成なら完成した姿で。さいころ座は 2026-11-11、金貨座は 2026-11-12 ユーザー指定）。
            // 空に出している状態はふつうレベルで覚えるが、管理者のこの表示だけ -1 で覚える（管理者の判定はあとから届くため）
            bool adminShow = tappable && p.Level == 0 && AdminClaim.IsAdmin;
            int level = adminShow ? -1 : p.Level;
            _skyLevels.TryGetValue(p.Def.Id, out int shown);
            if (level == shown) continue;
            _skyLevels[p.Def.Id] = level;
            if (_skyFigures.TryGetValue(p.Def.Id, out var old) && old != null) Destroy(old.gameObject);
            _skyFigures.Remove(p.Def.Id);
            if (_tapGlows.TryGetValue(p.Def.Id, out var oldGlow))
            {
                if (oldGlow.Img != null) Destroy(oldGlow.Img.gameObject);
                _tapGlows.Remove(p.Def.Id);
            }
            if (level == 0) continue;

            int slot = Array.IndexOf(all, p.Def);
            if (slot < 0 || slot >= SKY_SLOTS.Length) continue;
            var pos = SkyPos(p.Def.Id, slot);
            var fig = SpaceTheme.Rect("Sky_" + p.Def.Id, _skyLayer, Vector2.zero, pos, new Vector2(SKY_FIG, SKY_FIG));
            // 星座ごとに少しだけ傾ける（並べたときに単調にならないように＝TiltOf）
            fig.localEulerAngles = new Vector3(0f, 0f, TiltOf(p.Def.Id, slot));
            SpaceConstellationPage.Figure(fig, p.Def, adminShow ? p.Def.Size : p.Count - p.Partial, SKY_FIG); // いまのレベルの姿
            var group = fig.gameObject.AddComponent<CanvasGroup>();
            group.alpha = SKY_ALPHA;
            // タップすると、その星座の詳細を開く（2026-11-09 ユーザー指定）。背景の動く層はふだん当たり判定を持たないので足す。
            // さいころ座はいろいろなダイスを振れる画面（2026-11-11）、金貨座はショップ（2026-11-12）を開く
            if (_anim.GetComponent<GraphicRaycaster>() == null) _anim.gameObject.AddComponent<GraphicRaycaster>();
            string id = p.Def.Id;
            if (tappable)
            {
                // 大きさは置き場ごと（ScaleOf）。星座の絵は同じ大きさで描いて、まるごと縮める
                float scale = ScaleOf(id);
                fig.localScale = new Vector3(scale, scale, 1f);
                var glowRt = SpaceTheme.Rect("Sky_" + id + "_glow", _skyLayer, Vector2.zero, pos,
                    new Vector2(SKY_FIG * 1.5f, SKY_FIG * 1.5f));
                glowRt.localScale = new Vector3(scale, scale, 1f);
                glowRt.SetSiblingIndex(fig.GetSiblingIndex()); // 星座の後ろ
                _tapGlows[id] = new TapGlow
                {
                    Img = SpaceTheme.Img(glowRt, SpaceSprites.Glow, SpaceTheme.A(p.Def.Color, 0.2f)),
                    Color = p.Def.Color,
                    // 同時に明滅しないよう、星座ごとにずらす
                    Phase = dice ? 0f : id == IgCurrency.CONSTELLATION ? Mathf.PI
                        : id == SpacePoker.CONSTELLATION ? Mathf.PI * 0.5f
                        : id == PuzzleStore.CONSTELLATION ? Mathf.PI * 0.75f
                        : id == MinionStore.CONSTELLATION ? Mathf.PI * 1.25f : Mathf.PI * 1.5f,
                };
                if (dice) SpaceTheme.Clickable(fig.gameObject, SpaceTheme.HitArea(fig), () => SpaceDiceRoller.Open());
                else if (id == IgCurrency.CONSTELLATION)
                    SpaceTheme.Clickable(fig.gameObject, SpaceTheme.HitArea(fig), () => SpaceShop.Open());
                else if (id == CoinPusher.CONSTELLATION)
                    SpaceTheme.Clickable(fig.gameObject, SpaceTheme.HitArea(fig), CoinPusher.Open);
                else if (id == PuzzleStore.CONSTELLATION) // ピースパズル（2026-11-13）
                    SpaceTheme.Clickable(fig.gameObject, SpaceTheme.HitArea(fig), () => SpacePuzzle.Open());
                else if (id == MinionStore.CONSTELLATION) // ミニオン（2026-11-14）
                    SpaceTheme.Clickable(fig.gameObject, SpaceTheme.HitArea(fig), () => SpaceMinions.Open());
                else SpaceTheme.Clickable(fig.gameObject, SpaceTheme.HitArea(fig), () => SpacePoker.Open());
            }
            else
            {
                float scale = ScaleOf(id); // 置き場ごとの大きさ
                fig.localScale = new Vector3(scale, scale, 1f);
                SpaceTheme.Clickable(fig.gameObject, SpaceTheme.HitArea(fig), () => SpaceConstellationPage.OpenDetail(id));
            }
            _skyFigures[p.Def.Id] = fig;
            if (adminShow)
            {
                // 管理者の判定が届いてから出るので、ぱっと出さずに静かに浮かび上がらせる
                if (!_skyFirst) StartCoroutine(SpaceTheme.Tween(0.6f, 0f, k => { if (group != null) group.alpha = SKY_ALPHA * k; }));
            }
            else if (!_skyFirst) StartCoroutine(SkyArrive(fig, group, p.Def.Color, id));
        }
        // 押せる星座は隣の星座と枠が少し重なることがあるので、いつもいちばん手前にして押しやすくする
        foreach (var tapId in TAPPABLE)
        {
            if (!_skyFigures.TryGetValue(tapId, out var tapFig) || tapFig == null) continue;
            if (_tapGlows.TryGetValue(tapId, out var g) && g.Img != null) g.Img.transform.SetAsLastSibling();
            tapFig.SetAsLastSibling();
        }
        UpdateMinionBadge(now);
        _skyFirst = false;
    }

    /// <summary>
    /// 帰ってきて話しかけられるのを待っているミニオンがいたら、空のきずな座の右上に金色の「!」を出す（2026-11-14）。
    /// 星座は淡く（SKY_ALPHA）描いているので、「!」は星座の子にせず、空の層の上に重ねる（押すと下のきずな座が反応する）
    /// </summary>
    private void UpdateMinionBadge(float now)
    {
        bool show = _skyFigures.TryGetValue(MinionStore.CONSTELLATION, out var fig) && fig != null && MinionStore.AnyBack;
        if (!show)
        {
            if (_minionBadge != null) _minionBadge.gameObject.SetActive(false);
            return;
        }
        if (_minionBadge == null)
        {
            _minionBadge = SpaceTheme.Rect("MinionBadge", _skyLayer, Vector2.zero, Vector2.zero, new Vector2(54f, 54f));
            var glow = SpaceTheme.Rect("Glow", _minionBadge, SpaceTheme.CENTER, Vector2.zero, new Vector2(120f, 120f));
            var glowImg = SpaceTheme.Img(glow, SpaceSprites.Glow, SpaceTheme.A(SpaceTheme.GOLD, 0.55f));
            glow.gameObject.AddComponent<SpaceTwinkle>().Init(glowImg, 0.55f);
            var dot = SpaceTheme.Rect("Dot", _minionBadge);
            SpaceTheme.Stretch(dot);
            SpaceTheme.Img(dot, SpaceSprites.Circle, SpaceTheme.GOLD);
            var mark = SpaceTheme.Text(_minionBadge, "Mark", "!", 38, SpaceTheme.TEXT_DARK, TMPro.FontStyles.Bold);
            SpaceTheme.Stretch(mark.rectTransform);
            SpaceTextScaler.Exclude(mark);
            _minionBadge.gameObject.SetActive(false);
        }
        if (!_minionBadge.gameObject.activeSelf)
        {
            _minionBadge.gameObject.SetActive(true);
            _minionBadgeShownAt = now;
        }
        _minionBadge.SetAsLastSibling();
    }

    /// <summary>
    /// 星座を空に置く場所（キャンバスの左下が 0,0）。SpaceSkyLayout の置き場（SetSkyFrame で決まる）。届く前は SKY_SLOTS の仮の場所
    /// </summary>
    private Vector2 SkyPos(string id, int slot)
    {
        if (_skySpots != null && _skySpots.TryGetValue(id, out var spot)) return new Vector2(spot.X, spot.Y);
        return new Vector2(SKY_SLOTS[slot].x * _size.x, SKY_SLOTS[slot].y * _size.y);
    }

    /// <summary>
    /// 星座の傾き（度。−10〜10）。星座ごとに決まっていて、並べたときに単調にならないように。
    /// 2026-11-14 の置き場（SpaceSkyLayout）は傾けたぶんの余裕（4%）しか見ていないので、大きく傾けない
    /// </summary>
    private static float TiltOf(string id, int slot) => ((slot * 37) % 21) - 10f;

    /// <summary>出ている星座を、今の置き場・大きさに置き直す（出てくる演出の途中の星座は位置だけ）</summary>
    private void LayoutSky()
    {
        var all = Constellations.All;
        foreach (var kv in _skyFigures)
        {
            var fig = kv.Value;
            if (fig == null) continue;
            int slot = Array.FindIndex(all, d => d.Id == kv.Key);
            if (slot < 0) continue;
            float scale = ScaleOf(kv.Key);
            var pos = SkyPos(kv.Key, slot);
            fig.anchoredPosition = pos;
            fig.localEulerAngles = new Vector3(0f, 0f, TiltOf(kv.Key, slot));
            if (!_arriving.Contains(fig)) fig.localScale = new Vector3(scale, scale, 1f);
            if (_tapGlows.TryGetValue(kv.Key, out var g) && g.Img != null)
            {
                g.Img.rectTransform.anchoredPosition = pos;
                g.Img.rectTransform.localScale = new Vector3(scale, scale, 1f);
            }
        }
    }

    /// <summary>
    /// ホームの部品の位置を受け取り、星座の置き場を決め直す（2026-11-14。NewHomeController が起動時と、
    /// INTALES WORLD のボタンを出し入れしたときに呼ぶ）。出ている星座はその場で動かす。
    /// 特に役割のない切り札座・山札座は端っこ（カードの下の帯の左右のはし）に置く（同日ユーザー指定「端っこに寄せて」）
    /// </summary>
    public void SetSkyFrame(SpaceSkyLayout.Frame frame)
    {
        var others = new List<string>();
        foreach (var d in Constellations.All)
            if (Array.IndexOf(TAPPABLE, d.Id) < 0) others.Add(d.Id);
        _skySpots = SpaceSkyLayout.Compute(frame, LEFT_TAP, RIGHT_TAP, others, CardDeck.TRUMP, CardDeck.DECK);
        LayoutSky();
    }

    /// <param name="id">星座（置き場ごとの大きさ＝ScaleOf で落ち着く）</param>
    private System.Collections.IEnumerator SkyArrive(RectTransform fig, CanvasGroup group, Color color, string id)
    {
        _arriving.Add(fig);
        float baseScale = ScaleOf(id);
        var flash = SpaceTheme.Rect("Arrive", _skyLayer, Vector2.zero, fig.anchoredPosition,
            new Vector2(SKY_FIG * 1.6f * baseScale, SKY_FIG * 1.6f * baseScale));
        var flashImg = SpaceTheme.Img(flash, SpaceSprites.Glow, SpaceTheme.A(color, 0f));
        var ring = SpaceTheme.Rect("ArriveRing", _skyLayer, Vector2.zero, fig.anchoredPosition,
            new Vector2(SKY_FIG * baseScale, SKY_FIG * baseScale));
        var ringImg = SpaceTheme.Img(ring, SpaceSprites.Ring, SpaceTheme.A(Color.white, 0f));
        const float D = 1.6f;
        for (float t = 0f; t < D; t += Time.unscaledDeltaTime)
        {
            if (fig == null) break;
            float k = t / D;
            float pop = k < 0.35f ? Mathf.Lerp(0f, 1.35f, SpaceTheme.EaseOutCubic(k / 0.35f))
                : Mathf.Lerp(1.35f, 1f, SpaceTheme.EaseOutCubic((k - 0.35f) / 0.65f));
            pop *= ScaleOf(id);
            fig.localScale = new Vector3(pop, pop, 1f);
            group.alpha = Mathf.Lerp(1f, SKY_ALPHA, Mathf.Clamp01((k - 0.4f) / 0.6f));
            flashImg.color = SpaceTheme.A(color, 0.7f * Mathf.Sin(Mathf.Clamp01(k / 0.7f) * Mathf.PI));
            float rs = Mathf.Lerp(0.4f, 2.4f, SpaceTheme.EaseOutCubic(k));
            ring.localScale = new Vector3(rs, rs, 1f);
            ringImg.color = SpaceTheme.A(Color.Lerp(color, Color.white, 0.5f), 0.8f * (1f - k));
            yield return null;
        }
        if (fig != null)
        {
            float s = ScaleOf(id);
            fig.localScale = new Vector3(s, s, 1f);
            group.alpha = SKY_ALPHA;
        }
        _arriving.Remove(fig);
        Destroy(flash.gameObject);
        Destroy(ring.gameObject);
    }
}
