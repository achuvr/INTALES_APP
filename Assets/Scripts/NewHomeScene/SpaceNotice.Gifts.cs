using TMPro;
using UnityEngine;

/// <summary>贈りもののお知らせ（誕生月の特典・友だち紹介の特典。2026-11-07）</summary>
public static partial class SpaceNotice
{
    private const string T_GIFT_CAPTION = "GIFT";
    public const string T_BIRTHDAY_TITLE = "お誕生月おめでとうございます！";
    public const string T_BIRTHDAY_BODY = "ささやかな贈りものです";
    public const string T_REFERRAL_TITLE_NEW = "ようこそ INTALES Cafe へ！";
    public const string T_REFERRAL_BODY_NEW = "紹介で来てくれた特典です";
    public const string T_REFERRAL_TITLE_FROM = "紹介した友だちが来店しました！";
    public const string T_REFERRAL_BODY_FROM = "さんが来てくれました。紹介の特典です";
    private const string T_GIFT_GOT = " を1枚 プレゼント";

    public static string GiftGlyphText => T_GIFT_CAPTION + T_BIRTHDAY_TITLE + T_BIRTHDAY_BODY + T_REFERRAL_TITLE_NEW
                                         + T_REFERRAL_BODY_NEW + T_REFERRAL_TITLE_FROM + T_REFERRAL_BODY_FROM + T_GIFT_GOT;

    /// <summary>チケットの贈りもの（絵・名前・ひとこと）。accent は枠と光の色</summary>
    public static GameObject Gift(string title, string body, SpaceCouponDef coupon, Color accent)
    {
        var card = Overlay("__GiftModal", out var root, SpaceTheme.A(accent, 0.55f));
        if (card == null) return null;

        float y = 48f;
        var caption = SpaceTheme.Text(card, "Caption", T_GIFT_CAPTION, 24, SpaceTheme.A(accent, 0.95f), FontStyles.Bold);
        SpaceTheme.Tracking(caption, 18f);
        Stack(caption.rectTransform, ref y, 36f, 0f);
        var t = SpaceTheme.Text(card, "Title", title, 40, SpaceTheme.TEXT, FontStyles.Bold);
        t.enableAutoSizing = true;
        t.fontSizeMin = 26f;
        t.fontSizeMax = 40f;
        Stack(t.rectTransform, ref y, 56f, 8f);
        var b = SpaceTheme.Text(card, "Body", body, 28, SpaceTheme.TEXT_SUB, FontStyles.Bold);
        Wrap(b);
        Stack(b.rectTransform, ref y, Measure(b, 40f), 4f);

        var stage = SpaceTheme.Rect("Stage", card);
        Stack(stage, ref y, 260f, 16f);
        var halo = SpaceTheme.Rect("Glow", stage, SpaceTheme.CENTER, Vector2.zero, new Vector2(340f, 340f));
        SpaceTheme.Img(halo, SpaceSprites.Glow, SpaceTheme.A(accent, 0.35f));
        if (coupon != null)
            SpaceInventoryPanel.RoundedIcon(stage, coupon.Icon(), SpaceTheme.CENTER, Vector2.zero, 220f, 44f);

        if (coupon != null)
        {
            var strong = SpaceTheme.Text(card, "Strong",
                $"<color=#{ColorUtility.ToHtmlStringRGB(SpaceTheme.GOLD)}>{coupon.Name}</color>{T_GIFT_GOT}", 38,
                SpaceTheme.TEXT, FontStyles.Bold);
            Wrap(strong);
            Stack(strong.rectTransform, ref y, Measure(strong, 56f), 16f);
        }

        Finish(card, root, ref y);
        return root;
    }
}
