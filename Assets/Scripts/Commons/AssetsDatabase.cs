using UnityEngine;

public class AssetsDatabase : SingletonBehaviour<AssetsDatabase>
{
    [SerializeField] private Color _fireColor;
    [SerializeField] private Color _waterColor;
    [SerializeField] private Color _natureColor;
    [SerializeField] private Color _thunderColor;
    public Color FireColor => _fireColor;
    public Color WaterColor => _waterColor;
    public Color NatureColor => _natureColor;
    public Color ThunderColor => _thunderColor;
    
    [Space(20), SerializeField] private Sprite _warriorSprite;
    [SerializeField] private Sprite _magicianSprite;
    [SerializeField] private Sprite _archerSprite;
    [SerializeField] private Sprite _gunnerSprite;
    public Sprite WarriorSprite => _warriorSprite;
    public Sprite MagicianSprite => _magicianSprite;
    public Sprite ArcherSprite => _archerSprite;
    public Sprite GunnerSprite => _gunnerSprite;
    
    [SerializeField] private Sprite _fiveCouponSprite;
    public Sprite FiveCouponSprite => _fiveCouponSprite;
    [SerializeField] private Sprite _sevenCouponSprite;
    public Sprite SevenCouponSprite => _sevenCouponSprite;
    [SerializeField] private Sprite _drinkCouponSprite;
    public Sprite DrinkCouponSprite => _drinkCouponSprite;
    [SerializeField] private Sprite _coffeeCouponSprite;
    public Sprite CoffeeCouponSprite => _coffeeCouponSprite;
    [SerializeField] private Sprite _atkCouponSprite;
    public Sprite AtkCouponSprite => _atkCouponSprite;
    
    [SerializeField] private GameObject _loadingPanel;
    public GameObject LoadingPanel => _loadingPanel;

    [Space(20), SerializeField] private AudioSource _audioSource;
    [SerializeField] private AudioClip _levelUpSE;
    public void PlaySE(AudioClip clip)
    {
        _audioSource.PlayOneShot(clip);
    }
    public void PlayLevelUpSE()
    {
        if (_levelUpSE != null)
            _audioSource.PlayOneShot(_levelUpSE);
    }




}
