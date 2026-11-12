using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 星座まわりのお知らせ:
///  ・ChooseConstellation … 2つ以上の星座に当てはまるゲームのアチーブメントで、星を灯す星座を1つ選ぶ（2026-11-06 ユーザー指定）
///  ・ConstellationReveal … 星座が完成・レベルアップしたときの特別な演出（全画面。2026-11-06 ユーザー指定）
/// </summary>
public static partial class SpaceNotice
{
    private const string T_CHOOSE_CAPTION = "CHOOSE A CONSTELLATION";
    private const string T_CHOOSE_TITLE = "どの星座に星を灯しますか？";
    private const string T_CHOOSE_NOTE = "このゲームは複数の星座に当てはまります。星が灯るのは選んだ1つだけです";
    private const string T_CHOOSE_NOTE_AGAIN = "このゲームでもう一度星を灯せます。灯す星座を1つ選んでください（星は1日1回まで）";
    private const string T_CHOOSE_DONE = "完成！";
    private const string T_CHOOSE_LEVEL = "に上がる！";
    private const string T_CHOOSE_CLOSE = "選ばずに閉じたときは、もう一度QRコードを読み取ってください";
    private const string T_REVEAL_COMPLETE = "CONSTELLATION COMPLETE";
    private const string T_REVEAL_LEVEL = "LEVEL UP";
    private const string T_REVEAL_DONE = "が完成しました！";
    private const string T_REVEAL_UP = "がもっと輝きはじめました";
    private const string T_REVEAL_PLACED = "宇宙に星座が加わりました";
    private const string T_REVEAL_SHINE = "宇宙の星座がもっと輝きます";
    private const string T_REVEAL_NEXT = "つづける";

    public static string ConstellationGlyphText => new StringBuilder()
        .Append(T_CHOOSE_CAPTION).Append(T_CHOOSE_TITLE).Append(T_CHOOSE_NOTE).Append(T_CHOOSE_NOTE_AGAIN).Append(T_CHOOSE_DONE).Append(T_CHOOSE_LEVEL)
        .Append(T_CHOOSE_CLOSE).Append(T_REVEAL_COMPLETE).Append(T_REVEAL_LEVEL).Append(T_REVEAL_DONE).Append(T_REVEAL_UP)
        .Append(T_REVEAL_PLACED).Append(T_REVEAL_SHINE).Append(T_REVEAL_NEXT).Append(SpaceNoticeTexts.DiceUnlocked)
        .Append(SpaceNoticeTexts.IgUnlocked).Append(SpaceNoticeTexts.IgUnlockedSubA).Append(SpaceNoticeTexts.IgUnlockedSubB)
        .Append(SpaceNoticeTexts.IgUnlockedShop)
        .Append(SpaceNoticeTexts.CardsUnlocked).Append(SpaceNoticeTexts.CardsUnlockedSub).Append(SpaceNoticeTexts.PokerUnlocked)
        .Append(SpaceNoticeTexts.PokerUnlockedSub).Append(SpaceNoticeTexts.PokerUnlockedTap)
        .Append(SpaceNoticeTexts.BlackUnlockedShop).Append(SpaceNoticeTexts.BlackUnlocked).Append(SpaceNoticeTexts.BlackUnlockedSub)
        .Append(SpaceNoticeTexts.PusherUnlocked).Append(SpaceNoticeTexts.PusherUnlockedSub).Append(SpaceNoticeTexts.PusherUnlockedTap)
        .Append(IgCurrency.GlyphText)
        .Append("Lv.0123456789/ →（）枚").ToString();

