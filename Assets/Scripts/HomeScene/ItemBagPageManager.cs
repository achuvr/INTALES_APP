using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Firebase.Firestore;
using Firebase.Extensions;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;

public class ItemBagPageManager : MonoBehaviour
{

    [SerializeField] private TextMeshProUGUI _fiveCouponText;
    [SerializeField] private TextMeshProUGUI _sevenCouponText;
    [SerializeField] private TextMeshProUGUI _drinkCouponText;
    [SerializeField] private TextMeshProUGUI _coffeeCouponText;
    [SerializeField] private TextMeshProUGUI _atkCouponText;

    [SerializeField] private Image _itemImage;
    [SerializeField] private TextMeshProUGUI _explanationText;
    [SerializeField] private TextMeshProUGUI _nameText;
    [SerializeField] private TextMeshProUGUI _numOfUsingCouponText;
    [SerializeField] private int _numOfUsingCoupon = 0;
    
    [SerializeField] private GameObject _couponPanel;

    [Header("ポップアップ設定")]
    [SerializeField] private CanvasGroup _popupCanvasGroup;
    [SerializeField] private TextMeshProUGUI _popupText;
    [SerializeField] private float _popupDisplayDuration = 2f;
    [SerializeField] private float _popupFadeDuration = 0.5f;

    // 5%
    private const string FIVE_COUPON_NAME = "5%OFFチケット";
    private const string FIVE_COUPON_EXPLANATION = "5%オフされるチケット。\n最大20%オフまで可能";
    
    // 7%
    private const string SEVEN_COUPON_NAME = "7%OFFチケット";
    private const string SEVEN_COUPON_EXPLANATION = "7%オフされるチケット。\n最大20%オフまで可能";
    
    // Drink
    private const string DRINK_COUPON_NAME = "ドリンクチケット";
    private const string DRINK_COUPON_EXPLANATION = "500円以下の飲み物の無料チケット。";
    
    // Coffee
    private const string COFFEE_COUPON_NAME = "コーヒーチケット";
    private const string COFFEE_COUPON_EXPLANATION = "コーヒーの無料チケット。";
    
    // ATK
    private const string ATK_COUPON_NAME = "ATK+1チケット";
    private const string ATK_COUPON_EXPLANATION = "ATKが1上がるチケット。";

    private string _currentCouponName = "";
    
    [SerializeField] private GameObject _loadingPanel;
    
    private void Start()
    {
        UpdateCouponDisplay();
        gameObject.SetActive(false);
    }

    /// <summary>
    /// 全クーポンの在庫表示を更新する
    /// </summary>
    private void UpdateCouponDisplay()
    {
        _fiveCouponText.text = UserDataManager.instance.UserData.FiveCoupon.ToString();
        _sevenCouponText.text = UserDataManager.instance.UserData.SevenCoupon.ToString();
        _drinkCouponText.text = UserDataManager.instance.UserData.DrinkCoupon.ToString();
        _coffeeCouponText.text = UserDataManager.instance.UserData.CoffeeCoupon.ToString();
        _atkCouponText.text = UserDataManager.instance.UserData.ATKCoupon.ToString();
    }

    /// <summary>
    /// アイテム使用ポップアップを表示し、2秒後にフェードアウトする
    /// </summary>
    private async UniTaskVoid ShowItemUsedPopup(string itemName, int count)
    {
        _popupText.text = $"{itemName}を{count}枚使いました。";
        _popupCanvasGroup.alpha = 1f;
        _popupCanvasGroup.gameObject.SetActive(true);

        // 表示時間待機
        await UniTask.Delay((int)(_popupDisplayDuration * 1000));

        // フェードアウト
        float elapsed = 0f;
        while (elapsed < _popupFadeDuration)
        {
            elapsed += Time.deltaTime;
            _popupCanvasGroup.alpha = 1f - (elapsed / _popupFadeDuration);
            await UniTask.Yield();
        }

        _popupCanvasGroup.alpha = 0f;
        _popupCanvasGroup.gameObject.SetActive(false);
    }

    public void OnClick_UseCoupon()
    {
        if(_numOfUsingCoupon <= 0) return;
        UseCoupon();
    }
    
