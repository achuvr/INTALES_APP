using System;
using System.Collections.Generic;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>惑星の定義（表示名・説明・色・描き方）</summary>
public sealed class SpacePlanetDef
{
    public string Id;
    public string Name;   // 表示名（カタカナ）
    public string En;     // 英字の名前（小見出し）
    public string Desc;   // ひとこと説明（1行に収まる長さ）
    public Color Accent;  // 惑星のまわりの光・ホームのオーブの光
    /// <summary>ルミナ: 最初からあるシアン〜すみれ色のオーブ（テクスチャを使わない）</summary>
    public bool IsOrb;
    /// <summary>以前のアプリでレベルを上げてきた人だけが持つ惑星（今は手に入らない）。持っていない人の一覧には出さない</summary>
    public bool Legacy;

    // ---- 球の描き方（SpaceSphere）----
    public float Tilt;            // 自転軸の画面内の傾き（度。反時計回りが正）
    public float Incline;         // 自転軸の手前への傾き（度）。環の楕円のつぶれ方もこれで決まる
    public float Ambient = 0.07f; // 夜側の明るさ（1 にすると昼夜のない自ら光る星）
    public Color Rim = Color.white; // 大気の縁の色
    public float RimAlpha = 0.85f;
    public bool RimEven;          // 縁を全周同じ明るさにする（自ら光る星・光をまとう星）
    public float GlowBase;        // 夜側で光る層の、昼側での光り具合（溶岩など常に光るもの）
    public float HaloScale = 1.8f;  // まわりの光の大きさ（球の直径に対する倍率）
    public float HaloAlpha = 0.38f;
    public bool HaloPulse;        // まわりの光がゆっくり脈打つ（太陽）
    public bool Flicker;          // 夜側で光る層が稲妻のように明滅する
    public float Scale = 1f;      // 枠に対する球の大きさ（環のある惑星は環が収まるよう小さめ）
    public float SpinPeriod = 100f; // 1回転の秒数
    public float CloudSpeed = 1.3f; // 雲は地表の何倍の速さで流れるか
    public float StartSpin = 0.18f; // 最初の向き（0〜1。一覧のサムネイルはこの向きで止める）

    // ---- 環 ----
    public bool Ring;
    public float RingOuter = 1.9f;  // 環の外径（球の直径に対する倍率）
    public float RingInner = 0.62f; // 環の内径（外径に対する割合）
}

/// <summary>
/// 新ホームの中央に置ける惑星の一覧と、選んだ惑星の保存（端末ローカル）、表面テクスチャの生成・キャッシュ。
///
/// テクスチャは SpacePlanetSurfaces（UnityEngine を使わない純粋な計算）でワーカースレッドに作らせ、
/// 出来上がった画素だけをメインスレッドで Texture2D にする。小（256。一覧のサムネイル・先出し用）と
/// 大（1024。ホーム・プレビュー用）の2種類。大は使っていないものから破棄して最大3惑星分だけ持つ。
/// </summary>
public static class SpacePlanets
{
    public const string DEFAULT_ID = "lumina";
    private const string PREF_KEY = "space_planet";
    private const int SMALL = 256;
    private const int LARGE = 1024;
    private const int RING_W = 512;
    private const int MAX_LARGE_SETS = 3;
    /// <summary>一覧のサムネイル画像の一辺（px）</summary>
    private const int THUMB_PX = 200;
    /// <summary>サムネイル画像の一辺に対する球の直径（Scale=1 のとき）。環や大気の縁がはみ出さない余白を取ってある</summary>
    public const float THUMB_SPHERE_FRAC = 0.713f;

