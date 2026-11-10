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
    Coupon,  // 5%OFFチケットのカケラをもらう（2枚で5%OFFチケット）
    StarFragment, // 星のカケラをもらう（5個で流れ星を1回。2026-11-11）
}

/// <summary>INTALES WORLD の置き場1つ分</summary>
public sealed class SpaceWorldArea
{
    public int Index;
    public WorldEffect Effect;
    public string Name;   // 置き場の名前（「星見の丘」など）
    public string En;     // 英字の名前（見出し）
    public string Text;   // 効果（ユーザー指定の文言そのまま）
    /// <summary>地図の札の3行目に出す短い補足（なければ null）</summary>
    public string Note;
    /// <summary>置き場のシートに出す説明（なければ null）</summary>
    public string Detail;
    /// <summary>
    /// 何色のコマでも置ける保険の置き場（マストフォローなし。2026-11-11 ユーザー指定）。
    /// 置き場の文書には色を決めず、色ごとの数（red4・blue4・yellow4 のように「色の名前＋番号」）を数える
    /// </summary>
    public bool AnyColor;
    /// <summary>地図の上の位置（左上が 0,0・右下が 1,1 の割合。tools/world/make_world_map.py の SPOTS と同じ）</summary>
    public Vector2 Pos;
    public Color Accent;
}

