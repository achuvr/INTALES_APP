using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 店舗のボードゲーム一覧（コード生成UI・古びた本のデザイン）。
///
/// BoardGameListButton から new GameObject("BoardGameListRoot").AddComponent&lt;BoardGameListController&gt;()
/// で生成され、Homeの Canvas 上に全画面の一覧を構築する。データは
/// Resources/BoardGames/boardgames.json（BoardGameCatalog 経由）を読み込む。
///
/// 上部の検索ボックスでタイトル・英名・ジャンルを絞り込め、行をタップすると詳細を表示。
/// 右上の×ボタンで閉じてHomeに戻る（自分が作ったUIと BoardGameListRoot を破棄する）。
///
/// 736件超を扱うため図鑑と同じ仮想化スクロール（可視範囲の行だけ生成・使い回し）にしている。
/// </summary>
public class BoardGameListController : MonoBehaviour
{
    // 図鑑と揃えた古い本のパレット
    private static readonly Color C_PARCHMENT = new Color(0.99f, 0.95f, 0.84f, 1.00f);
    private static readonly Color C_CARD      = new Color(0.97f, 0.92f, 0.78f, 1.00f);
    private static readonly Color C_BORDER    = new Color(0.84f, 0.66f, 0.18f, 1.00f);
    private static readonly Color C_TITLE     = new Color(0.38f, 0.16f, 0.04f, 1.00f);
    private static readonly Color C_INK       = new Color(0.30f, 0.18f, 0.06f, 1.00f);
    private static readonly Color C_MUTED     = new Color(0.52f, 0.38f, 0.22f, 1.00f);
    private static readonly Color C_CHIP      = new Color(0.90f, 0.83f, 0.62f, 1.00f);
    private static readonly Color C_CLOSE     = new Color(0.90f, 0.32f, 0.32f, 1.00f);
    private static readonly Color C_SEARCHBG  = new Color(1.00f, 0.99f, 0.93f, 1.00f);

    // レイアウト定数（仮想化スクロールで手動配置する）
    private const float ROW_H    = 196f;   // 1行の高さ
    private const float SPACING  = 14f;
    private const float PAD_T    = 10f;
    private const float PAD_B    = 10f;
    private const float ROW_STEP = ROW_H + SPACING;
    private const int   BUFFER   = 2;      // 画面外に余分に出しておく行数

    private const float TITLE_H  = 264f;
    private const float SEARCH_H = 96f;

    private Canvas _canvas;
    private GameObject _overlay;
    private RectTransform _listContent;
    private ScrollRect _scrollRect;
    private RectTransform _viewport;
    private GameObject _detailModal;
    private GameObject _emptyLabel;
    private TextMeshProUGUI _countLabel;
    private TMP_FontAsset _jp;

    /// <summary>全ゲーム（読み込み時のまま）。</summary>
    private readonly List<BoardGameEntry> _all = new List<BoardGameEntry>();
    /// <summary>現在の絞り込み結果（仮想化スクロールが参照する）。</summary>
    private readonly List<BoardGameEntry> _items = new List<BoardGameEntry>();

    private string _query = "";

    // 使い回す行のプール
    private readonly Dictionary<int, RowView> _active = new Dictionary<int, RowView>();
    private readonly Stack<RowView> _free = new Stack<RowView>();
    private readonly List<int> _recycleScratch = new List<int>();

    /// <summary>使い回す1行（GameObjectと差し替える子要素の参照を保持）。</summary>
    private class RowView
    {
        public GameObject go;
        public RectTransform rt;
        public TextMeshProUGUI title;
        public TextMeshProUGUI meta;
        public TextMeshProUGUI genre;
        public BoardGameEntry entry;
        public int index = -1;
    }

