using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Cysharp.Threading.Tasks;
using Firebase.Firestore;
using UnityEngine;

/// <summary>
/// コインプッシャー（2026-11-12 ユーザー指定「積み木座を完成させるとミニゲームができるように。別シーンに遷移してそのゲームをする。
/// そのゲームは G（お金）を使ったコインプッシャー。プッシュされる物は G とミープルと星のカケラ」）。
///  ・遊べるのは積み木座（block）を完成させた人（管理者は初めから）。ホームの空の積み木座をタップすると開く
///  ・別シーン CoinPusher（Assets/Scenes/CoinPusher.unity）を SceneLoader.MergeScene で加算マージして開く
///    （図鑑と同じ。UserDataManager などのシングルトンを残すため）。中身は CoinPusherController と CoinPusherWorld
///  ・コインを1枚入れるたびに 1G。台の手前から落ちた物を獲得（コイン＝1G・赤／青／黄コマ＝1個・星のカケラ＝1個）。
///    手前の左右から横に落ちた物は没収。コインを BONUS_EVERY 枚入れるごとに、ボーナスでコマか星のカケラが1つ落ちてくる。
///    コインを TOWER_EVERY 枚ゲットするごとに、ボーナスでプッシャーの上にコインタワー（TOWER_COINS 枚を積んだ柱）が出る
///  ・台の上の物の並びは端末に保存して、次に開いたときに続きから（閉じて開き直しても景品は戻らない）
///  ・所持金・コマ・星のカケラの増減は、手元の数はすぐに、サーバー（users）には数秒ごとと閉じるときにまとめて送る
///    （FieldValue.Increment。送れなかった分は端末に残して、次に送る）。星のカケラが5個そろったら、閉じたあとホームで流れ星
/// </summary>
public static class CoinPusher
{
    public const string SCENE = "CoinPusher";
    /// <summary>遊べるようになる星座（積み木座）</summary>
    public const string CONSTELLATION = "block";
    /// <summary>コイン1枚の値段（G）</summary>
    public const int COST = 1;
    /// <summary>この枚数を入れるごとに、ボーナスの景品（コマか星のカケラ）が1つ落ちてくる</summary>
    public const int BONUS_EVERY = 15;
    /// <summary>
    /// コインをこの枚数ゲットする（受け皿に落とす）ごとに、ボーナスでコインタワーが出る
    /// （2026-11-12 ユーザー指定「30枚コインを落下させてゲットするとボーナスでコインタワーが出現」。数は台と一緒に端末に保存）
    /// </summary>
    public const int TOWER_EVERY = 30;
    /// <summary>コインタワーに積むコインの枚数</summary>
    public const int TOWER_COINS = 10;
    /// <summary>履歴の種類（SpaceHistoryPanel）</summary>
    public const string HISTORY = "pusher";

    public const string T_TITLE = "コインプッシャー";
    public const string T_CAPTION = "MINI GAME";
    public const string T_SIGN = "COIN PUSHER";
    public const string T_LOCKED = "積み木座を完成させると、コインプッシャーで遊べます";
    public const string T_DROP = "コインを入れる";
    public const string T_DROP_SUB = "1回 1G ・ 長押しで続けて入れる";
    public const string T_HINT = "プッシャーの上をタップすると、そこにコインが落ちます";
    public const string T_WALLET = "所持金";
    public const string T_GOT = "今回の獲得";
    public const string T_SHORT = "G が足りません";
    public const string T_FULL = "台がいっぱいです。少し待ってから入れてください";
    public const string T_BONUS = "ボーナス！";
    public const string T_BONUS_STAR = "星のカケラが落ちてくる";
    public const string T_BONUS_MEEPLE_B = "が落ちてくる";
    public const string T_TOWER = "コインタワー！";
    public const string T_TOWER_SUB_A = "コインを ";          // 「コインを 30 枚ゲットしたボーナス」
    public const string T_TOWER_SUB_B = " 枚ゲットしたボーナス";
    public const string T_SAVING = "保存しています…";
    public const string T_SAVE_FAILED = "通信に失敗しました。結果は次に開いたときに送ります";
    public const string T_SUMMARY_A = "コインプッシャー：";
    public const string T_SUMMARY_SPENT_B = " 使って ";
    public const string T_SUMMARY_GOT_B = " を獲得";
    public const string T_SUMMARY_NONE = "獲得はありませんでした";
    public const string T_RULES = "手前から落ちた物を獲得 ・ 左右から落ちた物は没収";

