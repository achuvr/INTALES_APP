using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Cysharp.Threading.Tasks;
using Firebase.Firestore;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>ショップの品物1つ分の定義（Items に足せば品物が増える）</summary>
public sealed class SpaceShopItem
{
    public string Id;      // 品物のID
    public string Field;   // users/{uid} のフィールド名（買うとここが増える）
    public string Name;    // 「赤コマ」
    public string Desc;    // カードと買う画面の説明
    public int Price;      // 1個の値段（G）
    public Color Tint;     // カードの光・縁の色
    public Func<Sprite> Icon;
    public Func<UserData, int> Owned;
    public Action<UserData, int> Add;
    /// <summary>星のカケラ（5個そろうと、その場で流れ星を1回。INTALES WORLD の星くずの島と同じ決まり）</summary>
    public bool StarFragment;
    /// <summary>トランプを山札から引く（2026-11-12。Field の "cards" は手札と山札のまとまり。数を足すのではなく引く）</summary>
    public bool Card;
    /// <summary>並べるかどうか（null ならいつも並べる。トランプは宝石座か切り札座が完成した人だけ）</summary>
    public Func<bool> Visible;
    /// <summary>持っている数の書き方（null なら「持っている数 3個」）</summary>
    public Func<UserData, string> OwnedText;
    /// <summary>1回に買える数の上限（null なら 99。トランプは手札が8枚になるまで）</summary>
    public Func<int> Limit;
    /// <summary>いま買えない理由（null なら買える。トランプは手札が8枚のとき）</summary>
    public Func<string> Blocked;
    /// <summary>数の単位（null なら「個」）</summary>
    public string Unit;
}

/// <summary>
/// ショップ（2026-11-12 ユーザー指定「通貨座（＝金貨座）がアクティブになると、通貨座をタップしてショップを開けるように。
/// ショップはメニューの青っぽい色とは違って緑色をベースに。各色のミープル 1体5G・星のカケラ 2G。今後増える可能性あり」）。
/// 金貨座が完成した人（管理者は初めから）だけホームの空に金貨座が出て（SpaceBackground）、それをタップすると開く全画面。
/// アプリ内の通貨（単位 G。IgCurrency）で買う:
///  ・赤コマ・青コマ・黄コマ … 1個 5G（SpaceMeeples と同じフィールド。職人座が未完成でも所持品に出る）
///  ・黒コマ … 1個 7G（2026-11-12 追加。切り札座が完成した人だけ並ぶ。何色にでもなるコマ。SpaceMeeples.Black）
///  ・星のカケラ … 1個 2G（INTALES WORLD の星くずの島と同じ star_fragment。5個そろうと、その場で流れ星を1回）
///  ・惑星回復薬 … 1個 3G（2026-11-12 追加。所持品で使うと、力を失った惑星1つに力を戻す。SpacePlanetPotion）
///  ・トランプを引く … 1枚 3G（2026-11-12 追加。宝石座か切り札座が完成した人だけ並ぶ。来店のときと同じ山札から引く。CardDeck）
/// 品物を押すと個数を選ぶ画面 → 「購入する」。買うのは users を1回読むトランザクションで、
/// 所持金が足りるかをその場で確かめてから、所持金と品物をまとめて書く（足りなければ何も書かない）。
/// Android の戻るは NewHomeController が Instance.HandleBack を呼ぶ。
/// </summary>
public class SpaceShop : MonoBehaviour
{
    /// <summary>コマ1個の値段（G）</summary>
    public const int MEEPLE_PRICE = 5;
    /// <summary>星のカケラ1個の値段（G）</summary>
    public const int STAR_FRAGMENT_PRICE = 2;
    private const int MAX_COUNT = 99; // 1回に買える数

    // ---- 緑の配色（ふだんの紺のガラスとは分ける） ----
    /// <summary>ショップの色（見出し・縁・光）</summary>
    public static readonly Color GREEN = SpaceTheme.Hex(0x4DE8A0);
    private static readonly Color GREEN_LIGHT = SpaceTheme.Hex(0x9AF7CF);
    private static readonly Color GREEN_DEEP = SpaceTheme.Hex(0x1C9A63);
    private static readonly Color BG = new Color(0.008f, 0.042f, 0.027f, 0.985f);       // 背景（深い緑）
    private static readonly Color CARD = new Color(0.035f, 0.150f, 0.095f, 0.82f);      // 品物のカード
    private static readonly Color CARD_DEEP = new Color(0.018f, 0.088f, 0.056f, 0.97f); // 所持金・買う画面
    private static readonly Color BUTTON_DARK = new Color(0.03f, 0.13f, 0.08f, 0.88f);  // 閉じる・キャンセル・＋−
    private static readonly Color SHEET_DIM = new Color(0.004f, 0.025f, 0.015f, 0.72f);

    public const string T_CAPTION = "SHOP";
    public const string T_TITLE = "ショップ";
    private const string T_WALLET = "所持金";
    private const string T_WALLET_SUB_A = "来店するたびに ";
    private const string T_WALLET_SUB_B = " もらえます";
    private const string T_SECTION = "商品";
    private const string T_OWNED = "持っている数 ";
    private const string T_UNIT = "個";
    private const string T_FOOTER = "買ったものは所持品に入ります";
    private const string T_TOTAL = "合計 ";
    private const string T_AFTER = "所持金 ";
    private const string T_ARROW = " → ";
    private const string T_CANCEL = "キャンセル";
    private const string T_BUY = "購入する";
    private const string T_BUYING = "購入中…";
    private const string T_SHORT = IgCurrency.UNIT + " が足りません";
    private const string T_BOUGHT_A = " を";
    private const string T_BOUGHT_B = "個 買いました";
    private const string T_ERR_USER = "ユーザー情報を読み込めませんでした";
    private const string T_ERR_LOCKED = "金貨座を完成させると、ショップが使えます";
    private const string T_ERR_NETWORK = "通信に失敗しました。もう一度お試しください";
    private const string T_MEEPLE_DESC = "INTALES WORLD で使うコマ";
    private const string T_BLACK_DESC = "どの色の置き場にも置けるコマ"; // 3列のカードで1行に収まる長さ
    private const string T_STAR_DESC = "5個で流れ星を1回見られます"; // 3列のカードで1行に収まる長さ
    private const string T_HISTORY_A = "ショップで ";
    private const string T_HISTORY_B = "個 購入（";
    private const string T_HISTORY_STAR = "個そろって 流れ星を見た";
    private const string T_CARD_NAME = "トランプを引く";
    private const string T_CARD_DESC = "山札から1枚引きます（1日1回）"; // 3列のカード（文字は枠に合わせて縮む）
    private const string T_CARD_UNIT = "枚";
    private const string T_CARD_LOCKED = "宝石座か切り札座を完成させると、トランプが引けます";
    private const string T_CARD_HISTORY_A = "ショップで トランプを ";
    private const string T_CARD_HISTORY_B = "枚 引いた（";