    private void Start()
    {
        var canvasGO = GameObject.Find("Canvas");
        _canvas = canvasGO != null ? canvasGO.GetComponent<Canvas>() : FindFirstObjectByType<Canvas>();
        if (_canvas == null)
        {
            Debug.LogError("[BoardGame] Canvas が見つかりません");
            Destroy(gameObject);
            return;
        }
        _jp = GetJpFont();

        var data = BoardGameCatalog.Load();
        _all.AddRange(data.games ?? new BoardGameEntry[0]);

        BuildUI();
        RebuildList();
    }

    // ================================================================
    // UI構築
    // ================================================================
    private void BuildUI()
    {
        // 全画面オーバーレイ（古地図テクスチャでHomeを覆う）
        _overlay = new GameObject("__BoardGameOverlay");
        _overlay.transform.SetParent(_canvas.transform, false);
        var ort = _overlay.AddComponent<RectTransform>();
        ort.anchorMin = Vector2.zero; ort.anchorMax = Vector2.one;
        ort.offsetMin = ort.offsetMax = Vector2.zero;
        var bg = _overlay.AddComponent<Image>();
        OldMapBackground.Apply(bg);
        bg.raycastTarget = true; // タップがHome側へ抜けないように受ける

        // タイトル帯
        var titleBar = MakeRect("__TitleBar", _overlay.transform, new Color(0.40f, 0.23f, 0.08f, 0.92f), 0, TITLE_H);
        var trt = titleBar.GetComponent<RectTransform>();
        trt.anchorMin = new Vector2(0f, 1f); trt.anchorMax = new Vector2(1f, 1f);
        trt.pivot = new Vector2(0.5f, 1f);
        trt.offsetMin = new Vector2(0f, -TITLE_H); trt.offsetMax = new Vector2(0f, 0f);

        var titleGO = new GameObject("__TitleLabel");
        titleGO.transform.SetParent(titleBar.transform, false);
        var tlrt = titleGO.AddComponent<RectTransform>();
        tlrt.anchorMin = Vector2.zero; tlrt.anchorMax = Vector2.one;
        tlrt.offsetMin = new Vector2(48f, 16f); tlrt.offsetMax = new Vector2(-150f, -16f);
        var titleTmp = titleGO.AddComponent<TextMeshProUGUI>();
        if (_jp != null) titleTmp.font = _jp;
        titleTmp.text = "ボードゲーム一覧";
        titleTmp.fontSize = 84;
        titleTmp.fontStyle = FontStyles.Bold;
        titleTmp.color = new Color(0.99f, 0.94f, 0.78f);
        titleTmp.alignment = TextAlignmentOptions.MidlineLeft;
        titleTmp.raycastTarget = false;
        titleTmp.enableAutoSizing = true; titleTmp.fontSizeMax = 84; titleTmp.fontSizeMin = 48;

        // ×ボタン（右上）
        var close = MakeRoundButton("__Close", _overlay.transform, C_CLOSE, "✕", 56, Color.white, 120);
        var crt = close.GetComponent<RectTransform>();
        crt.anchorMin = crt.anchorMax = new Vector2(1f, 1f);
        crt.pivot = new Vector2(1f, 0.5f);              // 縦中央基準
        crt.anchoredPosition = new Vector2(-28, -TITLE_H / 2f); // ヘッダーの高さの中央に合わせる
        close.GetComponent<Button>().onClick.AddListener(Close);

        BuildSearch();
        BuildList();
    }

