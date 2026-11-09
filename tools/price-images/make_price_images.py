"""
INTALES Cafe の料金表（宇宙デザイン）を作る。新ホームの右上の水色の小さな惑星から開く「料金表」に出す画像。

  python make_price_images.py

出力:
  out/price_weekday.jpg, out/price_holiday.jpg                    … 確認用（そのまま店頭やSNSにも使える）
  ../../Assets/Resources/Prices/price_weekday.bytes, ...holiday.bytes … アプリが読む（中身は同じ JPG。
                                                                       .bytes にして Unity の画像の圧縮で文字がにじまないようにする）
料金や文言が変わったら、下の PRICES・FOOTER を直して実行し直し、アプリをビルドし直す。

キャラクターを入れるとき: 透明な背景の PNG を chars/ に置くと、CHARACTERS の位置に重ねる（無ければ入れない）。
  （元の料金表の JPG からは、白いふち取りと方眼紙が同じ色で、きれいに切り抜けなかった）
フォント: Windows の Noto Sans JP（可変フォント。太さを変えて使う）。必要なもの: Pillow, numpy
"""
import math
import os
import random

import numpy as np
from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.abspath(os.path.join(HERE, "..", ".."))
FONT_PATH = r"C:\Windows\Fonts\NotoSansJP-VF.ttf"
LOGO = os.path.join(PROJECT, "Assets", "Textures", "Logo", "Logo.png")
OUT_DIR = os.path.join(HERE, "out")
RES_DIR = os.path.join(PROJECT, "Assets", "Resources", "Prices")
CHARS_DIR = os.path.join(HERE, "chars")

W, H = 1200, 1790
S = 2  # 2倍の大きさで描いてから縮める（線・光・文字のふちをなめらかに）

WHITE = (255, 255, 255)
SUB = (196, 208, 255)
MUTED = (150, 164, 214)
DARK = (8, 12, 36)
GOLD = (255, 211, 122)
CYAN = (92, 225, 255)
VIOLET = (155, 123, 255)
PINK = (255, 122, 217)

AGES = ["大人", "学生", "小学生"]

# 料金（元の料金表 heijitu.jpg / kyuujitu.jpg と同じ。2026-11 時点）
PRICES = {
    "weekday": {
        "label": "平日料金",
        "accent": CYAN, "accent2": VIOLET,
        "plans": [
            ("1", "時間", ["1ドリンクオーダー制"], [600, 500, 300]),
            ("3", "時間", ["ソフトドリンク", "飲み放題付"], [2000, 1600, 1000]),
            ("5", "時間", ["ソフトドリンク", "飲み放題付"], [2500, 2000, 1250]),
            ("FREE", "", ["ソフトドリンク", "飲み放題付"], [3000, 2300, 1500]),
        ],
        "extension": ("平日の延長料金は通常より少しお得に", "500円/1H", "です！"),
        "nebula": [((190, 300), 560, (120, 80, 255), 0.30), ((1060, 760), 620, (40, 170, 255), 0.22),
                   ((140, 1420), 520, (230, 90, 200), 0.14), ((960, 1700), 460, (60, 200, 255), 0.12)],
    },
    "holiday": {
        "label": "土日・祝日料金",
        "accent": PINK, "accent2": VIOLET,
        "plans": [
            ("1", "時間", ["1ドリンクオーダー制"], [700, 600, 350]),
            ("3", "時間", ["ソフトドリンク", "飲み放題付"], [2300, 1800, 1150]),
            ("5", "時間", ["ソフトドリンク", "飲み放題付"], [2800, 2200, 1400]),
            ("FREE", "", ["ソフトドリンク", "飲み放題付"], [3500, 2800, 1750]),
        ],
        "extension": ("土日・祝日の延長料金は通常より少しお得に", "600円/1H", "です！"),
        "nebula": [((1010, 260), 580, (255, 90, 190), 0.26), ((170, 820), 640, (130, 80, 255), 0.25),
                   ((1040, 1480), 480, (255, 190, 90), 0.10), ((240, 1720), 460, (255, 110, 200), 0.12)],
    },
}

# 料金のカードの上の2つの吹き出し（元の料金表の吹き出しと同じ文言）
CHIP_DISCOUNT = ["メンズデー・レディースデーは", "ボードゲームカフェ利用料金が", "5%OFF！"]
CHIP_DRINK = ["500円で", "ソフトドリンク飲み放題を", "付けられます"]

