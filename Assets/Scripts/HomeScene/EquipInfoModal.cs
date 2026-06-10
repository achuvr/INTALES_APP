using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Linq;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;

public class EquipInfoModal : MonoBehaviour
{
    private static readonly (EquipmentSlot slot, string label)[] SLOTS =
    {
        (EquipmentSlot.Weapon,     "武器"),
        (EquipmentSlot.Head,       "頭"),
        (EquipmentSlot.Body,       "体"),
        (EquipmentSlot.Feet,       "足"),
        (EquipmentSlot.SkillBookA, "スキルブックA"),
        (EquipmentSlot.SkillBookB, "スキルブックB"),
    };

    private static readonly string[] JOB_KEYS =
        { "warrior", "magician", "archer", "gunner", "common" };

    private static readonly Dictionary<EffectType, (string name, string fmt)> EFFECT_LABELS
        = new Dictionary<EffectType, (string, string)>
    {
        { EffectType.AtkUp,          ("攻撃力",         "+{0}")  },
        { EffectType.DefUp,          ("防御力",         "+{0}")  },
        { EffectType.HpUp,           ("HP",             "+{0}")  },
        { EffectType.SpeedUp,        ("速度",           "+{0}")  },
        { EffectType.BonusExp,       ("獲得経験値",     "+{0}%") },
        { EffectType.CriticalRateUp, ("クリティカル率", "+{0}%") },
        { EffectType.GoldBonus,      ("獲得ゴールド",   "+{0}%") },
        { EffectType.SkillSlotUnlock,("スキルスロット", "Lv{0}") },
        { EffectType.CriticalDamageUp,("クリティカルダメージ", "+{0}%") },
        { EffectType.SpecialAbility, ("特殊能力",       "+{0}")  },
        { EffectType.ProbUp,         ("確率上昇",       "+{0}%") },
    };

    private static readonly Color C_OVERLAY  = new Color(0f,   0f,   0f,   0.65f);
    private static readonly Color C_BG       = new Color(0.99f,0.95f,0.84f,0.98f);
    private static readonly Color C_BORDER   = new Color(0.84f,0.66f,0.18f,1.00f);
    private static readonly Color C_TITLE    = new Color(0.38f,0.16f,0.04f,1.00f);
    private static readonly Color C_DIVIDER  = new Color(0.80f,0.62f,0.18f,0.70f);
    private static readonly Color C_ROW_ODD  = new Color(0.90f,0.84f,0.68f,0.55f);
    private static readonly Color C_ROW_EVEN = new Color(0.95f,0.91f,0.78f,0.40f);
    private static readonly Color C_LABEL    = new Color(0.48f,0.26f,0.06f,1.00f);
    private static readonly Color C_ITEM     = new Color(0.20f,0.10f,0.02f,1.00f);
    private static readonly Color C_NONE     = new Color(0.60f,0.48f,0.32f,1.00f);
    private static readonly Color C_CLOSE    = new Color(0.90f,0.32f,0.32f,1.00f);
    private static readonly Color C_STAT_VAL = new Color(0.14f,0.36f,0.12f,1.00f);

    private Canvas _canvas;
    private GameObject _modal;
    private TMP_FontAsset _jp;

    // ================================================================
    // Start
    // ================================================================
    private void Start()
    {
#if UNITY_EDITOR
        _jp = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Fonts/jp.asset");
        if (_jp == null)
            _jp = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Fonts 1/jp.asset");
#endif
        if (_jp == null)
        {
            var fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
            _jp = fonts.FirstOrDefault(f => f.name.ToLower() == "jp") ?? fonts.FirstOrDefault();
        }

        var cGO = GameObject.Find("Canvas");
        _canvas = cGO != null ? cGO.GetComponent<Canvas>() : FindFirstObjectByType<Canvas>();

        var btn = Resources.FindObjectsOfTypeAll<GameObject>()
            .FirstOrDefault(g => g.name == "Button_OpenProfile" && g.scene.IsValid());
        if (btn != null)
        {
            var b = btn.GetComponent<Button>();
            if (b != null)
            {
                b.onClick.RemoveAllListeners();
                b.onClick.AddListener(() => ShowModalAsync().Forget());
            }
        }
    }

