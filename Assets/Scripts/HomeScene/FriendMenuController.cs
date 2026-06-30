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
    private TextMeshProUGUI _shareCheckMark; // 「ログイン情報を公開する」チェックボックスの✓
    private Dictionary<string, Timestamp> _presence; // 在店状況（uid→チェックイン時刻）
    private bool _togglingShare;

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
        UpdateShareCheckMark();
        RebuildFriendList();
        _overlay.SetActive(true);
        _listPanel.SetActive(true);
        _qrPanel.SetActive(false);
        StopListener();
        RefreshPresenceAsync().Forget();
    }

    /// <summary>
    /// 在店状況を取得してリストに「ログイン中」を反映する。
    /// presence/store の1ドキュメント読み取りだけで全フレンド分がわかる。
    /// </summary>
    private async UniTask RefreshPresenceAsync()
    {
        _presence = await PresenceService.GetPresenceAsync();
        // 取得中にパネルが閉じられたりQR表示に切り替わっていたら何もしない
        if (_overlay.activeSelf && _listPanel.activeSelf)
            RebuildFriendList();
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

            // 在店中（公開ONでチェックイン中、営業終了時刻前）なら「ログイン中」を付ける
            if (_presence != null &&
                _presence.TryGetValue(kv.Key, out var checkinAt) &&
                PresenceService.IsStillPresent(checkinAt))
            {
                displayName += " <color=#1FA838><size=70%>●ログイン中</size></color>";
            }

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

    // ================================================================
    // ユーザー名（アカウント名）の変更
    // ================================================================
    /// <summary>
    /// ユーザー名を今変更できるか（前回変更から1か月経過しているか）を判定する。
    /// 未変更（name_changed_at 未設定=既定の1970年）なら常に可。
    /// nextAllowed には次に変更可能になるローカル日時を返す。
    /// </summary>
    private static bool CanRenameNow(UserData user, out System.DateTime nextAllowed)
    {
        System.DateTime last = user.NameChangedAt.ToDateTime();   // UTC
        System.DateTime nextUtc = last.AddMonths(1);
        nextAllowed = nextUtc.ToLocalTime();
        return System.DateTime.UtcNow >= nextUtc;
    }

    /// <summary>ユーザー名を変更するダイアログ（現在の名前を入力欄に初期表示）。</summary>
    private void OpenRenameDialog()
    {
        var manager = UserDataManager.instance;
        if (manager == null || manager.UserData == null) return;
        CloseModal();

        var jp = GetJpFont();
        _modal = BuildModalBase(out var panel, 760, 560);

        var title = MakeLabel("__Title", panel.transform, "ユーザー名を変更", jp, 46, FontStyles.Bold, C_TITLE, 680, 80);
        title.GetComponent<TextMeshProUGUI>().alignment = TextAlignmentOptions.Center;
        title.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, 200);

        MakeRect("__Div", panel.transform, C_DIVIDER, 620, 4)
            .GetComponent<RectTransform>().anchoredPosition = new Vector2(0, 145);

        string hintText = CanRenameNow(manager.UserData, out var nextAllowed)
            ? "フレンドに表示されるアカウントの名前です\n（キャラクター名とは別・変更は1か月に1回まで）"
            : $"変更は1か月に1回までです\n次に変更できるのは {nextAllowed:yyyy/MM/dd} 以降です";
        var hint = MakeLabel("__Hint", panel.transform, hintText,
            jp, 28, FontStyles.Normal, C_MUTED, 680, 90);
        hint.GetComponent<TextMeshProUGUI>().alignment = TextAlignmentOptions.Center;
        hint.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, 70);

        var input = MakeInputField(panel.transform, jp, manager.UserData.Username ?? "",
            "ユーザー名を入力", 600, 100, new Vector2(0, -25));

        MakeButton("__OK", panel.transform, C_GOLD_BTN, "変更する", jp, 38, C_TITLE, 300, 110,
            new Vector2(-160, -160), () => RenameAsync(input.text).Forget());
        MakeButton("__Cancel", panel.transform, new Color(0.48f, 0.26f, 0.06f, 1f), "キャンセル", jp, 38, Color.white, 300, 110,
            new Vector2(160, -160), CloseModal);
    }

    /// <summary>
    /// ユーザー名を変更する。まず自分の users ドキュメントの name を更新し、
    /// 続いて全フレンドのドキュメントの friends.{自分uid}.name へも反映する
    /// （フレンド一覧に表示される名前は登録時のスナップショットのため伝播が必要）。
    /// フレンドへの反映は各自独立のベストエフォート（退会済み等で一部失敗しても自分の変更は確定）。
    /// </summary>
    private async UniTask RenameAsync(string raw)
    {
        var manager = UserDataManager.instance;
        if (manager == null || manager.UserData == null) return;

        string name = (raw ?? "").Trim();
        if (string.IsNullOrEmpty(name)) { ShowToast("ユーザー名を入力してください"); return; }
        if (name.Length > 20) name = name.Substring(0, 20);
        if (name == manager.UserData.Username) { CloseModal(); return; }

        // 1か月に1回までの制限（ダイアログは開いたままにして理由を見せる）
        if (!CanRenameNow(manager.UserData, out var nextAllowed))
        {
            ShowToast($"ユーザー名の変更は1か月に1回までです（次回 {nextAllowed:yyyy/MM/dd} 以降）");
            return;
        }

        CloseModal();
        var db = FirebaseFirestore.DefaultInstance;
        var nowTs = Timestamp.GetCurrentTimestamp();

        // 1) 自分のドキュメントを更新（名前＋最終変更日時。ここが失敗したら中断）
        try
        {
            await db.Collection("users").Document(manager.UID)
                .UpdateAsync(new Dictionary<FieldPath, object>
                {
                    { new FieldPath("name"), name },
                    { new FieldPath("name_changed_at"), nowTs },
                }).AsUniTask();
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[Friend] ユーザー名変更エラー: {ex.Message}");
            ShowToast("ユーザー名の変更に失敗しました");
            return;
        }
        manager.UserData.Username = name;
        manager.UserData.NameChangedAt = nowTs;

        // 2) 全フレンドの friends.{自分uid}.name を更新（since は壊さずサブフィールドのみ）
        var friendUids = manager.UserData.Friends != null
            ? manager.UserData.Friends.Keys.ToList()
            : new List<string>();

        int ok = 0, fail = 0;
        foreach (var fuid in friendUids)
        {
            try
            {
                await db.Collection("users").Document(fuid)
                    .UpdateAsync(new Dictionary<FieldPath, object>
                    {
                        { new FieldPath("friends", manager.UID, "name"), name }
                    }).AsUniTask();
                ok++;
            }
            catch (System.Exception ex)
            {
                fail++;
                Debug.LogWarning($"[Friend] フレンド({fuid})への名前反映に失敗: {ex.Message}");
            }
        }

        if (friendUids.Count == 0)
            ShowToast($"ユーザー名を「{name}」に変更しました");
        else if (fail == 0)
            ShowToast($"ユーザー名を「{name}」に変更しました（フレンド{ok}人に反映）");
        else
            ShowToast($"ユーザー名を「{name}」に変更（{ok}/{friendUids.Count}人に反映・{fail}人は失敗）");
    }

    /// <summary>コード生成のTMP入力欄（背景・Text Area・プレースホルダ・本文を組み立てる）。</summary>
    private static TMP_InputField MakeInputField(Transform parent, TMP_FontAsset font, string value,
        string placeholder, float w, float h, Vector2 pos)
    {
        var go = MakeRect("__Input", parent, new Color(1f, 1f, 1f, 0.96f), w, h);
        go.GetComponent<RectTransform>().anchoredPosition = pos;
        var img = go.GetComponent<Image>();
        var input = go.AddComponent<TMP_InputField>();
        input.targetGraphic = img;

        // 表示領域（マスク）
        var area = new GameObject("Text Area");
        area.transform.SetParent(go.transform, false);
        var art = area.AddComponent<RectTransform>();
        art.anchorMin = Vector2.zero; art.anchorMax = Vector2.one;
        art.offsetMin = new Vector2(22, 8); art.offsetMax = new Vector2(-22, -8);
        area.AddComponent<RectMask2D>();

        // プレースホルダ
        var ph = new GameObject("Placeholder");
        ph.transform.SetParent(area.transform, false);
        var phrt = ph.AddComponent<RectTransform>();
        phrt.anchorMin = Vector2.zero; phrt.anchorMax = Vector2.one; phrt.offsetMin = phrt.offsetMax = Vector2.zero;
        var phTmp = ph.AddComponent<TextMeshProUGUI>();
        if (font != null) phTmp.font = font;
        phTmp.text = placeholder; phTmp.fontSize = 40; phTmp.fontStyle = FontStyles.Italic;
        phTmp.color = new Color(0.50f, 0.38f, 0.22f, 0.55f);
        phTmp.alignment = TextAlignmentOptions.MidlineLeft;

        // 本文
        var txt = new GameObject("Text");
        txt.transform.SetParent(area.transform, false);
        var txrt = txt.AddComponent<RectTransform>();
        txrt.anchorMin = Vector2.zero; txrt.anchorMax = Vector2.one; txrt.offsetMin = txrt.offsetMax = Vector2.zero;
        var txTmp = txt.AddComponent<TextMeshProUGUI>();
        if (font != null) txTmp.font = font;
        txTmp.fontSize = 40; txTmp.color = C_TITLE;
        txTmp.alignment = TextAlignmentOptions.MidlineLeft;

        input.textViewport = art;
        input.textComponent = txTmp;
        input.placeholder = phTmp;
        if (font != null) input.fontAsset = font;
        input.pointSize = 40;
        input.characterLimit = 20;
        input.lineType = TMP_InputField.LineType.SingleLine;
        input.onFocusSelectAll = true;
        input.text = value;
        return input;
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
        // 下部ボタングリッドの3行目右に置く（左はガチャボタン）。
        // 列・サイズ・配色はバッグ/情報/戦闘/装備ボタンに合わせる（GachaController の定数・スタイルを共用）。
        var btnGO = MakeRect("__FriendBtn", _canvas.transform, Color.white,
            GachaController.GRID_BTN_W, GachaController.GRID_BTN_H);
        GachaController.ApplyGridButtonStyle(btnGO.GetComponent<Image>());
        var rt = btnGO.GetComponent<RectTransform>();

        // Page_Characters の子に置く（キャラクターページ表示中のみ表示される）。
        // 既存4ボタンと同じく中央アンカー基準の座標で配置する。
        var page = _canvas.transform.Find("Page_Characters");
        if (page != null)
        {
            btnGO.transform.SetParent(page, false);
            btnGO.transform.SetAsLastSibling(); // ページ内の他要素より手前に表示
        }
        else
        {
            Debug.LogWarning("[Friend] Page_Characters が見つからないため、Canvas直下にボタンを置きます");
        }
        rt.anchoredPosition = new Vector2(GachaController.GRID_COL_X, GachaController.GRID_ROW_Y);

        var jp = GetJpFont();
        var ink = GachaController.GRID_TEXT_COLOR;

        // 人型アイコン（2人のシルエットを円の組み合わせで描く。色は他ボタンの文字色に合わせる）。
        // 「フレンド」は4文字で幅を取るため、アイコンは少し小さめにして左端に寄せる
        var icon = new GameObject("__Icon");
        icon.transform.SetParent(btnGO.transform, false);
        var irt = icon.AddComponent<RectTransform>();
        irt.sizeDelta = new Vector2(64, 64);
        irt.anchoredPosition = new Vector2(-152, 0);
        var inkBack = new Color(ink.r, ink.g, ink.b, 0.45f);
        MakeEllipse(icon.transform, inkBack, 18, 18, new Vector2(-16, 16)); // 後ろの人・頭
        MakeEllipse(icon.transform, inkBack, 32, 22, new Vector2(-16, -6)); // 後ろの人・体
        MakeEllipse(icon.transform, ink, 23, 23, new Vector2(9, 14));       // 前の人・頭
        MakeEllipse(icon.transform, ink, 39, 27, new Vector2(9, -11));      // 前の人・体

        // 文字サイズは既存ボタン（バッグ等）と同じ実効サイズに合わせる
        // （4文字で収まらない場合のみ自動で僅かに縮む）
        var label = MakeLabel("__Label", btnGO.transform, "フレンド", jp, GachaController.GRID_FONT_SIZE,
            FontStyles.Bold, ink, 296, GachaController.GRID_BTN_H);
        label.GetComponent<RectTransform>().anchoredPosition = new Vector2(36, 0);
        var labelTmp = label.GetComponent<TextMeshProUGUI>();
        labelTmp.alignment = TextAlignmentOptions.Center;
        labelTmp.enableAutoSizing = true;
        labelTmp.fontSizeMax = GachaController.GRID_FONT_SIZE;
        labelTmp.fontSizeMin = 50;

        var btn = btnGO.AddComponent<Button>();
        btn.targetGraphic = btnGO.GetComponent<Image>();
        btn.onClick.AddListener(ShowFriendList);
    }

    /// <summary>アイコン用の円・楕円パーツを置く</summary>
    private static void MakeEllipse(Transform parent, Color color, float w, float h, Vector2 pos)
    {
        var go = new GameObject("__Ellipse");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(w, h);
        rt.anchoredPosition = pos;
        var img = go.AddComponent<Image>();
        UICircleSprite.Apply(img);
        img.color = color;
        img.raycastTarget = false;
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

        // スクロールリスト（下部に公開設定の行を置くぶん少し短め）
        var scrollGO = MakeRect("__Scroll", panel.transform, new Color(0.94f, 0.89f, 0.76f, 0.90f), 860, 800);
        scrollGO.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, 70);
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

        // 「ログイン情報を公開する」チェックボックス
        BuildShareToggle(panel.transform, jp);

        // 自分のQRを表示ボタン（左）＋ ユーザー名を変更ボタン（右）
        MakeButton("__ShowQR", panel.transform, C_GOLD_BTN, "自分のQRを表示", jp, 38, C_TITLE, 430, 110,
            new Vector2(-235, -550), ShowMyQR);
        MakeButton("__RenameUser", panel.transform, C_GOLD_BTN, "ユーザー名を変更", jp, 38, C_TITLE, 430, 110,
            new Vector2(235, -550), OpenRenameDialog);

        return border;
    }

    /// <summary>「ログイン情報を公開する」のチェックボックス行を作る</summary>
    private void BuildShareToggle(Transform parent, TMP_FontAsset jp)
    {
        var row = new GameObject("__ShareRow");
        row.transform.SetParent(parent, false);
        var rt = row.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(700, 70);
        rt.anchoredPosition = new Vector2(0, -415);

        // チェックボックス（金枠＋羊皮紙＋✓）
        var box = MakeRect("__CheckBorder", row.transform, C_BORDER, 60, 60);
        box.GetComponent<RectTransform>().anchoredPosition = new Vector2(-310, 0);
        var inner = MakeRect("__Check", box.transform, Color.white, 50, 50);
        var mark = MakeLabel("__Mark", inner.transform, "✓", jp, 44, FontStyles.Bold,
            new Color(0.16f, 0.62f, 0.22f), 50, 50);
        _shareCheckMark = mark.GetComponent<TextMeshProUGUI>();
        _shareCheckMark.alignment = TextAlignmentOptions.Center;

        var label = MakeLabel("__ShareLabel", row.transform, "ログイン情報を公開する", jp, 36, FontStyles.Bold, C_TITLE, 560, 70);
        var labelTmp = label.GetComponent<TextMeshProUGUI>();
        labelTmp.alignment = TextAlignmentOptions.MidlineLeft;
        label.GetComponent<RectTransform>().anchoredPosition = new Vector2(30, 0);

        // 行全体をタップ可能にする
        var rowImg = row.AddComponent<Image>();
        rowImg.color = new Color(0, 0, 0, 0); // 透明だがレイキャストは受ける
        var btn = row.AddComponent<Button>();
        btn.transition = Selectable.Transition.None;
        btn.onClick.AddListener(() => ToggleSharePresenceAsync().Forget());

        UpdateShareCheckMark();
    }

    private void UpdateShareCheckMark()
    {
        if (_shareCheckMark != null)
            _shareCheckMark.enabled = UserDataManager.instance.UserData.SharePresence;
    }

    /// <summary>
    /// 公開設定のON/OFF切り替え。
    /// ONに切替: 設定を保存し、いまチェックイン中なら在店状態も共有する。
    /// OFFに切替: 設定を保存し、共有済みの在店状態を削除する（誰からも見えなくなる）。
    /// </summary>
    private async UniTask ToggleSharePresenceAsync()
    {
        if (_togglingShare) return; // 連打防止
        _togglingShare = true;

        var manager = UserDataManager.instance;
        bool newValue = !manager.UserData.SharePresence;

        try
        {
            await FirebaseFirestore.DefaultInstance
                .Collection("users").Document(manager.UID)
                .UpdateAsync("share_presence", newValue).AsUniTask();

            manager.UserData.SharePresence = newValue;
            UpdateShareCheckMark();

            if (newValue)
            {
                // いま店にいる（チェックイン中）なら、即座に共有する
                if (LocalVisitLog.HasOpenVisit())
                    await PresenceService.WriteAsync(true);
                ShowToast("ログイン情報を公開しました");
            }
            else
            {
                // 共有済みのエントリを消して、誰からも見えない状態にする
                await PresenceService.WriteAsync(false);
                ShowToast("ログイン情報を非公開にしました");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[Friend] 公開設定の更新エラー: {ex.Message}");
            ShowToast("設定の更新に失敗しました");
        }
        finally
        {
            _togglingShare = false;
        }
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
