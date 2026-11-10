using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Cysharp.Threading.Tasks;
using Firebase.Firestore;
using UnityEngine;

/// <summary>★お店の特典が使えるか</summary>
public enum PerkState { NotPerk, Ok, NotInStore, Used, WrongDay, WrongTime, WrongSeason }

/// <summary>
/// 惑星と来店のしくみ（2026-11 企画書「惑星と来店のしくみ 案1」）の判定と書き込み。
///  ・公転: 全員共通の2週間（木曜はじまり。StoreCalendar）の中で1回でも来店すると1公転。続いた数を数える。
///    休んだ公転は「軌道修正」（6公転に1回まで。セレスタを中央に置いていれば2回）→「休みの保険」（オルビス）の順で埋め、
///    埋められなければ公転の数を半分に戻す
///  ・流れ星: 公転の最初の来店で1回（テンペスタは2回）。当たりはいまあるクーポンかノワール。公転が続くほど当たりやすい
///  ・惑星の解放: PlanetPowers の条件（曜日・来店回数・公転・季節・満月・滞在時間・フレンドと一緒・相席募集・流れ星）
///  ・★お店の特典: 中央の惑星の特典を1公転に1回（全部の惑星で合わせて1回）
/// 状態は users/{uid}.journey（JourneyData）に持ち、入店などのたびに users を1回読む トランザクションで書き直す。
/// 来店回数は1営業日に1回だけ数える。初めての入店のときは、端末の来店記録の「来店した日数」から引きつぐ。
/// </summary>
public static class JourneyService
{
    /// <summary>来店の記録が変わった（ホームの公転・来店回数の表示を更新する）</summary>
    public static event Action Changed;

    private static UserDataManager Manager => UserDataManager.instance;
    private static UserData Data => Manager != null ? Manager.UserData : null;
    public static JourneyData Journey => Data?.Journey;

    // ================================================================
    // 表示用
    // ================================================================
    /// <summary>今の営業日に（新しい入店QRで）入店していて、まだ退店していない</summary>
    public static bool InStoreToday =>
        LocalVisitLog.HasOpenVisit() && Journey != null && Journey.VisitedOn(StoreCalendar.TodayKey);

    /// <summary>来店回数（記録がなければ端末の来店記録の日数）</summary>
    public static int DisplayVisits => Journey != null ? Journey.Visits : LocalDistinctDays();

    /// <summary>
    /// 今の公転の数（最後の来店から公転が1つ以上あいていれば、次に来たときの見込みではなく今の数をそのまま返す。
    /// 途切れたかどうかは次の入店で決まる）
    /// </summary>
    public static int DisplayStreak => Journey != null ? Journey.Streak : 0;

    // 惑星のパワーは 2026-11-10 にやめた（JourneyRules）

    /// <summary>今の公転で来店済みか</summary>
    public static bool VisitedThisOrbit => Journey != null && Journey.Period == StoreCalendar.CurrentPeriod;

    /// <summary>最後の来店のあと、来店せずに終わった公転の数（次の入店で軌道修正・保険・半分に戻すが決まる）</summary>
    public static int MissedOrbits =>
        Journey != null && Journey.Period > 0 ? Math.Max(0, StoreCalendar.CurrentPeriod - Journey.Period - 1) : 0;

    /// <summary>次の節目（公転の数と惑星）。全部持っていれば null</summary>
    public static (int n, SpacePlanetDef def)? NextStreakGoal()
    {
        foreach (var (n, id) in JourneyRules.STREAK_PLANETS)
        {
            var def = SpacePlanets.Find(id);
            if (def != null && !SpacePlanets.IsOwned(def)) return (n, def);
        }
        return null;
    }

    /// <summary>
    /// デバッグ用（エディタの R キー）: 今の公転の数・中央の惑星・今日の曜日と季節で流れ星を1回引いた当たり
    /// </summary>
    public static StarPrize DebugRoll()
    {
        var today = StoreCalendar.TodayBusiness;
        string center = SpacePlanets.Current.Id;
        return JourneyRules.Roll(new System.Random(), Math.Max(1, DisplayStreak), center,
            today.DayOfWeek, StoreCalendar.SeasonOf(today));
    }

