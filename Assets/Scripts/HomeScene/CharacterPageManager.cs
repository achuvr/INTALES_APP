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
        _nameText.text = UserDataManager.instance.UserData.Characters[_currentPage].Name;
        _statusText.text = "職業　　";

        switch (UserDataManager.instance.UserData.Characters[_currentPage].Job)
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
        switch (UserDataManager.instance.UserData.Characters[_currentPage].Element)
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

        _statusText.text += $"レベル　{UserDataManager.instance.UserData.Characters[_currentPage].Level}";

        // キャラクター変更時にText_Todayの強調表示を更新
        if (_todayText != null)
        {
            var chara = UserDataManager.instance.UserData.Characters[_currentPage];
            _todayText.text = HomeSceneInitializer.BuildTodayText(
                HomeSceneInitializer.TodayData, chara.Job, chara.Element);
        }
    }
}