using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// コード生成UI用の円スプライト（実行時生成・アンチエイリアス付き）。
/// RectTransform のサイズを非正方形にすれば楕円としても使える。
/// フレンドボタンの人型アイコンなど、簡単な図形の組み立てに使う。
/// </summary>
public static class UICircleSprite
{
    private const int SIZE = 64;
    private static Sprite _sprite;

    public static Sprite Get()
    {
        if (_sprite != null) return _sprite;

        var tex = new Texture2D(SIZE, SIZE, TextureFormat.RGBA32, false);
        var px = new Color32[SIZE * SIZE];
        float c = (SIZE - 1) / 2f;
        float r = SIZE / 2f - 1.5f;
        for (int y = 0; y < SIZE; y++)
        {
            for (int x = 0; x < SIZE; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                byte a = (byte)(Mathf.Clamp01(r - d + 1f) * 255f); // 1pxアンチエイリアス
                px[y * SIZE + x] = new Color32(255, 255, 255, a);
            }
        }
        tex.SetPixels32(px);
        tex.Apply();

        _sprite = Sprite.Create(tex, new Rect(0, 0, SIZE, SIZE), new Vector2(0.5f, 0.5f));
        return _sprite;
    }

    /// <summary>Imageを円（サイズ次第で楕円）として描画する</summary>
    public static void Apply(Image img)
    {
        img.sprite = Get();
        img.type = Image.Type.Simple;
    }
}
