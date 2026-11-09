using System;
using System.Collections.Generic;
using System.Text;
using Cysharp.Threading.Tasks;
using Firebase.Firestore;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 会員証QR（MemberCard）を管理者が読んだときの画面（2026-11-07）。お会計で見るものを一画面に並べる:
///   ・名前・来店回数・誕生月（今月なら目立たせる）
///   ・1か月パス（今日使えるか・期限）
///   ・持っているクーポン（枚数）
///   ・中央の惑星の★お店の特典と、今使えるか（お客様の端末で計算した状態を QR で受け取る）
/// 見るだけ（クーポンを使うのは、これまでどおりお客様のアプリの所持品から）。
/// users と passes の読み取りは管理者だけに許している（tools/firestore/firestore.rules）
/// </summary>
public static class SpaceMemberView
{
    private const float W = 960f;
    private const string T_CAPTION = "MEMBER";
    private const string T_LOADING = "読み込んでいます…";
    private const string T_FAILED = "読み込めませんでした\n通信環境を確認してもう一度読み取ってください";
    private const string T_VISITS = "来店";
    private const string T_BIRTH = "誕生月";
    private const string T_BIRTH_NOW = "今月が誕生月です";
    private const string T_PASS = "1か月パス";
    private const string T_NO_PASS = "なし";
    private const string T_TODAY_OK = "今日使えます";
    private const string T_TODAY_NG = "今日は使えません";
    private const string T_COUPONS = "クーポン";
    private const string T_NO_COUPON = "なし";
    private const string T_PERK = "★お店の特典";
    private const string T_NO_PERK = "なし（中央の惑星に★特典がない）";
    private const string T_CLOSE = "閉じる";

    public static string GlyphText => T_CAPTION + T_LOADING + T_FAILED + T_VISITS + T_BIRTH + T_BIRTH_NOW + T_PASS
                                      + T_NO_PASS + T_TODAY_OK + T_TODAY_NG + T_COUPONS + T_NO_COUPON + T_PERK + T_NO_PERK
                                      + T_CLOSE + "月回枚まで・：" + PerkLabelsText;

    private static string PerkLabelsText =>
        "使えます今の公転は使用済み入店していないときは使えない曜日が違う時間が違う季節が違う";

    private static string PerkLabel(PerkState s) => s switch
    {
        PerkState.Ok => "使えます",
        PerkState.Used => "今の公転は使用済み",
        PerkState.NotInStore => "入店していないときは使えない",
        PerkState.WrongDay => "曜日が違う",
        PerkState.WrongTime => "時間が違う",
        PerkState.WrongSeason => "季節が違う",
        _ => "",
    };

    public static void Show(MemberCard.Info info)
    {
        var canvas = SpaceTheme.MainCanvas();
        var root = SpaceTheme.Rect("__MemberView", canvas.transform);
        SpaceTheme.Stretch(root);
        root.SetAsLastSibling();
        var go = root.gameObject;
        void Close()
        {
            if (go != null) UnityEngine.Object.Destroy(go);
        }

        var dim = SpaceTheme.Rect("Dim", root);
        SpaceTheme.Stretch(dim);
        SpaceTheme.Img(dim, null, new Color(0.01f, 0.015f, 0.05f, 0.95f), true);
        var safe = SpaceTheme.Rect("Safe", root);
        SpaceTheme.Stretch(safe);
        safe.gameObject.AddComponent<SpaceSafeArea>();

        var card = SpaceTheme.Rect("Card", safe, SpaceTheme.CENTER, Vector2.zero, new Vector2(W, 600f));
        SpaceTheme.Glass(card, 56f, SpaceTheme.GLASS_DEEP, SpaceTheme.A(SpaceTheme.GOLD, 0.5f), true);
        var loading = SpaceTheme.Text(card, "Loading", T_LOADING, 34, SpaceTheme.TEXT_SUB, FontStyles.Bold);
        SpaceTheme.Stretch(loading.rectTransform);
        go.AddComponent<SpacePopIn>().Target = card;

        LoadAsync(info, card, loading, Close).Forget();
    }

