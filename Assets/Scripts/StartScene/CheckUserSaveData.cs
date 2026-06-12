using System.Collections.Generic;
using System.IO;
using UnityEngine;
using System.Text;
using Cysharp.Threading.Tasks;
using Firebase.Firestore;
using YokeijoAssets;

/// <summary>
/// ログイン情報のローカル保存（user.txt）と、それを使った自動ログイン。
/// 認証・Firestoreの完了待ちはすべて AsUniTask でメインスレッドに戻して行う。
/// </summary>
public class CheckUserSaveData : MonoBehaviour
{
    private string _path;

    private const string FILE_NAME = "user.txt";
    [SerializeField] private GameObject _whitePanel;

    private void Start()
    {
        #if UNITY_EDITOR
        _path = Path.Combine(Application.dataPath, "TestUser");
        #elif PLATFORM_ANDROID || UNITY_IOS
        Debug.Log("Android or iOS...");
        Directory.CreateDirectory(Application.persistentDataPath + "/TestUser");
        _path = Path.Combine(Application.persistentDataPath, "TestUser");
        Debug.Log("path = " +  _path);
        #endif
        AutoLoginAsync().Forget();
    }

    /// <summary>
    /// 保存済みの user.txt があれば自動ログインしてHomeへ遷移する。
    /// サインインの完了を待ってから Firestore を読む
    /// （待たずに読むと、セキュリティルールの signedIn() に弾かれる競合があった）。
    /// </summary>
    private async UniTask AutoLoginAsync()
    {
        var path = Path.Combine(_path, FILE_NAME);
        if (!File.Exists(path)) return;

        Debug.Log("Past Login... File exists");
        _whitePanel.SetActive(true);

        string uid, mail, pw;
        using (var sr = new StreamReader(path, Encoding.GetEncoding("utf-8")))
        {
            uid = sr.ReadLine();
            mail = sr.ReadLine();
            pw = DecryptAES256.Decrypt(sr.ReadLine());
        }

        try
        {
            var auth = Firebase.Auth.FirebaseAuth.DefaultInstance;
            await auth.SignInWithEmailAndPasswordAsync(mail, pw).AsUniTask();
            Debug.Log("Login Success!");
        }
        catch (System.Exception ex)
        {
            // パスワード変更・通信不良などで自動ログインできないときはログイン画面に戻す
            Debug.LogError($"自動ログイン失敗: {ex}");
            FriendMenuController.ShowToast("自動ログインできませんでした\nもう一度ログインしてください");
            _whitePanel.SetActive(false);
            return;
        }

        SetUserDataSingleton(uid);
        await UserDataManager.instance.FetchUserDataByUID();
    }

    /// <summary>
    /// ログイン／アカウント作成の成功後に呼ぶ（メインスレッド前提）。
    /// user.txt を保存し、ユーザーデータを読み込んでHomeへ遷移する。
    /// 失敗時は例外を投げる（呼び出し元でLoading解除とエラー表示を行う）。
    /// </summary>
    public async UniTask SaveAndEnterAsync(string uid, string mail, string pw, string nickname, bool isLogin)
    {
        var path = Path.Combine(_path, FILE_NAME);
        FirebaseFirestore db = FirebaseFirestore.DefaultInstance;
        DocumentReference docRef = db.Collection("users").Document(uid);

        if (File.Exists(path))
        {
            Debug.Log("File exists");
            SetUserDataSingleton(uid);
            await UserDataManager.instance.FetchUserDataByUID();
            return;
        }

        Debug.Log("File does not exist. Creating.");

        if (isLogin)
        {
            // 既存アカウントのログイン: ユーザー名を取得できてから保存ファイルを作る
            // （先にファイルを作ると、途中で失敗したとき次回起動が壊れた自動ログインになる）
            SetUserDataSingleton(uid);
            DocumentSnapshot snapshot = await docRef.GetSnapshotAsync().AsUniTask();
            var userData = snapshot.ConvertTo<UserData>();
            WriteUserFile(path, uid, mail, pw, userData.Username);
            await UserDataManager.instance.FetchUserDataByUID();
            return;
        }

        // 新規アカウント: 初期データをFirestoreに作成してからキャラ作成へ
        Dictionary<string, object> settings = new Dictionary<string, object>
        {
            { "five_coupon", 0 },
            { "gp", 5 },
            { "name", nickname },
            { "seven_coupon", 0 },
            { "lastDate", Timestamp.GetCurrentTimestamp() },
            { "atk_coupon", 1 },
            { "drink_coupon", 0 },
            { "coffee_coupon", 0 },
            { "characters", new Dictionary<string, object>() } // キャラはこのマップに内蔵する（サブコレクション廃止）
        };
        await docRef.SetAsync(settings, SetOptions.MergeAll).AsUniTask();
        WriteUserFile(path, uid, mail, pw, nickname);
        Debug.Log("書き込み終了");

        SetUserDataSingleton(uid);
        UserDataManager.instance.FetchUserDataByUID('a');
    }

    /// <summary>ログイン情報をローカルに保存する（パスワードはAES256で暗号化）</summary>
    private static void WriteUserFile(string path, string uid, string mail, string pw, string nickname)
    {
        using var writer = new StreamWriter(path, false, Encoding.GetEncoding("utf-8"));
        writer.WriteLine(uid);
        writer.WriteLine(mail);
        writer.WriteLine(EncryptAES256.Encrypt(pw));
        writer.WriteLine(nickname);
    }

    private void SetUserDataSingleton(string uid)
    {
        UserDataManager.instance.SetUID(uid);
    }
}
