using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 惑星と来店のしくみのお知らせ（入店したときの公転・流れ星）。新しい惑星の解放は PlanetUnlocked を使う。
/// ShowJourney で「公転 → 流れ星 → 新しい惑星」を1枚ずつ順に出す（前のお知らせを閉じると次が出る）。
/// </summary>
public static partial class SpaceNotice
{
    private const string T_ORBIT_CAPTION = "ORBIT";
    private const string T_ORBIT_TITLE = "ご来店ありがとうございます！";
    private const string T_ORBIT_UNIT = "公転 連続";
    private const string T_ORBIT_UNTIL_A = "この公転は ";
    private const string T_ORBIT_UNTIL_B = " まで";
    private const string T_ORBIT_SAME = "この公転は来店済みです（流れ星は公転ごとに1回）";
    private const string T_ORBIT_RESCUED = "休んだ公転は、軌道修正で続きました";
    private const string T_ORBIT_INSURED = "休んだ公転は、休みの保険で続きました";
    private const string T_ORBIT_BROKEN = "公転が途切れて、半分に戻りました";
    private const string T_ORBIT_INSURANCE = "休みの保険を1つ貯めました";
    private const string T_ORBIT_NEXT_A = "あと";
    private const string T_ORBIT_NEXT_B = "公転で";
    private const string T_ORBIT_FIRST = "これまでの来店回数を引きつぎました";
    private const string T_VISITS_A = "来店回数 ";
    private const string T_VISITS_B = "回";
    private const string T_STAR_CAPTION = "SHOOTING STAR";
    private const string T_STAR_TITLE = "流れ星をつかまえた！";
    private const string T_STAR_GOT = "が当たりました！";
    private const string T_STAR_HINT = "所持品に入りました";
    private const string T_STAR_NOIR = "ひみつの惑星";
    private const string T_STAR_NOIR_HINT = "めったに出会えない惑星です";
    private const string T_TOGETHER = "さんと一緒に来店しました！";
    private const string T_POWER_CHARGED = "にパワーをチャージ！";
    private const string T_POWER_FULL = "のパワーはMAXです";
    private const string T_POWER_MAXED = "パワーMAX！流れ星で7%オフが出やすくなりました";
    private const string T_POWER_KEEP = "中央に置いているあいだ、流れ星で7%オフが出やすくなります";
    private const string T_POWER_LEFT_A = "あと";
    private const string T_POWER_LEFT_B = "回でパワーMAX（流れ星で7%オフが出やすくなる）";

    /// <summary>このお知らせの定型文（NewHome の起動時にまとめてフォントへ登録する）</summary>
    public static string JourneyGlyphText => new StringBuilder()
        .Append(T_ORBIT_CAPTION).Append(T_ORBIT_TITLE).Append(T_ORBIT_UNIT).Append(T_ORBIT_UNTIL_A)
        .Append(T_ORBIT_UNTIL_B).Append(T_ORBIT_SAME).Append(T_ORBIT_RESCUED).Append(T_ORBIT_INSURED)
        .Append(T_ORBIT_BROKEN).Append(T_ORBIT_INSURANCE).Append(T_ORBIT_NEXT_A).Append(T_ORBIT_NEXT_B)
        .Append(T_ORBIT_FIRST).Append(T_VISITS_A).Append(T_VISITS_B).Append(T_STAR_CAPTION).Append(T_STAR_TITLE)
        .Append(T_STAR_GOT).Append(T_STAR_HINT).Append(T_STAR_NOIR).Append(T_STAR_NOIR_HINT).Append(T_TOGETHER)
        .Append(T_POWER_CHARGED).Append(T_POWER_FULL).Append(T_POWER_MAXED).Append(T_POWER_KEEP)
        .Append(T_POWER_LEFT_A).Append(T_POWER_LEFT_B)
        .Append(StoreCalendar.GlyphText).Append(SpaceGachaMeteor.GlyphText).ToString();

    /// <summary>
    /// 入店などの結果を順に出す: 公転（visitCard のとき）→ 流れ星（当たりごと）→ 新しい惑星（解放ごと）。
    /// 全部閉じたら onDone
    /// </summary>
    public static void ShowJourney(JourneyResult r, bool visitCard, Action onDone)
    {
        var steps = new List<Func<GameObject>>();
        if (r != null)
        {
            if (visitCard) steps.Add(() => OrbitVisit(r));
            foreach (var p in r.Prizes)
            {
                var prize = p;
                steps.Add(() => SpaceGachaMeteor.Play(prize)); // 流れ星ガチャの演出 → 当たりのカード
            }
            foreach (var id in r.Unlocked)
            {
                var def = SpacePlanets.Find(id);
                if (def != null) steps.Add(() => PlanetUnlocked(def));
            }
        }
        SpaceNoticeQueue.Run(steps, onDone);
    }

