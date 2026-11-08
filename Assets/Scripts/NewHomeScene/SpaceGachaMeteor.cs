using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 流れ星ガチャの演出（全画面。2026-11-06 ユーザー依頼「ガチャを引いている感があるとお客様は喜ぶ」）。
///  1. 画面が夜空になり、流星群が流れる（「流れ星に願いをこめて…」。タップか3秒ほどで次へ）
///  2. 大きな流れ星が右上から流れてきて、中央で光の玉になる
///  3. 当たりの格に合わせて、光の玉の色が段階的に上がる（青 → 金 → 虹 → 黒紫。昇格演出）。
///     良い当たり（SUPER RARE 以上）のときは、流星群に金色の筋が混じる（予兆）
///  4. 光がはじけて白く飛び、当たりのカード（SpaceNotice.StarPrizeNotice）が出る
/// タップで先へ進む（流星群 → 大きな流れ星、昇格の途中 → 結果）。戻るボタンは結果まで飛ばす。
/// 当たりの格: 5%引き=NORMAL / ドリンク・コーヒー=RARE / 7%引き=SUPER RARE / ノワール=SECRET
/// </summary>
public class SpaceGachaMeteor : MonoBehaviour
{
    public const string T_HINT = "流れ星に願いをこめて…";
    public const string T_TAP = "画面をタップ";

    /// <summary>この演出の文字（NewHome の起動時にまとめてフォントへ登録する）</summary>
    public static string GlyphText => T_HINT + T_TAP + "NORMALRESUPCT ";

    private enum Phase { Intro, Shower, Meteor, Impact, Charge, Final, Reveal }

    private const float ORB_Y = 80f;          // 光の玉の位置（画面の中央から）
    private const float SHOWER_MAX = 3.4f;    // タップしなくても次へ進むまで（秒）
    private const float UPGRADE_FIRST = 0.7f; // 光の玉ができてから最初の昇格まで
    private const float UPGRADE_GAP = 0.85f;  // 昇格の間隔

    private static readonly Color BG = new Color(0.01f, 0.015f, 0.06f, 1f);
    private static readonly Color C_NORMAL = SpaceTheme.CYAN;
    private static readonly Color C_RARE = SpaceTheme.Hex(0xFFC84A);
    private static readonly Color C_SECRET = SpaceTheme.Hex(0x9B7BFF);
    private static readonly Color C_SECRET_CORE = SpaceTheme.Hex(0x07040F);

    /// <summary>当たりの格（0 NORMAL / 1 RARE / 2 SUPER RARE / 3 SECRET）</summary>
    public static int TierOf(StarPrize p)
    {
        switch (p)
        {
            case StarPrize.Five: return 0;
            case StarPrize.Drink:
            case StarPrize.Coffee: return 1;
            case StarPrize.Seven: return 2;
            default: return 3; // ノワール・5時間無料・フリータイム（SECRET）
        }
    }

    public static string TierName(int tier) =>
        tier <= 0 ? "NORMAL" : tier == 1 ? "RARE" : tier == 2 ? "SUPER RARE" : "SECRET";

    /// <summary>格の色（SUPER RARE は時間で色が回る虹色）</summary>
    public static Color TierColor(int tier, float time)
    {
        if (tier <= 0) return C_NORMAL;
        if (tier == 1) return C_RARE;
        if (tier == 2) return Color.HSVToRGB(Mathf.Repeat(time * 0.35f, 1f), 0.55f, 1f);
        return C_SECRET;
    }

    private struct Star { public Image Img; public float Base, Speed, Phase; }
    private struct Streak { public RectTransform Rt; public Image Img; public Vector2 From, To; public float Life, Dur; public Color Color; public bool On; }
    private struct Particle { public RectTransform Rt; public Image Img; public Vector2 Pos, Vel; public float Life, Dur, Size; public Color Color; public bool On; }
    private struct Ring { public RectTransform Rt; public Image Img; public float Life, Dur; public Color Color; public bool On; }

