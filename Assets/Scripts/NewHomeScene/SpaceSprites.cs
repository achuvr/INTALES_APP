using UnityEngine;

/// <summary>
/// 宇宙デザイン用のスプライト・テクスチャを実行時に生成する（画像アセット不要）。
/// どれも初回アクセス時に1回だけ作って static にキャッシュする（シーンをまたいでも使い回す）。
///
/// 生成物:
///   RoundedFill / RoundedStroke … 角丸の面・縁（9スライス）
///   Circle / Glow / Ring / RingSmall / RingBold / Sparkle / Streak / FadeLine … 光・軌道・星などの部品
///   Camera / Ticket … アイコン（白。Image.color で着色）
///   Planet / Moon … リング付き惑星・小さな衛星（背景用）
///   GoldMoon … 金色の小さな惑星（新ホームの履歴ボタン。背景の Moon と同じ描き方の色違い）
///   PlanetRim / PlanetIcon … 選べる惑星（SpacePlanetView）の大気の縁・惑星を選ぶボタンのアイコン
///   Lock / QrIcon … 未解放の惑星の錠前・管理者用の惑星QRボタンのアイコン
///   BellIcon … 鈴（管理者用の「お知らせ通知を送る」ボタンのアイコン）
///   BubbleTail … 吹き出しのしっぽ（白い三角形。お知らせの「New」）
///   FriendsIcon … フレンドのアイコン（白。2人の人の形）
///   Nebula(w,h) / Stars(w,h) … 背景の星雲と星（キャンバスの縦横比に合わせて生成）
/// </summary>
public static class SpaceSprites
{
    /// <summary>RoundedFill/RoundedStroke の角丸半径（スプライトのピクセル）。見た目の半径は pixelsPerUnitMultiplier で決まる</summary>
    public const float ROUNDED_RADIUS = 48f;
    private const int ROUNDED_SIZE = 128;
    private const float STROKE_PX = 3f;

    private static Sprite _roundedFill, _roundedStroke, _circle, _glow, _ring, _ringThin, _ringSmall, _sparkle, _streak,
        _fadeLine, _camera, _ticket, _gear, _inverseCorner, _planet, _moon, _goldMoon, _planetRim, _planetRimEven,
        _planetIcon, _lock, _qrIcon, _bubbleTail, _friendsIcon, _greenMoon, _cyanMoon, _ringBold, _bellIcon,
        _batteryEmpty;
    private static Texture2D _nebula, _stars;

    public static Sprite RoundedFill   => _roundedFill   != null ? _roundedFill   : (_roundedFill   = MakeRounded(false));
    public static Sprite RoundedStroke => _roundedStroke != null ? _roundedStroke : (_roundedStroke = MakeRounded(true));
    public static Sprite Circle        => _circle        != null ? _circle        : (_circle        = MakeCircle());
    public static Sprite Glow          => _glow          != null ? _glow          : (_glow          = MakeGlow());
    /// <summary>細いリング（大きく表示する軌道用。512px・線幅3px）</summary>
    public static Sprite Ring          => _ring          != null ? _ring          : (_ring          = MakeRing(512, 3f));
    /// <summary>もっと大きく表示する軌道用の細いリング（512px・線幅1.8px。直径940前後で表示して、
    /// 直径560で表示する Ring と線の太さがそろう）</summary>
    public static Sprite RingThin      => _ringThin      != null ? _ringThin      : (_ringThin      = MakeRing(512, 1.8f, "space_ring_thin"));
    /// <summary>小さく表示する縁取り用リング（256px・線幅4px）</summary>
    public static Sprite RingSmall     => _ringSmall     != null ? _ringSmall     : (_ringSmall     = MakeRing(256, 4f));
    /// <summary>太めのリング（128px・線幅7px。遊んだ記録のカレンダーの〇。直径90前後で表示して線の太さ5ほど）</summary>
    public static Sprite RingBold      => _ringBold      != null ? _ringBold      : (_ringBold      = MakeRing(128, 7f, "space_ring_bold"));
    public static Sprite Sparkle       => _sparkle       != null ? _sparkle       : (_sparkle       = MakeSparkle());
    public static Sprite Streak        => _streak        != null ? _streak        : (_streak        = MakeStreak());
    public static Sprite FadeLine      => _fadeLine      != null ? _fadeLine      : (_fadeLine      = MakeFadeLine());
    public static Sprite Camera        => _camera        != null ? _camera        : (_camera        = MakeCamera());
    public static Sprite Ticket        => _ticket        != null ? _ticket        : (_ticket        = MakeTicket());
    public static Sprite Gear          => _gear          != null ? _gear          : (_gear          = MakeGear());
    /// <summary>角丸の「窓」の外側の角（左上向き。回転して4隅に使う）。QRの読み取り枠の暗幕用</summary>
    public static Sprite InverseCorner => _inverseCorner != null ? _inverseCorner : (_inverseCorner = MakeInverseCorner());
    public static Sprite Planet        => _planet        != null ? _planet        : (_planet        = MakePlanet(512));
    public static Sprite Moon          => _moon          != null ? _moon          :
        (_moon = MakeMoon(160, 9, Hex(0xF29AD8), Hex(0xFFC6F0), Hex(0xFF8AD8), "space_moon"));
    /// <summary>金色の小さな惑星（新ホームの履歴ボタン）。背景の Moon と同じ描き方で、色と模様だけ変える</summary>
    public static Sprite GoldMoon      => _goldMoon      != null ? _goldMoon      :
        (_goldMoon = MakeMoon(192, 23, Hex(0xFFC978), Hex(0xFFEBC4), Hex(0xFFC46E), "space_moon_gold"));
    /// <summary>緑の小さな惑星（新ホームのボードゲーム図鑑ボタン。初めからだれにでも出る）</summary>
    public static Sprite GreenMoon     => _greenMoon     != null ? _greenMoon     :
        (_greenMoon = MakeMoon(192, 41, Hex(0x58D69A), Hex(0xCFFBE2), Hex(0x63F0A8), "space_moon_green"));
    /// <summary>水色の小さな惑星（新ホームの料金表ボタン）</summary>
    public static Sprite CyanMoon      => _cyanMoon      != null ? _cyanMoon      :
        (_cyanMoon = MakeMoon(192, 57, Hex(0x5CC8F2), Hex(0xD6F6FF), Hex(0x5CE1FF), "space_moon_cyan"));
    /// <summary>惑星の大気の縁（白。球の半径は画像の半分の 0.8 倍。光の当たる左上ほど明るい）</summary>
    public static Sprite PlanetRim     => _planetRim     != null ? _planetRim     : (_planetRim     = MakePlanetRim(256, false));
    /// <summary>全周が同じ明るさの縁（自ら光る星・光をまとう星）</summary>
    public static Sprite PlanetRimEven => _planetRimEven != null ? _planetRimEven : (_planetRimEven = MakePlanetRim(256, true));
    /// <summary>環のある惑星のアイコン（白。惑星を選ぶボタン用）</summary>
    public static Sprite PlanetIcon    => _planetIcon    != null ? _planetIcon    : (_planetIcon    = MakePlanetIcon());
    /// <summary>錠前のアイコン（白。未解放の惑星の印）</summary>
    public static Sprite Lock          => _lock          != null ? _lock          : (_lock          = MakeLock());
    /// <summary>QRコード風のアイコン（白。管理者用の「惑星の解放QRコード」ボタン）</summary>
    public static Sprite QrIcon        => _qrIcon        != null ? _qrIcon        : (_qrIcon        = MakeQrIcon());
    /// <summary>鈴のアイコン（白。管理者用の「お知らせ通知を送る」ボタン）</summary>
    public static Sprite BellIcon      => _bellIcon      != null ? _bellIcon      : (_bellIcon      = MakeBellIcon());
    /// <summary>空っぽの電池のアイコン（白。力を失った＝使用済みで非アクティブな惑星の印。2026-11-10）</summary>
    public static Sprite BatteryEmptyIcon => _batteryEmpty != null ? _batteryEmpty : (_batteryEmpty = MakeBatteryEmptyIcon());
    /// <summary>
    /// 吹き出しのしっぽ（白い三角形・縦横比 40:25）。左下の角が先端、上辺が吹き出しの中に入る（新ホームの「New」）。
    /// 表示（40x25）に近い大きさで作る（大きく作って縮めると、ミップマップがないのでふちがギザギザになる）
    /// </summary>
    public static Sprite BubbleTail    => _bubbleTail    != null ? _bubbleTail    :
        (_bubbleTail = MakeTriangle(64, 40, new Vector2(0f, 0f), new Vector2(0.45f, 1f), new Vector2(1f, 1f),
            "space_bubble_tail"));
    /// <summary>フレンドのアイコン（白。手前と奥の2人の人の形。新ホームの右上のフレンドボタン）</summary>
    public static Sprite FriendsIcon   => _friendsIcon   != null ? _friendsIcon   : (_friendsIcon   = MakeFriendsIcon(128));

