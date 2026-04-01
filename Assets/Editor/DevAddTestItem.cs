#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using Firebase.Firestore;
using System.Collections.Generic;

/// <summary>
/// 開発・テスト用：プレイヤーのインベントリにサンプルアイテムを追加する
/// Unity メニュー → Tools → [Dev] Add Test Equipment Item から実行
/// ※ Playモード中に実行してください（Firebase接続が必要）
/// </summary>
public static class DevAddTestItem
{
    [MenuItem("Tools/[Dev] Add Test Equipment Item")]
    public static async void Execute()
    {
        if (!Application.isPlaying)
        {
            EditorUtility.DisplayDialog("注意", "Playモード中に実行してください（Firebase接続が必要）", "OK");
            return;
        }

        var manager = UserDataManager.instance;
        if (manager == null || string.IsNullOrEmpty(manager.UID))
        {
            Debug.LogError("[DevAddTestItem] UserDataManager が見つからないか、UIDが未設定です。");
            return;
        }

        // ---- テスト用アイテム定義 ----
        // ここを自由に書き換えてください
        var testItem = new ItemData
        {
            ItemId     = "helm_test_001",
            Name       = "革のヘルム",
            SlotType   = EquipmentSlot.Head,

            Effects    = new List<ItemEffect>
            {
                ItemEffect.Make(EffectType.DefUp, 5),   // 防御力 +5
                ItemEffect.Make(EffectType.HpUp,  10),  // HP上限 +10
            }
        };

        // ---- キャラクターのインベントリに追加 ----
        int charIdx = manager.CurrentSelectCharacterNumber;
        var chara   = manager.UserData.Characters[charIdx];

        // 同じIDが既にあれば上書き、なければ追加
        var existing = chara.Inventory.FindIndex(i => i.ItemId == testItem.ItemId);
        if (existing >= 0)
            chara.Inventory[existing] = testItem;
        else
            chara.Inventory.Add(testItem);

        Debug.Log($"[DevAddTestItem] ローカルに追加: {testItem}");

        // ---- Firestoreに保存 ----
        var db      = FirebaseFirestore.DefaultInstance;
        var docRef  = db.Collection("users")
                        .Document(manager.UID)
                        .Collection("characters")
                        .Document(charIdx.ToString());

        // inventoryをFirestoreの配列形式に変換
        var inventoryData = new List<Dictionary<string, object>>();
        foreach (var item in chara.Inventory)
        {
            var effectList = new List<Dictionary<string, object>>();
            foreach (var fx in item.Effects)
            {
                effectList.Add(new Dictionary<string, object>
                {
                    { "effect_type", fx.EffectTypeName },
                    { "value",       fx.Value          }
                });
            }
            inventoryData.Add(new Dictionary<string, object>
            {
                { "item_id",      item.ItemId      },
                { "name",         item.Name        },
                { "slot_type",    item.SlotTypeName },
                { "effects",      effectList       },

            });
        }

        await docRef.SetAsync(
            new Dictionary<string, object> { { "inventory", inventoryData } },
            SetOptions.MergeAll
        );

        Debug.Log($"[DevAddTestItem] Firestore に保存完了！ item_id={testItem.ItemId}");
        EditorUtility.DisplayDialog(
            "完了",
            $"テストアイテムを追加しました\n\n名前: {testItem.Name}\nスロット: {testItem.SlotType}\n効果: DefUp+5 / HpUp+10",
            "OK"
        );
    }
}
#endif
