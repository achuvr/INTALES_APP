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
            FriendMenuController.ShowToast("ログインできませんでした\nメールアドレスとパスワードをご確認ください");
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
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"アカウント作成失敗: {ex}");
            FriendMenuController.ShowToast("アカウントを作成できませんでした\n入力内容をご確認ください");
            _whitePanel.SetActive(false);
        }
        finally
        {
            _busy = false;
        }
    }
}
