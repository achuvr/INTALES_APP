using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Firebase.Firestore;

public class HomeSceneInitializer : MonoBehaviour
{
    
    private FirebaseFirestore _database;
    private Today _today;

    [SerializeField] private UnityEngine.UI.Image _jobImage;
    [SerializeField] private TextMeshProUGUI _nameText;
    [SerializeField] private TextMeshProUGUI _statusText;
    [SerializeField] private TextMeshProUGUI _todayText;

    [SerializeField] private GameObject _rightArrow;
    [SerializeField] private GameObject _leftArrow;
    
    public async void Start()
    {
        var assets = AssetsDatabase.instance;
        _database = FirebaseFirestore.DefaultInstance;
        FetchGoodDay();

        if (UserDataManager.instance.UserData.Characters.Count == 1)
        {
            _leftArrow.SetActive(false);
            _rightArrow.SetActive(false);
        }
        
        _nameText.text = UserDataManager.instance.UserData.Characters[UserDataManager.instance.CurrentSelectCharacterNumber].Name;
        _statusText.text = "職業　　";
        
        switch (UserDataManager.instance.UserData.Characters[UserDataManager.instance.CurrentSelectCharacterNumber].Job)
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
        switch (UserDataManager.instance.UserData.Characters[UserDataManager.instance.CurrentSelectCharacterNumber].Element)
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

        _statusText.text += $"レベル　{UserDataManager.instance.UserData.Characters[UserDataManager.instance.CurrentSelectCharacterNumber].Level}";
    }

    private async void FetchGoodDay()
    {
        CollectionReference colRef = _database.Collection("today");
        try
        {
            QuerySnapshot snapshot = await colRef.GetSnapshotAsync();
            if (colRef != null)
            {
                Today today;
                foreach(var document in snapshot.Documents)
                {
                    if (document.Exists)
                    {
                        _today = document.ConvertTo<Today>();
                        Debug.Log($"{_today.Job},{_today.Element}");
                        _todayText.text = $"本日は…\n{_today.GetJobJPName()} の {_today.GetElementJPName()} の日！";
                    }
                }
                
            }
            else
            {
                Debug.Log($"ドキュメントが見つかりません。");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"データ取得エラー: {ex.Message}");
        }
    }
}

[FirestoreData, System.Serializable]
public class Today
{
    public Today() {}
    
    [SerializeField] private string job;
    [FirestoreProperty("job")]
    public string Job
    {
        get { return job; }
        set { job = value; } 
    }
    
    [SerializeField] private string element;
    [FirestoreProperty("el")]
    public string Element
    {
        get { return element; }
        set { element = value; }
    }

    public string GetJobJPName()
    {
        switch (job)
        {
            case "warrior":
                return "戦士";
            case "magician":
                return "魔法使い";
            case "archer":
                return "弓使い";
            case "gunner":
                return "銃使い";
        }

        return "error";
    }

    public string GetElementJPName()
    {
        switch (element)
        {
            case "fire":
                return "炎";
            case "water":
                return "水";
            case "nature":
                return "自然";
            case "thunder":
                return "雷";
        }

        return "error";
    }
}
