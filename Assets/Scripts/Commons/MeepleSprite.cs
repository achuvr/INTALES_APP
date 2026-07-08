using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// コード生成UI用のミープル（ボードゲームの人型コマ）シルエットスプライト（実行時生成）。
/// 形はカルカソンヌのミープルに合わせている：首なしで頭が肩に直結、太い腕がやや
/// 下がり気味に張り出し、胴は裾広がりで底面が平ら（外角は丸め）、脚の間は細いスリット。
/// 白のシルエットとして焼くので、色は Image.color で付ける（例: 赤いミープル）。
/// ボードゲーム一覧ボタンのアイコンに使う。
/// </summary>
public static class MeepleSprite
{
    private const int SIZE = 128;
    private const int SS = 3; // 3x3 スーパーサンプリングで輪郭を滑らかにする
    private static Sprite _sprite;

    public static Sprite Get()
    {
        if (_sprite != null) return _sprite;

        var tex = new Texture2D(SIZE, SIZE, TextureFormat.RGBA32, false);
        var px = new Color32[SIZE * SIZE];
        float step = 1f / (SIZE * SS);

        for (int y = 0; y < SIZE; y++)
        {
            for (int x = 0; x < SIZE; x++)
            {
                int hit = 0;
                for (int sy = 0; sy < SS; sy++)
                    for (int sx = 0; sx < SS; sx++)
                        if (Inside((x * SS + sx + 0.5f) * step, (y * SS + sy + 0.5f) * step))
                            hit++;
                byte a = (byte)(hit * 255 / (SS * SS));
                px[y * SIZE + x] = new Color32(255, 255, 255, a);
            }
        }
        tex.SetPixels32(px);
        tex.Apply();

        _sprite = Sprite.Create(tex, new Rect(0, 0, SIZE, SIZE), new Vector2(0.5f, 0.5f));
        return _sprite;
    }

    /// <summary>Imageをミープルのシルエットとして描画する（色は呼び出し側が Image.color で指定）。</summary>
    public static void Apply(Image img)
    {
        img.sprite = Get();
        img.type = Image.Type.Simple;
        img.preserveAspect = true;
    }

    /// <summary>
    /// ミープルの中身判定（u,v は 0〜1。v は上向き）。カルカソンヌのコマの形：
    /// 頭の円・胸ブロブ・下がり気味の太い腕・裾広がりの胴の合成から、
    /// 脚の間のスリットを除き、底面の外角を丸める。
    /// </summary>
    private static bool Inside(float u, float v)
    {
        // 脚の間の細いスリット（上端は丸い）
        if (InCapsule(u, v, 0.5f, -0.10f, 0.5f, 0.175f, 0.078f)) return false;
        // 底面の外角を丸める（角領域のうち丸め円の外側を削る）
        const float R = 0.055f, CX = 0.215f, CY = 0.115f;
        if (u < CX && v < CY && Dist2(u, v, CX, CY) > R * R) return false;
        if (u > 1f - CX && v < CY && Dist2(u, v, 1f - CX, CY) > R * R) return false;

        // 頭（首なしで肩に直結）
        if (Dist2(u, v, 0.5f, 0.795f) < 0.165f * 0.165f) return true;
        // 胸ブロブ（頭と腕・胴をつなぐ）
        if (Dist2(u, v, 0.5f, 0.60f) < 0.155f * 0.155f) return true;
        // 腕（太く、やや下がり気味に張り出す）
        if (InCapsule(u, v, 0.45f, 0.635f, 0.125f, 0.555f, 0.10f)) return true;
        if (InCapsule(u, v, 0.55f, 0.635f, 0.875f, 0.555f, 0.10f)) return true;
        // 胴（裾広がり・側面はわずかに凹カーブ、底面は平ら）
        if (v >= 0.06f && v <= 0.62f)
        {
            float t = (v - 0.06f) / 0.56f; // 0=裾, 1=肩
            float hw = 0.155f + (0.345f - 0.155f) * Mathf.Pow(1f - t, 1.5f); // 半幅
            if (Mathf.Abs(u - 0.5f) <= hw) return true;
        }
        return false;
    }

    /// <summary>カプセル（線分 (x0,y0)-(x1,y1) を太さ r で膨らませた形）の中身判定。</summary>
    private static bool InCapsule(float u, float v, float x0, float y0, float x1, float y1, float r)
    {
        float dx = x1 - x0, dy = y1 - y0;
        float len2 = dx * dx + dy * dy;
        float t = len2 <= 0f ? 0f : Mathf.Clamp01(((u - x0) * dx + (v - y0) * dy) / len2);
        float px = u - (x0 + t * dx), py = v - (y0 + t * dy);
        return px * px + py * py < r * r;
    }

    private static float Dist2(float u, float v, float cx, float cy)
    {
        float dx = u - cx, dy = v - cy;
        return dx * dx + dy * dy;
    }
}
