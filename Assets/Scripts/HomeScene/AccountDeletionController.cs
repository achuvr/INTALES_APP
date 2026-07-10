using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Cysharp.Threading.Tasks;
using Firebase.Firestore;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using YokeijoAssets;

/// <summary>
/// 退会（アカウント削除）機能。Google Playの「アカウント作成があるアプリは
/// アプリ内に削除手段を提供すること」という要件に対応する。
///
/// 入口: アカウントモーダル（AccountButton）内の「アカウント削除」リンク
/// 流れ: 警告モーダル → 最終確認モーダル → 削除実行
///
/// 削除内容:
///   1. 在店共有エントリ（presence/store の自分のフィールド）
///   2. フレンドの friends マップから自分を削除（相互登録の後始末）
///   3. 旧構造サブコレクション（characters / achievements）
///   4. users/{uid} ドキュメント
///   5. Firebase Auth アカウント（直近ログインが必要なため保存済み認証情報で再ログインしてから削除）
///   6. 端末ローカルのデータ（user.txt・来店記録・PlayerPrefs）
/// </summary>
public class AccountDeletionController : MonoBehaviour
{
    private const string USER_FILE = "user.txt";

    private static readonly Color C_PARCHMENT = new Color(0.99f, 0.95f, 0.84f, 0.98f);
    private static readonly Color C_BORDER    = new Color(0.84f, 0.66f, 0.18f, 1.00f);
    private static readonly Color C_TITLE     = new Color(0.38f, 0.16f, 0.04f, 1.00f);
    private static readonly Color C_DANGER    = new Color(0.78f, 0.16f, 0.16f, 1.00f);
    private static readonly Color C_CANCEL    = new Color(0.48f, 0.26f, 0.06f, 1.00f);

    private Canvas _canvas;
    private GameObject _modal;
    private bool _deleting;

    private void Start()
    {
        var canvasGO = GameObject.Find("Canvas");
        _canvas = canvasGO != null ? canvasGO.GetComponent<Canvas>() : FindFirstObjectByType<Canvas>();
        // 入口ボタンは置かない（アカウントモーダル内の「アカウント削除」リンクから開く）
    }

    /// <summary>アカウント削除フローを開く（アカウントモーダルの「アカウント削除」リンクから呼ばれる）</summary>
    public void OpenDeletionFlow()
    {
        if (_canvas == null)
        {
            var canvasGO = GameObject.Find("Canvas");
            _canvas = canvasGO != null ? canvasGO.GetComponent<Canvas>() : FindFirstObjectByType<Canvas>();
            if (_canvas == null) return;
        }
        ShowWarningModal();
    }

    // ================================================================
    // 確認モーダル（2段階）
    // ================================================================
    private void ShowWarningModal()
    {
        CloseModal();
        var jp = GetJpFont();
        _modal = BuildModalBase(out var panel, 800, 640);

        MakeLabel(panel.transform, "アカウント削除", jp, 46, FontStyles.Bold, C_DANGER,
            700, 80, new Vector2(0, 240));
        MakeLabel(panel.transform,
            "アカウントを削除すると、\nキャラクター・レベル・クーポン・フレンドなど\n全てのデータが完全に削除されます。\n\nこの操作は元に戻せません。",
            jp, 34, FontStyles.Normal, C_TITLE, 720, 280, new Vector2(0, 30));

        MakeButton(panel.transform, C_DANGER, "削除をすすめる", jp, 36, Color.white,
            340, 100, new Vector2(-185, -230), ShowFinalConfirmModal);
        MakeButton(panel.transform, C_CANCEL, "キャンセル", jp, 36, Color.white,
            340, 100, new Vector2(185, -230), CloseModal);
    }

    private void ShowFinalConfirmModal()
    {
        CloseModal();
        var jp = GetJpFont();
        _modal = BuildModalBase(out var panel, 800, 480);

        MakeLabel(panel.transform, "最終確認", jp, 46, FontStyles.Bold, C_DANGER,
            700, 80, new Vector2(0, 160));
        MakeLabel(panel.transform, "本当にアカウントを削除しますか？",
            jp, 38, FontStyles.Bold, C_TITLE, 720, 100, new Vector2(0, 40));

        MakeButton(panel.transform, C_DANGER, "完全に削除する", jp, 36, Color.white,
            340, 100, new Vector2(-185, -140), () => DeleteAccountAsync().Forget());
        MakeButton(panel.transform, C_CANCEL, "キャンセル", jp, 36, Color.white,
            340, 100, new Vector2(185, -140), CloseModal);
    }

