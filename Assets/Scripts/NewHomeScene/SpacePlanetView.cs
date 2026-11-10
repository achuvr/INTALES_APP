using System.Collections;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 惑星1つの見た目（ホーム中央・惑星選択のプレビュー・一覧のサムネイルで共通）。
///   奥: まわりの光 → 環の奥半分 → 球（地表 → 夜側で光る層 → 雲）→ 大気の縁 → 環の手前半分
/// 枠（Size）に対して球の直径は SPHERE_RATIO 倍（光や環は枠からはみ出してよい）。
/// ルミナ（IsOrb）は最初からあるシアン〜すみれ色のオーブをそのまま描く。
///
/// テクスチャは SpacePlanets から非同期で受け取る。まず小（すぐできる）を貼り、Large なら大ができ次第差し替える。
/// テクスチャが届くまでは何も見せず、届いたらふわっと出す。
/// </summary>
public class SpacePlanetView : MonoBehaviour
{
    public enum Quality { Thumb, Large }

    /// <summary>枠に対する球の直径（0.8。ホームの枠 475 → 直径 380 = 元のオーブと同じ）</summary>
    public const float SPHERE_RATIO = 0.8f;

    public SpacePlanetDef Def { get; private set; }
    public float Size { get; private set; }
    /// <summary>自転させるか（隠れている間は止めて負荷を減らす）</summary>
    public bool Animate { get; set; }

    private Quality _quality;
    private bool _locked;
    private bool _faded;
    private CanvasGroup _group;
    private SpaceSphere _surface, _night, _clouds;
    private Image _ringBack, _ringFront, _halo;
    // 稲妻の明滅（Def.Flicker）
    private float _flashT = -1f, _nextFlash, _shownFlash = -1f;
    private SpacePlanets.TexSet _set;
    private float _phase, _cloudPhase, _pendingDt;
    private const float SPIN_INTERVAL = 1f / 30f;

    /// <param name="locked">未解放の惑星（色を落とした影の姿で見せる。雲・夜の光・明滅は出さない）</param>
    /// <param name="faded">
    /// 力を失った惑星（2026-11-10。色を白く淡くした版のテクスチャで描き、夜の光・光の脈動・明滅は出さない。
    /// SpacePlanets.IsExhausted を渡す）
    /// </param>
    public static SpacePlanetView Create(Transform parent, SpacePlanetDef def, float size, Quality quality, bool animate,
        bool locked = false, bool faded = false)
    {
        var rt = SpaceTheme.Rect("Planet_" + def.Id, parent, SpaceTheme.CENTER, Vector2.zero, new Vector2(size, size));
        var view = rt.gameObject.AddComponent<SpacePlanetView>();
        view.Def = def;
        view.Size = size;
        view._quality = quality;
        view.Animate = animate;
        view._locked = locked; // ルミナ（オーブ）も、最初にほかの色を選んだ人には未解放の影で出す（2026-11-10）
        view._faded = faded && !locked;
        view._phase = view._cloudPhase = def.StartSpin;
        view.Build();
        return view;
    }

    /// <summary>未解放の惑星の地表の色（ほぼ黒の影。縁の光だけで形がわかる）</summary>
    public static readonly Color LOCKED_TINT = new Color(0.13f, 0.14f, 0.22f, 1f);

    /// <summary>まわりの光の色（力を失った惑星は淡くする）</summary>
    private Color HaloColor => _faded ? SpacePlanets.FadedColor(Def.Accent) : Def.Accent;

    /// <summary>自転の位相をそろえる（選択画面から飛んできた惑星とホームの惑星を入れ替えるとき）</summary>
    public void CopyPhase(SpacePlanetView other)
    {
        if (other == null) return;
        _phase = other._phase;
        _cloudPhase = other._cloudPhase;
        ApplySpin();
    }

