using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Firebase.Firestore;
using UnityEngine;

/// <summary>INTALES WORLD の置き場の効果</summary>
public enum WorldEffect
{
    Star,    // 流れ星を見る（入店のときと同じ流れ星を1回）
    Planets, // 惑星を3つアクティブにする（力を失った惑星に力を戻す）
    Meeples, // 各色のコマを1個ずつもらう
    Coupon,  // 5%OFFチケットをもらう
}

/// <summary>INTALES WORLD の置き場1つ分</summary>
public sealed class SpaceWorldArea
{
    public int Index;
    public WorldEffect Effect;
    public string Name;   // 置き場の名前（「星見の丘」など）
    public string En;     // 英字の名前（見出し）
    public string Text;   // 効果（ユーザー指定の文言そのまま）
    /// <summary>地図の上の位置（左上が 0,0・右下が 1,1 の割合。tools/world/make_world_map.py の SPOTS と同じ）</summary>
    public Vector2 Pos;
    public Color Accent;
}

/// <summary>
/// INTALES WORLD（2026-11-10 ユーザー指定）。コマ（SpaceMeeples）を使う遊びで、ボードゲーム図鑑と同じく
/// 使いたい人だけが歯車メニューからダウンロードする（端末に記録。PlayerPrefs）。
///  ・ダウンロードしていない人: 所持品のコマの欄を出さない・入店でコマをもらえない・惑星が力を失うこともない
///  ・ダウンロードした人: ホームの所持品の下に「INTALES WORLD」のボタン → オリジナルの世界地図（SpaceWorldMap）
/// 世界地図には置き場が4か所あり、コマを1体置くと、その置き場の効果をもらえる（ワーカープレイスメント）。
///  ・その日まだ誰も置いていない置き場には、好きな色を置ける。1体でも置かれたら、その日は同じ色しか置けない（マストフォロー）
///  ・1人が置けるのは1日1体だけ（どこか1か所）／置いたコマは使い切り（戻らない）／置けるのは入店中だけ
///    （以上 2026-11-10 ユーザーの回答）
///  ・置き場ごとの色と数は全員で共有して、リアルタイムに同期する（world/{営業日}。毎朝4時の営業日の変わり目でリセット）
/// 置くのは1回のトランザクションで、置き場の数・自分のコマ・その日に置いた記録・効果をまとめて書く。
/// </summary>
public static class SpaceWorld
{
    private const string PREF_KEY = "world_installed";
    public const string T_TITLE = "INTALES WORLD";

    // 置けなかったときの知らせ
    public const string T_ERR_USER = "ユーザー情報を読み込めませんでした";
    public const string T_ERR_NOT_IN_STORE = "コマを置けるのは入店中だけです";
    public const string T_ERR_PLACED = "今日はもうコマを置きました（1日1体まで）";
    public const string T_ERR_NO_MEEPLE = "その色のコマを持っていません";
    public const string T_ERR_COLOR = "ほかの人が先に別の色のコマを置きました。もう一度選んでください";
    public const string T_ERR_NO_EXHAUSTED = "力を失った惑星がありません";
    public const string T_ERR_PICK = "力を戻す惑星を選んでください";
    public const string T_ERR_NETWORK = "通信に失敗しました。もう一度お試しください";

    public static readonly SpaceWorldArea[] Areas =
    {
        new SpaceWorldArea
        {
            Index = 0, Effect = WorldEffect.Star, Name = "星見の丘", En = "STARGAZER HILL", Text = "流れ星を見る",
            Pos = new Vector2(0.30f, 0.21f), Accent = SpaceTheme.Hex(0x9EC9FF),
        },
        new SpaceWorldArea
        {
            Index = 1, Effect = WorldEffect.Planets, Name = "惑星の神殿", En = "PLANET TEMPLE", Text = "惑星を3つアクティブにする",
            Pos = new Vector2(0.75f, 0.37f), Accent = SpaceTheme.Hex(0xC6A8FF),
        },
        new SpaceWorldArea
        {
            Index = 2, Effect = WorldEffect.Meeples, Name = "コマの工房", En = "PIECE WORKSHOP", Text = "各色のコマを1個ずつもらう",
            Pos = new Vector2(0.27f, 0.60f), Accent = SpaceTheme.Hex(0xFFB37A),
        },
        new SpaceWorldArea
        {
            // 2026-11-10 ユーザー指定で「5%OFFチケット」から「5%OFFチケットのカケラ（2枚で完成）」に変更
            Index = 3, Effect = WorldEffect.Coupon, Name = "商人の港", En = "MERCHANT PORT", Text = "5%OFFチケットのカケラをもらう",
            Pos = new Vector2(0.70f, 0.80f), Accent = SpaceTheme.Hex(0x6FA8FF),
        },
    };

