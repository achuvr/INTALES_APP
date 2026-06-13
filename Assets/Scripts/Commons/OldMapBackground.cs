using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 「古びた世界地図」風の背景テクスチャ（実行時生成・画像アセット不要）。
/// 紙の繊維ムラ・しみ・経緯線・コンパスローズ・焦げた縁をプロシージャルに描き、
/// 各ページの全画面背景（Image_Background）に貼って王道RPGの冒険感を出す。
/// 模様は決め打ちシードで生成するため、起動ごとに変わらない。
/// </summary>
public static class OldMapBackground
{
    private const int W = 384;
    private const int H = 696; // 表示先（約1240x2237）とほぼ同じ縦横比

    private static Sprite _sprite;

    public static void Apply(Image img)
    {
        img.sprite = Get();
        img.type = Image.Type.Simple;
        img.color = Color.white;
    }

    public static Sprite Get()
    {
        if (_sprite != null) return _sprite;

        // しみ（位置・大きさは決め打ちの疑似乱数。毎回同じ模様になる）
        var stains = new (float cx, float cy, float r, float strength)[14];
        for (int i = 0; i < stains.Length; i++)
        {
            stains[i] = (
                Hash01(i, 11) * W,
                Hash01(i, 23) * H,
                Mathf.Lerp(20f, 72f, Hash01(i, 37)),
                Mathf.Lerp(0.25f, 0.60f, Hash01(i, 53)));
        }

        // 文字の可読性を優先して全体は白寄りに。茶の要素も薄めにとどめる
        var ink   = new Color(0.50f, 0.38f, 0.24f); // 線・しみのセピア
        var burnt = new Color(0.62f, 0.50f, 0.34f); // 焦げた縁
        var baseA = new Color(0.99f, 0.97f, 0.90f); // 明るい紙（ほぼ白）
        var baseB = new Color(0.92f, 0.87f, 0.74f); // 暗い紙

        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        var px = new Color32[W * H];

        for (int y = 0; y < H; y++)
        {
            float ny = y / (float)H;
            for (int x = 0; x < W; x++)
            {
                float nx = x / (float)W;

                // 紙の繊維ムラ（多重ノイズ）
                float n = Fbm(nx * 6f, ny * 10.8f, 100);
                var c = Color.Lerp(baseA, baseB, n);

                // 経緯線（うっすらとした地図のグリッド）
                if (x % 64 < 2 || y % 64 < 2)
                    c = Color.Lerp(c, ink, 0.07f);

                // コンパスローズ（右上にうっすら）
                float compass = CompassRose(x, y);
                if (compass > 0f)
                    c = Color.Lerp(c, ink, compass);

                // しみ（輪郭はノイズで不規則に）
                float stain = 0f;
                foreach (var s in stains)
                {
                    float dx = x - s.cx, dy = y - s.cy;
                    float d2 = dx * dx + dy * dy;
                    if (d2 < s.r * s.r)
                    {
                        float f = 1f - Mathf.Sqrt(d2) / s.r;
                        stain += f * f * s.strength;
                    }
                }
                if (stain > 0f)
                {
                    stain *= 0.55f + 0.9f * Fbm(nx * 14f, ny * 25f, 200);
                    c = Color.Lerp(c, ink, Mathf.Clamp01(stain) * 0.20f);
                }

                // 焦げた縁（縁ほど濃い茶。ゆらぎ付き。縦横で見た目の太さが揃うよう補正）
                float edge = Mathf.Min(Mathf.Min(nx, 1f - nx), Mathf.Min(ny, 1f - ny) * 1.8f);
                float burn = 1f - Mathf.Clamp01(edge / 0.10f);
                if (burn > 0f)
                {
                    burn = burn * burn * (0.65f + 0.7f * Fbm(nx * 13f, ny * 23f, 300));
                    c = Color.Lerp(c, burnt, Mathf.Clamp01(burn) * 0.75f);
                }

                px[y * W + x] = new Color32(
                    (byte)(Mathf.Clamp01(c.r) * 255f),
                    (byte)(Mathf.Clamp01(c.g) * 255f),
                    (byte)(Mathf.Clamp01(c.b) * 255f),
                    255);
            }
        }

        tex.SetPixels32(px);
        tex.Apply();
        _sprite = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f));
        return _sprite;
    }

    /// <summary>コンパスローズ（外輪・内輪・8方位のスポーク）の描画強度</summary>
    private static float CompassRose(int x, int y)
    {
        const float cx = W * 0.74f, cy = H * 0.79f;
        float dx = x - cx, dy = y - cy;
        float d = Mathf.Sqrt(dx * dx + dy * dy);
        if (d > 62f) return 0f;

        float a = 0f;
        if (Mathf.Abs(d - 56f) < 1.4f || Mathf.Abs(d - 40f) < 1.1f)
            a = 0.09f;

        // 8方位のスポーク（中心は空ける）
        if (d > 7f && d < 56f)
        {
            float ang = Mathf.Repeat(Mathf.Atan2(dy, dx), Mathf.PI / 4f);
            float distToSpoke = Mathf.Min(ang, Mathf.PI / 4f - ang) * d; // 弧長近似
            if (distToSpoke < 1.2f)
                a = Mathf.Max(a, 0.08f);
        }
        return a;
    }

    // ================================================================
    // ノイズ（シード付き・決定的）
    // ================================================================
    private static float Hash01(int x, int y, int seed = 5)
    {
        unchecked
        {
            int h = x * 374761393 + y * 668265263 + seed * 1442695041;
            h = (h ^ (h >> 13)) * 1274126177;
            return ((h ^ (h >> 16)) & 0x7fffffff) / (float)0x7fffffff;
        }
    }

    private static float ValueNoise(float x, float y, int seed)
    {
        int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
        float xf = x - xi, yf = y - yi;
        float u = xf * xf * (3f - 2f * xf);
        float v = yf * yf * (3f - 2f * yf);
        float a = Hash01(xi, yi, seed);
        float b = Hash01(xi + 1, yi, seed);
        float c = Hash01(xi, yi + 1, seed);
        float d = Hash01(xi + 1, yi + 1, seed);
        return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v);
    }

    /// <summary>4オクターブの多重ノイズ（0〜1程度）</summary>
    private static float Fbm(float x, float y, int seed)
    {
        float sum = 0f, amp = 0.5f, freq = 1f;
        for (int i = 0; i < 4; i++)
        {
            sum += ValueNoise(x * freq, y * freq, seed + i) * amp;
            freq *= 2f;
            amp *= 0.5f;
        }
        return sum;
    }
}
