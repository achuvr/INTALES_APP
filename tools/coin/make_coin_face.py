# コインプッシャー（2026-11-12）の 3D のコインの面の絵。金色の地に、ふちの段と浮き彫りの「G」。
# 円の外も金色で塗りつぶす（コインの面は円に切り抜いて貼るので、外側は見えない）。不透明の 256×256。
#   python make_coin_face.py <書き出し先.png> [確認用.png]
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

OUT = sys.argv[1]
PREVIEW = sys.argv[2] if len(sys.argv) > 2 else None

S = 256
SS = 4
N = S * SS
C = N / 2

YY, XX = np.mgrid[0:N, 0:N].astype(np.float64)
R = np.hypot(XX - C, YY - C) / (N / 2)  # 中心0・ふち1

GOLD_HI = np.array([255, 236, 150], dtype=np.float64)
GOLD_MID = np.array([246, 192, 60], dtype=np.float64)
GOLD_LO = np.array([176, 112, 22], dtype=np.float64)


def lerp(a, b, t):
    t = np.clip(t, 0, 1)[..., None]
    return a * (1 - t) + b * t


# 地: 左上が明るい金、右下が暗い金
diag = ((XX - C) * 0.6 + (YY - C) * 0.8) / (N / 2)
base = lerp(GOLD_HI, GOLD_MID, (diag + 1) * 0.5)
base = lerp(base, GOLD_LO, np.clip(diag - 0.2, 0, 1) * 0.8)

# ふちの段（外側の盛り上がった輪 0.84〜0.98 と、その内側の溝 0.80〜0.84）
rim = (R > 0.84) & (R <= 0.985)
groove = (R > 0.79) & (R <= 0.84)
img = base.copy()
light = np.clip(-((XX - C) * 0.6 + (YY - C) * 0.8) / (N / 2), -1, 1)  # 左上ほど +1
img[rim] = lerp(GOLD_MID, GOLD_HI, (light[rim] + 1) * 0.5)
img[groove] = lerp(GOLD_LO, GOLD_MID, (light[groove] + 1) * 0.35)

# 小さな点の飾り（ふちに沿って24個）
dots = Image.new("L", (N, N), 0)
dd = ImageDraw.Draw(dots)
for k in range(24):
    a = k / 24 * 2 * np.pi
    x, y = C + np.cos(a) * N * 0.455, C + np.sin(a) * N * 0.455
    r = N * 0.012
    dd.ellipse([x - r, y - r, x + r, y + r], fill=255)
dm = np.asarray(dots, dtype=np.float64)[..., None] / 255.0
img = img * (1 - dm * 0.55) + GOLD_HI * (dm * 0.55)

# 浮き彫りの「G」: 形の高さをぼかして、左上から光を当てる
font = None
for path in (r"C:\Windows\Fonts\arialbd.ttf", r"C:\Windows\Fonts\segoeuib.ttf", r"C:\Windows\Fonts\arial.ttf"):
    try:
        font = ImageFont.truetype(path, int(N * 0.58))
        break
    except OSError:
        continue
glyph = Image.new("L", (N, N), 0)
ImageDraw.Draw(glyph).text((C, C + N * 0.02), "G", font=font, anchor="mm", fill=255)
g = np.asarray(glyph.filter(ImageFilter.GaussianBlur(N * 0.012)), dtype=np.float64) / 255.0
gy, gx = np.gradient(g)
shade = np.clip((gx * 0.6 + gy * 0.8) * N * 0.06, -1, 1)  # 左上を向いたふちが明るい（浮き彫り）
inside = g > 0.5
face = lerp(GOLD_MID, GOLD_HI, 0.35 + 0.0 * g)
img = np.where(inside[..., None], face, img)
img = img + (shade[..., None] * 90.0) * (g > 0.05)[..., None]
img = np.clip(img, 0, 255)

out = Image.fromarray(img.astype(np.uint8), "RGB").resize((S, S), Image.LANCZOS)
out.save(OUT)
print("書き出し:", OUT)
if PREVIEW:
    pv = Image.new("RGB", (S * 2 + 30, S + 20), (14, 18, 46))
    pv.paste(out, (10, 10))
    mask = Image.new("L", (S, S), 0)
    ImageDraw.Draw(mask).ellipse([0, 0, S - 1, S - 1], fill=255)
    pv.paste(out, (S + 20, 10), mask)
    pv.save(PREVIEW)
