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
///  ・コインを1枚入れるたびに 1G。台の手前から落ちた物を獲得（コイン＝1G・赤／青／黄／黒コマ＝1個・星のカケラ＝1個・
///    惑星回復薬＝1個）。手前の左右から横に落ちた物は没収。コインを BONUS_EVERY 枚入れるごとに、ボーナスで星のカケラ・コマ・
///    惑星回復薬のどれかが1つ落ちてくる（RollBonus。ピース座が完成している人は、ピースパズルのピースも落ちてくる＝BONUS_PIECE。
///    2026-11-13）。コインを TOWER_EVERY 枚ゲットするごとに、ボーナスでプッシャーの上に
///    コインタワー（TOWER_COINS 枚を積んだ柱。いちばん上はプラチナコイン＝PLATINUM_VALUE の 5G）が出て、
///    コマ（赤・青・黄・黒のどれか）も1つ落ちてくる
///  ・台の上の物の並びは端末に保存して、次に開いたときに続きから（閉じて開き直しても景品は戻らない）
///  ・台は月に1回リセット（2026-11-13 ユーザー指定「コインプッシャーも1カ月に1回リセットがかかるように。管理者にはリセットボタン」）:
///    営業日の月（MonthKey。トランプの月替わりと同じ朝4時切り替え）が台の月より後になったら、はじめの並びに戻す
///    （ボーナス・コインタワーまでの数も 0 から。送っていない増減＝獲得した物はそのまま）。管理者は画面右上の「リセット」でいつでも
///  ・所持金・コマ・星のカケラ・惑星回復薬の増減は、手元の数はすぐに、サーバー（users）には数秒ごとと閉じるときにまとめて送る
///    （FieldValue.Increment。送れなかった分は端末に残して、次に送る）。星のカケラが5個そろったら、閉じたあとホームで流れ星
/// </summary>
public static class CoinPusher
{
    public const string SCENE = "CoinPusher";
    /// <summary>遊べるようになる星座（積み木座）</summary>
    public const string CONSTELLATION = "block";
    /// <summary>コイン1枚の値段（G）</summary>
    public const int COST = 1;
    /// <summary>
    /// この枚数を入れるごとに、ボーナスの景品（星のカケラ・コマ・惑星回復薬のどれか）が1つ落ちてくる
    /// （2026-11-12 ユーザー指定で 15→20。数えている途中の枚数は台と一緒に保存してあるので、そのまま20まで数える）
    /// </summary>
    public const int BONUS_EVERY = 20;
    /// <summary>
    /// ボーナスの景品の割合: 星のカケラ・惑星回復薬。残り（45%）がコマで、赤・青・黄を同じ割合で（各15%）。
    /// 2026-11-12 ユーザー指定「15Gごとのボーナスに惑星回復ポーションも落ちてくるように」で惑星回復薬を足した
    /// （星のカケラを 55%→40% にして、その分を惑星回復薬に。コマは前のまま）
    /// </summary>
    public const float BONUS_STAR = 0.40f;
    public const float BONUS_POTION = 0.15f;
    /// <summary>
    /// ピース座が完成している人（管理者は初めから）のボーナスの景品のうち、ピースパズルのピースになる割合
    /// （2026-11-13 ユーザー指定「ピース座をアクティブにすると、コインプッシャーにピースも落ちてくるように。レアも含めていい」。
    /// 割合はこちらで決めた）。ピースの種類はショップのランダムと同じ（U字＝レアは PuzzleRules.RARE_PERCENT %、ほかの7種類は同じ割合）。
    /// 残りは、いつもの景品（星のカケラ・惑星回復薬・コマ）をいつもの割合で
    /// </summary>
    public const float BONUS_PIECE = 0.25f;
    /// <summary>
    /// コインをこの枚数ゲットする（受け皿に落とす）ごとに、ボーナスでコインタワーが出る
    /// （2026-11-12 ユーザー指定「30枚コインを落下させてゲットするとボーナスでコインタワーが出現」。数は台と一緒に端末に保存）。
    /// 同時にコマも1つ、タワーの横に落ちてくる（同日ユーザー指定「タワーボーナスに黒ミープルを落とすのも追加」→
    /// 「黒固定ではなく、4色の中からランダムで」。TOWER_MEEPLES から同じ割合で）。
    /// 枚数は同日ユーザー指定で 30→40（数えている途中の枚数は台と一緒に保存してあるので、そのまま40まで数える）
    /// </summary>
    public const int TOWER_EVERY = 40;
    /// <summary>コインタワーに積むコインの枚数（いちばん上の1枚はプラチナコイン。2026-11-13）</summary>
    public const int TOWER_COINS = 10;
    /// <summary>
    /// プラチナコインの価値（G）。2026-11-13 ユーザー指定「コインタワーの一番上に乗るコインはプラチナコインに。5Gの価値」。
    /// 受け皿に落ちると 5G（所持金・今回の獲得の G に足す）。コインタワーまでの数には、G のコインと同じく1枚と数える
    /// </summary>
    public const int PLATINUM_VALUE = 5;
    /// <summary>プラチナコインのアイコン（Resources/ItemIcon/PlatinumCoin。tools/coin/make_ig_coin_icon.py --platinum）</summary>
    public static Sprite PlatinumIcon => s_platinumIcon != null ? s_platinumIcon
        : (s_platinumIcon = Resources.Load<Sprite>("ItemIcon/PlatinumCoin"));
    private static Sprite s_platinumIcon;
    /// <summary>コインタワーと一緒に落ちてくるコマの色（この中から同じ割合で1つ）</summary>
    public static readonly PusherItem[] TOWER_MEEPLES =
        { PusherItem.MeepleRed, PusherItem.MeepleBlue, PusherItem.MeepleYellow, PusherItem.MeepleBlack };
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
    public const string T_BONUS_FALL_B = "が落ちてくる";      // 「星のカケラが落ちてくる」「惑星回復薬が落ちてくる」
    public const string T_TOWER = "コインタワー！";
    public const string T_TOWER_SUB_A = "コインを ";          // 「コインを 40 枚ゲットしたボーナス」
    public const string T_TOWER_SUB_B = " 枚ゲットしたボーナス";
    public const string T_TOWER_ALSO_B = "も落ちてくる";      // 「黒コマも落ちてくる」
    public const string T_SAVING = "保存しています…";
    public const string T_SAVE_FAILED = "通信に失敗しました。結果は次に開いたときに送ります";
    public const string T_SUMMARY_A = "コインプッシャー：";
    public const string T_SUMMARY_SPENT_B = " 使って ";
    public const string T_SUMMARY_GOT_B = " を獲得";
    public const string T_SUMMARY_NONE = "獲得はありませんでした";
    public const string T_RULES = "手前から落ちた物を獲得 ・ 左右から落ちた物は没収";
    public const string T_MONTHLY = "台が新しくなりました";               // 月が変わってリセットしたときの知らせ
    public const string T_MONTHLY_SUB = "月が変わったので、台をリセットしました";
    public const string T_RESET = "リセット";                            // 管理者だけのボタン・確認の決定・リセットしたときの知らせ
    public const string T_RESET_CONFIRM = "台をリセットしますか？";
    public const string T_RESET_CONFIRM_BODY = "台の上のコインと景品を消して、\nはじめの状態に戻します";
    public const string T_RESET_DONE = "台をはじめの状態に戻しました";
    public const string T_PLATINUM = "プラチナ";             // 獲得したときに浮かぶ「プラチナ +5G」
    public const string T_PLATINUM_NAME = "プラチナコイン";

