using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// トランプのお知らせ（2026-11-12）: 引いたカードをめくって見せる（CardDrawn）／手札が8枚になったら・山札座で引いたら
/// 捨てるカードを選ぶ（CardDiscard。選ぶまで閉じない）／ポーカーの結果（PokerResult）。カードの決まりは CardRules、保存は CardDeck
/// </summary>
public static partial class SpaceNotice
{
    private const string T_CARD_CAPTION = "TRUMP";
    private const string T_CARD_TITLE_ONE = "トランプを1枚引きました";
    private const string T_CARD_TITLE_A = "トランプを";
    private const string T_CARD_TITLE_B = "枚引きました";
    private const string T_CARD_OVER = "手札が8枚になりました。このあと、捨てるカードを1枚選びます";
    private const string T_DISCARD_TITLE = "捨てるカードを選んでください";
    private const string T_DISCARD_SUB_A = "手札は7枚までです（いま ";
    private const string T_DISCARD_SUB_B = "枚）。捨てたカードは山札に戻りません";
    // 山札座で引いたあと（2026-11-13）
    private const string T_DISCARD_SWAP_SUB = "山札座で1枚引いたので、手札から1枚捨てます。捨てたカードは山札に戻りません";
    private const string T_DISCARD_OK = "このカードを捨てる";
    private const string T_DISCARD_PICK = "カードを1枚選んでください";
    private const string T_DISCARDED = " を捨てました";
    private const string T_DISCARD_FAILED = "捨てられませんでした。通信の状態を確かめて、もう一度押してください";
    private const string T_DISCARD_MARK = "捨てる";
    private const string T_NEW = "NEW";
    private const string T_POKER_CAPTION = "POKER";
    private const string T_POKER_GOT = "をもらいました";
    private const string T_POKER_COUPON_NOTE = "チケットは所持品に入りました";

    /// <summary>このお知らせの定型文（NewHome の起動時にまとめてフォントへ登録する）</summary>
    public static string CardGlyphText => new StringBuilder()
        .Append(T_CARD_CAPTION).Append(T_CARD_TITLE_ONE).Append(T_CARD_TITLE_A).Append(T_CARD_TITLE_B).Append(T_CARD_OVER)
        .Append(T_DISCARD_TITLE).Append(T_DISCARD_SUB_A).Append(T_DISCARD_SUB_B).Append(T_DISCARD_SWAP_SUB).Append(T_DISCARD_OK)
        .Append(T_DISCARD_PICK).Append(T_DISCARDED).Append(T_DISCARD_FAILED).Append(T_DISCARD_MARK).Append(T_NEW)
        .Append(T_POKER_CAPTION).Append(T_POKER_GOT).Append(T_POKER_COUPON_NOTE).Append(T_IG_TOTAL)
        .Append(CardDeck.GlyphText).ToString();

