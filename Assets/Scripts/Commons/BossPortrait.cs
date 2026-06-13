using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ボスモンスター（角のある魔獣）の立ち絵を実行時生成する（画像アセット不要）。
/// 角・顔のシルエット、光る眼、牙、まわりに揺らぐ魔法のオーラをプロシージャルに描く。
/// 属性（fire/water/nature/thunder）ごとに配色を変え、属性別にキャッシュする。
/// 模様は決め打ちシードで、起動ごとに変わらない。
/// </summary>
public static class BossPortrait
{
    private const int W = 512;
    private const int H = 560;

    private static readonly Dictionary<string, Sprite> _cache = new Dictionary<string, Sprite>();

    // 属性共通の色（角・眼の芯・牙）
    private static readonly Color HornDark = new Color(0.16f, 0.14f, 0.22f);
    private static readonly Color HornLite = new Color(0.93f, 0.89f, 0.72f);
    private static readonly Color EyeCore  = new Color(1.00f, 1.00f, 1.00f);
    private static readonly Color Fang     = new Color(0.97f, 0.97f, 0.93f);

    /// <summary>属性ごとの配色。</summary>
    private struct Palette
    {
        public Color BodyDark, BodyMid, BodyHot, EyeGlow, MouthIn, AuraIn, AuraOut, Rim;
    }

    private static Palette PaletteFor(string element)
    {
        switch (element)
        {
            case "fire": // 炎: 赤〜橙
                return new Palette {
                    BodyDark = new Color(0.16f, 0.03f, 0.02f), BodyMid = new Color(0.60f, 0.12f, 0.04f),
                    BodyHot = new Color(0.98f, 0.62f, 0.16f), EyeGlow = new Color(1.00f, 0.80f, 0.20f),
                    MouthIn = new Color(0.16f, 0.03f, 0.02f), AuraIn = new Color(1.00f, 0.55f, 0.15f),
                    AuraOut = new Color(0.45f, 0.05f, 0.03f), Rim = new Color(1.00f, 0.70f, 0.30f),
                };
            case "water": // 水: 青〜水色
                return new Palette {
                    BodyDark = new Color(0.03f, 0.07f, 0.18f), BodyMid = new Color(0.10f, 0.34f, 0.66f),
                    BodyHot = new Color(0.48f, 0.85f, 0.96f), EyeGlow = new Color(0.45f, 0.92f, 1.00f),
                    MouthIn = new Color(0.04f, 0.08f, 0.20f), AuraIn = new Color(0.58f, 0.92f, 1.00f),
                    AuraOut = new Color(0.10f, 0.20f, 0.55f), Rim = new Color(0.62f, 0.93f, 1.00f),
                };
            case "nature": // 自然: 緑
                return new Palette {
                    BodyDark = new Color(0.04f, 0.14f, 0.05f), BodyMid = new Color(0.15f, 0.45f, 0.16f),
                    BodyHot = new Color(0.62f, 0.90f, 0.32f), EyeGlow = new Color(0.78f, 1.00f, 0.38f),
                    MouthIn = new Color(0.05f, 0.12f, 0.04f), AuraIn = new Color(0.66f, 0.95f, 0.45f),
                    AuraOut = new Color(0.12f, 0.32f, 0.12f), Rim = new Color(0.76f, 1.00f, 0.50f),
                };
            case "thunder": // 雷: 黄（紫紺ベース）
                return new Palette {
                    BodyDark = new Color(0.12f, 0.08f, 0.20f), BodyMid = new Color(0.38f, 0.30f, 0.60f),
                    BodyHot = new Color(1.00f, 0.92f, 0.30f), EyeGlow = new Color(1.00f, 0.95f, 0.35f),
                    MouthIn = new Color(0.12f, 0.08f, 0.18f), AuraIn = new Color(1.00f, 0.95f, 0.45f),
                    AuraOut = new Color(0.30f, 0.22f, 0.55f), Rim = new Color(1.00f, 0.95f, 0.50f),
                };
            default:
                goto case "fire";
        }
    }

    // 顔の輪郭（高さt → 横半幅）。下が顎先、上が頭頂。区分線形。
    private static readonly Vector2[] FaceProfile =
    {
        new Vector2(0.27f, 0.00f), new Vector2(0.33f, 0.13f), new Vector2(0.40f, 0.21f),
        new Vector2(0.50f, 0.28f), new Vector2(0.59f, 0.305f), new Vector2(0.66f, 0.275f),
        new Vector2(0.71f, 0.20f), new Vector2(0.74f, 0.10f), new Vector2(0.76f, 0.00f),
    };

