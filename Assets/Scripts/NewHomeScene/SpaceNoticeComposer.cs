using System;
using System.Collections;
using System.Text;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 管理者用: お知らせ通知を送る画面（2026-11-10 ユーザー指定。新ホームで管理者アカウントにだけ出る、鈴の絵のボタンから開く全画面）。
/// タイトル・本文・送り先（テスト＝話題 "staff"／全員＝"all"）を入れて「送る」→ 確認 → NoticeFunction（Cloud Functions の sendNotice）。
/// 送る権限はサーバー側にあり、サーバーが管理者か確かめてから送る（アプリに鍵は入れない）。
/// 文言・文字数の上限・確認の出し方は、パソコンの送信画面（NoticeSenderController）と同じ。
///
/// ・タイトル・本文の下に定型文（NoticeTemplates）。タイトルは置き換え、本文は最後に足して「」の中にカーソルを置く
/// ・入力欄を押したときは全体を選ばず、文の最後から続けて打てる（NoticeInputCaret）
/// ・テストを送ったあとは文面を残す（そのまま「全員」に切り替えて送れる）
/// ・全員へ送ったら文面を消して送り先をテストに戻す（うっかり二重に送らないように。サーバーも10分以内の同じ内容は止める）
/// ・閉じても、アプリを終えるまでは書きかけの文面を覚えておく
/// ・絵文字はこのフォントで出せないので、入力のときに受け付けない
/// </summary>
public class SpaceNoticeComposer : MonoBehaviour
{
    private const string T_CAPTION = "ADMIN ONLY";
    private const string T_TITLE = "お知らせ通知を送る";
    private const string T_SUB = "アプリを入れている人のスマホに通知が届きます";
    private const string T_FIELD_TITLE = "タイトル";
    private const string T_FIELD_BODY = "本文";
    private const string T_PH_TITLE = "例: 新しいゲームが入りました";
    private const string T_PH_BODY = "例: 今週末から『〇〇』が遊べます。ぜひお試しください";
    private const string T_TARGET = "送り先";
    private const string T_TEST = "テスト（スタッフのスマホだけ）";
    private const string T_ALL = "全員";
    private const string T_PREVIEW = "スマホでの見え方";
    private const string T_APP = "INTALES Cafe・今";
    private const string T_SEND = "送る";
    private const string T_SENDING = "送っています…";
    private const string T_NEED_TITLE = "タイトルを入れてください";
    private const string T_CONFIRM_ALL = "全員に送りますか？";
    private const string T_CONFIRM_ALL_BODY = "アプリを入れているお客様全員に届きます\n送ったあとは取り消せません";
    private const string T_CONFIRM_TEST = "テストを送りますか？";
    private const string T_CONFIRM_TEST_BODY = "管理者アカウントでホームを開いたスマホだけに届きます";
    private const string T_SENT_TEST = "テストを送りました\nスタッフのスマホに届くか確かめてください";
    private const string T_SENT_ALL = "全員に送りました";
    private const string T_NO_EMOJI = "絵文字は使えません";
    private const string T_COUNT_SEP = " / ";

    /// <summary>この画面で表示しうる文字（入力された文字は1文字ずつ増えるので登録しない）</summary>
    public static string GlyphText => new StringBuilder()
        .Append(T_CAPTION).Append(T_TITLE).Append(T_SUB).Append(T_FIELD_TITLE).Append(T_FIELD_BODY)
        .Append(T_PH_TITLE).Append(T_PH_BODY).Append(T_TARGET).Append(T_TEST).Append(T_ALL).Append(T_PREVIEW)
        .Append(T_APP).Append(T_SEND).Append(T_SENDING).Append(T_NEED_TITLE).Append(T_CONFIRM_ALL)
        .Append(T_CONFIRM_ALL_BODY).Append(T_CONFIRM_TEST).Append(T_CONFIRM_TEST_BODY).Append(T_SENT_TEST)
        .Append(T_SENT_ALL).Append(T_NO_EMOJI).Append(T_COUNT_SEP).Append(NoticeFunction.GlyphText)
        .Append(NoticeTemplates.GlyphText)
        .Append("キャンセル0123456789")
        .ToString();

