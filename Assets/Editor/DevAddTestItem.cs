#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using Firebase.Firestore;
using System.Collections.Generic;

/// <summary>
/// 開発・テスト用：プレイヤーのインベントリにサンプルアイテムを追加する
/// Playモード中に Tools メニューから実行してください
/// </summary>
public static class DevAddTestItem
{
    // ================================================================
    // 頭装備を追加
    // ================================================================
    [MenuItem("Tools/[Dev] Add Test Item/頭: 革のヘルム (DefUp+5 HpUp+10)")]
    public static async void AddHelm()
    {
        await AddItem(new ItemData
        {
            ItemId   = "helm_test_001",
            Name     = "革のヘルム",
            SlotType = EquipmentSlot.Head,
            Effects  = new List<ItemEffect>
            {
                ItemEffect.Make(EffectType.DefUp, 5),
                ItemEffect.Make(EffectType.HpUp,  10),
            }
        });
    }

    // ================================================================
    // 武器を追加
    // ================================================================
    [MenuItem("Tools/[Dev] Add Test Item/武器: ブロンズソード (AtkUp+12)")]
    public static async void AddSword()
    {
        await AddItem(new ItemData
        {
            ItemId   = "weapon_test_001",
            Name     = "ブロンズソード",
            SlotType = EquipmentSlot.Weapon,
            Effects  = new List<ItemEffect>
            {
                ItemEffect.Make(EffectType.AtkUp, 12),
            }
        });
    }

    [MenuItem("Tools/[Dev] Add Test Item/武器: 鉄の剣 (AtkUp+25 SpeedUp+3)")]
    public static async void AddIronSword()
    {
        await AddItem(new ItemData
        {
            ItemId   = "weapon_test_002",
            Name     = "鉄の剣",
            SlotType = EquipmentSlot.Weapon,
            Effects  = new List<ItemEffect>
            {
                ItemEffect.Make(EffectType.AtkUp,   25),
                ItemEffect.Make(EffectType.SpeedUp,  3),
            }
        });
    }

    // ================================================================
    // 体装備を追加
    // ================================================================
    [MenuItem("Tools/[Dev] Add Test Item/体: 革のよろい (DefUp+8 HpUp+20)")]
    public static async void AddLeatherArmor()
    {
        await AddItem(new ItemData
        {
            ItemId   = "body_test_001",
            Name     = "革のよろい",
            SlotType = EquipmentSlot.Body,
            Effects  = new List<ItemEffect>
            {
                ItemEffect.Make(EffectType.DefUp, 8),
                ItemEffect.Make(EffectType.HpUp,  20),
            }
        });
    }

    [MenuItem("Tools/[Dev] Add Test Item/体: 鎖帷子 (DefUp+18 CriticalRateUp+5)")]
    public static async void AddChainmail()
    {
        await AddItem(new ItemData
        {
            ItemId   = "body_test_002",
            Name     = "鎖帷子",
            SlotType = EquipmentSlot.Body,
            Effects  = new List<ItemEffect>
            {
                ItemEffect.Make(EffectType.DefUp,         18),
                ItemEffect.Make(EffectType.CriticalRateUp, 5),
            }
        });
    }

    // ================================================================
    // 足装備を追加
    // ================================================================
    [MenuItem("Tools/[Dev] Add Test Item/足: 革のブーツ (SpeedUp+5 DefUp+3)")]
    public static async void AddBoots()
    {
        await AddItem(new ItemData
        {
            ItemId   = "feet_test_001",
            Name     = "革のブーツ",
            SlotType = EquipmentSlot.Feet,
            Effects  = new List<ItemEffect>
            {
                ItemEffect.Make(EffectType.SpeedUp, 5),
                ItemEffect.Make(EffectType.DefUp,   3),
            }
        });
    }

    // ================================================================
    // スキルブックを追加
    // ================================================================
    [MenuItem("Tools/[Dev] Add Test Item/スキルA: 炎のスキルブック (BonusExp+20)")]
    public static async void AddSkillBookA()
    {
        await AddItem(new ItemData
        {
            ItemId   = "skilla_test_001",
            Name     = "炎のスキルブック",
            SlotType = EquipmentSlot.SkillBookA,
            Effects  = new List<ItemEffect>
            {
                ItemEffect.Make(EffectType.BonusExp, 20),
            }
        });
    }

    [MenuItem("Tools/[Dev] Add Test Item/スキルB: 幸運のスキルブック (GoldBonus+15 ProbUp+1)")]
    public static async void AddSkillBookB()
    {
        await AddItem(new ItemData
        {
            ItemId   = "skillb_test_001",
            Name     = "幸運のスキルブック",
            SlotType = EquipmentSlot.SkillBookB,
            Effects  = new List<ItemEffect>
            {
                ItemEffect.Make(EffectType.GoldBonus, 15),
                ItemEffect.Make(EffectType.ProbUp,     1),
            }
        });
    }

