using System.Collections.Generic;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;

/// <summary>
/// ピースパズルのお知らせ（2026-11-13）: ショップでピースを手に入れた（PiecesGot。「パズルを開く」つき）／
/// ボーナスマスのまわりを囲んで物をもらった（PuzzleBonus）。決まりは PuzzleRules、保存は PuzzleStore、画面は SpacePuzzle
/// </summary>
public static partial class SpaceNotice
{
    private const string T_PIECES_TITLE = "ピースを手に入れました";
    private const string T_PIECES_RARE = "レアなU字のピースが出ました！";
    private const string T_PIECES_OPEN = "パズルを開く";
    private const string T_PIECES_NOTE = "ホームの空のピース座をタップしても開けます";
    private const string T_BONUS_CAPTION = "BONUS";
    private const string T_BONUS_TITLE = "ボーナスマスを囲みました！";
    private const string T_BONUS_GOT = " を手に入れました";
    private const string T_BONUS_NOTE = "所持品に入りました";

    /// <summary>このお知らせの定型文（SpacePuzzle.GlyphText に含めて、NewHome の起動時にフォントへ登録する）</summary>
    public static string PuzzleGlyphText => new StringBuilder()
        .Append(T_PIECES_TITLE).Append(T_PIECES_RARE).Append(T_PIECES_OPEN).Append(T_PIECES_NOTE).Append(T_BONUS_CAPTION)
        .Append(T_BONUS_TITLE).Append(T_BONUS_GOT).Append(T_BONUS_NOTE).Append(T_CLOSE).Append("×・").ToString();

    /// <summary>
    /// 手に入れたピース（types。手に入れた順）を、種類ごとに絵と数で見せる。「パズルを開く」でパズルの画面を開く
    /// </summary>
    public static GameObject PiecesGot(List<int> types, string captionText, Color captionColor)
    {
        if (types == null || types.Count == 0) return null;
        bool rare = types.Contains(PuzzleRules.RARE);
        var card = Overlay("__PiecesGotModal", out var root, SpaceTheme.A(rare ? SpaceTheme.GOLD : SpacePuzzle.ACCENT, 0.55f));
        if (card == null) return null;

        float y = 48f;
        var caption = SpaceTheme.Text(card, "Caption", captionText ?? "PUZZLE", 24, SpaceTheme.A(captionColor, 0.9f),
            FontStyles.Bold);
        SpaceTheme.Tracking(caption, 18f);
        Stack(caption.rectTransform, ref y, 36f, 0f);
        var title = SpaceTheme.Text(card, "Title", T_PIECES_TITLE, 40, SpaceTheme.TEXT, FontStyles.Bold);
        Stack(title.rectTransform, ref y, 56f, 8f);

        // 種類ごとに1つ（手に入れた順。4つずつ並べる）。下に「×2」
        var kinds = new List<int>();
        foreach (int t in types)
            if (!kinds.Contains(t)) kinds.Add(t);
        const float TILE = 170f, TILE_GAP = 18f, LABEL_H = 40f;
        int perRow = Mathf.Min(4, kinds.Count);
        int rows = (kinds.Count + perRow - 1) / perRow;
        var stage = SpaceTheme.Rect("Pieces", card);
        Stack(stage, ref y, rows * (TILE + LABEL_H) + (rows - 1) * TILE_GAP, 24f);
        var top = new Vector2(0.5f, 1f);
        for (int i = 0; i < kinds.Count; i++)
        {
            int type = kinds[i];
            int row = i / perRow, col = i % perRow;
            int inRow = Mathf.Min(perRow, kinds.Count - row * perRow);
            float rowW = inRow * TILE + (inRow - 1) * TILE_GAP;
            var pos = new Vector2(-rowW * 0.5f + TILE * 0.5f + col * (TILE + TILE_GAP), -(TILE * 0.5f + row * (TILE + LABEL_H + TILE_GAP)));
            var color = PuzzleArt.ColorOf(type);
            var glow = SpaceTheme.Rect("Glow" + type, stage, top, pos, new Vector2(TILE * 1.4f, TILE * 1.4f));
            SpaceTheme.Img(glow, SpaceSprites.Glow, SpaceTheme.A(color, PuzzleRules.Piece(type).Rare ? 0.55f : 0.3f));
            var tile = SpaceTheme.Rect("Piece" + type, stage, top, pos, new Vector2(TILE, TILE));
            var img = SpaceTheme.Img(tile, PuzzleArt.Icon(type), Color.white);
            img.preserveAspect = true;
            int n = types.Count(x => x == type);
            string label = (PuzzleRules.Piece(type)?.Name ?? "") + (n > 1 ? " ×" + n : "");
            var t = SpaceTheme.Text(stage, "Name" + type, label, 28, PuzzleRules.Piece(type).Rare ? SpaceTheme.GOLD : SpaceTheme.TEXT,
                top, pos + new Vector2(0f, -(TILE * 0.5f + LABEL_H * 0.5f)), new Vector2(TILE + TILE_GAP, LABEL_H), FontStyles.Bold);
            t.enableAutoSizing = true;
            t.fontSizeMin = 18f;
            t.fontSizeMax = 28f;
        }
        if (rare)
        {
            var r = SpaceTheme.Text(card, "Rare", T_PIECES_RARE, 32, SpaceTheme.GOLD, FontStyles.Bold);
            Wrap(r);
            Stack(r.rectTransform, ref y, Measure(r, 44f), 20f);
        }
        var note = SpaceTheme.Text(card, "Note", T_PIECES_NOTE, 24, SpaceTheme.TEXT_SUB, FontStyles.Bold);
        Wrap(note);
        Stack(note.rectTransform, ref y, Measure(note, 36f), 16f);

        // パズルを開く（主ボタン）＋とじる
        var open = SpaceTheme.Rect("Open", card);
        Stack(open, ref y, 128f, 36f, 560f);
        var face = SpaceTheme.PrimaryFace(open);
        SpaceTheme.Clickable(open.gameObject, face, () =>
        {
            Object.Destroy(root);
            // パズルの上からショップを開いていたら（パズルの「ショップでピースを買う」）、ショップを閉じてパズルに戻る
            if (SpacePuzzle.Instance != null)
            {
                if (SpaceShop.Instance != null) SpaceShop.Instance.Close();
            }
            else SpacePuzzle.Open();
        });
        var openLabel = SpaceTheme.Text(open, "Label", T_PIECES_OPEN, 40, SpaceTheme.TEXT_DARK, FontStyles.Bold);
        SpaceTheme.Stretch(openLabel.rectTransform);

        var later = SpaceTheme.Rect("Later", card);
        Stack(later, ref y, 76f, 12f, 320f);
        var laterHit = SpaceTheme.HitArea(later);
        SpaceTheme.Clickable(later.gameObject, laterHit, () => Object.Destroy(root));
        var laterLabel = SpaceTheme.Text(later, "Label", T_CLOSE, 32, SpaceTheme.TEXT_SUB, FontStyles.Bold);
        SpaceTheme.Stretch(laterLabel.rectTransform);

        y += 44f;
        card.sizeDelta = new Vector2(WIDTH, y);
        root.AddComponent<SpacePopIn>().Target = card;
        return root;
    }