# キャラクター（chars/ に透明な PNG があれば、ファイル名ごとにこの位置・高さで重ねる）
CHARACTERS = {
    "weekday": [("weekday_right.png", (1075, 905), 470), ("weekday_left.png", (130, 1500), 470)],
    "holiday": [("holiday_right.png", (1075, 905), 470), ("holiday_left.png", (130, 1500), 470)],
}

# レイアウト（1倍のときの座標）
X0, X1 = 60, 1140
LOGO_TOP, LOGO_H = 14, 200
TITLE_Y = 262
PILL_Y, PILL_W, PILL_H = 350, 340, 86
CHIP_TOP, CHIP_H = 412, 154
CARD_TOP, CARD_H, CARD_GAP = 590, 210, 20
FOOT_TOP, FOOT_H = 1516, 250


def sc(v):
    return int(round(v * S))


_fonts = {}


def font(size, weight=700):
    key = (size, weight)
    if key not in _fonts:
        f = ImageFont.truetype(FONT_PATH, sc(size))
        f.set_variation_by_axes([weight])
        _fonts[key] = f
    return _fonts[key]


class Canvas:
    def __init__(self, w, h):
        self.w, self.h = sc(w), sc(h)
        self.img = Image.new("RGBA", (self.w, self.h), (0, 0, 0, 255))

    def put(self, layer, x, y):
        """layer を (x, y)（2倍の座標）に重ねる。はみ出した分は切る"""
        lx0, ly0 = max(0, -x), max(0, -y)
        lx1, ly1 = min(layer.width, self.w - x), min(layer.height, self.h - y)
        if lx1 <= lx0 or ly1 <= ly0:
            return
        part = layer.crop((lx0, ly0, lx1, ly1))
        self.img.alpha_composite(part, (x + lx0, y + ly0))

    def full(self):
        return Image.new("RGBA", (self.w, self.h), (0, 0, 0, 0))


def solid(size, color, alpha_mask):
    """色 color・透明度 alpha_mask（L画像）の画像"""
    im = Image.new("RGBA", size, color + (255,))
    im.putalpha(alpha_mask)
    return im


def scaled(mask, k):
    return mask.point(lambda v: int(v * k))


# ---------------------------------------------------------------- 背景
def background(cv, nebulae, seed):
    h = cv.h
    t = np.linspace(0, 1, h).reshape(h, 1, 1)
    top, mid, bot = np.array((15, 19, 56)), np.array((8, 11, 35)), np.array((4, 6, 20))
    upper = top + (mid - top) * np.clip(t / 0.5, 0, 1)
    lower = mid + (bot - mid) * np.clip((t - 0.5) / 0.5, 0, 1)
    arr = np.broadcast_to(np.where(t < 0.5, upper, lower), (h, cv.w, 3)).astype(np.float32)
    cv.img = Image.fromarray(np.clip(arr, 0, 255).astype(np.uint8), "RGB").convert("RGBA")
    for (cx, cy), r, color, a in nebulae:
        nebula(cv, cx, cy, r, color, a * 1.35)
    # かすかな粒（JPG の縞を目立たなくする）
    rng = np.random.default_rng(seed)
    noise = rng.normal(0, 2.2, (cv.h, cv.w, 1)).astype(np.float32)
    base = np.asarray(cv.img).astype(np.float32)
    base[..., :3] += noise
    cv.img = Image.fromarray(np.clip(base, 0, 255).astype(np.uint8), "RGBA")


def nebula(cv, cx, cy, r, color, alpha):
    R = sc(r)
    yy, xx = np.mgrid[-R:R, -R:R].astype(np.float32)
    d = np.sqrt(xx * xx + yy * yy) / R
    a = np.clip(1 - d, 0, 1) ** 2.0 * alpha * 255
    layer = solid((2 * R, 2 * R), color, Image.fromarray(a.astype(np.uint8), "L"))
    cv.put(layer, sc(cx) - R, sc(cy) - R)