    // ================================================================
    // 公転（入店したとき）
    // ================================================================
    public static GameObject OrbitVisit(JourneyResult r)
    {
        var center = SpacePlanets.Current;
        var card = Overlay("__OrbitVisitModal", out var root, SpaceTheme.A(SpaceTheme.CYAN, 0.45f));
        if (card == null) return null;
        SpaceTheme.SubCanvas(root, true); // 惑星が自転し続けるので、メインのキャンバスと分ける

        float y = 48f;
        var caption = SpaceTheme.Text(card, "Caption", T_ORBIT_CAPTION, 24, SpaceTheme.A(SpaceTheme.CYAN, 0.9f),
            FontStyles.Bold);
        SpaceTheme.Tracking(caption, 18f);
        Stack(caption.rectTransform, ref y, 36f, 0f);
        var title = SpaceTheme.Text(card, "Title", T_ORBIT_TITLE, 40, SpaceTheme.TEXT, FontStyles.Bold);
        Stack(title.rectTransform, ref y, 56f, 8f);

        var goal = JourneyService.NextStreakGoal();
        var stage = SpaceTheme.Rect("Orbit", card);
        Stack(stage, ref y, 340f, 18f, 340f);
        OrbitRing(stage, 330f, r.Streak, goal?.n ?? Mathf.Max(r.Streak, 26), center);

        var big = SpaceTheme.Text(card, "Streak",
            $"{r.Streak}<size=50%> {T_ORBIT_UNIT}</size>", 84, SpaceTheme.TEXT, FontStyles.Bold);
        Stack(big.rectTransform, ref y, 100f, 10f);
        var until = SpaceTheme.Text(card, "Until",
            T_ORBIT_UNTIL_A + StoreCalendar.MonthDayW(StoreCalendar.PeriodEnd(r.Period)) + T_ORBIT_UNTIL_B, 30,
            SpaceTheme.TEXT_SUB, FontStyles.Bold);
        Stack(until.rectTransform, ref y, 44f, 0f);

        var lines = new List<(string text, Color color)>();
        if (r.FirstEver && r.Visits > 1) lines.Add((T_ORBIT_FIRST, SpaceTheme.TEXT_SUB));
        if (!r.NewOrbit) lines.Add((T_ORBIT_SAME, SpaceTheme.TEXT_MUTED));
        if (r.Rescued > 0) lines.Add((T_ORBIT_RESCUED, SpaceTheme.MINT));
        if (r.InsuranceUsed > 0) lines.Add((T_ORBIT_INSURED, SpaceTheme.MINT));
        if (r.Broken) lines.Add((T_ORBIT_BROKEN, SpaceTheme.PINK));
        if (r.InsuranceGained) lines.Add((T_ORBIT_INSURANCE, SpaceTheme.GOLD));
        for (int i = 0; i < lines.Count; i++)
        {
            var t = SpaceTheme.Text(card, "Line" + i, lines[i].text, 28, lines[i].color, FontStyles.Bold);
            Wrap(t);
            Stack(t.rectTransform, ref y, Measure(t, 40f), i == 0 ? 18f : 4f);
        }

        // パワーのチャージ（その公転にもう来ている状態での来店）
        if (!string.IsNullOrEmpty(r.PowerPlanet))
        {
            var pdef = SpacePlanets.Find(r.PowerPlanet);
            bool full = r.Power >= JourneyRules.POWER_MAX;
            string head = (pdef?.Name ?? "") + (full && !r.PowerMaxed ? T_POWER_FULL : T_POWER_CHARGED);
            var headText = SpaceTheme.Text(card, "Power", head, 34, SpaceTheme.GOLD, FontStyles.Bold);
            Stack(headText.rectTransform, ref y, 48f, 22f);
            var gauge = SpaceTheme.Rect("PowerGauge", card);
            Stack(gauge, ref y, 48f, 8f, 420f);
            PowerGauge(gauge, r.Power, SpaceTheme.GOLD);
            string note = r.PowerMaxed ? T_POWER_MAXED
                : full ? T_POWER_KEEP
                : T_POWER_LEFT_A + (JourneyRules.POWER_MAX - r.Power) + T_POWER_LEFT_B;
            var noteText = SpaceTheme.Text(card, "PowerNote", note, 26, full ? SpaceTheme.GOLD : SpaceTheme.TEXT_SUB,
                FontStyles.Bold);
            Wrap(noteText);
            Stack(noteText.rectTransform, ref y, Measure(noteText, 38f), 8f);
        }

        if (goal.HasValue && goal.Value.n > r.Streak)
        {
            var next = SpaceTheme.Text(card, "Next",
                T_ORBIT_NEXT_A + (goal.Value.n - r.Streak) + T_ORBIT_NEXT_B + goal.Value.def.Name, 32,
                SpaceTheme.CYAN, FontStyles.Bold);
            Stack(next.rectTransform, ref y, 46f, 20f);
        }
        var visits = SpaceTheme.Text(card, "Visits", T_VISITS_A + r.Visits + T_VISITS_B, 26, SpaceTheme.TEXT_MUTED,
            FontStyles.Bold);
        Stack(visits.rectTransform, ref y, 38f, 8f);

        Finish(card, root, ref y);
        return root;
    }

