using System.Linq;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// グループ機能のUI（コード生成）。
/// 画面左上の「グループ」ボタンから A〜D のルームを選んで Fusion に参加する。
/// 参加中は同じルームのメンバーの選択中キャラクター名をボタンの下にリスト表示し、
/// もう一度グループボタンを押すと脱退する（最後の1人ならルームは自動消滅）。
/// 接続自体は GroupSession（DontDestroyOnLoad）が保持するため、
/// シーンが再読み込みされてもグループには入ったままになる。
/// </summary>
public class GroupController : MonoBehaviour
{
    private static readonly Color C_CHIP = new Color(1f, 1f, 1f, 0.93f);

    private static GroupController _instance;

    private Canvas _canvas;
    private GameObject _groupBtn;
    private TextMeshProUGUI _groupBtnLabel;
    private GameObject _selector;
    private RectTransform _memberRoot;
    private bool _selectorOpen;

    private void Start()
    {
        var canvasGO = GameObject.Find("Canvas");
        _canvas = canvasGO != null ? canvasGO.GetComponent<Canvas>() : FindFirstObjectByType<Canvas>();
        if (_canvas == null) return;

        _instance = this;
        BuildUI();
        GroupSession.Changed += Refresh;
        Refresh();
        UpdateVisibility();
    }

    private void OnDestroy()
    {
        GroupSession.Changed -= Refresh;
        if (_instance == this) _instance = null;
    }

    /// <summary>チェックイン／チェックアウト後に表示を更新する（PresenceIndicator.Refreshと同じ流儀）</summary>
    public static void RefreshVisibility()
    {
        if (_instance != null) _instance.UpdateVisibility();
    }

    /// <summary>
    /// グループ機能は入店中（チェックイン〜チェックアウトの間）のみ使える。
    /// 退店時にグループ参加中だった場合は自動で脱退する
    /// （ボタンが消えて脱退手段が無くなるのを防ぐ）。
    /// </summary>
    private void UpdateVisibility()
    {
        bool checkedIn = LocalVisitLog.HasOpenVisit();
        _groupBtn.SetActive(checkedIn);

        if (!checkedIn)
        {
            _selectorOpen = false;
            _selector.SetActive(false);
            if (GroupSession.Active != null && !GroupSession.IsBusy)
                GroupSession.LeaveAsync().Forget();
        }
        Refresh();
    }

    // ================================================================
    // 操作
    // ================================================================
    private void OnGroupButton()
    {
        if (GroupSession.IsBusy) return;

        if (GroupSession.Active != null)
        {
            // 参加中 → 脱退
            GroupSession.LeaveAsync().Forget();
            return;
        }

        // 未参加 → ルーム選択の開閉
        _selectorOpen = !_selectorOpen;
        Refresh();
    }

    private void OnRoomButton(string room)
    {
        if (GroupSession.IsBusy) return;
        _selectorOpen = false;
        Refresh();
        JoinAsync(room).Forget();
    }

    private async UniTask JoinAsync(string room)
    {
        bool ok = await GroupSession.JoinAsync(room);
        if (!ok)
            FriendMenuController.ShowToast("グループに参加できませんでした");
    }

    // ================================================================
    // 表示更新
    // ================================================================
    private void Refresh()
    {
        if (_groupBtnLabel == null) return;

        if (GroupSession.IsBusy)
            _groupBtnLabel.text = "接続中…";
        else if (GroupSession.Active != null)
            _groupBtnLabel.text = "グループ脱退";
        else
            _groupBtnLabel.text = "グループ";

        _selector.SetActive(_selectorOpen && GroupSession.Active == null && !GroupSession.IsBusy);
        RebuildMemberList();
    }

