using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Cysharp.Threading.Tasks;
using Firebase.Firestore;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// トランプ（2026-11-12 ユーザー指定）。宝石座か切り札座を完成させると（管理者は初めから）、トランプのようなカードを持てる。
///  ・カードは 赤・青・黄・黒 × 1〜13 の52枚（決まりは Commons/PlayingCards.cs の CardRules）
///  ・来店した日（その営業日の最初の入店）に山札から1枚引く（JourneyRules.DrawCardOnVisit。入店のお知らせのあとにめくって見せる）。
///    ショップでも 3G で1枚引ける（宝石座か切り札座が完成していたら並ぶ。同日ユーザー指定）。ショップで引けるのは
///    1日1回・1枚まで（同日ユーザー指定。cards.shop_day に引いた営業日を書く。来店の1枚とは別に数える）
///  ・手札は7枚まで。8枚目を引いたら、好きなカードを1枚捨てる（SpaceNotice.CardDiscard。選ぶまで閉じない）
///  ・山札は全部引き切るまで同じカードは出ない／同じ色と数字のカードは2枚持てない（引けるのが手札のカードだけなら山札をリセット）
///  ・月が変わると、手札も山札もリセット（表示も書き込みも、月が違えばリセットしたものとして扱う）
///  ・宝石座が完成していれば（管理者は初めから）、ホームの空の宝石座をタップしてポーカー（SpacePoker）。
///    手札から5枚選んで勝負し、役に応じて報酬（CardRules.Rewards）。使った5枚はなくなる
///  ・山札座も完成していれば（管理者は初めから）、1日1回「山札から1枚引いて、手札から1枚捨てる」（SpaceDeckSwap。2026-11-13）
/// 保存先は users/{uid}.cards（本人が書けるのでルールの変更はなし）。書き込みはどれも users を1回読むトランザクション。
/// </summary>
public static class CardDeck
{
    public const string FIELD = "cards";
    /// <summary>履歴の種類（SpaceHistoryPanel）</summary>
    public const string HISTORY = "card";
    /// <summary>カードを持てるようになる星座（宝石座＝セットコレクション・切り札座＝トリックテイキング）</summary>
    public const string GEM = "gem", TRUMP = "trump";
    /// <summary>1日1回「1枚引いて1枚捨てる」ができるようになる星座（山札座＝デッキ構築。2026-11-13）</summary>
    public const string DECK = "deck";
    /// <summary>ショップで1枚引く値段（G。2026-11-12 ユーザー指定）</summary>
    public const int SHOP_PRICE = 3;

    public const string T_TITLE = "トランプ";
    public const string T_HAND_A = "手札 ";
    public const string T_DECK_A = "山札 ";
    public const string T_DECK_B = "枚";
    public const string T_RESET = "毎月1日に手札と山札がリセットされます";
    public const string T_INFO =
        "来店するたびに山札から1枚引けます（ショップでも1日1回引けます）。手札は7枚まで。同じカードは2枚持てません。\n毎月1日に手札と山札がリセットされます";
    /// <summary>ショップで今日もう引いた（1日1回まで。2026-11-12 ユーザー指定）</summary>
    public const string T_SHOP_DONE = "ショップで引けるのは1日1回までです。また明日引けます";
    public const string T_SHOP_DONE_SHORT = "今日はもう引きました";
    public const string T_POKER_HINT = "ホームの空の宝石座をタップすると、ポーカーができます";
    public const string T_ERR_USER = "ユーザー情報を読み込めませんでした";
    public const string T_ERR_NETWORK = "通信に失敗しました。もう一度お試しください";
    public const string T_ERR_DISCARD_FIRST = "手札が8枚あります。先に捨てるカードを1枚選んでください";
    public const string T_ERR_CHANGED = "手札が変わりました。もう一度選んでください";
    /// <summary>山札座で引いて、まだ捨てていない（2026-11-13）</summary>
    public const string T_ERR_SWAP_FIRST = "山札座で引いたので、先に捨てるカードを1枚選んでください";
    /// <summary>山札座で今日もう引いた（1日1回まで。2026-11-13 ユーザー指定）</summary>
    public const string T_SWAP_DONE = "山札座で引けるのは1日1回までです。また明日使えます";

