using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 新ホーム画面（NewHome シーン）。2026-11 の店舗形態変更に合わせた宇宙デザインのホーム。
/// 戦闘・装備・アチーブメントは再設計中のため、ログイン後はこの画面だけを出す:
///   ・最低限のユーザー情報（名前・来店回数・入店状況）
///   ・カメラ起動ボタン（QRシーンを加算マージで開く。旧Homeのカメラボタンと同じ方式）
///   ・所持品ボタン（クーポンの一覧と使用。SpaceInventoryPanel）
///   ・右上の設定ボタン（プライバシーポリシー・ログアウト・退会。SpaceAccountMenu）
///   ・右上の惑星ボタン（中央の惑星を選ぶ。SpacePlanetPicker。選んだ惑星は端末に保存）
///   ・右上のフレンドボタン（惑星ボタンの左。フレンド一覧・自分のQR・入店中を知らせる設定。SpaceFriendPanel）
/// 中央のカメラボタンは選んだ惑星（SpacePlanetView）そのもの。ルミナ（最初のオーブ）以外では右下にカメラの印を付ける。
/// 惑星のまわりは、緑の衛星の円・紫の衛星の傾いた楕円、いちばん外の円には入店中の人数ぶんの小さな球が
/// それぞれが選んでいる惑星の色で回る（StoreVisitors。名前もIDもない記録）。
/// その右下（「カメラを起動」の右）に浮かぶ金色の小さな惑星が履歴ボタン（SpaceHistoryPanel）。文字やアイコンは付けず、
/// 背景の左上の小さな惑星と同じ描き方の星にしている。
/// 左上のピンクの小さな惑星（背景 SpaceBackground の衛星）はお知らせのボタン（SpaceNewsPage。店の画像と相席募集の概要）。
/// お知らせに見ていない内容（新しい画像・新しい募集）があると、その惑星から「New」の吹き出しが出る（SpaceNewsBadge）。
///
/// UIはすべてコード生成。シーンには Camera / EventSystem / AssetsDatabase / Canvas と本コンポーネントだけを置く。
/// QR読み取り後のモーダル・トースト（InfoModal など）は "Canvas" という名前のキャンバスに出るため、
/// シーンのキャンバス名は "Canvas" のままにしておくこと。
/// </summary>
public class NewHomeController : MonoBehaviour
{
    [SerializeField] private Canvas _canvas;
    [SerializeField] private TMP_FontAsset _font;

    private const string T_WORDMARK = "INTALES Cafe";
    // 見出しの右端（右上のボタンの左端から24の余白）。管理者はボタンが2つ多いので、入りきらない分だけ文字を小さくする
    private const float HEADER_RIGHT = 336f + 48f + 24f;       // フレンドボタンの左
    private const float HEADER_RIGHT_ADMIN = 560f + 48f + 24f; // 管理者: パスの購入者ボタンの左
    private const string T_WELCOME = "WELCOME";            // 初来店前
    private const string T_WELCOME_BACK = "WELCOME BACK";  // 来店したことがある
    private const string T_SAN = "さん";
    private const string T_VISITS = "来店回数";
    private const string T_LAST_VISIT = "前回の来店";
    private const string T_STAY = "滞在時間";
    private const string T_IN_STORE = "入店中";
    private const string T_CAMERA = "カメラを起動";
    private const string T_CAMERA_SUB = "QRコードを読み取る";
    private const string T_BAG = "所持品";
    private const string T_BAG_PASS = "パス"; // 所持品ボタンの小さな文字（使える1か月パスの枚数）
    private const string T_GUEST = "ゲスト";
    private const string T_GAINED = "を入手しました";
    private const string T_GAINED_MANY = "クーポンを入手しました";
    private const string GLYPH_EXTRA = "0123456789 /:%ー回時間分枚クーポン★";

    private const float ORBIT_TILT = 18f;     // 楕円軌道の傾き（度）
    private const float ORBIT_RADIUS = 360f;  // 楕円軌道の長半径
    private const float ORBIT_FLAT = 0.30f;   // 楕円軌道の扁平率
    private const float PLANET_VIEW = 475f;   // 中央の惑星の枠（球の直径は 0.8 倍 = 380。元のオーブと同じ）

    // 履歴ボタン（金色の小さな惑星）: 中央の惑星から見て右下・「カメラを起動」の右。背景の左上の小さな惑星
    // （92px）と対になる位置で、押すものなので少し大きめ。押せる範囲は絵より広くとる。
    // いちばん外の円（入店中の人数の球）の上には乗せず、円の外の少し下に置く（2026-11-05 ユーザー指定）
    private static readonly Vector2 HISTORY_POS = new Vector2(360f, -420f);
    private const float HISTORY_SIZE = 112f;  // 絵の大きさ（球の直径は 0.8 倍）
    private const float HISTORY_HIT = 176f;

    // ボードゲーム図鑑ボタン（緑の小さな惑星）: 履歴の惑星と左右対称の左下。図鑑をダウンロードした人だけ（SpaceZukan）
    private static readonly Vector2 ZUKAN_POS = new Vector2(-HISTORY_POS.x, HISTORY_POS.y);
    private static readonly Color ZUKAN_ACCENT = SpaceTheme.Hex(0x63F0A8);

    // 料金表ボタン（水色の小さな惑星）: 中央の惑星の右上。背景の左上のピンクの小さな惑星（お知らせ）と左右対称の位置・大きさ
    // （ピンクの惑星はセーフエリアの上端から 760・左端から 120。こちらは右端から 120）
    private static readonly Vector2 PRICE_POS = new Vector2(-120f, -760f);
    private const float PRICE_SIZE = 92f;
    private const float PRICE_HIT = 176f;

    // いちばん外の円: 入店中の人数ぶんの小さな球が、それぞれが選んでいる惑星の色で回る（StoreVisitors）。
    // 下側は「カメラを起動」「QRコードを読み取る」の文字の外を通る大きさ。
    // 履歴・図鑑の小さな惑星はこの円の外の少し下にある（円の上には乗らない）
    private const float VISITOR_RADIUS = 468.6f;
    private const int VISITOR_MAX = 48;       // 球はここまで（多すぎると円が埋まるため）
    private const float VISITOR_GLOW = 56f;   // 球の光の大きさ（紫の衛星と同じ）
    private const float VISITOR_CORE = 20f;   // 光の中の明るい芯

    // 入店のQRを読んだら、自分の惑星が中央の惑星から飛び出して、いちばん外の円の自分の場所へ飛んでいく
    private const float ARRIVAL_DURATION = 1.3f;  // 飛ぶ時間
    private const float ARRIVAL_DELAY = 0.35f;    // ホームが見えてから飛び出すまでの間
    private const float ARRIVAL_R0 = 110f;        // 飛び出す位置（中央の惑星の中心からの距離）
    private const float ARRIVAL_SWEEP = 80f;      // 円の回る向き（反時計回り）に回り込みながら着く角度
    private const float ARRIVAL_SPHERE0 = 150f;   // 飛ぶ惑星の球の直径（飛び出した直後）
    private const float ARRIVAL_SPHERE1 = 34f;    // 〃（着く直前。円の上の球より少し大きい）
    private const float TRAIL_LIFE = 0.55f;       // 光の尾の1粒が消えるまで
    private const float TRAIL_STEP = 0.025f;      // 光の尾の粒を出す間隔

    // 中央の惑星のまわりを回る衛星の色（円軌道＝緑、傾いた楕円軌道＝紫）。光と、その中の明るい芯
    private static readonly Color SAT_GREEN = SpaceTheme.Hex(0x6BFF8E);
    private static readonly Color SAT_GREEN_CORE = new Color(0.86f, 1f, 0.90f, 1f);
    private static readonly Color SAT_PURPLE = SpaceTheme.Hex(0xB07BFF);
    private static readonly Color SAT_PURPLE_CORE = new Color(0.92f, 0.84f, 1f, 1f);

    // ---- UI ----
    private RectTransform _content, _cardRt, _orbRt, _bagRt, _orbCore, _orbitRing, _dotBack, _dotFront;
    private RectTransform _planetHolder, _cameraIcon, _cameraBadge, _flash, _historyMoon;
    private CanvasGroup _headerGroup, _cardGroup, _orbGroup, _bagGroup, _planetBtnGroup, _friendBtnGroup;
    private RectTransform _headerRt;
    private CanvasGroup _planetHolderGroup, _cameraIconGroup, _badgeGroup, _historyGroup;
    private TextMeshProUGUI _avatarText, _captionText, _nameText, _visitValue, _statusLabel, _statusValue, _bagSub;
    private GameObject _inStorePill, _tiltBack, _tiltFront, _planetBtnDot;
    private Image _pillGlow, _orbGlow, _flashImg, _historyGlow;
    private RectTransform _priceMoon;   // 料金表の水色の小さな惑星（漂う絵）
    private CanvasGroup _priceGroup;
    private Image _priceGlow;
    private SpacePricePage _prices;     // 料金表（開いている間）
    private SpaceInventoryPanel _inventory;
    private SpaceHistoryPanel _history;
    private SpaceNewsPage _news;
    private SpaceFriendPanel _friends;
    private SpaceBackground _background;
    private RectTransform _visitorRing;                                      // 入店中の人の球（まとめて回す）
    private readonly List<VisitorOrb> _visitorOrbs = new List<VisitorOrb>();
    private string _visitorKey;                                              // 表示中の人と惑星の並び（同じなら何もしない）
    private string _arrivalId;           // 飛んでいく途中の自分の球の記録のID（着くまで円の上には出さない）
    private bool _arrivalFlying;
    private float _arrivalReadyAt = -1f; // ホームが見えた（QR画面・お知らせが閉じた）あと、飛び出す時刻

    /// <summary>いちばん外の円の球ひとつ（入店中の人ひとり）</summary>
    private class VisitorOrb
    {
        public string Id, Planet;     // 記録のID（だれかはわからない）・選んでいる惑星
        public RectTransform Rt;
        public Image Glow, Core;
        public float Angle, Target;   // 円の上の位置（度。円ごと回すので、円の中での角度）
        public float Show;            // 0→1 で膨らんで出る、いなくなったら 1→0 でしぼんで消える
        public bool Leaving;
    }
    private List<string> _newsUrls; // お店のお知らせ画像の一覧（起動時に1回。読めたかの目印。判定は MasterData の今の一覧で行う）
    private SpaceAccountMenu _accountMenu;
    private SpacePlanetPicker _picker;
    private SpaceAdminQrList _qrList;
    private SpaceAdminPassList _passList;   // 管理者用: 1か月パスの購入者の一覧
    private SpaceBoardGameZukan _zukan;      // ボードゲーム図鑑（開いている間）
    private RectTransform _zukanBtn, _zukanMoon;
    private CanvasGroup _zukanGroup;
    private Image _zukanGlow;
    private Coroutine _zukanAnim;
    private RectTransform _adminPassBtn;
    private RectTransform _adminBtn;
    private SpacePlanetView _planetView;
    private SpacePlanetDef _planet;

