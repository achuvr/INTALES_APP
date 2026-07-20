using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cysharp.Threading.Tasks;
using Firebase.Firestore;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// お知らせページ（Page_Info）に「アカウント」ボタンを追加する（コード生成）。
/// ゲーム一覧／メニューの2ボタンの上に置き、押すとアカウントモーダルを開く。
///
/// モーダルの内容:
///  ・ユーザー名の表示と「なまえを編集」ボタン
///    （編集は1週間に1回まで。users/{uid}.name_edited_at で判定するので機種変更しても有効）
///  ・ログアウトボタン（誤タップ防止の2度押し確認つき）
///    ログアウトすると端末の user.txt を削除して Start シーンへ戻る（自動ログイン解除）
///
/// HomeSceneInitializer から gameObject.AddComponent&lt;AccountButton&gt;() で追加される（シーン編集不要）。
/// </summary>
public class AccountButton : MonoBehaviour
{
    // ボタン配置（お知らせページ下部。ゲーム一覧/メニュー行 y=350〜480 のすぐ上の行）。
    // 左=アカウント、右=募集（RecruitChatButton）。
    // 大きさと±Xは下の2ボタン（ゲーム一覧/メニュー）と共通にする
    public const float BTN_W = BoardGameListButton.INFO_BTN_W;
    public const float BTN_H = BoardGameListButton.INFO_BTN_H;
    public const float BTN_Y = 496f;
    public const float BTN_X = BoardGameListButton.INFO_BTN_X;

    /// <summary>ユーザー名を再編集できるようになるまでの日数</summary>
    private const int NAME_EDIT_COOLDOWN_DAYS = 7;
    private const int NAME_MAX_LENGTH = 12;

    // 羊皮紙パレット（AccountDeletionController と同系）
    private static readonly Color C_PARCHMENT = new Color(0.99f, 0.95f, 0.84f, 0.98f);
    private static readonly Color C_BORDER    = new Color(0.84f, 0.66f, 0.18f, 1.00f);
    private static readonly Color C_TITLE     = new Color(0.38f, 0.16f, 0.04f, 1.00f);
    private static readonly Color C_MUTED     = new Color(0.52f, 0.38f, 0.22f, 1.00f);
    private static readonly Color C_LOGOUT    = new Color(0.78f, 0.16f, 0.16f, 1.00f);
    private static readonly Color C_LOGOUT_ARMED = new Color(0.55f, 0.08f, 0.08f, 1.00f);
    private static readonly Color C_INPUTBG   = new Color(1.00f, 0.99f, 0.93f, 1.00f);

    private Canvas _canvas;
    private GameObject _modal;

    // モーダル内の表示モード（閲覧⇔名前編集）
    private GameObject _viewRoot;
    private GameObject _editRoot;
    private TextMeshProUGUI _nameValueText;
    private TextMeshProUGUI _editHintText;
    private Button _editButton;
    private TMP_InputField _nameInput;

    // ログアウトの2度押し確認
    private Button _logoutButton;
    private TextMeshProUGUI _logoutLabel;
    private bool _logoutArmed;

    private bool _saving;

    private void Start()
    {
        var canvasGO = GameObject.Find("Canvas");
        _canvas = canvasGO != null ? canvasGO.GetComponent<Canvas>() : FindFirstObjectByType<Canvas>();
        if (_canvas == null) return;
        BuildButton();
        // 管理者判定（モーダル内の「ゲーム画像の管理」ボタン表示用）を先に済ませておく
        AdminClaim.RefreshAsync().Forget();
    }

