#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using TMPro;
using System.Linq;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

public static class AddCharacterButtons2
{
    [MenuItem("Tools/Setup Character Buttons 2x2")]
    public static void Execute()
    {
        // 非アクティブを含む全GameObjectから名前で検索
        var all = Resources.FindObjectsOfTypeAll<GameObject>();
        var bag     = all.FirstOrDefault(g => g.name == "Button_OpenBag"     && g.scene.IsValid());
        var profile = all.FirstOrDefault(g => g.name == "Button_OpenProfile" && g.scene.IsValid());

        if (bag == null || profile == null)
        {
            Debug.LogError("[Setup] Button_OpenBag or Button_OpenProfile not found!");
            return;
        }

        var parent = bag.transform.parent;
        Debug.Log($"[Setup] Parent: {parent.name}");

        // ===== 既存ボタンを上段に移動 =====
        // 上段: y = -690（既存2つ）
        var bagRT = bag.GetComponent<RectTransform>();
        bagRT.anchoredPosition = new Vector2(-221.23f, -690f);
        EditorUtility.SetDirty(bag);

        var profileRT = profile.GetComponent<RectTransform>();
        profileRT.anchoredPosition = new Vector2(221.23f, -690f);
        EditorUtility.SetDirty(profile);

        Debug.Log($"[Setup] Moved existing buttons to y=-690");

        // 日本語フォント取得
        var fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
        var jpFont = fonts.FirstOrDefault(f => f.name.ToLower() == "jp")
                  ?? fonts.FirstOrDefault();

        // ===== 既存の戦闘・装備ボタンがあれば削除して作り直し =====
        var oldBattle = all.FirstOrDefault(g => g.name == "Button_Battle"    && g.scene.IsValid());
        var oldEquip  = all.FirstOrDefault(g => g.name == "Button_Equipment" && g.scene.IsValid());
        if (oldBattle  != null) { Object.DestroyImmediate(oldBattle);  Debug.Log("[Setup] Removed old Button_Battle"); }
        if (oldEquip   != null) { Object.DestroyImmediate(oldEquip);   Debug.Log("[Setup] Removed old Button_Equipment"); }

        // ===== 下段ボタンを作成 =====
        // 下段: y = -840
        CreateButton(parent, bag, "Button_Battle",    "戦闘", -221.23f, -840f, jpFont);
        CreateButton(parent, bag, "Button_Equipment", "装備",  221.23f, -840f, jpFont);

        Debug.Log("[Setup] Created Button_Battle and Button_Equipment at y=-840");

        // シーンをダーティに
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log("[Setup] Done! 2x2 layout complete.");
    }

    static void CreateButton(Transform parent, GameObject template,
        string goName, string label, float x, float y, TMP_FontAsset jpFont)
    {
        var go = Object.Instantiate(template, parent);
        go.name = goName;
        go.SetActive(true);

        var rt = go.GetComponent<RectTransform>();
        rt.anchoredPosition = new Vector2(x, y);

        // テキストを差し替え
        var txt = go.GetComponentInChildren<TextMeshProUGUI>();
        if (txt != null)
        {
            txt.text = label;
            if (jpFont != null) txt.font = jpFont;
        }

        // onClickをクリア
        var b = go.GetComponent<Button>();
        if (b != null) b.onClick.RemoveAllListeners();

        EditorUtility.SetDirty(go);
        Debug.Log($"[Setup] Created {goName} at ({x}, {y})");
    }
}
#endif