using System.Collections.Generic;
using System.Text;

/// <summary>惑星を解放する条件の種類</summary>
public enum UnlockKind
{
    Start,     // はじめから持っている（もとはルミナ。2026-11-10 に最初の惑星を選ぶしくみにして、今はどの惑星も使っていない）
    Visits,    // 来店回数（Param 回目）
    Weekday,   // その曜日に来店（Param = System.DayOfWeek）
    Streak,    // 公転を Param 回続ける
    Season,    // その季節に来店（Param = StoreCalendar.Season）
    FullMoon,  // 満月のころに来店
    LongStay,  // 1回で Param 分以上の滞在
    Together,  // 友だち紹介で、紹介した友だち・紹介された自分が初めて来店した（2026-11-09 フレンドと一緒の来店から変更）
    Recruit,   // 相席募集に参加して遊ぶ
    Star,      // 流れ星でまれに出る
    Legacy,    // 以前のアプリでレベルを上げてきた人への贈りもの（今は手に入らない）
}

/// <summary>★お店の特典を使える条件</summary>
public enum PerkRule
{
    Any,      // いつでも（入店中）
    Thursday, // 木曜の営業日
    Morning,  // 開店から1時間以内（12〜13時）に入店した
    Evening,  // 18時以降に入店した
    Summer,   // 夏（6〜8月）
}

/// <summary>惑星ひとつ分の解放の条件と能力</summary>
public sealed class PlanetPower
{
    public string Id;
    public UnlockKind Unlock;
    public int Param;
    public string UnlockText;  // 解放の条件（惑星を選ぶ画面などに出す）
    public string AbilityText; // 能力
    /// <summary>★お店の特典（1公転に1回まで。お会計で画面を見せて使う）。false なら☆アプリの能力か、能力なし</summary>
    public bool Store;
    /// <summary>★だがお金はかからない（ヘクシア）</summary>
    public bool NoCost;
    public PerkRule Rule;
    /// <summary>★の使用画面に大きく出す短い名前</summary>
    public string PerkName;
    public bool HasAbility => !string.IsNullOrEmpty(AbilityText) && Unlock != UnlockKind.Start;
}

/// <summary>
/// 惑星の能力は 2026-11-09 にすべてやめた（ユーザー指定「惑星は見た目を変える機能とパワーを持つ機能だけ」）。
/// 残っていたパワー（JourneyRules）も 2026-11-10 にやめたので、惑星は見た目と解放の条件だけ（ユーザー指定「お客様に説明が難しい」）。
/// いまは解放の条件だけを持つ。能力・お店の特典の欄（AbilityText・Store など）は残してあるが、どの惑星にも入れていない。
/// 以下はもとの説明:
/// 24個の惑星の解放の条件と能力（2026-11 企画書「惑星と来店のしくみ 案1」の表。定休日が月・火・水になったので、
/// 月・火・水の惑星（ルナリス・イグニス・アクアリス）は別の条件にした）。
/// 能力が効くのは、ホームの中央に置いた惑星1つだけ。★お店の特典は1公転（2週間）に1回まで（全部の惑星で合わせて1回）。
/// 判定と書き込みは JourneyService が行う。
/// </summary>
public static class PlanetPowers
{
    public const string T_STORE = "お店の特典";
    public const string T_APP = "アプリの能力";
    public const string T_NONE = "能力なし";

