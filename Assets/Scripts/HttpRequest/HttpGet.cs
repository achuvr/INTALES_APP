using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace YokeijoAssets
{
    public class HttpGet : HttpRequest
    {
        
        [SerializeField] private ResponseLog _responseLog;
        
        
        private IEnumerator GetRequest()
        {
            // 受け取ったパラメーターからAuthCodeを作成
            Dictionary<string, string> paramDic = new Dictionary<string, string>();
            foreach (var param in _paramsList)
            {
                paramDic.Add(param.Key, param.Value);
            }
            
            _httpCommonParams = new HttpCommonParams(
                _httpCommonParams.UserId,
                AuthCodeGenerator.GenerateAuthCode(paramDic),
                _httpCommonParams.AccessTime,
                _httpCommonParams.MstVer
            );
            var uriWithParams = $"{_uri}?{AuthCodeGenerator.StrConcatenation}";
            Debug.Log("URI = \n" + uriWithParams);
            
            using (var req = UnityWebRequest.Get(uriWithParams))
            {
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success)
                {
                    Debug.Log(req.error);
                }
                else
                {
                    if (req.downloadHandler != null)
                    {
                        SetRequestParams(req.downloadHandler.text);
                    }
                }
            }
        }

        private void SetRequestParams(string resJson)
        {
            var responseJson = new ResponseJson(resJson);
            if (_responseLog != null)
            {
                _responseLog.SetResponseJson(responseJson);
            }
        }
    }
}