    private static readonly Vector2 TOP = new Vector2(0.5f, 1f);
    private const float W = 960f;
    private const float LEFT = -W / 2f;

    /// <summary>書きかけの文面（閉じて開き直しても残す。全員へ送ったら消す）</summary>
    private static string s_draftTitle = "", s_draftBody = "";

    private Canvas _canvas;
    private CanvasGroup _group;
    private RectTransform _safe;
    private TMP_InputField _title, _body;
    private NoticeInputCaret _bodyCaret;
    private TextMeshProUGUI _titleCount, _bodyCount, _previewTitle, _previewBody;
    private Image _testFace, _allFace;
    private TextMeshProUGUI _testText, _allText;
    private bool _toAll, _busy, _closing;
    private float _emojiToastUntil;
    private Action _onClosed;

    public static SpaceNoticeComposer Open(Canvas canvas, Action onClosed)
    {
        var root = SpaceTheme.Rect("__NoticeComposer", canvas.transform);
        SpaceTheme.Stretch(root);
        root.SetAsLastSibling();
        SpaceTheme.SubCanvas(root.gameObject, true);
        var c = root.gameObject.AddComponent<SpaceNoticeComposer>();
        c._canvas = canvas;
        c._onClosed = onClosed;
        c.Build();
        return c;
    }

    /// <summary>戻る操作（送っている間は閉じない）</summary>
    public void HandleBack()
    {
        if (!_busy) Close();
    }

    public void Close()
    {
        if (_closing) return;
        _closing = true;
        SaveDraft();
        StartCoroutine(FadeOutAndDestroy());
    }

    private void Build()
    {
        var rt = (RectTransform)transform;
        _group = gameObject.AddComponent<CanvasGroup>();
        _group.alpha = 0f;

        var dim = SpaceTheme.Rect("Dim", rt);
        SpaceTheme.Stretch(dim);
        SpaceTheme.Img(dim, null, new Color(0.01f, 0.015f, 0.05f, 0.96f), true);

        _safe = SpaceTheme.Rect("Safe", rt);
        SpaceTheme.Stretch(_safe);
        _safe.gameObject.AddComponent<SpaceSafeArea>();

        var caption = SpaceTheme.Text(_safe, "Caption", T_CAPTION, 24, SpaceTheme.A(SpaceTheme.GOLD, 0.9f), TOP,
            new Vector2(0f, -70f), new Vector2(700f, 36f), FontStyles.Bold);
        SpaceTheme.Tracking(caption, 18f);
        SpaceTheme.Text(_safe, "Title", T_TITLE, 44, SpaceTheme.TEXT, TOP, new Vector2(0f, -124f),
            new Vector2(800f, 60f), FontStyles.Bold);
        SpaceTheme.Text(_safe, "Sub", T_SUB, 26, SpaceTheme.TEXT_SUB, TOP, new Vector2(0f, -184f),
            new Vector2(W, 40f), FontStyles.Bold);
        CloseButton(_safe);

        // 入力（タイトル・本文。右上に文字数、下に定型文）。y は上から順に並べるための位置（見出しの中心）
        const float TITLE_H = 112f, BODY_H = 280f, CHIP_FONT = 22f, CHIP_H = 60f, CHIP_GAP = 12f;
        float y = -252f;
        _title = Field(T_FIELD_TITLE, T_PH_TITLE, y, TITLE_H, NoticeFunction.TITLE_MAX, false, out _titleCount);
        y = NoticeTemplates.BuildChips(_safe, TOP, new Vector2(LEFT, y - 30f - TITLE_H - 14f), W,
            NoticeTemplates.Titles, CHIP_FONT, CHIP_H, CHIP_GAP, OnTitleTemplate) - 44f;
        _body = Field(T_FIELD_BODY, T_PH_BODY, y, BODY_H, NoticeFunction.BODY_MAX, true, out _bodyCount);
        y = NoticeTemplates.BuildChips(_safe, TOP, new Vector2(LEFT, y - 30f - BODY_H - 14f), W,
            NoticeTemplates.Bodies, CHIP_FONT, CHIP_H, CHIP_GAP, OnBodyTemplate) - 44f;
        NoticeInputCaret.Attach(_title);
        _bodyCaret = NoticeInputCaret.Attach(_body);
        _title.text = s_draftTitle;
        _body.text = s_draftBody;
        _title.onValueChanged.AddListener(_ => Refresh());
        _body.onValueChanged.AddListener(_ => Refresh());

        // 送り先
        const float PILL_H = 104f;
        Label(T_TARGET, y);
        (_testFace, _testText) = Pill(T_TEST, 0f, 560f, y - 30f, PILL_H, () => SetTarget(false));
        (_allFace, _allText) = Pill(T_ALL, 580f, 380f, y - 30f, PILL_H, () => SetTarget(true));
        y -= 30f + PILL_H + 44f;

        // スマホでの見え方
        Label(T_PREVIEW, y);
        y = BuildPreview(y - 30f);

        // 送る
        var send = SpaceTheme.Rect("Send", _safe, TOP, new Vector2(0f, y - 40f), new Vector2(W, 128f), TOP);
        var face = SpaceTheme.PrimaryFace(send);
        SpaceTheme.Clickable(send.gameObject, face, OnSend);
        var label = SpaceTheme.Text(send, "Label", T_SEND, 40, SpaceTheme.TEXT_DARK, FontStyles.Bold);
        SpaceTheme.Stretch(label.rectTransform);

        Refresh();
        RefreshTarget();
        StartCoroutine(SpaceTheme.Tween(0.25f, 0f, k => _group.alpha = k));
    }

