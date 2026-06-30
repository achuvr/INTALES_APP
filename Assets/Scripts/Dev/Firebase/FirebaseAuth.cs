using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;

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

            // 登録に成功したときだけキャラクター作成画面を表示する。
            // （旧実装はボタンのonClickで即SetActiveしていたため、登録失敗でも画面が出ていた）
            if (_characterCreateScreen != null) _characterCreateScreen.SetActive(true);
            _whitePanel.SetActive(false);
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
