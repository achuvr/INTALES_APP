using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Cysharp.Threading.Tasks;
using Firebase.Firestore;
using Firebase.Storage;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 管理者用: お知らせの画像（master/config.event_images）の編集。
/// お知らせページの「お店からのお知らせ」の見出しの右の「画像を編集」から開く全画面（admin クレームを持つアカウントにだけ出る）。
///  ・画像を追加 … 端末のアルバムから1枚選ぶ → 確認 → 縮小して JPG にし、Storage の events/ に上げて、
///                 一覧の最後（＝お知らせのいちばん上）に足す
///  ・上へ／下へ … お知らせでの並びを入れ替える（この画面で上にある画像ほど、お知らせの上に出る）
///  ・削除      … 一覧から外し、Storage の events/ にある画像ファイルも消す
/// 変更は操作のたびにサーバーへ保存する。並べ替え・削除はトランザクションで、サーバーの一覧が開いたときと
/// 違っていたら（ほかの端末で変わっていたら）書かずに読み直す。
/// 一般ユーザーのアプリにこの画面への入口はなく、実際の保護は Firestore（master は管理者のみ書き込み可）と
/// Storage（書き込みは admin クレームのみ）のルールで行う。
/// ほかの人のアプリには、次に起動したときから新しい一覧が出る（master/config は起動ごとに1回読む）。
/// </summary>
public class SpaceAdminNewsImages : MonoBehaviour
{
    private const string BUCKET = "gs://intales-a0459.firebasestorage.app";
    private const string FOLDER = "events";      // これまで手作業で上げていた画像と同じ置き場所
    private const string FIELD = "event_images";
    // 上げる画像の大きさ: お知らせの横幅いっぱいで、4倍まで拡大しても文字が読める程度。縦長のチラシも入るよう縦は長めに
    private const int MAX_W = 1440, MAX_H = 2880;
    private const int JPG_QUALITY = 88;
    private const int THUMB_MAX = 600;           // 一覧の小さい画像（長い辺）

    private const string T_CAPTION = "ADMIN ONLY";
    private const string T_TITLE = "お知らせの画像";
    private const string T_NOTE = "上にある画像ほど、お知らせの上に表示されます";
    private const string T_ADD = "画像を追加";
    private const string T_LOADING = "読み込み中…";
    private const string T_FAILED = "読み込めませんでした。通信の状態を確かめてください";
    private const string T_RETRY = "もう一度読み込む";
    private const string T_EMPTY = "まだ画像がありません";
    private const string T_EMPTY_SUB = "「画像を追加」から、お知らせに載せる画像を選べます";
    private const string T_ORDER = "枚目";
    private const string T_TOP = "お知らせのいちばん上";
    private const string T_UP = "上へ";
    private const string T_DOWN = "下へ";
    private const string T_DELETE = "削除";
    private const string T_IMAGE_FAILED = "画像を読み込めませんでした";
    private const string T_PICK = "お知らせに載せる画像を選んでください";
    private const string T_PREVIEW_TITLE = "この画像を追加しますか？";
    private const string T_PREVIEW_BODY = "お知らせのいちばん上に表示されます";
    private const string T_PREVIEW_OK = "追加する";
    private const string T_PREVIEW_CANCEL = "やめる";
    private const string T_UPLOADING = "アップロードしています…";
    private const string T_SAVING = "保存しています…";
    private const string T_DELETING = "削除しています…";
    private const string T_ADDED = "お知らせに画像を追加しました";
    private const string T_DELETED = "画像を削除しました";
    private const string T_DELETE_TITLE = "この画像を削除しますか？";
    private const string T_DELETE_BODY = "お知らせから外れます。元には戻せません";
    private const string T_DELETE_OK = "削除する";
    private const string T_CHANGED = "ほかの端末で変更されていたので、読み直しました";
    private const string T_SAVE_FAILED = "保存できませんでした\nもう一度お試しください";
    private const string T_NO_PERMISSION = "管理者の権限がないため保存できませんでした\n（ログインし直すと直ることがあります）";

    /// <summary>この画面で表示しうる文字</summary>
    public static string GlyphText
    {
        get
        {
            var sb = new StringBuilder();
            sb.Append(T_CAPTION).Append(T_TITLE).Append(T_NOTE).Append(T_ADD).Append(T_LOADING).Append(T_FAILED)
              .Append(T_RETRY).Append(T_EMPTY).Append(T_EMPTY_SUB).Append(T_ORDER).Append(T_TOP).Append(T_UP)
              .Append(T_DOWN).Append(T_DELETE).Append(T_IMAGE_FAILED).Append(T_PREVIEW_TITLE).Append(T_PREVIEW_BODY)
              .Append(T_PREVIEW_OK).Append(T_PREVIEW_CANCEL).Append(T_UPLOADING).Append(T_SAVING).Append(T_DELETING)
              .Append(T_ADDED).Append(T_DELETED).Append(T_DELETE_TITLE).Append(T_DELETE_BODY).Append(T_DELETE_OK)
              .Append(T_CHANGED).Append(T_SAVE_FAILED).Append(T_NO_PERMISSION).Append("0123456789キャンセル");
            return sb.ToString();
        }
    }

