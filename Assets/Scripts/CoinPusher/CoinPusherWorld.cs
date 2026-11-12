using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>コインプッシャーの台に乗る物（保存するときの番号でもあるので、並びを変えない）</summary>
public enum PusherItem
{
    Coin = 0,         // G のコイン（受け皿に落ちると 1G）
    MeepleRed = 1,    // 赤コマ
    MeepleBlue = 2,   // 青コマ
    MeepleYellow = 3, // 黄コマ
    StarFragment = 4, // 星のカケラ
    MeepleBlack = 5,  // 黒コマ（2026-11-12 追加。コインタワーのボーナスで落ちてくる）
    PlanetPotion = 6, // 惑星回復薬（2026-11-12 追加。ボーナスの景品）
}

/// <summary>
/// コインプッシャーの台（2026-11-12）。シーンの遠く（y=-2000）に 3D の台と専用カメラを作り、映像を RenderTexture に描く
/// （Dice3DSimulator と同じやり方。CoinPusherController が全画面の RawImage に出す）。
/// 台の形（台の上面が y=0、手前のふちが z=0、奥が +z）:
///  ・手前のふちから落ちた物は受け皿へ（獲得）。手前の左右 SIDE_OPEN だけ横の壁がなく、そこから横に落ちた物は没収
///  ・プッシャー（台の幅いっぱいの箱）が奥で前後に動き、前の物を押し出す。いちばん奥に下がっても、上面はコイン2枚分
///    （PUSH_TOP_KEEP）奥の壁より手前に残るので、上に乗った物は全部は落ちない。それより奥の物は、下がるときに奥の壁で前へ押される
///  ・コインは、狙った所（プッシャーの上をタップした所）の真上の投入口から、まっすぐ下へ落ちる。
///    落ちる先は、落ちたときにプッシャーの上になる範囲に収める（手前の台へ直接は落とせない）
/// 本物らしい速さに見えるよう、開いているあいだだけ重力を強く・物理の刻みを細かくする（閉じたら元に戻す）。
/// 台の上の物の位置は、CoinPusherController が端末に保存して、次に開いたときに続きから（Save / Load）。
/// </summary>
public sealed class CoinPusherWorld : MonoBehaviour
{
    public const float HALF_W = 3.0f;          // 台の左右の半分の幅
    // 手前の左右の、横の壁がないところ（ここから横に落ちると没収）。2026-11-12 ユーザー指定「壁をもう少し短くして、
    // 横に落ちやすく」で 1.0→1.6（プッシャーがいちばん手前に来たときの前の台 2.6 のうち、手前 1.6 は横があいている）
    public const float SIDE_OPEN = 1.6f;
    public const float PUSHER_H = 0.6f;        // プッシャーの高さ
    public const float PUSHER_D = 4.4f;        // プッシャーの奥行き
    public const float PUSH_NEAR = 2.6f;       // プッシャーの前の面がいちばん手前に来たとき
    public const float PUSH_FAR = 4.0f;        // いちばん奥に下がったとき
    public const float PUSH_PERIOD = 3.6f;     // 1往復の秒数
    /// <summary>
    /// いちばん奥に下がったときも、プッシャーの上面を奥の壁より手前に残す奥行き（コイン2枚分＋すき間）。
    /// 2026-11-12 ユーザー指定「全部しまわれると上に乗っている物が全部落ちてしまうので、コイン2枚分は残す」
    /// （それまでは壁が PUSH_FAR のすぐ奥の 4.15 で、上面は 0.15 しか残らなかった）
    /// </summary>
    public const float PUSH_TOP_KEEP = 2f * (2f * CoinPusherMeshes.COIN_R) + 0.1f;
    public const float WALL_Z = PUSH_FAR + PUSH_TOP_KEEP; // 奥の壁の前の面（5.7。ここより奥に行ったプッシャーの上の物を前へ押す）
    public const float DROP_Y = 3.4f;          // 投入口の高さ（狙った所の真上のこの高さから落とす）
    public const float DROP_HALF_W = 2.5f;     // 狙える左右の半分の幅
    private const float AIM_Z_DEFAULT = (PUSH_FAR + WALL_Z) * 0.5f; // はじめの狙いの奥行き（いちばん奥に下がっても上面が残る所のまん中）
    private const float FALL_TIME = 0.23f;     // 投入口からプッシャーの上まで落ちる時間（重力96・初めの速さ3で約0.21〜0.22秒）
    private const float ITEM_HALF = 0.55f;     // コマ・星のカケラ・惑星回復薬の半分の大きさ（向きが変わってもほぼこの円に収まる）
    private const float BONUS_HALF_W = DROP_HALF_W * 0.8f; // ボーナスの景品を落とす左右の範囲
    // コインタワーの中心とプッシャーの前の面の距離（いちばん奥に下がっても上面が残る帯のまん中。壁にも前のふちにも当たらない）
    private const float TOWER_FROM_FRONT = PUSH_TOP_KEEP * 0.5f;
    private const float TOWER_HALF_W = 1.8f;   // コインタワーを置く左右の範囲
    // タワーと一緒に落とすコマは、タワーの中心から左右にこれだけ離す（コインの半径＋コマの半分＋すき間。落ちてタワーを崩さないように）
    private const float TOWER_CLEAR = CoinPusherMeshes.COIN_R + ITEM_HALF + 0.25f;
    public const float GONE_Y = -1.2f;         // ここより下に落ちたら、手前なら獲得・それ以外は没収
    public const int MAX_ITEMS = 220;          // 台に乗せられる数（これ以上はコインを入れられない）
    // 重力と物理の刻み。2026-11-12 ユーザー指定「落ちる速さを今の倍に（プッシャーから落ちるときも含めて）」で、
    // 重力 24→96・刻み 1/60→1/120 にした（落ちる時間を半分＝速さ2倍にするには重力4倍。刻みも半分にすると、1回の刻みでの
    // 動きが前と同じになり、積み重なったコインの安定は前のまま。プッシャーの往復の速さは変えていない）。
    // 減衰・押し戻し・眠る基準（Spawn）と落とすときの速さ（DropAt）も、同じ2倍速に合わせてある
    private const float GRAVITY = 96f;
    private const float FIXED_DT = 1f / 120f;
    private const float MAX_DELTA = 0.1f;      // 1フレームで進める物理の最大の時間（重い端末で刻みが追いつかず固まるのを防ぐ）
    // 縦に立ったコインを倒す（2026-11-12 ユーザー指定「縦になりにくく」。当たり判定の形でも立ちにくくしてある）
    private const float STAND_CHECK = 0.2f;    // 調べる間隔（秒）
    private const float STAND_DOT = 0.5f;      // コインの軸と真上の内積の絶対値がこれ未満＝60°より傾いている＝立っている
    private const float STAND_WAIT = 0.5f;     // これだけ立って止まったままなら倒す
    private const float TOPPLE_MARGIN = 1.3f;  // 倒すときの回転は、真上を越えるのにちょうど足りる速さのこの倍
    private const float TOPPLE_MIN = 3f;       // 倒すときの回転の速さの下限・上限（ラジアン/秒。上限は回転の上限 14 の内）
    private const float TOPPLE_MAX = 12f;

