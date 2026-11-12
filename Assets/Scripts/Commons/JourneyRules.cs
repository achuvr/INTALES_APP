using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// 流れ星の当たり。ODDS の列と同じ並び（新しい当たりは必ず末尾に足す）。クーポン（5%・ドリンク・コーヒー・7%・5時間無料・
/// フリータイム）と、ひみつの惑星ノワール。2026-11-12 から持ち物の当たり（赤・青・黄・黒コマ・7G・惑星回復薬・
/// 5%OFFチケットのカケラ。中身は JourneyService.ItemOf）も
/// </summary>
public enum StarPrize
{
    Five, Drink, Coffee, Seven, Noir, FiveHour, FreeTime,
    MeepleRed, MeepleBlue, MeepleYellow, MeepleBlack, Gold, PlanetPotion, FiveFragment,
}

/// <summary>
/// 流れ星の当たりで増える物（トランザクションの中で JourneyService.StarRewardsOf が数える。書き込みと手元の数・履歴に使う）
/// </summary>
public sealed class StarRewards
{
    /// <summary>増える数（users のフィールドごと。クーポン・コマ・G・惑星回復薬と、カケラがそろってできた5%OFFチケット）</summary>
    public readonly Dictionary<string, long> Inc = new Dictionary<string, long>();
    /// <summary>5%OFFチケットのカケラが当たったときの、そろった分を引いたあとのカケラの数（当たらなければ -1。数そのものを書く）</summary>
    public long Fragments = -1;
    /// <summary>カケラがそろってできた5%OFFチケットの枚数</summary>
    public int Completed;
}

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
    public readonly List<StarPrize> Prizes = new List<StarPrize>();
    /// <summary>流れ星の当たり（Prizes）で増えた物（書き込むときに数える。手元の数・履歴に使う。当たりがなければ null のことも）</summary>
    public StarRewards Rewards;
    public readonly List<string> Unlocked = new List<string>(); // 解放した惑星のID
    /// <summary>もらったコマの色（1色1個。赤・青・黄の順。入店した日の最初の入店のときだけ）</summary>
    public readonly List<PlanetColor> Meeples = new List<PlanetColor>();
    /// <summary>入店した日の最初の入店で、中央の惑星が力を失っていたのでコマをもらえなかった</summary>
    public bool CenterExhausted;
    /// <summary>
    /// 複数の色の惑星で入店した: この色の中から1個だけ選んでもらう（選ぶ画面を出す。2026-11-10）。なければ None
    /// </summary>
    public PlanetColor MeeplePick;
    /// <summary>今回力を失った惑星のID（退店したとき。退店しないまま次の営業日に入店したときも）。なければ null</summary>
    public string Exhausted;
    /// <summary>退店の結果（力を失ったお知らせを出すのは退店のときだけ）</summary>
    public bool CheckOut;
    /// <summary>もらったアプリ内の通貨（単位 G。入店した日の最初の入店のときだけ。金貨座を完成させた人だけ。2026-11-12）</summary>
    public int Ig;
    /// <summary>山札から引いたトランプ（入店した日の最初の入店のときだけ。宝石座か切り札座を完成させた人だけ。なければ -1）</summary>
    public int Card = -1;
    /// <summary>引いて手札が8枚になった（このあと1枚捨てる）</summary>
    public bool CardOverflow;
    /// <summary>書き込む手札と山札（users.cards。引いたときだけ）</summary>
    public Dictionary<string, object> CardsMap;

    public bool HasRewards => Prizes.Count > 0 || Unlocked.Count > 0;
}

/// <summary>
/// 惑星と来店のしくみの判定（公転・惑星の解放・流れ星の抽選）。Unity・Firebase を使わない計算だけをここに置く
/// （JourneyService がトランザクションの中から呼ぶ。エディタの外の試験プログラムでも動かせる）。
/// </summary>
public static class JourneyRules
{
    // 節目（公転を続けた数）・来店回数・曜日・季節で解放する惑星。
    // 2026-11-10: 新しい惑星（ランテルナ=9公転・ヴェスペラ=5回目・アンバリア=15回目・フォリア=秋）を足し、
    // 最初に選べる3つ（ルブラ・ルミナ・リモナ）は、選ばなかったものを7回目の来店で解放する（ルブラの秋・リモナの5回目はやめた）
    public static readonly (int n, string id)[] STREAK_PLANETS =
        { (2, "mistia"), (4, "tempesta"), (6, "celesta"), (9, "lanterna"), (13, "neonia"), (26, "galaxia") };
    public static readonly (int n, string id)[] VISIT_PLANETS =
    {
        (2, "aquaris"), (5, "vespera"), (7, "rubra"), (7, "lumina"), (7, "limona"), (10, "mocha"), (15, "ambaria"),
        (25, "voltia"), (50, "noctis"),
    };
    public static readonly Dictionary<DayOfWeek, string> WEEKDAY_PLANETS = new Dictionary<DayOfWeek, string>
    {
        { DayOfWeek.Thursday, "verdia" }, { DayOfWeek.Friday, "crysta" },
        { DayOfWeek.Saturday, "orbis" }, { DayOfWeek.Sunday, "soleil" },
    };
    public static readonly string[] SEASON_PLANETS = { "pastelia", "coralia", "folia", "glacia" }; // 秋は 2026-11-10 にルブラからフォリアへ

