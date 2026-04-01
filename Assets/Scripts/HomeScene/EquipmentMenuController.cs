using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.Linq;

public class EquipmentMenuController : MonoBehaviour
{
    private Canvas  _canvas;
    private GameObject _overlay;
    private GameObject _slotPanel;
    private GameObject _listPanel;
    private TextMeshProUGUI _listTitle;
    [SerializeField] private Transform _listContent;
    private EquipmentSlot _currentSlot;
    private bool _isEquipping = false;

    private static readonly Color C_PARCHMENT    = new Color(0.99f, 0.95f, 0.84f, 0.98f);
    private static readonly Color C_BORDER       = new Color(0.84f, 0.66f, 0.18f, 1.00f);
    private static readonly Color C_TITLE        = new Color(0.38f, 0.16f, 0.04f, 1.00f);
    private static readonly Color C_DIVIDER      = new Color(0.80f, 0.62f, 0.18f, 0.70f);
    private static readonly Color C_SCROLL_BG    = new Color(0.94f, 0.89f, 0.76f, 0.90f);
    private static readonly Color C_CLOSE_BTN    = new Color(0.90f, 0.32f, 0.32f, 1.00f);
    private static readonly Color C_BACK_BTN     = new Color(0.48f, 0.26f, 0.06f, 1.00f);
    private static readonly Color C_ROW_NORMAL   = new Color(0.90f, 0.84f, 0.68f, 0.85f);
    private static readonly Color C_ROW_EQUIPPED = new Color(0.99f, 0.96f, 0.72f, 0.98f);
    private static readonly Color C_EQ_BORDER    = new Color(0.84f, 0.62f, 0.08f, 1.00f);
    private static readonly Color C_BADGE_EQ     = new Color(0.22f, 0.68f, 0.22f, 0.95f);
    private static readonly Color C_EMPTY_TXT    = new Color(0.50f, 0.32f, 0.10f);
    private static readonly Color C_EFFECT       = new Color(0.52f, 0.28f, 0.06f);

    private static readonly (EquipmentSlot slot, string label, Color color, string iconRes)[] SLOTS =
    {
        (EquipmentSlot.Weapon,     "武器",    new Color(0.95f,0.38f,0.38f), "EquipmentIcons/icon_weapon"),
        (EquipmentSlot.Head,       "頭",      new Color(0.35f,0.62f,0.98f), "EquipmentIcons/icon_head"),
        (EquipmentSlot.Body,       "体",      new Color(0.28f,0.80f,0.48f), "EquipmentIcons/icon_body"),
        (EquipmentSlot.Feet,       "足",      new Color(0.98f,0.72f,0.20f), "EquipmentIcons/icon_feet"),
        (EquipmentSlot.SkillBookA, "スキルA", new Color(0.76f,0.40f,0.96f), "EquipmentIcons/icon_skilla"),
        (EquipmentSlot.SkillBookB, "スキルB", new Color(0.22f,0.82f,0.88f), "EquipmentIcons/icon_skillb"),
    };

    private Canvas GetMainCanvas()
    {
        var go = GameObject.Find("Canvas");
        return go != null ? go.GetComponent<Canvas>() : FindFirstObjectByType<Canvas>();
    }

    private TMP_FontAsset GetJpFont()
    {
        var fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
        return fonts.FirstOrDefault(f => f.name.ToLower() == "jp") ?? fonts.FirstOrDefault();
    }

    private void Start()
    {
        _canvas = GetMainCanvas();
        BuildUI();
        var equipBtn = Resources.FindObjectsOfTypeAll<GameObject>()
            .FirstOrDefault(g => g.name == "Button_Equipment" && g.scene.IsValid());
        if (equipBtn != null)
        {
            var b = equipBtn.GetComponent<Button>();
            if (b != null) { b.onClick.RemoveAllListeners(); b.onClick.AddListener(ShowSlotPanel); }
        }
    }

