using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>星座ひとつ分の決まり（どのジャンルのゲームで星が灯るか・星の並び）</summary>
public sealed class ConstellationDef
{
    public string Id;
    public string Name;     // 「職人座」
    public string En;       // 英字の小見出し
    /// <summary>図鑑のジャンル名（BoardGameEntry.genre と完全一致）。どれか1つでも持つゲームで星が灯る（1作品で1つだけ）</summary>
    public string[] Genres;
    public string GenreLabel; // 画面に出すジャンル名（長いものは短く）
    public Color Color;
    public Vector2[] Stars; // 星の位置（-1〜1 の正方形の中。上が +）
    public int[] Lines;     // 線で結ぶ星の組（2つずつ）
    /// <summary>完成・レベルアップでもらえるチケット（users のフィールド名。SpaceCoupons の Field）。ふつうはドリンクチケット</summary>
    public string Reward = "drink_coupon";
    public int Size => Stars.Length;
}

/// <summary>
/// 星座ひとつ分の進み具合。6作品で完成（Lv.1）、そのあとも6作品ごとにレベルが上がり、もっと輝く
/// （2026-11-06 ユーザー指定）。
/// </summary>
public sealed class ConstellationProgress
{
    public ConstellationDef Def;
    /// <summary>
    /// そのジャンルの公認されたゲーム（公認された順。全部）。お店から灯した星（StarGiftQr。2026-11-13）も1つずつ入る
    /// （Game は null、Key は StarGiftQr.KeyOf(番号)。見分けは StarGiftQr.IsGiftKey）
    /// </summary>
    public readonly List<(string Key, BoardGameEntry Game, string Day)> Games = new List<(string, BoardGameEntry, string)>();
    public int Count => Games.Count;
    /// <summary>レベル（0=まだ完成していない、1=完成、2〜=さらに6作品ずつ）</summary>
    public int Level => Count / Def.Size;
    /// <summary>いまのレベルから次のレベルまでに灯った星の数（0〜5）</summary>
    public int Partial => Count % Def.Size;
    public bool Complete => Level >= 1;
    /// <summary>いまの1周（6つの星）に入っているゲームの、Games の中での先頭</summary>
    public int RoundStart => Level * Def.Size;
    /// <summary>この公認でちょうどレベルが上がった（6の倍数になった）</summary>
    public bool JustLeveled => Count > 0 && Partial == 0;
}

/// <summary>
/// 星座（2026-11-06 ユーザー決定「星座報酬」）。お店に公認されたゲーム（BoardGameCertify）1つで、
/// そのゲームのジャンルの星座の星が1つ灯る。星座の星の数だけそのジャンルのゲームが公認されると完成。
/// 1つのゲームが2つ以上の星座に当てはまるときは、お客様が1つ選ぶ（灯るのはその1つだけ。2026-11-06 ユーザー指定）。
/// 星は公認された順に、星座の決まった順番（Stars の並び）で灯る。
/// </summary>
public static class Constellations
{
    public const string T_UNIT = "座";
    // 星座の星の数＝完成・レベルアップに要る作品数（ConstellationDef.Size）。
    // 2026-11-06 ユーザー指定: ふつうは6つ。7つは さいころ座（中心の星と「Y」の線で立方体に見せるため）と、
    // 作品が多くほかのジャンルとも重なりやすい 宝石座（セットコレクション）・選び手座（ドラフト）。
    // 8つは 職人座（ミープルの形。脇のくびれまで描くため）
    /// <summary>見た目の輝きはこのレベルで頭打ち（レベル自体は上がり続ける）</summary>
    public const int MAX_GLOW_LEVEL = 5;

    private static Vector2 P(float x, float y) => new Vector2(x, y);

