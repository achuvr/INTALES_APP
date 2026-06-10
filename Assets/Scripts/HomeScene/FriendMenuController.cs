using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using Firebase.Firestore;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// フレンド機能のUI（コード生成）。
///
/// - ホーム画面右上の「フレンド」ボタンから開く
/// - フレンド一覧の表示（ログイン時に取得済みの UserData.Friends を使うので読み取りゼロ）
/// - 「自分のQRを表示」… "friend:{uid}:{name}" のQRを生成して表示。
///   表示中は自分の users ドキュメントを Listen し、相手が読み取った瞬間に
///   こちらの画面でも「○○さんとフレンドになりました！」と出す
/// - 読み取り側は既存のQRカメラ（QRReader → CallMethodFromQR.AddFriend）を使う
/// </summary>
public class FriendMenuController : MonoBehaviour
{
    private Canvas _canvas;
    private GameObject _overlay;
    private GameObject _listPanel;
    private GameObject _qrPanel;
    private Transform _listContent;
    private RawImage _qrImage;
    private Texture2D _qrTexture;
    private ListenerRegistration _listener;
    private GameObject _modal; // アクションメニュー／確認ダイアログ（開閉のたびに生成・破棄）

    private static readonly Color C_PARCHMENT = new Color(0.99f, 0.95f, 0.84f, 0.98f);
    private static readonly Color C_BORDER    = new Color(0.84f, 0.66f, 0.18f, 1.00f);
    private static readonly Color C_TITLE     = new Color(0.38f, 0.16f, 0.04f, 1.00f);
    private static readonly Color C_DIVIDER   = new Color(0.80f, 0.62f, 0.18f, 0.70f);
    private static readonly Color C_ROW       = new Color(0.90f, 0.84f, 0.68f, 0.85f);
    private static readonly Color C_CLOSE_BTN = new Color(0.90f, 0.32f, 0.32f, 1.00f);
    private static readonly Color C_GOLD_BTN  = new Color(0.84f, 0.66f, 0.18f, 1.00f);
    private static readonly Color C_MUTED     = new Color(0.52f, 0.38f, 0.22f, 0.85f);
    private static readonly Color C_EMPTY     = new Color(0.50f, 0.32f, 0.10f);

    // ================================================================
    // 初期化
    // ================================================================
    private void Start()
    {
        _canvas = GetMainCanvas();
        if (_canvas == null)
        {
            Debug.LogError("[Friend] Canvas が見つかりません");
            return;
        }
        BuildEntryButton();
        BuildUI();
    }

    private void OnDestroy()
    {
        StopListener();
        if (_qrTexture != null) Destroy(_qrTexture);
    }

    private static Canvas GetMainCanvas()
    {
        var go = GameObject.Find("Canvas");
        return go != null ? go.GetComponent<Canvas>() : FindFirstObjectByType<Canvas>();
    }

    private static TMP_FontAsset GetJpFont()
    {
        var fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
        return fonts.FirstOrDefault(f => f.name.ToLower() == "jp") ?? fonts.FirstOrDefault();
    }

    // ================================================================
    // 開閉
    // ================================================================
    public void ShowFriendList()
    {
        RebuildFriendList();
        _overlay.SetActive(true);
        _listPanel.SetActive(true);
        _qrPanel.SetActive(false);
        StopListener();
    }

    private void ShowMyQR()
    {
        var manager = UserDataManager.instance;
        if (manager == null || string.IsNullOrEmpty(manager.UID)) return;

        string myName = manager.UserData.Username ?? "";
        string content = $"{CallMethodFromQR.FRIEND_QR_PREFIX}{manager.UID}:{myName}";

        if (_qrTexture != null) Destroy(_qrTexture);
        _qrTexture = QRCodeHelper.CreateQRCode(content, 512, 512);
        _qrTexture.filterMode = FilterMode.Point;
        _qrImage.texture = _qrTexture;

        _listPanel.SetActive(false);
        _qrPanel.SetActive(true);

        // 相手が読み取ったらこちらの画面でも気づけるように自分のdocを監視する
        StartListener();
    }

    private void Hide()
    {
        StopListener();
        CloseModal();
        _overlay.SetActive(false);
    }

    private void CloseModal()
    {
        if (_modal != null) Destroy(_modal);
        _modal = null;
    }

