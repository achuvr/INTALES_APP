using System;
using System.Text;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// お知らせ通知を送る、パソコン専用の画面（Assets/Scenes/dev/NoticeSender.unity。2026-11-07 ユーザー指定）。
/// タイトルと本文を入れて「送る」を押すと、FcmNotice が Firebase Cloud Messaging へ直接送る。
///
/// 使い方は2通り（2026-11-09 ユーザー指定で Windows 用にビルドできるようにした。このパソコンとノートパソコンで使う）:
///   ・Unity エディタでこのシーンを開いて再生する
///   ・メニュー「Build/通知の送信画面（Windows）」で作った exe を開く（NoticeSenderBuild）
/// サービスアカウントの鍵はパソコンにだけ置く（探す場所は FcmNotice.KeyFolders）。スマホのアプリには入れない。
///
/// 送り先:
///   ・テスト … 話題 "staff"。管理者アカウントでホームを開いたスマホだけが購読している（AppNotifications）
///   ・全員   … 話題 "all"。アプリを入れてホームを開いた人全員（取り消せないので確認を出す）
/// スマホのアプリのビルド設定のシーン一覧には足さない。カメラ・キャンバス・EventSystem もここで作る
/// </summary>
public class NoticeSenderController : MonoBehaviour
{
    [Tooltip("日本語フォント（jp）。シーンから参照しておかないと Windows 用のビルドに入らず、文字が□になる")]
    [SerializeField] private TMP_FontAsset _jpFont;

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
    private const string T_APP = "INTALES Cafe";
    private const string T_SEND = "送る";
    private const string T_SENDING = "送っています…";
    private const string T_NEED_TITLE = "タイトルを入れてください";
    private const string T_CONFIRM_ALL = "全員に送りますか？";
    private const string T_CONFIRM_ALL_BODY = "アプリを入れているお客様全員に届きます\n送ったあとは取り消せません";
    private const string T_CONFIRM_TEST = "テストを送りますか？";
    private const string T_CONFIRM_TEST_BODY = "管理者アカウントでホームを開いたスマホだけに届きます";
    private const string T_LOG = "送った記録";
    private const string T_KEY = "使う鍵: ";
    private const string T_KEY_ADMIN = "（管理用・全権限）";
    private const string T_KEY_SEND_ONLY = "（通知を送る権限だけ）";
    private const string T_NO_KEY = "鍵ファイル（.json）が見つかりません。次の場所に置いてから送ってください: ";
    private const string T_NO_KEY_SHORT = "鍵ファイルが見つかりません（画面の下を見てください）";
    private const int TITLE_MAX = 40, BODY_MAX = 150;

    private Canvas _canvas;
    private TMP_InputField _title, _body;
    private TextMeshProUGUI _previewTitle, _previewBody, _log, _sendLabel, _keyNote;
    private bool _toAll, _busy;
    private Image _testFace, _allFace;
    private TextMeshProUGUI _testText, _allText;
    private readonly StringBuilder _logText = new StringBuilder();
    private FcmNotice.Key _key;

    private void Start()
    {
        Application.targetFrameRate = 30;
        var jp = _jpFont;
#if UNITY_EDITOR
        if (jp == null) jp = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Fonts/jp.asset");
#endif
        if (jp != null) SpaceTheme.SetFont(jp);
        SpaceTheme.InUse = true;
        _key = FcmNotice.FindKey();
        BuildScene();
        Build();
        RefreshPreview();
        RefreshTarget();
    }