    private async UniTask UseCoupon()
    {
        AssetsDatabase.instance.LoadingPanel.SetActive(true);
        var db = FirebaseFirestore.DefaultInstance;
        var uid = UserDataManager.instance.UID;
        CollectionReference charactersRef = db.Collection("users").Document(uid).Collection("characters");
        
        switch (_currentCouponName)
        {
            case "5":
                charactersRef.Limit(1).GetSnapshotAsync().ContinueWithOnMainThread(async task =>
                {
                    if (task.IsFaulted)
                    {
                        Debug.LogError("エラーが発生しました: " + task.Exception);
                        return;
                    }

                    QuerySnapshot snapshot = task.Result;
                    if (snapshot.Count > 0)
                    {
                        Dictionary<string, object> data = new Dictionary<string, object>
                        {
                            {
                                "five_coupon", (UserDataManager.instance.UserData.FiveCoupon - _numOfUsingCoupon)
                            },
                        };
                        
                        var docRef = db.Collection("users").Document(uid);
                        await docRef.SetAsync(data, SetOptions.MergeAll);
                        await UniTask.Delay(1000);

                        // データの更新
                        DocumentSnapshot ds = await docRef.GetSnapshotAsync();
                        var userData = ds.ConvertTo<UserData>();
                        UserDataManager.instance.SetUserData(userData);
                        Debug.Log("5クーポンを消費！");
                        UpdateCouponDisplay();
                        int usedCount = _numOfUsingCoupon;
                        _numOfUsingCoupon = 0;
                        _numOfUsingCouponText.text = "0";
                        AssetsDatabase.instance.LoadingPanel.SetActive(false);
                        _couponPanel.SetActive(false);
                        ShowItemUsedPopup(FIVE_COUPON_NAME, usedCount).Forget();
                    }
                });
                break;

            case "7":
                charactersRef.Limit(1).GetSnapshotAsync().ContinueWithOnMainThread(async task =>
                {
                    if (task.IsFaulted)
                    {
                        Debug.LogError("エラーが発生しました: " + task.Exception);
                        return;
                    }

                    QuerySnapshot snapshot = task.Result;
                    if (snapshot.Count > 0)
                    {
                        Dictionary<string, object> data = new Dictionary<string, object>
                        {
                            {
                                "seven_coupon", (UserDataManager.instance.UserData.SevenCoupon - _numOfUsingCoupon)
                            },
                        };
                        
                        var docRef = db.Collection("users").Document(uid);
                        await docRef.SetAsync(data, SetOptions.MergeAll);
                        await UniTask.Delay(1000);

                        // データの更新
                        DocumentSnapshot ds = await docRef.GetSnapshotAsync();
                        var userData = ds.ConvertTo<UserData>();
                        UserDataManager.instance.SetUserData(userData);
                        Debug.Log("7クーポンを消費！");
                        UpdateCouponDisplay();
                        int usedCount = _numOfUsingCoupon;
                        _numOfUsingCoupon = 0;
                        _numOfUsingCouponText.text = "0";
                        AssetsDatabase.instance.LoadingPanel.SetActive(false);
                        _couponPanel.SetActive(false);
                        ShowItemUsedPopup(SEVEN_COUPON_NAME, usedCount).Forget();
                    }
                });
                break;

            case "drink":
                charactersRef.Limit(1).GetSnapshotAsync().ContinueWithOnMainThread(async task =>
                {
                    if (task.IsFaulted)
                    {
                        Debug.LogError("エラーが発生しました: " + task.Exception);
                        return;
                    }

                    QuerySnapshot snapshot = task.Result;
                    if (snapshot.Count > 0)
                    {
                        Dictionary<string, object> data = new Dictionary<string, object>
                        {
                            {
                                "drink_coupon", (UserDataManager.instance.UserData.DrinkCoupon - _numOfUsingCoupon)
                            },
                        };
                        
                        var docRef = db.Collection("users").Document(uid);
                        await docRef.SetAsync(data, SetOptions.MergeAll);
                        await UniTask.Delay(1000);

                        // データの更新
                        DocumentSnapshot ds = await docRef.GetSnapshotAsync();
                        var userData = ds.ConvertTo<UserData>();
                        UserDataManager.instance.SetUserData(userData);
                        Debug.Log("Drinkクーポンを消費！");
                        UpdateCouponDisplay();
                        int usedCount = _numOfUsingCoupon;
                        _numOfUsingCoupon = 0;
                        _numOfUsingCouponText.text = "0";
                        AssetsDatabase.instance.LoadingPanel.SetActive(false);
                        _couponPanel.SetActive(false);
                        ShowItemUsedPopup(DRINK_COUPON_NAME, usedCount).Forget();
                    }
                });
                break;

            case "coffee":
                charactersRef.Limit(1).GetSnapshotAsync().ContinueWithOnMainThread(async task =>
                {
                    if (task.IsFaulted)
                    {
                        Debug.LogError("エラーが発生しました: " + task.Exception);
                        return;
                    }

                    QuerySnapshot snapshot = task.Result;
                    if (snapshot.Count > 0)
                    {
                        Dictionary<string, object> data = new Dictionary<string, object>
                        {
                            {
                                "coffee_coupon", (UserDataManager.instance.UserData.CoffeeCoupon - _numOfUsingCoupon)
                            },
                        };
                        
                        var docRef = db.Collection("users").Document(uid);
                        await docRef.SetAsync(data, SetOptions.MergeAll);
                        await UniTask.Delay(1000);

                        // データの更新
                        DocumentSnapshot ds = await docRef.GetSnapshotAsync();
                        var userData = ds.ConvertTo<UserData>();
                        UserDataManager.instance.SetUserData(userData);
                        Debug.Log("Coffeeクーポンを消費！");
                        UpdateCouponDisplay();
                        int usedCount = _numOfUsingCoupon;
                        _numOfUsingCoupon = 0;
                        _numOfUsingCouponText.text = "0";
                        AssetsDatabase.instance.LoadingPanel.SetActive(false);
                        _couponPanel.SetActive(false);
                        ShowItemUsedPopup(COFFEE_COUPON_NAME, usedCount).Forget();
                    }
                });
                break;

            case "atk":
                charactersRef.Limit(1).GetSnapshotAsync().ContinueWithOnMainThread(async task =>
                {
                    if (task.IsFaulted)
                    {
                        Debug.LogError("エラーが発生しました: " + task.Exception);
                        return;
                    }

                    QuerySnapshot snapshot = task.Result;
                    if (snapshot.Count > 0)
                    {
                        Dictionary<string, object> data = new Dictionary<string, object>
                        {
                            {
                                "atk_coupon", (UserDataManager.instance.UserData.ATKCoupon - _numOfUsingCoupon)
                            },
                        };
                        
                        var docRef = db.Collection("users").Document(uid);
                        await docRef.SetAsync(data, SetOptions.MergeAll);
                        await UniTask.Delay(1000);

                        // データの更新
                        DocumentSnapshot ds = await docRef.GetSnapshotAsync();
                        var userData = ds.ConvertTo<UserData>();
                        UserDataManager.instance.SetUserData(userData);
                        Debug.Log("ATKクーポンを消費！");
                        UpdateCouponDisplay();
                        int usedCount = _numOfUsingCoupon;
                        _numOfUsingCoupon = 0;
                        _numOfUsingCouponText.text = "0";
                        AssetsDatabase.instance.LoadingPanel.SetActive(false);
                        _couponPanel.SetActive(false);
                        ShowItemUsedPopup(ATK_COUPON_NAME, usedCount).Forget();
                    }
                });
                break;
        }
    }

