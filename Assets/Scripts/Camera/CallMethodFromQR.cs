using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Firebase.Firestore;

public class CallMethodFromQR : MonoBehaviour
{
    /// <summary>users/{uid} ドキュメント参照。全ユーザーデータがこの1ドキュメントに入っている</summary>
    private static DocumentReference UserDocRef =>
        FirebaseFirestore.DefaultInstance
            .Collection("users")
            .Document(UserDataManager.instance.UID);

    /// <summary>
    /// レベルアップ。characters マップの該当キャラの lv だけを1回の書き込みで更新する。
    /// 読み取りは発生しない（ローカルの UserData を直接更新する）。
    /// </summary>
    public async UniTask LevelUp(int upLevel)
    {
        var charIdx = UserDataManager.instance.CurrentSelectCharacterNumber;
        var chara = UserDataManager.instance.UserData.Characters[charIdx];
        int newLevel = chara.Level + upLevel;

        try
        {
            await UserDocRef.UpdateAsync(new Dictionary<FieldPath, object>
            {
                { new FieldPath("characters", charIdx.ToString(), "lv"), newLevel },
                { new FieldPath("lastDate"), Timestamp.GetCurrentTimestamp() },
            }).AsUniTask();

            chara.Level = newLevel;
            Debug.Log($"[QR] レベルアップ: {chara.Name} → Lv{newLevel}");
            AssetsDatabase.instance.PlayLevelUpSE(); // レベルアップSEを再生
            End();
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[QR] レベルアップ書き込みエラー: {ex.Message}");
            EndFromButton();
        }
    }

    public UniTask Atk()    => AddCouponAsync("atk_coupon",    d => d.ATKCoupon++,    "ATK");
    public UniTask Drink()  => AddCouponAsync("drink_coupon",  d => d.DrinkCoupon++,  "Drink");
    public UniTask Coffee() => AddCouponAsync("coffee_coupon", d => d.CoffeeCoupon++, "Coffee");
    public UniTask Five()   => AddCouponAsync("five_coupon",   d => d.FiveCoupon++,   "5");
    public UniTask Seven()  => AddCouponAsync("seven_coupon",  d => d.SevenCoupon++,  "7");

    /// <summary>
    /// クーポン入手の共通処理。
    /// FieldValue.Increment を使ったサーバー側加算1回だけで完結する
    /// （書き込み前の読み取り・書き込み後の再取得は行わない）。
    /// </summary>
    private async UniTask AddCouponAsync(string couponField, System.Action<UserData> applyLocal, string label)
    {
        try
        {
            await UserDocRef.UpdateAsync(new Dictionary<string, object>
            {
                { couponField, FieldValue.Increment(1) },
                { "lastDate", Timestamp.GetCurrentTimestamp() },
            }).AsUniTask();

            applyLocal(UserDataManager.instance.UserData);
            Debug.Log($"[QR] {label}クーポンを入手");
            End();
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[QR] クーポン書き込みエラー ({label}): {ex.Message}");
            EndFromButton();
        }
    }

    public async UniTask NewCharacter()
    {
        SceneLoader.instance.MergeScene("New");
    }

    /// <summary>QRコードのフレンド登録プレフィックス。形式: "friend:{uid}:{name}"</summary>
    public const string FRIEND_QR_PREFIX = "friend:";

