using System;
using System.Collections.Generic;
using System.Text;
using Cysharp.Threading.Tasks;
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
    private const string T_MEEPLE_GOT = "コマをもらいました"; // 入店した日に、中央の惑星の色のコマ（2026-11-10）
    private const string T_MEEPLE_NONE = " は力を失っているので、コマはもらえませんでした";
    // 退店して、コマをもらった惑星が力を失ったとき（2026-11-10）
    private const string T_FADED_CAPTION = "PLANET";
    private const string T_FADED_TITLE = "惑星が力を使いました";
    private const string T_FADED_BODY_B = " の色が淡くなりました";
    private const string T_FADED_NOTE = "この惑星で入店しても、コマはもらえなくなりました\nほかの惑星をホームに置くと、また入店のときにもらえます";
    // 惑星のパワー（チャージ・MAX の知らせ）は 2026-11-10 にやめた

    /// <summary>このお知らせの定型文（NewHome の起動時にまとめてフォントへ登録する）</summary>
    public static string JourneyGlyphText => new StringBuilder()
        .Append(T_ORBIT_CAPTION).Append(T_ORBIT_TITLE).Append(T_ORBIT_UNIT).Append(T_ORBIT_UNTIL_A)
        .Append(T_ORBIT_UNTIL_B).Append(T_ORBIT_SAME).Append(T_ORBIT_RESCUED).Append(T_ORBIT_INSURED)
        .Append(T_ORBIT_BROKEN).Append(T_ORBIT_INSURANCE).Append(T_ORBIT_NEXT_A).Append(T_ORBIT_NEXT_B)
        .Append(T_ORBIT_FIRST).Append(T_VISITS_A).Append(T_VISITS_B).Append(T_STAR_CAPTION).Append(T_STAR_TITLE)
        .Append(T_STAR_GOT).Append(T_STAR_HINT).Append(T_STAR_NOIR).Append(T_STAR_NOIR_HINT).Append(T_TOGETHER)
        .Append(T_MEEPLE_GOT).Append(SpaceMeeples.GlyphText).Append(T_MEEPLE_NONE).Append(T_FADED_CAPTION)
        .Append(T_FADED_TITLE).Append(T_FADED_BODY_B).Append(T_FADED_NOTE).Append(T_WORLD_CAPTION)
        .Append(T_REVIVED_TITLE).Append(T_REVIVED_NOTE).Append(T_PICK_CAPTION).Append(T_PICK_TITLE)
        .Append(T_PICK_SUB_B).Append(T_PICK_SUB).Append(T_PICK_GOT).Append(T_PICK_FAILED).Append(T_PICK_NEXT)
        .Append(T_FRAGMENT_GOT).Append(T_FRAGMENT_LEFT_A).Append(T_FRAGMENT_LEFT_B).Append(T_FRAGMENT_DONE)
        .Append(T_FRAGMENT_DONE_NOTE).Append(SpaceWorld.FRAGMENT_NAME)
        .Append(StoreCalendar.GlyphText).Append(SpaceGachaMeteor.GlyphText).ToString();

    /// <summary>
    /// 入店などの結果を順に出す: 公転（visitCard のとき）／惑星が力を使った（退店のとき）→ 流れ星（当たりごと）
    /// → 新しい惑星（解放ごと）。全部閉じたら onDone
    /// </summary>
    public static void ShowJourney(JourneyResult r, bool visitCard, Action onDone)
    {
        var steps = new List<Func<GameObject>>();
        if (r != null)
        {
            if (visitCard) steps.Add(() => OrbitVisit(r));
            // 複数の色の惑星で入店した: 入店のカードのあとに、もらうコマを1個選ぶ（選ぶまで閉じない）
            if (r.MeeplePick != PlanetColor.None)
            {
                var pick = r.MeeplePick;
                steps.Add(() => JourneyService.PendingPick != PlanetColor.None ? MeeplePick(pick) : null);
            }
            var faded = FadedAtCheckOut(r);
            if (faded != null) steps.Add(() => PlanetFaded(faded));
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

        // 入店でもらったコマ（その営業日の最初の入店のとき。中央の惑星の色ごとに1個。2026-11-10）
        if (r.Meeples.Count > 0) MeepleStrip(card, r.Meeples, ref y);

        var lines = new List<(string text, Color color)>();
        if (r.FirstEver && r.Visits > 1) lines.Add((T_ORBIT_FIRST, SpaceTheme.TEXT_SUB));
        // 中央の惑星が力を失っていて、コマをもらえなかった（2026-11-10）
        if (r.CenterExhausted) lines.Add((center.Name + T_MEEPLE_NONE, SpaceTheme.TEXT_SUB));
        // 複数の色の惑星: このあとの画面で、もらうコマを1個選ぶ
        if (r.MeeplePick != PlanetColor.None) lines.Add((T_PICK_NEXT, SpaceTheme.GOLD));
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

    // ================================================================
    // 惑星が力を使った（退店したとき。2026-11-10）
    // ================================================================
    /// <summary>
    /// 退店して、コマをもらった惑星が力を失ったお知らせ。いつもの色の惑星が、少しして白く淡い色に移り変わる
    /// （同じ惑星を2つ重ね、淡い方を上に重ねてゆっくり出す。自転の向き・速さは同じなので形はずれない）
    /// </summary>
    public static GameObject PlanetFaded(SpacePlanetDef def)
    {
        var card = Overlay("__PlanetFadedModal", out var root, SpaceTheme.A(SpacePlanets.FadedColor(def.Accent), 0.5f));
        if (card == null) return null;
        SpaceTheme.SubCanvas(root, true); // 惑星が自転し続けるので、メインのキャンバスと分ける

        float y = 48f;
        var caption = SpaceTheme.Text(card, "Caption", T_FADED_CAPTION, 24, SpaceTheme.A(SpaceTheme.TEXT_SUB, 0.9f),
            FontStyles.Bold);
        SpaceTheme.Tracking(caption, 18f);
        Stack(caption.rectTransform, ref y, 36f, 0f);
        var title = SpaceTheme.Text(card, "Title", T_FADED_TITLE, 40, SpaceTheme.TEXT, FontStyles.Bold);
        Stack(title.rectTransform, ref y, 56f, 8f);

        const float FRAME = 360f;
        var stage = SpaceTheme.Rect("Planet", card);
        Stack(stage, ref y, FRAME, 16f, FRAME);
        var vivid = SpaceTheme.Rect("Vivid", stage);
        SpaceTheme.Stretch(vivid);
        var vividGroup = vivid.gameObject.AddComponent<CanvasGroup>();
        SpacePlanetView.Create(vivid, def, FRAME, SpacePlanetView.Quality.Large, true);
        var pale = SpaceTheme.Rect("Pale", stage);
        SpaceTheme.Stretch(pale);
        var paleGroup = pale.gameObject.AddComponent<CanvasGroup>();
        paleGroup.alpha = 0f;
        SpacePlanetView.Create(pale, def, FRAME, SpacePlanetView.Quality.Large, true, false, true);
        var swap = root.AddComponent<SpaceCrossFade>();
        swap.From = vividGroup;
        swap.To = paleGroup;

        var name = SpaceTheme.Text(card, "Name", def.Name + T_FADED_BODY_B, 36, SpaceTheme.TEXT, FontStyles.Bold);
        name.enableAutoSizing = true;
        name.fontSizeMin = 26f;
        name.fontSizeMax = 36f;
        Stack(name.rectTransform, ref y, 52f, 14f);
        var note = SpaceTheme.Text(card, "Note", T_FADED_NOTE, 26, SpaceTheme.TEXT_SUB, FontStyles.Bold);
        Wrap(note);
        Stack(note.rectTransform, ref y, Measure(note, 40f), 10f);

        Finish(card, root, ref y);
        return root;
    }

    // ================================================================
    // もらうコマを選ぶ（複数の色の惑星で入店したとき。2026-11-10）
    // ================================================================
    private const string T_PICK_CAPTION = "CHOOSE A PIECE";
    private const string T_PICK_TITLE = "もらうコマを1個選んでください";
    private const string T_PICK_SUB_B = " の色から、1個だけ選べます";
    private const string T_PICK_SUB = "惑星の色から、1個だけ選べます";
    private const string T_PICK_GOT = " をもらいました";
    private const string T_PICK_FAILED = "受け取れませんでした。通信の状態を確かめて、もう一度押してください";
    private const string T_PICK_NEXT = "このあと、もらうコマの色を1つ選べます";

    /// <summary>
    /// 複数の色の惑星で入店したとき: その色の中から、もらうコマを1個だけ選ぶ（選ぶまで閉じない）。
    /// 押した色を JourneyService.ClaimMeepleAsync で受け取ってから閉じる
    /// </summary>
    public static GameObject MeeplePick(PlanetColor colors)
    {
        colors &= PlanetColor.All;
        if (colors == PlanetColor.None) return null;
        var card = Overlay("__MeeplePickModal", out var root, SpaceTheme.A(SpaceTheme.GOLD, 0.5f), null, false);
        if (card == null) return null;

        float y = 48f;
        var caption = SpaceTheme.Text(card, "Caption", T_PICK_CAPTION, 24, SpaceTheme.A(SpaceTheme.CYAN, 0.9f),
            FontStyles.Bold);
        SpaceTheme.Tracking(caption, 18f);
        Stack(caption.rectTransform, ref y, 36f, 0f);
        var title = SpaceTheme.Text(card, "Title", T_PICK_TITLE, 40, SpaceTheme.TEXT, FontStyles.Bold);
        title.enableAutoSizing = true;
        title.fontSizeMin = 28f;
        title.fontSizeMax = 40f;
        Stack(title.rectTransform, ref y, 56f, 8f);
        var planet = JourneyService.PendingPickPlanet;
        var sub = SpaceTheme.Text(card, "Sub", planet != null ? planet.Name + T_PICK_SUB_B : T_PICK_SUB, 28,
            SpaceTheme.TEXT_SUB, FontStyles.Bold);
        Wrap(sub);
        Stack(sub.rectTransform, ref y, Measure(sub, 40f), 6f);

        var list = new List<PlanetColor>();
        foreach (var c in PlanetColorOrder.All)
            if ((colors & c) != 0) list.Add(c);
        const float CW = 230f, CH = 250f, GAP = 22f;
        var row = SpaceTheme.Rect("Choices", card);
        Stack(row, ref y, CH, 28f);
        float total = list.Count * CW + (list.Count - 1) * GAP;
        var top = new Vector2(0.5f, 1f);
        var groups = new List<CanvasGroup>();
        bool busy = false;

        async UniTaskVoid Claim(PlanetColor color)
        {
            if (busy) return;
            busy = true;
            foreach (var g in groups) g.alpha = 0.5f;
            bool ok = await JourneyService.ClaimMeepleAsync(color);
            if (root == null) return;
            if (ok)
            {
                UnityEngine.Object.Destroy(root);
                SpaceTheme.Toast(SpaceMeeples.Of(color)?.Name + T_PICK_GOT);
                AssetsDatabase.instance?.PlayLevelUpSE();
                return;
            }
            busy = false;
            foreach (var g in groups) g.alpha = 1f;
            SpaceTheme.Toast(T_PICK_FAILED);
        }

        for (int i = 0; i < list.Count; i++)
        {
            var color = list[i];
            var m = SpaceMeeples.Of(color);
            if (m == null) continue;
            float x = -total * 0.5f + CW * 0.5f + i * (CW + GAP);
            var rt = SpaceTheme.Rect("Pick_" + m.Field, row, top, new Vector2(x, -CH * 0.5f), new Vector2(CW, CH));
            groups.Add(rt.gameObject.AddComponent<CanvasGroup>());
            var glow = SpaceTheme.Rect("Glow", rt, SpaceTheme.CENTER, Vector2.zero, new Vector2(CW + 100f, CH + 100f));
            SpaceTheme.Img(glow, SpaceSprites.Glow, SpaceTheme.A(m.Tint, 0.3f));
            var faceRt = SpaceTheme.Rect("Face", rt);
            SpaceTheme.Stretch(faceRt);
            var face = SpaceTheme.Rounded(faceRt, new Color(0.07f, 0.09f, 0.24f, 0.9f), 36f, true);
            SpaceTheme.Clickable(rt.gameObject, face, () => Claim(color).Forget());
            var stroke = SpaceTheme.Rect("Stroke", rt);
            SpaceTheme.Stretch(stroke);
            SpaceTheme.RoundedStroke(stroke, SpaceTheme.A(m.Tint, 0.85f), 36f);
            var icon = SpaceTheme.Rect("Icon", rt, top, new Vector2(0f, -82f), new Vector2(124f, 124f));
            var img = SpaceTheme.Img(icon, m.Icon, Color.white);
            img.preserveAspect = true;
            img.enabled = m.Icon != null;
            SpaceTheme.Text(rt, "Name", m.Name, 34, SpaceTheme.TEXT, new Vector2(0.5f, 0f), new Vector2(0f, 46f),
                new Vector2(CW - 16f, 48f), FontStyles.Bold);
        }

        y += 56f;
        card.sizeDelta = new Vector2(WIDTH, y);
        root.AddComponent<SpacePopIn>().Target = card;
        return root;
    }

    // ================================================================
    // INTALES WORLD の効果（2026-11-10）
    // ================================================================
    private const string T_WORLD_CAPTION = "INTALES WORLD";
    private const string T_REVIVED_TITLE = "惑星が力を取り戻しました";
    private const string T_REVIVED_NOTE = "また入店のときに、この惑星の色のコマがもらえます";

    /// <summary>惑星の神殿: 力を戻した惑星（最大3つ）が、白く淡い色からいつもの色に戻っていく</summary>
    public static GameObject PlanetsRevived(List<SpacePlanetDef> planets)
    {
        if (planets == null || planets.Count == 0) return null;
        var card = Overlay("__PlanetsRevivedModal", out var root, SpaceTheme.A(planets[0].Accent, 0.5f));
        if (card == null) return null;
        SpaceTheme.SubCanvas(root, true); // 惑星が自転し続けるので、メインのキャンバスと分ける

        float y = 48f;
        var caption = SpaceTheme.Text(card, "Caption", T_WORLD_CAPTION, 24, SpaceTheme.A(SpaceTheme.CYAN, 0.9f),
            FontStyles.Bold);
        SpaceTheme.Tracking(caption, 18f);
        Stack(caption.rectTransform, ref y, 36f, 0f);
        var title = SpaceTheme.Text(card, "Title", T_REVIVED_TITLE, 40, SpaceTheme.TEXT, FontStyles.Bold);
        title.enableAutoSizing = true;
        title.fontSizeMin = 28f;
        title.fontSizeMax = 40f;
        Stack(title.rectTransform, ref y, 56f, 8f);

        const float FRAME = 230f, GAP = 14f;
        int n = Mathf.Min(planets.Count, 3);
        var stage = SpaceTheme.Rect("Planets", card);
        Stack(stage, ref y, FRAME + 52f, 16f);
        float total = n * FRAME + (n - 1) * GAP;
        var top = new Vector2(0.5f, 1f);
        for (int i = 0; i < n; i++)
        {
            var def = planets[i];
            float x = -total * 0.5f + FRAME * 0.5f + i * (FRAME + GAP);
            var slot = SpaceTheme.Rect("Slot" + i, stage, top, new Vector2(x, -FRAME * 0.5f), new Vector2(FRAME, FRAME));
            var pale = SpaceTheme.Rect("Pale", slot);
            SpaceTheme.Stretch(pale);
            var paleGroup = pale.gameObject.AddComponent<CanvasGroup>();
            SpacePlanetView.Create(pale, def, FRAME, SpacePlanetView.Quality.Thumb, true, false, true);
            var vivid = SpaceTheme.Rect("Vivid", slot);
            SpaceTheme.Stretch(vivid);
            var vividGroup = vivid.gameObject.AddComponent<CanvasGroup>();
            vividGroup.alpha = 0f;
            SpacePlanetView.Create(vivid, def, FRAME, SpacePlanetView.Quality.Thumb, true);
            var swap = slot.gameObject.AddComponent<SpaceCrossFade>();
            swap.From = paleGroup;
            swap.To = vividGroup;
            swap.Delay = 0.8f + i * 0.3f; // 左から順に色が戻る
            var name = SpaceTheme.Text(stage, "Name" + i, def.Name, 28, SpaceTheme.TEXT, top,
                new Vector2(x, -(FRAME + 26f)), new Vector2(FRAME, 40f), FontStyles.Bold);
            name.enableAutoSizing = true;
            name.fontSizeMin = 20f;
            name.fontSizeMax = 28f;
        }

        var note = SpaceTheme.Text(card, "Note", T_REVIVED_NOTE, 26, SpaceTheme.TEXT_SUB, FontStyles.Bold);
        Wrap(note);
        Stack(note.rectTransform, ref y, Measure(note, 40f), 14f);
        Finish(card, root, ref y);
        return root;
    }

    private const string T_FRAGMENT_GOT = " を1枚もらいました";
    private const string T_FRAGMENT_LEFT_A = "あと ";
    private const string T_FRAGMENT_LEFT_B = "枚で 5%OFFチケット になります";
    private const string T_FRAGMENT_DONE = "カケラがそろって、5%OFFチケット になりました！";
    private const string T_FRAGMENT_DONE_NOTE = "所持品に入りました";

    /// <summary>
    /// 商人の港: 5%OFFチケットのカケラを1枚もらった（2026-11-10）。カケラの枠を2つ並べて、集まった数だけ埋める。
    /// そろったとき（completed）は、5%OFFチケットができたことを知らせる
    /// </summary>
    public static GameObject FragmentGot(string place, int have, bool completed)
    {
        var accent = SpaceTheme.Hex(0x6FA8FF);
        var card = Overlay("__FragmentGotModal", out var root, SpaceTheme.A(accent, 0.55f));
        if (card == null) return null;
        float y = 48f;
        var caption = SpaceTheme.Text(card, "Caption", T_WORLD_CAPTION, 24, SpaceTheme.A(SpaceTheme.CYAN, 0.9f),
            FontStyles.Bold);
        SpaceTheme.Tracking(caption, 18f);
        Stack(caption.rectTransform, ref y, 36f, 0f);
        var title = SpaceTheme.Text(card, "Title", place, 40, SpaceTheme.TEXT, FontStyles.Bold);
        Stack(title.rectTransform, ref y, 56f, 8f);

        int need = SpaceWorld.FRAGMENTS_PER_COUPON;
        int filled = completed ? need : Mathf.Clamp(have, 0, need);
        const float SLOT = 200f, GAP = 30f;
        var stage = SpaceTheme.Rect("Fragments", card);
        Stack(stage, ref y, SLOT, 20f);
        float total = need * SLOT + (need - 1) * GAP;
        var top = new Vector2(0.5f, 1f);
        for (int i = 0; i < need; i++)
        {
            float x = -total * 0.5f + SLOT * 0.5f + i * (SLOT + GAP);
            var slot = SpaceTheme.Rect("Slot" + i, stage, top, new Vector2(x, -SLOT * 0.5f), new Vector2(SLOT, SLOT));
            bool on = i < filled;
            if (on)
            {
                var glow = SpaceTheme.Rect("Glow", slot, SpaceTheme.CENTER, Vector2.zero, new Vector2(SLOT * 1.5f, SLOT * 1.5f));
                SpaceTheme.Img(glow, SpaceSprites.Glow, SpaceTheme.A(accent, 0.35f));
            }
            else
            {
                var ring = SpaceTheme.Rect("Empty", slot);
                SpaceTheme.Stretch(ring);
                SpaceTheme.RoundedStroke(ring, SpaceTheme.A(SpaceTheme.TEXT_SUB, 0.45f), 36f);
            }
            // 1枚目は左半分（5）、2枚目は右半分（%）。そろうと元のチケットの形になる
            var sprite = i == 0 ? SpaceWorld.FragmentIcon : (SpaceWorld.FragmentIconRight ?? SpaceWorld.FragmentIcon);
            var icon = SpaceTheme.Rect("Icon", slot, SpaceTheme.CENTER, Vector2.zero, new Vector2(SLOT * 0.86f, SLOT * 0.86f));
            var img = SpaceTheme.Img(icon, sprite, on ? Color.white : new Color(1f, 1f, 1f, 0.16f));
            img.preserveAspect = true;
            img.enabled = sprite != null;
        }

        var got = SpaceTheme.Text(card, "Got",
            $"<color=#{ColorUtility.ToHtmlStringRGB(SpaceTheme.GOLD)}>{SpaceWorld.FRAGMENT_NAME}</color>{T_FRAGMENT_GOT}", 32,
            SpaceTheme.TEXT, FontStyles.Bold);
        Wrap(got);
        Stack(got.rectTransform, ref y, Measure(got, 48f), 22f);
        string noteText = completed ? T_FRAGMENT_DONE : T_FRAGMENT_LEFT_A + (need - filled) + T_FRAGMENT_LEFT_B;
        var note = SpaceTheme.Text(card, "Note", noteText, 28, completed ? SpaceTheme.GOLD : SpaceTheme.TEXT_SUB,
            FontStyles.Bold);
        Wrap(note);
        Stack(note.rectTransform, ref y, Measure(note, 40f), 6f);
        if (completed)
        {
            // できた5%OFFチケット
            var coupon = SpaceCoupons.ByField("five_coupon");
            var done = SpaceTheme.Rect("Coupon", card);
            Stack(done, ref y, 190f, 14f);
            var halo = SpaceTheme.Rect("Glow", done, SpaceTheme.CENTER, Vector2.zero, new Vector2(300f, 300f));
            SpaceTheme.Img(halo, SpaceSprites.Glow, SpaceTheme.A(SpaceTheme.GOLD, 0.3f));
            if (coupon != null) SpaceInventoryPanel.RoundedIcon(done, coupon.Icon(), SpaceTheme.CENTER, Vector2.zero, 180f, 38f);
            var sub = SpaceTheme.Text(card, "DoneNote", T_FRAGMENT_DONE_NOTE, 24, SpaceTheme.TEXT_MUTED, FontStyles.Bold);
            Stack(sub.rectTransform, ref y, 34f, 8f);
        }
        Finish(card, root, ref y);
        return root;
    }

    /// <summary>コマの工房: 各色のコマを1個ずつもらった（place は置き場の名前）</summary>
    public static GameObject MeeplesGot(string place)
    {
        var card = Overlay("__MeeplesGotModal", out var root, SpaceTheme.A(SpaceTheme.GOLD, 0.45f));
        if (card == null) return null;
        float y = 48f;
        var caption = SpaceTheme.Text(card, "Caption", T_WORLD_CAPTION, 24, SpaceTheme.A(SpaceTheme.CYAN, 0.9f),
            FontStyles.Bold);
        SpaceTheme.Tracking(caption, 18f);
        Stack(caption.rectTransform, ref y, 36f, 0f);
        var title = SpaceTheme.Text(card, "Title", place, 40, SpaceTheme.TEXT, FontStyles.Bold);
        Stack(title.rectTransform, ref y, 56f, 8f);
        MeepleStrip(card, new List<PlanetColor>(PlanetColorOrder.All), ref y);
        Finish(card, root, ref y);
        return root;
    }

    /// <summary>入店でもらったコマ: 金色の枠の中に「コマをもらいました」と、色ごとのコマの絵と「赤コマ +1」を横に並べる</summary>
    private static void MeepleStrip(RectTransform card, List<PlanetColor> colors, ref float y)
    {
        const float CELL = 220f, ICON = 96f, TOP_PAD = 62f;
        var box = SpaceTheme.Rect("Meeples", card);
        Stack(box, ref y, TOP_PAD + ICON + 64f, 24f);
        SpaceTheme.Rounded(box, SpaceTheme.A(SpaceTheme.GOLD, 0.07f), 32f);
        var edge = SpaceTheme.Rect("Edge", box);
        SpaceTheme.Stretch(edge);
        SpaceTheme.RoundedStroke(edge, SpaceTheme.A(SpaceTheme.GOLD, 0.35f), 32f);

        var top = new Vector2(0.5f, 1f);
        SpaceTheme.Text(box, "Label", T_MEEPLE_GOT, 28, SpaceTheme.GOLD, top, new Vector2(0f, -36f),
            new Vector2(INNER - 40f, 40f), FontStyles.Bold);
        for (int i = 0; i < colors.Count; i++)
        {
            var def = SpaceMeeples.Of(colors[i]);
            if (def == null) continue;
            float x = (i - (colors.Count - 1) * 0.5f) * CELL;
            var icon = SpaceTheme.Rect("Icon" + i, box, top, new Vector2(x, -(TOP_PAD + ICON * 0.5f)),
                new Vector2(ICON, ICON));
            var img = SpaceTheme.Img(icon, def.Icon, Color.white);
            img.preserveAspect = true;
            img.enabled = def.Icon != null;
            var name = SpaceTheme.Text(box, "Name" + i, def.Name + " +1", 26, SpaceTheme.TEXT, top,
                new Vector2(x, -(TOP_PAD + ICON + 26f)), new Vector2(CELL - 10f, 36f), FontStyles.Bold);
            name.enableAutoSizing = true;
            name.fontSizeMin = 18f;
            name.fontSizeMax = 26f;
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
        SpacePlanetView.Create(rt, center, planet, SpacePlanetView.Quality.Thumb, animatePlanet,
            false, SpacePlanets.IsExhausted(center)); // 力を失った惑星は白く淡い色で（2026-11-10）
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
        if (r.HasRewards || FadedAtCheckOut(r) != null)
        {
            if (r.HasRewards) AssetsDatabase.instance?.PlayLevelUpSE();
            ShowJourney(r, false, null);
        }
    }

    /// <summary>
    /// 退店で力を失った惑星（お知らせを出す）。退店しないまま次の営業日に入店して移したときは出さない
    /// （その惑星は営業日が変わった時点で、もう淡い色で表示している）
    /// </summary>
    private static SpacePlanetDef FadedAtCheckOut(JourneyResult r) =>
        r != null && r.CheckOut && !string.IsNullOrEmpty(r.Exhausted) ? SpacePlanets.Find(r.Exhausted) : null;
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

/// <summary>選ぶまで閉じられないお知らせの印（暗幕を押しても・戻る操作でも閉じない。SpaceNotice.Overlay の tapToClose）</summary>
public class SpaceNoticeSticky : MonoBehaviour
{
}

/// <summary>
/// 2つの表示を入れ替える（From を消しながら To を出す）。少し待ってから、ゆっくり。
/// 惑星が力を使ったお知らせで、いつもの色 → 白く淡い色に移り変わるのに使う（2026-11-10）
/// </summary>
public class SpaceCrossFade : MonoBehaviour
{
    public CanvasGroup From, To;
    public float Delay = 0.9f, Duration = 1.6f;
    private float _t;

    private void Update()
    {
        _t += Time.unscaledDeltaTime;
        float k = Mathf.Clamp01((_t - Delay) / Duration);
        k = k * k * (3f - 2f * k); // なめらかに始まり、なめらかに終わる
        // 下（From）は出しきるまで不透明のまま（両方を半透明にすると途中で透けて見える）
        if (To != null) To.alpha = k;
        if (From != null) From.alpha = k >= 1f ? 0f : 1f;
        if (k >= 1f) enabled = false;
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