    public static readonly SpacePlanetDef[] All =
    {
        new SpacePlanetDef
        {
            Id = "lumina", Name = "ルミナ", En = "LUMINA", Desc = "はじまりの光をまとう星",
            Accent = Hex(0x5CE1FF), IsOrb = true,
        },
        new SpacePlanetDef
        {
            Id = "aquaris", Name = "アクアリス", En = "AQUARIS", Desc = "青い海と白い雲の惑星",
            Accent = Hex(0x4FC3FF), Tilt = 12f, Incline = 10f, Ambient = 0.07f, Rim = Hex(0x7FD8FF), RimAlpha = 0.9f,
        },
        new SpacePlanetDef
        {
            Id = "verdia", Name = "ヴェルディア", En = "VERDIA", Desc = "夜になると森が光る惑星",
            Accent = Hex(0x5CFFB8), Tilt = -8f, Incline = 8f, Ambient = 0.06f, Rim = Hex(0x8CFFC8), RimAlpha = 0.85f,
        },
        new SpacePlanetDef
        {
            Id = "ignis", Name = "イグニス", En = "IGNIS", Desc = "溶岩が脈打つ灼熱の惑星",
            Accent = Hex(0xFF7A3C), Tilt = 15f, Incline = 6f, Ambient = 0.10f, Rim = Hex(0xFF9A5A), RimAlpha = 0.85f,
            GlowBase = 0.45f, SpinPeriod = 110f,
        },
        new SpacePlanetDef
        {
            Id = "glacia", Name = "グラシア", En = "GLACIA", Desc = "氷河に閉ざされた白銀の惑星",
            Accent = Hex(0xA8E6FF), Tilt = -14f, Incline = 12f, Ambient = 0.10f, Rim = Hex(0xCFF4FF), RimAlpha = 0.9f,
        },
        new SpacePlanetDef
        {
            Id = "tempesta", Name = "テンペスタ", En = "TEMPESTA", Desc = "青い大嵐が渦巻くガスの惑星",
            Accent = Hex(0xFFB86B), Tilt = 6f, Incline = 4f, Ambient = 0.06f, Rim = Hex(0xFFD9A8), RimAlpha = 0.75f,
            SpinPeriod = 70f, StartSpin = 0.80f,
        },
        new SpacePlanetDef
        {
            Id = "orbis", Name = "オルビス", En = "ORBIS", Desc = "金色の環をまとう惑星",
            Accent = Hex(0xFFD37A), Tilt = -20f, Incline = 16f, Ambient = 0.07f, Rim = Hex(0xFFE2B8), RimAlpha = 0.8f,
            Scale = 0.72f, Ring = true, SpinPeriod = 80f,
        },
        new SpacePlanetDef
        {
            Id = "mistia", Name = "ミスティア", En = "MISTIA", Desc = "すみれ色の霧がたゆたう惑星",
            Accent = Hex(0xB98BFF), Tilt = 10f, Incline = 10f, Ambient = 0.08f, Rim = Hex(0xE0A8FF), RimAlpha = 0.9f,
        },
        new SpacePlanetDef
        {
            Id = "rubra", Name = "ルブラ", En = "RUBRA", Desc = "赤い砂丘と峡谷の惑星",
            Accent = Hex(0xFF7A5C), Tilt = -10f, Incline = 10f, Ambient = 0.06f, Rim = Hex(0xFFB08A), RimAlpha = 0.75f,
        },
        new SpacePlanetDef
        {
            Id = "lunaris", Name = "ルナリス", En = "LUNARIS", Desc = "クレーターだらけの静かな月",
            Accent = Hex(0xD9DEF2), Tilt = 0f, Incline = 0f, Ambient = 0.05f, Rim = Hex(0xFFFFFF), RimAlpha = 0.25f,
            SpinPeriod = 160f,
        },
        new SpacePlanetDef
        {
            Id = "crysta", Name = "クリスタ", En = "CRYSTA", Desc = "宝石の結晶がきらめく惑星",
            Accent = Hex(0xFF8AD8), Tilt = 18f, Incline = 10f, Ambient = 0.12f, Rim = Hex(0xFFC2F0), RimAlpha = 0.9f,
            GlowBase = 0.15f,
        },
        new SpacePlanetDef
        {
            Id = "noctis", Name = "ノクティス", En = "NOCTIS", Desc = "夜側に街の灯りが広がる惑星",
            Accent = Hex(0x6F8CFF), Tilt = -12f, Incline = 10f, Ambient = 0.035f, Rim = Hex(0x7FA8FF), RimAlpha = 0.8f,
        },
        new SpacePlanetDef
        {
            Id = "soleil", Name = "ソレイユ", En = "SOLEIL", Desc = "燃えさかる小さな太陽",
            Accent = Hex(0xFFB347), Tilt = 0f, Incline = 6f, Ambient = 1f, Rim = Hex(0xFFE7A0), RimAlpha = 0.9f,
            RimEven = true, HaloScale = 2.6f, HaloAlpha = 0.75f, HaloPulse = true, SpinPeriod = 80f,
        },
        new SpacePlanetDef
        {
            Id = "coralia", Name = "コーラリア", En = "CORALIA", Desc = "サンゴ礁がきらめく南の海の惑星",
            Accent = Hex(0x4FE3D8), Tilt = 14f, Incline = 10f, Ambient = 0.07f, Rim = Hex(0x8FF2FF), RimAlpha = 0.9f,
        },
        new SpacePlanetDef
        {
            Id = "hexia", Name = "ヘクシア", En = "HEXIA", Desc = "六角形のマスでできたボードゲームの惑星",
            Accent = Hex(0x9BD46E), Tilt = 8f, Incline = 14f, Ambient = 0.10f, Rim = Hex(0xDDF7C8), RimAlpha = 0.7f,
            SpinPeriod = 120f,
        },
        new SpacePlanetDef
        {
            Id = "noir", Name = "ノワール", En = "NOIR", Desc = "漆黒に光の円盤をまとう星",
            Accent = Hex(0x9B7BFF), Tilt = -8f, Incline = 12f, Ambient = 0.04f, Rim = Hex(0xB892FF), RimAlpha = 1f,
            RimEven = true, HaloScale = 2.0f, HaloAlpha = 0.5f, Scale = 0.62f,
            Ring = true, RingOuter = 2.25f, RingInner = 0.50f, SpinPeriod = 90f,
        },
        new SpacePlanetDef
        {
            Id = "mocha", Name = "モカ", En = "MOCHA", Desc = "カフェラテのように渦巻く惑星",
            Accent = Hex(0xE0B080), Tilt = 10f, Incline = 10f, Ambient = 0.08f, Rim = Hex(0xFFE2C2), RimAlpha = 0.75f,
        },
        new SpacePlanetDef
        {
            Id = "celesta", Name = "セレスタ", En = "CELESTA", Desc = "大きく傾いた環をもつ氷の巨大惑星",
            Accent = Hex(0x7FE0FF), Tilt = 62f, Incline = 22f, Ambient = 0.08f, Rim = Hex(0xD9FCFF), RimAlpha = 0.85f,
            Scale = 0.70f, Ring = true, RingOuter = 1.85f, RingInner = 0.84f, SpinPeriod = 75f,
        },
        new SpacePlanetDef
        {
            Id = "opalia", Name = "オパーリア", En = "OPALIA", Desc = "虹色にゆらめくオパールの惑星",
            Accent = Hex(0xE6C8FF), Tilt = -6f, Incline = 8f, Ambient = 0.12f, Rim = Hex(0xFFFFFF), RimAlpha = 0.7f,
        },
        new SpacePlanetDef
        {
            Id = "voltia", Name = "ボルティア", En = "VOLTIA", Desc = "雷雲がとどろく嵐の惑星",
            Accent = Hex(0x7FCBFF), Tilt = -10f, Incline = 10f, Ambient = 0.06f, Rim = Hex(0x9FC4FF), RimAlpha = 0.8f,
            GlowBase = 0.25f, Flicker = true,
        },
        new SpacePlanetDef
        {
            Id = "pastelia", Name = "パステリア", En = "PASTELIA", Desc = "わたあめのような雲の惑星",
            Accent = Hex(0xFFB3DE), Tilt = -12f, Incline = 10f, Ambient = 0.14f, Rim = Hex(0xFFE0F2), RimAlpha = 0.85f,
        },
        new SpacePlanetDef
        {
            Id = "galaxia", Name = "ガラクシア", En = "GALAXIA", Desc = "星空を閉じこめた惑星",
            Accent = Hex(0x8E7BFF), Tilt = 12f, Incline = 12f, Ambient = 0.18f, Rim = Hex(0xB9A6FF), RimAlpha = 0.8f,
            GlowBase = 0.75f,
        },
        new SpacePlanetDef
        {
            Id = "limona", Name = "リモナ", En = "LIMONA", Desc = "レモン色の雲が流れる惑星",
            Accent = Hex(0xF5E86A), Tilt = -8f, Incline = 8f, Ambient = 0.08f, Rim = Hex(0xFFF6B8), RimAlpha = 0.8f,
        },
        new SpacePlanetDef
        {
            Id = "neonia", Name = "ネオニア", En = "NEONIA", Desc = "光の回路が走るネオンの惑星",
            Accent = Hex(0x3FF0FF), Tilt = 10f, Incline = 12f, Ambient = 0.10f, Rim = Hex(0x7F9BFF), RimAlpha = 0.8f,
            GlowBase = 0.55f,
        },
        // 古参の人への贈りもの（2026-11-07 ユーザー指定「以前から来ている人しかゲットできない惑星」）。
        // 配るのは tools/firestore/grant-legacy-planet.js。持っていない人の一覧・解放の数には出さない（Visible）
        new SpacePlanetDef
        {
            Id = LEGACY_ID, Name = "レガシア", En = "LEGACIA", Desc = "古き冒険者たちの記憶が眠る瑠璃と白銀の惑星",
            Accent = Hex(0x9CC8FF), Tilt = -14f, Incline = 12f, Ambient = 0.08f, Rim = Hex(0xEAF3FF), RimAlpha = 0.95f,
            GlowBase = 0.35f, HaloScale = 2.1f, HaloAlpha = 0.5f, SpinPeriod = 130f, Legacy = true,
        },
    };