    private const float LIST_W = 960f, CARD_W = 920f;
    private const float ADD_TOP = 236f, ADD_H = 112f;
    private const float LIST_TOP = ADD_TOP + ADD_H + 28f, LIST_BOTTOM = 40f;
    private const float ROW_H = 340f, ROW_GAP = 20f, PAD_TOP = 8f, PAD_BOTTOM = 40f;
    private const float THUMB_BOX = 300f;
    private const float BTN_W = 170f, BTN_H = 96f, BTN_GAP = 12f;
    private static readonly Color PILL_FILL = new Color(0.07f, 0.09f, 0.24f, 0.85f);

    /// <summary>一覧の1枚ぶんの表示</summary>
    private sealed class CardView
    {
        public string Url;
        public RawImage Pic;
        public TextMeshProUGUI Label;
    }

    private Canvas _canvas;
    private Action<bool> _onClosed;
    private CanvasGroup _group;
    private RectTransform _safe, _body, _content;
    private GameObject _preview;
    private Texture2D _previewTex;
    private bool _closing, _busy, _changed;
    private float _scrollY;
    private int _loadSerial;
    private List<string> _shown;   // 開いたときにお知らせページが表示していた一覧
    private List<string> _urls;    // サーバーの一覧（並びもそのまま。最後＝お知らせのいちばん上）
    private readonly Dictionary<string, byte[]> _bytes = new Dictionary<string, byte[]>();     // 画像のデータ（キャッシュの書き直し用）
    private readonly Dictionary<string, Texture2D> _thumbs = new Dictionary<string, Texture2D>();
    private readonly HashSet<string> _failed = new HashSet<string>();
    private readonly List<CardView> _cards = new List<CardView>();

    /// <summary>
    /// 開く。parent はお知らせページ（その上に重ねる）。閉じたら onClosed(一覧が変わったか)。
    /// お知らせページごと消えたときは onClosed を呼ばない
    /// </summary>
    public static SpaceAdminNewsImages Open(Transform parent, Canvas canvas, Action<bool> onClosed)
    {
        var root = SpaceTheme.Rect("__AdminNewsImages", parent);
        SpaceTheme.Stretch(root);
        root.SetAsLastSibling();
        var editor = root.gameObject.AddComponent<SpaceAdminNewsImages>();
        editor._canvas = canvas;
        editor._onClosed = onClosed;
        editor._shown = new List<string>(MasterData.CachedConfig?.EventImages ?? new List<string>());
        editor.Build();
        return editor;
    }

    /// <summary>戻る操作: 追加の確認を開いていればそれを、なければこの画面を閉じる（保存中は閉じない）</summary>
    public void HandleBack()
    {
        if (_busy) return;
        if (_preview != null) CancelPreview();
        else Close();
    }

    public void Close()
    {
        if (_closing || _busy) return;
        _closing = true;
        StartCoroutine(FadeOutAndDestroy());
    }

    private IEnumerator FadeOutAndDestroy()
    {
        float from = _group.alpha;
        yield return SpaceTheme.Tween(0.2f, 0f, k => _group.alpha = from * (1f - k));
        Destroy(gameObject);
    }

    private void OnDestroy()
    {
        if (_previewTex != null) Destroy(_previewTex);
        foreach (var t in _thumbs.Values)
            if (t != null) Destroy(t);
        _thumbs.Clear();
        if (_closing) _onClosed?.Invoke(_changed || (_urls != null && !SameList(_urls, _shown)));
    }

    // ================================================================
    // 画面
    // ================================================================
    private void Build()
    {
        var rt = (RectTransform)transform;
        _group = gameObject.AddComponent<CanvasGroup>();
        _group.alpha = 0f;

        var dim = SpaceTheme.Rect("Dim", rt);
        SpaceTheme.Stretch(dim);
        SpaceTheme.Img(dim, null, new Color(0.01f, 0.015f, 0.05f, 0.97f), true);

        _safe = SpaceTheme.Rect("Safe", rt);
        SpaceTheme.Stretch(_safe);
        _safe.gameObject.AddComponent<SpaceSafeArea>();

        var caption = SpaceTheme.Text(_safe, "Caption", T_CAPTION, 24, SpaceTheme.A(SpaceTheme.GOLD, 0.9f),
            new Vector2(0.5f, 1f), new Vector2(0f, -70f), new Vector2(700f, 36f), FontStyles.Bold);
        caption.characterSpacing = 18f;
        SpaceTheme.Text(_safe, "Title", T_TITLE, 44, SpaceTheme.TEXT, new Vector2(0.5f, 1f), new Vector2(0f, -124f),
            new Vector2(800f, 60f), FontStyles.Bold);
        SpaceTheme.Text(_safe, "Note", T_NOTE, 24, SpaceTheme.TEXT_MUTED, new Vector2(0.5f, 1f), new Vector2(0f, -190f),
            new Vector2(980f, 36f), FontStyles.Bold);
        CloseButton(_safe);

        // 画像を追加（＋の絵は線で描く）
        var add = SpaceTheme.Rect("Add", _safe, new Vector2(0.5f, 1f), new Vector2(0f, -(ADD_TOP + ADD_H / 2f)),
            new Vector2(CARD_W, ADD_H));
        var addFace = SpaceTheme.PrimaryFace(add);
        SpaceTheme.Clickable(add.gameObject, addFace, PickImage);
        SpaceTheme.Bar(add, new Vector2(-118f, 0f), 34f, 6f, 0f, SpaceTheme.TEXT_DARK);
        SpaceTheme.Bar(add, new Vector2(-118f, 0f), 34f, 6f, 90f, SpaceTheme.TEXT_DARK);
        SpaceTheme.Text(add, "Label", T_ADD, 38, SpaceTheme.TEXT_DARK, SpaceTheme.CENTER, new Vector2(24f, 0f),
            new Vector2(300f, 60f), FontStyles.Bold);

        _body = SpaceTheme.Rect("Body", _safe);
        _body.anchorMin = new Vector2(0.5f, 0f);
        _body.anchorMax = new Vector2(0.5f, 1f);
        _body.pivot = SpaceTheme.CENTER;
        _body.offsetMin = new Vector2(-LIST_W * 0.5f, LIST_BOTTOM);
        _body.offsetMax = new Vector2(LIST_W * 0.5f, -LIST_TOP);

        StartCoroutine(SpaceTheme.Tween(0.25f, 0f, k => _group.alpha = k));
        LoadAsync().Forget();
    }