    // ================================================================
    // 入口ボタン（Page_Info・2ボタン行の上）
    // ================================================================
    private void BuildButton()
    {
        var jp = GetJpFont();
        var ink = GachaController.GRID_TEXT_COLOR;

        var btnGO = new GameObject("__AccountButton");
        btnGO.transform.SetParent(_canvas.transform, false);
        var rt = btnGO.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(-BTN_X, BTN_Y + BTN_H / 2f);
        rt.sizeDelta = new Vector2(BTN_W, BTN_H);
        var bg = btnGO.AddComponent<Image>();
        GachaController.ApplyGridButtonStyle(bg);

        // お知らせページ表示中のみ出す（画面基準で位置を決めてから Page_Info の子へ移動）
        var page = _canvas.transform.Find("Page_Info");
        if (page != null)
        {
            btnGO.transform.SetParent(page, true);
            btnGO.transform.SetAsLastSibling();
        }
        else
        {
            Debug.LogWarning("[Account] Page_Info が見つからないため、Canvas直下にボタンを置きます");
        }

        // 人型アイコン（頭の丸 + 肩の角丸）
        var icon = new GameObject("__PersonIcon");
        icon.transform.SetParent(btnGO.transform, false);
        var irt = icon.AddComponent<RectTransform>();
        irt.sizeDelta = new Vector2(80, 80);
        irt.localScale = Vector3.one * (92f / 80f); // 下段ボタンのアイコン(92px)と同じ見た目サイズ
        irt.anchoredPosition = new Vector2(-130, 0);
        var head = new GameObject("__Head");
        head.transform.SetParent(icon.transform, false);
        var hrt = head.AddComponent<RectTransform>();
        hrt.sizeDelta = new Vector2(30, 30);
        hrt.anchoredPosition = new Vector2(0, 20);
        var himg = head.AddComponent<Image>();
        UICircleSprite.Apply(himg);
        himg.color = ink;
        himg.raycastTarget = false;
        var body = new GameObject("__Body");
        body.transform.SetParent(icon.transform, false);
        var brt = body.AddComponent<RectTransform>();
        brt.sizeDelta = new Vector2(52, 30);
        brt.anchoredPosition = new Vector2(0, -14);
        var bimg = body.AddComponent<Image>();
        RoundedRectSprite.Apply(bimg);
        bimg.color = ink;
        bimg.raycastTarget = false;

        MakeLabel(btnGO.transform, "アカウント", jp, 44, FontStyles.Bold, ink, 250, BTN_H, new Vector2(40, 0));

        var btn = btnGO.AddComponent<Button>();
        btn.targetGraphic = bg;
        btn.onClick.AddListener(ShowModal);
    }