    // ================================================================
    // データ取得
    // ================================================================
    private async UniTaskVoid ShowModalAsync()
    {
        if (_modal != null) Destroy(_modal);

        // Canvas を毎回取得し直す（参照が切れていても安全）
        if (_canvas == null)
        {
            var cGO = GameObject.Find("Canvas");
            _canvas = cGO != null ? cGO.GetComponent<Canvas>() : FindFirstObjectByType<Canvas>();
        }
        if (_canvas == null)
        {
            Debug.LogError("[EquipInfoModal] Canvas が見つかりません");
            return;
        }

        int charIdx = UserDataManager.instance.CurrentSelectCharacterNumber;
        var chara = UserDataManager.instance?.UserData?.Characters?
            .ElementAtOrDefault(charIdx);
        if (chara == null) return;

        var equippedIds = new Dictionary<EquipmentSlot, string>();
        foreach (var (slot, _) in SLOTS)
            equippedIds[slot] = LocalEquipSave.Load(charIdx, slot);

        var itemDataMap  = new Dictionary<string, ItemData>();
        var equippedItemIds = equippedIds.Values
            .Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList();

        if (equippedItemIds.Count > 0 && ItemRepository.instance != null)
        {
            var refs = new List<InventoryRef>();
            foreach (var itemId in equippedItemIds)
            {
                var r = chara.Inventory.FirstOrDefault(x => x.ItemId == itemId);
                if (r != null) refs.Add(r);
                else foreach (var job in JOB_KEYS)
                    refs.Add(new InventoryRef { Job = job, ItemId = itemId });
            }
            var items = await ItemRepository.instance.GetItemsAsync(refs);
            foreach (var item in items)
                if (item != null && !string.IsNullOrEmpty(item.ItemId) && !itemDataMap.ContainsKey(item.ItemId))
                    itemDataMap[item.ItemId] = item;
        }

        // Effects を集計
        var statTotals = new Dictionary<EffectType, int>();
        foreach (var itemId in equippedItemIds)
        {
            if (!itemDataMap.TryGetValue(itemId, out var data) || data.Effects == null) continue;
            foreach (var fx in data.Effects)
            {
                if (fx == null) continue;
                if (!statTotals.ContainsKey(fx.Type)) statTotals[fx.Type] = 0;
                statTotals[fx.Type] += fx.Value;
            }
        }

        BuildModal(equippedIds, itemDataMap, statTotals);
    }

