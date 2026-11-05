using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Cysharp.Threading.Tasks;
using Firebase.Firestore;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>所持品一覧に並べるクーポン1種類の定義</summary>
public sealed class SpaceCouponDef
{
    public string Key;      // 旧バッグ（ItemBagPageManager）と同じ種別キー
    public string Field;    // users/{uid} のフィールド名
    public string Name;
    public string RowDesc;  // 一覧の1行説明
    public string Desc;     // 使用画面の説明（旧バッグと同じ文言）
    public int Percent;     // 割引クーポンの割引率（割引でなければ0）
    public string En;       // 英字の名前（管理者のQR表示の見出し）
    public string QrText;   // 店頭QRの中身（読むと1枚もらえる。CallMethodFromQR.QR_*_COUPON）
    public Color Accent;    // 管理者のQR表示の光の色（絵の色に合わせる）
    public Func<UserData, int> Get;
    public Action<UserData, int> Add;
    public Func<Sprite> Icon;
}

/// <summary>
/// 新ホームで扱うクーポン。店頭で使う4種だけを並べる
/// （ATK+1チケット・キャラクターチケットは戦闘・キャラクターの再設計中のため一覧に出さない。所持データはそのまま）。
/// </summary>
public static class SpaceCoupons
{
    public static readonly SpaceCouponDef[] All =
    {
        new SpaceCouponDef
        {
            Key = "5", Field = "five_coupon", Name = "5%OFFチケット", Percent = 5,
            En = "5% OFF COUPON", QrText = CallMethodFromQR.QR_FIVE_COUPON, Accent = SpaceTheme.Hex(0x6FA8FF),
            RowDesc = "5%オフ（組み合わせ可）",
            Desc = "5%オフされるチケット。\n組み合わせて最大100%オフまで可能",
            Get = d => d.FiveCoupon, Add = (d, n) => d.FiveCoupon += n,
            Icon = () => AssetsDatabase.instance != null ? AssetsDatabase.instance.FiveCouponSprite : null,
        },
        new SpaceCouponDef
        {
            Key = "7", Field = "seven_coupon", Name = "7%OFFチケット", Percent = 7,
            En = "7% OFF COUPON", QrText = CallMethodFromQR.QR_SEVEN_COUPON, Accent = SpaceTheme.Hex(0xFFB060),
            RowDesc = "7%オフ（組み合わせ可）",
            Desc = "7%オフされるチケット。\n組み合わせて最大100%オフまで可能",
            Get = d => d.SevenCoupon, Add = (d, n) => d.SevenCoupon += n,
            Icon = () => AssetsDatabase.instance != null ? AssetsDatabase.instance.SevenCouponSprite : null,
        },
        new SpaceCouponDef
        {
            Key = "drink", Field = "drink_coupon", Name = "ドリンクチケット",
            En = "DRINK COUPON", QrText = CallMethodFromQR.QR_DRINK_COUPON, Accent = SpaceTheme.Hex(0x5CE1FF),
            RowDesc = "500円以下のドリンクが無料",
            Desc = "500円以下の飲み物の無料チケット。",
            Get = d => d.DrinkCoupon, Add = (d, n) => d.DrinkCoupon += n,
            Icon = () => AssetsDatabase.instance != null ? AssetsDatabase.instance.DrinkCouponSprite : null,
        },
        new SpaceCouponDef
        {
            Key = "coffee", Field = "coffee_coupon", Name = "コーヒーチケット",
            En = "COFFEE COUPON", QrText = CallMethodFromQR.QR_COFFEE_COUPON, Accent = SpaceTheme.Hex(0xFFC08A),
            RowDesc = "コーヒーが無料",
            Desc = "コーヒーの無料チケット。",
            Get = d => d.CoffeeCoupon, Add = (d, n) => d.CoffeeCoupon += n,
            Icon = () => AssetsDatabase.instance != null ? AssetsDatabase.instance.CoffeeCouponSprite : null,
        },
    };

    public static int[] Snapshot(UserData d)
    {
        var r = new int[All.Length];
        if (d == null) return r;
        for (int i = 0; i < All.Length; i++) r[i] = All[i].Get(d);
        return r;
    }

    public static int Total(UserData d)
    {
        int sum = 0;
        if (d == null) return 0;
        foreach (var c in All) sum += Mathf.Max(0, c.Get(d));
        return sum;
    }
}