    /// <summary>
    /// デバッグ用（エディタの R キー。2026-11-06 ユーザー指定で本当に増やす）: 流れ星を1回引き、当たりをログイン中の
    /// アカウントに足す（クーポン＋1。ノワールは解放、持っていれば7%引き）。公転ごとの流れ星の回数（journey）は使わない
    /// </summary>
    public static async UniTask<JourneyResult> DebugDrawAsync()
    {
        var mgr = Manager;
        if (mgr == null || string.IsNullOrEmpty(mgr.UID) || Data == null) return null;
        var r = new JourneyResult();
        JourneyRules.AddPrize(DebugRoll(), new HashSet<string>(Data.Planets), r);

        var updates = new Dictionary<string, object> { { "lastDate", Timestamp.GetCurrentTimestamp() } };
        foreach (var p in r.Prizes)
        {
            var coupon = CouponOf(p);
            if (coupon != null) updates[coupon.Field] = FieldValue.Increment(1);
        }
        if (r.Unlocked.Count > 0) updates["planets"] = FieldValue.ArrayUnion(r.Unlocked.Cast<object>().ToArray());
        await FirebaseFirestore.DefaultInstance.Collection("users").Document(mgr.UID).UpdateAsync(updates).AsUniTask();

        ApplyLocal(null, r);
        foreach (var p in r.Prizes)
        {
            var coupon = CouponOf(p);
            if (coupon != null) LocalHistoryLog.Add("coupon", $"流れ星で {coupon.Name} を1枚 入手（デバッグ）");
        }
        foreach (var id in r.Unlocked)
        {
            var def = SpacePlanets.Find(id);
            if (def != null) LocalHistoryLog.Add("planet", $"惑星「{def.Name}」を解放（デバッグ）");
        }
        return r;
    }

    /// <summary>今の公転で、まだ使える軌道修正の数</summary>
    public static int RescuesLeft(string centerId)
    {
        int allowance = JourneyRules.RESCUE_ALLOWANCE; // 惑星によって変わらない（セレスタの2回は 2026-11-09 にやめた）
        var j = Journey;
        if (j == null) return allowance;
        int now = StoreCalendar.CurrentPeriod;
        int recent = j.Rescues.Count(x => now - x < JourneyRules.RESCUE_WINDOW);
        return Math.Max(0, allowance - recent);
    }

    // ================================================================
    // 入店
    // ================================================================
    /// <summary>
    /// 新しい入店QRで入店した。visitUtc は入店QRを読み取った時刻（今日の営業日のQRであることは確かめ済み）。
    /// 端末の来店記録（LocalVisitLog）に入店を書いたあとに呼ぶこと（初回の来店回数の引きつぎに使う）
    /// </summary>
    public static async UniTask<JourneyResult> CheckInAsync(DateTime visitUtc)
    {
        var mgr = Manager;
        if (mgr == null || string.IsNullOrEmpty(mgr.UID)) return null;
        string uid = mgr.UID;

        var jst = StoreCalendar.ToJst(visitUtc);
        var day = StoreCalendar.BusinessDate(jst);
        var center = SpacePlanets.Current;
        var ctx = new JourneyRules.VisitContext
        {
            DayKey = StoreCalendar.DayKey(day),
            Period = StoreCalendar.PeriodOf(day),
            Weekday = day.DayOfWeek,
            Season = StoreCalendar.SeasonOf(day),
            FullMoon = StoreCalendar.IsFullMoonDay(day),
            Center = center.Id,
            CenterColors = center.Colors, // この色のコマを1個ずつ（その営業日の最初の入店のとき。2026-11-10）
            InAt = new DateTimeOffset(visitUtc.ToUniversalTime()).ToUnixTimeSeconds(),
            SeedDays = LocalDistinctDays(),
        };
        if (!SpacePlanets.IsOwned(SpacePlanets.Find("hexia")))
            ctx.Recruit = await PlayedRecruitAsync(uid, ctx.DayKey);

        var rng = new System.Random(unchecked(Environment.TickCount * 31 + uid.GetHashCode()));
        var db = FirebaseFirestore.DefaultInstance;
        var doc = db.Collection("users").Document(uid);
        JourneyData journey = null;
        var result = await db.RunTransactionAsync(tx => tx.GetSnapshotAsync(doc).ContinueWith(t =>
        {
            var snap = t.Result;
            var map = snap.ToDictionary();
            var owned = OwnedPlanets(map);
            var j = JourneyData.From(map.TryGetValue("journey", out var raw) ? raw : null);
            var r = new JourneyResult();
            j = JourneyRules.ApplyVisit(j, ctx, r, owned, rng);
            tx.Update(doc, BuildUpdates(j, r));
            journey = j;
            return r;
        })).AsUniTask();

        ApplyLocal(journey, result);
        History(result);
        return result;
    }

