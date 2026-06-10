using Firebase;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using TMPro;

public class FirebaseAuth : MonoBehaviour
{
    
    [SerializeField] private  TMP_InputField _registerEmailInputField;
    [SerializeField] private  TMP_InputField _registerPasswordInputField;
    
    [SerializeField] private  TMP_InputField _loginUsernameInputField;
    [SerializeField] private  TMP_InputField _loginPasswordInputField;
    
    [SerializeField] private  CheckUserSaveData _checkUserSaveData;

    [SerializeField] private GameObject _whitePanel;
    
    [SerializeField] private TMP_InputField _nickNameInputField;
    
    public void RegisterUser()
    {
        Firebase.Auth.FirebaseAuth auth = Firebase.Auth.FirebaseAuth.DefaultInstance;
        
        auth.CreateUserWithEmailAndPasswordAsync(_registerEmailInputField.text, _registerPasswordInputField.text).ContinueWith(task => {
            if (task.IsCanceled) {
                Debug.LogError("CreateUserWithEmailAndPasswordAsync was canceled.");
                _whitePanel.SetActive(false);
                return;
            }
            if (task.IsFaulted) {
                Debug.LogError("CreateUserWithEmailAndPasswordAsync encountered an error: " + task.Exception);
                _whitePanel.SetActive(false);
                return;
            }

            // Firebase user has been created.
            Firebase.Auth.AuthResult result = task.Result;
            Debug.Log("Register Success!");
            
            _checkUserSaveData.CheckExistFile(result.User.UserId, _registerEmailInputField.text, _registerPasswordInputField.text, _nickNameInputField.text, false);
        });
    }


    public void LoginUser()
    {
        Firebase.Auth.FirebaseAuth auth = Firebase.Auth.FirebaseAuth.DefaultInstance;
        
        Debug.Log("E-mail: " + _loginUsernameInputField.text);
        Debug.Log("Password: " + _loginPasswordInputField.text);
        auth.SignInWithEmailAndPasswordAsync(_loginUsernameInputField.text, _loginPasswordInputField.text).ContinueWith(task => {
            if (task.IsCanceled) {
                Debug.LogError("SignInWithEmailAndPasswordAsync was canceled.");
                _whitePanel.SetActive(false);
                return;
            }
            if (task.IsFaulted) {
                Debug.LogError("SignInWithEmailAndPasswordAsync encountered an error: " + task.Exception);
                _whitePanel.SetActive(false);
                return;
            }

            Firebase.Auth.AuthResult result = task.Result;
            // Debug.LogFormat("User signed in successfully: {0} ({1})", result.User.DisplayName, result.User.UserId);
            Debug.Log("Login Success!");
            
            _checkUserSaveData.CheckExistFile(result.User.UserId, _loginUsernameInputField.text, _loginPasswordInputField.text,"", true);
        });
    }
}
