using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 1か月遊び放題パスのカード（高級感のある会員カード風。縦横比はクレジットカードと同じ）。
/// 3種類で見た目を変え、ALL DAY がいちばん豪華:
///  ・平日のみ … サファイアの青に銀の箔（2組の波線が交差する地紋）
///  ・土日のみ … ワイン色にローズゴールドの箔（右下からの放射と同心円の地紋）
///  ・ALL DAY  … 黒に金の箔（2重の花模様の地紋＋ヘアライン・金の星くず・金の二重の縁・きらめき・強めの光の帯）
/// 面（グラデーション・地紋・星くず・角丸）は種類ごとに実行時に1回だけ作るテクスチャ
/// （scratchpad の mockup29.py と同じ計算）。文字は TMP の縦グラデーションで箔押し風にする。
/// full=false は一覧用の小さなカード（面・縁・チップ・紋章だけで、文字は出さない）。
/// animate のとき、光の帯がときどきカードの上を走る（ALL DAY は金色のきらめきも瞬く）。
/// </summary>
public class SpacePassCard : MonoBehaviour
{
    public const float ASPECT = 1.586f;
    private const int FACE_W = 640, FACE_H = 404;

    private const string T_BRAND = "INTALES Cafe";
    private const string T_BOUGHT = "購入日";
    private const string T_EXPIRES = "有効期限";
    private const string T_UNTIL = " まで";
    private const string T_LEFT_A = "あと";
    private const string T_LEFT_B = "日";
    private const string T_LAST_DAY = "本日まで";
    private const string T_EXPIRED = "期限切れ";
    private const string T_SAMPLE_DAYS = "30日間";
    private static readonly Color INK = SpaceTheme.Hex(0x1E160C); // 金属の札の上の文字

    /// <summary>カードに出しうる文字（NewHome の起動時にまとめてフォントへ登録する）</summary>
    public static string GlyphText
    {
        get
        {
            var sb = new StringBuilder();
            sb.Append(T_BRAND).Append(T_BOUGHT).Append(T_EXPIRES).Append(T_UNTIL).Append(T_LEFT_A).Append(T_LEFT_B)
              .Append(T_LAST_DAY).Append(T_EXPIRED).Append(T_SAMPLE_DAYS).Append(StorePasses.T_NAME)
              .Append(StorePasses.T_SHORT).Append("0123456789/");
            foreach (var t in StorePasses.Types) sb.Append(t.Label).Append(t.En);
            return sb.ToString();
        }
    }

    // ================================================================
    // 見た目の種類
    // ================================================================
    private sealed class Style
    {
        public int Kind;                  // 0=平日のみ 1=土日のみ 2=ALL DAY
        public Color C0, C1, C2;          // 面のグラデーション（左上 → 中 → 右下）
        public Color Pattern;             // 地紋の色
        public float PatternA;
        public Color Dust;                // 星くずの色
        public float DustA;
        public int DustCount;
        public Color FoilTop, FoilBottom; // 箔（文字・縁・紋章）の上→下
        public Color ChipTop, ChipBottom; // チップの金属
        public Color Glow;                // カードのまわりの光
        public float SheenA;              // 光の帯の濃さ
    }

    private static readonly Style WEEKDAY = new Style
    {
        Kind = 0, C0 = SpaceTheme.Hex(0x2C5AB0), C1 = SpaceTheme.Hex(0x14306E), C2 = SpaceTheme.Hex(0x0A1636),
        Pattern = SpaceTheme.Hex(0xA8C8FF), PatternA = 0.13f, Dust = Color.white, DustA = 0.55f, DustCount = 240,
        FoilTop = SpaceTheme.Hex(0xFFFFFF), FoilBottom = SpaceTheme.Hex(0xA9B8DA),
        ChipTop = SpaceTheme.Hex(0xF3F5FA), ChipBottom = SpaceTheme.Hex(0x8E9AB8),
        Glow = SpaceTheme.Hex(0x4F7BFF), SheenA = 0.22f,
    };

