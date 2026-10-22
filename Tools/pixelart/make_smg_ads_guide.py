#!/usr/bin/env python3
"""
Layout guide for the ADS sprite of the SMG (design base: Assets/Game/Art/Weapons/SMG/smg_hip_candidate_v2.png).

Same game rules as the rifle ADS guide (make_ads_guide.py): view-model camera with a vertical FOV of 50 deg at z = 1,
the aim point (front-sight tip) on the screen centre, the reload / kick motion of the current weapon view profile, and
the hip dot size (1 dot = 4 canvas px). Only the SMG's parts differ (short, boxy receiver, front-sight fork close to the
rear sight, left hand under the front block, right hand on the pistol grip, folded wire stock on the right side).

Writes, into OUTPUT_DIR:
    smg_ads_canvas_guide.png        the ADS canvas at full size (2400 x 1200) with screen frames, aim point, sight /
                                    muzzle reference points, regions to draw / keep clear, hands and sleeves. GUIDE ONLY.
    smg_ads_canvas_guide_small.png  the same at 1/2 size
    smg_ads_screen_layout_*.png     planned look on the game screen (iPhone 16 Pro / 16:9 / 4:3), simple shapes only
    smg_parts_reference.png         the hip candidate with its parts named (what the ADS must keep recognisable)

Run by hand (needs Pillow):  python3 Tools/pixelart/make_smg_ads_guide.py OUTPUT_DIR [GAME_SCREEN_DIR]
GAME_SCREEN_DIR: optional folder with game screenshots named <W>x<H>.png (without weapon) for the layout images.
"""
import math
import os
import re
import sys

from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
FONT = "/System/Library/Fonts/ヒラギノ角ゴシック W6.ttc"
HALF_H = math.tan(math.radians(25))  # view-model camera half height at z = 1
HIP = os.path.join(ROOT, "Assets", "Game", "Art", "Weapons", "SMG", "smg_hip_candidate_v2.png")

DOT = 4                       # canvas pixels per art dot (same as the hip images)
W, H = 2400, 1200             # 600 x 300 dots
AIM = (1200, 522)             # front-sight post tip = pivot = game screen centre
SCREEN_PX = 2 * AIM[1]        # canvas pixels per full screen height (1044, like the hip's 1045)
PPU = SCREEN_PX / (2 * HALF_H)
MUZZLE = (1200, 622)          # flash reference (the barrel nut, mostly hidden below the front sight)


def font(size):
    return ImageFont.truetype(FONT, size) if os.path.exists(FONT) else None


def screen_rect(aspect):
    half_w = HALF_H * aspect * PPU
    return (AIM[0] - half_w, AIM[1] - SCREEN_PX / 2, AIM[0] + half_w, AIM[1] + SCREEN_PX / 2)


def regions():
    """Planned regions in canvas pixels (proposals: positions and rough extents, not the weapon's design)."""
    cx = AIM[0]
    return {
        # rear sight (near): two ears either side of the opening on a block, as on the hip image
        "rear_ears": [
            [(cx - 112, 430), (cx - 70, 430), (cx - 70, 600), (cx - 120, 600)],
            [(cx + 70, 430), (cx + 112, 430), (cx + 120, 600), (cx + 70, 600)],
        ],
        "rear_opening": (cx - 58, AIM[1] - 58, cx + 58, AIM[1] + 58),
        "rear_base": [(cx - 140, 600), (cx + 140, 600), (cx + 190, 740), (cx - 190, 740)],
        # front sight (far): a fork of two ears with a centre post (post tip = aim point); the SMG is short, so the
        # front sight is a little larger and closer to the rear sight than on the rifle
        "front_parts": [
            [(cx - 6, AIM[1]), (cx + 6, AIM[1]), (cx + 6, 578), (cx - 6, 578)],
            [(cx - 36, 496), (cx - 22, 496), (cx - 22, 578), (cx - 36, 578)],
            [(cx + 22, 496), (cx + 36, 496), (cx + 36, 578), (cx + 22, 578)],
            [(cx - 48, 578), (cx + 48, 578), (cx + 58, 608), (cx - 58, 608)],
        ],
        # boxy receiver seen from behind: flat top, converging toward the front block under the front sight
        "body": [(cx - 58, 608), (cx + 58, 608), (cx + 440, 1200), (cx - 440, 1200)],
        # left hand under the front block (lower left), sleeve off the bottom edge
        "left_hand": [(700, 720), (1010, 640), (1120, 860), (1020, 1200), (520, 1200)],
        # right hand on the pistol grip below the rear of the receiver (lower right), sleeve off the bottom edge
        "right_hand": [(1380, 900), (1720, 860), (1900, 1200), (1360, 1200)],
        # folded wire stock along the right side of the receiver (optional, thin)
        "stock": [(1520, 760), (1600, 760), (1700, 1200), (1630, 1200)],
        # keep clear: the view above the sights (only sight parts may enter the area around the aim point)
        "clear": (cx - 330, 120, cx + 330, 495),
    }


