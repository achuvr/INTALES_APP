using System;
using UnityEngine;
using TMPro;

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
        _currentPage++;
        if (UserDataManager.instance.UserData.Characters.Count <= _currentPage)
            _currentPage = 0;
        UserDataManager.instance.SetCurrentSelectCharacterNumber(_currentPage);
        ChangePage();
    }

    public void PageDown()
    {
        _currentPage--;
        if (0 > _currentPage)
            _currentPage = UserDataManager.instance.UserData.Characters.Count - 1;
        UserDataManager.instance.SetCurrentSelectCharacterNumber(_currentPage);
        ChangePage();
    }

    public void ChangePage()
    {
        var assets = AssetsDatabase.instance;
        var chara = UserDataManager.instance.UserData.Characters[_currentPage];

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
        var chara = UserDataManager.instance.UserData.Characters[_currentPage];
        if (ElementEffectController.instance != null)
            ElementEffectController.instance.PlayEffect(chara.Element);
    }
}

