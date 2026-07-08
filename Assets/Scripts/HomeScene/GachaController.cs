using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 常用ガチャのUI（コード生成・ポップなガチャマシン風）。
/// キャラクターページ下部の「ガチャ」ボタン（バッグ等と同じグリッド）から開き、
/// 所持GPを消費して1回または5連で引く（1回の消費量は master/gacha の standard.cost_gp。
/// 5連は単価×5で、抽選と付与は GachaService.DrawManyAsync が1回の書き込みにまとめる）。
/// ガチャウィンドウを開くと現在の所持GPが表示される。
///
/// 演出: カプセルが詰まったドーム → ダイヤル回転＆本体シェイク →
/// カプセル排出 → ぶるぶる → パカッと開いて景品＋集中線＋紙吹雪。
/// 5連は1個ずつ「つぎへ！」で開封する（右上に n/5 の進行表示）。
/// イベントガチャはここからは引けず、店内のイベントQR読み取りで引く。
/// </summary>
public class GachaController : MonoBehaviour
{
    // ---- ポップ系パレット ----
    private static readonly Color C_BG        = new Color(1.00f, 0.97f, 0.88f, 0.99f); // クリーム
    private static readonly Color C_FRAME     = new Color(0.98f, 0.42f, 0.55f, 1.00f); // ピンク
    private static readonly Color C_TEXT      = new Color(0.30f, 0.20f, 0.25f, 1.00f); // こげ茶
    private static readonly Color C_MUTED     = new Color(0.55f, 0.45f, 0.45f, 0.90f);
    private static readonly Color C_MACHINE   = new Color(0.93f, 0.26f, 0.32f, 1.00f); // マシン赤
    private static readonly Color C_MACHINE_D = new Color(0.74f, 0.16f, 0.22f, 1.00f);
    private static readonly Color C_DOME      = new Color(0.78f, 0.92f, 1.00f, 0.55f); // ドーム水色
    private static readonly Color C_DOME_EDGE = new Color(0.55f, 0.78f, 0.95f, 0.90f);
    private static readonly Color C_DIAL      = new Color(0.96f, 0.96f, 0.93f, 1.00f);
    private static readonly Color C_SLOT      = new Color(0.22f, 0.14f, 0.18f, 1.00f);
    private static readonly Color C_BTN       = new Color(1.00f, 0.78f, 0.10f, 1.00f); // ビタミンイエロー
    private static readonly Color C_CLOSE     = new Color(0.98f, 0.42f, 0.55f, 1.00f);
    private static readonly Color C_CAP_WHITE = new Color(0.99f, 0.98f, 0.95f, 1.00f);

    /// <summary>カプセル・紙吹雪のカラフルカラー</summary>
    private static readonly Color[] POP_COLORS =
    {
        new Color(1.00f, 0.35f, 0.35f), // 赤
        new Color(1.00f, 0.66f, 0.15f), // オレンジ
        new Color(1.00f, 0.85f, 0.20f), // 黄
        new Color(0.35f, 0.80f, 0.40f), // 緑
        new Color(0.30f, 0.62f, 1.00f), // 青
        new Color(0.78f, 0.45f, 1.00f), // 紫
        new Color(1.00f, 0.55f, 0.75f), // ピンク
    };

    private Canvas _canvas;
    private GameObject _overlay;
    private GameObject _machineView;
    private GameObject _resultView;
    private RectTransform _machineRoot;
    private RectTransform _dial;
    private readonly List<(RectTransform rt, Vector2 basePos, float phase)> _capsules
        = new List<(RectTransform, Vector2, float)>();

    private TextMeshProUGUI _costText;
    private TextMeshProUGUI _gpText;
    private TextMeshProUGUI _drawBtnText;
    private Button _drawButton;
    private TextMeshProUGUI _drawFiveBtnText;
    private Button _drawFiveButton;

    // 結果ビュー
    private RectTransform _burst;
    private RectTransform _resCapRoot;
    private RectTransform _resCapTop;
    private RectTransform _resCapBottom;
    private Image _resCapTopImg;
    private Image _resCapBottomImg;
    private Image _resCapShineImg;
    private RectTransform _prizeRoot;
    private Image _prizeIcon;
    private GameObject _prizeIconShine;
    private TextMeshProUGUI _prizeText;
    private TextMeshProUGUI _prizeSubText;
    private TextMeshProUGUI _multiProgressText; // 5連の進行表示（例: 2 / 5）
    private GameObject _nextBtn;                // 5連の途中で次のカプセルへ進むボタン
    private GameObject _againBtn;
    private TextMeshProUGUI _againBtnText;
    private GameObject _backBtn;
    private bool _nextTapped;

    private bool _drawing;
    private int _cost = 5;      // 1回引くのに消費するGP（master/gacha から読み込む）
    private int _lastCount = 1; // 「もういちど」で同じ連数を引くために覚えておく

    // 円スプライト（コード生成。カプセル・ドーム・ダイヤルに使う）
    private static Texture2D _circleTex;
    private static Sprite _circleSprite;
    private static Sprite _topHalfSprite;
    private static Sprite _bottomHalfSprite;

