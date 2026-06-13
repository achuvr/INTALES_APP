using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 装備図鑑（コード生成UI・古びた本のデザイン）。
///
/// Zukanシーンのルート（ZukanRoot）にアタッチされ、SceneLoader.MergeScene("Zukan")で
/// Homeに加算マージされて起動する。Homeの Canvas 上に全画面の図鑑を構築し、
/// master/items（ItemCacheManagerのローカルキャッシュ）の全装備を一覧表示する。
/// 装備種別（武器/頭/体/足/本A/本B）でフィルタでき、カードをタップすると詳細を表示。
/// 右上の×ボタンで図鑑を閉じてHomeに戻る（自分が作ったUIと ZukanRoot を破棄する）。
///
/// 全画面の不透明背景でHomeを覆うため、見た目は専用シーンに遷移したのと同じになる
/// （実際は加算マージなので UserDataManager / ItemCacheManager などの
/// シングルトンが生き残り、Home復帰時にデータが消える問題が起きない）。
/// </summary>
public class ZukanController : MonoBehaviour
{
    private static readonly Color C_PARCHMENT = new Color(0.99f, 0.95f, 0.84f, 1.00f);
    private static readonly Color C_CARD      = new Color(0.97f, 0.92f, 0.78f, 1.00f);
    private static readonly Color C_BORDER    = new Color(0.84f, 0.66f, 0.18f, 1.00f);
    private static readonly Color C_TITLE     = new Color(0.38f, 0.16f, 0.04f, 1.00f);
    private static readonly Color C_INK       = new Color(0.30f, 0.18f, 0.06f, 1.00f);
    private static readonly Color C_MUTED     = new Color(0.52f, 0.38f, 0.22f, 1.00f);
    private static readonly Color C_TAB_ON    = new Color(0.84f, 0.66f, 0.18f, 1.00f);
    private static readonly Color C_TAB_OFF   = new Color(0.86f, 0.78f, 0.58f, 1.00f);
    private static readonly Color C_CLOSE     = new Color(0.90f, 0.32f, 0.32f, 1.00f);
    private static readonly Color C_EFFECT    = new Color(0.52f, 0.28f, 0.06f, 1.00f);

    /// <summary>装備カテゴリのフィルタタブ（表示名, slot_type。"" = すべて）</summary>
    private static readonly (string label, string slot)[] TABS =
    {
        ("すべて", ""),
        ("武器",  "weapon"),
        ("頭",    "head"),
        ("体",    "body"),
        ("足",    "feet"),
        ("本A",   "skill_book_a"),
        ("本B",   "skill_book_b"),
    };

    /// <summary>職業のフィルタタブ（表示名, job, 職業アイコンのResource名。"" = すべて）</summary>
    private static readonly (string label, string job, string icon)[] JOB_TABS =
    {
        ("すべて",   "",         null),
        ("戦士",     "warrior",  "JobIcons/warrior1"),
        ("魔法使い", "magician", "JobIcons/magician1"),
        ("弓使い",   "archer",   "JobIcons/archer1"),
        ("銃使い",   "gunner",   "JobIcons/gunner1"),
        ("共通",     "common",   null),
    };

    private Canvas _canvas;
    private GameObject _overlay;
    private RectTransform _gridContent;
    private GameObject _detailModal;
    private string _currentSlot = "";
    private string _currentJob = "";
    private readonly List<(Button btn, Image bg, string slot)> _tabButtons = new List<(Button, Image, string)>();
    private readonly List<(Button btn, Image bg, string job)> _jobButtons = new List<(Button, Image, string)>();

    // アイコン遅延ロード用（カードのアイコンImageと item_id、世代カウンタ）
    private readonly List<(Image img, string itemId)> _pendingIcons = new List<(Image, string)>();
    private int _gridGeneration;

