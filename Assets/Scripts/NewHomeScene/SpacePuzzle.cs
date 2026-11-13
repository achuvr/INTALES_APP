using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// ピースパズル（2026-11-13 ユーザー指定「ピース座がアクティブになったら、横7マス・縦9マスの方眼に、テトリスのようなピースを
/// 当てはめてパズルができるように。オーディンの祝祭のように、ボーナスマス（5%OFFチケットなどのアイテムの絵）のまわり8マスを
/// ピースで囲むとその報酬がもらえる。1か月に1回リセット。配置も毎回変わる」）の全画面。
///  ・下の手持ちの欄からピースを選ぶ → 盤面をタップ・ドラッグして置く場所を決める（「回す」「裏返す」で向きを変える）→「ここに置く」。
///    置いたピースは動かせない。ほかのピース・ボーナスマスには重ねられない（重なると赤）
///  ・ボーナスマスのまわり8マスが全部埋まると、そのマスの物がもらえる（SpaceNotice.PuzzleBonus）。空きマスはどれも金色の縁
///  ・ピースは金貨座のショップで買う（ランダム 3G・選んで 7G）。この画面の「ショップでピースを買う」からも開ける
/// 開く場所: ホームの空のピース座（2026-11-13 ユーザー指定「宇宙に浮かんでいるピース座をタップしたらパズルゲームが出てくるように」。
/// 右の列にさいころ座・積み木座と3つ並ぶ。SpaceBackground）、所持品の「パズル」、ピースを買ったお知らせの「パズルを開く」。
/// 配色はピース座の色（青みのすみれ色）。Android の戻るは NewHomeController が Instance.HandleBack を呼ぶ（選んだピースを戻す → 閉じる）。
/// </summary>
public class SpacePuzzle : MonoBehaviour
{
    /// <summary>ピース座の色（見出し・縁・光）</summary>
    public static readonly Color ACCENT = SpaceTheme.Hex(0x8FA8FF);
    private static readonly Color ACCENT_LIGHT = SpaceTheme.Hex(0xCBD6FF);
    private static readonly Color BG = new Color(0.012f, 0.018f, 0.055f, 0.985f);
    private static readonly Color BOARD_BG = new Color(0.045f, 0.06f, 0.16f, 0.94f);
    private static readonly Color CELL_EMPTY = new Color(0.17f, 0.21f, 0.42f, 0.38f);
    private static readonly Color BONUS_BG = new Color(0.03f, 0.035f, 0.09f, 0.96f);
    private static readonly Color BUTTON_DARK = new Color(0.05f, 0.07f, 0.19f, 0.92f);

    private const string T_CAPTION = "PUZZLE";
    private const string T_TITLE = "ピースパズル";
    private const string T_MONTH_B = "月の盤面";
    private const string T_BONUS = "ボーナス ";
    private const string T_ALL_DONE = "ボーナスを全部手に入れました！";
    private const string T_INFO = "ボーナスマスのまわり8マスをピースで囲むと、描かれたアイテムがもらえます（毎月1日に新しい盤面）";
    private const string T_PICK = "下からピースを選んでください";
    private const string T_EMPTY = "ピースを持っていません。ショップで買えます";
    private const string T_MOVE = "盤面をタップ・ドラッグして、置く場所を決めます";
    private const string T_BAD = "ほかのピースやボーナスマスには重ねられません";
    private const string T_ROTATE = "回す";
    private const string T_FLIP = "裏返す";
    private const string T_PLACE = "ここに置く";
    private const string T_PLACING = "置いています…";
    private const string T_SHOP = "ショップでピースを買う";
    private const string T_NO_SHOP = "ピースは金貨座のショップで買えます（金貨座の完成が必要です）";
    private const string T_NONE = "このピースは持っていません。ショップで買えます";
    private const string T_SIZE_B = "マス）";
    private const string T_OPEN_FAILED = "パズルを開けませんでした";

    /// <summary>この画面で表示しうる文字（NewHome の起動時にまとめてフォントへ登録する）</summary>
    public static string GlyphText => new StringBuilder()
        .Append(T_CAPTION).Append(T_TITLE).Append(T_MONTH_B).Append(T_BONUS).Append(T_ALL_DONE).Append(T_INFO).Append(T_PICK)
        .Append(T_EMPTY).Append(T_MOVE).Append(T_BAD).Append(T_ROTATE).Append(T_FLIP).Append(T_PLACE).Append(T_PLACING)
        .Append(T_SHOP).Append(T_NO_SHOP).Append(T_NONE).Append(T_SIZE_B).Append(T_OPEN_FAILED)
        .Append("0123456789/ ×（）✓")
        .Append(PuzzleStore.GlyphText).Append(SpaceNotice.PuzzleGlyphText).ToString();

