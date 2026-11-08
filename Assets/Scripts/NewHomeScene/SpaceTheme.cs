using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// 宇宙デザイン（2026-11 リニューアル）のデザイントークンとUI部品ヘルパー。
/// NewHome から適用を始め、今後ほかの画面もこの配色・部品に寄せていく。
/// 画像アセットは使わず、角丸・光・アイコンなどのスプライトは SpaceSprites が実行時に生成する。
///
/// 使い方の基本:
///   var rt = SpaceTheme.Rect("Card", parent, ...);   … 矩形を作る
///   SpaceTheme.Glass(rt, 44f);                        … ガラス風パネル（面＋つや＋縁）
///   SpaceTheme.Text(rt, "Title", "所持品", 46, SpaceTheme.TEXT, FontStyles.Bold);
/// 日本語を新しく表示するときは、先に Prewarm でフォントへ文字を登録しておくこと（OOM対策）。
/// </summary>
public static class SpaceTheme
{
    // ================================================================
    // 配色トークン
    // ================================================================
    public static readonly Color TEXT       = Hex(0xEEF2FF);   // 本文・見出し
    public static readonly Color TEXT_SUB   = Hex(0xA9B4DE);   // 補足
    public static readonly Color TEXT_MUTED = Hex(0x6E79A8);   // ラベル・無効
    public static readonly Color TEXT_DARK  = Hex(0x080C24);   // 明るいボタン上の文字
    public static readonly Color CYAN       = Hex(0x5CE1FF);
    public static readonly Color VIOLET     = Hex(0x9B7BFF);
    public static readonly Color PINK       = Hex(0xFF7AD9);
    public static readonly Color MINT       = Hex(0x5CFFB8);
    public static readonly Color GOLD       = Hex(0xFFD37A);
    public static readonly Color DANGER     = Hex(0xFF6B8B);   // 退会など取り消せない操作
    public static readonly Color GLASS      = new Color(0.09f, 0.12f, 0.30f, 0.62f); // カード（背景が少し透ける）
    public static readonly Color GLASS_DEEP = new Color(0.05f, 0.07f, 0.19f, 0.94f); // シート・モーダル
    public static readonly Color STROKE     = new Color(0.62f, 0.72f, 1.00f, 0.30f); // 縁取り
    public static readonly Color LINE       = new Color(0.62f, 0.72f, 1.00f, 1.00f); // 区切り線・軌道（アルファは使う側で指定）
    public static readonly Color DIM        = new Color(0.01f, 0.02f, 0.06f, 0.80f); // 暗幕
    public static readonly Color ICON_BASE  = new Color(0.04f, 0.05f, 0.14f, 1.00f); // アイコン枠の地

    /// <summary>リッチテキスト用の補足色（TEXT_SUB）</summary>
    public const string SUB_HEX = "#A9B4DE";

    public static readonly Vector2 CENTER = new Vector2(0.5f, 0.5f);

