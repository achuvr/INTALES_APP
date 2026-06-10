using System.Collections;
using Firebase.Firestore;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;

public class UserDataManager : SingletonBehaviour<UserDataManager>
{
    [SerializeField] private string _uid;
    public string UID => _uid;

    private UserData _userData;
    public UserData UserData => _userData ?? (_userData = new UserData());
    private int _currentSelectCharacterNumber = 0;
    public int CurrentSelectCharacterNumber => _currentSelectCharacterNumber;
    private FirebaseFirestore db;

    public GameObject _loadingPanel;
    public GameObject _createNewCharacterPanel;


    public void SetCurrentSelectCharacterNumber(int number)
    {
        _currentSelectCharacterNumber = number;
    }

    public void SetUID(string uid)
    {
        _uid = uid;
    }

    public void FetchUserDataByUID(char init)
    {
        Debug.Log("FetchUserDataByUID.CreateNewCharacter()");
        _createNewCharacterPanel.SetActive(true);
        _loadingPanel.SetActive(false);
    }

    public async UniTask FetchUserDataByUID()
    {
        await FetchAsync();
        StartCoroutine(LoadSceneAsyncWithActivationControl());
    }

    public async UniTask FetchUserDataByUIDForReload()
    {
        await FetchAsync();
    }

    public async UniTask FetchUserDataByUIDForReload(bool isStartScene)
    {
        await FetchAsync();
        SceneManager.LoadScene("Home");
    }

    /// <summary>
    /// users/{uid} を1回読むだけでユーザー情報とキャラクター一覧の両方を取得する。
    /// キャラクターは characters マップフィールドに内蔵されている
    /// （旧構造の characters サブコレクションは廃止。N+1回 → 1回の読み取りになった）。
    /// </summary>
    private async UniTask FetchAsync()
    {
        db = FirebaseFirestore.DefaultInstance;
        if (string.IsNullOrEmpty(_uid))
        {
            Debug.LogError("No user data found");
            return;
        }

        DocumentReference docRef = db.Collection("users").Document(_uid);
        try
        {
            DocumentSnapshot snapshot = await docRef.GetSnapshotAsync();
            if (snapshot.Exists)
            {
                Debug.Log("Find document! UID = " + _uid);

                _userData = snapshot.ConvertTo<UserData>();
                _userData.BuildCharacterList();
                Debug.Log($"{_userData.Username},{_userData.FiveCoupon},{_userData.SevenCoupon},{_userData.GetRegistrationDateTime().ToString()}");
                Debug.Log($"Loaded characters = {_userData.Characters.Count}");
            }
            else
            {
                Debug.LogWarning($"警告: UID '{_uid}' に対応するドキュメントは存在しません。");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Firestoreからのデータ取得中にエラーが発生しました: {ex.Message}");
        }
    }

    private IEnumerator LoadSceneAsyncWithActivationControl()
    {
        Debug.Log("LoadScene Home");
        AsyncOperation asyncLoad = SceneManager.LoadSceneAsync("Home");
        asyncLoad.allowSceneActivation = false;

        while (asyncLoad.progress < 0.9f) // ロード処理が9割完了するまで待機
        {
            float progress = Mathf.Clamp01(asyncLoad.progress / 0.9f);
            yield return null;
        }
        yield return new WaitForSeconds(0.1f);
        asyncLoad.allowSceneActivation = true;
    }

    public void SetUserData(UserData userData)
    {
        _userData = userData;
        _userData?.BuildCharacterList();
    }
}

[FirestoreData, System.Serializable]
public class UserData
{
    public UserData()
    {
        Characters = new List<Character>();
    }

    [FirestoreProperty("name")]
    public string Username { get; set; }

    [FirestoreProperty("five_coupon")]
    public int FiveCoupon { get; set; }

    [FirestoreProperty("seven_coupon")]
    public int SevenCoupon { get; set; }

    [FirestoreProperty("lastDate")]
    public Timestamp LastDate { get; set; }
    public System.DateTime GetRegistrationDateTime()
    {
        return LastDate.ToDateTime();
    }
    [FirestoreProperty("gp")]
    public int GP { get; set; }
    [FirestoreProperty("atk_coupon")]
    public int ATKCoupon { get; set; }
    [FirestoreProperty("drink_coupon")]
    public int DrinkCoupon { get; set; }
    [FirestoreProperty("coffee_coupon")]
    public int CoffeeCoupon { get; set; }