    // ================================================================
    // モーダル構築
    // ================================================================
    private void BuildModal(
        Dictionary<EquipmentSlot, string> equippedIds,
        Dictionary<string, ItemData> itemDataMap,
        Dictionary<EffectType, int> statTotals)
    {
        const float ROW_H      = 105f;
        const float ROW_GAP    = 8f;
        const int   ROW_COUNT  = 6;
        const float TITLE_AREA = 148f;
        const float SEC2_TITLE = 80f;
        const float STAT_ROW_H = 72f;
        const float STAT_GAP   = 6f;
        const float BOTTOM_PAD = 28f;
        const float DIV2_H     = 20f;

        int   statCount = statTotals.Count;
        float equipArea = ROW_COUNT * ROW_H + (ROW_COUNT - 1) * ROW_GAP;

        // ステータスが0件でも「なし」行1行分のスペースを確保
        int   statRows  = statCount > 0 ? Mathf.CeilToInt(statCount / 2f) : 1;
        float statArea  = DIV2_H + SEC2_TITLE + statRows * STAT_ROW_H + (statRows - 1) * STAT_GAP;
        float panelH    = TITLE_AREA + equipArea + statArea + BOTTOM_PAD;

        // ---- オーバーレイ ----
        _modal = new GameObject("__EquipInfoModal");
        _modal.transform.SetParent(_canvas.transform, false);
        var overlayRT = _modal.AddComponent<RectTransform>();
        overlayRT.anchorMin = Vector2.zero; overlayRT.anchorMax = Vector2.one;
        overlayRT.offsetMin = overlayRT.offsetMax = Vector2.zero;
        _modal.AddComponent<Image>().color = C_OVERLAY;
        _modal.transform.SetAsLastSibling();
        var overlayBtn = _modal.AddComponent<Button>();
        overlayBtn.transition = Selectable.Transition.None;
        overlayBtn.onClick.AddListener(HideModal);

        // ---- ボーダー / パネル ----
        var border = MakeRect("__Border", _modal.transform, C_BORDER, 920f, panelH + 16f);
        var panel  = MakeRect("__Panel",  border.transform, C_BG,     904f, panelH);
        var panelBtn = panel.AddComponent<Button>();
        panelBtn.transition = Selectable.Transition.None;
        panelBtn.onClick.AddListener(() => {});

        MakeCloseButton(panel.transform);

        // ---- タイトル ----
        var tGO = MakeGO("__Title", panel.transform);
        var tRT = tGO.AddComponent<RectTransform>();
        tRT.anchorMin = new Vector2(0f,1f); tRT.anchorMax = new Vector2(1f,1f);
        tRT.pivot = new Vector2(0.5f,1f);
        tRT.anchoredPosition = new Vector2(0f,-26f);
        tRT.sizeDelta = new Vector2(0f, 100f);
        var tTxt = tGO.AddComponent<TextMeshProUGUI>();
        tTxt.text = "装備中アイテム";
        tTxt.fontSize = 52f; tTxt.fontStyle = FontStyles.Bold;
        tTxt.alignment = TextAlignmentOptions.Center;
        tTxt.color = C_TITLE; tTxt.raycastTarget = false;
        AF(tTxt);

        // ---- 仕切り線1 ----
        MakeHDivider(panel.transform, -134f);

        // ---- 装備スロット行 ----
        for (int i = 0; i < SLOTS.Length; i++)
        {
            var (slot, label) = SLOTS[i];
            equippedIds.TryGetValue(slot, out string itemId);
            bool hasItem = !string.IsNullOrEmpty(itemId);

            string displayName = "（未装備）";
            if (hasItem && itemDataMap.TryGetValue(itemId, out var data2))
                displayName = !string.IsNullOrEmpty(data2.Name) ? data2.Name : itemId;
            else if (hasItem)
                displayName = itemId;

            float topOffset = TITLE_AREA + i * (ROW_H + ROW_GAP);
            MakeEquipRow(panel.transform, i, label, displayName, hasItem, topOffset, ROW_H);
        }

        // ---- ステータス合計セクション ----
        float sectionTop = TITLE_AREA + equipArea;

        // 仕切り線2
        MakeHDivider(panel.transform, -(sectionTop + 12f));

        // セクションタイトル
        var stGO = MakeGO("__StatTitle", panel.transform);
        var stRT = stGO.AddComponent<RectTransform>();
        stRT.anchorMin = new Vector2(0f,1f); stRT.anchorMax = new Vector2(1f,1f);
        stRT.pivot = new Vector2(0.5f,1f);
        stRT.anchoredPosition = new Vector2(0f, -(sectionTop + DIV2_H));
        stRT.sizeDelta = new Vector2(0f, SEC2_TITLE);
        var stTxt = stGO.AddComponent<TextMeshProUGUI>();
        stTxt.text = "ステータス合計";
        stTxt.fontSize = 42f; stTxt.fontStyle = FontStyles.Bold;
        stTxt.alignment = TextAlignmentOptions.Center;
        stTxt.color = C_TITLE; stTxt.raycastTarget = false;
        AF(stTxt);

        float statStartY = sectionTop + DIV2_H + SEC2_TITLE;

        if (statCount == 0)
        {
            // effectsが1件もない場合は「なし」表示
            var noneGO = MakeGO("__NoStat", panel.transform);
            var nRT = noneGO.AddComponent<RectTransform>();
            nRT.anchorMin = new Vector2(0f,1f); nRT.anchorMax = new Vector2(1f,1f);
            nRT.pivot = new Vector2(0.5f,1f);
            nRT.anchoredPosition = new Vector2(0f, -statStartY);
            nRT.sizeDelta = new Vector2(0f, STAT_ROW_H);
            noneGO.AddComponent<Image>().color = C_ROW_ODD;
            var nTxtGO = MakeGO("__NoStatTxt", noneGO.transform);
            var nTxtRT = nTxtGO.AddComponent<RectTransform>();
            nTxtRT.anchorMin = Vector2.zero; nTxtRT.anchorMax = Vector2.one;
            nTxtRT.offsetMin = nTxtRT.offsetMax = Vector2.zero;
            var nTxt = nTxtGO.AddComponent<TextMeshProUGUI>();
            nTxt.text = "装備中アイテムにステータス効果はありません";
            nTxt.fontSize = 36f;
            nTxt.alignment = TextAlignmentOptions.Center;
            nTxt.color = C_NONE; nTxt.raycastTarget = false;
            AF(nTxt);
        }
        else
        {
            // ステータス行（2列グリッド）
            int   col    = 0;
            float rowY   = statStartY;
            float halfW  = 420f;

            var orderedStats = EFFECT_LABELS.Keys
                .Where(k => statTotals.ContainsKey(k))
                .Concat(statTotals.Keys.Except(EFFECT_LABELS.Keys))
                .ToList();

            foreach (var effectType in orderedStats)
            {
                if (!statTotals.TryGetValue(effectType, out int total)) continue;
                EFFECT_LABELS.TryGetValue(effectType, out var lbl);
                string effectName  = lbl.name ?? effectType.ToString();
                string effectValue = string.Format(lbl.fmt ?? "+{0}", total);
                float  xOffset     = col == 0 ? -halfW * 0.5f : halfW * 0.5f;

                MakeStatRow(panel.transform, effectName, effectValue,
                    xOffset, -rowY, halfW, STAT_ROW_H,
                    col % 2 == 0 ? C_ROW_ODD : C_ROW_EVEN);

                col++;
                if (col >= 2) { col = 0; rowY += STAT_ROW_H + STAT_GAP; }
            }
        }
    }