    // ================================================================
    // 全アイテムを一括追加
    // ================================================================
    [MenuItem("Tools/[Dev] Add Test Item/★ 全アイテムを一括追加")]
    public static async void AddAll()
    {
        if (!CheckPlaying()) return;
        await AddItemInternal(new ItemData { ItemId="helm_test_001",   Name="革のヘルム",       SlotType=EquipmentSlot.Head,       Effects=new List<ItemEffect>{ ItemEffect.Make(EffectType.DefUp,5), ItemEffect.Make(EffectType.HpUp,10) } });
        await AddItemInternal(new ItemData { ItemId="weapon_test_001", Name="ブロンズソード",   SlotType=EquipmentSlot.Weapon,     Effects=new List<ItemEffect>{ ItemEffect.Make(EffectType.AtkUp,12) } });
        await AddItemInternal(new ItemData { ItemId="weapon_test_002", Name="鉄の剣",           SlotType=EquipmentSlot.Weapon,     Effects=new List<ItemEffect>{ ItemEffect.Make(EffectType.AtkUp,25), ItemEffect.Make(EffectType.SpeedUp,3) } });
        await AddItemInternal(new ItemData { ItemId="body_test_001",   Name="革のよろい",       SlotType=EquipmentSlot.Body,       Effects=new List<ItemEffect>{ ItemEffect.Make(EffectType.DefUp,8), ItemEffect.Make(EffectType.HpUp,20) } });
        await AddItemInternal(new ItemData { ItemId="body_test_002",   Name="鎖帷子",           SlotType=EquipmentSlot.Body,       Effects=new List<ItemEffect>{ ItemEffect.Make(EffectType.DefUp,18), ItemEffect.Make(EffectType.CriticalRateUp,5) } });
        await AddItemInternal(new ItemData { ItemId="feet_test_001",   Name="革のブーツ",       SlotType=EquipmentSlot.Feet,       Effects=new List<ItemEffect>{ ItemEffect.Make(EffectType.SpeedUp,5), ItemEffect.Make(EffectType.DefUp,3) } });
        await AddItemInternal(new ItemData { ItemId="skilla_test_001", Name="炎のスキルブック", SlotType=EquipmentSlot.SkillBookA, Effects=new List<ItemEffect>{ ItemEffect.Make(EffectType.BonusExp,20) } });
        await AddItemInternal(new ItemData { ItemId="skillb_test_001", Name="幸運のスキルブック",SlotType=EquipmentSlot.SkillBookB,Effects=new List<ItemEffect>{ ItemEffect.Make(EffectType.GoldBonus,15), ItemEffect.Make(EffectType.ProbUp,1) } });
        await SaveToFirestore();
        EditorUtility.DisplayDialog("完了", "全8アイテムを追加しました！", "OK");
    }

    // ================================================================
    // 共通処理
    // ================================================================
    private static bool CheckPlaying()
    {
        if (!Application.isPlaying)
        {
            EditorUtility.DisplayDialog("注意", "Playモード中に実行してください（Firebase接続が必要）", "OK");
            return false;
        }
        var manager = UserDataManager.instance;
        if (manager == null || string.IsNullOrEmpty(manager.UID))
        {
            Debug.LogError("[DevAddTestItem] UserDataManager が見つからないか、UIDが未設定です。");
            return false;
        }
        return true;
    }

    private static async System.Threading.Tasks.Task AddItem(ItemData item)
    {
        if (!CheckPlaying()) return;
        await AddItemInternal(item);
        await SaveToFirestore();
        EditorUtility.DisplayDialog(
            "追加完了",
            $"名前: {item.Name}\nスロット: {item.SlotType}\n効果数: {item.Effects.Count}",
            "OK"
        );
    }

    private static System.Threading.Tasks.Task AddItemInternal(ItemData item)
    {
        var manager = UserDataManager.instance;
        int charIdx = manager.CurrentSelectCharacterNumber;
        var chara   = manager.UserData.Characters[charIdx];

        var idx = chara.Inventory.FindIndex(i => i.ItemId == item.ItemId);
        if (idx >= 0) chara.Inventory[idx] = item;
        else          chara.Inventory.Add(item);

        Debug.Log($"[DevAddTestItem] ローカルに追加: {item}");
        return System.Threading.Tasks.Task.CompletedTask;
    }

    private static async System.Threading.Tasks.Task SaveToFirestore()
    {
        var manager = UserDataManager.instance;
        int charIdx = manager.CurrentSelectCharacterNumber;
        var chara   = manager.UserData.Characters[charIdx];

        var db     = FirebaseFirestore.DefaultInstance;
        var docRef = db.Collection("users")
                       .Document(manager.UID)
                       .Collection("characters")
                       .Document(charIdx.ToString());

        var inventoryData = new List<Dictionary<string, object>>();
        foreach (var item in chara.Inventory)
        {
            var effectList = new List<Dictionary<string, object>>();
            foreach (var fx in item.Effects)
                effectList.Add(new Dictionary<string, object>
                {
                    { "effect_type", fx.EffectTypeName },
                    { "value",       fx.Value          }
                });

            inventoryData.Add(new Dictionary<string, object>
            {
                { "item_id",   item.ItemId      },
                { "name",      item.Name        },
                { "slot_type", item.SlotTypeName },
                { "effects",   effectList       },
            });
        }

        await docRef.SetAsync(
            new Dictionary<string, object> { { "inventory", inventoryData } },
            SetOptions.MergeAll
        );
        Debug.Log($"[DevAddTestItem] Firestore保存完了（{inventoryData.Count}件）");
    }
}
#endif