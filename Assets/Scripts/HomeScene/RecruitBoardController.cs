using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using Firebase.Firestore;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 募集ボード（相席募集）。お店で一緒に遊ぶ人を募集する掲示板。
///
/// 誹謗中傷・プライバシー対策の設計方針:
///  ・自由入力のテキストは一切投稿できない。ゲームは図鑑カタログから選択、
///    日時/人数/ひとことタグ/参加メッセージ/通報理由はすべて定型ID（数値）。
///  ・表示する文字列は「ローカルの定型文配列」と「ローカルのカタログ」から
///    引く。Firestore から来た文字列を直接表示するのはユーザー名だけ
///    （ユーザー名はセキュリティルールで users/{uid}.name と一致を強制）。
///  ・1対1のDMは作らない。待ち合わせの詳細は店で会って決める前提。
///  ・通報が1件でも付いた募集は投稿者以外には表示しない（店側が
///    tools/firestore の Admin SDK で確認・対処する）。
///  ・募集は開催日（約3か月=90日先まで指定可）の集合時刻+猶予で期限切れとなり、
///    日次ワークフローが物理削除する（tools/firestore/cleanup-recruits.js）。
///
/// Firestore: recruits/{postId}
///   uid/name(投稿者), game(カタログのタイトル。""=未定), date("yyyy-MM-dd"),
///   slot(0時からの分。720〜1350の30分刻み), seats(あと何人。1〜9),
///   tags(定型ID配列), status("open"|"closed"),
///   participants{uid:{name,msg}}, reports{uid:理由ID},
///   created_at, expires_at
/// 書き込み形状はセキュリティルール（tools/firestore/firestore.rules）で強制。
///
/// 入口: RecruitChatButton。BoardGameListController と同じ全画面オーバーレイ方式。
/// </summary>
public class RecruitBoardController : MonoBehaviour
{
    // ================================================================
    // 定型データ（すべて選択式。ここの文言を変えるだけで運用調整できる）
    // ※既存表示と同じ文字を優先し、絵文字は使わない（TMPグリフOOM対策）
    // ================================================================

    /// <summary>募集に付けられる「ひとこと」タグ。Firestoreには添字だけ保存する。</summary>
    public static readonly string[] TAGS =
    {
        "初心者歓迎",       // 0
        "ルール教えます",   // 1
        "ルール覚えたい",   // 2
        "まったり",         // 3
        "じっくり考えたい", // 4
        "ワイワイしたい",   // 5
        "短時間でサクッと", // 6
        "長時間OK",         // 7
        "重ゲー歓迎",       // 8
        "軽ゲー中心",       // 9
        "とちゅう参加OK",   // 10
        "だれでもOK",       // 11
    };

    /// <summary>参加表明のときに選ぶメッセージ。Firestoreには添字だけ保存する。</summary>
    public static readonly string[] JOIN_MSGS =
    {
        "よろしくお願いします！", // 0
        "初心者です",             // 1
        "ルールわかります",       // 2
        "時間ぴったりに行きます", // 3
        "少し遅れるかもです",     // 4
    };

    /// <summary>通報理由。Firestoreには添字だけ保存する。</summary>
    public static readonly string[] REPORT_REASONS =
    {
        "名前が不適切",   // 0
        "迷惑な使い方",   // 1
        "その他",         // 2
    };

    // 時間枠: 開店12:00〜最終入店22:30の30分刻み（22枠）
    public const int SLOT_MIN = 720;
    public const int SLOT_MAX = 1350;
    public const int SLOT_STEP = 30;

    private const int MAX_TAGS = 4;        // 1つの募集に付けられるタグ数
    private const int MAX_SEATS = 9;       // 募集できる人数の上限（ルールの seats <= 9 と対）
    private const int GRACE_MINUTES = 30;  // 開始時刻を過ぎても30分は表示し続ける
    private const int MAX_DAYS_AHEAD = 90; // 何日先まで募集を出せるか＝約3か月（ルールの92日制限と対）

    /// <summary>
    /// 「持ち込みゲーム」を表す game フィールドの内部値。
    /// カタログのタイトルと衝突しないよう '!' 始まりにする（表示は GameDisplay が変換）。
    /// </summary>
    private const string GAME_BRING_OWN = "!bring_own";

    private const string WEEKDAYS = "日月火水木金土";

    // ================================================================
    // 見た目（BoardGameListController と同じ古い本パレット）
    // ================================================================
    private static readonly Color C_CARD      = new Color(0.97f, 0.92f, 0.78f, 1.00f);
    private static readonly Color C_PARCHMENT = new Color(0.99f, 0.95f, 0.84f, 1.00f);
    private static readonly Color C_BORDER    = new Color(0.84f, 0.66f, 0.18f, 1.00f);
    private static readonly Color C_TITLEBAR  = new Color(0.40f, 0.23f, 0.08f, 0.92f);
    private static readonly Color C_TITLETEXT = new Color(0.99f, 0.94f, 0.78f, 1.00f);
    private static readonly Color C_TITLE     = new Color(0.38f, 0.16f, 0.04f, 1.00f);
    private static readonly Color C_INK       = new Color(0.30f, 0.18f, 0.06f, 1.00f);
    private static readonly Color C_MUTED     = new Color(0.52f, 0.38f, 0.22f, 1.00f);
    private static readonly Color C_CHIP      = new Color(0.90f, 0.83f, 0.62f, 1.00f);
    private static readonly Color C_CHIP_ON   = new Color(0.84f, 0.66f, 0.18f, 1.00f);
    private static readonly Color C_CLOSE     = new Color(0.90f, 0.32f, 0.32f, 1.00f);
    private static readonly Color C_GOLD      = new Color(0.84f, 0.66f, 0.18f, 1.00f);
    private static readonly Color C_WHITE     = new Color(1.00f, 0.99f, 0.93f, 1.00f);
    private static readonly Color C_DANGER    = new Color(0.78f, 0.16f, 0.16f, 1.00f);
    private static readonly Color C_SIL_ON    = new Color(0.38f, 0.16f, 0.04f, 1.00f); // 埋まっている枠（主催＋参加者）
    private static readonly Color C_SIL_OFF   = new Color(0.52f, 0.38f, 0.22f, 0.25f); // 空いている枠

    private const float TITLE_H = 264f;
    private const float FOOTER_H = 170f;
    private const float CARD_H = 380f;
    private const float CARD_GAP = 24f;

    // ================================================================
    // 状態
    // ================================================================
    private Canvas _canvas;
    private TMP_FontAsset _jp;
    private GameObject _overlay;
    private RectTransform _listContent;
    private TextMeshProUGUI _emptyLabel;
    private GameObject _popup;          // 作成フォーム/参加/通報などの前面モーダル
    private ListenerRegistration _listener;
    private Coroutine _rebuild;
    private readonly List<Post> _posts = new List<Post>();
    private bool _busy;                 // 書き込み中の多重タップ防止

    // 作成フォームの選択値
    private string _formGame = "";      // カタログのタイトル。""=未定
    private string _formDate;           // "yyyy-MM-dd"
    private int _formSlot;
    private int _formSeats = 1;
    private readonly HashSet<int> _formTags = new HashSet<int>();
    private TextMeshProUGUI _formGameLabel;
    private TextMeshProUGUI _formSlotLabel;
    private TextMeshProUGUI _formSeatsLabel;
    private TextMeshProUGUI _formDateLabel;
    private readonly List<(int id, Image bg, TextMeshProUGUI label)> _formTagChips
        = new List<(int, Image, TextMeshProUGUI)>();

    // カレンダーピッカーの状態
    private DateTime _calMonth;           // 表示中の月（1日固定）
    private GameObject _calGridRoot;
    private TextMeshProUGUI _calMonthLabel;
    private Button _calPrevBtn, _calNextBtn;
    private Coroutine _calBuild;

    // ゲーム画像（master/boardgame_images。店側が GameImageAdminController で登録。1ゲーム最大3枚）
    private readonly Dictionary<string, List<string>> _gameImages = new Dictionary<string, List<string>>(); // タイトル→URL一覧
    private readonly Dictionary<string, Texture2D> _thumbCache = new Dictionary<string, Texture2D>();       // URL→テクスチャ
    private readonly List<Sprite> _thumbSprites = new List<Sprite>();
    private static string ThumbCacheDir => System.IO.Path.Combine(Application.persistentDataPath, "recruit_thumbs");

    // 画像拡大ビューア
    private List<string> _viewerUrls;
    private int _viewerIndex;
    private Image _viewerImg;
    private TextMeshProUGUI _viewerPage;

    /// <summary>1件の募集（Firestoreドキュメントのローカル表現）。</summary>
    private class Post
    {
        public string Id;
        public string Uid;
        public string Name;
        public string Game;
        public string GameCustom; // 店側がコンソール/Admin SDKで直接設定するタイトル（クライアントは書けない）
        public string Date;
        public int Slot;
        public int Seats;
        public readonly List<int> Tags = new List<int>();
        public string Status;
        public readonly List<(string uid, string name, int msg)> Participants
            = new List<(string, string, int)>();
        public int ReportCount;
    }

    private static string DateStr(DateTime d) => d.ToString("yyyy-MM-dd");
    private static string SlotText(int slot) => $"{slot / 60}:{slot % 60:00}";
    private static FirebaseFirestore Db => FirebaseFirestore.DefaultInstance;
    private static string MyUid => UserDataManager.instance != null ? UserDataManager.instance.UID : null;
    private static string MyName
    {
        get
        {
            var d = UserDataManager.instance != null ? UserDataManager.instance.UserData : null;
            return d != null ? d.Username : null;
        }
    }

    // ================================================================
    // 起動・破棄
    // ================================================================
    private void Start()
    {
        var canvasGO = GameObject.Find("Canvas");
        _canvas = canvasGO != null ? canvasGO.GetComponent<Canvas>() : FindFirstObjectByType<Canvas>();
        if (_canvas == null)
        {
            Debug.LogError("[Recruit] Canvas が見つかりません");
            Destroy(gameObject);
            return;
        }
        _jp = GetJpFont();
        BuildUI();
        StartCoroutine(PrewarmThenListen());
    }

