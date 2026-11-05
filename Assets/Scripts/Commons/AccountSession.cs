using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Firebase.Firestore;
using UnityEngine;
using UnityEngine.SceneManagement;
using YokeijoAssets;

/// <summary>
/// アカウントまわりの操作（ログアウト・退会）とプライバシーポリシーのURL。UIは持たない。
/// 旧ホーム（AccountButton / AccountDeletionController）と新ホーム（SpaceAccountMenu）の両方から呼ぶ。
/// どちらの画面でも同じ処理になるよう、実装はここだけに置く。
/// </summary>
public static class AccountSession
{
    /// <summary>プライバシーポリシー（Firebase Hosting で docs/ を公開）</summary>
    public const string PRIVACY_POLICY_URL = "https://intales-a0459.web.app/privacy-policy.html";

    private const string USER_FILE = "user.txt";

    /// <summary>自動ログイン用に保存しているログイン情報のファイル（CheckUserSaveData と同じ場所）</summary>
    private static string UserFilePath
    {
        get
        {
#if UNITY_EDITOR
            return Path.Combine(Application.dataPath, "TestUser", USER_FILE);
#else
            return Path.Combine(Application.persistentDataPath, "TestUser", USER_FILE);
#endif
        }
    }

    public static void OpenPrivacyPolicy() => Application.OpenURL(PRIVACY_POLICY_URL);

    /// <summary>
    /// ログアウト: 端末の自動ログイン情報（user.txt）を削除し、
    /// Firebase Auth からサインアウトして Start シーンへ戻る。
    /// </summary>
    public static void Logout()
    {
        try
        {
            if (File.Exists(UserFilePath)) File.Delete(UserFilePath);
            Debug.Log("[Account] user.txt を削除しました（自動ログイン解除）");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[Account] user.txt 削除エラー: {ex.Message}");
        }

        // プロジェクト内に同名の FirebaseAuth クラスがあるためフルパスで指定
        try { Firebase.Auth.FirebaseAuth.DefaultInstance.SignOut(); }
        catch (System.Exception ex) { Debug.LogWarning($"[Account] サインアウトエラー: {ex.Message}"); }

        ReturnToStart();
    }

    public static void ReturnToStart() => SceneManager.LoadScene("Start");

    /// <summary>
    /// 退会（アカウント削除）。Google Play / App Store の「アプリ内で削除できること」の要件に対応する。
    /// 失敗したら例外を投げる（呼び出し側で表示を戻してエラーを出す）。完了後の画面遷移は呼び出し側で行う。
    ///
    /// 削除内容:
    ///   1. 在店共有エントリ（presence/store の自分のフィールド）
    ///   2. フレンドの friends マップから自分を削除（相互登録の後始末）
    ///   3. 旧構造サブコレクション（characters / achievements）
    ///   4. users/{uid} ドキュメント
    ///   5. Firebase Auth アカウント（直近ログインが必要なため保存済み認証情報で再ログインしてから削除）
    ///   6. 端末ローカルのデータ（user.txt・来店記録・PlayerPrefs）
    /// </summary>
    public static async UniTask DeleteAccountAsync()
    {
        var manager = UserDataManager.instance;
        string uid = manager.UID;
        var db = FirebaseFirestore.DefaultInstance;

        // 1) 在店共有のエントリを削除（公開設定に関わらず消す）。入店中の人数の記録も消す
        //    （名前もIDもない記録だが、この端末が覚えている記録のIDは後で PlayerPrefs ごと消えるため先に）
        await PresenceService.WriteAsync(false);
        await StoreVisitors.CheckOutAsync();

        // 2) フレンドの friends マップから自分を削除
        var friendUids = manager.UserData.Friends.Keys.ToList();
        if (friendUids.Count > 0)
        {
            var batch = db.StartBatch();
            foreach (var fuid in friendUids)
            {
                batch.Update(db.Collection("users").Document(fuid),
                    new Dictionary<FieldPath, object>
                    {
                        { new FieldPath("friends", uid), FieldValue.Delete },
                    });
            }
            try
            {
                await batch.CommitAsync().AsUniTask();
            }
            catch (System.Exception ex)
            {
                // 相手のアカウントが既に消えている場合など。退会自体は続行する
                Debug.LogWarning($"[AccountDeletion] フレンド側の掃除に一部失敗: {ex.Message}");
            }
        }

        // 3) 旧構造のサブコレクションを削除（残っていれば）
        await DeleteSubcollectionAsync(db, uid, "characters");
        await DeleteSubcollectionAsync(db, uid, "achievements");

        // 4) ユーザードキュメント本体を削除
        await db.Collection("users").Document(uid).DeleteAsync().AsUniTask();

        // 5) Firebase Auth のアカウントを削除
        //    削除には直近のログインが必要なため、保存済みの認証情報で再ログインしてから消す
        var auth = Firebase.Auth.FirebaseAuth.DefaultInstance;
        if (TryReadSavedCredentials(out string mail, out string pw))
        {
            try
            {
                await auth.SignInWithEmailAndPasswordAsync(mail, pw).AsUniTask();
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[AccountDeletion] 再ログインに失敗（そのまま削除を試みます）: {ex.Message}");
            }
        }
        if (auth.CurrentUser != null)
            await auth.CurrentUser.DeleteAsync().AsUniTask();

        // 6) 端末ローカルのデータを削除
        DeleteLocalData();
        manager.SetUID("");
        manager.SetUserData(new UserData());

        Debug.Log("[AccountDeletion] アカウント削除が完了しました");
    }

    private static async UniTask DeleteSubcollectionAsync(FirebaseFirestore db, string uid, string subName)
    {
        try
        {
            var snap = await db.Collection("users").Document(uid).Collection(subName)
                .GetSnapshotAsync().AsUniTask();
            if (snap.Count == 0) return;

            var batch = db.StartBatch();
            foreach (var doc in snap.Documents)
                batch.Delete(doc.Reference);
            await batch.CommitAsync().AsUniTask();
            Debug.Log($"[AccountDeletion] 旧 {subName} サブコレクションを削除 ({snap.Count}件)");
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[AccountDeletion] {subName} の削除に失敗（続行）: {ex.Message}");
        }
    }

    /// <summary>user.txt から保存済みのメール・パスワードを読む（再認証用）</summary>
    private static bool TryReadSavedCredentials(out string mail, out string pw)
    {
        mail = null;
        pw = null;
        try
        {
            if (!File.Exists(UserFilePath)) return false;

            using var sr = new StreamReader(UserFilePath, Encoding.GetEncoding("utf-8"));
            sr.ReadLine();                         // uid（不要）
            mail = sr.ReadLine();
            pw = DecryptAES256.Decrypt(sr.ReadLine());
            return !string.IsNullOrEmpty(mail) && !string.IsNullOrEmpty(pw);
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[AccountDeletion] 認証情報の読み込みに失敗: {ex.Message}");
            return false;
        }
    }

    /// <summary>端末に残る個人データを削除する</summary>
    private static void DeleteLocalData()
    {
        try
        {
            if (File.Exists(UserFilePath)) File.Delete(UserFilePath);
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[AccountDeletion] user.txt 削除失敗: {ex.Message}");
        }

        LocalVisitLog.Clear();      // 来店記録
        PlayerPrefs.DeleteAll();    // 装備状態・各種同期フラグなど
        PlayerPrefs.Save();
    }
}