    /// <summary>この画面で表示しうる文字（NewHome の起動時にまとめてフォントへ登録する）</summary>
    public static string GlyphText => new StringBuilder()
        .Append(T_TITLE).Append(T_CAPTION).Append(T_SIGN).Append(T_LOCKED).Append(T_DROP).Append(T_DROP_SUB).Append(T_HINT)
        .Append(T_WALLET).Append(T_GOT).Append(T_SHORT).Append(T_FULL).Append(T_BONUS).Append(T_BONUS_FALL_B)
        .Append(T_TOWER).Append(T_TOWER_SUB_A).Append(T_TOWER_SUB_B).Append(T_TOWER_ALSO_B).Append(T_SAVING).Append(T_SAVE_FAILED)
        .Append(T_SUMMARY_A).Append(T_SUMMARY_SPENT_B).Append(T_SUMMARY_GOT_B).Append(T_SUMMARY_NONE).Append(T_RULES)
        .Append(T_MONTHLY).Append(T_MONTHLY_SUB).Append(T_RESET).Append(T_RESET_CONFIRM).Append(T_RESET_CONFIRM_BODY)
        .Append(T_RESET_DONE).Append("キャンセル").Append(T_PLATINUM).Append(T_PLATINUM_NAME)
        .Append(SpaceWorld.STAR_FRAGMENT_NAME).Append(SpacePlanetPotion.NAME)
        .Append(SpaceMeeples.GlyphText).Append(IgCurrency.GlyphText).Append(PuzzleStore.GlyphText)
        .Append("+×・：、0123456789").ToString();