    public static Color Hex(int rgb, float a = 1f) =>
        new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, a);

    public static Color A(Color c, float a)
    {
        c.a = a;
        return c;
    }

    // ================================================================
    // フォント
    // ================================================================
    private static TMP_FontAsset _font;

    /// <summary>日本語フォント（jp）。シーン側で SetFont 済みならそれを使う</summary>
    public static TMP_FontAsset Font
    {
        get
        {
            if (_font == null)
                _font = Resources.FindObjectsOfTypeAll<TMP_FontAsset>()
                    .FirstOrDefault(f => f.name.ToLower() == "jp");
            return _font;
        }
    }

    public static void SetFont(TMP_FontAsset font)
    {
        if (font != null) _font = font;
    }

    // ================================================================
    // 矩形
    // ================================================================
    public static RectTransform Rect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = 5; // UI
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        return rt;
    }

    /// <summary>親の1点（anchor）を基準に置く矩形</summary>
    public static RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size,
        Vector2? pivot = null)
    {
        var rt = Rect(name, parent);
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = pivot ?? CENTER;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        return rt;
    }

    /// <summary>親いっぱいに広げる（内側の余白を指定可）</summary>
    public static void Stretch(RectTransform rt, float left = 0f, float bottom = 0f, float right = 0f, float top = 0f)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = CENTER;
        rt.offsetMin = new Vector2(left, bottom);
        rt.offsetMax = new Vector2(-right, -top);
    }

    /// <summary>親の上端から top の位置に、左右を余白付きで横いっぱいに置く</summary>
    public static void TopBand(RectTransform rt, float top, float height, float left, float right)
    {
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.offsetMin = new Vector2(left, -top - height);
        rt.offsetMax = new Vector2(-right, -top);
    }

    /// <summary>
    /// キャンバスの実寸（CanvasScaler 適用後）。起動直後でレイアウト前でも正しい値になるよう、
    /// CanvasScaler の設定（ScaleWithScreenSize）とスクリーン解像度から直接計算する。
    /// </summary>
    public static Vector2 CanvasSize(Canvas canvas)
    {
        var root = canvas.rootCanvas != null ? canvas.rootCanvas : canvas;
        var scaler = root.GetComponent<CanvasScaler>();
        float sw = Screen.width, sh = Screen.height;
        if (scaler != null && scaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize && sw > 0f && sh > 0f)
        {
            var r = scaler.referenceResolution;
            float sf;
            switch (scaler.screenMatchMode)
            {
                case CanvasScaler.ScreenMatchMode.Expand:
                    sf = Mathf.Min(sw / r.x, sh / r.y);
                    break;
                case CanvasScaler.ScreenMatchMode.Shrink:
                    sf = Mathf.Max(sw / r.x, sh / r.y);
                    break;
                default:
                    sf = Mathf.Pow(2f, Mathf.Lerp(Mathf.Log(sw / r.x, 2f), Mathf.Log(sh / r.y, 2f),
                        scaler.matchWidthOrHeight));
                    break;
            }
            if (sf > 0f) return new Vector2(sw / sf, sh / sf);
        }
        var s = ((RectTransform)root.transform).rect.size;
        return s.x > 10f && s.y > 10f ? s : new Vector2(1080f, 1920f);
    }

    /// <summary>
    /// 子のサブキャンバスを作る（毎フレーム動く部分を分けて、メインキャンバスの再構築を避ける）。
    /// TMP の文字を正しく描くため、追加シェーダーチャンネルはルートキャンバスと揃える。
    /// </summary>
    public static Canvas SubCanvas(GameObject go, bool raycast)
    {
        var c = go.AddComponent<Canvas>();
        var parent = go.transform.parent != null ? go.transform.parent.GetComponentInParent<Canvas>() : null;
        if (parent != null) c.additionalShaderChannels = parent.rootCanvas.additionalShaderChannels;
        if (raycast) go.AddComponent<GraphicRaycaster>(); // サブキャンバス内のボタンを押せるようにする
        return c;
    }

    /// <summary>セーフエリア外の上下の余白（キャンバス単位）</summary>
    public static (float top, float bottom) SafeInsets(Vector2 canvasSize)
    {
        if (Screen.height <= 0) return (0f, 0f);
        var sa = Screen.safeArea;
        float k = canvasSize.y / Screen.height;
        return ((Screen.height - sa.yMax) * k, sa.yMin * k);
    }

    public static Canvas MainCanvas()
    {
        var go = GameObject.Find("Canvas");
        var c = go != null ? go.GetComponent<Canvas>() : null;
        return c != null ? c : Object.FindFirstObjectByType<Canvas>();
    }

    // ================================================================
    // 画像
    // ================================================================
    public static Image Img(RectTransform rt, Sprite sprite, Color color, bool raycast = false)
    {
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = sprite;
        img.color = color;
        img.raycastTarget = raycast;
        if (raycast && color.a <= 0f)
        {
            // 透明な当たり判定は描画カリングされると raycast も無効になるため、カリングを切る
            img.canvasRenderer.cullTransparentMesh = false;
        }
        return img;
    }

    /// <summary>透明な当たり判定（ボタンの押せる範囲を広げる用）</summary>
    public static Image HitArea(RectTransform rt) => Img(rt, null, new Color(0f, 0f, 0f, 0f), true);

    /// <summary>角丸の面（9スライス）。radius はキャンバス単位の見た目の半径（高さの半分未満にする）</summary>
    public static Image Rounded(RectTransform rt, Color color, float radius, bool raycast = false)
    {
        var img = Img(rt, SpaceSprites.RoundedFill, color, raycast);
        img.type = Image.Type.Sliced;
        img.pixelsPerUnitMultiplier = SpaceSprites.ROUNDED_RADIUS / Mathf.Max(1f, radius);
        return img;
    }

    /// <summary>角丸の縁取り線（9スライス）</summary>
    public static Image RoundedStroke(RectTransform rt, Color color, float radius)
    {
        var img = Img(rt, SpaceSprites.RoundedStroke, color);
        img.type = Image.Type.Sliced;
        img.pixelsPerUnitMultiplier = SpaceSprites.ROUNDED_RADIUS / Mathf.Max(1f, radius);
        return img;
    }

    /// <summary>
    /// ガラス風パネル: 面（rt 自身の Image）＋上部のつや＋縁取り。
    /// 戻り値は面の Image（raycast=true にするとボタンの当たり判定に使える）。
    /// つや・縁取りは先に子として作るので、このあと足した中身はその上に描かれる。
    /// </summary>
    public static Image Glass(RectTransform rt, float radius, Color? fill = null, Color? stroke = null,
        bool raycast = false)
    {
        var face = Rounded(rt, fill ?? GLASS, radius, raycast);

        var sheen = Rect("__Sheen", rt);
        Stretch(sheen);
        Rounded(sheen, new Color(1f, 1f, 1f, 0.075f), radius);
        sheen.gameObject.AddComponent<SpaceAlphaGradient>().Set(1f, 0f, 0.45f); // 上端→高さ45%で消える
        sheen.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;

        var edge = Rect("__Stroke", rt);
        Stretch(edge);
        RoundedStroke(edge, stroke ?? STROKE, radius);
        edge.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        return face;
    }

    /// <summary>縦グラデーション（Image の色に乗算）。既存の UIVerticalGradient を使う</summary>
    public static void Gradient(Graphic g, Color top, Color bottom) => UITheme.AddGradient(g.gameObject, top, bottom);

    /// <summary>主ボタンの面（シアン→すみれ色のグラデーション＋背後の光）。上に載せる文字は TEXT_DARK</summary>
    public static Image PrimaryFace(RectTransform rt, float radius = 46f) =>
        ButtonFace(rt, radius, CYAN, VIOLET, VIOLET);

    /// <summary>退会など取り消せない操作の決定ボタンの面（警告色）。上に載せる文字は白</summary>
    public static Image DangerFace(RectTransform rt, float radius = 46f) =>
        ButtonFace(rt, radius, Hex(0xF0628A), Hex(0xC9365C), DANGER);

    /// <summary>好きな2色（上→下）のボタンの面＋背後の光。退店のお知らせの黄色いカードの上のボタンなど</summary>
    public static Image GradientFace(RectTransform rt, Color top, Color bottom, Color glowColor, float radius = 46f) =>
        ButtonFace(rt, radius, top, bottom, glowColor);

    private static Image ButtonFace(RectTransform rt, float radius, Color top, Color bottom, Color glowColor)
    {
        // 光はボタンの「兄」として直前に挟む（子にすると面より手前に描かれてしまう）
        var glow = Rect("__Glow_" + rt.name, rt.parent);
        glow.anchorMin = rt.anchorMin;
        glow.anchorMax = rt.anchorMax;
        glow.pivot = rt.pivot;
        glow.anchoredPosition = rt.anchoredPosition + new Vector2(0f, -10f);
        glow.sizeDelta = rt.sizeDelta + new Vector2(120f, 110f);
        glow.SetSiblingIndex(rt.GetSiblingIndex());
        Img(glow, SpaceSprites.Glow, A(glowColor, 0.30f));

        var face = Rounded(rt, Color.white, radius, true);
        Gradient(face, top, bottom);
        return face;
    }

    /// <summary>細い角丸バー（×・＞・＋−などの記号を描く用）</summary>
    public static RectTransform Bar(RectTransform parent, Vector2 pos, float length, float thickness, float angle,
        Color color)
    {
        var rt = Rect("__Bar", parent, CENTER, pos, new Vector2(length, thickness));
        rt.localEulerAngles = new Vector3(0f, 0f, angle);
        Rounded(rt, color, thickness * 0.49f);
        return rt;
    }

    // ================================================================
    // 文字
    // ================================================================
    public static TextMeshProUGUI Text(Transform parent, string name, string text, float size, Color color,
        FontStyles style = FontStyles.Normal, TextAlignmentOptions align = TextAlignmentOptions.Center)
    {
        var rt = Rect(name, parent);
        var tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
        if (Font != null) tmp.font = Font;
        tmp.text = text;
        tmp.fontSize = size;
        tmp.color = color;
        tmp.fontStyle = style;
        tmp.alignment = align;
        tmp.raycastTarget = false;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.overflowMode = TextOverflowModes.Overflow;
        rt.gameObject.AddComponent<SpaceTextScaler>(); // 文字の大きさの設定（TextScale）を反映する
        return tmp;
    }

    // ---------------------------------------------------------------- 文字の大きさの設定
    private const string PREF_TEXT_SCALE = "space_text_scale";
    /// <summary>文字の大きさの段階（いまの大きさ＝標準）。2026-11-07 ユーザー依頼</summary>
    public static readonly float[] TEXT_SCALES = { 1f, 1.15f, 1.3f };
    public static readonly string[] TEXT_SCALE_NAMES = { "標準", "大きめ", "とても大きい" };
    private static int _textScaleIndex = -1;

    /// <summary>いまの文字の大きさの段階（TEXT_SCALES の番号。端末に保存）</summary>
    public static int TextScaleIndex
    {
        get
        {
            if (_textScaleIndex < 0)
                _textScaleIndex = Mathf.Clamp(PlayerPrefs.GetInt(PREF_TEXT_SCALE, 0), 0, TEXT_SCALES.Length - 1);
            return _textScaleIndex;
        }
        set
        {
            int v = Mathf.Clamp(value, 0, TEXT_SCALES.Length - 1);
            if (v == TextScaleIndex) return;
            _textScaleIndex = v;
            PlayerPrefs.SetInt(PREF_TEXT_SCALE, v);
            PlayerPrefs.Save();
            SpaceTextScaler.ApplyAll(); // いま出ている文字にもすぐ反映
        }
    }

    /// <summary>文字の大きさの倍率（標準=1）</summary>
    public static float TextScale => TEXT_SCALES[TextScaleIndex];

    /// <summary>親の1点を基準に置くテキスト</summary>
    public static TextMeshProUGUI Text(Transform parent, string name, string text, float size, Color color,
        Vector2 anchor, Vector2 pos, Vector2 box, FontStyles style = FontStyles.Normal,
        TextAlignmentOptions align = TextAlignmentOptions.Center, Vector2? pivot = null)
    {
        var tmp = Text(parent, name, text, size, color, style, align);
        var rt = tmp.rectTransform;
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = pivot ?? CENTER;
        rt.anchoredPosition = pos;
        rt.sizeDelta = box;
        return tmp;
    }

    /// <summary>
    /// 字間を広げる（英字の見出しなど）。characterSpacing は直接いじらず、必ずこれを使う。
    /// 字間を広げる文字はカーニング（文字の組ごとの間隔の調整）を切る:
    /// TextMeshPro がフォントから読み込むカーニングの記録には、「字間の指定を無視する」印（IgnoreSpacingAdjustments）が
    /// でたらめに付いている（初期化されていない値とみられる。jp.asset では英字の組の約6割。ビルドにもそのまま入る）。
    /// 字間を広げると印の付いた組の前後だけ字間が消え、「INTALES Cafe」の ES・Ca のように所々だけ詰まって見えるため。
    /// 字間を広げない文字（ふつうの文）ではこの印は効かないので、カーニングはそのまま使う
    /// </summary>
    public static void Tracking(TMP_Text t, float spacing)
    {
        if (t == null) return;
        t.characterSpacing = spacing;
        const UnityEngine.TextCore.OTL_FeatureTag KERN = UnityEngine.TextCore.OTL_FeatureTag.kern;
        if (spacing == 0f || t.fontFeatures == null || !t.fontFeatures.Contains(KERN)) return;
        var features = new List<UnityEngine.TextCore.OTL_FeatureTag>(t.fontFeatures);
        features.Remove(KERN);
        t.fontFeatures = features;
    }

    /// <summary>ユーザー入力の文字列をリッチテキストとして解釈させずに埋め込む</summary>
    public static string NoParse(string s) =>
        "<noparse>" + (s ?? "").Replace("</noparse>", "") + "</noparse>";

    // ================================================================
    // ボタン
    // ================================================================
    /// <summary>
    /// go をボタンにする。色の遷移は使わず、押下時の沈み込み（UIPressEffect）で触感を出す。
    /// </summary>
    public static Button Clickable(GameObject go, Graphic target, UnityAction onClick, bool pressEffect = true)
    {
        var btn = go.AddComponent<Button>();
        btn.transition = Selectable.Transition.None;
        btn.targetGraphic = target;
        var nav = btn.navigation;
        nav.mode = Navigation.Mode.None;
        btn.navigation = nav;
        if (onClick != null) btn.onClick.AddListener(onClick);
        if (pressEffect) UITheme.AddPressEffect(go);
        return btn;
    }

    // ================================================================
    // トースト
    // ================================================================
    /// <summary>
    /// 新ホーム（宇宙デザイン）が表示中か。NewHomeController が出入りで切り替える。
    /// 旧画面と共用のお知らせ（InfoModal・CheckOutModal・BuffCardModal・FriendMenuController.ShowToast）は
    /// これが true のあいだ宇宙デザイン版（SpaceNotice / Toast）で表示する。
    /// </summary>
    public static bool InUse { get; set; }

    private const string TOAST_NAME = "__SpaceToast";

    /// <summary>
    /// 画面下部に一時表示のメッセージを出す（宇宙デザイン版。旧画面は FriendMenuController.ShowToast）。
    /// 長い文は折り返して箱を縦に伸ばす。表示中のトーストがあればその上に積む。
    /// </summary>
    public static void Toast(string message, Canvas canvas = null, float bottom = 300f)
    {
        if (canvas == null) canvas = MainCanvas();
        if (canvas == null)
        {
            Debug.Log($"[Toast] {message}");
            return;
        }

        var size = CanvasSize(canvas);
        float w = Mathf.Min(880f, size.x - 80f);
        float y = bottom + SafeInsets(size).bottom;

        var rt = Rect(TOAST_NAME, canvas.transform, new Vector2(0.5f, 0f), Vector2.zero, new Vector2(w, 116f),
            new Vector2(0.5f, 0f));
        Glass(rt, 40f, GLASS_DEEP, A(CYAN, 0.45f));
        var label = Text(rt, "__Text", SafeNow(message), 32, TEXT, FontStyles.Bold);
        Stretch(label.rectTransform, 32f, 8f, 32f, 8f);
        label.textWrappingMode = TextWrappingModes.Normal;
        float h = Mathf.Clamp(label.GetPreferredValues(label.text, w - 64f, 0f).y + 44f, 116f, 320f);
        rt.sizeDelta = new Vector2(w, h);

        // 表示中のトーストの上に積む（消えかけのものも含めて重ならない位置へ）
        foreach (Transform child in canvas.transform)
        {
            if (child == rt || child.name != TOAST_NAME) continue;
            var other = (RectTransform)child;
            y = Mathf.Max(y, other.anchoredPosition.y + other.sizeDelta.y + 16f);
        }
        rt.anchoredPosition = new Vector2(0f, y);
        rt.SetAsLastSibling();
        rt.gameObject.AddComponent<SpaceToastFader>(); // 位置を決めてから付ける（Awake で基準位置を覚える）
    }

    // ================================================================
    // グリフの事前登録（TMP動的フォントのOOM対策）
    // ================================================================
    private static readonly HashSet<char> _missingGlyphs = new HashSet<char>();

    /// <summary>
    /// 表示予定の文字を、40文字/フレームに分けて事前にフォントへ登録する。
    /// TMP動的フォントに1フレームで大量（150文字超）の新規グリフを追加すると、実機でネイティブメモリが
    /// 暴走してOOMで落ちるため（BoardGameListController と同じ対策）。
    /// フォントに存在しない文字（絵文字など）は記録しておき、Safe() で表示から除去する。
    /// </summary>
    public static IEnumerator Prewarm(string text, int perFrame = 40)
    {
        var font = Font;
        if (font == null || string.IsNullOrEmpty(text)) yield break;

        var seen = new HashSet<char>();
        var sb = new StringBuilder();
        foreach (var c in text)
        {
            if (char.IsControl(c) || !seen.Add(c)) continue;
            if (font.HasCharacter(c)) continue;
            sb.Append(c);
        }

        string all = sb.ToString();
        for (int i = 0; i < all.Length; i += perFrame)
        {
            string chunk = all.Substring(i, Mathf.Min(perFrame, all.Length - i));
            if (!font.TryAddCharacters(chunk, out string missing) && !string.IsNullOrEmpty(missing))
            {
                // 本当にフォントに無い文字は数個のはず。大半が失敗するのはアトラス満杯などの一時要因なので
                // 除去対象にはしない（全部消えるのを防ぐ。表示が□になるだけ）
                if (missing.Length <= 8)
                    foreach (var m in missing) _missingGlyphs.Add(m);
                else
                    Debug.LogWarning($"[SpaceTheme] グリフ追加失敗 {missing.Length}文字（除去せず続行）");
            }
            yield return null;
        }
    }

    /// <summary>
    /// その場で表示する短い文言用: 未登録の文字をすぐフォントへ登録し、フォントに無い文字を取り除いて返す。
    /// QRの結果など事前に分からない文言（名前・景品名など）に使う。
    /// 1通の文言で増えるグリフは数十文字までなので、1フレームで登録しても安全な量に収まる。
    /// </summary>
    public static string SafeNow(string s)
    {
        var font = Font;
        if (font == null || string.IsNullOrEmpty(s)) return s;

        var seen = new HashSet<char>();
        var sb = new StringBuilder();
        foreach (var c in s)
        {
            if (char.IsControl(c) || !seen.Add(c)) continue;
            if (char.IsSurrogate(c) || c == '️' || c == '‍')
            {
                _missingGlyphs.Add(c); // 絵文字の部品はこのフォントでは表示できない
                continue;
            }
            if (!font.HasCharacter(c)) sb.Append(c);
        }
        if (sb.Length > 0 && !font.TryAddCharacters(sb.ToString(), out string missing)
            && !string.IsNullOrEmpty(missing) && missing.Length <= 8)
        {
            foreach (var m in missing) _missingGlyphs.Add(m);
        }
        return Safe(s);
    }

    /// <summary>フォントに無い文字を取り除く（欠落グリフによるTMPの無限再レイアウト対策）</summary>
    public static string Safe(string s)
    {
        if (string.IsNullOrEmpty(s) || _missingGlyphs.Count == 0) return s;
        StringBuilder sb = null;
        for (int i = 0; i < s.Length; i++)
        {
            if (_missingGlyphs.Contains(s[i]))
            {
                if (sb == null) sb = new StringBuilder(s, 0, i, s.Length);
            }
            else sb?.Append(s[i]);
        }
        return sb != null ? sb.ToString() : s;
    }

    // ================================================================
    // アニメーション
    // ================================================================
    /// <summary>
    /// duration 秒かけて step(0→1) を呼ぶ（easeOutCubic）。delay 秒待ってから始める。
    /// 呼び出し側の MonoBehaviour で StartCoroutine する（破棄されれば自動で止まる）。
    /// </summary>
    public static IEnumerator Tween(float duration, float delay, System.Action<float> step)
    {
        float w = 0f;
        while (w < delay)
        {
            w += Time.unscaledDeltaTime;
            yield return null;
        }
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            step(EaseOutCubic(Mathf.Clamp01(t / duration)));
            yield return null;
        }
        step(1f);
    }

    public static float EaseOutCubic(float x)
    {
        x = 1f - x;
        return 1f - x * x * x;
    }
}