    private void Start()
    {
        var canvasGO = GameObject.Find("Canvas");
        _canvas = canvasGO != null ? canvasGO.GetComponent<Canvas>() : FindFirstObjectByType<Canvas>();
        if (_canvas == null)
        {
            Debug.LogError("[Zukan] Canvas が見つかりません");
            Destroy(gameObject);
            return;
        }
        BuildUI();
        RebuildGrid();
    }

    // ================================================================
    // UI構築
    // ================================================================
    private void BuildUI()
    {
        var jp = GetJpFont();

        // 全画面オーバーレイ（古地図テクスチャでHomeを覆う）
        _overlay = new GameObject("__ZukanOverlay");
        _overlay.transform.SetParent(_canvas.transform, false);
        var ort = _overlay.AddComponent<RectTransform>();
        ort.anchorMin = Vector2.zero; ort.anchorMax = Vector2.one;
        ort.offsetMin = ort.offsetMax = Vector2.zero;
        var bg = _overlay.AddComponent<Image>();
        OldMapBackground.Apply(bg);
        // タップがHome側へ抜けないように背景でレイキャストを受ける
        bg.raycastTarget = true;

        // タイトル帯
        var titleBar = MakeRect("__TitleBar", _overlay.transform, new Color(0.40f, 0.23f, 0.08f, 0.92f), 0, 150);
        var trt = titleBar.GetComponent<RectTransform>();
        trt.anchorMin = new Vector2(0f, 1f); trt.anchorMax = new Vector2(1f, 1f);
        trt.pivot = new Vector2(0.5f, 1f);
        trt.offsetMin = new Vector2(0f, -150f); trt.offsetMax = new Vector2(0f, 0f);
        // タイトル文字は左上に寄せる（左パディングを取り左揃え）
        var titleGO = new GameObject("__TitleLabel");
        titleGO.transform.SetParent(titleBar.transform, false);
        var tlrt = titleGO.AddComponent<RectTransform>();
        tlrt.anchorMin = Vector2.zero; tlrt.anchorMax = Vector2.one;
        tlrt.offsetMin = new Vector2(40f, 0f); tlrt.offsetMax = new Vector2(-140f, 0f);
        var titleTmp = titleGO.AddComponent<TextMeshProUGUI>();
        if (jp != null) titleTmp.font = jp;
        titleTmp.text = "装備図鑑";
        titleTmp.fontSize = 52;
        titleTmp.fontStyle = FontStyles.Bold;
        titleTmp.color = new Color(0.98f, 0.92f, 0.74f);
        titleTmp.alignment = TextAlignmentOptions.MidlineLeft;
        titleTmp.raycastTarget = false;

        // ×ボタン（右上）
        var close = MakeRoundButton("__Close", _overlay.transform, C_CLOSE, "✕", jp, 56, Color.white, 110);
        var crt = close.GetComponent<RectTransform>();
        crt.anchorMin = crt.anchorMax = new Vector2(1f, 1f);
        crt.pivot = new Vector2(1f, 1f);
        crt.anchoredPosition = new Vector2(-24, -20);
        close.GetComponent<Button>().onClick.AddListener(Close);

        // フィルタタブ（上段=装備カテゴリ、下段=職業）
        BuildTabs(jp);
        BuildJobTabs(jp);

        // 装備カードのスクロールグリッド
        BuildGrid();
    }

    private void BuildTabs(TMP_FontAsset jp)
    {
        var bar = new GameObject("__TabBar");
        bar.transform.SetParent(_overlay.transform, false);
        var brt = bar.AddComponent<RectTransform>();
        brt.anchorMin = new Vector2(0f, 1f); brt.anchorMax = new Vector2(1f, 1f);
        brt.pivot = new Vector2(0.5f, 1f);
        brt.offsetMin = new Vector2(24f, -274f); brt.offsetMax = new Vector2(-24f, -170f);

        var hlg = bar.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 10f;
        hlg.childForceExpandWidth = true;
        hlg.childForceExpandHeight = true;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;

        _tabButtons.Clear();
        foreach (var (label, slot) in TABS)
        {
            var go = new GameObject($"__Tab_{(slot == "" ? "all" : slot)}");
            go.transform.SetParent(bar.transform, false);
            go.AddComponent<RectTransform>();
            var img = go.AddComponent<Image>();
            RoundedRectSprite.Apply(img);
            img.color = slot == _currentSlot ? C_TAB_ON : C_TAB_OFF;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            string captured = slot;
            btn.onClick.AddListener(() => OnTab(captured));
            MakeLabel(go.transform, label, jp, 34, FontStyles.Bold, C_TITLE, 0, 0, stretch: true);
            _tabButtons.Add((btn, img, slot));
        }
    }

