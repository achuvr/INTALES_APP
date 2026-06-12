using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Firebase.Firestore;
using Firebase.Extensions;
using System.Collections.Generic;
using System.Linq;
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
    private const string FIVE_COUPON_EXPLANATION = "5%オフされるチケット。\n組み合わせて最大100%オフまで可能";

    // 7%
    private const string SEVEN_COUPON_NAME = "7%OFFチケット";
    private const string SEVEN_COUPON_EXPLANATION = "7%オフされるチケット。\n組み合わせて最大100%オフまで可能";
    
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

    /// <summary>枚数選択中の「合計 ◯% OFF」表示（5%/7%クーポンのみ。実行時に生成）</summary>
    private TextMeshProUGUI _totalDiscountText;

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
        UpdateTotalDiscountText();
        AssetsDatabase.instance.LoadingPanel.SetActive(false);
        _couponPanel.SetActive(false);

        // 割引クーポンは合計何%OFFかをモーダルで表示する（お店の人に見せる用）。
        // それ以外のクーポンは従来どおりのフェードするポップアップ。
        if (_currentCouponName == "5" || _currentCouponName == "7")
            ShowDiscountModal(couponDisplayName, usedCount, _currentCouponName == "5" ? 5 : 7);
        else
            ShowItemUsedPopup(couponDisplayName, usedCount).Forget();
    }

    /// <summary>割引上限（%）。チケット説明文の「最大100%オフまで可能」に対応</summary>
    private const int MAX_DISCOUNT_PERCENT = 100;

    /// <summary>
    /// 5%/7%クーポン使用後に、合計割引率をモーダルで表示する。
    /// タップで閉じるまで残るので、会計時にそのまま提示できる。
    /// </summary>
    private static void ShowDiscountModal(string couponDisplayName, int usedCount, int percentPerCoupon)
    {
        int total = percentPerCoupon * usedCount;
        bool capped = total > MAX_DISCOUNT_PERCENT;
        if (capped) total = MAX_DISCOUNT_PERCENT;

        string sub = $"{couponDisplayName} × {usedCount}枚";
        if (capped) sub += $"\n※割引は最大{MAX_DISCOUNT_PERCENT}%までです";

        InfoModal.Show("クーポン使用", $"合計 {total}% OFF", sub);
    }

    /// <summary>
    /// 枚数テキストの下に「合計 ◯% OFF」のラベルを実行時に生成する
    /// （シーン編集なしで追加するため、枚数テキストを複製して流用する）。
    /// </summary>
    private void EnsureTotalDiscountText()
    {
        if (_totalDiscountText != null) return;

        var go = Instantiate(_numOfUsingCouponText.gameObject, _numOfUsingCouponText.transform.parent);
        go.name = "__TotalDiscountText";
        _totalDiscountText = go.GetComponent<TextMeshProUGUI>();

        // 複製元（枚数テキスト）のフォントは日本語グリフを持たず文字化けするため、
        // 他のコード生成UIと同じ日本語フォントに差し替える
        var jp = Resources.FindObjectsOfTypeAll<TMP_FontAsset>()
            .FirstOrDefault(f => f.name.ToLower() == "jp");
        if (jp != null) _totalDiscountText.font = jp;

        // ±ボタンの下端(y≈-109)と説明文の上端(y≈-148)の間に収める
        var rt = _totalDiscountText.rectTransform;
        rt.anchoredPosition = _numOfUsingCouponText.rectTransform.anchoredPosition + new Vector2(0, -73);
        rt.sizeDelta = new Vector2(460, 36);
        _totalDiscountText.enableAutoSizing = false;
        _totalDiscountText.fontSize = 28;
        _totalDiscountText.fontStyle = FontStyles.Bold;
        _totalDiscountText.color = new Color(0.80f, 0.18f, 0.12f); // 赤系で目立たせる
        _totalDiscountText.alignment = TextAlignmentOptions.Center;
    }

    /// <summary>5%/7%クーポンのとき、選択中枚数の合計割引率を表示する</summary>
    private void UpdateTotalDiscountText()
    {
        bool isDiscountCoupon = _currentCouponName == "5" || _currentCouponName == "7";
        if (!isDiscountCoupon)
        {
            if (_totalDiscountText != null) _totalDiscountText.gameObject.SetActive(false);
            return;
        }

        EnsureTotalDiscountText();
        _totalDiscountText.gameObject.SetActive(true);

        int pct = _currentCouponName == "5" ? 5 : 7;
        int total = Mathf.Min(pct * _numOfUsingCoupon, MAX_DISCOUNT_PERCENT);
        _totalDiscountText.text = _numOfUsingCoupon > 0 ? $"合計 {total}% OFF" : "";
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
        UpdateTotalDiscountText();
    }

    public void Minus()
    {
        _numOfUsingCoupon--;
        if (_numOfUsingCoupon <= 0)
        {
            _numOfUsingCoupon = 0;
        }

        _numOfUsingCouponText.text = _numOfUsingCoupon.ToString();
        UpdateTotalDiscountText();
    }

    public void Cancel()
    {
        _numOfUsingCoupon = 0;
        _numOfUsingCouponText.text = _numOfUsingCoupon.ToString();
        UpdateTotalDiscountText();
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
        UpdateTotalDiscountText();
    }
}
