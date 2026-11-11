# INTALES WORLD の世界地図（オリジナル。手続き生成）を PNG に書き出す
# （2026-11-10。2026-11-11 に5か所目、2026-11-12 に6か所目を追加）
#   python make_world_map.py <書き出し先.png> [確認用の縮小版.png]
# 置き場（SpaceWorld.Areas の Pos と同じ割合の位置）には、必ずそれらしい陸を作る:
#   0 星見の丘（北の雪山）/ 1 惑星の神殿（東の小島）/ 2 コマの工房（西の平原と森）/ 3 商人の港（南東の入り江）
#   4 星くずの島（南西の小島。まわりの海に星くずがきらめく。何色のコマでも置ける保険の置き場）
#   5 黄金の鉱山（地図の右上の北東の小島。島の上に金のきらめき。3G をもらう。2026-11-12。
#     最初は南東の大陸の山並みに置いたが、同日ユーザー指定で右上へ移した）
#     前の版の地図と木の並びが変わらないよう、木の乱数は前の5か所で判定してから、鉱山のまわりだけ描かない
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

OUT = sys.argv[1]
PREVIEW = sys.argv[2] if len(sys.argv) > 2 else None
W, H = 1080, 1440
SEED = 20261110
rng = np.random.default_rng(SEED)

# 置き場の位置（横・縦の割合。SpaceWorld.cs の Areas と同じ値にする）
SPOTS = [(0.30, 0.21), (0.75, 0.37), (0.27, 0.60), (0.70, 0.80), (0.125, 0.855), (0.80, 0.145)]
BASE_SPOTS = SPOTS[:5]  # 2026-11-11 までの5か所（木の乱数の並びをそろえるのに使う）


def noise(scale, octaves=5, persistence=0.5):
    """なめらかな値ノイズ（粗い乱数の格子を拡大して重ねる）。-1〜1"""
    total = np.zeros((H, W))
    amp, norm = 1.0, 0.0
    for o in range(octaves):
        gw = max(2, int(W / scale * (2 ** o)))
        gh = max(2, int(H / scale * (2 ** o)))
        grid = rng.random((gh, gw)).astype(np.float32)
        img = Image.fromarray((grid * 255).astype(np.uint8), "L").resize((W, H), Image.BICUBIC)
        total += (np.asarray(img, dtype=np.float64) / 255.0 * 2 - 1) * amp
        norm += amp
        amp *= persistence
    return total / norm


yy, xx = np.mgrid[0:H, 0:W]
u, v = xx / W, yy / H


def bump(cx, cy, r, aspect=1.0):
    d2 = ((u - cx) / r) ** 2 + ((v - cy) / (r * aspect)) ** 2
    return np.exp(-d2)


# ---- 陸と海の形（海岸線を決める値。0 より上が陸） ----
shape = noise(380, 6) * 0.38 + noise(120, 4) * 0.16
# 西の大陸（星見の丘・コマの工房）・南東の大陸（商人の港）・北東の小さな陸
shape += bump(0.31, 0.42, 0.20, 2.0) * 0.86
shape += bump(0.66, 0.76, 0.19, 1.15) * 0.80
shape += bump(0.80, 0.15, 0.13, 0.75) * 0.95
shape += bump(0.90, 0.52, 0.05, 1.4) * 0.70  # 東の細長い島
shape += bump(*SPOTS[4], 0.045) * 0.62  # 南西の小島（星くずの島。2026-11-11 に置き場にするため少し大きく・上へ）
shape -= bump(0.165, 0.80, 0.04) * 0.45  # 星くずの島と大陸の間の海峡
edge = np.minimum.reduce([u, 1 - u, v, 1 - v])
shape -= np.clip(0.09 - edge, 0, None) * 8.0
shape -= 0.46
# 惑星の神殿の小島（まわりを海にしてから島を置く）
shape -= bump(*SPOTS[1], 0.15) * 0.55
shape += bump(*SPOTS[1], 0.068, 0.9) * 1.15
# 置き場は必ず陸に
for i, rr in ((0, 0.07), (2, 0.08)):
    shape = np.where(bump(*SPOTS[i], rr) > 0.45, np.maximum(shape, 0.12), shape)
