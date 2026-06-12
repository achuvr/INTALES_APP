using System.Collections.Generic;
using UnityEngine;
using Firebase.Firestore;
using TMPro;

public class CreateNewCharacter : MonoBehaviour
{

    private string _currentSelectJob;
    private string _currentSelectElement;

    [SerializeField] private TMP_InputField _characterNameInputField;

    [SerializeField] private GameObject _loadingPanel;

    private void Start()
    {
        _currentSelectJob = "warrior";
        _currentSelectElement = "fire";
    }

    /// <summary>
    /// 新キャラクターを users/{uid} の characters マップに追加する。
    /// 旧実装にあった「コレクション存在確認のための読み取り」は不要
    /// （マップフィールドへのマージ書き込み1回で完結する）。
    /// </summary>
    public async void CreateCharacter()
    {
        if (string.IsNullOrEmpty(_characterNameInputField.text))
        {
            Debug.Log("name not setting.");
            return;
        }

        SetLoading(true);

        var db = FirebaseFirestore.DefaultInstance;
        var uid = UserDataManager.instance.UID;
        int newIndex = UserDataManager.instance.UserData.Characters.Count;

        var characterData = new Dictionary<string, object>
        {
            { "el", _currentSelectElement },
            { "job", _currentSelectJob },
            { "lv", 1 },
            { "name", _characterNameInputField.text }
        };

        try
        {
            // characters.{newIndex} だけを深いマージで書き込む
            await db.Collection("users").Document(uid).SetAsync(
                new Dictionary<string, object>
                {
                    { "characters", new Dictionary<string, object> { { newIndex.ToString(), characterData } } }
                },
                SetOptions.MergeAll);

            Debug.Log($"キャラクターを作成しました: {_characterNameInputField.text} (slot {newIndex})");
        }
        catch (System.Exception ex)
        {
            Debug.LogError("書き込みエラー: " + ex.Message);
            SetLoading(false);
            return;
        }

        // 新規アカウント直後はローカルにユーザーデータが無いことがあるため、
        // キャラ作成時だけは1回の再取得（1ドキュメント）で確実に同期してからHomeへ
        await UserDataManager.instance.FetchUserDataByUIDForReload(true);

        var newObj = GameObject.FindWithTag("New");
        if (newObj != null) Destroy(newObj);

        var qr = GameObject.FindWithTag("QR");
        if (qr != null) Destroy(qr.gameObject);
    }

    /// <summary>
    /// ローディング表示の切り替え。
    /// Startシーンでは _loadingPanel がインスペクタで割り当て済みだが、
    /// Newシーン（QRからのキャラ追加。Homeに追加ロードされる）では未割り当てのため、
    /// Homeシーン側の共通ローディングパネルにフォールバックする。
    /// どちらも無い場合は何もしない（NullReferenceException を出さない）。
    /// </summary>
    private void SetLoading(bool on)
    {
        var panel = _loadingPanel;
        if (panel == null && AssetsDatabase.instance != null)
            panel = AssetsDatabase.instance.LoadingPanel;
        if (panel != null) panel.SetActive(on);
    }

    public void SetCurrentSelectJob(string currentSelectJob)
    {
        _currentSelectJob = currentSelectJob;
    }

    public void SetCurrentSelectElement(string currentSelectElement)
    {
        _currentSelectElement = currentSelectElement;
    }
}
