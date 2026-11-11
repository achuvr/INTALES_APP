using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// 新ホーム右上の設定ボタン（歯車）。押すと下にボタンが順に出てくる（文字の大きさ・ユーザー名・誕生月・友だち紹介と、以下）:
///   ・INTALES WORLD        … 使いたい人だけのダウンロード（SpaceWorld）。ワーカープレイスメントの星座（職人座）を
///                            完成させるまでは錠前がかかっていて、ダウンロードできない（2026-11-11）
///   （ボードゲーム図鑑は 2026-11-11 から初めから入っている。ダウンロードのボタンは無くした）
///   ・プライバシーポリシー … 公開ページ（Firebase Hosting）をブラウザで開く
///   ・ログアウト             … 確認ダイアログ → 自動ログインを解除して Start へ
///   ・退会                   … 2段階の確認 → アカウントと全データを削除して Start へ
/// 退会は取り消せないので一番下に離して置き、警告色にしている。処理本体は旧ホームと共通の AccountSession。
/// もう一度歯車を押すか、まわりをタップすると閉じる。
/// </summary>
public class SpaceAccountMenu : MonoBehaviour
{
    private const string T_PRIVACY = "プライバシーポリシー";
    private const string T_LOGOUT = "ログアウト";
    private const string T_DELETE = "退会";
    private const string T_CANCEL = "キャンセル";
    private const string T_LOGOUT_BODY =
        "ログアウトすると、この端末の\n自動ログインが解除されます。\n\n次回はメールアドレスとパスワードで\nログインしてください。";
    private const string T_DELETE_TITLE = "退会（アカウント削除）";
    private const string T_DELETE_BODY =
        "退会するとアカウントが削除され、\nクーポン・来店記録・惑星など\nすべてのデータが完全に消えます。\n\nこの操作は元に戻せません。";
    private const string T_DELETE_NEXT = "退会をすすめる";
    private const string T_FINAL_TITLE = "最終確認";
    private const string T_FINAL_BODY = "本当に退会しますか？";
    private const string T_FINAL_OK = "退会する";
    private const string T_DELETING = "退会処理中…";
    private const string T_DELETE_FAILED = "退会に失敗しました\n通信環境を確認して再度お試しください";
    // ダウンロード・削除の共通の文字（INTALES WORLD。ボードゲーム図鑑は 2026-11-11 から初めから入っている）
    private const string T_DOWNLOAD = "ダウンロード";
    private const string T_DOWNLOADING = "ダウンロード中…";
    private const string T_DOWNLOAD_FAILED = "ダウンロードに失敗しました\n通信環境を確認して再度お試しください";
    private const string T_REMOVE = "削除する";
    // INTALES WORLD（2026-11-10）。ダウンロード前の注意はユーザー指定の文言そのまま
    private const string T_WORLD_BODY = "このコンテンツを遊ぶには、ワーカープレイスメントとマストフォローを知っている必要があります！";
    private const string T_WORLD_DONE = "ダウンロードしました\nホームの所持品の下の「INTALES WORLD」から遊べます";
    private const string T_WORLD_INSTALLED_BODY =
        "ダウンロード済みです。\nホームの所持品の下の\n「INTALES WORLD」から遊べます。\n\n削除すると、ボタンと所持品のコマが\n見えなくなり、入店してもコマを\nもらえなくなります。\n（持っているコマは残ります）";
    private const string T_WORLD_REMOVED = "INTALES WORLD を削除しました";
    // INTALES WORLD の鍵（2026-11-11 ユーザー指定）: ワーカープレイスメントの星座（職人座）を完成させるまでダウンロードできない
    private const string T_WORLD_LOCKED_A = "ワーカープレイスメントの星座\n「";
    private const string T_WORLD_LOCKED_B = "」を完成させると\n鍵が開いてダウンロードできます。\n\n";
    private const string T_WORLD_LOCKED_STARS = "の星　";
    private const string T_WORLD_LOCKED_ADMIN = "\n\n（管理者は鍵がかかっていても\nダウンロードできます）";
    private const string T_WORLD_SEE = "星座を見る";
    private const string T_BIRTH = "誕生月";
    private const string T_BIRTH_SET_A = "誕生月は ";
    private const string T_BIRTH_SET_B = "月 で登録しています\n変えたいときはスタッフにお声がけください";
    private const string T_BIRTH_BODY = "登録すると誕生月に特典があります（あとから変えられません）";
    private const string T_BIRTH_FAILED = "登録に失敗しました\n通信環境を確認して再度お試しください";
    private const string T_BIRTH_DONE = "誕生月を登録しました";
    private const string T_INVITE = "友だちを紹介";
    private const string T_INVITE_CAPTION = "INVITE";
    private const string T_INVITE_TITLE = "友だち紹介QR";
    private const string T_INVITE_NOTE = "まだ来店したことのない友だちに、アプリのホームのカメラでこのQRを読んでもらってください。その友だちが初めて来店すると、2人にドリンクチケットをプレゼント";
    private const string T_TEXT_SIZE = "文字の大きさ";
    private const string T_TEXT_SIZE_BODY = "アプリ全体の文字の大きさを変えます";
    private const string T_TEXT_SIZE_SAMPLE = "あいう Aa 123";
    private const string T_CLOSE = "閉じる";