    /// <summary>職業フィルタ行（装備カテゴリとは独立。職業アイコン＋名前）</summary>
    private void BuildJobTabs(TMP_FontAsset jp)
    {
        var bar = new GameObject("__JobTabBar");
        bar.transform.SetParent(_overlay.transform, false);
        var brt = bar.AddComponent<RectTransform>();
        brt.anchorMin = new Vector2(0f, 1f); brt.anchorMax = new Vector2(1f, 1f);
        brt.pivot = new Vector2(0.5f, 1f);
        brt.offsetMin = new Vector2(24f, -440f); brt.offsetMax = new Vector2(-24f, -284f);

        var hlg = bar.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 10f;
        hlg.childForceExpandWidth = true;
        hlg.childForceExpandHeight = true;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;

        _jobButtons.Clear();
        foreach (var (label, job, icon) in JOB_TABS)
        {
            var go = new GameObject($"__JobTab_{(job == "" ? "all" : job)}");
            go.transform.SetParent(bar.transform, false);
            go.AddComponent<RectTransform>();
            var img = go.AddComponent<Image>();
            RoundedRectSprite.Apply(img);
            img.color = job == _currentJob ? C_TAB_ON : C_TAB_OFF;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            string captured = job;
            btn.onClick.AddListener(() => OnJob(captured));

            // 職業アイコン（上部）＋名前（下部）。アイコンが無い項目は名前のみ中央
            var sprite = string.IsNullOrEmpty(icon) ? null : Resources.Load<Sprite>(icon);
            if (sprite != null)
            {
                var iconGO = new GameObject("__JobIcon");
                iconGO.transform.SetParent(go.transform, false);
                var irt = iconGO.AddComponent<RectTransform>();
                irt.anchorMin = new Vector2(0.5f, 1f); irt.anchorMax = new Vector2(0.5f, 1f);
                irt.pivot = new Vector2(0.5f, 1f);
                irt.sizeDelta = new Vector2(96, 96);
                irt.anchoredPosition = new Vector2(0, -6);
                var iimg = iconGO.AddComponent<Image>();
                iimg.sprite = sprite; iimg.preserveAspect = true; iimg.raycastTarget = false;

                var nameGO = new GameObject("__JobName");
                nameGO.transform.SetParent(go.transform, false);
                var nrt = nameGO.AddComponent<RectTransform>();
                nrt.anchorMin = new Vector2(0f, 0f); nrt.anchorMax = new Vector2(1f, 0f);
                nrt.pivot = new Vector2(0.5f, 0f);
                nrt.offsetMin = new Vector2(2f, 6f); nrt.offsetMax = new Vector2(-2f, 44f);
                var ntmp = nameGO.AddComponent<TextMeshProUGUI>();
                if (jp != null) ntmp.font = jp;
                ntmp.text = label; ntmp.fontSize = 24; ntmp.fontStyle = FontStyles.Bold;
                ntmp.color = C_TITLE; ntmp.alignment = TextAlignmentOptions.Center;
                ntmp.raycastTarget = false;
                ntmp.enableAutoSizing = true; ntmp.fontSizeMax = 24; ntmp.fontSizeMin = 14;
            }
            else
            {
                MakeLabel(go.transform, label, jp, 30, FontStyles.Bold, C_TITLE, 0, 0, stretch: true);
            }
            _jobButtons.Add((btn, img, job));
        }
    }

