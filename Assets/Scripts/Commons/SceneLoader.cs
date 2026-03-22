using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;

public class SceneLoader : SingletonBehaviour<SceneLoader>
{

    public void MergeScene(string sceneName)
    {
        // シーン名が設定されているか確認
        if (string.IsNullOrEmpty(sceneName))
        {
            Debug.LogError("マージするシーン名が設定されていません。インスペクターで 'Scene To Load' を設定してください。");
            return;
        }

        // ロードとマージ処理を開始
        StartCoroutine(LoadAndMergeScene(sceneName));
    }

    /// <summary>
    /// シーンの非同期ロードとマージを行うコルーチン
    /// </summary>
    IEnumerator LoadAndMergeScene(string sceneName)
    {
        Debug.Log($"シーン '{sceneName}' の非同期ロードを開始します...");

        // 1. 追加ロードの実行
        // LoadSceneMode.Additive を指定して、現在のシーンに追加する
        AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);

        // ロードが完了するまで待機
        while (!asyncLoad.isDone)
        {
            // ロード進捗状況の確認などを行うことも可能
            // Debug.Log($"進捗: {asyncLoad.progress * 100}%");
            yield return null;
        }

        Debug.Log($"シーン '{sceneName}' のロードが完了しました。");
        
        // 2. マージ処理の実行
        MergeLoadedScene(sceneName);
    }

    /// <summary>
    /// ロードされたシーンを現在のアクティブシーンにマージする
    /// </summary>
    void MergeLoadedScene(string sceneName)
    {
        // ロードされたシーンの参照を取得
        Scene newlyLoadedScene = SceneManager.GetSceneByName(sceneName);

        // シーンが有効かつロードされているか確認
        if (newlyLoadedScene.IsValid() && newlyLoadedScene.isLoaded)
        {
            // マージ先となるアクティブなシーンを取得
            Scene destinationScene = SceneManager.GetActiveScene(); 

            if (!destinationScene.IsValid())
            {
                Debug.LogError("マージ先となるアクティブシーンが有効ではありません。");
                return;
            }

            // シーンのマージを実行
            // newlyLoadedScene のすべてのルートオブジェクトが destinationScene に移動する
            SceneManager.MergeScenes(newlyLoadedScene, destinationScene);

            Debug.Log($"✅ シーン '{sceneName}' のオブジェクトは '{destinationScene.name}' に正常にマージされました。");
        }
        else
        {
            Debug.LogError($"❌ 新しくロードされたシーン '{sceneName}' が見つからないか、有効ではありませんでした。");
        }
    }
}
