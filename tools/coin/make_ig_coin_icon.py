# アプリ内の通貨（単位 G。2026-11-12）のアイコン。金色の硬貨を正面から少し見下ろした形で、ふちの厚み（ギザギザ）が下に見える。
# 硬貨の面は、浮き出た「G」の文字・その上の小さな星・まわりの粒のふち。外側はぼんやり光らせて、小さなきらめきを添える
# （星のカケラ make_star_fragment_icon.py と同じ見せ方）。背景は透明の 512×512。
# 単位は最初「IG」だったが、I が 1 と見間違えやすいので同日ユーザー指定で「G」に変えた（ファイル名は ig のまま）
#   python make_ig_coin_icon.py <書き出し先.png> [確認用.png]
# 確認用は、暗い宇宙の色の上に 512・128・96・64 の大きさで並べた絵（所持品の行は 128、お知らせの枠は 96 前後で出る）
import math
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

OUT = sys.argv[1]
PREVIEW = sys.argv[2] if len(sys.argv) > 2 else None

S = 512
SS = 4  # 4倍の大きさで描いてから縮める（ふちをなめらかに）
W = S * SS
FONT = r"C:\Windows\Fonts\georgiab.ttf"  # 硬貨らしい、うろこ（セリフ）のある太字

CX, CY = 256 * SS, 242 * SS  # 面の中心（下にふちの厚みが見えるので少し上）
R = 194 * SS                 # 面の半径
THICK = 18 * SS              # ふちの厚み（下に見える分）
FIELD = 0.83                 # 内側の面（ふちより一段低い）の半径の割合
BEADS_AT = 0.755             # 粒のふちの半径の割合


def u(v):
    return v * SS


def blank_l():
    return Image.new("L", (W, W), 0)


def circle(cx, cy, r):
    m = blank_l()
    ImageDraw.Draw(m).ellipse([cx - r, cy - r, cx + r, cy + r], fill=255)
    return m


def arr(m):
    return np.asarray(m, dtype=np.float64) / 255.0


def shift(m, dx, dy):
    """L の絵を (dx, dy) ずらす（空いたところは 0）"""
    out = blank_l()
    out.paste(m, (int(round(dx)), int(round(dy))))
    return out


YY, XX = np.mgrid[0:W, 0:W].astype(np.float64)


def lerp_color(c0, c1, t):
    t = np.clip(t, 0, 1)[..., None]
    return np.array(c0, dtype=np.float64) * (1 - t) + np.array(c1, dtype=np.float64) * t


def linear(c0, c1, angle_deg, x0, y0, half):
    """角度 angle_deg（0=右・90=下）の向きに、中心 (x0,y0) から ±half で c0→c1 に変わる色"""
    a = math.radians(angle_deg)
    t = ((XX - x0) * math.cos(a) + (YY - y0) * math.sin(a)) / (2 * half) + 0.5
    return lerp_color(c0, c1, t)


def radial(c0, c1, x0, y0, radius):
    t = np.sqrt((XX - x0) ** 2 + (YY - y0) ** 2) / radius
    return lerp_color(c0, c1, t)


class Layer:
    """RGBA の浮動小数の絵（重ねていく）"""

    def __init__(self):
        self.rgb = np.zeros((W, W, 3))
        self.a = np.zeros((W, W))

    def paint(self, color, mask, alpha=1.0):
        """color は (H,W,3) か (r,g,b)。mask は 0〜1 の (H,W)"""
        m = np.clip(mask, 0, 1) * alpha
        col = color if isinstance(color, np.ndarray) else np.array(color, dtype=np.float64)
        out_a = m + self.a * (1 - m)
        safe = np.where(out_a > 1e-6, out_a, 1)
        self.rgb = (col * m[..., None] + self.rgb * (self.a * (1 - m))[..., None]) / safe[..., None]
        self.a = out_a

    def image(self):
        rgba = np.dstack([np.clip(self.rgb, 0, 255), np.clip(self.a * 255, 0, 255)]).astype(np.uint8)
        return Image.fromarray(rgba, "RGBA")


def star_points(cx, cy, r_out, r_in, n=5, rot=-90.0):
    pts = []
    for i in range(n * 2):
        r = r_out if i % 2 == 0 else r_in
        a = math.radians(rot + i * 180.0 / n)
        pts.append((cx + math.cos(a) * r, cy + math.sin(a) * r))
    return pts


