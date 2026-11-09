using System;
using System.Collections;
using System.Text;
using Cysharp.Threading.Tasks;
using Firebase.Firestore;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 管理者用: お客様に見せるQRコードの一覧（新ホームで管理者アカウントにだけ出る QR ボタンから開く全画面）。
/// 上のタブで種類を切り替え、マスを選ぶとそのQRを白地で大きく表示する。お客様のアプリのカメラで読み取ってもらう。
///  ・クーポン   … 店頭クーポン4種（所持品と同じ並び）。中身は従来の店頭QRと同じ文字（CallMethodFromQR.QR_*_COUPON）。
///                 読み取るたびに1枚もらえ、受け取った側の新ホームが「○○を1枚入手しました」と知らせる
///  ・惑星の解放 … SpacePlanets.QrPayload。惑星を選ぶ画面と同じ並び・同じマス（最初から持っているルミナのマスは空き）
///  ・1か月パス  … 1か月遊び放題パス（平日のみ・土日のみ・ALL DAY）の受け取りQR。カードを選ぶたびに
///                 1回だけ使えるQRをその場で発行する（StorePasses.IssueAsync。発行から60分間有効）。
///                 受け取られたら「✓ 受け取りました（名前）」。受け取られずに閉じたらそのQRは取り消す
/// 誤タップで消えないよう、閉じるのはボタンだけ（旧ホームのイベントガチャQRの表示と同じ）。
///
/// 表示の出し分けは AdminClaim（IDトークンの admin クレーム）。QRの中身はアプリ内で作れるので、
/// この画面は「見せる」ための道具（書き込みは読み取った側のアカウントに対して行われる）。
/// </summary>
public class SpaceAdminQrList : MonoBehaviour
{
    private const string T_CAPTION = "ADMIN ONLY";
    private const string T_TITLE = "お客様に見せるQRコード";
    private static readonly string[] T_TABS = { "入店", "クーポン", "ゲーム", "惑星の解放", StorePasses.T_SHORT };
    private static readonly string[] T_NOTES =
    {
        "お店の入口などに出しておきます（毎日変わります）",
        "お客様に読み取ってもらうと、1枚もらえます",
        "遊んだゲームを選んで見せると、アチーブメント達成になり星座が灯ります",
        "条件を達成したお客様に見せて、読み取ってもらいます",
        "購入されたお客様に、種類を選んで受け取りQRを見せます",
    };
    private const string T_PASS_TAP = "タップすると、このパスの受け取りQRを発行します";
    private const string T_PASS_ISSUING = "受け取りQRを発行しています…";
    private const string T_PASS_ISSUE_FAILED = "受け取りQRを発行できませんでした";
    private const string T_SCAN_PASS_B = "」パスを受け取れます（今日から30日間）";
    private const string T_SCAN_PASS_SUB = "1回だけ・発行から60分間使えます。閉じると、このQRコードは使えなくなります";
    private const string T_PASS_WAITING = "受け取り待ち…";
    private const string T_PASS_DONE = "✓ 受け取りました";
    private const string T_PASS_GONE = "このQRコードは取り消されました";
    private const string T_SCAN_A = "このQRコードを読み取ると「";
    private const string T_SCAN_COUPON_B = "」が1枚もらえます";
    private const string T_SCAN_COUPON_SUB = "読み取るたびに1枚ずつ増えます。渡し終えたら閉じてください";
    private const string T_SCAN_PLANET_B = "」が解放されます";
    private const string T_BACK = "一覧にもどる";
    private const string T_CHECKIN_EN = "CHECK IN";
    private const string T_CHECKIN_NAME = "入店・退店";
    private const string T_CHECKIN_CELL = "入店・退店のQRコード";
    private const string T_CHECKIN_CELL_SUB = "読み取ると入店、入店中なら退店になります";
    private const string T_CHECKIN_SCAN = "このQRコードを読み取ると、入店（入店中なら退店）になります";
    private const string T_CHECKIN_SUB = "毎日、朝4時に変わります。表示中は画面が消えません";
    private const string T_CHECKIN_DAY_B = " の入店QRコードです";
    private const string T_GAME_SEARCH = "遊んだゲームを検索…";
    private const string T_GAME_EN = "ACHIEVEMENT";
    private const string T_GAME_SCAN = "このQRコードを読み取ると、このゲームのアチーブメントを達成します";
    private const string T_GAME_SUB = "今日だけ使えます。同じ卓の人は全員読み取れます";

    /// <summary>この画面で表示しうる文字（惑星の名前は SpacePlanets.GlyphText）</summary>
    public static string GlyphText
    {
        get
        {
            var sb = new StringBuilder();
            sb.Append(T_CAPTION).Append(T_TITLE).Append(T_SCAN_A).Append(T_SCAN_COUPON_B)
              .Append(T_SCAN_COUPON_SUB).Append(T_SCAN_PLANET_B).Append(T_BACK).Append(T_PASS_TAP)
              .Append(T_PASS_ISSUING).Append(T_PASS_ISSUE_FAILED).Append(T_SCAN_PASS_B).Append(T_SCAN_PASS_SUB)
              .Append(T_PASS_WAITING).Append(T_PASS_DONE).Append(T_PASS_GONE).Append("（）さん")
              .Append(T_CHECKIN_EN).Append(T_CHECKIN_NAME).Append(T_CHECKIN_CELL).Append(T_CHECKIN_CELL_SUB)
              .Append(T_CHECKIN_SCAN).Append(T_CHECKIN_SUB).Append(T_CHECKIN_DAY_B).Append(StoreCalendar.GlyphText)
              .Append(T_GAME_SEARCH).Append(T_GAME_EN).Append(T_GAME_SCAN).Append(T_GAME_SUB);
            foreach (var t in T_TABS) sb.Append(t);
            foreach (var t in T_NOTES) sb.Append(t);
            foreach (var c in SpaceCoupons.All) sb.Append(c.Name).Append(c.RowDesc).Append(c.En);
            return sb.ToString();
        }
    }

