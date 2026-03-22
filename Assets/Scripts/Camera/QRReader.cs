using System;
using Cysharp.Threading.Tasks;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Firebase.Firestore;
using Firebase.Extensions;

public class QRReader : MonoBehaviour
{
    private string _result = "";
    private WebCamTexture _webCam;
    private CallMethodFromQR _callMethodFromQR;
    [SerializeField] private GameObject _rawImageObject;

    private bool _isOnce = false;
    
    void Awake()
    {
        _callMethodFromQR = GetComponent<CallMethodFromQR>();
        ReadyWebCamAsync().Forget();
    }

    void Update()
    {
        if (_webCam.isPlaying)
        {
            if ((_webCam != null && QRCodeHelper.TryRead(_webCam, out var result)) && !_isOnce)
            {
                _isOnce = true;
                _result = result;
                BranchText();
            }
            else
            {
                _result = "";
            }
        }
    }

    private async void BranchText()
    {
        switch (_result) 
        {
            case "Lv+1":
                _callMethodFromQR.LevelUp(1);
                break;
            case "Lv+2":
                _callMethodFromQR.LevelUp(2);
                break;
            case "Lv+3":
                _callMethodFromQR.LevelUp(3);
                break;
            case "Lv+4":
                _callMethodFromQR.LevelUp(4);
                break;
            case "Lv+5":
                _callMethodFromQR.LevelUp(5);
                break;
            case "Lv+6":
                _callMethodFromQR.LevelUp(6);
                break;
            
            case "drink":
                _callMethodFromQR.Drink();
                break;
            
            case "atk":
                _callMethodFromQR.Atk();
                break;
            
            case "5":
                _callMethodFromQR.Five();
                break;
            case "7":
                _callMethodFromQR.Seven();
                break;
            
            case "NewCharacter":
                _callMethodFromQR.NewCharacter();
                break;
            
            case "coffee":
                _callMethodFromQR.Coffee();
                break;
            
            default:
                Debug.Log("Not Found QR Code");
                break;
        }
        
        var db = FirebaseFirestore.DefaultInstance;
        var uid = UserDataManager.instance.UID;
        var docRef = db.Collection("users").Document(uid);
        Dictionary<string, object> settings = new Dictionary<string, object>
        {
            { "lastDate", Timestamp.GetCurrentTimestamp() }
        };
        await docRef.SetAsync(settings, SetOptions.MergeAll);
    }
    
    private IEnumerator LoadSceneAsyncWithActivationControl()
    {
        Debug.Log("LoadScene Home");
        AsyncOperation asyncLoad = SceneManager.LoadSceneAsync("Home");
        asyncLoad.allowSceneActivation = false; 

        while (asyncLoad.progress < 0.9f) // ロード処理が9割完了するまで待機
        {
            float progress = Mathf.Clamp01(asyncLoad.progress / 0.9f);
            yield return null;
        }
        yield return new WaitForSeconds(0.1f); 
        asyncLoad.allowSceneActivation = true;
    }

    private void OnDisable()
    {
        Debug.Log("Camera Off");
        _webCam.Stop();
        _webCam = null;
    }

    private async UniTask ReadyWebCamAsync()
    {
        await Application.RequestUserAuthorization(UserAuthorization.WebCam);

        if (Application.HasUserAuthorization(UserAuthorization.WebCam) == false)
        {
            return;
        }

        WebCamDevice[] devices = WebCamTexture.devices;

        if (devices == null || devices.Length == 0)
        {
            return;
        }

        Debug.Log(devices[0].name);
        _webCam = new WebCamTexture(devices[0].name, Screen.width, Screen.height, 12);
        _rawImageObject.GetComponent<RawImage>().texture = _webCam;
        _webCam.Play();
        _rawImageObject.GetComponent<AspectRatioFitter>().aspectRatio = (float)_webCam.width / _webCam.height;
    }

}