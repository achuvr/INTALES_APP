using System.Collections.Generic;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;

/// <summary>
/// ミニオンのお知らせ（2026-11-14）: 帰ってきたミニオンに話しかけた（MinionBack。持ち帰った物・失敗・レベル）／
/// 帰ってから1週間すぎて、持ち帰った物を食べてしまった（MinionsAte）。決まりは MinionRules、保存は MinionStore、画面は SpaceMinions
/// </summary>
public static partial class SpaceNotice
{
    private const string T_MINION_CAPTION = "MINION";
    private const string T_MINION_BACK_B = " が帰ってきました！";
    private const string T_MINION_SAY_OK = "「これを見つけてきたよ！」";
    private const string T_MINION_SAY_RARE = "「すごい物を見つけてきたよ！」";
    private const string T_MINION_SAY_FAIL = "「ごめんね…今回は何も見つけられなかった」";
    private const string T_MINION_GOT_B = " を受け取りました";
    private const string T_MINION_RARE = "RARE";
    private const string T_MINION_LEVEL_A = "Lv.";
    private const string T_MINION_ARROW = " → ";
    private const string T_MINION_MAX = "レベルMAX！ 新しいミニオンを作れるようになりました";
    private const string T_MINION_FAIL_NOTE = "旅は失敗することもあります。レベルが上がるほど、うまくいきやすくなります";
    private const string T_MINION_ATE_TITLE = "持ち帰った物を食べてしまいました";
    private const string T_MINION_ATE_A = "は、持ち帰った ";
    private const string T_MINION_ATE_B = " を食べてしまいました";
    private const string T_MINION_ATE_NOTE = "帰ってから1週間のうちに、入店中に話しかけると持ち帰った物を受け取れます";
    private const string T_MINION_ATE_MORE_B = " 体も食べてしまいました";
    // お店からミニオンが届いた（2026-11-14。管理者のQR＝MinionGiftQr）
    private const string T_MINION_GIFT_OPEN = "ミニオンの画面を開く";
    private const string T_MINION_GIFT_NOTE = "ホームの空のきずな座をタップすると、旅に出せます";
    private const string T_MINION_GIFT_LOCKED = "きずな座を完成させると、ミニオンの画面で会えます";

    /// <summary>このお知らせの定型文（SpaceMinions.GlyphText に含めて、NewHome の起動時にフォントへ登録する）</summary>
    public static string MinionGlyphText => new StringBuilder()
        .Append(T_MINION_CAPTION).Append(T_MINION_BACK_B).Append(T_MINION_SAY_OK).Append(T_MINION_SAY_RARE)
        .Append(T_MINION_SAY_FAIL).Append(T_MINION_GOT_B).Append(T_MINION_RARE).Append(T_MINION_LEVEL_A).Append(T_MINION_ARROW)
        .Append(T_MINION_MAX).Append(T_MINION_FAIL_NOTE).Append(T_MINION_ATE_TITLE).Append(T_MINION_ATE_A).Append(T_MINION_ATE_B)
        .Append(T_MINION_ATE_NOTE).Append(T_MINION_ATE_MORE_B).Append(T_MINION_GIFT_OPEN).Append(T_MINION_GIFT_NOTE)
        .Append(T_MINION_GIFT_LOCKED).Append(MinionGiftQr.GlyphText).Append(T_CLOSE).Append("ほか0123456789").ToString();

