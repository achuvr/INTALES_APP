# 星のカケラの絵（2026-11-11）。金色の五芒星を真ん中から5つに割った「とがった1片」（凧の形）。
# 光の当たる左半分は明るく、右半分は暗くして、立体の星の1片に見せる。5個集めると流れ星を1回見られる。
#   python make_star_fragment_icon.py <アイコンの書き出し先.png> <組み立て用の1片の書き出し先.png> [確認用.png]
#  ・アイコン: 1片を少し傾けて真ん中に置き、まわりをぼんやり光らせて小さなきらめきを添える（所持品・地図の置き場の絵）
#  ・組み立て用: 星の中心＝絵の中心。上向きの1片だけを描く（お知らせで 72 度ずつ回して5つ並べると星になる）
# どちらも背景は透明の 512×512
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

OUT_ICON, OUT_PIECE = sys.argv[1], sys.argv[2]
PREVIEW = sys.argv[3] if len(sys.argv) > 3 else None
S = 512
SS = 4  # 4倍の大きさで描いてから縮める（ふちをなめらかに）

LIGHT_TIP = np.array([255, 246, 196])
LIGHT_BASE = np.array([255, 208, 96])
DARK_TIP = np.array([246, 192, 84])
DARK_BASE = np.array([200, 120, 30])
RIDGE = (255, 255, 236, 255)
OUTLINE = (150, 88, 18, 210)


def kite(cx, cy, R, angle_deg, inset=0.0):
    """星の中心 (cx,cy)・外の半径 R の五芒星の、angle_deg の向き（0=上）の1片の4つの点（中心・左・先・右）"""
    r = R * 0.381966  # 正しい五芒星のへこみの半径
    a = np.deg2rad(angle_deg)

    def at(rad, ang):
        return np.array([cx + np.sin(a + ang) * rad, cy - np.cos(a + ang) * rad])

    c, tip = np.array([cx, cy], dtype=np.float64), at(R, 0.0)
    left, right = at(r, -np.deg2rad(36)), at(r, np.deg2rad(36))
    pts = [c, left, tip, right]
    if inset > 0:  # すき間（1片ずつに見えるよう、少しだけ内側へ縮める）
        g = sum(pts) / 4
        pts = [g + (p - g) * (1 - inset) for p in pts]
    return pts


def draw_piece(img, pts, scale):
    """1片を描く（左右の面・稜線・ふち）。img は SS 倍の大きさ"""
    c, left, tip, right = [tuple(p * scale) for p in pts]
    w, h = img.size
    yy, xx = np.mgrid[0:h, 0:w]

    def facet(a, b, d, col_tip, col_base):
        m = Image.new("L", (w, h), 0)
        ImageDraw.Draw(m).polygon([a, b, d], fill=255)
        mask = np.asarray(m, dtype=np.float64) / 255.0
        # 先（tip）に近いほど明るい色
        t_axis = np.array(tip) - np.array(c)
        L2 = (t_axis ** 2).sum()
        k = np.clip(((xx - c[0]) * t_axis[0] + (yy - c[1]) * t_axis[1]) / L2, 0, 1)[..., None]
        col = col_base * (1 - k) + col_tip * k
        arr = np.asarray(img, dtype=np.float64)
        alpha = mask[..., None]
        arr[..., :3] = arr[..., :3] * (1 - alpha) + col * alpha
        arr[..., 3] = np.maximum(arr[..., 3], mask * 255)
        return Image.fromarray(np.clip(arr, 0, 255).astype(np.uint8), "RGBA")

    img = facet(c, left, tip, LIGHT_TIP, LIGHT_BASE)
    img = facet(c, tip, right, DARK_TIP, DARK_BASE)
    d = ImageDraw.Draw(img)
    d.line([c, left, tip, right, c], fill=OUTLINE, width=int(2.2 * SS), joint="curve")
    d.line([c, tip], fill=RIDGE, width=int(2.6 * SS))
    return img


def sparkle(d, x, y, s, col):
    d.polygon([(x, y - s * 1.9), (x + s * 0.3, y), (x, y + s * 1.9), (x - s * 0.3, y)], fill=col)
    d.polygon([(x - s * 1.9, y), (x, y + s * 0.3), (x + s * 1.9, y), (x, y - s * 0.3)], fill=col)


# ---- 組み立て用の1片（星の中心＝絵の中心・上向き） ----
big = Image.new("RGBA", (S * SS, S * SS), (0, 0, 0, 0))
piece_pts = kite(S / 2, S / 2, S / 2 - 14, 0.0, inset=0.035)
big = draw_piece(big, piece_pts, SS)
piece = big.resize((S, S), Image.LANCZOS)
piece.save(OUT_PIECE)

# ---- アイコン（1片を傾けて真ん中に。まわりに光ときらめき） ----
R = 372
pts = kite(0, 0, R, 22.0)
# 4点の外接の真ん中が絵の中心に来るように動かす
xs, ys = [p[0] for p in pts], [p[1] for p in pts]
off = np.array([S / 2 - (min(xs) + max(xs)) / 2, S / 2 - (min(ys) + max(ys)) / 2])
pts = [p + off for p in pts]
big = Image.new("RGBA", (S * SS, S * SS), (0, 0, 0, 0))
big = draw_piece(big, pts, SS)
shard = big.resize((S, S), Image.LANCZOS)

glow_mask = shard.split()[3].filter(ImageFilter.GaussianBlur(26))
glow = Image.new("RGBA", (S, S), (255, 214, 120, 0))
glow.putalpha(glow_mask.point(lambda v: int(v * 0.85)))
icon = Image.new("RGBA", (S, S), (0, 0, 0, 0))
icon.alpha_composite(glow)
icon.alpha_composite(shard)
d = ImageDraw.Draw(icon)
sparkle(d, S * 0.24, S * 0.26, 13, (255, 250, 220, 245))
sparkle(d, S * 0.80, S * 0.72, 9, (255, 244, 205, 230))
sparkle(d, S * 0.27, S * 0.78, 6, (230, 240, 255, 220))
icon.save(OUT_ICON)
print("書き出し:", OUT_ICON, OUT_PIECE)

if PREVIEW:
    # 確認用: アイコン／1片／5片で組んだ星（4片＋空き1つ）を並べる
    bg = Image.new("RGBA", (S * 3, S), (20, 24, 50, 255))
    bg.alpha_composite(icon, (0, 0))
    bg.alpha_composite(piece, (S, 0))
    star = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    for i in range(5):
        p = piece.rotate(-72 * i, resample=Image.BICUBIC)
        if i == 4:
            a = np.asarray(p, dtype=np.float64)
            a[..., 3] *= 0.16
            p = Image.fromarray(a.astype(np.uint8), "RGBA")
        star.alpha_composite(p)
    bg.alpha_composite(star, (S * 2, 0))
    bg.convert("RGB").save(PREVIEW)