    private void CloseModal()
    {
        if (_modal != null) Destroy(_modal);
        _modal = null;
    }

    // ================================================================
    // 削除実行
    // ================================================================
    private async UniTask DeleteAccountAsync()
    {
        if (_deleting) return;
        _deleting = true;
        CloseModal();
        AssetsDatabase.instance?.LoadingPanel?.SetActive(true);

        var manager = UserDataManager.instance;
        string uid = manager.UID;
        var db = FirebaseFirestore.DefaultInstance;

        try
        {
            // 1) 在店共有のエントリを削除（公開設定に関わらず消す）
            await PresenceService.WriteAsync(false);

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
            //    （プロジェクト内に同名の FirebaseAuth クラスがあるためフルパスで指定）
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
            SceneManager.LoadScene("Start");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[AccountDeletion] 削除エラー: {ex.Message}");
            AssetsDatabase.instance?.LoadingPanel?.SetActive(false);
            FriendMenuController.ShowToast("削除に失敗しました。通信環境を確認して再度お試しください");
            _deleting = false;
        }
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
#if UNITY_EDITOR
            string dir = Path.Combine(Application.dataPath, "TestUser");
#else
            string dir = Path.Combine(Application.persistentDataPath, "TestUser");
#endif
            string path = Path.Combine(dir, USER_FILE);
            if (!File.Exists(path)) return false;

            using var sr = new StreamReader(path, Encoding.GetEncoding("utf-8"));
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
#if UNITY_EDITOR
            string userFile = Path.Combine(Application.dataPath, "TestUser", USER_FILE);
#else
            string userFile = Path.Combine(Application.persistentDataPath, "TestUser", USER_FILE);
#endif
            if (File.Exists(userFile)) File.Delete(userFile);
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[AccountDeletion] user.txt 削除失敗: {ex.Message}");
        }

        LocalVisitLog.Clear();      // 来店記録
        PlayerPrefs.DeleteAll();    // 装備状態・各種同期フラグなど
        PlayerPrefs.Save();
    }

    // ================================================================
    // UI部品ヘルパー
    // ================================================================
    private GameObject BuildModalBase(out GameObject panel, float w, float h)
    {
        var dim = new GameObject("__DeleteModal");
        dim.transform.SetParent(_canvas.transform, false);
        var rt = dim.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        dim.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);
        var dimBtn = dim.AddComponent<Button>();
        dimBtn.transition = Selectable.Transition.None;
        dimBtn.onClick.AddListener(CloseModal);

        var border = MakeRect("__ModalBorder", dim.transform, C_BORDER, w + 16, h + 16);
        panel = MakeRect("__ModalPanel", border.transform, C_PARCHMENT, w, h);
        panel.AddComponent<Button>().transition = Selectable.Transition.None;
        return dim;
    }

    private static TMP_FontAsset GetJpFont()
    {
        var fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
        return fonts.FirstOrDefault(f => f.name.ToLower() == "jp") ?? fonts.FirstOrDefault();
    }

    private static GameObject MakeRect(string name, Transform parent, Color color, float w, float h)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>().sizeDelta = new Vector2(w, h);
        go.AddComponent<Image>().color = color;
        return go;
    }

    private static GameObject MakeLabel(Transform parent, string text, TMP_FontAsset font,
        float size, FontStyles style, Color color, float w, float h, Vector2 pos)
    {
        var go = new GameObject("__Label");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(w, h);
        rt.anchoredPosition = pos;
        var tmp = go.AddComponent<TextMeshProUGUI>();
        if (font != null) tmp.font = font;
        tmp.text = text;
        tmp.fontSize = size;
        tmp.fontStyle = style;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;
        return go;
    }

    private void MakeButton(Transform parent, Color bg, string text, TMP_FontAsset font,
        float fontSize, Color textColor, float w, float h, Vector2 pos, UnityEngine.Events.UnityAction onClick)
    {
        var go = MakeRect("__Btn", parent, bg, w, h);
        go.GetComponent<RectTransform>().anchoredPosition = pos;
        MakeLabel(go.transform, text, font, fontSize, FontStyles.Bold, textColor, w, h, Vector2.zero);
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = go.GetComponent<Image>();
        btn.onClick.AddListener(onClick);
    }
}
