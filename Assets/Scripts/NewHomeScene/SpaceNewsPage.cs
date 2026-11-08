using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Cysharp.Threading.Tasks;
using Firebase.Firestore;
using Firebase.Storage;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Networking;
using UnityEngine.UI;

/// <summary>
/// お知らせページ（新ホームの中央の惑星の左上にある、ピンクの小さな惑星から開く全画面）。
///  ・お店からのお知らせ … 店側が Firestore の master/config.event_images に登録した画像（旧ホームのお知らせページと同じ。
///                         並びも旧ホームと同じく、一覧の最後＝あとから足したものが一番上）。
///                         タップで拡大表示（ピンチ・ダブルタップで拡大、拡大中はドラッグで移動）。
///                         管理者には見出しの右に「画像を編集」（SpaceAdminNewsImages。追加・並べ替え・削除）
///  ・相席募集 … recruits の期限内の募集の概要（日時・ゲーム・ひとこと・集まる人数）。見るだけで、参加・投稿はできない
///              （旧ホームの募集ボード RecruitBoardController と同じ基準で、通報付き・時間を過ぎたものは出さない）
/// 閉じるのは右上のボタンか戻る操作。
/// </summary>
public class SpaceNewsPage : MonoBehaviour
{
    /// <summary>このページの色（ホームのピンクの小さな惑星の光・見出し）</summary>
    public static readonly Color ACCENT = SpaceTheme.Hex(0xFF8AD8);

    private const string T_CAPTION = "NEWS";
    private const string T_TITLE = "お知らせ";
    private const string T_SEC_SHOP = "お店からのお知らせ";
    private const string T_SEC_RECRUIT = "相席募集";
    private const string T_LOADING = "読み込み中…";
    private const string T_IMAGE_FAILED = "画像を読み込めませんでした";
    private const string T_NO_IMAGES = "いまお知らせはありません";
    private const string T_NO_RECRUITS = "いまは相席募集がありません";
    private const string T_NO_RECRUITS_SUB = "お店でいっしょに遊ぶ人の募集がここに表示されます";
    private const string T_RECRUIT_FAILED = "募集を読み込めませんでした";
    private const string T_OPEN_A = "募集中 ";
    private const string T_OPEN_B = "件";
    private const string T_LEFT_A = "あと";
    private const string T_LEFT_B = "人";
    private const string T_FULL = "満員";
    private const string T_CLOSED = "締め切り";
    private const string T_HOST_B = "さんの募集";
    private const string T_SAN = "さん";
    // フレンドでない参加者の呼び名（名前は出さない）。カードごとに、フレンドでない人の並び順で「ユーザーA」「ユーザーB」…
    private const string T_USER = "ユーザー";
    private const string USER_LETTERS = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string T_NAMES_NOTE = "募集した人・参加した人の名前は、フレンドにだけ表示されます";
    private const string T_ZOOM_HINT = "ピンチやダブルタップで拡大できます";
    private const string T_EDIT = "画像を編集"; // 管理者だけ
    private const string T_POST = "相席募集をする";
    private const string T_MINE = "あなたの募集";
    private const string T_CANCEL_POST = "取り消す";
    private const string T_CANCEL_TITLE = "募集を取り消しますか？";
    private const string T_CANCEL_BODY = "取り消した募集は元に戻せません";
    private const string T_CANCELING = "取り消しています…";
    private const string T_CANCELED = "募集を取り消しました";
    private const string T_ONE_ONLY = "募集は1人1件までです\n（今の募集を取り消せば作れます）";
    private const string T_NO_NAME = "ユーザー名が登録されていないため、募集できません";
    private const string T_FAILED = "通信に失敗しました\nもう一度お試しください";
    private const string T_JOIN = "参加する";
    private const string T_LEAVE = "参加をやめる";
    private const string T_JOINED = "参加中";
    private const string T_JOIN_TITLE = "ひとことえらんで参加";
    private const string T_JOIN_CANCEL = "やめる";
    private const string T_JOINING = "参加を伝えています…";
    private const string T_LEAVING = "参加を取り消しています…";
    private const string T_JOIN_DONE = "参加を伝えました！\n当日お店で声をかけてね";
    private const string T_LEAVE_DONE = "参加を取り消しました";
    private const string T_NO_NAME_JOIN = "ユーザー名が登録されていないため、参加できません";
    private const string T_CLOSE_POST = "締め切る";
    private const string T_CLOSE_TITLE = "募集を締め切りますか？";
    private const string T_CLOSE_BODY = "締め切ると、ほかの人は参加できなくなります";
    private const string T_CLOSING_POST = "締め切っています…";
    private const string T_CLOSED_POST = "募集を締め切りました";
    private const string T_REOPEN = "再開する";
    private const string T_REOPENING = "再開しています…";
    private const string T_REOPENED = "募集を再開しました";
    private const string T_ONE_OPEN = "受付中の募集は1人1件までです\n（ほかの募集を締め切るか取り消すと再開できます）";
    // 募集の日時（"今日 19:00～" "7/15(水) 12:30～"）とゲーム名の定型（RecruitBoardController.GameDisplay）
    private const string GLYPH_EXTRA = "0123456789:/()～今日明日日月火水木金土『』・"
                                       + "ゲームはお店できめる持ち込みゲームであそぶ（取り扱い外のゲーム）（持ち込み）";

    /// <summary>この画面で表示しうる固定の文字（名前・ゲーム名は読み込んだときに登録する）</summary>
    public static string GlyphText
    {
        get
        {
            var sb = new StringBuilder();
            sb.Append(T_CAPTION).Append(T_TITLE).Append(T_SEC_SHOP).Append(T_SEC_RECRUIT).Append(T_LOADING)
              .Append(T_IMAGE_FAILED).Append(T_NO_IMAGES).Append(T_NO_RECRUITS).Append(T_NO_RECRUITS_SUB)
              .Append(T_RECRUIT_FAILED).Append(T_OPEN_A).Append(T_OPEN_B).Append(T_LEFT_A).Append(T_LEFT_B)
              .Append(T_FULL).Append(T_CLOSED).Append(T_HOST_B).Append(T_SAN).Append(T_USER).Append(USER_LETTERS).Append(T_NAMES_NOTE).Append(T_ZOOM_HINT).Append(T_POST).Append(T_MINE)
              .Append(T_CANCEL_POST).Append(T_CANCEL_TITLE).Append(T_CANCEL_BODY).Append(T_CANCELING)
              .Append(T_CANCELED).Append(T_ONE_ONLY).Append(T_NO_NAME).Append(T_FAILED).Append("キャンセル")
              .Append(SpaceBackground.T_NEW).Append(T_JOIN).Append(T_LEAVE).Append(T_JOINED).Append(T_JOIN_TITLE)
              .Append(T_JOIN_CANCEL).Append(T_JOINING).Append(T_LEAVING).Append(T_JOIN_DONE).Append(T_LEAVE_DONE)
              .Append(T_NO_NAME_JOIN).Append(T_CLOSE_POST).Append(T_CLOSE_TITLE).Append(T_CLOSE_BODY)
              .Append(T_CLOSING_POST).Append(T_CLOSED_POST).Append(T_REOPEN).Append(T_REOPENING).Append(T_REOPENED)
              .Append(T_ONE_OPEN).Append(T_EDIT).Append("　").Append(GLYPH_EXTRA)
              .Append(SpaceRecruitForm.GlyphText);
            foreach (var m in RecruitBoardController.JOIN_MSGS) sb.Append(m);
            foreach (var t in RecruitBoardController.TAGS) sb.Append(t);
            return sb.ToString();
        }
    }

    private const float LIST_TOP = 196f;     // 見出しの下から一覧
    private const float POST_H = 120f;       // 一番下の「相席募集をする」
    private const float POST_BOTTOM = 40f;
    private const float LIST_BOTTOM = POST_BOTTOM + POST_H + 20f; // 一覧はボタンの上まで
    private const float VIEW_W = 1000f;      // 一覧の枠の幅（カードの光が左右に少しはみ出せるように）
    private const float CARD_W = 920f;
    private const float PAD_TOP = 8f, PAD_BOTTOM = 72f;
    private const float SECTION_H = 72f;     // 見出し
    private const float SECTION_GAP = 48f;   // 前の項目と見出しの間
    private const float FIRST_GAP = 8f;      // 見出しと最初の項目の間
    private const float GAP = 24f;           // 項目の間
    private const float IMAGE_RADIUS = 32f;
    private const float IMAGE_LOADING_H = 360f;
    private const float MESSAGE_H = 150f, MESSAGE_SUB_H = 196f;
    private const float MEMBER_ROW_H = 40f;  // 募集のカードの、参加した人の1行

    /// <summary>一覧に縦に並べる1項目（見出し・画像・募集・案内）</summary>
    private sealed class Block
    {
        public RectTransform Rt;
        public float H, Gap;
    }

    private sealed class ImageCard
    {
        public Block Block;
        public RawImage Pic;
        public TextMeshProUGUI Label;
        public Texture2D Tex;
    }

