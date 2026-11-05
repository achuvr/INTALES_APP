using System;
using System.Collections;
using System.Text;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Startシーンの画面（新ホームと同じ宇宙デザイン。コードで組み立てる。2026-11-05 に旧デザインから作り直した）。
///  ・ログイン情報が端末にあるとき … 回る惑星と「INTALES Cafe」の下に「ログインしています…」。
///                                   自動ログインは CheckUserSaveData が行い、できなかったら OnAutoLoginFailed でログインの入力に切り替える
///  ・ないとき                     … 「アカウント作成」「ログイン」を切り替えられる入力のカード（最初はアカウント作成。旧画面と同じ）
/// ログイン・アカウント作成の処理は FirebaseAuth（LoginWithAsync / RegisterWithAsync）。成功したら新ホームへ移る。
/// アカウント作成のあとのキャラクター作成は省いた（新ホームではキャラクターを使わないため。2026-11-05 ユーザー指定。
/// 旧画面の「あとで作る」と同じく、キャラクターチケットを1枚付けて始める）。
/// シーンに置いてある旧デザインの画面（Canvas）は、作るときに隠す（シーンには残してある）。
/// </summary>
public class SpaceStartScreen : MonoBehaviour
{
    public enum Mode { Register, Login }

    private const string T_WORDMARK = "INTALES Cafe";
    private const string T_TAGLINE = "BOARD GAME CAFE";
    private const string T_TAB_REGISTER = "アカウント作成";
    private const string T_TAB_LOGIN = "ログイン";
    private const string T_MAIL = "メールアドレス";
    private const string T_PASSWORD = "パスワード";
    private const string T_NICKNAME = "ニックネーム";
    private const string T_PH_MAIL = "メールアドレスを入力";
    private const string T_PH_NEW_PASSWORD = "6文字以上";
    private const string T_PH_PASSWORD = "パスワードを入力";
    private const string T_PH_NICKNAME = "ニックネームを入力";
    private const string T_SHOW = "表示";
    private const string T_HIDE = "隠す";
    private const string T_SUBMIT_REGISTER = "アカウントを作成";
    private const string T_SUBMIT_LOGIN = "ログイン";
    private const string T_LOGGING_IN = "ログインしています…";
    private const string T_CREATING = "アカウントを作成しています…";
    private const string T_FAILED = "通信に失敗しました\nもう一度お試しください";
    /// <summary>自動ログインできなかったときの知らせ（CheckUserSaveData からも使う）</summary>
    public const string T_AUTO_FAILED = "自動ログインできませんでした\nもう一度ログインしてください";
    private const int NAME_MAX = 12; // 新ホームの名前の変更と同じ上限

    /// <summary>この画面で表示しうる文字（入力されうる英数字・記号も先に登録しておく）</summary>
    private static string GlyphText
    {
        get
        {
            var sb = new StringBuilder();
            sb.Append(T_WORDMARK).Append(T_TAGLINE).Append(T_TAB_REGISTER).Append(T_TAB_LOGIN).Append(T_MAIL)
              .Append(T_PASSWORD).Append(T_NICKNAME).Append(T_PH_MAIL).Append(T_PH_NEW_PASSWORD).Append(T_PH_PASSWORD)
              .Append(T_PH_NICKNAME).Append(T_SHOW).Append(T_HIDE).Append(T_SUBMIT_REGISTER).Append(T_SUBMIT_LOGIN)
              .Append(T_LOGGING_IN).Append(T_CREATING).Append(T_FAILED).Append(T_AUTO_FAILED);
            for (char c = ' '; c <= '~'; c++) sb.Append(c);
            return sb.ToString();
        }
    }

