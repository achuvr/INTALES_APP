using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Cysharp.Threading.Tasks;
using Firebase.Firestore;
using UnityEngine;

/// <summary>
/// ミニオン（2026-11-14 ユーザー指定）の保存・書き込み。決まりは Commons/Minions.cs（MinionRules）、画面は SpaceMinions。
/// 保存先は users/{uid}.minions（本人が書けるのでルールの変更はなし）。
/// 書き込みはどれも、先に minions.clock へサーバーの時刻を書いてから（FieldValue.ServerTimestamp）、users を1回読むトランザクションで
/// その時刻を「今」として使う（端末の時計を進めても、24時間の旅・1週間の期限をごまかせないように）。
/// 画面の残り時間は、そのときのサーバーの時刻と端末の時計の差（s_offset）で数える。
/// </summary>
public static class MinionStore
{
    /// <summary>ミニオンを作れるようになる星座（きずな座＝協力プレイ）</summary>
    public const string CONSTELLATION = "bond";
    public const string FIELD = "minions";
    /// <summary>履歴の種類（SpaceHistoryPanel）</summary>
    public const string HISTORY = "minion";

    public const string T_LOCKED = "きずな座を完成させると、ミニオンを作れます";
    public const string T_ERR_USER = "ユーザー情報を読み込めませんでした";
    public const string T_ERR_NETWORK = "通信に失敗しました。もう一度お試しください";
    public const string T_ERR_GONE = "このミニオンは見つかりませんでした";
    public const string T_ERR_LIMIT = "これ以上ミニオンを作れません（ミニオンが Lv.150 になると、もう1体作れます）";
    public const string T_ERR_TRAVELING = "まだ旅の途中です";
    public const string T_ERR_NOT_SENT = "このミニオンは旅に出ていません";
    public const string T_ERR_TALK_FIRST = "先に話しかけて、持ち帰った物を受け取ってください";
    public const string T_ERR_NOT_IN_STORE = "持ち帰った物を受け取れるのは入店中だけです"; // 2026-11-14 ユーザー指定
    public const string T_ERR_LIMIT_BLOCKED = "これ以上ミニオンを作れません（お店からもらった Lv.150 のミニオンは、作れる数を増やしません）";
    public const string T_CARD_NAME = "トランプ（山札から1枚）";
    private const string T_HISTORY_A = "ミニオン「";
    private const string T_HISTORY_CREATED_B = "」が仲間になった";
    private const string T_HISTORY_SENT_B = "」を旅に出した";
    private const string T_HISTORY_BACK_B = "」が旅から帰って ";
    private const string T_HISTORY_BACK_C = " を持ってきた";
    private const string T_HISTORY_FAIL_B = "」の旅は失敗だった";
    private const string T_HISTORY_ATE_B = "」が持ち帰った ";
    private const string T_HISTORY_ATE_C = " を食べてしまった";
    private const string T_HISTORY_MAX_B = "」が Lv.150（MAX）になった";
    private const string T_HISTORY_INSTANT_B = "」を管理者がすぐ帰還させた";
    private const string T_HISTORY_GIFT_A = "お店からミニオン「";
    private const string T_HISTORY_GIFT_B = "」（Lv.";
    private const string T_HISTORY_GIFT_C = "）が届いた";

    public static string GlyphText
    {
        get
        {
            var sb = new StringBuilder()
                .Append(T_LOCKED).Append(T_ERR_USER).Append(T_ERR_NETWORK).Append(T_ERR_GONE).Append(T_ERR_LIMIT)
                .Append(T_ERR_TRAVELING).Append(T_ERR_NOT_SENT).Append(T_ERR_TALK_FIRST).Append(T_ERR_NOT_IN_STORE)
                .Append(T_ERR_LIMIT_BLOCKED).Append(T_CARD_NAME);
            foreach (var n in MinionRules.JOB_NAMES) sb.Append(n);
            foreach (var n in MinionRules.ELEMENT_NAMES) sb.Append(n);
            foreach (var k in MinionRules.AllRewardKeys) sb.Append(RewardName(k));
            return sb.Append("の「」 0123456789%+.LvMAX/").ToString();
        }
    }