/// <summary>
/// 所持品パネル（新ホームの「所持品」ボタンから開く全画面オーバーレイ）。
/// クーポンの一覧 → タップで使用画面（枚数選択）→ 使う → 使用完了画面（スタッフに見せる用）。
/// 消費は旧バッグと同じく FieldValue.Increment による1回の書き込みで、読み取りは発生しない。
/// 1か月遊び放題パス（StorePasses）を持っていれば、クーポンの上に「パス」の欄を出す
/// （使えるもの・期限切れから30日以内のもの）。タップでカードを大きく表示（SpacePassViewer）。
/// </summary>
public class SpaceInventoryPanel : MonoBehaviour
{
    private const float HEADER_H = 262f;
    private const float ROW_H = 176f;
    private const float ROW_GAP = 20f;
    private const float FOOTER_H = 128f;
    private const float TOP_MARGIN = 96f;
    private const float BOTTOM_MARGIN = 32f;
    private const int MAX_DISCOUNT = 100; // 旧バッグと同じ割引上限（%）
    private const float SECTION_H = 64f;  // 一覧の中の見出し（パスがあるときの「クーポン」）
    private const float PASS_CARD_W = 192f;

    private const string T_CAPTION = "INVENTORY";
    private const string T_TITLE = "所持品";
    private const string T_SECTION = "クーポン";
    private const string T_PASS_SECTION = "パス";
    private const string T_PASS_SUFFIX = "パス";
    private const string T_PASS_UNTIL = "まで";
    private const string T_PASS_LEFT_A = "あと";
    private const string T_PASS_LEFT_B = "日";
    private const string T_PASS_LAST_DAY = "本日まで";
    private const string T_PASS_EXPIRED = "期限切れ";
    private const string T_FOOTER = "使うときはスタッフに画面を見せてください";
    private const string T_EMPTY = "まだ持っていません";
    private const string T_OWNED = "所持";
    private const string T_CANCEL = "キャンセル";
    private const string T_USE = "使う";
    private const string T_BUSY = "処理中…";
    private const string T_CAP = "（上限）";
    private const string T_DONE = "クーポンを使用しました";
    private const string T_CAP_NOTE = "※割引は最大100%までです";
    private const string T_SHOW_STAFF = "この画面をスタッフにお見せください";
    private const string T_USED_AT = "使用";
    private const string T_CLOSE = "とじる";
    private const string T_FAILED = "通信に失敗しました\nもう一度お試しください";
    private const string T_NO_USER = "ユーザー情報を読み込めませんでした";

    /// <summary>このパネルで表示しうる文字（NewHome の起動時にまとめてフォントへ登録する）</summary>
    public static string GlyphText
    {
        get
        {
            var sb = new StringBuilder();
            sb.Append(T_CAPTION).Append(T_TITLE).Append(T_SECTION).Append(T_FOOTER).Append(T_EMPTY)
              .Append(T_OWNED).Append(T_CANCEL).Append(T_USE).Append(T_BUSY).Append(T_CAP).Append(T_DONE)
              .Append(T_CAP_NOTE).Append(T_SHOW_STAFF).Append(T_USED_AT).Append(T_CLOSE).Append(T_FAILED)
              .Append(T_NO_USER).Append(T_PASS_SECTION).Append(T_PASS_SUFFIX).Append(T_PASS_UNTIL)
              .Append(T_PASS_LEFT_A).Append(T_PASS_LEFT_B).Append(T_PASS_LAST_DAY).Append(T_PASS_EXPIRED)
              .Append("合計OFF枚×0123456789%/: ");
            foreach (var c in SpaceCoupons.All) sb.Append(c.Name).Append(c.RowDesc).Append(c.Desc);
            return sb.ToString();
        }
    }

    private class Row
    {
        public SpaceCouponDef Def;
        public CanvasGroup Group;
        public TextMeshProUGUI Count;
    }

    private Canvas _canvas;
    private Action _onClosed;
    private CanvasGroup _group;
    private RectTransform _sheet;
    private Vector2 _sheetBase;
    private readonly List<Row> _rows = new List<Row>();
    private bool _closing, _busy;

    // 一覧（パスを読み込んだら作り直す）
    private RectTransform _content;
    private ScrollRect _scroll;
    private TextMeshProUGUI _section;
    private float _safeH;
    private SpacePassViewer _viewer;

    // 使用画面
    private RectTransform _useSheet;
    private SpaceCouponDef _useDef;
    private int _useCount;
    private TextMeshProUGUI _useCountText, _useTotalText, _useOwnedText, _useButtonLabel;
    private CanvasGroup _minusGroup, _plusGroup;

    // 使用完了画面
    private RectTransform _result;

    private static UserData Data =>
        UserDataManager.instance != null ? UserDataManager.instance.UserData : null;

    public static SpaceInventoryPanel Open(Canvas canvas, Action onClosed)
    {
        var root = SpaceTheme.Rect("__SpaceInventory", canvas.transform);
        SpaceTheme.Stretch(root);
        root.SetAsLastSibling();
        var panel = root.gameObject.AddComponent<SpaceInventoryPanel>();
        panel._canvas = canvas;
        panel._onClosed = onClosed;
        panel.Build();
        return panel;
    }

