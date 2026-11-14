using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

/// <summary>
/// ミニオンをあげるQR（2026-11-14 ユーザー指定「管理画面で、指定した職業・属性・レベルのミニオンをあげるQRコードを発行できるように」）。
/// 管理者の「お客様に見せるQRコード」のクーポンタブの「ミニオンをあげる」で、職業・属性・レベルを決めて出す（SpaceAdminQrList）。
///  ・中身: "minion:{職業}:{属性}:{レベル}:{営業日 yyyyMMdd}:{番号}:{署名12桁}"。職業・属性・レベル・営業日・番号まで署名に入れるので、
///    書き換えたQRや自作のQRでは受け取れない（署名の鍵はアプリの中にあるので、G のQR・星座のQRと同じ程度の対策）
///  ・その営業日のうちだけ使える（朝4時まで）。同じQRは1人1回まで（同じ卓の人は1回ずつ読める）。
///    受け取った番号は users.minions.claims = { 番号: 営業日 } に残す（その日の分だけ。前の日の分は受け取るときに消す）
///  ・受け取ったミニオンは「お店からもらったミニオン」（MinionData.Gift）。自分で作ったミニオンと同じく作れる数に数える。
///    もらった時点で Lv.150 なら作れる数を増やさない（2026-11-14 ユーザー指定。もらってから育てて Lv.150 になれば増える）。
///    上限に届いていても受け取れる（そのあとは作れない）。きずな座が完成していない人も受け取れる（ミニオンの画面はきずな座の完成から）
/// 書き込みは MinionStore.ReceiveGiftAsync
/// </summary>
public static class MinionGiftQr
{
    public const string PREFIX = "minion:";
    private const string DAY_FORMAT = "yyyyMMdd";
    private const string SALT = "intales/minion-gift-qr/2026-11";

    public const string T_INVALID = "このQRコードは使えません";
    public const string T_EXPIRED = "このQRコードは今日のものではありません";
    public const string T_FAILED = "受け取れませんでした。通信状況を確かめて、もう一度読み取ってください";
    public const string T_ALREADY = "このQRコードはもう受け取りました";
    public const string T_ALREADY_SUB = "同じQRコードで受け取れるのは1人1回までです";
    public const string T_CAPTION = "MINION";
    public const string T_TITLE = "お店からミニオンが届きました！";
    private const string T_LEVEL = " Lv.";

    public static string GlyphText => T_INVALID + T_EXPIRED + T_FAILED + T_ALREADY + T_ALREADY_SUB + T_CAPTION + T_TITLE
                                      + T_LEVEL + "0123456789";

    public enum Result { Ok, Invalid, Expired }

    /// <summary>「炎の戦士 Lv.50」（お知らせ・管理者のQRの名前）</summary>
    public static string Title(int job, int element, int level) =>
        MinionRules.NameOf(job, element) + T_LEVEL + level.ToString(CultureInfo.InvariantCulture);

    // ================================================================
    // QR
    // ================================================================
    /// <summary>今日の営業日の、そのミニオンをあげる新しいQRの中身（出すたびに番号が変わる）</summary>
    public static string NewPayload(int job, int element, int level) =>
        Payload(Mathf.Clamp(job, 0, MinionRules.JOBS - 1), Mathf.Clamp(element, 0, MinionRules.ELEMENTS - 1),
            Mathf.Clamp(level, 1, MinionRules.MAX_LEVEL), StoreCalendar.TodayBusiness, Guid.NewGuid().ToString("N").Substring(0, 8));

    public static string Payload(int job, int element, int level, DateTime businessDate, string nonce)
    {
        string j = job.ToString(CultureInfo.InvariantCulture);
        string e = element.ToString(CultureInfo.InvariantCulture);
        string l = level.ToString(CultureInfo.InvariantCulture);
        string day = businessDate.ToString(DAY_FORMAT, CultureInfo.InvariantCulture);
        return PREFIX + j + ":" + e + ":" + l + ":" + day + ":" + nonce + ":" + Sign(j, e, l, day, nonce);
    }

    /// <summary>
    /// 読み取ったQRを確かめる。今日の営業日の正しいQRなら Ok（job・element・level はあげるミニオン、nonce はQRの番号）
    /// </summary>
    public static Result Parse(string text, DateTime nowUtc, out int job, out int element, out int level, out string nonce)
    {
        job = element = level = 0;
        nonce = null;
        if (string.IsNullOrEmpty(text)) return Result.Invalid;
        text = text.Trim();
        if (!text.StartsWith(PREFIX, StringComparison.Ordinal)) return Result.Invalid;
        var parts = text.Substring(PREFIX.Length).Split(':');
        if (parts.Length != 6) return Result.Invalid;
        string j = parts[0], e = parts[1], l = parts[2], dayText = parts[3], n = parts[4], sig = parts[5];
        if (!int.TryParse(j, NumberStyles.None, CultureInfo.InvariantCulture, out int jv) || jv < 0 || jv >= MinionRules.JOBS)
            return Result.Invalid;
        if (!int.TryParse(e, NumberStyles.None, CultureInfo.InvariantCulture, out int ev) || ev < 0 || ev >= MinionRules.ELEMENTS)
            return Result.Invalid;
        if (!int.TryParse(l, NumberStyles.None, CultureInfo.InvariantCulture, out int lv) || lv < 1 || lv > MinionRules.MAX_LEVEL)
            return Result.Invalid;
        if (string.IsNullOrEmpty(n) || n.Length > 32) return Result.Invalid;
        if (!DateTime.TryParseExact(dayText, DAY_FORMAT, CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            return Result.Invalid;
        if (!string.Equals(sig, Sign(j, e, l, dayText, n), StringComparison.OrdinalIgnoreCase)) return Result.Invalid;
        if (day.Date != StoreCalendar.BusinessDate(StoreCalendar.ToJst(nowUtc.ToUniversalTime()))) return Result.Expired;
        job = jv;
        element = ev;
        level = lv;
        nonce = n;
        return Result.Ok;
    }

    private static string Sign(string job, string element, string level, string day, string nonce)
    {
        using (var sha = SHA256.Create())
        {
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(SALT + "|" + job + "|" + element + "|" + level + "|" + day + "|" + nonce));
            var sb = new StringBuilder(12);
            for (int i = 0; i < 6; i++) sb.Append(bytes[i].ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }
    }
}
