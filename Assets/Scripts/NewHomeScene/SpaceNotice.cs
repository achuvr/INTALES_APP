using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// QR読み取り後などに出るお知らせの宇宙デザイン版（コード生成）。
/// 旧画面と共用の InfoModal / CheckOutModal / BuffCardModal は、新ホーム表示中（SpaceTheme.InUse）だけ
/// ここに処理を回す。呼び出し側（CallMethodFromQR など）の文言はそのまま使い、見た目だけを変える。
///
/// ルートのオブジェクト名は旧版と同じにしている（"__InfoModal" など）。
/// 名前で存在を確かめている処理（新ホームのクーポン入手通知の重複抑止など）がそのまま動くように。
/// </summary>
public static class SpaceNotice
{
    private const float WIDTH = 880f;
    private const float INNER = 780f;

    private const string T_CLOSE = "とじる";
    private const string T_THANKS = "ご来店ありがとうございました！";
    private const string T_STAY = "本日の滞在時間";
    private const string T_STAY_BONUS = "★ 5時間以上のご滞在ボーナス ★";
    private const string T_LEVEL_UP = " がレベルアップ！";
    private const string T_CARD_TITLE = "カードを引いた！";
    private const string T_CARD_NOTE = "次のボス戦で自動的に発動します";
    private const string T_PLANET_CAPTION = "NEW PLANET";
    private const string T_PLANET_USE = "この惑星にする";
    private const string T_PLANET_LATER = "あとで";
    private const string T_PASS_CAPTION = "MONTHLY PASS";
    private const string T_PASS_TITLE = "パスを受け取りました！";
    private const string T_PASS_ALREADY = "このパスは受け取り済みです";
    private const string T_PASS_UNTIL = "まで遊び放題です";
    private const string T_PASS_HINT = "所持品からいつでも表示できます";

    // 退店（ご来店ありがとうございました）は全体を黄色にする（2026-11 ユーザー指定）。
    // ほかのお知らせの紺のガラスと違い、明るい黄色のカードにこげ茶の文字・ボタン
    private static readonly Color CO_CARD_TOP = SpaceTheme.Hex(0xFFE88A);     // カードの上（明るい黄）
    private static readonly Color CO_CARD_BOTTOM = SpaceTheme.Hex(0xFFCF3F);  // カードの下（濃い黄）
    private static readonly Color CO_STROKE = SpaceTheme.Hex(0xFFF6CF, 0.85f); // カードの縁
    private static readonly Color CO_INK = SpaceTheme.Hex(0x3A2705);          // 文字（こげ茶）
    private static readonly Color CO_INK_SUB = SpaceTheme.Hex(0x7A5A1E);      // 補足・区切り線
    private static readonly Color CO_ORANGE = SpaceTheme.Hex(0xB86A00);       // ボーナス・レベル
    private static readonly Color CO_ACCENT = SpaceTheme.Hex(0xE89A10);       // 紋章の光・軌道・衛星
    private static readonly Color CO_BUTTON_TOP = SpaceTheme.Hex(0x4D360C);
    private static readonly Color CO_BUTTON_BOTTOM = SpaceTheme.Hex(0x2B1C05);
    private static readonly Color CO_BUTTON_GLOW = SpaceTheme.Hex(0xFFD84A);
    private static readonly Color CO_BUTTON_TEXT = SpaceTheme.Hex(0xFFEFA6);

    /// <summary>お知らせの定型文（NewHome の起動時にまとめてフォントへ登録する）</summary>
    public static string GlyphText => new StringBuilder()
        .Append(T_CLOSE).Append(T_THANKS).Append(T_STAY).Append(T_STAY_BONUS).Append(T_LEVEL_UP)
        .Append(T_CARD_TITLE).Append(T_CARD_NOTE).Append(T_PLANET_CAPTION).Append(T_PLANET_USE)
        .Append(T_PLANET_LATER).Append(T_PASS_CAPTION).Append(T_PASS_TITLE).Append(T_PASS_ALREADY)
        .Append(T_PASS_UNTIL).Append(T_PASS_HINT).Append("（）/").Append("Lv→0123456789").ToString();

    /// <summary>開いているお知らせ（戻るボタンで手前から閉じる用）</summary>
    private static readonly List<GameObject> _open = new List<GameObject>();