    // ================================================================
    // 装備処理（PlayerPrefsにローカル保存）
    // ================================================================
    private void EquipItem(ItemData item)
    {
        if (_isEquipping || item == null) return;
        _isEquipping = true;
        try
        {
            var manager = UserDataManager.instance;
            if (manager == null) return;
            int charIdx = manager.CurrentSelectCharacterNumber;
            var characters = manager.UserData?.Characters;
            if (characters == null || charIdx >= characters.Count) return;
            var chara = characters[charIdx];
            if (chara == null) return;

            chara.Equipment.SetItemId(item.SlotType, item.ItemId);
            LocalEquipSave.Save(charIdx, item.SlotType, item.ItemId);
            Debug.Log($"[Equip] {item.SlotType} = {item.ItemId} ({item.Name ?? "?"})");

            // 装備SEを再生
            AssetsDatabase.instance?.PlayEquipSE();

            // UIを即時更新
            SafeRebuildItemList(_currentSlot);
            RefreshSlotIndicators();
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[Equip] エラー: {ex.Message}\n{ex.StackTrace}");
        }
        finally
        {
            _isEquipping = false;
        }
    }

    // ================================================================
    // UI構築
    // ================================================================
    private void BuildUI()
    {
        var jp = GetJpFont();
        _overlay = MakeGO("__EquipOverlay", _canvas.transform);
        var ort = _overlay.AddComponent<RectTransform>();
        ort.anchorMin = Vector2.zero; ort.anchorMax = Vector2.one;
        ort.offsetMin = ort.offsetMax = Vector2.zero;
        _overlay.AddComponent<Image>().color = new Color(0.08f,0.04f,0.20f,0.68f);
        _overlay.SetActive(false);
        _slotPanel = BuildSlotPanel(jp);
        _slotPanel.transform.SetParent(_overlay.transform, false);
        _listPanel = BuildListPanel(jp);
        _listPanel.transform.SetParent(_overlay.transform, false);
        _listPanel.SetActive(false);
    }

    private GameObject BuildSlotPanel(TMP_FontAsset jp)
    {
        var border = MakeRect("__SlotBorder", _overlay.transform, C_BORDER, 978, 1089);
        var panel  = MakeRect("__SlotPanel", border.transform, C_PARCHMENT, 962, 1073);
        MakeFancyBtn("__Close", panel.transform, C_CLOSE_BTN, "✕", jp, 64, 422, 478, 93, 93, Hide);
        MakeLabel("__Title", panel.transform, "装備スロット選択", jp, 48, FontStyles.Bold, C_TITLE, 0, 444, 851, 93);
        MakeRect("__Div", panel.transform, C_DIVIDER, 851, 4)
            .GetComponent<RectTransform>().anchoredPosition = new Vector2(0, 379);
        float[] colX = { -231f, 231f };
        float[] rowY = {  241f,  74f, -93f };
        for (int i = 0; i < SLOTS.Length; i++)
        {
            int col = i % 2, row = i / 2;
            var (slot, label, color, iconRes) = SLOTS[i];
            MakeSlotBtn(panel.transform, slot, label, color, iconRes, jp, colX[col], rowY[row], 416, 133);
        }
        return border;
    }

