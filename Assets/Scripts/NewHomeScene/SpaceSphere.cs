using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 自転する球（uGUI の Graphic）。正距円筒図法のテクスチャ（SpacePlanetSurfaces）を球に貼って見せる。
///
/// 円盤を同心円状の細かい三角形に分け、各頂点で「球面のどこ（緯度・経度）が見えているか」を計算して UV にする。
/// 光の当たり方（昼夜）は頂点色で付けるので、カスタムシェーダー不要（UI/Default のまま描ける）。
/// 自転は経度方向に U をずらすだけ（テクスチャは横方向 Repeat）。形が変わらない限り頂点の計算はやり直さない。
///
/// 経度は ±180° でつながっているため、三角形ごとに U を連続させる（頂点を三角形間で共有しない）。
/// Shading.Night は夜側ほど不透明になる層（溶岩・街の灯りなど、暗い側で光るもの）。
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public class SpaceSphere : MaskableGraphic
{
    public enum Shading { Lit, Night }

    /// <summary>光の向き（左上手前から。ほかの宇宙デザインの画像と同じ向き）</summary>
    public static readonly Vector3 LightDir = new Vector3(-0.55f, 0.50f, 0.67f).normalized;

    private Texture _texture;
    private float _spin;
    private float _tilt, _incline, _ambient = 0.07f, _nightBase;
    private Shading _shading = Shading.Lit;
    private int _segments = 48, _rings = 16;

    private Vector3[] _pos;
    private float[] _u, _v;
    private Color[] _shade;
    private bool _geomDirty = true;
    private Rect _builtRect;
    private static readonly List<UIVertex> Stream = new List<UIVertex>();

    public override Texture mainTexture => _texture != null ? _texture : s_WhiteTexture;

    public Texture Texture
    {
        get => _texture;
        set
        {
            if (_texture == value) return;
            _texture = value;
            SetMaterialDirty();
        }
    }

    /// <summary>自転の位相（0〜1で1周）。増やすと模様が左から右へ流れる</summary>
    public float Spin
    {
        get => _spin;
        set
        {
            if (_spin == value) return;
            _spin = value;
            SetVerticesDirty();
        }
    }

    /// <param name="tilt">自転軸の画面内の傾き（度）</param>
    /// <param name="incline">自転軸の手前への傾き（度）</param>
    /// <param name="ambient">夜側の明るさ（Lit）</param>
    /// <param name="nightBase">昼側での不透明度（Night）</param>
    /// <param name="segments">円周方向の分割数</param>
    /// <param name="rings">半径方向の分割数（縁ほど細かく分ける）</param>
    public void Setup(float tilt, float incline, float ambient, Shading shading, float nightBase, int segments, int rings)
    {
        _tilt = tilt;
        _incline = incline;
        _ambient = ambient;
        _shading = shading;
        _nightBase = nightBase;
        _segments = Mathf.Max(8, segments);
        _rings = Mathf.Max(2, rings);
        raycastTarget = false;
        _geomDirty = true;
        SetVerticesDirty();
    }

    protected override void OnRectTransformDimensionsChange()
    {
        base.OnRectTransformDimensionsChange();
        _geomDirty = true;
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        var rect = GetPixelAdjustedRect();
        if (rect.width <= 0f || rect.height <= 0f) return;
        if (_geomDirty || rect != _builtRect) Build(rect);

        Color tint = color;
        int n = _pos.Length;
        Stream.Clear();
        if (Stream.Capacity < n) Stream.Capacity = n;
        var vert = UIVertex.simpleVert;
        for (int i = 0; i < n; i++)
        {
            vert.position = _pos[i];
            vert.uv0 = new Vector4(_u[i] - _spin, _v[i], 0f, 0f);
            vert.color = _shade[i] * tint;
            Stream.Add(vert);
        }
        vh.AddUIVertexTriangleStream(Stream);
    }

    /// <summary>頂点（位置・UV・陰影）を計算し直す。形・大きさ・傾きが変わったときだけ</summary>
    private void Build(Rect rect)
    {
        _builtRect = rect;
        _geomDirty = false;

        int seg = _segments, rings = _rings;
        float cx = rect.center.x, cy = rect.center.y;
        float rad = Mathf.Min(rect.width, rect.height) * 0.5f;
        // 縁の外側に約1.2px のぼかし輪を足してアンチエイリアスにする
        float feather = Mathf.Clamp(1.2f / Mathf.Max(1f, rad), 0.002f, 0.05f);

        // ---- 一意な頂点: 中心 ＋ リング（rings 本は球面上、最後の1本は縁のぼかし）× seg ----
        int ringTotal = rings + 1;
        int uniq = 1 + ringTotal * seg;
        var up = new Vector3[uniq];
        var uu = new float[uniq];
        var uv = new float[uniq];
        var uc = new Color[uniq];

        float tau = _tilt * Mathf.Deg2Rad, inc = _incline * Mathf.Deg2Rad;
        float ct = Mathf.Cos(tau), st = Mathf.Sin(tau), ci = Mathf.Cos(inc), si = Mathf.Sin(inc);
        var light = LightDir;

        void Vertex(int i, float px, float py, float posMul, float alpha)
        {
            float z = Mathf.Sqrt(Mathf.Max(0f, 1f - px * px - py * py));
            up[i] = new Vector3(cx + px * posMul * rad, cy + py * posMul * rad, 0f);

            // 画面の座標 → 惑星の座標（自転軸の傾きを戻す）→ 緯度・経度
            float x1 = px * ct + py * st;
            float y1 = -px * st + py * ct;
            float y2 = y1 * ci + z * si;
            float z2 = -y1 * si + z * ci;
            float lat = Mathf.Asin(Mathf.Clamp(y2, -1f, 1f));
            float lon = Mathf.Atan2(x1, z2);
            uu[i] = lon / (Mathf.PI * 2f);
            uv[i] = 0.5f + lat / Mathf.PI;

            float ndl = px * light.x + py * light.y + z * light.z;
            if (_shading == Shading.Lit)
            {
                // 明暗の境目は少し回り込ませてやわらかく。縁はわずかに暗く（周辺減光）
                float d = Mathf.Clamp01((ndl + 0.15f) / 1.15f);
                float s = (_ambient + (1f - _ambient) * d) * (0.82f + 0.18f * z);
                uc[i] = new Color(s, s, s, alpha);
            }
            else
            {
                float t = Mathf.Clamp01((0.25f - ndl) / 0.40f);
                float night = t * t * (3f - 2f * t);
                uc[i] = new Color(1f, 1f, 1f, (_nightBase + (1f - _nightBase) * night) * alpha);
            }
        }

        int idx = 0;
        Vertex(idx++, 0f, 0f, 1f, 1f);
        for (int k = 1; k <= ringTotal; k++)
        {
            bool edge = k == ringTotal;
            float rho = edge ? 1f : Mathf.Sin(k / (float)rings * Mathf.PI * 0.5f); // 縁ほど細かく
            for (int j = 0; j < seg; j++)
            {
                float a = j / (float)seg * Mathf.PI * 2f;
                Vertex(idx++, rho * Mathf.Cos(a), rho * Mathf.Sin(a), edge ? 1f + feather : 1f, edge ? 0f : 1f);
            }
        }

        // ---- 三角形（中心の扇＋リング間の帯）。三角形ごとに U を連続させる ----
        int tris = seg + 2 * seg * rings;
        int count = tris * 3;
        if (_pos == null || _pos.Length != count)
        {
            _pos = new Vector3[count];
            _u = new float[count];
            _v = new float[count];
            _shade = new Color[count];
        }
        int w = 0;

        void Tri(int a, int b, int c)
        {
            float ua = uu[a];
            Put(a, ua);
            Put(b, Unwrap(uu[b], ua));
            Put(c, Unwrap(uu[c], ua));
        }

        void Put(int i, float u)
        {
            _pos[w] = up[i];
            _u[w] = u;
            _v[w] = uv[i];
            _shade[w] = uc[i];
            w++;
        }

        int Ring(int k, int j) => 1 + (k - 1) * seg + (j % seg);

        for (int j = 0; j < seg; j++) Tri(0, Ring(1, j), Ring(1, j + 1));
        for (int k = 1; k <= rings; k++)
        for (int j = 0; j < seg; j++)
        {
            int i00 = Ring(k, j), i01 = Ring(k, j + 1), i10 = Ring(k + 1, j), i11 = Ring(k + 1, j + 1);
            Tri(i00, i10, i11);
            Tri(i00, i11, i01);
        }
    }

    private static float Unwrap(float u, float reference)
    {
        while (u - reference > 0.5f) u -= 1f;
        while (u - reference < -0.5f) u += 1f;
        return u;
    }
}