    private Canvas _canvas;
    private Action _onClosed;
    private CanvasGroup _group;
    private RectTransform _safe, _content, _viewer;
    private TextMeshProUGUI _recruitInfo;
    private Block _shopHeader, _recruitHeader;
    private readonly List<Block> _shopBlocks = new List<Block>();
    private readonly List<Block> _recruitBlocks = new List<Block>();
    private readonly List<Texture2D> _textures = new List<Texture2D>();
    private List<SpaceRecruitDigest.Item> _recruits; // 最後に表示した募集（1人1件の確認用。読み込み前・失敗時は null）
    private SpaceRecruitForm _form;
    private SpaceAdminNewsImages _imageEditor; // 管理者のお知らせの画像の編集（開いている間）
    private int _imagesSerial;      // 画像の読み込みの回数（読み直したら古い読み込みを止める）
    private bool _imagesComplete;   // 画像を最後まで読み込んだか
    private RectTransform _joinSheet;   // 参加のひとことを選ぶシート
    private Coroutine _openAnim, _recruitBuild;
    private bool _closing, _writing;

    public static SpaceNewsPage Open(Canvas canvas, Action onClosed)
    {
        var root = SpaceTheme.Rect("__SpaceNews", canvas.transform);
        SpaceTheme.Stretch(root);
        root.SetAsLastSibling();
        SpaceTheme.SubCanvas(root.gameObject, true); // 一覧をスクロールしてもホーム全体を描き直さないように
        var page = root.gameObject.AddComponent<SpaceNewsPage>();
        page._canvas = canvas;
        page._onClosed = onClosed;
        page.Build();
        return page;
    }

    /// <summary>戻る操作: 参加のひとこと・募集の作成・拡大表示を開いていればそれを、なければお知らせを閉じる</summary>
    public void HandleBack()
    {
        if (_imageEditor != null) _imageEditor.HandleBack();
        else if (_joinSheet != null) CloseJoinSheet();
        else if (_form != null) _form.HandleBack();
        else if (_viewer != null) CloseViewer();
        else Close();
    }

    public void Close()
    {
        if (_closing) return;
        _closing = true;
        if (_openAnim != null) StopCoroutine(_openAnim);
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
        SpaceRecruitDigest.Changed -= OnRecruitsChanged;
        // 画像は開くたびにキャッシュから読み直すので、閉じたら手放す（実機メモリ対策）
        foreach (var t in _textures)
            if (t != null) Destroy(t);
        _textures.Clear();
        _onClosed?.Invoke();
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
        // ガラスのカードの後ろにホームが透けないよう、ほぼ不透明の暗幕
        SpaceTheme.Img(dim, null, new Color(0.01f, 0.015f, 0.05f, 0.97f), true);

        _safe = SpaceTheme.Rect("Safe", rt);
        SpaceTheme.Stretch(_safe);
        _safe.gameObject.AddComponent<SpaceSafeArea>();

        var caption = SpaceTheme.Text(_safe, "Caption", T_CAPTION, 24, SpaceTheme.A(ACCENT, 0.9f),
            new Vector2(0.5f, 1f), new Vector2(0f, -70f), new Vector2(700f, 36f), FontStyles.Bold);
        SpaceTheme.Tracking(caption, 18f);
        SpaceTheme.Text(_safe, "Title", T_TITLE, 44, SpaceTheme.TEXT, new Vector2(0.5f, 1f), new Vector2(0f, -124f),
            new Vector2(800f, 60f), FontStyles.Bold);
        CloseButton(_safe, Close);

        // 一覧（縦スクロール。一番下の「相席募集をする」の上まで）
        var viewport = SpaceTheme.Rect("List", _safe);
        viewport.anchorMin = new Vector2(0.5f, 0f);
        viewport.anchorMax = new Vector2(0.5f, 1f);
        viewport.pivot = SpaceTheme.CENTER;
        viewport.offsetMin = new Vector2(-VIEW_W * 0.5f, LIST_BOTTOM);
        viewport.offsetMax = new Vector2(VIEW_W * 0.5f, -LIST_TOP);
        SpaceTheme.HitArea(viewport); // 項目の間の余白でもドラッグできるように
        viewport.gameObject.AddComponent<RectMask2D>().softness = new Vector2Int(0, 28);
        _content = SpaceTheme.Rect("Content", viewport, new Vector2(0.5f, 1f), Vector2.zero,
            new Vector2(VIEW_W, 100f), new Vector2(0.5f, 1f));
        var scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = _content;
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Elastic;
        scroll.scrollSensitivity = 40f;

        _shopHeader = SectionHeader(T_SEC_SHOP, ACCENT, out _);
        if (AdminClaim.IsAdmin) EditButton(_shopHeader); // 管理者だけ（実際の保護はルール）
        _shopBlocks.Add(MessageBlock(T_LOADING, null));
        _recruitHeader = SectionHeader(T_SEC_RECRUIT, SpaceTheme.MINT, out _recruitInfo);
        _recruitBlocks.Add(MessageBlock(T_LOADING, null));
        Relayout();

        // 一番下: 相席募集をする（スクロールしても常に見える位置）
        var post = SpaceTheme.Rect("PostRecruit", _safe, new Vector2(0.5f, 0f), new Vector2(0f, POST_BOTTOM),
            new Vector2(CARD_W, POST_H), new Vector2(0.5f, 0f));
        var postFace = SpaceTheme.PrimaryFace(post);
        SpaceTheme.Clickable(post.gameObject, postFace, OpenRecruitForm);
        var postLabel = SpaceTheme.Text(post, "Label", T_POST, 40, SpaceTheme.TEXT_DARK, FontStyles.Bold);
        SpaceTheme.Stretch(postLabel.rectTransform);

        _openAnim = StartCoroutine(SpaceTheme.Tween(0.25f, 0f, k => _group.alpha = k));
        LoadImagesAsync().Forget();
        LoadRecruitsAsync().Forget();
        SpaceRecruitDigest.Changed += OnRecruitsChanged;
    }

    /// <summary>見出し・画像・募集を上から順に並べ直す（画像が届いて高さが変わるたびに呼ぶ）</summary>
    private void Relayout()
    {
        float y = -PAD_TOP;
        bool first = true;

        void Place(Block b)
        {
            if (!first) y -= b.Gap;
            first = false;
            b.Rt.anchoredPosition = new Vector2(0f, y);
            b.Rt.sizeDelta = new Vector2(CARD_W, b.H);
            y -= b.H;
        }

        Place(_shopHeader);
        foreach (var b in _shopBlocks) Place(b);
        Place(_recruitHeader);
        foreach (var b in _recruitBlocks) Place(b);
        _content.sizeDelta = new Vector2(VIEW_W, -y + PAD_BOTTOM);
    }

    private Block NewBlock(string name, float h, float gap)
    {
        var rt = SpaceTheme.Rect(name, _content, new Vector2(0.5f, 1f), Vector2.zero, new Vector2(CARD_W, h),
            new Vector2(0.5f, 1f));
        return new Block { Rt = rt, H = h, Gap = gap };
    }

    /// <summary>見出しの直後は詰め、項目どうしは少しあける</summary>
    private static void FixGaps(List<Block> blocks)
    {
        for (int i = 0; i < blocks.Count; i++) blocks[i].Gap = i == 0 ? FIRST_GAP : GAP;
    }

    private static void ClearBlocks(List<Block> blocks)
    {
        foreach (var b in blocks)
            if (b.Rt != null) Destroy(b.Rt.gameObject);
        blocks.Clear();
    }