    private void MakeSlotBtn(Transform parent, EquipmentSlot slot, string label,
        Color color, string iconRes, TMP_FontAsset jp, float x, float y, float w, float h)
    {
        var go = MakeRect($"__Slot_{slot}", parent, color, w, h);
        go.GetComponent<RectTransform>().anchoredPosition = new Vector2(x, y);
        var hi = MakeRect("__Hi", go.transform, new Color(1f,1f,1f,0.18f), w-6, h*0.5f-3);
        var hrt = hi.GetComponent<RectTransform>();
        hrt.anchorMin = new Vector2(0f,0.5f); hrt.anchorMax = new Vector2(1f,1f);
        hrt.offsetMin = new Vector2(3f,0f); hrt.offsetMax = new Vector2(-3f,-3f);
        hrt.anchoredPosition = Vector2.zero; hrt.sizeDelta = Vector2.zero;
        hi.GetComponent<Image>().raycastTarget = false;
        var btn = go.AddComponent<Button>();
        btn.navigation = new Navigation { mode = Navigation.Mode.None };
        var cb = ColorBlock.defaultColorBlock;
        cb.highlightedColor = new Color(1f,1f,1f,0.82f);
        cb.pressedColor = new Color(0.75f,0.75f,0.75f,1f);
        btn.colors = cb;
        btn.onClick.AddListener(() => ShowListPanel(slot));
        float iconSize = 100f, iconPad = 8f;
        var iconGO = MakeGO("__Icon", go.transform);
        var irt = iconGO.AddComponent<RectTransform>();
        irt.anchorMin = new Vector2(0f,0.5f); irt.anchorMax = new Vector2(0f,0.5f);
        irt.pivot = new Vector2(0.5f,0.5f);
        irt.anchoredPosition = new Vector2(iconPad + iconSize*0.5f, 0f);
        irt.sizeDelta = new Vector2(iconSize, iconSize);
        var iImg = iconGO.AddComponent<Image>();
        iImg.raycastTarget = false; iImg.color = Color.white;
        var sp = Resources.Load<Sprite>(iconRes);
        if (sp != null) iImg.sprite = sp;
        float textLeft = iconPad + iconSize + 8f;
        var txtGO = MakeGO("__SlotLabel", go.transform);
        var trt = txtGO.AddComponent<RectTransform>();
        trt.anchorMin = new Vector2(0f,0f); trt.anchorMax = new Vector2(1f,1f);
        trt.offsetMin = new Vector2(textLeft,4f); trt.offsetMax = new Vector2(-8f,-4f);
        var txt = txtGO.AddComponent<TextMeshProUGUI>();
        txt.text = label; txt.fontSize = 46; txt.fontStyle = FontStyles.Bold;
        txt.color = Color.white; txt.alignment = TextAlignmentOptions.MidlineLeft;
        txt.raycastTarget = false;
        if (jp != null) txt.font = jp;
        var ind = MakeRect("__Ind", go.transform, new Color(1f,1f,1f,0.3f), 22, 22);
        var indrt = ind.GetComponent<RectTransform>();
        indrt.anchorMin = new Vector2(1f,0f); indrt.anchorMax = new Vector2(1f,0f);
        indrt.pivot = new Vector2(1f,0f);
        indrt.anchoredPosition = new Vector2(-13f,13f);
        ind.GetComponent<Image>().raycastTarget = false;
    }

    private GameObject BuildListPanel(TMP_FontAsset jp)
    {
        var border = MakeRect("__ListBorder", _overlay.transform, C_BORDER, 1004, 1232);
        var panel  = MakeRect("__ListPanel", border.transform, C_PARCHMENT, 988, 1216);
        _listTitle = MakeLabel("__ListTitle", panel.transform, "", jp, 46, FontStyles.Bold, C_TITLE, 0, 513, 874, 95);
        MakeRect("__Div2", panel.transform, C_DIVIDER, 874, 4)
            .GetComponent<RectTransform>().anchoredPosition = new Vector2(0, 447);
        var svGO = MakeGO("__ScrollView", panel.transform);
        var svrt = svGO.AddComponent<RectTransform>();
        svrt.anchorMin = new Vector2(0.04f,0.14f);
        svrt.anchorMax = new Vector2(0.96f,0.84f);
        svrt.offsetMin = svrt.offsetMax = Vector2.zero;
        svGO.AddComponent<Image>().color = C_SCROLL_BG;
        var scroll = svGO.AddComponent<ScrollRect>();
        scroll.horizontal = false; scroll.vertical = true; scroll.scrollSensitivity = 35f;
        var vpGO = MakeGO("__VP", svGO.transform);
        var vprt = vpGO.AddComponent<RectTransform>();
        vprt.anchorMin = Vector2.zero; vprt.anchorMax = Vector2.one;
        vprt.offsetMin = new Vector2(4f,4f); vprt.offsetMax = new Vector2(-4f,-4f);
        vpGO.AddComponent<RectMask2D>(); scroll.viewport = vprt;
        var ctGO = MakeGO("__Content", vpGO.transform);
        var ctrt = ctGO.AddComponent<RectTransform>();
        ctrt.anchorMin = new Vector2(0f,1f); ctrt.anchorMax = new Vector2(1f,1f);
        ctrt.pivot = new Vector2(0.5f,1f); ctrt.offsetMin = ctrt.offsetMax = Vector2.zero;
        var vlg = ctGO.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 24f; vlg.padding = new RectOffset(14,14,14,14);
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
        vlg.childControlHeight = true;
        ctGO.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.content = ctrt;
        _listContent = ctGO.transform;
        MakeFancyBtn("__BackBtn", panel.transform, C_BACK_BTN, "← 戻る", jp, 38, 0, -551, 380, 99, ShowSlotPanel);
        return border;
    }