    private static readonly Style WEEKEND = new Style
    {
        Kind = 1, C0 = SpaceTheme.Hex(0x9A3065), C1 = SpaceTheme.Hex(0x56143A), C2 = SpaceTheme.Hex(0x2A0A1E),
        Pattern = SpaceTheme.Hex(0xFFC4B4), PatternA = 0.13f, Dust = SpaceTheme.Hex(0xFFE2D8), DustA = 0.5f,
        DustCount = 200,
        FoilTop = SpaceTheme.Hex(0xFFE9DE), FoilBottom = SpaceTheme.Hex(0xD9967E),
        ChipTop = SpaceTheme.Hex(0xFFE6DA), ChipBottom = SpaceTheme.Hex(0xB97A66),
        Glow = SpaceTheme.Hex(0xFF6FA8), SheenA = 0.22f,
    };

    private static readonly Style ALLDAY = new Style
    {
        Kind = 2, C0 = SpaceTheme.Hex(0x3A3A42), C1 = SpaceTheme.Hex(0x16161B), C2 = SpaceTheme.Hex(0x050506),
        Pattern = SpaceTheme.Hex(0xE8C26A), PatternA = 0.17f, Dust = SpaceTheme.Hex(0xFFD98A), DustA = 0.8f,
        DustCount = 340,
        FoilTop = SpaceTheme.Hex(0xFFF4C8), FoilBottom = SpaceTheme.Hex(0xC8962E),
        ChipTop = SpaceTheme.Hex(0xFFF6DA), ChipBottom = SpaceTheme.Hex(0xB8862C),
        Glow = SpaceTheme.Hex(0xFFC75A), SheenA = 0.30f,
    };

    private static Style StyleOf(PassTypeDef type) =>
        type == StorePasses.AllDay ? ALLDAY : type == StorePasses.Weekend ? WEEKEND : WEEKDAY;

    /// <summary>種類ごとのカードの光の色（まわりの光・管理者のQR表示の光など）</summary>
    public static Color GlowColor(PassTypeDef type) => StyleOf(type).Glow;

    /// <summary>種類ごとの箔の色（上・下）。見出しの英字などに使う</summary>
    public static Color FoilColor(PassTypeDef type, float t = 0.35f)
    {
        var st = StyleOf(type);
        return Color.Lerp(st.FoilTop, st.FoilBottom, t);
    }

