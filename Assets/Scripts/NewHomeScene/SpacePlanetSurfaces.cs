using System;
using System.Threading.Tasks;

/// <summary>
/// 惑星の表面テクスチャを手続き的に作る（画像アセット不要）。
/// 正距円筒図法（横:縦 = 2:1。左右の端はつながる）で、球面上の点の3Dノイズから塗るので継ぎ目も極の歪みも出ない。
///
/// UnityEngine を一切使わない純粋な計算なので、ワーカースレッドで実行できる（SpacePlanets が非同期で呼ぶ）。
/// 出力は RGBA の byte 配列（ストレートアルファ）。Unity 側で Texture2D.SetPixelData に渡す。
///   Surface … 地表（不透明）
///   Glow    … 夜側で光る部分（溶岩・街の灯り・発光する森など）。無い惑星は null
///   Clouds  … 雲（白＋アルファ。地表より少し速く回す）。無い惑星は null
/// </summary>
public static class SpacePlanetSurfaces
{
    public sealed class Maps
    {
        public int Width, Height;
        public byte[] Surface, Glow, Clouds;
        public int CloudWidth, CloudHeight;
    }

    /// <summary>惑星 kind の地図を作る（height = width / 2。雲は半分の解像度）</summary>
    public static Maps Generate(string kind, int width)
    {
        var painter = CreatePainter(kind);
        int w = Math.Max(16, width & ~1), h = w / 2;
        painter.PixelAngle = MathF.PI / h;
        var maps = new Maps { Width = w, Height = h, Surface = new byte[w * h * 4] };
        if (painter.HasGlow) maps.Glow = new byte[w * h * 4];
        Fill(w, h, (x, y, z, lat, row, i) =>
        {
            var px = default(Px);
            painter.Paint(x, y, z, lat, ref px);
            Put(maps.Surface, i, px.Surface, 1f);
            if (maps.Glow != null) Put(maps.Glow, i, px.Glow, px.GlowA);
        });
        painter.Post(maps); // 星や稲妻など、点や線を後から描き足す惑星

        if (painter.HasClouds)
        {
            int cw = Math.Max(16, (w / 2) & ~1), ch = cw / 2;
            maps.CloudWidth = cw;
            maps.CloudHeight = ch;
            maps.Clouds = new byte[cw * ch * 4];
            Fill(cw, ch, (x, y, z, lat, row, i) =>
            {
                float a = painter.Cloud(x, y, z, lat);
                Put(maps.Clouds, i, new C3(1f, 1f, 1f), a);
            });
        }
        return maps;
    }

    // ================================================================
    // 力を失った惑星の色（2026-11-10 ユーザー指定「1回使って退店すると力を失い、色も少し白く淡くなる」）
    // ================================================================
    /// <summary>残す鮮やかさ（1＝そのまま）</summary>
    public const float FADE_SATURATION = 0.62f;
    /// <summary>白に寄せる割合（0＝そのまま）</summary>
    public const float FADE_WHITEN = 0.32f;

    /// <summary>
    /// 地図（RGBA。ストレートアルファ）の色を、鮮やかさを落として白に寄せる（明るさの順は保つ。アルファはそのまま）。
    /// 地表・雲・環に使う。光る層（Glow）は力を失った惑星では作らない（夜の灯りが消える）
    /// </summary>
    public static void Fade(byte[] px)
    {
        if (px == null) return;
        for (int i = 0; i + 3 < px.Length; i += 4)
        {
            float r = px[i], g = px[i + 1], b = px[i + 2];
            FadeColor(ref r, ref g, ref b, 255f);
            px[i] = (byte)(r + 0.5f);
            px[i + 1] = (byte)(g + 0.5f);
            px[i + 2] = (byte)(b + 0.5f);
        }
    }

    /// <summary>1色分（max は白の値: byte なら 255、0〜1 の色なら 1）</summary>
    public static void FadeColor(ref float r, ref float g, ref float b, float max)
    {
        float l = 0.299f * r + 0.587f * g + 0.114f * b;
        r = l + (r - l) * FADE_SATURATION;
        g = l + (g - l) * FADE_SATURATION;
        b = l + (b - l) * FADE_SATURATION;
        r = Math.Clamp(r + (max - r) * FADE_WHITEN, 0f, max);
        g = Math.Clamp(g + (max - g) * FADE_WHITEN, 0f, max);
        b = Math.Clamp(b + (max - b) * FADE_WHITEN, 0f, max);
    }