    public const string LEGACY_ID = "legacia";

    /// <summary>一覧に出す惑星（古参の惑星は持っている人にだけ出す）</summary>
    public static SpacePlanetDef[] Visible => Array.FindAll(All, d => !d.Legacy || IsOwned(d));

    /// <summary>
    /// 古参の惑星の贈りもののお知らせを、この端末・このアカウントでまだ出していなければ true を返して「出した」にする。
    /// 店頭のQR（管理者のQR一覧の「惑星の解放」タブ）でも、スクリプトで配っても、お知らせは1回だけ
    /// </summary>
    public static bool TakeLegacyGift()
    {
        var user = Firebase.Auth.FirebaseAuth.DefaultInstance.CurrentUser;
        if (user == null) return false;
        string key = "space_legacy_gift_" + user.UserId;
        if (PlayerPrefs.GetInt(key, 0) == 1) return false;
        PlayerPrefs.SetInt(key, 1);
        PlayerPrefs.Save();
        return true;
    }

    // ---- 惑星の解放（アチーブメント報酬）で出す文言 ----
    public const string T_UNLOCKED_TITLE = "新しい惑星を解放しました！";
    public const string T_ALREADY_TITLE = "解放済みの惑星です";
    public const string T_HOW_TO_CHOOSE = "ホームの惑星ボタンからいつでも選べます";
    public const string T_INVALID_QR = "無効な惑星QRです";
    public const string T_UNLOCK_FAILED = "惑星の解放に失敗しました\n通信環境を確認して再度お試しください";
    public const string T_LEGACY_TITLE = "これまでの冒険に感謝をこめて";
    public const string T_LEGACY_NOTE = "以前からレベルを上げてきた方だけの、特別な惑星です";

