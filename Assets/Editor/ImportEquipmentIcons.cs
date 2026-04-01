#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public static class ImportEquipmentIcons
{
    [MenuItem("Tools/Import Equipment Icons As Sprites")]
    public static void Execute()
    {
        string[] paths = {
            "Assets/Resources/EquipmentIcons/icon_weapon.png",
            "Assets/Resources/EquipmentIcons/icon_head.png",
            "Assets/Resources/EquipmentIcons/icon_body.png",
            "Assets/Resources/EquipmentIcons/icon_feet.png",
            "Assets/Resources/EquipmentIcons/icon_skilla.png",
            "Assets/Resources/EquipmentIcons/icon_skillb.png",
        };
        foreach (var path in paths)
        {
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp == null) { Debug.LogWarning("Not found: " + path); continue; }
            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Single;
            imp.alphaIsTransparency = true;
            imp.filterMode = FilterMode.Bilinear;
            imp.mipmapEnabled = false;
            EditorUtility.SetDirty(imp);
            imp.SaveAndReimport();
            Debug.Log("Imported: " + path);
        }
        AssetDatabase.Refresh();
        Debug.Log("[ImportEquipmentIcons] Done!");
    }
}
#endif