    /// <summary>パワーのゲージ（JourneyRules.POWER_MAX 個の丸。たまった数だけ光る）。公転の画面でも使う</summary>
    public static void PowerGauge(RectTransform parent, int power, Color on)
    {
        int max = JourneyRules.POWER_MAX;
        const float SIZE = 34f, GAP = 24f;
        float total = max * SIZE + (max - 1) * GAP;
        for (int i = 0; i < max; i++)
        {
            bool lit = i < power;
            var pip = SpaceTheme.Rect("Pip" + i, parent, SpaceTheme.CENTER,
                new Vector2(-total * 0.5f + SIZE * 0.5f + i * (SIZE + GAP), 0f), new Vector2(SIZE, SIZE));
            if (lit)
            {
                var glow = SpaceTheme.Rect("Glow", pip, SpaceTheme.CENTER, Vector2.zero, new Vector2(SIZE * 2.4f, SIZE * 2.4f));
                SpaceTheme.Img(glow, SpaceSprites.Glow, SpaceTheme.A(on, 0.45f));
            }
            var dot = SpaceTheme.Rect("Dot", pip);
            SpaceTheme.Stretch(dot);
            SpaceTheme.Img(dot, SpaceSprites.Circle, lit ? on : SpaceTheme.A(SpaceTheme.LINE, 0.18f));
        }
    }

    /// <summary>
    /// 公転の輪: 中央の惑星のまわりを、目標（次の節目）の数に分けた弧が囲み、続いた数だけ光る。
    /// 公転パネル（SpaceOrbitPanel）でも使う
    /// </summary>
    public static RectTransform OrbitRing(RectTransform parent, float size, int streak, int segments,
        SpacePlanetDef center, bool animatePlanet = true)
    {
        segments = Mathf.Clamp(segments, 1, 26);
        var rt = SpaceTheme.Rect("Ring", parent, SpaceTheme.CENTER, Vector2.zero, new Vector2(size, size));
        var glow = SpaceTheme.Rect("Glow", rt, SpaceTheme.CENTER, Vector2.zero, new Vector2(size * 1.2f, size * 1.2f));
        SpaceTheme.Img(glow, SpaceSprites.Glow, SpaceTheme.A(center.Accent, 0.22f));

        float gap = segments > 12 ? 0.012f : 0.022f; // 弧と弧のすき間（1周に対する割合）
        int lit = Mathf.Min(streak, segments);
        for (int i = 0; i < segments; i++)
        {
            var seg = SpaceTheme.Rect("Seg" + i, rt);
            SpaceTheme.Stretch(seg);
            seg.localEulerAngles = new Vector3(0f, 0f, -360f * i / segments - gap * 180f);
            var img = SpaceTheme.Img(seg, SpaceSprites.RingSmall,
                i < lit ? SpaceTheme.CYAN : SpaceTheme.A(SpaceTheme.LINE, 0.22f));
            img.type = Image.Type.Filled;
            img.fillMethod = Image.FillMethod.Radial360;
            img.fillOrigin = (int)Image.Origin360.Top;
            img.fillClockwise = true;
            img.fillAmount = 1f / segments - gap;
        }
        float planet = size * 0.62f;
        SpacePlanetView.Create(rt, center, planet, SpacePlanetView.Quality.Thumb, animatePlanet);
        return rt;
    }