    /// <summary>お知らせが1つでも開いているか（ボタンで閉じられたものは数えない）</summary>
    public static bool AnyOpen
    {
        get
        {
            for (int i = _open.Count - 1; i >= 0; i--)
            {
                if (_open[i] != null) return true;
                _open.RemoveAt(i);
            }
            return false;
        }
    }

    /// <summary>一番手前のお知らせを閉じる。閉じたら true（Android の戻るボタン用）</summary>
    public static bool CloseTop()
    {
        for (int i = _open.Count - 1; i >= 0; i--)
        {
            var go = _open[i];
            _open.RemoveAt(i);
            if (go == null) continue; // ボタンなどで既に閉じられている
            Object.Destroy(go);
            return true;
        }
        return false;
    }

    // ================================================================
    // 汎用のお知らせ（InfoModal の代わり）
    // ================================================================
    /// <summary>
    /// タイトル＋強調テキスト＋補足テキストのお知らせ。
    /// 例: 入店ボーナス、会員証の引き継ぎ完了、イベントガチャの結果。
    /// </summary>
    public static GameObject Info(string title, string strong, string sub = null)
    {
        var card = Overlay("__InfoModal", out var root, SpaceTheme.A(SpaceTheme.CYAN, 0.38f));
        if (card == null) return null;

        float y = 56f;
        var emblem = Emblem(card, SpaceTheme.CYAN, SpaceSprites.Sparkle, 150f, Color.white);
        Stack(emblem, ref y, 150f, 0f, 150f);

        var t = SpaceTheme.Text(card, "Title", SpaceTheme.SafeNow(title), 40, SpaceTheme.TEXT, FontStyles.Bold);
        Wrap(t);
        Stack(t.rectTransform, ref y, Measure(t, 56f), 24f);
        Stack(Divider(card), ref y, 2f, 16f, INNER - 120f);

        var s = SpaceTheme.Text(card, "Strong", SpaceTheme.SafeNow(strong), 54, SpaceTheme.GOLD, FontStyles.Bold);
        Wrap(s);
        s.lineSpacing = -8f;
        Stack(s.rectTransform, ref y, Measure(s, 72f), 28f);

        if (!string.IsNullOrEmpty(sub))
        {
            var b = SpaceTheme.Text(card, "Sub", SpaceTheme.SafeNow(sub), 30, SpaceTheme.TEXT_SUB, FontStyles.Bold);
            Wrap(b);
            Stack(b.rectTransform, ref y, Measure(b, 44f), 20f);
        }

        Finish(card, root, ref y);
        return root;
    }

    // ================================================================
    // 退店（CheckOutModal の代わり）
    // ================================================================
    /// <summary>
    /// 退店時の滞在時間。levelUpName があれば 5時間以上滞在のレベルアップも表示する。
    /// 全体を黄色にしている（明るい黄色のカード＋こげ茶の文字・ボタン。紋章の中央は金色の小さな惑星）。
    /// </summary>
    public static GameObject CheckOut(string stayText, string levelUpName, int oldLevel, int newLevel)
    {
        bool bonus = !string.IsNullOrEmpty(levelUpName);
        var card = Overlay("__CheckOutModal", out var root, CO_STROKE, Color.white);
        if (card == null) return null;
        SpaceTheme.Gradient(card.GetComponent<Image>(), CO_CARD_TOP, CO_CARD_BOTTOM);

        float y = 56f;
        var emblem = Emblem(card, CO_ACCENT, SpaceSprites.GoldMoon, 104f, Color.white);
        Stack(emblem, ref y, 150f, 0f, 150f);

        var t = SpaceTheme.Text(card, "Title", T_THANKS, 40, CO_INK, FontStyles.Bold);
        Stack(t.rectTransform, ref y, 56f, 24f);
        Stack(Divider(card, SpaceTheme.A(CO_INK_SUB, 0.45f)), ref y, 2f, 16f, INNER - 120f);

        var label = SpaceTheme.Text(card, "StayLabel", T_STAY, 28, CO_INK_SUB, FontStyles.Bold);
        Stack(label.rectTransform, ref y, 40f, 26f);
        var time = SpaceTheme.Text(card, "Stay", StayRichText(stayText), 84, CO_INK, FontStyles.Bold);
        Stack(time.rectTransform, ref y, 104f, 4f);

        if (bonus)
        {
            Stack(Divider(card, SpaceTheme.A(CO_INK_SUB, 0.45f)), ref y, 2f, 26f, INNER - 120f);
            var b = SpaceTheme.Text(card, "Bonus", T_STAY_BONUS, 28, CO_ORANGE, FontStyles.Bold);
            Stack(b.rectTransform, ref y, 42f, 24f);
            var n = SpaceTheme.Text(card, "LevelUp", SpaceTheme.SafeNow(levelUpName) + T_LEVEL_UP, 34,
                CO_INK, FontStyles.Bold);
            Wrap(n);
            Stack(n.rectTransform, ref y, Measure(n, 50f), 8f);
            var lv = SpaceTheme.Text(card, "Level", $"Lv{oldLevel} → Lv{newLevel}", 64, CO_ORANGE,
                FontStyles.Bold);
            Stack(lv.rectTransform, ref y, 84f, 4f);
        }

        Finish(card, root, ref y, CO_BUTTON_TOP, CO_BUTTON_BOTTOM, CO_BUTTON_GLOW, CO_BUTTON_TEXT);
        return root;
    }