    // ================================================================
    // 表示の更新
    // ================================================================
    private void Refresh()
    {
        string t = (_title.text ?? "").Trim(), b = (_body.text ?? "").Trim();
        _previewTitle.text = t.Length == 0 ? T_PH_TITLE : SpaceTheme.NoParse(t);
        _previewTitle.alpha = t.Length == 0 ? 0.4f : 1f;
        _previewBody.text = SpaceTheme.NoParse(b);
        SetCount(_titleCount, (_title.text ?? "").Length, NoticeFunction.TITLE_MAX);
        SetCount(_bodyCount, (_body.text ?? "").Length, NoticeFunction.BODY_MAX);
    }

    private static void SetCount(TextMeshProUGUI t, int n, int max)
    {
        t.text = n + T_COUNT_SEP + max;
        t.color = n >= max ? SpaceTheme.GOLD : SpaceTheme.TEXT_MUTED;
    }

    /// <summary>タイトルの定型文: タイトルを置き換える</summary>
    private void OnTitleTemplate(string template)
    {
        if (_busy) return;
        _title.text = template;
    }

    /// <summary>本文の定型文: 本文の最後に足し、「」の中にカーソルを置いて入力をはじめる（キーボードが出る）</summary>
    private void OnBodyTemplate(string template)
    {
        if (_busy) return;
        var r = NoticeTemplates.AppendBody(_body.text, template, NoticeFunction.BODY_MAX);
        if (r == null)
        {
            SpaceTheme.Toast(NoticeTemplates.T_BODY_FULL, _canvas);
            return;
        }
        _body.text = r.Value.text;
        _bodyCaret.FocusAt(r.Value.caret);
    }

    private void SetTarget(bool all)
    {
        _toAll = all;
        RefreshTarget();
    }

    /// <summary>選んでいる送り先を色で示す（テストは緑、全員は取り消せないので警告色）</summary>
    private void RefreshTarget()
    {
        void Set(Image f, TextMeshProUGUI tx, bool on, Color c)
        {
            f.color = on ? SpaceTheme.A(c, 0.22f) : new Color(0.07f, 0.09f, 0.24f, 0.85f);
            tx.color = on ? c : SpaceTheme.TEXT_SUB;
        }
        Set(_testFace, _testText, !_toAll, SpaceTheme.MINT);
        Set(_allFace, _allText, _toAll, SpaceTheme.DANGER);
    }

    // ================================================================
    // 送る
    // ================================================================
    private void OnSend()
    {
        if (_busy || _closing) return;
        string title = (_title.text ?? "").Trim(), body = (_body.text ?? "").Trim();
        if (title.Length == 0)
        {
            SpaceTheme.Toast(T_NEED_TITLE, _canvas);
            return;
        }
        bool all = _toAll;
        SpaceDialog.Confirm(_canvas, all ? T_CONFIRM_ALL : T_CONFIRM_TEST, all ? T_CONFIRM_ALL_BODY : T_CONFIRM_TEST_BODY,
            T_SEND, all, () => SendAsync(title, body, all).Forget());
    }

