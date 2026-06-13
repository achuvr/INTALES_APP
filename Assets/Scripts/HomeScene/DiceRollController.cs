using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ダイスロール機能（コード生成UI）。
/// Homeシーンの戦闘ボタン（Button_Battle）から開く。
///
/// ダイスの種類: 1D4 / 1D6 / 1D8 / 1D10 / 2D6
///   ＋ 選択中キャラクターがレベル100超のときのみ 2D8 が解放される。
/// 結果はローカルで完結（Firestore通信なし）。
/// </summary>
public class DiceRollController : MonoBehaviour
{
    /// <summary>ダイス定義（表記、個数、面数、解放に必要なレベル）</summary>
    private static readonly (string label, int count, int faces, int requiredLevel)[] DICE =
    {
        ("1D4",  1, 4,  0),
        ("1D6",  1, 6,  0),
        ("1D8",  1, 8,  0),
        ("1D10", 1, 10, 0),
        ("2D6",  2, 6,  0),
        ("2D8",  2, 8,  101), // レベル100超（101以上）で解放
    };

    private static readonly Color C_PARCHMENT = new Color(0.99f, 0.95f, 0.84f, 0.98f);
    private static readonly Color C_BORDER    = new Color(0.84f, 0.66f, 0.18f, 1.00f);
    private static readonly Color C_TITLE     = new Color(0.38f, 0.16f, 0.04f, 1.00f);
    private static readonly Color C_DIVIDER   = new Color(0.80f, 0.62f, 0.18f, 0.70f);
    private static readonly Color C_DICE_BTN  = new Color(0.90f, 0.84f, 0.68f, 1.00f);
    private static readonly Color C_DICE_TXT  = new Color(0.45f, 0.22f, 0.04f, 1.00f);
    private static readonly Color C_RESULT    = new Color(0.55f, 0.30f, 0.02f, 1.00f);
    private static readonly Color C_ROLL_BTN  = new Color(0.84f, 0.66f, 0.18f, 1.00f);
    private static readonly Color C_BACK_BTN  = new Color(0.48f, 0.26f, 0.06f, 1.00f);
    private static readonly Color C_CLOSE_BTN = new Color(0.90f, 0.32f, 0.32f, 1.00f);
    private static readonly Color C_MUTED     = new Color(0.52f, 0.38f, 0.22f, 0.85f);

    private Canvas _canvas;
    private GameObject _overlay;
    private GameObject _selectPanel;
    private Transform _diceGrid;
    private GameObject _resultPanel;
    private TextMeshProUGUI _resultTitle;
    private TextMeshProUGUI _resultValue;
    private TextMeshProUGUI _resultDetail;
    private RawImage _diceView;
    private Dice3DSimulator _sim;
    private (string label, int count, int faces) _currentDice;
    private bool _rolling;

    private void Start()
    {
        var canvasGO = GameObject.Find("Canvas");
        _canvas = canvasGO != null ? canvasGO.GetComponent<Canvas>() : FindFirstObjectByType<Canvas>();
        if (_canvas == null) return;

        BuildUI();

        // 戦闘ボタンをフック（EquipmentMenuController と同じ方式）
        var battleBtn = Resources.FindObjectsOfTypeAll<GameObject>()
            .FirstOrDefault(g => g.name == "Button_Battle" && g.scene.IsValid());
        if (battleBtn != null)
        {
            var b = battleBtn.GetComponent<Button>();
            if (b != null)
            {
                // インスペクタで設定済みのonClick（バッグを開く等）は
                // RemoveAllListeners では消えないため、persistent側を個別に無効化する
                for (int i = 0; i < b.onClick.GetPersistentEventCount(); i++)
                    b.onClick.SetPersistentListenerState(i, UnityEngine.Events.UnityEventCallState.Off);
                b.onClick.RemoveAllListeners();
                b.onClick.AddListener(ShowDiceSelect);
            }
        }
        else
        {
            Debug.LogWarning("[Dice] Button_Battle が見つかりません");
        }
    }

