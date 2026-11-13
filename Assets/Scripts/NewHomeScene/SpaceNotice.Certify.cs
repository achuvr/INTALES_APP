using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;

/// <summary>ボードゲームの公認のお知らせ（公認されたゲーム名と、灯った星座）</summary>
public static partial class SpaceNotice
{
    private const string T_CERT_CAPTION = "ACHIEVEMENT";
    private const string T_CERT_TITLE = "アチーブメント達成！";
    private const string T_CERT_RELIGHT = "もう一度星を灯しました！";
    private const string T_CERT_NONE = "図鑑にアチーブメントの印が付きました（このゲームのジャンルには星座がありません）";
    private const string T_CERT_STAR = "星が灯りました";
    private const string T_CERT_DONE = "が完成しました！";
    private const string T_CERT_SEE = "星座を見る";
    private const string T_CERT_COMPLETE = "完成";
    private const string T_CERT_LEVEL = "に上がりました！もっと輝きます";
    private const string T_CERT_NEXT = "次のレベルまで";
    private const string T_CERT_REWARD_CAPTION = "星座のボーナス";
    private const string T_CERT_REWARD_A = "を ";
    private const string T_CERT_REWARD_B = "枚 入手しました";
    private const string T_CERT_REWARD_NOTE = "星座が完成・レベルアップするごとに、チケットが1枚もらえます";

    public static string CertifyGlyphText => new StringBuilder()
        .Append(T_CERT_COMPLETE).Append(T_CERT_RELIGHT).Append(T_CERT_LEVEL).Append(T_CERT_NEXT).Append("Lv.").Append(T_CERT_REWARD_CAPTION).Append(T_CERT_REWARD_A).Append(T_CERT_REWARD_B)
        .Append(T_CERT_REWARD_NOTE).Append(T_CERT_CAPTION).Append(T_CERT_TITLE).Append(T_CERT_NONE).Append(T_CERT_STAR).Append(T_CERT_DONE)
        .Append(T_CERT_SEE).Append("0123456789/ ★").Append(StarGiftQr.GlyphText).ToString();

