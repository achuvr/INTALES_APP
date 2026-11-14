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
/// 画面は宇宙デザインの SpaceStartScreen（起動時にここで作る。旧デザインの画面は隠す）。
/// </summary>
public class CheckUserSaveData : MonoBehaviour
{
    private string _path;

    private const string FILE_NAME = "user.txt";
    [SerializeField] private GameObject _whitePanel;

    private SpaceStartScreen _screen;

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

        // 宇宙デザインの画面を作る（ログイン情報があれば「ログインしています…」で出す）
        bool saved = File.Exists(Path.Combine(_path, FILE_NAME));
        _screen = SpaceStartScreen.Create(FindFirstObjectByType<FirebaseAuth>(), saved);
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
        SetWhitePanel(true);

        string uid = null;
        try
        {
            string mail, pw;
            using (var sr = new StreamReader(path, Encoding.GetEncoding("utf-8")))
            {
                uid = sr.ReadLine();
                mail = sr.ReadLine();
                pw = DecryptAES256.Decrypt(sr.ReadLine());
            }
            var auth = Firebase.Auth.FirebaseAuth.DefaultInstance;
            await auth.SignInWithEmailAndPasswordAsync(mail, pw).AsUniTask();
            Debug.Log("Login Success!");
        }
        catch (System.Exception ex)
        {
            if (this == null) return; // ログインの途中で再生を止めた（エディタ）。止めたあとの画面に何も出さない
            // パスワード変更・通信不良・ファイルの破損などで自動ログインできないときはログインの入力に戻す
            Debug.LogError($"自動ログイン失敗: {ex}");
            if (_screen != null) _screen.OnAutoLoginFailed();
            else FriendMenuController.ShowToast(SpaceStartScreen.T_AUTO_FAILED);
            SetWhitePanel(false);
            return;
        }

        // ログインの途中で再生を止めた（エディタ）: Start シーンも UserDataManager も消えているので、ここで終える
        // （続けると、再生を止めたあとに Firestore を読み、消えた UserDataManager で MissingReferenceException。2026-11-14）
        if (this == null || UserDataManager.instance == null) return;
        SetUserDataSingleton(uid);
        await UserDataManager.instance.FetchUserDataByUID();
    }

    /// <summary>
    /// ログイン／アカウント作成の成功後に呼ぶ（メインスレッド前提）。
    /// user.txt を保存し、ユーザーデータを読み込んでHomeへ遷移する（アカウント作成のときは遷移しない）。
    /// characterTicket はアカウント作成のときに初期データへ入れるキャラクターチケットの枚数。
    /// 失敗時は例外を投げる（呼び出し元でLoading解除とエラー表示を行う）。
    /// </summary>
    public async UniTask SaveAndEnterAsync(string uid, string mail, string pw, string nickname, bool isLogin,
        int characterTicket = 0)
    {
        // ログイン情報は毎回保存し直す（以前の保存が残っていると、パスワードを変えたあとも古い情報で
        // 自動ログインしようとして毎回失敗する。アカウント作成で初期データを作らずに進むこともあった）
        var path = Path.Combine(_path, FILE_NAME);
        FirebaseFirestore db = FirebaseFirestore.DefaultInstance;
        DocumentReference docRef = db.Collection("users").Document(uid);

        if (isLogin)
        {
            // 既存アカウントのログイン: ユーザー名を取得できてから保存ファイルを作る
            // （先にファイルを作ると、途中で失敗したとき次回起動が壊れた自動ログインになる）
            SetUserDataSingleton(uid);
            DocumentSnapshot snapshot = await docRef.GetSnapshotAsync().AsUniTask();
            string name;
            if (snapshot.Exists)
            {
                name = snapshot.ConvertTo<UserData>().Username;
            }
            else
            {
                // アカウント作成の途中で初期データを書けなかったアカウント: ここで初期データを作る
                // （名前は空。新ホームのフレンドの画面から付けられる）
                Debug.LogWarning($"ユーザーの初期データがないので作ります: {uid}");
                await docRef.SetAsync(InitialData("", 0), SetOptions.MergeAll).AsUniTask();
                name = "";
            }
            WriteUserFile(path, uid, mail, pw, name);
            await UserDataManager.instance.FetchUserDataByUID();
            return;
        }

        // 新規アカウント: 初期データをFirestoreに作成してから進む
        await docRef.SetAsync(InitialData(nickname, characterTicket), SetOptions.MergeAll).AsUniTask();
        WriteUserFile(path, uid, mail, pw, nickname);
        Debug.Log("書き込み終了");

        SetUserDataSingleton(uid);
        UserDataManager.instance.FetchUserDataByUID('a');
    }

    /// <summary>新しいユーザーの初期データ</summary>
    private static Dictionary<string, object> InitialData(string nickname, int characterTicket)
    {
        var settings = new Dictionary<string, object>
        {
            { "five_coupon", 0 },
            { "gp", 5 },
            { "name", nickname },
            { "seven_coupon", 0 },
            { "lastDate", Timestamp.GetCurrentTimestamp() },
            { "atk_coupon", 1 },
            { "drink_coupon", 0 },
            { "coffee_coupon", 0 },
            { "characters", new Dictionary<string, object>() }, // キャラはこのマップに内蔵する（サブコレクション廃止）
            // 最初の惑星はホームで赤・青・黄から選んでもらう（2026-11-10。"" ＝まだ選んでいない。SpaceStarterPicker）
            { "starter", "" },
        };
        if (characterTicket > 0) settings["character_ticket"] = characterTicket;
        // アカウント作成の画面で選んだ誕生月（2026-11-07）。選ばなかったら入れない（あとから歯車メニューで登録できる）
        if (SignupExtras.BirthMonth >= 1 && SignupExtras.BirthMonth <= 12) settings["birth_month"] = SignupExtras.BirthMonth;
        SignupExtras.BirthMonth = 0;
        return settings;
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

    /// <summary>旧デザインの読み込み中の白い画面（宇宙デザインの画面では隠れているので見えない）</summary>
    private void SetWhitePanel(bool on)
    {
        if (_whitePanel != null) _whitePanel.SetActive(on);
    }

    private void SetUserDataSingleton(string uid)
    {
        UserDataManager.instance.SetUID(uid);
    }
}