    // 配置（セーフエリアの上端・下端からの距離）
    // 見出しは背景の左の小さな惑星（セーフエリアの上端から 760）より下に置き、同じ高さに並ばないようにする
    private const float HERO_Y = 380f;          // 惑星の中心
    private const float PLANET_FRAME = 500f;    // 惑星の枠（球の直径はその 0.8 倍）
    private const float ORBIT = 600f;           // 衛星の円の直径
    private const float WORDMARK_Y = 822f, TAGLINE_Y = 884f;
    private const float AUTO_Y = 1070f;         // 自動ログイン中の表示
    private const float CARD_W = 960f, CARD_BOTTOM = 48f, CARD_PAD = 40f;
    private const float TAB_H = 88f, TAB_GAP = 16f, TABS_GAP = 30f;
    private const float LABEL_H = 40f, LABEL_GAP = 10f, FIELD_H = 100f, FIELD_GAP = 24f, SUBMIT_H = 120f;
    private static readonly Color FIELD_FILL = new Color(0.04f, 0.06f, 0.17f, 0.92f);
    private static readonly Color PILL_FILL = new Color(0.07f, 0.09f, 0.24f, 0.85f);

    private sealed class Tab
    {
        public Image Face, Edge;
        public TextMeshProUGUI Label;
    }

    private sealed class Field
    {
        public TMP_InputField Input;
        public Image Edge;
    }

    private Canvas _canvas;
    private FirebaseAuth _auth;
    private bool _autoLogin, _built, _busy;
    private bool _pendingAutoFailed; // 画面を組み立てる前に自動ログインに失敗した
    private Mode _mode = Mode.Register;
    private RectTransform _safe, _hero, _orbit, _card, _cardBody, _autoBox;
    private CanvasGroup _heroGroup, _wordGroup, _cardGroup, _autoGroup;
    private Image _heroGlow;
    private Color _glowColor;
    private readonly Tab[] _tabs = new Tab[2];
    private Field _mail, _password, _nickname;
    private TextMeshProUGUI _submitLabel;
    private GameObject _busyDialog;
    private float _cardLift;   // キーボードを避けるために持ち上げている量
    private string _keepMail = "", _keepPassword = "", _keepName = ""; // 切り替えても入力を残す

