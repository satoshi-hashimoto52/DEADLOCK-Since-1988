#!/usr/bin/env python3
"""
Layout guide for the ADS sprite of the approved rifle (rifle_hip_approved.png).

Writes, into OUTPUT_DIR:
    ads_canvas_guide.png        the ADS canvas at full size (2400 x 1200) with screen frames, aim point, sight / muzzle
                                reference points, regions to draw / keep transparent, hand and sleeve areas. GUIDE ONLY.
    ads_canvas_guide_small.png  the same at 1/2 size (for reading)
    ads_screen_layout_*.png     planned look on the game screen (iPhone 16 Pro / 16:9 / 4:3): the current game screen
                                with the planned regions drawn over it as simple shapes (not art)

The numbers come from the game: the view-model camera (vertical FOV 50 deg at z = 1: half height tan 25 deg), the
reload / kick motion in WeaponView_ProtoRifle.asset, and the approved hip placement (rifle_hip_approved.placement.json)
for the dot size. The shapes are placeholders for positions, not the weapon's design.

Run by hand (needs Pillow):  python3 Tools/pixelart/make_ads_guide.py OUTPUT_DIR [GAME_SCREEN_DIR]
GAME_SCREEN_DIR: optional folder with game screenshots named <W>x<H>.png (without weapon) for the layout images.
"""
import json
import math
import os
import sys

from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
FONT = "/System/Library/Fonts/ヒラギノ角ゴシック W6.ttc"
HALF_H = math.tan(math.radians(25))  # view-model camera half height at z = 1

# ---- canvas (see README in the guide folder)
DOT = 4                       # canvas pixels per art dot (same as the approved hip image)
W, H = 2400, 1200             # 600 x 300 dots
AIM = (1200, 522)             # front-sight tip = pivot = game screen centre
SCREEN_PX = 2 * AIM[1]        # canvas pixels per full screen height (1044 = 261 dots)
PPU = SCREEN_PX / (2 * HALF_H)
MUZZLE = (1200, 622)          # flash reference (muzzle, may be hidden behind the front sight)


def font(size):
    return ImageFont.truetype(FONT, size) if os.path.exists(FONT) else None


def screen_rect(aspect):
    half_w = HALF_H * aspect * PPU
    return (AIM[0] - half_w, AIM[1] - SCREEN_PX / 2, AIM[0] + half_w, AIM[1] + SCREEN_PX / 2)


def regions():
    """Planned regions in canvas pixels (proposals: positions and rough extents, not the weapon's design)."""
    cx = AIM[0]
    return {
        # rear sight (near): two protective ears either side of the opening, a thin rim, a base below the sight line
        "rear_ears": [
            [(cx - 104, 440), (cx - 66, 440), (cx - 66, 600), (cx - 112, 600)],
            [(cx + 66, 440), (cx + 104, 440), (cx + 112, 600), (cx + 66, 600)],
        ],
        "rear_opening": (cx - 56, AIM[1] - 56, cx + 56, AIM[1] + 56),
        "rear_base": [(cx - 130, 600), (cx + 130, 600), (cx + 175, 760), (cx - 175, 760)],
        # front sight (far, small): post whose tip is the aim point, two small ears, a base on the barrel
        "front_parts": [
            [(cx - 5, AIM[1]), (cx + 5, AIM[1]), (cx + 5, 575), (cx - 5, 575)],
            [(cx - 26, 505), (cx - 17, 505), (cx - 17, 575), (cx - 26, 575)],
            [(cx + 17, 505), (cx + 26, 505), (cx + 26, 575), (cx + 17, 575)],
            [(cx - 38, 575), (cx + 38, 575), (cx + 46, 600), (cx - 46, 600)],
        ],
        # receiver / rail converging forward toward the front sight base
        "body": [(cx - 46, 600), (cx + 46, 600), (cx + 520, 1200), (cx - 520, 1200)],
        # hands / sleeves (proposal): left hand under the handguard, sleeve off the bottom-left; right hand mostly hidden
        "left_hand": [(760, 700), (1040, 650), (1110, 900), (980, 1200), (560, 1200)],
        "right_hand": [(1420, 960), (1700, 900), (1860, 1200), (1400, 1200)],
        # keep clear: the view above the sights (only sight parts may enter the area around the aim point)
        "clear": (cx - 330, 120, cx + 330, 500),
    }