    /// <summary>
    /// フレンド登録QRを読み取ったときの処理。
    /// 自分と相手の users ドキュメントの friends マップを
    /// 1つのバッチ書き込みで同時に更新する（アトミックなので
    /// 「片方だけフレンドになっている」状態は発生しない。
    /// 相手のドキュメントが存在しない不正QRならバッチごと失敗する）。
    /// 読み取りは発生しない。
    /// </summary>
    public async UniTask AddFriend(string qrText)
    {
        // "friend:{uid}:{name}" をパース（uidに':'は含まれない。名前は':'を含んでもよい）
        string payload = qrText.Substring(FRIEND_QR_PREFIX.Length);
        int sep = payload.IndexOf(':');
        if (sep <= 0 || sep == payload.Length - 1)
        {
            FriendMenuController.ShowToast("フレンドQRを読み取れませんでした");
            EndFromButton();
            return;
        }
        string friendUid  = payload.Substring(0, sep);
        string friendName = payload.Substring(sep + 1);

        var manager = UserDataManager.instance;
        var me = manager.UserData;

        if (friendUid == manager.UID)
        {
            FriendMenuController.ShowToast("自分のQRコードは登録できません");
            EndFromButton();
            return;
        }
        if (me.Friends.ContainsKey(friendUid))
        {
            FriendMenuController.ShowToast($"{friendName}さんとは既にフレンドです");
            EndFromButton();
            return;
        }

        try
        {
            var db = FirebaseFirestore.DefaultInstance;
            var now = Timestamp.GetCurrentTimestamp();
            var batch = db.StartBatch();

            // 相手のdocに自分を追加
            batch.Update(
                db.Collection("users").Document(friendUid),
                new Dictionary<FieldPath, object>
                {
                    { new FieldPath("friends", manager.UID), new Dictionary<string, object>
                        { { "name", me.Username ?? "" }, { "since", now } } },
                });
            // 自分のdocに相手を追加
            batch.Update(
                db.Collection("users").Document(manager.UID),
                new Dictionary<FieldPath, object>
                {
                    { new FieldPath("friends", friendUid), new Dictionary<string, object>
                        { { "name", friendName }, { "since", now } } },
                });
            await batch.CommitAsync().AsUniTask();

            // ローカルにも反映（再取得しない）
            me.Friends[friendUid] = new FriendEntry { Name = friendName, Since = now };

            Debug.Log($"[QR] フレンド登録: {friendName} ({friendUid})");
            AssetsDatabase.instance?.PlayLevelUpSE();
            FriendMenuController.ShowToast($"{friendName}さんとフレンドになりました！");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[QR] フレンド登録エラー: {ex.Message}");
            FriendMenuController.ShowToast("フレンド登録に失敗しました");
        }
        EndFromButton();
    }

    /// <summary>
    /// 来店時に専用QRを読み込んだときの入店時間記録。
    /// Firestoreは使わず、端末ローカル（LocalVisitLog）に来店履歴を1件追記する。
    /// </summary>
    public void CheckIn()
    {
        var record = LocalVisitLog.Record();
        Debug.Log($"[CheckIn] 入店時間をローカルに記録しました: {record.checkedInAt}");

        // 「ログイン情報を公開する」がONならフレンドに在店中が見えるよう共有する
        PresenceService.SetCheckedInAsync(true).Forget();

        // 左上の「ログイン中」バッジを更新
        PresenceIndicator.Refresh();

        EndFromButton();
    }

    /// <summary>
    /// 入退店兼用QR。1枚のQRでチェックイン／チェックアウトを切り替える。
    /// チェックイン中（未チェックアウトの来店あり）ならチェックアウト、
    /// そうでなければチェックインを記録する。
    /// </summary>
    public void ToggleVisit()
    {
        // チェックアウト忘れ（営業終了時刻超過）を先に自動クローズしておく
        LocalVisitLog.AutoCloseStaleVisits();

        if (LocalVisitLog.HasOpenVisit())
            CheckOut();   // 既にチェックイン中 → 退店
        else
            CheckIn();    // チェックインしていない → 入店
    }

    /// <summary>
    /// 退店時に専用QRを読み込んだときのチェックアウト記録。
    /// 直近のチェックインからの滞在時間（○時間○分）を計算し、ローカルに保存する。
    /// </summary>
    public void CheckOut()
    {
        var record = LocalVisitLog.CheckOut();

        // 共有していた在店状態を解除する
        PresenceService.SetCheckedInAsync(false).Forget();

        // 左上の「ログイン中」バッジを更新
        PresenceIndicator.Refresh();

        EndFromButton();

        if (record == null)
        {
            Debug.LogWarning("[CheckOut] チェックイン記録が無いため、チェックアウトを記録できませんでした");
            FriendMenuController.ShowToast("チェックイン記録が見つかりませんでした");
        }
        else
        {
            Debug.Log($"[CheckOut] チェックアウトを記録しました: 滞在 {record.stayText}");
            // QRカメラを閉じてから滞在時間のモーダルを表示する
            CheckOutModal.Show(record.stayText);
        }
    }

    /// <summary>
    /// QR表示を閉じてUIを更新する。
    /// ローカルの UserData は書き込み時に更新済みなので、
    /// Firestore からの再取得（旧 ReloadUserData.Reload）は行わない。
    /// </summary>
    public void End()
    {
        var qr = GameObject.FindWithTag("QR");
        if (qr != null) Destroy(qr.gameObject);

        var cpm = GameObject.FindObjectOfType<CharacterPageManager>();
        if (cpm != null) cpm.ChangePage();
    }

    public void EndFromButton()
    {
        var qr = GameObject.FindWithTag("QR");
        if (qr != null) Destroy(qr.gameObject);
    }
}