    /// <summary>惑星の名前・説明・解放の文言に使う文字（NewHome の起動時にまとめてフォントへ登録する）</summary>
    public static string GlyphText
    {
        get
        {
            var sb = new StringBuilder();
            foreach (var d in All) sb.Append(d.Name).Append(d.En).Append(d.Desc);
            sb.Append(T_UNLOCKED_TITLE).Append(T_ALREADY_TITLE).Append(T_HOW_TO_CHOOSE).Append(T_INVALID_QR)
              .Append(T_UNLOCK_FAILED).Append(T_LEGACY_TITLE).Append(T_LEGACY_NOTE);
            return sb.ToString();
        }
    }

    public static SpacePlanetDef Find(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        foreach (var d in All)
            if (d.Id == id) return d;
        return null;
    }

    private static SpacePlanetDef _saved; // PlayerPrefs の値（毎フレーム読まないよう覚えておく）

    /// <summary>
    /// ホームの中央に置く惑星（この端末に保存。未設定ならルミナ）。
    /// 保存されていても、ログイン中のアカウントが持っていない惑星ならルミナを返す
    /// （同じ端末で別のアカウントに切り替えたときなど）。
    /// </summary>
    public static SpacePlanetDef Current
    {
        get
        {
            if (_saved == null) _saved = Find(PlayerPrefs.GetString(PREF_KEY, DEFAULT_ID)) ?? All[0];
            return IsOwned(_saved) ? _saved : All[0];
        }
        set
        {
            if (value == null) return;
            bool changed = _saved != value;
            _saved = value;
            PlayerPrefs.SetString(PREF_KEY, value.Id);
            PlayerPrefs.Save();
            // 入店中なら、ホームのいちばん外の円を回る自分の球の色も変える（入店中の記録がなければ何もしない）
            if (changed) StoreVisitors.SetPlanetAsync(value.Id).Forget();
        }
    }

