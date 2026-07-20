using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>バフカードの種類。</summary>
public enum BuffCardType
{
    SoftDrink,   // ソフトドリンク
    Beer,        // ビール
    OtherDrink,  // アザードリンク
    SideDish,    // サイドディッシュ
    StapleFood,  // メインフード
    Sweets,      // スイーツ
    SP1,         // スペシャルカード1
    SP2,         // スペシャルカード2
}

/// <summary>
/// バフカードのメタ情報・山札(QR)エンコード・抽選・手札(端末保持)を扱うユーティリティ。
///
/// 流れ:
///   管理者が山札（各カードの枚数）を決めて QR を生成（"deck:soft=10,beer=5,..."）。
///   客がQRを読むと、枚数を重みにして1枚だけ抽選し手札として端末に保持（1枚のみ）。
///   次のボス戦開始時に自動で発動して消費する（戦闘1回限り）。
/// </summary>
public static class BuffCard
{
    /// <summary>山札QRのプレフィックス。形式: "deck:soft=10,beer=5,sweets=1,..."</summary>
    public const string QR_PREFIX = "deck:";

    private const string PrefHeld = "buffcard_held"; // 手札（codeまたは空）

    private class Meta { public string Name; public string Code; public string Image; public string Effect; public int Rank; }

    // Rank … カードの強さランク（0が最弱）。スキルの強化・比較判定などに使う
    private static readonly Dictionary<BuffCardType, Meta> META = new Dictionary<BuffCardType, Meta>
    {
        { BuffCardType.SoftDrink,  new Meta{ Name="ソフトドリンク",   Code="soft",   Image="Cards/Soft Drink",  Effect="1D8・2D6・2D8 の成功確率＋5",                        Rank=0 } },
        { BuffCardType.Beer,       new Meta{ Name="ビール",           Code="beer",   Image="Cards/Beer",        Effect="1D10を振り、出目ぶん全ダイスの成功確率＋",           Rank=3 } },
        { BuffCardType.OtherDrink, new Meta{ Name="アザードリンク",   Code="other",  Image="Cards/Other Drink", Effect="1D6を振り、出目ぶん全ダイスの成功確率＋／攻撃力＋1", Rank=5 } },
        { BuffCardType.SideDish,   new Meta{ Name="サイドディッシュ", Code="side",   Image="Cards/Side Dish",   Effect="全ダイスの成功確率＋5",                              Rank=1 } },
        { BuffCardType.StapleFood, new Meta{ Name="メインフード",     Code="main",   Image="Cards/Staple Food", Effect="1D100に成功したとき攻撃力＋1",                       Rank=4 } },
        { BuffCardType.Sweets,     new Meta{ Name="スイーツ",         Code="sweets", Image="Cards/Sweets",      Effect="自分を含む全員の成功確率＋5",                        Rank=2 } },
        { BuffCardType.SP1,        new Meta{ Name="スペシャルカード1", Code="sp1",    Image="Cards/SP1",         Effect="1D10を振り、出目ぶん全ダイスの成功確率＋／攻撃力＋1", Rank=6 } },
        { BuffCardType.SP2,        new Meta{ Name="スペシャルカード2", Code="sp2",    Image="Cards/SP2",         Effect="1D10を振り、出目ぶん全ダイスの成功確率＋／攻撃力＋1", Rank=6 } },
    };

    public static IEnumerable<BuffCardType> AllTypes => META.Keys;

    public static string DisplayName(BuffCardType t) => META[t].Name;
    public static string Code(BuffCardType t)        => META[t].Code;
    public static string EffectText(BuffCardType t)  => META[t].Effect;

    /// <summary>カードの強さランク（0=最弱〜6=SP）。</summary>
    public static int Rank(BuffCardType t)           => META[t].Rank;

    /// <summary>
    /// 1ランク上のカードを返す（同ランクに複数いる場合はランダム。例: ランク5→SP1/SP2のどちらか）。
    /// これ以上ランクが無ければ null。
    /// </summary>
    public static BuffCardType? UpgradeTarget(BuffCardType t)
    {
        int next = Rank(t) + 1;
        var ups = META.Keys.Where(x => Rank(x) == next).ToList();
        if (ups.Count == 0) return null;
        return ups[Random.Range(0, ups.Count)];
    }

    /// <summary>
    /// 山札の指定カード1枚を1ランク上のカードへ強化する（オーディンの術書(A)のユニークスキル「アップグレード」用）。
    /// counts を直接書き換え、強化後のカードを返す。強化できなければ何もしない（null）。
    /// </summary>
    public static BuffCardType? UpgradeOneCard(Dictionary<BuffCardType, int> counts, BuffCardType from)
    {
        if (!counts.TryGetValue(from, out int n) || n <= 0) return null;
        var to = UpgradeTarget(from);
        if (to == null) return null;

        counts[from]--;
        if (counts[from] <= 0) counts.Remove(from);
        counts[to.Value] = counts.TryGetValue(to.Value, out int m) ? m + 1 : 1;
        return to;
    }