def read_profile():
    values = {}
    with open(os.path.join(ROOT, "Assets", "Game", "Data", "WeaponView_ProtoRifle.asset"), encoding="utf-8") as f:
        for line in f:
            m = re.match(r"\s+(\w+): \{x: ([-\d.e]+), y: ([-\d.e]+)\}", line)
            if m:
                values[m.group(1)] = (float(m.group(2)), float(m.group(3)))
                continue
            m = re.match(r"\s+(\w+): ([-\d.e]+)\s*$", line)
            if m:
                values[m.group(1)] = float(m.group(2))
    return values


def reload_margin(profile):
    """Lowest canvas point that reaches the screen's bottom corners during reload / kick (same as the rifle guide)."""
    hold = profile.get("adsHoldOffset", (0.0, -0.3))
    drop = profile.get("reloadDrop", 0.1)
    roll = math.radians(profile.get("reloadRollDegrees", 14.0))
    worst = 0.0
    for aspect in (2622 / 1206, 16 / 9, 4 / 3):
        for sx in (-1, 1):
            p = (sx * HALF_H * aspect - 0.02, -HALF_H + drop)
            q = (p[0] - hold[0], p[1] - hold[1])
            y = -q[0] * math.sin(roll) + q[1] * math.cos(roll) + hold[1]
            worst = min(worst, y)
    return worst