    private async UniTaskVoid SendAsync(string title, string body, bool all)
    {
        if (_busy) return;
        _busy = true;
        var busy = SpaceDialog.Busy(_canvas, T_SENDING);
        var (ok, message) = await NoticeFunction.SendAsync(title, body,
            all ? AppNotifications.TOPIC_ALL : AppNotifications.TOPIC_STAFF);
        if (busy != null) Destroy(busy);
        _busy = false;
        SpaceTheme.Toast(ok ? (all ? T_SENT_ALL : T_SENT_TEST) : message, _canvas);
        if (this == null || !ok || !all) return;
        _title.text = "";
        _body.text = "";
        SetTarget(false);
        SaveDraft();
    }

    private void SaveDraft()
    {
        if (_title != null) s_draftTitle = _title.text ?? "";
        if (_body != null) s_draftBody = _body.text ?? "";
    }

    // ================================================================
    // 部品
    // ================================================================
    private TextMeshProUGUI Label(string text, float y) =>
        SpaceTheme.Text(_safe, "Label_" + text, text, 28, SpaceTheme.TEXT_SUB, TOP, new Vector2(LEFT + 12f, y),
            new Vector2(600f, 40f), FontStyles.Bold, TextAlignmentOptions.Left, new Vector2(0f, 0.5f));

    /// <summary>見出し（左）・文字数（右）・入力欄（その下）。複数行の欄は改行できる</summary>
    private TMP_InputField Field(string label, string placeholder, float y, float h, int limit, bool multiLine,
        out TextMeshProUGUI counter)
    {
        Label(label, y);
        counter = SpaceTheme.Text(_safe, "Count_" + label, "", 24, SpaceTheme.TEXT_MUTED, TOP, new Vector2(-LEFT - 12f, y),
            new Vector2(240f, 40f), FontStyles.Bold, TextAlignmentOptions.Right, new Vector2(1f, 0.5f));

        var box = SpaceTheme.Rect("Field_" + label, _safe, TOP, new Vector2(0f, y - 30f), new Vector2(W, h), TOP);
        var face = SpaceTheme.Rounded(box, new Color(0.04f, 0.06f, 0.17f, 0.95f), 28f, true);
        var edgeRt = SpaceTheme.Rect("Edge", box);
        SpaceTheme.Stretch(edgeRt);
        var edge = SpaceTheme.RoundedStroke(edgeRt, SpaceTheme.STROKE, 28f);
        var area = SpaceTheme.Rect("TextArea", box);
        SpaceTheme.Stretch(area, 28f, 18f, 28f, 18f);
        area.gameObject.AddComponent<RectMask2D>();
        var align = multiLine ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.Left;
        var ph = SpaceTheme.Text(area, "Placeholder", placeholder, 32, SpaceTheme.TEXT_MUTED, FontStyles.Normal, align);
        SpaceTheme.Stretch(ph.rectTransform);
        var text = SpaceTheme.Text(area, "Text", "", 32, SpaceTheme.TEXT, FontStyles.Normal, align);
        SpaceTheme.Stretch(text.rectTransform);
        if (multiLine)
        {
            ph.textWrappingMode = TextWrappingModes.Normal;
            text.textWrappingMode = TextWrappingModes.Normal;
        }

        var input = box.gameObject.AddComponent<TMP_InputField>();
        input.textViewport = area;
        input.textComponent = text;
        input.placeholder = ph;
        input.fontAsset = SpaceTheme.Font;
        input.pointSize = 32;
        input.richText = false;
        input.customCaretColor = true;
        input.caretColor = SpaceTheme.CYAN;
        input.caretWidth = 3;
        input.selectionColor = SpaceTheme.A(SpaceTheme.CYAN, 0.3f);
        input.lineType = multiLine ? TMP_InputField.LineType.MultiLineNewline : TMP_InputField.LineType.SingleLine;
        input.characterLimit = limit;
        input.targetGraphic = face;
        input.onValidateInput = RejectEmoji;
        input.onSelect.AddListener(_ => edge.color = SpaceTheme.A(SpaceTheme.CYAN, 0.85f));
        input.onDeselect.AddListener(_ => edge.color = SpaceTheme.STROKE);
        return input;
    }