/// <summary>
/// 縦方向のアルファグラデーション（頂点アルファに乗算）。ガラスの上部のつやに使う。
/// span: 上端から高さの何割で top→bottom に遷移するか（1で全体）。
/// </summary>
public class SpaceAlphaGradient : BaseMeshEffect
{
    public float Top = 1f;
    public float Bottom = 0f;
    public float Span = 1f;

    public SpaceAlphaGradient Set(float top, float bottom, float span)
    {
        Top = top;
        Bottom = bottom;
        Span = Mathf.Clamp01(span);
        if (graphic != null) graphic.SetVerticesDirty();
        return this;
    }

    public override void ModifyMesh(VertexHelper vh)
    {
        if (!IsActive() || vh.currentVertCount == 0) return;

        var v = new UIVertex();
        float minY = float.MaxValue, maxY = float.MinValue;
        for (int i = 0; i < vh.currentVertCount; i++)
        {
            vh.PopulateUIVertex(ref v, i);
            if (v.position.y < minY) minY = v.position.y;
            if (v.position.y > maxY) maxY = v.position.y;
        }
        float h = Mathf.Max(0.0001f, maxY - minY);

        for (int i = 0; i < vh.currentVertCount; i++)
        {
            vh.PopulateUIVertex(ref v, i);
            float t = (v.position.y - minY) / h; // 0=下端, 1=上端
            float k = Span > 0f ? Mathf.Clamp01((t - (1f - Span)) / Span) : t;
            float a = Mathf.Lerp(Bottom, Top, k);
            v.color.a = (byte)Mathf.Clamp(v.color.a * a, 0f, 255f);
            vh.SetUIVertex(v, i);
        }
    }
}