    /// <summary>背景の星雲（低解像度で作って拡大表示する。ぼけるほど雲らしい）</summary>
    public static Texture2D Nebula(int w, int h)
    {
        if (_nebula == null || _nebula.width != w || _nebula.height != h) _nebula = MakeNebula(w, h);
        return _nebula;
    }

    /// <summary>背景の星（キャンバスの半分程度の解像度で作る）</summary>
    public static Texture2D Stars(int w, int h)
    {
        if (_stars == null || _stars.width != w || _stars.height != h) _stars = MakeStars(w, h, 7);
        return _stars;
    }

    // ================================================================
    // 部品
    // ================================================================
    private static Sprite MakeRounded(bool stroke)
    {
        int s = ROUNDED_SIZE;
        float half = s / 2f;
        var px = new Color32[s * s];
        for (int y = 0; y < s; y++)
        for (int x = 0; x < s; x++)
        {
            // 外周1pxはアンチエイリアス用に空ける
            float d = SdRoundBox(x + 0.5f - half, y + 0.5f - half, half - 1f, half - 1f, ROUNDED_RADIUS);
            float a = Mathf.Clamp01(0.5f - d);
            if (stroke) a -= Mathf.Clamp01(0.5f - (d + STROKE_PX));
            px[y * s + x] = White(a);
        }
        float b = ROUNDED_RADIUS + 2f;
        return ToSprite(Tex(s, s, px, stroke ? "space_rounded_stroke" : "space_rounded"), new Vector4(b, b, b, b));
    }

    private static Sprite MakeCircle()
    {
        const int s = 128;
        float half = s / 2f;
        var px = new Color32[s * s];
        for (int y = 0; y < s; y++)
        for (int x = 0; x < s; x++)
        {
            float r = Dist(x + 0.5f - half, y + 0.5f - half);
            px[y * s + x] = White(Mathf.Clamp01(half - 1f - r + 0.5f));
        }
        return ToSprite(Tex(s, s, px, "space_circle"));
    }

    private static Sprite MakeGlow()
    {
        const int s = 128;
        float half = s / 2f;
        var px = new Color32[s * s];
        for (int y = 0; y < s; y++)
        for (int x = 0; x < s; x++)
        {
            float r = Dist(x + 0.5f - half, y + 0.5f - half) / half;
            px[y * s + x] = White(Mathf.Pow(Mathf.Clamp01(1f - r), 2.2f));
        }
        return ToSprite(Tex(s, s, px, "space_glow"));
    }

