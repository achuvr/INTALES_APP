#if UNITY_EDITOR_OSX
using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;
using Debug = UnityEngine.Debug;

public static class PodInstallPostBuild
{
    private const string RubyVersion = "2.7.3";

    private static Process s_currentProcess;
    private static string s_workingDirectory;

    // EDM4U 等が実行された後に走らせるため大きめの優先度を指定
    [PostProcessBuild(9999)]
    public static void OnPostProcessBuild(BuildTarget target, string pathToBuiltProject)
    {
        if (target != BuildTarget.iOS)
        {
            return;
        }

        var podfilePath = Path.Combine(pathToBuiltProject, "Podfile");
        if (!File.Exists(podfilePath))
        {
            Debug.LogWarning($"[PodInstallPostBuild] Podfile が見つからないためスキップします: {podfilePath}");
            return;
        }

        if (s_currentProcess != null && !s_currentProcess.HasExited)
        {
            Debug.LogWarning("[PodInstallPostBuild] 前回の pod install がまだ実行中です。完了を待ってから再ビルドしてください。");
            return;
        }

        Debug.Log($"[PodInstallPostBuild] iOS ビルド完了。バックグラウンドで rbenv shell {RubyVersion} && pod install を実行します。\nWorking dir: {pathToBuiltProject}");
        StartPodInstall(pathToBuiltProject);
    }

    private static void StartPodInstall(string workingDirectory)
    {
        // .zshrc に依存せず rbenv を確実に初期化してから rbenv shell + pod install を実行する
        // Homebrew (Apple Silicon/Intel) と rbenv 直接インストールの両方に対応するため PATH を明示追加
        // Unity から起動した非対話シェルは locale が ASCII-8BIT になり
        // CocoaPods の Unicode 正規化 (Encoding::CompatibilityError) で落ちるため UTF-8 を明示
        var script =
            "export LANG=en_US.UTF-8 LC_ALL=en_US.UTF-8 && " +
            "export PATH=\"/opt/homebrew/bin:/usr/local/bin:$HOME/.rbenv/bin:$PATH\" && " +
            "eval \"$(rbenv init - zsh)\" && " +
            $"cd \"{workingDirectory}\" && " +
            $"rbenv shell {RubyVersion} && " +
            "pod install";

        var startInfo = new ProcessStartInfo
        {
            FileName = "/bin/zsh",
            Arguments = $"-c {EscapeForShell(script)}",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory,
        };

        try
        {
            var process = Process.Start(startInfo);
            if (process == null)
            {
                Debug.LogError("[PodInstallPostBuild] zsh プロセスの起動に失敗しました。");
                return;
            }

            s_currentProcess = process;
            s_workingDirectory = workingDirectory;

            process.EnableRaisingEvents = true;
            process.OutputDataReceived += OnOutputDataReceived;
            process.ErrorDataReceived += OnErrorDataReceived;
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            EditorApplication.update += MonitorProcess;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[PodInstallPostBuild] pod install の起動中に例外が発生しました: {ex.Message}\n{ex.StackTrace}");
        }
    }

    private static void OnOutputDataReceived(object sender, DataReceivedEventArgs e)
    {
        if (!string.IsNullOrEmpty(e.Data))
        {
            Debug.Log($"[pod install] {e.Data}");
        }
    }

    private static void OnErrorDataReceived(object sender, DataReceivedEventArgs e)
    {
        if (!string.IsNullOrEmpty(e.Data))
        {
            Debug.LogWarning($"[pod install:stderr] {e.Data}");
        }
    }

    private static void MonitorProcess()
    {
        if (s_currentProcess == null)
        {
            EditorApplication.update -= MonitorProcess;
            return;
        }

        if (!s_currentProcess.HasExited)
        {
            return;
        }

        EditorApplication.update -= MonitorProcess;

        var exitCode = s_currentProcess.ExitCode;
        var workingDirectory = s_workingDirectory;

        s_currentProcess.OutputDataReceived -= OnOutputDataReceived;
        s_currentProcess.ErrorDataReceived -= OnErrorDataReceived;
        s_currentProcess.Dispose();
        s_currentProcess = null;
        s_workingDirectory = null;

        if (exitCode == 0)
        {
            Debug.Log("[PodInstallPostBuild] pod install が正常に完了しました。");
            OpenXcworkspace(workingDirectory);
        }
        else
        {
            Debug.LogError($"[PodInstallPostBuild] pod install がエラーで終了しました (ExitCode: {exitCode})");
        }
    }

    private static void OpenXcworkspace(string workingDirectory)
    {
        var workspacePath = Path.Combine(workingDirectory, "Unity-iPhone.xcworkspace");
        if (!Directory.Exists(workspacePath))
        {
            Debug.LogWarning($"[PodInstallPostBuild] Unity-iPhone.xcworkspace が見つかりません: {workspacePath}");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "/usr/bin/open",
                Arguments = $"\"{workspacePath}\"",
                UseShellExecute = false,
            });
            Debug.Log($"[PodInstallPostBuild] Unity-iPhone.xcworkspace を Xcode で開きました: {workspacePath}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[PodInstallPostBuild] Unity-iPhone.xcworkspace のオープンに失敗しました: {ex.Message}");
        }
    }

    private static string EscapeForShell(string value)
    {
        return "'" + value.Replace("'", "'\\''") + "'";
    }
}
#endif