def stars(cv, n, seed, tints):
    rng = random.Random(seed)
    layer = cv.full()
    d = ImageDraw.Draw(layer)
    for _ in range(n):
        x, y = rng.uniform(0, cv.w), rng.uniform(0, cv.h)
        r = sc(rng.choice([0.5, 0.6, 0.7, 0.8, 1.0, 1.2, 1.5, 1.8]))
        a = int(rng.uniform(0.25, 0.95) * 255)
        c = rng.choice(tints)
        d.ellipse((x - r, y - r, x + r, y + r), fill=c + (a,))
    cv.img.alpha_composite(layer)


def sparkle(cv, x, y, size, color, alpha=1.0):
    """4つの角の光る星（中心 x, y・大きさ size は1倍の座標）"""
    R = sc(size)
    pad = R
    im = Image.new("L", (4 * R, 4 * R), 0)
    d = ImageDraw.Draw(im)
    c = 2 * R
    w = R * 0.16
    d.polygon([(c, c - R), (c + w, c - w), (c + R, c), (c + w, c + w), (c, c + R), (c - w, c + w), (c - R, c),
               (c - w, c - w)], fill=255)
    glow = im.filter(ImageFilter.GaussianBlur(R * 0.35))
    layer = solid(im.size, color, scaled(glow, 0.8 * alpha))
    layer.alpha_composite(solid(im.size, WHITE, scaled(im, alpha)))
    cv.put(layer, sc(x) - c, sc(y) - c)


def planet(cv, cx, cy, r, c1, c2, ring=None, seed=1):
    """球（左上から光が当たる）。ring=(色, 傾き) で輪を付ける"""
    R = sc(r)
    pad = R * 2
    size = 2 * pad
    yy, xx = np.mgrid[0:size, 0:size].astype(np.float32)
    nx, ny = (xx - pad) / R, (yy - pad) / R
    d2 = nx * nx + ny * ny
    inside = d2 <= 1
    nz = np.sqrt(np.clip(1 - d2, 0, 1))
    light = np.array([-0.55, -0.6, 0.58])
    light /= np.linalg.norm(light)
    lam = np.clip(nx * light[0] + ny * light[1] + nz * light[2], 0, 1)
    rng = np.random.default_rng(seed)
    bands = 0.5 + 0.5 * np.sin(ny * 7.0 + rng.uniform(0, 6)) * 0.35
    t = np.clip((ny + 1) / 2, 0, 1)[..., None]
    base = np.array(c1) * (1 - t) + np.array(c2) * t
    col = base * (0.18 + 0.95 * lam[..., None]) * (0.85 + 0.3 * bands[..., None])
    rim = np.clip(1 - nz, 0, 1) ** 3 * 120
    col = col + rim[..., None] * np.array([0.6, 0.8, 1.0])
    edge = np.clip((1 - np.sqrt(d2)) * R, 0, 1)
    alpha = (edge * 255).astype(np.uint8)
    sphere = Image.fromarray(np.clip(col, 0, 255).astype(np.uint8), "RGB").convert("RGBA")
    sphere.putalpha(Image.fromarray(alpha, "L"))
    glow = solid((size, size), c1, Image.fromarray(alpha, "L").filter(ImageFilter.GaussianBlur(R * 0.35)).point(
        lambda v: int(v * 0.45)))
    layer = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    layer.alpha_composite(glow)
    if ring:
        rc, tilt = ring
        # 輪: 半透明の太い帯に、明るい細い帯を重ねる（外側の楕円から内側の楕円を抜いて作る）
        ring_im = Image.new("L", (size, size), 0)
        for k_out, k_in, a in ((1.95, 1.40, 72), (1.72, 1.56, 165), (1.50, 1.45, 120)):
            band = Image.new("L", (size, size), 0)
            bd = ImageDraw.Draw(band)
            rx, ry = R * k_out, R * k_out * 0.26
            bd.ellipse((pad - rx, pad - ry, pad + rx, pad + ry), fill=a)
            rx, ry = R * k_in, R * k_in * 0.26
            bd.ellipse((pad - rx, pad - ry, pad + rx, pad + ry), fill=0)
            ring_im = ImageChops.lighter(ring_im, band)
        ring_im = ring_im.rotate(tilt, resample=Image.BICUBIC, center=(pad, pad))
        back = ring_im.copy()
        bd = np.asarray(back).copy()
        bd[pad:, :] = 0  # 奥（上半分）だけ
        front = np.asarray(ring_im).copy()
        front[:pad, :] = 0
        layer.alpha_composite(solid((size, size), rc, Image.fromarray(bd, "L")))
        layer.alpha_composite(sphere)
        layer.alpha_composite(solid((size, size), rc, Image.fromarray(front, "L")))
    else:
        layer.alpha_composite(sphere)
    cv.put(layer, sc(cx) - pad, sc(cy) - pad)


