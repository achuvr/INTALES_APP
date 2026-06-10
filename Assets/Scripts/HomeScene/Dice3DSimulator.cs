using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;

/// <summary>
/// 3D物理ダイスのシミュレーター。
///
/// シーンの遠く（y=-500）に見えない「ダイストレイ」と専用カメラを構築し、
/// 物理演算（Rigidbody）でダイスを転がす。映像は RenderTexture に描画され、
/// DiceRollController の結果パネル内の RawImage に表示される。
///
/// 出目の判定:
///   静止後、各面の法線がワールド上方向と最も一致する面を「上面」として読む。
///   d4（四面体）だけは慣習に合わせて接地面（下向きの面）を読む。
///
/// メッシュは全てコード生成（d4=正四面体, d6=立方体, d8=正八面体, d10=五方台形体）。
/// 数字は面ごとに 3D TextMeshPro を貼り付ける。
/// </summary>
public class Dice3DSimulator : MonoBehaviour
{
    private const float TRAY_HALF = 3.1f;   // トレイの内側半径
    private const float DIE_SIZE = 1.0f;    // ダイスの基準サイズ
    private const float SETTLE_TIMEOUT = 6f;

    private Camera _cam;
    private RenderTexture _rt;
    private TMP_FontAsset _font;
    private Material _dieMat;
    private Material _floorMat;
    private AudioSource _audio;
    private AudioClip _knockClip;
    private readonly List<GameObject> _activeDice = new List<GameObject>();

    public RenderTexture Texture => _rt;

    /// <summary>面情報（ローカル座標の中心・法線と出目の値）</summary>
    private class FaceInfo
    {
        public Vector3 center;
        public Vector3 normal;
        public int value;
    }

    /// <summary>ダイスワールドを構築して返す</summary>
    public static Dice3DSimulator Create(TMP_FontAsset font)
    {
        var root = new GameObject("__DiceWorld");
        root.transform.position = new Vector3(0, -500, 0); // シーンと干渉しない遠い場所
        var sim = root.AddComponent<Dice3DSimulator>();
        sim._font = font;
        sim.BuildWorld();
        return sim;
    }

    public void SetVisible(bool visible)
    {
        if (_cam != null) _cam.enabled = visible;
    }

    // ================================================================
    // ロール実行
    // ================================================================
    public async UniTask<List<int>> RollAsync(int count, int faces)
    {
        ClearDice();
        SetVisible(true);

        var dice = new List<(GameObject go, List<FaceInfo> faceInfos)>();
        for (int i = 0; i < count; i++)
        {
            var (go, infos) = CreateDie(faces);
            // トレイ上空のランダムな位置・回転・勢いで投げ込む
            go.transform.position = transform.position + new Vector3(
                Random.Range(-1.2f, 1.2f), 3.5f + i * 1.4f, Random.Range(-1.2f, 1.2f));
            go.transform.rotation = Random.rotation;

            var rb = go.GetComponent<Rigidbody>();
            rb.linearVelocity = new Vector3(Random.Range(-2.5f, 2.5f), -1f, Random.Range(-2.5f, 2.5f));
            rb.angularVelocity = Random.onUnitSphere * Random.Range(8f, 16f);

            dice.Add((go, infos));
            _activeDice.Add(go);
        }

        // 全ダイスが静止するまで待つ（タイムアウトあり）
        float elapsed = 0f;
        while (elapsed < SETTLE_TIMEOUT)
        {
            await UniTask.Delay(100);
            elapsed += 0.1f;
            if (elapsed < 0.8f) continue; // 投げた直後は判定しない

            bool allResting = dice.All(d =>
            {
                var rb = d.go.GetComponent<Rigidbody>();
                return rb.IsSleeping() ||
                       (rb.linearVelocity.magnitude < 0.05f && rb.angularVelocity.magnitude < 0.05f);
            });
            if (allResting) break;
        }

        // 静止させて出目を読む
        var results = new List<int>();
        foreach (var (go, infos) in dice)
        {
            var rb = go.GetComponent<Rigidbody>();
            rb.isKinematic = true;
            results.Add(ReadResult(go.transform, infos, faces));
        }
        return results;
    }