    /// <summary>"2時間15分" → 数字を大きく・単位を小さく（0時間は省く）</summary>
    private static string StayRichText(string stay)
    {
        if (string.IsNullOrEmpty(stay)) return "ー";
        if (stay.StartsWith("0時間")) stay = stay.Substring(3);
        return Regex.Replace(SpaceTheme.SafeNow(stay), @"(\D+)", "<size=50%>$1</size>");
    }

    // ================================================================
    // バフカード（BuffCardModal の代わり）
    // ================================================================
    public static GameObject BuffCard(BuffCardType cardType)
    {
        var card = Overlay("__BuffCardModal", out var root, SpaceTheme.A(SpaceTheme.GOLD, 0.38f));
        if (card == null) return null;

        float y = 56f;
        var t = SpaceTheme.Text(card, "Title", T_CARD_TITLE, 44, SpaceTheme.TEXT, FontStyles.Bold);
        Stack(t.rectTransform, ref y, 64f, 0f);

        // カード画像（縦横比を保って 520 四方に収め、角丸で切り抜く）
        var tex = global::BuffCard.LoadImage(cardType);
        var size = new Vector2(520f, 520f);
        if (tex != null && tex.height > 0)
        {
            float aspect = tex.width / (float)tex.height;
            size = aspect >= 1f ? new Vector2(520f, 520f / aspect) : new Vector2(520f * aspect, 520f);
        }
        var frame = SpaceTheme.Rect("Card", card);
        Stack(frame, ref y, size.y, 24f, size.x);
        var glow = SpaceTheme.Rect("__Glow", card);
        glow.anchorMin = glow.anchorMax = frame.anchorMin;
        glow.pivot = SpaceTheme.CENTER;
        glow.anchoredPosition = frame.anchoredPosition + new Vector2(0f, -size.y / 2f);
        glow.sizeDelta = size + new Vector2(220f, 220f);
        glow.SetSiblingIndex(frame.GetSiblingIndex());
        SpaceTheme.Img(glow, SpaceSprites.Glow, SpaceTheme.A(SpaceTheme.GOLD, 0.22f));
        SpaceTheme.Rounded(frame, SpaceTheme.ICON_BASE, 28f);
        frame.gameObject.AddComponent<Mask>().showMaskGraphic = true;
        var pic = SpaceTheme.Rect("Picture", frame);
        SpaceTheme.Stretch(pic);
        var raw = pic.gameObject.AddComponent<RawImage>();
        raw.raycastTarget = false;
        if (tex != null) raw.texture = tex;
        else raw.color = new Color(0.25f, 0.27f, 0.45f);
        var edge = SpaceTheme.Rect("Edge", card);
        edge.anchorMin = edge.anchorMax = frame.anchorMin;
        edge.pivot = frame.pivot;
        edge.anchoredPosition = frame.anchoredPosition;
        edge.sizeDelta = size;
        SpaceTheme.RoundedStroke(edge, SpaceTheme.A(SpaceTheme.GOLD, 0.45f), 28f);

        var name = SpaceTheme.Text(card, "Name", SpaceTheme.SafeNow(global::BuffCard.DisplayName(cardType)), 48,
            SpaceTheme.GOLD, FontStyles.Bold);
        Stack(name.rectTransform, ref y, 64f, 28f);
        var effect = SpaceTheme.Text(card, "Effect", SpaceTheme.SafeNow(global::BuffCard.EffectText(cardType)), 30,
            SpaceTheme.TEXT_SUB, FontStyles.Bold);
        Wrap(effect);
        Stack(effect.rectTransform, ref y, Measure(effect, 44f), 12f);
        var note = SpaceTheme.Text(card, "Note", T_CARD_NOTE, 26, SpaceTheme.TEXT_MUTED, FontStyles.Bold);
        Stack(note.rectTransform, ref y, 38f, 16f);

        Finish(card, root, ref y);
        return root;
    }

