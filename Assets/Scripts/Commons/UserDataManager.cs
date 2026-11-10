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
        // このオブジェクトは DontDestroyOnLoad で残るため、ログアウト・退会で Start シーンに戻ったあとは
        // 最初の Start シーンのパネルを指したまま（破棄済み）になる。パネルは FirebaseAuth 側でも閉じるので無ければ何もしない
        if (_loadingPanel != null) _loadingPanel.SetActive(false);
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
        SceneManager.LoadScene(AppScenes.Home);
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
        Debug.Log($"LoadScene {AppScenes.Home}");
        AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(AppScenes.Home);
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
    /// <summary>フリータイムチケット（フリータイム料金が無料。2026-11-07 追加）</summary>
    [FirestoreProperty("freetime_coupon")]
    public int FreeTimeCoupon { get; set; }
    /// <summary>5時間無料チケット（2026-11-07 追加）</summary>
    [FirestoreProperty("fivehour_coupon")]
    public int FiveHourCoupon { get; set; }

    // コマ（2026-11-10 追加）。入店した日に、ホームの中央の惑星の色のコマを1色につき1個もらえる（JourneyRules・SpaceMeeples）
    /// <summary>赤コマの数</summary>
    [FirestoreProperty("red_meeple")]
    public int RedMeeple { get; set; }
    /// <summary>青コマの数</summary>
    [FirestoreProperty("blue_meeple")]
    public int BlueMeeple { get; set; }
    /// <summary>黄コマの数</summary>
    [FirestoreProperty("yellow_meeple")]
    public int YellowMeeple { get; set; }

    // INTALES WORLD で今日コマを置いた記録（2026-11-10。1日1体まで。SpaceWorld）
    /// <summary>コマを置いた営業日 "yyyy-MM-dd"</summary>
    [FirestoreProperty("world_day")]
    public string WorldDay { get; set; }
    /// <summary>置いた置き場（0〜3）</summary>
    [FirestoreProperty("world_area")]
    public int WorldArea { get; set; }
    /// <summary>置いたコマの色（"red" / "blue" / "yellow"）</summary>
    [FirestoreProperty("world_color")]
    public string WorldColor { get; set; }
    /// <summary>5%OFFチケットのカケラの数（INTALES WORLD の商人の港でもらう。2枚そろうと5%OFFチケットになって0に戻る）</summary>
    [FirestoreProperty("five_fragment")]
    public int FiveFragment { get; set; }
    /// <summary>星のカケラの数（INTALES WORLD の星くずの島でもらう。5個そろうと流れ星を1回見て0に戻る。2026-11-11）</summary>
    [FirestoreProperty("star_fragment")]
    public int StarFragment { get; set; }

    /// <summary>誕生月（1〜12。0＝未登録。アカウント作成時に聞く。登録したあとは本人は変えられない画面にしている）</summary>
    [FirestoreProperty("birth_month")]
    public int BirthMonth { get; set; }
    /// <summary>誕生日特典をもらった年（同じ年に2回もらえないように）</summary>
    [FirestoreProperty("birthday_gift_year")]
    public int BirthdayGiftYear { get; set; }
    /// <summary>紹介してくれた人のUID（友だち紹介のQRを読んだとき。Referral）</summary>
    [FirestoreProperty("referred_by")]
    public string ReferredBy { get; set; }
    /// <summary>紹介されて初めて来店した特典をもらったか</summary>
    [FirestoreProperty("referral_rewarded")]
    public bool ReferralRewarded { get; set; }

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

    /// <summary>
    /// お店に公認されたボードゲーム（キー: BoardGameMarks.KeyOf → 公認された営業日 "yyyy-MM-dd"）。
    /// スタッフが見せる公認QRを読んだときだけ増える。操作は BoardGameCertify 経由で行う
    /// </summary>
    [FirestoreProperty("certified_boardgames")]
    public Dictionary<string, object> CertifiedMap
    {
        get => _certifiedMap ?? (_certifiedMap = new Dictionary<string, object>());
        set => _certifiedMap = value;
    }
    private Dictionary<string, object> _certifiedMap;

    /// <summary>
    /// アチーブメントのゲームが星を灯した星座（キー: ゲームキー → 星座ID）。ジャンルが2つ以上の星座に当てはまる
    /// ゲームはお客様が選ぶ。操作は BoardGameCertify 経由で行う
    /// </summary>
    [FirestoreProperty("certified_constellation")]
    public Dictionary<string, object> CertifiedStarMap
    {
        get => _certifiedStarMap ?? (_certifiedStarMap = new Dictionary<string, object>());
        set => _certifiedStarMap = value;
    }
    private Dictionary<string, object> _certifiedStarMap;

    /// <summary>
    /// 解放済みの惑星ID（新ホームの中央に置ける惑星。アチーブメント報酬）。
    /// 以前からの会員のルミナは入れない（最初から持っている扱い）。最初に選んだ惑星（Starter）は入れる。
    /// 店頭の惑星QRを読むと ArrayUnion で1つずつ増える（CallMethodFromQR.UnlockPlanet）。判定は SpacePlanets.IsOwned 経由で行う。
    /// </summary>
    [FirestoreProperty("planets")]
    public List<string> Planets
    {
        get => _planets ?? (_planets = new List<string>());
        set => _planets = value;
    }
    private List<string> _planets;

    /// <summary>
    /// 最初に選んだ惑星（2026-11-10 ユーザー指定。赤＝ルブラ・青＝ルミナ・黄＝リモナから1つ。SpacePlanets.STARTERS）。
    /// 新しく登録した人は "" で作り、ホームで選ぶまで ""（選ぶ画面 SpaceStarterPicker を出す）。
    /// 以前からの会員には無い（null）＝ルミナを最初から持っている
    /// </summary>
    [FirestoreProperty("starter")]
    public string Starter { get; set; }

    /// <summary>
    /// 来店の記録（惑星と来店のしくみ。公転・流れ星・★特典の状態）の Firestore のマップそのまま。
    /// 読むときは Journey を使う（数値の型をそろえて読み直したもの）
    /// </summary>
    [FirestoreProperty("journey")]
    public Dictionary<string, object> JourneyMap
    {
        get => _journeyMap;
        set
        {
            _journeyMap = value;
            _journey = null;
        }
    }
    private Dictionary<string, object> _journeyMap;
    private JourneyData _journey;

    /// <summary>来店の記録（新しい入店QRでまだ一度も入店していなければ null）。書き込みは JourneyService</summary>
    public JourneyData Journey
    {
        get => _journey ?? (_journey = JourneyData.From(_journeyMap));
        set
        {
            _journey = value;
            _journeyMap = value?.ToMap();
        }
    }

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

    /// <summary>
    /// このフレンドと一緒に来店した営業日（"yyyy-MM-dd"。お店で入店中にどちらかがもう一方のQRを読むと、
    /// 読んだ側が2人分の friends に書く）。惑星オパーリアの解放と能力に使う（JourneyService）
    /// </summary>
    [FirestoreProperty("met")]
    public string Met { get; set; }

    /// <summary>このフレンドが、その営業日に初めて来店していた（相手のアプリが書く。惑星パステリアの能力）</summary>
    [FirestoreProperty("first")]
    public string First { get; set; }

    /// <summary>このフレンドの惑星の能力で、その営業日に流れ星をもう1回もらった（相手のアプリが書く）</summary>
    [FirestoreProperty("gift")]
    public string Gift { get; set; }
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
