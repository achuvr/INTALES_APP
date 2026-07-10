using System.Linq;
using Cysharp.Threading.Tasks;
using Firebase.Firestore;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Startシーンのログイン／アカウント作成。
/// Firebaseのタスクはバックグラウンドスレッドで完了するため、
/// 続きの処理は AsUniTask で待ってメインスレッドで行う。
/// （旧実装は ContinueWith の中でUnity APIを呼んでいたため、
/// 成功・失敗のどちらでも例外が握りつぶされてLoadingのまま固まっていた）
/// </summary>
public class FirebaseAuth : MonoBehaviour
{
    [SerializeField] private TMP_InputField _registerEmailInputField;
    [SerializeField] private TMP_InputField _registerPasswordInputField;

    [SerializeField] private TMP_InputField _loginUsernameInputField;
    [SerializeField] private TMP_InputField _loginPasswordInputField;

    [SerializeField] private CheckUserSaveData _checkUserSaveData;

    [SerializeField] private GameObject _whitePanel;

    [SerializeField] private TMP_InputField _nickNameInputField;

    // 登録成功時にのみ表示するキャラクター作成画面（CreateNewCharacterPanel）。
    [SerializeField] private GameObject _characterCreateScreen;

    private bool _busy;

    // ボタンのonClickから呼ばれる（インスペクタ設定のため void のまま）
    public void RegisterUser() => RegisterAsync().Forget();
    public void LoginUser() => LoginAsync().Forget();

