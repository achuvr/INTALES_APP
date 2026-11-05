using System;
using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 惑星を選ぶ画面（新ホーム右上の惑星ボタンから開く全画面オーバーレイ）。
///   上: 選んでいる惑星の大きなプレビュー（自転する）と、英字の名前・名前・ひとこと説明
///   中: 惑星の一覧（4列・縦スクロール。タップでプレビューが切り替わる）
///   下: 「この惑星にする」ボタン
/// 一覧の惑星は止まった1枚絵（SpacePlanets のサムネイル）なので、数が多くてもスクロールが重くならない。
/// 決定すると選んだ惑星を保存し、プレビューの惑星そのものをホームに渡して閉じる
/// （ホーム側がそれを中央まで飛ばして入れ替える）。✕・戻るボタンでは何も変えずに閉じる。
///
/// 惑星はアチーブメント報酬で、最初はルミナだけ。未解放の惑星は影の姿＋錠前で並び、
/// 選ぶとプレビューも影の姿になって「まだ解放されていません」と出る（決定はできない）。
/// 左上に解放済みの数を出す。解放はお店の惑星QR（CallMethodFromQR.UnlockPlanet）で行う。
/// </summary>
public class SpacePlanetPicker : MonoBehaviour
{
    private const string T_CAPTION = "PLANET SELECT";
    private const string T_TITLE = "惑星を選ぶ";
    private const string T_CHOOSE = "この惑星にする";
    private const string T_CURRENT = "いまの惑星です";
    private const string T_LOCKED = "まだ解放されていません";
    private const string T_LOCKED_SUB = "お店で条件を達成すると、QRコードで解放できます";
    private const string T_UNLOCKED = "解放";

    /// <summary>この画面で表示しうる文字（惑星の名前・説明は SpacePlanets.GlyphText）</summary>
    public static string GlyphText =>
        T_CAPTION + T_TITLE + T_CHOOSE + T_CURRENT + T_LOCKED + T_LOCKED_SUB + T_UNLOCKED + "0123456789/";

    // 配置（セーフエリア基準）
    private const int COLS = 4;
    private const float CELL_W = 212f, CELL_H = 216f, GAP = 18f;
    private const float THUMB = 168f;          // 一覧の惑星の枠（球の直径は 0.8 倍 = 134）
    private const float PREVIEW = 480f;        // プレビューの惑星の枠（球の直径 384）
    private const float HEADER_H = 170f;       // 見出しの高さ
    private const float BLOCK_PLANET = 430f;   // プレビューの惑星が占める高さ
    private const float BLOCK_TEXT = 162f;     // 名前・説明の高さ
    private const float BTN_H = 120f, BOTTOM = 56f, BTN_GAP = 30f;
    private const float LIST_PAD_TOP = 24f, LIST_PAD_BOTTOM = 36f, LIST_PAD_SIDE = 40f;

    private class Cell
    {
        public SpacePlanetDef Def;
        public Image Stroke;
        public GameObject Glow;
    }

    private Canvas _canvas;
    private CanvasGroup _group;
    private RectTransform _previewHolder, _previewSlot, _orbit;
    private SpacePlanetView _preview;
    private TextMeshProUGUI _enText, _nameText, _descText;
    private GameObject _chooseWrap, _currentWrap, _lockedWrap;
    private readonly List<Cell> _cells = new List<Cell>();
    private SpacePlanetDef _current, _selected;
    private Action<SpacePlanetDef, SpacePlanetView> _onChosen;
    private Action _onClosed;
    private bool _closing;

    /// <param name="current">いまホームにある惑星</param>
    /// <param name="onChosen">別の惑星に決めたとき（プレビューの惑星を渡す。受け取った側で付け替えること）</param>
    /// <param name="onClosed">画面が消えたとき（決定・取りやめのどちらでも）</param>
    public static SpacePlanetPicker Open(Canvas canvas, SpacePlanetDef current,
        Action<SpacePlanetDef, SpacePlanetView> onChosen, Action onClosed)
    {
        var root = SpaceTheme.Rect("__PlanetPicker", canvas.transform);
        SpaceTheme.Stretch(root);
        root.SetAsLastSibling();
        // プレビューが自転し続けるので、メインのキャンバスと分けておく
        SpaceTheme.SubCanvas(root.gameObject, true);
        var picker = root.gameObject.AddComponent<SpacePlanetPicker>();
        picker._canvas = canvas;
        picker._current = current;
        picker._selected = current;
        picker._onChosen = onChosen;
        picker._onClosed = onClosed;
        picker.Build();
        return picker;
    }