    private void CloseButton(RectTransform safe)
    {
        var rt = SpaceTheme.Rect("Close", safe, new Vector2(1f, 1f), new Vector2(-112f, -84f), new Vector2(104f, 104f));
        var face = SpaceTheme.Img(rt, SpaceSprites.Circle, new Color(0.05f, 0.07f, 0.19f, 0.85f), true);
        var rim = SpaceTheme.Rect("Rim", rt);
        SpaceTheme.Stretch(rim);
        SpaceTheme.Img(rim, SpaceSprites.RingSmall, SpaceTheme.STROKE);
        SpaceTheme.Bar(rt, Vector2.zero, 44f, 6f, 45f, SpaceTheme.TEXT);
        SpaceTheme.Bar(rt, Vector2.zero, 44f, 6f, -45f, SpaceTheme.TEXT);
        SpaceTheme.Clickable(rt.gameObject, face, HandleBack);
    }

    private void ClearBody()
    {
        if (_content != null) _scrollY = _content.anchoredPosition.y;
        foreach (Transform child in _body) Destroy(child.gameObject);
        _content = null;
        _cards.Clear();
    }

    /// <summary>読み込み中・空・失敗のときの枠</summary>
    private void Message(string text, string sub, bool retry)
    {
        float h = retry ? 300f : sub != null ? 220f : 160f;
        var box = SpaceTheme.Rect("Message", _body, new Vector2(0.5f, 1f), new Vector2(0f, -PAD_TOP),
            new Vector2(CARD_W, h), new Vector2(0.5f, 1f));
        SpaceTheme.Glass(box, 36f, new Color(0.07f, 0.09f, 0.24f, 0.55f));
        var t = SpaceTheme.Text(box, "Text", text, 30, SpaceTheme.TEXT_SUB, new Vector2(0.5f, 1f), new Vector2(0f, -70f),
            new Vector2(CARD_W - 60f, 46f), FontStyles.Bold);
        t.enableAutoSizing = true;
        t.fontSizeMin = 22f;
        t.fontSizeMax = 30f;
        if (sub != null)
        {
            var s = SpaceTheme.Text(box, "Sub", sub, 24, SpaceTheme.TEXT_MUTED, new Vector2(0.5f, 1f),
                new Vector2(0f, -126f), new Vector2(CARD_W - 60f, 36f), FontStyles.Bold);
            s.enableAutoSizing = true;
            s.fontSizeMin = 18f;
            s.fontSizeMax = 24f;
        }
        if (retry)
        {
            var b = SpaceTheme.Rect("Retry", box, new Vector2(0.5f, 0f), new Vector2(0f, 40f), new Vector2(460f, 104f),
                new Vector2(0.5f, 0f));
            var face = SpaceTheme.Glass(b, 46f, PILL_FILL, SpaceTheme.A(SpaceTheme.CYAN, 0.5f), true);
            SpaceTheme.Clickable(b.gameObject, face, () => LoadAsync().Forget());
            var label = SpaceTheme.Text(b, "Label", T_RETRY, 32, SpaceTheme.CYAN, FontStyles.Bold);
            SpaceTheme.Stretch(label.rectTransform);
        }
    }

    // ================================================================
    // 読み込み
    // ================================================================
    private static DocumentReference ConfigRef =>
        FirebaseFirestore.DefaultInstance.Collection("master").Document("config");

    private async UniTaskVoid LoadAsync()
    {
        if (_busy) return;
        int serial = ++_loadSerial;
        ClearBody();
        Message(T_LOADING, null, false);

        List<string> urls;
        try
        {
            // 編集はいつもサーバーの最新の一覧に対して行う（起動時に読んだ設定は古いことがある）
            var snap = await ConfigRef.GetSnapshotAsync(Source.Server).AsUniTask();
            urls = ReadList(snap);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[NewsAdmin] お知らせの画像の一覧を読めませんでした: {ex.Message}");
            if (this == null || serial != _loadSerial) return;
            ClearBody();
            Message(T_FAILED, null, true);
            return;
        }
        if (this == null || serial != _loadSerial) return;
        SetUrls(urls);
        BuildCards();
        LoadThumbsAsync(serial).Forget();
    }

