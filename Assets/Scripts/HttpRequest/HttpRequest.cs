using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace YokeijoAssets
{
    public class HttpRequest
    {
        [SerializeField] protected string _uri;
        public string Uri => _uri;
        [SerializeField] protected HttpCommonParams _httpCommonParams;
        public HttpCommonParams CommonParams => _httpCommonParams;
        [SerializeField] protected List<RequestParameter> _paramsList;
        public List<RequestParameter> ParamList => _paramsList;
        protected bool _isSuccess;
        public bool IsSuccess => _isSuccess;
        
        [SerializeField] protected string _responseText;
        public string ResponseText => _responseText;

        
        public void SetUserId(string id)
        {
            _httpCommonParams = new HttpCommonParams(
                id,
                _httpCommonParams.AuthCode,
                _httpCommonParams.AccessTime,
                _httpCommonParams.MstVer
            );
        }
    }
}
