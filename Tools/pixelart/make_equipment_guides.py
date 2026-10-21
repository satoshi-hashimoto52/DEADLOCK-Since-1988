#!/usr/bin/env python3
"""
Production guides for the equipment-screen art (GitHub Issue #1): the rifle side view and the standard player front view.

GUIDES ONLY: boxes, lines and labels that give the canvas, reference points, part areas, attachment areas, layer order
and colour-change regions. Nothing here is art and no output may stand in for a delivered image.

The numbers live in SPEC below (one place); the script writes into OUTPUT_DIR:
    rifle_side_guide.png     canvas, bore axis, muzzle point, part areas (visible in the approved first-person images vs
                             proposed), attachment areas (optic / suppressor / extended magazine), colour regions
    player_front_guide.png   canvas, ground line / centre, body areas, arm-to-body gaps, colour regions (the hand / arm
                             regions the current skins already use vs regions outside the current implementation)
    layer_design.png         layer order (back to front) and add / swap rules for both images
    layer_design.json        the same numbers for tools (canvas, anchors, layers, regions, palette from the approved art)
The palette swatches are averages measured from the approved first-person art and its skin region map (data, not art).

Run by hand (needs Pillow):  python3 Tools/pixelart/make_equipment_guides.py OUTPUT_DIR
"""
import json
import os
import sys

from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
FONT = "/System/Library/Fonts/ヒラギノ角ゴシック W6.ttc"

# Skin regions of the current implementation (Deadlock.View.SkinRegion; region map value = index x 32).
REGIONS = {"weapon_body": 1, "glove_body": 2, "glove_reinforcement": 3, "wrist_band": 4, "sleeve_base": 5, "sleeve_camo": 6}

