# 「トランプを引く」（ショップの品物・星座の完成のお知らせ。2026-11-12）のアイコン。
# すみれ色の裏のカードを重ねた山札から、1枚が右上へ引き抜かれている。引いたカードの表は 赤・青・黄・黒 の4色の帯に
# 大きな白い「?」（何が出るかはお楽しみ）。まわりはぼんやり光らせて、小さなきらめきを添える
# （金貨・星のカケラ・惑星回復薬のアイコンと同じ見せ方）。背景は透明の 512×512。
#   python make_card_draw_icon.py <書き出し先.png> [確認用.png]
import sys

from PIL import Image, ImageDraw, ImageFilter, ImageFont

OUT = sys.argv[1]
PREVIEW = sys.argv[2] if len(sys.argv) > 2 else None

S = 512
SS = 4
W = S * SS
CARD_W, CARD_H = 200 * SS, 284 * SS   # トランプと同じ縦横比（1.42）
RADIUS = 24 * SS

BACK_TOP, BACK_BOTTOM = (106, 75, 196), (42, 26, 102)   # 裏: すみれ色（CardDeck.CreateBack と同じ）
GOLD = (255, 211, 122)
COLORS = [(255, 107, 107), (92, 147, 255), (255, 216, 74), (74, 77, 90)]       # 赤・青・黄・黒（上の色）
COLORS_DARK = [(201, 54, 60), (47, 95, 214), (224, 168, 0), (28, 29, 36)]      # 同じ（下の色）


def vgrad(w, h, top, bottom):
    g = Image.new("RGB", (1, h))
    for y in range(h):
        t = y / max(1, h - 1)
        g.putpixel((0, y), tuple(int(top[i] * (1 - t) + bottom[i] * t) for i in range(3)))
    return g.resize((w, h))


def rounded_mask(w, h, r):
    m = Image.new("L", (w, h), 0)
    ImageDraw.Draw(m).rounded_rectangle([0, 0, w - 1, h - 1], radius=r, fill=255)
    return m


def card_back():
    img = Image.new("RGBA", (CARD_W, CARD_H), (0, 0, 0, 0))
    face = vgrad(CARD_W, CARD_H, BACK_TOP, BACK_BOTTOM).convert("RGBA")
    img.paste(face, (0, 0), rounded_mask(CARD_W, CARD_H, RADIUS))
    d = ImageDraw.Draw(img)
    inset = 12 * SS
    d.rounded_rectangle([inset, inset, CARD_W - inset, CARD_H - inset], radius=RADIUS - 8 * SS,
                        outline=GOLD + (230,), width=6 * SS)
    # まん中のきらめき
    cx, cy, s = CARD_W / 2, CARD_H / 2, 26 * SS
    d.polygon([(cx, cy - s * 1.9), (cx + s * 0.32, cy), (cx, cy + s * 1.9), (cx - s * 0.32, cy)], fill=GOLD + (255,))
    d.polygon([(cx - s * 1.9, cy), (cx, cy + s * 0.32), (cx + s * 1.9, cy), (cx, cy - s * 0.32)], fill=GOLD + (255,))
    # ふち（暗い線）
    d.rounded_rectangle([0, 0, CARD_W - 1, CARD_H - 1], radius=RADIUS, outline=(24, 14, 60, 255), width=4 * SS)
    return img