    /// <summary>このメニューで表示しうる文字（NewHome の起動時にまとめてフォントへ登録する）</summary>
    public static string GlyphText => new StringBuilder()
        .Append(T_PRIVACY).Append(T_LOGOUT).Append(T_DELETE).Append(T_CANCEL).Append(T_LOGOUT_BODY)
        .Append(T_DELETE_TITLE).Append(T_DELETE_BODY).Append(T_DELETE_NEXT).Append(T_FINAL_TITLE)
        .Append(T_FINAL_BODY).Append(T_FINAL_OK).Append(T_DELETING).Append(T_DELETE_FAILED)
        .Append(T_DOWNLOAD).Append(T_DOWNLOADING).Append(T_DOWNLOAD_FAILED).Append(T_REMOVE)
        .Append(T_WORLD_BODY).Append(T_WORLD_DONE).Append(T_WORLD_INSTALLED_BODY)
        .Append(T_WORLD_REMOVED).Append(SpaceWorld.T_TITLE)
        .Append(T_WORLD_LOCKED_A).Append(T_WORLD_LOCKED_B).Append(T_WORLD_LOCKED_STARS).Append(T_WORLD_LOCKED_ADMIN)
        .Append(T_WORLD_SEE).Append("0123456789/")
        .Append(T_TEXT_SIZE).Append(T_TEXT_SIZE_BODY).Append(T_TEXT_SIZE_SAMPLE)
        .Append(T_CLOSE).Append(string.Concat(SpaceTheme.TEXT_SCALE_NAMES)).Append("あ")
        .Append(T_BIRTH).Append(T_BIRTH_SET_A).Append(T_BIRTH_SET_B).Append(T_BIRTH_BODY).Append(T_BIRTH_FAILED)
        .Append(T_BIRTH_DONE).Append(T_INVITE).Append(T_INVITE_CAPTION).Append(T_INVITE_TITLE).Append(T_INVITE_NOTE)
        .Append(SpaceMonthPicker.GlyphText).Append("月").ToString();

    // 配置（セーフエリアの右上基準。歯車はヘッダーの「INTALES Cafe」と同じ高さ）
    private const float TRIGGER_SIZE = 96f;
    private static readonly Vector2 TRIGGER_POS = new Vector2(-112f, -84f);
    private const float ITEM_W = 460f;
    private const float ITEM_H = 104f;
    private const float ITEM_GAP = 16f;
    private const float ITEM_RIGHT = -64f;  // ボタンの右端（歯車の右端にそろえる）
    private const float STAGGER = 0.12f;    // 順に出てくる間隔（開閉の進み具合に対する割合）

    private class Item
    {
        public RectTransform Rt;
        public CanvasGroup Group;
        public Vector2 Base;
    }

    private Canvas _canvas;
    private GameObject _dim;
    private CanvasGroup _dimGroup;
    private RectTransform _gear;
    private Image _gearImg;
    private readonly List<Item> _items = new List<Item>();
    private Coroutine _anim;
    private float _progress;   // 0=閉 1=開
    private bool _open, _busy;
    private GameObject _dialog;
    // INTALES WORLD のボタン（鍵がかかっている間は、矢印を錠前に替えて文字を暗くする）
    private GameObject _worldDownloadIcon, _worldLockIcon;
    private Image _worldBubble;
    private TextMeshProUGUI _worldLabel;

    /// <summary>メニューかダイアログが開いているか（開いている間はホームの他のボタンを押させない）</summary>
    public bool IsOpen => _open || _dialog != null || _busy;

    /// <summary>歯車ボタンの CanvasGroup（ホームの登場演出でフェードインさせる）</summary>
    public CanvasGroup TriggerGroup { get; private set; }

    public static SpaceAccountMenu Create(Canvas canvas)
    {
        var root = SpaceTheme.Rect("__AccountMenu", canvas.transform);
        SpaceTheme.Stretch(root);
        var menu = root.gameObject.AddComponent<SpaceAccountMenu>();
        menu._canvas = canvas;
        menu.Build();
        return menu;
    }