    // ================================================================
    // 惑星の解放（アチーブメント報酬。CallMethodFromQR.UnlockPlanet から）
    // ================================================================
    /// <summary>
    /// 新しい惑星を解放したお祝い。解放した惑星が回って見え、その場で「この惑星にする」も選べる
    /// （SpacePlanets.Current を変えると、新ホームが中央の惑星を入れ替える）。「あとで」・まわりのタップで閉じる。
    /// </summary>
    public static GameObject PlanetUnlocked(SpacePlanetDef def)
    {
        var card = Overlay("__PlanetUnlockModal", out var root, SpaceTheme.A(def.Accent, 0.55f));
        if (card == null) return null;
        // 惑星が自転し続けるので、メインのキャンバスと分ける
        SpaceTheme.SubCanvas(root, true);

        float y = 48f;
        var caption = SpaceTheme.Text(card, "Caption", T_PLANET_CAPTION, 24, SpaceTheme.A(SpaceTheme.CYAN, 0.9f),
            FontStyles.Bold);
        caption.characterSpacing = 18f;
        Stack(caption.rectTransform, ref y, 36f, 0f);
        var title = SpaceTheme.Text(card, "Title", SpacePlanets.T_UNLOCKED_TITLE, 40, SpaceTheme.TEXT, FontStyles.Bold);
        Stack(title.rectTransform, ref y, 56f, 8f);

        // 解放した惑星（後ろでゆっくり回る軌道つき）
        var stage = SpaceTheme.Rect("Planet", card);
        Stack(stage, ref y, 400f, 16f, 400f);
        var orbit = SpaceTheme.Rect("Orbit", stage, SpaceTheme.CENTER, Vector2.zero, new Vector2(420f, 420f));
        SpaceTheme.Img(orbit, SpaceSprites.Ring, SpaceTheme.A(def.Accent, 0.35f));
        var sat = SpaceTheme.Rect("Satellite", orbit, SpaceTheme.CENTER, new Vector2(0f, 210f), new Vector2(36f, 36f));
        SpaceTheme.Img(sat, SpaceSprites.Glow, SpaceTheme.A(def.Accent, 0.95f));
        orbit.gameObject.AddComponent<SpaceSpinner>().DegreesPerSecond = -30f;
        SpacePlanetView.Create(stage, def, 400f, SpacePlanetView.Quality.Large, true);

        var en = SpaceTheme.Text(card, "En", def.En, 26, SpaceTheme.A(def.Accent, 0.95f), FontStyles.Bold);
        en.characterSpacing = 24f;
        Stack(en.rectTransform, ref y, 36f, 16f);
        var name = SpaceTheme.Text(card, "Name", def.Name, 60, SpaceTheme.TEXT, FontStyles.Bold);
        Stack(name.rectTransform, ref y, 78f, 0f);
        var desc = SpaceTheme.Text(card, "Desc", def.Desc, 30, SpaceTheme.TEXT_SUB, FontStyles.Bold);
        Stack(desc.rectTransform, ref y, 44f, 4f);

        var use = SpaceTheme.Rect("Use", card);
        Stack(use, ref y, 128f, 40f, 560f);
        var face = SpaceTheme.PrimaryFace(use);
        SpaceTheme.Clickable(use.gameObject, face, () =>
        {
            SpacePlanets.Current = def;
            Object.Destroy(root);
        });
        var useLabel = SpaceTheme.Text(use, "Label", T_PLANET_USE, 40, SpaceTheme.TEXT_DARK, FontStyles.Bold);
        SpaceTheme.Stretch(useLabel.rectTransform);

        var later = SpaceTheme.Rect("Later", card);
        Stack(later, ref y, 76f, 12f, 320f);
        var laterHit = SpaceTheme.HitArea(later);
        SpaceTheme.Clickable(later.gameObject, laterHit, () => Object.Destroy(root));
        var laterLabel = SpaceTheme.Text(later, "Label", T_PLANET_LATER, 32, SpaceTheme.TEXT_SUB, FontStyles.Bold);
        SpaceTheme.Stretch(laterLabel.rectTransform);

        var hint = SpaceTheme.Text(card, "Hint", SpacePlanets.T_HOW_TO_CHOOSE, 24, SpaceTheme.TEXT_MUTED, FontStyles.Bold);
        Stack(hint.rectTransform, ref y, 34f, 4f);

        y += 44f;
        card.sizeDelta = new Vector2(WIDTH, y);
        root.AddComponent<SpacePopIn>().Target = card;
        return root;
    }