    /// <summary>この画面で表示しうる文字（NewHome の起動時にまとめてフォントへ登録する）</summary>
    public static string GlyphText => new StringBuilder()
        .Append(T_TITLE).Append(T_CAPTION).Append(T_SIGN).Append(T_LOCKED).Append(T_DROP).Append(T_DROP_SUB).Append(T_HINT)
        .Append(T_WALLET).Append(T_GOT).Append(T_SHORT).Append(T_FULL).Append(T_BONUS).Append(T_BONUS_STAR)
        .Append(T_BONUS_MEEPLE_B).Append(T_TOWER).Append(T_TOWER_SUB_A).Append(T_TOWER_SUB_B).Append(T_SAVING).Append(T_SAVE_FAILED).Append(T_SUMMARY_A).Append(T_SUMMARY_SPENT_B)
        .Append(T_SUMMARY_GOT_B).Append(T_SUMMARY_NONE).Append(T_RULES).Append(SpaceWorld.STAR_FRAGMENT_NAME)
        .Append(SpaceMeeples.GlyphText).Append(IgCurrency.GlyphText).Append("+×・：、0123456789").ToString();

    // ================================================================
    // 遊べるか・開く
    // ================================================================
    /// <summary>遊べるか（積み木座が完成している。管理者は初めから）</summary>
    public static bool Unlocked
    {
        get
        {
            if (AdminClaim.IsAdmin) return true;
            var p = Constellations.Progress().Find(x => x.Def.Id == CONSTELLATION);
            return p != null && p.Complete;
        }
    }

    private static bool s_loading;
    private static float s_loadingAt;

    /// <summary>開いている（読み込み中も。10秒たっても開かなければ、読み込めなかったものとして扱う）</summary>
    public static bool Active =>
        CoinPusherController.Instance != null || (s_loading && Time.unscaledTime - s_loadingAt < 10f);

    /// <summary>コインプッシャーのシーンを開く（ホームの空の積み木座から）</summary>
    public static void Open()
    {
        if (Active) return;
        if (!Unlocked)
        {
            SpaceTheme.Toast(T_LOCKED);
            return;
        }
        var loader = SceneLoader.instance;
        if (loader == null)
        {
            Debug.LogWarning("[CoinPusher] SceneLoader がないので開けません");
            return;
        }
        s_loading = true;
        s_loadingAt = Time.unscaledTime;
        loader.MergeScene(SCENE);
    }

    /// <summary>シーンが読み込まれた（CoinPusherController.Awake から）</summary>
    internal static void MarkLoaded() => s_loading = false;

    // ================================================================
    // まだサーバーに送っていない増減
    // ================================================================
    [Serializable]
    public sealed class Ledger
    {
        public int spent;   // 使った G
        public int gold;    // 獲得した G
        public int red, blue, yellow; // 獲得したコマ
        public int star;    // 獲得した星のカケラ

        public bool Empty => spent == 0 && gold == 0 && red == 0 && blue == 0 && yellow == 0 && star == 0;
        public Ledger Copy() => (Ledger)MemberwiseClone();

        public void Subtract(Ledger o)
        {
            spent -= o.spent;
            gold -= o.gold;
            red -= o.red;
            blue -= o.blue;
            yellow -= o.yellow;
            star -= o.star;
        }
    }

    private static Ledger s_ledger;
    private static string s_ledgerUid;
    private static bool s_flushing, s_ledgerDirty;

    private static string Uid => UserDataManager.instance != null ? UserDataManager.instance.UID : null;
    private static UserData Data => UserDataManager.instance != null ? UserDataManager.instance.UserData : null;
    private static string LedgerKey(string uid) => "coin_pusher_pending_" + uid;

    /// <summary>台の並びを保存する端末の場所（アカウントごと）</summary>
    public static string FieldKey(string uid) => "coin_pusher_field_v1_" + uid;

