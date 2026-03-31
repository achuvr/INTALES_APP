#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using System.Linq;

public static class AssignButtonIconSprites
{
    static readonly (string go, string spritePath)[] Defs =
    {
        ("Button_OpenBag",     "Assets/Textures/Icons/icon_bag.png"),
        ("Button_OpenProfile", "Assets/Textures/Icons/icon_info.png"),
        ("Button_Battle",      "Assets/Textures/Icons/icon_battle.png"),
        ("Button_Equipment",   "Assets/Textures/Icons/icon_equipment.png"),
    };

    [MenuItem("Tools/Assign Button Icon Sprites")]
    public static void Execute()
    {
        // テクスチャのインポート設定を更新（スプライトとして使えるように）
        foreach (var def in Defs)
        {
            var imp = AssetImporter.GetAtPath(def.spritePath) as TextureImporter;
            if (imp == null) { Debug.LogError($"Not found: {def.spritePath}"); continue; }
            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Single;
            imp.alphaIsTransparency = true;
            imp.filterMode = FilterMode.Bilinear;
            imp.mipmapEnabled = false;
            EditorUtility.SetDirty(imp);
            imp.SaveAndReimport();
        }

        AssetDatabase.Refresh();

        // スプライトを各ボタンの__Icon Imageに割り当て
        var allGOs = Resources.FindObjectsOfTypeAll<GameObject>();
        foreach (var def in Defs)
        {
            var go = allGOs.FirstOrDefault(g => g.name == def.go && g.scene.IsValid());
            if (go == null) { Debug.LogError($"[Assign] Not found: {def.go}"); continue; }

            var iconTr = go.transform.Find("__Icon");
            if (iconTr == null) { Debug.LogError($"[Assign] __Icon not found in {def.go}"); continue; }

            var img = iconTr.GetComponent<Image>();
            if (img == null) { Debug.LogError($"[Assign] Image not found in __Icon of {def.go}"); continue; }

            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(def.spritePath);
            if (sprite == null) { Debug.LogError($"[Assign] Sprite not loaded: {def.spritePath}"); continue; }

            img.sprite = sprite;
            img.color = Color.white;   // 色のマスクをリセット（生成画像の色をそのまま使う）
            img.type = Image.Type.Simple;
            img.preserveAspect = true;
            EditorUtility.SetDirty(iconTr.gameObject);
            Debug.Log($"[Assign] {def.go} -> {def.spritePath}");
        }

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log("[Assign] Done! All sprites assigned.");
    }
}
#endif