    // =================== 表示制御 ===================

    public void ShowSlotPanel()
    {
        _overlay.SetActive(true);
        _overlay.transform.SetAsLastSibling();
        _slotPanel.SetActive(true);
        _listPanel.SetActive(false);
        RefreshSlotIndicators();
    }

    private void ShowListPanel(EquipmentSlot slot)
    {
        _currentSlot = slot;
        _slotPanel.SetActive(false);
        _listPanel.SetActive(true);
        string label = SLOTS.FirstOrDefault(s => s.slot == slot).label;
        _listTitle.text = label + "の装備一覧";
        SafeRebuildItemList(slot);
    }

    public void Hide() => _overlay.SetActive(false);

    private void RefreshSlotIndicators()
    {
        var chara = UserDataManager.instance?.UserData?.Characters?
            .ElementAtOrDefault(UserDataManager.instance.CurrentSelectCharacterNumber);
        foreach (var (slot, _, __, ___) in SLOTS)
        {
            var slotGO = _slotPanel.transform.Find("__SlotPanel/__Slot_" + slot);
            if (slotGO == null) continue;
            var ind = slotGO.Find("__Ind");
            if (ind == null) continue;
            bool eq = chara != null && chara.Equipment.IsEquipped(slot);
            ind.GetComponent<Image>().color =
                eq ? new Color(1f,0.95f,0.2f,0.95f) : new Color(1f,1f,1f,0.3f);
        }
    }

    // ================================================================
    // アイテム一覧（DestroyImmediateで同フレーム再構築）
    // ================================================================
    private void SafeRebuildItemList(EquipmentSlot slot)
    {
        if (_listContent == null) return;

        // 逆順でDestroyImmediate（同フレームで即時削除）
        for (int i = _listContent.childCount - 1; i >= 0; i--)
        {
            var child = _listContent.GetChild(i);
            if (child != null && child.gameObject != null)
                DestroyImmediate(child.gameObject);
        }

        // 削除直後に再構築
        RebuildItemList(slot);
    }

    private void RebuildItemList(EquipmentSlot slot)
    {
        if (_listContent == null) return;
        var jp = GetJpFont();
        var chara = UserDataManager.instance?.UserData?.Characters?
            .ElementAtOrDefault(UserDataManager.instance.CurrentSelectCharacterNumber);
        var items = chara?.GetInventoryBySlot(slot) ?? new List<ItemData>();

        if (items.Count == 0)
        {
            var emGO = new GameObject("__Empty");
            emGO.transform.SetParent(_listContent, false);
            emGO.AddComponent<RectTransform>();
            emGO.AddComponent<LayoutElement>().preferredHeight = 120f;
            var etxt = emGO.AddComponent<TextMeshProUGUI>();
            etxt.text = "アイテムがありません";
            etxt.fontSize = 36f; etxt.alignment = TextAlignmentOptions.Center;
            etxt.color = C_EMPTY_TXT; etxt.raycastTarget = false;
            if (jp != null) etxt.font = jp;
            return;
        }

        foreach (var item in items)
        {
            if (item == null) continue;
            bool isEquipped = item.IsEquippedBy(chara);
            MakeItemRow(item, isEquipped, jp);
        }
    }