    /// <summary>
    /// 定型文のグリフを40文字/フレームでアトラスへ事前登録してからリスナーを張る
    /// （1フレームに大量の新規グリフを追加すると実機OOMで落ちるため。
    /// BoardGameListController.PrewarmRowsThenShow と同じ対策）。
    /// </summary>
    private IEnumerator PrewarmThenListen()
    {
        var texts = new List<string>
        {
            "今日明日 あと人 満員 募集中 参加 締め切りました 通報があり確認中です",
            "ゲームはお店できめる 持ち込みゲームであそぶ 取り扱い外のゲーム 12345:067890～",
            "日にちをえらぶ 前の月 次の月 年 " + WEEKDAYS,
            "タイトル設定 タイトルを消す この名前にする まえ つぎ 枚",
        };
        texts.AddRange(TAGS);
        texts.AddRange(JOIN_MSGS);
        texts.AddRange(REPORT_REASONS);
        yield return PrewarmGlyphs(texts);

        // ゲーム画像のURL一覧（1ドキュメント）を先に読む
        yield return LoadGameImagesAsync().ToCoroutine();

        // 期限内の募集だけ購読する（閉じるまでの間の追加読み取りは差分のみ）
        _listener = Db.Collection("recruits")
            .WhereGreaterThan("expires_at", Timestamp.GetCurrentTimestamp())
            .Listen(OnSnapshot);
    }

    private async UniTask LoadGameImagesAsync()
    {
        try
        {
            var snap = await Db.Collection("master").Document("boardgame_images")
                .GetSnapshotAsync().AsUniTask();
            if (!snap.Exists) return;
            if (snap.ToDictionary().TryGetValue("images", out var io)
                && io is Dictionary<string, object> im)
            {
                foreach (var kv in im)
                {
                    if (!(kv.Value is Dictionary<string, object> v)) continue;
                    var urls = new List<string>();
                    if (v.TryGetValue("items", out var itemsO) && itemsO is List<object> items)
                    {
                        foreach (var it in items)
                            if (it is Dictionary<string, object> m
                                && m.TryGetValue("url", out var u2) && u2 is string s2)
                                urls.Add(s2);
                    }
                    else if (v.TryGetValue("url", out var u) && u is string s)
                    {
                        urls.Add(s); // 旧形式（1枚だけの登録）との互換
                    }
                    if (urls.Count > 0) _gameImages[kv.Key] = urls;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Recruit] ゲーム画像一覧の読み込みに失敗: {ex.Message}");
        }
    }

    private void OnDestroy()
    {
        _listener?.Stop();
        _listener = null;
        ClosePopup(); // 開いたままのフォーム/ピッカーを道連れにする（暗幕の取り残し防止）
        if (_overlay != null) Destroy(_overlay);

        // サムネイルのスプライト・テクスチャを破棄（実機メモリ対策）
        foreach (var s in _thumbSprites) if (s != null) Destroy(s);
        _thumbSprites.Clear();
        foreach (var kv in _thumbCache) if (kv.Value != null) Destroy(kv.Value);
        _thumbCache.Clear();
    }

    private void CloseBoard()
    {
        Destroy(gameObject); // OnDestroy がリスナー解除とオーバーレイ破棄を行う
    }

    // ================================================================
    // Firestore 購読
    // ================================================================
    private void OnSnapshot(QuerySnapshot snapshot)
    {
        if (this == null || _overlay == null) return;
        _posts.Clear();
        foreach (var doc in snapshot.Documents)
        {
            try
            {
                var d = doc.ToDictionary();
                var p = new Post
                {
                    Id = doc.Id,
                    Uid = Str(d, "uid"),
                    Name = Str(d, "name"),
                    Game = Str(d, "game"),
                    GameCustom = Str(d, "game_custom"),
                    Date = Str(d, "date"),
                    Slot = Int(d, "slot"),
                    Seats = Int(d, "seats"),
                    Status = Str(d, "status"),
                };
                if (d.TryGetValue("tags", out var to) && to is List<object> tl)
                    foreach (var t in tl) p.Tags.Add(Convert.ToInt32(t));
                if (d.TryGetValue("participants", out var po) && po is Dictionary<string, object> pm)
                    foreach (var kv in pm)
                        if (kv.Value is Dictionary<string, object> v)
                            p.Participants.Add((kv.Key, Str(v, "name"), Int(v, "msg")));
                if (d.TryGetValue("reports", out var ro) && ro is Dictionary<string, object> rm)
                    p.ReportCount = rm.Count;
                _posts.Add(p);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Recruit] 募集の読み込みに失敗: {doc.Id} {ex.Message}");
            }
        }

        // 通報付きは投稿者以外に見せない。過ぎた時間枠（+猶予）も落とす
        string uid = MyUid;
        var now = DateTime.Now;
        _posts.RemoveAll(p =>
            (p.ReportCount > 0 && p.Uid != uid)
            || IsPastSlot(p, now));

        // 自分の募集を先頭に、あとは日付→時間順
        _posts.Sort((a, b) =>
        {
            bool am = a.Uid == uid, bm = b.Uid == uid;
            if (am != bm) return am ? -1 : 1;
            int c = string.CompareOrdinal(a.Date, b.Date);
            return c != 0 ? c : a.Slot.CompareTo(b.Slot);
        });

        if (_rebuild != null) StopCoroutine(_rebuild);
        _rebuild = StartCoroutine(RebuildList());
    }

    private static bool IsPastSlot(Post p, DateTime now)
    {
        string today = DateStr(now);
        if (string.CompareOrdinal(p.Date, today) > 0) return false;
        if (string.CompareOrdinal(p.Date, today) < 0) return true;
        return now.Hour * 60 + now.Minute > p.Slot + GRACE_MINUTES;
    }

    private static string Str(Dictionary<string, object> d, string k)
        => d.TryGetValue(k, out var v) && v is string s ? s : "";
    private static int Int(Dictionary<string, object> d, string k)
        => d.TryGetValue(k, out var v) ? Convert.ToInt32(v) : 0;

    // ================================================================
    // 画面骨格
    // ================================================================
    private void BuildUI()
    {
        _overlay = new GameObject("__RecruitBoardOverlay");
        _overlay.transform.SetParent(_canvas.transform, false);
        var ort = _overlay.AddComponent<RectTransform>();
        ort.anchorMin = Vector2.zero; ort.anchorMax = Vector2.one;
        ort.offsetMin = ort.offsetMax = Vector2.zero;
        var bg = _overlay.AddComponent<Image>();
        OldMapBackground.Apply(bg);
        bg.raycastTarget = true;

        // タイトル帯
        var titleBar = new GameObject("__TitleBar");
        titleBar.transform.SetParent(_overlay.transform, false);
        var trt = titleBar.AddComponent<RectTransform>();
        trt.anchorMin = new Vector2(0f, 1f); trt.anchorMax = new Vector2(1f, 1f);
        trt.pivot = new Vector2(0.5f, 1f);
        trt.offsetMin = new Vector2(0f, -TITLE_H); trt.offsetMax = Vector2.zero;
        titleBar.AddComponent<Image>().color = C_TITLEBAR;
        UITheme.PolishTitleBar(titleBar);

        var title = MakeLabel(titleBar.transform, "募集ボード", _jp, 84, FontStyles.Bold, C_TITLETEXT, 700, TITLE_H, Vector2.zero);
        var trt2 = title.GetComponent<RectTransform>();
        trt2.anchorMin = Vector2.zero; trt2.anchorMax = Vector2.one;
        trt2.offsetMin = new Vector2(48f, 16f); trt2.offsetMax = new Vector2(-150f, -16f);
        title.GetComponent<TextMeshProUGUI>().alignment = TextAlignmentOptions.MidlineLeft;

        // ×ボタン
        var close = MakeButton(_overlay.transform, C_CLOSE, "✕", _jp, 56, Color.white, 120, 120, Vector2.zero, CloseBoard);
        var crt = close.GetComponent<RectTransform>();
        crt.anchorMin = crt.anchorMax = new Vector2(1f, 1f);
        crt.pivot = new Vector2(1f, 0.5f);
        crt.anchoredPosition = new Vector2(-28, -TITLE_H / 2f);

        // 説明（自由チャットではないことをやわらかく伝える）
        var hint = MakeLabel(_overlay.transform, "お店でいっしょに遊ぶ人を募集できます（3か月先まで）", _jp, 30, FontStyles.Normal, C_MUTED, 0, 48, Vector2.zero);
        var hrt = hint.GetComponent<RectTransform>();
        hrt.anchorMin = new Vector2(0f, 1f); hrt.anchorMax = new Vector2(1f, 1f);
        hrt.pivot = new Vector2(0.5f, 1f);
        hrt.offsetMin = new Vector2(28f, -(TITLE_H + 56f)); hrt.offsetMax = new Vector2(-28f, -(TITLE_H + 8f));

        // スクロールリスト
        var svGO = new GameObject("__Scroll");
        svGO.transform.SetParent(_overlay.transform, false);
        var svrt = svGO.AddComponent<RectTransform>();
        svrt.anchorMin = Vector2.zero; svrt.anchorMax = Vector2.one;
        svrt.offsetMin = new Vector2(24f, FOOTER_H + 24f);
        svrt.offsetMax = new Vector2(-24f, -(TITLE_H + 64f));
        var scroll = svGO.AddComponent<ScrollRect>();
        scroll.horizontal = false; scroll.vertical = true; scroll.scrollSensitivity = 40f;
        scroll.movementType = ScrollRect.MovementType.Clamped;

        var vpGO = new GameObject("__VP");
        vpGO.transform.SetParent(svGO.transform, false);
        var vprt = vpGO.AddComponent<RectTransform>();
        vprt.anchorMin = Vector2.zero; vprt.anchorMax = Vector2.one;
        vprt.offsetMin = vprt.offsetMax = Vector2.zero;
        vpGO.AddComponent<RectMask2D>();
        scroll.viewport = vprt;

        var ctGO = new GameObject("__Content");
        ctGO.transform.SetParent(vpGO.transform, false);
        _listContent = ctGO.AddComponent<RectTransform>();
        _listContent.anchorMin = new Vector2(0f, 1f); _listContent.anchorMax = new Vector2(1f, 1f);
        _listContent.pivot = new Vector2(0.5f, 1f);
        _listContent.offsetMin = _listContent.offsetMax = Vector2.zero;
        scroll.content = _listContent;

        // 空状態 / 読み込み中
        _emptyLabel = MakeLabel(vpGO.transform, "読み込み中…", _jp, 34, FontStyles.Normal, C_MUTED, 0, 0, Vector2.zero)
            .GetComponent<TextMeshProUGUI>();
        var ert = _emptyLabel.GetComponent<RectTransform>();
        ert.anchorMin = Vector2.zero; ert.anchorMax = Vector2.one;
        ert.offsetMin = ert.offsetMax = Vector2.zero;

        // 下部の「募集をつくる」
        var create = MakeButton(_overlay.transform, C_GOLD, "募集をつくる", _jp, 44, C_INK, 0, FOOTER_H - 48f, Vector2.zero, OpenCreateForm);
        var frt = create.GetComponent<RectTransform>();
        frt.anchorMin = new Vector2(0f, 0f); frt.anchorMax = new Vector2(1f, 0f);
        frt.pivot = new Vector2(0.5f, 0f);
        frt.offsetMin = new Vector2(28f, 24f); frt.offsetMax = new Vector2(-28f, FOOTER_H - 24f);
    }