    // ================================================================
    // 作る
    // ================================================================
    /// <summary>
    /// カードを作る（中心に置く。大きさは width と縦横比で決まる）。pass が null なら見本
    /// （今日受け取った場合の日付で出す。管理者の受け取りQRの一覧用）
    /// </summary>
    public static RectTransform Create(Transform parent, PassTypeDef type, PassInfo pass, float width, bool full,
        bool animate)
    {
        var st = StyleOf(type);
        float h = width / ASPECT, u = width / 900f, pad = 48f * u;
        var now = DateTime.Now;
        bool expired = pass != null && !pass.IsActive(now);

        var root = SpaceTheme.Rect("PassCard_" + type.Id, parent, SpaceTheme.CENTER, Vector2.zero, new Vector2(width, h));

        // カードの下の影と、まわりの光
        var shadow = SpaceTheme.Rect("Shadow", root, SpaceTheme.CENTER, new Vector2(0f, -14f * u),
            new Vector2(width * 1.12f, h * 1.32f));
        SpaceTheme.Img(shadow, SpaceSprites.Glow, new Color(0f, 0f, 0f, 0.55f));
        if (full && !expired)
        {
            var glow = SpaceTheme.Rect("Glow", root, SpaceTheme.CENTER, Vector2.zero, new Vector2(width * 1.3f, h * 1.7f));
            SpaceTheme.Img(glow, SpaceSprites.Glow, SpaceTheme.A(st.Glow, st.Kind == 2 ? 0.30f : 0.22f));
        }

        // 面。光の帯ときらめきはこの角丸の形で切り抜く
        var face = SpaceTheme.Rect("Face", root);
        SpaceTheme.Stretch(face);
        SpaceTheme.Img(face, FaceSprite(st), Color.white);
        if (full && animate) face.gameObject.AddComponent<Mask>().showMaskGraphic = true;

        // 縁（箔）。ALL DAY は内側にもう1本
        float radius = width * 0.045f;
        var foilMid = Color.Lerp(st.FoilTop, st.FoilBottom, 0.5f);
        Border(root, 16f * u, Mathf.Max(2f, radius - 10f * u), SpaceTheme.A(foilMid, 0.85f));
        if (st.Kind == 2) Border(root, 25f * u, Mathf.Max(2f, radius - 17f * u), SpaceTheme.A(foilMid, 0.45f));

        // チップと紋章（惑星）
        var chip = SpaceTheme.Rect("Chip", root, new Vector2(0f, 1f), new Vector2(pad + 50f * u, -165f * u),
            new Vector2(100f * u, 76f * u));
        SpaceTheme.Gradient(SpaceTheme.Img(chip, ChipSprite, Color.white), st.ChipTop, st.ChipBottom);
        var emblemPos = new Vector2(-(pad + 56f * u), -165f * u);
        var emblemGlow = SpaceTheme.Rect("EmblemGlow", root, new Vector2(1f, 1f), emblemPos,
            new Vector2(168f * u, 168f * u));
        SpaceTheme.Img(emblemGlow, SpaceSprites.Glow, SpaceTheme.A(st.Glow, 0.28f));
        var emblem = SpaceTheme.Rect("Emblem", root, new Vector2(1f, 1f), emblemPos, new Vector2(104f * u, 104f * u));
        SpaceTheme.Gradient(SpaceTheme.Img(emblem, SpaceSprites.PlanetIcon, Color.white), st.FoilTop, st.FoilBottom);

        if (!full)
        {
            if (expired) root.gameObject.AddComponent<CanvasGroup>().alpha = 0.55f;
            return root;
        }

        // 文字（箔押し風）
        var brand = Foil(root, "Brand", T_BRAND, 24f * u, st, new Vector2(0f, 1f), new Vector2(pad, -52f * u),
            new Vector2(400f * u, 36f * u), TextAlignmentOptions.Left, new Vector2(0f, 0.5f), 0.9f);
        SpaceTheme.Tracking(brand, 40f);
        TypePill(root, type.Label, st, u, pad);

        var title = Foil(root, "Title", type.En, 64f * u, st, new Vector2(0f, 1f), new Vector2(pad, -276f * u),
            new Vector2(width - pad * 2f, 84f * u), TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
        SpaceTheme.Tracking(title, 6f);
        Foil(root, "Sub", StorePasses.T_NAME, 28f * u, st, new Vector2(0f, 1f), new Vector2(pad, -336f * u),
            new Vector2(600f * u, 40f * u), TextAlignmentOptions.Left, new Vector2(0f, 0.5f), 0.85f);

        var start = pass != null ? pass.Start : now.Date;
        var expiry = pass != null ? pass.Expiry : now.Date.AddDays(StorePasses.DAYS - 1);
        float col2 = pad + 280f * u;
        Foil(root, "BoughtLabel", T_BOUGHT, 20f * u, st, new Vector2(0f, 1f), new Vector2(pad, -423f * u),
            new Vector2(260f * u, 30f * u), TextAlignmentOptions.Left, new Vector2(0f, 0.5f), 0.62f);
        var bought = Foil(root, "Bought", StorePasses.DateText(start), 36f * u, st, new Vector2(0f, 1f),
            new Vector2(pad, -463f * u), new Vector2(270f * u, 50f * u), TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
        SpaceTheme.Tracking(bought, 2f);
        Foil(root, "ExpiresLabel", T_EXPIRES, 20f * u, st, new Vector2(0f, 1f), new Vector2(col2, -423f * u),
            new Vector2(260f * u, 30f * u), TextAlignmentOptions.Left, new Vector2(0f, 0.5f), 0.62f);
        var expires = Foil(root, "Expires", StorePasses.DateText(expiry) + $"<size=78%>{T_UNTIL}</size>", 36f * u, st,
            new Vector2(0f, 1f), new Vector2(col2, -463f * u), new Vector2(360f * u, 50f * u),
            TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
        SpaceTheme.Tracking(expires, 2f);

        string left = pass == null ? T_SAMPLE_DAYS
            : expired ? T_EXPIRED
            : pass.DaysLeft(now) <= 1 ? T_LAST_DAY
            : T_LEFT_A + pass.DaysLeft(now) + T_LEFT_B;
        LeftPill(root, left, st, u, pad, expired);

        if (expired)
        {
            root.gameObject.AddComponent<CanvasGroup>().alpha = 0.6f;
        }
        else if (animate)
        {
            var anim = root.gameObject.AddComponent<SpacePassCard>();
            anim.Init(face, st, width, h, u);
        }
        return root;
    }

    /// <summary>箔押し風の文字（白×縦グラデーション）。alpha で薄くする</summary>
    private static TextMeshProUGUI Foil(RectTransform parent, string name, string text, float size, Style st,
        Vector2 anchor, Vector2 pos, Vector2 box, TextAlignmentOptions align, Vector2 pivot, float alpha = 1f)
    {
        var t = SpaceTheme.Text(parent, name, text, size, new Color(1f, 1f, 1f, alpha), anchor, pos, box,
            FontStyles.Bold, align, pivot);
        t.enableVertexGradient = true;
        t.colorGradient = new VertexGradient(st.FoilTop, st.FoilTop, st.FoilBottom, st.FoilBottom);
        return t;
    }

    private static void Border(RectTransform root, float inset, float radius, Color color)
    {
        var rt = SpaceTheme.Rect("Border", root);
        SpaceTheme.Stretch(rt, inset, inset, inset, inset);
        SpaceTheme.RoundedStroke(rt, color, radius);
    }

    /// <summary>右上の種類の札（薄い箔の地＋箔の縁＋箔の文字）</summary>
    private static void TypePill(RectTransform root, string label, Style st, float u, float pad)
    {
        var mid = Color.Lerp(st.FoilTop, st.FoilBottom, 0.5f);
        var pill = SpaceTheme.Rect("Type", root, new Vector2(1f, 1f), new Vector2(-pad, -52f * u),
            new Vector2(200f * u, 52f * u), new Vector2(1f, 0.5f));
        SpaceTheme.Rounded(pill, SpaceTheme.A(mid, 0.16f), 25f * u);
        var edge = SpaceTheme.Rect("Edge", pill);
        SpaceTheme.Stretch(edge);
        SpaceTheme.RoundedStroke(edge, SpaceTheme.A(mid, 0.85f), 25f * u);
        var text = Foil(pill, "Label", label, 26f * u, st, SpaceTheme.CENTER, Vector2.zero, new Vector2(400f * u, 52f * u),
            TextAlignmentOptions.Center, SpaceTheme.CENTER);
        float w = text.GetPreferredValues(label).x + 44f * u;
        pill.sizeDelta = new Vector2(w, 52f * u);
        text.rectTransform.sizeDelta = new Vector2(w, 52f * u);
    }

    /// <summary>右下の残り日数の札（金属の地に暗い文字。期限切れは灰色）</summary>
    private static void LeftPill(RectTransform root, string text, Style st, float u, float pad, bool expired)
    {
        var pill = SpaceTheme.Rect("Left", root, new Vector2(1f, 0f), new Vector2(-pad, 73f * u),
            new Vector2(200f * u, 50f * u), new Vector2(1f, 0.5f));
        var face = SpaceTheme.Rounded(pill, Color.white, 25f * u);
        if (expired) face.color = new Color(0.42f, 0.44f, 0.52f, 1f);
        else SpaceTheme.Gradient(face, st.FoilTop, st.FoilBottom);
        var label = SpaceTheme.Text(pill, "Label", text, 26f * u, expired ? SpaceTheme.TEXT : INK, SpaceTheme.CENTER,
            Vector2.zero, new Vector2(400f * u, 50f * u), FontStyles.Bold);
        float w = label.GetPreferredValues(text).x + 40f * u;
        pill.sizeDelta = new Vector2(w, 50f * u);
        label.rectTransform.sizeDelta = new Vector2(w, 50f * u);
    }

    // ================================================================
    // 動き（光の帯・きらめき）
    // ================================================================
    private RectTransform _sheen;
    private Image _sheenImg;
    private float _sheenA, _w, _t0;
    private readonly List<(RectTransform rt, Image img, float phase, float speed, float size)> _sparkles =
        new List<(RectTransform, Image, float, float, float)>();
    private Color _sparkleColor;

    private const float SHEEN_PERIOD = 4.0f;  // 光の帯が走る間隔
    private const float SHEEN_SWEEP = 1.25f;  // 端から端まで走る時間

    private void Init(RectTransform face, Style st, float w, float h, float u)
    {
        _w = w;
        _sheenA = st.SheenA;
        _t0 = Time.unscaledTime - (SHEEN_PERIOD - 0.5f); // 開いて少ししたら最初の帯が走る
        _sheen = SpaceTheme.Rect("Sheen", face, SpaceTheme.CENTER, Vector2.zero, new Vector2(140f * u, h * 2.6f));
        _sheen.localEulerAngles = new Vector3(0f, 0f, -25f);
        _sheenImg = SpaceTheme.Img(_sheen, SpaceSprites.Glow, new Color(1f, 1f, 1f, 0f));
        _sheen.gameObject.SetActive(false);

        if (st.Kind != 2) return;
        // ALL DAY だけ、金色のきらめきがところどころで瞬く（位置はカードの左上からの割合）
        _sparkleColor = Color.Lerp(st.FoilTop, Color.white, 0.5f);
        var spots = new[]
        {
            new Vector3(0.20f, 0.30f, 46f), new Vector3(0.63f, 0.17f, 38f), new Vector3(0.90f, 0.60f, 52f),
            new Vector3(0.44f, 0.80f, 34f), new Vector3(0.76f, 0.88f, 40f), new Vector3(0.33f, 0.56f, 30f),
        };
        for (int i = 0; i < spots.Length; i++)
        {
            var s = spots[i];
            var rt = SpaceTheme.Rect("Sparkle", face, new Vector2(0f, 1f), new Vector2(s.x * w, -s.y * h),
                new Vector2(s.z * u, s.z * u));
            var img = SpaceTheme.Img(rt, SpaceSprites.Sparkle, new Color(1f, 1f, 1f, 0f));
            _sparkles.Add((rt, img, i * 1.7f, 1.1f + 0.23f * i, s.z * u));
        }
    }

    private void Update()
    {
        if (_sheen == null) return;
        float t = Time.unscaledTime - _t0;
        float p = (t % SHEEN_PERIOD) / SHEEN_SWEEP;
        bool on = p <= 1f;
        if (_sheen.gameObject.activeSelf != on) _sheen.gameObject.SetActive(on);
        if (on)
        {
            float e = p * p * (3f - 2f * p);
            _sheen.anchoredPosition = new Vector2(Mathf.Lerp(-0.85f, 0.85f, e) * _w, 0f);
            _sheenImg.color = new Color(1f, 1f, 1f, _sheenA * Mathf.Sin(Mathf.PI * p));
        }

        foreach (var s in _sparkles)
        {
            float k = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(t * s.speed + s.phase)), 8f);
            s.rt.localScale = Vector3.one * (0.4f + 0.6f * k);
            s.rt.localEulerAngles = new Vector3(0f, 0f, t * 40f + s.phase * 20f);
            s.img.color = SpaceTheme.A(_sparkleColor, 0.95f * k);
        }
    }

    // ================================================================
    // 画像（種類ごとに1回だけ作る）
    // ================================================================
    private static readonly Dictionary<int, Sprite> Faces = new Dictionary<int, Sprite>();
    private static Sprite _chip, _icon;

    /// <summary>
    /// カードの面: 斜めのグラデーション＋地紋＋周辺の暗がり＋星くず＋角丸
    /// （mockup29.py の face() と同じ計算。座標は上からの距離で考える）
    /// </summary>
    private static Sprite FaceSprite(Style st)
    {
        if (Faces.TryGetValue(st.Kind, out var cached) && cached != null) return cached;
        int w = FACE_W, h = FACE_H;
        var col = new Color[w * h];
        for (int y = 0; y < h; y++)
        {
            float py = h - y - 0.5f; // テクスチャは下から並ぶので、上からの距離に直す
            float v = py / h;
            for (int x = 0; x < w; x++)
            {
                float px = x + 0.5f, u = px / w;
                float t = Mathf.Clamp01(u * 0.62f + v * 0.38f);
                var c = t < 0.5f ? Color.Lerp(st.C0, st.C1, t * 2f) : Color.Lerp(st.C1, st.C2, t * 2f - 1f);
                c = Color.Lerp(c, st.Pattern, Pattern(st.Kind, px, py, w, h) * st.PatternA);
                float du = (u - 0.42f) * 1.15f, dv = (v - 0.40f) * 1.5f;
                float vig = Mathf.Clamp01(1f - 0.38f * (du * du + dv * dv));
                col[y * w + x] = new Color(c.r * vig, c.g * vig, c.b * vig, 1f);
            }
        }

        // 星くず（決まった並び）
        var rng = new System.Random(17 + st.Kind);
        for (int i = 0; i < st.DustCount; i++)
        {
            float x0 = (float)rng.NextDouble() * w, y0 = (float)rng.NextDouble() * h;
            float rad = 0.5f + (float)rng.NextDouble();
            float al = (0.15f + 0.85f * (float)rng.NextDouble()) * st.DustA;
            for (int yy = Mathf.Max(0, (int)(y0 - 3)); yy < Mathf.Min(h, (int)(y0 + 4)); yy++)
            for (int xx = Mathf.Max(0, (int)(x0 - 3)); xx < Mathf.Min(w, (int)(x0 + 4)); xx++)
            {
                float d = Mathf.Sqrt((xx + 0.5f - x0) * (xx + 0.5f - x0) + (yy + 0.5f - y0) * (yy + 0.5f - y0));
                float k = Mathf.Pow(Mathf.Clamp01(1f - d / (rad + 0.8f)), 1.5f) * al;
                if (k <= 0f) continue;
                int idx = (h - 1 - yy) * w + xx; // yy は上からの行
                col[idx] = Color.Lerp(col[idx], st.Dust, k);
            }
        }

        // 角丸（カードの幅の4.5%）
        float rr = w * 0.045f;
        var px32 = new Color32[w * h];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            float qx = Mathf.Max(Mathf.Abs(x + 0.5f - w * 0.5f) - (w * 0.5f - rr), 0f);
            float qy = Mathf.Max(Mathf.Abs(y + 0.5f - h * 0.5f) - (h * 0.5f - rr), 0f);
            float a = Mathf.Clamp01(0.5f - (Mathf.Sqrt(qx * qx + qy * qy) - rr));
            var c = col[y * w + x];
            px32[y * w + x] = new Color32(To8(c.r), To8(c.g), To8(c.b), To8(a));
        }
        var sp = MakeSprite(w, h, px32, "pass_face_" + st.Kind);
        Faces[st.Kind] = sp;
        return sp;
    }

