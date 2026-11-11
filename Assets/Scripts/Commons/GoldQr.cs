using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Cysharp.Threading.Tasks;
using Firebase.Firestore;
using UnityEngine;

/// <summary>
/// G（アプリ内の通貨。IgCurrency）を渡すQR（2026-11-12 ユーザー指定「管理者のクーポンをあげるQRコードを出す所に、
/// お金をゲットするためのQRも出せるように。こちらで指定した額を出せるように」）。
/// 管理者の「お客様に見せるQRコード」のクーポンタブの「G を渡す」で金額を決めて出す（SpaceAdminQrList）。
///  ・中身: "gold:{金額}:{営業日 yyyyMMdd}:{番号}:{署名12桁}"。金額・営業日・番号まで署名に入れるので、
///    数字を書き換えたQRや自作のQRでは受け取れない（署名の鍵はアプリの中にあるので、入店QR・公認QRと同じ程度の対策）
///  ・その営業日のうちだけ使える（朝4時まで）。同じQRは1人1回まで（同じ卓の人は1回ずつ読める）。
///    クーポンのQRと違い、同じ人が何度も読んでも増えない（受け取った番号を users.gold_claims = { 番号: 営業日 } に残す。
///    残すのはその日の分だけで、前の日の分は受け取るときに消す）
///  ・金貨座が完成していない人も受け取れる（2026-11-12 ユーザー指定。最初は受け取れないようにしていた）。
///    持っている G は所持品に出る（IgCurrency.Visible）が、ショップと来店でもらえる G は金貨座の完成から
/// </summary>
public static class GoldQr
{
    public const string PREFIX = "gold:";
    /// <summary>users のフィールド名（その日に受け取ったQRの番号 → 営業日 "yyyy-MM-dd"）</summary>
    public const string FIELD_CLAIMS = "gold_claims";
    /// <summary>1枚のQRで渡せる金額の範囲</summary>
    public const int MIN = 1, MAX = 999;
    private const string DAY_FORMAT = "yyyyMMdd";
    private const string SALT = "intales/gold-qr/2026-11";

    public const string T_INVALID = "このQRコードは使えません";
    public const string T_EXPIRED = "このQRコードは今日のものではありません";
    public const string T_FAILED = "受け取れませんでした。通信状況を確かめて、もう一度読み取ってください";
    public const string T_ALREADY = "このQRコードはもう受け取りました";
    public const string T_ALREADY_SUB = "同じQRコードで受け取れるのは1人1回までです";
    private const string T_HISTORY_A = "QRコードで ";
    private const string T_HISTORY_B = " を受け取った";

    public static string GlyphText => T_INVALID + T_EXPIRED + T_FAILED + T_ALREADY + T_ALREADY_SUB
                                      + T_HISTORY_A + T_HISTORY_B + IgCurrency.GlyphText + IgCurrency.T_LOCKED_NOTE;

    public enum Result { Ok, Invalid, Expired }
    public enum ClaimResult { Ok, Already, Failed }

    // ================================================================
    // QR
    // ================================================================
    /// <summary>今日の営業日の、amount G を渡す新しいQRの中身（出すたびに番号が変わる）</summary>
    public static string NewPayload(int amount) =>
        Payload(Mathf.Clamp(amount, MIN, MAX), StoreCalendar.TodayBusiness, Guid.NewGuid().ToString("N").Substring(0, 8));

    public static string Payload(int amount, DateTime businessDate, string nonce)
    {
        string a = amount.ToString(CultureInfo.InvariantCulture);
        string day = businessDate.ToString(DAY_FORMAT, CultureInfo.InvariantCulture);
        return PREFIX + a + ":" + day + ":" + nonce + ":" + Sign(a, day, nonce);
    }

