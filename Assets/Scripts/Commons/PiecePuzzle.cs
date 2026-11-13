using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

/// <summary>ピースの形1つ分（2026-11-13 ユーザー指定の8種類。番号はユーザーの (1)〜(8) のまま）</summary>
public sealed class PieceDef
{
    public int Id;
    public string Name;           // 「L字」
    public (int X, int Y)[] Cells; // 形（左上が 0,0。y は下へ）
    public bool Rare;             // ランダム（3G）でしか出ない
    public int Size => Cells.Length;
}

/// <summary>ボーナスマス1つ分（マスの番号と、まわり8マスを囲むともらえる物）</summary>
public sealed class PuzzleBonus
{
    public int Cell;      // マスの番号（y × 7 + x）。盤のふちには置かない（まわり8マスがいつも盤の中）
    public string Reward; // もらえる物（users のフィールド名。PuzzleRules.REWARD_*）
    public int Count;     // 数（G なら金額）
}

/// <summary>
/// ピースパズルの盤面（users/{uid}.puzzle = { month, board, types, bonus, claimed }）。2026-11-13。
///  ・month: 営業日の月 "yyyy-MM"。月が変わると盤面もボーナスマスもリセット（PuzzleRules.Normalize）
///  ・board: 63マス（横7×縦9。番号 = y × 7 + x）。0 = 空き、k = k番目に置いたピース
///  ・types: k番目に置いたピースの種類（1〜8）
///  ・bonus: ボーナスマス [{ cell, reward, count }]（月の初めに作り、その月のあいだ変わらない）
///  ・claimed: もうもらったボーナスマスの番号
/// 持っている（まだ置いていない）ピースは別のフィールド users.pieces = { "1": 個数, … }（月が変わっても残る）
/// </summary>
public sealed class PuzzleState
{
    public string Month = "";
    public int[] Board = new int[PuzzleRules.CELLS];
    public List<int> Types = new List<int>();
    public List<PuzzleBonus> Bonus = new List<PuzzleBonus>();
    public List<int> Claimed = new List<int>();

    public static PuzzleState From(object raw)
    {
        var s = new PuzzleState();
        if (!(raw is Dictionary<string, object> m)) return s;
        s.Month = m.TryGetValue("month", out var mo) && mo != null ? mo.ToString() : "";
        var board = Ints(m, "board");
        for (int i = 0; i < PuzzleRules.CELLS && i < board.Count; i++) s.Board[i] = Math.Max(0, board[i]);
        s.Types = Ints(m, "types");
        // 種類の記録がない置き場（壊れたデータ）は空きにする
        for (int i = 0; i < PuzzleRules.CELLS; i++)
            if (s.Board[i] > s.Types.Count) s.Board[i] = 0;
        if (m.TryGetValue("bonus", out var bonusRaw) && bonusRaw is IEnumerable<object> items)
            foreach (var o in items)
            {
                if (!(o is Dictionary<string, object> b)) continue;
                int cell = Int(b, "cell", -1);
                string reward = b.TryGetValue("reward", out var r) ? r as string : null;
                int count = Int(b, "count", 0);
                if (cell < 0 || cell >= PuzzleRules.CELLS || string.IsNullOrEmpty(reward) || count <= 0) continue;
                s.Bonus.Add(new PuzzleBonus { Cell = cell, Reward = reward, Count = count });
            }
        s.Claimed = Ints(m, "claimed").Distinct().ToList();
        return s;
    }

    public Dictionary<string, object> ToMap() => new Dictionary<string, object>
    {
        { "month", Month ?? "" },
        { "board", Board.Select(x => (object)(long)x).ToList() },
        { "types", Types.Select(x => (object)(long)x).ToList() },
        {
            "bonus", Bonus.Select(b => (object)new Dictionary<string, object>
            {
                { "cell", (long)b.Cell }, { "reward", b.Reward ?? "" }, { "count", (long)b.Count },
            }).ToList()
        },
        { "claimed", Claimed.Select(x => (object)(long)x).ToList() },
    };

    public PuzzleState Clone() => new PuzzleState
    {
        Month = Month,
        Board = (int[])Board.Clone(),
        Types = new List<int>(Types),
        Bonus = Bonus.Select(b => new PuzzleBonus { Cell = b.Cell, Reward = b.Reward, Count = b.Count }).ToList(),
        Claimed = new List<int>(Claimed),
    };

    private static int Int(Dictionary<string, object> m, string k, int fallback)
    {
        if (!m.TryGetValue(k, out var v) || v == null) return fallback;
        try
        {
            return Convert.ToInt32(v, CultureInfo.InvariantCulture);
        }
        catch
        {
            return fallback;
        }
    }

