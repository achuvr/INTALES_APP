using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// INTALES WORLD の世界地図（2026-11-10 ユーザー指定。ホームの所持品の下の「INTALES WORLD」から開く全画面）。
/// オリジナルの世界地図（SpaceWorld.MapSprite）の上に置き場が6か所あり、置かれたコマの色と数を全員で同期して出す
/// （6か所目の黄金の鉱山＝3G をもらう は 2026-11-12 追加）。
/// 置き場を押すと下からシートが出て、色を選んで「ここに置く」→ 確認 → 置く（SpaceWorld.PlaceAsync）→ 効果のお知らせ。
///  ・まだ誰も置いていなければ好きな色、1体でも置かれていればその色だけ（マストフォロー）
///  ・左下の星くずの島だけは何色でも置ける保険の置き場（輪が赤・青・黄の3つの弧。2026-11-11）
///  ・1日1体まで／入店中だけ／置いたコマは戻らない（置けないときは、シートの下に理由を出す）
///  ・惑星の神殿（惑星を3つアクティブにする）は、力を失った惑星が4つ以上なら、置く前に戻す3つを選ぶ
///  ・黒コマ（2026-11-12。何色にでもなるコマ）を持っているか、切り札座が完成していれば、色のカードの4枚目に黒コマを出す。
///    色が決まった置き場ではその色として置き、まだ決まっていない置き場と星くずの島では、何色として置くかを選ぶ（OpenWildChooser）
/// </summary>
public class SpaceWorldMap : MonoBehaviour
{
    private const string T_CAPTION = "INTALES WORLD";
    private const string T_TITLE = "世界地図";
    private const string T_MINE = "持っているコマ";
    private const string T_STATUS_PLACED_A = "今日は ";
    private const string T_STATUS_PLACED_B = " にコマを置きました（また明日）";
    private const string T_STATUS_NOT_IN = "コマを置けるのは入店中だけです";
    private const string T_STATUS_READY = "置き場を押して、今日のコマを1体置こう";
    private const string T_STATUS_ADMIN = "管理者なので、何回でも置けます（デバッグ用）";
    private const string T_RULES = "1日1体まで ・ 入店中だけ ・ 置いたコマは戻りません ・ 毎朝4時にリセット";
    private const string T_YOU = "あなた";
    private const string T_EMPTY_AREA = "まだ誰も置いていません。好きな色のコマを置けます";
    private const string T_FOLLOW_A = "のコマが ";
    private const string T_FOLLOW_B = "体 置かれています。今日は";
    private const string T_FOLLOW_C = "のコマしか置けません";
    private const string T_TODAY = "今日は";
    private const string T_EXHAUSTED_A = "力を失った惑星：";
    private const string T_EXHAUSTED_B = "個";
    private const string T_OWNED = "持っている数 ";
    private const string T_PLACE = "ここに置く";
    private const string T_PICK_COLOR = "置くコマの色を選んでください";
    private const string T_BLOCK_PLACED = "今日はもうコマを置きました（1日1体まで）";
    private const string T_BLOCK_NOT_IN = "コマを置けるのは入店中だけです";
    private const string T_BLOCK_NO_EXHAUSTED = "力を失った惑星がないので、ここには置けません";
    private const string T_BLOCK_NO_COLOR = "置ける色のコマを持っていません";
    private const string T_CONFIRM_B = "を置きますか？";
    private const string T_CONFIRM_NOTE = "1日に置けるのは1体だけです。置いたコマは戻りません";
    private const string T_CONFIRM_NOTE_ADMIN = "管理者なので何回でも置けます（デバッグ用）。置いたコマは戻りません";
    private const string T_CONFIRM_REVIVE = "力を戻す惑星：";
    private const string T_OK = "置く";
    private const string T_PLACING = "コマを置いています…";
    private const string T_CHOOSE_TITLE = "力を戻す惑星を3つ選んでください";
    private const string T_CHOOSE_OK = "この3つに力を戻す";
    private const string T_CANCEL = "キャンセル";
    // 保険の置き場（何色でも置ける。2026-11-11）と、カケラの集まり具合
    private const string T_ANY_EMPTY = "まだ誰も置いていません。何色のコマでも置けます";
    private const string T_ANY_PLACED = "置かれているコマ：";
    private const string T_ANY_UNIT = "体";
    private const string T_HAVE = "持っている";
    private const string T_HAVE_SEP = "：";
    private const string T_WALLET = "所持金"; // 黄金の鉱山のシート（2026-11-12）
    // 黒コマ（2026-11-12）
    private const string T_WILD_TITLE = "黒コマを何色として置きますか？";
    private const string T_WILD_SUB_EMPTY = "選んだ色が、今日のこの置き場の色になります";
    private const string T_WILD_SUB_ANY = "選んだ色のコマとして数えます";
    private const string T_WILD_AS_A = "黒コマ（";
    private const string T_WILD_AS_B = "として）";
    private const string T_WILD_BUTTON = "として置く";
    private const string T_FOLLOW_BLACK = "（黒コマなら置けます）";

    /// <summary>この画面で出しうる文字（置き場の名前・効果は SpaceWorld.GlyphText、惑星の名前は SpacePlanets.GlyphText）</summary>
    public static string GlyphText => new StringBuilder()
        .Append(T_CAPTION).Append(T_TITLE).Append(T_MINE).Append(T_STATUS_PLACED_A).Append(T_STATUS_PLACED_B)
        .Append(T_STATUS_NOT_IN).Append(T_STATUS_READY).Append(T_STATUS_ADMIN).Append(T_RULES).Append(T_YOU)
        .Append(T_EMPTY_AREA)
        .Append(T_FOLLOW_A).Append(T_FOLLOW_B).Append(T_FOLLOW_C).Append(T_TODAY).Append(T_EXHAUSTED_A).Append(T_EXHAUSTED_B)
        .Append(T_OWNED).Append(T_PLACE).Append(T_PICK_COLOR).Append(T_BLOCK_PLACED).Append(T_BLOCK_NOT_IN)
        .Append(T_BLOCK_NO_EXHAUSTED).Append(T_BLOCK_NO_COLOR).Append(T_CONFIRM_B).Append(T_CONFIRM_NOTE)
        .Append(T_CONFIRM_NOTE_ADMIN)
        .Append(T_CONFIRM_REVIVE).Append(T_OK).Append(T_PLACING).Append(T_CHOOSE_TITLE).Append(T_CHOOSE_OK)
        .Append(T_CANCEL).Append(T_ANY_EMPTY).Append(T_ANY_PLACED).Append(T_ANY_UNIT).Append(T_HAVE).Append(T_HAVE_SEP)
        .Append(T_WALLET).Append(IgCurrency.GlyphText)
        .Append(T_WILD_TITLE).Append(T_WILD_SUB_EMPTY).Append(T_WILD_SUB_ANY).Append(T_WILD_AS_A).Append(T_WILD_AS_B)
        .Append(T_WILD_BUTTON).Append(T_FOLLOW_BLACK)
        .Append(SpaceWorld.GlyphText).Append(SpaceMeeples.GlyphText)
        .Append("0123456789×/ ・").ToString();

    // 配置（セーフエリア基準）
    private const float HEADER_H = 170f;
    private const float FOOTER_H = 250f;
    private const float MAP_ASPECT = 1080f / 1440f; // 地図の絵の 横÷縦
    private const float SPOT = 112f;
    private const float LABEL_W = 300f, LABEL_H = 92f;
    private const float LABEL_H_NOTE = 116f; // 3行目（補足）のある札
    private const float SHEET_H = 900f;
    private const float DETAIL_H = 76f;      // シートの説明（置き場の Detail）の分だけ高くする

    private sealed class Spot
    {
        public SpaceWorldArea Area;
        public Image Ring, Glow, Meeple, Badge;
        public GameObject EffectIcon, BadgeGo, You;
        public TextMeshProUGUI BadgeText;
        public Image[] Arcs;  // 保険の置き場: 赤・青・黄の3つの弧の輪
        public Image[] Minis; // 保険の置き場: 置かれた色ごとの小さなコマ（赤・青・黄の順）
    }

    private sealed class ColorCard
    {
        public PlanetColor Color;
        public bool Wild;   // 黒コマのカード
        public Color Tint;  // 選んだときの縁の色
        public CanvasGroup Group;
        public Image Stroke;
        public GameObject Glow;
        public TextMeshProUGUI Owned;
        public bool Allowed;
    }