    /// <summary>
    /// ボーナスの景品を決める（r・r2 は 0〜1 の乱数）。星のカケラ 40%・惑星回復薬 15%・赤／青／黄コマ 各15%。
    /// pieces（ピース座が完成している）なら、まず BONUS_PIECE の割合でピース（種類は r2 で RollPieceType）、残りをいつもの割合で
    /// </summary>
    public static PusherItem RollBonus(float r, bool pieces = false, float r2 = 0f)
    {
        if (pieces)
        {
            if (r < BONUS_PIECE) return CoinPusherWorld.PieceItem(RollPieceType(r2));
            r = (r - BONUS_PIECE) / (1f - BONUS_PIECE);
        }
        if (r < BONUS_STAR) return PusherItem.StarFragment;
        r -= BONUS_STAR;
        if (r < BONUS_POTION) return PusherItem.PlanetPotion;
        r -= BONUS_POTION;
        float each = (1f - BONUS_STAR - BONUS_POTION) / 3f;
        return r < each ? PusherItem.MeepleRed : r < each * 2f ? PusherItem.MeepleBlue : PusherItem.MeepleYellow;
    }

    /// <summary>
    /// ボーナスで落ちてくるピースの種類（r は 0〜1 の乱数）。ショップのランダム（PuzzleRules.RollPiece）と同じ割合:
    /// U字（レア）が RARE_PERCENT %、ほかの7種類は同じ割合
    /// </summary>
    public static int RollPieceType(float r)
    {
        float rare = PuzzleRules.RARE_PERCENT / 100f;
        if (r < rare) return PuzzleRules.RARE;
        int t = 1 + (int)((r - rare) / (1f - rare) * (PuzzleRules.TYPES - 1));
        return Mathf.Clamp(t, 1, PuzzleRules.TYPES - 1);
    }

    /// <summary>コインタワーと一緒に落ちてくるコマを決める（r は 0〜1 の乱数）。赤・青・黄・黒 各25%</summary>
    public static PusherItem RollTowerMeeple(float r) =>
        TOWER_MEEPLES[Mathf.Clamp((int)(r * TOWER_MEEPLES.Length), 0, TOWER_MEEPLES.Length - 1)];

    // ================================================================
    // 月に1回のリセット（2026-11-13）
    // ================================================================
    /// <summary>今の営業日の月（"yyyy-MM"。朝4時切り替え。トランプの月替わりと同じ）</summary>
    public static string MonthKey => CardRules.MonthKey(StoreCalendar.TodayBusiness);

    /// <summary>
    /// 台の月 fieldMonth より今の月 month が後か（＝月が変わったので台をリセットする）。"yyyy-MM" は文字の順が月の順。
    /// 先へ進んだときだけにして、端末の日付を前後に行き来させても、そのたびにリセットされないようにする
    /// </summary>
    public static bool Expired(string fieldMonth, string month) => string.CompareOrdinal(month ?? "", fieldMonth ?? "") > 0;

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
    /// <summary>
    /// 使った G と獲得した物の数（まだ送っていない増減。今回遊んだ分を数えるのにも使う）。
    /// 黒コマ・惑星回復薬は 2026-11-12 に追加（前に端末へ書いた記録では 0）
    /// </summary>
    [Serializable]
    public sealed class Ledger
    {
        public int spent;   // 使った G
        public int gold;    // 獲得した G
        public int red, blue, yellow; // 獲得したコマ
        public int black;   // 獲得した黒コマ（コインタワーのボーナス）
        public int star;    // 獲得した星のカケラ
        public int potion;  // 獲得した惑星回復薬（ボーナスの景品）
        /// <summary>獲得したピースパズルのピース（添字が種類 1〜8。0 は使わない。2026-11-13 追加。前に端末へ書いた記録にはない）</summary>
        public int[] pieces = new int[PuzzleRules.TYPES + 1];