    /// <summary>売っている品物（並びのとおりにカードを出す）。今後ふえる予定（2026-11-12 ユーザー）</summary>
    public static readonly SpaceShopItem[] Items = BuildItems();

    /// <summary>いま並べる品物（Visible が false の品物を除く）</summary>
    private static List<SpaceShopItem> VisibleItems() => Items.Where(i => i.Visible == null || i.Visible()).ToList();

    private static SpaceShopItem[] BuildItems()
    {
        var list = new List<SpaceShopItem>();
        foreach (var m in SpaceMeeples.All)
        {
            var meeple = m;
            list.Add(new SpaceShopItem
            {
                Id = meeple.Field, Field = meeple.Field, Name = meeple.Name, Desc = T_MEEPLE_DESC, Price = MEEPLE_PRICE,
                Tint = meeple.Tint, Icon = () => meeple.Icon, Owned = meeple.Get, Add = meeple.Add,
            });
        }
        // 黒コマ（2026-11-12 ユーザー指定「切り札座が完成するとショップに黒コマが並ぶ。何色にでもなるコマで、
        // 既に色が決まっているエリアにも置くことが出来る。7Gで購入できる」）。3色のコマのすぐ後ろに並べる
        var black = SpaceMeeples.Black;
        list.Add(new SpaceShopItem
        {
            Id = black.Field, Field = black.Field, Name = black.Name, Desc = T_BLACK_DESC, Price = SpaceMeeples.BLACK_PRICE,
            Tint = black.Tint, Icon = () => black.Icon, Owned = black.Get, Add = black.Add,
            Visible = () => SpaceMeeples.BlackUnlocked,
        });
        list.Add(new SpaceShopItem
        {
            Id = SpaceWorld.STAR_FRAGMENT_FIELD, Field = SpaceWorld.STAR_FRAGMENT_FIELD, Name = SpaceWorld.STAR_FRAGMENT_NAME,
            Desc = T_STAR_DESC, Price = STAR_FRAGMENT_PRICE, Tint = SpaceTheme.Hex(0xFFD36E),
            Icon = () => SpaceWorld.StarFragmentIcon, Owned = d => d.StarFragment, Add = (d, n) => d.StarFragment += n,
            StarFragment = true,
        });
        // 惑星回復薬（2026-11-12 ユーザー指定。3G。所持品のいちばん下の行を押すと、力を失った惑星1つに力を戻す）
        list.Add(new SpaceShopItem
        {
            Id = SpacePlanetPotion.FIELD, Field = SpacePlanetPotion.FIELD, Name = SpacePlanetPotion.NAME,
            Desc = SpacePlanetPotion.SHOP_DESC, Price = SpacePlanetPotion.PRICE, Tint = SpacePlanetPotion.TINT,
            Icon = () => SpacePlanetPotion.Icon, Owned = d => d.PlanetPotion, Add = (d, n) => d.PlanetPotion += n,
        });
        // トランプを引く（2026-11-12 ユーザー指定「切り札座か宝石座が完成していたら、山札からカードを引くのを購入できるように。3Gで」）。
        // 来店のときと同じ山札から引く（同じカードは2枚持てない・引き切るまで同じカードは出ない）。手札が8枚になったら1枚捨てる。
        // 引けるのは1日1回・1枚まで（同日ユーザー指定「ショップでカードを購入できるのは1日1回まで」。cards.shop_day）
        list.Add(new SpaceShopItem
        {
            Id = "card_draw", Field = CardDeck.FIELD, Name = T_CARD_NAME, Desc = T_CARD_DESC, Price = CardDeck.SHOP_PRICE,
            Tint = CardDeck.ACCENT, Icon = () => CardDeck.DrawIcon, Owned = d => CardDeck.Current.Hand.Count,
            Add = (d, n) => { }, Card = true, Unit = T_CARD_UNIT, Visible = () => CardDeck.Unlocked,
            OwnedText = d => CardDeck.ShopDrawnToday
                ? CardDeck.T_SHOP_DONE_SHORT
                : CardDeck.T_HAND_A + CardDeck.Current.Hand.Count + " / " + CardRules.HAND_MAX,
            Limit = () => CardDeck.ShopDrawnToday ? 0 : Mathf.Clamp(CardRules.HAND_MAX + 1 - CardDeck.Current.Hand.Count, 0, 1),
            Blocked = () => CardDeck.ShopDrawnToday ? CardDeck.T_SHOP_DONE
                : CardDeck.MustDiscard ? CardDeck.T_ERR_DISCARD_FIRST : null,
        });
        return list.ToArray();
    }

    /// <summary>この画面で表示しうる文字（NewHome の起動時にまとめてフォントへ登録する）</summary>
    public static string GlyphText
    {
        get
        {
            var sb = new StringBuilder()
                .Append(T_CAPTION).Append(T_TITLE).Append(T_WALLET).Append(T_WALLET_SUB_A).Append(T_WALLET_SUB_B)
                .Append(T_SECTION).Append(T_OWNED).Append(T_UNIT).Append(T_FOOTER).Append(T_TOTAL).Append(T_AFTER)
                .Append(T_ARROW).Append(T_CANCEL).Append(T_BUY).Append(T_BUYING).Append(T_SHORT).Append(T_BOUGHT_A)
                .Append(T_BOUGHT_B).Append(T_ERR_USER).Append(T_ERR_LOCKED).Append(T_ERR_NETWORK).Append(T_HISTORY_A)
                .Append(T_HISTORY_B).Append(T_HISTORY_STAR).Append("（）/×").Append(IgCurrency.GlyphText)
                .Append(T_CARD_UNIT).Append(T_CARD_LOCKED).Append(T_CARD_HISTORY_A).Append(T_CARD_HISTORY_B);
            foreach (var item in Items) sb.Append(item.Name).Append(item.Desc);
            return sb.ToString();
        }
    }

    // ================================================================
    // 買う（トランザクション）
    // ================================================================
    /// <summary>買った結果</summary>
    public sealed class Purchase
    {
        public bool Ok;
        public string Error;
        public SpaceShopItem Item;
        public int Count;          // 買った数
        public int Cost;           // 払った G
        public int Balance;        // 買ったあとの所持金
        public int OwnedAfter;     // 買ったあとのその品物の数（星のカケラはそろった分を引いたあと）
        public int Stars;          // 星のカケラがそろって見る流れ星の数
        // 流れ星の当たり（Prizes）・ノワール（Unlocked）・当たりで増えた物（Rewards。クーポン・持ち物）
        public readonly JourneyResult Star = new JourneyResult();
        public readonly List<int> Cards = new List<int>(); // 引いたトランプ（引いた順）
        public bool CardOverflow;                          // 引いて手札が8枚になった（このあと1枚捨てる）
        internal Dictionary<string, object> CardsMap;      // 書き込んだ手札と山札
    }