    /// <summary>
    /// 環の画像（見る角度でつぶした楕円のまま作る。width×height の楕円の外周＝半径1、inner〜1 に帯を描く）。
    /// あらかじめつぶしておくと、表示時に縦だけ縮めたときの縞のちらつき（モアレ）が出ない。2×2 の超標本化で滑らかにする。
    /// Unity 側で傾け、奥半分を惑星の後ろ・手前半分（下半分）を惑星の前に重ねる。RGBA ストレートアルファ。
    /// </summary>
    public static byte[] RingMap(string kind, int width, int height, float inner)
    {
        var px = new byte[width * height * 4];
        var n = new Noise(9001);
        float hx = width / 2f, hy = height / 2f;
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            float sumA = 0f;
            C3 sumC = default;
            for (int s = 0; s < 4; s++)
            {
                float dx = (x + 0.25f + 0.5f * (s & 1) - hx) / (hx - 1f);
                float dy = (y + 0.25f + 0.5f * (s >> 1) - hy) / (hy - 1f);
                RingSample(kind, MathF.Sqrt(dx * dx + dy * dy), inner, n, out var col, out float alpha);
                if (alpha <= 0f) continue;
                sumA += alpha;
                sumC.R += col.R * alpha;
                sumC.G += col.G * alpha;
                sumC.B += col.B * alpha;
            }
            if (sumA <= 0f) continue;
            int i = (y * width + x) * 4;
            px[i] = ToByte(sumC.R / sumA);
            px[i + 1] = ToByte(sumC.G / sumA);
            px[i + 2] = ToByte(sumC.B / sumA);
            px[i + 3] = ToByte(sumA / 4f);
        }
        return px;
    }

    /// <summary>
    /// 環の1点の色と不透明度。r は外縁を 1 とした中心からの距離、inner は内縁の位置（0〜1）。
    /// 惑星ごとに帯の模様が違う（orbis: 金の太い環／noir: 光る降着円盤／celesta: 細く淡い氷の環）
    /// </summary>
    private static void RingSample(string kind, float r, float inner, Noise n, out C3 col, out float alpha)
    {
        col = default;
        alpha = 0f;
        if (r < inner - 0.02f || r > 1.01f) return;
        float t = Clamp01((r - inner) / (1f - inner)); // 0=内縁 1=外縁（縁のぼかし部分ではみ出さないよう 0〜1 に収める）
        float edge = Smooth(inner - 0.006f, inner + 0.012f, r) * (1f - Smooth(0.982f, 1.0f, r));
        switch (kind)
        {
            case "noir":
            {
                // 内側ほど白く熱く、外へ向かって紫→水色に冷めていく光の円盤
                float streak = (0.75f + 0.25f * (0.5f + 0.5f * n.At(r * 60f, 1.5f, 0.5f))) * (0.75f + 0.25f * MathF.Sin(r * 210f));
                alpha = Clamp01(edge * MathF.Pow(1f - t, 1.3f) * streak * 1.3f);
                col = C3.Lerp(C3.Hex(0xFFFFFF), C3.Hex(0xD8C2FF), Smooth(0.0f, 0.18f, t));
                col = C3.Lerp(col, C3.Hex(0x9B7BFF), Smooth(0.15f, 0.55f, t));
                col = C3.Lerp(col, C3.Hex(0x5CD8FF), Smooth(0.55f, 1.0f, t));
                break;
            }
            case "celesta":
            {
                // 細い帯が数本だけの淡い氷の環
                float bands = 0.55f + 0.45f * MathF.Sin(t * 26f + 0.8f);
                float gap = 1f - 0.8f * Gauss(t, 0.48f, 0.06f);
                alpha = Clamp01(edge * bands * gap) * 0.7f;
                col = C3.Lerp(C3.Hex(0xEFFFFF), C3.Hex(0xA6E3F2), t);
                break;
            }
            default:
            {
                // orbis: 金〜クリーム〜桃。細かい縞＋暗い溝を2本
                float fine = 0.62f + 0.38f * (0.5f + 0.5f * n.At(r * 46f, 0.5f, 0.5f)) * (0.6f + 0.4f * MathF.Sin(r * 180f));
                float gap1 = 1f - 0.88f * Gauss(t, 0.56f, 0.024f);
                float gap2 = 1f - 0.65f * Gauss(t, 0.80f, 0.014f);
                float density = (0.35f + 0.65f * Smooth(0.0f, 0.22f, t)) * (1f - 0.55f * Smooth(0.86f, 1f, t));
                alpha = Clamp01(edge * fine * gap1 * gap2 * density) * 0.92f;
                col = C3.Lerp(C3.Hex(0xF4A98E), C3.Hex(0xFFD58A), Smooth(0.05f, 0.45f, t));
                col = C3.Lerp(col, C3.Hex(0xFFF1D2), Smooth(0.55f, 0.95f, t) * 0.8f);
                col *= 0.86f + 0.14f * (0.5f + 0.5f * MathF.Sin(r * 140f + 1.3f));
                break;
            }
        }
    }

    /// <summary>一覧のサムネイル（止まった惑星の1枚絵）の描き方。SpacePlanetView と同じ見た目になる値を渡す</summary>
    public sealed class ThumbSpec
    {
        public string Kind;
        public float Tilt, Incline, Ambient, GlowBase, Spin;
        public float RimR, RimG, RimB, RimAlpha;
        public bool RimEven;
        /// <summary>球の直径 ÷ 画像の一辺</summary>
        public float SphereFrac;
        /// <summary>力を失った惑星（環の色も淡くする。地表・雲は渡す地図の側で淡くしておく）</summary>
        public bool Faded;
        public bool Ring;
        public float RingOuter, RingInner;
    }

    /// <summary>
    /// 一覧用のサムネイルを1枚の画像に描く（環の奥 → 地表・夜光・雲 → 大気の縁 → 環の手前）。
    /// SpaceSphere・SpacePlanetView と同じ式で、画素ごとに計算する。まわりの光（ハロー）は含めない。
    /// 一覧に何十個並んでも描画が軽いように、動くメッシュではなくこの1枚絵を使う。
    /// </summary>
    public static byte[] Thumbnail(Maps m, ThumbSpec s, int size)
    {
        var px = new byte[size * size * 4];
        float half = size * 0.5f, rad = half * s.SphereFrac;
        float ll = MathF.Sqrt(0.55f * 0.55f + 0.5f * 0.5f + 0.67f * 0.67f);
        float lx = -0.55f / ll, ly = 0.50f / ll, lz = 0.67f / ll;
        float l2 = MathF.Sqrt(lx * lx + ly * ly), l2x = lx / l2, l2y = ly / l2;
        float tau = s.Tilt * MathF.PI / 180f, inc = s.Incline * MathF.PI / 180f;
        float ct = MathF.Cos(tau), st = MathF.Sin(tau), ci = MathF.Cos(inc), si = MathF.Sin(inc);
        float flat = Math.Max(0.05f, si);
        var ringNoise = new Noise(9001);
        var rim = new C3(s.RimR, s.RimG, s.RimB);

        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float fx = (x + 0.5f - half) / rad, fy = (y + 0.5f - half) / rad;
            C3 acc = default;
            float accA = 0f;

            // 環（奥半分）
            float qy = 0f, ringA = 0f;
            C3 ringC = default;
            if (s.Ring)
            {
                float qx = fx * ct + fy * st;
                qy = -fx * st + fy * ct;
                float rr = MathF.Sqrt(qx * qx + (qy / flat) * (qy / flat)) / s.RingOuter;
                RingSample(s.Kind, rr, s.RingInner, ringNoise, out ringC, out ringA);
                if (s.Faded) FadeColor(ref ringC.R, ref ringC.G, ref ringC.B, 1f);
                if (qy > 0f) Over(ref acc, ref accA, ringC, ringA);
            }

            // 球
            float rho = MathF.Sqrt(fx * fx + fy * fy);
            float cover = Clamp01((1f - rho) * rad + 0.5f);
            if (cover > 0f)
            {
                float sx = rho > 1f ? fx / rho : fx, sy = rho > 1f ? fy / rho : fy;
                float z = MathF.Sqrt(Math.Max(0f, 1f - sx * sx - sy * sy));
                float x1 = sx * ct + sy * st, y1 = -sx * st + sy * ct;
                float y2 = y1 * ci + z * si, z2 = -y1 * si + z * ci;
                float lat = MathF.Asin(Math.Max(-1f, Math.Min(1f, y2)));
                float lon = MathF.Atan2(x1, z2);
                float u = lon / (MathF.PI * 2f) - s.Spin, v = 0.5f + lat / MathF.PI;

                float ndl = sx * lx + sy * ly + z * lz;
                float shade = (s.Ambient + (1f - s.Ambient) * Clamp01((ndl + 0.15f) / 1.15f)) * (0.82f + 0.18f * z);
                C3 col = Sample(m.Surface, m.Width, m.Height, u, v, out _) * shade;
                if (m.Glow != null)
                {
                    C3 g = Sample(m.Glow, m.Width, m.Height, u, v, out float ga);
                    float tn = Clamp01((0.25f - ndl) / 0.40f);
                    ga *= s.GlowBase + (1f - s.GlowBase) * tn * tn * (3f - 2f * tn);
                    col = C3.Lerp(col, g, ga);
                }
                if (m.Clouds != null)
                {
                    Sample(m.Clouds, m.CloudWidth, m.CloudHeight, u, v, out float ca);
                    col = C3.Lerp(col, new C3(shade, shade, shade), ca);
                }
                Over(ref acc, ref accA, col, cover);
            }

            // 大気の縁（SpaceSprites.PlanetRim と同じ式）
            float facing = 1f;
            if (!s.RimEven && rho > 0.0001f)
            {
                facing = Clamp01((fx * l2x + fy * l2y) / rho * 0.5f + 0.5f);
                facing = 0.18f + 0.82f * MathF.Pow(facing, 1.4f);
            }
            float rimIn = rho <= 1f ? MathF.Pow(rho, 7f) * 0.85f : 0f;
            float rimOut = rho > 1f ? MathF.Pow(Clamp01(1f - (rho - 1f) / 0.16f), 2.4f) * 0.55f : 0f;
            Over(ref acc, ref accA, rim, Clamp01((rimIn + rimOut) * facing * s.RimAlpha));

            // 環（手前半分）
            if (s.Ring && qy <= 0f) Over(ref acc, ref accA, ringC, ringA);

            if (accA <= 0.0001f) continue;
            int i = (y * size + x) * 4;
            px[i] = ToByte(acc.R / accA);
            px[i + 1] = ToByte(acc.G / accA);
            px[i + 2] = ToByte(acc.B / accA);
            px[i + 3] = ToByte(accA);
        }
        return px;
    }

    /// <summary>乗算済みアルファで上に重ねる</summary>
    private static void Over(ref C3 acc, ref float accA, C3 src, float a)
    {
        if (a <= 0f) return;
        acc.R = src.R * a + acc.R * (1f - a);
        acc.G = src.G * a + acc.G * (1f - a);
        acc.B = src.B * a + acc.B * (1f - a);
        accA = a + accA * (1f - a);
    }

    /// <summary>地図をバイリニアで読む（横は1周つながる、縦は端で止める）</summary>
    private static C3 Sample(byte[] buf, int w, int h, float u, float v, out float alpha)
    {
        float fx = (u - MathF.Floor(u)) * w - 0.5f, fy = v * h - 0.5f;
        int x0 = (int)MathF.Floor(fx), y0 = (int)MathF.Floor(fy);
        float tx = fx - x0, ty = fy - y0;
        int x1 = (x0 + 1) % w;
        x0 = (x0 % w + w) % w;
        int y1 = Math.Min(h - 1, Math.Max(0, y0 + 1));
        y0 = Math.Min(h - 1, Math.Max(0, y0));
        int a = (y0 * w + x0) * 4, b = (y0 * w + x1) * 4, c = (y1 * w + x0) * 4, d = (y1 * w + x1) * 4;
        float L(int k) => ((buf[a + k] * (1f - tx) + buf[b + k] * tx) * (1f - ty) + (buf[c + k] * (1f - tx) + buf[d + k] * tx) * ty) / 255f;
        alpha = L(3);
        return new C3(L(0), L(1), L(2));
    }

    /// <summary>
    /// ミップマップ（1/2ずつ縮めた画像の列。[0]が元画像）。回転する球の縁や小さく表示したときのちらつきを防ぐ。
    /// Unity の Texture2D と同じ段数（どちらの辺も 1 になるまで）を作る。
    /// </summary>
    public static byte[][] BuildMips(byte[] level0, int width, int height)
    {
        int count = 1;
        for (int w = width, h = height; w > 1 || h > 1; count++)
        {
            w = Math.Max(1, w / 2);
            h = Math.Max(1, h / 2);
        }
        var levels = new byte[count][];
        levels[0] = level0;
        int cw = width, ch = height;
        for (int l = 1; l < count; l++)
        {
            int nw = Math.Max(1, cw / 2), nh = Math.Max(1, ch / 2);
            var src = levels[l - 1];
            var dst = new byte[nw * nh * 4];
            for (int y = 0; y < nh; y++)
            {
                int y0 = Math.Min(ch - 1, y * 2) * cw, y1 = Math.Min(ch - 1, y * 2 + 1) * cw;
                for (int x = 0; x < nw; x++)
                {
                    int x0 = Math.Min(cw - 1, x * 2), x1 = Math.Min(cw - 1, x * 2 + 1);
                    int a = (y0 + x0) * 4, b = (y0 + x1) * 4, c = (y1 + x0) * 4, d = (y1 + x1) * 4;
                    int o = (y * nw + x) * 4;
                    for (int k = 0; k < 4; k++)
                        dst[o + k] = (byte)((src[a + k] + src[b + k] + src[c + k] + src[d + k] + 2) >> 2);
                }
            }
            levels[l] = dst;
            cw = nw;
            ch = nh;
        }
        return levels;
    }

    // ================================================================
    // 走査
    // ================================================================
    private delegate void PixelFn(float x, float y, float z, float lat, int row, int index);

    /// <summary>
    /// 全画素について、球面上の単位ベクトル（x=右, y=北, z=経度0の正面）と緯度を渡して塗らせる。
    /// 行ごとに並列化する（メインスレッドの描画を邪魔しないよう、コアは2つ残す）。
    /// </summary>
    private static void Fill(int w, int h, PixelFn fn)
    {
        var sinLon = new float[w];
        var cosLon = new float[w];
        for (int x = 0; x < w; x++)
        {
            float lon = (x + 0.5f) / w * MathF.PI * 2f;
            sinLon[x] = MathF.Sin(lon);
            cosLon[x] = MathF.Cos(lon);
        }
        var options = new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 2) };
        Parallel.For(0, h, options, row =>
        {
            float lat = ((row + 0.5f) / h - 0.5f) * MathF.PI;
            float cl = MathF.Cos(lat), sl = MathF.Sin(lat);
            for (int x = 0; x < w; x++)
                fn(cl * sinLon[x], sl, cl * cosLon[x], lat, row, (row * w + x) * 4);
        });
    }

    private static void Put(byte[] buf, int i, C3 c, float a)
    {
        buf[i] = ToByte(c.R);
        buf[i + 1] = ToByte(c.G);
        buf[i + 2] = ToByte(c.B);
        buf[i + 3] = ToByte(a);
    }

    // ================================================================
    // 惑星ごとの塗り方
    // ================================================================
    private struct Px
    {
        public C3 Surface;
        public C3 Glow;
        public float GlowA;
    }

    private abstract class Painter
    {
        /// <summary>地図の1画素が球面上で占める角度（ラジアン。細い線のぼかし幅に使う）</summary>
        public float PixelAngle;
        public virtual bool HasGlow => false;
        public virtual bool HasClouds => false;
        public abstract void Paint(float x, float y, float z, float lat, ref Px px);
        public virtual float Cloud(float x, float y, float z, float lat) => 0f;
        /// <summary>全画素を塗った後の描き足し（星・稲妻など）</summary>
        public virtual void Post(Maps maps) { }
    }

    private static Painter CreatePainter(string kind)
    {
        switch (kind)
        {
            case "aquaris": return new Aquaris();
            case "verdia": return new Verdia();
            case "ignis": return new Ignis();
            case "glacia": return new Glacia();
            case "tempesta": return new Tempesta();
            case "orbis": return new Orbis();
            case "mistia": return new Mistia();
            case "rubra": return new Rubra();
            case "lunaris": return new Lunaris();
            case "crysta": return new Crysta();
            case "noctis": return new Noctis();
            case "soleil": return new Soleil();
            case "noir": return new Noir();
            case "mocha": return new Mocha();
            case "hexia": return new Hexia();
            case "opalia": return new Opalia();
            case "coralia": return new Coralia();
            case "celesta": return new Celesta();
            case "voltia": return new Voltia();
            case "galaxia": return new Galaxia();
            case "pastelia": return new Pastelia();
            case "neonia": return new Neonia();
            case "limona": return new Limona();
            case "legacia": return new Legacia();
            case "folia": return new Folia();
            case "vespera": return new Vespera();
            case "ambaria": return new Ambaria();
            case "lanterna": return new Lanterna();
            default: return new Aquaris();
        }
    }

    /// <summary>海と雲の惑星: 青い海・緑の島・白い雲・極冠</summary>
    private sealed class Aquaris : Painter
    {
        private readonly Noise _n = new Noise(101), _w = new Noise(102), _m = new Noise(103), _c = new Noise(104);
        public override bool HasClouds => true;

        public override void Paint(float x, float y, float z, float lat, ref Px px)
        {
            float wx = _w.Fbm(x * 1.6f + 5.2f, y * 1.6f + 1.3f, z * 1.6f + 2.8f, 3);
            float wy = _w.Fbm(x * 1.6f + 1.7f, y * 1.6f + 9.2f, z * 1.6f + 3.1f, 3);
            float wz = _w.Fbm(x * 1.6f + 8.3f, y * 1.6f + 2.8f, z * 1.6f + 4.4f, 3);
            float h = _n.Fbm(x * 1.9f + wx * 0.9f, y * 1.9f + wy * 0.9f, z * 1.9f + wz * 0.9f, 6);
            const float sea = 0.07f;
            C3 c;
            if (h < sea)
            {
                float depth = sea - h;
                c = C3.Lerp(C3.Hex(0x2C9BE0), C3.Hex(0x0A2A6E), Smooth(0f, 0.28f, depth));
                c = C3.Lerp(c, C3.Hex(0x45E3D6), Smooth(0.035f, 0f, depth) * 0.75f); // 浅瀬
            }
            else
            {
                float e = h - sea;
                float m = _m.Fbm(x * 3.1f, y * 3.1f, z * 3.1f, 3);
                C3 green = C3.Lerp(C3.Hex(0x57C46E), C3.Hex(0x2C8A52), Smooth(-0.1f, 0.15f, m));
                c = C3.Lerp(C3.Hex(0xEADBA8), green, Smooth(0.004f, 0.025f, e)); // 砂浜→緑
                c = C3.Lerp(c, C3.Hex(0x9C7E58), Smooth(0.16f, 0.24f, e));    // 高地
                c = C3.Lerp(c, C3.Hex(0xF2F6FF), Smooth(0.27f, 0.32f, e));    // 山頂の雪
            }
            float pole = MathF.Abs(lat) / (MathF.PI / 2f) + 0.06f * _m.At(x * 5f, y * 5f, z * 5f);
            c = C3.Lerp(c, C3.Hex(0xEEF6FF), Smooth(0.80f, 0.86f, pole));
            px.Surface = c;
        }

        public override float Cloud(float x, float y, float z, float lat)
        {
            // 緯度方向に少し引き伸ばした渦（貿易風の筋）＋大きな低気圧の渦
            float wx = _c.Fbm(x * 1.2f + 3.3f, y * 1.2f, z * 1.2f, 3);
            float wz = _c.Fbm(x * 1.2f, y * 1.2f + 7.7f, z * 1.2f + 1.1f, 3);
            float v = _c.Fbm(x * 2.4f + wx * 1.4f, y * 4.2f, z * 2.4f + wz * 1.4f, 5);
            float band = 0.10f * MathF.Cos(lat * 6f);
            return Smooth(0.02f, 0.30f, v + band) * 0.92f;
        }
    }

    /// <summary>発光する森の惑星: 深い緑の森・湖。夜側では森がほのかに光る</summary>
    private sealed class Verdia : Painter
    {
        private readonly Noise _n = new Noise(201), _d = new Noise(202), _g = new Noise(203), _c = new Noise(204);
        public override bool HasGlow => true;
        public override bool HasClouds => true;

        public override void Paint(float x, float y, float z, float lat, ref Px px)
        {
            float wx = _d.Fbm(x * 1.4f + 2.1f, y * 1.4f, z * 1.4f, 3);
            float wz = _d.Fbm(x * 1.4f, y * 1.4f + 4.4f, z * 1.4f + 6.6f, 3);
            float h = _n.Fbm(x * 2.2f + wx, y * 2.2f, z * 2.2f + wz, 6);
            float detail = _d.Fbm(x * 7f, y * 7f, z * 7f, 4);
            C3 c = C3.Lerp(C3.Hex(0x1C5E40), C3.Hex(0x3FBE70), Smooth(-0.2f, 0.25f, h + detail * 0.5f));
            c = C3.Lerp(c, C3.Hex(0xB4E266), Smooth(0.18f, 0.30f, h) * 0.8f); // 草原
            float lake = Smooth(-0.15f, -0.20f, h);
            c = C3.Lerp(c, C3.Lerp(C3.Hex(0x1E7584), C3.Hex(0x0E3A4E), Smooth(-0.2f, -0.35f, h)), lake);
            px.Surface = c;

            float spots = Smooth(0.18f, 0.42f, _g.Fbm(x * 13f, y * 13f, z * 13f, 3));
            px.Glow = C3.Lerp(C3.Hex(0x5CFFC4), C3.Hex(0xB6FF7A), Smooth(0.25f, 0.45f, detail));
            px.GlowA = spots * (1f - lake) * 0.95f;
        }

        public override float Cloud(float x, float y, float z, float lat)
        {
            float v = _c.Fbm(x * 2.0f, y * 3.4f, z * 2.0f, 5);
            return Smooth(0.10f, 0.40f, v) * 0.55f;
        }
    }

    /// <summary>溶岩の惑星: 黒い岩盤に赤〜橙の溶岩の割れ目と溶岩湖。割れ目は夜側で強く光る</summary>
    private sealed class Ignis : Painter
    {
        private readonly Noise _n = new Noise(301), _r = new Noise(302), _w = new Noise(303);
        public override bool HasGlow => true;

        public override void Paint(float x, float y, float z, float lat, ref Px px)
        {
            float wx = _w.Fbm(x * 2f + 1.1f, y * 2f, z * 2f, 3);
            float wy = _w.Fbm(x * 2f, y * 2f + 3.3f, z * 2f, 3);
            float wz = _w.Fbm(x * 2f, y * 2f, z * 2f + 5.5f, 3);
            float rock = _n.Fbm(x * 4f, y * 4f, z * 4f, 5);
            C3 crust = C3.Lerp(C3.Hex(0x140E10), C3.Hex(0x3E2824), Smooth(-0.25f, 0.3f, rock));

            // 割れ目: ノイズのゼロ交差（網目）を細い線にする。2スケール重ねる
            float a1 = MathF.Abs(_r.Fbm(x * 2.3f + wx * 0.7f, y * 2.3f + wy * 0.7f, z * 2.3f + wz * 0.7f, 4));
            float a2 = MathF.Abs(_r.Fbm(x * 6.1f + 9f, y * 6.1f, z * 6.1f + wx, 3));
            float crack = Math.Max(Smooth(0.055f, 0.0f, a1), Smooth(0.035f, 0.0f, a2) * 0.7f);
            // 溶岩湖: 表面が冷えて固まりかけたまだら模様にする（べた塗りの黄色い塊にならないように）
            float pool = Smooth(0.27f, 0.36f, _n.Fbm(x * 1.2f + 7f, y * 1.2f, z * 1.2f, 3));
            pool *= 0.45f + 0.55f * Smooth(-0.25f, 0.25f, _r.Fbm(x * 9f, y * 9f, z * 9f, 3));
            float lava = Math.Max(crack, pool * 0.9f);
            C3 hot = C3.Lerp(C3.Hex(0xB3200C), C3.Hex(0xFF6A1A), Smooth(0.2f, 0.8f, lava));
            hot = C3.Lerp(hot, C3.Hex(0xFFC64F), Smooth(0.9f, 1f, lava) * 0.6f);
            px.Surface = C3.Lerp(crust, hot, Clamp01(lava * 1.1f));
            px.Glow = hot;
            px.GlowA = Clamp01(MathF.Pow(lava, 1.3f) * 1.05f);
        }
    }

    /// <summary>氷の惑星: 白〜水色の氷床、青く光るクレバス、凍った海</summary>
    private sealed class Glacia : Painter
    {
        private readonly Noise _n = new Noise(401), _r = new Noise(402), _c = new Noise(403);
        public override bool HasGlow => true;
        public override bool HasClouds => true;

        public override void Paint(float x, float y, float z, float lat, ref Px px)
        {
            float b = _n.Fbm(x * 2.6f, y * 2.6f, z * 2.6f, 5);
            C3 c = C3.Lerp(C3.Hex(0xB7D3F0), C3.Hex(0xF4F9FF), Smooth(-0.25f, 0.25f, b));
            // 青い氷の帯（氷河の流れ）
            float flow = _c.Fbm(x * 1.6f + 2f, y * 4.0f, z * 1.6f, 4);
            c = C3.Lerp(c, C3.Hex(0x9CCBF2), Smooth(0.05f, 0.30f, flow) * 0.55f);
            float sea = Smooth(-0.12f, -0.22f, _n.Fbm(x * 1.3f + 4f, y * 1.3f, z * 1.3f, 4));
            c = C3.Lerp(c, C3.Hex(0x7FB2E3), sea * 0.85f);
            // クレバス: 一部の地域にだけ、太さに強弱のある割れ目を入れる（全面に細線が走ると地図の国境のように見える）
            float region = Smooth(-0.02f, 0.18f, _n.Fbm(x * 1.7f + 9f, y * 1.7f, z * 1.7f, 3));
            float a = MathF.Abs(_r.Fbm(x * 2.8f, y * 2.8f, z * 2.8f, 3));
            float width = 0.018f + 0.03f * Smooth(-0.2f, 0.3f, _r.At(x * 4f + 3f, y * 4f, z * 4f));
            float crev = Smooth(width, 0.0f, a) * region;
            c = C3.Lerp(c, C3.Hex(0x3F98DA), crev * 0.85f);
            px.Surface = c;
            px.Glow = C3.Hex(0x7FE6FF);
            px.GlowA = crev * 0.55f;
        }

        public override float Cloud(float x, float y, float z, float lat)
        {
            float v = _c.Fbm(x * 1.8f, y * 3.0f, z * 1.8f, 4);
            return Smooth(0.12f, 0.42f, v) * 0.45f;
        }
    }

    /// <summary>嵐の巨大ガス惑星: 緯度方向の縞＋乱流、南半球に大きな渦の嵐</summary>
    private sealed class Tempesta : Painter
    {
        private readonly Noise _n = new Noise(501), _f = new Noise(502);
        private static readonly float[] StopT =
            { -1.0f, -0.82f, -0.64f, -0.50f, -0.36f, -0.22f, -0.08f, 0.06f, 0.20f, 0.34f, 0.50f, 0.68f, 0.84f, 1.0f };
        private static readonly int[] StopC =
        {
            0xCDA987, 0xEBD7B6, 0xB7774B, 0xF4E7CF, 0xD39A66, 0xFFF3DE, 0xC6824F,
            0xF6E3C4, 0xA7683F, 0xEFD9B6, 0xC88B5B, 0xF3E3C8, 0xBA9674, 0xD9C5A8,
        };
        private readonly C3[] _stops;

        // 嵐の中心（緯度・経度）と大きさ（ラジアン）
        private const float STORM_LAT = -0.40f, STORM_LON = 1.1f, STORM_A = 0.30f, STORM_B = 0.17f;

        public Tempesta()
        {
            _stops = new C3[StopC.Length];
            for (int i = 0; i < StopC.Length; i++) _stops[i] = C3.Hex(StopC[i]);
        }

        public override void Paint(float x, float y, float z, float lat, ref Px px)
        {
            float lon = MathF.Atan2(x, z);
            // 嵐: 中心からの楕円距離で渦を巻かせる（緯度・経度の局所座標を回してから縞を読む）
            float dLon = WrapPi(lon - STORM_LON) * MathF.Cos(STORM_LAT);
            float dLat = lat - STORM_LAT;
            float d = MathF.Sqrt((dLon / STORM_A) * (dLon / STORM_A) + (dLat / STORM_B) * (dLat / STORM_B));
            float swirl = d < 1.6f ? 2.6f * MathF.Pow(Clamp01(1f - d / 1.6f), 2f) : 0f;
            float sampleLat = lat;
            if (swirl > 0f)
            {
                float cs = MathF.Cos(swirl), sn = MathF.Sin(swirl);
                float rLat = dLon * sn * (STORM_B / STORM_A) + dLat * cs;
                sampleLat = STORM_LAT + rLat;
            }

            float t = sampleLat / (MathF.PI / 2f);
            float turb = _n.Fbm(x * 2.6f, y * 9f, z * 2.6f, 5) * 0.07f + _f.Fbm(x * 7f, y * 22f, z * 7f, 3) * 0.025f;
            float bt = t + turb;
            C3 c = Palette(bt);
            c *= 0.92f + 0.08f * MathF.Sin(bt * 140f + _f.At(x * 4f, y * 4f, z * 4f) * 5f);

            if (d < 1.15f)
            {
                // 青緑の渦（琥珀色の縞の中でよく目立つ）。渦の筋は回転させた座標のノイズで描く
                float k = Smooth(1.15f, 0.5f, d);
                float arms = _f.Fbm(dLon * 9f + swirl * 1.8f, dLat * 9f - swirl * 1.2f, d * 3f, 3);
                C3 storm = C3.Lerp(C3.Hex(0x8FE0DA), C3.Hex(0x2F8FA8), Smooth(1.0f, 0.15f, d));
                storm = C3.Lerp(storm, C3.Hex(0xE6FFF8), Smooth(0.05f, 0.35f, arms) * 0.45f);
                c = C3.Lerp(c, storm, k);
                c = C3.Lerp(c, C3.Hex(0xFFF1DE), Gauss(d, 1.02f, 0.08f) * 0.55f); // 嵐の縁
            }
            px.Surface = c;
        }

        private C3 Palette(float t)
        {
            if (t <= StopT[0]) return _stops[0];
            for (int i = 1; i < StopT.Length; i++)
            {
                if (t <= StopT[i])
                {
                    float k = (t - StopT[i - 1]) / (StopT[i] - StopT[i - 1]);
                    return C3.Lerp(_stops[i - 1], _stops[i], Smooth(0f, 1f, k));
                }
            }
            return _stops[_stops.Length - 1];
        }
    }

    /// <summary>環をもつ惑星: やわらかいパステルの縞（環は RingMap で別に描く）</summary>
    private sealed class Orbis : Painter
    {
        private readonly Noise _n = new Noise(601);

        public override void Paint(float x, float y, float z, float lat, ref Px px)
        {
            float t = lat / (MathF.PI / 2f) + _n.Fbm(x * 2.4f, y * 6f, z * 2.4f, 4) * 0.05f;
            float b1 = 0.5f + 0.5f * MathF.Sin(t * 11f + 0.6f);
            float b2 = 0.5f + 0.5f * MathF.Sin(t * 27f + 2.1f);
            C3 c = C3.Lerp(C3.Hex(0xF6C7A2), C3.Hex(0xFFF0DA), b1);
            c = C3.Lerp(c, C3.Hex(0xE9A2B3), Smooth(0.55f, 1f, b2) * 0.55f);
            c = C3.Lerp(c, C3.Hex(0xC4B2F0), Smooth(0.75f, 0.95f, MathF.Abs(t)) * 0.7f); // 極は藤色
            px.Surface = c;
        }
    }

    /// <summary>すみれ色の霧の惑星: 何重にもゆがめた渦の雲。紫〜桃〜ところどころ水色</summary>
    private sealed class Mistia : Painter
    {
        private readonly Noise _n = new Noise(701), _c = new Noise(702);
        public override bool HasClouds => true;

        public override void Paint(float x, float y, float z, float lat, ref Px px)
        {
            float qx = _n.Fbm(x * 1.5f + 0.0f, y * 1.5f + 0.0f, z * 1.5f + 0.0f, 3);
            float qy = _n.Fbm(x * 1.5f + 5.2f, y * 1.5f + 1.3f, z * 1.5f + 2.7f, 3);
            float qz = _n.Fbm(x * 1.5f + 1.7f, y * 1.5f + 9.2f, z * 1.5f + 4.1f, 3);
            float f = _n.Fbm(x * 1.8f + qx * 3.2f, y * 1.8f + qy * 3.2f, z * 1.8f + qz * 3.2f, 5);
            float q = MathF.Sqrt(qx * qx + qy * qy + qz * qz);
            C3 c = C3.Lerp(C3.Hex(0x1E0E46), C3.Hex(0x5A34C6), Smooth(-0.35f, 0.15f, f));
            c = C3.Lerp(c, C3.Hex(0xC452DC), Smooth(0.15f, 0.45f, q) * 0.7f);
            c = C3.Lerp(c, C3.Hex(0xFF9CD6), Smooth(0.10f, 0.32f, f) * 0.85f);
            c = C3.Lerp(c, C3.Hex(0xFFE4F6), Smooth(0.30f, 0.48f, f) * 0.7f);   // 明るい霧の芯
            c = C3.Lerp(c, C3.Hex(0x86E4FF), Smooth(0.28f, 0.45f, qy) * 0.45f);
            px.Surface = c;
        }

        public override float Cloud(float x, float y, float z, float lat)
        {
            float wx = _c.Fbm(x * 1.3f + 2f, y * 1.3f, z * 1.3f, 3);
            float v = _c.Fbm(x * 2.2f + wx * 2f, y * 2.6f, z * 2.2f - wx, 5);
            return Smooth(0.10f, 0.42f, v) * 0.50f;
        }
    }

    /// <summary>赤い砂の惑星: 風紋の砂丘・深い峡谷・小さな極冠</summary>
    private sealed class Rubra : Painter
    {
        private readonly Noise _n = new Noise(801), _r = new Noise(802), _d = new Noise(803);

        public override void Paint(float x, float y, float z, float lat, ref Px px)
        {
            float b = _n.Fbm(x * 2.4f, y * 2.4f, z * 2.4f, 5);
            C3 c = C3.Lerp(C3.Hex(0xA9402A), C3.Hex(0xE58A4E), Smooth(-0.3f, 0.3f, b));
            // 砂丘: ゆがめた縞（風紋）
            float warp = _d.Fbm(x * 3f, y * 3f, z * 3f, 3) * 6f;
            float dune = 0.5f + 0.5f * MathF.Sin((x * 0.6f + y * 0.8f + z * 0.2f) * 38f + warp);
            float field = Smooth(-0.05f, 0.2f, _d.Fbm(x * 1.5f + 3f, y * 1.5f, z * 1.5f, 3));
            c = C3.Lerp(c, C3.Hex(0xF7B66C), MathF.Pow(dune, 3f) * field * 0.55f);
            // 峡谷
            float a = MathF.Abs(_r.Fbm(x * 1.9f + b, y * 1.9f, z * 1.9f - b, 4));
            float canyon = Smooth(0.05f, 0.0f, a);
            c = C3.Lerp(c, C3.Hex(0x5A2016), canyon * 0.85f);
            c = C3.Lerp(c, C3.Hex(0xF09A60), Smooth(0.07f, 0.05f, a) * (1f - canyon) * 0.35f); // 峡谷の縁
            float pole = MathF.Abs(lat) / (MathF.PI / 2f) + 0.05f * b;
            c = C3.Lerp(c, C3.Hex(0xF7EEE8), Smooth(0.87f, 0.91f, pole));
            px.Surface = c;
        }
    }

    /// <summary>クレーターの月: 灰色の地表・暗い「海」・大小のクレーター（明るい縁と暗い底）</summary>
    private sealed class Lunaris : Painter
    {
        private readonly Noise _n = new Noise(901);
        private readonly Crater[] _craters;

        private struct Crater
        {
            public float X, Y, Z, CosR, R;
            public float Depth;
        }

        public Lunaris()
        {
            var rnd = new Random(77);
            const int count = 170;
            _craters = new Crater[count];
            for (int i = 0; i < count; i++)
            {
                // 球面上の一様な点
                float u = (float)rnd.NextDouble() * 2f - 1f;
                float th = (float)rnd.NextDouble() * MathF.PI * 2f;
                float s = MathF.Sqrt(1f - u * u);
                float r = 0.03f + MathF.Pow((float)rnd.NextDouble(), 2.4f) * 0.22f; // 小さいものほど多い
                _craters[i] = new Crater
                {
                    X = s * MathF.Cos(th), Y = u, Z = s * MathF.Sin(th),
                    R = r, CosR = MathF.Cos(r * 1.35f),
                    Depth = 0.6f + 0.4f * (float)rnd.NextDouble(),
                };
            }
        }

        public override void Paint(float x, float y, float z, float lat, ref Px px)
        {
            float b = _n.Fbm(x * 3.2f, y * 3.2f, z * 3.2f, 5);
            C3 c = C3.Lerp(C3.Hex(0x7E8497), C3.Hex(0xCBD0DD), Smooth(-0.3f, 0.3f, b));
            float mare = Smooth(-0.06f, -0.16f, _n.Fbm(x * 1.1f + 6f, y * 1.1f, z * 1.1f, 4));
            c = C3.Lerp(c, C3.Hex(0x585E72), mare * 0.75f);

            float shade = 0f;
            for (int i = 0; i < _craters.Length; i++)
            {
                ref var k = ref _craters[i];
                float dot = x * k.X + y * k.Y + z * k.Z;
                if (dot < k.CosR) continue; // 影響範囲外（縁の外側 35% まで）
                float d = MathF.Acos(Math.Min(1f, dot)) / k.R; // 0=中心 1=縁
                float floor = d < 1f ? -0.20f * k.Depth * (1f - d * d * 0.6f) : 0f;
                float rim = 0.22f * k.Depth * Gauss(d, 1.0f, 0.12f);
                float ejecta = d > 1f ? 0.05f * k.Depth * Smooth(1.35f, 1.05f, d) : 0f;
                shade += floor + rim + ejecta;
            }
            c *= 1f + Math.Max(-0.5f, Math.Min(0.5f, shade));
            px.Surface = c;
        }
    }

    /// <summary>
    /// 結晶の惑星: 球面を大小の多角形の面（ボロノイ分割）に割った宝石のような地表。
    /// 面ごとに色・明るさ・照り返しの向きが違い、面の境目は白く光る線にして夜側でもきらめかせる。
    /// </summary>
    private sealed class Crysta : Painter
    {
        private const int CELLS = 96;
        private readonly float[] _cx = new float[CELLS], _cy = new float[CELLS], _cz = new float[CELLS];
        private readonly C3[] _col = new C3[CELLS];
        private readonly float[] _gx = new float[CELLS], _gy = new float[CELLS], _gz = new float[CELLS];
        private readonly bool[] _lit = new bool[CELLS];
        private readonly Noise _n = new Noise(1001);
        private static readonly int[] Colors = { 0xFF6FC8, 0xE65CB8, 0x9B7BFF, 0xCDB8FF, 0x66E0FF, 0xB8F2FF, 0xFFB3DE };
        public override bool HasGlow => true;

        public Crysta()
        {
            var rnd = new Random(31);
            float golden = MathF.PI * (3f - MathF.Sqrt(5f));
            for (int i = 0; i < CELLS; i++)
            {
                // 黄金角で均等に並べてから少し揺らす（大きさのそろいすぎない面にする）
                float yy = 1f - 2f * (i + 0.5f) / CELLS + ((float)rnd.NextDouble() - 0.5f) * 0.02f;
                yy = Math.Max(-1f, Math.Min(1f, yy));
                float r = MathF.Sqrt(1f - yy * yy);
                float th = i * golden + ((float)rnd.NextDouble() - 0.5f) * 0.5f;
                _cx[i] = r * MathF.Cos(th);
                _cy[i] = yy;
                _cz[i] = r * MathF.Sin(th);
                var c = C3.Hex(Colors[rnd.Next(Colors.Length)]);
                _col[i] = c * (0.72f + 0.28f * (float)rnd.NextDouble());
                // 面の照り返しの向き（面の中で明るさが傾く）
                _gx[i] = (float)rnd.NextDouble() * 2f - 1f;
                _gy[i] = (float)rnd.NextDouble() * 2f - 1f;
                _gz[i] = (float)rnd.NextDouble() * 2f - 1f;
                _lit[i] = rnd.NextDouble() < 0.14;
            }
        }

        public override void Paint(float x, float y, float z, float lat, ref Px px)
        {
            int best = 0, second = 0;
            float d1 = -2f, d2 = -2f;
            for (int i = 0; i < CELLS; i++)
            {
                float d = x * _cx[i] + y * _cy[i] + z * _cz[i];
                if (d > d1)
                {
                    d2 = d1;
                    second = best;
                    d1 = d;
                    best = i;
                }
                else if (d > d2)
                {
                    d2 = d;
                    second = i;
                }
            }
            // 境目（2点の垂直二等分線）までのおおよその距離。中心どうしが近くても線の太さが変わらないよう間隔で割る
            float sx = _cx[best] - _cx[second], sy = _cy[best] - _cy[second], sz = _cz[best] - _cz[second];
            float edge = (d1 - d2) / Math.Max(0.02f, MathF.Sqrt(sx * sx + sy * sy + sz * sz));

            float gx = x - _cx[best], gy = y - _cy[best], gz = z - _cz[best];
            float tiltLight = (gx * _gx[best] + gy * _gy[best] + gz * _gz[best]) * 1.8f;
            float grain = 0.96f + 0.04f * _n.At(x * 20f, y * 20f, z * 20f);
            C3 c = _col[best] * ((0.92f + Math.Max(-0.28f, Math.Min(0.4f, tiltLight))) * grain);
            c = C3.Lerp(c, C3.Hex(0xFFFFFF), Smooth(0.1f, 0.6f, tiltLight) * 0.45f); // 照り返し
            float line = Smooth(0.012f, 0.0f, edge);
            c = C3.Lerp(c, C3.Hex(0xFFF4FD), line * 0.9f);
            px.Surface = c;
            px.Glow = C3.Lerp(_col[best] * 1.4f, C3.Hex(0xFFFFFF), Math.Max(line, 0.35f));
            px.GlowA = Math.Max(line * 0.9f, _lit[best] ? 0.45f : 0f);
        }
    }

    /// <summary>夜景の惑星: 紺の海と暗い大陸。夜側に金色の街の灯りと、それをつなぐ道の光が広がる</summary>
    private sealed class Noctis : Painter
    {
        private readonly Noise _n = new Noise(1101), _w = new Noise(1102), _l = new Noise(1103), _c = new Noise(1104);
        public override bool HasGlow => true;
        public override bool HasClouds => true;

        public override void Paint(float x, float y, float z, float lat, ref Px px)
        {
            float wx = _w.Fbm(x * 1.5f, y * 1.5f + 2f, z * 1.5f, 3);
            float wz = _w.Fbm(x * 1.5f + 7f, y * 1.5f, z * 1.5f, 3);
            float h = _n.Fbm(x * 2.0f + wx, y * 2.0f, z * 2.0f + wz, 6);
            const float sea = 0.02f;
            C3 c;
            float land = Smooth(sea - 0.004f, sea + 0.006f, h);
            C3 ocean = C3.Lerp(C3.Hex(0x0E3566), C3.Hex(0x061533), Smooth(0f, 0.25f, sea - h));
            C3 ground = C3.Lerp(C3.Hex(0x3A4C5E), C3.Hex(0x26323F), Smooth(0f, 0.25f, h - sea));
            c = C3.Lerp(ocean, ground, land);
            px.Surface = c;

            // 街: 海岸に近いほど密。高周波ノイズの粒＋道（ゼロ交差の網目）
            float coast = Smooth(0.16f, 0.0f, h - sea);
            float dots = Smooth(0.12f, 0.36f, _l.Fbm(x * 22f, y * 22f, z * 22f, 3));
            float roads = Smooth(0.03f, 0.0f, MathF.Abs(_l.Fbm(x * 7f + 3f, y * 7f, z * 7f, 3)));
            float city = land * (dots * (0.35f + 0.65f * coast) + roads * 0.45f * (0.3f + 0.7f * coast));
            px.Glow = C3.Lerp(C3.Hex(0xFFB560), C3.Hex(0xFFE7A8), dots);
            px.GlowA = Clamp01(city * 1.15f);
        }

        public override float Cloud(float x, float y, float z, float lat)
        {
            float v = _c.Fbm(x * 2.2f, y * 3.6f, z * 2.2f, 5);
            return Smooth(0.14f, 0.40f, v) * 0.40f;
        }
    }

    /// <summary>小さな太陽: 自ら光る星（夜側なし）。細かな粒状の模様・明るい白斑・赤道寄りの黒点</summary>
    private sealed class Soleil : Painter
    {
        private readonly Noise _g = new Noise(1201), _s = new Noise(1202), _b = new Noise(1203);

        public override void Paint(float x, float y, float z, float lat, ref Px px)
        {
            // うねるプラズマ（ゆがめたノイズ）＋細かな粒＋赤道寄りの小さな黒点
            float qx = _b.Fbm(x * 1.8f, y * 1.8f, z * 1.8f, 3), qz = _b.Fbm(x * 1.8f + 5f, y * 1.8f, z * 1.8f, 3);
            float f = _b.Fbm(x * 3.0f + qx * 1.6f, y * 3.0f, z * 3.0f + qz * 1.6f, 5);
            C3 c = C3.Lerp(C3.Hex(0xFF6A14), C3.Hex(0xFFC548), Smooth(-0.3f, 0.25f, f));
            c = C3.Lerp(c, C3.Hex(0xFFF1C2), Smooth(0.15f, 0.42f, f) * 0.7f);
            c *= 0.92f + 0.12f * Smooth(-0.3f, 0.3f, _g.Fbm(x * 22f, y * 22f, z * 22f, 2));
            float sv = _s.Fbm(x * 2.8f + 5f, y * 2.8f, z * 2.8f, 3);
            float band = Smooth(0.85f, 0.35f, MathF.Abs(lat) / (MathF.PI / 2f));
            c = C3.Lerp(c, C3.Hex(0xC2511C), Smooth(0.31f, 0.37f, sv) * band * 0.85f); // 半暗部
            c = C3.Lerp(c, C3.Hex(0x5A1A06), Smooth(0.37f, 0.42f, sv) * band * 0.9f);  // 暗部
            px.Surface = c;
        }
    }

    /// <summary>漆黒の星: 光をほとんど返さない黒い球に、かすかな紫の筋（光る円盤は RingMap "noir"）</summary>
    private sealed class Noir : Painter
    {
        private readonly Noise _n = new Noise(1301), _w = new Noise(1302);

        public override void Paint(float x, float y, float z, float lat, ref Px px)
        {
            float wx = _w.Fbm(x * 1.5f, y * 1.5f + 3f, z * 1.5f, 3);
            float f = _n.Fbm(x * 2.2f + wx * 2f, y * 2.2f, z * 2.2f - wx, 5);
            C3 c = C3.Lerp(C3.Hex(0x030208), C3.Hex(0x170E2E), Smooth(-0.3f, 0.4f, f));
            float vein = Smooth(0.035f, 0.0f, MathF.Abs(_w.Fbm(x * 3.2f + f, y * 3.2f, z * 3.2f, 3)));
            px.Surface = C3.Lerp(c, C3.Hex(0x4B2C8E), vein * 0.55f);
        }
    }

    /// <summary>カフェラテの惑星: コーヒーにミルクを注いで混ぜたようなマーブルと、ラテアートのような白い渦</summary>
    private sealed class Mocha : Painter
    {
        private readonly Noise _n = new Noise(1401), _w = new Noise(1402);
        private readonly V3[] _center, _e1, _e2;

        public Mocha()
        {
            // 渦の中心（1つは最初の向きで正面に来る位置）
            _center = new[] { FromLatLon(0.25f, -1.0f), FromLatLon(-0.35f, 2.2f), FromLatLon(0.6f, 0.9f) };
            _e1 = new V3[_center.Length];
            _e2 = new V3[_center.Length];
            for (int k = 0; k < _center.Length; k++) TangentBasis(_center[k], out _e1[k], out _e2[k]);
        }

        public override void Paint(float x, float y, float z, float lat, ref Px px)
        {
            float qx = _w.Fbm(x * 1.4f, y * 1.4f, z * 1.4f, 3);
            float qy = _w.Fbm(x * 1.4f + 5.1f, y * 1.4f + 2.2f, z * 1.4f, 3);
            float qz = _w.Fbm(x * 1.4f, y * 1.4f + 7.7f, z * 1.4f + 3.3f, 3);
            float f = _n.Fbm(x * 1.7f + qx * 2.6f, y * 1.7f + qy * 2.6f, z * 1.7f + qz * 2.6f, 5);
            C3 c = C3.Lerp(C3.Hex(0x3A2014), C3.Hex(0x7A4C2E), Smooth(-0.35f, 0.15f, f));
            c = C3.Lerp(c, C3.Hex(0xB98A5C), Smooth(0.0f, 0.28f, f) * 0.85f);
            // 注いだミルクの細い筋
            float strands = 0.5f + 0.5f * MathF.Sin(f * 34f + qx * 5f);
            float foam = Smooth(-0.1f, 0.2f, _n.Fbm(x * 1.1f + 9f, y * 1.1f, z * 1.1f, 3));
            c = C3.Lerp(c, C3.Hex(0xF5E6D2), MathF.Pow(strands, 8f) * foam * 0.8f);
            // かき混ぜた白い渦巻き
            for (int k = 0; k < _center.Length; k++)
            {
                float d = x * _center[k].X + y * _center[k].Y + z * _center[k].Z;
                if (d < 0.80f) continue; // 中心から約37°以内だけ
                float ang = MathF.Acos(Math.Min(1f, d));
                float a = MathF.Atan2(x * _e2[k].X + y * _e2[k].Y + z * _e2[k].Z, x * _e1[k].X + y * _e1[k].Y + z * _e1[k].Z);
                float spiral = 0.5f + 0.5f * MathF.Sin(a * 2f + ang * 26f);
                c = C3.Lerp(c, C3.Hex(0xF7EADA), Smooth(0.55f, 0.9f, spiral) * Smooth(0.64f, 0.15f, ang) * 0.9f);
            }
            px.Surface = c;
        }
    }

    /// <summary>
    /// 六角形のマスの惑星: ボードゲームの盤のように、森・草原・小麦畑・丘・山・湖・砂地のマスがすき間をあけて並ぶ。
    /// （球面を均等な点で区切るので、ほとんどが六角形、ところどころ五角形のマスになる）
    /// </summary>
    private sealed class Hexia : Painter
    {
        private static readonly int[] Terrain = { 0x4E9E68, 0x9BD46E, 0xF0CE6A, 0xD9875A, 0xA6A3B8, 0x5BB5E6, 0xEBD3A0 };
        private readonly SphereCells _cells = SphereCells.Geodesic(4, 0.35f, 0.22f); // 162マス
        private readonly int[] _type;
        private readonly float[] _tone;
        private readonly Noise _n = new Noise(1501);

        public Hexia()
        {
            var rnd = new Random(1502);
            _type = new int[_cells.Count];
            _tone = new float[_cells.Count];
            for (int i = 0; i < _cells.Count; i++)
            {
                _type[i] = rnd.Next(Terrain.Length);
                _tone[i] = 0.93f + 0.13f * (float)rnd.NextDouble();
            }
        }

        public override void Paint(float x, float y, float z, float lat, ref Px px)
        {
            _cells.Nearest2(x, y, z, lat, out int best, out float edge);
            int type = _type[best];
            C3 c = C3.Hex(Terrain[type]) * _tone[best];
            float n = _n.At(x * 60f, y * 60f, z * 60f);
            switch (type)
            {
                case 0: c *= 0.82f + 0.28f * Smooth(0.0f, 0.3f, n); break;                          // 森: 木々のまだら
                case 2: c *= 0.90f + 0.12f * MathF.Sin((x * 0.7f + y * 0.2f + z * 0.7f) * 150f); break; // 小麦畑: 畝
                case 4: c = C3.Lerp(c, C3.Hex(0xF4F6FF), Smooth(0.15f, 0.3f, n)); break;            // 山: 雪
                case 5: c *= 0.94f + 0.08f * MathF.Sin((y * 0.9f + x * 0.3f) * 120f + n * 3f); break;  // 湖: さざなみ
                default: c *= 0.96f + 0.06f * n; break;
            }
            // マスの縁の段差（内側ほど明るい）と、マスどうしのすき間
            c *= 0.84f + 0.16f * Smooth(0.0f, 0.05f, edge);
            float gapW = Math.Max(0.010f, PixelAngle * 0.8f);
            c = C3.Lerp(c, C3.Hex(0x1C2442), Smooth(gapW + PixelAngle, gapW, edge));
            px.Surface = c;
        }
    }

    /// <summary>オパールの惑星: 乳白色の地に、場所によって色が変わる虹色のゆらめきと細かなきらめき</summary>
    private sealed class Opalia : Painter
    {
        private readonly Noise _n = new Noise(1601), _w = new Noise(1602), _s = new Noise(1603);

        public override void Paint(float x, float y, float z, float lat, ref Px px)
        {
            float qx = _w.Fbm(x * 1.3f, y * 1.3f, z * 1.3f, 3);
            float qy = _w.Fbm(x * 1.3f + 4.4f, y * 1.3f + 1.1f, z * 1.3f, 3);
            float qz = _w.Fbm(x * 1.3f + 2.2f, y * 1.3f, z * 1.3f + 6.6f, 3);
            float f = _n.Fbm(x * 2.0f + qx * 3f, y * 2.0f + qy * 3f, z * 2.0f + qz * 3f, 5);
            float g = _n.Fbm(x * 4.5f + 2f, y * 4.5f, z * 4.5f, 3);
            float hue = Frac(f * 1.8f + g * 0.5f + 0.58f);
            float sat = 0.25f + 0.30f * Smooth(0.0f, 0.35f, MathF.Abs(f + g * 0.5f));
            float play = Smooth(-0.1f, 0.25f, _s.Fbm(x * 2.6f + 7f, y * 2.6f, z * 2.6f, 4)); // 色が出る場所
            C3 c = C3.Lerp(C3.Hex(0xF2F5FF), Hsv(hue, sat, 0.98f), 0.25f + 0.55f * play);   // 乳白色の地を多めに
            float fleck = Smooth(0.36f, 0.46f, _s.Fbm(x * 26f, y * 26f, z * 26f, 2));
            px.Surface = C3.Lerp(c, C3.Hex(0xFFFFFF), fleck * 0.6f);
        }
    }

    /// <summary>サンゴ礁の海の惑星: 深い青〜透きとおった水色の浅瀬、ピンクや橙のサンゴ、白い砂の小島</summary>
    private sealed class Coralia : Painter
    {
        private readonly Noise _n = new Noise(1701), _w = new Noise(1702), _r = new Noise(1703), _c = new Noise(1704);
        public override bool HasClouds => true;

        public override void Paint(float x, float y, float z, float lat, ref Px px)
        {
            float wx = _w.Fbm(x * 1.6f, y * 1.6f, z * 1.6f + 2f, 3), wz = _w.Fbm(x * 1.6f + 5f, y * 1.6f, z * 1.6f, 3);
            float h = _n.Fbm(x * 2.2f + wx * 0.8f, y * 2.2f, z * 2.2f + wz * 0.8f, 6);
            C3 c = C3.Lerp(C3.Hex(0x0A4F86), C3.Hex(0x1BB8D0), Smooth(-0.30f, 0.06f, h));  // 深い海 → 浅瀬
            c = C3.Lerp(c, C3.Hex(0x7EEDE2), Smooth(0.06f, 0.14f, h) * 0.85f);               // 浅いラグーン
            float reef = Smooth(0.06f, 0.10f, h) * Smooth(0.18f, 0.14f, h);
            float rc = _r.Fbm(x * 14f, y * 14f, z * 14f, 3);
            C3 coral = C3.Lerp(C3.Hex(0xFF8FAB), C3.Hex(0xFFB06B), Smooth(-0.15f, 0.2f, rc));
            coral = C3.Lerp(coral, C3.Hex(0xC98BEA), Smooth(0.15f, 0.3f, _r.At(x * 9f + 3f, y * 9f, z * 9f)) * 0.7f);
            c = C3.Lerp(c, coral, reef * Smooth(-0.05f, 0.15f, rc) * 0.9f);
            c = C3.Lerp(c, C3.Hex(0xF8E9C2), Smooth(0.17f, 0.19f, h));                      // 白い砂の小島
            c = C3.Lerp(c, C3.Hex(0x6CCB7E), Smooth(0.23f, 0.26f, h));                      // 島のまん中の緑
            px.Surface = c;
        }

        public override float Cloud(float x, float y, float z, float lat) =>
            Smooth(0.22f, 0.42f, _c.Fbm(x * 4.2f, y * 4.2f, z * 4.2f, 4)) * 0.85f;
    }

    /// <summary>淡い青の巨大な氷の惑星: やわらかな縞と白い極・小さな白い嵐。大きく傾いた細い環（RingMap "celesta"）</summary>
    private sealed class Celesta : Painter
    {
        private readonly Noise _n = new Noise(1801);

        public override void Paint(float x, float y, float z, float lat, ref Px px)
        {
            float t = lat / (MathF.PI / 2f) + _n.Fbm(x * 2.2f, y * 6f, z * 2.2f, 4) * 0.035f;
            C3 c = C3.Lerp(C3.Hex(0x7FD3E3), C3.Hex(0xD2F7FB), 0.5f + 0.5f * MathF.Sin(t * 8f + 0.5f));
            c = C3.Lerp(c, C3.Hex(0x4FA9C6), Smooth(0.7f, 1f, 0.5f + 0.5f * MathF.Sin(t * 19f + 2f)) * 0.35f);
            c = C3.Lerp(c, C3.Hex(0xF2FEFF), Smooth(0.72f, 0.95f, MathF.Abs(t)));
            float spot = Smooth(0.33f, 0.40f, _n.Fbm(x * 3.4f + 11f, y * 3.4f, z * 3.4f, 3)) * Smooth(0.6f, 0.2f, MathF.Abs(t));
            px.Surface = C3.Lerp(c, C3.Hex(0xFFFFFF), spot * 0.7f);
        }
    }

    /// <summary>雷の惑星: 渦を巻く濃紺の雷雲。夜側で稲妻が光る（明滅は SpacePlanetView が付ける）</summary>
    private sealed class Voltia : Painter
    {
        private readonly Noise _n = new Noise(1901), _w = new Noise(1902);
        public override bool HasGlow => true;

        public override void Paint(float x, float y, float z, float lat, ref Px px)
        {
            float qx = _w.Fbm(x * 1.3f, y * 1.3f, z * 1.3f, 3), qz = _w.Fbm(x * 1.3f + 6f, y * 1.3f, z * 1.3f, 3);
            float f = _n.Fbm(x * 2.2f + qx * 2f, y * 2.6f, z * 2.2f + qz * 2f, 6);
            C3 c = C3.Lerp(C3.Hex(0x1C2340), C3.Hex(0x44527A), Smooth(-0.3f, 0.3f, f));
            px.Surface = C3.Lerp(c, C3.Hex(0x8FA3CC), Smooth(0.15f, 0.4f, f) * 0.7f);
            // 雷雲の中のぼんやりした明るさ（稲妻の下地）
            px.Glow = C3.Hex(0x9FD8FF);
            px.GlowA = Smooth(0.25f, 0.45f, f) * 0.18f;
        }

        public override void Post(Maps m)
        {
            // 稲妻: 球面上をジグザグに進む線（ときどき枝分かれ）を細い光の点で描く
            var rnd = new Random(1903);
            for (int b = 0; b < 60; b++)
            {
                var p = RandomDir(rnd);
                Bolt(m, p, RandomTangent(rnd, p), 22, 0.011f, rnd, 0);
            }
        }

        private void Bolt(Maps m, V3 p, V3 dir, int steps, float stepLen, Random rnd, int depth)
        {
            for (int s = 0; s < steps; s++)
            {
                dir = Rotate(dir, p, ((float)rnd.NextDouble() - 0.5f) * 1.2f);
                var next = Norm(p + dir * stepLen);
                int dots = Math.Max(2, (int)(stepLen / (PixelAngle * 0.5f)));
                for (int k = 0; k < dots; k++)
                {
                    var q = Norm(p + (next - p) * (k / (float)dots));
                    SplatGlow(m, q, PixelAngle * 3.5f, C3.Hex(0x7FCBFF), 0.35f);
                    SplatGlow(m, q, PixelAngle * 0.9f, C3.Hex(0xE8FAFF), 1f);
                }
                if (depth < 1 && rnd.NextDouble() < 0.18)
                    Bolt(m, next, Rotate(dir, next, rnd.NextDouble() < 0.5 ? 0.7f : -0.7f), steps / 2, stepLen * 0.9f, rnd, depth + 1);
                p = next;
                dir = Norm(dir - p * Dot(dir, p)); // 接平面に戻す
            }
        }
    }

    /// <summary>星空を閉じこめた惑星: 紺〜すみれの地に桃色と水色の星雲、天の川のような帯と無数の星（星は昼側でも光る）</summary>
    private sealed class Galaxia : Painter
    {
        private readonly Noise _n = new Noise(2001), _w = new Noise(2002), _b = new Noise(2003);
        private static readonly V3 BandNormal = Norm(new V3(0.35f, 0.85f, 0.40f));
        public override bool HasGlow => true;

        public override void Paint(float x, float y, float z, float lat, ref Px px)
        {
            float qx = _w.Fbm(x * 1.2f, y * 1.2f, z * 1.2f, 3), qy = _w.Fbm(x * 1.2f + 3f, y * 1.2f, z * 1.2f, 3);
            float n1 = _n.Fbm(x * 2.0f + qx * 2.2f, y * 2.0f + qy * 2.2f, z * 2.0f, 5);
            float n2 = _n.Fbm(x * 2.6f + 7f + qy, y * 2.6f, z * 2.6f + qx, 4);
            float band = Gauss(x * BandNormal.X + y * BandNormal.Y + z * BandNormal.Z, 0f, 0.22f)
                         * (0.55f + 0.45f * Smooth(-0.2f, 0.3f, n2));
            C3 c = C3.Lerp(C3.Hex(0x060519), C3.Hex(0x1B1045), Smooth(-0.3f, 0.3f, _b.Fbm(x * 1.5f, y * 1.5f, z * 1.5f, 4)));
            float pink = Smooth(0.08f, 0.38f, n1), cyan = Smooth(0.12f, 0.42f, n2);
            c = Add(c, C3.Hex(0xC94FC2) * (pink * 0.55f));
            c = Add(c, C3.Hex(0x3FA8FF) * (cyan * 0.45f));
            c = Add(c, C3.Hex(0x8E7BFF) * (band * 0.45f));
            px.Surface = c;
            // 夜側でも星雲がほのかに光る
            px.Glow = C3.Lerp(C3.Hex(0x8E7BFF), C3.Hex(0xE07AE0), pink);
            px.GlowA = Clamp01(pink * 0.35f + cyan * 0.25f + band * 0.3f);
        }

        public override void Post(Maps m)
        {
            var rnd = new Random(2004);
            for (int i = 0; i < 1100; i++)
            {
                var p = RandomDir(rnd);
                // 天の川の帯の近くほど星が多い
                float bandK = Gauss(Dot(p, BandNormal), 0f, 0.3f);
                if (rnd.NextDouble() > 0.35 + 0.65 * bandK) continue;
                float size = 0.0022f + MathF.Pow((float)rnd.NextDouble(), 4f) * 0.006f;
                double t = rnd.NextDouble();
                var col = t < 0.1 ? C3.Hex(0xFFE6A8) : t < 0.3 ? C3.Hex(0xA8C8FF) : C3.Hex(0xFFFFFF);
                SplatGlow(m, p, size, col, 0.6f + 0.4f * (float)rnd.NextDouble());
            }
        }
    }

    /// <summary>わたあめの惑星: 空色の地に、桃色・藤色・白のふわふわした雲のかたまり</summary>
    private sealed class Pastelia : Painter
    {
        private readonly Noise _n = new Noise(2101), _w = new Noise(2102), _c = new Noise(2103);
        public override bool HasClouds => true;

        public override void Paint(float x, float y, float z, float lat, ref Px px)
        {
            float wx = _w.Fbm(x * 1.3f, y * 1.3f, z * 1.3f, 3);
            C3 c = C3.Lerp(C3.Hex(0x93CCFF), C3.Hex(0xCBE7FF), Smooth(-0.3f, 0.3f, _w.Fbm(x * 1.5f + 3f, y * 1.5f, z * 1.5f, 4)));
            float b1 = Smooth(0.02f, 0.20f, _n.Fbm(x * 2.4f + wx, y * 2.4f, z * 2.4f, 5));
            float b2 = Smooth(0.08f, 0.26f, _n.Fbm(x * 3.1f + 5f, y * 3.1f + wx, z * 3.1f, 5));
            float b3 = Smooth(0.16f, 0.30f, _n.Fbm(x * 4.0f + 9f, y * 4.0f, z * 4.0f + wx, 4));
            c = C3.Lerp(c, C3.Hex(0xFFC0DF), b1 * 0.9f);
            c = C3.Lerp(c, C3.Hex(0xD9C9FF), b2 * 0.85f);
            px.Surface = C3.Lerp(c, C3.Hex(0xFFF7FC), b3 * 0.9f);
        }

        public override float Cloud(float x, float y, float z, float lat) =>
            Smooth(0.18f, 0.40f, _c.Fbm(x * 3.3f, y * 3.3f, z * 3.3f, 5)) * 0.6f;
    }

    /// <summary>
    /// ネオンの惑星: 暗い地に、水色と桃色の光の回路が走る（昼側でもほのかに光る）。
    /// 球を立方体に見立てて面ごとのマス目に回路を引く。隣のマスと共有する辺ごとに「つなぐ/つながない」を決めるので線が途切れない。
    /// </summary>
    private sealed class Neonia : Painter
    {
        private const int N = 10; // 立方体の1面あたりのマス数
        private readonly Noise _n = new Noise(2201);
        public override bool HasGlow => true;

        public override void Paint(float x, float y, float z, float lat, ref Px px)
        {
            float ax = MathF.Abs(x), ay = MathF.Abs(y), az = MathF.Abs(z);
            int face;
            float u, v;
            if (ax >= ay && ax >= az) { face = x > 0f ? 0 : 1; u = z / ax; v = y / ax; }
            else if (ay >= az) { face = y > 0f ? 2 : 3; u = x / ay; v = z / ay; }
            else { face = z > 0f ? 4 : 5; u = x / az; v = y / az; }
            float gu = (u + 1f) * 0.5f * N, gv = (v + 1f) * 0.5f * N;
            int iu = Math.Min(N - 1, (int)gu), iv = Math.Min(N - 1, (int)gv);
            float fu = gu - iu - 0.5f, fv = gv - iv - 0.5f;
            float aa = Math.Max(0.02f, PixelAngle * N * 2f / MathF.PI); // 1画素ぶんの幅（マス単位）
            const float w = 0.075f;

            bool right = Edge(face, iu + 1, iv, 0), left = Edge(face, iu, iv, 0);
            bool top = Edge(face, iu, iv + 1, 1), bottom = Edge(face, iu, iv, 1);
            float barH = Clamp01((w - MathF.Abs(fv)) / aa + 0.5f), barV = Clamp01((w - MathF.Abs(fu)) / aa + 0.5f);
            float line = 0f;
            if (right) line = Math.Max(line, barH * Clamp01((fu + w) / aa + 0.5f));
            if (left) line = Math.Max(line, barH * Clamp01((w - fu) / aa + 0.5f));
            if (top) line = Math.Max(line, barV * Clamp01((fv + w) / aa + 0.5f));
            if (bottom) line = Math.Max(line, barV * Clamp01((w - fv) / aa + 0.5f));
            int links = (right ? 1 : 0) + (left ? 1 : 0) + (top ? 1 : 0) + (bottom ? 1 : 0);
            float r = MathF.Sqrt(fu * fu + fv * fv);
            if (links == 1) line = Math.Max(line, Clamp01((0.06f - MathF.Abs(r - 0.17f)) / aa + 0.5f)); // 端子の丸
            if (links >= 3) line = Math.Max(line, Clamp01((0.13f - r) / aa + 0.5f));                    // 分岐の点

            uint h = Mix((uint)(face * 7919 + iu * 104729 + iv * 1299709 + 17));
            C3 neon = links == 1 ? C3.Hex(0xFFE45C) : (h % 3 == 0 ? C3.Hex(0xFF4FD8) : C3.Hex(0x3FF0FF));
            float gridLine = Math.Max(Clamp01((0.012f - MathF.Abs(fu - 0.5f)) / aa + 0.5f), Clamp01((0.012f - MathF.Abs(fv - 0.5f)) / aa + 0.5f));
            C3 c = C3.Lerp(C3.Hex(0x070A1E), C3.Hex(0x121838), Smooth(-0.3f, 0.3f, _n.Fbm(x * 3f, y * 3f, z * 3f, 3)));
            c = C3.Lerp(c, C3.Hex(0x1E2A5C), gridLine * 0.6f);
            px.Surface = C3.Lerp(c, neon * 0.6f, line * 0.85f);
            px.Glow = neon;
            px.GlowA = line;
        }

        /// <summary>マスの辺に回路を通すか（同じ辺は隣のマスからも同じ答えになる）。面の外周は通さない</summary>
        private static bool Edge(int face, int i, int j, int dir)
        {
            if (dir == 0 ? (i <= 0 || i >= N) : (j <= 0 || j >= N)) return false;
            return Mix((uint)(face * 92821 + i * 68917 + j * 1513 + dir * 7 + 3)) % 100 < 40;
        }
    }

    /// <summary>レモン色の惑星: 黄色〜黄緑のやわらかな縞と明るい渦、白い雲の筋</summary>
    private sealed class Limona : Painter
    {
        private readonly Noise _n = new Noise(2301), _w = new Noise(2302), _c = new Noise(2303);
        public override bool HasClouds => true;

        public override void Paint(float x, float y, float z, float lat, ref Px px)
        {
            float qx = _w.Fbm(x * 1.2f, y * 1.2f, z * 1.2f, 3), qz = _w.Fbm(x * 1.2f + 4f, y * 1.2f, z * 1.2f, 3);
            float t = lat / (MathF.PI / 2f) + _n.Fbm(x * 2f + qx, y * 5f, z * 2f + qz, 4) * 0.08f;
            C3 c = C3.Lerp(C3.Hex(0xFFF0A0), C3.Hex(0xE6F27E), 0.5f + 0.5f * MathF.Sin(t * 10f));
            c = C3.Lerp(c, C3.Hex(0xBFE36A), Smooth(0.65f, 0.95f, 0.5f + 0.5f * MathF.Sin(t * 23f + 1f)) * 0.5f);
            px.Surface = C3.Lerp(c, C3.Hex(0xFFFBE2),
                Smooth(0.10f, 0.35f, _n.Fbm(x * 3f + qx * 2f, y * 3f, z * 3f + qz * 2f, 4)) * 0.5f);
        }

        public override float Cloud(float x, float y, float z, float lat) =>
            Smooth(0.15f, 0.40f, _c.Fbm(x * 2.2f, y * 7f, z * 2.2f, 5)) * 0.6f;
    }

    /// <summary>
    /// 古参の人の惑星（レガシア）: 瑠璃（ラピスラズリ）のような深い青に、白い大理石の筋が走る。
    /// そこに白銀に光る古代文字の帯が何本も巻きつく（2026-11-07 ユーザー指定「青と白を基調に高級感」。最初は金色だった）。
    /// 帯は2本の細い線のあいだに、マスごとに形の違う文字（縦棒・横棒・点・ななめ）を刻む。
    /// 夜側では帯と、散らばる白銀の粒が光る（GlowBase で昼側もほんのり光る）
    /// </summary>
    private sealed class Legacia : Painter
    {
        private static readonly float[] Bands = { -0.92f, -0.42f, 0.10f, 0.62f }; // 帯の中心の緯度（ラジアン）
        private const float HALF = 0.055f;  // 帯の半分の幅（ラジアン）
        private const int CELLS = 40;       // 1周あたりの文字の数
        private readonly Noise _n = new Noise(2401), _w = new Noise(2402), _g = new Noise(2403);
        public override bool HasGlow => true;

        public override void Paint(float x, float y, float z, float lat, ref Px px)
        {
            // 地: 瑠璃色のまだら（濃紺〜ロイヤルブルー）＋細かな粒
            float qx = _w.Fbm(x * 1.6f, y * 1.6f, z * 1.6f, 3), qz = _w.Fbm(x * 1.6f + 3f, y * 1.6f, z * 1.6f, 3);
            float f = _n.Fbm(x * 2.4f + qx * 1.5f, y * 2.4f, z * 2.4f + qz * 1.5f, 5);
            C3 c = C3.Lerp(C3.Hex(0x0B1A52), C3.Hex(0x2650B8), Smooth(-0.35f, 0.45f, f));
            c = C3.Lerp(c, C3.Hex(0x4C7FE0), Smooth(0.30f, 0.60f, f) * 0.45f);
            c *= 0.92f + 0.10f * _n.At(x * 40f, y * 40f, z * 40f);
            // 白い大理石の筋（ゆがめたノイズの 0 付近を細い線にする。太い筋と細い筋の2種類）
            float v1 = MathF.Abs(_w.Fbm(x * 1.1f + qx * 0.9f + 5f, y * 1.1f + qz * 0.6f, z * 1.1f, 3));
            float v2 = MathF.Abs(_n.Fbm(x * 2.6f + qz * 0.8f + 9f, y * 2.6f, z * 2.6f + qx * 0.8f, 3));
            float vein = Math.Max(Smooth(0.030f, 0.0f, v1), Smooth(0.012f, 0.0f, v2) * 0.35f);
            vein = Math.Max(vein, Smooth(0.12f, 0.0f, v1) * 0.25f); // 筋のまわりのにじみ
            c = C3.Lerp(c, C3.Hex(0xEEF4FF), vein);

            float aa = Math.Max(0.004f, PixelAngle);
            float lon = MathF.Atan2(x, z);
            float cl = Math.Max(0.15f, MathF.Cos(lat));
            float cellAngle = MathF.PI * 2f / CELLS;
            float line = 0f;
            for (int b = 0; b < Bands.Length; b++)
            {
                float dv = lat - Bands[b];
                if (MathF.Abs(dv) > HALF + 0.02f) continue;
                // 帯のふちの2本の線
                float edge = MathF.Abs(MathF.Abs(dv) - HALF);
                line = Math.Max(line, Clamp01((0.006f - edge) / aa + 0.5f));
                if (MathF.Abs(dv) > HALF - 0.012f) continue;

                // 文字のマス（帯ごとに少しずらす）
                float g = (lon / cellAngle) + b * 0.37f;
                int cell = (int)MathF.Floor(g);
                float du = (g - cell - 0.5f) * cellAngle * cl; // マスの中心からの横の距離（ラジアン）
                uint h = Mix((uint)(b * 7919 + (cell + 1000) * 104729 + 31));
                float v = dv / (HALF - 0.012f);                 // -1〜1（帯の中での高さ）
                float halfW = cellAngle * cl * 0.30f;           // 文字の半分の幅
                float w = 0.0045f;
                float glyph = 0f;
                if ((h & 1) != 0) glyph = Math.Max(glyph, Clamp01((w - MathF.Abs(du)) / aa + 0.5f) * Clamp01((0.75f - MathF.Abs(v)) * 8f));
                if ((h & 2) != 0) glyph = Math.Max(glyph, Clamp01((w - MathF.Abs(dv)) / aa + 0.5f) * Clamp01((halfW - MathF.Abs(du)) / aa + 0.5f));
                if ((h & 4) != 0)
                {
                    float d = MathF.Sqrt(du * du + (dv - (HALF - 0.012f) * 0.55f) * (dv - (HALF - 0.012f) * 0.55f));
                    glyph = Math.Max(glyph, Clamp01((0.008f - d) / aa + 0.5f));
                }
                if ((h & 8) != 0)
                {
                    float diag = MathF.Abs(du - dv * 0.6f) / 1.17f;
                    glyph = Math.Max(glyph, Clamp01((w - diag) / aa + 0.5f) * Clamp01((0.7f - MathF.Abs(v)) * 8f));
                }
                if ((h & 15) == 0) // 空のマスは丸い印
                {
                    float r = MathF.Sqrt(du * du + dv * dv);
                    glyph = Math.Max(glyph, Clamp01((w - MathF.Abs(r - 0.016f)) / aa + 0.5f));
                }
                line = Math.Max(line, glyph);
                // 帯の地は深い紺に（白銀の文字が浮かぶように）
                c = C3.Lerp(c, C3.Hex(0x081340), 0.7f);
            }

            // 散らばる白銀の粒（星のきらめき）
            float dust = Smooth(0.47f, 0.54f, _g.Fbm(x * 30f, y * 30f, z * 30f, 2)) * 0.7f;

            float lit = Math.Max(line, dust * 0.7f);
            px.Surface = C3.Lerp(c, C3.Hex(0xF4F8FF), lit * 0.9f);
            px.Glow = C3.Hex(0xD8E8FF);
            px.GlowA = Math.Max(line, dust * 0.5f);
        }
    }

    // ---------------------------------------------------------------- 2026-11-10 追加（4つとも赤＋黄）
    /// <summary>
    /// 紅葉の惑星（フォリア）: 森がまだらに紅・朱・橙・金に色づく。谷は暗い赤茶の筋、木々の粒で細かくざわめく。
    /// 青・緑は使わない（色分けは赤＋黄）。うすい雲が少しだけ流れる
    /// </summary>
    private sealed class Folia : Painter
    {
        private readonly Noise _n = new Noise(2501), _w = new Noise(2502), _l = new Noise(2503), _c = new Noise(2504);
        public override bool HasClouds => true;

        public override void Paint(float x, float y, float z, float lat, ref Px px)
        {
            float wx = _w.Fbm(x * 1.6f, y * 1.6f + 2f, z * 1.6f, 3);
            float wz = _w.Fbm(x * 1.6f + 5f, y * 1.6f, z * 1.6f, 3);
            // 大きな色のまだら（紅 → 朱 → 橙 → 金）
            float h = _n.Fbm(x * 2.2f + wx * 0.9f, y * 2.2f, z * 2.2f + wz * 0.9f, 5);
            float t = Clamp01(0.42f + h * 1.5f);
            C3 c = t < 0.34f ? C3.Lerp(C3.Hex(0x9E1B22), C3.Hex(0xD8352A), Smooth(0f, 0.34f, t))
                : t < 0.67f ? C3.Lerp(C3.Hex(0xD8352A), C3.Hex(0xF06A22), Smooth(0.34f, 0.67f, t))
                : C3.Lerp(C3.Hex(0xF06A22), C3.Hex(0xF7AE35), Smooth(0.67f, 1f, t));
            c = C3.Lerp(c, C3.Hex(0xFFCB4A), Smooth(0.86f, 1f, t) * 0.6f); // ところどころ明るい金
            // 木々の粒（葉のざわめき）: 明るさを細かく揺らす
            float leaf = _l.Fbm(x * 26f, y * 26f, z * 26f, 3);
            c *= 0.9f + 0.16f * Smooth(-0.3f, 0.3f, leaf);
            // 谷（ノイズのゼロ交差）を暗い赤茶の筋に
            float valley = Smooth(0.045f, 0f, MathF.Abs(_w.Fbm(x * 3.1f + 1f, y * 3.1f, z * 3.1f, 4)));
            c = C3.Lerp(c, C3.Hex(0x5C1A12), valley * 0.6f);
            // 極は金色がかった明るい落ち葉色
            float pole = MathF.Abs(lat) / (MathF.PI / 2f) + 0.06f * h;
            c = C3.Lerp(c, C3.Hex(0xFFE3A3), Smooth(0.86f, 0.93f, pole) * 0.8f);
            px.Surface = c;
        }

        public override float Cloud(float x, float y, float z, float lat) =>
            Smooth(0.22f, 0.45f, _c.Fbm(x * 2.4f, y * 3.2f, z * 2.4f, 5)) * 0.32f;
    }

    /// <summary>
    /// 夕焼けの惑星（ヴェスペラ）: 赤道の金色から、琥珀・橙・珊瑚色を経て、極の深い紅へと移る縞。
    /// 縞は風で細かく波打ち、ところどころに金〜橙の渦が浮かぶ。色分けは赤＋黄
    /// </summary>
    private sealed class Vespera : Painter
    {
        private readonly Noise _n = new Noise(2601), _f = new Noise(2602);
        // 赤道（0）→ 極（1）
        private static readonly float[] StopT = { 0f, 0.16f, 0.34f, 0.52f, 0.70f, 0.86f, 1f };
        private static readonly int[] StopC = { 0xFFE08A, 0xFFC25A, 0xF89A3E, 0xEE6A3E, 0xD9434A, 0xA9213C, 0x7A1530 };
        private readonly C3[] _stops;
        // 渦（緯度・経度・横と縦の大きさ。ラジアン）
        private static readonly (float lat, float lon, float a, float b)[] Ovals =
        {
            (0.30f, 0.6f, 0.21f, 0.11f), (-0.46f, 2.4f, 0.16f, 0.09f), (0.62f, -1.9f, 0.13f, 0.07f),
            (-0.14f, -0.8f, 0.12f, 0.065f),
        };

        public Vespera()
        {
            _stops = new C3[StopC.Length];
            for (int i = 0; i < StopC.Length; i++) _stops[i] = C3.Hex(StopC[i]);
        }

        public override void Paint(float x, float y, float z, float lat, ref Px px)
        {
            float lon = MathF.Atan2(x, z);
            float t = MathF.Abs(lat) / (MathF.PI / 2f);
            float turb = _n.Fbm(x * 2.4f, y * 8f, z * 2.4f, 5) * 0.06f + _f.Fbm(x * 6f, y * 20f, z * 6f, 3) * 0.02f;
            float bt = Clamp01(t + turb);
            C3 c = Palette(bt);
            c *= 0.93f + 0.07f * MathF.Sin(lat * 60f + _f.At(x * 3f, y * 3f, z * 3f) * 4f);
            foreach (var o in Ovals)
            {
                float dLon = WrapPi(lon - o.lon) * MathF.Cos(o.lat);
                float dLat = lat - o.lat;
                float d = MathF.Sqrt((dLon / o.a) * (dLon / o.a) + (dLat / o.b) * (dLat / o.b));
                if (d >= 1.2f) continue;
                C3 oval = C3.Lerp(C3.Hex(0xFFD27A), C3.Hex(0xF2703A), Smooth(0f, 1f, d));
                c = C3.Lerp(c, oval, Smooth(1.2f, 0.4f, d) * 0.85f);
                c = C3.Lerp(c, C3.Hex(0xFFF0C8), Gauss(d, 1.0f, 0.07f) * 0.4f); // 渦の縁
            }
            px.Surface = c;
        }

        private C3 Palette(float t)
        {
            for (int i = 1; i < StopT.Length; i++)
            {
                if (t <= StopT[i])
                {
                    float k = (t - StopT[i - 1]) / (StopT[i] - StopT[i - 1]);
                    return C3.Lerp(_stops[i - 1], _stops[i], Smooth(0f, 1f, k));
                }
            }
            return _stops[_stops.Length - 1];
        }
    }

    /// <summary>
    /// 琥珀の惑星（アンバリア）: とろりと流れた跡のある蜂蜜色〜濃い琥珀色。ところどころに暗い内包物があり、
    /// 金色の光の粒が閉じこめられている。夜側ではその粒がほのかに光る（GlowBase で昼もかすかに）。色分けは赤＋黄
    /// </summary>
    private sealed class Ambaria : Painter
    {
        private readonly Noise _n = new Noise(2701), _w = new Noise(2702), _s = new Noise(2703);
        public override bool HasGlow => true;

        public override void Paint(float x, float y, float z, float lat, ref Px px)
        {
            float qx = _w.Fbm(x * 1.3f, y * 1.3f, z * 1.3f, 4), qz = _w.Fbm(x * 1.3f + 6f, y * 1.3f, z * 1.3f, 4);
            // 流れた跡: ゆがめた座標の縞
            float flow = 0.5f + 0.5f * MathF.Sin((y * 2.2f + qx * 3.2f + qz * 1.6f) * 6.5f);
            float depth = _n.Fbm(x * 2f + qx, y * 2f, z * 2f + qz, 5);
            C3 c = C3.Lerp(C3.Hex(0xB04E0A), C3.Hex(0xF59A22), Smooth(-0.35f, 0.35f, depth));
            c = C3.Lerp(c, C3.Hex(0xFFD36A), Smooth(0.5f, 1f, flow) * 0.55f);
            // 内包物（暗いしみ）
            float inc = Smooth(0.34f, 0.46f, _s.Fbm(x * 5f + 3f, y * 5f, z * 5f, 3));
            c = C3.Lerp(c, C3.Hex(0x6A2C06), inc * 0.45f);
            // 光の粒（細かいノイズの山の頂だけ）
            float spark = Smooth(0.40f, 0.54f, _s.Fbm(x * 40f, y * 40f, z * 40f, 2));
            c = C3.Lerp(c, C3.Hex(0xFFF0B0), spark * 0.8f);
            px.Surface = c;
            px.Glow = C3.Hex(0xFFD77A);
            px.GlowA = Clamp01(spark * 1.2f);
        }
    }

    /// <summary>
    /// 提灯の惑星（ランテルナ）: 朱色〜深い紅の大地に、金色の細い曲線の模様（蒔絵のよう）。
    /// 夜側では、道に沿って点々と連なる提灯の灯りと、集まった町の灯りが金色にともる（GlowBase で昼もかすかに）。色分けは赤＋黄
    /// </summary>
    private sealed class Lanterna : Painter
    {
        private readonly Noise _n = new Noise(2801), _w = new Noise(2802), _l = new Noise(2803), _g = new Noise(2804);
        public override bool HasGlow => true;

        public override void Paint(float x, float y, float z, float lat, ref Px px)
        {
            float wx = _w.Fbm(x * 1.4f, y * 1.4f + 2f, z * 1.4f, 3), wz = _w.Fbm(x * 1.4f + 7f, y * 1.4f, z * 1.4f, 3);
            float h = _n.Fbm(x * 2.0f + wx, y * 2.0f, z * 2.0f + wz, 5);
            C3 c = C3.Lerp(C3.Hex(0x7A1424), C3.Hex(0xD3362E), Smooth(-0.3f, 0.35f, h));
            c = C3.Lerp(c, C3.Hex(0xE5553A), Smooth(0.25f, 0.5f, h) * 0.5f);
            // 金の模様: ゼロ交差の細い曲線
            float gold = Smooth(0.03f, 0f, MathF.Abs(_g.Fbm(x * 3.4f + wx, y * 3.4f, z * 3.4f + wz, 3)));
            c = C3.Lerp(c, C3.Hex(0xF5C65A), gold * 0.85f);
            px.Surface = c;
            // 灯り: 道（ゼロ交差の網目）に沿って間をあけて並ぶ提灯＋町の灯りの粒
            float road = Smooth(0.03f, 0f, MathF.Abs(_l.Fbm(x * 5f + 2f, y * 5f, z * 5f, 3)));
            float beads = Smooth(0.35f, 0.75f, 0.5f + 0.5f * MathF.Sin((x * 31f + y * 37f + z * 29f) * 2.2f));
            float town = Smooth(0.20f, 0.42f, _l.Fbm(x * 20f, y * 20f, z * 20f, 3)) * Smooth(0.05f, 0.35f, h);
            float light = Math.Max(road * beads, town);
            px.Glow = C3.Lerp(C3.Hex(0xFF9A3C), C3.Hex(0xFFD98A), Smooth(0f, 1f, town));
            px.GlowA = Clamp01(light * 1.2f);
        }
    }

    // ================================================================
    // 点・線を描き足す道具（星・稲妻）と、球面のマス分け
    // ================================================================
    /// <summary>
    /// 球面上の点 p を中心に、角度半径 radius のぼかした光の点を Glow に描く（明るいほうを残す）。
    /// 地図の該当範囲（緯度・経度の箱）だけを走査するので、点がたくさんあっても速い。
    /// </summary>
    private static void SplatGlow(Maps m, V3 p, float radius, C3 color, float intensity)
    {
        if (m.Glow == null) return;
        int w = m.Width, h = m.Height;
        float pix = MathF.PI / h;
        float r = Math.Max(radius, pix * 0.7f);
        float ext = r * 2.6f;
        float lat0 = MathF.Asin(Math.Max(-1f, Math.Min(1f, p.Y)));
        float lon0 = MathF.Atan2(p.X, p.Z);
        if (lon0 < 0f) lon0 += MathF.PI * 2f;
        int y0 = Math.Max(0, (int)MathF.Floor(((lat0 - ext) / MathF.PI + 0.5f) * h));
        int y1 = Math.Min(h - 1, (int)MathF.Ceiling(((lat0 + ext) / MathF.PI + 0.5f) * h));
        bool nearPole = MathF.Abs(lat0) + ext > MathF.PI / 2f - 0.02f;
        float lonExt = nearPole ? MathF.PI : Math.Min(MathF.PI, ext / Math.Max(0.05f, MathF.Cos(lat0)));
        int x0 = (int)MathF.Floor((lon0 - lonExt) / (MathF.PI * 2f) * w);
        int x1 = (int)MathF.Ceiling((lon0 + lonExt) / (MathF.PI * 2f) * w);
        if (x1 - x0 >= w) { x0 = 0; x1 = w - 1; }
        var buf = m.Glow;
        for (int y = y0; y <= y1; y++)
        {
            float lat = ((y + 0.5f) / h - 0.5f) * MathF.PI;
            float cl = MathF.Cos(lat), sl = MathF.Sin(lat);
            for (int xi = x0; xi <= x1; xi++)
            {
                int x = ((xi % w) + w) % w;
                float lon = (x + 0.5f) / w * MathF.PI * 2f;
                float dot = cl * MathF.Sin(lon) * p.X + sl * p.Y + cl * MathF.Cos(lon) * p.Z;
                float d2 = Math.Max(0f, 2f - 2f * dot); // 弦の長さの2乗（小さい角度なら角度の2乗）
                float a = intensity * MathF.Exp(-d2 / (r * r));
                if (a < 0.004f) continue;
                int i = (y * w + x) * 4;
                if (a * 255f <= buf[i + 3]) continue;
                buf[i] = ToByte(color.R);
                buf[i + 1] = ToByte(color.G);
                buf[i + 2] = ToByte(color.B);
                buf[i + 3] = ToByte(a);
            }
        }
    }

    /// <summary>
    /// 球面を点の集まりで区切る（ボロノイ分割）。
    /// Geodesic は正二十面体を細分した点（六角形のマス＋五角形12個のきれいな六角盤）。
    /// 緯度・経度の粗い格子ごとに「近くの点」の候補を持っておき、1画素あたり十数個だけ調べる。
    /// </summary>
    private sealed class SphereCells
    {
        private const int GW = 48, GH = 24;
        public readonly int Count;
        private readonly float[] _x, _y, _z;
        private readonly int[][] _cand;

        /// <summary>正二十面体の各面を freq 分割した頂点（10×freq²+2 個）。rotY/rotX で全体を少し回す</summary>
        public static SphereCells Geodesic(int freq, float rotY, float rotX)
        {
            float t = (1f + MathF.Sqrt(5f)) / 2f;
            var v = new[]
            {
                new V3(-1, t, 0), new V3(1, t, 0), new V3(-1, -t, 0), new V3(1, -t, 0),
                new V3(0, -1, t), new V3(0, 1, t), new V3(0, -1, -t), new V3(0, 1, -t),
                new V3(t, 0, -1), new V3(t, 0, 1), new V3(-t, 0, -1), new V3(-t, 0, 1),
            };
            int[] f =
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
            };
            float cy = MathF.Cos(rotY), sy = MathF.Sin(rotY), cx = MathF.Cos(rotX), sx = MathF.Sin(rotX);
            var seen = new System.Collections.Generic.Dictionary<long, V3>();
            for (int k = 0; k < f.Length; k += 3)
            {
                V3 a = v[f[k]], b = v[f[k + 1]], c = v[f[k + 2]];
                for (int i = 0; i <= freq; i++)
                for (int j = 0; i + j <= freq; j++)
                {
                    var p = Norm(a + (b - a) * (i / (float)freq) + (c - a) * (j / (float)freq));
                    long key = ((long)MathF.Round(p.X * 4096f) + 8192) * 16777216L
                               + ((long)MathF.Round(p.Y * 4096f) + 8192) * 16384L
                               + ((long)MathF.Round(p.Z * 4096f) + 8192);
                    if (seen.ContainsKey(key)) continue;
                    // 少し回して、五角形のマスが正面にそろって来ないようにする
                    var q = new V3(p.X * cy + p.Z * sy, p.Y, -p.X * sy + p.Z * cy);
                    seen[key] = new V3(q.X, q.Y * cx - q.Z * sx, q.Y * sx + q.Z * cx);
                }
            }
            var pts = new V3[seen.Count];
            seen.Values.CopyTo(pts, 0);
            return new SphereCells(pts);
        }

        private SphereCells(V3[] pts)
        {
            int count = pts.Length;
            Count = count;
            _x = new float[count];
            _y = new float[count];
            _z = new float[count];
            for (int i = 0; i < count; i++)
            {
                _x[i] = pts[i].X;
                _y[i] = pts[i].Y;
                _z[i] = pts[i].Z;
            }
            // 1つのマスの半径の約2.6倍＋格子の半対角より内側の点を候補にする（2番目に近い点まで確実に入る）
            float reach = 2.6f * MathF.Sqrt(4f / count) + 0.1f;
            float cosReach = MathF.Cos(Math.Min(MathF.PI, reach));
            _cand = new int[GW * GH][];
            var list = new System.Collections.Generic.List<int>();
            for (int gy = 0; gy < GH; gy++)
            for (int gx = 0; gx < GW; gx++)
            {
                float lat = ((gy + 0.5f) / GH - 0.5f) * MathF.PI, lon = (gx + 0.5f) / GW * MathF.PI * 2f;
                float cx = MathF.Cos(lat) * MathF.Sin(lon), cy = MathF.Sin(lat), cz = MathF.Cos(lat) * MathF.Cos(lon);
                list.Clear();
                for (int i = 0; i < count; i++)
                    if (cx * _x[i] + cy * _y[i] + cz * _z[i] >= cosReach) list.Add(i);
                _cand[gy * GW + gx] = list.ToArray();
            }
        }

        /// <summary>いちばん近い点と、境目（2番目に近い点との垂直二等分線）までのおおよその距離（ラジアン）</summary>
        public void Nearest2(float x, float y, float z, float lat, out int best, out float edge)
        {
            float lon = MathF.Atan2(x, z);
            if (lon < 0f) lon += MathF.PI * 2f;
            int gx = Math.Min(GW - 1, (int)(lon / (MathF.PI * 2f) * GW));
            int gy = Math.Min(GH - 1, Math.Max(0, (int)((lat / MathF.PI + 0.5f) * GH)));
            var cand = _cand[gy * GW + gx];
            best = 0;
            int second = 0;
            float d1 = -2f, d2 = -2f;
            for (int k = 0; k < cand.Length; k++)
            {
                int i = cand[k];
                float d = x * _x[i] + y * _y[i] + z * _z[i];
                if (d > d1)
                {
                    d2 = d1;
                    second = best;
                    d1 = d;
                    best = i;
                }
                else if (d > d2)
                {
                    d2 = d;
                    second = i;
                }
            }
            float sx = _x[best] - _x[second], sy = _y[best] - _y[second], sz = _z[best] - _z[second];
            edge = (d1 - d2) / Math.Max(0.02f, MathF.Sqrt(sx * sx + sy * sy + sz * sz));
        }
    }

    // ---- 小さな3Dベクトル ----
    private struct V3
    {
        public float X, Y, Z;
        public V3(float x, float y, float z) { X = x; Y = y; Z = z; }
        public static V3 operator +(V3 a, V3 b) => new V3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static V3 operator -(V3 a, V3 b) => new V3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static V3 operator *(V3 a, float k) => new V3(a.X * k, a.Y * k, a.Z * k);
    }

    private static float Dot(V3 a, V3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    private static V3 Cross(V3 a, V3 b) =>
        new V3(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

    private static V3 Norm(V3 a)
    {
        float l = MathF.Sqrt(Dot(a, a));
        return l > 1e-6f ? a * (1f / l) : new V3(0f, 1f, 0f);
    }

    private static V3 FromLatLon(float lat, float lon) =>
        new V3(MathF.Cos(lat) * MathF.Sin(lon), MathF.Sin(lat), MathF.Cos(lat) * MathF.Cos(lon));

    /// <summary>p の接平面の正規直交基底</summary>
    private static void TangentBasis(V3 p, out V3 e1, out V3 e2)
    {
        var up = MathF.Abs(p.Y) < 0.9f ? new V3(0f, 1f, 0f) : new V3(1f, 0f, 0f);
        e1 = Norm(Cross(up, p));
        e2 = Cross(p, e1);
    }

    private static V3 RandomDir(Random rnd)
    {
        float u = (float)rnd.NextDouble() * 2f - 1f, th = (float)rnd.NextDouble() * MathF.PI * 2f;
        float s = MathF.Sqrt(1f - u * u);
        return new V3(s * MathF.Cos(th), u, s * MathF.Sin(th));
    }

    private static V3 RandomTangent(Random rnd, V3 p)
    {
        TangentBasis(p, out var e1, out var e2);
        float a = (float)rnd.NextDouble() * MathF.PI * 2f;
        return e1 * MathF.Cos(a) + e2 * MathF.Sin(a);
    }

    /// <summary>接ベクトル v を、p を軸に angle だけ回す（接平面内で向きを変える）</summary>
    private static V3 Rotate(V3 v, V3 p, float angle) =>
        Norm(v * MathF.Cos(angle) + Cross(p, v) * MathF.Sin(angle));

    // ================================================================
    // ノイズ（改良版パーリンノイズ。シードごとの置換表は読み取り専用なのでスレッド間で共有してよい）
    // ================================================================
    private sealed class Noise
    {
        private readonly int[] _p = new int[512];

        public Noise(int seed)
        {
            var perm = new int[256];
            for (int i = 0; i < 256; i++) perm[i] = i;
            var rnd = new Random(seed);
            for (int i = 255; i > 0; i--)
            {
                int j = rnd.Next(i + 1);
                int t = perm[i];
                perm[i] = perm[j];
                perm[j] = t;
            }
            for (int i = 0; i < 512; i++) _p[i] = perm[i & 255];
        }

        /// <summary>おおむね -1〜1</summary>
        public float At(float x, float y, float z)
        {
            int xi = Floor(x), yi = Floor(y), zi = Floor(z);
            x -= xi;
            y -= yi;
            z -= zi;
            xi &= 255;
            yi &= 255;
            zi &= 255;
            float u = Fade(x), v = Fade(y), w = Fade(z);
            var p = _p;
            int a = p[xi] + yi, aa = p[a] + zi, ab = p[a + 1] + zi;
            int b = p[xi + 1] + yi, ba = p[b] + zi, bb = p[b + 1] + zi;
            return L(w,
                L(v, L(u, G(p[aa], x, y, z), G(p[ba], x - 1, y, z)),
                     L(u, G(p[ab], x, y - 1, z), G(p[bb], x - 1, y - 1, z))),
                L(v, L(u, G(p[aa + 1], x, y, z - 1), G(p[ba + 1], x - 1, y, z - 1)),
                     L(u, G(p[ab + 1], x, y - 1, z - 1), G(p[bb + 1], x - 1, y - 1, z - 1))));
        }

        /// <summary>フラクタル（オクターブを重ねる）。おおむね -0.6〜0.6、中央付近に集まる</summary>
        public float Fbm(float x, float y, float z, int octaves)
        {
            float sum = 0f, amp = 1f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                sum += amp * At(x, y, z);
                norm += amp;
                amp *= 0.5f;
                x = x * 2.03f + 17.13f;
                y = y * 2.03f + 5.71f;
                z = z * 2.03f + 11.37f;
            }
            return sum / norm;
        }

        private static int Floor(float f)
        {
            int i = (int)f;
            return f < i ? i - 1 : i;
        }

        private static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);
        private static float L(float t, float a, float b) => a + t * (b - a);

        private static float G(int hash, float x, float y, float z)
        {
            int h = hash & 15;
            float u = h < 8 ? x : y;
            float v = h < 4 ? y : (h == 12 || h == 14 ? x : z);
            return ((h & 1) == 0 ? u : -u) + ((h & 2) == 0 ? v : -v);
        }
    }

    // ================================================================
    // 小物
    // ================================================================
    private struct C3
    {
        public float R, G, B;

        public C3(float r, float g, float b)
        {
            R = r;
            G = g;
            B = b;
        }

        public static C3 Hex(int rgb) =>
            new C3(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);

        public static C3 Lerp(C3 a, C3 b, float t) =>
            new C3(a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t);

        public static C3 operator *(C3 c, float k) => new C3(c.R * k, c.G * k, c.B * k);
    }

    /// <summary>0〜1 に収める（NaN は 0 にする。計算の端で NaN が混ざっても画素が壊れないように）</summary>
    private static float Clamp01(float v) => v > 0f ? (v < 1f ? v : 1f) : 0f;

    private static float Frac(float x) => x - MathF.Floor(x);

    /// <summary>色を足す（1で頭打ち）</summary>
    private static C3 Add(C3 a, C3 b) =>
        new C3(Math.Min(1f, a.R + b.R), Math.Min(1f, a.G + b.G), Math.Min(1f, a.B + b.B));

    /// <summary>HSV → RGB（h は 0〜1 で1周）</summary>
    private static C3 Hsv(float h, float s, float v)
    {
        h = Frac(h) * 6f;
        int i = (int)h;
        float f = h - i, p = v * (1f - s), q = v * (1f - s * f), t = v * (1f - s * (1f - f));
        switch (i)
        {
            case 0: return new C3(v, t, p);
            case 1: return new C3(q, v, p);
            case 2: return new C3(p, v, t);
            case 3: return new C3(p, q, v);
            case 4: return new C3(t, p, v);
            default: return new C3(v, p, q);
        }
    }

    /// <summary>smoothstep（e0 > e1 なら逆向き）</summary>
    private static float Smooth(float e0, float e1, float x)
    {
        float t = Clamp01((x - e0) / (e1 - e0));
        return t * t * (3f - 2f * t);
    }

    private static float Gauss(float x, float mu, float sigma)
    {
        float d = (x - mu) / sigma;
        return MathF.Exp(-d * d);
    }

    private static float WrapPi(float a)
    {
        while (a > MathF.PI) a -= MathF.PI * 2f;
        while (a < -MathF.PI) a += MathF.PI * 2f;
        return a;
    }

    private static uint Mix(uint h)
    {
        unchecked
        {
            h ^= h >> 16;
            h *= 0x7FEB352Du;
            h ^= h >> 15;
            h *= 0x846CA68Bu;
            h ^= h >> 16;
            return h;
        }
    }

    private static byte ToByte(float v) => (byte)(Clamp01(v) * 255f + 0.5f);
}