    /// <summary>この機能で表示しうる文字（NewHome の起動時にまとめてフォントへ登録する）</summary>
    public static string GlyphText
    {
        get
        {
            var sb = new StringBuilder()
                .Append(T_TITLE).Append(T_HAND_A).Append(T_DECK_A).Append(T_DECK_B).Append(T_RESET).Append(T_INFO)
                .Append(T_POKER_HINT).Append(T_ERR_USER).Append(T_ERR_NETWORK).Append(T_ERR_DISCARD_FIRST).Append(T_ERR_CHANGED)
                .Append(T_SHOP_DONE).Append(T_SHOP_DONE_SHORT).Append(T_ERR_SWAP_FIRST).Append(T_SWAP_DONE)
                .Append(SpaceDeckSwap.GlyphText)
                .Append("0123456789/の・＋+GOLD");
            foreach (var n in CardRules.COLOR_NAMES) sb.Append(n);
            foreach (var r in CardRules.Rewards)
            {
                sb.Append(r.Name);
                if (r.Coupon != null) sb.Append(SpaceCoupons.NameOf(r.Coupon));
            }
            return sb.ToString();
        }
    }

    private static UserData Data => UserDataManager.instance != null ? UserDataManager.instance.UserData : null;

    private static bool Complete(string id)
    {
        var p = Constellations.Progress().Find(x => x.Def.Id == id);
        return p != null && p.Complete;
    }

    /// <summary>カードを持てるか（宝石座か切り札座が完成。管理者は初めから）</summary>
    public static bool Unlocked => AdminClaim.IsAdmin || Complete(GEM) || Complete(TRUMP);

    /// <summary>ポーカーができるか（宝石座が完成。管理者は初めから）</summary>
    public static bool PokerUnlocked => AdminClaim.IsAdmin || Complete(GEM);

    /// <summary>
    /// 山札座の「1枚引いて1枚捨てる」が使えるか（山札座が完成していて、カードを持てる＝宝石座か切り札座も完成。管理者は初めから）
    /// </summary>
    public static bool SwapUnlocked => AdminClaim.IsAdmin || (Complete(DECK) && (Complete(GEM) || Complete(TRUMP)));

    /// <summary>今の営業日の月</summary>
    public static string MonthKey => CardRules.MonthKey(StoreCalendar.TodayBusiness);

    /// <summary>今の手札と山札（表示用の写し。月が変わっていればリセットした姿）</summary>
    public static CardState Current
    {
        get
        {
            var s = CardState.From(Data?.CardsMap);
            CardRules.Normalize(s, MonthKey);
            return s;
        }
    }

    /// <summary>
    /// 手札が8枚以上、または山札座で引いてまだ捨てていない（1枚捨てるまで、引くこともポーカーもできない）
    /// </summary>
    public static bool MustDiscard => CardRules.MustDiscard(Current);

    /// <summary>先に捨てるカードを選ぶよう促す一言（手札が8枚以上なら「手札が8枚あります…」、山札座で引いたあとなら山札座の一言）</summary>
    public static string DiscardFirstText(CardState s) => CardRules.Overflow(s) ? T_ERR_DISCARD_FIRST : T_ERR_SWAP_FIRST;

    /// <summary>今日（今の営業日）はもうショップで引いた（ショップで引けるのは1日1回まで。2026-11-12）</summary>
    public static bool ShopDrawnToday => Current.ShopDay == StoreCalendar.TodayKey;

    /// <summary>今日（今の営業日）はもう山札座で引いた（1日1回まで。2026-11-13）</summary>
    public static bool SwappedToday => Current.SwapDay == StoreCalendar.TodayKey;

    /// <summary>手札と山札の一言（「手札 5 / 7 ・ 山札 41枚」）</summary>
    public static string Summary(CardState s) =>
        T_HAND_A + s.Hand.Count + " / " + CardRules.HAND_MAX + " ・ " + T_DECK_A + s.Deck.Count + T_DECK_B;

