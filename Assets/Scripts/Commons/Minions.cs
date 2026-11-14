using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Firebase.Firestore;

/// <summary>ミニオン1体（users.minions.items の1つ。読み書きは MinionBook・MinionStore）</summary>
public sealed class MinionData
{
    public string Id;          // "1", "2", …（作った順の番号を文字にしたもの）
    public int Number;         // 作った順（1〜。並べる順）
    public int Job;            // 職業（MinionRules.JOB_NAMES の番号）
    public int Element;        // 属性（MinionRules.ELEMENT_NAMES の番号）
    public int Level = 1;      // 1〜MinionRules.MAX_LEVEL
    public DateTime Created;   // 作った時刻（UTC）
    public DateTime? Sent;     // 旅に出した時刻（UTC。サーバーの時刻）。null なら待機中
    public bool Ok;            // 旅がうまくいったか（旅に出したときに決めておく）
    public string Reward;      // 持ち帰る物（MinionRules の報酬のキー。失敗なら null）
    public bool Rare;          // レアな物か
    public string Day;         // 旅に出した営業日 "yyyy-MM-dd"（今日の幸運を決めた日）
    public int Luck;           // 今日の幸運の職業・属性と一致した数（0〜2）
    /// <summary>
    /// お店からもらったミニオン（2026-11-14。管理者のQR＝MinionGiftQr）。自分で作ったミニオンと同じく作れる数に数える
    /// （2026-11-14 ユーザー指定「自分で作れる数に数えて」）。作れる数を増やすかは AddsSlot
    /// </summary>
    public bool Gift;
    /// <summary>お店からもらったときのレベル（Gift のときだけ）</summary>
    public int GiftLevel;

    /// <summary>
    /// 作れる数を1体増やすか: Lv.MAX になったミニオン。ただしお店からもらったミニオンは、もらってから育てて Lv.MAX になったときだけ。
    /// もらった時点で Lv.MAX なら増やさない（2026-11-14 ユーザー指定「150LVを貰っても作れる数を1体増やさないで」）
    /// </summary>
    public bool AddsSlot => Level >= MinionRules.MAX_LEVEL && (!Gift || GiftLevel < MinionRules.MAX_LEVEL);

    public MinionData Clone() => (MinionData)MemberwiseClone();

    public Dictionary<string, object> ToMap()
    {
        var map = new Dictionary<string, object>
        {
            { "n", (long)Number },
            { "look", (long)Job }, // 保存のキーは最初の名前 look のまま
            { "element", (long)Element },
            { "level", (long)Level },
            { "created", Timestamp.FromDateTime(DateTime.SpecifyKind(Created, DateTimeKind.Utc)) },
        };
        if (Gift)
        {
            map["gift"] = true;
            map["gift_lv"] = (long)GiftLevel;
        }
        if (Sent.HasValue)
        {
            map["sent"] = Timestamp.FromDateTime(DateTime.SpecifyKind(Sent.Value, DateTimeKind.Utc));
            map["ok"] = Ok;
            map["reward"] = Reward ?? "";
            map["rare"] = Rare;
            map["day"] = Day ?? "";
            map["luck"] = (long)Luck;
        }
        return map;
    }

