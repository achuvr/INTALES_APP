# 黒コマのアイコン（2026-11-12。切り札座の完成でショップに並ぶ「何色にでもなるコマ」。背景透過の 512×512 PNG）を書き出す。
# 形・光と影の付け方は meeple_icon.py（赤・青・黄のコマ）と同じ。つやのある黒い体のふちに、赤→黄→青と移り変わる光の帯を
# ぐるりと入れて「どの色にもなれる」ことを表す。胸に小さなきらめき
#   python meeple_black_icon.py <meeple.py のあるフォルダ> <書き出し先の png> [確認用の絵]
import io
import math
import os
import re
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

HERE = sys.argv[1]
OUT = sys.argv[2]
PREVIEW = sys.argv[3] if len(sys.argv) > 3 else None

SIZE = 512
SS = 4
N = SIZE * SS
BASE = (40, 40, 52)  # 黒（少し青みのある黒。真っ黒だと暗い背景に沈むため）
RIM = [(255, 86, 86), (255, 214, 64), (72, 140, 255)]  # 赤・黄・青（PlanetColors に近い明るめの色）

src = io.open(os.path.join(HERE, "meeple.py"), encoding="utf-8").read()
block = src[src.index("RIGHT = ["):src.index("SMOOTH =")]
smooth_line = re.search(r"^SMOOTH = .*$", src, re.M).group(0)
box_line = re.search(r"^x0, x1, y0, y1 = .*$", src, re.M).group(0)
ns = {}
exec(block + "\n" + smooth_line + "\n" + box_line, ns)
BODY, HEAD_C, HEAD_R, SMOOTH = ns["BODY"], ns["HEAD_C"], ns["HEAD_R"], ns["SMOOTH"]
x0, x1, y0, y1 = ns["x0"], ns["x1"], ns["y0"], ns["y1"]

# 形の置き方は meeple_icon.py と同じ（ほかの色のコマと並べたときに大きさがそろう）
scale = N * 0.84 / max(x1 - x0, y1 - y0)
ox = N / 2 - (x0 + x1) / 2 * scale
oy = N * 0.47 - (y0 + y1) / 2 * scale
P = lambda x, y: (ox + x * scale, oy + y * scale)

mask = Image.new("L", (N, N), 0)
d = ImageDraw.Draw(mask)
d.polygon([P(x, y) for x, y in BODY], fill=255)
cx, cy = P(*HEAD_C)
r = HEAD_R * scale
d.ellipse((cx - r, cy - r, cx + r, cy + r), fill=255)
mask = mask.filter(ImageFilter.GaussianBlur(SMOOTH * scale))
inside = (np.asarray(mask) >= 128).astype(np.float64)
B = Image.fromarray((inside * 255).astype(np.uint8), "L")

# 盛り上がりと光（meeple_icon.py と同じ計算）
bevel = N * 0.05
H = np.asarray(B.filter(ImageFilter.GaussianBlur(bevel)), dtype=np.float64) / 255.0
t = np.clip(H / 0.8, 0.0, 1.0)
H = (1.0 - (1.0 - t) ** 2) * inside
gy, gx = np.gradient(H)
k = 1.0 / (bevel * 0.9)
nx, ny, nz = -gx / k * 5, -gy / k * 5, np.ones_like(H)
norm = np.sqrt(nx * nx + ny * ny + nz * nz)
nx, ny, nz = nx / norm, ny / norm, nz / norm
L = np.array([-0.55, -0.65, 0.52])
L = L / np.linalg.norm(L)
lit = nx * L[0] + ny * L[1] + nz * L[2]
flat = L[2]
f = 1.0 + 1.1 * (lit - flat)
yy = np.arange(N, dtype=np.float64)[:, None] / N
f = f * (1.07 - 0.14 * yy)
rz = 2 * lit * nz - L[2]
spec = np.clip(rz, 0, 1) ** 24 * 0.32 * (1 - nz)
spec = np.clip(spec * 3, 0, 0.45)