    // 配置（セーフエリアの中。上からの距離）
    private const float SIDE = 40f;
    private const float MAX_W = 1000f;
    private const float STATUS_TOP = 206f, INFO_TOP = 246f, BOARD_TOP = 290f;
    private const float PAD = 12f;       // 盤の外枠の内側の余白
    private const float GAP = 8f;        // マスとマスのすき間
    private const float TRAY_H = 150f, TRAY_GAP = 10f, HINT_H = 40f, CONTROLS_H = 112f;
    // 盤のほかに要る高さ（見出し〜盤の上・手持ちの欄・説明・ボタン・下の余白）
    private const float FIXED_H = BOARD_TOP + 24f + TRAY_H + 8f + HINT_H + 12f + CONTROLS_H + 40f;
    private const float MAX_CELL = 128f;

    /// <summary>開いている画面（なければ null）</summary>
    public static SpacePuzzle Instance { get; private set; }

    private Canvas _canvas;
    private CanvasGroup _group;
    private RectTransform _safe, _board, _cellLayer, _ghostLayer;
    private float _cw, _cell, _boardW, _boardH;
    private TextMeshProUGUI _status, _hint, _placeLabel;
    private GameObject _editRow, _pickRow;
    private CanvasGroup _placeGroup;
    private bool _closing, _busy;
    private float _nextCheck;
    private string _shownKey;

    private PuzzleState _state;
    private int[] _owned = new int[PuzzleRules.TYPES + 1];

    private sealed class TraySlot
    {
        public CanvasGroup Group;
        public TextMeshProUGUI Count;
        public GameObject Ring;
    }
    private readonly TraySlot[] _tray = new TraySlot[PuzzleRules.TYPES + 1];

    // 選んでいるピース（0 なら選んでいない）・向き・置く場所（形の真ん中のマス）
    private int _sel, _rot, _cx, _cy;
    private bool _flip, _valid;
    private List<int> _ghostCells;

    /// <summary>パズルを開く。ピース座が完成していなければ開かない（管理者は初めから開ける）</summary>
    public static SpacePuzzle Open(Canvas canvas = null)
    {
        if (Instance != null) return Instance;
        if (!PuzzleStore.Unlocked)
        {
            SpaceTheme.Toast(PuzzleStore.T_LOCKED);
            return null;
        }
        if (canvas == null) canvas = SpaceTheme.MainCanvas();
        if (canvas == null) return null;
        var root = SpaceTheme.Rect("__SpacePuzzle", canvas.transform);
        SpaceTheme.Stretch(root);
        root.SetAsLastSibling();
        SpaceTheme.SubCanvas(root.gameObject, true); // 星のまたたきをホームの再描画と分ける
        var page = root.gameObject.AddComponent<SpacePuzzle>();
        Instance = page;
        try
        {
            page.Build(canvas);
        }
        catch (Exception ex)
        {
            // 作る途中で失敗したら、見えないまま画面全体を覆って押せなくなる（アプリが固まったように見える）ので、片づけて知らせる
            Debug.LogError($"[SpacePuzzle] パズルの画面を作れませんでした: {ex}");
            Instance = null;
            UnityEngine.Object.Destroy(root.gameObject);
            SpaceTheme.Toast(T_OPEN_FAILED);
            return null;
        }
        return page;
    }

    /// <summary>戻る操作（Android の戻るボタン）。ピースを選んでいたら選ぶのをやめ、そうでなければ閉じる</summary>
    public void HandleBack()
    {
        if (_busy) return;
        if (_sel != 0)
        {
            Select(0);
            return;
        }
        Close();
    }

    public void Close()
    {
        if (_closing || _busy) return;
        _closing = true;
        StartCoroutine(CloseRoutine());
    }

    private IEnumerator CloseRoutine()
    {
        float from = _group.alpha;
        yield return SpaceTheme.Tween(0.2f, 0f, k => _group.alpha = from * (1f - k));
        Destroy(gameObject);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        if (_closing || _busy || Time.unscaledTime < _nextCheck) return;
        _nextCheck = Time.unscaledTime + 0.5f;
        // 盤面・持っているピースが変わったら（ショップで買った・月が変わった・ほかの端末で置いた など）作り直す
        if (PuzzleStore.Key != _shownKey) Refresh();
    }

