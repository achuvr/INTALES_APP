using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;

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
    private static readonly Color C_MUTED        = new Color(0.52f, 0.38f, 0.22f, 0.85f);

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
    // 装備処理
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

            // 装備中のアイテムを再度押したら脱ぐ（トグル）。違うアイテムなら付け替え。
            bool alreadyEquipped = chara.Equipment.GetItemId(item.SlotType) == item.ItemId;
            string newItemId = alreadyEquipped ? "" : item.ItemId;

            chara.Equipment.SetItemId(item.SlotType, newItemId);
            LocalEquipSave.Save(charIdx, item.SlotType, newItemId);
            Debug.Log(alreadyEquipped
                ? $"[Equip] {item.SlotType} を外した ({item.Name ?? "?"})"
                : $"[Equip] {item.SlotType} = {item.ItemId} ({item.Name ?? "?"})");

            CheckSetEffect(charIdx);
            RecalcStats(chara, charIdx).Forget();

            AssetsDatabase.instance?.PlayEquipSE();
            SafeRebuildItemList(_currentSlot);
            RefreshSlotIndicators();

            // キャラページのシルエット表示を更新（セット完成/解除で専用画像⇔デフォルトを切替）
            var cpm = FindObjectOfType<CharacterPageManager>();
            if (cpm != null) cpm.RefreshSeriesImage();
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
    // ステータス再計算
    // ================================================================
    private async UniTaskVoid RecalcStats(Character chara, int charIdx)
    {
        if (ItemRepository.instance == null) return;

        var inventory = UserDataManager.instance?.UserData?.Inventory;
        var refs = new List<InventoryRef>();
        foreach (EquipmentSlot slot in System.Enum.GetValues(typeof(EquipmentSlot)))
        {
            string itemId = LocalEquipSave.Load(charIdx, slot);
            if (string.IsNullOrEmpty(itemId)) continue;
            var inv = inventory?.FirstOrDefault(x => x.ItemId == itemId);
            if (inv != null) refs.Add(inv);
        }

        var items = await ItemRepository.instance.GetItemsAsync(refs);

        int atk = 0;
        float prob = 0f, critRate = 0f, critDmg = 0f;

        foreach (var item in items)
        {
            if (item?.Effects == null) continue;
            foreach (var fx in item.Effects)
            {
                if (fx == null) continue;
                switch (fx.Type)
                {
                    case EffectType.AtkUp:            atk      += fx.Value; break;
                    case EffectType.ProbUp:            prob     += fx.Value; break;
                    case EffectType.CriticalRateUp:    critRate += fx.Value; break;
                    case EffectType.CriticalDamageUp:  critDmg  += fx.Value; break;
                }
            }
        }

        // シリーズセットスキル（武器・頭・体・足が同一シリーズ）の効果を加算
        var set = SeriesSetBonus.GetActiveSeries(charIdx);
        if (set?.effects != null)
        {
            foreach (var fx in set.effects)
            {
                if (fx == null || !System.Enum.TryParse(fx.effectType, out EffectType t)) continue;
                switch (t)
                {
                    case EffectType.AtkUp:             atk      += fx.value; break;
                    case EffectType.ProbUp:            prob     += fx.value; break;
                    case EffectType.CriticalRateUp:    critRate += fx.value; break;
                    case EffectType.CriticalDamageUp:  critDmg  += fx.value; break;
                }
            }
        }

        chara.BaseAtk        = atk;
        chara.BaseProb       = prob;
        chara.BaseCritRate   = critRate;
        chara.BaseCritDamage = critDmg;
        chara.JobRank        = chara.Level / 50 + 1;

        Debug.Log($"[Equip] Stats: ATK={atk} Prob={prob} CritRate={critRate} CritDmg={critDmg} JobRank={chara.JobRank}");
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
        _overlay.AddComponent<Image>().color = UITheme.DIM;
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
        UITheme.ElevateCard(border, 18f, 10f, 0.35f); // モーダルを浮かせる
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
        // デザイン基盤: 明るい面は白グラデ、濃い面は控えめグラデで磨く（透明ヒットエリアは除外）
        if (color.a >= 0.5f)
        {
            if (color.r + color.g + color.b >= 2.4f) UITheme.PolishButton(go.GetComponent<Image>());
            else UITheme.PolishDarkButton(go.GetComponent<Image>());
        }
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
        UITheme.ElevateCard(border, 18f, 10f, 0.35f); // モーダルを浮かせる
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

    // ================================================================
    // セットスキル判定（シリーズ）
    // ================================================================

    /// <summary>直近で発動を通知したシリーズID（付け替えのたびに再通知しない）。</summary>
    private string _announcedSetSeriesId;

    /// <summary>
    /// 武器・頭・体・足の4部位が同一シリーズになった瞬間にSEとモーダルで発動を知らせる。
    /// 効果の適用自体は RecalcStats（ステータス）と BossBattleController（ボス戦）が行う。
    /// </summary>
    private void CheckSetEffect(int charIdx)
    {
        var set = SeriesSetBonus.GetActiveSeries(charIdx);
        string id = set?.series_id;
        if (id != null && id != _announcedSetSeriesId)
        {
            Debug.Log($"[Equip] シリーズ「{set.name}」のセットスキルが発動！");
            AssetsDatabase.instance?.PlaySetSE();
            InfoModal.Show("セットスキル発動！",
                $"シリーズ「{set.name}」\n{SeriesSetBonus.DescribeEffects(set.effects)}");
        }
        _announcedSetSeriesId = id;
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
    // アイテム一覧
    // ================================================================
    private void SafeRebuildItemList(EquipmentSlot slot)
    {
        if (_listContent == null) return;
        for (int i = _listContent.childCount - 1; i >= 0; i--)
        {
            var child = _listContent.GetChild(i);
            if (child != null && child.gameObject != null)
                DestroyImmediate(child.gameObject);
        }
        RebuildItemListAsync(slot).Forget();
    }

    private async UniTaskVoid RebuildItemListAsync(EquipmentSlot slot)
    {
        if (_listContent == null) return;
        var jp = GetJpFont();
        var chara = UserDataManager.instance?.UserData?.Characters?
            .ElementAtOrDefault(UserDataManager.instance.CurrentSelectCharacterNumber);

        // 所持品はアカウント単位（全キャラ共有）だが、装備できるのは選択中キャラの
        // 職業（＋共通 "common"）に合うアイテムだけ。職業＋スロットで絞って表示する。
        var inventory = UserDataManager.instance?.UserData?.Inventory;
        List<ItemData> items;
        if (inventory != null && inventory.Count > 0 && ItemRepository.instance != null && chara != null)
        {
            string job = chara.Job ?? "";
            var jobRefs = inventory
                .Where(r => r != null && (r.Job == job || r.Job == "common"))
                .ToList();
            items = await ItemRepository.instance.GetInventoryBySlotAsync(jobRefs, slot);
        }
        else
            items = new List<ItemData>();

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

        // 同じ装備を複数持っている場合（スキルブックA/Bは重複所持できる）は1行にまとめて所持数を表示する
        foreach (var group in items.Where(i => i != null).GroupBy(i => i.ItemId))
        {
            var item = group.First();
            MakeItemRow(item, item.IsEquippedBy(chara), jp, group.Count());
        }
    }

    // ================================================================
    // アイテム行（アイコン + 装備名 + 効果）
    // ================================================================
    private void MakeItemRow(ItemData item, bool isEquipped, TMP_FontAsset jp, int count = 1)
    {
        if (item == null) { Debug.LogError("MakeItemRow: item が null"); return; }
        if (_listContent == null) { Debug.LogError("MakeItemRow: _listContent がnull"); return; }

        const float ICON_SIZE = 160f;
        const float ICON_PAD  = 16f;


        // ---- 行背景 ----
        var row = new GameObject("__Row_" + (item.ItemId ?? "unknown"));
        row.transform.SetParent(_listContent, false);
        var rowRT  = row.AddComponent<RectTransform>();
        rowRT.anchorMin = new Vector2(0f,1f); rowRT.anchorMax = new Vector2(1f,1f);
        rowRT.pivot = new Vector2(0.5f,1f);
        var rowImg = row.AddComponent<Image>();
        rowImg.color = isEquipped ? C_ROW_EQUIPPED : C_ROW_NORMAL;
        var rowLE   = row.AddComponent<LayoutElement>();
        rowLE.preferredHeight = 290f; rowLE.flexibleWidth = 1f; rowLE.flexibleHeight = 1f;

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

        var btn = row.AddComponent<Button>();
        btn.navigation = new Navigation { mode = Navigation.Mode.None };
        var cb = ColorBlock.defaultColorBlock;
        cb.normalColor = Color.white; cb.highlightedColor = new Color(1f,1f,0.75f,1f);
        cb.pressedColor = new Color(0.85f,0.85f,0.60f,1f);
        btn.colors = cb; btn.targetGraphic = rowImg;
        var capturedItem = item;
        btn.onClick.AddListener(() => EquipItem(capturedItem));

        // ---- アイコン（左端）----
        float leftOffset = 16f;
        var cacheManager = ItemCacheManager.instance;
        if (cacheManager != null)
        {
            var sprite = cacheManager.GetIconSprite(item.ItemId);
            if (sprite != null)
            {
                var iconGO = new GameObject("__Icon");
                iconGO.transform.SetParent(row.transform, false);
                var irt = iconGO.AddComponent<RectTransform>();
                // 上下ストレッチ・左端固定・内側パディングで中央揃え
                irt.anchorMin = new Vector2(0f, 0f); irt.anchorMax = new Vector2(0f, 1f);
                irt.pivot = new Vector2(0f, 0.5f);
                irt.offsetMin = new Vector2(ICON_PAD, 20f);          // 左端・下端パディング
                irt.offsetMax = new Vector2(ICON_PAD + ICON_SIZE, -20f); // 右端・上端パディング
                irt.anchoredPosition = Vector2.zero;
                irt.pivot = new Vector2(0f, 0.5f);
                // sizeDelta はストレッチ時に不要（offsetMin/Maxで制御済み）

                var iconImg = iconGO.AddComponent<Image>();
                iconImg.sprite = sprite; iconImg.preserveAspect = true; iconImg.raycastTarget = false;
                leftOffset = ICON_SIZE + ICON_PAD * 2f;
            }
        }

        // ---- 左テキストエリア（名前＋効果） ----
        float rightPct = isEquipped ? 0.65f : 1.00f;
        var leftGO = new GameObject("__Left");
        leftGO.transform.SetParent(row.transform, false);
        var lrt = leftGO.AddComponent<RectTransform>();
        lrt.anchorMin = new Vector2(0f,0f); lrt.anchorMax = new Vector2(rightPct,1f);
        lrt.offsetMin = new Vector2(leftOffset, 0f); lrt.offsetMax = new Vector2(-8f, 0f);
        var lvlg = leftGO.AddComponent<VerticalLayoutGroup>();
        lvlg.childForceExpandWidth = true; lvlg.childForceExpandHeight = false;
        lvlg.childControlHeight = true; lvlg.childControlWidth = true;
        lvlg.spacing = 8f; lvlg.padding = new RectOffset(0, 0, 18, 14);

        var nameGO = new GameObject("__Name");
        nameGO.transform.SetParent(leftGO.transform, false);
        nameGO.AddComponent<RectTransform>();
        nameGO.AddComponent<LayoutElement>().preferredHeight = 70f;
        var ntxt = nameGO.AddComponent<TextMeshProUGUI>();
        string baseName = !string.IsNullOrEmpty(item.Name) ? item.Name : (item.ItemId ?? "---");
        ntxt.text = count > 1 ? $"{baseName} ×{count}" : baseName; // 複数所持（スキルブック）は所持数を表示
        ntxt.fontSize = 50f; ntxt.fontStyle = FontStyles.Bold;
        ntxt.alignment = TextAlignmentOptions.MidlineLeft;
        ntxt.color = C_TITLE;
        ntxt.overflowMode = TextOverflowModes.Ellipsis;
        ntxt.raycastTarget = false;
        if (jp != null) ntxt.font = jp;

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

        // 説明文
        if (!string.IsNullOrEmpty(item.Description))
        {
            var descGO = new GameObject("__Desc");
            descGO.transform.SetParent(leftGO.transform, false);
            descGO.AddComponent<RectTransform>();
            var descLE = descGO.AddComponent<LayoutElement>();
            descLE.preferredHeight = 60f; descLE.flexibleHeight = 1f;
            var dtxt = descGO.AddComponent<TextMeshProUGUI>();
            dtxt.text = item.Description;
            // 枠からはみ出しそうなら自動でフォントを縮小（最大40→最小22）。それでも入らなければ省略表示。
            dtxt.enableAutoSizing = true;
            dtxt.fontSizeMax = 40f;
            dtxt.fontSizeMin = 22f;
            dtxt.alignment = TextAlignmentOptions.TopLeft;
            dtxt.color = new Color(0.40f, 0.22f, 0.06f, 1f);
            dtxt.overflowMode = TextOverflowModes.Ellipsis;
            dtxt.enableWordWrapping = true;
            dtxt.raycastTarget = false;
            if (jp != null) dtxt.font = jp;
        }

        if (isEquipped)
        {
            var badgeGO = new GameObject("__Badge");
            badgeGO.transform.SetParent(row.transform, false);
            var brt = badgeGO.AddComponent<RectTransform>();
            brt.anchorMin = new Vector2(0.67f, 0.18f);
            brt.anchorMax = new Vector2(0.97f, 0.82f);
            brt.offsetMin = brt.offsetMax = Vector2.zero;
            badgeGO.AddComponent<Image>().color = C_BADGE_EQ;
            var textGO = new GameObject("__BadgeText");
            textGO.transform.SetParent(badgeGO.transform, false);
            var trt = textGO.AddComponent<RectTransform>();
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = trt.offsetMax = Vector2.zero;
            var btxt = textGO.AddComponent<TextMeshProUGUI>();
            btxt.text = "装備中"; btxt.fontSize = 38f;
            btxt.alignment = TextAlignmentOptions.Center;
            btxt.color = Color.white; btxt.raycastTarget = false;
            if (jp != null) btxt.font = jp;
        }

        // デザイン基盤: 行カードを軽く持ち上げる（行は再構築のたびに作り直すため、生成時の1回だけ）
        UITheme.ElevateCard(row, 12f, 6f, 0.22f);
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
        // デザイン基盤: 明るい面は白グラデ、濃い面は控えめグラデで磨く（透明ヒットエリアは除外）
        if (color.a >= 0.5f)
        {
            if (color.r + color.g + color.b >= 2.4f) UITheme.PolishButton(go.GetComponent<Image>());
            else UITheme.PolishDarkButton(go.GetComponent<Image>());
        }
        MakeLabel("__Txt", go.transform, label, jp, fontSize, FontStyles.Bold, Color.white, 0,0,w,h);
    }
}
