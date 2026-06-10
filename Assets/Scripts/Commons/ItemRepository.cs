using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// アイテムマスターデータを ItemData 形式で提供するシングルトン。
///
/// 旧実装では item/{job}/items/{itemId} を1件ずつFirestoreから読んでいた
/// （インベントリ32件なら32読み取り）が、現在はローカル同期済みの
/// ItemCacheManager から変換するだけなので Firestore 読み取りは発生しない。
/// </summary>
public class ItemRepository : SingletonBehaviour<ItemRepository>
{
    private readonly Dictionary<string, ItemData> _cache = new Dictionary<string, ItemData>();

    /// <summary>単一アイテムを取得（ローカルDB参照・読み取りゼロ）</summary>
    public async UniTask<ItemData> GetItemAsync(InventoryRef inventoryRef)
    {
        if (inventoryRef == null || string.IsNullOrEmpty(inventoryRef.ItemId)) return null;

        if (_cache.TryGetValue(inventoryRef.ItemId, out var cached)) return cached;

        var cacheManager = ItemCacheManager.instance;
        if (cacheManager == null)
        {
            Debug.LogError("[ItemRepository] ItemCacheManager が見つかりません");
            return null;
        }

        // ローカルDBが空ならまず同期する（バージョン未更新なら読み取りゼロ）
        if (cacheManager.Db.TotalCount == 0)
            await cacheManager.SyncAsync();

        var entry = cacheManager.FindById(inventoryRef.ItemId);
        if (entry == null)
        {
            Debug.LogWarning($"[ItemRepository] アイテムが見つかりません: {inventoryRef.FirestorePath}");
            return null;
        }

        var item = ToItemData(entry);
        _cache[item.ItemId] = item;
        return item;
    }

    /// <summary>複数アイテムを一括取得（キャッシュ済みはスキップ）</summary>
    public async UniTask<List<ItemData>> GetItemsAsync(List<InventoryRef> refs)
    {
        if (refs == null || refs.Count == 0) return new List<ItemData>();

        var results = new List<ItemData>();
        foreach (var r in refs)
        {
            var item = await GetItemAsync(r);
            if (item != null) results.Add(item);
        }
        return results;
    }

    /// <summary>インベントリ参照を解決し、指定スロットのアイテムだけ返す</summary>
    public async UniTask<List<ItemData>> GetInventoryBySlotAsync(List<InventoryRef> refs, EquipmentSlot slot)
    {
        var allItems = await GetItemsAsync(refs);
        return allItems.Where(item => item.SlotType == slot).ToList();
    }

    /// <summary>CachedItem を ItemData に変換（effects 含む）</summary>
    private static ItemData ToItemData(CachedItem c)
    {
        var item = new ItemData
        {
            ItemId       = c.item_id,
            Name         = c.name,
            SlotTypeName = c.slot_type,
            IconUrl      = c.icon_url,
            Description  = c.description,
            Game         = c.game,
        };

        if (c.effects != null)
        {
            foreach (var fx in c.effects)
            {
                item.Effects.Add(new ItemEffect
                {
                    EffectTypeName = fx.effectType,
                    Value          = fx.value,
                });
            }
        }
        return item;
    }

    /// <summary>キャッシュを1件クリア</summary>
    public void Invalidate(string job, string itemId)
    {
        _cache.Remove(itemId);
    }

    /// <summary>キャッシュを全クリア</summary>
    public void ClearCache()
    {
        _cache.Clear();
    }
}