    // ================================================================
    // 解放（アチーブメント報酬）
    // ================================================================
    /// <summary>
    /// 惑星を解放するQRの中身: "planet:{惑星ID}:{署名8桁}"。
    /// 署名は惑星IDから作る短いハッシュで、"planet:noir" のような手作りのQRでは解放できないようにする
    /// （クーポンQRと同じく、アプリを解析されれば作れてしまう程度の簡易な対策）。
    /// QRは管理者アカウントのQRコード一覧（SpaceAdminQrList の「惑星の解放」タブ）で表示する。
    /// </summary>
    public const string QR_PREFIX = "planet:";
    private const string QR_SALT = "intales/space-planet/2026";
    private const string PREF_NEWS = "space_planet_news"; // 惑星ボタンの「新着」の点（1=表示）

    /// <summary>ログイン中のアカウントがその惑星を持っているか（ルミナは最初から持っている）</summary>
    public static bool IsOwned(SpacePlanetDef def)
    {
        if (def == null) return false;
        if (def.IsOrb) return true;
        var data = UserDataManager.instance != null ? UserDataManager.instance.UserData : null;
        return data != null && data.Planets.Contains(def.Id);
    }

    /// <summary>解放済みの数（ルミナを含む）</summary>
    public static int OwnedCount
    {
        get
        {
            int n = 0;
            foreach (var d in Visible)
                if (IsOwned(d)) n++;
            return n;
        }
    }

    /// <summary>惑星を解放するQRの中身</summary>
    public static string QrPayload(SpacePlanetDef def) => QR_PREFIX + def.Id + ":" + Sign(def.Id);

    /// <summary>読み取ったQRが正しい惑星QRなら、その惑星を返す（署名違い・存在しない惑星・ルミナは null）</summary>
    public static SpacePlanetDef ParseQr(string text)
    {
        if (string.IsNullOrEmpty(text) || !text.StartsWith(QR_PREFIX)) return null;
        var parts = text.Trim().Substring(QR_PREFIX.Length).Split(':');
        if (parts.Length != 2) return null;
        var def = Find(parts[0]);
        if (def == null || def.IsOrb) return null;
        return string.Equals(parts[1], Sign(def.Id), StringComparison.OrdinalIgnoreCase) ? def : null;
    }