    // ================================================================
    // 装備スロット行
    // ================================================================
    private void MakeEquipRow(Transform panel, int i, string label,
        string displayName, bool hasItem, float topOffset, float rowH)
    {
        Color rowColor = i % 2 == 0 ? C_ROW_ODD : C_ROW_EVEN;
        var rowGO = MakeGO($"__Row_{i}", panel);
        var rowRT = rowGO.AddComponent<RectTransform>();
        rowRT.anchorMin = new Vector2(0f,1f); rowRT.anchorMax = new Vector2(1f,1f);
        rowRT.pivot = new Vector2(0.5f,1f);
        rowRT.offsetMin  = new Vector2(14f,0f);
        rowRT.offsetMax  = new Vector2(-14f,0f);
        rowRT.anchoredPosition = new Vector2(0f,-topOffset);
        rowRT.sizeDelta        = new Vector2(0f, rowH);
        rowGO.AddComponent<Image>().color = rowColor;

        var lblGO = MakeGO("__Label", rowGO.transform);
        var lblRT = lblGO.AddComponent<RectTransform>();
        lblRT.anchorMin = new Vector2(0f,0f); lblRT.anchorMax = new Vector2(0.37f,1f);
        lblRT.offsetMin = new Vector2(18f,0f); lblRT.offsetMax = Vector2.zero;
        var lblTxt = lblGO.AddComponent<TextMeshProUGUI>();
        lblTxt.text = label; lblTxt.fontSize = 38f; lblTxt.fontStyle = FontStyles.Bold;
        lblTxt.alignment = TextAlignmentOptions.MidlineLeft;
        lblTxt.color = C_LABEL; lblTxt.raycastTarget = false;
        AF(lblTxt);

        var vDivGO = MakeGO("__VDiv", rowGO.transform);
        var vDivRT = vDivGO.AddComponent<RectTransform>();
        vDivRT.anchorMin = new Vector2(0.37f,0.12f); vDivRT.anchorMax = new Vector2(0.37f,0.88f);
        vDivRT.pivot = new Vector2(0.5f,0.5f);
        vDivRT.sizeDelta = new Vector2(3f,0f);
        vDivRT.anchoredPosition = Vector2.zero;
        vDivGO.AddComponent<Image>().color = new Color(0.80f,0.62f,0.18f,0.35f);

        var itmGO = MakeGO("__Item", rowGO.transform);
        var itmRT = itmGO.AddComponent<RectTransform>();
        itmRT.anchorMin = new Vector2(0.39f,0f); itmRT.anchorMax = new Vector2(1f,1f);
        itmRT.offsetMin = new Vector2(10f,0f); itmRT.offsetMax = new Vector2(-14f,0f);
        var itmTxt = itmGO.AddComponent<TextMeshProUGUI>();
        itmTxt.text = displayName; itmTxt.fontSize = 38f;
        itmTxt.alignment = TextAlignmentOptions.MidlineLeft;
        itmTxt.color = hasItem ? C_ITEM : C_NONE;
        itmTxt.overflowMode = TextOverflowModes.Ellipsis;
        itmTxt.raycastTarget = false;
        AF(itmTxt);
    }

