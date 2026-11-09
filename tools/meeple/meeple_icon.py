# 所持品のコマのアイコン（赤・青・黄。背景透過の 512×512 PNG）を書き出す（2026-11-10）
# 形は meeple.py の数値をそのまま読む。ふちを少し丸く盛り上げたように光と影をつけ（左上から光）、足もとに淡い影を落とす
#   python meeple_icon.py <meeple.py のあるフォルダ> <書き出し先のフォルダ> [確認用の絵]
import io
import os
import re
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

HERE = sys.argv[1]
OUT_DIR = sys.argv[2]
PREVIEW = sys.argv[3] if len(sys.argv) > 3 else None

SIZE = 512
SS = 4
N = SIZE * SS
COLORS = {  # 3Dの駒・惑星の色の丸（PlanetColors）と同じ色
    "MeepleRed": (214, 40, 40),
    "MeepleBlue": (40, 103, 214),
    "MeepleYellow": (255, 211, 30),
}

src = io.open(os.path.join(HERE, "meeple.py"), encoding="utf-8").read()
block = src[src.index("RIGHT = ["):src.index("SMOOTH =")]
smooth_line = re.search(r"^SMOOTH = .*$", src, re.M).group(0)
box_line = re.search(r"^x0, x1, y0, y1 = .*$", src, re.M).group(0)
ns = {}
exec(block + "\n" + smooth_line + "\n" + box_line, ns)
BODY, HEAD_C, HEAD_R, SMOOTH = ns["BODY"], ns["HEAD_C"], ns["HEAD_R"], ns["SMOOTH"]
x0, x1, y0, y1 = ns["x0"], ns["x1"], ns["y0"], ns["y1"]

# 形を少し上に寄せて置く（下に影のぶんの余白）
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
# 角を丸める（meeple.py と同じ）
mask = mask.filter(ImageFilter.GaussianBlur(SMOOTH * scale))
inside = (np.asarray(mask) >= 128).astype(np.float64)
B = Image.fromarray((inside * 255).astype(np.uint8), "L")

# 盛り上がり: 形をぼかした値を高さにして、ふちだけ傾かせる
bevel = N * 0.05
H = np.asarray(B.filter(ImageFilter.GaussianBlur(bevel)), dtype=np.float64) / 255.0
t = np.clip(H / 0.8, 0.0, 1.0)
H = (1.0 - (1.0 - t) ** 2) * inside  # ふちは急に、内側へ行くほどなめらかに平らへ（境目に線が出ないように）
gy, gx = np.gradient(H)
k = 1.0 / (bevel * 0.9)
nx, ny, nz = -gx / k * 5, -gy / k * 5, np.ones_like(H)
norm = np.sqrt(nx * nx + ny * ny + nz * nz)
nx, ny, nz = nx / norm, ny / norm, nz / norm
L = np.array([-0.55, -0.65, 0.52])
L = L / np.linalg.norm(L)
lit = nx * L[0] + ny * L[1] + nz * L[2]
flat = L[2]
f = 1.0 + 1.1 * (lit - flat)  # 平らなところは 1（もとの色）
yy = np.arange(N, dtype=np.float64)[:, None] / N
f = f * (1.07 - 0.14 * yy)  # 上ほど少し明るく
# つや: 光を返す向きが目に向くところ
rz = 2 * lit * nz - L[2]
spec = np.clip(rz, 0, 1) ** 24 * 0.32 * (1 - nz)  # ふちの上側だけ
spec = np.clip(spec * 3, 0, 0.45)


def colorize(base):
    base = np.array(base, dtype=np.float64)[None, None, :]
    ff = f[..., None]
    light = base + (255 - base) * np.clip(ff - 1, 0, 1) * 0.9
    dark = base * np.clip(ff, 0.45, 1)
    rgb = np.where(ff >= 1, light, dark)
    rgb = rgb + (255 - rgb) * spec[..., None]
    return np.clip(rgb, 0, 255)


def shadow_layer():
    s = Image.new("L", (N, N), 0)
    s.paste(B, (int(N * 0.012), int(N * 0.03)))
    s = s.filter(ImageFilter.GaussianBlur(N * 0.02))
    a = np.asarray(s, dtype=np.float64) / 255.0 * 0.38
    arr = np.zeros((N, N, 4))
    arr[..., :3] = (10, 12, 30)
    arr[..., 3] = a * 255
    return Image.fromarray(arr.astype(np.uint8), "RGBA")


os.makedirs(OUT_DIR, exist_ok=True)
shadow = shadow_layer()
outs = []
for name, base in COLORS.items():
    rgb = colorize(base)
    piece = np.concatenate([rgb, inside[..., None] * 255], axis=2).astype(np.uint8)
    img = shadow.copy()
    img.alpha_composite(Image.fromarray(piece, "RGBA"))
    # 縮めるときは色と透明度を掛け合わせた状態で（ふちに黒いにじみが出ないように）
    small = img.convert("RGBa").resize((SIZE, SIZE), Image.LANCZOS).convert("RGBA")
    path = os.path.join(OUT_DIR, name + ".png")
    small.save(path)
    outs.append(small)
    a = np.asarray(small)[..., 3]
    print("書き出し:", path, small.size, "四隅の透明度:", a[0, 0], a[0, -1], a[-1, 0], a[-1, -1])

if PREVIEW:
    sheet = Image.new("RGBA", (SIZE * 3 + 80, SIZE + 40), (14, 18, 46, 255))
    for i, im in enumerate(outs):
        sheet.alpha_composite(im, (20 + i * (SIZE + 20), 20))
    sheet.convert("RGB").save(PREVIEW)
    print("確認用:", PREVIEW)