    // ================================================================
    // 画面
    // ================================================================
    private void Build(Canvas canvas)
    {
        _canvas = canvas;
        var rt = (RectTransform)transform;
        _group = gameObject.AddComponent<CanvasGroup>();
        _group.alpha = 0f;

        var size = SpaceTheme.CanvasSize(canvas);
        var insets = SpaceTheme.SafeInsets(size);
        _cw = Mathf.Min(size.x - SIDE * 2f, MAX_W);
        float availH = size.y - insets.top - insets.bottom;
        _cell = Mathf.Floor(Mathf.Min(MAX_CELL, (_cw - PAD * 2f) / PuzzleRules.W, (availH - FIXED_H) / PuzzleRules.H));
        _cell = Mathf.Max(64f, _cell);
        _boardW = _cell * PuzzleRules.W + PAD * 2f;
        _boardH = _cell * PuzzleRules.H + PAD * 2f;

        // 背景（下のホームを押させない）: 深い紺に、上から差すピース座の色の光・星
        var dim = SpaceTheme.Rect("Dim", rt);
        SpaceTheme.Stretch(dim);
        SpaceTheme.Img(dim, null, BG, true);
        var glow = SpaceTheme.Rect("Glow", rt, new Vector2(0.5f, 1f), new Vector2(0f, -300f), new Vector2(1700f, 1300f));
        SpaceTheme.Img(glow, SpaceSprites.Glow, SpaceTheme.A(ACCENT, 0.13f));
        Sky(rt);

        _safe = SpaceTheme.Rect("Safe", rt);
        SpaceTheme.Stretch(_safe);
        _safe.gameObject.AddComponent<SpaceSafeArea>();

        BuildHeader();
        BuildBoard();
        BuildTray();
        BuildControls();
        Refresh();
        StartCoroutine(SpaceTheme.Tween(0.25f, 0f, k => _group.alpha = k));
    }

    private void BuildHeader()
    {
        var caption = SpaceTheme.Text(_safe, "Caption", T_CAPTION, 24, SpaceTheme.A(ACCENT, 0.95f), new Vector2(0f, 1f),
            new Vector2(SIDE + 16f, -70f), new Vector2(400f, 36f), FontStyles.Bold, TextAlignmentOptions.Left,
            new Vector2(0f, 0.5f));
        SpaceTheme.Tracking(caption, 18f);
        SpaceTheme.Text(_safe, "Title", T_TITLE, 52, SpaceTheme.TEXT, new Vector2(0f, 1f), new Vector2(SIDE + 16f, -126f),
            new Vector2(420f, 66f), FontStyles.Bold, TextAlignmentOptions.Left, new Vector2(0f, 0.5f));

        // 題の右に、この画面をくれたピース座（いまのレベルの姿。管理者で未完成なら完成した姿）を小さく
        var p = Constellations.Progress().Find(x => x.Def.Id == PuzzleStore.CONSTELLATION);
        if (p != null)
        {
            var fig = SpaceTheme.Rect("Constellation", _safe, new Vector2(0f, 1f), new Vector2(SIDE + 16f + 400f, -104f),
                new Vector2(120f, 120f));
            SpaceConstellationPage.Figure(fig, p.Def, Mathf.Max(p.Def.Size, p.Count - p.Partial), 120f);
        }

        // 右上の閉じる
        var close = SpaceTheme.Rect("Close", _safe, new Vector2(1f, 1f), new Vector2(-(SIDE + 52f), -84f),
            new Vector2(104f, 104f));
        var face = SpaceTheme.Img(close, SpaceSprites.Circle, BUTTON_DARK, true);
        var rim = SpaceTheme.Rect("Rim", close);
        SpaceTheme.Stretch(rim);
        SpaceTheme.Img(rim, SpaceSprites.RingSmall, SpaceTheme.A(ACCENT, 0.5f));
        SpaceTheme.Bar(close, Vector2.zero, 44f, 6f, 45f, SpaceTheme.TEXT);
        SpaceTheme.Bar(close, Vector2.zero, 44f, 6f, -45f, SpaceTheme.TEXT);
        SpaceTheme.Clickable(close.gameObject, face, Close);

        // 今月の盤面とボーナスの数・遊び方
        var top = new Vector2(0.5f, 1f);
        _status = SpaceTheme.Text(_safe, "Status", "", 30, ACCENT_LIGHT, top, new Vector2(0f, -STATUS_TOP),
            new Vector2(_cw, 42f), FontStyles.Bold);
        _status.richText = true;
        var info = SpaceTheme.Text(_safe, "Info", T_INFO, 22, SpaceTheme.A(SpaceTheme.TEXT_SUB, 0.9f), top,
            new Vector2(0f, -INFO_TOP), new Vector2(_cw, 34f), FontStyles.Bold);
        info.enableAutoSizing = true;
        info.fontSizeMin = 15f;
        info.fontSizeMax = 22f;
    }

    private void BuildBoard()
    {
        _board = SpaceTheme.Rect("Board", _safe, new Vector2(0.5f, 1f), new Vector2(0f, -(BOARD_TOP + _boardH * 0.5f)),
            new Vector2(_boardW, _boardH));
        SpaceTheme.Glass(_board, 28f, BOARD_BG, SpaceTheme.A(ACCENT, 0.4f), true); // 盤を押した位置を受け取る
        var input = _board.gameObject.AddComponent<SpacePuzzleBoard>();
        input.Owner = this;
        _cellLayer = SpaceTheme.Rect("Cells", _board);
        SpaceTheme.Stretch(_cellLayer);
        _ghostLayer = SpaceTheme.Rect("Ghost", _board);
        SpaceTheme.Stretch(_ghostLayer);
    }

