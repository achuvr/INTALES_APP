# 惑星回復薬（ショップの品物。2026-11-12）のアイコン。丸いフラスコに、宇宙の色（水色→すみれ色）の光る薬が入っていて、
# 中に色あざやかな小さな惑星（環つき）が浮かぶ。力を失って淡くなった惑星に、色と力を戻す薬のイメージ。
# まわりはぼんやり光らせて、小さなきらめきを添える（金貨・星のカケラのアイコンと同じ見せ方）。背景は透明の 512×512。
#   python make_planet_potion_icon.py <書き出し先.png> [確認用.png]
import math
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

OUT = sys.argv[1]
PREVIEW = sys.argv[2] if len(sys.argv) > 2 else None

S = 512
SS = 4
W = S * SS

CX = 256 * SS
BODY_CY, BODY_R = 318 * SS, 150 * SS        # フラスコの丸い胴
NECK_HW, NECK_TOP = 46 * SS, 118 * SS       # 首（半分の幅・上端）
LIP_HW, LIP_TOP, LIP_BOTTOM = 60 * SS, 100 * SS, 128 * SS  # 口のふち
CORK_HW, CORK_TOP, CORK_BOTTOM = 40 * SS, 50 * SS, 112 * SS
GLASS_W = 9 * SS                             # ガラスの厚み
SURFACE = 252 * SS                           # 薬の水面の高さ

LIQ_TOP = np.array([120, 236, 255])   # 水色
LIQ_BOTTOM = np.array([126, 72, 232])  # すみれ色

YY, XX = np.mgrid[0:W, 0:W].astype(np.float64)


def blank():
    return Image.new("L", (W, W), 0)


def arr(m):
    return np.asarray(m, dtype=np.float64) / 255.0


class Layer:
    def __init__(self):
        self.rgb = np.zeros((W, W, 3))
        self.a = np.zeros((W, W))

    def paint(self, color, mask, alpha=1.0):
        m = np.clip(mask, 0, 1) * alpha
        col = color if isinstance(color, np.ndarray) else np.array(color, dtype=np.float64)
        out_a = m + self.a * (1 - m)
        safe = np.where(out_a > 1e-6, out_a, 1)
        self.rgb = (col * m[..., None] + self.rgb * (self.a * (1 - m))[..., None]) / safe[..., None]
        self.a = out_a

    def image(self):
        rgba = np.dstack([np.clip(self.rgb, 0, 255), np.clip(self.a * 255, 0, 255)]).astype(np.uint8)
        return Image.fromarray(rgba, "RGBA")


def lerp(c0, c1, t):
    t = np.clip(t, 0, 1)[..., None]
    return np.array(c0, dtype=np.float64) * (1 - t) + np.array(c1, dtype=np.float64) * t


# ---- フラスコの形（胴＋首＋口のふち） ----
shape = blank()
d = ImageDraw.Draw(shape)
d.ellipse([CX - BODY_R, BODY_CY - BODY_R, CX + BODY_R, BODY_CY + BODY_R], fill=255)
d.rectangle([CX - NECK_HW, NECK_TOP, CX + NECK_HW, BODY_CY - BODY_R * 0.6], fill=255)
d.rounded_rectangle([CX - LIP_HW, LIP_TOP, CX + LIP_HW, LIP_BOTTOM], radius=12 * SS, fill=255)
shape_m = arr(shape)
inner = shape.filter(ImageFilter.MinFilter(int(GLASS_W) | 1))  # ガラスの内側
inner_m = arr(inner)

# 胴の内側（薬を入れるところ）
body_in = blank()
ImageDraw.Draw(body_in).ellipse([CX - BODY_R + GLASS_W, BODY_CY - BODY_R + GLASS_W,
                                 CX + BODY_R - GLASS_W, BODY_CY + BODY_R - GLASS_W], fill=255)
body_in_m = arr(body_in)

L = Layer()

# ---- ガラスの中（うっすら青い） ----
L.paint((190, 225, 255), inner_m, 0.16)