    // ================================================================
    // アカウントモーダル
    // ================================================================
    private void ShowModal()
    {
        CloseModal();
        var jp = GetJpFont();

        _modal = new GameObject("__AccountModal");
        _modal.transform.SetParent(_canvas.transform, false);
        var mrt = _modal.AddComponent<RectTransform>();
        mrt.anchorMin = Vector2.zero; mrt.anchorMax = Vector2.one;
        mrt.offsetMin = mrt.offsetMax = Vector2.zero;
        var dim = _modal.AddComponent<Image>();
        dim.color = UITheme.DIM;
        var dimBtn = _modal.AddComponent<Button>();
        dimBtn.transition = Selectable.Transition.None;
        dimBtn.onClick.AddListener(CloseModal);

        var border = MakeRect("__Border", _modal.transform, C_BORDER, 860, 900);
        RoundedRectSprite.Apply(border.GetComponent<Image>());
        UITheme.ElevateCard(border, 18f, 10f, 0.35f); // モーダルを浮かせる
        var panel = MakeRect("__Panel", border.transform, C_PARCHMENT, 836, 876);
        RoundedRectSprite.Apply(panel.GetComponent<Image>());
        panel.AddComponent<Button>().transition = Selectable.Transition.None; // パネル内タップで閉じない

        MakeLabel(panel.transform, "アカウント", jp, 46, FontStyles.Bold, C_TITLE,
            700, 80, new Vector2(0, 370));
        MakeLabel(panel.transform, "ユーザー名", jp, 30, FontStyles.Bold, C_MUTED,
            700, 44, new Vector2(0, 280));

        // ---- 閲覧モード（名前表示＋編集ボタン）----
        _viewRoot = new GameObject("__ViewMode");
        _viewRoot.transform.SetParent(panel.transform, false);
        var vrt = _viewRoot.AddComponent<RectTransform>();
        vrt.sizeDelta = Vector2.zero;

        _nameValueText = MakeLabel(_viewRoot.transform, "", jp, 46, FontStyles.Bold, C_TITLE,
            760, 80, new Vector2(0, 210)).GetComponent<TextMeshProUGUI>();
        _nameValueText.enableAutoSizing = true;
        _nameValueText.fontSizeMax = 46;
        _nameValueText.fontSizeMin = 26;

        var editBtn = MakeButton(_viewRoot.transform, C_BORDER, "なまえを編集", jp, 34, C_TITLE,
            400, 96, new Vector2(0, 100), () => SwitchToEditMode());
        _editButton = editBtn.GetComponent<Button>();
        _editHintText = MakeLabel(_viewRoot.transform, "", jp, 26, FontStyles.Normal, C_MUTED,
            760, 40, new Vector2(0, 30)).GetComponent<TextMeshProUGUI>();

        // ---- 編集モード（入力欄＋決定/やめる）----
        _editRoot = new GameObject("__EditMode");
        _editRoot.transform.SetParent(panel.transform, false);
        var ert = _editRoot.AddComponent<RectTransform>();
        ert.sizeDelta = Vector2.zero;
        BuildNameInput(_editRoot.transform, jp, new Vector2(0, 210));
        MakeButton(_editRoot.transform, C_BORDER, "決定", jp, 34, C_TITLE,
            300, 96, new Vector2(-165, 95), () => SaveNameAsync().Forget());
        MakeButton(_editRoot.transform, new Color(1f, 1f, 1f, 0.95f), "やめる", jp, 30, C_MUTED,
            300, 96, new Vector2(165, 95), SwitchToViewMode);

        // ---- 管理者専用: ゲーム画像の管理・バフデッキ作成・イベントガチャQR（店側アカウントにだけ表示） ----
        // 表示の出し分けは AdminClaim（IDトークンのadminクレーム）。実際の書き込み保護は
        // Firestore/Storage ルール側なので、この判定を偽装されても登録はできない
        if (AdminClaim.IsAdmin)
        {
            var imgBtn = MakeButton(panel.transform, C_BORDER, "ゲーム画像の管理", jp, 26, C_TITLE,
                250, 60, new Vector2(-270, -25), OpenGameImageAdmin);
            ShrinkLabelToFit(imgBtn, 26);
            var deckBtn = MakeButton(panel.transform, C_BORDER, "バフデッキ作成", jp, 26, C_TITLE,
                250, 60, new Vector2(0, -25), OpenDeckBuilder);
            ShrinkLabelToFit(deckBtn, 26);
            var qrBtn = MakeButton(panel.transform, C_BORDER, "イベントガチャQR", jp, 26, C_TITLE,
                250, 60, new Vector2(270, -25), OpenEventQr);
            ShrinkLabelToFit(qrBtn, 26);
        }

        // ---- ログアウト＆アカウント削除＆とじる ----
        var logoutBtn = MakeButton(panel.transform, C_LOGOUT, "ログアウト", jp, 34, Color.white,
            400, 100, new Vector2(0, -120), OnLogoutPressed);
        _logoutButton = logoutBtn.GetComponent<Button>();
        _logoutLabel = logoutBtn.GetComponentInChildren<TextMeshProUGUI>();
        MakeLabel(panel.transform, "ログアウトすると自動ログインが解除されます", jp, 24, FontStyles.Normal, C_MUTED,
            760, 36, new Vector2(0, -190));

        // アカウント削除（削除フロー本体は AccountDeletionController）
        var deleteGO = MakeRect("__DeleteAccountLink", panel.transform, new Color(0, 0, 0, 0f), 360, 60);
        deleteGO.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, -260);
        var deleteLabel = MakeLabel(deleteGO.transform, "アカウント削除", jp, 32, FontStyles.Underline,
            new Color(0.75f, 0.30f, 0.30f, 0.9f), 360, 60, Vector2.zero);
        deleteLabel.GetComponent<TextMeshProUGUI>().raycastTarget = true;
        var deleteBtn = deleteGO.AddComponent<Button>();
        deleteBtn.transition = Selectable.Transition.None;
        deleteBtn.onClick.AddListener(() =>
        {
            CloseModal();
            var deletion = GetComponent<AccountDeletionController>();
            if (deletion != null) deletion.OpenDeletionFlow();
            else Debug.LogWarning("[Account] AccountDeletionController が見つかりません");
        });