    private void BuildGrid()
    {
        var svGO = new GameObject("__Scroll");
        svGO.transform.SetParent(_overlay.transform, false);
        var svrt = svGO.AddComponent<RectTransform>();
        svrt.anchorMin = new Vector2(0f, 0f); svrt.anchorMax = new Vector2(1f, 1f);
        svrt.offsetMin = new Vector2(28f, 28f); svrt.offsetMax = new Vector2(-28f, -452f);
        var scroll = svGO.AddComponent<ScrollRect>();
        scroll.horizontal = false; scroll.vertical = true; scroll.scrollSensitivity = 40f;
        scroll.movementType = ScrollRect.MovementType.Clamped;

        var vpGO = new GameObject("__VP");
        vpGO.transform.SetParent(svGO.transform, false);
        var vprt = vpGO.AddComponent<RectTransform>();
        vprt.anchorMin = Vector2.zero; vprt.anchorMax = Vector2.one;
        vprt.offsetMin = vprt.offsetMax = Vector2.zero;
        vpGO.AddComponent<RectMask2D>();
        scroll.viewport = vprt;

        var ctGO = new GameObject("__Content");
        ctGO.transform.SetParent(vpGO.transform, false);
        _gridContent = ctGO.AddComponent<RectTransform>();
        _gridContent.anchorMin = new Vector2(0f, 1f); _gridContent.anchorMax = new Vector2(1f, 1f);
        _gridContent.pivot = new Vector2(0.5f, 1f);
        _gridContent.offsetMin = _gridContent.offsetMax = Vector2.zero;

        var grid = ctGO.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(316, 392);
        grid.spacing = new Vector2(16, 16);
        grid.padding = new RectOffset(8, 8, 8, 8);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 3;
        grid.childAlignment = TextAnchor.UpperCenter;
        ctGO.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.content = _gridContent;
    }

    // ================================================================
    // フィルタ・グリッド再構築
    // ================================================================
    private void OnTab(string slot)
    {
        _currentSlot = slot;
        foreach (var t in _tabButtons)
            t.bg.color = t.slot == _currentSlot ? C_TAB_ON : C_TAB_OFF;
        RebuildGrid();
    }

    private void OnJob(string job)
    {
        _currentJob = job;
        foreach (var t in _jobButtons)
            t.bg.color = t.job == _currentJob ? C_TAB_ON : C_TAB_OFF;
        RebuildGrid();
    }

    private void RebuildGrid()
    {
        if (_gridContent == null) return;
        _gridGeneration++;     // 進行中のアイコン遅延ロードを無効化
        _pendingIcons.Clear();
        for (int i = _gridContent.childCount - 1; i >= 0; i--)
            Destroy(_gridContent.GetChild(i).gameObject);

        var jp = GetJpFont();
        var items = GetItems()
            .Where(it => _currentSlot == "" || it.slot_type == _currentSlot)
            .Where(it => _currentJob == "" || it.job == _currentJob)
            .OrderBy(it => SlotOrder(it.slot_type))
            .ThenBy(it => JobOrder(it.job))
            .ThenBy(it => it.name)
            .ToList();

        if (items.Count == 0)
        {
            var em = new GameObject("__Empty");
            em.transform.SetParent(_gridContent, false);
            em.AddComponent<RectTransform>();
            // グリッドの1セルとして中央に出す
            var le = em.AddComponent<LayoutElement>();
            le.ignoreLayout = false;
            var tmp = em.AddComponent<TextMeshProUGUI>();
            if (jp != null) tmp.font = jp;
            tmp.text = "図鑑データがありません\n（通信後にもう一度開いてください）";
            tmp.fontSize = 34; tmp.color = C_MUTED;
            tmp.alignment = TextAlignmentOptions.Center;
            return;
        }

        foreach (var item in items)
            MakeCard(item, jp);

        // アイコンを一度に読むと実機で重く落ちるため、数枚ずつ遅延読み込みする
        LoadIconsAsync(_gridGeneration).Forget();
    }

