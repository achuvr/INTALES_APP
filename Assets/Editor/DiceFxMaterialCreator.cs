#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// 3Dダイス（Dice3DSimulator）が実行時に使うシェーダーをビルドに含めるため、
/// それらを参照するマテリアルアセットを Assets/Resources/DiceFX/ に自動生成する。
///
/// 背景: 実行時にコードだけで作るマテリアルはビルドの依存解析に乗らず、
/// シェーダーがビルドから除外されて実機で Shader.Find が null になる。
/// Resources フォルダのアセットは必ずビルドに含まれるため、ここに置いた
/// マテリアル経由でシェーダーを確実に同梱する。
/// </summary>
public static class DiceFxMaterialCreator
{
    private const string DIR = "Assets/Resources/DiceFX";

    [InitializeOnLoadMethod]
    private static void EnsureMaterials()
    {
        // エディタ起動・コンパイルのたびに不足分だけ作る
        EditorApplication.delayCall += () =>
        {
            bool created = false;
            created |= CreateIfMissing("DiceLit",
                "Universal Render Pipeline/Lit", "Standard");
            created |= CreateIfMissing("DiceFx",
                "Legacy Shaders/Particles/Additive", "Universal Render Pipeline/Particles/Unlit", "Sprites/Default");
            created |= CreateIfMissing("DiceSprite",
                "Sprites/Default");

            if (created)
            {
                AssetDatabase.SaveAssets();
                Debug.Log("[DiceFxMaterialCreator] DiceFX用マテリアルを生成しました（実機ビルドにシェーダーを同梱するため）");
            }
        };
    }

    private static bool CreateIfMissing(string name, params string[] shaderNames)
    {
        string path = $"{DIR}/{name}.mat";
        if (AssetDatabase.LoadAssetAtPath<Material>(path) != null) return false;

        Shader shader = null;
        foreach (var sn in shaderNames)
        {
            shader = Shader.Find(sn);
            if (shader != null) break;
        }
        if (shader == null)
        {
            Debug.LogError($"[DiceFxMaterialCreator] シェーダーが見つかりません: {string.Join(", ", shaderNames)}");
            return false;
        }

        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            AssetDatabase.CreateFolder("Assets", "Resources");
        if (!AssetDatabase.IsValidFolder(DIR))
            AssetDatabase.CreateFolder("Assets/Resources", "DiceFX");

        var mat = new Material(shader) { name = name };
        AssetDatabase.CreateAsset(mat, path);
        return true;
    }
}
#endif
