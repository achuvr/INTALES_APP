using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using Firebase.Firestore;
using UnityEngine;

/// <summary>
/// ガチャ機能の中核。master/gacha の排出テーブルを読み（セッション内キャッシュ）、
/// 重み付き抽選 → GP消費と景品付与を users/{uid} への1回の書き込みで行う。
///
/// Firestore 構造（master/gacha）:
///   pools: {
///     standard: { name, cost_gp: 5, entries: [ { type, id, weight }, ... ] },
///     event:    { name, cost_gp: 0, entries: [...] },
///   }
/// cost_gp … 1回引くのに消費するGP。未設定なら旧 cost_lv の値をGPコストとして扱う。
/// entry.type:
///   "coupon" … id: atk / drink / coffee / five / seven
///   "item"   … id: master/items の item_id（選択中キャラのインベントリに追加）
/// 装備のダブりの扱いはプールで異なる:
///   standard … ダブりあり（所持済みでも排出される）。ただし所持品(inventory)への登録は1つだけで、
///              再排出時は GP に変換される（DUP_ITEM_GP。装備一覧の重複表示防止＋ハズレ感の軽減）
///   イベント（standard以外）… ダブりなし（所持済みの装備は抽選候補から除外される）。
///                              さらに選択中キャラの職業に合う装備（job一致 or common）だけが排出候補になる
///   スキルブックA/B … 例外として常にダブりあり。GP変換もせず、何冊でも所持品に追加される（重複所持可）
/// </summary>
public static class GachaService
{
    public const string STANDARD_POOL = "standard";
    public const string EVENT_POOL = "event";

    /// <summary>所持済みの装備が排出されたときに代わりに付与するGP。</summary>
    private const int DUP_ITEM_GP = 2;

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

