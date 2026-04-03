using System.Collections.Generic;
using System.Linq;
using Firebase.Firestore;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// item/{job}/items/{itemId} からアイテムマスターデータを取得・キャッシュするシングルトン。
/// シーン上に配置するか、DontDestroyOnLoad な GameObject にアタッチして使う。
/// </summary>
public class ItemRepository : SingletonBehaviour<ItemRepository>
{
    private readonly Dictionary<string, ItemData> _cache = new Dictionary<string, ItemData>();

    private static string CacheKey(string job, string itemId) => $"{job}/{itemId}";
    private static string CacheKey(InventoryRef r) => CacheKey(r.Job, r.ItemId);

    /// <summary>単一アイテムを取得（キャッシュ優先）</summary>
    public async UniTask<ItemData> GetItemAsync(InventoryRef inventoryRef)
    {
        if (inventoryRef == null || string.IsNullOrEmpty(inventoryRef.ItemId)) return null;

        var key = CacheKey(inventoryRef);
        if (_cache.TryGetValue(key, out var cached)) return cached;

        try
        {
            var db = FirebaseFirestore.DefaultInstance;
            var docRef = db.Collection("item")
                           .Document(inventoryRef.Job)
                           .Collection("items")
                           .Document(inventoryRef.ItemId);

            var snap = await docRef.GetSnapshotAsync().AsUniTask();
            if (!snap.Exists)
            {
                Debug.LogWarning($"[ItemRepository] ドキュメントが見つかりません: {inventoryRef.FirestorePath}");
                return null;
            }

            var item = snap.ConvertTo<ItemData>();
            item.ItemId = inventoryRef.ItemId;
            _cache[key] = item;
            return item;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[ItemRepository] 取得エラー ({inventoryRef.FirestorePath}): {ex.Message}");
            return null;
        }
    }

    /// <summary>複数アイテムを一括取得（キャッシュ済みはスキップ）</summary>
    public async UniTask<List<ItemData>> GetItemsAsync(List<InventoryRef> refs)
    {
        if (refs == null || refs.Count == 0) return new List<ItemData>();

        var tasks = refs.Select(r => GetItemAsync(r));
        var results = await UniTask.WhenAll(tasks);
        return results.Where(item => item != null).ToList();
    }

    /// <summary>インベントリ参照を解決し、指定スロットのアイテムだけ返す</summary>
    public async UniTask<List<ItemData>> GetInventoryBySlotAsync(List<InventoryRef> refs, EquipmentSlot slot)
    {
        var allItems = await GetItemsAsync(refs);
        return allItems.Where(item => item.SlotType == slot).ToList();
    }

    /// <summary>キャッシュを1件クリア</summary>
    public void Invalidate(string job, string itemId)
    {
        _cache.Remove(CacheKey(job, itemId));
    }

    /// <summary>キャッシュを全クリア</summary>
    public void ClearCache()
    {
        _cache.Clear();
    }
}