    private Canvas _canvas;
    private Action _onClosed;
    private CanvasGroup _group;
    private RectTransform _safe;
    private float _safeW, _safeH;
    private readonly List<Spot> _spots = new List<Spot>();
    private readonly TextMeshProUGUI[] _mineCounts = new TextMeshProUGUI[4]; // 赤・青・黄・黒（黒は出すときだけ）
    private TextMeshProUGUI _status;
    private bool _closing, _busy;
    private float _nextTick;
    private bool _lastInStore, _lastPlaced;

    // 置き場のシート
    private GameObject _sheet;
    private SpaceWorldArea _sheetArea;
    private TextMeshProUGUI _sheetState, _blockText;
    private readonly List<ColorCard> _cards = new List<ColorCard>();
    private GameObject _placeWrap, _blockWrap;
    private CanvasGroup _placeGroup;
    private PlanetColor _selected = PlanetColor.None;
    private bool _wild;                                  // 黒コマを選んでいる
    private PlanetColor _wildColor = PlanetColor.None;   // 黒コマがなる色（色の決まっていない置き場で選んだ色）
    private GameObject _wildChooser;                     // 黒コマを何色として置くか選ぶ画面

    // 惑星の神殿: 力を戻す惑星を選ぶ画面
    private GameObject _chooser;
    private readonly List<string> _chosen = new List<string>();
    private TextMeshProUGUI _chooseCounter;
    private CanvasGroup _chooseOkGroup;
    private readonly Dictionary<string, (Image stroke, CanvasGroup vivid)> _chooseCells =
        new Dictionary<string, (Image, CanvasGroup)>();

    private static UserData Data => UserDataManager.instance != null ? UserDataManager.instance.UserData : null;

    public static SpaceWorldMap Open(Canvas canvas, Action onClosed)
    {
        var root = SpaceTheme.Rect("__WorldMap", canvas.transform);
        SpaceTheme.Stretch(root);
        root.SetAsLastSibling();
        SpaceTheme.SubCanvas(root.gameObject, true);
        var map = root.gameObject.AddComponent<SpaceWorldMap>();
        map._canvas = canvas;
        map._onClosed = onClosed;
        map.Build();
        return map;
    }

    /// <summary>戻る操作: 手前から閉じる（惑星を選ぶ画面 → 置き場のシート → 地図）</summary>
    public void HandleBack()
    {
        if (_busy) return;
        if (_wildChooser != null) CloseWildChooser();
        else if (_chooser != null) CloseChooser();
        else if (_sheet != null) CloseSheet();
        else Close();
    }

    public void Close()
    {
        if (_closing || _busy) return;
        _closing = true;
        StartCoroutine(FadeOutAndDestroy());
    }

    private IEnumerator FadeOutAndDestroy()
    {
        float from = _group.alpha;
        yield return SpaceTheme.Tween(0.25f, 0f, k => _group.alpha = from * (1f - k));
        Destroy(gameObject);
    }

    private void OnDestroy()
    {
        SpaceWorld.BoardChanged -= Refresh;
        JourneyService.Changed -= Refresh;
        SpaceWorld.StopListening();
        var cb = _onClosed;
        _onClosed = null;
        cb?.Invoke();
    }

    private void Update()
    {
        if (Time.unscaledTime < _nextTick) return;
        _nextTick = Time.unscaledTime + 1f;
        SpaceWorld.CheckDay(); // 朝4時に営業日が変わったら、新しい日の置き場へ
        bool inStore = SpaceWorld.InStore, placed = SpaceWorld.PlacedToday;
        if (inStore != _lastInStore || placed != _lastPlaced) Refresh(); // 入店・退店・日付の変わり目
    }

    // ================================================================
    // 組み立て
    // ================================================================
    private void Build()
    {
        var rt = (RectTransform)transform;
        _group = gameObject.AddComponent<CanvasGroup>();
        _group.alpha = 0f;

        var dim = SpaceTheme.Rect("Dim", rt);
        SpaceTheme.Stretch(dim);
        SpaceTheme.Img(dim, null, new Color(0.01f, 0.015f, 0.05f, 0.97f), true);

        var size = SpaceTheme.CanvasSize(_canvas);
        var insets = SpaceTheme.SafeInsets(size);
        _safeW = size.x;
        _safeH = size.y - insets.top - insets.bottom;
        _safe = SpaceTheme.Rect("Safe", rt);
        SpaceTheme.Stretch(_safe);
        _safe.gameObject.AddComponent<SpaceSafeArea>();

        var top = new Vector2(0.5f, 1f);
        var caption = SpaceTheme.Text(_safe, "Caption", T_CAPTION, 24, SpaceTheme.A(SpaceTheme.CYAN, 0.85f), top,
            new Vector2(0f, -70f), new Vector2(700f, 36f), FontStyles.Bold);
        SpaceTheme.Tracking(caption, 18f);
        SpaceTheme.Text(_safe, "Title", T_TITLE, 44, SpaceTheme.TEXT, top, new Vector2(0f, -124f),
            new Vector2(800f, 60f), FontStyles.Bold);
        BuildCloseButton(_safe, Close);

        // 地図（縦横比を保って、見出しと下の欄の間に収まる大きさ）
        float availH = _safeH - HEADER_H - FOOTER_H - 16f;
        float availW = _safeW - 48f;
        float mapW = Mathf.Min(availW, availH * MAP_ASPECT);
        float mapH = mapW / MAP_ASPECT;
        var mapPos = new Vector2(0f, -(HEADER_H + 8f));
        var frame = SpaceTheme.Rect("Map", _safe, top, mapPos, new Vector2(mapW, mapH), top);
        SpaceTheme.Rounded(frame, Color.white, 36f);
        frame.gameObject.AddComponent<Mask>().showMaskGraphic = false;
        var pic = SpaceTheme.Rect("Picture", frame);
        SpaceTheme.Stretch(pic);
        SpaceTheme.Img(pic, SpaceWorld.MapSprite, Color.white);
        var edge = SpaceTheme.Rect("Edge", _safe, top, mapPos, new Vector2(mapW, mapH), top);
        SpaceTheme.RoundedStroke(edge, SpaceTheme.STROKE, 36f);

        // 置き場（地図の外へはみ出してもよいように、切り抜かない別の層に置く）
        var spots = SpaceTheme.Rect("Spots", _safe, top, mapPos, new Vector2(mapW, mapH), top);
        foreach (var area in SpaceWorld.Areas) _spots.Add(BuildSpot(spots, area, mapW, mapH));

        BuildFooter();
        Refresh();

        SpaceWorld.BoardChanged += Refresh;
        JourneyService.Changed += Refresh;
        SpaceWorld.StartListening();
        StartCoroutine(SpaceTheme.Tween(0.3f, 0f, k => _group.alpha = k));
    }

    private static void BuildCloseButton(RectTransform parent, UnityEngine.Events.UnityAction onClick)
    {
        var rt = SpaceTheme.Rect("Close", parent, new Vector2(1f, 1f), new Vector2(-112f, -84f), new Vector2(104f, 104f));
        var face = SpaceTheme.Img(rt, SpaceSprites.Circle, new Color(0.05f, 0.07f, 0.19f, 0.85f), true);
        var rim = SpaceTheme.Rect("Rim", rt);
        SpaceTheme.Stretch(rim);
        SpaceTheme.Img(rim, SpaceSprites.RingSmall, SpaceTheme.STROKE);
        SpaceTheme.Bar(rt, Vector2.zero, 44f, 6f, 45f, SpaceTheme.TEXT);
        SpaceTheme.Bar(rt, Vector2.zero, 44f, 6f, -45f, SpaceTheme.TEXT);
        SpaceTheme.Clickable(rt.gameObject, face, onClick);
    }

