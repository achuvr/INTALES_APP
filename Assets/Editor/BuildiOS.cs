using UnityEditor;
using UnityEngine;

public static class BuildiOS
{
    private const string BUNDLE_IDENTIFIER = "com.intalescafe.intalescafeapp";
    private const string CAMERA_USAGE_DESCRIPTION = "このアプリではカメラを使用します";

    [MenuItem("Build/iOS Build")]
    public static void PerformBuild()
    {
        // Bundle Identifier を設定
        PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.iOS, BUNDLE_IDENTIFIER);

        // Camera Usage Description を設定
        PlayerSettings.iOS.cameraUsageDescription = CAMERA_USAGE_DESCRIPTION;

        // Build Settings に登録されているシーンを取得
        var scenes = new System.Collections.Generic.List<string>();
        foreach (var scene in EditorBuildSettings.scenes)
        {
            if (scene.enabled)
            {
                scenes.Add(scene.path);
            }
        }

        // 出力先
        string outputPath = "Builds/iOS";

        var options = new BuildPlayerOptions
        {
            scenes = scenes.ToArray(),
            locationPathName = outputPath,
            target = BuildTarget.iOS,
            options = BuildOptions.None
        };

        var report = BuildPipeline.BuildPlayer(options);

        if (report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
        {
            Debug.Log($"iOS ビルド成功: {outputPath}");
        }
        else
        {
            Debug.LogError($"iOS ビルド失敗: {report.summary.result}");
        }
    }
}