# ---------------------------------------------------------------- 文字
def text_box(s, f, xy, anchor):
    d = ImageDraw.Draw(Image.new("L", (1, 1)))
    return d.textbbox(xy, s, font=f, anchor=anchor)


def text(cv, xy, s, f, color, anchor="mm", alpha=1.0, glow=None):
    """文字（xy は1倍の座標）。glow=(色, 半径, 濃さ) で後ろに光"""
    x, y = sc(xy[0]), sc(xy[1])
    l, t, r, b = text_box(s, f, (x, y), anchor)
    pad = sc(40)
    w, h = r - l + 2 * pad, b - t + 2 * pad
    mask = Image.new("L", (w, h), 0)
    ImageDraw.Draw(mask).text((x - l + pad, y - t + pad), s, font=f, fill=255, anchor=anchor)
    layer = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    if glow:
        gc, gr, ga = glow
        layer.alpha_composite(solid((w, h), gc, scaled(mask.filter(ImageFilter.GaussianBlur(sc(gr))), ga)))
    if isinstance(color, tuple) and len(color) == 2 and isinstance(color[0], tuple):
        top, bottom = color  # 上から下へのグラデーション
        g = np.linspace(0, 1, h)[:, None, None]
        tt = np.clip((g * h - (pad + (b - t) * 0.05)) / max(1, (b - t) * 0.9), 0, 1)
        arr = np.array(top) * (1 - tt) + np.array(bottom) * tt
        fill = Image.fromarray(np.broadcast_to(arr, (h, w, 3)).astype(np.uint8), "RGB").convert("RGBA")
        fill.putalpha(scaled(mask, alpha))
        layer.alpha_composite(fill)
    else:
        layer.alpha_composite(solid((w, h), color, scaled(mask, alpha)))
    cv.put(layer, l - pad, t - pad)
    return (r - l) / S


def width(s, f):
    return f.getlength(s) / S


def rich_line(cv, cx, y, segs):
    """色・太さの違う文字を1行に並べて中央にそろえる。segs=[(文字, フォント, 色), ...]"""
    total = sum(width(s, f) for s, f, _ in segs)
    x = cx - total / 2
    for s, f, c in segs:
        text(cv, (x, y), s, f, c, anchor="lm")
        x += width(s, f)


# ---------------------------------------------------------------- 部品
def rounded(cv, box, r, fill=None, outline=None, width_=2, glow=None):
    x0, y0, x1, y1 = [sc(v) for v in box]
    pad = sc(60)
    w, h = x1 - x0 + 2 * pad, y1 - y0 + 2 * pad
    layer = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    if glow:
        gc, gr, ga = glow
        m = Image.new("L", (w, h), 0)
        ImageDraw.Draw(m).rounded_rectangle((pad, pad, w - pad, h - pad), sc(r), fill=255)
        layer.alpha_composite(solid((w, h), gc, scaled(m.filter(ImageFilter.GaussianBlur(sc(gr))), ga)))
    d = ImageDraw.Draw(layer)
    if fill:
        f = Image.new("RGBA", (w, h), (0, 0, 0, 0))
        ImageDraw.Draw(f).rounded_rectangle((pad, pad, w - pad, h - pad), sc(r), fill=fill)
        layer.alpha_composite(f)
    if outline:
        o = Image.new("RGBA", (w, h), (0, 0, 0, 0))
        ImageDraw.Draw(o).rounded_rectangle((pad, pad, w - pad, h - pad), sc(r), outline=outline, width=sc(width_))
        layer.alpha_composite(o)
    cv.put(layer, x0 - pad, y0 - pad)


