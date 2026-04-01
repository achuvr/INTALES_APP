using UnityEngine;

/// <summary>
/// 装備中アイテムをPlayerPrefsでローカル管理するユーティリティ。
/// Firestoreを使わず端末にセーブデータとして保存する。
/// キー形式: "eq_{charIndex}_{slotIndex}"
/// </summary>
public static class LocalEquipSave
{
    private static string Key(int charIdx, EquipmentSlot slot)
        => $"eq_{charIdx}_{(int)slot}";

    /// <summary>指定スロットの装備IDを保存</summary>
    public static void Save(int charIdx, EquipmentSlot slot, string itemId)
    {
        PlayerPrefs.SetString(Key(charIdx, slot), itemId ?? "");
        PlayerPrefs.Save();
        Debug.Log($"[LocalEquipSave] Save: char={charIdx} slot={slot} item={itemId}");
    }

    /// <summary>指定スロットの装備IDを読み込み（未設定なら""）</summary>
    public static string Load(int charIdx, EquipmentSlot slot)
        => PlayerPrefs.GetString(Key(charIdx, slot), "");

    /// <summary>キャラクターの全スロットをローカルデータから復元</summary>
    public static void ApplyToCharacter(int charIdx, Character character)
    {
        if (character == null) return;
        foreach (EquipmentSlot slot in System.Enum.GetValues(typeof(EquipmentSlot)))
        {
            string itemId = Load(charIdx, slot);
            character.Equipment.SetItemId(slot, itemId);
        }
        Debug.Log($"[LocalEquipSave] Applied to char[{charIdx}]");
    }

    /// <summary>全キャラクターの装備をまとめて復元</summary>
    public static void ApplyAll(System.Collections.Generic.List<Character> characters)
    {
        if (characters == null) return;
        for (int i = 0; i < characters.Count; i++)
            ApplyToCharacter(i, characters[i]);
    }
}