    /// <summary>
    /// 一覧の画像を上から1枚ずつ読み込む。お知らせのキャッシュがこの一覧のものならそこから、なければダウンロード
    /// （ダウンロードした分はキャッシュに書かない。キャッシュを書き直すのは Store だけにして、並べ替えの途中で
    /// 別の画像が別の番号に入らないようにする）
    /// </summary>
    private async UniTaskVoid LoadThumbsAsync(int serial)
    {
        var urls = new List<string>(_urls);
        bool downloaded = false;
        for (int i = urls.Count - 1; i >= 0; i--)
        {
            string url = urls[i];
            if (string.IsNullOrEmpty(url) || _bytes.ContainsKey(url) || _failed.Contains(url)) continue;
            var bytes = SpaceNewsImages.ReadCached(urls, i);
            if (bytes == null)
            {
                bytes = await SpaceNewsImages.DownloadAsync(url);
                downloaded = true;
            }
            if (this == null || serial != _loadSerial) return;
            if (bytes != null && MakeThumb(url, bytes)) _bytes[url] = bytes;
            else _failed.Add(url);
            RefreshCards(url);
            await UniTask.Yield();
            if (this == null || serial != _loadSerial) return;
        }
        // ダウンロードした画像も含めてお知らせのキャッシュをそろえる（お知らせでダウンロードし直さないように）
        if (downloaded && SameList(urls, _urls)) SpaceNewsImages.Store(urls, BytesOf);
    }

    /// <summary>まだ読み込んでいない画像があれば、続きを読み込む（並びが変わったあと・保存に失敗したあと）</summary>
    private void ResumeThumbs()
    {
        if (_urls == null) return;
        if (_urls.Any(u => !string.IsNullOrEmpty(u) && !_bytes.ContainsKey(u) && !_failed.Contains(u)))
            LoadThumbsAsync(++_loadSerial).Forget();
    }

    private byte[] BytesOf(string url) => url != null && _bytes.TryGetValue(url, out var b) ? b : null;

