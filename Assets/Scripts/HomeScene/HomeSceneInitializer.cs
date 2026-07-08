using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using Firebase.Firestore;
using Cysharp.Threading.Tasks;

public class HomeSceneInitializer : MonoBehaviour
{
    private FirebaseFirestore _database;
    private Today _today;

    [SerializeField] private bool _isDebugMode;
    [SerializeField] private bool _isCardTransferMode; // 紙の会員証引き継ぎツールを開く（店側用）

    [SerializeField] private UnityEngine.UI.Image _jobImage;
    [SerializeField] private TextMeshProUGUI _nameText;
    [SerializeField] private TextMeshProUGUI _statusText;
    [SerializeField] private TextMeshProUGUI _todayText;

    [SerializeField] private GameObject _rightArrow;
    [SerializeField] private GameObject _leftArrow;

    // 今日のjob/elementを他クラスから参照できるよう保持
    public static Today TodayData { get; private set; }
    
    public async void Start()
    {
        if (_isDebugMode)
        {
            SceneManager.LoadScene("IconUploader");
        }
        if (_isCardTransferMode)
        {
            SceneManager.LoadScene("CardTransfer");
        }
        
        var assets = AssetsDatabase.instance;
        _database = FirebaseFirestore.DefaultInstance;
        FetchGoodDay();

        // アイテムマスターデータの週次同期（土曜5時以降の初回起動時）
        if (ItemSyncManager.instance != null)
            ItemSyncManager.instance.InitAsync().Forget();

        // ローカルに保存した装備データをキャラクターに復元
        LocalEquipSave.ApplyAll(UserDataManager.instance.UserData.Characters);

        // フレンド機能のUI（コード生成）をセットアップ（シーン編集不要で追加するため）
        if (GetComponent<FriendMenuController>() == null)
            gameObject.AddComponent<FriendMenuController>();

        // 左上の「ログイン中」バッジ（チェックイン中のみ表示）
        if (GetComponent<PresenceIndicator>() == null)
            gameObject.AddComponent<PresenceIndicator>();

        // 退会（アカウント削除）機能（Page_Info下部にボタンを置く）
        if (GetComponent<AccountDeletionController>() == null)
            gameObject.AddComponent<AccountDeletionController>();

        // ボス討伐バトル（戦闘ボタンから開く。内部で既存のダイス機能=Dice3DSimulatorを流用）
        if (GetComponent<BossBattleController>() == null)
            gameObject.AddComponent<BossBattleController>();

        // ガチャ機能（左上のガチャボタンから開く。イベントガチャはQR読み取りから）
        if (GetComponent<GachaController>() == null)
            gameObject.AddComponent<GachaController>();

        // 履歴機能（キャラページ右上の履歴ボタンから開く）
        if (GetComponent<HistoryController>() == null)
            gameObject.AddComponent<HistoryController>();

        // グループ機能（左上のグループボタンからA〜Dのルームに参加）
        if (GetComponent<GroupController>() == null)
            gameObject.AddComponent<GroupController>();

        // 図鑑機能（右上の図鑑ボタンから装備一覧シーンを開く）
        if (GetComponent<ZukanButton>() == null)
            gameObject.AddComponent<ZukanButton>();

        // ボードゲーム一覧（お知らせページ下部のゲーム一覧ボタンから店舗の全ゲームを閲覧）
        if (GetComponent<BoardGameListButton>() == null)
            gameObject.AddComponent<BoardGameListButton>();

        // 食事・ドリンクのメニュー（お知らせページ下部のメニューボタンから開く）
        if (GetComponent<MenuButton>() == null)
            gameObject.AddComponent<MenuButton>();

        // タブレット(横長比率)でフッターと重なる下部の6ボタンを
        // 2行3列に組み替えてフッター上に収める（スマホは変更なし）
        if (GetComponent<TabletFooterLayout>() == null)
            gameObject.AddComponent<TabletFooterLayout>();

        // 各ページの全画面背景を「古びた世界地図」風テクスチャに差し替える（冒険感）
        ApplyOldMapBackgrounds();

        // チェックアウト忘れ（営業終了時刻を過ぎた来店）を自動クローズ
        // 自動クローズが発生した場合は、共有していた在店状態も解除する
        if (LocalVisitLog.AutoCloseStaleVisits() > 0)
            PresenceService.SetCheckedInAsync(false).Forget();

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

#if UNITY_EDITOR
    /// <summary>
    /// デバッグ用: Unity Editor 上で N キーを押すと入店処理（CheckIn）を実行する。
    /// 実機ビルドには含まれない（#if UNITY_EDITOR で囲っているため）。
    /// 新旧どちらの Input バックエンドでも拾えるよう両対応している。
    /// </summary>
    private void Update()
    {
        bool pressed = false;
#if ENABLE_INPUT_SYSTEM
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb != null && kb.nKey.wasPressedThisFrame)
            pressed = true;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKeyDown(KeyCode.N))
            pressed = true;
#endif
        if (pressed)
        {
            Debug.Log("[Debug] N キー押下 → 入店処理を実行します");
            var caller = GetComponent<CallMethodFromQR>() ?? gameObject.AddComponent<CallMethodFromQR>();
            caller.CheckIn();
        }
    }
