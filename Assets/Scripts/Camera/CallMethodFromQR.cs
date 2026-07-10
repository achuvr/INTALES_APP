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
        var chars = UserDataManager.instance.UserData.Characters;
        if (chars == null || charIdx >= chars.Count)
        {
            // キャラ未作成（アカウント作成時に「あとで作る」を選んだ場合）
            FriendMenuController.ShowToast("キャラクターがいません\nバッグのキャラクターチケットで作成できます");
            EndFromButton();
            return;
        }
        var chara = chars[charIdx];
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
            await CharacterTicketService.CheckAndGrantAsync(); // Lv50到達ごとのキャラクターチケット
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
    /// バフカード山札QR。形式: "deck:soft=10,beer=5,sweets=1,..."。
    /// 読むと枚数を重みに1枚抽選して手札に保持し、次のボス戦で自動発動する。
    /// </summary>
    public void DrawBuffCard(string qrText)
    {
        var counts = BuffCard.DecodeDeck(qrText);
        var drawn = BuffCard.DrawFrom(counts);
        if (drawn == null)
        {
            FriendMenuController.ShowToast("山札が空、または無効なカードQRです");
            EndFromButton();
            return;
        }

        BuffCard.SetHeld(drawn.Value); // 1枚だけ保持（上書き）
        Debug.Log($"[QR] バフカード「{BuffCard.DisplayName(drawn.Value)}」を引いた");
        AssetsDatabase.instance?.PlayLevelUpSE();
        End();
        BuffCardModal.Show(drawn.Value);
    }

    /// <summary>
    /// イベントガチャQRのプレフィックス。
    /// "gacha_event" → master/gacha の event プール、
    /// "gacha_event:{プールID}" → 指定プール（イベントごとに変えたい場合用）。
    /// </summary>
    public const string GACHA_EVENT_QR_PREFIX = "gacha_event";

    /// <summary>
    /// イベントガチャQRを読み取ったときの処理。無料で1回引ける。
    /// 同じQRを読むたびに引ける（来店していないと読めないQRであることが前提）。
    /// </summary>
    public async UniTask EventGacha(string qrText)
    {
        // "gacha_event:xxx" ならプールIDを取り出す（なければ "event"）
        string poolId = GachaService.EVENT_POOL;
        int sep = qrText.IndexOf(':');
        if (sep >= 0 && sep < qrText.Length - 1)
            poolId = qrText.Substring(sep + 1).Trim();

        try
        {
            var result = await GachaService.DrawAsync(poolId, free: true);
            if (!result.Success)
            {
                FriendMenuController.ShowToast(result.Error);
                EndFromButton();
                return;
            }

            Debug.Log($"[QR] イベントガチャ: {result.Type}/{result.Id} を入手 ({poolId})");
            AssetsDatabase.instance?.PlayLevelUpSE();
            End();
            InfoModal.Show("イベントガチャ", result.DisplayName, result.SubText);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[QR] イベントガチャエラー: {ex.Message}");
            FriendMenuController.ShowToast("ガチャに失敗しました");
            EndFromButton();
        }
    }

    /// <summary>紙の会員証引き継ぎQRのプレフィックス。形式: "transfer:{引き継ぎコード}"</summary>
    public const string TRANSFER_QR_PREFIX = "transfer:";

    /// <summary>
    /// 紙の会員証の引き継ぎQRを読み取ったときの処理。
    /// 店側が CardTransfer シーンで発行した transfers/{code} を読み（1read）、
    /// キャラクター追加と使用済みマークを1つのバッチで書き込む（アトミック）。
    /// コードは一度使うと使用済みになり、二重引き継ぎはできない。
    /// </summary>
    public async UniTask ClaimTransfer(string qrText)
    {
        string code = qrText.Substring(TRANSFER_QR_PREFIX.Length).Trim();
        if (string.IsNullOrEmpty(code))
        {
            FriendMenuController.ShowToast("引き継ぎQRを読み取れませんでした");
            EndFromButton();
            return;
        }

        var manager = UserDataManager.instance;
        var db = FirebaseFirestore.DefaultInstance;
        var transferRef = db.Collection("transfers").Document(code);

        try
        {
            var snap = await transferRef.GetSnapshotAsync().AsUniTask();
            if (!snap.Exists)
            {
                FriendMenuController.ShowToast("引き継ぎコードが見つかりません");
                EndFromButton();
                return;
            }
            if (snap.TryGetValue("claimed", out bool claimed) && claimed)
            {
                FriendMenuController.ShowToast("この引き継ぎコードは使用済みです");
                EndFromButton();
                return;
            }

            string charaName = snap.TryGetValue("name", out string n) ? n : "";
            string job       = snap.TryGetValue("job", out string j) ? j : "warrior";
            string el        = snap.TryGetValue("el", out string e) ? e : "fire";
            int    level     = snap.TryGetValue("lv", out int lv) ? lv : 1;
            int fiveCoupon  = snap.TryGetValue("five_coupon", out int f5) ? f5 : 0;
            int sevenCoupon = snap.TryGetValue("seven_coupon", out int f7) ? f7 : 0;
            int drinkCoupon = snap.TryGetValue("drink_coupon", out int fd) ? fd : 0;

            int newIndex = manager.UserData.Characters.Count;
            var characterData = new Dictionary<string, object>
            {
                { "name", charaName },
                { "job", job },
                { "el", el },
                { "lv", level },
            };

            // キャラクター追加・クーポン加算・使用済みマークを同時に書き込む
            var userUpdates = new Dictionary<FieldPath, object>
            {
                { new FieldPath("characters", newIndex.ToString()), characterData },
            };
            if (fiveCoupon > 0)
                userUpdates[new FieldPath("five_coupon")] = FieldValue.Increment(fiveCoupon);
            if (sevenCoupon > 0)
                userUpdates[new FieldPath("seven_coupon")] = FieldValue.Increment(sevenCoupon);
            if (drinkCoupon > 0)
                userUpdates[new FieldPath("drink_coupon")] = FieldValue.Increment(drinkCoupon);

            var batch = db.StartBatch();
            batch.Update(db.Collection("users").Document(manager.UID), userUpdates);
            batch.Update(transferRef, new Dictionary<FieldPath, object>
            {
                { new FieldPath("claimed"), true },
                { new FieldPath("claimed_by"), manager.UID },
                { new FieldPath("claimed_at"), Timestamp.GetCurrentTimestamp() },
            });
            await batch.CommitAsync().AsUniTask();

            // ローカルにも反映（再取得しない）
            var chara = new Character
            {
                Name = charaName, Job = job, Element = el, Level = level,
            };
            if (manager.UserData.CharactersMap == null)
                manager.UserData.CharactersMap = new Dictionary<string, Character>();
            manager.UserData.CharactersMap[newIndex.ToString()] = chara;
            manager.UserData.BuildCharacterList();
            manager.UserData.FiveCoupon  += fiveCoupon;
            manager.UserData.SevenCoupon += sevenCoupon;
            manager.UserData.DrinkCoupon += drinkCoupon;

            // 引き継いだキャラは何も装備していない状態で始める
            // （装備はキャラ番号キーの端末保存のため、過去に同じ番号だったキャラの装備が残っている）
            LocalEquipSave.ClearCharacter(newIndex);

            Debug.Log($"[QR] 会員証引き継ぎ完了: {charaName} Lv{level} クーポン5%×{fiveCoupon}/7%×{sevenCoupon}/ドリンク×{drinkCoupon} ({code})");
            AssetsDatabase.instance?.PlayLevelUpSE();
            End();

            var couponParts = new List<string>();
            if (fiveCoupon > 0)  couponParts.Add($"5%OFF×{fiveCoupon}");
            if (sevenCoupon > 0) couponParts.Add($"7%OFF×{sevenCoupon}");
            if (drinkCoupon > 0) couponParts.Add($"ドリンク×{drinkCoupon}");
            string sub = "紙の会員証のキャラクターが\nアプリに引き継がれました";
            if (couponParts.Count > 0)
                sub += $"\nクーポン: {string.Join(" / ", couponParts)}";
            InfoModal.Show("引き継ぎ完了！", $"{charaName}\nLv{level}", sub);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[QR] 引き継ぎエラー: {ex.Message}");
            FriendMenuController.ShowToast("引き継ぎに失敗しました");
            EndFromButton();
        }
    }

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

            // 履歴に記録（端末ローカル・最大50件）
            LocalHistoryLog.Add("friend", $"{friendName}さんとフレンドになった");

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
        // 新しい来店を記録する前に「前回の来店時刻」を取得（1か月以内の再来ボーナス判定用）
        var lastVisit = LocalVisitLog.GetLastCheckInTime();

        var record = LocalVisitLog.Record();
        Debug.Log($"[CheckIn] 入店時間をローカルに記録しました: {record.checkedInAt}");

        // 入店チャイム（ピンポーン）
        VisitChime.PlayCheckIn();

        // 「ログイン情報を公開する」がONならフレンドに在店中が見えるよう共有する
        PresenceService.SetCheckedInAsync(true).Forget();

        // 左上の「ログイン中」バッジとグループボタンの表示を更新
        PresenceIndicator.Refresh();
        GroupController.RefreshVisibility();

        EndFromButton();

        // 来店ボーナス（毎回）＋1か月以内の再来ボーナスをまとめて付与
        GrantCheckInBonusesAsync(lastVisit).Forget();
    }

    /// <summary>1日1回ボーナスの判定キー（端末ローカルの今日の日付 "yyyy-MM-dd"）。</summary>
    private static string TodayKey() => System.DateTime.Now.ToString("yyyy-MM-dd");

    /// <summary>
    /// 選択中キャラを1レベル上げ、指定のボーナス日付フィールドを今日に更新する（1回の書き込み）。
    /// 成功したら (旧Lv, 新Lv, キャラ名)。キャラが取得できなければ null。書き込み失敗は例外を投げる。
    /// </summary>
    private async UniTask<(int oldLv, int newLv, string name)?> LevelUpWithBonusDateAsync(string bonusDateField, string today)
    {
        var manager = UserDataManager.instance;
        var chars = manager?.UserData?.Characters;
        int idx = manager != null ? manager.CurrentSelectCharacterNumber : 0;
        if (chars == null || idx >= chars.Count) return null;

        var chara = chars[idx];
        int oldLv = chara.Level;
        int newLv = oldLv + 1;

        await UserDocRef.UpdateAsync(new Dictionary<FieldPath, object>
        {
            { new FieldPath("characters", idx.ToString(), "lv"), newLv },
            { new FieldPath(bonusDateField), today },
            { new FieldPath("lastDate"), Timestamp.GetCurrentTimestamp() },
        }).AsUniTask();

        chara.Level = newLv;
        await CharacterTicketService.CheckAndGrantAsync(); // Lv50到達ごとのキャラクターチケット
        return (oldLv, newLv, chara.Name);
    }

    /// <summary>
    /// 来店時のレベルアップボーナスをまとめて付与する。
    ///  ・来店ボーナス（毎回）… +1（1日1回）
    ///  ・1か月以内の再来ボーナス … 前回来店から1か月以内なら追加で +1（1日1回）
    /// 競合（同じ旧レベルを二重に読む）を避けるため、合計レベルを1回の書き込みで反映する。
    /// 結果は InfoModal で告知する。
    /// </summary>
    private async UniTaskVoid GrantCheckInBonusesAsync(System.DateTime? lastVisit)
    {
        var manager = UserDataManager.instance;
        var chars = manager?.UserData?.Characters;
        int idx = manager != null ? manager.CurrentSelectCharacterNumber : 0;
        if (chars == null || idx >= chars.Count) return;

        var chara = chars[idx];
        string today = TodayKey();

        // 本日まだ受け取っていないボーナスだけを対象にする（各ボーナス1日1回）
        bool visitBonus = manager.UserData.VisitBonusDate != today;
        bool revisitBonus = lastVisit.HasValue
                            && System.DateTime.Now < lastVisit.Value.AddMonths(1)
                            && manager.UserData.RevisitBonusDate != today;

        int add = (visitBonus ? 1 : 0) + (revisitBonus ? 1 : 0);
        if (add == 0) return; // 本日分は付与済み

        int oldLv = chara.Level;
        int newLv = oldLv + add;

        var updates = new Dictionary<FieldPath, object>
        {
            { new FieldPath("characters", idx.ToString(), "lv"), newLv },
            { new FieldPath("lastDate"), Timestamp.GetCurrentTimestamp() },
        };
        if (visitBonus)   updates[new FieldPath("visit_bonus_date")]   = today;
        if (revisitBonus) updates[new FieldPath("revisit_bonus_date")] = today;

        try
        {
            await UserDocRef.UpdateAsync(updates).AsUniTask();

            chara.Level = newLv;
            if (visitBonus)   manager.UserData.VisitBonusDate = today;
            if (revisitBonus) manager.UserData.RevisitBonusDate = today;
            AssetsDatabase.instance?.PlayLevelUpSE();

            var parts = new List<string>();
            if (visitBonus)   parts.Add("来店ボーナス +1");
            if (revisitBonus) parts.Add("1か月以内の再来ボーナス +1");

            LocalHistoryLog.Add("visit",
                $"来店ボーナス: {chara.Name} Lv{oldLv}→Lv{newLv}（{string.Join("／", parts)}）");
            InfoModal.Show("ご来店ありがとうございます！",
                $"{chara.Name} がレベルアップ！\nLv{oldLv} → Lv{newLv}",
                string.Join("\n", parts), strongYOffset: 40f);
            await CharacterTicketService.CheckAndGrantAsync(); // Lv50到達ごとのキャラクターチケット
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[CheckIn] 来店ボーナスのレベルアップ書き込みエラー: {ex.Message}");
        }
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

        // 左上の「ログイン中」バッジとグループボタンの表示を更新
        // （グループ参加中なら GroupController 側で自動脱退する）
        PresenceIndicator.Refresh();
        GroupController.RefreshVisibility();

        EndFromButton();

        if (record == null)
        {
            Debug.LogWarning("[CheckOut] チェックイン記録が無いため、チェックアウトを記録できませんでした");
            FriendMenuController.ShowToast("チェックイン記録が見つかりませんでした");
            return;
        }

        Debug.Log($"[CheckOut] チェックアウトを記録しました: 滞在 {record.stayText}");

        // 退店チャイム（下降音）
        VisitChime.PlayCheckOut();

        // 滞在時間を履歴に記録（端末ローカル・最大50件）
        LocalHistoryLog.Add("visit", $"滞在時間 {record.stayText}");

        // 5時間（300分）以上の滞在で選択中キャラを1レベルアップ。
        // 書き込み完了後にレベルアップ込みのモーダルを出すため、ボーナス時のみ非同期処理に回す。
        const long BONUS_STAY_MINUTES = 5 * 60;
        if (record.stayMinutes >= BONUS_STAY_MINUTES)
            GrantStayBonusAndShowAsync(record.stayText).Forget();
        else
            CheckOutModal.Show(record.stayText);
    }

    /// <summary>
    /// 5時間以上の滞在ボーナス: 選択中キャラを1レベル上げてからチェックアウトモーダルを表示する（1日1回）。
    /// 本日すでに付与済み、または書き込みに失敗した場合はレベルアップを反映せず滞在時間のみ表示する。
    /// </summary>
    private async UniTaskVoid GrantStayBonusAndShowAsync(string stayText)
    {
        var manager = UserDataManager.instance;
        string today = TodayKey();

        // 1日1回（本日すでに滞在ボーナスを受け取っていれば付与しない）
        if (manager?.UserData != null && manager.UserData.StayBonusDate == today)
        {
            CheckOutModal.Show(stayText);
            return;
        }

        try
        {
            var r = await LevelUpWithBonusDateAsync("stay_bonus_date", today);
            if (r == null) { CheckOutModal.Show(stayText); return; } // キャラが取れない場合は滞在時間のみ

            manager.UserData.StayBonusDate = today;
            AssetsDatabase.instance?.PlayLevelUpSE();
            LocalHistoryLog.Add("visit", $"5時間以上の滞在ボーナス: {r.Value.name} Lv{r.Value.oldLv}→Lv{r.Value.newLv}");
            CheckOutModal.Show(stayText, r.Value.name, r.Value.oldLv, r.Value.newLv);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[CheckOut] 滞在ボーナスのレベルアップ書き込みエラー: {ex.Message}");
            CheckOutModal.Show(stayText); // 失敗時はレベルアップ表示なし
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
