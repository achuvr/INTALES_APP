using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// コインプッシャー（2026-11-12）の 3D の形（すべてコードで作る）。台に乗る景品は3種類:
///  ・コイン（G）… 円柱。上下の面に金貨の絵（Resources/CoinPusher/CoinFace。tools/coin/make_coin_face.py）、ふちは金色
///  ・コマ … ミープルの輪郭（tools/meeple/meeple_outline.py で取り出した64点）に厚みをつけた板。台の上に寝かせて置く
///  ・星のカケラ … 星の5つの先のうちの1つ（中心・内側の2点・先端の4点の凧形）に厚みをつけた板
/// 形はアプリを起動しているあいだ使い回す（台を閉じても消さない。材質は CoinPusherWorld が作って片づける）
/// </summary>
public static class CoinPusherMeshes
{
    public const float COIN_R = 0.4f;    // コインの半径
    public const float COIN_H = 0.12f;   // コインの厚み
    public const float COIN_CHAMFER = 0.025f; // 当たり判定で、ふちの上下を削る幅（縦に立ちにくくする）
    public const float MEEPLE_H = 1.0f;  // コマの背の高さ（寝かせるので、台の上では奥行き）
    public const float MEEPLE_T = 0.4f;  // コマの厚み（寝かせるので、台の上では高さ）
    public const float STAR_L = 1.05f;   // 星のカケラの長さ（星の中心から先端まで）
    public const float STAR_T = 0.22f;   // 星のカケラの厚み

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

    private static Mesh _coin, _coinCollider, _meeple, _star;

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
    private static Mesh BuildMeeple()
    {
        int count = MEEPLE_OUTLINE.Length / 2;
        var pts = new Vector2[count];
        for (int i = 0; i < count; i++)
            pts[i] = new Vector2(MEEPLE_OUTLINE[i * 2] * MEEPLE_H, (MEEPLE_OUTLINE[i * 2 + 1] - 0.5f) * MEEPLE_H);
        return Extrude(pts, MEEPLE_T, true, "PusherMeeple");
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
        int count = pts.Length;
        float h = thick * 0.5f;
        float area = SignedArea(pts);
        var tris2 = EarClip(pts);
        var v = new List<Vector3>();
        var n = new List<Vector3>();
        var t = new List<int>();

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
                Tri(t, v, start + tris2[k], start + tris2[k + 1], start + tris2[k + 2], normal);
        }

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
                Tri(t, v, a0, a1, b1, edgeN[i]);
                Tri(t, v, a0, b1, b0, edgeN[i]);
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
                Tri(t, v, s, s + 1, s + 2, edgeN[i]);
                Tri(t, v, s, s + 2, s + 3, edgeN[i]);
            }
        }

        var mesh = new Mesh { name = name };
        mesh.SetVertices(v);
        mesh.SetNormals(n);
        mesh.SetTriangles(t, 0);
        mesh.RecalculateBounds();
        return mesh;
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
