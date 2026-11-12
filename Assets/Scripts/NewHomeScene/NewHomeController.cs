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
///   ・フレンド機能は 2026-11-09 にやめた（争いの火種になりうるため。ユーザー名の変更は歯車メニューへ移した）
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
    // 見出しの右端（右上のボタンの左端から24の余白）。管理者はボタンが3つ多いので、入りきらない分だけ文字を小さくする
    // 見出し（INTALES Cafe）の右端。フレンドボタンをなくしたので詰めた（2026-11-09）
    private const float HEADER_RIGHT = 224f + 48f + 24f;       // 惑星ボタンの左
    private const float HEADER_RIGHT_ADMIN = 560f + 48f + 24f; // 管理者: お知らせ通知のボタンの左（2026-11-10）
    private const string T_WELCOME = "WELCOME";            // 初来店前
    private const string T_WELCOME_BACK = "WELCOME BACK";  // 来店したことがある
    private const string T_SAN = "さん";
    private const string T_VISITS = "来店回数";
    private const string T_ORBIT = "公転";
    private const string T_STREAK_UNIT = " 連続";
    private const string T_LAST_VISIT = "前回の来店";
    private const string T_STAY = "滞在時間";
    private const string T_IN_STORE = "入店中";
    private const string T_CAMERA = "カメラを起動";
    private const string T_CAMERA_SUB = "QRコードを読み取る";
    // 小さな惑星の名前（中央の惑星の「カメラを起動」のように、惑星のすぐ下に出す。2026-11-06 ユーザー指定）
    private const string T_MOON_HISTORY = "履歴";
    private const string T_MOON_ZUKAN = "ボードゲーム図鑑";
    private const string T_MOON_PRICE = "料金表";
    private const string T_MOON_NEWS = "お知らせ";
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

    // ボードゲーム図鑑ボタン（緑の小さな惑星）: 履歴の惑星と左右対称の左下。初めからだれにでも出す（2026-11-11）
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
    private const float ARRIVAL_SPHERE1 = 34f;
    private const float AWAY_HIDE = 0.22f;        // 入店で飛び出すとき、中央の惑星が消えるまでの割合（飛ぶ時間に対して）
    private const float RETURN_DURATION = 1.2f;   // 退店して円から中央へ戻る時間    // 〃（着く直前。円の上の球より少し大きい）
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
    private CanvasGroup _headerGroup, _cardGroup, _orbGroup, _bagGroup, _planetBtnGroup;
    private RectTransform _headerRt;
    private CanvasGroup _planetHolderGroup, _cameraIconGroup, _badgeGroup, _historyGroup;
    private TextMeshProUGUI _avatarText, _captionText, _nameText, _visitValue, _statusLabel, _statusValue, _bagSub;
    private TextMeshProUGUI _orbitValue;  // ユーザーカードの「公転」（続いている数）
    private SpaceOrbitPanel _orbitPanel;  // 公転の画面（開いている間）
    private SpaceSnow _snow;              // 冬にグラシアの人が入店中なら、店内の円に雪
    private GameObject _inStorePill, _tiltBack, _tiltFront, _planetBtnDot;
    private Image _pillGlow, _orbGlow, _flashImg, _historyGlow;
    private RectTransform _priceMoon;   // 料金表の水色の小さな惑星（漂う絵）
    private CanvasGroup _priceGroup;
    private Image _priceGlow;
    private SpacePricePage _prices;     // 料金表（開いている間）
    private SpaceInventoryPanel _inventory;
    private SpaceHistoryPanel _history;
    private SpaceNewsPage _news;
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
        public Image Disk;            // ノワールの人の球だけ、光の円盤をまとう
        public float Angle, Target;   // 円の上の位置（度。円ごと回すので、円の中での角度）
        public float Show;            // 0→1 で膨らんで出る、いなくなったら 1→0 でしぼんで消える
        public bool Leaving;
    }
    private List<string> _newsUrls; // お店のお知らせ画像の一覧（起動時に1回。読めたかの目印。判定は MasterData の今の一覧で行う）
    private SpaceAccountMenu _accountMenu;
    private SpacePlanetPicker _picker;
    private SpaceAdminQrList _qrList;
    private SpaceAdminPassList _passList;   // 管理者用: 1か月パスの購入者の一覧
    private SpaceNoticeComposer _composer;  // 管理者用: お知らせ通知を送る画面
    private SpaceStarterPicker _starter;    // 新しく登録した人: 最初の惑星を選ぶ画面（選ぶまで閉じない。2026-11-10）
    private bool _planetFaded;              // 中央の惑星を「力を失った」淡い色で描いているか（2026-11-10）
    // INTALES WORLD（2026-11-10。ダウンロードした人だけ、所持品の下にボタン）
    private SpaceWorldMap _world;
    private RectTransform _worldRt, _cameraArea;
    private CanvasGroup _worldGroup;
    private TextMeshProUGUI _worldSub;
    // ボタンの高さは所持品ボタン（176）と同じ（2026-11-10 ユーザー指定「大きさを一緒に」）
    private const float WORLD_H = 176f, WORLD_GAP = 20f, BAG_Y = 64f, CAMERA_BOTTOM = 260f;
    private const float CAMERA_TOP = 524f; // 中央の惑星の領域の上（セーフエリアの上端からの距離。カードの下）
    private const float ORB_Y = 48f;       // 中央の惑星の、領域のまん中からのずれ
    private const string T_WORLD_PLACED = "今日はコマを置きました";
    private const string T_WORLD_READY = "今日のコマを1体置けます";
    private const string T_WORLD_NOT_IN = "入店中にコマを置けます";
    private SpaceBoardGameZukan _zukan;      // ボードゲーム図鑑（開いている間）
    private RectTransform _zukanBtn, _zukanMoon;
    private CanvasGroup _zukanGroup;
    private Image _zukanGlow;
    private RectTransform _adminPassBtn;
    private RectTransform _adminBtn;
    private RectTransform _adminNoticeBtn;
    private SpacePlanetView _planetView;
    private SpacePlanetDef _planet;

    // ---- 状態 ----
    private bool _built, _wasQrOpen, _dotIsFront, _tiltOrbitOn = true, _landing;
    // 入店中は中央の惑星をいちばん外の円に出しているので、中央から消す（2026-11-07 ユーザー指定）。
    // 退店したら円から中央へ戻ってくる（FlyReturn）
    private bool _planetAway, _returning;
    private float _returnReadyAt = -1f;
    private float _selfAngle = 90f; // 円の上の自分の球の角度（円の回転を含まない。戻るときの出発点）
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
        SpaceRenameDialog.NameChanged -= RefreshUser;
        SpaceWorld.Changed -= OnWorldChanged;
        SpaceWorld.StopListening();
        StoreVisitors.Changed -= RefreshVisitors;
        StoreVisitors.StopListening();
        StorePasses.Changed -= RefreshBagSub;
        JourneyService.Changed -= OnJourneyChanged;
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
        // 押せる星座（さいころ座・金貨座・宝石座）の置き場を、星座を並べる前に渡す（あとで決まった場所から跳ばないように）
        UpdateTapBand(SpaceWorld.Installed ? WORLD_H + WORLD_GAP : 0f);
        _background.EnableConstellations(); // 完成した星座を空に並べる

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
        BuildWorldButton();
        LayoutWorld();
        // コインプッシャーの、前に送れなかった増減（遊んでいる途中でアプリを閉じたなど）を送る（2026-11-12）
        CoinPusher.FlushAsync().Forget();
        BuildPlanetButton();
        BuildPriceMoon(); // 中央の惑星の輪より手前に描く（輪の上でも押せるように）
        _accountMenu = SpaceAccountMenu.Create(_canvas); // ホームの上に重ねる（右上の歯車）
        _background.EnableMoonButton(OpenNews, SpaceNewsPage.ACCENT); // 左上のピンクの小さな惑星＝お知らせ
        _background.SetMoonLabel(T_MOON_NEWS);
        _built = true;

        RefreshUser();
        RefreshVisit();
        PollUserData(false);
        PlayEntrance();
        RefreshAdminAsync().Forget(); // 管理者ならQRコード一覧（クーポン・惑星の解放）のボタンを出す
        StartNewsWatch();             // お知らせ（店の画像・相席募集）に見ていない内容があれば「New」
        SpaceRenameDialog.NameChanged += RefreshUser; // 歯車メニューでユーザー名を変えたら、自分のカードの名前も新しく
        SpaceWorld.Changed += OnWorldChanged;        // INTALES WORLD をダウンロード・削除したら、ボタンを出し入れ
        StoreVisitors.Changed += RefreshVisitors;    // 入店中の人数ぶんの球（いちばん外の円）
        StoreVisitors.StartListening();
        // 営業カレンダー（臨時休業など）。読めたらパスの期限と「明日は臨時休業です」の予約に反映される（2026-11-10）
        ShopCalendar.LoadAsync().Forget();
        AppNotifications.Start(); // お店からの通知の購読と、公転・誕生月の通知の予約（2026-11-07）
        StorePasses.Changed += RefreshBagSub;        // 1か月パスを読み込んだ・受け取ったら所持品ボタンの枚数を更新
        StorePasses.LoadMineAsync().Forget();
        JourneyService.Changed += OnJourneyChanged;  // 入店・退店で公転・来店回数が変わったら表示を更新
        // 入店中にフレンドから届いた「一緒に来店」などがあれば処理する（流れ星・新しい惑星のお知らせ）
        JourneyService.ProcessAndReport(r => SpaceNotice.ShowJourneyRewards(r, true)).Forget();
        // ダイスチャレンジで振ったのに送れていない今日の出目があれば送る（ダイスの画面を途中で閉じたときなど。2026-11-11）
        DiceChallenge.SubmitPendingAsync().Forget();

        // 惑星を選ぶ画面の一覧用（小さいテクスチャ）を、登場の演出が落ち着いてから裏で作っておく
        yield return new WaitForSecondsRealtime(1.5f);
        SpacePlanets.PreloadThumbnails().Forget();
    }

    private string CollectGlyphText()
    {
        var sb = new StringBuilder();
        sb.Append(T_WORDMARK).Append(T_WELCOME_BACK).Append(T_SAN).Append(T_VISITS).Append(T_LAST_VISIT)
          .Append(T_STAY).Append(T_IN_STORE).Append(T_CAMERA).Append(T_CAMERA_SUB).Append(T_BAG)
          .Append(T_MOON_HISTORY).Append(T_MOON_ZUKAN).Append(T_MOON_PRICE).Append(T_MOON_NEWS)
          .Append(T_GUEST).Append(T_GAINED).Append(T_GAINED_MANY).Append(GLYPH_EXTRA)
          .Append(SpaceInventoryPanel.GlyphText).Append(SpaceAccountMenu.GlyphText).Append(SpaceNotice.GlyphText)
          .Append(SpaceQRScreen.GlyphText).Append(SpacePlanets.GlyphText).Append(SpacePlanetPicker.GlyphText)
          .Append(SpaceAdminQrList.GlyphText).Append(SpaceHistoryPanel.GlyphText).Append(SpaceNewsPage.GlyphText)
          .Append(SpaceRenameDialog.GlyphText).Append(StorePasses.GlyphText).Append(SpacePassCard.GlyphText)
          .Append(SpacePassViewer.GlyphText).Append(SpaceAdminPassList.GlyphText).Append(SpaceAdminNewsImages.GlyphText).Append(SpacePricePage.GlyphText).Append(T_BAG_PASS)
          .Append(SpaceNoticeComposer.GlyphText).Append(SpaceStarterPicker.GlyphText)
          .Append(SpaceWorldMap.GlyphText).Append(T_WORLD_PLACED).Append(T_WORLD_READY).Append(T_WORLD_NOT_IN)
          .Append(SpaceBoardGameZukan.GlyphText).Append(SpaceConstellationPage.GlyphText)
          .Append(SpaceMemberView.GlyphText).Append(Referral.GlyphText).Append(SpaceNotice.GiftGlyphText)
          .Append(SpaceMonthPicker.GlyphText).Append(SpaceQrSheet.T_CLOSE).Append(SpacePlayLog.GlyphText).Append(SpacePlayCalendar.GlyphText)
          .Append(SpaceNotice.CertifyGlyphText).Append(BoardGameCertify.GlyphText).Append(SpaceNotice.ConstellationGlyphText)
          .Append(T_ORBIT).Append(T_STREAK_UNIT).Append(PlanetPowers.GlyphText).Append(SpaceNotice.JourneyGlyphText)
          .Append(SpaceOrbitPanel.GlyphText).Append(SpaceDiceRoller.GlyphText).Append(DiceChallenge.GlyphText)
          .Append(IgCurrency.GlyphText).Append(SpaceShop.GlyphText).Append(SpaceNotice.CardGlyphText)
          .Append(SpacePoker.GlyphText).Append(CoinPusher.GlyphText);
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
        SpaceTheme.Tracking(t, 32f);
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
        SpaceTheme.Tracking(_captionText, 18f);
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
        for (int i = 1; i <= 2; i++)
        {
            var vdiv = SpaceTheme.Rect("DividerV" + i, _cardRt, new Vector2(i / 3f, 0f), new Vector2(0f, 66f),
                new Vector2(80f, 2f));
            vdiv.localEulerAngles = new Vector3(0f, 0f, 90f);
            SpaceTheme.Img(vdiv, SpaceSprites.FadeLine, SpaceTheme.A(SpaceTheme.LINE, 0.28f));
        }

        // 統計（左: 公転 / 中: 来店回数 / 右: 滞在時間 or 前回の来店）。押すと公転の画面
        _orbitValue = StatCell("Orbit", 0f, T_ORBIT, out _);
        _visitValue = StatCell("Visits", 1f / 3f, T_VISITS, out _);
        _statusValue = StatCell("Status", 2f / 3f, T_LAST_VISIT, out _statusLabel);
        var statsHit = SpaceTheme.Rect("StatsHit", _cardRt);
        statsHit.anchorMin = new Vector2(0f, 0f);
        statsHit.anchorMax = new Vector2(1f, 0f);
        statsHit.pivot = new Vector2(0.5f, 0f);
        statsHit.sizeDelta = new Vector2(0f, 132f);
        statsHit.anchoredPosition = Vector2.zero;
        SpaceTheme.Clickable(statsHit.gameObject, SpaceTheme.HitArea(statsHit), OpenOrbit);
    }

    private TextMeshProUGUI StatCell(string name, float x0, string label, out TextMeshProUGUI labelText)
    {
        const float W = 1f / 3f;
        var cell = SpaceTheme.Rect(name, _cardRt);
        cell.anchorMin = new Vector2(x0, 0f);
        cell.anchorMax = new Vector2(x0 + W, 0f);
        cell.pivot = new Vector2(0.5f, 0f);
        cell.sizeDelta = new Vector2(0f, 120f);
        cell.anchoredPosition = new Vector2(0f, 8f);
        labelText = SpaceTheme.Text(cell, "Label", label, 24, SpaceTheme.TEXT_MUTED, new Vector2(0.5f, 1f),
            new Vector2(0f, -26f), new Vector2(290f, 36f), FontStyles.Bold);
        var value = SpaceTheme.Text(cell, "Value", "", 50, SpaceTheme.TEXT, new Vector2(0.5f, 0f),
            new Vector2(0f, 40f), new Vector2(290f, 70f), FontStyles.Bold);
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
        // カードの下〜所持品ボタンの上の空き領域の中央に置く（INTALES WORLD のボタンがあるときは LayoutWorld が上へ縮める）
        var area = SpaceTheme.Rect("CameraArea", _content);
        SpaceTheme.Stretch(area, 0f, CAMERA_BOTTOM, 0f, CAMERA_TOP);
        _cameraArea = area;

        _orbRt = SpaceTheme.Rect("CameraOrb", area, SpaceTheme.CENTER, new Vector2(0f, ORB_Y), new Vector2(760f, 760f));
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
        MoonLabel(btn, T_MOON_HISTORY, HISTORY_SIZE);
        SpaceTheme.Clickable(btn.gameObject, hit, OpenHistory);
    }

    /// <summary>
    /// ボードゲーム図鑑ボタン（緑の小さな惑星）。履歴の惑星と左右対称の左下に置く。
    /// 文字もアイコンもない、ほかの小さな惑星と同じ見た目。図鑑は初めから入っている（2026-11-11 ユーザー指定。
    /// 以前は歯車メニューからダウンロードした人だけに出していた）
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
        MoonLabel(_zukanBtn, T_MOON_ZUKAN, HISTORY_SIZE);
        SpaceTheme.Clickable(_zukanBtn.gameObject, hit, OpenZukan);
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
        MoonLabel(btn, T_MOON_PRICE, PRICE_SIZE);
        SpaceTheme.Clickable(btn.gameObject, hit, OpenPrices);
    }

    /// <summary>
    /// 小さな惑星の名前を惑星のすぐ下に出す（中央の惑星の「カメラを起動」と同じ白い太字で、ひと回り小さく）。
    /// ボタンの子に置くので一緒に出し入れされるが、惑星の漂いには付いていかない（文字が揺れないように）
    /// </summary>
    internal static TextMeshProUGUI MoonLabel(RectTransform parent, string text, float moonSize)
    {
        var t = SpaceTheme.Text(parent, "Label", text, 26, SpaceTheme.A(SpaceTheme.TEXT, 0.92f), SpaceTheme.CENTER,
            new Vector2(0f, -(moonSize * 0.5f + 26f)), new Vector2(280f, 38f), FontStyles.Bold);
        t.enableAutoSizing = true;
        t.fontSizeMin = 20f;
        t.fontSizeMax = 26f;
        return t;
    }

    // 星座の画面の「このジャンルのゲームを見る」で、図鑑をそのジャンルで開く（星座の画面が閉じてから。2026-11-09）
    private static ConstellationDef _pendingZukanGenre;

    public static void RequestZukanGenre(ConstellationDef def) => _pendingZukanGenre = def;

    private void UpdatePendingZukan()
    {
        if (_pendingZukanGenre == null || !_built || PanelOpen || _landing) return;
        var def = _pendingZukanGenre;
        _pendingZukanGenre = null;
        if (_zukan == null) OpenZukan();
        if (_zukan != null) _zukan.ShowGenre(def);
    }

    /// <summary>ボードゲーム図鑑を開く（宇宙デザイン。できることは旧ホームの図鑑と同じ。ホームの上に全画面で重ねる）</summary>
    private void OpenZukan()
    {
        if (!_built || PanelOpen || _landing) return;
        _zukan = SpaceBoardGameZukan.Open(_canvas);
    }

    /// <summary>中央の惑星を差し替える（見た目の入れ替えだけ。保存は SpacePlanetPicker が行う）</summary>
    private void ApplyPlanet(SpacePlanetDef def)
    {
        _planet = def;
        if (_planetView != null) Destroy(_planetView.gameObject);
        _planetFaded = SpacePlanets.IsExhausted(def); // 力を失った惑星は白く淡い色で（2026-11-10）
        _planetView = SpacePlanetView.Create(_planetHolder, def, PLANET_VIEW, SpacePlanetView.Quality.Large, true,
            false, _planetFaded);
        _cameraIcon.gameObject.SetActive(def.IsOrb);
        _cameraBadge.gameObject.SetActive(!def.IsOrb);
        SetTiltOrbit(true); // 環のある惑星でも、紫の衛星の軌道を出す（2026-11-09 ユーザー指定）
        _glowColor = def.Accent;
        if (_planetAway) ShowAwayCenter();
    }

    /// <summary>
    /// 中央の惑星が力を失ったか（退店した・コマをもらったまま営業日が変わった）を1秒ごとに見て、
    /// 変わっていたら白く淡い色で描き直す（惑星が飛んでいる間・選ぶ画面を開いている間は待つ）
    /// </summary>
    private void RefreshPlanetFaded()
    {
        if (_planet == null || _landing || _returning || _arrivalFlying || _picker != null) return;
        if (SpacePlanets.IsExhausted(_planet) != _planetFaded) ApplyPlanet(_planet);
    }

    /// <summary>
    /// 入店中の中央: 惑星を消して、カメラのアイコンだけを置く（ここを押すとカメラが開くのは同じ）。
    /// 緑の衛星の円・紫の衛星の傾いた楕円軌道は、入店中もそのまま回し続ける
    /// （以前はここで楕円軌道と紫の衛星を消していた。2026-11-11 ユーザーの指摘でバグとして直した）
    /// </summary>
    private void ShowAwayCenter()
    {
        _planetHolderGroup.alpha = 0f;
        _cameraIcon.gameObject.SetActive(true);
        _cameraIconGroup.alpha = 1f;
        _cameraBadge.gameObject.SetActive(false);
    }

    /// <summary>
    /// 傾いた楕円軌道の線と紫の衛星の表示。環のある惑星でも線と衛星を出す（2026-11-09 ユーザー指定）。
    /// 入店中（中央が空いているとき）も出したまま（2026-11-11）
    /// </summary>
    private void SetTiltOrbit(bool lines, bool dot = true)
    {
        _tiltOrbitOn = dot;
        _tiltBack.SetActive(lines);
        _tiltFront.SetActive(lines);
        _dotIsFront = false;
        _dotBack.gameObject.SetActive(dot);
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

    /// <summary>
    /// 管理者判定（IDトークンの admin クレーム）が済んだら、管理者にだけ
    /// お客様に見せるQRコードの一覧（クーポン・惑星の解放・1か月パス）を開くボタンと、
    /// その左に1か月パスの購入者の一覧を開くボタン（カードの絵）、
    /// さらにその左にお知らせ通知を送る画面を開くボタン（鈴の絵。2026-11-10）を出す
    /// </summary>
    private async UniTaskVoid RefreshAdminAsync()
    {
        await AdminClaim.RefreshAsync();
        if (this == null || !AdminClaim.IsAdmin || _adminBtn != null) return;

        // 惑星ボタンの左（以前フレンドボタンがあった場所）。金色の縁で「店側の道具」とわかるように
        _adminBtn = SpaceTheme.Rect("AdminQrButton", _content, new Vector2(1f, 1f), new Vector2(-336f, -84f),
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

        // 右上のボタンが3つ増えるので、見出し（INTALES Cafe）はその手前までに収める
        _headerRt.offsetMax = new Vector2(-HEADER_RIGHT_ADMIN, _headerRt.offsetMax.y);

        // その左: 1か月パスの購入者の一覧（カードの絵）
        _adminPassBtn = SpaceTheme.Rect("AdminPassButton", _content, new Vector2(1f, 1f), new Vector2(-448f, -84f),
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

        // その左: お知らせ通知を送る（鈴の絵）
        _adminNoticeBtn = SpaceTheme.Rect("AdminNoticeButton", _content, new Vector2(1f, 1f), new Vector2(-560f, -84f),
            new Vector2(96f, 96f));
        var noticeGroup = _adminNoticeBtn.gameObject.AddComponent<CanvasGroup>();
        var noticeFace = SpaceTheme.Img(_adminNoticeBtn, SpaceSprites.Circle, new Color(0.05f, 0.07f, 0.19f, 0.85f), true);
        var noticeRim = SpaceTheme.Rect("Rim", _adminNoticeBtn);
        SpaceTheme.Stretch(noticeRim);
        SpaceTheme.Img(noticeRim, SpaceSprites.RingSmall, SpaceTheme.A(SpaceTheme.GOLD, 0.7f));
        var noticeIcon = SpaceTheme.Rect("Icon", _adminNoticeBtn, SpaceTheme.CENTER, Vector2.zero, new Vector2(52f, 52f));
        SpaceTheme.Img(noticeIcon, SpaceSprites.BellIcon, SpaceTheme.GOLD);
        SpaceTheme.Clickable(_adminNoticeBtn.gameObject, noticeFace, OpenNoticeComposer);
        noticeGroup.alpha = 0f;
        StartCoroutine(SpaceTheme.Tween(0.4f, 0.2f, k => noticeGroup.alpha = k));
    }

    /// <summary>管理者用: 1か月パスの購入者（だれが・どの種類を・いつまで）</summary>
    private void OpenPassList()
    {
        if (PanelOpen || _landing) return;
        _passList = SpaceAdminPassList.Open(_canvas, () => _passList = null);
    }

    /// <summary>管理者用: お知らせ通知を送る（Cloud Functions 経由。送る権限はサーバー側で確かめる）</summary>
    private void OpenNoticeComposer()
    {
        if (PanelOpen || _landing) return;
        _composer = SpaceNoticeComposer.Open(_canvas, () => _composer = null);
    }

    private void OpenQrList()
    {
        if (PanelOpen || _landing) return;
        _qrList = SpaceAdminQrList.Open(_canvas, () => _qrList = null, ToggleVisitAsAdmin);
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

    // ================================================================
    // INTALES WORLD のボタン（所持品の下。ダウンロードした人だけ。2026-11-10）
    // ================================================================
    private void BuildWorldButton()
    {
        _worldRt = SpaceTheme.Rect("WorldButton", _content);
        _worldRt.anchorMin = new Vector2(0f, 0f);
        _worldRt.anchorMax = new Vector2(1f, 0f);
        _worldRt.pivot = new Vector2(0.5f, 0f);
        _worldRt.sizeDelta = new Vector2(-112f, WORLD_H);
        _worldRt.anchoredPosition = new Vector2(0f, BAG_Y);
        _worldGroup = _worldRt.gameObject.AddComponent<CanvasGroup>();
        // 並び（四角いアイコン・見出し・一言・＞）も所持品ボタンと同じ位置にそろえる
        var face = SpaceTheme.Glass(_worldRt, 48f, null, SpaceTheme.A(SpaceTheme.CYAN, 0.45f), true);
        SpaceTheme.Clickable(_worldRt.gameObject, face, OpenWorld);

        var tile = SpaceTheme.Rect("IconTile", _worldRt, new Vector2(0f, 0.5f), new Vector2(32f, 0f),
            new Vector2(112f, 112f), new Vector2(0f, 0.5f));
        SpaceTheme.Gradient(SpaceTheme.Rounded(tile, Color.white, 30f), SpaceTheme.CYAN, SpaceTheme.Hex(0x3B6BFF));
        var icon = SpaceTheme.Rect("Planet", tile, SpaceTheme.CENTER, Vector2.zero, new Vector2(74f, 74f));
        SpaceTheme.Img(icon, SpaceSprites.PlanetIcon, Color.white).preserveAspect = true;

        var title = SpaceTheme.Text(_worldRt, "Title", SpaceWorld.T_TITLE, 42, SpaceTheme.TEXT, new Vector2(0f, 0.5f),
            new Vector2(176f, 26f), new Vector2(560f, 60f), FontStyles.Bold, TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
        SpaceTheme.Tracking(title, 4f);
        _worldSub = SpaceTheme.Text(_worldRt, "Sub", "", 28, SpaceTheme.TEXT_SUB, new Vector2(0f, 0.5f),
            new Vector2(176f, -28f), new Vector2(560f, 40f), FontStyles.Bold, TextAlignmentOptions.Left,
            new Vector2(0f, 0.5f));

        var chev = SpaceTheme.Rect("Chevron", _worldRt, new Vector2(1f, 0.5f), new Vector2(-64f, 0f), new Vector2(40f, 60f));
        SpaceTheme.Bar(chev, new Vector2(-1f, 11f), 34f, 6f, -45f, SpaceTheme.TEXT_SUB);
        SpaceTheme.Bar(chev, new Vector2(-1f, -11f), 34f, 6f, 45f, SpaceTheme.TEXT_SUB);
        RefreshWorldButton();
    }

    /// <summary>ダウンロード済みならボタンを出して、所持品ボタンと中央の惑星の領域をその分だけ上へ</summary>
    private void LayoutWorld()
    {
        if (_worldRt == null) return;
        bool on = SpaceWorld.Installed;
        _worldRt.gameObject.SetActive(on);
        float lift = on ? WORLD_H + WORLD_GAP : 0f;
        _bagRt.anchoredPosition = new Vector2(0f, BAG_Y + lift);
        if (_cameraArea != null) _cameraArea.offsetMin = new Vector2(_cameraArea.offsetMin.x, CAMERA_BOTTOM + lift);
        UpdateTapBand(lift);
    }

    /// <summary>お知らせ・料金表の小さな惑星の名前の下端（小さな惑星の中心からの距離。文字の大きさの設定で少しのびる分も含む）</summary>
    private const float MOON_LABEL_BELOW = 100f;

    /// <summary>
    /// 空の押せる星座（さいころ座・金貨座・宝石座）を置ける高さを背景に渡す（2026-11-12）:
    /// 図鑑・履歴の小さな惑星の上端 〜 お知らせ・料金表の小さな惑星の名前の下端（キャンバスの下端からの距離）
    /// </summary>
    private void UpdateTapBand(float lift)
    {
        if (_background == null || _canvas == null) return;
        var size = SpaceTheme.CanvasSize(_canvas);
        var insets = SpaceTheme.SafeInsets(size);
        float top = size.y - insets.top;
        float areaLo = insets.bottom + CAMERA_BOTTOM + lift, areaHi = top - CAMERA_TOP;
        float orb = (areaLo + areaHi) * 0.5f + ORB_Y;                    // 中央の惑星の中心
        float lo = orb + HISTORY_POS.y + HISTORY_HIT * 0.5f;              // 図鑑・履歴の小さな惑星の上端
        float hi = top + PRICE_POS.y - MOON_LABEL_BELOW;                  // お知らせ・料金表の名前の下端
        _background.SetTapBand(lo, hi);
    }

    /// <summary>ボタンの下の一言（今日もう置いたか・入店中か）</summary>
    private void RefreshWorldButton()
    {
        if (_worldSub == null || !SpaceWorld.Installed) return;
        bool can = SpaceWorld.CanPlaceToday; // 管理者は何回でも置ける（デバッグ用）
        string text = !can ? T_WORLD_PLACED : SpaceWorld.InStore ? T_WORLD_READY : T_WORLD_NOT_IN;
        if (_worldSub.text != text) _worldSub.text = text;
        _worldSub.color = can && SpaceWorld.InStore ? SpaceTheme.GOLD : SpaceTheme.TEXT_SUB;
    }

    /// <summary>歯車メニューでダウンロード・削除した: ボタンの出し入れと並びの調整</summary>
    private void OnWorldChanged()
    {
        if (this == null || !_built) return;
        if (!SpaceWorld.Installed && _world != null) _world.Close();
        LayoutWorld();
        RefreshWorldButton();
        if (SpaceWorld.Installed)
        {
            _worldGroup.alpha = 0f;
            StartCoroutine(SpaceTheme.Tween(0.4f, 0.1f, k => { if (_worldGroup != null) _worldGroup.alpha = k; }));
        }
    }

    private void OpenWorld()
    {
        if (PanelOpen || !SpaceWorld.Installed) return;
        _world = SpaceWorldMap.Open(_canvas, () => _world = null);
    }

    private void PlayEntrance()
    {
        var menuGroup = _accountMenu.TriggerGroup;
        _headerGroup.alpha = _cardGroup.alpha = _orbGroup.alpha = _bagGroup.alpha = menuGroup.alpha = 0f;
        _worldGroup.alpha = 0f;
        _planetBtnGroup.alpha = 0f;
        var cardBase = _cardRt.anchoredPosition;
        var bagBase = _bagRt.anchoredPosition;
        StartCoroutine(SpaceTheme.Tween(0.6f, 0f,
            k => _headerGroup.alpha = menuGroup.alpha = _planetBtnGroup.alpha = k));
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
        var worldBase = _worldRt.anchoredPosition;
        StartCoroutine(SpaceTheme.Tween(0.55f, 0.25f, k =>
        {
            _bagGroup.alpha = k;
            _bagRt.anchoredPosition = bagBase + new Vector2(0f, -36f * (1f - k));
        }));
        StartCoroutine(SpaceTheme.Tween(0.55f, 0.32f, k =>
        {
            _worldGroup.alpha = k;
            _worldRt.anchoredPosition = worldBase + new Vector2(0f, -36f * (1f - k));
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
    /// <summary>
    /// 今日の入店QRを読んだのと同じ入店・退店をする（管理者のQRコード画面の「ログイン」ボタン。2026-11-12 ユーザー指定。
    /// エディタの N キーも同じ）。入店中なら退店、入店の記録はあるのに公転に数えていなければ公転の記録だけやり直す
    /// </summary>
    private void ToggleVisitAsAdmin()
    {
        var caller = GetComponent<CallMethodFromQR>();
        if (caller == null) caller = gameObject.AddComponent<CallMethodFromQR>();
        caller.ToggleVisitSigned(StoreCheckinQr.Payload(StoreCheckinQr.Today));
        RefreshVisit();
    }

    private void RefreshVisit()
    {
        var log = LocalVisitLog.Load();
        int count = log.visits != null ? log.visits.Count : 0;
        // 来店回数・公転はサーバーの来店の記録（JourneyService）から。記録がまだなければ端末の来店記録の日数
        int visits = JourneyService.DisplayVisits;
        _visitValue.text = $"{visits}<size=55%><color={SpaceTheme.SUB_HEX}> 回</color></size>";
        _orbitValue.text = JourneyService.Journey != null
            ? $"{JourneyService.DisplayStreak}<size=55%><color={SpaceTheme.SUB_HEX}>{T_STREAK_UNIT}</color></size>"
            : "ー";
        _orbitValue.color = JourneyService.Journey == null || JourneyService.VisitedThisOrbit
            ? SpaceTheme.TEXT : SpaceTheme.GOLD; // この公転にまだ来ていなければ金色
        _captionText.text = visits > 0 || count > 0 ? T_WELCOME_BACK : T_WELCOME;

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
        if (announce) CheckStarter(); // 贈りものの知らせより先に（選ぶ画面が開いている間は贈りものを待たせる）
        if (announce) CheckMeeplePick();
        if (announce) CheckCardDiscard();
        if (announce) CheckLegacyGift();
        if (announce && !_giftBusy) UpdateGifts();
    }

    /// <summary>
    /// 複数の色の惑星で入店して、まだもらうコマを選んでいない（選ぶ画面を閉じる前にアプリを閉じたなど）:
    /// 選ぶ画面をもう一度出す（選ぶまで閉じない。2026-11-10）
    /// </summary>
    private void CheckMeeplePick()
    {
        if (!_built || _starter != null) return;
        var pick = JourneyService.PendingPick;
        if (pick == PlanetColor.None) return;
        if (PanelOpen || _landing || SpaceNoticeQueue.Running || SpaceNotice.AnyOpen) return;
        SpaceNotice.MeeplePick(pick);
    }

    /// <summary>
    /// トランプの手札が8枚のまま（捨てるカードを選ぶ前にアプリを閉じたなど）: 捨てるカードを選ぶ画面をもう一度出す
    /// （選ぶまで閉じない。2026-11-12）
    /// </summary>
    private void CheckCardDiscard()
    {
        if (!_built || _starter != null) return;
        if (_landing || SpaceNoticeQueue.Running || SpaceNotice.AnyOpen) return;
        if (!CardDeck.MustDiscard) return;
        SpaceNotice.CardDiscard();
    }

    /// <summary>
    /// 新しく登録した人（starter が ""）: 最初の惑星を赤・青・黄から選ぶ画面を出す（選ぶまで閉じない。2026-11-10）。
    /// 選んだら中央の惑星を差し替えて、惑星を手に入れたときと同じ知らせを出す
    /// </summary>
    private void CheckStarter()
    {
        if (_starter != null || !_built || !SpacePlanets.NeedsStarter) return;
        if (PanelOpen || _landing || SpaceNoticeQueue.Running || SpaceNotice.AnyOpen) return;
        _starter = SpaceStarterPicker.Open(_canvas, def =>
        {
            if (this == null) return;
            ApplyPlanet(def);
            SpaceNotice.PlanetUnlocked(def, SpaceStarterPicker.T_DONE_TITLE, SpaceStarterPicker.T_DONE_NOTE,
                SpaceStarterPicker.T_DONE_OK);
        }, () => _starter = null);
    }

    // ---- 贈りもの（誕生月・友だち紹介。2026-11-07）----
    private bool _giftBusy;
    private float _nextReferrerCheck = 5f; // 紹介した人の特典の確認（起動して少したってから。以後5分ごと）
    private const float REFERRER_CHECK_INTERVAL = 300f;

    private void UpdateGifts()
    {
        if (SpaceNoticeQueue.Running || SpaceNotice.AnyOpen || PanelOpen || _arrivalFlying || _returning) return;
        if (GameObject.FindWithTag("QR") != null) return;
        bool referrer = Time.unscaledTime >= _nextReferrerCheck;
        if (!BirthdayGift.Due && !Referral.CanTakeNewcomerGift && !referrer && !DiceChallenge.ClaimReady) return;
        GiftsAsync(referrer).Forget();
    }

    private async UniTaskVoid GiftsAsync(bool referrer)
    {
        _giftBusy = true;
        try
        {
            var drink = SpaceCoupons.ByField("drink_coupon");
            if (BirthdayGift.Due && await BirthdayGift.TakeAsync())
            {
                SyncCouponSnapshot();
                await ShowAndWait(SpaceNotice.Gift(SpaceNotice.T_BIRTHDAY_TITLE, SpaceNotice.T_BIRTHDAY_BODY, drink,
                    SpaceTheme.PINK));
            }
            if (Referral.CanTakeNewcomerGift && await Referral.TakeNewcomerGiftAsync())
            {
                SyncCouponSnapshot();
                await ShowAndWait(SpaceNotice.Gift(SpaceNotice.T_REFERRAL_TITLE_NEW, SpaceNotice.T_REFERRAL_BODY_NEW, drink,
                    SpaceTheme.CYAN));
                await ShowReferralPlanet();
            }
            if (referrer)
            {
                _nextReferrerCheck = Time.unscaledTime + REFERRER_CHECK_INTERVAL;
                var names = await Referral.ClaimReferrerGiftsAsync();
                foreach (var n in names)
                {
                    SyncCouponSnapshot();
                    await ShowAndWait(SpaceNotice.Gift(SpaceNotice.T_REFERRAL_TITLE_FROM,
                        SpaceTheme.NoParse(SpaceTheme.Safe(n)) + SpaceNotice.T_REFERRAL_BODY_FROM, drink, SpaceTheme.MINT));
                    await ShowReferralPlanet();
                }
            }
            // ダイスチャレンジ（2026-11-11）: 終わった日にいちばん大きい目を出していたら、5%OFFチケットを受け取る
            if (DiceChallenge.ClaimReady)
            {
                var win = await DiceChallenge.ClaimAsync();
                if (win.HasValue)
                {
                    SyncCouponSnapshot();
                    await ShowAndWait(SpaceNotice.Gift(DiceChallenge.WinTitle(win.Value), DiceChallenge.WinBody(win.Value),
                        SpaceCoupons.ByField(DiceChallenge.REWARD_FIELD), SpaceTheme.GOLD, DiceChallenge.T_WIN_CAPTION,
                        DiceChallenge.T_WIN_GET));
                }
            }
        }
        finally
        {
            _giftBusy = false;
        }
    }

    /// <summary>友だち紹介でオパーリアを新しく解放していたら、解放のお知らせを出す</summary>
    private static async UniTask ShowReferralPlanet()
    {
        if (!Referral.PlanetJustUnlocked) return;
        Referral.PlanetJustUnlocked = false;
        var def = SpacePlanets.Find(Referral.PLANET);
        if (def != null) await ShowAndWait(SpaceNotice.PlanetUnlocked(def));
    }

    /// <summary>贈りもので増えたクーポンを「○○を入手しました」と二重に知らせないよう、いまの枚数を覚え直す</summary>
    private void SyncCouponSnapshot()
    {
        _couponSnapshot = SpaceCoupons.Snapshot(Data);
        RefreshBagSub();
    }

    private static async UniTask ShowAndWait(GameObject notice)
    {
        if (notice == null) return;
        await UniTask.WaitUntil(() => notice == null);
    }

    /// <summary>
    /// 古参の惑星（レガシア）を持っていて、贈りもののお知らせをまだ見ていなければ1回だけ出す。
    /// ふだんは店頭のQRで配る（そのときは QR の処理がお知らせを出す）。ここはスクリプト
    /// tools/firestore/grant-legacy-planet.js で配ったときや、QRを読んだのが別の端末だったとき用
    /// </summary>
    private void CheckLegacyGift()
    {
        var def = SpacePlanets.Find(SpacePlanets.LEGACY_ID);
        if (def == null || !SpacePlanets.IsOwned(def)) return;
        if (SpaceNoticeQueue.Running || SpaceNotice.AnyOpen) return;
        if (!SpacePlanets.TakeLegacyGift()) return;
        SpacePlanets.MarkNews();
        SpaceNotice.PlanetUnlocked(def, SpacePlanets.T_LEGACY_TITLE, SpacePlanets.T_LEGACY_NOTE);
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
        // 流れ星（入店・R キー）の当たりは、ガチャの演出と当たりのカードで知らせるので重ねない
        // （演出の途中に「○○を1枚入手しました」が出ると、結果が先にわかってしまう）
        if (SpaceNoticeQueue.Running || SpaceNotice.AnyOpen) return;

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
    private bool PanelOpen => _inventory != null || _history != null || _news != null
                              || _picker != null || _qrList != null || _passList != null || _composer != null
                              || _starter != null || _world != null || _zukan != null
                              || _prices != null || _orbitPanel != null || _accountMenu.IsOpen
                              || SpaceConstellationPage.Instance != null || SpaceDiceRoller.Instance != null
                              || SpaceShop.Instance != null || SpacePoker.Instance != null || CoinPusher.Active;

    /// <summary>公転の画面（ユーザーカードの公転・来店回数・入店状況を押す）</summary>
    private void OpenOrbit()
    {
        if (PanelOpen || _landing) return;
        _orbitPanel = SpaceOrbitPanel.Open(_canvas, () => _orbitPanel = null);
    }

    private void OnJourneyChanged()
    {
        if (this == null || !_built) return;
        RefreshVisit();
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

    /// <summary>料金表（右上の水色の小さな惑星）。平日料金・土日・祝日料金の画像を見せる</summary>
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
        if (_planetAway)
        {
            // 入店中は惑星が円の上にいるので中央には降ろさない（円の上の自分の球の色は StoreVisitors が変える）
            ApplyPlanet(def);
            if (flying != null) Destroy(flying.gameObject);
            yield break;
        }
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
                // ノワールの能力: 店内の円で光の円盤をまとって回る（ほかの人にも見える）
                // レガシアの能力: 白銀の光の輪をまとう（円盤と同じ部品を、つぶさない丸い輪にして使う）
                bool legacy = def.Id == SpacePlanets.LEGACY_ID;
                o.Disk.gameObject.SetActive(false); // 惑星の能力（ノワールの円盤・レガシアの輪）は 2026-11-09 にやめた
                o.Disk.color = SpaceTheme.A(Color.Lerp(def.Accent, Color.white, 0.25f), 0.9f);
                o.Disk.rectTransform.sizeDelta = legacy
                    ? new Vector2(VISITOR_GLOW * 0.95f, VISITOR_GLOW * 0.95f)
                    : new Vector2(VISITOR_GLOW * 1.5f, VISITOR_GLOW * 0.5f);
            }
        }

        // グラシアの能力: 冬のあいだ、グラシアの人が入店中なら店内の円に雪が舞う
        bool snow = false; // 惑星の能力（グラシアの雪）は 2026-11-09 にやめた
        if (snow && _snow == null)
        {
            var layer = SpaceTheme.Rect("Snow", _orbRt, SpaceTheme.CENTER, Vector2.zero,
                new Vector2(VISITOR_RADIUS * 2.3f, VISITOR_RADIUS * 2.3f));
            layer.SetAsFirstSibling();
            _snow = layer.gameObject.AddComponent<SpaceSnow>();
        }
        else if (!snow && _snow != null)
        {
            _snow.FadeOut();
            _snow = null;
        }
    }

    private VisitorOrb MakeVisitorOrb(string id, float angle)
    {
        var rt = SpaceTheme.Rect("Visitor", _visitorRing, SpaceTheme.CENTER, Vector2.zero,
            new Vector2(VISITOR_GLOW, VISITOR_GLOW));
        var core = SpaceTheme.Rect("Core", rt, SpaceTheme.CENTER, Vector2.zero, new Vector2(VISITOR_CORE, VISITOR_CORE));
        var disk = SpaceTheme.Rect("Disk", rt, SpaceTheme.CENTER, Vector2.zero,
            new Vector2(VISITOR_GLOW * 1.5f, VISITOR_GLOW * 0.5f));
        disk.localEulerAngles = new Vector3(0f, 0f, -18f);
        var o = new VisitorOrb
        {
            Id = id, Rt = rt, Angle = angle, Target = angle,
            Glow = SpaceTheme.Img(rt, SpaceSprites.Glow, Color.clear),
            Core = SpaceTheme.Img(core, SpaceSprites.Circle, Color.clear),
            Disk = SpaceTheme.Img(disk, SpaceSprites.RingSmall, Color.clear),
        };
        disk.gameObject.SetActive(false);
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
        bool wasAway = _planetAway;
        _planetAway = true; // 中央の惑星が円へ出ていく
        var trailColor = Color.Lerp(def.Accent, Color.white, 0.35f);

        // 尾 → 惑星のまわりの光 → 惑星の順に重ねる（いちばん手前の層）
        var holder = SpaceTheme.Rect("Arrival", _orbRt, SpaceTheme.CENTER, Vector2.zero, Vector2.zero);
        var trailLayer = SpaceTheme.Rect("Trail", holder, SpaceTheme.CENTER, Vector2.zero, Vector2.zero);
        var glowRt = SpaceTheme.Rect("Glow", holder, SpaceTheme.CENTER, Vector2.zero, Vector2.one);
        var glowImg = SpaceTheme.Img(glowRt, SpaceSprites.Glow, Color.clear);
        float frame0 = ARRIVAL_SPHERE0 / SpacePlanetView.SPHERE_RATIO;
        var view = SpacePlanetView.Create(holder, def, frame0, SpacePlanetView.Quality.Thumb, false,
            false, SpacePlanets.IsExhausted(def)); // 力を失った惑星は淡い色のまま飛ぶ
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
            if (!wasAway && !_landing) HideCenterStep(p);
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

    /// <summary>
    /// 入店で飛び出すときの中央: 惑星がしぼんで消え（飛んでいく惑星と入れ替わる）、そのあとカメラのアイコンが出る
    /// </summary>
    private void HideCenterStep(float p)
    {
        float k = Mathf.Clamp01(p / AWAY_HIDE);
        float s = Mathf.Lerp(1f, 0.55f, EaseOutCubic(k));
        _planetHolder.localScale = new Vector3(s, s, 1f);
        _planetHolderGroup.alpha = 1f - k;
        _badgeGroup.alpha = 1f - k;
        if (k >= 1f)
        {
            _planetHolder.localScale = Vector3.one;
            ShowAwayCenter();
            _cameraIconGroup.alpha = Mathf.Clamp01((p - AWAY_HIDE) / 0.25f);
            if (p >= 1f) _cameraIconGroup.alpha = 1f;
        }
        else if (!_planet.IsOrb)
        {
            _cameraIconGroup.alpha = 0f;
        }
    }

    /// <summary>
    /// 入店中は中央の惑星を消しておき、退店したら（入店中でなくなったら）円から中央へ戻す。
    /// 入店のQRを読んだ直後は FlyArrival が飛び出しと一緒に消すので待つ。起動したときにもう入店中なら、演出なしで消す
    /// </summary>
    private void UpdatePlanetAway(bool qrOpen)
    {
        if (_arrivalFlying || _returning) return;
        bool inStore = _checkInTime.HasValue;

        // 円の上の自分の球の場所を覚えておく（退店すると球はすぐ消えるので、戻るときの出発点にする）
        string self = StoreVisitors.SelfDocId;
        if (!string.IsNullOrEmpty(self))
        {
            var orb = _visitorOrbs.Find(o => o.Id == self && !o.Leaving);
            if (orb != null) _selfAngle = orb.Angle;
        }

        if (inStore && !_planetAway)
        {
            if (StoreVisitors.ArrivalId != null) return; // もうすぐ飛び出す
            _planetAway = true;
            if (!_landing) ShowAwayCenter();
            return;
        }
        if (!inStore && _planetAway)
        {
            bool covered = qrOpen || _landing || PanelOpen || SpaceNotice.AnyOpen || SpaceNoticeQueue.Running;
            if (covered)
            {
                _returnReadyAt = -1f;
                return;
            }
            if (_returnReadyAt < 0f) _returnReadyAt = Time.unscaledTime + ARRIVAL_DELAY;
            if (Time.unscaledTime < _returnReadyAt) return;
            _returnReadyAt = -1f;
            StartCoroutine(FlyReturn());
        }
    }

    /// <summary>
    /// 退店したとき: 自分の惑星が、いちばん外の円の自分がいた場所から、来たときと逆向きに回り込みながら
    /// 大きくなって中央へ戻り、中央にぽんと収まる（光の尾を引き、着いたら光の輪が広がる）
    /// </summary>
    private IEnumerator FlyReturn()
    {
        _returning = true;
        var def = SpacePlanets.Current;
        var trailColor = Color.Lerp(def.Accent, Color.white, 0.35f);
        float startAngle = _visitorRing.localEulerAngles.z + _selfAngle;

        var holder = SpaceTheme.Rect("Return", _orbRt, SpaceTheme.CENTER, Vector2.zero, Vector2.zero);
        var trailLayer = SpaceTheme.Rect("Trail", holder, SpaceTheme.CENTER, Vector2.zero, Vector2.zero);
        var glowRt = SpaceTheme.Rect("Glow", holder, SpaceTheme.CENTER, Vector2.zero, Vector2.one);
        var glowImg = SpaceTheme.Img(glowRt, SpaceSprites.Glow, Color.clear);
        float frame0 = ARRIVAL_SPHERE0 / SpacePlanetView.SPHERE_RATIO;
        var view = SpacePlanetView.Create(holder, def, frame0, SpacePlanetView.Quality.Thumb, false,
            false, SpacePlanets.IsExhausted(def)); // 力を失った惑星は淡い色のまま飛ぶ
        var viewRt = (RectTransform)view.transform;
        var viewGroup = view.GetComponent<CanvasGroup>();
        viewRt.localScale = Vector3.zero;

        // 円の上の場所に光の輪（ここから出発する）
        StartCoroutine(FlashAt(new Vector2(Mathf.Cos(startAngle * Mathf.Deg2Rad), Mathf.Sin(startAngle * Mathf.Deg2Rad))
                               * VISITOR_RADIUS, def.Accent));

        var trail = new List<TrailDot>();
        float nextTrail = 0f;
        int trailCount = 0;
        bool landed = false;
        for (float t = 0f; ; t += Time.unscaledDeltaTime)
        {
            float p = Mathf.Clamp01(t / RETURN_DURATION);
            if (p < 1f)
            {
                float e = EaseInOutSine(p);
                float r = Mathf.Lerp(VISITOR_RADIUS, ARRIVAL_R0 * 0.3f, EaseInOutSine(p));
                float ang = (startAngle - ARRIVAL_SWEEP * e) * Mathf.Deg2Rad; // 来たときと逆向きに回り込む
                var pos = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r;
                float sphere = Mathf.Lerp(ARRIVAL_SPHERE1, ARRIVAL_SPHERE0, p * p)
                               * EaseOutBack(Mathf.Clamp01(p / 0.12f)); // 円の上でぽんと膨らんで出発
                float alpha = p < 0.88f ? 1f : 1f - (p - 0.88f) / 0.12f;
                viewRt.anchoredPosition = pos;
                float s = sphere / ARRIVAL_SPHERE0;
                viewRt.localScale = new Vector3(s, s, 1f);
                if (viewGroup != null) viewGroup.alpha = alpha;
                glowRt.anchoredPosition = pos;
                glowRt.sizeDelta = Vector2.one * Mathf.Max(1f, sphere * 1.6f);
                glowImg.color = SpaceTheme.A(def.Accent, 0.45f * alpha);
                _cameraIconGroup.alpha = def.IsOrb ? 1f : 1f - Mathf.Clamp01((p - 0.6f) / 0.25f);

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

            // 着く少し前に、中央の惑星をぽんと膨らませて戻す
            if (!landed && p >= 0.85f)
            {
                landed = true;
                StartCoroutine(LandCenter(def));
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
        _returning = false;
    }

    /// <summary>戻ってきた惑星が中央に収まる（ふくらんで出て、光の輪が広がる）</summary>
    private IEnumerator LandCenter(SpacePlanetDef def)
    {
        _planetAway = false;
        if (def != _planet) ApplyPlanet(def);
        _cameraIcon.gameObject.SetActive(def.IsOrb);
        _cameraBadge.gameObject.SetActive(!def.IsOrb);
        SetTiltOrbit(true); // 環のある惑星でも、紫の衛星の軌道を出す（2026-11-09 ユーザー指定）
        _badgeGroup.alpha = 0f;
        StartCoroutine(FlashRing(def.Accent));
        yield return SpaceTheme.Tween(0.45f, 0f, k =>
        {
            _planetHolderGroup.alpha = Mathf.Clamp01(k * 2f);
            float s = Mathf.LerpUnclamped(0.5f, 1f, EaseOutBack(k));
            _planetHolder.localScale = new Vector3(s, s, 1f);
        });
        _planetHolder.localScale = Vector3.one;
        _planetHolderGroup.alpha = 1f;
        _cameraIconGroup.alpha = 1f;
        yield return SpaceTheme.Tween(0.35f, 0f, k =>
        {
            _badgeGroup.alpha = k;
            float s = Mathf.LerpUnclamped(0.5f, 1f, EaseOutBack(k));
            _cameraBadge.localScale = new Vector3(s, s, 1f);
        });
    }

    /// <summary>いちばん外の円の上の1点に光の輪（円の回転とは関係なく、その場に出す）</summary>
    private IEnumerator FlashAt(Vector2 pos, Color color)
    {
        var rt = SpaceTheme.Rect("ReturnFlash", _orbRt, SpaceTheme.CENTER, pos, new Vector2(40f, 40f));
        var img = SpaceTheme.Img(rt, SpaceSprites.RingSmall, Color.clear);
        yield return SpaceTheme.Tween(0.5f, 0f, k =>
        {
            rt.sizeDelta = Vector2.one * Mathf.Lerp(40f, 150f, EaseOutCubic(k));
            img.color = SpaceTheme.A(color, 0.9f * (1f - k));
        });
        Destroy(rt.gameObject);
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
        // 入店中は中央の惑星を消し、退店したら円から中央へ戻す
        UpdatePlanetAway(qrOpen);
        UpdatePendingZukan();

        // QR画面・惑星を選ぶ画面・お知らせで隠れている間は中央の惑星の自転を止める（毎フレームの再描画を省く）
        if (_planetView != null)
            _planetView.Animate = !qrOpen && _news == null && _zukan == null && _prices == null
                                  && SpaceConstellationPage.Instance == null && SpaceDiceRoller.Instance == null
                                  && SpaceShop.Instance == null && SpacePoker.Instance == null && !CoinPusher.Active
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
            RefreshPlanetFaded();
            RefreshWorldButton();
            TickStayTime();
            RefreshVisitors(); // 営業終了時刻を過ぎた入店（退店し忘れ）は、新しい知らせがなくても外す
        }

        if (BackPressed())
        {
            // 手前から閉じる: QR読み取り後のお知らせ → QRコード一覧（管理者）→ 惑星を選ぶ画面 → 所持品 → 履歴
            // → お知らせページ → 設定メニュー
            if (SpaceNotice.CloseTop()) { }
            else if (_starter != null) _starter.HandleBack(); // 最初の惑星を選ぶまでは閉じない
            else if (CoinPusherController.Instance != null) CoinPusherController.Instance.HandleBack(); // コインプッシャー（全画面）
            else if (SpaceConstellationPage.Instance != null) SpaceConstellationPage.Instance.HandleBack();
            else if (SpaceDiceRoller.Instance != null) SpaceDiceRoller.Instance.HandleBack();
            else if (SpaceShop.Instance != null) SpaceShop.Instance.HandleBack();
            else if (SpacePoker.Instance != null) SpacePoker.Instance.HandleBack();
            else if (_zukan != null) _zukan.HandleBack();
            else if (_prices != null) _prices.HandleBack();
            else if (_qrList != null) _qrList.HandleBack();
            else if (_passList != null) _passList.HandleBack();
            else if (_composer != null) _composer.HandleBack();
            else if (_picker != null) _picker.HandleBack();
            else if (_world != null) _world.HandleBack();
            else if (_orbitPanel != null) _orbitPanel.HandleBack();
            else if (_inventory != null) _inventory.HandleBack();
            else if (_history != null) _history.HandleBack();
            else if (_news != null) _news.HandleBack();
            else _accountMenu.HandleBack();
        }

#if UNITY_EDITOR
        if (DebugTogglePressed())
        {
            // エディタ専用: N キーで入退店（お店の画面の入店QRを読んだときと同じ処理）
            ToggleVisitAsAdmin();
        }
        // エディタ専用: R キーで流れ星を1回引く（当たりはログイン中のアカウントに本当に足す）
        if (DebugStarPressed() && !SpaceNoticeQueue.Running && !_debugDrawing) DebugDrawAsync().Forget();
        // エディタ専用: G キーでログイン中のアカウントに 10G を足す（ショップを試すため。本当に足す。2026-11-12）
        if (DebugGoldPressed() && !_debugGold) DebugGoldAsync().Forget();
        // エディタ専用: T キーでトランプを1枚引く（1日1回の決まりを使わない。ポーカーを試すため。本当に書き込む。2026-11-12）
        if (DebugCardPressed() && !_debugCard && !SpaceNoticeQueue.Running) DebugCardAsync().Forget();
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

    private bool _debugDrawing;

    /// <summary>
    /// エディタ専用: R キーで流れ星を1回引く。当たりをアカウントに書き込んでから（JourneyService.DebugDrawAsync）
    /// ガチャの演出 → 当たりのカード（ノワールなら新しい惑星も）を出す
    /// </summary>
    private async UniTaskVoid DebugDrawAsync()
    {
        _debugDrawing = true;
        try
        {
            var r = await JourneyService.DebugDrawAsync();
            if (this == null) return;
            if (r == null)
            {
                SpaceTheme.Toast("ログインしていません", _canvas);
                return;
            }
            Debug.Log($"[Debug] 流れ星: {string.Join(",", r.Prizes)}（アカウントに追加）");
            SpaceNotice.ShowJourney(r, false, null);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Debug] 流れ星の書き込みに失敗: {ex.Message}");
            if (this != null) SpaceTheme.Toast("流れ星の書き込みに失敗しました", _canvas);
        }
        finally
        {
            _debugDrawing = false;
        }
    }

    /// <summary>エディタ専用: R キー（流れ星を引く）</summary>
    private static bool DebugStarPressed()
    {
        bool pressed = false;
#if ENABLE_INPUT_SYSTEM
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb != null && kb.rKey.wasPressedThisFrame) pressed = true;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKeyDown(KeyCode.R)) pressed = true;
#endif
        return pressed;
    }

    private const int DEBUG_GOLD = 10;
    private bool _debugGold;

    /// <summary>エディタ専用: G キーでログイン中のアカウントに 10G を足す（ショップを試すため。IgCurrency.DebugAddAsync）</summary>
    private async UniTaskVoid DebugGoldAsync()
    {
        _debugGold = true;
        try
        {
            bool ok = await IgCurrency.DebugAddAsync(DEBUG_GOLD);
            if (this == null) return;
            SpaceTheme.Toast(SpaceTheme.SafeNow(ok ? IgCurrency.Format(DEBUG_GOLD) + " を足しました（デバッグ）" : "ログインしていません"),
                _canvas);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Debug] G の書き込みに失敗: {ex.Message}");
        }
        finally
        {
            _debugGold = false;
        }
    }

    private bool _debugCard;

    /// <summary>エディタ専用: T キーでトランプを1枚引く（CardDeck.DebugDrawAsync）→ めくって見せる → 8枚なら捨てる</summary>
    private async UniTaskVoid DebugCardAsync()
    {
        _debugCard = true;
        try
        {
            if (CardDeck.MustDiscard)
            {
                SpaceNotice.CardDiscard();
                return;
            }
            var (card, overflow) = await CardDeck.DebugDrawAsync();
            if (this == null) return;
            if (card < 0)
            {
                SpaceTheme.Toast("ログインしていません", _canvas);
                return;
            }
            var steps = new List<Func<GameObject>> { () => SpaceNotice.CardDrawn(new List<int> { card }, "DEBUG", overflow) };
            if (overflow) steps.Add(() => CardDeck.MustDiscard ? SpaceNotice.CardDiscard() : null);
            SpaceNoticeQueue.Run(steps, null);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Debug] トランプの書き込みに失敗: {ex.Message}");
        }
        finally
        {
            _debugCard = false;
        }
    }

    /// <summary>エディタ専用: T キー（トランプを1枚引く）</summary>
    private static bool DebugCardPressed()
    {
        bool pressed = false;
#if ENABLE_INPUT_SYSTEM
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb != null && kb.tKey.wasPressedThisFrame) pressed = true;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKeyDown(KeyCode.T)) pressed = true;
#endif
        return pressed;
    }

    /// <summary>エディタ専用: G キー（10G を足す）</summary>
    private static bool DebugGoldPressed()
    {
        bool pressed = false;
#if ENABLE_INPUT_SYSTEM
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb != null && kb.gKey.wasPressedThisFrame) pressed = true;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKeyDown(KeyCode.G)) pressed = true;
#endif
        return pressed;
    }
#endif
}

/// <summary>
/// 店内の円に舞う雪（冬にグラシアを中央に置いた人が入店中のとき）。円の中だけに、小さな光の粒がゆっくり降る
/// </summary>
public class SpaceSnow : MonoBehaviour
{
    private const int COUNT = 26;

    private struct Flake
    {
        public RectTransform Rt;
        public UnityEngine.UI.Image Img;
        public float X, Y, Speed, Sway, Phase, Alpha;
    }

    private readonly Flake[] _flakes = new Flake[COUNT];
    private float _radius, _show, _target = 1f;

    private void Start()
    {
        var rt = (RectTransform)transform;
        _radius = rt.sizeDelta.x * 0.5f;
        for (int i = 0; i < COUNT; i++)
        {
            float size = UnityEngine.Random.Range(8f, 18f);
            var f = SpaceTheme.Rect("Flake", rt, SpaceTheme.CENTER, Vector2.zero, new Vector2(size, size));
            _flakes[i] = new Flake
            {
                Rt = f,
                Img = SpaceTheme.Img(f, SpaceSprites.Glow, Color.clear),
                X = UnityEngine.Random.Range(-_radius, _radius),
                Y = UnityEngine.Random.Range(-_radius, _radius),
                Speed = UnityEngine.Random.Range(22f, 48f),
                Sway = UnityEngine.Random.Range(8f, 22f),
                Phase = UnityEngine.Random.Range(0f, 6.28f),
                Alpha = UnityEngine.Random.Range(0.45f, 0.9f),
            };
        }
    }

    /// <summary>ゆっくり消えてから片づける</summary>
    public void FadeOut() => _target = 0f;

    private void Update()
    {
        float dt = Time.unscaledDeltaTime;
        _show = Mathf.MoveTowards(_show, _target, dt * 0.8f);
        if (_target <= 0f && _show <= 0f)
        {
            Destroy(gameObject);
            return;
        }
        float t = Time.unscaledTime;
        for (int i = 0; i < COUNT; i++)
        {
            var f = _flakes[i];
            if (f.Rt == null) continue;
            f.Y -= f.Speed * dt;
            if (f.Y < -_radius)
            {
                f.Y = _radius;
                f.X = UnityEngine.Random.Range(-_radius, _radius);
            }
            float x = f.X + Mathf.Sin(t * 0.8f + f.Phase) * f.Sway;
            f.Rt.anchoredPosition = new Vector2(x, f.Y);
            float d = Mathf.Clamp01(new Vector2(x, f.Y).magnitude / _radius);
            f.Img.color = new Color(0.92f, 0.97f, 1f, f.Alpha * (1f - d * d) * _show);
            _flakes[i] = f;
        }
    }
}
