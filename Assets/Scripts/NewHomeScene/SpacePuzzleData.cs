using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Cysharp.Threading.Tasks;
using Firebase.Firestore;
using UnityEngine;

/// <summary>
/// ピースパズル（2026-11-13 ユーザー指定）の保存・書き込みと、表示に使う情報。決まりは Commons/PiecePuzzle.cs（PuzzleRules）。
/// ピース座が完成すると（管理者は初めから）遊べる。画面は SpacePuzzle、ピースはショップで買う（SpaceShop）。
/// 保存先は users/{uid}.puzzle（盤面）と users/{uid}.pieces（持っているピース）。本人が書けるのでルールの変更はなし。
/// 置くのは users を1回読むトランザクションで、持っているピースを1つ減らし、囲んだボーナスマスの物をまとめて足す。
/// </summary>
public static class PuzzleStore
{
    /// <summary>パズルが遊べるようになる星座（ピース座＝パズル）</summary>
    public const string CONSTELLATION = "piece";
    public const string FIELD = "puzzle";
    public const string FIELD_PIECES = "pieces";
    /// <summary>履歴の種類（SpaceHistoryPanel）</summary>
    public const string HISTORY = "puzzle";

    public const string T_LOCKED = "ピース座を完成させると遊べます";
    public const string T_ERR_USER = "ユーザー情報を読み込めませんでした";
    public const string T_ERR_NETWORK = "通信に失敗しました。もう一度お試しください";
    public const string T_ERR_MONTH = "月が変わって、盤面が新しくなりました";
    public const string T_ERR_NO_PIECE = "このピースを持っていません";
    public const string T_ERR_CHANGED = "そこには置けません。盤面が変わりました";
    public const string T_PIECE_A = "ピース（";
    public const string T_PIECE_B = "）";
    private const string T_HISTORY_PLACE_B = "）を置いた";
    private const string T_HISTORY_BONUS_A = "ボーナスマスを囲んで ";
    private const string T_HISTORY_BONUS_B = " を手に入れた";

    public static string GlyphText
    {
        get
        {
            var sb = new StringBuilder()
                .Append(T_LOCKED).Append(T_ERR_USER).Append(T_ERR_NETWORK).Append(T_ERR_MONTH).Append(T_ERR_NO_PIECE)
                .Append(T_ERR_CHANGED).Append(T_PIECE_A).Append(T_PIECE_B);
            foreach (var p in PuzzleRules.Pieces) sb.Append(p.Name);
            foreach (var r in AllRewards) sb.Append(RewardName(r));
            return sb.Append(IgCurrency.GlyphText).ToString();
        }
    }

    /// <summary>ボーナスマスでもらえる物の全部（表示の文字の登録用）</summary>
    private static IEnumerable<string> AllRewards =>
        new[] { PuzzleRules.REWARD_DRINK, PuzzleRules.REWARD_FIVE, PuzzleRules.REWARD_GOLD, PuzzleRules.REWARD_POTION }
            .Concat(PuzzleRules.REWARD_MEEPLES);

    private static UserData Data => UserDataManager.instance != null ? UserDataManager.instance.UserData : null;

    /// <summary>遊べるか（ピース座が完成。管理者は初めから）</summary>
    public static bool Unlocked
    {
        get
        {
            if (AdminClaim.IsAdmin) return true;
            var p = Constellations.Progress().Find(x => x.Def.Id == CONSTELLATION);
            return p != null && p.Complete;
        }
    }

    /// <summary>今の営業日の月</summary>
    public static string MonthKey => CardRules.MonthKey(StoreCalendar.TodayBusiness);

    /// <summary>今の盤面（表示用の写し。月が変わっていれば新しい盤面）</summary>
    public static PuzzleState Current
    {
        get
        {
            var s = PuzzleState.From(Data?.PuzzleMap);
            PuzzleRules.Normalize(s, MonthKey);
            return s;
        }
    }

    /// <summary>持っているピースの数（添字 1〜8）</summary>
    public static int[] Owned => PuzzleRules.PiecesFrom(Data?.PiecesMap);