    private void Start()
    {
        var canvasGO = GameObject.Find("Canvas");
        _canvas = canvasGO != null ? canvasGO.GetComponent<Canvas>() : FindFirstObjectByType<Canvas>();
        if (_canvas == null) return;

        // 下部の既存4ボタンもコード生成ボタンと同じ角丸スタイルに統一する
        RestyleSceneGridButtons();

        BuildEntryButton();
        BuildPanel();
    }

    /// <summary>カプセルのぷかぷか揺れと集中線の回転</summary>
    private void Update()
    {
        if (_overlay == null || !_overlay.activeSelf) return;

        if (_machineView.activeSelf)
        {
            // 抽選中はカプセルが激しく踊る
            float amp = _drawing ? 9f : 4f;
            float speed = _drawing ? 16f : 2.4f;
            foreach (var c in _capsules)
            {
                c.rt.anchoredPosition = c.basePos + new Vector2(
                    Mathf.Sin(Time.time * speed * 0.8f + c.phase) * amp * 0.6f,
                    Mathf.Abs(Mathf.Sin(Time.time * speed + c.phase)) * amp);
                c.rt.localRotation = Quaternion.Euler(0, 0,
                    Mathf.Sin(Time.time * speed * 0.7f + c.phase) * (_drawing ? 14f : 5f));
            }
        }

        if (_resultView.activeSelf && _burst != null && _burst.gameObject.activeSelf)
            _burst.Rotate(0, 0, 28f * Time.deltaTime);
    }

    // ================================================================
    // 開閉
    // ================================================================
    private void ShowPanel()
    {
        BackToMachine();
        _overlay.SetActive(true);
        LoadPoolInfoAsync().Forget();
    }

    private void Hide()
    {
        if (_drawing) return;
        _overlay.SetActive(false);
    }

    private void BackToMachine()
    {
        _resultView.SetActive(false);
        _machineView.SetActive(true);
        _machineRoot.anchoredPosition = Vector2.zero;
        RefreshLabels();
    }

    private async UniTask LoadPoolInfoAsync()
    {
        var pool = await GachaService.GetPoolAsync(GachaService.STANDARD_POOL);
        if (pool == null)
        {
            _costText.text = "設定なし";
            return;
        }
        _cost = pool.Cost;
        _costText.text = $"1回 {pool.Cost}GP";
        _drawBtnText.text = $"{pool.Cost}GPで 1回";
        _drawFiveBtnText.text = $"{pool.Cost * 5}GPで 5連";
    }

    private void RefreshLabels()
    {
        var manager = UserDataManager.instance;
        if (_gpText != null)
            _gpText.text = $"所持GP：{(manager?.UserData != null ? manager.UserData.GP : 0)}";
    }