    /// <summary>手前の受け皿に落ちた（獲得。位置はワールド座標）</summary>
    public event Action<PusherItem, Vector3> Won;
    /// <summary>横から落ちた（没収）</summary>
    public event Action<PusherItem> Lost;

    public RenderTexture Texture => _rt;
    public Camera Cam => _cam;
    public int ItemCount => _items.Count;

    private sealed class Item
    {
        public PusherItem Kind;
        public Rigidbody Rb;
        public float StandSince = -1f; // 縦に立って止まった時刻（立っていなければ -1）
        public bool ToppleBack;        // 次に倒すときは、もたれている側へ（交互に試す）
    }

    private readonly List<Item> _items = new List<Item>();
    private readonly List<UnityEngine.Object> _owned = new List<UnityEngine.Object>(); // 閉じるときに片づける材質・形・絵
    private Rigidbody _pusher;
    private Transform _chute, _rail;
    private Camera _cam;
    private RenderTexture _rt;
    private Mesh _unitBox;
    private float _time, _nextStandCheck;
    // 狙っている所（台の中の x・z）。プッシャーの上をタップした所。z は落とすときに、その時プッシャーの上に落ちる範囲へ収める
    // （2026-11-12 ユーザー指定「プッシャーのタップした位置にコインを落とす」。それまでは左右だけで、奥行きは決まった位置 3.3）
    private float _aimX, _aimZ = AIM_Z_DEFAULT;
    private Vector3 _oldGravity;
    private float _oldFixed, _oldMaxDelta;
    private bool _physicsChanged;
    private PhysicsMaterial _itemPm, _floorPm, _wallPm;
    private Material _coinFace, _coinRim, _star, _meepleRed, _meepleBlue, _meepleYellow;
    private Material _meepleBlack, _potionGlass, _potionCork, _paint; // _paint は頂点の色で塗る光らない材質（黒コマのふち・薬）

    /// <summary>台を作る。width・height は映像（RenderTexture）の大きさ</summary>
    public static CoinPusherWorld Create(int width, int height)
    {
        var go = new GameObject("__CoinPusherWorld");
        go.transform.position = new Vector3(0f, -2000f, 0f); // ホームの画面・ダイスの台（y=-500）と重ならない遠い場所
        var world = go.AddComponent<CoinPusherWorld>();
        world.Build(Mathf.Max(64, width), Mathf.Max(64, height));
        return world;
    }

    private void OnDestroy()
    {
        if (_physicsChanged)
        {
            Physics.gravity = _oldGravity;
            Time.fixedDeltaTime = _oldFixed;
            Time.maximumDeltaTime = _oldMaxDelta;
            _physicsChanged = false;
        }
        if (_cam != null) _cam.targetTexture = null;
        if (_rt != null) _rt.Release();
        foreach (var o in _owned)
            if (o != null) Destroy(o);
        _owned.Clear();
        if (_rt != null) Destroy(_rt);
    }