    public static int OwnedTotal => Owned.Sum();

    /// <summary>
    /// 手元の持っているピースに足す（2026-11-13。コインプッシャーで獲得したとき。サーバーへは CoinPusher.FlushAsync がまとめて送る）
    /// </summary>
    public static void AddPiecesLocal(int type, int n)
    {
        var d = Data;
        if (d == null || type < 1 || type > PuzzleRules.TYPES || n == 0) return;
        var owned = PuzzleRules.PiecesFrom(d.PiecesMap);
        owned[type] = Math.Max(0, owned[type] + n);
        d.PiecesMap = PuzzleRules.PiecesToMap(owned);
    }

    /// <summary>盤面・持っているピースが変わったかどうかを見る文字</summary>
    public static string Key
    {
        get
        {
            var s = Current;
            return s.Month + ":" + string.Join(",", s.Types) + ":" + string.Join(",", s.Claimed) + ":" + string.Join(",", Owned);
        }
    }

    /// <summary>「ピース（L字）」</summary>
    public static string PieceLabel(int type) => T_PIECE_A + (PuzzleRules.Piece(type)?.Name ?? "") + T_PIECE_B;

    // ================================================================
    // ボーナスマスでもらえる物（表示）
    // ================================================================
    private static SpaceMeepleDef MeepleOf(string field)
    {
        foreach (var m in SpaceMeeples.All)
            if (m.Field == field) return m;
        return SpaceMeeples.Black.Field == field ? SpaceMeeples.Black : null;
    }

    /// <summary>物の名前（「ドリンクチケット」「赤コマ」。G は「G」）</summary>
    public static string RewardName(string field)
    {
        if (field == PuzzleRules.REWARD_GOLD) return IgCurrency.UNIT;
        var coupon = SpaceCoupons.ByField(field);
        if (coupon != null) return coupon.Name;
        var meeple = MeepleOf(field);
        if (meeple != null) return meeple.Name;
        if (field == SpacePlanetPotion.FIELD) return SpacePlanetPotion.NAME;
        return field ?? "";
    }

    /// <summary>物と数の書き方（「ドリンクチケット」「5G」「赤コマ ×2」）</summary>
    public static string RewardText(PuzzleBonus b)
    {
        if (b == null) return "";
        if (b.Reward == PuzzleRules.REWARD_GOLD) return IgCurrency.Format(b.Count);
        return RewardName(b.Reward) + (b.Count > 1 ? " ×" + b.Count : "");
    }

    /// <summary>物の絵（チケットは角の四角い絵なので、使う側で角を丸める＝RewardIsCoupon）</summary>
    public static Sprite RewardIcon(string field)
    {
        if (field == PuzzleRules.REWARD_GOLD) return IgCurrency.Icon;
        var coupon = SpaceCoupons.ByField(field);
        if (coupon != null) return coupon.Icon();
        var meeple = MeepleOf(field);
        if (meeple != null) return meeple.Icon;
        if (field == SpacePlanetPotion.FIELD) return SpacePlanetPotion.Icon;
        return null;
    }

    public static bool RewardIsCoupon(string field) => SpaceCoupons.ByField(field) != null;

    /// <summary>物の色（ボーナスマスの光・縁）</summary>
    public static Color RewardTint(string field)
    {
        if (field == PuzzleRules.REWARD_GOLD) return SpaceTheme.GOLD;
        var coupon = SpaceCoupons.ByField(field);
        if (coupon != null) return coupon.Accent;
        var meeple = MeepleOf(field);
        if (meeple != null) return meeple.Tint;
        if (field == SpacePlanetPotion.FIELD) return SpacePlanetPotion.TINT;
        return SpaceTheme.GOLD;
    }

    /// <summary>手元の UserData に物を足す（G 以外。G は書き込んだ所持金をそのまま入れる）</summary>
    private static void AddLocal(UserData d, string field, int n)
    {
        var coupon = SpaceCoupons.ByField(field);
        if (coupon != null)
        {
            coupon.Add(d, n);
            return;
        }
        var meeple = MeepleOf(field);
        if (meeple != null)
        {
            meeple.Add(d, n);
            return;
        }
        if (field == SpacePlanetPotion.FIELD) d.PlanetPotion += n;
    }