    // ================================================================
    // 1か月遊び放題パスを受け取った
    // ================================================================
    /// <summary>
    /// 1か月遊び放題パスを受け取ったお知らせ。受け取ったカード（光の帯が走る）と、いつまで遊び放題か。
    /// already: 自分がすでに受け取っていたパスのQRをもう一度読んだ
    /// </summary>
    public static GameObject PassReceived(PassInfo pass, bool already)
    {
        var card = Overlay("__PassReceivedModal", out var root, SpaceTheme.A(SpacePassCard.GlowColor(pass.Type), 0.55f));
        if (card == null) return null;
        SpaceTheme.SubCanvas(root, true); // カードの光の帯が動き続けるので、メインのキャンバスと分ける

        float y = 48f;
        var caption = SpaceTheme.Text(card, "Caption", T_PASS_CAPTION, 24,
            SpaceTheme.A(SpacePassCard.FoilColor(pass.Type), 0.95f), FontStyles.Bold);
        caption.characterSpacing = 18f;
        Stack(caption.rectTransform, ref y, 36f, 0f);
        var title = SpaceTheme.Text(card, "Title", already ? T_PASS_ALREADY : T_PASS_TITLE, 40, SpaceTheme.TEXT,
            FontStyles.Bold);
        Stack(title.rectTransform, ref y, 56f, 8f);

        var stage = SpaceTheme.Rect("Pass", card);
        Stack(stage, ref y, INNER / SpacePassCard.ASPECT, 32f, INNER);
        SpacePassCard.Create(stage, pass.Type, pass, INNER, true, true);

        var desc = SpaceTheme.Text(card, "Desc",
            $"{StorePasses.DateText(pass.Expiry)}{T_PASS_UNTIL}（{pass.Type.Label}）", 32, SpaceTheme.TEXT, FontStyles.Bold);
        desc.enableAutoSizing = true;
        desc.fontSizeMin = 24f;
        desc.fontSizeMax = 32f;
        Stack(desc.rectTransform, ref y, 46f, 36f);
        var hint = SpaceTheme.Text(card, "Hint", T_PASS_HINT, 26, SpaceTheme.TEXT_SUB, FontStyles.Bold);
        Stack(hint.rectTransform, ref y, 38f, 8f);

        Finish(card, root, ref y);
        return root;
    }

    // ================================================================
    // 共通の組み立て
    // ================================================================
    /// <summary>暗幕（タップで閉じる）＋カード。カードの中身は上から Stack で積む。fill を省くと紺のガラス</summary>
    private static RectTransform Overlay(string name, out GameObject rootGo, Color stroke, Color? fill = null)
    {
        rootGo = null;
        var canvas = SpaceTheme.MainCanvas();
        if (canvas == null)
        {
            Debug.LogWarning($"[SpaceNotice] Canvasが見つかりません: {name}");
            return null;
        }

        var root = SpaceTheme.Rect(name, canvas.transform);
        SpaceTheme.Stretch(root);
        root.SetAsLastSibling();
        var go = root.gameObject;
        rootGo = go;

        var dim = SpaceTheme.Rect("Dim", root);
        SpaceTheme.Stretch(dim);
        var dimImg = SpaceTheme.Img(dim, null, SpaceTheme.DIM, true);
        SpaceTheme.Clickable(dim.gameObject, dimImg, () => Object.Destroy(go), false);

        var card = SpaceTheme.Rect("Card", root, SpaceTheme.CENTER, Vector2.zero, new Vector2(WIDTH, 600f));
        SpaceTheme.Glass(card, 56f, fill ?? SpaceTheme.GLASS_DEEP, stroke, true); // カード上のタップは暗幕へ抜けない
        _open.Add(go);
        return card;
    }

