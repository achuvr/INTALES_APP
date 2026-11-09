using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Profile;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// お知らせ通知の送信画面（Assets/Scenes/dev/NoticeSender.unity）だけを、Windows 用の exe にビルドする
/// （2026-11-09 ユーザー指定。このパソコンとノートパソコンの2台で使う）。メニュー「Build/通知の送信画面（Windows）」。
///
/// ・出力先はプロジェクト直下の Builds/NoticeSender（.gitignore 済み）。フォルダごと使うパソコンへコピーする
/// ・いったん Windows に切り替えてビルドし、終わったら元のビルド先（Android のビルドプロファイルなど）へ戻す
/// ・ビルドの間だけ、Windows 向けを Mono・ウィンドウ表示（1600×900・大きさ変更可）にして、終わったら元に戻す
///   （このパソコンには Windows 用の IL2CPP が入っていない。どれもスマホのアプリには関係しない設定）
/// ・鍵ファイルはビルドに入れない（使うパソコンで exe の隣か C:\Users\(名前)\.secrets に置く。FcmNotice）
/// </summary>
internal static class NoticeSenderBuild
{
    private const string SCENE = "Assets/Scenes/dev/NoticeSender.unity";
    private const string JP_FONT_GUID = "79feebe53caadef44ac669339d7494fa"; // Assets/TextMesh Pro/Fonts/jp.asset
    private const string EXE = "NoticeSender.exe";
    private const string README = "使い方.txt";

    private static string OutDir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "NoticeSender"));

    [MenuItem("Build/通知の送信画面（Windows）")]
    private static void Build()
    {
        if (!EditorUtility.DisplayDialog("通知の送信画面を Windows 用にビルド",
                "いったん Windows に切り替えてビルドし、終わったら元のビルド先に戻します。\n" +
                "切り替えのたびに読み込み直しが入るので、少し時間がかかります。\n\n出力先: " + OutDir,
                "ビルドする", "やめる"))
            return;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        if (!File.ReadAllText(SCENE).Contains(JP_FONT_GUID))
        {
            EditorUtility.DisplayDialog("ビルドできません",
                "送信シーンに日本語フォント（jp）の参照がありません。\n" +
                "NoticeSenderRoot の Jp Font に jp を入れて、シーンを保存してください。", "OK");
            return;
        }

        var prevTarget = EditorUserBuildSettings.activeBuildTarget;
        var prevGroup = BuildPipeline.GetBuildTargetGroup(prevTarget);
        var prevProfile = BuildProfile.GetActiveBuildProfile();
        var standalone = NamedBuildTarget.Standalone;
        var prevBackend = PlayerSettings.GetScriptingBackend(standalone);
        var prevMode = PlayerSettings.fullScreenMode;
        int prevW = PlayerSettings.defaultScreenWidth, prevH = PlayerSettings.defaultScreenHeight;
        bool prevResizable = PlayerSettings.resizableWindow;

        string exePath = Path.Combine(OutDir, EXE);
        BuildSummary summary;
        try
        {
            PlayerSettings.SetScriptingBackend(standalone, ScriptingImplementation.Mono2x);
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.resizableWindow = true;
            if (prevTarget != BuildTarget.StandaloneWindows64 || prevProfile != null)
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64);

            summary = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { SCENE },
                locationPathName = exePath,
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None,
            }).summary;
        }
        finally
        {
            PlayerSettings.SetScriptingBackend(standalone, prevBackend);
            PlayerSettings.fullScreenMode = prevMode;
            PlayerSettings.defaultScreenWidth = prevW;
            PlayerSettings.defaultScreenHeight = prevH;
            PlayerSettings.resizableWindow = prevResizable;
            bool restored = false;
            if (prevProfile != null)
            {
                try
                {
                    BuildProfile.SetActiveBuildProfile(prevProfile);
                    restored = true;
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[NoticeSenderBuild] ビルドプロファイルを元に戻せませんでした: {ex.Message}");
                }
            }
            if (!restored && EditorUserBuildSettings.activeBuildTarget != prevTarget)
                EditorUserBuildSettings.SwitchActiveBuildTarget(prevGroup, prevTarget);
        }

        if (summary.result != BuildResult.Succeeded)
        {
            Debug.LogError($"[NoticeSenderBuild] ビルドに失敗しました: {summary.result}（エラー {summary.totalErrors} 件）");
            EditorUtility.DisplayDialog("ビルドに失敗しました", "Console に出ているエラーを見てください。", "OK");
            return;
        }

        // 配らなくてよいデバッグ情報のフォルダは消す
        string burst = Path.Combine(OutDir, Path.GetFileNameWithoutExtension(EXE) + "_BurstDebugInformation_DoNotShip");
        if (Directory.Exists(burst)) Directory.Delete(burst, true);
        File.WriteAllText(Path.Combine(OutDir, README), ReadmeText, new UTF8Encoding(true));

        Debug.Log($"[NoticeSenderBuild] できました: {exePath}（{summary.totalSize / (1024 * 1024)} MB）");
        EditorUtility.RevealInFinder(exePath);
        EditorUtility.DisplayDialog("できました",
            "Builds/NoticeSender フォルダができました。\n" +
            "フォルダごとノートパソコンにコピーし、鍵ファイル（.json）を exe の隣か C:\\Users\\(名前)\\.secrets に置いてください。\n" +
            "（くわしくはフォルダの中の「" + README + "」）", "OK");
    }

    private const string ReadmeText = @"INTALES お知らせ通知の送信画面

1. このフォルダごと、使うパソコンにコピーします（NoticeSender.exe だけでは動きません）

2. 通知を送るための鍵ファイル（.json）を、次のどちらかに置きます
     ・この NoticeSender.exe と同じフォルダ
     ・C:\Users\(ユーザー名)\.secrets
   NoticeSender.exe を開いて、画面の左下に「使う鍵: 〜」と出ていれば準備できています

3. タイトルと本文を入れて「送る」を押します
     ・まず「テスト」で送り、スタッフのスマホに届くか確かめてください
     ・「全員」は、アプリを入れているお客様全員に届きます（取り消せません）

鍵ファイルは、ほかの人に渡したり、インターネット上に置いたりしないでください。
";
}
