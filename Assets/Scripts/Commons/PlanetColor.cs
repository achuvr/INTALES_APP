using System;

/// <summary>
/// 惑星の色（2026-11-10 ユーザー指定）。赤・青・黄の3色（絵の具の三原色）で、混ざった色の惑星は複数持つ:
/// 紫＝赤＋青、緑＝青＋黄、橙＝赤＋黄、茶色・黒・灰色＝全色。桃色は赤、水色・青緑は青、金色・クリーム色は黄に寄せる。
/// 惑星を見たときの主な色で決める（小さな模様や夜側の暗さは数えない）。惑星を選ぶ画面の一覧の左上に丸で出す。
/// 入店した日には、中央の惑星の色のコマ（SpaceMeeples）を1色につき1個もらえる（2026-11-10。JourneyRules）。
/// Unity を使わない（来店の判定 JourneyRules でも使うため。表示の色は SpacePlanets.cs の PlanetColors）
/// </summary>
[Flags]
public enum PlanetColor
{
    None = 0,
    Red = 1,
    Blue = 2,
    Yellow = 4,
    All = Red | Blue | Yellow,
}

public static class PlanetColorOrder
{
    /// <summary>並べる順（赤・青・黄）。1色ずつ取り出すときもこの順</summary>
    public static readonly PlanetColor[] All = { PlanetColor.Red, PlanetColor.Blue, PlanetColor.Yellow };
}