    private const int TAB_CHECKIN = 0, TAB_COUPON = 1, TAB_GAME = 2, TAB_PLANET = 3, TAB_PASS = 4;

    // 惑星は4列（惑星を選ぶ画面と同じマス）。クーポンは同じ幅に2列で大きく
    private const int COLS = 4;
    private const float CELL_W = 212f, CELL_H = 228f, GAP = 18f;
    private const float GRID_W = COLS * CELL_W + (COLS - 1) * GAP;
    private const float COUPON_W = (GRID_W - GAP) * 0.5f, COUPON_H = 404f, COUPON_ICON = 240f;
    private const float THUMB = 168f;
    private const float TAB_W = 192f, TAB_H = 84f, TAB_PAD = 7f;
    private const float LIST_TOP = 330f, LIST_BOTTOM = 56f, PAD_TOP = 20f, PAD_BOTTOM = 40f, PAD_SIDE = 40f;
    private const float QR_CARD = 760f, QR_IMAGE = 680f;

    /// <summary>最後に開いていたタブ（アプリを起動しているあいだ覚えておく。最初は入店）</summary>
    private static int s_tab = TAB_CHECKIN;

    private CanvasGroup _group;
    private RectTransform _safe;
    private readonly GameObject[] _pages = new GameObject[5];
    private readonly GameObject[] _tabOn = new GameObject[5];
    private readonly TextMeshProUGUI[] _tabLabels = new TextMeshProUGUI[5];
    private TextMeshProUGUI _note;
    private GameObject _detail;
    private Texture2D _qrTex;
    private Action _onClosed;
    private bool _closing;

    // 1か月パスの受け取りQR（表示中のもの）
    private string _passId;           // 受け取られずに閉じたら取り消す
    private bool _passClaimed;
    private ListenerRegistration _passWatch;
    private TextMeshProUGUI _passStatus;
    private int _detailSerial;        // QRの表示を開け閉めするたびに増やす（発行している間に閉じられたかの判定）

    public static SpaceAdminQrList Open(Canvas canvas, Action onClosed)
    {
        var root = SpaceTheme.Rect("__AdminQrList", canvas.transform);
        SpaceTheme.Stretch(root);
        root.SetAsLastSibling();
        SpaceTheme.SubCanvas(root.gameObject, true);
        var list = root.gameObject.AddComponent<SpaceAdminQrList>();
        list._onClosed = onClosed;
        list.Build();
        return list;
    }

    /// <summary>戻る操作: QRを表示中なら一覧へ、一覧なら閉じる</summary>
    public bool HandleBack()
    {
        if (_detail != null) CloseDetail();
        else Close();
        return true;
    }

    public void Close()
    {
        if (_closing) return;
        _closing = true;
        StartCoroutine(FadeOutAndDestroy());
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
        SpaceTheme.Img(dim, null, new Color(0.01f, 0.015f, 0.05f, 0.96f), true);

        _safe = SpaceTheme.Rect("Safe", rt);
        SpaceTheme.Stretch(_safe);
        _safe.gameObject.AddComponent<SpaceSafeArea>();

        var caption = SpaceTheme.Text(_safe, "Caption", T_CAPTION, 24, SpaceTheme.A(SpaceTheme.GOLD, 0.9f),
            new Vector2(0.5f, 1f), new Vector2(0f, -70f), new Vector2(700f, 36f), FontStyles.Bold);
        SpaceTheme.Tracking(caption, 18f);
        SpaceTheme.Text(_safe, "Title", T_TITLE, 44, SpaceTheme.TEXT, new Vector2(0.5f, 1f), new Vector2(0f, -124f),
            new Vector2(800f, 60f), FontStyles.Bold);
        BuildTabs();
        _note = SpaceTheme.Text(_safe, "Note", "", 26, SpaceTheme.TEXT_SUB, new Vector2(0.5f, 1f),
            new Vector2(0f, -290f), new Vector2(960f, 40f), FontStyles.Bold);
        CloseButton(_safe, Close);

        SelectTab(s_tab);
        StartCoroutine(SpaceTheme.Tween(0.25f, 0f, k => _group.alpha = k));
    }

    /// <summary>種類の切り替え（金色の地が選択中）</summary>
    private void BuildTabs()
    {
        int count = T_TABS.Length;
        var bar = SpaceTheme.Rect("Tabs", _safe, new Vector2(0.5f, 1f), new Vector2(0f, -210f),
            new Vector2(TAB_W * count + TAB_PAD * 2f, TAB_H + TAB_PAD * 2f));
        SpaceTheme.Glass(bar, TAB_H * 0.5f + TAB_PAD - 1f, new Color(0.05f, 0.07f, 0.19f, 0.9f));
        for (int i = 0; i < count; i++)
        {
            int index = i;
            var seg = SpaceTheme.Rect("Tab" + i, bar, SpaceTheme.CENTER,
                new Vector2((i - (count - 1) * 0.5f) * TAB_W, 0f), new Vector2(TAB_W, TAB_H));
            var hit = SpaceTheme.HitArea(seg);
            var on = SpaceTheme.Rect("On", seg);
            SpaceTheme.Stretch(on);
            SpaceTheme.Rounded(on, SpaceTheme.GOLD, TAB_H * 0.5f - 1f);
            _tabOn[i] = on.gameObject;
            var label = SpaceTheme.Text(seg, "Label", T_TABS[i], 30, SpaceTheme.TEXT_SUB, FontStyles.Bold);
            label.enableAutoSizing = true;
            label.fontSizeMin = 22f;
            label.fontSizeMax = 30f;
            SpaceTheme.Stretch(label.rectTransform);
            _tabLabels[i] = label;
            SpaceTheme.Clickable(seg.gameObject, hit, () => SelectTab(index));
        }
    }

