using System.Collections;
using Firebase.Firestore;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;

public class UserDataManager : SingletonBehaviour<UserDataManager>
{
    [SerializeField] private string _uid;
    public string UID => _uid;
    
    private UserData _userData;
    public UserData UserData => _userData ?? (_userData = new UserData());
    private int _currentSelectCharacterNumber = 0;
    public int CurrentSelectCharacterNumber => _currentSelectCharacterNumber;
    private FirebaseFirestore db;

    public GameObject _loadingPanel;
    public GameObject _createNewCharacterPanel;


    public void SetCurrentSelectCharacterNumber(int number)
    {
        _currentSelectCharacterNumber = number;
    }
    
    public void SetUID(string uid)
    {
        _uid = uid;
    }

    public void FetchUserDataByUID(char init)
    {
        Debug.Log("FetchUserDataByUID.CreateNewCharacter()");
        _createNewCharacterPanel.SetActive(true);
        _loadingPanel.SetActive(false);
    }

    public async UniTask FetchUserDataByUID()
    {
        db = FirebaseFirestore.DefaultInstance;
        if (string.IsNullOrEmpty(_uid))
        {
            Debug.LogError("No user data found");
            return;
        }

        DocumentReference docRef = db.Collection("users").Document(_uid);
        try
        {
            DocumentSnapshot snapshot = await docRef.GetSnapshotAsync();
            if (snapshot.Exists)
            {
                Debug.Log("Find document! UID = " + _uid);

                // ユーザーの基本情報を取得
                _userData = snapshot.ConvertTo<UserData>();
                Debug.Log($"{_userData.Username},{_userData.FiveCoupon},{_userData.SevenCoupon},{_userData.GetRegistrationDateTime().ToString()}");

                // キャラクターデータの取得
                CollectionReference charactersRef = db.Collection("users").Document(_uid).Collection("characters");
                try
                {
                    QuerySnapshot querySnapshot = await charactersRef.GetSnapshotAsync();
                    _userData.Characters.Clear();

                    foreach (var document in querySnapshot.Documents)
                    {
                        Debug.Log($"document.id = {document.Id}");
                        if (document.Exists)
                        {
                            Character character = document.ConvertTo<Character>();
                            Debug.Log($"[character] {document.Id} => " + $"{character.Name}, {character.Job}, {character.Element}, {character.Level}");
                            _userData.Characters.Add(character);
                        }
                    }
                    Debug.Log($"Loaded characters = {_userData.Characters.Count}");
                }
                catch (System.Exception ex)
                {
                    // 4. エラー処理
                    // ★注意: ここでも "Missing or insufficient permissions" が発生する可能性があります
                    Debug.LogError($"キャラ取得エラー: {ex}");
                    Debug.LogError($"サブコレクションの取得エラー: {ex.Message}");
                }
            }
            else
            {
                // ドキュメントが存在しない場合 (UIDが存在しない、またはまだデータが書き込まれていない)
                Debug.LogWarning($"警告: UID '{_uid}' に対応するドキュメントは存在しません。");

                // ★存在しなかった場合の処理 (例: 新規登録画面へ誘導、初期データをFirestoreに書き込むなど)
                //HandleMissingUser(_uid);
            }
        }
        catch (System.Exception ex)
        {
            // 4. エラー処理 (権限不足、ネットワークエラーなど)
            Debug.LogError($"Firestoreからのデータ取得中にエラーが発生しました: {ex.Message}");
        }
        StartCoroutine(LoadSceneAsyncWithActivationControl());
    }
    