SPEC = {
    "rifle": {
        "canvas": [1760, 800],
        "dotPx": 4,
        "outlinePx": [6, 8],
        "boreAxisY": 330,
        "muzzleTip": [360, 330],
        "buttEndX": 1640,
        "view": "true side view of the rifle's LEFT side (muzzle left, stock right): the side seen in the approved hip image (selector mark)",
        # part areas: name, box [x0, y0, x1, y1], kind (visible = seen in the approved first-person art, proposed = hidden there),
        # layer, colour (tan = weapon_body region, steel / black / red = fixed)
        "parts": [
            ["muzzle_device", [360, 306, 440, 354], "visible", "muzzle_standard", "steel", "標準の銃口部品（短い筒）"],
            ["barrel_thread", [440, 314, 470, 346], "proposed", "body", "steel", "ネジ部（銃口部品の下。分離時に描き足す）"],
            ["handguard", [470, 292, 880, 386], "visible", "body", "tan", "ハンドガード（長方形の穴、下側は提案）"],
            ["top_rail", [470, 276, 1170, 294], "visible", "body", "tan", "上面のレール（溝の山はタン）"],
            ["front_sight", [800, 218, 860, 276], "visible", "sights_iron", "steel", "フロントサイト（跳ね上げ式、左右の耳）"],
            ["upper_receiver", [880, 292, 1170, 392], "visible", "body", "steel", "機関部の上側（暗い鋼色）"],
            ["lower_receiver", [880, 380, 1170, 452], "visible", "body", "tan", "機関部の下側（タンの側板・通気の溝）"],
            ["selector_mark", [1060, 410, 1084, 422], "visible", "body", "red", "赤い切り替え表示（固定色）"],
            ["magazine_well", [920, 392, 1020, 470], "visible", "body", "tan", "弾倉受け（タンの側板）"],
            ["magazine", [928, 452, 1018, 650], "visible", "magazine_standard", "black", "標準弾倉（黒、横溝。差し込み部も描く）"],
            ["trigger_guard", [1020, 452, 1090, 506], "proposed", "body", "steel", "引き金・用心金（手元画像では指で隠れる）"],
            ["pistol_grip", [1090, 448, 1160, 600], "proposed", "body", "black", "グリップ（右手で隠れる）"],
            ["rear_sight", [1090, 222, 1166, 276], "visible", "sights_iron", "steel", "リアサイト（耳2枚・側面のつまみ）"],
            ["stock", [1170, 300, 1600, 470], "visible", "body", "tan", "銃床（箱形、暗い溝。後半は提案）"],
            ["butt_pad", [1600, 300, 1640, 476], "proposed", "body", "black", "床尾板（画面外で見えない）"],
        ],
        # attachment / swap areas (not drawn in the standard image)
        "attachments": [
            ["optic", [940, 110, 1170, 276], "add", "optic_*", "照準器の取付範囲（ドット・スコープ）。標準では描かない"],
            ["suppressor", [100, 296, 470, 364], "swap", "muzzle_suppressor", "サプレッサー（標準の銃口部品と交換）。標準では描かない"],
            ["extended_magazine", [928, 452, 1030, 750], "swap", "magazine_extended", "拡張マガジン（標準弾倉と交換）。標準では描かない"],
        ],
        "layers": [
            ["magazine_standard", "swap", "弾倉。弾倉受けの後ろ。差し込まれて隠れる上端まで描く（後続の magazine_extended と交換）"],
            ["body", "base", "機関部・ハンドガード・レール・銃床・グリップ。銃口部品の下のネジ部、照準器の台座の下のレールも描く"],
            ["muzzle_standard", "swap", "標準の銃口部品（後続の muzzle_suppressor と交換）"],
            ["sights_iron", "base", "フロント・リアサイト（立てた状態）"],
            ["optic_*", "add", "後続：照準器（今回は作らない）"],
        ],
    },
    "player": {
        "canvas": [1024, 1536],
        "dotPx": 4,
        "outlinePx": [6, 8],
        "groundY": 1456,
        "centreX": 512,
        "headTopY": 140,
        "view": "front view, full body, standing, arms held slightly away from the body, no weapon",
        # areas: name, box, layer, region (skin region of the current implementation, or a future / fixed one), label
        "parts": [
            ["helmet", [402, 140, 622, 260], "head_gear", "future:head_gear", "頭部装備（提案）"],
            ["head", [432, 200, 592, 380], "head", "future:skin", "顔・頭（提案）"],
            ["neck", [472, 370, 552, 410], "head", "future:skin", "首"],
            ["torso_shirt", [362, 400, 662, 860], "top", "future:uniform", "上着の胴（袖と同じ迷彩の提案）"],
            ["vest", [382, 430, 642, 800], "vest", "future:vest", "胴体装備（提案）"],
            ["belt", [372, 820, 652, 868], "vest", "future:vest", "ベルト（提案）"],
            ["sleeve_left_upper", [252, 410, 352, 640], "arms", "sleeve_base+sleeve_camo", "袖（迷彩・一人称と対応）"],
            ["sleeve_right_upper", [672, 410, 772, 640], "arms", "sleeve_base+sleeve_camo", "袖（迷彩・一人称と対応）"],
            ["sleeve_left_lower", [212, 630, 312, 830], "arms", "sleeve_base+sleeve_camo", "前腕の袖"],
            ["sleeve_right_lower", [712, 630, 812, 830], "arms", "sleeve_base+sleeve_camo", "前腕の袖"],
            ["wrist_band_left", [206, 830, 314, 862], "hands", "wrist_band", "手首の帯（暗い灰）"],
            ["wrist_band_right", [710, 830, 818, 862], "hands", "wrist_band", "手首の帯（暗い灰）"],
            ["glove_left", [200, 860, 316, 990], "hands", "glove_body", "茶の革グローブ"],
            ["glove_right", [708, 860, 824, 990], "hands", "glove_body", "茶の革グローブ"],
            ["pants_hip", [382, 860, 642, 960], "legs", "future:pants", "ズボンの腰（提案）"],
            ["pants_left", [382, 960, 500, 1370], "legs", "future:pants", "ズボン（提案）"],
            ["pants_right", [524, 960, 642, 1370], "legs", "future:pants", "ズボン（提案）"],
            ["boot_left", [370, 1360, 504, 1456], "feet", "future:boots", "靴（提案）"],
            ["boot_right", [520, 1360, 654, 1456], "feet", "future:boots", "靴（提案）"],
        ],
        "gaps": [
            ["gap_left", [318, 660, 360, 990], "腕と胴の間：背景が見える隙間（目安24px以上）"],
            ["gap_right", [664, 660, 706, 990], "腕と胴の間：背景が見える隙間（目安24px以上）"],
            ["gap_legs", [502, 980, 522, 1360], "脚の間の隙間"],
        ],
        "layers": [
            ["body_base", "base", "肌・顔・首（服の下も最低限描く）"],
            ["legs", "base", "ズボン"],
            ["feet", "base", "靴"],
            ["top", "base", "上着の胴"],
            ["vest", "add", "胴体装備・ベルト（外すと上着が見える。上着は胴体装備の下も描く）"],
            ["arms", "base", "袖（左右）"],
            ["hands", "base", "手首の帯・グローブ（左右）"],
            ["head_gear", "add", "頭部装備（外すと髪・頭が見える。頭は装備の下も描く）"],
        ],
    },
}