    /// <summary>手持ちのピースの欄（8種類を1列。持っている数。押すと選ぶ・もう一度押すと選ぶのをやめる）</summary>
    private void BuildTray()
    {
        float trayTop = BOARD_TOP + _boardH + 24f;
        float slotW = (_cw - TRAY_GAP * (PuzzleRules.TYPES - 1)) / PuzzleRules.TYPES;
        var top = new Vector2(0.5f, 1f);
        for (int t = 1; t <= PuzzleRules.TYPES; t++)
        {
            int type = t;
            var slot = SpaceTheme.Rect("Piece_" + t, _safe, top,
                new Vector2(-_cw * 0.5f + slotW * 0.5f + (t - 1) * (slotW + TRAY_GAP), -(trayTop + TRAY_H * 0.5f)),
                new Vector2(slotW, TRAY_H));
            var face = SpaceTheme.Glass(slot, 24f, BUTTON_DARK, SpaceTheme.A(PuzzleArt.ColorOf(t), 0.25f), true);
            SpaceTheme.Clickable(slot.gameObject, face, () => OnTraySlot(type));
            var ring = SpaceTheme.Rect("Selected", slot);
            SpaceTheme.Stretch(ring, -5f, -5f, -5f, -5f);
            SpaceTheme.RoundedStroke(ring, ACCENT_LIGHT, 29f);
            ring.gameObject.SetActive(false);
            var icon = SpaceTheme.Rect("Icon", slot, new Vector2(0.5f, 1f), new Vector2(0f, -54f), new Vector2(slotW - 12f, 84f));
            var img = SpaceTheme.Img(icon, PuzzleArt.Icon(t), Color.white);
            img.preserveAspect = true;
            var count = SpaceTheme.Text(slot, "Count", "", 26, SpaceTheme.TEXT, new Vector2(0.5f, 0f), new Vector2(0f, 26f),
                new Vector2(slotW - 8f, 34f), FontStyles.Bold);
            count.enableAutoSizing = true;
            count.fontSizeMin = 18f;
            count.fontSizeMax = 26f;
            // U字（レア）にも「レア」とは書かない（2026-11-13 ユーザー指定「レアと記述する必要はない」）
            _tray[t] = new TraySlot { Group = slot.gameObject.AddComponent<CanvasGroup>(), Count = count, Ring = ring.gameObject };
        }
        _hint = SpaceTheme.Text(_safe, "Hint", "", 24, SpaceTheme.TEXT_SUB, top, new Vector2(0f, -(trayTop + TRAY_H + 8f + HINT_H * 0.5f)),
            new Vector2(_cw, HINT_H), FontStyles.Bold);
        _hint.enableAutoSizing = true;
        _hint.fontSizeMin = 16f;
        _hint.fontSizeMax = 24f;
    }