    private static UserData Data => UserDataManager.instance != null ? UserDataManager.instance.UserData : null;

    /// <summary>ミニオンを作れるか（きずな座が完成。管理者は初めから）</summary>
    public static bool Unlocked
    {
        get
        {
            if (AdminClaim.IsAdmin) return true;
            var p = Constellations.Progress().Find(x => x.Def.Id == CONSTELLATION);
            return p != null && p.Complete;
        }
    }

    /// <summary>今のミニオン（表示用の写し）</summary>
    public static MinionBook Book => MinionBook.From(Data?.MinionsMap);

    /// <summary>
    /// 入店中か（2026-11-14 ユーザー指定「ミニオンから報酬を貰えるのは入店中のみ」。話しかける・管理者のすぐ帰還は入店中だけ。
    /// 作る・旅に出すはいつでも。INTALES WORLD・ダイスチャレンジと同じ JourneyService.InStoreToday。管理者も同じ）
    /// </summary>
    public static bool InStore => JourneyService.InStoreToday;

    // サーバーの時刻 − 端末の時計（最後のトランザクションで読んだ minions.clock から。まだ読んでいなければ 0＝端末の時計のまま）
    private static TimeSpan s_offset;

    /// <summary>今（サーバーの時刻に合わせた UTC）</summary>
    public static DateTime NowUtc => DateTime.UtcNow + s_offset;

    /// <summary>今日（今の営業日）の "yyyy-MM-dd"</summary>
    public static string TodayKey => DayKeyOf(NowUtc);

    public static string DayKeyOf(DateTime utc) => StoreCalendar.DayKey(StoreCalendar.BusinessDate(StoreCalendar.ToJst(utc)));

    /// <summary>帰ってきて話しかけられるのを待っているミニオンがいるか（ホームの空のきずな座の「!」）</summary>
    public static bool AnyBack
    {
        get
        {
            if (Data?.MinionsMap == null) return false;
            var now = NowUtc;
            return Book.Items.Any(m => MinionRules.StateOf(m, now) == MinionState.Back);
        }
    }

    /// <summary>旅に出したときに持ち帰れる物（使える機能だけ。トランザクションの外で読んでおく）</summary>
    public static MinionUnlocks CurrentUnlocks =>
        new MinionUnlocks { Pieces = PuzzleStore.Unlocked, Gold = IgCurrency.Unlocked, Cards = CardDeck.Unlocked };

    /// <summary>名前（同じ職業・属性のミニオンが2体以上いれば、作った順の番号を添える）</summary>
    public static string NameOf(MinionData m, MinionBook book = null)
    {
        string name = MinionRules.NameOf(m);
        if (book == null) return name;
        var same = book.Items.Where(x => x.Job == m.Job && x.Element == m.Element).ToList();
        if (same.Count < 2) return name;
        return name + " " + (same.IndexOf(same.Find(x => x.Id == m.Id)) + 1);
    }

    // ================================================================
    // 報酬の表示
    // ================================================================
    /// <summary>報酬の名前（「5%OFFチケット」「ピース（L字）」「10G」「トランプ（山札から1枚）」）</summary>
    public static string RewardName(string key)
    {
        if (string.IsNullOrEmpty(key)) return "";
        if (key == MinionRules.CARD) return T_CARD_NAME;
        if (MinionRules.IsPiece(key, out int type)) return PuzzleStore.PieceLabel(type);
        if (MinionRules.IsGold(key, out int amount)) return IgCurrency.Format(amount);
        if (key == SpaceWorld.STAR_FRAGMENT_FIELD) return SpaceWorld.STAR_FRAGMENT_NAME;
        return PuzzleStore.RewardName(key);
    }