    /// <summary>検索ボックス（タイトル・英名・ジャンルを部分一致で絞り込む）。</summary>
    private void BuildSearch()
    {
        var barGO = new GameObject("__SearchBar");
        barGO.transform.SetParent(_overlay.transform, false);
        var brt = barGO.AddComponent<RectTransform>();
        brt.anchorMin = new Vector2(0f, 1f); brt.anchorMax = new Vector2(1f, 1f);
        brt.pivot = new Vector2(0.5f, 1f);
        brt.offsetMin = new Vector2(28f, -(TITLE_H + SEARCH_H)); brt.offsetMax = new Vector2(-28f, -(TITLE_H + 12f));
        var bimg = barGO.AddComponent<Image>();
        RoundedRectSprite.Apply(bimg);
        bimg.color = C_SEARCHBG;

        // 入力テキスト領域（マスク）
        var areaGO = new GameObject("__TextArea");
        areaGO.transform.SetParent(barGO.transform, false);
        var art = areaGO.AddComponent<RectTransform>();
        art.anchorMin = Vector2.zero; art.anchorMax = Vector2.one;
        art.offsetMin = new Vector2(28f, 6f); art.offsetMax = new Vector2(-220f, -6f);
        areaGO.AddComponent<RectMask2D>();

        var placeholder = new GameObject("__Placeholder");
        placeholder.transform.SetParent(areaGO.transform, false);
        var prt = placeholder.AddComponent<RectTransform>();
        prt.anchorMin = Vector2.zero; prt.anchorMax = Vector2.one;
        prt.offsetMin = prt.offsetMax = Vector2.zero;
        var ptmp = placeholder.AddComponent<TextMeshProUGUI>();
        if (_jp != null) ptmp.font = _jp;
        ptmp.text = "タイトル・ジャンルで検索…";
        ptmp.fontSize = 34; ptmp.color = C_MUTED; ptmp.fontStyle = FontStyles.Italic;
        ptmp.alignment = TextAlignmentOptions.Left;

        var textGO = new GameObject("__Text");
        textGO.transform.SetParent(areaGO.transform, false);
        var txrt = textGO.AddComponent<RectTransform>();
        txrt.anchorMin = Vector2.zero; txrt.anchorMax = Vector2.one;
        txrt.offsetMin = txrt.offsetMax = Vector2.zero;
        var ttmp = textGO.AddComponent<TextMeshProUGUI>();
        if (_jp != null) ttmp.font = _jp;
        ttmp.fontSize = 34; ttmp.color = C_INK;
        ttmp.alignment = TextAlignmentOptions.Left;

        var input = barGO.AddComponent<TMP_InputField>();
        input.textViewport = art;
        input.textComponent = ttmp;
        input.placeholder = ptmp;
        input.fontAsset = _jp;
        input.pointSize = 34;
        input.customCaretColor = true;
        input.caretColor = C_INK;
        input.selectionColor = new Color(0.84f, 0.66f, 0.18f, 0.4f);
        input.lineType = TMP_InputField.LineType.SingleLine;
        input.onValueChanged.AddListener(OnSearchChanged);

        // 件数ラベル（右側）
        var cntGO = new GameObject("__Count");
        cntGO.transform.SetParent(barGO.transform, false);
        var crt = cntGO.AddComponent<RectTransform>();
        crt.anchorMin = new Vector2(1f, 0f); crt.anchorMax = new Vector2(1f, 1f);
        crt.pivot = new Vector2(1f, 0.5f);
        crt.sizeDelta = new Vector2(200f, 0f);
        crt.anchoredPosition = new Vector2(-20f, 0f);
        _countLabel = cntGO.AddComponent<TextMeshProUGUI>();
        if (_jp != null) _countLabel.font = _jp;
        _countLabel.fontSize = 34; _countLabel.fontStyle = FontStyles.Bold; _countLabel.color = C_MUTED;
        _countLabel.alignment = TextAlignmentOptions.Right;
        _countLabel.raycastTarget = false;
    }