    private StarPrize _prize;
    private int _tier, _step;
    private Phase _phase;
    private float _t, _pt;
    private bool _tapped;
    private Vector2 _size;
    private CanvasGroup _group;
    private RectTransform _shake, _rayRoot, _orb, _meteor;
    private Image _bg, _nebula, _flash, _meteorImg, _orbOuter, _orbInner, _orbCore;
    private TextMeshProUGUI _hint, _tap;
    private Vector2 _mFrom, _mTo;
    private float _flashA, _raysA, _shakeLeft, _nextShower, _nextTrail, _orbScale;
    private readonly List<Star> _stars = new List<Star>();
    private readonly List<Image> _rays = new List<Image>();
    private readonly Streak[] _shower = new Streak[12];
    private readonly Particle[] _particles = new Particle[80];
    private readonly Ring[] _rings = new Ring[4];

    /// <summary>流れ星ガチャの演出を始める。結果のカードが出たら自分は消える（お知らせの順番待ちはそのカードを待つ）</summary>
    public static GameObject Play(StarPrize prize)
    {
        var canvas = SpaceTheme.MainCanvas();
        if (canvas == null) return SpaceNotice.StarPrizeNotice(prize);
        var root = SpaceTheme.Rect("__StarGacha", canvas.transform);
        SpaceTheme.Stretch(root);
        root.SetAsLastSibling();
        SpaceTheme.SubCanvas(root.gameObject, true); // 毎フレーム動くので、メインのキャンバスと分ける
        var g = root.gameObject.AddComponent<SpaceGachaMeteor>();
        g._prize = prize;
        g._tier = TierOf(prize);
        g._size = SpaceTheme.CanvasSize(canvas);
        g.Build();
        SpaceNotice.Track(root.gameObject); // 戻るボタン（SpaceNotice.CloseTop）は Skip を呼ぶ
        return root.gameObject;
    }

    /// <summary>戻るボタン: 演出を飛ばして結果へ（もう結果なら何もしない）</summary>
    public void Skip()
    {
        if (_phase >= Phase.Final) return;
        _step = _tier;
        _meteorImg.color = Color.clear;
        _orbScale = 1f;
        _hint.alpha = 0f;
        _tap.alpha = 0f;
        Go(Phase.Final);
    }

