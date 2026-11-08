using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Firebase.Firestore;
using UnityEngine;

/// <summary>
/// 誕生日特典（2026-11-07 ユーザー指定）。誕生月（users.birth_month）になって最初にホームを開いたとき、
/// ドリンクチケットを1枚（1年に1回。users.birthday_gift_year にその年を入れる）。
/// 誕生月はアカウント作成のときに聞き、聞いていない人はホームの歯車メニューから1回だけ登録できる
/// </summary>
public static class BirthdayGift
{
    public const string FIELD = "drink_coupon";
    private static UserDataManager Manager => UserDataManager.instance;

    /// <summary>今月が誕生月で、今年の特典をまだ受け取っていない</summary>
    public static bool Due
    {
        get
        {
            var d = Manager?.UserData;
            if (d == null || string.IsNullOrEmpty(Manager.UID)) return false;
            var today = StoreCalendar.TodayBusiness;
            return d.BirthMonth == today.Month && d.BirthdayGiftYear != today.Year;
        }
    }

    public static async UniTask<bool> TakeAsync()
    {
        if (!Due) return false;
        var m = Manager;
        var d = m.UserData;
        int year = StoreCalendar.TodayBusiness.Year;
        int before = d.BirthdayGiftYear;
        d.BirthdayGiftYear = year; // 二重に受け取らないよう先に入れる（失敗したら戻す）
        try
        {
            await FirebaseFirestore.DefaultInstance.Collection("users").Document(m.UID).UpdateAsync(
                new Dictionary<string, object>
                {
                    { FIELD, FieldValue.Increment(1) },
                    { "birthday_gift_year", year },
                }).AsUniTask();
            d.DrinkCoupon++;
            LocalHistoryLog.Add("coupon", "ドリンクチケット を1枚 入手（誕生月の特典）");
            return true;
        }
        catch (Exception ex)
        {
            d.BirthdayGiftYear = before;
            Debug.LogWarning($"[Birthday] 誕生日特典の受け取りに失敗: {ex.Message}");
            return false;
        }
    }

    /// <summary>誕生月を登録する（まだ登録していない人だけ）。成功したら true</summary>
    public static async UniTask<bool> RegisterAsync(int month)
    {
        var m = Manager;
        if (m == null || string.IsNullOrEmpty(m.UID) || month < 1 || month > 12) return false;
        if (m.UserData.BirthMonth != 0) return false;
        try
        {
            await FirebaseFirestore.DefaultInstance.Collection("users").Document(m.UID).UpdateAsync(
                new Dictionary<string, object> { { "birth_month", month } }).AsUniTask();
            m.UserData.BirthMonth = month;
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Birthday] 誕生月の登録に失敗: {ex.Message}");
            return false;
        }
    }
}