    // ================================================================
    // 流れ星
    // ================================================================
    public static GameObject StarPrizeNotice(StarPrize prize)
    {
        var coupon = JourneyService.CouponOf(prize);
        bool noir = prize == StarPrize.Noir;
        var noirDef = SpacePlanets.Find("noir");
        Color accent = noir && noirDef != null ? noirDef.Accent : coupon != null ? coupon.Accent : SpaceTheme.GOLD;
        var card = Overlay("__StarPrizeModal", out var root, SpaceTheme.A(accent, 0.5f));
        if (card == null) return null;
        SpaceTheme.SubCanvas(root, true);

        float y = 48f;
        var caption = SpaceTheme.Text(card, "Caption", T_STAR_CAPTION, 24, SpaceTheme.A(SpaceTheme.GOLD, 0.9f),
            FontStyles.Bold);
        SpaceTheme.Tracking(caption, 18f);
        Stack(caption.rectTransform, ref y, 36f, 0f);
        var title = SpaceTheme.Text(card, "Title", T_STAR_TITLE, 40, SpaceTheme.TEXT, FontStyles.Bold);
        Stack(title.rectTransform, ref y, 56f, 8f);
        // 当たりの格（ガチャの演出の色と同じ。SUPER RARE はピンク、SECRET はすみれ色）
        int tier = SpaceGachaMeteor.TierOf(prize);
        var rarity = SpaceTheme.Text(card, "Rarity", SpaceGachaMeteor.TierName(tier), 30,
            tier == 2 ? SpaceTheme.PINK : SpaceGachaMeteor.TierColor(tier, 0f), FontStyles.Bold);
        SpaceTheme.Tracking(rarity, 20f);
        Stack(rarity.rectTransform, ref y, 42f, 6f);

        var stage = SpaceTheme.Rect("Stage", card);
        Stack(stage, ref y, 320f, 16f, INNER);
        stage.gameObject.AddComponent<RectMask2D>();
        var halo = SpaceTheme.Rect("Glow", stage, SpaceTheme.CENTER, Vector2.zero, new Vector2(360f, 360f));
        SpaceTheme.Img(halo, SpaceSprites.Glow, SpaceTheme.A(accent, 0.35f));
        var prizeHolder = SpaceTheme.Rect("Prize", stage, SpaceTheme.CENTER, Vector2.zero, new Vector2(220f, 220f));
        if (noir && noirDef != null)
            SpacePlanetView.Create(prizeHolder, noirDef, 240f, SpacePlanetView.Quality.Large, true);
        else if (coupon != null)
            SpaceInventoryPanel.RoundedIcon(prizeHolder, coupon.Icon(), SpaceTheme.CENTER, Vector2.zero, 200f, 40f);
        var meteor = stage.gameObject.AddComponent<SpaceMeteorShow>();
        meteor.Prize = prizeHolder;
        meteor.Color = Color.Lerp(accent, Color.white, 0.5f);

        string name = noir ? T_STAR_NOIR + "「" + (noirDef?.Name ?? "") + "」" : coupon?.Name ?? "";
        var strong = SpaceTheme.Text(card, "Strong",
            $"<color=#{ColorUtility.ToHtmlStringRGB(SpaceTheme.GOLD)}>{name}</color>{T_STAR_GOT}", 42,
            SpaceTheme.TEXT, FontStyles.Bold);
        Wrap(strong);
        Stack(strong.rectTransform, ref y, Measure(strong, 60f), 20f);
        var hint = SpaceTheme.Text(card, "Hint", noir ? T_STAR_NOIR_HINT : T_STAR_HINT, 28, SpaceTheme.TEXT_SUB,
            FontStyles.Bold);
        Stack(hint.rectTransform, ref y, 40f, 6f);

        Finish(card, root, ref y);
        return root;
    }

    /// <summary>
    /// 開いているお知らせとして数える（戻るボタンの CloseTop・AnyOpen の対象にする）。
    /// 流れ星ガチャの演出（SpaceGachaMeteor）が使う
    /// </summary>
    internal static void Track(GameObject go)
    {
        if (go != null) _open.Add(go);
    }

    /// <summary>フレンドと一緒に来店したときの短い知らせ</summary>
    public static void TogetherToast(string names)
    {
        if (string.IsNullOrEmpty(names)) return;
        SpaceTheme.Toast(SpaceTheme.NoParse(SpaceTheme.SafeNow(names)) + T_TOGETHER);
    }

    /// <summary>
    /// 入店中に届いた「一緒に来店」などの結果（流れ星・新しい惑星）を出す。toastTogether なら「○○さんと一緒に来店しました！」も
    /// </summary>
    public static void ShowJourneyRewards(JourneyResult r, bool toastTogether)
    {
        if (r == null) return;
        if (toastTogether) TogetherToast(r.Together);
        if (r.HasRewards)
        {
            AssetsDatabase.instance?.PlayLevelUpSE();
            ShowJourney(r, false, null);
        }
    }
}