    /// <summary>カードのアイコンを数枚ずつ遅延読み込みする（フリーズと一括メモリ確保を避ける）。</summary>
    private async UniTaskVoid LoadIconsAsync(int generation)
    {
        const int PER_FRAME = 3;
        var cache = ItemCacheManager.instance;
        if (cache == null) return;

        var pending = _pendingIcons.ToList();
        for (int i = 0; i < pending.Count; i++)
        {
            if (generation != _gridGeneration) return; // 別フィルタに切り替わった/閉じた
            var (img, id) = pending[i];
            if (img == null) continue;                  // カードが破棄済み

            var sprite = cache.GetIconSprite(id);
            if (generation != _gridGeneration) return;
            if (img != null && sprite != null)
            {
                img.sprite = sprite;
                img.color = Color.white;
            }

            if (i % PER_FRAME == PER_FRAME - 1)
                await UniTask.Yield();
        }
    }

    /// <summary>装備1件のカード（アイコン＋名前）。タップで詳細表示</summary>
    private void MakeCard(CachedItem item, TMP_FontAsset jp)
    {
        var card = new GameObject("__Card_" + item.item_id);
        card.transform.SetParent(_gridContent, false);
        card.AddComponent<RectTransform>();
        var bg = card.AddComponent<Image>();
        RoundedRectSprite.Apply(bg);
        bg.color = C_CARD;
        var btn = card.AddComponent<Button>();
        btn.targetGraphic = bg;
        var captured = item;
        btn.onClick.AddListener(() => ShowDetail(captured));

        // アイコン（上部・正方形）
        var iconGO = new GameObject("__Icon");
        iconGO.transform.SetParent(card.transform, false);
        var irt = iconGO.AddComponent<RectTransform>();
        irt.anchorMin = new Vector2(0.5f, 1f); irt.anchorMax = new Vector2(0.5f, 1f);
        irt.pivot = new Vector2(0.5f, 1f);
        irt.sizeDelta = new Vector2(250, 250);
        irt.anchoredPosition = new Vector2(0, -20);
        var iimg = iconGO.AddComponent<Image>();
        iimg.preserveAspect = true; iimg.raycastTarget = false;
        // プレースホルダを置き、実アイコンは LoadIconsAsync で後から差し込む（フリーズ防止）
        iimg.color = new Color(0.80f, 0.72f, 0.52f);
        RoundedRectSprite.Apply(iimg);
        if (ItemCacheManager.instance != null && ItemCacheManager.instance.HasIcon(item.item_id))
            _pendingIcons.Add((iimg, item.item_id));

        // スロット種別の小タグ（右上）
        var tag = MakeRect("__Tag", card.transform, C_TAB_ON, 96, 44);
        var tagrt = tag.GetComponent<RectTransform>();
        tagrt.anchorMin = tagrt.anchorMax = new Vector2(1f, 1f);
        tagrt.pivot = new Vector2(1f, 1f);
        tagrt.anchoredPosition = new Vector2(-8, -8);
        RoundedRectSprite.Apply(tag.GetComponent<Image>());
        MakeLabel(tag.transform, SlotJp(item.slot_type), jp, 24, FontStyles.Bold, Color.white, 0, 0, stretch: true);

        // 名前（下部）
        var nameGO = new GameObject("__Name");
        nameGO.transform.SetParent(card.transform, false);
        var nrt = nameGO.AddComponent<RectTransform>();
        nrt.anchorMin = new Vector2(0f, 0f); nrt.anchorMax = new Vector2(1f, 0f);
        nrt.pivot = new Vector2(0.5f, 0f);
        nrt.offsetMin = new Vector2(10f, 12f); nrt.offsetMax = new Vector2(-10f, 120f);
        var ntmp = nameGO.AddComponent<TextMeshProUGUI>();
        if (jp != null) ntmp.font = jp;
        ntmp.text = !string.IsNullOrEmpty(item.name) ? item.name : item.item_id;
        ntmp.fontSize = 30; ntmp.fontStyle = FontStyles.Bold; ntmp.color = C_TITLE;
        ntmp.alignment = TextAlignmentOptions.Top;
        ntmp.enableWordWrapping = true;
        ntmp.overflowMode = TextOverflowModes.Ellipsis;
        ntmp.raycastTarget = false;
        ntmp.enableAutoSizing = true; ntmp.fontSizeMax = 30; ntmp.fontSizeMin = 20;
    }

