using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Cysharp.Threading.Tasks;
using Firebase.Firestore;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// フレンド（新ホームの右上の人のアイコンから開く、下から出るシート）。旧ホームのフレンド機能（FriendMenuController）の宇宙デザイン版で、
/// 書き込む中身は旧ホームと同じ:
///  ・フレンド一覧 … ログイン時に読んだ UserData.Friends を使う（読み取りなし）。お気に入りが先頭、あとはフレンドになった日が新しい順。
///                   入店中を知らせる設定のフレンドがお店にいれば「入店中」（presence/store を開いたときに1回読む）
///  ・自分のQRを表示 … "friend:{uid}:{名前}" のQR。相手がホームのカメラで読むと、おたがいにフレンドになる（CallMethodFromQR.AddFriend）。
///                     表示している間は自分の users ドキュメントを購読し、読み取られたらその場で知らせる
///  ・入店中をフレンドに知らせる … users.share_presence（旧ホームの「ログイン情報を公開する」と同じ設定）
///  ・フレンドを押す … お気に入りの追加・解除（自分の側だけ）、フレンドを解除（自分と相手の両方から消す）
///  ・ユーザー名を変更 … 12文字まで・1週間に1回まで（旧ホームのアカウント画面と同じ決まり）。
///                       フレンドの一覧に出る自分の名前も全員の分を書き換える（旧ホームのフレンド画面と同じ）
/// 相席募集（SpaceNewsPage）では、募集した人・参加した人の名前をフレンドにだけ表示する（SpaceFriends.IsFriend）。
/// </summary>
public class SpaceFriendPanel : MonoBehaviour
{
    private const string T_CAPTION = "FRIENDS";
    private const string T_TITLE = "フレンド";
    private const string T_SHOW_QR = "自分のQRを表示";
    private const string T_SHARE = "入店中をフレンドに知らせる";
    private const string T_SHARE_SUB = "オンにすると、お店にいる間フレンドの一覧に「入店中」と出ます";
    private const string T_COUNT_A = "フレンド ";
    private const string T_COUNT_B = "人";
    private const string T_EMPTY = "まだフレンドがいません";
    private const string T_EMPTY_SUB = "QRを見せ合って、フレンドになろう";
    private const string T_FOOTER = "フレンドのQRは、ホームのカメラで読み取ると登録できます";
    private const string T_IN_STORE = "入店中";
    private const string T_SINCE = "から";
    private const string T_FAV_ADD = "お気に入りに追加";
    private const string T_FAV_REMOVE = "お気に入りを解除";
    private const string T_UNFRIEND = "フレンドを解除";
    private const string T_CLOSE = "とじる";
    private const string T_UNFRIEND_TITLE = "フレンドを解除しますか？";
    private const string T_UNFRIEND_BODY_A = "さんとのフレンドを解除します";
    private const string T_UNFRIEND_BODY_B = "解除すると、おたがいのフレンド一覧から消えます";
    private const string T_UNFRIEND_OK = "解除する";
    private const string T_UNFRIENDING = "解除しています…";
    private const string T_UNFRIENDED = "さんとのフレンドを解除しました";
    private const string T_FAV_ADDED = "さんをお気に入りに追加しました";
    private const string T_FAV_REMOVED = "さんのお気に入りを解除しました";
    private const string T_SHARE_ON = "入店中をフレンドに知らせます";
    private const string T_SHARE_OFF = "入店中をフレンドに知らせないようにしました";
    private const string T_FAILED = "通信に失敗しました\nもう一度お試しください";
    private const string T_NO_ACCOUNT = "ユーザー情報を読み込めませんでした";
    private const string T_QR_CAPTION = "FRIEND QR";
    private const string T_QR_TITLE = "フレンド登録QR";
    private const string T_QR_NOTE = "フレンドにホームのカメラでこのQRを読み取ってもらうと、おたがいにフレンドになります";
    private const string T_QR_BACK = "一覧にもどる";
    private const string T_SAN = "さん";
    private const string T_ADDED = "さんとフレンドになりました！";
    private const string T_RENAME = "ユーザー名を変更";
    private const string T_RENAME_HINT = "フレンドや相席募集で表示される名前です\n（12文字まで・変更は1週間に1回まで）";
    private const string T_RENAME_WAIT_A = "ユーザー名は変更したばかりです\n（あと";
    private const string T_RENAME_WAIT_B = "日で変更できます）";
    private const string T_RENAME_PLACEHOLDER = "ユーザー名を入力";
    private const string T_RENAME_OK = "変更する";
    private const string T_RENAME_CANCEL = "キャンセル";
    private const string T_RENAMING = "変更しています…";
    private const string T_RENAMED_A = "ユーザー名を「";
    private const string T_RENAMED_B = "」に変更しました";
    private const string T_NAME_EMPTY = "ユーザー名を入力してください";
    private const string T_NAME_LONG = "ユーザー名は12文字以内で入力してください";
    private const string T_NAME_EMOJI = "ユーザー名に絵文字は使えません";
    private const string GOLD_HEX = "#FFD37A";

    // 旧ホームのアカウント画面（AccountButton）と同じ決まり。12文字は相席募集のルール（名前は12文字まで）とも対
    private const int NAME_MAX = 12;
    private const int RENAME_COOLDOWN_DAYS = 7;

    /// <summary>この画面で表示しうる固定の文字（フレンドの名前は開くときに登録する）</summary>
    public static string GlyphText
    {
        get
        {
            var sb = new StringBuilder();
            sb.Append(T_CAPTION).Append(T_TITLE).Append(T_SHOW_QR).Append(T_SHARE).Append(T_SHARE_SUB)
              .Append(T_COUNT_A).Append(T_COUNT_B).Append(T_EMPTY).Append(T_EMPTY_SUB).Append(T_FOOTER)
              .Append(T_IN_STORE).Append(T_SINCE).Append(T_FAV_ADD).Append(T_FAV_REMOVE).Append(T_UNFRIEND)
              .Append(T_CLOSE).Append(T_UNFRIEND_TITLE).Append(T_UNFRIEND_BODY_A).Append(T_UNFRIEND_BODY_B)
              .Append(T_UNFRIEND_OK).Append(T_UNFRIENDING).Append(T_UNFRIENDED).Append(T_FAV_ADDED)
              .Append(T_FAV_REMOVED).Append(T_SHARE_ON).Append(T_SHARE_OFF).Append(T_FAILED).Append(T_NO_ACCOUNT)
              .Append(T_QR_CAPTION).Append(T_QR_TITLE).Append(T_QR_NOTE).Append(T_QR_BACK).Append(T_SAN)
              .Append(T_ADDED).Append(T_RENAME).Append(T_RENAME_HINT).Append(T_RENAME_WAIT_A).Append(T_RENAME_WAIT_B)
              .Append(T_RENAME_PLACEHOLDER).Append(T_RENAME_OK).Append(T_RENAME_CANCEL).Append(T_RENAMING)
              .Append(T_RENAMED_A).Append(T_RENAMED_B).Append(T_NAME_EMPTY).Append(T_NAME_LONG).Append(T_NAME_EMOJI)
              .Append("★0123456789/キャンセル");
            return sb.ToString();
        }
    }