/// <summary>Screen.safeArea に合わせてアンカーを調整する（ノッチ・ホームバーを避ける）</summary>
public class SpaceSafeArea : MonoBehaviour
{
    private Rect _applied;
    private int _w, _h;

    private void OnEnable() => Apply();

    private void Update()
    {
        if (Screen.safeArea != _applied || Screen.width != _w || Screen.height != _h) Apply();
    }

    private void Apply()
    {
        _applied = Screen.safeArea;
        _w = Screen.width;
        _h = Screen.height;
        if (_w <= 0 || _h <= 0) return;

        var rt = (RectTransform)transform;
        rt.anchorMin = new Vector2(_applied.xMin / _w, _applied.yMin / _h);
        rt.anchorMax = new Vector2(_applied.xMax / _w, _applied.yMax / _h);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}

/// <summary>ダイアログなどの出現演出（フェードイン＋少し拡大）。付けたオブジェクトの CanvasGroup を使う</summary>
public class SpacePopIn : MonoBehaviour
{
    public RectTransform Target;
    public float Duration = 0.22f;
    public float FromScale = 0.94f;

    private CanvasGroup _group;
    private float _t;

    private void Awake()
    {
        _group = GetComponent<CanvasGroup>();
        if (_group == null) _group = gameObject.AddComponent<CanvasGroup>();
        _group.alpha = 0f;
    }