    /// <summary>戻る操作（Android の戻るボタン）。手前の画面から順に閉じる</summary>
    public void HandleBack()
    {
        if (_busy) return;
        if (_viewer != null) _viewer.Close();
        else if (_result != null) CloseResult();
        else if (_useSheet != null) CloseUseSheet();
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
        yield return SpaceTheme.Tween(0.18f, 0f, k =>
        {
            _group.alpha = 1f - k;
            _sheet.anchoredPosition = _sheetBase + new Vector2(0f, -60f * k);
        });
        _onClosed?.Invoke();
        Destroy(gameObject);
    }

    // ================================================================
    // 一覧
    // ================================================================
    private void Build()
    {
        var rt = (RectTransform)transform;
        _group = gameObject.AddComponent<CanvasGroup>();

        // 暗幕（タップで閉じる）
        var dim = SpaceTheme.Rect("Dim", rt);
        SpaceTheme.Stretch(dim);
        var dimImg = SpaceTheme.Img(dim, null, SpaceTheme.DIM, true);
        SpaceTheme.Clickable(dim.gameObject, dimImg, Close, false);

        var safe = SpaceTheme.Rect("Safe", rt);
        SpaceTheme.Stretch(safe);
        safe.gameObject.AddComponent<SpaceSafeArea>();

        // シート: 下寄せ・中身に合わせた高さ（入りきらない端末では一覧をスクロール。高さは BuildList で決める）
        var size = SpaceTheme.CanvasSize(_canvas);
        var insets = SpaceTheme.SafeInsets(size);
        _safeH = size.y - insets.top - insets.bottom;

        _sheet = SpaceTheme.Rect("Sheet", safe);
        _sheet.anchorMin = new Vector2(0f, 0f);
        _sheet.anchorMax = new Vector2(1f, 0f);
        _sheet.pivot = new Vector2(0.5f, 0f);
        _sheet.sizeDelta = new Vector2(-64f, 600f);
        _sheet.anchoredPosition = new Vector2(0f, BOTTOM_MARGIN);
        _sheetBase = _sheet.anchoredPosition;
        // シート上のタップが暗幕（＝閉じる）へ抜けないよう面で受け止める
        SpaceTheme.Glass(_sheet, 56f, SpaceTheme.GLASS_DEEP, null, true);

        var caption = SpaceTheme.Text(_sheet, "Caption", T_CAPTION, 24, SpaceTheme.A(SpaceTheme.CYAN, 0.85f),
            new Vector2(0f, 1f), new Vector2(56f, -70f), new Vector2(400f, 36f), FontStyles.Bold,
            TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
        caption.characterSpacing = 18f;
        SpaceTheme.Text(_sheet, "Title", T_TITLE, 56, SpaceTheme.TEXT,
            new Vector2(0f, 1f), new Vector2(56f, -128f), new Vector2(560f, 72f), FontStyles.Bold,
            TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
        _section = SpaceTheme.Text(_sheet, "Section", T_SECTION, 28, SpaceTheme.TEXT_MUTED,
            new Vector2(0f, 1f), new Vector2(56f, -222f), new Vector2(400f, 40f), FontStyles.Bold,
            TextAlignmentOptions.Left, new Vector2(0f, 0.5f));

        // 閉じる（右上の丸ボタン）
        var close = SpaceTheme.Rect("Close", _sheet, new Vector2(1f, 1f), new Vector2(-80f, -96f), new Vector2(96f, 96f));
        var closeFace = SpaceTheme.Img(close, SpaceSprites.Circle, SpaceTheme.A(new Color(0.09f, 0.12f, 0.30f), 0.65f), true);
        var closeRim = SpaceTheme.Rect("Rim", close);
        SpaceTheme.Stretch(closeRim);
        SpaceTheme.Img(closeRim, SpaceSprites.RingSmall, SpaceTheme.STROKE);
        SpaceTheme.Bar(close, Vector2.zero, 44f, 6f, 45f, SpaceTheme.TEXT_SUB);
        SpaceTheme.Bar(close, Vector2.zero, 44f, 6f, -45f, SpaceTheme.TEXT_SUB);
        SpaceTheme.Clickable(close.gameObject, closeFace, Close);

        // 一覧（スクロール）
        var list = SpaceTheme.Rect("List", _sheet);
        SpaceTheme.Stretch(list, 32f, FOOTER_H, 32f, HEADER_H);
        var viewport = SpaceTheme.Rect("Viewport", list);
        SpaceTheme.Stretch(viewport);
        SpaceTheme.HitArea(viewport); // 行の間の余白でもドラッグできるように
        viewport.gameObject.AddComponent<RectMask2D>();
        _content = SpaceTheme.Rect("Content", viewport);
        _content.anchorMin = new Vector2(0f, 1f);
        _content.anchorMax = new Vector2(1f, 1f);
        _content.pivot = new Vector2(0.5f, 1f);
        _content.anchoredPosition = Vector2.zero;
        _scroll = list.gameObject.AddComponent<ScrollRect>();
        _scroll.viewport = viewport;
        _scroll.content = _content;
        _scroll.horizontal = false;
        _scroll.movementType = ScrollRect.MovementType.Elastic;
        _scroll.scrollSensitivity = 30f;

        SpaceTheme.Text(_sheet, "Footer", T_FOOTER, 26, SpaceTheme.TEXT_MUTED,
            new Vector2(0.5f, 0f), new Vector2(0f, 64f), new Vector2(900f, 40f), FontStyles.Bold);

        BuildList();
        // パスを読み込んでいなければ読み込み、増えたら一覧を作り直す
        StorePasses.Changed += OnPassesChanged;
        if (!StorePasses.MineLoaded) StorePasses.LoadMineAsync().Forget();

        // 下から浮き上がって開く
        _group.alpha = 0f;
        StartCoroutine(SpaceTheme.Tween(0.32f, 0f, k =>
        {
            _group.alpha = k;
            _sheet.anchoredPosition = _sheetBase + new Vector2(0f, -80f * (1f - k));
        }));
    }

    /// <summary>
    /// 一覧を作る（作り直す）: パスがあれば上の見出しを「パス」にしてパスの行 → 「クーポン」の見出し → クーポンの行。
    /// シートの高さもここで中身に合わせる
    /// </summary>
    private void BuildList()
    {
        foreach (Transform child in _content) Destroy(child.gameObject);
        _rows.Clear();

        var passes = StorePasses.MineToShow(DateTime.Now);
        float y = 0f;
        foreach (var pass in passes)
        {
            BuildPassRow(_content, pass, -y);
            y += ROW_H + ROW_GAP;
        }
        if (passes.Count > 0)
        {
            SpaceTheme.Text(_content, "CouponSection", T_SECTION, 28, SpaceTheme.TEXT_MUTED, new Vector2(0f, 1f),
                new Vector2(24f, -(y + SECTION_H * 0.5f - 6f)), new Vector2(400f, 40f), FontStyles.Bold,
                TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
            y += SECTION_H;
        }
        _section.text = passes.Count > 0 ? T_PASS_SECTION : T_SECTION;

        int n = SpaceCoupons.All.Length;
        for (int i = 0; i < n; i++) BuildRow(_content, SpaceCoupons.All[i], -(y + i * (ROW_H + ROW_GAP)));
        float listH = y + n * ROW_H + (n - 1) * ROW_GAP;

        float sheetH = Mathf.Min(HEADER_H + listH + FOOTER_H, _safeH - TOP_MARGIN - BOTTOM_MARGIN);
        _sheet.sizeDelta = new Vector2(-64f, sheetH);
        _content.sizeDelta = new Vector2(0f, listH);
        _scroll.vertical = listH > sheetH - HEADER_H - FOOTER_H + 1f; // 収まるときはスクロールさせない
        UpdateRows();
    }

    private void OnPassesChanged()
    {
        if (this == null || _closing) return;
        BuildList();
    }

    /// <summary>パスの行: 小さなカード＋「ALL DAYパス」＋期限、右に残り日数。タップでカードを大きく表示</summary>
    private void BuildPassRow(RectTransform content, PassInfo pass, float y)
    {
        var now = DateTime.Now;
        bool active = pass.IsActive(now);
        var row = SpaceTheme.Rect("Pass_" + pass.Id, content);
        row.anchorMin = new Vector2(0f, 1f);
        row.anchorMax = new Vector2(1f, 1f);
        row.pivot = new Vector2(0.5f, 1f);
        row.sizeDelta = new Vector2(0f, ROW_H);
        row.anchoredPosition = new Vector2(0f, y);

        var face = SpaceTheme.Glass(row, 36f, SpaceTheme.GLASS,
            SpaceTheme.A(SpacePassCard.GlowColor(pass.Type), active ? 0.5f : 0.2f), true);
        SpaceTheme.Clickable(row.gameObject, face, () => OpenPass(pass));
        if (!active) row.gameObject.AddComponent<CanvasGroup>().alpha = 0.6f;

        var holder = SpaceTheme.Rect("Card", row, new Vector2(0f, 0.5f), new Vector2(24f + PASS_CARD_W * 0.5f, 0f),
            new Vector2(PASS_CARD_W, PASS_CARD_W / SpacePassCard.ASPECT));
        SpacePassCard.Create(holder, pass.Type, pass, PASS_CARD_W, false, false);

        float textX = 24f + PASS_CARD_W + 24f;
        var name = SpaceTheme.Text(row, "Name", pass.Type.Label + T_PASS_SUFFIX, 40, SpaceTheme.TEXT,
            new Vector2(0f, 0.5f), new Vector2(textX, 26f), new Vector2(400f, 56f), FontStyles.Bold,
            TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
        name.enableAutoSizing = true;
        name.fontSizeMin = 30f;
        name.fontSizeMax = 40f;
        SpaceTheme.Text(row, "Desc", StorePasses.DateText(pass.Expiry) + T_PASS_UNTIL, 26, SpaceTheme.TEXT_SUB,
            new Vector2(0f, 0.5f), new Vector2(textX, -28f), new Vector2(400f, 40f), FontStyles.Bold,
            TextAlignmentOptions.Left, new Vector2(0f, 0.5f));

        int days = pass.DaysLeft(now);
        string left = !active ? T_PASS_EXPIRED
            : days <= 1 ? T_PASS_LAST_DAY
            : $"<size=55%><color={SpaceTheme.SUB_HEX}>{T_PASS_LEFT_A} </color></size>{days}<size=55%><color={SpaceTheme.SUB_HEX}> {T_PASS_LEFT_B}</color></size>";
        SpaceTheme.Text(row, "Left", left, active && days > 1 ? 56 : 32, active ? SpaceTheme.GOLD : SpaceTheme.TEXT_MUTED,
            new Vector2(1f, 0.5f), new Vector2(-40f, 0f), new Vector2(260f, 80f), FontStyles.Bold,
            TextAlignmentOptions.Right, new Vector2(1f, 0.5f));
    }

    private void OpenPass(PassInfo pass)
    {
        if (_busy || _useSheet != null || _result != null || _closing || _viewer != null) return;
        _viewer = SpacePassViewer.Open(_canvas, pass, () => _viewer = null);
    }

    private void OnDestroy()
    {
        StorePasses.Changed -= OnPassesChanged;
    }

    private void BuildRow(RectTransform content, SpaceCouponDef def, float y)
    {
        var row = SpaceTheme.Rect("Row_" + def.Key, content);
        row.anchorMin = new Vector2(0f, 1f);
        row.anchorMax = new Vector2(1f, 1f);
        row.pivot = new Vector2(0.5f, 1f);
        row.sizeDelta = new Vector2(0f, ROW_H);
        row.anchoredPosition = new Vector2(0f, y);

        var face = SpaceTheme.Glass(row, 36f, SpaceTheme.GLASS, null, true);
        var group = row.gameObject.AddComponent<CanvasGroup>();
        SpaceTheme.Clickable(row.gameObject, face, () => OnRowTapped(def));

        RoundedIcon(row, def.Icon(), new Vector2(0f, 0.5f), new Vector2(24f + 64f, 0f), 128f, 28f);
        SpaceTheme.Text(row, "Name", def.Name, 40, SpaceTheme.TEXT,
            new Vector2(0f, 0.5f), new Vector2(176f, 26f), new Vector2(520f, 56f), FontStyles.Bold,
            TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
        SpaceTheme.Text(row, "Desc", def.RowDesc, 26, SpaceTheme.TEXT_SUB,
            new Vector2(0f, 0.5f), new Vector2(176f, -28f), new Vector2(560f, 40f), FontStyles.Bold,
            TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
        var count = SpaceTheme.Text(row, "Count", "", 60, SpaceTheme.GOLD,
            new Vector2(1f, 0.5f), new Vector2(-40f, 0f), new Vector2(220f, 80f), FontStyles.Bold,
            TextAlignmentOptions.Right, new Vector2(1f, 0.5f));

        _rows.Add(new Row { Def = def, Group = group, Count = count });
    }

    private void UpdateRows()
    {
        var d = Data;
        foreach (var r in _rows)
        {
            int n = d != null ? Mathf.Max(0, r.Def.Get(d)) : 0;
            r.Count.text = $"{n}<size=43%><color={SpaceTheme.SUB_HEX}> 枚</color></size>";
            r.Count.color = n > 0 ? SpaceTheme.GOLD : SpaceTheme.TEXT_MUTED;
            r.Group.alpha = n > 0 ? 1f : 0.55f;
        }
    }

    private void OnRowTapped(SpaceCouponDef def)
    {
        if (_busy || _useSheet != null || _result != null || _closing) return;
        var d = Data;
        if (d == null || def.Get(d) <= 0)
        {
            SpaceTheme.Toast(T_EMPTY, _canvas);
            return;
        }
        OpenUseSheet(def);
    }

    /// <summary>角丸に切り抜いたアイコン（クーポン画像は四隅が暗いので角丸の枠に収める）。管理者のQR一覧でも使う</summary>
    internal static RectTransform RoundedIcon(RectTransform parent, Sprite sprite, Vector2 anchor, Vector2 pos,
        float size, float radius)
    {
        var frame = SpaceTheme.Rect("Icon", parent, anchor, pos, new Vector2(size, size));
        SpaceTheme.Rounded(frame, SpaceTheme.ICON_BASE, radius);
        frame.gameObject.AddComponent<Mask>().showMaskGraphic = true;
        var pic = SpaceTheme.Rect("Picture", frame);
        SpaceTheme.Stretch(pic);
        var img = SpaceTheme.Img(pic, sprite, Color.white);
        img.preserveAspect = true;
        img.enabled = sprite != null;
        return frame;
    }

    // ================================================================
    // 使用画面（枚数選択）
    // ================================================================
    private void OpenUseSheet(SpaceCouponDef def)
    {
        _useDef = def;
        _useCount = 1;

        _useSheet = SpaceTheme.Rect("UseSheet", transform);
        SpaceTheme.Stretch(_useSheet);
        var group = _useSheet.gameObject.AddComponent<CanvasGroup>();

        var dim = SpaceTheme.Rect("Dim", _useSheet);
        SpaceTheme.Stretch(dim);
        var dimImg = SpaceTheme.Img(dim, null, SpaceTheme.A(SpaceTheme.DIM, 0.6f), true);
        SpaceTheme.Clickable(dim.gameObject, dimImg, () => CloseUseSheet(), false);

        var card = SpaceTheme.Rect("Card", _useSheet, SpaceTheme.CENTER, Vector2.zero, new Vector2(920f, 1060f));
        SpaceTheme.Glass(card, 56f, SpaceTheme.GLASS_DEEP, null, true);

        var glow = SpaceTheme.Rect("Glow", card, SpaceTheme.CENTER, new Vector2(0f, 330f), new Vector2(460f, 460f));
        SpaceTheme.Img(glow, SpaceSprites.Glow, SpaceTheme.A(SpaceTheme.CYAN, 0.24f));
        RoundedIcon(card, def.Icon(), SpaceTheme.CENTER, new Vector2(0f, 330f), 240f, 48f);

        SpaceTheme.Text(card, "Name", def.Name, 50, SpaceTheme.TEXT,
            SpaceTheme.CENTER, new Vector2(0f, 150f), new Vector2(820f, 64f), FontStyles.Bold);
        var desc = SpaceTheme.Text(card, "Desc", def.Desc, 30, SpaceTheme.TEXT_SUB,
            SpaceTheme.CENTER, new Vector2(0f, 62f), new Vector2(820f, 100f), FontStyles.Bold);
        desc.textWrappingMode = TextWrappingModes.Normal;
        _useOwnedText = SpaceTheme.Text(card, "Owned", "", 26, SpaceTheme.TEXT_MUTED,
            SpaceTheme.CENTER, new Vector2(0f, -18f), new Vector2(600f, 40f), FontStyles.Bold);

        // 枚数選択
        _minusGroup = StepperButton(card, new Vector2(-210f, -120f), false, () => { _useCount--; UpdateUseSheet(); });
        _plusGroup = StepperButton(card, new Vector2(210f, -120f), true, () => { _useCount++; UpdateUseSheet(); });
        _useCountText = SpaceTheme.Text(card, "Count", "1", 100, SpaceTheme.TEXT,
            SpaceTheme.CENTER, new Vector2(0f, -120f), new Vector2(260f, 130f), FontStyles.Bold);
        _useTotalText = SpaceTheme.Text(card, "Total", "", 46, SpaceTheme.GOLD,
            SpaceTheme.CENTER, new Vector2(0f, -240f), new Vector2(820f, 60f), FontStyles.Bold);

        // キャンセル / 使う
        var cancel = SpaceTheme.Rect("Cancel", card, SpaceTheme.CENTER, new Vector2(-210f, -390f), new Vector2(380f, 132f));
        var cancelFace = SpaceTheme.Glass(cancel, 46f, SpaceTheme.A(new Color(0.09f, 0.12f, 0.30f), 0.63f), null, true);
        SpaceTheme.Clickable(cancel.gameObject, cancelFace, () => CloseUseSheet());
        var cancelLabel = SpaceTheme.Text(cancel, "Label", T_CANCEL, 38, SpaceTheme.TEXT_SUB, FontStyles.Bold);
        SpaceTheme.Stretch(cancelLabel.rectTransform);

        var use = SpaceTheme.Rect("Use", card, SpaceTheme.CENTER, new Vector2(210f, -390f), new Vector2(380f, 132f));
        var useFace = SpaceTheme.PrimaryFace(use);
        SpaceTheme.Clickable(use.gameObject, useFace, OnUsePressed);
        _useButtonLabel = SpaceTheme.Text(use, "Label", T_USE, 40, SpaceTheme.TEXT_DARK, FontStyles.Bold);
        SpaceTheme.Stretch(_useButtonLabel.rectTransform);

        UpdateUseSheet();

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
        var face = SpaceTheme.Img(rt, SpaceSprites.Circle, SpaceTheme.A(new Color(0.09f, 0.12f, 0.30f), 0.63f), true);
        var rim = SpaceTheme.Rect("Rim", rt);
        SpaceTheme.Stretch(rim);
        SpaceTheme.Img(rim, SpaceSprites.RingSmall, SpaceTheme.STROKE);
        SpaceTheme.Bar(rt, Vector2.zero, 40f, 7f, 0f, SpaceTheme.TEXT);
        if (plus) SpaceTheme.Bar(rt, Vector2.zero, 40f, 7f, 90f, SpaceTheme.TEXT);
        SpaceTheme.Clickable(rt.gameObject, face, onClick);
        return rt.gameObject.AddComponent<CanvasGroup>();
    }

    private void UpdateUseSheet()
    {
        if (_useSheet == null || _useDef == null) return;
        var d = Data;
        int owned = d != null ? Mathf.Max(0, _useDef.Get(d)) : 0;
        _useCount = Mathf.Clamp(_useCount, 1, Mathf.Max(1, owned));
        _useCountText.text = _useCount.ToString();
        _useOwnedText.text = $"{T_OWNED} {owned}枚";

        if (_useDef.Percent > 0)
        {
            int raw = _useDef.Percent * _useCount;
            _useTotalText.text = $"合計 {Mathf.Min(raw, MAX_DISCOUNT)}% OFF" + (raw > MAX_DISCOUNT ? T_CAP : "");
        }
        else
        {
            _useTotalText.text = "";
        }

        SetStepper(_minusGroup, _useCount > 1);
        SetStepper(_plusGroup, _useCount < owned);
    }

    private static void SetStepper(CanvasGroup g, bool enabled)
    {
        g.alpha = enabled ? 1f : 0.35f;
        g.interactable = enabled;
        g.blocksRaycasts = enabled;
    }

    private void CloseUseSheet()
    {
        if (_busy || _useSheet == null) return;
        Destroy(_useSheet.gameObject);
        _useSheet = null;
        _useDef = null;
    }

    private void OnUsePressed()
    {
        if (_busy || _useDef == null) return;
        UseAsync(_useDef, _useCount).Forget();
    }

    /// <summary>
    /// クーポンを消費する。FieldValue.Increment によるサーバー側減算1回で完結する
    /// （読み取りは発生しない。ローカルの UserData も同時に減らす）。履歴の文言は旧バッグと同じ。
    /// </summary>
    private async UniTaskVoid UseAsync(SpaceCouponDef def, int count)
    {
        var manager = UserDataManager.instance;
        if (manager == null || manager.UserData == null || string.IsNullOrEmpty(manager.UID))
        {
            SpaceTheme.Toast(T_NO_USER, _canvas);
            return;
        }
        int owned = def.Get(manager.UserData);
        if (count <= 0 || count > owned) return;

        _busy = true;
        _useButtonLabel.text = T_BUSY;
        try
        {
            await FirebaseFirestore.DefaultInstance.Collection("users").Document(manager.UID)
                .UpdateAsync(new Dictionary<string, object>
                {
                    { def.Field, FieldValue.Increment(-count) },
                }).AsUniTask();
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Inventory] クーポン消費エラー ({def.Name}): {ex.Message}");
            if (this == null) return;
            _busy = false;
            if (_useButtonLabel != null) _useButtonLabel.text = T_USE;
            SpaceTheme.Toast(T_FAILED, _canvas);
            return;
        }

        def.Add(manager.UserData, -count);
        int total = 0;
        if (def.Percent > 0)
        {
            total = Mathf.Min(def.Percent * count, MAX_DISCOUNT);
            LocalHistoryLog.Add("coupon", $"{def.Name} を{count}枚 使用 → 合計{total}%OFF");
        }
        else
        {
            LocalHistoryLog.Add("coupon", $"{def.Name} を{count}枚 使用");
        }
        Debug.Log($"[Inventory] {def.Name} を{count}枚 使用");

        if (this == null) return;
        _busy = false;
        CloseUseSheet();
        UpdateRows();
        ShowResult(def, count, total, def.Percent * count > MAX_DISCOUNT);
    }

    // ================================================================
    // 使用完了画面（スタッフに見せる）
    // ================================================================
    private void ShowResult(SpaceCouponDef def, int count, int totalPercent, bool capped)
    {
        _result = SpaceTheme.Rect("Result", transform);
        SpaceTheme.Stretch(_result);
        var group = _result.gameObject.AddComponent<CanvasGroup>();

        // 誤って閉じないよう、暗幕のタップでは閉じない（「とじる」ボタンのみ）
        var dim = SpaceTheme.Rect("Dim", _result);
        SpaceTheme.Stretch(dim);
        SpaceTheme.Img(dim, null, SpaceTheme.A(SpaceTheme.DIM, 0.7f), true);

        var card = SpaceTheme.Rect("Card", _result, SpaceTheme.CENTER, Vector2.zero, new Vector2(920f, 1120f));
        SpaceTheme.Glass(card, 56f, SpaceTheme.GLASS_DEEP, SpaceTheme.A(SpaceTheme.GOLD, 0.35f), true);

        SpaceTheme.Text(card, "Title", T_DONE, 40, SpaceTheme.TEXT,
            SpaceTheme.CENTER, new Vector2(0f, 470f), new Vector2(820f, 60f), FontStyles.Bold);

        // アイコンの周りを衛星が回り続ける（スクリーンショットではなく今の画面だと分かるように）
        var glow = SpaceTheme.Rect("Glow", card, SpaceTheme.CENTER, new Vector2(0f, 260f), new Vector2(520f, 520f));
        SpaceTheme.Img(glow, SpaceSprites.Glow, SpaceTheme.A(SpaceTheme.GOLD, 0.24f));
        var orbit = SpaceTheme.Rect("Orbit", card, SpaceTheme.CENTER, new Vector2(0f, 260f), new Vector2(330f, 330f));
        SpaceTheme.Img(orbit, SpaceSprites.Ring, SpaceTheme.A(SpaceTheme.GOLD, 0.6f));
        var sat = SpaceTheme.Rect("Satellite", orbit, SpaceTheme.CENTER, new Vector2(0f, 165f), new Vector2(40f, 40f));
        SpaceTheme.Img(sat, SpaceSprites.Glow, SpaceTheme.A(SpaceTheme.GOLD, 0.95f));
        var satCore = SpaceTheme.Rect("Core", sat, SpaceTheme.CENTER, Vector2.zero, new Vector2(12f, 12f));
        SpaceTheme.Img(satCore, SpaceSprites.Circle, Color.white);
        orbit.gameObject.AddComponent<SpaceSpinner>().DegreesPerSecond = -40f;
        RoundedIcon(card, def.Icon(), SpaceTheme.CENTER, new Vector2(0f, 260f), 220f, 44f);

        string strong = def.Percent > 0 ? $"合計 {totalPercent}% OFF" : $"{count}枚 {T_USED_AT}";
        var strongText = SpaceTheme.Text(card, "Strong", strong, 96, SpaceTheme.GOLD,
            SpaceTheme.CENTER, new Vector2(0f, 0f), new Vector2(860f, 120f), FontStyles.Bold);
        strongText.enableAutoSizing = true;
        strongText.fontSizeMin = 56f;
        strongText.fontSizeMax = 96f;
        SpaceTheme.Text(card, "Detail", $"{def.Name} × {count}枚", 34, SpaceTheme.TEXT_SUB,
            SpaceTheme.CENTER, new Vector2(0f, -110f), new Vector2(820f, 50f), FontStyles.Bold);
        float y = -200f;
        if (capped)
        {
            SpaceTheme.Text(card, "Cap", T_CAP_NOTE, 28, SpaceTheme.PINK,
                SpaceTheme.CENTER, new Vector2(0f, -158f), new Vector2(820f, 40f), FontStyles.Bold);
            y = -212f;
        }
        string when = DateTime.Now.ToString("MM/dd HH:mm", CultureInfo.InvariantCulture);
        SpaceTheme.Text(card, "UsedAt", $"{when} {T_USED_AT}", 30, SpaceTheme.TEXT_MUTED,
            SpaceTheme.CENTER, new Vector2(0f, y), new Vector2(820f, 44f), FontStyles.Bold);
        SpaceTheme.Text(card, "Hint", T_SHOW_STAFF, 30, SpaceTheme.TEXT_SUB,
            SpaceTheme.CENTER, new Vector2(0f, -272f), new Vector2(820f, 44f), FontStyles.Bold);

        var close = SpaceTheme.Rect("Close", card, SpaceTheme.CENTER, new Vector2(0f, -430f), new Vector2(440f, 132f));
        var face = SpaceTheme.PrimaryFace(close);
        SpaceTheme.Clickable(close.gameObject, face, CloseResult);
        var label = SpaceTheme.Text(close, "Label", T_CLOSE, 40, SpaceTheme.TEXT_DARK, FontStyles.Bold);
        SpaceTheme.Stretch(label.rectTransform);

        group.alpha = 0f;
        StartCoroutine(SpaceTheme.Tween(0.28f, 0f, k =>
        {
            group.alpha = k;
            card.localScale = Vector3.one * Mathf.Lerp(0.92f, 1f, k);
        }));
    }

    private void CloseResult()
    {
        if (_result == null) return;
        Destroy(_result.gameObject);
        _result = null;
    }
}

/// <summary>一定速度で回転し続ける（使用完了画面の軌道など）</summary>
public class SpaceSpinner : MonoBehaviour
{
    public float DegreesPerSecond = 30f;

    private void Update()
    {
        transform.localEulerAngles = new Vector3(0f, 0f, Time.unscaledTime * DegreesPerSecond);
    }
}