    private static List<int> Ints(Dictionary<string, object> m, string k)
    {
        var list = new List<int>();
        if (!m.TryGetValue(k, out var v) || !(v is IEnumerable<object> items)) return list;
        foreach (var o in items)
        {
            try
            {
                list.Add(Convert.ToInt32(o, CultureInfo.InvariantCulture));
            }
            catch
            {
                list.Add(0);
            }
        }
        return list;
    }
}

/// <summary>
/// ピースパズルの決まり（2026-11-13 ユーザー指定「ピース座がアクティブになったら、横7マス・縦9マスの方眼に、テトリスのような
/// ピースを当てはめてパズルができるように。オーディンの祝祭のように、マスのどこかにあるボーナスマス（5%OFFチケットなどの
/// アイテムの絵）のまわり8マスをピースで囲むとその報酬がもらえる。1か月に1回リセット。配置も毎回変わる」）。
/// Unity・Firebase を使わない計算だけ（トランザクションの中からも呼ぶ）。
///  ・ピースは回してよく、裏返してもよい（L字だけ向きが2通り増える）。盤からはみ出す・ほかのピースやボーナスマスに重ねるのはだめ。
///    置いたピースは動かせない
///  ・ボーナスマスは盤のふちを除いた所に BONUS_COUNT 個。互いのまわり8マスには入らない（どれも囲める）。
///    並びともらえる物は月ごとに決まる（その月はみんな同じ盤面。Generate）
///  ・ピースはショップで、ランダムに1つ 3G・選んで 7G（U字はランダムでしか出ない）
/// </summary>
public static class PuzzleRules
{
    public const int W = 7, H = 9, CELLS = W * H;
    /// <summary>1つの盤面のボーナスマスの数（こちらで決めた）</summary>
    public const int BONUS_COUNT = 5;
    public const int TYPES = 8;
    /// <summary>ランダムでしか出ないレアのピース（U字）</summary>
    public const int RARE = 8;
    /// <summary>ランダムでレアのピースが出る割合（%。こちらで決めた。ほかの7種類は同じ割合）</summary>
    public const int RARE_PERCENT = 5;
    /// <summary>ショップの値段（G。ユーザー指定）</summary>
    public const int RANDOM_PRICE = 3, CHOOSE_PRICE = 7;

    // ボーナスマスでもらえる物（users のフィールド名。こちらで決めた）。毎月この5つ（コマの色は月ごとに変わる）
    public const string REWARD_DRINK = "drink_coupon";
    public const string REWARD_FIVE = "five_coupon";
    public const string REWARD_GOLD = "ig";
    public const string REWARD_POTION = "planet_potion";
    public static readonly string[] REWARD_MEEPLES = { "red_meeple", "blue_meeple", "yellow_meeple" };
    public const int GOLD_REWARD = 5;

    public static readonly PieceDef[] Pieces =
    {
        new PieceDef { Id = 1, Name = "2マス", Cells = new[] { (0, 0), (1, 0) } },
        new PieceDef { Id = 2, Name = "かぎ", Cells = new[] { (0, 0), (1, 0), (0, 1) } },
        new PieceDef { Id = 3, Name = "L字", Cells = new[] { (2, 0), (0, 1), (1, 1), (2, 1) } },
        new PieceDef { Id = 4, Name = "四角", Cells = new[] { (0, 0), (1, 0), (0, 1), (1, 1) } },
        new PieceDef { Id = 5, Name = "T字", Cells = new[] { (1, 0), (0, 1), (1, 1), (2, 1) } },
        new PieceDef { Id = 6, Name = "棒", Cells = new[] { (0, 0), (1, 0), (2, 0), (3, 0) } },
        new PieceDef { Id = 7, Name = "1マス", Cells = new[] { (0, 0) } },
        new PieceDef { Id = 8, Name = "U字", Cells = new[] { (0, 0), (2, 0), (0, 1), (1, 1), (2, 1) }, Rare = true },
    };

    /// <summary>番号（1〜8）のピース。なければ null</summary>
    public static PieceDef Piece(int id) => id >= 1 && id <= Pieces.Length ? Pieces[id - 1] : null;

    public static int Index(int x, int y) => y * W + x;
    public static int XOf(int cell) => cell % W;
    public static int YOf(int cell) => cell / W;

