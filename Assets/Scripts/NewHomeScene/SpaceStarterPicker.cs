using System;
using System.Collections;
using System.Text;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 最初の惑星を選ぶ画面（2026-11-10 ユーザー指定。新しく登録した人がホームを最初に開いたとき。SpacePlanets.NeedsStarter）。
/// 赤＝ルブラ・青＝ルミナ・黄＝リモナ（SpacePlanets.STARTERS）を横に並べ、押して選んで「この惑星にする」→ 確認 → 保存
/// （SpacePlanets.ChooseStarterAsync。starter と planets に入れてホームの中央に置く）。
/// 選ぶまで閉じられない（戻る操作は「惑星を1つ選んでください」と知らせるだけ）。選ばなかった2つは7回目の来店でもらえる。
/// 以前からの会員（starter が無い）には出さない（ルミナを持ったまま）。
/// </summary>
public class SpaceStarterPicker : MonoBehaviour
{
    private const string T_CAPTION = "FIRST PLANET";
    private const string T_TITLE = "最初の惑星を選ぼう";
    private const string T_SUB = "好きな色の惑星を1つ選んでください";
    private const string T_NOTE = "選ばなかった2つの惑星も、7回目の来店でもらえます";
    private const string T_RED = "赤", T_BLUE = "青", T_YELLOW = "黄";
    private const string T_CHOOSE = "この惑星にする";
    private const string T_PICK_FIRST = "惑星を1つ選んでください";
    private const string T_CONFIRM_B = "を最初の惑星にしますか？";
    // 「選び直せません」だと、ホームに置く惑星もずっと変えられないように読めるので言い方を変える
    private const string T_CONFIRM_BODY = "最初にもらえる惑星は1つだけです\n選ばなかった2つは、7回目の来店でもらえます";
    private const string T_CONFIRM_OK = "決める";
    private const string T_SAVING = "保存しています…";
    private const string T_FAILED = "保存できませんでした。通信の状態を確かめて、もう一度お試しください";
    public const string T_DONE_TITLE = "最初の惑星が決まりました！";
    public const string T_DONE_NOTE = "ほかの2つの惑星は、7回目の来店でもらえます";
    public const string T_DONE_OK = "はじめる";

    /// <summary>この画面で出しうる文字（惑星の名前・説明は SpacePlanets.GlyphText）</summary>
    public static string GlyphText => new StringBuilder()
        .Append(T_CAPTION).Append(T_TITLE).Append(T_SUB).Append(T_NOTE).Append(T_RED).Append(T_BLUE).Append(T_YELLOW)
        .Append(T_CHOOSE).Append(T_PICK_FIRST).Append(T_CONFIRM_B).Append(T_CONFIRM_BODY).Append(T_CONFIRM_OK)
        .Append(T_SAVING).Append(T_FAILED).Append(T_DONE_TITLE).Append(T_DONE_NOTE).Append(T_DONE_OK).Append("キャンセル")
        .ToString();

    private static readonly Vector2 TOP = new Vector2(0.5f, 1f);
    private const float CARD_W = 320f, CARD_H = 560f, CARD_GAP = 20f, CARD_TOP = 300f; // CARD_TOP は見出しとカードのまとまりの上端から
    private const float BLOCK_H = CARD_TOP + CARD_H;  // 見出しとカードのまとまり（画面の中央付近に置く）
    private const float BLOCK_LIFT = 70f;             // 下の「この惑星にする」のぶん、少し上に寄せる
    private const float PLANET_FRAME = 280f;

    private sealed class Card
    {
        public SpacePlanetDef Def;
        public RectTransform Rt;
        public CanvasGroup Group;
        public GameObject Glow;
        public Image Stroke;
        public Color Tint;
    }

    private Canvas _canvas;
    private Action<SpacePlanetDef> _onChosen;
    private Action _onClosed;
    private CanvasGroup _group, _chooseGroup;
    private readonly Card[] _cards = new Card[3];
    private int _selected = -1;
    private bool _saving, _closing;
    private float _nextBackToast;

    public static SpaceStarterPicker Open(Canvas canvas, Action<SpacePlanetDef> onChosen, Action onClosed)
    {
        var root = SpaceTheme.Rect("__StarterPicker", canvas.transform);
        SpaceTheme.Stretch(root);
        root.SetAsLastSibling();
        SpaceTheme.SubCanvas(root.gameObject, true);
        var p = root.gameObject.AddComponent<SpaceStarterPicker>();
        p._canvas = canvas;
        p._onChosen = onChosen;
        p._onClosed = onClosed;
        p.Build();
        return p;
    }

    /// <summary>戻る操作: 選ぶまで閉じない（知らせるだけ）</summary>
    public void HandleBack()
    {
        if (_saving || Time.unscaledTime < _nextBackToast) return;
        _nextBackToast = Time.unscaledTime + 2f;
        SpaceTheme.Toast(T_PICK_FIRST, _canvas);
    }