    // ================================================================
    // 組み立て
    // ================================================================
    private void Build()
    {
        var rt = (RectTransform)transform;
        _group = gameObject.AddComponent<CanvasGroup>();

        var bg = SpaceTheme.Rect("Bg", rt);
        SpaceTheme.Stretch(bg);
        _bg = SpaceTheme.Img(bg, null, new Color(BG.r, BG.g, BG.b, 0f), true);
        SpaceTheme.Clickable(bg.gameObject, _bg, () => _tapped = true, false);

        var neb = SpaceTheme.Rect("Nebula", rt, SpaceTheme.CENTER, new Vector2(0f, ORB_Y), new Vector2(1900f, 1900f));
        _nebula = SpaceTheme.Img(neb, SpaceSprites.Glow, Color.clear);

        for (int i = 0; i < 70; i++)
        {
            float s = Random.Range(4f, 12f);
            var st = SpaceTheme.Rect("Star", rt, SpaceTheme.CENTER,
                new Vector2(Random.Range(-0.5f, 0.5f) * _size.x, Random.Range(-0.5f, 0.5f) * _size.y), new Vector2(s, s));
            _stars.Add(new Star
            {
                Img = SpaceTheme.Img(st, SpaceSprites.Glow, Color.clear),
                Base = Random.Range(0.3f, 0.95f),
                Speed = Random.Range(1.5f, 4f),
                Phase = Random.Range(0f, 6.28f),
            });
        }

        _shake = SpaceTheme.Rect("Shake", rt);
        SpaceTheme.Stretch(_shake);

        // 流星群（光の筋の絵は右端が頭。頭を基準点にして進む向きへ回す）
        for (int i = 0; i < _shower.Length; i++)
        {
            var s = SpaceTheme.Rect("Shower", _shake, SpaceTheme.CENTER, Vector2.zero, new Vector2(300f, 16f),
                new Vector2(1f, 0.5f));
            _shower[i] = new Streak { Rt = s, Img = SpaceTheme.Img(s, SpaceSprites.Streak, Color.clear) };
        }

        // 放射する光の線
        _rayRoot = SpaceTheme.Rect("Rays", _shake, SpaceTheme.CENTER, new Vector2(0f, ORB_Y), Vector2.zero);
        for (int i = 0; i < 10; i++)
        {
            var r = SpaceTheme.Rect("Ray", _rayRoot, SpaceTheme.CENTER, Vector2.zero, new Vector2(1500f, 12f));
            r.localEulerAngles = new Vector3(0f, 0f, i * 18f);
            _rays.Add(SpaceTheme.Img(r, SpaceSprites.FadeLine, Color.clear));
        }

        // 衝撃波の輪
        for (int i = 0; i < _rings.Length; i++)
        {
            var r = SpaceTheme.Rect("Ring", _shake, SpaceTheme.CENTER, new Vector2(0f, ORB_Y), new Vector2(420f, 420f));
            _rings[i] = new Ring { Rt = r, Img = SpaceTheme.Img(r, SpaceSprites.Ring, Color.clear) };
        }

        // 光の玉
        _orb = SpaceTheme.Rect("Orb", _shake, SpaceTheme.CENTER, new Vector2(0f, ORB_Y), Vector2.one);
        _orbOuter = SpaceTheme.Img(SpaceTheme.Rect("Outer", _orb, SpaceTheme.CENTER, Vector2.zero, new Vector2(680f, 680f)),
            SpaceSprites.Glow, Color.clear);
        _orbInner = SpaceTheme.Img(SpaceTheme.Rect("Inner", _orb, SpaceTheme.CENTER, Vector2.zero, new Vector2(320f, 320f)),
            SpaceSprites.Glow, Color.clear);
        _orbCore = SpaceTheme.Img(SpaceTheme.Rect("Core", _orb, SpaceTheme.CENTER, Vector2.zero, new Vector2(120f, 120f)),
            SpaceSprites.Circle, Color.clear);
        _orb.localScale = Vector3.zero;

        // 大きな流れ星
        _meteor = SpaceTheme.Rect("Meteor", _shake, SpaceTheme.CENTER, Vector2.zero, new Vector2(1100f, 64f),
            new Vector2(1f, 0.5f));
        _meteorImg = SpaceTheme.Img(_meteor, SpaceSprites.Streak, Color.clear);

        // 光の粒
        for (int i = 0; i < _particles.Length; i++)
        {
            var p = SpaceTheme.Rect("Particle", _shake, SpaceTheme.CENTER, Vector2.zero, new Vector2(12f, 12f));
            _particles[i] = new Particle
            {
                Rt = p,
                Img = SpaceTheme.Img(p, i % 3 == 0 ? SpaceSprites.Sparkle : SpaceSprites.Glow, Color.clear),
            };
        }

        _hint = SpaceTheme.Text(rt, "Hint", T_HINT, 42, SpaceTheme.TEXT, new Vector2(0.5f, 0f), new Vector2(0f, 470f),
            new Vector2(1000f, 64f), FontStyles.Bold);
        _tap = SpaceTheme.Text(rt, "Tap", T_TAP, 30, SpaceTheme.TEXT_SUB, new Vector2(0.5f, 0f), new Vector2(0f, 396f),
            new Vector2(800f, 44f), FontStyles.Bold);
        _hint.alpha = 0f;
        _tap.alpha = 0f;

        var fl = SpaceTheme.Rect("Flash", rt);
        SpaceTheme.Stretch(fl);
        _flash = SpaceTheme.Img(fl, null, new Color(1f, 1f, 1f, 0f));

        _phase = Phase.Intro;
        _nextShower = 0.25f;
    }