    /// <summary>タブを切り替える。一覧は初めて開いたときに作る</summary>
    private void SelectTab(int index)
    {
        if (_detail != null || _closing) return;
        s_tab = Mathf.Clamp(index, 0, _pages.Length - 1);
        for (int i = 0; i < _pages.Length; i++)
        {
            bool on = i == s_tab;
            _tabOn[i].SetActive(on);
            _tabLabels[i].color = on ? SpaceTheme.TEXT_DARK : SpaceTheme.TEXT_SUB;
            if (on && _pages[i] == null)
                _pages[i] = i == TAB_CHECKIN ? BuildCheckinPage()
                    : i == TAB_COUPON ? BuildCouponPage()
                    : i == TAB_GAME ? BuildGamePage()
                    : i == TAB_PLANET ? BuildPlanetPage()
                    : BuildPassPage();
            if (_pages[i] != null) _pages[i].SetActive(on);
        }
        _note.text = T_NOTES[s_tab];
    }

    /// <summary>タブの下いっぱいの縦スクロールの枠を作り、中身を置く Content を返す</summary>
    private RectTransform BuildScroll(string name, float contentH, out GameObject page)
    {
        var viewport = SpaceTheme.Rect(name, _safe);
        viewport.anchorMin = new Vector2(0.5f, 0f);
        viewport.anchorMax = new Vector2(0.5f, 1f);
        viewport.pivot = SpaceTheme.CENTER;
        viewport.offsetMin = new Vector2(-(GRID_W * 0.5f + PAD_SIDE), LIST_BOTTOM);
        viewport.offsetMax = new Vector2(GRID_W * 0.5f + PAD_SIDE, -LIST_TOP);
        SpaceTheme.HitArea(viewport);
        viewport.gameObject.AddComponent<RectMask2D>().softness = new Vector2Int(0, 28);
        var content = SpaceTheme.Rect("Content", viewport, new Vector2(0.5f, 1f), Vector2.zero,
            new Vector2(GRID_W, contentH), new Vector2(0.5f, 1f));
        var scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.scrollSensitivity = 40f;
        page = viewport.gameObject;
        return content;
    }

    // ---------------------------------------------------------------- 入店
    /// <summary>
    /// 入店・退店のQR（1日1回変わる）。タブを押したらそのまま大きく出す（2026-11-06 ユーザー指定。
    /// 以前はマスを1回押してから表示していた）。営業日が変わったら（朝4時）作り直し、表示中は画面を消さない
    /// </summary>
    private GameObject BuildCheckinPage()
    {
        var page = SpaceTheme.Rect("CheckinPage", _safe);
        SpaceTheme.Stretch(page);
        float top = LIST_TOP + 24f;

        var day = StoreCheckinQr.Today;
        var cardRt = SpaceTheme.Rect("QrCard", page, new Vector2(0.5f, 1f), new Vector2(0f, -(top + QR_CARD * 0.5f)),
            new Vector2(QR_CARD, QR_CARD));
        var glow = SpaceTheme.Rect("Glow", page, new Vector2(0.5f, 1f), cardRt.anchoredPosition,
            new Vector2(QR_CARD + 220f, QR_CARD + 220f));
        glow.SetSiblingIndex(cardRt.GetSiblingIndex());
        SpaceTheme.Img(glow, SpaceSprites.Glow, SpaceTheme.A(SpaceTheme.CYAN, 0.30f));
        SpaceTheme.Rounded(cardRt, Color.white, 44f);
        var qr = SpaceTheme.Rect("Qr", cardRt, SpaceTheme.CENTER, Vector2.zero, new Vector2(QR_IMAGE, QR_IMAGE));
        var raw = qr.gameObject.AddComponent<RawImage>();
        raw.raycastTarget = false;
        var tex = QRCodeHelper.CreateQRCode(StoreCheckinQr.Payload(day), 512, 512);
        tex.filterMode = FilterMode.Point; // ドットがにじまないよう拡大は最近傍
        raw.texture = tex;

        float y = top + QR_CARD + 50f;
        var dayLabel = SpaceTheme.Text(page, "Day", "", 34, SpaceTheme.CYAN, new Vector2(0.5f, 1f), new Vector2(0f, -y),
            new Vector2(960f, 48f), FontStyles.Bold);
        var scan = SpaceTheme.Text(page, "ScanNote", T_CHECKIN_SCAN, 28, SpaceTheme.TEXT_SUB, new Vector2(0.5f, 1f),
            new Vector2(0f, -(y + 58f)), new Vector2(960f, 44f), FontStyles.Bold);
        scan.enableAutoSizing = true;
        scan.fontSizeMin = 20f;
        scan.fontSizeMax = 28f;
        var sub = SpaceTheme.Text(page, "ScanSub", T_CHECKIN_SUB, 24, SpaceTheme.TEXT_MUTED, new Vector2(0.5f, 1f),
            new Vector2(0f, -(y + 104f)), new Vector2(960f, 36f), FontStyles.Bold);
        sub.enableAutoSizing = true;
        sub.fontSizeMin = 18f;
        sub.fontSizeMax = 24f;

        var updater = page.gameObject.AddComponent<CheckinQrUpdater>();
        updater.Init(raw, dayLabel, day, T_CHECKIN_DAY_B, tex);
        page.gameObject.AddComponent<SpacePopIn>().Target = cardRt;
        return page.gameObject;
    }