    // ================================================================
    // 組み立て
    // ================================================================
    private void Build(int width, int height)
    {
        _oldGravity = Physics.gravity;
        _oldFixed = Time.fixedDeltaTime;
        _oldMaxDelta = Time.maximumDeltaTime;
        Physics.gravity = new Vector3(0f, -GRAVITY, 0f);
        Time.fixedDeltaTime = FIXED_DT;
        Time.maximumDeltaTime = Mathf.Min(_oldMaxDelta, MAX_DELTA);
        _physicsChanged = true;

        _unitBox = CoinPusherMeshes.Box(Vector3.one, Color.white);
        _owned.Add(_unitBox);
        // 景品の当たり判定（凸の形）を先に1回だけ作っておく（同じ形の MeshCollider はこれを使い回す）
        Physics.BakeMesh(CoinPusherMeshes.CoinCollider.GetInstanceID(), true);
        Physics.BakeMesh(CoinPusherMeshes.Meeple.GetInstanceID(), true);
        Physics.BakeMesh(CoinPusherMeshes.Star.GetInstanceID(), true);
        Physics.BakeMesh(CoinPusherMeshes.PotionCollider.GetInstanceID(), true);
        _itemPm = NewPhysicsMaterial("PusherItem", 0.32f, 0.42f, 0.02f);
        _floorPm = NewPhysicsMaterial("PusherFloor", 0.28f, 0.34f, 0f);
        _wallPm = NewPhysicsMaterial("PusherWall", 0.06f, 0.08f, 0f);

        // ---- 材質 ----
        // 金属の度合い（metallic）は低めに: ホームのシーンは環境光が黒で空の映り込みもないので、金属にすると
        // 映り込みが黒くなって色が沈む（ダイスの材質も metallic 0）。つやは smoothness とライトのハイライトで出す
        var face = Resources.Load<Texture2D>("CoinPusher/CoinFace");
        _coinFace = Lit(Color.white, 0.7f, 0.3f, face);
        _coinRim = Lit(SpaceTheme.Hex(0xF2B93B), 0.7f, 0.35f);
        _star = Lit(SpaceTheme.Hex(0xFFD36E), 0.8f, 0.2f);
        _meepleRed = Lit(PlanetColors.Of(PlanetColor.Red), 0.4f, 0f);
        _meepleBlue = Lit(PlanetColors.Of(PlanetColor.Blue), 0.4f, 0f);
        _meepleYellow = Lit(PlanetColors.Of(PlanetColor.Yellow), 0.4f, 0f);
        // 黒コマは黒い面（つやを強めにして、暗い台の上でも光の映りで形が分かるように）＋光るふち。惑星回復薬は淡い水色のガラスと栓
        _meepleBlack = Lit(SpaceTheme.Hex(0x2B2A36), 0.78f, 0.1f);
        _potionGlass = Lit(SpaceTheme.Hex(0xC9ECFF), 0.9f, 0.1f);
        _potionCork = Lit(SpaceTheme.Hex(0xB5824E), 0.3f, 0f);
        _paint = Unlit(Color.white);
        var grid = GridTexture();
        _owned.Add(grid);
        var floor = Lit(SpaceTheme.Hex(0x2A3478), 0.62f, 0.15f, grid, new Vector2(4f, 6f));
        var body = Lit(SpaceTheme.Hex(0x161B44), 0.55f, 0.15f);
        var pusher = Lit(SpaceTheme.Hex(0x51409F), 0.7f, 0.15f);
        var wall = Lit(SpaceTheme.Hex(0x0F1438), 0.5f, 0.1f);
        var tray = Lit(SpaceTheme.Hex(0x4A3610), 0.7f, 0.25f);
        var cyan = Unlit(SpaceTheme.Hex(0x5CE1FF));
        var violet = Unlit(SpaceTheme.Hex(0xB89BFF));
        var gold = Unlit(SpaceTheme.Hex(0xFFD37A));
        var red = Unlit(SpaceTheme.Hex(0xFF5C7A));

        // ---- 台（プッシャーの下・奥の壁の下まで続く） ----
        Solid("Field", new Vector3(0f, -0.25f, 4.75f), new Vector3(HALF_W * 2f, 0.5f, 9.5f), floor, _floorPm);
        Solid("FieldEdge", new Vector3(0f, -0.25f, -0.006f), new Vector3(HALF_W * 2f, 0.5f, 0.012f), gold, null, false); // 手前のふち（獲得の目印）

        // ---- 横の壁（手前の SIDE_OPEN だけ開いている） ----
        float wallLen = 9.5f - SIDE_OPEN;
        foreach (float s in new[] { -1f, 1f })
        {
            float x = s * (HALF_W + 0.15f);
            Solid("Side", new Vector3(x, 1.6f, SIDE_OPEN + wallLen * 0.5f), new Vector3(0.3f, 3.7f, wallLen), body, _wallPm);
            Solid("SideTop", new Vector3(x, 3.47f, SIDE_OPEN + wallLen * 0.5f), new Vector3(0.32f, 0.06f, wallLen), cyan, null, false);
            Solid("SideFront", new Vector3(x, 1.6f, SIDE_OPEN - 0.012f), new Vector3(0.32f, 3.7f, 0.02f), violet, null, false);
            // 横の落とし口（見た目だけ。赤い光＝没収）
            Solid("Gutter", new Vector3(s * (HALF_W + 0.5f), -1.25f, SIDE_OPEN * 0.5f - 0.1f), new Vector3(0.9f, 0.2f, SIDE_OPEN + 0.6f),
                wall, null, false);
            Solid("GutterLine", new Vector3(s * (HALF_W + 0.06f), -0.03f, SIDE_OPEN * 0.5f), new Vector3(0.05f, 0.05f, SIDE_OPEN),
                red, null, false);
        }

        // ---- 奥の壁（プッシャーの上を少しだけあける） ----
        float wallBottom = PUSHER_H + 0.03f, wallTop = 5.2f;
        Solid("BackWall", new Vector3(0f, (wallBottom + wallTop) * 0.5f, WALL_Z + 0.25f),
            new Vector3(HALF_W * 2f + 0.6f, wallTop - wallBottom, 0.5f), wall, _wallPm);
        Solid("BackLine", new Vector3(0f, wallBottom + 0.04f, WALL_Z - 0.012f), new Vector3(HALF_W * 2f, 0.05f, 0.02f), cyan, null, false);
        Solid("BackLine2", new Vector3(0f, 4.55f, WALL_Z - 0.012f), new Vector3(HALF_W * 2f + 0.3f, 0.05f, 0.02f), violet, null, false);
        Sign(new Vector3(0f, 3.95f, WALL_Z - 0.03f));

        // ---- プッシャー（動く箱。光る線も一緒に動く） ----
        var p = new GameObject("Pusher");
        p.transform.SetParent(transform, false);
        p.transform.localPosition = PusherLocal(0f);
        var pb = Solid("Body", Vector3.zero, new Vector3(HALF_W * 2f - 0.04f, PUSHER_H, PUSHER_D), pusher, _floorPm);
        pb.transform.SetParent(p.transform, false);
        var pl = Solid("Edge", new Vector3(0f, PUSHER_H * 0.5f - 0.03f, -PUSHER_D * 0.5f - 0.012f),
            new Vector3(HALF_W * 2f - 0.04f, 0.06f, 0.02f), cyan, null, false);
        pl.transform.SetParent(p.transform, false);
        var pl2 = Solid("Edge2", new Vector3(0f, -PUSHER_H * 0.5f + 0.1f, -PUSHER_D * 0.5f - 0.012f),
            new Vector3(HALF_W * 2f - 0.04f, 0.04f, 0.02f), violet, null, false);
        pl2.transform.SetParent(p.transform, false);
        _pusher = p.AddComponent<Rigidbody>();
        _pusher.isKinematic = true;
        _pusher.useGravity = false;
        _pusher.interpolation = RigidbodyInterpolation.Interpolate;

        // ---- 手前の受け皿・土台（見た目だけ） ----
        Solid("Tray", new Vector3(0f, -1.65f, -1.2f), new Vector3(HALF_W * 2f + 0.6f, 0.3f, 2.3f), tray, null, false);
        Solid("TrayLine", new Vector3(0f, -1.49f, -2.34f), new Vector3(HALF_W * 2f + 0.6f, 0.04f, 0.04f), gold, null, false);
        Solid("Base", new Vector3(0f, -2.6f, 4.5f), new Vector3(16f, 1.6f, 20f), wall, null, false);

        // ---- 投入口（狙った所の真上。レール・半透明のコイン・落ちる所までの線。レールも狙いに合わせて前後に動く） ----
        _rail = Solid("Rail", new Vector3(0f, DROP_Y + 0.5f, AIM_Z_DEFAULT), new Vector3(DROP_HALF_W * 2f + 1f, 0.06f, 0.06f),
            cyan, null, false).transform;
        var chute = new GameObject("Chute");
        chute.transform.SetParent(transform, false);
        _chute = chute.transform;
        var ghostMat = Unlit(SpaceTheme.Hex(0xFFD37A, 0.55f));
        var ghost = new GameObject("Ghost");
        ghost.transform.SetParent(_chute, false);
        ghost.AddComponent<MeshFilter>().sharedMesh = CoinPusherMeshes.Coin;
        var gr = ghost.AddComponent<MeshRenderer>();
        gr.sharedMaterials = new[] { ghostMat, ghostMat };
        gr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var beam = Solid("Beam", new Vector3(0f, -(DROP_Y - PUSHER_H) * 0.5f, 0f), new Vector3(0.05f, DROP_Y - PUSHER_H, 0.05f),
            Unlit(SpaceTheme.Hex(0x5CE1FF, 0.35f)), null, false);
        beam.transform.SetParent(_chute, false);
        var cap = Solid("Holder", new Vector3(0f, 0.5f, 0f), new Vector3(0.5f, 0.16f, 0.24f), violet, null, false);
        cap.transform.SetParent(_chute, false);
        PlaceChute();

        // ---- 背景（宇宙のグラデーションと星） ----
        BuildBackdrop();

        // ---- 光 ----
        var key = new GameObject("KeyLight");
        key.transform.SetParent(transform, false);
        key.transform.localRotation = Quaternion.Euler(52f, -28f, 0f);
        var kl = key.AddComponent<Light>();
        kl.type = LightType.Directional;
        kl.intensity = 1.2f;
        kl.color = new Color(1f, 0.97f, 0.92f);
        kl.shadows = LightShadows.Soft;
        var fill = new GameObject("FillLight");
        fill.transform.SetParent(transform, false);
        fill.transform.localRotation = Quaternion.Euler(28f, 155f, 0f);
        var fl = fill.AddComponent<Light>();
        fl.type = LightType.Directional;
        fl.intensity = 0.45f;
        fl.color = new Color(0.75f, 0.82f, 1f);
        fl.shadows = LightShadows.None;

        // ---- カメラ（RenderTexture に描く） ----
        _rt = new RenderTexture(width, height, 24) { antiAliasing = Msaa(width, height), name = "CoinPusherView" };
        var camGo = new GameObject("PusherCam");
        camGo.transform.SetParent(transform, false);
        _cam = camGo.AddComponent<Camera>();
        _cam.clearFlags = CameraClearFlags.SolidColor;
        _cam.backgroundColor = new Color(0.02f, 0.025f, 0.07f);
        _cam.nearClipPlane = 0.5f;
        _cam.farClipPlane = 60f;
        _cam.cullingMask = ~(1 << 5); // UI の層は描かない
        _cam.targetTexture = _rt;
        FrameCamera((float)width / height);
    }