    /// <summary>「惑星を3つアクティブにする」で力を戻す数</summary>
    public const int REVIVE_COUNT = 3;

    // ---- 5%OFFチケットのカケラ（2026-11-10 ユーザー指定「2枚集めると完成」） ----
    public const string FRAGMENT_FIELD = "five_fragment";
    public const string FRAGMENT_NAME = "5%OFFチケットのカケラ";
    /// <summary>5%OFFチケット1枚になるカケラの数</summary>
    public const int FRAGMENTS_PER_COUPON = 2;

    private static Sprite _fragmentIcon, _fragmentIconRight;

    /// <summary>
    /// カケラの絵（5%OFFチケットを破った左半分＝「5」の側。所持品の絵にも使う。
    /// Resources/ItemIcon/FiveCouponFragment。tools/world/make_fragment_icon.py で作る）
    /// </summary>
    public static Sprite FragmentIcon =>
        _fragmentIcon != null ? _fragmentIcon : (_fragmentIcon = Resources.Load<Sprite>("ItemIcon/FiveCouponFragment"));

    /// <summary>2枚目のカケラの絵（右半分＝「%」の側。左と同じ破れ目なので、並べると元のチケットになる）</summary>
    public static Sprite FragmentIconRight =>
        _fragmentIconRight != null ? _fragmentIconRight
            : (_fragmentIconRight = Resources.Load<Sprite>("ItemIcon/FiveCouponFragmentR"));

    /// <summary>
    /// 管理者はデバッグ用に、1日1体の制限なしで何回でも置ける（2026-11-10 ユーザー指定。入店中だけ・色の決まりはそのまま）
    /// </summary>
    public static bool Unlimited => AdminClaim.IsAdmin;

    /// <summary>今コマを置ける回数が残っているか（1日1体。管理者は何回でも）</summary>
    public static bool CanPlaceToday => Unlimited || !PlacedToday;

    // ================================================================
    // ダウンロード
    // ================================================================
    /// <summary>この端末でダウンロード済みか（コマを使う遊び全体のスイッチ）</summary>
    public static bool Installed => PlayerPrefs.GetInt(PREF_KEY, 0) == 1;

    /// <summary>ダウンロード・削除したとき</summary>
    public static event Action Changed;

    /// <summary>ダウンロードする（地図の絵を読み込み、今日の置き場を1回読めたら済みにする）。できたら true</summary>
    public static async UniTask<bool> DownloadAsync()
    {
        if (MapSprite == null) return false;
        try
        {
            var snap = await Doc(StoreCalendar.TodayKey).GetSnapshotAsync().AsUniTask();
            Board = Parse(StoreCalendar.TodayKey, snap);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[SpaceWorld] ダウンロードできませんでした: {ex.Message}");
            return false;
        }
        PlayerPrefs.SetInt(PREF_KEY, 1);
        PlayerPrefs.Save();
        Changed?.Invoke();
        return true;
    }

    /// <summary>削除する（ダウンロード済みを外すだけ。持っているコマ・力を失った惑星の記録は残す）</summary>
    public static void Remove()
    {
        PlayerPrefs.DeleteKey(PREF_KEY);
        PlayerPrefs.Save();
        StopListening();
        Changed?.Invoke();
    }

    private static Sprite _map;

    /// <summary>世界地図の絵（Resources/World/WorldMap。tools/world/make_world_map.py で作る）</summary>
    public static Sprite MapSprite => _map != null ? _map : (_map = Resources.Load<Sprite>("World/WorldMap"));