    // ================================================================
    // 進行
    // ================================================================
    private void Go(Phase next)
    {
        _phase = next;
        _pt = 0f;
        switch (next)
        {
            case Phase.Meteor:
                _tapped = false;
                _mFrom = new Vector2(_size.x * 0.62f, _size.y * 0.46f);
                _mTo = new Vector2(0f, ORB_Y);
                var dir = _mTo - _mFrom;
                _meteor.localEulerAngles = new Vector3(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
                _meteor.anchoredPosition = _mFrom;
                GachaSound.Whoosh();
                break;
            case Phase.Impact:
                _meteorImg.color = Color.clear;
                _flashA = 0.8f;
                AddRing(Color.white);
                Burst(28, C_NORMAL);
                GachaSound.Pon();
                break;
            case Phase.Final:
                _tapped = false;
                break;
            case Phase.Reveal:
                _flashA = 1f;
                var card = SpaceNotice.StarPrizeNotice(_prize);
                if (card != null) TapGuard(card, 0.7f); // 連打で結果をすぐ閉じてしまわないように
                GachaSound.Reveal(_tier);
                break;
        }
    }

    private void Update()
    {
        float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
        _t += dt;
        _pt += dt;

        switch (_phase)
        {
            case Phase.Intro:
                _bg.color = new Color(BG.r, BG.g, BG.b, 0.96f * Mathf.Clamp01(_pt / 0.35f));
                if (_pt >= 0.35f) Go(Phase.Shower);
                break;
            case Phase.Shower:
                _hint.alpha = Mathf.Clamp01((_pt - 0.2f) / 0.5f);
                _tap.alpha = _pt > 1f ? 0.55f + 0.45f * Mathf.Sin((_pt - 1f) * 4f) : 0f;
                if (_t >= _nextShower)
                {
                    SpawnShower();
                    _nextShower = _t + Random.Range(0.16f, 0.34f);
                }
                if (_tapped || _pt >= SHOWER_MAX) Go(Phase.Meteor);
                break;
            case Phase.Meteor:
                _hint.alpha = Mathf.MoveTowards(_hint.alpha, 0f, dt * 4f);
                _tap.alpha = Mathf.MoveTowards(_tap.alpha, 0f, dt * 4f);
                UpdateMeteor();
                break;
            case Phase.Impact:
                _orbScale = EaseOutBack(Mathf.Clamp01(_pt / 0.4f));
                if (_pt >= 0.5f) Go(Phase.Charge);
                break;
            case Phase.Charge:
                UpdateCharge();
                break;
            case Phase.Final:
            {
                float p = Mathf.Clamp01(_pt / 0.55f);
                _orbScale = Mathf.Lerp(1f, 2.8f, p * p);
                _raysA = Mathf.Lerp(_raysA, 0.95f, p);
                _flashA = Mathf.Max(_flashA, p * p);
                if (p >= 1f) Go(Phase.Reveal);
                break;
            }
            case Phase.Reveal:
                _group.alpha = 1f - Mathf.Clamp01(_pt / 0.45f);
                if (_pt >= 0.45f)
                {
                    Destroy(gameObject);
                    return;
                }
                break;
        }

        UpdateScene(dt);
    }

    private void UpdateMeteor()
    {
        const float DUR = 0.8f;
        float p = Mathf.Clamp01(_pt / DUR);
        float e = 1f - Mathf.Pow(1f - p, 3f); // 勢いよく来て、中央でゆっくり止まる
        var head = Vector2.Lerp(_mFrom, _mTo, e);
        _meteor.anchoredPosition = head;
        _meteor.localScale = new Vector3(Mathf.Lerp(1f, 0.4f, e), 1f, 1f); // 遅くなるほど尾が短く
        float a = p < 0.85f ? 1f : 1f - (p - 0.85f) / 0.15f;
        _meteorImg.color = SpaceTheme.A(Color.Lerp(Color.white, C_NORMAL, 0.25f), a);
        if (_t >= _nextTrail)
        {
            _nextTrail = _t + 0.02f;
            var back = (_mFrom - _mTo).normalized;
            SpawnParticle(head + Random.insideUnitCircle * 10f, back * Random.Range(40f, 140f) + Random.insideUnitCircle * 60f,
                Random.Range(0.45f, 0.8f), Random.Range(8f, 20f), Color.Lerp(Color.white, C_NORMAL, Random.value));
        }
        if (p >= 1f) Go(Phase.Impact);
    }

    private void UpdateCharge()
    {
        _orbScale = 1f;
        // 当たりの格まで、色が1段ずつ上がる（昇格）
        if (_step < _tier && _pt >= UPGRADE_FIRST + _step * UPGRADE_GAP)
        {
            _step++;
            var c = TierColor(_step, _t);
            _flashA = 0.55f;
            _shakeLeft = 0.3f;
            AddRing(c);
            Burst(32, c);
            _raysA = Mathf.Max(_raysA, 0.35f + 0.15f * _step);
            GachaSound.Upgrade(_step);
        }
        float end = _tier > 0 ? UPGRADE_FIRST + (_tier - 1) * UPGRADE_GAP + 0.95f : 0.9f;
        if (_tapped || _pt >= end)
        {
            _step = _tier;
            Go(Phase.Final);
        }
    }

    /// <summary>毎フレームの見た目（星・流星群・光の玉・光の線・輪・粒・白い光・揺れ）</summary>
    private void UpdateScene(float dt)
    {
        float intro = Mathf.Clamp01(_t / 0.5f);
        foreach (var s in _stars)
            s.Img.color = new Color(0.9f, 0.95f, 1f, intro * s.Base * (0.55f + 0.45f * Mathf.Sin(_t * s.Speed + s.Phase)));

        var c = TierColor(_step, _t);
        bool orbOn = _phase >= Phase.Impact;
        float nebA = orbOn ? 0.10f + 0.07f * _step : 0.06f;
        var nebColor = orbOn ? c : _tier >= 3 && _phase == Phase.Shower && _pt > 2f ? C_SECRET : SpaceTheme.VIOLET;
        _nebula.color = SpaceTheme.A(nebColor, nebA * intro);

        for (int i = 0; i < _shower.Length; i++)
        {
            var s = _shower[i];
            if (!s.On) continue;
            s.Life += dt;
            float p = s.Life / s.Dur;
            if (p >= 1f)
            {
                s.On = false;
                s.Img.color = Color.clear;
            }
            else
            {
                s.Rt.anchoredPosition = Vector2.Lerp(s.From, s.To, p);
                s.Img.color = SpaceTheme.A(s.Color, Mathf.Sin(p * Mathf.PI) * 0.9f);
            }
            _shower[i] = s;
        }

        if (orbOn)
        {
            float pulse = 1f + 0.06f * Mathf.Sin(_t * 9f);
            float s = _orbScale * pulse;
            _orb.localScale = new Vector3(s, s, 1f);
            _orbOuter.color = SpaceTheme.A(c, 0.55f);
            _orbInner.color = SpaceTheme.A(Color.Lerp(c, Color.white, 0.45f), 0.9f);
            _orbCore.color = _step >= 3 ? C_SECRET_CORE : Color.Lerp(c, Color.white, 0.75f);
        }

        _rayRoot.localEulerAngles = new Vector3(0f, 0f, _t * (_phase == Phase.Final ? 90f : 16f));
        for (int i = 0; i < _rays.Count; i++)
            _rays[i].color = SpaceTheme.A(c, _raysA * (0.55f + 0.45f * Mathf.Sin(_t * 3f + i * 1.7f)));

        for (int i = 0; i < _rings.Length; i++)
        {
            var r = _rings[i];
            if (!r.On) continue;
            r.Life += dt;
            float p = r.Life / r.Dur;
            if (p >= 1f)
            {
                r.On = false;
                r.Img.color = Color.clear;
            }
            else
            {
                float k = 1f - Mathf.Pow(1f - p, 3f);
                float sc = Mathf.Lerp(0.3f, 2.6f, k);
                r.Rt.localScale = new Vector3(sc, sc, 1f);
                r.Img.color = SpaceTheme.A(r.Color, (1f - p) * 0.85f);
            }
            _rings[i] = r;
        }

        for (int i = 0; i < _particles.Length; i++)
        {
            var p = _particles[i];
            if (!p.On) continue;
            p.Life += dt;
            float k = p.Life / p.Dur;
            if (k >= 1f)
            {
                p.On = false;
                p.Img.color = Color.clear;
            }
            else
            {
                p.Pos += p.Vel * dt;
                p.Vel *= 1f - Mathf.Clamp01(dt * 1.8f);
                p.Rt.anchoredPosition = p.Pos;
                float sz = p.Size * (1f - 0.5f * k);
                p.Rt.sizeDelta = new Vector2(sz, sz);
                p.Img.color = SpaceTheme.A(p.Color, 1f - k);
            }
            _particles[i] = p;
        }

        if (_phase != Phase.Final && _phase != Phase.Reveal) _flashA = Mathf.MoveTowards(_flashA, 0f, dt * 2.2f);
        _flash.color = new Color(1f, 1f, 1f, _flashA);

        if (_shakeLeft > 0f)
        {
            _shakeLeft -= dt;
            _shake.anchoredPosition = Random.insideUnitCircle * 16f * Mathf.Clamp01(_shakeLeft / 0.3f);
        }
        else
        {
            _shake.anchoredPosition = Vector2.zero;
        }
    }

    // ================================================================
    // 部品
    // ================================================================
    private void SpawnShower()
    {
        for (int i = 0; i < _shower.Length; i++)
        {
            if (_shower[i].On) continue;
            var s = _shower[i];
            float ang = Random.Range(198f, 214f) * Mathf.Deg2Rad;
            var dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
            s.From = new Vector2(Random.Range(-0.15f, 0.65f) * _size.x, Random.Range(0.05f, 0.5f) * _size.y);
            s.To = s.From + dir * Random.Range(650f, 1100f);
            s.Dur = Random.Range(0.5f, 0.85f);
            s.Life = 0f;
            s.On = true;
            // 予兆: SUPER RARE 以上なら、途中から金色の筋が混じる
            bool hint = _tier >= 2 && _pt > 1.4f && Random.value < 0.45f;
            s.Color = hint ? C_RARE : Color.Lerp(Color.white, C_NORMAL, Random.value * 0.5f);
            s.Rt.sizeDelta = new Vector2(Random.Range(240f, 420f), Random.Range(12f, 20f));
            s.Rt.localEulerAngles = new Vector3(0f, 0f, ang * Mathf.Rad2Deg);
            s.Rt.anchoredPosition = s.From;
            _shower[i] = s;
            return;
        }
    }

    private void SpawnParticle(Vector2 pos, Vector2 vel, float dur, float size, Color color)
    {
        for (int i = 0; i < _particles.Length; i++)
        {
            if (_particles[i].On) continue;
            var p = _particles[i];
            p.Pos = pos;
            p.Vel = vel;
            p.Dur = dur;
            p.Life = 0f;
            p.Size = size;
            p.Color = color;
            p.On = true;
            p.Rt.anchoredPosition = pos;
            p.Rt.sizeDelta = new Vector2(size, size);
            _particles[i] = p;
            return;
        }
    }

    /// <summary>光の玉から光の粒がはじける</summary>
    private void Burst(int count, Color color)
    {
        var center = new Vector2(0f, ORB_Y);
        for (int i = 0; i < count; i++)
        {
            float a = Random.Range(0f, Mathf.PI * 2f);
            var v = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Random.Range(260f, 980f);
            SpawnParticle(center, v, Random.Range(0.6f, 1.15f), Random.Range(10f, 28f),
                Color.Lerp(color, Color.white, Random.value * 0.6f));
        }
    }

    private void AddRing(Color color)
    {
        for (int i = 0; i < _rings.Length; i++)
        {
            if (_rings[i].On) continue;
            var r = _rings[i];
            r.Life = 0f;
            r.Dur = 0.7f;
            r.Color = color;
            r.On = true;
            _rings[i] = r;
            return;
        }
    }

    /// <summary>結果のカードの上に、しばらくタップを受け止める透明な面を置く</summary>
    private static void TapGuard(GameObject card, float seconds)
    {
        var guard = SpaceTheme.Rect("TapGuard", card.transform);
        SpaceTheme.Stretch(guard);
        guard.SetAsLastSibling();
        SpaceTheme.HitArea(guard);
        guard.gameObject.AddComponent<SpaceTimedDestroy>().Seconds = seconds;
    }

    private static float EaseOutBack(float x)
    {
        const float c1 = 1.70158f, c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f);
    }
}