    /// <summary>
    /// 下のボタン。ピースを選んでいる間は「回す」「裏返す」「ここに置く」、選んでいないときは「ショップでピースを買う」
    /// </summary>
    private void BuildControls()
    {
        float y = BOARD_TOP + _boardH + 24f + TRAY_H + 8f + HINT_H + 12f + CONTROLS_H * 0.5f;
        var top = new Vector2(0.5f, 1f);

        _editRow = SpaceTheme.Rect("Edit", _safe, top, new Vector2(0f, -y), new Vector2(_cw, CONTROLS_H)).gameObject;
        var edit = (RectTransform)_editRow.transform;
        PillButton(edit, new Vector2(-_cw * 0.5f + 100f, 0f), new Vector2(200f, 100f), T_ROTATE, () =>
        {
            if (_busy || _sel == 0) return;
            _rot = (_rot + 1) % 4;
            UpdateGhost();
        });
        PillButton(edit, new Vector2(-_cw * 0.5f + 316f, 0f), new Vector2(200f, 100f), T_FLIP, () =>
        {
            if (_busy || _sel == 0) return;
            _flip = !_flip;
            UpdateGhost();
        });
        float placeW = Mathf.Min(440f, _cw - 432f - 16f);
        var place = SpaceTheme.Rect("Place", edit, SpaceTheme.CENTER, new Vector2(_cw * 0.5f - placeW * 0.5f, 0f),
            new Vector2(placeW, CONTROLS_H));
        var placeFace = SpaceTheme.GradientFace(place, SpaceTheme.Hex(0xFFE3A3), SpaceTheme.Hex(0xE09A3E), SpaceTheme.GOLD);
        SpaceTheme.Clickable(place.gameObject, placeFace, OnPlacePressed);
        _placeGroup = place.gameObject.AddComponent<CanvasGroup>();
        _placeLabel = SpaceTheme.Text(place, "Label", T_PLACE, 40, SpaceTheme.TEXT_DARK, FontStyles.Bold);
        SpaceTheme.Stretch(_placeLabel.rectTransform, 16f, 0f, 16f, 0f);
        _placeLabel.enableAutoSizing = true;
        _placeLabel.fontSizeMin = 24f;
        _placeLabel.fontSizeMax = 40f;

        _pickRow = SpaceTheme.Rect("Pick", _safe, top, new Vector2(0f, -y), new Vector2(_cw, CONTROLS_H)).gameObject;
        var pick = (RectTransform)_pickRow.transform;
        if (IgCurrency.Unlocked)
        {
            var shop = SpaceTheme.Rect("Shop", pick, SpaceTheme.CENTER, Vector2.zero, new Vector2(Mathf.Min(640f, _cw), CONTROLS_H));
            var shopFace = SpaceTheme.Glass(shop, 50f, new Color(0.03f, 0.13f, 0.08f, 0.9f), SpaceTheme.A(SpaceShop.GREEN, 0.7f), true);
            SpaceTheme.Clickable(shop.gameObject, shopFace, () =>
            {
                if (_busy) return;
                // ショップの上から開いていたら（ピースを買ったお知らせの「パズルを開く」）、閉じてショップに戻る。
                // そうでなければショップをこの画面の上に開く
                if (SpaceShop.Instance != null) Close();
                else SpaceShop.Open(_canvas);
            });
            var shopLabel = SpaceTheme.Text(shop, "Label", T_SHOP, 36, SpaceShop.GREEN, FontStyles.Bold);
            SpaceTheme.Stretch(shopLabel.rectTransform, 16f, 0f, 16f, 0f);
            shopLabel.enableAutoSizing = true;
            shopLabel.fontSizeMin = 24f;
            shopLabel.fontSizeMax = 36f;
        }
        else
        {
            var note = SpaceTheme.Text(pick, "Note", T_NO_SHOP, 24, SpaceTheme.TEXT_MUTED, FontStyles.Bold);
            SpaceTheme.Stretch(note.rectTransform);
            note.enableAutoSizing = true;
            note.fontSizeMin = 16f;
            note.fontSizeMax = 24f;
        }
    }

    private void PillButton(RectTransform parent, Vector2 pos, Vector2 size, string label, Action onClick)
    {
        var rt = SpaceTheme.Rect("Button_" + label, parent, SpaceTheme.CENTER, pos, size);
        var face = SpaceTheme.Glass(rt, size.y * 0.5f, BUTTON_DARK, SpaceTheme.A(ACCENT, 0.6f), true);
        SpaceTheme.Clickable(rt.gameObject, face, () => onClick());
        var t = SpaceTheme.Text(rt, "Label", label, 34, SpaceTheme.TEXT, FontStyles.Bold);
        SpaceTheme.Stretch(t.rectTransform, 10f, 0f, 10f, 0f);
        t.enableAutoSizing = true;
        t.fontSizeMin = 22f;
        t.fontSizeMax = 34f;
    }

    // ================================================================
    // 表示を今の内容に合わせる
    // ================================================================
    private void Refresh()
    {
        _state = PuzzleStore.Current;
        _owned = PuzzleStore.Owned;
        _shownKey = PuzzleStore.Key;
        if (_sel != 0 && _owned[_sel] <= 0) _sel = 0;
        RebuildCells();
        UpdateStatus();
        UpdateTray();
        UpdateGhost();
    }

    private void UpdateStatus()
    {
        int month = 0;
        if (_state.Month != null && _state.Month.Length >= 7)
            int.TryParse(_state.Month.Substring(5), NumberStyles.Integer, CultureInfo.InvariantCulture, out month);
        int got = _state.Claimed.Count, all = _state.Bonus.Count;
        string head = month > 0 ? month + T_MONTH_B + "　" : "";
        _status.text = got >= all && all > 0
            ? head + $"<color=#{ColorUtility.ToHtmlStringRGB(SpaceTheme.GOLD)}>{T_ALL_DONE}</color>"
            : head + T_BONUS + $"<color=#{ColorUtility.ToHtmlStringRGB(SpaceTheme.GOLD)}>{got}</color> / {all}";
    }

    private void UpdateTray()
    {
        for (int t = 1; t <= PuzzleRules.TYPES; t++)
        {
            var slot = _tray[t];
            int n = _owned[t];
            slot.Count.text = "×" + n;
            slot.Count.color = n > 0 ? SpaceTheme.TEXT : SpaceTheme.TEXT_MUTED;
            slot.Group.alpha = n > 0 ? 1f : 0.4f;
            slot.Ring.SetActive(t == _sel);
        }
    }