    public static Sprite RewardIcon(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        if (key == MinionRules.CARD) return CardDeck.DrawIcon;
        if (MinionRules.IsPiece(key, out int type)) return PuzzleArt.Icon(type);
        if (MinionRules.IsGold(key, out _)) return IgCurrency.Icon;
        if (key == SpaceWorld.STAR_FRAGMENT_FIELD) return SpaceWorld.StarFragmentIcon;
        return PuzzleStore.RewardIcon(key);
    }

    public static Color RewardTint(string key)
    {
        if (key == MinionRules.CARD) return CardDeck.ACCENT;
        if (MinionRules.IsPiece(key, out int type)) return PuzzleArt.ColorOf(type);
        if (MinionRules.IsGold(key, out _)) return SpaceTheme.GOLD;
        if (key == SpaceWorld.STAR_FRAGMENT_FIELD) return SpaceTheme.Hex(0xFFD36E);
        return PuzzleStore.RewardTint(key);
    }

    /// <summary>チケット（角の四角い絵なので、使う側で角を丸める）</summary>
    public static bool RewardIsCoupon(string key) => !string.IsNullOrEmpty(key) && SpaceCoupons.ByField(key) != null;

    // ================================================================
    // 書き込みの結果
    // ================================================================
    /// <summary>1週間すぎて片づけたミニオン（持ち帰った物があれば食べてしまった）</summary>
    public sealed class Ate
    {
        public MinionData Minion;  // 片づけたあと
        public string Name;
        public string Reward;      // 食べてしまった物（失敗した旅なら null）
        public int LevelBefore, LevelAfter;
    }

    public sealed class Result
    {
        public bool Ok;
        public string Error;
        public MinionData Minion;           // 対象のミニオン（書いたあと）
        public string Name;
        public int LevelBefore, LevelAfter;
        public bool TripOk;                 // 話しかけた: 旅がうまくいったか
        public string Reward;               // 話しかけた: 持ち帰った物
        public bool Rare;
        public int Balance;                 // G をもらったときの所持金
        public int StarFragments;           // 星のカケラをもらったときの数（そろった分は、このあと流れ星にする）
        public readonly List<int> Cards = new List<int>(); // 引いたトランプ
        public bool CardOverflow;           // 引いて手札が8枚になった（このあと1枚捨てる）
        public bool Expired;                // 話しかけようとしたら1週間すぎていた（Ate に入る）
        public bool Instant;                // 管理者が旅の途中ですぐ帰還させた（2026-11-14）
        public bool Already;                // お店のミニオンのQRを、その日にもう受け取っていた（2026-11-14）
        public readonly List<Ate> Ate = new List<Ate>();
        internal MinionBook Book;           // 書いたあとのミニオン（サーバーの時刻つき）
        internal DateTime ServerNow, DeviceNow;
        internal Dictionary<string, object> CardsMap, PiecesMap;
    }

    // ================================================================
    // 作る・旅に出す・話しかける・片づける
    // ================================================================
    /// <summary>新しいミニオンを作る（job・element は MinionRules の番号）</summary>
    public static UniTask<Result> CreateAsync(int job, int element)
    {
        if (job < 0 || job >= MinionRules.JOBS || element < 0 || element >= MinionRules.ELEMENTS)
            return UniTask.FromResult(new Result { Error = T_ERR_GONE });
        return RunAsync("作る", (u, book, now, updates) =>
        {
            var r = new Result();
            SettleExpired(book, now, r);
            if (!book.CanCreate)
            {
                r.Error = book.CreateBlocked ? T_ERR_LIMIT_BLOCKED : T_ERR_LIMIT;
                return r;
            }
            var m = new MinionData
            {
                Id = book.Next.ToString(System.Globalization.CultureInfo.InvariantCulture), Number = book.Next,
                Job = job, Element = element, Level = 1, Created = now,
            };
            book.Next++;
            book.Items.Add(m);
            r.Minion = m.Clone();
            r.LevelBefore = r.LevelAfter = 1;
            updates[new FieldPath(FIELD, "items")] = book.ItemsMap();
            updates[new FieldPath(FIELD, "next")] = (long)book.Next;
            r.Ok = true;
            return r;
        }, r =>
        {
            r.Name = NameOf(r.Minion, r.Book);
            LocalHistoryLog.Add(HISTORY, T_HISTORY_A + r.Name + T_HISTORY_CREATED_B);
        });
    }