    // ---- 状態 ----
    private bool _built, _wasQrOpen, _dotIsFront, _tiltOrbitOn = true, _landing;
    private Color _glowColor = SpaceTheme.CYAN, _shownGlow = SpaceTheme.CYAN;
    private float _qrGuardUntil, _nextPoll;
    private int[] _couponSnapshot;
    private DateTime? _checkInTime;
    private long _shownStayMinutes = -1;

    private static UserData Data =>
        UserDataManager.instance != null ? UserDataManager.instance.UserData : null;

    private void Awake()
    {
        SpaceTheme.SetFont(_font);
        // この画面にいる間は、QR読み取り後のお知らせ（InfoModal など旧画面と共用のもの）も宇宙デザインで出す
        SpaceTheme.InUse = true;
        EnsureSceneLoader();
    }

    private void OnDestroy()
    {
        // ログイン画面などへ移ったら共用のお知らせは元のデザインに戻す
        SpaceTheme.InUse = false;
        // 相席募集のリアルタイム購読は新ホームを開いている間だけ
        SpaceRecruitDigest.Changed -= RefreshNewsBadge;
        SpaceRecruitDigest.StopListening();
        SpaceFriendPanel.NameChanged -= RefreshUser;
        SpaceZukan.Changed -= OnZukanChanged;
        StoreVisitors.Changed -= RefreshVisitors;
        StoreVisitors.StopListening();
        StorePasses.Changed -= RefreshBagSub;
    }

    private IEnumerator Start()
    {
        if (_canvas == null) _canvas = SpaceTheme.MainCanvas();
        if (_canvas == null)
        {
            Debug.LogError("[NewHome] Canvas が見つかりません");
            yield break;
        }

        // キャラクターチケットの「使用途中」フラグの後始末（旧Homeの起動時処理と同じ。
        // QRからのキャラクター作成でチケットを誤って消費しないように）
        CharacterTicketService.PendingUse = false;

        // チェックアウト忘れ（営業終了時刻を過ぎた来店）を自動クローズ（旧Homeの起動時処理と同じ）
        if (LocalVisitLog.AutoCloseStaleVisits() > 0)
            PresenceService.SetCheckedInAsync(false).Forget();
        // 入店中の人数の記録（StoreVisitors）を、この端末の入店状況に合わせる:
        // 入店中なのに記録がない（この機能より前に入店した・書き込みに失敗した）なら作り、入店していないのに残っていれば消す
        if (LocalVisitLog.HasOpenVisit())
        {
            if (!StoreVisitors.HasRecord) StoreVisitors.CheckInAsync(SpacePlanets.Current.Id).Forget();
        }
        else if (StoreVisitors.HasRecord)
        {
            StoreVisitors.CheckOutAsync().Forget();
        }

        var canvasRt = (RectTransform)_canvas.transform;
        var size = SpaceTheme.CanvasSize(_canvas);
        var insets = SpaceTheme.SafeInsets(size);
        _background = SpaceBackground.Create(canvasRt, size, insets.top);

        // 中央の惑星の表面テクスチャは裏のスレッドで作るので、文字の登録・UIの組み立てと並行して先に始めておく
        var planet = SpacePlanets.Current;
        SpacePlanets.Load(planet, false).Forget();
        SpacePlanets.Load(planet, true).Forget();

        // 表示する文字を先にフォントへ登録してからUIを組む（TMP動的フォントのOOM対策）
        yield return SpaceTheme.Prewarm(CollectGlyphText());

        _content = SpaceTheme.Rect("__Content", canvasRt);
        SpaceTheme.Stretch(_content);
        _content.gameObject.AddComponent<SpaceSafeArea>();

        BuildHeader();
        BuildUserCard();
        BuildCameraOrb(planet);
        BuildBagButton();
        BuildPlanetButton();
        BuildFriendButton();
        BuildPriceMoon(); // 中央の惑星の輪より手前に描く（輪の上でも押せるように）
        _accountMenu = SpaceAccountMenu.Create(_canvas); // ホームの上に重ねる（右上の歯車）
        _background.EnableMoonButton(OpenNews, SpaceNewsPage.ACCENT); // 左上のピンクの小さな惑星＝お知らせ
        _built = true;

        RefreshUser();
        RefreshVisit();
        PollUserData(false);
        PlayEntrance();
        RefreshAdminAsync().Forget(); // 管理者ならQRコード一覧（クーポン・惑星の解放）のボタンを出す
        StartNewsWatch();             // お知らせ（店の画像・相席募集）に見ていない内容があれば「New」
        SpaceFriendPanel.NameChanged += RefreshUser; // フレンドの画面でユーザー名を変えたら、自分のカードの名前も新しく
        SpaceZukan.Changed += OnZukanChanged;        // 図鑑をダウンロード・削除したら、図鑑の惑星を出し入れ
        StoreVisitors.Changed += RefreshVisitors;    // 入店中の人数ぶんの球（いちばん外の円）
        StoreVisitors.StartListening();
        StorePasses.Changed += RefreshBagSub;        // 1か月パスを読み込んだ・受け取ったら所持品ボタンの枚数を更新
        StorePasses.LoadMineAsync().Forget();

        // 惑星を選ぶ画面の一覧用（小さいテクスチャ）を、登場の演出が落ち着いてから裏で作っておく
        yield return new WaitForSecondsRealtime(1.5f);
        SpacePlanets.PreloadThumbnails().Forget();
    }

    private string CollectGlyphText()
    {
        var sb = new StringBuilder();
        sb.Append(T_WORDMARK).Append(T_WELCOME_BACK).Append(T_SAN).Append(T_VISITS).Append(T_LAST_VISIT)
          .Append(T_STAY).Append(T_IN_STORE).Append(T_CAMERA).Append(T_CAMERA_SUB).Append(T_BAG)
          .Append(T_GUEST).Append(T_GAINED).Append(T_GAINED_MANY).Append(GLYPH_EXTRA)
          .Append(SpaceInventoryPanel.GlyphText).Append(SpaceAccountMenu.GlyphText).Append(SpaceNotice.GlyphText)
          .Append(SpaceQRScreen.GlyphText).Append(SpacePlanets.GlyphText).Append(SpacePlanetPicker.GlyphText)
          .Append(SpaceAdminQrList.GlyphText).Append(SpaceHistoryPanel.GlyphText).Append(SpaceNewsPage.GlyphText)
          .Append(SpaceFriendPanel.GlyphText).Append(StorePasses.GlyphText).Append(SpacePassCard.GlyphText)
          .Append(SpacePassViewer.GlyphText).Append(SpaceAdminPassList.GlyphText).Append(SpaceAdminNewsImages.GlyphText).Append(SpacePricePage.GlyphText).Append(T_BAG_PASS)
          .Append(SpaceBoardGameZukan.GlyphText);
        var d = Data;
        if (d != null && !string.IsNullOrEmpty(d.Username)) sb.Append(d.Username);
        return sb.ToString();
    }

    // ================================================================
    // 画面の組み立て
    // ================================================================
    private void BuildHeader()
    {
        // 左端64〜右上のボタンの手前まで。入りきらなければ文字を小さくする（ふだんは40のまま収まる）
        _headerRt = SpaceTheme.Rect("Header", _content);
        _headerRt.anchorMin = new Vector2(0f, 1f);
        _headerRt.anchorMax = new Vector2(1f, 1f);
        _headerRt.pivot = new Vector2(0f, 0.5f);
        _headerRt.offsetMin = new Vector2(64f, -84f - 35f);
        _headerRt.offsetMax = new Vector2(-HEADER_RIGHT, -84f + 35f);
        _headerGroup = _headerRt.gameObject.AddComponent<CanvasGroup>();
        var t = SpaceTheme.Text(_headerRt, "Wordmark", T_WORDMARK, 40, SpaceTheme.A(SpaceTheme.TEXT, 0.92f),
            FontStyles.Bold, TextAlignmentOptions.Left);
        SpaceTheme.Stretch(t.rectTransform);
        t.characterSpacing = 32f;
        t.enableAutoSizing = true;
        t.fontSizeMin = 28f;
        t.fontSizeMax = 40f;
    }

