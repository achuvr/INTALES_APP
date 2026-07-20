using System.Collections.Generic;
using System.Linq;

/// <summary>スキルの挙動の種類。</summary>
public enum SkillKind
{
    /// <summary>指定面ダイスを振り、当たり目(WinFaces)なら効果(Effect+Value)を発動する（手動発動・1戦闘1回）。</summary>
    StatBuff,
    /// <summary>戦闘開始時に自動で DiceFaces 面ダイスを振り、当たり目(WinFaces)なら効果(Effect+Value)をその戦闘だけ適用する。</summary>
    BattleStartStatBuff,
    /// <summary>1D100と一緒に指定面ダイスを振り、その出目ぶん 1D100 の出目を引く（低いほど有利）。</summary>
    D100Subtract,
    /// <summary>所持しているアクティブスキル本1冊につき成功確率＋Value（戦闘開始時に自動適用）。</summary>
    ProbPerOwnedSkillBook,
    /// <summary>自分の攻撃力−1と引き換えに、味方全員の成功確率＋Value（手動発動）。</summary>
    CoopProbBuff,
    /// <summary>
    /// クリティカル率を Value〜DiceFaces 倍にする（戦闘開始時に倍率をランダム決定して自動適用・常時。
    /// Value=DiceFaces なら固定倍率）。
    /// </summary>
    CritRateMultiplier,
    /// <summary>オーバーキルダメージが DiceFaces 以上なら討伐報酬GPに＋Value（パッシブ）。</summary>
    OverkillGpBonus,
    /// <summary>オーバーキルダメージ DiceFaces ごとに討伐時のレベルアップを＋1する（パッシブ）。</summary>
    OverkillLevelBonus,
    /// <summary>
    /// 連勝ボーナス（パッシブ）。現在の連勝数 × Value ぶん成功確率を上げ、
    /// さらに DiceFaces 連勝ごとに討伐時のレベルアップを＋1する（DiceFaces=0 ならレベル加算なし）。
    /// </summary>
    WinStreakBonus,
}

/// <summary>
/// スキルブックの「任意発動スキル」1つ分の定義。
/// ボス戦で 1D100 を振る前に任意発動できる。
///  - StatBuff:     指定面ダイスを振り、当たり目が出たら効果(Effect+Value)を発動
///                  例「8面ダイスを振って1,3,6が出たら確率+10」
///  - D100Subtract: 1D100と一緒に指定面ダイスを振り、その出目ぶん 1D100 の出目を引く
///                  例「100面ダイスと一緒に8面ダイスを振り、その出目分を引ける」
/// </summary>
public class BattleSkillDef
{
    public readonly string Id;
    public readonly string Name;
    public readonly string Description;
    public readonly SkillKind Kind;
    public readonly int DiceFaces;          // 振るダイスの面数
    public readonly HashSet<int> WinFaces;  // StatBuff: この出目なら発動成功
    public readonly EffectType Effect;       // StatBuff: 成功時の効果種別
    public readonly int Value;               // StatBuff: 成功時の効果量

    public BattleSkillDef(string id, string name, string description, SkillKind kind,
        int diceFaces, IEnumerable<int> winFaces, EffectType effect, int value)
    {
        Id = id; Name = name; Description = description; Kind = kind;
        DiceFaces = diceFaces; WinFaces = new HashSet<int>(winFaces ?? System.Array.Empty<int>());
        Effect = effect; Value = value;
    }

    /// <summary>StatBuff スキルを作る。</summary>
    public static BattleSkillDef StatBuffSkill(string id, string name, string desc,
        int diceFaces, IEnumerable<int> winFaces, EffectType effect, int value)
        => new BattleSkillDef(id, name, desc, SkillKind.StatBuff, diceFaces, winFaces, effect, value);

    /// <summary>戦闘開始時に自動でダイスを振る StatBuff（当たり目なら効果がその戦闘だけ有効）を作る。</summary>
    public static BattleSkillDef BattleStartStatBuffSkill(string id, string name, string desc,
        int diceFaces, IEnumerable<int> winFaces, EffectType effect, int value)
        => new BattleSkillDef(id, name, desc, SkillKind.BattleStartStatBuff, diceFaces, winFaces, effect, value);

    /// <summary>1D100減算スキルを作る。</summary>
    public static BattleSkillDef D100SubtractSkill(string id, string name, string desc, int diceFaces)
        => new BattleSkillDef(id, name, desc, SkillKind.D100Subtract, diceFaces, null, EffectType.AtkUp, 0);

    /// <summary>所持アクティブスキル本1冊につき成功確率＋valuePerBook の自動パッシブを作る。</summary>
    public static BattleSkillDef ProbPerOwnedBookSkill(string id, string name, string desc, int valuePerBook)
        => new BattleSkillDef(id, name, desc, SkillKind.ProbPerOwnedSkillBook, 0, null, EffectType.ProbUp, valuePerBook);

    /// <summary>自分のATK−1と引き換えに味方全員へ成功確率＋probBonus を配る協力スキルを作る。</summary>
    public static BattleSkillDef CoopBuffSkill(string id, string name, string desc, int probBonus)
        => new BattleSkillDef(id, name, desc, SkillKind.CoopProbBuff, 0, null, EffectType.ProbUp, probBonus);

    /// <summary>
    /// クリティカル率を minMultiplier〜maxMultiplier 倍（戦闘ごとにランダム）にする自動パッシブを作る。
    /// 最小倍率を Value、最大倍率を DiceFaces に格納する（同値なら固定倍率）。
    /// </summary>
    public static BattleSkillDef CritRateMultiplierSkill(string id, string name, string desc, int minMultiplier, int maxMultiplier)
        => new BattleSkillDef(id, name, desc, SkillKind.CritRateMultiplier, maxMultiplier, null, EffectType.CriticalRateUp, minMultiplier);

