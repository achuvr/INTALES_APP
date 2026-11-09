using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 料金表（新ホームの中央の惑星の右上にある、水色の小さな惑星から開く全画面）。
/// 平日料金・土日・祝日料金の2枚の画像を、上の切り替えで見せる。開いたときは、土曜・日曜なら土日・祝日料金、それ以外は平日料金
/// （祝日は端末では判定できないので、平日に当たる祝日は切り替えて見てもらう）。
/// 画像はピンチ・ダブルタップで拡大、拡大中はドラッグで移動できる（お知らせの画像と同じ SpaceZoomImage）。
/// 画像は Resources/Prices/price_weekday・price_holiday（中身は JPG の .bytes。Unity の画像の圧縮で文字がにじまないように）。
/// 作り方は tools/price-images/make_price_images.py（料金が変わったらそこを直して作り直し、アプリをビルドし直す）。
/// </summary>
public class SpacePricePage : MonoBehaviour
{
    /// <summary>この画面の色（ホームの水色の小さな惑星と同じ）</summary>
    public static readonly Color ACCENT = SpaceTheme.CYAN;

    private const string T_CAPTION = "PRICE";
    private const string T_TITLE = "料金表";
    private const string T_WEEKDAY = "平日料金";
    private const string T_HOLIDAY = "土日・祝日料金";
    private const string T_ZOOM_HINT = "ピンチやダブルタップで拡大できます";
    private const string T_FAILED = "料金表を読み込めませんでした";
    private static readonly string[] RESOURCES = { "Prices/price_weekday", "Prices/price_holiday" };
    private static readonly string[] LABELS = { T_WEEKDAY, T_HOLIDAY };
    private static readonly Color[] TAB_COLORS = { SpaceTheme.CYAN, SpaceTheme.PINK }; // 画像の主な色と同じ

    /// <summary>この画面で表示しうる文字</summary>
    public static string GlyphText => T_CAPTION + T_TITLE + T_WEEKDAY + T_HOLIDAY + T_ZOOM_HINT + T_FAILED;

    private const float TAB_Y = 222f, TAB_W = 340f, TAB_H = 88f, TAB_GAP = 20f;
    private const float AREA_TOP = 290f, AREA_BOTTOM = 112f, AREA_SIDE = 20f;
    private static readonly Color PILL_FILL = new Color(0.07f, 0.09f, 0.24f, 0.85f);

    private sealed class Tab
    {
        public Image Face, Edge;
        public TextMeshProUGUI Label;
    }

    private Canvas _canvas;
    private Action _onClosed;
    private CanvasGroup _group;
    private RectTransform _safe, _area, _pic;
    private Vector2 _areaSize;
    private TextMeshProUGUI _failed;
    private readonly Texture2D[] _tex = new Texture2D[2];
    private readonly Tab[] _tabs = new Tab[2];
    private int _shown = -1;
    private bool _closing;

    public static SpacePricePage Open(Canvas canvas, Action onClosed)
    {
        var root = SpaceTheme.Rect("__SpacePrice", canvas.transform);
        SpaceTheme.Stretch(root);
        root.SetAsLastSibling();
        SpaceTheme.SubCanvas(root.gameObject, true);
        var page = root.gameObject.AddComponent<SpacePricePage>();
        page._canvas = canvas;
        page._onClosed = onClosed;
        page.Build();
        return page;
    }

    public void HandleBack() => Close();

    public void Close()
    {
        if (_closing) return;
        _closing = true;
        StartCoroutine(FadeOutAndDestroy());
    }

    private IEnumerator FadeOutAndDestroy()
    {
        float from = _group.alpha;
        yield return SpaceTheme.Tween(0.2f, 0f, k => _group.alpha = from * (1f - k));
        Destroy(gameObject);
    }

    private void OnDestroy()
    {
        // 大きな画像なので、閉じたらすぐ手放す（開くたびに読み直す）
        for (int i = 0; i < _tex.Length; i++)
        {
            if (_tex[i] != null) Destroy(_tex[i]);
            _tex[i] = null;
        }
        _onClosed?.Invoke();
    }