    // ================================================================
    // 星座を選ぶ
    // ================================================================
    /// <summary>
    /// 星を灯す星座を選ぶ。choices は候補の今の進み具合。押した星座の ID で onPick を呼んで閉じる。
    /// 誤って消えないよう暗幕を押しても閉じない（戻るボタンで閉じたときは、何も書き込まない）
    /// </summary>
    public static GameObject ChooseConstellation(string title, List<ConstellationProgress> choices, Action<string> onPick,
        bool again = false)
    {
        var card = Overlay("__ChooseConstellation", out var root, SpaceTheme.A(SpaceTheme.GOLD, 0.5f));
        if (card == null) return null;
        SpaceTheme.SubCanvas(root, true); // 星がまたたくので分ける
        var dimBtn = root.transform.Find("Dim")?.GetComponent<Button>();
        if (dimBtn != null) dimBtn.enabled = false;

        float y = 48f;
        var caption = SpaceTheme.Text(card, "Caption", T_CHOOSE_CAPTION, 22, SpaceTheme.A(SpaceTheme.GOLD, 0.9f),
            FontStyles.Bold);
        SpaceTheme.Tracking(caption, 12f);
        Stack(caption.rectTransform, ref y, 34f, 0f);
        var t = SpaceTheme.Text(card, "Title", T_CHOOSE_TITLE, 40, SpaceTheme.TEXT, FontStyles.Bold);
        Stack(t.rectTransform, ref y, 56f, 8f);
        var name = SpaceTheme.Text(card, "Game", SpaceTheme.NoParse(SpaceTheme.SafeNow(title)), 36, SpaceTheme.GOLD,
            FontStyles.Bold);
        Wrap(name);
        Stack(name.rectTransform, ref y, Measure(name, 50f), 12f);
        var note = SpaceTheme.Text(card, "Note", again ? T_CHOOSE_NOTE_AGAIN : T_CHOOSE_NOTE, 24, SpaceTheme.TEXT_SUB,
            FontStyles.Bold);
        Wrap(note);
        Stack(note.rectTransform, ref y, Measure(note, 34f), 8f);

        bool picked = false;
        foreach (var p in choices)
        {
            var def = p.Def;
            var row = SpaceTheme.Rect("Choice_" + def.Id, card);
            Stack(row, ref y, 210f, 16f);
            var face = SpaceTheme.Glass(row, 32f, new Color(0.07f, 0.09f, 0.24f, 0.8f), SpaceTheme.A(def.Color, 0.6f), true);
            // 選んだあとの姿（星が1つ増えた状態）を見せる
            var fig = SpaceTheme.Rect("Figure", row, new Vector2(0f, 0.5f), new Vector2(115f, 0f), new Vector2(184f, 184f));
            SpaceConstellationPage.Figure(fig, def, p.Count + 1, 184f);
            SpaceTheme.Text(row, "Name", def.Name, 38, SpaceTheme.TEXT, new Vector2(0f, 0.5f), new Vector2(240f, 40f),
                new Vector2(500f, 50f), FontStyles.Bold, TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
            int n = def.Size;
            int after = p.Count + 1;
            bool up = after % n == 0;
            string ticket = SpaceCoupons.NameOf(def.Reward);
            string sub = !up
                ? $"{p.Partial} / {n} → {after % n} / {n}"
                : after / n == 1
                    ? $"★ {T_CHOOSE_DONE}（{ticket}1枚）"
                    : $"★ Lv.{after / n} {T_CHOOSE_LEVEL}（{ticket}1枚）";
            var s = SpaceTheme.Text(row, "State", sub, 28, up ? SpaceTheme.GOLD : SpaceTheme.A(def.Color, 0.95f),
                new Vector2(0f, 0.5f), new Vector2(240f, -26f), new Vector2(510f, 44f), FontStyles.Bold,
                TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
            s.enableAutoSizing = true;
            s.fontSizeMin = 20f;
            s.fontSizeMax = 28f;
            string id = def.Id;
            SpaceTheme.Clickable(row.gameObject, face, () =>
            {
                if (picked) return;
                picked = true;
                UnityEngine.Object.Destroy(root);
                onPick?.Invoke(id);
            });
        }

        var close = SpaceTheme.Text(card, "CloseNote", T_CHOOSE_CLOSE, 20, SpaceTheme.TEXT_MUTED, FontStyles.Bold);
        Wrap(close);
        Stack(close.rectTransform, ref y, Measure(close, 30f), 20f);

        y += 44f;
        card.sizeDelta = new Vector2(WIDTH, y);
        root.AddComponent<SpacePopIn>().Target = card;
        return root;
    }

    // ================================================================
    // 完成・レベルアップの演出
    // ================================================================
    /// <summary>
    /// 星座の完成・レベルアップの全画面の演出。星が1つずつ灯り、線がつながり、光がはじけて名前が出る。
    /// 閉じたら（「つづける」・戻るボタン）onClosed
    /// </summary>
    public static GameObject ConstellationReveal(ConstellationProgress p, Action onClosed)
    {
        var canvas = SpaceTheme.MainCanvas();
        if (canvas == null)
        {
            onClosed?.Invoke();
            return null;
        }
        var root = SpaceTheme.Rect("__ConstellationReveal", canvas.transform);
        SpaceTheme.Stretch(root);
        root.SetAsLastSibling();
        SpaceTheme.SubCanvas(root.gameObject, true);
        _open.Add(root.gameObject);
        var reveal = root.gameObject.AddComponent<SpaceConstellationReveal>();
        reveal.Play(p, onClosed);
        return root.gameObject;
    }
}

/// <summary>星座の完成・レベルアップの演出の本体（SpaceNotice.ConstellationReveal から作る）</summary>
public class SpaceConstellationReveal : MonoBehaviour
{
    private const float FIG = 680f;
    private Action _onClosed;
    private bool _closed;

    public void Play(ConstellationProgress p, Action onClosed)
    {
        _onClosed = onClosed;
        StartCoroutine(Run(p));
    }

    private void OnDestroy()
    {
        if (_closed) return;
        _closed = true;
        _onClosed?.Invoke();
    }

    private IEnumerator Run(ConstellationProgress p)
    {
        var def = p.Def;
        bool first = p.Level == 1;
        var rt = (RectTransform)transform;
        var group = gameObject.AddComponent<CanvasGroup>();

        // 深い宇宙（下のホームを押させない）＋星座の色の光
        var dim = SpaceTheme.Rect("Dim", rt);
        SpaceTheme.Stretch(dim);
        SpaceTheme.Img(dim, null, new Color(0.004f, 0.006f, 0.03f, 0.96f), true);
        var safe = SpaceTheme.Rect("Safe", rt);
        SpaceTheme.Stretch(safe);
        safe.gameObject.AddComponent<SpaceSafeArea>();
        var center = new Vector2(0f, 120f);
        var haze = SpaceTheme.Rect("Haze", safe, SpaceTheme.CENTER, center, new Vector2(1500f, 1500f));
        var hazeImg = SpaceTheme.Img(haze, SpaceSprites.Glow, SpaceTheme.A(def.Color, 0f));

        var caption = SpaceTheme.Text(safe, "Caption", first ? SpaceNoticeTexts.Complete : SpaceNoticeTexts.LevelUp, 28,
            SpaceTheme.A(SpaceTheme.GOLD, 0f), new Vector2(0.5f, 0.5f), center + new Vector2(0f, FIG * 0.5f + 120f),
            new Vector2(1000f, 44f), FontStyles.Bold);
        SpaceTheme.Tracking(caption, 16f);

        // 星座（完成した姿で作ってから、部品を隠して順に出す）
        var figRt = SpaceTheme.Rect("Figure", safe, SpaceTheme.CENTER, center, new Vector2(FIG, FIG));
        SpaceConstellationPage.Figure(figRt, def, p.Count, FIG);
        var stars = new List<RectTransform>();
        var bars = new List<RectTransform>();
        var extras = new List<RectTransform>();
        for (int i = 0; i < figRt.childCount; i++)
        {
            var c = (RectTransform)figRt.GetChild(i);
            if (c.name == "Star") stars.Add(c);
            else if (c.name == "__Bar") bars.Add(c);
            else extras.Add(c); // 光・回る輪
        }
        foreach (var s in stars) s.localScale = Vector3.zero;
        foreach (var b in bars) b.localScale = new Vector3(0f, 1f, 1f);
        var extraGroups = new List<CanvasGroup>();
        foreach (var e in extras)
        {
            var g = e.gameObject.AddComponent<CanvasGroup>();
            g.alpha = 0f;
            extraGroups.Add(g);
        }

        // 0) 暗転
        yield return SpaceTheme.Tween(0.35f, 0f, k => group.alpha = k);
        StartCoroutine(SpaceTheme.Tween(0.6f, 0f, k =>
            caption.color = SpaceTheme.A(SpaceTheme.GOLD, 0.9f * k)));

        // 1) 星が1つずつ灯る（ぽんと膨らむ）
        foreach (var s in stars)
        {
            var star = s;
            StartCoroutine(SpaceTheme.Tween(0.32f, 0f, k =>
            {
                float sc = k < 0.6f ? Mathf.Lerp(0f, 1.45f, k / 0.6f) : Mathf.Lerp(1.45f, 1f, (k - 0.6f) / 0.4f);
                if (star != null) star.localScale = new Vector3(sc, sc, 1f);
            }));
            yield return new WaitForSecondsRealtime(0.14f);
        }
        yield return new WaitForSecondsRealtime(0.2f);

        // 2) 線がつながる
        foreach (var b in bars)
        {
            var bar = b;
            StartCoroutine(SpaceTheme.Tween(0.22f, 0f, k =>
            {
                if (bar != null) bar.localScale = new Vector3(SpaceTheme.EaseOutCubic(k), 1f, 1f);
            }));
            yield return new WaitForSecondsRealtime(0.07f);
        }
        yield return new WaitForSecondsRealtime(0.3f);

        // 3) 光がはじける: 白い閃光・広がる輪・飛び散る光の粒・星座の色の光が満ちる
        AssetsDatabase.instance?.PlayLevelUpSE();
        var flash = SpaceTheme.Rect("Flash", safe, SpaceTheme.CENTER, center, new Vector2(FIG, FIG));
        var flashImg = SpaceTheme.Img(flash, SpaceSprites.Glow, Color.white);
        StartCoroutine(SpaceTheme.Tween(0.9f, 0f, k =>
        {
            if (flash == null) return;
            float sc = Mathf.Lerp(0.4f, 2.6f, SpaceTheme.EaseOutCubic(k));
            flash.localScale = new Vector3(sc, sc, 1f);
            flashImg.color = SpaceTheme.A(Color.white, 0.9f * (1f - k));
        }));
        for (int r = 0; r < 2; r++)
        {
            var ring = SpaceTheme.Rect("Burst", safe, SpaceTheme.CENTER, center, new Vector2(FIG, FIG));
            var ringImg = SpaceTheme.Img(ring, SpaceSprites.Ring, SpaceTheme.A(def.Color, 0f));
            float delay = r * 0.18f;
            StartCoroutine(SpaceTheme.Tween(1.2f, delay, k =>
            {
                if (ring == null) return;
                float sc = Mathf.Lerp(0.3f, 2.2f, SpaceTheme.EaseOutCubic(k));
                ring.localScale = new Vector3(sc, sc, 1f);
                ringImg.color = SpaceTheme.A(Color.Lerp(def.Color, Color.white, 0.4f), 0.8f * (1f - k));
            }));
        }
        var rnd = new System.Random(def.Id.GetHashCode());
        for (int i = 0; i < 22; i++)
        {
            float ang = (float)(i / 22.0 * Math.PI * 2 + rnd.NextDouble() * 0.3);
            float dist = 380f + (float)rnd.NextDouble() * 360f;
            float size = 26f + (float)rnd.NextDouble() * 34f;
            var spark = SpaceTheme.Rect("Spark", safe, SpaceTheme.CENTER, center, new Vector2(size, size));
            var sparkImg = SpaceTheme.Img(spark, SpaceSprites.Sparkle, Color.white);
            var to = center + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * dist;
            float life = 0.9f + (float)rnd.NextDouble() * 0.6f;
            StartCoroutine(SpaceTheme.Tween(life, 0f, k =>
            {
                if (spark == null) return;
                spark.anchoredPosition = Vector2.Lerp(center, to, SpaceTheme.EaseOutCubic(k));
                sparkImg.color = SpaceTheme.A(Color.Lerp(Color.white, def.Color, k), 1f - k * k);
                spark.localEulerAngles = new Vector3(0f, 0f, k * 90f);
            }));
        }
        StartCoroutine(SpaceTheme.Tween(0.8f, 0f, k =>
        {
            hazeImg.color = SpaceTheme.A(def.Color, 0.22f * k);
            foreach (var g in extraGroups) if (g != null) g.alpha = k;
        }));
        // 星座全体がゆっくり大きくなる
        StartCoroutine(SpaceTheme.Tween(2.4f, 0f, k =>
        {
            if (figRt != null) figRt.localScale = Vector3.one * Mathf.Lerp(1f, 1.06f, SpaceTheme.EaseOutCubic(k));
        }));
        yield return new WaitForSecondsRealtime(0.5f);

        // 4) 名前と、宇宙に加わったこと
        float textY = center.y - FIG * 0.5f - 70f;
        var name = SpaceTheme.Text(safe, "Name", def.Name, 84, SpaceTheme.TEXT, SpaceTheme.CENTER, new Vector2(0f, textY),
            new Vector2(1000f, 110f), FontStyles.Bold);
        var en = SpaceTheme.Text(safe, "En", def.En, 26, SpaceTheme.A(def.Color, 0.95f), SpaceTheme.CENTER,
            new Vector2(0f, textY - 74f), new Vector2(1000f, 40f), FontStyles.Bold);
        SpaceTheme.Tracking(en, 22f);
        var line = SpaceTheme.Text(safe, "Line", first ? SpaceNoticeTexts.Placed : $"Lv.{p.Level}　{SpaceNoticeTexts.Shine}",
            34, SpaceTheme.GOLD, SpaceTheme.CENTER, new Vector2(0f, textY - 140f), new Vector2(1000f, 50f), FontStyles.Bold);
        var textGroup = SpaceTheme.Rect("TextGroup", safe);
        SpaceTheme.Stretch(textGroup);
        name.transform.SetParent(textGroup, true);
        en.transform.SetParent(textGroup, true);
        line.transform.SetParent(textGroup, true);
        if (first && def.Id == SpaceDiceRoller.CONSTELLATION)
        {
            // さいころ座の完成でダイスの画面が使えるようになる（2026-11-11）。どこから開くかを添える
            var dice = SpaceTheme.Text(safe, "Dice", SpaceNoticeTexts.DiceUnlocked, 30, SpaceTheme.TEXT, SpaceTheme.CENTER,
                new Vector2(0f, textY - 204f), new Vector2(1000f, 46f), FontStyles.Bold);
            dice.enableAutoSizing = true;
            dice.fontSizeMin = 22f;
            dice.fontSizeMax = 30f;
            dice.transform.SetParent(textGroup, true);
        }
        if (first && def.Id == IgCurrency.CONSTELLATION)
            IgUnlockedLine(safe, textGroup, textY - 250f); // 金貨座の完成でアプリ内の通貨（G）とショップが使えるようになる（2026-11-12）
        if (first && (def.Id == CardDeck.GEM || def.Id == CardDeck.TRUMP))
            CardsUnlockedLine(safe, textGroup, textY - 250f, def.Id); // 宝石座・切り札座の完成でトランプ（宝石座はポーカーも）
        if (first && def.Id == CoinPusher.CONSTELLATION) // 積み木座の完成でコインプッシャー（2026-11-12）
            UnlockedLines(safe, textGroup, textY - 250f, "Pusher", IgCurrency.Icon, SpaceNoticeTexts.PusherUnlocked,
                SpaceNoticeTexts.PusherUnlockedSub, SpaceNoticeTexts.PusherUnlockedTap, SpaceTheme.Hex(0x7FE7FF));
        var tg = textGroup.gameObject.AddComponent<CanvasGroup>();
        tg.alpha = 0f;
        yield return SpaceTheme.Tween(0.5f, 0f, k =>
        {
            tg.alpha = k;
            float sc = Mathf.Lerp(0.85f, 1f, SpaceTheme.EaseOutCubic(k));
            name.rectTransform.localScale = new Vector3(sc, sc, 1f);
        });

        // 5) つづける
        var next = SpaceTheme.Rect("Next", safe, new Vector2(0.5f, 0f), new Vector2(0f, 120f), new Vector2(480f, 120f),
            new Vector2(0.5f, 0f));
        var face = SpaceTheme.PrimaryFace(next);
        var label = SpaceTheme.Text(next, "Label", SpaceNoticeTexts.Next, 40, SpaceTheme.TEXT_DARK, FontStyles.Bold);
        SpaceTheme.Stretch(label.rectTransform);
        var ng = next.gameObject.AddComponent<CanvasGroup>();
        ng.alpha = 0f;
        SpaceTheme.Clickable(next.gameObject, face, () => StartCoroutine(Close(group)));
        yield return SpaceTheme.Tween(0.3f, 0f, k => ng.alpha = k);
    }

    private IEnumerator Close(CanvasGroup group)
    {
        yield return SpaceTheme.Tween(0.3f, 0f, k => group.alpha = 1f - k);
        Destroy(gameObject);
    }

    /// <summary>
    /// 金貨座が完成したときだけ添える一言（2026-11-12）: 金貨の絵と「アプリ内の通貨（G）が使えるようになりました」
    /// 「来店するたびに 1G もらえます。所持金は所持品で見られます」「ホームの空の金貨座をタップすると、ショップが開きます」。
    /// 絵と文字をひとまとまりにして真ん中にそろえる
    /// </summary>
    private static void IgUnlockedLine(RectTransform safe, RectTransform textGroup, float cy)
    {
        string sub = SpaceNoticeTexts.IgUnlockedSubA + IgCurrency.Format(IgCurrency.PerVisit) + SpaceNoticeTexts.IgUnlockedSubB;
        UnlockedLines(safe, textGroup, cy, "Ig", IgCurrency.Icon, SpaceNoticeTexts.IgUnlocked, sub,
            SpaceNoticeTexts.IgUnlockedShop, SpaceTheme.Hex(0x8CF7C8));
    }

    /// <summary>
    /// 宝石座・切り札座が完成したときだけ添える一言（2026-11-12）: 「トランプのカードを持てるようになりました」
    /// 「来店するたびに山札から1枚引けます。手札は所持品で見られます」、宝石座なら「ホームの空の宝石座をタップすると、ポーカーができます」、
    /// 切り札座なら「金貨座のショップに黒コマが並びます」（同日追加の黒コマ）。
    /// もう片方の星座でトランプが使えるようになっていたら、宝石座は「ポーカーができるようになりました」、
    /// 切り札座は「ショップに黒コマが並ぶようになりました」だけにする
    /// </summary>
    private static void CardsUnlockedLine(RectTransform safe, RectTransform textGroup, float cy, string id)
    {
        bool gem = id == CardDeck.GEM;
        string other = gem ? CardDeck.TRUMP : CardDeck.GEM;
        var op = Constellations.Progress().Find(x => x.Def.Id == other);
        bool already = op != null && op.Complete; // もう片方の完成で、トランプはもう持てていた
        if (!gem)
        {
            // 切り札座: トランプ（まだなら）と黒コマ
            if (already)
                UnlockedLines(safe, textGroup, cy, "Black", SpaceMeeples.Black.Icon, SpaceNoticeTexts.BlackUnlocked,
                    SpaceNoticeTexts.BlackUnlockedSub, null, SpaceMeeples.Black.Tint);
            else
                UnlockedLines(safe, textGroup, cy, "Cards", CardDeck.DrawIcon, SpaceNoticeTexts.CardsUnlocked,
                    SpaceNoticeTexts.CardsUnlockedSub, SpaceNoticeTexts.BlackUnlockedShop, SpaceMeeples.Black.Tint);
            return;
        }
        string main = already ? SpaceNoticeTexts.PokerUnlocked : SpaceNoticeTexts.CardsUnlocked;
        string sub = already ? SpaceNoticeTexts.PokerUnlockedSub : SpaceNoticeTexts.CardsUnlockedSub;
        UnlockedLines(safe, textGroup, cy, "Cards", CardDeck.DrawIcon, main, sub, SpaceNoticeTexts.PokerUnlockedTap,
            SpacePoker.PINK);
    }

    /// <summary>絵と3行の文字（1行目は白、2行目は淡い色、3行目は extraColor）をひとまとまりにして真ん中にそろえる</summary>
    private static void UnlockedLines(RectTransform safe, RectTransform textGroup, float cy, string id, Sprite sprite,
        string mainText, string subText, string extraText, Color extraColor)
    {
        const float ICON = 96f, GAP = 20f, MAX_W = 1000f;
        var main = SpaceTheme.Text(safe, id, mainText, 30, SpaceTheme.TEXT, FontStyles.Bold);
        var note = SpaceTheme.Text(safe, id + "Sub", subText, 24, SpaceTheme.TEXT_SUB, FontStyles.Bold);
        var extra = string.IsNullOrEmpty(extraText) ? null
            : SpaceTheme.Text(safe, id + "Extra", extraText, 24, extraColor, FontStyles.Bold);
        float textW = Mathf.Max(main.GetPreferredValues(main.text).x, note.GetPreferredValues(subText).x);
        if (extra != null) textW = Mathf.Max(textW, extra.GetPreferredValues(extraText).x);
        textW = Mathf.Min(MAX_W - ICON - GAP, textW * Mathf.Max(1f, SpaceTheme.TextScale) + 8f);
        float left = -(ICON + GAP + textW) * 0.5f;

        var icon = SpaceTheme.Rect(id + "Icon", safe, SpaceTheme.CENTER, new Vector2(left + ICON * 0.5f, cy),
            new Vector2(ICON, ICON));
        var img = SpaceTheme.Img(icon, sprite, Color.white);
        img.preserveAspect = true;
        img.enabled = sprite != null;
        icon.SetParent(textGroup, true);

        var lines = extra != null
            ? new[] { (main, 40f, 46f), (note, -2f, 36f), (extra, -42f, 36f) }
            : new[] { (main, 22f, 46f), (note, -20f, 36f) };
        foreach (var (t, dy, h) in lines)
        {
            var rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = SpaceTheme.CENTER;
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = new Vector2(left + ICON + GAP, cy + dy);
            rt.sizeDelta = new Vector2(textW, h);
            t.alignment = TextAlignmentOptions.Left;
            t.transform.SetParent(textGroup, true);
        }
    }
}

/// <summary>演出の文字（SpaceNotice の定数を演出の部品から使うため）</summary>
internal static class SpaceNoticeTexts
{
    public const string Complete = "CONSTELLATION COMPLETE";
    public const string LevelUp = "LEVEL UP";
    public const string Done = "が完成しました！";
    public const string Up = "がもっと輝きはじめました";
    public const string Placed = "宇宙に星座が加わりました";
    public const string Shine = "宇宙の星座がもっと輝きます";
    public const string Next = "つづける";
    /// <summary>さいころ座が完成したときだけ添える一言（2026-11-11）</summary>
    public const string DiceUnlocked = "ホームの空のさいころ座をタップすると、ダイスを振れます";
    /// <summary>金貨座が完成したときだけ添える一言（2026-11-12。アプリ内の通貨＝単位 G が使えるようになる）</summary>
    public const string IgUnlocked = "アプリ内の通貨（" + IgCurrency.UNIT + "）が使えるようになりました";
    public const string IgUnlockedSubA = "来店するたびに ";
    /// <summary>宝石座・切り札座が完成したときだけ添える一言（2026-11-12。トランプ・ポーカー）</summary>
    public const string CardsUnlocked = "トランプのカードを持てるようになりました";
    public const string CardsUnlockedSub = "来店するたびに山札から1枚引けます。手札は所持品で見られます";
    public const string PokerUnlocked = "ポーカーができるようになりました";
    public const string PokerUnlockedSub = "手札から5枚選んで勝負すると、役に応じて報酬がもらえます";
    public const string PokerUnlockedTap = "ホームの空の宝石座をタップすると、ポーカーができます";
    /// <summary>切り札座が完成したときだけ添える一言（2026-11-12。黒コマ＝何色にでもなるコマがショップに並ぶ）</summary>
    public const string BlackUnlockedShop = "金貨座のショップに、何色にでもなる黒コマが並びます";
    public const string BlackUnlocked = "ショップに黒コマが並ぶようになりました";
    public const string BlackUnlockedSub = "黒コマは何色にでもなるコマです。色が決まった置き場にも置けます";
    /// <summary>積み木座が完成したときだけ添える一言（2026-11-12。ミニゲームのコインプッシャー）</summary>
    public const string PusherUnlocked = "ミニゲーム「コインプッシャー」で遊べるようになりました";
    public const string PusherUnlockedSub = "G を入れて、G・コマ・星のカケラを手前に押し出そう";
    public const string PusherUnlockedTap = "ホームの空の積み木座をタップすると遊べます";
    public const string IgUnlockedSubB = " もらえます。所持金は所持品で見られます";
    public const string IgUnlockedShop = "ホームの空の金貨座をタップすると、ショップが開きます";
}
