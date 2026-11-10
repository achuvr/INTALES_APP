# 5%OFFチケットのカケラの絵（2026-11-10）。5%OFFチケットの絵（Assets/Textures/ItemIcon/5coupon.png）を
# 真ん中あたりでギザギザに破って、左半分（「5」の側）と右半分（「%」の側）を書き出す（同じ破れ目なので、2枚そろうと元のチケット）。
# 破れ目のふちを白く光らせ、少し傾ける。背景は透明の 512×512。1枚目のカケラ＝左、2枚目＝右（お知らせの枠に並べる）
#   python make_fragment_icon.py <5coupon.png> <左の書き出し先.png> <右の書き出し先.png> [確認用.png]
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

SRC, OUT, OUT_R = sys.argv[1], sys.argv[2], sys.argv[3]
PREVIEW = sys.argv[4] if len(sys.argv) > 4 else None
rng = np.random.default_rng(7)

im = Image.open(SRC).convert("RGBA")
W, H = im.size

# 破れ目: 上から下へ、少し右に傾いたギザギザの線（チケットの横の真ん中より少し右。「5」を残して「%」を切る）
ys = np.linspace(0, H, 34)
xs = W * 0.53 + (ys - H / 2) * 0.10 + rng.uniform(-26, 26, len(ys))
xs[::2] += 14
tear = list(zip(xs, ys))
def piece_of(poly, angle):
    """poly で切り抜いて、破れ目のふちを白くし、angle 度傾けて 512×512 の真ん中に置く"""
    mask = Image.new("L", (W, H), 0)
    ImageDraw.Draw(mask).polygon(poly, fill=255)
    mask = mask.filter(ImageFilter.GaussianBlur(1.2))
    a = np.asarray(im, dtype=np.float64).copy()
    a[..., 3] *= np.asarray(mask, dtype=np.float64) / 255.0
    edge = Image.new("L", (W, H), 0)
    ImageDraw.Draw(edge).line(tear, fill=255, width=14)
    edge = edge.filter(ImageFilter.GaussianBlur(2.5))
    e = np.asarray(edge, dtype=np.float64) / 255.0 * (a[..., 3] / 255.0)
    a[..., :3] = a[..., :3] * (1 - e[..., None] * 0.85) + 255 * e[..., None] * 0.85
    p = Image.fromarray(np.clip(a, 0, 255).astype(np.uint8), "RGBA").rotate(angle, resample=Image.BICUBIC, expand=False)
    p = p.crop(p.getbbox())
    S = 1024
    canvas = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    scale = min(S * 0.86 / p.width, S * 0.86 / p.height)
    p = p.resize((int(p.width * scale), int(p.height * scale)), Image.LANCZOS)
    canvas.alpha_composite(p, ((S - p.width) // 2, (S - p.height) // 2))
    return canvas.resize((512, 512), Image.LANCZOS)


left = piece_of([(0, 0)] + tear + [(0, H)], 9)
right = piece_of([(W, 0)] + tear + [(W, H)], -9)
left.save(OUT)
right.save(OUT_R)
print("書き出し:", OUT, OUT_R)
if PREVIEW:
    bg = Image.new("RGBA", (1024, 512), (20, 24, 50, 255))
    bg.alpha_composite(left, (0, 0))
    bg.alpha_composite(right, (512, 0))
    bg.convert("RGB").save(PREVIEW)
