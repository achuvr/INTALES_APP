using System;
using System.Text;
using System.IO;
using Firebase.Firestore;
using Firebase.Auth; // ユーザー認証が必要なため
using UnityEngine;
using System.Threading.Tasks;
using Firebase;
using Unity.VisualScripting; // 非同期処理に必要
using System.Collections.Generic;
using Firebase.Extensions;
using UnityEngine.Networking; // 画像ダウンロードに必要
using Firebase.Storage;

public class FirebaseDataReceiver : SingletonBehaviour<FirebaseDataReceiver>
{
    private const string CACHE_FOLDER_NAME = "EventImageCache";
    private const string LAST_FETCH_DATE_KEY = "LastEventImageFetchDate";

    private FirebaseFirestore _database;
    private Firebase.Auth.FirebaseAuth _auth;
    private FirebaseStorage _storage;

    [SerializeField] private List<Achievement> _achievementList;
    public List<Achievement> AchievementList => _achievementList;
    public GameObject _eventNoticePrefab;
    public Transform _scrollContent;

    [SerializeField] private GameObject _loadingPanel;

    private string CacheFolderPath => Path.Combine(Application.persistentDataPath, CACHE_FOLDER_NAME);

    private async void Start()
    {
        _storage = FirebaseStorage.DefaultInstance;
        _database = FirebaseFirestore.DefaultInstance;
        _auth = Firebase.Auth.FirebaseAuth.DefaultInstance;

        CheckAndClearCacheIfNewDay();
        FetchEventNoticeData();
        FetchAchievementData();
    }

    /// <summary>
    /// 日付が変わっていたらキャッシュをクリアする
    /// </summary>
    private void CheckAndClearCacheIfNewDay()
    {
        string today = DateTime.Now.ToString("yyyy-MM-dd");
        string lastFetchDate = PlayerPrefs.GetString(LAST_FETCH_DATE_KEY, "");

        if (lastFetchDate != today)
        {
            ClearImageCache();
            PlayerPrefs.SetString(LAST_FETCH_DATE_KEY, today);
            PlayerPrefs.Save();
            Debug.Log($"新しい日付です。キャッシュをクリアしました: {today}");
        }
    }

    /// <summary>
    /// キャッシュフォルダを削除
    /// </summary>
    private void ClearImageCache()
    {
        if (Directory.Exists(CacheFolderPath))
        {
            Directory.Delete(CacheFolderPath, true);
        }
    }

    /// <summary>
    /// [デバッグ用] 日付が変わった時の処理をシミュレートする
    /// キャッシュをクリアし、UIをリセットして、Firebaseから再取得する
    /// </summary>
    [ContextMenu("Debug: Simulate New Day")]
    public void DebugSimulateNewDay()
    {
        Debug.Log("[デバッグ] 日付変更をシミュレートします");

        // キャッシュをクリア
        ClearImageCache();
        Debug.Log("[デバッグ] キャッシュをクリアしました");

        // 保存された日付をリセット
        PlayerPrefs.DeleteKey(LAST_FETCH_DATE_KEY);
        PlayerPrefs.Save();
        Debug.Log("[デバッグ] 保存された日付をリセットしました");

        // 既存のUI要素をクリア
        ClearEventNoticeUI();

        // ローディングパネルを表示
        _loadingPanel.SetActive(true);

        // Firebaseから再取得
        FetchEventNoticeData();
        Debug.Log("[デバッグ] Firebaseからの再取得を開始しました");
    }

    /// <summary>
    /// イベント通知UIをクリアする
    /// </summary>
    private void ClearEventNoticeUI()
    {
        if (_scrollContent != null)
        {
            for (int i = _scrollContent.childCount - 1; i >= 0; i--)
            {
                Destroy(_scrollContent.GetChild(i).gameObject);
            }
        }
    }

    /// <summary>
    /// 画像をローカルにキャッシュとして保存
    /// </summary>
    private void SaveImageToCache(string fileName, byte[] imageData)
    {
        if (!Directory.Exists(CacheFolderPath))
        {
            Directory.CreateDirectory(CacheFolderPath);
        }

        string filePath = Path.Combine(CacheFolderPath, fileName + ".jpg");
        File.WriteAllBytes(filePath, imageData);
    }

    /// <summary>
    /// ローカルキャッシュから画像を読み込む
    /// </summary>
    private byte[] LoadImageFromCache(string fileName)
    {
        string filePath = Path.Combine(CacheFolderPath, fileName + ".jpg");
        if (File.Exists(filePath))
        {
            return File.ReadAllBytes(filePath);
        }
        return null;
    }

    /// <summary>
    /// キャッシュされた画像ファイル名のリストを取得
    /// </summary>
    private List<string> GetCachedImageFileNames()
    {
        List<string> fileNames = new List<string>();
        if (Directory.Exists(CacheFolderPath))
        {
            string[] files = Directory.GetFiles(CacheFolderPath, "*.jpg");
            foreach (string file in files)
            {
                fileNames.Add(Path.GetFileNameWithoutExtension(file));
            }
        }
        return fileNames;
    }

    /// <summary>
    /// キャッシュが存在するかチェック
    /// </summary>
    private bool HasValidCache()
    {
        return Directory.Exists(CacheFolderPath) && Directory.GetFiles(CacheFolderPath, "*.jpg").Length > 0;
    }