    /// <summary>ユーザー情報カード（アバター・名前・入店中バッジ・来店回数・入店状況）</summary>
    private void BuildUserCard()
    {
        _cardRt = SpaceTheme.Rect("UserCard", _content);
        SpaceTheme.TopBand(_cardRt, 168f, 336f, 56f, 56f);
        _cardGroup = _cardRt.gameObject.AddComponent<CanvasGroup>();
        SpaceTheme.Glass(_cardRt, 44f);

        // アバター（名前の頭文字）
        var av = SpaceTheme.Rect("Avatar", _cardRt, new Vector2(0f, 1f), new Vector2(110f, -110f), new Vector2(132f, 132f));
        var avGlow = SpaceTheme.Rect("Glow", av, SpaceTheme.CENTER, Vector2.zero, new Vector2(260f, 260f));
        SpaceTheme.Img(avGlow, SpaceSprites.Glow, SpaceTheme.A(SpaceTheme.VIOLET, 0.42f));
        var face = SpaceTheme.Rect("Face", av);
        SpaceTheme.Stretch(face);
        SpaceTheme.Gradient(SpaceTheme.Img(face, SpaceSprites.Circle, Color.white), SpaceTheme.CYAN, SpaceTheme.VIOLET);
        var rim = SpaceTheme.Rect("Rim", av);
        SpaceTheme.Stretch(rim);
        SpaceTheme.Img(rim, SpaceSprites.RingSmall, SpaceTheme.A(Color.white, 0.45f));
        _avatarText = SpaceTheme.Text(av, "Initial", "", 58, SpaceTheme.TEXT, FontStyles.Bold);
        SpaceTheme.Stretch(_avatarText.rectTransform);

        // 名前
        _captionText = SpaceTheme.Text(_cardRt, "Caption", T_WELCOME, 24, SpaceTheme.A(SpaceTheme.CYAN, 0.85f),
            FontStyles.Bold, TextAlignmentOptions.Left);
        SpaceTheme.TopBand(_captionText.rectTransform, 52f, 36f, 200f, 240f);
        _captionText.characterSpacing = 18f;
        _nameText = SpaceTheme.Text(_cardRt, "Name", "", 60, SpaceTheme.TEXT, FontStyles.Bold, TextAlignmentOptions.Left);
        SpaceTheme.TopBand(_nameText.rectTransform, 86f, 80f, 200f, 230f);
        _nameText.enableAutoSizing = true;
        _nameText.fontSizeMin = 34f;
        _nameText.fontSizeMax = 60f;

        // 入店中バッジ（チェックイン中のみ表示）
        var pill = SpaceTheme.Rect("InStore", _cardRt, new Vector2(1f, 1f), new Vector2(-36f, -36f),
            new Vector2(176f, 56f), new Vector2(1f, 1f));
        SpaceTheme.Rounded(pill, SpaceTheme.A(SpaceTheme.MINT, 0.12f), 26f);
        var pillEdge = SpaceTheme.Rect("Edge", pill);
        SpaceTheme.Stretch(pillEdge);
        SpaceTheme.RoundedStroke(pillEdge, SpaceTheme.A(SpaceTheme.MINT, 0.5f), 26f);
        var dotGlow = SpaceTheme.Rect("DotGlow", pill, new Vector2(0f, 0.5f), new Vector2(34f, 0f), new Vector2(40f, 40f));
        _pillGlow = SpaceTheme.Img(dotGlow, SpaceSprites.Glow, SpaceTheme.A(SpaceTheme.MINT, 0.8f));
        var dot = SpaceTheme.Rect("Dot", pill, new Vector2(0f, 0.5f), new Vector2(34f, 0f), new Vector2(16f, 16f));
        SpaceTheme.Img(dot, SpaceSprites.Circle, SpaceTheme.MINT);
        SpaceTheme.Text(pill, "Label", T_IN_STORE, 28, SpaceTheme.MINT, new Vector2(0f, 0.5f), new Vector2(56f, 0f),
            new Vector2(112f, 40f), FontStyles.Bold, TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
        _inStorePill = pill.gameObject;
        _inStorePill.SetActive(false);

        // 区切り線
        var div = SpaceTheme.Rect("Divider", _cardRt);
        SpaceTheme.TopBand(div, 199f, 2f, 44f, 44f);
        SpaceTheme.Img(div, SpaceSprites.FadeLine, SpaceTheme.A(SpaceTheme.LINE, 0.32f));
        var vdiv = SpaceTheme.Rect("DividerV", _cardRt, new Vector2(0.5f, 0f), new Vector2(0f, 66f), new Vector2(80f, 2f));
        vdiv.localEulerAngles = new Vector3(0f, 0f, 90f);
        SpaceTheme.Img(vdiv, SpaceSprites.FadeLine, SpaceTheme.A(SpaceTheme.LINE, 0.28f));

        // 統計（左: 来店回数 / 右: 滞在時間 or 前回の来店）
        _visitValue = StatCell("Visits", 0f, T_VISITS, out _);
        _statusValue = StatCell("Status", 0.5f, T_LAST_VISIT, out _statusLabel);
    }

    private TextMeshProUGUI StatCell(string name, float x0, string label, out TextMeshProUGUI labelText)
    {
        var cell = SpaceTheme.Rect(name, _cardRt);
        cell.anchorMin = new Vector2(x0, 0f);
        cell.anchorMax = new Vector2(x0 + 0.5f, 0f);
        cell.pivot = new Vector2(0.5f, 0f);
        cell.sizeDelta = new Vector2(0f, 120f);
        cell.anchoredPosition = new Vector2(0f, 8f);
        labelText = SpaceTheme.Text(cell, "Label", label, 24, SpaceTheme.TEXT_MUTED, new Vector2(0.5f, 1f),
            new Vector2(0f, -26f), new Vector2(420f, 36f), FontStyles.Bold);
        var value = SpaceTheme.Text(cell, "Value", "", 50, SpaceTheme.TEXT, new Vector2(0.5f, 0f),
            new Vector2(0f, 40f), new Vector2(420f, 70f), FontStyles.Bold);
        value.enableAutoSizing = true;
        value.fontSizeMin = 32f;
        value.fontSizeMax = 50f;
        return value;
    }

    /// <summary>
    /// カメラ起動ボタン。中央の惑星（選んだもの）の周りを、円軌道と傾いた楕円軌道の衛星が回る。
    /// 楕円軌道は奥半分を惑星の後ろ、手前半分を惑星の前に描いて立体感を出す（環のある惑星では楕円軌道は消す）。
    /// 回転・明滅し続けるので専用のサブキャンバスに分けている。
    /// </summary>
    private void BuildCameraOrb(SpacePlanetDef planet)
    {
        // カードの下〜所持品ボタンの上の空き領域の中央に置く
        var area = SpaceTheme.Rect("CameraArea", _content);
        SpaceTheme.Stretch(area, 0f, 260f, 0f, 524f);

        _orbRt = SpaceTheme.Rect("CameraOrb", area, SpaceTheme.CENTER, new Vector2(0f, 48f), new Vector2(760f, 760f));
        _orbGroup = _orbRt.gameObject.AddComponent<CanvasGroup>();
        SpaceTheme.SubCanvas(_orbRt.gameObject, true);

        var glow = SpaceTheme.Rect("Glow", _orbRt, SpaceTheme.CENTER, Vector2.zero, new Vector2(720f, 720f));
        _orbGlow = SpaceTheme.Img(glow, SpaceSprites.Glow, SpaceTheme.A(SpaceTheme.CYAN, 0.27f));

        // いちばん外の円と、入店中の人の球（ほかの軌道・履歴の小さな惑星より奥）
        var outer = SpaceTheme.Rect("VisitorOrbit", _orbRt, SpaceTheme.CENTER, Vector2.zero,
            new Vector2(VISITOR_RADIUS * 2f, VISITOR_RADIUS * 2f));
        SpaceTheme.Img(outer, SpaceSprites.RingThin, SpaceTheme.A(SpaceTheme.LINE, 0.41f));
        _visitorRing = SpaceTheme.Rect("Visitors", _orbRt, SpaceTheme.CENTER, Vector2.zero,
            new Vector2(VISITOR_RADIUS * 2f, VISITOR_RADIUS * 2f));

        var back = OrbitTilt("OrbitTiltBack");
        SpaceTheme.Img(back, SpaceSprites.Ring, SpaceTheme.A(SpaceTheme.LINE, 0.47f));
        _tiltBack = back.gameObject;

        _orbitRing = SpaceTheme.Rect("OrbitRing", _orbRt, SpaceTheme.CENTER, Vector2.zero, new Vector2(560f, 560f));
        SpaceTheme.Img(_orbitRing, SpaceSprites.Ring, SpaceTheme.A(SpaceTheme.LINE, 0.41f));
        var sat = SpaceTheme.Rect("Satellite", _orbitRing, SpaceTheme.CENTER, new Vector2(0f, 280f), new Vector2(44f, 44f));
        SpaceTheme.Img(sat, SpaceSprites.Glow, SpaceTheme.A(SAT_GREEN, 0.8f));
        var satCore = SpaceTheme.Rect("Core", sat, SpaceTheme.CENTER, Vector2.zero, new Vector2(16f, 16f));
        SpaceTheme.Img(satCore, SpaceSprites.Circle, SAT_GREEN_CORE);

        _dotBack = OrbitDot("MoonBack");

        // 押せる範囲（惑星＋下のラベルまで）
        var hit = SpaceTheme.Rect("Button", _orbRt, SpaceTheme.CENTER, new Vector2(0f, -60f), new Vector2(500f, 720f));
        SpaceTheme.Clickable(hit.gameObject, SpaceTheme.HitArea(hit), OnCameraPressed);

        // 中央の惑星（SpacePlanetView を差し替える入れ物）。ルミナのときだけ中央にカメラのアイコンを重ねる
        _orbCore = SpaceTheme.Rect("Core", hit, SpaceTheme.CENTER, new Vector2(0f, 60f), new Vector2(380f, 380f));
        _planetHolder = SpaceTheme.Rect("Planet", _orbCore, SpaceTheme.CENTER, Vector2.zero,
            new Vector2(PLANET_VIEW, PLANET_VIEW));
        _planetHolderGroup = _planetHolder.gameObject.AddComponent<CanvasGroup>();
        _cameraIcon = SpaceTheme.Rect("Icon", _orbCore, SpaceTheme.CENTER, new Vector2(0f, -4f), new Vector2(156f, 156f));
        SpaceTheme.Img(_cameraIcon, SpaceSprites.Camera, Color.white);
        _cameraIconGroup = _cameraIcon.gameObject.AddComponent<CanvasGroup>();

        SpaceTheme.Text(hit, "Title", T_CAMERA, 46, SpaceTheme.TEXT, SpaceTheme.CENTER, new Vector2(0f, -270f),
            new Vector2(700f, 64f), FontStyles.Bold);
        SpaceTheme.Text(hit, "Sub", T_CAMERA_SUB, 28, SpaceTheme.TEXT_SUB, SpaceTheme.CENTER, new Vector2(0f, -328f),
            new Vector2(700f, 40f), FontStyles.Bold);

        // 楕円軌道の手前半分（右→下→左の半周）を惑星の前に重ねる
        var front = OrbitTilt("OrbitTiltFront");
        var frontImg = SpaceTheme.Img(front, SpaceSprites.Ring, SpaceTheme.A(SpaceTheme.Hex(0xBECDFF), 0.69f));
        frontImg.type = Image.Type.Filled;
        frontImg.fillMethod = Image.FillMethod.Radial360;
        frontImg.fillOrigin = (int)Image.Origin360.Right;
        frontImg.fillClockwise = true;
        frontImg.fillAmount = 0.5f;
        _tiltFront = front.gameObject;

        _dotFront = OrbitDot("MoonFront");
        _dotFront.gameObject.SetActive(false);

        BuildCameraBadge();
        BuildHistoryMoon();
        BuildZukanMoon();

        // 惑星を入れ替えた瞬間に広がる光の輪
        _flash = SpaceTheme.Rect("Flash", _orbRt, SpaceTheme.CENTER, Vector2.zero, new Vector2(380f, 380f));
        _flashImg = SpaceTheme.Img(_flash, SpaceSprites.Ring, new Color(1f, 1f, 1f, 0f));
        _flash.gameObject.SetActive(false);

        ApplyPlanet(planet);
        _shownGlow = _glowColor; // 起動時は最初から惑星の色
    }

    /// <summary>惑星の右下に付けるカメラの印（ここも含めて惑星全体がカメラボタン）</summary>
    private void BuildCameraBadge()
    {
        _cameraBadge = SpaceTheme.Rect("CameraBadge", _orbRt, SpaceTheme.CENTER, new Vector2(136f, -136f),
            new Vector2(112f, 112f));
        _badgeGroup = _cameraBadge.gameObject.AddComponent<CanvasGroup>();
        _badgeGroup.blocksRaycasts = false;
        var glow = SpaceTheme.Rect("Glow", _cameraBadge, SpaceTheme.CENTER, Vector2.zero, new Vector2(200f, 200f));
        SpaceTheme.Img(glow, SpaceSprites.Glow, SpaceTheme.A(SpaceTheme.CYAN, 0.35f));
        var face = SpaceTheme.Rect("Face", _cameraBadge);
        SpaceTheme.Stretch(face);
        SpaceTheme.Img(face, SpaceSprites.Circle, new Color(0.05f, 0.07f, 0.19f, 0.92f));
        var rim = SpaceTheme.Rect("Rim", _cameraBadge);
        SpaceTheme.Stretch(rim);
        SpaceTheme.Img(rim, SpaceSprites.RingSmall, SpaceTheme.A(SpaceTheme.CYAN, 0.8f));
        var icon = SpaceTheme.Rect("Icon", _cameraBadge, SpaceTheme.CENTER, new Vector2(0f, -1f), new Vector2(60f, 60f));
        SpaceTheme.Img(icon, SpaceSprites.Camera, Color.white);
    }

    /// <summary>
    /// 履歴ボタン: 惑星の右下に浮かぶ金色の小さな惑星（文字やアイコンは付けない）。
    /// ゆっくり漂い、後ろの光が明滅する（AnimateOrb）。押すと履歴（SpaceHistoryPanel）を開く
    /// </summary>
    private void BuildHistoryMoon()
    {
        var btn = SpaceTheme.Rect("HistoryButton", _orbRt, SpaceTheme.CENTER, HISTORY_POS,
            new Vector2(HISTORY_HIT, HISTORY_HIT));
        var hit = SpaceTheme.HitArea(btn);
        _historyGroup = btn.gameObject.AddComponent<CanvasGroup>();
        _historyMoon = SpaceTheme.Rect("Moon", btn, SpaceTheme.CENTER, Vector2.zero, new Vector2(HISTORY_SIZE, HISTORY_SIZE));
        var glow = SpaceTheme.Rect("Glow", _historyMoon, SpaceTheme.CENTER, Vector2.zero,
            new Vector2(HISTORY_SIZE * 2.2f, HISTORY_SIZE * 2.2f));
        _historyGlow = SpaceTheme.Img(glow, SpaceSprites.Glow, SpaceTheme.A(SpaceHistoryPanel.ACCENT, 0.25f));
        var body = SpaceTheme.Rect("Body", _historyMoon);
        SpaceTheme.Stretch(body);
        SpaceTheme.Img(body, SpaceSprites.GoldMoon, Color.white);
        SpaceTheme.Clickable(btn.gameObject, hit, OpenHistory);
    }

    /// <summary>
    /// ボードゲーム図鑑ボタン（緑の小さな惑星）。履歴の惑星と左右対称の左下に置く。
    /// 文字もアイコンもない、ほかの小さな惑星と同じ見た目。図鑑をダウンロードしていなければ出さない
    /// </summary>
    private void BuildZukanMoon()
    {
        _zukanBtn = SpaceTheme.Rect("ZukanButton", _orbRt, SpaceTheme.CENTER, ZUKAN_POS,
            new Vector2(HISTORY_HIT, HISTORY_HIT));
        var hit = SpaceTheme.HitArea(_zukanBtn);
        _zukanGroup = _zukanBtn.gameObject.AddComponent<CanvasGroup>();
        _zukanMoon = SpaceTheme.Rect("Moon", _zukanBtn, SpaceTheme.CENTER, Vector2.zero, new Vector2(HISTORY_SIZE, HISTORY_SIZE));
        var glow = SpaceTheme.Rect("Glow", _zukanMoon, SpaceTheme.CENTER, Vector2.zero,
            new Vector2(HISTORY_SIZE * 2.2f, HISTORY_SIZE * 2.2f));
        _zukanGlow = SpaceTheme.Img(glow, SpaceSprites.Glow, SpaceTheme.A(ZUKAN_ACCENT, 0.25f));
        var body = SpaceTheme.Rect("Body", _zukanMoon);
        SpaceTheme.Stretch(body);
        SpaceTheme.Img(body, SpaceSprites.GreenMoon, Color.white);
        SpaceTheme.Clickable(_zukanBtn.gameObject, hit, OpenZukan);
        _zukanBtn.gameObject.SetActive(SpaceZukan.Installed);
    }

    /// <summary>
    /// 料金表ボタン（水色の小さな惑星）。中央の惑星の右上（背景のピンクの小さな惑星と左右対称）。
    /// 文字もアイコンもない、ほかの小さな惑星と同じ見た目で、後ろの光がゆっくり明滅する
    /// </summary>
    private void BuildPriceMoon()
    {
        var btn = SpaceTheme.Rect("PriceButton", _content, new Vector2(1f, 1f), PRICE_POS,
            new Vector2(PRICE_HIT, PRICE_HIT));
        var hit = SpaceTheme.HitArea(btn);
        _priceGroup = btn.gameObject.AddComponent<CanvasGroup>();
        _priceMoon = SpaceTheme.Rect("Moon", btn, SpaceTheme.CENTER, Vector2.zero, new Vector2(PRICE_SIZE, PRICE_SIZE));
        var glow = SpaceTheme.Rect("Glow", _priceMoon, SpaceTheme.CENTER, Vector2.zero,
            new Vector2(PRICE_SIZE * 2.2f, PRICE_SIZE * 2.2f));
        _priceGlow = SpaceTheme.Img(glow, SpaceSprites.Glow, SpaceTheme.A(SpacePricePage.ACCENT, 0.25f));
        var body = SpaceTheme.Rect("Body", _priceMoon);
        SpaceTheme.Stretch(body);
        SpaceTheme.Img(body, SpaceSprites.CyanMoon, Color.white);
        SpaceTheme.Clickable(btn.gameObject, hit, OpenPrices);
    }

    /// <summary>図鑑をダウンロード・削除した: 惑星をふくらませて出す／しぼませて消す</summary>
    private void OnZukanChanged()
    {
        if (this == null || _zukanBtn == null) return;
        if (_zukanAnim != null) StopCoroutine(_zukanAnim);
        _zukanAnim = StartCoroutine(SpaceZukan.Installed ? ShowZukanMoon() : HideZukanMoon());
    }

    private IEnumerator ShowZukanMoon()
    {
        _zukanBtn.gameObject.SetActive(true);
        _zukanGroup.alpha = 0f;
        yield return SpaceTheme.Tween(0.6f, 0.2f, k =>
        {
            _zukanGroup.alpha = k;
            float s = Mathf.LerpUnclamped(0.3f, 1f, EaseOutBack(k));
            _zukanMoon.localScale = new Vector3(s, s, 1f);
        });
        // 出たところに光の輪（どこに増えたかわかるように）
        var ring = SpaceTheme.Rect("ZukanFlash", _orbRt, SpaceTheme.CENTER, ZUKAN_POS, new Vector2(HISTORY_SIZE, HISTORY_SIZE));
        var img = SpaceTheme.Img(ring, SpaceSprites.RingSmall, Color.clear);
        yield return SpaceTheme.Tween(0.7f, 0f, k =>
        {
            ring.sizeDelta = Vector2.one * Mathf.Lerp(HISTORY_SIZE, HISTORY_SIZE * 2.6f, k);
            img.color = SpaceTheme.A(ZUKAN_ACCENT, 0.9f * (1f - k));
        });
        Destroy(ring.gameObject);
        _zukanAnim = null;
    }

    private IEnumerator HideZukanMoon()
    {
        float from = _zukanGroup.alpha;
        yield return SpaceTheme.Tween(0.3f, 0f, k =>
        {
            _zukanGroup.alpha = from * (1f - k);
            float s = Mathf.Lerp(1f, 0.6f, k);
            _zukanMoon.localScale = new Vector3(s, s, 1f);
        });
        _zukanBtn.gameObject.SetActive(false);
        _zukanAnim = null;
    }

    /// <summary>ボードゲーム図鑑を開く（宇宙デザイン。できることは旧ホームの図鑑と同じ。ホームの上に全画面で重ねる）</summary>
    private void OpenZukan()
    {
        if (!_built || PanelOpen || _landing || !SpaceZukan.Installed) return;
        _zukan = SpaceBoardGameZukan.Open(_canvas);
    }

    /// <summary>中央の惑星を差し替える（見た目の入れ替えだけ。保存は SpacePlanetPicker が行う）</summary>
    private void ApplyPlanet(SpacePlanetDef def)
    {
        _planet = def;
        if (_planetView != null) Destroy(_planetView.gameObject);
        _planetView = SpacePlanetView.Create(_planetHolder, def, PLANET_VIEW, SpacePlanetView.Quality.Large, true);
        _cameraIcon.gameObject.SetActive(def.IsOrb);
        _cameraBadge.gameObject.SetActive(!def.IsOrb);
        SetTiltOrbit(!def.Ring);
        _glowColor = def.Accent;
    }

    /// <summary>傾いた楕円軌道と紫の衛星の表示（環のある惑星では環と重なるので消す）</summary>
    private void SetTiltOrbit(bool on)
    {
        _tiltOrbitOn = on;
        _tiltBack.SetActive(on);
        _tiltFront.SetActive(on);
        _dotIsFront = false;
        _dotBack.gameObject.SetActive(on);
        _dotFront.gameObject.SetActive(false);
    }

    /// <summary>右上の惑星ボタン（歯車の左）。押すと惑星を選ぶ画面を開く</summary>
    private void BuildPlanetButton()
    {
        var rt = SpaceTheme.Rect("PlanetButton", _content, new Vector2(1f, 1f), new Vector2(-224f, -84f),
            new Vector2(96f, 96f));
        _planetBtnGroup = rt.gameObject.AddComponent<CanvasGroup>();
        var face = SpaceTheme.Img(rt, SpaceSprites.Circle, new Color(0.05f, 0.07f, 0.19f, 0.85f), true);
        var rim = SpaceTheme.Rect("Rim", rt);
        SpaceTheme.Stretch(rim);
        SpaceTheme.Img(rim, SpaceSprites.RingSmall, SpaceTheme.STROKE);
        var icon = SpaceTheme.Rect("Icon", rt, SpaceTheme.CENTER, Vector2.zero, new Vector2(60f, 60f));
        SpaceTheme.Img(icon, SpaceSprites.PlanetIcon, SpaceTheme.TEXT);

        // 新着の点（まだ一度も開いていない／新しく解放した惑星がある。開くと消える）
        var dot = SpaceTheme.Rect("NewDot", rt, SpaceTheme.CENTER, new Vector2(32f, 32f), new Vector2(20f, 20f));
        SpaceTheme.Img(dot, SpaceSprites.Circle, SpaceTheme.PINK);
        _planetBtnDot = dot.gameObject;
        _planetBtnDot.SetActive(SpacePlanets.HasNews);

        SpaceTheme.Clickable(rt.gameObject, face, OpenPicker);
    }

    /// <summary>右上のフレンドボタン（惑星ボタンの左）。押すとフレンドのシートを開く</summary>
    private void BuildFriendButton()
    {
        var rt = SpaceTheme.Rect("FriendButton", _content, new Vector2(1f, 1f), new Vector2(-336f, -84f),
            new Vector2(96f, 96f));
        _friendBtnGroup = rt.gameObject.AddComponent<CanvasGroup>();
        var face = SpaceTheme.Img(rt, SpaceSprites.Circle, new Color(0.05f, 0.07f, 0.19f, 0.85f), true);
        var rim = SpaceTheme.Rect("Rim", rt);
        SpaceTheme.Stretch(rim);
        SpaceTheme.Img(rim, SpaceSprites.RingSmall, SpaceTheme.STROKE);
        var icon = SpaceTheme.Rect("Icon", rt, SpaceTheme.CENTER, new Vector2(1f, -1f), new Vector2(60f, 60f));
        SpaceTheme.Img(icon, SpaceSprites.FriendsIcon, SpaceTheme.TEXT);
        SpaceTheme.Clickable(rt.gameObject, face, OpenFriends);
    }

    /// <summary>
    /// 管理者判定（IDトークンの admin クレーム）が済んだら、管理者にだけ
    /// お客様に見せるQRコードの一覧（クーポン・惑星の解放・1か月パス）を開くボタンと、
    /// その左に1か月パスの購入者の一覧を開くボタン（カードの絵）を出す
    /// </summary>
    private async UniTaskVoid RefreshAdminAsync()
    {
        await AdminClaim.RefreshAsync();
        if (this == null || !AdminClaim.IsAdmin || _adminBtn != null) return;

        // フレンドボタンの左。金色の縁で「店側の道具」とわかるように
        _adminBtn = SpaceTheme.Rect("AdminQrButton", _content, new Vector2(1f, 1f), new Vector2(-448f, -84f),
            new Vector2(96f, 96f));
        var group = _adminBtn.gameObject.AddComponent<CanvasGroup>();
        var face = SpaceTheme.Img(_adminBtn, SpaceSprites.Circle, new Color(0.05f, 0.07f, 0.19f, 0.85f), true);
        var rim = SpaceTheme.Rect("Rim", _adminBtn);
        SpaceTheme.Stretch(rim);
        SpaceTheme.Img(rim, SpaceSprites.RingSmall, SpaceTheme.A(SpaceTheme.GOLD, 0.7f));
        var icon = SpaceTheme.Rect("Icon", _adminBtn, SpaceTheme.CENTER, Vector2.zero, new Vector2(48f, 48f));
        SpaceTheme.Img(icon, SpaceSprites.QrIcon, SpaceTheme.GOLD);
        SpaceTheme.Clickable(_adminBtn.gameObject, face, OpenQrList);
        group.alpha = 0f;
        StartCoroutine(SpaceTheme.Tween(0.4f, 0f, k => group.alpha = k));

        // 右上のボタンが2つ増えるので、見出し（INTALES Cafe）はその手前までに収める
        _headerRt.offsetMax = new Vector2(-HEADER_RIGHT_ADMIN, _headerRt.offsetMax.y);

        // その左: 1か月パスの購入者の一覧（カードの絵）
        _adminPassBtn = SpaceTheme.Rect("AdminPassButton", _content, new Vector2(1f, 1f), new Vector2(-560f, -84f),
            new Vector2(96f, 96f));
        var passGroup = _adminPassBtn.gameObject.AddComponent<CanvasGroup>();
        var passFace = SpaceTheme.Img(_adminPassBtn, SpaceSprites.Circle, new Color(0.05f, 0.07f, 0.19f, 0.85f), true);
        var passRim = SpaceTheme.Rect("Rim", _adminPassBtn);
        SpaceTheme.Stretch(passRim);
        SpaceTheme.Img(passRim, SpaceSprites.RingSmall, SpaceTheme.A(SpaceTheme.GOLD, 0.7f));
        var passIcon = SpaceTheme.Rect("Icon", _adminPassBtn, SpaceTheme.CENTER, Vector2.zero, new Vector2(56f, 56f));
        SpaceTheme.Img(passIcon, SpacePassCard.PassIcon, SpaceTheme.GOLD);
        SpaceTheme.Clickable(_adminPassBtn.gameObject, passFace, OpenPassList);
        passGroup.alpha = 0f;
        StartCoroutine(SpaceTheme.Tween(0.4f, 0.1f, k => passGroup.alpha = k));
    }

    /// <summary>管理者用: 1か月パスの購入者（だれが・どの種類を・いつまで）</summary>
    private void OpenPassList()
    {
        if (PanelOpen || _landing) return;
        _passList = SpaceAdminPassList.Open(_canvas, () => _passList = null);
    }

    private void OpenQrList()
    {
        if (PanelOpen || _landing) return;
        _qrList = SpaceAdminQrList.Open(_canvas, () => _qrList = null);
    }

    /// <summary>傾いた楕円軌道（リングを縦につぶしてから傾ける）</summary>
    private RectTransform OrbitTilt(string name)
    {
        var rt = SpaceTheme.Rect(name, _orbRt, SpaceTheme.CENTER, Vector2.zero,
            new Vector2(ORBIT_RADIUS * 2f, ORBIT_RADIUS * 2f));
        rt.localEulerAngles = new Vector3(0f, 0f, ORBIT_TILT);
        rt.localScale = new Vector3(1f, ORBIT_FLAT, 1f);
        return rt;
    }

    /// <summary>楕円軌道を回る紫の衛星</summary>
    private RectTransform OrbitDot(string name)
    {
        var rt = SpaceTheme.Rect(name, _orbRt, SpaceTheme.CENTER, Vector2.zero, new Vector2(56f, 56f));
        SpaceTheme.Img(rt, SpaceSprites.Glow, SpaceTheme.A(SAT_PURPLE, 0.86f));
        var core = SpaceTheme.Rect("Core", rt, SpaceTheme.CENTER, Vector2.zero, new Vector2(20f, 20f));
        SpaceTheme.Img(core, SpaceSprites.Circle, SAT_PURPLE_CORE);
        return rt;
    }

    /// <summary>所持品ボタン（下部の横長ガラスボタン）</summary>
    private void BuildBagButton()
    {
        _bagRt = SpaceTheme.Rect("BagButton", _content);
        _bagRt.anchorMin = new Vector2(0f, 0f);
        _bagRt.anchorMax = new Vector2(1f, 0f);
        _bagRt.pivot = new Vector2(0.5f, 0f);
        _bagRt.sizeDelta = new Vector2(-112f, 176f);
        _bagRt.anchoredPosition = new Vector2(0f, 64f);
        _bagGroup = _bagRt.gameObject.AddComponent<CanvasGroup>();
        var face = SpaceTheme.Glass(_bagRt, 48f, null, null, true);
        SpaceTheme.Clickable(_bagRt.gameObject, face, OpenInventory);

        var tile = SpaceTheme.Rect("IconTile", _bagRt, new Vector2(0f, 0.5f), new Vector2(32f, 0f),
            new Vector2(112f, 112f), new Vector2(0f, 0.5f));
        SpaceTheme.Gradient(SpaceTheme.Rounded(tile, Color.white, 30f), SpaceTheme.VIOLET, SpaceTheme.PINK);
        var ticket = SpaceTheme.Rect("Ticket", tile, SpaceTheme.CENTER, Vector2.zero, new Vector2(70f, 46f));
        SpaceTheme.Img(ticket, SpaceSprites.Ticket, Color.white);

        SpaceTheme.Text(_bagRt, "Title", T_BAG, 46, SpaceTheme.TEXT, new Vector2(0f, 0.5f), new Vector2(176f, 26f),
            new Vector2(560f, 60f), FontStyles.Bold, TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
        _bagSub = SpaceTheme.Text(_bagRt, "Sub", "", 28, SpaceTheme.TEXT_SUB, new Vector2(0f, 0.5f),
            new Vector2(176f, -28f), new Vector2(560f, 40f), FontStyles.Bold, TextAlignmentOptions.Left,
            new Vector2(0f, 0.5f));

        // ＞
        var chev = SpaceTheme.Rect("Chevron", _bagRt, new Vector2(1f, 0.5f), new Vector2(-64f, 0f), new Vector2(40f, 60f));
        SpaceTheme.Bar(chev, new Vector2(-1f, 11f), 34f, 6f, -45f, SpaceTheme.TEXT_SUB);
        SpaceTheme.Bar(chev, new Vector2(-1f, -11f), 34f, 6f, 45f, SpaceTheme.TEXT_SUB);
    }

    private void PlayEntrance()
    {
        var menuGroup = _accountMenu.TriggerGroup;
        _headerGroup.alpha = _cardGroup.alpha = _orbGroup.alpha = _bagGroup.alpha = menuGroup.alpha = 0f;
        _planetBtnGroup.alpha = _friendBtnGroup.alpha = 0f;
        var cardBase = _cardRt.anchoredPosition;
        var bagBase = _bagRt.anchoredPosition;
        StartCoroutine(SpaceTheme.Tween(0.6f, 0f,
            k => _headerGroup.alpha = menuGroup.alpha = _planetBtnGroup.alpha = _friendBtnGroup.alpha = k));
        StartCoroutine(SpaceTheme.Tween(0.55f, 0.05f, k =>
        {
            _cardGroup.alpha = k;
            _cardRt.anchoredPosition = cardBase + new Vector2(0f, -36f * (1f - k));
        }));
        StartCoroutine(SpaceTheme.Tween(0.7f, 0.15f, k =>
        {
            _orbGroup.alpha = k;
            _orbRt.localScale = Vector3.one * Mathf.Lerp(0.88f, 1f, k);
        }));
        StartCoroutine(SpaceTheme.Tween(0.55f, 0.25f, k =>
        {
            _bagGroup.alpha = k;
            _bagRt.anchoredPosition = bagBase + new Vector2(0f, -36f * (1f - k));
        }));
        // 履歴・図鑑の小さな惑星は中央の惑星より少し遅れて、ふくらむように現れる
        _historyGroup.alpha = 0f;
        _zukanGroup.alpha = 0f;
        _priceGroup.alpha = 0f;
        StartCoroutine(SpaceTheme.Tween(0.6f, 0.55f, k =>
        {
            _historyGroup.alpha = k;
            _zukanGroup.alpha = k;
            _priceGroup.alpha = k;
            float s = Mathf.LerpUnclamped(0.6f, 1f, EaseOutBack(k));
            _historyMoon.localScale = new Vector3(s, s, 1f);
            _zukanMoon.localScale = new Vector3(s, s, 1f);
            _priceMoon.localScale = new Vector3(s, s, 1f);
        }));
    }

    // ================================================================
    // 表示の更新
    // ================================================================
    private void RefreshUser()
    {
        var d = Data;
        string name = SpaceTheme.Safe(d?.Username ?? "").Trim();
        if (string.IsNullOrEmpty(name)) name = T_GUEST;

        string initial = name.Substring(0, 1);
        if (char.IsSurrogate(initial[0])) initial = "★";
        _avatarText.text = SpaceTheme.NoParse(initial.ToUpperInvariant());
        _nameText.text = $"{SpaceTheme.NoParse(name)}<size=56%><color={SpaceTheme.SUB_HEX}> {T_SAN}</color></size>";
    }

    /// <summary>来店記録（端末ローカル）から来店回数と入店状況を表示する</summary>
    private void RefreshVisit()
    {
        var log = LocalVisitLog.Load();
        int count = log.visits != null ? log.visits.Count : 0;
        _visitValue.text = $"{count}<size=55%><color={SpaceTheme.SUB_HEX}> 回</color></size>";
        _captionText.text = count > 0 ? T_WELCOME_BACK : T_WELCOME;

        _checkInTime = null;
        for (int i = count - 1; i >= 0; i--)
        {
            var v = log.visits[i];
            if (v.IsCheckedOut) continue;
            if (TryParseTime(v.checkedInAt, out var t)) _checkInTime = t;
            break; // 直近の未チェックアウトだけを見る（LocalVisitLog.GetOpenCheckInTime と同じ）
        }

        bool inStore = _checkInTime.HasValue;
        _inStorePill.SetActive(inStore);
        if (inStore)
        {
            _statusLabel.text = T_STAY;
            _shownStayMinutes = -1;
            TickStayTime();
        }
        else
        {
            _statusLabel.text = T_LAST_VISIT;
            var latest = count > 0 ? log.visits[count - 1] : null;
            _statusValue.text = latest != null && TryParseTime(latest.checkedInAt, out var last)
                ? last.ToString("M/d", CultureInfo.InvariantCulture)
                : "ー";
        }
    }

    /// <summary>入店中の滞在時間を分単位で更新する</summary>
    private void TickStayTime()
    {
        if (!_checkInTime.HasValue) return;
        long minutes = Math.Max(0L, (long)(DateTime.Now - _checkInTime.Value).TotalMinutes);
        if (minutes == _shownStayMinutes) return;
        _shownStayMinutes = minutes;
        const string small = "<size=55%>";
        _statusValue.text = minutes < 60
            ? $"{minutes}{small}分</size>"
            : $"{minutes / 60}{small}時間</size>{minutes % 60}{small}分</size>";
    }

    /// <summary>
    /// 所持クーポンの枚数を見て、ボタンの表示を更新する。
    /// QRなどで増えていたら「○○を入手しました」と知らせる（減ったとき＝使用時は知らせない）。
    /// </summary>
    private void PollUserData(bool announce)
    {
        var d = Data;
        var now = SpaceCoupons.Snapshot(d);
        if (announce && _couponSnapshot != null)
        {
            var gains = new List<(SpaceCouponDef def, int n)>();
            for (int i = 0; i < now.Length && i < _couponSnapshot.Length; i++)
                if (now[i] > _couponSnapshot[i]) gains.Add((SpaceCoupons.All[i], now[i] - _couponSnapshot[i]));
            Announce(gains);
        }
        _couponSnapshot = now;
        RefreshBagSub();
    }

    /// <summary>所持品ボタンの小さな文字: 「クーポン 5枚」。使える1か月パスがあれば「パス 1枚・クーポン 5枚」</summary>
    private void RefreshBagSub()
    {
        if (this == null || _bagSub == null) return;
        int passes = StorePasses.ActiveCount(DateTime.Now);
        string coupons = $"クーポン {SpaceCoupons.Total(Data)}枚";
        _bagSub.text = passes > 0 ? $"{T_BAG_PASS} {passes}枚・{coupons}" : coupons;
    }

    private void Announce(List<(SpaceCouponDef def, int n)> gains)
    {
        if (gains.Count == 0) return;
        // 引き継ぎ・イベントガチャなどは結果モーダルで告知済みなので重ねない
        if (GameObject.Find("__InfoModal") != null) return;

        string msg = gains.Count == 1 ? $"{gains[0].def.Name}を{gains[0].n}枚入手しました" : T_GAINED_MANY;
        SpaceTheme.Toast(msg, _canvas);
        var assets = AssetsDatabase.instance;
        if (assets != null) assets.PlayLevelUpSE();
        StartCoroutine(PulseBag());
    }

    private IEnumerator PulseBag()
    {
        yield return SpaceTheme.Tween(0.5f, 0f, k =>
        {
            float s = 1f + 0.05f * Mathf.Sin(k * Mathf.PI);
            _bagRt.localScale = new Vector3(s, s, 1f);
        });
        _bagRt.localScale = Vector3.one;
    }

    private static bool TryParseTime(string iso, out DateTime t) =>
        DateTime.TryParse(iso, null, DateTimeStyles.RoundtripKind, out t);

    // ================================================================
    // 操作
    // ================================================================
    /// <summary>ホームの上に何か開いているか（重ねて開かない・カメラも起動しない）</summary>
    private bool PanelOpen => _inventory != null || _history != null || _news != null || _friends != null
                              || _picker != null || _qrList != null || _passList != null || _zukan != null
                              || _prices != null || _accountMenu.IsOpen;

    /// <summary>フレンド（右上の人のアイコン）</summary>
    private void OpenFriends()
    {
        if (PanelOpen || _landing) return;
        _friends = SpaceFriendPanel.Open(_canvas, () => _friends = null);
    }

    private void OnCameraPressed()
    {
        if (PanelOpen || _landing) return;
        if (Time.unscaledTime < _qrGuardUntil || GameObject.FindWithTag("QR") != null) return; // 二重起動防止
        _qrGuardUntil = Time.unscaledTime + 1.5f;
        EnsureSceneLoader();
        // QRシーンは「ルート＋自前のCanvas」だけの構成で、このシーンに加算マージされる（閉じるとルートごと破棄）
        SceneLoader.instance.MergeScene("QR");
    }

    private void OpenInventory()
    {
        if (PanelOpen) return;
        _inventory = SpaceInventoryPanel.Open(_canvas, () =>
        {
            _inventory = null;
            PollUserData(false);
        });
    }

    /// <summary>履歴（金色の小さな惑星）。開くたびに端末の最新の記録を読み直す</summary>
    private void OpenHistory()
    {
        if (PanelOpen || _landing) return;
        _history = SpaceHistoryPanel.Open(_canvas, () => _history = null);
    }

    /// <summary>料金表（右上の水色の小さな惑星）。平日料金・祝日料金の画像を見せる</summary>
    private void OpenPrices()
    {
        if (!_built || PanelOpen || _landing) return;
        if (GameObject.FindWithTag("QR") != null) return; // カメラの画面の上には出さない
        _prices = SpacePricePage.Open(_canvas, () => _prices = null);
    }

    /// <summary>お知らせ（左上のピンクの小さな惑星）。お店の画像と相席募集の概要を開くたびに読み直す</summary>
    private void OpenNews()
    {
        if (!_built || PanelOpen || _landing) return;
        if (GameObject.FindWithTag("QR") != null) return; // カメラの画面の上には出さない
        _background.SetMoonBadge(false); // 開いたら見たことになる（ページ側で見た内容を記録する）
        _news = SpaceNewsPage.Open(_canvas, () =>
        {
            _news = null;
            RefreshNewsBadge();
        });
    }

    /// <summary>
    /// お知らせの「New」の見張りを始める。相席募集はリアルタイム購読（変わった分だけ読み取り）、
    /// お店の画像は起動時に読む設定（master/config。ほかの機能と共用で読み取りは増えない）でくらべる
    /// </summary>
    private void StartNewsWatch()
    {
        SpaceRecruitDigest.Changed += RefreshNewsBadge;
        SpaceRecruitDigest.StartListening();
        LoadNewsUrlsAsync().Forget();
    }

    private async UniTaskVoid LoadNewsUrlsAsync()
    {
        var urls = await SpaceNewsImages.GetUrlsAsync();
        if (this == null) return;
        _newsUrls = urls;
        RefreshNewsBadge();
    }

    /// <summary>見ていない画像・募集があれば、左上のピンクの小さな惑星から「New」の吹き出しを出す</summary>
    private void RefreshNewsBadge()
    {
        if (this == null || !_built || _news != null) return; // お知らせを開いている間は出さない
        // 画像はいまの一覧でくらべる。管理者がお知らせの画像を編集すると設定の一覧が新しいものに差し替わるので、
        // 起動時に受け取った一覧のままだと、消した画像が「まだ見ていない」として残り New が消えなくなる
        var urls = _newsUrls != null ? MasterData.CachedConfig?.EventImages ?? _newsUrls : null;
        bool fresh = SpaceNewsBadge.HasNewImages(urls)
                     || SpaceNewsBadge.HasNewRecruits(SpaceRecruitDigest.Current);
        _background.SetMoonBadge(fresh);
    }

    private void OpenPicker()
    {
        if (PanelOpen || _landing) return;
        SpacePlanets.ClearNews();
        _planetBtnDot.SetActive(false);
        _picker = SpacePlanetPicker.Open(_canvas, _planet, OnPlanetChosen, () => _picker = null);
    }

    /// <summary>選ばれた惑星を、選択画面のプレビューの位置から中央まで飛ばして入れ替える</summary>
    private void OnPlanetChosen(SpacePlanetDef def, SpacePlanetView flying)
    {
        StartCoroutine(LandPlanet(def, flying));
    }

    private IEnumerator LandPlanet(SpacePlanetDef def, SpacePlanetView flying)
    {
        _landing = true;
        ApplyPlanet(def);
        _planetHolderGroup.alpha = 0f;
        _cameraIconGroup.alpha = 0f;
        _badgeGroup.alpha = 0f;

        if (flying != null)
        {
            // 選択画面が消えても残るよう、ルートのキャンバスへ付け替える（見た目の位置・大きさはそのまま）
            var canvasRt = (RectTransform)_canvas.transform;
            var frt = (RectTransform)flying.transform;
            frt.SetParent(canvasRt, true);
            frt.SetAsLastSibling();
            if (frt.GetComponent<Canvas>() == null) SpaceTheme.SubCanvas(frt.gameObject, false); // 自転の再描画を分ける
            Vector3 from = frt.position;
            float fromScale = frt.localScale.x;
            const float duration = 0.75f;
            for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
            {
                float k = Mathf.Clamp01(t / duration);
                k = k < 0.5f ? 4f * k * k * k : 1f - Mathf.Pow(-2f * k + 2f, 3f) / 2f; // easeInOutCubic
                // 行き先は毎フレーム取り直す（中央の惑星は呼吸するように少し拡縮している）
                Vector3 to = _planetHolder.position;
                float toScale = PLANET_VIEW * _planetHolder.lossyScale.x / (flying.Size * canvasRt.lossyScale.x);
                // 少し右にふくらむ弧を描いて降りてくる
                Vector3 ctrl = (from + to) * 0.5f + new Vector3((to - from).magnitude * 0.22f, 0f, 0f);
                frt.position = Vector3.Lerp(Vector3.Lerp(from, ctrl, k), Vector3.Lerp(ctrl, to, k), k);
                float s = Mathf.Lerp(fromScale, toScale, k);
                frt.localScale = new Vector3(s, s, 1f);
                yield return null;
            }
            _planetView.CopyPhase(flying);
            Destroy(flying.gameObject);
        }

        if (flying == null)
        {
            // 飛んでくる惑星がない（解放のお知らせから選んだ）ときは、その場でふくらむように出す
            yield return SpaceTheme.Tween(0.45f, 0f, k =>
            {
                _planetHolderGroup.alpha = k;
                float s = Mathf.LerpUnclamped(0.6f, 1f, EaseOutBack(k));
                _planetHolder.localScale = new Vector3(s, s, 1f);
            });
            _planetHolder.localScale = Vector3.one;
        }
        _planetHolderGroup.alpha = 1f;
        _landing = false;
        StartCoroutine(FlashRing(def.Accent));
        StartCoroutine(SpaceTheme.Tween(0.4f, 0.1f, k =>
        {
            _cameraIconGroup.alpha = _badgeGroup.alpha = k;
            float s = Mathf.LerpUnclamped(0.5f, 1f, EaseOutBack(k));
            _cameraBadge.localScale = new Vector3(s, s, 1f);
        }));
    }

    /// <summary>
    /// いちばん外の円の球を、入店中の人と、それぞれが選んでいる惑星に合わせる（変わったときだけ）。
    /// 球は入店した順に円の上へ等間隔に並べる。新しく来た人の球は膨らんで出て、いなくなった人の球はしぼんで消え、
    /// 残る人の球は新しい位置へすべるように移る。惑星を選び直した人の球は色を変えてぽんと弾む
    /// </summary>
    private void RefreshVisitors()
    {
        if (this == null || !_built || _visitorRing == null) return;
        var now = StoreVisitors.Current();
        if (now.Count > VISITOR_MAX) now.RemoveRange(VISITOR_MAX, now.Count - VISITOR_MAX);
        var sb = new System.Text.StringBuilder();
        foreach (var v in now) sb.Append(v.Id).Append(':').Append(v.Planet).Append(',');
        string key = sb.ToString();
        if (key == _visitorKey) return;
        _visitorKey = key;

        var ids = new HashSet<string>();
        foreach (var v in now) ids.Add(v.Id);
        foreach (var o in _visitorOrbs)
            if (!ids.Contains(o.Id)) o.Leaving = true;

        int n = now.Count;
        for (int i = 0; i < n; i++)
        {
            var v = now[i];
            float target = 90f + 360f * i / n; // いちばん上から反時計回りに
            var o = _visitorOrbs.Find(x => x.Id == v.Id);
            if (o == null)
            {
                o = MakeVisitorOrb(v.Id, target);
                _visitorOrbs.Add(o);
            }
            else if (o.Leaving)
            {
                o.Leaving = false; // しぼんでいる途中で戻ってきた（一時的に一覧から外れていた）
            }
            else if (o.Planet != v.Planet)
            {
                o.Show = Mathf.Min(o.Show, 0.2f); // 色が変わったことがわかるように弾ませる
            }
            o.Target = target;
            if (o.Planet != v.Planet)
            {
                o.Planet = v.Planet;
                var def = SpacePlanets.Find(v.Planet) ?? SpacePlanets.All[0]; // 知らない惑星（新しい版で増えた惑星など）はルミナの色
                o.Glow.color = SpaceTheme.A(def.Accent, 0.85f);
                o.Core.color = Color.Lerp(def.Accent, Color.white, 0.5f);
            }
        }
    }

    private VisitorOrb MakeVisitorOrb(string id, float angle)
    {
        var rt = SpaceTheme.Rect("Visitor", _visitorRing, SpaceTheme.CENTER, Vector2.zero,
            new Vector2(VISITOR_GLOW, VISITOR_GLOW));
        var core = SpaceTheme.Rect("Core", rt, SpaceTheme.CENTER, Vector2.zero, new Vector2(VISITOR_CORE, VISITOR_CORE));
        var o = new VisitorOrb
        {
            Id = id, Rt = rt, Angle = angle, Target = angle,
            Glow = SpaceTheme.Img(rt, SpaceSprites.Glow, Color.clear),
            Core = SpaceTheme.Img(core, SpaceSprites.Circle, Color.clear),
        };
        PlaceVisitorOrb(o);
        return o;
    }

    /// <summary>球を毎フレーム動かす（並びの移動・出入りの膨らみ／しぼみ）</summary>
    private void AnimateVisitors(float dt)
    {
        // 入店のQRを読んだ直後の自分の球は、飛んでいく惑星が着くまで出さない（ほかの人の球は先に間をあける）
        string held = _arrivalFlying ? _arrivalId : StoreVisitors.ArrivalId;
        for (int i = _visitorOrbs.Count - 1; i >= 0; i--)
        {
            var o = _visitorOrbs[i];
            o.Angle = Mathf.LerpAngle(o.Angle, o.Target, Mathf.Clamp01(dt * 3f));
            bool hidden = o.Leaving || (held != null && o.Id == held);
            o.Show = Mathf.MoveTowards(o.Show, hidden ? 0f : 1f, dt * (o.Leaving ? 2.5f : 1.8f));
            if (o.Leaving && o.Show <= 0f)
            {
                Destroy(o.Rt.gameObject);
                _visitorOrbs.RemoveAt(i);
                continue;
            }
            PlaceVisitorOrb(o);
        }
    }

    private void PlaceVisitorOrb(VisitorOrb o)
    {
        float r = o.Angle * Mathf.Deg2Rad;
        o.Rt.anchoredPosition = new Vector2(Mathf.Cos(r), Mathf.Sin(r)) * VISITOR_RADIUS;
        float s = o.Leaving ? o.Show * o.Show : EaseOutBack(o.Show);
        o.Rt.localScale = new Vector3(s, s, 1f);
    }

    // ================================================================
    // 入店したときの演出（自分の惑星がいちばん外の円へ飛んでいく）
    // ================================================================
    /// <summary>
    /// 入店のQRを読んだら（StoreVisitors.ArrivalId）、QR画面・来店ボーナスのお知らせ・ほかの画面が閉じて
    /// ホームが見えてから少し間をおいて、自分の惑星を飛ばす。読んでから時間が経ちすぎたら飛ばさない（球は普通に出る）
    /// </summary>
    private void UpdateArrival(bool qrOpen)
    {
        if (_arrivalFlying) return;
        string id = StoreVisitors.ArrivalId;
        bool covered = qrOpen || _landing || PanelOpen || SpaceNotice.AnyOpen || CallMethodFromQR.CheckInBonusPending;
        if (id == null || covered)
        {
            _arrivalReadyAt = -1f;
            return;
        }
        if (_arrivalReadyAt < 0f) _arrivalReadyAt = Time.unscaledTime + ARRIVAL_DELAY;
        if (Time.unscaledTime < _arrivalReadyAt) return;
        _arrivalReadyAt = -1f;
        StoreVisitors.ConsumeArrival();
        StartCoroutine(FlyArrival(id));
    }

    /// <summary>
    /// 自分の惑星（一覧のサムネイルと同じ軽い表示）が中央の惑星からぽんと飛び出し、円の回る向きに回り込みながら
    /// 遠ざかるように小さくなって、いちばん外の円の自分の場所へ着く。通ったあとには光の尾が残る。
    /// 着いたら光の輪が広がり、円の上の自分の球が膨らんで出る（ほかの人の球は飛んでいる間に間をあけている）
    /// </summary>
    private IEnumerator FlyArrival(string id)
    {
        _arrivalFlying = true;
        _arrivalId = id;
        var def = SpacePlanets.Current;
        var trailColor = Color.Lerp(def.Accent, Color.white, 0.35f);

        // 尾 → 惑星のまわりの光 → 惑星の順に重ねる（いちばん手前の層）
        var holder = SpaceTheme.Rect("Arrival", _orbRt, SpaceTheme.CENTER, Vector2.zero, Vector2.zero);
        var trailLayer = SpaceTheme.Rect("Trail", holder, SpaceTheme.CENTER, Vector2.zero, Vector2.zero);
        var glowRt = SpaceTheme.Rect("Glow", holder, SpaceTheme.CENTER, Vector2.zero, Vector2.one);
        var glowImg = SpaceTheme.Img(glowRt, SpaceSprites.Glow, Color.clear);
        float frame0 = ARRIVAL_SPHERE0 / SpacePlanetView.SPHERE_RATIO;
        var view = SpacePlanetView.Create(holder, def, frame0, SpacePlanetView.Quality.Thumb, false);
        var viewRt = (RectTransform)view.transform;
        var viewGroup = view.GetComponent<CanvasGroup>();
        viewRt.localScale = Vector3.zero;

        var trail = new List<TrailDot>();
        float nextTrail = 0f;
        int trailCount = 0;
        bool landed = false;
        for (float t = 0f; ; t += Time.unscaledDeltaTime)
        {
            float p = Mathf.Clamp01(t / ARRIVAL_DURATION);
            if (p < 1f)
            {
                // 行き先は毎フレーム取り直す（円は回っていて、自分の場所もほかの人の出入りで少し動く）
                float target = ArrivalTargetAngle(id);
                float r = Mathf.Lerp(ARRIVAL_R0, VISITOR_RADIUS, EaseOutCubic(p));
                float ang = (target - ARRIVAL_SWEEP * (1f - EaseInOutSine(p))) * Mathf.Deg2Rad;
                var pos = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r;
                float sphere = Mathf.Lerp(ARRIVAL_SPHERE0, ARRIVAL_SPHERE1, p * p)
                               * EaseOutBack(Mathf.Clamp01(p / 0.15f)); // 飛び出すときはぽんと膨らむ
                float alpha = p < 0.85f ? 1f : 1f - (p - 0.85f) / 0.15f;
                viewRt.anchoredPosition = pos;
                float s = sphere / ARRIVAL_SPHERE0;
                viewRt.localScale = new Vector3(s, s, 1f);
                if (viewGroup != null) viewGroup.alpha = alpha;
                glowRt.anchoredPosition = pos;
                glowRt.sizeDelta = Vector2.one * Mathf.Max(1f, sphere * 1.6f);
                glowImg.color = SpaceTheme.A(def.Accent, 0.45f * alpha);

                // 光の尾（ところどころに白い小さな光）
                if (t >= nextTrail)
                {
                    nextTrail = t + TRAIL_STEP;
                    trailCount++;
                    trail.Add(NewTrailDot(trailLayer, pos, Mathf.Max(8f, sphere * 0.75f), t, alpha, false));
                    if (trailCount % 3 == 0)
                    {
                        var jitter = new Vector2(Mathf.Sin(trailCount * 2.1f), Mathf.Cos(trailCount * 1.7f)) * 9f;
                        trail.Add(NewTrailDot(trailLayer, pos + jitter, 40f, t, 1f, true));
                    }
                }
            }
            else
            {
                viewRt.gameObject.SetActive(false);
                glowRt.gameObject.SetActive(false);
            }

            // 着く少し前に、円の上の自分の球を膨らませ始める（惑星はそこへ溶け込むように消える）
            if (!landed && p >= 0.88f)
            {
                landed = true;
                _arrivalId = null;
                StartCoroutine(ArrivalFlash(id, def.Accent));
            }

            for (int i = trail.Count - 1; i >= 0; i--)
            {
                var d = trail[i];
                float age = (t - d.Born) / TRAIL_LIFE;
                if (age >= 1f)
                {
                    Destroy(d.Rt.gameObject);
                    trail.RemoveAt(i);
                    continue;
                }
                float size = d.Spark ? d.Size * (1f - age) : d.Size * (1f - 0.6f * age);
                d.Rt.sizeDelta = Vector2.one * Mathf.Max(1f, size);
                d.Img.color = d.Spark
                    ? new Color(1f, 1f, 1f, 0.9f * (1f - age))
                    : SpaceTheme.A(trailColor, d.Alpha * (1f - age));
            }
            if (p >= 1f && trail.Count == 0) break;
            yield return null;
        }
        Destroy(holder.gameObject);
        _arrivalFlying = false;
    }

    /// <summary>光の尾の1粒（Spark＝ところどころの白い小さな光）</summary>
    private sealed class TrailDot
    {
        public RectTransform Rt;
        public Image Img;
        public float Born, Size, Alpha;
        public bool Spark;
    }

    private static TrailDot NewTrailDot(RectTransform layer, Vector2 pos, float size, float born, float alpha, bool spark)
    {
        var rt = SpaceTheme.Rect(spark ? "Spark" : "Dot", layer, SpaceTheme.CENTER, pos, Vector2.one * size);
        var img = SpaceTheme.Img(rt, SpaceSprites.Glow, Color.clear);
        return new TrailDot { Rt = rt, Img = img, Born = born, Size = size, Alpha = alpha, Spark = spark };
    }

    /// <summary>自分の球の今の向き（度。中央の惑星から見て、円の回転込み）。球が無ければ円のいちばん上</summary>
    private float ArrivalTargetAngle(string id)
    {
        var orb = _visitorOrbs.Find(o => o.Id == id && !o.Leaving);
        return orb != null ? _visitorRing.localEulerAngles.z + orb.Angle : 90f;
    }

    /// <summary>着いた場所に光の輪が広がる（円といっしょに回るよう、円の子にする）</summary>
    private IEnumerator ArrivalFlash(string id, Color color)
    {
        var orb = _visitorOrbs.Find(o => o.Id == id && !o.Leaving);
        float local = orb != null ? orb.Angle : 90f - _visitorRing.localEulerAngles.z; // 球が無ければ円のいちばん上
        var pos = new Vector2(Mathf.Cos(local * Mathf.Deg2Rad), Mathf.Sin(local * Mathf.Deg2Rad)) * VISITOR_RADIUS;
        var rt = SpaceTheme.Rect("ArrivalFlash", _visitorRing, SpaceTheme.CENTER, pos, new Vector2(40f, 40f));
        var img = SpaceTheme.Img(rt, SpaceSprites.RingSmall, Color.clear);
        yield return SpaceTheme.Tween(0.55f, 0f, k =>
        {
            rt.sizeDelta = Vector2.one * Mathf.Lerp(40f, 170f, EaseOutCubic(k));
            img.color = SpaceTheme.A(color, 0.9f * (1f - k));
        });
        Destroy(rt.gameObject);
    }

    private static float EaseOutCubic(float x) => 1f - Mathf.Pow(1f - x, 3f);
    private static float EaseInOutSine(float x) => -(Mathf.Cos(Mathf.PI * x) - 1f) / 2f;

    private IEnumerator FlashRing(Color color)
    {
        _flash.gameObject.SetActive(true);
        yield return SpaceTheme.Tween(0.7f, 0f, k =>
        {
            float s = Mathf.Lerp(1f, 1.75f, k);
            _flash.localScale = new Vector3(s, s, 1f);
            _flashImg.color = SpaceTheme.A(color, 0.8f * (1f - k));
        });
        _flash.gameObject.SetActive(false);
    }

    private static float EaseOutBack(float x)
    {
        const float c1 = 1.70158f, c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f);
    }

    private void EnsureSceneLoader()
    {
        if (SceneLoader.instance == null) gameObject.AddComponent<SceneLoader>();
    }

    // ================================================================
    // 毎フレーム
    // ================================================================
    private void Update()
    {
        if (!_built) return;

        AnimateOrb();

        // QR画面が閉じたら表示を更新（来店はすぐ、クーポンは結果モーダルの有無を見てから）
        bool qrOpen = GameObject.FindWithTag("QR") != null;
        if (_wasQrOpen && !qrOpen)
        {
            RefreshVisit();
            _nextPoll = Time.unscaledTime + 0.6f;
        }
        _wasQrOpen = qrOpen;

        // 入店のQRを読んだら、ホームが見えてから自分の惑星をいちばん外の円へ飛ばす
        UpdateArrival(qrOpen);

        // QR画面・惑星を選ぶ画面・お知らせで隠れている間は中央の惑星の自転を止める（毎フレームの再描画を省く）
        if (_planetView != null)
            _planetView.Animate = !qrOpen && _news == null && _zukan == null && _prices == null
                                  && (_picker == null || _landing);

        // 惑星を解放したお知らせで「この惑星にする」が押されたら、中央の惑星を入れ替える
        // （選ぶ画面からの決定は OnPlanetChosen が先に _planet を変えるので、ここでは何もしない）
        if (!_landing && _picker == null)
        {
            var current = SpacePlanets.Current;
            if (current != _planet) StartCoroutine(LandPlanet(current, null));
        }
        // 惑星を解放したら惑星ボタンに新着の点
        if (_planetBtnDot.activeSelf != SpacePlanets.HasNews) _planetBtnDot.SetActive(SpacePlanets.HasNews);

        if (Time.unscaledTime >= _nextPoll)
        {
            _nextPoll = Time.unscaledTime + 1f;
            PollUserData(true);
            TickStayTime();
            RefreshVisitors(); // 営業終了時刻を過ぎた入店（退店し忘れ）は、新しい知らせがなくても外す
        }

        if (BackPressed())
        {
            // 手前から閉じる: QR読み取り後のお知らせ → QRコード一覧（管理者）→ 惑星を選ぶ画面 → 所持品 → 履歴
            // → お知らせページ → 設定メニュー
            if (SpaceNotice.CloseTop()) { }
            else if (_zukan != null) _zukan.HandleBack();
            else if (_prices != null) _prices.HandleBack();
            else if (_qrList != null) _qrList.HandleBack();
            else if (_passList != null) _passList.HandleBack();
            else if (_picker != null) _picker.HandleBack();
            else if (_inventory != null) _inventory.HandleBack();
            else if (_history != null) _history.HandleBack();
            else if (_news != null) _news.HandleBack();
            else if (_friends != null) _friends.HandleBack();
            else _accountMenu.HandleBack();
        }

#if UNITY_EDITOR
        if (DebugTogglePressed())
        {
            // エディタ専用: N キーで入退店（QRの "checkin" と同じ処理）
            var caller = GetComponent<CallMethodFromQR>();
            if (caller == null) caller = gameObject.AddComponent<CallMethodFromQR>();
            caller.ToggleVisit();
            RefreshVisit();
        }
#endif
    }

    private void AnimateOrb()
    {
        float t = Time.unscaledTime;
        _orbitRing.localEulerAngles = new Vector3(0f, 0f, -t * 9f);
        _visitorRing.localEulerAngles = new Vector3(0f, 0f, t * 5f); // 入店中の人の球は、緑の衛星と逆向きにゆっくり
        AnimateVisitors(Time.unscaledDeltaTime);
        // 後ろの光は惑星の色へゆっくり移る
        _shownGlow = Color.Lerp(_shownGlow, _glowColor, Mathf.Clamp01(Time.unscaledDeltaTime * 3f));
        _orbGlow.color = SpaceTheme.A(_shownGlow, 0.22f + 0.07f * Mathf.Sin(t * 1.6f));
        float breathe = 1f + 0.015f * Mathf.Sin(t * 1.2f);
        _orbCore.localScale = new Vector3(breathe, breathe, 1f);
        if (_inStorePill.activeSelf)
            _pillGlow.color = SpaceTheme.A(SpaceTheme.MINT, 0.45f + 0.4f * (0.5f + 0.5f * Mathf.Sin(t * 3.2f)));

        // 履歴の小さな惑星: 背景の小さな惑星と同じくらいゆっくり漂い、後ろの光が明滅する
        _historyMoon.anchoredPosition = new Vector2(Mathf.Sin(t * 0.23f + 2f) * 5f, Mathf.Sin(t * 0.37f) * 7f);
        _historyGlow.color = SpaceTheme.A(SpaceHistoryPanel.ACCENT, 0.18f + 0.14f * (0.5f + 0.5f * Mathf.Sin(t * 1.4f)));
        // 料金表の小さな惑星も同じように（背景のピンクの惑星と同じくらいの揺れ）
        _priceMoon.anchoredPosition = new Vector2(Mathf.Sin(t * 0.21f + 4f) * 5f, Mathf.Sin(t * 0.31f + 2.6f) * 5f);
        _priceGlow.color = SpaceTheme.A(SpacePricePage.ACCENT, 0.18f + 0.14f * (0.5f + 0.5f * Mathf.Sin(t * 1.3f + 2f)));
        if (_zukanBtn.gameObject.activeSelf)
        {
            _zukanMoon.anchoredPosition = new Vector2(Mathf.Sin(t * 0.27f + 0.7f) * 5f, Mathf.Sin(t * 0.33f + 1.9f) * 7f);
            _zukanGlow.color = SpaceTheme.A(ZUKAN_ACCENT, 0.18f + 0.14f * (0.5f + 0.5f * Mathf.Sin(t * 1.25f + 1f)));
        }

        if (!_tiltOrbitOn) return;

        // 楕円軌道の衛星: 下半分（sin<0）が手前。手前のときは惑星の前の層に出す
        float ang = t * 0.55f;
        float ex = Mathf.Cos(ang) * ORBIT_RADIUS;
        float ey = Mathf.Sin(ang) * ORBIT_RADIUS * ORBIT_FLAT;
        float tilt = ORBIT_TILT * Mathf.Deg2Rad;
        var p = new Vector2(ex * Mathf.Cos(tilt) - ey * Mathf.Sin(tilt), ex * Mathf.Sin(tilt) + ey * Mathf.Cos(tilt));
        bool front = Mathf.Sin(ang) < 0f;
        if (front != _dotIsFront)
        {
            _dotIsFront = front;
            _dotFront.gameObject.SetActive(front);
            _dotBack.gameObject.SetActive(!front);
        }
        var dot = front ? _dotFront : _dotBack;
        dot.anchoredPosition = p;
        float depth = 0.5f - 0.5f * Mathf.Sin(ang); // 1=最も手前
        float s = Mathf.Lerp(0.7f, 1.1f, depth);
        dot.localScale = new Vector3(s, s, 1f);
    }

    private void OnApplicationFocus(bool focus)
    {
        // バックグラウンドから戻ったら滞在時間・入店状況を追いつかせる
        if (focus && _built) RefreshVisit();
    }

    private static bool BackPressed()
    {
        bool pressed = false;
#if ENABLE_INPUT_SYSTEM
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb != null && kb.escapeKey.wasPressedThisFrame) pressed = true;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKeyDown(KeyCode.Escape)) pressed = true;
#endif
        return pressed;
    }

#if UNITY_EDITOR
    private static bool DebugTogglePressed()
    {
        bool pressed = false;
#if ENABLE_INPUT_SYSTEM
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb != null && kb.nKey.wasPressedThisFrame) pressed = true;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKeyDown(KeyCode.N)) pressed = true;
#endif
        return pressed;
    }
#endif
}
