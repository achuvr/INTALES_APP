using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
#if MOBILE_NOTIFICATIONS
using Unity.Notifications;
#endif

/// <summary>
/// お知らせの通知（2026-11-07 ユーザー指定「両方」）。
///
/// ・お店からの通知（Firebase Cloud Messaging）: アプリは "all" という話題（topic）を購読する。
///   送るのは tools/firestore/send-notice.js（管理者のパソコンから。新しいゲーム・イベントなど）。
///   アプリを開いているときに届いたものは、画面の下に知らせる（トースト）。
///   iPhone に届けるには、Apple の APNs 認証キーを Firebase の管理画面に登録しておく必要がある。
/// ・スマホの中で予約する通知（Unity Mobile Notifications。パッケージがあるときだけ MOBILE_NOTIFICATIONS が定義される）:
///   ホームを開くたびに予約をし直す。
///     - 今の公転にまだ来ていなければ、公転の最後の土曜 11時「今の公転は明日の日曜まで」
///     - 次の公転のはじまり（木曜）11時「新しい公転がはじまりました」
///     - 誕生月を登録していれば、次の誕生月の1日 10時「お誕生月です。アプリに贈りもの」
/// 時刻はお店の暦（日本時間）で決め、端末の時刻に直して予約する。
/// </summary>
public static class AppNotifications
{
    public const string TOPIC_ALL = "all";
    /// <summary>テスト送信用。管理者アカウントでホームを開いたスマホだけが購読する</summary>
    public const string TOPIC_STAFF = "staff";
    private const int ID_ORBIT_LAST = 1001;
    private const int ID_ORBIT_NEW = 1002;
    private const int ID_BIRTHDAY = 1003;

    public const string T_ORBIT_LAST_TITLE = "今の公転は明日の日曜まで";
    public const string T_ORBIT_LAST_BODY = "来店すると公転が続きます。流れ星も待っています";
    public const string T_ORBIT_NEW_TITLE = "新しい公転がはじまりました";
    public const string T_ORBIT_NEW_BODY = "今回の公転の最初の来店で、流れ星が流れます";
    public const string T_BIRTHDAY_TITLE = "お誕生月おめでとうございます！";
    public const string T_BIRTHDAY_BODY = "アプリを開くと、お店からの贈りものが届きます";

    private static bool _initialized;

    /// <summary>ホームを開いたときに呼ぶ（2回目以降は予約のし直しだけ）</summary>
    public static void Start()
    {
        if (!_initialized)
        {
            _initialized = true;
            InitLocal();
            InitPush();
        }
        Reschedule();
    }

    // ================================================================
    // お店からの通知（FCM）
    // ================================================================
    private static void InitPush()
    {
        try
        {
            Firebase.Messaging.FirebaseMessaging.TokenReceived += (s, e) =>
                Debug.Log("[Notice] 通知のトークンを受け取りました");
            Firebase.Messaging.FirebaseMessaging.MessageReceived += (s, e) =>
            {
                // アプリを開いているときに届いた通知は、OS の通知としては出ないので画面で知らせる
                var n = e.Message?.Notification;
                if (n == null) return;
                string text = string.IsNullOrEmpty(n.Body) ? n.Title : $"{n.Title}\n{n.Body}";
                if (string.IsNullOrEmpty(text)) return;
                UniTask.Post(() =>
                {
                    if (SpaceTheme.InUse) SpaceTheme.Toast(SpaceTheme.SafeNow(text), SpaceTheme.MainCanvas());
                });
            };
            SubscribeAsync().Forget();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Notice] お店からの通知の準備に失敗: {ex.Message}");
        }
    }

    private static async UniTaskVoid SubscribeAsync()
    {
        try
        {
            await Firebase.Messaging.FirebaseMessaging.RequestPermissionAsync().AsUniTask();
            await Firebase.Messaging.FirebaseMessaging.SubscribeAsync(TOPIC_ALL).AsUniTask();
            Debug.Log("[Notice] お店からの通知を購読しました");
            // 管理者のスマホはテスト送信（パソコンの送信画面の「テスト」）も受け取る
            await AdminClaim.RefreshAsync();
            if (AdminClaim.IsAdmin) await Firebase.Messaging.FirebaseMessaging.SubscribeAsync(TOPIC_STAFF).AsUniTask();
            else await Firebase.Messaging.FirebaseMessaging.UnsubscribeAsync(TOPIC_STAFF).AsUniTask();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Notice] お店からの通知の購読に失敗: {ex.Message}");
        }
    }

    // ================================================================
    // スマホの中で予約する通知
    // ================================================================
    private static void InitLocal()
    {
#if MOBILE_NOTIFICATIONS && !UNITY_EDITOR
        try
        {
            var args = NotificationCenterArgs.Default;
            args.AndroidChannelId = "intales_default";
            args.AndroidChannelName = "お店からのお知らせ";
            args.AndroidChannelDescription = "公転・誕生月・お店からのお知らせ";
            NotificationCenter.Initialize(args);
            NotificationCenter.RequestPermission();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Notice] 予約する通知の準備に失敗: {ex.Message}");
        }