    // ================================================================
    // 退店
    // ================================================================
    /// <summary>
    /// 退店した（滞在時間が決まった）。5時間以上でイグニスを解放、秋にルブラを中央に置いて4時間以上なら流れ星をもう1回。
    /// 今日（新しい入店QRで）入店していなければ何もしない
    /// </summary>
    public static async UniTask<JourneyResult> CheckOutAsync(long stayMinutes)
    {
        var mgr = Manager;
        var j0 = Journey;
        if (mgr == null || string.IsNullOrEmpty(mgr.UID) || j0 == null || !j0.VisitedOn(StoreCalendar.TodayKey))
            return null;
        string center = SpacePlanets.Current.Id;
        var season = StoreCalendar.CurrentSeason;
        bool ignis = stayMinutes >= 5 * 60 && !SpacePlanets.IsOwned(SpacePlanets.Find("ignis"));
        bool rubra = false; // 惑星の能力（ルブラの流れ星もう1回）は 2026-11-09 にやめた
        bool exhaust = !string.IsNullOrEmpty(j0.Used); // コマをもらった惑星が力を失う（2026-11-10）
        if (!ignis && !rubra && !exhaust) return null;

        string uid = mgr.UID;
        var weekday = StoreCalendar.TodayBusiness.DayOfWeek;
        var rng = new System.Random(unchecked(Environment.TickCount * 17 + uid.GetHashCode()));
        var db = FirebaseFirestore.DefaultInstance;
        var doc = db.Collection("users").Document(uid);
        JourneyData journey = null;
        var result = await db.RunTransactionAsync(tx => tx.GetSnapshotAsync(doc).ContinueWith(t =>
        {
            var map = t.Result.ToDictionary();
            var owned = OwnedPlanets(map);
            var j = JourneyData.From(map.TryGetValue("journey", out var raw) ? raw : null) ?? j0.Clone();
            var r = new JourneyResult { Streak = j.Streak, Visits = j.Visits, Period = j.Period, CheckOut = true };
            JourneyRules.ApplyCheckOut(j, r);
            if (ignis) JourneyRules.Grant("ignis", owned, r);
            if (rubra && !j.Extra.Contains(JourneyRules.EXTRA_RUBRA))
            {
                j.Extra.Add(JourneyRules.EXTRA_RUBRA);
                JourneyRules.AddPrize(JourneyRules.Roll(rng, j.Streak, center, weekday, season), owned, r);
            }
            tx.Update(doc, BuildUpdates(j, r));
            journey = j;
            return r;
        })).AsUniTask();

        ApplyLocal(journey, result);
        History(result);
        return result;
    }