    /// <summary>置き場1つ（丸い置き場・置かれた色の輪とコマ・数・自分が置いた印・名前と効果の札）</summary>
    private Spot BuildSpot(RectTransform parent, SpaceWorldArea area, float mapW, float mapH)
    {
        var s = new Spot { Area = area };
        float x = area.Pos.x * mapW, y = -area.Pos.y * mapH;
        var rt = SpaceTheme.Rect("Spot" + area.Index, parent, new Vector2(0f, 1f), new Vector2(x, y),
            new Vector2(SPOT, SPOT));

        var glow = SpaceTheme.Rect("Glow", rt, SpaceTheme.CENTER, Vector2.zero, new Vector2(SPOT * 2.3f, SPOT * 2.3f));
        s.Glow = SpaceTheme.Img(glow, SpaceSprites.Glow, SpaceTheme.A(area.Accent, 0.35f));
        var faceRt = SpaceTheme.Rect("Face", rt);
        SpaceTheme.Stretch(faceRt);
        var face = SpaceTheme.Img(faceRt, SpaceSprites.Circle, new Color(0.04f, 0.06f, 0.16f, 0.92f), true);
        SpaceTheme.Clickable(rt.gameObject, face, () => OpenSheet(area));
        var ring = SpaceTheme.Rect("Ring", rt);
        SpaceTheme.Stretch(ring);
        s.Ring = SpaceTheme.Img(ring, SpaceSprites.Ring, Color.white);
        if (area.AnyColor)
        {
            // 何色でも置ける保険の置き場: 輪を赤・青・黄の3つの弧にする（すき間は弧の間に半分ずつ）
            s.Ring.enabled = false;
            const float GAP = 0.03f;
            s.Arcs = new Image[PlanetColorOrder.All.Length];
            for (int k = 0; k < s.Arcs.Length; k++)
            {
                var arc = SpaceTheme.Rect("Arc" + k, rt);
                SpaceTheme.Stretch(arc);
                arc.localEulerAngles = new Vector3(0f, 0f, -360f * k / s.Arcs.Length - GAP * 180f);
                var img = SpaceTheme.Img(arc, SpaceSprites.Ring, PlanetColors.Of(PlanetColorOrder.All[k]));
                img.type = Image.Type.Filled;
                img.fillMethod = Image.FillMethod.Radial360;
                img.fillOrigin = (int)Image.Origin360.Top;
                img.fillClockwise = true;
                img.fillAmount = 1f / s.Arcs.Length - GAP;
                s.Arcs[k] = img;
            }
        }

        // まだ誰も置いていない: 効果の印 ／ 置かれている: その色のコマ（保険の置き場は、置かれた色ごとの小さなコマ）
        var icon = SpaceTheme.Rect("Effect", rt, SpaceTheme.CENTER, Vector2.zero, new Vector2(SPOT, SPOT));
        BuildEffectIcon(icon, area);
        s.EffectIcon = icon.gameObject;
        var meeple = SpaceTheme.Rect("Meeple", rt, SpaceTheme.CENTER, new Vector2(0f, 2f), new Vector2(76f, 76f));
        s.Meeple = SpaceTheme.Img(meeple, null, Color.white);
        s.Meeple.preserveAspect = true;
        if (area.AnyColor)
        {
            s.Meeple.enabled = false;
            s.Minis = new Image[PlanetColorOrder.All.Length];
            for (int k = 0; k < s.Minis.Length; k++)
            {
                var def = SpaceMeeples.Of(PlanetColorOrder.All[k]);
                var mini = SpaceTheme.Rect("Mini" + k, rt, SpaceTheme.CENTER, Vector2.zero, new Vector2(48f, 48f));
                s.Minis[k] = SpaceTheme.Img(mini, def?.Icon, Color.white);
                s.Minis[k].preserveAspect = true;
                s.Minis[k].enabled = false;
            }
        }

        // 置かれた数（右上の札。真ん中のコマに重ならないよう、丸の外側へ寄せる）
        var badge = SpaceTheme.Rect("Count", rt, new Vector2(1f, 1f), new Vector2(22f, -2f), new Vector2(76f, 46f));
        s.Badge = SpaceTheme.Rounded(badge, Color.white, 23f);
        s.BadgeText = SpaceTheme.Text(badge, "Label", "", 28, Color.white, FontStyles.Bold);
        SpaceTheme.Stretch(s.BadgeText.rectTransform);
        s.BadgeGo = badge.gameObject;

        // 自分が今日置いた置き場（左上の札）
        var you = SpaceTheme.Rect("You", rt, new Vector2(0f, 1f), new Vector2(-16f, 6f), new Vector2(96f, 38f));
        SpaceTheme.Rounded(you, SpaceTheme.GOLD, 19f);
        var youText = SpaceTheme.Text(you, "Label", T_YOU, 22, SpaceTheme.TEXT_DARK, FontStyles.Bold);
        SpaceTheme.Stretch(youText.rectTransform);
        s.You = you.gameObject;

        // 名前と効果の札（置き場の下。地図の端からはみ出さないように寄せる）。補足（Note）のある置き場は3行
        bool hasNote = !string.IsNullOrEmpty(area.Note);
        float labelH = hasNote ? LABEL_H_NOTE : LABEL_H;
        float lx = Mathf.Clamp(x, LABEL_W * 0.5f + 6f, mapW - LABEL_W * 0.5f - 6f) - x;
        var label = SpaceTheme.Rect("Label", rt, new Vector2(0.5f, 0f), new Vector2(lx, -8f),
            new Vector2(LABEL_W, labelH), new Vector2(0.5f, 1f));
        var labelFace = SpaceTheme.Glass(label, 26f, new Color(0.03f, 0.05f, 0.14f, 0.86f), SpaceTheme.A(area.Accent, 0.55f),
            true);
        SpaceTheme.Clickable(label.gameObject, labelFace, () => OpenSheet(area), false);
        var top = new Vector2(0.5f, 1f);
        var name = SpaceTheme.Text(label, "Name", area.Name, 30, SpaceTheme.TEXT, top,
            new Vector2(0f, hasNote ? -24f : -26f), new Vector2(LABEL_W - 20f, hasNote ? 38f : 40f), FontStyles.Bold);
        name.enableAutoSizing = true;
        name.fontSizeMin = 20f;
        name.fontSizeMax = 30f;
        var fx = SpaceTheme.Text(label, "Effect", area.Text, 22, SpaceTheme.A(area.Accent, 0.95f), top,
            new Vector2(0f, hasNote ? -58f : -(LABEL_H - 24f)), new Vector2(LABEL_W - 20f, hasNote ? 30f : 32f),
            FontStyles.Bold);
        fx.enableAutoSizing = true;
        fx.fontSizeMin = 15f;
        fx.fontSizeMax = 22f;
        if (hasNote)
        {
            var note = SpaceTheme.Text(label, "Note", area.Note, 19, SpaceTheme.TEXT_SUB, top, new Vector2(0f, -89f),
                new Vector2(LABEL_W - 20f, 26f), FontStyles.Bold);
            note.enableAutoSizing = true;
            note.fontSizeMin = 13f;
            note.fontSizeMax = 19f;
        }
        return s;
    }

    /// <summary>効果の印（流れ星＝きらめき／惑星／3色のコマ／5%OFFチケット／星のカケラ）</summary>
    private static void BuildEffectIcon(RectTransform rt, SpaceWorldArea area)
    {
        switch (area.Effect)
        {
            case WorldEffect.Star:
            {
                var img = SpaceTheme.Rect("Sparkle", rt, SpaceTheme.CENTER, Vector2.zero, new Vector2(64f, 64f));
                SpaceTheme.Img(img, SpaceSprites.Sparkle, area.Accent);
                break;
            }
            case WorldEffect.Planets:
            {
                var img = SpaceTheme.Rect("Planet", rt, SpaceTheme.CENTER, Vector2.zero, new Vector2(62f, 62f));
                SpaceTheme.Img(img, SpaceSprites.PlanetIcon, area.Accent).preserveAspect = true;
                break;
            }
            case WorldEffect.Meeples:
            {
                float[] xs = { -20f, 0f, 20f };
                for (int i = 0; i < SpaceMeeples.All.Length; i++)
                {
                    var m = SpaceMeeples.All[i];
                    var img = SpaceTheme.Rect("Meeple" + i, rt, SpaceTheme.CENTER, new Vector2(xs[i], i == 1 ? 6f : -4f),
                        new Vector2(46f, 46f));
                    var image = SpaceTheme.Img(img, m.Icon, Color.white);
                    image.preserveAspect = true;
                    image.enabled = m.Icon != null;
                }
                break;
            }
            case WorldEffect.Coupon:
            {
                var coupon = SpaceCoupons.ByField("five_coupon");
                SpaceInventoryPanel.RoundedIcon(rt, coupon?.Icon(), SpaceTheme.CENTER, Vector2.zero, 64f, 14f);
                break;
            }
            case WorldEffect.StarFragment:
            {
                var img = SpaceTheme.Rect("StarFragment", rt, SpaceTheme.CENTER, Vector2.zero, new Vector2(80f, 80f));
                var image = SpaceTheme.Img(img, SpaceWorld.StarFragmentIcon, Color.white);
                image.preserveAspect = true;
                image.enabled = SpaceWorld.StarFragmentIcon != null;
                break;
            }
            case WorldEffect.Gold:
            {
                // 黄金の鉱山: G の金貨（2026-11-12）
                var img = SpaceTheme.Rect("Coin", rt, SpaceTheme.CENTER, Vector2.zero, new Vector2(84f, 84f));
                var image = SpaceTheme.Img(img, IgCurrency.Icon, Color.white);
                image.preserveAspect = true;
                image.enabled = IgCurrency.Icon != null;
                break;
            }
        }
    }

