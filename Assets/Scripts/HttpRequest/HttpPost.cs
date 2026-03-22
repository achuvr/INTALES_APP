using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using UniRx;
using UnityEngine;

namespace YokeijoAssets
{
    public class HttpPost : HttpRequest
    {

        /// <summary>
        /// 第一引数:URI 第ニ引数:パラメータのリスト
        /// </summary>
        /// <param name="uri"></param>
        /// <param name="paramList"></param>
        public HttpPost(string uri, List<RequestParameter> paramList, HttpCommonParams httpCommonParams)
        {
            _uri = uri;
            _paramsList = paramList;
            _httpCommonParams = httpCommonParams;
            
            Dictionary<string, string> paramDic = new Dictionary<string, string>();
            foreach (var param in _paramsList)
            {
                paramDic.Add(param.Key, param.Value);
            }
            
            // 受け取ったパラメーターからAuthCodeを作成
            _httpCommonParams = new HttpCommonParams(
                _httpCommonParams.UserId,
                AuthCodeGenerator.GenerateAuthCode(paramDic),
                UnixTimeUtility.GetUnixTime(DateTime.Now).ToString(),
                _httpCommonParams.MstVer
            );
        }
        

        public async Task<string> Post()
        {
            var httpClient = new HttpClient();
            var targetUri = new Uri(_uri);

            var forms = new Dictionary<string, string>();
            forms.Add("id", _httpCommonParams.UserId);
            forms.Add("authCode", _httpCommonParams.AuthCode);
            forms.Add("accessTime", _httpCommonParams.AccessTime);
            forms.Add("mstVer", _httpCommonParams.MstVer);
            foreach (var param in _paramsList)
            {
                forms.Add(param.Key, param.Value);
            }

            using var content = new FormUrlEncodedContent(forms);
            using var request = new HttpRequestMessage()
            {
                Method = HttpMethod.Post,
                RequestUri = targetUri,
                Content = content,
            };
            request.Version = new Version(2, 0);
            try
            {
                var response = await httpClient.SendAsync(request);
                _responseText = response.Content.ReadAsStringAsync().Result;
                _isSuccess = true;
                return _responseText;
            }
            catch (Exception e)
            {
                Debug.Log(e.ToString());
                _isSuccess = false;
                return e.ToString();
            }
        }
    }
}