#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using Firebase.Firestore;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// 開発・テスト用：プレイヤーのインベントリにサンプルアイテムを追加する
/// Playモード中に Tools メニューから実行してください。
///
/// マスターデータ: item/{job}/items/{itemId} に書き込み
/// インベントリ: users/{uid}/characters/{charIdx}/inventory に InventoryRef を書き込み
/// </summary>
public static class DevAddTestItem
{
    // ================================================================
    // 個別追加
    // ================================================================
    [MenuItem("Tools/[Dev] Add Test Item/頭: 革のヘルム (DefUp+5 HpUp+10)")]
    public static async void AddHelm()
    {
        await AddItem("warrior", new ItemData
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

    [MenuItem("Tools/[Dev] Add Test Item/武器: ブロンズソード (AtkUp+12)")]
    public static async void AddSword()
    {
        await AddItem("warrior", new ItemData
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
        await AddItem("warrior", new ItemData
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

    [MenuItem("Tools/[Dev] Add Test Item/体: 革のよろい (DefUp+8 HpUp+20)")]
    public static async void AddLeatherArmor()
    {
        await AddItem("warrior", new ItemData
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
        await AddItem("warrior", new ItemData
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

    [MenuItem("Tools/[Dev] Add Test Item/足: 革のブーツ (SpeedUp+5 DefUp+3)")]
    public static async void AddBoots()
    {
        await AddItem("warrior", new ItemData
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

    [MenuItem("Tools/[Dev] Add Test Item/スキルA: 炎のスキルブック (BonusExp+20)")]
    public static async void AddSkillBookA()
    {
        await AddItem("warrior", new ItemData
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
        await AddItem("warrior", new ItemData
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
        var job = GetCurrentCharaJob();

        var testItems = new[]
        {
            new ItemData { ItemId="helm_test_001",   Name="革のヘルム",       SlotType=EquipmentSlot.Head,       Effects=new List<ItemEffect>{ ItemEffect.Make(EffectType.DefUp,5), ItemEffect.Make(EffectType.HpUp,10) } },
            new ItemData { ItemId="weapon_test_001", Name="ブロンズソード",   SlotType=EquipmentSlot.Weapon,     Effects=new List<ItemEffect>{ ItemEffect.Make(EffectType.AtkUp,12) } },
            new ItemData { ItemId="weapon_test_002", Name="鉄の剣",           SlotType=EquipmentSlot.Weapon,     Effects=new List<ItemEffect>{ ItemEffect.Make(EffectType.AtkUp,25), ItemEffect.Make(EffectType.SpeedUp,3) } },
            new ItemData { ItemId="body_test_001",   Name="革のよろい",       SlotType=EquipmentSlot.Body,       Effects=new List<ItemEffect>{ ItemEffect.Make(EffectType.DefUp,8), ItemEffect.Make(EffectType.HpUp,20) } },
            new ItemData { ItemId="body_test_002",   Name="鎖帷子",           SlotType=EquipmentSlot.Body,       Effects=new List<ItemEffect>{ ItemEffect.Make(EffectType.DefUp,18), ItemEffect.Make(EffectType.CriticalRateUp,5) } },
            new ItemData { ItemId="feet_test_001",   Name="革のブーツ",       SlotType=EquipmentSlot.Feet,       Effects=new List<ItemEffect>{ ItemEffect.Make(EffectType.SpeedUp,5), ItemEffect.Make(EffectType.DefUp,3) } },
            new ItemData { ItemId="skilla_test_001", Name="炎のスキルブック", SlotType=EquipmentSlot.SkillBookA, Effects=new List<ItemEffect>{ ItemEffect.Make(EffectType.BonusExp,20) } },
            new ItemData { ItemId="skillb_test_001", Name="幸運のスキルブック",SlotType=EquipmentSlot.SkillBookB,Effects=new List<ItemEffect>{ ItemEffect.Make(EffectType.GoldBonus,15), ItemEffect.Make(EffectType.ProbUp,1) } },
        };

        foreach (var item in testItems)
        {
            await SaveToMasterAsync(job, item);
            AddInventoryRef(job, item.ItemId);
        }
        await SaveInventoryToFirestore();
        EditorUtility.DisplayDialog("完了", "全8アイテムを追加しました！", "OK");
    }

    // ================================================================
    // ローカルDBからアイテム入手（テスト用）
    // ================================================================
    [MenuItem("Tools/[Dev] Acquire Item/魔法使い: 夕日の開拓者(杖)")]
    public static async void AcquireSunsetStaff()
    {
        await AcquireByName("夕日の開拓者(杖)");
    }

    private static async System.Threading.Tasks.Task AcquireByName(string itemName)
    {
        if (!Application.isPlaying)
        {
            EditorUtility.DisplayDialog("注意", "Playモード中に実行してください", "OK");
            return;
        }
        if (ItemSyncManager.instance == null)
        {
            Debug.LogError("[DevAddTestItem] ItemSyncManager がシーンに存在しません。");
            return;
        }

        var entry = ItemSyncManager.instance.FindByName(itemName);
        if (entry == null)
        {
            EditorUtility.DisplayDialog("エラー",
                $"ローカルDBに「{itemName}」が見つかりません。\n先にアイテムDB同期を実行してください。", "OK");
            return;
        }

        bool ok = await ItemSyncManager.instance.AcquireItemAsync(entry);
        if (ok)
            EditorUtility.DisplayDialog("入手！",
                $"「{entry.name}」を入手しました！\n職業: {entry.job}\n種類: {entry.slotType}", "OK");
        else
            EditorUtility.DisplayDialog("失敗",
                $"「{entry.name}」の入手に失敗しました。\n既に所持しているか、通信エラーです。", "OK");
    }

    // ================================================================
    // アイテムDB同期（手動実行）
    // ================================================================
    [MenuItem("Tools/[Dev] アイテムDB強制同期")]
    public static async void ForceItemSync()
    {
        if (!Application.isPlaying)
        {
            EditorUtility.DisplayDialog("注意", "Playモード中に実行してください（Firebase接続が必要）", "OK");
            return;
        }
        if (ItemSyncManager.instance == null)
        {
            Debug.LogError("[DevAddTestItem] ItemSyncManager がシーンに存在しません。");
            return;
        }

        // lastSync をクリアして全件取得させる
        PlayerPrefs.DeleteKey("ItemSync_LastSyncUtc");
        PlayerPrefs.Save();

        await ItemSyncManager.instance.InitAsync();
        EditorUtility.DisplayDialog("完了", "アイテムDBの同期が完了しました。", "OK");
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

    private static string GetCurrentCharaJob()
    {
        var manager = UserDataManager.instance;
        int charIdx = manager.CurrentSelectCharacterNumber;
        return manager.UserData.Characters[charIdx].Job;
    }

    private static async System.Threading.Tasks.Task AddItem(string job, ItemData item)
    {
        if (!CheckPlaying()) return;

        var charaJob = GetCurrentCharaJob();
        await SaveToMasterAsync(charaJob, item);
        AddInventoryRef(charaJob, item.ItemId);
        await SaveInventoryToFirestore();

        EditorUtility.DisplayDialog(
            "追加完了",
            $"名前: {item.Name}\nスロット: {item.SlotType}\n効果数: {item.Effects.Count}",
            "OK"
        );
    }

    /// <summary>マスターデータ item/{job}/items/{itemId} に書き込む</summary>
    private static async System.Threading.Tasks.Task SaveToMasterAsync(string job, ItemData item)
    {
        var db = FirebaseFirestore.DefaultInstance;
        var docRef = db.Collection("item")
                       .Document(job)
                       .Collection("items")
                       .Document(item.ItemId);

        var effectList = new List<Dictionary<string, object>>();
        if (item.Effects != null)
        {
            foreach (var fx in item.Effects)
                effectList.Add(new Dictionary<string, object>
                {
                    { "effect_type", fx.EffectTypeName },
                    { "value",       fx.Value          }
                });
        }

        var data = new Dictionary<string, object>
        {
            { "name",      item.Name        },
            { "slot_type", item.SlotTypeName },
            { "job",       job               },
            { "effects",   effectList        },
        };

        await docRef.SetAsync(data, SetOptions.MergeAll);
        Debug.Log($"[DevAddTestItem] マスターに保存: item/{job}/items/{item.ItemId}");
    }

    /// <summary>キャラクターのインベントリに InventoryRef を追加（ローカルのみ）</summary>
    private static void AddInventoryRef(string job, string itemId)
    {
        var manager = UserDataManager.instance;
        int charIdx = manager.CurrentSelectCharacterNumber;
        var chara   = manager.UserData.Characters[charIdx];

        if (!chara.Inventory.Any(r => r.ItemId == itemId))
        {
            chara.Inventory.Add(new InventoryRef { Job = job, ItemId = itemId });
        }
        Debug.Log($"[DevAddTestItem] ローカルに追加: {job}/{itemId}");
    }

    /// <summary>インベントリ参照をFirestoreに保存</summary>
    private static async System.Threading.Tasks.Task SaveInventoryToFirestore()
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
        foreach (var r in chara.Inventory)
        {
            inventoryData.Add(new Dictionary<string, object>
            {
                { "job",     r.Job    },
                { "item_id", r.ItemId },
            });
        }

        await docRef.SetAsync(
            new Dictionary<string, object> { { "inventory", inventoryData } },
            SetOptions.MergeAll
        );
        Debug.Log($"[DevAddTestItem] Firestore保存完了（{inventoryData.Count}件の参照）");
    }
}
#endif
