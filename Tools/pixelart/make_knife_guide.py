#!/usr/bin/env python3
"""
Layout guide for a new first-person knife + gloved hand image (for an external artist / image-generation brief).

The numbers come from the game: the knife's view profile (Assets/Game/Data/MeleeView_ProtoKnife.asset: grip position,
right-edge follow, windup / strike offsets, rolls, strike scale, lowered distance), the swing timing shape
(MeleeViewModel: smoothstep per phase) and the view-model camera (vertical FOV 50 deg, z = 1). The dot size matches the
approved rifle hip (rifle_hip_approved.placement.json: screenHeightPixels).

Writes into OUTPUT_DIR:
    knife_canvas_guide.png        canvas at full size: grip (pivot), blade direction / tip, fist, wrist, arm band, the
                                  area that is ever on screen (union of all poses on iPhone 16 Pro / 16:9 / 4:3) and
                                  the area that is never on screen. GUIDE ONLY (lines, text and fills are not art).
    knife_canvas_guide_small.png  the same at 1/2
    knife_screen_layout_<WxH>.png the placeholder shape moved like the game moves the sprite, at idle, windup end, hit,
                                  strike end, recovery and switch, on each screen size
Prints the check that the arm's cut edges (canvas right / bottom edges) never come into view.

Run by hand (needs Pillow):  python3 Tools/pixelart/make_knife_guide.py OUTPUT_DIR
"""
import json
import math
import os
import re
import sys

from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
FONT = "/System/Library/Fonts/ヒラギノ角ゴシック W6.ttc"
HALF = math.tan(math.radians(25))
SCREENS = [("iPhone16Pro", 2622, 1206), ("16x9", 1920, 1080), ("iPad", 2048, 1536)]

# ---- proposed canvas (see the README); pixel coordinates from the top-left
W, H = 2400, 1500
GRIP = (640, 600)                  # pivot: centre of the fist on the handle
DOT = 4                            # canvas pixels per art dot (same as the approved rifle)


def font(size):
    return ImageFont.truetype(FONT, size) if os.path.exists(FONT) else ImageFont.load_default()


def profile():
    values = {}
    with open(os.path.join(ROOT, "Assets", "Game", "Data", "MeleeView_ProtoKnife.asset"), encoding="utf-8") as f:
        for line in f:
            m = re.match(r"\s+(\w+): \{x: ([-\d.e]+), y: ([-\d.e]+)\}", line)
            if m:
                values[m.group(1)] = (float(m.group(2)), float(m.group(3)))
                continue
            m = re.match(r"\s+(\w+): ([-\d.e]+)\s*$", line)
            if m:
                values[m.group(1)] = float(m.group(2))
    return values


def ppu():
    with open(os.path.join(ROOT, "Assets", "Game", "Art", "Weapons", "Rifle", "rifle_hip_approved.placement.json"), encoding="utf-8") as f:
        return json.load(f)["screenHeightPixels"] / (2 * HALF)


def current_blade():
    """Blade tip relative to the grip in units, from the current knife sprite (keeps the hit visual where it is)."""
    with open(os.path.join(ROOT, "Assets", "Game", "Art", "Weapons", "pixel_weapons.json"), encoding="utf-8") as f:
        info = json.load(f)
    knife = next(s for s in info["sprites"] if s["name"] == "Spr_PixelKnife")
    w, h = knife["size"]
    gx, gy = knife["pivot"][0] * w, (1 - knife["pivot"][1]) * h
    tip = (8, 8)  # blade tip of Spr_PixelKnife (draw_weapons.py)
    return ((tip[0] - gx) / info["pixelsPerUnit"], (gy - tip[1]) / info["pixelsPerUnit"])


def ease(x):
    return x * x * (3 - 2 * x)


