using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// コード生成UI用の角丸スプライト（9スライス・実行時生成）。
/// Image に Apply すると任意サイズの角丸矩形として描画される。
/// フレンドボタン・ガチャボタンなどフローティングボタンの角を丸めるのに使う。
/// </summary>
public static class RoundedRectSprite
{
    private const int SIZE = 64;
    private const int RADIUS = 20;
    private static Sprite _sprite;

    public static Sprite Get()
    {
        if (_sprite != null) return _sprite;

        var tex = new Texture2D(SIZE, SIZE, TextureFormat.RGBA32, false);
        var px = new Color32[SIZE * SIZE];
        float half = SIZE / 2f;
        float b = half - 1f - RADIUS; // 外周1pxはアンチエイリアス用に空ける

        for (int y = 0; y < SIZE; y++)
        {
            for (int x = 0; x < SIZE; x++)
            {
                // 角丸矩形のSDF（符号付き距離）からアルファを決める
                float qx = Mathf.Abs(x + 0.5f - half) - b;
                float qy = Mathf.Abs(y + 0.5f - half) - b;
                float d = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude
                          + Mathf.Min(Mathf.Max(qx, qy), 0f) - RADIUS;
                byte a = (byte)(Mathf.Clamp01(0.5f - d) * 255f);
                px[y * SIZE + x] = new Color32(255, 255, 255, a);
            }
        }
        tex.SetPixels32(px);
        tex.Apply();

        _sprite = Sprite.Create(tex, new Rect(0, 0, SIZE, SIZE), new Vector2(0.5f, 0.5f),
            100f, 0, SpriteMeshType.FullRect,
            new Vector4(RADIUS + 2, RADIUS + 2, RADIUS + 2, RADIUS + 2)); // 9スライスの境界
        return _sprite;
    }

    /// <summary>Imageを角丸矩形として描画する</summary>
    public static void Apply(Image img)
    {
        img.sprite = Get();
        img.type = Image.Type.Sliced;
    }
}