    /// <summary>戻る操作（Android の戻るボタン）。処理したら true</summary>
    public bool HandleBack()
    {
        if (_busy) return true; // 退会処理中は戻れない
        if (_dialog != null)
        {
            Destroy(_dialog);
            _dialog = null;
            return true;
        }
        if (_open)
        {
            Close();
            return true;
        }
        return false;
    }

    // ================================================================
    // 組み立て
    // ================================================================
    private void Build()
    {
        var rt = (RectTransform)transform;

        // 暗幕（開いている間だけ表示。タップで閉じる）
        var dim = SpaceTheme.Rect("Dim", rt);
        SpaceTheme.Stretch(dim);
        var dimImg = SpaceTheme.Img(dim, null, SpaceTheme.A(SpaceTheme.DIM, 0.55f), true);
        SpaceTheme.Clickable(dim.gameObject, dimImg, Close, false);
        _dimGroup = dim.gameObject.AddComponent<CanvasGroup>();
        _dim = dim.gameObject;
        _dim.SetActive(false);

        var safe = SpaceTheme.Rect("Safe", rt);
        SpaceTheme.Stretch(safe);
        safe.gameObject.AddComponent<SpaceSafeArea>();

        AddItem(safe, T_TEXT_SIZE, false, DrawTextSizeIcon, OnTextSize);
        AddItem(safe, SpaceRenameDialog.T_RENAME, false, DrawRenameIcon, OnRename);
        AddItem(safe, T_BIRTH, false, DrawBirthIcon, OnBirth);
        AddItem(safe, T_INVITE, false, DrawInviteIcon, OnInvite);
        var world = AddItem(safe, SpaceWorld.T_TITLE, false, DrawWorldIcon, OnWorld);
        _worldBubble = world.Find("Icon").GetComponent<Image>();
        _worldLabel = world.Find("Label").GetComponent<TextMeshProUGUI>();
        AddItem(safe, T_PRIVACY, false, DrawDocIcon, OnPrivacy);
        AddItem(safe, T_LOGOUT, false, DrawLogoutIcon, OnLogout);
        AddItem(safe, T_DELETE, true, DrawTrashIcon, OnDelete);

        // 歯車ボタン（ボタンより後に作って手前に描く）
        var trigger = SpaceTheme.Rect("SettingsButton", safe, new Vector2(1f, 1f), TRIGGER_POS,
            new Vector2(TRIGGER_SIZE, TRIGGER_SIZE));
        TriggerGroup = trigger.gameObject.AddComponent<CanvasGroup>();
        var face = SpaceTheme.Img(trigger, SpaceSprites.Circle, new Color(0.05f, 0.07f, 0.19f, 0.85f), true);
        var rim = SpaceTheme.Rect("Rim", trigger);
        SpaceTheme.Stretch(rim);
        SpaceTheme.Img(rim, SpaceSprites.RingSmall, SpaceTheme.STROKE);
        _gear = SpaceTheme.Rect("Gear", trigger, SpaceTheme.CENTER, Vector2.zero, new Vector2(50f, 50f));
        _gearImg = SpaceTheme.Img(_gear, SpaceSprites.Gear, SpaceTheme.TEXT);
        SpaceTheme.Clickable(trigger.gameObject, face, Toggle);
    }

    private RectTransform AddItem(RectTransform parent, string label, bool danger, Action<RectTransform, Color> drawIcon,
        UnityAction onClick)
    {
        int index = _items.Count;
        float y = TRIGGER_POS.y - TRIGGER_SIZE / 2f - 24f - ITEM_H / 2f - index * (ITEM_H + ITEM_GAP);
        var rt = SpaceTheme.Rect("Item_" + index, parent, new Vector2(1f, 1f), new Vector2(ITEM_RIGHT, y),
            new Vector2(ITEM_W, ITEM_H), new Vector2(1f, 0.5f));
        var color = danger ? SpaceTheme.DANGER : SpaceTheme.TEXT;
        var face = SpaceTheme.Glass(rt, 46f, new Color(0.05f, 0.07f, 0.19f, 0.92f),
            danger ? SpaceTheme.A(SpaceTheme.DANGER, 0.5f) : (Color?)null, true);
        var group = rt.gameObject.AddComponent<CanvasGroup>();
        SpaceTheme.Clickable(rt.gameObject, face, onClick);

        // 左のアイコン（丸い下地の上に線で描く）
        var bubble = SpaceTheme.Rect("Icon", rt, new Vector2(0f, 0.5f), new Vector2(52f, 0f), new Vector2(64f, 64f));
        SpaceTheme.Img(bubble, SpaceSprites.Circle,
            SpaceTheme.A(danger ? SpaceTheme.DANGER : SpaceTheme.CYAN, danger ? 0.16f : 0.13f));
        drawIcon(bubble, danger ? SpaceTheme.DANGER : SpaceTheme.CYAN);

        SpaceTheme.Text(rt, "Label", label, 34, color, new Vector2(0f, 0.5f), new Vector2(100f, 0f),
            new Vector2(ITEM_W - 120f, 50f), FontStyles.Bold, TextAlignmentOptions.Left, new Vector2(0f, 0.5f));

        rt.gameObject.SetActive(false);
        _items.Add(new Item { Rt = rt, Group = group, Base = rt.anchoredPosition });
        return rt;
    }