    // ---------------------------------------------------------------- クーポン
    private GameObject BuildCouponPage()
    {
        var all = SpaceCoupons.All;
        int rows = Mathf.CeilToInt(all.Length / 2f);
        float contentH = PAD_TOP + rows * COUPON_H + (rows - 1) * GAP + PAD_BOTTOM;
        var content = BuildScroll("CouponList", contentH, out var page);
        for (int i = 0; i < all.Length; i++) BuildCouponCell(content, all[i], i);
        return page;
    }

    private void BuildCouponCell(RectTransform content, SpaceCouponDef def, int index)
    {
        var pos = new Vector2(COUPON_W * 0.5f + (index % 2) * (COUPON_W + GAP),
            -(PAD_TOP + COUPON_H * 0.5f + (index / 2) * (COUPON_H + GAP)));
        var rt = SpaceTheme.Rect("Coupon_" + def.Key, content, new Vector2(0f, 1f), pos, new Vector2(COUPON_W, COUPON_H));
        var face = SpaceTheme.Glass(rt, 40f, new Color(0.07f, 0.09f, 0.24f, 0.78f), null, true);

        var iconPos = new Vector2(0f, 50f);
        var glow = SpaceTheme.Rect("Glow", rt, SpaceTheme.CENTER, iconPos,
            new Vector2(COUPON_ICON + 130f, COUPON_ICON + 130f));
        SpaceTheme.Img(glow, SpaceSprites.Glow, SpaceTheme.A(def.Accent, 0.30f));
        SpaceInventoryPanel.RoundedIcon(rt, def.Icon(), SpaceTheme.CENTER, iconPos, COUPON_ICON, 50f);

        SpaceTheme.Text(rt, "Name", def.Name, 34, SpaceTheme.TEXT, SpaceTheme.CENTER,
            new Vector2(0f, -COUPON_H * 0.5f + 92f), new Vector2(COUPON_W - 24f, 46f), FontStyles.Bold);
        SpaceTheme.Text(rt, "Desc", def.RowDesc, 24, SpaceTheme.TEXT_SUB, SpaceTheme.CENTER,
            new Vector2(0f, -COUPON_H * 0.5f + 48f), new Vector2(COUPON_W - 24f, 34f), FontStyles.Bold);
        QrMark(rt, 36f, 30f);

        SpaceTheme.Clickable(rt.gameObject, face, () => ShowCoupon(def));
    }