    private static UserData Data => UserDataManager.instance != null ? UserDataManager.instance.UserData : null;

    /// <summary>
    /// item を count 個買う。所持金が足りなければ何も書かずに Ok=false。
    /// 星のカケラは、そろった分（5個ごと）だけその場で流れ星を引き（入店のときと同じ当たり）、残りをカケラにする
    /// </summary>
    public static async UniTask<Purchase> BuyAsync(SpaceShopItem item, int count)
    {
        var fail = new Purchase { Item = item, Count = count };
        var m = UserDataManager.instance;
        if (item == null || count <= 0 || m == null || string.IsNullOrEmpty(m.UID) || m.UserData == null)
        {
            fail.Error = T_ERR_USER;
            return fail;
        }
        if (!IgCurrency.Unlocked)
        {
            fail.Error = T_ERR_LOCKED;
            return fail;
        }
        if (item.Card && !CardDeck.Unlocked)
        {
            fail.Error = T_CARD_LOCKED;
            return fail;
        }

        // トランザクションは裏のスレッドで動くので、Unity の値は先に読んでおく
        string uid = m.UID;
        string centerId = SpacePlanets.Current.Id;
        var weekday = StoreCalendar.TodayBusiness.DayOfWeek;
        var season = StoreCalendar.CurrentSeason;
        string cardMonth = CardDeck.MonthKey;
        string today = StoreCalendar.TodayKey; // トランプはショップで1日1回まで
        var rng = new System.Random(unchecked(Environment.TickCount * 7 + uid.GetHashCode()));
        var db = FirebaseFirestore.DefaultInstance;
        var userRef = db.Collection("users").Document(uid);

        Purchase res;
        try
        {
            res = await db.RunTransactionAsync(tx => tx.GetSnapshotAsync(userRef).ContinueWith(t =>
                Apply(tx, userRef, t.Result, item, count, rng, centerId, weekday, season, cardMonth, today))).AsUniTask();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[SpaceShop] 買えませんでした ({item.Id} x{count}): {ex.Message}");
            fail.Error = T_ERR_NETWORK;
            return fail;
        }
        if (res == null || !res.Ok)
        {
            // 所持金が足りなかった（ほかの端末で使っていた）ときは、手元の所持金をサーバーの値にそろえる
            if (res != null && res.Error == T_SHORT && Data != null) Data.Ig = res.Balance;
            // トランプをほかの端末で今日もう引いていたときは、手元の手札と山札もサーバーの値にそろえる（「今日はもう引きました」と出る）
            if (res != null && res.Error == CardDeck.T_SHOP_DONE && res.CardsMap != null && Data != null)
                Data.CardsMap = res.CardsMap;
            return res ?? fail;
        }
        ApplyLocal(res);
        return res;
    }

    /// <summary>トランザクションの中身（何度呼ばれてもよいように、外の状態は変えない）</summary>
    private static Purchase Apply(Transaction tx, DocumentReference userRef, DocumentSnapshot snap, SpaceShopItem item,
        int count, System.Random rng, string centerId, DayOfWeek weekday, StoreCalendar.Season season, string cardMonth,
        string today)
    {
        var r = new Purchase { Item = item, Count = count };
        var u = snap != null && snap.Exists ? snap.ToDictionary() : null;
        if (u == null)
        {
            r.Error = T_ERR_USER;
            return r;
        }
        long balance = Math.Max(0, Long(u, IgCurrency.FIELD));
        long cost = (long)item.Price * count;
        if (balance < cost)
        {
            r.Error = T_SHORT;
            r.Balance = (int)balance;
            return r;
        }

        var updates = new Dictionary<string, object> { { IgCurrency.FIELD, balance - cost } };
        if (item.Card)
        {
            // トランプ: 来店のときと同じ山札から count 枚引く。手札が8枚（捨てる前）なら引かない。
            // 手札は8枚までしか増やさない（あふれた1枚は、このあと捨てるカードを選ぶ）。
            // ショップで引けるのは1日1回・1枚まで（2026-11-12 ユーザー指定。引いた営業日を cards.shop_day に書く）
            var s = CardState.From(u.TryGetValue(CardDeck.FIELD, out var cardsRaw) ? cardsRaw : null);
            CardRules.Normalize(s, cardMonth);
            if (s.ShopDay == today)
            {
                r.Error = CardDeck.T_SHOP_DONE;
                r.Balance = (int)balance;
                r.CardsMap = s.ToMap();
                return r;
            }
            if (count != 1)
            {
                r.Error = CardDeck.T_ERR_CHANGED;
                r.Balance = (int)balance;
                return r;
            }
            s.ShopDay = today;
            if (CardRules.Overflow(s) || s.Hand.Count + count > CardRules.HAND_MAX + 1)
            {
                r.Error = CardRules.Overflow(s) ? CardDeck.T_ERR_DISCARD_FIRST : CardDeck.T_ERR_CHANGED;
                r.Balance = (int)balance;
                return r;
            }
            for (int i = 0; i < count; i++)
            {
                int c = CardRules.Draw(s, rng);
                if (c >= 0) r.Cards.Add(c);
            }
            r.CardOverflow = CardRules.Overflow(s);
            r.CardsMap = s.ToMap();
            updates[CardDeck.FIELD] = r.CardsMap;
            tx.Update(userRef, updates);
            r.Cost = (int)cost;
            r.Balance = (int)(balance - cost);
            r.OwnedAfter = s.Hand.Count;
            r.Ok = true;
            return r;
        }
        long after = Math.Max(0, Long(u, item.Field)) + count;
        long gold = 0; // 流れ星で当たった G（所持金は数そのものを書くので、そこに足す）
        if (item.StarFragment)
        {
            // 5個ごとに流れ星を1回（入店のときと同じ当たりやすさ＝公転の数）。クーポン・持ち物は数を増やし、ノワールは惑星に足す
            r.Stars = (int)(after / SpaceWorld.STAR_FRAGMENTS_PER_STAR);
            after %= SpaceWorld.STAR_FRAGMENTS_PER_STAR;
            if (r.Stars > 0)
            {
                var j = JourneyData.From(u.TryGetValue("journey", out var raw) ? raw : null);
                var owned = JourneyService.OwnedPlanetsOf(u);
                for (int i = 0; i < r.Stars; i++)
                    JourneyRules.AddPrize(JourneyRules.Roll(rng, j?.Streak ?? 0, centerId, weekday, season), owned, r.Star);
                var rewards = JourneyService.StarRewardsOf(r.Star.Prizes, u);
                r.Star.Rewards = rewards;
                if (rewards.Inc.TryGetValue(IgCurrency.FIELD, out gold)) rewards.Inc.Remove(IgCurrency.FIELD);
                JourneyService.WriteStarRewards(updates, rewards);
                if (gold != 0) rewards.Inc[IgCurrency.FIELD] = gold; // 手元の数・履歴のために戻す（書き込みは下の所持金で）
                if (r.Star.Unlocked.Count > 0)
                    updates["planets"] = FieldValue.ArrayUnion(r.Star.Unlocked.Cast<object>().ToArray());
            }
        }
        updates[item.Field] = after;
        updates[IgCurrency.FIELD] = balance - cost + gold;
        tx.Update(userRef, updates);
        r.Cost = (int)cost;
        r.Balance = (int)(balance - cost + gold);
        r.OwnedAfter = (int)after;
        r.Ok = true;
        return r;
    }

