using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 管理者用: 1か月遊び放題パスの購入者の一覧（新ホームで管理者アカウントにだけ出る、カードの絵のボタンから開く全画面）。
/// だれが（受け取ったときのユーザー名）・どの種類を・いつからいつまで使えるかを、
/// 利用中（期限の近い順）→ 期限切れ（期限の新しい順）に並べる。上に、いま利用中の人数と種類ごとの内訳。
/// 開くたびにサーバーから読み直す（受け取られたパスの枚数ぶんの読み取り）。
/// </summary>
public class SpaceAdminPassList : MonoBehaviour
{
    private const string T_CAPTION = "ADMIN ONLY";
    private const string T_TITLE = "パスの購入者";
    private const string T_LOADING = "読み込み中…";
    private const string T_FAILED = "読み込めませんでした。通信の状態を確かめてください";
    private const string T_RETRY = "もう一度読み込む";
    private const string T_EMPTY = "まだパスを受け取った人はいません";
    private const string T_ACTIVE = "利用中";
    private const string T_EXPIRED = "期限切れ";
    private const string T_PEOPLE = "人";
    private const string T_SAN = " さん";
    private const string T_NO_NAME = "（名前なし）";
    private const string T_BOUGHT = "購入日 ";
    private const string T_LIMIT = "　期限 ";
    private const string T_UNTIL = "まで";
    private const string T_LEFT_A = "あと";
    private const string T_LEFT_B = "日";
    private const string T_LAST_DAY = "本日まで";
    private const string T_NOTE = "名前は受け取ったときのユーザー名です";

    /// <summary>この画面で表示しうる文字（名前は読み込んだあとに登録する）</summary>
    public static string GlyphText
    {
        get
        {
            var sb = new StringBuilder();
            sb.Append(T_CAPTION).Append(T_TITLE).Append(T_LOADING).Append(T_FAILED).Append(T_RETRY).Append(T_EMPTY)
              .Append(T_ACTIVE).Append(T_EXPIRED).Append(T_PEOPLE).Append(T_SAN).Append(T_NO_NAME).Append(T_BOUGHT)
              .Append(T_LIMIT).Append(T_UNTIL).Append(T_LEFT_A).Append(T_LEFT_B).Append(T_LAST_DAY).Append(T_NOTE)
              .Append("（）・0123456789/");
            foreach (var t in StorePasses.Types) sb.Append(t.Label);
            return sb.ToString();
        }
    }

    private const float LIST_W = 960f;
    private const float LIST_TOP = 300f, LIST_BOTTOM = 56f;
    private const float ROW_H = 156f, ROW_GAP = 16f, SECTION_H = 76f, PAD_TOP = 8f, PAD_BOTTOM = 40f;
    private const float CARD_W = 150f;

    private CanvasGroup _group;
    private RectTransform _safe, _body;
    private TextMeshProUGUI _summary;
    private Action _onClosed;
    private bool _closing, _loading;

    public static SpaceAdminPassList Open(Canvas canvas, Action onClosed)
    {
        var root = SpaceTheme.Rect("__AdminPassList", canvas.transform);
        SpaceTheme.Stretch(root);
        root.SetAsLastSibling();
        SpaceTheme.SubCanvas(root.gameObject, true);
        var list = root.gameObject.AddComponent<SpaceAdminPassList>();
        list._onClosed = onClosed;
        list.Build();
        return list;
    }

    public void HandleBack() => Close();

    public void Close()
    {
        if (_closing) return;
        _closing = true;
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

        var caption = SpaceTheme.Text(_safe, "Caption", T_CAPTION, 24, SpaceTheme.A(SpaceTheme.GOLD, 0.9f),
            new Vector2(0.5f, 1f), new Vector2(0f, -70f), new Vector2(700f, 36f), FontStyles.Bold);
        SpaceTheme.Tracking(caption, 18f);
        SpaceTheme.Text(_safe, "Title", T_TITLE, 44, SpaceTheme.TEXT, new Vector2(0.5f, 1f), new Vector2(0f, -124f),
            new Vector2(800f, 60f), FontStyles.Bold);
        _summary = SpaceTheme.Text(_safe, "Summary", "", 28, SpaceTheme.TEXT_SUB, new Vector2(0.5f, 1f),
            new Vector2(0f, -200f), new Vector2(980f, 44f), FontStyles.Bold);
        _summary.enableAutoSizing = true;
        _summary.fontSizeMin = 20f;
        _summary.fontSizeMax = 28f;
        SpaceTheme.Text(_safe, "Note", T_NOTE, 22, SpaceTheme.TEXT_MUTED, new Vector2(0.5f, 1f),
            new Vector2(0f, -248f), new Vector2(980f, 34f), FontStyles.Bold);
        CloseButton(_safe);

        _body = SpaceTheme.Rect("Body", _safe);
        _body.anchorMin = new Vector2(0.5f, 0f);
        _body.anchorMax = new Vector2(0.5f, 1f);
        _body.pivot = SpaceTheme.CENTER;
        _body.offsetMin = new Vector2(-LIST_W * 0.5f, LIST_BOTTOM);
        _body.offsetMax = new Vector2(LIST_W * 0.5f, -LIST_TOP);

        StartCoroutine(SpaceTheme.Tween(0.25f, 0f, k => _group.alpha = k));
        LoadAsync().Forget();
    }