    // ================================================================
    // 置き場（全員で共有。営業日ごとの文書）
    // ================================================================
    /// <summary>その日の置き場の様子</summary>
    public sealed class WorldBoard
    {
        public string Day;
        public readonly PlanetColor[] Colors = new PlanetColor[4]; // まだ誰も置いていなければ None
        public readonly int[] Counts = new int[4];
    }

    public static WorldBoard Board { get; private set; } = new WorldBoard();

    /// <summary>置き場の様子が変わった（だれかが置いた・営業日が変わった）</summary>
    public static event Action BoardChanged;

    private static ListenerRegistration _listener;
    private static string _listenDay;

    private static DocumentReference Doc(string day) =>
        FirebaseFirestore.DefaultInstance.Collection("world").Document(day);

    /// <summary>今日の置き場をリアルタイムに受け取り始める（地図を開いている間だけ）</summary>
    public static void StartListening()
    {
        string day = StoreCalendar.TodayKey;
        if (_listener != null && _listenDay == day) return;
        StopListening();
        _listenDay = day;
        if (Board == null || Board.Day != day)
        {
            Board = new WorldBoard { Day = day };
            BoardChanged?.Invoke();
        }
        _listener = Doc(day).Listen(snap =>
        {
            if (_listenDay != day) return;
            Board = Parse(day, snap);
            BoardChanged?.Invoke();
        });
    }

    public static void StopListening()
    {
        _listener?.Stop();
        _listener = null;
        _listenDay = null;
    }

    /// <summary>受け取り中に営業日が変わったら（朝4時）、新しい日の置き場に切り替える</summary>
    public static void CheckDay()
    {
        if (_listener != null && _listenDay != StoreCalendar.TodayKey) StartListening();
    }

    private static WorldBoard Parse(string day, DocumentSnapshot snap)
    {
        var b = new WorldBoard { Day = day };
        if (snap == null || !snap.Exists) return b;
        var d = snap.ToDictionary();
        for (int i = 0; i < 4; i++)
        {
            b.Colors[i] = ColorOf(d.TryGetValue("c" + i, out var c) ? c as string : null);
            b.Counts[i] = (int)Long(d, "n" + i);
        }
        return b;
    }

    /// <summary>Firestore に書く色の名前</summary>
    public static string ColorKey(PlanetColor c) => c switch
    {
        PlanetColor.Red => "red",
        PlanetColor.Blue => "blue",
        PlanetColor.Yellow => "yellow",
        _ => "",
    };

    public static PlanetColor ColorOf(string key) => key switch
    {
        "red" => PlanetColor.Red,
        "blue" => PlanetColor.Blue,
        "yellow" => PlanetColor.Yellow,
        _ => PlanetColor.None,
    };

    /// <summary>「赤」「青」「黄」</summary>
    public static string ColorName(PlanetColor c) => c switch
    {
        PlanetColor.Red => "赤",
        PlanetColor.Blue => "青",
        PlanetColor.Yellow => "黄",
        _ => "",
    };

    // ================================================================
    // 自分の状態
    // ================================================================
    private static UserData Data => UserDataManager.instance != null ? UserDataManager.instance.UserData : null;

    /// <summary>今日（営業日）もうコマを置いたか</summary>
    public static bool PlacedToday => Data != null && Data.WorldDay == StoreCalendar.TodayKey;

    /// <summary>今日コマを置いた置き場（置いていなければ -1）</summary>
    public static int PlacedAreaToday => PlacedToday ? Data.WorldArea : -1;

    /// <summary>今コマを置けるか（入店中）</summary>
    public static bool InStore => JourneyService.InStoreToday;

    /// <summary>力を戻せる惑星（力を失った惑星。退店し忘れで営業日が変わった惑星も含む）</summary>
    public static List<SpacePlanetDef> ExhaustedPlanets()
    {
        var list = new List<SpacePlanetDef>();
        var j = JourneyService.Journey;
        if (j == null) return list;
        foreach (var id in JourneyRules.ExhaustedList(j, StoreCalendar.TodayKey))
        {
            var def = SpacePlanets.Find(id);
            if (def != null) list.Add(def);
        }
        return list;
    }

