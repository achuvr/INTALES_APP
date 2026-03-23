using System.Collections.Generic;
using UnityEngine;
using Firebase.Firestore;
using Firebase.Extensions;
using TMPro;
using UnityEngine.SceneManagement;

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
    
    public void CreateCharacter()
    {
        _loadingPanel.SetActive(true);
        if (_characterNameInputField.text != "")
        {
            var db = FirebaseFirestore.DefaultInstance;
            var uid = UserDataManager.instance.UID;
            CollectionReference charactersRef = db.Collection("users").Document(uid).Collection("characters");

            charactersRef.Limit(1).GetSnapshotAsync().ContinueWithOnMainThread(task =>
            {
                if (task.IsFaulted)
                {
                    Debug.LogError("エラーが発生しました: " + task.Exception);
                    return;
                }

                QuerySnapshot snapshot = task.Result;

                // Countが0より大きければ、ドキュメントが存在する＝コレクションが存在する
                if (snapshot.Count > 0)
                {
                    Debug.Log("charactersコレクションは存在します（データがあります）。");
                    // データの作成 (Dictionaryを使用)
                    Dictionary<string, object> characterData = new Dictionary<string, object>
                    {
                        { "el", _currentSelectElement },
                        { "job", _currentSelectJob },
                        { "lv", 1 },
                        { "name", _characterNameInputField.text }
                    };
                    Debug.Log(UserDataManager.instance.UserData.Characters.Count.ToString());
                    DocumentReference docRef = db
                        .Collection("users")
                        .Document(uid)
                        .Collection("characters")
                        .Document(UserDataManager.instance.UserData.Characters.Count.ToString());
                    
                    // データの書き込み (SetAsyncを使用)
                    docRef.SetAsync(characterData).ContinueWithOnMainThread(task =>
                    {
                        if (task.IsFaulted)
                        {
                            Debug.LogError("書き込みエラー: " + task.Exception);
                        }
                        else
                        {
                            Debug.Log("サブコレクションにデータを追加しました！");
                        }
                    });
                }
                else
                {
                    Debug.Log("charactersコレクションは存在しません（空です）。");

                    // データの作成 (Dictionaryを使用)
                    Dictionary<string, object> characterData = new Dictionary<string, object>
                    {
                        { "el", _currentSelectElement },
                        { "job", _currentSelectJob },
                        { "lv", 1 },
                        { "name", _characterNameInputField.text }
                    };
                    DocumentReference docRef = db
                        .Collection("users")
                        .Document(uid)
                        .Collection("characters")
                        .Document("0");
                    
                    // データの書き込み (SetAsyncを使用)
                    docRef.SetAsync(characterData).ContinueWithOnMainThread(task =>
                    {
                        if (task.IsFaulted)
                        {
                            Debug.LogError("書き込みエラー: " + task.Exception);
                        }
                        else
                        {
                            Debug.Log("サブコレクションにデータを追加しました！");
                        }
                    });
                    
                }
                
                // Scene Loading
                UserDataManager.instance.FetchUserDataByUIDForReload();

                if (SceneManager.GetActiveScene().name.IndexOf("Start") != -1)
                {
                    SceneManager.LoadScene("Home");
                }
                
                Destroy(GameObject.FindWithTag("New").gameObject);
                GameObject.FindObjectOfType<CallMethodFromQR>().End();
            });
        }
        else
        {
            Debug.Log("name not setting.");
        }
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
