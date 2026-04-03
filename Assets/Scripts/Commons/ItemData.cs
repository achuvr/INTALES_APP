using Firebase.Firestore;
using System.Collections.Generic;

// ============================================================
// 装備効果の種類
// ステータス上昇系とシステム変更系に大別する。
// 新しい効果を追加するときはここにenumを追加するだけでOK。
// ============================================================
public enum EffectType
{
    // ---- ステータス上昇系 ----
    AtkUp,          // 攻撃力 + Value
    DefUp,          // 防御力 + Value
    HpUp,           // HP上限 + Value
    SpeedUp,        // 速度   + Value

    // ---- システム変更系 ----
    BonusExp,       // 獲得経験値 + Value%
    CriticalRateUp, // クリティカル率 + Value%
    GoldBonus,      // 獲得ゴールド + Value%
    SkillSlotUnlock,// スキルスロットを解放 (Valueでスロット番号)

    SpecialAbility, // 特殊能力付与 (Valueで種類番号)
    ProbUp,         // 確率上昇 (Valueで対象種類・確率%など)
}

// ============================================================
// 装備の1つの効果
// 複数の効果を持たせたいときは ItemData.Effects にリストで入れる
// ============================================================
[FirestoreData, System.Serializable]
public class ItemEffect
{
    public ItemEffect() {}

    // 効果の種類（enumを文字列として保存）
    [FirestoreProperty("effect_type")]
    public string EffectTypeName
    {
        get => _effectTypeName;
        set => _effectTypeName = value;
    }
    private string _effectTypeName;

    // 効果量（+10のダメージ、+5%のボーナスなど）
    [FirestoreProperty("value")]
    public int Value { get; set; }

    // EffectTypeに変換するプロパティ（Firestore非保存）
    [System.NonSerialized]
    private EffectType? _cachedType;
    public EffectType Type
    {
        get
        {
            if (_cachedType == null && !string.IsNullOrEmpty(_effectTypeName))
                System.Enum.TryParse(_effectTypeName, out EffectType t);
            return _cachedType ?? EffectType.AtkUp;
        }
        set
        {
            _effectTypeName = value.ToString();
            _cachedType = value;
        }
    }

    // 作成ヘルパー
    public static ItemEffect Make(EffectType type, int value)
        => new ItemEffect { Type = type, Value = value };

    public override string ToString()
        => $"{Type} +{Value}";
}

// ============================================================
// アイテム1つのデータ
// Firestoreの users/{uid}/items/{itemId} に保存する
// または Character.Inventory のリストに埋め込む
// ============================================================
[FirestoreData, System.Serializable]
public class ItemData
{
    public ItemData() { Effects = new System.Collections.Generic.List<ItemEffect>(); }

    // アイテム固有ID（Firestoreのドキュメントキーと一致させる）
    [FirestoreProperty("item_id")]
    public string ItemId { get; set; }

    // 表示名
    [FirestoreProperty("name")]
    public string Name { get; set; }

    // 装備スロットの種類（"weapon"/"head"/"body"/"feet"/"skill_book_a"/"skill_book_b"）
    [FirestoreProperty("slot_type")]
    public string SlotTypeName { get; set; }

    // EquipmentSlotに変換するプロパティ
    public EquipmentSlot SlotType
    {
        get
        {
            switch (SlotTypeName)
            {
                case "weapon":       return EquipmentSlot.Weapon;
                case "head":         return EquipmentSlot.Head;
                case "body":         return EquipmentSlot.Body;
                case "foot":         // Firebase登録値
                case "feet":         return EquipmentSlot.Feet;
                case "skillA":       // Firebase登録値
                case "skill_book_a": return EquipmentSlot.SkillBookA;
                case "skillB":       // Firebase登録値
                case "skill_book_b": return EquipmentSlot.SkillBookB;
                default:             return EquipmentSlot.Weapon;
            }









        }
        set
        {
            switch (value)
            {
                case EquipmentSlot.Weapon:     SlotTypeName = "weapon";      break;
                case EquipmentSlot.Head:       SlotTypeName = "head";        break;
                case EquipmentSlot.Body:       SlotTypeName = "body";        break;
                case EquipmentSlot.Feet:       SlotTypeName = "feet";        break;
                case EquipmentSlot.SkillBookA: SlotTypeName = "skill_book_a";break;
                case EquipmentSlot.SkillBookB: SlotTypeName = "skill_book_b";break;
            }
        }
    }

    // 効果リスト（複数の効果を持てる）
    [FirestoreProperty("effects")]
    public List<ItemEffect> Effects { get; set; }

    // アイコン画像URL（マスターデータの icon_url）
    [FirestoreProperty("icon_url")]
    public string IconUrl { get; set; }

    // 説明文
    [FirestoreProperty("description")]
    public string Description { get; set; }

    // ゲーム名
    [FirestoreProperty("game")]
    public string Game { get; set; }





    // 装備中かどうかを確認するヘルパー（CharacterのEquipmentと照合）
    public bool IsEquippedBy(Character character)
        => character?.Equipment?.GetItemId(SlotType) == ItemId;

    public override string ToString()
        => $"[{SlotType}] {Name} (id={ItemId})";
}
