using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using Firebase.Firestore;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 紙の会員証 → アプリ引き継ぎ登録ツール（店側・CardTransferシーン）。
///
/// 使い方:
///   1. 店側がこのシーンで紙の会員証の内容（名前・職業・属性・レベル・クーポン枚数）を入力して登録
///   2. transfers/{引き継ぎコード} に保存され、QRコードが表示される
///   3. お客さんがアプリのQRカメラでそのQRを読むと、自分のアカウントに
///      キャラクターとして追加され、コードは使用済みになる（CallMethodFromQR.ClaimTransfer）
///
/// お客さん自身に入力させず店側が登録する方式なので、レベルの自己申告詐称を防げる。
/// </summary>
public class CardTransferManager : MonoBehaviour
{
    private static readonly (string label, string key)[] JOBS =
    {
        ("戦士", "warrior"), ("魔法使い", "magician"), ("弓使い", "archer"), ("銃使い", "gunner"),
    };
    private static readonly (string label, string key)[] ELEMENTS =
    {
        ("炎", "fire"), ("水", "water"), ("自然", "nature"), ("雷", "thunder"),
    };

    // 紛らわしい文字(0/O, 1/I/L)を除いた引き継ぎコード用文字セット
    private const string CODE_CHARS = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";
    private const int CODE_LENGTH = 8;

    private static readonly Color C_BG       = new Color(0.10f, 0.08f, 0.06f);
    private static readonly Color C_PANEL    = new Color(0.18f, 0.14f, 0.10f);
    private static readonly Color C_GOLD     = new Color(0.92f, 0.72f, 0.22f);
    private static readonly Color C_TEXT     = new Color(0.95f, 0.90f, 0.78f);
    private static readonly Color C_MUTED    = new Color(0.55f, 0.48f, 0.38f);
    private static readonly Color C_SELECTED = new Color(0.84f, 0.66f, 0.18f);
    private static readonly Color C_UNSEL    = new Color(0.32f, 0.28f, 0.22f);
    private static readonly Color C_REGISTER = new Color(0.15f, 0.45f, 0.20f);
    private static readonly Color C_LIST     = new Color(0.20f, 0.35f, 0.55f);
    private static readonly Color C_BACK     = new Color(0.45f, 0.20f, 0.20f);
    private static readonly Color C_OK       = new Color(0.28f, 0.72f, 0.28f);
    private static readonly Color C_ERR      = new Color(0.85f, 0.25f, 0.25f);

    private TMP_FontAsset _jp;
    private Canvas _canvas;
    private TMP_InputField _nameInput;
    private TMP_InputField _levelInput;
    private TMP_InputField _fiveCouponInput;
    private TMP_InputField _sevenCouponInput;
    private TMP_InputField _drinkCouponInput;
    private int _selectedJob;
    private int _selectedElement;
    private GameObject[] _jobBtns;
    private GameObject[] _elBtns;
    private TextMeshProUGUI _status;
    private GameObject _resultPanel;
    private RawImage _qrImage;
    private TextMeshProUGUI _codeText;
    private TextMeshProUGUI _resultInfo;
    private Texture2D _qrTex;
    private GameObject _listPanel;
    private TextMeshProUGUI _listText;
    private bool _busy;

    private void Start()
    {
#if UNITY_EDITOR
        _jp = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Fonts/jp.asset");
        if (_jp == null)
            _jp = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Fonts 1/jp.asset");
#endif
        if (_jp == null)
        {
            var fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
            _jp = fonts.FirstOrDefault(f => f.name.ToLower() == "jp") ?? fonts.FirstOrDefault();
        }

        BuildUI();
    }

    private void OnDestroy()
    {
        if (_qrTex != null) Destroy(_qrTex);
    }