def reload_margin(profile):
    """Lowest canvas point that reaches the screen's bottom corners during reload / kick (for the margin note)."""
    hold = profile.get("adsHoldOffset", (0.0, -0.3))
    drop = profile.get("reloadDrop", 0.1)
    roll = math.radians(profile.get("reloadRollDegrees", 14.0))
    worst = 0.0
    for aspect in (2622 / 1206, 16 / 9, 4 / 3):
        for sx in (-1, 1):
            p = (sx * HALF_H * aspect - 0.02, -HALF_H + drop)
            q = (p[0] - hold[0], p[1] - hold[1])
            x = q[0] * math.cos(roll) + q[1] * math.sin(roll) + hold[0]
            y = -q[0] * math.sin(roll) + q[1] * math.cos(roll) + hold[1]
            worst = min(worst, y)
    return worst


def read_profile():
    import re
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


def guide(out):
    img = Image.new("RGBA", (W, H), (250, 250, 250, 255))
    d = ImageDraw.Draw(img, "RGBA")
    # dot grid every 10 dots
    for x in range(0, W + 1, DOT * 10):
        d.line([x, 0, x, H], fill=(225, 225, 225, 255), width=1)
    for y in range(0, H + 1, DOT * 10):
        d.line([0, y, W, y], fill=(225, 225, 225, 255), width=1)
    r = regions()
    # margin below the screen (reload / kick)
    d.rectangle([0, SCREEN_PX, W, H], fill=(255, 220, 160, 110))
    # clear zone
    d.rectangle(r["clear"], fill=(120, 200, 255, 70), outline=(30, 120, 220, 255), width=3)
    # body / sights / hands (placeholders)
    d.polygon(r["body"], fill=(150, 150, 150, 90), outline=(80, 80, 80, 255))
    d.polygon(r["left_hand"], fill=(160, 100, 60, 90), outline=(120, 70, 40, 255))
    d.polygon(r["right_hand"], fill=(160, 100, 60, 60), outline=(120, 70, 40, 255))
    d.polygon(r["rear_base"], fill=(60, 60, 70, 90), outline=(40, 40, 50, 255))
    for ear in r["rear_ears"]:
        d.polygon(ear, fill=(60, 60, 70, 110), outline=(40, 40, 50, 255))
    d.ellipse(r["rear_opening"], outline=(40, 40, 50, 255), width=12)
    for part in r["front_parts"]:
        d.polygon(part, fill=(60, 60, 70, 170), outline=(40, 40, 50, 255))
    # screen frames
    frames = [("iPhone 16 Pro（2622×1206、約2.17:1）", 2622 / 1206, (200, 0, 0)), ("16:9", 16 / 9, (0, 130, 0)), ("iPad 4:3", 4 / 3, (120, 0, 160))]
    f22 = font(22)
    for i, (label, aspect, color) in enumerate(frames):
        x0, y0, x1, y1 = screen_rect(aspect)
        d.rectangle([x0, y0, x1, y1], outline=color + (255,), width=3)
        d.text((x0 + 8, y1 - 34 - 30 * i), f"画面の範囲：{label}  x {x0:.0f}〜{x1:.0f}", fill=color + (255,), font=f22)
    # aim point / muzzle
    for (x, y), color, label in ((AIM, (220, 0, 0), f"狙点＝ピボット＝画面中央 ({AIM[0]}, {AIM[1]})"), (MUZZLE, (230, 120, 0), f"銃口・発砲エフェクトの基準点 ({MUZZLE[0]}, {MUZZLE[1]})")):
        d.line([x - 40, y, x + 40, y], fill=color + (255,), width=3)
        d.line([x, y - 40, x, y + 40], fill=color + (255,), width=3)
        d.text((x + 70, y - 14), label, fill=color + (255,), font=f22, stroke_width=3, stroke_fill=(255, 255, 255))
    labels = [
        ((1200 + 340, 130), "中央の視界：照門・フロントサイト以外を描かない（透明）", (30, 120, 220)),
        ((1200 + 120, 430), "照門（手前）：左右の耳と開口。開口の中心を狙点に", (40, 40, 50)),
        ((1200 + 60, 545), "フロントサイト（奥・小）：先端を狙点に", (40, 40, 50)),
        ((1200 + 300, 800), "機関部・レール：前方（狙点）へ収束", (80, 80, 80)),
        ((180, 760), "左手・袖（提案）：下端の外へ続く", (120, 70, 40)),
        ((1720, 860), "右手（提案）：大部分は機関部の下に隠れる", (120, 70, 40)),
        ((20, SCREEN_PX + 14), f"画面の下端（y={SCREEN_PX}）より下：リロード・反動で見える余白。手・袖・機関部をキャンバスの下端まで描き切る", (160, 90, 0)),
    ]
    for (x, y), text, color in labels:
        d.text((x, y), text, fill=color + (255,), font=f22, stroke_width=3, stroke_fill=(255, 255, 255))
    d.text((20, 16), "ADS構図ガイド（配置を伝える資料。完成アートではない。線・文字・色面は完成素材に含めない）", fill=(0, 0, 0, 255), font=font(30))
    d.text((20, 56), f"キャンバス {W}×{H}px（{W // DOT}×{H // DOT}ドット、2:1）、1ドット＝{DOT}px、原点は左上（x右・y下）。灰色の格子＝10ドット", fill=(0, 0, 0, 255), font=f22)
    img.convert("RGB").save(os.path.join(out, "ads_canvas_guide.png"))
    img.convert("RGB").resize((W // 2, H // 2), Image.LANCZOS).save(os.path.join(out, "ads_canvas_guide_small.png"))


def layouts(out, screens):
    r = regions()
    for name in sorted(os.listdir(screens)):
        if not name.endswith(".png"):
            continue
        base = Image.open(os.path.join(screens, name)).convert("RGBA")
        sw, sh = base.size
        scale = sh / SCREEN_PX  # canvas px -> screen px
        ox, oy = sw / 2 - AIM[0] * scale, sh / 2 - AIM[1] * scale

        def tr(p):
            return (ox + p[0] * scale, oy + p[1] * scale)

        over = Image.new("RGBA", base.size, (0, 0, 0, 0))
        d = ImageDraw.Draw(over, "RGBA")
        d.polygon([tr(p) for p in r["body"]], fill=(120, 120, 120, 150))
        d.polygon([tr(p) for p in r["left_hand"]], fill=(150, 90, 50, 150))
        d.polygon([tr(p) for p in r["right_hand"]], fill=(150, 90, 50, 110))
        d.polygon([tr(p) for p in r["rear_base"]], fill=(50, 50, 60, 170))
        for ear in r["rear_ears"]:
            d.polygon([tr(p) for p in ear], fill=(50, 50, 60, 190))
        e = r["rear_opening"]
        a, b = tr((e[0], e[1])), tr((e[2], e[3]))
        d.ellipse([a, b], outline=(50, 50, 60, 220), width=max(2, int(12 * scale)))
        for part in r["front_parts"]:
            d.polygon([tr(p) for p in part], fill=(40, 40, 50, 230))
        c = r["clear"]
        a, b = tr((c[0], c[1])), tr((c[2], c[3]))
        d.rectangle([a, b], outline=(60, 160, 255, 255), width=3)
        mx, my = tr(MUZZLE)
        d.ellipse([mx - 10, my - 10, mx + 10, my + 10], outline=(255, 140, 0, 255), width=3)
        img = Image.alpha_composite(base, over)
        dd = ImageDraw.Draw(img)
        dd.text((20, sh - 60), f"ADS配置予定図（{sw}×{sh}）：灰・茶の色面は位置の目安で、武器の絵ではない。青枠＝中央の視界", fill=(255, 255, 255), font=font(int(28 * sh / 1206)), stroke_width=3, stroke_fill=(0, 0, 0))
        img.convert("RGB").save(os.path.join(out, f"ads_screen_layout_{name}"))


def main():
    out = sys.argv[1]
    os.makedirs(out, exist_ok=True)
    guide(out)
    if len(sys.argv) > 2:
        layouts(out, sys.argv[2])
    worst = reload_margin(read_profile())
    hip = json.load(open(os.path.join(ROOT, "Assets", "Game", "Art", "Weapons", "Rifle", "rifle_hip_approved.placement.json")))
    print(f"canvas {W}x{H}, aim {AIM}, muzzle {MUZZLE}, ppu {PPU:.2f}, ads muzzle offset (0, {-(MUZZLE[1] - AIM[1]) / PPU:.4f})")
    print(f"reload/kick lowest needed point: {worst:.4f} units = {-worst * PPU:.0f} px below the aim point (canvas has {H - AIM[1]})")
    print(f"hip: {hip['screenHeightPixels']} image px per screen height; ads: {SCREEN_PX}")


if __name__ == "__main__":
    main()
