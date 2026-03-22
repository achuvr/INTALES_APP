using UnityEngine;
using UnityEngine.UI;

public class ButtonActivater : MonoBehaviour
{

    [SerializeField] private Image _warriorImage;
    [SerializeField] private Image _magicianImage;
    [SerializeField] private Image _archerImage;
    [SerializeField] private Image _gunnerImage;
    
    [SerializeField] private Color _fireColor;
    [SerializeField] private Color _waterColor;
    [SerializeField] private Color _natureColor;
    [SerializeField] private Color _thunderColor;
    
    [Space(20), SerializeField] private Sprite _warriorSprite;
    [SerializeField] private Sprite _magicianSprite;
    [SerializeField] private Sprite _archerSprite;
    [SerializeField] private Sprite _gunnerSprite;

    [SerializeField] private Image _jobImage;
    
    [Space(20),SerializeField] private Image _fireImage;
    [SerializeField] private Image _waterImage;
    [SerializeField] private Image _natureImage;
    [SerializeField] private Image _thunderImage;
    
    [Space(20), SerializeField] private CreateNewCharacter _createNewCharacter;
    private const string WARRIOR = "warrior";
    private const string MAGICIAN = "magician";
    private const string ARCHER = "archer";
    private const string GUNNER = "gunner";
    private const string THUNDER = "thunder";
    private const string NATURE = "nature";
    private const string FIRE = "fire";
    private const string WATER = "water";
    
    public void OnToggleValueChanged_Warrior()
    {
        _warriorImage.enabled = true;
        _magicianImage.enabled = _archerImage.enabled = _gunnerImage.enabled = false;
        _jobImage.sprite = _warriorSprite;
        _createNewCharacter.SetCurrentSelectJob(WARRIOR);
    }

    public void OnToggleValueChanged_Magician()
    {
        _magicianImage.enabled = true;
        _warriorImage.enabled = _archerImage.enabled = _gunnerImage.enabled = false;
        _jobImage.sprite = _magicianSprite;
        _createNewCharacter.SetCurrentSelectJob(MAGICIAN);
    }

    public void OnToggleValueChanged_Archer()
    {
        _archerImage.enabled = true;
        _gunnerImage.enabled = _warriorImage.enabled = _magicianImage.enabled = false;
        _jobImage.sprite = _archerSprite;
        _createNewCharacter.SetCurrentSelectJob(ARCHER);
    }

    public void OnToggleValueChanged_Gunner()
    {
        _gunnerImage.enabled = true;
        _warriorImage.enabled = _archerImage.enabled = _magicianImage.enabled = false;
        _jobImage.sprite = _gunnerSprite;
        _createNewCharacter.SetCurrentSelectJob(GUNNER);
    }

    public void OnToggleValueChanged_Fire()
    {
        _fireImage.enabled = true;
        _waterImage.enabled = _natureImage.enabled = _thunderImage.enabled = false;
        _jobImage.color = _fireColor;
        _createNewCharacter.SetCurrentSelectElement(FIRE);
    }
    
    public void OnToggleValueChanged_Water()
    {
        _waterImage.enabled = true;
        _fireImage.enabled = _natureImage.enabled = _thunderImage.enabled = false;
        _jobImage.color = _waterColor;
        _createNewCharacter.SetCurrentSelectElement(WATER);
    }
    
    public void OnToggleValueChanged_Nature()
    {
        _natureImage.enabled = true;
        _waterImage.enabled = _fireImage.enabled = _thunderImage.enabled = false;
        _jobImage.color = _natureColor;
        _createNewCharacter.SetCurrentSelectElement(NATURE);
    }
    
    public void OnToggleValueChanged_Thunder()
    {
        _thunderImage.enabled = true;
        _waterImage.enabled = _natureImage.enabled = _fireImage.enabled = false;
        _jobImage.color = _thunderColor;
        _createNewCharacter.SetCurrentSelectElement(THUNDER);
    }
}