    /// <summary>地紋の濃さ（0〜1）。px, py は上からの座標</summary>
    private static float Pattern(int kind, float px, float py, int w, int h)
    {
        switch (kind)
        {
            case 0: // 2組の波線が交差する
            {
                float a1 = Mathf.Abs(Mathf.Sin(py * 0.16f + 7f * Mathf.Sin(px * 0.021f)));
                float a2 = Mathf.Abs(Mathf.Sin(py * 0.16f + 7f * Mathf.Sin(px * 0.021f + 2.1f)));
                return Mathf.Max(SS(0.12f, 0f, a1), SS(0.12f, 0f, a2));
            }
            case 1: // 右下からの放射と同心円
            {
                float dx = px - w * 0.86f, dy = py - h * 0.88f;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float ang = Mathf.Atan2(dy, dx);
                float l1 = SS(0.10f, 0f, Mathf.Abs(Mathf.Sin(ang * 40f))) * SS(40f, 140f, r) * 0.8f;
                float l2 = SS(0.08f, 0f, Mathf.Abs(Mathf.Sin(r * 0.11f))) * 0.7f;
                return Mathf.Max(l1, l2);
            }
            default: // 2重の花模様（ロゼット）＋細いヘアライン
            {
                float dx = px - w * 0.80f, dy = py - h * 0.45f;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float th = Mathf.Atan2(dy, dx);
                float fade = 1f - SS(150f, 250f, r);
                float l1 = SS(0.10f, 0f, Mathf.Abs(Mathf.Sin(r * 0.085f + 2.4f * Mathf.Sin(th * 12f)))) * fade;
                float l2 = SS(0.10f, 0f, Mathf.Abs(Mathf.Sin(r * 0.085f + 2.4f * Mathf.Sin(th * 12f + Mathf.PI))))
                           * fade * 0.8f;
                float hair = SS(0.05f, 0f, Mathf.Abs(Mathf.Sin((px + py * 0.35f) * 0.9f))) * 0.25f;
                return Mathf.Max(Mathf.Max(l1, l2), hair);
            }
        }
    }