    // ================================================================
    // 詳細モーダル
    // ================================================================
    private void ShowDetail(CachedItem item)
    {
        var jp = GetJpFont();
        CloseDetail();

        _detailModal = new GameObject("__ZukanDetail");
        _detailModal.transform.SetParent(_overlay.transform, false);
        var drt = _detailModal.AddComponent<RectTransform>();
        drt.anchorMin = Vector2.zero; drt.anchorMax = Vector2.one;
        drt.offsetMin = drt.offsetMax = Vector2.zero;
        var dim = _detailModal.AddComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.6f);
        var dimBtn = _detailModal.AddComponent<Button>();
        dimBtn.transition = Selectable.Transition.None;
        dimBtn.onClick.AddListener(CloseDetail);

        // パネル
        var border = MakeRect("__DBorder", _detailModal.transform, C_BORDER, 900, 1180);
        RoundedRectSprite.Apply(border.GetComponent<Image>());
        var panel = MakeRect("__DPanel", border.transform, C_PARCHMENT, 876, 1156);
        RoundedRectSprite.Apply(panel.GetComponent<Image>());
        // パネル内タップはモーダルを閉じない
        panel.AddComponent<Button>().transition = Selectable.Transition.None;

        // 大アイコン
        var iconGO = new GameObject("__DIcon");
        iconGO.transform.SetParent(panel.transform, false);
        var irt = iconGO.AddComponent<RectTransform>();
        irt.anchorMin = new Vector2(0.5f, 1f); irt.anchorMax = new Vector2(0.5f, 1f);
        irt.pivot = new Vector2(0.5f, 1f);
        irt.sizeDelta = new Vector2(380, 380);
        irt.anchoredPosition = new Vector2(0, -40);
        var iimg = iconGO.AddComponent<Image>();
        iimg.preserveAspect = true; iimg.raycastTarget = false;
        var sprite = ItemCacheManager.instance != null ? ItemCacheManager.instance.GetIconSprite(item.item_id) : null;
        if (sprite != null) iimg.sprite = sprite;
        else { iimg.color = new Color(0.80f, 0.72f, 0.52f); RoundedRectSprite.Apply(iimg); }

        // 名前
        var name = MakeLabel(panel.transform, !string.IsNullOrEmpty(item.name) ? item.name : item.item_id,
            jp, 50, FontStyles.Bold, C_TITLE, 0, 0, stretch: false);
        var nrt = name.GetComponent<RectTransform>();
        nrt.anchorMin = new Vector2(0.5f, 1f); nrt.anchorMax = new Vector2(0.5f, 1f);
        nrt.pivot = new Vector2(0.5f, 1f);
        nrt.sizeDelta = new Vector2(820, 90);
        nrt.anchoredPosition = new Vector2(0, -435);
        var ntmp = name.GetComponent<TextMeshProUGUI>();
        ntmp.enableAutoSizing = true; ntmp.fontSizeMax = 50; ntmp.fontSizeMin = 30;

        // 種別・職業タグ（区切り線と重ならないよう名前のすぐ下に置く）
        string tagText = $"{SlotJp(item.slot_type)}　/　{JobJp(item.job)}";
        var tags = MakeLabel(panel.transform, tagText, jp, 34, FontStyles.Bold, C_MUTED, 0, 0, stretch: false);
        var tgrt = tags.GetComponent<RectTransform>();
        tgrt.anchorMin = new Vector2(0.5f, 1f); tgrt.anchorMax = new Vector2(0.5f, 1f);
        tgrt.pivot = new Vector2(0.5f, 1f);
        tgrt.sizeDelta = new Vector2(820, 50);
        tgrt.anchoredPosition = new Vector2(0, -530);