    private async UniTask LoginAsync()
    {
        if (_busy) return;
        string mail = _loginUsernameInputField.text.Trim();
        string pw = _loginPasswordInputField.text;
        if (string.IsNullOrEmpty(mail) || string.IsNullOrEmpty(pw))
        {
            FriendMenuController.ShowToast("メールアドレスとパスワードを入力してください");
            _whitePanel.SetActive(false);
            return;
        }

        _busy = true;
        _whitePanel.SetActive(true);
        try
        {
            var auth = Firebase.Auth.FirebaseAuth.DefaultInstance;
            var result = await auth.SignInWithEmailAndPasswordAsync(mail, pw).AsUniTask();
            Debug.Log("Login Success!");

            await _checkUserSaveData.SaveAndEnterAsync(result.User.UserId, mail, pw, "", isLogin: true);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"ログイン失敗: {ex}");
            FriendMenuController.ShowToast(LoginErrorMessage(ex));
            _whitePanel.SetActive(false);
        }
        finally
        {
            _busy = false;
        }
    }

    private async UniTask RegisterAsync()
    {
        if (_busy) return;
        string mail = _registerEmailInputField.text.Trim();
        string pw = _registerPasswordInputField.text;
        string nickname = _nickNameInputField.text.Trim();
        if (string.IsNullOrEmpty(mail) || string.IsNullOrEmpty(pw) || string.IsNullOrEmpty(nickname))
        {
            FriendMenuController.ShowToast("メールアドレス・パスワード・ニックネームを入力してください");
            _whitePanel.SetActive(false);
            return;
        }

        _busy = true;
        _whitePanel.SetActive(true);
        try
        {
            var auth = Firebase.Auth.FirebaseAuth.DefaultInstance;
            var result = await auth.CreateUserWithEmailAndPasswordAsync(mail, pw).AsUniTask();
            Debug.Log("Register Success!");

            await _checkUserSaveData.SaveAndEnterAsync(result.User.UserId, mail, pw, nickname, isLogin: false);

            // 登録に成功したら「キャラクターを作成しますか？」の選択モーダルを出す。
            // はい → キャラクター作成画面へ / いいえ → キャラクターチケットを1枚受け取ってHomeへ
            _whitePanel.SetActive(false);
            ShowCreateCharacterPrompt();
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"アカウント作成失敗: {ex}");
            FriendMenuController.ShowToast(RegisterErrorMessage(ex));
            _whitePanel.SetActive(false);
        }
        finally
        {
            _busy = false;
        }
    }

    // ================================================================
    // アカウント作成直後の「キャラクターを作成しますか？」モーダル
    // ================================================================
    private static readonly Color C_PARCHMENT = new Color(0.99f, 0.95f, 0.84f, 0.98f);
    private static readonly Color C_BORDER    = new Color(0.84f, 0.66f, 0.18f, 1.00f);
    private static readonly Color C_TITLE     = new Color(0.38f, 0.16f, 0.04f, 1.00f);
    private static readonly Color C_NO_BROWN  = new Color(0.48f, 0.26f, 0.06f, 1.00f);

    private GameObject _createPromptModal;

    /// <summary>
    /// アカウント作成直後の確認モーダル。
    /// 「作成する」→ 従来どおりキャラクター作成画面へ。
    /// 「あとで作る」→ キャラクターチケットを1枚付与してHomeへ（キャラ0体で開始）。
    /// </summary>
    private void ShowCreateCharacterPrompt()
    {
        // 引き継ぎ済み端末での再登録など、既にキャラクターがいる場合は何も出さない
        var d = UserDataManager.instance != null ? UserDataManager.instance.UserData : null;
        if (d != null && d.Characters != null && d.Characters.Count > 0) return;

        var canvas = FindFirstObjectByType<Canvas>();
        if (canvas == null)
        {
            // 万一Canvasが見つからなければ従来どおり作成画面を直接開く
            if (_characterCreateScreen != null) _characterCreateScreen.SetActive(true);
            return;
        }
        if (_createPromptModal != null) Destroy(_createPromptModal);

        var jp = Resources.FindObjectsOfTypeAll<TMP_FontAsset>()
            .FirstOrDefault(f => f.name.ToLower() == "jp");

        // 暗幕（ボタン以外では閉じられない。必ずどちらかを選ばせる）
        _createPromptModal = new GameObject("__CreateCharacterPrompt");
        _createPromptModal.transform.SetParent(canvas.transform, false);
        var mrt = _createPromptModal.AddComponent<RectTransform>();
        mrt.anchorMin = Vector2.zero; mrt.anchorMax = Vector2.one;
        mrt.offsetMin = mrt.offsetMax = Vector2.zero;
        _createPromptModal.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);

        var border = MakeRect("__Border", _createPromptModal.transform, C_BORDER, 860, 640);
        RoundedRectSprite.Apply(border.GetComponent<Image>());
        var panel = MakeRect("__Panel", border.transform, C_PARCHMENT, 836, 616);
        RoundedRectSprite.Apply(panel.GetComponent<Image>());

        MakeLabel(panel.transform, "キャラクター作成", jp, 46, FontStyles.Bold, C_TITLE,
            760, 80, new Vector2(0, 240));
        MakeLabel(panel.transform,
            "最初のキャラクターを作成しますか？\n\n「あとで作る」を選ぶと\nキャラクターチケットを1枚受け取り、\nバッグからいつでも作成できます。",
            jp, 33, FontStyles.Normal, C_TITLE, 760, 280, new Vector2(0, 30));

        MakeButton(panel.transform, C_BORDER, "作成する", jp, 36, C_TITLE,
            360, 110, new Vector2(-195, -220), () =>
            {
                Destroy(_createPromptModal);
                _createPromptModal = null;
                if (_characterCreateScreen != null) _characterCreateScreen.SetActive(true);
            });
        MakeButton(panel.transform, C_NO_BROWN, "あとで作る", jp, 36, Color.white,
            360, 110, new Vector2(195, -220), () => GrantTicketAndGoHomeAsync().Forget());
    }

    /// <summary>
    /// 「あとで作る」: キャラクターチケットを1枚付与してHomeへ遷移する。
    /// 付与に失敗した場合はモーダルを残したままにして選び直せるようにする。
    /// </summary>
    private async UniTask GrantTicketAndGoHomeAsync()
    {
        if (_busy) return;
        _busy = true;
        _whitePanel.SetActive(true);
        try
        {
            await FirebaseFirestore.DefaultInstance
                .Collection("users").Document(UserDataManager.instance.UID)
                .UpdateAsync(new System.Collections.Generic.Dictionary<string, object>
                {
                    { "character_ticket", FieldValue.Increment(1) },
                }).AsUniTask();

            LocalHistoryLog.Add("coupon", "キャラクターチケット を1枚 入手（アカウント作成特典）");
            if (_createPromptModal != null) Destroy(_createPromptModal);
            _createPromptModal = null;

            // ユーザーデータを取得してHomeへ（キャラ0体でもHome側はプレースホルダ表示で動く）
            await UserDataManager.instance.FetchUserDataByUIDForReload(true);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"キャラクターチケット付与エラー: {ex}");
            FriendMenuController.ShowToast("通信に失敗しました\nもう一度お試しください");
            _whitePanel.SetActive(false);
        }
        finally
        {
            _busy = false;
        }
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
        return go;
    }

    // アカウント作成失敗時の、Firebaseのエラーコードに応じた利用者向けメッセージ。
    private static string RegisterErrorMessage(System.Exception ex)
    {
        var fe = AsFirebaseException(ex);
        if (fe != null)
        {
            switch ((Firebase.Auth.AuthError)fe.ErrorCode)
            {
                case Firebase.Auth.AuthError.EmailAlreadyInUse:
                    return "このメールアドレスは既に登録されています\nログイン画面からお進みください";
                case Firebase.Auth.AuthError.InvalidEmail:
                case Firebase.Auth.AuthError.MissingEmail:
                    return "メールアドレスの形式が正しくありません";
                case Firebase.Auth.AuthError.WeakPassword:
                    return "パスワードは6文字以上で設定してください";
                case Firebase.Auth.AuthError.MissingPassword:
                    return "パスワードを入力してください";
                case Firebase.Auth.AuthError.NetworkRequestFailed:
                    return "通信エラーです\n電波の良い場所で再度お試しください";
            }
        }
        return "アカウントを作成できませんでした\n入力内容をご確認ください";
    }

    // ログイン失敗時の、Firebaseのエラーコードに応じた利用者向けメッセージ。
    private static string LoginErrorMessage(System.Exception ex)
    {
        var fe = AsFirebaseException(ex);
        if (fe != null)
        {
            switch ((Firebase.Auth.AuthError)fe.ErrorCode)
            {
                case Firebase.Auth.AuthError.WrongPassword:
                case Firebase.Auth.AuthError.InvalidCredential:
                case Firebase.Auth.AuthError.UserNotFound:
                case Firebase.Auth.AuthError.InvalidEmail:
                    return "メールアドレスまたはパスワードが正しくありません";
                case Firebase.Auth.AuthError.UserDisabled:
                    return "このアカウントは現在ご利用いただけません";
                case Firebase.Auth.AuthError.TooManyRequests:
                    return "試行回数が多すぎます\nしばらくしてから再度お試しください";
                case Firebase.Auth.AuthError.NetworkRequestFailed:
                    return "通信エラーです\n電波の良い場所で再度お試しください";
            }
        }
        return "ログインできませんでした\nメールアドレスとパスワードをご確認ください";
    }

    // AsUniTask 経由でも FirebaseException を取り出せるようにする（AggregateException 対策）。
    private static Firebase.FirebaseException AsFirebaseException(System.Exception ex)
    {
        switch (ex)
        {
            case null:
                return null;
            case Firebase.FirebaseException fe:
                return fe;
            case System.AggregateException ae:
                foreach (var inner in ae.Flatten().InnerExceptions)
                    if (inner is Firebase.FirebaseException f) return f;
                return null;
            default:
                return ex.InnerException as Firebase.FirebaseException;
        }
    }
}