    /// <summary>マスの中心（盤の中の位置。x は右・y は下へ数える）</summary>
    private Vector2 CellCenter(int x, int y) =>
        new Vector2(-_boardW * 0.5f + PAD + (x + 0.5f) * _cell, _boardH * 0.5f - PAD - (y + 0.5f) * _cell);

    /// <summary>
    /// 盤のマスを描き直す: 空きマス（どれも金色の縁）→ 置いたピース（同じピースのマスはつなげて1つの塊に見せる）
    /// → ボーナスマス（物の絵。もらったものは淡くして ✓）
    /// </summary>
    private void RebuildCells()
    {
        for (int i = _cellLayer.childCount - 1; i >= 0; i--) Destroy(_cellLayer.GetChild(i).gameObject);
        var s = _state;
        float size = _cell - GAP, radius = Mathf.Round(_cell * 0.13f);
        var bonusCells = new HashSet<int>(s.Bonus.Select(b => b.Cell));

        // 空きマス: どれも金色の縁（2026-11-13 ユーザー指定「全ての方眼を、囲むと報酬がもらえるマスの色に」。
        // それまでは、まだのボーナスマスのまわりの空きマスだけ金色の縁だった）
        for (int i = 0; i < PuzzleRules.CELLS; i++)
        {
            if (s.Board[i] != 0 || bonusCells.Contains(i)) continue;
            var rt = CellRect("Empty" + i, i, size);
            SpaceTheme.Rounded(rt, CELL_EMPTY, radius);
            Edge(rt, SpaceTheme.A(SpaceTheme.GOLD, 0.4f), radius);
        }

        // 置いたピース: 先につなぎ目（同じピースの隣のマスの間）を描き、その上にマス
        for (int i = 0; i < PuzzleRules.CELLS; i++)
        {
            int k = s.Board[i];
            if (k == 0) continue;
            var color = PuzzleArt.ColorOf(s.Types[k - 1]);
            int x = PuzzleRules.XOf(i), y = PuzzleRules.YOf(i);
            var c0 = CellCenter(x, y);
            bool right = x + 1 < PuzzleRules.W && s.Board[i + 1] == k;
            bool down = y + 1 < PuzzleRules.H && s.Board[i + PuzzleRules.W] == k;
            if (right) Bridge(c0 + new Vector2(_cell * 0.5f, 0f), new Vector2(_cell, size), color);
            if (down) Bridge(c0 + new Vector2(0f, -_cell * 0.5f), new Vector2(size, _cell), color);
            if (right && down && s.Board[i + PuzzleRules.W + 1] == k)
                Bridge(c0 + new Vector2(_cell * 0.5f, -_cell * 0.5f), new Vector2(GAP + 2f, GAP + 2f), color);
        }
        for (int i = 0; i < PuzzleRules.CELLS; i++)
        {
            int k = s.Board[i];
            if (k == 0) continue;
            var rt = CellRect("Piece" + i, i, size);
            SpaceTheme.Rounded(rt, PuzzleArt.ColorOf(s.Types[k - 1]), radius);
        }

        // ボーナスマス
        foreach (var b in s.Bonus) BuildBonus(b, s.Claimed.Contains(b.Cell), size, radius);
    }

    /// <summary>
    /// マスの縁取り線。縁は子に描く（1つの部品に付けられる絵は1つだけなので、面の絵と同じ部品に付けると失敗して例外になる。
    /// 2026-11-13 にそれで画面を作る途中で止まり、見えない画面が残ってアプリが固まったようになった）
    /// </summary>
    private static void Edge(RectTransform cell, Color color, float radius)
    {
        var edge = SpaceTheme.Rect("Edge", cell);
        SpaceTheme.Stretch(edge);
        SpaceTheme.RoundedStroke(edge, color, radius);
    }

    private RectTransform CellRect(string name, int cell, float size) =>
        SpaceTheme.Rect(name, _cellLayer, SpaceTheme.CENTER, CellCenter(PuzzleRules.XOf(cell), PuzzleRules.YOf(cell)),
            new Vector2(size, size));

    private void Bridge(Vector2 center, Vector2 size, Color color)
    {
        var rt = SpaceTheme.Rect("Joint", _cellLayer, SpaceTheme.CENTER, center, size);
        SpaceTheme.Img(rt, null, color);
    }