    public static readonly PlanetPower[] All =
    {
        // ---- 最初に選べる3つ（2026-11-10 ユーザー指定。選ばなかったものは7回目の来店。以前からの会員はルミナを持っている） ----
        new PlanetPower { Id = "rubra", Unlock = UnlockKind.Visits, Param = 7, UnlockText = "最初に選ぶ（赤）か、7回目の来店" },
        new PlanetPower { Id = "lumina", Unlock = UnlockKind.Visits, Param = 7, UnlockText = "最初に選ぶ（青）か、7回目の来店" },
        new PlanetPower { Id = "limona", Unlock = UnlockKind.Visits, Param = 7, UnlockText = "最初に選ぶ（黄）か、7回目の来店" },

        // ---- 来店回数 ----
        new PlanetPower { Id = "aquaris", Unlock = UnlockKind.Visits, Param = 2, UnlockText = "2回目の来店" },
        new PlanetPower { Id = "vespera", Unlock = UnlockKind.Visits, Param = 5, UnlockText = "5回目の来店" },
        new PlanetPower { Id = "mocha", Unlock = UnlockKind.Visits, Param = 10, UnlockText = "10回目の来店" },
        new PlanetPower { Id = "ambaria", Unlock = UnlockKind.Visits, Param = 15, UnlockText = "15回目の来店" },
        new PlanetPower { Id = "voltia", Unlock = UnlockKind.Visits, Param = 25, UnlockText = "25回目の来店" },
        new PlanetPower { Id = "noctis", Unlock = UnlockKind.Visits, Param = 50, UnlockText = "50回目の来店" },

        // ---- 曜日（営業日の木〜日） ----
        new PlanetPower { Id = "verdia", Unlock = UnlockKind.Weekday, Param = (int)System.DayOfWeek.Thursday,
            UnlockText = "木曜に来店" },
        new PlanetPower { Id = "crysta", Unlock = UnlockKind.Weekday, Param = (int)System.DayOfWeek.Friday,
            UnlockText = "金曜に来店" },
        new PlanetPower { Id = "orbis", Unlock = UnlockKind.Weekday, Param = (int)System.DayOfWeek.Saturday,
            UnlockText = "土曜に来店" },
        new PlanetPower { Id = "soleil", Unlock = UnlockKind.Weekday, Param = (int)System.DayOfWeek.Sunday,
            UnlockText = "日曜に来店" },

        // ---- 定休日（月・火・水）の惑星の代わりの条件 ----
        new PlanetPower { Id = "lunaris", Unlock = UnlockKind.FullMoon, UnlockText = "満月のころ（前後2日）に来店" },
        new PlanetPower { Id = "ignis", Unlock = UnlockKind.LongStay, Param = 5 * 60, UnlockText = "1回で5時間以上滞在する" },

        // ---- 公転 ----
        new PlanetPower { Id = "mistia", Unlock = UnlockKind.Streak, Param = 2, UnlockText = "2公転（約1か月）続ける" },
        new PlanetPower { Id = "tempesta", Unlock = UnlockKind.Streak, Param = 4, UnlockText = "4公転（約2か月）続ける" },
        new PlanetPower { Id = "celesta", Unlock = UnlockKind.Streak, Param = 6, UnlockText = "6公転（約3か月）続ける" },
        new PlanetPower { Id = "lanterna", Unlock = UnlockKind.Streak, Param = 9, UnlockText = "9公転（約4か月）続ける" },
        new PlanetPower { Id = "neonia", Unlock = UnlockKind.Streak, Param = 13, UnlockText = "13公転（約半年）続ける" },
        new PlanetPower { Id = "galaxia", Unlock = UnlockKind.Streak, Param = 26, UnlockText = "26公転（約1年）続ける" },

        // ---- 季節 ----
        new PlanetPower { Id = "pastelia", Unlock = UnlockKind.Season, Param = (int)StoreCalendar.Season.Spring,
            UnlockText = "春（3〜5月）に来店" },
        new PlanetPower { Id = "coralia", Unlock = UnlockKind.Season, Param = (int)StoreCalendar.Season.Summer,
            UnlockText = "夏（6〜8月）に来店" },
        new PlanetPower { Id = "folia", Unlock = UnlockKind.Season, Param = (int)StoreCalendar.Season.Autumn,
            UnlockText = "秋（9〜11月）に来店" },
        new PlanetPower { Id = "glacia", Unlock = UnlockKind.Season, Param = (int)StoreCalendar.Season.Winter,
            UnlockText = "冬（12〜2月）に来店" },

        // ---- なかま ----
        new PlanetPower { Id = "opalia", Unlock = UnlockKind.Together,
            UnlockText = "友だち紹介で、紹介した友だち（または紹介された自分）が初めて来店する" },
        new PlanetPower { Id = "hexia", Unlock = UnlockKind.Recruit, UnlockText = "相席募集に参加して遊ぶ" },

        // ---- ひみつ ----
        new PlanetPower { Id = "noir", Unlock = UnlockKind.Star, UnlockText = "流れ星でまれに出る" },

        // ---- 古参の人だけ（2026-11-07） ----
        new PlanetPower { Id = "legacia", Unlock = UnlockKind.Legacy,
            UnlockText = "以前からレベルを上げてきた方への贈りもの" },
    };

    private static Dictionary<string, PlanetPower> _byId;

    public static PlanetPower Of(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        if (_byId == null)
        {
            _byId = new Dictionary<string, PlanetPower>();
            foreach (var p in All) _byId[p.Id] = p;
        }
        return _byId.TryGetValue(id, out var power) ? power : null;
    }

    public static PlanetPower Of(SpacePlanetDef def) => def != null ? Of(def.Id) : null;

    /// <summary>「★ お店の特典」「☆ アプリの能力」（能力がなければ空）</summary>
    public static string KindLabel(PlanetPower p)
    {
        if (p == null || !p.HasAbility) return "";
        return p.Store ? "★ " + T_STORE : "☆ " + T_APP;
    }

    /// <summary>条件・能力の文字（NewHome の起動時にまとめてフォントへ登録する）</summary>
    public static string GlyphText
    {
        get
        {
            var sb = new StringBuilder(T_STORE + T_APP + T_NONE + "★☆");
            foreach (var p in All) sb.Append(p.UnlockText).Append(p.AbilityText).Append(p.PerkName);
            return sb.ToString();
        }
    }
}