    public static MinionData From(string id, object raw)
    {
        if (string.IsNullOrEmpty(id) || !(raw is Dictionary<string, object> m)) return null;
        var d = new MinionData
        {
            Id = id,
            Number = MinionBook.Int(m, "n"),
            Job = Math.Max(0, Math.Min(MinionRules.JOBS - 1, MinionBook.Int(m, "look"))),
            Element = Math.Max(0, Math.Min(MinionRules.ELEMENTS - 1, MinionBook.Int(m, "element"))),
            Level = Math.Max(1, Math.Min(MinionRules.MAX_LEVEL, MinionBook.Int(m, "level"))),
            Created = MinionBook.Time(m, "created") ?? DateTime.MinValue,
            Sent = MinionBook.Time(m, "sent"),
            Gift = m.TryGetValue("gift", out var gift) && gift is bool g && g,
        };
        if (d.Number <= 0) int.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out d.Number);
        // もらったときのレベル（なければ今のレベル＝今が Lv.MAX なら、もらった時から Lv.MAX だったとみなす）
        if (d.Gift) d.GiftLevel = m.ContainsKey("gift_lv") ? Math.Max(1, MinionBook.Int(m, "gift_lv")) : d.Level;
        if (d.Sent.HasValue)
        {
            d.Ok = m.TryGetValue("ok", out var ok) && ok is bool b && b;
            d.Reward = m.TryGetValue("reward", out var rw) ? rw as string : null;
            if (string.IsNullOrEmpty(d.Reward)) d.Reward = null;
            d.Rare = m.TryGetValue("rare", out var rare) && rare is bool rb && rb;
            d.Day = m.TryGetValue("day", out var day) ? day as string : null;
            d.Luck = Math.Max(0, Math.Min(2, MinionBook.Int(m, "luck")));
        }
        return d;
    }
}

/// <summary>ミニオンの今の様子</summary>
public enum MinionState
{
    Idle,      // 待機中（旅に出せる）
    Traveling, // 旅の途中（出してから24時間）
    Back,      // 帰ってきた（1週間のうちに、入店中に話しかけると報酬を受け取れる）
    Expired,   // 帰ってから1週間すぎた（持ち帰った報酬を食べてしまった）
}

/// <summary>
/// ミニオンの持ち物全体（users.minions = { clock, next, items: { 番号: ミニオン }, claims: { QRの番号: 営業日 } }）。
/// clock は旅に出す・話しかける・作るたびに書くサーバーの時刻（MinionStore。端末の時計を進めても旅の時間をごまかせないように）。
/// claims はお店のミニオンのQR（MinionGiftQr）をその日に受け取った番号（同じQRは1人1回。前の日の分は受け取るときに消す）
/// </summary>
public sealed class MinionBook
{
    public readonly List<MinionData> Items = new List<MinionData>();
    public int Next = 1;
    /// <summary>最後に書いたサーバーの時刻（UTC）</summary>
    public DateTime? Clock;
    /// <summary>お店のミニオンのQRを受け取った番号 → 営業日 "yyyy-MM-dd"（2026-11-14）</summary>
    public readonly Dictionary<string, string> Claims = new Dictionary<string, string>();

    public static MinionBook From(object raw)
    {
        var b = new MinionBook();
        if (!(raw is Dictionary<string, object> m)) return b;
        b.Clock = Time(m, "clock");
        b.Next = Math.Max(1, Int(m, "next"));
        if (m.TryGetValue("claims", out var claimsRaw) && claimsRaw is Dictionary<string, object> claims)
            foreach (var kv in claims)
                if (kv.Value != null) b.Claims[kv.Key] = kv.Value.ToString();
        if (m.TryGetValue("items", out var itemsRaw) && itemsRaw is Dictionary<string, object> items)
        {
            foreach (var kv in items)
            {
                var d = MinionData.From(kv.Key, kv.Value);
                if (d != null) b.Items.Add(d);
            }
        }
        b.Items.Sort((x, y) => x.Number.CompareTo(y.Number));
        foreach (var d in b.Items) b.Next = Math.Max(b.Next, d.Number + 1);
        return b;
    }

    public Dictionary<string, object> ItemsMap()
    {
        var m = new Dictionary<string, object>();
        foreach (var d in Items) m[d.Id] = d.ToMap();
        return m;
    }

    public Dictionary<string, object> ClaimsMap()
    {
        var m = new Dictionary<string, object>();
        foreach (var kv in Claims) m[kv.Key] = kv.Value;
        return m;
    }

    /// <summary>端末に持っておく形（clock・next・items・claims をそろえたもの）</summary>
    public Dictionary<string, object> ToMap()
    {
        var m = new Dictionary<string, object> { { "next", (long)Next }, { "items", ItemsMap() } };
        if (Clock.HasValue) m["clock"] = Timestamp.FromDateTime(DateTime.SpecifyKind(Clock.Value, DateTimeKind.Utc));
        if (Claims.Count > 0) m["claims"] = ClaimsMap();
        return m;
    }