    /// <summary>静止したダイスの出目を読む</summary>
    private static int ReadResult(Transform t, List<FaceInfo> faceInfos, int faces)
    {
        // d4は接地面（下向きの面）を、それ以外は上向きの面を読む
        Vector3 target = faces == 4 ? Vector3.down : Vector3.up;

        FaceInfo best = null;
        float bestDot = float.MinValue;
        foreach (var f in faceInfos)
        {
            float dot = Vector3.Dot(t.TransformDirection(f.normal), target);
            if (dot > bestDot)
            {
                bestDot = dot;
                best = f;
            }
        }
        return best?.value ?? 1;
    }

    public void ClearDice()
    {
        foreach (var d in _activeDice)
            if (d != null) Destroy(d);
        _activeDice.Clear();
    }

    // ================================================================
    // ワールド構築（トレイ・カメラ・ライト）
    // ================================================================
    private void BuildWorld()
    {
        _dieMat = MakeMaterial(new Color(0.96f, 0.93f, 0.85f), 0.97f); // アイボリー・テカテカ（ハイグロス）
        _floorMat = MakeMaterial(new Color(0.10f, 0.30f, 0.18f), 0.15f); // フェルト緑・マット

        // 床（見える）
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "__Floor";
        floor.transform.SetParent(transform, false);
        floor.transform.localPosition = new Vector3(0, -0.15f, 0);
        floor.transform.localScale = new Vector3(TRAY_HALF * 2.6f, 0.3f, TRAY_HALF * 2.6f);
        floor.GetComponent<MeshRenderer>().material = _floorMat;

        // 壁と天井（コライダーのみ・不可視）
        AddInvisibleWall(new Vector3(TRAY_HALF + 0.5f, 2.5f, 0), new Vector3(1, 6, TRAY_HALF * 2.6f));
        AddInvisibleWall(new Vector3(-(TRAY_HALF + 0.5f), 2.5f, 0), new Vector3(1, 6, TRAY_HALF * 2.6f));
        AddInvisibleWall(new Vector3(0, 2.5f, TRAY_HALF + 0.5f), new Vector3(TRAY_HALF * 2.6f, 6, 1));
        AddInvisibleWall(new Vector3(0, 2.5f, -(TRAY_HALF + 0.5f)), new Vector3(TRAY_HALF * 2.6f, 6, 1));
        AddInvisibleWall(new Vector3(0, 6f, 0), new Vector3(TRAY_HALF * 2.6f, 1, TRAY_HALF * 2.6f));

        // ダイス衝突音（コード合成の「カラッ」という音。衝突のたびに鳴らす）
        _knockClip = CreateKnockClip();
        _audio = gameObject.AddComponent<AudioSource>();
        _audio.playOnAwake = false;
        _audio.spatialBlend = 0f; // ダイスワールドは遠所にあるため2D再生にする

        // メインライト
        var lightGO = new GameObject("__Light");
        lightGO.transform.SetParent(transform, false);
        lightGO.transform.localRotation = Quaternion.Euler(55f, -25f, 0);
        var light = lightGO.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.25f;

        // ハイライト用のサブライト（逆方向から当ててツヤの反射を増やす）
        var fillGO = new GameObject("__FillLight");
        fillGO.transform.SetParent(transform, false);
        fillGO.transform.localRotation = Quaternion.Euler(38f, 140f, 0);
        var fill = fillGO.AddComponent<Light>();
        fill.type = LightType.Directional;
        fill.intensity = 0.7f;
        fill.color = new Color(1f, 0.97f, 0.9f);

        // カメラ（RenderTextureに描画）
        _rt = new RenderTexture(768, 768, 16);
        var camGO = new GameObject("__DiceCam");
        camGO.transform.SetParent(transform, false);
        camGO.transform.localPosition = new Vector3(0, 7.2f, -3.6f);
        camGO.transform.LookAt(transform.position + new Vector3(0, 0.3f, 0));
        _cam = camGO.AddComponent<Camera>();
        _cam.clearFlags = CameraClearFlags.SolidColor;
        _cam.backgroundColor = new Color(0.07f, 0.05f, 0.10f);
        _cam.fieldOfView = 46f;
        _cam.nearClipPlane = 0.5f;
        _cam.farClipPlane = 30f;
        _cam.targetTexture = _rt;
        _cam.enabled = false;
    }