    // ================================================================
    // 抽選＋演出
    // ================================================================
    /// <summary>ガチャを count 回引いて、カプセルを1個ずつ開封する（1=単発、5=5連）。</summary>
    private async UniTask DrawFlowAsync(int count)
    {
        if (_drawing) return;
        _drawing = true;
        SetDrawButtons(false);
        var ct = this.GetCancellationTokenOnDestroy();
        GameObject droppedCap = null;

        try
        {
            // GP不足は回す前に弾く（ダイヤルだけ回って何も出ないのを防ぐ）
            var manager = UserDataManager.instance;
            if (manager.UserData.GP < _cost * count)
            {
                FriendMenuController.ShowToast($"GPが足りません（{_cost * count}GP必要です）");
                return;
            }

            // 抽選はダイヤル回転と並行して進める（count回分まとめて1回の書き込み）
            var drawTask = GachaService.DrawManyAsync(GachaService.STANDARD_POOL, count, free: false);

            // ダイヤル2回転＋本体ガタガタ
            await Tween(1.1f, t =>
            {
                float e = 1f - (1f - t) * (1f - t); // easeOut
                _dial.localRotation = Quaternion.Euler(0, 0, -e * 720f);
                _machineRoot.anchoredPosition = new Vector2(Mathf.Sin(t * 60f) * 7f * (1f - t), 0);
            }, ct);
            _machineRoot.anchoredPosition = Vector2.zero;

            var multi = await drawTask;
            if (!multi.Success)
            {
                FriendMenuController.ShowToast(multi.Error);
                return;
            }
            _lastCount = count;
            int n = multi.Results.Count;

            // 出口からカプセルがコロン！（手前に拡大しながら落ちる）
            var firstColor = POP_COLORS[Random.Range(0, POP_COLORS.Length)];
            droppedCap = MakeCapsule("__Drop", _machineView.transform, firstColor, 130);
            var crt = droppedCap.GetComponent<RectTransform>();
            await Tween(0.55f, t =>
            {
                float bounce = Mathf.Abs(Mathf.Sin(t * Mathf.PI * 2.2f)) * 36f * (1f - t);
                crt.anchoredPosition = new Vector2(0, Mathf.Lerp(-360f, -130f, t) + bounce);
                crt.localScale = Vector3.one * Mathf.Lerp(0.45f, 1.5f, t * t);
            }, ct);

            // 結果ビューで1個ずつ開封（5連は「つぎへ！」で送る）
            for (int i = 0; i < n; i++)
            {
                var result = multi.Results[i];
                bool last = i == n - 1;
                // GP消費の内訳は最後のカプセルの補足にだけ出す
                if (last && multi.TotalCost > 0)
                    result.SubText += $"\nGPを{multi.TotalCost}消費（{multi.OldGp} → {multi.NewGp}）";

                var capColor = i == 0 ? firstColor : POP_COLORS[Random.Range(0, POP_COLORS.Length)];
                PrepareResultView(capColor, result);
                _multiProgressText.text = n > 1 ? $"{i + 1} / {n}" : "";
                _nextBtn.SetActive(false);
                _againBtn.SetActive(false);
                _backBtn.SetActive(false);

                if (i == 0)
                {
                    // 結果ビューへ（大きなカプセルにバトンタッチ）
                    Destroy(droppedCap);
                    droppedCap = null;
                    _machineView.SetActive(false);
                    _resultView.SetActive(true);
                }

                // ぶるぶる…
                await Tween(0.5f, t =>
                {
                    float shake = 12f * Mathf.Sin(t * 70f) * Mathf.SmoothStep(0.3f, 1f, t);
                    _resCapRoot.anchoredPosition = new Vector2(shake, 100);
                    _resCapRoot.localRotation = Quaternion.Euler(0, 0, shake * 0.6f);
                }, ct);
                _resCapRoot.localRotation = Quaternion.identity;

                // パカッ！（集中線＋景品＋紙吹雪）
                AssetsDatabase.instance?.PlayLevelUpSE();
                _burst.gameObject.SetActive(true);
                ConfettiAsync(ct).Forget();
                await Tween(0.45f, t =>
                {
                    float e = 1f - (1f - t) * (1f - t) * (1f - t);
                    // 殻は大きく飛んでいきながらフェードアウトし、中の景品画像を見せる
                    _resCapTop.anchoredPosition = new Vector2(-70f * e, 4 + 430f * e);
                    _resCapTop.localRotation = Quaternion.Euler(0, 0, 45f * e);
                    _resCapBottom.anchoredPosition = new Vector2(50f * e, -4 - 380f * e);
                    _resCapBottom.localRotation = Quaternion.Euler(0, 0, -30f * e);
                    float fade = 1f - Mathf.Clamp01((t - 0.35f) / 0.65f);
                    SetAlpha(_resCapTopImg, fade);
                    SetAlpha(_resCapBottomImg, fade);
                    SetAlpha(_resCapShineImg, 0.55f * fade);
                    _burst.localScale = Vector3.one * e;
                    // 景品はポンッと飛び出す（少し行き過ぎて戻る）
                    float p = t < 0.7f ? Mathf.Lerp(0f, 1.15f, t / 0.7f) : Mathf.Lerp(1.15f, 1f, (t - 0.7f) / 0.3f);
                    _prizeRoot.localScale = Vector3.one * p;
                }, ct);

                if (!last)
                {
                    // 「つぎへ！」のタップを待って次のカプセルへ
                    _nextTapped = false;
                    _nextBtn.SetActive(true);
                    while (!_nextTapped)
                        await UniTask.Yield(PlayerLoopTiming.Update, ct);
                    _nextBtn.SetActive(false);
                }
                else
                {
                    _againBtnText.text = count > 1 ? $"もういちど {count}連！" : "もういちど まわす！";
                    _againBtn.SetActive(true);
                    _backBtn.SetActive(true);
                }
            }

            // ホーム画面のレベル表示を更新する
            RefreshLabels();
            var cpm = FindObjectOfType<CharacterPageManager>();
            if (cpm != null) cpm.ChangePage();
        }
        catch (System.OperationCanceledException)
        {
            return; // 画面破棄時
        }
        finally
        {
            if (droppedCap != null) Destroy(droppedCap);
            _drawing = false;
            SetDrawButtons(true);
        }
    }

    private void SetDrawButtons(bool interactable)
    {
        if (_drawButton != null) _drawButton.interactable = interactable;
        if (_drawFiveButton != null) _drawFiveButton.interactable = interactable;
    }

    /// <summary>結果ビューの初期状態（閉じたカプセル）をセットする</summary>
    private void PrepareResultView(Color capColor, GachaResult result)
    {
        _resCapRoot.anchoredPosition = new Vector2(0, 100);
        _resCapRoot.localRotation = Quaternion.identity;
        _resCapTop.anchoredPosition = new Vector2(0, 4);
        _resCapTop.localRotation = Quaternion.identity;
        _resCapTopImg.color = capColor; // 代入でalphaも1に戻る
        _resCapBottom.anchoredPosition = new Vector2(0, -4);
        _resCapBottom.localRotation = Quaternion.identity;
        SetAlpha(_resCapBottomImg, 1f);
        SetAlpha(_resCapShineImg, 0.55f);
        _burst.gameObject.SetActive(false);
        _burst.localScale = Vector3.zero;
        _prizeRoot.localScale = Vector3.zero;
        _prizeText.text = result.DisplayName;
        _prizeSubText.text = result.SubText;

        // 景品画像（クーポン=Textures/ItemIcon、装備=DLキャッシュ）
        var sprite = PrizeSprite(result);
        if (sprite != null)
        {
            _prizeIcon.sprite = sprite;
            _prizeIcon.color = Color.white;
            _prizeIconShine.SetActive(false);
        }
        else
        {
            // アイコンが未取得の装備など: カプセル色の球で代用
            _prizeIcon.sprite = CircleSprite();
            _prizeIcon.color = capColor;
            _prizeIconShine.SetActive(true);
        }
    }