COLOURS = {"tan": (205, 170, 110), "steel": (70, 68, 70), "black": (30, 30, 32), "red": (210, 40, 40)}


def palette():
    """Average tones of the approved hip image per skin region (15 / 50 / 85% by brightness): data, not art."""
    base = Image.open(os.path.join(ROOT, "Assets/Game/Art/Weapons/Rifle/rifle_hip_approved_opaque.png")).convert("RGBA").load()
    reg = Image.open(os.path.join(ROOT, "Assets/Game/Art/Weapons/Rifle/Layers/Hip/rifle_hip_skin_regions.png")).convert("L")
    rp = reg.load()
    groups = {}
    W, H = reg.size
    for y in range(H):
        for x in range(W):
            v = rp[x, y] // 32
            if v:
                groups.setdefault(v, []).append(base[x, y][:3])
    out = {}
    for name, idx in REGIONS.items():
        px = sorted(groups.get(idx, []), key=sum)
        if px:
            out[name] = ["#%02x%02x%02x" % px[int(len(px) * q)] for q in (0.15, 0.5, 0.85)]
    return out


def font(size):
    return ImageFont.truetype(FONT, size) if os.path.exists(FONT) else ImageFont.load_default()


def label(dr, xy, text, size=18, fill=(0, 0, 0)):
    dr.text(xy, text, fill=fill + (255,), font=font(size), stroke_width=3, stroke_fill=(255, 255, 255))


def dashed_rect(dr, box, colour, width=3, dash=14):
    x0, y0, x1, y1 = box
    for a, b, horiz, c in ((x0, x1, True, y0), (x0, x1, True, y1), (y0, y1, False, x0), (y0, y1, False, x1)):
        p = a
        while p < b:
            q = min(p + dash, b)
            dr.line([(p, c), (q, c)] if horiz else [(c, p), (c, q)], fill=colour, width=width)
            p += dash * 2