    /// <summary>
    /// まだ送っていない増減。アプリを起動して初めて読んだとき、前に送れなかった分があれば、
    /// 手元の数（サーバーから読んだばかりの値）にも足しておく
    /// </summary>
    public static Ledger Pending
    {
        get
        {
            var uid = Uid;
            if (string.IsNullOrEmpty(uid)) return null;
            if (s_ledger != null && s_ledgerUid == uid) return s_ledger;
            s_ledgerUid = uid;
            s_ledger = null;
            var json = PlayerPrefs.GetString(LedgerKey(uid), "");
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    s_ledger = JsonUtility.FromJson<Ledger>(json);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[CoinPusher] 送っていない記録を読めませんでした: {ex.Message}");
                }
            }
            if (s_ledger == null) s_ledger = new Ledger();
            if (!s_ledger.Empty) ApplyLocal(s_ledger);
            return s_ledger;
        }
    }

    private static void ApplyLocal(Ledger l)
    {
        var d = Data;
        if (d == null) return;
        d.Ig += l.gold - l.spent;
        d.RedMeeple += l.red;
        d.BlueMeeple += l.blue;
        d.YellowMeeple += l.yellow;
        d.StarFragment += l.star;
    }

    /// <summary>コインを入れた（手元の所持金はすぐ減らす）</summary>
    public static void RecordSpend(int n)
    {
        var l = Pending;
        var d = Data;
        if (l == null || d == null) return;
        l.spent += n;
        d.Ig -= n;
        s_ledgerDirty = true;
    }

    /// <summary>手前に落ちた物を獲得した（手元の数はすぐ増やす）</summary>
    public static void RecordWin(PusherItem item)
    {
        var l = Pending;
        var d = Data;
        if (l == null || d == null) return;
        switch (item)
        {
            case PusherItem.Coin:
                l.gold++;
                d.Ig++;
                break;
            case PusherItem.MeepleRed:
                l.red++;
                d.RedMeeple++;
                break;
            case PusherItem.MeepleBlue:
                l.blue++;
                d.BlueMeeple++;
                break;
            case PusherItem.MeepleYellow:
                l.yellow++;
                d.YellowMeeple++;
                break;
            case PusherItem.StarFragment:
                l.star++;
                d.StarFragment++;
                break;
        }
        s_ledgerDirty = true;
    }

    /// <summary>送っていない増減を端末に書く（変わっていなければ何もしない）</summary>
    public static void Persist(bool force = false)
    {
        if (s_ledger == null || string.IsNullOrEmpty(s_ledgerUid)) return;
        if (!s_ledgerDirty && !force) return;
        PlayerPrefs.SetString(LedgerKey(s_ledgerUid), JsonUtility.ToJson(s_ledger));
        s_ledgerDirty = false;
    }

    /// <summary>送っていない増減をサーバーに送る（FieldValue.Increment）。送れたら（送る物がなくても）true</summary>
    public static async UniTask<bool> FlushAsync()
    {
        var l = Pending;
        var uid = s_ledgerUid;
        if (l == null || string.IsNullOrEmpty(uid)) return false;
        if (l.Empty) return true;
        if (s_flushing) return false;
        var snap = l.Copy();
        var updates = new Dictionary<string, object>();
        int net = snap.gold - snap.spent;
        if (net != 0) updates[IgCurrency.FIELD] = FieldValue.Increment(net);
        if (snap.red != 0) updates[SpaceMeeples.Of(PlanetColor.Red).Field] = FieldValue.Increment(snap.red);
        if (snap.blue != 0) updates[SpaceMeeples.Of(PlanetColor.Blue).Field] = FieldValue.Increment(snap.blue);
        if (snap.yellow != 0) updates[SpaceMeeples.Of(PlanetColor.Yellow).Field] = FieldValue.Increment(snap.yellow);
        if (snap.star != 0) updates[SpaceWorld.STAR_FRAGMENT_FIELD] = FieldValue.Increment(snap.star);
        if (updates.Count > 0)
        {
            s_flushing = true;
            try
            {
                await FirebaseFirestore.DefaultInstance.Collection("users").Document(uid).UpdateAsync(updates).AsUniTask();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[CoinPusher] 結果を送れませんでした: {ex.Message}");
                return false;
            }
            finally
            {
                s_flushing = false;
            }
        }
        if (s_ledger != l) return true; // 送っているあいだにアカウントが変わった
        l.Subtract(snap);
        Persist(true);
        PlayerPrefs.Save();
        return true;
    }

    // ================================================================
    // 星のカケラ（5個で流れ星。ショップ・INTALES WORLD と同じ決まり）
    // ================================================================
    public sealed class StarExchange
    {
        public int Stars;  // 見る流れ星の数
        public int Left;   // 残った星のカケラ
        public readonly JourneyResult Star = new JourneyResult(); // 流れ星の当たり（Prizes）・ノワール（Unlocked）
        internal readonly Dictionary<string, long> Coupons = new Dictionary<string, long>();
    }

    /// <summary>
    /// 星のカケラが5個以上なら、5個ごとに流れ星を1回引く（入店のときと同じ当たりやすさ）。引いたら結果を返す（なければ null）
    /// </summary>
    public static async UniTask<StarExchange> ExchangeStarFragmentsAsync()
    {
        var m = UserDataManager.instance;
        if (m == null || string.IsNullOrEmpty(m.UID) || m.UserData == null) return null;
        if (m.UserData.StarFragment < SpaceWorld.STAR_FRAGMENTS_PER_STAR) return null;
        // トランザクションは裏のスレッドで動くので、Unity の値は先に読んでおく
        string centerId = SpacePlanets.Current.Id;
        var weekday = StoreCalendar.TodayBusiness.DayOfWeek;
        var season = StoreCalendar.CurrentSeason;
        var rng = new System.Random(unchecked(Environment.TickCount * 17 + m.UID.GetHashCode()));
        var db = FirebaseFirestore.DefaultInstance;
        var doc = db.Collection("users").Document(m.UID);
        StarExchange res;
        try
        {
            res = await db.RunTransactionAsync(tx => tx.GetSnapshotAsync(doc).ContinueWith(t =>
            {
                var u = t.Result.Exists ? t.Result.ToDictionary() : null;
                if (u == null) return null;
                long n = Math.Max(0, Long(u, SpaceWorld.STAR_FRAGMENT_FIELD));
                int stars = (int)(n / SpaceWorld.STAR_FRAGMENTS_PER_STAR);
                if (stars <= 0) return null;
                var r = new StarExchange { Stars = stars, Left = (int)(n % SpaceWorld.STAR_FRAGMENTS_PER_STAR) };
                var j = JourneyData.From(u.TryGetValue("journey", out var raw) ? raw : null);
                var owned = JourneyService.OwnedPlanetsOf(u);
                for (int i = 0; i < stars; i++)
                {
                    int before = r.Star.Prizes.Count;
                    JourneyRules.AddPrize(JourneyRules.Roll(rng, j?.Streak ?? 0, centerId, weekday, season), owned, r.Star);
                    for (int k = before; k < r.Star.Prizes.Count; k++)
                    {
                        var coupon = JourneyService.CouponOf(r.Star.Prizes[k]);
                        if (coupon == null) continue;
                        r.Coupons[coupon.Field] = (r.Coupons.TryGetValue(coupon.Field, out var c) ? c : 0) + 1;
                    }
                }
                var updates = new Dictionary<string, object> { { SpaceWorld.STAR_FRAGMENT_FIELD, (long)r.Left } };
                foreach (var kv in r.Coupons) updates[kv.Key] = FieldValue.Increment(kv.Value);
                if (r.Star.Unlocked.Count > 0)
                    updates["planets"] = FieldValue.ArrayUnion(r.Star.Unlocked.Cast<object>().ToArray());
                tx.Update(doc, updates);
                return r;
            })).AsUniTask();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[CoinPusher] 星のカケラを流れ星にできませんでした: {ex.Message}");
            return null;
        }
        if (res == null) return null;

        var d = m.UserData;
        d.StarFragment = res.Left;
        foreach (var kv in res.Coupons) SpaceCoupons.ByField(kv.Key)?.Add(d, (int)kv.Value);
        foreach (var id in res.Star.Unlocked)
            if (!d.Planets.Contains(id)) d.Planets.Add(id);
        if (res.Star.Unlocked.Count > 0) SpacePlanets.MarkNews();
        LocalHistoryLog.Add("fragment",
            $"{SpaceWorld.STAR_FRAGMENT_NAME}が{SpaceWorld.STAR_FRAGMENTS_PER_STAR}個そろって 流れ星を見た"
            + (res.Stars > 1 ? $"（{res.Stars}回）" : ""));
        foreach (var p in res.Star.Prizes)
        {
            var c = JourneyService.CouponOf(p);
            if (c != null) LocalHistoryLog.Add("coupon", $"流れ星で {c.Name} を1枚 入手");
        }
        foreach (var id in res.Star.Unlocked)
        {
            var def = SpacePlanets.Find(id);
            if (def != null) LocalHistoryLog.Add("planet", $"惑星「{def.Name}」を解放");
        }
        JourneyService.NotifyChanged();
        return res;
    }

    private static long Long(Dictionary<string, object> m, string k)
    {
        if (m == null || !m.TryGetValue(k, out var v) || v == null) return 0;
        try { return Convert.ToInt64(v); }
        catch { return 0; }
    }

    // ================================================================
    // 履歴・終わったときの一言
    // ================================================================
    /// <summary>「10G 使って 6G・赤コマ1個・星のカケラ2個 を獲得」（何もしていなければ null）</summary>
    public static string SessionText(int spent, int gold, int red, int blue, int yellow, int star)
    {
        if (spent == 0 && gold + red + blue + yellow + star == 0) return null;
        var parts = new List<string>();
        if (gold > 0) parts.Add(IgCurrency.Format(gold));
        if (red > 0) parts.Add(SpaceMeeples.Of(PlanetColor.Red).Name + red + "個");
        if (blue > 0) parts.Add(SpaceMeeples.Of(PlanetColor.Blue).Name + blue + "個");
        if (yellow > 0) parts.Add(SpaceMeeples.Of(PlanetColor.Yellow).Name + yellow + "個");
        if (star > 0) parts.Add(SpaceWorld.STAR_FRAGMENT_NAME + star + "個");
        string head = spent > 0 ? IgCurrency.Format(spent) + T_SUMMARY_SPENT_B : "";
        return parts.Count > 0 ? head + string.Join("・", parts) + T_SUMMARY_GOT_B : head + T_SUMMARY_NONE;
    }
}