    // ================================================================
    // 書き込み
    // ================================================================
    /// <summary>
    /// 手札から1枚捨てる（手札が8枚以上のときと、山札座で引いたあとだけ。山札には戻らない）。できたら true
    /// </summary>
    public static async UniTask<bool> DiscardAsync(int code)
    {
        var m = UserDataManager.instance;
        if (m == null || string.IsNullOrEmpty(m.UID) || m.UserData == null) return false;
        string month = MonthKey;
        var db = FirebaseFirestore.DefaultInstance;
        var doc = db.Collection("users").Document(m.UID);
        Dictionary<string, object> written = null;
        bool ok;
        try
        {
            ok = await db.RunTransactionAsync(tx => tx.GetSnapshotAsync(doc).ContinueWith(t =>
            {
                var u = t.Result.Exists ? t.Result.ToDictionary() : new Dictionary<string, object>();
                var s = CardState.From(u.TryGetValue(FIELD, out var raw) ? raw : null);
                bool reset = CardRules.Normalize(s, month);
                if (!CardRules.MustDiscard(s))
                {
                    // もう捨てなくてよい（月が変わった・ほかの端末で捨てた）: 捨てずに今の状態をそろえる
                    if (reset) tx.Update(doc, new Dictionary<string, object> { { FIELD, s.ToMap() } });
                    written = s.ToMap();
                    return true;
                }
                if (!CardRules.Discard(s, code)) return false;
                written = s.ToMap();
                tx.Update(doc, new Dictionary<string, object> { { FIELD, written } });
                return true;
            })).AsUniTask();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[CardDeck] 捨てられませんでした ({code}): {ex.Message}");
            return false;
        }
        if (written != null) m.UserData.CardsMap = written;
        if (ok) LocalHistoryLog.Add(HISTORY, $"トランプの {CardRules.Name(code)} を捨てた");
        return ok;
    }

    /// <summary>ポーカーの結果</summary>
    public sealed class PokerPlay
    {
        public bool Ok;
        public string Error;
        public List<int> Five;
        public PokerHand Hand;
        public PokerReward Reward;
        public int Balance; // 勝負したあとの所持金
    }

    /// <summary>
    /// 手札の5枚で勝負する。役に応じた報酬を足し、使った5枚を手札から除く（山札には戻らない）
    /// </summary>
    public static async UniTask<PokerPlay> PlayPokerAsync(List<int> five)
    {
        var fail = new PokerPlay { Five = five, Error = T_ERR_USER };
        var m = UserDataManager.instance;
        if (five == null || five.Count != CardRules.POKER_CARDS || five.Distinct().Count() != CardRules.POKER_CARDS
            || m == null || string.IsNullOrEmpty(m.UID) || m.UserData == null)
            return fail;
        string month = MonthKey;
        var db = FirebaseFirestore.DefaultInstance;
        var doc = db.Collection("users").Document(m.UID);
        Dictionary<string, object> written = null;
        PokerPlay res;
        try
        {
            res = await db.RunTransactionAsync(tx => tx.GetSnapshotAsync(doc).ContinueWith(t =>
            {
                var r = new PokerPlay { Five = new List<int>(five) };
                var u = t.Result.Exists ? t.Result.ToDictionary() : null;
                if (u == null)
                {
                    r.Error = T_ERR_USER;
                    return r;
                }
                var s = CardState.From(u.TryGetValue(FIELD, out var raw) ? raw : null);
                CardRules.Normalize(s, month);
                if (CardRules.MustDiscard(s))
                {
                    r.Error = DiscardFirstText(s);
                    return r;
                }
                if (!five.All(s.Hand.Contains))
                {
                    r.Error = T_ERR_CHANGED;
                    return r;
                }
                r.Hand = CardRules.Evaluate(five);
                r.Reward = CardRules.RewardOf(r.Hand);
                foreach (var c in five) s.Hand.Remove(c);
                written = s.ToMap();
                var updates = new Dictionary<string, object> { { FIELD, written } };
                long gold = Math.Max(0, Convert.ToInt64(u.TryGetValue(IgCurrency.FIELD, out var g) && g != null ? g : 0L));
                if (r.Reward.Gold > 0) updates[IgCurrency.FIELD] = gold + r.Reward.Gold;
                if (r.Reward.Coupon != null) updates[r.Reward.Coupon] = FieldValue.Increment(r.Reward.CouponCount);
                r.Balance = (int)(gold + r.Reward.Gold);
                tx.Update(doc, updates);
                r.Ok = true;
                return r;
            })).AsUniTask();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[CardDeck] ポーカーの記録に失敗: {ex.Message}");
            fail.Error = T_ERR_NETWORK;
            return fail;
        }
        if (res == null || !res.Ok) return res ?? fail;

        var d = m.UserData;
        d.CardsMap = written;
        if (res.Reward.Gold > 0) d.Ig = res.Balance;
        if (res.Reward.Coupon != null) SpaceCoupons.ByField(res.Reward.Coupon)?.Add(d, res.Reward.CouponCount);
        LocalHistoryLog.Add(HISTORY, $"ポーカーで {res.Reward.Name}（{RewardText(res.Reward)}）");
        JourneyService.NotifyChanged();
        return res;
    }