def poses(p):
    wo, so = p["windupOffset"], p["strikeOffset"]
    out = []
    for i in range(11):
        k = ease(i / 10)
        out.append(("windup", i / 10, (wo[0] * k, wo[1] * k), p["windupRoll"] * k, 1.0, 0.0))
    for i in range(11):
        k = ease(i / 10)
        out.append(("active", i / 10, (wo[0] + (so[0] - wo[0]) * k, wo[1] + (so[1] - wo[1]) * k),
                    p["windupRoll"] + (p["strikeRoll"] - p["windupRoll"]) * k, 1 + (p["strikeScale"] - 1) * k, 0.0))
    for i in range(11):
        k = ease(i / 10)
        out.append(("recovery", i / 10, (so[0] * (1 - k), so[1] * (1 - k)), p["strikeRoll"] * (1 - k),
                    p["strikeScale"] + (1 - p["strikeScale"]) * k, 0.0))
    for i in range(11):
        out.append(("switch", i / 10, (0.0, 0.0), 0.0, 1.0, -p["lowered"] * (1 - ease(i / 10))))
    return out


def to_screen(pt, pose, aspect, p, unit):
    _, _, off, roll, scale, low = pose
    edge = (aspect - p["referenceAspect"]) * HALF * p["rightEdgeFollow"]
    x = (pt[0] - GRIP[0]) / unit * scale
    y = (GRIP[1] - pt[1]) / unit * scale
    r = math.radians(roll)
    X, Y = x * math.cos(r) - y * math.sin(r), x * math.sin(r) + y * math.cos(r)
    return p["gripPosition"][0] + edge + off[0] + X, p["gripPosition"][1] + off[1] + low + Y


def to_canvas(sx, sy, pose, aspect, p, unit):
    _, _, off, roll, scale, low = pose
    edge = (aspect - p["referenceAspect"]) * HALF * p["rightEdgeFollow"]
    X, Y = sx - (p["gripPosition"][0] + edge + off[0]), sy - (p["gripPosition"][1] + off[1] + low)
    r = math.radians(-roll)
    x, y = X * math.cos(r) - Y * math.sin(r), X * math.sin(r) + Y * math.cos(r)
    return GRIP[0] + x / scale * unit, GRIP[1] - y / scale * unit


def shapes(unit, blade):
    tip = (GRIP[0] + blade[0] * unit, GRIP[1] - blade[1] * unit)
    d = (tip[0] - GRIP[0], tip[1] - GRIP[1])
    L = math.hypot(*d)
    ux, uy = d[0] / L, d[1] / L
    nx, ny = -uy, ux
    guard = (GRIP[0] + ux * 150, GRIP[1] + uy * 150)
    pommel = (GRIP[0] - ux * 140, GRIP[1] - uy * 140)
    blade_poly = [(guard[0] + nx * 30, guard[1] + ny * 30), (tip[0], tip[1]), (guard[0] - nx * 30, guard[1] - ny * 30)]
    handle_poly = [(guard[0] + nx * 22, guard[1] + ny * 22), (guard[0] - nx * 22, guard[1] - ny * 22),
                   (pommel[0] - nx * 22, pommel[1] - ny * 22), (pommel[0] + nx * 22, pommel[1] + ny * 22)]
    fist = (GRIP[0] - 150, GRIP[1] - 140, GRIP[0] + 170, GRIP[1] + 160)
    wrist = (GRIP[0] + 130, GRIP[1] + 170)
    band = [(wrist[0] - 150, wrist[1] - 20), (wrist[0] + 40, wrist[1] - 140), (wrist[0] + 110, wrist[1] - 40), (wrist[0] - 80, wrist[1] + 90)]
    # forearm: from the band toward the bottom-right corner, a little wider toward the viewer (perspective)
    arm = [(wrist[0] - 80, wrist[1] + 90), (wrist[0] + 110, wrist[1] - 40), (W, H - 360), (W, H), (W - 520, H)]
    return dict(tip=tip, guard=guard, pommel=pommel, blade=blade_poly, handle=handle_poly, fist=fist, wrist=wrist, band=band, arm=arm)