    private void Update()
    {
        _t += Time.unscaledDeltaTime;
        float k = SpaceTheme.EaseOutCubic(Mathf.Clamp01(_t / Duration));
        _group.alpha = k;
        if (Target != null) Target.localScale = Vector3.one * Mathf.Lerp(FromScale, 1f, k);
        if (_t >= Duration) enabled = false;
    }
}

/// <summary>トーストの表示→フェードアウト→破棄</summary>
public class SpaceToastFader : MonoBehaviour
{
    private const float FADE_IN = 0.18f;
    private const float LIFE = 2.4f;
    private const float FADE_OUT = 0.5f;

    private CanvasGroup _group;
    private RectTransform _rt;
    private Vector2 _base;
    private float _t;

    private void Awake()
    {
        _group = gameObject.AddComponent<CanvasGroup>();
        _group.alpha = 0f;
        _group.blocksRaycasts = false;
        _rt = (RectTransform)transform;
        _base = _rt.anchoredPosition;
    }

    private void Update()
    {
        _t += Time.unscaledDeltaTime;
        float a = _t < FADE_IN ? _t / FADE_IN : (_t > LIFE ? 1f - (_t - LIFE) / FADE_OUT : 1f);
        _group.alpha = Mathf.Clamp01(a);
        float rise = _t < FADE_IN ? (1f - _t / FADE_IN) * -20f : 0f; // 下から少し浮き上がる
        _rt.anchoredPosition = _base + new Vector2(0f, rise);
        if (_t > LIFE + FADE_OUT) Destroy(gameObject);
    }
}