    private const float SIDE = 56f;
    private const float TOP_MARGIN = 96f, BOTTOM_MARGIN = 32f;
    private const float QR_BTN_TOP = 196f, QR_BTN_H = 112f;
    private const float SHARE_TOP = 330f, SHARE_H = 112f;
    private const float COUNT_Y = 486f;     // 「フレンド n人」の中心（シートの上端から）
    private const float LIST_TOP = 516f;
    private const float FOOTER_H = 112f;
    private const float ROW_H = 124f, ROW_GAP = 16f;
    private const float EMPTY_H = 260f;
    private const float QR_CARD = 760f, QR_IMAGE = 680f;

    /// <summary>ユーザー名を変えたとき（新ホームの自分のカードの名前を新しくする）</summary>
    public static event Action NameChanged;

    private Canvas _canvas;
    private Action _onClosed;
    private CanvasGroup _group;
    private RectTransform _sheet, _content, _actions, _qr, _rename;
    private TMP_InputField _nameInput;
    private Vector2 _sheetBase;
    private float _innerW;
    private TextMeshProUGUI _countLabel;
    private Image _shareTrack;
    private RectTransform _shareKnob;
    private Dictionary<string, Timestamp> _presence; // 入店中（uid → 入店した時刻）
    private Texture2D _qrTex;
    private ListenerRegistration _listener;
    private Coroutine _openAnim;
    private bool _closing, _busy;

    private static UserDataManager Manager => UserDataManager.instance;
    private static UserData Data => Manager != null ? Manager.UserData : null;

    public static SpaceFriendPanel Open(Canvas canvas, Action onClosed)
    {
        var root = SpaceTheme.Rect("__SpaceFriends", canvas.transform);
        SpaceTheme.Stretch(root);
        root.SetAsLastSibling();
        SpaceTheme.SubCanvas(root.gameObject, true); // 一覧のスクロールでホーム全体を描き直さないように
        var panel = root.gameObject.AddComponent<SpaceFriendPanel>();
        panel._canvas = canvas;
        panel._onClosed = onClosed;
        panel.StartCoroutine(panel.Build());
        return panel;
    }

    /// <summary>戻る操作: 名前の変更・フレンドの操作・自分のQRを開いていればそれを、なければ閉じる</summary>
    public void HandleBack()
    {
        if (_rename != null) CloseRename();
        else if (_actions != null) CloseActions();
        else if (_qr != null) CloseMyQr();
        else Close();
    }

    public void Close()
    {
        if (_closing || _busy) return;
        _closing = true;
        if (_openAnim != null) StopCoroutine(_openAnim);
        StartCoroutine(CloseRoutine());
    }

    private IEnumerator CloseRoutine()
    {
        float from = _group.alpha;
        yield return SpaceTheme.Tween(0.18f, 0f, k =>
        {
            _group.alpha = from * (1f - k);
            if (_sheet != null) _sheet.anchoredPosition = _sheetBase + new Vector2(0f, -60f * k);
        });
        Destroy(gameObject);
    }

    private void OnDestroy()
    {
        StopListener();
        if (_qrTex != null) Destroy(_qrTex);
        _onClosed?.Invoke();
    }

    // ================================================================
    // シート
    // ================================================================
    private IEnumerator Build()
    {
        var rt = (RectTransform)transform;
        _group = gameObject.AddComponent<CanvasGroup>();
        _group.alpha = 0f;

        var dim = SpaceTheme.Rect("Dim", rt);
        SpaceTheme.Stretch(dim);
        var dimImg = SpaceTheme.Img(dim, null, SpaceTheme.DIM, true);
        SpaceTheme.Clickable(dim.gameObject, dimImg, Close, false);

        // フレンドの名前は事前に分からないので、40文字/フレームでフォントへ登録してから組む（TMP動的フォントのOOM対策）
        var names = new StringBuilder();
        var data = Data;
        if (data != null)
        {
            names.Append(data.Username);
            foreach (var kv in data.Friends) names.Append(kv.Value.Name);
        }
        yield return SpaceTheme.Prewarm(names.ToString());
        if (_closing) yield break;

        BuildSheet();
        _openAnim = StartCoroutine(SpaceTheme.Tween(0.32f, 0f, k =>
        {
            _group.alpha = k;
            _sheet.anchoredPosition = _sheetBase + new Vector2(0f, -80f * (1f - k));
        }));
        LoadPresenceAsync().Forget();
    }

