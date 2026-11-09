using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>流れ星の当たり（いまあるクーポン4種と、ひみつの惑星ノワール）</summary>
/// <summary>流れ星の当たり。ODDS の列と同じ並び（新しい当たりは必ず末尾に足す）</summary>
public enum StarPrize { Five, Drink, Coffee, Seven, Noir, FiveHour, FreeTime }

/// <summary>入店・退店・フレンドと一緒に来店したときの結果（お知らせに出す内容）</summary>
public sealed class JourneyResult
{
    public bool FirstEver;        // 新しい入店QRで初めて入店した（来店回数を端末の記録から引きついだ）
    public bool NewDay;           // その営業日の最初の入店（来店回数が増えた）
    public bool NewOrbit;         // その公転の最初の来店
    public int Streak, PrevStreak;
    public int Rescued;           // 軌道修正で続いた休みの数
    public int InsuranceUsed;     // 休みの保険で続いた数
    public bool Broken;           // 途切れて半分に戻った
    public bool InsuranceGained;  // 休みの保険を貯めた（オルビス）
    public int Visits;
    public int Period;
    public string Together;       // 一緒に来店したフレンドの名前（フレンドのQRを読んだとき）
    public string PowerPlanet;    // パワーをためた惑星（その公転にもう来ている状態での来店）
    public int Power;             // ためたあとのパワー（0〜JourneyRules.POWER_MAX）
    public bool PowerMaxed;       // この来店でパワーが MAX になった
    public readonly List<StarPrize> Prizes = new List<StarPrize>();
    public readonly List<string> Unlocked = new List<string>(); // 解放した惑星のID

    public bool HasRewards => Prizes.Count > 0 || Unlocked.Count > 0;
}

/// <summary>
/// 惑星と来店のしくみの判定（公転・惑星の解放・流れ星の抽選）。Unity・Firebase を使わない計算だけをここに置く
/// （JourneyService がトランザクションの中から呼ぶ。エディタの外の試験プログラムでも動かせる）。
/// </summary>
public static class JourneyRules
{
    // 節目（公転を続けた数）・来店回数・曜日・季節で解放する惑星
    public static readonly (int n, string id)[] STREAK_PLANETS =
        { (2, "mistia"), (4, "tempesta"), (6, "celesta"), (13, "neonia"), (26, "galaxia") };
    public static readonly (int n, string id)[] VISIT_PLANETS =
        { (2, "aquaris"), (5, "limona"), (10, "mocha"), (25, "voltia"), (50, "noctis") };
    public static readonly Dictionary<DayOfWeek, string> WEEKDAY_PLANETS = new Dictionary<DayOfWeek, string>
    {
        { DayOfWeek.Thursday, "verdia" }, { DayOfWeek.Friday, "crysta" },
        { DayOfWeek.Saturday, "orbis" }, { DayOfWeek.Sunday, "soleil" },
    };
    public static readonly string[] SEASON_PLANETS = { "pastelia", "coralia", "rubra", "glacia" };

    /// <summary>軌道修正を数える範囲（公転の数）。「3か月（6公転）に1回まで」</summary>
    public const int RESCUE_WINDOW = 6;
    /// <summary>秋のルブラで流れ星をもう1回引ける滞在時間（分）</summary>
    public const int RUBRA_STAY_MINUTES = 4 * 60;

    /// <summary>
    /// パワーの MAX（2026-11-06 ユーザー指定「6回チャージで MAX」）。その公転にもう来ている状態でまた来店すると
    /// （1日1回）、中央の惑星に1たまる。MAX の惑星を中央に置いていると、流れ星で7%オフが POWER_SEVEN 倍出やすい
    /// </summary>
    public const int POWER_MAX = 6;
    public const double POWER_SEVEN = 5.0;