    // ---- アイコン（64px の丸の中に、細いバーで記号を描く）----
    /// <summary>ユーザー名の変更: 鉛筆（ななめの軸と先）</summary>
    private static void DrawRenameIcon(RectTransform p, Color c)
    {
        SpaceTheme.Bar(p, new Vector2(2f, 2f), 30f, 6f, 45f, c);
        SpaceTheme.Bar(p, new Vector2(-12f, -12f), 7f, 4f, 45f, c);
        SpaceTheme.Bar(p, new Vector2(0f, -17f), 24f, 3f, 0f, c);
    }

    /// <summary>誕生月: 「月」の字</summary>
    private static void DrawBirthIcon(RectTransform p, Color c)
    {
        var t = SpaceTheme.Text(p, "Moon", "月", 32, c, SpaceTheme.CENTER, new Vector2(0f, 1f), new Vector2(44f, 44f),
            FontStyles.Bold);
        SpaceTextScaler.Exclude(t);
    }

    /// <summary>友だちを紹介: 2人の人の形（フレンドのボタンと同じ絵）</summary>
    private static void DrawInviteIcon(RectTransform p, Color c)
    {
        var rt = SpaceTheme.Rect("People", p, SpaceTheme.CENTER, Vector2.zero, new Vector2(40f, 40f));
        SpaceTheme.Img(rt, SpaceSprites.FriendsIcon, c);
    }

    /// <summary>文字の大きさ: 大小2つの「あ」</summary>
    private static void DrawTextSizeIcon(RectTransform p, Color c)
    {
        var big = SpaceTheme.Text(p, "Big", "あ", 34, c, SpaceTheme.CENTER, new Vector2(6f, 1f), new Vector2(40f, 44f),
            FontStyles.Bold);
        var small = SpaceTheme.Text(p, "Small", "あ", 20, c, SpaceTheme.CENTER, new Vector2(-15f, -6f),
            new Vector2(24f, 28f), FontStyles.Bold);
        SpaceTextScaler.Exclude(big);
        SpaceTextScaler.Exclude(small);
    }

    private static void DrawDocIcon(RectTransform p, Color c)
    {
        SpaceTheme.Bar(p, new Vector2(-11f, 0f), 34f, 3f, 90f, c);   // 左辺
        SpaceTheme.Bar(p, new Vector2(11f, 0f), 34f, 3f, 90f, c);    // 右辺
        SpaceTheme.Bar(p, new Vector2(0f, 15.5f), 25f, 3f, 0f, c);   // 上辺
        SpaceTheme.Bar(p, new Vector2(0f, -15.5f), 25f, 3f, 0f, c);  // 下辺
        for (int k = -1; k <= 1; k++) SpaceTheme.Bar(p, new Vector2(0f, k * 7f), 12f, 3f, 0f, c); // 本文の行
    }

    private static void DrawDownloadIcon(RectTransform p, Color c)
    {
        SpaceTheme.Bar(p, new Vector2(0f, 5f), 22f, 3f, 90f, c);       // 矢印の軸
        SpaceTheme.Bar(p, new Vector2(-3.5f, -2.5f), 11f, 3f, -45f, c); // 矢じり（左）
        SpaceTheme.Bar(p, new Vector2(3.5f, -2.5f), 11f, 3f, 45f, c);   // 矢じり（右）
        SpaceTheme.Bar(p, new Vector2(0f, -14f), 26f, 3f, 0f, c);      // 受け皿の底
        SpaceTheme.Bar(p, new Vector2(-12f, -10f), 9f, 3f, 90f, c);    // 受け皿の左
        SpaceTheme.Bar(p, new Vector2(12f, -10f), 9f, 3f, 90f, c);     // 受け皿の右
    }

