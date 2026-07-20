using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ユニークスキル「アップグレード」（オーディンの術書(A)装備中）の選択モーダル（コード生成・羊皮紙スタイル）。
/// バフカード山札QRを読んだとき、山札のカードを1枚選んで1ランク上のカードに強化してから抽選できる。
/// SPカード（最高ランク）は選べない。カードをタップ＝そのカードを強化して抽選へ、
/// 「強化せずに引く」＝山札そのままで抽選へ進む。
/// 暗幕タップでは閉じない（誤タップでスキルが流れるのを防ぐ）。
/// </summary>
public static class DeckUpgradeModal
{
    private static readonly Color C_DIM    = UITheme.DIM;
    private static readonly Color C_BORDER = new Color(0.84f, 0.66f, 0.18f, 1f);
    private static readonly Color C_BG     = new Color(0.99f, 0.95f, 0.84f, 0.98f);
    private static readonly Color C_TITLE  = new Color(0.38f, 0.16f, 0.04f, 1f);
    private static readonly Color C_SUB    = new Color(0.30f, 0.18f, 0.04f, 1f);
    private static readonly Color C_MUTED  = new Color(0.50f, 0.36f, 0.18f, 1f);
    private static readonly Color C_ROW    = new Color(0.93f, 0.87f, 0.70f, 0.92f);
    private static readonly Color C_ARROW  = new Color(0.14f, 0.42f, 0.16f, 1f); // 強化先（緑系）
    private static readonly Color C_BTN    = new Color(0.48f, 0.26f, 0.06f, 1f);