        MakeButton(panel.transform, new Color(1f, 1f, 1f, 0.95f), "とじる", jp, 30, C_TITLE,
            360, 84, new Vector2(0, -360), CloseModal);

        _logoutArmed = false;
        SwitchToViewMode();
    }

    private void CloseModal()
    {
        if (_modal != null) Destroy(_modal);
        _modal = null;
        _viewRoot = null;
        _editRoot = null;
        _nameValueText = null;
        _editHintText = null;
        _editButton = null;
        _nameInput = null;
        _logoutButton = null;
        _logoutLabel = null;
        _logoutArmed = false;
    }

    /// <summary>閲覧モードへ（名前と編集可否の表示を最新化する）</summary>
    private void SwitchToViewMode()
    {
        if (_viewRoot == null) return;
        _viewRoot.SetActive(true);
        _editRoot.SetActive(false);

        var data = UserDataManager.instance != null ? UserDataManager.instance.UserData : null;
        _nameValueText.text = string.IsNullOrEmpty(data?.Username) ? "（未設定）" : data.Username;

        // 編集は1週間に1回まで
        var nextEditableAt = NextEditableAtUtc(data);
        bool canEdit = System.DateTime.UtcNow >= nextEditableAt;
        _editButton.interactable = canEdit;
        if (canEdit)
        {
            _editHintText.text = $"※なまえの変更は{NAME_EDIT_COOLDOWN_DAYS}日に1回までです";
        }
        else
        {
            int daysLeft = Mathf.Max(1, Mathf.CeilToInt(
                (float)(nextEditableAt - System.DateTime.UtcNow).TotalDays));
            _editHintText.text = $"なまえは変更したばかりです（あと{daysLeft}日で編集できます）";
        }
    }

    /// <summary>次に名前を編集できるUTC日時（未編集なら過去日時=すぐ編集可）</summary>
    private static System.DateTime NextEditableAtUtc(UserData data)
    {
        if (data == null) return System.DateTime.MinValue;
        return data.NameEditedAt.ToDateTime().AddDays(NAME_EDIT_COOLDOWN_DAYS);
    }

    private void SwitchToEditMode()
    {
        if (_editRoot == null) return;
        _viewRoot.SetActive(false);
        _editRoot.SetActive(true);
        var data = UserDataManager.instance != null ? UserDataManager.instance.UserData : null;
        _nameInput.text = data?.Username ?? "";
    }

    /// <summary>入力された名前を保存する（name と name_edited_at を1回の書き込みで更新）</summary>
    private async UniTask SaveNameAsync()
    {
        if (_saving || _nameInput == null) return;

        string newName = (_nameInput.text ?? "").Trim();
        if (string.IsNullOrEmpty(newName))
        {
            FriendMenuController.ShowToast("ユーザー名を入力してください");
            return;
        }
        if (newName.Length > NAME_MAX_LENGTH)
        {
            FriendMenuController.ShowToast($"ユーザー名は{NAME_MAX_LENGTH}文字以内で入力してください");
            return;
        }

        var manager = UserDataManager.instance;
        // 二重チェック（ボタン無効化をすり抜けた場合の保険）
        if (System.DateTime.UtcNow < NextEditableAtUtc(manager.UserData))
        {
            FriendMenuController.ShowToast("なまえは変更したばかりです");
            SwitchToViewMode();
            return;
        }

        _saving = true;
        if (AssetsDatabase.instance != null) AssetsDatabase.instance.LoadingPanel.SetActive(true);
        try
        {
            var now = Timestamp.GetCurrentTimestamp();
            await FirebaseFirestore.DefaultInstance
                .Collection("users").Document(manager.UID)
                .UpdateAsync(new Dictionary<string, object>
                {
                    { "name", newName },
                    { "name_edited_at", now },
                }).AsUniTask();

            manager.UserData.Username = newName;
            manager.UserData.NameEditedAt = now;
            FriendMenuController.ShowToast("ユーザー名を変更しました");
            SwitchToViewMode();
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[Account] ユーザー名変更エラー: {ex.Message}");
            FriendMenuController.ShowToast("通信に失敗しました\nもう一度お試しください");
        }
        finally
        {
            _saving = false;
            if (AssetsDatabase.instance != null) AssetsDatabase.instance.LoadingPanel.SetActive(false);
        }
    }

    /// <summary>ゲーム画像管理シーンを開く（Zukanと同じ加算マージ方式）。</summary>
    private void OpenGameImageAdmin()
    {
        CloseModal();
        if (FindFirstObjectByType<GameImageAdminController>() != null) return; // 既に開いている
        if (SceneLoader.instance == null)
            new GameObject("SceneLoader").AddComponent<SceneLoader>();
        SceneLoader.instance.MergeScene("GameImageAdmin");
    }

    /// <summary>
    /// バフカード山札ビルダーを開く（GachaAdmin と同じ全画面LoadScene方式。
    /// シーン専用のCanvasを持つためマージせず、右上の「✕ Home」でHomeへ戻る）。
    /// </summary>
    private void OpenDeckBuilder()
    {
        CloseModal();
        SceneManager.LoadScene("BuffCardDeckBuilder");
    }

    /// <summary>イベントガチャQRの表示モーダルを開く（プール一覧→選択でQR表示。シーン遷移なし）。</summary>
    private void OpenEventQr()
    {
        CloseModal();
        EventGachaQrViewer.Show();
    }

    /// <summary>ボタンのラベルを枠内に収まるよう自動縮小させる（管理者ボタンなど文字数が多いもの用）。</summary>
    private static void ShrinkLabelToFit(GameObject buttonGO, float maxFontSize)
    {
        var label = buttonGO.GetComponentInChildren<TextMeshProUGUI>();
        if (label == null) return;
        label.enableAutoSizing = true;
        label.fontSizeMax = maxFontSize;
        label.fontSizeMin = 18;
        label.enableWordWrapping = false;
        label.overflowMode = TextOverflowModes.Ellipsis;
    }

    // ================================================================
    // ログアウト（誤タップ防止のため2度押しで実行）
    // ================================================================
    private void OnLogoutPressed()
    {
        if (!_logoutArmed)
        {
            _logoutArmed = true;
            _logoutLabel.text = "もう一度押すとログアウト";
            _logoutButton.image.color = C_LOGOUT_ARMED;
            return;
        }
        Logout();
    }

    /// <summary>
    /// ログアウト: 端末の自動ログイン情報（user.txt）を削除し、
    /// Firebase Auth からサインアウトして Start シーンへ戻る。
    /// </summary>
    private void Logout()
    {
        try
        {
#if UNITY_EDITOR
            string path = Path.Combine(Application.dataPath, "TestUser", "user.txt");
#else
            string path = Path.Combine(Application.persistentDataPath, "TestUser", "user.txt");
#endif
            if (File.Exists(path)) File.Delete(path);
            Debug.Log("[Account] user.txt を削除しました（自動ログイン解除）");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[Account] user.txt 削除エラー: {ex.Message}");
        }

        try { Firebase.Auth.FirebaseAuth.DefaultInstance.SignOut(); }
        catch (System.Exception ex) { Debug.LogWarning($"[Account] サインアウトエラー: {ex.Message}"); }

        SceneManager.LoadScene("Start");
    }

    // ================================================================
    // UI部品ヘルパー
    // ================================================================

    /// <summary>名前入力欄（BoardGameListControllerの検索ボックスと同じ構成）</summary>
    private void BuildNameInput(Transform parent, TMP_FontAsset jp, Vector2 pos)
    {
        var barGO = new GameObject("__NameInput");
        barGO.transform.SetParent(parent, false);
        var brt = barGO.AddComponent<RectTransform>();
        brt.sizeDelta = new Vector2(640, 96);
        brt.anchoredPosition = pos;
        var bimg = barGO.AddComponent<Image>();
        RoundedRectSprite.Apply(bimg);
        bimg.color = C_INPUTBG;

        var areaGO = new GameObject("__TextArea");
        areaGO.transform.SetParent(barGO.transform, false);
        var art = areaGO.AddComponent<RectTransform>();
        art.anchorMin = Vector2.zero; art.anchorMax = Vector2.one;
        art.offsetMin = new Vector2(28f, 6f); art.offsetMax = new Vector2(-28f, -6f);
        areaGO.AddComponent<RectMask2D>();

        var placeholder = new GameObject("__Placeholder");
        placeholder.transform.SetParent(areaGO.transform, false);
        var prt = placeholder.AddComponent<RectTransform>();
        prt.anchorMin = Vector2.zero; prt.anchorMax = Vector2.one;
        prt.offsetMin = prt.offsetMax = Vector2.zero;
        var ptmp = placeholder.AddComponent<TextMeshProUGUI>();
        if (jp != null) ptmp.font = jp;
        ptmp.text = "あたらしい なまえ";
        ptmp.fontSize = 34; ptmp.color = C_MUTED; ptmp.fontStyle = FontStyles.Italic;
        ptmp.alignment = TextAlignmentOptions.Left;

        var textGO = new GameObject("__Text");
        textGO.transform.SetParent(areaGO.transform, false);
        var txrt = textGO.AddComponent<RectTransform>();
        txrt.anchorMin = Vector2.zero; txrt.anchorMax = Vector2.one;
        txrt.offsetMin = txrt.offsetMax = Vector2.zero;
        var ttmp = textGO.AddComponent<TextMeshProUGUI>();
        if (jp != null) ttmp.font = jp;
        ttmp.fontSize = 34; ttmp.color = C_TITLE;
        ttmp.alignment = TextAlignmentOptions.Left;

        _nameInput = barGO.AddComponent<TMP_InputField>();
        _nameInput.textViewport = art;
        _nameInput.textComponent = ttmp;
        _nameInput.placeholder = ptmp;
        _nameInput.fontAsset = jp;
        _nameInput.pointSize = 34;
        _nameInput.characterLimit = NAME_MAX_LENGTH;
        _nameInput.customCaretColor = true;
        _nameInput.caretColor = C_TITLE;
        _nameInput.selectionColor = new Color(0.84f, 0.66f, 0.18f, 0.4f);
        _nameInput.lineType = TMP_InputField.LineType.SingleLine;
    }

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

    private static GameObject MakeButton(Transform parent, Color bg, string text, TMP_FontAsset font,
        float fontSize, Color textColor, float w, float h, Vector2 pos,
        UnityEngine.Events.UnityAction onClick)
    {
        var go = MakeRect("__Btn_" + text, parent, bg, w, h);
        RoundedRectSprite.Apply(go.GetComponent<Image>());
        go.GetComponent<Image>().color = bg;
        go.GetComponent<RectTransform>().anchoredPosition = pos;
        MakeLabel(go.transform, text, font, fontSize, FontStyles.Bold, textColor, w, h, Vector2.zero);
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = go.GetComponent<Image>();
        btn.onClick.AddListener(onClick);
        // デザイン基盤: 明るい面は白グラデ、濃い面は控えめグラデで磨く（透明ヒットエリアは除外）
        if (bg.a >= 0.5f)
        {
            if (bg.r + bg.g + bg.b >= 2.4f) UITheme.PolishButton(go.GetComponent<Image>());
            else UITheme.PolishDarkButton(go.GetComponent<Image>());
        }
        return go;
    }
}