    public static readonly ConstellationDef[] All =
    {
        new ConstellationDef { Id = "artisan", Name = "職人座", En = "ARTISAN", Genres = new[] { "ワーカープレイスメント" },
            GenreLabel = "ワーカープレイスメント", Color = new Color32(0xFF, 0xC0, 0x6A, 0xFF),
            Reward = "seven_coupon", // 職人座だけ7%OFFチケット（2026-11-06 ユーザー指定。星が8つで集めるのが大変なため）
            // ワーカーの駒（ミープル）の形（2026-11-06 ユーザー指定）: 頭→手→脇→足→股→足→脇→手→頭 の輪郭。
            // 脇のくびれがないとミープルに見えないため8つ
            Stars = new[] { P(0f, 0.94f), P(-0.94f, 0.30f), P(0.94f, 0.30f), P(-0.40f, 0.06f), P(0.40f, 0.06f),
                P(-0.70f, -0.92f), P(0.70f, -0.92f), P(0f, -0.36f) },
            Lines = new[] { 0, 1, 1, 3, 3, 5, 5, 7, 7, 6, 6, 4, 4, 2, 2, 0 } },
        new ConstellationDef { Id = "deck", Name = "山札座", En = "DECK", Genres = new[] { "デッキビルディング（デッキ構築）" },
            GenreLabel = "デッキ構築", Color = new Color32(0x9B, 0x7B, 0xFF, 0xFF),
            Stars = new[] { P(-0.62f, 0.72f), P(0.40f, 0.84f), P(0.52f, -0.62f), P(-0.50f, -0.74f), P(-0.04f, 0.04f), P(0.92f, 0.22f) },
            Lines = new[] { 0, 1, 1, 2, 2, 3, 3, 0, 0, 4, 4, 2, 1, 5, 5, 2 } },
        new ConstellationDef { Id = "bond", Name = "きずな座", En = "BOND", Genres = new[] { "協力プレイ" },
            GenreLabel = "協力プレイ", Color = new Color32(0x5C, 0xFF, 0xB8, 0xFF),
            Stars = new[] { P(-0.60f, 0.80f), P(0.60f, 0.80f), P(-0.44f, 0.06f), P(0.44f, 0.06f), P(-0.72f, -0.80f), P(0.72f, -0.80f) },
            Lines = new[] { 0, 2, 1, 3, 2, 3, 2, 4, 3, 5 } },
        new ConstellationDef { Id = "detective", Name = "探偵座", En = "DETECTIVE", Genres = new[] { "推理" },
            GenreLabel = "推理", Color = new Color32(0x5C, 0xE1, 0xFF, 0xFF),
            Stars = new[] { P(-0.34f, 0.76f), P(0.30f, 0.54f), P(0.16f, -0.08f), P(-0.46f, 0.08f), P(0.52f, -0.50f), P(0.90f, -0.90f) },
            Lines = new[] { 0, 1, 1, 2, 2, 3, 3, 0, 2, 4, 4, 5 } },
        new ConstellationDef { Id = "mask", Name = "仮面座", En = "MASK", Genres = new[] { "正体隠匿/隠蔽", "ブラフ" },
            GenreLabel = "正体隠匿・ブラフ", Color = new Color32(0xFF, 0x7A, 0xD9, 0xFF),
            Stars = new[] { P(-0.90f, 0.40f), P(-0.32f, 0.16f), P(0f, 0.50f), P(0.32f, 0.16f), P(0.90f, 0.40f), P(0f, -0.66f) },
            Lines = new[] { 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 0 } },
        new ConstellationDef { Id = "feast", Name = "うたげ座", En = "FEAST", Genres = new[] { "パーティゲーム" },
            GenreLabel = "パーティゲーム", Color = new Color32(0xFF, 0x9E, 0x6B, 0xFF),
            Stars = new[] { P(-0.62f, 0.82f), P(0.62f, 0.82f), P(0f, 0.02f), P(0f, -0.56f), P(-0.50f, -0.86f), P(0.50f, -0.86f) },
            Lines = new[] { 0, 1, 1, 2, 2, 0, 2, 3, 3, 4, 3, 5 } },
        new ConstellationDef { Id = "trump", Name = "切り札座", En = "TRUMP", Genres = new[] { "トリックテイキング" },
            GenreLabel = "トリックテイキング", Color = new Color32(0xFF, 0x6B, 0x8B, 0xFF),
            Stars = new[] { P(0f, 0.92f), P(0.62f, 0.18f), P(0f, -0.46f), P(-0.62f, 0.18f), P(-0.34f, -0.90f), P(0.34f, -0.90f) },
            Lines = new[] { 0, 1, 1, 2, 2, 3, 3, 0, 2, 4, 2, 5, 4, 5 } },
        new ConstellationDef { Id = "banner", Name = "旗座", En = "BANNER", Genres = new[] { "エリアマジョリティ（陣取り）" },
            GenreLabel = "陣取り", Color = new Color32(0xFF, 0xD3, 0x7A, 0xFF),
            Stars = new[] { P(-0.52f, -0.92f), P(-0.52f, 0.06f), P(-0.52f, 0.90f), P(0.72f, 0.76f), P(0.60f, 0.24f), P(0.06f, 0.40f) },
            Lines = new[] { 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 1 } },
        new ConstellationDef { Id = "dice", Name = "さいころ座", En = "DICE", Genres = new[] { "ダイスロール" },
            GenreLabel = "ダイスロール", Color = new Color32(0xEE, 0xF2, 0xFF, 0xFF),
            Stars = new[] { P(0f, 0.92f), P(0.80f, 0.46f), P(0.80f, -0.46f), P(0f, -0.92f), P(-0.80f, -0.46f), P(-0.80f, 0.46f), P(0f, 0f) },
            Lines = new[] { 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 0, 6, 1, 6, 5, 6, 3 } },
        new ConstellationDef { Id = "tile", Name = "タイル座", En = "TILE", Genres = new[] { "タイル・カード配置" },
            GenreLabel = "タイル・カード配置", Color = new Color32(0x7B, 0xC8, 0xFF, 0xFF),
            Stars = new[] { P(-0.74f, 0.50f), P(0f, 0.62f), P(0.74f, 0.50f), P(-0.74f, -0.42f), P(0f, -0.30f), P(0.74f, -0.42f) },
            Lines = new[] { 0, 1, 1, 2, 3, 4, 4, 5, 0, 3, 1, 4, 2, 5 } },
        new ConstellationDef { Id = "coin", Name = "金貨座", En = "COIN", Genres = new[] { "経済/経営" },
            GenreLabel = "経済・経営", Color = new Color32(0xFF, 0xE0, 0x5A, 0xFF),
            Stars = new[] { P(0f, 0.86f), P(0.82f, 0.26f), P(0.50f, -0.70f), P(-0.50f, -0.70f), P(-0.82f, 0.26f), P(0f, 0f) },
            Lines = new[] { 0, 1, 1, 2, 2, 3, 3, 4, 4, 0 } },

        // ---- 2026-11-06 追加（星座を偶数の14個に） ----
        new ConstellationDef { Id = "gem", Name = "宝石座", En = "GEM", Genres = new[] { "セットコレクション" },
            GenreLabel = "セットコレクション", Color = new Color32(0xB6, 0xF3, 0x6B, 0xFF),
            Stars = new[] { P(-0.44f, 0.72f), P(0.44f, 0.72f), P(0.86f, 0.22f), P(0f, -0.90f), P(-0.86f, 0.22f), P(-0.30f, 0.22f), P(0.30f, 0.22f) },
            Lines = new[] { 0, 1, 1, 2, 2, 3, 3, 4, 4, 0, 4, 5, 5, 6, 6, 2, 0, 5, 1, 6, 5, 3, 6, 3 } },
        new ConstellationDef { Id = "draft", Name = "選び手座", En = "DRAFT", Genres = new[] { "場札の獲得（ドラフト / リミテッド）" },
            GenreLabel = "ドラフト", Color = new Color32(0xFF, 0xA6, 0xC9, 0xFF),
            Stars = new[] { P(0f, -0.86f), P(-0.84f, 0.22f), P(-0.58f, 0.62f), P(-0.20f, 0.88f), P(0.20f, 0.88f), P(0.58f, 0.62f), P(0.84f, 0.22f) },
            Lines = new[] { 0, 1, 0, 2, 0, 3, 0, 4, 0, 5, 0, 6 } },
        new ConstellationDef { Id = "piece", Name = "ピース座", En = "PIECE", Genres = new[] { "パズル" },
            GenreLabel = "パズル", Color = new Color32(0x8F, 0xA8, 0xFF, 0xFF),
            // L字のピース（2026-11-06 ユーザー指定）
            Stars = new[] { P(-0.60f, 0.92f), P(0f, 0.92f), P(0f, -0.30f), P(0.60f, -0.30f), P(0.60f, -0.92f), P(-0.60f, -0.92f) },
            Lines = new[] { 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 0 } },

        // ---- 2026-11-07 追加（アブストラクト・アクションゲームで16個に。ユーザー指定）----
        // 末尾に足すこと: 選んだ記録のないゲームは「当てはまる最初の星座」に灯るので、前に入れると既存の灯りが動く
        new ConstellationDef { Id = "board", Name = "盤座", En = "BOARD", Genres = new[] { "アブストラクト" },
            GenreLabel = "アブストラクト", Color = new Color32(0xD9, 0xA6, 0xFF, 0xFF),
            // ななめに見た盤（ひし形）と、まん中を横切る1本の線
            Stars = new[] { P(0f, 0.92f), P(0.92f, 0f), P(0f, -0.92f), P(-0.92f, 0f), P(-0.46f, 0.46f), P(0.46f, -0.46f) },
            Lines = new[] { 0, 1, 1, 2, 2, 3, 3, 4, 4, 0, 4, 5 } },
        new ConstellationDef { Id = "block", Name = "積み木座", En = "BLOCK", Genres = new[] { "アクションゲーム" },
            GenreLabel = "アクションゲーム", Color = new Color32(0xFF, 0xF2, 0x8A, 0xFF),
            // 積み木（2026-11-07 ユーザー指定。稲妻から変更）: 平たい積み木の上に、角を下にした積み木が
            // 1点で乗ってバランスをとっている形。四角を2つ積んだ形はピース座（L字）と似るため不採用（同日）。
            // 下の積み木の4つ＋上の積み木の4つで8つ
            Stars = new[] { P(-0.90f, -0.90f), P(0.90f, -0.90f), P(0.90f, -0.46f), P(-0.90f, -0.46f),
                P(0f, -0.46f), P(0.66f, 0.20f), P(0f, 0.86f), P(-0.66f, 0.20f) },
            Lines = new[] { 0, 1, 1, 2, 2, 4, 4, 3, 3, 0, 4, 5, 5, 6, 6, 7, 7, 4 } },
    };

