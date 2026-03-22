using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace YokeijoAssets
{
    public class ResponseLog : MonoBehaviour
    {

        [SerializeField] private List<ResponseJson> _responseJsonLogList = new List<ResponseJson>();
        public List<ResponseJson> ResponseJsons => _responseJsonLogList;
        
        public void SetResponseJson(ResponseJson response)
        {
            _responseJsonLogList.Add(response);
        }

        public void SetResponseFromHttpMethod(HttpRequest request)
        {
            _responseJsonLogList.Add(new ResponseJson(request.ResponseText));
        }

        public ResponseJson GetLastResponseJson()
        {
            return _responseJsonLogList[_responseJsonLogList.Count - 1];
        }
    }
}