        var div = MakeRect("__Div", panel.transform, new Color(0.80f, 0.62f, 0.18f, 0.6f), 760, 3)
            .GetComponent<RectTransform>();
        div.anchorMin = div.anchorMax = new Vector2(0.5f, 1f);
        div.pivot = new Vector2(0.5f, 1f);
        div.anchoredPosition = new Vector2(0, -595);

        // 効果・説明・セットを縦に自動整列（重なり防止）。
        // 区切り線の下から閉じるボタンの上までの範囲に配置する。
        var stack = new GameObject("__Stack");
        stack.transform.SetParent(panel.transform, false);
        var strt = stack.AddComponent<RectTransform>();
        strt.anchorMin = new Vector2(0f, 0f); strt.anchorMax = new Vector2(1f, 1f);
        strt.offsetMin = new Vector2(40f, 150f);   // 下端: 閉じるボタンの上
        strt.offsetMax = new Vector2(-40f, -615f); // 上端: 区切り線の下
        var vlg = stack.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 18f;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
        vlg.childControlWidth = true; vlg.childControlHeight = true;
        vlg.childAlignment = TextAnchor.UpperCenter;

        // 効果
        if (item.effects != null && item.effects.Count > 0)
        {
            string fx = string.Join("　/　", item.effects
                .Where(e => e != null)
                .Select(e => $"{EffectJp(e.effectType)} +{e.value}"));
            AddStackLabel(stack.transform, fx, jp, 48, FontStyles.Bold, C_EFFECT, 130f);
        }

        // 説明（詳細コメント）。1行に収まるなら中央、折り返して2行以上になるなら左揃え。
        if (!string.IsNullOrEmpty(item.description))
        {
            var descTmp = AddStackLabel(stack.transform, item.description, jp, 32, FontStyles.Normal, C_INK, 200f,
                flexible: true, align: TextAlignmentOptions.Top);
            // 説明欄の表示幅（パネル876 - スタック左右余白40×2）。
            // 改行を含む or 1行幅が表示幅を超える（=折り返す）場合のみ左揃えにする。
            const float availWidth = 876f - 80f;
            bool multiLine = item.description.Contains('\n')
                || descTmp.GetPreferredValues(item.description).x > availWidth;
            descTmp.alignment = multiLine ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.Top;
        }

        // セット（game）名
        if (!string.IsNullOrEmpty(item.game))
            AddStackLabel(stack.transform, $"セット: {item.game}", jp, 28, FontStyles.Italic, C_MUTED, 50f);