    // ================================================================
    // 置く（トランザクション）
    // ================================================================
    public sealed class PlaceResult
    {
        public bool Ok;
        public string Error;
        public List<PuzzleBonus> Got = new List<PuzzleBonus>(); // 囲んでもらったボーナスマス
        public int Balance;                                      // 置いたあとの所持金（G をもらったとき）
    }

    /// <summary>
    /// 種類 type のピースをマス cells に置く。shownMonth は表示していた盤面の月（月が変わっていたら置かずに T_ERR_MONTH）。
    /// 持っているピースを1つ減らし、置いたことで囲まれたボーナスマスの物を足す（チケット・コマ・惑星回復薬は数を増やし、G は所持金に足す）
    /// </summary>
    public static async UniTask<PlaceResult> PlaceAsync(int type, List<int> cells, string shownMonth)
    {
        var m = UserDataManager.instance;
        if (m == null || string.IsNullOrEmpty(m.UID) || m.UserData == null || cells == null)
            return new PlaceResult { Error = T_ERR_USER };
        if (!Unlocked) return new PlaceResult { Error = T_LOCKED };
        string month = MonthKey; // トランザクションは裏のスレッドで動くので先に読んでおく
        if (month != shownMonth) return new PlaceResult { Error = T_ERR_MONTH };
        var target = new List<int>(cells);
        var db = FirebaseFirestore.DefaultInstance;
        var doc = db.Collection("users").Document(m.UID);
        Dictionary<string, object> puzzleMap = null, piecesMap = null;
        PlaceResult res;
        try
        {
            res = await db.RunTransactionAsync(tx => tx.GetSnapshotAsync(doc).ContinueWith(t =>
            {
                var r = new PlaceResult();
                puzzleMap = null;
                piecesMap = null;
                var u = t.Result.Exists ? t.Result.ToDictionary() : null;
                if (u == null)
                {
                    r.Error = T_ERR_USER;
                    return r;
                }
                var s = PuzzleState.From(u.TryGetValue(FIELD, out var raw) ? raw : null);
                PuzzleRules.Normalize(s, month);
                var owned = PuzzleRules.PiecesFrom(u.TryGetValue(FIELD_PIECES, out var piecesRaw) ? piecesRaw : null);
                if (type < 1 || type > PuzzleRules.TYPES || owned[type] < 1)
                {
                    // ほかの端末で使った: 置かずに手元の数をそろえる
                    r.Error = T_ERR_NO_PIECE;
                    piecesMap = PuzzleRules.PiecesToMap(owned);
                    return r;
                }
                if (!PuzzleRules.MatchesShape(type, target) || !PuzzleRules.CanPlace(s, target))
                {
                    // ほかの端末で置いた: 置かずに手元の盤面をそろえる
                    r.Error = T_ERR_CHANGED;
                    puzzleMap = s.ToMap();
                    return r;
                }
                r.Got = PuzzleRules.Place(s, type, target);
                owned[type]--;
                puzzleMap = s.ToMap();
                piecesMap = PuzzleRules.PiecesToMap(owned);
                var updates = new Dictionary<string, object>
                {
                    { FIELD, puzzleMap },
                    { FIELD_PIECES, piecesMap },
                    { "lastDate", Timestamp.GetCurrentTimestamp() },
                };
                long gold = 0;
                var inc = new Dictionary<string, long>();
                foreach (var b in r.Got)
                {
                    if (b.Reward == PuzzleRules.REWARD_GOLD) gold += b.Count;
                    else inc[b.Reward] = (inc.TryGetValue(b.Reward, out long n) ? n : 0) + b.Count;
                }
                long balance = Math.Max(0, Long(u, IgCurrency.FIELD));
                if (gold > 0) updates[IgCurrency.FIELD] = balance + gold;
                foreach (var kv in inc) updates[kv.Key] = FieldValue.Increment(kv.Value);
                r.Balance = (int)(balance + gold);
                tx.Update(doc, updates);
                r.Ok = true;
                return r;
            })).AsUniTask();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[PuzzleStore] ピースを置けませんでした ({type}): {ex.Message}");
            return new PlaceResult { Error = T_ERR_NETWORK };
        }

        var d = m.UserData;
        if (puzzleMap != null) d.PuzzleMap = puzzleMap;
        if (piecesMap != null) d.PiecesMap = piecesMap;
        if (res == null || !res.Ok) return res ?? new PlaceResult { Error = T_ERR_NETWORK };
        if (res.Got.Any(b => b.Reward == PuzzleRules.REWARD_GOLD)) d.Ig = res.Balance;
        foreach (var b in res.Got)
            if (b.Reward != PuzzleRules.REWARD_GOLD) AddLocal(d, b.Reward, b.Count);
        LocalHistoryLog.Add(HISTORY, T_PIECE_A + (PuzzleRules.Piece(type)?.Name ?? "") + T_HISTORY_PLACE_B);
        foreach (var b in res.Got) LocalHistoryLog.Add(HISTORY, T_HISTORY_BONUS_A + RewardText(b) + T_HISTORY_BONUS_B);
        JourneyService.NotifyChanged();
        return res;
    }