    // ================================================================
    // 読み込み
    // ================================================================
    private async UniTaskVoid LoadAsync()
    {
        if (_loading) return;
        _loading = true;
        ClearBody();
        _summary.text = "";
        Message(T_LOADING, SpaceTheme.TEXT_SUB);

        List<PassInfo> passes = null;
        try
        {
            passes = await StorePasses.LoadAllAsync();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[AdminPass] 一覧の読み込みに失敗: {ex.Message}");
        }
        if (this == null || _closing) return;
        _loading = false;
        if (passes == null)
        {
            ClearBody();
            Message(T_FAILED, SpaceTheme.DANGER);
            RetryButton();
            return;
        }
        StartCoroutine(Show(passes));
    }

    /// <summary>名前の文字を少しずつフォントへ登録してから並べる（一度に大量に登録すると実機で落ちるため）</summary>
    private IEnumerator Show(List<PassInfo> passes)
    {
        var names = new StringBuilder();
        foreach (var p in passes) names.Append(p.HolderName);
        yield return SpaceTheme.Prewarm(names.ToString());
        if (_closing) yield break;

        var now = DateTime.Now;
        var active = new List<PassInfo>();
        var expired = new List<PassInfo>();
        foreach (var p in passes) (p.IsActive(now) ? active : expired).Add(p);
        active.Sort((a, b) => a.Expiry != b.Expiry ? a.Expiry.CompareTo(b.Expiry) : string.CompareOrdinal(a.HolderName, b.HolderName));
        expired.Sort((a, b) => b.Expiry.CompareTo(a.Expiry));

        // いま利用中の人数と種類ごとの内訳（同じ人が2枚持っていたら2と数える）
        var sb = new StringBuilder();
        sb.Append(T_ACTIVE).Append(' ').Append(active.Count).Append(T_PEOPLE).Append("（");
        for (int i = 0; i < StorePasses.Types.Length; i++)
        {
            var t = StorePasses.Types[i];
            int n = active.FindAll(p => p.Type == t).Count;
            if (i > 0) sb.Append("・");
            sb.Append($"<color=#{ColorUtility.ToHtmlStringRGB(SpacePassCard.FoilColor(t))}>{t.Label}</color> {n}");
        }
        sb.Append("）");
        _summary.text = sb.ToString();

        ClearBody();
        if (passes.Count == 0)
        {
            Message(T_EMPTY, SpaceTheme.TEXT_SUB);
            yield break;
        }

        float h = PAD_TOP;
        if (active.Count > 0) h += SECTION_H + active.Count * (ROW_H + ROW_GAP);
        if (expired.Count > 0) h += SECTION_H + expired.Count * (ROW_H + ROW_GAP);
        h += PAD_BOTTOM;
        var content = BuildScroll(h);

        float y = PAD_TOP;
        if (active.Count > 0)
        {
            Section(content, ref y, $"{T_ACTIVE}（{active.Count}{T_PEOPLE}）", SpaceTheme.MINT);
            foreach (var p in active) Row(content, ref y, p, now);
        }
        if (expired.Count > 0)
        {
            Section(content, ref y, $"{T_EXPIRED}（{expired.Count}{T_PEOPLE}）", SpaceTheme.TEXT_MUTED);
            foreach (var p in expired) Row(content, ref y, p, now);
        }
    }

    private RectTransform BuildScroll(float contentH)
    {
        var viewport = SpaceTheme.Rect("List", _body);
        SpaceTheme.Stretch(viewport);
        SpaceTheme.HitArea(viewport);
        viewport.gameObject.AddComponent<RectMask2D>().softness = new Vector2Int(0, 28);
        var content = SpaceTheme.Rect("Content", viewport, new Vector2(0.5f, 1f), Vector2.zero,
            new Vector2(LIST_W, contentH), new Vector2(0.5f, 1f));
        var scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.scrollSensitivity = 40f;
        return content;
    }