    // ================================================================
    // 開閉
    // ================================================================
    private void ShowDiceSelect()
    {
        RebuildDiceGrid();
        _overlay.SetActive(true);
        _selectPanel.SetActive(true);
        _resultPanel.SetActive(false);
    }

    private void Hide()
    {
        _overlay.SetActive(false);
        if (_sim != null) _sim.SetVisible(false); // ダイスカメラの描画を止める
    }

    // ================================================================
    // ダイス選択グリッド
    // ================================================================
    private void RebuildDiceGrid()
    {
        for (int i = _diceGrid.childCount - 1; i >= 0; i--)
            Destroy(_diceGrid.GetChild(i).gameObject);

        var jp = GetJpFont();

        // 選択中キャラクターのレベルで解放判定
        int level = 0;
        var manager = UserDataManager.instance;
        if (manager != null)
        {
            var characters = manager.UserData?.Characters;
            int idx = manager.CurrentSelectCharacterNumber;
            if (characters != null && idx < characters.Count)
                level = characters[idx].Level;
        }

        var available = DICE.Where(d => level >= d.requiredLevel).ToList();

        const int COLS = 2;
        const float W = 400, H = 150, GAP_X = 24, GAP_Y = 24;
        for (int i = 0; i < available.Count; i++)
        {
            var dice = available[i];
            int col = i % COLS, row = i / COLS;
            float x = (col - (COLS - 1) / 2f) * (W + GAP_X);
            float y = 280 - row * (H + GAP_Y);

            var btn = MakeButton($"__Dice_{dice.label}", _diceGrid, C_DICE_BTN, dice.label, jp, 64, C_DICE_TXT,
                W, H, new Vector2(x, y), () => StartRoll(dice.label, dice.count, dice.faces));

            // 2D8（解放ダイス）には目印を付ける
            if (dice.requiredLevel > 0)
            {
                MakeLabel(btn.transform, "Lv100超 解放！", jp, 24, FontStyles.Bold,
                    new Color(0.78f, 0.16f, 0.16f), W, 36, new Vector2(0, -54));
            }
        }

        // 未解放の2D8をグレーで見せる（モチベーション用）
        if (level <= 100)
        {
            int i = available.Count;
            int col = i % COLS, row = i / COLS;
            float x = (col - (COLS - 1) / 2f) * (W + GAP_X);
            float y = 280 - row * (H + GAP_Y);
            var locked = MakeRect("__Dice_Locked", _diceGrid, new Color(0.75f, 0.72f, 0.65f, 0.85f), W, H);
            locked.GetComponent<RectTransform>().anchoredPosition = new Vector2(x, y);
            MakeLabel(locked.transform, "2D8", jp, 64, FontStyles.Bold, new Color(0.5f, 0.48f, 0.45f), W, H, Vector2.zero);
            MakeLabel(locked.transform, "Lv101で解放", jp, 24, FontStyles.Bold, C_MUTED, W, 36, new Vector2(0, -54));
        }
    }

    // ================================================================
    // ロール
    // ================================================================
    private void StartRoll(string label, int count, int faces)
    {
        if (_rolling) return;
        _currentDice = (label, count, faces);
        _selectPanel.SetActive(false);
        _resultPanel.SetActive(true);
        RollAsync().Forget();
    }

