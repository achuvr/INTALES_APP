using System;
using System.Globalization;

/// <summary>
/// お店の暦（惑星と来店のしくみで使う日付の決まり。2026-11 企画書「惑星と来店のしくみ 案1」）。
///  ・時刻はすべて日本時間で数える（端末のタイムゾーンに左右されない）
///  ・営業日: 26時閉店なので朝4時で日付を切り替える（深夜1時の入店は前の日の営業日）
///  ・定休日: 月・火・水（2026-11-06 ユーザー指定）
///  ・公転: 全員共通の2週間。木曜はじまり（2026-01-01 木 を0番目の公転の初日として数える）
///  ・季節: 春3〜5月・夏6〜8月・秋9〜11月・冬12〜2月
/// 入店QR（StoreCheckinQr）は今日の営業日のものしか読めないので、端末の日付を変えても曜日・季節・公転はごまかせない。
/// </summary>
public static class StoreCalendar
{
    public static readonly TimeSpan JST = TimeSpan.FromHours(9);
    /// <summary>営業日を切り替える時刻（日本時間の朝4時）</summary>
    public const int DAY_CHANGE_HOUR = 4;
    /// <summary>開店時刻（深夜0時からの分。相席募集の時間枠 RecruitBoardController.SLOT_MIN と同じ 12:00）</summary>
    public const int OPEN_MINUTES = 12 * 60;
    /// <summary>公転の長さ（日）</summary>
    public const int PERIOD_DAYS = 14;
    /// <summary>0番目の公転の初日（木曜）。公転の番号は 2026-11 の時点で 22 以上になる（0 は「記録なし」に使う）</summary>
    public static readonly DateTime ANCHOR = new DateTime(2026, 1, 1);

    public enum Season { Spring, Summer, Autumn, Winter }

    private static readonly string[] WEEK = { "日", "月", "火", "水", "木", "金", "土" };
    private static readonly string[] SEASON_NAMES = { "春", "夏", "秋", "冬" };

    /// <summary>UTC の時刻を日本時間に（Kind は Unspecified。日付・時刻の数字だけを使う）</summary>
    public static DateTime ToJst(DateTime utc) =>
        DateTime.SpecifyKind(utc.ToUniversalTime() + JST, DateTimeKind.Unspecified);

    public static DateTime NowJst => ToJst(DateTime.UtcNow);

    /// <summary>日本時間の時刻が属する営業日（朝4時より前は前の日）</summary>
    public static DateTime BusinessDate(DateTime jst) => jst.AddHours(-DAY_CHANGE_HOUR).Date;

    public static DateTime TodayBusiness => BusinessDate(NowJst);

    public static string DayKey(DateTime businessDate) =>
        businessDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string TodayKey => DayKey(TodayBusiness);

    public static bool IsClosed(DayOfWeek d) =>
        d == DayOfWeek.Monday || d == DayOfWeek.Tuesday || d == DayOfWeek.Wednesday;

    /// <summary>営業日が何番目の公転か</summary>
    public static int PeriodOf(DateTime businessDate)
    {
        int days = (int)Math.Floor((businessDate.Date - ANCHOR).TotalDays);
        return days >= 0 ? days / PERIOD_DAYS : -((-days + PERIOD_DAYS - 1) / PERIOD_DAYS);
    }

    public static int CurrentPeriod => PeriodOf(TodayBusiness);

    /// <summary>公転の初日（木曜）</summary>
    public static DateTime PeriodStart(int period) => ANCHOR.AddDays((double)period * PERIOD_DAYS);

    /// <summary>公転の最終日（2週間後の水曜）</summary>
    public static DateTime PeriodEnd(int period) => PeriodStart(period).AddDays(PERIOD_DAYS - 1);

    /// <summary>営業日を含む公転が終わるまでの日数（最終日なら 0）</summary>
    public static int DaysLeft(DateTime businessDate) =>
        (PeriodEnd(PeriodOf(businessDate)) - businessDate.Date).Days;

    public static Season SeasonOf(DateTime date)
    {
        int m = date.Month;
        if (m >= 3 && m <= 5) return Season.Spring;
        if (m >= 6 && m <= 8) return Season.Summer;
        if (m >= 9 && m <= 11) return Season.Autumn;
        return Season.Winter;
    }

    public static Season CurrentSeason => SeasonOf(TodayBusiness);

    public static string SeasonName(Season s) => SEASON_NAMES[(int)s];

    /// <summary>
    /// 満月のころ（満月の前後2日）の営業日か。平均の朔望月で近似する（実際の満月とのずれは半日ほど）。
    /// 前後2日（5日間）にしてあるので、定休日（月〜水の3日）に重なっても毎月どこかで来店できる
    /// </summary>
    public static bool IsFullMoonDay(DateTime businessDate)
    {
        // その営業日の21時（日本時間）＝ 12時（UTC）の月齢
        var utc = businessDate.Date.AddHours(21) - JST;
        double jd = utc.ToOADate() + 2415018.5; // OADate の 0 = 1899-12-30 0時 = ユリウス日 2415018.5
        const double SYNODIC = 29.530588853;
        double age = (jd - 2451550.1) % SYNODIC; // 2451550.1 = 2000-01-06 の新月
        if (age < 0) age += SYNODIC;
        return Math.Abs(age - SYNODIC * 0.5) <= 2.0;
    }

    public static string WeekdayName(DayOfWeek d) => WEEK[(int)d];

    /// <summary>「11月18日（水）」</summary>
    public static string MonthDayW(DateTime d) => $"{d.Month}月{d.Day}日（{WEEK[(int)d.DayOfWeek]}）";

    /// <summary>この暦が出しうる文字（NewHome の起動時にまとめてフォントへ登録する）</summary>
    public static string GlyphText => "月日（）" + string.Concat(WEEK) + string.Concat(SEASON_NAMES) + "0123456789";
}