    // 「もう1回」の種類（1公転に1回ずつ）
    public const string EXTRA_TOGETHER = "together"; // オパーリア: フレンドと一緒に来店
    public const string EXTRA_FIRST = "first";       // パステリア: 春に、初めて来店する人と一緒に来店
    public const string EXTRA_GIFT = "gift";         // フレンドの惑星の能力でもらった
    public const string EXTRA_RUBRA = "rubra";       // ルブラ: 秋に4時間以上の滞在

    /// <summary>
    /// 流れ星の当たりの重み（%。合計100）。公転の数 1〜3 / 4〜12 / 13〜 の3段階（企画書の例そのまま）。
    /// 並び: 5% / ドリンク / コーヒー / 7% / ノワール / 5時間無料 / フリータイム。
    /// 5時間無料・フリータイムは 2026-11-07 追加（ユーザー指定「提案の3分の1の確率で」: 5時間無料 0.3/0.6/1.0% と
    /// フリータイム 0.1/0.3/0.5% の3分の1）。足した分は5%から引いて合計を100のままにした
    /// </summary>
    public static readonly double[][] ODDS =
    {
        new[] { 70.0 - 0.1 - 0.1 / 3, 20.0, 7.5, 2.0, 0.5, 0.1, 0.1 / 3 },
        new[] { 55.0 - 0.2 - 0.1, 27.0, 13.0, 4.0, 1.0, 0.2, 0.1 },
        new[] { 40.0 - 1.0 / 3 - 0.5 / 3, 33.0, 18.0, 7.0, 2.0, 1.0 / 3, 0.5 / 3 },
    };


    public sealed class VisitContext
    {
        public string DayKey;
        public int Period;
        public DayOfWeek Weekday;
        public StoreCalendar.Season Season;
        public bool FullMoon;
        public bool Recruit;
        public string Center;
        public long InAt;
        public int SeedDays;
    }

    /// <summary>入店1回ぶんを記録に反映する（トランザクションの中で呼ぶ。Unity の API は使わない）</summary>
    public static JourneyData ApplyVisit(JourneyData j, VisitContext c, JourneyResult r, HashSet<string> owned,
        System.Random rng)
    {
        bool first = j == null;
        if (first) j = new JourneyData();
        r.FirstEver = first;

        // ---- 来店回数（1営業日に1回） ----
        r.NewDay = !j.VisitedOn(c.DayKey);
        if (first) j.Visits = Math.Max(1, c.SeedDays);
        else if (r.NewDay) j.Visits++;
        j.Day = c.DayKey;
        j.InAt = c.InAt;
        if (!j.Days.Contains((int)c.Weekday)) j.Days.Add((int)c.Weekday);
        r.Visits = j.Visits;

        // ---- 公転 ----
        r.PrevStreak = j.Streak;
        if (j.Period <= 0 || j.Streak <= 0)
        {
            j.Streak = 1;
            j.Period = c.Period;
            r.NewOrbit = true;
        }
        else if (c.Period > j.Period)
        {
            r.NewOrbit = true;
            int allowance = 1; // 惑星の能力（セレスタ）は 2026-11-09 にやめた
            int streak = j.Streak;
            var rescues = new List<int>(j.Rescues);
            int insurance = j.Insurance;
            int missed = c.Period - j.Period - 1;
            for (int i = 0, m = j.Period + 1; i < missed && streak > 0; i++, m++)
            {
                int mm = m;
                if (rescues.Count(x => mm - x < RESCUE_WINDOW) < allowance)
                {
                    rescues.Add(m);
                    r.Rescued++;
                }
                else if (insurance > 0)
                {
                    insurance--;
                    r.InsuranceUsed++;
                }
                else
                {
                    streak /= 2; // 途切れたら半分に戻す（休んだ公転ごと）
                    r.Broken = true;
                }
            }
            j.Streak = streak + 1;
            j.Rescues = rescues.Where(x => c.Period - x < RESCUE_WINDOW).ToList();
            j.Insurance = insurance;
            j.Period = c.Period;
        }
        r.Streak = j.Streak;
        r.Period = j.Period;
        j.Best = Math.Max(j.Best, j.Streak);

        // ---- パワーのチャージ（その公転にもう来ている状態で、別の日にまた来た） ----
        if (!first && !r.NewOrbit && r.NewDay && !string.IsNullOrEmpty(c.Center))
        {
            int before = j.PowerOf(c.Center);
            int after = Math.Min(POWER_MAX, before + 1);
            j.Power[c.Center] = after;
            r.PowerPlanet = c.Center;
            r.Power = after;
            r.PowerMaxed = before < POWER_MAX && after >= POWER_MAX;
        }

        // 休みの保険（オルビスの能力）は 2026-11-09 にやめた。貯まっている分は使える

        // ---- 惑星の解放 ----
        if (WEEKDAY_PLANETS.TryGetValue(c.Weekday, out var wd)) Grant(wd, owned, r);
        foreach (var (n, id) in VISIT_PLANETS)
            if (j.Visits >= n) Grant(id, owned, r);
        foreach (var (n, id) in STREAK_PLANETS)
            if (j.Streak >= n) Grant(id, owned, r);
        Grant(SEASON_PLANETS[(int)c.Season], owned, r);
        if (c.FullMoon) Grant("lunaris", owned, r);
        if (c.Recruit) Grant("hexia", owned, r);

        // ---- 流れ星（公転の最初の来店。テンペスタは2回） ----
        if (j.StarPeriod != c.Period)
        {
            j.StarPeriod = c.Period;
            j.Extra.Clear();
            int draws = 1; // 惑星の能力（テンペスタの2回）は 2026-11-09 にやめた
            bool powerMax = j.PowerOf(c.Center) >= POWER_MAX;
            for (int i = 0; i < draws; i++)
                AddPrize(Roll(rng, j.Streak, c.Center, c.Weekday, c.Season, powerMax), owned, r);
        }
        return j;
    }

