using System.Collections.Generic;
using System.IO;
using UnityEngine;
using System.Text;
using System.Linq;

namespace YokeijoAssets
{
    public static class AuthCodeGenerator
    {

        private static string _authCode = "";
        public static string AuthCode => _authCode;
        
        private static string _strConcatenation;
        public static string StrConcatenation => _strConcatenation;
        
        public static string GenerateAuthCode(Dictionary<string, string> requestParams)
        {
            // 文字でソート
            requestParams = requestParams
                .OrderBy(p => p.Key)
                .ToDictionary(p => p.Key, p => p.Value);

            _authCode = GetAuthCode(requestParams, ReadKey());
            return _authCode;
        }

        public static string GenerateAuthCode(Dictionary<string, string> requestParams, string key)
        {
            // 文字でソート
            requestParams = requestParams
                .OrderBy(p => p.Key)
                .ToDictionary(p => p.Key, p => p.Value);
            _authCode = GetAuthCode(requestParams, key);
            return _authCode;
        }
        
        /// <summary>
        /// 渡されたパラメータ群とシークレットキーを1つの文字列にする処理
        /// </summary>
        /// <param name="requestParams">パラメーター</param>
        /// <param name="secretkey">ローカルに保存されているシークレットキー</param>
        /// <returns></returns>
        private static string GetAuthCode(Dictionary<string, string> requestParams, string secretkey)
        {
            int count = 0;
            StringBuilder builder = new StringBuilder();
            
            foreach (var param in requestParams)
            {
                builder.Append(param.Key);
                builder.Append("=");
                builder.Append(param.Value);
                if (count < requestParams.Count - 1)
                {
                    builder.Append("&");
                }
                count++;
            }
            _strConcatenation = builder.ToString();
            // return new EncryptAES256(_strConcatenation + $":{secretkey}").EncryptedText;
            return EncryptAES256.Encrypt(_strConcatenation + $":{secretkey}");
        }
        
        
        /// <summary>
        /// 現状はローカルのkey.txtを読み込む。本実装までには変更する。
        /// </summary>
        /// <returns></returns>
        private static string ReadKey()
        {
            string path = Application.dataPath;
            using (StreamReader reader = new StreamReader(Path.Combine(path, "key.txt"), Encoding.GetEncoding("utf-8")))
            {
                return reader.ReadLine();
            }
        }
    }
}