    /// <summary>読み取ったQRを確かめる。今日の営業日の正しいQRなら Ok（amount は金額、nonce はQRの番号）</summary>
    public static Result Parse(string text, DateTime nowUtc, out int amount, out string nonce)
    {
        amount = 0;
        nonce = null;
        if (string.IsNullOrEmpty(text)) return Result.Invalid;
        text = text.Trim();
        if (!text.StartsWith(PREFIX, StringComparison.Ordinal)) return Result.Invalid;
        var parts = text.Substring(PREFIX.Length).Split(':');
        if (parts.Length != 4) return Result.Invalid;
        string a = parts[0], dayText = parts[1], n = parts[2], sig = parts[3];
        if (!int.TryParse(a, NumberStyles.None, CultureInfo.InvariantCulture, out int value) || value < MIN || value > MAX)
            return Result.Invalid;
        if (string.IsNullOrEmpty(n) || n.Length > 32) return Result.Invalid;
        if (!DateTime.TryParseExact(dayText, DAY_FORMAT, CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            return Result.Invalid;
        if (!string.Equals(sig, Sign(a, dayText, n), StringComparison.OrdinalIgnoreCase)) return Result.Invalid;
        if (day.Date != StoreCalendar.BusinessDate(StoreCalendar.ToJst(nowUtc.ToUniversalTime()))) return Result.Expired;
        amount = value;
        nonce = n;
        return Result.Ok;
    }

    private static string Sign(string amount, string day, string nonce)
    {
        using (var sha = SHA256.Create())
        {
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(SALT + "|" + amount + "|" + day + "|" + nonce));
            var sb = new StringBuilder(12);
            for (int i = 0; i < 6; i++) sb.Append(bytes[i].ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }
    }

    // ================================================================
    // 受け取る
    // ================================================================
    /// <summary>
    /// QR（番号 nonce）の amount G を受け取る。users を1回読むトランザクションで、その日にもう受け取った番号なら Already。
    /// balance は受け取ったあとの所持金（Already のときは今の所持金）
    /// </summary>
    public static async UniTask<(ClaimResult result, int balance)> ClaimAsync(int amount, string nonce)
    {
        var m = UserDataManager.instance;
        if (m == null || string.IsNullOrEmpty(m.UID) || m.UserData == null || string.IsNullOrEmpty(nonce))
            return (ClaimResult.Failed, 0);
        string today = StoreCalendar.TodayKey; // トランザクションは裏のスレッドで動くので先に読んでおく
        var db = FirebaseFirestore.DefaultInstance;
        var doc = db.Collection("users").Document(m.UID);
        ClaimResult result = ClaimResult.Failed;
        long balance = 0;
        Dictionary<string, object> kept = null;
        try
        {
            await db.RunTransactionAsync(tx => tx.GetSnapshotAsync(doc).ContinueWith(t =>
            {
                var snap = t.Result;
                var u = snap != null && snap.Exists ? snap.ToDictionary() : new Dictionary<string, object>();
                long now = Math.Max(0, Long(u, IgCurrency.FIELD));
                // その日に受け取った番号だけ残す（前の日の分は消す）
                var claims = new Dictionary<string, object>();
                if (u.TryGetValue(FIELD_CLAIMS, out var raw) && raw is Dictionary<string, object> old)
                    foreach (var kv in old)
                        if (kv.Value != null && kv.Value.ToString() == today) claims[kv.Key] = today;
                if (claims.ContainsKey(nonce))
                {
                    result = ClaimResult.Already;
                    balance = now;
                    return false;
                }
                claims[nonce] = today;
                balance = now + amount;
                kept = claims;
                tx.Update(doc, new Dictionary<string, object>
                {
                    { IgCurrency.FIELD, balance },
                    { FIELD_CLAIMS, claims },
                    { "lastDate", Timestamp.GetCurrentTimestamp() },
                });
                result = ClaimResult.Ok;
                return true;
            })).AsUniTask();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[GoldQr] 受け取れませんでした ({amount}G): {ex.Message}");
            return (ClaimResult.Failed, IgCurrency.Balance);
        }

        var d = m.UserData;
        d.Ig = (int)balance;
        if (result == ClaimResult.Ok)
        {
            d.GoldClaims = kept;
            LocalHistoryLog.Add(IgCurrency.HISTORY, T_HISTORY_A + IgCurrency.Format(amount) + T_HISTORY_B);
            JourneyService.NotifyChanged();
        }
        return (result, (int)balance);
    }

    private static long Long(Dictionary<string, object> m, string k)
    {
        if (m == null || !m.TryGetValue(k, out var v) || v == null) return 0;
        try { return Convert.ToInt64(v); }
        catch { return 0; }
    }
}
