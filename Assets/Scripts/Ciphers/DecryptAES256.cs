using System.IO;
using System.Text;
using System.Security.Cryptography;

namespace YokeijoAssets
{
    public static class DecryptAES256
    {
        
        private const int BLOCK_SIZE = 128;
        private const int AES = 256;
        
        public static string Decrypt(string text)
        {
            // 初期化ベクトル半角32文字"
            const string AES_IV_256 = @"intd#s@ksm!ksi!i";
            // 暗号化鍵半角32文字
            const string AES_Key_256 = @"intdid#33=dicmirirofkwikdi11lakd";
            
            using (RijndaelManaged rijndael = new RijndaelManaged())
            {
                // ブロックサイズ（何文字単位で処理するか）
                rijndael.BlockSize = BLOCK_SIZE;
                // 暗号化方式はAES-256を採用
                rijndael.KeySize = AES;
                // 暗号利用モード
                rijndael.Mode = CipherMode.CBC;
                // パディング
                rijndael.Padding = PaddingMode.PKCS7;

                rijndael.IV = Encoding.UTF8.GetBytes(AES_IV_256);
                rijndael.Key = Encoding.UTF8.GetBytes(AES_Key_256);

                ICryptoTransform decryptor = rijndael.CreateDecryptor(rijndael.Key, rijndael.IV);

                using (MemoryStream mStream = new MemoryStream(System.Convert.FromBase64String(text)))
                {
                    using (CryptoStream ctStream = new CryptoStream(mStream, decryptor, CryptoStreamMode.Read))
                    {
                        using (StreamReader sr = new StreamReader(ctStream))
                        {
                            return sr.ReadLine();
                        }
                    }
                }
            }
        }
    }
}