    // ================================================================
    // 募集リスト（カードは1フレーム2枚まで生成: TMP大量生成のOOM対策）
    // ================================================================
    private IEnumerator RebuildList()
    {
        if (_listContent == null) yield break;
        for (int i = _listContent.childCount - 1; i >= 0; i--)
            Destroy(_listContent.GetChild(i).gameObject);

        // 投稿者名とゲーム名（カタログ由来）のグリフを先に登録する
        var dyn = new List<string>();
        foreach (var p in _posts) { dyn.Add(p.Name); dyn.Add(GameDisplay(p)); foreach (var pt in p.Participants) dyn.Add(pt.name); }
        yield return PrewarmGlyphs(dyn);

        _emptyLabel.text = _posts.Count == 0
            ? "いま募集はありません\n\n「募集をつくる」から\n最初の募集を出してみましょう" : "";
        _listContent.sizeDelta = new Vector2(0f, _posts.Count * (CARD_H + CARD_GAP) + CARD_GAP);

        for (int i = 0; i < _posts.Count; i++)
        {
            BuildCard(_posts[i], i);
            if (i % 2 == 1) yield return null;
        }
        _rebuild = null;
    }

    private void BuildCard(Post p, int index)
    {
        string uid = MyUid;
        bool mine = p.Uid == uid;
        bool joined = p.Participants.Any(x => x.uid == uid);
        bool closed = p.Status != "open";
        bool full = p.Participants.Count >= p.Seats;

        var card = MakeRect("__Card", _listContent, C_CARD, 0, CARD_H);
        RoundedRectSprite.Apply(card.GetComponent<Image>());
        UITheme.ElevateCard(card, 12f, 6f, 0.22f);
        var rt = card.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.offsetMin = new Vector2(4f, -(CARD_GAP + index * (CARD_H + CARD_GAP)) - CARD_H);
        rt.offsetMax = new Vector2(-4f, -(CARD_GAP + index * (CARD_H + CARD_GAP)));

        // ゲーム画像（店側が登録している場合のみ。カード右上に表示、タップで拡大）
        var thumbUrls = ImageUrlsFor(p);
        bool hasThumb = thumbUrls != null;
        if (hasThumb)
        {
            var thumbGO = new GameObject("__Thumb");
            thumbGO.transform.SetParent(card.transform, false);
            var thrt = thumbGO.AddComponent<RectTransform>();
            thrt.anchorMin = thrt.anchorMax = new Vector2(1f, 1f);
            thrt.pivot = new Vector2(1f, 1f);
            thrt.anchoredPosition = new Vector2(-24f, -20f);
            thrt.sizeDelta = new Vector2(170, 170);
            var timg = thumbGO.AddComponent<Image>();
            timg.color = new Color(1f, 1f, 1f, 0f); // 読み込み完了までは透明
            timg.preserveAspect = true;
            var tbtn = thumbGO.AddComponent<Button>();
            tbtn.targetGraphic = timg;
            UITheme.AddPressEffect(thumbGO);
            string display = GameDisplay(p);
            tbtn.onClick.AddListener(() => OpenImageViewer(thumbUrls, display));
            LoadThumbAsync(thumbUrls[0], timg).Forget();

            if (thumbUrls.Count > 1)
            {
                // 複数枚あることが分かる小さなバッジ
                var badge = MakeRect("__ThumbBadge", thumbGO.transform, new Color(0f, 0f, 0f, 0.55f), 64, 34);
                RoundedRectSprite.Apply(badge.GetComponent<Image>());
                badge.GetComponent<Image>().raycastTarget = false;
                var brt2 = badge.GetComponent<RectTransform>();
                brt2.anchorMin = brt2.anchorMax = new Vector2(1f, 0f);
                brt2.pivot = new Vector2(1f, 0f);
                brt2.anchoredPosition = new Vector2(-4f, 4f);
                MakeLabel(badge.transform, $"{thumbUrls.Count}枚", _jp, 20, FontStyles.Bold, Color.white, 64, 34, Vector2.zero);
            }
        }

        // 1行目: 日時を大きく強調し、投稿者名は小さめ・薄めで添える
        string when = $"{DateLabel(p.Date)} {SlotText(p.Slot)}～";
        var l1 = MakeLabel(card.transform,
            $"{when}<size=70%><color=#856138>　{p.Name}さん</color></size>",
            _jp, 46, FontStyles.Bold, C_TITLE, 0, 60, Vector2.zero);
        StretchTop(l1, 28, 20, 60); AutoSize(l1, 28, 46, TextAlignmentOptions.TopLeft);
        if (hasThumb) ShrinkRight(l1, 214f);

        // 2行目: ゲーム（Firestoreの文字列は表示せず、ローカルのカタログ/定型文から引く）。
        // 実タイトルは太字・大きめで強調、「お店できめる」等は控えめに
        string gameText = GameDisplay(p);
        bool realTitle = gameText.StartsWith("『") || p.Game == GAME_BRING_OWN;
        var l2 = MakeLabel(card.transform, gameText, _jp, realTitle ? 40 : 30,
            realTitle ? FontStyles.Bold : FontStyles.Normal,
            realTitle ? C_TITLE : C_MUTED, 0, 54, Vector2.zero);
        StretchTop(l2, 28, 84, 54); AutoSize(l2, 24, realTitle ? 40 : 30, TextAlignmentOptions.TopLeft);
        if (hasThumb) ShrinkRight(l2, 214f);

        // 3行目: タグ
        string tags = string.Join("・", p.Tags.Where(t => t >= 0 && t < TAGS.Length).Select(t => TAGS[t]));
        var l3 = MakeLabel(card.transform, tags, _jp, 28, FontStyles.Normal, C_MUTED, 0, 40, Vector2.zero);
        StretchTop(l3, 28, 142, 40); AutoSize(l3, 20, 28, TextAlignmentOptions.TopLeft);
        if (hasThumb) ShrinkRight(l3, 214f);

        // 4行目: 集まる人数を人型シルエットで表示。
        // 主催者＋参加済みは濃い色、まだ空いている枠は薄い色（例: 3人募集なら4体並び、最初は1体だけ濃い）
        bool twoButtons = mine && !closed && p.ReportCount == 0;
        int capacity = 1 + p.Seats; // 主催者ぶんを含めた集まる人数
        int filled = Mathf.Clamp(1 + p.Participants.Count, 1, capacity);
        var seatsRow = new GameObject("__Seats");
        seatsRow.transform.SetParent(card.transform, false);
        var srt2 = seatsRow.AddComponent<RectTransform>();
        srt2.anchorMin = srt2.anchorMax = new Vector2(0f, 1f);
        srt2.pivot = new Vector2(0f, 1f);
        srt2.anchoredPosition = new Vector2(28f, -184f);
        srt2.sizeDelta = new Vector2(capacity * 46f + 8f, 48f);
        for (int i = 0; i < capacity; i++)
            BuildPersonSilhouette(seatsRow.transform, new Vector2(22f + i * 46f, -24f),
                i < filled ? C_SIL_ON : C_SIL_OFF);

        // シルエットの右に状態の短いテキスト
        string statusText = closed ? "締め切りました" : full ? "満員" : $"あと{p.Seats - p.Participants.Count}人";
        var st = MakeLabel(card.transform, statusText, _jp, 26,
            closed || full ? FontStyles.Bold : FontStyles.Normal,
            closed ? C_MUTED : C_TITLE, 260, 48, Vector2.zero);
        var strt = st.GetComponent<RectTransform>();
        strt.anchorMin = strt.anchorMax = new Vector2(0f, 1f);
        strt.pivot = new Vector2(0f, 1f);
        strt.anchoredPosition = new Vector2(40f + capacity * 46f, -184f);
        st.GetComponent<TextMeshProUGUI>().alignment = TextAlignmentOptions.MidlineLeft;

        // 参加者の名前とひとこと（自分の未締切カードは右下ボタン2つと重ならないよう短めに）
        if (p.Participants.Count > 0)
        {
            string members = "参加: " + string.Join("・", p.Participants.Select(x =>
                $"{x.name}さん（{JOIN_MSGS[Mathf.Clamp(x.msg, 0, JOIN_MSGS.Length - 1)]}）"));
            float memH = twoButtons ? 30f : 72f;
            var mem = MakeLabel(card.transform, members, _jp, 24, FontStyles.Normal, C_TITLE, 0, memH, Vector2.zero);
            StretchTop(mem, 28, 238, memH); AutoSize(mem, 16, 24, TextAlignmentOptions.TopLeft);
            mem.GetComponent<TextMeshProUGUI>().enableWordWrapping = true;
            mem.GetComponent<RectTransform>().offsetMax = new Vector2(-250f, -238f); // 右下のボタンを避ける
        }

        // 通報付きの自分の募集（他人には非表示になっている）
        if (p.ReportCount > 0 && mine)
        {
            var warn = MakeLabel(card.transform, "通報があり確認中です（他の人には表示されていません）",
                _jp, 24, FontStyles.Bold, C_DANGER, 0, 36, Vector2.zero);
            StretchTop(warn, 28, 316, 36); AutoSize(warn, 16, 24, TextAlignmentOptions.TopLeft);
            warn.GetComponent<RectTransform>().offsetMax = new Vector2(-250f, -316f);
        }

        // 下段ボタン（カード右下に右詰めで並べる）
        if (mine)
        {
            // 通報で非表示中の募集は締め切る意味がないので「取り消す」だけにする
            if (twoButtons)
                MakeCardButton(card.transform, C_BORDER, "締め切る", -250f, () => SetStatusAsync(p, "closed").Forget());
            MakeCardButton(card.transform, C_DANGER, "取り消す", -30f, () => DeleteAsync(p).Forget());
        }
        else if (joined)
        {
            MakeCardButton(card.transform, C_WHITE, "参加をやめる", -30f, () => LeaveAsync(p).Forget());
        }
        else if (!closed && !full)
        {
            MakeCardButton(card.transform, C_GOLD, "参加する", -30f, () => OpenJoinPopup(p));
            // 通報（小さめのリンク）
            var rep = MakeLabel(card.transform, "通報", _jp, 26, FontStyles.Underline, C_MUTED, 100, 60, Vector2.zero);
            var rrt = rep.GetComponent<RectTransform>();
            rrt.anchorMin = rrt.anchorMax = new Vector2(0f, 0f);
            rrt.pivot = new Vector2(0f, 0f);
            rrt.anchoredPosition = new Vector2(28f, 24f);
            rep.GetComponent<TextMeshProUGUI>().raycastTarget = true;
            var rbtn = rep.AddComponent<Button>();
            rbtn.transition = Selectable.Transition.None;
            rbtn.onClick.AddListener(() => OpenReportPopup(p));
        }

        // 管理者だけ: 持ち込み募集に表示用タイトルを設定するリンク
        // （タイトルを設定すると『○○』（持ち込み）表示になり、登録画像も出る）
        if (AdminClaim.IsAdmin && p.Game == GAME_BRING_OWN)
        {
            bool hasReportLink = !mine && !joined && !closed && !full;
            var set = MakeLabel(card.transform, "タイトル設定", _jp, 26, FontStyles.Underline, C_MUTED, 220, 60, Vector2.zero);
            var srt = set.GetComponent<RectTransform>();
            srt.anchorMin = srt.anchorMax = new Vector2(0f, 0f);
            srt.pivot = new Vector2(0f, 0f);
            srt.anchoredPosition = new Vector2(hasReportLink ? 150f : 28f, 24f); // 通報リンクの右に避ける
            set.GetComponent<TextMeshProUGUI>().raycastTarget = true;
            var sbtn = set.AddComponent<Button>();
            sbtn.transition = Selectable.Transition.None;
            sbtn.onClick.AddListener(() => OpenTitleSetter(p));
        }
    }