    /// <summary>景品のスプライトを解決する（見つからなければnull）</summary>
    private static Sprite PrizeSprite(GachaResult r)
    {
        switch (r.Type)
        {
            case "coupon":
                var assets = AssetsDatabase.instance;
                if (assets == null) return null;
                switch (r.Id)
                {
                    case "five":   return assets.FiveCouponSprite;
                    case "seven":  return assets.SevenCouponSprite;
                    case "drink":  return assets.DrinkCouponSprite;
                    case "coffee": return assets.CoffeeCouponSprite;
                    case "atk":    return assets.AtkCouponSprite;
                }
                return null;
            case "item":
                return ItemCacheManager.instance != null
                    ? ItemCacheManager.instance.GetIconSprite(r.Id)
                    : null;
            default:
                return null;
        }
    }

    private static void SetAlpha(Image img, float a)
    {
        var c = img.color;
        c.a = a;
        img.color = c;
    }

    /// <summary>紙吹雪をぱらぱら降らせる（約2秒で消える）</summary>
    private async UniTask ConfettiAsync(CancellationToken ct)
    {
        var root = new GameObject("__Confetti");
        root.transform.SetParent(_resultView.transform, false);
        root.AddComponent<RectTransform>().sizeDelta = Vector2.zero;

        var pieces = new List<(RectTransform rt, Image img, float x0, float fall, float sway, float phase, float spin)>();
        for (int i = 0; i < 30; i++)
        {
            var p = MakeRect("__C", root.transform, POP_COLORS[i % POP_COLORS.Length], 24, 13);
            var rt = p.GetComponent<RectTransform>();
            pieces.Add((rt, p.GetComponent<Image>(),
                Random.Range(-420f, 420f),
                Random.Range(900f, 1400f),
                Random.Range(2.5f, 5f),
                Random.Range(0f, 6.28f),
                Random.Range(-540f, 540f)));
        }

        try
        {
            await Tween(2.0f, t =>
            {
                foreach (var p in pieces)
                {
                    p.rt.anchoredPosition = new Vector2(
                        p.x0 + Mathf.Sin(t * p.sway * 6.28f + p.phase) * 45f,
                        640f - t * p.fall);
                    p.rt.localRotation = Quaternion.Euler(0, 0, t * p.spin);
                    var c = p.img.color;
                    c.a = t < 0.7f ? 1f : 1f - (t - 0.7f) / 0.3f;
                    p.img.color = c;
                }
            }, ct);
        }
        finally
        {
            if (root != null) Destroy(root);
        }
    }

    /// <summary>tが0→1まで毎フレームstepを呼ぶ簡易トゥイーン</summary>
    private static async UniTask Tween(float duration, System.Action<float> step, CancellationToken ct)
    {
        float t = 0f;
        while (t < 1f)
        {
            t = Mathf.Min(1f, t + Time.deltaTime / duration);
            step(t);
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
        }
    }

    // ================================================================
    // UI構築
    // ================================================================

    // 下部ボタングリッドに合わせた配置（バッグ/情報/戦闘/装備と同じ列・同じ実効サイズ）。
    // 既存グリッド: 列x=±221.23、行y=-638/-788、実効サイズ379.4x134.7（150ピッチ）。
    // その1行上（y=-488）にガチャ（左）とフレンド（右）を並べる。
    public const float GRID_COL_X = 221.23f;
    public const float GRID_ROW_Y = -488f;
    public const float GRID_BTN_W = 379.4f;
    public const float GRID_BTN_H = 134.7f;

    /// <summary>下部グリッドボタンの文字・アイコン色（既存ボタンのテキスト色に合わせる）</summary>
    public static readonly Color GRID_TEXT_COLOR = new Color(0.196f, 0.196f, 0.196f, 1f);

    /// <summary>
    /// 下部グリッドボタンの実効フォントサイズ。
    /// 既存ボタンのテキスト（fontSize 17.9 × ボタンscale 4.49）に合わせる。
    /// </summary>
    public const float GRID_FONT_SIZE = 80.4f;

    /// <summary>下部グリッドボタン共通の背景スタイル（白の角丸）を適用する</summary>
    public static void ApplyGridButtonStyle(Image img)
    {
        RoundedRectSprite.Apply(img);
        img.color = Color.white;
    }

    /// <summary>
    /// シーン上の下部4ボタン（バッグ/情報/戦闘/装備）の背景も同じ角丸スタイルに差し替える。
    /// シーンのボタンは Unity標準のUISprite＋scale=4.49 で作られているため、
    /// pixelsPerUnitMultiplier にスケール値を入れて角丸の見た目の半径を
    /// コード生成ボタン（scale=1）と一致させる。
    /// </summary>
    public static void RestyleSceneGridButtons()
    {
        string[] names = { "Button_OpenBag", "Button_OpenProfile", "Button_Battle", "Button_Equipment" };
        foreach (var name in names)
        {
            var go = Resources.FindObjectsOfTypeAll<GameObject>()
                .FirstOrDefault(g => g.name == name && g.scene.IsValid());
            var img = go != null ? go.GetComponent<Image>() : null;
            if (img == null)
            {
                Debug.LogWarning($"[Gacha] 下部ボタンが見つかりません: {name}");
                continue;
            }
            ApplyGridButtonStyle(img);
            img.pixelsPerUnitMultiplier = Mathf.Max(1f, go.transform.localScale.x);
        }
    }