    /// <summary>
    /// 公認のお知らせ。after は公認した後の進み具合（そのゲームが入る星座だけ。最大3つ表示）。
    /// rewards は星座の完成・レベルアップでもらえたチケット（フィールド名 → 枚数。種類ごとに1つ欄を出す）。
    /// お店から星を灯すQR（StarGiftQr。2026-11-13）でも使う: 見出しを captionText・titleText に替え、
    /// 灯す前のレベル levelBefore を渡す（一度に何個も灯して、ちょうど6の倍数でなくても「完成しました」と出すため）
    /// </summary>
    public static GameObject GameCertified(string title, List<ConstellationProgress> after,
        IReadOnlyDictionary<string, int> rewards, bool relight = false, string noneText = null,
        string captionText = null, string titleText = null, int levelBefore = -1)
    {
        var accent = after.Count > 0 ? after[0].Def.Color : SpaceTheme.GOLD;
        var card = Overlay("__CertifyModal", out var root, SpaceTheme.A(accent, 0.5f));
        if (card == null) return null;
        SpaceTheme.SubCanvas(root, true); // 星がまたたくので分ける

        float y = 48f;
        var caption = SpaceTheme.Text(card, "Caption", captionText ?? T_CERT_CAPTION, 24, SpaceTheme.A(SpaceTheme.GOLD, 0.9f),
            FontStyles.Bold);
        SpaceTheme.Tracking(caption, 18f);
        Stack(caption.rectTransform, ref y, 36f, 0f);
        var t = SpaceTheme.Text(card, "Title", titleText ?? (relight ? T_CERT_RELIGHT : T_CERT_TITLE), 40, SpaceTheme.TEXT,
            FontStyles.Bold);
        Stack(t.rectTransform, ref y, 56f, 8f);
        Stack(Divider(card), ref y, 2f, 16f, INNER - 120f);

        var name = SpaceTheme.Text(card, "Game", SpaceTheme.NoParse(SpaceTheme.SafeNow(title)), 50, SpaceTheme.GOLD,
            FontStyles.Bold);
        Wrap(name);
        name.lineSpacing = -8f;
        Stack(name.rectTransform, ref y, Measure(name, 66f), 24f);

        if (after.Count == 0)
        {
            var none = SpaceTheme.Text(card, "None", noneText ?? T_CERT_NONE, 28, SpaceTheme.TEXT_SUB, FontStyles.Bold);
            Wrap(none);
            Stack(none.rectTransform, ref y, Measure(none, 40f), 20f);
        }
        else
        {
            // 星座ごとに1段: 左に小さな星座の絵、右に名前と「3 / 6」（完成なら金色で「○○座が完成しました！」）
            int shown = Mathf.Min(3, after.Count);
            for (int i = 0; i < shown; i++)
            {
                var p = after[i];
                var row = SpaceTheme.Rect("Row", card);
                Stack(row, ref y, 200f, i == 0 ? 24f : 12f);
                // 今回ちょうど完成・レベルアップしたか（公認は1つずつなので6の倍数になったとき。お店から灯す星は前のレベルと比べる）
                bool leveled = levelBefore >= 0 ? p.Level > levelBefore : p.JustLeveled;
                bool completed = levelBefore >= 0 ? levelBefore == 0 && p.Level >= 1 : p.JustLeveled && p.Level == 1;
                var bg = SpaceTheme.Rect("Bg", row);
                SpaceTheme.Stretch(bg);
                SpaceTheme.Glass(bg, 32f, new Color(0.07f, 0.09f, 0.24f, 0.7f),
                    leveled ? SpaceTheme.A(p.Def.Color, 0.8f) : null);
                var fig = SpaceTheme.Rect("Figure", row, new Vector2(0f, 0.5f), new Vector2(110f, 0f), new Vector2(180f, 180f));
                SpaceConstellationPage.Figure(fig, p.Def, p.Count, 180f);
                SpaceTheme.Text(row, "Name", p.Def.Name, 38, SpaceTheme.TEXT, new Vector2(0f, 0.5f), new Vector2(240f, 34f),
                    new Vector2(500f, 50f), FontStyles.Bold, TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
                // 今回ちょうど完成「○○座が完成しました！」／ちょうどレベルアップ「Lv.2 に上がりました！もっと輝きます」
                // ／それ以外は次まで「星が灯りました 3 / 6」「★ Lv.1　次のレベルまで 2 / 6」
                int n = p.Def.Size;
                string sub = !p.Complete ? $"{T_CERT_STAR}　{p.Partial} / {n}"
                    : completed ? "★ " + p.Def.Name + T_CERT_DONE
                    : leveled ? $"★ Lv.{p.Level} {T_CERT_LEVEL}"
                    : $"★ Lv.{p.Level}　{T_CERT_NEXT} {p.Partial} / {n}";
                var s = SpaceTheme.Text(row, "State", sub, 28, p.Complete ? SpaceTheme.GOLD : SpaceTheme.A(p.Def.Color, 0.95f),
                    new Vector2(0f, 0.5f), new Vector2(240f, -28f), new Vector2(510f, 44f), FontStyles.Bold,
                    TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
                s.enableAutoSizing = true;
                s.fontSizeMin = 20f;
                s.fontSizeMax = 28f;
            }
        }

        // 星座のボーナス（完成・レベルアップ。チケットの種類ごとに、絵＋枚数）
        bool firstReward = true;
        foreach (var reward in rewards ?? new Dictionary<string, int>())
        {
            if (reward.Value <= 0) continue;
            var coupon = SpaceCoupons.ByField(reward.Key);
            var box = SpaceTheme.Rect("Reward", card);
            Stack(box, ref y, 200f, firstReward ? 24f : 12f);
            firstReward = false;
            SpaceTheme.Glass(box, 32f, new Color(0.18f, 0.14f, 0.05f, 0.75f), SpaceTheme.A(SpaceTheme.GOLD, 0.8f));
            var glow = SpaceTheme.Rect("Glow", box, new Vector2(0f, 0.5f), new Vector2(110f, 0f), new Vector2(260f, 260f));
            SpaceTheme.Img(glow, SpaceSprites.Glow, SpaceTheme.A(SpaceTheme.GOLD, 0.35f));
            Sprite icon = coupon?.Icon();
            SpaceInventoryPanel.RoundedIcon(box, icon, new Vector2(0f, 0.5f), new Vector2(110f, 0f), 140f, 30f);
            var cap = SpaceTheme.Text(box, "Caption", T_CERT_REWARD_CAPTION, 26, SpaceTheme.GOLD, new Vector2(0f, 0.5f),
                new Vector2(220f, 52f), new Vector2(530f, 40f), FontStyles.Bold, TextAlignmentOptions.Left,
                new Vector2(0f, 0.5f));
            SpaceTheme.Text(box, "Text", SpaceCoupons.NameOf(reward.Key) + T_CERT_REWARD_A + reward.Value + T_CERT_REWARD_B,
                34, SpaceTheme.TEXT,
                new Vector2(0f, 0.5f), new Vector2(220f, 4f), new Vector2(530f, 48f), FontStyles.Bold,
                TextAlignmentOptions.Left, new Vector2(0f, 0.5f)).enableAutoSizing = true;
            var note = SpaceTheme.Text(box, "Note", T_CERT_REWARD_NOTE, 20, SpaceTheme.TEXT_SUB, new Vector2(0f, 0.5f),
                new Vector2(220f, -50f), new Vector2(530f, 34f), FontStyles.Bold, TextAlignmentOptions.Left,
                new Vector2(0f, 0.5f));
            note.enableAutoSizing = true;
            note.fontSizeMin = 16f;
            note.fontSizeMax = 20f;
            cap.enableAutoSizing = true;
            cap.fontSizeMin = 20f;
            cap.fontSizeMax = 26f;
        }

        // 星座を見る（主ボタン）＋とじる
        var see = SpaceTheme.Rect("See", card);
        Stack(see, ref y, 128f, 40f, 560f);
        var face = SpaceTheme.PrimaryFace(see);
        SpaceTheme.Clickable(see.gameObject, face, () =>
        {
            Object.Destroy(root);
            SpaceConstellationPage.Open();
        });
        var seeLabel = SpaceTheme.Text(see, "Label", T_CERT_SEE, 40, SpaceTheme.TEXT_DARK, FontStyles.Bold);
        SpaceTheme.Stretch(seeLabel.rectTransform);

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
}
