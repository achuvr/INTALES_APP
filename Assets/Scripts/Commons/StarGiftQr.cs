using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Cysharp.Threading.Tasks;
using Firebase.Firestore;
using UnityEngine;

/// <summary>
/// 星座に星を灯すQR（2026-11-13 ユーザー指定「管理者用機能に、指定した星座に指定した個数分灯す機能を作って」）。
/// 管理者の「お客様に見せるQRコード」のゲームタブの「星座に星を灯す」で、星座と個数を決めて出す（SpaceAdminQrList）。
///  ・中身: "stars:{星座ID}:{個数}:{営業日 yyyyMMdd}:{番号}:{署名12桁}"。星座・個数・営業日・番号まで署名に入れるので、
///    書き換えたQRや自作のQRでは灯らない（署名の鍵はアプリの中にあるので、G のQR・公認QRと同じ程度の対策）
///  ・その営業日のうちだけ使える（朝4時まで）。同じQRは1人1回まで（同じ卓の人は1回ずつ読める）
///  ・保存: users.star_gifts = { 番号: { id: 星座ID, count: 個数, day: 灯した営業日 "yyyy-MM-dd" } }。
///    灯した星の記録なので消さない（同じQRの2回目もこれで分かる）。users は本人が書けるのでルール変更は不要
///  ・灯した星は、アチーブメント（公認）の星と同じく星座の進み具合に入る（Constellations.Progress。ゲームはない星）。
///    「星を灯せるのは1日1回」（公認QR。BoardGameCertify.LitToday）とは別に数える
///  ・この星で星座が完成・レベルアップしたら、ふだんと同じくその星座のチケット（1つ上がるごとに1枚。ふつうはドリンク、職人座は7%OFF）
/// </summary>
public static class StarGiftQr
{
    public const string PREFIX = "stars:";
    /// <summary>users のフィールド名（QRの番号 → { id, count, day }）</summary>
    public const string FIELD = "star_gifts";
    /// <summary>1枚のQRで灯せる星の数の範囲</summary>
    public const int MIN = 1, MAX = 30;
    /// <summary>星座の進み具合（ConstellationProgress.Games）の中で、この星を表すキーの頭（ゲームのキーと見分ける）</summary>
    public const string KEY_PREFIX = "gift:";
    private const string DAY_FORMAT = "yyyyMMdd";
    private const string SALT = "intales/star-gift-qr/2026-11";

    public const string T_INVALID = "このQRコードは使えません";
    public const string T_EXPIRED = "このQRコードは今日のものではありません";
    public const string T_FAILED = "星を灯せませんでした。通信状況を確かめて、もう一度読み取ってください";
    public const string T_ALREADY = "このQRコードはもう読み取りました";
    public const string T_ALREADY_SUB = "同じQRコードで星を灯せるのは1人1回までです";
    public const string T_CAPTION = "STARS";
    public const string T_TITLE = "お店から星が届きました！";
    private const string T_NAME_A = "に 星";
    private const string T_NAME_B = "つ";

    public static string GlyphText => T_INVALID + T_EXPIRED + T_FAILED + T_ALREADY + T_ALREADY_SUB + T_CAPTION + T_TITLE
                                      + T_NAME_A + T_NAME_B + "0123456789";

    public enum Result { Ok, Invalid, Expired }
    public enum ClaimResult { Ok, Already, Failed }

    /// <summary>「山札座に 星3つ」（お知らせ・管理者のQRの名前）</summary>
    public static string Title(ConstellationDef def, int count) =>
        (def != null ? def.Name : "") + T_NAME_A + count.ToString(CultureInfo.InvariantCulture) + T_NAME_B;

    /// <summary>今の星の数 before に count 個足すと、いくつレベルが上がるか（完成も1つと数える。星座の星の数 size ごとに1つ）</summary>
    public static int LevelsGained(int before, int count, int size) =>
        size <= 0 || count <= 0 ? 0 : (Math.Max(0, before) + count) / size - Math.Max(0, before) / size;

    // ================================================================
    // QR
    // ================================================================
    /// <summary>今日の営業日の、星座 def に星を count 個灯す新しいQRの中身（出すたびに番号が変わる）</summary>
    public static string NewPayload(ConstellationDef def, int count) =>
        Payload(def.Id, Mathf.Clamp(count, MIN, MAX), StoreCalendar.TodayBusiness, Guid.NewGuid().ToString("N").Substring(0, 8));

    public static string Payload(string id, int count, DateTime businessDate, string nonce)
    {
        string c = count.ToString(CultureInfo.InvariantCulture);
        string day = businessDate.ToString(DAY_FORMAT, CultureInfo.InvariantCulture);
        return PREFIX + id + ":" + c + ":" + day + ":" + nonce + ":" + Sign(id, c, day, nonce);
    }

