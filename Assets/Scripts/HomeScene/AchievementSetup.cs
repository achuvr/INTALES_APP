using System;
using Firebase.Storage;
using UnityEngine.Networking; // 画像ダウンロードに必要
using System.Threading.Tasks;
using UnityEngine.UI; // UIに適用するために必要
using UnityEngine;
using TMPro;

public class AchievementSetup : MonoBehaviour
{

    [SerializeField] private Transform _layoutParent;
    [SerializeField] private GameObject _achievementPrefab;
    [SerializeField] private GameObject _skillPanel;
    
    private FirebaseStorage _storage;

    private void OnEnable()
    {
        SetUp();
    }


    private async void SetUp()
    {
        _storage = FirebaseStorage.DefaultInstance;
        
        // 発動できるアチーブメントスキルがなければ飛ばされる
        bool canUse = false;
        foreach (var achievement in FirebaseDataReceiver.instance.AchievementList)
        {
            if (!achievement.IsAuto)
            {
                canUse = true;
                break;
            }
        }
        if (!canUse)
        {
            _skillPanel.SetActive(true);
            gameObject.SetActive(false);
            return;
        }
        
        foreach (var achievement in FirebaseDataReceiver.instance.AchievementList)
        {
            if (!achievement.IsAuto)
            {
                var instance = Instantiate(_achievementPrefab, _layoutParent);
                foreach (Transform child in instance.transform)
                {
                    if (child.name.IndexOf("Text") != -1)
                    {
                        child.GetComponent<TextMeshProUGUI>().text = achievement.Description;
                        continue;
                    }

                    if (child.name.IndexOf("Image") != -1)
                    {
                        try
                        {
                            StorageReference imageRef = _storage.GetReferenceFromUrl(achievement.ImageUrl);
                            Task<System.Uri> getUrlTask = imageRef.GetDownloadUrlAsync();
                            System.Uri downloadUrl = await getUrlTask;
                            Debug.Log("ダウンロードURL取得成功: " + downloadUrl.AbsoluteUri);
                            
                            using (UnityWebRequest www = UnityWebRequestTexture.GetTexture(downloadUrl))
                            {
                                // ダウンロードが完了するまで待機
                                var operation = www.SendWebRequest();
                                while (!operation.isDone)
                                {
                                    await Task.Yield(); // 毎フレーム待機
                                }

                                // エラーチェック
                                if (www.result != UnityWebRequest.Result.Success)
                                {
                                    Debug.LogError("画像のダウンロードに失敗しました: " + www.error);
                                    return;
                                }

                                // ダウンロードしたTexture2Dを取得
                                Texture2D texture = DownloadHandlerTexture.GetContent(www);
                

                                // =======================================================
                                // 5. (3/3) Spriteへの変換とUIへの適用
                                // =======================================================
                                // Texture2DをSpriteに変換
                                Sprite sprite = Sprite.Create(
                                    texture, 
                                    new Rect(0, 0, texture.width, texture.height), // 画像全体
                                    Vector2.one * 0.5f // ピボットを中央に設定
                                );

                                // UIのImageコンポーネントにSpriteを適用
                                child.GetComponent<UnityEngine.UI.Image>().sprite = sprite;
                                Debug.Log("画像のダウンロードとSpriteへの変換・適用が完了しました。");
                            }
                        }
                        catch (System.Exception e)
                        {
                            Debug.LogError($"画像処理中にエラーが発生しました: {e.Message}");
                        }
                    }
                }
            }
        }
    }
}