def rifle_guide(out, pal):
    r = SPEC["rifle"]
    W, H = r["canvas"]
    HEAD, FOOT, EXTRA = 120, 230, 300
    img = Image.new("RGBA", (W + EXTRA, H + HEAD + FOOT), (255, 255, 255, 255))
    dr = ImageDraw.Draw(img, "RGBA")
    oy = HEAD
    dr.rectangle([0, oy, W - 1, oy + H - 1], fill=(246, 246, 246, 255), outline=(0, 120, 255, 255), width=3)
    for x in range(0, W, 100):
        dr.line([x, oy, x, oy + H], fill=(226, 226, 226, 255))
    for y in range(0, H, 100):
        dr.line([0, oy + y, W, oy + y], fill=(226, 226, 226, 255))

    def b(box):
        return [box[0], box[1] + oy, box[2], box[3] + oy]

    for name, box, kind, layer, colour, text in r["parts"]:
        c = COLOURS[colour]
        dr.rectangle(b(box), fill=c + (150 if kind == "visible" else 70,), outline=(0, 0, 0, 255) if kind == "visible" else None, width=2)
        if kind == "proposed":
            dashed_rect(dr, b(box), (200, 0, 160, 255), 3, 8)
    for name, box, kind, layer, text in r["attachments"]:
        dashed_rect(dr, b(box), (0, 150, 70, 255), 3)
    # bore axis and muzzle point
    ay = r["boreAxisY"] + oy
    for x in range(0, W, 24):
        dr.line([(x, ay), (x + 12, ay)], fill=(0, 90, 220, 255), width=2)
    mx, my = r["muzzleTip"][0], r["muzzleTip"][1] + oy
    dr.line([(mx - 26, my), (mx + 26, my)], fill=(220, 0, 0, 255), width=4)
    dr.line([(mx, my - 26), (mx, my + 26)], fill=(220, 0, 0, 255), width=4)
    dr.line([(r["buttEndX"], oy + 260), (r["buttEndX"], oy + 520)], fill=(220, 0, 0, 255), width=3)

    # labels (outside clutter: numbered list in the footer, numbers on the canvas)
    items = [(p[0], p[1], p[2], p[5]) for p in r["parts"]] + [(a[0], a[1], "attach", a[4]) for a in r["attachments"]]
    for i, (name, box, kind, text) in enumerate(items, 1):
        cx, cy = (box[0] + box[2]) // 2, (box[1] + box[3]) // 2 + oy
        if name == "optic":
            cy = box[1] + oy + 20
        if name == "suppressor":
            cx = box[0] + 60
        if name == "extended_magazine":
            cy = box[3] + oy - 24
        label(dr, (cx - 10, cy - 12), str(i), 22, (0, 0, 160))
    label(dr, (mx - 150, my - 64), f"銃口の先端 {tuple(r['muzzleTip'])}", 18, (200, 0, 0))
    label(dr, (20, ay + 40), f"銃身の中心線 y={r['boreAxisY']}", 18, (0, 70, 200))
    label(dr, (r["buttEndX"] - 150, oy + 530), f"床尾の後端 x≈{r['buttEndX']}", 18, (200, 0, 0))

    label(dr, (16, 10), f"ライフル横向き全体図：構図ガイド　キャンバス {W}×{H}（青枠、透明背景、座標は青枠の左上が原点）　図形は位置・範囲の目安で、絵ではない", 26)
    label(dr, (16, 48), f"真横・銃口が左（銃の左側面＝腰だめで見えている面）。1ドット≈{r['dotPx']}px、黒い輪郭線 {r['outlinePx'][0]}〜{r['outlinePx'][1]}px（腰だめ画像と同じ粗さ）。手・腕・背景・文字・床の影は描かない", 19)
    label(dr, (16, 80), "実線＝手元画像で見えている部品　紫の破線＝手元画像で隠れている部品（形は提案）　緑の破線＝取付・交換部品の範囲（標準では描かない）", 19)
    fy = oy + H + 12
    col_w = (W + EXTRA) // 3
    for i, (name, box, kind, text) in enumerate(items, 1):
        x = 16 + ((i - 1) // 6) * col_w
        y = fy + ((i - 1) % 6) * 30
        mark = {"visible": "見えている", "proposed": "提案", "attach": "取付・交換"}[kind]
        label(dr, (x, y), f"{i}. {text}［{mark}］", 17)
    # colour legend
    lx = W - 560
    label(dr, (lx, oy + 14), "色：", 18)
    for j, (c, t) in enumerate([("tan", "タン＝武器本体の色替え領域（weapon_body）"), ("steel", "暗い鋼色＝固定"), ("black", "黒＝固定（弾倉・グリップ）"), ("red", "赤＝固定（切り替え表示）")]):
        dr.rectangle([lx + 50, oy + 14 + j * 30, lx + 80, oy + 38 + j * 30], fill=COLOURS[c] + (255,), outline=(0, 0, 0, 255))
        label(dr, (lx + 90, oy + 12 + j * 30), t, 17)
    for j, hexes in enumerate(pal.get("weapon_body", [])):
        rgb = tuple(int(hexes[k:k + 2], 16) for k in (1, 3, 5))
        dr.rectangle([lx + 50 + j * 40, oy + 140, lx + 84 + j * 40, oy + 170], fill=rgb + (255,), outline=(0, 0, 0, 255))
    label(dr, (lx + 180, oy + 142), "腰だめ画像のタン（暗・中・明）", 16)
    img.convert("RGB").save(os.path.join(out, "rifle_side_guide.png"))


def player_guide(out, pal):
    p = SPEC["player"]
    W, H = p["canvas"]
    HEAD, SIDE = 120, 700
    img = Image.new("RGBA", (W + SIDE, H + HEAD), (255, 255, 255, 255))
    dr = ImageDraw.Draw(img, "RGBA")
    oy = HEAD
    dr.rectangle([0, oy, W - 1, oy + H - 1], fill=(246, 246, 246, 255), outline=(0, 120, 255, 255), width=3)
    for x in range(0, W, 100):
        dr.line([x, oy, x, oy + H], fill=(226, 226, 226, 255))
    for y in range(0, H, 100):
        dr.line([0, oy + y, W, oy + y], fill=(226, 226, 226, 255))

    def b(box):
        return [box[0], box[1] + oy, box[2], box[3] + oy]

    region_col = {"glove_body": (130, 78, 45), "wrist_band": (60, 58, 60), "sleeve_base+sleeve_camo": (110, 100, 60),
                  "future:uniform": (150, 140, 90), "future:pants": (120, 120, 90), "future:vest": (95, 100, 70),
                  "future:boots": (90, 70, 55), "future:head_gear": (200, 180, 130), "future:skin": (215, 180, 150)}
    for name, box, layer, region, text in p["parts"]:
        c = region_col[region]
        now = region in ("glove_body", "wrist_band", "sleeve_base+sleeve_camo")
        dr.rectangle(b(box), fill=c + (150 if now else 80,), outline=(0, 0, 0, 255) if now else None, width=2)
        if not now:
            dashed_rect(dr, b(box), (120, 120, 120, 255), 2, 8)
    for name, box, text in p["gaps"]:
        dr.rectangle(b(box), fill=(0, 200, 255, 90))
        dashed_rect(dr, b(box), (0, 140, 220, 255), 3)
    gy = p["groundY"] + oy
    dr.line([(0, gy), (W, gy)], fill=(220, 0, 0, 255), width=3)
    cx = p["centreX"]
    for y in range(oy, oy + H, 24):
        dr.line([(cx, y), (cx, y + 12)], fill=(0, 90, 220, 255), width=2)
    dr.line([(cx - 26, gy), (cx + 26, gy)], fill=(220, 0, 0, 255), width=5)
    dr.line([(cx, gy - 26), (cx, gy)], fill=(220, 0, 0, 255), width=5)
    dr.line([(300, p["headTopY"] + oy), (724, p["headTopY"] + oy)], fill=(220, 0, 0, 255), width=2)
    label(dr, (cx + 30, gy - 40), f"基準点：足元の中央 ({cx}, {p['groundY']})", 18, (200, 0, 0))
    label(dr, (40, p["headTopY"] + oy - 30), f"頭の上端 y≈{p['headTopY']}（頭部装備を含む）", 17, (200, 0, 0))
    label(dr, (cx + 8, oy + 40), "左右の中心線", 17, (0, 70, 200))

    label(dr, (16, 10), f"標準プレイヤー正面立ち絵：構図ガイド　キャンバス {W}×{H}（青枠、透明背景）　図形は範囲の目安で、絵ではない", 26)
    label(dr, (16, 48), f"正面・全身・武器なし。腕は胴から少し離す（水色＝背景が見える隙間）。1ドット≈{p['dotPx']}px、黒い輪郭線 {p['outlinePx'][0]}〜{p['outlinePx'][1]}px", 19)
    label(dr, (16, 80), "実線の枠＝今の実装で色替えできる領域（手・腕）　灰の破線＝今回の実装範囲外（将来の全身スキンで部位ごとに色替えできるよう分けて描く）", 19)

    sx = W + 30
    label(dr, (sx, oy + 10), "領域（今の実装との対応）", 22)
    rows = [("glove_body", "茶の革グローブ → glove_body（2）"), ("wrist_band", "手首の帯 → wrist_band（4）"),
            ("sleeve_base+sleeve_camo", "迷彩の袖 → sleeve_base（5）・sleeve_camo（6）"),
            ("future:uniform", "上着の胴 → 今回の範囲外（将来の領域）"), ("future:pants", "ズボン → 今回の範囲外（将来の領域）"),
            ("future:vest", "胴体装備・ベルト → 今回の範囲外（将来の領域）"), ("future:boots", "靴 → 今回の範囲外（将来の領域）"),
            ("future:head_gear", "頭部装備 → 今回の範囲外（将来の領域）"), ("future:skin", "顔・首（肌）→ 今回の範囲外（将来の領域）")]
    for j, (r, t) in enumerate(rows):
        y = oy + 50 + j * 40
        dr.rectangle([sx, y, sx + 34, y + 26], fill=region_col[r] + (255,), outline=(0, 0, 0, 255))
        label(dr, (sx + 46, y), t, 16)
    y = oy + 420
    label(dr, (sx, y), "一人称の手元画像から測った色（暗・中・明）", 20)
    for j, (name, t) in enumerate([("glove_body", "グローブ"), ("wrist_band", "手首の帯"), ("sleeve_base", "袖の地色"), ("sleeve_camo", "袖の迷彩")]):
        yy = y + 40 + j * 44
        label(dr, (sx, yy + 4), t, 17)
        for k, hexes in enumerate(pal.get(name, [])):
            rgb = tuple(int(hexes[i:i + 2], 16) for i in (1, 3, 5))
            dr.rectangle([sx + 150 + k * 60, yy, sx + 200 + k * 60, yy + 32], fill=rgb + (255,), outline=(0, 0, 0, 255))
        label(dr, (sx + 340, yy + 6), " ".join(pal.get(name, [])), 14)
    y += 240
    for i, (name, box, layer, region, text) in enumerate(p["parts"], 1):
        label(dr, (sx, y + (i - 1) * 28), f"{i}. {text}", 16)
        bx, by = (box[0] + box[2]) // 2, (box[1] + box[3]) // 2 + oy
        if name == "torso_shirt":
            bx, by = box[0] + 20, box[1] + oy + 16
        label(dr, (bx - 8, by - 12), str(i), 18, (0, 0, 160))
    img.convert("RGB").save(os.path.join(out, "player_front_guide.png"))


def layer_diagram(out):
    W, H = 1700, 900
    img = Image.new("RGB", (W, H), (255, 255, 255))
    dr = ImageDraw.Draw(img, "RGBA")
    label(dr, (16, 10), "部品分離の設計（後ろ→前の順に重ねる。全レイヤーは同じキャンバス寸法・同じ基準点、個別のトリミングなし）", 24)
    kind_col = {"base": (90, 140, 220), "swap": (230, 150, 40), "add": (60, 170, 90)}
    for col, (key, title) in enumerate([("rifle", "ライフル横向き全体図（1760×800）"), ("player", "プレイヤー正面立ち絵（1024×1536）")]):
        x = 30 + col * 840
        label(dr, (x, 60), title, 22)
        layers = SPEC[key]["layers"]
        for i, (name, kind, text) in enumerate(layers):
            y = 110 + i * 80
            dr.rectangle([x, y, x + 780, y + 64], fill=kind_col[kind] + (60,), outline=kind_col[kind] + (255,), width=3)
            label(dr, (x + 12, y + 6), f"{i + 1}. {name}［{ {'base': '基礎', 'swap': '交換', 'add': '追加'}[kind] }］", 19)
            label(dr, (x + 12, y + 34), text, 15)
        label(dr, (x, 110 + len(layers) * 80 + 10), "番号の小さい方が後ろ（先に描画）、大きい方が前", 16)
    y = H - 150
    for i, (k, t) in enumerate([("base", "基礎：常に表示"), ("swap", "交換：標準部品を非表示にして置き換える（弾倉・銃口部品）"), ("add", "追加：基礎の上に重ねる（照準器・胴体装備・頭部装備）")]):
        dr.rectangle([30, y + i * 40, 64, y + 26 + i * 40], fill=kind_col[k] + (255,))
        label(dr, (76, y + i * 40), t, 18)
    img.save(os.path.join(out, "layer_design.png"))


def main():
    out = sys.argv[1]
    os.makedirs(out, exist_ok=True)
    pal = palette()
    rifle_guide(out, pal)
    player_guide(out, pal)
    layer_diagram(out)
    data = {"note": "Guide numbers (proposals, not approved designs). Coordinates: pixels from the canvas top-left.",
            "skinRegions": REGIONS, "paletteFromApprovedHip": pal, **SPEC}
    with open(os.path.join(out, "layer_design.json"), "w", encoding="utf-8") as f:
        json.dump(data, f, indent=2, ensure_ascii=False)
    print("written", out)


if __name__ == "__main__":
    main()
