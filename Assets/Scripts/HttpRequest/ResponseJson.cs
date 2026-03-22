using SimpleJSON;
using UnityEngine;
using System.Collections.Generic;

namespace YokeijoAssets
{
    [System.Serializable]
    public class ResponseJson
    {

        private string _json;
        public string Json => _json;
        
        [SerializeField] private string _update;
        public string Update => _update;
        [SerializeField] private string _replace;
        public string Replace => _replace;
        [SerializeField] private string _delete;
        [SerializeField] private string _cache;
        [SerializeField] private List<string> _appVer;
        [SerializeField] private int _dataVer;
        [SerializeField] private string _error;

        private const string RESPONSE = "response";

        [SerializeField] private ResponseType _responseType;
        public ResponseType ThisResponseType => _responseType;
        
        public ResponseJson(string resJson)
        {
            var jsonNode = JSONNode.Parse(resJson);
            foreach (var node in jsonNode[RESPONSE])
            {
                switch (node.Key)
                {
                    case "update":
                        _update = node.Value.ToString();
                        if (_update.IndexOf("user") != -1)
                        {
                            _responseType = ResponseType.Update;
                        }
                        break;
                    
                    case "replace":
                        _replace = node.Value.ToString();
                        if (_replace.IndexOf("user") != -1)
                        {
                            _responseType = ResponseType.Replace;
                        }
                        break;
                    
                    case "delete":
                        _delete = node.Value.ToString();
                        if (_delete.IndexOf("user") != -1)
                        {
                            _responseType = ResponseType.Delete;
                        }
                        break;
                    
                    case "cache":
                        _cache = node.Value.ToString();
                        break;
                    
                    case "appVer":
                        _appVer = node.Value;
                        break;
                    
                    case "dataVer":
                        _dataVer = int.Parse(node.Value);
                        break;
                    
                    case "error":
                        _error = node.Value.ToString();
                        if (_delete.IndexOf("title") != -1)
                        {
                            _responseType = ResponseType.Error;
                        }
                        break;
                }
            }
        }
    }
}