    /// <summary>ボーナスマスのまわりを囲んで、物をもらった（1回に2つ以上囲んだら全部並べる）</summary>
    public static GameObject PuzzleBonus(List<PuzzleBonus> got)
    {
        if (got == null || got.Count == 0) return null;
        var card = Overlay("__PuzzleBonusModal", out var root, SpaceTheme.A(SpaceTheme.GOLD, 0.6f));
        if (card == null) return null;
        SpaceTheme.SubCanvas(root, true);

        float y = 48f;
        var caption = SpaceTheme.Text(card, "Caption", T_BONUS_CAPTION, 24, SpaceTheme.A(SpaceTheme.GOLD, 0.95f), FontStyles.Bold);
        SpaceTheme.Tracking(caption, 18f);
        Stack(caption.rectTransform, ref y, 36f, 0f);
        var title = SpaceTheme.Text(card, "Title", T_BONUS_TITLE, 40, SpaceTheme.TEXT, FontStyles.Bold);
        Stack(title.rectTransform, ref y, 56f, 8f);

        // もらった物（1つなら大きく、2つ以上なら並べる）
        const float ICON = 200f, ICON_GAP = 40f;
        int n = Mathf.Min(got.Count, 3);
        var stage = SpaceTheme.Rect("Stage", card);
        Stack(stage, ref y, ICON + 40f, 24f);
        float rowW = n * ICON + (n - 1) * ICON_GAP;
        for (int i = 0; i < n; i++)
        {
            var b = got[i];
            var pos = new Vector2(-rowW * 0.5f + ICON * 0.5f + i * (ICON + ICON_GAP), 0f);
            var glow = SpaceTheme.Rect("Glow" + i, stage, SpaceTheme.CENTER, pos, new Vector2(ICON * 1.6f, ICON * 1.6f));
            SpaceTheme.Img(glow, SpaceSprites.Glow, SpaceTheme.A(PuzzleStore.RewardTint(b.Reward), 0.4f));
            var sprite = PuzzleStore.RewardIcon(b.Reward);
            if (PuzzleStore.RewardIsCoupon(b.Reward))
                SpaceInventoryPanel.RoundedIcon(stage, sprite, SpaceTheme.CENTER, pos, ICON * 0.9f, 36f);
            else
            {
                var icon = SpaceTheme.Rect("Icon" + i, stage, SpaceTheme.CENTER, pos, new Vector2(ICON, ICON));
                var img = SpaceTheme.Img(icon, sprite, Color.white);
                img.preserveAspect = true;
                img.enabled = sprite != null;
            }
        }

        string names = string.Join("・", got.Select(PuzzleStore.RewardText));
        var strong = SpaceTheme.Text(card, "Strong",
            $"<color=#{ColorUtility.ToHtmlStringRGB(SpaceTheme.GOLD)}>{names}</color>{T_BONUS_GOT}", 40, SpaceTheme.TEXT,
            FontStyles.Bold);
        strong.richText = true;
        Wrap(strong);
        Stack(strong.rectTransform, ref y, Measure(strong, 56f), 20f);
        var note = SpaceTheme.Text(card, "Note", T_BONUS_NOTE, 26, SpaceTheme.TEXT_SUB, FontStyles.Bold);
        Stack(note.rectTransform, ref y, 40f, 8f);
        Finish(card, root, ref y);
        return root;
    }
}