    /// <summary>書き込んだ内容を手元の UserData・履歴にも反映する</summary>
    private static void ApplyLocal(Purchase r)
    {
        var d = Data;
        if (d != null)
        {
            JourneyService.ApplyStarRewardsLocal(d, r.Star.Rewards); // 流れ星の当たり（クーポン・持ち物）
            d.Ig = r.Balance; // 流れ星の G も入った、トランザクションで書いた所持金（上で足した分はここで正しい値になる）
            if (r.Item.Card) d.CardsMap = r.CardsMap;
            else r.Item.Add(d, r.OwnedAfter - r.Item.Owned(d));
            foreach (var id in r.Star.Unlocked)
                if (!d.Planets.Contains(id)) d.Planets.Add(id);
            if (r.Star.Unlocked.Count > 0) SpacePlanets.MarkNews();
        }

        if (r.Item.Card)
        {
            LocalHistoryLog.Add(IgCurrency.HISTORY,
                T_CARD_HISTORY_A + r.Cards.Count + T_CARD_HISTORY_B + IgCurrency.Format(r.Cost) + "）");
            foreach (var c in r.Cards) LocalHistoryLog.Add(CardDeck.HISTORY, $"ショップで トランプの {CardRules.Name(c)} を引いた");
        }
        else
        {
            LocalHistoryLog.Add(IgCurrency.HISTORY,
                T_HISTORY_A + r.Item.Name + T_BOUGHT_A + r.Count + T_HISTORY_B + IgCurrency.Format(r.Cost) + "）");
        }
        if (r.Stars > 0)
        {
            LocalHistoryLog.Add("fragment",
                $"{SpaceWorld.STAR_FRAGMENT_NAME}が{SpaceWorld.STAR_FRAGMENTS_PER_STAR}{T_HISTORY_STAR}"
                + (r.Stars > 1 ? $"（{r.Stars}回）" : ""));
            JourneyService.LogStarPrizes(r.Star.Prizes, r.Star.Rewards);
            foreach (var id in r.Star.Unlocked)
            {
                var def = SpacePlanets.Find(id);
                if (def != null) LocalHistoryLog.Add("planet", $"惑星「{def.Name}」を解放");
            }
        }
        JourneyService.NotifyChanged(); // ホームの表示（所持品の数など）を更新
    }

    private static long Long(Dictionary<string, object> m, string k)
    {
        if (m == null || !m.TryGetValue(k, out var v) || v == null) return 0;
        try { return Convert.ToInt64(v); }
        catch { return 0; }
    }

    // ================================================================
    // 画面
    // ================================================================
    // 配置（セーフエリアの中。上からの距離）
    private const float SIDE = 40f;
    private const float MAX_W = 1000f;
    private const float WALLET_TOP = 196f, WALLET_H = 150f;
    private const float LIST_TOP = 420f, FOOTER_H = 110f;
    // 品物のカードは3列（2026-11-12 ユーザー指定で2列から変更）。幅は (1000 - 20×2) / 3 = 320
    private const int COLS = 3;
    private const float CARD_H = 440f, CARD_GAP = 20f;

    /// <summary>開いている画面（なければ null）</summary>
    public static SpaceShop Instance { get; private set; }

    private class Card
    {
        public SpaceShopItem Item;
        public RectTransform Rt;
        public CanvasGroup Group;
        public TextMeshProUGUI Owned, Price;
    }

    private Canvas _canvas;
    private CanvasGroup _group;
    private RectTransform _safe;
    private float _cw;
    private bool _closing, _busy;
    private int _shownHand = -1; // 表示している手札の枚数（トランプ）
    private TextMeshProUGUI _walletValue;
    private readonly List<Card> _cards = new List<Card>();
    private int _shownBalance = -1;
    private float _nextCheck;

    // 個数を選んで買う画面
    private RectTransform _sheet;
    private SpaceShopItem _sheetItem;
    private int _sheetCount;
    private TextMeshProUGUI _sheetCountText, _sheetTotal, _sheetAfter, _sheetOwned, _buyLabel;
    private CanvasGroup _minusGroup, _plusGroup, _buyGroup;

    /// <summary>ショップを開く。金貨座が完成していなければ開かない（管理者は初めから開ける）</summary>
    public static SpaceShop Open(Canvas canvas = null)
    {
        if (Instance != null) return Instance;
        if (!IgCurrency.Unlocked) return null;
        if (canvas == null) canvas = SpaceTheme.MainCanvas();
        if (canvas == null) return null;
        var root = SpaceTheme.Rect("__SpaceShop", canvas.transform);
        SpaceTheme.Stretch(root);
        root.SetAsLastSibling();
        SpaceTheme.SubCanvas(root.gameObject, true); // 星のまたたきをホームの再描画と分ける
        var page = root.gameObject.AddComponent<SpaceShop>();
        Instance = page;
        page.Build(canvas);
        return page;
    }

    /// <summary>戻る操作（Android の戻るボタン）。買う画面 → ショップの順に閉じる</summary>
    public void HandleBack()
    {
        if (_busy) return;
        if (_sheet != null) CloseSheet();
        else Close();
    }

    public void Close()
    {
        if (_closing || _busy) return;
        _closing = true;
        StartCoroutine(CloseRoutine());
    }

    private IEnumerator CloseRoutine()
    {
        float from = _group.alpha;
        yield return SpaceTheme.Tween(0.2f, 0f, k => _group.alpha = from * (1f - k));
        Destroy(gameObject);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        if (_closing || Time.unscaledTime < _nextCheck) return;
        _nextCheck = Time.unscaledTime + 0.5f;
        // 所持金や手札の枚数が変わったら（入店・カードを捨てた・ほかの画面での変化など）表示を合わせる
        if (IgCurrency.Balance != _shownBalance || CardDeck.Current.Hand.Count != _shownHand)
        {
            Refresh();
            if (_sheet != null) UpdateSheet();
        }
    }