    // ---------------------------------------------------------------- ゲーム（公認）
    /// <summary>検索欄＋ゲームの一覧（仮想スクロール）。選ぶとそのゲームの今日の公認QRを出す</summary>
    private GameObject BuildGamePage()
    {
        var page = SpaceTheme.Rect("GamePage", _safe);
        page.anchorMin = new Vector2(0.5f, 0f);
        page.anchorMax = new Vector2(0.5f, 1f);
        page.pivot = SpaceTheme.CENTER;
        page.offsetMin = new Vector2(-GRID_W * 0.5f, LIST_BOTTOM);
        page.offsetMax = new Vector2(GRID_W * 0.5f, -LIST_TOP);
        var input = SpaceRecruitForm.SearchInput(page, new Vector2(0f, 0f), new Vector2(GRID_W, 96f), T_GAME_SEARCH);

        // 行の数は大きめの高さで用意し（画面の高さは機種で違う）、枠は検索欄の下いっぱいに広げる
        const float listH = 1900f;
        var games = BoardGameCatalog.Load()?.games ?? Array.Empty<BoardGameEntry>();
        var byTitle = new System.Collections.Generic.Dictionary<string, BoardGameEntry>();
        foreach (var g in games)
            if (!string.IsNullOrEmpty(g.title) && !byTitle.ContainsKey(g.title)) byTitle[g.title] = g;
        var picker = TitlePickerList.Create(page, Vector2.zero, GRID_W, listH,
            SpaceTheme.Font, t => { if (byTitle.TryGetValue(t, out var g)) ShowGame(g); },
            new Color(0.09f, 0.12f, 0.30f, 0.55f), SpaceTheme.TEXT);
        var pickerRt = (RectTransform)picker.transform;
        pickerRt.anchorMin = Vector2.zero;
        pickerRt.anchorMax = Vector2.one;
        pickerRt.pivot = SpaceTheme.CENTER;
        pickerRt.offsetMin = Vector2.zero;
        pickerRt.offsetMax = new Vector2(0f, -120f);
        var all = new System.Collections.Generic.List<string>(byTitle.Keys);
        all.Sort(StringComparer.Ordinal);
        picker.PrewarmAsync(all);

        void Refresh(string q)
        {
            q = (q ?? "").Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(q))
            {
                picker.SetItems(all);
                return;
            }
            var hit = new System.Collections.Generic.List<string>();
            foreach (var kv in byTitle)
            {
                var g = kv.Value;
                if (kv.Key.ToLowerInvariant().Contains(q)
                    || (g.title_en != null && g.title_en.ToLowerInvariant().Contains(q)))
                    hit.Add(kv.Key);
            }
            hit.Sort(StringComparer.Ordinal);
            picker.SetItems(hit);
        }
        input.onValueChanged.AddListener(Refresh);
        Refresh("");
        return page.gameObject;
    }

    private void ShowGame(BoardGameEntry g)
    {
        var safe = OpenDetail();
        if (safe == null) return;
        var color = SpaceTheme.GOLD;
        var icon = SpaceTheme.Rect("Icon", safe, new Vector2(0.5f, 1f), new Vector2(0f, -150f), new Vector2(150f, 150f));
        SpaceTheme.Img(icon, SpaceSprites.Glow, SpaceTheme.A(color, 0.6f));
        var core = SpaceTheme.Rect("Core", icon, SpaceTheme.CENTER, Vector2.zero, new Vector2(70f, 70f));
        SpaceTheme.Img(core, SpaceSprites.Sparkle, Color.white);
        FillDetail(safe, T_GAME_EN, color, SpaceTheme.NoParse(SpaceTheme.SafeNow(g.title)), BoardGameCertify.Payload(g),
            T_GAME_SCAN, T_GAME_SUB);
        var name = safe.Find("Name") as RectTransform;
        var tmp = name != null ? name.GetComponent<TextMeshProUGUI>() : null;
        if (tmp != null)
        {
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 30f;
            tmp.fontSizeMax = 56f;
        }
    }

    // ---------------------------------------------------------------- 惑星
    private GameObject BuildPlanetPage()
    {
        // 惑星を選ぶ画面と同じ並びにして、同じ惑星が同じマスに来るようにする
        var all = SpacePlanets.All;
        int rows = Mathf.CeilToInt(all.Length / (float)COLS);
        float contentH = PAD_TOP + rows * CELL_H + (rows - 1) * GAP + PAD_BOTTOM;
        var content = BuildScroll("PlanetList", contentH, out var page);
        // ルミナも、最初にほかの色を選んだ人は持っていないので解放QRを出す（2026-11-10。以前は空けていた）
        for (int i = 0; i < all.Length; i++) BuildPlanetCell(content, all[i], i);
        return page;
    }

    /// <summary>惑星の一覧の index 番目のマスの位置（惑星を選ぶ画面と同じ順に、左上から4列で並べる）</summary>
    private static Vector2 CellPos(int index)
    {
        int col = index % COLS, row = index / COLS;
        return new Vector2(CELL_W * 0.5f + col * (CELL_W + GAP), -(PAD_TOP + CELL_H * 0.5f + row * (CELL_H + GAP)));
    }

    private void BuildPlanetCell(RectTransform content, SpacePlanetDef def, int index)
    {
        var rt = SpaceTheme.Rect("Cell_" + def.Id, content, new Vector2(0f, 1f), CellPos(index),
            new Vector2(CELL_W, CELL_H));
        var face = SpaceTheme.Glass(rt, 34f, new Color(0.07f, 0.09f, 0.24f, 0.78f), null, true);

        var pos = new Vector2(0f, 26f);
        if (def.IsOrb)
        {
            // ルミナ（光の玉）は画像を使わずにその場で描く（惑星を選ぶ画面と同じ）
            var view = SpacePlanetView.Create(rt, def, THUMB, SpacePlanetView.Quality.Thumb, false);
            ((RectTransform)view.transform).anchoredPosition = pos;
        }
        else
        {
            float d = THUMB * SpacePlanetView.SPHERE_RATIO * def.Scale;
            var halo = SpaceTheme.Rect("Halo", rt, SpaceTheme.CENTER, pos, new Vector2(d * def.HaloScale, d * def.HaloScale));
            SpaceTheme.Img(halo, SpaceSprites.Glow, SpaceTheme.A(def.Accent, def.HaloAlpha));
            float side = THUMB * SpacePlanetView.SPHERE_RATIO / SpacePlanets.THUMB_SPHERE_FRAC;
            var thumb = SpaceTheme.Rect("Thumb", rt, SpaceTheme.CENTER, pos, new Vector2(side, side));
            var raw = thumb.gameObject.AddComponent<RawImage>();
            raw.raycastTarget = false;
            SetThumb(raw, def).Forget();
        }

        SpaceTheme.Text(rt, "Name", def.Name, 26, SpaceTheme.TEXT, SpaceTheme.CENTER, new Vector2(0f, -CELL_H * 0.5f + 34f),
            new Vector2(CELL_W - 12f, 36f), FontStyles.Bold);
        QrMark(rt, 30f, 24f);

        SpaceTheme.Clickable(rt.gameObject, face, () => ShowPlanet(def));
    }

    private static async UniTaskVoid SetThumb(RawImage raw, SpacePlanetDef def)
    {
        var tex = SpacePlanets.Thumb(def);
        if (tex == null)
        {
            raw.enabled = false;
            await SpacePlanets.Load(def, false);
            tex = SpacePlanets.Thumb(def);
        }
        if (raw == null || tex == null) return;
        raw.texture = tex;
        raw.enabled = true;
    }

    // ---------------------------------------------------------------- 1か月パス
    /// <summary>3種類のカード（見本。今日受け取った場合の日付）を縦に並べる。タップで受け取りQRを発行</summary>
    private GameObject BuildPassPage()
    {
        var types = StorePasses.Types;
        float cardH = GRID_W / SpacePassCard.ASPECT;
        float itemH = cardH + 70f; // カード＋下の説明
        float contentH = PAD_TOP + 20f + types.Length * itemH + (types.Length - 1) * GAP * 2f + PAD_BOTTOM;
        var content = BuildScroll("PassList", contentH, out var page);
        for (int i = 0; i < types.Length; i++)
        {
            var type = types[i];
            float top = PAD_TOP + 20f + i * (itemH + GAP * 2f);
            var holder = SpaceTheme.Rect("Pass_" + type.Id, content, new Vector2(0.5f, 1f),
                new Vector2(0f, -(top + cardH * 0.5f)), new Vector2(GRID_W, cardH));
            var hit = SpaceTheme.HitArea(holder);
            SpacePassCard.Create(holder, type, null, GRID_W, true, false);
            SpaceTheme.Clickable(holder.gameObject, hit, () => ShowPass(type));
            SpaceTheme.Text(content, "Tap", T_PASS_TAP, 26, SpaceTheme.TEXT_SUB, new Vector2(0.5f, 1f),
                new Vector2(0f, -(top + cardH + 36f)), new Vector2(GRID_W, 40f), FontStyles.Bold);
        }
        return page;
    }

    /// <summary>マスの右上の小さなQRの印（押すとQRが出ることがわかるように）</summary>
    private static void QrMark(RectTransform cell, float size, float inset)
    {
        var mark = SpaceTheme.Rect("Qr", cell, new Vector2(1f, 1f), new Vector2(-inset, -inset), new Vector2(size, size));
        SpaceTheme.Img(mark, SpaceSprites.QrIcon, SpaceTheme.A(SpaceTheme.GOLD, 0.85f));
    }

    // ================================================================
    // QRの表示
    // ================================================================
    private void ShowCoupon(SpaceCouponDef def)
    {
        var safe = OpenDetail();
        if (safe == null) return;

        // クーポンの絵（お客様にもどのクーポンか伝わるように）
        var pos = new Vector2(0f, -150f);
        var glow = SpaceTheme.Rect("IconGlow", safe, new Vector2(0.5f, 1f), pos, new Vector2(300f, 300f));
        SpaceTheme.Img(glow, SpaceSprites.Glow, SpaceTheme.A(def.Accent, 0.35f));
        SpaceInventoryPanel.RoundedIcon(safe, def.Icon(), new Vector2(0.5f, 1f), pos, 160f, 34f);

        FillDetail(safe, def.En, def.Accent, def.Name, def.QrText, T_SCAN_A + def.Name + T_SCAN_COUPON_B,
            T_SCAN_COUPON_SUB);
    }

    private void ShowPlanet(SpacePlanetDef def)
    {
        var safe = OpenDetail();
        if (safe == null) return;

        // 惑星（小さく）
        var planet = SpaceTheme.Rect("Planet", safe, new Vector2(0.5f, 1f), new Vector2(0f, -150f), new Vector2(1f, 1f));
        if (def.IsOrb)
        {
            // ルミナ（光の玉）はその場で描く（球の直径 150）
            SpacePlanetView.Create(planet, def, 150f / SpacePlanetView.SPHERE_RATIO, SpacePlanetView.Quality.Thumb, false);
        }
        else
        {
            float d = 150f * def.Scale;
            var halo = SpaceTheme.Rect("Halo", planet, SpaceTheme.CENTER, Vector2.zero,
                new Vector2(d * def.HaloScale, d * def.HaloScale));
            SpaceTheme.Img(halo, SpaceSprites.Glow, SpaceTheme.A(def.Accent, def.HaloAlpha));
            var thumb = SpaceTheme.Rect("Thumb", planet, SpaceTheme.CENTER, Vector2.zero,
                new Vector2(150f / SpacePlanets.THUMB_SPHERE_FRAC, 150f / SpacePlanets.THUMB_SPHERE_FRAC));
            var raw = thumb.gameObject.AddComponent<RawImage>();
            raw.raycastTarget = false;
            SetThumb(raw, def).Forget();
        }

        FillDetail(safe, def.En, def.Accent, def.Name, SpacePlanets.QrPayload(def),
            T_SCAN_A + def.Name + T_SCAN_PLANET_B, null);
    }

    private void ShowPass(PassTypeDef type) => ShowPassAsync(type).Forget();

    /// <summary>
    /// 1か月パスの受け取りQR: その場で1枚発行してから表示する（1回だけ・60分間有効）。
    /// 受け取られたら下に「✓ 受け取りました（名前）」と出してQRを薄くする
    /// </summary>
    private async UniTaskVoid ShowPassAsync(PassTypeDef type)
    {
        var safe = OpenDetail();
        if (safe == null) return;
        int serial = _detailSerial;

        var stage = SpaceTheme.Rect("Pass", safe, new Vector2(0.5f, 1f), new Vector2(0f, -150f),
            new Vector2(240f, 240f / SpacePassCard.ASPECT));
        SpacePassCard.Create(stage, type, null, 240f, false, false);
        var issuing = SpaceTheme.Text(safe, "Issuing", T_PASS_ISSUING, 30, SpaceTheme.TEXT_SUB, new Vector2(0.5f, 1f),
            new Vector2(0f, -640f), new Vector2(900f, 44f), FontStyles.Bold);

        string id;
        try
        {
            id = await StorePasses.IssueAsync(type);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[AdminQr] 受け取りQRの発行に失敗: {ex.Message}");
            if (this == null || serial != _detailSerial) return;
            SpaceTheme.Toast(T_PASS_ISSUE_FAILED);
            CloseDetail();
            return;
        }
        if (this == null || _closing || serial != _detailSerial)
        {
            StorePasses.CancelAsync(id).Forget(); // 発行している間に閉じられた
            return;
        }

        Destroy(issuing.gameObject);
        _passId = id;
        _passClaimed = false;
        FillDetail(safe, type.En, SpacePassCard.GlowColor(type), StorePasses.T_SHORT + "（" + type.Label + "）",
            StorePasses.QR_PREFIX + id, T_SCAN_A + type.Label + T_SCAN_PASS_B, T_SCAN_PASS_SUB);
        _passStatus = SpaceTheme.Text(safe, "PassStatus", T_PASS_WAITING, 34, SpaceTheme.TEXT_SUB, new Vector2(0.5f, 1f),
            new Vector2(0f, -(372f + QR_CARD + 52f + 46f + 66f)), new Vector2(960f, 50f), FontStyles.Bold);
        _passStatus.enableAutoSizing = true;
        _passStatus.fontSizeMin = 24f;
        _passStatus.fontSizeMax = 34f;
        _passWatch = StorePasses.Watch(id, OnPassChanged);
    }

    /// <summary>表示中の受け取りQRの様子が変わった（受け取られた・取り消された）</summary>
    private void OnPassChanged(PassInfo pass)
    {
        if (this == null || _passStatus == null || _passClaimed) return;
        if (pass == null)
        {
            _passStatus.text = T_PASS_GONE;
            _passStatus.color = SpaceTheme.TEXT_MUTED;
            return;
        }
        if (!pass.Claimed) return;
        _passClaimed = true;
        string who = string.IsNullOrEmpty(pass.HolderName)
            ? ""
            : "（" + SpaceTheme.NoParse(SpaceTheme.SafeNow(pass.HolderName)) + "さん）";
        _passStatus.text = T_PASS_DONE + who;
        _passStatus.color = SpaceTheme.MINT;
        // QRは使い終わったので薄くする
        var qrCard = _detail != null ? _detail.transform.Find("Safe/QrCard") : null;
        if (qrCard != null) qrCard.gameObject.AddComponent<CanvasGroup>().alpha = 0.25f;
        AssetsDatabase.instance?.PlayLevelUpSE();
    }

    /// <summary>表示中の受け取りQRを片づける（受け取られていなければ取り消す）</summary>
    private void EndPass()
    {
        _passWatch?.Stop();
        _passWatch = null;
        if (_passId != null && !_passClaimed) StorePasses.CancelAsync(_passId).Forget();
        _passId = null;
        _passClaimed = false;
        _passStatus = null;
    }

    /// <summary>QR表示の土台（暗幕・セーフエリア・閉じるボタン）を作り、中身を置く Safe を返す</summary>
    private RectTransform OpenDetail()
    {
        if (_closing) return null;
        CloseDetail();

        var root = SpaceTheme.Rect("Detail", transform);
        SpaceTheme.Stretch(root);
        _detail = root.gameObject;
        var dim = SpaceTheme.Rect("Dim", root);
        SpaceTheme.Stretch(dim);
        SpaceTheme.Img(dim, null, new Color(0.01f, 0.015f, 0.05f, 0.98f), true); // 下の一覧を押させない

        var safe = SpaceTheme.Rect("Safe", root);
        SpaceTheme.Stretch(safe);
        safe.gameObject.AddComponent<SpaceSafeArea>();
        CloseButton(safe, CloseDetail);
        return safe;
    }

    /// <summary>名前・QRコード（白地の角丸カードに大きく）・説明・「一覧にもどる」</summary>
    private void FillDetail(RectTransform safe, string en, Color accent, string name, string payload, string note,
        string subNote)
    {
        var enText = SpaceTheme.Text(safe, "En", en, 26, SpaceTheme.A(accent, 0.95f), new Vector2(0.5f, 1f),
            new Vector2(0f, -258f), new Vector2(900f, 36f), FontStyles.Bold);
        SpaceTheme.Tracking(enText, 24f);
        SpaceTheme.Text(safe, "Name", name, 56, SpaceTheme.TEXT, new Vector2(0.5f, 1f), new Vector2(0f, -310f),
            new Vector2(900f, 72f), FontStyles.Bold);

        // QRコード（ドットがにじまないよう拡大は最近傍）
        var cardRt = SpaceTheme.Rect("QrCard", safe, new Vector2(0.5f, 1f), new Vector2(0f, -(372f + QR_CARD * 0.5f)),
            new Vector2(QR_CARD, QR_CARD));
        var glow = SpaceTheme.Rect("Glow", safe, new Vector2(0.5f, 1f), cardRt.anchoredPosition,
            new Vector2(QR_CARD + 220f, QR_CARD + 220f));
        glow.SetSiblingIndex(cardRt.GetSiblingIndex());
        SpaceTheme.Img(glow, SpaceSprites.Glow, SpaceTheme.A(accent, 0.30f));
        SpaceTheme.Rounded(cardRt, Color.white, 44f);
        var qr = SpaceTheme.Rect("Qr", cardRt, SpaceTheme.CENTER, Vector2.zero, new Vector2(QR_IMAGE, QR_IMAGE));
        var qrRaw = qr.gameObject.AddComponent<RawImage>();
        qrRaw.raycastTarget = false;
        _qrTex = QRCodeHelper.CreateQRCode(payload, 512, 512);
        _qrTex.filterMode = FilterMode.Point;
        qrRaw.texture = _qrTex;

        float y = 372f + QR_CARD + 52f;
        var noteText = SpaceTheme.Text(safe, "ScanNote", note, 28, SpaceTheme.TEXT_SUB, new Vector2(0.5f, 1f),
            new Vector2(0f, -y), new Vector2(960f, 44f), FontStyles.Bold);
        noteText.enableAutoSizing = true;
        noteText.fontSizeMin = 20f;
        noteText.fontSizeMax = 28f;
        if (!string.IsNullOrEmpty(subNote))
        {
            var sub = SpaceTheme.Text(safe, "ScanSub", subNote, 24, SpaceTheme.TEXT_MUTED, new Vector2(0.5f, 1f),
                new Vector2(0f, -(y + 46f)), new Vector2(960f, 36f), FontStyles.Bold);
            sub.enableAutoSizing = true;
            sub.fontSizeMin = 18f;
            sub.fontSizeMax = 24f;
        }

        // 下: 一覧にもどる
        var back = SpaceTheme.Rect("Back", safe, new Vector2(0.5f, 0f), new Vector2(0f, 56f), new Vector2(560f, 112f),
            new Vector2(0.5f, 0f));
        var face = SpaceTheme.Glass(back, 46f, new Color(0.05f, 0.07f, 0.19f, 0.92f), null, true);
        SpaceTheme.Clickable(back.gameObject, face, CloseDetail);
        var label = SpaceTheme.Text(back, "Label", T_BACK, 34, SpaceTheme.TEXT, FontStyles.Bold);
        SpaceTheme.Stretch(label.rectTransform);

        _detail.AddComponent<SpacePopIn>().Target = cardRt;
    }

    private void CloseDetail()
    {
        _detailSerial++;
        EndPass();
        if (_detail != null) Destroy(_detail);
        _detail = null;
        if (_qrTex != null) Destroy(_qrTex);
        _qrTex = null;
    }

    // ================================================================
    // 共通
    // ================================================================
    private static void CloseButton(RectTransform safe, UnityEngine.Events.UnityAction onClick)
    {
        var rt = SpaceTheme.Rect("Close", safe, new Vector2(1f, 1f), new Vector2(-112f, -84f), new Vector2(104f, 104f));
        var face = SpaceTheme.Img(rt, SpaceSprites.Circle, new Color(0.05f, 0.07f, 0.19f, 0.85f), true);
        var rim = SpaceTheme.Rect("Rim", rt);
        SpaceTheme.Stretch(rim);
        SpaceTheme.Img(rim, SpaceSprites.RingSmall, SpaceTheme.STROKE);
        SpaceTheme.Bar(rt, Vector2.zero, 44f, 6f, 45f, SpaceTheme.TEXT);
        SpaceTheme.Bar(rt, Vector2.zero, 44f, 6f, -45f, SpaceTheme.TEXT);
        SpaceTheme.Clickable(rt.gameObject, face, onClick);
    }

    private IEnumerator FadeOutAndDestroy()
    {
        float from = _group.alpha;
        yield return SpaceTheme.Tween(0.25f, 0f, k => _group.alpha = from * (1f - k));
        Destroy(gameObject);
    }

    private void OnDestroy()
    {
        EndPass();
        if (_qrTex != null) Destroy(_qrTex);
        var cb = _onClosed;
        _onClosed = null;
        cb?.Invoke();
    }
}