    /// <summary>山札座で引いた結果</summary>
    public sealed class SwapPlay
    {
        public bool Ok;
        public string Error;
        public int Card = -1;
    }

    /// <summary>
    /// 山札座の「1枚引いて1枚捨てる」の、引くほう（1営業日に1回。2026-11-13）。引いたカードは手札に入り、
    /// 1枚捨てるまで cards.swap_pending が立つ（捨てるのは SpaceNotice.CardDiscard → DiscardAsync）。
    /// 今日もう引いた・先に捨てるカードがある（ほかの端末で引いた など）ときは引かずに、手元の cards をそろえて Error を返す
    /// </summary>
    public static async UniTask<SwapPlay> SwapDrawAsync()
    {
        var m = UserDataManager.instance;
        if (m == null || string.IsNullOrEmpty(m.UID) || m.UserData == null) return new SwapPlay { Error = T_ERR_USER };
        string month = MonthKey, today = StoreCalendar.TodayKey;
        var rng = new System.Random();
        var db = FirebaseFirestore.DefaultInstance;
        var doc = db.Collection("users").Document(m.UID);
        Dictionary<string, object> written = null;
        SwapPlay res;
        try
        {
            res = await db.RunTransactionAsync(tx => tx.GetSnapshotAsync(doc).ContinueWith(t =>
            {
                var r = new SwapPlay();
                written = null;
                if (!t.Result.Exists)
                {
                    r.Error = T_ERR_USER;
                    return r;
                }
                var u = t.Result.ToDictionary();
                var s = CardState.From(u.TryGetValue(FIELD, out var raw) ? raw : null);
                CardRules.Normalize(s, month);
                r.Card = CardRules.SwapDraw(s, today, rng);
                written = s.ToMap();
                if (r.Card < 0)
                {
                    r.Error = CardRules.MustDiscard(s) ? DiscardFirstText(s) : T_SWAP_DONE;
                    return r;
                }
                tx.Update(doc, new Dictionary<string, object> { { FIELD, written } });
                r.Ok = true;
                return r;
            })).AsUniTask();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[CardDeck] 山札座で引けませんでした: {ex.Message}");
            return new SwapPlay { Error = T_ERR_NETWORK };
        }
        if (written != null) m.UserData.CardsMap = written;
        if (res.Ok) LocalHistoryLog.Add(HISTORY, $"山札座で トランプの {CardRules.Name(res.Card)} を引いた");
        JourneyService.NotifyChanged();
        return res;
    }