    /// <summary>
    /// お店のQR（MinionGiftQr。番号 nonce）で、決められた職業・属性・レベルのミニオンを受け取る（2026-11-14）。
    /// その日にもう受け取った番号なら Already（Error も入れる）。持てる数に届いていても受け取れる
    /// （お店からもらったミニオン＝Gift。作れる数には数える。もらった時点で Lv.150 なら作れる数を増やさない＝MinionData.AddsSlot）。
    /// きずな座が完成していなくても受け取れる
    /// </summary>
    public static UniTask<Result> ReceiveGiftAsync(int job, int element, int level, string nonce)
    {
        if (job < 0 || job >= MinionRules.JOBS || element < 0 || element >= MinionRules.ELEMENTS || level < 1
            || level > MinionRules.MAX_LEVEL || string.IsNullOrEmpty(nonce))
            return UniTask.FromResult(new Result { Error = MinionGiftQr.T_INVALID });
        return RunAsync("お店のミニオンを受け取る", (u, book, now, updates) =>
        {
            var r = new Result();
            // その日に受け取った番号だけ残す（前の日の分は消す）
            string today = DayKeyOf(now);
            var keep = book.Claims.Where(kv => kv.Value == today).ToList();
            book.Claims.Clear();
            foreach (var kv in keep) book.Claims[kv.Key] = kv.Value;
            if (book.Claims.ContainsKey(nonce))
            {
                r.Already = true;
                r.Error = MinionGiftQr.T_ALREADY;
                return r;
            }
            book.Claims[nonce] = today;
            var m = new MinionData
            {
                Id = book.Next.ToString(System.Globalization.CultureInfo.InvariantCulture), Number = book.Next,
                Job = job, Element = element, Level = level, Created = now, Gift = true, GiftLevel = level,
            };
            book.Next++;
            book.Items.Add(m);
            r.Minion = m.Clone();
            r.LevelBefore = r.LevelAfter = level;
            updates[new FieldPath(FIELD, "items")] = book.ItemsMap();
            updates[new FieldPath(FIELD, "next")] = (long)book.Next;
            updates[new FieldPath(FIELD, "claims")] = book.ClaimsMap();
            r.Ok = true;
            return r;
        }, r =>
        {
            r.Name = NameOf(r.Minion, r.Book);
            LocalHistoryLog.Add(HISTORY, T_HISTORY_GIFT_A + r.Name + T_HISTORY_GIFT_B
                                         + level.ToString(System.Globalization.CultureInfo.InvariantCulture) + T_HISTORY_GIFT_C);
        });
    }

    /// <summary>待機中のミニオンを旅に出す（帰ってから1週間すぎていたら、先に片づけてから出す）</summary>
    public static UniTask<Result> SendAsync(string id)
    {
        var unlocks = CurrentUnlocks; // トランザクションは裏のスレッドで動くので、Unity の値は先に読んでおく
        var rng = new System.Random(unchecked(Environment.TickCount * 31 + (id ?? "").GetHashCode()));
        return RunAsync("旅に出す", (u, book, now, updates) =>
        {
            var r = new Result();
            SettleExpired(book, now, r);
            var m = book.Find(id);
            if (m == null)
            {
                r.Error = T_ERR_GONE;
                return r;
            }
            var st = MinionRules.StateOf(m, now);
            if (st == MinionState.Traveling)
            {
                r.Error = T_ERR_TRAVELING;
                return r;
            }
            if (st == MinionState.Back)
            {
                r.Error = T_ERR_TALK_FIRST;
                return r;
            }
            MinionRules.Send(m, now, DayKeyOf(now), rng, unlocks);
            r.Minion = m.Clone();
            r.LevelBefore = r.LevelAfter = m.Level;
            updates[new FieldPath(FIELD, "items")] = book.ItemsMap();
            r.Ok = true;
            return r;
        }, r =>
        {
            r.Name = NameOf(r.Minion, r.Book);
            LocalHistoryLog.Add(HISTORY, T_HISTORY_A + r.Name + T_HISTORY_SENT_B);
        });
    }