    /// <summary>
    /// 山札の強化できるカード（SPを除く種類→枚数）を一覧表示し、強化するカードの決定を onDecided で返す。
    /// 強化しない場合は null を渡す。onDecided は必ず1回だけ呼ばれる。
    /// notice はカード変化（塔覇の本(B)）などの事前発動の告知文（不要なら null）。
    /// </summary>
    public static void Show(Dictionary<BuffCardType, int> counts, Action<BuffCardType?> onDecided,
        string notice = null)
    {
        var canvasGO = GameObject.Find("Canvas");
        var canvas = canvasGO != null ? canvasGO.GetComponent<Canvas>()
                                      : UnityEngine.Object.FindFirstObjectByType<Canvas>();
        if (canvas == null)
        {
            Debug.LogWarning("[DeckUpgradeModal] Canvasが見つからないため強化なしで進めます");
            onDecided(null);
            return;
        }

        var jp = Resources.FindObjectsOfTypeAll<TMP_FontAsset>()
            .FirstOrDefault(f => f.name.ToLower() == "jp");

        // 強化できるのはSP（最高ランク）を除いた、山札にあるカードだけ（定義順で並べる）
        var rows = BuffCard.AllTypes
            .Where(t => t != BuffCardType.SP1 && t != BuffCardType.SP2)
            .Where(t => counts.TryGetValue(t, out int n) && n > 0)
            .ToList();
        if (rows.Count == 0) { onDecided(null); return; } // 保険（呼び出し側でもガード済み）

        const float ROW_H = 104f, ROW_GAP = 10f, ROW_W = 680f;
        float rowsH   = rows.Count * (ROW_H + ROW_GAP);
        float headerH = string.IsNullOrEmpty(notice) ? 300f : 420f; // 告知ぶん高くする
        float panelH  = headerH + rowsH + 150f;

        var dim = new GameObject("__DeckUpgradeModal");
        dim.transform.SetParent(canvas.transform, false);
        var drt = dim.AddComponent<RectTransform>();
        drt.anchorMin = Vector2.zero; drt.anchorMax = Vector2.one;
        drt.offsetMin = drt.offsetMax = Vector2.zero;
        dim.AddComponent<Image>().color = C_DIM; // 暗幕（クリック遮断のみ。タップで閉じない）
        dim.transform.SetAsLastSibling();

        var border = MakeRect("__Border", dim.transform, C_BORDER, 760, panelH + 16f);
        var panel  = MakeRect("__Panel", border.transform, C_BG, 744, panelH);
        UITheme.ElevateCard(border, 18f, 10f, 0.35f);

        float yTop = panelH / 2f;

        MakeLabel(panel.transform, "ユニークスキル発動！", jp, 44, FontStyles.Bold, C_TITLE,
            700, 70, new Vector2(0, yTop - 70f));
        MakeLabel(panel.transform,
            "「オーディンの術書(A)」アップグレード\n山札のカードを1枚えらんで、1ランク上のカードに強化できます。\n（SPカードは選べません）",
            jp, 27, FontStyles.Normal, C_SUB,
            680, 140, new Vector2(0, yTop - 190f));

        // カード変化（塔覇の本(B)）などの事前発動の告知（変化後の山札が下の一覧に出ている）
        if (!string.IsNullOrEmpty(notice))
            MakeLabel(panel.transform, notice, jp, 25, FontStyles.Bold,
                new Color(0.62f, 0.32f, 0.04f, 1f), 680, 100, new Vector2(0, yTop - 315f));

        bool decided = false; // 二重タップで onDecided が2回走るのを防ぐ
        void Decide(BuffCardType? picked)
        {
            if (decided) return;
            decided = true;
            UnityEngine.Object.Destroy(dim);
            onDecided(picked);
        }

        for (int i = 0; i < rows.Count; i++)
        {
            var type = rows[i];
            float y = yTop - headerH - i * (ROW_H + ROW_GAP) - ROW_H / 2f;
            MakeCardRow(panel.transform, jp, type, counts[type], ROW_W, ROW_H, new Vector2(0, y),
                () => Decide(type));
        }

        var skipBtn = MakeRect("__Skip", panel.transform, C_BTN, 460, 96);
        skipBtn.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, -yTop + 90f);
        MakeLabel(skipBtn.transform, "強化せずに引く", jp, 34, FontStyles.Bold, Color.white,
            460, 96, Vector2.zero);
        skipBtn.AddComponent<Button>().onClick.AddListener(() => Decide(null));
        UITheme.PolishDarkButton(skipBtn.GetComponent<Image>());
    }

    /// <summary>強化先の表示名（同ランクに複数いる場合＝SPは「スペシャルカード（ランダム）」）。</summary>
    private static string TargetLabel(BuffCardType from)
    {
        int next = BuffCard.Rank(from) + 1;
        var ups = BuffCard.AllTypes.Where(t => BuffCard.Rank(t) == next).ToList();
        if (ups.Count == 0) return "";
        return ups.Count == 1 ? BuffCard.DisplayName(ups[0]) : "スペシャルカード（ランダム）";
    }

    /// <summary>強化先カードの効果（同ランク複数の場合は先頭の効果＝SP1/SP2は同効果）。</summary>
    private static string TargetEffect(BuffCardType from)
    {
        int next = BuffCard.Rank(from) + 1;
        var up = BuffCard.AllTypes.Where(t => BuffCard.Rank(t) == next).Cast<BuffCardType?>().FirstOrDefault();
        return up != null ? BuffCard.EffectText(up.Value) : "";
    }

    /// <summary>カード1種類の行（名前＋枚数＋強化先。タップでそのカードを強化に決定）。</summary>
    private static void MakeCardRow(Transform parent, TMP_FontAsset jp,
        BuffCardType type, int count, float w, float h, Vector2 pos, Action onTap)
    {
        var row = MakeRect($"__Card_{BuffCard.Code(type)}", parent, C_ROW, w, h);
        row.GetComponent<RectTransform>().anchoredPosition = pos;

        // 名前＋枚数（上段左）
        var nameGO = new GameObject("__Name");
        nameGO.transform.SetParent(row.transform, false);
        var nrt = nameGO.AddComponent<RectTransform>();
        nrt.anchorMin = new Vector2(0f, 0.40f); nrt.anchorMax = new Vector2(0.44f, 1f);
        nrt.offsetMin = new Vector2(20f, 0f); nrt.offsetMax = Vector2.zero;
        var nTxt = nameGO.AddComponent<TextMeshProUGUI>();
        nTxt.text = $"{BuffCard.DisplayName(type)} ×{count}";
        nTxt.fontSize = 27; nTxt.fontStyle = FontStyles.Bold;
        nTxt.alignment = TextAlignmentOptions.MidlineLeft;
        nTxt.color = C_TITLE; nTxt.raycastTarget = false;
        nTxt.enableAutoSizing = true; nTxt.fontSizeMax = 27; nTxt.fontSizeMin = 18;
        nTxt.enableWordWrapping = false;
        if (jp != null) nTxt.font = jp;

        // 強化先（上段右・緑）
        var toGO = new GameObject("__To");
        toGO.transform.SetParent(row.transform, false);
        var trt = toGO.AddComponent<RectTransform>();
        trt.anchorMin = new Vector2(0.44f, 0.40f); trt.anchorMax = new Vector2(1f, 1f);
        trt.offsetMin = Vector2.zero; trt.offsetMax = new Vector2(-16f, 0f);
        var tTxt = toGO.AddComponent<TextMeshProUGUI>();
        tTxt.text = $"→ {TargetLabel(type)}";
        tTxt.fontSize = 27; tTxt.fontStyle = FontStyles.Bold;
        tTxt.alignment = TextAlignmentOptions.MidlineRight;
        tTxt.color = C_ARROW; tTxt.raycastTarget = false;
        tTxt.enableAutoSizing = true; tTxt.fontSizeMax = 27; tTxt.fontSizeMin = 18;
        tTxt.enableWordWrapping = false;
        if (jp != null) tTxt.font = jp;

        // 強化後の効果（下段・小さく）
        var fxGO = new GameObject("__Effect");
        fxGO.transform.SetParent(row.transform, false);
        var frt = fxGO.AddComponent<RectTransform>();
        frt.anchorMin = new Vector2(0f, 0f); frt.anchorMax = new Vector2(1f, 0.40f);
        frt.offsetMin = new Vector2(20f, 4f); frt.offsetMax = new Vector2(-20f, 0f);
        var fTxt = fxGO.AddComponent<TextMeshProUGUI>();
        fTxt.text = $"強化後: {TargetEffect(type)}";
        fTxt.fontSize = 19;
        fTxt.alignment = TextAlignmentOptions.MidlineLeft;
        fTxt.color = C_MUTED;
        fTxt.overflowMode = TextOverflowModes.Ellipsis;
        fTxt.enableWordWrapping = false;
        fTxt.raycastTarget = false;
        if (jp != null) fTxt.font = jp;

        var btn = row.AddComponent<Button>();
        btn.targetGraphic = row.GetComponent<Image>();
        var cb = ColorBlock.defaultColorBlock;
        cb.highlightedColor = new Color(1f, 1f, 1f, 0.85f);
        cb.pressedColor = new Color(0.72f, 0.72f, 0.72f, 1f);
        btn.colors = cb;
        btn.onClick.AddListener(() => onTap());
    }

    private static GameObject MakeRect(string name, Transform parent, Color color, float w, float h)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>().sizeDelta = new Vector2(w, h);
        go.AddComponent<Image>().color = color;
        return go;
    }

    private static void MakeLabel(Transform parent, string text, TMP_FontAsset font,
        float size, FontStyles style, Color color, float w, float h, Vector2 pos)
    {
        var go = new GameObject("__Label");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(w, h);
        rt.anchoredPosition = pos;
        var tmp = go.AddComponent<TextMeshProUGUI>();
        if (font != null) tmp.font = font;
        tmp.text = text; tmp.fontSize = size; tmp.fontStyle = style;
        tmp.color = color; tmp.alignment = TextAlignmentOptions.Center;
        tmp.enableWordWrapping = true;
        tmp.raycastTarget = false;
    }
}
