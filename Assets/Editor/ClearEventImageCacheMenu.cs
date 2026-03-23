using System.IO;
using UnityEditor;
using UnityEngine;

public static class ClearEventImageCacheMenu
{
    private const string CACHE_FOLDER_NAME = "EventImageCache";
    private const string LAST_FETCH_DATE_KEY = "LastEventImageFetchDate";

    [MenuItem("Tools/Clear Event Image Cache")]
    private static void ClearEventImageCache()
    {
        string cacheFolderPath = Path.Combine(Application.persistentDataPath, CACHE_FOLDER_NAME);

        if (Directory.Exists(cacheFolderPath))
        {
            Directory.Delete(cacheFolderPath, true);
            PlayerPrefs.DeleteKey(LAST_FETCH_DATE_KEY);
            PlayerPrefs.Save();
            Debug.Log($"イベント画像キャッシュをクリアしました: {cacheFolderPath}");
        }
        else
        {
            Debug.Log("キャッシュフォルダが存在しません。");
        }
    }
}