    // ================================================================
    // 引いたカード
    // ================================================================
    /// <summary>
    /// 山札から引いたカードを、裏から表へめくって見せる（入店・ショップ・山札座）。overflow なら「このあと捨てるカードを選ぶ」と添える。
    /// note を渡すと、その一言を代わりに添える（山札座の「このあと、手札から捨てるカードを1枚選びます」）
    /// </summary>
    public static GameObject CardDrawn(List<int> cards, string captionText = null, bool overflow = false,
        Color? captionColor = null, string note = null)
    {
        if (cards == null || cards.Count == 0) return null;
        var card = Overlay("__CardDrawnModal", out var root, SpaceTheme.A(CardDeck.ACCENT, 0.55f));
        if (card == null) return null;
        SpaceTheme.SubCanvas(root, true); // めくる動きをメインのキャンバスの再描画と分ける

        float y = 48f;
        var caption = SpaceTheme.Text(card, "Caption", captionText ?? T_CARD_CAPTION, 24,
            SpaceTheme.A(captionColor ?? SpaceTheme.CYAN, 0.9f), FontStyles.Bold);
        SpaceTheme.Tracking(caption, 18f);
        Stack(caption.rectTransform, ref y, 36f, 0f);
        string titleText = cards.Count == 1 ? T_CARD_TITLE_ONE : T_CARD_TITLE_A + cards.Count + T_CARD_TITLE_B;
        var title = SpaceTheme.Text(card, "Title", titleText, 40, SpaceTheme.TEXT, FontStyles.Bold);
        title.enableAutoSizing = true;
        title.fontSizeMin = 28f;
        title.fontSizeMax = 40f;
        Stack(title.rectTransform, ref y, 56f, 8f);

        // カード（1枚なら大きく。複数なら4枚ずつ並べる）
        int n = Mathf.Min(cards.Count, CardRules.HAND_MAX + 1);
        float w = n == 1 ? 240f : 150f;
        float h = w * CardDeck.ASPECT;
        int perRow = Mathf.Min(n, 4);
        int rows = (n + perRow - 1) / perRow;
        const float GAP = 22f;
        var stage = SpaceTheme.Rect("Cards", card);
        Stack(stage, ref y, rows * h + (rows - 1) * GAP, 28f);
        var glows = GlowLayer(stage);
        var top = new Vector2(0.5f, 1f);
        for (int i = 0; i < n; i++)
        {
            int row = i / perRow, col = i % perRow;
            int inRow = Mathf.Min(perRow, n - row * perRow);
            float rowW = inRow * w + (inRow - 1) * GAP;
            var pos = new Vector2(-rowW * 0.5f + w * 0.5f + col * (w + GAP), -(h * 0.5f + row * (h + GAP)));
            var slot = SpaceTheme.Rect("Slot" + i, stage, top, pos, new Vector2(w, h));
            AddFlip(slot, glows, cards[i], w, 0.45f + i * 0.18f);
        }

        // 名前（「赤の7」）と、手札・山札の枚数
        string names = string.Join("・", cards.Take(n).Select(CardRules.Name));
        var name = SpaceTheme.Text(card, "Name", names, n == 1 ? 44 : 32,
            n == 1 ? CardDeck.Tint(cards[0]) : SpaceTheme.TEXT, FontStyles.Bold);
        Wrap(name);
        Stack(name.rectTransform, ref y, Measure(name, n == 1 ? 60f : 46f), 22f);
        var summary = SpaceTheme.Text(card, "Summary", CardDeck.Summary(CardDeck.Current), 28, SpaceTheme.TEXT_SUB,
            FontStyles.Bold);
        Stack(summary.rectTransform, ref y, 40f, 6f);
        if (!string.IsNullOrEmpty(note) || overflow)
        {
            var over = SpaceTheme.Text(card, "Over", string.IsNullOrEmpty(note) ? T_CARD_OVER : note, 28, SpaceTheme.GOLD,
                FontStyles.Bold);
            Wrap(over);
            Stack(over.rectTransform, ref y, Measure(over, 40f), 14f);
        }
        else if (CardDeck.PokerUnlocked && CardDeck.Current.Hand.Count >= CardRules.POKER_CARDS)
        {
            var hint = SpaceTheme.Text(card, "Hint", CardDeck.T_POKER_HINT, 24, SpaceTheme.A(SpaceTheme.TEXT_SUB, 0.85f),
                FontStyles.Bold);
            Wrap(hint);
            Stack(hint.rectTransform, ref y, Measure(hint, 36f), 12f);
        }
        Finish(card, root, ref y);
        return root;
    }

    /// <summary>カードの後ろの光を置く層（カードより奥。隣のカードの上に光がかぶらないように）</summary>
    private static RectTransform GlowLayer(RectTransform stage)
    {
        var layer = SpaceTheme.Rect("Glows", stage);
        SpaceTheme.Stretch(layer);
        return layer;
    }

    /// <summary>slot の中に、裏から表へめくれるカードを置く（delay 秒後にめくる。光は glows の層の同じ位置に）</summary>
    private static void AddFlip(RectTransform slot, RectTransform glows, int code, float w, float delay)
    {
        float h = w * CardDeck.ASPECT;
        var glow = SpaceTheme.Rect("Glow", glows, slot.anchorMin, slot.anchoredPosition, new Vector2(w * 1.9f, h * 1.6f));
        var glowImg = SpaceTheme.Img(glow, SpaceSprites.Glow, SpaceTheme.A(CardDeck.Tint(code), 0f));
        var face = CardDeck.CreateCard(slot, code, w, SpaceTheme.CENTER, Vector2.zero);
        face.localScale = new Vector3(0f, 1f, 1f);
        var back = CardDeck.CreateBack(slot, w, SpaceTheme.CENTER, Vector2.zero);
        var flip = slot.gameObject.AddComponent<SpaceCardFlip>();
        flip.Back = back;
        flip.Face = face;
        flip.Glow = glowImg;
        flip.GlowColor = CardDeck.Tint(code);
        flip.Delay = delay;
    }