    private void BuildSheet()
    {
        var safe = SpaceTheme.Rect("Safe", transform);
        SpaceTheme.Stretch(safe);
        safe.gameObject.AddComponent<SpaceSafeArea>();

        var size = SpaceTheme.CanvasSize(_canvas);
        var insets = SpaceTheme.SafeInsets(size);
        float safeH = size.y - insets.top - insets.bottom;
        _innerW = size.x - 64f - SIDE * 2f;

        // 高さはフレンドの人数に合わせる（入りきらなければ一覧をスクロール）
        int n = Data != null ? Data.Friends.Count : 0;
        float listH = n == 0 ? EMPTY_H : n * (ROW_H + ROW_GAP) - ROW_GAP + 8f;
        float sheetH = Mathf.Min(LIST_TOP + listH + FOOTER_H, safeH - TOP_MARGIN - BOTTOM_MARGIN);

        _sheet = SpaceTheme.Rect("Sheet", safe);
        _sheet.anchorMin = new Vector2(0f, 0f);
        _sheet.anchorMax = new Vector2(1f, 0f);
        _sheet.pivot = new Vector2(0.5f, 0f);
        _sheet.sizeDelta = new Vector2(-64f, sheetH);
        _sheet.anchoredPosition = new Vector2(0f, BOTTOM_MARGIN);
        _sheetBase = _sheet.anchoredPosition;
        SpaceTheme.Glass(_sheet, 56f, SpaceTheme.GLASS_DEEP, null, true); // シート上のタップが暗幕へ抜けないように

        var caption = SpaceTheme.Text(_sheet, "Caption", T_CAPTION, 24, SpaceTheme.A(SpaceTheme.CYAN, 0.85f),
            new Vector2(0f, 1f), new Vector2(SIDE, -70f), new Vector2(400f, 36f), FontStyles.Bold,
            TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
        SpaceTheme.Tracking(caption, 18f);
        SpaceTheme.Text(_sheet, "Title", T_TITLE, 56, SpaceTheme.TEXT, new Vector2(0f, 1f), new Vector2(SIDE, -128f),
            new Vector2(560f, 72f), FontStyles.Bold, TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
        CloseButton(_sheet, -96f, Close);

        // 自分のQRを表示 ・ ユーザー名を変更（旧ホームのフレンド画面と同じく横に並べる）
        float half = (_innerW - 24f) * 0.5f;
        var qr = SpaceTheme.Rect("ShowQr", _sheet, new Vector2(0f, 1f), new Vector2(SIDE, -QR_BTN_TOP),
            new Vector2(half, QR_BTN_H), new Vector2(0f, 1f));
        var qrFace = SpaceTheme.PrimaryFace(qr);
        SpaceTheme.Clickable(qr.gameObject, qrFace, OpenMyQr);
        var qrIcon = SpaceTheme.Rect("Icon", qr, SpaceTheme.CENTER, new Vector2(-150f, 0f), new Vector2(40f, 40f));
        SpaceTheme.Img(qrIcon, SpaceSprites.QrIcon, SpaceTheme.TEXT_DARK);
        var qrLabel = SpaceTheme.Text(qr, "Label", T_SHOW_QR, 34, SpaceTheme.TEXT_DARK, SpaceTheme.CENTER,
            new Vector2(24f, 0f), new Vector2(half - 130f, 56f), FontStyles.Bold);
        qrLabel.enableAutoSizing = true;
        qrLabel.fontSizeMin = 24f;
        qrLabel.fontSizeMax = 34f;
        var rename = SpaceTheme.Rect("Rename", _sheet, new Vector2(0f, 1f), new Vector2(SIDE + half + 24f, -QR_BTN_TOP),
            new Vector2(half, QR_BTN_H), new Vector2(0f, 1f));
        var renameFace = SpaceTheme.Glass(rename, 46f, SpaceTheme.A(new Color(0.09f, 0.12f, 0.30f), 0.7f), null, true);
        SpaceTheme.Clickable(rename.gameObject, renameFace, OpenRename);
        var renameLabel = SpaceTheme.Text(rename, "Label", T_RENAME, 34, SpaceTheme.TEXT, FontStyles.Bold);
        SpaceTheme.Stretch(renameLabel.rectTransform, 16f, 0f, 16f, 0f);
        renameLabel.enableAutoSizing = true;
        renameLabel.fontSizeMin = 24f;
        renameLabel.fontSizeMax = 34f;

        // 入店中をフレンドに知らせる（オン・オフのスイッチ。行のどこを押しても切り替わる）
        var share = SpaceTheme.Rect("Share", _sheet, new Vector2(0f, 1f), new Vector2(SIDE, -SHARE_TOP),
            new Vector2(_innerW, SHARE_H), new Vector2(0f, 1f));
        var shareFace = SpaceTheme.Glass(share, 32f, SpaceTheme.GLASS, null, true);
        SpaceTheme.Clickable(share.gameObject, shareFace, () => ToggleShareAsync().Forget());
        SpaceTheme.Text(share, "Label", T_SHARE, 30, SpaceTheme.TEXT, new Vector2(0f, 0.5f), new Vector2(32f, 18f),
            new Vector2(_innerW - 220f, 44f), FontStyles.Bold, TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
        var shareSub = SpaceTheme.Text(share, "Sub", T_SHARE_SUB, 22, SpaceTheme.TEXT_MUTED, new Vector2(0f, 0.5f),
            new Vector2(32f, -24f), new Vector2(_innerW - 210f, 34f), FontStyles.Bold, TextAlignmentOptions.Left,
            new Vector2(0f, 0.5f));
        shareSub.enableAutoSizing = true;
        shareSub.fontSizeMin = 16f;
        shareSub.fontSizeMax = 22f;
        var track = SpaceTheme.Rect("Switch", share, new Vector2(1f, 0.5f), new Vector2(-32f, 0f), new Vector2(116f, 64f),
            new Vector2(1f, 0.5f));
        _shareTrack = SpaceTheme.Rounded(track, Color.white, 31f);
        _shareKnob = SpaceTheme.Rect("Knob", track, SpaceTheme.CENTER, Vector2.zero, new Vector2(52f, 52f));
        SpaceTheme.Img(_shareKnob, SpaceSprites.Circle, Color.white);
        UpdateShareSwitch();

        _countLabel = SpaceTheme.Text(_sheet, "Count", "", 26, SpaceTheme.TEXT_MUTED, new Vector2(0f, 1f),
            new Vector2(SIDE, -COUNT_Y), new Vector2(600f, 38f), FontStyles.Bold, TextAlignmentOptions.Left,
            new Vector2(0f, 0.5f));

        // 一覧（スクロール）
        var list = SpaceTheme.Rect("List", _sheet);
        SpaceTheme.Stretch(list, SIDE, FOOTER_H, SIDE, LIST_TOP);
        var viewport = SpaceTheme.Rect("Viewport", list);
        SpaceTheme.Stretch(viewport);
        SpaceTheme.HitArea(viewport); // 行の間の余白でもドラッグできるように
        viewport.gameObject.AddComponent<RectMask2D>().softness = new Vector2Int(0, 20);
        _content = SpaceTheme.Rect("Content", viewport);
        _content.anchorMin = new Vector2(0f, 1f);
        _content.anchorMax = new Vector2(1f, 1f);
        _content.pivot = new Vector2(0.5f, 1f);
        _content.anchoredPosition = Vector2.zero;
        var scroll = list.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = _content;
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Elastic;
        scroll.scrollSensitivity = 30f;

        SpaceTheme.Text(_sheet, "Footer", T_FOOTER, 24, SpaceTheme.TEXT_MUTED, new Vector2(0.5f, 0f),
            new Vector2(0f, 56f), new Vector2(_innerW, 36f), FontStyles.Bold);

        RebuildList();
    }

    /// <summary>フレンドの行を作り直す（お気に入りが先頭、あとはフレンドになった日が新しい順）</summary>
    private void RebuildList()
    {
        if (_content == null) return;
        for (int i = _content.childCount - 1; i >= 0; i--) Destroy(_content.GetChild(i).gameObject);

        var data = Data;
        var friends = data != null
            ? data.Friends.OrderByDescending(kv => kv.Value.Favorite)
                .ThenByDescending(kv => kv.Value.Since.ToDateTime()).ToList()
            : new List<KeyValuePair<string, FriendEntry>>();
        _countLabel.text = T_COUNT_A + friends.Count + T_COUNT_B;

        if (friends.Count == 0)
        {
            SpaceTheme.Text(_content, "Empty", T_EMPTY, 34, SpaceTheme.TEXT_SUB, new Vector2(0.5f, 1f),
                new Vector2(0f, -100f), new Vector2(_innerW, 50f), FontStyles.Bold);
            SpaceTheme.Text(_content, "EmptySub", T_EMPTY_SUB, 26, SpaceTheme.TEXT_MUTED, new Vector2(0.5f, 1f),
                new Vector2(0f, -156f), new Vector2(_innerW, 40f), FontStyles.Bold);
            _content.sizeDelta = new Vector2(0f, EMPTY_H);
            return;
        }

        for (int i = 0; i < friends.Count; i++)
            BuildRow(friends[i].Key, friends[i].Value, -i * (ROW_H + ROW_GAP));
        _content.sizeDelta = new Vector2(0f, friends.Count * (ROW_H + ROW_GAP) - ROW_GAP + 8f);
    }

    /// <summary>フレンド1人: 頭文字の丸・名前（お気に入りは★）・いつから・入店中。押すとお気に入り／解除</summary>
    private void BuildRow(string uid, FriendEntry e, float y)
    {
        var row = SpaceTheme.Rect("Friend", _content);
        row.anchorMin = new Vector2(0f, 1f);
        row.anchorMax = new Vector2(1f, 1f);
        row.pivot = new Vector2(0.5f, 1f);
        row.sizeDelta = new Vector2(0f, ROW_H);
        row.anchoredPosition = new Vector2(0f, y);
        var face = SpaceTheme.Glass(row, 36f, SpaceTheme.GLASS, null, true);
        SpaceTheme.Clickable(row.gameObject, face, () => OpenActions(uid));

        string name = SpaceTheme.Safe(e.Name ?? "").Trim();
        Avatar(row, name, new Vector2(24f + 44f, 0f), 88f);

        string shown = SpaceTheme.NoParse(name) + $"<size=60%><color={SpaceTheme.SUB_HEX}> {T_SAN}</color></size>";
        if (e.Favorite) shown = $"<color={GOLD_HEX}>★</color> " + shown;
        var nameText = SpaceTheme.Text(row, "Name", shown, 36, SpaceTheme.TEXT, new Vector2(0f, 0.5f),
            new Vector2(136f, 16f), new Vector2(_innerW - 136f - 190f, 50f), FontStyles.Bold,
            TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
        nameText.enableAutoSizing = true;
        nameText.fontSizeMin = 24f;
        nameText.fontSizeMax = 36f;
        SpaceTheme.Text(row, "Since", e.Since.ToDateTime().ToLocalTime().ToString("yyyy/MM/dd") + T_SINCE, 22,
            SpaceTheme.TEXT_MUTED, new Vector2(0f, 0.5f), new Vector2(136f, -26f), new Vector2(400f, 32f),
            FontStyles.Bold, TextAlignmentOptions.Left, new Vector2(0f, 0.5f));

        // 入店中（相手が知らせる設定にしていて、お店にいるとき）
        if (_presence != null && _presence.TryGetValue(uid, out var at) && PresenceService.IsStillPresent(at))
        {
            var pill = SpaceTheme.Rect("InStore", row, new Vector2(1f, 0.5f), new Vector2(-28f, 0f),
                new Vector2(150f, 52f), new Vector2(1f, 0.5f));
            SpaceTheme.Rounded(pill, SpaceTheme.A(SpaceTheme.MINT, 0.12f), 25f);
            var edge = SpaceTheme.Rect("Edge", pill);
            SpaceTheme.Stretch(edge);
            SpaceTheme.RoundedStroke(edge, SpaceTheme.A(SpaceTheme.MINT, 0.5f), 25f);
            var dot = SpaceTheme.Rect("Dot", pill, new Vector2(0f, 0.5f), new Vector2(28f, 0f), new Vector2(14f, 14f));
            SpaceTheme.Img(dot, SpaceSprites.Circle, SpaceTheme.MINT);
            SpaceTheme.Text(pill, "Label", T_IN_STORE, 26, SpaceTheme.MINT, new Vector2(0f, 0.5f), new Vector2(46f, 0f),
                new Vector2(96f, 40f), FontStyles.Bold, TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
        }
    }

    /// <summary>名前の頭文字の丸（ホームの自分のカードのアバターと同じ見た目）</summary>
    private static void Avatar(RectTransform parent, string name, Vector2 pos, float d)
    {
        var av = SpaceTheme.Rect("Avatar", parent, new Vector2(0f, 0.5f), pos, new Vector2(d, d));
        var face = SpaceTheme.Rect("Face", av);
        SpaceTheme.Stretch(face);
        SpaceTheme.Gradient(SpaceTheme.Img(face, SpaceSprites.Circle, Color.white), SpaceTheme.CYAN, SpaceTheme.VIOLET);
        var rim = SpaceTheme.Rect("Rim", av);
        SpaceTheme.Stretch(rim);
        SpaceTheme.Img(rim, SpaceSprites.RingSmall, SpaceTheme.A(Color.white, 0.45f));
        string initial = string.IsNullOrEmpty(name) ? "?" : name.Substring(0, 1);
        if (char.IsSurrogate(initial[0])) initial = "★";
        var t = SpaceTheme.Text(av, "Initial", SpaceTheme.NoParse(initial.ToUpperInvariant()), d * 0.45f,
            SpaceTheme.TEXT, FontStyles.Bold);
        SpaceTheme.Stretch(t.rectTransform);
    }

    /// <summary>入店中のフレンド（presence/store の1ドキュメント読み取り）</summary>
    private async UniTaskVoid LoadPresenceAsync()
    {
        var presence = await PresenceService.GetPresenceAsync();
        if (this == null || _closing) return;
        _presence = presence;
        RebuildList();
    }

    // ================================================================
    // 入店中をフレンドに知らせる
    // ================================================================
    private void UpdateShareSwitch()
    {
        bool on = Data != null && Data.SharePresence;
        _shareTrack.color = on ? SpaceTheme.A(SpaceTheme.MINT, 0.85f) : new Color(0.20f, 0.24f, 0.45f, 0.9f);
        _shareKnob.anchoredPosition = new Vector2(on ? 26f : -26f, 0f);
    }

    /// <summary>
    /// 旧ホームの「ログイン情報を公開する」と同じ: users.share_presence を切り替え、オンでお店にいるなら
    /// すぐ presence/store に書く。オフにしたら自分の分を消して、だれからも見えないようにする
    /// </summary>
    private async UniTaskVoid ToggleShareAsync()
    {
        var manager = Manager;
        if (_busy || manager == null || Data == null || string.IsNullOrEmpty(manager.UID)) return;
        _busy = true;
        bool next = !Data.SharePresence;
        bool ok = false;
        try
        {
            await FirebaseFirestore.DefaultInstance.Collection("users").Document(manager.UID)
                .UpdateAsync("share_presence", next).AsUniTask();
            Data.SharePresence = next;
            if (next)
            {
                if (LocalVisitLog.HasOpenVisit()) await PresenceService.WriteAsync(true);
            }
            else
            {
                await PresenceService.WriteAsync(false);
            }
            ok = true;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Friend] 入店中を知らせる設定の変更に失敗: {ex.Message}");
        }
        _busy = false;
        SpaceTheme.Toast(ok ? (next ? T_SHARE_ON : T_SHARE_OFF) : T_FAILED, _canvas);
        if (this != null && _shareTrack != null) UpdateShareSwitch();
    }

    // ================================================================
    // フレンドを押したとき（お気に入り・フレンドを解除）
    // ================================================================
    private void OpenActions(string uid)
    {
        if (_actions != null || _qr != null || _rename != null || _busy || _closing) return;
        var data = Data;
        if (data == null || !data.Friends.TryGetValue(uid, out var e)) return;

        _actions = SpaceTheme.Rect("Actions", transform);
        SpaceTheme.Stretch(_actions);
        var group = _actions.gameObject.AddComponent<CanvasGroup>();
        var dim = SpaceTheme.Rect("Dim", _actions);
        SpaceTheme.Stretch(dim);
        var dimImg = SpaceTheme.Img(dim, null, SpaceTheme.DIM, true);
        SpaceTheme.Clickable(dim.gameObject, dimImg, CloseActions, false);
        var safe = SpaceTheme.Rect("Safe", _actions);
        SpaceTheme.Stretch(safe);
        safe.gameObject.AddComponent<SpaceSafeArea>();

        const float h = 578f;
        var sheet = SpaceTheme.Rect("Sheet", safe);
        sheet.anchorMin = new Vector2(0f, 0f);
        sheet.anchorMax = new Vector2(1f, 0f);
        sheet.pivot = new Vector2(0.5f, 0f);
        sheet.sizeDelta = new Vector2(-64f, h);
        sheet.anchoredPosition = new Vector2(0f, BOTTOM_MARGIN);
        SpaceTheme.Glass(sheet, 56f, SpaceTheme.GLASS_DEEP, null, true);

        string name = SpaceTheme.Safe(e.Name ?? "");
        var title = SpaceTheme.Text(sheet, "Name", SpaceTheme.NoParse(name) + T_SAN, 44, SpaceTheme.TEXT,
            new Vector2(0f, 1f), new Vector2(SIDE, -84f), new Vector2(_innerW - 120f, 60f), FontStyles.Bold,
            TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
        title.enableAutoSizing = true;
        title.fontSizeMin = 28f;
        title.fontSizeMax = 44f;
        SpaceTheme.Text(sheet, "Since", e.Since.ToDateTime().ToLocalTime().ToString("yyyy/MM/dd") + T_SINCE, 24,
            SpaceTheme.TEXT_MUTED, new Vector2(0f, 1f), new Vector2(SIDE, -136f), new Vector2(400f, 34f),
            FontStyles.Bold, TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
        CloseButton(sheet, -88f, CloseActions);

        // お気に入り（自分の側だけ。相手には影響しない）
        var fav = SpaceTheme.Rect("Favorite", sheet, new Vector2(0f, 1f), new Vector2(SIDE, -190f),
            new Vector2(_innerW, 104f), new Vector2(0f, 1f));
        var favFace = SpaceTheme.Glass(fav, 46f, SpaceTheme.A(SpaceTheme.GOLD, 0.12f), SpaceTheme.A(SpaceTheme.GOLD, 0.5f),
            true);
        SpaceTheme.Clickable(fav.gameObject, favFace, () => ToggleFavoriteAsync(uid).Forget());
        var favText = SpaceTheme.Text(fav, "Label", (e.Favorite ? "" : "★ ") + (e.Favorite ? T_FAV_REMOVE : T_FAV_ADD),
            36, SpaceTheme.GOLD, FontStyles.Bold);
        SpaceTheme.Stretch(favText.rectTransform);

        // フレンドを解除（確認してから）
        var un = SpaceTheme.Rect("Unfriend", sheet, new Vector2(0f, 1f), new Vector2(SIDE, -310f),
            new Vector2(_innerW, 104f), new Vector2(0f, 1f));
        var unFace = SpaceTheme.DangerFace(un);
        SpaceTheme.Clickable(un.gameObject, unFace, () => ConfirmUnfriend(uid));
        var unText = SpaceTheme.Text(un, "Label", T_UNFRIEND, 36, Color.white, FontStyles.Bold);
        SpaceTheme.Stretch(unText.rectTransform);

        var close = SpaceTheme.Rect("Close", sheet, new Vector2(0.5f, 1f), new Vector2(0f, -434f),
            new Vector2(440f, 104f), new Vector2(0.5f, 1f));
        var closeFace = SpaceTheme.Glass(close, 46f, SpaceTheme.A(new Color(0.09f, 0.12f, 0.30f), 0.63f), null, true);
        SpaceTheme.Clickable(close.gameObject, closeFace, CloseActions);
        var closeText = SpaceTheme.Text(close, "Label", T_CLOSE, 36, SpaceTheme.TEXT_SUB, FontStyles.Bold);
        SpaceTheme.Stretch(closeText.rectTransform);

        group.alpha = 0f;
        var basePos = sheet.anchoredPosition;
        StartCoroutine(SpaceTheme.Tween(0.28f, 0f, k =>
        {
            if (group == null) return;
            group.alpha = k;
            sheet.anchoredPosition = basePos + new Vector2(0f, -60f * (1f - k));
        }));
    }

    private void CloseActions()
    {
        if (_actions == null) return;
        Destroy(_actions.gameObject);
        _actions = null;
    }

    /// <summary>お気に入りの追加・解除（自分の users.friends.{uid}.favorite だけ書き換える）</summary>
    private async UniTaskVoid ToggleFavoriteAsync(string uid)
    {
        var manager = Manager;
        var data = Data;
        if (_busy || manager == null || data == null || !data.Friends.TryGetValue(uid, out var e)) return;
        _busy = true;
        CloseActions();
        bool next = !e.Favorite;
        bool ok = false;
        try
        {
            await FirebaseFirestore.DefaultInstance.Collection("users").Document(manager.UID)
                .UpdateAsync(new Dictionary<FieldPath, object>
                {
                    { new FieldPath("friends", uid, "favorite"), next },
                }).AsUniTask();
            e.Favorite = next;
            ok = true;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Friend] お気に入りの更新に失敗: {ex.Message}");
        }
        _busy = false;
        string name = SpaceTheme.NoParse(SpaceTheme.Safe(e.Name ?? ""));
        SpaceTheme.Toast(ok ? name + (next ? T_FAV_ADDED : T_FAV_REMOVED) : T_FAILED, _canvas);
        if (this != null) RebuildList();
    }

    private void ConfirmUnfriend(string uid)
    {
        var data = Data;
        if (data == null || !data.Friends.TryGetValue(uid, out var e)) return;
        CloseActions();
        string name = SpaceTheme.NoParse(SpaceTheme.Safe(e.Name ?? ""));
        SpaceDialog.Confirm(_canvas, T_UNFRIEND_TITLE, name + T_UNFRIEND_BODY_A + "\n" + T_UNFRIEND_BODY_B,
            T_UNFRIEND_OK, true, () => UnfriendAsync(uid).Forget());
    }

    /// <summary>
    /// フレンドを解除する。登録のときと同じく、自分と相手の両方の friends から1回の書き込みでまとめて消す
    /// （片方だけ残ることはない。相手のドキュメントは「自分のキー」だけ消せるルール）
    /// </summary>
    private async UniTaskVoid UnfriendAsync(string uid)
    {
        var manager = Manager;
        var data = Data;
        if (_busy || manager == null || data == null || !data.Friends.TryGetValue(uid, out var e)) return;
        _busy = true;
        var busy = SpaceDialog.Busy(_canvas, T_UNFRIENDING);
        bool ok = false;
        try
        {
            var db = FirebaseFirestore.DefaultInstance;
            var batch = db.StartBatch();
            batch.Update(db.Collection("users").Document(manager.UID), new Dictionary<FieldPath, object>
            {
                { new FieldPath("friends", uid), FieldValue.Delete },
            });
            batch.Update(db.Collection("users").Document(uid), new Dictionary<FieldPath, object>
            {
                { new FieldPath("friends", manager.UID), FieldValue.Delete },
            });
            await batch.CommitAsync().AsUniTask();
            data.Friends.Remove(uid);
            ok = true;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Friend] フレンドの解除に失敗: {ex.Message}");
        }
        _busy = false;
        if (busy != null) Destroy(busy);
        string name = SpaceTheme.NoParse(SpaceTheme.Safe(e.Name ?? ""));
        SpaceTheme.Toast(ok ? name + T_UNFRIENDED : T_FAILED, _canvas);
        if (this != null) RebuildList();
    }

    // ================================================================
    // 自分のQR（相手にホームのカメラで読み取ってもらう）
    // ================================================================
    private void OpenMyQr()
    {
        if (_qr != null || _actions != null || _rename != null || _busy || _closing) return;
        var manager = Manager;
        if (manager == null || Data == null || string.IsNullOrEmpty(manager.UID))
        {
            SpaceTheme.Toast(T_NO_ACCOUNT, _canvas);
            return;
        }
        string myName = Data.Username ?? "";
        string payload = $"{CallMethodFromQR.FRIEND_QR_PREFIX}{manager.UID}:{myName}";

        _qr = SpaceTheme.Rect("MyQr", transform);
        SpaceTheme.Stretch(_qr);
        var group = _qr.gameObject.AddComponent<CanvasGroup>();
        var dim = SpaceTheme.Rect("Dim", _qr);
        SpaceTheme.Stretch(dim);
        SpaceTheme.Img(dim, null, new Color(0.01f, 0.015f, 0.05f, 0.97f), true); // 誤タップで閉じないよう、背景では閉じない
        var safe = SpaceTheme.Rect("Safe", _qr);
        SpaceTheme.Stretch(safe);
        safe.gameObject.AddComponent<SpaceSafeArea>();

        var caption = SpaceTheme.Text(safe, "Caption", T_QR_CAPTION, 24, SpaceTheme.A(SpaceTheme.CYAN, 0.9f),
            new Vector2(0.5f, 1f), new Vector2(0f, -70f), new Vector2(700f, 36f), FontStyles.Bold);
        SpaceTheme.Tracking(caption, 18f);
        SpaceTheme.Text(safe, "Title", T_QR_TITLE, 44, SpaceTheme.TEXT, new Vector2(0.5f, 1f), new Vector2(0f, -124f),
            new Vector2(800f, 60f), FontStyles.Bold);
        CloseButtonTop(safe, CloseMyQr);

        // 白地のQR（後ろに光）
        float cardY = -(196f + QR_CARD * 0.5f);
        var glow = SpaceTheme.Rect("Glow", safe, new Vector2(0.5f, 1f), new Vector2(0f, cardY),
            new Vector2(QR_CARD + 220f, QR_CARD + 220f));
        SpaceTheme.Img(glow, SpaceSprites.Glow, SpaceTheme.A(SpaceTheme.CYAN, 0.30f));
        var card = SpaceTheme.Rect("Card", safe, new Vector2(0.5f, 1f), new Vector2(0f, cardY), new Vector2(QR_CARD, QR_CARD));
        SpaceTheme.Rounded(card, Color.white, 44f);
        var pic = SpaceTheme.Rect("Code", card, SpaceTheme.CENTER, Vector2.zero, new Vector2(QR_IMAGE, QR_IMAGE));
        var raw = pic.gameObject.AddComponent<RawImage>();
        raw.raycastTarget = false;
        if (_qrTex != null) Destroy(_qrTex);
        _qrTex = QRCodeHelper.CreateQRCode(payload, 512, 512);
        _qrTex.filterMode = FilterMode.Point;
        raw.texture = _qrTex;

        float textY = 196f + QR_CARD;
        var me = SpaceTheme.Text(safe, "Name", SpaceTheme.NoParse(SpaceTheme.Safe(myName)) + T_SAN, 44, SpaceTheme.TEXT,
            new Vector2(0.5f, 1f), new Vector2(0f, -(textY + 56f)), new Vector2(_innerW, 60f), FontStyles.Bold);
        me.enableAutoSizing = true;
        me.fontSizeMin = 28f;
        me.fontSizeMax = 44f;
        var note = SpaceTheme.Text(safe, "Note", T_QR_NOTE, 28, SpaceTheme.TEXT_SUB, new Vector2(0.5f, 1f),
            new Vector2(0f, -(textY + 136f)), new Vector2(_innerW, 84f), FontStyles.Bold);
        note.textWrappingMode = TextWrappingModes.Normal;

        var back = SpaceTheme.Rect("Back", safe, new Vector2(0.5f, 0f), new Vector2(0f, 56f), new Vector2(560f, 112f),
            new Vector2(0.5f, 0f));
        var backFace = SpaceTheme.Glass(back, 46f, new Color(0.05f, 0.07f, 0.19f, 0.92f), null, true);
        SpaceTheme.Clickable(back.gameObject, backFace, CloseMyQr);
        var backText = SpaceTheme.Text(back, "Label", T_QR_BACK, 36, SpaceTheme.TEXT, FontStyles.Bold);
        SpaceTheme.Stretch(backText.rectTransform);

        group.alpha = 0f;
        StartCoroutine(SpaceTheme.Tween(0.22f, 0f, k =>
        {
            if (group != null) group.alpha = k;
        }));
        StartListener();
    }

    private void CloseMyQr()
    {
        StopListener();
        if (_qr != null) Destroy(_qr.gameObject);
        _qr = null;
        if (_qrTex != null) Destroy(_qrTex);
        _qrTex = null;
    }

    /// <summary>
    /// QRを表示している間だけ自分の users ドキュメントを購読し、相手が読み取って friends に自分が増えたら、
    /// その場で「○○さんとフレンドになりました！」と知らせて一覧へ戻る（旧ホームと同じ）
    /// </summary>
    private void StartListener()
    {
        StopListener();
        var manager = Manager;
        if (manager == null || string.IsNullOrEmpty(manager.UID)) return;
        _listener = FirebaseFirestore.DefaultInstance.Collection("users").Document(manager.UID).Listen(snap =>
        {
            if (this == null || _qr == null || !snap.Exists) return;
            var local = Data;
            if (local == null) return;
            var fresh = snap.ConvertTo<UserData>();
            var added = new List<string>();
            foreach (var kv in fresh.Friends)
            {
                if (local.Friends.ContainsKey(kv.Key)) continue;
                local.Friends[kv.Key] = kv.Value;
                added.Add(kv.Value.Name ?? "");
            }
            // 入店中に相手がこのQRを読んだ＝一緒に来店（惑星オパーリアなど。すでにフレンドの人でも届く）
            JourneyService.MergeFriendMarks(fresh);
            if (JourneyService.HasPendingFriendMarks())
            {
                JourneyService.ProcessAndReport(r =>
                {
                    if (this != null && _qr != null && added.Count == 0) CloseMyQr();
                    SpaceNotice.ShowJourneyRewards(r, true);
                }).Forget();
            }
            if (added.Count == 0) return;

            foreach (var name in added)
                SpaceTheme.Toast(SpaceTheme.NoParse(SpaceTheme.SafeNow(name)) + T_ADDED, _canvas);
            AssetsDatabase.instance?.PlayLevelUpSE();
            CloseMyQr();
            RebuildList();
        });
    }

    private void StopListener()
    {
        _listener?.Stop();
        _listener = null;
    }

    // ================================================================
    // ユーザー名を変更
    // ================================================================
    /// <summary>
    /// 次にユーザー名を変更できる日時（UTC）。旧ホームの2つの変更画面の記録
    /// （アカウント画面の name_edited_at・フレンド画面の name_changed_at）の新しいほうから1週間
    /// </summary>
    private static DateTime NextRenameUtc(UserData d)
    {
        var a = d.NameEditedAt.ToDateTime();
        var b = d.NameChangedAt.ToDateTime();
        return (a > b ? a : b).AddDays(RENAME_COOLDOWN_DAYS);
    }

    /// <summary>
    /// 名前を入れ直すカード。キーボードで隠れないよう画面の上のほうに出す。
    /// 変更したばかり（1週間以内）なら、あと何日で変えられるかを見せて入力できないようにする
    /// </summary>
    private void OpenRename()
    {
        if (_rename != null || _qr != null || _actions != null || _busy || _closing) return;
        var data = Data;
        if (data == null || Manager == null || string.IsNullOrEmpty(Manager.UID))
        {
            SpaceTheme.Toast(T_NO_ACCOUNT, _canvas);
            return;
        }
        var next = NextRenameUtc(data);
        bool canEdit = DateTime.UtcNow >= next;

        _rename = SpaceTheme.Rect("Rename", transform);
        SpaceTheme.Stretch(_rename);
        var group = _rename.gameObject.AddComponent<CanvasGroup>();
        var dim = SpaceTheme.Rect("Dim", _rename);
        SpaceTheme.Stretch(dim);
        var dimImg = SpaceTheme.Img(dim, null, SpaceTheme.DIM, true);
        SpaceTheme.Clickable(dim.gameObject, dimImg, CloseRename, false);
        var safe = SpaceTheme.Rect("Safe", _rename);
        SpaceTheme.Stretch(safe);
        safe.gameObject.AddComponent<SpaceSafeArea>();

        const float w = 920f, h = 600f;
        var card = SpaceTheme.Rect("Card", safe, new Vector2(0.5f, 1f), new Vector2(0f, -200f), new Vector2(w, h),
            new Vector2(0.5f, 1f));
        SpaceTheme.Glass(card, 56f, SpaceTheme.GLASS_DEEP, null, true);
        SpaceTheme.Text(card, "Title", T_RENAME, 44, SpaceTheme.TEXT, new Vector2(0.5f, 1f), new Vector2(0f, -84f),
            new Vector2(w - 80f, 60f), FontStyles.Bold);
        string hint = canEdit
            ? T_RENAME_HINT
            : T_RENAME_WAIT_A + Mathf.Max(1, Mathf.CeilToInt((float)(next - DateTime.UtcNow).TotalDays)) + T_RENAME_WAIT_B;
        var hintText = SpaceTheme.Text(card, "Hint", hint, 26, canEdit ? SpaceTheme.TEXT_SUB : SpaceTheme.GOLD,
            new Vector2(0.5f, 1f), new Vector2(0f, -164f), new Vector2(w - 100f, 80f), FontStyles.Bold);
        hintText.textWrappingMode = TextWrappingModes.Normal;

        _nameInput = NameInput(card, new Vector2(0f, -276f), new Vector2(w - 100f, 104f), data.Username ?? "");
        _nameInput.interactable = canEdit;

        float by = -(h - 104f);
        var cancel = SpaceTheme.Rect("Cancel", card, new Vector2(0.5f, 1f), new Vector2(-210f, by), new Vector2(380f, 116f));
        var cancelFace = SpaceTheme.Glass(cancel, 46f, SpaceTheme.A(new Color(0.09f, 0.12f, 0.30f), 0.63f), null, true);
        SpaceTheme.Clickable(cancel.gameObject, cancelFace, CloseRename);
        var cancelText = SpaceTheme.Text(cancel, "Label", T_RENAME_CANCEL, 36, SpaceTheme.TEXT_SUB, FontStyles.Bold);
        SpaceTheme.Stretch(cancelText.rectTransform);

        var ok = SpaceTheme.Rect("Ok", card, new Vector2(0.5f, 1f), new Vector2(210f, by), new Vector2(380f, 116f));
        var okFace = SpaceTheme.PrimaryFace(ok);
        var okText = SpaceTheme.Text(ok, "Label", T_RENAME_OK, 38, SpaceTheme.TEXT_DARK, FontStyles.Bold);
        SpaceTheme.Stretch(okText.rectTransform);
        if (canEdit) SpaceTheme.Clickable(ok.gameObject, okFace, SubmitRename);
        else ok.gameObject.AddComponent<CanvasGroup>().alpha = 0.35f; // 変えられない間は押せない見た目に

        group.alpha = 0f;
        StartCoroutine(SpaceTheme.Tween(0.2f, 0f, k =>
        {
            if (group != null) group.alpha = k;
        }));
    }

    private void CloseRename()
    {
        if (_rename == null) return;
        Destroy(_rename.gameObject);
        _rename = null;
        _nameInput = null;
    }

    /// <summary>入力を確かめてから変更する（空・長すぎ・絵文字・同じ名前は書き込まない）</summary>
    private void SubmitRename()
    {
        if (_busy || _nameInput == null) return;
        var data = Data;
        if (data == null) return;
        string name = (_nameInput.text ?? "").Trim();
        if (name.Length == 0)
        {
            SpaceTheme.Toast(T_NAME_EMPTY, _canvas);
            return;
        }
        if (name.Length > NAME_MAX)
        {
            SpaceTheme.Toast(T_NAME_LONG, _canvas);
            return;
        }
        // このフォントで表示できない絵文字（サロゲートペア・異体字セレクタ・ゼロ幅接合子）は名前に使わせない
        if (name.Any(c => char.IsSurrogate(c) || c == '️' || c == '‍' || char.IsControl(c)))
        {
            SpaceTheme.Toast(T_NAME_EMOJI, _canvas);
            return;
        }
        if (name == data.Username)
        {
            CloseRename();
            return;
        }
        if (DateTime.UtcNow < NextRenameUtc(data)) return; // 念のため（ボタンは押せないようにしてある）
        RenameAsync(name).Forget();
    }

    /// <summary>
    /// 自分の users の name を変える（変更日時も記録。旧ホームの2つの変更画面の記録をどちらも更新）。
    /// そのあと、フレンドの一覧に出る名前（フレンド登録のときに相手に写した名前）も、全員の分を新しい名前にする
    /// （旧ホームのフレンド画面と同じ。相手のドキュメントは friends の自分のキーだけ変えられるルール。
    /// 一部のフレンドで失敗しても、自分の名前の変更はそのまま）
    /// </summary>
    private async UniTaskVoid RenameAsync(string name)
    {
        var manager = Manager;
        var data = Data;
        if (_busy || manager == null || data == null) return;
        _busy = true;
        CloseRename();
        var busy = SpaceDialog.Busy(_canvas, T_RENAMING);
        var db = FirebaseFirestore.DefaultInstance;
        var now = Timestamp.GetCurrentTimestamp();
        bool ok = false;
        try
        {
            await db.Collection("users").Document(manager.UID).UpdateAsync(new Dictionary<string, object>
            {
                { "name", name },
                { "name_edited_at", now },
                { "name_changed_at", now },
            }).AsUniTask();
            data.Username = name;
            data.NameEditedAt = now;
            data.NameChangedAt = now;
            ok = true;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Friend] ユーザー名の変更に失敗: {ex.Message}");
        }

        if (ok && data.Friends.Count > 0)
        {
            string myUid = manager.UID;
            await UniTask.WhenAll(data.Friends.Keys.ToList().Select(fuid => CopyNameToFriendAsync(db, fuid, myUid, name)));
        }

        _busy = false;
        if (busy != null) Destroy(busy);
        SpaceTheme.Toast(ok ? T_RENAMED_A + SpaceTheme.NoParse(SpaceTheme.SafeNow(name)) + T_RENAMED_B : T_FAILED,
            _canvas);
        if (ok) NameChanged?.Invoke();
    }

    private static async UniTask CopyNameToFriendAsync(FirebaseFirestore db, string friendUid, string myUid, string name)
    {
        try
        {
            await db.Collection("users").Document(friendUid).UpdateAsync(new Dictionary<FieldPath, object>
            {
                { new FieldPath("friends", myUid, "name"), name },
            }).AsUniTask();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Friend] フレンド({friendUid})への名前の反映に失敗: {ex.Message}");
        }
    }

    /// <summary>名前の入力欄（ガラスの枠。12文字まで。押すと全体が選ばれて、すぐ打ち直せる）</summary>
    private static TMP_InputField NameInput(RectTransform parent, Vector2 topCenter, Vector2 size, string value)
    {
        var bar = SpaceTheme.Rect("Input", parent, new Vector2(0.5f, 1f), topCenter, size, new Vector2(0.5f, 1f));
        var face = SpaceTheme.Glass(bar, 32f, new Color(0.03f, 0.04f, 0.12f, 0.9f), SpaceTheme.A(SpaceTheme.CYAN, 0.45f),
            true);
        var area = SpaceTheme.Rect("TextArea", bar);
        SpaceTheme.Stretch(area, 32f, 8f, 32f, 8f);
        area.gameObject.AddComponent<RectMask2D>();
        var ph = SpaceTheme.Text(area, "Placeholder", T_RENAME_PLACEHOLDER, 40, SpaceTheme.TEXT_MUTED, FontStyles.Bold,
            TextAlignmentOptions.Left);
        SpaceTheme.Stretch(ph.rectTransform);
        var tx = SpaceTheme.Text(area, "Text", "", 40, SpaceTheme.TEXT, FontStyles.Bold, TextAlignmentOptions.Left);
        SpaceTheme.Stretch(tx.rectTransform);

        var input = bar.gameObject.AddComponent<TMP_InputField>();
        input.targetGraphic = face;
        input.textViewport = area;
        input.textComponent = tx;
        input.placeholder = ph;
        input.fontAsset = SpaceTheme.Font;
        input.pointSize = 40;
        input.richText = false;
        input.characterLimit = NAME_MAX;
        input.lineType = TMP_InputField.LineType.SingleLine;
        input.onFocusSelectAll = true;
        input.customCaretColor = true;
        input.caretColor = SpaceTheme.CYAN;
        input.selectionColor = SpaceTheme.A(SpaceTheme.CYAN, 0.35f);
        input.text = SpaceTheme.Safe(value);
        return input;
    }

    // ================================================================
    // 部品
    // ================================================================
    /// <summary>シートの右上の閉じるボタン</summary>
    private static void CloseButton(RectTransform sheet, float y, UnityAction onClick)
    {
        var close = SpaceTheme.Rect("Close", sheet, new Vector2(1f, 1f), new Vector2(-80f, y), new Vector2(96f, 96f));
        var face = SpaceTheme.Img(close, SpaceSprites.Circle, SpaceTheme.A(new Color(0.09f, 0.12f, 0.30f), 0.65f), true);
        var rim = SpaceTheme.Rect("Rim", close);
        SpaceTheme.Stretch(rim);
        SpaceTheme.Img(rim, SpaceSprites.RingSmall, SpaceTheme.STROKE);
        SpaceTheme.Bar(close, Vector2.zero, 44f, 6f, 45f, SpaceTheme.TEXT_SUB);
        SpaceTheme.Bar(close, Vector2.zero, 44f, 6f, -45f, SpaceTheme.TEXT_SUB);
        SpaceTheme.Clickable(close.gameObject, face, onClick);
    }

    /// <summary>全画面の右上の閉じるボタン（管理者のQR一覧などと同じ）</summary>
    private static void CloseButtonTop(RectTransform safe, UnityAction onClick)
    {
        var rt = SpaceTheme.Rect("Close", safe, new Vector2(1f, 1f), new Vector2(-112f, -84f), new Vector2(104f, 104f));
        var face = SpaceTheme.Img(rt, SpaceSprites.Circle, new Color(0.05f, 0.07f, 0.19f, 0.85f), true);
        var rim = SpaceTheme.Rect("Rim", rt);
        SpaceTheme.Stretch(rim);
        SpaceTheme.Img(rim, SpaceSprites.RingSmall, SpaceTheme.STROKE);
        SpaceTheme.Bar(rt, Vector2.zero, 44f, 6f, 45f, SpaceTheme.TEXT);
        SpaceTheme.Bar(rt, Vector2.zero, 44f, 6f, -45f, SpaceTheme.TEXT);
        SpaceTheme.Clickable(rt.gameObject, face, onClick);
    }
}

/// <summary>フレンドかどうか（ログイン時に読んだ自分の users.friends。フレンドの登録・解除でその場で更新される）</summary>
public static class SpaceFriends
{
    public const bool FEATURE_ENDED = true;
    public static bool IsFriend(string uid)
    {
        // フレンド機能は 2026-11-09 にやめた。相席募集などのフレンドの印も出さない（フレンドのデータは残してある）
        if (FEATURE_ENDED) return false;
        if (string.IsNullOrEmpty(uid)) return false;
        var manager = UserDataManager.instance;
        if (manager == null || manager.UserData == null) return false;
        return manager.UserData.Friends.ContainsKey(uid);
    }
}