    /// <summary>戻る操作（Android の戻るボタン）</summary>
    public bool HandleBack()
    {
        Close();
        return true;
    }

    /// <summary>何も変えずに閉じる</summary>
    public void Close()
    {
        if (_closing) return;
        _closing = true;
        StartCoroutine(FadeOutAndDestroy());
    }

    // ================================================================
    // 組み立て
    // ================================================================
    private void Build()
    {
        var rt = (RectTransform)transform;
        _group = gameObject.AddComponent<CanvasGroup>();
        _group.alpha = 0f;

        // 暗幕（下のホームを押させない）
        var dim = SpaceTheme.Rect("Dim", rt);
        SpaceTheme.Stretch(dim);
        SpaceTheme.Img(dim, null, new Color(0.01f, 0.015f, 0.05f, 0.95f), true);

        var size = SpaceTheme.CanvasSize(_canvas);
        var insets = SpaceTheme.SafeInsets(size);
        float safeH = size.y - insets.top - insets.bottom;

        var safe = SpaceTheme.Rect("Safe", rt);
        SpaceTheme.Stretch(safe);
        safe.gameObject.AddComponent<SpaceSafeArea>();

        // 見出し（QR画面と同じ並び）と閉じるボタン（ホームの歯車と同じ位置）
        var caption = SpaceTheme.Text(safe, "Caption", T_CAPTION, 24, SpaceTheme.A(SpaceTheme.CYAN, 0.85f),
            new Vector2(0.5f, 1f), new Vector2(0f, -70f), new Vector2(700f, 36f), FontStyles.Bold);
        caption.characterSpacing = 18f;
        SpaceTheme.Text(safe, "Title", T_TITLE, 44, SpaceTheme.TEXT, new Vector2(0.5f, 1f), new Vector2(0f, -124f),
            new Vector2(800f, 60f), FontStyles.Bold);
        BuildCloseButton(safe);
        BuildCounter(safe);

        // 上: プレビュー（見出しのすぐ下）
        float planetY = -(HEADER_H + BLOCK_PLANET * 0.5f);
        _orbit = SpaceTheme.Rect("Orbit", safe, new Vector2(0.5f, 1f), new Vector2(0f, planetY),
            new Vector2(PREVIEW * 1.2f, PREVIEW * 1.2f));
        SpaceTheme.Img(_orbit, SpaceSprites.Ring, SpaceTheme.A(SpaceTheme.LINE, 0.16f));
        var sat = SpaceTheme.Rect("Satellite", _orbit, SpaceTheme.CENTER, new Vector2(0f, PREVIEW * 0.6f),
            new Vector2(40f, 40f));
        SpaceTheme.Img(sat, SpaceSprites.Glow, SpaceTheme.A(SpaceTheme.CYAN, 0.8f));
        var satCore = SpaceTheme.Rect("Core", sat, SpaceTheme.CENTER, Vector2.zero, new Vector2(14f, 14f));
        SpaceTheme.Img(satCore, SpaceSprites.Circle, new Color(0.78f, 0.96f, 1f, 1f));

        _previewHolder = SpaceTheme.Rect("Preview", safe, new Vector2(0.5f, 1f), new Vector2(0f, planetY),
            new Vector2(PREVIEW, PREVIEW));

        float textTop = HEADER_H + BLOCK_PLANET;
        _enText = SpaceTheme.Text(safe, "En", "", 26, SpaceTheme.A(SpaceTheme.CYAN, 0.9f), new Vector2(0.5f, 1f),
            new Vector2(0f, -(textTop + 16f)), new Vector2(900f, 36f), FontStyles.Bold);
        _enText.characterSpacing = 24f;
        _nameText = SpaceTheme.Text(safe, "Name", "", 56, SpaceTheme.TEXT, new Vector2(0.5f, 1f),
            new Vector2(0f, -(textTop + 68f)), new Vector2(900f, 74f), FontStyles.Bold);
        _descText = SpaceTheme.Text(safe, "Desc", "", 30, SpaceTheme.TEXT_SUB, new Vector2(0.5f, 1f),
            new Vector2(0f, -(textTop + 126f)), new Vector2(980f, 44f), FontStyles.Bold);

        // 下: 決定ボタン
        float gridW = COLS * CELL_W + (COLS - 1) * GAP;
        BuildButtons(safe, gridW);

        // 中: 一覧（縦スクロール）
        float listTop = textTop + BLOCK_TEXT;
        float listBottom = BOTTOM + BTN_H + BTN_GAP;
        float viewH = Mathf.Max(CELL_H, safeH - listTop - listBottom);
        BuildList(safe, gridW, listTop, listBottom, viewH);

        ShowPreview(_selected, false);
        UpdateSelection();
        StartCoroutine(SpaceTheme.Tween(0.25f, 0f, k => _group.alpha = k));
    }