    /// <summary>カード画像（Resources/Cards 配下）を読み込む（なければ null）。</summary>
    public static Texture2D LoadImage(BuffCardType t) => Resources.Load<Texture2D>(META[t].Image);

    private static bool TryFromCode(string code, out BuffCardType type)
    {
        foreach (var kv in META)
            if (kv.Value.Code == code) { type = kv.Key; return true; }
        type = BuffCardType.SoftDrink;
        return false;
    }

    // ============================================================
    // 山札（QR）エンコード / デコード
    // ============================================================

    /// <summary>枚数マップを "deck:soft=10,beer=5,..." 形式にエンコード（0枚は省略）。</summary>
    public static string EncodeDeck(Dictionary<BuffCardType, int> counts)
    {
        var parts = new List<string>();
        foreach (var t in META.Keys)
            if (counts.TryGetValue(t, out int n) && n > 0)
                parts.Add($"{META[t].Code}={n}");
        return QR_PREFIX + string.Join(",", parts);
    }

    /// <summary>"deck:soft=10,beer=5,..." をデコードして枚数マップにする。</summary>
    public static Dictionary<BuffCardType, int> DecodeDeck(string qr)
    {
        var counts = new Dictionary<BuffCardType, int>();
        if (string.IsNullOrEmpty(qr)) return counts;
        string body = qr.StartsWith(QR_PREFIX) ? qr.Substring(QR_PREFIX.Length) : qr;
        foreach (var token in body.Split(','))
        {
            var kv = token.Split('=');
            if (kv.Length == 2 && TryFromCode(kv[0].Trim(), out var t)
                && int.TryParse(kv[1].Trim(), out int n) && n > 0)
                counts[t] = counts.TryGetValue(t, out int e) ? e + n : n;
        }
        return counts;
    }

    /// <summary>枚数を重みにして山札から1枚抽選する（空なら null）。</summary>
    public static BuffCardType? DrawFrom(Dictionary<BuffCardType, int> counts)
    {
        int total = counts.Values.Sum();
        if (total <= 0) return null;
        int r = Random.Range(0, total);
        foreach (var kv in counts)
        {
            r -= kv.Value;
            if (r < 0) return kv.Key;
        }
        return null;
    }

    /// <summary>
    /// 山札の1枚をランダムな別の種類のカードへ変化させる（塔覇の本(B)のユニークスキル「カード変化」用）。
    /// SP1/SP2 はスペシャルカードのため変化に関与しない（変化元にも変化先にもならない）。
    /// 変化元はSPを除いた山札から枚数を重みに1枚選び（＝山札から物理的に1枚引く相当）、
    /// 変化先は元と異なる通常カードから等確率で選ぶ。山札の合計枚数は変わらない。
    /// counts を直接書き換え、(変化元, 変化先) を返す。変化できるカードが無ければ何もしない（null）。
    /// </summary>
    public static (BuffCardType from, BuffCardType to)? TransmuteRandomCard(Dictionary<BuffCardType, int> counts)
    {
        // SPカードは変化元から守る（SPを除いた山札で抽選する）
        var normals = counts
            .Where(kv => kv.Key != BuffCardType.SP1 && kv.Key != BuffCardType.SP2)
            .ToDictionary(kv => kv.Key, kv => kv.Value);
        var from = DrawFrom(normals);
        if (from == null) return null;

        var candidates = META.Keys
            .Where(t => t != from.Value && t != BuffCardType.SP1 && t != BuffCardType.SP2)
            .ToList();
        var to = candidates[Random.Range(0, candidates.Count)];

        counts[from.Value]--;
        if (counts[from.Value] <= 0) counts.Remove(from.Value);
        counts[to] = counts.TryGetValue(to, out int n) ? n + 1 : 1;
        return (from.Value, to);
    }

    // ============================================================
    // 手札（端末ローカル・1枚のみ保持）
    // ============================================================

    public static bool HasHeld => !string.IsNullOrEmpty(PlayerPrefs.GetString(PrefHeld, ""));

    public static BuffCardType? Held
    {
        get
        {
            string code = PlayerPrefs.GetString(PrefHeld, "");
            return TryFromCode(code, out var t) ? t : (BuffCardType?)null;
        }
    }

    public static void SetHeld(BuffCardType t)
    {
        PlayerPrefs.SetString(PrefHeld, META[t].Code);
        PlayerPrefs.Save();
    }

    public static void ClearHeld()
    {
        PlayerPrefs.DeleteKey(PrefHeld);
        PlayerPrefs.Save();
    }
}
