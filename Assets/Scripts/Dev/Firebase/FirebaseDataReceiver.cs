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
    // 前回ダウンロードしたイベント画像URLリスト（改行区切り）。これが変わったときだけ再取得する
    private const string CACHED_EVENT_URLS_KEY = "CachedEventImageUrls";

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

        // EventImagePreviewがシーンになければ自動生成
        if (EventImagePreview.instance == null)
        {
            var go = new UnityEngine.GameObject("EventImagePreview");
            go.AddComponent<EventImagePreview>();
        }



        FetchEventNoticeData();
        FetchAchievementData();
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
    /// [デバッグ用] イベント画像を強制的に再取得する
    /// キャッシュとURL履歴をクリアし、UIをリセットして、Firebaseから再取得する
    /// </summary>
    [ContextMenu("Debug: Force Refetch Event Images")]
    public void DebugSimulateNewDay()
    {
        Debug.Log("[デバッグ] イベント画像を強制再取得します");

        // キャッシュをクリア
        ClearImageCache();
        Debug.Log("[デバッグ] キャッシュをクリアしました");

        // 保存されたURLリストをリセット
        PlayerPrefs.DeleteKey(CACHED_EVENT_URLS_KEY);
        PlayerPrefs.Save();
        Debug.Log("[デバッグ] 保存されたURLリストをリセットしました");

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

    /// <summary>
    /// イベント画像を表示する。
    /// master/config の URL リストが前回ダウンロード時と同じならローカルキャッシュを使い、
    /// 変わっていたときだけ再ダウンロードする
    /// （旧実装の「毎日キャッシュ破棄→再DL」をやめ、転送量を画像更新時のみに抑える）。
    /// </summary>
    private async void FetchEventNoticeData()
    {
        var config = await MasterData.GetConfigAsync();
        // 通信待ちの間にシーン遷移などで自分が破棄されていたら何もしない
        if (this == null) return;
        string signature = config.EventImages != null ? string.Join("\n", config.EventImages) : "";
        string cachedSignature = PlayerPrefs.GetString(CACHED_EVENT_URLS_KEY, "__none__");

        if (signature == cachedSignature && HasValidCache())
        {
            Debug.Log("イベント画像に変更なし → キャッシュから読み込みます");
            LoadImagesFromCache();
            return;
        }

        Debug.Log("イベント画像が更新されています → 再ダウンロードします");
        ClearImageCache();
        // 旧署名もここで消す。部分DLのまま中断し、その後 event_images が旧リストに
        // 戻されたとき、「旧署名＋部分キャッシュ」が完全なキャッシュ扱いされるのを防ぐ
        PlayerPrefs.DeleteKey(CACHED_EVENT_URLS_KEY);
        PlayerPrefs.Save();
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
                    var img = pref.GetComponent<UnityEngine.UI.Image>();
                    img.sprite = sprite;
                    // アスペクト比を保ってサイズ調整
                    var rt = pref.GetComponent<RectTransform>();
                    float parentWidth = _scrollContent.GetComponent<RectTransform>().rect.width;
                    if (parentWidth <= 0) parentWidth = Screen.width;
                    float aspectRatio = (float)texture.width / texture.height;
                    rt.sizeDelta = new Vector2(parentWidth, parentWidth / aspectRatio);
                    // タップでプレビューを開く
                    var tappable = pref.AddComponent<EventImageTappable>();
                    tappable.Setup(sprite);





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
    /// Firebaseから画像を取得してキャッシュに保存。
    /// 画像URL一覧は master/config の event_images（1ドキュメント）から取得する。
    /// </summary>
    private async Task FetchAndCacheImagesFromFirebase()
    {
        try
        {
            var config = await MasterData.GetConfigAsync();
            // 通信待ちの間にシーン遷移などで自分が破棄されていたら何もしない
            if (this == null) return;
            var eventImages = config.EventImages;
            if (eventImages != null && eventImages.Count > 0)
            {
                int failedCount = 0;
                for (int i = 0; i < eventImages.Count; i++)
                {
                            try
                            {
                                string key = i.ToString();
                                if (string.IsNullOrEmpty(eventImages[i]))
                                {
                                    Debug.LogWarning($"画像キー '{key}' のURLが空です。スキップします。");
                                    continue;
                                }

                                string storageUrl = eventImages[i];
                                Debug.Log($"画像取得開始: key={key}, url={storageUrl}");

                                string imageUrl;
                                if (storageUrl.StartsWith("gs://"))
                                {
                                    // gs://bucket/path 形式からパスを抽出してReferenceを作成
                                    System.Uri gsUri = new System.Uri(storageUrl);
                                    string path = gsUri.AbsolutePath.TrimStart('/');
                                    StorageReference imageRef = _storage.RootReference.Child(path);
                                    System.Uri downloadUri = await imageRef.GetDownloadUrlAsync();
                                    imageUrl = downloadUri.ToString();
                                }
                                else if (storageUrl.StartsWith("http"))
                                {
                                    // https:// 形式の場合はそのまま使用
                                    imageUrl = storageUrl;
                                }
                                else
                                {
                                    // パスのみの場合はStorageのChildとして取得
                                    StorageReference imageRef = _storage.RootReference.Child(storageUrl);
                                    System.Uri downloadUri = await imageRef.GetDownloadUrlAsync();
                                    imageUrl = downloadUri.ToString();
                                }

                                using (UnityWebRequest www = UnityWebRequestTexture.GetTexture(imageUrl))
                                {
                                    // ダウンロードが完了するまで待機
                                    var operation = www.SendWebRequest();
                                    while (!operation.isDone)
                                    {
                                        await Task.Yield();
                                    }

                                    // ダウンロード中にシーン遷移していたら中断（表示先が破棄されている）
                                    if (this == null || _scrollContent == null) return;

                                    // エラーチェック
                                    if (www.result != UnityWebRequest.Result.Success)
                                    {
                                        Debug.LogError($"画像のダウンロードに失敗しました (key={key}): {www.error} / HTTP {www.responseCode}");
                                        failedCount++;
                                        continue;
                                    }

                                    // ダウンロードしたTexture2Dを取得
                                    Texture2D texture = DownloadHandlerTexture.GetContent(www);

                                    // 画像をキャッシュに保存（JPG形式）
                                    byte[] jpgData = texture.EncodeToJPG();
                                    SaveImageToCache(key, jpgData);

                                    // Texture2DをSpriteに変換
                                    Sprite sprite = Sprite.Create(
                                        texture,
                                        new Rect(0, 0, texture.width, texture.height),
                                        Vector2.one * 0.5f
                                    );
                                    sprite.name = key;

                                    var pref = Instantiate(_eventNoticePrefab);
                                    var img = pref.GetComponent<UnityEngine.UI.Image>();
                                    img.sprite = sprite;
                                    // アスペクト比を保ってサイズ調整
                                    var rt = pref.GetComponent<RectTransform>();
                                    float parentWidth = _scrollContent.GetComponent<RectTransform>().rect.width;
                                    if (parentWidth <= 0) parentWidth = Screen.width;
                                    float aspectRatio = (float)texture.width / texture.height;
                                    rt.sizeDelta = new Vector2(parentWidth, parentWidth / aspectRatio);
                                    // タップでプレビューを開く
                                    var tappable2 = pref.AddComponent<EventImageTappable>();
                                    tappable2.Setup(sprite);





                                    pref.transform.SetParent(_scrollContent, false);
                                    pref.transform.SetAsFirstSibling();
                                }
                            }
                            catch (Firebase.Storage.StorageException storageEx)
                            {
                                failedCount++;
                                Debug.LogError($"Firebase Storage エラー (key={i}): ErrorCode={storageEx.ErrorCode}, HTTP={storageEx.HttpResultCode}, Message={storageEx.Message}");
                            }
                            catch (System.Exception e)
                            {
                                failedCount++;
                                Debug.LogError($"画像処理中にエラーが発生しました (key={i}): {e.GetType().Name}: {e.Message}\n{e.StackTrace}");
                            }
                }

                if (failedCount == 0)
                {
                    // 全画像のDLに成功したときだけURLリストを記録する
                    // （失敗があれば記録せず、次回起動時に再ダウンロードを試みる）
                    PlayerPrefs.SetString(CACHED_EVENT_URLS_KEY, string.Join("\n", eventImages));
                    PlayerPrefs.Save();
                }
                else
                {
                    Debug.LogWarning($"イベント画像 {failedCount}件のDLに失敗。次回起動時に再取得します");
                }
                if (_loadingPanel != null) _loadingPanel.SetActive(false);
            }
            else
            {
                Debug.Log($"イベント画像が登録されていません。");
                if (_loadingPanel != null) _loadingPanel.SetActive(false);
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"データ取得エラー: {ex.Message}");
        }
    }

    private async void FetchAchievementData()
    {
        // master/config の achievements 配列から取得（追加の読み取りは発生しない）
        try
        {
            var config = await MasterData.GetConfigAsync();
            _achievementList = config.Achievements ?? new List<Achievement>();
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
