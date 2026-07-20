using System;
using UnityEngine;
using TMPro;
using Cysharp.Threading.Tasks;

public class CharacterPageManager : MonoBehaviour
{
    [SerializeField] private int _currentPage = 0;

    [SerializeField] private UnityEngine.UI.Image _jobImage;
    [SerializeField] private TextMeshProUGUI _nameText;
    [SerializeField] private TextMeshProUGUI _statusText;
    [SerializeField] private TextMeshProUGUI _todayText;

    // ATKクーポン使用中（次のロールに加算される分）をアイコン右下に出すバッジ
    private TextMeshProUGUI _atkBonusBadge;

    private void Start()
    {
        _currentPage = UserDataManager.instance.CurrentSelectCharacterNumber;
    }

    public void PageUp()
    {
        if (UserDataManager.instance.UserData.Characters.Count == 0) return; // キャラ未作成
        _currentPage++;
        if (UserDataManager.instance.UserData.Characters.Count <= _currentPage)
            _currentPage = 0;
        UserDataManager.instance.SetCurrentSelectCharacterNumber(_currentPage);
        ChangePage();
    }

    public void PageDown()
    {
        if (UserDataManager.instance.UserData.Characters.Count == 0) return; // キャラ未作成
        _currentPage--;
        if (0 > _currentPage)
            _currentPage = UserDataManager.instance.UserData.Characters.Count - 1;
        UserDataManager.instance.SetCurrentSelectCharacterNumber(_currentPage);
        ChangePage();
    }

    public void ChangePage()
    {
        var assets = AssetsDatabase.instance;
        var characters = UserDataManager.instance.UserData.Characters;
        if (characters.Count == 0) return; // キャラ未作成（チケットで作成するまで表示なし）
        if (_currentPage >= characters.Count) _currentPage = 0; // 範囲外になった場合の保険
        var chara = characters[_currentPage];

        _nameText.text = chara.Name;
        _statusText.text = "職業　　";

        switch (chara.Job)
        {
            case "warrior":
                _jobImage.sprite = assets.WarriorSprite;
                _statusText.text += "戦士\n";
                break;
            case "magician":
                _jobImage.sprite = assets.MagicianSprite;
                _statusText.text += "魔法使い\n";
                break;
            case "archer":
                _jobImage.sprite = assets.ArcherSprite;
                _statusText.text += "弓使い\n";
                break;
            case "gunner":
                _jobImage.sprite = assets.GunnerSprite;
                _statusText.text += "銃使い\n";
                break;
        }
        _statusText.text += "属性　　";

        Color32 color;
        string hexColor;
        switch (chara.Element)
        {
            case "fire":
                _jobImage.color = color = assets.FireColor;
                hexColor = $"#{color.r:X2}{color.g:X2}{color.b:X2}";
                _statusText.text += $"<color={hexColor}>炎</color>\n";
                break;
            case "water":
                _jobImage.color = color = assets.WaterColor;
                hexColor = $"#{color.r:X2}{color.g:X2}{color.b:X2}";
                _statusText.text += $"<color={hexColor}>水</color>\n";
                break;
            case "nature":
                _jobImage.color = color = assets.NatureColor;
                hexColor = $"#{color.r:X2}{color.g:X2}{color.b:X2}";
                _statusText.text += $"<color={hexColor}>自然</color>\n";
                break;
            case "thunder":
                _jobImage.color = color = assets.ThunderColor;
                hexColor = $"#{color.r:X2}{color.g:X2}{color.b:X2}";
                _statusText.text += $"<color={hexColor}>雷</color>\n";
                break;
        }

        _statusText.text += $"レベル　{chara.Level}";

        // Text_Today 強調更新
        if (_todayText != null)
            _todayText.text = HomeSceneInitializer.BuildTodayText(
                HomeSceneInitializer.TodayData, chara.Job, chara.Element);

        // 属性エフェクト再生
        if (ElementEffectController.instance != null)
            ElementEffectController.instance.PlayEffect(chara.Element);

        RefreshAtkBonusBadge();

        // シリーズセットスキル発動中なら専用画像へ差し替え（未設定・不成立ならデフォルトのまま）
        HideSeriesSprite();
        ApplySeriesImageAsync(chara, _currentPage).Forget();
    }

    // ================================================================
    // シリーズセットスキル発動中の専用キャラ画像
    // ================================================================
    private int _seriesImageToken; // 非同期DL中にページが変わったときの反映ガード

