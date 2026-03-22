using UnityEngine;
using ZXing;
using ZXing.QrCode;

/// <summary>
/// QRコードの作成と読み込みヘルパークラス
/// </summary>
public static class QRCodeHelper
{
    /// <summary>
    /// テクスチャからQRコード読み取り
    /// </summary>
    public static bool TryRead(Texture2D tex, out string result)
    {
        BarcodeReader reader = new BarcodeReader();
        int w = tex.width;
        int h = tex.height;
        var pixel32s = tex.GetPixels32();
        var r = reader.Decode(pixel32s, w, h);
        return CheckResult(r, out result);
    }

    /// <summary>
    /// WebカメラからQRコード読み取り
    /// </summary>
    public static bool TryRead(WebCamTexture tex, out string result)
    {
        BarcodeReader reader = new BarcodeReader();
        int w = tex.width;
        int h = tex.height;
        var pixel32s = tex.GetPixels32();
        var r = reader.Decode(pixel32s, w, h);
        return CheckResult(r, out result);
    }

    public static Texture2D CreateQRCode(string str, int w, int h)
    {
        var tex = new Texture2D(w, h, TextureFormat.ARGB32, false);
        var content = Write(str, w, h);
        tex.SetPixels32(content);
        tex.Apply();
        return tex;
    }
    
    private static bool CheckResult(Result r, out string result)
    {
        if (r != null)
        {
            result =  r.Text;
            return true;
        }

        result = "error";
        return false;
    }

    private static Color32[] Write(string content, int w, int h)
    {
        Debug.Log(content + " / " + w + " / " + h);

        var writer = new BarcodeWriter
        {
            Format = BarcodeFormat.QR_CODE,
            Options = new QrCodeEncodingOptions
            {
                Width = w, Height = h
            }
        };
        return writer.Write(content);
    }
}