    /// <summary>
    /// 全プール（poolId → プール定義）を返す。
    /// forceRefresh=true ならセッションキャッシュを破棄してFirestoreから読み直す
    /// （管理者のイベントQR表示など、最新のプール一覧が欲しいとき用）。
    /// </summary>
    public static async UniTask<Dictionary<string, GachaPool>> GetPoolsAsync(bool forceRefresh = false)
    {
        if (forceRefresh) Invalidate();
        var master = await GetMasterAsync();
        return master?.Pools ?? new Dictionary<string, GachaPool>();
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
    /// free=false のときはプール定義の cost_gp 分だけ所持GPを消費する
    /// （所持GPが足りない場合は引けない）。
    /// GP消費と景品付与は users/{uid} への1回の UpdateAsync にまとめる。
    /// </summary>
    public static async UniTask<GachaResult> DrawAsync(string poolId, bool free)
    {
        var pool = await GetPoolAsync(poolId);
        if (pool == null || pool.Entries == null || pool.Entries.Count == 0)
            return GachaResult.Fail("ガチャの設定が見つかりません");

        var manager = UserDataManager.instance;
        if (manager == null || string.IsNullOrEmpty(manager.UID))
            return GachaResult.Fail("ユーザーデータが読み込まれていません");

        int cost = free ? 0 : Mathf.Max(0, pool.Cost);
        if (cost > 0 && manager.UserData.GP < cost)
            return GachaResult.Fail($"GPが足りません（{cost}GP必要です）");

        // イベントガチャ（standard以外）は所持済みの装備を候補から外し（スキルブックA/Bは除く）、
        // 選択中キャラの職業に合う装備（job一致 or common）だけを出す
        bool excludeOwned = poolId != STANDARD_POOL;
        string jobFilter = excludeOwned ? SelectedCharacterJob(manager) : null;
        var candidates = pool.Entries
            .Where(e => IsGrantable(e, manager.UserData, excludeOwned, jobFilter)).ToList();
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
        int oldGp = manager.UserData.GP;
        if (cost > 0)
            updates[new FieldPath("gp")] = FieldValue.Increment(-cost);

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
                var entry = ItemCacheManager.instance.FindById(picked.Id);
                displayName = entry.name;
                // 装備の再排出（所持済み）: 重複登録せず GP に変換する（消費と合算して1回のIncrementに）。
                // スキルブックA/Bは何冊でも持てるため変換せず、そのまま所持品に追加する
                if (!IsSkillBook(picked.Id) && manager.UserData.Inventory.Any(r => r.ItemId == picked.Id))
                {
                    updates[new FieldPath("gp")] = FieldValue.Increment(-cost + DUP_ITEM_GP);
                    applyLocal = () => manager.UserData.GP += DUP_ITEM_GP;
                    subText = $"すでに所持しているため GP+{DUP_ITEM_GP} に変換されました";
                    break;
                }
                var newRef = new InventoryRef { Job = entry.job, ItemId = entry.item_id };
                // 所持品はアカウント単位（users/{uid}.inventory）。全キャラ共有。
                var inventoryData = manager.UserData.Inventory
                    .Concat(new[] { newRef })
                    .Select(r => (object)new Dictionary<string, object>
                    {
                        { "job",     r.Job    },
                        { "item_id", r.ItemId },
                    })
                    .ToList();
                updates[new FieldPath("inventory")] = inventoryData;
                applyLocal = () => manager.UserData.Inventory.Add(newRef);
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
        if (cost > 0) manager.UserData.GP = oldGp - cost;
        applyLocal();

        if (cost > 0)
            subText += $"\nGPを{cost}消費（{oldGp} → {manager.UserData.GP}）";

        Debug.Log($"[Gacha] {poolId}: {picked.Type}/{picked.Id} を入手（cost={cost}）");

        // ガチャ結果を履歴に記録（端末ローカル・最大50件）
        LocalHistoryLog.Add("gacha",
            $"{(string.IsNullOrEmpty(pool.Name) ? "ガチャ" : pool.Name)}で {displayName} を入手");

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

    /// <summary>
    /// ガチャをまとめて count 回引く（5連用）。
    /// 抽選は1回ずつ行う。standard は装備ダブりありなので同一バッチ内で同じ装備が複数回出うる
    /// （イベントガチャの装備はダブりなし。スキルブックA/Bはどのプールでもダブりあり）。
    /// GP消費と全景品の付与は users/{uid} への1回の UpdateAsync にまとめる
    /// （途中で通信が切れて一部だけ付与される事故を防ぐ）。
    /// 装備が尽きる等で count 回引けない場合は引けた分だけ確定し、GPもその分だけ消費する。
    /// </summary>
    public static async UniTask<GachaMultiResult> DrawManyAsync(string poolId, int count, bool free)
    {
        var pool = await GetPoolAsync(poolId);
        if (pool == null || pool.Entries == null || pool.Entries.Count == 0)
            return GachaMultiResult.Fail("ガチャの設定が見つかりません");

        var manager = UserDataManager.instance;
        if (manager == null || string.IsNullOrEmpty(manager.UID))
            return GachaMultiResult.Fail("ユーザーデータが読み込まれていません");

        int unitCost = free ? 0 : Mathf.Max(0, pool.Cost);
        if (unitCost > 0 && manager.UserData.GP < unitCost * count)
            return GachaMultiResult.Fail($"GPが足りません（{unitCost * count}GP必要です）");

        // ---- 抽選 ----
        // standard は装備ダブりありなので同一バッチ内でも候補から外さない。
        // イベントガチャは所持済み＋バッチ内で引いた装備を候補から外し（スキルブックA/Bは除く）、
        // 選択中キャラの職業に合う装備（job一致 or common）だけを出す
        bool excludeOwned = poolId != STANDARD_POOL;
        string jobFilter = excludeOwned ? SelectedCharacterJob(manager) : null;
        var picks = new List<GachaEntry>();
        var pickedItemIds = new HashSet<string>();
        for (int i = 0; i < count; i++)
        {
            var candidates = pool.Entries
                .Where(e => IsGrantable(e, manager.UserData, excludeOwned, jobFilter, pickedItemIds)).ToList();
            if (candidates.Count == 0) break; // 引けるものが尽きた（引けた分だけで確定する）
            var picked = WeightedPick(candidates);
            if (picked == null) break;
            picks.Add(picked);
            if (picked.Type == "item") pickedItemIds.Add(picked.Id);
        }
        if (picks.Count == 0)
            return GachaMultiResult.Fail("引ける景品がありません");

        int cost = unitCost * picks.Count;

        // ---- 書き込み内容の組み立て（1回のUpdateで全部反映する）----
        var updates = new Dictionary<FieldPath, object>
        {
            { new FieldPath("lastDate"), Timestamp.GetCurrentTimestamp() },
        };
        int oldGp = manager.UserData.GP;
        if (cost > 0)
            updates[new FieldPath("gp")] = FieldValue.Increment(-cost);

        var results = new List<GachaResult>();
        var applyLocals = new List<System.Action>();
        var couponCounts = new Dictionary<string, int>(); // Firestoreフィールド → 加算数
        var newRefs = new List<InventoryRef>();
        int dupGpTotal = 0; // 所持済み装備のGP変換の合計

        foreach (var picked in picks)
        {
            switch (picked.Type)
            {
                case "coupon":
                {
                    var coupon = CouponInfo(picked.Id);
                    couponCounts[coupon.field] =
                        couponCounts.TryGetValue(coupon.field, out int n) ? n + 1 : 1;
                    applyLocals.Add(() => coupon.applyLocal(manager.UserData));
                    results.Add(new GachaResult
                    {
                        Success = true, Type = picked.Type, Id = picked.Id,
                        DisplayName = coupon.nameJp,
                        SubText = "クーポンを1枚入手しました",
                        CostPaid = unitCost,
                    });
                    break;
                }
                case "item":
                {
                    var entry = ItemCacheManager.instance.FindById(picked.Id);
                    // 装備の再排出（所持済み・同一バッチ内で入手済み）: 重複登録せず GP に変換する。
                    // スキルブックA/Bは何冊でも持てるため変換せず、そのまま所持品に追加する
                    bool alreadyOwned = !IsSkillBook(picked.Id)
                                     && (manager.UserData.Inventory.Any(r => r.ItemId == picked.Id)
                                         || newRefs.Any(r => r.ItemId == picked.Id));
                    if (alreadyOwned)
                    {
                        dupGpTotal += DUP_ITEM_GP;
                        applyLocals.Add(() => manager.UserData.GP += DUP_ITEM_GP);
                    }
                    else
                    {
                        var newRef = new InventoryRef { Job = entry.job, ItemId = entry.item_id };
                        newRefs.Add(newRef);
                        applyLocals.Add(() => manager.UserData.Inventory.Add(newRef));
                    }
                    results.Add(new GachaResult
                    {
                        Success = true, Type = picked.Type, Id = picked.Id,
                        DisplayName = entry.name,
                        SubText = alreadyOwned
                            ? $"すでに所持しているため GP+{DUP_ITEM_GP} に変換されました"
                            : "装備メニューから確認できます",
                        CostPaid = unitCost,
                    });
                    break;
                }
                default:
                    return GachaMultiResult.Fail($"不明な景品タイプです: {picked.Type}");
            }
        }

        foreach (var kv in couponCounts)
            updates[new FieldPath(kv.Key)] = FieldValue.Increment(kv.Value);
        // GP変換分があれば消費と合算して1回のIncrementにまとめる
        if (dupGpTotal > 0)
            updates[new FieldPath("gp")] = FieldValue.Increment(-cost + dupGpTotal);
        if (newRefs.Count > 0)
        {
            // 所持品はアカウント単位（users/{uid}.inventory）。全キャラ共有。
            updates[new FieldPath("inventory")] = manager.UserData.Inventory
                .Concat(newRefs)
                .Select(r => (object)new Dictionary<string, object>
                {
                    { "job",     r.Job    },
                    { "item_id", r.ItemId },
                })
                .ToList();
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
            return GachaMultiResult.Fail("通信に失敗しました。もう一度お試しください");
        }

        // ローカルにも反映（再取得しない）
        if (cost > 0) manager.UserData.GP = oldGp - cost;
        foreach (var apply in applyLocals) apply();

        string poolName = string.IsNullOrEmpty(pool.Name) ? "ガチャ" : pool.Name;
        Debug.Log($"[Gacha] {poolId}: {picks.Count}連で入手（cost={cost}）");
        // ガチャ結果を履歴に記録（端末ローカル・最大50件）
        LocalHistoryLog.Add("gacha", picks.Count == 1
            ? $"{poolName}で {results[0].DisplayName} を入手"
            : $"{poolName}{picks.Count}連で {string.Join("、", results.Select(r => r.DisplayName))} を入手");

        return new GachaMultiResult
        {
            Success = true,
            Results = results,
            TotalCost = cost,
            OldGp = oldGp,
            NewGp = manager.UserData.GP,
        };
    }

    /// <summary>
    /// このエントリが現在の状態で付与可能か（定義不明は除外）。
    /// excludeOwnedItems=true（イベントガチャ）のとき、所持済みの装備と
    /// alreadyPicked（同一バッチ内で引いた装備ID）を候補から外す。
    /// jobFilter が指定されていれば、その職業の装備と共通("common")装備だけを候補に残す。
    /// スキルブックA/Bは所持済みでも候補に残る（何冊でも持てる。職業フィルタは適用される）。
    /// standard はダブりあり: 所持済みでも排出され、付与側でGPに変換される。
    /// </summary>
    private static bool IsGrantable(GachaEntry e, UserData user, bool excludeOwnedItems,
        string jobFilter = null, HashSet<string> alreadyPicked = null)
    {
        if (e == null || e.Weight <= 0 || string.IsNullOrEmpty(e.Id)) return false;
        switch (e.Type)
        {
            case "coupon":
                return CouponInfo(e.Id).field != null;
            case "item":
                // ItemSyncManager はシーン未配置で instance が常に null のため、実体の ItemCacheManager を見る
                var item = ItemCacheManager.instance?.FindById(e.Id);
                if (item == null) return false;
                // 職業フィルタ: 選択中キャラの職業の装備か共通装備だけを出す
                if (jobFilter != null && item.job != jobFilter && item.job != "common") return false;
                if (excludeOwnedItems && !IsSkillBook(e.Id))
                {
                    if (user.Inventory.Any(r => r.ItemId == e.Id)) return false;
                    if (alreadyPicked != null && alreadyPicked.Contains(e.Id)) return false;
                }
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// 選択中キャラクターの職業（イベントガチャの職業フィルタ用）。
    /// キャラがいない場合は ""（共通装備だけが候補に残る）。
    /// </summary>
    private static string SelectedCharacterJob(UserDataManager manager)
    {
        var chara = manager.UserData?.Characters?
            .ElementAtOrDefault(manager.CurrentSelectCharacterNumber);
        return chara?.Job ?? "";
    }

    /// <summary>スキルブック（A/B）か。スキルブックはどのプールでもダブり排出あり＆何冊でも重複所持できる。</summary>
    private static bool IsSkillBook(string itemId)
    {
        var item = ItemCacheManager.instance?.FindById(itemId);
        switch (item?.slot_type)
        {
            case "skill_book_a": case "skillA":
            case "skill_book_b": case "skillB":
                return true;
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

/// <summary>連続ガチャ（5連など）の結果</summary>
public class GachaMultiResult
{
    public bool Success;
    public string Error;
    /// <summary>引けた分の結果（装備が尽きた場合は依頼数より少ないことがある）</summary>
    public List<GachaResult> Results;
    /// <summary>実際に消費したGPの合計</summary>
    public int TotalCost;
    public int OldGp;
    public int NewGp;

    public static GachaMultiResult Fail(string error) =>
        new GachaMultiResult { Success = false, Error = error };
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

    /// <summary>1回引くのに消費するGP（0なら無料）</summary>
    [FirestoreProperty("cost_gp")]
    public int CostGp { get; set; }

    /// <summary>旧: 1回引くのに消費するレベル数。cost_gp 未設定時のGPコストとして流用する。</summary>
    [FirestoreProperty("cost_lv")]
    public int CostLv { get; set; }

    /// <summary>実際に消費するGP。cost_gp があればそれ、無ければ旧 cost_lv の値を使う。</summary>
    public int Cost => CostGp > 0 ? CostGp : CostLv;

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