        // 閉じるボタン
        var closeBtn = MakeRoundButton("__DClose", panel.transform, C_BORDER, "とじる", jp, 38, C_TITLE, 0);
        var cbrt = closeBtn.GetComponent<RectTransform>();
        cbrt.sizeDelta = new Vector2(360, 100);
        cbrt.anchorMin = cbrt.anchorMax = new Vector2(0.5f, 0f);
        cbrt.pivot = new Vector2(0.5f, 0f);
        cbrt.anchoredPosition = new Vector2(0, 24);
        RoundedRectSprite.Apply(closeBtn.GetComponent<Image>());
        closeBtn.GetComponent<Button>().onClick.AddListener(CloseDetail);
    }

    private void CloseDetail()
    {
        if (_detailModal != null) Destroy(_detailModal);
        _detailModal = null;
    }

    // ================================================================
    // 閉じる（Homeに戻る）
    // ================================================================
    private void Close()
    {
        _gridGeneration++; // 進行中のアイコン遅延ロードを止める
        if (_overlay != null) Destroy(_overlay);
        Destroy(gameObject); // ZukanRoot ごと破棄
    }

    // ================================================================
    // データ・変換
    // ================================================================
    private static List<CachedItem> GetItems()
    {
        var cache = ItemCacheManager.instance;
        return cache != null ? cache.GetAll() : new List<CachedItem>();
    }

    private static int SlotOrder(string slot) => slot switch
    {
        "weapon" => 0, "head" => 1, "body" => 2, "feet" => 3,
        "skill_book_a" => 4, "skill_book_b" => 5, _ => 9,
    };

    private static int JobOrder(string job) => job switch
    {
        "warrior" => 0, "magician" => 1, "archer" => 2, "gunner" => 3, "common" => 4, _ => 9,
    };

    private static string SlotJp(string slot) => slot switch
    {
        "weapon" => "武器", "head" => "頭", "body" => "体", "feet" => "足",
        "skill_book_a" => "本A", "skill_book_b" => "本B", _ => "?",
    };

    private static string JobJp(string job) => job switch
    {
        "warrior" => "戦士", "magician" => "魔法使い", "archer" => "弓使い",
        "gunner" => "銃使い", "common" => "共通", _ => job,
    };

    private static string EffectJp(string effectType) => effectType switch
    {
        "AtkUp" => "攻撃力", "DefUp" => "防御力", "HpUp" => "HP", "SpeedUp" => "速度",
        "BonusExp" => "経験値", "CriticalRateUp" => "クリ率", "GoldBonus" => "ゴールド",
        "SkillSlotUnlock" => "スキル枠", "CriticalDamageUp" => "クリダメージ",
        "SpecialAbility" => "特殊能力", "ProbUp" => "確率", _ => effectType,
    };

    // ================================================================
    // UI部品ヘルパー
    // ================================================================
    private static TMP_FontAsset GetJpFont()
    {
        var fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
        return fonts.FirstOrDefault(f => f.name.ToLower() == "jp") ?? fonts.FirstOrDefault();
    }

    private static GameObject MakeRect(string name, Transform parent, Color color, float w, float h)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(w, h);
        go.AddComponent<Image>().color = color;
        return go;
    }

    /// <summary>ラベル。stretch=trueなら親いっぱいに広げて中央寄せ</summary>
    private static GameObject MakeLabel(Transform parent, string text, TMP_FontAsset font,
        float size, FontStyles style, Color color, float w, float h, bool stretch)
    {
        var go = new GameObject("__Label");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        if (stretch)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }
        else
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(w, h);
        }
        var tmp = go.AddComponent<TextMeshProUGUI>();
        if (font != null) tmp.font = font;
        tmp.text = text;
        tmp.fontSize = size;
        tmp.fontStyle = style;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;
        return go;
    }

    /// <summary>縦レイアウトグループ内の1行ラベル（自動折り返し・高さ指定）</summary>
    private static TextMeshProUGUI AddStackLabel(Transform parent, string text, TMP_FontAsset font,
        float size, FontStyles style, Color color, float preferredHeight, bool flexible = false,
        TextAlignmentOptions align = TextAlignmentOptions.Top)
    {
        var go = new GameObject("__StackLabel");
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        var le = go.AddComponent<LayoutElement>();
        le.preferredHeight = preferredHeight;
        if (flexible) le.flexibleHeight = 1f;
        var tmp = go.AddComponent<TextMeshProUGUI>();
        if (font != null) tmp.font = font;
        tmp.text = text;
        tmp.fontSize = size;
        tmp.fontStyle = style;
        tmp.color = color;
        tmp.alignment = align;
        tmp.enableWordWrapping = true;
        tmp.overflowMode = TextOverflowModes.Ellipsis;
        tmp.raycastTarget = false;
        return tmp;
    }

    private static GameObject MakeRoundButton(string name, Transform parent, Color bg, string text,
        TMP_FontAsset font, float fontSize, Color textColor, float diameter)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        if (diameter > 0) rt.sizeDelta = new Vector2(diameter, diameter);
        var img = go.AddComponent<Image>();
        RoundedRectSprite.Apply(img);
        img.color = bg;
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        MakeLabel(go.transform, text, font, fontSize, FontStyles.Bold, textColor, 0, 0, stretch: true);
        return go;
    }
}
