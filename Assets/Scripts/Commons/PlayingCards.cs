using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

/// <summary>ポーカーの役（弱い順。並びの数字で強さを比べる）</summary>
public enum PokerHand
{
    HighCard, OnePair, TwoPair, ThreeOfAKind, Straight, Flush, FullHouse, FourOfAKind, StraightFlush, RoyalFlush,
}

/// <summary>ポーカーの役ごとの報酬（G と、クーポン1種類）</summary>
public sealed class PokerReward
{
    public PokerHand Hand;
    public string Name;      // 役の名前（「フルハウス」）
    public string Desc;      // 役の短い説明（「同じ数字3枚 ＋ 同じ数字2枚」）
    public int Gold;         // もらえる G（0 ならなし）
    public string Coupon;    // もらえるクーポンの users のフィールド名（null ならなし）
    public int CouponCount = 1;
}

/// <summary>
/// 手札と山札（users/{uid}.cards = { month, hand, deck, day }）。2026-11-12。
///  ・month: 営業日の月 "yyyy-MM"。月が変わると手札も山札もリセット（CardRules.Normalize）
///  ・hand: 手札のカード（番号 0〜51。引いた順）。上限は7枚で、8枚になったら1枚捨てるまで遊べない
///  ・deck: 山札に残っているカード（引いたカードは山札に戻らない。捨てたカード・ポーカーで使ったカードも戻らない）
///  ・day: 来店で最後に引いた営業日 "yyyy-MM-dd"
/// </summary>
public sealed class CardState
{
    public string Month = "";
    public List<int> Hand = new List<int>();
    public List<int> Deck = new List<int>();
    public string Day = "";

    public static CardState From(object raw)
    {
        var s = new CardState();
        if (!(raw is Dictionary<string, object> m)) return s;
        s.Month = Str(m, "month");
        s.Day = Str(m, "day");
        s.Hand = Ints(m, "hand");
        s.Deck = Ints(m, "deck");
        return s;
    }

    public Dictionary<string, object> ToMap() => new Dictionary<string, object>
    {
        { "month", Month ?? "" },
        { "hand", Hand.Select(x => (object)(long)x).ToList() },
        { "deck", Deck.Select(x => (object)(long)x).ToList() },
        { "day", Day ?? "" },
    };

    public CardState Clone() => new CardState
    {
        Month = Month, Day = Day, Hand = new List<int>(Hand), Deck = new List<int>(Deck),
    };

    private static string Str(Dictionary<string, object> m, string k) =>
        m.TryGetValue(k, out var v) && v != null ? v.ToString() : "";

    private static List<int> Ints(Dictionary<string, object> m, string k)
    {
        var list = new List<int>();
        if (!m.TryGetValue(k, out var v) || !(v is IEnumerable<object> items)) return list;
        foreach (var o in items)
        {
            try
            {
                int n = Convert.ToInt32(o, CultureInfo.InvariantCulture);
                if (CardRules.Valid(n) && !list.Contains(n)) list.Add(n);
            }
            catch
            {
                // 数でないものは読み飛ばす
            }
        }
        return list;
    }
}

/// <summary>
/// トランプ（2026-11-12 ユーザー指定）の決まり。Unity・Firebase を使わない計算だけ（トランザクションの中からも呼ぶ）。
///  ・カードは 赤・青・黄・黒 × 1〜13 の52枚。番号 = 色×13 + (数字−1)
///  ・山札は全部引き切るまで同じカードは出ない（引いたカードは山札から除く）
///  ・同じ色と数字のカードは2枚持てない（手札にあるカードは引かない）。引けるカードが手札にあるものだけなら、
///    山札をリセットする（リセットした山札には、そのとき手札にあるカードは入れない＝実物のトランプと同じ）
///  ・手札は7枚まで（8枚目を引いたら、好きなカードを1枚捨てる）
///  ・月が変わると、手札も山札もリセット
/// </summary>
public static class CardRules
{
    public const int COLORS = 4, RANKS = 13, DECK_SIZE = COLORS * RANKS, HAND_MAX = 7, POKER_CARDS = 5;