    // ================================================================
    // フレンドと一緒に来店（お店で、入店中にフレンドのQRを読む）
    // ================================================================
    /// <summary>
    /// フレンドのQRを読んだときに、2人分の friends に「一緒に来店した営業日」を書く（自分が入店中のときだけ）。
    /// 相手の users は friends の自分のキーだけ書けるルールなので、ルールを変えずに書ける。
    /// 自分が初めての来店なら、相手の側に first も書く（相手のパステリアの能力に使う）
    /// </summary>
    public static void AddMetMarks(WriteBatch batch, string friendUid)
    {
        var mgr = Manager;
        if (batch == null || mgr == null || !InStoreToday) return;
        var db = FirebaseFirestore.DefaultInstance;
        string today = StoreCalendar.TodayKey;
        var theirs = new Dictionary<FieldPath, object> { { new FieldPath("friends", mgr.UID, "met"), today } };
        if (IsFirstTimerToday) theirs[new FieldPath("friends", mgr.UID, "first")] = today;
        batch.Update(db.Collection("users").Document(friendUid), theirs);
        batch.Update(db.Collection("users").Document(mgr.UID), new Dictionary<FieldPath, object>
        {
            { new FieldPath("friends", friendUid, "met"), today },
        });
    }

    /// <summary>AddMetMarks で書いた内容を、手元のフレンド一覧にも反映する</summary>
    public static void ApplyMetLocal(string friendUid)
    {
        var d = Data;
        if (d == null || !d.Friends.TryGetValue(friendUid, out var f) || f == null) return;
        f.Met = StoreCalendar.TodayKey;
    }

    /// <summary>今日が初めての来店（来店回数1で、今日入店した）</summary>
    public static bool IsFirstTimerToday
    {
        get
        {
            var j = Journey;
            return j != null && j.Visits <= 1 && j.VisitedOn(StoreCalendar.TodayKey);
        }
    }

    /// <summary>手元のフレンド一覧に、まだ処理していない今日の「一緒に来店」「初来店」「もう1回」があるか</summary>
    public static bool HasPendingFriendMarks()
    {
        var d = Data;
        var j = Journey;
        if (d == null || j == null || !InStoreToday) return false;
        string today = StoreCalendar.TodayKey;
        var done = j.MetDay == today ? j.MetUids : null;
        foreach (var kv in d.Friends)
        {
            var f = kv.Value;
            if (f == null) continue;
            if (f.Met == today && (done == null || !done.Contains(kv.Key))) return true;
            if (f.Gift == today && !j.Extra.Contains(JourneyRules.EXTRA_GIFT)) return true;
            if (f.First == today && SpacePlanets.Current.Id == "pastelia"
                && StoreCalendar.CurrentSeason == StoreCalendar.Season.Spring && !j.Extra.Contains(JourneyRules.EXTRA_FIRST)) return true;
        }
        return false;
    }