    private static long Long(Dictionary<string, object> m, string k)
    {
        if (m == null || !m.TryGetValue(k, out var v) || v == null) return 0;
        try { return Convert.ToInt64(v); }
        catch { return 0; }
    }
}

/// <summary>
/// ピースの色と絵（2026-11-13）。絵はその場で描く（ショップのカード・所持品・お知らせ・パズルの手持ちの欄で使う）。
/// 盤面のピースは同じ色で SpacePuzzle が UI の四角で描く
/// </summary>
public static class PuzzleArt
{
    // 種類ごとの色（添字 1〜8。U字＝レアは金色）
    private static readonly Color[] COLORS =
    {
        Color.white,
        SpaceTheme.Hex(0x5CD9FF), SpaceTheme.Hex(0x6EF0B0), SpaceTheme.Hex(0xFFA04D), SpaceTheme.Hex(0xFFD84A),
        SpaceTheme.Hex(0xB98CFF), SpaceTheme.Hex(0x5B8DFF), SpaceTheme.Hex(0xFF7FB0), SpaceTheme.Hex(0xFFE58A),
    };

    public static Color ColorOf(int type) => type >= 1 && type < COLORS.Length ? COLORS[type] : SpaceTheme.GOLD;

    private static readonly Dictionary<int, Sprite> _icons = new Dictionary<int, Sprite>();
    private const int SIZE = 192;

    /// <summary>ピースの絵（type 0 はランダム＝色の違う4マスときらめき）</summary>
    public static Sprite Icon(int type)
    {
        if (_icons.TryGetValue(type, out var cached) && cached != null) return cached;
        var cells = new List<(int X, int Y, Color C)>();
        bool sparkle = false;
        if (type == 0)
        {
            cells.Add((0, 0, ColorOf(3)));
            cells.Add((1, 0, ColorOf(5)));
            cells.Add((0, 1, ColorOf(1)));
            cells.Add((1, 1, ColorOf(8)));
            sparkle = true;
        }
        else
        {
            var def = PuzzleRules.Piece(type);
            if (def == null) return null;
            foreach (var (x, y) in def.Cells) cells.Add((x, y, ColorOf(type)));
            sparkle = def.Rare;
        }
        var sprite = Render(cells, sparkle, "PieceIcon" + type);
        _icons[type] = sprite;
        return sprite;
    }

