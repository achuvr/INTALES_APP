using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

namespace YokeijoAssets
{
    public class LastRequest : HttpRequest
    {

        private WWWForm _lastForm;
        private HttpRequest _httpRequest;


        public void SetLastRequest(HttpRequest request)
        {
            _httpRequest = request;
        }
        

        private IEnumerator PastRequest()
        {
            using (var webRequest = UnityWebRequest.Post(_uri, _lastForm))
            {
                Debug.Log("Retry..");
                yield return webRequest.SendWebRequest();
                if (webRequest.result != UnityWebRequest.Result.Success)
                {
                    Debug.Log(webRequest.error);
                }
                else
                {
                    if (webRequest.downloadHandler != null)
                    {
                        _responseText = webRequest.downloadHandler.text;
                        // _onPostSuccess.Invoke();
                    }
                }
            }            
        }
    }
}