        /// <summary>ピースの数（前の記録から読んで長さが合わないときは、長さをそろえる）</summary>
        public int[] Pieces
        {
            get
            {
                if (pieces == null || pieces.Length != PuzzleRules.TYPES + 1)
                {
                    var fixedArr = new int[PuzzleRules.TYPES + 1];
                    if (pieces != null) Array.Copy(pieces, fixedArr, Math.Min(pieces.Length, fixedArr.Length));
                    pieces = fixedArr;
                }
                return pieces;
            }
        }

        public bool Empty => spent == 0 && gold == 0 && red == 0 && blue == 0 && yellow == 0 && black == 0 && star == 0
                             && potion == 0 && Pieces.All(n => n == 0);

        /// <summary>獲得したピースの数（全種類の合計。上の帯の「今回の獲得」）</summary>
        public int PieceTotal => Pieces.Sum();

        /// <summary>写し（ピースの数の並びも別のものにする。同じ並びを分け合うと、送っているあいだに増えた分まで引いてしまう）</summary>
        public Ledger Copy()
        {
            var c = (Ledger)MemberwiseClone();
            c.pieces = (int[])Pieces.Clone();
            return c;
        }

        /// <summary>獲得した数（item の種類の）</summary>
        public int Count(PusherItem item) => item switch
        {
            PusherItem.Coin => gold,
            PusherItem.MeepleRed => red,
            PusherItem.MeepleBlue => blue,
            PusherItem.MeepleYellow => yellow,
            PusherItem.MeepleBlack => black,
            PusherItem.StarFragment => star,
            PusherItem.PlanetPotion => potion,
            _ when CoinPusherWorld.IsPiece(item) => Pieces[CoinPusherWorld.PieceType(item)],
            _ => 0,
        };

        /// <summary>獲得を1つ数える（プラチナコインは G に PLATINUM_VALUE 足す）</summary>
        public void Add(PusherItem item)
        {
            switch (item)
            {
                case PusherItem.Coin: gold++; break;
                case PusherItem.PlatinumCoin: gold += PLATINUM_VALUE; break;
                case PusherItem.MeepleRed: red++; break;
                case PusherItem.MeepleBlue: blue++; break;
                case PusherItem.MeepleYellow: yellow++; break;
                case PusherItem.MeepleBlack: black++; break;
                case PusherItem.StarFragment: star++; break;
                case PusherItem.PlanetPotion: potion++; break;
                default:
                    if (CoinPusherWorld.IsPiece(item)) Pieces[CoinPusherWorld.PieceType(item)]++;
                    break;
            }
        }