# 商人の港: 港の南東を入り江にして、港は海ぎわの陸に
shape -= bump(SPOTS[3][0] + 0.085, SPOTS[3][1] + 0.01, 0.06, 1.3) * 0.95
shape = np.where(bump(*SPOTS[3], 0.03) > 0.5, np.maximum(shape, 0.05), shape)
# 星くずの島: 置き場は必ず陸に
shape = np.where(bump(*SPOTS[4], 0.03) > 0.5, np.maximum(shape, 0.06), shape)

land = shape > 0
landf = land.astype(np.float64)

# ---- 高さ（色と陰影。山は星見の丘のまわりと、東の小さな山並みだけ） ----
h = np.clip(shape, 0, None) * 0.55
h += bump(*SPOTS[0], 0.085, 1.1) * 0.80
h += bump(SPOTS[0][0] + 0.10, SPOTS[0][1] + 0.07, 0.07, 1.6) * 0.45
h += bump(0.62, 0.66, 0.05, 2.2) * 0.42
h += noise(70, 3) * 0.04
h = np.where(bump(*SPOTS[2], 0.10) > 0.3, np.minimum(h, 0.18), h)  # 工房のあたりは平原
h = h * landf + shape * (1 - landf)

# ---- 海 ----
ocean_deep = np.array([12, 34, 74], dtype=np.float64)
ocean_shallow = np.array([34, 92, 138], dtype=np.float64)
L = Image.fromarray((landf * 255).astype(np.uint8), "L")
near = np.asarray(L.filter(ImageFilter.GaussianBlur(26)), dtype=np.float64) / 255.0
near2 = np.asarray(L.filter(ImageFilter.GaussianBlur(70)), dtype=np.float64) / 255.0
t = np.clip(near * 1.6 + near2 * 0.6, 0, 1)
img = ocean_deep * (1 - t[..., None]) + ocean_shallow * t[..., None]
# 海岸のまわりの波紋（昔の地図の等深線）
ripple = np.zeros((H, W))
for lv in (0.10, 0.22, 0.36):
    ripple += np.exp(-((near - lv) / 0.012) ** 2)
img += (ripple * (1 - landf))[..., None] * np.array([60, 110, 150]) * 0.35
# 海のさざ波（細かい模様）
wave = (np.sin((xx * 0.9 + noise(60, 2) * 40) * 0.12) * np.sin((yy + noise(80, 2) * 30) * 0.05)) * 0.5 + 0.5
patch = noise(200, 3) > 0.05
img += ((wave > 0.97) * patch * (1 - landf) * (1 - t))[..., None] * np.array([40, 70, 100]) * 0.45

# ---- 陸 ----
forest = noise(90, 4) > 0.08
hl = np.clip(h, 0, None)
sand = np.array([226, 210, 160])
grass = np.array([118, 170, 98])
wood = np.array([66, 122, 78])
high = np.array([150, 140, 104])
rock = np.array([128, 126, 132])
snow = np.array([246, 248, 252])


def mix(a, b, k):
    k = np.clip(k, 0, 1)[..., None]
    return a * (1 - k) + b * k


c = mix(sand, grass, (hl - 0.015) / 0.04)
c = np.where((forest & (hl > 0.06) & (hl < 0.42))[..., None], mix(c, wood, (hl - 0.06) / 0.05), c)
c = mix(c, high, (hl - 0.38) / 0.12)
c = mix(c, rock, (hl - 0.52) / 0.12)
c = mix(c, snow, (hl - 0.70) / 0.08)

# 陰影（北西から光）
gy, gx = np.gradient(h * 900)
nx, ny, nz = -gx, -gy, np.ones_like(h) * 4.0
nrm = np.sqrt(nx ** 2 + ny ** 2 + nz ** 2)
lx, ly, lz = -0.6, -0.6, 0.55
shade = (nx * lx + ny * ly + nz * lz) / nrm
shade = np.clip(0.55 + shade * 0.75, 0.35, 1.25)
c = c * shade[..., None]
img = np.where(land[..., None], c, img)

# 海岸の線（明るい縁）
coast = np.asarray(L.filter(ImageFilter.FIND_EDGES).filter(ImageFilter.GaussianBlur(1.2)), dtype=np.float64) / 255.0
img = img + coast[..., None] * np.array([255, 245, 210]) * 0.55

# ---- 経緯線 ----
grid = np.zeros((H, W))
for gxp in np.linspace(0, W, 9)[1:-1]:
    grid += np.exp(-((xx - gxp) / 1.1) ** 2)