    /// <summary>
    /// ICチップ（白〜灰の濃淡。Image に縦グラデーションをかけて金属の色にする）。
    /// 中央の四角と、そこから伸びる仕切り線
    /// </summary>
    private static Sprite ChipSprite
    {
        get
        {
            if (_chip != null) return _chip;
            const int w = 100, h = 76;
            const float lw = 1.3f;
            float rr = h * 0.18f;
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float fx = x + 0.5f, fy = h - y - 0.5f; // fy は上から
                float qx = Mathf.Max(Mathf.Abs(fx - w * 0.5f) - (w * 0.5f - rr), 0f);
                float qy = Mathf.Max(Mathf.Abs(fy - h * 0.5f) - (h * 0.5f - rr), 0f);
                float a = Mathf.Clamp01(0.5f - (Mathf.Sqrt(qx * qx + qy * qy) - rr));

                float d = 99f;
                // 左右の横線
                if (fx < w * 0.36f || fx > w * 0.64f)
                    d = Mathf.Min(d, Mathf.Min(Mathf.Abs(fy - h * 0.34f), Mathf.Abs(fy - h * 0.66f)));
                // 中央の四角の縁
                d = Mathf.Min(d, RectEdge(fx, fy, w * 0.36f, h * 0.20f, w * 0.64f, h * 0.80f));
                // 上下の縦線
                if (fy < h * 0.20f || fy > h * 0.80f) d = Mathf.Min(d, Mathf.Abs(fx - w * 0.5f));
                float line = Mathf.Clamp01(lw - d + 0.5f);
                float shade = Mathf.Lerp(1f, 0.38f, line);
                byte g = To8(shade);
                px[y * w + x] = new Color32(g, g, g, To8(a));
            }
            _chip = MakeSprite(w, h, px, "pass_chip");
            return _chip;
        }
    }

    /// <summary>管理者用の「パスの購入者」ボタンのアイコン（白。カードの輪郭＋磁気の帯＋チップ）</summary>
    public static Sprite PassIcon
    {
        get
        {
            if (_icon != null) return _icon;
            const int s = 96;
            const float x0 = 10f, x1 = 86f, y0 = 22f, y1 = 74f, rr = 9f, stroke = 6f;
            var px = new Color32[s * s];
            for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float fx = x + 0.5f, fy = s - y - 0.5f; // fy は上から
                float cx = (x0 + x1) * 0.5f, cy = (y0 + y1) * 0.5f;
                float hw = (x1 - x0) * 0.5f, hh = (y1 - y0) * 0.5f;
                float qx = Mathf.Max(Mathf.Abs(fx - cx) - (hw - rr), 0f);
                float qy = Mathf.Max(Mathf.Abs(fy - cy) - (hh - rr), 0f);
                float sd = Mathf.Sqrt(qx * qx + qy * qy) - rr; // 外形からの距離（内側は負）
                float outline = Mathf.Clamp01(stroke * 0.5f + 0.5f - Mathf.Abs(sd + stroke * 0.5f));
                float inside = Mathf.Clamp01(0.5f - sd);
                float stripe = fy > y0 + 12f && fy < y0 + 20f ? inside : 0f;                 // 磁気の帯
                float chip = fx > x0 + 12f && fx < x0 + 28f && fy > y0 + 28f && fy < y0 + 40f ? inside : 0f; // チップ
                px[y * s + x] = new Color32(255, 255, 255, To8(Mathf.Max(outline, Mathf.Max(stripe, chip))));
            }
            _icon = MakeSprite(s, s, px, "pass_icon");
            return _icon;
        }
    }

    /// <summary>四角の縁までの距離（中は内側への距離、外は外側への距離）</summary>
    private static float RectEdge(float x, float y, float x0, float y0, float x1, float y1)
    {
        bool inside = x >= x0 && x <= x1 && y >= y0 && y <= y1;
        if (inside) return Mathf.Min(Mathf.Min(x - x0, x1 - x), Mathf.Min(y - y0, y1 - y));
        float dx = Mathf.Max(Mathf.Max(x0 - x, 0f), x - x1);
        float dy = Mathf.Max(Mathf.Max(y0 - y, 0f), y - y1);
        return Mathf.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>mockup29.py の ss(e0, e1, x) と同じ（e0 &gt; e1 なら x が小さいほど 1）</summary>
    private static float SS(float e0, float e1, float x)
    {
        float t = Mathf.Clamp01((x - e0) / (e1 - e0));
        return t * t * (3f - 2f * t);
    }

    private static byte To8(float v) => (byte)(Mathf.Clamp01(v) * 255f + 0.5f);

    private static Sprite MakeSprite(int w, int h, Color32[] px, string name)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            name = name,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };
        tex.SetPixels32(px);
        tex.Apply(false, true); // CPU側のコピーは解放する
        var sp = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        sp.name = name;
        return sp;
    }
}