    public MinionData Find(string id) => Items.Find(x => x.Id == id);

    /// <summary>
    /// 持てるミニオンの数（1体目＋Lv.MAX になって作れる数を増やしたミニオンの数。MinionData.AddsSlot。
    /// もらった時点で Lv.MAX だったお店のミニオンは増やさない）
    /// </summary>
    public int MaxCount => 1 + Items.Count(x => x.AddsSlot);

    /// <summary>
    /// 新しいミニオンを作れるか（お店からもらったミニオンも数に入れる。2026-11-14 ユーザー指定「自分で作れる数に数えて」）。
    /// 数え方から、いまいるミニオンがみんな作れる数を増やした（Lv.MAX になった）ときに作れる
    /// </summary>
    public bool CanCreate => Items.Count < MaxCount;

    /// <summary>
    /// もう新しく作れないか: 作れる数を増やさない Lv.MAX のミニオン（もらった時点で Lv.MAX だったお店のミニオン）がいると、
    /// ほかのミニオンがみんな Lv.MAX になっても作れる数が足りない
    /// </summary>
    public bool CreateBlocked => Items.Any(x => x.Level >= MinionRules.MAX_LEVEL && !x.AddsSlot);

    /// <summary>まだ Lv.MAX でないミニオンの数（作れないとき、この数のミニオンが Lv.MAX になると作れる）</summary>
    public int Growing => Items.Count(x => x.Level < MinionRules.MAX_LEVEL);

    internal static int Int(Dictionary<string, object> m, string k)
    {
        if (m == null || !m.TryGetValue(k, out var v) || v == null) return 0;
        try { return Convert.ToInt32(v, CultureInfo.InvariantCulture); }
        catch { return 0; }
    }

    internal static DateTime? Time(Dictionary<string, object> m, string k)
    {
        if (m == null || !m.TryGetValue(k, out var v) || v == null) return null;
        if (v is Timestamp ts) return ts.ToDateTime().ToUniversalTime();
        if (v is DateTime dt) return dt.ToUniversalTime();
        return null;
    }
}

/// <summary>旅に出したときに選べる報酬（使える機能だけ。MinionStore が今の解放の様子で作る）</summary>
public struct MinionUnlocks
{
    public bool Pieces; // ピースパズル（ピース座）
    public bool Gold;   // アプリ内の通貨 G（金貨座）
    public bool Cards;  // トランプ（宝石座か切り札座）
}

/// <summary>
/// ミニオン（2026-11-14 ユーザー指定）の決まり。
///  ・きずな座を完成させると（管理者は初めから）、ミニオンを作れる。職業は戦士・魔法使い・弓使い・銃使い、属性は炎・水・自然・雷から選ぶ
///    （同日ユーザー指定で、最初に「見た目」と呼んでいたものは「職業」に名前を統一。画面もコードも職業＝Job。保存のキーだけ look のまま）
///  ・待機中のミニオンを旅に出すと、24時間後に帰ってくる。帰ってから1週間のうちに話しかけると、持ち帰った報酬を受け取れる。
///    受け取れるのは入店中だけ（2026-11-14 ユーザー指定。MinionStore.InStore）。1週間すぎると、ミニオンが報酬を食べてしまう
///  ・帰ってくるたびにレベルが1上がる（失敗した旅でも）。報酬を食べたときもさらに1上がる。Lv.MAX は 150
///  ・旅は失敗することもある。成功率は Lv.1 で 50%・Lv.150 で 95%（そのあいだは同じ割合で上がる）。
///    毎日（営業日ごと）成功しやすい職業と属性が1つずつ決まり、一致するごとに成功率 +5%（100% まで）
///  ・持ち帰る物がレアな割合は、Lv.1 で 5%（こちらで決めた）・Lv.150 で 33%（ユーザー指定）。
///    レア: 7%OFFチケット（いちばんレア）・U字のピース・黒コマ・10G・5%OFFチケット・トランプを山札から1枚。
///    ふつう: 星のカケラ・赤／青／黄コマ・惑星回復薬・ピース（U字以外のどれか）。それぞれの割合はこちらで決めた（RARE・NORMAL）
///  ・ミニオンが Lv.150 になると、もう1体作れる（2体目・3体目…。それぞれ同時に旅に出せる）。
///    お店からもらったミニオン（Gift）も持っている数に入る。もらってから育てて Lv.150 になれば1体増えるが、
///    もらった時点で Lv.150 なら増えない（MinionData.AddsSlot）
/// 旅がうまくいくか・何を持ち帰るかは旅に出したときに決めておく（話しかけるたびに引き直せないように）。
/// </summary>
public static class MinionRules
{
    public const int JOBS = 4, ELEMENTS = 4;
    public static readonly string[] JOB_NAMES = { "戦士", "魔法使い", "弓使い", "銃使い" };
    public static readonly string[] ELEMENT_NAMES = { "炎", "水", "自然", "雷" };

