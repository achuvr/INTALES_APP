using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using Firebase.Firestore;
using UnityEngine;

/// <summary>
/// ガチャ機能の中核。master/gacha の排出テーブルを読み（セッション内キャッシュ）、
/// 重み付き抽選 → レベル消費と景品付与を users/{uid} への1回の書き込みで行う。
///
/// Firestore 構造（master/gacha）:
///   pools: {
///     standard: { name, cost_lv: 5, entries: [ { type, id, weight }, ... ] },
///     event:    { name, cost_lv: 0, entries: [...] },
///   }
/// entry.type:
///   "coupon" … id: atk / drink / coffee / five / seven
///   "item"   … id: master/items の item_id（選択中キャラのインベントリに追加）
/// 既に所持している装備は抽選から自動的に除外される（ダブりなし）。
/// </summary>
public static class GachaService
{
    public const string STANDARD_POOL = "standard";
    public const string EVENT_POOL = "event";

    private static GachaMaster _master;
    private static UniTaskCompletionSource<GachaMaster> _loadingTcs;

    // ================================================================
    // 排出テーブルの取得（MasterData と同じセッションキャッシュ方式）
    // ================================================================
    public static async UniTask<GachaPool> GetPoolAsync(string poolId)
    {
        var master = await GetMasterAsync();
        if (master?.Pools == null) return null;
        return master.Pools.TryGetValue(poolId, out var pool) ? pool : null;
    }

    private static async UniTask<GachaMaster> GetMasterAsync()
    {
        if (_master != null) return _master;
        if (_loadingTcs != null) return await _loadingTcs.Task;

        _loadingTcs = new UniTaskCompletionSource<GachaMaster>();
        try
        {
            var db = FirebaseFirestore.DefaultInstance;
            var snap = await db.Collection("master").Document("gacha")
                .GetSnapshotAsync().AsUniTask();
            _master = snap.Exists ? snap.ConvertTo<GachaMaster>() : new GachaMaster();
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[Gacha] master/gacha 取得エラー: {ex.Message}");
            _master = new GachaMaster();
        }

        _loadingTcs.TrySetResult(_master);
        return _master;
    }

    /// <summary>キャッシュを破棄して次回取得時に再読み込みさせる（デバッグ用）</summary>
    public static void Invalidate()
    {
        _master = null;
        _loadingTcs = null;
    }

    // ================================================================
    // 抽選＋付与
    // ================================================================

    /// <summary>
    /// ガチャを1回引く。
    /// free=false のときはプール定義の cost_lv 分だけ選択中キャラのレベルを消費する
    /// （引いたあとのレベルが1未満になる場合は引けない）。
    /// レベル消費と景品付与は users/{uid} への1回の UpdateAsync にまとめる。
    /// </summary>
    public static async UniTask<GachaResult> DrawAsync(string poolId, bool free)
    {
        var pool = await GetPoolAsync(poolId);
        if (pool == null || pool.Entries == null || pool.Entries.Count == 0)
            return GachaResult.Fail("ガチャの設定が見つかりません");

        var manager = UserDataManager.instance;
        if (manager == null || string.IsNullOrEmpty(manager.UID))
            return GachaResult.Fail("ユーザーデータが読み込まれていません");

        int charIdx = manager.CurrentSelectCharacterNumber;
        var characters = manager.UserData.Characters;
        if (characters == null || charIdx >= characters.Count)
            return GachaResult.Fail("キャラクターが見つかりません");
        var chara = characters[charIdx];

        int cost = free ? 0 : Mathf.Max(0, pool.CostLv);
        if (cost > 0 && chara.Level - cost < 1)
            return GachaResult.Fail($"レベルが足りません（Lv{cost + 1}以上で引けます）");

        // 既に持っている装備は候補から外す
        var candidates = pool.Entries.Where(e => IsGrantable(e, chara)).ToList();
        if (candidates.Count == 0)
            return GachaResult.Fail("引ける景品がありません");

        var picked = WeightedPick(candidates);
        if (picked == null)
            return GachaResult.Fail("ガチャの設定が正しくありません");

        // ---- 書き込み内容の組み立て（1回のUpdateで全部反映する）----
        var updates = new Dictionary<FieldPath, object>
        {
            { new FieldPath("lastDate"), Timestamp.GetCurrentTimestamp() },
        };
        int oldLevel = chara.Level;
        if (cost > 0)
            updates[new FieldPath("characters", charIdx.ToString(), "lv")] = oldLevel - cost;

        System.Action applyLocal;
        string displayName, subText;

        switch (picked.Type)
        {
            case "coupon":
            {
                var coupon = CouponInfo(picked.Id);
                updates[new FieldPath(coupon.field)] = FieldValue.Increment(1);
                applyLocal = () => coupon.applyLocal(manager.UserData);
                displayName = coupon.nameJp;
                subText = "クーポンを1枚入手しました";
                break;
            }
            case "item":
            {
                var entry = ItemSyncManager.instance.FindById(picked.Id);
                var newRef = new InventoryRef { Job = entry.job, ItemId = entry.itemId };
                var inventoryData = chara.Inventory
                    .Concat(new[] { newRef })
                    .Select(r => (object)new Dictionary<string, object>
                    {
                        { "job",     r.Job    },
                        { "item_id", r.ItemId },
                    })
                    .ToList();
                updates[new FieldPath("characters", charIdx.ToString(), "inventory")] = inventoryData;
                applyLocal = () => chara.Inventory.Add(newRef);
                displayName = entry.name;
                subText = "装備メニューから確認できます";
                break;
            }
            default:
                return GachaResult.Fail($"不明な景品タイプです: {picked.Type}");
        }

        try
        {
            var docRef = FirebaseFirestore.DefaultInstance
                .Collection("users").Document(manager.UID);
            await docRef.UpdateAsync(updates).AsUniTask();
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[Gacha] 書き込みエラー: {ex.Message}");
            return GachaResult.Fail("通信に失敗しました。もう一度お試しください");
        }

        // ローカルにも反映（再取得しない）
        if (cost > 0) chara.Level = oldLevel - cost;
        applyLocal();

        if (cost > 0)
            subText += $"\nレベルを{cost}消費（Lv{oldLevel} → Lv{chara.Level}）";

        Debug.Log($"[Gacha] {poolId}: {picked.Type}/{picked.Id} を入手（cost={cost}）");
        return new GachaResult
        {
            Success = true,
            Type = picked.Type,
            Id = picked.Id,
            DisplayName = displayName,
            SubText = subText,
            CostPaid = cost,
        };
    }