    /// <summary>
    /// 帰ってきたミニオンに話しかける: 旅がうまくいっていたら持ち帰った物を受け取る（トランプは山札から1枚引く。
    /// 手札が8枚・山札座で引いて捨てる前なら、受け取らずに T_ERR_DISCARD_FIRST）。レベル +1。
    /// 1週間すぎていたら（Expired）、受け取らずに片づける（Ate に入る）。
    /// instant: 管理者だけ、旅の途中のミニオンをすぐ帰還させて、そのまま受け取る（2026-11-14 ユーザー指定。
    /// 旅の結果と持ち帰る物は旅に出したときに決めたもの。帰ってきたので Lv +1 も同じ）。
    /// 受け取れるのは入店中だけ（InStore。管理者のすぐ帰還も）
    /// </summary>
    public static UniTask<Result> TalkAsync(string id, bool instant = false)
    {
        if (!InStore) return UniTask.FromResult(new Result { Error = T_ERR_NOT_IN_STORE });
        string cardMonth = CardDeck.MonthKey; // トランザクションは裏のスレッドで動くので先に読んでおく
        bool admin = AdminClaim.IsAdmin;
        var rng = new System.Random(unchecked(Environment.TickCount * 17 + (id ?? "").GetHashCode()));
        return RunAsync(instant ? "すぐ帰還させる（管理者）" : "話しかける", (u, book, now, updates) =>
        {
            var r = new Result();
            var m = book.Find(id);
            if (m == null)
            {
                r.Error = T_ERR_GONE;
                return r;
            }
            var st = MinionRules.StateOf(m, now);
            if (st == MinionState.Idle)
            {
                r.Error = T_ERR_NOT_SENT;
                return r;
            }
            if (st == MinionState.Traveling)
            {
                if (!instant || !admin)
                {
                    r.Error = T_ERR_TRAVELING;
                    return r;
                }
                r.Instant = true; // 管理者: 24時間を待たずに帰ってきたことにする
            }
            if (st == MinionState.Expired)
            {
                SettleExpired(book, now, r);
                r.Expired = true;
                r.Minion = book.Find(id)?.Clone();
                updates[new FieldPath(FIELD, "items")] = book.ItemsMap();
                r.Ok = true;
                return r;
            }

            r.TripOk = m.Ok;
            r.Reward = m.Ok ? m.Reward : null;
            r.Rare = m.Ok && m.Rare;
            if (r.TripOk && !string.IsNullOrEmpty(r.Reward))
            {
                string key = r.Reward;
                if (key == MinionRules.CARD)
                {
                    var s = CardState.From(u.TryGetValue(CardDeck.FIELD, out var cardsRaw) ? cardsRaw : null);
                    CardRules.Normalize(s, cardMonth);
                    if (CardRules.MustDiscard(s))
                    {
                        // 手札が8枚（捨てる前）・山札座で引いて捨てる前: 受け取らずに、先に捨ててもらう
                        r.Error = CardDeck.DiscardFirstText(s);
                        r.CardsMap = s.ToMap();
                        return r;
                    }
                    int c = CardRules.Draw(s, rng);
                    if (c >= 0) r.Cards.Add(c);
                    r.CardOverflow = CardRules.Overflow(s);
                    r.CardsMap = s.ToMap();
                    updates[new FieldPath(CardDeck.FIELD)] = r.CardsMap;
                }
                else if (MinionRules.IsPiece(key, out int type))
                {
                    var owned = PuzzleRules.PiecesFrom(u.TryGetValue(PuzzleStore.FIELD_PIECES, out var piecesRaw) ? piecesRaw : null);
                    owned[type]++;
                    r.PiecesMap = PuzzleRules.PiecesToMap(owned);
                    updates[new FieldPath(PuzzleStore.FIELD_PIECES)] = r.PiecesMap;
                }
                else if (MinionRules.IsGold(key, out int amount))
                {
                    long balance = Math.Max(0, Long(u, IgCurrency.FIELD)) + amount;
                    updates[new FieldPath(IgCurrency.FIELD)] = balance;
                    r.Balance = (int)balance;
                }
                else
                {
                    // チケット・コマ・星のカケラ・惑星回復薬は1つ足す（星のカケラが5個そろったら、このあと流れ星）
                    long after = Math.Max(0, Long(u, key)) + 1;
                    updates[new FieldPath(key)] = after;
                    if (key == SpaceWorld.STAR_FRAGMENT_FIELD) r.StarFragments = (int)after;
                }
            }
            r.LevelBefore = m.Level;
            MinionRules.Finish(m, false);
            r.LevelAfter = m.Level;
            r.Minion = m.Clone();
            updates[new FieldPath(FIELD, "items")] = book.ItemsMap();
            r.Ok = true;
            return r;
        }, r =>
        {
            r.Name = NameOf(r.Minion, r.Book);
            if (r.Expired) return;
            var d = Data;
            if (d != null && r.TripOk && !string.IsNullOrEmpty(r.Reward)) AddLocal(d, r);
            if (r.Instant) LocalHistoryLog.Add(HISTORY, T_HISTORY_A + r.Name + T_HISTORY_INSTANT_B);
            if (r.TripOk && !string.IsNullOrEmpty(r.Reward))
            {
                LocalHistoryLog.Add(HISTORY, T_HISTORY_A + r.Name + T_HISTORY_BACK_B + RewardName(r.Reward) + T_HISTORY_BACK_C);
                foreach (var c in r.Cards) LocalHistoryLog.Add(CardDeck.HISTORY, $"ミニオンが トランプの {CardRules.Name(c)} を持ってきた");
            }
            else LocalHistoryLog.Add(HISTORY, T_HISTORY_A + r.Name + T_HISTORY_FAIL_B);
            if (r.LevelBefore < MinionRules.MAX_LEVEL && r.LevelAfter >= MinionRules.MAX_LEVEL)
                LocalHistoryLog.Add(HISTORY, T_HISTORY_A + r.Name + T_HISTORY_MAX_B);
        }, r =>
        {
            // 受け取れなかった（手札がいっぱい）ときも、手元の手札と山札をサーバーの値にそろえる
            if (r != null && r.CardsMap != null && Data != null) Data.CardsMap = r.CardsMap;
        });
    }

