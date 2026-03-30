using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
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






public static class StyleLoginButtonTool
{
    [MenuItem("Tools/Style Login Button")]
    public static void Apply()
    {
        GameObject[] all = Resources.FindObjectsOfTypeAll<GameObject>();
        GameObject loginPanel = null;
        foreach (var go in all)
        {
            if (go.name == "LOGIN" && go.scene.name == "Start")
            { loginPanel = go; break; }
        }
        if (loginPanel == null) { Debug.LogError("[StyleLogin] LOGIN panel not found"); return; }

        Transform btnT = loginPanel.transform.Find("Button_Login");
        if (btnT == null) { Debug.LogError("[StyleLogin] Button_Login not found"); return; }
        GameObject btn = btnT.gameObject;

        Image img = btn.GetComponent<Image>();
        if (img != null) img.color = new Color(0.18f, 0.22f, 0.45f, 1f);

        Button button = btn.GetComponent<Button>();
        if (button != null)
        {
            ColorBlock cb = button.colors;
            cb.normalColor      = new Color(0.18f, 0.22f, 0.45f, 1f);
            cb.highlightedColor = new Color(0.28f, 0.34f, 0.65f, 1f);
            cb.pressedColor     = new Color(0.10f, 0.13f, 0.30f, 1f);
            cb.selectedColor    = new Color(0.28f, 0.34f, 0.65f, 1f);
            cb.colorMultiplier  = 1.2f;
            cb.fadeDuration     = 0.15f;
            button.colors = cb;
        }

        Transform textT = btn.transform.Find("Text_Login");
        if (textT != null)
        {
            TextMeshProUGUI tmp = textT.GetComponent<TextMeshProUGUI>();
            if (tmp != null)
            {
                tmp.color = new Color(0.98f, 0.88f, 0.5f, 1f);
                tmp.fontSize = 19f;
                tmp.fontStyle = FontStyles.Bold;
                tmp.characterSpacing = 3f;
                tmp.enableVertexGradient = true;
                tmp.colorGradient = new VertexGradient(
                    new Color(1f, 0.95f, 0.6f, 1f),
                    new Color(1f, 0.95f, 0.6f, 1f),
                    new Color(0.85f, 0.72f, 0.3f, 1f),
                    new Color(0.85f, 0.72f, 0.3f, 1f)
                );
            }
        }

        RectTransform rt = btn.GetComponent<RectTransform>();
        if (rt != null) rt.sizeDelta = new Vector2(170f, 36f);

        EditorUtility.SetDirty(btn);
        EditorSceneManager.MarkSceneDirty(btn.scene);
        Debug.Log("[StyleLogin] Button_Login のデザインを変更しました！");
    }
}