    // ================================================================
    // 組み立て
    // ================================================================
    private void Build()
    {
        var rt = (RectTransform)transform;
        _group = gameObject.AddComponent<CanvasGroup>();
        _group.blocksRaycasts = false;
        _group.interactable = false;

        if (Def.IsOrb)
        {
            BuildOrb(rt, Size * SPHERE_RATIO, _locked, _faded);
            return;
        }

        float d = Size * SPHERE_RATIO * Def.Scale;
        var halo = SpaceTheme.Rect("Halo", rt, SpaceTheme.CENTER, Vector2.zero,
            new Vector2(d * Def.HaloScale, d * Def.HaloScale));
        _halo = SpaceTheme.Img(halo, SpaceSprites.Glow,
            SpaceTheme.A(HaloColor, Def.HaloAlpha * (_locked ? 0.25f : _faded ? 0.6f : 1f)));

        if (Def.Ring) _ringBack = RingImage("RingBack", rt, d, false);

        var body = SpaceTheme.Rect("Body", rt, SpaceTheme.CENTER, Vector2.zero, new Vector2(d, d));
        bool large = _quality == Quality.Large;
        int seg = large ? 48 : 32, rings = large ? 16 : 10;
        _surface = Sphere("Surface", body, SpaceSphere.Shading.Lit, 0f, seg, rings);
        _night = Sphere("Night", body, SpaceSphere.Shading.Night, Def.GlowBase, seg, rings);
        _clouds = Sphere("Clouds", body, SpaceSphere.Shading.Lit, 0f, seg, rings);
        if (_locked) _surface.color = LOCKED_TINT;

        var rim = SpaceTheme.Rect("Rim", body, SpaceTheme.CENTER, Vector2.zero, new Vector2(d / 0.8f, d / 0.8f));
        SpaceTheme.Img(rim, Def.RimEven ? SpaceSprites.PlanetRimEven : SpaceSprites.PlanetRim,
            SpaceTheme.A(_faded ? SpacePlanets.FadedColor(Def.Rim) : Def.Rim, Def.RimAlpha * (_locked ? 0.55f : 1f)));

        if (Def.Ring) _ringFront = RingImage("RingFront", rt, d, true);

        ApplySpin();
        var cached = SpacePlanets.Peek(Def, large, _faded) ?? SpacePlanets.Peek(Def, false, _faded);
        if (cached != null) Use(cached, false);
        else _group.alpha = 0f; // テクスチャが届くまで隠す
        if (cached == null || (large && !cached.Large)) LoadTextures().Forget();
    }

    private SpaceSphere Sphere(string name, RectTransform parent, SpaceSphere.Shading shading, float nightBase,
        int seg, int rings)
    {
        var rt = SpaceTheme.Rect(name, parent);
        SpaceTheme.Stretch(rt);
        var s = rt.gameObject.AddComponent<SpaceSphere>();
        s.Setup(Def.Tilt, Def.Incline, Def.Ambient, shading, nightBase, seg, rings);
        s.enabled = false; // テクスチャが届いてから描く（未設定だと白い円になる）
        return s;
    }

    /// <summary>環（見る角度でつぶした楕円の画像を自転軸と同じだけ傾ける）。手前側は下半分だけを描く</summary>
    private Image RingImage(string name, RectTransform parent, float d, bool front)
    {
        float w = d * Def.RingOuter;
        float h = w * Mathf.Sin(Def.Incline * Mathf.Deg2Rad);
        var rt = SpaceTheme.Rect(name, parent, SpaceTheme.CENTER, Vector2.zero, new Vector2(w, h));
        rt.localEulerAngles = new Vector3(0f, 0f, Def.Tilt);
        var img = SpaceTheme.Img(rt, null, Color.white);
        if (front)
        {
            img.type = Image.Type.Filled;
            img.fillMethod = Image.FillMethod.Radial360;
            img.fillOrigin = (int)Image.Origin360.Right;
            img.fillClockwise = true;
            img.fillAmount = 0.5f; // 右→下→左の半周＝手前側
        }
        img.enabled = false; // 環の画像ができてから描く
        return img;
    }

    /// <summary>
    /// ルミナ: 元の新ホームのオーブ（d=380 のとき元と同じ寸法）。locked なら、ほかの未解放の惑星と同じ
    /// ほぼ黒の影にして、光をほとんど消す（最初にほかの色を選んだ人の、惑星を選ぶ画面）
    /// </summary>
    private static void BuildOrb(RectTransform rt, float d, bool locked, bool faded)
    {
        float k = d / 380f;
        bool small = d < 250f;
        // 力を失ったとき（faded）は、ほかの惑星と同じ割合で色を白く淡くする
        Color Tone(Color c) => faded ? SpacePlanets.FadedColor(c) : c;
        var halo = SpaceTheme.Rect("Halo", rt, SpaceTheme.CENTER, Vector2.zero, new Vector2(520f * k, 520f * k));
        SpaceTheme.Img(halo, SpaceSprites.Glow, Tone(new Color(0.51f, 0.43f, 1f, locked ? 0.12f : faded ? 0.3f : 0.47f)));
        var face = SpaceTheme.Rect("Face", rt, SpaceTheme.CENTER, Vector2.zero, new Vector2(d, d));
        if (locked) SpaceTheme.Img(face, SpaceSprites.Circle, LOCKED_TINT);
        else
            SpaceTheme.Gradient(SpaceTheme.Img(face, SpaceSprites.Circle, Color.white),
                Tone(new Color(0.34f, 0.86f, 1f, 1f)), Tone(new Color(0.59f, 0.38f, 1f, 1f)));
        var sheen = SpaceTheme.Rect("Sheen", rt, SpaceTheme.CENTER, new Vector2(-70f * k, 80f * k),
            new Vector2(300f * k, 300f * k));
        SpaceTheme.Img(sheen, SpaceSprites.Glow, SpaceTheme.A(Color.white, locked ? 0.05f : 0.31f));
        var rim = SpaceTheme.Rect("Rim", rt, SpaceTheme.CENTER, Vector2.zero, new Vector2(d, d));
        SpaceTheme.Img(rim, small ? SpaceSprites.RingSmall : SpaceSprites.Ring,
            locked ? SpaceTheme.A(new Color(0.6f, 0.55f, 1f), 0.4f) : SpaceTheme.A(Color.white, 0.47f));
        var outer = SpaceTheme.Rect("OuterRing", rt, SpaceTheme.CENTER, Vector2.zero, new Vector2(420f * k, 420f * k));
        SpaceTheme.Img(outer, small ? SpaceSprites.RingSmall : SpaceSprites.Ring,
            SpaceTheme.A(SpaceTheme.LINE, locked ? 0.1f : 0.24f));
    }