    public void Plus()
    {
        _numOfUsingCoupon++;
        switch (_currentCouponName)
        {
            case "5":
                if (UserDataManager.instance.UserData.FiveCoupon < _numOfUsingCoupon)
                {
                    _numOfUsingCoupon = UserDataManager.instance.UserData.FiveCoupon;
                }
                break;
            
            case "7":
                if (UserDataManager.instance.UserData.SevenCoupon < _numOfUsingCoupon)
                {
                    _numOfUsingCoupon = UserDataManager.instance.UserData.SevenCoupon;
                }
                break;
            
            case "drink":
                if (UserDataManager.instance.UserData.DrinkCoupon < _numOfUsingCoupon)
                {
                    _numOfUsingCoupon = UserDataManager.instance.UserData.DrinkCoupon;
                }
                break;
            
            case "coffee":
                if (UserDataManager.instance.UserData.CoffeeCoupon < _numOfUsingCoupon)
                {
                    _numOfUsingCoupon = UserDataManager.instance.UserData.CoffeeCoupon;
                }
                break;
            
            case "atk":
                if (UserDataManager.instance.UserData.ATKCoupon < _numOfUsingCoupon)
                {
                    _numOfUsingCoupon = UserDataManager.instance.UserData.ATKCoupon;
                }
                break;
        }
        _numOfUsingCouponText.text = _numOfUsingCoupon.ToString();
    }

    public void Minus()
    {
        _numOfUsingCoupon--;
        if (_numOfUsingCoupon <= 0)
        {
            _numOfUsingCoupon = 0;
        }

        _numOfUsingCouponText.text = _numOfUsingCoupon.ToString();
    }

    public void Cancel()
    {
        _numOfUsingCoupon = 0;
        _numOfUsingCouponText.text = _numOfUsingCoupon.ToString();
        _couponPanel.SetActive(false);
    }

    public void OnClickItemButton(string type)
    {
        _couponPanel.SetActive(true);
        switch (type)
        {
            case "5":
                _itemImage.sprite = AssetsDatabase.instance.FiveCouponSprite;
                _nameText.text = FIVE_COUPON_NAME;
                _explanationText.text = FIVE_COUPON_EXPLANATION;
                break;
            
            case "7":
                _itemImage.sprite = AssetsDatabase.instance.SevenCouponSprite;
                _nameText.text = SEVEN_COUPON_NAME;
                _explanationText.text = SEVEN_COUPON_EXPLANATION;
                break;
            
            case "drink":
                _itemImage.sprite = AssetsDatabase.instance.DrinkCouponSprite;
                _nameText.text = DRINK_COUPON_NAME;
                _explanationText.text = DRINK_COUPON_EXPLANATION;
                break;
            
            case "coffee":
                _itemImage.sprite = AssetsDatabase.instance.CoffeeCouponSprite;
                _nameText.text = COFFEE_COUPON_NAME;
                _explanationText.text = COFFEE_COUPON_EXPLANATION;
                break;
            
            case "atk":
                _itemImage.sprite = AssetsDatabase.instance.AtkCouponSprite;
                _nameText.text = ATK_COUPON_NAME;
                _explanationText.text = ATK_COUPON_EXPLANATION;
                break;
        }
        _currentCouponName = type;
    }
}