    /// <summary>惑星IDの署名（FNV-1a 64bit の上位32bitを16進8桁）</summary>
    private static string Sign(string id)
    {
        ulong h = 14695981039346656037UL;
        foreach (char c in QR_SALT + "|" + id)
        {
            h ^= c;
            h *= 1099511628211UL;
        }
        return ((uint)(h >> 32)).ToString("x8");
    }

    private static int _news = -1; // PlayerPrefs の値（毎フレーム読まないよう覚えておく）

    /// <summary>惑星ボタンに「新着」の点を出すか（惑星を選ぶ画面をまだ開いていない／新しく解放した惑星がある）</summary>
    public static bool HasNews
    {
        get
        {
            if (_news < 0) _news = PlayerPrefs.GetInt(PREF_NEWS, 1);
            return _news == 1;
        }
    }

    /// <summary>惑星を選ぶ画面を開いたら点を消す</summary>
    public static void ClearNews() => SetNews(0);

    /// <summary>惑星を解放したら点を出す</summary>
    public static void MarkNews() => SetNews(1);

    private static void SetNews(int v)
    {
        if (_news == v) return;
        _news = v;
        PlayerPrefs.SetInt(PREF_NEWS, v);
        PlayerPrefs.Save();
    }

    // ================================================================
    // テクスチャ
    // ================================================================
    /// <summary>1つの惑星の表面テクスチャ一式（Glow・Clouds は無い惑星もある。Thumb は小のときだけ）</summary>
    public sealed class TexSet
    {
        public Texture2D Surface, Glow, Clouds;
        /// <summary>一覧用の止まった1枚絵（環・大気の縁込み。まわりの光は含まない）</summary>
        public Texture2D Thumb;
        public bool Large;
        internal string Key;
        internal int Refs;
        internal float LastUsed;
        public bool Alive => Surface != null;
    }

    private sealed class Generated
    {
        public int W, H, CloudW, CloudH, RingH;
        public byte[][] Surface, Glow, Clouds, Ring;
        public byte[] Thumb;
    }

    private static readonly Dictionary<string, TexSet> Sets = new Dictionary<string, TexSet>();
    // 作成中の約束。同じ惑星を同時に複数の場所（起動時の先読み・ホーム・一覧）が待つので、
    // 何度でも await できる UniTaskCompletionSource を使う（UniTask.Preserve は同時に2か所から待つと例外になる）
    private static readonly Dictionary<string, UniTaskCompletionSource<TexSet>> Pending =
        new Dictionary<string, UniTaskCompletionSource<TexSet>>();
    private static readonly Dictionary<string, Sprite> Rings = new Dictionary<string, Sprite>();

    private static string Key(SpacePlanetDef def, bool large) => def.Id + (large ? "@L" : "@S");

    /// <summary>作成済みならすぐ返す（無ければ null）</summary>
    public static TexSet Peek(SpacePlanetDef def, bool large)
    {
        if (def == null || def.IsOrb) return null;
        return Sets.TryGetValue(Key(def, large), out var s) && s.Alive ? s : null;
    }

    /// <summary>一覧のサムネイル（小のテクスチャを作った後なら返る）</summary>
    public static Texture2D Thumb(SpacePlanetDef def) => Peek(def, false)?.Thumb;

    /// <summary>環の画像（環のある惑星で、テクスチャを1度でも作った後なら返る）</summary>
    public static Sprite RingSprite(SpacePlanetDef def) =>
        def != null && Rings.TryGetValue(def.Id, out var s) && s != null ? s : null;