    /// <summary>
    /// 自分の friends に届いている今日の「一緒に来店」などを処理する（入店中のときだけ）。
    /// オパーリアを解放し、中央の惑星の能力で流れ星をもう1回引く。オパーリア・パステリアの能力では相手にも
    /// 「もう1回」を届ける（相手の friends の自分のキーに gift を書く）。処理済みの印は journey に残す。
    /// 何もなければ null
    /// </summary>
    public static async UniTask<JourneyResult> ProcessFriendMarksAsync()
    {
        var mgr = Manager;
        if (mgr == null || string.IsNullOrEmpty(mgr.UID) || !InStoreToday) return null;
        string uid = mgr.UID;
        string today = StoreCalendar.TodayKey;
        string center = SpacePlanets.Current.Id;
        var season = StoreCalendar.CurrentSeason;
        var weekday = StoreCalendar.TodayBusiness.DayOfWeek;
        int period = StoreCalendar.CurrentPeriod;
        var rng = new System.Random(unchecked(Environment.TickCount * 13 + uid.GetHashCode()));
        var db = FirebaseFirestore.DefaultInstance;
        var doc = db.Collection("users").Document(uid);

        JourneyData journey = null;
        var gifts = new List<string>();     // もう1回を届ける相手
        var firsts = new List<string>();    // 自分が初来店だと知らせる相手
        var names = new List<string>();
        var friends = new Dictionary<string, FriendEntry>();
        var result = await db.RunTransactionAsync(tx => tx.GetSnapshotAsync(doc).ContinueWith(t =>
        {
            gifts.Clear();
            firsts.Clear();
            names.Clear();
            var snap = t.Result;
            var map = snap.ToDictionary();
            var owned = OwnedPlanets(map);
            var j = JourneyData.From(map.TryGetValue("journey", out var raw) ? raw : null);
            var r = new JourneyResult();
            if (j == null || !j.VisitedOn(today)) return null;
            r.Streak = j.Streak;
            r.Visits = j.Visits;
            r.Period = j.Period;
            if (j.MetDay != today)
            {
                j.MetDay = today;
                j.MetUids.Clear();
            }
            bool firstTimer = j.Visits <= 1;
            bool changed = false;
            friends.Clear();
            if (map.TryGetValue("friends", out var fr) && fr is Dictionary<string, object> fm)
            {
                foreach (var kv in fm)
                {
                    if (!(kv.Value is Dictionary<string, object> e)) continue;
                    friends[kv.Key] = new FriendEntry
                    {
                        Name = e.TryGetValue("name", out var n) ? n?.ToString() : null,
                        Met = e.TryGetValue("met", out var m) ? m?.ToString() : null,
                        First = e.TryGetValue("first", out var f) ? f?.ToString() : null,
                        Gift = e.TryGetValue("gift", out var g) ? g?.ToString() : null,
                    };
                }
            }

            foreach (var kv in friends)
            {
                var f = kv.Value;
                if (f.Met == today && !j.MetUids.Contains(kv.Key))
                {
                    j.MetUids.Add(kv.Key);
                    changed = true;
                    names.Add(f.Name ?? "");
                    JourneyRules.Grant("opalia", owned, r);
                    if (firstTimer) firsts.Add(kv.Key);
                    if (center == "opalia" && !j.Extra.Contains(JourneyRules.EXTRA_TOGETHER) && j.StarPeriod == period)
                    {
                        j.Extra.Add(JourneyRules.EXTRA_TOGETHER);
                        JourneyRules.AddPrize(JourneyRules.Roll(rng, j.Streak, center, weekday, season), owned, r);
                        gifts.Add(kv.Key);
                    }
                }
                if (f.First == today && center == "pastelia" && season == StoreCalendar.Season.Spring
                    && !j.Extra.Contains(JourneyRules.EXTRA_FIRST) && j.StarPeriod == period)
                {
                    j.Extra.Add(JourneyRules.EXTRA_FIRST);
                    changed = true;
                    JourneyRules.AddPrize(JourneyRules.Roll(rng, j.Streak, center, weekday, season), owned, r);
                    if (!gifts.Contains(kv.Key)) gifts.Add(kv.Key);
                }
                if (f.Gift == today && !j.Extra.Contains(JourneyRules.EXTRA_GIFT) && j.StarPeriod == period)
                {
                    j.Extra.Add(JourneyRules.EXTRA_GIFT);
                    changed = true;
                    JourneyRules.AddPrize(JourneyRules.Roll(rng, j.Streak, center, weekday, season), owned, r);
                }
            }
            if (!changed) return null;
            if (names.Count > 0) r.Together = string.Join("・", names);
            tx.Update(doc, BuildUpdates(j, r));
            journey = j;
            return r;
        })).AsUniTask();

        if (result == null) return null;
        ApplyLocal(journey, result);
        History(result);

        // 相手に「もう1回」「初来店」を届ける（失敗しても自分の結果はそのまま）
        if (gifts.Count > 0 || firsts.Count > 0)
        {
            try
            {
                var batch = db.StartBatch();
                foreach (var f in gifts.Union(firsts))
                {
                    var upd = new Dictionary<FieldPath, object>();
                    if (gifts.Contains(f)) upd[new FieldPath("friends", uid, "gift")] = today;
                    if (firsts.Contains(f)) upd[new FieldPath("friends", uid, "first")] = today;
                    batch.Update(db.Collection("users").Document(f), upd);
                }
                await batch.CommitAsync().AsUniTask();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Journey] フレンドへの書き込みに失敗: {ex.Message}");
            }
        }
        return result;
    }

    /// <summary>
    /// フレンドのQRを読んだあと、しばらく自分の users を購読して、相手のアプリから届く「初来店」「もう1回」を受け取る
    /// </summary>
    public static void WatchFriendMarks(float seconds, Action<JourneyResult> onResult)
    {
        var mgr = Manager;
        if (mgr == null || string.IsNullOrEmpty(mgr.UID)) return;
        _watch?.Stop();
        var reg = FirebaseFirestore.DefaultInstance.Collection("users").Document(mgr.UID).Listen(snap =>
        {
            if (!snap.Exists) return;
            MergeFriendMarks(snap.ConvertTo<UserData>());
            if (!HasPendingFriendMarks() || _processing) return;
            ProcessAndReport(onResult).Forget();
        });
        _watch = reg;
        StopWatchLater(reg, seconds).Forget();
    }

    private static ListenerRegistration _watch;
    private static bool _processing;

    private static async UniTaskVoid StopWatchLater(ListenerRegistration reg, float seconds)
    {
        await UniTask.Delay(TimeSpan.FromSeconds(seconds), DelayType.Realtime);
        reg.Stop();
        if (_watch == reg) _watch = null;
    }

    /// <summary>まだ処理していない「一緒に来店」などがあれば処理して、結果を渡す（入店中のホームを開いたときなど）</summary>
    public static async UniTaskVoid ProcessAndReport(Action<JourneyResult> onResult)
    {
        if (_processing || !HasPendingFriendMarks()) return;
        _processing = true;
        try
        {
            var r = await ProcessFriendMarksAsync();
            if (r != null) onResult?.Invoke(r);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Journey] 一緒に来店の処理に失敗: {ex.Message}");
        }
        finally
        {
            _processing = false;
        }
    }

    /// <summary>読み直した users の friends の印（met / first / gift）を手元のフレンド一覧に写す</summary>
    public static void MergeFriendMarks(UserData fresh)
    {
        var d = Data;
        if (d == null || fresh == null) return;
        foreach (var kv in fresh.Friends)
        {
            if (kv.Value == null || !d.Friends.TryGetValue(kv.Key, out var mine) || mine == null) continue;
            mine.Met = kv.Value.Met;
            mine.First = kv.Value.First;
            mine.Gift = kv.Value.Gift;
        }
    }

    // ================================================================
    // ★お店の特典
    // ================================================================
    /// <summary>中央の惑星の★特典が今使えるか</summary>
    public static PerkState PerkStateOf(PlanetPower p)
    {
        if (p == null || !p.Store) return PerkState.NotPerk;
        var j = Journey;
        if (!InStoreToday || j == null) return PerkState.NotInStore;
        if (j.PerkPeriod == StoreCalendar.CurrentPeriod) return PerkState.Used;
        var inJst = StoreCalendar.ToJst(DateTimeOffset.FromUnixTimeSeconds(j.InAt).UtcDateTime);
        int inMinutes = inJst.Hour * 60 + inJst.Minute;
        if (inJst.Hour < StoreCalendar.DAY_CHANGE_HOUR) inMinutes += 24 * 60; // 深夜は前の日の続き
        switch (p.Rule)
        {
            case PerkRule.Thursday:
                return StoreCalendar.TodayBusiness.DayOfWeek == DayOfWeek.Thursday ? PerkState.Ok : PerkState.WrongDay;
            case PerkRule.Morning:
                return inMinutes >= StoreCalendar.OPEN_MINUTES && inMinutes < StoreCalendar.OPEN_MINUTES + 60
                    ? PerkState.Ok : PerkState.WrongTime;
            case PerkRule.Evening:
                return inMinutes >= 18 * 60 ? PerkState.Ok : PerkState.WrongTime;
            case PerkRule.Summer:
                return StoreCalendar.CurrentSeason == StoreCalendar.Season.Summer ? PerkState.Ok : PerkState.WrongSeason;
            default:
                return PerkState.Ok;
        }
    }

    /// <summary>★特典を使った記録を書く（1公転に1回）。使えたら true</summary>
    public static async UniTask<bool> UsePerkAsync(PlanetPower p)
    {
        var mgr = Manager;
        if (mgr == null || string.IsNullOrEmpty(mgr.UID) || PerkStateOf(p) != PerkState.Ok) return false;
        int period = StoreCalendar.CurrentPeriod;
        string today = StoreCalendar.TodayKey;
        var db = FirebaseFirestore.DefaultInstance;
        var doc = db.Collection("users").Document(mgr.UID);
        JourneyData journey = null;
        bool ok = await db.RunTransactionAsync(tx => tx.GetSnapshotAsync(doc).ContinueWith(t =>
        {
            var map = t.Result.ToDictionary();
            var j = JourneyData.From(map.TryGetValue("journey", out var raw) ? raw : null);
            if (j == null || !j.VisitedOn(today) || j.PerkPeriod == period) return false;
            j.PerkPeriod = period;
            j.PerkPlanet = p.Id;
            tx.Update(doc, new Dictionary<string, object> { { "journey", j.ToMap() } });
            journey = j;
            return true;
        })).AsUniTask();
        if (!ok) return false;
        ApplyLocal(journey, null);
        var def = SpacePlanets.Find(p.Id);
        LocalHistoryLog.Add("coupon", $"{def?.Name}の特典「{p.PerkName}」を使用");
        return true;
    }

    // ================================================================
    // 共通
    // ================================================================
    /// <summary>当たりに対応するクーポン（ノワールは null）</summary>
    public static SpaceCouponDef CouponOf(StarPrize p)
    {
        string key = p switch
        {
            StarPrize.Five => CallMethodFromQR.QR_FIVE_COUPON,
            StarPrize.Seven => CallMethodFromQR.QR_SEVEN_COUPON,
            StarPrize.Drink => CallMethodFromQR.QR_DRINK_COUPON,
            StarPrize.Coffee => CallMethodFromQR.QR_COFFEE_COUPON,
            StarPrize.FiveHour => CallMethodFromQR.QR_FIVEHOUR_COUPON,
            StarPrize.FreeTime => CallMethodFromQR.QR_FREETIME_COUPON,
            _ => null,
        };
        if (key == null) return null;
        foreach (var c in SpaceCoupons.All)
            if (c.QrText == key) return c;
        return null;
    }

    private static Dictionary<string, object> BuildUpdates(JourneyData j, JourneyResult r)
    {
        var updates = new Dictionary<string, object>
        {
            { "journey", j.ToMap() },
            { "lastDate", Timestamp.GetCurrentTimestamp() },
        };
        foreach (var g in r.Prizes.GroupBy(p => p))
        {
            var coupon = CouponOf(g.Key);
            if (coupon != null) updates[coupon.Field] = FieldValue.Increment(g.Count());
        }
        foreach (var g in r.Meeples.GroupBy(m => m))
        {
            var meeple = SpaceMeeples.Of(g.Key);
            if (meeple != null) updates[meeple.Field] = FieldValue.Increment(g.Count());
        }
        if (r.Unlocked.Count > 0) updates["planets"] = FieldValue.ArrayUnion(r.Unlocked.Cast<object>().ToArray());
        return updates;
    }

    /// <summary>書き込んだ内容を手元の UserData にも反映する</summary>
    private static void ApplyLocal(JourneyData j, JourneyResult r)
    {
        var d = Data;
        if (d == null) return;
        if (j != null) d.Journey = j;
        if (r != null)
        {
            foreach (var p in r.Prizes)
                CouponOf(p)?.Add(d, 1);
            foreach (var m in r.Meeples)
                SpaceMeeples.Of(m)?.Add(d, 1);
            foreach (var id in r.Unlocked)
                if (!d.Planets.Contains(id)) d.Planets.Add(id);
            if (r.Unlocked.Count > 0) SpacePlanets.MarkNews();
        }
        Changed?.Invoke();
    }

    /// <summary>履歴（端末ローカル）に残す</summary>
    private static void History(JourneyResult r)
    {
        if (r == null) return;
        if (r.NewOrbit) LocalHistoryLog.Add("visit", $"公転 {r.Streak} 連続");
        if (r.Meeples.Count > 0)
            LocalHistoryLog.Add("meeple", r.Meeples.Count == 1
                ? $"入店で {SpaceMeeples.Names(r.Meeples)} を1個 入手"
                : $"入店で {SpaceMeeples.Names(r.Meeples)} を1個ずつ 入手");
        if (!string.IsNullOrEmpty(r.Exhausted))
        {
            var used = SpacePlanets.Find(r.Exhausted);
            if (used != null) LocalHistoryLog.Add("planet", $"惑星「{used.Name}」が力を失った");
        }
        foreach (var p in r.Prizes)
        {
            if (p == StarPrize.Noir) continue;
            var c = CouponOf(p);
            if (c != null) LocalHistoryLog.Add("coupon", $"流れ星で {c.Name} を1枚 入手");
        }
        foreach (var id in r.Unlocked)
        {
            var def = SpacePlanets.Find(id);
            if (def != null) LocalHistoryLog.Add("planet", $"惑星「{def.Name}」を解放");
        }
    }

    private static HashSet<string> OwnedPlanets(Dictionary<string, object> map)
    {
        var set = new HashSet<string>();
        if (map.TryGetValue("planets", out var raw) && raw is IEnumerable<object> list)
            foreach (var o in list)
                if (o != null) set.Add(o.ToString());
        // 以前からの会員（starter が無い）はルミナを最初から持っている（7回目の来店で「ルミナを解放」と出さないように）
        if (!map.TryGetValue("starter", out var starter) || starter == null) set.Add(SpacePlanets.DEFAULT_ID);
        return set;
    }

    /// <summary>端末の来店記録で来店した営業日の数（初めて新しい入店QRで入店したときに来店回数として引きつぐ）</summary>
    public static int LocalDistinctDays()
    {
        var log = LocalVisitLog.Load();
        var days = new HashSet<string>();
        if (log.visits == null) return 0;
        foreach (var v in log.visits)
        {
            if (!DateTime.TryParse(v.checkedInAt, null, DateTimeStyles.RoundtripKind, out var t)) continue;
            var utc = t.Kind == DateTimeKind.Utc ? t : t.ToUniversalTime();
            days.Add(StoreCalendar.DayKey(StoreCalendar.BusinessDate(StoreCalendar.ToJst(utc))));
        }
        return days.Count;
    }

    /// <summary>今日の相席募集に参加している（または参加者のいる自分の募集がある）。ヘクシアの解放</summary>
    private static async UniTask<bool> PlayedRecruitAsync(string uid, string dayKey)
    {
        try
        {
            var snap = await FirebaseFirestore.DefaultInstance.Collection("recruits")
                .WhereEqualTo("date", dayKey).GetSnapshotAsync().AsUniTask();
            foreach (var doc in snap.Documents)
            {
                var d = doc.ToDictionary();
                var participants = d.TryGetValue("participants", out var p) ? p as Dictionary<string, object> : null;
                if (participants != null && participants.ContainsKey(uid)) return true;
                if (d.TryGetValue("uid", out var owner) && owner?.ToString() == uid
                    && participants != null && participants.Count > 0) return true;
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Journey] 相席募集の確認に失敗: {ex.Message}");
        }
        return false;
    }
}