    /// <summary>このエントリが現在の状態で付与可能か（所持済み・定義不明は除外）</summary>
    private static bool IsGrantable(GachaEntry e, Character chara)
    {
        if (e == null || e.Weight <= 0 || string.IsNullOrEmpty(e.Id)) return false;
        switch (e.Type)
        {
            case "coupon":
                return CouponInfo(e.Id).field != null;
            case "item":
                return ItemSyncManager.instance?.FindById(e.Id) != null
                    && !chara.Inventory.Any(r => r.ItemId == e.Id);
            default:
                return false;
        }
    }

    private static GachaEntry WeightedPick(List<GachaEntry> entries)
    {
        int total = entries.Sum(e => e.Weight);
        if (total <= 0) return null;
        int roll = Random.Range(0, total); // [0, total)
        foreach (var e in entries)
        {
            roll -= e.Weight;
            if (roll < 0) return e;
        }
        return entries[entries.Count - 1];
    }

    /// <summary>クーポンID → Firestoreフィールド・表示名・ローカル反映</summary>
    private static (string field, string nameJp, System.Action<UserData> applyLocal) CouponInfo(string id)
    {
        switch (id)
        {
            case "atk":    return ("atk_coupon",    "ATKクーポン",     d => d.ATKCoupon++);
            case "drink":  return ("drink_coupon",  "ドリンククーポン", d => d.DrinkCoupon++);
            case "coffee": return ("coffee_coupon", "コーヒークーポン", d => d.CoffeeCoupon++);
            case "five":   return ("five_coupon",   "5%OFFクーポン",   d => d.FiveCoupon++);
            case "seven":  return ("seven_coupon",  "7%OFFクーポン",   d => d.SevenCoupon++);
            default:       return (null, null, null);
        }
    }
}

/// <summary>ガチャ1回の結果</summary>
public class GachaResult
{
    public bool Success;
    public string Error;
    public string Type;
    public string Id;
    public string DisplayName;
    public string SubText;
    public int CostPaid;

    public static GachaResult Fail(string error) =>
        new GachaResult { Success = false, Error = error };
}

// ================================================================
// master/gacha のFirestoreモデル
// ================================================================

[FirestoreData, System.Serializable]
public class GachaMaster
{
    [FirestoreProperty("pools")]
    public Dictionary<string, GachaPool> Pools { get; set; }
}

[FirestoreData, System.Serializable]
public class GachaPool
{
    [FirestoreProperty("name")]
    public string Name { get; set; }

    /// <summary>1回引くのに消費するレベル数（0なら無料）</summary>
    [FirestoreProperty("cost_lv")]
    public int CostLv { get; set; }

    [FirestoreProperty("entries")]
    public List<GachaEntry> Entries { get; set; }
}

[FirestoreData, System.Serializable]
public class GachaEntry
{
    /// <summary>"coupon" / "item"</summary>
    [FirestoreProperty("type")]
    public string Type { get; set; }

    [FirestoreProperty("id")]
    public string Id { get; set; }

    /// <summary>排出の重み（プール内の合計に対する割合で当選確率が決まる）</summary>
    [FirestoreProperty("weight")]
    public int Weight { get; set; }
}