    // ================================================================
    // アイテム行（タップで装備・装備中の視覚表示）
    // ================================================================
    private void MakeItemRow(ItemData item, bool isEquipped, TMP_FontAsset jp)
    {
        // デバッグ用：何がnullか突き止める
        if (item == null) {
            Debug.LogError("MakeItemRow: 引数の item が null です！");
            return;
        }
        if (_listContent == null) {
            Debug.LogError("MakeItemRow: _listContent がセットされていません！");
            return;
        }
        
        // 行の背景
        var row = new GameObject("__Row_" + (item.ItemId ?? "unknown"));
        row.transform.SetParent(_listContent, false);
        var rowRT = row.AddComponent<RectTransform>();
        rowRT.anchorMin = new Vector2(0f,1f); rowRT.anchorMax = new Vector2(1f,1f);
        rowRT.pivot = new Vector2(0.5f,1f);
        var rowImg = row.AddComponent<Image>();
        rowImg.color = isEquipped ? C_ROW_EQUIPPED : C_ROW_NORMAL;
        var rowLE = row.AddComponent<LayoutElement>();
        rowLE.preferredHeight = 190f; rowLE.flexibleWidth = 1f;

        // 装備中: ゴールド枠（背後に配置）
        if (isEquipped)
        {
            var eb = new GameObject("__EqBorder");
            eb.transform.SetParent(row.transform, false);
            var ebRT = eb.AddComponent<RectTransform>();
            ebRT.anchorMin = Vector2.zero; ebRT.anchorMax = Vector2.one;
            ebRT.offsetMin = new Vector2(-4f,-4f); ebRT.offsetMax = new Vector2(4f,4f);
            eb.AddComponent<Image>().color = C_EQ_BORDER;
            eb.transform.SetAsFirstSibling();
        }

        // タップで装備ボタン
        var btn = row.AddComponent<Button>();
        btn.navigation = new Navigation { mode = Navigation.Mode.None };
        var cb = ColorBlock.defaultColorBlock;
        cb.normalColor      = Color.white;
        cb.highlightedColor = new Color(1f,1f,0.75f,1f);
        cb.pressedColor     = new Color(0.85f,0.85f,0.60f,1f);
        btn.colors = cb;
        btn.targetGraphic = rowImg;
        var capturedItem = item;
        btn.onClick.AddListener(() => EquipItem(capturedItem));

        // 左エリア（名前＋効果を縦積み）
        float rightPct = isEquipped ? 0.65f : 1.00f;
        var leftGO = new GameObject("__Left");
        leftGO.transform.SetParent(row.transform, false);
        var lrt = leftGO.AddComponent<RectTransform>();
        lrt.anchorMin = new Vector2(0f,0f); lrt.anchorMax = new Vector2(rightPct,1f);
        lrt.offsetMin = new Vector2(20f,0f); lrt.offsetMax = new Vector2(-8f,0f);
        var lvlg = leftGO.AddComponent<VerticalLayoutGroup>();
        lvlg.childForceExpandWidth = true; lvlg.childForceExpandHeight = false;
        lvlg.childControlHeight = true; lvlg.childControlWidth = true;
        lvlg.spacing = 10f; lvlg.padding = new RectOffset(0,0,20,20);

        // アイテム名
        var nameGO = new GameObject("__Name");
        nameGO.transform.SetParent(leftGO.transform, false);
        nameGO.AddComponent<RectTransform>();
        nameGO.AddComponent<LayoutElement>().preferredHeight = 70f;
        var ntxt = nameGO.AddComponent<TextMeshProUGUI>();
        ntxt.text = (!string.IsNullOrEmpty(item.Name)) ? item.Name : (item.ItemId ?? "---");
        ntxt.fontSize = 50f; ntxt.fontStyle = FontStyles.Bold;
        ntxt.alignment = TextAlignmentOptions.MidlineLeft;
        ntxt.color = C_TITLE;
        ntxt.overflowMode = TextOverflowModes.Ellipsis;
        ntxt.raycastTarget = false;
        if (jp != null) ntxt.font = jp;

        // 効果テキスト
        if (item.Effects != null && item.Effects.Count > 0)
        {
            var fxGO = new GameObject("__Effects");
            fxGO.transform.SetParent(leftGO.transform, false);
            fxGO.AddComponent<RectTransform>();
            fxGO.AddComponent<LayoutElement>().preferredHeight = 55f;
            var ftxt = fxGO.AddComponent<TextMeshProUGUI>();
            ftxt.text = string.Join("  /  ",
                item.Effects.Where(e => e != null).Select(e => e.Type + " +" + e.Value));
            ftxt.fontSize = 38f; ftxt.alignment = TextAlignmentOptions.MidlineLeft;
            ftxt.color = C_EFFECT;
            ftxt.overflowMode = TextOverflowModes.Ellipsis;
            ftxt.raycastTarget = false;
            if (jp != null) ftxt.font = jp;
        }

        // 装備中バッジ（右エリア）
        if (isEquipped)
        {
            // 1. 背景用のオブジェクト
            var badgeGO = new GameObject("__Badge");
            badgeGO.transform.SetParent(row.transform, false);
            var brt = badgeGO.AddComponent<RectTransform>();
            brt.anchorMin = new Vector2(0.67f,0.18f);
            brt.anchorMax = new Vector2(0.97f,0.82f);
            brt.offsetMin = brt.offsetMax = Vector2.zero;
            badgeGO.AddComponent<Image>().color = C_BADGE_EQ;

            // 2. 文字用のオブジェクト（背景の子にする）
            var textGO = new GameObject("__BadgeText");
            textGO.transform.SetParent(badgeGO.transform, false); // badgeGOの子にする
            var trt = textGO.AddComponent<RectTransform>();
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; // 親いっぱいに広げる
            trt.offsetMin = trt.offsetMax = Vector2.zero;

            var btxt = textGO.AddComponent<TextMeshProUGUI>();
            btxt.text = "装備中"; 
            btxt.fontSize = 38f;
            btxt.alignment = TextAlignmentOptions.Center;
            btxt.color = Color.white; 
            btxt.raycastTarget = false;
            if (jp != null) btxt.font = jp;
        }
    }