    private void BuildCloseButton(RectTransform safe)
    {
        var rt = SpaceTheme.Rect("Close", safe, new Vector2(1f, 1f), new Vector2(-112f, -84f), new Vector2(104f, 104f));
        var face = SpaceTheme.Img(rt, SpaceSprites.Circle, new Color(0.05f, 0.07f, 0.19f, 0.85f), true);
        var rim = SpaceTheme.Rect("Rim", rt);
        SpaceTheme.Stretch(rim);
        SpaceTheme.Img(rim, SpaceSprites.RingSmall, SpaceTheme.STROKE);
        SpaceTheme.Bar(rt, Vector2.zero, 44f, 6f, 45f, SpaceTheme.TEXT);
        SpaceTheme.Bar(rt, Vector2.zero, 44f, 6f, -45f, SpaceTheme.TEXT);
        SpaceTheme.Clickable(rt.gameObject, face, Close);
    }

    /// <summary>左上の「解放 3/24」（閉じるボタンと左右対称の位置）</summary>
    private static void BuildCounter(RectTransform safe)
    {
        var rt = SpaceTheme.Rect("Counter", safe, new Vector2(0f, 1f), new Vector2(64f, -84f), new Vector2(200f, 64f),
            new Vector2(0f, 0.5f));
        SpaceTheme.Glass(rt, 30f, new Color(0.05f, 0.07f, 0.19f, 0.85f));
        int owned = SpacePlanets.OwnedCount, total = SpacePlanets.All.Length;
        var t = SpaceTheme.Text(rt, "Label",
            $"<size=70%><color={SpaceTheme.SUB_HEX}>{T_UNLOCKED}</color></size> {owned}<size=70%><color={SpaceTheme.SUB_HEX}>/{total}</color></size>",
            32, SpaceTheme.TEXT, FontStyles.Bold);
        SpaceTheme.Stretch(t.rectTransform);
    }

    /// <summary>「この惑星にする」／いまの惑星を選んでいるとき／未解放の惑星を選んでいるときの表示（下の2つは押せない）</summary>
    private void BuildButtons(RectTransform safe, float width)
    {
        var choose = SpaceTheme.Rect("ChooseWrap", safe, new Vector2(0.5f, 0f), new Vector2(0f, BOTTOM),
            new Vector2(width, BTN_H), new Vector2(0.5f, 0f));
        var btn = SpaceTheme.Rect("ChooseButton", choose);
        SpaceTheme.Stretch(btn);
        var face = SpaceTheme.PrimaryFace(btn); // 背後の光は btn の兄として ChooseWrap の中に入る
        SpaceTheme.Clickable(btn.gameObject, face, Choose);
        SpaceTheme.Text(btn, "Label", T_CHOOSE, 38, SpaceTheme.TEXT_DARK, SpaceTheme.CENTER, Vector2.zero,
            new Vector2(width - 40f, 56f), FontStyles.Bold);
        _chooseWrap = choose.gameObject;

        var current = SpaceTheme.Rect("CurrentWrap", safe, new Vector2(0.5f, 0f), new Vector2(0f, BOTTOM),
            new Vector2(width, BTN_H), new Vector2(0.5f, 0f));
        SpaceTheme.Glass(current, 46f, new Color(0.05f, 0.07f, 0.19f, 0.7f));
        SpaceTheme.Text(current, "Label", T_CURRENT, 32, SpaceTheme.TEXT_MUTED, SpaceTheme.CENTER, Vector2.zero,
            new Vector2(width - 40f, 50f), FontStyles.Bold);
        _currentWrap = current.gameObject;

        var locked = SpaceTheme.Rect("LockedWrap", safe, new Vector2(0.5f, 0f), new Vector2(0f, BOTTOM),
            new Vector2(width, BTN_H), new Vector2(0.5f, 0f));
        SpaceTheme.Glass(locked, 46f, new Color(0.05f, 0.07f, 0.19f, 0.7f));
        var icon = SpaceTheme.Rect("Lock", locked, new Vector2(0f, 0.5f), new Vector2(56f, 0f), new Vector2(44f, 44f));
        SpaceTheme.Img(icon, SpaceSprites.Lock, SpaceTheme.TEXT_MUTED);
        SpaceTheme.Text(locked, "Label", T_LOCKED, 32, SpaceTheme.TEXT_SUB, SpaceTheme.CENTER, new Vector2(20f, 18f),
            new Vector2(width - 160f, 46f), FontStyles.Bold);
        SpaceTheme.Text(locked, "Sub", T_LOCKED_SUB, 22, SpaceTheme.TEXT_MUTED, SpaceTheme.CENTER, new Vector2(20f, -24f),
            new Vector2(width - 160f, 34f), FontStyles.Bold);
        _lockedWrap = locked.gameObject;
    }

