using UnityEngine;
using UnityEditor;
using System.IO;

/// <summary>
/// QRコード作成EditorWindow
/// </summary>
public class CreateQRCodeWindow : EditorWindow
{
    private enum QRImageSize
    {
        SIZE_128 = 7,
        SIZE_256,
        SIZE_512,
        SIZE_1024,
        SIZE_2048,
        SIZE_4096
    }

    [MenuItem("Window/QR")]
    static void Init()
    {
        var w = GetWindow<CreateQRCodeWindow>();
        w.Show();
    }

    QRImageSize _size = QRImageSize.SIZE_256;
    string _content = "";

    void OnGUI()
    {
        string savePath = Application.dataPath + "/qr.png";
        _content = GUILayout.TextArea(_content, GUILayout.Height(30f));
        _size = (QRImageSize)EditorGUILayout.EnumPopup(_size);
        
        EditorGUI.BeginDisabledGroup(string.IsNullOrEmpty(_content));
        if (GUILayout.Button("Save"))
        {
            int size = (int)Mathf.Pow(2f, (int)_size);
            Debug.Log(size);
            var tex = QRCodeHelper.CreateQRCode(_content, size, size);
            using (var fs = new FileStream(savePath, FileMode.OpenOrCreate))
            {
                var b = tex.EncodeToPNG();
                fs.Write(b, 0, b.Length);
            }

            AssetDatabase.Refresh();
        }
        EditorGUI.EndDisabledGroup();
    }
}