    // ================================================================
    // 捨てるカードを選ぶ（選ぶまで閉じない）
    // ================================================================
    private static GameObject _discard;

    /// <summary>
    /// 手札が8枚以上のとき・山札座で引いたあと: 捨てるカードを1枚選ぶ（選ぶまで閉じない）。捨てたあともまだ8枚以上なら、もう一度出す。
    /// すでに出ていればそれを返す
    /// </summary>
    public static GameObject CardDiscard()
    {
        if (_discard != null) return _discard;
        var state = CardDeck.Current;
        if (!CardRules.MustDiscard(state)) return null;
        // 山札座で引いたあと（7枚から引いて8枚になったときも、1枚捨てれば両方済むので山札座の文にする）
        bool swap = state.SwapPending;
        var card = Overlay("__CardDiscardModal", out var root, SpaceTheme.A(SpaceTheme.GOLD, 0.5f), null, false);
        if (card == null) return null;
        _discard = root;

        float y = 48f;
        var caption = SpaceTheme.Text(card, "Caption", swap ? SpaceDeckSwap.T_CAPTION : T_CARD_CAPTION, 24,
            SpaceTheme.A(swap ? SpaceDeckSwap.Tint : SpaceTheme.CYAN, 0.9f), FontStyles.Bold);
        SpaceTheme.Tracking(caption, 18f);
        Stack(caption.rectTransform, ref y, 36f, 0f);
        var title = SpaceTheme.Text(card, "Title", T_DISCARD_TITLE, 40, SpaceTheme.TEXT, FontStyles.Bold);
        title.enableAutoSizing = true;
        title.fontSizeMin = 28f;
        title.fontSizeMax = 40f;
        Stack(title.rectTransform, ref y, 56f, 8f);
        string subText = swap ? T_DISCARD_SWAP_SUB : T_DISCARD_SUB_A + state.Hand.Count + T_DISCARD_SUB_B;
        var sub = SpaceTheme.Text(card, "Sub", subText, 26, SpaceTheme.TEXT_SUB, FontStyles.Bold);
        Wrap(sub);
        Stack(sub.rectTransform, ref y, Measure(sub, 40f), 6f);

        // 手札（4枚ずつ。いちばん新しいカードに NEW）
        var hand = state.Hand;
        const float W = 150f, GAP = 24f, LIFT = 18f;
        float h = W * CardDeck.ASPECT;
        int perRow = 4;
        int rows = (hand.Count + perRow - 1) / perRow;
        var grid = SpaceTheme.Rect("Hand", card);
        Stack(grid, ref y, rows * (h + LIFT) + (rows - 1) * GAP + 40f, 24f);
        var top = new Vector2(0.5f, 1f);

        int selected = -1;
        bool busy = false;
        var holders = new List<RectTransform>();
        var marks = new List<GameObject>();
        var groups = new List<CanvasGroup>();
        CanvasGroup okGroup = null;
        TextMeshProUGUI okLabel = null;

        void Select(int index)
        {
            if (busy) return;
            selected = selected == index ? -1 : index;
            for (int k = 0; k < holders.Count; k++)
            {
                bool on = k == selected;
                var p = holders[k].anchoredPosition;
                holders[k].anchoredPosition = new Vector2(p.x, BaseY(k) + (on ? LIFT : 0f));
                marks[k].SetActive(on);
                groups[k].alpha = on ? 0.62f : 1f;
            }
            if (okGroup != null) okGroup.alpha = selected >= 0 ? 1f : 0.45f;
        }

        float BaseY(int k) => -(LIFT + h * 0.5f + (k / perRow) * (h + LIFT + GAP));

        for (int i = 0; i < hand.Count; i++)
        {
            int index = i;
            int row = i / perRow, col = i % perRow;
            int inRow = Mathf.Min(perRow, hand.Count - row * perRow);
            float rowW = inRow * W + (inRow - 1) * GAP;
            var holder = SpaceTheme.Rect("Card" + i, grid, top,
                new Vector2(-rowW * 0.5f + W * 0.5f + col * (W + GAP), BaseY(i)), new Vector2(W, h));
            var hit = SpaceTheme.HitArea(holder);
            SpaceTheme.Clickable(holder.gameObject, hit, () => Select(index));
            var cardRt = CardDeck.CreateCard(holder, hand[i], W, SpaceTheme.CENTER, Vector2.zero);
            groups.Add(cardRt.gameObject.AddComponent<CanvasGroup>());
            // 選んだカード: 赤い縁と「捨てる」
            var mark = SpaceTheme.Rect("Mark", holder);
            SpaceTheme.Stretch(mark, -8f, -8f, -8f, -8f);
            SpaceTheme.RoundedStroke(mark, SpaceTheme.DANGER, W * 0.14f);
            var markLabel = SpaceTheme.Text(mark, "Label", T_DISCARD_MARK, 26, SpaceTheme.DANGER,
                new Vector2(0.5f, 0f), new Vector2(0f, -24f), new Vector2(W + 40f, 36f), FontStyles.Bold);
            markLabel.raycastTarget = false;
            mark.gameObject.SetActive(false);
            marks.Add(mark.gameObject);
            if (i == hand.Count - 1)
            {
                var badge = SpaceTheme.Rect("New", holder, new Vector2(0.5f, 1f), new Vector2(0f, 6f), new Vector2(84f, 34f));
                SpaceTheme.Rounded(badge, SpaceTheme.GOLD, 17f);
                var bl = SpaceTheme.Text(badge, "Label", T_NEW, 20, SpaceTheme.TEXT_DARK, FontStyles.Bold);
                SpaceTheme.Stretch(bl.rectTransform);
                SpaceTextScaler.Exclude(bl);
            }
            holders.Add(holder);
        }

        async UniTaskVoid Discard()
        {
            if (busy) return;
            if (selected < 0 || selected >= hand.Count)
            {
                SpaceTheme.Toast(T_DISCARD_PICK);
                return;
            }
            busy = true;
            int code = hand[selected];
            okGroup.alpha = 0.5f;
            bool ok = await CardDeck.DiscardAsync(code);
            if (root == null) return;
            if (!ok)
            {
                busy = false;
                okGroup.alpha = 1f;
                SpaceTheme.Toast(T_DISCARD_FAILED);
                return;
            }
            UnityEngine.Object.Destroy(root);
            _discard = null;
            SpaceTheme.Toast(CardRules.Name(code) + T_DISCARDED);
            JourneyService.NotifyChanged();
            // まだ8枚以上（入店とショップで続けて引いた など）: もう一度選ぶ
            if (CardDeck.MustDiscard) CardDiscard();
        }

        var okRt = SpaceTheme.Rect("Ok", card);
        Stack(okRt, ref y, 128f, 36f, 520f);
        var okFace = SpaceTheme.GradientFace(okRt, SpaceTheme.Hex(0xFF8FA6), SpaceTheme.Hex(0xD6456A), SpaceTheme.DANGER);
        SpaceTheme.Clickable(okRt.gameObject, okFace, () => Discard().Forget());
        okGroup = okRt.gameObject.AddComponent<CanvasGroup>();
        okGroup.alpha = 0.45f;
        okLabel = SpaceTheme.Text(okRt, "Label", T_DISCARD_OK, 38, Color.white, FontStyles.Bold);
        SpaceTheme.Stretch(okLabel.rectTransform, 16f, 0f, 16f, 0f);
        okLabel.enableAutoSizing = true;
        okLabel.fontSizeMin = 26f;
        okLabel.fontSizeMax = 38f;

        y += 56f;
        card.sizeDelta = new Vector2(WIDTH, y);
        root.AddComponent<SpacePopIn>().Target = card;
        return root;
    }