    private async void FetchEventNoticeData()
    {
        // キャッシュが存在する場合はローカルから読み込み
        if (HasValidCache())
        {
            Debug.Log("キャッシュから画像を読み込みます");
            LoadImagesFromCache();
            return;
        }

        // キャッシュがない場合はFirebaseから取得
        Debug.Log("Firebaseから画像を取得します");
        await FetchAndCacheImagesFromFirebase();
    }

    /// <summary>
    /// ローカルキャッシュから画像を読み込んでUIに表示
    /// </summary>
    private void LoadImagesFromCache()
    {
        try
        {
            List<string> cachedFiles = GetCachedImageFileNames();
            // ファイル名でソート（数字順）
            cachedFiles.Sort((a, b) => int.Parse(a).CompareTo(int.Parse(b)));

            foreach (string fileName in cachedFiles)
            {
                byte[] imageData = LoadImageFromCache(fileName);
                if (imageData != null)
                {
                    Texture2D texture = new Texture2D(2, 2);
                    texture.LoadImage(imageData);

                    Sprite sprite = Sprite.Create(
                        texture,
                        new Rect(0, 0, texture.width, texture.height),
                        Vector2.one * 0.5f
                    );
                    sprite.name = fileName;

                    var pref = Instantiate(_eventNoticePrefab);
                    pref.GetComponent<UnityEngine.UI.Image>().sprite = sprite;
                    pref.transform.SetParent(_scrollContent, false);
                    pref.transform.SetAsFirstSibling();
                }
            }
            _loadingPanel.SetActive(false);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"キャッシュからの読み込みエラー: {ex.Message}");
            // キャッシュ読み込みに失敗した場合はFirebaseから取得
            _ = FetchAndCacheImagesFromFirebase();
        }
    }

    /// <summary>
    /// Firebaseから画像を取得してキャッシュに保存
    /// </summary>
    private async Task FetchAndCacheImagesFromFirebase()
    {
        CollectionReference colRef = _database.Collection("events");
        try
        {
            QuerySnapshot snapshot = await colRef.GetSnapshotAsync();
            if (snapshot != null)
            {
                foreach (var document in snapshot.Documents)
                {
                    if (document.Exists)
                    {
                        Dictionary<string, object> data = document.ToDictionary();
                        for (int i = 0; i < data.Count; i++)
                        {
                            try
                            {
                                StorageReference imageRef = _storage.GetReferenceFromUrl(data[i.ToString()].ToString());
                                Task<System.Uri> getUrlTask = imageRef.GetDownloadUrlAsync();
                                System.Uri downloadUrl = await getUrlTask;

                                using (UnityWebRequest www = UnityWebRequestTexture.GetTexture(downloadUrl))
                                {
                                    // ダウンロードが完了するまで待機
                                    var operation = www.SendWebRequest();
                                    while (!operation.isDone)
                                    {
                                        await Task.Yield();
                                    }

                                    // エラーチェック
                                    if (www.result != UnityWebRequest.Result.Success)
                                    {
                                        Debug.LogError("画像のダウンロードに失敗しました: " + www.error);
                                        return;
                                    }

                                    // ダウンロードしたTexture2Dを取得
                                    Texture2D texture = DownloadHandlerTexture.GetContent(www);

                                    // 画像をキャッシュに保存（JPG形式）
                                    byte[] jpgData = texture.EncodeToJPG();
                                    SaveImageToCache(i.ToString(), jpgData);

                                    // Texture2DをSpriteに変換
                                    Sprite sprite = Sprite.Create(
                                        texture,
                                        new Rect(0, 0, texture.width, texture.height),
                                        Vector2.one * 0.5f
                                    );
                                    sprite.name = i.ToString();

                                    var pref = Instantiate(_eventNoticePrefab);
                                    pref.GetComponent<UnityEngine.UI.Image>().sprite = sprite;
                                    pref.transform.SetParent(_scrollContent, false);
                                    pref.transform.SetAsFirstSibling();
                                }
                            }
                            catch (System.Exception e)
                            {
                                Debug.LogError($"画像処理中にエラーが発生しました: {e.Message}");
                            }
                        }
                        _loadingPanel.SetActive(false);
                    }
                }
            }
            else
            {
                Debug.Log($"ドキュメントが見つかりません。");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"データ取得エラー: {ex.Message}");
        }
    }

    private async void FetchAchievementData()
    {
        CollectionReference colRef = _database.Collection("achievements");
        try
        {
            QuerySnapshot snapshot = await colRef.GetSnapshotAsync();
            _achievementList = new List<Achievement>();
            if (colRef != null)
            {
                Achievement achievement;
                foreach(var document in snapshot.Documents)
                {
                    if (document.Exists)
                    {
                        achievement = document.ConvertTo<Achievement>();
                        _achievementList.Add(achievement);
                    }
                }
                
            }
            else
            {
                Debug.Log($"ドキュメントが見つかりません。");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"データ取得エラー: {ex.Message}");
        }
    }
}

[FirestoreData, System.Serializable]
public class Achievement
{
    public Achievement() {}

    [SerializeField] private string name;
    [FirestoreProperty("name")]
    public string Name
    {
        get { return name; }
        set { name = value; } 
    }
    
    [SerializeField] private string description;
    [FirestoreProperty("text")]
    public string Description
    {
        get { return description; }
        set { description = value; }
    }
    
    [SerializeField] private string imageUrl;
    [FirestoreProperty("image_url")]
    public string ImageUrl
    {
        get { return imageUrl; }
        set { imageUrl = value; }
    }

    private bool _isAuto;
    [FirestoreProperty("auto")]
    public bool IsAuto
    {
        get { return _isAuto; }
        set { _isAuto = value; }
    }
}
