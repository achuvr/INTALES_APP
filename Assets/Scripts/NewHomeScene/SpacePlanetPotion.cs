using System;
using System.Collections.Generic;
using System.Text;
using Cysharp.Threading.Tasks;
using Firebase.Firestore;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 惑星回復薬（2026-11-12 ユーザー指定「惑星回復薬をショップに並べる。使うと、いま非アクティブな惑星を1つアクティブに戻せる。
/// 所持品リストのいちばん下に。非アクティブな惑星が1つもなければ『使えません』と表示」。値段は同日ユーザー指定で 3G）。
/// 「非アクティブな惑星」＝力を失った惑星（コマをもらって退店した惑星。JourneyRules.ExhaustedList）。
/// 力の考え方は INTALES WORLD をダウンロードした人だけのもの（SpacePlanets.IsExhausted）なので、
/// ダウンロードしていない人には力を失った惑星がない＝使えない。
/// 所持品の行を押す → 力を失った惑星から1つ選ぶ（SpacePotionChooser。1つだけなら選んだ状態で開く）→「この惑星に力を戻す」。
/// 使うのは users を1回読むトランザクションで、薬が1つ以上あり、選んだ惑星がまだ力を失っているかを確かめてから、
/// journey（JourneyRules.Reactivate）と薬の数をまとめて書く。
/// </summary>
public static class SpacePlanetPotion
{
    /// <summary>users のフィールド名</summary>
    public const string FIELD = "planet_potion";
    public const string NAME = "惑星回復薬";
    /// <summary>ショップの値段（G。2026-11-12 ユーザー指定）</summary>
    public const int PRICE = 3;
    /// <summary>カードの光・縁の色（薬の水色）</summary>
    public static readonly Color TINT = SpaceTheme.Hex(0x7AD2FF);
    /// <summary>ショップのカードの説明（3列のカードで1行に収まる長さ）</summary>
    public const string SHOP_DESC = "力を失った惑星1つに力を戻す";
    /// <summary>所持品の行の説明</summary>
    public const string ROW_DESC = "力を失った惑星1つに力を戻せます";
    /// <summary>力を失った惑星が1つもないとき</summary>
    public const string T_CANNOT = "力を失った惑星がないので、使えません";
    public const string T_CAPTION = "PLANET POTION";

    private const string T_TITLE = "力を戻す惑星を1つ選んでください";
    private const string T_HAVE_A = NAME + " 残り ";
    private const string T_HAVE_B = "個";
    private const string T_OK = "この惑星に力を戻す";
    private const string T_USING = "使っています…";
    private const string T_ERR_NONE = NAME + "を持っていません";
    private const string T_ERR_ACTIVE = "その惑星はもう力を取り戻しています";
    private const string T_ERR_USER = "ユーザー情報を読み込めませんでした";
    private const string T_ERR_NETWORK = "通信に失敗しました。もう一度お試しください";
    private const string T_HISTORY_A = NAME + "で 惑星「";
    private const string T_HISTORY_B = "」が力を取り戻した";

    /// <summary>この機能で表示しうる文字（NewHome の起動時にまとめてフォントへ登録する）</summary>
    public static string GlyphText => new StringBuilder()
        .Append(NAME).Append(SHOP_DESC).Append(ROW_DESC).Append(T_CANNOT).Append(T_CAPTION).Append(T_TITLE)
        .Append(T_HAVE_A).Append(T_HAVE_B).Append(T_OK).Append(T_USING).Append(T_ERR_NONE).Append(T_ERR_ACTIVE)
        .Append(T_ERR_USER).Append(T_ERR_NETWORK).Append(T_HISTORY_A).Append(T_HISTORY_B).Append("0123456789")
        .ToString();

    // 選ぶ画面（SpacePotionChooser）から使う
    internal static string TitleText => T_TITLE;
    internal static string OkText => T_OK;
    internal static string UsingText => T_USING;
    internal static string HaveText(int n) => T_HAVE_A + n + T_HAVE_B;

    private static Sprite _icon;

    /// <summary>絵（丸いフラスコに宇宙の色の薬と小さな惑星。Resources/ItemIcon/PlanetPotion。tools/shop/make_planet_potion_icon.py）</summary>
    public static Sprite Icon => _icon != null ? _icon : (_icon = Resources.Load<Sprite>("ItemIcon/PlanetPotion"));

    private static UserData Data => UserDataManager.instance != null ? UserDataManager.instance.UserData : null;

    /// <summary>持っている数</summary>
    public static int Count => Data != null ? Mathf.Max(0, Data.PlanetPotion) : 0;

