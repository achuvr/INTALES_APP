using System;
using TMPro;
using UnityEngine;

/// <summary>
/// 誕生月を選ぶ小さな画面（1〜12月を4列×3段。2026-11-07）。アカウント作成の画面と、ホームの歯車メニューから使う。
/// 月だけ聞く（日付・年は聞かない）。「答えない」も選べる（0）
/// </summary>
public static class SpaceMonthPicker
{
    public const string T_TITLE = "誕生月";
    public const string T_BODY = "誕生月に特典があります（月だけ・あとから変えられません）";
    public const string T_SKIP = "答えない";
    public const string T_UNIT = "月";

    public static string GlyphText => T_TITLE + T_BODY + T_SKIP + T_UNIT + "0123456789";

    /// <param name="onPick">選んだ月（1〜12）。「答えない」は 0。まわりのタップで閉じたときは呼ばない</param>
    public static GameObject Show(Canvas canvas, int current, Action<int> onPick, string body = null)
    {
        if (canvas == null) canvas = SpaceTheme.MainCanvas();
        var root = SpaceTheme.Rect("__MonthPicker", canvas.transform);
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
        SpaceTheme.Clickable(dim.gameObject, dimImg, Close, false);

        const float W = 920f, CELL_W = 190f, CELL_H = 120f, GAP = 18f, TOP = 200f;
        float h = TOP + 3 * CELL_H + 2 * GAP + 40f + 100f + 48f;
        var card = SpaceTheme.Rect("Card", root, SpaceTheme.CENTER, Vector2.zero, new Vector2(W, h));
        SpaceTheme.Glass(card, 56f, SpaceTheme.GLASS_DEEP, SpaceTheme.A(SpaceTheme.PINK, 0.45f), true);
        SpaceTheme.Text(card, "Title", T_TITLE, 44, SpaceTheme.TEXT, new Vector2(0.5f, 1f), new Vector2(0f, -80f),
            new Vector2(W - 80f, 64f), FontStyles.Bold);
        var b = SpaceTheme.Text(card, "Body", body ?? T_BODY, 26, SpaceTheme.TEXT_SUB, new Vector2(0.5f, 1f),
            new Vector2(0f, -140f), new Vector2(W - 80f, 44f), FontStyles.Bold);
        b.enableAutoSizing = true;
        b.fontSizeMin = 18f;
        b.fontSizeMax = 26f;

        float gridW = 4 * CELL_W + 3 * GAP;
        for (int i = 0; i < 12; i++)
        {
            int month = i + 1;
            bool on = month == current;
            float x = -gridW / 2f + CELL_W / 2f + (i % 4) * (CELL_W + GAP);
            float y = -(TOP + CELL_H / 2f + (i / 4) * (CELL_H + GAP));
            var cell = SpaceTheme.Rect("M" + month, card, new Vector2(0.5f, 1f), new Vector2(x, y), new Vector2(CELL_W, CELL_H));
            var face = SpaceTheme.Glass(cell, 34f,
                on ? SpaceTheme.A(SpaceTheme.PINK, 0.2f) : new Color(0.07f, 0.09f, 0.24f, 0.78f),
                on ? SpaceTheme.A(SpaceTheme.PINK, 0.9f) : (Color?)null, true);
            SpaceTheme.Clickable(cell.gameObject, face, () =>
            {
                Close();
                onPick?.Invoke(month);
            });
            var t = SpaceTheme.Text(cell, "Label", month + T_UNIT, 40, on ? SpaceTheme.PINK : SpaceTheme.TEXT, FontStyles.Bold);
            SpaceTheme.Stretch(t.rectTransform);
        }

        var skip = SpaceTheme.Rect("Skip", card, new Vector2(0.5f, 0f), new Vector2(0f, 48f), new Vector2(420f, 100f),
            new Vector2(0.5f, 0f));
        var skipFace = SpaceTheme.Glass(skip, 46f, new Color(0.09f, 0.12f, 0.30f, 0.63f), null, true);
        SpaceTheme.Clickable(skip.gameObject, skipFace, () =>
        {
            Close();
            onPick?.Invoke(0);
        });
        var st = SpaceTheme.Text(skip, "Label", T_SKIP, 34, SpaceTheme.TEXT_SUB, FontStyles.Bold);
        SpaceTheme.Stretch(st.rectTransform);

        go.AddComponent<SpacePopIn>().Target = card;
        return go;
    }
}
