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

    // キャラクターチケット（ガチャ排出なし。Lv50到達ごとのボーナス。1度に1枚のみ使用可）
    private const string CHARACTER_TICKET_TYPE = "character";
    private const string CHARACTER_TICKET_NAME = "キャラクターチケット";
    private const string CHARACTER_TICKET_EXPLANATION = "新しいキャラクターを1体作れるチケット。";

    /// <summary>キャラクターチケット行の枚数表示（行ごと実行時に生成する）</summary>
    private TextMeshProUGUI _characterTicketText;

    private string _currentCouponName = "";

    /// <summary>枚数選択中の「合計 ◯% OFF」表示（5%/7%クーポンのみ。実行時に生成）</summary>
    private TextMeshProUGUI _totalDiscountText;

    [SerializeField] private GameObject _loadingPanel;
    
    private void Start()
    {
        BuildCharacterTicketRow(); // RestyleBagPage が行スタイルも整えるため、その前に作る
        UpdateCouponDisplay();
        RestyleCouponPanel();
        RestyleBagPage();
        gameObject.SetActive(false);
    }

    /// <summary>
    /// キャラクターチケットの行を、ATK行の複製で実行時に追加する（シーン編集なし）。
    /// 行名は Button_5 にして RestyleBagPage の行スタイル適用（Button_+数字1桁）に乗せる。
    /// 行の位置は「コーヒー行→ATK行」の間隔ぶんだけATK行の下に置く。
    /// </summary>
    private void BuildCharacterTicketRow()
    {
        Transform RowOf(TextMeshProUGUI t)
        {
            for (var p = t != null ? t.transform : null; p != null; p = p.parent)
                if (p.name.StartsWith("Button_")) return p;
            return null;
        }
        var atkRow = RowOf(_atkCouponText);
        var coffeeRow = RowOf(_coffeeCouponText);
        if (atkRow == null || coffeeRow == null)
        {
            Debug.LogWarning("[Bag] クーポン行を特定できないため、キャラクターチケット行の追加をスキップします");
            return;
        }

        var clone = Instantiate(atkRow.gameObject, atkRow.parent);
        clone.name = "Button_5";
        var rt = (RectTransform)clone.transform;
        var atkRt = (RectTransform)atkRow;
        var coffeeRt = (RectTransform)coffeeRow;
        rt.anchoredPosition = atkRt.anchoredPosition + (atkRt.anchoredPosition - coffeeRt.anchoredPosition);

        foreach (var t in clone.GetComponentsInChildren<Transform>(true))
        {
            switch (t.name)
            {
                case "Text_ItemName":
                {
                    var tmp = t.GetComponent<TextMeshProUGUI>();
                    if (tmp != null)
                    {
                        tmp.text = CHARACTER_TICKET_NAME;
                        // 既存行より名前が長いので、はみ出すぶんは自動縮小で収める
                        tmp.enableAutoSizing = true;
                        tmp.fontSizeMax = tmp.fontSize;
                        tmp.fontSizeMin = tmp.fontSize * 0.5f;
                    }
                    break;
                }
                case "Text_ItemCount":
                    _characterTicketText = t.GetComponent<TextMeshProUGUI>();
                    break;
                case "Image_Icon":
                {
                    var img = t.GetComponent<Image>();
                    if (img != null && CharacterTicketService.IconSprite != null)
                        img.sprite = CharacterTicketService.IconSprite;
                    break;
                }
            }
        }

        var btn = clone.GetComponent<Button>();
        if (btn != null)
        {
            // 複製元のonClick（"atk"引数のシーン設定）を丸ごと差し替える
            btn.onClick = new Button.ButtonClickedEvent();
            btn.onClick.AddListener(() => OnClickItemButton(CHARACTER_TICKET_TYPE));
        }
    }

    /// <summary>
    /// バッグページ本体（クーポン一覧）も羊皮紙スタイルに整える。
    /// 各クーポンの行ボタンは角丸＋羊皮紙の行色＋こげ茶文字にする
    /// （フレンド一覧・履歴の行と同じ配色。シーンは編集せず起動時にコードで差し替える。
    /// 背景の古地図テクスチャは HomeSceneInitializer 側で全ページ一括適用）。
    /// </summary>
    private void RestyleBagPage()
    {
        var row       = new Color(0.90f, 0.84f, 0.68f, 1f);    // 行ボタン（一覧の行色）
        var title     = new Color(0.38f, 0.16f, 0.04f, 1f);    // こげ茶
        var close     = Color.white;                            // 閉じる（素材の色のまま）

        Transform bagUi = null;
        foreach (var t in GetComponentsInChildren<Transform>(true))
        {
            if (t.name == "BAG_UI") { bagUi = t; break; }
        }
        if (bagUi == null)
        {
            Debug.LogWarning("[Bag] BAG_UI が見つからないため、バッグ一覧の再スタイルをスキップします");
            return;
        }

        foreach (var t in bagUi.GetComponentsInChildren<Transform>(true))
        {
            switch (t.name)
            {
                // Image_Background は HomeSceneInitializer が古地図テクスチャを貼るためここでは触らない
                case "Image_Close":
                {
                    var img = t.GetComponent<Image>();
                    if (img != null) img.color = close;
                    break;
                }
                case "Text_ItemName":
                {
                    var tmp = t.GetComponent<TextMeshProUGUI>();
                    if (tmp != null) tmp.color = title;
                    break;
                }
                default:
                {
                    // クーポンの行ボタン（Button_0〜Button_4）
                    if (t.name.StartsWith("Button_") && t.name.Length == 8 && char.IsDigit(t.name[7]))
                    {
                        var img = t.GetComponent<Image>();
                        if (img != null)
                        {
                            RoundedRectSprite.Apply(img);
                            // 行にはscale(5.41)が掛かっているため、角丸の見た目の半径を補正
                            img.pixelsPerUnitMultiplier = Mathf.Max(1f, t.localScale.x);
                            img.color = row;
                            // デザイン基盤: 明るい面は白グラデ、濃い面は控えめグラデで磨く
                            // （行のscale補正を渡して影の見た目を他画面と揃える）
                            if (row.a >= 0.5f)
                            {
                                if (row.r + row.g + row.b >= 2.4f) UITheme.PolishButton(img, t.localScale.x);
                                else UITheme.PolishDarkButton(img, t.localScale.x);
                            }
                        }
                    }
                    break;
                }
            }
        }
    }

    /// <summary>
    /// クーポン使用ポップアップを羊皮紙スタイル（InfoModal・ダイス等と同じ配色）に整える。
    /// シーン側はUnityデフォルト風の配色（白背景・ピンク／青ボタン）のため、
    /// シーンを編集せずに起動時にコードで差し替える。
    /// </summary>
    private void RestyleCouponPanel()
    {
        if (_couponPanel == null) return;

        var parchment = new Color(0.99f, 0.95f, 0.84f, 0.98f); // 羊皮紙
        var title     = new Color(0.38f, 0.16f, 0.04f, 1f);    // こげ茶
        var gold      = new Color(0.84f, 0.66f, 0.18f, 1f);    // 金
        var brown     = new Color(0.48f, 0.26f, 0.06f, 1f);    // 濃茶
        var accent    = new Color(0.55f, 0.30f, 0.02f, 1f);    // 強調茶

        // 背景: 白の全画面シート → 羊皮紙色
        var bg = _couponPanel.GetComponent<Image>();
        if (bg != null) bg.color = parchment;

        // テキスト類: 黒・グレー → こげ茶系
        if (_nameText != null) _nameText.color = title;
        if (_explanationText != null) _explanationText.color = title;
        if (_numOfUsingCouponText != null) _numOfUsingCouponText.color = accent;

        // ＋／−ボタン: ピンク・青 → 金（スプライトに記号が含まれるため色だけ変える）
        TintChild("Button_Plus", gold, null, false);
        TintChild("Button_Minus", gold, null, false);

        // 使用・キャンセル: 角丸スプライトに差し替えて金／濃茶に
        TintChild("Button_Use", gold, title, true);
        TintChild("Button_Cancel", brown, Color.white, true);
    }

    /// <summary>ポップアップ内の子ボタンの色（と必要ならスプライト・文字色）を差し替える</summary>
    private void TintChild(string childName, Color bgColor, Color? textColor, bool replaceSprite)
    {
        Transform target = null;
        foreach (var t in _couponPanel.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == childName) { target = t; break; }
        }
        if (target == null)
        {
            Debug.LogWarning($"[Bag] クーポンパネル内に {childName} が見つかりません");
            return;
        }

        var img = target.GetComponent<Image>();
        if (img != null)
        {
            if (replaceSprite)
            {
                RoundedRectSprite.Apply(img);
                // ボタンにscaleが掛かっていても角丸の見た目の半径が揃うよう補正
                img.pixelsPerUnitMultiplier = Mathf.Max(1f, target.localScale.x);
            }
            img.color = bgColor;
            // デザイン基盤: 角丸に差し替えたボタン（使用/キャンセル）だけ磨く。
            // ＋/−はスプライト自体に記号が描かれているため対象外。
            if (replaceSprite && bgColor.a >= 0.5f)
            {
                if (bgColor.r + bgColor.g + bgColor.b >= 2.4f) UITheme.PolishButton(img, target.localScale.x);
                else UITheme.PolishDarkButton(img, target.localScale.x);
            }
        }
        if (textColor.HasValue)
        {
            var tmp = target.GetComponentInChildren<TextMeshProUGUI>(true);
            if (tmp != null) tmp.color = textColor.Value;
        }
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
        if (_characterTicketText != null)
            _characterTicketText.text = UserDataManager.instance.UserData.CharacterTicket.ToString();
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
        // キャラクターチケットは消費フローが特殊（キャラ作成の書き込みと同時に消費）なので別処理
        if (_currentCouponName == CHARACTER_TICKET_TYPE)
        {
            UseCharacterTicket();
            return;
        }

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

        // ATKクーポンは使った枚数ぶん、次のダイスロールの出目に+1される
        if (_currentCouponName == "atk")
        {
            DiceAtkBonus.Add(usedCount);
            var cpm = FindObjectOfType<CharacterPageManager>();
            if (cpm != null) cpm.RefreshAtkBonusBadge();
        }

        UpdateCouponDisplay();
        _numOfUsingCoupon = 0;
        _numOfUsingCouponText.text = "0";
        UpdateTotalDiscountText();
        AssetsDatabase.instance.LoadingPanel.SetActive(false);
        _couponPanel.SetActive(false);

        // 割引クーポンは合計何%OFFかをモーダルで表示する（お店の人に見せる用）。
        // それ以外のクーポンは従来どおりのフェードするポップアップ。
        if (_currentCouponName == "5" || _currentCouponName == "7")
        {
            ShowDiscountModal(couponDisplayName, usedCount, _currentCouponName == "5" ? 5 : 7);
        }
        else
        {
            // 使用枚数を履歴に記録（割引クーポンは ShowDiscountModal 側で計算結果ごと記録する）
            LocalHistoryLog.Add("coupon", $"{couponDisplayName} を{usedCount}枚 使用");
            ShowItemUsedPopup(couponDisplayName, usedCount).Forget();
        }
    }

    /// <summary>
    /// キャラクターチケットを使う（1度に1枚のみ）。
    /// ここではまだ消費せず、キャラクター作成シーン（New）をHomeに加算マージで開く。
    /// チケットの消費（-1）はキャラクター作成の書き込みと同時に行う
    /// （CreateNewCharacter 側。作成せずアプリを終了した場合にチケットだけ減る事故を防ぐ）。
    /// </summary>
    private void UseCharacterTicket()
    {
        if (UserDataManager.instance.UserData.CharacterTicket <= 0) return;

        _numOfUsingCoupon = 0;
        _numOfUsingCouponText.text = "0";
        _couponPanel.SetActive(false);

        CharacterTicketService.PendingUse = true;
        if (SceneLoader.instance == null)
            new GameObject("SceneLoader").AddComponent<SceneLoader>();
        SceneLoader.instance.MergeScene("New");
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

        // 使用枚数と計算結果を履歴に記録（端末ローカル・最大50件）
        LocalHistoryLog.Add("coupon", $"{couponDisplayName} を{usedCount}枚 使用 → 合計{total}%OFF");

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

            case CHARACTER_TICKET_TYPE:
            {
                // 1度に1枚しか使えない（所持0枚なら0のまま）
                int max = Mathf.Min(1, UserDataManager.instance.UserData.CharacterTicket);
                if (_numOfUsingCoupon > max) _numOfUsingCoupon = max;
                break;
            }
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

            case CHARACTER_TICKET_TYPE:
                _itemImage.sprite = CharacterTicketService.IconSprite;
                _nameText.text = CHARACTER_TICKET_NAME;
                _explanationText.text = CHARACTER_TICKET_EXPLANATION;
                break;
        }
        _currentCouponName = type;
        UpdateTotalDiscountText();
    }
}