    /// <summary>色の名前（番号の順）</summary>
    public static readonly string[] COLOR_NAMES = { "赤", "青", "黄", "黒" };

    public static int ColorOf(int code) => code / RANKS;
    public static int RankOf(int code) => code % RANKS + 1;
    public static int Code(int color, int rank) => color * RANKS + (rank - 1);
    public static bool Valid(int code) => code >= 0 && code < DECK_SIZE;

    /// <summary>「赤の7」</summary>
    public static string Name(int code) =>
        Valid(code) ? COLOR_NAMES[ColorOf(code)] + "の" + RankOf(code).ToString(CultureInfo.InvariantCulture) : "";

    public static List<int> FullDeck() => Enumerable.Range(0, DECK_SIZE).ToList();

    /// <summary>営業日の月の文字（"yyyy-MM"）</summary>
    public static string MonthKey(DateTime businessDate) => businessDate.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    /// <summary>
    /// 月が変わっていたら（初めてのときも）手札も山札もリセットする。リセットしたら true
    /// </summary>
    public static bool Normalize(CardState s, string month)
    {
        if (s.Month == month) return false;
        s.Month = month;
        s.Hand.Clear();
        s.Deck = FullDeck();
        s.Day = "";
        return true;
    }

    /// <summary>
    /// 山札から1枚引いて手札に入れる。手札にあるカードは引かない。引けるカードがなければ山札をリセットしてから引く。
    /// 引いたカードの番号を返す（手札が上限を超えたかは Overflow で見る）
    /// </summary>
    public static int Draw(CardState s, Random rng)
    {
        var candidates = s.Deck.Where(c => !s.Hand.Contains(c)).ToList();
        if (candidates.Count == 0)
        {
            // 山札を引き切った、または残りが手札にあるカードだけ: 手札にないカードで山札を作り直す
            s.Deck = FullDeck().Where(c => !s.Hand.Contains(c)).ToList();
            candidates = new List<int>(s.Deck);
        }
        if (candidates.Count == 0) return -1; // 手札は8枚までなので起きない
        int card = candidates[rng.Next(candidates.Count)];
        s.Deck.Remove(card);
        s.Hand.Add(card);
        return card;
    }

    /// <summary>手札が上限を超えている（1枚捨てるまで、引くこともポーカーもできない）</summary>
    public static bool Overflow(CardState s) => s.Hand.Count > HAND_MAX;

    /// <summary>手札から1枚捨てる（山札には戻らない）。捨てられたら true</summary>
    public static bool Discard(CardState s, int code) => s.Hand.Remove(code);

    // ================================================================
    // ポーカー
    // ================================================================
    /// <summary>5枚の役（1 はエースとして 10-11-12-13-1 と 1-2-3-4-5 の両方のストレートに使える）</summary>
    public static PokerHand Evaluate(IList<int> five)
    {
        if (five == null || five.Count != POKER_CARDS) return PokerHand.HighCard;
        var ranks = five.Select(RankOf).ToList();
        bool flush = five.Select(ColorOf).Distinct().Count() == 1;
        var counts = ranks.GroupBy(r => r).Select(g => g.Count()).OrderByDescending(n => n).ToList();
        var distinct = ranks.Distinct().OrderBy(r => r).ToList();
        bool straight = false, royal = false;
        if (distinct.Count == 5)
        {
            if (distinct[4] - distinct[0] == 4) straight = true;
            else if (distinct[0] == 1 && distinct[1] == 10 && distinct[2] == 11 && distinct[3] == 12 && distinct[4] == 13)
                straight = royal = true;
        }
        if (straight && flush) return royal ? PokerHand.RoyalFlush : PokerHand.StraightFlush;
        if (counts[0] == 4) return PokerHand.FourOfAKind;
        if (counts[0] == 3 && counts.Count > 1 && counts[1] == 2) return PokerHand.FullHouse;
        if (flush) return PokerHand.Flush;
        if (straight) return PokerHand.Straight;
        if (counts[0] == 3) return PokerHand.ThreeOfAKind;
        if (counts[0] == 2 && counts.Count > 1 && counts[1] == 2) return PokerHand.TwoPair;
        if (counts[0] == 2) return PokerHand.OnePair;
        return PokerHand.HighCard;
    }