    private static Sprite MakeRing(int s, float stroke, string name = null)
    {
        float half = s / 2f;
        float rr = half - stroke - 1f;
        var px = new Color32[s * s];
        for (int y = 0; y < s; y++)
        for (int x = 0; x < s; x++)
        {
            float r = Dist(x + 0.5f - half, y + 0.5f - half);
            px[y * s + x] = White(Mathf.Clamp01(stroke * 0.5f + 0.5f - Mathf.Abs(r - rr)));
        }
        return ToSprite(Tex(s, s, px, name ?? "space_ring_" + s));
    }

    /// <summary>4方向に光芒が伸びる星</summary>
    private static Sprite MakeSparkle()
    {
        const int s = 128;
        float half = s / 2f;
        var px = new Color32[s * s];
        for (int y = 0; y < s; y++)
        for (int x = 0; x < s; x++)
        {
            float dx = x + 0.5f - half, dy = y + 0.5f - half;
            float r = Dist(dx, dy);
            float core = Mathf.Exp(-(r / 5f) * (r / 5f));
            float halo = Mathf.Exp(-(r / 16f) * (r / 16f)) * 0.35f;
            float rayH = Mathf.Exp(-(dy / 1.5f) * (dy / 1.5f)) * Mathf.Pow(Mathf.Clamp01(1f - Mathf.Abs(dx) / (half - 2f)), 2.6f);
            float rayV = Mathf.Exp(-(dx / 1.5f) * (dx / 1.5f)) * Mathf.Pow(Mathf.Clamp01(1f - Mathf.Abs(dy) / (half - 2f)), 2.6f);
            px[y * s + x] = White(Mathf.Clamp01(core + halo + 0.85f * (rayH + rayV)));
        }
        return ToSprite(Tex(s, s, px, "space_sparkle"));
    }