    // ================================================================
    // ステータス行（2列グリッド）
    // ================================================================
    private void MakeStatRow(Transform panel, string statName, string statValue,
        float xOffset, float yOffset, float w, float h, Color bgColor)
    {
        var go = MakeGO("__Stat", panel);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f,1f); rt.anchorMax = new Vector2(0.5f,1f);
        rt.pivot = new Vector2(0.5f,1f);
        rt.anchoredPosition = new Vector2(xOffset, yOffset);
        rt.sizeDelta = new Vector2(w - 10f, h);
        go.AddComponent<Image>().color = bgColor;

        var nameGO = MakeGO("__SName", go.transform);
        var nRT = nameGO.AddComponent<RectTransform>();
        nRT.anchorMin = new Vector2(0f,0f); nRT.anchorMax = new Vector2(0.62f,1f);
        nRT.offsetMin = new Vector2(16f,0f); nRT.offsetMax = Vector2.zero;
        var nTxt = nameGO.AddComponent<TextMeshProUGUI>();
        nTxt.text = statName; nTxt.fontSize = 36f;
        nTxt.alignment = TextAlignmentOptions.MidlineLeft;
        nTxt.color = C_LABEL; nTxt.raycastTarget = false;
        AF(nTxt);

        var valGO = MakeGO("__SVal", go.transform);
        var vRT = valGO.AddComponent<RectTransform>();
        vRT.anchorMin = new Vector2(0.62f,0f); vRT.anchorMax = new Vector2(1f,1f);
        vRT.offsetMin = Vector2.zero; vRT.offsetMax = new Vector2(-14f,0f);
        var vTxt = valGO.AddComponent<TextMeshProUGUI>();
        vTxt.text = statValue; vTxt.fontSize = 40f; vTxt.fontStyle = FontStyles.Bold;
        vTxt.alignment = TextAlignmentOptions.MidlineRight;
        vTxt.color = C_STAT_VAL; vTxt.raycastTarget = false;
        AF(vTxt);
    }

    // ================================================================
    // 水平仕切り線
    // ================================================================
    private void MakeHDivider(Transform parent, float yOffset)
    {
        var go = MakeGO("__Div", parent);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.05f,1f); rt.anchorMax = new Vector2(0.95f,1f);
        rt.pivot = new Vector2(0.5f,1f);
        rt.anchoredPosition = new Vector2(0f, yOffset);
        rt.sizeDelta = new Vector2(0f, 4f);
        go.AddComponent<Image>().color = C_DIVIDER;
    }

    // ================================================================
    // 閉じるボタン
    // ================================================================
    private void MakeCloseButton(Transform parent)
    {
        var go = MakeGO("__CloseBtn", parent);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(1f,1f); rt.anchorMax = new Vector2(1f,1f);
        rt.pivot = new Vector2(1f,1f);
        rt.anchoredPosition = new Vector2(-14f,-14f);
        rt.sizeDelta = new Vector2(84f,84f);
        go.AddComponent<Image>().color = C_CLOSE;
        var btn = go.AddComponent<Button>();
        btn.navigation = new Navigation { mode = Navigation.Mode.None };
        var cb = ColorBlock.defaultColorBlock;
        cb.highlightedColor = new Color(1f,1f,1f,0.82f);
        cb.pressedColor = new Color(0.75f,0.75f,0.75f,1f);
        btn.colors = cb;
        btn.onClick.AddListener(HideModal);
        var tGO = MakeGO("__X", go.transform);
        var tRT = tGO.AddComponent<RectTransform>();
        tRT.anchorMin = Vector2.zero; tRT.anchorMax = Vector2.one;
        tRT.offsetMin = tRT.offsetMax = Vector2.zero;
        var txt = tGO.AddComponent<TextMeshProUGUI>();
        txt.text = "✕"; txt.fontSize = 44f; txt.fontStyle = FontStyles.Bold;
        txt.alignment = TextAlignmentOptions.Center;
        txt.color = Color.white; txt.raycastTarget = false;
        AF(txt);
    }

    // ================================================================
    // ユーティリティ
    // ================================================================
    public void HideModal()
    {
        if (_modal != null) { Destroy(_modal); _modal = null; }
    }

    private void AF(TextMeshProUGUI t) { if (_jp != null) t.font = _jp; }

    private GameObject MakeGO(string name, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go;
    }

    private GameObject MakeRect(string name, Transform parent, Color color, float w, float h)
    {
        var go = MakeGO(name, parent);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f,0.5f);
        rt.pivot = new Vector2(0.5f,0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(w, h);
        go.AddComponent<Image>().color = color;
        return go;
    }
}