    private void Build()
    {
        var rt = (RectTransform)transform;
        _group = gameObject.AddComponent<CanvasGroup>();
        _group.alpha = 0f;

        var dim = SpaceTheme.Rect("Dim", rt);
        SpaceTheme.Stretch(dim);
        SpaceTheme.Img(dim, null, new Color(0.01f, 0.015f, 0.05f, 0.97f), true);
        var glow = SpaceTheme.Rect("Glow", rt, TOP, new Vector2(0f, -620f), new Vector2(1600f, 1100f));
        SpaceTheme.Img(glow, SpaceSprites.Glow, SpaceTheme.A(SpaceTheme.VIOLET, 0.12f));

        var safe = SpaceTheme.Rect("Safe", rt);
        SpaceTheme.Stretch(safe);
        safe.gameObject.AddComponent<SpaceSafeArea>();

        // 見出しとカードは1つのまとまりにして、画面の中央付近に置く（縦長の画面でも間延びしないように）
        var block = SpaceTheme.Rect("Block", safe, SpaceTheme.CENTER, new Vector2(0f, BLOCK_LIFT), new Vector2(1060f, BLOCK_H));
        var caption = SpaceTheme.Text(block, "Caption", T_CAPTION, 24, SpaceTheme.A(SpaceTheme.CYAN, 0.9f), TOP,
            new Vector2(0f, -18f), new Vector2(700f, 36f), FontStyles.Bold);
        SpaceTheme.Tracking(caption, 18f);
        SpaceTheme.Text(block, "Title", T_TITLE, 52, SpaceTheme.TEXT, TOP, new Vector2(0f, -80f), new Vector2(980f, 70f),
            FontStyles.Bold);
        SpaceTheme.Text(block, "Sub", T_SUB, 30, SpaceTheme.TEXT_SUB, TOP, new Vector2(0f, -154f), new Vector2(980f, 44f),
            FontStyles.Bold);
        var note = SpaceTheme.Text(block, "Note", T_NOTE, 24, SpaceTheme.TEXT_MUTED, TOP, new Vector2(0f, -206f),
            new Vector2(980f, 36f), FontStyles.Bold);
        note.enableAutoSizing = true;
        note.fontSizeMin = 18f;
        note.fontSizeMax = 24f;

        for (int i = 0; i < SpacePlanets.STARTERS.Length && i < _cards.Length; i++)
        {
            var (color, id) = SpacePlanets.STARTERS[i];
            float x = (i - 1) * (CARD_W + CARD_GAP);
            _cards[i] = BuildCard(block, SpacePlanets.Find(id), color, x, i);
        }

        // この惑星にする（選ぶまでは薄く）
        var choose = SpaceTheme.Rect("Choose", safe, new Vector2(0.5f, 0f), new Vector2(0f, 110f), new Vector2(880f, 128f),
            new Vector2(0.5f, 0f));
        var face = SpaceTheme.PrimaryFace(choose);
        SpaceTheme.Clickable(choose.gameObject, face, OnChoose);
        var label = SpaceTheme.Text(choose, "Label", T_CHOOSE, 40, SpaceTheme.TEXT_DARK, FontStyles.Bold);
        SpaceTheme.Stretch(label.rectTransform);
        _chooseGroup = choose.gameObject.AddComponent<CanvasGroup>();
        _chooseGroup.alpha = 0.4f;

        StartCoroutine(SpaceTheme.Tween(0.35f, 0f, k => _group.alpha = k));
    }