    /// <summary>マスを角の丸い四角で描く（上が明るい色・ふちは少し濃い色）。sparkle なら右上にきらめき</summary>
    private static Sprite Render(List<(int X, int Y, Color C)> cells, bool sparkle, string name)
    {
        int gw = cells.Max(c => c.X) + 1, gh = cells.Max(c => c.Y) + 1;
        const float MARGIN = 14f;
        float cell = (SIZE - MARGIN * 2f) / Mathf.Max(3, Mathf.Max(gw, gh));
        float ox = (SIZE - gw * cell) * 0.5f, oy = (SIZE - gh * cell) * 0.5f;
        float gap = cell * 0.07f, radius = cell * 0.2f, border = Mathf.Max(2f, cell * 0.06f);
        var px = new Color32[SIZE * SIZE];
        foreach (var (cx, cy, color) in cells)
        {
            float x0 = ox + cx * cell + gap, y0 = oy + cy * cell + gap, w = cell - gap * 2f;
            float hx = w * 0.5f, centerX = x0 + hx, centerY = y0 + hx;
            var top = Color.Lerp(color, Color.white, 0.3f);
            var bottom = Color.Lerp(color, Color.black, 0.12f);
            var edge = Color.Lerp(color, Color.black, 0.35f);
            int ix0 = Mathf.Max(0, Mathf.FloorToInt(x0) - 1), ix1 = Mathf.Min(SIZE - 1, Mathf.CeilToInt(x0 + w) + 1);
            int iy0 = Mathf.Max(0, Mathf.FloorToInt(y0) - 1), iy1 = Mathf.Min(SIZE - 1, Mathf.CeilToInt(y0 + w) + 1);
            for (int py = iy0; py <= iy1; py++)
                for (int pxi = ix0; pxi <= ix1; pxi++)
                {
                    // 角の丸い四角までの距離（中は負）
                    float qx = Mathf.Abs(pxi + 0.5f - centerX) - (hx - radius);
                    float qy = Mathf.Abs(py + 0.5f - centerY) - (hx - radius);
                    float outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude;
                    float d = outside + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
                    float a = Mathf.Clamp01(0.5f - d);
                    if (a <= 0f) continue;
                    float t = Mathf.Clamp01((py + 0.5f - y0) / w);
                    var c = Color.Lerp(top, bottom, t);
                    if (d > -border) c = Color.Lerp(c, edge, Mathf.Clamp01((d + border) / border));
                    Blend(px, pxi, py, c, a);
                }
        }
        if (sparkle) Sparkle(px, SIZE * 0.80f, SIZE * 0.18f, SIZE * 0.16f);

        var tex = new Texture2D(SIZE, SIZE, TextureFormat.RGBA32, false)
        {
            name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear,
        };
        tex.SetPixels32(px);
        tex.Apply(false, true);
        return Sprite.Create(tex, new Rect(0, 0, SIZE, SIZE), new Vector2(0.5f, 0.5f), 100f);
    }

    /// <summary>白い十字のきらめき（中心 cx,cy は上からの座標、r は腕の長さ）</summary>
    private static void Sparkle(Color32[] px, float cx, float cy, float r)
    {
        int x0 = Mathf.Max(0, (int)(cx - r)), x1 = Mathf.Min(SIZE - 1, (int)(cx + r));
        int y0 = Mathf.Max(0, (int)(cy - r)), y1 = Mathf.Min(SIZE - 1, (int)(cy + r));
        float thin = r * 0.16f;
        for (int py = y0; py <= y1; py++)
            for (int x = x0; x <= x1; x++)
            {
                float dx = Mathf.Abs(x + 0.5f - cx), dy = Mathf.Abs(py + 0.5f - cy);
                // 横と縦の細いひし形を重ねる
                float h = 1f - (dx / r + dy / thin);
                float v = 1f - (dy / r + dx / thin);
                float a = Mathf.Clamp01(Mathf.Max(h, v) * 2.2f);
                if (a > 0f) Blend(px, x, py, Color.white, a);
            }
    }

    /// <summary>上からの座標 (x, y) に色を重ねる（テクスチャの行は下から数えるので上下を返す）</summary>
    private static void Blend(Color32[] px, int x, int y, Color c, float a)
    {
        int i = (SIZE - 1 - y) * SIZE + x;
        Color under = px[i];
        float outA = a + under.a * (1f - a);
        if (outA <= 0f) return;
        var rgb = (c * a + under * under.a * (1f - a)) / outA;
        px[i] = new Color(rgb.r, rgb.g, rgb.b, outA);
    }
}
