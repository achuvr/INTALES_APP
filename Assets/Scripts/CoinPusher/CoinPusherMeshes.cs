using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// コインプッシャー（2026-11-12）の 3D の形（すべてコードで作る）。台に乗る物:
///  ・コイン（G）… 円柱。上下の面に金貨の絵（Resources/CoinPusher/CoinFace。tools/coin/make_coin_face.py）、ふちは金色
///  ・コマ … ミープルの輪郭（tools/meeple/meeple_outline.py で取り出した64点）に厚みをつけた板。台の上に寝かせて置く
///  ・黒コマ … コマと同じ形で、黒い面・虹色に光るふち・胸の白い星（所持品の絵 ItemIcon/MeepleBlack と同じ見た目）
///  ・星のカケラ … 星の5つの先のうちの1つ（中心・内側の2点・先端の4点の凧形）に厚みをつけた板
///  ・惑星回復薬 … 丸いフラスコの形の板。ガラス・栓と、面に描いた水色〜すみれ色の薬と輪のある惑星（絵 ItemIcon/PlanetPotion と同じ）
/// 光る部分（黒コマのふち・薬）は頂点の色で塗り、光らない材質（Sprites/Default）のサブメッシュにする。
/// 形はアプリを起動しているあいだ使い回す（台を閉じても消さない。材質は CoinPusherWorld が作って片づける）
/// </summary>
public static class CoinPusherMeshes
{
    public const float COIN_R = 0.4f;    // コインの半径
    public const float COIN_H = 0.12f;   // コインの厚み
    public const float COIN_CHAMFER = 0.025f; // 当たり判定で、ふちの上下を削る幅（縦に立ちにくくする）
    // コマの大きさ。2026-11-13 ユーザー指定「ミープルの大きさを今の0.6倍に」で、形は変えずに背の高さ1.0・厚み0.4 を0.6倍にした
    // （重さ CoinPusherWorld.Spawn はそのまま）。同時にコインも0.85倍（のち0.86倍）にしたが、「難しすぎた」で同日元の大きさに戻した
    public const float MEEPLE_H = 0.6f;     // コマの背の高さ（寝かせるので、台の上では奥行き）
    public const float MEEPLE_T = 0.24f;    // コマの厚み（寝かせるので、台の上では高さ）
    public const float STAR_L = 1.05f;   // 星のカケラの長さ（星の中心から先端まで）
    public const float STAR_T = 0.22f;   // 星のカケラの厚み
    public const float POTION_L = 0.98f; // 惑星回復薬の長さ（瓶の底から栓の上まで。寝かせるので、台の上では奥行き）
    public const float POTION_T = 0.34f; // 惑星回復薬の厚み（寝かせるので、台の上では高さ）

    // ミープルの輪郭（高さ1・足もとが0・左右の中心が0・反時計回り。tools/meeple/meeple_outline.py tools/meeple out.json 64）
    private static readonly float[] MEEPLE_OUTLINE =
    {
        -0.0719f, 0.9831f, -0.1235f, 0.9426f, -0.1549f, 0.8857f, -0.1609f, 0.8190f, -0.1402f, 0.7569f, -0.1450f, 0.7028f,
        -0.2105f, 0.6901f, -0.2769f, 0.6793f, -0.3433f, 0.6682f, -0.4096f, 0.6568f, -0.4701f, 0.6331f, -0.5077f, 0.5798f,
        -0.4996f, 0.5160f, -0.4531f, 0.4701f, -0.3889f, 0.4528f, -0.3218f, 0.4457f, -0.2548f, 0.4395f, -0.2328f, 0.3839f,
        -0.2468f, 0.3188f, -0.2736f, 0.2592f, -0.3015f, 0.2000f, -0.3325f, 0.1424f, -0.3622f, 0.0845f, -0.3673f, 0.0213f,
        -0.3076f, 0.0000f, -0.2392f, 0.0000f, -0.1708f, 0.0000f, -0.1064f, 0.0118f, -0.0799f, 0.0711f, -0.0605f, 0.1345f,
        -0.0390f, 0.1972f, 0.0074f, 0.2344f, 0.0441f, 0.1829f, 0.0651f, 0.1198f, 0.0838f, 0.0563f, 0.1191f, 0.0043f,
        0.1865f, 0.0000f, 0.2548f, 0.0000f, 0.3231f, 0.0004f, 0.3724f, 0.0354f, 0.3556f, 0.0983f, 0.3248f, 0.1558f,
        0.2944f, 0.2137f, 0.2669f, 0.2730f, 0.2414f, 0.3333f, 0.2312f, 0.3996f, 0.2696f, 0.4426f, 0.3373f, 0.4466f,
        0.4040f, 0.4559f, 0.4658f, 0.4780f, 0.5055f, 0.5298f, 0.5020f, 0.5939f, 0.4574f, 0.6419f, 0.3938f, 0.6595f,
        0.3275f, 0.6708f, 0.2611f, 0.6819f, 0.1947f, 0.6927f, 0.1322f, 0.7110f, 0.1469f, 0.7703f, 0.1618f, 0.8347f,
        0.1494f, 0.9000f, 0.1126f, 0.9541f, 0.0577f, 0.9893f, -0.0081f, 0.9999f,
    };

    private static Mesh _coin, _coinCollider, _meeple, _star, _meepleBlack, _potion, _potionCollider;