    private void BuildList()
    {
        var svGO = new GameObject("__Scroll");
        svGO.transform.SetParent(_overlay.transform, false);
        var svrt = svGO.AddComponent<RectTransform>();
        svrt.anchorMin = new Vector2(0f, 0f); svrt.anchorMax = new Vector2(1f, 1f);
        svrt.offsetMin = new Vector2(24f, 24f);
        svrt.offsetMax = new Vector2(-24f, -(TITLE_H + SEARCH_H + 16f));
        _scrollRect = svGO.AddComponent<ScrollRect>();
        _scrollRect.horizontal = false; _scrollRect.vertical = true; _scrollRect.scrollSensitivity = 40f;
        _scrollRect.movementType = ScrollRect.MovementType.Clamped;

        var vpGO = new GameObject("__VP");
        vpGO.transform.SetParent(svGO.transform, false);
        _viewport = vpGO.AddComponent<RectTransform>();
        _viewport.anchorMin = Vector2.zero; _viewport.anchorMax = Vector2.one;
        _viewport.offsetMin = _viewport.offsetMax = Vector2.zero;
        vpGO.AddComponent<RectMask2D>();
        _scrollRect.viewport = _viewport;

        var ctGO = new GameObject("__Content");
        ctGO.transform.SetParent(vpGO.transform, false);
        _listContent = ctGO.AddComponent<RectTransform>();
        _listContent.anchorMin = new Vector2(0f, 1f); _listContent.anchorMax = new Vector2(1f, 1f);
        _listContent.pivot = new Vector2(0.5f, 1f);
        _listContent.offsetMin = _listContent.offsetMax = Vector2.zero;
        _scrollRect.content = _listContent;

        _scrollRect.onValueChanged.AddListener(_ => UpdateVisibleRows());
    }

    // ================================================================
    // 絞り込み・リスト再構築
    // ================================================================
    private void OnSearchChanged(string q)
    {
        _query = (q ?? "").Trim();
        RebuildList();
    }

    private void RebuildList()
    {
        if (_listContent == null) return;

        // 表示中の行を全部プールへ戻す
        foreach (var kv in _active) Recycle(kv.Value);
        _active.Clear();

        // 絞り込み
        _items.Clear();
        if (string.IsNullOrEmpty(_query))
        {
            _items.AddRange(_all);
        }
        else
        {
            string q = _query.ToLowerInvariant();
            foreach (var g in _all)
                if (Matches(g, q)) _items.Add(g);
        }

        // コンテンツ高さをアイテム数から手動算出
        int n = _items.Count;
        float contentH = n > 0 ? PAD_T + n * ROW_H + (n - 1) * SPACING + PAD_B : 0f;
        _listContent.sizeDelta = new Vector2(0f, contentH);
        _listContent.anchoredPosition = Vector2.zero; // 先頭へ戻す

        if (_countLabel != null) _countLabel.text = $"{n}件";
        SetEmptyVisible(n == 0);

        Canvas.ForceUpdateCanvases();
        UpdateVisibleRows();
    }

    private static bool Matches(BoardGameEntry g, string qLower)
    {
        if (g.title != null && g.title.ToLowerInvariant().Contains(qLower)) return true;
        if (g.title_en != null && g.title_en.ToLowerInvariant().Contains(qLower)) return true;
        if (g.genre != null)
            foreach (var t in g.genre)
                if (t != null && t.ToLowerInvariant().Contains(qLower)) return true;
        return false;
    }

    private void SetEmptyVisible(bool show)
    {
        if (show && _emptyLabel == null)
        {
            _emptyLabel = new GameObject("__Empty");
            _emptyLabel.transform.SetParent(_viewport, false);
            var rt = _emptyLabel.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var tmp = _emptyLabel.AddComponent<TextMeshProUGUI>();
            if (_jp != null) tmp.font = _jp;
            tmp.text = "該当するゲームがありません";
            tmp.fontSize = 36; tmp.color = C_MUTED;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.raycastTarget = false;
        }
        if (_emptyLabel != null) _emptyLabel.SetActive(show);
    }