    private void BuildBonus(PuzzleBonus b, bool claimed, float size, float radius)
    {
        var tint = PuzzleStore.RewardTint(b.Reward);
        var rt = CellRect("Bonus" + b.Cell, b.Cell, size);
        SpaceTheme.Rounded(rt, BONUS_BG, radius);
        Edge(rt, SpaceTheme.A(claimed ? SpaceTheme.GOLD : tint, claimed ? 0.9f : 0.75f), radius);
        var glow = SpaceTheme.Rect("Glow", rt, SpaceTheme.CENTER, Vector2.zero, new Vector2(size * 1.25f, size * 1.25f));
        SpaceTheme.Img(glow, SpaceSprites.Glow, SpaceTheme.A(tint, claimed ? 0.12f : 0.35f));
        var holder = SpaceTheme.Rect("Icon", rt, SpaceTheme.CENTER, Vector2.zero, new Vector2(size, size));
        var icon = PuzzleStore.RewardIcon(b.Reward);
        if (PuzzleStore.RewardIsCoupon(b.Reward))
            SpaceInventoryPanel.RoundedIcon(holder, icon, SpaceTheme.CENTER, Vector2.zero, size * 0.68f, size * 0.12f);
        else
        {
            var img = SpaceTheme.Img(holder, icon, Color.white);
            img.preserveAspect = true;
            img.enabled = icon != null;
            holder.sizeDelta = new Vector2(size * 0.78f, size * 0.78f);
        }
        if (claimed) holder.gameObject.AddComponent<CanvasGroup>().alpha = 0.35f;
        if (b.Reward == PuzzleRules.REWARD_GOLD || b.Count > 1)
        {
            string label = b.Reward == PuzzleRules.REWARD_GOLD ? IgCurrency.Format(b.Count) : "×" + b.Count;
            // 右下（マスの中に収まる位置）
            var t = SpaceTheme.Text(rt, "Count", label, Mathf.Round(size * 0.22f), SpaceTheme.GOLD, new Vector2(1f, 0f),
                new Vector2(-size * 0.34f, size * 0.15f), new Vector2(size * 0.6f, size * 0.3f), FontStyles.Bold);
            t.alignment = TextAlignmentOptions.Right;
            SpaceTextScaler.Exclude(t);
        }
        if (claimed)
        {
            var check = SpaceTheme.Rect("Check", rt, new Vector2(1f, 1f), new Vector2(-size * 0.18f, -size * 0.18f),
                new Vector2(size * 0.4f, size * 0.4f));
            SpaceTheme.Img(check, SpaceSprites.Circle, SpaceTheme.GOLD);
            var mark = SpaceTheme.Text(check, "Mark", "✓", Mathf.Round(size * 0.26f), SpaceTheme.TEXT_DARK, FontStyles.Bold);
            SpaceTheme.Stretch(mark.rectTransform);
            SpaceTextScaler.Exclude(mark);
        }
    }

    // ================================================================
    // ピースを選ぶ・動かす・置く
    // ================================================================
    private void OnTraySlot(int type)
    {
        if (_busy) return;
        if (_sel == type)
        {
            Select(0);
            return;
        }
        if (_owned[type] <= 0)
        {
            SpaceTheme.Toast(T_NONE, _canvas);
            return;
        }
        Select(type);
    }

    /// <summary>ピースを選ぶ（0 で選ぶのをやめる）。選んだら盤のまん中に、元の向きで出す</summary>
    private void Select(int type)
    {
        _sel = type;
        _rot = 0;
        _flip = false;
        _cx = PuzzleRules.W / 2;
        _cy = PuzzleRules.H / 2;
        UpdateTray();
        UpdateGhost();
    }

