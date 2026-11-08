using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

/// <summary>
/// 入店・退店のQR（1日1回変わる。2026-11-06 ユーザー指定）。お店の端末（管理者の「お客様に見せるQRコード」の
/// 「入店」タブ）に出し、お客様のアプリのカメラで読み取ってもらう。入店中に読むと退店になる（1枚で入退店を切り替える）。
///
/// 中身: "checkin:{営業日 yyyyMMdd}:{署名12桁}"。営業日は StoreCalendar と同じく朝4時に切り替わる。
///  ・署名の合わないQR（手作り）は受け付けない
///  ・今日の営業日のQRでなければ受け付けない（前の日に撮った写真では入店できない）
///  ・同じ日のうちは同じQRなので、その日に撮った写真でも読めてしまう（1日1回の切り替えはユーザーの判断）
/// 署名の鍵はアプリの中にあるので、アプリを解析されれば作れてしまう（惑星QR・クーポンQRと同じ程度の対策）。
/// 以前の固定の入店QR（"checkin" / "visit"）では入店できない（退店だけはできる）。
/// </summary>
public static class StoreCheckinQr
{
    public const string PREFIX = "checkin:";
    private const string DAY_FORMAT = "yyyyMMdd";
    private const string SALT = "intales/store-checkin/day/2026-11";

    public enum Result { Ok, Invalid, Expired }

    /// <summary>今日の営業日（日本時間・朝4時切り替え）</summary>
    public static DateTime Today => StoreCalendar.TodayBusiness;

    /// <summary>その営業日のQRの中身</summary>
    public static string Payload(DateTime businessDate)
    {
        string day = businessDate.ToString(DAY_FORMAT, CultureInfo.InvariantCulture);
        return PREFIX + day + ":" + Sign(day);
    }

    /// <summary>
    /// 読み取ったQRを確かめる。今日の営業日の正しいQRなら Ok（visitUtc は読み取った時刻）。
    /// 別の日のQRは Expired、署名の合わないものは Invalid
    /// </summary>
    public static Result Parse(string text, DateTime nowUtc, out DateTime visitUtc)
    {
        visitUtc = default;
        if (string.IsNullOrEmpty(text)) return Result.Invalid;
        text = text.Trim();
        if (!text.StartsWith(PREFIX, StringComparison.Ordinal)) return Result.Invalid;
        var parts = text.Substring(PREFIX.Length).Split(':');
        if (parts.Length != 2) return Result.Invalid;
        if (!DateTime.TryParseExact(parts[0], DAY_FORMAT, CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            return Result.Invalid;
        if (!string.Equals(parts[1], Sign(parts[0]), StringComparison.OrdinalIgnoreCase)) return Result.Invalid;

        var utc = nowUtc.ToUniversalTime();
        if (day.Date != StoreCalendar.BusinessDate(StoreCalendar.ToJst(utc))) return Result.Expired;
        visitUtc = utc;
        return Result.Ok;
    }

    private static string Sign(string day)
    {
        using (var sha = SHA256.Create())
        {
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(SALT + "|" + day));
            var sb = new StringBuilder(12);
            for (int i = 0; i < 6; i++) sb.Append(bytes[i].ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }
    }
}