def card_front():
    img = Image.new("RGBA", (CARD_W, CARD_H), (0, 0, 0, 0))
    face = Image.new("RGBA", (CARD_W, CARD_H), (0, 0, 0, 0))
    band = CARD_W // 4
    for i in range(4):
        g = vgrad(band + 2, CARD_H, COLORS[i], COLORS_DARK[i]).convert("RGBA")
        face.paste(g, (i * band, 0))
    img.paste(face, (0, 0), rounded_mask(CARD_W, CARD_H, RADIUS))
    d = ImageDraw.Draw(img)
    inset = 12 * SS
    d.rounded_rectangle([inset, inset, CARD_W - inset, CARD_H - inset], radius=RADIUS - 8 * SS,
                        outline=(255, 255, 255, 170), width=5 * SS)
    # 大きな「?」（白・濃いふち）
    font = None
    for path in (r"C:\Windows\Fonts\arialbd.ttf", r"C:\Windows\Fonts\segoeuib.ttf", r"C:\Windows\Fonts\arial.ttf"):
        try:
            font = ImageFont.truetype(path, 190 * SS)
            break
        except OSError:
            continue
    if font is None:
        font = ImageFont.load_default()
    d.text((CARD_W / 2, CARD_H / 2 + 6 * SS), "?", font=font, anchor="mm", fill=(255, 255, 255, 255),
           stroke_width=10 * SS, stroke_fill=(40, 24, 90, 255))
    d.rounded_rectangle([0, 0, CARD_W - 1, CARD_H - 1], radius=RADIUS, outline=(255, 255, 255, 235), width=5 * SS)
    return img


def place(canvas, card, cx, cy, angle):
    rot = card.rotate(angle, resample=Image.BICUBIC, expand=True)
    canvas.alpha_composite(rot, (int(cx - rot.width / 2), int(cy - rot.height / 2)))


layer = Image.new("RGBA", (W, W), (0, 0, 0, 0))
back = card_back()
# 山札（3枚を少しずつずらして重ねる）
for k, (dx, dy) in enumerate(((22, 22), (11, 11), (0, 0))):
    shade = back.copy()
    if k < 2:
        dark = Image.new("RGBA", back.size, (10, 6, 30, 0))
        dark.putalpha(back.split()[3].point(lambda v: int(v * 0.35)))
        shade.alpha_composite(dark)
    place(layer, shade, 206 * SS + dx * SS, 300 * SS + dy * SS, 8)
# 引き抜かれた1枚（右上へ・右に傾ける）
front = card_front()
shadow = Image.new("RGBA", front.size, (8, 4, 24, 0))
shadow.putalpha(front.split()[3].point(lambda v: int(v * 0.45)))
place(layer, shadow.filter(ImageFilter.GaussianBlur(10 * SS)), 318 * SS, 222 * SS, -14)
place(layer, front, 306 * SS, 206 * SS, -14)

cards = layer.resize((S, S), Image.LANCZOS)

# ---- 外の光ときらめき ----
glow_mask = cards.split()[3].filter(ImageFilter.GaussianBlur(24))
glow = Image.new("RGBA", (S, S), (184, 155, 255, 0))
glow.putalpha(glow_mask.point(lambda v: int(v * 0.7)))
icon = Image.new("RGBA", (S, S), (0, 0, 0, 0))
icon.alpha_composite(glow)
icon.alpha_composite(cards)


def sparkle(dr, x, y, s, col):
    dr.polygon([(x, y - s * 1.9), (x + s * 0.3, y), (x, y + s * 1.9), (x - s * 0.3, y)], fill=col)
    dr.polygon([(x - s * 1.9, y), (x, y + s * 0.3), (x + s * 1.9, y), (x, y - s * 0.3)], fill=col)


dr = ImageDraw.Draw(icon)
sparkle(dr, S * 0.86, S * 0.13, 13, (255, 246, 220, 245))
sparkle(dr, S * 0.13, S * 0.22, 8, (240, 230, 255, 230))
sparkle(dr, S * 0.88, S * 0.62, 6, (230, 240, 255, 220))
icon.save(OUT)
print("書き出し:", OUT)

if PREVIEW:
    bg = Image.new("RGBA", (S + 360, S), (13, 18, 48, 255))
    bg.alpha_composite(icon, (0, 0))
    x = S + 20
    for size, yy in ((148, 20), (128, 200), (96, 360)):
        bg.alpha_composite(icon.resize((size, size), Image.LANCZOS), (x, yy))
    card = Image.new("RGBA", (180, 170), (9, 38, 24, 255))
    card.alpha_composite(icon.resize((148, 148), Image.LANCZOS), (16, 11))
    bg.alpha_composite(card, (S + 175, 20))
    bg.convert("RGB").save(PREVIEW)