    /// <summary>
    /// Firestore の characters マップ（キー: "0","1",... のスロット番号）。
    /// 直接は使わず、BuildCharacterList() で Characters リストに変換して使う。
    /// </summary>
    [FirestoreProperty("characters")]
    public Dictionary<string, Character> CharactersMap { get; set; }

    /// <summary>
    /// 在店状況（チェックイン中かどうか）をフレンドに公開するか。
    /// ONのときだけ presence/store に自分のチェックイン時刻が書き込まれる。
    /// </summary>
    [FirestoreProperty("share_presence")]
    public bool SharePresence { get; set; }

    /// <summary>
    /// フレンド一覧（キー: フレンドのUID）。
    /// Firestore の friends マップに対応。表示用に相手の名前を非正規化して持つ
    /// （フレンド一覧の表示に追加の読み取りを発生させないため）。
    /// </summary>
    [FirestoreProperty("friends")]
    public Dictionary<string, FriendEntry> Friends
    {
        get => _friends ?? (_friends = new Dictionary<string, FriendEntry>());
        set => _friends = value;
    }
    private Dictionary<string, FriendEntry> _friends;

    /// <summary>スロット番号順に並べたキャラクター一覧（ローカル用）</summary>
    public List<Character> Characters { get; set; }

    /// <summary>CharactersMap をスロット番号順の Characters リストへ変換する</summary>
    public void BuildCharacterList()
    {
        Characters = (CharactersMap ?? new Dictionary<string, Character>())
            .OrderBy(kv => int.TryParse(kv.Key, out var n) ? n : int.MaxValue)
            .Select(kv => kv.Value)
            .ToList();
    }
}


/// <summary>
/// フレンド1人分の情報。users/{uid} の friends.{friendUid} マップに対応。
/// </summary>
[FirestoreData, System.Serializable]
public class FriendEntry
{
    public FriendEntry() {}

    /// <summary>フレンドの表示名（登録時点のスナップショット）</summary>
    [FirestoreProperty("name")]
    public string Name { get; set; }

    /// <summary>フレンドになった日時</summary>
    [FirestoreProperty("since")]
    public Timestamp Since { get; set; }

    /// <summary>お気に入りかどうか（自分側のみの設定。相手には影響しない）</summary>
    [FirestoreProperty("favorite")]
    public bool Favorite { get; set; }
}


[FirestoreData, System.Serializable]
public class Character
{
    public Character() {}

    [FirestoreProperty("name")]
    public string Name { get; set; }

    [FirestoreProperty("el")]
    public string Element { get; set; }

    [FirestoreProperty("job")]
    public string Job { get; set; }

    [FirestoreProperty("lv")]
    public int Level { get; set; }

    // 装備品のEffectsから算出されるステータス（Firestore非保存）
    public int BaseAtk { get; set; }
    public float BaseProb { get; set; }
    public float BaseCritRate { get; set; }
    public float BaseCritDamage { get; set; }
    public int JobRank { get; set; }

    /// <summary>
    /// 装備データ（武器/頭/体/足/スキルブックA/B）。
    /// Firestore の "equipment" マップに対応。
    /// 古いキャラクターデータに存在しない場合は空の Equipment を返す。
    /// </summary>
    [FirestoreProperty("equipment")]
    public Equipment Equipment
    {
        get => _equipment ?? (_equipment = new Equipment());
        set => _equipment = value;
    }
    private Equipment _equipment;

    /// <summary>
    /// このキャラクターが所持しているアイテムの参照一覧。
    /// Firestore の "inventory" 配列に対応。
    /// 各エントリは job + item_id だけを持ち、実データは ItemRepository で取得する。
    /// </summary>
    [FirestoreProperty("inventory")]
    public System.Collections.Generic.List<InventoryRef> Inventory
    {
        get => _inventory ?? (_inventory = new System.Collections.Generic.List<InventoryRef>());
        set => _inventory = value;
    }
    private System.Collections.Generic.List<InventoryRef> _inventory;
}

// NOTE: Equipment クラスと EquipmentSlot 列挙型は
// Assets/Scripts/Commons/Equipment.cs で定義されています。
