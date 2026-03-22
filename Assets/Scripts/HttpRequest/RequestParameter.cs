using UnityEngine;

namespace YokeijoAssets
{
    [System.Serializable]
    public class RequestParameter
    {
        [SerializeField] private string _key;
        [SerializeField] private string _value;
        public string Key => _key;
        public string Value => _value;

        public RequestParameter(string key, string value)
        {
            _key = key;
            _value = value;
        }
    }
}