    public const int MAX_LEVEL = 150;
    /// <summary>旅に出してから帰ってくるまで（時間）</summary>
    public const int TRIP_HOURS = 24;
    /// <summary>帰ってから報酬を持っていてくれる日数（すぎると食べてしまう）</summary>
    public const int KEEP_DAYS = 7;
    /// <summary>旅の成功率（Lv.1・Lv.150。ユーザー指定「レベルが低いと50%くらいで失敗、MAXだと95%で成功」）</summary>
    public const double SUCCESS_MIN = 0.50, SUCCESS_MAX = 0.95;
    /// <summary>今日の幸運の職業・属性と一致するごとに足す成功率（ユーザー指定）</summary>
    public const double LUCK_BONUS = 0.05;
    /// <summary>持ち帰る物がレアな割合（Lv.1 はこちらで決めた・Lv.150 はユーザー指定の 33%）</summary>
    public const double RARE_MIN = 0.05, RARE_MAX = 0.33;

    // 報酬のキー（ふつうは users のフィールド名。ピース・G・トランプだけ別の書き方）
    public const string CARD = "card";
    public const string PIECE_PREFIX = "piece:";
    public const string GOLD_PREFIX = "gold:";
    public const int GOLD_AMOUNT = 10;
    /// <summary>ふつうの報酬の「ピース（U字以外のどれか）」（旅に出したときに種類を決めて "piece:3" のように持つ）</summary>
    private const string ANY_PIECE = "piece";

    private sealed class RewardDef
    {
        public string Key;
        public int Weight;
        public bool NeedPieces, NeedGold, NeedCards;
    }

    /// <summary>レアの報酬と、その中での割合（7%OFFチケットがいちばんレア。ほかは同じ割合）</summary>
    private static readonly RewardDef[] RARE =
    {
        new RewardDef { Key = "seven_coupon", Weight = 5 },
        new RewardDef { Key = PIECE_PREFIX + PuzzleRules.RARE, Weight = 19, NeedPieces = true },
        new RewardDef { Key = "black_meeple", Weight = 19 },
        new RewardDef { Key = GOLD_PREFIX + GOLD_AMOUNT, Weight = 19, NeedGold = true },
        new RewardDef { Key = "five_coupon", Weight = 19 },
        new RewardDef { Key = CARD, Weight = 19, NeedCards = true },
    };

    /// <summary>ふつうの報酬と、その中での割合</summary>
    private static readonly RewardDef[] NORMAL =
    {
        new RewardDef { Key = "star_fragment", Weight = 25 },
        new RewardDef { Key = "red_meeple", Weight = 15 },
        new RewardDef { Key = "blue_meeple", Weight = 15 },
        new RewardDef { Key = "yellow_meeple", Weight = 15 },
        new RewardDef { Key = "planet_potion", Weight = 15 },
        new RewardDef { Key = ANY_PIECE, Weight = 15, NeedPieces = true },
    };

