using System;
using System.Collections;
using System.Globalization;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 星座の画面（全画面）。お店に公認されたボードゲームで灯る16の星座（Constellations）を2列で並べる。
/// 星座を押すと大きく出して、灯った星に入っているゲームの名前を出す。
/// 開く場所: ボードゲーム図鑑の上の「星座」、公認のお知らせの「星座を見る」。
/// Android の戻るは NewHomeController が Instance.HandleBack を呼ぶ（図鑑より手前）。
/// </summary>
public class SpaceConstellationPage : MonoBehaviour
{
    private const string T_CAPTION = "CONSTELLATIONS";
    private const string T_TITLE = "星座";
    private const string T_HINT = "星の数（6〜8つ）だけアチーブメントを達成すると星座が完成。さらに同じ数ごとにレベルが上がり、もっと輝きます。どちらもチケット1枚（職人座は7%OFF、ほかはドリンク）";
    private const string T_CERT = "アチーブメント";
    private const string T_WORKS = "作品";
    private const string T_STARS = "星";
    private const string T_BEST = "最高";
    private const string T_DONE = "完成";
    private const string T_DONE_BIG = "完成！";
    private const string T_LEFT_A = "あと ";
    private const string T_LEFT_B = " 作品で完成";
    private const string T_GENRE_A = "「";
    private const string T_GENRE_B = "」のゲームでアチーブメントを達成すると、星が1つ灯ります";
    private const string T_EMPTY_STAR = "まだ灯っていない星";
    private const string T_CLOSE = "とじる";
    private const string T_SEE_GAMES = "このジャンルのゲームを見る";
    private const string T_REWARD_A = "（";
    private const string T_REWARD_B = "1枚）";
    private const string T_LV = "Lv.";
    private const string T_NEXT_A = "あと ";
    private const string T_NEXT_B = " 作品で ";
    private const string T_TOTAL_A = "これまでに達成したアチーブメント ";
    private const string T_ROUND = "いまの星";

    public static string GlyphText => new StringBuilder()
        .Append(T_CAPTION).Append(T_TITLE).Append(T_HINT).Append(T_CERT).Append(T_WORKS).Append(T_STARS).Append(T_BEST)
        .Append(T_DONE).Append(T_DONE_BIG).Append(T_LEFT_A).Append(T_LEFT_B).Append(T_GENRE_A).Append(T_GENRE_B)
        .Append(T_EMPTY_STAR).Append(T_CLOSE).Append(T_SEE_GAMES).Append(T_REWARD_A).Append(T_REWARD_B).Append(T_LV).Append(T_NEXT_A).Append(T_NEXT_B)
        .Append("ドリンクチケット7%OFFチケット").Append(T_TOTAL_A).Append(T_ROUND).Append("0123456789/ 　・月日")
        .Append(Constellations.GlyphText).ToString();

    private const float SIDE = 40f;
    private const int COLS = 2;
    private const float CELL_W = 470f, CELL_H = 470f, GAP = 24f;
    private const float GRID_W = COLS * CELL_W + (COLS - 1) * GAP;
    private const float LIST_TOP = 356f, PAD = 20f;
    private const float FIGURE = 290f;

    /// <summary>開いている画面（なければ null）</summary>
    public static SpaceConstellationPage Instance { get; private set; }

    private CanvasGroup _group;
    private RectTransform _safe;
    private GameObject _detail;
    private bool _closing;

    public static SpaceConstellationPage Open(Canvas canvas = null)
    {
        if (Instance != null) return Instance;
        if (canvas == null) canvas = SpaceTheme.MainCanvas();
        if (canvas == null) return null;
        var root = SpaceTheme.Rect("__SpaceConstellations", canvas.transform);
        SpaceTheme.Stretch(root);
        root.SetAsLastSibling();
        SpaceTheme.SubCanvas(root.gameObject, true); // 星の明滅をホームの再描画と分ける
        var page = root.gameObject.AddComponent<SpaceConstellationPage>();
        Instance = page;
        page.Build();
        return page;
    }