    /// <summary>下部ボタングリッドの3行目左に置くガチャボタン</summary>
    private void BuildEntryButton()
    {
        var jp = GetJpFont();

        var btnGO = new GameObject("__GachaButton");
        var rt = btnGO.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(GRID_BTN_W, GRID_BTN_H);

        // Page_Characters の子に置く（キャラクターページ表示中のみ表示される）。
        // 既存4ボタンと同じく中央アンカー基準の座標で配置する。
        var page = _canvas.transform.Find("Page_Characters");
        btnGO.transform.SetParent(page != null ? page : _canvas.transform, false);
        if (page == null)
            Debug.LogWarning("[Gacha] Page_Characters が見つからないため、Canvas直下にボタンを置きます");
        rt.anchoredPosition = new Vector2(-GRID_COL_X, GRID_ROW_Y);
        btnGO.transform.SetAsLastSibling();

        // 既存の下部ボタン（バッグ等）と同じ白背景＋濃グレーの配色に統一する
        var bg = btnGO.AddComponent<Image>();
        ApplyGridButtonStyle(bg);

        // カプセルアイコン（上=濃グレー、下=ライトグレーの2トーン）
        var cap = MakeCapsule("__Icon", btnGO.transform, GRID_TEXT_COLOR, 78,
            new Color(0.72f, 0.72f, 0.72f, 1f));
        cap.GetComponent<RectTransform>().anchoredPosition = new Vector2(-130, 0);
        // 文字サイズは既存ボタン（バッグ等）と同じ実効サイズに合わせる
        var label = MakeLabel(btnGO.transform, "ガチャ", jp, GRID_FONT_SIZE, FontStyles.Bold,
            GRID_TEXT_COLOR, 250, GRID_BTN_H, new Vector2(45, 0));
        var labelTmp = label.GetComponent<TextMeshProUGUI>();
        labelTmp.enableAutoSizing = true;
        labelTmp.fontSizeMax = GRID_FONT_SIZE;
        labelTmp.fontSizeMin = 50;

        var btn = btnGO.AddComponent<Button>();
        btn.targetGraphic = bg;
        btn.onClick.AddListener(ShowPanel);
    }

    private void BuildPanel()
    {
        var jp = GetJpFont();

        _overlay = new GameObject("__GachaOverlay");
        _overlay.transform.SetParent(_canvas.transform, false);
        var ort = _overlay.AddComponent<RectTransform>();
        ort.anchorMin = Vector2.zero; ort.anchorMax = Vector2.one;
        ort.offsetMin = ort.offsetMax = Vector2.zero;
        _overlay.AddComponent<Image>().color = new Color(0.15f, 0.05f, 0.20f, 0.72f);
        _overlay.SetActive(false);

        var border = MakeRect("__Border", _overlay.transform, C_FRAME, 936, 1316);
        var panel  = MakeRect("__Panel", border.transform, C_BG, 920, 1300);

        // にじ色タイトル
        MakeLabel(panel.transform, RainbowText("ガチャガチャ"), jp, 64, FontStyles.Bold, Color.white,
            760, 100, new Vector2(0, 585));
        var close = MakeCircleButton("__Close", panel.transform, C_CLOSE, "✕", jp, 48, Color.white,
            92, new Vector2(400, 590), Hide);

        _machineView = new GameObject("__MachineView");
        _machineView.transform.SetParent(panel.transform, false);
        var mvrt = _machineView.AddComponent<RectTransform>();
        mvrt.anchorMin = Vector2.zero; mvrt.anchorMax = Vector2.one;
        mvrt.offsetMin = mvrt.offsetMax = Vector2.zero;
        BuildMachineView(jp);

        _resultView = new GameObject("__ResultView");
        _resultView.transform.SetParent(panel.transform, false);
        var rvrt = _resultView.AddComponent<RectTransform>();
        rvrt.anchorMin = Vector2.zero; rvrt.anchorMax = Vector2.one;
        rvrt.offsetMin = rvrt.offsetMax = Vector2.zero;
        BuildResultView(jp);
        _resultView.SetActive(false);

        close.transform.SetAsLastSibling(); // ✕は常に最前面
    }