    /// <summary>
    /// 一覧（縦スクロール）。上下の端はぼかして切る。開いたときは、いまの惑星が見える位置までスクロールしておく。
    /// </summary>
    private void BuildList(RectTransform safe, float gridW, float listTop, float listBottom, float viewH)
    {
        var all = SpacePlanets.All;
        int rows = Mathf.CeilToInt(all.Length / (float)COLS);
        float contentH = LIST_PAD_TOP + rows * CELL_H + (rows - 1) * GAP + LIST_PAD_BOTTOM;

        var viewport = SpaceTheme.Rect("List", safe);
        viewport.anchorMin = new Vector2(0.5f, 0f);
        viewport.anchorMax = new Vector2(0.5f, 1f);
        viewport.pivot = SpaceTheme.CENTER;
        viewport.offsetMin = new Vector2(-(gridW * 0.5f + LIST_PAD_SIDE), listBottom);
        viewport.offsetMax = new Vector2(gridW * 0.5f + LIST_PAD_SIDE, -listTop);
        SpaceTheme.HitArea(viewport); // マスのすき間でもスクロールできるように
        var mask = viewport.gameObject.AddComponent<RectMask2D>();
        mask.softness = new Vector2Int(0, 28);

        var content = SpaceTheme.Rect("Content", viewport, new Vector2(0.5f, 1f), Vector2.zero,
            new Vector2(gridW, contentH), new Vector2(0.5f, 1f));
        var scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Elastic;
        scroll.scrollSensitivity = 40f;

        for (int i = 0; i < all.Length; i++) BuildCell(content, all[i], i);

        int index = Array.IndexOf(all, _current);
        if (index > 0)
        {
            float rowCenter = LIST_PAD_TOP + (index / COLS) * (CELL_H + GAP) + CELL_H * 0.5f;
            float y = Mathf.Clamp(rowCenter - viewH * 0.5f, 0f, Mathf.Max(0f, contentH - viewH));
            content.anchoredPosition = new Vector2(0f, y);
        }
    }