# ---- 薬（水面から下）: 上が水色・下がすみれ色。中ほどがわずかに明るい ----
liquid_m = body_in_m * (YY >= SURFACE)
t = (YY - SURFACE) / (BODY_CY + BODY_R - SURFACE)
liq = lerp(LIQ_TOP, LIQ_BOTTOM, t)
glow_c = np.exp(-(((XX - CX) / (BODY_R * 0.75)) ** 2 + ((YY - (BODY_CY + 20 * SS)) / (BODY_R * 0.6)) ** 2))
liq = liq + glow_c[..., None] * 40
L.paint(liq, liquid_m, 0.96)
# 水面（明るい楕円の線）
surf = blank()
sw = math.sqrt(max(0.0, (BODY_R - GLASS_W) ** 2 - (SURFACE - BODY_CY) ** 2))
ImageDraw.Draw(surf).ellipse([CX - sw, SURFACE - 16 * SS, CX + sw, SURFACE + 16 * SS], outline=255, width=int(6 * SS))
L.paint((230, 255, 255), arr(surf) * body_in_m * (YY >= SURFACE - 18 * SS), 0.85)
top_face = blank()
ImageDraw.Draw(top_face).ellipse([CX - sw, SURFACE - 16 * SS, CX + sw, SURFACE + 16 * SS], fill=255)
L.paint((170, 245, 255), arr(top_face) * body_in_m, 0.55)

# ---- 薬の中の星くず ----
rnd = np.random.RandomState(7)
for _ in range(26):
    x = CX + (rnd.rand() * 2 - 1) * BODY_R * 0.8
    y = SURFACE + 20 * SS + rnd.rand() * (BODY_CY + BODY_R * 0.85 - SURFACE - 20 * SS)
    r = (1.2 + rnd.rand() * 2.4) * SS
    dot = blank()
    ImageDraw.Draw(dot).ellipse([x - r, y - r, x + r, y + r], fill=255)
    L.paint((255, 255, 255), arr(dot) * liquid_m, 0.55 + rnd.rand() * 0.4)

# ---- 浮かぶ小さな惑星（暖かい色・環つき） ----
PX, PY, PR = CX + 6 * SS, BODY_CY + 34 * SS, 50 * SS
ring_back = blank()
rb = ImageDraw.Draw(ring_back)
ring_box = [PX - PR * 1.75, PY - PR * 0.42, PX + PR * 1.75, PY + PR * 0.42]
rb.ellipse(ring_box, outline=255, width=int(7 * SS))
ring_img = ring_back.rotate(-16, center=(PX, PY), resample=Image.BICUBIC)
ring_m = arr(ring_img)
# 環の奥側（惑星の後ろ）
L.paint((255, 236, 190), ring_m * (YY < PY) * liquid_m, 0.9)
planet = blank()
ImageDraw.Draw(planet).ellipse([PX - PR, PY - PR, PX + PR, PY + PR], fill=255)
planet_m = arr(planet)
pcol = lerp((255, 214, 120), (255, 92, 120), ((YY - (PY - PR)) / (2 * PR)) * 0.8 + ((XX - (PX - PR)) / (2 * PR)) * 0.4)
L.paint(pcol, planet_m)
# 惑星の縞と光
bands = (np.sin((YY - PY) / PR * 7.0) * 0.5 + 0.5) * planet_m
L.paint((255, 245, 220), bands, 0.18)
shade = np.clip(((XX - PX) * 0.6 + (YY - PY) * 0.8) / PR, 0, 1) * planet_m
L.paint((110, 40, 120), shade, 0.42)
hl = blank()
ImageDraw.Draw(hl).ellipse([PX - PR * 0.62, PY - PR * 0.7, PX - PR * 0.05, PY - PR * 0.2], fill=255)
L.paint((255, 255, 240), arr(hl.filter(ImageFilter.GaussianBlur(5 * SS))) * planet_m, 0.6)
# 環の手前側
L.paint((255, 242, 205), ring_m * (YY >= PY) * liquid_m, 0.95)

# ---- 泡 ----
for bx, by, br in ((-62, -36, 9), (-30, -70, 6), (40, -54, 7), (70, -20, 5)):
    x, y, r = CX + bx * SS, BODY_CY + by * SS, br * SS
    bub = blank()
    ImageDraw.Draw(bub).ellipse([x - r, y - r, x + r, y + r], outline=255, width=int(2.2 * SS))
    L.paint((240, 255, 255), arr(bub) * liquid_m, 0.85)

