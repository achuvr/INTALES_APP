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

    private RawImage _rawImage;
    private RectTransform _rawRect;

    // 映像の向き・大きさを最後に合わせたときの状態（どれかが変わったときだけ合わせ直す）
    private int _fitAngle = -1, _fitWidth, _fitHeight;
    private bool _fitMirrored;
    private Vector2 _fitArea;

    void Awake()
    {
        _callMethodFromQR = GetComponent<CallMethodFromQR>();
        PrepareCameraImage();
        ReadyWebCamAsync().Forget();
    }

    void Update()
    {
        // カメラの許可待ち・許可なし・カメラなしの間は _webCam が null（毎フレーム例外にならないように）
        if (_webCam != null && _webCam.isPlaying)
        {
            FitCameraImage();

            if (QRCodeHelper.TryRead(_webCam, out var result) && !_isOnce)
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

    private void BranchText()
    {
        // フレンド登録QR（"friend:{uid}:{name}"）は内容が可変なのでswitchの前に判定する
        if (_result.StartsWith(CallMethodFromQR.FRIEND_QR_PREFIX))
        {
            // フレンド機能は 2026-11-09 にやめた（争いの火種になりうるため）。何も書き込まない
            _callMethodFromQR.EndFromButton();
            FriendMenuController.ShowToast("フレンド機能は終了しました");
            return;
        }

        // 紙の会員証引き継ぎQR（"transfer:{コード}"）
        if (_result.StartsWith(CallMethodFromQR.TRANSFER_QR_PREFIX))
        {
            _callMethodFromQR.ClaimTransfer(_result).Forget();
            return;
        }

        // イベントガチャQR（"gacha_event" または "gacha_event:{プールID}"）
        if (_result.StartsWith(CallMethodFromQR.GACHA_EVENT_QR_PREFIX))
        {
            _callMethodFromQR.EventGacha(_result).Forget();
            return;
        }

        // バフカード山札QR（"deck:soft=10,beer=5,..."）
        if (_result.StartsWith(BuffCard.QR_PREFIX))
        {
            _callMethodFromQR.DrawBuffCard(_result);
            return;
        }

        // 1か月遊び放題パスの受け取りQR（"pass:{ID}"。管理者が、購入されたお客様に見せる。1回だけ使える）
        if (_result.StartsWith(StorePasses.QR_PREFIX))
        {
            _callMethodFromQR.ClaimPass(_result).Forget();
            return;
        }

        // 入店・退店のQR（"checkin:{営業日}:{署名}"。お店の端末に出す、1日1回変わるQR）
        if (_result.StartsWith(StoreCheckinQr.PREFIX))
        {
            _callMethodFromQR.ToggleVisitSigned(_result);
            return;
        }

        // ボードゲームの公認QR（"play:{ゲームキー}:{営業日}:{署名}"。スタッフが卓で見せる。その日のうちだけ）
        if (_result.StartsWith(BoardGameCertify.QR_PREFIX))
        {
            _callMethodFromQR.CertifyGame(_result).Forget();
            return;
        }

        // 会員証QR（"member:..."。お客様が所持品から出し、スタッフが読む。お会計用）
        if (_result.StartsWith(MemberCard.PREFIX))
        {
            _callMethodFromQR.ShowMember(_result);
            return;
        }

        // 友だち紹介QR（"invite:{uid}:{名前}"。初めての人が読む）
        if (_result.StartsWith(Referral.QR_PREFIX))
        {
            _callMethodFromQR.AcceptInvite(_result).Forget();
            return;
        }

        // 惑星の解放QR（"planet:{惑星ID}:{署名}"。アチーブメント報酬）
        if (_result.StartsWith(SpacePlanets.QR_PREFIX))
        {
            _callMethodFromQR.UnlockPlanet(_result).Forget();
            return;
        }

        // G（アプリ内の通貨）を渡すQR（"gold:{金額}:{営業日}:{番号}:{署名}"。管理者が金額を決めて見せる。その日だけ・1人1回）
        if (_result.StartsWith(GoldQr.PREFIX))
        {
            _callMethodFromQR.ClaimGold(_result).Forget();
            return;
        }

        // 星座に星を灯すQR（"stars:{星座ID}:{個数}:{営業日}:{番号}:{署名}"。管理者が星座と個数を決めて見せる。
        // その日だけ・1人1回。2026-11-13 StarGiftQr）
        if (_result.StartsWith(StarGiftQr.PREFIX))
        {
            _callMethodFromQR.ClaimStars(_result).Forget();
            return;
        }

        // ミニオンをあげるQR（"minion:{職業}:{属性}:{レベル}:{営業日}:{番号}:{署名}"。管理者が職業・属性・レベルを決めて見せる。
        // その日だけ・1人1回。2026-11-14 MinionGiftQr）
        if (_result.StartsWith(MinionGiftQr.PREFIX))
        {
            _callMethodFromQR.ClaimMinion(_result).Forget();
            return;
        }

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
            
            // 店頭クーポン（新ホームの管理者用QR一覧も同じ文字のQRを表示する）
            case CallMethodFromQR.QR_DRINK_COUPON:
                _callMethodFromQR.Drink();
                break;

            case CallMethodFromQR.QR_ATK_COUPON:
                _callMethodFromQR.Atk();
                break;

            case CallMethodFromQR.QR_FIVE_COUPON:
                _callMethodFromQR.Five();
                break;
            case CallMethodFromQR.QR_SEVEN_COUPON:
                _callMethodFromQR.Seven();
                break;
            
            case "NewCharacter":
                _callMethodFromQR.NewCharacter();
                break;

            case CallMethodFromQR.QR_COFFEE_COUPON:
                _callMethodFromQR.Coffee();
                break;
            case CallMethodFromQR.QR_FREETIME_COUPON:
                _callMethodFromQR.FreeTime();
                break;
            case CallMethodFromQR.QR_FIVEHOUR_COUPON:
                _callMethodFromQR.FiveHour();
                break;

            // 以前の固定の入退店QR。新ホームでは退店だけできる（入店は1日1回変わる入店QRで。ToggleVisit）
            case "checkin":
            case "checkout":
            case "visit":
                _callMethodFromQR.ToggleVisit();
                break;

            default:
                Debug.Log("Not Found QR Code");
                break;
        }

        // lastDate の更新は各アクション（レベルアップ・クーポン入手）の書き込みに
        // 含めるようにしたため、ここでの追加書き込みは行わない。
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
        if (_webCam != null) _webCam.Stop();
        _webCam = null;
    }

    private async UniTask ReadyWebCamAsync()
    {
        await Application.RequestUserAuthorization(UserAuthorization.WebCam);
        if (this == null) return; // 許可を待っている間に画面が閉じられた

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
        _rawImage.texture = _webCam;
        _webCam.Play();
        // 向き・大きさは映像が届いてから FitCameraImage で合わせる（届くまでは幅・高さが 16 のまま）
    }

    /// <summary>
    /// カメラ映像の RawImage を手動で合わせる準備。
    /// シーン上は「-90°固定回転＋AspectRatioFitter」だったが、それだと回転が要らない環境
    /// （エディタのPCカメラなど）で90°傾き、縦横比も回転を考えずに計算されて崩れるため、
    /// フィッターは止めて FitCameraImage で向きと大きさを決める。映像が届くまでは大きさ 0。
    /// </summary>
    private void PrepareCameraImage()
    {
        _rawImage = _rawImageObject.GetComponent<RawImage>();
        _rawRect = _rawImage.rectTransform;

        var fitter = _rawImageObject.GetComponent<AspectRatioFitter>();
        if (fitter != null) fitter.enabled = false;

        _rawRect.anchorMin = _rawRect.anchorMax = new Vector2(0.5f, 0.5f);
        _rawRect.pivot = new Vector2(0.5f, 0.5f);
        _rawRect.anchoredPosition = Vector2.zero;
        _rawRect.sizeDelta = Vector2.zero;
        _rawRect.localEulerAngles = Vector3.zero;
    }

    /// <summary>
    /// 映像を正対させて画面全体を覆う大きさにする（はみ出た分は画面外）。
    /// カメラの生の映像はセンサーの向き（スマホの背面カメラは多くが横向き）なので、
    /// videoRotationAngle（時計回りに回すべき角度）だけ回し、上下反転なら UV を反転する。
    /// 端末を回したとき・解像度が途中で変わったときも合わせ直す。
    /// </summary>
    private void FitCameraImage()
    {
        int width = _webCam.width, height = _webCam.height;
        if (width <= 16 || height <= 16) return; // 最初のフレームが届くまでは 16x16 を返す

        int angle = ((_webCam.videoRotationAngle % 360) + 360) % 360;
        bool mirrored = _webCam.videoVerticallyMirrored;
        var parent = _rawRect.parent as RectTransform;
        Vector2 area = parent != null ? parent.rect.size : new Vector2(Screen.width, Screen.height);

        if (angle == _fitAngle && mirrored == _fitMirrored && width == _fitWidth && height == _fitHeight && area == _fitArea)
            return;
        _fitAngle = angle;
        _fitMirrored = mirrored;
        _fitWidth = width;
        _fitHeight = height;
        _fitArea = area;

        // 90°・270° 回すときは画面上の縦横が入れ替わる
        bool sideways = angle % 180 != 0;
        float shownW = sideways ? height : width;
        float shownH = sideways ? width : height;
        float scale = Mathf.Max(area.x / shownW, area.y / shownH);

        _rawRect.sizeDelta = new Vector2(width * scale, height * scale); // 回す前の大きさ
        _rawRect.localEulerAngles = new Vector3(0f, 0f, -angle);         // UI の z 回転は反時計回りが正
        _rawImage.uvRect = mirrored ? new Rect(0f, 1f, 1f, -1f) : new Rect(0f, 0f, 1f, 1f);
        Debug.Log($"[QRReader] camera {width}x{height} rotation {angle} mirrored {mirrored}");
    }

}