/// <summary>決めた秒数がたったら自分を消す</summary>
public class SpaceTimedDestroy : MonoBehaviour
{
    public float Seconds = 1f;

    private void Update()
    {
        Seconds -= Time.unscaledDeltaTime;
        if (Seconds <= 0f) Destroy(gameObject);
    }
}

/// <summary>
/// 流れ星ガチャの効果音（実行時に波形を合成。音声アセット不要。VisitChime と同じベル風の音色）。
/// 流れる音（下がっていくきらめき）・光の玉ができる音・昇格（段階ごとに高く）・結果のファンファーレ（格ごとに豪華に）
/// </summary>
public static class GachaSound
{
    private const int RATE = 44100;
    private static AudioSource _source;
    private static AudioClip _whoosh, _pon;
    private static readonly AudioClip[] _up = new AudioClip[4];
    private static readonly AudioClip[] _reveal = new AudioClip[4];

    public static void Whoosh() => Play(_whoosh ? _whoosh : (_whoosh = MakeWhoosh()), 0.7f);
    public static void Pon() => Play(_pon ? _pon : (_pon = Bells("gacha_pon", (1318.5f, 0f, 0.35f))), 0.8f);

    public static void Upgrade(int step)
    {
        int i = Mathf.Clamp(step, 1, 3);
        if (_up[i] == null)
        {
            float f = 880f * Mathf.Pow(2f, (i - 1) * 3f / 12f);
            _up[i] = Bells("gacha_up" + i, (f, 0f, 0.14f), (f * 1.5f, 0.11f, 0.5f));
        }
        Play(_up[i], 0.85f);
    }