    // ================================================================
    // コマを置く
    // ================================================================
    /// <summary>置いた結果（お知らせに出す内容）</summary>
    public sealed class PlaceResult
    {
        public bool Ok;
        public string Error;
        public SpaceWorldArea Area;
        public PlanetColor Color;
        public int Count;                                     // 置いたあとの、その置き場の数
        public readonly JourneyResult Star = new JourneyResult(); // 流れ星の当たり（Prizes）・ノワール（Unlocked）
        public readonly List<string> Revived = new List<string>(); // 力を戻した惑星
        public int Fragments;                                 // 商人の港: もらったあとのカケラの数（完成したら0）
        public bool CouponCompleted;                          // 商人の港: カケラがそろって5%OFFチケットになった
        internal JourneyData Journey;                         // 力を戻したあとの来店の記録
        internal readonly Dictionary<string, long> Delta = new Dictionary<string, long>(); // 増減した数（フィールドごと）
    }

    /// <summary>
    /// 置き場 area に color のコマを1体置く。revive は「惑星を3つアクティブにする」で力を戻す惑星
    /// （力を失った惑星が3つ以下なら null でよい＝全部戻す）。結果を返す（失敗なら Ok=false と Error）
    /// </summary>
    public static async UniTask<PlaceResult> PlaceAsync(SpaceWorldArea area, PlanetColor color, IList<string> revive)
    {
        var fail = new PlaceResult { Area = area, Color = color };
        var m = UserDataManager.instance;
        if (area == null || m == null || string.IsNullOrEmpty(m.UID) || m.UserData == null)
        {
            fail.Error = T_ERR_USER;
            return fail;
        }
        if (!InStore)
        {
            fail.Error = T_ERR_NOT_IN_STORE;
            return fail;
        }

        string uid = m.UID;
        string day = StoreCalendar.TodayKey;
        string centerId = SpacePlanets.Current.Id;
        var weekday = StoreCalendar.TodayBusiness.DayOfWeek;
        var season = StoreCalendar.CurrentSeason;
        var rng = new System.Random(unchecked(Environment.TickCount * 13 + uid.GetHashCode()));
        var db = FirebaseFirestore.DefaultInstance;
        var boardRef = Doc(day);
        var userRef = db.Collection("users").Document(uid);
        bool unlimited = Unlimited; // 管理者は1日1体の制限なし（トランザクションは裏のスレッドで動くので先に読んでおく）

        PlaceResult res;
        try
        {
            res = await db.RunTransactionAsync(tx => tx.GetSnapshotAsync(boardRef).ContinueWith(tb =>
                tx.GetSnapshotAsync(userRef).ContinueWith(tu =>
                    Apply(tx, boardRef, userRef, tb.Result, tu.Result, area, color, revive, day, rng, centerId,
                        weekday, season, unlimited))).Unwrap()).AsUniTask();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[SpaceWorld] コマを置けませんでした: {ex.Message}");
            fail.Error = T_ERR_NETWORK;
            return fail;
        }
        if (res == null || !res.Ok) return res ?? fail;

        ApplyLocal(res, day);
        return res;
    }

