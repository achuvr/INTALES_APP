using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 店舗のボードゲーム一覧（コード生成UI・古びた本のデザイン）。
///
/// BoardGameListButton から new GameObject("BoardGameListRoot").AddComponent&lt;BoardGameListController&gt;()
/// で生成され、Homeの Canvas 上に全画面の一覧を構築する。データは
/// Resources/BoardGames/boardgames.json（BoardGameCatalog 経由）を読み込む。
///
/// 上部の検索ボックスでタイトル・英名・ジャンルを絞り込め、その下のボタンで
/// 人数・プレイ時間の絞り込みと並び替え（五十音順/時間が短い順/人気順=当店お気に入り数）ができる。
/// さらにその下のチェックで「遊んだのみ」「お気に入りのみ」に絞り込める。
/// 各行の✓（遊んだ）・★（お気に入り）は BoardGameMarks 経由で Firestore に保存される。
/// 行をタップすると詳細を表示。詳細のデザイナー名・ジャンルタグはリンクになっていて、
/// タップするとそのデザイナー/ジャンルで一覧を絞り込む（チェック行の下に解除チップを表示）。
/// 詳細下部の「遊んだ記録」ではカメラで撮影した写真を
/// 貼り付けられる（BoardGamePhotoStore。端末ローカル保存のみでFirestoreには送らない）。
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
    private const float FILTER_H = 96f;
    private const float CHECKS_H = 84f;    // 「遊んだのみ」「お気に入りのみ」チェック行
    private const float TAG_H    = 84f;    // デザイナー/ジャンルの絞り込みチップ行（絞り込み中のみ）

    /// <summary>詳細モーダル内のリンク（デザイナー・ジャンル）の文字色。</summary>
    private const string LINK_HEX = "#1d5c8a";

    // 詳細モーダルの縦サイズ。情報量（デザイナー・ジャンルの折り返し行数）に応じて
    // BASE〜MAX の間で自動調整する。MAX は Canvas 基準解像度 1080x1920 に収まる上限
    private const float DETAIL_PANEL_BASE_H = 1276f;
    private const float DETAIL_PANEL_MAX_H  = 1560f;
    private const float DETAIL_STACK_TOP    = 232f;   // パネル上端〜情報スタック（和名・英名・区切り線）
    private const float DETAIL_STACK_BOTTOM = 440f;   // 情報スタック下端〜パネル下端（写真行・ボタン類）

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

    // 人数・時間フィルタ（0=指定なし。人数は1〜7がその人数、8が「8人以上」。
    // 時間は正の15/30/45/60/90が「〜分以内」、999が「60分超」、負の-90が「90分以上」）
    private int _playersFilter = 0;
    private int _timeFilter = 0;
    // 「遊んだのみ」「お気に入りのみ」チェック
    private bool _playedOnly = false;
    private bool _favOnly = false;
    // デザイナー/ジャンルのリンクタップによる絞り込み（null=なし。同時に1つだけ）
    private string _tagFilter = null;
    private bool _tagIsDesigner = false;
    private GameObject _tagBar;
    private RectTransform _scrollRootRt;
    private TMP_InputField _searchInput;
    // 並び順（0=五十音順（データの標準順）、1=プレイ時間が短い順、2=人気順=当店お気に入り数）
    private int _sortMode = 0;
    private TextMeshProUGUI _playersBtnLabel, _timeBtnLabel, _sortBtnLabel;
    private Image _playersBtnBg, _timeBtnBg, _sortBtnBg;
    private TextMeshProUGUI _playedOnlyLabel, _favOnlyLabel;
    private Image _playedOnlyBg, _favOnlyBg;
    private GameObject _filterPopup;
    /// <summary>五十音順（読み込み時の並び）に戻すための元インデックス。</summary>
    private readonly Dictionary<BoardGameEntry, int> _orderIndex =
        new Dictionary<BoardGameEntry, int>();

    /// <summary>players/time 文字列のパース結果キャッシュ（0=不明）。</summary>
    private struct ParsedRange { public int pMin, pMax, tMax; }
    private readonly Dictionary<BoardGameEntry, ParsedRange> _parsed =
        new Dictionary<BoardGameEntry, ParsedRange>();

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
        public Image checkBg;          // 「遊んだ」チェックボックスの背景
        public TextMeshProUGUI check;  // チェックマーク（✓）
        public Image favBg;            // 「お気に入り」ボタンの背景
        public TextMeshProUGUI fav;    // 星マーク（★）
        public GameObject thumbGO;     // 「遊んだ記録」写真サムネイル（1枚目。無い行は非表示）
        public RawImage thumbRaw;
        public RectTransform thumbImgRt;
        public BoardGameEntry entry;
        public int index = -1;
    }

    // 詳細モーダルの「遊んだ」「お気に入り」トグル（開いている間だけ保持）
    private BoardGameEntry _detailEntry;
    private Image _detailPlayedBg;
    private TextMeshProUGUI _detailPlayedLabel;
    private Image _detailFavBg;
    private TextMeshProUGUI _detailFavLabel;
    // 「遊んだ記録」写真行（開いている間だけ保持。テクスチャは閉じる時に破棄する）
    private RectTransform _detailPhotoContent;
    private readonly List<Texture2D> _detailThumbTextures = new List<Texture2D>();

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
        for (int i = 0; i < _all.Count; i++) _orderIndex[_all[i]] = i;

        BuildUI();
        StartCoroutine(PrewarmRowsThenShow());
    }

    /// <summary>
    /// UIをフレーム分散で構築し、行プールを事前生成してからリストを表示する。
    ///
    /// 1フレーム内に大量のUI（特にTMPテキスト。行UIなら8行×4テキスト以上）を生成すると
    /// 一部の実機でGPU/ネイティブメモリが暴走しOOMでアプリごと落ちるため、
    /// 「行の生成は1フレームに3行まで」を厳守する。プール充填後の表示・スクロール・
    /// 絞り込みは既存行の使い回しだけで動くので、以降に一括生成は発生しない。
    /// （2026-07-07 実機クラッシュ調査の結論。3行/フレームまでは安全を実機確認済み）
    /// </summary>
    private System.Collections.IEnumerator PrewarmRowsThenShow()
    {
        yield return null;
        BuildSearch();
        yield return null;
        BuildFilters();
        BuildList(); // スクロール枠のみ（テキスト生成なし）
        yield return null;

        // 可視行数 + 上下バッファ + 予備。レイアウト未確定時は既定値で多めに確保
        float viewH = _viewport != null && _viewport.rect.height > 1f ? _viewport.rect.height : 1920f;
        int need = Mathf.CeilToInt(viewH / ROW_STEP) + BUFFER * 2 + 2;

        while (_free.Count < need)
        {
            for (int i = 0; i < 3 && _free.Count < need; i++)
                Recycle(CreateRow());
            yield return null;
        }

        // 全ゲームの文字をフォントアトラスへ少しずつ事前登録する（本クラッシュの核心対策）。
        // 未登録グリフを1フレームで大量（150文字超）に動的アトラスへ追加すると実機で
        // ネイティブメモリが暴走して落ちるため、40文字/フレームに分割する。
        // 登録できなかった文字（フォントに無い文字）は表示時に除去する（欠落グリフは
        // TMPの再レイアウト無限ループ→OOMを引き起こすため。絵文字クラッシュと同根）。
        if (_countLabel != null) _countLabel.text = "読み込み中…";
        if (_jp != null)
        {
            _missingGlyphs.Clear(); // 前回セッションの一時的な失敗を持ち越さない
            var seen = new HashSet<char>();
            var sb = new System.Text.StringBuilder();
            void Collect(string s)
            {
                if (s == null) return;
                foreach (var c in s) if (seen.Add(c)) sb.Append(c);
            }
            foreach (var g in _all)
            {
                Collect(g.title); Collect(g.title_en); Collect(g.players);
                Collect(g.time); Collect(g.year);
                if (g.genre != null) foreach (var t in g.genre) Collect(t);
                if (g.designer != null) foreach (var d in g.designer) Collect(d);
            }
            Collect("_"); // リンクの下線グリフ（TMPは'_'のSDFデータで<u>の下線を描く）

            string allChars = sb.ToString();
            for (int i = 0; i < allChars.Length; i += 40)
            {
                string chunk = allChars.Substring(i, Mathf.Min(40, allChars.Length - i));
                if (!_jp.TryAddCharacters(chunk, out string missing) && !string.IsNullOrEmpty(missing))
                {
                    // 本当にフォントに無い文字は全体で数個のはず。チャンクの大半が「失敗」と
                    // 返るのはアトラス満杯やAPIの一時要因なので、その場合は除去対象にしない
                    // （SafeTextで全テキストが消えるのを防ぐ。表示は□になるだけ）
                    if (missing.Length <= 8)
                        foreach (var c in missing) _missingGlyphs.Add(c);
                    else
                        Debug.LogWarning($"[BoardGame] グリフ追加失敗 {missing.Length}文字（除去せず続行）: '{missing}'");
                }
                yield return null;
            }
            if (_missingGlyphs.Count > 0)
                Debug.Log($"[BoardGame] フォントに無い文字を表示から除去: {_missingGlyphs.Count}文字");
        }

        RebuildList();
    }

    /// <summary>フォントに無い文字の集合（表示時に除去する）。</summary>
    private static readonly HashSet<char> _missingGlyphs = new HashSet<char>();

    /// <summary>フォントに無い文字を除去する（欠落グリフによるTMP無限再レイアウト対策）。</summary>
    private static string SafeText(string s)
    {
        if (string.IsNullOrEmpty(s) || _missingGlyphs.Count == 0) return s;
        System.Text.StringBuilder sb = null;
        for (int i = 0; i < s.Length; i++)
        {
            if (_missingGlyphs.Contains(s[i]))
            {
                if (sb == null) sb = new System.Text.StringBuilder(s, 0, i, s.Length);
            }
            else sb?.Append(s[i]);
        }
        return sb != null ? sb.ToString() : s;
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

        // 検索・フィルタ・リストは PrewarmRowsThenShow がフレームを分けて構築する
        // （1フレームに大量のUI生成を集中させない。PrewarmRowsThenShow のコメント参照）
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
        _searchInput = input;

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

    /// <summary>人数・時間フィルタと並び順のボタン行（検索バーの下）。タップで選択肢ポップアップを開く。</summary>
    private void BuildFilters()
    {
        var barGO = new GameObject("__FilterBar");
        barGO.transform.SetParent(_overlay.transform, false);
        var brt = barGO.AddComponent<RectTransform>();
        brt.anchorMin = new Vector2(0f, 1f); brt.anchorMax = new Vector2(1f, 1f);
        brt.pivot = new Vector2(0.5f, 1f);
        brt.offsetMin = new Vector2(28f, -(TITLE_H + SEARCH_H + 12f + FILTER_H));
        brt.offsetMax = new Vector2(-28f, -(TITLE_H + SEARCH_H + 12f));

        GameObject MakeBar(string name, float aMin, float aMax, UnityEngine.Events.UnityAction onClick)
        {
            var btn = MakeRoundButton(name, barGO.transform, C_SEARCHBG, "", 30, C_INK, 0);
            var rt = btn.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(aMin, 0f); rt.anchorMax = new Vector2(aMax, 1f);
            rt.offsetMin = new Vector2(aMin > 0f ? 6f : 0f, 6f);
            rt.offsetMax = new Vector2(aMax < 1f ? -6f : 0f, 0f);
            var label = btn.GetComponentInChildren<TextMeshProUGUI>();
            label.enableAutoSizing = true; label.fontSizeMax = 30; label.fontSizeMin = 18;
            btn.GetComponent<Button>().onClick.AddListener(onClick);
            return btn;
        }

        var pBtn = MakeBar("__PlayersFilter", 0f, 1f / 3f, () => ShowFilterPopup(0));
        _playersBtnBg = pBtn.GetComponent<Image>();
        _playersBtnLabel = pBtn.GetComponentInChildren<TextMeshProUGUI>();

        var tBtn = MakeBar("__TimeFilter", 1f / 3f, 2f / 3f, () => ShowFilterPopup(1));
        _timeBtnBg = tBtn.GetComponent<Image>();
        _timeBtnLabel = tBtn.GetComponentInChildren<TextMeshProUGUI>();

        var sBtn = MakeBar("__SortButton", 2f / 3f, 1f, () => ShowFilterPopup(2));
        _sortBtnBg = sBtn.GetComponent<Image>();
        _sortBtnLabel = sBtn.GetComponentInChildren<TextMeshProUGUI>();

        // チェック行（フィルタボタンの下）: 「遊んだのみ」「お気に入りのみ」のトグル
        var chkGO = new GameObject("__CheckBar");
        chkGO.transform.SetParent(_overlay.transform, false);
        var chrt = chkGO.AddComponent<RectTransform>();
        chrt.anchorMin = new Vector2(0f, 1f); chrt.anchorMax = new Vector2(1f, 1f);
        chrt.pivot = new Vector2(0.5f, 1f);
        chrt.offsetMin = new Vector2(28f, -(TITLE_H + SEARCH_H + 12f + FILTER_H + 8f + CHECKS_H));
        chrt.offsetMax = new Vector2(-28f, -(TITLE_H + SEARCH_H + 12f + FILTER_H + 8f));

        GameObject MakeCheck(string name, float aMin, float aMax, UnityEngine.Events.UnityAction onClick)
        {
            var btn = MakeRoundButton(name, chkGO.transform, C_SEARCHBG, "", 30, C_MUTED, 0);
            var rt = btn.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(aMin, 0f); rt.anchorMax = new Vector2(aMax, 1f);
            rt.offsetMin = new Vector2(aMin > 0f ? 6f : 0f, 6f);
            rt.offsetMax = new Vector2(aMax < 1f ? -6f : 0f, 0f);
            var label = btn.GetComponentInChildren<TextMeshProUGUI>();
            label.enableAutoSizing = true; label.fontSizeMax = 30; label.fontSizeMin = 18;
            btn.GetComponent<Button>().onClick.AddListener(onClick);
            return btn;
        }

        var pChk = MakeCheck("__PlayedOnly", 0f, 0.5f, () =>
        {
            _playedOnly = !_playedOnly;
            UpdateFilterButtons();
            RebuildList();
        });
        _playedOnlyBg = pChk.GetComponent<Image>();
        _playedOnlyLabel = pChk.GetComponentInChildren<TextMeshProUGUI>();

        var fChk = MakeCheck("__FavOnly", 0.5f, 1f, () =>
        {
            _favOnly = !_favOnly;
            UpdateFilterButtons();
            RebuildList();
        });
        _favOnlyBg = fChk.GetComponent<Image>();
        _favOnlyLabel = fChk.GetComponentInChildren<TextMeshProUGUI>();

        UpdateFilterButtons();
    }

    private void UpdateFilterButtons()
    {
        if (_playersBtnLabel == null) return;
        _playersBtnLabel.text = _playersFilter == 0 ? "人数 ▼"
            : _playersFilter >= 8 ? "8人以上 ▼" : $"{_playersFilter}人 ▼";
        _playersBtnBg.color = _playersFilter == 0 ? C_SEARCHBG : C_CHIP;
        _timeBtnLabel.text = _timeFilter == 0 ? "時間 ▼"
            : _timeFilter == 999 ? "60分超 ▼"
            : _timeFilter < 0 ? $"{-_timeFilter}分以上 ▼"
            : $"{_timeFilter}分以内 ▼";
        _timeBtnBg.color = _timeFilter == 0 ? C_SEARCHBG : C_CHIP;
        _sortBtnLabel.text = _sortMode == 0 ? "並び順 ▼"
            : _sortMode == 1 ? "時間が短い順 ▼" : "人気順 ▼";
        _sortBtnBg.color = _sortMode == 0 ? C_SEARCHBG : C_CHIP;

        if (_playedOnlyLabel != null)
        {
            _playedOnlyLabel.text = "✓ 遊んだのみ";
            _playedOnlyLabel.color = _playedOnly ? C_TITLE : C_MUTED;
            _playedOnlyBg.color = _playedOnly ? C_CHIP : C_SEARCHBG;
            _favOnlyLabel.text = "★ お気に入りのみ";
            _favOnlyLabel.color = _favOnly ? C_TITLE : C_MUTED;
            _favOnlyBg.color = _favOnly ? C_CHIP : C_SEARCHBG;
        }
    }

    /// <summary>選択肢ポップアップ（kind: 0=人数, 1=時間, 2=並び順）。</summary>
    private void ShowFilterPopup(int kind)
    {
        CloseFilterPopup();

        string[] labels =
            kind == 0 ? new[] { "指定なし", "1人", "2人", "3人", "4人", "5人", "6人", "7人", "8人以上" }
            : kind == 1 ? new[] { "指定なし", "15分以内", "30分以内", "45分以内", "60分以内", "90分以内", "60分超", "90分以上" }
            : new[] { "五十音順", "プレイ時間が短い順", "人気順" };
        int[] values =
            kind == 0 ? new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8 }
            : kind == 1 ? new[] { 0, 15, 30, 45, 60, 90, 999, -90 }
            : new[] { 0, 1, 2 };
        int current = kind == 0 ? _playersFilter : kind == 1 ? _timeFilter : _sortMode;
        string title = kind == 0 ? "遊ぶ人数で絞り込む" : kind == 1 ? "プレイ時間で絞り込む" : "並び順を選ぶ";

        _filterPopup = new GameObject("__FilterPopup");
        _filterPopup.transform.SetParent(_overlay.transform, false);
        var frt = _filterPopup.AddComponent<RectTransform>();
        frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one;
        frt.offsetMin = frt.offsetMax = Vector2.zero;
        var dim = _filterPopup.AddComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.6f);
        var dimBtn = _filterPopup.AddComponent<Button>();
        dimBtn.transition = Selectable.Transition.None;
        dimBtn.onClick.AddListener(CloseFilterPopup);

        float panelH = 120f + labels.Length * 104f + 24f;
        var border = MakeRect("__FBorder", _filterPopup.transform, C_BORDER, 640, panelH + 24f);
        RoundedRectSprite.Apply(border.GetComponent<Image>());
        var panel = MakeRect("__FPanel", border.transform, C_PARCHMENT, 616, panelH);
        RoundedRectSprite.Apply(panel.GetComponent<Image>());
        panel.AddComponent<Button>().transition = Selectable.Transition.None; // パネル内タップで閉じない

        var titleLbl = MakeLabel(panel.transform, title, 40, FontStyles.Bold, C_TITLE, 560, 100);
        var tirt = titleLbl.GetComponent<RectTransform>();
        tirt.anchorMin = tirt.anchorMax = new Vector2(0.5f, 1f);
        tirt.pivot = new Vector2(0.5f, 1f);
        tirt.anchoredPosition = new Vector2(0, -16);

        for (int i = 0; i < labels.Length; i++)
        {
            int value = values[i];
            bool selected = value == current;
            var opt = MakeRoundButton("__FOpt_" + i, panel.transform,
                selected ? C_CHIP : C_SEARCHBG, labels[i], 34,
                selected ? C_TITLE : C_INK, 0);
            var ort = opt.GetComponent<RectTransform>();
            ort.sizeDelta = new Vector2(520, 92);
            ort.anchorMin = ort.anchorMax = new Vector2(0.5f, 1f);
            ort.pivot = new Vector2(0.5f, 1f);
            ort.anchoredPosition = new Vector2(0, -(120f + i * 104f));
            opt.GetComponent<Button>().onClick.AddListener(() =>
            {
                if (kind == 0) _playersFilter = value;
                else if (kind == 1) _timeFilter = value;
                else _sortMode = value;
                UpdateFilterButtons();
                RebuildList();
                CloseFilterPopup();
            });
        }
    }

    private void CloseFilterPopup()
    {
        if (_filterPopup != null) Destroy(_filterPopup);
        _filterPopup = null;
    }

    /// <summary>人数・時間フィルタの判定。データが不明（—）のゲームはフィルタ指定時は除外する。</summary>
    private bool MatchesFilters(BoardGameEntry g)
    {
        if (_playersFilter > 0)
        {
            var r = GetRange(g);
            if (r.pMax <= 0) return false; // 人数不明
            if (_playersFilter >= 8 ? r.pMax < 8 : (_playersFilter < r.pMin || r.pMax < _playersFilter))
                return false;
        }
        if (_timeFilter != 0)
        {
            var r = GetRange(g);
            if (r.tMax <= 0) return false; // 時間不明
            bool ok = _timeFilter == 999 ? r.tMax > 60      // 60分超
                : _timeFilter < 0 ? r.tMax >= -_timeFilter  // 〜分以上
                : r.tMax <= _timeFilter;                    // 〜分以内
            if (!ok) return false;
        }
        if (_tagFilter != null)
        {
            var tags = _tagIsDesigner ? g.designer : g.genre;
            if (tags == null || System.Array.IndexOf(tags, _tagFilter) < 0) return false;
        }
        if (_playedOnly && !BoardGameMarks.IsPlayed(g)) return false;
        if (_favOnly && !BoardGameMarks.IsFavorite(g)) return false;
        return true;
    }

    /// <summary>一覧スクロール領域の上端オフセット（絞り込みチップが出ている間はそのぶん下げる）。</summary>
    private float ListTopOffset()
        => TITLE_H + SEARCH_H + FILTER_H + CHECKS_H + 36f + (_tagFilter == null ? 0f : TAG_H + 8f);

    /// <summary>時間ソート用キー（最大プレイ時間。不明は最後に回す）。</summary>
    private int SortTimeKey(BoardGameEntry g)
    {
        int t = GetRange(g).tMax;
        return t > 0 ? t : int.MaxValue;
    }

    /// <summary>「2人～4人」「15分～30分」等の表記を数値範囲にパースする（結果はキャッシュ）。</summary>
    private ParsedRange GetRange(BoardGameEntry g)
    {
        if (_parsed.TryGetValue(g, out var r)) return r;

        r = new ParsedRange();
        var pm = Regex.Match(g.players ?? "", @"([0-9]+)人(?:～([0-9]+)人)?");
        if (pm.Success)
        {
            r.pMin = int.Parse(pm.Groups[1].Value);
            r.pMax = pm.Groups[2].Success ? int.Parse(pm.Groups[2].Value) : r.pMin;
        }
        var tm = Regex.Match(g.time ?? "", @"([0-9]+)分(?:～([0-9]+)分)?");
        if (tm.Success)
        {
            int t1 = int.Parse(tm.Groups[1].Value);
            r.tMax = tm.Groups[2].Success ? int.Parse(tm.Groups[2].Value) : t1;
        }
        _parsed[g] = r;
        return r;
    }

    private void BuildList()
    {
        var svGO = new GameObject("__Scroll");
        svGO.transform.SetParent(_overlay.transform, false);
        var svrt = svGO.AddComponent<RectTransform>();
        _scrollRootRt = svrt;
        svrt.anchorMin = new Vector2(0f, 0f); svrt.anchorMax = new Vector2(1f, 1f);
        svrt.offsetMin = new Vector2(24f, 24f);
        svrt.offsetMax = new Vector2(-24f, -ListTopOffset());
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

        // 絞り込み（テキスト検索 AND 人数 AND 時間）
        _items.Clear();
        string q = string.IsNullOrEmpty(_query) ? null : _query.ToLowerInvariant();
        foreach (var g in _all)
        {
            if (q != null && !Matches(g, q)) continue;
            if (!MatchesFilters(g)) continue;
            _items.Add(g);
        }

        // 並び替え（五十音順=データの標準順。同順位は標準順で安定させる）
        if (_sortMode == 1)
        {
            // プレイ時間が短い順（最大時間の昇順。時間不明は最後）
            _items.Sort((a, b) =>
            {
                int ta = SortTimeKey(a), tb = SortTimeKey(b);
                return ta != tb ? ta.CompareTo(tb) : _orderIndex[a].CompareTo(_orderIndex[b]);
            });
        }
        else if (_sortMode == 2)
        {
            // 人気順（当店アプリユーザーの「お気に入り」数の降順）
            _items.Sort((a, b) =>
            {
                int c = b.popularity.CompareTo(a.popularity);
                return c != 0 ? c : _orderIndex[a].CompareTo(_orderIndex[b]);
            });
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
    // デザイナー/ジャンルの絞り込み（詳細モーダルのリンクタップで有効化）
    // ================================================================

    /// <summary>詳細モーダルのデザイナー名/ジャンルタグのタップで一覧を絞り込む。</summary>
    private void ApplyTagFilter(bool isDesigner, string value)
    {
        if (string.IsNullOrEmpty(value)) return;
        _tagIsDesigner = isDesigner;
        _tagFilter = value;
        // テキスト検索は「特定のゲームを探す」操作なので、リンクからのジャンプでは白紙に戻す
        // （検索語とのANDでほぼ0件になるのを防ぐ。人数・時間・✓★の絞り込みは維持する）
        if (_searchInput != null) _searchInput.SetTextWithoutNotify("");
        _query = "";
        CloseDetail();
        UpdateTagFilterBar();
        RebuildList();
    }

    private void ClearTagFilter()
    {
        _tagFilter = null;
        UpdateTagFilterBar();
        RebuildList();
    }

    /// <summary>絞り込みチップ（チェック行と一覧の間）を状態に合わせて作り直し、一覧の上端も詰め直す。</summary>
    private void UpdateTagFilterBar()
    {
        if (_tagBar != null) { Destroy(_tagBar); _tagBar = null; }
        if (_scrollRootRt != null)
            _scrollRootRt.offsetMax = new Vector2(-24f, -ListTopOffset());
        if (_tagFilter == null) return;

        _tagBar = new GameObject("__TagFilterBar");
        _tagBar.transform.SetParent(_overlay.transform, false);
        var rt = _tagBar.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        float top = TITLE_H + SEARCH_H + 12f + FILTER_H + 8f + CHECKS_H + 8f;
        rt.offsetMin = new Vector2(28f, -(top + TAG_H));
        rt.offsetMax = new Vector2(-28f, -top);
        var img = _tagBar.AddComponent<Image>();
        RoundedRectSprite.Apply(img);
        img.color = C_CHIP;
        var btn = _tagBar.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(ClearTagFilter); // チップのどこを押しても解除

        var label = MakeChildLabel(_tagBar.transform, 30, FontStyles.Bold, C_TITLE,
            Vector2.zero, Vector2.one, new Vector2(28f, 0f), new Vector2(-92f, 0f),
            TextAlignmentOptions.MidlineLeft);
        label.text = SafeText($"{(_tagIsDesigner ? "デザイナー" : "ジャンル")}: {_tagFilter}");
        label.enableAutoSizing = true; label.fontSizeMax = 30; label.fontSizeMin = 20;
        label.overflowMode = TextOverflowModes.Ellipsis;
        label.enableWordWrapping = false;

        var x = MakeChildLabel(_tagBar.transform, 36, FontStyles.Bold, C_TITLE,
            new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-88f, 0f), new Vector2(-24f, 0f),
            TextAlignmentOptions.Center);
        x.text = "✕";
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
        row.title.text = SafeText(string.IsNullOrEmpty(g.title) ? "(名称不明)" : g.title);
        row.meta.text = SafeText(BuildMeta(g));
        row.genre.text = SafeText((g.genre != null && g.genre.Length > 0)
            ? "ジャンル: " + string.Join("・", g.genre)
            : "ジャンル: —");
        UpdateMarkVisuals(row);
        UpdateRowThumb(row);

        row.go.SetActive(true);
        return row;
    }

    // ================================================================
    // 「遊んだ」✓・「お気に入り」★（users/{uid} に保存。BoardGameMarks 経由）
    // ================================================================
    private static readonly Color C_MARK_OFF = new Color(0.62f, 0.55f, 0.42f, 0.30f);

    private void TogglePlayed(BoardGameEntry g)
    {
        BoardGameMarks.SetPlayed(g, !BoardGameMarks.IsPlayed(g));
        RefreshMarkVisuals(g);
        if (_playedOnly) RebuildList(); // 「遊んだのみ」表示中は外したゲームを即座に消す
    }

    private void ToggleFavorite(BoardGameEntry g)
    {
        BoardGameMarks.SetFavorite(g, !BoardGameMarks.IsFavorite(g));
        RefreshMarkVisuals(g);
        if (_favOnly) RebuildList();
    }

    /// <summary>表示中の行と詳細モーダルのマーク表示を更新する。</summary>
    private void RefreshMarkVisuals(BoardGameEntry g)
    {
        foreach (var kv in _active)
            if (kv.Value.entry == g) UpdateMarkVisuals(kv.Value);
        UpdateDetailMarkVisuals();
    }

    private void UpdateMarkVisuals(RowView row)
    {
        if (row.checkBg == null || row.entry == null) return;
        bool played = BoardGameMarks.IsPlayed(row.entry);
        row.checkBg.color = played ? C_BORDER : C_SEARCHBG;
        row.check.color = played ? C_TITLE : C_MARK_OFF;

        bool fav = BoardGameMarks.IsFavorite(row.entry);
        row.favBg.color = fav ? C_BORDER : C_SEARCHBG;
        row.fav.color = fav ? C_TITLE : C_MARK_OFF;
    }

    private void UpdateDetailMarkVisuals()
    {
        if (_detailEntry == null) return;
        if (_detailPlayedBg != null)
        {
            bool played = BoardGameMarks.IsPlayed(_detailEntry);
            _detailPlayedBg.color = played ? C_BORDER : C_SEARCHBG;
            _detailPlayedLabel.text = played ? "✓ 遊んだ！" : "まだ遊んでない";
            _detailPlayedLabel.color = played ? C_TITLE : C_MUTED;
        }
        if (_detailFavBg != null)
        {
            bool fav = BoardGameMarks.IsFavorite(_detailEntry);
            _detailFavBg.color = fav ? C_BORDER : C_SEARCHBG;
            _detailFavLabel.text = fav ? "★ お気に入り" : "☆ お気に入り";
            _detailFavLabel.color = fav ? C_TITLE : C_MUTED;
        }
    }

    // 注意: 絵文字（👥⏱等）は使わないこと。実機のjpフォントにグリフが無く、
    // TMPの欠落グリフ置換とEllipsisの組み合わせで毎フレーム再レイアウトが走り続け、
    // メモリを食い尽くしてOOMでアプリが落ちる（2026-07-07 実機クラッシュの原因）。
    private static string BuildMeta(BoardGameEntry g)
    {
        var parts = new List<string>();
        parts.Add(string.IsNullOrEmpty(g.players) ? "人数 —" : g.players);
        parts.Add(string.IsNullOrEmpty(g.time) ? "時間 —" : g.time);
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
        // 右端はチェックボックス（幅96+余白）を避けて -150 まで
        row.title = MakeChildLabel(go.transform, 30, FontStyles.Bold, C_TITLE,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(28f, -74f), new Vector2(-150f, -14f),
            TextAlignmentOptions.Left);
        row.title.enableAutoSizing = true; row.title.fontSizeMax = 40; row.title.fontSizeMin = 24;
        row.title.overflowMode = TextOverflowModes.Ellipsis;

        // 人数・時間・発売年（中段）: 上端-78〜下端-126
        row.meta = MakeChildLabel(go.transform, 30, FontStyles.Bold, C_INK,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(28f, -126f), new Vector2(-150f, -78f),
            TextAlignmentOptions.Left);
        row.meta.overflowMode = TextOverflowModes.Ellipsis;

        // ジャンル（下段・1行省略）
        row.genre = MakeChildLabel(go.transform, 26, FontStyles.Normal, C_MUTED,
            new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(28f, 14f), new Vector2(-150f, 62f),
            TextAlignmentOptions.Left);
        row.genre.overflowMode = TextOverflowModes.Ellipsis;
        row.genre.enableWordWrapping = false;

        // 「遊んだ」✓と「お気に入り」★のボタン（右端・縦2段）。子のButtonが先にタップを
        // 受けるため、ここを押しても行タップ（詳細表示）にはならない
        (Image bg, TextMeshProUGUI label) MakeMarkButton(string name, string mark, float y, UnityEngine.Events.UnityAction onClick)
        {
            var btnGO = new GameObject(name);
            btnGO.transform.SetParent(go.transform, false);
            var rt = btnGO.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.sizeDelta = new Vector2(84f, 84f);
            rt.anchoredPosition = new Vector2(-28f, y);
            var bg = btnGO.AddComponent<Image>();
            RoundedRectSprite.Apply(bg);
            var btn = btnGO.AddComponent<Button>();
            btn.targetGraphic = bg;
            btn.onClick.AddListener(onClick);
            var label = MakeChildLabel(btnGO.transform, 52, FontStyles.Bold, C_TITLE,
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, TextAlignmentOptions.Center);
            label.text = mark;
            return (bg, label);
        }

        (row.checkBg, row.check) = MakeMarkButton("__Check", "✓", 47f,
            () => { if (row.entry != null) TogglePlayed(row.entry); });
        (row.favBg, row.fav) = MakeMarkButton("__Fav", "★", -47f,
            () => { if (row.entry != null) ToggleFavorite(row.entry); });

        // 「遊んだ記録」写真のサムネイル（✓★ボタンのすぐ左。写真が無い行は非表示）
        var thGO = new GameObject("__Thumb");
        thGO.transform.SetParent(go.transform, false);
        var thrt = thGO.AddComponent<RectTransform>();
        thrt.anchorMin = thrt.anchorMax = new Vector2(1f, 0.5f);
        thrt.pivot = new Vector2(1f, 0.5f);
        thrt.sizeDelta = new Vector2(140f, 140f);
        thrt.anchoredPosition = new Vector2(-124f, 0f);
        var thbg = thGO.AddComponent<Image>();
        RoundedRectSprite.Apply(thbg);
        thbg.color = C_CHIP;
        thbg.raycastTarget = false;
        thGO.AddComponent<RectMask2D>(); // 正方形からはみ出す分を切る（カバー表示）
        var thImgGO = new GameObject("__Img");
        thImgGO.transform.SetParent(thGO.transform, false);
        row.thumbImgRt = thImgGO.AddComponent<RectTransform>();
        row.thumbImgRt.anchorMin = row.thumbImgRt.anchorMax = new Vector2(0.5f, 0.5f);
        row.thumbRaw = thImgGO.AddComponent<RawImage>();
        row.thumbRaw.raycastTarget = false;
        row.thumbGO = thGO;
        thGO.SetActive(false);

        return row;
    }

    // ================================================================
    // 詳細モーダル
    // ================================================================
    private void ShowDetail(BoardGameEntry g)
    {
        CloseDetail();
        _detailEntry = g;

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

        // パネル（下部に「遊んだ記録」写真行があるぶん縦長。高さは情報スタック構築後に量に応じて伸ばす）
        var border = MakeRect("__DBorder", _detailModal.transform, C_BORDER, 900, DETAIL_PANEL_BASE_H + 24f);
        RoundedRectSprite.Apply(border.GetComponent<Image>());
        var panel = MakeRect("__DPanel", border.transform, C_PARCHMENT, 876, DETAIL_PANEL_BASE_H);
        RoundedRectSprite.Apply(panel.GetComponent<Image>());
        panel.AddComponent<Button>().transition = Selectable.Transition.None; // パネル内タップで閉じない

        // 和名
        var name = MakeLabel(panel.transform, SafeText(string.IsNullOrEmpty(g.title) ? "(名称不明)" : g.title),
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
            var en = MakeLabel(panel.transform, SafeText(g.title_en), 30, FontStyles.Italic, C_MUTED, 820, 44);
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
        strt.offsetMin = new Vector2(48f, DETAIL_STACK_BOTTOM);   // 下端: 「遊んだ記録」写真行の上
        strt.offsetMax = new Vector2(-48f, -DETAIL_STACK_TOP);    // 上端: 区切り線の下
        var vlg = stack.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 20f;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
        vlg.childControlWidth = true; vlg.childControlHeight = true;
        vlg.childAlignment = TextAnchor.UpperLeft;

        AddInfoRow(stack.transform, "人数", SafeText(string.IsNullOrEmpty(g.players) ? "—" : g.players));
        AddInfoRow(stack.transform, "プレイ時間", SafeText(string.IsNullOrEmpty(g.time) ? "—" : g.time));
        if (!string.IsNullOrEmpty(g.year))
            AddInfoRow(stack.transform, "発売年", SafeText(g.year));
        if (g.designer != null && g.designer.Length > 0)
            AddInfoRow(stack.transform, "デザイナー", LinkedValues(g.designer, "、"),
                PlainLength(g.designer, "、"), i => ApplyTagFilter(true, g.designer[i]));
        if (g.popularity > 0)
            AddInfoRow(stack.transform, "人気度", $"★ {g.popularity}人がお気に入り");

        if (g.genre != null && g.genre.Length > 0)
            AddInfoBlock(stack.transform, "ジャンル", LinkedValues(g.genre, "　/　"),
                i => ApplyTagFilter(false, g.genre[i]));
        else
            AddInfoBlock(stack.transform, "ジャンル", "—", null);

        // 情報量に応じてパネルを縦長に伸ばす（スタックの必要高さ＋上下の固定領域）。
        // MAX でも収まらない極端な場合は各行が minHeight まで縮み、あふれは省略記号で切れる
        LayoutRebuilder.ForceRebuildLayoutImmediate(strt);
        float needed = 0f;
        for (int i = 0; i < strt.childCount; i++)
        {
            var child = (RectTransform)strt.GetChild(i);
            needed += Mathf.Max(LayoutUtility.GetMinHeight(child), LayoutUtility.GetPreferredHeight(child));
        }
        needed += vlg.spacing * (strt.childCount - 1);
        float panelH = Mathf.Clamp(DETAIL_STACK_TOP + DETAIL_STACK_BOTTOM + needed,
            DETAIL_PANEL_BASE_H, DETAIL_PANEL_MAX_H);
        border.GetComponent<RectTransform>().sizeDelta = new Vector2(900f, panelH + 24f);
        panel.GetComponent<RectTransform>().sizeDelta = new Vector2(876f, panelH);

        // 「遊んだ」「お気に入り」トグルボタン（閉じるボタンの上に横並び）
        GameObject MakeDetailToggle(string name, float x, UnityEngine.Events.UnityAction onClick)
        {
            var btn = MakeRoundButton(name, panel.transform, C_SEARCHBG, "", 30, C_MUTED, 0);
            var rt = btn.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(396, 100);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(x, 140);
            var label = btn.GetComponentInChildren<TextMeshProUGUI>();
            label.enableAutoSizing = true; label.fontSizeMax = 30; label.fontSizeMin = 20;
            btn.GetComponent<Button>().onClick.AddListener(onClick);
            return btn;
        }

        var playedBtn = MakeDetailToggle("__DPlayed", -204, () => TogglePlayed(g));
        _detailPlayedBg = playedBtn.GetComponent<Image>();
        _detailPlayedLabel = playedBtn.GetComponentInChildren<TextMeshProUGUI>();

        var favBtn = MakeDetailToggle("__DFav", 204, () => ToggleFavorite(g));
        _detailFavBg = favBtn.GetComponent<Image>();
        _detailFavLabel = favBtn.GetComponentInChildren<TextMeshProUGUI>();

        UpdateDetailMarkVisuals();

        // 「遊んだ記録」写真行（カメラボタン + 撮影済みサムネイル。トグルボタンの上）
        BuildDetailPhotoRow(panel.transform);

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

    // ================================================================
    // 行のサムネイル（「遊んだ記録」写真の1枚目を✓★ボタンの左に表示）
    // ================================================================

    private const int THUMB_CACHE_MAX = 64; // 256pxサムネイル≒0.2MB/枚 → 上限でも十数MB

    /// <summary>ゲームごとの1枚目の写真パス（null=写真なし）。ディレクトリ走査を毎バインドしないためのキャッシュ。</summary>
    private readonly Dictionary<BoardGameEntry, string> _firstPhoto =
        new Dictionary<BoardGameEntry, string>();
    /// <summary>読み込み済みサムネイルテクスチャ（パス→テクスチャ。LRUで上限管理）。</summary>
    private readonly Dictionary<string, Texture2D> _rowThumbCache =
        new Dictionary<string, Texture2D>();
    /// <summary>使用順（先頭が最も古い。EvictThumbsIfNeeded が先頭から捨てる）。</summary>
    private readonly List<string> _rowThumbOrder = new List<string>();

    /// <summary>行のサムネイル表示を更新し、写真の有無でテキストの右端も切り替える。</summary>
    private void UpdateRowThumb(RowView row)
    {
        if (row.thumbGO == null || row.entry == null) return;

        Texture2D tex = null;
        string path = FirstPhotoPathOf(row.entry);
        if (path != null) tex = GetCachedThumb(path);

        bool has = tex != null;
        row.thumbGO.SetActive(has);
        if (has)
        {
            row.thumbRaw.texture = tex;
            float aspect = (float)tex.width / tex.height;
            row.thumbImgRt.sizeDelta = aspect >= 1f
                ? new Vector2(140f * aspect, 140f)
                : new Vector2(140f, 140f / aspect);
        }

        // 写真がある行はテキストをサムネイルの左まで、無い行は✓★ボタンの左まで使う
        float right = has ? -280f : -150f;
        SetLabelRight(row.title, right);
        SetLabelRight(row.meta, right);
        SetLabelRight(row.genre, right);
    }

    private static void SetLabelRight(TextMeshProUGUI label, float x)
    {
        var rt = label.rectTransform;
        rt.offsetMax = new Vector2(x, rt.offsetMax.y);
    }

    private string FirstPhotoPathOf(BoardGameEntry g)
    {
        if (!_firstPhoto.TryGetValue(g, out var path))
        {
            var photos = BoardGamePhotoStore.PhotosOf(g);
            path = photos.Count > 0 ? photos[0] : null;
            _firstPhoto[g] = path;
        }
        return path;
    }

    private Texture2D GetCachedThumb(string path)
    {
        if (_rowThumbCache.TryGetValue(path, out var tex))
        {
            _rowThumbOrder.Remove(path);
            _rowThumbOrder.Add(path);
            return tex;
        }
        tex = BoardGamePhotoStore.LoadThumbTexture(path);
        if (tex == null) return null;
        _rowThumbCache[path] = tex;
        _rowThumbOrder.Add(path);
        EvictThumbsIfNeeded();
        return tex;
    }

    /// <summary>上限を超えたら古い順に破棄する（表示中の行が使っているものは残す）。</summary>
    private void EvictThumbsIfNeeded()
    {
        int i = 0;
        while (_rowThumbCache.Count > THUMB_CACHE_MAX && i < _rowThumbOrder.Count)
        {
            string cand = _rowThumbOrder[i];
            var tex = _rowThumbCache[cand];
            bool inUse = false;
            foreach (var kv in _active)
                if (kv.Value.thumbRaw != null && kv.Value.thumbRaw.texture == tex) { inUse = true; break; }
            if (inUse) { i++; continue; }
            Destroy(tex);
            _rowThumbCache.Remove(cand);
            _rowThumbOrder.RemoveAt(i);
        }
    }

    /// <summary>写真の追加・削除後に、このゲームの行サムネイルを取り直して表示し直す。</summary>
    private void InvalidateRowThumb(BoardGameEntry g)
    {
        if (_firstPhoto.TryGetValue(g, out var old) && old != null
            && _rowThumbCache.TryGetValue(old, out var tex))
        {
            _rowThumbCache.Remove(old);
            _rowThumbOrder.Remove(old);
            Destroy(tex);
        }
        _firstPhoto.Remove(g);
        foreach (var kv in _active)
            if (kv.Value.entry == g) UpdateRowThumb(kv.Value);
    }

    private void OnDestroy()
    {
        foreach (var t in _rowThumbCache.Values) Destroy(t);
        _rowThumbCache.Clear();
        _rowThumbOrder.Clear();
        foreach (var t in _detailThumbTextures) Destroy(t);
        _detailThumbTextures.Clear();
    }

    // ================================================================
    // 「遊んだ記録」写真（端末ローカル保存のみ。BoardGamePhotoStore 参照）
    // ================================================================

    /// <summary>写真行の枠（横スクロール）を作る。中身は RefreshDetailPhotos が入れる。</summary>
    private void BuildDetailPhotoRow(Transform panel)
    {
        var rowGO = new GameObject("__PhotoRow");
        rowGO.transform.SetParent(panel, false);
        var rrt = rowGO.AddComponent<RectTransform>();
        rrt.anchorMin = new Vector2(0f, 0f); rrt.anchorMax = new Vector2(1f, 0f);
        rrt.offsetMin = new Vector2(48f, 256f); rrt.offsetMax = new Vector2(-48f, 416f);
        rowGO.AddComponent<RectMask2D>();
        var sr = rowGO.AddComponent<ScrollRect>();
        sr.horizontal = true; sr.vertical = false; sr.scrollSensitivity = 30f;
        sr.movementType = ScrollRect.MovementType.Clamped;
        sr.viewport = rrt;

        var ctGO = new GameObject("__PhotoContent");
        ctGO.transform.SetParent(rowGO.transform, false);
        var ct = ctGO.AddComponent<RectTransform>();
        ct.anchorMin = new Vector2(0f, 0f); ct.anchorMax = new Vector2(0f, 1f);
        ct.pivot = new Vector2(0f, 0.5f);
        ct.offsetMin = ct.offsetMax = Vector2.zero;
        var hl = ctGO.AddComponent<HorizontalLayoutGroup>();
        hl.spacing = 12f;
        hl.childAlignment = TextAnchor.MiddleLeft;
        hl.childForceExpandWidth = false; hl.childForceExpandHeight = false;
        hl.childControlWidth = true; hl.childControlHeight = true;
        var fit = ctGO.AddComponent<ContentSizeFitter>();
        fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        sr.content = ct;

        _detailPhotoContent = ct;
        RefreshDetailPhotos();
    }

    /// <summary>写真行の中身（カメラボタン + サムネイル一覧）を作り直す。</summary>
    private void RefreshDetailPhotos()
    {
        if (_detailPhotoContent == null || _detailEntry == null) return;

        foreach (Transform c in _detailPhotoContent) Destroy(c.gameObject);
        foreach (var t in _detailThumbTextures) Destroy(t);
        _detailThumbTextures.Clear();

        var g = _detailEntry;

        // カメラボタン（アイコンは絵文字が使えないためImageで描く。emoji-tmp-oom-crash 対策）
        var camGO = new GameObject("__Camera");
        camGO.transform.SetParent(_detailPhotoContent, false);
        camGO.AddComponent<RectTransform>();
        var camLe = camGO.AddComponent<LayoutElement>();
        camLe.preferredWidth = 150f; camLe.preferredHeight = 150f;
        var camImg = camGO.AddComponent<Image>();
        RoundedRectSprite.Apply(camImg);
        camImg.color = C_SEARCHBG;
        var camBtn = camGO.AddComponent<Button>();
        camBtn.targetGraphic = camImg;
        camBtn.onClick.AddListener(() => TakePhoto(g));

        var camBody = MakeRect("__CamBody", camGO.transform, C_TITLE, 84, 56);
        RoundedRectSprite.Apply(camBody.GetComponent<Image>());
        camBody.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, 18);
        var camBump = MakeRect("__CamBump", camGO.transform, C_TITLE, 26, 14);
        camBump.GetComponent<RectTransform>().anchoredPosition = new Vector2(-20, 50);
        var camLens = MakeRect("__CamLens", camGO.transform, C_SEARCHBG, 34, 34);
        UICircleSprite.Apply(camLens.GetComponent<Image>());
        camLens.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, 18);
        var camLensIn = MakeRect("__CamLensIn", camGO.transform, C_TITLE, 16, 16);
        UICircleSprite.Apply(camLensIn.GetComponent<Image>());
        camLensIn.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, 18);
        var camLabel = MakeChildLabel(camGO.transform, 24, FontStyles.Bold, C_MUTED,
            new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 8f), new Vector2(0f, 40f),
            TextAlignmentOptions.Center);
        camLabel.text = "撮影";

        // 撮影済みサムネイル（タップで全画面ビューア）
        foreach (var photoPath in BoardGamePhotoStore.PhotosOf(g))
        {
            string path = photoPath;
            var cell = new GameObject("__Photo");
            cell.transform.SetParent(_detailPhotoContent, false);
            cell.AddComponent<RectTransform>();
            var le = cell.AddComponent<LayoutElement>();
            le.preferredWidth = 150f; le.preferredHeight = 150f;
            var bg = cell.AddComponent<Image>();
            RoundedRectSprite.Apply(bg);
            bg.color = C_CHIP;
            cell.AddComponent<RectMask2D>(); // 正方形からはみ出す分を切る（カバー表示）
            var btn = cell.AddComponent<Button>();
            btn.targetGraphic = bg;
            btn.onClick.AddListener(() =>
                BoardGamePhotoViewer.Show(_overlay.transform, path, _jp, () =>
                {
                    InvalidateRowThumb(g);
                    RefreshDetailPhotos();
                }));

            var tex = BoardGamePhotoStore.LoadThumbTexture(path);
            if (tex == null) continue;
            _detailThumbTextures.Add(tex);

            var imgGO = new GameObject("__Img");
            imgGO.transform.SetParent(cell.transform, false);
            var irt = imgGO.AddComponent<RectTransform>();
            irt.anchorMin = irt.anchorMax = new Vector2(0.5f, 0.5f);
            float aspect = (float)tex.width / tex.height;
            irt.sizeDelta = aspect >= 1f
                ? new Vector2(150f * aspect, 150f)
                : new Vector2(150f, 150f / aspect);
            var raw = imgGO.AddComponent<RawImage>();
            raw.texture = tex;
            raw.raycastTarget = false;

            // 撮影日（月/日）の帯を写真の上部に重ねる
            var taken = BoardGamePhotoStore.TakenAtOf(path);
            if (taken != System.DateTime.MinValue)
            {
                var bandGO = new GameObject("__Date");
                bandGO.transform.SetParent(cell.transform, false);
                var brt = bandGO.AddComponent<RectTransform>();
                brt.anchorMin = new Vector2(0f, 1f); brt.anchorMax = new Vector2(1f, 1f);
                brt.pivot = new Vector2(0.5f, 1f);
                brt.sizeDelta = new Vector2(0f, 36f);
                var bimg = bandGO.AddComponent<Image>();
                bimg.color = new Color(0f, 0f, 0f, 0.45f);
                bimg.raycastTarget = false;
                var dlbl = MakeChildLabel(bandGO.transform, 22, FontStyles.Bold, Color.white,
                    Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, TextAlignmentOptions.Center);
                dlbl.text = $"{taken.Month}月{taken.Day}日";
            }
        }
    }

    /// <summary>
    /// カメラを起動して撮影し、写真をこのゲームの「遊んだ記録」として端末に保存する。
    /// 写真はローカルのみ（Firestoreへはアップロードしない。BoardGamePhotoStore 参照）。
    /// </summary>
    private void TakePhoto(BoardGameEntry g)
    {
#if UNITY_EDITOR
        // エディタではネイティブカメラが使えないため、ファイル選択で代用する
        string picked = UnityEditor.EditorUtility.OpenFilePanel("テスト用の写真を選択", "", "jpg,jpeg,png");
        if (string.IsNullOrEmpty(picked)) return;
        var editorTex = BoardGamePhotoStore.LoadTexture(picked);
        if (editorTex == null) return;
        BoardGamePhotoStore.AddPhoto(g, editorTex);
        Destroy(editorTex);
        InvalidateRowThumb(g);
        RefreshDetailPhotos();
#else
        NativeCamera.TakePicture(path =>
        {
            if (string.IsNullOrEmpty(path)) return; // 撮影キャンセル
            var tex = NativeCamera.LoadImageAtPath(path, 1280, markTextureNonReadable: false);
            if (tex == null)
            {
                Debug.LogError($"[BoardGame] 撮影画像の読み込みに失敗: {path}");
                return;
            }
            BoardGamePhotoStore.AddPhoto(g, tex);
            Destroy(tex);
            InvalidateRowThumb(g);
            RefreshDetailPhotos();
        }, maxSize: 1280);
#endif
    }

    /// <summary>
    /// 「見出し：値」の1行。値の文字量に応じて自動調整する：
    /// 長い値は段階的に文字を縮め、それでも収まらない分は折り返して行の高さを広げる
    /// （高さは preferredHeight 任せなので、隣の行に重ならない）。
    /// </summary>
    private void AddInfoRow(Transform parent, string label, string value)
        => AddInfoRow(parent, label, value, value.Length, null);

    /// <summary>
    /// リンク入りの値も受けられる本体。plainValueLen はマークアップ抜きの値の文字数
    /// （縮小判定はタグを数えると誤るため別で受ける）。onLinkTap が非null のときは
    /// タップを受け、押された &lt;link="{i}"&gt; のインデックスを通知する。
    /// </summary>
    private void AddInfoRow(Transform parent, string label, string value, int plainValueLen,
        System.Action<int> onLinkTap)
    {
        var go = new GameObject("__InfoRow");
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        var le = go.AddComponent<LayoutElement>();
        le.minHeight = 60f;
        var tmp = go.AddComponent<TextMeshProUGUI>();
        if (_jp != null) tmp.font = _jp;
        tmp.richText = true;
        int len = label.Length + plainValueLen;
        string v = len <= 26 ? value
            : len <= 60 ? $"<size=32>{value}</size>"
            : $"<size=28>{value}</size>";
        tmp.text = $"<b><color=#854d1a>{label}</color></b>　{v}";
        tmp.fontSize = 38; tmp.color = C_INK;
        tmp.alignment = TextAlignmentOptions.Left;
        tmp.enableWordWrapping = true;
        tmp.overflowMode = TextOverflowModes.Ellipsis;
        tmp.raycastTarget = onLinkTap != null;
        if (onLinkTap != null) go.AddComponent<TmpLinkTapHandler>().onTap = onLinkTap;
    }

    /// <summary>「見出し」＋折り返し値のブロック（ジャンル用）。onLinkTap は AddInfoRow と同様。</summary>
    private void AddInfoBlock(Transform parent, string label, string value, System.Action<int> onLinkTap)
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
        tmp.raycastTarget = onLinkTap != null;
        if (onLinkTap != null) go.AddComponent<TmpLinkTapHandler>().onTap = onLinkTap;
    }

    /// <summary>各値を下線＋リンク色の &lt;link="{i}"&gt; にして sep で連結する（詳細モーダル用）。</summary>
    private static string LinkedValues(string[] values, string sep)
    {
        // 下線グリフ（'_'）が無いフォントでは色だけでリンクを表す
        // （欠落グリフはTMPの再レイアウト無限ループ→OOMの引き金。emoji-tmp-oom-crash と同根）
        bool underline = !_missingGlyphs.Contains('_');
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < values.Length; i++)
        {
            if (i > 0) sb.Append(sep);
            sb.Append("<link=\"").Append(i).Append("\">");
            if (underline) sb.Append("<u>");
            sb.Append("<color=").Append(LINK_HEX).Append(">").Append(SafeText(values[i])).Append("</color>");
            if (underline) sb.Append("</u>");
            sb.Append("</link>");
        }
        return sb.ToString();
    }

    /// <summary>マークアップ抜きでの連結後の文字数（AddInfoRow の縮小判定用）。</summary>
    private static int PlainLength(string[] values, string sep)
    {
        int n = sep.Length * Mathf.Max(0, values.Length - 1);
        foreach (var v in values) n += v == null ? 0 : v.Length;
        return n;
    }

    /// <summary>TMPテキスト内の &lt;link&gt; 領域のタップを拾い、リンクID（=インデックス）を通知する。</summary>
    private class TmpLinkTapHandler : MonoBehaviour, IPointerClickHandler
    {
        public System.Action<int> onTap;

        public void OnPointerClick(PointerEventData e)
        {
            var tmp = GetComponent<TextMeshProUGUI>();
            if (tmp == null || onTap == null) return;
            var canvas = tmp.canvas;
            var cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera : null;
            int i = TMP_TextUtilities.FindIntersectingLink(tmp, e.position, cam);
            if (i >= 0 && int.TryParse(tmp.textInfo.linkInfo[i].GetLinkID(), out int idx))
                onTap(idx);
        }
    }

    private void CloseDetail()
    {
        if (_detailModal != null) Destroy(_detailModal);
        _detailModal = null;
        _detailEntry = null;
        _detailPlayedBg = null;
        _detailPlayedLabel = null;
        _detailFavBg = null;
        _detailFavLabel = null;
        _detailPhotoContent = null;
        foreach (var t in _detailThumbTextures) Destroy(t);
        _detailThumbTextures.Clear();
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