    /// <summary>手札（5枚以上）からいちばん強い役になる5枚。5枚に足りなければ null</summary>
    public static List<int> BestFive(IList<int> hand, out PokerHand best)
    {
        best = PokerHand.HighCard;
        if (hand == null || hand.Count < POKER_CARDS) return null;
        List<int> pick = null;
        int n = hand.Count;
        var idx = new int[POKER_CARDS];
        for (int i = 0; i < POKER_CARDS; i++) idx[i] = i;
        while (true)
        {
            var five = idx.Select(k => hand[k]).ToList();
            var h = Evaluate(five);
            if (pick == null || h > best)
            {
                best = h;
                pick = five;
            }
            // 次の組み合わせ（辞書順）
            int p = POKER_CARDS - 1;
            while (p >= 0 && idx[p] == n - POKER_CARDS + p) p--;
            if (p < 0) break;
            idx[p]++;
            for (int q = p + 1; q < POKER_CARDS; q++) idx[q] = idx[q - 1] + 1;
        }
        return pick;
    }

    /// <summary>
    /// 役ごとの報酬（2026-11-12 ユーザー指定「役によって報酬内容が変わる。報酬内容を考えて実装」→ こちらで考えた）。
    /// 弱い役は G（ショップで使える）、ストレートから上はお店のチケット。チケットの格は流れ星の当たりの格に合わせた
    /// （5%OFF ＜ ドリンク・コーヒー ＜ 7%OFF ＜ 5時間無料・フリータイム）
    /// </summary>
    public static readonly PokerReward[] Rewards =
    {
        new PokerReward { Hand = PokerHand.HighCard, Name = "ノーペア", Desc = "役なし", Gold = 1 },
        new PokerReward { Hand = PokerHand.OnePair, Name = "ワンペア", Desc = "同じ数字が2枚", Gold = 2 },
        new PokerReward { Hand = PokerHand.TwoPair, Name = "ツーペア", Desc = "同じ数字2枚の組が2つ", Gold = 4 },
        new PokerReward { Hand = PokerHand.ThreeOfAKind, Name = "スリーカード", Desc = "同じ数字が3枚", Gold = 6 },
        new PokerReward { Hand = PokerHand.Straight, Name = "ストレート", Desc = "数字が5つ続く（10・11・12・13・1 も可）", Coupon = "five_coupon" },
        new PokerReward { Hand = PokerHand.Flush, Name = "フラッシュ", Desc = "5枚とも同じ色", Coupon = "drink_coupon" },
        new PokerReward { Hand = PokerHand.FullHouse, Name = "フルハウス", Desc = "同じ数字3枚 ＋ 同じ数字2枚", Coupon = "coffee_coupon", Gold = 5 },
        new PokerReward { Hand = PokerHand.FourOfAKind, Name = "フォーカード", Desc = "同じ数字が4枚", Coupon = "seven_coupon", Gold = 10 },
        new PokerReward { Hand = PokerHand.StraightFlush, Name = "ストレートフラッシュ", Desc = "同じ色で数字が5つ続く", Coupon = "fivehour_coupon" },
        new PokerReward { Hand = PokerHand.RoyalFlush, Name = "ロイヤルストレートフラッシュ", Desc = "同じ色の 10・11・12・13・1", Coupon = "freetime_coupon" },
    };

    public static PokerReward RewardOf(PokerHand hand) => Rewards[(int)hand];
}