    // 角の中心線（片側・対称に使う）。base→tip。
    private static readonly Vector2[] Horn =
    {
        new Vector2(0.15f, 0.64f), new Vector2(0.23f, 0.75f), new Vector2(0.32f, 0.85f),
        new Vector2(0.41f, 0.93f), new Vector2(0.50f, 1.00f),
    };

    public static Sprite Get(string element)
    {
        if (string.IsNullOrEmpty(element)) element = "fire";
        if (_cache.TryGetValue(element, out var cached)) return cached;

        var pal = PaletteFor(element);
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        var px = new Color32[W * H];

        for (int y = 0; y < H; y++)
        {
            float t = y / (float)H;                 // 0=下, 1=上
            for (int x = 0; x < W; x++)
            {
                float u = x / (float)W - 0.5f;       // -0.5..0.5
                float au = Mathf.Abs(u);

                float scale = Fbm(u * 7f, t * 8f, 700);   // 鱗・陰影むら
                float fine  = Fbm(u * 24f, t * 26f, 900);

                // ---- 角 ----
                float hs;
                float hd = HornDist(au, t, out hs);
                float hornW = Mathf.Lerp(0.085f, 0.004f, hs);
                float horn = Smooth(hornW, hornW - 0.012f, hd);

                // ---- 顔 ----
                float hw = FaceHalfWidth(t);
                float face = hw > 0f ? Smooth(0.018f, 0f, au - hw) : 0f;

                float sil = Mathf.Max(face, horn);

                Color c; float a;
                if (sil > 0.01f)
                {
                    if (horn >= face)
                    {
                        // 角: 付け根は暗く、先端ほど骨色。内側にハイライト。
                        float lite = Mathf.Clamp01(hs * 0.9f + 0.15f + 0.25f * scale);
                        c = Color.Lerp(HornDark, HornLite, lite);
                        c = Color.Lerp(c, pal.Rim, Smooth(hornW, hornW - 0.05f, hd) * 0.25f);
                    }
                    else
                    {
                        // 顔: 上ほど明るく、下と輪郭は暗い。中央に陰影。
                        float heat = Mathf.Clamp01((t - 0.30f) / 0.40f);
                        float center = 1f - Mathf.Clamp01(au / Mathf.Max(hw, 0.001f)); // 中央=1
                        float shade = Mathf.Clamp01(heat * (0.45f + 0.55f * center) + 0.25f * scale - 0.12f);
                        c = Color.Lerp(pal.BodyDark, pal.BodyMid, Mathf.Clamp01(shade * 1.6f));
                        c = Color.Lerp(c, pal.BodyHot, Mathf.Clamp01(shade - 0.5f) * 1.4f);
                        // 鱗の細かな陰影
                        c = Color.Lerp(c, pal.BodyDark, Mathf.Clamp01(0.35f - fine * 0.5f) * 0.4f);
                        // 輪郭リム光（シルエット際）
                        float rim = Smooth(0.05f, 0.0f, hw - au) * Mathf.Clamp01(heat + 0.2f);
                        c = Color.Lerp(c, pal.Rim, rim * 0.5f);

                        // 眉のうっすらした陰（怒り顔にならない程度に控えめ）
                        float brow = Smooth(0.04f, 0.0f, Mathf.Abs(t - 0.64f))
                                     * Smooth(0.22f, 0.12f, au);
                        c = Color.Lerp(c, pal.BodyDark, brow * 0.25f);

                        // ---- 眼光 ----
                        float eye = EyeField(au, t);
                        if (eye > 0f)
                        {
                            c = Color.Lerp(c, pal.EyeGlow, Mathf.Clamp01(eye));
                            c = Color.Lerp(c, EyeCore, Mathf.Clamp01(eye - 0.55f) * 2.2f);
                        }

                        // ---- 口・牙 ----
                        float mouth, fang;
                        MouthField(au, t, out mouth, out fang);
                        if (mouth > 0f) c = Color.Lerp(c, pal.MouthIn, mouth);
                        if (fang > 0f)  c = Color.Lerp(c, Fang, fang);
                    }
                    a = sil;
                }
                else
                {
                    // ---- 魔法のオーラ（体の外側）----
                    float aura = AuraField(u, t, scale, fine);
                    c = Color.Lerp(pal.AuraOut, pal.AuraIn, Mathf.Clamp01(aura * 1.3f));
                    a = Mathf.Clamp01(aura) * 0.85f;
                }

                px[y * W + x] = new Color32(
                    (byte)(Mathf.Clamp01(c.r) * 255f),
                    (byte)(Mathf.Clamp01(c.g) * 255f),
                    (byte)(Mathf.Clamp01(c.b) * 255f),
                    (byte)(Mathf.Clamp01(a) * 255f));
            }
        }

        tex.SetPixels32(px);
        tex.Apply();
        tex.wrapMode = TextureWrapMode.Clamp;
        var sprite = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f));
        _cache[element] = sprite;
        return sprite;
    }

    // ================================================================
    // 形状フィールド
    // ================================================================
    private static float FaceHalfWidth(float t)
    {
        if (t < FaceProfile[0].x || t > FaceProfile[FaceProfile.Length - 1].x) return 0f;
        for (int i = 0; i < FaceProfile.Length - 1; i++)
        {
            var p = FaceProfile[i]; var q = FaceProfile[i + 1];
            if (t >= p.x && t <= q.x)
            {
                float f = Mathf.InverseLerp(p.x, q.x, t);
                return Mathf.Lerp(p.y, q.y, f);
            }
        }
        return 0f;
    }

    /// <summary>角中心線への最短距離と、その位置の媒介変数 hs(0=base,1=tip)。</summary>
    private static float HornDist(float au, float t, out float hs)
    {
        float best = float.MaxValue; hs = 0f;
        for (int i = 0; i < Horn.Length - 1; i++)
        {
            var p = Horn[i]; var q = Horn[i + 1];
            float s;
            float d = DistToSeg(au, t, p.x, p.y, q.x, q.y, out s);
            if (d < best)
            {
                best = d;
                hs = (i + s) / (Horn.Length - 1);
            }
        }
        return best;
    }

    private static float EyeField(float au, float t)
    {
        // 大きめのまるい目（傾けず、生き物らしい愛嬌を出す）
        float ex = (au - 0.135f) / 0.080f;
        float ey = (t - 0.545f) / 0.080f;
        float d = ex * ex + ey * ey;
        return d < 1f ? (1f - d) : 0f;
    }

    private static void MouthField(float au, float t, out float mouth, out float fang)
    {
        mouth = 0f; fang = 0f;
        // 小さめの口（怖くならない控えめなサイズ）
        float top = 0.44f, bot = 0.40f;
        if (au > 0.12f || t <= bot || t >= top) return;
        float halfW = 0.12f * Mathf.Clamp01((top - t) / (top - bot) + 0.20f);
        if (au >= halfW) return;
        mouth = Smooth(0.0f, 0.015f, halfW - au) * 0.85f;

        // 小さな牙を左右に1本ずつだけ、ちょこんと
        float fd = Mathf.Abs(au - 0.07f) / 0.022f;
        float drop = Smooth(0.0f, 0.02f, (top - t) - 0.012f);
        if (fd < 1f) fang = (1f - fd) * drop * mouth;
    }

    private static float AuraField(float u, float t, float scale, float fine)
    {
        // 顔・角の中心(0,0.58)からの距離ベースのオーラ。
        float dx = u;
        float dy = (t - 0.58f) * 0.85f;
        float d = Mathf.Sqrt(dx * dx + dy * dy);
        float core = Mathf.Clamp01(1f - d / 0.66f);
        if (core <= 0f) return 0f;
        // ふんわりした魔法の光輪（炎のような激しい揺らぎは抑える）
        float halo = 0.78f + 0.35f * scale;
        return Mathf.Clamp01(core * core * halo - 0.04f);
    }

    // ================================================================
    // ユーティリティ
    // ================================================================
    private static float Smooth(float edge0, float edge1, float x)
    {
        float f = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
        return f * f * (3f - 2f * f);
    }

    private static float DistToSeg(float px, float py, float ax, float ay, float bx, float by, out float s)
    {
        float vx = bx - ax, vy = by - ay;
        float wx = px - ax, wy = py - ay;
        float len2 = vx * vx + vy * vy;
        s = len2 > 0f ? Mathf.Clamp01((wx * vx + wy * vy) / len2) : 0f;
        float cx = ax + s * vx, cy = ay + s * vy;
        float dx = px - cx, dy = py - cy;
        return Mathf.Sqrt(dx * dx + dy * dy);
    }

    private static float Hash01(int x, int y, int seed)
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
