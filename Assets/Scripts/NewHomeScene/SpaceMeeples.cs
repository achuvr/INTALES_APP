using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>コマ1色分の定義</summary>
public sealed class SpaceMeepleDef
{
    public PlanetColor Color;
    public string Field;    // users/{uid} のフィールド名
    public string Name;     // 表示名（「赤コマ」など）
    public string IconPath; // Resources の絵（背景は透明）
    public Func<UserData, int> Get;
    public Action<UserData, int> Add;
    /// <summary>何色にでもなるコマ（黒コマ。Color は None。2026-11-12）</summary>
    public bool Wild;
    /// <summary>光・縁の色（色のないコマ用。なければ惑星の色）</summary>
    public Color? TintOverride;

    private Sprite _icon;

    /// <summary>コマの絵（1回だけ読む。見つからなければ null）</summary>
    public Sprite Icon => _icon != null ? _icon : (_icon = Resources.Load<Sprite>(IconPath));

    /// <summary>コマの色（惑星の色の丸・3Dの駒と同じ。黒コマは銀色がかった淡いすみれ色）</summary>
    public Color Tint => TintOverride ?? PlanetColors.Of(Color);
}

/// <summary>
/// コマ（2026-11-10 ユーザー指定「所持品として赤コマ・青コマ・黄コマを追加。その色の惑星で入店したときに、その色のミープルを
/// 1個。複数の色を持つ惑星は、それぞれ1個ずつ」）。
/// 配る判定は JourneyRules.ApplyVisit（その営業日の最初の入店のときだけ。中央の惑星の色 PlanetColor で決める）、
/// 書き込みは JourneyService（入店のトランザクションで FieldValue.Increment）。所持品パネルの「コマ」の欄に数を出す。
/// 今は持つだけで、使い道はまだない。絵は Resources/ItemIcon/Meeple*.png（tools/meeple/meeple_icon.py で作る）
/// </summary>
public static class SpaceMeeples
{
    public const string T_UNIT = "個";

    public static readonly SpaceMeepleDef[] All =
    {
        new SpaceMeepleDef
        {
            Color = PlanetColor.Red, Field = "red_meeple", Name = "赤コマ", IconPath = "ItemIcon/MeepleRed",
            Get = d => d.RedMeeple, Add = (d, n) => d.RedMeeple += n,
        },
        new SpaceMeepleDef
        {
            Color = PlanetColor.Blue, Field = "blue_meeple", Name = "青コマ", IconPath = "ItemIcon/MeepleBlue",
            Get = d => d.BlueMeeple, Add = (d, n) => d.BlueMeeple += n,
        },
        new SpaceMeepleDef
        {
            Color = PlanetColor.Yellow, Field = "yellow_meeple", Name = "黄コマ", IconPath = "ItemIcon/MeepleYellow",
            Get = d => d.YellowMeeple, Add = (d, n) => d.YellowMeeple += n,
        },
    };

    /// <summary>
    /// 黒コマ（2026-11-12 ユーザー指定「切り札座が完成するとショップに黒コマが並ぶ。黒コマは何色にでもなるコマで、
    /// 既に色が決まっているエリアにも置くことが出来る。7Gで購入できる」）。
    /// All（赤・青・黄）には入れない（入店で配る・コマの工房の「各色1個ずつ」・ショップの3色の並びは今までどおり3色のまま）。
    /// INTALES WORLD では、置いた置き場の色のコマとして数える（色が決まっていれば自動でその色。まだ決まっていない置き場と
    /// 星くずの島では、何色として置くかを選ぶ）。置き場の文書は赤・青・黄のどれかで書くので、Firestore のルールは変えていない
    /// </summary>
    public static readonly SpaceMeepleDef Black = new SpaceMeepleDef
    {
        Color = PlanetColor.None, Field = "black_meeple", Name = "黒コマ", IconPath = "ItemIcon/MeepleBlack",
        Get = d => d.BlackMeeple, Add = (d, n) => d.BlackMeeple += n, Wild = true,
        TintOverride = new Color(0.85f, 0.82f, 0.95f),
    };

    /// <summary>黒コマがショップに並ぶ星座（切り札座＝トリックテイキング）</summary>
    public const string BLACK_CONSTELLATION = "trump";

    /// <summary>黒コマの値段（G。2026-11-12 ユーザー指定）</summary>
    public const int BLACK_PRICE = 7;

    /// <summary>黒コマが使えるようになったか（切り札座が完成。管理者は初めから）</summary>
    public static bool BlackUnlocked
    {
        get
        {
            if (AdminClaim.IsAdmin) return true;
            var p = Constellations.Progress().Find(x => x.Def.Id == BLACK_CONSTELLATION);
            return p != null && p.Complete;
        }
    }

    /// <summary>黒コマを出すか（使えるようになった・または持っている）</summary>
    public static bool ShowBlack(UserData d) => BlackUnlocked || (d != null && d.BlackMeeple > 0);

    /// <summary>フィールド名からコマの定義を探す（黒コマも。なければ null）</summary>
    public static SpaceMeepleDef ByField(string field)
    {
        foreach (var m in All)
            if (m.Field == field) return m;
        return Black.Field == field ? Black : null;
    }

    /// <summary>色からコマの定義を探す（1色だけの値で。なければ null）</summary>
    public static SpaceMeepleDef Of(PlanetColor color)
    {
        foreach (var m in All)
            if (m.Color == color) return m;
        return null;
    }

    /// <summary>「赤コマ・青コマ」のように名前をつなげる</summary>
    public static string Names(IEnumerable<PlanetColor> colors)
    {
        var sb = new StringBuilder();
        foreach (var c in colors)
        {
            var def = Of(c);
            if (def == null) continue;
            if (sb.Length > 0) sb.Append('・');
            sb.Append(def.Name);
        }
        return sb.ToString();
    }

    /// <summary>コマの名前と単位（NewHome の起動時にまとめてフォントへ登録する）</summary>
    public static string GlyphText
    {
        get
        {
            var sb = new StringBuilder(T_UNIT + "・+");
            foreach (var m in All) sb.Append(m.Name);
            sb.Append(Black.Name);
            return sb.ToString();
        }
    }
}
