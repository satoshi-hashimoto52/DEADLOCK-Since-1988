#!/usr/bin/env python3
"""
Production guide for the SMG candidate's equipment-screen side view (GitHub Issue #2). Same style as the rifle side
view guide (make_equipment_guides.py).

GUIDES ONLY: boxes, lines and labels that give the canvas, reference points, part areas, attachment areas and the
colour-change regions. Nothing here is art and no output may stand in for a delivered image. The two reference sheets
are crops of the existing candidate images with labels (the images themselves are only read).

The numbers live in SPEC below (one place); the script writes into OUTPUT_DIR:
    smg_side_canvas_guide.png         canvas, bore axis, muzzle point, part areas (visible in the hip v2 / ADS images vs
                                      estimated), attachment areas (muzzle device / optic / extended magazine), regions
    smg_side_display_proposal.png     the equipment screen's weapon preview: whole canvas vs the standard view range
    smg_side_reference_parts.png      labelled crops of hip v2 (side seen) and of the ADS candidate (sights from behind)
    smg_side_guide.json               the same numbers for tools (canvas, anchors, parts, attachments, view range, palette)

Run by hand (needs Pillow):  python3 Tools/pixelart/make_smg_side_guide.py OUTPUT_DIR
"""
import json
import os
import sys

from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
FONT = "/System/Library/Fonts/ヒラギノ角ゴシック W6.ttc"
HIP = "Assets/Game/Art/Weapons/SMG/smg_hip_candidate_v2_opaque.png"
HIP_REGIONS = "Assets/Game/Art/Weapons/SMG/Regions/Hip/smg_hip_skin_regions.png"
ADS = "Assets/Game/Art/Weapons/SMG/smg_ads_front_sight_gap3.png"

SPEC = {
    "canvas": [1440, 800],
    "dotPx": 4,
    "outlinePx": [9, 11],
    "boreAxisY": 360,
    "muzzleTip": [420, 360],
    "stockRearX": 1340,
    "view": "true side view of the SMG's LEFT side (muzzle left, stock right): the side seen in the hip candidate v2 (side slots, tan frame)",
    # part areas: name, box [x0, y0, x1, y1], kind (visible = seen in hip v2 / ADS, estimated = hidden there),
    # colour (housing = weapon_body region, steel / black / tan = fixed), label
    "parts": [
        ["barrel_nut", [420, 334, 490, 386], "visible", "steel", "銃口のナット（段付き。固定色）"],
        ["barrel_thread", [490, 342, 516, 378], "estimated", "steel", "ネジ部（ナットの下。将来の銃口部品の取付部）"],
        ["front_block", [516, 316, 640, 420], "visible", "housing", "前部ブロック（外装・色替え）"],
        ["front_block_lower", [516, 420, 640, 470], "estimated", "housing", "前部ブロックの下側（左手で隠れる）"],
        ["front_sight", [552, 248, 606, 316], "visible", "steel", "照星（左右の耳＋中央の柱。固定色）"],
        ["receiver", [640, 308, 1080, 452], "visible", "housing", "機関部（側面に長い溝2本。色替え）"],
        ["tan_frame", [884, 330, 922, 444], "visible", "tan", "砂色の縦長の枠（固定色）"],
        ["magazine", [748, 452, 832, 690], "visible", "black", "弾倉（まっすぐ下へ。固定色）"],
        ["trigger_guard", [860, 452, 950, 522], "estimated", "steel", "引き金・用心金（右手で隠れる。固定色）"],
        ["pistol_grip", [950, 452, 1036, 640], "estimated", "black", "グリップ（右手で隠れる。固定色）"],
        ["rear_sight", [1084, 250, 1144, 308], "visible", "steel", "照門（左右の耳。固定色）"],
        ["rear_block", [1080, 308, 1200, 472], "visible", "housing", "後部ブロック（暗い下面を含む。色替え）"],
        ["stock_bolt", [1184, 378, 1214, 408], "visible", "housing", "銃床の付け根の丸いボルト（色替え）"],
        ["hinge_plate", [1200, 356, 1250, 462], "visible", "steel", "ヒンジ板（固定色）"],
        ["wire_stock", [1200, 440, 1340, 600], "estimated", "steel", "折りたたんだワイヤー銃床（真横からの見え方は推定。固定色）"],
    ],
    # attachment / swap areas (not drawn in the standard image)
    "attachments": [
        ["muzzle_device", [140, 330, 516, 390], "swap", "銃口部品（サプレッサー等）の範囲。標準では描かない"],
        ["optic", [680, 170, 1060, 308], "add", "照準器の取付範囲（上面）。標準では描かない"],
        ["extended_magazine", [748, 452, 846, 770], "swap", "拡張マガジン（標準弾倉と交換）。標準では描かない"],
    ],
    # equipment screen: the range shown by default (standard parts only) and the range with every attachment
    "viewStandard": [400, 232, 1360, 712],
    "viewWithAttachments": [120, 160, 1360, 780],
    # weapon preview of the equipment screen (GameMenu): 800 x 340 box, image area inset 12 / 12 / 12 / 44 px
    "previewImageArea": [776, 284],
}

