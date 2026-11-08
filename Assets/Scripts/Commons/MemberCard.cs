using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

/// <summary>
/// 会員証QR（お会計用。2026-11-07 ユーザー指定）。お客様が所持品の「会員証」で出し、スタッフ（管理者アカウント）が
/// ホームのカメラで読むと、そのお客様のパス・クーポン・★お店の特典・誕生月が一画面でわかる（SpaceMemberView）。
///
/// 中身: "member:{uid}:{中央の惑星ID}:{★特典の状態}:{営業日 yyyyMMdd}:{署名12桁}"
///  ・中央の惑星と★特典の状態はお客様の端末にしかないので、QRに入れて渡す（PerkState の番号）
///  ・営業日の入ったQRなので、前の日のスクリーンショットでは読めない（同じ日のうちは読める）
///  ・署名の鍵はアプリの中にある（入店QR・惑星QRと同じ程度の対策）
/// </summary>
public static class MemberCard
{
    public const string PREFIX = "member:";
    private const string DAY_FORMAT = "yyyyMMdd";
    private const string SALT = "intales/member-card/2026-11";

    public enum Result { Ok, Invalid, Expired }

    public sealed class Info
    {
        public string Uid;
        public string PlanetId;
        public PerkState Perk;
    }

    public static string Payload(string uid, string planetId, PerkState perk)
    {
        string day = StoreCalendar.TodayBusiness.ToString(DAY_FORMAT, CultureInfo.InvariantCulture);
        string body = $"{uid}:{planetId}:{(int)perk}:{day}";
        return PREFIX + body + ":" + Sign(body);
    }

    public static Result Parse(string text, out Info info)
    {
        info = null;
        if (string.IsNullOrEmpty(text)) return Result.Invalid;
        text = text.Trim();
        if (!text.StartsWith(PREFIX, StringComparison.Ordinal)) return Result.Invalid;
        var parts = text.Substring(PREFIX.Length).Split(':');
        if (parts.Length != 5) return Result.Invalid;
        string body = $"{parts[0]}:{parts[1]}:{parts[2]}:{parts[3]}";
        if (!string.Equals(parts[4], Sign(body), StringComparison.OrdinalIgnoreCase)) return Result.Invalid;
        if (!int.TryParse(parts[2], out int perk)) return Result.Invalid;
        if (!DateTime.TryParseExact(parts[3], DAY_FORMAT, CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            return Result.Invalid;
        if (day.Date != StoreCalendar.TodayBusiness.Date) return Result.Expired;
        info = new Info { Uid = parts[0], PlanetId = parts[1], Perk = (PerkState)perk };
        return Result.Ok;
    }

    private static string Sign(string body)
    {
        using (var sha = SHA256.Create())
        {
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(SALT + "|" + body));
            var sb = new StringBuilder(12);
            for (int i = 0; i < 6; i++) sb.Append(bytes[i].ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }
    }
}