    /// <summary>
    /// 読み取ったQRを確かめる。今日の営業日の正しいQRなら Ok（def は星座、count は灯す数、nonce はQRの番号）
    /// </summary>
    public static Result Parse(string text, DateTime nowUtc, out ConstellationDef def, out int count, out string nonce)
    {
        def = null;
        count = 0;
        nonce = null;
        if (string.IsNullOrEmpty(text)) return Result.Invalid;
        text = text.Trim();
        if (!text.StartsWith(PREFIX, StringComparison.Ordinal)) return Result.Invalid;
        var parts = text.Substring(PREFIX.Length).Split(':');
        if (parts.Length != 5) return Result.Invalid;
        string id = parts[0], c = parts[1], dayText = parts[2], n = parts[3], sig = parts[4];
        var found = Array.Find(Constellations.All, d => d.Id == id);
        if (found == null) return Result.Invalid;
        if (!int.TryParse(c, NumberStyles.None, CultureInfo.InvariantCulture, out int value) || value < MIN || value > MAX)
            return Result.Invalid;
        if (string.IsNullOrEmpty(n) || n.Length > 32) return Result.Invalid;
        if (!DateTime.TryParseExact(dayText, DAY_FORMAT, CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            return Result.Invalid;
        if (!string.Equals(sig, Sign(id, c, dayText, n), StringComparison.OrdinalIgnoreCase)) return Result.Invalid;
        if (day.Date != StoreCalendar.BusinessDate(StoreCalendar.ToJst(nowUtc.ToUniversalTime()))) return Result.Expired;
        def = found;
        count = value;
        nonce = n;
        return Result.Ok;
    }

    private static string Sign(string id, string count, string day, string nonce)
    {
        using (var sha = SHA256.Create())
        {
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(SALT + "|" + id + "|" + count + "|" + day + "|" + nonce));
            var sb = new StringBuilder(12);
            for (int i = 0; i < 6; i++) sb.Append(bytes[i].ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }
    }

    // ================================================================
    // 記録
    // ================================================================
    /// <summary>お店から灯した星の1回分（QR1枚分）</summary>
    public struct Gift
    {
        public string Nonce;
        public string Id;
        public int Count;
        public string Day;
    }

    /// <summary>星座の進み具合の中で、お店から灯した星を表すキー（ゲームのキーと重ならない）</summary>
    public static string KeyOf(string nonce) => KEY_PREFIX + nonce;

    /// <summary>星座の進み具合の中のキーが、お店から灯した星のものか</summary>
    public static bool IsGiftKey(string key) => key != null && key.StartsWith(KEY_PREFIX, StringComparison.Ordinal);

    /// <summary>今までにお店から灯した星（users.star_gifts。読めないものは飛ばす）</summary>
    public static List<Gift> Gifts
    {
        get
        {
            var list = new List<Gift>();
            var data = UserDataManager.instance != null ? UserDataManager.instance.UserData : null;
            if (data?.StarGiftMap == null) return list;
            foreach (var kv in data.StarGiftMap)
            {
                if (!(kv.Value is Dictionary<string, object> m)) continue;
                string id = m.TryGetValue("id", out var i) ? i as string : null;
                string day = m.TryGetValue("day", out var d) ? d as string ?? "" : "";
                int count = 0;
                try
                {
                    if (m.TryGetValue("count", out var c) && c != null) count = Convert.ToInt32(c, CultureInfo.InvariantCulture);
                }
                catch
                {
                    count = 0;
                }
                if (string.IsNullOrEmpty(id) || count <= 0) continue;
                list.Add(new Gift { Nonce = kv.Key, Id = id, Count = Math.Min(count, MAX), Day = day });
            }
            return list;
        }
    }

    // ================================================================
    // 受け取る
    // ================================================================
    /// <summary>
    /// QR（番号 nonce）の星を灯す。users を1回読むトランザクションで、もう読んだ番号なら Already。
    /// rewards は、この星で完成・レベルアップする星座のチケット（フィールド名 → 枚数。呼ぶ側で LevelsGained から決める）。
    /// 星の記録とチケットは1回の書き込みで一緒に入る。メモリ上の UserData にも反映する
    /// </summary>
    public static async UniTask<ClaimResult> ClaimAsync(ConstellationDef def, int count, string nonce,
        IReadOnlyDictionary<string, int> rewards)
    {
        var m = UserDataManager.instance;
        if (m == null || string.IsNullOrEmpty(m.UID) || m.UserData == null || def == null || string.IsNullOrEmpty(nonce)
            || count < MIN || count > MAX)
            return ClaimResult.Failed;
        string today = StoreCalendar.TodayKey; // トランザクションは裏のスレッドで動くので先に読んでおく
        var db = FirebaseFirestore.DefaultInstance;
        var doc = db.Collection("users").Document(m.UID);
        ClaimResult result = ClaimResult.Failed;
        Dictionary<string, object> kept = null;
        try
        {
            await db.RunTransactionAsync(tx => tx.GetSnapshotAsync(doc).ContinueWith(t =>
            {
                var snap = t.Result;
                var u = snap != null && snap.Exists ? snap.ToDictionary() : new Dictionary<string, object>();
                var gifts = new Dictionary<string, object>();
                if (u.TryGetValue(FIELD, out var raw) && raw is Dictionary<string, object> old)
                    foreach (var kv in old) gifts[kv.Key] = kv.Value;
                kept = gifts;
                if (gifts.ContainsKey(nonce))
                {
                    result = ClaimResult.Already;
                    return false;
                }
                gifts[nonce] = new Dictionary<string, object> { { "id", def.Id }, { "count", (long)count }, { "day", today } };
                var updates = new Dictionary<string, object>
                {
                    { FIELD, gifts },
                    { "lastDate", Timestamp.GetCurrentTimestamp() },
                };
                if (rewards != null)
                    foreach (var kv in rewards)
                        if (kv.Value > 0) updates[kv.Key] = FieldValue.Increment(kv.Value);
                tx.Update(doc, updates);
                result = ClaimResult.Ok;
                return true;
            })).AsUniTask();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[StarGiftQr] 星を灯せませんでした ({def.Id} ×{count}): {ex.Message}");
            return ClaimResult.Failed;
        }

        var data = m.UserData;
        if (kept != null) data.StarGiftMap = kept; // もう読んでいた（ほかの端末で読んだ）ときも、手元の記録をそろえる
        if (result == ClaimResult.Ok)
        {
            if (rewards != null)
                foreach (var kv in rewards)
                {
                    var coupon = SpaceCoupons.ByField(kv.Key);
                    if (coupon != null && kv.Value > 0) coupon.Add(data, kv.Value);
                }
            JourneyService.NotifyChanged();
        }
        return result;
    }
}