    // ================================================================
    // 相手の読み取り検知（QR表示中のみ Listen）
    // ================================================================
    private void StartListener()
    {
        StopListener();
        var manager = UserDataManager.instance;
        var docRef = FirebaseFirestore.DefaultInstance
            .Collection("users").Document(manager.UID);

        _listener = docRef.Listen(snapshot =>
        {
            if (!snapshot.Exists) return;
            var fresh = snapshot.ConvertTo<UserData>();
            var local = UserDataManager.instance.UserData;
            bool added = false;

            foreach (var kv in fresh.Friends)
            {
                if (local.Friends.ContainsKey(kv.Key)) continue;
                local.Friends[kv.Key] = kv.Value;
                added = true;
                ShowToast($"{kv.Value.Name}さんとフレンドになりました！");
            }

            if (added)
            {
                AssetsDatabase.instance?.PlayLevelUpSE();
                // QR表示からフレンド一覧へ戻して結果を見せる
                if (_qrPanel.activeSelf) ShowFriendList();
            }
        });
    }

    private void StopListener()
    {
        _listener?.Stop();
        _listener = null;
    }

    // ================================================================
    // フレンド一覧の構築
    // ================================================================
    private void RebuildFriendList()
    {
        for (int i = _listContent.childCount - 1; i >= 0; i--)
            Destroy(_listContent.GetChild(i).gameObject);

        var jp = GetJpFont();
        var friends = UserDataManager.instance.UserData.Friends
            .OrderByDescending(kv => kv.Value.Favorite)              // お気に入りを先頭に
            .ThenByDescending(kv => kv.Value.Since.ToDateTime())     // あとは登録が新しい順
            .ToList();

        if (friends.Count == 0)
        {
            var empty = MakeLabel("__Empty", _listContent, "まだフレンドがいません。\nQRを見せ合って登録しよう！",
                jp, 38, FontStyles.Normal, C_EMPTY, 800, 200);
            empty.GetComponent<TextMeshProUGUI>().alignment = TextAlignmentOptions.Center;
            return;
        }

        foreach (var kv in friends)
        {
            var row = MakeRect($"__Friend_{kv.Key}", _listContent, C_ROW, 820, 110);
            var layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(30, 30, 10, 10);
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;

            // お気に入りは金色の★を名前の前に付ける
            string displayName = kv.Value.Favorite
                ? $"<color=#D4A82E>★</color> {kv.Value.Name}"
                : kv.Value.Name;
            var name = MakeLabel("__Name", row.transform, displayName, jp, 40, FontStyles.Bold, C_TITLE, 500, 90);
            name.GetComponent<TextMeshProUGUI>().alignment = TextAlignmentOptions.MidlineLeft;

            string sinceText = kv.Value.Since.ToDateTime().ToLocalTime().ToString("yyyy/MM/dd");
            var since = MakeLabel("__Since", row.transform, sinceText, jp, 28, FontStyles.Normal, C_MUTED, 260, 90);
            since.GetComponent<TextMeshProUGUI>().alignment = TextAlignmentOptions.MidlineRight;

            // 行タップでアクションメニュー（お気に入り／フレンド解除）を開く
            string uid = kv.Key;
            var rowBtn = row.AddComponent<Button>();
            rowBtn.targetGraphic = row.GetComponent<Image>();
            rowBtn.onClick.AddListener(() => OpenActionMenu(uid));
        }
    }

    // ================================================================
    // フレンドのアクションメニュー（お気に入り／解除）
    // ================================================================
    private void OpenActionMenu(string friendUid)
    {
        if (!UserDataManager.instance.UserData.Friends.TryGetValue(friendUid, out var entry)) return;
        CloseModal();

        var jp = GetJpFont();
        _modal = BuildModalBase(out var panel, 720, 560);

        var title = MakeLabel("__Title", panel.transform, entry.Name, jp, 48, FontStyles.Bold, C_TITLE, 600, 80);
        title.GetComponent<TextMeshProUGUI>().alignment = TextAlignmentOptions.Center;
        title.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, 200);

        MakeRect("__Div", panel.transform, C_DIVIDER, 620, 4)
            .GetComponent<RectTransform>().anchoredPosition = new Vector2(0, 145);

        string favLabel = entry.Favorite ? "★ お気に入りを解除" : "☆ お気に入りに追加";
        MakeButton("__Fav", panel.transform, C_GOLD_BTN, favLabel, jp, 40, C_TITLE, 560, 110,
            new Vector2(0, 60), () => ToggleFavoriteAsync(friendUid).Forget());

        MakeButton("__Unfriend", panel.transform, C_CLOSE_BTN, "フレンド解除", jp, 40, Color.white, 560, 110,
            new Vector2(0, -75), () => OpenUnfriendConfirm(friendUid));