/// <summary>
/// 管理者の画面の入店・退店のQR: 営業日が変わったら（朝4時）QRを作り直し、何日のQRかを出す。
/// 表示しているあいだは画面が消えないようにする（お店の端末に出しておくため）
/// </summary>
public class CheckinQrUpdater : MonoBehaviour
{
    private RawImage _raw;
    private TextMeshProUGUI _label;
    private System.DateTime _day;
    private string _suffix;
    private Texture2D _tex; // 自分で作り直したもの（最初の1枚は一覧の画面が片づける）
    private float _nextCheck;
    private int _sleep;

    /// <param name="ownedTexture">最初のQRの画像をこの部品に片づけさせるとき（入店タブにそのまま出す場合）</param>
    public void Init(RawImage raw, TextMeshProUGUI label, System.DateTime day, string suffix, Texture2D ownedTexture = null)
    {
        _raw = raw;
        _label = label;
        _day = day;
        _suffix = suffix;
        _tex = ownedTexture;
        if (_label != null) _label.text = StoreCalendar.MonthDayW(_day) + _suffix;
    }

    // 表示しているあいだだけ画面を消さない（ほかのタブに切り替えたら元に戻す）
    private bool _awake;

    private void OnEnable()
    {
        if (_awake) return;
        _awake = true;
        _sleep = Screen.sleepTimeout;
        Screen.sleepTimeout = SleepTimeout.NeverSleep;
    }

    private void OnDisable()
    {
        if (!_awake) return;
        _awake = false;
        Screen.sleepTimeout = _sleep;
    }

    private void Update()
    {
        if (Time.unscaledTime < _nextCheck) return;
        _nextCheck = Time.unscaledTime + 1f;
        var day = StoreCheckinQr.Today;
        if (day == _day || _raw == null) return;
        _day = day;
        var old = _raw.texture;
        var tex = QRCodeHelper.CreateQRCode(StoreCheckinQr.Payload(day), 512, 512);
        tex.filterMode = FilterMode.Point;
        _raw.texture = tex;
        if (old != null) Destroy(old);
        _tex = tex;
        if (_label != null) _label.text = StoreCalendar.MonthDayW(_day) + _suffix;
    }

    private void OnDestroy()
    {
        if (_tex != null) Destroy(_tex);
    }
}
