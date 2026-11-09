using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Firebase.Firestore;
using UnityEngine;

/// <summary>
/// 友だち紹介（2026-11-07 ユーザー指定）。
///   1. 紹介する人が、ホームの歯車メニュー「友だちを紹介」で紹介QR（"invite:{uid}:{名前}"）を出す
///   2. まだ来店したことのない人が、ホームのカメラでそのQRを読む → 紹介を受け付ける
///      （referrals/{読んだ人のUID} を作り、users.referred_by に紹介した人のUID）
///   3. 読んだ人が初めて入店すると、読んだ人にドリンクチケット1枚（referrals の status を visited に）
///   4. 紹介した人のアプリが visited の紹介を見つけたら、紹介した人にもドリンクチケット1枚（claimed を true に）
/// ほかの人の users には書けないので、それぞれが自分の分を自分で受け取る。referrals の形はルールで決めている。
/// 「来店したことがない」はアプリ側の判定（来店回数が0、または今日が初めての来店）
/// </summary>
public static class Referral
{
    public const string QR_PREFIX = "invite:";
    private const string COLLECTION = "referrals";
    private const string STATUS_JOINED = "joined";
    private const string STATUS_VISITED = "visited";

    public const string T_ACCEPTED_A = "さんの紹介を受け付けました！";
    public const string T_ACCEPTED_B = "\nはじめて来店すると、2人にドリンクチケットをプレゼント";
    public const string T_SELF = "自分の紹介QRは読めません";
    public const string T_ALREADY = "すでに紹介を受け付けています";
    public const string T_VISITED = "紹介の特典は、まだ来店したことのない方だけです";
    public const string T_INVALID = "読み取れない紹介QRです";
    public const string T_FAILED = "通信に失敗しました。もう一度読み取ってください";
    public const string T_NO_ACCOUNT = "ログインしてから読み取ってください";

    public static string GlyphText => T_ACCEPTED_A + T_ACCEPTED_B + T_SELF + T_ALREADY + T_VISITED + T_INVALID + T_FAILED
                                      + T_NO_ACCOUNT;

    private static FirebaseFirestore Db => FirebaseFirestore.DefaultInstance;

    /// <summary>紹介で初めて来店したとき、紹介した人・された人の両方が解放する惑星（2026-11-09 フレンドの条件から変更）</summary>
    public const string PLANET = "opalia";

    /// <summary>直前の受け取りで、オパーリアを新しく解放したか（ホームが解放のお知らせを出したら false に戻す）</summary>
    public static bool PlanetJustUnlocked;

    private static void AddPlanetLocal(UserData d)
    {
        if (d.Planets.Contains(PLANET)) return;
        d.Planets.Add(PLANET);
        PlanetJustUnlocked = true;
        SpacePlanets.MarkNews();
        LocalHistoryLog.Add("planet", "惑星「オパーリア」を解放（友だち紹介）");
    }
    private static UserDataManager Manager => UserDataManager.instance;

    public static string Payload(string uid, string name) => QR_PREFIX + uid + ":" + (name ?? "");

    /// <summary>まだ来店したことがない（または今日が初めての来店）か</summary>
    private static bool IsNewcomer()
    {
        int visits = JourneyService.DisplayVisits;
        return visits == 0 || (visits == 1 && JourneyService.InStoreToday);
    }