    // ================================================================
    // 登録処理
    // ================================================================
    private async UniTask RegisterAsync()
    {
        if (_busy) return;

        string charaName = _nameInput.text.Trim();
        if (string.IsNullOrEmpty(charaName))
        {
            SetStatus("名前を入力してください", C_ERR);
            return;
        }
        if (!int.TryParse(_levelInput.text.Trim(), out int level) || level < 1)
        {
            SetStatus("レベルは1以上の数字で入力してください", C_ERR);
            return;
        }

        int fiveCoupon  = ParseCount(_fiveCouponInput.text);
        int sevenCoupon = ParseCount(_sevenCouponInput.text);
        int drinkCoupon = ParseCount(_drinkCouponInput.text);

        _busy = true;
        SetStatus("登録中...", C_MUTED);

        try
        {
            var db = FirebaseFirestore.DefaultInstance;
            string code = await GenerateUniqueCodeAsync(db);

            var data = new Dictionary<string, object>
            {
                { "name", charaName },
                { "job", JOBS[_selectedJob].key },
                { "el", ELEMENTS[_selectedElement].key },
                { "lv", level },
                { "five_coupon", fiveCoupon },
                { "seven_coupon", sevenCoupon },
                { "drink_coupon", drinkCoupon },
                { "claimed", false },
                { "created_at", Timestamp.GetCurrentTimestamp() },
            };
            await db.Collection("transfers").Document(code).SetAsync(data).AsUniTask();

            SetStatus($"発行完了: {code}", C_OK);
            ShowResultPanel(code, charaName, JOBS[_selectedJob].label, ELEMENTS[_selectedElement].label, level,
                fiveCoupon, sevenCoupon, drinkCoupon);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[CardTransfer] 登録エラー: {ex}");
            SetStatus($"エラー: {ex.Message}", C_ERR);
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>クーポン枚数入力のパース（空欄・不正値は0扱い）</summary>
    private static int ParseCount(string text)
    {
        return int.TryParse((text ?? "").Trim(), out int n) && n > 0 ? n : 0;
    }

    /// <summary>未使用の引き継ぎコードを生成する（衝突したら作り直し）</summary>
    private async UniTask<string> GenerateUniqueCodeAsync(FirebaseFirestore db)
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            var sb = new System.Text.StringBuilder(CODE_LENGTH);
            for (int i = 0; i < CODE_LENGTH; i++)
                sb.Append(CODE_CHARS[UnityEngine.Random.Range(0, CODE_CHARS.Length)]);
            string code = sb.ToString();

            var snap = await db.Collection("transfers").Document(code).GetSnapshotAsync().AsUniTask();
            if (!snap.Exists) return code;
        }
        throw new Exception("コード生成に失敗しました（衝突が多すぎます）");
    }

    // ================================================================
    // 発行済み一覧
    // ================================================================
    private async UniTask ShowListAsync()
    {
        if (_busy) return;
        _busy = true;
        SetStatus("一覧を取得中...", C_MUTED);

        try
        {
            var db = FirebaseFirestore.DefaultInstance;
            var snap = await db.Collection("transfers")
                .OrderByDescending("created_at")
                .Limit(30)
                .GetSnapshotAsync().AsUniTask();

            var sb = new System.Text.StringBuilder();
            foreach (var doc in snap.Documents)
            {
                string name = doc.TryGetValue("name", out string n) ? n : "?";
                int lv = doc.TryGetValue("lv", out int l) ? l : 0;
                bool claimed = doc.TryGetValue("claimed", out bool c) && c;
                string mark = claimed ? "<color=#55BB55>済</color>" : "<color=#DDAA33>未</color>";
                sb.AppendLine($"[{mark}] {doc.Id}  {name}  Lv{lv}");
            }
            if (snap.Count == 0) sb.AppendLine("発行済みの引き継ぎコードはありません");

            _listText.text = sb.ToString();
            _listPanel.SetActive(true);
            SetStatus($"直近{snap.Count}件を表示中", C_OK);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[CardTransfer] 一覧取得エラー: {ex}");
            SetStatus($"エラー: {ex.Message}", C_ERR);
        }
        finally
        {
            _busy = false;
        }
    }

    private void SetStatus(string msg, Color color)
    {
        if (_status != null) { _status.text = msg; _status.color = color; }
    }