    // ================================================================
    // ポーカーの結果
    // ================================================================
    /// <summary>ポーカーの結果: 役の名前・使った5枚（順にめくる）・もらったもの</summary>
    public static GameObject PokerResult(CardDeck.PokerPlay res)
    {
        if (res == null || !res.Ok || res.Reward == null) return null;
        bool big = res.Hand >= PokerHand.Straight;
        var accent = big ? SpaceTheme.GOLD : CardDeck.ACCENT;
        var card = Overlay("__PokerResultModal", out var root, SpaceTheme.A(accent, 0.6f));
        if (card == null) return null;
        SpaceTheme.SubCanvas(root, true);

        float y = 48f;
        var caption = SpaceTheme.Text(card, "Caption", T_POKER_CAPTION, 24, SpaceTheme.A(SpacePoker.PINK, 0.95f),
            FontStyles.Bold);
        SpaceTheme.Tracking(caption, 18f);
        Stack(caption.rectTransform, ref y, 36f, 0f);
        var title = SpaceTheme.Text(card, "Title", res.Reward.Name, big ? 56 : 48, big ? SpaceTheme.GOLD : SpaceTheme.TEXT,
            FontStyles.Bold);
        title.enableAutoSizing = true;
        title.fontSizeMin = 30f;
        title.fontSizeMax = big ? 56f : 48f;
        Stack(title.rectTransform, ref y, 74f, 8f);

        // 使った5枚（左から順にめくる）
        const float W = 128f, GAP = 14f;
        float h = W * CardDeck.ASPECT;
        var stage = SpaceTheme.Rect("Cards", card);
        Stack(stage, ref y, h, 26f);
        var glows = GlowLayer(stage);
        var five = res.Five ?? new List<int>();
        float rowW = five.Count * W + Mathf.Max(0, five.Count - 1) * GAP;
        for (int i = 0; i < five.Count; i++)
        {
            var slot = SpaceTheme.Rect("Slot" + i, stage, new Vector2(0.5f, 1f),
                new Vector2(-rowW * 0.5f + W * 0.5f + i * (W + GAP), -h * 0.5f), new Vector2(W, h));
            AddFlip(slot, glows, five[i], W, 0.3f + i * 0.14f);
        }

        // もらったもの
        var got = SpaceTheme.Text(card, "Got", CardDeck.RewardText(res.Reward), 44, SpaceTheme.GOLD, FontStyles.Bold);
        got.enableAutoSizing = true;
        got.fontSizeMin = 28f;
        got.fontSizeMax = 44f;
        Stack(got.rectTransform, ref y, 60f, 30f);
        var gotSub = SpaceTheme.Text(card, "GotSub", T_POKER_GOT, 28, SpaceTheme.TEXT_SUB, FontStyles.Bold);
        Stack(gotSub.rectTransform, ref y, 40f, 2f);
        if (res.Reward.Gold > 0)
        {
            var bal = SpaceTheme.Text(card, "Balance", T_IG_TOTAL + IgCurrency.Format(res.Balance), 26,
                SpaceTheme.A(SpaceTheme.TEXT_SUB, 0.85f), FontStyles.Bold);
            Stack(bal.rectTransform, ref y, 38f, 10f);
        }
        if (res.Reward.Coupon != null)
        {
            var note = SpaceTheme.Text(card, "Note", T_POKER_COUPON_NOTE, 26, SpaceTheme.A(SpaceTheme.TEXT_SUB, 0.85f),
                FontStyles.Bold);
            Stack(note.rectTransform, ref y, 38f, 10f);
        }
        Finish(card, root, ref y);
        return root;
    }
}