    // ================================================================
    // 仮想化スクロール（可視範囲の行だけ生成・配置する）
    // ================================================================
    private void UpdateVisibleRows()
    {
        if (_listContent == null || _items.Count == 0) return;

        float viewH = _viewport.rect.height;
        if (viewH < 1f) viewH = 1280f; // レイアウト未確定時のフォールバック
        float scrollY = Mathf.Max(0f, _listContent.anchoredPosition.y);

        int firstIndex = Mathf.Max(0, Mathf.FloorToInt((scrollY - PAD_T) / ROW_STEP) - BUFFER);
        int lastIndex = Mathf.Min(_items.Count - 1,
            Mathf.FloorToInt((scrollY + viewH - PAD_T) / ROW_STEP) + BUFFER);

        // 範囲外をプールへ返す
        _recycleScratch.Clear();
        foreach (var kv in _active)
            if (kv.Key < firstIndex || kv.Key > lastIndex)
                _recycleScratch.Add(kv.Key);
        foreach (int idx in _recycleScratch)
        {
            Recycle(_active[idx]);
            _active.Remove(idx);
        }

        // 範囲内の未表示インデックスへ行を割り当てる
        for (int i = firstIndex; i <= lastIndex; i++)
            if (!_active.ContainsKey(i))
                _active[i] = BindRow(i);
    }

    private void Recycle(RowView row)
    {
        if (row == null) return;
        row.index = -1;
        row.entry = null;
        row.go.SetActive(false);
        _free.Push(row);
    }

    private RowView BindRow(int index)
    {
        var row = _free.Count > 0 ? _free.Pop() : CreateRow();
        var g = _items[index];
        row.index = index;
        row.entry = g;

        row.rt.anchoredPosition = new Vector2(0f, -(PAD_T + index * ROW_STEP));

        row.go.name = "__Row_" + index;
        row.title.text = string.IsNullOrEmpty(g.title) ? "(名称不明)" : g.title;
        row.meta.text = BuildMeta(g);
        row.genre.text = (g.genre != null && g.genre.Length > 0)
            ? "ジャンル: " + string.Join("・", g.genre)
            : "ジャンル: —";

        row.go.SetActive(true);
        return row;
    }

    private static string BuildMeta(BoardGameEntry g)
    {
        var parts = new List<string>();
        parts.Add("👥 " + (string.IsNullOrEmpty(g.players) ? "—" : g.players));
        parts.Add("⏱ " + (string.IsNullOrEmpty(g.time) ? "—" : g.time));
        if (!string.IsNullOrEmpty(g.year)) parts.Add(g.year);
        return string.Join("　", parts);
    }

    /// <summary>使い回す1行の実体を生成する（中身は BindRow で差し替える）。</summary>
    private RowView CreateRow()
    {
        var row = new RowView();

        var go = new GameObject("__Row");
        go.transform.SetParent(_listContent, false);
        row.go = go;
        row.rt = go.AddComponent<RectTransform>();
        row.rt.anchorMin = new Vector2(0f, 1f); row.rt.anchorMax = new Vector2(1f, 1f);
        row.rt.pivot = new Vector2(0.5f, 1f);
        row.rt.sizeDelta = new Vector2(0f, ROW_H);
        var bg = go.AddComponent<Image>();
        RoundedRectSprite.Apply(bg);
        bg.color = C_CARD;
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = bg;
        btn.onClick.AddListener(() => { if (row.entry != null) ShowDetail(row.entry); });

        // タイトル（上部・大きめ）: 上端-14〜下端-74（offsetMin=下端, offsetMax=上端）
        row.title = MakeChildLabel(go.transform, 30, FontStyles.Bold, C_TITLE,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(28f, -74f), new Vector2(-28f, -14f),
            TextAlignmentOptions.Left);
        row.title.enableAutoSizing = true; row.title.fontSizeMax = 40; row.title.fontSizeMin = 24;
        row.title.overflowMode = TextOverflowModes.Ellipsis;

        // 人数・時間・発売年（中段）: 上端-78〜下端-126
        row.meta = MakeChildLabel(go.transform, 30, FontStyles.Bold, C_INK,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(28f, -126f), new Vector2(-28f, -78f),
            TextAlignmentOptions.Left);
        row.meta.overflowMode = TextOverflowModes.Ellipsis;

        // ジャンル（下段・1行省略）
        row.genre = MakeChildLabel(go.transform, 26, FontStyles.Normal, C_MUTED,
            new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(28f, 14f), new Vector2(-28f, 62f),
            TextAlignmentOptions.Left);
        row.genre.overflowMode = TextOverflowModes.Ellipsis;
        row.genre.enableWordWrapping = false;

        return row;
    }