    /// <summary>
    /// 帰ってから1週間すぎたミニオンを片づける（画面を開いたとき。片づけたものがなくても、サーバーの時刻を読んで残り時間を合わせる）
    /// </summary>
    public static UniTask<Result> SyncAsync() =>
        RunAsync("時刻を合わせる", (u, book, now, updates) =>
        {
            var r = new Result();
            SettleExpired(book, now, r);
            if (r.Ate.Count > 0) updates[new FieldPath(FIELD, "items")] = book.ItemsMap();
            r.Ok = true;
            return r;
        }, null);

    /// <summary>
    /// 帰ってから1週間すぎたミニオンを片づける: 帰ってきたのでレベル +1、持ち帰った物があれば食べてしまってさらに +1。
    /// 片づけたものは r.Ate に入れる（トランザクションの中で呼ぶ）
    /// </summary>
    private static void SettleExpired(MinionBook book, DateTime now, Result r)
    {
        foreach (var m in book.Items)
        {
            if (MinionRules.StateOf(m, now) != MinionState.Expired) continue;
            string reward = m.Ok ? m.Reward : null;
            int before = m.Level;
            MinionRules.Finish(m, reward != null);
            r.Ate.Add(new Ate { Minion = m.Clone(), Reward = reward, LevelBefore = before, LevelAfter = m.Level });
        }
    }