    private static void Section(RectTransform content, ref float y, string text, Color color)
    {
        SpaceTheme.Text(content, "Section", text, 30, color, new Vector2(0f, 1f), new Vector2(12f, -(y + SECTION_H * 0.5f + 6f)),
            new Vector2(600f, 44f), FontStyles.Bold, TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
        y += SECTION_H;
    }

    /// <summary>1人1行: 小さなカード＋名前、種類（箔の色）・購入日・期限、右に残り日数</summary>
    private static void Row(RectTransform content, ref float y, PassInfo p, DateTime now)
    {
        bool active = p.IsActive(now);
        var row = SpaceTheme.Rect("Row", content, new Vector2(0.5f, 1f), new Vector2(0f, -y), new Vector2(LIST_W, ROW_H),
            new Vector2(0.5f, 1f));
        SpaceTheme.Glass(row, 34f, new Color(0.07f, 0.09f, 0.24f, 0.78f),
            SpaceTheme.A(SpacePassCard.GlowColor(p.Type), active ? 0.45f : 0.15f));
        if (!active) row.gameObject.AddComponent<CanvasGroup>().alpha = 0.6f;

        var holder = SpaceTheme.Rect("Card", row, new Vector2(0f, 0.5f), new Vector2(24f + CARD_W * 0.5f, 0f),
            new Vector2(CARD_W, CARD_W / SpacePassCard.ASPECT));
        SpacePassCard.Create(holder, p.Type, p, CARD_W, false, false);

        float x = 24f + CARD_W + 24f;
        string name = string.IsNullOrEmpty(p.HolderName) ? T_NO_NAME
            : SpaceTheme.NoParse(SpaceTheme.Safe(p.HolderName)) + $"<size=70%><color={SpaceTheme.SUB_HEX}>{T_SAN}</color></size>";
        var nameText = SpaceTheme.Text(row, "Name", name, 36, SpaceTheme.TEXT, new Vector2(0f, 0.5f), new Vector2(x, 28f),
            new Vector2(LIST_W - x - 200f, 50f), FontStyles.Bold, TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
        nameText.enableAutoSizing = true;
        nameText.fontSizeMin = 24f;
        nameText.fontSizeMax = 36f;

        string type = $"<color=#{ColorUtility.ToHtmlStringRGB(SpacePassCard.FoilColor(p.Type))}>{p.Type.Label}</color>";
        var detail = SpaceTheme.Text(row, "Detail",
            $"{type}　{T_BOUGHT}{StorePasses.DateText(p.Start)}{T_LIMIT}{StorePasses.DateText(p.Expiry)}{T_UNTIL}", 24,
            SpaceTheme.TEXT_SUB, new Vector2(0f, 0.5f), new Vector2(x, -28f), new Vector2(LIST_W - x - 200f, 38f),
            FontStyles.Bold, TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
        detail.enableAutoSizing = true;
        detail.fontSizeMin = 18f;
        detail.fontSizeMax = 24f;

        int days = p.DaysLeft(now);
        string left = !active ? T_EXPIRED
            : days <= 1 ? T_LAST_DAY
            : $"<size=55%><color={SpaceTheme.SUB_HEX}>{T_LEFT_A} </color></size>{days}<size=55%><color={SpaceTheme.SUB_HEX}> {T_LEFT_B}</color></size>";
        SpaceTheme.Text(row, "Left", left, active && days > 1 ? 50 : 28, active ? SpaceTheme.GOLD : SpaceTheme.TEXT_MUTED,
            new Vector2(1f, 0.5f), new Vector2(-32f, 0f), new Vector2(180f, 70f), FontStyles.Bold,
            TextAlignmentOptions.Right, new Vector2(1f, 0.5f));
        y += ROW_H + ROW_GAP;
    }

    private void Message(string text, Color color)
    {
        var t = SpaceTheme.Text(_body, "Message", text, 30, color, new Vector2(0.5f, 1f), new Vector2(0f, -120f),
            new Vector2(LIST_W, 120f), FontStyles.Bold);
        t.textWrappingMode = TextWrappingModes.Normal;
    }

    private void RetryButton()
    {
        var rt = SpaceTheme.Rect("Retry", _body, new Vector2(0.5f, 1f), new Vector2(0f, -260f), new Vector2(520f, 112f));
        var face = SpaceTheme.Glass(rt, 46f, new Color(0.05f, 0.07f, 0.19f, 0.92f), null, true);
        SpaceTheme.Clickable(rt.gameObject, face, () => LoadAsync().Forget());
        var label = SpaceTheme.Text(rt, "Label", T_RETRY, 32, SpaceTheme.TEXT, FontStyles.Bold);
        SpaceTheme.Stretch(label.rectTransform);
    }

    private void ClearBody()
    {
        foreach (Transform child in _body) Destroy(child.gameObject);
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