def emboss(layer, mask_img, face_color, clip, light=(255, 253, 232), dark=(150, 88, 14), depth=u(2.2), drop=u(4.5)):
    """mask_img（L）の形を、面から浮き出た金の模様として描く（落ち影・左上の光・右下の影・輪郭）。clip は落ち影を収める面"""
    m = arr(mask_img)
    # 落ち影（右下へ。ぼかして面に落とす）
    sh = arr(shift(mask_img, drop * 0.8, drop).filter(ImageFilter.GaussianBlur(u(2.2))))
    layer.paint((112, 60, 6), sh * (1 - m) * clip, 0.68)
    # 本体
    layer.paint(face_color, m)
    # 左上の縁の光・右下の縁の影（形を少しずらして引いた残り＝縁の帯）
    hi = np.clip(m - arr(shift(mask_img, depth, depth)), 0, 1)
    lo = np.clip(m - arr(shift(mask_img, -depth, -depth)), 0, 1)
    layer.paint(light, hi, 0.95)
    layer.paint(dark, lo, 0.8)
    # 輪郭（金の地に金の文字なので、小さく出したときも形が読めるよう濃いめに）
    grown = arr(mask_img.filter(ImageFilter.MaxFilter(7)))
    layer.paint((118, 64, 6), np.clip(grown - m, 0, 1), 0.85)


# ================================================================
# 硬貨
# ================================================================
coin = Layer()
face_img = circle(CX, CY, R)
face = arr(face_img)
side_img = circle(CX, CY + THICK, R)
side = arr(side_img)

# ---- ふちの厚み（下に見える三日月の帯）: 暗い金のグラデーション＋ギザギザ ----
side_col = linear((214, 146, 42), (128, 72, 12), 90, CX, CY + THICK, R)
coin.paint(side_col, side)
band = np.clip(side - face, 0, 1)
reed = blank_l()
dr = ImageDraw.Draw(reed)
step = u(7.5)
x = CX - R
while x < CX + R:
    dr.line([(x, CY), (x, CY + R + THICK + u(4))], fill=255, width=int(u(2.4)))
    x += step
reed_m = arr(reed) * band
coin.paint((96, 52, 6), reed_m, 0.45)
# 厚みの下の端を少し暗く（丸みのかげ）
coin.paint((90, 48, 6), band * np.clip((YY - (CY + R * 0.55)) / (R * 0.5), 0, 1), 0.35)

# ---- 面（ふち）: 左上が明るく右下が濃い金 ----
rim_col = linear((255, 241, 176), (210, 140, 30), 45, CX, CY, R * 0.95)
coin.paint(rim_col, face)

# ---- 内側の面（ふちより一段低い） ----
field_img = circle(CX, CY, R * FIELD)
field_mask = arr(field_img)
field_col = radial((255, 232, 140), (222, 154, 40), CX - R * 0.32, CY - R * 0.38, R * 1.25)
coin.paint(field_col, field_mask)
# 一段低いので、左上の内側に影・右下の内側に光
in_shadow = np.clip(field_mask - arr(shift(field_img, u(5), u(5))), 0, 1)
in_light = np.clip(field_mask - arr(shift(field_img, -u(3.5), -u(3.5))), 0, 1)
coin.paint((150, 90, 16), np.asarray(Image.fromarray((in_shadow * 255).astype(np.uint8)).filter(
    ImageFilter.GaussianBlur(u(1.2))), dtype=np.float64) / 255 * field_mask, 0.7)
coin.paint((255, 250, 214), in_light, 0.85)

# ---- 粒のふち ----
N_BEADS = 30
bead_r = R * 0.034
for i in range(N_BEADS):
    a = math.radians(-90 + i * 360.0 / N_BEADS)
    bx, by = CX + math.cos(a) * R * BEADS_AT, CY + math.sin(a) * R * BEADS_AT
    sh = arr(circle(bx + u(1.6), by + u(2.0), bead_r))
    coin.paint((140, 80, 10), sh, 0.55)
    body = arr(circle(bx, by, bead_r))
    coin.paint(radial((255, 246, 196), (214, 146, 36), bx - bead_r * 0.4, by - bead_r * 0.45, bead_r * 1.6), body)