        public void Subtract(Ledger o)
        {
            spent -= o.spent;
            gold -= o.gold;
            red -= o.red;
            blue -= o.blue;
            yellow -= o.yellow;
            black -= o.black;
            star -= o.star;
            potion -= o.potion;
            var mine = Pieces;
            var theirs = o.Pieces;
            for (int t = 1; t <= PuzzleRules.TYPES; t++) mine[t] -= theirs[t];
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
        d.BlackMeeple += l.black;
        d.StarFragment += l.star;
        d.PlanetPotion += l.potion;
        for (int t = 1; t <= PuzzleRules.TYPES; t++)
            if (l.Pieces[t] != 0) PuzzleStore.AddPiecesLocal(t, l.Pieces[t]);
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
        l.Add(item);
        switch (item)
        {
            case PusherItem.Coin: d.Ig++; break;
            case PusherItem.PlatinumCoin: d.Ig += PLATINUM_VALUE; break;
            case PusherItem.MeepleRed: d.RedMeeple++; break;
            case PusherItem.MeepleBlue: d.BlueMeeple++; break;
            case PusherItem.MeepleYellow: d.YellowMeeple++; break;
            case PusherItem.MeepleBlack: d.BlackMeeple++; break;
            case PusherItem.StarFragment: d.StarFragment++; break;
            case PusherItem.PlanetPotion: d.PlanetPotion++; break;
            default:
                // ピース（2026-11-13）: 手元の持っているピースにすぐ足す（パズルの手持ちの欄にも出る）
                if (CoinPusherWorld.IsPiece(item)) PuzzleStore.AddPiecesLocal(CoinPusherWorld.PieceType(item), 1);
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
        // ピースは users.pieces の中の種類ごとの数なので、入れ子の場所（FieldPath）で足す。ほかもそろえて FieldPath で書く
        var updates = new Dictionary<FieldPath, object>();
        int net = snap.gold - snap.spent;
        if (net != 0) updates[new FieldPath(IgCurrency.FIELD)] = FieldValue.Increment(net);
        if (snap.red != 0) updates[new FieldPath(SpaceMeeples.Of(PlanetColor.Red).Field)] = FieldValue.Increment(snap.red);
        if (snap.blue != 0) updates[new FieldPath(SpaceMeeples.Of(PlanetColor.Blue).Field)] = FieldValue.Increment(snap.blue);
        if (snap.yellow != 0) updates[new FieldPath(SpaceMeeples.Of(PlanetColor.Yellow).Field)] = FieldValue.Increment(snap.yellow);
        if (snap.black != 0) updates[new FieldPath(SpaceMeeples.Black.Field)] = FieldValue.Increment(snap.black);
        if (snap.star != 0) updates[new FieldPath(SpaceWorld.STAR_FRAGMENT_FIELD)] = FieldValue.Increment(snap.star);
        if (snap.potion != 0) updates[new FieldPath(SpacePlanetPotion.FIELD)] = FieldValue.Increment(snap.potion);
        for (int t = 1; t <= PuzzleRules.TYPES; t++)
            if (snap.Pieces[t] != 0)
                updates[new FieldPath(PuzzleStore.FIELD_PIECES, t.ToString(System.Globalization.CultureInfo.InvariantCulture))] =
                    FieldValue.Increment(snap.Pieces[t]);
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
        // 流れ星の当たり（Prizes）・ノワール（Unlocked）・当たりで増えた物（Rewards。クーポン・持ち物）
        public readonly JourneyResult Star = new JourneyResult();
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
                    JourneyRules.AddPrize(JourneyRules.Roll(rng, j?.Streak ?? 0, centerId, weekday, season), owned, r.Star);
                // クーポン・持ち物（コマ・G・惑星回復薬・5%OFFチケットのカケラ。2026-11-12〜）は数を増やす
                r.Star.Rewards = JourneyService.StarRewardsOf(r.Star.Prizes, u);
                var updates = new Dictionary<string, object> { { SpaceWorld.STAR_FRAGMENT_FIELD, (long)r.Left } };
                JourneyService.WriteStarRewards(updates, r.Star.Rewards);
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
        JourneyService.ApplyStarRewardsLocal(d, res.Star.Rewards);
        foreach (var id in res.Star.Unlocked)
            if (!d.Planets.Contains(id)) d.Planets.Add(id);
        if (res.Star.Unlocked.Count > 0) SpacePlanets.MarkNews();
        LocalHistoryLog.Add("fragment",
            $"{SpaceWorld.STAR_FRAGMENT_NAME}が{SpaceWorld.STAR_FRAGMENTS_PER_STAR}個そろって 流れ星を見た"
            + (res.Stars > 1 ? $"（{res.Stars}回）" : ""));
        JourneyService.LogStarPrizes(res.Star.Prizes, res.Star.Rewards);
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
    /// <summary>「10G 使って 6G・赤コマ1個・星のカケラ2個 を獲得」（今回遊んだ分 s。何もしていなければ null）</summary>
    public static string SessionText(Ledger s)
    {
        if (s == null || s.Empty) return null;
        var parts = new List<string>();
        if (s.gold > 0) parts.Add(IgCurrency.Format(s.gold));
        if (s.red > 0) parts.Add(SpaceMeeples.Of(PlanetColor.Red).Name + s.red + "個");
        if (s.blue > 0) parts.Add(SpaceMeeples.Of(PlanetColor.Blue).Name + s.blue + "個");
        if (s.yellow > 0) parts.Add(SpaceMeeples.Of(PlanetColor.Yellow).Name + s.yellow + "個");
        if (s.black > 0) parts.Add(SpaceMeeples.Black.Name + s.black + "個");
        if (s.star > 0) parts.Add(SpaceWorld.STAR_FRAGMENT_NAME + s.star + "個");
        if (s.potion > 0) parts.Add(SpacePlanetPotion.NAME + s.potion + "個");
        for (int t = 1; t <= PuzzleRules.TYPES; t++)
            if (s.Pieces[t] > 0) parts.Add(PuzzleStore.PieceLabel(t) + s.Pieces[t] + "個");
        string head = s.spent > 0 ? IgCurrency.Format(s.spent) + T_SUMMARY_SPENT_B : "";
        return parts.Count > 0 ? head + string.Join("・", parts) + T_SUMMARY_GOT_B : head + T_SUMMARY_NONE;
    }
}