    /// <summary>INTALES WORLD: ふだんはダウンロードの矢印、鍵がかかっている間は錠前（RefreshWorldItem で切り替える）</summary>
    private void DrawWorldIcon(RectTransform p, Color c)
    {
        var download = SpaceTheme.Rect("Download", p);
        SpaceTheme.Stretch(download);
        DrawDownloadIcon(download, c);
        _worldDownloadIcon = download.gameObject;
        // 錠前の絵は下寄りなので少し上げて、丸の中央に見えるようにする
        var lockIcon = SpaceTheme.Rect("Lock", p, SpaceTheme.CENTER, new Vector2(0f, 2f), new Vector2(44f, 44f));
        SpaceTheme.Img(lockIcon, SpaceSprites.Lock, SpaceTheme.TEXT_SUB);
        _worldLockIcon = lockIcon.gameObject;
        _worldLockIcon.SetActive(false);
    }

    private static void DrawLogoutIcon(RectTransform p, Color c)
    {
        SpaceTheme.Bar(p, new Vector2(-14f, 0f), 33f, 3f, 90f, c);   // 扉の縦線
        SpaceTheme.Bar(p, new Vector2(-9f, 15f), 13f, 3f, 0f, c);    // 上
        SpaceTheme.Bar(p, new Vector2(-9f, -15f), 13f, 3f, 0f, c);   // 下
        SpaceTheme.Bar(p, new Vector2(3f, 0f), 22f, 3f, 0f, c);      // 矢印の軸
        SpaceTheme.Bar(p, new Vector2(10.5f, 3.5f), 11f, 3f, -45f, c);
        SpaceTheme.Bar(p, new Vector2(10.5f, -3.5f), 11f, 3f, 45f, c);
    }

    private static void DrawTrashIcon(RectTransform p, Color c)
    {
        SpaceTheme.Bar(p, new Vector2(0f, 10f), 30f, 3f, 0f, c);     // ふた
        SpaceTheme.Bar(p, new Vector2(0f, 15f), 11f, 3f, 0f, c);     // 取っ手
        SpaceTheme.Bar(p, new Vector2(-10f, -3f), 24f, 3f, 90f, c);  // 左
        SpaceTheme.Bar(p, new Vector2(10f, -3f), 24f, 3f, 90f, c);   // 右
        SpaceTheme.Bar(p, new Vector2(0f, -14.5f), 23f, 3f, 0f, c);  // 底
        SpaceTheme.Bar(p, new Vector2(-3.5f, -3f), 12f, 2.5f, 90f, c);
        SpaceTheme.Bar(p, new Vector2(3.5f, -3f), 12f, 2.5f, 90f, c);
    }

    // ================================================================
    // 開閉
    // ================================================================
    private void Toggle()
    {
        if (_busy) return;
        if (_open) Close();
        else Open();
    }

    private void Open()
    {
        if (_open || _busy) return;
        _open = true;
        transform.SetAsLastSibling(); // ホームの上に出す（所持品・ダイアログはこのあと開くので更に上に来る）
        RefreshWorldItem(); // 星座はホームにいる間にも灯るので、開くたびに鍵を確かめる
        _dim.SetActive(true);
        foreach (var it in _items) it.Rt.gameObject.SetActive(true);
        Animate(1f, 0.30f);
    }

    /// <summary>
    /// INTALES WORLD のボタンの見た目。鍵がかかっている間（まだダウンロードしていなくて、職人座が完成していない）は
    /// 矢印を錠前に替えて、文字と丸を暗くする
    /// </summary>
    private void RefreshWorldItem()
    {
        if (_worldLabel == null) return;
        bool locked = !SpaceWorld.Installed && !SpaceWorld.Unlocked;
        _worldDownloadIcon.SetActive(!locked);
        _worldLockIcon.SetActive(locked);
        _worldLabel.color = locked ? SpaceTheme.TEXT_SUB : SpaceTheme.TEXT;
        _worldBubble.color = locked ? SpaceTheme.A(SpaceTheme.LINE, 0.10f) : SpaceTheme.A(SpaceTheme.CYAN, 0.13f);
    }

    public void Close()
    {
        if (!_open) return;
        _open = false;
        Animate(0f, 0.16f);
    }

    private void Animate(float target, float duration)
    {
        if (_anim != null) StopCoroutine(_anim);
        _anim = StartCoroutine(AnimateRoutine(target, duration));
    }