    private async UniTask RollAsync()
    {
        _rolling = true;
        var (label, count, faces) = _currentDice;
        _resultTitle.text = label;
        _resultValue.text = "";
        _resultDetail.text = "";

        // 3Dダイスワールドは初回ロール時に構築（以降使い回す）
        if (_sim == null)
            _sim = Dice3DSimulator.Create(GetJpFont());
        _diceView.texture = _sim.Texture;

        // ダイス本体の色をキャラクターの属性カラーにする
        var (job, element) = GetSelectedJobAndElement();
        _sim.SetDiceElement(element);

        // ロール音はダイスの物理衝突に同期して Dice3DSimulator 側で鳴る

        // 物理演算でダイスを転がし、静止後の出目を受け取る
        var rolls = await _sim.RollAsync(count, faces);
        int rollTotal = rolls.Sum();

        // ATKクーポンのボーナス（使用済みの分をここで消費して出目に加算）
        int atkBonus = DiceAtkBonus.Consume();
        int total = rollTotal + atkBonus;

        // 内訳表示（複数ダイスかボーナスがあるときだけ出す）
        string detail = "";
        if (count > 1 || atkBonus > 0)
        {
            detail = $"（{string.Join(" ＋ ", rolls)}";
            if (atkBonus > 0) detail += $" ＋ ATK{atkBonus}";
            detail += "）";
        }

        _resultValue.text = total.ToString();
        _resultDetail.text = detail;

        // 出目の強さ（0=全部1の最小値, 1=全部最大の出目）に応じたスキルエフェクト
        // 形は職業、色は属性で決まる（演出はダイスの実際の出目基準）
        float denom = count * faces - count;
        float intensity = denom > 0 ? (rollTotal - count) / denom : 1f;
        _sim.PlayResultEffect(job, element, intensity);

        Debug.Log($"[Dice] {label} → {total} ({string.Join(",", rolls)} +ATK{atkBonus}) intensity={intensity:F2}");

        // 出目を履歴に記録（端末ローカル・最大50件）
        LocalHistoryLog.Add("dice", $"{label} で {total} が出た{detail}");

        // グループ参加中なら、出目をメンバーリストの名前の右側に共有表示する
        GroupSession.AnnounceDiceResult(total);

        _rolling = false;
    }

    /// <summary>選択中キャラクターの職業と属性（取得できないときはwarrior/fire）</summary>
    private static (string job, string element) GetSelectedJobAndElement()
    {
        var manager = UserDataManager.instance;
        var characters = manager?.UserData?.Characters;
        int idx = manager != null ? manager.CurrentSelectCharacterNumber : 0;
        if (characters != null && idx < characters.Count)
        {
            var chara = characters[idx];
            return (
                string.IsNullOrEmpty(chara.Job) ? "warrior" : chara.Job,
                string.IsNullOrEmpty(chara.Element) ? "fire" : chara.Element
            );
        }
        return ("warrior", "fire");
    }

