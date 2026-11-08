using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// QRカメラ画面（QR シーン）の宇宙デザイン。QR シーンのルートに付いている。
/// 新ホームから開いたとき（SpaceTheme.InUse）だけ、カメラ映像の上に重ねて表示する:
///   ・読み取り枠の外を暗くし、枠の四隅をシアンのブラケットで縁取る
///   ・枠の中を走査ラインが上下する
///   ・上部にタイトル、枠の下に案内文。カメラが使えないときは枠の中に案内を出す
///   ・閉じるボタン（シーンの Button_Close）は押したときの処理はそのままで、見た目だけガラスの丸ボタンに
/// 旧ホームから開いたときは何もしない（従来の見た目のまま）。
/// </summary>
public class SpaceQRScreen : MonoBehaviour
{
    [SerializeField] private Canvas _canvas;
    [SerializeField] private Image _background;
    [SerializeField] private RawImage _camera;
    [SerializeField] private Button _closeButton;

    private const float WINDOW = 640f;    // 読み取り枠の一辺
    private const float WINDOW_Y = 40f;   // 枠の中心（画面中央から上へ）
    private const float CORNER_R = 40f;   // 枠の角丸
    private const float NO_CAMERA_AFTER = 3f; // この秒数たっても映像が来なければ「カメラを使えません」

    private const string T_CAPTION = "QR SCAN";
    private const string T_TITLE = "QRコードを読み取る";
    private const string T_HINT = "枠の中にQRコードを合わせてください";
    private const string T_STARTING = "カメラを起動しています…";
    private const string T_NO_CAMERA = "カメラを使えません";
    private const string T_NO_CAMERA_SUB = "端末の設定でカメラへのアクセスを\n許可してください";

    /// <summary>この画面で表示する文字（NewHome の起動時にまとめてフォントへ登録する）</summary>
    public static string GlyphText => new StringBuilder()
        .Append(T_CAPTION).Append(T_TITLE).Append(T_HINT).Append(T_STARTING).Append(T_NO_CAMERA)
        .Append(T_NO_CAMERA_SUB).ToString();

    private bool _active, _cameraShown;
    private float _t;
    private RectTransform _scanLine, _scanGlow;
    private Image _scanLineImg;
    private readonly Image[] _bracketGlows = new Image[4];
    private GameObject _status;
    private TextMeshProUGUI _statusTitle, _statusSub;

    private void Awake()
    {
        if (!SpaceTheme.InUse || _canvas == null) return; // 旧ホームから開いたときは従来の見た目
        _active = true;
        Build();
    }

    // ================================================================
    // 組み立て
    // ================================================================
    private void Build()
    {
        var canvasRt = (RectTransform)_canvas.transform;
        var size = SpaceTheme.CanvasSize(_canvas);

        // 背景: 灰色 → 宇宙の黒。カメラ映像が来るまでは星空を見せる（背景は下のホームを押させない役目もそのまま）
        if (_background != null)
        {
            _background.color = new Color(0.012f, 0.016f, 0.055f, 1f);
            // 新ホームの背景と同じサイズなので、生成済みのテクスチャがそのまま使い回される
            int index = _background.transform.GetSiblingIndex() + 1;
            Backdrop("__SpaceNebula", canvasRt, index,
                SpaceSprites.Nebula(Mathf.Max(32, Mathf.RoundToInt(size.x / 6f)), Mathf.Max(32, Mathf.RoundToInt(size.y / 6f))));
            Backdrop("__SpaceStars", canvasRt, index + 1,
                SpaceSprites.Stars(Mathf.Max(16, Mathf.RoundToInt(size.x / 2f)), Mathf.Max(16, Mathf.RoundToInt(size.y / 2f))));
        }
        // 映像が届くまではカメラの RawImage を消しておく（テクスチャ未設定だと全面が白く塗られるため）
        if (_camera != null) _camera.color = new Color(1f, 1f, 1f, 0f);

        var overlay = SpaceTheme.Rect("__SpaceQR", canvasRt);
        SpaceTheme.Stretch(overlay);
        overlay.SetAsLastSibling();

        BuildMask(overlay);
        BuildBrackets(overlay);
        BuildScanLine(overlay);

        // カメラの状態（起動中／使えない）を枠の中に出す
        _status = SpaceTheme.Rect("Status", overlay, SpaceTheme.CENTER, new Vector2(0f, WINDOW_Y),
            new Vector2(WINDOW - 60f, 260f)).gameObject;
        _statusTitle = SpaceTheme.Text(_status.transform, "Title", T_STARTING, 36, SpaceTheme.TEXT,
            SpaceTheme.CENTER, new Vector2(0f, 32f), new Vector2(WINDOW - 60f, 56f), FontStyles.Bold);
        _statusSub = SpaceTheme.Text(_status.transform, "Sub", "", 28, SpaceTheme.TEXT_SUB,
            SpaceTheme.CENTER, new Vector2(0f, -40f), new Vector2(WINDOW - 60f, 90f), FontStyles.Bold);

        SpaceTheme.Text(overlay, "Hint", T_HINT, 30, SpaceTheme.TEXT_SUB, SpaceTheme.CENTER,
            new Vector2(0f, WINDOW_Y - WINDOW / 2f - 84f), new Vector2(900f, 44f), FontStyles.Bold);

        // 上部（ノッチを避ける）: タイトルと閉じるボタン
        var safe = SpaceTheme.Rect("Safe", overlay);
        SpaceTheme.Stretch(safe);
        safe.gameObject.AddComponent<SpaceSafeArea>();
        var cap = SpaceTheme.Text(safe, "Caption", T_CAPTION, 24, SpaceTheme.A(SpaceTheme.CYAN, 0.85f),
            new Vector2(0.5f, 1f), new Vector2(0f, -70f), new Vector2(600f, 36f), FontStyles.Bold);
        SpaceTheme.Tracking(cap, 18f);
        SpaceTheme.Text(safe, "Title", T_TITLE, 44, SpaceTheme.TEXT, new Vector2(0.5f, 1f), new Vector2(0f, -124f),
            new Vector2(760f, 64f), FontStyles.Bold);

        RestyleCloseButton(safe);
    }