for gyp in np.linspace(0, H, 12)[1:-1]:
    grid += np.exp(-((yy - gyp) / 1.1) ** 2)
img = img + grid[..., None] * np.array([150, 210, 255]) * 0.10

img = np.clip(img, 0, 255).astype(np.uint8)
pic = Image.fromarray(img, "RGB").convert("RGBA")
d = ImageDraw.Draw(pic)

# ---- 山と木の記号 ----
def jitter_points(step):
    pts = []
    for gy0 in range(0, H, step):
        for gx0 in range(0, W, step):
            pts.append((gx0 + rng.uniform(0, step), gy0 + rng.uniform(0, step)))
    return pts


def near_spot(x, y, r=70, spots=None):
    return any(((x - sx * W) ** 2 + (y - sy * H) ** 2) < r * r for sx, sy in (spots or SPOTS))


for x, y in jitter_points(34):
    xi, yi = int(x), int(y)
    if xi >= W or yi >= H or near_spot(x, y):
        continue
    e = hl[yi, xi]
    if e > 0.5:
        s = 13 + (e - 0.5) * 30
        d.polygon([(x - s, y + s * 0.55), (x, y - s * 0.9), (x + s, y + s * 0.55)], fill=(92, 88, 96, 230))
        d.polygon([(x, y - s * 0.9), (x + s, y + s * 0.55), (x + s * 0.15, y + s * 0.55)], fill=(60, 56, 66, 230))
        if e > 0.66:
            d.polygon([(x - s * 0.32, y - s * 0.32), (x, y - s * 0.9), (x + s * 0.32, y - s * 0.32)],
                      fill=(250, 250, 255, 240))
for x, y in jitter_points(22):
    xi, yi = int(x), int(y)
    if xi >= W or yi >= H or near_spot(x, y, 56, BASE_SPOTS):
        continue
    if land[yi, xi] and forest[yi, xi] and 0.07 < hl[yi, xi] < 0.40 and rng.random() < 0.8:
        if near_spot(x, y, 56, SPOTS[5:]):
            rng.uniform(5, 8)  # 黄金の鉱山のまわりは描かない（乱数は描いたときと同じだけ使い、ほかの木の並びを変えない）
            continue
        r0 = rng.uniform(5, 8)
        d.ellipse((x - r0, y - r0 + 2, x + r0, y + r0 + 2), fill=(28, 60, 36, 150))
        d.ellipse((x - r0, y - r0, x + r0, y + r0), fill=(48, 104, 62, 255))
        d.ellipse((x - r0 * 0.5, y - r0 * 0.7, x + r0 * 0.1, y - r0 * 0.1), fill=(96, 160, 104, 200))

# ---- 星くずの島のまわりの海に、小さな星くず（4本の光のきらめき。別の乱数で描く） ----
srng = np.random.default_rng(SEED + 4)
scx, scy = SPOTS[4][0] * W, SPOTS[4][1] * H
for i in range(16):
    ang = srng.uniform(0, np.pi * 2)
    rr = srng.uniform(78, 150)
    x, y = scx + np.cos(ang) * rr, scy + np.sin(ang) * rr * 0.85
    if land[min(H - 1, int(y)), min(W - 1, int(x))]:
        continue
    s0 = srng.uniform(4, 9)
    col = (255, 244, 200, 235) if srng.random() < 0.6 else (210, 230, 255, 225)
    d.polygon([(x, y - s0 * 1.8), (x + s0 * 0.32, y), (x, y + s0 * 1.8), (x - s0 * 0.32, y)], fill=col)
    d.polygon([(x - s0 * 1.8, y), (x, y + s0 * 0.32), (x + s0 * 1.8, y), (x, y - s0 * 0.32)], fill=col)
    d.ellipse((x - 1.6, y - 1.6, x + 1.6, y + 1.6), fill=(255, 255, 255, 255))
for i in range(40):
    ang = srng.uniform(0, np.pi * 2)
    rr = srng.uniform(66, 170)
    x, y = scx + np.cos(ang) * rr, scy + np.sin(ang) * rr * 0.85
    if land[min(H - 1, int(y)), min(W - 1, int(x))]:
        continue
    r0 = srng.uniform(0.8, 2.0)
    d.ellipse((x - r0, y - r0, x + r0, y + r0), fill=(255, 246, 214, int(srng.uniform(140, 230))))