    // ================================================================
    // UI構築
    // ================================================================
    private void BuildUI()
    {
        // Canvas + EventSystem
        var cGO = new GameObject("Canvas");
        _canvas = cGO.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = cGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        cGO.AddComponent<GraphicRaycaster>();

        var sys = new GameObject("EventSystem");
        sys.AddComponent<UnityEngine.EventSystems.EventSystem>();
        sys.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();

        // 背景
        var bg = MakeRect("__BG", _canvas.transform, C_BG, 0, 0);
        var bgrt = bg.GetComponent<RectTransform>();
        bgrt.anchorMin = Vector2.zero; bgrt.anchorMax = Vector2.one;
        bgrt.offsetMin = bgrt.offsetMax = Vector2.zero;

        MakeLabel("__Title", _canvas.transform, "紙の会員証 → アプリ引き継ぎ", 52, FontStyles.Bold, C_GOLD,
            900, 80, new Vector2(0, 870));

        _status = MakeLabel("__Status", _canvas.transform, "会員証の内容を入力してください", 32, FontStyles.Normal, C_MUTED,
            960, 60, new Vector2(0, 800)).GetComponent<TextMeshProUGUI>();

        // 名前
        MakeLabel("__NameLabel", _canvas.transform, "キャラクター名", 36, FontStyles.Bold, C_TEXT,
            400, 50, new Vector2(-280, 730));
        _nameInput = MakeInput(_canvas.transform, new Vector2(0, 662), new Vector2(800, 86),
            "例: ゆうしゃアチュ", TMP_InputField.ContentType.Standard);

        // レベル
        MakeLabel("__LvLabel", _canvas.transform, "レベル", 36, FontStyles.Bold, C_TEXT,
            400, 50, new Vector2(-280, 580));
        _levelInput = MakeInput(_canvas.transform, new Vector2(-200, 512), new Vector2(400, 86),
            "例: 42", TMP_InputField.ContentType.IntegerNumber);

        // 職業
        MakeLabel("__JobLabel", _canvas.transform, "職業", 36, FontStyles.Bold, C_TEXT,
            400, 50, new Vector2(-280, 430));
        _jobBtns = MakeChoiceRow(JOBS, 362, i =>
        {
            _selectedJob = i;
            RefreshChoiceRow(_jobBtns, _selectedJob);
        });

        // 属性
        MakeLabel("__ElLabel", _canvas.transform, "属性", 36, FontStyles.Bold, C_TEXT,
            400, 50, new Vector2(-280, 285));
        _elBtns = MakeChoiceRow(ELEMENTS, 217, i =>
        {
            _selectedElement = i;
            RefreshChoiceRow(_elBtns, _selectedElement);
        });

        // 引き継ぐクーポン（枚数。空欄は0扱い）
        MakeLabel("__CouponLabel", _canvas.transform, "引き継ぐクーポン（枚数）", 36, FontStyles.Bold, C_TEXT,
            600, 50, new Vector2(-180, 140));
        MakeLabel("__FiveLabel", _canvas.transform, "5%OFF", 28, FontStyles.Normal, C_MUTED,
            280, 40, new Vector2(-330, 88));
        MakeLabel("__SevenLabel", _canvas.transform, "7%OFF", 28, FontStyles.Normal, C_MUTED,
            280, 40, new Vector2(0, 88));
        MakeLabel("__DrinkLabel", _canvas.transform, "ドリンク", 28, FontStyles.Normal, C_MUTED,
            280, 40, new Vector2(330, 88));
        _fiveCouponInput  = MakeInput(_canvas.transform, new Vector2(-330, 22), new Vector2(280, 80),
            "0", TMP_InputField.ContentType.IntegerNumber);
        _sevenCouponInput = MakeInput(_canvas.transform, new Vector2(0, 22), new Vector2(280, 80),
            "0", TMP_InputField.ContentType.IntegerNumber);
        _drinkCouponInput = MakeInput(_canvas.transform, new Vector2(330, 22), new Vector2(280, 80),
            "0", TMP_InputField.ContentType.IntegerNumber);

        // 操作ボタン
        MakeButton("__Register", _canvas.transform, C_REGISTER, "登録してQRコード発行", 44, Color.white,
            720, 120, new Vector2(0, -160), () => RegisterAsync().Forget());
        MakeButton("__List", _canvas.transform, C_LIST, "発行済み一覧", 36, Color.white,
            400, 100, new Vector2(-220, -320), () => ShowListAsync().Forget());
        MakeButton("__Back", _canvas.transform, C_BACK, "ホームに戻る", 36, Color.white,
            400, 100, new Vector2(220, -320), () => SceneManager.LoadScene("Home"));

        BuildResultPanel();
        BuildListPanel();
    }