        MakeButton("__Cancel", panel.transform, new Color(0.48f, 0.26f, 0.06f, 1f), "閉じる", jp, 36, Color.white, 360, 95,
            new Vector2(0, -205), CloseModal);
    }

    private void OpenUnfriendConfirm(string friendUid)
    {
        if (!UserDataManager.instance.UserData.Friends.TryGetValue(friendUid, out var entry)) return;
        CloseModal();

        var jp = GetJpFont();
        _modal = BuildModalBase(out var panel, 760, 480);

        var msg = MakeLabel("__Msg", panel.transform,
            $"「{entry.Name}」さんとのフレンドを\n本当に解除しますか？",
            jp, 42, FontStyles.Bold, C_TITLE, 680, 180);
        msg.GetComponent<TextMeshProUGUI>().alignment = TextAlignmentOptions.Center;
        msg.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, 105);

        MakeButton("__OK", panel.transform, C_CLOSE_BTN, "解除する", jp, 40, Color.white, 300, 110,
            new Vector2(-170, -110), () => UnfriendAsync(friendUid).Forget());

        MakeButton("__Cancel", panel.transform, new Color(0.48f, 0.26f, 0.06f, 1f), "キャンセル", jp, 40, Color.white, 300, 110,
            new Vector2(170, -110), CloseModal);
    }

    /// <summary>モーダルの共通土台（暗幕＋羊皮紙パネル）。暗幕タップで閉じる</summary>
    private GameObject BuildModalBase(out GameObject panel, float w, float h)
    {
        var dim = new GameObject("__FriendModal");
        dim.transform.SetParent(_overlay.transform, false);
        var rt = dim.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        dim.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.45f);
        var dimBtn = dim.AddComponent<Button>();
        dimBtn.transition = Selectable.Transition.None;
        dimBtn.onClick.AddListener(CloseModal);

        var border = MakeRect("__ModalBorder", dim.transform, C_BORDER, w + 16, h + 16);
        panel = MakeRect("__ModalPanel", border.transform, C_PARCHMENT, w, h);
        // パネル内のタップが暗幕の「閉じる」に吸われないようにする
        panel.GetComponent<Image>().raycastTarget = true;
        panel.AddComponent<Button>().transition = Selectable.Transition.None;
        return dim;
    }

    /// <summary>お気に入りON/OFF。自分のdocの1フィールドだけ更新する（0read+1write）</summary>
    private async UniTask ToggleFavoriteAsync(string friendUid)
    {
        var manager = UserDataManager.instance;
        if (!manager.UserData.Friends.TryGetValue(friendUid, out var entry)) return;
        bool newValue = !entry.Favorite;

        try
        {
            await FirebaseFirestore.DefaultInstance
                .Collection("users").Document(manager.UID)
                .UpdateAsync(new Dictionary<FieldPath, object>
                {
                    { new FieldPath("friends", friendUid, "favorite"), newValue }
                }).AsUniTask();

            entry.Favorite = newValue;
            ShowToast(newValue
                ? $"★ {entry.Name}さんをお気に入りに追加しました"
                : $"{entry.Name}さんのお気に入りを解除しました");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[Friend] お気に入り更新エラー: {ex.Message}");
            ShowToast("お気に入りの更新に失敗しました");
        }

        CloseModal();
        RebuildFriendList();
    }

    /// <summary>
    /// フレンド解除。登録時と同様にバッチ書き込みで自分と相手の両方から
    /// アトミックに削除する（片方だけ残る状態は発生しない）。
    /// </summary>
    private async UniTask UnfriendAsync(string friendUid)
    {
        var manager = UserDataManager.instance;
        if (!manager.UserData.Friends.TryGetValue(friendUid, out var entry)) return;

        try
        {
            var db = FirebaseFirestore.DefaultInstance;
            var batch = db.StartBatch();
            batch.Update(
                db.Collection("users").Document(manager.UID),
                new Dictionary<FieldPath, object>
                {
                    { new FieldPath("friends", friendUid), FieldValue.Delete },
                });
            batch.Update(
                db.Collection("users").Document(friendUid),
                new Dictionary<FieldPath, object>
                {
                    { new FieldPath("friends", manager.UID), FieldValue.Delete },
                });
            await batch.CommitAsync().AsUniTask();

            manager.UserData.Friends.Remove(friendUid);
            Debug.Log($"[Friend] フレンド解除: {entry.Name} ({friendUid})");
            ShowToast($"{entry.Name}さんとのフレンドを解除しました");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[Friend] フレンド解除エラー: {ex.Message}");
            ShowToast("フレンド解除に失敗しました");
        }

        CloseModal();
        RebuildFriendList();
    }

    // ================================================================
    // UI構築
    // ================================================================
    private void BuildEntryButton()
    {
        // 右上のフローティングボタン
        var border = MakeRect("__FriendBtnBorder", _canvas.transform, C_BORDER, 196, 86);
        var rt = border.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(-30, -280); // リロードボタン(150px)の下に配置

        var inner = MakeRect("__FriendBtn", border.transform, C_PARCHMENT, 188, 78);
        var jp = GetJpFont();
        var label = MakeLabel("__Label", inner.transform, "フレンド", jp, 36, FontStyles.Bold, C_TITLE, 188, 78);
        label.GetComponent<TextMeshProUGUI>().alignment = TextAlignmentOptions.Center;

        var btn = inner.AddComponent<Button>();
        btn.targetGraphic = inner.GetComponent<Image>();
        btn.onClick.AddListener(ShowFriendList);
    }

    private void BuildUI()
    {
        var jp = GetJpFont();

        // 全画面の半透明オーバーレイ
        _overlay = new GameObject("__FriendOverlay");
        _overlay.transform.SetParent(_canvas.transform, false);
        var ort = _overlay.AddComponent<RectTransform>();
        ort.anchorMin = Vector2.zero; ort.anchorMax = Vector2.one;
        ort.offsetMin = ort.offsetMax = Vector2.zero;
        _overlay.AddComponent<Image>().color = new Color(0.08f, 0.04f, 0.20f, 0.68f);
        _overlay.SetActive(false);

        _listPanel = BuildListPanel(jp);
        _listPanel.transform.SetParent(_overlay.transform, false);

        _qrPanel = BuildQRPanel(jp);
        _qrPanel.transform.SetParent(_overlay.transform, false);
        _qrPanel.SetActive(false);
    }

    private GameObject BuildListPanel(TMP_FontAsset jp)
    {
        var border = MakeRect("__ListBorder", _overlay.transform, C_BORDER, 936, 1316);
        var panel  = MakeRect("__ListPanel", border.transform, C_PARCHMENT, 920, 1300);

        MakeLabel("__Title", panel.transform, "フレンド", jp, 52, FontStyles.Bold, C_TITLE, 700, 90)
            .GetComponent<RectTransform>().anchoredPosition = new Vector2(0, 570);

        MakeButton("__Close", panel.transform, C_CLOSE_BTN, "✕", jp, 52, Color.white, 96, 96,
            new Vector2(396, 586), Hide);

        MakeRect("__Div", panel.transform, C_DIVIDER, 820, 4)
            .GetComponent<RectTransform>().anchoredPosition = new Vector2(0, 510);

        // スクロールリスト
        var scrollGO = MakeRect("__Scroll", panel.transform, new Color(0.94f, 0.89f, 0.76f, 0.90f), 860, 880);
        scrollGO.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, 30);
        var scroll = scrollGO.AddComponent<ScrollRect>();
        scrollGO.AddComponent<RectMask2D>();

        var content = new GameObject("__Content");
        content.transform.SetParent(scrollGO.transform, false);
        var crt = content.AddComponent<RectTransform>();
        crt.anchorMin = new Vector2(0.5f, 1f);
        crt.anchorMax = new Vector2(0.5f, 1f);
        crt.pivot = new Vector2(0.5f, 1f);
        crt.sizeDelta = new Vector2(860, 0);
        var vlayout = content.AddComponent<VerticalLayoutGroup>();
        vlayout.spacing = 14;
        vlayout.padding = new RectOffset(0, 0, 14, 14);
        vlayout.childAlignment = TextAnchor.UpperCenter;
        vlayout.childControlWidth = false;
        vlayout.childControlHeight = false;
        content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scroll.content = crt;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        _listContent = content.transform;

        // 自分のQRを表示ボタン
        MakeButton("__ShowQR", panel.transform, C_GOLD_BTN, "自分のQRを表示", jp, 44, C_TITLE, 600, 110,
            new Vector2(0, -550), ShowMyQR);

        return border;
    }

    private GameObject BuildQRPanel(TMP_FontAsset jp)
    {
        var border = MakeRect("__QRBorder", _overlay.transform, C_BORDER, 936, 1316);
        var panel  = MakeRect("__QRPanel", border.transform, C_PARCHMENT, 920, 1300);

        MakeLabel("__Title", panel.transform, "フレンド登録QR", jp, 52, FontStyles.Bold, C_TITLE, 700, 90)
            .GetComponent<RectTransform>().anchoredPosition = new Vector2(0, 570);

        MakeButton("__Close", panel.transform, C_CLOSE_BTN, "✕", jp, 52, Color.white, 96, 96,
            new Vector2(396, 586), Hide);

        MakeRect("__Div", panel.transform, C_DIVIDER, 820, 4)
            .GetComponent<RectTransform>().anchoredPosition = new Vector2(0, 510);

        // QRコード（白フチ付き）
        var qrBg = MakeRect("__QRBg", panel.transform, Color.white, 700, 700);
        qrBg.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, 90);
        var qrGO = new GameObject("__QRImage");
        qrGO.transform.SetParent(qrBg.transform, false);
        var qrt = qrGO.AddComponent<RectTransform>();
        qrt.sizeDelta = new Vector2(640, 640);
        _qrImage = qrGO.AddComponent<RawImage>();

        var caption = MakeLabel("__Caption", panel.transform,
            "お友達にカメラ（QR読み取り）で\nこのQRを読み取ってもらうと、\nお互いにフレンドになります",
            jp, 38, FontStyles.Normal, C_TITLE, 800, 220);
        caption.GetComponent<TextMeshProUGUI>().alignment = TextAlignmentOptions.Center;
        caption.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, -380);

        MakeButton("__Back", panel.transform, new Color(0.48f, 0.26f, 0.06f, 1f), "もどる", jp, 40, Color.white, 360, 100,
            new Vector2(0, -560), ShowFriendList);

        return border;
    }

    // ================================================================
    // トースト（他クラスからも呼べる）
    // ================================================================
    public static void ShowToast(string message)
    {
        var canvas = GetMainCanvas();
        if (canvas == null)
        {
            Debug.Log($"[Toast] {message}");
            return;
        }

        var jp = GetJpFont();
        var go = new GameObject("__FriendToast");
        go.transform.SetParent(canvas.transform, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0, 320);
        rt.sizeDelta = new Vector2(900, 130);
        go.AddComponent<Image>().color = new Color(0.10f, 0.08f, 0.06f, 0.88f);

        var label = MakeLabel("__Text", go.transform, message, jp, 40, FontStyles.Bold, Color.white, 860, 120);
        label.GetComponent<TextMeshProUGUI>().alignment = TextAlignmentOptions.Center;

        go.AddComponent<ToastFader>();
    }

    /// <summary>表示後しばらくしてフェードアウトし、自分を破棄する</summary>
    private class ToastFader : MonoBehaviour
    {
        private const float LIFE = 2.2f;
        private const float FADE = 0.6f;
        private CanvasGroup _cg;
        private float _t;

        private void Awake() { _cg = gameObject.AddComponent<CanvasGroup>(); }

        private void Update()
        {
            _t += Time.deltaTime;
            if (_t > LIFE) _cg.alpha = 1f - Mathf.Clamp01((_t - LIFE) / FADE);
            if (_t > LIFE + FADE) Destroy(gameObject);
        }
    }

    // ================================================================
    // UI部品ヘルパー
    // ================================================================
    private static GameObject MakeRect(string name, Transform parent, Color color, float w, float h)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(w, h);
        go.AddComponent<Image>().color = color;
        return go;
    }

    private static GameObject MakeLabel(string name, Transform parent, string text,
        TMP_FontAsset font, float size, FontStyles style, Color color, float w, float h)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(w, h);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        if (font != null) tmp.font = font;
        tmp.text = text;
        tmp.fontSize = size;
        tmp.fontStyle = style;
        tmp.color = color;
        tmp.raycastTarget = false;
        return go;
    }

    private static GameObject MakeButton(string name, Transform parent, Color bgColor,
        string text, TMP_FontAsset font, float fontSize, Color textColor,
        float w, float h, Vector2 pos, UnityEngine.Events.UnityAction onClick)
    {
        var go = MakeRect(name, parent, bgColor, w, h);
        go.GetComponent<RectTransform>().anchoredPosition = pos;
        var label = MakeLabel("__Label", go.transform, text, font, fontSize, FontStyles.Bold, textColor, w, h);
        label.GetComponent<TextMeshProUGUI>().alignment = TextAlignmentOptions.Center;
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = go.GetComponent<Image>();
        btn.onClick.AddListener(onClick);
        return go;
    }
}