    /// <summary>
    /// 映像の MSAA の数。4 か、使えなければ MSAA なし（2026-11-12）。
    /// 2 は使わない: Vulkan で必ず使えるのは 1 と 4 だけで、2 を頼んだ実機（SCV43）では描画先が 1 で作られ、
    /// URP が 2 として描こうとして毎フレーム失敗し（RenderPass: Attachment 0 was created with 1 samples but 2 samples were requested）、
    /// 映像が砂嵐になった。4 はさいころ座のダイス（Dice3DSimulator）で実機でも映ることを確かめてある
    /// </summary>
    private static int Msaa(int width, int height)
    {
        var desc = new RenderTextureDescriptor(width, height, RenderTextureFormat.Default, 24) { msaaSamples = 4 };
        return SystemInfo.GetRenderTextureSupportedMSAASampleCount(desc) >= 4 ? 4 : 1;
    }

    /// <summary>
    /// カメラの位置: 台の手前の斜め上から見下ろす。台の幅（横の壁まで）が横に収まり、受け皿から奥の看板までが
    /// 縦に収まる距離のうち、遠いほうに置く（縦長のスマホは幅で、横に広い iPad は縦で決まる）
    /// </summary>
    private void FrameCamera(float aspect)
    {
        const float VFOV = 46f, PITCH = 57f;
        _cam.fieldOfView = VFOV;
        float tanV = Mathf.Tan(VFOV * 0.5f * Mathf.Deg2Rad);
        float tanH = tanV * aspect;
        float dW = (HALF_W + 1.0f) / tanH;
        float dH = 9.0f / tanV;
        float d = Mathf.Max(dW, dH);
        // 16:9・20:9・iPad のどれでも、奥の看板が上の帯に隠れず、受け皿まで下の帯より上に見える位置
        // （2026-11-12 奥の壁を 5.7 に下げたときに投影図で決め直した。それまでは z 2.0・縦 7.4。投影図と実機の画面の位置は一致を確認）
        var target = new Vector3(0f, 0.2f, 2.9f);
        var rot = Quaternion.Euler(PITCH, 0f, 0f);
        _cam.transform.localPosition = target - rot * Vector3.forward * d;
        _cam.transform.localRotation = rot;
    }

    /// <summary>奥の壁の看板（3D の文字）</summary>
    private void Sign(Vector3 pos)
    {
        var go = new GameObject("Sign");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = pos;
        var tmp = go.AddComponent<TextMeshPro>();
        if (SpaceTheme.Font != null) tmp.font = SpaceTheme.Font;
        tmp.text = CoinPusher.T_SIGN;
        tmp.fontSize = 7f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = SpaceTheme.Hex(0x9EEBFF);
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.rectTransform.sizeDelta = new Vector2(HALF_W * 2f, 1.2f);
        tmp.enableAutoSizing = true;
        tmp.fontSizeMin = 3f;
        tmp.fontSizeMax = 7f;
    }

    /// <summary>台の後ろの宇宙（上が深い紺・下がすみれ色のグラデーションの板と、小さな星）</summary>
    private void BuildBackdrop()
    {
        var mat = Unlit(Color.white);
        var go = new GameObject("Backdrop");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, 3f, 16f);
        var mesh = new Mesh { name = "PusherBackdrop" };
        const float W = 46f, H = 34f;
        mesh.SetVertices(new List<Vector3>
        {
            new Vector3(-W * 0.5f, -H * 0.5f, 0f), new Vector3(W * 0.5f, -H * 0.5f, 0f),
            new Vector3(W * 0.5f, H * 0.5f, 0f), new Vector3(-W * 0.5f, H * 0.5f, 0f),
        });
        var top = new Color(0.03f, 0.04f, 0.13f);
        var bottom = new Color(0.16f, 0.07f, 0.28f);
        mesh.SetColors(new List<Color> { bottom, bottom, top, top });
        mesh.SetUVs(0, new List<Vector2> { Vector2.zero, Vector2.right, Vector2.one, Vector2.up });
        mesh.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
        mesh.RecalculateBounds();
        _owned.Add(mesh);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;