    private bool MakeThumb(string url, byte[] bytes)
    {
        var full = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!full.LoadImage(bytes, true))
        {
            Destroy(full);
            return false;
        }
        var small = BoardGamePhotoStore.Downscale(full, THUMB_MAX);
        if (small != full) Destroy(full);
        small.wrapMode = TextureWrapMode.Clamp;
        if (_thumbs.TryGetValue(url, out var old) && old != null) Destroy(old);
        _thumbs[url] = small;
        return true;
    }

    // ================================================================
    // 一覧
    // ================================================================
    private void BuildCards(bool toTop = false)
    {
        ClearBody();
        if (toTop) _scrollY = 0f;
        int n = _urls.Count;
        if (n == 0)
        {
            Message(T_EMPTY, T_EMPTY_SUB, false);
            return;
        }

        var viewport = SpaceTheme.Rect("Viewport", _body);
        SpaceTheme.Stretch(viewport);
        SpaceTheme.HitArea(viewport); // カードの間の余白でもドラッグできるように
        viewport.gameObject.AddComponent<RectMask2D>().softness = new Vector2Int(0, 24);
        float contentH = PAD_TOP + n * ROW_H + (n - 1) * ROW_GAP + PAD_BOTTOM;
        _content = SpaceTheme.Rect("Content", viewport, new Vector2(0.5f, 1f), Vector2.zero,
            new Vector2(LIST_W, contentH), new Vector2(0.5f, 1f));
        var scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = _content;
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Elastic;
        scroll.scrollSensitivity = 40f;

        // この画面の上から＝お知らせの上から（一覧の最後から）
        for (int d = 0; d < n; d++)
            BuildCard(_content, -(PAD_TOP + d * (ROW_H + ROW_GAP)), d, n - 1 - d, n);

        // 並べ替えのあとも、見ていた位置のまま
        Canvas.ForceUpdateCanvases();
        float max = Mathf.Max(0f, contentH - viewport.rect.height);
        _content.anchoredPosition = new Vector2(0f, Mathf.Clamp(_scrollY, 0f, max));
    }

    /// <summary>1枚ぶん: 左に画像、右に「n枚目」と 上へ・下へ・削除。d は表示の順番（0＝いちばん上）、a は一覧の番号</summary>
    private void BuildCard(RectTransform content, float y, int d, int a, int n)
    {
        string url = _urls[a];
        var card = SpaceTheme.Rect("Card", content, new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(CARD_W, ROW_H),
            new Vector2(0.5f, 1f));
        SpaceTheme.Glass(card, 40f, SpaceTheme.GLASS, d == 0 ? SpaceTheme.A(SpaceNewsPage.ACCENT, 0.45f) : (Color?)null);

        // 画像（正方形の枠の中に、縦横の比を保って収める）
        var box = SpaceTheme.Rect("Thumb", card, new Vector2(0f, 0.5f), new Vector2(20f, 0f),
            new Vector2(THUMB_BOX, THUMB_BOX), new Vector2(0f, 0.5f));
        SpaceTheme.Rounded(box, new Color(0.03f, 0.04f, 0.12f, 0.9f), 28f);
        box.gameObject.AddComponent<Mask>().showMaskGraphic = true;
        var pic = SpaceTheme.Rect("Picture", box, SpaceTheme.CENTER, Vector2.zero, new Vector2(THUMB_BOX, THUMB_BOX));
        var raw = pic.gameObject.AddComponent<RawImage>();
        raw.raycastTarget = false;
        raw.enabled = false;
        var label = SpaceTheme.Text(box, "Label", T_LOADING, 24, SpaceTheme.TEXT_MUTED, FontStyles.Bold);
        SpaceTheme.Stretch(label.rectTransform, 16f, 0f, 16f, 0f);
        label.textWrappingMode = TextWrappingModes.Normal;

        float x0 = 20f + THUMB_BOX + 28f;
        SpaceTheme.Text(card, "Order", (d + 1) + T_ORDER, 38, SpaceTheme.TEXT, new Vector2(0f, 1f), new Vector2(x0, -62f),
            new Vector2(420f, 52f), FontStyles.Bold, TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
        if (d == 0)
            SpaceTheme.Text(card, "Top", T_TOP, 24, SpaceTheme.A(SpaceNewsPage.ACCENT, 0.95f), new Vector2(0f, 1f),
                new Vector2(x0, -112f), new Vector2(420f, 36f), FontStyles.Bold, TextAlignmentOptions.Left,
                new Vector2(0f, 0.5f));

        // 上へ＝お知らせで1つ上（一覧では後ろへ）、下へ＝1つ下（一覧では前へ）
        float by = 28f + BTN_H / 2f;
        CardButton(card, "Up", T_UP, x0 + BTN_W * 0.5f, by, d > 0, false, () => MoveAsync(a, a + 1).Forget());
        CardButton(card, "Down", T_DOWN, x0 + BTN_W * 1.5f + BTN_GAP, by, d < n - 1, false,
            () => MoveAsync(a, a - 1).Forget());
        CardButton(card, "Delete", T_DELETE, x0 + BTN_W * 2.5f + BTN_GAP * 2f, by, true, true, () => ConfirmDelete(a));

        var view = new CardView { Url = url, Pic = raw, Label = label };
        _cards.Add(view);
        ShowThumb(view);
    }

    private static void CardButton(RectTransform card, string name, string text, float x, float y, bool enabled,
        bool danger, UnityEngine.Events.UnityAction onClick)
    {
        var rt = SpaceTheme.Rect(name, card, Vector2.zero, new Vector2(x, y), new Vector2(BTN_W, BTN_H));
        var color = danger ? SpaceTheme.DANGER : SpaceTheme.TEXT_SUB;
        var face = SpaceTheme.Glass(rt, 40f, PILL_FILL, danger ? SpaceTheme.A(SpaceTheme.DANGER, 0.6f) : (Color?)null, true);
        var label = SpaceTheme.Text(rt, "Label", text, 32, color, FontStyles.Bold);
        SpaceTheme.Stretch(label.rectTransform);
        if (enabled) SpaceTheme.Clickable(rt.gameObject, face, onClick);
        else rt.gameObject.AddComponent<CanvasGroup>().alpha = 0.3f; // これ以上動かせない向き
    }

    private void RefreshCards(string url)
    {
        foreach (var c in _cards)
            if (c.Url == url) ShowThumb(c);
    }

    private void ShowThumb(CardView c)
    {
        if (c.Pic == null) return;
        if (c.Url != null && _thumbs.TryGetValue(c.Url, out var tex) && tex != null)
        {
            float k = Mathf.Min(THUMB_BOX / tex.width, THUMB_BOX / (float)tex.height);
            c.Pic.texture = tex;
            c.Pic.rectTransform.sizeDelta = new Vector2(tex.width * k, tex.height * k);
            c.Pic.enabled = true;
            c.Label.gameObject.SetActive(false);
        }
        else
        {
            c.Pic.enabled = false;
            c.Label.gameObject.SetActive(true);
            // URL が空の項目（手作業の登録の名残など）も、削除できるよう「読み込めませんでした」と出す
            c.Label.text = string.IsNullOrEmpty(c.Url) || _failed.Contains(c.Url) ? T_IMAGE_FAILED : T_LOADING;
        }
    }

    // ================================================================
    // 追加
    // ================================================================
    private void PickImage()
    {
        if (_busy || _preview != null || _urls == null) return;
#if UNITY_EDITOR
        // エディタではネイティブのアルバムが使えないので、ファイルを選んで代わりにする
        OnPicked(UnityEditor.EditorUtility.OpenFilePanel("お知らせの画像を選択", "", "png,jpg,jpeg"));
#else
        if (NativeGallery.IsMediaPickerBusy()) return;
        NativeGallery.GetImageFromGallery(OnPicked, T_PICK);
#endif
    }

    private void OnPicked(string path)
    {
        if (this == null || string.IsNullOrEmpty(path)) return; // 選ぶのをやめた
        Texture2D tex = null;
        try
        {
#if UNITY_EDITOR
            tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!tex.LoadImage(System.IO.File.ReadAllBytes(path)))
            {
                Destroy(tex);
                tex = null;
            }
#else
            // 大きな写真も、読み込むときに縦の上限まで縮める（メモリ対策）。JPG にするので readable のまま
            tex = NativeGallery.LoadImageAtPath(path, MAX_H, markTextureNonReadable: false, generateMipmaps: false);
#endif
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[NewsAdmin] 画像を読み込めませんでした: {ex.Message}");
        }
        if (tex == null)
        {
            SpaceTheme.Toast(T_IMAGE_FAILED, _canvas);
            return;
        }
        var fitted = Fit(tex, MAX_W, MAX_H);
        if (fitted != tex) Destroy(tex);
        ShowPreview(fitted);
    }

    /// <summary>
    /// 横 maxW・縦 maxH に収まるよう縮める（収まっていればそのまま返す）。
    /// 一度に大きく縮めると粗くなるので、半分ずつ縮めてから最後に合わせる
    /// </summary>
    private static Texture2D Fit(Texture2D src, int maxW, int maxH)
    {
        float k = Mathf.Min(1f, Mathf.Min((float)maxW / src.width, (float)maxH / src.height));
        if (k >= 1f) return src;
        int w = Mathf.Max(1, Mathf.RoundToInt(src.width * k));
        int h = Mathf.Max(1, Mathf.RoundToInt(src.height * k));

        var prev = RenderTexture.active;
        Texture current = src;
        RenderTexture temp = null;
        int cw = src.width, ch = src.height;
        while (cw / 2 >= w && ch / 2 >= h)
        {
            cw /= 2;
            ch /= 2;
            var half = RenderTexture.GetTemporary(cw, ch, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(current, half);
            if (temp != null) RenderTexture.ReleaseTemporary(temp);
            temp = half;
            current = half;
        }
        var target = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
        Graphics.Blit(current, target);
        if (temp != null) RenderTexture.ReleaseTemporary(temp);

        RenderTexture.active = target;
        var result = new Texture2D(w, h, TextureFormat.RGB24, false);
        result.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        result.Apply();
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(target);
        return result;
    }

    /// <summary>選んだ画像の確認（追加する／やめる）</summary>
    private void ShowPreview(Texture2D tex)
    {
        ClosePreview();
        _previewTex = tex;

        var root = SpaceTheme.Rect("Preview", transform);
        SpaceTheme.Stretch(root);
        _preview = root.gameObject;
        var dim = SpaceTheme.Rect("Dim", root);
        SpaceTheme.Stretch(dim);
        var dimImg = SpaceTheme.Img(dim, null, SpaceTheme.DIM, true);
        SpaceTheme.Clickable(dim.gameObject, dimImg, CancelPreview, false);

        const float maxW = 860f, maxH = 1040f;
        float k = Mathf.Min(maxW / tex.width, maxH / tex.height);
        float iw = tex.width * k, ih = tex.height * k;
        float h = 140f + ih + 96f + 200f;
        var card = SpaceTheme.Rect("Card", root, SpaceTheme.CENTER, Vector2.zero, new Vector2(960f, h));
        SpaceTheme.Glass(card, 56f, SpaceTheme.GLASS_DEEP, SpaceTheme.A(SpaceNewsPage.ACCENT, 0.4f), true);
        SpaceTheme.Text(card, "Title", T_PREVIEW_TITLE, 40, SpaceTheme.TEXT, new Vector2(0.5f, 1f), new Vector2(0f, -76f),
            new Vector2(880f, 56f), FontStyles.Bold);

        var frame = SpaceTheme.Rect("Frame", card, new Vector2(0.5f, 1f), new Vector2(0f, -(140f + ih / 2f)),
            new Vector2(iw, ih));
        SpaceTheme.Rounded(frame, new Color(0.03f, 0.04f, 0.12f, 1f), 24f);
        frame.gameObject.AddComponent<Mask>().showMaskGraphic = true;
        var pic = SpaceTheme.Rect("Picture", frame);
        SpaceTheme.Stretch(pic);
        var raw = pic.gameObject.AddComponent<RawImage>();
        raw.texture = tex;
        raw.raycastTarget = false;

        SpaceTheme.Text(card, "Body", T_PREVIEW_BODY, 28, SpaceTheme.TEXT_SUB, new Vector2(0.5f, 1f),
            new Vector2(0f, -(140f + ih + 48f)), new Vector2(880f, 44f), FontStyles.Bold);

        var cancel = SpaceTheme.Rect("Cancel", card, new Vector2(0.5f, 0f), new Vector2(-210f, 104f), new Vector2(380f, 128f));
        var cancelFace = SpaceTheme.Glass(cancel, 46f, SpaceTheme.A(new Color(0.09f, 0.12f, 0.30f), 0.63f), null, true);
        SpaceTheme.Clickable(cancel.gameObject, cancelFace, CancelPreview);
        var cancelText = SpaceTheme.Text(cancel, "Label", T_PREVIEW_CANCEL, 38, SpaceTheme.TEXT_SUB, FontStyles.Bold);
        SpaceTheme.Stretch(cancelText.rectTransform);

        var ok = SpaceTheme.Rect("Ok", card, new Vector2(0.5f, 0f), new Vector2(210f, 104f), new Vector2(380f, 128f));
        var okFace = SpaceTheme.PrimaryFace(ok);
        SpaceTheme.Clickable(ok.gameObject, okFace, ConfirmPreview);
        var okText = SpaceTheme.Text(ok, "Label", T_PREVIEW_OK, 38, SpaceTheme.TEXT_DARK, FontStyles.Bold);
        SpaceTheme.Stretch(okText.rectTransform);

        root.gameObject.AddComponent<SpacePopIn>().Target = card;
    }

    private void ClosePreview()
    {
        if (_preview != null) Destroy(_preview);
        _preview = null;
    }

    private void CancelPreview()
    {
        ClosePreview();
        if (_previewTex != null) Destroy(_previewTex);
        _previewTex = null;
    }

    private void ConfirmPreview()
    {
        var tex = _previewTex;
        _previewTex = null;
        ClosePreview();
        if (tex != null) AddAsync(tex).Forget();
    }

    /// <summary>JPG にして Storage の events/ に上げ、一覧の最後（お知らせのいちばん上）に足す</summary>
    private async UniTaskVoid AddAsync(Texture2D picked)
    {
        if (_busy)
        {
            Destroy(picked);
            return;
        }
        _busy = true;
        ++_loadSerial; // 読み込み中の画像は、保存のあとで読み直す
        var busy = SpaceDialog.Busy(_canvas, T_UPLOADING);
        StorageReference fileRef = null;
        bool uploaded = false;
        try
        {
            byte[] jpg = picked.EncodeToJPG(JPG_QUALITY);
            string path = $"{FOLDER}/{DateTime.Now:yyyyMMdd_HHmmss_fff}.jpg";
            string url = $"{BUCKET}/{path}"; // これまでの画像と同じ gs:// の形で一覧に入れる
            fileRef = FirebaseStorage.DefaultInstance.GetReferenceFromUrl(BUCKET).Child(path);
            await fileRef.PutBytesAsync(jpg, new MetadataChange { ContentType = "image/jpeg" }).AsUniTask();
            uploaded = true;
            // 足すのはサーバーの今の一覧の最後（ほかの端末の変更があっても、それを残したまま足す）
            var next = await UpdateListAsync(null, list =>
            {
                list.Add(url);
                return list;
            });
            if (this == null) return;
            _bytes[url] = jpg;
            MakeThumb(url, jpg);
            Apply(next, true); // 足した画像（いちばん上）が見えるように
            SpaceTheme.Toast(T_ADDED, _canvas);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[NewsAdmin] 画像を追加できませんでした: {ex}");
            // 一覧に入らなかった画像は Storage から消す
            if (uploaded && fileRef != null)
            {
                try
                {
                    await fileRef.DeleteAsync().AsUniTask();
                }
                catch (Exception e2)
                {
                    Debug.LogWarning($"[NewsAdmin] 上げた画像を消せませんでした: {e2.Message}");
                }
            }
            if (this != null)
            {
                SpaceTheme.Toast(IsPermissionDenied(ex) ? T_NO_PERMISSION : T_SAVE_FAILED, _canvas);
                ResumeThumbs();
            }
        }
        finally
        {
            if (picked != null) Destroy(picked);
            if (busy != null) Destroy(busy);
            _busy = false;
        }
    }

    // ================================================================
    // 並べ替え・削除
    // ================================================================
    private async UniTaskVoid MoveAsync(int a, int b)
    {
        if (_busy || _urls == null || a < 0 || b < 0 || a >= _urls.Count || b >= _urls.Count) return;
        await SaveAsync(T_SAVING, new List<string>(_urls), list =>
        {
            (list[a], list[b]) = (list[b], list[a]);
            return list;
        });
    }

    private void ConfirmDelete(int a)
    {
        if (_busy || _urls == null || a < 0 || a >= _urls.Count) return;
        string url = _urls[a];
        var expected = new List<string>(_urls);
        SpaceDialog.Confirm(_canvas, T_DELETE_TITLE, T_DELETE_BODY, T_DELETE_OK, true,
            () => DeleteAsync(expected, a, url).Forget());
    }

    private async UniTaskVoid DeleteAsync(List<string> expected, int a, string url)
    {
        var next = await SaveAsync(T_DELETING, expected, list =>
        {
            list.RemoveAt(a);
            return list;
        });
        if (next == null || this == null) return;
        SpaceTheme.Toast(T_DELETED, _canvas);
        // 画像ファイルも消す（events/ の中のものだけ。同じ画像がまだ一覧に残っていれば消さない）
        if (!next.Contains(url)) DeleteFileAsync(url).Forget();
    }

    /// <summary>
    /// サーバーの一覧が expected と同じときだけ op で書き換えて保存し、画面に反映する。
    /// 違っていたら（ほかの端末で変わっていたら）書かずに読み直す。成功したら新しい一覧、それ以外は null
    /// </summary>
    private async UniTask<List<string>> SaveAsync(string busyText, List<string> expected,
        Func<List<string>, List<string>> op)
    {
        if (_busy) return null;
        _busy = true;
        ++_loadSerial;
        var busy = SpaceDialog.Busy(_canvas, busyText);
        bool reload = false;
        try
        {
            var next = await UpdateListAsync(expected, op);
            if (this == null) return null;
            if (next == null)
            {
                SpaceTheme.Toast(T_CHANGED, _canvas);
                reload = true;
                return null;
            }
            Apply(next);
            return next;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[NewsAdmin] 保存できませんでした: {ex}");
            if (this != null)
            {
                SpaceTheme.Toast(IsPermissionDenied(ex) ? T_NO_PERMISSION : T_SAVE_FAILED, _canvas);
                ResumeThumbs();
            }
            return null;
        }
        finally
        {
            if (busy != null) Destroy(busy);
            _busy = false;
            if (reload && this != null) LoadAsync().Forget();
        }
    }

    /// <summary>
    /// master/config の event_images をトランザクションで書き換える。expected が null でなければ、
    /// サーバーの一覧がそれと同じときだけ書く（違えば何も書かずに null）。
    /// （コールバックは Firebase のスレッドで動くことがあるので、中では Unity の機能を使わない）
    /// </summary>
    private static async UniTask<List<string>> UpdateListAsync(List<string> expected,
        Func<List<string>, List<string>> op)
    {
        var db = FirebaseFirestore.DefaultInstance;
        var docRef = ConfigRef;
        return await db.RunTransactionAsync(tx => tx.GetSnapshotAsync(docRef).ContinueWith(t =>
        {
            var current = ReadList(t.Result);
            if (expected != null && !SameList(current, expected)) return null;
            var next = op(new List<string>(current));
            tx.Set(docRef, new Dictionary<string, object> { { FIELD, next } }, SetOptions.MergeAll);
            return next;
        })).AsUniTask();
    }

    /// <summary>保存できた一覧を画面・設定のキャッシュ・お知らせの画像のキャッシュに反映する</summary>
    private void Apply(List<string> next, bool toTop = false)
    {
        _changed = true;
        SetUrls(next);
        // 一覧から外れた画像は手放す
        foreach (var u in _bytes.Keys.Where(k => !next.Contains(k)).ToList()) _bytes.Remove(u);
        foreach (var u in _thumbs.Keys.Where(k => !next.Contains(k)).ToList())
        {
            if (_thumbs[u] != null) Destroy(_thumbs[u]);
            _thumbs.Remove(u);
        }
        _failed.RemoveWhere(u => !next.Contains(u));
        // お知らせを開き直したとき、ダウンロードし直さずにそのまま出せるように
        SpaceNewsImages.Store(next, BytesOf);
        BuildCards(toTop);
        ResumeThumbs();
    }

    private void SetUrls(List<string> urls)
    {
        _urls = urls;
        // この端末の設定のキャッシュも新しい一覧に（お知らせページ・ホームの「New」がこれを見る）
        if (MasterData.CachedConfig != null) MasterData.CachedConfig.EventImages = new List<string>(urls);
    }

    /// <summary>Storage の画像を消す（events/ の中のものだけ。失敗しても一覧からは外れている）</summary>
    private static async UniTaskVoid DeleteFileAsync(string url)
    {
        string path = StoragePathOf(url);
        if (path == null || !path.StartsWith(FOLDER + "/")) return;
        try
        {
            await FirebaseStorage.DefaultInstance.GetReferenceFromUrl(BUCKET).Child(path).DeleteAsync().AsUniTask();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[NewsAdmin] Storage の画像を消せませんでした（一覧からは外れています）: {ex.Message}");
        }
    }

    /// <summary>gs://～・https のダウンロードURL・パスのみ のどれでも、このバケットの中のパスにする（別のバケットなら null）</summary>
    private static string StoragePathOf(string url)
    {
        if (string.IsNullOrEmpty(url)) return null;
        if (url.StartsWith(BUCKET + "/")) return url.Substring(BUCKET.Length + 1);
        if (url.StartsWith("gs://")) return null;
        if (url.StartsWith("https://") || url.StartsWith("http://"))
        {
            const string marker = "/b/intales-a0459.firebasestorage.app/o/";
            int i = url.IndexOf(marker, StringComparison.Ordinal);
            if (i < 0) return null;
            string rest = url.Substring(i + marker.Length);
            int q = rest.IndexOf('?');
            return Uri.UnescapeDataString(q >= 0 ? rest.Substring(0, q) : rest);
        }
        return url.TrimStart('/');
    }

    private static List<string> ReadList(DocumentSnapshot snap)
    {
        var list = new List<string>();
        if (snap == null || !snap.Exists) return list;
        var d = snap.ToDictionary();
        if (d.TryGetValue(FIELD, out var v) && v is IEnumerable<object> items)
            foreach (var o in items) list.Add(o as string ?? "");
        return list;
    }

    private static bool SameList(List<string> a, List<string> b)
    {
        if (a == null || b == null) return a == b;
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
            if (a[i] != b[i]) return false;
        return true;
    }

    private static bool IsPermissionDenied(Exception ex)
    {
        for (var e = ex; e != null; e = e.InnerException)
        {
            if (e is FirestoreException fe && fe.ErrorCode == FirestoreError.PermissionDenied) return true;
            if (e is StorageException se && (se.ErrorCode == StorageException.ErrorNotAuthorized ||
                                             se.ErrorCode == StorageException.ErrorNotAuthenticated)) return true;
            if (e is AggregateException ae)
                foreach (var inner in ae.InnerExceptions)
                    if (IsPermissionDenied(inner)) return true;
        }
        return false;
    }
}