    public static int TotalStars => All.Sum(c => c.Size);

    /// <summary>今の公認の記録から、全部の星座の進み具合を作る</summary>
    public static List<ConstellationProgress> Progress()
    {
        var result = All.Select(d => new ConstellationProgress { Def = d }).ToList();
        // 公認された順（日付 → キー）
        var certified = BoardGameCertify.All
            .OrderBy(kv => kv.Value, StringComparer.Ordinal)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .ToList();
        var byId = result.ToDictionary(p => p.Def.Id);
        foreach (var kv in certified)
        {
            var g = BoardGameCertify.Find(kv.Key);
            if (g?.genre == null) continue;
            foreach (var (def, day) in LitBy(kv.Key, g, kv.Value))
                byId[def.Id].Games.Add((kv.Key, g, day));
        }
        // お店から灯した星（管理者が星座と個数を決めて出すQR。2026-11-13 StarGiftQr）: 個数ぶん、灯した日に入る（ゲームはない）
        foreach (var gift in StarGiftQr.Gifts)
            if (byId.TryGetValue(gift.Id, out var gp))
                for (int i = 0; i < gift.Count; i++) gp.Games.Add((StarGiftQr.KeyOf(gift.Nonce), null, gift.Day));
        // もう一度灯した星は灯した日に入るので、星座ごとに灯した順（日付 → キー）に並べ直す
        foreach (var p in result)
            p.Games.Sort((a, b) =>
            {
                int c = string.CompareOrdinal(a.Day, b.Day);
                return c != 0 ? c : string.CompareOrdinal(a.Key, b.Key);
            });
        return result;
    }