    /// <summary>
    /// お店からミニオンが届いた（管理者のQR。2026-11-14）: ミニオンの絵・名前・レベル。ミニオンの画面を開けるなら（unlocked）
    /// 「ミニオンの画面を開く」、開けないなら（きずな座が未完成）どうすれば会えるか
    /// </summary>
    public static GameObject MinionGift(MinionStore.Result r, bool unlocked)
    {
        if (r == null || r.Minion == null) return null;
        var m = r.Minion;
        var color = MinionArt.ColorOf(m.Element);
        var card = Overlay("__MinionGiftModal", out var root, SpaceTheme.A(color, 0.6f));
        if (card == null) return null;
        SpaceTheme.SubCanvas(root, true); // 光がまたたくので、メインのキャンバスと分ける

        float y = 48f;
        var caption = SpaceTheme.Text(card, "Caption", MinionGiftQr.T_CAPTION, 24, SpaceTheme.A(color, 0.95f), FontStyles.Bold);
        SpaceTheme.Tracking(caption, 18f);
        Stack(caption.rectTransform, ref y, 36f, 0f);
        var title = SpaceTheme.Text(card, "Title", MinionGiftQr.T_TITLE, 40, SpaceTheme.TEXT, FontStyles.Bold);
        Wrap(title);
        Stack(title.rectTransform, ref y, Measure(title, 56f), 8f);

        // ミニオン（光がまたたく）と属性の印
        const float STAGE = 320f, PIC = 290f;
        var stage = SpaceTheme.Rect("Stage", card);
        Stack(stage, ref y, STAGE, 16f);
        var glow = SpaceTheme.Rect("Glow", stage, SpaceTheme.CENTER, Vector2.zero, new Vector2(PIC * 1.5f, PIC * 1.5f));
        var glowImg = SpaceTheme.Img(glow, SpaceSprites.Glow, SpaceTheme.A(color, 0.45f));
        glow.gameObject.AddComponent<SpaceTwinkle>().Init(glowImg, 0.45f);
        var pic = SpaceTheme.Rect("Minion", stage, SpaceTheme.CENTER, Vector2.zero, new Vector2(PIC, PIC));
        var img = SpaceTheme.Img(pic, MinionArt.Sprite(m), Color.white);
        img.preserveAspect = true;
        var el = SpaceTheme.Rect("Element", stage, SpaceTheme.CENTER, new Vector2(-PIC * 0.42f, -PIC * 0.36f), new Vector2(80f, 80f));
        var elImg = SpaceTheme.Img(el, MinionArt.ElementIcon(m.Element), Color.white);
        elImg.preserveAspect = true;

        // 名前と Lv.
        var name = SpaceTheme.Text(card, "Name",
            $"<color=#{ColorUtility.ToHtmlStringRGB(color)}>{SpaceTheme.SafeNow(r.Name ?? MinionRules.NameOf(m))}</color>"
            + $"  <color=#{ColorUtility.ToHtmlStringRGB(SpaceTheme.GOLD)}>{T_MINION_LEVEL_A}{m.Level}</color>",
            44, SpaceTheme.TEXT, FontStyles.Bold);
        name.richText = true;
        Wrap(name);
        Stack(name.rectTransform, ref y, Measure(name, 60f), 14f);
        var note = SpaceTheme.Text(card, "Note", unlocked ? T_MINION_GIFT_NOTE : T_MINION_GIFT_LOCKED, 26, SpaceTheme.TEXT_SUB,
            FontStyles.Bold);
        Wrap(note);
        Stack(note.rectTransform, ref y, Measure(note, 40f), 12f);

        if (!unlocked)
        {
            Finish(card, root, ref y);
            return root;
        }
        // ミニオンの画面を開く（主ボタン）＋とじる
        var open = SpaceTheme.Rect("Open", card);
        Stack(open, ref y, 128f, 36f, 560f);
        var face = SpaceTheme.PrimaryFace(open);
        SpaceTheme.Clickable(open.gameObject, face, () =>
        {
            Object.Destroy(root);
            SpaceMinions.Open();
        });
        var openLabel = SpaceTheme.Text(open, "Label", T_MINION_GIFT_OPEN, 40, SpaceTheme.TEXT_DARK, FontStyles.Bold);
        SpaceTheme.Stretch(openLabel.rectTransform, 16f, 0f, 16f, 0f);
        openLabel.enableAutoSizing = true;
        openLabel.fontSizeMin = 26f;
        openLabel.fontSizeMax = 40f;
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

    /// <summary>
    /// 帰ってきたミニオンに話しかけた: ミニオンと持ち帰った物（レアなら金色の光と「RARE」）・ミニオンのひとこと・レベルの上がり方。
    /// 旅が失敗していたら、ミニオンだけで「ごめんね…」
    /// </summary>
    public static GameObject MinionBack(MinionStore.Result r)
    {
        if (r == null || r.Minion == null) return null;
        var m = r.Minion;
        var color = MinionArt.ColorOf(m.Element);
        bool ok = r.TripOk && !string.IsNullOrEmpty(r.Reward);
        bool rare = ok && r.Rare;
        var card = Overlay("__MinionBackModal", out var root, SpaceTheme.A(rare ? SpaceTheme.GOLD : color, 0.6f));
        if (card == null) return null;
        SpaceTheme.SubCanvas(root, true); // 光がまたたくので、メインのキャンバスと分ける

        float y = 48f;
        var caption = SpaceTheme.Text(card, "Caption", T_MINION_CAPTION, 24, SpaceTheme.A(color, 0.95f), FontStyles.Bold);
        SpaceTheme.Tracking(caption, 18f);
        Stack(caption.rectTransform, ref y, 36f, 0f);
        var title = SpaceTheme.Text(card, "Title", SpaceTheme.SafeNow((r.Name ?? MinionRules.NameOf(m)) + T_MINION_BACK_B), 40,
            SpaceTheme.TEXT, FontStyles.Bold);
        Wrap(title);
        Stack(title.rectTransform, ref y, Measure(title, 56f), 8f);

        // ミニオン（左）と持ち帰った物（右）。失敗なら、ミニオンだけをまん中に
        const float STAGE = 300f, PIC = 260f, ICON = 190f;
        var stage = SpaceTheme.Rect("Stage", card);
        Stack(stage, ref y, STAGE, 16f);
        float minionX = ok ? -170f : 0f;
        var mg = SpaceTheme.Rect("MinionGlow", stage, SpaceTheme.CENTER, new Vector2(minionX, 0f), new Vector2(PIC * 1.4f, PIC * 1.4f));
        SpaceTheme.Img(mg, SpaceSprites.Glow, SpaceTheme.A(color, ok ? 0.35f : 0.18f));
        var mp = SpaceTheme.Rect("Minion", stage, SpaceTheme.CENTER, new Vector2(minionX, 0f), new Vector2(PIC, PIC));
        var mimg = SpaceTheme.Img(mp, MinionArt.Sprite(m), ok ? Color.white : new Color(1f, 1f, 1f, 0.8f));
        mimg.preserveAspect = true;
        if (ok)
        {
            var tint = rare ? SpaceTheme.GOLD : MinionStore.RewardTint(r.Reward);
            var pos = new Vector2(170f, 0f);
            var rg = SpaceTheme.Rect("RewardGlow", stage, SpaceTheme.CENTER, pos, new Vector2(ICON * 1.7f, ICON * 1.7f));
            var rgImg = SpaceTheme.Img(rg, SpaceSprites.Glow, SpaceTheme.A(tint, rare ? 0.6f : 0.4f));
            if (rare) rg.gameObject.AddComponent<SpaceTwinkle>().Init(rgImg, 0.6f);
            var sprite = MinionStore.RewardIcon(r.Reward);
            if (MinionStore.RewardIsCoupon(r.Reward))
                SpaceInventoryPanel.RoundedIcon(stage, sprite, SpaceTheme.CENTER, pos, ICON * 0.9f, 34f);
            else
            {
                var icon = SpaceTheme.Rect("Reward", stage, SpaceTheme.CENTER, pos, new Vector2(ICON, ICON));
                var img = SpaceTheme.Img(icon, sprite, Color.white);
                img.preserveAspect = true;
                img.enabled = sprite != null;
            }
            if (rare)
            {
                var badge = SpaceTheme.Text(stage, "Rare", T_MINION_RARE, 30, SpaceTheme.GOLD, SpaceTheme.CENTER,
                    pos + new Vector2(0f, ICON * 0.5f + 24f), new Vector2(240f, 40f), FontStyles.Bold);
                SpaceTheme.Tracking(badge, 14f);
            }
        }

        // ミニオンのひとこと
        var say = SpaceTheme.Text(card, "Say", ok ? (rare ? T_MINION_SAY_RARE : T_MINION_SAY_OK) : T_MINION_SAY_FAIL, 30,
            ok ? SpaceTheme.TEXT : SpaceTheme.TEXT_SUB, FontStyles.Bold);
        Wrap(say);
        Stack(say.rectTransform, ref y, Measure(say, 44f), 18f);

        if (ok)
        {
            var got = SpaceTheme.Text(card, "Got",
                $"<color=#{ColorUtility.ToHtmlStringRGB(SpaceTheme.GOLD)}>{MinionStore.RewardName(r.Reward)}</color>{T_MINION_GOT_B}", 36,
                SpaceTheme.TEXT, FontStyles.Bold);
            got.richText = true;
            Wrap(got);
            Stack(got.rectTransform, ref y, Measure(got, 50f), 14f);
        }
        else
        {
            var note = SpaceTheme.Text(card, "FailNote", T_MINION_FAIL_NOTE, 24, SpaceTheme.TEXT_MUTED, FontStyles.Bold);
            Wrap(note);
            Stack(note.rectTransform, ref y, Measure(note, 36f), 12f);
        }

        // レベル（帰ってくるたびに +1）
        Stack(Divider(card), ref y, 2f, 22f, INNER - 160f);
        var level = SpaceTheme.Text(card, "Level",
            T_MINION_LEVEL_A + r.LevelBefore + T_MINION_ARROW
            + $"<color=#{ColorUtility.ToHtmlStringRGB(SpaceTheme.GOLD)}>{T_MINION_LEVEL_A}{r.LevelAfter}</color>",
            40, SpaceTheme.TEXT_SUB, FontStyles.Bold);
        level.richText = true;
        Stack(level.rectTransform, ref y, 56f, 14f);
        if (r.LevelBefore < MinionRules.MAX_LEVEL && r.LevelAfter >= MinionRules.MAX_LEVEL)
        {
            var max = SpaceTheme.Text(card, "Max", T_MINION_MAX, 28, SpaceTheme.MINT, FontStyles.Bold);
            Wrap(max);
            Stack(max.rectTransform, ref y, Measure(max, 40f), 6f);
        }
        Finish(card, root, ref y);
        return root;
    }

    /// <summary>
    /// 帰ってから1週間すぎて、ミニオンが持ち帰った物を食べてしまった（失敗した旅で何も持っていなかったミニオンは出さない。
    /// 出すものがなければ null）。いちばん多くて3体を並べ、残りは「ほか n 体も」
    /// </summary>
    public static GameObject MinionsAte(List<MinionStore.Ate> ate)
    {
        var list = ate?.Where(a => a != null && a.Minion != null && !string.IsNullOrEmpty(a.Reward)).ToList();
        if (list == null || list.Count == 0) return null;
        var card = Overlay("__MinionsAteModal", out var root, SpaceTheme.A(SpaceTheme.DANGER, 0.5f));
        if (card == null) return null;

        float y = 48f;
        var caption = SpaceTheme.Text(card, "Caption", T_MINION_CAPTION, 24, SpaceTheme.A(SpaceTheme.MINT, 0.95f), FontStyles.Bold);
        SpaceTheme.Tracking(caption, 18f);
        Stack(caption.rectTransform, ref y, 36f, 0f);
        var title = SpaceTheme.Text(card, "Title", T_MINION_ATE_TITLE, 40, SpaceTheme.TEXT, FontStyles.Bold);
        Wrap(title);
        Stack(title.rectTransform, ref y, Measure(title, 56f), 8f);

        const float ROW = 170f, PIC = 150f;
        int shown = Mathf.Min(3, list.Count);
        for (int i = 0; i < shown; i++)
        {
            var a = list[i];
            var row = SpaceTheme.Rect("Row" + i, card);
            Stack(row, ref y, ROW, i == 0 ? 20f : 10f);
            var color = MinionArt.ColorOf(a.Minion.Element);
            var g = SpaceTheme.Rect("Glow", row, new Vector2(0f, 0.5f), new Vector2(PIC * 0.5f, 0f), new Vector2(PIC * 1.4f, PIC * 1.4f));
            SpaceTheme.Img(g, SpaceSprites.Glow, SpaceTheme.A(color, 0.25f));
            var pic = SpaceTheme.Rect("Minion", row, new Vector2(0f, 0.5f), new Vector2(PIC * 0.5f, 0f), new Vector2(PIC, PIC));
            var img = SpaceTheme.Img(pic, MinionArt.Sprite(a.Minion), Color.white);
            img.preserveAspect = true;
            var text = SpaceTheme.Text(row, "Text",
                SpaceTheme.SafeNow(a.Name ?? MinionRules.NameOf(a.Minion)) + T_MINION_ATE_A
                + $"<color=#{ColorUtility.ToHtmlStringRGB(SpaceTheme.GOLD)}>{MinionStore.RewardName(a.Reward)}</color>" + T_MINION_ATE_B,
                28, SpaceTheme.TEXT, new Vector2(0f, 0.5f), new Vector2(PIC + 24f, 18f), new Vector2(INNER - PIC - 24f, 96f),
                FontStyles.Bold, TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
            text.richText = true;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.enableAutoSizing = true;
            text.fontSizeMin = 20f;
            text.fontSizeMax = 28f;
            var lv = SpaceTheme.Text(row, "Level",
                T_MINION_LEVEL_A + a.LevelBefore + T_MINION_ARROW
                + $"<color=#{ColorUtility.ToHtmlStringRGB(SpaceTheme.GOLD)}>{T_MINION_LEVEL_A}{a.LevelAfter}</color>",
                28, SpaceTheme.TEXT_SUB, new Vector2(0f, 0.5f), new Vector2(PIC + 24f, -50f), new Vector2(INNER - PIC - 24f, 40f),
                FontStyles.Bold, TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
            lv.richText = true;
        }
        if (list.Count > shown)
        {
            var more = SpaceTheme.Text(card, "More", "ほか " + (list.Count - shown) + T_MINION_ATE_MORE_B, 26, SpaceTheme.TEXT_SUB,
                FontStyles.Bold);
            Stack(more.rectTransform, ref y, 40f, 8f);
        }
        var note = SpaceTheme.Text(card, "Note", T_MINION_ATE_NOTE, 24, SpaceTheme.TEXT_MUTED, FontStyles.Bold);
        Wrap(note);
        Stack(note.rectTransform, ref y, Measure(note, 36f), 18f);
        Finish(card, root, ref y);
        return root;
    }
}