    /// <summary>オーバーキルが threshold 以上なら報酬GPに＋bonusGp する自動パッシブを作る（threshold は DiceFaces に格納）。</summary>
    public static BattleSkillDef OverkillGpBonusSkill(string id, string name, string desc, int threshold, int bonusGp)
        => new BattleSkillDef(id, name, desc, SkillKind.OverkillGpBonus, threshold, null, EffectType.AtkUp, bonusGp);

    /// <summary>オーバーキル perDamage ダメージごとに討伐時のレベルアップを＋1する自動パッシブを作る（perDamage は DiceFaces に格納）。</summary>
    public static BattleSkillDef OverkillLevelBonusSkill(string id, string name, string desc, int perDamage)
        => new BattleSkillDef(id, name, desc, SkillKind.OverkillLevelBonus, perDamage, null, EffectType.AtkUp, 0);

    /// <summary>連勝ボーナス自動パッシブを作る。連勝数×probPerStreak ぶん確率上昇＋levelEveryN 連勝ごとにレベル＋1（0でレベル加算なし）。</summary>
    public static BattleSkillDef WinStreakSkill(string id, string name, string desc, int probPerStreak, int levelEveryN)
        => new BattleSkillDef(id, name, desc, SkillKind.WinStreakBonus, levelEveryN, null, EffectType.ProbUp, probPerStreak);

    /// <summary>当たり目の一覧表記（"1,3,6"）。</summary>
    public string WinFacesText => string.Join(",", WinFaces.OrderBy(x => x));

    /// <summary>発動ボタン用の短い説明。</summary>
    public string ShortHint
    {
        get
        {
            switch (Kind)
            {
                case SkillKind.D100Subtract: return $"1D100−{DiceFaces}面";
                case SkillKind.CoopProbBuff: return $"味方全員 確率+{Value}／自分ATK-1";
                default:                     return $"{DiceFaces}面で{WinFacesText}";
            }
        }
    }
}

/// <summary>
/// スキルブックが指す skill_id → スキル定義 のレジストリ。
/// 新しいスキルはここに1行追加し、対象のスキルブック(master/items)の skill_id に同じIDを設定する。
/// </summary>
public static class BattleSkillRegistry
{
    private static readonly Dictionary<string, BattleSkillDef> _all =
        new List<BattleSkillDef>
        {
            // 8面ダイスを振って 1/3/6 が出たら成功確率 +10
            BattleSkillDef.StatBuffSkill("dice_prob10", "ダイスの加護",
                "8面ダイスを振り、1・3・6が出たら成功確率＋10",
                8, new[] { 1, 3, 6 }, EffectType.ProbUp, 10),

            // 100面ダイスと一緒に8面ダイスを振り、その出目ぶん 1D100 の出目を引く
            BattleSkillDef.D100SubtractSkill("dice_d8_subtract", "ダイスの極意",
                "1D100と一緒に8面ダイスを振り、その出目ぶん 1D100 の出目を引く", 8),

            // 所持しているアクティブスキル本1冊につき成功確率＋1
            BattleSkillDef.ProbPerOwnedBookSkill("engine_prob_per_book", "エンジンビルド",
                "所持しているアクティブスキルの本1冊につき成功確率＋1", 1),

            // 自分の攻撃力−1と引き換えに、味方全員の成功確率＋20
            BattleSkillDef.CoopBuffSkill("coop_party_prob20", "協力",
                "自分の攻撃力−1と引き換えに、味方全員の成功確率＋20", 20),

            // クリティカル率を2〜3倍（戦闘ごとにランダム）にする
            // （IDは×2固定だった旧仕様の名残。master/items の skill_id が参照しているため変えない）
            BattleSkillDef.CritRateMultiplierSkill("wingspan_crit_x2", "ウイングスパン",
                "クリティカル率が2倍〜3倍（戦闘ごとにランダム）になる", 2, 3),

            // オーバーキルが3以上なら討伐報酬GPに＋2
            BattleSkillDef.OverkillGpBonusSkill("wall_overkill_gp", "城壁都市",
                "オーバーキルダメージが3以上なら GP を追加で2もらえる", 3, 2),

            // 連勝数ぶん成功確率が上がり、5連勝ごとに討伐時のレベルアップ＋1
            BattleSkillDef.WinStreakSkill("wingspan_prob_per_streak", "ウイングスパン",
                "現在の連勝数ぶん成功確率が上がる。さらに5連勝するたびに討伐時のレベルが1追加で上がる", 1, 5),

            // 戦闘開始時に自動で8面ダイスを振り、1・3・6が出たら成功確率＋20（その戦闘だけ）
            BattleSkillDef.BattleStartStatBuffSkill("dominion_d8_prob20", "ドミニオン",
                "戦闘開始時に8面ダイスを振り、1・3・6が出たら成功確率＋20",
                8, new[] { 1, 3, 6 }, EffectType.ProbUp, 20),

            // オーバーキル4ダメージごとに討伐時のレベルアップ＋1
            BattleSkillDef.OverkillLevelBonusSkill("catan_overkill_level", "カタン",
                "オーバーキルダメージ4につき、討伐時のレベルが1追加で上がる", 4),

            // ここに各スキルブックのスキルを追加していく
        }
        .ToDictionary(s => s.Id);

    /// <summary>skill_id からスキル定義を取得（未定義なら null）。</summary>
    public static BattleSkillDef Get(string skillId)
        => !string.IsNullOrEmpty(skillId) && _all.TryGetValue(skillId, out var def) ? def : null;
}