    /// <summary>
    /// その星座の詳細だけを開く（ホームの空の星座をタップしたとき。2026-11-09）。
    /// 詳細を閉じたら一覧は見せずにそのまま閉じる
    /// </summary>
    public static void OpenDetail(string id)
    {
        if (Instance != null) return; // もう星座の画面を開いている
        var p = Constellations.Progress().Find(x => x.Def.Id == id);
        if (p == null) return;
        var page = Open();
        if (page == null) return;
        page._detailOnly = true;
        // 一覧（背景・星座のマス）は隠して、ホームの上に詳細だけを出す
        foreach (Transform c in page.transform) c.gameObject.SetActive(false);
        page.ShowDetail(p);
    }

    private bool _detailOnly;

    public void HandleBack()
    {
        if (_detail != null) CloseDetail();
        else Close();
    }

    public void Close()
    {
        if (_closing) return;
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

    // ================================================================
    // 一覧
    // ================================================================
    private void Build()
    {
        var rt = (RectTransform)transform;
        _group = gameObject.AddComponent<CanvasGroup>();
        _group.alpha = 0f;

        var dim = SpaceTheme.Rect("Dim", rt);
        SpaceTheme.Stretch(dim);
        SpaceTheme.Img(dim, null, new Color(0.005f, 0.008f, 0.035f, 0.98f), true);
        var glow = SpaceTheme.Rect("Glow", rt, new Vector2(0.5f, 1f), new Vector2(0f, -200f), new Vector2(1600f, 1100f));
        SpaceTheme.Img(glow, SpaceSprites.Glow, SpaceTheme.A(SpaceTheme.VIOLET, 0.10f));
        Sky(rt);

        _safe = SpaceTheme.Rect("Safe", rt);
        SpaceTheme.Stretch(_safe);
        _safe.gameObject.AddComponent<SpaceSafeArea>();

        var caption = SpaceTheme.Text(_safe, "Caption", T_CAPTION, 24, SpaceTheme.A(SpaceTheme.GOLD, 0.9f),
            new Vector2(0f, 1f), new Vector2(SIDE + 16f, -70f), new Vector2(600f, 36f), FontStyles.Bold,
            TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
        SpaceTheme.Tracking(caption, 18f);
        SpaceTheme.Text(_safe, "Title", T_TITLE, 52, SpaceTheme.TEXT, new Vector2(0f, 1f), new Vector2(SIDE + 16f, -126f),
            new Vector2(600f, 66f), FontStyles.Bold, TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
        CloseButton(_safe, -84f, Close);

        var all = Constellations.Progress();
        int done = 0, best = 0;
        foreach (var p in all)
        {
            if (p.Complete) done++;
            best = Mathf.Max(best, p.Level);
        }
        string gold = "#" + ColorUtility.ToHtmlStringRGB(SpaceTheme.GOLD);
        var summary = SpaceTheme.Text(_safe, "Summary",
            $"{T_CERT} <color={gold}>{BoardGameCertify.Count}</color>　　{T_DONE} <color={gold}>{done}</color> / {all.Count}"
            + (best >= 2 ? $"　　{T_BEST} <color={gold}>{T_LV}{best}</color>" : ""),
            32, SpaceTheme.TEXT, new Vector2(0.5f, 1f), new Vector2(0f, -212f), new Vector2(GRID_W, 48f), FontStyles.Bold);
        summary.richText = true;
        // 2行まで折り返す（長いときは小さくする）
        var hint = SpaceTheme.Text(_safe, "Hint", T_HINT, 24, SpaceTheme.TEXT_SUB, new Vector2(0.5f, 1f),
            new Vector2(0f, -290f), new Vector2(GRID_W, 72f), FontStyles.Bold);
        hint.textWrappingMode = TextWrappingModes.Normal;
        hint.enableAutoSizing = true;
        hint.fontSizeMin = 18f;
        hint.fontSizeMax = 24f;

        // 2列の縦スクロール
        var viewport = SpaceTheme.Rect("List", _safe);
        viewport.anchorMin = new Vector2(0.5f, 0f);
        viewport.anchorMax = new Vector2(0.5f, 1f);
        viewport.pivot = SpaceTheme.CENTER;
        viewport.offsetMin = new Vector2(-(GRID_W * 0.5f + SIDE), 24f);
        viewport.offsetMax = new Vector2(GRID_W * 0.5f + SIDE, -LIST_TOP);
        SpaceTheme.HitArea(viewport);
        viewport.gameObject.AddComponent<RectMask2D>().softness = new Vector2Int(0, 28);
        int rows = Mathf.CeilToInt(all.Count / (float)COLS);
        var content = SpaceTheme.Rect("Content", viewport, new Vector2(0.5f, 1f), Vector2.zero,
            new Vector2(GRID_W, PAD * 2f + rows * CELL_H + (rows - 1) * GAP + 40f), new Vector2(0.5f, 1f));
        var scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.scrollSensitivity = 40f;

        for (int i = 0; i < all.Count; i++) BuildCell(content, all[i], i);

        StartCoroutine(SpaceTheme.Tween(0.25f, 0f, k => _group.alpha = k));
    }

    private void BuildCell(RectTransform content, ConstellationProgress p, int index)
    {
        var def = p.Def;
        var pos = new Vector2(CELL_W * 0.5f + (index % COLS) * (CELL_W + GAP),
            -(PAD + CELL_H * 0.5f + (index / COLS) * (CELL_H + GAP)));
        var rt = SpaceTheme.Rect("Cell_" + def.Id, content, new Vector2(0f, 1f), pos, new Vector2(CELL_W, CELL_H));
        var face = SpaceTheme.Glass(rt, 40f, new Color(0.05f, 0.07f, 0.20f, 0.72f),
            p.Complete ? SpaceTheme.A(def.Color, 0.75f) : null, true);

        var en = SpaceTheme.Text(rt, "En", def.En, 20, SpaceTheme.A(def.Color, p.Count > 0 ? 0.9f : 0.45f),
            new Vector2(0.5f, 1f), new Vector2(0f, -40f), new Vector2(CELL_W - 40f, 30f), FontStyles.Bold);
        SpaceTheme.Tracking(en, 14f);
        SpaceTheme.Text(rt, "Name", def.Name, 38, p.Count > 0 ? SpaceTheme.TEXT : SpaceTheme.TEXT_SUB, new Vector2(0.5f, 1f),
            new Vector2(0f, -80f), new Vector2(CELL_W - 40f, 50f), FontStyles.Bold);

        var fig = SpaceTheme.Rect("Figure", rt, SpaceTheme.CENTER, new Vector2(0f, -14f), new Vector2(FIGURE, FIGURE));
        Figure(fig, def, p.Count, FIGURE);

        // 完成前「3 / 6」、完成後「★ Lv.2　1 / 6」（次のレベルまで）
        string foot = !p.Complete ? $"{p.Partial} / {def.Size}"
            : $"★ {T_LV}{p.Level}　<size=24><color={SpaceTheme.SUB_HEX}>{p.Partial} / {def.Size}</color></size>";
        SpaceTheme.Text(rt, "Count", foot, 30, p.Complete ? SpaceTheme.GOLD : SpaceTheme.TEXT_SUB, new Vector2(0.5f, 0f),
            new Vector2(0f, 46f), new Vector2(CELL_W - 40f, 44f), FontStyles.Bold).richText = true;

        SpaceTheme.Clickable(rt.gameObject, face, () => ShowDetail(p));
    }

    // ================================================================
    // 詳細
    // ================================================================
    private void ShowDetail(ConstellationProgress p)
    {
        CloseDetail();
        var def = p.Def;
        var root = SpaceTheme.Rect("Detail", transform);
        SpaceTheme.Stretch(root);
        _detail = root.gameObject;
        var dim = SpaceTheme.Rect("Dim", root);
        SpaceTheme.Stretch(dim);
        var dimImg = SpaceTheme.Img(dim, null, SpaceTheme.DIM, true);
        SpaceTheme.Clickable(dim.gameObject, dimImg, CloseDetail, false);

        const float W = 920f, FIG = 520f, ROW = 58f;
        int n = def.Size;
        float listH = n * ROW;
        float h = 120f + 50f + 70f + FIG + 30f + 50f + 60f + 44f + listH + 40f + 120f + 50f;
        var card = SpaceTheme.Rect("Card", root, SpaceTheme.CENTER, Vector2.zero, new Vector2(W, h));
        SpaceTheme.Glass(card, 56f, SpaceTheme.GLASS_DEEP, SpaceTheme.A(def.Color, 0.5f), true);
        var glow = SpaceTheme.Rect("Glow", card, new Vector2(0.5f, 1f), new Vector2(0f, -(240f + FIG * 0.5f)),
            new Vector2(FIG * 1.6f, FIG * 1.6f));
        SpaceTheme.Img(glow, SpaceSprites.Glow,
            SpaceTheme.A(def.Color, p.Complete ? 0.18f + 0.05f * Mathf.Min(p.Level, Constellations.MAX_GLOW_LEVEL) : 0.08f));

        float y = 64f;
        var en = SpaceTheme.Text(card, "En", def.En, 24, SpaceTheme.A(def.Color, 0.95f), new Vector2(0.5f, 1f),
            new Vector2(0f, -y), new Vector2(W - 80f, 34f), FontStyles.Bold);
        SpaceTheme.Tracking(en, 20f);
        y += 56f;
        SpaceTheme.Text(card, "Name", def.Name, 60, SpaceTheme.TEXT, new Vector2(0.5f, 1f), new Vector2(0f, -y),
            new Vector2(W - 80f, 76f), FontStyles.Bold);
        y += 64f;
        var genre = SpaceTheme.Text(card, "Genre", T_GENRE_A + def.GenreLabel + T_GENRE_B, 26, SpaceTheme.TEXT_SUB,
            new Vector2(0.5f, 1f), new Vector2(0f, -y), new Vector2(W - 80f, 40f), FontStyles.Bold);
        genre.enableAutoSizing = true;
        genre.fontSizeMin = 18f;
        genre.fontSizeMax = 26f;
        y += 40f;

        var fig = SpaceTheme.Rect("Figure", card, new Vector2(0.5f, 1f), new Vector2(0f, -(y + FIG * 0.5f)),
            new Vector2(FIG, FIG));
        Figure(fig, def, p.Count, FIG);
        y += FIG + 30f;

        // 完成前「あと 2 作品で完成（ドリンクチケット1枚）」、完成後「Lv.2　あと 4 作品で Lv.3（もっと輝きます）」
        string state = !p.Complete
            ? T_LEFT_A + (n - p.Partial) + T_LEFT_B + T_REWARD_A + SpaceCoupons.NameOf(def.Reward) + T_REWARD_B
            : $"★ {T_LV}{p.Level}　<size=28><color={SpaceTheme.SUB_HEX}>{T_NEXT_A}{n - p.Partial}{T_NEXT_B}{T_LV}{p.Level + 1}{T_REWARD_A}{SpaceCoupons.NameOf(def.Reward)}{T_REWARD_B}</color></size>";
        var stateText = SpaceTheme.Text(card, "State", state, 40, p.Complete ? SpaceTheme.GOLD : SpaceTheme.TEXT, new Vector2(0.5f, 1f),
            new Vector2(0f, -(y + 25f)), new Vector2(W - 80f, 54f), FontStyles.Bold);
        stateText.enableAutoSizing = true;
        stateText.fontSizeMin = 26f;
        stateText.fontSizeMax = 40f;
        stateText.richText = true;
        y += 50f + 30f;

        // いまの1周（6つの星）に入っているゲーム（公認された順）。まだの星は薄い行。完成後はこれまでの作品数も
        string total = p.Complete ? $"{T_TOTAL_A}{p.Count}" : T_ROUND;
        SpaceTheme.Text(card, "Total", total, 24, SpaceTheme.TEXT_MUTED, new Vector2(0f, 1f), new Vector2(84f, -(y + 18f)),
            new Vector2(W - 168f, 36f), FontStyles.Bold, TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
        y += 44f;
        for (int i = 0; i < n; i++)
        {
            float rowY = -(y + ROW * i + ROW * 0.5f);
            int gi = p.RoundStart + i;
            bool on = gi < p.Games.Count;
            var dot = SpaceTheme.Rect("Dot", card, new Vector2(0f, 1f), new Vector2(100f, rowY), new Vector2(30f, 30f));
            SpaceTheme.Img(dot, on ? SpaceSprites.Glow : SpaceSprites.Circle,
                on ? SpaceTheme.A(def.Color, 0.95f) : SpaceTheme.A(SpaceTheme.LINE, 0.18f));
            string label;
            string day = "";
            if (on)
            {
                var g = p.Games[gi];
                label = SpaceTheme.NoParse(SpaceTheme.SafeNow(g.Game?.title ?? g.Key));
                if (DateTime.TryParseExact(g.Day, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None,
                        out var d))
                    day = $"{d.Month}/{d.Day}";
            }
            else label = T_EMPTY_STAR;
            var t = SpaceTheme.Text(card, "Game", label, 30, on ? SpaceTheme.TEXT : SpaceTheme.TEXT_MUTED,
                new Vector2(0f, 1f), new Vector2(140f, rowY), new Vector2(W - 140f - 180f, ROW),
                FontStyles.Bold, TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
            t.overflowMode = TextOverflowModes.Ellipsis;
            if (day.Length > 0)
                SpaceTheme.Text(card, "Day", day, 26, SpaceTheme.TEXT_MUTED, new Vector2(1f, 1f), new Vector2(-70f, rowY),
                    new Vector2(150f, ROW), FontStyles.Bold, TextAlignmentOptions.Right, new Vector2(1f, 0.5f));
        }

        // このジャンルのゲームを見る（図鑑をそのジャンルで絞り込んで開く。2026-11-09）と、とじる
        var see = SpaceTheme.Rect("SeeGames", card, new Vector2(0.5f, 0f), new Vector2(-180f, 50f), new Vector2(460f, 112f),
            new Vector2(0.5f, 0f));
        var seeFace = SpaceTheme.Glass(see, 46f, SpaceTheme.A(def.Color, 0.18f), SpaceTheme.A(def.Color, 0.8f), true);
        SpaceTheme.Clickable(see.gameObject, seeFace, () => SeeGames(def));
        var seeLabel = SpaceTheme.Text(see, "Label", T_SEE_GAMES, 30, def.Color, FontStyles.Bold);
        SpaceTheme.Stretch(seeLabel.rectTransform, 16f, 0f, 16f, 0f);
        seeLabel.enableAutoSizing = true;
        seeLabel.fontSizeMin = 20f;
        seeLabel.fontSizeMax = 30f;

        var close = SpaceTheme.Rect("Close", card, new Vector2(0.5f, 0f), new Vector2(250f, 50f), new Vector2(340f, 112f),
            new Vector2(0.5f, 0f));
        var face = SpaceTheme.Glass(close, 46f, new Color(0.07f, 0.09f, 0.24f, 0.9f), null, true);
        SpaceTheme.Clickable(close.gameObject, face, CloseDetail);
        var label2 = SpaceTheme.Text(close, "Label", T_CLOSE, 36, SpaceTheme.TEXT, FontStyles.Bold);
        SpaceTheme.Stretch(label2.rectTransform);

        root.gameObject.AddComponent<SpacePopIn>().Target = card;
    }

    /// <summary>
    /// その星座のジャンルのゲームを図鑑で見る。図鑑の上から開いていれば、星座の画面を閉じてその図鑑を絞り込む。
    /// ホームから開いていれば（公認のお知らせの「星座を見る」など）、ホームに図鑑を開いてもらう（NewHomeController）
    /// </summary>
    private void SeeGames(ConstellationDef def)
    {
        var zukan = SpaceBoardGameZukan.Instance;
        CloseDetail();
        Close();
        if (zukan != null) zukan.ShowGenre(def);
        else NewHomeController.RequestZukanGenre(def);
    }

    private void CloseDetail()
    {
        if (_detail != null) Destroy(_detail);
        _detail = null;
        if (_detailOnly) Close(); // 空の星座から詳細だけを開いたときは、一覧を見せずに閉じる
    }

    // ================================================================
    // 星座の絵（お知らせからも使う）
    // ================================================================
    /// <summary>
    /// parent（size×size の正方形）の中に星座を描く。count はそのジャンルの公認された作品数。
    ///  ・完成前（0〜5）: count 個の星が灯り、灯った星どうしの線が明るくなる
    ///  ・完成後: 全部の星がレベルの明るさで灯る。次のレベルへ向けて集めた分の星は、ひとつ上の明るさで光る
    /// レベルが上がるほど: 星と光が大きく明るくなり、線が太く白みを帯びる。Lv.2 から星に光の十字、
    /// Lv.3 から外側に回る光の輪、Lv.5 で光の輪が二重になり全体がまたたく（見た目は Lv.5 で頭打ち）
    /// </summary>
    public static void Figure(RectTransform parent, ConstellationDef def, int count, float size)
    {
        int n = def.Size;
        int level = count / n;
        int partial = count % n;
        int gl = Mathf.Min(level, Constellations.MAX_GLOW_LEVEL);
        float half = size * 0.42f;
        Vector2 At(int i) => def.Stars[i] * half;

        if (gl >= 1)
        {
            float auraSize = size * (1.25f + 0.12f * (gl - 1));
            var aura = SpaceTheme.Rect("Aura", parent, SpaceTheme.CENTER, Vector2.zero, new Vector2(auraSize, auraSize));
            var auraImg = SpaceTheme.Img(aura, SpaceSprites.Glow, SpaceTheme.A(def.Color, 0.18f + 0.07f * (gl - 1)));
            if (gl >= 5) aura.gameObject.AddComponent<SpaceTwinkle>().Init(auraImg, auraImg.color.a);
        }
        if (gl >= 3)
        {
            for (int r = 0; r < (gl >= 5 ? 2 : 1); r++)
            {
                float ringSize = size * (1.02f + 0.14f * r);
                var ring = SpaceTheme.Rect("Ring", parent, SpaceTheme.CENTER, Vector2.zero, new Vector2(ringSize, ringSize));
                SpaceTheme.Img(ring, SpaceSprites.RingThin, SpaceTheme.A(Glint(def.Color, gl), 0.22f + 0.06f * (gl - 3)));
                var spark = SpaceTheme.Rect("Spark", ring, SpaceTheme.CENTER, new Vector2(0f, ringSize * 0.5f),
                    new Vector2(size * 0.07f, size * 0.07f));
                SpaceTheme.Img(spark, SpaceSprites.Glow, SpaceTheme.A(Color.white, 0.9f));
                ring.gameObject.AddComponent<SpaceSpinner>().DegreesPerSecond = r == 0 ? -14f : 9f;
            }
        }

        float thick = Mathf.Max(2f, size * 0.011f);
        for (int i = 0; i + 1 < def.Lines.Length; i += 2)
        {
            int a = def.Lines[i], b = def.Lines[i + 1];
            bool on = gl >= 1 || (a < partial && b < partial);
            Vector2 pa = At(a), pb = At(b);
            var d = pb - pa;
            float angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            Color color;
            float w = thick;
            if (gl >= 1)
            {
                color = SpaceTheme.A(Glint(def.Color, gl), 0.85f);
                w = thick * (1f + 0.22f * (gl - 1));
            }
            else color = on ? SpaceTheme.A(SpaceTheme.LINE, 0.55f) : SpaceTheme.A(SpaceTheme.LINE, 0.10f);
            SpaceTheme.Bar(parent, (pa + pb) * 0.5f, d.magnitude, on ? w : thick * 0.7f, angle, color);
        }

        for (int i = 0; i < n; i++)
        {
            var p = At(i);
            // その星の明るさ（0=まだ灯っていない）
            int starLevel = level == 0
                ? (i < partial ? 1 : 0)
                : Mathf.Min(i < partial ? level + 1 : level, Constellations.MAX_GLOW_LEVEL);
            if (starLevel == 0)
            {
                float c0 = Mathf.Max(5f, size * 0.03f);
                var dim = SpaceTheme.Rect("Star", parent, SpaceTheme.CENTER, p, new Vector2(c0, c0));
                SpaceTheme.Img(dim, SpaceSprites.Circle, SpaceTheme.A(SpaceTheme.LINE, 0.28f));
                continue;
            }
            float k = starLevel - 1;
            float g = size * 0.19f * (1f + 0.16f * k);
            var glow = SpaceTheme.Rect("Star", parent, SpaceTheme.CENTER, p, new Vector2(g, g));
            float alpha = Mathf.Min(1f, 0.85f + 0.04f * k);
            var img = SpaceTheme.Img(glow, SpaceSprites.Glow, SpaceTheme.A(Glint(def.Color, starLevel), alpha));
            glow.gameObject.AddComponent<SpaceTwinkle>().Init(img, alpha);
            if (starLevel >= 2)
            {
                float sp = g * (0.9f + 0.12f * (starLevel - 2));
                var cross = SpaceTheme.Rect("Cross", glow, SpaceTheme.CENTER, Vector2.zero, new Vector2(sp, sp));
                var crossImg = SpaceTheme.Img(cross, SpaceSprites.Sparkle, SpaceTheme.A(Color.white, 0.55f + 0.08f * (starLevel - 2)));
                cross.gameObject.AddComponent<SpaceTwinkle>().Init(crossImg, crossImg.color.a);
            }
            float c = Mathf.Max(6f, size * 0.045f * (1f + 0.12f * k));
            var core = SpaceTheme.Rect("Core", glow, SpaceTheme.CENTER, Vector2.zero, new Vector2(c, c));
            SpaceTheme.Img(core, SpaceSprites.Circle, Color.white);
        }
    }

    /// <summary>レベルが上がるほど星座の色に白い輝きを混ぜる</summary>
    private static Color Glint(Color c, int level) =>
        Color.Lerp(c, Color.white, Mathf.Clamp01(0.10f * (level - 1)));

    /// <summary>背景のまばらな星（決まった並び。毎回同じ空になる）</summary>
    private static void Sky(RectTransform rt)
    {
        var rnd = new System.Random(20261106);
        var layer = SpaceTheme.Rect("Sky", rt);
        SpaceTheme.Stretch(layer);
        for (int i = 0; i < 70; i++)
        {
            var pos = new Vector2((float)rnd.NextDouble(), (float)rnd.NextDouble());
            float s = 3f + (float)rnd.NextDouble() * 5f;
            var star = SpaceTheme.Rect("S", layer, pos, Vector2.zero, new Vector2(s, s));
            var img = SpaceTheme.Img(star, SpaceSprites.Circle, SpaceTheme.A(Color.white, 0.15f + (float)rnd.NextDouble() * 0.35f));
            if (i % 3 == 0) star.gameObject.AddComponent<SpaceTwinkle>().Init(img, img.color.a);
        }
    }

    private static void CloseButton(RectTransform safe, float y, UnityEngine.Events.UnityAction onClick)
    {
        var rt = SpaceTheme.Rect("Close", safe, new Vector2(1f, 1f), new Vector2(-(SIDE + 52f), y), new Vector2(104f, 104f));
        var face = SpaceTheme.Img(rt, SpaceSprites.Circle, new Color(0.05f, 0.07f, 0.19f, 0.85f), true);
        var rim = SpaceTheme.Rect("Rim", rt);
        SpaceTheme.Stretch(rim);
        SpaceTheme.Img(rim, SpaceSprites.RingSmall, SpaceTheme.STROKE);
        SpaceTheme.Bar(rt, Vector2.zero, 44f, 6f, 45f, SpaceTheme.TEXT);
        SpaceTheme.Bar(rt, Vector2.zero, 44f, 6f, -45f, SpaceTheme.TEXT);
        SpaceTheme.Clickable(rt.gameObject, face, onClick);
    }
}

/// <summary>星のまたたき（アルファをゆっくり上下させる。位相は星ごとにばらす）</summary>
public class SpaceTwinkle : MonoBehaviour
{
    private Graphic _g;
    private float _base, _phase, _speed;

    public void Init(Graphic g, float baseAlpha)
    {
        _g = g;
        _base = baseAlpha;
        _phase = UnityEngine.Random.value * Mathf.PI * 2f;
        _speed = 1.2f + UnityEngine.Random.value * 1.6f;
    }

    private void Update()
    {
        if (_g == null) return;
        var c = _g.color;
        c.a = _base * (0.6f + 0.4f * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * _speed + _phase)));
        _g.color = c;
    }
}