def gradient_pill(cv, box, c1, c2, glow):
    x0, y0, x1, y1 = [sc(v) for v in box]
    w, h = x1 - x0, y1 - y0
    t = np.linspace(0, 1, w)[None, :, None]
    arr = np.array(c1) * (1 - t) + np.array(c2) * t
    fill = Image.fromarray(np.broadcast_to(arr, (h, w, 3)).astype(np.uint8), "RGB").convert("RGBA")
    m = Image.new("L", (w, h), 0)
    ImageDraw.Draw(m).rounded_rectangle((0, 0, w - 1, h - 1), h // 2, fill=255)
    fill.putalpha(m)
    pad = sc(50)
    gl = Image.new("L", (w + 2 * pad, h + 2 * pad), 0)
    gl.paste(m, (pad, pad))
    cv.put(solid(gl.size, glow, scaled(gl.filter(ImageFilter.GaussianBlur(sc(22))), 0.55)), x0 - pad, y0 - pad + sc(6))
    cv.put(fill, x0, y0)


def dotted(cv, x0, x1, y, color, alpha):
    layer = cv.full()
    d = ImageDraw.Draw(layer)
    r = sc(2.2)
    x = x0
    while x <= x1:
        d.ellipse((sc(x) - r, sc(y) - r, sc(x) + r, sc(y) + r), fill=color + (int(alpha * 255),))
        x += 14
    cv.img.alpha_composite(layer)


def drink_icon(cv, cx, cy, color):
    """グラスとストロー（線で描く）"""
    layer = cv.full()
    d = ImageDraw.Draw(layer)
    w = sc(4)
    pts = [(cx - 20, cy - 22), (cx + 20, cy - 22), (cx + 14, cy + 26), (cx - 14, cy + 26)]
    d.line([(sc(x), sc(y)) for x, y in pts + [pts[0]]], fill=color + (255,), width=w, joint="curve")
    d.line([(sc(cx - 17), sc(cy - 6)), (sc(cx + 17), sc(cy - 6))], fill=color + (150,), width=sc(3))
    d.line([(sc(cx + 4), sc(cy - 22)), (sc(cx + 14), sc(cy - 40)), (sc(cx + 26), sc(cy - 40))], fill=color + (255,),
           width=w, joint="curve")
    for bx, by, br in ((cx - 5, cy + 8, 3.5), (cx + 5, cy + 16, 2.5), (cx + 2, cy + 2, 2.0)):
        d.ellipse((sc(bx - br), sc(by - br), sc(bx + br), sc(by + br)), outline=color + (210,), width=sc(2))
    cv.img.alpha_composite(layer)


def logo(cv, cx, top, height):
    im = Image.open(LOGO).convert("RGBA")
    im = im.crop(im.getbbox())
    k = sc(height) / im.height
    im = im.resize((int(im.width * k), int(im.height * k)), Image.LANCZOS)
    pad = sc(40)
    a = Image.new("L", (im.width + 2 * pad, im.height + 2 * pad), 0)
    a.paste(im.getchannel("A"), (pad, pad))
    x, y = sc(cx) - im.width // 2, sc(top)
    cv.put(solid(a.size, (120, 200, 255), scaled(a.filter(ImageFilter.GaussianBlur(sc(18))), 0.45)), x - pad, y - pad)
    cv.put(im, x, y)


def characters(cv, kind):
    for name, (cx, bottom), height in CHARACTERS[kind]:
        path = os.path.join(CHARS_DIR, name)
        if not os.path.exists(path):
            continue
        im = Image.open(path).convert("RGBA")
        im = im.crop(im.getbbox())
        k = sc(height) / im.height
        im = im.resize((int(im.width * k), int(im.height * k)), Image.LANCZOS)
        pad = sc(40)
        a = Image.new("L", (im.width + 2 * pad, im.height + 2 * pad), 0)
        a.paste(im.getchannel("A"), (pad, pad))
        x, y = sc(cx) - im.width // 2, sc(bottom) - im.height
        cv.put(solid(a.size, PRICES[kind]["accent"], scaled(a.filter(ImageFilter.GaussianBlur(sc(14))), 0.55)),
               x - pad, y - pad)
        cv.put(im, x, y)


# ---------------------------------------------------------------- 料金表
def chip(cv, box, lines, accent, icon, highlight_first):
    x0, y0, x1, y1 = box
    rounded(cv, box, 30, fill=(20, 26, 74, 205), outline=accent + (130,), width_=2, glow=(accent, 26, 0.18))
    tx = x0 + 104
    if icon == "star":
        sparkle(cv, x0 + 56, (y0 + y1) / 2, 28, GOLD)
    else:
        drink_icon(cv, x0 + 54, (y0 + y1) / 2 + 6, accent)
    big = 0 if highlight_first else len(lines) - 1  # 大きく出す行（金色）
    sizes = [(38, 900) if i == big else (25, 700) for i in range(len(lines))]
    heights = [(46 if s[0] >= 38 else 34) for s in sizes]
    y = (y0 + y1) / 2 - sum(heights) / 2
    for line, (size, weight), lh in zip(lines, sizes, heights):
        c = (GOLD, (255, 170, 90)) if size >= 38 else WHITE
        text(cv, (tx, y + lh / 2), line, font(size, weight), c, anchor="lm",
             glow=(GOLD, 10, 0.35) if size >= 38 else None)
        y += lh


def price_card(cv, y0, plan, accent, accent2):
    big, unit, subs, prices = plan
    x0, x1, y1 = X0, X1, y0 + CARD_H
    rounded(cv, (x0, y0, x1, y1), 34, fill=(16, 22, 66, 190), outline=accent + (110,), width_=2,
            glow=(accent, 30, 0.12))
    # 上側のうっすらした光
    hl = Image.new("L", (sc(x1 - x0), sc(CARD_H)), 0)
    hd = ImageDraw.Draw(hl)
    hd.rounded_rectangle((0, 0, hl.width - 1, hl.height - 1), sc(34), fill=255)
    grad = np.clip(1 - np.linspace(0, 1, hl.height) / 0.55, 0, 1)[:, None] * 0.07 * 255
    hl = Image.fromarray((np.asarray(hl) / 255 * grad).astype(np.uint8), "L")
    cv.put(solid(hl.size, WHITE, hl), sc(x0), sc(y0))

    # 左: 時間（数字を大きく）と、付いているもの
    bx0, bx1 = x0 + 22, x0 + 360
    by0, by1 = y0 + 18, y1 - 18
    rounded(cv, (bx0, by0, bx1, by1), 26, fill=(255, 255, 255, 16), outline=accent + (80,), width_=2)
    cx = (bx0 + bx1) / 2
    num_y = by0 + (58 if len(subs) == 2 else 70)
    if unit:
        fb, fu = font(124, 900), font(46, 800)
        wb, wu = width(big, fb), width(unit, fu)
        left = cx - (wb + 10 + wu) / 2
        text(cv, (left, num_y + 40), big, fb, (WHITE, accent), anchor="ls", glow=(accent, 16, 0.55))
        text(cv, (left + wb + 10, num_y + 40), unit, fu, WHITE, anchor="ls")
    else:
        text(cv, (cx, num_y + 2), big, font(96, 900), (WHITE, accent), anchor="mm", glow=(accent, 16, 0.55))
    sy = by1 - (24 if len(subs) == 1 else 50)
    for i, s in enumerate(subs):
        text(cv, (cx, sy + i * 32), s, font(25, 700), SUB, anchor="mm")

    # 右: 大人・学生・小学生
    rx0, rx1 = bx1 + 46, x1 - 40
    row_h = (CARD_H - 36) / 3
    for i, (age, price) in enumerate(zip(AGES, prices)):
        cy = y0 + 18 + row_h * (i + 0.5)
        text(cv, (rx0, cy), age, font(38, 700), (206, 216, 255), anchor="lm")
        yen = font(32, 800)
        wy = width("円", yen)
        text(cv, (rx1, cy + 4), "円", yen, SUB, anchor="rm")
        text(cv, (rx1 - wy - 4, cy), f"{price:,}", font(52, 900), WHITE, anchor="rm", glow=(accent2, 12, 0.35))
        if i < 2:
            dotted(cv, rx0 - 4, rx1, y0 + 18 + row_h * (i + 1), accent, 0.55)


def footer(cv, kind, accent):
    y0, y1 = FOOT_TOP, FOOT_TOP + FOOT_H
    rounded(cv, (X0, y0, X1, y1), 34, fill=(14, 18, 54, 185), outline=(255, 255, 255, 34), width_=2)
    cx = (X0 + X1) / 2
    hi = font(31, 900)
    n = font(28, 700)
    ext = PRICES[kind]["extension"]
    lines = [
        [("小学生未満→無料", font(33, 900), GOLD), ("  でご案内いたします", n, WHITE)],
        [(ext[0], n, WHITE), (ext[1], hi, accent), (ext[2], n, WHITE)],
        [("利用パックより1つ上のパックにする場合は差額を頂戴いたします", font(26, 600), SUB)],
        [("1,600円/2H～", hi, GOLD), ("でアルコール・ノンアルコールカクテル含む", n, WHITE)],
        [("ドリンク(約50種類)の飲み放題", hi, GOLD), ("を追加することが可能です", n, WHITE)],
    ]
    step = (FOOT_H - 40) / len(lines)
    for i, segs in enumerate(lines):
        rich_line(cv, cx, y0 + 20 + step * (i + 0.5), segs)


def decorations(cv, kind, accent):
    # リング付きの惑星（右上・一部は画面の外）と小さな惑星
    if kind == "weekday":
        planet(cv, 1118, 118, 92, (150, 120, 255), (60, 70, 190), ring=((190, 210, 255), -14), seed=3)
        planet(cv, 96, 182, 40, (90, 220, 255), (30, 110, 200), seed=7)
    else:
        planet(cv, 1118, 118, 92, (255, 140, 210), (150, 70, 200), ring=((255, 220, 240), -14), seed=5)
        planet(cv, 96, 182, 40, (255, 210, 120), (220, 120, 70), seed=9)
    for x, y, s, c in ((210, 96, 20, accent), (1000, 330, 16, WHITE), (60, 560, 14, accent), (1150, 560, 18, GOLD),
                       (40, 1000, 12, WHITE), (1170, 1250, 14, accent), (28, 1503, 14, GOLD), (640, 1782, 12, WHITE),
                       (880, 32, 12, WHITE), (330, 365, 10, accent)):
        sparkle(cv, x, y, s, c, 0.9)


def render(kind):
    p = PRICES[kind]
    accent, accent2 = p["accent"], p["accent2"]
    cv = Canvas(W, H)
    background(cv, p["nebula"], 7 if kind == "weekday" else 8)
    stars(cv, 460, 21 if kind == "weekday" else 22, [WHITE, WHITE, WHITE, (190, 230, 255), (255, 210, 240)])
    decorations(cv, kind, accent)

    logo(cv, W / 2, LOGO_TOP, LOGO_H)
    text(cv, (W / 2, TITLE_Y), "ボードゲームカフェ料金表", font(66, 900), WHITE, glow=(accent, 14, 0.5))
    # 帯の幅は見出しの文字に合わせて広げる（「土日・祝日料金」は長い。2026-11-09）
    lf = font(52, 900)
    l, _, r, _ = text_box(p["label"], lf, (0, 0), "mm")
    pill_w = max(PILL_W, (r - l) / sc(1) + 110)
    gradient_pill(cv, (W / 2 - pill_w / 2, PILL_Y - PILL_H / 2, W / 2 + pill_w / 2, PILL_Y + PILL_H / 2),
                  accent, accent2, accent)
    text(cv, (W / 2, PILL_Y), p["label"], font(52, 900), DARK)

    half = (X1 - X0 - 20) / 2
    chip(cv, (X0, CHIP_TOP, X0 + half, CHIP_TOP + CHIP_H), CHIP_DISCOUNT, accent, "star", False)
    chip(cv, (X1 - half, CHIP_TOP, X1, CHIP_TOP + CHIP_H), CHIP_DRINK, accent, "drink", True)

    for i, plan in enumerate(p["plans"]):
        price_card(cv, CARD_TOP + i * (CARD_H + CARD_GAP), plan, accent, accent2)
    footer(cv, kind, accent)
    characters(cv, kind)
    return cv.img.convert("RGB").resize((W, H), Image.LANCZOS)


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    os.makedirs(RES_DIR, exist_ok=True)
    for kind in PRICES:
        img = render(kind)
        out = os.path.join(OUT_DIR, f"price_{kind}.jpg")
        img.save(out, quality=92, subsampling=0, optimize=True)
        with open(out, "rb") as f:
            data = f.read()
        with open(os.path.join(RES_DIR, f"price_{kind}.bytes"), "wb") as f:
            f.write(data)
        print(f"{out} ({len(data) // 1024}KB)")


if __name__ == "__main__":
    main()
