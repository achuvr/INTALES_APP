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
/// 新ホームで扱うクーポン。店頭で使う6種だけを並べる
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
        // ---- 2026-11-07 追加（ユーザー指定）。絵は Resources/ItemIcon の画像 ----
        new SpaceCouponDef
        {
            Key = "freetime", Field = "freetime_coupon", Name = "フリータイムチケット",
            En = "FREE TIME TICKET", QrText = CallMethodFromQR.QR_FREETIME_COUPON, Accent = SpaceTheme.Hex(0xB278FF),
            RowDesc = "フリータイムが無料",
            Desc = "フリータイムの料金が無料になるチケット。",
            Get = d => d.FreeTimeCoupon, Add = (d, n) => d.FreeTimeCoupon += n,
            Icon = () => ResourceIcon(ref _freeTimeIcon, "ItemIcon/FreeTimecoupon"),
        },
        new SpaceCouponDef
        {
            Key = "fivehour", Field = "fivehour_coupon", Name = "5時間無料チケット",
            En = "5 HOURS FREE TICKET", QrText = CallMethodFromQR.QR_FIVEHOUR_COUPON, Accent = SpaceTheme.Hex(0x5AF0C8),
            RowDesc = "5時間ぶんの料金が無料",
            Desc = "5時間ぶんの料金が無料になるチケット。",
            Get = d => d.FiveHourCoupon, Add = (d, n) => d.FiveHourCoupon += n,
            Icon = () => ResourceIcon(ref _fiveHourIcon, "ItemIcon/FiveHourcoupon"),
        },
    };

    private static Sprite _freeTimeIcon, _fiveHourIcon;

    /// <summary>Resources の画像をスプライトとして読む（1回だけ）</summary>
    private static Sprite ResourceIcon(ref Sprite cache, string path)
    {
        if (cache == null) cache = Resources.Load<Sprite>(path);
        return cache;
    }

    /// <summary>フィールド名（"drink_coupon" など）からクーポンの定義を探す（なければ null）</summary>
    public static SpaceCouponDef ByField(string field)
    {
        foreach (var c in All)
            if (c.Field == field) return c;
        return null;
    }

    /// <summary>フィールド名からクーポンの表示名（「ドリンクチケット」など）</summary>
    public static string NameOf(string field) => ByField(field)?.Name ?? field;

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
    private const string T_MEMBER = "会員証";
    private const string T_MEMBER_CAPTION = "MEMBER CARD";
    private const string T_MEMBER_TITLE = "会員証";
    private const string T_MEMBER_NOTE = "お会計のときにスタッフに見せてください。パス・クーポン・お店の特典をまとめて確認できます";
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
    // 割引チケット（5%・7%）をまとめて使う画面
    private const string T_DISCOUNT_TITLE = "割引チケットを使う";
    private const string T_DISCOUNT_DESC = "5%と7%のチケットを組み合わせて使えます（最大100%オフ）";
    private const string T_DISCOUNT_NONE = "使う枚数を選んでください";
    // 惑星の特典（中央の惑星の★お店の特典。1公転に1回）
    private const string T_PERK_SECTION = "惑星の特典";
    private const string T_PERK_OF = "の特典";
    private const string T_PERK_OK = "使える";
    private const string T_PERK_USED = "使用済み";
    private const string T_PERK_NOT_IN = "入店中に";
    private const string T_PERK_WRONG_DAY = "木曜に";
    private const string T_PERK_WRONG_TIME = "条件外";
    private const string T_PERK_WRONG_SEASON = "夏に";
    private const string T_PERK_MSG_NOT_IN = "お店の画面の入店QRコードを読み取ってから使えます";
    private const string T_PERK_MSG_USED_A = "この公転はもう使いました。次は ";
    private const string T_PERK_MSG_USED_B = " から使えます";
    private const string T_PERK_MSG_DAY = "木曜に使える特典です";
    private const string T_PERK_MSG_MORNING = "開店から1時間以内（12〜13時）に入店した日に使えます";
    private const string T_PERK_MSG_EVENING = "18時以降に入店した日に使えます";
    private const string T_PERK_MSG_SEASON = "夏（6〜8月）に使える特典です";
    private const string T_PERK_CONFIRM_TITLE = "特典を使いますか？";
    private const string T_PERK_CONFIRM_BODY = "1公転（2週間）に1回だけ使えます\nお会計のときに使ってください";
    private const string T_PERK_DONE = "特典を使用しました";

    /// <summary>このパネルで表示しうる文字（NewHome の起動時にまとめてフォントへ登録する）</summary>
    public static string GlyphText
    {
        get
        {
            var sb = new StringBuilder();
            sb.Append(T_CAPTION).Append(T_TITLE).Append(T_SECTION).Append(T_FOOTER).Append(T_EMPTY)
              .Append(T_MEMBER).Append(T_MEMBER_CAPTION).Append(T_MEMBER_NOTE)
              .Append(T_OWNED).Append(T_CANCEL).Append(T_USE).Append(T_BUSY).Append(T_CAP).Append(T_DONE)
              .Append(T_CAP_NOTE).Append(T_SHOW_STAFF).Append(T_USED_AT).Append(T_CLOSE).Append(T_FAILED)
              .Append(T_NO_USER).Append(T_PASS_SECTION).Append(T_PASS_SUFFIX).Append(T_PASS_UNTIL)
              .Append(T_PASS_LEFT_A).Append(T_PASS_LEFT_B).Append(T_PASS_LAST_DAY).Append(T_PASS_EXPIRED)
              .Append(T_PERK_SECTION).Append(T_PERK_OF).Append(T_PERK_OK).Append(T_PERK_USED).Append(T_PERK_NOT_IN)
              .Append(T_PERK_WRONG_DAY).Append(T_PERK_WRONG_TIME).Append(T_PERK_WRONG_SEASON).Append(T_PERK_MSG_NOT_IN)
              .Append(T_PERK_MSG_USED_A).Append(T_PERK_MSG_USED_B).Append(T_PERK_MSG_DAY).Append(T_PERK_MSG_MORNING)
              .Append(T_PERK_MSG_EVENING).Append(T_PERK_MSG_SEASON).Append(T_PERK_CONFIRM_TITLE)
              .Append(T_PERK_CONFIRM_BODY).Append(T_PERK_DONE)
              .Append(T_DISCOUNT_TITLE).Append(T_DISCOUNT_DESC).Append(T_DISCOUNT_NONE).Append("・")
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

    // 割引チケット（5%・7%）をまとめて使う画面（_discountMode のとき。並びは Discounts と同じ）
    private bool _discountMode;
    private readonly int[] _discountCounts = new int[2];
    private readonly TextMeshProUGUI[] _discountCountTexts = new TextMeshProUGUI[2];
    private readonly TextMeshProUGUI[] _discountOwnedTexts = new TextMeshProUGUI[2];
    private readonly CanvasGroup[] _discountMinus = new CanvasGroup[2];
    private readonly CanvasGroup[] _discountPlus = new CanvasGroup[2];
    private CanvasGroup _useButtonGroup;

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
        SpaceTheme.Tracking(caption, 18f);
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

        // 会員証（お会計用のQR。閉じるボタンの左）
        var member = SpaceTheme.Rect("Member", _sheet, new Vector2(1f, 1f), new Vector2(-150f, -96f),
            new Vector2(200f, 84f), new Vector2(1f, 0.5f));
        var memberFace = SpaceTheme.Glass(member, 42f, SpaceTheme.A(SpaceTheme.GOLD, 0.14f), SpaceTheme.A(SpaceTheme.GOLD, 0.7f), true);
        SpaceTheme.Clickable(member.gameObject, memberFace, OpenMemberCard);
        var memberText = SpaceTheme.Text(member, "Label", T_MEMBER, 32, SpaceTheme.GOLD, FontStyles.Bold);
        SpaceTheme.Stretch(memberText.rectTransform);

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
        var perk = PlanetPowers.Of(SpacePlanets.Current);
        bool hasPerk = perk != null && perk.Store;
        if (hasPerk)
        {
            BuildPerkRow(_content, perk, -y);
            y += ROW_H + ROW_GAP;
            if (passes.Count > 0) y = SectionLabel(T_PASS_SECTION, y);
        }
        foreach (var pass in passes)
        {
            BuildPassRow(_content, pass, -y);
            y += ROW_H + ROW_GAP;
        }
        if (passes.Count > 0 || hasPerk) y = SectionLabel(T_SECTION, y);
        _section.text = hasPerk ? T_PERK_SECTION : passes.Count > 0 ? T_PASS_SECTION : T_SECTION;

        int n = SpaceCoupons.All.Length;
        for (int i = 0; i < n; i++) BuildRow(_content, SpaceCoupons.All[i], -(y + i * (ROW_H + ROW_GAP)));
        float listH = y + n * ROW_H + (n - 1) * ROW_GAP;

        float sheetH = Mathf.Min(HEADER_H + listH + FOOTER_H, _safeH - TOP_MARGIN - BOTTOM_MARGIN);
        _sheet.sizeDelta = new Vector2(-64f, sheetH);
        _content.sizeDelta = new Vector2(0f, listH);
        _scroll.vertical = listH > sheetH - HEADER_H - FOOTER_H + 1f; // 収まるときはスクロールさせない
        UpdateRows();
    }

    /// <summary>一覧の中の見出し（「パス」「クーポン」）を置き、次の行の位置を返す</summary>
    private float SectionLabel(string text, float y)
    {
        SpaceTheme.Text(_content, "Section_" + text, text, 28, SpaceTheme.TEXT_MUTED, new Vector2(0f, 1f),
            new Vector2(24f, -(y + SECTION_H * 0.5f - 6f)), new Vector2(400f, 40f), FontStyles.Bold,
            TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
        return y + SECTION_H;
    }

    // ================================================================
    // 惑星の特典（中央の惑星の★お店の特典。1公転に1回）
    // ================================================================
    private void BuildPerkRow(RectTransform content, PlanetPower perk, float y)
    {
        var def = SpacePlanets.Find(perk.Id);
        var state = JourneyService.PerkStateOf(perk);
        bool ok = state == PerkState.Ok;
        var row = SpaceTheme.Rect("Perk", content);
        row.anchorMin = new Vector2(0f, 1f);
        row.anchorMax = new Vector2(1f, 1f);
        row.pivot = new Vector2(0.5f, 1f);
        row.sizeDelta = new Vector2(0f, ROW_H);
        row.anchoredPosition = new Vector2(0f, y);
        var face = SpaceTheme.Glass(row, 36f, SpaceTheme.GLASS, SpaceTheme.A(SpaceTheme.GOLD, ok ? 0.55f : 0.2f), true);
        SpaceTheme.Clickable(row.gameObject, face, () => OnPerkTapped(perk));
        if (!ok) row.gameObject.AddComponent<CanvasGroup>().alpha = 0.7f;

        if (def != null)
        {
            var holder = SpaceTheme.Rect("Planet", row, new Vector2(0f, 0.5f), new Vector2(24f + 64f, 0f),
                new Vector2(128f, 128f));
            var glow = SpaceTheme.Rect("Glow", holder, SpaceTheme.CENTER, Vector2.zero, new Vector2(190f, 190f));
            SpaceTheme.Img(glow, SpaceSprites.Glow, SpaceTheme.A(def.Accent, 0.3f));
            SpacePlanetView.Create(holder, def, 128f, SpacePlanetView.Quality.Thumb, false);
        }
        var name = SpaceTheme.Text(row, "Name", (def?.Name ?? "") + T_PERK_OF, 40, SpaceTheme.TEXT,
            new Vector2(0f, 0.5f), new Vector2(176f, 26f), new Vector2(520f, 56f), FontStyles.Bold,
            TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
        name.enableAutoSizing = true;
        name.fontSizeMin = 30f;
        name.fontSizeMax = 40f;
        var desc = SpaceTheme.Text(row, "Desc", perk.AbilityText, 26, SpaceTheme.TEXT_SUB,
            new Vector2(0f, 0.5f), new Vector2(176f, -28f), new Vector2(600f, 40f), FontStyles.Bold,
            TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
        desc.enableAutoSizing = true;
        desc.fontSizeMin = 18f;
        desc.fontSizeMax = 26f;
        string label = state switch
        {
            PerkState.Ok => T_PERK_OK,
            PerkState.Used => T_PERK_USED,
            PerkState.WrongDay => T_PERK_WRONG_DAY,
            PerkState.WrongSeason => T_PERK_WRONG_SEASON,
            PerkState.WrongTime => T_PERK_WRONG_TIME,
            _ => T_PERK_NOT_IN,
        };
        SpaceTheme.Text(row, "State", label, 34, ok ? SpaceTheme.GOLD : SpaceTheme.TEXT_MUTED,
            new Vector2(1f, 0.5f), new Vector2(-40f, 0f), new Vector2(200f, 60f), FontStyles.Bold,
            TextAlignmentOptions.Right, new Vector2(1f, 0.5f));
    }

    private void OnPerkTapped(PlanetPower perk)
    {
        if (_busy || _useSheet != null || _result != null || _closing || _viewer != null) return;
        var state = JourneyService.PerkStateOf(perk);
        if (state != PerkState.Ok)
        {
            string msg = state switch
            {
                PerkState.Used => T_PERK_MSG_USED_A
                                  + StoreCalendar.MonthDayW(StoreCalendar.PeriodStart(StoreCalendar.CurrentPeriod + 1))
                                  + T_PERK_MSG_USED_B,
                PerkState.WrongDay => T_PERK_MSG_DAY,
                PerkState.WrongSeason => T_PERK_MSG_SEASON,
                PerkState.WrongTime => perk.Rule == PerkRule.Morning ? T_PERK_MSG_MORNING : T_PERK_MSG_EVENING,
                _ => T_PERK_MSG_NOT_IN,
            };
            SpaceTheme.Toast(SpaceTheme.SafeNow(msg), _canvas);
            return;
        }
        var def = SpacePlanets.Find(perk.Id);
        SpaceDialog.Confirm(_canvas, T_PERK_CONFIRM_TITLE, (def?.Name ?? "") + T_PERK_OF + "「" + perk.PerkName + "」\n"
            + T_PERK_CONFIRM_BODY, T_USE, false, () => UsePerkAsync(perk).Forget());
    }

    private async UniTaskVoid UsePerkAsync(PlanetPower perk)
    {
        if (_busy) return;
        _busy = true;
        bool ok;
        try
        {
            ok = await JourneyService.UsePerkAsync(perk);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Inventory] 特典の使用エラー ({perk.Id}): {ex.Message}");
            ok = false;
        }
        if (this == null) return;
        _busy = false;
        if (!ok)
        {
            SpaceTheme.Toast(T_FAILED, _canvas);
            BuildList();
            return;
        }
        BuildList();
        ShowPerkResult(perk);
    }

    /// <summary>特典の使用完了画面（スタッフに見せる。惑星のまわりを衛星が回り続ける）</summary>
    private void ShowPerkResult(PlanetPower perk)
    {
        var def = SpacePlanets.Find(perk.Id);
        _result = SpaceTheme.Rect("Result", transform);
        SpaceTheme.Stretch(_result);
        SpaceTheme.SubCanvas(_result.gameObject, true);
        var group = _result.gameObject.AddComponent<CanvasGroup>();
        var dim = SpaceTheme.Rect("Dim", _result);
        SpaceTheme.Stretch(dim);
        SpaceTheme.Img(dim, null, SpaceTheme.A(SpaceTheme.DIM, 0.7f), true);

        var card = SpaceTheme.Rect("Card", _result, SpaceTheme.CENTER, Vector2.zero, new Vector2(920f, 1120f));
        SpaceTheme.Glass(card, 56f, SpaceTheme.GLASS_DEEP, SpaceTheme.A(SpaceTheme.GOLD, 0.35f), true);
        SpaceTheme.Text(card, "Title", T_PERK_DONE, 40, SpaceTheme.TEXT,
            SpaceTheme.CENTER, new Vector2(0f, 470f), new Vector2(820f, 60f), FontStyles.Bold);

        var accent = def != null ? def.Accent : SpaceTheme.GOLD;
        var glow = SpaceTheme.Rect("Glow", card, SpaceTheme.CENTER, new Vector2(0f, 260f), new Vector2(520f, 520f));
        SpaceTheme.Img(glow, SpaceSprites.Glow, SpaceTheme.A(accent, 0.26f));
        var orbit = SpaceTheme.Rect("Orbit", card, SpaceTheme.CENTER, new Vector2(0f, 260f), new Vector2(330f, 330f));
        SpaceTheme.Img(orbit, SpaceSprites.Ring, SpaceTheme.A(SpaceTheme.GOLD, 0.6f));
        var sat = SpaceTheme.Rect("Satellite", orbit, SpaceTheme.CENTER, new Vector2(0f, 165f), new Vector2(40f, 40f));
        SpaceTheme.Img(sat, SpaceSprites.Glow, SpaceTheme.A(SpaceTheme.GOLD, 0.95f));
        var satCore = SpaceTheme.Rect("Core", sat, SpaceTheme.CENTER, Vector2.zero, new Vector2(12f, 12f));
        SpaceTheme.Img(satCore, SpaceSprites.Circle, Color.white);
        orbit.gameObject.AddComponent<SpaceSpinner>().DegreesPerSecond = -40f;
        if (def != null)
        {
            var holder = SpaceTheme.Rect("Planet", card, SpaceTheme.CENTER, new Vector2(0f, 260f), new Vector2(240f, 240f));
            SpacePlanetView.Create(holder, def, 240f, SpacePlanetView.Quality.Large, true);
        }

        var strongText = SpaceTheme.Text(card, "Strong", perk.PerkName, 80, SpaceTheme.GOLD,
            SpaceTheme.CENTER, new Vector2(0f, 0f), new Vector2(860f, 120f), FontStyles.Bold);
        strongText.enableAutoSizing = true;
        strongText.fontSizeMin = 48f;
        strongText.fontSizeMax = 80f;
        SpaceTheme.Text(card, "Detail", (def?.Name ?? "") + T_PERK_OF, 34, SpaceTheme.TEXT_SUB,
            SpaceTheme.CENTER, new Vector2(0f, -110f), new Vector2(820f, 50f), FontStyles.Bold);
        string when = DateTime.Now.ToString("MM/dd HH:mm", CultureInfo.InvariantCulture);
        SpaceTheme.Text(card, "UsedAt", $"{when} {T_USED_AT}", 30, SpaceTheme.TEXT_MUTED,
            SpaceTheme.CENTER, new Vector2(0f, -200f), new Vector2(820f, 44f), FontStyles.Bold);
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
    /// <summary>会員証（お会計用のQR。MemberCard）を全画面で出す</summary>
    private void OpenMemberCard()
    {
        var m = UserDataManager.instance;
        if (m == null || string.IsNullOrEmpty(m.UID)) return;
        var planet = SpacePlanets.Current;
        var perk = JourneyService.PerkStateOf(PlanetPowers.Of(planet));
        string payload = MemberCard.Payload(m.UID, planet.Id, perk);
        string name = SpaceTheme.NoParse(SpaceTheme.Safe(m.UserData.Username ?? "")) + " さん";
        SpaceQrSheet.Show(_canvas, T_MEMBER_CAPTION, T_MEMBER_TITLE, payload, name, T_MEMBER_NOTE, SpaceTheme.GOLD);
    }

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
        // 5%・7%の割引チケットは、両方の枚数を選んでまとめて使う画面（2026-11-06 ユーザー依頼）
        if (def.Percent > 0)
        {
            OpenDiscountSheet(def);
            return;
        }
        _discountMode = false;
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
        if (_discountMode)
        {
            UpdateDiscountSheet();
            return;
        }
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
        _discountMode = false;
    }

    private void OnUsePressed()
    {
        if (_busy) return;
        if (_discountMode)
        {
            UseDiscountAsync().Forget();
            return;
        }
        if (_useDef == null) return;
        UseAsync(_useDef, _useCount).Forget();
    }

    // ================================================================
    // 割引チケット（5%・7%）をまとめて使う画面
    // ================================================================
    /// <summary>割引チケットの種類（SpaceCoupons の並び順。5% → 7%）</summary>
    private static SpaceCouponDef[] Discounts => Array.FindAll(SpaceCoupons.All, c => c.Percent > 0);

    /// <summary>5%・7%の枚数をそれぞれ選び、合計の割引率を見ながらまとめて使う。最初はタップしたチケットを1枚</summary>
    private void OpenDiscountSheet(SpaceCouponDef tapped)
    {
        _discountMode = true;
        _useDef = null;
        var defs = Discounts;
        for (int i = 0; i < _discountCounts.Length; i++)
            _discountCounts[i] = i < defs.Length && defs[i] == tapped ? 1 : 0;

        _useSheet = SpaceTheme.Rect("UseSheet", transform);
        SpaceTheme.Stretch(_useSheet);
        var group = _useSheet.gameObject.AddComponent<CanvasGroup>();

        var dim = SpaceTheme.Rect("Dim", _useSheet);
        SpaceTheme.Stretch(dim);
        var dimImg = SpaceTheme.Img(dim, null, SpaceTheme.A(SpaceTheme.DIM, 0.6f), true);
        SpaceTheme.Clickable(dim.gameObject, dimImg, () => CloseUseSheet(), false);

        var card = SpaceTheme.Rect("Card", _useSheet, SpaceTheme.CENTER, Vector2.zero, new Vector2(920f, 1080f));
        SpaceTheme.Glass(card, 56f, SpaceTheme.GLASS_DEEP, null, true);

        SpaceTheme.Text(card, "Title", T_DISCOUNT_TITLE, 46, SpaceTheme.TEXT,
            SpaceTheme.CENTER, new Vector2(0f, 452f), new Vector2(820f, 64f), FontStyles.Bold);
        var desc = SpaceTheme.Text(card, "Desc", T_DISCOUNT_DESC, 28, SpaceTheme.TEXT_SUB,
            SpaceTheme.CENTER, new Vector2(0f, 386f), new Vector2(840f, 44f), FontStyles.Bold);
        desc.enableAutoSizing = true;
        desc.fontSizeMin = 20f;
        desc.fontSizeMax = 28f;

        for (int i = 0; i < defs.Length && i < _discountCounts.Length; i++)
            BuildDiscountRow(card, defs[i], i, 216f - i * 220f);

        _useTotalText = SpaceTheme.Text(card, "Total", "", 56, SpaceTheme.GOLD,
            SpaceTheme.CENTER, new Vector2(0f, -212f), new Vector2(840f, 72f), FontStyles.Bold);

        // キャンセル / 使う（使う枚数が0のときは押せない）
        var cancel = SpaceTheme.Rect("Cancel", card, SpaceTheme.CENTER, new Vector2(-210f, -400f), new Vector2(380f, 132f));
        var cancelFace = SpaceTheme.Glass(cancel, 46f, SpaceTheme.A(new Color(0.09f, 0.12f, 0.30f), 0.63f), null, true);
        SpaceTheme.Clickable(cancel.gameObject, cancelFace, () => CloseUseSheet());
        var cancelLabel = SpaceTheme.Text(cancel, "Label", T_CANCEL, 38, SpaceTheme.TEXT_SUB, FontStyles.Bold);
        SpaceTheme.Stretch(cancelLabel.rectTransform);

        var use = SpaceTheme.Rect("Use", card, SpaceTheme.CENTER, new Vector2(210f, -400f), new Vector2(380f, 132f));
        var useFace = SpaceTheme.PrimaryFace(use);
        SpaceTheme.Clickable(use.gameObject, useFace, OnUsePressed);
        _useButtonLabel = SpaceTheme.Text(use, "Label", T_USE, 40, SpaceTheme.TEXT_DARK, FontStyles.Bold);
        SpaceTheme.Stretch(_useButtonLabel.rectTransform);
        _useButtonGroup = use.gameObject.AddComponent<CanvasGroup>();

        UpdateDiscountSheet();

        group.alpha = 0f;
        StartCoroutine(SpaceTheme.Tween(0.22f, 0f, k =>
        {
            group.alpha = k;
            card.localScale = Vector3.one * Mathf.Lerp(0.94f, 1f, k);
        }));
    }

    /// <summary>割引チケット1種類の行: 絵・名前・所持枚数、右に −／枚数／＋</summary>
    private void BuildDiscountRow(RectTransform card, SpaceCouponDef def, int index, float y)
    {
        var row = SpaceTheme.Rect("Row_" + def.Key, card, SpaceTheme.CENTER, new Vector2(0f, y), new Vector2(840f, 196f));
        SpaceTheme.Glass(row, 36f, SpaceTheme.GLASS, null);
        RoundedIcon(row, def.Icon(), new Vector2(0f, 0.5f), new Vector2(24f + 64f, 0f), 128f, 28f);
        SpaceTheme.Text(row, "Name", def.Name, 36, SpaceTheme.TEXT, new Vector2(0f, 0.5f), new Vector2(176f, 28f),
            new Vector2(300f, 50f), FontStyles.Bold, TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
        _discountOwnedTexts[index] = SpaceTheme.Text(row, "Owned", "", 24, SpaceTheme.TEXT_MUTED, new Vector2(0f, 0.5f),
            new Vector2(176f, -26f), new Vector2(300f, 36f), FontStyles.Bold, TextAlignmentOptions.Left,
            new Vector2(0f, 0.5f));
        int i = index;
        _discountMinus[i] = StepperButton(row, new Vector2(120f, 0f), false, () =>
        {
            _discountCounts[i]--;
            UpdateDiscountSheet();
        });
        _discountPlus[i] = StepperButton(row, new Vector2(352f, 0f), true, () =>
        {
            _discountCounts[i]++;
            UpdateDiscountSheet();
        });
        _discountCountTexts[i] = SpaceTheme.Text(row, "Count", "0", 64, SpaceTheme.TEXT,
            SpaceTheme.CENTER, new Vector2(236f, 0f), new Vector2(112f, 90f), FontStyles.Bold);
    }

    private void UpdateDiscountSheet()
    {
        if (_useSheet == null) return;
        var d = Data;
        var defs = Discounts;
        int raw = 0, used = 0;
        for (int i = 0; i < defs.Length && i < _discountCounts.Length; i++)
        {
            int owned = d != null ? Mathf.Max(0, defs[i].Get(d)) : 0;
            _discountCounts[i] = Mathf.Clamp(_discountCounts[i], 0, owned);
            _discountCountTexts[i].text = _discountCounts[i].ToString();
            _discountCountTexts[i].color = _discountCounts[i] > 0 ? SpaceTheme.TEXT : SpaceTheme.TEXT_MUTED;
            _discountOwnedTexts[i].text = $"{T_OWNED} {owned}枚";
            SetStepper(_discountMinus[i], _discountCounts[i] > 0);
            SetStepper(_discountPlus[i], _discountCounts[i] < owned);
            raw += defs[i].Percent * _discountCounts[i];
            used += _discountCounts[i];
        }
        _useTotalText.text = used > 0
            ? $"合計 {Mathf.Min(raw, MAX_DISCOUNT)}% OFF" + (raw > MAX_DISCOUNT ? T_CAP : "")
            : T_DISCOUNT_NONE;
        _useTotalText.color = used > 0 ? SpaceTheme.GOLD : SpaceTheme.TEXT_MUTED;
        SpaceTextScaler.SetSize(_useTotalText, used > 0 ? 56f : 34f);
        SetStepper(_useButtonGroup, used > 0);
    }

    /// <summary>
    /// 選んだ割引チケットをまとめて消費する。FieldValue.Increment の減算を1回の書き込みで（読み取りなし）。
    /// 履歴は「5%OFFチケット を1枚・7%OFFチケット を2枚 使用 → 合計19%OFF」（1種類なら旧バッグと同じ書き方）
    /// </summary>
    private async UniTaskVoid UseDiscountAsync()
    {
        var manager = UserDataManager.instance;
        if (manager == null || manager.UserData == null || string.IsNullOrEmpty(manager.UID))
        {
            SpaceTheme.Toast(T_NO_USER, _canvas);
            return;
        }
        var defs = Discounts;
        var counts = new int[defs.Length];
        int used = 0, raw = 0;
        for (int i = 0; i < defs.Length && i < _discountCounts.Length; i++)
        {
            counts[i] = _discountCounts[i];
            if (counts[i] < 0 || counts[i] > defs[i].Get(manager.UserData)) return;
            used += counts[i];
            raw += defs[i].Percent * counts[i];
        }
        if (used <= 0) return;

        var updates = new Dictionary<string, object>();
        for (int i = 0; i < defs.Length; i++)
            if (counts[i] > 0) updates[defs[i].Field] = FieldValue.Increment(-counts[i]);

        _busy = true;
        _useButtonLabel.text = T_BUSY;
        try
        {
            await FirebaseFirestore.DefaultInstance.Collection("users").Document(manager.UID)
                .UpdateAsync(updates).AsUniTask();
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Inventory] 割引チケットの消費エラー: {ex.Message}");
            if (this == null) return;
            _busy = false;
            if (_useButtonLabel != null) _useButtonLabel.text = T_USE;
            SpaceTheme.Toast(T_FAILED, _canvas);
            return;
        }

        var parts = new List<string>();
        var details = new List<string>();
        var icons = new List<Sprite>();
        for (int i = 0; i < defs.Length; i++)
        {
            if (counts[i] <= 0) continue;
            defs[i].Add(manager.UserData, -counts[i]);
            parts.Add($"{defs[i].Name} を{counts[i]}枚");
            details.Add($"{defs[i].Name} × {counts[i]}枚");
            icons.Add(defs[i].Icon());
        }
        int total = Mathf.Min(raw, MAX_DISCOUNT);
        LocalHistoryLog.Add("coupon", $"{string.Join("・", parts)} 使用 → 合計{total}%OFF");
        Debug.Log($"[Inventory] {string.Join("・", parts)} 使用 → 合計{total}%OFF");

        if (this == null) return;
        _busy = false;
        CloseUseSheet();
        UpdateRows();
        ShowResultCore(icons, $"合計 {total}% OFF", string.Join("・", details), raw > MAX_DISCOUNT);
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
        string strong = def.Percent > 0 ? $"合計 {totalPercent}% OFF" : $"{count}枚 {T_USED_AT}";
        ShowResultCore(new List<Sprite> { def.Icon() }, strong, $"{def.Name} × {count}枚", capped);
    }

    /// <summary>使用完了画面の本体。icons が2枚なら左右に少し重ねて並べる（割引チケットを2種類使ったとき）</summary>
    private void ShowResultCore(List<Sprite> icons, string strong, string detail, bool capped)
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
        if (icons.Count >= 2)
        {
            RoundedIcon(card, icons[0], SpaceTheme.CENTER, new Vector2(-84f, 260f), 190f, 40f);
            RoundedIcon(card, icons[1], SpaceTheme.CENTER, new Vector2(84f, 260f), 190f, 40f);
        }
        else
        {
            RoundedIcon(card, icons.Count > 0 ? icons[0] : null, SpaceTheme.CENTER, new Vector2(0f, 260f), 220f, 44f);
        }

        var strongText = SpaceTheme.Text(card, "Strong", strong, 96, SpaceTheme.GOLD,
            SpaceTheme.CENTER, new Vector2(0f, 0f), new Vector2(860f, 120f), FontStyles.Bold);
        strongText.enableAutoSizing = true;
        strongText.fontSizeMin = 56f;
        strongText.fontSizeMax = 96f;
        var detailText = SpaceTheme.Text(card, "Detail", detail, 34, SpaceTheme.TEXT_SUB,
            SpaceTheme.CENTER, new Vector2(0f, -110f), new Vector2(840f, 50f), FontStyles.Bold);
        detailText.enableAutoSizing = true;
        detailText.fontSizeMin = 22f;
        detailText.fontSizeMax = 34f;
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