    /// <summary>いま力を失っている惑星（使える相手。力を失った順）。INTALES WORLD をダウンロードしていなければ空</summary>
    public static List<SpacePlanetDef> Candidates()
    {
        var list = new List<SpacePlanetDef>();
        if (!SpaceWorld.Installed) return list;
        foreach (var id in JourneyRules.ExhaustedList(JourneyService.Journey, StoreCalendar.TodayKey))
        {
            var def = SpacePlanets.Find(id);
            if (def != null && !list.Contains(def)) list.Add(def);
        }
        return list;
    }

    /// <summary>使った結果</summary>
    public sealed class UseResult
    {
        public bool Ok;
        public string Error;
        public string PlanetId;
        public int Left; // 使ったあとの数
        internal JourneyData Journey;
    }

    /// <summary>薬を1つ使って、planetId の惑星に力を戻す。できなければ Ok=false と Error（何も書かない）</summary>
    public static async UniTask<UseResult> UseAsync(string planetId)
    {
        var fail = new UseResult { PlanetId = planetId };
        var m = UserDataManager.instance;
        if (string.IsNullOrEmpty(planetId) || m == null || string.IsNullOrEmpty(m.UID) || m.UserData == null)
        {
            fail.Error = T_ERR_USER;
            return fail;
        }
        string today = StoreCalendar.TodayKey; // トランザクションは裏のスレッドで動くので先に読んでおく
        var db = FirebaseFirestore.DefaultInstance;
        var doc = db.Collection("users").Document(m.UID);
        UseResult r;
        try
        {
            r = await db.RunTransactionAsync(tx => tx.GetSnapshotAsync(doc).ContinueWith(t =>
            {
                var res = new UseResult { PlanetId = planetId };
                var snap = t.Result;
                var u = snap != null && snap.Exists ? snap.ToDictionary() : null;
                if (u == null)
                {
                    res.Error = T_ERR_USER;
                    return res;
                }
                long have = Long(u, FIELD);
                if (have < 1)
                {
                    res.Error = T_ERR_NONE;
                    return res;
                }
                var j = JourneyData.From(u.TryGetValue("journey", out var raw) ? raw : null);
                // 退店し忘れで営業日が変わった惑星も、先に力を失った惑星へ移してから戻す（JourneyRules.Reactivate）
                var done = j != null ? JourneyRules.Reactivate(j, new[] { planetId }, today) : new List<string>();
                if (done.Count == 0)
                {
                    res.Error = T_ERR_ACTIVE;
                    return res;
                }
                tx.Update(doc, new Dictionary<string, object> { { "journey", j.ToMap() }, { FIELD, have - 1 } });
                res.Ok = true;
                res.Left = (int)(have - 1);
                res.Journey = j;
                return res;
            })).AsUniTask();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[PlanetPotion] 使えませんでした ({planetId}): {ex.Message}");
            fail.Error = T_ERR_NETWORK;
            return fail;
        }
        if (r == null || !r.Ok)
        {
            if (r != null && r.Error == T_ERR_NONE && Data != null) Data.PlanetPotion = 0; // ほかの端末で使い切っていた
            return r ?? fail;
        }

        var d = Data;
        if (d != null)
        {
            d.Journey = r.Journey;
            d.PlanetPotion = r.Left;
        }
        var def = SpacePlanets.Find(planetId);
        LocalHistoryLog.Add("planet", T_HISTORY_A + (def != null ? def.Name : planetId) + T_HISTORY_B);
        JourneyService.NotifyChanged(); // ホームの中央の惑星の色（淡い→いつもの色）なども更新
        return r;
    }

    private static long Long(Dictionary<string, object> m, string k)
    {
        if (m == null || !m.TryGetValue(k, out var v) || v == null) return 0;
        try { return Convert.ToInt64(v); }
        catch { return 0; }
    }
}

/// <summary>
/// 惑星回復薬で力を戻す惑星を1つ選ぶ画面（所持品パネルの上に重ねる全画面）。INTALES WORLD の惑星の神殿の選ぶ画面と同じ見た目で、
/// 淡い色の惑星が並び、押すとその惑星だけがいつもの色（力が戻った姿）になる。候補が1つなら選んだ状態で開く
/// </summary>
public class SpacePotionChooser : MonoBehaviour
{
    private const int COLS = 3;
    private const float CELL_W = 300f, CELL_H = 290f, GAP = 18f, FRAME = 200f;
    private const float LIST_TOP = 240f, LIST_BOTTOM = 220f;

    private Canvas _canvas;
    private Action<SpacePlanetDef> _onUsed;
    private Action _onClosed;
    private List<SpacePlanetDef> _candidates;
    private string _chosen;
    private readonly Dictionary<string, (Image stroke, CanvasGroup vivid)> _cells =
        new Dictionary<string, (Image stroke, CanvasGroup vivid)>();
    private CanvasGroup _okGroup;
    private TextMeshProUGUI _okLabel;
    private bool _busy, _closed;