    /// <summary>紹介QRを読んだ。結果のメッセージを返す</summary>
    public static async UniTask<string> AcceptAsync(string qrText)
    {
        var m = Manager;
        if (m == null || string.IsNullOrEmpty(m.UID)) return T_NO_ACCOUNT;
        var d = m.UserData;
        if (string.IsNullOrEmpty(qrText) || !qrText.StartsWith(QR_PREFIX, StringComparison.Ordinal)) return T_INVALID;
        var parts = qrText.Substring(QR_PREFIX.Length).Split(new[] { ':' }, 2);
        string from = parts[0].Trim();
        string fromName = parts.Length > 1 ? parts[1] : "";
        if (string.IsNullOrEmpty(from)) return T_INVALID;
        if (from == m.UID) return T_SELF;
        if (!string.IsNullOrEmpty(d.ReferredBy)) return T_ALREADY;
        if (!IsNewcomer()) return T_VISITED;

        try
        {
            var batch = Db.StartBatch();
            batch.Set(Db.Collection(COLLECTION).Document(m.UID), new Dictionary<string, object>
            {
                { "from", from },
                { "name", d.Username ?? "" },
                { "at", FieldValue.ServerTimestamp },
                { "status", STATUS_JOINED },
                { "claimed", false },
            });
            batch.Update(Db.Collection("users").Document(m.UID), new Dictionary<string, object>
            {
                { "referred_by", from },
            });
            await batch.CommitAsync().AsUniTask();
            d.ReferredBy = from;
            LocalHistoryLog.Add("friend", $"{fromName}さんの紹介を受け付けた");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Referral] 紹介の受け付けに失敗: {ex.Message}");
            return T_FAILED;
        }
        return SpaceTheme.NoParse(SpaceTheme.Safe(fromName)) + T_ACCEPTED_A + T_ACCEPTED_B;
    }

    /// <summary>紹介されて来た人が入店しているなら、自分の特典（ドリンクチケット1枚）を受け取る。受け取ったら true</summary>
    public static bool CanTakeNewcomerGift
    {
        get
        {
            var d = Manager?.UserData;
            return d != null && !string.IsNullOrEmpty(d.ReferredBy) && !d.ReferralRewarded && JourneyService.InStoreToday;
        }
    }

    public static async UniTask<bool> TakeNewcomerGiftAsync()
    {
        var m = Manager;
        if (m == null || string.IsNullOrEmpty(m.UID) || !CanTakeNewcomerGift) return false;
        var d = m.UserData;
        d.ReferralRewarded = true; // 二重に受け取らないよう先に立てる（失敗したら戻す）
        try
        {
            var batch = Db.StartBatch();
            batch.Update(Db.Collection(COLLECTION).Document(m.UID), new Dictionary<string, object>
            {
                { "status", STATUS_VISITED },
                { "visited_at", FieldValue.ServerTimestamp },
            });
            batch.Update(Db.Collection("users").Document(m.UID), new Dictionary<string, object>
            {
                { "drink_coupon", FieldValue.Increment(1) },
                { "referral_rewarded", true },
                { "planets", FieldValue.ArrayUnion(PLANET) }, // 惑星オパーリアの解放（2026-11-09）
            });
            await batch.CommitAsync().AsUniTask();
            d.DrinkCoupon++;
            AddPlanetLocal(d);
            LocalHistoryLog.Add("coupon", "ドリンクチケット を1枚 入手（紹介で来店した特典）");
            return true;
        }
        catch (Exception ex)
        {
            d.ReferralRewarded = false;
            Debug.LogWarning($"[Referral] 紹介の特典の受け取りに失敗: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 自分が紹介した人のうち、来店してくれた人の分の特典（1人につきドリンクチケット1枚）を受け取る。
    /// 受け取った人の名前を返す（なければ空）
    /// </summary>
    public static async UniTask<List<string>> ClaimReferrerGiftsAsync()
    {
        var names = new List<string>();
        var m = Manager;
        if (m == null || string.IsNullOrEmpty(m.UID)) return names;
        try
        {
            var snap = await Db.Collection(COLLECTION)
                .WhereEqualTo("from", m.UID)
                .WhereEqualTo("status", STATUS_VISITED)
                .WhereEqualTo("claimed", false)
                .GetSnapshotAsync().AsUniTask();
            foreach (var doc in snap.Documents)
            {
                var batch = Db.StartBatch();
                batch.Update(doc.Reference, new Dictionary<string, object> { { "claimed", true } });
                batch.Update(Db.Collection("users").Document(m.UID), new Dictionary<string, object>
                {
                    { "drink_coupon", FieldValue.Increment(1) },
                    { "planets", FieldValue.ArrayUnion(PLANET) }, // 惑星オパーリアの解放（2026-11-09）
                });
                await batch.CommitAsync().AsUniTask();
                m.UserData.DrinkCoupon++;
                AddPlanetLocal(m.UserData);
                string name = doc.TryGetValue("name", out string n) ? n : "";
                names.Add(name);
                LocalHistoryLog.Add("coupon", $"ドリンクチケット を1枚 入手（{name}さんの紹介の特典）");
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Referral] 紹介した人の特典の確認に失敗: {ex.Message}");
        }
        return names;
    }
}