    // ================================================================
    // テクスチャ
    // ================================================================
    private async UniTaskVoid LoadTextures()
    {
        bool large = _quality == Quality.Large;
        if (_set == null)
        {
            var small = await SpacePlanets.Load(Def, false, _faded);
            if (this == null) return;
            if (small != null) Use(small, true);
        }
        if (!large) return;
        var big = await SpacePlanets.Load(Def, true, _faded);
        if (this == null) return;
        if (big != null) Use(big, true);
    }

    private void Use(SpacePlanets.TexSet set, bool fade)
    {
        if (set == null || set == _set || !set.Alive) return;
        SpacePlanets.Retain(set);
        SpacePlanets.Release(_set);
        _set = set;

        _surface.Texture = set.Surface;
        _surface.enabled = true;
        _night.Texture = set.Glow;
        _night.enabled = set.Glow != null && !_locked;
        _clouds.Texture = set.Clouds;
        _clouds.enabled = set.Clouds != null && !_locked;

        var ring = SpacePlanets.RingSprite(Def, _faded);
        if (ring != null && _ringBack != null)
        {
            _ringBack.sprite = ring;
            _ringFront.sprite = ring;
            if (_locked) _ringBack.color = _ringFront.color = new Color(0.24f, 0.25f, 0.36f, 0.85f);
            _ringBack.enabled = _ringFront.enabled = true;
        }

        if (_group.alpha < 1f)
        {
            if (fade && isActiveAndEnabled) StartCoroutine(FadeIn());
            else _group.alpha = 1f;
        }
    }

    private IEnumerator FadeIn()
    {
        float from = _group.alpha;
        yield return SpaceTheme.Tween(0.3f, 0f, k => _group.alpha = Mathf.Lerp(from, 1f, k));
    }

    // ================================================================
    // 自転
    // ================================================================
    private void Update()
    {
        if (!Animate || Def.IsOrb || _set == null) return;
        // 力を失った惑星は、光の脈動も稲妻の明滅もしない
        if (Def.HaloPulse && !_locked && !_faded)
        {
            float k = 0.86f + 0.14f * Mathf.Sin(Time.unscaledTime * 1.3f);
            _halo.color = SpaceTheme.A(Def.Accent, Def.HaloAlpha * k);
        }
        if (Def.Flicker && _night.enabled && !_locked && !_faded) UpdateFlicker();

        // 自転はゆっくりなので、球の頂点の作り直しは 30 回/秒で十分（毎フレームより負荷を減らす）
        _pendingDt += Time.unscaledDeltaTime;
        if (_pendingDt < SPIN_INTERVAL) return;
        _phase = Frac(_phase + _pendingDt / Def.SpinPeriod);
        _cloudPhase = Frac(_cloudPhase + _pendingDt * Def.CloudSpeed / Def.SpinPeriod);
        _pendingDt = 0f;
        ApplySpin();
    }

    /// <summary>
    /// 稲妻: ときどき「ピカッ・ピカッ」と2回光ってから余韻を残して消える（夜側で光る層の不透明度を変える）。
    /// 普段は少し暗めにしておき、光った瞬間だけ明るくする。
    /// </summary>
    private void UpdateFlicker()
    {
        float now = Time.unscaledTime;
        if (_nextFlash <= 0f) _nextFlash = now + Random.Range(0.6f, 2f);
        float a = 0.3f;
        if (_flashT >= 0f)
        {
            _flashT += Time.unscaledDeltaTime;
            float t = _flashT;
            a = t < 0.06f ? 1f : t < 0.12f ? 0.4f : t < 0.20f ? 0.95f : 0.3f + 0.65f * Mathf.Exp(-(t - 0.2f) * 7f);
            if (t > 0.9f) _flashT = -1f;
        }
        else if (now >= _nextFlash)
        {
            _flashT = 0f;
            _nextFlash = now + Random.Range(1.2f, 3.8f);
        }
        if (Mathf.Abs(a - _shownFlash) < 0.02f) return; // 変化が小さいときは描き直さない
        _shownFlash = a;
        _night.color = new Color(1f, 1f, 1f, a);
    }

    private void ApplySpin()
    {
        if (_surface == null) return;
        _surface.Spin = _phase;
        _night.Spin = _phase;
        _clouds.Spin = _cloudPhase;
    }

    private static float Frac(float x) => x - Mathf.Floor(x);

    private void OnDestroy()
    {
        SpacePlanets.Release(_set);
        _set = null;
    }
}