    public async UniTask FetchUserDataByUIDForReload()
    {
        db = FirebaseFirestore.DefaultInstance;
        if (string.IsNullOrEmpty(_uid))
        {
            Debug.LogError("No user data found");
            return;
        }

        DocumentReference docRef = db.Collection("users").Document(_uid);
        try
        {
            DocumentSnapshot snapshot = await docRef.GetSnapshotAsync();
            if (snapshot.Exists)
            {
                Debug.Log("Find document! UID = " + _uid);

                // ユーザーの基本情報を取得
                _userData = snapshot.ConvertTo<UserData>();
                Debug.Log($"{_userData.Username},{_userData.FiveCoupon},{_userData.SevenCoupon},{_userData.GetRegistrationDateTime().ToString()}");

                // キャラクターデータの取得
                CollectionReference charactersRef = db.Collection("users").Document(_uid).Collection("characters");
                try
                {
                    QuerySnapshot querySnapshot = await charactersRef.GetSnapshotAsync();
                    _userData.Characters.Clear();

                    foreach (var document in querySnapshot.Documents)
                    {
                        Debug.Log($"document.id = {document.Id}");
                        if (document.Exists)
                        {
                            Character character = document.ConvertTo<Character>();
                            Debug.Log($"[character] {document.Id} => " + $"{character.Name}, {character.Job}, {character.Element}, {character.Level}");
                            _userData.Characters.Add(character);
                        }
                    }
                    Debug.Log($"Loaded characters = {_userData.Characters.Count}");
                }
                catch (System.Exception ex)
                {
                    // 4. エラー処理
                    // ★注意: ここでも "Missing or insufficient permissions" が発生する可能性があります
                    Debug.LogError($"キャラ取得エラー: {ex}");
                    Debug.LogError($"サブコレクションの取得エラー: {ex.Message}");
                }
            }
            else
            {
                // ドキュメントが存在しない場合 (UIDが存在しない、またはまだデータが書き込まれていない)
                Debug.LogWarning($"警告: UID '{_uid}' に対応するドキュメントは存在しません。");

                // ★存在しなかった場合の処理 (例: 新規登録画面へ誘導、初期データをFirestoreに書き込むなど)
                //HandleMissingUser(_uid);
            }
        }
        catch (System.Exception ex)
        {
            // 4. エラー処理 (権限不足、ネットワークエラーなど)
            Debug.LogError($"Firestoreからのデータ取得中にエラーが発生しました: {ex.Message}");
        }
    }

    private IEnumerator LoadSceneAsyncWithActivationControl()
    {
        Debug.Log("LoadScene Home");
        AsyncOperation asyncLoad = SceneManager.LoadSceneAsync("Home");
        asyncLoad.allowSceneActivation = false; 

        while (asyncLoad.progress < 0.9f) // ロード処理が9割完了するまで待機
        {
            float progress = Mathf.Clamp01(asyncLoad.progress / 0.9f);
            yield return null;
        }
        yield return new WaitForSeconds(0.1f); 
        asyncLoad.allowSceneActivation = true;
    }

    public void SetUserData(UserData userData)
    {
        _userData = userData;
    }
}

[FirestoreData, System.Serializable]
public class UserData
{
    public UserData()
    {
        Characters = new List<Character>();
    }
    
    [FirestoreProperty("name")]
    public string Username { get; set; }
    
    [FirestoreProperty("five_coupon")]
    public int FiveCoupon { get; set; }
    
    [FirestoreProperty("seven_coupon")]
    public int SevenCoupon { get; set; }
    
    [FirestoreProperty("lastDate")]
    public Timestamp LastDate { get; set; }
    public System.DateTime GetRegistrationDateTime()
    {
        return LastDate.ToDateTime();
    }
    [FirestoreProperty("gp")]
    public int GP { get; set; }
    [FirestoreProperty("atk_coupon")]
    public int ATKCoupon { get; set; }
    [FirestoreProperty("drink_coupon")]
    public int DrinkCoupon { get; set; }
    [FirestoreProperty("coffee_coupon")]
    public int CoffeeCoupon { get; set; }
    
    
    public List<Character> Characters { get; set; }
}


[FirestoreData, System.Serializable]
public class Character
{
    public Character() {}
    
    [FirestoreProperty("name")]
    public string Name { get; set; }
    
    [FirestoreProperty("el")]
    public string Element { get; set; }
    
    [FirestoreProperty("job")]
    public string Job { get; set; }
    
    [FirestoreProperty("lv")]
    public int Level { get; set; }
}