    private IEnumerator AnimateRoutine(float target, float duration)
    {
        float from = _progress;
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            Apply(Mathf.Lerp(from, target, Mathf.Clamp01(t / duration)));
            yield return null;
        }
        Apply(target);
        if (target <= 0f)
        {
            _dim.SetActive(false);
            foreach (var it in _items) it.Rt.gameObject.SetActive(false);
        }
        _anim = null;
    }

    /// <summary>開き具合 p(0〜1) を反映する。ボタンは上から順に、歯車の下からすべり出る</summary>
    private void Apply(float p)
    {
        _progress = p;
        _dimGroup.alpha = p;
        float span = 1f - STAGGER * (_items.Count - 1);
        for (int i = 0; i < _items.Count; i++)
        {
            float k = SpaceTheme.EaseOutCubic(Mathf.Clamp01((p - i * STAGGER) / span));
            var it = _items[i];
            it.Group.alpha = k;
            it.Group.interactable = k > 0.9f;
            it.Rt.anchoredPosition = it.Base + new Vector2(0f, 26f * (1f - k));
            it.Rt.localScale = Vector3.one * Mathf.Lerp(0.92f, 1f, k);
        }
        float g = SpaceTheme.EaseOutCubic(p);
        _gear.localEulerAngles = new Vector3(0f, 0f, -90f * g);
        _gearImg.color = Color.Lerp(SpaceTheme.TEXT, SpaceTheme.CYAN, g);
    }

    // ================================================================
    // 各ボタン
    // ================================================================
    /// <summary>
    /// 文字の大きさ（2026-11-07 ユーザー依頼）: 標準・大きめ・とても大きいの3つ。選ぶとすぐにアプリ全体に反映し、
    /// この画面も選んだ大きさで作り直す。見本の文字だけは、それぞれの段階の大きさで固定して見せる
    /// </summary>
    private void OnTextSize()
    {
        Close();
        _dialog = BuildTextSizePanel();
    }

    private GameObject BuildTextSizePanel()
    {
        const float W = 880f, OPT_H = 150f, OPT_GAP = 20f, TOP = 200f;
        int n = SpaceTheme.TEXT_SCALES.Length;
        float h = TOP + n * OPT_H + (n - 1) * OPT_GAP + 220f;

        var canvas = _canvas != null ? _canvas : SpaceTheme.MainCanvas();
        var root = SpaceTheme.Rect("__TextSizePanel", canvas.transform);
        SpaceTheme.Stretch(root);
        root.SetAsLastSibling();
        var go = root.gameObject;
        void CloseThis()
        {
            if (go != null) Destroy(go);
            if (_dialog == go) _dialog = null;
        }

        var dim = SpaceTheme.Rect("Dim", root);
        SpaceTheme.Stretch(dim);
        var dimImg = SpaceTheme.Img(dim, null, SpaceTheme.DIM, true);
        SpaceTheme.Clickable(dim.gameObject, dimImg, CloseThis, false);

        var card = SpaceTheme.Rect("Card", root, SpaceTheme.CENTER, Vector2.zero, new Vector2(W, h));
        SpaceTheme.Glass(card, 56f, SpaceTheme.GLASS_DEEP, SpaceTheme.A(SpaceTheme.CYAN, 0.4f), true);
        SpaceTheme.Text(card, "Title", T_TEXT_SIZE, 44, SpaceTheme.TEXT, new Vector2(0.5f, 1f), new Vector2(0f, -80f),
            new Vector2(W - 80f, 64f), FontStyles.Bold);
        SpaceTheme.Text(card, "Body", T_TEXT_SIZE_BODY, 28, SpaceTheme.TEXT_SUB, new Vector2(0.5f, 1f),
            new Vector2(0f, -140f), new Vector2(W - 100f, 44f), FontStyles.Bold);

        int current = SpaceTheme.TextScaleIndex;
        for (int i = 0; i < n; i++)
        {
            int index = i;
            bool on = i == current;
            float y = -(TOP + i * (OPT_H + OPT_GAP) + OPT_H / 2f);
            var opt = SpaceTheme.Rect("Option_" + i, card, new Vector2(0.5f, 1f), new Vector2(0f, y),
                new Vector2(W - 100f, OPT_H));
            var face = SpaceTheme.Glass(opt, 40f,
                on ? SpaceTheme.A(SpaceTheme.CYAN, 0.16f) : new Color(0.07f, 0.09f, 0.24f, 0.78f),
                on ? SpaceTheme.A(SpaceTheme.CYAN, 0.9f) : (Color?)null, true);
            SpaceTheme.Clickable(opt.gameObject, face, () =>
            {
                if (index == SpaceTheme.TextScaleIndex) return;
                SpaceTheme.TextScaleIndex = index;
                CloseThis();
                _dialog = BuildTextSizePanel(); // 選んだ大きさで作り直す
            });
            SpaceTheme.Text(opt, "Name", SpaceTheme.TEXT_SCALE_NAMES[i], 30, on ? SpaceTheme.CYAN : SpaceTheme.TEXT_SUB,
                new Vector2(0f, 0.5f), new Vector2(40f, 0f), new Vector2(240f, 48f), FontStyles.Bold,
                TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
            // 見本（その段階の大きさで固定）
            var sample = SpaceTheme.Text(opt, "Sample", T_TEXT_SIZE_SAMPLE, 34f * SpaceTheme.TEXT_SCALES[i],
                SpaceTheme.TEXT, new Vector2(1f, 0.5f), new Vector2(-40f, 0f), new Vector2(460f, OPT_H - 20f),
                FontStyles.Bold, TextAlignmentOptions.Right, new Vector2(1f, 0.5f));
            SpaceTextScaler.Exclude(sample);
            if (on)
            {
                // 選んでいる印（左上のチェック）
                var badge = SpaceTheme.Rect("Check", opt, new Vector2(0f, 1f), new Vector2(0f, 0f), new Vector2(36f, 36f));
                SpaceTheme.Img(badge, SpaceSprites.Circle, SpaceTheme.CYAN);
                SpaceTheme.Bar(badge, new Vector2(-4.5f, -1.5f), 11f, 4f, -45f, SpaceTheme.TEXT_DARK);
                SpaceTheme.Bar(badge, new Vector2(3f, 1.5f), 17f, 4f, 48f, SpaceTheme.TEXT_DARK);
            }
        }

        var close = SpaceTheme.Rect("Close", card, new Vector2(0.5f, 0f), new Vector2(0f, 110f), new Vector2(480f, 128f));
        var closeFace = SpaceTheme.PrimaryFace(close);
        SpaceTheme.Clickable(close.gameObject, closeFace, CloseThis);
        var closeText = SpaceTheme.Text(close, "Label", T_CLOSE, 38, SpaceTheme.TEXT_DARK, FontStyles.Bold);
        SpaceTheme.Stretch(closeText.rectTransform);

        go.AddComponent<SpacePopIn>().Target = card;
        return go;
    }

    /// <summary>ユーザー名を変更（2026-11-09 フレンドの画面から移した）</summary>
    private void OnRename()
    {
        Close();
        _dialog = SpaceRenameDialog.Open(_canvas);
    }

    /// <summary>誕生月: まだなら選んで登録（1回だけ）。登録済みなら月を知らせる</summary>
    private void OnBirth()
    {
        Close();
        var d = UserDataManager.instance != null ? UserDataManager.instance.UserData : null;
        if (d == null) return;
        if (d.BirthMonth >= 1 && d.BirthMonth <= 12)
        {
            SpaceTheme.Toast(T_BIRTH_SET_A + d.BirthMonth + T_BIRTH_SET_B, _canvas);
            return;
        }
        _dialog = SpaceMonthPicker.Show(_canvas, 0, month =>
        {
            _dialog = null;
            if (month <= 0) return;
            RegisterBirthAsync(month).Forget();
        }, T_BIRTH_BODY);
    }

    private async UniTaskVoid RegisterBirthAsync(int month)
    {
        bool ok = await BirthdayGift.RegisterAsync(month);
        if (this == null) return;
        SpaceTheme.Toast(ok ? T_BIRTH_DONE : T_BIRTH_FAILED, _canvas);
    }

    /// <summary>友だちを紹介: 紹介QR（Referral）を全画面で出す</summary>
    private void OnInvite()
    {
        Close();
        var m = UserDataManager.instance;
        if (m == null || string.IsNullOrEmpty(m.UID)) return;
        string name = m.UserData.Username ?? "";
        _dialog = SpaceQrSheet.Show(_canvas, T_INVITE_CAPTION, T_INVITE_TITLE, Referral.Payload(m.UID, name),
            SpaceTheme.NoParse(SpaceTheme.Safe(name)) + " さん", T_INVITE_NOTE, SpaceTheme.MINT, () => _dialog = null);
    }

    private void OnPrivacy()
    {
        Close();
        AccountSession.OpenPrivacyPolicy();
    }

    private void OnLogout()
    {
        Close();
        _dialog = SpaceDialog.Confirm(_canvas, T_LOGOUT, T_LOGOUT_BODY, T_LOGOUT, false,
            () => { _dialog = null; AccountSession.Logout(); },
            () => _dialog = null, T_CANCEL);
    }

    /// <summary>
    /// INTALES WORLD（2026-11-10）: ダウンロード済みなら削除の確認。まだなら、鍵（職人座の完成。2026-11-11）が
    /// 開いていれば注意（ユーザー指定の文言）を出してからダウンロード、かかっていれば鍵の知らせ
    /// </summary>
    private void OnWorld()
    {
        Close();
        if (SpaceWorld.Installed)
        {
            _dialog = SpaceDialog.Confirm(_canvas, SpaceWorld.T_TITLE, T_WORLD_INSTALLED_BODY, T_REMOVE, true,
                () =>
                {
                    _dialog = null;
                    SpaceWorld.Remove();
                    SpaceTheme.Toast(T_WORLD_REMOVED, _canvas);
                },
                () => _dialog = null, T_CANCEL);
            return;
        }
        var key = SpaceWorld.UnlockProgress;
        if (key != null && !key.Complete) ShowWorldLocked(key);
        else ConfirmWorldDownload();
    }

    /// <summary>ダウンロード前の注意（ユーザー指定の文言そのまま）→ ダウンロード</summary>
    private void ConfirmWorldDownload()
    {
        _dialog = SpaceDialog.Confirm(_canvas, SpaceWorld.T_TITLE, T_WORLD_BODY, T_DOWNLOAD, false,
            () => { _dialog = null; DownloadWorldAsync().Forget(); },
            () => _dialog = null, T_CANCEL);
    }

    /// <summary>
    /// 鍵がかかっているとき（2026-11-11 ユーザー指定）: 職人座を完成させるとダウンロードできることと、いま灯っている星の数。
    /// 「星座を見る」で職人座の詳細（灯った星のゲーム・このジャンルのゲームを見る）を開く。
    /// 管理者はデバッグのため、同じ知らせを見たうえでダウンロードに進める
    /// </summary>
    private void ShowWorldLocked(ConstellationProgress key)
    {
        var def = key.Def;
        string body = T_WORLD_LOCKED_A + def.Name + T_WORLD_LOCKED_B + def.Name + T_WORLD_LOCKED_STARS
                      + key.Partial + " / " + def.Size;
        if (AdminClaim.IsAdmin)
        {
            _dialog = SpaceDialog.Confirm(_canvas, SpaceWorld.T_TITLE, body + T_WORLD_LOCKED_ADMIN, T_DOWNLOAD,
                false, ConfirmWorldDownload, () => _dialog = null, T_CLOSE);
            return;
        }
        _dialog = SpaceDialog.Confirm(_canvas, SpaceWorld.T_TITLE, body, T_WORLD_SEE, false,
            () => { _dialog = null; SpaceConstellationPage.OpenDetail(def.Id); },
            () => _dialog = null, T_CLOSE);
    }

    private async UniTaskVoid DownloadWorldAsync()
    {
        if (_busy) return;
        _busy = true;
        var busy = SpaceDialog.Busy(_canvas, T_DOWNLOADING);
        bool ok = false;
        try
        {
            var download = SpaceWorld.DownloadAsync();
            await UniTask.Delay(TimeSpan.FromSeconds(0.8f), ignoreTimeScale: true);
            ok = await download;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[AccountMenu] INTALES WORLD のダウンロードでエラー: {ex.Message}");
        }
        finally
        {
            if (busy != null) Destroy(busy);
            _busy = false;
        }
        if (this == null) return;
        SpaceTheme.Toast(ok ? T_WORLD_DONE : T_DOWNLOAD_FAILED, _canvas);
    }

    private void OnDelete()
    {
        Close();
        _dialog = SpaceDialog.Confirm(_canvas, T_DELETE_TITLE, T_DELETE_BODY, T_DELETE_NEXT, true,
            ShowFinalConfirm, () => _dialog = null, T_CANCEL);
    }

    private void ShowFinalConfirm()
    {
        _dialog = SpaceDialog.Confirm(_canvas, T_FINAL_TITLE, T_FINAL_BODY, T_FINAL_OK, true,
            () => { _dialog = null; DeleteAsync().Forget(); },
            () => _dialog = null, T_CANCEL);
    }

    private async UniTaskVoid DeleteAsync()
    {
        if (_busy) return;
        _busy = true;
        var busy = SpaceDialog.Busy(_canvas, T_DELETING);
        try
        {
            await AccountSession.DeleteAccountAsync();
            AccountSession.ReturnToStart();
        }
        catch (Exception ex)
        {
            Debug.LogError($"[AccountMenu] 退会エラー: {ex.Message}");
            if (busy != null) Destroy(busy);
            _busy = false;
            SpaceTheme.Toast(T_DELETE_FAILED, _canvas);
        }
    }
}