    private static RawImage Backdrop(string name, RectTransform parent, int siblingIndex, Texture tex)
    {
        var rt = SpaceTheme.Rect(name, parent);
        SpaceTheme.Stretch(rt);
        rt.SetSiblingIndex(siblingIndex);
        var raw = rt.gameObject.AddComponent<RawImage>();
        raw.texture = tex;
        raw.raycastTarget = false;
        return raw;
    }

    /// <summary>読み取り枠の外を暗くする（上下左右の4枚＋角丸を抜いた四隅）</summary>
    private static void BuildMask(RectTransform overlay)
    {
        var dim = SpaceTheme.A(SpaceTheme.DIM, 0.62f);
        float h = WINDOW / 2f;
        float top = WINDOW_Y + h, bottom = WINDOW_Y - h;

        MaskPiece(overlay, "Top", dim, new Vector2(0f, 0.5f), new Vector2(1f, 1f), new Vector2(0f, top), Vector2.zero);
        MaskPiece(overlay, "Bottom", dim, new Vector2(0f, 0f), new Vector2(1f, 0.5f), Vector2.zero, new Vector2(0f, bottom));
        MaskPiece(overlay, "Left", dim, new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, bottom), new Vector2(-h, top));
        MaskPiece(overlay, "Right", dim, new Vector2(0.5f, 0.5f), new Vector2(1f, 0.5f),
            new Vector2(h, bottom), new Vector2(0f, top));

