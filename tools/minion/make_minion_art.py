"""
ミニオン（2026-11-14。きずな座の完成で作れる、旅に出てアイテムを持ち帰る仲間）の絵を作る。

  python tools/minion/make_minion_art.py [--preview out.png]

元の絵は Assets/Textures/Job/{warrior,magician,archer,gunner}.png（白いシルエット。以前のキャラクター作成の職業の絵）。
職業4種類 × 属性4種類（炎・水・自然・雷）の16枚を、属性の色のグラデーション・縁の光・中の小さな星・外側の淡い光で塗って
Assets/Resources/Minions/{職業}_{属性}.png（512×512）に書き出す。属性の印（炎・しずく・葉・稲妻）も
Assets/Resources/Minions/element_{属性}.png（256×256）に書き出す。
4種類とも同じ倍率で縮め、足もとの高さをそろえる（並べたときに大きさがそろうように）。
"""
import argparse
import math
import os
import random

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, '..', '..'))
SRC = os.path.join(ROOT, 'Assets', 'Textures', 'Job')
OUT = os.path.join(ROOT, 'Assets', 'Resources', 'Minions')

JOBS = ['warrior', 'magician', 'archer', 'gunner']
# 属性の色: (上の明るい色, 下の濃い色, 縁と外側の光の色)
ELEMENTS = {
    'fire': ((255, 214, 150), (255, 92, 54), (255, 150, 90)),
    'water': ((190, 236, 255), (52, 132, 255), (110, 196, 255)),
    'nature': ((214, 255, 170), (52, 190, 96), (130, 236, 130)),
    'thunder': ((255, 252, 196), (255, 196, 40), (255, 230, 110)),
}

SIZE = 512
PAD_BOTTOM = 34
SCALE = (SIZE - 70) / 870.0  # いちばん背の高い戦士（元の絵で 870）が収まる倍率。4種類とも同じ