    private void BuildCell(RectTransform content, SpacePlanetDef def, int index)
    {
        int col = index % COLS, row = index / COLS;
        var rt = SpaceTheme.Rect("Cell_" + def.Id, content, new Vector2(0f, 1f),
            new Vector2(CELL_W * 0.5f + col * (CELL_W + GAP), -(LIST_PAD_TOP + CELL_H * 0.5f + row * (CELL_H + GAP))),
            new Vector2(CELL_W, CELL_H));

        // 選択中の光（面の後ろ）
        var glow = SpaceTheme.Rect("Glow", rt, SpaceTheme.CENTER, Vector2.zero, new Vector2(CELL_W + 90f, CELL_H + 90f));
        SpaceTheme.Img(glow, SpaceSprites.Glow, SpaceTheme.A(SpaceTheme.CYAN, 0.30f));

        var face = SpaceTheme.Rect("Face", rt);
        SpaceTheme.Stretch(face);
        var faceImg = SpaceTheme.Rounded(face, new Color(0.07f, 0.09f, 0.24f, 0.78f), 34f, true);
        var stroke = SpaceTheme.Rect("Stroke", rt);
        SpaceTheme.Stretch(stroke);
        var strokeImg = SpaceTheme.RoundedStroke(stroke, SpaceTheme.STROKE, 34f);

        bool owned = SpacePlanets.IsOwned(def);
        BuildThumb(rt, def, new Vector2(0f, 20f), owned);

        bool inUse = def == _current;
        SpaceTheme.Text(rt, "Name", def.Name, 26,
            inUse ? SpaceTheme.MINT : (owned ? SpaceTheme.TEXT : SpaceTheme.TEXT_MUTED), SpaceTheme.CENTER,
            new Vector2(0f, -CELL_H * 0.5f + 30f), new Vector2(CELL_W - 12f, 36f), FontStyles.Bold);

        if (inUse)
        {
            // いまの惑星: 右上の角にチェックの印（惑星に重ならない位置）＋名前をミント色に（ホームの「入店中」と同じ色）
            var badge = SpaceTheme.Rect("InUse", rt, new Vector2(1f, 1f), new Vector2(-24f, -24f), new Vector2(34f, 34f));
            SpaceTheme.Img(badge, SpaceSprites.Circle, SpaceTheme.MINT);
            SpaceTheme.Bar(badge, new Vector2(-4.5f, -1.5f), 11f, 4f, -45f, SpaceTheme.TEXT_DARK);
            SpaceTheme.Bar(badge, new Vector2(3f, 1.5f), 17f, 4f, 48f, SpaceTheme.TEXT_DARK);
        }
        else if (!owned)
        {
            // 未解放: 右上の角に錠前
            var badge = SpaceTheme.Rect("Locked", rt, new Vector2(1f, 1f), new Vector2(-24f, -24f), new Vector2(36f, 36f));
            SpaceTheme.Img(badge, SpaceSprites.Circle, new Color(0.05f, 0.07f, 0.19f, 0.95f));
            var rim = SpaceTheme.Rect("Rim", badge);
            SpaceTheme.Stretch(rim);
            SpaceTheme.Img(rim, SpaceSprites.RingSmall, SpaceTheme.STROKE);
            var icon = SpaceTheme.Rect("Icon", badge, SpaceTheme.CENTER, Vector2.zero, new Vector2(20f, 20f));
            SpaceTheme.Img(icon, SpaceSprites.Lock, SpaceTheme.TEXT_SUB);
        }

        SpaceTheme.Clickable(rt.gameObject, faceImg, () => Select(def));
        _cells.Add(new Cell { Def = def, Stroke = strokeImg, Glow = glow.gameObject });
    }

    /// <summary>
    /// 一覧の惑星。ルミナ（オーブ）は画像なしで描けるのでそのまま、ほかは止まった1枚絵（できるまでは光だけ）。
    /// 1枚絵は球の直径が THUMB×0.8 になる大きさで置く。まわりの光だけは別の画像で後ろに敷く。
    /// 未解放の惑星は1枚絵を暗い色で染めて影の姿にし、光もほとんど消す。
    /// </summary>
    private void BuildThumb(RectTransform cell, SpacePlanetDef def, Vector2 pos, bool owned)
    {
        if (def.IsOrb)
        {
            var view = SpacePlanetView.Create(cell, def, THUMB, SpacePlanetView.Quality.Thumb, false);
            ((RectTransform)view.transform).anchoredPosition = pos;
            return;
        }
        float d = THUMB * SpacePlanetView.SPHERE_RATIO * def.Scale;
        var halo = SpaceTheme.Rect("Halo", cell, SpaceTheme.CENTER, pos, new Vector2(d * def.HaloScale, d * def.HaloScale));
        SpaceTheme.Img(halo, SpaceSprites.Glow, SpaceTheme.A(def.Accent, def.HaloAlpha * (owned ? 1f : 0.2f)));

        float side = THUMB * SpacePlanetView.SPHERE_RATIO / SpacePlanets.THUMB_SPHERE_FRAC;
        var rt = SpaceTheme.Rect("Thumb", cell, SpaceTheme.CENTER, pos, new Vector2(side, side));
        var raw = rt.gameObject.AddComponent<RawImage>();
        raw.raycastTarget = false;
        var tint = owned ? Color.white : SpacePlanetView.LOCKED_TINT;
        var tex = SpacePlanets.Thumb(def);
        if (tex != null)
        {
            raw.texture = tex;
            raw.color = tint;
        }
        else
        {
            raw.enabled = false;
            LoadThumb(raw, def, tint).Forget();
        }
    }