    private void MakeCardButton(Transform card, Color bg, string text, float xFromRight, Action onClick)
    {
        var go = MakeButton(card, bg, text, _jp, 30, bg == C_GOLD || bg == C_WHITE ? C_INK : Color.white,
            210, 84, Vector2.zero, () => onClick());
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot = new Vector2(1f, 0f);
        rt.anchoredPosition = new Vector2(xFromRight, 24f);
    }

    private static string DateLabel(string date)
    {
        var now = DateTime.Now;
        if (date == DateStr(now)) return "今日";
        if (date == DateStr(now.AddDays(1))) return "明日";
        return MonthDayLabel(date);
    }

    /// <summary>"yyyy-MM-dd" → "7/15(水)" 形式。</summary>
    private static string MonthDayLabel(string date)
    {
        if (!DateTime.TryParseExact(date, "yyyy-MM-dd", null,
                System.Globalization.DateTimeStyles.None, out var d))
            return date;
        return $"{d.Month}/{d.Day}({WEEKDAYS[(int)d.DayOfWeek]})";
    }

    /// <summary>作成フォームの日にち表示（今日・明日は日付も添える）。</summary>
    private static string FormDateDisplay(string date)
    {
        var now = DateTime.Now;
        if (date == DateStr(now)) return $"今日 {MonthDayLabel(date)}";
        if (date == DateStr(now.AddDays(1))) return $"明日 {MonthDayLabel(date)}";
        return MonthDayLabel(date);
    }

    /// <summary>
    /// ゲーム名の表示。Firestore の文字列をそのまま出さず、ローカルの
    /// カタログに存在するタイトルか既知の定型だけを表示する（悪意ある文字列の表示防止）。
    ///
    /// 例外: game_custom は店側が Firebase コンソール/Admin SDK で直接設定する
    /// フィールド（持ち込みゲームのタイトルを後から入れる用途）。ルールの
    /// hasOnly/affectedKeys 制約によりクライアントからは作成時も更新時も
    /// 一切書き込めないため、そのまま表示してよい唯一のゲーム名文字列。
    /// </summary>
    private static string GameDisplay(Post p)
    {
        if (!string.IsNullOrEmpty(p.GameCustom))
            return p.Game == GAME_BRING_OWN ? $"『{p.GameCustom}』（持ち込み）" : $"『{p.GameCustom}』";
        if (string.IsNullOrEmpty(p.Game)) return "ゲームはお店できめる";
        if (p.Game == GAME_BRING_OWN) return "持ち込みゲームであそぶ";
        var data = BoardGameCatalog.Load();
        var hit = data?.games?.FirstOrDefault(g => g.title == p.Game);
        return hit != null ? $"『{hit.title}』" : "（取り扱い外のゲーム）";
    }

    /// <summary>
    /// この募集に表示するゲーム画像のURL一覧（最大3枚。無ければ null）。
    /// URLは管理者しか書けない master/boardgame_images 由来だが、念のため
    /// 自プロジェクトの Storage 配信URLだけ許可する（多重防御）。
    /// </summary>
    private List<string> ImageUrlsFor(Post p)
    {
        string title = !string.IsNullOrEmpty(p.GameCustom) ? p.GameCustom
            : (!string.IsNullOrEmpty(p.Game) && p.Game != GAME_BRING_OWN) ? p.Game
            : null;
        if (title == null || !_gameImages.TryGetValue(title, out var urls)) return null;
        var safe = urls.Where(u => u != null
            && u.StartsWith("https://firebasestorage.googleapis.com/")).ToList();
        return safe.Count > 0 ? safe : null;
    }