# ---- 黄金の鉱山の島に、金のきらめきと金の粒（2026-11-12。別の乱数で描く。陸の上だけ。
#      島は小さく、まん中は置き場の丸に隠れるので、丸のすぐ外の島のふちに散らす） ----
grng = np.random.default_rng(SEED + 5)
gcx, gcy = SPOTS[5][0] * W, SPOTS[5][1] * H
for i in range(22):
    ang = grng.uniform(0, np.pi * 2)
    rr = grng.uniform(60, 96)
    x, y = gcx + np.cos(ang) * rr, gcy + np.sin(ang) * rr * 0.9
    if not land[min(H - 1, int(y)), min(W - 1, int(x))]:
        continue
    s0 = grng.uniform(3.5, 7.5)
    col = (255, 218, 96, 240) if grng.random() < 0.7 else (255, 244, 200, 235)
    d.polygon([(x, y - s0 * 1.8), (x + s0 * 0.32, y), (x, y + s0 * 1.8), (x - s0 * 0.32, y)], fill=col)
    d.polygon([(x - s0 * 1.8, y), (x, y + s0 * 0.32), (x + s0 * 1.8, y), (x, y - s0 * 0.32)], fill=col)
    d.ellipse((x - 1.6, y - 1.6, x + 1.6, y + 1.6), fill=(255, 255, 240, 255))
for i in range(34):
    ang = grng.uniform(0, np.pi * 2)
    rr = grng.uniform(58, 98)
    x, y = gcx + np.cos(ang) * rr, gcy + np.sin(ang) * rr * 0.9
    if not land[min(H - 1, int(y)), min(W - 1, int(x))]:
        continue
    r0 = grng.uniform(1.6, 3.2)
    d.ellipse((x - r0, y - r0 + 1, x + r0, y + r0 + 1), fill=(120, 80, 20, 150))   # 影
    d.ellipse((x - r0, y - r0, x + r0, y + r0), fill=(240, 186, 60, 255))           # 金の粒
    d.ellipse((x - r0 * 0.5, y - r0 * 0.6, x, y - r0 * 0.1), fill=(255, 246, 200, 230))  # 光

# ---- 方位記号（右下の海） ----
cx, cy, R = W * 0.88, H * 0.92, 58
for ang, ln, col in ((0, 1.0, (250, 236, 200)), (90, 0.62, (200, 214, 236)), (180, 0.62, (200, 214, 236)),
                     (270, 0.62, (200, 214, 236))):
    a = np.deg2rad(ang - 90)
    tip = (cx + np.cos(a) * R * ln, cy + np.sin(a) * R * ln)
    side = np.deg2rad(ang)
    l1 = (cx + np.cos(side) * 11, cy + np.sin(side) * 11)
    l2 = (cx - np.cos(side) * 11, cy - np.sin(side) * 11)
    d.polygon([tip, l1, (cx, cy)], fill=col + (235,))
    d.polygon([tip, l2, (cx, cy)], fill=tuple(int(x * 0.7) for x in col) + (235,))
d.ellipse((cx - R * 0.78, cy - R * 0.78, cx + R * 0.78, cy + R * 0.78), outline=(220, 230, 250, 140), width=2)
d.text((cx - 5, cy - R - 26), "N", fill=(250, 236, 200, 255))

# ---- 縁（暗く落とす） ----
vig = np.ones((H, W))
k = np.clip(edge / 0.06, 0, 1)
vig = 0.55 + 0.45 * k
a = np.asarray(pic, dtype=np.float64)
a[..., :3] *= vig[..., None]
pic = Image.fromarray(np.clip(a, 0, 255).astype(np.uint8), "RGBA")
d = ImageDraw.Draw(pic)
d.rectangle((6, 6, W - 7, H - 7), outline=(200, 220, 255, 90), width=2)

pic.convert("RGB").save(OUT)
print("書き出し:", OUT, pic.size)
if PREVIEW:
    prev = pic.convert("RGB").copy()
    pd = ImageDraw.Draw(prev)
    for i, (sx, sy) in enumerate(SPOTS):
        pd.ellipse((sx * W - 40, sy * H - 40, sx * W + 40, sy * H + 40), outline=(255, 220, 80), width=5)
        pd.text((sx * W - 4, sy * H - 6), str(i), fill=(255, 255, 255))
    prev.resize((W // 2, H // 2), Image.LANCZOS).save(PREVIEW)
    print("確認用:", PREVIEW)
