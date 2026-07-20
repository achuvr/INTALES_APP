using System.Collections.Generic;

/// <summary>
/// 装備シリーズのセットスキル判定。
/// 武器・頭・体・足の4部位すべてに同じシリーズの装備を付けると、
/// そのシリーズのセットスキル（effects）が発動する。
/// シリーズ定義は master/items ドキュメントの series マップ（ItemCacheManager が items と一緒に同期）。
/// スキルブックA/Bはセット判定に関与しない。
/// </summary>
public static class SeriesSetBonus
{
    private static readonly EquipmentSlot[] SET_SLOTS =
        { EquipmentSlot.Weapon, EquipmentSlot.Head, EquipmentSlot.Body, EquipmentSlot.Feet };

    /// <summary>指定キャラの4部位が同一シリーズならそのシリーズ定義を返す（不成立ならnull）。</summary>
    public static CachedSeries GetActiveSeries(int charIdx)
    {
        var cache = ItemCacheManager.instance;
        if (cache == null || !cache.IsLoaded) return null;

        string seriesId = null;
        foreach (var slot in SET_SLOTS)
        {
            string itemId = LocalEquipSave.Load(charIdx, slot);
            if (string.IsNullOrEmpty(itemId)) return null;
            var item = cache.FindById(itemId);
            if (item == null || string.IsNullOrEmpty(item.series)) return null;
            if (seriesId == null) seriesId = item.series;
            else if (seriesId != item.series) return null;
        }
        return seriesId != null ? cache.FindSeriesById(seriesId) : null;
    }

    /// <summary>効果リストの日本語表記（例: 「クリティカル率+10% / 攻撃力+2」）。</summary>
    public static string DescribeEffects(List<LocalItemEffect> effects)
    {
        if (effects == null || effects.Count == 0) return "効果なし";
        var parts = new List<string>();
        foreach (var fx in effects)
        {
            if (fx == null) continue;
            if (!System.Enum.TryParse(fx.effectType, out EffectType type)) continue;
            parts.Add(Describe(type, fx.value));
        }
        return parts.Count > 0 ? string.Join(" / ", parts) : "効果なし";
    }

    /// <summary>効果1件の日本語表記。</summary>
    public static string Describe(EffectType type, int value) => type switch
    {
        EffectType.AtkUp            => $"攻撃力+{value}",
        EffectType.DefUp            => $"防御力+{value}",
        EffectType.HpUp             => $"HP+{value}",
        EffectType.SpeedUp          => $"速度+{value}",
        EffectType.BonusExp         => $"獲得経験値+{value}%",
        EffectType.CriticalRateUp   => $"クリティカル率+{value}%",
        EffectType.GoldBonus        => $"獲得ゴールド+{value}%",
        EffectType.SkillSlotUnlock  => $"スキルスロットLv{value}",
        EffectType.CriticalDamageUp => $"クリティカルダメージ+{value}",
        EffectType.SpecialAbility   => $"特殊能力+{value}",
        EffectType.ProbUp           => $"成功確率+{value}%",
        _                           => $"{type}+{value}",
    };
}