    private async UniTaskVoid LoadThumb(RawImage raw, SpacePlanetDef def, Color tint)
    {
        await SpacePlanets.Load(def, false);
        var tex = SpacePlanets.Thumb(def);
        if (raw == null || tex == null) return;
        raw.texture = tex;
        raw.color = new Color(tint.r, tint.g, tint.b, 0f);
        raw.enabled = true;
        if (isActiveAndEnabled)
            StartCoroutine(SpaceTheme.Tween(0.25f, 0f, k => { if (raw != null) raw.color = new Color(tint.r, tint.g, tint.b, k); }));
        else raw.color = tint;
    }

    // ================================================================
    // 操作
    // ================================================================
    private void Select(SpacePlanetDef def)
    {
        if (_closing || def == _selected) return;
        _selected = def;
        ShowPreview(def, true);
        UpdateSelection();
    }

    private void UpdateSelection()
    {
        foreach (var c in _cells)
        {
            bool on = c.Def == _selected;
            c.Stroke.color = on ? SpaceTheme.A(SpaceTheme.CYAN, 0.95f) : SpaceTheme.STROKE;
            c.Glow.SetActive(on);
        }
        _enText.text = _selected.En;
        _nameText.text = _selected.Name;
        _descText.text = _selected.Desc;
        bool owned = SpacePlanets.IsOwned(_selected);
        bool isCurrent = _selected == _current;
        _nameText.color = owned ? SpaceTheme.TEXT : SpaceTheme.TEXT_SUB;
        _chooseWrap.SetActive(owned && !isCurrent);
        _currentWrap.SetActive(owned && isCurrent);
        _lockedWrap.SetActive(!owned);
    }

    /// <summary>プレビューを差し替える（古いものは少し広がりながら消え、新しいものが奥から出てくる）</summary>
    private void ShowPreview(SpacePlanetDef def, bool animate)
    {
        if (_previewSlot != null) StartCoroutine(Vanish(_previewSlot));
        var slot = SpaceTheme.Rect("Slot", _previewHolder);
        SpaceTheme.Stretch(slot);
        var group = slot.gameObject.AddComponent<CanvasGroup>();
        _preview = SpacePlanetView.Create(slot, def, PREVIEW, SpacePlanetView.Quality.Large, true,
            !SpacePlanets.IsOwned(def));
        _previewSlot = slot;
        if (animate) StartCoroutine(Appear(slot, group));
    }

    private static IEnumerator Appear(RectTransform slot, CanvasGroup group)
    {
        group.alpha = 0f;
        slot.localScale = Vector3.one * 0.86f;
        yield return SpaceTheme.Tween(0.35f, 0.05f, k =>
        {
            if (slot == null) return;
            group.alpha = k;
            slot.localScale = Vector3.one * Mathf.Lerp(0.86f, 1f, k);
        });
    }

    private static IEnumerator Vanish(RectTransform slot)
    {
        var group = slot.GetComponent<CanvasGroup>();
        float from = group.alpha;
        yield return SpaceTheme.Tween(0.2f, 0f, k =>
        {
            if (slot == null) return;
            group.alpha = from * (1f - k);
            slot.localScale = Vector3.one * (1f + 0.1f * k);
        });
        if (slot != null) Destroy(slot.gameObject);
    }

    /// <summary>「この惑星にする」: 保存して、プレビューの惑星をホームに渡して閉じる</summary>
    private void Choose()
    {
        if (_closing || _selected == null || !SpacePlanets.IsOwned(_selected)) return;
        if (_selected == _current)
        {
            Close();
            return;
        }
        _closing = true;
        SpacePlanets.Current = _selected;
        var view = _preview;
        _preview = null;
        _onChosen?.Invoke(_selected, view); // 受け取った側がこの場で自分の下へ付け替える
        StartCoroutine(FadeOutAndDestroy());
    }

    private IEnumerator FadeOutAndDestroy()
    {
        float from = _group.alpha;
        yield return SpaceTheme.Tween(0.25f, 0f, k => _group.alpha = from * (1f - k));
        Destroy(gameObject);
    }

    private void Update()
    {
        if (_orbit != null) _orbit.localEulerAngles = new Vector3(0f, 0f, -Time.unscaledTime * 10f);
    }

    private void OnDestroy()
    {
        var cb = _onClosed;
        _onClosed = null;
        cb?.Invoke();
    }
}