    /// <summary>
    /// 装備変更後・Home初期表示時に呼ぶ。シルエットをデフォルト（職業＋属性色）に戻したうえで、
    /// セットスキル発動中かつその職業のセット画像が設定されていれば専用画像へ差し替える。
    /// </summary>
    public void RefreshSeriesImage()
    {
        var manager = UserDataManager.instance;
        var characters = manager?.UserData?.Characters;
        int idx = manager != null ? manager.CurrentSelectCharacterNumber : 0;
        if (characters == null || characters.Count == 0 || idx >= characters.Count) return;
        var chara = characters[idx];

        ApplyDefaultJobImage(chara);
        ApplySeriesImageAsync(chara, idx).Forget();
    }

    /// <summary>デフォルトの職業シルエット＋属性色を適用する。</summary>
    private void ApplyDefaultJobImage(Character chara)
    {
        HideSeriesSprite();
        var assets = AssetsDatabase.instance;
        if (_jobImage == null || assets == null) return;
        switch (chara.Job)
        {
            case "warrior":  _jobImage.sprite = assets.WarriorSprite;  break;
            case "magician": _jobImage.sprite = assets.MagicianSprite; break;
            case "archer":   _jobImage.sprite = assets.ArcherSprite;   break;
            case "gunner":   _jobImage.sprite = assets.GunnerSprite;   break;
        }
        switch (chara.Element)
        {
            case "fire":    _jobImage.color = assets.FireColor;    break;
            case "water":   _jobImage.color = assets.WaterColor;   break;
            case "nature":  _jobImage.color = assets.NatureColor;  break;
            case "thunder": _jobImage.color = assets.ThunderColor; break;
        }
    }

    /// <summary>セットスキル発動中なら専用画像をロードして差し替える。</summary>
    private async UniTaskVoid ApplySeriesImageAsync(Character chara, int charIdx)
    {
        int token = ++_seriesImageToken;

        // 起動直後はアイテムDBのローカル読み込みが終わっていないことがあるので少し待つ（最大5秒）
        for (int i = 0; i < 100 && (ItemCacheManager.instance == null || !ItemCacheManager.instance.IsLoaded); i++)
            await UniTask.Delay(50);
        if (token != _seriesImageToken) return;

        var set = SeriesSetBonus.GetActiveSeries(charIdx);
        if (set == null) return;

        var sprite = await SeriesImageCache.GetAsync(set, chara.Job);
        if (sprite == null || token != _seriesImageToken || _jobImage == null) return;
        ShowSeriesSprite(sprite);
    }

    // ================================================================
    // 専用画像の表示ビュー
    // シーン上の _jobImage の矩形は一部が画面外にはみ出している（デフォルトの
    // シルエットは余白が大きく目立たない）ため、専用画像は子ビューに表示し、
    // その矩形を「画面内に収まる範囲」へ自動クランプしたうえで
    // preserveAspect で縦横比を保って収める。
    // ================================================================
    private UnityEngine.UI.Image _seriesImageView;

    /// <summary>専用画像を画面内に収めて表示する（デフォルトのシルエットは非表示にする）。</summary>
    private void ShowSeriesSprite(Sprite sprite)
    {
        EnsureSeriesImageView();
        FitSeriesViewInsideScreen();
        _seriesImageView.sprite = sprite;
        _seriesImageView.color = Color.white; // 専用イラストは属性色で染めない
        _seriesImageView.gameObject.SetActive(true);
        // ATKバッジ（同じく_jobImageの子）が画像の上に重なるよう、ビューは最背面へ
        _seriesImageView.transform.SetAsFirstSibling();
        _jobImage.enabled = false; // 子は enabled=false でも表示され続ける
    }

    /// <summary>専用画像を隠してデフォルトのシルエット表示へ戻す。</summary>
    private void HideSeriesSprite()
    {
        if (_seriesImageView != null) _seriesImageView.gameObject.SetActive(false);
        if (_jobImage != null) _jobImage.enabled = true;
    }