    // ================================================================
    // 画面
    // ================================================================
    private void Build()
    {
        var rt = (RectTransform)transform;
        _group = gameObject.AddComponent<CanvasGroup>();
        _group.alpha = 0f;

        // 指が画像の外に出ても閉じないよう、背景のタップでは閉じない（閉じるのはボタン・戻る操作）
        var dim = SpaceTheme.Rect("Dim", rt);
        SpaceTheme.Stretch(dim);
        SpaceTheme.Img(dim, null, new Color(0.01f, 0.015f, 0.05f, 0.97f), true);
        var glow = SpaceTheme.Rect("Glow", rt, new Vector2(0.5f, 1f), new Vector2(0f, -140f), new Vector2(1500f, 800f));
        SpaceTheme.Img(glow, SpaceSprites.Glow, SpaceTheme.A(ACCENT, 0.10f));

        _safe = SpaceTheme.Rect("Safe", rt);
        SpaceTheme.Stretch(_safe);
        _safe.gameObject.AddComponent<SpaceSafeArea>();

        var caption = SpaceTheme.Text(_safe, "Caption", T_CAPTION, 24, SpaceTheme.A(ACCENT, 0.9f),
            new Vector2(0.5f, 1f), new Vector2(0f, -70f), new Vector2(700f, 36f), FontStyles.Bold);
        SpaceTheme.Tracking(caption, 18f);
        SpaceTheme.Text(_safe, "Title", T_TITLE, 44, SpaceTheme.TEXT, new Vector2(0.5f, 1f), new Vector2(0f, -124f),
            new Vector2(800f, 60f), FontStyles.Bold);
        CloseButton(_safe);

        // 平日料金・土日・祝日料金の切り替え
        for (int i = 0; i < 2; i++)
        {
            int index = i;
            float x = (i == 0 ? -1f : 1f) * (TAB_W + TAB_GAP) * 0.5f;
            var tab = SpaceTheme.Rect("Tab" + i, _safe, new Vector2(0.5f, 1f), new Vector2(x, -TAB_Y),
                new Vector2(TAB_W, TAB_H));
            var face = SpaceTheme.Rounded(tab, PILL_FILL, TAB_H / 2f, true);
            var edgeRt = SpaceTheme.Rect("Edge", tab);
            SpaceTheme.Stretch(edgeRt);
            var edge = SpaceTheme.RoundedStroke(edgeRt, SpaceTheme.STROKE, TAB_H / 2f);
            var label = SpaceTheme.Text(tab, "Label", LABELS[i], 34, SpaceTheme.TEXT_SUB, FontStyles.Bold);
            SpaceTheme.Stretch(label.rectTransform);
            SpaceTheme.Clickable(tab.gameObject, face, () => Show(index));
            _tabs[i] = new Tab { Face = face, Edge = edge, Label = label };
        }

        // 画像の枠（拡大はこの中で行う）。画面の大きさから先に決める（組み立て直後はまだ大きさが決まっていないため）
        var size = SpaceTheme.CanvasSize(_canvas);
        var insets = SpaceTheme.SafeInsets(size);
        _areaSize = new Vector2(size.x - AREA_SIDE * 2f,
            Mathf.Max(400f, size.y - insets.top - insets.bottom - AREA_TOP - AREA_BOTTOM));
        _area = SpaceTheme.Rect("Area", _safe, new Vector2(0.5f, 1f), new Vector2(0f, -AREA_TOP), _areaSize,
            new Vector2(0.5f, 1f));
        _area.gameObject.AddComponent<RectMask2D>();
        _failed = SpaceTheme.Text(_area, "Failed", T_FAILED, 30, SpaceTheme.TEXT_SUB, FontStyles.Bold);
        SpaceTheme.Stretch(_failed.rectTransform);
        _failed.gameObject.SetActive(false);

        SpaceTheme.Text(_safe, "Hint", T_ZOOM_HINT, 24, SpaceTheme.TEXT_MUTED, new Vector2(0.5f, 0f),
            new Vector2(0f, 60f), new Vector2(900f, 36f), FontStyles.Bold);

        Show(DefaultTab());
        StartCoroutine(SpaceTheme.Tween(0.25f, 0f, k => _group.alpha = k));
    }