    // ================================================================
    // 形
    // ================================================================
    /// <summary>
    /// 向きを変えた形（rot = 時計回りに90°ずつ 0〜3、flip = 左右に裏返してから回す）。左上が 0,0 になるようにそろえ、並べ直して返す
    /// </summary>
    public static List<(int X, int Y)> Orient(PieceDef def, int rot, bool flip)
    {
        var list = new List<(int X, int Y)>();
        if (def == null) return list;
        foreach (var (cx, cy) in def.Cells)
        {
            int x = flip ? -cx : cx, y = cy;
            for (int i = 0; i < ((rot % 4) + 4) % 4; i++) (x, y) = (-y, x); // 時計回り（y が下向きの座標）
            list.Add((x, y));
        }
        int minX = list.Min(c => c.X), minY = list.Min(c => c.Y);
        return list.Select(c => (c.X - minX, c.Y - minY)).OrderBy(c => c.Item2).ThenBy(c => c.Item1).ToList();
    }

    /// <summary>形の横と縦のマス数</summary>
    public static (int W, int H) SizeOf(List<(int X, int Y)> shape) =>
        shape == null || shape.Count == 0 ? (0, 0) : (shape.Max(c => c.X) + 1, shape.Max(c => c.Y) + 1);

    /// <summary>形の左上を (ox, oy) に置いたときのマスの番号。盤からはみ出すなら null</summary>
    public static List<int> CellsAt(List<(int X, int Y)> shape, int ox, int oy)
    {
        var cells = new List<int>();
        foreach (var (cx, cy) in shape)
        {
            int x = ox + cx, y = oy + cy;
            if (x < 0 || x >= W || y < 0 || y >= H) return null;
            cells.Add(Index(x, y));
        }
        return cells;
    }

    /// <summary>マスの集まりが、その種類のピースのどれかの向き・どこかの位置の形と同じか</summary>
    public static bool MatchesShape(int type, IList<int> cells)
    {
        var def = Piece(type);
        if (def == null || cells == null || cells.Count != def.Size || cells.Distinct().Count() != cells.Count) return false;
        if (cells.Any(c => c < 0 || c >= CELLS)) return false;
        int minX = cells.Min(XOf), minY = cells.Min(YOf);
        var given = cells.Select(c => (XOf(c) - minX, YOf(c) - minY)).OrderBy(c => c.Item2).ThenBy(c => c.Item1).ToList();
        for (int f = 0; f < 2; f++)
            for (int r = 0; r < 4; r++)
                if (Orient(def, r, f == 1).SequenceEqual(given)) return true;
        return false;
    }

    // ================================================================
    // 盤面
    // ================================================================
    public static bool IsBonusCell(PuzzleState s, int cell) => s.Bonus.Any(b => b.Cell == cell);

    /// <summary>そこにピースを置けるか（盤の中・空き・ボーナスマスでない）</summary>
    public static bool CanPlace(PuzzleState s, IList<int> cells)
    {
        if (cells == null || cells.Count == 0) return false;
        foreach (int c in cells)
            if (c < 0 || c >= CELLS || s.Board[c] != 0 || IsBonusCell(s, c)) return false;
        return true;
    }