    private void BuildScene()
    {
        if (Camera.main == null)
        {
            var cam = new GameObject("Main Camera").AddComponent<Camera>();
            cam.tag = "MainCamera";
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.02f, 0.03f, 0.09f);
        }
        if (FindFirstObjectByType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
        }
        var cgo = new GameObject("Canvas", typeof(RectTransform));
        _canvas = cgo.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = cgo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        // ウィンドウの縦横比が変わっても、左の入力と右の見え方が重ならないように（常に 1920×1080 以上の広さ）
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        cgo.AddComponent<GraphicRaycaster>();
    }

    private void Build()
    {
        var root = (RectTransform)_canvas.transform;
        var bg = SpaceTheme.Rect("Background", root);
        SpaceTheme.Stretch(bg);
        SpaceTheme.Img(bg, null, new Color(0.02f, 0.03f, 0.09f), false);

        SpaceTheme.Text(root, "Title", T_TITLE, 52, SpaceTheme.TEXT, new Vector2(0f, 1f), new Vector2(80f, -70f),
            new Vector2(1200f, 70f), FontStyles.Bold, TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
        SpaceTheme.Text(root, "Sub", T_SUB, 26, SpaceTheme.TEXT_SUB, new Vector2(0f, 1f), new Vector2(80f, -122f),
            new Vector2(1200f, 40f), FontStyles.Bold, TextAlignmentOptions.Left, new Vector2(0f, 0.5f));

        // 左: 入力
        _title = Field(root, T_FIELD_TITLE, T_PH_TITLE, new Vector2(80f, -190f), 960f, 100f, TITLE_MAX, false);
        _body = Field(root, T_FIELD_BODY, T_PH_BODY, new Vector2(80f, -360f), 960f, 240f, BODY_MAX, true);
        _title.onValueChanged.AddListener(_ => RefreshPreview());
        _body.onValueChanged.AddListener(_ => RefreshPreview());

        // 送り先
        SpaceTheme.Text(root, "TargetLabel", T_TARGET, 28, SpaceTheme.TEXT_SUB, new Vector2(0f, 1f), new Vector2(92f, -680f),
            new Vector2(400f, 40f), FontStyles.Bold, TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
        (_testFace, _testText) = Pill(root, T_TEST, new Vector2(80f, -720f), 560f, () => { _toAll = false; RefreshTarget(); });
        (_allFace, _allText) = Pill(root, T_ALL, new Vector2(660f, -720f), 380f, () => { _toAll = true; RefreshTarget(); });

        // 送る
        var send = SpaceTheme.Rect("Send", root, new Vector2(0f, 1f), new Vector2(80f, -850f), new Vector2(960f, 112f),
            new Vector2(0f, 1f));
        var face = SpaceTheme.PrimaryFace(send);
        SpaceTheme.Clickable(send.gameObject, face, OnSend);
        _sendLabel = SpaceTheme.Text(send, "Label", T_SEND, 40, SpaceTheme.TEXT_DARK, FontStyles.Bold);
        SpaceTheme.Stretch(_sendLabel.rectTransform);

        // 左下: 使う鍵（見つからなければ置き場所）
        _keyNote = SpaceTheme.Text(root, "KeyNote", "", 22, SpaceTheme.TEXT_MUTED, new Vector2(0f, 0f), new Vector2(80f, 52f),
            new Vector2(1760f, 64f), FontStyles.Normal, TextAlignmentOptions.BottomLeft, new Vector2(0f, 0.5f));
        _keyNote.textWrappingMode = TextWrappingModes.Normal;
        RefreshKeyNote();

        // 右: スマホでの見え方
        SpaceTheme.Text(root, "PreviewLabel", T_PREVIEW, 28, SpaceTheme.TEXT_SUB, new Vector2(1f, 1f), new Vector2(-760f, -206f),
            new Vector2(680f, 40f), FontStyles.Bold, TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
        var phone = SpaceTheme.Rect("Preview", root, new Vector2(1f, 1f), new Vector2(-80f, -240f), new Vector2(680f, 230f),
            new Vector2(1f, 1f));
        SpaceTheme.Rounded(phone, new Color(0.93f, 0.94f, 0.97f), 36f);
        var icon = SpaceTheme.Rect("Icon", phone, new Vector2(0f, 1f), new Vector2(44f, -44f), new Vector2(40f, 40f));
        SpaceTheme.Img(icon, SpaceSprites.Circle, SpaceTheme.Hex(0x3F5BD9));
        var app = SpaceTheme.Text(phone, "App", T_APP + "・今", 22, new Color(0.35f, 0.37f, 0.45f), new Vector2(0f, 1f),
            new Vector2(80f, -44f), new Vector2(560f, 32f), FontStyles.Normal, TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
        _previewTitle = SpaceTheme.Text(phone, "PTitle", "", 30, new Color(0.08f, 0.09f, 0.14f), new Vector2(0f, 1f),
            new Vector2(36f, -94f), new Vector2(610f, 44f), FontStyles.Bold, TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
        _previewTitle.overflowMode = TextOverflowModes.Ellipsis;
        _previewBody = SpaceTheme.Text(phone, "PBody", "", 26, new Color(0.25f, 0.27f, 0.33f), new Vector2(0f, 1f),
            new Vector2(36f, -120f), new Vector2(610f, 96f), FontStyles.Normal, TextAlignmentOptions.TopLeft, new Vector2(0f, 1f));
        _previewBody.textWrappingMode = TextWrappingModes.Normal;
        _previewBody.overflowMode = TextOverflowModes.Ellipsis;

        // 右下: 送った記録
        SpaceTheme.Text(root, "LogLabel", T_LOG, 28, SpaceTheme.TEXT_SUB, new Vector2(1f, 1f), new Vector2(-760f, -520f),
            new Vector2(680f, 40f), FontStyles.Bold, TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
        var logBox = SpaceTheme.Rect("Log", root, new Vector2(1f, 1f), new Vector2(-80f, -550f), new Vector2(680f, 400f),
            new Vector2(1f, 1f));
        SpaceTheme.Rounded(logBox, new Color(0.05f, 0.07f, 0.19f, 0.9f), 30f);
        logBox.gameObject.AddComponent<RectMask2D>();
        _log = SpaceTheme.Text(logBox, "Text", "", 22, SpaceTheme.TEXT_SUB, FontStyles.Normal, TextAlignmentOptions.TopLeft);
        SpaceTheme.Stretch(_log.rectTransform, 24f, 20f, 24f, 20f);
        _log.textWrappingMode = TextWrappingModes.Normal;
        _log.overflowMode = TextOverflowModes.Truncate;
    }

    private void RefreshPreview()
    {
        string t = _title.text.Trim(), b = _body.text.Trim();
        _previewTitle.text = string.IsNullOrEmpty(t) ? T_PH_TITLE : SpaceTheme.NoParse(t);
        _previewTitle.alpha = string.IsNullOrEmpty(t) ? 0.4f : 1f;
        _previewBody.text = SpaceTheme.NoParse(b);
    }

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

    private void RefreshKeyNote()
    {
        if (_key != null)
        {
            _keyNote.text = T_KEY + SpaceTheme.NoParse(_key.client_email) + (_key.IsAdmin ? T_KEY_ADMIN : T_KEY_SEND_ONLY);
            _keyNote.color = SpaceTheme.TEXT_MUTED;
        }
        else
        {
            _keyNote.text = T_NO_KEY + SpaceTheme.NoParse(string.Join("　または　", FcmNotice.KeyFolders()));
            _keyNote.color = SpaceTheme.DANGER;
        }
    }

    private void OnSend()
    {
        if (_busy) return;
        string title = _title.text.Trim(), body = _body.text.Trim();
        if (string.IsNullOrEmpty(title))
        {
            SpaceTheme.Toast(T_NEED_TITLE, _canvas);
            return;
        }
        if (_key == null)
        {
            _key = FcmNotice.FindKey(); // 画面を開いたあとに鍵を置いた場合
            RefreshKeyNote();
            if (_key == null)
            {
                SpaceTheme.Toast(T_NO_KEY_SHORT, _canvas);
                return;
            }
        }
        bool all = _toAll;
        SpaceDialog.Confirm(_canvas, all ? T_CONFIRM_ALL : T_CONFIRM_TEST, all ? T_CONFIRM_ALL_BODY : T_CONFIRM_TEST_BODY,
            T_SEND, all, () => SendAsync(title, body, all ? AppNotifications.TOPIC_ALL : AppNotifications.TOPIC_STAFF).Forget());
    }

    private async UniTaskVoid SendAsync(string title, string body, string topic)
    {
        _busy = true;
        _sendLabel.text = T_SENDING;
        var (ok, output) = await FcmNotice.SendAsync(_key, title, body, topic);
        _busy = false;
        if (this == null) return;
        _sendLabel.text = T_SEND;
        string who = topic == AppNotifications.TOPIC_ALL ? T_ALL : "テスト";
        string line = $"{DateTime.Now:MM/dd HH:mm}　{(ok ? "送信" : "失敗")}（{who}）　{title}";
        if (!ok) line += "\n" + output.Trim();
        _logText.Insert(0, line + "\n");
        _log.text = SpaceTheme.NoParse(_logText.ToString());
        SpaceTheme.Toast(ok ? "送りました" : "送れませんでした（記録を見てください）", _canvas);
        Debug.Log($"[NoticeSender] {line}\n{output}");
    }

    // ---------------------------------------------------------------- 部品
    private TMP_InputField Field(RectTransform parent, string label, string placeholder, Vector2 pos, float w, float h,
        int limit, bool multiLine)
    {
        SpaceTheme.Text(parent, "Label_" + label, label, 28, SpaceTheme.TEXT_SUB, new Vector2(0f, 1f),
            pos + new Vector2(12f, 0f), new Vector2(600f, 40f), FontStyles.Bold, TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
        var box = SpaceTheme.Rect("Field_" + label, parent, new Vector2(0f, 1f), pos + new Vector2(0f, -30f), new Vector2(w, h),
            new Vector2(0f, 1f));
        var face = SpaceTheme.Rounded(box, new Color(0.04f, 0.06f, 0.17f, 0.95f), 28f, true);
        var edgeRt = SpaceTheme.Rect("Edge", box);
        SpaceTheme.Stretch(edgeRt);
        var edge = SpaceTheme.RoundedStroke(edgeRt, SpaceTheme.STROKE, 28f);
        var area = SpaceTheme.Rect("TextArea", box);
        SpaceTheme.Stretch(area, 28f, 18f, 28f, 18f);
        area.gameObject.AddComponent<RectMask2D>();
        var align = multiLine ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.Left;
        var ph = SpaceTheme.Text(area, "Placeholder", placeholder, 30, SpaceTheme.TEXT_MUTED, FontStyles.Normal, align);
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
        input.customCaretColor = true;
        input.caretColor = SpaceTheme.CYAN;
        input.caretWidth = 3;
        input.selectionColor = SpaceTheme.A(SpaceTheme.CYAN, 0.3f);
        input.lineType = multiLine ? TMP_InputField.LineType.MultiLineNewline : TMP_InputField.LineType.SingleLine;
        input.characterLimit = limit;
        input.targetGraphic = face;
        input.onSelect.AddListener(_ => edge.color = SpaceTheme.A(SpaceTheme.CYAN, 0.85f));
        input.onDeselect.AddListener(_ => edge.color = SpaceTheme.STROKE);
        return input;
    }

    private static (Image face, TextMeshProUGUI text) Pill(RectTransform parent, string label, Vector2 pos, float w,
        UnityEngine.Events.UnityAction onClick)
    {
        var rt = SpaceTheme.Rect("Pill_" + label, parent, new Vector2(0f, 1f), pos, new Vector2(w, 92f), new Vector2(0f, 1f));
        var face = SpaceTheme.Rounded(rt, new Color(0.07f, 0.09f, 0.24f, 0.85f), 46f, true);
        SpaceTheme.Clickable(rt.gameObject, face, onClick);
        var t = SpaceTheme.Text(rt, "Label", label, 30, SpaceTheme.TEXT_SUB, FontStyles.Bold);
        SpaceTheme.Stretch(t.rectTransform);
        return (face, t);
    }
}