    /// <summary>
    /// 「とじる」を足して高さを確定し、出現演出を付ける。
    /// ボタンの色を省くといつもの主ボタン（シアン→すみれ色）、指定すると上→下のその2色（退店の黄色いカード用）
    /// </summary>
    private static void Finish(RectTransform card, GameObject root, ref float y, Color? faceTop = null,
        Color? faceBottom = null, Color? faceGlow = null, Color? labelColor = null)
    {
        var close = SpaceTheme.Rect("Close", card);
        Stack(close, ref y, 128f, 44f, 440f);
        var face = faceTop.HasValue
            ? SpaceTheme.GradientFace(close, faceTop.Value, faceBottom ?? faceTop.Value, faceGlow ?? faceTop.Value)
            : SpaceTheme.PrimaryFace(close);
        SpaceTheme.Clickable(close.gameObject, face, () => Object.Destroy(root));
        var label = SpaceTheme.Text(close, "Label", T_CLOSE, 40, labelColor ?? SpaceTheme.TEXT_DARK, FontStyles.Bold);
        SpaceTheme.Stretch(label.rectTransform);

        y += 56f;
        card.sizeDelta = new Vector2(WIDTH, y);
        root.AddComponent<SpacePopIn>().Target = card;
    }

    /// <summary>
    /// カード上端から順に、中央そろえで縦に積む（y は上端からの距離で、積んだ分だけ進む）。
    /// width を省略すると本文の幅（INNER）。
    /// </summary>
    private static void Stack(RectTransform rt, ref float y, float height, float gapBefore, float width = INNER)
    {
        y += gapBefore;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -y);
        rt.sizeDelta = new Vector2(width, height);
        y += height;
    }

    private static void Wrap(TextMeshProUGUI t) => t.textWrappingMode = TextWrappingModes.Normal;

    /// <summary>折り返し後の高さ（最低 minHeight）</summary>
    private static float Measure(TextMeshProUGUI t, float minHeight) =>
        Mathf.Max(minHeight, t.GetPreferredValues(t.text, INNER, 0f).y + 6f);

    /// <summary>区切り線（色を省くといつもの青白い線）</summary>
    private static RectTransform Divider(RectTransform card, Color? color = null)
    {
        var rt = SpaceTheme.Rect("Divider", card);
        SpaceTheme.Img(rt, SpaceSprites.FadeLine, color ?? SpaceTheme.A(SpaceTheme.LINE, 0.32f));
        return rt;
    }

    /// <summary>お知らせの上に置く紋章（光＋回る軌道と衛星＋中央の絵）</summary>
    private static RectTransform Emblem(RectTransform card, Color accent, Sprite center, float centerSize,
        Color centerColor)
    {
        var rt = SpaceTheme.Rect("Emblem", card);
        var glow = SpaceTheme.Rect("Glow", rt, SpaceTheme.CENTER, Vector2.zero, new Vector2(300f, 300f));
        SpaceTheme.Img(glow, SpaceSprites.Glow, SpaceTheme.A(accent, 0.24f));
        var orbit = SpaceTheme.Rect("Orbit", rt, SpaceTheme.CENTER, Vector2.zero, new Vector2(150f, 150f));
        SpaceTheme.Img(orbit, SpaceSprites.RingSmall, SpaceTheme.A(accent, 0.55f));
        var sat = SpaceTheme.Rect("Satellite", orbit, SpaceTheme.CENTER, new Vector2(0f, 74f), new Vector2(34f, 34f));
        SpaceTheme.Img(sat, SpaceSprites.Glow, SpaceTheme.A(accent, 0.95f));
        var core = SpaceTheme.Rect("Core", sat, SpaceTheme.CENTER, Vector2.zero, new Vector2(10f, 10f));
        SpaceTheme.Img(core, SpaceSprites.Circle, Color.white);
        orbit.gameObject.AddComponent<SpaceSpinner>().DegreesPerSecond = -40f;
        var halo = SpaceTheme.Rect("CenterGlow", rt, SpaceTheme.CENTER, Vector2.zero, new Vector2(110f, 110f));
        SpaceTheme.Img(halo, SpaceSprites.Glow, SpaceTheme.A(accent, 0.55f));
        var pic = SpaceTheme.Rect("Center", rt, SpaceTheme.CENTER, Vector2.zero, new Vector2(centerSize, centerSize));
        SpaceTheme.Img(pic, center, centerColor);
        return rt;
    }
}