    /// <summary>絵文字（サロゲートペア・異体字セレクタ・ゼロ幅接合子・囲みキーキャップ）は入れない</summary>
    private char RejectEmoji(string text, int index, char ch)
    {
        if (!char.IsSurrogate(ch) && ch != '️' && ch != '‍' && ch != '⃣') return ch;
        if (Time.unscaledTime >= _emojiToastUntil)
        {
            _emojiToastUntil = Time.unscaledTime + 2f;
            SpaceTheme.Toast(T_NO_EMOJI, _canvas);
        }
        return '\0';
    }

    private (Image face, TextMeshProUGUI text) Pill(string label, float x, float w, float y, float h,
        UnityEngine.Events.UnityAction onClick)
    {
        var rt = SpaceTheme.Rect("Pill_" + label, _safe, TOP, new Vector2(LEFT + x, y), new Vector2(w, h),
            new Vector2(0f, 1f));
        var face = SpaceTheme.Rounded(rt, new Color(0.07f, 0.09f, 0.24f, 0.85f), h * 0.5f, true);
        SpaceTheme.Clickable(rt.gameObject, face, onClick);
        var t = SpaceTheme.Text(rt, "Label", label, 30, SpaceTheme.TEXT_SUB, FontStyles.Bold);
        SpaceTheme.Stretch(t.rectTransform, 20f, 0f, 20f, 0f);
        return (face, t);
    }

    /// <summary>スマホの通知の見え方（白いカード。アプリ名・タイトル・本文）。戻り値はカードの下端の y</summary>
    private float BuildPreview(float y)
    {
        const float H = 240f;
        var card = SpaceTheme.Rect("Preview", _safe, TOP, new Vector2(0f, y), new Vector2(W, H), TOP);
        SpaceTheme.Rounded(card, new Color(0.93f, 0.94f, 0.97f), 36f);
        var icon = SpaceTheme.Rect("Icon", card, new Vector2(0f, 1f), new Vector2(50f, -48f), new Vector2(40f, 40f));
        SpaceTheme.Img(icon, SpaceSprites.Circle, SpaceTheme.Hex(0x3F5BD9));
        SpaceTheme.Text(card, "App", T_APP, 24, new Color(0.35f, 0.37f, 0.45f), new Vector2(0f, 1f),
            new Vector2(86f, -48f), new Vector2(W - 126f, 34f), FontStyles.Normal, TextAlignmentOptions.Left,
            new Vector2(0f, 0.5f));
        _previewTitle = SpaceTheme.Text(card, "PTitle", "", 32, new Color(0.08f, 0.09f, 0.14f), new Vector2(0f, 1f),
            new Vector2(40f, -98f), new Vector2(W - 80f, 46f), FontStyles.Bold, TextAlignmentOptions.Left,
            new Vector2(0f, 0.5f));
        _previewTitle.overflowMode = TextOverflowModes.Ellipsis;
        _previewBody = SpaceTheme.Text(card, "PBody", "", 28, new Color(0.25f, 0.27f, 0.33f), new Vector2(0f, 1f),
            new Vector2(40f, -132f), new Vector2(W - 80f, 92f), FontStyles.Normal, TextAlignmentOptions.TopLeft,
            new Vector2(0f, 1f));
        _previewBody.textWrappingMode = TextWrappingModes.Normal;
        _previewBody.overflowMode = TextOverflowModes.Ellipsis;
        return y - H;
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
        SpaceTheme.Clickable(rt.gameObject, face, Close);
    }

    private IEnumerator FadeOutAndDestroy()
    {
        float from = _group.alpha;
        yield return SpaceTheme.Tween(0.25f, 0f, k => _group.alpha = from * (1f - k));
        Destroy(gameObject);
    }

    private void OnDestroy()
    {
        var cb = _onClosed;
        _onClosed = null;
        cb?.Invoke();
    }
}