    /// <summary>トランザクションの中身（何度呼ばれてもよいように、外の状態は変えない）</summary>
    private static PlaceResult Apply(Transaction tx, DocumentReference boardRef, DocumentReference userRef,
        DocumentSnapshot boardSnap, DocumentSnapshot userSnap, SpaceWorldArea area, PlanetColor color,
        IList<string> revive, string day, System.Random rng, string centerId, DayOfWeek weekday,
        StoreCalendar.Season season, bool unlimited)
    {
        var r = new PlaceResult { Area = area, Color = color };
        var u = userSnap != null && userSnap.Exists ? userSnap.ToDictionary() : new Dictionary<string, object>();
        if (!unlimited && Str(u, "world_day") == day)
        {
            r.Error = T_ERR_PLACED;
            return r;
        }
        var meeple = SpaceMeeples.Of(color);
        if (meeple == null || Long(u, meeple.Field) < 1)
        {
            r.Error = T_ERR_NO_MEEPLE;
            return r;
        }

        int a = area.Index;
        string cKey = "c" + a, nKey = "n" + a;
        var b = boardSnap != null && boardSnap.Exists ? boardSnap.ToDictionary() : null;
        string placed = b != null ? Str(b, cKey) ?? "" : "";
        if (placed.Length > 0 && placed != ColorKey(color))
        {
            r.Error = T_ERR_COLOR;
            return r;
        }
        long count = b != null ? Long(b, nKey) : 0;

        var updates = new Dictionary<string, object>();
        r.Delta[meeple.Field] = -1;
        switch (area.Effect)
        {
            case WorldEffect.Star:
            {
                var j = JourneyData.From(u.TryGetValue("journey", out var raw) ? raw : null);
                var owned = JourneyService.OwnedPlanetsOf(u);
                JourneyRules.AddPrize(JourneyRules.Roll(rng, j?.Streak ?? 0, centerId, weekday, season), owned, r.Star);
                foreach (var p in r.Star.Prizes)
                {
                    var coupon = JourneyService.CouponOf(p);
                    if (coupon != null) Add(r.Delta, coupon.Field, 1);
                }
                if (r.Star.Unlocked.Count > 0)
                    updates["planets"] = FieldValue.ArrayUnion(r.Star.Unlocked.Cast<object>().ToArray());
                break;
            }
            case WorldEffect.Planets:
            {
                var j = JourneyData.From(u.TryGetValue("journey", out var raw) ? raw : null);
                var candidates = j != null ? JourneyRules.ExhaustedList(j, day) : new List<string>();
                if (candidates.Count == 0)
                {
                    r.Error = T_ERR_NO_EXHAUSTED;
                    return r;
                }
                var pick = candidates.Count <= REVIVE_COUNT
                    ? candidates
                    : (revive ?? new List<string>()).Where(candidates.Contains).Distinct().Take(REVIVE_COUNT).ToList();
                if (pick.Count == 0)
                {
                    r.Error = T_ERR_PICK;
                    return r;
                }
                r.Revived.AddRange(JourneyRules.Reactivate(j, pick, day));
                updates["journey"] = j.ToMap();
                r.Journey = j;
                break;
            }
            case WorldEffect.Meeples:
                foreach (var each in SpaceMeeples.All) Add(r.Delta, each.Field, 1);
                break;
            case WorldEffect.Coupon:
            {
                // カケラを1枚。2枚そろったら5%OFFチケット1枚にして、カケラは0から
                long fragments = Long(u, FRAGMENT_FIELD) + 1;
                if (fragments >= FRAGMENTS_PER_COUPON)
                {
                    Add(r.Delta, "five_coupon", 1);
                    fragments -= FRAGMENTS_PER_COUPON;
                    r.CouponCompleted = true;
                }
                updates[FRAGMENT_FIELD] = fragments;
                r.Fragments = (int)fragments;
                break;
            }
        }
        foreach (var kv in r.Delta)
            if (kv.Value != 0) updates[kv.Key] = FieldValue.Increment(kv.Value);
        updates["world_day"] = day;
        updates["world_area"] = a;
        updates["world_color"] = ColorKey(color);

        if (b == null)
        {
            var doc = new Dictionary<string, object>();
            for (int i = 0; i < 4; i++)
            {
                doc["c" + i] = i == a ? ColorKey(color) : "";
                doc["n" + i] = i == a ? 1L : 0L;
            }
            tx.Set(boardRef, doc);
        }
        else
        {
            var bu = new Dictionary<string, object> { { nKey, count + 1 } };
            if (placed.Length == 0) bu[cKey] = ColorKey(color);
            tx.Update(boardRef, bu);
        }
        tx.Update(userRef, updates);
        r.Count = (int)(count + 1);
        r.Ok = true;
        return r;
    }

