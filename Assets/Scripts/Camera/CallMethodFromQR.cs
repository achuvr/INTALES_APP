using System.Collections.Generic;
using System.Linq;
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
    /// 以前のレベルアップQR（"Lv+1"〜"Lv+6"）。プレイヤー（キャラクター）の概念はアプリから外したので、
    /// レベルは上げない（2026-11-06 ユーザー指定）。読んでも「使えなくなりました」と出すだけ
    /// </summary>
    public UniTask LevelUp(int upLevel)
    {
        FriendMenuController.ShowToast(T_LEVEL_QR_RETIRED);
        EndFromButton();
        return UniTask.CompletedTask;
    }

    private const string T_LEVEL_QR_RETIRED = "このQRコードは使えなくなりました";

    /// <summary>（使っていない）以前のレベルアップの書き込み。キャラクターの lv を上げる</summary>
    private async UniTask LegacyLevelUpAsync(int upLevel)
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

    // 店頭クーポンのQRの中身（読むとそのクーポンが1枚もらえる。判定は QRReader.BranchText）。
    // 新ホームの管理者用QR一覧（SpaceAdminQrList）もこの文字をQRにして表示する
    public const string QR_FIVE_COUPON   = "5";
    public const string QR_SEVEN_COUPON  = "7";
    public const string QR_DRINK_COUPON  = "drink";
    public const string QR_COFFEE_COUPON = "coffee";
    public const string QR_FREETIME_COUPON = "freetime";
    public const string QR_FIVEHOUR_COUPON = "fivehour";
    public const string QR_ATK_COUPON    = "atk";

    // 最後の引数はクーポンの表示名（旧バッグ・新ホームの所持品と同じ名前。履歴に残す）
    public UniTask Atk()    => AddCouponAsync("atk_coupon",    d => d.ATKCoupon++,    "ATK+1チケット");
    public UniTask Drink()  => AddCouponAsync("drink_coupon",  d => d.DrinkCoupon++,  "ドリンクチケット");
    public UniTask Coffee() => AddCouponAsync("coffee_coupon", d => d.CoffeeCoupon++, "コーヒーチケット");
    public UniTask Five()   => AddCouponAsync("five_coupon",   d => d.FiveCoupon++,   "5%OFFチケット");
    public UniTask Seven()  => AddCouponAsync("seven_coupon",  d => d.SevenCoupon++,  "7%OFFチケット");
    public UniTask FreeTime() => AddCouponAsync("freetime_coupon", d => d.FreeTimeCoupon++, "フリータイムチケット");
    public UniTask FiveHour() => AddCouponAsync("fivehour_coupon", d => d.FiveHourCoupon++, "5時間無料チケット");

    /// <summary>
    /// クーポン入手の共通処理。
    /// FieldValue.Increment を使ったサーバー側加算1回だけで完結する
    /// （書き込み前の読み取り・書き込み後の再取得は行わない）。
    /// </summary>
    private async UniTask AddCouponAsync(string couponField, System.Action<UserData> applyLocal, string name)
    {
        try
        {
            await UserDocRef.UpdateAsync(new Dictionary<string, object>
            {
                { couponField, FieldValue.Increment(1) },
                { "lastDate", Timestamp.GetCurrentTimestamp() },
            }).AsUniTask();

            applyLocal(UserDataManager.instance.UserData);
            Debug.Log($"[QR] {name}を入手");
            // 履歴に記録（端末ローカル・最大50件。使ったときの「○○ を n枚 使用」と同じ書き方）
            LocalHistoryLog.Add("coupon", $"{name} を1枚 入手");
            End();
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[QR] クーポン書き込みエラー ({name}): {ex.Message}");
            EndFromButton();
        }
    }

    public async UniTask NewCharacter()
    {
        SceneLoader.instance.MergeScene("New");
    }

    /// <summary>
    /// 惑星の解放QR（アチーブメント報酬）。形式: "planet:{惑星ID}:{署名}"（SpacePlanets.ParseQr）。
    /// 店側の管理者アカウントの「惑星の解放QRコード」一覧で表示したものを読み取る。
    /// 署名の合わないQR（自作）は受け付けない。解放済みならお知らせだけ出す。
    /// 書き込みは users/{uid}.planets への ArrayUnion 1回だけ（読み取りなし）。
    /// </summary>
    public async UniTask UnlockPlanet(string qrText)
    {
        var def = SpacePlanets.ParseQr(qrText);
        if (def == null)
        {
            FriendMenuController.ShowToast(SpacePlanets.T_INVALID_QR);
            EndFromButton();
            return;
        }
        if (SpacePlanets.IsOwned(def))
        {
            EndFromButton();
            InfoModal.Show(SpacePlanets.T_ALREADY_TITLE, def.Name, SpacePlanets.T_HOW_TO_CHOOSE);
            return;
        }

        try
        {
            await UserDocRef.UpdateAsync(new Dictionary<string, object>
            {
                { "planets", FieldValue.ArrayUnion(def.Id) },
                { "lastDate", Timestamp.GetCurrentTimestamp() },
            }).AsUniTask();

            var planets = UserDataManager.instance.UserData.Planets;
            if (!planets.Contains(def.Id)) planets.Add(def.Id);
            SpacePlanets.MarkNews();
            Debug.Log($"[QR] 惑星を解放: {def.Id}");
            LocalHistoryLog.Add("planet", $"惑星「{def.Name}」を解放"); // 履歴に記録（端末ローカル）
            AssetsDatabase.instance?.PlayLevelUpSE();
            End();
            // 新ホームでは惑星が回るお祝いのお知らせ（その場で「この惑星にする」も選べる）
            // 古参の惑星（レガシア）は贈りものの言葉で（ホームでもう一度出ないよう、出したことを覚える）
            if (SpaceTheme.InUse && def.Legacy && SpacePlanets.TakeLegacyGift())
                SpaceNotice.PlanetUnlocked(def, SpacePlanets.T_LEGACY_TITLE, SpacePlanets.T_LEGACY_NOTE);
            else if (SpaceTheme.InUse) SpaceNotice.PlanetUnlocked(def);
            else InfoModal.Show(SpacePlanets.T_UNLOCKED_TITLE, def.Name, SpacePlanets.T_HOW_TO_CHOOSE);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[QR] 惑星の解放エラー ({def.Id}): {ex.Message}");
            FriendMenuController.ShowToast(SpacePlanets.T_UNLOCK_FAILED);
            EndFromButton();
        }
    }

    /// <summary>
    /// G（アプリ内の通貨）を渡すQR（"gold:{金額}:{営業日}:{番号}:{署名}"。2026-11-12。GoldQr）。
    /// 管理者の「お客様に見せるQRコード」のクーポンタブで金額を決めて出したもの。その営業日のうちだけ・同じQRは1人1回まで。
    /// 金貨座が完成していない人も受け取れる（2026-11-12 ユーザー指定）
    /// </summary>
    public async UniTask ClaimGold(string qrText)
    {
        var parsed = GoldQr.Parse(qrText, System.DateTime.UtcNow, out int amount, out string nonce);
        if (parsed != GoldQr.Result.Ok)
        {
            FriendMenuController.ShowToast(parsed == GoldQr.Result.Expired ? GoldQr.T_EXPIRED : GoldQr.T_INVALID);
            EndFromButton();
            return;
        }

        var (result, balance) = await GoldQr.ClaimAsync(amount, nonce);
        switch (result)
        {
            case GoldQr.ClaimResult.Ok:
                AssetsDatabase.instance?.PlayLevelUpSE();
                End();
                if (SpaceTheme.InUse) SpaceNotice.GoldGot(amount, balance);
                else InfoModal.Show(IgCurrency.Format(amount) + " をもらいました！", "所持金 " + IgCurrency.Format(balance), "");
                break;
            case GoldQr.ClaimResult.Already:
                EndFromButton();
                InfoModal.Show(GoldQr.T_ALREADY, IgCurrency.Format(amount), GoldQr.T_ALREADY_SUB);
                break;
            default:
                FriendMenuController.ShowToast(GoldQr.T_FAILED);
                EndFromButton();
                break;
        }
    }

    /// <summary>
    /// 星座に星を灯すQR（"stars:{星座ID}:{個数}:{営業日}:{番号}:{署名}"。2026-11-13。StarGiftQr）。
    /// 管理者の「お客様に見せるQRコード」のゲームタブの「星座に星を灯す」で、星座と個数を決めて出したもの。
    /// その営業日のうちだけ・同じQRは1人1回まで。「星を灯せるのは1日1回」（公認QR）とは別に数える。
    /// 完成・レベルアップしたら、公認のときと同じくその星座のチケット（1つ上がるごとに1枚）と全画面の演出
    /// </summary>
    public async UniTask ClaimStars(string qrText)
    {
        var parsed = StarGiftQr.Parse(qrText, System.DateTime.UtcNow, out var def, out int count, out string nonce);
        if (parsed != StarGiftQr.Result.Ok)
        {
            FriendMenuController.ShowToast(parsed == StarGiftQr.Result.Expired ? StarGiftQr.T_EXPIRED : StarGiftQr.T_INVALID);
            EndFromButton();
            return;
        }

        // この星で完成・レベルアップするなら、その星座のチケットを上がった数だけ（公認と同じ。ふつうはドリンク、職人座は7%OFF）
        var before = Constellations.Progress().Find(p => p.Def.Id == def.Id);
        int levelBefore = before != null ? before.Level : 0;
        int gained = StarGiftQr.LevelsGained(before != null ? before.Count : 0, count, def.Size);
        var rewards = new Dictionary<string, int>();
        if (gained > 0) rewards[def.Reward] = gained;

        string title = StarGiftQr.Title(def, count);
        var result = await StarGiftQr.ClaimAsync(def, count, nonce, rewards);
        if (result == StarGiftQr.ClaimResult.Already)
        {
            EndFromButton();
            InfoModal.Show(StarGiftQr.T_ALREADY, title, StarGiftQr.T_ALREADY_SUB);
            return;
        }
        if (result != StarGiftQr.ClaimResult.Ok)
        {
            FriendMenuController.ShowToast(StarGiftQr.T_FAILED);
            EndFromButton();
            return;
        }

        AssetsDatabase.instance?.PlayLevelUpSE();
        End();
        LocalHistoryLog.Add("certify", $"お店から{def.Name}に星を{count}つ灯してもらった");
        var after = Constellations.Progress().Find(p => p.Def.Id == def.Id);
        bool leveled = after != null && after.Level > levelBefore;
        if (leveled)
        {
            string ticket = SpaceCoupons.NameOf(def.Reward);
            LocalHistoryLog.Add("certify", levelBefore == 0
                ? $"{def.Name}が完成（{ticket}を{gained}枚 入手）"
                : $"{def.Name}が Lv.{after.Level} に上がった（{ticket}を{gained}枚 入手）");
        }

        if (SpaceTheme.InUse)
        {
            // 星座が完成・レベルアップしたら、先に全画面の演出（完成していなかった星座なら完成の演出。閉じると星のお知らせ）
            var mine = after != null ? new List<ConstellationProgress> { after } : new List<ConstellationProgress>();
            void ShowNotice() => SpaceNotice.GameCertified(title, mine, rewards, false, null, StarGiftQr.T_CAPTION,
                StarGiftQr.T_TITLE, levelBefore);
            if (leveled) SpaceNotice.ConstellationReveal(after, ShowNotice, levelBefore == 0);
            else ShowNotice();
        }
        else
        {
            var parts = new List<string>();
            foreach (var kv in rewards) parts.Add($"{SpaceCoupons.NameOf(kv.Key)}を{kv.Value}枚");
            InfoModal.Show(StarGiftQr.T_TITLE, title,
                parts.Count > 0 ? $"星座のボーナス！ {string.Join("・", parts)} 入手しました" : null);
        }
    }

    /// <summary>
    /// ミニオンをあげるQR（"minion:{職業}:{属性}:{レベル}:{営業日}:{番号}:{署名}"。2026-11-14。MinionGiftQr）。
    /// 管理者の「お客様に見せるQRコード」のクーポンタブの「ミニオンをあげる」で、職業・属性・レベルを決めて出したもの。
    /// その営業日のうちだけ・同じQRは1人1回まで。持てる数に関係なく受け取れる（お店からもらったミニオン。自分で作れる数には数えない）
    /// </summary>
    public async UniTask ClaimMinion(string qrText)
    {
        var parsed = MinionGiftQr.Parse(qrText, System.DateTime.UtcNow, out int job, out int element, out int level, out string nonce);
        if (parsed != MinionGiftQr.Result.Ok)
        {
            FriendMenuController.ShowToast(parsed == MinionGiftQr.Result.Expired ? MinionGiftQr.T_EXPIRED : MinionGiftQr.T_INVALID);
            EndFromButton();
            return;
        }

        string title = MinionGiftQr.Title(job, element, level);
        MinionStore.Result r;
        try
        {
            r = await MinionStore.ReceiveGiftAsync(job, element, level, nonce);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[QR] ミニオンを受け取れませんでした ({title}): {ex.Message}");
            r = null;
        }
        if (r != null && r.Already)
        {
            EndFromButton();
            InfoModal.Show(MinionGiftQr.T_ALREADY, title, MinionGiftQr.T_ALREADY_SUB);
            return;
        }
        if (r == null || !r.Ok)
        {
            FriendMenuController.ShowToast(MinionGiftQr.T_FAILED);
            EndFromButton();
            return;
        }

        AssetsDatabase.instance?.PlayLevelUpSE();
        End();
        if (SpaceTheme.InUse) SpaceNotice.MinionGift(r, MinionStore.Unlocked);
        else InfoModal.Show(MinionGiftQr.T_TITLE, title, "");
    }

    /// <summary>
    /// 1か月遊び放題パスの受け取りQR（"pass:{ID}"）。管理者の「お客様に見せるQRコード」の「1か月パス」で
    /// 発行したもの（1回だけ・発行から60分間使える）。受け取ると、今日を1日目として30日間のパスが所持品に入る
    /// </summary>
    public async UniTask ClaimPass(string qrText)
    {
        var (result, pass) = await StorePasses.ClaimAsync(qrText);
        switch (result)
        {
            case PassClaimResult.Ok:
                LocalHistoryLog.Add("pass",
                    $"{StorePasses.T_SHORT}「{pass.Type.Label}」を受け取り（{StorePasses.DateText(pass.Expiry)}まで）");
                AssetsDatabase.instance?.PlayLevelUpSE();
                End();
                ShowPassNotice(pass, false);
                break;
            case PassClaimResult.AlreadyMine:
                End();
                ShowPassNotice(pass, true);
                break;
            default:
                FriendMenuController.ShowToast(StorePasses.ClaimMessage(result));
                EndFromButton();
                break;
        }
    }

    private static void ShowPassNotice(PassInfo pass, bool already)
    {
        if (SpaceTheme.InUse)
        {
            SpaceNotice.PassReceived(pass, already);
            return;
        }
        InfoModal.Show(already ? "このパスは受け取り済みです" : "パスを受け取りました！",
            $"{StorePasses.T_SHORT}（{pass.Type.Label}）", $"{StorePasses.DateText(pass.Expiry)}まで遊び放題です");
    }

    /// <summary>
    /// ボードゲームの公認QR（"play:{ゲームキー}:{営業日}:{署名}"。管理者の「お客様に見せるQRコード」の「ゲーム」で出す）。
    /// その営業日のうちだけ使える。公認すると ✓ も付き、そのゲームのジャンルの星座に星が1つ灯る
    /// </summary>
    public async UniTask CertifyGame(string qrText)
    {
        var result = BoardGameCertify.Parse(qrText, System.DateTime.UtcNow, out var key);
        if (result != BoardGameCertify.Result.Ok)
        {
            FriendMenuController.ShowToast(result == BoardGameCertify.Result.Expired
                ? BoardGameCertify.T_EXPIRED : BoardGameCertify.T_INVALID);
            EndFromButton();
            return;
        }

        var game = BoardGameCertify.Find(key);
        string title = game != null && !string.IsNullOrEmpty(game.title) ? game.title : key;
        var candidates = Constellations.Of(game);
        bool certified = BoardGameCertify.IsCertified(key);
        // 星座の星を灯せるのは1日1回まで。達成済みのゲームでも、別の日なら同じ星座・別の星座にもう一度灯せる
        // （2026-11-09 ユーザー指定。それまでは「灯した星座が完成したら別の星座にだけ」だった）
        bool todayDone = BoardGameCertify.LitToday;
        if (certified && (todayDone || candidates.Count == 0 || game == null))
        {
            EndFromButton();
            if (candidates.Count == 0 || game == null)
                InfoModal.Show(BoardGameCertify.T_ALREADY, title, BoardGameCertify.T_NO_CONSTELLATION);
            else
                InfoModal.Show(BoardGameCertify.T_TODAY_DONE, title, BoardGameCertify.T_TODAY_DONE_SUB);
            return;
        }
        bool relight = certified;
        if (todayDone && candidates.Count > 0)
        {
            // 初めてのゲームだが、今日の星はもう灯した: アチーブメント（図鑑の印）だけ付けて、星は灯さない
            await FinishCertifyAsync(key, title, null, false, true);
            return;
        }

        // 星を灯す星座。ジャンルが2つ以上の星座に当てはまるゲームは、お客様に1つ選んでもらう
        // （2026-11-06 ユーザー指定: 全部には灯らない）。旧ホームでは選ぶ画面がないので最初の1つ
        if (candidates.Count >= 2 && SpaceTheme.InUse)
        {
            End();
            var progress = Constellations.Progress();
            var choices = new List<ConstellationProgress>();
            foreach (var p in progress)
                if (candidates.Contains(p.Def)) choices.Add(p);
            bool again = relight;
            SpaceNotice.ChooseConstellation(title, choices, id => FinishCertifyAsync(key, title, id, again).Forget(),
                again);
            return;
        }
        await FinishCertifyAsync(key, title, candidates.Count > 0 ? candidates[0].Id : null, relight);
    }

    /// <summary>
    /// 公認の書き込みとお知らせ（星座 starId に星を1つ灯す。null ならどの星座にも入らない）。
    /// 星座を選ぶお知らせの後から呼ばれることがあるので、QR画面（この部品）が消えていても動くよう static
    /// </summary>
    /// <param name="noStarToday">今日の星をもう灯したので星を灯さなかった（お知らせでそう伝える）</param>
    private static async UniTask FinishCertifyAsync(string key, string title, string starId, bool relight = false,
        bool noStarToday = false)
    {
        // この公認でちょうど完成・レベルアップする（あと1つだった）なら、その星座のチケットを1枚
        // （2026-11-06 ユーザー指定: 完成でも、そのあとのレベルアップでも。ふつうはドリンク、職人座は7%OFF）
        var rewards = new Dictionary<string, int>();
        foreach (var p in Constellations.Progress())
            if (p.Def.Id == starId && p.Partial == p.Def.Size - 1) rewards[p.Def.Reward] = 1;

        try
        {
            await BoardGameCertify.CertifyAsync(key, starId, rewards, relight);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[QR] 公認エラー ({key}): {ex.Message}");
            FriendMenuController.ShowToast(BoardGameCertify.T_FAILED);
            var qr = GameObject.FindWithTag("QR");
            if (qr != null) Destroy(qr.gameObject);
            return;
        }

        var starDef = System.Array.Find(Constellations.All, d => d.Id == starId);
        LocalHistoryLog.Add("certify", relight && starDef != null
            ? $"「{title}」で{starDef.Name}にもう一度星を灯した"
            : $"「{title}」のアチーブメントを達成");
        AssetsDatabase.instance?.PlayLevelUpSE();
        var qrRoot = GameObject.FindWithTag("QR");
        if (qrRoot != null) Destroy(qrRoot.gameObject);
        var cpm = GameObject.FindObjectOfType<CharacterPageManager>();
        if (cpm != null) cpm.ChangePage();

        // 星を灯した星座の進み具合（公認した後）
        var mine = new List<ConstellationProgress>();
        foreach (var p in Constellations.Progress())
            if (p.Def.Id == starId) mine.Add(p);
        foreach (var p in mine)
        {
            if (!p.JustLeveled) continue;
            string ticket = SpaceCoupons.NameOf(p.Def.Reward);
            LocalHistoryLog.Add("certify", p.Level == 1
                ? $"{p.Def.Name}が完成（{ticket}を1枚 入手）"
                : $"{p.Def.Name}が Lv.{p.Level} に上がった（{ticket}を1枚 入手）");
        }

        if (SpaceTheme.InUse)
        {
            // 星座が完成・レベルアップしたら、先に全画面の演出（閉じるとアチーブメントのお知らせ）
            var leveled = mine.Find(p => p.JustLeveled);
            string none = noStarToday ? BoardGameCertify.T_TODAY_DONE_SUB : null;
            if (leveled != null)
                SpaceNotice.ConstellationReveal(leveled, () => SpaceNotice.GameCertified(title, mine, rewards, relight, none));
            else SpaceNotice.GameCertified(title, mine, rewards, relight, none);
        }
        else
        {
            var parts = new List<string>();
            foreach (var kv in rewards) parts.Add($"{SpaceCoupons.NameOf(kv.Key)}を{kv.Value}枚");
            InfoModal.Show("アチーブメント達成！", title,
                parts.Count > 0 ? $"星座のボーナス！ {string.Join("・", parts)} 入手しました" : null);
        }
    }

    /// <summary>QRコードのフレンド登録プレフィックス。形式: "friend:{uid}:{name}"</summary>
    public const string FRIEND_QR_PREFIX = "friend:";

    /// <summary>
    /// バフカード山札QR。形式: "deck:soft=10,beer=5,sweets=1,..."。
    /// 読むと枚数を重みに1枚抽選して手札に保持し、次のボス戦で自動発動する。
    /// ユニークスキルを装備していると、抽選の前に山札へ介入できる:
    ///  1. 財宝（ドミニオンのスキルブック(B)・自動発動）… 山札枚数に応じてGPを獲得する
    ///  2. カード変化（塔覇の本(B)・自動発動）… 山札の1枚がランダムな別のカードに変化する
    ///  3. カード除去（塔覇の本(A)・選択式）… 山札から好きなカードを1種類取り除ける
    ///  4. カード選択（カタンのスキルブック(A)・選択式）… 山札が「パーティ人数+3」枚以上なら
    ///     抽選の代わりに好きなカードを選んで引ける（SPカードは選べない）
    ///  5. アップグレード（オーディンの術書(A)・選択式）… 山札のカードを1枚選んで
    ///     1ランク上のカードに強化してから抽選する（SPカードは選べない）
    /// 変化→除去/選択/強化の順で適用するため、変化で山札に入ったカードも各スキルの対象にできる。
    /// </summary>
    public void DrawBuffCard(string qrText)
    {
        var counts = BuffCard.DecodeDeck(qrText);

        // 財宝（ドミニオンのスキルブック(B)・自動発動）:
        // 「元の山札の枚数（カード変化・除去の前）−パーティ人数」が6枚以上なら3枚ごとにGP+1
        if (UniqueSkill.IsActive(UniqueSkillType.DeckTreasure))
            GrantDeckTreasureAsync(counts.Values.Sum()).Forget();

        // カード変化（自動発動）: 山札の1枚をランダムな別のカードに置き換える
        string transmuteText = null;
        if (UniqueSkill.IsActive(UniqueSkillType.DeckTransmute))
        {
            var t = BuffCard.TransmuteRandomCard(counts);
            if (t != null)
            {
                transmuteText = $"「塔覇の本(B)」カード変化：山札の「{BuffCard.DisplayName(t.Value.from)}」1枚が「{BuffCard.DisplayName(t.Value.to)}」に変化した！";
                Debug.Log($"[QR] カード変化: {BuffCard.DisplayName(t.Value.from)} → {BuffCard.DisplayName(t.Value.to)}");
            }
        }

        // カード除去は山札に2種類以上あるときだけ発動する（1種類の山札を空にはできない）
        if (UniqueSkill.IsActive(UniqueSkillType.DeckPurge) && counts.Count >= 2)
        {
            EndFromButton(); // 選択モーダルを出すため先にQR画面を閉じる
            // 変化の告知は除去モーダル内に出す（変化後の山札から取り除くカードを選べる）
            DeckPurgeModal.Show(counts, removed =>
            {
                if (removed.HasValue)
                {
                    counts.Remove(removed.Value);
                    Debug.Log($"[QR] カード除去: 「{BuffCard.DisplayName(removed.Value)}」を山札から取り除いた");
                }
                DrawBuffCardAndShow(counts);
            }, transmuteText);
            return;
        }

        // カード選択（カタンのスキルブック(A)・選択式）:
        // 山札が「自分を含めたパーティ人数＋3」枚以上あれば、ランダム抽選の代わりに
        // 欲しいカードを1枚選んで手札にできる（SPカードは選べない。変化後の山札から選ぶ）
        if (UniqueSkill.IsActive(UniqueSkillType.DeckPick))
        {
            int members = GroupSession.Active != null ? Mathf.Max(1, GroupSession.MemberCount()) : 1;
            bool hasPickable = counts.Any(kv => kv.Value > 0
                && kv.Key != BuffCardType.SP1 && kv.Key != BuffCardType.SP2);
            if (counts.Values.Sum() >= members + 3 && hasPickable)
            {
                EndFromButton(); // 選択モーダルを出すため先にQR画面を閉じる
                DeckPickModal.Show(counts, picked =>
                {
                    if (picked.HasValue)
                    {
                        Debug.Log($"[QR] カード選択: 「{BuffCard.DisplayName(picked.Value)}」を選んで入手");
                        HoldAndShow(picked.Value, transmuteText);
                    }
                    else
                    {
                        DrawBuffCardAndShow(counts, transmuteText);
                    }
                }, transmuteText);
                return;
            }
        }

        // アップグレード（オーディンの術書(A)・選択式）:
        // 山札のカードを1枚選んで1ランク上のカードに強化してから抽選する（SPカード＝最高ランクは選べない）
        if (UniqueSkill.IsActive(UniqueSkillType.DeckUpgrade))
        {
            bool hasUpgradable = counts.Any(kv => kv.Value > 0
                && kv.Key != BuffCardType.SP1 && kv.Key != BuffCardType.SP2);
            if (hasUpgradable)
            {
                EndFromButton(); // 選択モーダルを出すため先にQR画面を閉じる
                DeckUpgradeModal.Show(counts, chosen =>
                {
                    string notice = transmuteText;
                    if (chosen.HasValue)
                    {
                        var to = BuffCard.UpgradeOneCard(counts, chosen.Value);
                        if (to != null)
                        {
                            string upNote = $"アップグレード：「{BuffCard.DisplayName(chosen.Value)}」1枚が「{BuffCard.DisplayName(to.Value)}」に強化された！";
                            notice = string.IsNullOrEmpty(notice) ? upNote : $"{notice}\n{upNote}";
                            Debug.Log($"[QR] アップグレード: {BuffCard.DisplayName(chosen.Value)} → {BuffCard.DisplayName(to.Value)}");
                        }
                    }
                    DrawBuffCardAndShow(counts, notice);
                }, transmuteText);
                return;
            }
        }

        DrawBuffCardAndShow(counts, transmuteText);
    }

    /// <summary>
    /// 財宝（ドミニオンのスキルブック(B)）: 山札の枚数に応じてGPを付与する。
    /// 有効枚数＝QRに書かれた元の山札の枚数 − 自分を含めたパーティメンバーの人数
    /// （グループ未参加なら1人。例: 山札10枚・3人なら7枚）。
    /// 有効枚数が6枚以上なら3枚ごとにGP+1。
    /// GPの加算は FieldValue.Increment の1回書き込み。成功したらローカルにも反映して告知する。
    /// </summary>
    private async UniTaskVoid GrantDeckTreasureAsync(int deckCount)
    {
        const int MIN_CARDS = 6;    // 発動に必要な有効枚数
        const int CARDS_PER_GP = 3; // この枚数ごとにGP+1

        // 自分を含めたパーティ人数（グループ未参加なら自分1人）
        int members = GroupSession.Active != null ? Mathf.Max(1, GroupSession.MemberCount()) : 1;
        int effective = deckCount - members;
        if (effective < MIN_CARDS) return;
        int gp = effective / CARDS_PER_GP;

        var manager = UserDataManager.instance;
        if (manager == null || string.IsNullOrEmpty(manager.UID)) return;

        try
        {
            await UserDocRef.UpdateAsync(new Dictionary<string, object>
            {
                { "gp", FieldValue.Increment(gp) },
                { "lastDate", Timestamp.GetCurrentTimestamp() },
            }).AsUniTask();

            manager.UserData.GP += gp;
            Debug.Log($"[QR] 財宝: 山札{deckCount}枚−{members}人={effective}枚 → GP+{gp}");
            LocalHistoryLog.Add("gacha", $"財宝スキルで GP+{gp}（山札{deckCount}枚−メンバー{members}人）");
            // カード除去モーダルが開いていても隠れないよう、モーダルより下の空きに出す
            FriendMenuController.ShowToast(
                $"「ドミニオンのスキルブック(B)」財宝発動！ 山札{deckCount}枚−メンバー{members}人={effective}枚 → GP+{gp}", 120f);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[QR] 財宝GP付与エラー: {ex.Message}");
        }
    }

    /// <summary>
    /// 山札から1枚引いて手札に保持し、結果モーダルを表示する。
    /// transmuteText があれば結果表示の上にトーストでカード変化を告知する
    /// （除去モーダルを経由した場合はモーダル内で告知済みなので null を渡す）。
    /// </summary>
    private void DrawBuffCardAndShow(Dictionary<BuffCardType, int> counts, string transmuteText = null)
    {
        var drawn = BuffCard.DrawFrom(counts);
        if (drawn == null)
        {
            FriendMenuController.ShowToast("山札が空、または無効なカードQRです");
            EndFromButton();
            return;
        }

        Debug.Log($"[QR] バフカード「{BuffCard.DisplayName(drawn.Value)}」を引いた");
        HoldAndShow(drawn.Value, transmuteText);
    }

    /// <summary>カードを手札に保持して結果モーダルを表示する（ランダム抽選・カード選択の共通処理）。</summary>
    private void HoldAndShow(BuffCardType card, string transmuteText = null)
    {
        BuffCard.SetHeld(card); // 1枚だけ保持（上書き）
        AssetsDatabase.instance?.PlayLevelUpSE();
        End();
        BuffCardModal.Show(card);
        if (!string.IsNullOrEmpty(transmuteText))
            FriendMenuController.ShowToast(transmuteText); // モーダルの後に出して最前面に表示する
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

        // 常用プールを自作QRで無料で引かれないようにする
        if (poolId == GachaService.STANDARD_POOL)
        {
            FriendMenuController.ShowToast("無効なイベントQRです");
            EndFromButton();
            return;
        }

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
            // 結果モーダルのタイトルはプール名（例:「ウイングスパンガチャ」）。取れなければ汎用名
            var pool = await GachaService.GetPoolAsync(poolId);
            string title = string.IsNullOrEmpty(pool?.Name) ? "イベントガチャ" : pool.Name;
            InfoModal.Show(title, result.DisplayName, result.SubText);
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

    /// <summary>紙の会員証の引き継ぎを受け付けるか（2026-11-06 ユーザー指定で停止）</summary>
    public static readonly bool TRANSFER_ENABLED = false;

    /// <summary>
    /// 紙の会員証の引き継ぎQRを読み取ったときの処理。
    /// 店側が CardTransfer シーンで発行した transfers/{code} を読み（1read）、
    /// キャラクター追加と使用済みマークを1つのバッチで書き込む（アトミック）。
    /// コードは一度使うと使用済みになり、二重引き継ぎはできない。
    /// </summary>
    public async UniTask ClaimTransfer(string qrText)
    {
        // プレイヤー（キャラクター）の概念をアプリから外したので、紙の会員証の引き継ぎもやめた（2026-11-06 ユーザー指定）。
        // 読んでも何も書き込まない（transfers のコードも使用済みにしない）
        if (!TRANSFER_ENABLED)
        {
            FriendMenuController.ShowToast(T_LEVEL_QR_RETIRED);
            EndFromButton();
            return;
        }
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
        // 入店中にフレンドのQRを読んだ＝フレンドと一緒に来店（惑星オパーリアの条件。JourneyService）
        bool together = JourneyService.InStoreToday;
        if (me.Friends.ContainsKey(friendUid))
        {
            if (together)
            {
                await MarkTogetherAsync(friendUid, friendName);
                return;
            }
            FriendMenuController.ShowToast($"{friendName}さんとは既にフレンドです");
            EndFromButton();
            return;
        }

        try
        {
            var db = FirebaseFirestore.DefaultInstance;
            var now = Timestamp.GetCurrentTimestamp();
            var batch = db.StartBatch();
            string today = StoreCalendar.TodayKey;

            // 相手のdocに自分を追加（一緒に来店なら、その日も。自分が初めての来店ならそれも）
            var theirs = new Dictionary<string, object> { { "name", me.Username ?? "" }, { "since", now } };
            if (together)
            {
                theirs["met"] = today;
                if (JourneyService.IsFirstTimerToday) theirs["first"] = today;
            }
            batch.Update(
                db.Collection("users").Document(friendUid),
                new Dictionary<FieldPath, object> { { new FieldPath("friends", manager.UID), theirs } });
            // 自分のdocに相手を追加
            var mine = new Dictionary<string, object> { { "name", friendName }, { "since", now } };
            if (together) mine["met"] = today;
            batch.Update(
                db.Collection("users").Document(manager.UID),
                new Dictionary<FieldPath, object> { { new FieldPath("friends", friendUid), mine } });
            await batch.CommitAsync().AsUniTask();

            // ローカルにも反映（再取得しない）
            me.Friends[friendUid] = new FriendEntry { Name = friendName, Since = now, Met = together ? today : null };
            if (together) AfterTogether();

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
    /// すでにフレンドの人のQRを入店中に読んだ: 2人分の friends に「一緒に来店した日」を書き、
    /// オパーリアの解放・流れ星のもう1回などを処理する
    /// </summary>
    private async UniTask MarkTogetherAsync(string friendUid, string friendName)
    {
        try
        {
            var batch = FirebaseFirestore.DefaultInstance.StartBatch();
            JourneyService.AddMetMarks(batch, friendUid);
            await batch.CommitAsync().AsUniTask();
            JourneyService.ApplyMetLocal(friendUid);
            AssetsDatabase.instance?.PlayLevelUpSE();
            FriendMenuController.ShowToast($"{friendName}さんと一緒に来店しました！");
            AfterTogether();
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[QR] 一緒に来店の記録エラー: {ex.Message}");
            FriendMenuController.ShowToast("通信に失敗しました\nもう一度お試しください");
        }
        EndFromButton();
    }

    /// <summary>
    /// 一緒に来店を書いたあと: 自分の側を処理し、しばらく自分の users を見て、相手のアプリから届く
    /// 「初来店」「もう1回」も受け取る
    /// </summary>
    private static void AfterTogether()
    {
        JourneyService.ProcessAndReport(r => SpaceNotice.ShowJourneyRewards(r, false)).Forget();
        JourneyService.WatchFriendMarks(90f, r => SpaceNotice.ShowJourneyRewards(r, false));
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
        // 入店中の人数（名前もIDもない記録。新ホームの惑星のまわりを、選んでいる惑星の色の球が回る）。
        // 新ホームでは、自分の惑星がいちばん外の円の自分の場所へ飛んでいく演出をする
        StoreVisitors.CheckInAsync(SpacePlanets.Current.Id, arrival: true).Forget();

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
    /// 入店ボーナスの付与とお知らせが終わるまで true（新ホームは、これが終わってお知らせも閉じてから、
    /// 自分の惑星をいちばん外の円へ飛ばす）
    /// </summary>
    public static bool CheckInBonusPending { get; private set; }

    /// <summary>
    /// プレイヤー（キャラクター）のレベルを上げるか。2026-11-06 ユーザー指定で、プレイヤーの概念をアプリから外したので false。
    /// 来店ボーナス・5時間以上の滞在ボーナス・ボス討伐（旧ホーム）・レベルアップQR のどれでもレベルは上がらない。
    /// キャラクターのデータ（users.characters）は消さずに残している
    /// </summary>
    public static readonly bool PLAYER_LEVELS = false;

    private async UniTaskVoid GrantCheckInBonusesAsync(System.DateTime? lastVisit)
    {
        CheckInBonusPending = true;
        try
        {
            await GrantCheckInBonusesCoreAsync(lastVisit);
        }
        finally
        {
            CheckInBonusPending = false;
        }
    }

    /// <summary>
    /// 来店時のレベルアップボーナスをまとめて付与する。
    ///  ・来店ボーナス（毎回）… +1（1日1回）
    ///  ・1か月以内の再来ボーナス … 前回来店から1か月以内なら追加で +1（1日1回）
    /// 競合（同じ旧レベルを二重に読む）を避けるため、合計レベルを1回の書き込みで反映する。
    /// 結果は InfoModal で告知する。
    /// </summary>
    private async UniTask GrantCheckInBonusesCoreAsync(System.DateTime? lastVisit)
    {
        // プレイヤー（キャラクター）の概念はアプリから外したので、来店でレベルを上げない（2026-11-06 ユーザー指定）
        if (!PLAYER_LEVELS) return;
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
    /// 以前の固定の入退店QR（"checkin" / "checkout" / "visit"）。チェックイン中ならチェックアウト。
    /// 新ホームでは、このQRでは入店できない（写真や手作りのQRで入店できてしまうため。2026-11 惑星と来店のしくみ）。
    /// 入店は、お店の端末に出す1日1回変わるQR（ToggleVisitSigned）で行う。旧ホームでは従来どおり入店もできる
    /// </summary>
    public void ToggleVisit()
    {
        // チェックアウト忘れ（営業終了時刻超過）を先に自動クローズしておく
        LocalVisitLog.AutoCloseStaleVisits();

        if (LocalVisitLog.HasOpenVisit())
        {
            CheckOut();   // 既にチェックイン中 → 退店
            return;
        }
        if (SpaceTheme.InUse)
        {
            EndFromButton();
            InfoModal.Show(T_LEGACY_TITLE, T_LEGACY_STRONG, T_LEGACY_SUB);
            return;
        }
        CheckIn();    // チェックインしていない → 入店（旧ホーム）
    }

    private const string T_LEGACY_TITLE = "入店のQRコードが新しくなりました";
    private const string T_LEGACY_STRONG = "お店の画面のQRコードを\n読み取ってください";
    private const string T_LEGACY_SUB = "毎日変わるQRコードです";
    private const string T_CHECKIN_EXPIRED = "今日の入店QRコードではありません\nお店の画面のQRコードを読み取ってください";
    private const string T_CHECKIN_INVALID = "入店のQRコードを読み取れませんでした";
    private const string T_JOURNEY_FAILED = "公転の記録に失敗しました\n通信のよいところで、もう一度入店のQRコードを読み取ってください";

    /// <summary>
    /// 入店・退店のQR（お店の端末に出す、1日1回変わるQR。StoreCheckinQr）。
    /// 入店中（今日の入店が公転に数えられている）なら退店。端末に入店の記録はあるのに公転に数えていない
    /// （以前の固定QRで入店した・前回の書き込みに失敗した）なら、入店の記録はそのままで公転の記録だけやり直す。
    /// どちらでもなければ入店
    /// </summary>
    public void ToggleVisitSigned(string qrText)
    {
        var res = StoreCheckinQr.Parse(qrText, System.DateTime.UtcNow, out var visitUtc);
        if (res != StoreCheckinQr.Result.Ok)
        {
            FriendMenuController.ShowToast(res == StoreCheckinQr.Result.Expired ? T_CHECKIN_EXPIRED : T_CHECKIN_INVALID);
            EndFromButton();
            return;
        }

        LocalVisitLog.AutoCloseStaleVisits();
        bool open = LocalVisitLog.HasOpenVisit();
        if (open && JourneyService.InStoreToday)
        {
            CheckOut();
            return;
        }
        if (open)
        {
            // 入店の記録はあるが公転に数えていない → 公転の記録だけ
            if (!StoreVisitors.HasRecord) StoreVisitors.CheckInAsync(SpacePlanets.Current.Id, arrival: true).Forget();
            EndFromButton();
            RecordJourneyAsync(visitUtc).Forget();
            return;
        }

        LocalVisitLog.Record();
        VisitChime.PlayCheckIn();
        PresenceService.SetCheckedInAsync(true).Forget();
        StoreVisitors.CheckInAsync(SpacePlanets.Current.Id, arrival: true).Forget();
        PresenceIndicator.Refresh();
        GroupController.RefreshVisibility();
        EndFromButton();
        RecordJourneyAsync(visitUtc).Forget();
    }

    /// <summary>
    /// 公転・流れ星・惑星の解放を記録して（JourneyService）、結果のお知らせを順に出す。
    /// 新ホームは、お知らせが全部閉じてから自分の惑星をいちばん外の円へ飛ばす（CheckInBonusPending）
    /// </summary>
    private async UniTaskVoid RecordJourneyAsync(System.DateTime visitUtc)
    {
        CheckInBonusPending = true;
        JourneyResult r = null;
        try
        {
            r = await JourneyService.CheckInAsync(visitUtc);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[CheckIn] 公転の記録に失敗: {ex.Message}");
        }
        if (r == null)
        {
            CheckInBonusPending = false;
            FriendMenuController.ShowToast(T_JOURNEY_FAILED);
            return;
        }

        AssetsDatabase.instance?.PlayLevelUpSE();
        if (SpaceTheme.InUse)
        {
            SpaceNotice.ShowJourney(r, true, () => CheckInBonusPending = false);
        }
        else
        {
            InfoModal.Show("ご来店ありがとうございます！", $"公転 {r.Streak} 連続", $"来店回数 {r.Visits}回");
            CheckInBonusPending = false;
        }
        // 先に届いていた「一緒に来店」など（入店前にフレンドのQRを読んだ場合はない）も処理する
        JourneyService.ProcessAndReport(x => SpaceNotice.ShowJourneyRewards(x, true)).Forget();
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
        StoreVisitors.CheckOutAsync().Forget(); // 入店中の人数から外れる

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

        if (SpaceTheme.InUse)
        {
            // 新ホーム: キャラクターのレベルアップはしない（惑星と来店のしくみ）。
            // 5時間以上の滞在でイグニスを解放、秋にルブラを中央に置いて4時間以上なら流れ星をもう1回（退店のお知らせのあと）
            CheckOutModal.Show(record.stayText);
            JourneyCheckOutAsync(record.stayMinutes).Forget();
            return;
        }

        // 5時間（300分）以上の滞在で選択中キャラを1レベルアップ。
        // 書き込み完了後にレベルアップ込みのモーダルを出すため、ボーナス時のみ非同期処理に回す。
        const long BONUS_STAY_MINUTES = 5 * 60;
        if (PLAYER_LEVELS && record.stayMinutes >= BONUS_STAY_MINUTES)
            GrantStayBonusAndShowAsync(record.stayText).Forget();
        else
            CheckOutModal.Show(record.stayText);
    }

    private static async UniTaskVoid JourneyCheckOutAsync(long stayMinutes)
    {
        try
        {
            var r = await JourneyService.CheckOutAsync(stayMinutes);
            if (r != null) SpaceNotice.ShowJourneyRewards(r, false);
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[CheckOut] 退店の記録に失敗: {ex.Message}");
        }
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

    /// <summary>
    /// 会員証QR（お会計用）を読んだ。管理者なら、そのお客様のパス・クーポン・★特典の画面を出す（SpaceMemberView）。
    /// お客様どうしで読んでも何も見えない
    /// </summary>
    public void ShowMember(string qrText)
    {
        var r = MemberCard.Parse(qrText, out var info);
        EndFromButton();
        if (!AdminClaim.IsAdmin)
        {
            FriendMenuController.ShowToast("お会計用の会員証QRです（スタッフが読み取ります）");
            return;
        }
        if (r == MemberCard.Result.Expired)
        {
            FriendMenuController.ShowToast("前の日の会員証QRです\nお客様に会員証を開き直してもらってください");
            return;
        }
        if (r != MemberCard.Result.Ok)
        {
            FriendMenuController.ShowToast("読み取れない会員証QRです");
            return;
        }
        SpaceMemberView.Show(info);
    }

    /// <summary>
    /// 友だち紹介QRを読んだ（Referral）。まだ一度も来店していない人だけ、紹介を受け付ける
    /// </summary>
    public async UniTask AcceptInvite(string qrText)
    {
        EndFromButton();
        string msg = await Referral.AcceptAsync(qrText);
        if (SpaceTheme.InUse) SpaceTheme.Toast(msg, SpaceTheme.MainCanvas());
        else FriendMenuController.ShowToast(msg);
    }

    public void EndFromButton()
    {
        var qr = GameObject.FindWithTag("QR");
        if (qr != null) Destroy(qr.gameObject);
    }
}