    /// <summary>表面テクスチャを用意する（作成中なら同じ処理の完了を待つ）。失敗したら null</summary>
    public static UniTask<TexSet> Load(SpacePlanetDef def, bool large)
    {
        if (def == null || def.IsOrb) return UniTask.FromResult<TexSet>(null);
        var hit = Peek(def, large);
        if (hit != null) return UniTask.FromResult(hit);
        string key = Key(def, large);
        if (Pending.TryGetValue(key, out var pending)) return pending.Task;
        var promise = new UniTaskCompletionSource<TexSet>();
        Pending[key] = promise;
        Run(def, large, key, promise).Forget();
        return promise.Task;
    }

    private static async UniTaskVoid Run(SpacePlanetDef def, bool large, string key, UniTaskCompletionSource<TexSet> promise)
    {
        TexSet set = null;
        try
        {
            set = await LoadCore(def, large, key);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SpacePlanets] {def.Id} のテクスチャ作成に失敗: {e.Message}");
        }
        Pending.Remove(key);
        promise.TrySetResult(set);
    }

    /// <summary>一覧のサムネイル用（小）を裏で順に作っておく（一覧を開いたときにすぐ並ぶように）</summary>
    public static async UniTaskVoid PreloadThumbnails()
    {
        foreach (var def in All)
        {
            if (def.IsOrb || Peek(def, false) != null) continue;
            await Load(def, false);
        }
    }

    /// <summary>表示側が使い始める/使い終わる（使用中の大テクスチャは破棄しない）</summary>
    public static void Retain(TexSet set)
    {
        if (set == null) return;
        set.Refs++;
        set.LastUsed = Time.unscaledTime;
    }

    public static void Release(TexSet set)
    {
        if (set == null) return;
        set.Refs = Math.Max(0, set.Refs - 1);
        set.LastUsed = Time.unscaledTime;
    }

    /// <summary>生成（裏のスレッド）→ テクスチャ化（メインスレッド）。Load からだけ呼ぶ（同じ惑星は1回だけ走る）</summary>
    private static async UniTask<TexSet> LoadCore(SpacePlanetDef def, bool large, string key)
    {
        string id = def.Id;
        int width = large ? LARGE : SMALL;
        bool needRing = def.Ring && RingSprite(def) == null;
        int ringH = Mathf.Max(8, Mathf.RoundToInt(RING_W * Mathf.Sin(def.Incline * Mathf.Deg2Rad)));
        float ringInner = def.RingInner;
        var thumb = large ? null : ThumbSpecOf(def); // サムネイルは小と一緒に作る

        Generated g = null;
        try
        {
            g = await UniTask.RunOnThreadPool(() => Generate(id, width, large, needRing, ringH, ringInner, thumb));
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SpacePlanets] {id} のテクスチャ生成に失敗: {e.Message}");
        }
        if (g == null) return null;

        string suffix = large ? "_l" : "_s";
        var set = new TexSet
        {
            Key = key,
            Large = large,
            LastUsed = Time.unscaledTime,
            Surface = MakeTexture(g.Surface, g.W, g.H, "planet_" + id + suffix),
            Glow = g.Glow != null ? MakeTexture(g.Glow, g.W, g.H, "planet_" + id + "_glow" + suffix) : null,
            Clouds = g.Clouds != null ? MakeTexture(g.Clouds, g.CloudW, g.CloudH, "planet_" + id + "_clouds" + suffix) : null,
        };
        if (g.Thumb != null)
        {
            set.Thumb = MakeTexture(new[] { g.Thumb }, THUMB_PX, THUMB_PX, "planet_" + id + "_thumb");
            set.Thumb.wrapMode = TextureWrapMode.Clamp;
        }
        Sets[key] = set;

        if (g.Ring != null && RingSprite(def) == null)
        {
            var tex = MakeTexture(g.Ring, RING_W, g.RingH, "planet_" + id + "_ring");
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.anisoLevel = 4;
            var sp = Sprite.Create(tex, new Rect(0, 0, RING_W, g.RingH), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect);
            sp.name = tex.name;
            Rings[id] = sp;
        }

        if (large) EvictLarge();
        return set;
    }

    /// <summary>ワーカースレッドで実行する部分（UnityEngine の API は使わない）</summary>
    private static Generated Generate(string id, int width, bool mips, bool ring, int ringH, float ringInner,
        SpacePlanetSurfaces.ThumbSpec thumb)
    {
        var maps = SpacePlanetSurfaces.Generate(id, width);
        var g = new Generated
        {
            W = maps.Width, H = maps.Height, CloudW = maps.CloudWidth, CloudH = maps.CloudHeight,
            Surface = Levels(maps.Surface, maps.Width, maps.Height, mips),
            Glow = maps.Glow != null ? Levels(maps.Glow, maps.Width, maps.Height, mips) : null,
            Clouds = maps.Clouds != null ? Levels(maps.Clouds, maps.CloudWidth, maps.CloudHeight, mips) : null,
            Thumb = thumb != null ? SpacePlanetSurfaces.Thumbnail(maps, thumb, THUMB_PX) : null,
        };
        if (ring)
        {
            g.RingH = ringH;
            g.Ring = SpacePlanetSurfaces.BuildMips(SpacePlanetSurfaces.RingMap(id, RING_W, ringH, ringInner), RING_W, ringH);
        }
        return g;
    }

    /// <summary>サムネイルの描き方（SpacePlanetView の一覧表示と同じ見た目になる値）</summary>
    private static SpacePlanetSurfaces.ThumbSpec ThumbSpecOf(SpacePlanetDef d) => new SpacePlanetSurfaces.ThumbSpec
    {
        Kind = d.Id,
        Tilt = d.Tilt,
        Incline = d.Incline,
        Ambient = d.Ambient,
        GlowBase = d.GlowBase,
        Spin = d.StartSpin,
        RimR = d.Rim.r,
        RimG = d.Rim.g,
        RimB = d.Rim.b,
        RimAlpha = d.RimAlpha,
        RimEven = d.RimEven,
        SphereFrac = THUMB_SPHERE_FRAC * d.Scale,
        Ring = d.Ring,
        RingOuter = d.RingOuter,
        RingInner = d.RingInner,
    };

    private static byte[][] Levels(byte[] px, int w, int h, bool mips) =>
        mips ? SpacePlanetSurfaces.BuildMips(px, w, h) : new[] { px };

    private static Texture2D MakeTexture(byte[][] levels, int w, int h, string name)
    {
        var t = new Texture2D(w, h, TextureFormat.RGBA32, levels.Length, false)
        {
            name = name,
            wrapModeU = TextureWrapMode.Repeat, // 経度方向は1周つながる（自転は U をずらすだけ）
            wrapModeV = TextureWrapMode.Clamp,
            filterMode = levels.Length > 1 ? FilterMode.Trilinear : FilterMode.Bilinear,
        };
        for (int i = 0; i < levels.Length; i++) t.SetPixelData(levels[i], i);
        t.Apply(false, true); // CPU側のコピーは解放する
        return t;
    }

    /// <summary>大テクスチャは最大 MAX_LARGE_SETS 惑星分。使われていない古いものから破棄する</summary>
    private static void EvictLarge()
    {
        while (true)
        {
            int count = 0;
            TexSet oldest = null;
            foreach (var s in Sets.Values)
            {
                if (!s.Large || !s.Alive) continue;
                count++;
                if (s.Refs == 0 && (oldest == null || s.LastUsed < oldest.LastUsed)) oldest = s;
            }
            if (count <= MAX_LARGE_SETS || oldest == null) return;
            Sets.Remove(oldest.Key);
            UnityEngine.Object.Destroy(oldest.Surface);
            if (oldest.Glow != null) UnityEngine.Object.Destroy(oldest.Glow);
            if (oldest.Clouds != null) UnityEngine.Object.Destroy(oldest.Clouds);
        }
    }

    private static Color Hex(int rgb) =>
        new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
}
