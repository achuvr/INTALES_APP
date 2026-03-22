using System.IO;
using System.Text;
using System.Security.Cryptography;

public static class EncryptAES256
{
    private const int BLOCK_SIZE = 128;
    private const int AES = 256;

    public static string Encrypt(string text)
    {
        // 初期化ベクトル半角32文字"
        const string AES_IV_256 = @"intd#s@ksm!ksi!i";
        // 暗号化鍵半角32文字
        const string AES_Key_256 = @"intdid#33=dicmirirofkwikdi11lakd";

        RijndaelManaged myRijndael = new RijndaelManaged();
        // ブロックサイズ（何文字単位で処理するか）
        myRijndael.BlockSize = BLOCK_SIZE;
        // 暗号化方式はAES-256を採用
        myRijndael.KeySize = AES;
        // 暗号利用モード
        myRijndael.Mode = CipherMode.CBC;
        // パディング
        myRijndael.Padding = PaddingMode.PKCS7;

        myRijndael.IV = Encoding.UTF8.GetBytes(AES_IV_256);
        myRijndael.Key = Encoding.UTF8.GetBytes(AES_Key_256);

        // 暗号化
        ICryptoTransform encryptor = myRijndael.CreateEncryptor(myRijndael.Key, myRijndael.IV);

        byte[] encrypted;
        using (MemoryStream mStream = new MemoryStream())
        {
            using (CryptoStream ctStream = new CryptoStream(mStream, encryptor, CryptoStreamMode.Write))
            {
                using (StreamWriter sw = new StreamWriter(ctStream))
                {
                    sw.Write(text);
                }

                encrypted = mStream.ToArray();
            }
        }

        // Base64形式（64種類の英数字で表現）で返す
        return System.Convert.ToBase64String(encrypted);
    }
}