    // ================================================================
    // 詳細モーダル
    // ================================================================
    private void ShowDetail(BoardGameEntry g)
    {
        CloseDetail();

        _detailModal = new GameObject("__BoardGameDetail");
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
        var border = MakeRect("__DBorder", _detailModal.transform, C_BORDER, 900, 1120);
        RoundedRectSprite.Apply(border.GetComponent<Image>());
        var panel = MakeRect("__DPanel", border.transform, C_PARCHMENT, 876, 1096);
        RoundedRectSprite.Apply(panel.GetComponent<Image>());
        panel.AddComponent<Button>().transition = Selectable.Transition.None; // パネル内タップで閉じない

        // 和名
        var name = MakeLabel(panel.transform, string.IsNullOrEmpty(g.title) ? "(名称不明)" : g.title,
            50, FontStyles.Bold, C_TITLE, 820, 120);
        var nrt = name.GetComponent<RectTransform>();
        nrt.anchorMin = new Vector2(0.5f, 1f); nrt.anchorMax = new Vector2(0.5f, 1f);
        nrt.pivot = new Vector2(0.5f, 1f);
        nrt.anchoredPosition = new Vector2(0, -40);
        var ntmp = name.GetComponent<TextMeshProUGUI>();
        ntmp.enableWordWrapping = true;
        ntmp.enableAutoSizing = true; ntmp.fontSizeMax = 50; ntmp.fontSizeMin = 30;

        // 英名
        if (!string.IsNullOrEmpty(g.title_en))
        {
            var en = MakeLabel(panel.transform, g.title_en, 30, FontStyles.Italic, C_MUTED, 820, 44);
            var ert = en.GetComponent<RectTransform>();
            ert.anchorMin = new Vector2(0.5f, 1f); ert.anchorMax = new Vector2(0.5f, 1f);
            ert.pivot = new Vector2(0.5f, 1f);
            ert.anchoredPosition = new Vector2(0, -158);
        }

        var div = MakeRect("__Div", panel.transform, new Color(0.80f, 0.62f, 0.18f, 0.6f), 760, 3)
            .GetComponent<RectTransform>();
        div.anchorMin = div.anchorMax = new Vector2(0.5f, 1f);
        div.pivot = new Vector2(0.5f, 1f);
        div.anchoredPosition = new Vector2(0, -212);

        // 情報スタック（人数／時間／発売年／ジャンル）
        var stack = new GameObject("__Stack");
        stack.transform.SetParent(panel.transform, false);
        var strt = stack.AddComponent<RectTransform>();
        strt.anchorMin = new Vector2(0f, 0f); strt.anchorMax = new Vector2(1f, 1f);
        strt.offsetMin = new Vector2(48f, 150f);   // 下端: 閉じるボタンの上
        strt.offsetMax = new Vector2(-48f, -232f);  // 上端: 区切り線の下
        var vlg = stack.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 20f;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
        vlg.childControlWidth = true; vlg.childControlHeight = true;
        vlg.childAlignment = TextAnchor.UpperLeft;

        AddInfoRow(stack.transform, "人数", string.IsNullOrEmpty(g.players) ? "—" : g.players);
        AddInfoRow(stack.transform, "プレイ時間", string.IsNullOrEmpty(g.time) ? "—" : g.time);
        if (!string.IsNullOrEmpty(g.year))
            AddInfoRow(stack.transform, "発売年", g.year);

        string genreText = (g.genre != null && g.genre.Length > 0)
            ? string.Join("　/　", g.genre) : "—";
        AddInfoBlock(stack.transform, "ジャンル", genreText);

        // 閉じるボタン
        var closeBtn = MakeRoundButton("__DClose", panel.transform, C_BORDER, "とじる", 38, C_TITLE, 0);
        var cbrt = closeBtn.GetComponent<RectTransform>();
        cbrt.sizeDelta = new Vector2(360, 100);
        cbrt.anchorMin = cbrt.anchorMax = new Vector2(0.5f, 0f);
        cbrt.pivot = new Vector2(0.5f, 0f);
        cbrt.anchoredPosition = new Vector2(0, 24);
        RoundedRectSprite.Apply(closeBtn.GetComponent<Image>());
        closeBtn.GetComponent<Button>().onClick.AddListener(CloseDetail);
    }