/// <summary>
/// INTALES WORLD（2026-11-10 ユーザー指定）。コマ（SpaceMeeples）を使う遊びで、ボードゲーム図鑑と同じく
/// 使いたい人だけが歯車メニューからダウンロードする（端末に記録。PlayerPrefs）。
///  ・ダウンロードしていない人: 所持品のコマの欄を出さない・入店でコマをもらえない・惑星が力を失うこともない
///  ・ダウンロードした人: ホームの所持品の下に「INTALES WORLD」のボタン → オリジナルの世界地図（SpaceWorldMap）
/// 世界地図には置き場が5か所あり、コマを1体置くと、その置き場の効果をもらえる（ワーカープレイスメント）。
///  ・その日まだ誰も置いていない置き場には、好きな色を置ける。1体でも置かれたら、その日は同じ色しか置けない（マストフォロー）
///  ・ただし左下の「星くずの島」だけは、何色でも置ける保険の置き場（2026-11-11 ユーザー指定。星のカケラ5個で流れ星1回）
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
            // 2026-11-10 ユーザー指定で「5%OFFチケット」から「5%OFFチケットのカケラ（2枚で完成）」に変更。
            // 2枚集めると完成することを置き場にも書く（2026-11-11 ユーザー指定）
            Index = 3, Effect = WorldEffect.Coupon, Name = "商人の港", En = "MERCHANT PORT", Text = "5%OFFチケットのカケラをもらう",
            Note = "2枚集めると5%OFFチケットが完成", Detail = "カケラを2枚集めると、5%OFFチケットが1枚完成します",
            Pos = new Vector2(0.70f, 0.80f), Accent = SpaceTheme.Hex(0x6FA8FF),
        },
        new SpaceWorldArea
        {
            // 地図の左下の小島（2026-11-11 ユーザー指定）。ここだけは何色のコマでも置ける保険の置き場
            Index = 4, Effect = WorldEffect.StarFragment, Name = "星くずの島", En = "STARDUST ISLE", Text = "星のカケラをもらう",
            Note = "何色でも置ける ・ 5個で流れ星", Detail = "ここだけは何色のコマでも置けます\n星のカケラを5個集めると、流れ星を1回見られます",
            AnyColor = true, Pos = new Vector2(0.125f, 0.855f), Accent = SpaceTheme.Hex(0xFFD36E),
        },
    };

    /// <summary>置き場の数（置き場の文書の c0..c3・n0..n3 と red4 などの番号）</summary>
    public const int AREA_COUNT = 5;

    /// <summary>「惑星を3つアクティブにする」で力を戻す数</summary>
    public const int REVIVE_COUNT = 3;

    // ---- 星のカケラ（2026-11-11 ユーザー指定「5個集めると1回だけ流れ星を見ることができる」） ----
    public const string STAR_FRAGMENT_FIELD = "star_fragment";
    public const string STAR_FRAGMENT_NAME = "星のカケラ";
    /// <summary>流れ星を1回見られる星のカケラの数</summary>
    public const int STAR_FRAGMENTS_PER_STAR = 5;

    private static Sprite _starFragmentIcon, _starFragmentPiece;

    /// <summary>星のカケラの絵（金色の五芒星の1片。Resources/ItemIcon/StarFragment。tools/world/make_star_fragment_icon.py で作る）</summary>
    public static Sprite StarFragmentIcon =>
        _starFragmentIcon != null ? _starFragmentIcon : (_starFragmentIcon = Resources.Load<Sprite>("ItemIcon/StarFragment"));

    /// <summary>星を組み立てる1片（星の中心＝絵の中心で上向き。72度ずつ回して5つ並べると星になる）</summary>
    public static Sprite StarFragmentPiece =>
        _starFragmentPiece != null ? _starFragmentPiece
            : (_starFragmentPiece = Resources.Load<Sprite>("ItemIcon/StarFragmentPiece"));

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
        public readonly PlanetColor[] Colors = new PlanetColor[AREA_COUNT]; // まだ誰も置いていなければ None（保険の置き場はいつも None）
        public readonly int[] Counts = new int[AREA_COUNT];                 // 置かれた数（保険の置き場は全色の合計）
        /// <summary>保険の置き場（AnyColor）の色ごとの数。[置き場, 色]（色は PlanetColorOrder.All の順＝赤・青・黄）</summary>
        public readonly int[,] ByColor = new int[AREA_COUNT, 3];

        /// <summary>置き場 area に置かれた color のコマの数</summary>
        public int CountOf(int area, PlanetColor color)
        {
            int k = Array.IndexOf(PlanetColorOrder.All, color);
            if (area < 0 || area >= AREA_COUNT || k < 0) return 0;
            if (Areas[area].AnyColor) return ByColor[area, k];
            return Colors[area] == color ? Counts[area] : 0;
        }
    }

    /// <summary>保険の置き場で色ごとの数を数えるフィールドの名前（「red4」など）</summary>
    public static string FreeKey(int area, PlanetColor color) => ColorKey(color) + area;

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
        for (int i = 0; i < AREA_COUNT; i++)
        {
            if (Areas[i].AnyColor)
            {
                // 保険の置き場: 色は決めず、色ごとの数（まだ誰も置いていない日・前の版のアプリが作った文書には無い）
                int sum = 0;
                for (int k = 0; k < PlanetColorOrder.All.Length; k++)
                {
                    int n = (int)Math.Max(0, Long(d, FreeKey(i, PlanetColorOrder.All[k])));
                    b.ByColor[i, k] = n;
                    sum += n;
                }
                b.Counts[i] = sum;
                continue;
            }
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
        public int Count;                                     // 置いたあとの、その置き場の数（保険の置き場は全色の合計）
        public int ColorCount;                                // 置いたあとの、その置き場のその色の数
        public readonly JourneyResult Star = new JourneyResult(); // 流れ星の当たり（Prizes）・ノワール（Unlocked）
        public readonly List<string> Revived = new List<string>(); // 力を戻した惑星
        public int Fragments;                                 // 商人の港: もらったあとのカケラの数（完成したら0）
        public bool CouponCompleted;                          // 商人の港: カケラがそろって5%OFFチケットになった
        public int StarFragments;                             // 星くずの島: もらったあとの星のカケラの数（そろったら0）
        public bool StarCompleted;                            // 星くずの島: 星のカケラがそろって流れ星を見られる（当たりは Star）
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
        string placed = "";
        long count, colorCount;
        if (area.AnyColor)
        {
            // 保険の置き場: 何色でも置ける（マストフォローなし）。色ごとに数える
            colorCount = b != null ? Math.Max(0, Long(b, FreeKey(a, color))) : 0;
            count = 0;
            if (b != null)
                foreach (var each in PlanetColorOrder.All) count += Math.Max(0, Long(b, FreeKey(a, each)));
        }
        else
        {
            placed = b != null ? Str(b, cKey) ?? "" : "";
            if (placed.Length > 0 && placed != ColorKey(color))
            {
                r.Error = T_ERR_COLOR;
                return r;
            }
            count = b != null ? Long(b, nKey) : 0;
            colorCount = count;
        }

        var updates = new Dictionary<string, object>();
        r.Delta[meeple.Field] = -1;
        switch (area.Effect)
        {
            case WorldEffect.Star:
                RollStar(u, r, updates, rng, centerId, weekday, season);
                break;
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
            case WorldEffect.StarFragment:
            {
                // 星のカケラを1個。5個そろったら流れ星を1回（入店のときと同じ当たりを引く）、カケラは0から
                long stars = Math.Max(0, Long(u, STAR_FRAGMENT_FIELD)) + 1;
                if (stars >= STAR_FRAGMENTS_PER_STAR)
                {
                    stars -= STAR_FRAGMENTS_PER_STAR;
                    r.StarCompleted = true;
                    RollStar(u, r, updates, rng, centerId, weekday, season);
                }
                updates[STAR_FRAGMENT_FIELD] = stars;
                r.StarFragments = (int)stars;
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
            // その日の最初の1体: 色の決まる置き場（c0..c3・n0..n3）を空で作り、置いた所だけ1にする。
            // 保険の置き場の色ごとの数は、そこに置いたときだけ足す（ほかの置き場なら前の版と同じ形の文書になる）
            var doc = new Dictionary<string, object>();
            for (int i = 0; i < AREA_COUNT; i++)
            {
                if (Areas[i].AnyColor) continue;
                doc["c" + i] = i == a ? ColorKey(color) : "";
                doc["n" + i] = i == a ? 1L : 0L;
            }
            if (area.AnyColor) doc[FreeKey(a, color)] = 1L;
            tx.Set(boardRef, doc);
        }
        else if (area.AnyColor)
        {
            tx.Update(boardRef, new Dictionary<string, object> { { FreeKey(a, color), colorCount + 1 } });
        }
        else
        {
            var bu = new Dictionary<string, object> { { nKey, count + 1 } };
            if (placed.Length == 0) bu[cKey] = ColorKey(color);
            tx.Update(boardRef, bu);
        }
        tx.Update(userRef, updates);
        r.Count = (int)(count + 1);
        r.ColorCount = (int)(colorCount + 1);
        r.Ok = true;
        return r;
    }

    /// <summary>
    /// 流れ星を1回引いて結果（r.Star）に足す。星見の丘と、星のカケラが5個そろったとき。
    /// 当たりやすさは入店のときと同じ（公転の数）。クーポンは数を増やし、ノワールは惑星に足す
    /// </summary>
    private static void RollStar(Dictionary<string, object> u, PlaceResult r, Dictionary<string, object> updates,
        System.Random rng, string centerId, DayOfWeek weekday, StoreCalendar.Season season)
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
            if (r.Area.Effect == WorldEffect.StarFragment) d.StarFragment = r.StarFragments;
            if (r.Journey != null) d.Journey = r.Journey;
            foreach (var id in r.Star.Unlocked)
                if (!d.Planets.Contains(id)) d.Planets.Add(id);
            if (r.Star.Unlocked.Count > 0) SpacePlanets.MarkNews();
        }

        // 置き場はすぐ手元でも1つ増やす（同期の知らせが届くまでの間も数が合うように）
        if (Board != null && Board.Day == day)
        {
            int a = r.Area.Index;
            if (r.Area.AnyColor)
            {
                int k = Array.IndexOf(PlanetColorOrder.All, r.Color);
                if (k >= 0) Board.ByColor[a, k] = Math.Max(Board.ByColor[a, k], r.ColorCount);
                int sum = 0;
                for (int i = 0; i < PlanetColorOrder.All.Length; i++) sum += Board.ByColor[a, i];
                Board.Counts[a] = Math.Max(sum, r.Count);
            }
            else
            {
                Board.Colors[a] = r.Color;
                Board.Counts[a] = Math.Max(Board.Counts[a], r.Count);
            }
            BoardChanged?.Invoke();
        }

        var meeple = SpaceMeeples.Of(r.Color);
        LocalHistoryLog.Add("meeple", $"{T_TITLE} の{r.Area.Name}に {meeple?.Name} を置いた");
        switch (r.Area.Effect)
        {
            case WorldEffect.Star:
                LogStar(r);
                break;
            case WorldEffect.StarFragment:
                LocalHistoryLog.Add("fragment", $"{r.Area.Name}で {STAR_FRAGMENT_NAME} を1個 入手");
                if (r.StarCompleted)
                {
                    LocalHistoryLog.Add("fragment", $"{STAR_FRAGMENT_NAME}が{STAR_FRAGMENTS_PER_STAR}個そろって 流れ星を見た");
                    LogStar(r);
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

    /// <summary>流れ星の当たり（クーポン・ノワール）を履歴に残す</summary>
    private static void LogStar(PlaceResult r)
    {
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
                .Append(T_ERR_NETWORK).Append("赤青黄").Append(FRAGMENT_NAME).Append(STAR_FRAGMENT_NAME);
            foreach (var a in Areas) sb.Append(a.Name).Append(a.En).Append(a.Text).Append(a.Note).Append(a.Detail);
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