    /// <summary>軌道修正を数える範囲（公転の数）。「3か月（6公転）に1回まで」</summary>
    public const int RESCUE_WINDOW = 6;
    /// <summary>軌道修正できる回数（RESCUE_WINDOW の中で。セレスタの2回は 2026-11-09 にやめた。公転の画面の表示もこれ）</summary>
    public const int RESCUE_ALLOWANCE = 1;
    /// <summary>秋のルブラで流れ星をもう1回引ける滞在時間（分）</summary>
    public const int RUBRA_STAY_MINUTES = 4 * 60;
    /// <summary>来店1回（1営業日）でもらえるアプリ内の通貨（2026-11-12 ユーザー指定「来店時に1」。単位は G。IgCurrency）</summary>
    public const int IG_PER_VISIT = 1;
    /// <summary>流れ星の当たりの G の額（2026-11-12 ユーザー指定「7G」。StarPrize.Gold）</summary>
    public const int STAR_GOLD = 7;

    // 惑星のパワー（同じ公転にもう一度来ると中央の惑星にたまり、MAX で流れ星の7%オフが出やすい）は
    // 2026-11-10 にやめた（ユーザー指定「お客様に説明が難しい」）。たまっていた値は JourneyData.Power にそのまま残る

    // 「もう1回」の種類（1公転に1回ずつ）
    public const string EXTRA_TOGETHER = "together"; // オパーリア: フレンドと一緒に来店
    public const string EXTRA_FIRST = "first";       // パステリア: 春に、初めて来店する人と一緒に来店
    public const string EXTRA_GIFT = "gift";         // フレンドの惑星の能力でもらった
    public const string EXTRA_RUBRA = "rubra";       // ルブラ: 秋に4時間以上の滞在

    /// <summary>
    /// 持ち物の当たりの重み（%。公転の数に関係なく同じ・合計30）。並び（StarPrize の MeepleRed から）:
    /// 赤コマ / 青コマ / 黄コマ / 黒コマ / 7G / 惑星回復薬 / 5%OFFチケットのカケラ。
    /// 2026-11-12 ユーザー指定「流れ星から出てくるものに、各色のミープル（黒も）・7G・惑星回復ポーション・5%OFFチケットの
    /// カケラを追加」で足した。割合の指定はなかったので合計30%にし、その分をいちばん出やすい5%から引いた
    /// （ほかのクーポン・ノワールの割合は変えていない。5% は 約70→40% / 約55→25% / 約40→10%）
    /// </summary>
    private static readonly double[] ITEM_ODDS = { 5.0, 5.0, 5.0, 2.0, 3.0, 5.0, 5.0 };

    /// <summary>
    /// 流れ星の当たりの重み（%。合計100）。公転の数 1〜3 / 4〜12 / 13〜 の3段階（企画書の例そのまま）。
    /// 並び: 5% / ドリンク / コーヒー / 7% / ノワール / 5時間無料 / フリータイム / 持ち物の当たり（ITEM_ODDS）。
    /// 5時間無料・フリータイムは 2026-11-07 追加（ユーザー指定「提案の3分の1の確率で」: 5時間無料 0.3/0.6/1.0% と
    /// フリータイム 0.1/0.3/0.5% の3分の1）。足した分は5%から引いて合計を100のままにした（持ち物の当たりも同じ）
    /// </summary>
    public static readonly double[][] ODDS =
    {
        WithItems(70.0 - 0.1 - 0.1 / 3, 20.0, 7.5, 2.0, 0.5, 0.1, 0.1 / 3),
        WithItems(55.0 - 0.2 - 0.1, 27.0, 13.0, 4.0, 1.0, 0.2, 0.1),
        WithItems(40.0 - 1.0 / 3 - 0.5 / 3, 33.0, 18.0, 7.0, 2.0, 1.0 / 3, 0.5 / 3),
    };

    /// <summary>クーポン・ノワールの重み（7つ）のうしろに持ち物の当たりの重みを並べ、その合計を5%（先頭）から引いた1段階分</summary>
    private static double[] WithItems(params double[] coupons)
    {
        var row = new double[coupons.Length + ITEM_ODDS.Length];
        coupons.CopyTo(row, 0);
        ITEM_ODDS.CopyTo(row, coupons.Length);
        row[0] -= ITEM_ODDS.Sum();
        return row;
    }


