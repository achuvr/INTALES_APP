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

    private Sprite _icon;

    /// <summary>コマの絵（1回だけ読む。見つからなければ null）</summary>
    public Sprite Icon => _icon != null ? _icon : (_icon = Resources.Load<Sprite>(IconPath));

    /// <summary>コマの色（惑星の色の丸・3Dの駒と同じ）</summary>
    public Color Tint => PlanetColors.Of(Color);
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
            return sb.ToString();
        }
    }
}