COLOURS = {"housing": (110, 112, 120), "steel": (60, 58, 62), "black": (28, 28, 30), "tan": (196, 168, 120)}


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


def palette():
    """Tones of the hip candidate v2 (15 / 50 / 85% by brightness) per skin region: data measured from the image, not art."""
    base = Image.open(os.path.join(ROOT, HIP)).convert("RGBA").load()
    reg = Image.open(os.path.join(ROOT, HIP_REGIONS)).convert("L")
    rp = reg.load()
    W, H = reg.size
    groups = {}
    for y in range(H):
        for x in range(W):
            v = rp[x, y] // 32
            if v:
                groups.setdefault(v, []).append(base[x, y][:3])
    out = {}
    for name, idx in (("weapon_body", 1), ("glove_body", 2), ("wrist_band", 4), ("sleeve_base", 5), ("sleeve_camo", 6)):
        px = sorted(groups.get(idx, []), key=sum)
        if px:
            out[name] = ["#%02x%02x%02x" % px[int(len(px) * q)] for q in (0.15, 0.5, 0.85)]
    return out


def canvas_guide(out, pal):
    W, H = SPEC["canvas"]
    HEAD, FOOT, EXTRA = 120, 260, 420
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

    dashed_rect(dr, b(SPEC["viewStandard"]), (255, 140, 0, 255), 2, 10)
    for name, box, kind, colour, text in SPEC["parts"]:
        c = COLOURS[colour]
        dr.rectangle(b(box), fill=c + (160 if kind == "visible" else 70,), outline=(0, 0, 0, 255) if kind == "visible" else None, width=2)
        if kind == "estimated":
            dashed_rect(dr, b(box), (200, 0, 160, 255), 3, 8)
    for name, box, kind, text in SPEC["attachments"]:
        dashed_rect(dr, b(box), (0, 150, 70, 255), 3)
    ay = SPEC["boreAxisY"] + oy
    for x in range(0, W, 24):
        dr.line([(x, ay), (x + 12, ay)], fill=(0, 90, 220, 255), width=2)
    mx, my = SPEC["muzzleTip"][0], SPEC["muzzleTip"][1] + oy
    dr.line([(mx - 26, my), (mx + 26, my)], fill=(220, 0, 0, 255), width=4)
    dr.line([(mx, my - 26), (mx, my + 26)], fill=(220, 0, 0, 255), width=4)
    sx = SPEC["stockRearX"]
    dr.line([(sx, oy + 300), (sx, oy + 640)], fill=(220, 0, 0, 255), width=3)

    items = [(p[0], p[1], p[2], p[4]) for p in SPEC["parts"]] + [(a[0], a[1], "attach", a[3]) for a in SPEC["attachments"]]
    for i, (name, box, kind, text) in enumerate(items, 1):
        cx, cy = (box[0] + box[2]) // 2, (box[1] + box[3]) // 2 + oy
        if name == "optic":
            cy = box[1] + oy + 22
        if name == "muzzle_device":
            cx = box[0] + 70
        if name == "extended_magazine":
            cy = box[3] + oy - 26
        if name == "front_block_lower":
            cx = box[0] + 24
        label(dr, (cx - 10, cy - 12), str(i), 22, (0, 0, 160))
    label(dr, (mx - 120, my - 70), f"銃口の先端 {tuple(SPEC['muzzleTip'])}", 18, (200, 0, 0))
    label(dr, (16, ay + 34), f"銃身の中心線 y={SPEC['boreAxisY']}", 18, (0, 70, 200))
    label(dr, (sx - 120, oy + 650), f"銃床の後端 x≈{sx}", 18, (200, 0, 0))
    v = SPEC["viewStandard"]
    label(dr, (v[0] + 6, v[1] + oy + 4), "標準の表示範囲（装備画面）", 16, (220, 110, 0))

    label(dr, (16, 10), f"SMG（候補）横向き全体図：構図ガイド　キャンバス {W}×{H}（青枠、透明背景、座標は青枠の左上が原点）　図形は位置・範囲の目安で、絵ではない", 26)
    label(dr, (16, 48), f"真横・銃口が左（銃の左側面＝腰だめv2で見えている面）。1ドット={SPEC['dotPx']}px、黒い輪郭線 {SPEC['outlinePx'][0]}〜{SPEC['outlinePx'][1]}px（腰だめv2と同じ太さ）。手・腕・背景・文字・床の影は描かない", 19)
    label(dr, (16, 80), "実線＝腰だめv2・ADS候補で見えている部品　紫の破線＝隠れている部品（形は推定）　緑の破線＝取付・交換部品の範囲（標準では描かない）　橙の破線＝標準の表示範囲", 19)
    fy = oy + H + 12
    col_w = (W + EXTRA) // 3
    for i, (name, box, kind, text) in enumerate(items, 1):
        x = 16 + ((i - 1) // 7) * col_w
        y = fy + ((i - 1) % 7) * 32
        mark = {"visible": "見えている", "estimated": "推定", "attach": "取付・交換"}[kind]
        label(dr, (x, y), f"{i}. {text}［{mark}］", 17)
    lx = W + 20
    label(dr, (lx, oy + 14), "色（位置の目安。描く色ではない）：", 18)
    legend = [("housing", "外装＝武器本体の色替え領域（weapon_body）"), ("steel", "暗い鋼色＝固定"), ("black", "黒＝固定（弾倉・グリップ）"), ("tan", "砂色＝固定（縦長の枠）")]
    for j, (c, t) in enumerate(legend):
        dr.rectangle([lx, oy + 50 + j * 32, lx + 30, oy + 74 + j * 32], fill=COLOURS[c] + (255,), outline=(0, 0, 0, 255))
        label(dr, (lx + 40, oy + 48 + j * 32), t, 16)
    label(dr, (lx, oy + 190), "腰だめv2で測った外装の色（暗・中・明）", 16)
    for j, hexes in enumerate(pal.get("weapon_body", [])):
        rgb = tuple(int(hexes[k:k + 2], 16) for k in (1, 3, 5))
        dr.rectangle([lx + j * 44, oy + 220, lx + 36 + j * 44, oy + 252], fill=rgb + (255,), outline=(0, 0, 0, 255))
    img.convert("RGB").save(os.path.join(out, "smg_side_canvas_guide.png"))


def fit(box_w, box_h, w, h):
    s = min(box_w / w, box_h / h)
    return s, int(w * s), int(h * s)


def display_proposal(out):
    """Equipment screen weapon preview: the whole canvas (margins for later parts) vs the standard view range."""
    aw, ah = SPEC["previewImageArea"]
    W, H = SPEC["canvas"]
    gx0, gx1 = SPEC["muzzleTip"][0], SPEC["stockRearX"]
    cases = [
        ("A：キャンバス全体を表示（余白込み）", [0, 0, W, H]),
        ("B（提案）：標準の表示範囲だけを表示", SPEC["viewStandard"]),
        ("C（取付時の例）：取付部品の範囲まで広げる", SPEC["viewWithAttachments"]),
    ]
    pad = 40
    img = Image.new("RGB", (aw + 2 * pad + 520, len(cases) * (ah + 110) + 90), (255, 255, 255))
    dr = ImageDraw.Draw(img)
    label(dr, (16, 12), f"装備画面の武器の欄（画像の枠 {aw}×{ah}。縦横比を保って全体を収める）での、銃の大きさの比較", 22)
    for i, (title, view) in enumerate(cases):
        y0 = 70 + i * (ah + 110)
        label(dr, (pad, y0), title, 20)
        bx, by = pad, y0 + 34
        dr.rectangle([bx, by, bx + aw, by + ah], fill=(40, 43, 54), outline=(0, 0, 0))
        vw, vh = view[2] - view[0], view[3] - view[1]
        s, w, h = fit(aw, ah, vw, vh)
        ox, oy = bx + (aw - w) // 2, by + (ah - h) // 2

        def m(x, y):
            return ox + (x - view[0]) * s, oy + (y - view[1]) * s

        for name, box, kind, colour, text in SPEC["parts"]:
            p0, p1 = m(box[0], box[1]), m(box[2], box[3])
            if p1[0] < bx or p0[0] > bx + aw:
                continue
            dr.rectangle([max(p0[0], bx), max(p0[1], by), min(p1[0], bx + aw), min(p1[1], by + ah)],
                         fill=COLOURS[colour], outline=(0, 0, 0))
        if i == 2:
            for name, box, kind, text in SPEC["attachments"]:
                p0, p1 = m(box[0], box[1]), m(box[2], box[3])
                dashed_rect(dr, [p0[0], p0[1], p1[0], p1[1]], (0, 200, 90), 2, 8)
        length = (gx1 - gx0) * s
        label(dr, (bx + aw + 20, by + 10), f"倍率 {s:.3f}", 18)
        label(dr, (bx + aw + 20, by + 40), f"銃の長さ（銃口〜銃床）{length:.0f}px", 18)
        label(dr, (bx + aw + 20, by + 70), f"＝欄の幅の {length / aw * 100:.0f}%", 18)
    img.save(os.path.join(out, "smg_side_display_proposal.png"))


def reference_parts(out):
    """Labelled crops of the existing images: hip v2 (the side seen) and the ADS candidate (the sights from behind)."""
    hip = Image.open(os.path.join(ROOT, HIP)).convert("RGBA")
    ads = Image.open(os.path.join(ROOT, ADS)).convert("RGBA")

    def flat(im, box):
        c = im.crop(box)
        bg = Image.new("RGBA", c.size, (235, 235, 235, 255))
        bg.alpha_composite(c)
        return bg.convert("RGB")

    a = flat(hip, (940, 360, 1580, 941))
    b = flat(ads, (760, 360, 1040, 640)).resize((420, 420), Image.NEAREST)
    img = Image.new("RGB", (a.width + b.width + 60, max(a.height, b.height) + 150), (255, 255, 255))
    img.paste(a, (20, 120))
    img.paste(b, (a.width + 40, 120))
    dr = ImageDraw.Draw(img)
    label(dr, (16, 10), "参考：既存の候補画像の切り出し（新しい絵ではない）", 20)
    label(dr, (16, 42), "左＝腰だめv2（左側面が見えている）、右＝ADS候補（照門・照星を真後ろから。1.5倍）", 18)
    label(dr, (16, 74), "腰だめv2：茶色の手袋の下（前部ブロックの下側、引き金・用心金・グリップ）は見えない＝推定", 18, (160, 0, 120))
    ox, oy = 20 - 940, 120 - 360
    marks = [
        ("ナット", (975, 425)), ("照星", (1060, 395)), ("前部ブロック", (1030, 470)), ("機関部の溝", (1130, 500)),
        ("砂色の枠", (1240, 640)), ("弾倉", (1120, 800)), ("照門", (1300, 470)), ("後部ブロック", (1350, 560)),
        ("丸いボルト", (1320, 600)), ("ワイヤー銃床", (1500, 720)),
    ]
    for text, (x, y) in marks:
        label(dr, (x + ox, y + oy), text, 18, (0, 0, 180))
    bx = a.width + 40
    label(dr, (bx + 10, 130), "照門の左右の耳", 18, (0, 0, 180))
    label(dr, (bx + 120, 190), "照星（耳＋中央の柱）", 18, (0, 0, 180))
    label(dr, (bx + 10, 430), "段になった台＝機関部の上面", 18, (0, 0, 180))
    img.save(os.path.join(out, "smg_side_reference_parts.png"))


def main():
    out = sys.argv[1]
    os.makedirs(out, exist_ok=True)
    pal = palette()
    canvas_guide(out, pal)
    display_proposal(out)
    reference_parts(out)
    data = dict(SPEC)
    data["paletteFromHipV2"] = pal
    data["note"] = "GUIDE DATA ONLY (Tools/pixelart/make_smg_side_guide.py): canvas, anchors, part / attachment areas and view ranges for the SMG candidate's side view. Not art."
    with open(os.path.join(out, "smg_side_guide.json"), "w", encoding="utf-8") as f:
        json.dump(data, f, indent=2, ensure_ascii=False)
        f.write("\n")
    print("written", out)


if __name__ == "__main__":
    main()