    /// <summary>ガチャマシン本体のビュー</summary>
    private void BuildMachineView(TMP_FontAsset jp)
    {
        // コスト・キャラのバッジ（ピル風）
        var costPill = MakeRect("__CostPill", _machineView.transform, POP_COLORS[1], 320, 70);
        costPill.GetComponent<RectTransform>().anchoredPosition = new Vector2(-190, 488);
        _costText = MakeLabel(costPill.transform, "1回 5GP", jp, 32, FontStyles.Bold, Color.white,
            320, 70, Vector2.zero).GetComponent<TextMeshProUGUI>();

        // 所持GP表示（「1回 5GP」の横。ガチャを開くたびに RefreshLabels で更新する）
        var gpPill = MakeRect("__GpPill", _machineView.transform, POP_COLORS[3], 420, 70);
        gpPill.GetComponent<RectTransform>().anchoredPosition = new Vector2(200, 488);
        _gpText = MakeLabel(gpPill.transform, "所持GP：0", jp, 30, FontStyles.Bold, Color.white,
            420, 70, Vector2.zero).GetComponent<TextMeshProUGUI>();

        // ---- マシン本体（シェイク用に1つの親へ）----
        var machineGO = new GameObject("__Machine");
        machineGO.transform.SetParent(_machineView.transform, false);
        _machineRoot = machineGO.AddComponent<RectTransform>();
        _machineRoot.sizeDelta = Vector2.zero;

        // ドーム（カプセルがぎっしり）
        var domeEdge = MakeCircle("__DomeEdge", _machineRoot, C_DOME_EDGE, 480);
        domeEdge.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, 150);
        var dome = MakeCircle("__Dome", domeEdge.transform, new Color(0.92f, 0.98f, 1.00f, 0.95f), 460);

        _capsules.Clear();
        Vector2[] capPos =
        {
            new Vector2(-130, -95), new Vector2(5, -130), new Vector2(130, -90),
            new Vector2(-105, 15),  new Vector2(45, -25),  new Vector2(140, 35),
            new Vector2(-20, 95),
        };
        for (int i = 0; i < capPos.Length; i++)
        {
            var c = MakeCapsule($"__Cap{i}", dome.transform, POP_COLORS[i % POP_COLORS.Length], 115);
            var crt = c.GetComponent<RectTransform>();
            crt.anchoredPosition = capPos[i];
            _capsules.Add((crt, capPos[i], i * 1.3f));
        }

        // ドームのツヤ
        var shine = MakeCircle("__DomeShine", domeEdge.transform, new Color(1, 1, 1, 0.45f), 110);
        shine.GetComponent<RectTransform>().anchoredPosition = new Vector2(-130, 130);

