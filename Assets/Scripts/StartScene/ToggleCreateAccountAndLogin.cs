using TMPro;
using UnityEngine;

public class ToggleCreateAccountAndLogin : MonoBehaviour
{
    
    private const string LOGIN = "アカウントをお持ちの方はこちら";
    private const string REGISTER = "アカウントをお持ちでない方はこちら";

    private const string CREATE_ACCOUNT = "アカウント作成";
    private const string LOGIN_ACCOUNT = "ログイン";

    [SerializeField] private TextMeshProUGUI _text;
    private bool _isLogin;
    
    [SerializeField] private GameObject _loginPanel;
    [SerializeField] private GameObject _registerPanel;

    [SerializeField] private TextMeshProUGUI _titleText;
    
    void Start()
    {
        _isLogin = true;
    }

    public void ToggleText()
    {
        if (_isLogin)
        {
            _text.text = REGISTER;
            _registerPanel.SetActive(false);
            _loginPanel.SetActive(true);
            _titleText.text = LOGIN_ACCOUNT;
        }
        else
        {
            _text.text = LOGIN;
            _registerPanel.SetActive(true);
            _loginPanel.SetActive(false);
            _titleText.text = CREATE_ACCOUNT;
        }
        _isLogin = !_isLogin;
    }
}