    /// <summary>盤を押した・なぞった位置へ、選んでいるピースを動かす（SpacePuzzleBoard から）</summary>
    public void OnBoardPointer(PointerEventData e)
    {
        if (_sel == 0 || _busy || _board == null) return;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_board, e.position, e.pressEventCamera, out var local))
            return;
        int x = Mathf.FloorToInt((local.x + _boardW * 0.5f - PAD) / _cell);
        int y = Mathf.FloorToInt((_boardH * 0.5f - PAD - local.y) / _cell);
        x = Mathf.Clamp(x, 0, PuzzleRules.W - 1);
        y = Mathf.Clamp(y, 0, PuzzleRules.H - 1);
        if (x == _cx && y == _cy) return;
        _cx = x;
        _cy = y;
        UpdateGhost();
    }

    /// <summary>選んでいるピースの影（置ける所なら色つき、重なるなら赤）と、説明・ボタンを合わせる</summary>
    private void UpdateGhost()
    {
        for (int i = _ghostLayer.childCount - 1; i >= 0; i--) Destroy(_ghostLayer.GetChild(i).gameObject);
        _ghostCells = null;
        _valid = false;
        var def = PuzzleRules.Piece(_sel);
        if (def != null)
        {
            var shape = PuzzleRules.Orient(def, _rot, _flip);
            var (bw, bh) = PuzzleRules.SizeOf(shape);
            int ox = Mathf.Clamp(_cx - (bw - 1) / 2, 0, PuzzleRules.W - bw);
            int oy = Mathf.Clamp(_cy - (bh - 1) / 2, 0, PuzzleRules.H - bh);
            _ghostCells = PuzzleRules.CellsAt(shape, ox, oy);
            _valid = _ghostCells != null && PuzzleRules.CanPlace(_state, _ghostCells);
            float size = _cell - GAP, radius = Mathf.Round(_cell * 0.13f);
            var color = _valid ? PuzzleArt.ColorOf(_sel) : SpaceTheme.DANGER;
            if (_ghostCells != null)
                foreach (int c in _ghostCells)
                {
                    var rt = SpaceTheme.Rect("Ghost" + c, _ghostLayer, SpaceTheme.CENTER,
                        CellCenter(PuzzleRules.XOf(c), PuzzleRules.YOf(c)), new Vector2(size, size));
                    SpaceTheme.Rounded(rt, SpaceTheme.A(color, _valid ? 0.78f : 0.6f), radius);
                    Edge(rt, SpaceTheme.A(Color.white, 0.9f), radius);
                }
        }

        // 説明とボタン
        bool editing = _sel != 0;
        _editRow.SetActive(editing);
        _pickRow.SetActive(!editing);
        if (!editing)
        {
            _hint.text = _owned.Sum() > 0 ? T_PICK : T_EMPTY;
            _hint.color = SpaceTheme.TEXT_SUB;
        }
        else
        {
            _hint.text = PuzzleStore.PieceLabel(_sel).TrimEnd('）') + "・" + def.Size + T_SIZE_B + "　" + (_valid ? T_MOVE : T_BAD);
            _hint.color = _valid ? SpaceTheme.TEXT_SUB : SpaceTheme.DANGER;
        }
        _placeGroup.alpha = _valid && !_busy ? 1f : 0.4f;
        if (!_busy) _placeLabel.text = T_PLACE;
    }

    private void OnPlacePressed()
    {
        if (_busy || _sel == 0) return;
        if (!_valid || _ghostCells == null)
        {
            SpaceTheme.Toast(T_BAD, _canvas);
            return;
        }
        PlaceRoutine(_sel, new List<int>(_ghostCells)).Forget();
    }

    private async UniTaskVoid PlaceRoutine(int type, List<int> cells)
    {
        _busy = true;
        _placeLabel.text = T_PLACING;
        _placeGroup.alpha = 0.6f;
        PuzzleStore.PlaceResult res;
        try
        {
            res = await PuzzleStore.PlaceAsync(type, cells, _state.Month);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[SpacePuzzle] 置けませんでした: {ex.Message}");
            res = new PuzzleStore.PlaceResult { Error = PuzzleStore.T_ERR_NETWORK };
        }
        if (this == null) return;
        _busy = false;
        if (res == null || !res.Ok)
        {
            SpaceTheme.Toast(res?.Error ?? PuzzleStore.T_ERR_NETWORK, _canvas);
            if (res != null && res.Error == PuzzleStore.T_ERR_MONTH) _sel = 0;
            Refresh();
            return;
        }
        _sel = 0;
        AssetsDatabase.instance?.PlaySetSE();
        Refresh();
        if (res.Got.Count > 0)
        {
            AssetsDatabase.instance?.PlayLevelUpSE();
            SpaceNotice.PuzzleBonus(res.Got);
        }
    }

    /// <summary>背景の星（ところどころまたたく）</summary>
    private static void Sky(RectTransform rt)
    {
        var rnd = new System.Random(20261114);
        var layer = SpaceTheme.Rect("Sky", rt);
        SpaceTheme.Stretch(layer);
        var tint = Color.Lerp(Color.white, ACCENT, 0.3f);
        for (int i = 0; i < 60; i++)
        {
            var pos = new Vector2((float)rnd.NextDouble(), (float)rnd.NextDouble());
            float s = 3f + (float)rnd.NextDouble() * 5f;
            var star = SpaceTheme.Rect("S", layer, pos, Vector2.zero, new Vector2(s, s));
            var img = SpaceTheme.Img(star, SpaceSprites.Circle, SpaceTheme.A(tint, 0.12f + (float)rnd.NextDouble() * 0.3f));
            if (i % 3 == 0) star.gameObject.AddComponent<SpaceTwinkle>().Init(img, img.color.a);
        }
    }
}

/// <summary>パズルの盤を押した・なぞった位置を SpacePuzzle に渡す（盤の外枠の絵が押す判定を持つ）</summary>
public class SpacePuzzleBoard : MonoBehaviour, IPointerDownHandler, IDragHandler
{
    public SpacePuzzle Owner;

    public void OnPointerDown(PointerEventData e)
    {
        if (Owner != null) Owner.OnBoardPointer(e);
    }

    public void OnDrag(PointerEventData e)
    {
        if (Owner != null) Owner.OnBoardPointer(e);
    }
}