    public static void Reveal(int tier)
    {
        int i = Mathf.Clamp(tier, 0, 3);
        if (_reveal[i] == null)
        {
            var notes = new List<(float, float, float)>
            {
                (1046.5f, 0.00f, 0.5f), (1318.5f, 0.07f, 0.55f), (1568.0f, 0.14f, 0.6f), (2093.0f, 0.21f, 0.9f),
            };
            if (i >= 2) notes.Add((2637.0f, 0.30f, 1.1f));
            if (i >= 3) notes.Add((784.0f, 0.00f, 1.4f));
            _reveal[i] = Bells("gacha_reveal" + i, notes.ToArray());
        }
        Play(_reveal[i], 0.9f);
    }

    private static void Play(AudioClip clip, float volume)
    {
        if (clip == null) return;
        if (_source == null)
        {
            var go = new GameObject("__GachaSound");
            Object.DontDestroyOnLoad(go);
            _source = go.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0f;
        }
        _source.PlayOneShot(clip, volume);
    }

    /// <summary>下がっていくきらめき（流れ星が流れる音）</summary>
    private static AudioClip MakeWhoosh()
    {
        const float dur = 0.75f;
        int n = (int)(dur * RATE);
        var data = new float[n];
        double phase = 0, phase2 = 0;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)RATE;
            float f = 2200f * Mathf.Pow(0.35f, t / dur);
            phase += 2.0 * Mathf.PI * f / RATE;
            phase2 += 2.0 * Mathf.PI * f * 3.01f / RATE;
            float env = Mathf.Min(1f, t / 0.04f) * Mathf.Exp(-2.4f * t);
            float trem = 0.6f + 0.4f * Mathf.Sin(2f * Mathf.PI * 18f * t);
            data[i] = (float)(Mathf.Sin((float)phase) * 0.32f + Mathf.Sin((float)phase2) * 0.12f * trem) * env;
        }
        var clip = AudioClip.Create("gacha_whoosh", n, 1, RATE, false);
        clip.SetData(data, 0);
        return clip;
    }

    /// <summary>ベル風の音を並べて1つのクリップにする（基音＋2・3倍音、早めに減衰）</summary>
    private static AudioClip Bells(string name, params (float freq, float start, float dur)[] notes)
    {
        float total = 0f;
        foreach (var x in notes) total = Mathf.Max(total, x.start + x.dur);
        int length = (int)((total + 0.05f) * RATE);
        var data = new float[length];
        foreach (var x in notes)
        {
            int offset = (int)(x.start * RATE);
            int count = (int)(x.dur * RATE);
            for (int i = 0; i < count && offset + i < length; i++)
            {
                float t = i / (float)RATE;
                float env = Mathf.Min(1f, t / 0.005f) * Mathf.Exp(-3.5f * t / x.dur);
                float w = Mathf.Sin(2f * Mathf.PI * x.freq * t)
                          + 0.35f * Mathf.Sin(4f * Mathf.PI * x.freq * t) * Mathf.Exp(-6f * t / x.dur)
                          + 0.12f * Mathf.Sin(6f * Mathf.PI * x.freq * t) * Mathf.Exp(-9f * t / x.dur);
                data[offset + i] += w * env * 0.32f;
            }
        }
        for (int i = 0; i < length; i++) data[i] = Mathf.Clamp(data[i], -1f, 1f);
        var clip = AudioClip.Create(name, length, 1, RATE, false);
        clip.SetData(data, 0);
        return clip;
    }
}