    /// <summary>参加中ルームのメンバー（キャラクター名）一覧を作り直す</summary>
    private void RebuildMemberList()
    {
        for (int i = _memberRoot.childCount - 1; i >= 0; i--)
            Destroy(_memberRoot.GetChild(i).gameObject);

        if (GroupSession.Active == null) return;

        var jp = GetJpFont();
        var ink = GachaController.GRID_TEXT_COLOR;

        // ヘッダー（ルーム名と人数）
        var members = GroupSession.Members.Values.ToList();
        var header = MakeChip(0, new Color(0.30f, 0.55f, 0.35f, 0.95f));
        MakeLabel(header.transform, $"{GroupSession.CurrentRoom}ルーム（{members.Count}人）",
            jp, 28, FontStyles.Bold, Color.white, 240, 56);

        // メンバー行: [職業アイコン][属性ルーン] キャラクター名
        for (int i = 0; i < members.Count; i++)
        {
            var m = members[i];
            var chip = MakeChip(i + 1, C_CHIP);

            float nameLeft = 14f; // アイコンが無い場合は名前を左へ広げる
            var jobSprite = LoadJobSprite(m.Job, m.Level);
            if (jobSprite != null)
            {
                MakeIcon(chip.transform, jobSprite, 44, new Vector2(28, 0));
                nameLeft = 52f;
            }
            var elementSprite = LoadElementSprite(m.Element);
            if (elementSprite != null)
            {
                MakeIcon(chip.transform, elementSprite, 38, new Vector2(72, 0));
                nameLeft = 94f;
            }

            // ダイスの出目（名前の右側に金色バッジで表示）
            float nameRight = 10f;
            if (m.Dice >= 0)
            {
                var badge = new GameObject("__DiceBadge");
                badge.transform.SetParent(chip.transform, false);
                var brt = badge.AddComponent<RectTransform>();
                brt.anchorMin = brt.anchorMax = new Vector2(0f, 0.5f);
                brt.sizeDelta = new Vector2(42, 42);
                brt.anchoredPosition = new Vector2(226, 0);
                var bimg = badge.AddComponent<Image>();
                UICircleSprite.Apply(bimg);
                bimg.color = new Color(0.84f, 0.66f, 0.18f, 1f); // 金
                bimg.raycastTarget = false;
                var diceLabel = MakeLabel(badge.transform, m.Dice.ToString(), jp, 24,
                    FontStyles.Bold, Color.white, 42, 42);
                var diceTmp = diceLabel.GetComponent<TextMeshProUGUI>();
                diceTmp.enableAutoSizing = true;
                diceTmp.fontSizeMax = 24;
                diceTmp.fontSizeMin = 14;
                nameRight = 54f;
            }

            // 名前（アイコンと出目バッジの間、残り幅に収まるよう自動縮小）
            float nameW = 250f - nameLeft - nameRight;
            var label = MakeLabel(chip.transform, m.Name, jp, 26, FontStyles.Bold, ink, nameW, 56);
            label.GetComponent<RectTransform>().anchoredPosition =
                new Vector2((nameLeft - nameRight) / 2f, 0); // チップ中心基準でアイコン分右へ寄せる
            var tmp = label.GetComponent<TextMeshProUGUI>();
            tmp.enableAutoSizing = true;
            tmp.fontSizeMax = 26;
            tmp.fontSizeMin = 14;
        }

        // 出目の合計（リストの下。未ロールのメンバーは除いて合算）
        int diceSum = members.Where(m => m.Dice >= 0).Sum(m => m.Dice);
        var footer = MakeChip(members.Count + 1, new Color(0.84f, 0.66f, 0.18f, 0.95f)); // 金
        MakeLabel(footer.transform, $"出目の合計　{diceSum}", jp, 26, FontStyles.Bold, Color.white, 240, 56);
    }

    /// <summary>
    /// 職業アイコンをレベル帯で出し分ける。
    /// Lv50以下={job}1 / Lv51〜100={job}2 / Lv101以上={job}3（Resources/JobIcons/）
    /// </summary>
    private static Sprite LoadJobSprite(string job, int level)
    {
        if (string.IsNullOrEmpty(job)) return null;
        int tier = level <= 50 ? 1 : (level <= 100 ? 2 : 3);
        return Resources.Load<Sprite>($"JobIcons/{job}{tier}");
    }

    /// <summary>属性ルーン（Resources/Runes/。雷のみファイル名が Rune_lightning_yellow）</summary>
    private static Sprite LoadElementSprite(string element)
    {
        string file = element switch
        {
            "fire"    => "Rune_fire",
            "water"   => "Rune_water",
            "nature"  => "Rune_nature",
            "thunder" => "Rune_lightning_yellow",
            _         => null,
        };
        return file == null ? null : Resources.Load<Sprite>($"Runes/{file}");
    }

    /// <summary>チップ内のアイコン（チップ左端基準のX位置で配置・枠に収まるサイズ）</summary>
    private static void MakeIcon(Transform chip, Sprite sprite, float size, Vector2 leftPos)
    {
        var go = new GameObject("__Icon");
        go.transform.SetParent(chip, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f); // チップ左端中央基準
        rt.sizeDelta = new Vector2(size, size);
        rt.anchoredPosition = leftPos;
        var img = go.AddComponent<Image>();
        img.sprite = sprite;
        img.preserveAspect = true;
        img.raycastTarget = false;
    }