        float r = CORNER_R, c = h - r / 2f;
        Corner(overlay, dim, new Vector2(-c, WINDOW_Y + c), 0f);    // 左上
        Corner(overlay, dim, new Vector2(c, WINDOW_Y + c), -90f);   // 右上
        Corner(overlay, dim, new Vector2(c, WINDOW_Y - c), 180f);   // 右下
        Corner(overlay, dim, new Vector2(-c, WINDOW_Y - c), 90f);   // 左下
    }

    private static void MaskPiece(RectTransform parent, string name, Color color, Vector2 anchorMin, Vector2 anchorMax,
        Vector2 offsetMin, Vector2 offsetMax)
    {
        var rt = SpaceTheme.Rect("Mask" + name, parent);
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = SpaceTheme.CENTER;
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;
        SpaceTheme.Img(rt, null, color);
    }

    private static void Corner(RectTransform parent, Color color, Vector2 pos, float angle)
    {
        var rt = SpaceTheme.Rect("MaskCorner", parent, SpaceTheme.CENTER, pos, new Vector2(CORNER_R, CORNER_R));
        rt.localEulerAngles = new Vector3(0f, 0f, angle);
        SpaceTheme.Img(rt, SpaceSprites.InverseCorner, color);
    }

    /// <summary>枠の四隅のL字ブラケット（少し外側に置き、後ろに光をにじませる）</summary>
    private void BuildBrackets(RectTransform overlay)
    {
        const float len = 96f, thick = 8f, outset = 10f;
        float e = WINDOW / 2f + outset;
        var signs = new[] { new Vector2(-1f, 1f), new Vector2(1f, 1f), new Vector2(1f, -1f), new Vector2(-1f, -1f) };
        for (int i = 0; i < 4; i++)
        {
            var sgn = signs[i];
            var corner = new Vector2(sgn.x * e, WINDOW_Y + sgn.y * e);
            var glow = SpaceTheme.Rect("BracketGlow", overlay, SpaceTheme.CENTER, corner, new Vector2(220f, 220f));
            _bracketGlows[i] = SpaceTheme.Img(glow, SpaceSprites.Glow, SpaceTheme.A(SpaceTheme.CYAN, 0.25f));
            // 角の点から、横と縦に len だけ伸ばす（端は丸い）
            SpaceTheme.Bar(overlay, corner + new Vector2(-sgn.x * (len - thick) / 2f, 0f), len, thick, 0f, SpaceTheme.CYAN);
            SpaceTheme.Bar(overlay, corner + new Vector2(0f, -sgn.y * (len - thick) / 2f), len, thick, 90f, SpaceTheme.CYAN);
        }
    }

    private void BuildScanLine(RectTransform overlay)
    {
        _scanGlow = SpaceTheme.Rect("ScanGlow", overlay, SpaceTheme.CENTER, new Vector2(0f, WINDOW_Y),
            new Vector2(WINDOW, 90f));
        SpaceTheme.Img(_scanGlow, SpaceSprites.Glow, SpaceTheme.A(SpaceTheme.CYAN, 0.30f));
        _scanLine = SpaceTheme.Rect("ScanLine", overlay, SpaceTheme.CENTER, new Vector2(0f, WINDOW_Y),
            new Vector2(WINDOW - 40f, 4f));
        _scanLineImg = SpaceTheme.Img(_scanLine, SpaceSprites.FadeLine, new Color(0.63f, 0.94f, 1f, 0.95f));
    }

    /// <summary>シーンの閉じるボタンを、押したときの処理はそのままでガラスの丸ボタンにする</summary>
    private void RestyleCloseButton(RectTransform safe)
    {
        if (_closeButton == null) return;
        var rt = (RectTransform)_closeButton.transform;
        rt.SetParent(safe, false);
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = SpaceTheme.CENTER;
        rt.anchoredPosition = new Vector2(-112f, -84f); // 新ホームの歯車と同じ位置
        rt.sizeDelta = new Vector2(104f, 104f);
        rt.localScale = Vector3.one;
        for (int i = rt.childCount - 1; i >= 0; i--) rt.GetChild(i).gameObject.SetActive(false); // 旧デザインの子は隠す

        var img = _closeButton.targetGraphic as Image;
        if (img == null) img = _closeButton.GetComponent<Image>();
        if (img != null)
        {
            img.sprite = SpaceSprites.Circle;
            img.type = Image.Type.Simple;
            img.preserveAspect = false;
            img.color = new Color(0.05f, 0.07f, 0.19f, 0.85f);
        }
        _closeButton.transition = Selectable.Transition.None;

        var rim = SpaceTheme.Rect("__Rim", rt);
        SpaceTheme.Stretch(rim);
        SpaceTheme.Img(rim, SpaceSprites.RingSmall, SpaceTheme.STROKE);
        SpaceTheme.Bar(rt, Vector2.zero, 44f, 6f, 45f, SpaceTheme.TEXT);
        SpaceTheme.Bar(rt, Vector2.zero, 44f, 6f, -45f, SpaceTheme.TEXT);
        UITheme.AddPressEffect(rt.gameObject);
    }

    // ================================================================
    // 毎フレーム
    // ================================================================
    private void Update()
    {
        if (!_active) return;
        _t += Time.unscaledDeltaTime;

        // 走査ライン: 枠の中を上下に往復
        float y = WINDOW_Y + (WINDOW / 2f - 28f) * Mathf.Sin(_t * 1.7f);
        _scanLine.anchoredPosition = new Vector2(0f, y);
        _scanGlow.anchoredPosition = new Vector2(0f, y);

        float pulse = 0.18f + 0.10f * (0.5f + 0.5f * Mathf.Sin(_t * 2.2f));
        for (int i = 0; i < _bracketGlows.Length; i++)
            if (_bracketGlows[i] != null) _bracketGlows[i].color = SpaceTheme.A(SpaceTheme.CYAN, pulse);

        UpdateCameraState();
    }

    /// <summary>映像が届いたらカメラを見せる。一定時間届かなければ「カメラを使えません」</summary>
    private void UpdateCameraState()
    {
        var cam = _camera != null ? _camera.texture as WebCamTexture : null;
        bool playing = cam != null && cam.isPlaying && cam.width > 16;
        if (playing != _cameraShown)
        {
            _cameraShown = playing;
            if (_camera != null) _camera.color = new Color(1f, 1f, 1f, playing ? 1f : 0f);
            _status.SetActive(!playing);
        }
        if (!playing && _t > NO_CAMERA_AFTER && _statusSub.text.Length == 0)
        {
            _statusTitle.text = T_NO_CAMERA;
            _statusSub.text = T_NO_CAMERA_SUB;
        }
    }
}
