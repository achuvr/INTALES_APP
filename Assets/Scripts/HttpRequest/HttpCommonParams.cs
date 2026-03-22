using System.Collections.Generic;
using UnityEngine;

namespace YokeijoAssets
{
    [System.Serializable]
    public class HttpCommonParams
    {
        [SerializeField] protected string userId;
        [SerializeField] protected string authCode;
        [SerializeField] protected string accessTime;
        [SerializeField] protected string mstVer;
        
        public string UserId => userId;
        public string AuthCode => authCode;
        public string AccessTime => accessTime;
        public string MstVer => mstVer;
        
        public HttpCommonParams(string userId, string authCode, string accessTime, string mstVer)
        {
            this.userId = userId;
            this.authCode = authCode;
            this.accessTime = accessTime;
            this.mstVer = mstVer;
        }

        public HttpCommonParams()
        {
            this.userId = "1";
            this.authCode = "";
            this.accessTime = "";
            this.mstVer = "1";
        }
    }
}