    /// <summary>
    /// そのゲームが星を灯している星座と、灯した日。選んだ記録のうち、いまも当てはまる星座だけ。
    /// 記録が1つもない（または全部もう当てはまらない）ときは、当てはまる最初の星座に達成日で灯っているとみなす
    /// </summary>
    public static List<(ConstellationDef Def, string Day)> LitBy(string key, BoardGameEntry g, string certDay)
    {
        var list = new List<(ConstellationDef, string)>();
        // 同じゲームで同じ星座に何度でも灯せる（2026-11-09 ユーザー指定。1日1回まで）ので、重ねて数える
        foreach (var (id, day) in BoardGameCertify.StarsOf(key))
        {
            var def = All.FirstOrDefault(d => d.Id == id);
            if (def == null || !Matches(def, g)) continue;
            list.Add((def, day));
        }
        if (list.Count == 0)
        {
            var first = All.FirstOrDefault(d => Matches(d, g));
            if (first != null) list.Add((first, certDay));
        }
        return list;
    }

    /// <summary>そのゲームが入る星座（ジャンルが合うもの）</summary>
    public static List<ConstellationDef> Of(BoardGameEntry g)
    {
        var list = new List<ConstellationDef>();
        if (g?.genre == null) return list;
        foreach (var d in All)
            if (Matches(d, g)) list.Add(d);
        return list;
    }

    /// <summary>そのゲームが星座のジャンルのどれかを持つか</summary>
    public static bool Matches(ConstellationDef d, BoardGameEntry g)
    {
        if (g?.genre == null) return false;
        foreach (var genre in d.Genres)
            if (Array.IndexOf(g.genre, genre) >= 0) return true;
        return false;
    }

    public static string GlyphText
    {
        get
        {
            var sb = new StringBuilder(T_UNIT);
            foreach (var c in All) sb.Append(c.Name).Append(c.En).Append(c.GenreLabel);
            return sb.ToString();
        }
    }
}
