using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 自分のQRを全画面で大きく見せる画面（会員証・友だち紹介など。2026-11-07）。
/// 白地のQR・名前・説明・閉じるボタン。背景のタップでは閉じない（見せている途中の誤タップ対策）。
/// 画面の明るさは変えられないので、「明るさを上げて」の案内は説明文に入れる
/// </summary>
public static class SpaceQrSheet
{
    private const float QR_CARD = 760f, QR_IMAGE = 680f;
    public const string T_CLOSE = "閉じる";

    /// <param name="accent">QRの後ろの光の色</param>
    /// <param name="onClose">閉じたとき（省略可）</param>
    public static GameObject Show(Canvas canvas, string caption, string title, string payload, string nameLine,
        string note, Color accent, Action onClose = null)
    {
        if (canvas == null) canvas = SpaceTheme.MainCanvas();
        var root = SpaceTheme.Rect("__QrSheet", canvas.transform);
        SpaceTheme.Stretch(root);
        root.SetAsLastSibling();
        var go = root.gameObject;
        Texture2D tex = null;

        void Close()
        {
            if (go != null) UnityEngine.Object.Destroy(go);
            if (tex != null) UnityEngine.Object.Destroy(tex);
            onClose?.Invoke();
        }

        var dim = SpaceTheme.Rect("Dim", root);
        SpaceTheme.Stretch(dim);
        SpaceTheme.Img(dim, null, new Color(0.01f, 0.015f, 0.05f, 0.97f), true);
        var safe = SpaceTheme.Rect("Safe", root);
        SpaceTheme.Stretch(safe);
        safe.gameObject.AddComponent<SpaceSafeArea>();

        var cap = SpaceTheme.Text(safe, "Caption", caption, 24, SpaceTheme.A(accent, 0.95f), new Vector2(0.5f, 1f),
            new Vector2(0f, -70f), new Vector2(700f, 36f), FontStyles.Bold);
        SpaceTheme.Tracking(cap, 18f);
        SpaceTheme.Text(safe, "Title", title, 44, SpaceTheme.TEXT, new Vector2(0.5f, 1f), new Vector2(0f, -124f),
            new Vector2(800f, 60f), FontStyles.Bold);

        // 右上の閉じるボタン
        var x = SpaceTheme.Rect("Close", safe, new Vector2(1f, 1f), new Vector2(-112f, -84f), new Vector2(104f, 104f));
        var xFace = SpaceTheme.Img(x, SpaceSprites.Circle, new Color(0.05f, 0.07f, 0.19f, 0.85f), true);
        var rim = SpaceTheme.Rect("Rim", x);
        SpaceTheme.Stretch(rim);
        SpaceTheme.Img(rim, SpaceSprites.RingSmall, SpaceTheme.STROKE);
        SpaceTheme.Bar(x, Vector2.zero, 44f, 6f, 45f, SpaceTheme.TEXT);
        SpaceTheme.Bar(x, Vector2.zero, 44f, 6f, -45f, SpaceTheme.TEXT);
        SpaceTheme.Clickable(x.gameObject, xFace, Close);

        // 白地のQR（後ろに光）
        float cardY = -(196f + QR_CARD * 0.5f);
        var glow = SpaceTheme.Rect("Glow", safe, new Vector2(0.5f, 1f), new Vector2(0f, cardY),
            new Vector2(QR_CARD + 220f, QR_CARD + 220f));
        SpaceTheme.Img(glow, SpaceSprites.Glow, SpaceTheme.A(accent, 0.30f));
        var card = SpaceTheme.Rect("Card", safe, new Vector2(0.5f, 1f), new Vector2(0f, cardY), new Vector2(QR_CARD, QR_CARD));
        SpaceTheme.Rounded(card, Color.white, 44f);
        var pic = SpaceTheme.Rect("Code", card, SpaceTheme.CENTER, Vector2.zero, new Vector2(QR_IMAGE, QR_IMAGE));
        var raw = pic.gameObject.AddComponent<RawImage>();
        raw.raycastTarget = false;
        tex = QRCodeHelper.CreateQRCode(payload, 512, 512);
        tex.filterMode = FilterMode.Point;
        raw.texture = tex;

        float textY = 196f + QR_CARD;
        float innerW = 900f;
        if (!string.IsNullOrEmpty(nameLine))
        {
            var me = SpaceTheme.Text(safe, "Name", nameLine, 44, SpaceTheme.TEXT, new Vector2(0.5f, 1f),
                new Vector2(0f, -(textY + 56f)), new Vector2(innerW, 60f), FontStyles.Bold);
            me.enableAutoSizing = true;
            me.fontSizeMin = 28f;
            me.fontSizeMax = 44f;
        }
        var noteText = SpaceTheme.Text(safe, "Note", note, 28, SpaceTheme.TEXT_SUB, new Vector2(0.5f, 1f),
            new Vector2(0f, -(textY + 150f)), new Vector2(innerW, 120f), FontStyles.Bold);
        noteText.textWrappingMode = TextWrappingModes.Normal;

        var back = SpaceTheme.Rect("Back", safe, new Vector2(0.5f, 0f), new Vector2(0f, 56f), new Vector2(560f, 112f),
            new Vector2(0.5f, 0f));
        var backFace = SpaceTheme.Glass(back, 46f, new Color(0.05f, 0.07f, 0.19f, 0.92f), null, true);
        SpaceTheme.Clickable(back.gameObject, backFace, Close);
        var backText = SpaceTheme.Text(back, "Label", T_CLOSE, 36, SpaceTheme.TEXT, FontStyles.Bold);
        SpaceTheme.Stretch(backText.rectTransform);

        go.AddComponent<SpacePopIn>().Target = card;
        return go;
    }
}