/// <summary>
/// 流れ星の演出: 光の筋が右上から左下へ流れ、少し遅れて当たり（クーポン・惑星）がぽんと出る。
/// 開いているあいだ、ときどき小さな流れ星が流れ続ける
/// </summary>
public class SpaceMeteorShow : MonoBehaviour
{
    public RectTransform Prize;
    public Color Color = Color.white;

    // 流れる道すじ（右上から左下へ）。光の筋の絵（SpaceSprites.Streak）は右端が明るい頭で、左へ尾が伸びる。
    // 頭を基準点にして進む向きへ回すので、頭が先・尾が後ろになる（背景の流れ星 SpaceBackground と同じやり方）
    private static readonly Vector2 FROM = new Vector2(360f, 170f);
    private static readonly Vector2 TO = -FROM;

    private RectTransform _streak;
    private Image _streakImg;
    private float _t;
    private float _next;

    private void Start()
    {
        var rt = (RectTransform)transform;
        _streak = SpaceTheme.Rect("Meteor", rt, SpaceTheme.CENTER, FROM, new Vector2(420f, 26f), new Vector2(1f, 0.5f));
        var dir = TO - FROM;
        _streak.localEulerAngles = new Vector3(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
        _streakImg = SpaceTheme.Img(_streak, SpaceSprites.Streak, Color);
        _streak.SetAsFirstSibling();
        if (Prize != null) Prize.localScale = Vector3.zero;
        _next = 0f;
    }

    private void Update()
    {
        _t += Time.unscaledDeltaTime;
        // 当たりは 0.55 秒後に弾むように出る
        if (Prize != null)
        {
            float k = Mathf.Clamp01((_t - 0.55f) / 0.45f);
            float s = k <= 0f ? 0f : 1f + 0.18f * Mathf.Sin(k * Mathf.PI) * (1f - k) * 2f;
            Prize.localScale = new Vector3(s * k, s * k, 1f);
        }
        // 光の筋（最初は大きく、そのあとは3秒ごとに小さく）
        float since = _t - _next;
        float dur = 0.7f;
        if (since > dur)
        {
            _next = _t + (_next <= 0f ? 1.2f : 3f);
            since = -1f;
        }
        if (since < 0f)
        {
            _streakImg.color = Color.clear;
            return;
        }
        float p = since / dur;
        bool firstOne = _t < 1f;
        float scale = firstOne ? 1f : 0.55f;
        _streak.localScale = new Vector3(scale, scale, 1f);
        float reach = firstOne ? 1f : 0.9f;
        _streak.anchoredPosition = Vector2.Lerp(FROM * reach, TO * reach, p);
        _streakImg.color = SpaceTheme.A(Color, Mathf.Sin(p * Mathf.PI) * 0.95f);
    }
}

/// <summary>お知らせを1枚ずつ順に出す（前のお知らせが閉じたら次。全部終わったら onDone）</summary>
public class SpaceNoticeQueue : MonoBehaviour
{
    private readonly Queue<Func<GameObject>> _steps = new Queue<Func<GameObject>>();
    private GameObject _current;
    private Action _onDone;
    private bool _done;
    private static int s_running;

    /// <summary>順番待ちのお知らせが残っている</summary>
    public static bool Running => s_running > 0;

    public static void Run(List<Func<GameObject>> steps, Action onDone)
    {
        var go = new GameObject("__NoticeQueue");
        var q = go.AddComponent<SpaceNoticeQueue>();
        foreach (var s in steps) q._steps.Enqueue(s);
        q._onDone = onDone;
        s_running++;
    }

    private void Update()
    {
        // 前のお知らせ（この順番待ちのもの・退店のお知らせなど）が閉じてから次を出す
        if (_current != null || SpaceNotice.AnyOpen) return;
        while (_steps.Count > 0)
        {
            var next = _steps.Dequeue();
            try
            {
                _current = next();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NoticeQueue] お知らせを出せませんでした: {ex.Message}");
                _current = null;
            }
            if (_current != null) return;
        }
        End();
    }

    private void End()
    {
        if (_done) return;
        _done = true;
        s_running = Mathf.Max(0, s_running - 1);
        var cb = _onDone;
        _onDone = null;
        cb?.Invoke();
        Destroy(gameObject);
    }

    private void OnDestroy()
    {
        if (_done) return;
        _done = true;
        s_running = Mathf.Max(0, s_running - 1);
        _onDone?.Invoke();
    }
}
