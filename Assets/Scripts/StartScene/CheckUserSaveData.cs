using System.Collections.Generic;
using System.IO;
using UnityEngine;
using System.Text;
using System.Threading.Tasks;
using Firebase.Firestore;
using UnityEngine.SceneManagement;
using YokeijoAssets;

public class CheckUserSaveData : MonoBehaviour
{
    
    private string _path;
    
    private const string FILE_NAME = "user.txt";
    [SerializeField] private GameObject _whitePanel;
    
    private async void Start()
    {
        #if UNITY_EDITOR
        _path = Path.Combine(Application.dataPath, "TestUser");
        #elif PLATFORM_ANDROID || UNITY_IOS
        Debug.Log("Android or iOS...");
        Directory.CreateDirectory(Application.persistentDataPath + "/TestUser");
        _path = Path.Combine(Application.persistentDataPath, "TestUser");
        #endif
        await CheckExistFile();
    }

    
    private async Task CheckExistFile()
    {
        var path = Path.Combine(_path, FILE_NAME);

        if (File.Exists(path))
        {
            Debug.Log("Past Login... File exists");
            _whitePanel.SetActive(true);
            StreamReader sr = new StreamReader(path, Encoding.GetEncoding("utf-8"));
            var uid = sr.ReadLine();
            var mail = sr.ReadLine();
            var pw = DecryptAES256.Decrypt(sr.ReadLine());
            sr.Close();
            
            Firebase.Auth.FirebaseAuth auth = Firebase.Auth.FirebaseAuth.DefaultInstance;
            auth.SignInWithEmailAndPasswordAsync(mail, pw).ContinueWith(task =>
            {
                if (task.IsCanceled)
                {
                    Debug.LogError("SignInWithEmailAndPasswordAsync was canceled.");
                    _whitePanel.SetActive(false);
                    return;
                }

                if (task.IsFaulted)
                {
                    Debug.LogError("SignInWithEmailAndPasswordAsync encountered an error: " + task.Exception);
                    _whitePanel.SetActive(false);
                    return;
                }

                Firebase.Auth.AuthResult result = task.Result;
                Debug.Log("Login Success!");
            });

            SetUserDataSingleton(uid);
            UserDataManager.instance.FetchUserDataByUID();
        }
    }
    

    public async void CheckExistFile(string uid, string mail, string pw, string nickname, bool isLogin)
    {
        var path = Path.Combine(_path, FILE_NAME);
        // Firestoreにデータ書き込み
        FirebaseFirestore db = FirebaseFirestore.DefaultInstance;
        DocumentReference docRef = db.Collection("users").Document(uid);

        if (File.Exists(path))
        {
            Debug.Log("File exists");
            SetUserDataSingleton(uid);
            UserDataManager.instance.FetchUserDataByUID();
        }
        else
        {
            Debug.Log("File does not exist. Creating.");
            
            // 文字コードを指定
            Encoding enc = Encoding.GetEncoding("utf-8");

            // ファイルを開く
            StreamWriter writer = new StreamWriter(path, false, enc);

            // テキストを書き込む
            writer.WriteLine(uid);
            writer.WriteLine(mail);
            writer.WriteLine(EncryptAES256.Encrypt(pw));
            
            if (isLogin)
            {
                SetUserDataSingleton(uid);
                DocumentSnapshot snapshot = await docRef.GetSnapshotAsync();
                var userData = snapshot.ConvertTo<UserData>();
                writer.WriteLine(userData.Username);
                writer.Close();
                UserDataManager.instance.FetchUserDataByUID();
                return;
            }
            writer.WriteLine(nickname);
            writer.Close();
            Debug.Log("書き込み終了");

            Dictionary<string, object> settings = new Dictionary<string, object>
            {
                { "five_coupon", 0 },
                {"gp", 5},
                { "name", nickname },
                { "seven_coupon", 0 },
                { "lastDate", Timestamp.GetCurrentTimestamp() },
                { "atk_coupon", 1 },
                { "drink_coupon", 0 },
                { "coffee_coupon", 0 }
            };
            await docRef.SetAsync(settings, SetOptions.MergeAll);
            SetUserDataSingleton(uid);
            UserDataManager.instance.FetchUserDataByUID('a');
        }
    }

    private void SetUserDataSingleton(string uid)
    {
        UserDataManager.instance.SetUID(uid);
    }
}
