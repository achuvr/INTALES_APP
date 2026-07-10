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

        // グループ参加中なら、切り替え後のキャラ名をメンバーリストへ即時反映する
        GroupSession.AnnounceSelfIfActive();
    }

    public void SetUID(string uid)
    {
        _uid = uid;
    }

    public void FetchUserDataByUID(char init)
    {
        // 新規アカウントの初期化完了。キャラクター作成画面は直接開かず、
        // FirebaseAuth 側の「キャラクターを作成しますか？」モーダルの選択に委ねる
        // （いいえ＝キャラクターチケットを受け取ってHomeへ）
        Debug.Log("FetchUserDataByUID: 新規アカウント初期化完了");
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

    /// <summary>ユーザー名を最後に編集した日時（編集は1週間に1回まで。未編集なら1970年=編集可）</summary>
    [FirestoreProperty("name_edited_at")]
    public Timestamp NameEditedAt { get; set; }

    /// <summary>ユーザー名を最後に変更した日時（未設定=未変更。変更は1か月に1回まで）。</summary>
    [FirestoreProperty("name_changed_at")]
    public Timestamp NameChangedAt { get; set; }

    /// <summary>来店ボーナス（毎回）を最後に付与した日付（"yyyy-MM-dd"。1日1回判定用）。</summary>
    [FirestoreProperty("visit_bonus_date")]
    public string VisitBonusDate { get; set; }

    /// <summary>1か月以内の再来ボーナスを最後に付与した日付（"yyyy-MM-dd"。1日1回判定用）。</summary>
    [FirestoreProperty("revisit_bonus_date")]
    public string RevisitBonusDate { get; set; }

    /// <summary>5時間以上の滞在ボーナスを最後に付与した日付（"yyyy-MM-dd"。1日1回判定用）。</summary>
    [FirestoreProperty("stay_bonus_date")]
    public string StayBonusDate { get; set; }

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

    /// <summary>キャラクターチケット（レベル50到達ごとのボーナス。ガチャからは排出されない）</summary>
    [FirestoreProperty("character_ticket")]
    public int CharacterTicket { get; set; }

    /// <summary>キャラクターチケットの付与済みマイルストーン数（全キャラの lv÷50 の合計。二重付与防止）</summary>
    [FirestoreProperty("char_ticket_milestones")]
    public int CharTicketMilestones { get; set; }

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

    /// <summary>
    /// アカウント（全キャラ共有）の所持品。Firestore の users/{uid}.inventory 配列に対応。
    /// 所持品はキャラ単位ではなくアカウント単位で管理し、全キャラがアクセスできる。
    /// </summary>
    [FirestoreProperty("inventory")]
    public List<InventoryRef> Inventory
    {
        get => _inventory ?? (_inventory = new List<InventoryRef>());
        set => _inventory = value;
    }
    private List<InventoryRef> _inventory;

    /// <summary>
    /// 遊んだことのあるボードゲーム（キー: ボドゲーマURL末尾のスラッグ）。
    /// 図鑑リストのチェックマークに対応。操作は BoardGameMarks 経由で行う。
    /// </summary>
    [FirestoreProperty("played_boardgames")]
    public List<string> PlayedBoardgames
    {
        get => _playedBoardgames ?? (_playedBoardgames = new List<string>());
        set => _playedBoardgames = value;
    }
    private List<string> _playedBoardgames;

    /// <summary>
    /// お気に入りのボードゲーム（キー: ボドゲーマURL末尾のスラッグ）。
    /// 図鑑リストの★マークに対応。操作は BoardGameMarks 経由で行う。
    /// </summary>
    [FirestoreProperty("favorite_boardgames")]
    public List<string> FavoriteBoardgames
    {
        get => _favoriteBoardgames ?? (_favoriteBoardgames = new List<string>());
        set => _favoriteBoardgames = value;
    }
    private List<string> _favoriteBoardgames;

    /// <summary>スロット番号順に並べたキャラクター一覧（ローカル用）</summary>
    public List<Character> Characters { get; set; }

    /// <summary>CharactersMap をスロット番号順の Characters リストへ変換する</summary>
    public void BuildCharacterList()
    {
        Characters = (CharactersMap ?? new Dictionary<string, Character>())
            .OrderBy(kv => int.TryParse(kv.Key, out var n) ? n : int.MaxValue)
            .Select(kv => kv.Value)
            .ToList();

        MergeLegacyCharacterInventories();
    }

    /// <summary>
    /// 旧構造（characters[*].inventory）の所持品をアカウント所持品(Inventory)へ統合する（item_idで重複除外）。
    /// 所持品をキャラ単位からアカウント単位へ移したことによる後方互換。メモリ上のみで、
    /// 永続化は次回の所持品書き込み（ガチャ/入手）時に users/{uid}.inventory へ反映される。
    /// </summary>
    private void MergeLegacyCharacterInventories()
    {
        var seen = new HashSet<string>();
        foreach (var r in Inventory)
            if (r != null && !string.IsNullOrEmpty(r.ItemId)) seen.Add(r.ItemId);

        if (Characters == null) return;
        foreach (var c in Characters)
        {
            if (c?.Inventory == null) continue;
            foreach (var r in c.Inventory)
            {
                if (r == null || string.IsNullOrEmpty(r.ItemId)) continue;
                if (seen.Add(r.ItemId)) Inventory.Add(r);
            }
        }
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
    /// 【旧構造・互換用】キャラ単位の所持品。所持品はアカウント単位（UserData.Inventory）へ移行済み。
    /// 既存データの読み込み・アカウント所持品への統合（移行）のためだけに残している。新規書き込みはしない。
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