    private GameObject[] MakeChoiceRow((string label, string key)[] items, float y, Action<int> onSelect)
    {
        var btns = new GameObject[items.Length];
        const float W = 230, GAP = 12;
        float totalW = items.Length * W + (items.Length - 1) * GAP;
        for (int i = 0; i < items.Length; i++)
        {
            int idx = i;
            float x = -totalW / 2f + W / 2f + i * (W + GAP);
            btns[i] = MakeButton($"__Choice_{items[i].key}", _canvas.transform,
                i == 0 ? C_SELECTED : C_UNSEL, items[i].label, 34,
                i == 0 ? Color.black : C_TEXT, W, 90, new Vector2(x, y), () => onSelect(idx));
        }
        return btns;
    }

    private void RefreshChoiceRow(GameObject[] btns, int selected)
    {
        for (int i = 0; i < btns.Length; i++)
        {
            btns[i].GetComponent<Image>().color = i == selected ? C_SELECTED : C_UNSEL;
            btns[i].GetComponentInChildren<TextMeshProUGUI>().color = i == selected ? Color.black : C_TEXT;
        }
    }

    // ================================================================
    // 発行結果（QR）パネル
    // ================================================================
    private void BuildResultPanel()
    {
        _resultPanel = MakeRect("__ResultPanel", _canvas.transform, new Color(0, 0, 0, 0.85f), 0, 0);
        var rt = _resultPanel.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;

        var panel = MakeRect("__Inner", _resultPanel.transform, C_PANEL, 920, 1300);

        MakeLabel("__RTitle", panel.transform, "引き継ぎQRコード", 48, FontStyles.Bold, C_GOLD,
            700, 80, new Vector2(0, 560));

        _resultInfo = MakeLabel("__RInfo", panel.transform, "", 36, FontStyles.Normal, C_TEXT,
            800, 80, new Vector2(0, 470)).GetComponent<TextMeshProUGUI>();

        var qrBg = MakeRect("__QRBg", panel.transform, Color.white, 660, 660);
        qrBg.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, 60);
        var qrGO = new GameObject("__QR");
        qrGO.transform.SetParent(qrBg.transform, false);
        qrGO.AddComponent<RectTransform>().sizeDelta = new Vector2(600, 600);
        _qrImage = qrGO.AddComponent<RawImage>();

        _codeText = MakeLabel("__Code", panel.transform, "", 56, FontStyles.Bold, C_GOLD,
            800, 80, new Vector2(0, -340)).GetComponent<TextMeshProUGUI>();

        MakeLabel("__RHint", panel.transform, "お客さんのアプリのQRカメラで\n読み取ってもらってください", 32, FontStyles.Normal, C_MUTED,
            800, 100, new Vector2(0, -440));

        MakeButton("__RClose", panel.transform, C_BACK, "閉じる", 38, Color.white,
            360, 100, new Vector2(0, -570), () => _resultPanel.SetActive(false));