def main():
    out = sys.argv[1]
    os.makedirs(out, exist_ok=True)
    p = profile()
    unit = ppu()
    blade = current_blade()
    sh = shapes(unit, blade)
    all_poses = poses(p)
    aspects = [w / h for _, w, h in SCREENS]

    # ever-visible canvas area (sampled every 8 px)
    visible = set()
    step = 8
    for y in range(0, H, step):
        for x in range(0, W, step):
            for a in aspects:
                hw = HALF * a
                for pose in all_poses:
                    sx, sy = to_screen((x, y), pose, a, p, unit)
                    if -hw <= sx <= hw and -HALF <= sy <= HALF:
                        visible.add((x, y))
                        break
                if (x, y) in visible:
                    break
    # cut-edge check: canvas right / bottom edges inside the arm band
    exit_pts = [(x, H) for x in range(int(sh["arm"][4][0]), W + 1, 10)] + [(W, y) for y in range(int(sh["arm"][2][1]), H + 1, 10)]
    # the sleeve's long side edges are outlines of the arm, not cut edges; only the ends at the canvas border must stay hidden
    shown = [(a, pose[0], pose[1]) for a in aspects for pose in all_poses
             if any((lambda s: -HALF * a < s[0] < HALF * a and -HALF < s[1] < HALF)(to_screen(q, pose, a, p, unit)) for q in exit_pts)]

    # ---- canvas guide
    img = Image.new("RGBA", (W, H), (250, 250, 250, 255))
    d = ImageDraw.Draw(img, "RGBA")
    for x in range(0, W + 1, DOT * 10):
        d.line([x, 0, x, H], fill=(228, 228, 228, 255))
    for y in range(0, H + 1, DOT * 10):
        d.line([0, y, W, y], fill=(228, 228, 228, 255))
    for (x, y) in visible:
        d.rectangle([x, y, x + step - 1, y + step - 1], fill=(120, 200, 255, 60))
    d.polygon(sh["arm"], fill=(110, 120, 60, 120), outline=(70, 80, 30, 255))
    d.polygon(sh["band"], fill=(50, 50, 55, 150), outline=(30, 30, 30, 255))
    d.ellipse(sh["fist"], fill=(150, 90, 50, 130), outline=(110, 60, 30, 255), width=3)
    d.polygon(sh["handle"], fill=(40, 40, 40, 170))
    d.polygon(sh["blade"], fill=(160, 170, 185, 170), outline=(90, 100, 110, 255))
    f22 = font(26)
    def mark(pt, color, label, dx=24, dy=-14):
        x, y = pt
        d.line([x - 30, y, x + 30, y], fill=color + (255,), width=4)
        d.line([x, y - 30, x, y + 30], fill=color + (255,), width=4)
        d.text((x + dx, y + dy), label, fill=color + (255,), font=f22, stroke_width=3, stroke_fill=(255, 255, 255))
    mark(GRIP, (220, 0, 0), f"握りの中心＝ピボット ({GRIP[0]}, {GRIP[1]})")
    mark(sh["tip"], (40, 80, 200), f"刃先 ({sh['tip'][0]:.0f}, {sh['tip'][1]:.0f})")
    mark(sh["wrist"], (20, 120, 40), f"手首・帯 ({sh['wrist'][0]:.0f}, {sh['wrist'][1]:.0f}) 付近", dy=10)
    d.text((20, 14), "ナイフと手 構図ガイド（配置を伝える資料。完成アートではない。線・文字・色面は素材に含めない）", fill=(0, 0, 0, 255), font=font(34))
    d.text((20, 60), f"キャンバス {W}×{H}px（{W // DOT}×{H // DOT}ドット）・1ドット＝{DOT}px・原点は左上。水色＝どれかの動き・画面で画面内に入る範囲（ここは完成度高く描く）。白＝画面に出ない範囲", fill=(0, 0, 0, 255), font=f22)
    d.text((W - 1080, H - 150), "袖（オリーブ迷彩）は手首からキャンバスの右端・下端まで\n途切れずに描き切る（切り口は画面に出ない）", fill=(60, 70, 20, 255), font=f22, stroke_width=3, stroke_fill=(255, 255, 255))
    img.convert("RGB").save(os.path.join(out, "knife_canvas_guide.png"))
    img.convert("RGB").resize((W // 2, H // 2), Image.LANCZOS).save(os.path.join(out, "knife_canvas_guide_small.png"))

    # ---- on-screen layout per phase
    picks = [("待機", ("windup", 0.0)), ("振りかぶり終わり", ("windup", 1.0)), ("当たる瞬間（命中時間帯の中央）", ("active", 0.5)),
             ("振り抜き終わり", ("active", 1.0)), ("戻り途中", ("recovery", 0.5)), ("持ち替え途中", ("switch", 0.5))]
    for name, sw, sh_ in SCREENS:
        a = sw / sh_
        tw = 640
        th = int(tw * sh_ / sw)
        tiles = []
        for label, key in picks:
            pose = next(q for q in all_poses if (q[0], round(q[1], 1)) == key)
            im = Image.new("RGB", (sw, sh_), (52, 54, 62))
            dd = ImageDraw.Draw(im, "RGBA")
            def S(pt):
                x, y = to_screen(pt, pose, a, p, unit)
                return ((x / (HALF * a) + 1) / 2 * sw, (1 - (y / HALF + 1) / 2) * sh_)
            dd.polygon([S(q) for q in sh["arm"]], fill=(110, 120, 60, 255))
            dd.polygon([S(q) for q in sh["band"]], fill=(45, 45, 50, 255))
            fx0, fy0, fx1, fy1 = sh["fist"]
            fist_pts = [(fx0 + (fx1 - fx0) * (0.5 + 0.5 * math.cos(t)), fy0 + (fy1 - fy0) * (0.5 + 0.5 * math.sin(t))) for t in [i * math.pi / 16 for i in range(32)]]
            dd.polygon([S(q) for q in fist_pts], fill=(150, 90, 50, 255))
            dd.polygon([S(q) for q in sh["handle"]], fill=(30, 30, 30, 255))
            dd.polygon([S(q) for q in sh["blade"]], fill=(170, 180, 195, 255))
            canvas_pts = [(0, 0), (W, 0), (W, H), (0, H)]
            dd.line([S(q) for q in canvas_pts + [canvas_pts[0]]], fill=(255, 210, 0, 255), width=4)
            dd.line([sw / 2 - 20, sh_ / 2, sw / 2 + 20, sh_ / 2], fill=(255, 255, 255, 255), width=3)
            dd.line([sw / 2, sh_ / 2 - 20, sw / 2, sh_ / 2 + 20], fill=(255, 255, 255, 255), width=3)
            t = im.resize((tw, th), Image.LANCZOS)
            box = Image.new("RGB", (tw, th + 34), "white")
            ImageDraw.Draw(box).text((4, 4), label, fill="black", font=font(22))
            box.paste(t, (0, 34))
            tiles.append(box)
        cols = 3
        rows = (len(tiles) + cols - 1) // cols
        sheet = Image.new("RGB", (cols * (tw + 10), rows * (th + 44) + 50), "white")
        ImageDraw.Draw(sheet).text((6, 8), f"{name} {sw}×{sh_}：配置予定図（色面は位置の目安で絵ではない。黄色の線＝キャンバスの外周。白い十字＝画面中央）", fill="black", font=font(24))
        for i, t in enumerate(tiles):
            sheet.paste(t, ((i % cols) * (tw + 10), 50 + (i // cols) * (th + 44)))
        sheet.save(os.path.join(out, f"knife_screen_layout_{name}_{sw}x{sh_}.png"))

    report = {
        "canvas": [W, H], "dot": DOT, "grip": list(GRIP), "pixelsPerUnit": round(unit, 3),
        "bladeTip": [round(sh["tip"][0]), round(sh["tip"][1])], "wrist": [round(sh["wrist"][0]), round(sh["wrist"][1])],
        "bladeFromCurrentSpriteUnits": [round(blade[0], 4), round(blade[1], 4)],
        "everVisibleArea": {"minX": min(x for x, _ in visible), "maxX": max(x for x, _ in visible) + step,
                            "minY": min(y for _, y in visible), "maxY": max(y for _, y in visible) + step},
        "cutEdgeVisiblePoses": shown,
        "profile": {k: p[k] for k in ("gripPosition", "referenceAspect", "rightEdgeFollow", "windupOffset", "windupRoll", "strikeOffset", "strikeRoll", "strikeScale", "lowered")},
    }
    with open(os.path.join(out, "knife_layout_report.json"), "w", encoding="utf-8") as f:
        json.dump(report, f, indent=2, ensure_ascii=False)
    print(json.dumps(report, indent=2, ensure_ascii=False))


if __name__ == "__main__":
    main()