    private void EnsureSeriesImageView()
    {
        if (_seriesImageView != null || _jobImage == null) return;
        var go = new GameObject("__SeriesImageView");
        go.transform.SetParent(_jobImage.transform, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        var img = go.AddComponent<UnityEngine.UI.Image>();
        img.preserveAspect = true; // 縦横比を保ったまま矩形内に収める
        img.raycastTarget = false;
        _seriesImageView = img;
        go.SetActive(false);
    }

    /// <summary>
    /// ビューの矩形を「_jobImage の矩形と画面（Canvas矩形）の重なり部分」にクランプする。
    /// _jobImage が画面外にはみ出していても、専用画像は必ず画面内に収まる。
    /// </summary>
    private void FitSeriesViewInsideScreen()
    {
        var rt = _seriesImageView.rectTransform;
        rt.offsetMin = rt.offsetMax = Vector2.zero; // いったん親いっぱいへ戻す

        var canvas = _jobImage.canvas;
        if (canvas == null) return;
        var canvasRt = canvas.transform as RectTransform;
        if (canvasRt == null) return;

        // 親(_jobImage)の四隅をCanvasローカル座標へ変換
        var corners = new Vector3[4];
        _jobImage.rectTransform.GetWorldCorners(corners);
        Vector2 min = canvasRt.InverseTransformPoint(corners[0]); // 左下
        Vector2 max = canvasRt.InverseTransformPoint(corners[2]); // 右上

        // 画面（Canvas矩形）からのはみ出し量（Canvasローカル単位）
        Rect cr = canvasRt.rect;
        float padL = Mathf.Max(0f, cr.xMin - min.x);
        float padR = Mathf.Max(0f, max.x - cr.xMax);
        float padB = Mathf.Max(0f, cr.yMin - min.y);
        float padT = Mathf.Max(0f, max.y - cr.yMax);
        if (padL <= 0f && padR <= 0f && padB <= 0f && padT <= 0f) return; // はみ出しなし

        // Canvasローカル単位 → _jobImageローカル単位への換算（スケール差を吸収）
        float sx = _jobImage.rectTransform.rect.width  / Mathf.Max(0.001f, max.x - min.x);
        float sy = _jobImage.rectTransform.rect.height / Mathf.Max(0.001f, max.y - min.y);

        rt.offsetMin = new Vector2(padL * sx, padB * sy);
        rt.offsetMax = new Vector2(-padR * sx, -padT * sy);
    }

    /// <summary>
    /// ATKクーポン使用中の加算値（DiceAtkBonus.Pending）をアイコン右下にバッジ表示する。
    /// 0 のときは非表示。クーポン使用直後・ページ更新時に呼ぶ。
    /// </summary>
    public void RefreshAtkBonusBadge()
    {
        EnsureAtkBonusBadge();
        if (_atkBonusBadge == null) return;

        int bonus = DiceAtkBonus.Pending;
        _atkBonusBadge.transform.parent.gameObject.SetActive(bonus > 0);
        if (bonus > 0) _atkBonusBadge.text = $"ATK+{bonus}";
    }

    /// <summary>アイコン右下のATKバッジUIを一度だけ生成する。</summary>
    private void EnsureAtkBonusBadge()
    {
        if (_atkBonusBadge != null || _jobImage == null) return;

        // 背景ピル（暗色・半透明）をアイコンの右下隅にアンカー
        var bg = new GameObject("__AtkBonusBadge");
        bg.transform.SetParent(_jobImage.transform, false);
        var bgRt = bg.AddComponent<RectTransform>();
        bgRt.anchorMin = bgRt.anchorMax = bgRt.pivot = new Vector2(1f, 0f);
        bgRt.sizeDelta = new Vector2(150f, 56f);
        bgRt.anchoredPosition = new Vector2(-6f, 120f);
        bg.AddComponent<UnityEngine.UI.Image>().color = new Color(0f, 0f, 0f, 0.6f);

        var go = new GameObject("__Label");
        go.transform.SetParent(bg.transform, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        var tmp = go.AddComponent<TextMeshProUGUI>();
        if (_nameText != null && _nameText.font != null) tmp.font = _nameText.font;
        tmp.fontSize = 34;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color = new Color(1f, 0.85f, 0.3f);
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;
        _atkBonusBadge = tmp;

        bg.SetActive(false);
    }

    /// <summary>
    /// Image_CharacterSelecterのEventTriggerから呼ぶ
    /// キャラクター画面を開いたときに現在のキャラクターの属性エフェクトを発動
    /// </summary>
    public void OnCharacterPageOpened()
    {
        var characters = UserDataManager.instance.UserData.Characters;
        if (_currentPage >= characters.Count) return; // キャラ未作成
        var chara = characters[_currentPage];
        if (ElementEffectController.instance != null)
            ElementEffectController.instance.PlayEffect(chara.Element);
    }
}

