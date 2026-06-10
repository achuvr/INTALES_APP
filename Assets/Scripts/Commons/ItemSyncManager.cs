using System;
using System.Collections.Generic;
using System.Linq;
using Firebase.Firestore;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// アイテムマスターデータへのアクセスと、アイテム入手（インベントリ書き込み）を担うマネージャー。
///
/// データの実体・同期は ItemCacheManager に一本化されており、
/// このクラスは LocalItemEntry 形式での検索APIと AcquireItemAsync を提供する
/// 互換レイヤー（旧実装では独自にFirestoreから同期していたが、二重同期を解消した）。
///
/// Firestore 構造:
///   master/items                     … 全アイテムマスター（1ドキュメント）
///   users/{uid}.characters.{i}.inventory … キャラクターの所持アイテム
/// </summary>
public class ItemSyncManager : SingletonBehaviour<ItemSyncManager>
{
    // ================================================================
    // 公開API
    // ================================================================

    /// <summary>起動時に呼び出す。バージョンが変わっていれば同期する（変更なしなら読み取りゼロ）。</summary>
    public async UniTask InitAsync()
    {
        if (ItemCacheManager.instance == null)
        {
            Debug.LogError("[ItemSync] ItemCacheManager が見つかりません");
            return;
        }
        await ItemCacheManager.instance.SyncAsync(msg => Debug.Log($"[ItemSync] {msg}"));
    }

    /// <summary>職業＋スロットでアイテム一覧を取得</summary>
    public List<LocalItemEntry> GetItems(string job, string slotType)
    {
        var cache = ItemCacheManager.instance;
        if (cache == null) return new List<LocalItemEntry>();
        return cache.GetByJobAndSlot(job, slotType).Select(ToEntry).ToList();
    }

    /// <summary>全アイテムを取得</summary>
    public List<LocalItemEntry> GetAllItems()
    {
        var cache = ItemCacheManager.instance;
        if (cache == null) return new List<LocalItemEntry>();
        return cache.GetAll().Select(ToEntry).ToList();
    }

    /// <summary>アイテム名でローカルDBを検索</summary>
    public LocalItemEntry FindByName(string itemName)
    {
        var cached = ItemCacheManager.instance?.GetAll()
            .FirstOrDefault(x => x.name == itemName);
        return cached != null ? ToEntry(cached) : null;
    }

    /// <summary>itemId でローカルDBを検索</summary>
    public LocalItemEntry FindById(string itemId)
    {
        var cached = ItemCacheManager.instance?.FindById(itemId);
        return cached != null ? ToEntry(cached) : null;
    }

    /// <summary>
    /// アイテムを入手し、キャラクターのインベントリに追加して Firestore に保存する。
    /// ローカルDBに存在するアイテムを itemId で指定する。
    /// </summary>
    public async UniTask<bool> AcquireItemAsync(string itemId)
    {
        var entry = FindById(itemId);
        if (entry == null)
        {
            Debug.LogError($"[ItemSync] アイテムが見つかりません: {itemId}");
            return false;
        }
        return await AcquireItemAsync(entry);
    }

    /// <summary>
    /// アイテムを入手し、キャラクターのインベントリに追加して Firestore に保存する。
    /// users/{uid} の characters.{i}.inventory フィールドを1回の書き込みで更新する。
    /// </summary>
    public async UniTask<bool> AcquireItemAsync(LocalItemEntry entry)
    {
        var manager = UserDataManager.instance;
        if (manager == null || string.IsNullOrEmpty(manager.UID))
        {
            Debug.LogError("[ItemSync] UserDataManager が未初期化です");
            return false;
        }

        int charIdx = manager.CurrentSelectCharacterNumber;
        var chara = manager.UserData.Characters[charIdx];

        // 既に所持していたら追加しない
        if (chara.Inventory.Any(r => r.ItemId == entry.itemId))
        {
            Debug.LogWarning($"[ItemSync] 既に所持しています: {entry.name} ({entry.itemId})");
            return false;
        }

        // ローカルのインベントリに追加
        var inventoryRef = new InventoryRef { Job = entry.job, ItemId = entry.itemId };
        chara.Inventory.Add(inventoryRef);

        // Firestore に保存
        try
        {
            await SaveInventoryAsync(manager.UID, charIdx, chara.Inventory);
            Debug.Log($"[ItemSync] アイテム入手: {entry.name} ({entry.job}/{entry.itemId})");
            return true;
        }
        catch (Exception ex)
        {
            // 失敗したらローカルからも戻す
            chara.Inventory.Remove(inventoryRef);
            Debug.LogError($"[ItemSync] アイテム入手エラー: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// インベントリ一覧を users/{uid} の characters.{charIdx}.inventory に書き込む。
    /// </summary>
    public static async UniTask SaveInventoryAsync(string uid, int charIdx, List<InventoryRef> inventory)
    {
        var db = FirebaseFirestore.DefaultInstance;
        var docRef = db.Collection("users").Document(uid);

        var inventoryData = new List<Dictionary<string, object>>();
        foreach (var r in inventory)
        {
            inventoryData.Add(new Dictionary<string, object>
            {
                { "job",     r.Job    },
                { "item_id", r.ItemId },
            });
        }

        await docRef.UpdateAsync(new Dictionary<FieldPath, object>
        {
            { new FieldPath("characters", charIdx.ToString(), "inventory"), inventoryData }
        }).AsUniTask();
    }

    // ================================================================
    // CachedItem → LocalItemEntry 変換
    // ================================================================

    private static LocalItemEntry ToEntry(CachedItem c)
    {
        return new LocalItemEntry
        {
            job         = c.job,
            itemId      = c.item_id,
            name        = c.name,
            slotType    = c.slot_type,
            iconUrl     = c.icon_url,
            description = c.description,
            game        = c.game,
            effects     = c.effects ?? new List<LocalItemEffect>(),
        };
    }
}

// ================================================================
// ローカル保存用データクラス（JsonUtility 対応）
// ================================================================

[Serializable]
public class LocalItemDatabaseWrapper
{
    public List<LocalItemEntry> items = new List<LocalItemEntry>();
}

[Serializable]
public class LocalItemEntry
{
    public string job;
    public string itemId;
    public string name;
    public string slotType;
    public string iconUrl;
    public string description;
    public string game;
    public List<LocalItemEffect> effects = new List<LocalItemEffect>();
}

[Serializable]
public class LocalItemEffect
{
    public string effectType;
    public int value;
}