        var rnd = new System.Random(20261112);
        var starMat = Unlit(new Color(0.85f, 0.9f, 1f, 0.8f));
        for (int i = 0; i < 70; i++)
        {
            float s = 0.04f + (float)rnd.NextDouble() * 0.09f;
            var star = Solid("Star", new Vector3(((float)rnd.NextDouble() - 0.5f) * W, -1f + (float)rnd.NextDouble() * (H * 0.5f + 1f),
                -0.05f), new Vector3(s, s, 0.01f), starMat, null, false);
            star.transform.SetParent(go.transform, false);
        }
    }

    // ================================================================
    // 動き
    // ================================================================
    /// <summary>時計 time のときのプッシャーの前の面の奥行き（PUSH_NEAR〜PUSH_FAR をなめらかに往復する）</summary>
    private static float PusherFront(float time)
    {
        float k = 0.5f - 0.5f * Mathf.Cos(time / PUSH_PERIOD * Mathf.PI * 2f);
        return Mathf.Lerp(PUSH_NEAR, PUSH_FAR, k);
    }

    /// <summary>時計 time のときのプッシャーの中心（台の中の座標）</summary>
    private static Vector3 PusherLocal(float time) => new Vector3(0f, PUSHER_H * 0.5f, PusherFront(time) + PUSHER_D * 0.5f);

    /// <summary>時計 time のときのプッシャーの前後の速さ（奥へ向かうときが +。PusherFront を時間で微分したもの）</summary>
    private static float PusherSpeed(float time)
    {
        float w = Mathf.PI * 2f / PUSH_PERIOD;
        return (PUSH_FAR - PUSH_NEAR) * 0.5f * w * Mathf.Sin(time * w);
    }

    // プッシャーが動くと落とせる範囲も動くので、投入口も毎フレーム合わせる
    private void Update() => PlaceChute();

    private void FixedUpdate()
    {
        if (_pusher == null) return;
        _time += Time.fixedDeltaTime;
        _pusher.MovePosition(transform.TransformPoint(PusherLocal(_time)));
        ToppleStanding();

        // 落ちた物: 手前の受け皿なら獲得、横なら没収（台の外へ飛んだ物も没収）
        for (int i = _items.Count - 1; i >= 0; i--)
        {
            var it = _items[i];
            if (it.Rb == null)
            {
                _items.RemoveAt(i);
                continue;
            }
            var lp = transform.InverseTransformPoint(it.Rb.position);
            if (lp.y > GONE_Y && Mathf.Abs(lp.x) < 12f && lp.z < 14f && lp.z > -8f) continue;
            _items.RemoveAt(i);
            bool win = lp.y <= GONE_Y && lp.z < 0f && Mathf.Abs(lp.x) < HALF_W + 1.5f;
            var wp = it.Rb.position;
            Destroy(it.Rb.gameObject);
            if (win) Won?.Invoke(it.Kind, wp);
            else Lost?.Invoke(it.Kind);
        }
    }

    /// <summary>
    /// 縦に立って止まっているコインを倒す。当たり判定の形で立ちにくくしてあるが、ほかのコインや壁にもたれて立つことはあるので、
    /// STAND_WAIT 秒そのままのものを少し回して寝かせる。もたれている側へは倒れないので、まず反対側
    /// （いったん真上を越えて向こう）へ、まだ立っていたら次はもたれている側へ、と交互に試す（両側から挟まれていたら倒れないまま）
    /// </summary>
    private void ToppleStanding()
    {
        if (_time < _nextStandCheck) return;
        _nextStandCheck = _time + STAND_CHECK;
        var up = transform.up;
        foreach (var it in _items)
        {
            if (it.Kind != PusherItem.Coin || it.Rb == null) continue;
            var axis = it.Rb.rotation * Vector3.up; // コインの軸（上下の面の向き）
            float dot = Vector3.Dot(axis, up);
            bool standing = Mathf.Abs(dot) < STAND_DOT
                            && it.Rb.linearVelocity.sqrMagnitude < 1f && it.Rb.angularVelocity.sqrMagnitude < 4f;
            if (!standing)
            {
                it.StandSince = -1f;
                continue;
            }
            if (it.StandSince < 0f)
            {
                it.StandSince = _time;
                continue;
            }
            if (_time - it.StandSince < STAND_WAIT) continue;
            it.StandSince = -1f;
            // 回す軸 n: 水平で、コインのいちばん下の点のふちに沿う向き（|dot| < 0.5 なので toward と up は平行にならない）。
            // n の向きに回すと軸が近いほうの真上（真下）へ向かう＝もたれている側へ倒れる。ToppleBack でないときはその反対へ
            var toward = dot >= 0f ? axis : -axis;
            var n = Vector3.Cross(toward, up).normalized;
            bool away = !it.ToppleBack;
            if (away) n = -n;
            it.ToppleBack = !it.ToppleBack;
            float spin = away ? ToppleSpin(Mathf.Abs(dot)) : TOPPLE_MIN; // もたれている側へは下り坂なので弱く
            // ふちのいちばん下の点を支点に回る動き（回転と、それに見合う重心の速さ）をそのまま与える。
            // 重心のまわりに回すだけだと、ふちが床に当たったときに回転の8割が消えて倒れきらない
            var down = Vector3.ProjectOnPlane(-up, axis).normalized; // 重心からいちばん下のふちへの向き
            var omega = n * spin;
            it.Rb.WakeUp();
            it.Rb.angularVelocity = omega;
            it.Rb.linearVelocity = Vector3.Cross(omega, -down * CoinPusherMeshes.COIN_R);
        }
    }

    /// <summary>
    /// 傾いたコイン（軸と真上の内積の絶対値 c）が、ふちを支点に真上を越えて向こうへ倒れるのに要る回転の速さ×余裕。
    /// 重心の高さは、下の輪が床に触れているあいだ (r-chamfer)·sin + (厚み/2)·cos、まん中の輪なら r·sin。いちばん高いのは
    /// まっすぐ立ったとき（r）。持ち上げる高さ Δh に要る速さは √(2·重力·Δh / k²)、k² はふちを支点にした慣性（1.25r² + 厚み²/12）
    /// </summary>
    private static float ToppleSpin(float c)
    {
        const float R = CoinPusherMeshes.COIN_R, H = CoinPusherMeshes.COIN_H, CH = CoinPusherMeshes.COIN_CHAMFER;
        float s = Mathf.Sqrt(Mathf.Max(0f, 1f - c * c));
        float h = Mathf.Max((R - CH) * s + H * 0.5f * c, R * s);
        float dh = Mathf.Max(0f, R - h);
        float k2 = 1.25f * R * R + H * H / 12f;
        return Mathf.Clamp(TOPPLE_MARGIN * Mathf.Sqrt(2f * GRAVITY * dh / k2), TOPPLE_MIN, TOPPLE_MAX);
    }

    // ================================================================
    // 物を置く・落とす
    // ================================================================
    /// <summary>狙っている所（プッシャーの上をタップした所）にコインを1枚落とす</summary>
    public void DropCoin() => DropAt(PusherItem.Coin, _aimX, _aimZ, 0.04f);

    /// <summary>
    /// ボーナスの景品を、プッシャーの上のどこかに落とす。avoidX（コインタワーの左右の位置）があれば、そこから TOWER_CLEAR 以上
    /// 離れた所に（タワーの左右の空いている幅に合わせて、どちらかの側を選ぶ）
    /// </summary>
    public void DropBonus(PusherItem kind, float? avoidX = null)
    {
        DropRange(ITEM_HALF, out float zMin, out float zMax);
        float x = UnityEngine.Random.Range(-BONUS_HALF_W, BONUS_HALF_W);
        if (avoidX.HasValue)
        {
            float left = Mathf.Max(0f, avoidX.Value - TOWER_CLEAR + BONUS_HALF_W);  // 左はし 〜 タワーの左
            float right = Mathf.Max(0f, BONUS_HALF_W - avoidX.Value - TOWER_CLEAR); // タワーの右 〜 右はし
            if (left + right > 0f)
            {
                float r = UnityEngine.Random.Range(0f, left + right);
                x = r < left ? -BONUS_HALF_W + r : avoidX.Value + TOWER_CLEAR + (r - left);
            }
        }
        DropAt(kind, x, UnityEngine.Random.Range(zMin, zMax), 0f);
    }

    /// <summary>x・z の真上の投入口の高さから、まっすぐ下へ落とす（横の速さは付けない。狙った所からずれないように）</summary>
    private void DropAt(PusherItem kind, float x, float z, float jitter)
    {
        float half = kind == PusherItem.Coin ? CoinPusherMeshes.COIN_R : ITEM_HALF;
        float xMax = HALF_W - half - 0.08f;
        var lp = new Vector3(Mathf.Clamp(x + UnityEngine.Random.Range(-jitter, jitter), -xMax, xMax),
            DROP_Y + (kind == PusherItem.Coin ? 0f : 0.4f), DropZ(z + UnityEngine.Random.Range(-jitter, jitter), half));
        // 傾きと回転は控えめに（大きいと縦に近い向きで落ちて立ちやすい）。落ちる速さは物理の2倍速に合わせて 3
        var rot = Quaternion.Euler(UnityEngine.Random.Range(-8f, 8f), UnityEngine.Random.Range(0f, 360f), UnityEngine.Random.Range(-8f, 8f));
        var it = Spawn(kind, lp, rot);
        it.Rb.linearVelocity = transform.TransformDirection(new Vector3(0f, -3f, 0f));
        it.Rb.angularVelocity = UnityEngine.Random.insideUnitSphere * 1.5f;
    }

    /// <summary>
    /// コインタワー: プッシャーの上に、コインを count 枚まっすぐ積む。置く所はプッシャーの前の面から TOWER_FROM_FRONT 奥
    /// （プッシャーと一緒に動き、いちばん奥に下がっても壁に当たらない）。左右は、上に何も乗っていない所をなるべく選ぶ
    /// （ほかのコインの上に積むと傾いて崩れやすい）。プッシャーは動いているので、積んだコインにもプッシャーと同じ速さを与える
    /// （止まったまま置くと、動く床に引っぱられて崩れる）。崩すのは、上に落としたコインや、ほかの物に押されて。
    /// 置いた左右の位置を返す（一緒に落とすコマが、そこを避けるように）
    /// </summary>
    public float SpawnTower(int count)
    {
        if (_pusher == null || count <= 0) return 0f;
        float z = PusherFront(_time) + TOWER_FROM_FRONT;
        var xs = new List<float>();
        for (int i = 0; i < 7; i++) xs.Add(Mathf.Lerp(-TOWER_HALF_W, TOWER_HALF_W, i / 6f));
        for (int i = xs.Count - 1; i > 0; i--)
        {
            int j = UnityEngine.Random.Range(0, i + 1);
            (xs[i], xs[j]) = (xs[j], xs[i]);
        }
        float bestX = xs[0], bestTop = float.MaxValue;
        foreach (float x in xs)
        {
            // 上から下へ、コインより少し細い球を落として、そこにある物のいちばん上を調べる
            var from = transform.TransformPoint(new Vector3(x, PUSHER_H + 3f, z));
            if (!Physics.SphereCast(from, CoinPusherMeshes.COIN_R - 0.02f, -transform.up, out var hit, 4f, ~0,
                    QueryTriggerInteraction.Ignore)) continue;
            float top = transform.InverseTransformPoint(hit.point).y;
            if (hit.rigidbody == _pusher)
            {
                bestX = x; // プッシャーの上に何も乗っていない
                bestTop = top;
                break;
            }
            if (top < bestTop)
            {
                bestX = x;
                bestTop = top;
            }
        }
        if (bestTop == float.MaxValue) bestTop = PUSHER_H;

        const float STEP = CoinPusherMeshes.COIN_H + 0.004f;
        var velocity = transform.TransformDirection(new Vector3(0f, 0f, PusherSpeed(_time)));
        float y = bestTop + CoinPusherMeshes.COIN_H * 0.5f + 0.004f;
        for (int i = 0; i < count; i++)
        {
            var pos = new Vector3(bestX + UnityEngine.Random.Range(-0.008f, 0.008f), y + i * STEP, z + UnityEngine.Random.Range(-0.008f, 0.008f));
            var it = Spawn(PusherItem.Coin, pos, Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f));
            it.Rb.linearVelocity = velocity;
        }

        // 出たときの光の柱（プッシャーと一緒に動き、薄れて消える。当たり判定なし）
        float h = count * STEP;
        var flash = Solid("TowerFlash", new Vector3(bestX, bestTop + h * 0.5f + 0.2f, z), new Vector3(1.0f, h + 0.8f, 1.0f),
            Unlit(SpaceTheme.Hex(0xFFD37A, 0.55f)), null, false);
        flash.transform.SetParent(_pusher.transform, true);
        flash.AddComponent<CoinPusherFade>().Init(0.9f);
        return bestX;
    }

    /// <summary>
    /// いま落とすとプッシャーの上に落ちる奥行きの範囲（half は落とす物の半分の大きさ）。奥は壁の手前まで。
    /// 手前は、いまから落ちきるまでのプッシャーの前の面のいちばん奥より、さらに half と少し奥
    /// （落ちるあいだにプッシャーが下がっても手前の台に落ちず、上に乗るように。手前の台へ直接は落とせない）
    /// </summary>
    private void DropRange(float half, out float zMin, out float zMax)
    {
        zMax = WALL_Z - half - 0.08f;
        float front = PusherFront(_time);
        for (int i = 1; i <= 3; i++) front = Mathf.Max(front, PusherFront(_time + FALL_TIME * i / 3f));
        zMin = Mathf.Min(zMax, front + half + 0.08f);
    }

    private float DropZ(float z, float half)
    {
        DropRange(half, out float zMin, out float zMax);
        return Mathf.Clamp(z, zMin, zMax);
    }

    /// <summary>投入口（半透明のコイン・落ちる所までの線・つかみ）とレールを、いまコインを落とすと落ちる所の真上に置く</summary>
    private void PlaceChute()
    {
        if (_chute == null) return;
        float z = DropZ(_aimZ, CoinPusherMeshes.COIN_R);
        _chute.localPosition = new Vector3(_aimX, DROP_Y, z);
        if (_rail != null) _rail.localPosition = new Vector3(0f, DROP_Y + 0.5f, z);
    }

    private Item Spawn(PusherItem kind, Vector3 localPos, Quaternion localRot)
    {
        var go = new GameObject(kind.ToString());
        go.transform.SetParent(transform, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = localRot;
        var mf = go.AddComponent<MeshFilter>();
        var mr = go.AddComponent<MeshRenderer>();
        var col = go.AddComponent<MeshCollider>();
        col.convex = true;
        float mass;
        switch (kind)
        {
            case PusherItem.Coin:
                mf.sharedMesh = CoinPusherMeshes.Coin;
                mr.sharedMaterials = new[] { _coinFace, _coinRim };
                col.sharedMesh = CoinPusherMeshes.CoinCollider;
                mass = 1f;
                break;
            case PusherItem.StarFragment:
                mf.sharedMesh = CoinPusherMeshes.Star;
                mr.sharedMaterial = _star;
                col.sharedMesh = CoinPusherMeshes.Star;
                mass = 0.9f;
                break;
            case PusherItem.PlanetPotion:
                mf.sharedMesh = CoinPusherMeshes.Potion;
                mr.sharedMaterials = new[] { _potionGlass, _potionCork, _paint };
                col.sharedMesh = CoinPusherMeshes.PotionCollider;
                mass = 1.2f;
                break;
            case PusherItem.MeepleBlack:
                mf.sharedMesh = CoinPusherMeshes.MeepleBlack;
                mr.sharedMaterials = new[] { _meepleBlack, _paint };
                col.sharedMesh = CoinPusherMeshes.Meeple; // 当たり判定はほかのコマと同じ形
                mass = 1.6f;
                break;
            default:
                mf.sharedMesh = CoinPusherMeshes.Meeple;
                mr.sharedMaterial = kind == PusherItem.MeepleRed ? _meepleRed
                    : kind == PusherItem.MeepleBlue ? _meepleBlue : _meepleYellow;
                col.sharedMesh = CoinPusherMeshes.Meeple;
                mass = 1.6f;
                break;
        }
        col.sharedMaterial = _itemPm;
        var rb = go.AddComponent<Rigidbody>();
        rb.mass = mass;
        // 減衰・押し戻しの速さ・眠る基準は、物理を2倍速にした分に合わせて前（0.05・0.3・2.5・0.01）の 2倍・2倍・2倍・4倍
        rb.linearDamping = 0.1f;
        rb.angularDamping = 0.6f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        rb.solverIterations = 10;
        rb.solverVelocityIterations = 2;
        rb.maxDepenetrationVelocity = 5f;
        rb.sleepThreshold = 0.04f;
        rb.maxAngularVelocity = 14f; // 回転の上限も2倍速に合わせて Unity の初期値 7 の2倍（縦のコインを倒す回転もこの内）
        var it = new Item { Kind = kind, Rb = rb };
        _items.Add(it);
        return it;
    }

    /// <summary>
    /// 初めて遊ぶとき（保存した台がないとき）の台: 手前にコインを2段に敷きつめ、プッシャーの上にも少し。
    /// コマを2つ・星のカケラを3つ、コインの上に置く
    /// </summary>
    public void FillInitial()
    {
        float d = CoinPusherMeshes.COIN_R * 2f + 0.03f;
        float rowStep = d * 0.88f;
        for (int layer = 0; layer < 2; layer++)
        {
            float y = CoinPusherMeshes.COIN_H * (0.5f + layer) + 0.006f + layer * 0.006f;
            int row = 0;
            for (float z = 0.45f + layer * rowStep * 0.5f; z < PUSH_NEAR - CoinPusherMeshes.COIN_R - 0.06f; z += rowStep, row++)
            {
                float shift = ((row + layer) % 2) * d * 0.5f;
                for (float x = -HALF_W + CoinPusherMeshes.COIN_R + 0.06f + shift; x <= HALF_W - CoinPusherMeshes.COIN_R - 0.06f;
                     x += d)
                    Spawn(PusherItem.Coin, new Vector3(x, y, z), Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f)).Rb.Sleep();
            }
        }
        // プッシャーの上: 前のふちから2列（いちばん奥に下がっても壁に当たらない、残る上面のぶん）
        for (int row = 0; row < 2; row++)
        {
            float z = PUSH_NEAR + CoinPusherMeshes.COIN_R + 0.1f + row * rowStep;
            float shift = (row % 2) * d * 0.5f;
            for (float x = -HALF_W + CoinPusherMeshes.COIN_R + 0.08f + shift; x <= HALF_W - CoinPusherMeshes.COIN_R - 0.08f; x += d)
                Spawn(PusherItem.Coin, new Vector3(x, PUSHER_H + CoinPusherMeshes.COIN_H * 0.5f + 0.006f, z),
                    Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f)).Rb.Sleep();
        }

        float top = CoinPusherMeshes.COIN_H * 2f + 0.02f;
        var meeples = new[] { PusherItem.MeepleRed, PusherItem.MeepleBlue, PusherItem.MeepleYellow };
        Spawn(meeples[UnityEngine.Random.Range(0, 3)], new Vector3(-1.3f, top + CoinPusherMeshes.MEEPLE_T * 0.5f, 1.3f),
            Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f));
        Spawn(meeples[UnityEngine.Random.Range(0, 3)], new Vector3(1.2f, top + CoinPusherMeshes.MEEPLE_T * 0.5f, 1.6f),
            Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f));
        foreach (var pos in new[] { new Vector3(-0.2f, 0f, 0.9f), new Vector3(2.0f, 0f, 0.8f), new Vector3(-2.2f, 0f, 1.8f) })
            Spawn(PusherItem.StarFragment, new Vector3(pos.x, top + CoinPusherMeshes.STAR_T * 0.5f, pos.z),
                Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f));
    }

    // ================================================================
    // 保存（端末に。CoinPusherController が PlayerPrefs に書く）
    // ================================================================
    [Serializable]
    private sealed class SaveItem
    {
        public int t;
        public float x, y, z, qx, qy, qz, qw;
    }

    [Serializable]
    private sealed class SaveData
    {
        public int v = 1;
        public float time;
        public int bonus;
        public int tower; // コインタワーまでに数えた、ゲットしたコインの枚数（2026-11-12 追加。前の保存では 0）
        public List<SaveItem> items = new List<SaveItem>();
    }

    /// <summary>台の上の物の位置・向きと、プッシャーの時計・ボーナスとコインタワーまでの数を文字にする</summary>
    public string Save(int sinceBonus, int sinceTower)
    {
        var data = new SaveData { time = _time % PUSH_PERIOD, bonus = sinceBonus, tower = sinceTower };
        foreach (var it in _items)
        {
            if (it.Rb == null) continue;
            var lp = transform.InverseTransformPoint(it.Rb.position);
            var lr = Quaternion.Inverse(transform.rotation) * it.Rb.rotation;
            data.items.Add(new SaveItem { t = (int)it.Kind, x = lp.x, y = lp.y, z = lp.z, qx = lr.x, qy = lr.y, qz = lr.z, qw = lr.w });
        }
        return JsonUtility.ToJson(data);
    }

    /// <summary>保存した台を並べ直す。読めなければ false（呼んだ側で FillInitial）</summary>
    public bool Load(string json, out int sinceBonus, out int sinceTower)
    {
        sinceBonus = 0;
        sinceTower = 0;
        if (string.IsNullOrEmpty(json)) return false;
        SaveData data;
        try
        {
            data = JsonUtility.FromJson<SaveData>(json);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[CoinPusher] 保存した台を読めませんでした: {ex.Message}");
            return false;
        }
        if (data == null || data.items == null || data.items.Count == 0) return false;
        _time = data.time;
        sinceBonus = Mathf.Max(0, data.bonus);
        sinceTower = Mathf.Max(0, data.tower);
        // プッシャーを保存したときの位置へ先に置き直してから物を並べる（置き直さないと、いちばん手前の位置に作ったプッシャーに
        // 前の物がめり込み、最初の物理の1回で MovePosition が一気に動かして前の物をはじき飛ばす）
        if (_pusher != null)
        {
            _pusher.transform.localPosition = PusherLocal(_time);
            _pusher.position = _pusher.transform.position;
        }
        foreach (var s in data.items)
        {
            if (!Enum.IsDefined(typeof(PusherItem), s.t)) continue;
            var pos = new Vector3(s.x, s.y, s.z);
            if (pos.y < -0.5f || pos.y > 6f || Mathf.Abs(pos.x) > HALF_W + 0.5f) continue; // 落ちかけていた物は戻さない
            var rot = new Quaternion(s.qx, s.qy, s.qz, s.qw);
            if (rot.x * rot.x + rot.y * rot.y + rot.z * rot.z + rot.w * rot.w < 0.5f) rot = Quaternion.identity;
            var it = Spawn((PusherItem)s.t, pos, rot);
            if (pos.y < 1.2f) it.Rb.Sleep(); // 止まっていた物は眠らせて、並べ直したときに揺れないように
        }
        return _items.Count > 0;
    }

    /// <summary>
    /// 画面の位置（映像の 0〜1）を狙う所にする: プッシャーの上面の高さの面と交わる所の x・z。
    /// z はここでは台の範囲に収めるだけで、落とすとき（と投入口を置くとき）に、その時プッシャーの上に落ちる範囲へ収める
    /// （プッシャーより手前を押したら、プッシャーの前のふちの少し奥に落ちる）
    /// </summary>
    public bool AimAtViewport(Vector2 viewport)
    {
        if (_cam == null) return false;
        var ray = _cam.ViewportPointToRay(new Vector3(viewport.x, viewport.y, 0f));
        var plane = new Plane(transform.up, transform.TransformPoint(new Vector3(0f, PUSHER_H, 0f)));
        if (!plane.Raycast(ray, out float dist)) return false;
        var p = transform.InverseTransformPoint(ray.GetPoint(dist));
        _aimX = Mathf.Clamp(p.x, -DROP_HALF_W, DROP_HALF_W);
        _aimZ = Mathf.Clamp(p.z, PUSH_NEAR, WALL_Z);
        PlaceChute();
        return true;
    }

    /// <summary>ワールドの位置が映像のどこに見えるか（0〜1）</summary>
    public Vector2 WorldToViewport(Vector3 world)
    {
        if (_cam == null) return new Vector2(0.5f, 0.5f);
        var v = _cam.WorldToViewportPoint(world);
        return new Vector2(v.x, v.y);
    }

    // ================================================================
    // 部品
    // ================================================================
    /// <summary>直方体（1×1×1 の形を大きさに合わせて伸ばす）。collider なら当たり判定も</summary>
    private GameObject Solid(string name, Vector3 center, Vector3 size, Material mat, PhysicsMaterial pm, bool collider = true)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = center;
        go.transform.localScale = size;
        go.AddComponent<MeshFilter>().sharedMesh = _unitBox;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        if (!collider)
        {
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }
        var bc = go.AddComponent<BoxCollider>();
        if (pm != null) bc.sharedMaterial = pm;
        return go;
    }

    private PhysicsMaterial NewPhysicsMaterial(string name, float dynamicFriction, float staticFriction, float bounce)
    {
        var pm = new PhysicsMaterial(name)
        {
            dynamicFriction = dynamicFriction,
            staticFriction = staticFriction,
            bounciness = bounce,
            frictionCombine = PhysicsMaterialCombine.Average,
            bounceCombine = PhysicsMaterialCombine.Minimum,
        };
        _owned.Add(pm);
        return pm;
    }

    /// <summary>
    /// 光の当たる材質（URP の Lit）。Resources の DiceFX/DiceLit を複製する
    /// （実行時の Shader.Find だけだと、ビルドでシェーダーが外されて実機で表示されないため。Dice3DSimulator と同じ）
    /// </summary>
    private Material Lit(Color color, float smoothness, float metallic, Texture tex = null, Vector2? tiling = null)
    {
        var baseMat = Resources.Load<Material>("DiceFX/DiceLit");
        Material m;
        if (baseMat != null) m = new Material(baseMat);
        else
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            m = new Material(shader);
        }
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
        if (m.HasProperty("_Color")) m.SetColor("_Color", color);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
        if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smoothness);
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
        if (tex != null)
        {
            if (m.HasProperty("_BaseMap"))
            {
                m.SetTexture("_BaseMap", tex);
                if (tiling.HasValue) m.SetTextureScale("_BaseMap", tiling.Value);
            }
            if (m.HasProperty("_MainTex"))
            {
                m.SetTexture("_MainTex", tex);
                if (tiling.HasValue) m.SetTextureScale("_MainTex", tiling.Value);
            }
        }
        _owned.Add(m);
        return m;
    }

    /// <summary>光の当たらない材質（ネオンの線・半透明の目印）。Resources の DiceFX/DiceSprite（Sprites/Default）を複製する</summary>
    private Material Unlit(Color color)
    {
        var baseMat = Resources.Load<Material>("DiceFX/DiceSprite");
        var m = baseMat != null ? new Material(baseMat) : new Material(Shader.Find("Sprites/Default"));
        m.mainTexture = Texture2D.whiteTexture;
        m.color = color;
        _owned.Add(m);
        return m;
    }

    /// <summary>台の床の模様（細い格子。材質の色を掛けて使う）</summary>
    private static Texture2D GridTexture()
    {
        const int S = 64;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, true)
        {
            name = "PusherGrid", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4,
        };
        var px = new Color32[S * S];
        for (int y = 0; y < S; y++)
        for (int x = 0; x < S; x++)
        {
            bool line = x < 2 || y < 2;
            byte v = line ? (byte)255 : (byte)(196 + ((x * 7 + y * 13) % 9));
            px[y * S + x] = new Color32(v, v, v, 255);
        }
        tex.SetPixels32(px);
        tex.Apply(true, false);
        return tex;
    }
}

/// <summary>コインタワーが出たときの光の柱: だんだん薄く、少し太くなって消える（材質は CoinPusherWorld が片づける）</summary>
public class CoinPusherFade : MonoBehaviour
{
    private Material _mat;
    private Color _color;
    private Vector3 _scale;
    private float _life, _t;

    public void Init(float life)
    {
        _life = Mathf.Max(0.1f, life);
        _mat = GetComponent<MeshRenderer>().sharedMaterial;
        _color = _mat != null ? _mat.color : Color.white;
        _scale = transform.localScale;
    }

    private void Update()
    {
        _t += Time.deltaTime;
        float k = Mathf.Clamp01(_t / _life);
        if (_mat != null) _mat.color = new Color(_color.r, _color.g, _color.b, _color.a * (1f - k));
        transform.localScale = new Vector3(_scale.x * (1f + 0.35f * k), _scale.y, _scale.z * (1f + 0.35f * k));
        if (k >= 1f) Destroy(gameObject);
    }
}