# ---- ガラスの縁・つや ----
edge = np.clip(shape_m - inner_m, 0, 1)
L.paint((222, 240, 255), edge, 0.88)
outline = np.clip(arr(shape.filter(ImageFilter.MaxFilter(7))) - shape_m, 0, 1)
L.paint((60, 70, 130), outline, 0.75)
# 胴の左上の弧の光
arc = blank()
ad = ImageDraw.Draw(arc)
ad.arc([CX - BODY_R + 26 * SS, BODY_CY - BODY_R + 26 * SS, CX + BODY_R - 26 * SS, BODY_CY + BODY_R - 26 * SS],
       start=196, end=258, fill=255, width=int(14 * SS))
L.paint((255, 255, 255), arr(arc.filter(ImageFilter.GaussianBlur(2 * SS))), 0.7)
dot = blank()
ImageDraw.Draw(dot).ellipse([CX - 92 * SS, BODY_CY - 36 * SS, CX - 80 * SS, BODY_CY - 24 * SS], fill=255)
L.paint((255, 255, 255), arr(dot), 0.8)
# 首の縦の光
neck_hl = blank()
ImageDraw.Draw(neck_hl).rounded_rectangle([CX - NECK_HW + 14 * SS, NECK_TOP + 18 * SS, CX - NECK_HW + 26 * SS,
                                           BODY_CY - BODY_R + 30 * SS], radius=6 * SS, fill=255)
L.paint((255, 255, 255), arr(neck_hl), 0.55)

# ---- コルクの栓 ----
cork = blank()
ImageDraw.Draw(cork).rounded_rectangle([CX - CORK_HW, CORK_TOP, CX + CORK_HW, CORK_BOTTOM], radius=14 * SS, fill=255)
cork_m = arr(cork)
cork_col = lerp((214, 160, 104), (150, 96, 54), (XX - (CX - CORK_HW)) / (2 * CORK_HW))
L.paint(cork_col, cork_m)
for k in range(4):
    yy = CORK_TOP + (14 + k * 13) * SS
    line = blank()
    ImageDraw.Draw(line).line([CX - CORK_HW + 8 * SS, yy, CX + CORK_HW - 8 * SS, yy], fill=255, width=int(2.4 * SS))
    L.paint((120, 74, 40), arr(line) * cork_m, 0.45)
cork_out = np.clip(arr(cork.filter(ImageFilter.MaxFilter(7))) - cork_m, 0, 1)
L.paint((90, 54, 26), cork_out, 0.8)
cork_hl = blank()
ImageDraw.Draw(cork_hl).rounded_rectangle([CX - CORK_HW + 8 * SS, CORK_TOP + 6 * SS, CX - CORK_HW + 18 * SS,
                                           CORK_BOTTOM - 8 * SS], radius=4 * SS, fill=255)
L.paint((255, 226, 180), arr(cork_hl), 0.55)

flask = L.image().resize((S, S), Image.LANCZOS)

# ---- 外の光ときらめき ----
glow_mask = flask.split()[3].filter(ImageFilter.GaussianBlur(24))
glow = Image.new("RGBA", (S, S), (120, 210, 255, 0))
glow.putalpha(glow_mask.point(lambda v: int(v * 0.75)))
icon = Image.new("RGBA", (S, S), (0, 0, 0, 0))
icon.alpha_composite(glow)
icon.alpha_composite(flask)


def sparkle(dr, x, y, s, col):
    dr.polygon([(x, y - s * 1.9), (x + s * 0.3, y), (x, y + s * 1.9), (x - s * 0.3, y)], fill=col)
    dr.polygon([(x - s * 1.9, y), (x, y + s * 0.3), (x + s * 1.9, y), (x, y - s * 0.3)], fill=col)


dr = ImageDraw.Draw(icon)
sparkle(dr, S * 0.83, S * 0.22, 13, (235, 250, 255, 245))
sparkle(dr, S * 0.15, S * 0.40, 8, (255, 240, 250, 230))
sparkle(dr, S * 0.86, S * 0.80, 6, (230, 240, 255, 220))
icon.save(OUT)
print("書き出し:", OUT)

if PREVIEW:
    bg = Image.new("RGBA", (S + 360, S), (13, 18, 48, 255))
    bg.alpha_composite(icon, (0, 0))
    x = S + 20
    for size, yy in ((148, 20), (128, 200), (96, 360)):
        bg.alpha_composite(icon.resize((size, size), Image.LANCZOS), (x, yy))
    card = Image.new("RGBA", (180, 170), (9, 38, 24, 255))
    card.alpha_composite(icon.resize((148, 148), Image.LANCZOS), (16, 11))
    bg.alpha_composite(card, (S + 175, 20))
    bg.convert("RGB").save(PREVIEW)