    /// <summary>惑星1つ分のカード（惑星・色の印・名前・ひとこと）。押すと選ぶ</summary>
    private Card BuildCard(RectTransform parent, SpacePlanetDef def, PlanetColor color, float x, int index)
    {
        var card = new Card { Def = def };
        var tint = PlanetColors.Of(color);
        var rt = SpaceTheme.Rect("Card_" + def.Id, parent, TOP, new Vector2(x, -CARD_TOP), new Vector2(CARD_W, CARD_H), TOP);
        card.Rt = rt;
        card.Group = rt.gameObject.AddComponent<CanvasGroup>();
        card.Tint = tint;

        // 選んでいるときの光（面の後ろ。惑星を選ぶ画面のマスと同じ作り）
        var glow = SpaceTheme.Rect("Glow", rt, SpaceTheme.CENTER, Vector2.zero, new Vector2(CARD_W + 120f, CARD_H + 120f));
        SpaceTheme.Img(glow, SpaceSprites.Glow, SpaceTheme.A(tint, 0.35f));
        card.Glow = glow.gameObject;
        card.Glow.SetActive(false);

        var faceRt = SpaceTheme.Rect("Face", rt);
        SpaceTheme.Stretch(faceRt);
        var face = SpaceTheme.Rounded(faceRt, new Color(0.07f, 0.09f, 0.24f, 0.8f), 40f, true);
        SpaceTheme.Clickable(rt.gameObject, face, () => Select(index));
        // 縁（選んでいるときは惑星の色）
        var stroke = SpaceTheme.Rect("Stroke", rt);
        SpaceTheme.Stretch(stroke);
        card.Stroke = SpaceTheme.RoundedStroke(stroke, SpaceTheme.STROKE, 40f);

        float top = CARD_H * 0.5f; // カードの中心から上端まで
        var view = SpacePlanetView.Create(rt, def, PLANET_FRAME, SpacePlanetView.Quality.Thumb, true);
        ((RectTransform)view.transform).anchoredPosition = new Vector2(0f, top - 40f - PLANET_FRAME * 0.5f);

        // 色の印（丸＋「赤」）
        string colorName = color == PlanetColor.Red ? T_RED : color == PlanetColor.Blue ? T_BLUE : T_YELLOW;
        var chip = SpaceTheme.Rect("Color", rt, SpaceTheme.CENTER, new Vector2(0f, top - 352f), new Vector2(120f, 48f));
        SpaceTheme.Rounded(chip, SpaceTheme.A(PlanetColors.Of(color), 0.18f), 24f);
        var dot = SpaceTheme.Rect("Dot", chip, new Vector2(0f, 0.5f), new Vector2(30f, 0f), new Vector2(24f, 24f));
        SpaceTheme.Img(dot, SpaceSprites.Circle, PlanetColors.Of(color));
        SpaceTheme.Text(chip, "Label", colorName, 28, SpaceTheme.TEXT, new Vector2(0f, 0.5f), new Vector2(54f, 0f),
            new Vector2(56f, 40f), FontStyles.Bold, TextAlignmentOptions.Left, new Vector2(0f, 0.5f));

        SpaceTheme.Text(rt, "Name", def.Name, 40, SpaceTheme.TEXT, SpaceTheme.CENTER, new Vector2(0f, top - 418f),
            new Vector2(CARD_W - 24f, 56f), FontStyles.Bold);
        // ひとことは1行に収める（長ければ小さくする）
        var desc = SpaceTheme.Text(rt, "Desc", def.Desc, 24, SpaceTheme.TEXT_SUB, SpaceTheme.CENTER,
            new Vector2(0f, top - 476f), new Vector2(CARD_W - 28f, 40f), FontStyles.Bold);
        desc.enableAutoSizing = true;
        desc.fontSizeMin = 16f;
        desc.fontSizeMax = 24f;
        return card;
    }

    private void Select(int index)
    {
        if (_saving || _closing) return;
        _selected = index;
        for (int i = 0; i < _cards.Length; i++)
        {
            var c = _cards[i];
            if (c == null) continue;
            bool on = i == index;
            c.Glow.SetActive(on);
            c.Stroke.color = on ? SpaceTheme.A(c.Tint, 0.95f) : SpaceTheme.STROKE;
            c.Group.alpha = on ? 1f : 0.6f;
            c.Rt.localScale = Vector3.one * (on ? 1.04f : 1f);
        }
        _chooseGroup.alpha = 1f;
    }

    private void OnChoose()
    {
        if (_saving || _closing) return;
        if (_selected < 0 || _cards[_selected] == null)
        {
            SpaceTheme.Toast(T_PICK_FIRST, _canvas);
            return;
        }
        var def = _cards[_selected].Def;
        SpaceDialog.Confirm(_canvas, def.Name + T_CONFIRM_B, T_CONFIRM_BODY, T_CONFIRM_OK, false,
            () => SaveAsync(def).Forget());
    }

    private async UniTaskVoid SaveAsync(SpacePlanetDef def)
    {
        if (_saving) return;
        _saving = true;
        var busy = SpaceDialog.Busy(_canvas, T_SAVING);
        bool ok = await SpacePlanets.ChooseStarterAsync(def);
        if (busy != null) Destroy(busy);
        _saving = false;
        if (this == null) return;
        if (!ok)
        {
            SpaceTheme.Toast(T_FAILED, _canvas);
            return;
        }
        var cb = _onChosen;
        _onChosen = null;
        Close();
        cb?.Invoke(def);
    }

    private void Close()
    {
        if (_closing) return;
        _closing = true;
        StartCoroutine(FadeOutAndDestroy());
    }

    private IEnumerator FadeOutAndDestroy()
    {
        float from = _group.alpha;
        yield return SpaceTheme.Tween(0.3f, 0f, k => _group.alpha = from * (1f - k));
        Destroy(gameObject);
    }

    private void OnDestroy()
    {
        var cb = _onClosed;
        _onClosed = null;
        cb?.Invoke();
    }
}