    /// <summary>報酬になりうる物のキー（表示の文字の登録・確認用。ピースは種類ごと）</summary>
    public static IEnumerable<string> AllRewardKeys
    {
        get
        {
            foreach (var r in RARE) yield return r.Key;
            foreach (var r in NORMAL)
            {
                if (r.Key != ANY_PIECE) yield return r.Key;
                else
                    for (int t = 1; t < PuzzleRules.TYPES; t++) yield return PIECE_PREFIX + t;
            }
        }
    }

    public static bool IsPiece(string key, out int type)
    {
        type = 0;
        return key != null && key.StartsWith(PIECE_PREFIX, StringComparison.Ordinal)
               && int.TryParse(key.Substring(PIECE_PREFIX.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out type)
               && type >= 1 && type <= PuzzleRules.TYPES;
    }

    public static bool IsGold(string key, out int amount)
    {
        amount = 0;
        return key != null && key.StartsWith(GOLD_PREFIX, StringComparison.Ordinal)
               && int.TryParse(key.Substring(GOLD_PREFIX.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out amount)
               && amount > 0;
    }

    public static string NameOf(MinionData m) => NameOf(m.Job, m.Element);

    /// <summary>「炎の戦士」</summary>
    public static string NameOf(int job, int element) =>
        ELEMENT_NAMES[Math.Max(0, Math.Min(ELEMENTS - 1, element))] + "の" + JOB_NAMES[Math.Max(0, Math.Min(JOBS - 1, job))];

    private static double LevelT(int level) => (Math.Max(1, Math.Min(MAX_LEVEL, level)) - 1) / (double)(MAX_LEVEL - 1);

    /// <summary>旅の成功率（0〜1）。luck は今日の幸運と一致した数（0〜2）</summary>
    public static double SuccessChance(int level, int luck) =>
        Math.Min(1.0, SUCCESS_MIN + (SUCCESS_MAX - SUCCESS_MIN) * LevelT(level) + LUCK_BONUS * Math.Max(0, luck));

    /// <summary>持ち帰る物がレアな割合（0〜1）</summary>
    public static double RareChance(int level) => RARE_MIN + (RARE_MAX - RARE_MIN) * LevelT(level);

    /// <summary>今日の幸運を数え始める営業日（この日から1日進むごとに、前の日と違う組み合わせへ移る）</summary>
    private static readonly DateTime LUCK_EPOCH = new DateTime(2026, 11, 1);
    private const string DAY_KEY_FORMAT = "yyyy-MM-dd";

    private sealed class LuckMemo
    {
        public string Key;
        public int Combo;
    }
    // 最後に決めた日（画面を作るたびに同じ日を何度も聞かれるので覚えておく。裏のスレッドからも読むので丸ごと差し替える）
    private static LuckMemo s_luck;

    /// <summary>
    /// その営業日（"yyyy-MM-dd"。朝4時で変わる）に成功しやすい職業と属性（みんな同じ。日付の文字から決める自前の乱数なので、
    /// どの端末でも同じ）。毎日かならず前の日と違う組み合わせになる（16通りのうち、前の日以外の15通りから日付の乱数で選ぶ。
    /// 2026-11-14 ユーザーの確認「今日の幸運って毎日変わるようになってる？」。最初の作りは職業が前の日と同じになりやすかった）
    /// </summary>
    public static void Lucky(string dayKey, out int job, out int element)
    {
        var memo = s_luck;
        int combo;
        if (memo != null && memo.Key == dayKey) combo = memo.Combo;
        else
        {
            combo = LuckyCombo(dayKey);
            s_luck = new LuckMemo { Key = dayKey, Combo = combo };
        }
        job = combo / ELEMENTS;
        element = combo % ELEMENTS;
    }

    /// <summary>その日の組み合わせの番号（職業 × ELEMENTS ＋ 属性）。数え始めの日から1日ずつ、前の日と違う番号へ進める</summary>
    private static int LuckyCombo(string dayKey)
    {
        const int N = JOBS * ELEMENTS;
        if (!DateTime.TryParseExact(dayKey, DAY_KEY_FORMAT, CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
            || day < LUCK_EPOCH)
            return (int)(Mix(dayKey) % N); // 数え始めより前（ふつうはない）は、その日の乱数だけで
        int combo = (int)(Mix(LUCK_EPOCH.ToString(DAY_KEY_FORMAT, CultureInfo.InvariantCulture)) % N);
        for (var d = LUCK_EPOCH.AddDays(1); d <= day; d = d.AddDays(1))
            combo = (combo + 1 + (int)(Mix(d.ToString(DAY_KEY_FORMAT, CultureInfo.InvariantCulture)) % (N - 1))) % N;
        return combo;
    }

    /// <summary>日付の文字から作る乱数（FNV-1a のあとに murmur3 の仕上げで混ぜる。下の桁までよく混ざるように）</summary>
    private static uint Mix(string key)
    {
        uint h = 2166136261u; // FNV-1a
        foreach (char ch in "minion:" + (key ?? ""))
        {
            h ^= ch;
            h *= 16777619u;
        }
        h ^= h >> 16; // murmur3 の fmix32
        h *= 0x85EBCA6Bu;
        h ^= h >> 13;
        h *= 0xC2B2AE35u;
        h ^= h >> 16;
        return h;
    }

    /// <summary>今日の幸運と一致した数（0〜2）</summary>
    public static int LuckOf(int job, int element, string dayKey)
    {
        Lucky(dayKey, out int ll, out int le);
        return (job == ll ? 1 : 0) + (element == le ? 1 : 0);
    }

    public static DateTime BackAt(MinionData m) => (m.Sent ?? DateTime.MinValue).AddHours(TRIP_HOURS);
    public static DateTime EatAt(MinionData m) => BackAt(m).AddDays(KEEP_DAYS);

    public static MinionState StateOf(MinionData m, DateTime nowUtc)
    {
        if (m == null || !m.Sent.HasValue) return MinionState.Idle;
        if (nowUtc < BackAt(m)) return MinionState.Traveling;
        if (nowUtc < EatAt(m)) return MinionState.Back;
        return MinionState.Expired;
    }

    /// <summary>
    /// 旅に出す: 今日（dayKey）の幸運との一致・旅がうまくいくか・持ち帰る物を決める（rng は 0〜1 の乱数のもと）
    /// </summary>
    public static void Send(MinionData m, DateTime nowUtc, string dayKey, Random rng, MinionUnlocks unlocks)
    {
        m.Sent = nowUtc;
        m.Day = dayKey;
        m.Luck = LuckOf(m.Job, m.Element, dayKey);
        m.Ok = rng.NextDouble() < SuccessChance(m.Level, m.Luck);
        m.Rare = false;
        m.Reward = null;
        if (m.Ok) m.Reward = RollReward(rng, m.Level, unlocks, out m.Rare);
    }

    /// <summary>持ち帰る物を引く（使えない機能の物は除いて、残りの割合で）</summary>
    public static string RollReward(Random rng, int level, MinionUnlocks unlocks, out bool rare)
    {
        rare = rng.NextDouble() < RareChance(level);
        var table = (rare ? RARE : NORMAL)
            .Where(r => (!r.NeedPieces || unlocks.Pieces) && (!r.NeedGold || unlocks.Gold) && (!r.NeedCards || unlocks.Cards))
            .ToList();
        int total = table.Sum(r => r.Weight);
        int pick = rng.Next(Math.Max(1, total));
        foreach (var r in table)
        {
            if (pick < r.Weight) return r.Key == ANY_PIECE ? PIECE_PREFIX + (1 + rng.Next(PuzzleRules.TYPES - 1)) : r.Key;
            pick -= r.Weight;
        }
        return table.Count > 0 ? table[0].Key : "star_fragment";
    }

    /// <summary>
    /// 帰ってきた旅を終える（話しかけた・1週間すぎた）。帰ってきたのでレベル +1、報酬を食べたら（ate）さらに +1（MAX まで）
    /// </summary>
    public static void Finish(MinionData m, bool ate)
    {
        m.Level = Math.Min(MAX_LEVEL, m.Level + 1 + (ate ? 1 : 0));
        m.Sent = null;
        m.Ok = false;
        m.Reward = null;
        m.Rare = false;
        m.Day = null;
        m.Luck = 0;
    }
}