    private void AddInvisibleWall(Vector3 localPos, Vector3 size)
    {
        var go = new GameObject("__Wall");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = localPos;
        var col = go.AddComponent<BoxCollider>();
        col.size = size;
    }

    private void OnDestroy()
    {
        if (_rt != null) _rt.Release();
    }

    // ================================================================
    // ダイス生成
    // ================================================================
    private (GameObject, List<FaceInfo>) CreateDie(int faces)
    {
        List<Vector3[]> facePolys = faces switch
        {
            4 => BuildTetrahedronFaces(),
            6 => BuildCubeFaces(),
            8 => BuildOctahedronFaces(),
            10 => BuildD10Faces(),
            _ => BuildCubeFaces(),
        };

        var go = new GameObject($"__Die_d{faces}");
        go.transform.SetParent(transform, false);

        var mesh = new Mesh();
        var verts = new List<Vector3>();
        var normals = new List<Vector3>();
        var tris = new List<int>();
        var faceInfos = new List<FaceInfo>();

        int value = 1;
        foreach (var poly in facePolys)
        {
            var polyList = poly.ToList();
            Vector3 center = Vector3.zero;
            foreach (var v in polyList) center += v;
            center /= polyList.Count;

            Vector3 normal = Vector3.Cross(polyList[1] - polyList[0], polyList[2] - polyList[0]).normalized;
            // 法線が外向き（中心から離れる向き）になるよう統一する
            if (Vector3.Dot(normal, center) < 0)
            {
                polyList.Reverse();
                normal = -normal;
            }

            int baseIdx = verts.Count;
            foreach (var v in polyList)
            {
                verts.Add(v);
                normals.Add(normal);
            }
            for (int i = 1; i < polyList.Count - 1; i++)
            {
                tris.Add(baseIdx);
                tris.Add(baseIdx + i);
                tris.Add(baseIdx + i + 1);
            }

            faceInfos.Add(new FaceInfo { center = center, normal = normal, value = value });
            AttachNumberLabel(go.transform, center, normal, value);
            value++;
        }

        mesh.SetVertices(verts);
        mesh.SetNormals(normals);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();

        go.AddComponent<MeshFilter>().mesh = mesh;
        go.AddComponent<MeshRenderer>().material = _dieMat;

        var col = go.AddComponent<MeshCollider>();
        col.sharedMesh = mesh;
        col.convex = true;

        var rb = go.AddComponent<Rigidbody>();
        rb.mass = 1f;
        rb.linearDamping = 0.1f;
        rb.angularDamping = 0.25f;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

        // 衝突に同期してダイス音を鳴らす
        var sound = go.AddComponent<DieImpactSound>();
        sound.source = _audio;
        sound.clip = _knockClip;

        return (go, faceInfos);
    }

    /// <summary>
    /// ダイスが何かにぶつかったとき「カラッ」と鳴らす。
    /// 衝撃の強さで音量を変え、ピッチをランダムに揺らして自然にする。
    /// </summary>
    private class DieImpactSound : MonoBehaviour
    {
        public AudioSource source;
        public AudioClip clip;
        private float _lastPlayTime;

