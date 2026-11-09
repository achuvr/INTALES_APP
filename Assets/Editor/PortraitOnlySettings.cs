using UnityEditor;
using UnityEngine;

/// <summary>
/// 画面は縦向きだけにする（2026-11-09 ユーザー指定。iPad を横に持っても横画面にしない）。
/// 画面は縦長の前提で組み立てていて、横画面や途中での回転には対応していないため。
/// 上下逆さの縦は許可する（iPad は逆さに持たれることが多い。180°回っても画面の縦横は変わらないので崩れない）。
///
/// Player Settings の「Resolution and Presentation → Orientation」と同じ設定を、エディタの起動・再コンパイルのたびに確かめ、
/// 違っていたら直す（エディタを開いたまま ProjectSettings.asset を書き換えても、エディタが持っている古い値で
/// 上書きされないようにするため）。横向きも使うようにするときは、このファイルを消してから Player Settings で変える。
/// </summary>
[InitializeOnLoad]
internal static class PortraitOnlySettings
{
    static PortraitOnlySettings()
    {
        EditorApplication.delayCall += Apply;
    }

    private static void Apply()
    {
        bool changed = false;
        if (PlayerSettings.defaultInterfaceOrientation != UIOrientation.AutoRotation)
        {
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation; // 下の許可した向きの中でだけ回る
            changed = true;
        }
        if (!PlayerSettings.allowedAutorotateToPortrait)
        {
            PlayerSettings.allowedAutorotateToPortrait = true;
            changed = true;
        }
        if (!PlayerSettings.allowedAutorotateToPortraitUpsideDown)
        {
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = true;
            changed = true;
        }
        if (PlayerSettings.allowedAutorotateToLandscapeLeft)
        {
            PlayerSettings.allowedAutorotateToLandscapeLeft = false;
            changed = true;
        }
        if (PlayerSettings.allowedAutorotateToLandscapeRight)
        {
            PlayerSettings.allowedAutorotateToLandscapeRight = false;
            changed = true;
        }
        if (changed) Debug.Log("[PortraitOnlySettings] 画面の向きを縦だけ（上下逆さを含む）に直しました");
    }
}