    public static void Grant(string id, HashSet<string> owned, JourneyResult r)
    {
        if (string.IsNullOrEmpty(id) || owned.Contains(id) || r.Unlocked.Contains(id)) return;
        r.Unlocked.Add(id);
    }

    /// <summary>当たりを結果に足す。ノワールを持っていれば（今回の解放も含む）7%引きにする</summary>
    public static void AddPrize(StarPrize prize, HashSet<string> owned, JourneyResult r)
    {
        if (prize == StarPrize.Noir)
        {
            if (owned.Contains("noir") || r.Unlocked.Contains("noir")) prize = StarPrize.Seven;
            else r.Unlocked.Add("noir");
        }
        r.Prizes.Add(prize);
    }

    /// <summary>
    /// 流れ星を1回引く（中央の惑星の能力で当たりやすさが変わる）。
    /// powerMax: 中央の惑星のパワーが MAX（7%オフが POWER_SEVEN 倍出やすい）
    /// </summary>
    public static StarPrize Roll(System.Random rng, int streak, string center, DayOfWeek weekday,
        StoreCalendar.Season season, bool powerMax = false)
    {
        int tier = streak >= 13 ? 2 : streak >= 4 ? 1 : 0;
        var w = (double[])ODDS[tier].Clone(); // 5% / ドリンク / コーヒー / 7% / ノワール / 5時間無料 / フリータイム
        // 惑星ごとの当たりやすさ（能力）は 2026-11-09 にやめた。効くのはパワーMAXの7%オフだけ
        if (powerMax) w[3] *= POWER_SEVEN;

        double total = w.Sum();
        double x = rng.NextDouble() * total;
        int pick = 0;
        for (; pick < w.Length - 1; pick++)
        {
            if (x < w[pick]) break;
            x -= w[pick];
        }
        var prize = (StarPrize)pick; // ODDS と StarPrize は同じ並び
        return prize;
    }
}