        private void OnCollisionEnter(Collision collision)
        {
            if (source == null || clip == null) return;

            float impact = collision.relativeVelocity.magnitude;
            if (impact < 1.0f) return;                          // 弱すぎる接触は無音
            if (Time.time - _lastPlayTime < 0.05f) return;      // 同時多発の鳴りすぎ防止
            _lastPlayTime = Time.time;

            source.pitch = Random.Range(0.8f, 1.35f);
            source.PlayOneShot(clip, Mathf.Clamp01(impact / 9f));
        }
    }

    /// <summary>
    /// ダイスの衝突音をコードで合成する。
    /// 「カラッ」という芯の音に、残響の初期反射（減衰コピー）と低音の余韻を重ねて
    /// 木のトレイに響いているような鳴り方にする。
    /// </summary>
    private static AudioClip CreateKnockClip()
    {
        const int sampleRate = 44100;
        const float duration = 0.5f; // 余韻込みの長さ
        int n = (int)(sampleRate * duration);

        // --- 芯になる単発の打撃音（0.14秒）---
        int baseN = (int)(sampleRate * 0.14f);
        var knock = new float[baseN];
        for (int i = 0; i < baseN; i++)
        {
            float t = (float)i / sampleRate;
            float thump = Mathf.Sin(2f * Mathf.PI * 200f * t) * Mathf.Exp(-t * 42f) * 0.55f;  // 低い芯（余韻長め）
            float ring  = Mathf.Sin(2f * Mathf.PI * 330f * t) * Mathf.Exp(-t * 55f) * 0.22f;  // 倍音の鳴り
            float click = (Random.value * 2f - 1f) * Mathf.Exp(-t * 230f) * 0.45f;            // アタックノイズ
            knock[i] = thump + ring + click;
        }

        // --- 初期反射を重ねて「響き」を作る（遅延コピーを減衰させて加算）---
        var samples = new float[n];
        (float delaySec, float gain)[] reflections =
        {
            (0.000f, 1.00f), // 直接音
            (0.055f, 0.42f),
            (0.120f, 0.24f),
            (0.200f, 0.13f),
            (0.300f, 0.07f),
        };
        foreach (var (delaySec, gain) in reflections)
        {
            int offset = (int)(delaySec * sampleRate);
            for (int i = 0; i < baseN && offset + i < n; i++)
                samples[offset + i] += knock[i] * gain;
        }
        for (int i = 0; i < n; i++)
            samples[i] = Mathf.Clamp(samples[i] * 0.8f, -1f, 1f);

        var clip = AudioClip.Create("dice_knock", n, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    /// <summary>面に数字の3Dテキストを貼る</summary>
    private void AttachNumberLabel(Transform parent, Vector3 center, Vector3 normal, int value)
    {
        var go = new GameObject($"__Num_{value}");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = center + normal * 0.015f;

        // 法線と上方向が平行に近いときはLookRotationのupを差し替える
        Vector3 upHint = Mathf.Abs(Vector3.Dot(normal, Vector3.up)) > 0.95f ? Vector3.forward : Vector3.up;
        go.transform.localRotation = Quaternion.LookRotation(-normal, upHint);

        var tmp = go.AddComponent<TextMeshPro>();
        if (_font != null) tmp.font = _font;
        tmp.text = value.ToString();
        tmp.color = new Color(0.18f, 0.08f, 0.02f);
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.enableAutoSizing = true;
        tmp.fontSizeMin = 0.3f;
        tmp.fontSizeMax = 12f;
        // 視認性のため太字＋アウトラインで数字を太らせる
        tmp.fontStyle = FontStyles.Bold;
        tmp.outlineWidth = 0.18f;
        tmp.outlineColor = new Color32(46, 20, 5, 255);
        var rt = tmp.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(0.62f, 0.62f) * DIE_SIZE;
    }

    // ================================================================
    // 各ダイスの面（ポリゴン頂点）定義
    // ================================================================
    private static List<Vector3[]> BuildCubeFaces()
    {
        float h = DIE_SIZE * 0.55f;
        return new List<Vector3[]>
        {
            new[] { new Vector3(-h,  h, -h), new Vector3(-h,  h,  h), new Vector3( h,  h,  h), new Vector3( h,  h, -h) }, // 上
            new[] { new Vector3(-h, -h, -h), new Vector3( h, -h, -h), new Vector3( h, -h,  h), new Vector3(-h, -h,  h) }, // 下
            new[] { new Vector3(-h, -h,  h), new Vector3( h, -h,  h), new Vector3( h,  h,  h), new Vector3(-h,  h,  h) }, // 前
            new[] { new Vector3(-h, -h, -h), new Vector3(-h,  h, -h), new Vector3( h,  h, -h), new Vector3( h, -h, -h) }, // 後
            new[] { new Vector3( h, -h, -h), new Vector3( h,  h, -h), new Vector3( h,  h,  h), new Vector3( h, -h,  h) }, // 右
            new[] { new Vector3(-h, -h, -h), new Vector3(-h, -h,  h), new Vector3(-h,  h,  h), new Vector3(-h,  h, -h) }, // 左
        };
    }

    private static List<Vector3[]> BuildTetrahedronFaces()
    {
        float s = DIE_SIZE * 0.78f;
        var v = new[]
        {
            new Vector3( 1,  1,  1) * s * 0.5f,
            new Vector3(-1, -1,  1) * s * 0.5f,
            new Vector3(-1,  1, -1) * s * 0.5f,
            new Vector3( 1, -1, -1) * s * 0.5f,
        };
        return new List<Vector3[]>
        {
            new[] { v[0], v[1], v[2] },
            new[] { v[0], v[3], v[1] },
            new[] { v[0], v[2], v[3] },
            new[] { v[1], v[3], v[2] },
        };
    }

    private static List<Vector3[]> BuildOctahedronFaces()
    {
        float s = DIE_SIZE * 0.75f;
        var px = new Vector3( s, 0, 0); var nx = new Vector3(-s, 0, 0);
        var py = new Vector3(0,  s, 0); var ny = new Vector3(0, -s, 0);
        var pz = new Vector3(0, 0,  s); var nz = new Vector3(0, 0, -s);
        return new List<Vector3[]>
        {
            new[] { py, px, pz }, new[] { py, pz, nx }, new[] { py, nx, nz }, new[] { py, nz, px },
            new[] { ny, pz, px }, new[] { ny, nx, pz }, new[] { ny, nz, nx }, new[] { ny, px, nz },
        };
    }

    private static List<Vector3[]> BuildD10Faces()
    {
        float r = DIE_SIZE * 0.62f;     // 赤道リングの半径
        float eqH = DIE_SIZE * 0.10f;   // 赤道頂点の上下オフセット
        float apexH = DIE_SIZE * 0.72f; // 極の高さ

        var top = new Vector3(0, apexH, 0);
        var bottom = new Vector3(0, -apexH, 0);
        var eq = new Vector3[10];
        for (int k = 0; k < 10; k++)
        {
            float ang = k * 36f * Mathf.Deg2Rad;
            float y = (k % 2 == 0) ? eqH : -eqH;
            eq[k] = new Vector3(Mathf.Cos(ang) * r, y, Mathf.Sin(ang) * r);
        }

        var faces = new List<Vector3[]>();
        for (int j = 0; j < 5; j++)
        {
            // 上極に接する凧型面
            faces.Add(new[] { top, eq[(2 * j) % 10], eq[(2 * j + 1) % 10], eq[(2 * j + 2) % 10] });
            // 下極に接する凧型面
            faces.Add(new[] { bottom, eq[(2 * j + 3) % 10], eq[(2 * j + 2) % 10], eq[(2 * j + 1) % 10] });
        }
        return faces;
    }

    private static Material MakeMaterial(Color color, float smoothness = 0.3f)
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        var mat = new Material(shader);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
        // 光沢（URPは_Smoothness、Standardは_Glossiness）
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
        if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", smoothness);
        return mat;
    }
}