    /// <summary>下の欄: 持っているコマの数・今の状態・決まり</summary>
    private void BuildFooter()
    {
        var footer = SpaceTheme.Rect("Footer", _safe, new Vector2(0.5f, 0f), new Vector2(0f, 24f),
            new Vector2(_safeW - 64f, FOOTER_H - 34f), new Vector2(0.5f, 0f));
        SpaceTheme.Glass(footer, 40f, new Color(0.05f, 0.07f, 0.19f, 0.8f));

        SpaceTheme.Text(footer, "Mine", T_MINE, 24, SpaceTheme.TEXT_MUTED, new Vector2(0f, 1f), new Vector2(40f, -46f),
            new Vector2(260f, 36f), FontStyles.Bold, TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
        var defs = new List<SpaceMeepleDef>(SpaceMeeples.All);
        if (SpaceMeeples.ShowBlack(Data)) defs.Add(SpaceMeeples.Black); // 黒コマ（2026-11-12）
        float step = defs.Count > 3 ? 158f : 170f, chipW = defs.Count > 3 ? 140f : 150f;
        for (int i = 0; i < defs.Count; i++)
        {
            var m = defs[i];
            float x = -40f - (defs.Count - 1 - i) * step;
            var chip = SpaceTheme.Rect("Chip" + i, footer, new Vector2(1f, 1f), new Vector2(x, -46f), new Vector2(chipW, 60f),
                new Vector2(1f, 0.5f));
            var icon = SpaceTheme.Rect("Icon", chip, new Vector2(0f, 0.5f), new Vector2(28f, 0f), new Vector2(56f, 56f));
            var img = SpaceTheme.Img(icon, m.Icon, Color.white);
            img.preserveAspect = true;
            img.enabled = m.Icon != null;
            _mineCounts[m.Wild ? 3 : i] = SpaceTheme.Text(chip, "Count", "", 36, SpaceTheme.GOLD, new Vector2(0f, 0.5f),
                new Vector2(64f, 0f), new Vector2(90f, 50f), FontStyles.Bold, TextAlignmentOptions.Left,
                new Vector2(0f, 0.5f));
        }

        _status = SpaceTheme.Text(footer, "Status", "", 30, SpaceTheme.TEXT, new Vector2(0.5f, 0.5f), new Vector2(0f, -4f),
            new Vector2(_safeW - 120f, 44f), FontStyles.Bold);
        _status.enableAutoSizing = true;
        _status.fontSizeMin = 20f;
        _status.fontSizeMax = 30f;
        var rules = SpaceTheme.Text(footer, "Rules", T_RULES, 22, SpaceTheme.TEXT_MUTED, new Vector2(0.5f, 0f),
            new Vector2(0f, 34f), new Vector2(_safeW - 120f, 34f), FontStyles.Bold);
        rules.enableAutoSizing = true;
        rules.fontSizeMin = 15f;
        rules.fontSizeMax = 22f;
    }

    // ================================================================
    // 表示の更新（同期の知らせ・入店退店・置いたあと）
    // ================================================================
    private void Refresh()
    {
        if (this == null || _closing) return;
        var b = SpaceWorld.Board ?? new SpaceWorld.WorldBoard();
        int mine = SpaceWorld.PlacedAreaToday;
        foreach (var s in _spots)
        {
            int i = s.Area.Index;
            var c = b.Colors[i];
            int n = b.Counts[i];
            if (s.Area.AnyColor)
            {
                RefreshFreeSpot(s, b, n, mine == i);
                continue;
            }
            bool has = c != PlanetColor.None;
            var tint = has ? PlanetColors.Of(c) : s.Area.Accent;
            s.Ring.color = has ? tint : SpaceTheme.A(SpaceTheme.TEXT, 0.45f);
            s.Glow.color = SpaceTheme.A(tint, has ? 0.45f : 0.3f);
            s.EffectIcon.SetActive(!has);
            s.Meeple.enabled = has;
            if (has) s.Meeple.sprite = SpaceMeeples.Of(c)?.Icon;
            s.BadgeGo.SetActive(n > 0);
            s.Badge.color = has ? tint : SpaceTheme.TEXT_SUB;
            s.BadgeText.text = "×" + n;
            s.BadgeText.color = c == PlanetColor.Yellow ? SpaceTheme.TEXT_DARK : Color.white;
            s.You.SetActive(mine == i);
        }

        var d = Data;
        for (int i = 0; i < _mineCounts.Length; i++)
        {
            if (_mineCounts[i] == null) continue; // 黒コマは出していないとき
            var md = i < SpaceMeeples.All.Length ? SpaceMeeples.All[i] : SpaceMeeples.Black;
            int n = d != null ? Mathf.Max(0, md.Get(d)) : 0;
            _mineCounts[i].text = "×" + n;
            _mineCounts[i].color = n > 0 ? SpaceTheme.GOLD : SpaceTheme.TEXT_MUTED;
        }

        _lastInStore = SpaceWorld.InStore;
        _lastPlaced = SpaceWorld.PlacedToday;
        if (_lastPlaced && !SpaceWorld.Unlimited)
        {
            var placed = mine >= 0 && mine < SpaceWorld.Areas.Length ? SpaceWorld.Areas[mine].Name : "";
            _status.text = T_STATUS_PLACED_A + placed + T_STATUS_PLACED_B;
            _status.color = SpaceTheme.TEXT_SUB;
        }
        else if (!_lastInStore)
        {
            _status.text = T_STATUS_NOT_IN;
            _status.color = SpaceTheme.TEXT_SUB;
        }
        else
        {
            _status.text = SpaceWorld.Unlimited ? T_STATUS_ADMIN : T_STATUS_READY;
            _status.color = SpaceTheme.GOLD;
        }

        if (_sheet != null) RefreshSheet();
    }

    /// <summary>
    /// 保険の置き場（何色でも置ける）: まだ誰も置いていなければ効果の印、置かれていれば置かれた色ごとの小さなコマを並べる。
    /// 数の札は全色の合計（金色）
    /// </summary>
    private static void RefreshFreeSpot(Spot s, SpaceWorld.WorldBoard b, int total, bool mine)
    {
        int i = s.Area.Index;
        bool has = total > 0;
        foreach (var arc in s.Arcs) arc.color = SpaceTheme.A(arc.color, has ? 1f : 0.6f);
        s.Glow.color = SpaceTheme.A(s.Area.Accent, has ? 0.45f : 0.3f);
        s.EffectIcon.SetActive(!has);

        // 置かれた色だけを、数に合わせた大きさと並びで出す（1色: 真ん中に大きく／2色: 左右／3色: 山形）
        var shown = new List<int>();
        for (int k = 0; k < s.Minis.Length; k++)
            if (b.ByColor[i, k] > 0) shown.Add(k);
        float size = shown.Count <= 1 ? 76f : shown.Count == 2 ? 58f : 48f;
        for (int k = 0; k < s.Minis.Length; k++) s.Minis[k].enabled = false;
        for (int j = 0; j < shown.Count; j++)
        {
            var img = s.Minis[shown[j]];
            img.enabled = img.sprite != null;
            Vector2 pos = shown.Count == 1 ? new Vector2(0f, 2f)
                : shown.Count == 2 ? new Vector2(j == 0 ? -17f : 17f, 0f)
                : new Vector2((j - 1) * 22f, j == 1 ? 7f : -5f);
            img.rectTransform.anchoredPosition = pos;
            img.rectTransform.sizeDelta = new Vector2(size, size);
        }

        s.BadgeGo.SetActive(total > 0);
        s.Badge.color = s.Area.Accent;
        s.BadgeText.text = "×" + total;
        s.BadgeText.color = SpaceTheme.TEXT_DARK;
        s.You.SetActive(mine);
    }

    // ================================================================
    // 置き場のシート
    // ================================================================
    private void OpenSheet(SpaceWorldArea area)
    {
        if (_busy || _closing || _sheet != null) return;
        _sheetArea = area;
        _selected = PlanetColor.None;
        _wild = false;
        _wildColor = PlanetColor.None;
        _cards.Clear();

        var root = SpaceTheme.Rect("Sheet", transform);
        SpaceTheme.Stretch(root);
        root.SetAsLastSibling();
        _sheet = root.gameObject;
        var group = _sheet.AddComponent<CanvasGroup>();
        var dim = SpaceTheme.Rect("Dim", root);
        SpaceTheme.Stretch(dim);
        var dimImg = SpaceTheme.Img(dim, null, new Color(0f, 0f, 0.02f, 0.6f), true);
        SpaceTheme.Clickable(dim.gameObject, dimImg, CloseSheet, false);

        var safe = SpaceTheme.Rect("Safe", root);
        SpaceTheme.Stretch(safe);
        safe.gameObject.AddComponent<SpaceSafeArea>();
        // 説明（Detail）のある置き場は、その分だけシートを高くして、下の段をずらす
        bool hasDetail = !string.IsNullOrEmpty(area.Detail);
        float extra = hasDetail ? DETAIL_H : 0f;
        var panel = SpaceTheme.Rect("Panel", safe, new Vector2(0.5f, 0f), new Vector2(0f, 24f),
            new Vector2(_safeW - 48f, SHEET_H + extra), new Vector2(0.5f, 0f));
        SpaceTheme.Glass(panel, 52f, SpaceTheme.GLASS_DEEP, SpaceTheme.A(area.Accent, 0.6f), true);
        BuildCloseButtonSmall(panel, CloseSheet);

        var top = new Vector2(0.5f, 1f);
        var en = SpaceTheme.Text(panel, "En", area.En, 24, SpaceTheme.A(area.Accent, 0.95f), top, new Vector2(0f, -60f),
            new Vector2(700f, 36f), FontStyles.Bold);
        SpaceTheme.Tracking(en, 18f);
        SpaceTheme.Text(panel, "Name", area.Name, 48, SpaceTheme.TEXT, top, new Vector2(0f, -114f), new Vector2(800f, 64f),
            FontStyles.Bold);
        var fx = SpaceTheme.Text(panel, "Effect", area.Text, 34, SpaceTheme.GOLD, top, new Vector2(0f, -178f),
            new Vector2(_safeW - 140f, 48f), FontStyles.Bold);
        fx.enableAutoSizing = true;
        fx.fontSizeMin = 24f;
        fx.fontSizeMax = 34f;
        if (hasDetail)
        {
            // 置き場の説明（2枚で完成・何色でも置ける・5個で流れ星 など。2026-11-11）
            var detail = SpaceTheme.Text(panel, "Detail", area.Detail, 25, SpaceTheme.TEXT, top, new Vector2(0f, -240f),
                new Vector2(_safeW - 140f, 68f), FontStyles.Bold);
            detail.textWrappingMode = TextWrappingModes.Normal;
            detail.enableAutoSizing = true;
            detail.fontSizeMin = 18f;
            detail.fontSizeMax = 25f;
        }
        _sheetState = SpaceTheme.Text(panel, "State", "", 26, SpaceTheme.TEXT_SUB, top, new Vector2(0f, -(258f + extra)),
            new Vector2(_safeW - 140f, 84f), FontStyles.Bold);
        _sheetState.textWrappingMode = TextWrappingModes.Normal;
        _sheetState.enableAutoSizing = true;
        _sheetState.fontSizeMin = 18f;
        _sheetState.fontSizeMax = 26f;

        // 色を選ぶ（3枚のカード。黒コマを持っているか、切り札座が完成していれば4枚目に黒コマ）
        var defs = new List<SpaceMeepleDef>(SpaceMeeples.All);
        if (SpaceMeeples.ShowBlack(Data)) defs.Add(SpaceMeeples.Black);
        float cw = defs.Count > 3 ? 222f : 280f, gap = defs.Count > 3 ? 16f : 22f;
        const float CH = 250f;
        float total = defs.Count * cw + (defs.Count - 1) * gap;
        for (int i = 0; i < defs.Count; i++)
        {
            float x = -total * 0.5f + cw * 0.5f + i * (cw + gap);
            _cards.Add(BuildColorCard(panel, defs[i], new Vector2(x, -(330f + extra + CH * 0.5f)), new Vector2(cw, CH)));
        }

        // 置くボタン／置けない理由
        var place = SpaceTheme.Rect("Place", panel, new Vector2(0.5f, 0f), new Vector2(0f, 56f), new Vector2(_safeW - 160f, 128f),
            new Vector2(0.5f, 0f));
        var face = SpaceTheme.PrimaryFace(place);
        SpaceTheme.Clickable(place.gameObject, face, OnPlacePressed);
        var label = SpaceTheme.Text(place, "Label", T_PLACE, 40, SpaceTheme.TEXT_DARK, FontStyles.Bold);
        SpaceTheme.Stretch(label.rectTransform);
        _placeGroup = place.gameObject.AddComponent<CanvasGroup>();
        _placeWrap = place.gameObject;

        var block = SpaceTheme.Rect("Block", panel, new Vector2(0.5f, 0f), new Vector2(0f, 56f), new Vector2(_safeW - 160f, 128f),
            new Vector2(0.5f, 0f));
        SpaceTheme.Glass(block, 46f, new Color(0.05f, 0.07f, 0.19f, 0.7f));
        var lockIcon = SpaceTheme.Rect("Lock", block, new Vector2(0f, 0.5f), new Vector2(56f, 0f), new Vector2(44f, 44f));
        SpaceTheme.Img(lockIcon, SpaceSprites.Lock, SpaceTheme.TEXT_MUTED);
        _blockText = SpaceTheme.Text(block, "Label", "", 30, SpaceTheme.TEXT_SUB, SpaceTheme.CENTER, new Vector2(24f, 0f),
            new Vector2(_safeW - 320f, 80f), FontStyles.Bold);
        _blockText.textWrappingMode = TextWrappingModes.Normal;
        _blockText.enableAutoSizing = true;
        _blockText.fontSizeMin = 20f;
        _blockText.fontSizeMax = 30f;
        _blockWrap = block.gameObject;

        RefreshSheet();

        group.alpha = 0f;
        var basePos = panel.anchoredPosition;
        StartCoroutine(SpaceTheme.Tween(0.25f, 0f, k =>
        {
            if (group == null) return;
            group.alpha = k;
            panel.anchoredPosition = basePos + new Vector2(0f, -70f * (1f - k));
        }));
    }

    private static void BuildCloseButtonSmall(RectTransform parent, UnityEngine.Events.UnityAction onClick)
    {
        var rt = SpaceTheme.Rect("Close", parent, new Vector2(1f, 1f), new Vector2(-76f, -76f), new Vector2(88f, 88f));
        var face = SpaceTheme.Img(rt, SpaceSprites.Circle, new Color(0.09f, 0.12f, 0.30f, 0.65f), true);
        var rim = SpaceTheme.Rect("Rim", rt);
        SpaceTheme.Stretch(rim);
        SpaceTheme.Img(rim, SpaceSprites.RingSmall, SpaceTheme.STROKE);
        SpaceTheme.Bar(rt, Vector2.zero, 38f, 6f, 45f, SpaceTheme.TEXT_SUB);
        SpaceTheme.Bar(rt, Vector2.zero, 38f, 6f, -45f, SpaceTheme.TEXT_SUB);
        SpaceTheme.Clickable(rt.gameObject, face, onClick);
    }

    private ColorCard BuildColorCard(RectTransform parent, SpaceMeepleDef m, Vector2 pos, Vector2 size)
    {
        var card = new ColorCard { Color = m.Color, Wild = m.Wild, Tint = m.Tint };
        var rt = SpaceTheme.Rect("Color_" + m.Field, parent, new Vector2(0.5f, 1f), pos, size);
        card.Group = rt.gameObject.AddComponent<CanvasGroup>();
        var glow = SpaceTheme.Rect("Glow", rt, SpaceTheme.CENTER, Vector2.zero, size + new Vector2(110f, 110f));
        SpaceTheme.Img(glow, SpaceSprites.Glow, SpaceTheme.A(m.Tint, 0.35f));
        card.Glow = glow.gameObject;
        var faceRt = SpaceTheme.Rect("Face", rt);
        SpaceTheme.Stretch(faceRt);
        var face = SpaceTheme.Rounded(faceRt, new Color(0.07f, 0.09f, 0.24f, 0.85f), 36f, true);
        SpaceTheme.Clickable(rt.gameObject, face, () => SelectColor(card));
        var stroke = SpaceTheme.Rect("Stroke", rt);
        SpaceTheme.Stretch(stroke);
        card.Stroke = SpaceTheme.RoundedStroke(stroke, SpaceTheme.STROKE, 36f);

        var icon = SpaceTheme.Rect("Icon", rt, new Vector2(0.5f, 1f), new Vector2(0f, -76f), new Vector2(110f, 110f));
        var img = SpaceTheme.Img(icon, m.Icon, Color.white);
        img.preserveAspect = true;
        img.enabled = m.Icon != null;
        SpaceTheme.Text(rt, "Name", m.Name, 34, SpaceTheme.TEXT, new Vector2(0.5f, 0f), new Vector2(0f, 76f),
            new Vector2(size.x - 20f, 46f), FontStyles.Bold);
        card.Owned = SpaceTheme.Text(rt, "Owned", "", 24, SpaceTheme.TEXT_SUB, new Vector2(0.5f, 0f), new Vector2(0f, 34f),
            new Vector2(size.x - 20f, 34f), FontStyles.Bold);
        card.Owned.enableAutoSizing = true;
        card.Owned.fontSizeMin = 16f;
        card.Owned.fontSizeMax = 24f;
        return card;
    }

    private void RefreshSheet()
    {
        if (_sheet == null || _sheetArea == null) return;
        var area = _sheetArea;
        var b = SpaceWorld.Board ?? new SpaceWorld.WorldBoard();
        var c = b.Colors[area.Index];
        int n = b.Counts[area.Index];
        string state;
        if (area.AnyColor)
        {
            // 保険の置き場: 色は決まらない。置かれた数を色ごとに出す
            if (n <= 0) state = T_ANY_EMPTY;
            else
            {
                var parts = PlanetColorOrder.All.Select(pc =>
                    SpaceWorld.ColorName(pc) + " " + b.CountOf(area.Index, pc) + T_ANY_UNIT);
                state = T_ANY_PLACED + string.Join(" ・ ", parts);
            }
        }
        else
        {
            state = c == PlanetColor.None
                ? T_EMPTY_AREA
                : SpaceWorld.ColorName(c) + T_FOLLOW_A + n + T_FOLLOW_B + SpaceWorld.ColorName(c) + T_FOLLOW_C
                  + (SpaceMeeples.ShowBlack(Data) ? T_FOLLOW_BLACK : ""); // 黒コマは色が決まった置き場にも置ける
        }
        int exhausted = area.Effect == WorldEffect.Planets ? SpaceWorld.ExhaustedPlanets().Count : -1;
        if (exhausted >= 0) state += "\n" + T_EXHAUSTED_A + exhausted + T_EXHAUSTED_B;
        var d = Data;
        // カケラの集まり具合（持っている5%OFFチケットのカケラ：1 / 2 ・ 持っている星のカケラ：3 / 5）
        if (d != null && area.Effect == WorldEffect.Coupon)
            state += "\n" + T_HAVE + SpaceWorld.FRAGMENT_NAME + T_HAVE_SEP
                     + Mathf.Clamp(d.FiveFragment, 0, SpaceWorld.FRAGMENTS_PER_COUPON) + " / " + SpaceWorld.FRAGMENTS_PER_COUPON;
        if (d != null && area.Effect == WorldEffect.StarFragment)
            state += "\n" + T_HAVE + SpaceWorld.STAR_FRAGMENT_NAME + T_HAVE_SEP
                     + Mathf.Clamp(d.StarFragment, 0, SpaceWorld.STAR_FRAGMENTS_PER_STAR) + " / "
                     + SpaceWorld.STAR_FRAGMENTS_PER_STAR;
        if (d != null && area.Effect == WorldEffect.Gold)
            state += "\n" + T_WALLET + T_HAVE_SEP + IgCurrency.Format(IgCurrency.Balance); // 所持金：12G
        _sheetState.text = state;

        int allowedCount = 0;
        ColorCard only = null;
        foreach (var card in _cards)
        {
            var m = card.Wild ? SpaceMeeples.Black : SpaceMeeples.Of(card.Color);
            int owned = d != null && m != null ? Mathf.Max(0, m.Get(d)) : 0;
            // 黒コマはどの置き場にも置ける（色が決まっていればその色になる）
            card.Allowed = (card.Wild || area.AnyColor || c == PlanetColor.None || c == card.Color) && owned > 0;
            card.Owned.text = T_OWNED + owned;
            if (card.Allowed)
            {
                allowedCount++;
                only = card;
            }
            else if (card.Wild) _wild = false;
            else if (!_wild && _selected == card.Color) _selected = PlanetColor.None;
        }
        // 1つしか置けないなら選んでおく
        if (!_wild && _selected == PlanetColor.None && allowedCount == 1)
        {
            if (only.Wild) _wild = true;
            else _selected = only.Color;
        }

        string blockMsg = !SpaceWorld.CanPlaceToday ? T_BLOCK_PLACED
            : !SpaceWorld.InStore ? T_BLOCK_NOT_IN
            : exhausted == 0 ? T_BLOCK_NO_EXHAUSTED
            : allowedCount == 0 ? T_BLOCK_NO_COLOR
            : null;
        _placeWrap.SetActive(blockMsg == null);
        _blockWrap.SetActive(blockMsg != null);
        if (blockMsg != null) _blockText.text = blockMsg;
        _placeGroup.alpha = _selected != PlanetColor.None || _wild ? 1f : 0.4f;

        foreach (var card in _cards)
        {
            bool on = card.Wild ? _wild : !_wild && card.Color == _selected;
            card.Group.alpha = card.Allowed ? 1f : 0.3f;
            card.Glow.SetActive(on);
            card.Stroke.color = on ? SpaceTheme.A(card.Tint, 0.95f) : SpaceTheme.STROKE;
        }
    }

    private void SelectColor(ColorCard card)
    {
        if (_busy || card == null) return;
        if (!card.Allowed)
        {
            // 別の色が置かれている置き場（マストフォロー）か、その色を持っていないか
            var c = (SpaceWorld.Board ?? new SpaceWorld.WorldBoard()).Colors[_sheetArea.Index];
            string msg = card.Wild ? SpaceWorld.T_ERR_NO_BLACK
                : c != PlanetColor.None && c != card.Color ? T_TODAY + SpaceWorld.ColorName(c) + T_FOLLOW_C
                : SpaceWorld.T_ERR_NO_MEEPLE;
            SpaceTheme.Toast(msg, _canvas);
            return;
        }
        _wild = card.Wild;
        _selected = card.Wild ? PlanetColor.None : card.Color;
        _wildColor = PlanetColor.None;
        RefreshSheet();
    }

    private void CloseSheet()
    {
        if (_busy || _sheet == null) return;
        CloseWildChooser();
        Destroy(_sheet);
        _sheet = null;
        _sheetArea = null;
        _wild = false;
        _wildColor = PlanetColor.None;
        _cards.Clear();
    }

    private void OnPlacePressed()
    {
        if (_busy || _sheetArea == null) return;
        if (_selected == PlanetColor.None && !_wild)
        {
            SpaceTheme.Toast(T_PICK_COLOR, _canvas);
            return;
        }
        if (_wild)
        {
            // 黒コマ: 色が決まった置き場ならその色。まだ決まっていない置き場・星くずの島なら、何色として置くかを先に選ぶ
            var placed = (SpaceWorld.Board ?? new SpaceWorld.WorldBoard()).Colors[_sheetArea.Index];
            if (!_sheetArea.AnyColor && placed != PlanetColor.None) _wildColor = placed;
            else if (_wildColor == PlanetColor.None)
            {
                OpenWildChooser();
                return;
            }
        }
        if (_sheetArea.Effect == WorldEffect.Planets)
        {
            var candidates = SpaceWorld.ExhaustedPlanets();
            if (candidates.Count == 0)
            {
                SpaceTheme.Toast(SpaceWorld.T_ERR_NO_EXHAUSTED, _canvas);
                return;
            }
            if (candidates.Count > SpaceWorld.REVIVE_COUNT)
            {
                OpenChooser(candidates);
                return;
            }
            ConfirmPlace(candidates.Select(p => p.Id).ToList());
            return;
        }
        ConfirmPlace(null);
    }

    private void ConfirmPlace(List<string> revive)
    {
        var area = _sheetArea;
        bool wild = _wild;
        var color = wild ? _wildColor : _selected;
        var meeple = SpaceMeeples.Of(color);
        if (area == null || meeple == null) return;
        string body = area.Name + "：" + area.Text + "\n" + (SpaceWorld.Unlimited ? T_CONFIRM_NOTE_ADMIN : T_CONFIRM_NOTE);
        if (revive != null && revive.Count > 0)
            body += "\n" + T_CONFIRM_REVIVE + string.Join("・", revive.Select(id => SpacePlanets.Find(id)?.Name ?? id));
        // 黒コマは「黒コマ（赤として）を置きますか？」
        string title = wild ? T_WILD_AS_A + SpaceWorld.ColorName(color) + T_WILD_AS_B + T_CONFIRM_B : meeple.Name + T_CONFIRM_B;
        SpaceDialog.Confirm(_canvas, title, body, T_OK, false,
            () => PlaceAsync(area, color, revive, wild).Forget(),
            () => _wildColor = PlanetColor.None, T_CANCEL); // やめたら、次は色を選び直す
    }

    // ================================================================
    // 黒コマを何色として置くか選ぶ（まだ色の決まっていない置き場・星くずの島。2026-11-12）
    // ================================================================
    private void OpenWildChooser()
    {
        if (_wildChooser != null || _sheetArea == null) return;
        bool any = _sheetArea.AnyColor;
        var root = SpaceTheme.Rect("WildChooser", transform);
        SpaceTheme.Stretch(root);
        root.SetAsLastSibling();
        _wildChooser = root.gameObject;
        var group = _wildChooser.AddComponent<CanvasGroup>();
        var dim = SpaceTheme.Rect("Dim", root);
        SpaceTheme.Stretch(dim);
        var dimImg = SpaceTheme.Img(dim, null, new Color(0f, 0f, 0.02f, 0.72f), true);
        SpaceTheme.Clickable(dim.gameObject, dimImg, CloseWildChooser, false);

        const float BW = 250f, BH = 220f, GAP = 24f, H = 760f;
        var black = SpaceMeeples.Black;
        var panel = SpaceTheme.Rect("Panel", root, SpaceTheme.CENTER, Vector2.zero, new Vector2(Mathf.Min(_safeW - 64f, 960f), H));
        SpaceTheme.Glass(panel, 52f, SpaceTheme.GLASS_DEEP, SpaceTheme.A(black.Tint, 0.6f), true);
        var top = new Vector2(0.5f, 1f);
        var iconGlow = SpaceTheme.Rect("IconGlow", panel, top, new Vector2(0f, -104f), new Vector2(220f, 220f));
        SpaceTheme.Img(iconGlow, SpaceSprites.Glow, SpaceTheme.A(black.Tint, 0.3f));
        var icon = SpaceTheme.Rect("Icon", panel, top, new Vector2(0f, -104f), new Vector2(120f, 120f));
        var iconImg = SpaceTheme.Img(icon, black.Icon, Color.white);
        iconImg.preserveAspect = true;
        iconImg.enabled = black.Icon != null;
        var title = SpaceTheme.Text(panel, "Title", T_WILD_TITLE, 40, SpaceTheme.TEXT, top, new Vector2(0f, -212f),
            new Vector2(Mathf.Min(_safeW - 140f, 880f), 56f), FontStyles.Bold);
        title.enableAutoSizing = true;
        title.fontSizeMin = 26f;
        title.fontSizeMax = 40f;
        var sub = SpaceTheme.Text(panel, "Sub", any ? T_WILD_SUB_ANY : T_WILD_SUB_EMPTY, 26, SpaceTheme.TEXT_SUB, top,
            new Vector2(0f, -268f), new Vector2(Mathf.Min(_safeW - 140f, 880f), 40f), FontStyles.Bold);
        sub.enableAutoSizing = true;
        sub.fontSizeMin = 18f;
        sub.fontSizeMax = 26f;

        // 赤・青・黄（押したら、その色として置く流れへ進む）
        var colors = PlanetColorOrder.All;
        float total = colors.Length * BW + (colors.Length - 1) * GAP;
        for (int i = 0; i < colors.Length; i++)
        {
            var color = colors[i];
            var m = SpaceMeeples.Of(color);
            if (m == null) continue;
            var rt = SpaceTheme.Rect("As_" + m.Field, panel, top,
                new Vector2(-total * 0.5f + BW * 0.5f + i * (BW + GAP), -(320f + BH * 0.5f)), new Vector2(BW, BH));
            var glow = SpaceTheme.Rect("Glow", rt, SpaceTheme.CENTER, Vector2.zero, new Vector2(BW + 90f, BH + 90f));
            SpaceTheme.Img(glow, SpaceSprites.Glow, SpaceTheme.A(m.Tint, 0.25f));
            var faceRt = SpaceTheme.Rect("Face", rt);
            SpaceTheme.Stretch(faceRt);
            var face = SpaceTheme.Rounded(faceRt, new Color(0.07f, 0.09f, 0.24f, 0.88f), 36f, true);
            SpaceTheme.Clickable(rt.gameObject, face, () =>
            {
                CloseWildChooser();
                _wildColor = color;
                OnPlacePressed(); // 惑星の神殿なら力を戻す惑星を選ぶ → 確認 → 置く
            });
            var stroke = SpaceTheme.Rect("Stroke", rt);
            SpaceTheme.Stretch(stroke);
            SpaceTheme.RoundedStroke(stroke, SpaceTheme.A(m.Tint, 0.8f), 36f);
            var mIcon = SpaceTheme.Rect("Icon", rt, top, new Vector2(0f, -78f), new Vector2(104f, 104f));
            var mImg = SpaceTheme.Img(mIcon, m.Icon, Color.white);
            mImg.preserveAspect = true;
            mImg.enabled = m.Icon != null;
            var label = SpaceTheme.Text(rt, "Label", SpaceWorld.ColorName(color) + T_WILD_BUTTON, 32, SpaceTheme.TEXT,
                new Vector2(0.5f, 0f), new Vector2(0f, 46f), new Vector2(BW - 20f, 44f), FontStyles.Bold);
            label.enableAutoSizing = true;
            label.fontSizeMin = 22f;
            label.fontSizeMax = 32f;
        }

        // キャンセル
        var cancel = SpaceTheme.Rect("Cancel", panel, new Vector2(0.5f, 0f), new Vector2(0f, 48f), new Vector2(380f, 112f),
            new Vector2(0.5f, 0f));
        var cancelFace = SpaceTheme.Glass(cancel, 46f, new Color(0.05f, 0.07f, 0.19f, 0.85f), null, true);
        SpaceTheme.Clickable(cancel.gameObject, cancelFace, CloseWildChooser);
        var cancelLabel = SpaceTheme.Text(cancel, "Label", T_CANCEL, 36, SpaceTheme.TEXT_SUB, FontStyles.Bold);
        SpaceTheme.Stretch(cancelLabel.rectTransform);

        group.alpha = 0f;
        StartCoroutine(SpaceTheme.Tween(0.2f, 0f, k =>
        {
            if (group == null) return;
            group.alpha = k;
            panel.localScale = Vector3.one * Mathf.Lerp(0.95f, 1f, k);
        }));
    }

    private void CloseWildChooser()
    {
        if (_wildChooser != null) Destroy(_wildChooser);
        _wildChooser = null;
    }

    private async UniTaskVoid PlaceAsync(SpaceWorldArea area, PlanetColor color, List<string> revive, bool wild)
    {
        if (_busy) return;
        _busy = true;
        var busy = SpaceDialog.Busy(_canvas, T_PLACING);
        SpaceWorld.PlaceResult r;
        try
        {
            r = await SpaceWorld.PlaceAsync(area, color, revive, wild);
        }
        finally
        {
            if (busy != null) Destroy(busy);
            _busy = false;
        }
        if (this == null) return;
        if (r == null || !r.Ok)
        {
            SpaceTheme.Toast(r?.Error ?? SpaceWorld.T_ERR_NETWORK, _canvas);
            Refresh();
            return;
        }
        CloseChooser();
        CloseSheet();
        Refresh();
        AssetsDatabase.instance?.PlayLevelUpSE();
        ShowEffect(r);
    }

    /// <summary>置き場の効果のお知らせを順に出す</summary>
    private static void ShowEffect(SpaceWorld.PlaceResult r)
    {
        var steps = new List<Func<GameObject>>();
        switch (r.Area.Effect)
        {
            case WorldEffect.Star:
                AddStarSteps(r, steps);
                break;
            case WorldEffect.StarFragment:
                // 星のカケラ（5つの枠が星の形に並び、集まった数だけ埋まる）。5個そろったら、閉じたあと流れ星（2026-11-11）
                steps.Add(() => SpaceNotice.StarFragmentGot(r.Area.Name, r.StarFragments, r.StarCompleted));
                if (r.StarCompleted) AddStarSteps(r, steps);
                break;
            case WorldEffect.Planets:
            {
                var defs = r.Revived.Select(SpacePlanets.Find).Where(x => x != null).ToList();
                steps.Add(() => SpaceNotice.PlanetsRevived(defs));
                break;
            }
            case WorldEffect.Meeples:
                steps.Add(() => SpaceNotice.MeeplesGot(r.Area.Name));
                break;
            case WorldEffect.Coupon:
                // 5%OFFチケットのカケラ（2枚そろうと5%OFFチケット。2026-11-10）
                steps.Add(() => SpaceNotice.FragmentGot(r.Area.Name, r.Fragments, r.CouponCompleted));
                break;
            case WorldEffect.Gold:
                // 黄金の鉱山: 金貨が上から収まり「3G をもらいました」（2026-11-12）
                steps.Add(() => SpaceNotice.GoldGot(r.Gold, r.GoldBalance, T_CAPTION, r.Area.Name));
                break;
        }
        SpaceNoticeQueue.Run(steps, null);
    }

    /// <summary>流れ星（入店のときと同じ演出 → 当たりのカード）と、ノワールが当たったときの新しい惑星のお知らせ</summary>
    private static void AddStarSteps(SpaceWorld.PlaceResult r, List<Func<GameObject>> steps)
    {
        foreach (var p in r.Star.Prizes)
        {
            var prize = p;
            steps.Add(() => SpaceGachaMeteor.Play(prize));
        }
        foreach (var id in r.Star.Unlocked)
        {
            var def = SpacePlanets.Find(id);
            if (def != null) steps.Add(() => SpaceNotice.PlanetUnlocked(def));
        }
    }

    // ================================================================
    // 惑星の神殿: 力を戻す惑星を3つ選ぶ
    // ================================================================
    private void OpenChooser(List<SpacePlanetDef> candidates)
    {
        if (_chooser != null) return;
        _chosen.Clear();
        _chooseCells.Clear();

        var root = SpaceTheme.Rect("Chooser", transform);
        SpaceTheme.Stretch(root);
        root.SetAsLastSibling();
        _chooser = root.gameObject;
        var dim = SpaceTheme.Rect("Dim", root);
        SpaceTheme.Stretch(dim);
        SpaceTheme.Img(dim, null, new Color(0.01f, 0.015f, 0.05f, 0.96f), true);
        var safe = SpaceTheme.Rect("Safe", root);
        SpaceTheme.Stretch(safe);
        safe.gameObject.AddComponent<SpaceSafeArea>();

        var top = new Vector2(0.5f, 1f);
        var caption = SpaceTheme.Text(safe, "Caption", _sheetArea != null ? _sheetArea.En : "", 24,
            SpaceTheme.A(SpaceTheme.CYAN, 0.85f), top, new Vector2(0f, -70f), new Vector2(700f, 36f), FontStyles.Bold);
        SpaceTheme.Tracking(caption, 18f);
        var title = SpaceTheme.Text(safe, "Title", T_CHOOSE_TITLE, 40, SpaceTheme.TEXT, top, new Vector2(0f, -124f),
            new Vector2(_safeW - 260f, 56f), FontStyles.Bold);
        title.enableAutoSizing = true;
        title.fontSizeMin = 26f;
        title.fontSizeMax = 40f;
        BuildCloseButton(safe, CloseChooser);
        _chooseCounter = SpaceTheme.Text(safe, "Counter", "", 34, SpaceTheme.GOLD, top, new Vector2(0f, -186f),
            new Vector2(400f, 48f), FontStyles.Bold);

        // 一覧（3列。多ければスクロール）
        const int COLS = 3;
        const float CELL_W = 300f, CELL_H = 290f, GAP = 18f, FRAME = 200f;
        float gridW = COLS * CELL_W + (COLS - 1) * GAP;
        int rows = Mathf.CeilToInt(candidates.Count / (float)COLS);
        float contentH = rows * CELL_H + Mathf.Max(0, rows - 1) * GAP + 24f;
        float listTop = 240f, listBottom = 220f;
        var list = SpaceTheme.Rect("List", safe);
        SpaceTheme.Stretch(list, (_safeW - gridW) * 0.5f, listBottom, (_safeW - gridW) * 0.5f, listTop);
        var viewport = SpaceTheme.Rect("Viewport", list);
        SpaceTheme.Stretch(viewport);
        SpaceTheme.HitArea(viewport);
        viewport.gameObject.AddComponent<RectMask2D>();
        var content = SpaceTheme.Rect("Content", viewport);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.sizeDelta = new Vector2(0f, contentH);
        var scroll = list.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Elastic;
        scroll.scrollSensitivity = 30f;

        for (int i = 0; i < candidates.Count; i++)
        {
            var def = candidates[i];
            int col = i % COLS, row = i / COLS;
            var cell = SpaceTheme.Rect("Cell_" + def.Id, content, new Vector2(0f, 1f),
                new Vector2(CELL_W * 0.5f + col * (CELL_W + GAP), -(12f + CELL_H * 0.5f + row * (CELL_H + GAP))),
                new Vector2(CELL_W, CELL_H));
            var faceRt = SpaceTheme.Rect("Face", cell);
            SpaceTheme.Stretch(faceRt);
            var face = SpaceTheme.Rounded(faceRt, new Color(0.07f, 0.09f, 0.24f, 0.8f), 34f, true);
            string id = def.Id;
            SpaceTheme.Clickable(cell.gameObject, face, () => ToggleChosen(id));
            var stroke = SpaceTheme.Rect("Stroke", cell);
            SpaceTheme.Stretch(stroke);
            var strokeImg = SpaceTheme.RoundedStroke(stroke, SpaceTheme.STROKE, 34f);

            // 淡い色（今）と、選ぶと出るいつもの色（力が戻った姿）を重ねる
            var holder = SpaceTheme.Rect("Planet", cell, new Vector2(0.5f, 1f), new Vector2(0f, -FRAME * 0.5f - 8f),
                new Vector2(FRAME, FRAME));
            SpacePlanetView.Create(holder, def, FRAME, SpacePlanetView.Quality.Thumb, false, false, true);
            var vividRt = SpaceTheme.Rect("Vivid", holder);
            SpaceTheme.Stretch(vividRt);
            var vivid = vividRt.gameObject.AddComponent<CanvasGroup>();
            vivid.alpha = 0f;
            SpacePlanetView.Create(vividRt, def, FRAME, SpacePlanetView.Quality.Thumb, false);
            var name = SpaceTheme.Text(cell, "Name", def.Name, 30, SpaceTheme.TEXT, new Vector2(0.5f, 0f),
                new Vector2(0f, 36f), new Vector2(CELL_W - 20f, 44f), FontStyles.Bold);
            name.enableAutoSizing = true;
            name.fontSizeMin = 20f;
            name.fontSizeMax = 30f;
            _chooseCells[id] = (strokeImg, vivid);
        }

        var ok = SpaceTheme.Rect("Ok", safe, new Vector2(0.5f, 0f), new Vector2(0f, 64f), new Vector2(gridW, 128f),
            new Vector2(0.5f, 0f));
        var okFace = SpaceTheme.PrimaryFace(ok);
        SpaceTheme.Clickable(ok.gameObject, okFace, OnChooseOk);
        var okLabel = SpaceTheme.Text(ok, "Label", T_CHOOSE_OK, 38, SpaceTheme.TEXT_DARK, FontStyles.Bold);
        SpaceTheme.Stretch(okLabel.rectTransform);
        _chooseOkGroup = ok.gameObject.AddComponent<CanvasGroup>();
        RefreshChooser();
    }

    private void ToggleChosen(string id)
    {
        if (_busy) return;
        if (_chosen.Contains(id)) _chosen.Remove(id);
        else if (_chosen.Count < SpaceWorld.REVIVE_COUNT) _chosen.Add(id);
        RefreshChooser();
    }

    private void RefreshChooser()
    {
        if (_chooser == null) return;
        _chooseCounter.text = _chosen.Count + " / " + SpaceWorld.REVIVE_COUNT;
        foreach (var kv in _chooseCells)
        {
            bool on = _chosen.Contains(kv.Key);
            kv.Value.stroke.color = on ? SpaceTheme.A(SpaceTheme.GOLD, 0.95f) : SpaceTheme.STROKE;
            kv.Value.vivid.alpha = on ? 1f : 0f;
        }
        _chooseOkGroup.alpha = _chosen.Count == SpaceWorld.REVIVE_COUNT ? 1f : 0.4f;
    }

    private void OnChooseOk()
    {
        if (_busy) return;
        if (_chosen.Count != SpaceWorld.REVIVE_COUNT)
        {
            SpaceTheme.Toast(T_CHOOSE_TITLE, _canvas);
            return;
        }
        ConfirmPlace(new List<string>(_chosen));
    }

    private void CloseChooser()
    {
        if (_chooser == null) return;
        Destroy(_chooser);
        _chooser = null;
        _chosen.Clear();
        _chooseCells.Clear();
    }
}
