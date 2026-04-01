using Firebase.Firestore;

/// <summary>
/// キャラクターの装備データ。
/// Firestoreのcharacterドキュメント内の "equipment" マップに対応。
/// 各スロットは装備しているアイテムのIDを文字列で保持（未装備は空文字）。
/// </summary>
[FirestoreData, System.Serializable]
public class Equipment
{
    public Equipment() {}

    /// <summary>武器スロット</summary>
    [FirestoreProperty("weapon")]
    public string Weapon { get; set; } = "";

    /// <summary>頭防具スロット</summary>
    [FirestoreProperty("head")]
    public string Head { get; set; } = "";

    /// <summary>体防具スロット</summary>
    [FirestoreProperty("body")]
    public string Body { get; set; } = "";

    /// <summary>足防具スロット</summary>
    [FirestoreProperty("feet")]
    public string Feet { get; set; } = "";

    /// <summary>スキルブックAスロット</summary>
    [FirestoreProperty("skill_book_a")]
    public string SkillBookA { get; set; } = "";

    /// <summary>スキルブックBスロット</summary>
    [FirestoreProperty("skill_book_b")]
    public string SkillBookB { get; set; } = "";

    /// <summary>指定スロットに装備しているかどうか</summary>
    public bool IsEquipped(EquipmentSlot slot) => !string.IsNullOrEmpty(GetItemId(slot));

    /// <summary>スロットのアイテムIDを取得</summary>
    public string GetItemId(EquipmentSlot slot)
    {
        switch (slot)
        {
            case EquipmentSlot.Weapon:     return Weapon;
            case EquipmentSlot.Head:       return Head;
            case EquipmentSlot.Body:       return Body;
            case EquipmentSlot.Feet:       return Feet;
            case EquipmentSlot.SkillBookA: return SkillBookA;
            case EquipmentSlot.SkillBookB: return SkillBookB;
            default:                       return "";
        }
    }

    /// <summary>スロットにアイテムIDをセット（"" で未装備）</summary>
    public void SetItemId(EquipmentSlot slot, string itemId)
    {
        switch (slot)
        {
            case EquipmentSlot.Weapon:     Weapon     = itemId ?? ""; break;
            case EquipmentSlot.Head:       Head       = itemId ?? ""; break;
            case EquipmentSlot.Body:       Body       = itemId ?? ""; break;
            case EquipmentSlot.Feet:       Feet       = itemId ?? ""; break;
            case EquipmentSlot.SkillBookA: SkillBookA = itemId ?? ""; break;
            case EquipmentSlot.SkillBookB: SkillBookB = itemId ?? ""; break;
        }
    }
}

/// <summary>装備スロットの種類</summary>
public enum EquipmentSlot
{
    Weapon,
    Head,
    Body,
    Feet,
    SkillBookA,
    SkillBookB
}