# 体（黒）: 明るいところは白へ、暗いところは黒のまま。黒は光が乗りにくいので、つやを少し強める
base = np.array(BASE, dtype=np.float64)[None, None, :]
ff = f[..., None]
light = base + (255 - base) * np.clip(ff - 1, 0, 1) * 0.75
dark = base * np.clip(ff, 0.55, 1)
rgb = np.where(ff >= 1, light, dark)
rgb = rgb + (255 - rgb) * (spec * 1.4)[..., None]

# ふちの光の帯: 形の内側のふちに沿って、中心のまわりの角度で 赤→黄→青→赤 と色が移る
ring_w = N * 0.026
eroded = B.filter(ImageFilter.MinFilter(int(ring_w) | 1))
band = np.clip(inside - np.asarray(eroded, dtype=np.float64) / 255.0, 0, 1)
band = np.asarray(Image.fromarray((band * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(N * 0.003)),
                  dtype=np.float64) / 255.0 * inside
YY, XX = np.mgrid[0:N, 0:N].astype(np.float64)
mx, my = N / 2, N * 0.5
ang = (np.arctan2(YY - my, XX - mx) / (2 * math.pi) + 1.0) % 1.0  # 0〜1
seg = ang * 3.0
i0 = np.floor(seg).astype(int) % 3
i1 = (i0 + 1) % 3
fr = (seg - np.floor(seg))[..., None]
rim_cols = np.array(RIM, dtype=np.float64)
rim = rim_cols[i0] * (1 - fr) + rim_cols[i1] * fr
rgb = rgb * (1 - band[..., None] * 0.92) + rim * (band[..., None] * 0.92)
# 帯のすぐ内側にうっすら同じ色の照り返し
glow_in = np.asarray(Image.fromarray((band * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(N * 0.02)),
                     dtype=np.float64) / 255.0 * inside * (1 - band)
rgb = rgb + (rim - rgb) * (glow_in * 0.35)[..., None]
rgb = np.clip(rgb, 0, 255)


def shadow_layer():
    s = Image.new("L", (N, N), 0)
    s.paste(B, (int(N * 0.012), int(N * 0.03)))
    s = s.filter(ImageFilter.GaussianBlur(N * 0.02))
    a = np.asarray(s, dtype=np.float64) / 255.0 * 0.38
    arr = np.zeros((N, N, 4))
    arr[..., :3] = (10, 12, 30)
    arr[..., 3] = a * 255
    return Image.fromarray(arr.astype(np.uint8), "RGBA")


piece = np.concatenate([rgb, inside[..., None] * 255], axis=2).astype(np.uint8)
img = shadow_layer()
img.alpha_composite(Image.fromarray(piece, "RGBA"))

# 胸のきらめき（4本の光の筋）
dr = ImageDraw.Draw(img)
sx, sy, s = N * 0.5, N * 0.53, N * 0.034
dr.polygon([(sx, sy - s * 1.9), (sx + s * 0.28, sy), (sx, sy + s * 1.9), (sx - s * 0.28, sy)], fill=(255, 255, 255, 235))
dr.polygon([(sx - s * 1.9, sy), (sx, sy + s * 0.28), (sx + s * 1.9, sy), (sx, sy - s * 0.28)], fill=(255, 255, 255, 235))

small = img.convert("RGBa").resize((SIZE, SIZE), Image.LANCZOS).convert("RGBA")
small.save(OUT)
a = np.asarray(small)[..., 3]
print("書き出し:", OUT, small.size, "四隅の透明度:", a[0, 0], a[0, -1], a[-1, 0], a[-1, -1])

if PREVIEW:
    others = [os.path.join(os.path.dirname(OUT), n + ".png") for n in ("MeepleRed", "MeepleBlue", "MeepleYellow")]
    imgs = [Image.open(p).convert("RGBA") for p in others if os.path.exists(p)] + [small]
    sheet = Image.new("RGBA", (len(imgs) * (SIZE // 2 + 20) + 20, SIZE // 2 + 40), (14, 18, 46, 255))
    for i, im in enumerate(imgs):
        sheet.alpha_composite(im.resize((SIZE // 2, SIZE // 2), Image.LANCZOS), (20 + i * (SIZE // 2 + 20), 20))
    sheet.convert("RGB").save(PREVIEW)
    print("確認用:", PREVIEW)