/// <summary>
/// カードを裏から表へめくる（2026-11-12）。Delay 秒待ってから、裏を横に縮め → 表を横に広げる。めくれたら後ろの光を一度ふくらませる
/// </summary>
public class SpaceCardFlip : MonoBehaviour
{
    public RectTransform Back, Face;
    public Image Glow;
    public Color GlowColor = Color.white;
    public float Delay = 0.45f, Duration = 0.5f;
    private float _t;

    private void Update()
    {
        _t += Time.unscaledDeltaTime;
        float k = Mathf.Clamp01((_t - Delay) / Duration);
        if (k < 0.5f)
        {
            float a = k / 0.5f;
            if (Back != null) Back.localScale = new Vector3(1f - a * a, 1f + 0.05f * a, 1f);
            if (Face != null) Face.localScale = new Vector3(0f, 1f, 1f);
        }
        else
        {
            float b = (k - 0.5f) / 0.5f;
            float e = 1f - (1f - b) * (1f - b);
            if (Back != null && Back.gameObject.activeSelf) Back.gameObject.SetActive(false);
            if (Face != null) Face.localScale = new Vector3(e, 1f + 0.05f * (1f - e), 1f);
        }
        // 光: めくれた瞬間にふくらんで、少し残る
        if (Glow != null)
        {
            float g = _t - Delay - Duration * 0.5f;
            float alpha = g < 0f ? 0f : g < 0.25f ? g / 0.25f * 0.55f : Mathf.Max(0.22f, 0.55f - (g - 0.25f) * 0.5f);
            Glow.color = SpaceTheme.A(GlowColor, alpha);
            if (g > 1.2f && k >= 1f) enabled = false;
        }
        else if (k >= 1f)
        {
            enabled = false;
        }
    }
}