    // ================================================================
    // 作る
    // ================================================================
    /// <summary>
    /// Startシーンに宇宙デザインの画面を作る（CheckUserSaveData.Start から）。
    /// autoLogin=true なら「ログインしています…」で出す（端末にログイン情報がある）
    /// </summary>
    public static SpaceStartScreen Create(FirebaseAuth auth, bool autoLogin)
    {
        // 旧デザインの画面を隠す。名前も変えて、宇宙デザインの部品が探す「Canvas」と取り違えないようにする。
        // 文字は旧画面の見出しと同じフォント（新ホームと同じ jp。似た名前の別のフォントを使うとメモリを二重に使う）
        var old = GameObject.Find("Canvas");
        if (old != null)
        {
            var title = old.transform.Find("Text_Title");
            var tmp = title != null ? title.GetComponent<TMP_Text>() : null;
            if (tmp != null && tmp.font != null) SpaceTheme.SetFont(tmp.font);
            old.name = "Canvas_Legacy";
            old.SetActive(false);
        }

        var go = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler),
            typeof(GraphicRaycaster));
        go.layer = 5;
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // 新ホームと同じ大きさの決め方（横1080を基準に、縦長の端末では縦に伸ばす）
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080f, 1920f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        scaler.matchWidthOrHeight = 0f;

        var screen = go.AddComponent<SpaceStartScreen>();
        screen._canvas = canvas;
        screen._auth = auth;
        screen._autoLogin = autoLogin;
        return screen;
    }

    /// <summary>自動ログインできなかった: ログインの入力を出して知らせる</summary>
    public void OnAutoLoginFailed()
    {
        _autoLogin = false;
        if (!_built)
        {
            _pendingAutoFailed = true;
            return;
        }
        ShowCard(Mode.Login);
        Toast(T_AUTO_FAILED);
    }

    private IEnumerator Start()
    {
        yield return null; // キャンバスの大きさが決まってから組み立てる
        var canvasRt = (RectTransform)_canvas.transform;
        var size = SpaceTheme.CanvasSize(_canvas);
        var insets = SpaceTheme.SafeInsets(size);
        SpaceBackground.Create(canvasRt, size, insets.top);

        // 惑星の表面は裏のスレッドで作るので先に始めておく（新ホームでもそのまま使われる）
        var planet = SpacePlanets.Current;
        SpacePlanets.Load(planet, false).Forget();
        SpacePlanets.Load(planet, true).Forget();

        // 表示する文字を先にフォントへ登録してから組み立てる（TMP動的フォントのOOM対策）
        yield return SpaceTheme.Prewarm(GlyphText);

        _safe = SpaceTheme.Rect("Safe", canvasRt);
        SpaceTheme.Stretch(_safe);
        _safe.gameObject.AddComponent<SpaceSafeArea>();
        BuildHero(planet);
        BuildAutoBox();
        _card = SpaceTheme.Rect("Card", _safe, new Vector2(0.5f, 0f), new Vector2(0f, CARD_BOTTOM), new Vector2(CARD_W, 400f),
            new Vector2(0.5f, 0f));
        _cardGroup = _card.gameObject.AddComponent<CanvasGroup>();
        SpaceTheme.Glass(_card, 56f, SpaceTheme.GLASS_DEEP, SpaceTheme.A(SpaceTheme.CYAN, 0.28f), true);
        _built = true;

        if (_pendingAutoFailed)
        {
            ShowCard(Mode.Login);
            Toast(T_AUTO_FAILED);
        }
        else if (_autoLogin) ShowAuto();
        else ShowCard(Mode.Register);
        StartCoroutine(Entrance());
    }

    private void OnDestroy()
    {
        if (_busyDialog != null) Destroy(_busyDialog);
    }

    // ================================================================
    // 惑星と見出し
    // ================================================================
    private void BuildHero(SpacePlanetDef planet)
    {
        _hero = SpaceTheme.Rect("Hero", _safe, new Vector2(0.5f, 1f), new Vector2(0f, -HERO_Y), new Vector2(ORBIT, ORBIT));
        _heroGroup = _hero.gameObject.AddComponent<CanvasGroup>();
        _glowColor = planet.Accent;
        var glow = SpaceTheme.Rect("Glow", _hero, SpaceTheme.CENTER, Vector2.zero, new Vector2(ORBIT * 1.45f, ORBIT * 1.45f));
        _heroGlow = SpaceTheme.Img(glow, SpaceSprites.Glow, SpaceTheme.A(_glowColor, 0.26f));
        var ring = SpaceTheme.Rect("Orbit", _hero, SpaceTheme.CENTER, Vector2.zero, new Vector2(ORBIT, ORBIT));
        SpaceTheme.Img(ring, SpaceSprites.RingThin, SpaceTheme.A(SpaceTheme.TEXT, 0.22f));

        var holder = SpaceTheme.Rect("PlanetHolder", _hero, SpaceTheme.CENTER, Vector2.zero,
            new Vector2(PLANET_FRAME, PLANET_FRAME));
        SpacePlanetView.Create(holder, planet, PLANET_FRAME, SpacePlanetView.Quality.Large, true);

        // 円の上を回る2つの衛星（水色と紫）
        _orbit = SpaceTheme.Rect("Satellites", _hero, SpaceTheme.CENTER, Vector2.zero, new Vector2(ORBIT, ORBIT));
        Satellite(_orbit, new Vector2(ORBIT * 0.5f, 0f), SpaceTheme.CYAN, 26f);
        Satellite(_orbit, new Vector2(-ORBIT * 0.5f * 0.7071f, -ORBIT * 0.5f * 0.7071f), SpaceTheme.VIOLET, 20f);

        var word = SpaceTheme.Rect("Wordmark", _safe, new Vector2(0.5f, 1f), new Vector2(0f, -WORDMARK_Y),
            new Vector2(1000f, 90f));
        _wordGroup = word.gameObject.AddComponent<CanvasGroup>();
        var t = SpaceTheme.Text(word, "Text", T_WORDMARK, 62, SpaceTheme.A(SpaceTheme.TEXT, 0.95f));
        SpaceTheme.Stretch(t.rectTransform);
        t.characterSpacing = 32f;
        t.enableAutoSizing = true;
        t.fontSizeMin = 40f;
        t.fontSizeMax = 62f;
        var tag = SpaceTheme.Text(word, "Tagline", T_TAGLINE, 24, SpaceTheme.A(SpaceTheme.CYAN, 0.9f),
            new Vector2(0.5f, 0f), new Vector2(0f, -(TAGLINE_Y - WORDMARK_Y) + 45f), new Vector2(900f, 36f), FontStyles.Bold);
        tag.characterSpacing = 22f;
    }

    private static void Satellite(RectTransform orbit, Vector2 pos, Color color, float size)
    {
        var s = SpaceTheme.Rect("Satellite", orbit, SpaceTheme.CENTER, pos, new Vector2(size * 3f, size * 3f));
        SpaceTheme.Img(s, SpaceSprites.Glow, SpaceTheme.A(color, 0.9f));
        var core = SpaceTheme.Rect("Core", s, SpaceTheme.CENTER, Vector2.zero, new Vector2(size * 0.42f, size * 0.42f));
        SpaceTheme.Img(core, SpaceSprites.Circle, Color.white);
    }

    /// <summary>自動ログイン中の「ログインしています…」（回る軌道の印つき）</summary>
    private void BuildAutoBox()
    {
        _autoBox = SpaceTheme.Rect("AutoLogin", _safe, new Vector2(0.5f, 1f), new Vector2(0f, -AUTO_Y), new Vector2(700f, 220f));
        _autoGroup = _autoBox.gameObject.AddComponent<CanvasGroup>();
        var orbit = SpaceTheme.Rect("Spinner", _autoBox, SpaceTheme.CENTER, new Vector2(0f, 40f), new Vector2(96f, 96f));
        SpaceTheme.Img(orbit, SpaceSprites.RingSmall, SpaceTheme.A(SpaceTheme.CYAN, 0.45f));
        var sat = SpaceTheme.Rect("Satellite", orbit, SpaceTheme.CENTER, new Vector2(0f, 46f), new Vector2(30f, 30f));
        SpaceTheme.Img(sat, SpaceSprites.Glow, SpaceTheme.A(SpaceTheme.CYAN, 0.95f));
        var core = SpaceTheme.Rect("Core", sat, SpaceTheme.CENTER, Vector2.zero, new Vector2(10f, 10f));
        SpaceTheme.Img(core, SpaceSprites.Circle, Color.white);
        orbit.gameObject.AddComponent<SpaceSpinner>().DegreesPerSecond = -220f;
        SpaceTheme.Text(_autoBox, "Message", T_LOGGING_IN, 32, SpaceTheme.TEXT_SUB, SpaceTheme.CENTER,
            new Vector2(0f, -56f), new Vector2(700f, 50f), FontStyles.Bold);
        _autoBox.gameObject.SetActive(false);
    }

    private void ShowAuto()
    {
        _card.gameObject.SetActive(false);
        _autoBox.gameObject.SetActive(true);
    }

    // ================================================================
    // 入力のカード
    // ================================================================
    private void ShowCard(Mode mode)
    {
        _autoBox.gameObject.SetActive(false);
        _card.gameObject.SetActive(true);
        KeepInputs();
        _mode = mode;
        if (_cardBody != null) Destroy(_cardBody.gameObject);
        _cardBody = SpaceTheme.Rect("Body", _card);
        SpaceTheme.Stretch(_cardBody);

        int fields = mode == Mode.Register ? 3 : 2;
        float h = CARD_PAD + TAB_H + TABS_GAP + fields * (LABEL_H + LABEL_GAP + FIELD_H + FIELD_GAP) + SUBMIT_H + CARD_PAD;
        _card.sizeDelta = new Vector2(CARD_W, h);

        // アカウント作成・ログインの切り替え
        float tabW = (CARD_W - CARD_PAD * 2f - TAB_GAP) / 2f;
        for (int i = 0; i < 2; i++)
        {
            var m = i == 0 ? Mode.Register : Mode.Login;
            var tab = SpaceTheme.Rect("Tab" + i, _cardBody, new Vector2(0f, 1f),
                new Vector2(CARD_PAD + i * (tabW + TAB_GAP), -CARD_PAD), new Vector2(tabW, TAB_H), new Vector2(0f, 1f));
            var face = SpaceTheme.Rounded(tab, PILL_FILL, TAB_H / 2f, true);
            var edgeRt = SpaceTheme.Rect("Edge", tab);
            SpaceTheme.Stretch(edgeRt);
            var edge = SpaceTheme.RoundedStroke(edgeRt, SpaceTheme.STROKE, TAB_H / 2f);
            var label = SpaceTheme.Text(tab, "Label", i == 0 ? T_TAB_REGISTER : T_TAB_LOGIN, 32, SpaceTheme.TEXT_SUB,
                FontStyles.Bold);
            SpaceTheme.Stretch(label.rectTransform);
            SpaceTheme.Clickable(tab.gameObject, face, () =>
            {
                if (!_busy && _mode != m) ShowCard(m);
            });
            _tabs[i] = new Tab { Face = face, Edge = edge, Label = label };
            bool on = m == mode;
            face.color = on ? SpaceTheme.A(SpaceTheme.CYAN, 0.18f) : PILL_FILL;
            edge.color = on ? SpaceTheme.A(SpaceTheme.CYAN, 0.8f) : SpaceTheme.STROKE;
            label.color = on ? SpaceTheme.CYAN : SpaceTheme.TEXT_SUB;
        }

        // 入力欄（旧画面と同じ並び: メールアドレス・パスワード・ニックネーム）
        float y = CARD_PAD + TAB_H + TABS_GAP;
        _mail = MakeField(ref y, T_MAIL, T_PH_MAIL, TMP_InputField.ContentType.EmailAddress, false, 0);
        _password = MakeField(ref y, T_PASSWORD, mode == Mode.Register ? T_PH_NEW_PASSWORD : T_PH_PASSWORD,
            TMP_InputField.ContentType.Password, true, 0);
        _nickname = mode == Mode.Register
            ? MakeField(ref y, T_NICKNAME, T_PH_NICKNAME, TMP_InputField.ContentType.Standard, false, NAME_MAX)
            : null;
        _mail.Input.text = _keepMail;
        _password.Input.text = _keepPassword;
        if (_nickname != null) _nickname.Input.text = _keepName;

        // 次へ（キーボードの「次」「完了」）: メール → パスワード → ニックネーム → 送信
        Chain(_mail, _password);
        Chain(_password, _nickname);
        var last = _nickname ?? _password;
        last.Input.onSubmit.AddListener(_ => Submit());

        var submit = SpaceTheme.Rect("Submit", _cardBody, new Vector2(0.5f, 0f), new Vector2(0f, CARD_PAD),
            new Vector2(CARD_W - CARD_PAD * 2f, SUBMIT_H), new Vector2(0.5f, 0f));
        var submitFace = SpaceTheme.PrimaryFace(submit);
        SpaceTheme.Clickable(submit.gameObject, submitFace, Submit);
        _submitLabel = SpaceTheme.Text(submit, "Label", mode == Mode.Register ? T_SUBMIT_REGISTER : T_SUBMIT_LOGIN, 40,
            SpaceTheme.TEXT_DARK, FontStyles.Bold);
        SpaceTheme.Stretch(_submitLabel.rectTransform);
    }

    private static void Chain(Field from, Field to)
    {
        if (from == null || to == null) return;
        from.Input.onSubmit.AddListener(_ =>
        {
            to.Input.Select();
            to.Input.ActivateInputField();
        });
    }

    /// <summary>見出しと入力欄。y はカードの上端からの距離（作った分だけ進める）</summary>
    private Field MakeField(ref float y, string label, string placeholder, TMP_InputField.ContentType type,
        bool revealToggle, int limit)
    {
        SpaceTheme.Text(_cardBody, "Label_" + label, label, 28, SpaceTheme.TEXT_SUB, new Vector2(0f, 1f),
            new Vector2(CARD_PAD + 12f, -(y + LABEL_H / 2f)), new Vector2(600f, LABEL_H), FontStyles.Bold,
            TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
        y += LABEL_H + LABEL_GAP;

        var box = SpaceTheme.Rect("Field_" + label, _cardBody, new Vector2(0.5f, 1f), new Vector2(0f, -y),
            new Vector2(CARD_W - CARD_PAD * 2f, FIELD_H), new Vector2(0.5f, 1f));
        var face = SpaceTheme.Rounded(box, FIELD_FILL, 30f, true);
        var edgeRt = SpaceTheme.Rect("Edge", box);
        SpaceTheme.Stretch(edgeRt);
        var edge = SpaceTheme.RoundedStroke(edgeRt, SpaceTheme.STROKE, 30f);
        y += FIELD_H + FIELD_GAP;

        var area = SpaceTheme.Rect("TextArea", box);
        SpaceTheme.Stretch(area, 30f, 8f, revealToggle ? 150f : 30f, 8f);
        area.gameObject.AddComponent<RectMask2D>();
        var ph = SpaceTheme.Text(area, "Placeholder", placeholder, 32, SpaceTheme.TEXT_MUTED, FontStyles.Normal,
            TextAlignmentOptions.Left);
        SpaceTheme.Stretch(ph.rectTransform);
        var text = SpaceTheme.Text(area, "Text", "", 34, SpaceTheme.TEXT, FontStyles.Normal, TextAlignmentOptions.Left);
        SpaceTheme.Stretch(text.rectTransform);

        var input = box.gameObject.AddComponent<TMP_InputField>();
        input.textViewport = area;
        input.textComponent = text;
        input.placeholder = ph;
        input.fontAsset = SpaceTheme.Font;
        input.pointSize = 34;
        input.customCaretColor = true;
        input.caretColor = SpaceTheme.CYAN;
        input.caretWidth = 3;
        input.selectionColor = SpaceTheme.A(SpaceTheme.CYAN, 0.3f);
        input.lineType = TMP_InputField.LineType.SingleLine;
        input.contentType = type;
        input.characterLimit = limit;
        input.targetGraphic = face;
        var field = new Field { Input = input, Edge = edge };
        input.onSelect.AddListener(_ => edge.color = SpaceTheme.A(SpaceTheme.CYAN, 0.85f));
        input.onDeselect.AddListener(_ => edge.color = SpaceTheme.STROKE);

        if (revealToggle)
        {
            // パスワードの「表示」「隠す」
            var t = SpaceTheme.Rect("Reveal", box, new Vector2(1f, 0.5f), new Vector2(-16f, 0f), new Vector2(118f, 68f),
                new Vector2(1f, 0.5f));
            var tf = SpaceTheme.Rounded(t, SpaceTheme.A(SpaceTheme.CYAN, 0.10f), 34f, true);
            var tl = SpaceTheme.Text(t, "Label", T_SHOW, 26, SpaceTheme.CYAN, FontStyles.Bold);
            SpaceTheme.Stretch(tl.rectTransform);
            SpaceTheme.Clickable(t.gameObject, tf, () =>
            {
                bool hidden = input.contentType == TMP_InputField.ContentType.Password;
                input.contentType = hidden ? TMP_InputField.ContentType.Standard : TMP_InputField.ContentType.Password;
                input.ForceLabelUpdate();
                tl.text = hidden ? T_HIDE : T_SHOW;
            });
        }
        return field;
    }

    private void KeepInputs()
    {
        if (_mail != null) _keepMail = _mail.Input.text;
        if (_password != null) _keepPassword = _password.Input.text;
        if (_nickname != null) _keepName = _nickname.Input.text;
    }

    // ================================================================
    // ログイン・アカウント作成
    // ================================================================
    private void Submit()
    {
        if (_busy || !_built || _auth == null || _mail == null) return;
        SubmitAsync().Forget();
    }

    private async UniTaskVoid SubmitAsync()
    {
        _busy = true;
        var mode = _mode;
        string mail = _mail.Input.text, pw = _password.Input.text, name = _nickname != null ? _nickname.Input.text : "";
        _busyDialog = SpaceDialog.Busy(_canvas, mode == Mode.Login ? T_LOGGING_IN : T_CREATING);
        string error;
        try
        {
            error = mode == Mode.Login
                ? await _auth.LoginWithAsync(mail, pw)
                : await _auth.RegisterWithAsync(mail, pw, name);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Start] ログイン・アカウント作成に失敗: {ex}");
            error = T_FAILED;
        }
        if (this == null) return; // 新ホームへ移った
        if (error == null) return; // 新ホームへ移るところ（処理中の表示はそのまま）
        if (_busyDialog != null) Destroy(_busyDialog);
        _busyDialog = null;
        _busy = false;
        Toast(error);
    }

    /// <summary>知らせ（入力のカードに重ならないよう、カードの少し上に出す）</summary>
    private void Toast(string message)
    {
        float bottom = _card != null && _card.gameObject.activeSelf
            ? CARD_BOTTOM + _card.sizeDelta.y + _cardLift + 24f
            : 340f;
        SpaceTheme.Toast(SpaceTheme.SafeNow(message), _canvas, bottom);
    }

    // ================================================================
    // 動き
    // ================================================================
    private IEnumerator Entrance()
    {
        _heroGroup.alpha = 0f;
        _wordGroup.alpha = 0f;
        _cardGroup.alpha = 0f;
        _autoGroup.alpha = 0f;
        var cardBase = _card.anchoredPosition;
        StartCoroutine(SpaceTheme.Tween(0.7f, 0.05f, k =>
        {
            _heroGroup.alpha = k;
            float s = Mathf.Lerp(0.86f, 1f, 1f - (1f - k) * (1f - k));
            _hero.localScale = new Vector3(s, s, 1f);
        }));
        StartCoroutine(SpaceTheme.Tween(0.6f, 0.3f, k => _wordGroup.alpha = k));
        StartCoroutine(SpaceTheme.Tween(0.5f, 0.45f, k => _autoGroup.alpha = k));
        yield return SpaceTheme.Tween(0.55f, 0.45f, k =>
        {
            _cardGroup.alpha = k;
            _card.anchoredPosition = cardBase + new Vector2(0f, -60f * (1f - k) + _cardLift);
        });
    }

    private void Update()
    {
        if (!_built) return;
        float t = Time.unscaledTime;
        _orbit.localEulerAngles = new Vector3(0f, 0f, -t * 14f);
        _heroGlow.color = SpaceTheme.A(_glowColor, 0.22f + 0.07f * Mathf.Sin(t * 1.4f));
        LiftForKeyboard();
    }

    /// <summary>キーボードが入力欄を隠すときは、カードを持ち上げる（キーボードの大きさが分かる端末だけ）</summary>
    private void LiftForKeyboard()
    {
        float target = 0f;
        bool typing = (_mail != null && _mail.Input.isFocused) || (_password != null && _password.Input.isFocused)
                      || (_nickname != null && _nickname.Input.isFocused);
        if (typing && TouchScreenKeyboard.visible)
        {
            float kb = TouchScreenKeyboard.area.height / Mathf.Max(0.01f, _canvas.scaleFactor);
            float bottomInset = SpaceTheme.SafeInsets(SpaceTheme.CanvasSize(_canvas)).bottom;
            target = Mathf.Max(0f, kb - CARD_BOTTOM - bottomInset + 24f);
        }
        if (Mathf.Abs(target - _cardLift) < 0.5f) return;
        _cardLift = Mathf.Lerp(_cardLift, target, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 14f));
        _card.anchoredPosition = new Vector2(0f, CARD_BOTTOM + _cardLift);
    }
}