    private static async UniTaskVoid LoadAsync(MemberCard.Info info, RectTransform card, TMP_Text loading, Action close)
    {
        UserData data = null;
        List<PassInfo> passes = null;
        try
        {
            var db = FirebaseFirestore.DefaultInstance;
            var snapTask = db.Collection("users").Document(info.Uid).GetSnapshotAsync(Source.Server).AsUniTask();
            var passTask = StorePasses.LoadOfAsync(info.Uid);
            var snap = await snapTask;
            passes = await passTask;
            if (snap.Exists) data = snap.ConvertTo<UserData>();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Member] 会員の読み込みに失敗 ({info.Uid}): {ex.Message}");
        }
        if (card == null) return;
        if (data == null)
        {
            loading.text = T_FAILED;
            loading.textWrappingMode = TextWrappingModes.Normal;
            AddClose(card, 300f, close);
            return;
        }
        UnityEngine.Object.Destroy(loading.gameObject);
        Build(card, info, data, passes ?? new List<PassInfo>(), close);
    }

    private static void Build(RectTransform card, MemberCard.Info info, UserData d, List<PassInfo> passes, Action close)
    {
        float y = 56f;
        var cap = SpaceTheme.Text(card, "Caption", T_CAPTION, 24, SpaceTheme.A(SpaceTheme.GOLD, 0.95f),
            new Vector2(0.5f, 1f), new Vector2(0f, -y - 18f), new Vector2(W - 80f, 36f), FontStyles.Bold);
        SpaceTheme.Tracking(cap, 18f);
        y += 44f;
        var name = SpaceTheme.Text(card, "Name", SpaceTheme.NoParse(SpaceTheme.Safe(d.Username ?? "")) + " さん", 56,
            SpaceTheme.TEXT, new Vector2(0.5f, 1f), new Vector2(0f, -y - 36f), new Vector2(W - 80f, 72f), FontStyles.Bold);
        name.enableAutoSizing = true;
        name.fontSizeMin = 32f;
        name.fontSizeMax = 56f;
        y += 84f;

        // 来店回数・誕生月
        var journey = JourneyData.From(d.JourneyMap);
        int visits = journey != null ? journey.Visits : 0;
        int month = StoreCalendar.TodayBusiness.Month;
        bool birthNow = d.BirthMonth == month;
        string sub = $"{T_VISITS} {visits}回";
        if (d.BirthMonth >= 1 && d.BirthMonth <= 12) sub += $"　{T_BIRTH} {d.BirthMonth}月";
        SpaceTheme.Text(card, "Sub", sub, 30, SpaceTheme.TEXT_SUB, new Vector2(0.5f, 1f), new Vector2(0f, -y - 22f),
            new Vector2(W - 80f, 44f), FontStyles.Bold);
        y += 48f;
        if (birthNow)
        {
            SpaceTheme.Text(card, "Birth", "★ " + T_BIRTH_NOW, 30, SpaceTheme.PINK, new Vector2(0.5f, 1f),
                new Vector2(0f, -y - 22f), new Vector2(W - 80f, 44f), FontStyles.Bold);
            y += 48f;
        }
        y += 16f;

        // パス
        var today = StoreCalendar.TodayBusiness;
        var lines = new List<(string label, string value, Color color)>();
        var active = passes.FindAll(p => p.IsActive(today));
        if (active.Count == 0) lines.Add((T_PASS, T_NO_PASS, SpaceTheme.TEXT_MUTED));
        foreach (var p in active)
        {
            bool ok = StorePasses.UsableOn(p, today);
            lines.Add((T_PASS, $"{p.Type.Label}（{StorePasses.DateText(p.Expiry)}まで）・{(ok ? T_TODAY_OK : T_TODAY_NG)}",
                ok ? SpaceTheme.MINT : SpaceTheme.DANGER));
        }

        // クーポン
        var sb = new StringBuilder();
        foreach (var c in SpaceCoupons.All)
        {
            int n = c.Get(d);
            if (n <= 0) continue;
            if (sb.Length > 0) sb.Append("\n");
            sb.Append(c.Name).Append(" ×").Append(n).Append("枚");
        }
        lines.Add((T_COUPONS, sb.Length > 0 ? sb.ToString() : T_NO_COUPON, sb.Length > 0 ? SpaceTheme.TEXT : SpaceTheme.TEXT_MUTED));

        // ★お店の特典（惑星の能力）は 2026-11-09 にやめたので出さない

        foreach (var (label, value, color) in lines)
        {
            int rows = 1;
            foreach (char ch in value) if (ch == '\n') rows++;
            float h = 24f + rows * 42f;
            var row = SpaceTheme.Rect("Row", card, new Vector2(0.5f, 1f), new Vector2(0f, -y - h / 2f), new Vector2(W - 80f, h));
            SpaceTheme.Rounded(row, new Color(0.07f, 0.09f, 0.24f, 0.7f), 24f);
            SpaceTheme.Text(row, "Label", label, 26, SpaceTheme.TEXT_SUB, new Vector2(0f, 1f), new Vector2(28f, -33f),
                new Vector2(220f, 40f), FontStyles.Bold, TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
            var v = SpaceTheme.Text(row, "Value", value, 30, color, new Vector2(0f, 1f), new Vector2(250f, -12f),
                new Vector2(W - 80f - 280f, rows * 42f), FontStyles.Bold, TextAlignmentOptions.TopLeft, new Vector2(0f, 1f));
            v.enableAutoSizing = true;
            v.fontSizeMin = 20f;
            v.fontSizeMax = 30f;
            y += h + 12f;
        }

        y += 20f;
        AddClose(card, y, close);
        card.sizeDelta = new Vector2(W, y + 112f + 48f);
    }

    private static void AddClose(RectTransform card, float y, Action close)
    {
        var btn = SpaceTheme.Rect("Close", card, new Vector2(0.5f, 0f), new Vector2(0f, 48f), new Vector2(480f, 112f),
            new Vector2(0.5f, 0f));
        var face = SpaceTheme.PrimaryFace(btn);
        SpaceTheme.Clickable(btn.gameObject, face, () => close());
        var t = SpaceTheme.Text(btn, "Label", T_CLOSE, 38, SpaceTheme.TEXT_DARK, FontStyles.Bold);
        SpaceTheme.Stretch(t.rectTransform);
    }
}