    // ================================================================
    // UI構築
    // ================================================================
    private void BuildUI()
    {
        var jp = GetJpFont();

        _overlay = new GameObject("__DiceOverlay");
        _overlay.transform.SetParent(_canvas.transform, false);
        var ort = _overlay.AddComponent<RectTransform>();
        ort.anchorMin = Vector2.zero; ort.anchorMax = Vector2.one;
        ort.offsetMin = ort.offsetMax = Vector2.zero;
        _overlay.AddComponent<Image>().color = new Color(0.08f, 0.04f, 0.20f, 0.68f);
        _overlay.SetActive(false);

        // ---- 選択パネル ----
        var selBorder = MakeRect("__SelectBorder", _overlay.transform, C_BORDER, 936, 1016);
        _selectPanel = selBorder;
        var selPanel = MakeRect("__SelectPanel", selBorder.transform, C_PARCHMENT, 920, 1000);

        MakeLabel(selPanel.transform, "ダイスをえらぶ", jp, 52, FontStyles.Bold, C_TITLE,
            700, 90, new Vector2(0, 420));
        MakeButton("__Close", selPanel.transform, C_CLOSE_BTN, "✕", jp, 52, Color.white,
            96, 96, new Vector2(396, 436), Hide);
        MakeRect("__Div", selPanel.transform, C_DIVIDER, 820, 4)
            .GetComponent<RectTransform>().anchoredPosition = new Vector2(0, 360);

        var grid = new GameObject("__DiceGrid");
        grid.transform.SetParent(selPanel.transform, false);
        var grt = grid.AddComponent<RectTransform>();
        grt.sizeDelta = new Vector2(880, 700);
        grt.anchoredPosition = new Vector2(0, -60);
        _diceGrid = grid.transform;

        // ---- 結果パネル（3Dダイスビュー付き） ----
        var resBorder = MakeRect("__ResultBorder", _overlay.transform, C_BORDER, 936, 1296);
        _resultPanel = resBorder;
        var resPanel = MakeRect("__ResultPanel", resBorder.transform, C_PARCHMENT, 920, 1280);

        _resultTitle = MakeLabel(resPanel.transform, "", jp, 56, FontStyles.Bold, C_TITLE,
            600, 90, new Vector2(0, 580)).GetComponent<TextMeshProUGUI>();
        MakeButton("__RClose", resPanel.transform, C_CLOSE_BTN, "✕", jp, 48, Color.white,
            88, 88, new Vector2(400, 580), Hide);
        MakeRect("__RDiv", resPanel.transform, C_DIVIDER, 820, 4)
            .GetComponent<RectTransform>().anchoredPosition = new Vector2(0, 520);

        // 3Dダイスの映像（RenderTexture）
        var viewBg = MakeRect("__DiceViewBg", resPanel.transform, new Color(0.05f, 0.04f, 0.08f), 820, 700);
        viewBg.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, 130);
        var viewGO = new GameObject("__DiceView");
        viewGO.transform.SetParent(viewBg.transform, false);
        viewGO.AddComponent<RectTransform>().sizeDelta = new Vector2(810, 690);
        _diceView = viewGO.AddComponent<RawImage>();

        _resultValue = MakeLabel(resPanel.transform, "", jp, 130, FontStyles.Bold, C_RESULT,
            700, 170, new Vector2(0, -330)).GetComponent<TextMeshProUGUI>();
        _resultDetail = MakeLabel(resPanel.transform, "", jp, 40, FontStyles.Normal, C_MUTED,
            700, 60, new Vector2(0, -440)).GetComponent<TextMeshProUGUI>();

        MakeButton("__Reroll", resPanel.transform, C_ROLL_BTN, "もう一度振る", jp, 38, C_TITLE,
            380, 105, new Vector2(-200, -555), () => { if (!_rolling) RollAsync().Forget(); });
        MakeButton("__BackToSelect", resPanel.transform, C_BACK_BTN, "ダイスをえらぶ", jp, 34, Color.white,
            380, 105, new Vector2(200, -555), () =>
            {
                if (_rolling) return;
                if (_sim != null) _sim.SetVisible(false);
                RebuildDiceGrid();
                _resultPanel.SetActive(false);
                _selectPanel.SetActive(true);
            });

        _resultPanel.SetActive(false);
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
        go.AddComponent<RectTransform>().sizeDelta = new Vector2(w, h);
        go.AddComponent<Image>().color = color;
        return go;
    }

    private static GameObject MakeLabel(Transform parent, string text, TMP_FontAsset font,
        float size, FontStyles style, Color color, float w, float h, Vector2 pos)
    {
        var go = new GameObject("__Label");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(w, h);
        rt.anchoredPosition = pos;
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

    private static GameObject MakeButton(string name, Transform parent, Color bg, string text,
        TMP_FontAsset font, float fontSize, Color textColor, float w, float h, Vector2 pos,
        UnityEngine.Events.UnityAction onClick)
    {
        var go = MakeRect(name, parent, bg, w, h);
        go.GetComponent<RectTransform>().anchoredPosition = pos;
        MakeLabel(go.transform, text, font, fontSize, FontStyles.Bold, textColor, w, h, Vector2.zero);
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = go.GetComponent<Image>();
        btn.onClick.AddListener(onClick);
        return go;
    }
}