        _resultPanel.SetActive(false);
    }

    private void ShowResultPanel(string code, string name, string jobLabel, string elLabel, int level,
        int fiveCoupon, int sevenCoupon, int drinkCoupon)
    {
        if (_qrTex != null) Destroy(_qrTex);
        _qrTex = QRCodeHelper.CreateQRCode($"{CallMethodFromQR.TRANSFER_QR_PREFIX}{code}", 512, 512);
        _qrTex.filterMode = FilterMode.Point;
        _qrImage.texture = _qrTex;
        _codeText.text = code;

        string info = $"{name}（{jobLabel}・{elLabel}・Lv{level}）";
        var coupons = new List<string>();
        if (fiveCoupon > 0)  coupons.Add($"5%×{fiveCoupon}");
        if (sevenCoupon > 0) coupons.Add($"7%×{sevenCoupon}");
        if (drinkCoupon > 0) coupons.Add($"ドリンク×{drinkCoupon}");
        if (coupons.Count > 0) info += $"\nクーポン: {string.Join(" / ", coupons)}";
        _resultInfo.text = info;

        _resultPanel.SetActive(true);
    }

    // ================================================================
    // 一覧パネル
    // ================================================================
    private void BuildListPanel()
    {
        _listPanel = MakeRect("__ListPanel", _canvas.transform, new Color(0, 0, 0, 0.85f), 0, 0);
        var rt = _listPanel.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;

        var panel = MakeRect("__Inner", _listPanel.transform, C_PANEL, 960, 1400);

        MakeLabel("__LTitle", panel.transform, "発行済み引き継ぎコード（直近30件）", 40, FontStyles.Bold, C_GOLD,
            900, 70, new Vector2(0, 620));

        _listText = MakeLabel("__LText", panel.transform, "", 30, FontStyles.Normal, C_TEXT,
            880, 1150, new Vector2(0, -30)).GetComponent<TextMeshProUGUI>();
        _listText.alignment = TextAlignmentOptions.TopLeft;

        MakeButton("__LClose", panel.transform, C_BACK, "閉じる", 38, Color.white,
            360, 100, new Vector2(0, -630), () => _listPanel.SetActive(false));

        _listPanel.SetActive(false);
    }

    // ================================================================
    // UI部品ヘルパー
    // ================================================================
    private GameObject MakeRect(string name, Transform parent, Color color, float w, float h)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>().sizeDelta = new Vector2(w, h);
        go.AddComponent<Image>().color = color;
        return go;
    }

    private GameObject MakeLabel(string name, Transform parent, string text, float size,
        FontStyles style, Color color, float w, float h, Vector2 pos)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(w, h);
        rt.anchoredPosition = pos;
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

    private GameObject MakeButton(string name, Transform parent, Color bg, string text, float fontSize,
        Color textColor, float w, float h, Vector2 pos, Action onClick)
    {
        var go = MakeRect(name, parent, bg, w, h);
        go.GetComponent<RectTransform>().anchoredPosition = pos;
        var label = MakeLabel("__Label", go.transform, text, fontSize, FontStyles.Bold, textColor, w, h, Vector2.zero);
        label.GetComponent<TextMeshProUGUI>().alignment = TextAlignmentOptions.Center;
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = go.GetComponent<Image>();
        btn.onClick.AddListener(() => onClick());
        return go;
    }

    private TMP_InputField MakeInput(Transform parent, Vector2 pos, Vector2 size,
        string placeholder, TMP_InputField.ContentType contentType)
    {
        var go = MakeRect("__Input", parent, new Color(0.95f, 0.93f, 0.86f), size.x, size.y);
        go.GetComponent<RectTransform>().anchoredPosition = pos;

        var input = go.AddComponent<TMP_InputField>();
        input.targetGraphic = go.GetComponent<Image>();

        var area = new GameObject("TextArea");
        area.transform.SetParent(go.transform, false);
        var art = area.AddComponent<RectTransform>();
        art.anchorMin = Vector2.zero; art.anchorMax = Vector2.one;
        art.offsetMin = new Vector2(24, 10);
        art.offsetMax = new Vector2(-24, -10);
        area.AddComponent<RectMask2D>();

        var textGO = new GameObject("Text");
        textGO.transform.SetParent(area.transform, false);
        var trt = textGO.AddComponent<RectTransform>();
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.offsetMin = trt.offsetMax = Vector2.zero;
        var text = textGO.AddComponent<TextMeshProUGUI>();
        if (_jp != null) text.font = _jp;
        text.fontSize = 40;
        text.color = Color.black;
        text.alignment = TextAlignmentOptions.MidlineLeft;

        var phGO = new GameObject("Placeholder");
        phGO.transform.SetParent(area.transform, false);
        var prt = phGO.AddComponent<RectTransform>();
        prt.anchorMin = Vector2.zero; prt.anchorMax = Vector2.one;
        prt.offsetMin = prt.offsetMax = Vector2.zero;
        var ph = phGO.AddComponent<TextMeshProUGUI>();
        if (_jp != null) ph.font = _jp;
        ph.text = placeholder;
        ph.fontSize = 40;
        ph.fontStyle = FontStyles.Italic;
        ph.color = new Color(0.4f, 0.4f, 0.4f, 0.7f);
        ph.alignment = TextAlignmentOptions.MidlineLeft;

        input.textViewport = art;
        input.textComponent = text;
        input.placeholder = ph;
        if (_jp != null) input.fontAsset = _jp;
        input.contentType = contentType;

        return input;
    }
}