    // ピースパズルのピース（2026-11-13。ピース座が完成した人のボーナスの景品）: マス1つの大きさ・厚み・見た目のマスのすき間。
    // いちばん長い棒（4マス）でも長さ 1.08 で、景品を落とす範囲の目安（CoinPusherWorld.ITEM_HALF＝半分 0.55）に収まる
    public const float PIECE_CELL = 0.27f;
    public const float PIECE_T = 0.15f;
    private const float PIECE_GAP = 0.025f;
    private static readonly Mesh[] _pieces = new Mesh[PuzzleRules.TYPES + 1];
    private static Mesh _pieceCell;

    /// <summary>
    /// ピースの見た目（種類 1〜8）: マスごとの箱を並べた平たい形（マスの間はほんの少しすき間を空けて、マスの形が分かるように）。
    /// 原点は形のまん中
    /// </summary>
    public static Mesh Piece(int type)
    {
        if (type < 1 || type > PuzzleRules.TYPES) type = 1;
        if (_pieces[type] != null) return _pieces[type];
        if (_pieceCell == null)
            _pieceCell = Box(new Vector3(PIECE_CELL - PIECE_GAP, PIECE_T, PIECE_CELL - PIECE_GAP), Color.white);
        var centers = PieceCellCenters(type);
        var parts = new CombineInstance[centers.Count];
        for (int i = 0; i < centers.Count; i++)
            parts[i] = new CombineInstance { mesh = _pieceCell, transform = Matrix4x4.Translate(centers[i]) };
        var mesh = new Mesh { name = "PusherPiece" + type };
        mesh.CombineMeshes(parts, true, true);
        mesh.RecalculateBounds();
        _pieces[type] = mesh;
        return mesh;
    }

    /// <summary>ピースのマスの中心（形のまん中を原点にした、平たく置いたときの x・z。当たり判定の箱もここに置く）</summary>
    public static List<Vector3> PieceCellCenters(int type)
    {
        var list = new List<Vector3>();
        var def = PuzzleRules.Piece(type);
        if (def == null) return list;
        int maxX = 0, maxY = 0;
        foreach (var (x, y) in def.Cells)
        {
            if (x > maxX) maxX = x;
            if (y > maxY) maxY = y;
        }
        float ox = maxX * 0.5f, oy = maxY * 0.5f;
        foreach (var (x, y) in def.Cells) list.Add(new Vector3((x - ox) * PIECE_CELL, 0f, (oy - y) * PIECE_CELL));
        return list;
    }

    /// <summary>コインの見た目（サブメッシュ0＝上下の面・1＝ふち）</summary>
    public static Mesh Coin => _coin != null ? _coin : (_coin = BuildCoin(32));

    /// <summary>
    /// コインの当たり判定（凸の MeshCollider に使う）。16角の円盤で、ふちの上下を少し削り、ふちのまん中がいちばん外になる形。
    /// 2026-11-12 ユーザー指定「コインは縦に立つと使いにくいので、縦になりにくく」: それまでの10角柱は、ふちの平らな面
    /// （幅0.25）で立つと8°傾くまで倒れず、縦のまま安定していた。この形なら立ってもふちの線1本でしか支えられず、すぐ倒れる
    /// </summary>
    public static Mesh CoinCollider => _coinCollider != null ? _coinCollider : (_coinCollider = BuildCoinHull(16, COIN_R, COIN_H, COIN_CHAMFER));

    /// <summary>寝かせたコマ（輪郭は x と z、厚みは y。中心が原点）</summary>
    public static Mesh Meeple => _meeple != null ? _meeple : (_meeple = BuildMeeple());

    /// <summary>星のカケラ（凧形の板。中心が原点）</summary>
    public static Mesh Star => _star != null ? _star : (_star = BuildStar());

    /// <summary>
    /// 寝かせた黒コマ（2026-11-12 追加。コインタワーのボーナス）。形は Meeple と同じで、サブメッシュ0＝上下の面（黒い材質）・
    /// 1＝虹色のふちと胸の白い星（頂点の色。光らない材質）。当たり判定は Meeple を使う
    /// </summary>
    public static Mesh MeepleBlack => _meepleBlack != null ? _meepleBlack : (_meepleBlack = BuildMeepleBlack());

    /// <summary>
    /// 寝かせた惑星回復薬（2026-11-12 追加。ボーナスの景品）。サブメッシュ0＝ガラスの瓶・1＝栓・
    /// 2＝面に描いた薬と惑星（頂点の色。光らない材質）。瓶の底が -z・栓が +z、中心（長さのまん中）が原点
    /// </summary>
    public static Mesh Potion => _potion != null ? _potion : (_potion = BuildPotion(true));

    /// <summary>惑星回復薬の当たり判定（ガラスと栓だけ。凸の MeshCollider に使う）</summary>
    public static Mesh PotionCollider => _potionCollider != null ? _potionCollider : (_potionCollider = BuildPotion(false));