    /// <summary>報酬の書き方（「コーヒーチケット ＋ 5G」）</summary>
    public static string RewardText(PokerReward r)
    {
        var parts = new List<string>();
        if (r.Coupon != null)
            parts.Add(SpaceCoupons.NameOf(r.Coupon) + (r.CouponCount > 1 ? " ×" + r.CouponCount : ""));
        if (r.Gold > 0) parts.Add(IgCurrency.Format(r.Gold));
        return string.Join(" ＋ ", parts);
    }

#if UNITY_EDITOR
    /// <summary>エディタ専用（NewHome の C キー）: 1日1回の決まりを使わずに1枚引く（ポーカーを試すため。本当に書き込む）</summary>
    public static async UniTask<(int card, bool overflow)> DebugDrawAsync()
    {
        var m = UserDataManager.instance;
        if (m == null || string.IsNullOrEmpty(m.UID) || m.UserData == null) return (-1, false);
        string month = MonthKey;
        var rng = new System.Random();
        var db = FirebaseFirestore.DefaultInstance;
        var doc = db.Collection("users").Document(m.UID);
        int card = -1;
        bool overflow = false;
        Dictionary<string, object> written = null;
        await db.RunTransactionAsync(tx => tx.GetSnapshotAsync(doc).ContinueWith(t =>
        {
            var u = t.Result.Exists ? t.Result.ToDictionary() : new Dictionary<string, object>();
            var s = CardState.From(u.TryGetValue(FIELD, out var raw) ? raw : null);
            CardRules.Normalize(s, month);
            card = CardRules.Draw(s, rng);
            overflow = CardRules.Overflow(s);
            written = s.ToMap();
            tx.Update(doc, new Dictionary<string, object> { { FIELD, written } });
            return true;
        })).AsUniTask();
        m.UserData.CardsMap = written;
        LocalHistoryLog.Add(HISTORY, $"トランプの {CardRules.Name(card)} を引いた（デバッグ）");
        return (card, overflow);
    }
#endif

    // ================================================================
    // 絵（カードはコードで描く）
    // ================================================================
    /// <summary>カードの縦横比（縦÷横）</summary>
    public const float ASPECT = 1.42f;

    /// <summary>トランプの色（お知らせの縁・ショップの品物の光。すみれ色）</summary>
    public static readonly Color ACCENT = SpaceTheme.Hex(0xB89BFF);

    private static readonly Color[] TOP =
        { SpaceTheme.Hex(0xFF6B6B), SpaceTheme.Hex(0x5C93FF), SpaceTheme.Hex(0xFFD84A), SpaceTheme.Hex(0x4A4D5A) };
    private static readonly Color[] BOTTOM =
        { SpaceTheme.Hex(0xC9363C), SpaceTheme.Hex(0x2F5FD6), SpaceTheme.Hex(0xE0A800), SpaceTheme.Hex(0x1C1D24) };
    private static readonly Color[] INK = { Color.white, Color.white, SpaceTheme.Hex(0x4A3200), Color.white };

    /// <summary>その色の光の色（選んだカードの光など）</summary>
    public static Color Tint(int code) => CardRules.Valid(code) ? TOP[CardRules.ColorOf(code)] : SpaceTheme.GOLD;