def guide(out):
    img = Image.new("RGBA", (W, H), (250, 250, 250, 255))
    d = ImageDraw.Draw(img, "RGBA")
    for x in range(0, W + 1, DOT * 10):
        d.line([x, 0, x, H], fill=(225, 225, 225, 255), width=1)
    for y in range(0, H + 1, DOT * 10):
        d.line([0, y, W, y], fill=(225, 225, 225, 255), width=1)
    r = regions()
    d.rectangle([0, SCREEN_PX, W, H], fill=(255, 220, 160, 110))
    d.rectangle(r["clear"], fill=(120, 200, 255, 70), outline=(30, 120, 220, 255), width=3)
    d.polygon(r["body"], fill=(70, 70, 76, 90), outline=(40, 40, 46, 255))
    d.polygon(r["stock"], fill=(120, 120, 120, 60), outline=(90, 90, 90, 255))
    d.polygon(r["left_hand"], fill=(160, 100, 60, 90), outline=(120, 70, 40, 255))
    d.polygon(r["right_hand"], fill=(160, 100, 60, 70), outline=(120, 70, 40, 255))
    d.polygon(r["rear_base"], fill=(50, 50, 56, 110), outline=(30, 30, 36, 255))
    for ear in r["rear_ears"]:
        d.polygon(ear, fill=(50, 50, 56, 130), outline=(30, 30, 36, 255))
    d.ellipse(r["rear_opening"], outline=(30, 30, 36, 255), width=12)
    for part in r["front_parts"]:
        d.polygon(part, fill=(40, 40, 46, 180), outline=(30, 30, 36, 255))
    frames = [("iPhone 16 Pro（2622×1206、約2.17:1）", 2622 / 1206, (200, 0, 0)), ("16:9", 16 / 9, (0, 130, 0)), ("iPad 4:3", 4 / 3, (120, 0, 160))]
    f22 = font(22)
    for i, (label, aspect, color) in enumerate(frames):
        x0, y0, x1, y1 = screen_rect(aspect)
        d.rectangle([x0, y0, x1, y1], outline=color + (255,), width=3)
        d.text((x0 + 8, y1 - 34 - 30 * i), f"画面の範囲：{label}  x {x0:.0f}〜{x1:.0f}", fill=color + (255,), font=f22)
    for (x, y), color, label in ((AIM, (220, 0, 0), f"狙点＝照星の柱の先端＝画面中央 ({AIM[0]}, {AIM[1]})"), (MUZZLE, (230, 120, 0), f"銃口・発砲エフェクトの基準点 ({MUZZLE[0]}, {MUZZLE[1]})")):
        d.line([x - 40, y, x + 40, y], fill=color + (255,), width=3)
        d.line([x, y - 40, x, y + 40], fill=color + (255,), width=3)
        d.text((x + 70, y - 14), label, fill=color + (255,), font=f22, stroke_width=3, stroke_fill=(255, 255, 255))
    labels = [
        ((1200 + 340, 130), "中央の視界：照門・照星以外を描かない（透明）", (30, 120, 220)),
        ((1200 + 130, 420), "照門（手前）：左右の耳と開口。開口の中心を狙点に", (30, 30, 36)),
        ((1200 + 70, 470), "照星（奥）：左右の耳＋中央の柱。柱の先端を狙点に", (30, 30, 36)),
        ((1200 + 230, 700), "機関部：四角い箱形。上面は平ら、前（照星）へ収束", (40, 40, 46)),
        ((1640, 760), "折りたたみワイヤー銃床（任意・細く）", (90, 90, 90)),
        ((150, 700), "左手・左袖：前部ブロックを下から握る。袖は下端の外へ", (120, 70, 40)),
        ((1700, 830), "右手・右袖：グリップ。大部分は機関部の下", (120, 70, 40)),
        ((20, SCREEN_PX + 14), f"画面の下端（y={SCREEN_PX}）より下：リロード・反動・ADS移行で見える余白。手・袖・機関部をキャンバスの下端まで描き切る", (160, 90, 0)),
    ]
    for (x, y), text, color in labels:
        d.text((x, y), text, fill=color + (255,), font=f22, stroke_width=3, stroke_fill=(255, 255, 255))
    d.text((20, 16), "SMG ADS構図ガイド（配置を伝える資料。完成アートではない。線・文字・色面・格子は完成素材に含めない）", fill=(0, 0, 0, 255), font=font(30))
    d.text((20, 56), f"キャンバス {W}×{H}px（{W // DOT}×{H // DOT}ドット、2:1）、1ドット＝{DOT}px、原点は左上（x右・y下）。灰色の格子＝10ドット", fill=(0, 0, 0, 255), font=f22)
    img.convert("RGB").save(os.path.join(out, "smg_ads_canvas_guide.png"))
    img.convert("RGB").resize((W // 2, H // 2), Image.LANCZOS).save(os.path.join(out, "smg_ads_canvas_guide_small.png"))


def layouts(out, screens):
    r = regions()
    for name in sorted(os.listdir(screens)):
        if not name.endswith(".png"):
            continue
        base = Image.open(os.path.join(screens, name)).convert("RGBA")
        sw, sh = base.size
        scale = sh / SCREEN_PX
        ox, oy = sw / 2 - AIM[0] * scale, sh / 2 - AIM[1] * scale

        def tr(p):
            return (ox + p[0] * scale, oy + p[1] * scale)

        over = Image.new("RGBA", base.size, (0, 0, 0, 0))
        d = ImageDraw.Draw(over, "RGBA")
        d.polygon([tr(p) for p in r["body"]], fill=(50, 50, 56, 170))
        d.polygon([tr(p) for p in r["stock"]], fill=(110, 110, 110, 140))
        d.polygon([tr(p) for p in r["left_hand"]], fill=(150, 90, 50, 160))
        d.polygon([tr(p) for p in r["right_hand"]], fill=(150, 90, 50, 120))
        d.polygon([tr(p) for p in r["rear_base"]], fill=(40, 40, 46, 190))
        for ear in r["rear_ears"]:
            d.polygon([tr(p) for p in ear], fill=(40, 40, 46, 210))
        e = r["rear_opening"]
        a, b = tr((e[0], e[1])), tr((e[2], e[3]))
        d.ellipse([a, b], outline=(40, 40, 46, 230), width=max(2, int(12 * scale)))
        for part in r["front_parts"]:
            d.polygon([tr(p) for p in part], fill=(30, 30, 36, 240))
        c = r["clear"]
        a, b = tr((c[0], c[1])), tr((c[2], c[3]))
        d.rectangle([a, b], outline=(60, 160, 255, 255), width=3)
        mx, my = tr(MUZZLE)
        d.ellipse([mx - 10, my - 10, mx + 10, my + 10], outline=(255, 140, 0, 255), width=3)
        img = Image.alpha_composite(base, over)
        dd = ImageDraw.Draw(img)
        dd.text((20, sh - 60), f"SMG ADS配置予定図（{sw}×{sh}）：色面は位置の目安で、武器の絵ではない。青枠＝中央の視界", fill=(255, 255, 255), font=font(int(28 * sh / 1206)), stroke_width=3, stroke_fill=(0, 0, 0))
        img.convert("RGB").save(os.path.join(out, f"smg_ads_screen_layout_{name}"))


def parts_reference(out):
    """The hip candidate with its parts named: what the ADS has to keep recognisable (same gun)."""
    hip = Image.open(HIP).convert("RGBA")
    bg = Image.new("RGBA", hip.size, (205, 205, 205, 255))
    bg.alpha_composite(hip)
    box = (880, 330, 1672, 941)
    s = 2
    crop = bg.crop(box).resize(((box[2] - box[0]) * s, (box[3] - box[1]) * s), Image.NEAREST).convert("RGB")
    img = Image.new("RGB", (crop.width + 760, crop.height + 70), (30, 30, 34))
    img.paste(crop, (10, 60))
    d = ImageDraw.Draw(img)
    d.text((10, 14), "SMG候補v2（腰だめ）の部位：ADSでも同じ銃に見えるように保つ特徴（2倍、最近傍）", font=font(26), fill=(240, 240, 240))
    parts = [
        (985, 430, "1 銃口・銃身の先のナット（固定色）"),
        (1065, 400, "2 照星：左右2本の耳（中央の柱は推定）"),
        (1080, 478, "3 前部ブロック（外装）"),
        (1180, 520, "4 機関部：箱形、側面に2本の溝（外装）"),
        (1290, 495, "5 照門：左右2本の耳と開口（固定色）"),
        (1238, 630, "6 砂色の縦長の枠（固定色の差し色）"),
        (1380, 610, "7 後部ブロック・丸い部品（外装）"),
        (1500, 712, "8 折りたたみワイヤー銃床（固定色）"),
        (1120, 820, "9 弾倉：グリップの前（固定色）"),
        (1260, 880, "10 グリップ・引き金（固定色、右手で握る）"),
        (1010, 600, "11 左手：前部ブロックを下から握る"),
        (980, 700, "12 手首の暗い帯・オリーブの迷彩袖"),
    ]
    tx = crop.width + 30
    for i, (x, y, label) in enumerate(parts):
        px, py = 10 + (x - box[0]) * s, 60 + (y - box[1]) * s
        ty = 70 + i * 52
        d.ellipse([px - 9, py - 9, px + 9, py + 9], outline=(255, 210, 0), width=4)
        d.line([px, py, tx - 8, ty + 14], fill=(255, 210, 0), width=2)
        d.text((tx, ty), label, font=font(24), fill=(240, 240, 240))
    img.save(os.path.join(out, "smg_parts_reference.png"))


def main():
    out = sys.argv[1]
    os.makedirs(out, exist_ok=True)
    guide(out)
    parts_reference(out)
    if len(sys.argv) > 2:
        layouts(out, sys.argv[2])
    worst = reload_margin(read_profile())
    print(f"canvas {W}x{H}, aim {AIM}, muzzle {MUZZLE}, ppu {PPU:.2f}, ads muzzle offset (0, {-(MUZZLE[1] - AIM[1]) / PPU:.4f})")
    print(f"reload/kick lowest needed point: {worst:.4f} units = {-worst * PPU:.0f} px below the aim point (canvas has {H - AIM[1]})")
    for label, aspect in (("iPhone16Pro", 2622 / 1206), ("16:9", 16 / 9), ("4:3", 4 / 3)):
        x0, _, x1, _ = screen_rect(aspect)
        print(f"screen x range {label}: {x0:.0f}..{x1:.0f}")


if __name__ == "__main__":
    main()