def load_alpha(job):
    im = Image.open(os.path.join(SRC, job + '.png')).convert('RGBA')
    a = np.array(im)[..., 3].astype(np.float32) / 255.0
    ys, xs = np.nonzero(a > 0.04)
    x0, x1, y0, y1 = xs.min(), xs.max(), ys.min(), ys.max()
    crop = Image.fromarray((a[y0:y1 + 1, x0:x1 + 1] * 255).astype(np.uint8), 'L')
    w = int(round(crop.width * SCALE))
    h = int(round(crop.height * SCALE))
    crop = crop.resize((w, h), Image.LANCZOS)
    canvas = Image.new('L', (SIZE, SIZE), 0)
    canvas.paste(crop, ((SIZE - w) // 2, SIZE - PAD_BOTTOM - h))
    return np.array(canvas).astype(np.float32) / 255.0


def lerp(a, b, t):
    return a + (b - a) * t


def sparkle(draw_arr, cx, cy, r, alpha):
    """白い十字のきらめきを draw_arr（H×W の明るさ 0〜1）に足す"""
    h, w = draw_arr.shape
    x0, x1 = max(0, int(cx - r)), min(w - 1, int(cx + r))
    y0, y1 = max(0, int(cy - r)), min(h - 1, int(cy + r))
    thin = r * 0.18
    for y in range(y0, y1 + 1):
        for x in range(x0, x1 + 1):
            dx, dy = abs(x + 0.5 - cx), abs(y + 0.5 - cy)
            v = max(1 - (dx / r + dy / thin), 1 - (dy / r + dx / thin))
            if v > 0:
                draw_arr[y, x] = max(draw_arr[y, x], min(1.0, v * 2.2) * alpha)


def render(job, element, seed):
    top, bottom, glow_c = ELEMENTS[element]
    a = load_alpha(job)
    ys, xs = np.nonzero(a > 0.04)
    y0, y1 = ys.min(), ys.max()
    # 上から下へ明るい色 → 濃い色（少し左上からの光）
    yy, xx = np.mgrid[0:SIZE, 0:SIZE].astype(np.float32)
    t = np.clip((yy - y0) / max(1, (y1 - y0)), 0, 1)
    t = t ** 0.9
    rgb = np.zeros((SIZE, SIZE, 3), np.float32)
    for c in range(3):
        rgb[..., c] = lerp(top[c], bottom[c], t)
    # 左上の柔らかい光
    cx, cy = SIZE * 0.36, y0 + (y1 - y0) * 0.28
    d = np.sqrt((xx - cx) ** 2 + (yy - cy) ** 2) / (SIZE * 0.45)
    hl = np.clip(1 - d, 0, 1) ** 2 * 0.35
    rgb = rgb + (255 - rgb) * hl[..., None]
    # 縁の光（シルエットの内側のふち）
    a_img = Image.fromarray((a * 255).astype(np.uint8), 'L')
    inner = np.array(a_img.filter(ImageFilter.MinFilter(9))).astype(np.float32) / 255.0
    rim = np.clip(a - inner, 0, 1)
    rim = np.array(Image.fromarray((rim * 255).astype(np.uint8), 'L').filter(ImageFilter.GaussianBlur(1.6))).astype(np.float32) / 255.0
    rgb = rgb + (np.array([255, 255, 255], np.float32) - rgb) * (rim[..., None] * 0.55)
    # 中の小さな星（シルエットの中だけ）
    rnd = random.Random(seed)
    stars = np.zeros((SIZE, SIZE), np.float32)
    placed = 0
    tries = 0
    while placed < 7 and tries < 400:
        tries += 1
        x = rnd.uniform(SIZE * 0.15, SIZE * 0.85)
        y = rnd.uniform(y0 + 30, y1 - 30)
        if inner[int(y), int(x)] < 0.99:
            continue
        r = rnd.uniform(7, 15) if placed < 2 else rnd.uniform(3, 6)
        sparkle(stars, x, y, r, rnd.uniform(0.55, 0.9))
        placed += 1
    dots = np.zeros((SIZE, SIZE), np.float32)
    for _ in range(40):
        x = rnd.uniform(0, SIZE - 1)
        y = rnd.uniform(y0, y1)
        if inner[int(y), int(x)] < 0.99:
            continue
        rr = rnd.uniform(0.8, 1.8)
        dd = np.sqrt((xx - x) ** 2 + (yy - y) ** 2)
        dots = np.maximum(dots, np.clip(1 - dd / rr, 0, 1) * rnd.uniform(0.3, 0.7))
    sp = np.clip(stars + dots, 0, 1) * inner
    rgb = rgb + (255 - rgb) * sp[..., None]
    # 外側の淡い光（シルエットの形に沿って）
    halo = np.array(a_img.filter(ImageFilter.GaussianBlur(12))).astype(np.float32) / 255.0
    halo = np.clip(halo * 1.4, 0, 1) * 0.55
    out_a = np.maximum(a, halo)
    out_rgb = np.zeros_like(rgb)
    w_body = np.where(out_a > 0, a / np.maximum(out_a, 1e-6), 0)[..., None]
    out_rgb = rgb * w_body + np.array(glow_c, np.float32) * (1 - w_body)
    img = np.dstack([np.clip(out_rgb, 0, 255), np.clip(out_a * 255, 0, 255)]).astype(np.uint8)
    return Image.fromarray(img, 'RGBA')


def teardrop(S, cx, cy, r, stretch, tilt_deg=0.0, power=1.0):
    """先が上を向いたしずく形の点の並び（cx, cy は下の丸の中心。0〜1 の割合。tilt_deg だけ傾ける）"""
    pts = []
    ang = math.radians(tilt_deg)
    for i in range(241):
        th = math.pi * 2 * i / 240
        x = math.sin(th) * r
        y = -math.cos(th) * r
        if y < 0:
            y *= stretch
            k = 1 + y / (r * stretch)
            x *= k ** power if k > 0 else 0
        rx = x * math.cos(ang) - y * math.sin(ang)
        ry = x * math.sin(ang) + y * math.cos(ang)
        pts.append(((cx + rx) * S, (cy + ry) * S))
    return pts


def element_icon(element, size=256):
    """属性の印: 炎・しずく・葉・稲妻（白い縁取りの色つきの形。炎・しずくは中に明るい形）"""
    top, bottom, glow_c = ELEMENTS[element]
    S = size * 4  # 大きく描いて縮める（ふちをなめらかに）
    mask = Image.new('L', (S, S), 0)
    d = ImageDraw.Draw(mask)
    light = Image.new('L', (S, S), 0)  # 中の明るい部分
    ld = ImageDraw.Draw(light)
    if element == 'fire':
        # 炎: まん中の大きな炎と、左右の小さな炎を重ねる。中に明るい炎
        d.polygon(teardrop(S, 0.50, 0.66, 0.22, 2.35, 0, 0.75), fill=255)
        d.polygon(teardrop(S, 0.37, 0.72, 0.11, 1.9, -32, 0.8), fill=255)
        d.polygon(teardrop(S, 0.63, 0.72, 0.11, 1.9, 32, 0.8), fill=255)
        ld.polygon(teardrop(S, 0.50, 0.76, 0.11, 1.9, 0, 0.8), fill=255)
    elif element == 'water':
        d.polygon(teardrop(S, 0.50, 0.62, 0.26, 1.75, 0, 1.1), fill=255)
        ld.ellipse([0.33 * S, 0.56 * S, 0.42 * S, 0.70 * S], fill=255)  # 光の点
    elif element == 'nature':
        # 葉: 先のとがったレンズ形を斜めに。下に短い茎、まん中に葉脈
        pts = []
        ang = math.radians(35)
        for i in range(241):
            th = math.pi * 2 * i / 240
            x = math.sin(th) * 0.20 * S
            y = -math.cos(th) * 0.34 * S
            x *= (1 - abs(math.cos(th)) ** 3) ** 0.6
            pts.append((0.47 * S + x * math.cos(ang) - y * math.sin(ang), 0.47 * S + x * math.sin(ang) + y * math.cos(ang)))
        d.polygon(pts, fill=255)
        ax, ay = -math.sin(ang), math.cos(ang)  # 葉のまん中から下の先への向き

        def along(k):
            return (0.47 * S + ax * k * S, 0.47 * S + ay * k * S)

        d.line([along(0.30), along(0.46)], fill=255, width=int(0.05 * S))  # 茎
        ld.line([along(0.26), along(-0.22)], fill=255, width=int(0.03 * S))  # 葉脈
    else:  # thunder
        pts = [(0.58, 0.10), (0.30, 0.54), (0.48, 0.54), (0.38, 0.90), (0.72, 0.42), (0.53, 0.42), (0.66, 0.10)]
        d.polygon([(x * S, y * S) for x, y in pts], fill=255)
    body = np.array(mask).astype(np.float32) / 255.0
    lit = np.array(light).astype(np.float32) / 255.0 * body
    # 色: 上から下へグラデーション、白い縁、外側に淡い光
    yy = np.mgrid[0:S, 0:S][0].astype(np.float32) / S
    rgb = np.zeros((S, S, 3), np.float32)
    for k in range(3):
        rgb[..., k] = lerp(top[k], bottom[k], np.clip((yy - 0.1) / 0.8, 0, 1))
    body_img = Image.fromarray((body * 255).astype(np.uint8), 'L')
    outline = np.array(body_img.filter(ImageFilter.MaxFilter(int(S * 0.045) | 1))).astype(np.float32) / 255.0
    halo = np.array(body_img.filter(ImageFilter.GaussianBlur(S * 0.05))).astype(np.float32) / 255.0 * 0.6
    a = np.maximum(np.maximum(body, outline), halo)
    # 中の明るい部分（炎の芯・しずくの光の点・葉脈）は、上の明るい色と白の間
    lit_img = Image.fromarray((lit * 255).astype(np.uint8), 'L').filter(ImageFilter.GaussianBlur(S * 0.004))
    lit = np.array(lit_img).astype(np.float32) / 255.0
    lit_col = np.array([lerp(top[k], 255, 0.55) for k in range(3)], np.float32)
    rgb = rgb * (1 - lit[..., None]) + lit_col * lit[..., None]
    # ふちをなめらかに混ぜる
    w_body = body[..., None]
    w_out = np.clip(outline - body, 0, 1)[..., None]
    col = rgb * w_body + 255.0 * w_out + np.array(glow_c, np.float32) * np.clip(1 - w_body - w_out, 0, 1)
    img = np.dstack([np.clip(col, 0, 255), np.clip(a * 255, 0, 255)]).astype(np.uint8)
    return Image.fromarray(img, 'RGBA').resize((size, size), Image.LANCZOS)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--preview', help='16枚を並べた確認用の画像を書き出す（Resources には書かない）')
    args = ap.parse_args()
    images = {}
    for li, job in enumerate(JOBS):
        for ei, element in enumerate(ELEMENTS):
            images[(job, element)] = render(job, element, seed=1000 + li * 10 + ei)
    icons = {e: element_icon(e) for e in ELEMENTS}
    if args.preview:
        T = 256
        sheet = Image.new('RGBA', (T * 5, T * 4), (10, 14, 38, 255))
        for ei, element in enumerate(ELEMENTS):
            ic = icons[element].resize((T // 2, T // 2), Image.LANCZOS)
            sheet.alpha_composite(ic, (T // 4, ei * T + T // 4))
            for li, job in enumerate(JOBS):
                im = images[(job, element)].resize((T, T), Image.LANCZOS)
                sheet.alpha_composite(im, ((li + 1) * T, ei * T))
        sheet.save(args.preview)
        print('preview', args.preview)
        return
    os.makedirs(OUT, exist_ok=True)
    for (job, element), im in images.items():
        im.save(os.path.join(OUT, f'{job}_{element}.png'), optimize=True)
    for element, im in icons.items():
        im.save(os.path.join(OUT, f'element_{element}.png'), optimize=True)
    print('wrote', len(images), 'minions and', len(icons), 'icons to', OUT)


if __name__ == '__main__':
    main()