    /// <summary>カードの表（色の地に大きな数字・四隅の小さな数字・小さなきらめき）。w はカードの横幅</summary>
    public static RectTransform CreateCard(RectTransform parent, int code, float w, Vector2 anchor, Vector2 pos,
        string name = null)
    {
        float h = w * ASPECT;
        int color = CardRules.Valid(code) ? CardRules.ColorOf(code) : 0;
        string rank = CardRules.Valid(code) ? CardRules.RankOf(code).ToString() : "";
        var ink = INK[color];
        var rt = SpaceTheme.Rect(name ?? "Card_" + code, parent, anchor, pos, new Vector2(w, h));
        var face = SpaceTheme.Rounded(rt, Color.white, w * 0.11f);
        SpaceTheme.Gradient(face, TOP[color], BOTTOM[color]);
        var edge = SpaceTheme.Rect("Edge", rt);
        SpaceTheme.Stretch(edge, w * 0.05f, w * 0.05f, w * 0.05f, w * 0.05f);
        SpaceTheme.RoundedStroke(edge, SpaceTheme.A(Color.white, 0.6f), w * 0.07f);

        var big = SpaceTheme.Text(rt, "Rank", rank, w * 0.5f, ink, SpaceTheme.CENTER, new Vector2(0f, h * 0.04f),
            new Vector2(w, h * 0.5f), FontStyles.Bold);
        SpaceTextScaler.Exclude(big); // カードの中の大きさは決まった比率で
        var tl = SpaceTheme.Text(rt, "TL", rank, w * 0.18f, ink, new Vector2(0f, 1f), new Vector2(w * 0.2f, -w * 0.2f),
            new Vector2(w * 0.34f, w * 0.24f), FontStyles.Bold);
        SpaceTextScaler.Exclude(tl);
        var br = SpaceTheme.Text(rt, "BR", rank, w * 0.18f, ink, new Vector2(1f, 0f), new Vector2(-w * 0.2f, w * 0.2f),
            new Vector2(w * 0.34f, w * 0.24f), FontStyles.Bold);
        br.rectTransform.localEulerAngles = new Vector3(0f, 0f, 180f);
        SpaceTextScaler.Exclude(br);
        var gem = SpaceTheme.Rect("Gem", rt, SpaceTheme.CENTER, new Vector2(0f, -h * 0.29f), new Vector2(w * 0.2f, w * 0.2f));
        SpaceTheme.Img(gem, SpaceSprites.Sparkle, SpaceTheme.A(ink, 0.85f));
        return rt;
    }

    /// <summary>カードの裏（すみれ色の地に金の縁と、まん中のきらめき）</summary>
    public static RectTransform CreateBack(RectTransform parent, float w, Vector2 anchor, Vector2 pos)
    {
        float h = w * ASPECT;
        var rt = SpaceTheme.Rect("Back", parent, anchor, pos, new Vector2(w, h));
        var face = SpaceTheme.Rounded(rt, Color.white, w * 0.11f);
        SpaceTheme.Gradient(face, SpaceTheme.Hex(0x6A4BC4), SpaceTheme.Hex(0x2A1A66));
        var edge = SpaceTheme.Rect("Edge", rt);
        SpaceTheme.Stretch(edge, w * 0.05f, w * 0.05f, w * 0.05f, w * 0.05f);
        SpaceTheme.RoundedStroke(edge, SpaceTheme.A(SpaceTheme.GOLD, 0.85f), w * 0.07f);
        var glow = SpaceTheme.Rect("Glow", rt, SpaceTheme.CENTER, Vector2.zero, new Vector2(w * 0.9f, w * 0.9f));
        SpaceTheme.Img(glow, SpaceSprites.Glow, SpaceTheme.A(SpaceTheme.GOLD, 0.35f));
        var star = SpaceTheme.Rect("Star", rt, SpaceTheme.CENTER, Vector2.zero, new Vector2(w * 0.42f, w * 0.42f));
        SpaceTheme.Img(star, SpaceSprites.Sparkle, SpaceTheme.GOLD);
        for (int i = 0; i < 4; i++)
        {
            float a = Mathf.PI * 0.25f + i * Mathf.PI * 0.5f;
            var dot = SpaceTheme.Rect("Dot" + i, rt, SpaceTheme.CENTER,
                new Vector2(Mathf.Cos(a) * w * 0.3f, Mathf.Sin(a) * h * 0.3f), new Vector2(w * 0.07f, w * 0.07f));
            SpaceTheme.Img(dot, SpaceSprites.Circle, SpaceTheme.A(SpaceTheme.GOLD, 0.6f));
        }
        return rt;
    }

    private static Sprite _drawIcon;

    /// <summary>「カードを1枚引く」の絵（ショップ・所持品。Resources/ItemIcon/CardDraw。tools/shop/make_card_draw_icon.py）</summary>
    public static Sprite DrawIcon => _drawIcon != null ? _drawIcon : (_drawIcon = Resources.Load<Sprite>("ItemIcon/CardDraw"));
}