#endif
    }

    /// <summary>予約をし直す（公転・誕生月が変わったときにも呼べる）</summary>
    public static void Reschedule()
    {
#if MOBILE_NOTIFICATIONS && !UNITY_EDITOR
        try
        {
            NotificationCenter.CancelScheduledNotification(ID_ORBIT_LAST);
            NotificationCenter.CancelScheduledNotification(ID_ORBIT_NEW);
            NotificationCenter.CancelScheduledNotification(ID_BIRTHDAY);
            var now = DateTime.Now;

            int period = StoreCalendar.CurrentPeriod;
            // 公転の最後の土曜（木曜はじまりの2週間の10日目）。今の公転にまだ来ていなければ
            if (JourneyService.Journey != null && !JourneyService.VisitedThisOrbit)
                Schedule(ID_ORBIT_LAST, T_ORBIT_LAST_TITLE, T_ORBIT_LAST_BODY,
                    JstToLocal(StoreCalendar.PeriodStart(period).AddDays(9).AddHours(11)), now);
            // 次の公転のはじまり（木曜）
            Schedule(ID_ORBIT_NEW, T_ORBIT_NEW_TITLE, T_ORBIT_NEW_BODY,
                JstToLocal(StoreCalendar.PeriodStart(period + 1).AddHours(11)), now);

            // 次の誕生月の1日
            var d = UserDataManager.instance != null ? UserDataManager.instance.UserData : null;
            if (d != null && d.BirthMonth >= 1 && d.BirthMonth <= 12)
            {
                var today = StoreCalendar.TodayBusiness;
                var first = new DateTime(today.Year, d.BirthMonth, 1).AddHours(10);
                if (JstToLocal(first) <= now) first = first.AddYears(1);
                Schedule(ID_BIRTHDAY, T_BIRTHDAY_TITLE, T_BIRTHDAY_BODY, JstToLocal(first), now);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Notice] 通知の予約に失敗: {ex.Message}");
        }
#endif
    }

    /// <summary>日本時間の日時を、この端末の時刻に直す</summary>
    private static DateTime JstToLocal(DateTime jst) =>
        DateTime.SpecifyKind(jst - StoreCalendar.JST, DateTimeKind.Utc).ToLocalTime();

#if MOBILE_NOTIFICATIONS && !UNITY_EDITOR
    private static void Schedule(int id, string title, string body, DateTime fireLocal, DateTime now)
    {
        if (fireLocal <= now) return;
        var n = new Notification { Identifier = id, Title = title, Text = body };
        NotificationCenter.ScheduleNotification(n, new NotificationDateTimeSchedule(fireLocal));
    }
#endif
}
