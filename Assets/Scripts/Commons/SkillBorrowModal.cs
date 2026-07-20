using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ユニークスキル「ワーカー配置」（ワーカープレイスメントのスキルブック(A)/(B)装備中）の選択モーダル
/// （コード生成・羊皮紙スタイル）。
/// 戦闘開始時、参戦中の味方が装備している同じスロットのスキルブックのスキルを一覧表示し、
/// 1つ選ぶとこの戦闘だけ自分も使えるようになる。
/// スキルをタップ＝そのスキルを借りる、「借りずにたたかう」＝何も借りずに閉じる。
/// 暗幕タップでは閉じない（誤タップでスキルが流れるのを防ぐ）。
/// </summary>
public static class SkillBorrowModal
{
    private static readonly Color C_DIM    = UITheme.DIM;
    private static readonly Color C_BORDER = new Color(0.84f, 0.66f, 0.18f, 1f);
    private static readonly Color C_BG     = new Color(0.99f, 0.95f, 0.84f, 0.98f);
    private static readonly Color C_TITLE  = new Color(0.38f, 0.16f, 0.04f, 1f);
    private static readonly Color C_SUB    = new Color(0.30f, 0.18f, 0.04f, 1f);
    private static readonly Color C_MUTED  = new Color(0.50f, 0.36f, 0.18f, 1f);
    private static readonly Color C_ROW    = new Color(0.93f, 0.87f, 0.70f, 0.92f);
    private static readonly Color C_BTN    = new Color(0.48f, 0.26f, 0.06f, 1f);

    /// <summary>
    /// 借りられるスキル（定義と所持メンバー名）を一覧表示し、借りるスキルの決定を onDecided で返す。
    /// bookLabel は対象スロットの表記（"A" または "B"）。
    /// 借りない場合は null を渡す。onDecided は必ず1回だけ呼ばれる。
    /// </summary>
    public static void Show(string bookLabel, List<(BattleSkillDef def, string owner)> rows, Action<BattleSkillDef> onDecided)
    {
        var canvasGO = GameObject.Find("Canvas");
        var canvas = canvasGO != null ? canvasGO.GetComponent<Canvas>()
                                      : UnityEngine.Object.FindFirstObjectByType<Canvas>();
        if (canvas == null || rows == null || rows.Count == 0)
        {
            if (canvas == null) Debug.LogWarning("[SkillBorrowModal] Canvasが見つかりません");
            onDecided(null);
            return;
        }

        var jp = Resources.FindObjectsOfTypeAll<TMP_FontAsset>()
            .FirstOrDefault(f => f.name.ToLower() == "jp");

        const float ROW_H = 104f, ROW_GAP = 10f, ROW_W = 680f;
        float rowsH   = rows.Count * (ROW_H + ROW_GAP);
        const float headerH = 320f;
        float panelH  = headerH + rowsH + 150f;

        var dim = new GameObject("__SkillBorrowModal");
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
            $"「ワーカープレイスメントのスキルブック({bookLabel})」ワーカー配置\n参戦中の味方のスキルブック({bookLabel})から1つ借りて、\nこの戦闘で使うことができます。",
            jp, 27, FontStyles.Normal, C_SUB,
            680, 150, new Vector2(0, yTop - 200f));

        bool decided = false; // 二重タップで onDecided が2回走るのを防ぐ
        void Decide(BattleSkillDef picked)
        {
            if (decided) return;
            decided = true;
            UnityEngine.Object.Destroy(dim);
            onDecided(picked);
        }

        for (int i = 0; i < rows.Count; i++)
        {
            var (def, owner) = rows[i];
            float y = yTop - headerH - i * (ROW_H + ROW_GAP) - ROW_H / 2f;
            MakeSkillRow(panel.transform, jp, def, owner, ROW_W, ROW_H, new Vector2(0, y),
                () => Decide(def));
        }

        var skipBtn = MakeRect("__Skip", panel.transform, C_BTN, 520, 96);
        skipBtn.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, -yTop + 90f);
        MakeLabel(skipBtn.transform, "借りずにたたかう", jp, 32, FontStyles.Bold, Color.white,
            520, 96, Vector2.zero);
        skipBtn.AddComponent<Button>().onClick.AddListener(() => Decide(null));
        UITheme.PolishDarkButton(skipBtn.GetComponent<Image>());
    }

    /// <summary>スキル1つ分の行（スキル名＋所持メンバー名＋効果。タップでそのスキルを借りる）。</summary>
    private static void MakeSkillRow(Transform parent, TMP_FontAsset jp,
        BattleSkillDef def, string owner, float w, float h, Vector2 pos, Action onTap)
    {
        var row = MakeRect($"__Skill_{def.Id}", parent, C_ROW, w, h);
        row.GetComponent<RectTransform>().anchoredPosition = pos;

        // スキル名（上段左）
        var nameGO = new GameObject("__Name");
        nameGO.transform.SetParent(row.transform, false);
        var nrt = nameGO.AddComponent<RectTransform>();
        nrt.anchorMin = new Vector2(0f, 0.40f); nrt.anchorMax = new Vector2(0.62f, 1f);
        nrt.offsetMin = new Vector2(20f, 0f); nrt.offsetMax = Vector2.zero;
        var nTxt = nameGO.AddComponent<TextMeshProUGUI>();
        nTxt.text = def.Name;
        nTxt.fontSize = 31; nTxt.fontStyle = FontStyles.Bold;
        nTxt.alignment = TextAlignmentOptions.MidlineLeft;
        nTxt.color = C_TITLE; nTxt.raycastTarget = false;
        nTxt.overflowMode = TextOverflowModes.Ellipsis;
        nTxt.enableWordWrapping = false;
        if (jp != null) nTxt.font = jp;

        // 所持メンバー名（上段右）
        var ownGO = new GameObject("__Owner");
        ownGO.transform.SetParent(row.transform, false);
        var ort = ownGO.AddComponent<RectTransform>();
        ort.anchorMin = new Vector2(0.62f, 0.40f); ort.anchorMax = new Vector2(1f, 1f);
        ort.offsetMin = Vector2.zero; ort.offsetMax = new Vector2(-20f, 0f);
        var oTxt = ownGO.AddComponent<TextMeshProUGUI>();
        oTxt.text = $"{owner}の本";
        oTxt.fontSize = 24; oTxt.fontStyle = FontStyles.Bold;
        oTxt.alignment = TextAlignmentOptions.MidlineRight;
        oTxt.color = C_MUTED; oTxt.raycastTarget = false;
        oTxt.overflowMode = TextOverflowModes.Ellipsis;
        oTxt.enableWordWrapping = false;
        if (jp != null) oTxt.font = jp;

        // 効果（下段・小さく）
        var fxGO = new GameObject("__Effect");
        fxGO.transform.SetParent(row.transform, false);
        var frt = fxGO.AddComponent<RectTransform>();
        frt.anchorMin = new Vector2(0f, 0f); frt.anchorMax = new Vector2(1f, 0.40f);
        frt.offsetMin = new Vector2(20f, 4f); frt.offsetMax = new Vector2(-20f, 0f);
        var fTxt = fxGO.AddComponent<TextMeshProUGUI>();
        fTxt.text = def.Description;
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