    public sealed class VisitContext
    {
        public string DayKey;
        public int Period;
        public DayOfWeek Weekday;
        public StoreCalendar.Season Season;
        public bool FullMoon;
        public bool Recruit;
        public string Center;
        public PlanetColor CenterColors; // 中央の惑星の色（コマを配る）
        /// <summary>コマを配るか（INTALES WORLD をダウンロードした人だけ。2026-11-10）</summary>
        public bool Meeples;
        /// <summary>
        /// 管理者のデバッグ（2026-11-10 ユーザー指定「管理者は何度もミープルを惑星から貰える」）:
        /// その日の2回目以降の入店でも、力を失った惑星でもコマをもらえる（惑星はふつうどおり使ったことになる）
        /// </summary>
        public bool Debug;
        /// <summary>アプリ内の通貨（G）がもらえるか（金貨座が完成している。管理者は初めから。2026-11-12）</summary>
        public bool Ig;
        /// <summary>トランプを引けるか（宝石座か切り札座が完成している。管理者は初めから。2026-11-12）</summary>
        public bool Cards;
        /// <summary>営業日の月 "yyyy-MM"（トランプは月が変わるとリセット）</summary>
        public string CardMonth;
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

        // ---- アプリ内の通貨（2026-11-12 ユーザー指定「金貨座を解放したら、来店時に1」。単位は G）。来店回数と同じく1営業日に1回
        //      （入店・退店を繰り返しても増えない。管理者も同じ）。解放より前の来店の分はさかのぼらない ----
        if (r.NewDay && c.Ig) r.Ig = IG_PER_VISIT;

        // ---- コマ（2026-11-10 ユーザー指定「その色の惑星で入店したときに、その色のミープルを1個。
        //      複数の色を持つ惑星は、それぞれ1個ずつ」）。来店回数と同じく1営業日に1回（入店しなおしても増えない）。
        //      「1回使って退店すると、その惑星は力を失い、コマがもらえなくなる」（同日のユーザー指定）ので、
        //      もらった惑星を Used に覚えておき、退店で Exhausted に移す（ApplyCheckOut）。
        //      退店しないまま次の営業日に来たときは、ここで移してからコマを判定する ----
        //      INTALES WORLD をダウンロードしていない人（c.Meeples が false）には配らない（同日のユーザー指定）。
        //      複数の色の惑星は「その色の中から選んだ1個だけ」（同日のユーザー指定で「1色1個ずつ」から変更）。
        //      ここでは選べる色を Pick に入れるだけで、選んだときに渡す（JourneyService.ClaimMeepleAsync）
        if (!string.IsNullOrEmpty(j.Used) && j.UsedDay != c.DayKey) Exhaust(j, r);
        bool give = (r.NewDay || c.Debug) && c.Meeples;
        if (j.Pick != 0 && (j.PickDay != c.DayKey || (give && c.Debug)))
        {
            // 前の入店で選ばないまま次の入店になった分は、赤・青・黄の順で最初の色にして渡す（なくさないように）。
            // 管理者は同じ日に何度ももらうので、選ばないまま入店し直したときも渡す
            var firstColor = FirstColor((PlanetColor)j.Pick);
            if (firstColor != PlanetColor.None) r.Meeples.Add(firstColor);
            j.Pick = 0;
            j.PickDay = null;
        }
        if (give)
        {
            if (!c.Debug && !string.IsNullOrEmpty(c.Center) && j.Exhausted.Contains(c.Center))
            {
                r.CenterExhausted = true;
            }
            else
            {
                var colors = c.CenterColors & PlanetColor.All;
                int n = 0;
                foreach (var color in PlanetColorOrder.All)
                    if ((colors & color) != 0) n++;
                if (n == 1)
                {
                    r.Meeples.Add(colors);
                }
                else if (n > 1)
                {
                    r.MeeplePick = colors;
                    j.Pick = (int)colors;
                    j.PickDay = c.DayKey;
                }
                if (n > 0 && !string.IsNullOrEmpty(c.Center))
                {
                    j.Used = c.Center;
                    j.UsedDay = c.DayKey;
                }
            }
        }

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
            int allowance = RESCUE_ALLOWANCE;
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