    /// <summary>書き込んだ内容を手元の UserData・置き場・履歴にも反映する</summary>
    private static void ApplyLocal(PlaceResult r, string day)
    {
        var d = Data;
        if (d != null)
        {
            foreach (var kv in r.Delta)
            {
                int n = (int)kv.Value;
                var mm = SpaceMeeples.All.FirstOrDefault(x => x.Field == kv.Key);
                if (mm != null) mm.Add(d, n);
                else SpaceCoupons.ByField(kv.Key)?.Add(d, n);
            }
            d.WorldDay = day;
            d.WorldArea = r.Area.Index;
            d.WorldColor = ColorKey(r.Color);
            if (r.Area.Effect == WorldEffect.Coupon) d.FiveFragment = r.Fragments;
            if (r.Journey != null) d.Journey = r.Journey;
            foreach (var id in r.Star.Unlocked)
                if (!d.Planets.Contains(id)) d.Planets.Add(id);
            if (r.Star.Unlocked.Count > 0) SpacePlanets.MarkNews();
        }

        // 置き場はすぐ手元でも1つ増やす（同期の知らせが届くまでの間も数が合うように）
        if (Board != null && Board.Day == day)
        {
            Board.Colors[r.Area.Index] = r.Color;
            Board.Counts[r.Area.Index] = Math.Max(Board.Counts[r.Area.Index], r.Count);
            BoardChanged?.Invoke();
        }

        var meeple = SpaceMeeples.Of(r.Color);
        LocalHistoryLog.Add("meeple", $"{T_TITLE} の{r.Area.Name}に {meeple?.Name} を置いた");
        switch (r.Area.Effect)
        {
            case WorldEffect.Star:
                foreach (var p in r.Star.Prizes)
                {
                    var c = JourneyService.CouponOf(p);
                    if (c != null) LocalHistoryLog.Add("coupon", $"流れ星で {c.Name} を1枚 入手");
                }
                foreach (var id in r.Star.Unlocked)
                {
                    var def = SpacePlanets.Find(id);
                    if (def != null) LocalHistoryLog.Add("planet", $"惑星「{def.Name}」を解放");
                }
                break;
            case WorldEffect.Planets:
                foreach (var id in r.Revived)
                {
                    var def = SpacePlanets.Find(id);
                    if (def != null) LocalHistoryLog.Add("planet", $"惑星「{def.Name}」が力を取り戻した");
                }
                break;
            case WorldEffect.Meeples:
                LocalHistoryLog.Add("meeple", $"{r.Area.Name}で {SpaceMeeples.Names(PlanetColorOrder.All)} を1個ずつ 入手");
                break;
            case WorldEffect.Coupon:
                LocalHistoryLog.Add("coupon", $"{r.Area.Name}で {FRAGMENT_NAME} を1枚 入手");
                if (r.CouponCompleted)
                    LocalHistoryLog.Add("coupon", $"カケラが{FRAGMENTS_PER_COUPON}枚そろって {SpaceCoupons.NameOf("five_coupon")} が完成");
                break;
        }
        JourneyService.NotifyChanged(); // ホームの惑星（力を戻した惑星の色）・所持品の数を更新
    }

    // ================================================================
    // 文字
    // ================================================================
    /// <summary>置き場の名前・効果・知らせ（NewHome の起動時にまとめてフォントへ登録する）</summary>
    public static string GlyphText
    {
        get
        {
            var sb = new StringBuilder(T_TITLE).Append(T_ERR_USER).Append(T_ERR_NOT_IN_STORE).Append(T_ERR_PLACED)
                .Append(T_ERR_NO_MEEPLE).Append(T_ERR_COLOR).Append(T_ERR_NO_EXHAUSTED).Append(T_ERR_PICK)
                .Append(T_ERR_NETWORK).Append("赤青黄").Append(FRAGMENT_NAME);
            foreach (var a in Areas) sb.Append(a.Name).Append(a.En).Append(a.Text);
            return sb.ToString();
        }
    }

    private static void Add(Dictionary<string, long> d, string key, long n) =>
        d[key] = (d.TryGetValue(key, out var v) ? v : 0) + n;

    private static long Long(Dictionary<string, object> m, string k)
    {
        if (m == null || !m.TryGetValue(k, out var v) || v == null) return 0;
        try { return Convert.ToInt64(v); }
        catch { return 0; }
    }

    private static string Str(Dictionary<string, object> m, string k) =>
        m != null && m.TryGetValue(k, out var v) && v != null ? v.ToString() : null;
}