    /// <summary>「見出し：値」の1行（値は1行）。</summary>
    private void AddInfoRow(Transform parent, string label, string value)
    {
        var go = new GameObject("__InfoRow");
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        var le = go.AddComponent<LayoutElement>();
        le.preferredHeight = 60f;
        var tmp = go.AddComponent<TextMeshProUGUI>();
        if (_jp != null) tmp.font = _jp;
        tmp.richText = true;
        tmp.text = $"<b><color=#854d1a>{label}</color></b>　{value}";
        tmp.fontSize = 38; tmp.color = C_INK;
        tmp.alignment = TextAlignmentOptions.Left;
        tmp.raycastTarget = false;
    }

    /// <summary>「見出し」＋折り返し値のブロック（ジャンル用）。</summary>
    private void AddInfoBlock(Transform parent, string label, string value)
    {
        var go = new GameObject("__InfoBlock");
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        var le = go.AddComponent<LayoutElement>();
        le.flexibleHeight = 1f;
        le.minHeight = 120f;
        var tmp = go.AddComponent<TextMeshProUGUI>();
        if (_jp != null) tmp.font = _jp;
        tmp.richText = true;
        tmp.text = $"<b><color=#854d1a>{label}</color></b>\n{value}";
        tmp.fontSize = 34; tmp.color = C_INK;
        tmp.alignment = TextAlignmentOptions.TopLeft;
        tmp.enableWordWrapping = true;
        tmp.overflowMode = TextOverflowModes.Ellipsis;
        tmp.raycastTarget = false;
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
        if (_overlay != null) Destroy(_overlay);
        Destroy(gameObject); // BoardGameListRoot ごと破棄
    }

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

    /// <summary>親内に配置する固定サイズのラベル（中央基準）。</summary>
    private GameObject MakeLabel(Transform parent, string text, float size, FontStyles style,
        Color color, float w, float h)
    {
        var go = new GameObject("__Label");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(w, h);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        if (_jp != null) tmp.font = _jp;
        tmp.text = text;
        tmp.fontSize = size;
        tmp.fontStyle = style;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;
        return go;
    }

    /// <summary>行カード内に貼るアンカー指定のラベル。</summary>
    private TextMeshProUGUI MakeChildLabel(Transform parent, float size, FontStyles style, Color color,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax, TextAlignmentOptions align)
    {
        var go = new GameObject("__L");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
        rt.offsetMin = offsetMin; rt.offsetMax = offsetMax;
        var tmp = go.AddComponent<TextMeshProUGUI>();
        if (_jp != null) tmp.font = _jp;
        tmp.fontSize = size;
        tmp.fontStyle = style;
        tmp.color = color;
        tmp.alignment = align;
        tmp.raycastTarget = false;
        return tmp;
    }

    private GameObject MakeRoundButton(string name, Transform parent, Color bg, string text,
        float fontSize, Color textColor, float diameter)
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

        var labelGO = new GameObject("__Label");
        labelGO.transform.SetParent(go.transform, false);
        var lrt = labelGO.AddComponent<RectTransform>();
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
        lrt.offsetMin = lrt.offsetMax = Vector2.zero;
        var tmp = labelGO.AddComponent<TextMeshProUGUI>();
        if (_jp != null) tmp.font = _jp;
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color = textColor;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;
        return go;
    }
}