        // 台座まわり（赤いボディ）
        var collar = MakeRect("__Collar", _machineRoot, C_MACHINE_D, 660, 64);
        collar.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, -110);
        var body = MakeRect("__Body", _machineRoot, C_MACHINE, 660, 290);
        body.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, -255);

        var band = MakeRect("__Band", _machineRoot, Color.white, 660, 52);
        band.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, -140);
        MakeLabel(band.transform, "★ GACHA ★", jp, 30, FontStyles.Bold, C_FRAME, 660, 52, Vector2.zero);

        // 回転ダイヤル
        var dialGO = MakeCircle("__Dial", _machineRoot, C_DIAL, 140);
        _dial = dialGO.GetComponent<RectTransform>();
        _dial.anchoredPosition = new Vector2(0, -240);
        MakeRect("__BarH", dialGO.transform, C_MACHINE_D, 120, 30);
        MakeRect("__BarV", dialGO.transform, C_MACHINE_D, 30, 120);
        MakeCircle("__DialCenter", dialGO.transform, C_DIAL, 44);

        // カプセルの出口
        var slot = MakeRect("__Slot", _machineRoot, C_SLOT, 180, 80);
        slot.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, -362);
        MakeRect("__SlotInner", slot.transform, new Color(0.10f, 0.06f, 0.08f, 1f), 150, 52);

        // まわすボタン（左=1回、右=5連）
        var drawBtn = MakeButton("__Draw", _machineView.transform, C_BTN, "5GPで 1回", jp, 36, C_TEXT,
            310, 140, new Vector2(-165, -480), () => DrawFlowAsync(1).Forget());
        _drawBtnText = drawBtn.GetComponentInChildren<TextMeshProUGUI>();
        _drawBtnText.enableAutoSizing = true;
        _drawBtnText.fontSizeMax = 36; _drawBtnText.fontSizeMin = 24;
        _drawButton = drawBtn.GetComponent<Button>();

        var drawFiveBtn = MakeButton("__DrawFive", _machineView.transform, POP_COLORS[1], "25GPで 5連", jp, 36, Color.white,
            310, 140, new Vector2(165, -480), () => DrawFlowAsync(5).Forget());
        _drawFiveBtnText = drawFiveBtn.GetComponentInChildren<TextMeshProUGUI>();
        _drawFiveBtnText.enableAutoSizing = true;
        _drawFiveBtnText.fontSizeMax = 36; _drawFiveBtnText.fontSizeMin = 24;
        _drawFiveButton = drawFiveBtn.GetComponent<Button>();

        MakeLabel(_machineView.transform, "※所持GPが足りない場合は引けません",
            jp, 23, FontStyles.Normal, C_MUTED, 840, 36, new Vector2(0, -585));
        MakeLabel(_machineView.transform, "イベントガチャは来店時のイベントQRで無料で引けます",
            jp, 23, FontStyles.Normal, C_MUTED, 840, 36, new Vector2(0, -622));
    }

    /// <summary>カプセルが開く結果ビュー</summary>
    private void BuildResultView(TMP_FontAsset jp)
    {
        MakeLabel(_resultView.transform, "おめでとう！", jp, 56, FontStyles.Bold, C_FRAME,
            700, 90, new Vector2(0, 450));

        // 5連の進行表示（例: 2 / 5。単発のときは空文字で見えない）
        _multiProgressText = MakeLabel(_resultView.transform, "", jp, 34, FontStyles.Bold, C_MUTED,
            300, 50, new Vector2(0, 385)).GetComponent<TextMeshProUGUI>();

        // 集中線（パカッの瞬間に出て回り続ける）
        var burstGO = new GameObject("__Burst");
        burstGO.transform.SetParent(_resultView.transform, false);
        _burst = burstGO.AddComponent<RectTransform>();
        _burst.anchoredPosition = new Vector2(0, 100);
        for (int i = 0; i < 12; i++)
        {
            var ray = MakeRect("__Ray", burstGO.transform,
                i % 2 == 0 ? new Color(1f, 0.85f, 0.30f, 0.85f) : new Color(1f, 0.65f, 0.75f, 0.65f),
                26, 250);
            var rrt = ray.GetComponent<RectTransform>();
            rrt.pivot = new Vector2(0.5f, 0f);
            rrt.anchoredPosition = Vector2.zero;
            rrt.localRotation = Quaternion.Euler(0, 0, i * 30f);
        }
        burstGO.SetActive(false);

        // 開くカプセル（上下別パーツ）
        var capRootGO = new GameObject("__ResultCapsule");
        capRootGO.transform.SetParent(_resultView.transform, false);
        _resCapRoot = capRootGO.AddComponent<RectTransform>();
        _resCapRoot.anchoredPosition = new Vector2(0, 100);

        var bottom = new GameObject("__Bottom");
        bottom.transform.SetParent(capRootGO.transform, false);
        _resCapBottom = bottom.AddComponent<RectTransform>();
        _resCapBottom.sizeDelta = new Vector2(260, 130);
        _resCapBottom.pivot = new Vector2(0.5f, 1f);
        _resCapBottom.anchoredPosition = new Vector2(0, -4);
        _resCapBottomImg = bottom.AddComponent<Image>();
        _resCapBottomImg.sprite = BottomHalfSprite();
        _resCapBottomImg.color = C_CAP_WHITE;

        var top = new GameObject("__Top");
        top.transform.SetParent(capRootGO.transform, false);
        _resCapTop = top.AddComponent<RectTransform>();
        _resCapTop.sizeDelta = new Vector2(260, 130);
        _resCapTop.pivot = new Vector2(0.5f, 0f);
        _resCapTop.anchoredPosition = new Vector2(0, 4);
        _resCapTopImg = top.AddComponent<Image>();
        _resCapTopImg.sprite = TopHalfSprite();
        _resCapTopImg.color = POP_COLORS[0];
        var shine = MakeCircle("__Shine", top.transform, new Color(1, 1, 1, 0.55f), 56);
        shine.GetComponent<RectTransform>().anchoredPosition = new Vector2(-48, 20);
        _resCapShineImg = shine.GetComponent<Image>();

        // 景品（カプセルの中からポンッと飛び出す。画像＋名前＋補足）
        var prizeGO = new GameObject("__Prize");
        prizeGO.transform.SetParent(_resultView.transform, false);
        _prizeRoot = prizeGO.AddComponent<RectTransform>();
        _prizeRoot.anchoredPosition = new Vector2(0, 100);

        var iconGO = new GameObject("__Icon");
        iconGO.transform.SetParent(prizeGO.transform, false);
        iconGO.AddComponent<RectTransform>().sizeDelta = new Vector2(230, 230);
        _prizeIcon = iconGO.AddComponent<Image>();
        _prizeIcon.preserveAspect = true;
        _prizeIcon.raycastTarget = false;
        _prizeIconShine = MakeCircle("__IconShine", iconGO.transform, new Color(1, 1, 1, 0.5f), 52);
        _prizeIconShine.GetComponent<RectTransform>().anchoredPosition = new Vector2(-44, 44);

        _prizeText = MakeLabel(prizeGO.transform, "", jp, 54, FontStyles.Bold, C_TEXT,
            820, 130, new Vector2(0, -235)).GetComponent<TextMeshProUGUI>();
        _prizeText.enableAutoSizing = true;
        _prizeText.fontSizeMin = 30;
        _prizeText.fontSizeMax = 54;
        _prizeSubText = MakeLabel(prizeGO.transform, "", jp, 30, FontStyles.Normal, C_MUTED,
            800, 110, new Vector2(0, -350)).GetComponent<TextMeshProUGUI>();

        // もう一回 ＆ もどる ＆ つぎへ（5連の開封送り。普段は非表示）
        _againBtn = MakeButton("__Again", _resultView.transform, C_BTN, "もういちど まわす！", jp, 40, C_TEXT,
            600, 140, new Vector2(0, -460), () =>
            {
                if (_drawing) return;
                BackToMachine();
                DrawFlowAsync(_lastCount).Forget(); // 直前と同じ連数で引き直す
            });
        _againBtnText = _againBtn.GetComponentInChildren<TextMeshProUGUI>();
        _backBtn = MakeButton("__Back", _resultView.transform, new Color(1f, 1f, 1f, 0.95f), "マシンにもどる", jp, 30, C_FRAME,
            360, 84, new Vector2(0, -590), () =>
            {
                if (_drawing) return;
                BackToMachine();
            });
        _nextBtn = MakeButton("__Next", _resultView.transform, C_BTN, "つぎへ！", jp, 40, C_TEXT,
            600, 140, new Vector2(0, -460), () => _nextTapped = true);
        _nextBtn.SetActive(false);
    }

    /// <summary>1文字ずつカラフルにするリッチテキスト</summary>
    private static string RainbowText(string s)
    {
        string[] cols = { "#FF5A5A", "#FFA928", "#3EC54B", "#3B9BFF", "#B96BFF", "#FF6FA8" };
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < s.Length; i++)
            sb.Append($"<color={cols[i % cols.Length]}>{s[i]}</color>");
        return sb.ToString();
    }

    // ================================================================
    // 円・カプセルスプライト（コード生成）
    // ================================================================
    private static Texture2D CircleTexture()
    {
        if (_circleTex != null) return _circleTex;
        const int S = 128;
        _circleTex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        var px = new Color32[S * S];
        float c = (S - 1) / 2f, r = S / 2f - 1.5f;
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                byte a = (byte)(Mathf.Clamp01(r - d + 1f) * 255f); // 1pxアンチエイリアス
                px[y * S + x] = new Color32(255, 255, 255, a);
            }
        }
        _circleTex.SetPixels32(px);
        _circleTex.Apply();
        return _circleTex;
    }

    private static Sprite CircleSprite()
    {
        if (_circleSprite == null)
            _circleSprite = Sprite.Create(CircleTexture(), new Rect(0, 0, 128, 128), new Vector2(0.5f, 0.5f));
        return _circleSprite;
    }

    /// <summary>カプセル上半分（ドームが上・平らな面が下）</summary>
    private static Sprite TopHalfSprite()
    {
        if (_topHalfSprite == null)
            _topHalfSprite = Sprite.Create(CircleTexture(), new Rect(0, 64, 128, 64), new Vector2(0.5f, 0f));
        return _topHalfSprite;
    }

    /// <summary>カプセル下半分（ドームが下・平らな面が上）</summary>
    private static Sprite BottomHalfSprite()
    {
        if (_bottomHalfSprite == null)
            _bottomHalfSprite = Sprite.Create(CircleTexture(), new Rect(0, 0, 128, 64), new Vector2(0.5f, 1f));
        return _bottomHalfSprite;
    }

    private static GameObject MakeCircle(string name, Transform parent, Color color, float d)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>().sizeDelta = new Vector2(d, d);
        var img = go.AddComponent<Image>();
        img.sprite = CircleSprite();
        img.color = color;
        return go;
    }

    /// <summary>2トーンのカプセル（上=指定色、下=白（bottomColorで変更可）、ツヤ付き）</summary>
    private static GameObject MakeCapsule(string name, Transform parent, Color color, float d,
        Color? bottomColor = null)
    {
        var root = new GameObject(name);
        root.transform.SetParent(parent, false);
        root.AddComponent<RectTransform>().sizeDelta = new Vector2(d, d);

        var bottom = new GameObject("__Bottom");
        bottom.transform.SetParent(root.transform, false);
        var brt = bottom.AddComponent<RectTransform>();
        brt.sizeDelta = new Vector2(d, d / 2f);
        brt.pivot = new Vector2(0.5f, 1f);
        brt.anchoredPosition = new Vector2(0, -1);
        var bimg = bottom.AddComponent<Image>();
        bimg.sprite = BottomHalfSprite();
        bimg.color = bottomColor ?? C_CAP_WHITE;

        var top = new GameObject("__Top");
        top.transform.SetParent(root.transform, false);
        var trt = top.AddComponent<RectTransform>();
        trt.sizeDelta = new Vector2(d, d / 2f);
        trt.pivot = new Vector2(0.5f, 0f);
        trt.anchoredPosition = new Vector2(0, 1);
        var timg = top.AddComponent<Image>();
        timg.sprite = TopHalfSprite();
        timg.color = color;

        var shine = MakeCircle("__Shine", top.transform, new Color(1, 1, 1, 0.5f), d * 0.18f);
        shine.GetComponent<RectTransform>().anchoredPosition = new Vector2(-d * 0.18f, d * 0.08f);
        return root;
    }

    private static GameObject MakeCircleButton(string name, Transform parent, Color bg, string text,
        TMP_FontAsset font, float fontSize, Color textColor, float d, Vector2 pos,
        UnityEngine.Events.UnityAction onClick)
    {
        var go = MakeCircle(name, parent, bg, d);
        go.GetComponent<RectTransform>().anchoredPosition = pos;
        MakeLabel(go.transform, text, font, fontSize, FontStyles.Bold, textColor, d, d, Vector2.zero);
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = go.GetComponent<Image>();
        btn.onClick.AddListener(onClick);
        return go;
    }

    // ================================================================
    // UI部品ヘルパー（DiceRollControllerと同形式）
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