    // =================== ヘルパー ===================

    private GameObject MakeGO(string name, Transform parent)
    {
        if (parent == null) return null;
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go;
    }

    private GameObject MakeRect(string name, Transform parent, Color color, float w, float h)
    {
        var go = MakeGO(name, parent);
        if (go == null) return null;
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f,0.5f);
        rt.pivot = new Vector2(0.5f,0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(w,h);
        go.AddComponent<Image>().color = color;
        return go;
    }

    private TextMeshProUGUI MakeLabel(string name, Transform parent, string text,
        TMP_FontAsset jp, float size, FontStyles style, Color color,
        float x, float y, float w, float h)
    {
        var go = MakeGO(name, parent);
        if (go == null) return null;
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f,0.5f);
        rt.pivot = new Vector2(0.5f,0.5f);
        rt.anchoredPosition = new Vector2(x,y);
        rt.sizeDelta = new Vector2(w,h);
        var txt = go.AddComponent<TextMeshProUGUI>();
        txt.text = text; txt.fontSize = size; txt.fontStyle = style;
        txt.color = color; txt.alignment = TextAlignmentOptions.Center;
        txt.raycastTarget = false;
        if (jp != null) txt.font = jp;
        return txt;
    }

    private void MakeFancyBtn(string name, Transform parent, Color color, string label,
        TMP_FontAsset jp, float fontSize,
        float x, float y, float w, float h, UnityEngine.Events.UnityAction onClick)
    {
        var go = MakeRect(name, parent, color, w, h);
        if (go == null) return;
        go.GetComponent<RectTransform>().anchoredPosition = new Vector2(x,y);
        var hi = MakeRect("__Hi", go.transform, new Color(1f,1f,1f,0.20f), w-6, h*0.5f-3);
        if (hi != null)
        {
            var hrt = hi.GetComponent<RectTransform>();
            hrt.anchorMin = new Vector2(0f,0.5f); hrt.anchorMax = new Vector2(1f,1f);
            hrt.offsetMin = new Vector2(3f,0f); hrt.offsetMax = new Vector2(-3f,-3f);
            hrt.anchoredPosition = Vector2.zero; hrt.sizeDelta = Vector2.zero;
            hi.GetComponent<Image>().raycastTarget = false;
        }
        var btn = go.AddComponent<Button>();
        btn.navigation = new Navigation { mode = Navigation.Mode.None };
        var cb = ColorBlock.defaultColorBlock;
        cb.highlightedColor = new Color(1f,1f,1f,0.82f);
        cb.pressedColor = new Color(0.75f,0.75f,0.75f,1f);
        btn.colors = cb;
        btn.onClick.AddListener(onClick);
        MakeLabel("__Txt", go.transform, label, jp, fontSize, FontStyles.Bold, Color.white, 0,0,w,h);
    }
}
