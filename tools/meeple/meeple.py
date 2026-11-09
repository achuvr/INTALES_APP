# ミープル（一般的なコマの形）を赤・背景透過の PNG に書き出す。形は一から描く（公式の絵は使わない）
# 4倍の大きさで描いて、角をぼかしてから2値に戻し（角を丸める）、縮めてなめらかにする
import sys
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

OUT = sys.argv[1]
PREVIEW = sys.argv[2]
SIZE = 1024
SS = 4
N = SIZE * SS
RED = (214, 40, 40)  # #D62828

# 形（1000×1000 の座標）
# 右半分（首の中心から時計回りに股まで）を決めて、左右対称にする
RIGHT = [
    (500, 330), (600, 330), (700, 345), (850, 370), (935, 385), (985, 440), (985, 500), (940, 555), (850, 575),
    (712, 582), (720, 625), (725, 680), (785, 810), (840, 910), (862, 965), (842, 1000), (595, 1000), (565, 895),
    (530, 790), (500, 760),
]
BODY = RIGHT + [(1000 - x, y) for x, y in reversed(RIGHT[1:-1])]
HEAD_C, HEAD_R = (500, 205), 155  # 頭の下端（360）は変えずに小さく
SMOOTH = 20  # 角を丸める大きさ（座標の単位）

# 形の外枠（x 15〜985、y 50〜1000）をキャンバスの中央に、余白 6% で置く
x0, x1, y0, y1 = 15, 985, 50, 1000
scale = N * 0.88 / max(x1 - x0, y1 - y0)
ox = N / 2 - (x0 + x1) / 2 * scale
oy = N / 2 - (y0 + y1) / 2 * scale


def P(x, y):
    return (ox + x * scale, oy + y * scale)


mask = Image.new("L", (N, N), 0)
d = ImageDraw.Draw(mask)
d.polygon([P(x, y) for x, y in BODY], fill=255)
cx, cy = P(*HEAD_C)
r = HEAD_R * scale
d.ellipse((cx - r, cy - r, cx + r, cy + r), fill=255)

# 角を丸める: ぼかして半分のところで切る
mask = mask.filter(ImageFilter.GaussianBlur(SMOOTH * scale))
binary = (np.asarray(mask) >= 128).astype(np.uint8) * 255

# 縮めてふちをなめらかに（色は全面同じ赤にして、透明の度合いだけで形を作る）
alpha = Image.fromarray(binary, "L").resize((SIZE, SIZE), Image.LANCZOS)
img = Image.new("RGBA", (SIZE, SIZE), RED + (0,))
img.putalpha(alpha)
img.save(OUT)

# 確認用: 市松模様の上に載せた絵
checker = Image.new("RGBA", (SIZE, SIZE), (255, 255, 255, 255))
cd = ImageDraw.Draw(checker)
for yy in range(0, SIZE, 32):
    for xx in range(0, SIZE, 32):
        if (xx // 32 + yy // 32) % 2 == 0:
            cd.rectangle((xx, yy, xx + 31, yy + 31), fill=(220, 220, 220, 255))
checker.alpha_composite(img)
checker.convert("RGB").resize((512, 512), Image.LANCZOS).save(PREVIEW)

a = np.asarray(alpha)
ys, xs = np.where(a > 0)
print("書き出し:", OUT, img.size, "形の範囲:", xs.min(), "-", xs.max(), "/", ys.min(), "-", ys.max())
print("四隅の透明度:", a[0, 0], a[0, -1], a[-1, 0], a[-1, -1])
