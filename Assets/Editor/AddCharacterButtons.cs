#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using TMPro;
using System.Linq;

public static class AddCharacterButtons
{
    [MenuItem("Tools/Add Battle And Equipment Buttons")]
    public static void Execute()
    {
        // 既存ボタンを参照
        var bag     = GameObject.Find("Button_OpenBag");
        var profile = GameObject.Find("Button_OpenProfile");
        if (bag == null || profile == null)
        {
            Debug.LogError("Button_OpenBag or Button_OpenProfile not found!");
            return;
        }

        var parent = bag.transform.parent;

        // 既存ボタンを上段（y=-700）に移動
        var bagRT = bag.GetComponent<RectTransform>();
        bagRT.anchoredPosition = new Vector2(-221.23f, -700f);

        var profileRT = profile.GetComponent<RectTransform>();
        profileRT.anchoredPosition = new Vector2(221.23f, -700f);

        // 日本語フォントを取得
        var fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
        var jpFont = fonts.FirstOrDefault(f => f.name.ToLower() == "jp")
                  ?? fonts.FirstOrDefault();

        // 既存ボタンのテキストにもフォント適用
        foreach (var go in new[] { bag, profile })
        {
            var txt = go.GetComponentInChildren<TextMeshProUGUI>();
            if (txt != null && jpFont != null) txt.font = jpFont;
        }

        // 下段ボタン2つを作成（既存をコピー）
        CreateButton(parent, bag, "Button_Battle",    "戦闘", -221.23f, -840f, jpFont);
        CreateButton(parent, bag, "Button_Equipment", "装備",  221.23f, -840f, jpFont);

        // シーン保存
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        Debug.Log("[AddCharacterButtons] Done! 戦闘・装備ボタンを追加しました。");
    }

    static void CreateButton(Transform parent, GameObject template,
        string name, string label, float x, float y, TMP_FontAsset jpFont)
    {
        // 既存ボタンを複製
        var go = Object.Instantiate(template, parent);
        go.name = name;

        // 位置を設定
        var rt = go.GetComponent<RectTransform>();
        rt.anchoredPosition = new Vector2(x, y);

        // テキストを書き換え
        var txt = go.GetComponentInChildren<TextMeshProUGUI>();
        if (txt != null)
        {
            txt.text = label;
            if (jpFont != null) txt.font = jpFont;
        }

        // ButtonのonClickをクリア（未実装なので空にしておく）
        var btn = go.GetComponent<Button>();
        if (btn != null) btn.onClick.RemoveAllListeners();

        go.SetActive(true);
    }
}
#endif