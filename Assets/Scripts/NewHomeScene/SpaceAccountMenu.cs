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
/// 新ホーム右上の設定ボタン（歯車）。押すと下に4つのボタンが順に出てくる:
///   ・ボードゲーム図鑑     … 使いたい人だけのダウンロード（SpaceZukan）。ダウンロードすると、ホームの惑星の左下に
///                            図鑑の小さな惑星が出る。ダウンロード済みなら削除（ホームから図鑑の惑星を消す）
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
        "退会するとアカウントが削除され、\nクーポン・来店記録・フレンドなど\nすべてのデータが完全に消えます。\n\nこの操作は元に戻せません。";
    private const string T_DELETE_NEXT = "退会をすすめる";
    private const string T_FINAL_TITLE = "最終確認";
    private const string T_FINAL_BODY = "本当に退会しますか？";
    private const string T_FINAL_OK = "退会する";
    private const string T_DELETING = "退会処理中…";
    private const string T_DELETE_FAILED = "退会に失敗しました\n通信環境を確認して再度お試しください";
    private const string T_ZUKAN = "ボードゲーム図鑑";
    private const string T_ZUKAN_BODY =
        "お店のボードゲームを\n調べられる図鑑です。\n人数・時間で探したり、遊んだ記録や\n写真を残したりできます。\n\nダウンロードすると、ホームに\n図鑑の小さな惑星が出ます。";
    private const string T_ZUKAN_DOWNLOAD = "ダウンロード";
    private const string T_ZUKAN_DOWNLOADING = "ダウンロード中…";
    private const string T_ZUKAN_DONE = "ダウンロードしました\nホームの惑星の左下の、緑の小さな惑星から開けます";
    private const string T_ZUKAN_FAILED = "ダウンロードに失敗しました\n通信環境を確認して再度お試しください";
    private const string T_ZUKAN_INSTALLED_BODY =
        "ダウンロード済みです。\nホームの惑星の左下の\n緑の小さな惑星から開けます。\n\n削除すると、図鑑の惑星が\nホームからなくなります。\n（遊んだ記録・写真は残ります）";
    private const string T_ZUKAN_REMOVE = "削除する";
    private const string T_ZUKAN_REMOVED = "ボードゲーム図鑑を削除しました";

    /// <summary>このメニューで表示しうる文字（NewHome の起動時にまとめてフォントへ登録する）</summary>
    public static string GlyphText => new StringBuilder()
        .Append(T_PRIVACY).Append(T_LOGOUT).Append(T_DELETE).Append(T_CANCEL).Append(T_LOGOUT_BODY)
        .Append(T_DELETE_TITLE).Append(T_DELETE_BODY).Append(T_DELETE_NEXT).Append(T_FINAL_TITLE)
        .Append(T_FINAL_BODY).Append(T_FINAL_OK).Append(T_DELETING).Append(T_DELETE_FAILED).Append(T_ZUKAN)
        .Append(T_ZUKAN_BODY).Append(T_ZUKAN_DOWNLOAD).Append(T_ZUKAN_DOWNLOADING)
        .Append(T_ZUKAN_DONE).Append(T_ZUKAN_FAILED).Append(T_ZUKAN_INSTALLED_BODY).Append(T_ZUKAN_REMOVE)
        .Append(T_ZUKAN_REMOVED).ToString();

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

        AddItem(safe, T_ZUKAN, false, DrawDownloadIcon, OnZukan);
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
        _dim.SetActive(true);
        foreach (var it in _items) it.Rt.gameObject.SetActive(true);
        Animate(1f, 0.30f);
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

    /// <summary>ボードゲーム図鑑: ダウンロードしていなければダウンロード、済みなら削除の確認</summary>
    private void OnZukan()
    {
        Close();
        if (!SpaceZukan.Installed)
        {
            _dialog = SpaceDialog.Confirm(_canvas, T_ZUKAN, T_ZUKAN_BODY, T_ZUKAN_DOWNLOAD, false,
                () => { _dialog = null; DownloadZukanAsync().Forget(); },
                () => _dialog = null, T_CANCEL);
        }
        else
        {
            _dialog = SpaceDialog.Confirm(_canvas, T_ZUKAN, T_ZUKAN_INSTALLED_BODY, T_ZUKAN_REMOVE, true,
                () =>
                {
                    _dialog = null;
                    SpaceZukan.Remove();
                    SpaceTheme.Toast(T_ZUKAN_REMOVED, _canvas);
                },
                () => _dialog = null, T_CANCEL);
        }
    }

    /// <summary>ダウンロード（すぐ終わっても「ダウンロード中…」が一瞬で消えないよう、少しだけ見せる）</summary>
    private async UniTaskVoid DownloadZukanAsync()
    {
        if (_busy) return;
        _busy = true;
        var busy = SpaceDialog.Busy(_canvas, T_ZUKAN_DOWNLOADING);
        bool ok = false;
        try
        {
            var download = SpaceZukan.DownloadAsync();
            await UniTask.Delay(TimeSpan.FromSeconds(0.8f), ignoreTimeScale: true);
            ok = await download;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[AccountMenu] 図鑑のダウンロードでエラー: {ex.Message}");
        }
        finally
        {
            if (busy != null) Destroy(busy);
            _busy = false;
        }
        if (this == null) return;
        SpaceTheme.Toast(ok ? T_ZUKAN_DONE : T_ZUKAN_FAILED, _canvas);
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