    /// <summary>流れ星の尾（右端が頭）</summary>
    private static Sprite MakeStreak()
    {
        const int w = 256, h = 32;
        var px = new Color32[w * h];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            float t = (x + 0.5f) / w;
            float dy = y + 0.5f - h / 2f;
            float tail = Mathf.Pow(t, 2.4f) * Mathf.Exp(-(dy / 3f) * (dy / 3f));
            float hx = x + 0.5f - (w - 12f);
            float head = Mathf.Exp(-(hx * hx + dy * dy) / (2f * 6f * 6f));
            px[y * w + x] = White(Mathf.Clamp01(tail + head * 0.9f));
        }
        return ToSprite(Tex(w, h, px, "space_streak"));
    }

    /// <summary>両端が透明に消える区切り線（横向き。縦に使うときは90°回転）</summary>
    private static Sprite MakeFadeLine()
    {
        const int w = 256, h = 4;
        var px = new Color32[w * h];
        for (int x = 0; x < w; x++)
        {
            float t = (x + 0.5f) / w;
            var c = White(Mathf.Pow(Mathf.Sin(Mathf.PI * t), 1.5f));
            for (int y = 0; y < h; y++) px[y * w + x] = c;
        }
        return ToSprite(Tex(w, h, px, "space_fadeline"));
    }

    /// <summary>カメラのアイコン（本体＋ファインダー、レンズと小窓を抜く）</summary>
    private static Sprite MakeCamera()
    {
        const int s = 256;
        var px = new Color32[s * s];
        for (int y = 0; y < s; y++)
        for (int x = 0; x < s; x++)
        {
            float u = (x + 0.5f) / s - 0.5f, v = (y + 0.5f) / s - 0.5f;
            float body = SdRoundBox(u, v + 0.05f, 0.42f, 0.29f, 0.085f);
            float bump = SdRoundBox(u + 0.13f, v - 0.255f, 0.135f, 0.07f, 0.035f);
            float shape = Mathf.Min(body, bump);
            float ringCut = Mathf.Abs(Dist(u, v + 0.05f) - 0.17f) - 0.045f; // 外0.215〜内0.125の環を抜く
            float flash = Dist(u - 0.29f, v - 0.13f) - 0.032f;
            float d = Mathf.Max(shape, -Mathf.Min(ringCut, flash));
            px[y * s + x] = White(Mathf.Clamp01(0.5f - d * s));
        }
        return ToSprite(Tex(s, s, px, "space_camera"));
    }

    /// <summary>チケットのアイコン（左右に切り欠き、左寄りにミシン目）</summary>
    private static Sprite MakeTicket()
    {
        const int w = 160, h = 104;
        float hx = w / 2f - 4f, hy = h / 2f - 4f;
        var px = new Color32[w * h];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            float px0 = x + 0.5f - w / 2f, py0 = y + 0.5f - h / 2f;
            float box = SdRoundBox(px0, py0, hx, hy, 14f);
            float notch = Dist(Mathf.Abs(px0) - hx, py0) - 15f;
            float d = Mathf.Max(box, -notch);
            float holes = float.MaxValue;
            float mx = px0 + w * 0.16f;
            for (int k = -3; k <= 3; k++) holes = Mathf.Min(holes, Dist(mx, py0 - k * 13f) - 3.2f);
            d = Mathf.Max(d, -holes);
            px[y * w + x] = White(Mathf.Clamp01(0.5f - d));
        }
        return ToSprite(Tex(w, h, px, "space_ticket"));
    }

    /// <summary>歯車のアイコン（歯8枚・中央に穴）。設定メニューのボタン用</summary>
    private static Sprite MakeGear()
    {
        const int s = 256;
        var px = new Color32[s * s];
        for (int y = 0; y < s; y++)
        for (int x = 0; x < s; x++)
        {
            float u = (x + 0.5f) / s - 0.5f, v = (y + 0.5f) / s - 0.5f;
            float r = Dist(u, v);
            float tooth = Smooth((Mathf.Cos(8f * Mathf.Atan2(v, u)) - 0.05f) / 0.30f); // 台形の歯
            float d = Mathf.Max(r - (0.30f + 0.085f * tooth), 0.12f - r);
            px[y * s + x] = White(Mathf.Clamp01(0.5f - d * s));
        }
        return ToSprite(Tex(s, s, px, "space_gear"));
    }

    /// <summary>右下を中心とする四分円の外側だけ不透明（左上の角を丸く抜いた暗幕の角になる）</summary>
    private static Sprite MakeInverseCorner()
    {
        const int s = 64;
        var px = new Color32[s * s];
        for (int y = 0; y < s; y++)
        for (int x = 0; x < s; x++)
            px[y * s + x] = White(Mathf.Clamp01(Dist(x + 0.5f - s, y + 0.5f) - s + 0.5f));
        return ToSprite(Tex(s, s, px, "space_inverse_corner"));
    }

    // ================================================================
    // 惑星・衛星
    // ================================================================
    /// <summary>
    /// リング付きの惑星。左上から光が当たる球の陰影＋リング面に平行な縞＋大気のリムライト＋外側のハロー。
    /// リングは奥側（惑星に隠れる）と手前側（惑星の前を横切る）を分けて合成する。
    /// </summary>
    private static Sprite MakePlanet(int s)
    {
        float rp = s * 0.215f;
        const float tilt = -20f * Mathf.Deg2Rad;
        float ct = Mathf.Cos(tilt), st = Mathf.Sin(tilt);
        const float k = 0.27f; // リングの扁平率
        var light = new Vector3(-0.62f, 0.48f, 0.62f).normalized;

        var deep = Hex(0x2A1F74);
        var lav = Hex(0x8C74F2);
        var teal = Hex(0x3FB7D9);
        var pink = Hex(0xD58AE6);
        var rimC = Hex(0x7FE9FF);
        var haloC = Hex(0x6FD9FF) * 0.9f;
        var ringBase = new Color(0.86f, 0.84f, 1.0f);

        var px = new Color32[s * s];
        for (int y = 0; y < s; y++)
        for (int x = 0; x < s; x++)
        {
            float nx = (x + 0.5f - s / 2f) / rp;
            float ny = (y + 0.5f - s / 2f) / rp;
            float d = Mathf.Sqrt(nx * nx + ny * ny);
            float rx = nx * ct + ny * st;
            float ry = -nx * st + ny * ct;
            float re = Mathf.Sqrt(rx * rx + (ry / k) * (ry / k));

            // ハロー（大気の外側の光。光の当たる側ほど明るい）
            float haloA = 0f;
            if (d > 0.98f && d < 1.22f)
            {
                float hd = Mathf.Clamp01((d - 1f) / 0.22f);
                float facing = Mathf.Clamp((nx / d) * light.x + (ny / d) * light.y, -1f, 1f) * 0.5f + 0.5f;
                haloA = Mathf.Pow(1f - hd, 2.2f) * (0.20f + 0.55f * facing);
            }

            // リング
            float ringA = 0f;
            if (re > 1.30f && re < 2.10f)
            {
                float b = Smooth((re - 1.34f) / 0.05f) * (1f - Smooth((re - 2.02f) / 0.06f));
                float gap = Mathf.Exp(-((re - 1.66f) / 0.022f) * ((re - 1.66f) / 0.022f));
                float fine = 0.72f + 0.28f * Mathf.Sin(re * 70f) * Mathf.Sin(re * 23f + 1f);
                float innerFade = 0.55f + 0.45f * Smooth((re - 1.34f) / 0.35f);
                ringA = Mathf.Clamp01(b * (1f - 0.85f * gap) * fine * innerFade) * 0.80f;
            }
            Color ringC = ringBase * (0.55f + 0.45f * Mathf.Clamp01(-rx * 0.6f + 0.5f));

            // 惑星本体
            float planetA = Mathf.Clamp01((1f - d) * rp + 0.5f);
            Color lit = Color.black;
            if (planetA > 0f)
            {
                float nz = Mathf.Sqrt(Mathf.Clamp01(1f - d * d));
                float diff = Mathf.Clamp01(nx * light.x + ny * light.y + nz * light.z);
                float bandc = ry + 0.10f * (Fbm(rx * 2.2f + 3f, ry * 7.5f, 5, 4) - 0.5f);
                float bands = 0.5f + 0.5f * Mathf.Sin(bandc * 9f + 1.3f);
                float bands2 = 0.5f + 0.5f * Mathf.Sin(bandc * 23f + 0.4f);
                Color surf = Color.LerpUnclamped(deep, lav, bands);
                surf = Color.LerpUnclamped(surf, teal, Mathf.Clamp01(bands2 - 0.55f) * 1.1f);
                surf = Color.LerpUnclamped(surf, pink, Mathf.Clamp01(0.35f - Mathf.Abs(ry + 0.25f)) * 0.9f);
                lit = surf * (0.05f + 1.10f * Mathf.Pow(diff, 0.85f));
                float rim = Mathf.Pow(1f - nz, 2.6f) * Mathf.Clamp01(diff * 1.6f + 0.08f);
                lit += rimC * (rim * 0.85f);
            }

            // 合成（乗算済みアルファで 奥リング→惑星→手前リング の順に重ねる）
            float a = haloA;
            Color c = haloC * haloA;
            bool back = ry > 0f;
            float backA = back ? ringA * (1f - planetA) : 0f;
            Over(ref c, ref a, ringC, backA);
            Over(ref c, ref a, lit, planetA);
            Over(ref c, ref a, ringC, back ? 0f : ringA);

            px[y * s + x] = Straight(c, a);
        }
        return ToSprite(Tex(s, s, px, "space_planet"));
    }

    /// <summary>
    /// 小さな惑星: 左上から光が当たる球（うっすらした模様＋縁の光）と、外側の淡いハロー。
    /// 球の半径は画像の 0.4 倍。seed で模様を変える
    /// </summary>
    private static Sprite MakeMoon(int s, int seed, Color baseC, Color rimC, Color haloC, string name)
    {
        float r = s * 0.40f;
        var light = new Vector3(-0.55f, 0.55f, 0.63f).normalized;
        var px = new Color32[s * s];
        for (int y = 0; y < s; y++)
        for (int x = 0; x < s; x++)
        {
            float nx = (x + 0.5f - s / 2f) / r;
            float ny = (y + 0.5f - s / 2f) / r;
            float d = Mathf.Sqrt(nx * nx + ny * ny);
            float a = Mathf.Clamp01((1f - d) * r + 0.5f);
            Color lit = Color.black;
            if (a > 0f)
            {
                float nz = Mathf.Sqrt(Mathf.Clamp01(1f - d * d));
                float diff = Mathf.Clamp01(nx * light.x + ny * light.y + nz * light.z);
                float n = Fbm(nx * 3f + 5f, ny * 3f + 2f, seed, 4);
                lit = baseC * (0.75f + 0.25f * n) * (0.04f + Mathf.Pow(diff, 0.9f));
                float rim = Mathf.Pow(1f - nz, 2.5f) * Mathf.Clamp01(diff * 1.5f + 0.05f);
                lit += rimC * (rim * 0.6f);
            }
            float halo = d >= 1f ? Mathf.Pow(1f - Mathf.Clamp01((d - 1f) / 0.30f), 2.5f) * 0.35f : 0f;
            float outA = halo;
            Color c = haloC * halo;
            Over(ref c, ref outA, lit, a);
            px[y * s + x] = Straight(c, outA);
        }
        return ToSprite(Tex(s, s, px, name));
    }

    /// <summary>
    /// 大気の縁: 球の内側は縁に近いほど濃く（7乗）、外側は薄い光が少しにじむ。光の当たる左上ほど明るい。
    /// SpacePlanetView が球の直径 / 0.8 の大きさで球の上に重ね、惑星ごとの色で着色する。
    /// </summary>
    private static Sprite MakePlanetRim(int s, bool even)
    {
        float half = s / 2f, r0 = half * 0.8f;
        var light = new Vector2(-0.55f, 0.50f).normalized;
        var px = new Color32[s * s];
        for (int y = 0; y < s; y++)
        for (int x = 0; x < s; x++)
        {
            float dx = (x + 0.5f - half) / r0, dy = (y + 0.5f - half) / r0;
            float rho = Dist(dx, dy);
            if (rho > 1.2f) continue;
            float facing = 1f;
            if (!even)
            {
                facing = rho > 0.0001f ? Mathf.Clamp01((dx * light.x + dy * light.y) / rho * 0.5f + 0.5f) : 0.5f;
                facing = 0.18f + 0.82f * Mathf.Pow(facing, 1.4f);
            }
            float inner = rho <= 1f ? Mathf.Pow(rho, 7f) * 0.85f : 0f;
            float outer = rho > 1f ? Mathf.Pow(Mathf.Clamp01(1f - (rho - 1f) / 0.16f), 2.4f) * 0.55f : 0f;
            px[y * s + x] = White((inner + outer) * facing);
        }
        return ToSprite(Tex(s, s, px, even ? "space_planet_rim_even" : "space_planet_rim"));
    }

    /// <summary>
    /// 環のある惑星のアイコン: 円＋傾いた楕円の環。環の奥側は惑星に隠れ、手前側は惑星の上を横切る
    /// （横切るところは環のまわりを少し抜いて、線が読み取りやすいようにする）。
    /// </summary>
    private static Sprite MakePlanetIcon()
    {
        const int s = 128;
        float half = s / 2f, aa = 1f / half;
        const float pr = 0.42f, rx = 0.90f, ry = 0.27f, stroke = 0.085f, gap = 0.075f;
        float ang = 20f * Mathf.Deg2Rad, ca = Mathf.Cos(ang), sa = Mathf.Sin(ang);
        var px = new Color32[s * s];
        for (int y = 0; y < s; y++)
        for (int x = 0; x < s; x++)
        {
            float qx0 = (x + 0.5f - half) / half, qy0 = (y + 0.5f - half) / half;
            float r = Dist(qx0, qy0);
            float disc = Mathf.Clamp01((pr - r) / aa + 0.5f);

            // 環の座標（20°傾けた楕円）。楕円までの距離は1次近似 f/|∇f|
            float qx = qx0 * ca + qy0 * sa, qy = -qx0 * sa + qy0 * ca;
            float f = (qx / rx) * (qx / rx) + (qy / ry) * (qy / ry) - 1f;
            float gx = 2f * qx / (rx * rx), gy = 2f * qy / (ry * ry);
            float de = Mathf.Abs(f) / Mathf.Max(0.0001f, Mathf.Sqrt(gx * gx + gy * gy));
            float ring = Mathf.Clamp01((stroke * 0.5f - de) / aa + 0.5f);

            bool front = qy < 0f;
            if (front)
                disc *= Mathf.Clamp01((de - (stroke * 0.5f + gap)) / aa + 0.5f); // 手前の環のまわりを抜く
            else
                ring *= Mathf.Clamp01((r - (pr + gap)) / aa + 0.5f);            // 奥の環は惑星に隠れる
            px[y * s + x] = White(Mathf.Max(disc, ring));
        }
        return ToSprite(Tex(s, s, px, "space_planet_icon"));
    }

    /// <summary>錠前: 角丸の本体（鍵穴を抜く）＋上の U 字のつる</summary>
    private static Sprite MakeLock()
    {
        const int s = 96;
        float half = s / 2f, aa = 1f / half;
        var px = new Color32[s * s];
        for (int y = 0; y < s; y++)
        for (int x = 0; x < s; x++)
        {
            float u = (x + 0.5f - half) / half, v = (y + 0.5f - half) / half;
            // 本体
            float body = Mathf.Clamp01(0.5f - SdRoundBox(u, v + 0.30f, 0.62f, 0.44f, 0.16f) / aa);
            // 鍵穴（丸＋下の切れ込み）を抜く
            float hole = Mathf.Max(Mathf.Clamp01(0.5f - (Dist(u, v + 0.22f) - 0.11f) / aa),
                Mathf.Clamp01(0.5f - SdRoundBox(u, v + 0.40f, 0.05f, 0.12f, 0.04f) / aa));
            body *= 1f - hole;
            // つる（上は半円、下は本体まで伸びる2本の脚）
            const float r = 0.36f, t = 0.085f, cy = 0.16f;
            float d = v >= cy ? Mathf.Abs(Dist(u, v - cy) - r) - t
                : (v > -0.2f ? Mathf.Abs(Mathf.Abs(u) - r) - t : 1f);
            float shackle = Mathf.Clamp01(0.5f - d / aa);
            px[y * s + x] = White(Mathf.Max(body, shackle));
        }
        return ToSprite(Tex(s, s, px, "space_lock"));
    }

    /// <summary>QRコード風のアイコン: 9×9 のマス（3隅に位置合わせの四角）を角の丸い小さな四角で描く</summary>
    private static Sprite MakeQrIcon()
    {
        string[] rows =
        {
            "111010111",
            "101001101",
            "111010111",
            "000110000",
            "101101011",
            "010010110",
            "111001010",
            "101011001",
            "111010111",
        };
        const int s = 128, n = 9;
        float margin = s * 0.06f, cell = (s - margin * 2f) / n;
        var px = new Color32[s * s];
        for (int y = 0; y < s; y++)
        for (int x = 0; x < s; x++)
        {
            float fx = (x + 0.5f - margin) / cell, fy = (s - 1 - y + 0.5f - margin) / cell; // 上の行から
            int cx = Mathf.FloorToInt(fx), cy = Mathf.FloorToInt(fy);
            if (cx < 0 || cy < 0 || cx >= n || cy >= n || rows[cy][cx] != '1') continue;
            float lx = (fx - cx - 0.5f) * cell, ly = (fy - cy - 0.5f) * cell;
            float d = SdRoundBox(lx, ly, cell * 0.44f, cell * 0.44f, cell * 0.18f);
            px[y * s + x] = White(Mathf.Clamp01(0.5f - d));
        }
        return ToSprite(Tex(s, s, px, "space_qr_icon"));
    }

    /// <summary>鈴のアイコン: 上は半円のドーム、下へ行くほど反って広がる胴・裾の縁・上のつまみ・下の玉</summary>
    private static Sprite MakeBellIcon()
    {
        const int s = 128;
        const float top = 0.14f, r = 0.44f, bottom = -0.42f, flare = 0.24f;
        float half = s / 2f, aa = 1f / half;
        var px = new Color32[s * s];
        for (int y = 0; y < s; y++)
        for (int x = 0; x < s; x++)
        {
            float u = (x + 0.5f - half) / half, v = (y + 0.5f - half) / half;
            float d;
            if (v >= top) d = Dist(u, v - top) - r;
            else if (v >= bottom)
            {
                float t = (top - v) / (top - bottom);
                float slope = 2f * flare * t / (top - bottom); // 縁の傾き（横方向の距離を、斜めの縁までの距離に直す）
                d = (Mathf.Abs(u) - (r + flare * t * t)) / Mathf.Sqrt(1f + slope * slope);
            }
            else d = 1f;
            float body = Mathf.Clamp01(0.5f - d / aa);
            float rim = Mathf.Clamp01(0.5f - SdRoundBox(u, v + 0.47f, 0.80f, 0.085f, 0.085f) / aa);
            float knob = Mathf.Clamp01(0.5f - (Dist(u, v - 0.66f) - 0.10f) / aa);
            float clapper = Mathf.Clamp01(0.5f - (Dist(u, v + 0.72f) - 0.12f) / aa);
            px[y * s + x] = White(Mathf.Max(Mathf.Max(body, rim), Mathf.Max(knob, clapper)));
        }
        return ToSprite(Tex(s, s, px, "space_bell_icon"));
    }

    /// <summary>
    /// 空っぽの電池のアイコン（横向き）: 角の丸いふち・右の出っ張り・左にほんの少し残った量。
    /// 小さく出しても「電池切れ＝力を失った」と分かる形にした（稲妻に斜線は 36 の大きさだと読めなかった）
    /// </summary>
    private static Sprite MakeBatteryEmptyIcon()
    {
        const int s = 128;
        float half = s / 2f, aa = 1f / half;
        var px = new Color32[s * s];
        for (int y = 0; y < s; y++)
        for (int x = 0; x < s; x++)
        {
            float u = (x + 0.5f - half) / half, v = (y + 0.5f - half) / half;
            float outer = SdRoundBox(u + 0.08f, v, 0.78f, 0.46f, 0.14f);
            float inner = SdRoundBox(u + 0.08f, v, 0.62f, 0.30f, 0.06f);
            float shell = Mathf.Max(outer, -inner);
            float nub = SdRoundBox(u - 0.80f, v, 0.08f, 0.18f, 0.05f);
            float level = SdRoundBox(u + 0.58f, v, 0.08f, 0.20f, 0.03f);
            float a = Mathf.Max(Mathf.Clamp01(0.5f - shell / aa),
                Mathf.Max(Mathf.Clamp01(0.5f - nub / aa), Mathf.Clamp01(0.5f - level / aa)));
            px[y * s + x] = White(a);
        }
        return ToSprite(Tex(s, s, px, "space_battery_empty_icon"));
    }

    // ================================================================
    // 背景
    // ================================================================
    /// <summary>
    /// 星雲: 上（紺）→下（黒）のグラデーションに、右上のすみれ色・左下の青緑・中央の桃色の雲と、
    /// 左下→右上へ流れる淡い天の川を重ね、四隅を少し落とす。
    /// </summary>
    private static Texture2D MakeNebula(int w, int h)
    {
        var top = Hex(0x0C1438);
        var bottom = Hex(0x03040E);
        var violetC = Hex(0x5B3BC9);
        var tealC = Hex(0x0E7FA8);
        var pinkC = Hex(0xC04BA8);
        var bandC = Hex(0x6E86D6);
        float asp = (float)h / w;

        // 天の川の帯（アスペクトを掛けた空間での直線）
        float x0 = -0.1f, y0 = 0.10f * asp, x1 = 1.1f, y1 = 0.95f * asp;
        float ldx = x1 - x0, ldy = y1 - y0;
        float len = Mathf.Sqrt(ldx * ldx + ldy * ldy);

        var px = new Color32[w * h];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            float u = (x + 0.5f) / w, v = (y + 0.5f) / h;
            Color c = Color.LerpUnclamped(bottom, top, Mathf.Pow(Smooth(v), 1.15f));

            float n1 = Fbm(u * 2.6f, v * 2.6f * asp, 11, 4);
            float n2 = Fbm(u * 4.2f + 7.3f, v * 4.2f * asp + 1.7f, 23, 4);
            float n3 = Fbm(u * 7.0f + 3.1f, v * 7.0f * asp + 9.2f, 37, 3);
            float cloud1 = Mathf.Clamp01((n1 - 0.38f) * 2.2f);
            float cloud2 = Mathf.Clamp01((n2 - 0.40f) * 2.4f);
            float violet = Blob(u, v, asp, 0.92f, 0.80f, 1.05f) * (0.25f + 0.75f * cloud1);
            float teal = Blob(u, v, asp, 0.02f, 0.30f, 0.95f) * (0.20f + 0.80f * cloud2);
            float pink = Blob(u, v, asp, 0.62f, 0.56f, 0.55f) * cloud1 * cloud2;

            float dist = Mathf.Abs((u - x0) * ldy - (v * asp - y0) * ldx) / len;
            float band = Mathf.Exp(-(dist / 0.20f) * (dist / 0.20f)) * (0.35f + 0.65f * n3);

            c += violetC * (violet * 0.70f) + tealC * (teal * 0.50f) + pinkC * (pink * 0.32f) + bandC * (band * 0.10f);

            float vx = (u - 0.5f) * 2f, vy = (v - 0.5f) * 2f;
            float vig = Mathf.Clamp01(1f - (vx * vx * 0.35f + vy * vy * 0.25f));
            c *= 0.72f + 0.28f * vig;
            c.a = 1f;
            px[y * w + x] = c;
        }
        return Tex(w, h, px, "space_nebula");
    }

    /// <summary>星: 大半は小さく暗い星、たまに大きく明るい星。色は白・青白・暖色を混ぜる</summary>
    private static Texture2D MakeStars(int w, int h, int seed)
    {
        var rnd = new System.Random(seed);
        var px = new Color32[w * h];
        int n = (int)(w * h * 0.0016f);
        for (int i = 0; i < n; i++)
        {
            float sx = (float)rnd.NextDouble() * w;
            float sy = (float)rnd.NextDouble() * h;
            float size = 0.45f + Mathf.Pow((float)rnd.NextDouble(), 3.5f) * 1.35f;
            float inten = 0.25f + Mathf.Pow((float)rnd.NextDouble(), 2.2f) * 0.75f;
            if (size > 1.2f) inten = Mathf.Min(1f, inten + 0.3f);
            double t = rnd.NextDouble();
            Color32 col = t < 0.12 ? new Color32(255, 219, 179, 255)   // 暖色
                        : t < 0.30 ? new Color32(184, 219, 255, 255)   // 青白
                                   : new Color32(240, 245, 255, 255);
            float sig = size * 0.62f + 0.30f;
            int rad = Mathf.CeilToInt(sig * 3f) + 1;
            int xa = Mathf.Max(0, (int)sx - rad), xb = Mathf.Min(w, (int)sx + rad + 1);
            int ya = Mathf.Max(0, (int)sy - rad), yb = Mathf.Min(h, (int)sy + rad + 1);
            for (int yy = ya; yy < yb; yy++)
            for (int xx = xa; xx < xb; xx++)
            {
                float ddx = xx + 0.5f - sx, ddy = yy + 0.5f - sy;
                float a = inten * Mathf.Exp(-(ddx * ddx + ddy * ddy) / (2f * sig * sig));
                byte ab = (byte)(Mathf.Clamp01(a) * 255f + 0.5f);
                int idx = yy * w + xx;
                if (ab > px[idx].a)
                {
                    col.a = ab;
                    px[idx] = col;
                }
            }
        }
        return Tex(w, h, px, "space_stars");
    }

    /// <summary>
    /// 白い三角形（a・b・c は画像の左下を (0,0)、右上を (1,1) とした頂点）。ふちは1ピクセルでなめらかに消える
    /// </summary>
    private static Sprite MakeTriangle(int w, int h, Vector2 a, Vector2 b, Vector2 c, string name)
    {
        var p0 = new Vector2(a.x * w, a.y * h);
        var p1 = new Vector2(b.x * w, b.y * h);
        var p2 = new Vector2(c.x * w, c.y * h);
        // 反時計回りにそろえる（内側が各辺の左になる）
        float area = (p1.x - p0.x) * (p2.y - p0.y) - (p1.y - p0.y) * (p2.x - p0.x);
        if (area < 0f) (p1, p2) = (p2, p1);

        var px = new Color32[w * h];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            var p = new Vector2(x + 0.5f, y + 0.5f);
            float d = Mathf.Min(EdgeDistance(p0, p1, p), Mathf.Min(EdgeDistance(p1, p2, p), EdgeDistance(p2, p0, p)));
            px[y * w + x] = White(d + 0.5f);
        }
        return ToSprite(Tex(w, h, px, name));
    }

    /// <summary>
    /// 2人の人の形（手前の人と、右上の奥の人）。奥の人は手前の人のまわりを少しあけて、重なりが分かるようにする
    /// </summary>
    private static Sprite MakeFriendsIcon(int s)
    {
        var px = new Color32[s * s];
        float k = s / 128f;
        for (int y = 0; y < s; y++)
        for (int x = 0; x < s; x++)
        {
            float fx = x + 0.5f, fy = y + 0.5f; // テクスチャは下の行が先なので、y は上が+
            float front = PersonDistance(fx, fy, s * 0.42f, s * 0.47f, k);
            float back = PersonDistance(fx, fy, s * 0.64f, s * 0.56f, k * 0.86f);
            float aFront = Mathf.Clamp01(0.5f - front);
            float aBack = Mathf.Clamp01(0.5f - back) * Mathf.Clamp01(front - 5f * k + 0.5f);
            px[y * s + x] = White(Mathf.Max(aFront, aBack));
        }
        return ToSprite(Tex(s, s, px, "space_friends"));
    }

    /// <summary>人の形（頭の円＋肩＝楕円の上半分）までの距離（ピクセル。内側が負。y は上が+）</summary>
    private static float PersonDistance(float px, float py, float cx, float cy, float k)
    {
        float hx = px - cx, hy = py - (cy + 20f * k);
        float head = Mathf.Sqrt(hx * hx + hy * hy) - 15f * k;
        float by = cy - 26f * k, rx = 30f * k, ry = 26f * k;
        float dx = (px - cx) / rx, dy = (py - by) / ry;
        float shoulders = (Mathf.Sqrt(dx * dx + dy * dy) - 1f) * Mathf.Min(rx, ry);
        float body = Mathf.Max(shoulders, by - py); // 下半分は切る
        return Mathf.Min(head, body);
    }

    /// <summary>辺 a→b から点 p までの距離（ピクセル。辺の左側＝反時計回りの三角形の内側が正）</summary>
    private static float EdgeDistance(Vector2 a, Vector2 b, Vector2 p)
    {
        var e = b - a;
        return (e.x * (p.y - a.y) - e.y * (p.x - a.x)) / Mathf.Max(1e-5f, e.magnitude);
    }

    // ================================================================
    // 共通ヘルパー
    // ================================================================
    private static Texture2D Tex(int w, int h, Color32[] px, string name)
    {
        var t = new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            name = name,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };
        t.SetPixels32(px);
        t.Apply(false, true); // CPU側のコピーは解放する
        return t;
    }

    private static Sprite ToSprite(Texture2D t, Vector4 border = default)
    {
        var sp = Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f), 100f, 0,
            SpriteMeshType.FullRect, border);
        sp.name = t.name;
        return sp;
    }

    private static Color32 White(float a) => new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255f + 0.5f));

    private static Color Hex(int rgb) =>
        new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);

    private static float Dist(float x, float y) => Mathf.Sqrt(x * x + y * y);

    private static float Smooth(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }

    /// <summary>角丸矩形の符号付き距離（内側が負）。hx,hy は半幅・半高、r は角の半径</summary>
    private static float SdRoundBox(float px, float py, float hx, float hy, float r)
    {
        float qx = Mathf.Abs(px) - (hx - r);
        float qy = Mathf.Abs(py) - (hy - r);
        float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f);
        return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
    }

    private static float Blob(float u, float v, float asp, float cx, float cy, float r)
    {
        float dx = u - cx, dy = (v - cy) * asp;
        float t = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy) / r);
        return t * t;
    }

    /// <summary>乗算済みアルファの「上に重ねる」合成（c,a に src を重ねる）</summary>
    private static void Over(ref Color c, ref float a, Color src, float srcA)
    {
        if (srcA <= 0f) return;
        c = src * srcA + c * (1f - srcA);
        a = srcA + a * (1f - srcA);
    }

    /// <summary>乗算済みアルファ → ストレートアルファの Color32</summary>
    private static Color32 Straight(Color c, float a)
    {
        if (a <= 0.0001f) return new Color32(0, 0, 0, 0);
        float inv = 1f / a;
        return new Color32(
            (byte)(Mathf.Clamp01(c.r * inv) * 255f + 0.5f),
            (byte)(Mathf.Clamp01(c.g * inv) * 255f + 0.5f),
            (byte)(Mathf.Clamp01(c.b * inv) * 255f + 0.5f),
            (byte)(Mathf.Clamp01(a) * 255f + 0.5f));
    }

    // ---- 値ノイズ（Python試作と同じハッシュ・同じ補間） ----
    private static float Hash(int x, int y, int seed)
    {
        unchecked
        {
            uint h = (uint)x * 374761393u + (uint)y * 668265263u + (uint)seed * 2246822519u;
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / 16777215f;
        }
    }

    private static float ValueNoise(float x, float y, int seed)
    {
        int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
        float tx = x - xi, ty = y - yi;
        float sx = tx * tx * (3f - 2f * tx), sy = ty * ty * (3f - 2f * ty);
        float a = Hash(xi, yi, seed), b = Hash(xi + 1, yi, seed);
        float c = Hash(xi, yi + 1, seed), d = Hash(xi + 1, yi + 1, seed);
        float ab = a + (b - a) * sx, cd = c + (d - c) * sx;
        return ab + (cd - ab) * sy;
    }

    private static float Fbm(float x, float y, int seed, int octaves)
    {
        float sum = 0f, amp = 0.5f, f = 1f, norm = 0f;
        for (int i = 0; i < octaves; i++)
        {
            sum += amp * ValueNoise(x * f, y * f, seed + i * 31);
            norm += amp;
            amp *= 0.5f;
            f *= 2.03f;
        }
        return sum / norm;
    }
}
