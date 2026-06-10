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
    
    /// <summary>
    /// クーポンを消費する。
    /// FieldValue.Increment によるサーバー側減算1回で完結し、
    /// 読み取り（事前チェック・書き込み後の再取得）は行わない。
    /// ローカルの UserData も同時に減算して表示を更新する。
    /// </summary>
    private async UniTask UseCoupon()
    {
        string couponField;
        string couponDisplayName;
        System.Action<UserData, int> applyLocal;

        switch (_currentCouponName)
        {
            case "5":
                couponField = "five_coupon";
                couponDisplayName = FIVE_COUPON_NAME;
                applyLocal = (d, n) => d.FiveCoupon -= n;
                break;
            case "7":
                couponField = "seven_coupon";
                couponDisplayName = SEVEN_COUPON_NAME;
                applyLocal = (d, n) => d.SevenCoupon -= n;
                break;
            case "drink":
                couponField = "drink_coupon";
                couponDisplayName = DRINK_COUPON_NAME;
                applyLocal = (d, n) => d.DrinkCoupon -= n;
                break;
            case "coffee":
                couponField = "coffee_coupon";
                couponDisplayName = COFFEE_COUPON_NAME;
                applyLocal = (d, n) => d.CoffeeCoupon -= n;
                break;
            case "atk":
                couponField = "atk_coupon";
                couponDisplayName = ATK_COUPON_NAME;
                applyLocal = (d, n) => d.ATKCoupon -= n;
                break;
            default:
                return;
        }

        AssetsDatabase.instance.LoadingPanel.SetActive(true);
        var db = FirebaseFirestore.DefaultInstance;
        var uid = UserDataManager.instance.UID;
        int usedCount = _numOfUsingCoupon;

        try
        {
            await db.Collection("users").Document(uid).UpdateAsync(new Dictionary<string, object>
            {
                { couponField, FieldValue.Increment(-usedCount) },
            }).AsUniTask();
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"クーポン消費エラー ({couponDisplayName}): {ex.Message}");
            AssetsDatabase.instance.LoadingPanel.SetActive(false);
            return;
        }

        applyLocal(UserDataManager.instance.UserData, usedCount);
        Debug.Log($"{couponDisplayName}を消費！");
        UpdateCouponDisplay();
        _numOfUsingCoupon = 0;
        _numOfUsingCouponText.text = "0";
        AssetsDatabase.instance.LoadingPanel.SetActive(false);
        _couponPanel.SetActive(false);
        ShowItemUsedPopup(couponDisplayName, usedCount).Forget();
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
