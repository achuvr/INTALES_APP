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
    private Material _fxMat;
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
    // 出目確定時のスキルエフェクト（職業別・出目の強さでスケール）
    // ================================================================

    /// <summary>
    /// 出目確定時にスキルエフェクトを再生する。
    /// エフェクトの「形」は職業（戦士=斬撃/魔法使い=らせん/弓使い=疾風/銃使い=射撃）、
    /// 「色」はキャラクターの属性（炎=赤系/水=青系/自然=緑系/雷=黄・オレンジ系）で決まる。
    /// intensity01: 出目の強さ（0=最小値, 1=最大値）。
    /// パーティクル量・速度・発光の強さがこの値でスケールし、最大出目では追い打ち演出が出る。
    /// </summary>
    public void PlayResultEffect(string job, string element, float intensity01)
    {
        float t = Mathf.Clamp01(intensity01);
        Vector3 center = new Vector3(0, 0.7f, 0); // トレイ中央（ローカル）
        var (primary, secondary) = ElementColors(element);

        // 共通レイヤーは閃光のみ。魔法陣などの「らしさ」は各職業メソッドが担当する
        SpawnFlashLight(center + Vector3.up * 1.5f, primary, Mathf.Lerp(3f, 9f, t));

        switch (job)
        {
            case "warrior":  PlayWarriorEffect(center, t, primary, secondary); break;
            case "magician": PlayMagicianEffect(center, t, primary, secondary); break;
            case "archer":   PlayArcherEffect(center, t, primary, secondary); break;
            case "gunner":   PlayGunnerEffect(center, t, primary, secondary); break;
            default:         PlayWarriorEffect(center, t, primary, secondary); break;
        }

        // 出目が強いほどカメラが揺れる
        ShakeCameraAsync(Mathf.Lerp(0.02f, 0.28f, t)).Forget();

        // 最大出目（クリティカル）: 金色の演出。形は職業ごとに異なる
        if (t >= 0.999f)
        {
            var gold = new Color(1f, 0.85f, 0.25f);
            var goldDeep = new Color(1f, 0.5f, 0.1f);

            // 共通: 強い金の閃光＋特大衝撃波＋大シェイク
            SpawnFlashLight(center + Vector3.up * 2f, gold, 14f);
            SpawnShockwaveRing(gold, 10f);
            ShakeCameraAsync(0.45f).Forget();

            switch (job)
            {
                case "warrior":  CriticalWarriorAsync(center, gold, goldDeep).Forget(); break;
                case "magician": CriticalMagician(center, gold, goldDeep); break;
                case "archer":   CriticalArcherAsync(gold).Forget(); break;
                case "gunner":   CriticalGunnerAsync(gold, goldDeep).Forget(); break;
                default:         CriticalWarriorAsync(center, gold, goldDeep).Forget(); break;
            }
        }
    }

    /// <summary>戦士のクリティカル: 全方位への金色の乱舞斬り＋大爆発</summary>
    private async UniTaskVoid CriticalWarriorAsync(Vector3 center, Color gold, Color goldDeep)
    {
        // 6連の斬撃が全方位に走る
        for (int i = 0; i < 6; i++)
        {
            SpawnSlash(i * 30f + Random.Range(-8f, 8f), 9f, gold);
            await UniTask.Delay(Random.Range(50, 80));
        }
        // 締めの大爆発
        SpawnBurst(center, gold, goldDeep,
            count: 110, speed: 13f, size: 0.24f, life: 1.1f, coneAngle: 85f, stretch: true, trails: true);
        SpawnShockwaveRing(gold, 12f);
    }

    /// <summary>魔法使いのクリティカル: 特大の金魔法陣＋天に伸びる光の柱＋大爆発</summary>
    private void CriticalMagician(Vector3 center, Color gold, Color goldDeep)
    {
        SpawnMagicCircle(gold, 6f, 2.2f);
        SpawnLightPillar(gold);
        SpawnBurst(center, gold, goldDeep,
            count: 120, speed: 11f, size: 0.24f, life: 1.3f, coneAngle: 80f, stretch: true, trails: true);
    }

    /// <summary>弓使いのクリティカル: 金の矢の豪雨（約30本が降り注ぐ）</summary>
    private async UniTaskVoid CriticalArcherAsync(Color gold)
    {
        for (int i = 0; i < 30; i++)
        {
            SpawnArrow(gold);
            await UniTask.Delay(Random.Range(20, 55));
        }
    }

    /// <summary>銃使いのクリティカル: 3方向からの全弾掃射＋中心で大爆発</summary>
    private async UniTaskVoid CriticalGunnerAsync(Color gold, Color goldDeep)
    {
        Vector3[] muzzles =
        {
            new Vector3(-2.6f, 3.4f, -2.3f),
            new Vector3( 2.6f, 3.4f, -2.3f),
            new Vector3( 0f,   3.6f,  2.8f),
        };

        // 3方向から4連射ずつ中心へ撃ち込む
        for (int volley = 0; volley < 4; volley++)
        {
            foreach (var m in muzzles)
            {
                SpawnFlashLight(m, gold, 5f);
                SpawnBurst(m, gold, goldDeep,
                    count: 14, speed: 7f, size: 0.15f, life: 0.22f, coneAngle: 40f, stretch: true);
                FireBulletAsync(m,
                    new Vector3(Random.Range(-0.8f, 0.8f), 0.15f, Random.Range(-0.8f, 0.8f)),
                    gold, goldDeep).Forget();
            }
            await UniTask.Delay(90);
        }

        // 撃ち込まれた中心で大爆発
        SpawnBurst(new Vector3(0, 0.7f, 0), gold, goldDeep,
            count: 90, speed: 10f, size: 0.2f, life: 1f, coneAngle: 85f, stretch: true, trails: true);
        SpawnShockwaveRing(gold, 12f);
    }

    /// <summary>天に伸びる光の柱（クリティカル用）</summary>
    private void SpawnLightPillar(Color color)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = "__LightPillar";
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0, 4f, 0);

        var mat = new Material(GetFxMaterial());
        SetFxColor(mat, color);
        go.GetComponent<MeshRenderer>().material = mat;

        go.AddComponent<PillarAnim>();
    }

    /// <summary>光の柱のアニメ（広がりながら立ち上がり、フェードアウト）</summary>
    private class PillarAnim : MonoBehaviour
    {
        private const float LIFE = 1.4f;
        private float _t;
        private Material _mat;
        private Color _base;

        private void Start()
        {
            _mat = GetComponent<MeshRenderer>().material;
            _base = _mat.HasProperty("_TintColor") ? _mat.GetColor("_TintColor")
                  : _mat.HasProperty("_BaseColor") ? _mat.GetColor("_BaseColor")
                  : _mat.color;
            transform.localScale = new Vector3(0.2f, 4.5f, 0.2f);
        }

        private void Update()
        {
            _t += Time.deltaTime;

            // 半径方向にぐっと広がる（高さは固定）
            float grow = Mathf.Clamp01(_t / 0.25f);
            float radius = Mathf.Lerp(0.2f, 1.5f, 1f - (1f - grow) * (1f - grow));
            transform.localScale = new Vector3(radius, 4.5f, radius);
            transform.Rotate(0, 60f * Time.deltaTime, 0, Space.Self);

            // 後半でフェードアウト
            float fade = Mathf.Clamp01(1f - (_t - LIFE * 0.35f) / (LIFE * 0.65f));
            var c = _base;
            c.a = _base.a * fade;
            SetFxColor(_mat, c);

            if (_t >= LIFE) Destroy(gameObject);
        }
    }

    /// <summary>属性ごとのエフェクトカラー（メイン色とグラデーション先の2色）</summary>
    private static (Color primary, Color secondary) ElementColors(string element) => element switch
    {
        "fire"    => (new Color(1.00f, 0.40f, 0.15f), new Color(0.90f, 0.08f, 0.05f)), // 赤〜深紅
        "water"   => (new Color(0.30f, 0.65f, 1.00f), new Color(0.10f, 0.30f, 0.95f)), // 水色〜青
        "nature"  => (new Color(0.45f, 0.95f, 0.45f), new Color(0.12f, 0.70f, 0.30f)), // 黄緑〜深緑
        "thunder" => (new Color(1.00f, 0.90f, 0.30f), new Color(1.00f, 0.55f, 0.05f)), // 黄〜オレンジ
        _         => (Color.white, new Color(0.8f, 0.8f, 0.8f)),
    };

    /// <summary>戦士: 画面を走る巨大斬撃の連閃＋衝撃波＋火花（色は属性カラー）</summary>
    private void PlayWarriorEffect(Vector3 center, float t, Color primary, Color secondary)
    {
        // 地を走る衝撃波（斬撃の踏み込み）
        SpawnShockwaveRing(primary, Mathf.Lerp(4f, 8f, t));

        // 巨大な斬撃線が連続で走る（2〜3閃）
        PlayWarriorSlashesAsync(t, primary).Forget();

        // 斬った瞬間に散る火花
        SpawnBurst(center, primary, secondary,
            count: (int)Mathf.Lerp(25, 90, t), speed: Mathf.Lerp(6f, 13f, t),
            size: Mathf.Lerp(0.10f, 0.22f, t), life: 0.7f, coneAngle: 75f, stretch: true, trails: true);
    }

    /// <summary>斬撃線を時間差で複数走らせる</summary>
    private async UniTaskVoid PlayWarriorSlashesAsync(float t, Color primary)
    {
        int slashCount = t > 0.6f ? 3 : 2;
        for (int i = 0; i < slashCount; i++)
        {
            float angle = 35f + i * 75f + Random.Range(-12f, 12f); // 交差する角度で
            SpawnSlash(angle, Mathf.Lerp(5.5f, 8.5f, t), primary);
            await UniTask.Delay(Random.Range(80, 130));
        }
    }

    /// <summary>魔法使い: 魔法陣＋立ちのぼる魔力のらせん＋軌跡を引く魔力球＋スターバースト（色は属性カラー）</summary>
    private void PlayMagicianEffect(Vector3 center, float t, Color primary, Color secondary)
    {
        // 床に展開する魔法陣と魔力の波動（魔法使い専用の「らしさ」）
        SpawnMagicCircle(primary, Mathf.Lerp(2.6f, 4.8f, t), Mathf.Lerp(1.2f, 1.8f, t));
        SpawnShockwaveRing(primary, Mathf.Lerp(4f, 7f, t));

        // らせん上昇する魔力の粒
        var ps = SpawnBurst(center, primary, secondary,
            count: (int)Mathf.Lerp(40, 140, t), speed: Mathf.Lerp(1.5f, 3f, t),
            size: Mathf.Lerp(0.09f, 0.16f, t), life: 1.9f, coneAngle: 20f, stretch: false);
        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.orbitalY = new ParticleSystem.MinMaxCurve(Mathf.Lerp(3f, 7f, t));
        var main = ps.main;
        main.gravityModifier = -0.22f;

        // 軌跡を引いて舞い上がる魔力球
        var orbs = SpawnBurst(center, Color.Lerp(primary, Color.white, 0.25f), secondary,
            count: (int)Mathf.Lerp(6, 16, t), speed: 2.8f,
            size: Mathf.Lerp(0.18f, 0.3f, t), life: 1.8f, coneAngle: 30f, stretch: false, trails: true);
        var oMain = orbs.main;
        oMain.gravityModifier = -0.3f;
        var oVel = orbs.velocityOverLifetime;
        oVel.enabled = true;
        oVel.orbitalY = new ParticleSystem.MinMaxCurve(3.5f);

        // 中心のスターバースト（白→属性色の閃き）
        SpawnBurst(center + Vector3.up * 0.5f, Color.white, primary,
            count: (int)Mathf.Lerp(20, 50, t), speed: 9f,
            size: 0.1f, life: 0.4f, coneAngle: 85f, stretch: true);
    }

    /// <summary>弓使い: 上空から矢の雨が降り注ぎ、床に突き刺さる（色は属性カラー）</summary>
    private void PlayArcherEffect(Vector3 center, float t, Color primary, Color secondary)
    {
        PlayArrowVolleyAsync(t, primary).Forget();
    }

    /// <summary>矢を時間差でばらばらと降らせる</summary>
    private async UniTaskVoid PlayArrowVolleyAsync(float t, Color primary)
    {
        int arrows = (int)Mathf.Lerp(6, 18, t);
        for (int i = 0; i < arrows; i++)
        {
            SpawnArrow(primary);
            await UniTask.Delay(Random.Range(30, 90));
        }
    }

    /// <summary>1本の矢を上空に生成して落とす（軌跡付き、着地で土煙）</summary>
    private void SpawnArrow(Color primary)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = "__Arrow";
        Destroy(go.GetComponent<Collider>()); // 物理は使わず手動で落とす
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(Random.Range(-2.2f, 2.2f), 6.5f, Random.Range(-2.2f, 2.2f));
        go.transform.localRotation = Quaternion.Euler(Random.Range(-12f, 12f), Random.Range(0f, 360f), Random.Range(-12f, 12f));
        go.transform.localScale = new Vector3(0.05f, 0.34f, 0.05f);
        go.GetComponent<MeshRenderer>().material = MakeMaterial(new Color(0.55f, 0.38f, 0.2f), 0.4f); // 木の矢柄

        // 風切りの軌跡
        var trail = go.AddComponent<TrailRenderer>();
        trail.material = GetFxMaterial();
        trail.time = 0.22f;
        trail.startWidth = 0.08f;
        trail.endWidth = 0f;
        trail.startColor = primary;
        trail.endColor = new Color(primary.r, primary.g, primary.b, 0f);

        var drop = go.AddComponent<ArrowDrop>();
        drop.sim = this;
        drop.color = primary;

        Destroy(go, 4f); // 念のための後始末
    }

    /// <summary>矢の落下・着地・消滅を制御する</summary>
    private class ArrowDrop : MonoBehaviour
    {
        public Dice3DSimulator sim;
        public Color color;
        private const float SPEED = 17f;
        private bool _landed;
        private float _landedTime;

        private void Update()
        {
            if (!_landed)
            {
                // 矢柄の向き（ローカル-Y方向）に沿って落ちる
                transform.localPosition += transform.localRotation * Vector3.down * (SPEED * Time.deltaTime);
                if (transform.localPosition.y <= 0.34f)
                {
                    _landed = true; // 床に突き刺さって止まる
                    if (sim != null) sim.SpawnImpactPuff(transform.localPosition, color, 0.6f);
                }
            }
            else
            {
                _landedTime += Time.deltaTime;
                if (_landedTime > 1.1f)
                {
                    // すっと縮んで消える
                    transform.localScale *= 1f - Time.deltaTime * 4f;
                    if (transform.localScale.y < 0.02f) Destroy(gameObject);
                }
            }
        }
    }

    /// <summary>銃使い: 画面端からの連射（マズルフラッシュ→弾道→着弾）＋薬莢＋硝煙（色は属性カラー）</summary>
    private void PlayGunnerEffect(Vector3 center, float t, Color primary, Color secondary)
    {
        FireGunshotsAsync(t, primary, secondary).Forget();
    }

    /// <summary>「ダダダッ」と時間差で連射する</summary>
    private async UniTaskVoid FireGunshotsAsync(float t, Color primary, Color secondary)
    {
        Vector3 muzzle = new Vector3(-2.6f, 3.4f, -2.3f); // 画面左上奥の射撃位置
        int shots = (int)Mathf.Lerp(3, 8, t);

        // 薬莢（射撃位置から弾けて床でバウンド）
        var casings = SpawnBurst(muzzle, Color.Lerp(primary, Color.white, 0.2f), secondary,
            count: shots * 2, speed: 3.5f, size: 0.09f, life: 1.8f, coneAngle: 60f, stretch: false);
        var cMain = casings.main;
        cMain.gravityModifier = 1.2f;
        var cCol = casings.collision;
        cCol.enabled = true;
        cCol.type = ParticleSystemCollisionType.World;
        cCol.bounce = 0.55f;

        for (int i = 0; i < shots; i++)
        {
            // マズルフラッシュ（閃光＋火花）
            SpawnFlashLight(muzzle, primary, 6f);
            SpawnBurst(muzzle, primary, secondary,
                count: 18, speed: 7f, size: 0.17f, life: 0.25f, coneAngle: 40f, stretch: true);

            // 弾丸がトレイ中央付近へ飛ぶ
            var target = new Vector3(Random.Range(-1.3f, 1.3f), 0.15f, Random.Range(-1.3f, 1.3f));
            FireBulletAsync(muzzle, target, primary, secondary).Forget();

            await UniTask.Delay(Random.Range(70, 120));
        }

        // 連射後に硝煙が立ちのぼる
        var smoke = SpawnBurst(muzzle, new Color(0.55f, 0.55f, 0.58f, 0.5f), new Color(0.3f, 0.3f, 0.32f, 0f),
            count: (int)Mathf.Lerp(6, 16, t), speed: 1.2f,
            size: Mathf.Lerp(0.35f, 0.6f, t), life: 2f, coneAngle: 30f, stretch: false);
        var sMain = smoke.main;
        sMain.gravityModifier = -0.05f;
        var sol = smoke.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.6f, 1, 1.8f));
    }

    /// <summary>1発の弾丸を飛ばして着弾火花を出す</summary>
    private async UniTaskVoid FireBulletAsync(Vector3 from, Vector3 to, Color primary, Color secondary)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "__Bullet";
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(transform, false);
        go.transform.localPosition = from;
        go.transform.localScale = Vector3.one * 0.14f;
        go.GetComponent<MeshRenderer>().material = GetFxMaterial();

        // 弾道の軌跡（トレーサー・太め）
        var trail = go.AddComponent<TrailRenderer>();
        trail.material = GetFxMaterial();
        trail.time = 0.18f;
        trail.startWidth = 0.15f;
        trail.endWidth = 0f;
        trail.startColor = Color.Lerp(primary, Color.white, 0.5f);
        trail.endColor = new Color(primary.r, primary.g, primary.b, 0f);

        // 高速で直進（約0.1秒で着弾）
        const float duration = 0.1f;
        float e = 0f;
        while (e < duration && go != null)
        {
            go.transform.localPosition = Vector3.Lerp(from, to, e / duration);
            await UniTask.Yield();
            e += Time.deltaTime;
        }
        if (go == null) return;

        // 着弾: 大きめの火花＋閃光＋床に銃痕を残す
        SpawnImpactPuff(to, primary, 2f);
        SpawnFlashLight(to + Vector3.up * 0.4f, primary, 4f);
        SpawnBulletHole(to);
        Destroy(go);
    }

    /// <summary>着弾地点の床に銃痕（焦げた弾痕デカール）を残す。しばらくしてフェードアウト</summary>
    private void SpawnBulletHole(Vector3 localPos)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = "__BulletHole";
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(transform, false);
        // 床のわずか上に置く（複数の銃痕同士のチラつき防止に高さを微妙にずらす）
        go.transform.localPosition = new Vector3(localPos.x, 0.065f + Random.Range(0f, 0.008f), localPos.z);
        go.transform.localRotation = Quaternion.Euler(90, Random.Range(0f, 360f), 0);

        // 暗い跡なので加算ではなく半透明（アルファブレンド）のマテリアルを使う
        var mat = CloneMaterial("DiceFX/DiceSprite", "Sprites/Default");
        if (mat == null) { Destroy(go); return; }
        mat.mainTexture = GetBulletHoleTexture();
        SetFxColor(mat, Color.white);
        go.GetComponent<MeshRenderer>().material = mat;

        var anim = go.AddComponent<QuadFxAnim>();
        anim.targetScale = Random.Range(0.42f, 0.55f);
        anim.life = 3.0f;        // しばらく残ってから消える
        anim.rotateSpeed = 0f;
        anim.expandTime = 0.05f; // ほぼ一瞬で現れる
    }

    /// <summary>銃痕テクスチャ（黒い弾痕＋焦げた縁）</summary>
    private Texture2D _bulletHoleTex;
    private Texture2D GetBulletHoleTexture()
    {
        if (_bulletHoleTex != null) return _bulletHoleTex;
        const int S = 64;
        _bulletHoleTex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                float dx = (x - S / 2f) / (S / 2f);
                float dy = (y - S / 2f) / (S / 2f);
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                // ギザギザ感を出すための角度ノイズ
                float jag = 1f + 0.12f * Mathf.Sin(Mathf.Atan2(dy, dx) * 7f);

                Color c;
                if (r < 0.30f * jag)
                    c = new Color(0.04f, 0.03f, 0.02f, 0.95f);              // 中心の黒い穴
                else if (r < 0.62f * jag)
                    c = new Color(0.12f, 0.08f, 0.05f, 0.7f * (1f - r));    // 焦げた縁
                else
                    c = new Color(0.1f, 0.07f, 0.04f, Mathf.Clamp01(0.25f * (1f - r))); // うっすら煤
                _bulletHoleTex.SetPixel(x, y, c);
            }
        }
        _bulletHoleTex.Apply();
        return _bulletHoleTex;
    }

    /// <summary>着弾・着地の小さな火花/土煙</summary>
    private void SpawnImpactPuff(Vector3 localPos, Color color, float scale)
    {
        SpawnBurst(localPos, color, Color.Lerp(color, Color.white, 0.6f),
            count: (int)(8 * scale), speed: 3.5f * scale,
            size: 0.08f * scale, life: 0.4f, coneAngle: 70f, stretch: true);
    }

    /// <summary>巨大な斬撃線（横に伸びて一閃しフェードアウトする平面）</summary>
    private void SpawnSlash(float angleDeg, float length, Color color)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = "__Slash";
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0, 1.6f, 0);
        go.transform.localRotation = Quaternion.Euler(90, 0, angleDeg); // 床と平行に置いて回転

        var mat = new Material(GetFxMaterial());
        mat.mainTexture = GetSlashTexture();
        if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", GetSlashTexture());
        SetFxColor(mat, Color.Lerp(color, Color.white, 0.4f));
        go.GetComponent<MeshRenderer>().material = mat;

        var anim = go.AddComponent<SlashAnim>();
        anim.length = length;
    }

    /// <summary>斬撃線のアニメ（一瞬で伸びる→フェードアウト）</summary>
    private class SlashAnim : MonoBehaviour
    {
        public float length = 7f;
        private const float LIFE = 0.45f;
        private const float GROW_TIME = 0.10f;
        private float _t;
        private Material _mat;
        private Color _base;

        private void Start()
        {
            _mat = GetComponent<MeshRenderer>().material;
            _base = _mat.HasProperty("_TintColor") ? _mat.GetColor("_TintColor")
                  : _mat.HasProperty("_BaseColor") ? _mat.GetColor("_BaseColor")
                  : _mat.color;
            transform.localScale = new Vector3(0.3f, 0.9f, 1f);
        }

        private void Update()
        {
            _t += Time.deltaTime;

            // 一瞬で横に伸びる（イーズアウト）
            float grow = Mathf.Clamp01(_t / GROW_TIME);
            float x = Mathf.Lerp(0.3f, length, 1f - (1f - grow) * (1f - grow));
            transform.localScale = new Vector3(x, 0.9f, 1f);

            // 後半でフェードアウト
            float fade = Mathf.Clamp01(1f - (_t - LIFE * 0.35f) / (LIFE * 0.65f));
            var c = _base;
            c.a = _base.a * fade;
            SetFxColor(_mat, c);

            if (_t >= LIFE) Destroy(gameObject);
        }
    }

    /// <summary>斬撃線テクスチャ（中心が鋭く光る横長の閃）</summary>
    private Texture2D _slashTex;
    private Texture2D GetSlashTexture()
    {
        if (_slashTex != null) return _slashTex;
        const int W = 256, H = 64;
        _slashTex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        for (int y = 0; y < H; y++)
        {
            for (int x = 0; x < W; x++)
            {
                float nx = Mathf.Abs((x - W / 2f) / (W / 2f)); // 0=中央, 1=端
                float ny = Mathf.Abs((y - H / 2f) / (H / 2f));
                // 縦方向は鋭く減衰、横方向は端に向かって先細り
                float a = Mathf.Pow(Mathf.Clamp01(1f - ny), 2.5f) * Mathf.Pow(Mathf.Clamp01(1f - nx), 0.55f);
                a += Mathf.Pow(Mathf.Clamp01(1f - ny), 9f) * 0.6f * Mathf.Clamp01(1f - nx); // 中心の白い芯
                _slashTex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(a)));
            }
        }
        _slashTex.Apply();
        return _slashTex;
    }

    /// <summary>パーティクルバーストを生成して再生する共通ヘルパー</summary>
    private ParticleSystem SpawnBurst(Vector3 localPos, Color colorFrom, Color colorTo,
        int count, float speed, float size, float life, float coneAngle, bool stretch,
        bool trails = false)
    {
        var go = new GameObject("__FX");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = localPos;

        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop();

        var main = ps.main;
        main.loop = false;
        main.playOnAwake = false;
        main.duration = 0.2f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.6f, life);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.55f, speed);
        main.startSize = new ParticleSystem.MinMaxCurve(size * 0.6f, size);
        main.startColor = new ParticleSystem.MinMaxGradient(colorFrom, colorTo);
        main.maxParticles = 512;

        var emission = ps.emission;
        emission.rateOverTime = 0;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = coneAngle;
        shape.radius = 0.25f;

        // 透明度のフェードアウト
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
        col.color = grad;

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.material = GetFxMaterial();
        if (stretch)
        {
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = 0.08f;
            renderer.lengthScale = 1.6f;
        }

        // 軌跡（トレイル）
        if (trails)
        {
            var trail = ps.trails;
            trail.enabled = true;
            trail.lifetime = new ParticleSystem.MinMaxCurve(0.25f);
            trail.ratio = 0.85f;
            trail.dieWithParticles = true;
            trail.widthOverTrail = new ParticleSystem.MinMaxCurve(0.5f, AnimationCurve.Linear(0, 1f, 1, 0f));
            renderer.trailMaterial = GetFxMaterial();
        }

        ps.Play();
        Destroy(go, life + 1.5f);
        return ps;
    }

    /// <summary>床に展開する魔法陣（回転しながら広がってフェードアウト）</summary>
    private void SpawnMagicCircle(Color color, float scale, float life)
    {
        var go = CreateFlatQuad(GetMagicCircleTexture(), color);
        var anim = go.AddComponent<QuadFxAnim>();
        anim.targetScale = scale;
        anim.life = life;
        anim.rotateSpeed = 80f;
        anim.expandTime = 0.35f;
    }

    /// <summary>地を走る衝撃波リング（高速に広がってすぐ消える）</summary>
    private void SpawnShockwaveRing(Color color, float scale)
    {
        var go = CreateFlatQuad(GetRingTexture(), color);
        var anim = go.AddComponent<QuadFxAnim>();
        anim.targetScale = scale;
        anim.life = 0.55f;
        anim.rotateSpeed = 0f;
        anim.expandTime = 0.45f;
    }

    /// <summary>床上の平面クアッドを作る（魔法陣・リング共通）</summary>
    private GameObject CreateFlatQuad(Texture2D tex, Color color)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = "__QuadFX";
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0, 0.06f, 0);
        go.transform.localRotation = Quaternion.Euler(90, 0, 0);
        go.transform.localScale = Vector3.one * 0.3f;

        var mat = new Material(GetFxMaterial());
        mat.mainTexture = tex;
        if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
        SetFxColor(mat, color);
        go.GetComponent<MeshRenderer>().material = mat;
        return go;
    }

    /// <summary>FXマテリアルの色設定（シェーダーによりプロパティ名が違うため両対応）</summary>
    private static void SetFxColor(Material mat, Color color)
    {
        if (mat.HasProperty("_TintColor")) mat.SetColor("_TintColor", color); // Legacy Additive
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
    }

    /// <summary>平面エフェクトのアニメ（拡大→回転→フェードアウト→破棄）</summary>
    private class QuadFxAnim : MonoBehaviour
    {
        public float targetScale = 4f;
        public float life = 1.5f;
        public float rotateSpeed = 80f;
        public float expandTime = 0.35f;

        private float _t;
        private Material _mat;
        private Color _baseColor;

        private void Start()
        {
            _mat = GetComponent<MeshRenderer>().material;
            _baseColor = _mat.HasProperty("_TintColor") ? _mat.GetColor("_TintColor")
                       : _mat.HasProperty("_BaseColor") ? _mat.GetColor("_BaseColor")
                       : _mat.color;
        }

        private void Update()
        {
            _t += Time.deltaTime;

            // イーズアウトで拡大
            float expand = Mathf.Clamp01(_t / expandTime);
            float scale = Mathf.Lerp(0.3f, targetScale, 1f - (1f - expand) * (1f - expand));
            transform.localScale = Vector3.one * scale;

            // 回転（クアッドは90°倒してあるのでローカルZ軸まわり）
            if (rotateSpeed != 0f)
                transform.Rotate(0, 0, rotateSpeed * Time.deltaTime, Space.Self);

            // 後半でフェードアウト
            float fade = Mathf.Clamp01(1f - (_t - life * 0.45f) / (life * 0.55f));
            var c = _baseColor;
            c.a = _baseColor.a * fade;
            SetFxColor(_mat, c);

            if (_t >= life) Destroy(gameObject);
        }
    }

    /// <summary>出目の強さに応じてカメラを揺らす</summary>
    private bool _shaking;
    private async UniTaskVoid ShakeCameraAsync(float strength)
    {
        if (_cam == null || _shaking) return;
        _shaking = true;
        var tr = _cam.transform;
        Vector3 origin = tr.localPosition;

        const float duration = 0.45f;
        float elapsed = 0f;
        while (elapsed < duration && tr != null)
        {
            float decay = 1f - elapsed / duration;
            tr.localPosition = origin + Random.insideUnitSphere * strength * decay;
            await UniTask.Yield();
            elapsed += Time.deltaTime;
        }
        if (tr != null) tr.localPosition = origin;
        _shaking = false;
    }

    // ---- 魔法陣・リングのテクスチャ（コード生成・キャッシュ） ----
    private Texture2D _magicCircleTex;
    private Texture2D _ringTex;

    private Texture2D GetMagicCircleTexture()
    {
        if (_magicCircleTex != null) return _magicCircleTex;
        const int S = 256;
        _magicCircleTex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                float dx = (x - S / 2f) / (S / 2f);
                float dy = (y - S / 2f) / (S / 2f);
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float ang = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg + 180f;

                float a = 0f;
                // 三重のリング
                a += RingAlpha(r, 0.92f, 0.030f);
                a += RingAlpha(r, 0.62f, 0.020f);
                a += RingAlpha(r, 0.34f, 0.025f);
                // 放射状のスポーク（12本、内側リング〜外側リングの間）
                if (r > 0.34f && r < 0.92f && Mathf.Abs(((ang % 30f) + 30f) % 30f - 15f) < 0.9f)
                    a = Mathf.Max(a, 0.85f);
                // ルーン風の破線リング
                if (Mathf.Abs(r - 0.77f) < 0.045f && ((int)(ang / 9f)) % 2 == 0)
                    a = Mathf.Max(a, 0.9f);

                _magicCircleTex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(a)));
            }
        }
        _magicCircleTex.Apply();
        return _magicCircleTex;
    }

    private Texture2D GetRingTexture()
    {
        if (_ringTex != null) return _ringTex;
        const int S = 128;
        _ringTex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                float dx = (x - S / 2f) / (S / 2f);
                float dy = (y - S / 2f) / (S / 2f);
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                _ringTex.SetPixel(x, y, new Color(1f, 1f, 1f, RingAlpha(r, 0.86f, 0.10f)));
            }
        }
        _ringTex.Apply();
        return _ringTex;
    }

    private static float RingAlpha(float r, float center, float width)
        => Mathf.Clamp01(1f - Mathf.Abs(r - center) / width);

    /// <summary>強さに応じた閃光（すぐにフェードアウトするポイントライト）</summary>
    private void SpawnFlashLight(Vector3 localPos, Color color, float intensity)
    {
        var go = new GameObject("__Flash");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = localPos;
        var light = go.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = color;
        light.intensity = intensity;
        light.range = 12f;
        go.AddComponent<LightFader>();
    }

    /// <summary>ポイントライトを減衰させて消すヘルパー</summary>
    private class LightFader : MonoBehaviour
    {
        private Light _light;
        private void Awake() { _light = GetComponent<Light>(); }
        private void Update()
        {
            if (_light == null) { Destroy(gameObject); return; }
            _light.intensity -= Time.deltaTime * 14f;
            if (_light.intensity <= 0f) Destroy(gameObject);
        }
    }

    /// <summary>パーティクル用マテリアル（ソフト円テクスチャをコード生成）</summary>
    private Material GetFxMaterial()
    {
        if (_fxMat != null) return _fxMat;

        _fxMat = CloneMaterial("DiceFX/DiceFx",
            "Legacy Shaders/Particles/Additive", "Universal Render Pipeline/Particles/Unlit", "Sprites/Default");
        if (_fxMat == null) return null;

        // 中心が明るく外周へ柔らかく消える円形テクスチャ
        const int S = 64;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                float dx = (x - S / 2f) / (S / 2f);
                float dy = (y - S / 2f) / (S / 2f);
                float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                a *= a;
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }
        tex.Apply();
        _fxMat.mainTexture = tex;
        if (_fxMat.HasProperty("_BaseMap")) _fxMat.SetTexture("_BaseMap", tex);
        return _fxMat;
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

            // 四角形の面（d10の凧型など）は完全な平面ではないことがあるため、
            // 2つの三角形の法線の平均を「面の代表法線」として出目判定・数字配置に使う
            Vector3 faceNormal = normal;
            if (polyList.Count >= 4)
            {
                var nA = Vector3.Cross(polyList[1] - polyList[0], polyList[2] - polyList[0]).normalized;
                var nB = Vector3.Cross(polyList[2] - polyList[0], polyList[3] - polyList[0]).normalized;
                faceNormal = (nA + nB).normalized;
            }

            // 非平面の面では折り目に数字が食い込むため、浮かせ量を面の形状に応じて変える
            float labelOffset = polyList.Count >= 4 && faces == 10 ? 0.09f : 0.025f;

            faceInfos.Add(new FaceInfo { center = center, normal = faceNormal, value = value });
            AttachNumberLabel(go.transform, center, faceNormal, value, labelOffset);
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
    private void AttachNumberLabel(Transform parent, Vector3 center, Vector3 normal, int value, float offset = 0.025f)
    {
        var go = new GameObject($"__Num_{value}");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = center + normal * offset;

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
        // Resources のマテリアル（DiceFxMaterialCreator が生成）をベースに複製する。
        // 実行時の Shader.Find だけに頼るとビルド時にシェーダーが除外されて実機で null になるため。
        var mat = CloneMaterial("DiceFX/DiceLit",
            "Universal Render Pipeline/Lit", "Standard");
        if (mat == null) return null;

        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
        // 光沢（URPは_Smoothness、Standardは_Glossiness）
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
        if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", smoothness);
        return mat;
    }

    /// <summary>Resourcesのベースマテリアルを複製する（無ければシェーダー名から生成を試みる）</summary>
    private static Material CloneMaterial(string resourcePath, params string[] fallbackShaders)
    {
        var baseMat = Resources.Load<Material>(resourcePath);
        if (baseMat != null) return new Material(baseMat);

        foreach (var sn in fallbackShaders)
        {
            var shader = Shader.Find(sn);
            if (shader != null) return new Material(shader);
        }

        Debug.LogError($"[Dice3D] マテリアルが見つかりません: Resources/{resourcePath}。" +
                       "Unityエディタを一度開いて DiceFX マテリアルを生成してから再ビルドしてください");
        return null;
    }
}