    /// <summary>parent（所持品パネル）の上に開く。使えたら onUsed（力を戻した惑星）、閉じたら onClosed</summary>
    public static SpacePotionChooser Open(Transform parent, Canvas canvas, List<SpacePlanetDef> candidates,
        Action<SpacePlanetDef> onUsed, Action onClosed)
    {
        var root = SpaceTheme.Rect("__PotionChooser", parent);
        SpaceTheme.Stretch(root);
        root.SetAsLastSibling();
        var c = root.gameObject.AddComponent<SpacePotionChooser>();
        c._canvas = canvas;
        c._candidates = candidates;
        c._onUsed = onUsed;
        c._onClosed = onClosed;
        if (candidates.Count == 1) c._chosen = candidates[0].Id;
        c.Build();
        return c;
    }

    public void HandleBack()
    {
        if (_busy) return;
        Close();
    }

    public void Close()
    {
        if (_closed) return;
        _closed = true;
        Destroy(gameObject);
    }

    private void OnDestroy()
    {
        _closed = true;
        _onClosed?.Invoke();
        _onClosed = null;
    }

    private void Build()
    {
        var rt = (RectTransform)transform;
        var group = gameObject.AddComponent<CanvasGroup>();
        var dim = SpaceTheme.Rect("Dim", rt);
        SpaceTheme.Stretch(dim);
        SpaceTheme.Img(dim, null, new Color(0.01f, 0.015f, 0.05f, 0.96f), true);
        var safe = SpaceTheme.Rect("Safe", rt);
        SpaceTheme.Stretch(safe);
        safe.gameObject.AddComponent<SpaceSafeArea>();
        float safeW = SpaceTheme.CanvasSize(_canvas).x;

        var top = new Vector2(0.5f, 1f);
        var caption = SpaceTheme.Text(safe, "Caption", SpacePlanetPotion.T_CAPTION, 24,
            SpaceTheme.A(SpacePlanetPotion.TINT, 0.9f), top, new Vector2(0f, -70f), new Vector2(700f, 36f), FontStyles.Bold);
        SpaceTheme.Tracking(caption, 18f);
        var title = SpaceTheme.Text(safe, "Title", SpacePlanetPotion.TitleText, 40, SpaceTheme.TEXT, top,
            new Vector2(0f, -124f), new Vector2(safeW - 260f, 56f), FontStyles.Bold);
        title.enableAutoSizing = true;
        title.fontSizeMin = 26f;
        title.fontSizeMax = 40f;
        SpaceTheme.Text(safe, "Have", SpacePlanetPotion.HaveText(SpacePlanetPotion.Count), 30, SpaceTheme.GOLD, top,
            new Vector2(0f, -184f), new Vector2(600f, 44f), FontStyles.Bold);

        // 右上の閉じる
        var close = SpaceTheme.Rect("Close", safe, new Vector2(1f, 1f), new Vector2(-92f, -84f), new Vector2(104f, 104f));
        var closeFace = SpaceTheme.Img(close, SpaceSprites.Circle, new Color(0.05f, 0.07f, 0.19f, 0.85f), true);
        var rim = SpaceTheme.Rect("Rim", close);
        SpaceTheme.Stretch(rim);
        SpaceTheme.Img(rim, SpaceSprites.RingSmall, SpaceTheme.STROKE);
        SpaceTheme.Bar(close, Vector2.zero, 44f, 6f, 45f, SpaceTheme.TEXT);
        SpaceTheme.Bar(close, Vector2.zero, 44f, 6f, -45f, SpaceTheme.TEXT);
        SpaceTheme.Clickable(close.gameObject, closeFace, HandleBack);

        // 一覧（3列。多ければスクロール）
        float gridW = COLS * CELL_W + (COLS - 1) * GAP;
        int rows = Mathf.CeilToInt(_candidates.Count / (float)COLS);
        float contentH = rows * CELL_H + Mathf.Max(0, rows - 1) * GAP + 24f;
        var list = SpaceTheme.Rect("List", safe);
        SpaceTheme.Stretch(list, (safeW - gridW) * 0.5f, LIST_BOTTOM, (safeW - gridW) * 0.5f, LIST_TOP);
        var viewport = SpaceTheme.Rect("Viewport", list);
        SpaceTheme.Stretch(viewport);
        SpaceTheme.HitArea(viewport);
        viewport.gameObject.AddComponent<RectMask2D>();
        var content = SpaceTheme.Rect("Content", viewport);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.sizeDelta = new Vector2(0f, contentH);
        var scroll = list.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Elastic;
        scroll.scrollSensitivity = 30f;

        for (int i = 0; i < _candidates.Count; i++)
        {
            var def = _candidates[i];
            int col = i % COLS, row = i / COLS;
            var cell = SpaceTheme.Rect("Cell_" + def.Id, content, new Vector2(0f, 1f),
                new Vector2(CELL_W * 0.5f + col * (CELL_W + GAP), -(12f + CELL_H * 0.5f + row * (CELL_H + GAP))),
                new Vector2(CELL_W, CELL_H));
            var faceRt = SpaceTheme.Rect("Face", cell);
            SpaceTheme.Stretch(faceRt);
            var face = SpaceTheme.Rounded(faceRt, new Color(0.07f, 0.09f, 0.24f, 0.8f), 34f, true);
            string id = def.Id;
            SpaceTheme.Clickable(cell.gameObject, face, () => Choose(id));
            var stroke = SpaceTheme.Rect("Stroke", cell);
            SpaceTheme.Stretch(stroke);
            var strokeImg = SpaceTheme.RoundedStroke(stroke, SpaceTheme.STROKE, 34f);

            // 淡い色（今）と、選ぶと出るいつもの色（力が戻った姿）を重ねる
            var holder = SpaceTheme.Rect("Planet", cell, top, new Vector2(0f, -FRAME * 0.5f - 8f), new Vector2(FRAME, FRAME));
            SpacePlanetView.Create(holder, def, FRAME, SpacePlanetView.Quality.Thumb, false, false, true);
            var vividRt = SpaceTheme.Rect("Vivid", holder);
            SpaceTheme.Stretch(vividRt);
            var vivid = vividRt.gameObject.AddComponent<CanvasGroup>();
            vivid.alpha = 0f;
            SpacePlanetView.Create(vividRt, def, FRAME, SpacePlanetView.Quality.Thumb, false);
            var name = SpaceTheme.Text(cell, "Name", def.Name, 30, SpaceTheme.TEXT, new Vector2(0.5f, 0f),
                new Vector2(0f, 36f), new Vector2(CELL_W - 20f, 44f), FontStyles.Bold);
            name.enableAutoSizing = true;
            name.fontSizeMin = 20f;
            name.fontSizeMax = 30f;
            _cells[id] = (strokeImg, vivid);
        }

        var ok = SpaceTheme.Rect("Ok", safe, new Vector2(0.5f, 0f), new Vector2(0f, 64f), new Vector2(gridW, 128f),
            new Vector2(0.5f, 0f));
        var okFace = SpaceTheme.PrimaryFace(ok);
        SpaceTheme.Clickable(ok.gameObject, okFace, OnOk);
        _okLabel = SpaceTheme.Text(ok, "Label", SpacePlanetPotion.OkText, 38, SpaceTheme.TEXT_DARK, FontStyles.Bold);
        SpaceTheme.Stretch(_okLabel.rectTransform);
        _okGroup = ok.gameObject.AddComponent<CanvasGroup>();
        Refresh();

        group.alpha = 0f;
        StartCoroutine(SpaceTheme.Tween(0.22f, 0f, k => group.alpha = k));
    }