    private void Build(Canvas canvas)
    {
        _canvas = canvas;
        var rt = (RectTransform)transform;
        _group = gameObject.AddComponent<CanvasGroup>();
        _group.alpha = 0f;

        var size = SpaceTheme.CanvasSize(canvas);
        _cw = Mathf.Min(size.x - SIDE * 2f, MAX_W);
        float side = (size.x - _cw) * 0.5f;

        // 背景（下のホームを押させない）: 深い緑に、上から差す緑の光・下の淡い光・星
        var dim = SpaceTheme.Rect("Dim", rt);
        SpaceTheme.Stretch(dim);
        SpaceTheme.Img(dim, null, BG, true);
        var glow = SpaceTheme.Rect("Glow", rt, new Vector2(0.5f, 1f), new Vector2(0f, -240f), new Vector2(1700f, 1300f));
        SpaceTheme.Img(glow, SpaceSprites.Glow, SpaceTheme.A(GREEN, 0.14f));
        var low = SpaceTheme.Rect("GlowLow", rt, new Vector2(0.5f, 0f), Vector2.zero, new Vector2(1700f, 900f));
        SpaceTheme.Img(low, SpaceSprites.Glow, SpaceTheme.A(GREEN_DEEP, 0.12f));
        Sky(rt);

        _safe = SpaceTheme.Rect("Safe", rt);
        SpaceTheme.Stretch(_safe);
        _safe.gameObject.AddComponent<SpaceSafeArea>();

        BuildHeader();
        BuildWallet(side);
        SpaceTheme.Text(_safe, "Section", T_SECTION, 28, SpaceTheme.A(GREEN_LIGHT, 0.8f), new Vector2(0f, 1f),
            new Vector2(side + 16f, -(LIST_TOP - 30f)), new Vector2(400f, 40f), FontStyles.Bold,
            TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
        BuildList(side);
        SpaceTheme.Text(_safe, "Footer", T_FOOTER, 26, SpaceTheme.A(GREEN_LIGHT, 0.6f), new Vector2(0.5f, 0f),
            new Vector2(0f, 52f), new Vector2(900f, 40f), FontStyles.Bold);

        Refresh();
        StartCoroutine(SpaceTheme.Tween(0.25f, 0f, k => _group.alpha = k));
    }

    private void BuildHeader()
    {
        var caption = SpaceTheme.Text(_safe, "Caption", T_CAPTION, 24, SpaceTheme.A(GREEN, 0.95f),
            new Vector2(0f, 1f), new Vector2(SIDE + 16f, -70f), new Vector2(400f, 36f), FontStyles.Bold,
            TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
        SpaceTheme.Tracking(caption, 18f);
        SpaceTheme.Text(_safe, "Title", T_TITLE, 52, SpaceTheme.TEXT, new Vector2(0f, 1f), new Vector2(SIDE + 16f, -126f),
            new Vector2(400f, 66f), FontStyles.Bold, TextAlignmentOptions.Left, new Vector2(0f, 0.5f));

        // 題の右に、この画面をくれた金貨座（いまのレベルの姿。管理者で未完成なら完成した姿）を小さく
        var p = Constellations.Progress().Find(x => x.Def.Id == IgCurrency.CONSTELLATION);
        if (p != null)
        {
            var fig = SpaceTheme.Rect("Constellation", _safe, new Vector2(0f, 1f), new Vector2(SIDE + 16f + 290f, -104f),
                new Vector2(120f, 120f));
            SpaceConstellationPage.Figure(fig, p.Def, Mathf.Max(p.Def.Size, p.Count - p.Partial), 120f);
        }

        // 右上の閉じる（緑の丸）
        var close = SpaceTheme.Rect("Close", _safe, new Vector2(1f, 1f), new Vector2(-(SIDE + 52f), -84f),
            new Vector2(104f, 104f));
        var face = SpaceTheme.Img(close, SpaceSprites.Circle, BUTTON_DARK, true);
        var rim = SpaceTheme.Rect("Rim", close);
        SpaceTheme.Stretch(rim);
        SpaceTheme.Img(rim, SpaceSprites.RingSmall, SpaceTheme.A(GREEN, 0.5f));
        SpaceTheme.Bar(close, Vector2.zero, 44f, 6f, 45f, SpaceTheme.TEXT);
        SpaceTheme.Bar(close, Vector2.zero, 44f, 6f, -45f, SpaceTheme.TEXT);
        SpaceTheme.Clickable(close.gameObject, face, Close);
    }

    /// <summary>所持金の帯: 金貨の絵＋「所持金」＋もらい方、右に「12G」</summary>
    private void BuildWallet(float side)
    {
        var wallet = SpaceTheme.Rect("Wallet", _safe);
        SpaceTheme.TopBand(wallet, WALLET_TOP, WALLET_H, side, side);
        SpaceTheme.Glass(wallet, 44f, CARD_DEEP, SpaceTheme.A(SpaceTheme.GOLD, 0.5f));
        var icon = SpaceTheme.Rect("Coin", wallet, new Vector2(0f, 0.5f), new Vector2(24f + 54f, 0f), new Vector2(108f, 108f));
        var sprite = IgCurrency.Icon;
        var img = SpaceTheme.Img(icon, sprite, Color.white);
        img.preserveAspect = true;
        img.enabled = sprite != null;
        SpaceTheme.Text(wallet, "Label", T_WALLET, 34, SpaceTheme.TEXT, new Vector2(0f, 0.5f), new Vector2(152f, 22f),
            new Vector2(380f, 48f), FontStyles.Bold, TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
        var sub = SpaceTheme.Text(wallet, "Sub", T_WALLET_SUB_A + IgCurrency.Format(IgCurrency.PerVisit) + T_WALLET_SUB_B, 22,
            SpaceTheme.A(GREEN_LIGHT, 0.75f), new Vector2(0f, 0.5f), new Vector2(152f, -24f), new Vector2(400f, 34f),
            FontStyles.Bold, TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
        sub.enableAutoSizing = true;
        sub.fontSizeMin = 16f;
        sub.fontSizeMax = 22f;
        _walletValue = SpaceTheme.Text(wallet, "Value", "", 72, SpaceTheme.GOLD, new Vector2(1f, 0.5f),
            new Vector2(-36f, 0f), new Vector2(380f, 96f), FontStyles.Bold, TextAlignmentOptions.Right, new Vector2(1f, 0.5f));
        _walletValue.enableAutoSizing = true;
        _walletValue.fontSizeMin = 40f;
        _walletValue.fontSizeMax = 72f;
    }

    /// <summary>品物のカードを3列で並べる（左から順に詰める。入りきらなければスクロール）</summary>
    private void BuildList(float side)
    {
        var list = SpaceTheme.Rect("List", _safe);
        SpaceTheme.Stretch(list, side, FOOTER_H, side, LIST_TOP);
        var viewport = SpaceTheme.Rect("Viewport", list);
        SpaceTheme.Stretch(viewport);
        SpaceTheme.HitArea(viewport); // カードの間の余白でもドラッグできるように
        viewport.gameObject.AddComponent<RectMask2D>();
        var content = SpaceTheme.Rect("Content", viewport);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.anchoredPosition = Vector2.zero;
        var scroll = list.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Elastic;
        scroll.scrollSensitivity = 30f;

        const float PAD = 8f;
        float w = (_cw - CARD_GAP * (COLS - 1)) / COLS;
        var items = VisibleItems();
        for (int i = 0; i < items.Count; i++)
        {
            int col = i % COLS, row = i / COLS;
            float x = -_cw * 0.5f + w * 0.5f + col * (w + CARD_GAP);
            float y = -(PAD + row * (CARD_H + CARD_GAP) + CARD_H * 0.5f);
            BuildCard(content, items[i], new Vector2(x, y), w);
        }
        int rows = (items.Count + COLS - 1) / COLS;
        float h = PAD * 2f + rows * CARD_H + Mathf.Max(0, rows - 1) * CARD_GAP;
        content.sizeDelta = new Vector2(0f, h);
        var size = SpaceTheme.CanvasSize(_canvas);
        var insets = SpaceTheme.SafeInsets(size);
        float viewH = size.y - insets.top - insets.bottom - LIST_TOP - FOOTER_H;
        scroll.vertical = h > viewH + 1f; // 収まるときはスクロールさせない
    }

    private void BuildCard(RectTransform content, SpaceShopItem item, Vector2 pos, float w)
    {
        var card = SpaceTheme.Rect("Item_" + item.Id, content, new Vector2(0.5f, 1f), pos, new Vector2(w, CARD_H));
        var face = SpaceTheme.Glass(card, 40f, CARD, SpaceTheme.A(item.Tint, 0.45f), true);
        var group = card.gameObject.AddComponent<CanvasGroup>();
        SpaceTheme.Clickable(card.gameObject, face, () => OnItemTapped(item));

        var top = new Vector2(0.5f, 1f);
        // 3列で幅が 320 なので、絵・文字・値段の札は2列のときより小さめ。説明は2行まで折り返す
        var glow = SpaceTheme.Rect("Glow", card, top, new Vector2(0f, -112f), new Vector2(230f, 230f));
        SpaceTheme.Img(glow, SpaceSprites.Glow, SpaceTheme.A(item.Tint, 0.26f));
        var iconRt = SpaceTheme.Rect("Icon", card, top, new Vector2(0f, -112f), new Vector2(148f, 148f));
        var sprite = item.Icon?.Invoke();
        var img = SpaceTheme.Img(iconRt, sprite, Color.white);
        img.preserveAspect = true;
        img.enabled = sprite != null;

        var name = SpaceTheme.Text(card, "Name", item.Name, 34, SpaceTheme.TEXT, top, new Vector2(0f, -216f),
            new Vector2(w - 28f, 44f), FontStyles.Bold);
        name.enableAutoSizing = true;
        name.fontSizeMin = 22f;
        name.fontSizeMax = 34f;
        var desc = SpaceTheme.Text(card, "Desc", item.Desc, 19, SpaceTheme.TEXT_SUB, top, new Vector2(0f, -266f),
            new Vector2(w - 28f, 52f), FontStyles.Bold);
        desc.textWrappingMode = TextWrappingModes.Normal;
        desc.enableAutoSizing = true;
        desc.fontSizeMin = 14f;
        desc.fontSizeMax = 19f;
        var owned = SpaceTheme.Text(card, "Owned", "", 20, SpaceTheme.A(GREEN_LIGHT, 0.7f), top, new Vector2(0f, -312f),
            new Vector2(w - 28f, 30f), FontStyles.Bold);
        owned.enableAutoSizing = true;
        owned.fontSizeMin = 15f;
        owned.fontSizeMax = 20f;

        // 値段の札（金貨＋「5G」）
        var tag = SpaceTheme.Rect("Price", card, new Vector2(0.5f, 0f), new Vector2(0f, 22f), new Vector2(w - 32f, 74f),
            new Vector2(0.5f, 0f));
        SpaceTheme.Rounded(tag, SpaceTheme.A(GREEN, 0.13f), 36f);
        var tagEdge = SpaceTheme.Rect("Edge", tag);
        SpaceTheme.Stretch(tagEdge);
        SpaceTheme.RoundedStroke(tagEdge, SpaceTheme.A(GREEN, 0.55f), 36f);
        string priceText = PriceRich(item.Price);
        var price = SpaceTheme.Text(tag, "Value", priceText, 40, SpaceTheme.GOLD, FontStyles.Bold);
        float tw = price.GetPreferredValues(priceText).x * Mathf.Max(1f, SpaceTheme.TextScale) + 6f;
        const float COIN = 48f, GAP = 8f;
        float left = -(COIN + GAP + tw) * 0.5f;
        var coin = SpaceTheme.Rect("Coin", tag, SpaceTheme.CENTER, new Vector2(left + COIN * 0.5f, 0f), new Vector2(COIN, COIN));
        var coinSprite = IgCurrency.Icon;
        var coinImg = SpaceTheme.Img(coin, coinSprite, Color.white);
        coinImg.preserveAspect = true;
        coinImg.enabled = coinSprite != null;
        var prt = price.rectTransform;
        prt.anchorMin = prt.anchorMax = SpaceTheme.CENTER;
        prt.pivot = new Vector2(0f, 0.5f);
        prt.anchoredPosition = new Vector2(left + COIN + GAP, 0f);
        prt.sizeDelta = new Vector2(tw, 56f);
        price.alignment = TextAlignmentOptions.Left;

        _cards.Add(new Card { Item = item, Rt = card, Group = group, Owned = owned, Price = price });
    }

    /// <summary>「5G」（数字のすぐあとに少し小さい G）</summary>
    private static string PriceRich(int n) =>
        n.ToString("N0", CultureInfo.InvariantCulture) + "<size=60%>" + IgCurrency.UNIT + "</size>";

    /// <summary>所持金・持っている数・買えるかどうかを今の値に合わせる</summary>
    private void Refresh()
    {
        int balance = IgCurrency.Balance;
        _shownBalance = balance;
        if (_walletValue != null) _walletValue.text = PriceRich(balance);
        var d = Data;
        _shownHand = CardDeck.Current.Hand.Count;
        foreach (var c in _cards)
        {
            c.Owned.text = OwnedLine(c.Item, d);
            bool rich = balance >= c.Item.Price;
            bool can = rich && c.Item.Blocked?.Invoke() == null && (c.Item.Limit == null || c.Item.Limit() >= 1);
            c.Price.color = rich ? SpaceTheme.GOLD : SpaceTheme.TEXT_MUTED;
            c.Group.alpha = can ? 1f : 0.7f;
        }
    }

    /// <summary>「持っている数 3個」（トランプは「手札 3 / 7」）</summary>
    private static string OwnedLine(SpaceShopItem item, UserData d)
    {
        if (item.OwnedText != null) return d != null ? item.OwnedText(d) : "";
        int owned = d != null ? Mathf.Max(0, item.Owned(d)) : 0;
        return T_OWNED + owned + (item.Unit ?? T_UNIT);
    }

    private void OnItemTapped(SpaceShopItem item)
    {
        if (_busy || _closing || _sheet != null) return;
        string blocked = item.Blocked?.Invoke();
        if (blocked != null)
        {
            // 手札が8枚: 先に捨てるカードを選ぶ画面を出す（今日もう引いたときは知らせるだけ）
            SpaceTheme.Toast(blocked, _canvas);
            if (item.Card && blocked == CardDeck.T_ERR_DISCARD_FIRST) SpaceNotice.CardDiscard();
            return;
        }
        OpenSheet(item);
    }

    // ================================================================
    // 個数を選んで買う画面
    // ================================================================
    private void OpenSheet(SpaceShopItem item)
    {
        _sheetItem = item;
        _sheetCount = 1;
        _sheet = SpaceTheme.Rect("Sheet", transform);
        SpaceTheme.Stretch(_sheet);
        var group = _sheet.gameObject.AddComponent<CanvasGroup>();

        var dim = SpaceTheme.Rect("Dim", _sheet);
        SpaceTheme.Stretch(dim);
        var dimImg = SpaceTheme.Img(dim, null, SHEET_DIM, true);
        SpaceTheme.Clickable(dim.gameObject, dimImg, CloseSheet, false);

        var card = SpaceTheme.Rect("Card", _sheet, SpaceTheme.CENTER, Vector2.zero, new Vector2(920f, 1100f));
        SpaceTheme.Glass(card, 56f, CARD_DEEP, SpaceTheme.A(item.Tint, 0.55f), true);

        var glow = SpaceTheme.Rect("Glow", card, SpaceTheme.CENTER, new Vector2(0f, 360f), new Vector2(440f, 440f));
        SpaceTheme.Img(glow, SpaceSprites.Glow, SpaceTheme.A(item.Tint, 0.3f));
        var iconRt = SpaceTheme.Rect("Icon", card, SpaceTheme.CENTER, new Vector2(0f, 360f), new Vector2(220f, 220f));
        var sprite = item.Icon?.Invoke();
        var img = SpaceTheme.Img(iconRt, sprite, Color.white);
        img.preserveAspect = true;
        img.enabled = sprite != null;

        SpaceTheme.Text(card, "Name", item.Name, 50, SpaceTheme.TEXT, SpaceTheme.CENTER, new Vector2(0f, 200f),
            new Vector2(820f, 64f), FontStyles.Bold);
        var desc = SpaceTheme.Text(card, "Desc", item.Desc, 28, SpaceTheme.TEXT_SUB, SpaceTheme.CENTER,
            new Vector2(0f, 138f), new Vector2(820f, 44f), FontStyles.Bold);
        desc.enableAutoSizing = true;
        desc.fontSizeMin = 20f;
        desc.fontSizeMax = 28f;
        _sheetOwned = SpaceTheme.Text(card, "Owned", "", 26, SpaceTheme.A(GREEN_LIGHT, 0.75f), SpaceTheme.CENTER,
            new Vector2(0f, 82f), new Vector2(820f, 40f), FontStyles.Bold);

        // 個数（−  3個  ＋）
        _minusGroup = StepperButton(card, new Vector2(-220f, -36f), false, () => { _sheetCount--; UpdateSheet(); });
        _plusGroup = StepperButton(card, new Vector2(220f, -36f), true, () => { _sheetCount++; UpdateSheet(); });
        _sheetCountText = SpaceTheme.Text(card, "Count", "", 100, SpaceTheme.TEXT, SpaceTheme.CENTER, new Vector2(0f, -36f),
            new Vector2(300f, 130f), FontStyles.Bold);
        _sheetTotal = SpaceTheme.Text(card, "Total", "", 46, SpaceTheme.GOLD, SpaceTheme.CENTER, new Vector2(0f, -170f),
            new Vector2(820f, 62f), FontStyles.Bold);
        _sheetAfter = SpaceTheme.Text(card, "After", "", 28, SpaceTheme.TEXT_SUB, SpaceTheme.CENTER, new Vector2(0f, -236f),
            new Vector2(820f, 42f), FontStyles.Bold);

        // キャンセル / 購入する
        var cancel = SpaceTheme.Rect("Cancel", card, SpaceTheme.CENTER, new Vector2(-215f, -400f), new Vector2(390f, 132f));
        var cancelFace = SpaceTheme.Glass(cancel, 46f, BUTTON_DARK, SpaceTheme.A(GREEN, 0.3f), true);
        SpaceTheme.Clickable(cancel.gameObject, cancelFace, CloseSheet);
        var cancelLabel = SpaceTheme.Text(cancel, "Label", T_CANCEL, 38, SpaceTheme.TEXT_SUB, FontStyles.Bold);
        SpaceTheme.Stretch(cancelLabel.rectTransform);

        var buy = SpaceTheme.Rect("Buy", card, SpaceTheme.CENTER, new Vector2(215f, -400f), new Vector2(390f, 132f));
        var buyFace = SpaceTheme.GradientFace(buy, GREEN_LIGHT, GREEN_DEEP, GREEN);
        SpaceTheme.Clickable(buy.gameObject, buyFace, OnBuyPressed);
        _buyGroup = buy.gameObject.AddComponent<CanvasGroup>();
        _buyLabel = SpaceTheme.Text(buy, "Label", T_BUY, 40, SpaceTheme.TEXT_DARK, FontStyles.Bold);
        SpaceTheme.Stretch(_buyLabel.rectTransform, 12f, 0f, 12f, 0f);
        _buyLabel.enableAutoSizing = true;
        _buyLabel.fontSizeMin = 26f;
        _buyLabel.fontSizeMax = 40f;

        UpdateSheet();
        group.alpha = 0f;
        StartCoroutine(SpaceTheme.Tween(0.22f, 0f, k =>
        {
            group.alpha = k;
            card.localScale = Vector3.one * Mathf.Lerp(0.94f, 1f, k);
        }));
    }

    private static CanvasGroup StepperButton(RectTransform parent, Vector2 pos, bool plus, UnityEngine.Events.UnityAction onClick)
    {
        var rt = SpaceTheme.Rect(plus ? "Plus" : "Minus", parent, SpaceTheme.CENTER, pos, new Vector2(120f, 120f));
        var face = SpaceTheme.Img(rt, SpaceSprites.Circle, BUTTON_DARK, true);
        var rim = SpaceTheme.Rect("Rim", rt);
        SpaceTheme.Stretch(rim);
        SpaceTheme.Img(rim, SpaceSprites.RingSmall, SpaceTheme.A(GREEN, 0.45f));
        SpaceTheme.Bar(rt, Vector2.zero, 40f, 7f, 0f, SpaceTheme.TEXT);
        if (plus) SpaceTheme.Bar(rt, Vector2.zero, 40f, 7f, 90f, SpaceTheme.TEXT);
        SpaceTheme.Clickable(rt.gameObject, face, onClick);
        return rt.gameObject.AddComponent<CanvasGroup>();
    }

    /// <summary>買える数（所持金で買える数。最大 MAX_COUNT。トランプは手札が8枚になるまで）</summary>
    private int MaxCount
    {
        get
        {
            if (_sheetItem == null || _sheetItem.Price <= 0) return 0;
            int n = Mathf.Min(MAX_COUNT, IgCurrency.Balance / _sheetItem.Price);
            if (_sheetItem.Limit != null) n = Mathf.Min(n, _sheetItem.Limit());
            if (_sheetItem.Blocked?.Invoke() != null) n = 0;
            return n;
        }
    }

    private void UpdateSheet()
    {
        if (_sheet == null || _sheetItem == null) return;
        int balance = IgCurrency.Balance;
        int max = MaxCount;
        _sheetCount = Mathf.Clamp(_sheetCount, 1, Mathf.Max(1, max));
        int cost = _sheetItem.Price * _sheetCount;
        var d = Data;
        _sheetOwned.text = OwnedLine(_sheetItem, d);
        _sheetCountText.text = $"{_sheetCount}<size=40%><color={SpaceTheme.SUB_HEX}> {_sheetItem.Unit ?? T_UNIT}</color></size>";
        _sheetTotal.text = T_TOTAL + PriceRich(cost);
        _sheetAfter.text = max >= 1
            ? T_AFTER + IgCurrency.Format(balance) + T_ARROW + IgCurrency.Format(balance - cost)
            : T_AFTER + IgCurrency.Format(balance);
        _sheetTotal.color = max >= 1 ? SpaceTheme.GOLD : SpaceTheme.TEXT_MUTED;
        _minusGroup.alpha = _sheetCount > 1 ? 1f : 0.35f;
        _plusGroup.alpha = _sheetCount < max ? 1f : 0.35f;
        bool can = max >= 1;
        if (!_busy) _buyLabel.text = can || balance >= _sheetItem.Price ? T_BUY : T_SHORT;
        _buyGroup.alpha = can && !_busy ? 1f : 0.45f;
    }

    private void CloseSheet()
    {
        if (_sheet == null || _busy) return;
        Destroy(_sheet.gameObject);
        _sheet = null;
        _sheetItem = null;
    }

    private void OnBuyPressed()
    {
        if (_busy || _sheetItem == null) return;
        if (MaxCount < 1)
        {
            SpaceTheme.Toast(_sheetItem.Blocked?.Invoke() ?? T_SHORT, _canvas);
            return;
        }
        BuyRoutine(_sheetItem, _sheetCount).Forget();
    }

    private async UniTaskVoid BuyRoutine(SpaceShopItem item, int count)
    {
        _busy = true;
        _buyLabel.text = T_BUYING;
        _buyGroup.alpha = 0.6f;
        Purchase res;
        try
        {
            res = await BuyAsync(item, count);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[SpaceShop] 購入エラー ({item.Id}): {ex.Message}");
            res = new Purchase { Item = item, Count = count, Error = T_ERR_NETWORK };
        }
        if (this == null) return;
        _busy = false;
        if (!res.Ok)
        {
            SpaceTheme.Toast(res.Error ?? T_ERR_NETWORK, _canvas);
            Refresh();
            UpdateSheet();
            return;
        }

        CloseSheet();
        Refresh();
        AssetsDatabase.instance?.PlayLevelUpSE();
        if (item.StarFragment)
        {
            ShowStarResult(res);
        }
        else if (item.Card)
        {
            // 引いたカードをめくって見せる → 手札が8枚になったら、捨てるカードを選ぶ（選ぶまで閉じない）
            var steps = new List<Func<GameObject>>();
            var cards = new List<int>(res.Cards);
            bool over = res.CardOverflow;
            steps.Add(() => SpaceNotice.CardDrawn(cards, T_CAPTION, over, GREEN));
            if (over) steps.Add(() => CardDeck.MustDiscard ? SpaceNotice.CardDiscard() : null);
            SpaceNoticeQueue.Run(steps, () =>
            {
                if (this != null) Refresh();
            });
            var card = _cards.Find(c => c.Item == item);
            if (card != null) StartCoroutine(Pop(card.Rt));
        }
        else
        {
            SpaceTheme.Toast(item.Name + T_BOUGHT_A + count + T_BOUGHT_B, _canvas);
            var card = _cards.Find(c => c.Item == item);
            if (card != null) StartCoroutine(Pop(card.Rt));
        }
    }

    /// <summary>
    /// 星のカケラを買った: カケラの星（集まった数だけ金色）を出し、5個そろっていたら、とじたあと流れ星
    /// （入店のときと同じ演出 → 当たりのカード。ノワールなら新しい惑星も）
    /// </summary>
    private static void ShowStarResult(Purchase r)
    {
        var steps = new List<Func<GameObject>>();
        bool completed = r.Stars > 0;
        int have = r.OwnedAfter;
        string got = T_BOUGHT_A + r.Count + T_BOUGHT_B;
        steps.Add(() => SpaceNotice.StarFragmentGot(T_TITLE, have, completed, T_CAPTION, got, GREEN));
        foreach (var p in r.Star.Prizes)
        {
            var prize = p;
            steps.Add(() => SpaceGachaMeteor.Play(prize));
        }
        foreach (var id in r.Star.Unlocked)
        {
            var def = SpacePlanets.Find(id);
            if (def != null) steps.Add(() => SpaceNotice.PlanetUnlocked(def));
        }
        SpaceNoticeQueue.Run(steps, null);
    }

    /// <summary>買った品物のカードを軽く弾ませる</summary>
    private static IEnumerator Pop(RectTransform rt)
    {
        yield return SpaceTheme.Tween(0.45f, 0f, k =>
        {
            if (rt == null) return;
            float s = 1f + 0.06f * Mathf.Sin(k * Mathf.PI);
            rt.localScale = new Vector3(s, s, 1f);
        });
        if (rt != null) rt.localScale = Vector3.one;
    }

    /// <summary>背景の星（ところどころまたたく。少し緑がかった白）</summary>
    private static void Sky(RectTransform rt)
    {
        var rnd = new System.Random(20261112);
        var layer = SpaceTheme.Rect("Sky", rt);
        SpaceTheme.Stretch(layer);
        var tint = Color.Lerp(Color.white, GREEN_LIGHT, 0.35f);
        for (int i = 0; i < 60; i++)
        {
            var pos = new Vector2((float)rnd.NextDouble(), (float)rnd.NextDouble());
            float s = 3f + (float)rnd.NextDouble() * 5f;
            var star = SpaceTheme.Rect("S", layer, pos, Vector2.zero, new Vector2(s, s));
            var img = SpaceTheme.Img(star, SpaceSprites.Circle, SpaceTheme.A(tint, 0.12f + (float)rnd.NextDouble() * 0.3f));
            if (i % 3 == 0) star.gameObject.AddComponent<SpaceTwinkle>().Init(img, img.color.a);
        }
    }
}