    /// <summary>画像をディスクキャッシュ優先で読み込み、カードのImageに貼る。</summary>
    private async UniTask LoadThumbAsync(string url, Image target)
    {
        try
        {
            if (!_thumbCache.TryGetValue(url, out var tex) || tex == null)
            {
                string file = System.IO.Path.Combine(ThumbCacheDir, Md5(url) + ".jpg");
                if (System.IO.File.Exists(file))
                {
                    tex = new Texture2D(2, 2, TextureFormat.RGB24, false);
                    if (!tex.LoadImage(System.IO.File.ReadAllBytes(file))) { Destroy(tex); tex = null; }
                }
                if (tex == null)
                {
                    using (var www = UnityEngine.Networking.UnityWebRequest.Get(url))
                    {
                        await www.SendWebRequest().ToUniTask();
                        if (this == null) return;
                        var bytes = www.downloadHandler.data;
                        tex = new Texture2D(2, 2, TextureFormat.RGB24, false);
                        if (!tex.LoadImage(bytes))
                        {
                            Destroy(tex);
                            Debug.LogWarning($"[Recruit] 画像のデコードに失敗: {url}");
                            return;
                        }
                        try
                        {
                            // 差し替えでURLが変わると古いファイルが残るため、たまり過ぎたら作り直す
                            if (System.IO.Directory.Exists(ThumbCacheDir)
                                && System.IO.Directory.GetFiles(ThumbCacheDir).Length > 100)
                                System.IO.Directory.Delete(ThumbCacheDir, true);
                            System.IO.Directory.CreateDirectory(ThumbCacheDir);
                            // 再圧縮せず受信したバイト列をそのまま保存（JPGの世代劣化を防ぐ）
                            System.IO.File.WriteAllBytes(file, bytes);
                        }
                        catch { /* キャッシュ保存の失敗は無視（表示はできる） */ }
                    }
                }
                _thumbCache[url] = tex;
            }
            if (this == null || target == null) return;
            var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            _thumbSprites.Add(sprite);
            target.sprite = sprite;
            target.color = Color.white;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Recruit] ゲーム画像の読み込みに失敗: {ex.Message}");
        }
    }

    private static string Md5(string s)
    {
        using (var md5 = System.Security.Cryptography.MD5.Create())
        {
            var bytes = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(s));
            var sb = new System.Text.StringBuilder();
            foreach (var b in bytes) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }

    private static void ShrinkRight(GameObject go, float right)
    {
        var rt = go.GetComponent<RectTransform>();
        rt.offsetMax = new Vector2(-right, rt.offsetMax.y);
    }

    /// <summary>小さな人型シルエット（頭の丸＋肩の角丸。AccountButtonのアイコンと同じ構成の縮小版）。</summary>
    private static void BuildPersonSilhouette(Transform parent, Vector2 pos, Color color)
    {
        var root = new GameObject("__Person");
        root.transform.SetParent(parent, false);
        var rt = root.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(40, 40);

        var head = new GameObject("__Head");
        head.transform.SetParent(root.transform, false);
        var hrt = head.AddComponent<RectTransform>();
        hrt.sizeDelta = new Vector2(16, 16);
        hrt.anchoredPosition = new Vector2(0, 10);
        var himg = head.AddComponent<Image>();
        UICircleSprite.Apply(himg);
        himg.color = color;
        himg.raycastTarget = false;

        var body = new GameObject("__Body");
        body.transform.SetParent(root.transform, false);
        var brt = body.AddComponent<RectTransform>();
        brt.sizeDelta = new Vector2(28, 16);
        brt.anchoredPosition = new Vector2(0, -8);
        var bimg = body.AddComponent<Image>();
        RoundedRectSprite.Apply(bimg);
        bimg.color = color;
        bimg.raycastTarget = false;
    }

    // ================================================================
    // 募集作成フォーム
    // ================================================================
    private void OpenCreateForm()
    {
        if (!RequireName()) return;
        string uid = MyUid;
        if (_posts.Any(p => p.Uid == uid && p.Status == "open"))
        {
            FriendMenuController.ShowToast("募集は1人1件までです\n（今の募集を取り消せば作れます）");
            return;
        }

        // 既定値: 今日のこれからいちばん近い枠。閉店後なら明日の開店時間
        var now = DateTime.Now;
        int slot = Mathf.CeilToInt((now.Hour * 60 + now.Minute) / (float)SLOT_STEP) * SLOT_STEP;
        _formDate = DateStr(now);
        if (slot < SLOT_MIN) slot = SLOT_MIN;
        if (slot > SLOT_MAX) { _formDate = DateStr(now.AddDays(1)); slot = SLOT_MIN; }
        _formSlot = slot;
        _formGame = "";
        _formSeats = 1;
        _formTags.Clear();
        _formTagChips.Clear();

        StartCoroutine(BuildCreateForm());
    }

    /// <summary>フォームUIをフレーム分散で構築する（TMP大量生成のOOM対策）。</summary>
    private IEnumerator BuildCreateForm()
    {
        var panel = OpenPopupPanel(920, 1500);

        MakeLabel(panel.transform, "募集をつくる", _jp, 48, FontStyles.Bold, C_TITLE, 700, 70, new Vector2(0, 680));

        // --- ゲーム ---
        MakeLabel(panel.transform, "あそぶゲーム", _jp, 30, FontStyles.Bold, C_MUTED, 780, 44, new Vector2(0, 600));
        var gameBtn = MakeButton(panel.transform, C_WHITE, "", _jp, 32, C_INK, 780, 96, new Vector2(0, 525), OpenGamePicker);
        _formGameLabel = gameBtn.GetComponentInChildren<TextMeshProUGUI>();
        _formGameLabel.text = "ゲームはお店できめる（タップで選ぶ）";
        yield return null;

        // --- 日にち（タップでカレンダーを開く。今日〜30日先まで） ---
        MakeLabel(panel.transform, "日にち", _jp, 30, FontStyles.Bold, C_MUTED, 780, 44, new Vector2(0, 440));
        var dateBtn = MakeButton(panel.transform, C_WHITE, "", _jp, 34, C_INK, 780, 90, new Vector2(0, 365), OpenCalendarPicker);
        _formDateLabel = dateBtn.GetComponentInChildren<TextMeshProUGUI>();

        // --- 時間 ---
        MakeLabel(panel.transform, "集まる時間", _jp, 30, FontStyles.Bold, C_MUTED, 780, 44, new Vector2(0, 280));
        var slotBtn = MakeButton(panel.transform, C_WHITE, "", _jp, 40, C_INK, 380, 96, new Vector2(-200, 202), OpenSlotPicker);
        _formSlotLabel = slotBtn.GetComponentInChildren<TextMeshProUGUI>();

        // --- 人数 ---
        MakeLabel(panel.transform, "募集する人数", _jp, 30, FontStyles.Bold, C_MUTED, 360, 44, new Vector2(210, 280));
        MakeButton(panel.transform, C_CHIP, "－", _jp, 44, C_INK, 90, 90, new Vector2(80, 202), () => StepSeats(-1));
        _formSeatsLabel = MakeLabel(panel.transform, "", _jp, 36, FontStyles.Bold, C_TITLE, 170, 90, new Vector2(210, 202))
            .GetComponent<TextMeshProUGUI>();
        MakeButton(panel.transform, C_CHIP, "＋", _jp, 44, C_INK, 90, 90, new Vector2(340, 202), () => StepSeats(1));
        yield return null;

        // --- タグ（2列×6行、1フレーム6個まで生成） ---
        MakeLabel(panel.transform, $"ひとこと（{MAX_TAGS}つまで えらべます）", _jp, 30, FontStyles.Bold, C_MUTED, 780, 44, new Vector2(0, 120));
        for (int i = 0; i < TAGS.Length; i++)
        {
            int id = i;
            float x = (i % 2 == 0) ? -200 : 200;
            float y = 45 - (i / 2) * 100;
            var chip = MakeButton(panel.transform, C_CHIP, TAGS[i], _jp, 28, C_INK, 380, 84, new Vector2(x, y),
                () => ToggleTag(id));
            _formTagChips.Add((id, chip.GetComponent<Image>(), chip.GetComponentInChildren<TextMeshProUGUI>()));
            if (i % 6 == 5) yield return null;
        }

        // --- 決定・やめる ---
        MakeButton(panel.transform, C_GOLD, "募集する", _jp, 36, C_INK, 380, 100, new Vector2(-200, -640),
            () => CreateAsync().Forget());
        MakeButton(panel.transform, C_WHITE, "やめる", _jp, 32, C_MUTED, 380, 100, new Vector2(200, -640), ClosePopup);

        RefreshFormLabels();
    }

    private void SelectFormDate(DateTime day)
    {
        _formDate = DateStr(day);
        // 今日を選んだのに過ぎた時間だったら直近の枠へ進める
        if (_formDate == DateStr(DateTime.Now))
        {
            var now = DateTime.Now;
            int nowSlot = Mathf.CeilToInt((now.Hour * 60 + now.Minute) / (float)SLOT_STEP) * SLOT_STEP;
            if (_formSlot < nowSlot) _formSlot = Mathf.Clamp(nowSlot, SLOT_MIN, SLOT_MAX);
        }
        RefreshFormLabels();
    }

    // ================================================================
    // カレンダーピッカー（今日〜30日先だけ選べる月めくり式）
    // ================================================================
    private void OpenCalendarPicker()
    {
        var panel = OpenSubPopupPanel(860, 1240);
        MakeLabel(panel.transform, "日にちをえらぶ", _jp, 40, FontStyles.Bold, C_TITLE, 700, 60, new Vector2(0, 550));

        // 月ナビ（前の月・年月・次の月）
        _calPrevBtn = MakeButton(panel.transform, C_CHIP, "前の月", _jp, 26, C_INK, 170, 76, new Vector2(-290, 440), () => StepCalMonth(-1))
            .GetComponent<Button>();
        _calMonthLabel = MakeLabel(panel.transform, "", _jp, 38, FontStyles.Bold, C_TITLE, 340, 76, new Vector2(0, 440))
            .GetComponent<TextMeshProUGUI>();
        _calNextBtn = MakeButton(panel.transform, C_CHIP, "次の月", _jp, 26, C_INK, 170, 76, new Vector2(290, 440), () => StepCalMonth(1))
            .GetComponent<Button>();

        // 曜日ヘッダー
        for (int i = 0; i < 7; i++)
        {
            var wd = MakeLabel(panel.transform, WEEKDAYS[i].ToString(), _jp, 28, FontStyles.Bold,
                i == 0 ? C_DANGER : i == 6 ? new Color(0.20f, 0.35f, 0.65f, 1f) : C_MUTED,
                100, 44, new Vector2(-330 + i * 110, 350));
            wd.GetComponent<TextMeshProUGUI>().alignment = TextAlignmentOptions.Center;
        }

        // 日付セルの入れ物（月めくりのたびに中身だけ作り直す）
        _calGridRoot = new GameObject("__CalGrid");
        _calGridRoot.transform.SetParent(panel.transform, false);
        var grt = _calGridRoot.AddComponent<RectTransform>();
        grt.anchorMin = grt.anchorMax = new Vector2(0.5f, 0.5f);
        grt.sizeDelta = Vector2.zero;

        MakeButton(panel.transform, C_WHITE, "とじる", _jp, 30, C_MUTED, 360, 84, new Vector2(0, -560), CloseSubPopup);

        // 選択中の日付の月から表示を始める
        var sel = DateTime.TryParseExact(_formDate, "yyyy-MM-dd", null,
            System.Globalization.DateTimeStyles.None, out var d) ? d : DateTime.Now;
        _calMonth = new DateTime(sel.Year, sel.Month, 1);
        RefreshCalendar();
    }

    private void StepCalMonth(int dir)
    {
        _calMonth = _calMonth.AddMonths(dir);
        RefreshCalendar();
    }

    private void RefreshCalendar()
    {
        if (_calGridRoot == null) return;
        var today = DateTime.Today;
        var last = today.AddDays(MAX_DAYS_AHEAD);
        var firstMonth = new DateTime(today.Year, today.Month, 1);
        var lastMonth = new DateTime(last.Year, last.Month, 1);
        if (_calMonth < firstMonth) _calMonth = firstMonth;
        if (_calMonth > lastMonth) _calMonth = lastMonth;

        _calMonthLabel.text = $"{_calMonth.Year}年{_calMonth.Month}月";
        _calPrevBtn.gameObject.SetActive(_calMonth > firstMonth);
        _calNextBtn.gameObject.SetActive(_calMonth < lastMonth);

        if (_calBuild != null) StopCoroutine(_calBuild);
        _calBuild = StartCoroutine(BuildCalendarDays(today, last));
    }

    /// <summary>日付セルを1フレーム7個までで生成する（TMP大量生成のOOM対策）。</summary>
    private IEnumerator BuildCalendarDays(DateTime today, DateTime last)
    {
        for (int i = _calGridRoot.transform.childCount - 1; i >= 0; i--)
            Destroy(_calGridRoot.transform.GetChild(i).gameObject);

        int days = DateTime.DaysInMonth(_calMonth.Year, _calMonth.Month);
        int firstDow = (int)_calMonth.DayOfWeek;
        for (int d = 1; d <= days; d++)
        {
            if (_calGridRoot == null) yield break; // ピッカーが閉じられた
            var day = new DateTime(_calMonth.Year, _calMonth.Month, d);
            int cell = firstDow + d - 1;
            var pos = new Vector2(-330 + (cell % 7) * 110, 240 - (cell / 7) * 104);
            bool selectable = day >= today && day <= last;
            bool selected = DateStr(day) == _formDate;

            var go = MakeButton(_calGridRoot.transform,
                selected ? C_CHIP_ON : selectable ? C_WHITE : new Color(0f, 0f, 0f, 0.05f),
                d.ToString(), _jp, 30, selectable ? C_INK : new Color(0.52f, 0.38f, 0.22f, 0.35f),
                100, 92, pos, null);
            // カレンダーのマス目は密集して影が濁るので、影だけ外す（グラデ・押下は残す）
            var cellShadow = go.transform.Find("__Shadow");
            if (cellShadow != null) Destroy(cellShadow.gameObject);
            var btn = go.GetComponent<Button>();
            if (selectable)
            {
                var picked = day;
                btn.onClick.AddListener(() => { SelectFormDate(picked); CloseSubPopup(); });
            }
            else
            {
                btn.interactable = false;
            }
            if (d % 7 == 0) yield return null;
        }
        _calBuild = null;
    }

    private void StepSeats(int d)
    {
        _formSeats = Mathf.Clamp(_formSeats + d, 1, MAX_SEATS);
        RefreshFormLabels();
    }

    private void ToggleTag(int id)
    {
        if (_formTags.Contains(id)) _formTags.Remove(id);
        else if (_formTags.Count >= MAX_TAGS) { FriendMenuController.ShowToast($"ひとことは{MAX_TAGS}つまでです"); return; }
        else _formTags.Add(id);
        RefreshFormLabels();
    }

    private void RefreshFormLabels()
    {
        if (_formSlotLabel != null) _formSlotLabel.text = SlotText(_formSlot);
        if (_formSeatsLabel != null) _formSeatsLabel.text = $"あと{_formSeats}人";
        if (_formDateLabel != null) _formDateLabel.text = FormDateDisplay(_formDate);
        if (_formGameLabel != null)
            _formGameLabel.text = _formGame == GAME_BRING_OWN ? "持ち込みゲームであそぶ"
                : string.IsNullOrEmpty(_formGame) ? "ゲームはお店できめる（タップで選ぶ）"
                : _formGame;
        foreach (var (id, bg, label) in _formTagChips)
        {
            bool on = _formTags.Contains(id);
            bg.color = on ? C_CHIP_ON : C_CHIP;
            label.fontStyle = on ? FontStyles.Bold : FontStyles.Normal;
        }
    }

    /// <summary>時間枠ピッカー（22枠。1フレーム6行まで生成）。</summary>
    private void OpenSlotPicker()
    {
        StartCoroutine(BuildScrollPicker("集まる時間", Enumerable
            .Range(0, (SLOT_MAX - SLOT_MIN) / SLOT_STEP + 1)
            .Select(i => SLOT_MIN + i * SLOT_STEP)
            .Select(s => (SlotText(s), (Action)(() => { _formSlot = s; RefreshFormLabels(); })))
            .ToList()));
    }

    /// <summary>
    /// ゲームピッカー。検索入力＋候補8行（行は使い回すので大量生成しない）。
    /// 選べるのはカタログにあるタイトルだけ（自由文をゲーム欄に入れる余地を作らない）。
    /// </summary>
    private void OpenGamePicker()
    {
        var panel = OpenSubPopupPanel(880, 1240);
        MakeLabel(panel.transform, "あそぶゲームをえらぶ", _jp, 40, FontStyles.Bold, C_TITLE, 700, 60, new Vector2(0, 550));

        var games = BoardGameCatalog.Load()?.games ?? new BoardGameEntry[0];

        // 検索欄
        var input = BuildSearchInput(panel.transform, new Vector2(0, 460), 760, 90, "タイトルで検索…");

        // 「きめない」「持ち込み」ボタン（横並び）
        MakeButton(panel.transform, C_CHIP, "お店できめる", _jp, 28, C_INK, 372, 84, new Vector2(-196, 360),
            () => { _formGame = ""; RefreshFormLabels(); CloseSubPopup(); });
        MakeButton(panel.transform, C_CHIP, "持ち込みゲーム", _jp, 28, C_INK, 372, 84, new Vector2(196, 360),
            () => { _formGame = GAME_BRING_OWN; RefreshFormLabels(); CloseSubPopup(); });

        // 候補リスト（仮想スクロール。全740件でも行は十数個しか作らない）
        var picker = TitlePickerList.Create(panel.transform, new Vector2(0, -100), 780, 800, _jp,
            title => { _formGame = title; RefreshFormLabels(); CloseSubPopup(); });
        var allTitles = games.Select(g => g.title).Where(t => !string.IsNullOrEmpty(t))
            .OrderBy(t => t, StringComparer.Ordinal).ToList();
        picker.PrewarmAsync(allTitles);

        void Refresh(string q)
        {
            q = (q ?? "").Trim().ToLowerInvariant();
            picker.SetItems(string.IsNullOrEmpty(q) ? allTitles
                : games.Where(g =>
                        (g.title != null && g.title.ToLowerInvariant().Contains(q))
                        || (g.title_en != null && g.title_en.ToLowerInvariant().Contains(q)))
                    .Select(g => g.title).Where(t => !string.IsNullOrEmpty(t))
                    .OrderBy(t => t, StringComparer.Ordinal).ToList());
        }
        input.onValueChanged.AddListener(Refresh);
        Refresh("");

        MakeButton(panel.transform, C_WHITE, "とじる", _jp, 30, C_MUTED, 360, 84, new Vector2(0, -560), CloseSubPopup);
    }

    // ================================================================
    // 参加・通報
    // ================================================================
    private void OpenJoinPopup(Post p)
    {
        if (!RequireName()) return;
        var panel = OpenSubPopupPanel(860, 980);
        MakeLabel(panel.transform, "ひとことえらんで参加", _jp, 40, FontStyles.Bold, C_TITLE, 700, 60, new Vector2(0, 420));
        MakeLabel(panel.transform, $"{DateLabel(p.Date)} {SlotText(p.Slot)}～　{p.Name}さんの募集",
            _jp, 28, FontStyles.Normal, C_MUTED, 760, 44, new Vector2(0, 350));
        for (int i = 0; i < JOIN_MSGS.Length; i++)
        {
            int msg = i;
            MakeButton(panel.transform, C_WHITE, JOIN_MSGS[i], _jp, 30, C_INK, 700, 96, new Vector2(0, 250 - i * 115),
                () => JoinAsync(p, msg).Forget());
        }
        MakeButton(panel.transform, C_WHITE, "やめる", _jp, 30, C_MUTED, 360, 84, new Vector2(0, -400), CloseSubPopup);
    }

    private void OpenReportPopup(Post p)
    {
        var panel = OpenSubPopupPanel(860, 880);
        MakeLabel(panel.transform, "この募集を通報する", _jp, 40, FontStyles.Bold, C_TITLE, 700, 60, new Vector2(0, 370));
        MakeLabel(panel.transform, "通報すると この募集は表示されなくなり\nお店が内容を確認します",
            _jp, 28, FontStyles.Normal, C_MUTED, 760, 80, new Vector2(0, 280));
        for (int i = 0; i < REPORT_REASONS.Length; i++)
        {
            int reason = i;
            MakeButton(panel.transform, C_WHITE, REPORT_REASONS[i], _jp, 30, C_INK, 700, 96, new Vector2(0, 160 - i * 115),
                () => ReportAsync(p, reason).Forget());
        }
        MakeButton(panel.transform, C_WHITE, "やめる", _jp, 30, C_MUTED, 360, 84, new Vector2(0, -350), CloseSubPopup);
    }

    // ================================================================
    // 画像の拡大ビューア（カードのサムネイルをタップで開く。最大3枚を送り見）
    // ================================================================
    private void OpenImageViewer(List<string> urls, string title)
    {
        CloseSubPopup();
        _subPopup = BuildDimmed("__ImageViewer", CloseSubPopup);
        _viewerUrls = urls;
        _viewerIndex = 0;

        var tl = MakeLabel(_subPopup.transform, title, _jp, 34, FontStyles.Bold, Color.white, 0, 60, Vector2.zero);
        var tlrt = tl.GetComponent<RectTransform>();
        tlrt.anchorMin = new Vector2(0f, 1f); tlrt.anchorMax = new Vector2(1f, 1f);
        tlrt.pivot = new Vector2(0.5f, 1f);
        tlrt.offsetMin = new Vector2(40f, -160f); tlrt.offsetMax = new Vector2(-40f, -100f);
        AutoSize(tl, 24, 34, TextAlignmentOptions.Center);

        var imgGO = new GameObject("__ViewerImage");
        imgGO.transform.SetParent(_subPopup.transform, false);
        var irt = imgGO.AddComponent<RectTransform>();
        irt.anchorMin = Vector2.zero; irt.anchorMax = Vector2.one;
        irt.offsetMin = new Vector2(40f, 260f); irt.offsetMax = new Vector2(-40f, -180f);
        _viewerImg = imgGO.AddComponent<Image>();
        _viewerImg.preserveAspect = true;
        _viewerImg.raycastTarget = false;

        // ×ボタン（右上）
        var close = MakeButton(_subPopup.transform, C_CLOSE, "✕", _jp, 48, Color.white, 110, 110, Vector2.zero, CloseSubPopup);
        var crt = close.GetComponent<RectTransform>();
        crt.anchorMin = crt.anchorMax = new Vector2(1f, 1f);
        crt.pivot = new Vector2(1f, 1f);
        crt.anchoredPosition = new Vector2(-24f, -24f);

        if (urls.Count > 1)
        {
            var prev = MakeButton(_subPopup.transform, C_WHITE, "まえ", _jp, 32, C_INK, 220, 96, Vector2.zero,
                () => { _viewerIndex--; ShowViewerPage(); });
            var prt = prev.GetComponent<RectTransform>();
            prt.anchorMin = prt.anchorMax = new Vector2(0f, 0f);
            prt.pivot = new Vector2(0f, 0f);
            prt.anchoredPosition = new Vector2(48f, 80f);

            var next = MakeButton(_subPopup.transform, C_WHITE, "つぎ", _jp, 32, C_INK, 220, 96, Vector2.zero,
                () => { _viewerIndex++; ShowViewerPage(); });
            var nrt = next.GetComponent<RectTransform>();
            nrt.anchorMin = nrt.anchorMax = new Vector2(1f, 0f);
            nrt.pivot = new Vector2(1f, 0f);
            nrt.anchoredPosition = new Vector2(-48f, 80f);

            _viewerPage = MakeLabel(_subPopup.transform, "", _jp, 30, FontStyles.Bold, Color.white, 200, 96, Vector2.zero)
                .GetComponent<TextMeshProUGUI>();
            var pgRt = _viewerPage.GetComponent<RectTransform>();
            pgRt.anchorMin = pgRt.anchorMax = new Vector2(0.5f, 0f);
            pgRt.pivot = new Vector2(0.5f, 0f);
            pgRt.anchoredPosition = new Vector2(0f, 80f);
        }

        ShowViewerPage();
    }

    private void ShowViewerPage()
    {
        if (_viewerImg == null || _viewerUrls == null || _viewerUrls.Count == 0) return;
        _viewerIndex = (_viewerIndex + _viewerUrls.Count) % _viewerUrls.Count;
        if (_viewerPage != null) _viewerPage.text = $"{_viewerIndex + 1} / {_viewerUrls.Count}";
        _viewerImg.sprite = null;
        _viewerImg.color = new Color(1f, 1f, 1f, 0f); // 読み込み完了までは透明
        LoadThumbAsync(_viewerUrls[_viewerIndex], _viewerImg).Forget();
    }

    // ================================================================
    // 管理者用: 持ち込み募集への表示用タイトル設定（game_custom）
    // ================================================================
    private void OpenTitleSetter(Post p)
    {
        var panel = OpenSubPopupPanel(880, 1400);
        MakeLabel(panel.transform, "ゲームのタイトルを設定", _jp, 40, FontStyles.Bold, C_TITLE, 760, 60, new Vector2(0, 630));
        MakeLabel(panel.transform, "設定するとタイトルと登録画像が募集に表示されます", _jp, 26, FontStyles.Normal, C_MUTED, 780, 44, new Vector2(0, 560));

        var games = BoardGameCatalog.Load()?.games ?? new BoardGameEntry[0];
        var input = BuildSearchInput(panel.transform, new Vector2(0, 480), 760, 90, "タイトルを検索または入力…");

        // 候補リスト（仮想スクロール）
        var picker = TitlePickerList.Create(panel.transform, new Vector2(0, -25), 780, 890, _jp,
            title => SetGameCustomAsync(p, title).Forget());
        var allTitles = games.Select(g => g.title).Where(t => !string.IsNullOrEmpty(t))
            .OrderBy(t => t, StringComparer.Ordinal).ToList();
        picker.PrewarmAsync(allTitles);

        // カタログに無い名前もそのまま設定できる（持ち込みゲーム用）
        var customBtn = MakeButton(panel.transform, C_BORDER, "", _jp, 28, C_TITLE, 760, 96, new Vector2(0, -520), null);
        var customLabel = customBtn.GetComponentInChildren<TextMeshProUGUI>();
        AutoSize(customLabel.gameObject, 20, 28, TextAlignmentOptions.Center);
        customBtn.SetActive(false);

        void Refresh(string q)
        {
            q = (q ?? "").Trim();
            string qLower = q.ToLowerInvariant();
            picker.SetItems(string.IsNullOrEmpty(qLower) ? allTitles
                : games.Where(g =>
                        (g.title != null && g.title.ToLowerInvariant().Contains(qLower))
                        || (g.title_en != null && g.title_en.ToLowerInvariant().Contains(qLower)))
                    .Select(g => g.title).Where(t => !string.IsNullOrEmpty(t))
                    .OrderBy(t => t, StringComparer.Ordinal).ToList());

            bool custom = !string.IsNullOrEmpty(q) && q.Length <= 30
                && !games.Any(g => g.title == q);
            customBtn.SetActive(custom);
            if (custom)
            {
                if (_jp != null) _jp.TryAddCharacters(q, out _);
                customLabel.text = $"この名前にする：「{q}」";
                var btn = customBtn.GetComponent<Button>();
                btn.onClick.RemoveAllListeners();
                string picked = q;
                btn.onClick.AddListener(() => SetGameCustomAsync(p, picked).Forget());
            }
        }
        input.onValueChanged.AddListener(Refresh);
        Refresh("");

        // 消す=空文字で保存（クライアントは空をタイトル未設定として扱う）
        MakeButton(panel.transform, C_WHITE, "タイトルを消す", _jp, 28, C_MUTED, 372, 84, new Vector2(-196, -630),
            () => SetGameCustomAsync(p, "").Forget());
        MakeButton(panel.transform, C_WHITE, "とじる", _jp, 30, C_MUTED, 372, 84, new Vector2(196, -630), CloseSubPopup);
    }

    /// <summary>game_custom を書き込む（管理者のみルールが許可。一覧はリスナーが自動更新）。</summary>
    private async UniTask SetGameCustomAsync(Post p, string title)
    {
        if (_busy) return;
        _busy = true;
        try
        {
            await Db.Collection("recruits").Document(p.Id)
                .UpdateAsync("game_custom", title).AsUniTask();
            CloseSubPopup();
            FriendMenuController.ShowToast(string.IsNullOrEmpty(title)
                ? "タイトルを消しました" : $"『{title}』を設定しました");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Recruit] タイトル設定エラー: {ex.Message}");
            FriendMenuController.ShowToast("設定に失敗しました\n（管理者アカウントでログインしていますか？）");
        }
        finally { _busy = false; }
    }

    // ================================================================
    // Firestore 書き込み
    // ================================================================
    private bool RequireName()
    {
        if (!string.IsNullOrEmpty(MyName)) return true;
        FriendMenuController.ShowToast("先に「アカウント」から\nユーザー名を設定してください");
        return false;
    }

    private async UniTask CreateAsync()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            // 期限 = 集合時刻 + 猶予30分（ルール側で「2日以内」を強制）
            var day = DateTime.ParseExact(_formDate, "yyyy-MM-dd", null);
            var expires = new DateTime(day.Year, day.Month, day.Day, 0, 0, 0, DateTimeKind.Local)
                .AddMinutes(_formSlot + GRACE_MINUTES);

            await Db.Collection("recruits").AddAsync(new Dictionary<string, object>
            {
                { "uid", MyUid },
                { "name", MyName },
                { "game", _formGame },
                { "date", _formDate },
                { "slot", _formSlot },
                { "seats", _formSeats },
                { "tags", _formTags.OrderBy(t => t).ToList() },
                { "status", "open" },
                { "participants", new Dictionary<string, object>() },
                { "reports", new Dictionary<string, object>() },
                { "created_at", FieldValue.ServerTimestamp },
                { "expires_at", Timestamp.FromDateTime(expires.ToUniversalTime()) },
            }).AsUniTask();

            ClosePopup();
            FriendMenuController.ShowToast("募集を出しました！");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Recruit] 作成エラー: {ex.Message}");
            FriendMenuController.ShowToast("通信に失敗しました\nもう一度お試しください");
        }
        finally { _busy = false; }
    }

    private async UniTask JoinAsync(Post p, int msg)
    {
        if (_busy) return;
        _busy = true;
        try
        {
            await Db.Collection("recruits").Document(p.Id).UpdateAsync(new Dictionary<FieldPath, object>
            {
                { new FieldPath("participants", MyUid), new Dictionary<string, object>
                    { { "name", MyName }, { "msg", msg } } },
            }).AsUniTask();
            CloseSubPopup();
            FriendMenuController.ShowToast("参加を伝えました！\n当日お店で声をかけてね");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Recruit] 参加エラー: {ex.Message}");
            FriendMenuController.ShowToast("通信に失敗しました\nもう一度お試しください");
        }
        finally { _busy = false; }
    }

    private async UniTask LeaveAsync(Post p)
    {
        if (_busy) return;
        _busy = true;
        try
        {
            await Db.Collection("recruits").Document(p.Id).UpdateAsync(new Dictionary<FieldPath, object>
            {
                { new FieldPath("participants", MyUid), FieldValue.Delete },
            }).AsUniTask();
            FriendMenuController.ShowToast("参加を取り消しました");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Recruit] 参加取消エラー: {ex.Message}");
            FriendMenuController.ShowToast("通信に失敗しました\nもう一度お試しください");
        }
        finally { _busy = false; }
    }

    private async UniTask SetStatusAsync(Post p, string status)
    {
        if (_busy) return;
        _busy = true;
        try
        {
            await Db.Collection("recruits").Document(p.Id)
                .UpdateAsync("status", status).AsUniTask();
            FriendMenuController.ShowToast("募集を締め切りました");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Recruit] 更新エラー: {ex.Message}");
            FriendMenuController.ShowToast("通信に失敗しました\nもう一度お試しください");
        }
        finally { _busy = false; }
    }

    private async UniTask DeleteAsync(Post p)
    {
        if (_busy) return;
        _busy = true;
        try
        {
            await Db.Collection("recruits").Document(p.Id).DeleteAsync().AsUniTask();
            FriendMenuController.ShowToast("募集を取り消しました");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Recruit] 削除エラー: {ex.Message}");
            FriendMenuController.ShowToast("通信に失敗しました\nもう一度お試しください");
        }
        finally { _busy = false; }
    }

    private async UniTask ReportAsync(Post p, int reason)
    {
        if (_busy) return;
        _busy = true;
        try
        {
            await Db.Collection("recruits").Document(p.Id).UpdateAsync(new Dictionary<FieldPath, object>
            {
                { new FieldPath("reports", MyUid), reason },
            }).AsUniTask();
            CloseSubPopup();
            FriendMenuController.ShowToast("通報しました\nご協力ありがとうございます");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Recruit] 通報エラー: {ex.Message}");
            FriendMenuController.ShowToast("通信に失敗しました\nもう一度お試しください");
        }
        finally { _busy = false; }
    }

    // ================================================================
    // ポップアップ骨格
    // ================================================================
    private GameObject _subPopup; // ポップアップの上に重ねるピッカー類

    private GameObject OpenPopupPanel(float w, float h)
    {
        ClosePopup();
        _popup = BuildDimmed("__RecruitPopup", ClosePopup);
        return BuildPanel(_popup.transform, w, h);
    }

    private GameObject OpenSubPopupPanel(float w, float h)
    {
        CloseSubPopup();
        _subPopup = BuildDimmed("__RecruitSubPopup", CloseSubPopup);
        return BuildPanel(_subPopup.transform, w, h);
    }

    private GameObject BuildDimmed(string name, Action onTapOutside)
    {
        var go = new GameObject(name);
        go.transform.SetParent(_canvas.transform, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        var dim = go.AddComponent<Image>();
        dim.color = UITheme.DIM;
        var btn = go.AddComponent<Button>();
        btn.transition = Selectable.Transition.None;
        btn.onClick.AddListener(() => onTapOutside());
        return go;
    }

    private GameObject BuildPanel(Transform parent, float w, float h)
    {
        var border = MakeRect("__Border", parent, C_BORDER, w + 24, h + 24);
        RoundedRectSprite.Apply(border.GetComponent<Image>());
        UITheme.ElevateCard(border, 18f, 10f, 0.35f); // モーダルを浮かせる
        var panel = MakeRect("__Panel", border.transform, C_PARCHMENT, w, h);
        RoundedRectSprite.Apply(panel.GetComponent<Image>());
        panel.AddComponent<Button>().transition = Selectable.Transition.None; // パネル内タップで閉じない
        return panel;
    }

    private void ClosePopup()
    {
        CloseSubPopup();
        if (_popup != null) Destroy(_popup);
        _popup = null;
        _formGameLabel = null; _formSlotLabel = null; _formSeatsLabel = null;
        _formDateLabel = null;
        _formTagChips.Clear();
    }

    private void CloseSubPopup()
    {
        if (_calBuild != null) { StopCoroutine(_calBuild); _calBuild = null; }
        _calGridRoot = null; _calMonthLabel = null; _calPrevBtn = null; _calNextBtn = null;
        _viewerUrls = null; _viewerImg = null; _viewerPage = null;
        if (_subPopup != null) Destroy(_subPopup);
        _subPopup = null;
    }

    /// <summary>スクロール式の選択肢ピッカー（時間枠用）。行は1フレーム6個まで生成。</summary>
    private IEnumerator BuildScrollPicker(string title, List<(string label, Action onPick)> items)
    {
        var panel = OpenSubPopupPanel(700, 1300);
        MakeLabel(panel.transform, title, _jp, 40, FontStyles.Bold, C_TITLE, 600, 60, new Vector2(0, 580));

        var svGO = new GameObject("__PickerScroll");
        svGO.transform.SetParent(panel.transform, false);
        var svrt = svGO.AddComponent<RectTransform>();
        svrt.anchorMin = new Vector2(0f, 0f); svrt.anchorMax = new Vector2(1f, 1f);
        svrt.offsetMin = new Vector2(40f, 150f); svrt.offsetMax = new Vector2(-40f, -130f);
        var scroll = svGO.AddComponent<ScrollRect>();
        scroll.horizontal = false; scroll.vertical = true; scroll.scrollSensitivity = 40f;
        scroll.movementType = ScrollRect.MovementType.Clamped;

        var vpGO = new GameObject("__VP");
        vpGO.transform.SetParent(svGO.transform, false);
        var vprt = vpGO.AddComponent<RectTransform>();
        vprt.anchorMin = Vector2.zero; vprt.anchorMax = Vector2.one;
        vprt.offsetMin = vprt.offsetMax = Vector2.zero;
        vpGO.AddComponent<RectMask2D>();
        scroll.viewport = vprt;

        var ctGO = new GameObject("__Content");
        ctGO.transform.SetParent(vpGO.transform, false);
        var ct = ctGO.AddComponent<RectTransform>();
        ct.anchorMin = new Vector2(0f, 1f); ct.anchorMax = new Vector2(1f, 1f);
        ct.pivot = new Vector2(0.5f, 1f);
        ct.offsetMin = ct.offsetMax = Vector2.zero;
        scroll.content = ct;

        const float ROW_H = 110f;
        ct.sizeDelta = new Vector2(0f, items.Count * ROW_H + 20f);
        for (int i = 0; i < items.Count; i++)
        {
            var (label, onPick) = items[i];
            var row = MakeButton(ct, C_WHITE, label, _jp, 36, C_INK, 0, 96, Vector2.zero,
                () => { onPick(); CloseSubPopup(); });
            var rt = row.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(10f, -10f - i * ROW_H - 96f);
            rt.offsetMax = new Vector2(-10f, -10f - i * ROW_H);
            if (i % 6 == 5) yield return null;
        }

        MakeButton(panel.transform, C_WHITE, "とじる", _jp, 30, C_MUTED, 360, 84, new Vector2(0, -595), CloseSubPopup);
    }

    // ================================================================
    // グリフ事前登録（実機OOM対策。詳細は BoardGameListController 参照）
    // ================================================================
    private static readonly HashSet<char> _warmed = new HashSet<char>();

    private IEnumerator PrewarmGlyphs(IEnumerable<string> texts)
    {
        if (_jp == null) yield break;
        var sb = new System.Text.StringBuilder();
        foreach (var s in texts)
        {
            if (s == null) continue;
            foreach (var c in s) if (_warmed.Add(c)) sb.Append(c);
        }
        string all = sb.ToString();
        for (int i = 0; i < all.Length; i += 40)
        {
            _jp.TryAddCharacters(all.Substring(i, Mathf.Min(40, all.Length - i)), out _);
            yield return null;
        }
    }

    // ================================================================
    // UI部品ヘルパー
    // ================================================================
    private static TMP_FontAsset GetJpFont()
    {
        var fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
        return fonts.FirstOrDefault(f => f.name.ToLower() == "jp") ?? fonts.FirstOrDefault();
    }

    private static void StretchTop(GameObject go, float x, float top, float h)
    {
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.offsetMin = new Vector2(x, -top - h);
        rt.offsetMax = new Vector2(-x, -top);
    }

    private static void AutoSize(GameObject labelGO, float min, float max, TextAlignmentOptions align)
    {
        var tmp = labelGO.GetComponent<TextMeshProUGUI>();
        tmp.enableAutoSizing = true;
        tmp.fontSizeMin = min; tmp.fontSizeMax = max;
        tmp.alignment = align;
    }

    private static GameObject MakeRect(string name, Transform parent, Color color, float w, float h)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>().sizeDelta = new Vector2(w, h);
        go.AddComponent<Image>().color = color;
        return go;
    }

    private static GameObject MakeLabel(Transform parent, string text, TMP_FontAsset font,
        float size, FontStyles style, Color color, float w, float h, Vector2 pos)
    {
        var go = new GameObject("__Label");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(w, h);
        rt.anchoredPosition = pos;
        var tmp = go.AddComponent<TextMeshProUGUI>();
        if (font != null) tmp.font = font;
        tmp.text = text;
        tmp.fontSize = size;
        tmp.fontStyle = style;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;
        // 幅0で作ってあとからアンカーで広げるラベルがあるため、既定は折り返さない
        // （TMP既定の折り返しONだと幅0の瞬間に1文字ずつ縦に並んでしまう）
        tmp.enableWordWrapping = false;
        return go;
    }

    private static GameObject MakeButton(Transform parent, Color bg, string text, TMP_FontAsset font,
        float fontSize, Color textColor, float w, float h, Vector2 pos,
        UnityEngine.Events.UnityAction onClick)
    {
        var go = MakeRect("__Btn_" + text, parent, bg, w, h);
        RoundedRectSprite.Apply(go.GetComponent<Image>());
        go.GetComponent<Image>().color = bg;
        go.GetComponent<RectTransform>().anchoredPosition = pos;
        // ラベルはボタンいっぱいに追従させる（w=0で作ってアンカーで広げるボタン対応）
        var label = MakeLabel(go.transform, text, font, fontSize, FontStyles.Bold, textColor, w, h, Vector2.zero);
        var lrt = label.GetComponent<RectTransform>();
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
        lrt.offsetMin = lrt.offsetMax = Vector2.zero;
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = go.GetComponent<Image>();
        if (onClick != null) btn.onClick.AddListener(onClick);
        // デザイン基盤: 明るい面は白グラデ、濃い面は控えめグラデで磨く（透明ヒットエリアは除外）
        if (bg.a >= 0.5f)
        {
            if (bg.r + bg.g + bg.b >= 2.4f) UITheme.PolishButton(go.GetComponent<Image>());
            else UITheme.PolishDarkButton(go.GetComponent<Image>());
        }
        return go;
    }

    /// <summary>検索入力欄（BoardGameListController の検索ボックスと同じ構成）。</summary>
    private TMP_InputField BuildSearchInput(Transform parent, Vector2 pos, float w, float h, string placeholder)
    {
        var barGO = new GameObject("__SearchInput");
        barGO.transform.SetParent(parent, false);
        var brt = barGO.AddComponent<RectTransform>();
        brt.sizeDelta = new Vector2(w, h);
        brt.anchoredPosition = pos;
        var bimg = barGO.AddComponent<Image>();
        RoundedRectSprite.Apply(bimg);
        bimg.color = C_WHITE;

        var areaGO = new GameObject("__TextArea");
        areaGO.transform.SetParent(barGO.transform, false);
        var art = areaGO.AddComponent<RectTransform>();
        art.anchorMin = Vector2.zero; art.anchorMax = Vector2.one;
        art.offsetMin = new Vector2(28f, 6f); art.offsetMax = new Vector2(-28f, -6f);
        areaGO.AddComponent<RectMask2D>();

        var phGO = new GameObject("__Placeholder");
        phGO.transform.SetParent(areaGO.transform, false);
        var prt = phGO.AddComponent<RectTransform>();
        prt.anchorMin = Vector2.zero; prt.anchorMax = Vector2.one;
        prt.offsetMin = prt.offsetMax = Vector2.zero;
        var ptmp = phGO.AddComponent<TextMeshProUGUI>();
        if (_jp != null) ptmp.font = _jp;
        ptmp.text = placeholder;
        ptmp.fontSize = 30; ptmp.color = C_MUTED; ptmp.fontStyle = FontStyles.Italic;
        ptmp.alignment = TextAlignmentOptions.Left;

        var txGO = new GameObject("__Text");
        txGO.transform.SetParent(areaGO.transform, false);
        var txrt = txGO.AddComponent<RectTransform>();
        txrt.anchorMin = Vector2.zero; txrt.anchorMax = Vector2.one;
        txrt.offsetMin = txrt.offsetMax = Vector2.zero;
        var ttmp = txGO.AddComponent<TextMeshProUGUI>();
        if (_jp != null) ttmp.font = _jp;
        ttmp.fontSize = 30; ttmp.color = C_INK;
        ttmp.alignment = TextAlignmentOptions.Left;

        var input = barGO.AddComponent<TMP_InputField>();
        input.textViewport = art;
        input.textComponent = ttmp;
        input.placeholder = ptmp;
        input.fontAsset = _jp;
        input.pointSize = 30;
        input.customCaretColor = true;
        input.caretColor = C_INK;
        input.selectionColor = new Color(0.84f, 0.66f, 0.18f, 0.4f);
        input.lineType = TMP_InputField.LineType.SingleLine;
        return input;
    }
}