    /// <summary>
    /// 書き込みの共通の流れ: minions.clock にサーバーの時刻を書く → users を読むトランザクションで body を呼ぶ（その時刻が「今」）→
    /// 書いたミニオンを手元の UserData にも入れる → after（履歴など）。失敗なら failed
    /// </summary>
    private static async UniTask<Result> RunAsync(string what,
        Func<Dictionary<string, object>, MinionBook, DateTime, Dictionary<FieldPath, object>, Result> body,
        Action<Result> after, Action<Result> failed = null)
    {
        var mgr = UserDataManager.instance;
        if (mgr == null || string.IsNullOrEmpty(mgr.UID) || mgr.UserData == null) return new Result { Error = T_ERR_USER };
        var db = FirebaseFirestore.DefaultInstance;
        var doc = db.Collection("users").Document(mgr.UID);
        try
        {
            // 通信できないと書き込みが終わらないまま待ち続けるので、15秒で切り上げる（書き込み自体は、つながったときに届く）
            await doc.UpdateAsync(new Dictionary<FieldPath, object> { { new FieldPath(FIELD, "clock"), FieldValue.ServerTimestamp } })
                .AsUniTask().Timeout(TimeSpan.FromSeconds(15), DelayType.Realtime);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[MinionStore] サーバーの時刻を書けませんでした（{what}）: {ex.Message}");
            return new Result { Error = T_ERR_NETWORK };
        }

        Result res;
        try
        {
            res = await db.RunTransactionAsync(tx => tx.GetSnapshotAsync(doc).ContinueWith(t =>
            {
                var u = t.Result.Exists ? t.Result.ToDictionary() : null;
                if (u == null) return new Result { Error = T_ERR_USER };
                var book = MinionBook.From(u.TryGetValue(FIELD, out var raw) ? raw : null);
                if (!book.Clock.HasValue) return new Result { Error = T_ERR_NETWORK };
                var now = book.Clock.Value;
                var updates = new Dictionary<FieldPath, object>();
                var r = body(u, book, now, updates) ?? new Result { Error = T_ERR_NETWORK };
                r.Book = book;
                r.ServerNow = now;
                r.DeviceNow = DateTime.UtcNow;
                if (r.Ok && updates.Count > 0) tx.Update(doc, updates);
                return r;
            })).AsUniTask();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[MinionStore] 書き込めませんでした（{what}）: {ex.Message}");
            return new Result { Error = T_ERR_NETWORK };
        }
        if (res == null) return new Result { Error = T_ERR_NETWORK };

        // サーバーの時刻と端末の時計の差（残り時間の表示に使う）
        if (res.ServerNow != default) s_offset = res.ServerNow - res.DeviceNow;
        if (!res.Ok)
        {
            failed?.Invoke(res);
            return res;
        }
        var d = Data;
        if (d != null && res.Book != null) d.MinionsMap = res.Book.ToMap();
        foreach (var a in res.Ate)
        {
            a.Name = NameOf(a.Minion, res.Book);
            if (a.Reward != null)
                LocalHistoryLog.Add(HISTORY, T_HISTORY_A + a.Name + T_HISTORY_ATE_B + RewardName(a.Reward) + T_HISTORY_ATE_C);
            if (a.LevelBefore < MinionRules.MAX_LEVEL && a.LevelAfter >= MinionRules.MAX_LEVEL)
                LocalHistoryLog.Add(HISTORY, T_HISTORY_A + a.Name + T_HISTORY_MAX_B);
        }
        after?.Invoke(res);
        JourneyService.NotifyChanged(); // ホームの表示（所持品の数など）を更新
        AppNotifications.Reschedule();   // 帰ってくる時刻の通知を予約し直す
        return res;
    }