    /// <summary>まわり8マス（盤の中だけ）</summary>
    public static List<int> Ring(int cell)
    {
        var list = new List<int>(8);
        int x0 = XOf(cell), y0 = YOf(cell);
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                int x = x0 + dx, y = y0 + dy;
                if (x >= 0 && x < W && y >= 0 && y < H) list.Add(Index(x, y));
            }
        return list;
    }

    /// <summary>まわり8マスのうち、ピースが置かれた数</summary>
    public static int Covered(PuzzleState s, PuzzleBonus b) => Ring(b.Cell).Count(c => s.Board[c] != 0);

    /// <summary>まわり8マスが全部ピースで埋まっている</summary>
    public static bool Surrounded(PuzzleState s, PuzzleBonus b) => Ring(b.Cell).All(c => s.Board[c] != 0);

    /// <summary>
    /// ピースを置く（置けるかは呼ぶ前に CanPlace で）。置いたことで新しく囲まれたボーナスマスを claimed に足して返す
    /// </summary>
    public static List<PuzzleBonus> Place(PuzzleState s, int type, IList<int> cells)
    {
        s.Types.Add(type);
        int k = s.Types.Count;
        foreach (int c in cells) s.Board[c] = k;
        var got = new List<PuzzleBonus>();
        foreach (var b in s.Bonus)
        {
            if (s.Claimed.Contains(b.Cell) || !Surrounded(s, b)) continue;
            s.Claimed.Add(b.Cell);
            got.Add(b);
        }
        return got;
    }

    /// <summary>月が変わっていたら（初めてのときも）盤面とボーナスマスを新しくする。新しくしたら true</summary>
    public static bool Normalize(PuzzleState s, string month)
    {
        if (s.Month == month && s.Bonus.Count > 0) return false;
        s.Month = month;
        s.Board = new int[CELLS];
        s.Types = new List<int>();
        s.Bonus = Generate(month);
        s.Claimed = new List<int>();
        return true;
    }

    /// <summary>
    /// その月のボーナスマス（月の文字から決まる。その月はみんな同じ盤面）。盤のふちを除いた所に BONUS_COUNT 個、
    /// 互いのまわり8マスに入らないように置く（2つ空けた所はまわりのマスを分け合える）。もらえる物は
    /// ドリンクチケット・5%OFFチケット・5G・惑星回復薬・コマ1個（色は月ごと）を、どのマスにするか混ぜる
    /// </summary>
    public static List<PuzzleBonus> Generate(string month)
    {
        var rng = new Rng(Hash("intales/piece-puzzle/" + (month ?? "")));
        var candidates = new List<int>();
        for (int y = 1; y < H - 1; y++)
            for (int x = 1; x < W - 1; x++)
                candidates.Add(Index(x, y));
        var picked = new List<int>();
        for (int attempt = 0; attempt < 100 && picked.Count < BONUS_COUNT; attempt++)
        {
            picked.Clear();
            Shuffle(candidates, rng);
            foreach (int c in candidates)
            {
                if (picked.Any(p => Math.Max(Math.Abs(XOf(p) - XOf(c)), Math.Abs(YOf(p) - YOf(c))) < 2)) continue;
                picked.Add(c);
                if (picked.Count == BONUS_COUNT) break;
            }
        }
        var rewards = new List<(string Reward, int Count)>
        {
            (REWARD_DRINK, 1), (REWARD_FIVE, 1), (REWARD_GOLD, GOLD_REWARD), (REWARD_POTION, 1),
            (REWARD_MEEPLES[rng.Next(REWARD_MEEPLES.Length)], 1),
        };
        Shuffle(rewards, rng);
        var list = new List<PuzzleBonus>();
        for (int i = 0; i < picked.Count && i < rewards.Count; i++)
            list.Add(new PuzzleBonus { Cell = picked[i], Reward = rewards[i].Reward, Count = rewards[i].Count });
        return list.OrderBy(b => b.Cell).ToList();
    }

    /// <summary>ショップのランダム（3G）で出るピース。レア（U字）は RARE_PERCENT %、ほかの7種類は同じ割合</summary>
    public static int RollPiece(Random rng)
    {
        if (rng.Next(100) < RARE_PERCENT) return RARE;
        return 1 + rng.Next(TYPES - 1);
    }

    // ================================================================
    // 持っているピース（users.pieces = { "1": 個数, … }）
    // ================================================================
    /// <summary>持っているピースの数（添字 1〜8。0 は使わない）</summary>
    public static int[] PiecesFrom(object raw)
    {
        var owned = new int[TYPES + 1];
        if (!(raw is Dictionary<string, object> m)) return owned;
        foreach (var kv in m)
        {
            if (!int.TryParse(kv.Key, NumberStyles.None, CultureInfo.InvariantCulture, out int type) || type < 1 || type > TYPES)
                continue;
            try
            {
                owned[type] = Math.Max(0, Convert.ToInt32(kv.Value, CultureInfo.InvariantCulture));
            }
            catch
            {
                owned[type] = 0;
            }
        }
        return owned;
    }

    public static Dictionary<string, object> PiecesToMap(int[] owned)
    {
        var m = new Dictionary<string, object>();
        for (int t = 1; t <= TYPES; t++) m[t.ToString(CultureInfo.InvariantCulture)] = (long)Math.Max(0, owned[t]);
        return m;
    }

    // ================================================================
    // 乱数（月の文字から同じ並びを作るため、どの端末でも同じ結果になる自前の乱数）
    // ================================================================
    private static uint Hash(string text)
    {
        uint h = 2166136261u; // FNV-1a
        foreach (char ch in text)
        {
            h ^= ch;
            h *= 16777619u;
        }
        return h;
    }

    private sealed class Rng
    {
        private uint _s;
        public Rng(uint seed) => _s = seed == 0 ? 0x9E3779B9u : seed;

        /// <summary>0 以上 n 未満（xorshift32）</summary>
        public int Next(int n)
        {
            _s ^= _s << 13;
            _s ^= _s >> 17;
            _s ^= _s << 5;
            return (int)(_s % (uint)Math.Max(1, n));
        }
    }

    private static void Shuffle<T>(IList<T> list, Rng rng)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