# ---- 「G」の文字（浮き出し） ----
font = ImageFont.truetype(FONT, int(R * 1.0))
text = "G"
probe = ImageDraw.Draw(blank_l())
l, t, r_, b = probe.textbbox((0, 0), text, font=font)
tw, th = r_ - l, b - t
txt = blank_l()
ty = CY + R * 0.10  # 星を上に置くので文字は少し下
ImageDraw.Draw(txt).text((CX - tw / 2 - l, ty - th / 2 - t), text, font=font, fill=255)
text_col = linear((255, 251, 222), (240, 182, 66), 90, CX, ty, th * 0.6)
emboss(coin, txt, text_col, field_mask)

# ---- 文字の上の小さな星（宇宙のしるし） ----
star = blank_l()
sy = CY - R * 0.47
ImageDraw.Draw(star).polygon(star_points(CX, sy, R * 0.115, R * 0.048), fill=255)
emboss(coin, star, linear((255, 251, 222), (240, 182, 66), 90, CX, sy, R * 0.12), field_mask,
       depth=u(1.6), drop=u(3))

# ---- つや: 左上にうすい光・ふちの左上に細い光の線 ----
gloss = blank_l()
ImageDraw.Draw(gloss).ellipse([CX - R * 0.95, CY - R * 0.92, CX + R * 0.15, CY - R * 0.22], fill=255)
gloss = gloss.filter(ImageFilter.GaussianBlur(u(16)))
coin.paint((255, 255, 240), arr(gloss) * face, 0.22)
edge_hi = np.clip(face - arr(shift(face_img, u(3), u(3))), 0, 1)
coin.paint((255, 252, 230), edge_hi, 0.9)
# 外の輪郭（暗い金の細い線）
outline = np.clip(arr(face_img.filter(ImageFilter.MaxFilter(9))) - face, 0, 1)
outline = np.maximum(outline, np.clip(arr(side_img.filter(ImageFilter.MaxFilter(9))) - side, 0, 1))
coin.paint((120, 66, 8), outline * (1 - np.maximum(face, side)), 0.85)

coin_img = coin.image().resize((S, S), Image.LANCZOS)

# ================================================================
# 光ときらめき
# ================================================================
glow_mask = coin_img.split()[3].filter(ImageFilter.GaussianBlur(22))
glow = Image.new("RGBA", (S, S), (255, 210, 110, 0))
glow.putalpha(glow_mask.point(lambda v: int(v * 0.8)))
icon = Image.new("RGBA", (S, S), (0, 0, 0, 0))
icon.alpha_composite(glow)
icon.alpha_composite(coin_img)


def sparkle(d, x, y, s, col):
    d.polygon([(x, y - s * 1.9), (x + s * 0.3, y), (x, y + s * 1.9), (x - s * 0.3, y)], fill=col)
    d.polygon([(x - s * 1.9, y), (x, y + s * 0.3), (x + s * 1.9, y), (x, y - s * 0.3)], fill=col)


d = ImageDraw.Draw(icon)
sparkle(d, S * 0.865, S * 0.17, 13, (255, 250, 222, 245))
sparkle(d, S * 0.12, S * 0.78, 8, (255, 244, 205, 230))
sparkle(d, S * 0.15, S * 0.15, 5, (230, 240, 255, 220))
icon.save(OUT)
print("書き出し:", OUT)

if PREVIEW:
    # 確認用: アプリのシートの色の上に、512 と、実際に出る大きさ（128・96・64）を並べる
    bg = Image.new("RGBA", (S + 360, S), (13, 18, 48, 255))
    bg.alpha_composite(icon, (0, 0))
    x = S + 20
    for size, yy in ((128, 30), (96, 200), (64, 340)):
        bg.alpha_composite(icon.resize((size, size), Image.LANCZOS), (x, yy))
    # 行の色（ガラスのカード）の上に 128
    card = Image.new("RGBA", (180, 150), (34, 44, 92, 255))
    card.alpha_composite(icon.resize((128, 128), Image.LANCZOS), (26, 11))
    bg.alpha_composite(card, (S + 170, 30))
    bg.convert("RGB").save(PREVIEW)