    /// <summary>見出し（光る点＋名前。右端に補足）</summary>
    private Block SectionHeader(string title, Color dot, out TextMeshProUGUI info)
    {
        var b = NewBlock("Section", SECTION_H, SECTION_GAP);
        var glow = SpaceTheme.Rect("DotGlow", b.Rt, new Vector2(0f, 0.5f), new Vector2(16f, 0f), new Vector2(52f, 52f));
        SpaceTheme.Img(glow, SpaceSprites.Glow, SpaceTheme.A(dot, 0.75f));
        var d = SpaceTheme.Rect("Dot", b.Rt, new Vector2(0f, 0.5f), new Vector2(16f, 0f), new Vector2(16f, 16f));
        SpaceTheme.Img(d, SpaceSprites.Circle, dot);
        SpaceTheme.Text(b.Rt, "Title", title, 34, SpaceTheme.TEXT, new Vector2(0f, 0.5f), new Vector2(44f, 0f),
            new Vector2(600f, 50f), FontStyles.Bold, TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
        info = SpaceTheme.Text(b.Rt, "Info", "", 26, SpaceTheme.TEXT_SUB, new Vector2(1f, 0.5f), new Vector2(-8f, 0f),
            new Vector2(320f, 40f), FontStyles.Bold, TextAlignmentOptions.Right, new Vector2(1f, 0.5f));
        return b;
    }

    /// <summary>管理者用: 「お店からのお知らせ」の見出しの右の「画像を編集」</summary>
    private void EditButton(Block header)
    {
        var btn = SpaceTheme.Rect("EditImages", header.Rt, new Vector2(1f, 0.5f), new Vector2(-4f, 0f),
            new Vector2(220f, 66f), new Vector2(1f, 0.5f));
        var face = SpaceTheme.Glass(btn, 33f, new Color(0.07f, 0.09f, 0.24f, 0.85f), SpaceTheme.A(ACCENT, 0.6f), true);
        SpaceTheme.Clickable(btn.gameObject, face, OpenImageEditor);
        var label = SpaceTheme.Text(btn, "Label", T_EDIT, 28, ACCENT, FontStyles.Bold);
        SpaceTheme.Stretch(label.rectTransform);
    }

    private void OpenImageEditor()
    {
        if (_imageEditor != null || _closing) return;
        // 表示中の画像の読み込みを止める（編集で並びが変わると、読み込み中の画像が別の場所に入るため）
        _imagesSerial++;
        _imageEditor = SpaceAdminNewsImages.Open(transform, _canvas, changed =>
        {
            _imageEditor = null;
            if (this == null || _closing) return;
            if (changed || !_imagesComplete) ReloadImages();
        });
    }

    /// <summary>お店からのお知らせの欄だけ読み直す（管理者が画像を編集したあと）</summary>
    private void ReloadImages()
    {
        foreach (var t in _textures)
            if (t != null) Destroy(t);
        _textures.Clear();
        ClearBlocks(_shopBlocks);
        _shopBlocks.Add(MessageBlock(T_LOADING, null));
        FixGaps(_shopBlocks);
        Relayout();
        LoadImagesAsync().Forget();
    }

    /// <summary>見出しのすぐ下の小さな注意書き（枠なし）</summary>
    private Block NoteBlock(string text)
    {
        var b = NewBlock("Note", 40f, FIRST_GAP);
        var t = SpaceTheme.Text(b.Rt, "Text", text, 24, SpaceTheme.TEXT_MUTED, new Vector2(0f, 0.5f), new Vector2(12f, 0f),
            new Vector2(CARD_W - 24f, 36f), FontStyles.Bold, TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
        t.enableAutoSizing = true;
        t.fontSizeMin = 18f;
        t.fontSizeMax = 24f;
        return b;
    }

    /// <summary>読み込み中・空・失敗のときの枠</summary>
    private Block MessageBlock(string text, string sub)
    {
        var b = NewBlock("Message", sub != null ? MESSAGE_SUB_H : MESSAGE_H, FIRST_GAP);
        SpaceTheme.Glass(b.Rt, 36f, new Color(0.07f, 0.09f, 0.24f, 0.55f));
        SpaceTheme.Text(b.Rt, "Text", text, 30, SpaceTheme.TEXT_SUB, SpaceTheme.CENTER,
            new Vector2(0f, sub != null ? 22f : 0f), new Vector2(CARD_W - 60f, 46f), FontStyles.Bold);
        if (sub != null)
        {
            var s = SpaceTheme.Text(b.Rt, "Sub", sub, 24, SpaceTheme.TEXT_MUTED, SpaceTheme.CENTER, new Vector2(0f, -30f),
                new Vector2(CARD_W - 60f, 36f), FontStyles.Bold);
            s.enableAutoSizing = true;
            s.fontSizeMin = 18f;
            s.fontSizeMax = 24f;
        }
        return b;
    }

    // ================================================================
    // お店からのお知らせ（画像）
    // ================================================================
    private async UniTaskVoid LoadImagesAsync()
    {
        int serial = ++_imagesSerial;
        _imagesComplete = false;
        var urls = await SpaceNewsImages.GetUrlsAsync();
        if (this == null || serial != _imagesSerial) return;
        SpaceNewsBadge.MarkImagesSeen(urls); // 開いて見たので「New」の対象から外す

        // 旧ホームと同じく、一覧の最後（あとから足したもの）を一番上に
        var order = new List<int>();
        for (int i = urls.Count - 1; i >= 0; i--)
            if (!string.IsNullOrEmpty(urls[i])) order.Add(i);

        ClearBlocks(_shopBlocks);
        if (order.Count == 0)
        {
            _shopBlocks.Add(MessageBlock(T_NO_IMAGES, null));
            Relayout();
            _imagesComplete = true;
            return;
        }

        SpaceNewsImages.Prepare(urls);
        var cards = new List<ImageCard>();
        foreach (int i in order)
        {
            var card = BuildImageCard();
            cards.Add(card);
            _shopBlocks.Add(card.Block);
        }
        FixGaps(_shopBlocks);
        Relayout();

        // 上から1枚ずつ（大きな画像の展開が続いて画面が止まらないように）
        bool allOk = true;
        for (int k = 0; k < order.Count; k++)
        {
            var bytes = await SpaceNewsImages.LoadAsync(urls[order[k]], order[k]);
            if (this == null || serial != _imagesSerial) return;
            if (bytes == null || !ShowImage(cards[k], bytes))
            {
                allOk = false;
                cards[k].Label.text = T_IMAGE_FAILED;
            }
            await UniTask.Yield();
            if (this == null || serial != _imagesSerial) return;
        }
        _imagesComplete = true;
        if (allOk) SpaceNewsImages.MarkComplete(urls);
    }

    /// <summary>画像1枚の枠（角丸に切り抜き、縁取りを重ねる）。届くまでは「読み込み中…」</summary>
    private ImageCard BuildImageCard()
    {
        var b = NewBlock("Image", IMAGE_LOADING_H, GAP);
        var frame = SpaceTheme.Rect("Frame", b.Rt);
        SpaceTheme.Stretch(frame);
        var face = SpaceTheme.Rounded(frame, new Color(0.06f, 0.08f, 0.21f, 0.85f), IMAGE_RADIUS, true);
        frame.gameObject.AddComponent<Mask>().showMaskGraphic = true;
        var pic = SpaceTheme.Rect("Picture", frame);
        SpaceTheme.Stretch(pic);
        var raw = pic.gameObject.AddComponent<RawImage>();
        raw.raycastTarget = false;
        raw.enabled = false;
        var edge = SpaceTheme.Rect("Edge", b.Rt);
        SpaceTheme.Stretch(edge);
        SpaceTheme.RoundedStroke(edge, SpaceTheme.STROKE, IMAGE_RADIUS);
        var label = SpaceTheme.Text(b.Rt, "Label", T_LOADING, 28, SpaceTheme.TEXT_MUTED, FontStyles.Bold);
        SpaceTheme.Stretch(label.rectTransform);

        var card = new ImageCard { Block = b, Pic = raw, Label = label };
        SpaceTheme.Clickable(b.Rt.gameObject, face, () =>
        {
            if (card.Tex != null) OpenViewer(card.Tex);
        });
        return card;
    }

    private bool ShowImage(ImageCard card, byte[] bytes)
    {
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!tex.LoadImage(bytes, true))
        {
            Destroy(tex);
            return false;
        }
        tex.wrapMode = TextureWrapMode.Clamp;
        _textures.Add(tex);
        card.Tex = tex;
        card.Pic.texture = tex;
        card.Pic.enabled = true;
        card.Pic.color = new Color(1f, 1f, 1f, 0f);
        card.Label.gameObject.SetActive(false);
        card.Block.H = Mathf.Max(120f, CARD_W * tex.height / Mathf.Max(1f, tex.width));
        Relayout();
        var pic = card.Pic;
        StartCoroutine(SpaceTheme.Tween(0.3f, 0f, k => pic.color = new Color(1f, 1f, 1f, k)));
        return true;
    }

    // ================================================================
    // 相席募集（概要）
    // ================================================================
    private async UniTaskVoid LoadRecruitsAsync()
    {
        var items = await SpaceRecruitDigest.LoadAsync();
        if (this == null) return;
        if (_recruitBuild != null) StopCoroutine(_recruitBuild);
        _recruitBuild = StartCoroutine(ShowRecruits(items));
    }

    /// <summary>募集を出した・取り消したあとに、相席募集の欄だけ読み直す</summary>
    private void ReloadRecruits()
    {
        if (this == null || _closing) return;
        SpaceRecruitDigest.Invalidate();
        _recruits = null;
        ClearBlocks(_recruitBlocks);
        _recruitBlocks.Add(MessageBlock(T_LOADING, null));
        FixGaps(_recruitBlocks);
        _recruitInfo.text = "";
        Relayout();
        LoadRecruitsAsync().Forget();
    }

    private IEnumerator ShowRecruits(List<SpaceRecruitDigest.Item> items)
    {
        // 名前・ゲーム名は事前に分からないので、40文字/フレームでフォントへ登録してから並べる（TMP動的フォントのOOM対策）
        if (items != null && items.Count > 0)
        {
            var sb = new StringBuilder();
            foreach (var it in items)
            {
                sb.Append(it.When).Append(it.Game).Append(it.Tags).Append(it.Host);
                foreach (var m in it.Members) sb.Append(m.Name);
            }
            yield return SpaceTheme.Prewarm(sb.ToString());
        }
        if (_closing) yield break;

        _recruits = items;
        ClearBlocks(_recruitBlocks);
        int open = 0;
        if (items == null) _recruitBlocks.Add(MessageBlock(T_RECRUIT_FAILED, null));
        else if (items.Count == 0) _recruitBlocks.Add(MessageBlock(T_NO_RECRUITS, T_NO_RECRUITS_SUB));
        else
        {
            _recruitBlocks.Add(NoteBlock(T_NAMES_NOTE)); // 名前が出ない募集がある理由
            foreach (var it in items)
            {
                _recruitBlocks.Add(BuildRecruitCard(it));
                if (it.Status == SpaceRecruitDigest.State.Open) open++;
            }
        }
        FixGaps(_recruitBlocks);
        _recruitInfo.text = items != null && items.Count > 0 ? T_OPEN_A + open + T_OPEN_B : "";
        Relayout();
        SpaceNewsBadge.MarkRecruitsSeen(items); // 表示した募集は「New」の対象から外す
    }

    /// <summary>開いている間に募集が増えた・減った・変わったら、相席募集の欄を作り直す（リアルタイム）</summary>
    private void OnRecruitsChanged()
    {
        if (this == null || _closing) return;
        LoadRecruitsAsync().Forget();
    }

    /// <summary>
    /// 募集1件: いつ・空き / ゲーム / ひとこと / 集まる人数（●＝主催者と参加者、うすい●＝空き）と募集した人。
    /// 自分の募集は名前の代わりに「あなたの募集」と、締め切る（再開する）・取り消すボタン
    /// </summary>
    private Block BuildRecruitCard(SpaceRecruitDigest.Item it)
    {
        bool hasTags = !string.IsNullOrEmpty(it.Tags);
        int memberCount = it.Members.Count;
        // 参加した人の行（1人1行）はひとことの下。人数の点とボタンの行はそのさらに下
        float membersTop = hasTags ? -182f : -138f;
        float seatsY = memberCount > 0
            ? membersTop - memberCount * MEMBER_ROW_H - 36f
            : hasTags ? -206f : -162f;
        var b = NewBlock("Recruit", -seatsY + 48f, GAP);
        SpaceTheme.Glass(b.Rt, 36f, SpaceTheme.GLASS);
        // 締め切った募集はうすく。ただし自分が操作できるもの（自分の募集・参加中）はボタンが使えないように見えないよう、うすくしない
        if (it.Status == SpaceRecruitDigest.State.Closed && !it.Mine && !it.Joined)
            b.Rt.gameObject.AddComponent<CanvasGroup>().alpha = 0.55f;

        var when = SpaceTheme.Text(b.Rt, "When", SpaceTheme.NoParse(SpaceTheme.Safe(it.When)), 40, SpaceTheme.TEXT,
            new Vector2(0f, 1f), new Vector2(32f, -56f), new Vector2(600f, 54f), FontStyles.Bold,
            TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
        when.enableAutoSizing = true;
        when.fontSizeMin = 28f;
        when.fontSizeMax = 40f;
        StatusPill(b.Rt, it);

        var game = SpaceTheme.Text(b.Rt, "Game", SpaceTheme.NoParse(SpaceTheme.Safe(it.Game)), it.IsTitle ? 34 : 28,
            it.IsTitle ? SpaceTheme.TEXT : SpaceTheme.TEXT_SUB, new Vector2(0f, 1f), new Vector2(32f, -112f),
            new Vector2(CARD_W - 64f, 48f), FontStyles.Bold, TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
        game.enableAutoSizing = true;
        game.fontSizeMin = 22f;
        game.fontSizeMax = it.IsTitle ? 34f : 28f;

        if (hasTags)
        {
            var tags = SpaceTheme.Text(b.Rt, "Tags", it.Tags, 24, SpaceTheme.TEXT_MUTED, new Vector2(0f, 1f),
                new Vector2(32f, -158f), new Vector2(CARD_W - 64f, 36f), FontStyles.Bold, TextAlignmentOptions.Left,
                new Vector2(0f, 0.5f));
            tags.enableAutoSizing = true;
            tags.fontSizeMin = 18f;
            tags.fontSizeMax = 24f;
        }

        // 参加した人: 人数の点と同じ光る点＋「名前さん」＋選んだひとこと（自分の行はミント色）。
        // 名前を出すのは自分とフレンドだけ。フレンドでない人は上から順に「ユーザーA」「ユーザーB」…と呼んで、
        // ひとことだけ見せる（カードごとに数え直す。並びは SpaceRecruitDigest でユーザーIDの順にそろえてある）
        int others = 0;
        for (int i = 0; i < memberCount; i++)
        {
            var m = it.Members[i];
            float y = membersTop - MEMBER_ROW_H * (i + 0.5f);
            bool named = m.Me || m.Friend;
            var color = m.Me ? SpaceTheme.MINT : named ? SpaceTheme.CYAN : SpaceTheme.TEXT_MUTED;
            var dotPos = new Vector2(32f + 9f, y);
            var glow = SpaceTheme.Rect("MemberGlow", b.Rt, new Vector2(0f, 1f), dotPos, new Vector2(36f, 36f));
            SpaceTheme.Img(glow, SpaceSprites.Glow, SpaceTheme.A(color, 0.5f));
            var dot = SpaceTheme.Rect("MemberDot", b.Rt, new Vector2(0f, 1f), dotPos, new Vector2(12f, 12f));
            SpaceTheme.Img(dot, SpaceSprites.Circle, color);
            string name = named
                ? SpaceTheme.NoParse(SpaceTheme.Safe(m.Name)) + T_SAN // 名前に < があっても書式にしない
                : OtherName(others++);
            var line = SpaceTheme.Text(b.Rt, "Member", $"{name}<color={SpaceTheme.SUB_HEX}>　{m.Message}</color>", 26,
                m.Me ? SpaceTheme.MINT : named ? SpaceTheme.TEXT : SpaceTheme.TEXT_MUTED, new Vector2(0f, 1f),
                new Vector2(64f, y), new Vector2(CARD_W - 64f - 32f, 36f), FontStyles.Bold, TextAlignmentOptions.Left,
                new Vector2(0f, 0.5f));
            line.enableAutoSizing = true;
            line.fontSizeMin = 18f;
            line.fontSizeMax = 26f;
        }

        SeatDots(b.Rt, it, seatsY);

        // 右下のボタン（旧ホームの募集ボードと同じ並び）:
        //   自分の募集＝［締め切る］［取り消す］、締め切ったあとは［再開する］［取り消す］
        //   参加している＝参加をやめる（締め切られたあとも）/ 空きがある＝参加する（満員・締め切りはなし）
        var target = it;
        float buttonW = 0f;
        if (it.Mine)
        {
            // 取り消す: 確認してから消す。消すと1人1件の枠が空いて、また募集できる
            buttonW = 152f;
            PillButton(b.Rt, seatsY, -28f, buttonW, T_CANCEL_POST, SpaceTheme.DANGER, () => ConfirmCancel(target));
            if (it.Status != SpaceRecruitDigest.State.Closed)
            {
                // 締め切る: 確認してから。締め切るとほかの人は参加できず、1人1件の枠も空く
                PillButton(b.Rt, seatsY, -(28f + buttonW + 12f), 152f, T_CLOSE_POST, SpaceTheme.GOLD,
                    () => ConfirmClosePost(target));
            }
            else
            {
                // 再開する: 確認なし（締め切り直せるので）。受付中の募集がほかにあれば再開しない（1人1件まで）
                PillButton(b.Rt, seatsY, -(28f + buttonW + 12f), 152f, T_REOPEN, SpaceTheme.MINT,
                    () => ReopenPostAsync(target).Forget());
            }
            buttonW += 12f + 152f;
        }
        else if (it.Joined)
        {
            buttonW = 210f;
            PillButton(b.Rt, seatsY, -28f, buttonW, T_LEAVE, SpaceTheme.TEXT_SUB, () => LeaveAsync(target).Forget());
        }
        else if (it.Status == SpaceRecruitDigest.State.Open)
        {
            buttonW = 176f;
            PillButton(b.Rt, seatsY, -28f, buttonW, T_JOIN, SpaceTheme.MINT, () => OpenJoinSheet(target));
        }
        // 募集した人: 自分なら「あなたの募集」、フレンドなら「○○さんの募集」。フレンドでなければ名前を出さない
        if (it.Mine || it.HostFriend)
        {
            float hostRight = buttonW > 0f ? -(28f + buttonW + 20f) : -32f;
            // 名前は人数の点（最大10個＝左から約380）とボタンの間に収める
            float hostW = Mathf.Min(buttonW > 0f ? 300f : 440f, CARD_W + hostRight - 380f);
            var host = SpaceTheme.Text(b.Rt, "Host",
                it.Mine ? T_MINE : SpaceTheme.NoParse(SpaceTheme.Safe(it.Host)) + T_HOST_B, 24,
                it.Mine ? SpaceTheme.MINT : SpaceTheme.TEXT_MUTED, new Vector2(1f, 1f), new Vector2(hostRight, seatsY),
                new Vector2(hostW, 36f), FontStyles.Bold, TextAlignmentOptions.Right, new Vector2(1f, 0.5f));
            host.enableAutoSizing = true;
            host.fontSizeMin = 18f;
            host.fontSizeMax = 24f;
        }
        return b;
    }

    /// <summary>
    /// フレンドでない参加者の呼び名。0→「ユーザーA」、1→「ユーザーB」…25→「ユーザーZ」、26→「ユーザーAA」
    /// （相席は最大9人なので、ふだんは1文字）
    /// </summary>
    private static string OtherName(int index)
    {
        string letters = "";
        for (int n = index + 1; n > 0; n = (n - 1) / USER_LETTERS.Length)
            letters = USER_LETTERS[(n - 1) % USER_LETTERS.Length] + letters;
        return T_USER + letters;
    }

    /// <summary>カードの右下の小さなボタン（色の薄い地＋同じ色の縁と文字）。right はカードの右端からの位置</summary>
    private static void PillButton(RectTransform card, float y, float right, float width, string text, Color color,
        UnityEngine.Events.UnityAction onTap)
    {
        var rt = SpaceTheme.Rect("Button", card, new Vector2(1f, 1f), new Vector2(right, y), new Vector2(width, 64f),
            new Vector2(1f, 0.5f));
        var face = SpaceTheme.Rounded(rt, SpaceTheme.A(color, 0.14f), 31f, true);
        var edge = SpaceTheme.Rect("Edge", rt);
        SpaceTheme.Stretch(edge);
        SpaceTheme.RoundedStroke(edge, SpaceTheme.A(color, 0.6f), 31f);
        var label = SpaceTheme.Text(rt, "Label", text, 26, color, FontStyles.Bold);
        SpaceTheme.Stretch(label.rectTransform);
        SpaceTheme.Clickable(rt.gameObject, face, onTap);
    }

    // ---------------------------------------------------------------- 募集する・締め切る・取り消す
    /// <summary>一番下の「相席募集をする」。名前がない・自分の募集が出ているときは知らせるだけ（旧ホームと同じく1人1件まで）</summary>
    private void OpenRecruitForm()
    {
        if (_closing || _form != null || _viewer != null) return;
        var data = UserDataManager.instance != null ? UserDataManager.instance.UserData : null;
        if (data == null || string.IsNullOrEmpty(data.Username))
        {
            SpaceTheme.Toast(T_NO_NAME, _canvas);
            return;
        }
        if (_recruits != null && _recruits.Exists(r => r.Mine && r.Status != SpaceRecruitDigest.State.Closed))
        {
            SpaceTheme.Toast(T_ONE_ONLY, _canvas);
            return;
        }
        _form = SpaceRecruitForm.Open((RectTransform)transform, _canvas, RefreshAfterWrite, () => _form = null);
    }

    private void ConfirmCancel(SpaceRecruitDigest.Item it)
    {
        if (_closing || _form != null) return;
        SpaceDialog.Confirm(_canvas, T_CANCEL_TITLE, T_CANCEL_BODY, T_CANCEL_POST, true,
            () => CancelPostAsync(it.Id).Forget());
    }

    /// <summary>自分の募集を消す（ルールで投稿者本人だけが消せる）</summary>
    private async UniTaskVoid CancelPostAsync(string id)
    {
        var busy = SpaceDialog.Busy(_canvas, T_CANCELING);
        bool ok = false;
        try
        {
            await FirebaseFirestore.DefaultInstance.Collection("recruits").Document(id).DeleteAsync().AsUniTask();
            ok = true;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[News] 募集の取り消しに失敗: {id} {ex.Message}");
        }
        if (busy != null) Destroy(busy);
        SpaceTheme.Toast(ok ? T_CANCELED : T_FAILED, _canvas);
        if (ok) RefreshAfterWrite();
    }

    private void ConfirmClosePost(SpaceRecruitDigest.Item it)
    {
        if (_closing || _form != null || _writing) return;
        SpaceDialog.Confirm(_canvas, T_CLOSE_TITLE, T_CLOSE_BODY, T_CLOSE_POST, false,
            () => ClosePostAsync(it).Forget());
    }

    /// <summary>自分の募集を締め切る（status を closed にするだけ。ルールで投稿者本人だけが変えられる。旧ホームと同じ書き込み）</summary>
    private UniTaskVoid ClosePostAsync(SpaceRecruitDigest.Item it) =>
        SetPostStatusAsync(it, "closed", T_CLOSING_POST, T_CLOSED_POST);

    /// <summary>
    /// 締め切った自分の募集を再開する（status を open に戻す）。
    /// 受付中の自分の募集がほかにあれば再開しない（旧ホームと同じく、受付中の募集は1人1件まで）
    /// </summary>
    private UniTaskVoid ReopenPostAsync(SpaceRecruitDigest.Item it)
    {
        if (_recruits != null
            && _recruits.Exists(r => r.Mine && r.Id != it.Id && r.Status != SpaceRecruitDigest.State.Closed))
        {
            SpaceTheme.Toast(T_ONE_OPEN, _canvas);
            return default;
        }
        return SetPostStatusAsync(it, "open", T_REOPENING, T_REOPENED);
    }

    /// <summary>自分の募集の受付状態を変える（status だけ。ルールで投稿者本人だけが open/closed にできる。旧ホームと同じ書き込み）</summary>
    private async UniTaskVoid SetPostStatusAsync(SpaceRecruitDigest.Item it, string status, string busyText,
        string doneText)
    {
        if (_writing || _closing) return;
        _writing = true;
        var busy = SpaceDialog.Busy(_canvas, busyText);
        bool ok = false;
        try
        {
            await FirebaseFirestore.DefaultInstance.Collection("recruits").Document(it.Id)
                .UpdateAsync("status", status).AsUniTask();
            ok = true;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[News] 募集の受付状態の変更に失敗: {it.Id} → {status} {ex.Message}");
        }
        _writing = false;
        if (busy != null) Destroy(busy);
        SpaceTheme.Toast(ok ? doneText : T_FAILED, _canvas);
        if (ok) RefreshAfterWrite();
    }

    /// <summary>
    /// 書き込んだあとに相席募集の欄を新しくする。購読中は書き込みの結果が Changed で届くので、
    /// その場の内容で作り直すだけ（読み込み中の表示をはさまない）。購読していなければ読み直す
    /// </summary>
    private void RefreshAfterWrite()
    {
        if (this == null || _closing) return;
        if (SpaceRecruitDigest.IsLive) LoadRecruitsAsync().Forget();
        else ReloadRecruits();
    }

    // ---------------------------------------------------------------- 参加する・参加をやめる
    /// <summary>「参加する」: 旧ホームと同じく、定型のひとことを選んで参加を伝える（自由入力はない）</summary>
    private void OpenJoinSheet(SpaceRecruitDigest.Item it)
    {
        if (_closing || _joinSheet != null || _form != null || _writing) return;
        if (!HasUserName())
        {
            SpaceTheme.Toast(T_NO_NAME_JOIN, _canvas);
            return;
        }

        _joinSheet = SpaceTheme.Rect("JoinSheet", transform);
        SpaceTheme.Stretch(_joinSheet);
        var group = _joinSheet.gameObject.AddComponent<CanvasGroup>();
        var dim = SpaceTheme.Rect("Dim", _joinSheet);
        SpaceTheme.Stretch(dim);
        var dimImg = SpaceTheme.Img(dim, null, SpaceTheme.DIM, true);
        SpaceTheme.Clickable(dim.gameObject, dimImg, CloseJoinSheet, false);
        var safe = SpaceTheme.Rect("Safe", _joinSheet);
        SpaceTheme.Stretch(safe);
        safe.gameObject.AddComponent<SpaceSafeArea>();

        var msgs = RecruitBoardController.JOIN_MSGS;
        const float top = 206f, rowH = 96f, rowGap = 16f;
        float innerW = SpaceTheme.CanvasSize(_canvas).x - 64f - 112f;
        float cancelTop = top + msgs.Length * (rowH + rowGap) + 8f;
        float h = cancelTop + 112f + 40f;

        var sheet = SpaceTheme.Rect("Sheet", safe);
        sheet.anchorMin = new Vector2(0f, 0f);
        sheet.anchorMax = new Vector2(1f, 0f);
        sheet.pivot = new Vector2(0.5f, 0f);
        sheet.sizeDelta = new Vector2(-64f, h);
        sheet.anchoredPosition = new Vector2(0f, 32f);
        SpaceTheme.Glass(sheet, 56f, SpaceTheme.GLASS_DEEP, null, true);

        SpaceTheme.Text(sheet, "Title", T_JOIN_TITLE, 44, SpaceTheme.TEXT, new Vector2(0f, 1f), new Vector2(56f, -84f),
            new Vector2(760f, 60f), FontStyles.Bold, TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
        // 募集した人の名前はフレンドのときだけ（参加できるのは自分以外の募集なので、自分の場合はない）
        string summaryText = it.HostFriend
            ? SpaceTheme.NoParse(SpaceTheme.Safe(it.When + "　" + it.Host)) + T_HOST_B
            : SpaceTheme.NoParse(SpaceTheme.Safe(it.When));
        var summary = SpaceTheme.Text(sheet, "Summary", summaryText, 28, SpaceTheme.TEXT_SUB,
            new Vector2(0f, 1f), new Vector2(56f, -150f), new Vector2(innerW, 44f), FontStyles.Bold,
            TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
        summary.enableAutoSizing = true;
        summary.fontSizeMin = 20f;
        summary.fontSizeMax = 28f;

        for (int i = 0; i < msgs.Length; i++)
        {
            int msg = i;
            var row = SpaceTheme.Rect("Message", sheet, new Vector2(0f, 1f), new Vector2(56f, -(top + i * (rowH + rowGap))),
                new Vector2(innerW, rowH), new Vector2(0f, 1f));
            var face = SpaceTheme.Glass(row, 32f, new Color(0.09f, 0.12f, 0.30f, 0.62f), null, true);
            SpaceTheme.Clickable(row.gameObject, face, () => JoinAsync(it, msg).Forget());
            var label = SpaceTheme.Text(row, "Label", msgs[i], 32, SpaceTheme.TEXT, FontStyles.Bold);
            SpaceTheme.Stretch(label.rectTransform, 24f, 0f, 24f, 0f);
        }

        var cancel = SpaceTheme.Rect("Cancel", sheet, new Vector2(0.5f, 1f), new Vector2(0f, -cancelTop),
            new Vector2(440f, 112f), new Vector2(0.5f, 1f));
        var cancelFace = SpaceTheme.Glass(cancel, 46f, SpaceTheme.A(new Color(0.09f, 0.12f, 0.30f), 0.63f), null, true);
        SpaceTheme.Clickable(cancel.gameObject, cancelFace, CloseJoinSheet);
        var cancelText = SpaceTheme.Text(cancel, "Label", T_JOIN_CANCEL, 36, SpaceTheme.TEXT_SUB, FontStyles.Bold);
        SpaceTheme.Stretch(cancelText.rectTransform);

        group.alpha = 0f;
        var basePos = sheet.anchoredPosition;
        StartCoroutine(SpaceTheme.Tween(0.28f, 0f, k =>
        {
            if (group == null) return;
            group.alpha = k;
            sheet.anchoredPosition = basePos + new Vector2(0f, -60f * (1f - k));
        }));
    }

    private void CloseJoinSheet()
    {
        if (_joinSheet == null) return;
        Destroy(_joinSheet.gameObject);
        _joinSheet = null;
    }

    private static bool HasUserName()
    {
        var manager = UserDataManager.instance;
        return manager != null && manager.UserData != null && !string.IsNullOrEmpty(manager.UserData.Username)
               && !string.IsNullOrEmpty(manager.UID);
    }

    /// <summary>参加を伝える（participants の自分のキーに {名前, ひとこと}。旧ホームと同じ書き込み）</summary>
    private async UniTaskVoid JoinAsync(SpaceRecruitDigest.Item it, int msg)
    {
        if (_writing) return;
        if (!HasUserName())
        {
            SpaceTheme.Toast(T_NO_NAME_JOIN, _canvas);
            return;
        }
        var manager = UserDataManager.instance;
        string uid = manager.UID, name = manager.UserData.Username;
        _writing = true;
        CloseJoinSheet();
        var busy = SpaceDialog.Busy(_canvas, T_JOINING);
        bool ok = false;
        try
        {
            await FirebaseFirestore.DefaultInstance.Collection("recruits").Document(it.Id)
                .UpdateAsync(RecruitBoardController.JoinUpdate(uid, name, msg)).AsUniTask();
            ok = true;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[News] 相席募集への参加に失敗: {it.Id} {ex.Message}");
        }
        _writing = false;
        if (busy != null) Destroy(busy);
        SpaceTheme.Toast(ok ? T_JOIN_DONE : T_FAILED, _canvas);
        if (ok) RefreshAfterWrite();
    }

    /// <summary>参加をやめる（participants の自分のキーを消す）。もう一度「参加する」で参加し直せる</summary>
    private async UniTaskVoid LeaveAsync(SpaceRecruitDigest.Item it)
    {
        if (_writing || _closing) return;
        var manager = UserDataManager.instance;
        string uid = manager != null ? manager.UID : null;
        if (string.IsNullOrEmpty(uid)) return;
        _writing = true;
        var busy = SpaceDialog.Busy(_canvas, T_LEAVING);
        bool ok = false;
        try
        {
            await FirebaseFirestore.DefaultInstance.Collection("recruits").Document(it.Id)
                .UpdateAsync(RecruitBoardController.LeaveUpdate(uid)).AsUniTask();
            ok = true;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[News] 相席募集の参加の取り消しに失敗: {it.Id} {ex.Message}");
        }
        _writing = false;
        if (busy != null) Destroy(busy);
        SpaceTheme.Toast(ok ? T_LEAVE_DONE : T_FAILED, _canvas);
        if (ok) RefreshAfterWrite();
    }

    /// <summary>右上の空き状況（あと n人＝ミント / 満員＝金 / 締め切り＝灰。自分が参加している募集は「参加中」）</summary>
    private static void StatusPill(RectTransform card, SpaceRecruitDigest.Item it)
    {
        string text;
        Color c;
        if (it.Joined && it.Status != SpaceRecruitDigest.State.Closed)
        {
            text = T_JOINED;
            c = SpaceTheme.MINT;
        }
        else switch (it.Status)
        {
            case SpaceRecruitDigest.State.Full:
                text = T_FULL;
                c = SpaceTheme.GOLD;
                break;
            case SpaceRecruitDigest.State.Closed:
                text = T_CLOSED;
                c = SpaceTheme.TEXT_SUB;
                break;
            default:
                text = T_LEFT_A + it.Left + T_LEFT_B;
                c = SpaceTheme.MINT;
                break;
        }
        var pill = SpaceTheme.Rect("Status", card, new Vector2(1f, 1f), new Vector2(-28f, -56f),
            new Vector2(48f + 28f * text.Length, 52f), new Vector2(1f, 0.5f));
        SpaceTheme.Rounded(pill, SpaceTheme.A(c, 0.12f), 25f);
        var edge = SpaceTheme.Rect("Edge", pill);
        SpaceTheme.Stretch(edge);
        SpaceTheme.RoundedStroke(edge, SpaceTheme.A(c, 0.5f), 25f);
        var label = SpaceTheme.Text(pill, "Label", text, 28, c, FontStyles.Bold);
        SpaceTheme.Stretch(label.rectTransform);
    }

    /// <summary>集まる人数を小さな星の列で（光る点＝主催者と参加済み、うすい点＝まだ空き）</summary>
    private static void SeatDots(RectTransform card, SpaceRecruitDigest.Item it, float y)
    {
        const float D = 18f, STEP = 34f;
        for (int i = 0; i < it.Capacity; i++)
        {
            var pos = new Vector2(32f + D * 0.5f + i * STEP, y);
            if (i < it.Filled)
            {
                var glow = SpaceTheme.Rect("SeatGlow", card, new Vector2(0f, 1f), pos, new Vector2(44f, 44f));
                SpaceTheme.Img(glow, SpaceSprites.Glow, SpaceTheme.A(SpaceTheme.CYAN, 0.55f));
                var dot = SpaceTheme.Rect("Seat", card, new Vector2(0f, 1f), pos, new Vector2(D, D));
                SpaceTheme.Img(dot, SpaceSprites.Circle, SpaceTheme.CYAN);
            }
            else
            {
                var dot = SpaceTheme.Rect("Seat", card, new Vector2(0f, 1f), pos, new Vector2(D, D));
                SpaceTheme.Img(dot, SpaceSprites.Circle, SpaceTheme.A(SpaceTheme.TEXT_MUTED, 0.8f));
            }
        }
    }

    // ================================================================
    // 画像の拡大表示
    // ================================================================
    private void OpenViewer(Texture2D tex)
    {
        if (_viewer != null || _closing) return;
        _viewer = SpaceTheme.Rect("Viewer", transform);
        SpaceTheme.Stretch(_viewer);
        var group = _viewer.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;

        // 指が画像の外に出ても閉じないよう、背景のタップでは閉じない（閉じるのはボタン・戻る操作）
        var dim = SpaceTheme.Rect("Dim", _viewer);
        SpaceTheme.Stretch(dim);
        SpaceTheme.Img(dim, null, new Color(0.004f, 0.006f, 0.02f, 0.97f), true);

        var safe = SpaceTheme.Rect("Safe", _viewer);
        SpaceTheme.Stretch(safe);
        safe.gameObject.AddComponent<SpaceSafeArea>();

        // 画面に収まる大きさで中央に（縦横比はそのまま）。拡大は枠の中で行う
        var size = SpaceTheme.CanvasSize(_canvas);
        var insets = SpaceTheme.SafeInsets(size);
        float maxW = size.x - 40f;
        float maxH = Mathf.Max(200f, size.y - insets.top - insets.bottom - 320f);
        float s = Mathf.Min(maxW / tex.width, maxH / tex.height);
        var area = SpaceTheme.Rect("Area", safe, SpaceTheme.CENTER, new Vector2(0f, -10f), new Vector2(maxW, maxH));
        area.gameObject.AddComponent<RectMask2D>();
        var pic = SpaceTheme.Rect("Picture", area, SpaceTheme.CENTER, Vector2.zero,
            new Vector2(tex.width * s, tex.height * s));
        var raw = pic.gameObject.AddComponent<RawImage>();
        raw.texture = tex;
        pic.gameObject.AddComponent<SpaceZoomImage>().Setup(area);

        CloseButton(safe, CloseViewer);
        SpaceTheme.Text(safe, "Hint", T_ZOOM_HINT, 24, SpaceTheme.TEXT_MUTED, new Vector2(0.5f, 0f),
            new Vector2(0f, 64f), new Vector2(900f, 36f), FontStyles.Bold);
        StartCoroutine(SpaceTheme.Tween(0.2f, 0f, k => group.alpha = k));
    }

    private void CloseViewer()
    {
        if (_viewer == null) return;
        Destroy(_viewer.gameObject);
        _viewer = null;
    }

    /// <summary>右上の閉じるボタン（管理者のQR一覧などと同じ）</summary>
    private static void CloseButton(RectTransform safe, UnityEngine.Events.UnityAction onClick)
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

/// <summary>
/// お店からのお知らせ画像（master/config.event_images の URL 一覧）の読み込み。
/// 旧ホームのお知らせページ（FirebaseDataReceiver）と同じキャッシュ
/// （persistentDataPath/EventImageCache/{番号}.jpg と PlayerPrefs "CachedEventImageUrls"）を共有し、
/// URL一覧が前回と変わったときだけダウンロードし直す（変わらなければ通信なし）。
/// URL は gs://～・https://～・Storage 内のパスのどれでもよい（旧ホームと同じ）。
/// 管理者が画像を編集したときは、手元のデータでキャッシュを作り直す（Store。お知らせでダウンロードし直さない）。
/// </summary>
public static class SpaceNewsImages
{
    private const string CACHE_FOLDER = "EventImageCache";
    private const string CACHED_URLS_KEY = "CachedEventImageUrls";

    private static string Dir => Path.Combine(Application.persistentDataPath, CACHE_FOLDER);
    private static string FileOf(int index) => Path.Combine(Dir, index + ".jpg");
    private static string _checkedSignature; // キャッシュのフォルダの中身がどの一覧のものか（このセッションで確かめた分）
    // キャッシュを消した・作り直した回数。その前に始まったダウンロードが、新しいキャッシュの別の番号に書き込まないように
    private static int _generation;

    /// <summary>URL一覧（master/config。セッション中1回の読み取りで、ほかの機能と共用）</summary>
    public static async UniTask<List<string>> GetUrlsAsync()
    {
        var config = await MasterData.GetConfigAsync();
        return config.EventImages ?? new List<string>();
    }

    /// <summary>URL一覧が保存済みのものと違えば古い画像を消す（番号で保存しているので全部取り直す。旧ホームと同じ）</summary>
    public static void Prepare(List<string> urls)
    {
        string signature = string.Join("\n", urls);
        if (_checkedSignature == signature) return;
        _checkedSignature = signature;
        if (PlayerPrefs.GetString(CACHED_URLS_KEY, "__none__") == signature) return;
        _generation++;
        try
        {
            if (Directory.Exists(Dir)) Directory.Delete(Dir, true);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[News] 画像キャッシュの削除に失敗: {ex.Message}");
        }
        PlayerPrefs.DeleteKey(CACHED_URLS_KEY);
        PlayerPrefs.Save();
    }

    /// <summary>全部そろったら一覧を記録する（次からはキャッシュだけで表示できる。失敗があれば次回取り直す）</summary>
    public static void MarkComplete(List<string> urls)
    {
        string signature = string.Join("\n", urls);
        if (_checkedSignature != null && _checkedSignature != signature) return; // 途中でキャッシュが作り直された
        PlayerPrefs.SetString(CACHED_URLS_KEY, signature);
        PlayerPrefs.Save();
    }

    /// <summary>
    /// キャッシュがこの一覧のものなら index 番目のデータ（無ければ null。ダウンロードはしない）。
    /// 管理者の編集画面が、開いたときに表示済みの画像をダウンロードし直さないために使う
    /// </summary>
    public static byte[] ReadCached(List<string> urls, int index)
    {
        string signature = string.Join("\n", urls);
        if (_checkedSignature != signature && PlayerPrefs.GetString(CACHED_URLS_KEY, "__none__") != signature)
            return null;
        try
        {
            string path = FileOf(index);
            return File.Exists(path) ? File.ReadAllBytes(path) : null;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[News] 画像キャッシュの読み込みに失敗 ({index}): {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 一覧と画像のデータでキャッシュを作り直す（管理者が画像を足した・並べ替えた・消したあと）。
    /// データの無い画像は、次にお知らせで表示するときにダウンロードされる
    /// </summary>
    public static void Store(List<string> urls, Func<string, byte[]> bytesOf)
    {
        string signature = string.Join("\n", urls);
        _generation++;
        try
        {
            if (Directory.Exists(Dir)) Directory.Delete(Dir, true);
            Directory.CreateDirectory(Dir);
            bool complete = true;
            for (int i = 0; i < urls.Count; i++)
            {
                if (string.IsNullOrEmpty(urls[i])) continue;
                var bytes = bytesOf(urls[i]);
                if (bytes == null)
                {
                    complete = false;
                    continue;
                }
                File.WriteAllBytes(FileOf(i), bytes);
            }
            _checkedSignature = signature;
            if (complete) PlayerPrefs.SetString(CACHED_URLS_KEY, signature);
            else PlayerPrefs.DeleteKey(CACHED_URLS_KEY);
            PlayerPrefs.Save();
        }
        catch (Exception ex)
        {
            // 作り直しに失敗したら、次に表示するときに全部取り直させる
            Debug.LogWarning($"[News] 画像キャッシュの作り直しに失敗: {ex.Message}");
            _checkedSignature = null;
            PlayerPrefs.DeleteKey(CACHED_URLS_KEY);
            PlayerPrefs.Save();
        }
    }

    /// <summary>index 番目の画像のデータ（キャッシュ → なければダウンロードしてそのまま保存）。失敗したら null</summary>
    public static async UniTask<byte[]> LoadAsync(string url, int index)
    {
        string path = FileOf(index);
        try
        {
            if (File.Exists(path)) return File.ReadAllBytes(path);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[News] 画像キャッシュの読み込みに失敗 ({index}): {ex.Message}");
        }

        int generation = _generation;
        var bytes = await DownloadAsync(url);
        if (bytes == null) return null;
        if (generation != _generation) return bytes; // ダウンロード中にキャッシュが作り直された（この番号は別の画像かもしれない）
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllBytes(path, bytes);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[News] 画像キャッシュの保存に失敗 ({index}): {ex.Message}");
        }
        return bytes;
    }

    /// <summary>画像を1枚ダウンロードする（キャッシュには書かない）。失敗したら null</summary>
    public static async UniTask<byte[]> DownloadAsync(string url)
    {
        try
        {
            string http = await ResolveAsync(url);
            using (var req = UnityWebRequest.Get(http))
            {
                req.timeout = 30;
                var op = req.SendWebRequest();
                while (!op.isDone) await UniTask.Yield();
                if (req.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning($"[News] 画像のダウンロードに失敗: {req.error} / HTTP {req.responseCode}");
                    return null;
                }
                return req.downloadHandler.data;
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[News] 画像の取得に失敗: {ex.Message}");
            return null;
        }
    }

    private static async UniTask<string> ResolveAsync(string url)
    {
        if (url.StartsWith("https://") || url.StartsWith("http://")) return url;
        var storage = FirebaseStorage.DefaultInstance;
        var reference = url.StartsWith("gs://") ? storage.GetReferenceFromUrl(url) : storage.RootReference.Child(url);
        var uri = await reference.GetDownloadUrlAsync().AsUniTask();
        return uri.ToString();
    }
}

/// <summary>
/// 相席募集（recruits）の概要。旧ホームの募集ボード（RecruitBoardController）と同じ基準で、
/// 通報が付いたもの（自分の募集以外）と集合時刻を過ぎたものを除き、日付→時刻の順に並べる。
/// 表示する文字は募集ボードと同じく定型文とカタログから作り、Firestore の文字列をそのまま出すのは名前だけ。
/// 読み取りは開いたときの1回（期限内の募集の件数ぶん）。1分以内に開き直したときは前回の結果を使う。
/// </summary>
public static class SpaceRecruitDigest
{
    public enum State { Open, Full, Closed }

    public sealed class Item
    {
        public string Id;      // recruits のドキュメントID（取り消し・参加の書き込みに使う）
        public bool Mine;      // 自分の募集か
        public bool Joined;    // 自分が参加しているか
        public string When;    // "今日 19:00～"
        public string Game;    // 『カタン』 / ゲームはお店できめる など
        public bool IsTitle;   // 実際のゲーム名か（大きく出す）
        public string Tags;    // "初心者歓迎・まったり"
        public string Host;    // 募集した人の名前（表示はフレンドのときだけ。HostFriend）
        public bool HostFriend; // 募集した人がフレンドか
        public int Capacity;   // 集まる人数（募集した人を含む）
        public int Filled;     // 募集した人＋参加済みの人
        public int Left;       // あと何人
        public State Status;
        public List<Member> Members = new List<Member>(); // 参加した人
    }

    /// <summary>参加した人（名前は users の名前とルールで一致させてある。ひとことは定型の番号）</summary>
    public sealed class Member
    {
        public string Name;    // 表示は自分とフレンドだけ
        public string Message; // RecruitBoardController.JOIN_MSGS の文
        public bool Me;        // 自分
        public bool Friend;    // フレンド
    }

    private const float CACHE_SECONDS = 60f;
    private static List<Item> _cache;
    private static float _cachedAt;
    private static ListenerRegistration _listener;
    private static bool _listening;
    private static QuerySnapshot _live; // リアルタイム購読で届いた最新の結果

    /// <summary>購読中に募集が増えた・減った・変わったとき（新ホームの「New」の判定・開いているお知らせの更新）</summary>
    public static event Action Changed;

    /// <summary>購読中の最新の概要（購読していない・まだ届いていないときは null）。読み取りは発生しない</summary>
    public static List<Item> Current => _live != null ? Parse(_live) : null;

    /// <summary>リアルタイム購読の結果が届いているか（届いていれば、書き込んだ結果も Changed で自動的に届く）</summary>
    public static bool IsLive => _live != null;

    /// <summary>
    /// 期限内の募集のリアルタイム購読を始める（新ホームを開いている間）。読み取りは最初に件数ぶんと、変わった分だけ。
    /// 購読中は LoadAsync も届いている内容を返すので、お知らせを開いても読み取りは増えない。
    /// </summary>
    public static void StartListening()
    {
        if (_listening) return;
        _listening = true; // 最初の結果が Listen の戻りより先に届いても受け取れるよう、先に立てる
        BoardGameCatalog.SyncAsync().Forget(); // ゲーム名の照合用（変わっていなければ読み取りなし）
        _listener = FirebaseFirestore.DefaultInstance.Collection("recruits")
            .WhereGreaterThan("expires_at", Timestamp.GetCurrentTimestamp())
            .Listen(snap =>
            {
                if (!_listening) return; // 止めたあとに届いた分
                _live = snap;
                Changed?.Invoke();
            });
    }

    public static void StopListening()
    {
        _listening = false;
        _listener?.Stop();
        _listener = null;
        _live = null;
    }

    /// <summary>次の LoadAsync で読み直させる（購読していないときに、募集を出した・取り消したあと）</summary>
    public static void Invalidate() => _cache = null;

    /// <summary>期限内の募集の概要。読み取りに失敗したら null</summary>
    public static async UniTask<List<Item>> LoadAsync()
    {
        if (_live != null) return Parse(_live); // 購読中は届いている最新の内容（読み取りなし）
        if (_cache != null && Time.realtimeSinceStartup - _cachedAt < CACHE_SECONDS) return _cache;

        QuerySnapshot snap;
        try
        {
            // ゲーム名の照合に使うカタログを最新に（新ホームには起動時の同期がない。変わっていなければ読み取りなし）
            await BoardGameCatalog.SyncAsync();
            snap = await FirebaseFirestore.DefaultInstance.Collection("recruits")
                .WhereGreaterThan("expires_at", Timestamp.GetCurrentTimestamp())
                .GetSnapshotAsync().AsUniTask();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[News] 相席募集の読み込みに失敗: {ex.Message}");
            return null;
        }

        _cache = Parse(snap);
        _cachedAt = Time.realtimeSinceStartup;
        return _cache;
    }

    /// <summary>
    /// 読み取り結果を概要にする。通報付き（自分の以外）・集合時刻を過ぎたものは除き、日付→時刻の順に並べる
    /// （時刻で落とすのは呼ぶたびに今の時刻で行う。購読の結果は時間がたっても変わらないため）
    /// </summary>
    private static List<Item> Parse(QuerySnapshot snap)
    {
        string myUid = UserDataManager.instance != null ? UserDataManager.instance.UID : null;
        var now = DateTime.Now;
        var rows = new List<(string date, int slot, Item item)>();
        foreach (var doc in snap.Documents)
        {
            try
            {
                var d = doc.ToDictionary();
                string uid = Str(d, "uid"), date = Str(d, "date");
                int slot = Int(d, "slot");
                bool reported = d.TryGetValue("reports", out var ro) && ro is Dictionary<string, object> rm
                                && rm.Count > 0;
                if ((reported && uid != myUid) || RecruitBoardController.IsPastSlot(date, slot, now)) continue;

                int seats = Mathf.Max(0, Int(d, "seats"));
                var members = d.TryGetValue("participants", out var po) ? po as Dictionary<string, object> : null;
                int joined = members != null ? members.Count : 0;
                bool meIn = members != null && !string.IsNullOrEmpty(myUid) && members.ContainsKey(myUid);
                var memberList = new List<Member>();
                if (members != null)
                {
                    var msgs = RecruitBoardController.JOIN_MSGS;
                    // 並びはユーザーIDの順にそろえる（読み込み直しても同じ並び＝フレンドでない人の
                    // 「ユーザーA」「ユーザーB」…も同じになるように。参加した時刻は持っていないので、人が増減すると文字はずれうる）
                    var memberUids = new List<string>(members.Keys);
                    memberUids.Sort(string.CompareOrdinal);
                    foreach (var memberUid in memberUids)
                    {
                        if (!(members[memberUid] is Dictionary<string, object> mv)) continue;
                        memberList.Add(new Member
                        {
                            Name = Str(mv, "name"),
                            Message = msgs[Mathf.Clamp(Int(mv, "msg"), 0, msgs.Length - 1)],
                            Me = memberUid == myUid,
                            Friend = SpaceFriends.IsFriend(memberUid),
                        });
                    }
                }
                var tags = new List<string>();
                if (d.TryGetValue("tags", out var to) && to is List<object> tl)
                {
                    foreach (var t in tl)
                    {
                        int k = Convert.ToInt32(t);
                        if (k >= 0 && k < RecruitBoardController.TAGS.Length) tags.Add(RecruitBoardController.TAGS[k]);
                    }
                }
                string game = RecruitBoardController.GameDisplay(Str(d, "game"), Str(d, "game_custom"));
                rows.Add((date, slot, new Item
                {
                    Id = doc.Id,
                    Mine = !string.IsNullOrEmpty(myUid) && uid == myUid,
                    Joined = meIn,
                    When = $"{RecruitBoardController.DateLabel(date)} {RecruitBoardController.SlotText(slot)}～",
                    Game = game,
                    IsTitle = game.StartsWith("『"),
                    Tags = string.Join("・", tags),
                    Host = Str(d, "name"),
                    HostFriend = SpaceFriends.IsFriend(uid),
                    Capacity = 1 + seats,
                    Filled = Mathf.Min(1 + joined, 1 + seats),
                    Left = Mathf.Max(0, seats - joined),
                    Status = Str(d, "status") == "closed" ? State.Closed : joined >= seats ? State.Full : State.Open,
                    Members = memberList,
                }));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[News] 募集の読み込みに失敗: {doc.Id} {ex.Message}");
            }
        }

        rows.Sort((a, b) =>
        {
            int c = string.CompareOrdinal(a.date, b.date);
            return c != 0 ? c : a.slot.CompareTo(b.slot);
        });
        return rows.ConvertAll(r => r.item);
    }

    private static string Str(Dictionary<string, object> d, string k)
        => d.TryGetValue(k, out var v) && v is string s ? s : "";

    private static int Int(Dictionary<string, object> d, string k)
        => d.TryGetValue(k, out var v) && v != null ? Convert.ToInt32(v) : 0;
}

/// <summary>
/// お知らせの「New」（新ホームの左上のピンクの小さな惑星から出る吹き出し）の判定。
/// 最後にお知らせページを開いたときに見た内容（端末に保存）と今の内容をくらべ、まだ見ていないものがあれば New:
///  ・お店からのお知らせ … master/config.event_images に、まだ見ていない画像がある（起動ごとに1回読む設定なので読み取りは増えない）
///  ・相席募集 … 購読している募集に、まだ見ていない募集がある（自分の募集は数えない）
/// 消えた・締め切った・参加者が増えた だけでは New にしない。お知らせページを開くと、表示した内容を「見た」にする。
/// </summary>
public static class SpaceNewsBadge
{
    private const string KEY_IMAGES = "news_seen_images";
    private const string KEY_RECRUITS = "news_seen_recruits";

    public static bool HasNewImages(List<string> urls)
    {
        if (urls == null) return false;
        var seen = Load(KEY_IMAGES);
        foreach (var u in urls)
            if (!string.IsNullOrEmpty(u) && !seen.Contains(u)) return true;
        return false;
    }

    public static bool HasNewRecruits(List<SpaceRecruitDigest.Item> items)
    {
        if (items == null) return false;
        var seen = Load(KEY_RECRUITS);
        foreach (var it in items)
            if (!it.Mine && !seen.Contains(it.Id)) return true;
        return false;
    }

    /// <summary>お知らせページで見た画像の一覧を覚える（今の一覧で置き換える）</summary>
    public static void MarkImagesSeen(List<string> urls)
    {
        if (urls == null) return;
        Save(KEY_IMAGES, urls.Where(u => !string.IsNullOrEmpty(u)));
    }

    /// <summary>お知らせページで見た募集を覚える（今出ている募集で置き換えるので、消えた募集は自然に忘れる）</summary>
    public static void MarkRecruitsSeen(List<SpaceRecruitDigest.Item> items)
    {
        if (items == null) return;
        Save(KEY_RECRUITS, items.Select(i => i.Id));
    }

    private static HashSet<string> Load(string key) =>
        new HashSet<string>(PlayerPrefs.GetString(key, "").Split('\n').Where(s => s.Length > 0));

    private static void Save(string key, IEnumerable<string> values)
    {
        PlayerPrefs.SetString(key, string.Join("\n", values));
        PlayerPrefs.Save();
    }
}

/// <summary>
/// 拡大表示の画像: ピンチ・マウスホイール・ダブルタップで拡大（1〜4倍）、拡大中はドラッグで移動。
/// 拡大した画像が枠（area）から離れすぎないよう、はみ出した分だけ動かせる。
/// </summary>
public class SpaceZoomImage : MonoBehaviour, IDragHandler, IPointerClickHandler
{
    private const float MAX_SCALE = 4f, DOUBLE_TAP_SCALE = 2.5f;

    private RectTransform _rt, _area;
    private float _scale = 1f, _pinchFrom, _scaleFrom;
    private bool _pinching;

    public void Setup(RectTransform area)
    {
        _rt = (RectTransform)transform;
        _area = area;
    }

    private void Update()
    {
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.touchCount >= 2)
        {
            float d = Vector2.Distance(Input.GetTouch(0).position, Input.GetTouch(1).position);
            if (!_pinching)
            {
                _pinching = true;
                _pinchFrom = Mathf.Max(1f, d);
                _scaleFrom = _scale;
            }
            else SetScale(_scaleFrom * d / _pinchFrom);
        }
        else _pinching = false;

        float wheel = Input.mouseScrollDelta.y;
        if (Mathf.Abs(wheel) > 0.01f) SetScale(_scale * (1f + wheel * 0.1f));
#endif
    }

    public void OnDrag(PointerEventData e)
    {
        if (_pinching || _scale <= 1.001f) return;
        var canvas = GetComponentInParent<Canvas>();
        float k = canvas != null ? canvas.rootCanvas.scaleFactor : 1f;
        _rt.anchoredPosition += e.delta / Mathf.Max(0.01f, k);
        Clamp();
    }

    public void OnPointerClick(PointerEventData e)
    {
        if (e.clickCount == 2) SetScale(_scale > 1.01f ? 1f : DOUBLE_TAP_SCALE);
    }

    private void SetScale(float s)
    {
        _scale = Mathf.Clamp(s, 1f, MAX_SCALE);
        _rt.localScale = new Vector3(_scale, _scale, 1f);
        Clamp();
    }

    private void Clamp()
    {
        var size = _rt.rect.size * _scale;
        var area = _area.rect.size;
        float mx = Mathf.Max(0f, (size.x - area.x) * 0.5f);
        float my = Mathf.Max(0f, (size.y - area.y) * 0.5f);
        var p = _rt.anchoredPosition;
        _rt.anchoredPosition = new Vector2(Mathf.Clamp(p.x, -mx, mx), Mathf.Clamp(p.y, -my, my));
    }
}
