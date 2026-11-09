# meeple.py（デスクトップの画像）と同じ形から、立体用の輪郭線（高さ1・足もとが0・左右の中心が0）を取り出す
# 形の数値は meeple.py からそのまま読む（同じ形にするため）
import io
import json
import math
import re
import sys

import contourpy
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

HERE = sys.argv[1]
OUT = sys.argv[2]
POINTS = int(sys.argv[3]) if len(sys.argv) > 3 else 256

src = io.open(HERE + "/meeple.py", encoding="utf-8").read()
block = src[src.index("RIGHT = ["):src.index("SMOOTH =")]
smooth_line = re.search(r"^SMOOTH = .*$", src, re.M).group(0)
box_line = re.search(r"^x0, x1, y0, y1 = .*$", src, re.M).group(0)
ns = {}
exec(block + "\n" + smooth_line + "\n" + box_line, ns)
BODY, HEAD_C, HEAD_R, SMOOTH = ns["BODY"], ns["HEAD_C"], ns["HEAD_R"], ns["SMOOTH"]
x0, x1, y0, y1 = ns["x0"], ns["x1"], ns["y0"], ns["y1"]

# 画像と同じ手順（4096 の大きさで描いて、ぼかして半分のところ＝輪郭）
N = 4096
scale = N * 0.88 / max(x1 - x0, y1 - y0)
ox = N / 2 - (x0 + x1) / 2 * scale
oy = N / 2 - (y0 + y1) / 2 * scale
P = lambda x, y: (ox + x * scale, oy + y * scale)
mask = Image.new("L", (N, N), 0)
d = ImageDraw.Draw(mask)
d.polygon([P(x, y) for x, y in BODY], fill=255)
cx, cy = P(*HEAD_C)
r = HEAD_R * scale
d.ellipse((cx - r, cy - r, cx + r, cy + r), fill=255)
blurred = np.asarray(mask.filter(ImageFilter.GaussianBlur(SMOOTH * scale)), dtype=np.float64)

lines = contourpy.contour_generator(z=blurred).lines(127.5)
line = max(lines, key=len)  # いちばん長い輪郭（外形）
col, row = line[:, 0], line[:, 1]
X = (col - ox) / scale          # 形の座標（1000 単位）
Y = (row - oy) / scale
top, bottom = Y.min(), Y.max()
height = bottom - top
px = (X - 500.0) / height       # 高さ 1 に
py = (bottom - Y) / height      # 上向き・足もとが 0

# 閉じた線にして、長さで等間隔に取り直す
pts = np.stack([px, py], axis=1)
if np.linalg.norm(pts[0] - pts[-1]) > 1e-9:
    pts = np.vstack([pts, pts[:1]])
seg = np.linalg.norm(np.diff(pts, axis=0), axis=1)
s = np.concatenate([[0], np.cumsum(seg)])
total = s[-1]
t = np.linspace(0, total, POINTS, endpoint=False)
rx = np.interp(t, s, pts[:, 0])
ry = np.interp(t, s, pts[:, 1])
out = np.stack([rx, ry], axis=1)

# 反時計回り（上向きの座標で）にそろえる
area = 0.5 * np.sum(out[:, 0] * np.roll(out[:, 1], -1) - np.roll(out[:, 0], -1) * out[:, 1])
if area < 0:
    out = out[::-1]

# 外向きに出っ張った部分（手の先・足の角・頭）のいちばん小さい丸みの半径（角を丸める大きさの目安）
def curvature_radius(a):
    prev, nxt = np.roll(a, 1, axis=0), np.roll(a, -1, axis=0)
    v1, v2 = a - prev, nxt - a
    cross = v1[:, 0] * v2[:, 1] - v1[:, 1] * v2[:, 0]
    ang = np.arctan2(cross, (v1 * v2).sum(axis=1))
    lens = (np.linalg.norm(v1, axis=1) + np.linalg.norm(v2, axis=1)) / 2
    with np.errstate(divide="ignore"):
        rad = np.where(ang > 1e-9, lens / ang, np.inf)  # 反時計回りで左に曲がる＝外向きに出っ張る
    return rad

rad = curvature_radius(out)
json.dump({"points": out.round(6).tolist(), "width": float(out[:, 0].max() - out[:, 0].min()), "height": 1.0},
          io.open(OUT, "w", encoding="utf-8"))
print(f"輪郭の点: {len(out)}  幅: {out[:, 0].max() - out[:, 0].min():.3f}（高さ 1）")
print(f"出っ張りのいちばん小さい丸みの半径: {np.min(rad):.4f}")