#endif

    /// <summary>
    /// シーン内の全画面背景（Image_Background、各ページに1枚ずつ）へ
    /// 古地図風のプロシージャルテクスチャを貼る。
    /// CanvasFloat 内の小さな Image_Background は対象外（幅でフィルタ）。
    /// </summary>
    private static void ApplyOldMapBackgrounds()
    {
        foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
        {
            if (go.name != "Image_Background" || !go.scene.IsValid()) continue;
            var rt = go.transform as RectTransform;
            if (rt == null || rt.sizeDelta.x < 1000f) continue; // 全画面背景のみ
            var img = go.GetComponent<UnityEngine.UI.Image>();
            if (img != null) OldMapBackground.Apply(img);
        }
    }

    private async void FetchGoodDay()
    {
        // master/config に集約済み（today/achievements/events を1ドキュメントで取得）
        try
        {
            var config = await MasterData.GetConfigAsync();
            if (config.Today == null)
            {
                Debug.Log($"master/config に today がありません。");
                return;
            }

            _today = config.Today;
            TodayData = _today;
            Debug.Log($"{_today.Job},{_today.Element}");

            // 1行表示のまま画面内に収める:
            // シーン上のレイアウト（幅929px・中心が右寄り）だと長い職業名で右端が
            // 画面からはみ出すため、画面内（幅760px・中央）に寄せ直し、
            // それでも収まらない分はフォントの自動縮小で吸収する（改行はさせない）
            // 高さが狭いと縦方向にも自動縮小がかかって文字が小さくなりすぎるため、
            // 高さはフォント1行分より余裕を持たせ、横幅だけで縮小がかかるようにする
            var todayRt = _todayText.rectTransform;
            todayRt.anchoredPosition = new Vector2(0, todayRt.anchoredPosition.y);
            todayRt.sizeDelta = new Vector2(780, 100);
            _todayText.textWrappingMode = TextWrappingModes.NoWrap;
            _todayText.overflowMode = TextOverflowModes.Overflow;
            _todayText.alignment = TextAlignmentOptions.Center;
            _todayText.enableAutoSizing = true;
            _todayText.fontSizeMax = _todayText.fontSize; // 元の71ptを上限に
            _todayText.fontSizeMin = 40;

            var chara = UserDataManager.instance.UserData.Characters[UserDataManager.instance.CurrentSelectCharacterNumber];
            _todayText.text = BuildTodayText(_today, chara.Job, chara.Element);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"データ取得エラー: {ex.Message}");
        }
    }

    // 強調テキストを生成するstaticメソッド（CharacterPageManagerからも呼ぶ）
    public static string BuildTodayText(Today today, string charaJob, string charaElement)
    {
        if (today == null) return "";

        string jobName = today.GetJobJPName();
        string elName  = today.GetElementJPName();

        // 職業一致 → 黒で強調
        if (charaJob == today.Job)
            jobName = $"<color=#000000><size=115%>{jobName}</size></color>";

        // 属性一致 → 属性ごとの色で強調
        if (charaElement == today.Element)
        {
            string elColor = today.Element switch {
                "fire"    => "#FF3300",
                "water"   => "#0066FF",
                "nature"  => "#33AA00",
                "thunder" => "#FFD700",
                _         => "#FFFFFF"
            };
            elName = $"<color={elColor}><size=115%>{elName}</size></color>";
        }

        return $"本日は…{jobName} の {elName} の日！";
    }
}

[FirestoreData, System.Serializable]
public class Today
{
    public Today() {}

    [UnityEngine.SerializeField] private string job;
    [FirestoreProperty("job")]
    public string Job
    {
        get { return job; }
        set { job = value; }
    }

    [UnityEngine.SerializeField] private string element;
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
            case "warrior":  return "戦士";
            case "magician": return "魔法使い";
            case "archer":   return "弓使い";
            case "gunner":   return "銃使い";
        }
        return "error";
    }

    public string GetElementJPName()
    {
        switch (element)
        {
            case "fire":    return "炎";
            case "water":   return "水";
            case "nature":  return "自然";
            case "thunder": return "雷";
        }
        return "error";
    }
}