    /// <summary>メンバーリストの1行分の角丸チップ（クリックは透過させる）</summary>
    private GameObject MakeChip(int index, Color color)
    {
        var go = new GameObject($"__Member{index}");
        go.transform.SetParent(_memberRoot, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.sizeDelta = new Vector2(250, 56);
        rt.anchoredPosition = new Vector2(0, -index * 62);
        var img = go.AddComponent<Image>();
        RoundedRectSprite.Apply(img);
        img.color = color;
        img.raycastTarget = false;
        return go;
    }

    // ================================================================
    // UI構築
    // ================================================================
    private void BuildUI()
    {
        var jp = GetJpFont();
        var ink = GachaController.GRID_TEXT_COLOR;

        // グループボタン（左上・「ログイン中」バッジの下）
        _groupBtn = new GameObject("__GroupButton");
        _groupBtn.transform.SetParent(_canvas.transform, false);
        var rt = _groupBtn.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(30, -190);
        rt.sizeDelta = new Vector2(250, 90);
        var bg = _groupBtn.AddComponent<Image>();
        GachaController.ApplyGridButtonStyle(bg);

        _groupBtnLabel = MakeLabel(_groupBtn.transform, "グループ", jp, 40, FontStyles.Bold, ink, 240, 90)
            .GetComponent<TextMeshProUGUI>();
        _groupBtnLabel.enableAutoSizing = true;
        _groupBtnLabel.fontSizeMax = 40;
        _groupBtnLabel.fontSizeMin = 24;

        var btn = _groupBtn.AddComponent<Button>();
        btn.targetGraphic = bg;
        btn.onClick.AddListener(OnGroupButton);

        // ルーム選択（A〜D。グループボタンの下に縦並び）
        _selector = new GameObject("__RoomSelector");
        _selector.transform.SetParent(_canvas.transform, false);
        var srt = _selector.AddComponent<RectTransform>();
        srt.anchorMin = srt.anchorMax = new Vector2(0f, 1f);
        srt.pivot = new Vector2(0f, 1f);
        srt.anchoredPosition = new Vector2(30, -290);
        srt.sizeDelta = Vector2.zero;

        for (int i = 0; i < GroupSession.ROOMS.Length; i++)
        {
            string room = GroupSession.ROOMS[i];
            var roomBtnGO = new GameObject($"__Room_{room}");
            roomBtnGO.transform.SetParent(_selector.transform, false);
            var rrt = roomBtnGO.AddComponent<RectTransform>();
            rrt.anchorMin = rrt.anchorMax = new Vector2(0f, 1f);
            rrt.pivot = new Vector2(0f, 1f);
            rrt.sizeDelta = new Vector2(250, 80);
            rrt.anchoredPosition = new Vector2(0, -i * 86);
            var rbg = roomBtnGO.AddComponent<Image>();
            GachaController.ApplyGridButtonStyle(rbg);

            MakeLabel(roomBtnGO.transform, $"{room} ルーム", jp, 36, FontStyles.Bold, ink, 240, 80);

            var rbtn = roomBtnGO.AddComponent<Button>();
            rbtn.targetGraphic = rbg;
            rbtn.onClick.AddListener(() => OnRoomButton(room));
        }
        _selector.SetActive(false);

        // メンバーリストの親（グループボタンの下）
        var memberGO = new GameObject("__GroupMembers");
        memberGO.transform.SetParent(_canvas.transform, false);
        _memberRoot = memberGO.AddComponent<RectTransform>();
        _memberRoot.anchorMin = _memberRoot.anchorMax = new Vector2(0f, 1f);
        _memberRoot.pivot = new Vector2(0f, 1f);
        _memberRoot.anchoredPosition = new Vector2(30, -290);
        _memberRoot.sizeDelta = Vector2.zero;
    }

    // ================================================================
    // UI部品ヘルパー
    // ================================================================
    private static TMP_FontAsset GetJpFont()
    {
        var fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
        return fonts.FirstOrDefault(f => f.name.ToLower() == "jp") ?? fonts.FirstOrDefault();
    }

    private static GameObject MakeLabel(Transform parent, string text, TMP_FontAsset font,
        float size, FontStyles style, Color color, float w, float h)
    {
        var go = new GameObject("__Label");
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>().sizeDelta = new Vector2(w, h);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        if (font != null) tmp.font = font;
        tmp.text = text;
        tmp.fontSize = size;
        tmp.fontStyle = style;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;
        return go;
    }
}