    /// <summary>受け取った物を手元の UserData にも足す（書き込んだ値をそのまま入れる）</summary>
    private static void AddLocal(UserData d, Result r)
    {
        string key = r.Reward;
        if (key == MinionRules.CARD)
        {
            if (r.CardsMap != null) d.CardsMap = r.CardsMap;
            return;
        }
        if (MinionRules.IsPiece(key, out _))
        {
            if (r.PiecesMap != null) d.PiecesMap = r.PiecesMap;
            return;
        }
        if (MinionRules.IsGold(key, out _))
        {
            d.Ig = r.Balance;
            return;
        }
        if (key == SpaceWorld.STAR_FRAGMENT_FIELD)
        {
            d.StarFragment = r.StarFragments;
            return;
        }
        if (key == SpacePlanetPotion.FIELD)
        {
            d.PlanetPotion += 1;
            return;
        }
        var coupon = SpaceCoupons.ByField(key);
        if (coupon != null)
        {
            coupon.Add(d, 1);
            return;
        }
        SpaceMeeples.ByField(key)?.Add(d, 1);
    }

    private static long Long(Dictionary<string, object> m, string k)
    {
        if (m == null || !m.TryGetValue(k, out var v) || v == null) return 0;
        try { return Convert.ToInt64(v); }
        catch { return 0; }
    }

#if UNITY_EDITOR
    /// <summary>エディタ専用: 旅の途中のミニオンを全員、今帰ってきたことにする（24時間待たずに試すため。本当に書き込む）</summary>
    public static UniTask<Result> DebugReturnAllAsync() =>
        RunAsync("すぐ帰らせる（デバッグ）", (u, book, now, updates) =>
        {
            var r = new Result();
            int n = 0;
            foreach (var m in book.Items)
            {
                if (MinionRules.StateOf(m, now) != MinionState.Traveling) continue;
                m.Sent = now.AddHours(-MinionRules.TRIP_HOURS).AddSeconds(-1);
                n++;
            }
            r.LevelAfter = n; // 帰らせた数
            if (n > 0) updates[new FieldPath(FIELD, "items")] = book.ItemsMap();
            r.Ok = true;
            return r;
        }, null);
#endif
}

/// <summary>ミニオンの絵と色（絵は Resources/Minions。tools/minion/make_minion_art.py で作る）</summary>
public static class MinionArt
{
    private static readonly string[] JOB_FILES = { "warrior", "magician", "archer", "gunner" };
    private static readonly string[] ELEMENT_FILES = { "fire", "water", "nature", "thunder" };
    /// <summary>属性の色（炎・水・自然・雷。光・縁・文字に使う）</summary>
    private static readonly Color[] ELEMENT_COLORS =
    {
        SpaceTheme.Hex(0xFF8A5C), SpaceTheme.Hex(0x6CB6FF), SpaceTheme.Hex(0x6EE596), SpaceTheme.Hex(0xFFDA55),
    };

    private static readonly Dictionary<int, Sprite> s_sprites = new Dictionary<int, Sprite>();
    private static readonly Sprite[] s_icons = new Sprite[MinionRules.ELEMENTS];

    public static Color ColorOf(int element) => ELEMENT_COLORS[Mathf.Clamp(element, 0, MinionRules.ELEMENTS - 1)];

    /// <summary>ミニオンの絵（職業 × 属性。512×512・足もとをそろえてある）</summary>
    public static Sprite Sprite(int job, int element)
    {
        job = Mathf.Clamp(job, 0, MinionRules.JOBS - 1);
        element = Mathf.Clamp(element, 0, MinionRules.ELEMENTS - 1);
        int key = job * MinionRules.ELEMENTS + element;
        if (s_sprites.TryGetValue(key, out var s) && s != null) return s;
        s = Resources.Load<Sprite>("Minions/" + JOB_FILES[job] + "_" + ELEMENT_FILES[element]);
        s_sprites[key] = s;
        return s;
    }

    public static Sprite Sprite(MinionData m) => Sprite(m.Job, m.Element);

    /// <summary>属性の印（炎・しずく・葉・稲妻）</summary>
    public static Sprite ElementIcon(int element)
    {
        element = Mathf.Clamp(element, 0, MinionRules.ELEMENTS - 1);
        if (s_icons[element] == null) s_icons[element] = Resources.Load<Sprite>("Minions/element_" + ELEMENT_FILES[element]);
        return s_icons[element];
    }
}