    // ================================================================
    // コイン
    // ================================================================
    private static Mesh BuildCoin(int seg)
    {
        var v = new List<Vector3>();
        var n = new List<Vector3>();
        var uv = new List<Vector2>();
        var caps = new List<int>();
        var rim = new List<int>();
        float h = COIN_H * 0.5f;
        foreach (float side in new[] { 1f, -1f })
        {
            var normal = new Vector3(0f, side, 0f);
            int center = v.Count;
            v.Add(new Vector3(0f, h * side, 0f));
            n.Add(normal);
            uv.Add(new Vector2(0.5f, 0.5f));
            for (int i = 0; i < seg; i++)
            {
                float a = i * Mathf.PI * 2f / seg;
                v.Add(new Vector3(Mathf.Cos(a) * COIN_R, h * side, Mathf.Sin(a) * COIN_R));
                n.Add(normal);
                // 下の面は左右を反転して貼る（裏から見ても「G」が読めるように）
                uv.Add(new Vector2(0.5f + 0.5f * Mathf.Cos(a) * side, 0.5f + 0.5f * Mathf.Sin(a)));
            }
            for (int i = 0; i < seg; i++)
                Tri(caps, v, center, center + 1 + i, center + 1 + (i + 1) % seg, normal);
        }
        int ring = v.Count;
        for (int i = 0; i <= seg; i++)
        {
            float a = i * Mathf.PI * 2f / seg;
            var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            v.Add(dir * COIN_R + Vector3.up * h);
            v.Add(dir * COIN_R - Vector3.up * h);
            n.Add(dir);
            n.Add(dir);
            uv.Add(new Vector2(i / (float)seg, 1f));
            uv.Add(new Vector2(i / (float)seg, 0f));
        }
        for (int i = 0; i < seg; i++)
        {
            int t0 = ring + i * 2, b0 = t0 + 1, t1 = t0 + 2, b1 = t0 + 3;
            var dir = (n[t0] + n[t1]).normalized;
            Tri(rim, v, t0, t1, b1, dir);
            Tri(rim, v, t0, b1, b0, dir);
        }
        var mesh = new Mesh { name = "PusherCoin" };
        mesh.SetVertices(v);
        mesh.SetNormals(n);
        mesh.SetUVs(0, uv);
        mesh.subMeshCount = 2;
        mesh.SetTriangles(caps, 0);
        mesh.SetTriangles(rim, 1);
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>
    /// 当たり判定用の、ふちの上下を削った円盤（見た目には使わない）。上の輪（半径 r-chamfer）・まん中の輪（半径 r）・
    /// 下の輪（半径 r-chamfer）の3段。縦に立つと、まん中の輪の辺1本だけが床に触れる
    /// （上下の輪は床から chamfer 離れる。接触の余白 0.01 より大きいので、そこは床に触れない）
    /// </summary>
    private static Mesh BuildCoinHull(int seg, float r, float height, float chamfer)
    {
        var v = new List<Vector3>();
        var t = new List<int>();
        float h = height * 0.5f;
        for (int i = 0; i < seg; i++)
        {
            float a = i * Mathf.PI * 2f / seg;
            var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            v.Add(dir * (r - chamfer) + Vector3.up * h); // 3i   上の輪
            v.Add(dir * r);                              // 3i+1 まん中の輪
            v.Add(dir * (r - chamfer) - Vector3.up * h); // 3i+2 下の輪
        }
        for (int i = 1; i < seg - 1; i++)
        {
            Tri(t, v, 0, i * 3, (i + 1) * 3, Vector3.up);
            Tri(t, v, 2, i * 3 + 2, (i + 1) * 3 + 2, Vector3.down);
        }
        for (int i = 0; i < seg; i++)
        {
            int j = (i + 1) % seg;
            int t0 = i * 3, m0 = t0 + 1, b0 = t0 + 2, t1 = j * 3, m1 = t1 + 1, b1 = t1 + 2;
            var dir = v[m0] + v[m1];
            dir.y = 0f;
            dir.Normalize();
            Tri(t, v, t0, t1, m1, dir + Vector3.up * 0.4f);   // 上の帯（外向き・少し上向き）
            Tri(t, v, t0, m1, m0, dir + Vector3.up * 0.4f);
            Tri(t, v, m0, m1, b1, dir + Vector3.down * 0.4f); // 下の帯（外向き・少し下向き）
            Tri(t, v, m0, b1, b0, dir + Vector3.down * 0.4f);
        }
        var mesh = new Mesh { name = "PusherCoinCollider" };
        mesh.SetVertices(v);
        mesh.SetTriangles(t, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    // ================================================================
    // コマ（寝かせたミープル）
    // ================================================================
    /// <summary>ミープルの輪郭（x＝左右・y＝台の上での奥行き。頭が +・足が -、背の高さのまん中が 0）</summary>
    private static Vector2[] MeeplePoints()
    {
        int count = MEEPLE_OUTLINE.Length / 2;
        var pts = new Vector2[count];
        for (int i = 0; i < count; i++)
            pts[i] = new Vector2(MEEPLE_OUTLINE[i * 2] * MEEPLE_H, (MEEPLE_OUTLINE[i * 2 + 1] - 0.5f) * MEEPLE_H);
        return pts;
    }

    private static Mesh BuildMeeple() => Extrude(MeeplePoints(), MEEPLE_T, true, "PusherMeeple");

    // ================================================================
    // 黒コマ（2026-11-12）
    // ================================================================
    // ふちの虹色: 頭のてっぺんから時計回りの角度（度）と色相の組。絵（ItemIcon/MeepleBlack）のふちと同じく
    // 頭が青 → 右上が紫 → 右腕が赤 → 右足が橙 → 左足が黄 → 左腕が緑 → 頭の青へ戻る
    private static readonly float[] RIM_HUES = { 0f, 0.62f, 45f, 0.75f, 90f, 0.97f, 150f, 1.07f, 210f, 1.14f, 270f, 1.32f, 360f, 1.62f };

    private static Color RimColor(Vector2 p)
    {
        float deg = Mathf.Atan2(p.x, p.y) * Mathf.Rad2Deg; // 頭（+y）が 0・右（+x）が 90
        if (deg < 0f) deg += 360f;
        float hue = RIM_HUES[1];
        for (int i = 0; i + 3 < RIM_HUES.Length; i += 2)
        {
            if (deg > RIM_HUES[i + 2]) continue;
            hue = Mathf.Lerp(RIM_HUES[i + 1], RIM_HUES[i + 3], Mathf.InverseLerp(RIM_HUES[i], RIM_HUES[i + 2], deg));
            break;
        }
        return Color.HSVToRGB(Mathf.Repeat(hue, 1f), 0.72f, 1f);
    }

    /// <summary>
    /// 黒コマ: コマの形に厚みをつけ、上下の面（サブメッシュ0。黒い材質）と、虹色の側面・面のふちの虹色の帯・胸の白い星
    /// （サブメッシュ1。頂点の色で光らない材質）。帯と星は面から少し浮かせて、どちらの面が上になっても見えるよう両面に
    /// </summary>
    private static Mesh BuildMeepleBlack()
    {
        var pts = MeeplePoints();
        var v = new List<Vector3>();
        var n = new List<Vector3>();
        var faces = new List<int>();
        var glow = new List<int>();
        int sideStart = ExtrudeInto(v, n, faces, glow, pts, MEEPLE_T, true);
        var c = new List<Color>();
        for (int i = 0; i < sideStart; i++) c.Add(Color.white);
        for (int i = 0; i < pts.Length; i++) // 側面の頂点は、輪郭の点ごとに上・下の2つ
        {
            var col = RimColor(pts[i]);
            c.Add(col);
            c.Add(col);
        }
        var outward = VertexNormals(pts);
        float h = MEEPLE_T * 0.5f;
        foreach (float side in new[] { 1f, -1f })
        {
            // 帯の幅・星の大きさは背の高さ1のときに決めた値なので、背の高さに比例させる（2026-11-13 にコマを0.6倍にしたとき。
            // そのままだと細い腕で上下の帯が重なる）
            RimBand(v, n, c, glow, pts, outward, 0.07f * MEEPLE_H, side * (h + 0.003f), side);
            Sparkle(v, n, c, glow, new Vector2(0f, -0.07f * MEEPLE_H), 0.12f * MEEPLE_H, 0.028f * MEEPLE_H, side * (h + 0.004f), side);
        }
        var mesh = new Mesh { name = "PusherMeepleBlack" };
        mesh.SetVertices(v);
        mesh.SetNormals(n);
        mesh.SetColors(c);
        mesh.subMeshCount = 2;
        mesh.SetTriangles(faces, 0);
        mesh.SetTriangles(glow, 1);
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>輪郭に沿って内側へ width の帯（面の高さ y）。外のふちは虹色、内のふちは透明（光がにじんで消えるように）</summary>
    private static void RimBand(List<Vector3> v, List<Vector3> n, List<Color> c, List<int> t, Vector2[] pts, Vector2[] outward,
        float width, float y, float side)
    {
        var normal = new Vector3(0f, side, 0f);
        int count = pts.Length, start = v.Count;
        for (int i = 0; i < count; i++)
        {
            var col = RimColor(pts[i]);
            var inner = pts[i] - outward[i] * width;
            v.Add(new Vector3(pts[i].x, y, pts[i].y));
            v.Add(new Vector3(inner.x, y, inner.y));
            n.Add(normal);
            n.Add(normal);
            c.Add(col);
            c.Add(new Color(col.r, col.g, col.b, 0f));
        }
        for (int i = 0; i < count; i++)
        {
            int o0 = start + i * 2, i0 = o0 + 1, o1 = start + (i + 1) % count * 2, i1 = o1 + 1;
            Tri(t, v, o0, o1, i1, normal);
            Tri(t, v, o0, i1, i0, normal);
        }
    }

    /// <summary>4つの先の白い星（✦）。中心 center・先までの長さ outer・くびれ inner（面の高さ y）</summary>
    private static void Sparkle(List<Vector3> v, List<Vector3> n, List<Color> c, List<int> t, Vector2 center, float outer,
        float inner, float y, float side)
    {
        var normal = new Vector3(0f, side, 0f);
        int start = v.Count;
        v.Add(new Vector3(center.x, y, center.y));
        n.Add(normal);
        c.Add(Color.white);
        for (int i = 0; i < 8; i++)
        {
            float a = i * Mathf.PI * 0.25f, r = i % 2 == 0 ? outer : inner;
            v.Add(new Vector3(center.x + Mathf.Sin(a) * r, y, center.y + Mathf.Cos(a) * r));
            n.Add(normal);
            c.Add(Color.white);
        }
        for (int i = 0; i < 8; i++) Tri(t, v, start, start + 1 + i, start + 1 + (i + 1) % 8, normal);
    }

    // ================================================================
    // 惑星回復薬（2026-11-12）
    // ================================================================
    private static readonly Color LIQUID_TOP = SpaceTheme.Hex(0x67D5FF);    // 薬の水面（明るい水色）
    private static readonly Color LIQUID_BOTTOM = SpaceTheme.Hex(0x7A52F0); // 薬の底（すみれ色）
    private static readonly Color PLANET_LIGHT = SpaceTheme.Hex(0xFFA27A);  // 惑星の明るい側（左上）
    private static readonly Color PLANET_DARK = SpaceTheme.Hex(0xE2566E);   // 惑星の暗い側（右下）
    private static readonly Color PLANET_RING = SpaceTheme.Hex(0xFFE9C4);   // 惑星の輪

    /// <summary>
    /// 惑星回復薬: 丸い胴・細い首・口のふちのガラス（サブメッシュ0）と、口にさした栓（1。ガラスより薄い）。
    /// decorate なら、上下の面に薬（胴を水面で切った形）・惑星と輪・ガラスの光（2。頂点の色）を描く。
    /// 描く物は重なる順（奥の輪 → 惑星 → 手前の輪）に並べる（光らない材質は奥行きを書かないので、並べた順に重なる）
    /// </summary>
    private static Mesh BuildPotion(bool decorate)
    {
        float bottom = -POTION_L * 0.5f, top = POTION_L * 0.5f;
        const float R = 0.33f;                         // 丸い胴の半径
        float cz = bottom + R;                         // 胴の中心
        const float NECK = 0.11f, LIP = 0.135f;        // 首・口のふちの半分の幅
        float lipZ0 = top - 0.24f, lipZ1 = top - 0.19f; // 口のふちの下・上
        const float CORK = 0.10f;                      // 栓の半分の幅
        // 瓶の輪郭: 右の首のつけ根から時計回りに 胴の右 → 底 → 胴の左 → 左の首 → 口のふち → 右の首へ戻る
        var glass = new List<Vector2>();
        float a0 = Mathf.Asin(NECK / R); // 首のつけ根の、真上から測った胴の角度
        const int SEG = 28;
        for (int i = 0; i <= SEG; i++)
        {
            float a = a0 + (Mathf.PI * 2f - a0 * 2f) * i / SEG;
            glass.Add(new Vector2(R * Mathf.Sin(a), cz + R * Mathf.Cos(a)));
        }
        glass.Add(new Vector2(-NECK, lipZ0));
        glass.Add(new Vector2(-LIP, lipZ0));
        glass.Add(new Vector2(-LIP, lipZ1));
        glass.Add(new Vector2(LIP, lipZ1));
        glass.Add(new Vector2(LIP, lipZ0));
        glass.Add(new Vector2(NECK, lipZ0));
        var cork = new[]
        {
            new Vector2(-CORK, lipZ1 - 0.02f), new Vector2(CORK, lipZ1 - 0.02f), new Vector2(CORK, top - 0.03f),
            new Vector2(CORK - 0.025f, top), new Vector2(-CORK + 0.025f, top), new Vector2(-CORK, top - 0.03f),
        };

        var v = new List<Vector3>();
        var n = new List<Vector3>();
        var tGlass = new List<int>();
        var tCork = new List<int>();
        var tPaint = new List<int>();
        ExtrudeInto(v, n, tGlass, tGlass, glass.ToArray(), POTION_T, true);
        ExtrudeInto(v, n, tCork, tCork, cork, POTION_T * 0.7f, false);
        var mesh = new Mesh { name = decorate ? "PusherPotion" : "PusherPotionCollider" };
        if (!decorate)
        {
            tGlass.AddRange(tCork);
            mesh.SetVertices(v);
            mesh.SetTriangles(tGlass, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        var c = new List<Color>();
        for (int i = 0; i < v.Count; i++) c.Add(Color.white);
        const float WALL = 0.035f, LEVEL = 0.45f; // ガラスの厚み・水面の高さ（胴の中心から、内側の半径の何倍上か）
        var body = new Vector2(0f, cz);
        var planet = new Vector2(0.012f, cz - 0.04f);
        const float PLANET_R = 0.085f, TILT = -12f * Mathf.Deg2Rad;
        var ringOut = new Vector2(0.165f, 0.05f);
        var ringIn = new Vector2(0.14f, 0.032f);
        foreach (float side in new[] { 1f, -1f })
        {
            float y = side * (POTION_T * 0.5f + 0.003f);
            Liquid(v, n, c, tPaint, body, R - WALL, LEVEL, y, side);
            Arc(v, n, c, tPaint, planet, ringOut, ringIn, TILT, 0f, Mathf.PI, y, side, PLANET_RING);              // 奥の輪
            Disc(v, n, c, tPaint, planet, PLANET_R, y, side);                                                     // 惑星
            Arc(v, n, c, tPaint, planet, ringOut, ringIn, TILT, Mathf.PI, Mathf.PI * 2f, y, side, PLANET_RING);   // 手前の輪
            // ガラスの光（胴の左上、薬より上の空いた所）
            Arc(v, n, c, tPaint, body, new Vector2(R - 0.05f, R - 0.05f), new Vector2(R - 0.075f, R - 0.075f), 0f,
                118f * Mathf.Deg2Rad, 148f * Mathf.Deg2Rad, y, side, new Color(1f, 1f, 1f, 0.8f));
        }
        mesh.SetVertices(v);
        mesh.SetNormals(n);
        mesh.SetColors(c);
        mesh.subMeshCount = 3;
        mesh.SetTriangles(tGlass, 0);
        mesh.SetTriangles(tCork, 1);
        mesh.SetTriangles(tPaint, 2);
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>丸い胴の中の薬: 円（中心 center・半径 radius）を水面（中心から半径×level 上）で切った形。水面が明るい水色・底がすみれ色</summary>
    private static void Liquid(List<Vector3> v, List<Vector3> n, List<Color> c, List<int> t, Vector2 center, float radius,
        float level, float y, float side)
    {
        var normal = new Vector3(0f, side, 0f);
        float b0 = Mathf.Acos(Mathf.Clamp(level, -1f, 1f)); // 水面の両はしの、真上から測った角度
        float zTop = center.y + radius * level, zBottom = center.y - radius;
        int start = v.Count;
        // 扇の要（水面と底のまん中。切った形は凸なので、ここから扇に分けられる）
        v.Add(new Vector3(center.x, y, (zTop + zBottom) * 0.5f));
        n.Add(normal);
        c.Add(Color.Lerp(LIQUID_BOTTOM, LIQUID_TOP, 0.5f));
        const int SEG = 24;
        for (int i = 0; i <= SEG; i++)
        {
            float a = b0 + (Mathf.PI * 2f - b0 * 2f) * i / SEG;
            var p = new Vector2(center.x + radius * Mathf.Sin(a), center.y + radius * Mathf.Cos(a));
            v.Add(new Vector3(p.x, y, p.y));
            n.Add(normal);
            c.Add(Color.Lerp(LIQUID_BOTTOM, LIQUID_TOP, Mathf.InverseLerp(zBottom, zTop, p.y)));
        }
        for (int i = 0; i < SEG; i++) Tri(t, v, start, start + 1 + i, start + 2 + i, normal);
        Tri(t, v, start, start + 1 + SEG, start + 1, normal); // 水面の下（弦と要の三角形）
    }

    /// <summary>惑星の円（中心 center・半径 radius）。左上が明るく、右下が暗い</summary>
    private static void Disc(List<Vector3> v, List<Vector3> n, List<Color> c, List<int> t, Vector2 center, float radius, float y,
        float side)
    {
        var normal = new Vector3(0f, side, 0f);
        var light = new Vector2(-0.7071f, 0.7071f);
        int start = v.Count;
        v.Add(new Vector3(center.x, y, center.y));
        n.Add(normal);
        c.Add(Color.Lerp(PLANET_DARK, PLANET_LIGHT, 0.5f));
        const int SEG = 20;
        for (int i = 0; i < SEG; i++)
        {
            float a = i * Mathf.PI * 2f / SEG;
            var d = new Vector2(Mathf.Sin(a), Mathf.Cos(a));
            v.Add(new Vector3(center.x + d.x * radius, y, center.y + d.y * radius));
            n.Add(normal);
            c.Add(Color.Lerp(PLANET_DARK, PLANET_LIGHT, (Vector2.Dot(d, light) + 1f) * 0.5f));
        }
        for (int i = 0; i < SEG; i++) Tri(t, v, start, start + 1 + i, start + 1 + (i + 1) % SEG, normal);
    }

    /// <summary>
    /// だ円の帯の一部（外の半径 outer・内の半径 inner をそれぞれ x・y に。tilt だけ回す）。角度 from〜to はラジアンで、
    /// 0 が右・π/2 が上（瓶の口の向き）。惑星の輪（奥の半分と手前の半分）とガラスの光に使う
    /// </summary>
    private static void Arc(List<Vector3> v, List<Vector3> n, List<Color> c, List<int> t, Vector2 center, Vector2 outer,
        Vector2 inner, float tilt, float from, float to, float y, float side, Color color)
    {
        var normal = new Vector3(0f, side, 0f);
        float cs = Mathf.Cos(tilt), sn = Mathf.Sin(tilt);
        const int SEG = 14;
        int start = v.Count;
        for (int i = 0; i <= SEG; i++)
        {
            float a = Mathf.Lerp(from, to, i / (float)SEG);
            for (int k = 0; k < 2; k++)
            {
                var r = k == 0 ? outer : inner;
                float px = Mathf.Cos(a) * r.x, py = Mathf.Sin(a) * r.y;
                v.Add(new Vector3(center.x + px * cs - py * sn, y, center.y + px * sn + py * cs));
                n.Add(normal);
                c.Add(color);
            }
        }
        for (int i = 0; i < SEG; i++)
        {
            int o0 = start + i * 2, i0 = o0 + 1, o1 = o0 + 2, i1 = o0 + 3;
            Tri(t, v, o0, o1, i1, normal);
            Tri(t, v, o0, i1, i0, normal);
        }
    }

    // ================================================================
    // 星のカケラ（星の先の1つ）
    // ================================================================
    private static Mesh BuildStar()
    {
        // 正五角形の星: 内側の頂点は中心から 0.382 倍・先端の左右 36 度
        float r = STAR_L * 0.382f;
        var pts = new[]
        {
            new Vector2(0f, 0f),
            new Vector2(r * Mathf.Sin(36f * Mathf.Deg2Rad), r * Mathf.Cos(36f * Mathf.Deg2Rad)),
            new Vector2(0f, STAR_L),
            new Vector2(-r * Mathf.Sin(36f * Mathf.Deg2Rad), r * Mathf.Cos(36f * Mathf.Deg2Rad)),
        };
        // 面の重心を原点に
        var c = Centroid(pts);
        for (int i = 0; i < pts.Length; i++) pts[i] -= c;
        return Extrude(pts, STAR_T, false, "PusherStar");
    }

    // ================================================================
    // 共通
    // ================================================================
    /// <summary>
    /// 多角形（x と z）に厚み（y）をつける。smoothSides なら側面の法線をなめらかに（丸い輪郭のコマ）、
    /// そうでなければ角ばった側面（星のカケラ）
    /// </summary>
    private static Mesh Extrude(Vector2[] pts, float thick, bool smoothSides, string name)
    {
        var v = new List<Vector3>();
        var n = new List<Vector3>();
        var t = new List<int>();
        ExtrudeInto(v, n, t, t, pts, thick, smoothSides);
        var mesh = new Mesh { name = name };
        mesh.SetVertices(v);
        mesh.SetNormals(n);
        mesh.SetTriangles(t, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>
    /// Extrude の中身: 頂点と三角形をリストに足す。上下の面の三角形は faces、側面は sides へ（同じリストでもよい）。
    /// 側面の最初の頂点の番号を返す（smoothSides なら、そこから輪郭の点ごとに上・下の2つずつ並ぶ）
    /// </summary>
    private static int ExtrudeInto(List<Vector3> v, List<Vector3> n, List<int> faces, List<int> sides, Vector2[] pts, float thick,
        bool smoothSides)
    {
        int count = pts.Length;
        float h = thick * 0.5f;
        float area = SignedArea(pts);
        var tris2 = EarClip(pts);

        // 上下の面
        foreach (float side in new[] { 1f, -1f })
        {
            int start = v.Count;
            var normal = new Vector3(0f, side, 0f);
            for (int i = 0; i < count; i++)
            {
                v.Add(new Vector3(pts[i].x, h * side, pts[i].y));
                n.Add(normal);
            }
            for (int k = 0; k < tris2.Count; k += 3)
                Tri(faces, v, start + tris2[k], start + tris2[k + 1], start + tris2[k + 2], normal);
        }
        int sideStart = v.Count;

        // 側面（外向きの法線: 反時計回りなら辺の右手側）
        var edgeN = new Vector3[count];
        for (int i = 0; i < count; i++)
        {
            var d = pts[(i + 1) % count] - pts[i];
            var en = new Vector3(d.y, 0f, -d.x);
            if (area < 0f) en = -en;
            edgeN[i] = en.normalized;
        }
        if (smoothSides)
        {
            int start = v.Count;
            for (int i = 0; i < count; i++)
            {
                var vn = (edgeN[(i - 1 + count) % count] + edgeN[i]).normalized;
                v.Add(new Vector3(pts[i].x, h, pts[i].y));
                v.Add(new Vector3(pts[i].x, -h, pts[i].y));
                n.Add(vn);
                n.Add(vn);
            }
            for (int i = 0; i < count; i++)
            {
                int a0 = start + i * 2, b0 = a0 + 1, a1 = start + (i + 1) % count * 2, b1 = a1 + 1;
                Tri(sides, v, a0, a1, b1, edgeN[i]);
                Tri(sides, v, a0, b1, b0, edgeN[i]);
            }
        }
        else
        {
            for (int i = 0; i < count; i++)
            {
                var p0 = pts[i];
                var p1 = pts[(i + 1) % count];
                int s = v.Count;
                v.Add(new Vector3(p0.x, h, p0.y));
                v.Add(new Vector3(p1.x, h, p1.y));
                v.Add(new Vector3(p1.x, -h, p1.y));
                v.Add(new Vector3(p0.x, -h, p0.y));
                for (int k = 0; k < 4; k++) n.Add(edgeN[i]);
                Tri(sides, v, s, s + 1, s + 2, edgeN[i]);
                Tri(sides, v, s, s + 2, s + 3, edgeN[i]);
            }
        }
        return sideStart;
    }

    /// <summary>多角形の各頂点の外向きの向き（両どなりの辺の外向きの平均）</summary>
    private static Vector2[] VertexNormals(Vector2[] pts)
    {
        int count = pts.Length;
        float sign = SignedArea(pts) >= 0f ? 1f : -1f;
        var edge = new Vector2[count];
        for (int i = 0; i < count; i++)
        {
            var d = pts[(i + 1) % count] - pts[i];
            edge[i] = new Vector2(d.y, -d.x).normalized * sign;
        }
        var res = new Vector2[count];
        for (int i = 0; i < count; i++) res[i] = (edge[(i - 1 + count) % count] + edge[i]).normalized;
        return res;
    }

    /// <summary>
    /// 三角形を、表の面が normal の向きになる順で足す（Unity は Cross(b-a, c-a) の向きが表）
    /// </summary>
    private static void Tri(List<int> t, List<Vector3> v, int a, int b, int c, Vector3 normal)
    {
        if (Vector3.Dot(Vector3.Cross(v[b] - v[a], v[c] - v[a]), normal) >= 0f)
        {
            t.Add(a);
            t.Add(b);
            t.Add(c);
        }
        else
        {
            t.Add(a);
            t.Add(c);
            t.Add(b);
        }
    }

    private static float SignedArea(Vector2[] p)
    {
        float s = 0f;
        for (int i = 0; i < p.Length; i++)
        {
            var a = p[i];
            var b = p[(i + 1) % p.Length];
            s += a.x * b.y - b.x * a.y;
        }
        return s * 0.5f;
    }

    private static Vector2 Centroid(Vector2[] p)
    {
        float a = 0f, cx = 0f, cy = 0f;
        for (int i = 0; i < p.Length; i++)
        {
            var p0 = p[i];
            var p1 = p[(i + 1) % p.Length];
            float cr = p0.x * p1.y - p1.x * p0.y;
            a += cr;
            cx += (p0.x + p1.x) * cr;
            cy += (p0.y + p1.y) * cr;
        }
        if (Mathf.Abs(a) < 1e-6f) return Vector2.zero;
        return new Vector2(cx / (3f * a), cy / (3f * a));
    }

    private static float Cross2(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

    private static bool InTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float d1 = Cross2(b - a, p - a), d2 = Cross2(c - b, p - b), d3 = Cross2(a - c, p - c);
        return d1 > 1e-7f && d2 > 1e-7f && d3 > 1e-7f;
    }

    /// <summary>単純な多角形を三角形に分ける（耳切り法。頂点の番号を3つずつ返す。反時計回りの順）</summary>
    private static List<int> EarClip(Vector2[] p)
    {
        var idx = new List<int>();
        for (int i = 0; i < p.Length; i++) idx.Add(i);
        if (SignedArea(p) < 0f) idx.Reverse();
        var tris = new List<int>();
        float tolerance = 1e-9f;
        int guard = 0;
        while (idx.Count > 3 && guard++ < 20000)
        {
            bool clipped = false;
            for (int i = 0; i < idx.Count; i++)
            {
                int a = idx[(i - 1 + idx.Count) % idx.Count], b = idx[i], c = idx[(i + 1) % idx.Count];
                if (Cross2(p[b] - p[a], p[c] - p[b]) <= tolerance) continue; // へこんだ角（またはまっすぐ）
                bool inside = false;
                for (int k = 0; k < idx.Count; k++)
                {
                    int q = idx[k];
                    if (q == a || q == b || q == c) continue;
                    if (InTriangle(p[q], p[a], p[b], p[c]))
                    {
                        inside = true;
                        break;
                    }
                }
                if (inside) continue;
                tris.Add(a);
                tris.Add(b);
                tris.Add(c);
                idx.RemoveAt(i);
                clipped = true;
                break;
            }
            if (clipped) continue;
            // まっすぐに並んだ点しか残っていない: 少しゆるめてもう一度
            if (tolerance > -1e-4f) tolerance = -1e-4f;
            else break;
        }
        if (idx.Count == 3)
        {
            tris.Add(idx[0]);
            tris.Add(idx[1]);
            tris.Add(idx[2]);
        }
        return tris;
    }

    /// <summary>頂点の色つきの直方体（光らない材質で、ネオンの線や光る帯に使う）</summary>
    public static Mesh Box(Vector3 size, Color color)
    {
        var h = size * 0.5f;
        var v = new List<Vector3>();
        var n = new List<Vector3>();
        var c = new List<Color>();
        var uv = new List<Vector2>();
        var t = new List<int>();
        var faces = new[]
        {
            (Vector3.up, Vector3.right, Vector3.forward), (Vector3.down, Vector3.right, Vector3.back),
            (Vector3.forward, Vector3.left, Vector3.up), (Vector3.back, Vector3.right, Vector3.up),
            (Vector3.right, Vector3.forward, Vector3.up), (Vector3.left, Vector3.back, Vector3.up),
        };
        foreach (var (normal, u, w) in faces)
        {
            int s = v.Count;
            var center = Vector3.Scale(normal, h);
            var du = Vector3.Scale(u, h);
            var dw = Vector3.Scale(w, h);
            v.Add(center - du - dw);
            v.Add(center + du - dw);
            v.Add(center + du + dw);
            v.Add(center - du + dw);
            for (int k = 0; k < 4; k++)
            {
                n.Add(normal);
                c.Add(color);
            }
            uv.Add(new Vector2(0f, 0f));
            uv.Add(new Vector2(1f, 0f));
            uv.Add(new Vector2(1f, 1f));
            uv.Add(new Vector2(0f, 1f));
            Tri(t, v, s, s + 1, s + 2, normal);
            Tri(t, v, s, s + 2, s + 3, normal);
        }
        var mesh = new Mesh { name = "PusherBox" };
        mesh.SetVertices(v);
        mesh.SetNormals(n);
        mesh.SetColors(c);
        mesh.SetUVs(0, uv);
        mesh.SetTriangles(t, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>頂点の色つきの板（xy 面・表は -z。カメラに向けて置く背景・光の玉に使う）</summary>
    public static Mesh Quad(Vector2 size, Color color)
    {
        var h = size * 0.5f;
        var mesh = new Mesh { name = "PusherQuad" };
        mesh.SetVertices(new List<Vector3>
        {
            new Vector3(-h.x, -h.y, 0f), new Vector3(h.x, -h.y, 0f), new Vector3(h.x, h.y, 0f), new Vector3(-h.x, h.y, 0f),
        });
        mesh.SetNormals(new List<Vector3> { Vector3.back, Vector3.back, Vector3.back, Vector3.back });
        mesh.SetColors(new List<Color> { color, color, color, color });
        mesh.SetUVs(0, new List<Vector2> { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) });
        mesh.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
        mesh.RecalculateBounds();
        return mesh;
    }
}