        // パワーのチャージ（同じ公転にもう一度来たとき）は 2026-11-10 にやめた
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
            for (int i = 0; i < draws; i++)
                AddPrize(Roll(rng, j.Streak, c.Center, c.Weekday, c.Season), owned, r);
        }
        return j;
    }

    /// <summary>
    /// トランプを1枚引く（2026-11-12 ユーザー指定「来店時に1枚山札から引ける」）。入店した日の最初の入店のときだけ
    /// （来店回数と同じく1営業日に1回）。cardsRaw は users.cards の中身。引いたら r.Card・r.CardOverflow・r.CardsMap を入れる
    /// </summary>
    public static void DrawCardOnVisit(object cardsRaw, VisitContext c, JourneyResult r, System.Random rng)
    {
        if (!r.NewDay || !c.Cards || string.IsNullOrEmpty(c.CardMonth)) return;
        var s = CardState.From(cardsRaw);
        CardRules.Normalize(s, c.CardMonth);
        if (s.Day == c.DayKey) return; // その営業日はもう引いた（ショップで引いた分は別に数える）
        r.Card = CardRules.Draw(s, rng);
        s.Day = c.DayKey;
        r.CardOverflow = CardRules.Overflow(s);
        r.CardsMap = s.ToMap();
    }

    /// <summary>
    /// まだ選んでいないコマ（Pick）から1色選ぶ。選べる色なら Pick を空にして true（呼んだ側でその色のコマを1個足す）
    /// </summary>
    public static bool ClaimPick(JourneyData j, PlanetColor color)
    {
        if (j == null || j.Pick == 0) return false;
        bool single = color == PlanetColor.Red || color == PlanetColor.Blue || color == PlanetColor.Yellow;
        if (!single || (j.Pick & (int)color) == 0) return false;
        j.Pick = 0;
        j.PickDay = null;
        return true;
    }

    /// <summary>色の組み合わせのうち、赤・青・黄の順で最初の色（なければ None）</summary>
    public static PlanetColor FirstColor(PlanetColor colors)
    {
        foreach (var c in PlanetColorOrder.All)
            if ((colors & c) != 0) return c;
        return PlanetColor.None;
    }

    /// <summary>退店した: コマをもらった惑星（Used）が力を失う（2026-11-10）</summary>
    public static void ApplyCheckOut(JourneyData j, JourneyResult r)
    {
        if (j != null && !string.IsNullOrEmpty(j.Used)) Exhaust(j, r);
    }

    /// <summary>Used の惑星を力を失った惑星に移す</summary>
    private static void Exhaust(JourneyData j, JourneyResult r)
    {
        string id = j.Used;
        j.Used = null;
        j.UsedDay = null;
        if (string.IsNullOrEmpty(id) || j.Exhausted.Contains(id)) return;
        j.Exhausted.Add(id);
        r.Exhausted = id;
    }

    /// <summary>力を失った惑星の一覧（退店し忘れのまま営業日が変わった Used も含める。並びは力を失った順）</summary>
    public static List<string> ExhaustedList(JourneyData j, string todayKey)
    {
        var list = new List<string>();
        if (j == null) return list;
        list.AddRange(j.Exhausted);
        if (!string.IsNullOrEmpty(j.Used) && j.UsedDay != todayKey && !list.Contains(j.Used)) list.Add(j.Used);
        return list;
    }

    /// <summary>
    /// 力を失った惑星に力を戻す（INTALES WORLD の「惑星を3つアクティブにする」。2026-11-10）。
    /// 退店し忘れの Used は先に力を失った惑星へ移してから戻す。実際に戻した惑星を返す
    /// </summary>
    public static List<string> Reactivate(JourneyData j, IEnumerable<string> ids, string todayKey)
    {
        var done = new List<string>();
        if (j == null || ids == null) return done;
        if (!string.IsNullOrEmpty(j.Used) && j.UsedDay != todayKey) Exhaust(j, new JourneyResult());
        foreach (var id in ids)
            if (!string.IsNullOrEmpty(id) && j.Exhausted.Remove(id)) done.Add(id);
        return done;
    }

    /// <summary>
    /// 力を失った惑星か（表示用。退店した惑星に加え、コマをもらったまま退店せずに営業日が変わった惑星も含める。
    /// todayKey は今の営業日 "yyyy-MM-dd"）
    /// </summary>
    public static bool IsExhausted(JourneyData j, string id, string todayKey)
    {
        if (j == null || string.IsNullOrEmpty(id)) return false;
        if (j.Exhausted.Contains(id)) return true;
        return j.Used == id && j.UsedDay != todayKey;
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
    /// 流れ星を1回引く。当たりやすさは公転の数（3段階）だけで決まる
    /// （惑星ごとの当たりやすさは 2026-11-09、パワー MAX の7%オフの出やすさは 2026-11-10 にやめた）
    /// </summary>
    public static StarPrize Roll(System.Random rng, int streak, string center, DayOfWeek weekday,
        StoreCalendar.Season season)
    {
        int tier = streak >= 13 ? 2 : streak >= 4 ? 1 : 0;
        var w = (double[])ODDS[tier].Clone(); // 5% / ドリンク / コーヒー / 7% / ノワール / 5時間無料 / フリータイム / 持ち物

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
