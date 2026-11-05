using System;
using TMPro;
using UnityEngine;

/// <summary>
/// 宇宙デザインの確認ダイアログと処理中表示（コード生成）。
/// どちらも戻り値のルートを Destroy すれば消える。
/// 本文の日本語は事前に SpaceTheme.Prewarm で登録しておくこと（OOM対策）。
/// </summary>
public static class SpaceDialog
{
    private const float WIDTH = 880f;
    private const float LINE_H = 46f;

    /// <summary>
    /// 確認ダイアログ（キャンセル／決定の2ボタン）。暗幕のタップはキャンセル扱い。
    /// danger=true で決定ボタンとタイトルを警告色にする（退会など取り消せない操作）。
    /// </summary>
    public static GameObject Confirm(Canvas canvas, string title, string body, string okLabel, bool danger,
        Action onOk, Action onCancel = null, string cancelLabel = "キャンセル")
    {
        if (canvas == null) canvas = SpaceTheme.MainCanvas();
        var root = SpaceTheme.Rect("__SpaceDialog", canvas.transform);
        SpaceTheme.Stretch(root);
        root.SetAsLastSibling();
        var go = root.gameObject;

        void Close()
        {
            if (go != null) UnityEngine.Object.Destroy(go);
        }

        var dim = SpaceTheme.Rect("Dim", root);
        SpaceTheme.Stretch(dim);
        var dimImg = SpaceTheme.Img(dim, null, SpaceTheme.DIM, true);
        SpaceTheme.Clickable(dim.gameObject, dimImg, () => { Close(); onCancel?.Invoke(); }, false);

        // 本文の行数に合わせて高さを決める
        int lines = 1;
        foreach (var c in body ?? "") if (c == '\n') lines++;
        float bodyH = Mathf.Max(60f, lines * LINE_H);
        float h = 404f + bodyH;

        var card = SpaceTheme.Rect("Card", root, SpaceTheme.CENTER, Vector2.zero, new Vector2(WIDTH, h));
        SpaceTheme.Glass(card, 56f, SpaceTheme.GLASS_DEEP,
            danger ? SpaceTheme.A(SpaceTheme.DANGER, 0.43f) : (Color?)null, true);

        SpaceTheme.Text(card, "Title", title, 44, danger ? SpaceTheme.DANGER : SpaceTheme.TEXT,
            SpaceTheme.CENTER, new Vector2(0f, h / 2f - 96f), new Vector2(WIDTH - 80f, 64f), FontStyles.Bold);
        var bodyText = SpaceTheme.Text(card, "Body", body, 30, SpaceTheme.TEXT_SUB,
            SpaceTheme.CENTER, new Vector2(0f, h / 2f - 164f - bodyH / 2f), new Vector2(WIDTH - 100f, bodyH),
            FontStyles.Bold);
        bodyText.textWrappingMode = TextWrappingModes.Normal;

        float by = -h / 2f + 120f;
        var cancel = SpaceTheme.Rect("Cancel", card, SpaceTheme.CENTER, new Vector2(-210f, by), new Vector2(380f, 128f));
        var cancelFace = SpaceTheme.Glass(cancel, 46f, SpaceTheme.A(new Color(0.09f, 0.12f, 0.30f), 0.63f), null, true);
        SpaceTheme.Clickable(cancel.gameObject, cancelFace, () => { Close(); onCancel?.Invoke(); });
        var cancelText = SpaceTheme.Text(cancel, "Label", cancelLabel, 38, SpaceTheme.TEXT_SUB, FontStyles.Bold);
        SpaceTheme.Stretch(cancelText.rectTransform);

        var ok = SpaceTheme.Rect("Ok", card, SpaceTheme.CENTER, new Vector2(210f, by), new Vector2(380f, 128f));
        var okFace = danger ? SpaceTheme.DangerFace(ok) : SpaceTheme.PrimaryFace(ok);
        SpaceTheme.Clickable(ok.gameObject, okFace, () => { Close(); onOk?.Invoke(); });
        var okText = SpaceTheme.Text(ok, "Label", okLabel, 38, danger ? Color.white : SpaceTheme.TEXT_DARK,
            FontStyles.Bold);
        SpaceTheme.Stretch(okText.rectTransform, 16f, 0f, 16f, 0f);
        okText.enableAutoSizing = true;
        okText.fontSizeMin = 26f;
        okText.fontSizeMax = 38f;

        go.AddComponent<SpacePopIn>().Target = card;
        return go;
    }

    /// <summary>処理中の表示（回る軌道＋メッセージ）。タップでは消えない</summary>
    public static GameObject Busy(Canvas canvas, string message)
    {
        if (canvas == null) canvas = SpaceTheme.MainCanvas();
        var root = SpaceTheme.Rect("__SpaceBusy", canvas.transform);
        SpaceTheme.Stretch(root);
        root.SetAsLastSibling();

        var dim = SpaceTheme.Rect("Dim", root);
        SpaceTheme.Stretch(dim);
        SpaceTheme.Img(dim, null, SpaceTheme.DIM, true); // 下の画面を押せないようにする

        var card = SpaceTheme.Rect("Card", root, SpaceTheme.CENTER, Vector2.zero, new Vector2(620f, 320f));
        SpaceTheme.Glass(card, 48f, SpaceTheme.GLASS_DEEP, null, true);

        var orbit = SpaceTheme.Rect("Spinner", card, SpaceTheme.CENTER, new Vector2(0f, 44f), new Vector2(120f, 120f));
        SpaceTheme.Img(orbit, SpaceSprites.RingSmall, SpaceTheme.A(SpaceTheme.CYAN, 0.45f));
        var sat = SpaceTheme.Rect("Satellite", orbit, SpaceTheme.CENTER, new Vector2(0f, 58f), new Vector2(36f, 36f));
        SpaceTheme.Img(sat, SpaceSprites.Glow, SpaceTheme.A(SpaceTheme.CYAN, 0.95f));
        var core = SpaceTheme.Rect("Core", sat, SpaceTheme.CENTER, Vector2.zero, new Vector2(12f, 12f));
        SpaceTheme.Img(core, SpaceSprites.Circle, Color.white);
        orbit.gameObject.AddComponent<SpaceSpinner>().DegreesPerSecond = -220f;

        SpaceTheme.Text(card, "Message", message, 32, SpaceTheme.TEXT, SpaceTheme.CENTER, new Vector2(0f, -88f),
            new Vector2(560f, 50f), FontStyles.Bold);

        root.gameObject.AddComponent<SpacePopIn>().Target = card;
        return root.gameObject;
    }
}