    private void Choose(string id)
    {
        if (_busy) return;
        _chosen = id;
        Refresh();
    }

    private void Refresh()
    {
        foreach (var kv in _cells)
        {
            bool on = kv.Key == _chosen;
            kv.Value.stroke.color = on ? SpaceTheme.A(SpaceTheme.GOLD, 0.95f) : SpaceTheme.STROKE;
            kv.Value.vivid.alpha = on ? 1f : 0f;
        }
        _okGroup.alpha = _chosen != null && !_busy ? 1f : 0.4f;
    }

    private void OnOk()
    {
        if (_busy) return;
        if (_chosen == null)
        {
            SpaceTheme.Toast(SpacePlanetPotion.TitleText, _canvas);
            return;
        }
        UseRoutine(_chosen).Forget();
    }

    private async UniTaskVoid UseRoutine(string id)
    {
        _busy = true;
        _okLabel.text = SpacePlanetPotion.UsingText;
        Refresh();
        SpacePlanetPotion.UseResult r;
        try
        {
            r = await SpacePlanetPotion.UseAsync(id);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[PlanetPotion] 使うときのエラー ({id}): {ex.Message}");
            r = null;
        }
        if (this == null) return;
        _busy = false;
        if (r == null || !r.Ok)
        {
            // 失敗したら閉じて、所持品の一覧を最新の状態で見直してもらう
            SpaceTheme.Toast(r?.Error ?? "通信に失敗しました。もう一度お試しください", _canvas);
            Close();
            return;
        }
        var def = SpacePlanets.Find(id);
        var onUsed = _onUsed;
        _onUsed = null;
        Close();
        if (def != null) onUsed?.Invoke(def);
    }
}