    /// <summary>開いたときの料金表: 土曜・日曜は土日・祝日料金、それ以外は平日料金</summary>
    private static int DefaultTab()
    {
        var day = DateTime.Now.DayOfWeek;
        return day == DayOfWeek.Saturday || day == DayOfWeek.Sunday ? 1 : 0;
    }

    private void Show(int index)
    {
        if (index == _shown) return;
        _shown = index;
        for (int i = 0; i < _tabs.Length; i++)
        {
            bool on = i == index;
            var c = TAB_COLORS[i];
            _tabs[i].Face.color = on ? SpaceTheme.A(c, 0.18f) : PILL_FILL;
            _tabs[i].Edge.color = on ? SpaceTheme.A(c, 0.8f) : SpaceTheme.STROKE;
            _tabs[i].Label.color = on ? c : SpaceTheme.TEXT_SUB;
        }

        if (_pic != null) Destroy(_pic.gameObject);
        _pic = null;
        var tex = Load(index);
        _failed.gameObject.SetActive(tex == null);
        if (tex == null) return;

        // 枠に収まる大きさで中央に（縦横比はそのまま）。切り替えるたびに拡大は元に戻る
        float s = Mathf.Min(_areaSize.x / tex.width, _areaSize.y / tex.height);
        _pic = SpaceTheme.Rect("Picture", _area, SpaceTheme.CENTER, Vector2.zero, new Vector2(tex.width * s, tex.height * s));
        var raw = _pic.gameObject.AddComponent<RawImage>();
        raw.texture = tex;
        raw.color = new Color(1f, 1f, 1f, 0f);
        _pic.gameObject.AddComponent<SpaceZoomImage>().Setup(_area);
        StartCoroutine(SpaceTheme.Tween(0.2f, 0f, k =>
        {
            if (raw != null) raw.color = new Color(1f, 1f, 1f, k);
        }));
    }

    /// <summary>料金表の画像（開いている間は覚えておく）。無い・読めないときは null</summary>
    private Texture2D Load(int index)
    {
        if (_tex[index] != null) return _tex[index];
        var asset = Resources.Load<TextAsset>(RESOURCES[index]);
        if (asset == null)
        {
            Debug.LogWarning($"[Price] 料金表の画像がありません: Resources/{RESOURCES[index]}");
            return null;
        }
        var tex = new Texture2D(2, 2, TextureFormat.RGB24, false);
        bool ok = tex.LoadImage(asset.bytes, true);
        Resources.UnloadAsset(asset);
        if (!ok)
        {
            Destroy(tex);
            Debug.LogWarning($"[Price] 料金表の画像を読み込めませんでした: {RESOURCES[index]}");
            return null;
        }
        tex.wrapMode = TextureWrapMode.Clamp;
        _tex[index] = tex;
        return tex;
    }

    private void CloseButton(RectTransform safe)
    {
        var rt = SpaceTheme.Rect("Close", safe, new Vector2(1f, 1f), new Vector2(-112f, -84f), new Vector2(104f, 104f));
        var face = SpaceTheme.Img(rt, SpaceSprites.Circle, new Color(0.05f, 0.07f, 0.19f, 0.85f), true);
        var rim = SpaceTheme.Rect("Rim", rt);
        SpaceTheme.Stretch(rim);
        SpaceTheme.Img(rim, SpaceSprites.RingSmall, SpaceTheme.STROKE);
        SpaceTheme.Bar(rt, Vector2.zero, 44f, 6f, 45f, SpaceTheme.TEXT);
        SpaceTheme.Bar(rt, Vector2.zero, 44f, 6f, -45f, SpaceTheme.TEXT);
        SpaceTheme.Clickable(rt.gameObject, face, Close);
    }
}
