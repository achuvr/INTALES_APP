using UnityEngine;

/// <summary>
/// ボス討伐の「1日1回」制限を端末ローカル（PlayerPrefs）に記録するユーティリティ。
/// アカウント(UID)ごとに最後に戦った日付(yyyy-MM-dd)を保存し、同じ日の再戦をブロックする。
/// 日付の切り替わり（リセット）は日本時間の午前2時。
/// 管理者（AdminClaim.IsAdmin）の免除は呼び出し側（BossBattleController）で行う。
/// 記録は「1D100を振って後戻りできなくなった時点」で付ける（開いて閉じただけでは消費しない）。
/// </summary>
public static class DailyBattleLimit
{
    private const string KeyPrefix = "boss_daily_battle_date_";

    private static string Key => KeyPrefix + (UserDataManager.instance != null ? UserDataManager.instance.UID : "local");

    /// <summary>ゲーム内の「今日」。JST(UTC+9)の午前2時を切り替わりとするため、
    /// UTC+9h−2h = UTC+7h の日付を使う。端末のタイムゾーン設定に依存しない。</summary>
    private static string Today => System.DateTime.UtcNow.AddHours(7).ToString("yyyy-MM-dd");

    /// <summary>今日すでに戦ったか（日付が変われば自動的に false に戻る）。</summary>
    public static bool HasBattledToday => PlayerPrefs.GetString(Key, "") == Today;

    /// <summary>今日戦ったことを記録する。</summary>
    public static void RecordBattle()
    {
        PlayerPrefs.SetString(Key, Today);
        PlayerPrefs.Save();
    }
}