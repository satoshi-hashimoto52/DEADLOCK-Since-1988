#!/usr/bin/env python3
"""
Layout guides for a multi-pose knife slash (idle + windup + centre pass + follow-through), for an image-generation brief.

The poses are designed in screen space (view-model camera, z = 1, vertical half height tan 25 deg; 16:9 reference with
the knife's right-edge follow 0.5, as MeleeViewProfile). For each pose the script derives, on a common canvas:
the grip pixel (pivot), blade tip, wrist, the forearm path to the off-screen end, and the area that must be drawn --
checked on iPhone 16 Pro (2622x1206), 16:9 and iPad 4:3 at the actual positions / rolls the timeline uses (not "any
angle": the arm is finite). Shapes in the output are placement guides, not art.

Writes into OUTPUT_DIR (names start with multipose_):
    multipose_pose<N>_<name>_canvas_guide.png   per pose: canvas with grip / tip / wrist / forearm band / must-draw area
    multipose_screen_layout_<screen>.png         per screen: each pose (and the timeline in-betweens) placed on the screen
    multipose_timeline.png                       when each image is shown, with the hit window
    multipose_layout_report.json                 all numbers
Reads: Assets/Game/Data/Melee_ProtoKnife.asset (timing), MeleeView_ProtoKnife_Candidate.asset (grip / follow),
Assets/Game/Art/Weapons/Knife/knife_candidate.placement.json (dot size), knife_candidate_opaque.png (idle reference).

Run by hand (needs Pillow):  python3 Tools/pixelart/make_knife_pose_guide.py OUTPUT_DIR
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

# ---- poses in screen units (16:9 reference, before the right-edge follow). grip = fist centre on the handle,
# tip = blade tip, wrist = wrist band centre, elbowDir = direction of the forearm from the wrist (toward the elbow /
# off screen), forearmWidth = sleeve width at the wrist (units). Values are the design; the image is drawn to match.
POSES = [
    {"id": 1, "name": "windup", "label": "振りかぶり",
     "grip": (0.58, -0.16), "tip": (0.70, 0.20), "wrist": (0.66, -0.28), "elbowDir": (0.35, -1.0),
     "view": "手の甲と小指側が見える。手首を外側へ返し、刃は上を向いて右肩の前へ引く。刃は寝かせず立てる（刃先が画面の上へ）。刃側が画面左、峰側が画面右、右手持ち",
     "forearmWidth": 0.16},
    {"id": 2, "name": "center_pass", "label": "斬撃・中央通過",
     "grip": (0.20, -0.20), "tip": (-0.20, -0.06), "wrist": (0.32, -0.27), "elbowDir": (0.75, -0.65),
     "view": "拳の親指側と指の背が見える。手首はまっすぐ、刃は左へ水平に近く倒れ、刃先は少し奥（遠近で短く細く）。刃が画面中央を横切る",
     "forearmWidth": 0.17},
    {"id": 3, "name": "follow_through", "label": "振り抜き",
     "grip": (-0.38, -0.30), "tip": (-0.72, -0.40), "wrist": (-0.26, -0.36), "elbowDir": (0.55, -0.85),
     "view": "手首を内側へ返し、手のひら側と指先が少し見える。刃は左下へ流れ、刃の腹が見える。前腕は画面の下中央へ向かって斜めに横切る",
     "forearmWidth": 0.18},
]

# ---- timeline (fractions of each phase) and in-pose motion: which image is shown, where it moves within its window.
# offsets in units relative to the pose's own place, roll in degrees.
TIMELINE = [
    # phase, from k, to k, image, offset at start, offset at end, roll start, roll end
    ("windup", 0.00, 0.30, "idle", (0.00, 0.00), (0.04, 0.03), 0, -4),
    ("windup", 0.30, 1.00, "windup", (-0.02, -0.02), (0.02, 0.02), 4, -2),
    ("active", 0.00, 0.60, "center_pass", (0.30, 0.03), (-0.10, -0.01), 6, -4),
    ("active", 0.60, 1.00, "follow_through", (0.06, 0.03), (-0.03, -0.01), 4, -2),
    ("recovery", 0.00, 0.35, "follow_through", (-0.03, -0.01), (0.00, -0.30), -2, -8),
    ("recovery", 0.35, 1.00, "idle", (0.00, -0.30), (0.00, 0.00), 0, 0),
]


def font(size):
    return ImageFont.truetype(FONT, size) if os.path.exists(FONT) else ImageFont.load_default()


def read_asset(path):
    values = {}
    with open(os.path.join(ROOT, path), encoding="utf-8") as f:
        for line in f:
            m = re.match(r"\s+(\w+): \{x: ([-\d.e]+), y: ([-\d.e]+)\}", line)
            if m:
                values[m.group(1)] = (float(m.group(2)), float(m.group(3)))
                continue
            m = re.match(r"\s+(\w+): ([-\d.e]+)\s*$", line)
            if m:
                values[m.group(1)] = float(m.group(2))
    return values


def main():
    out = sys.argv[1]
    os.makedirs(out, exist_ok=True)
    timing = read_asset("Assets/Game/Data/Melee_ProtoKnife.asset")
    view = read_asset("Assets/Game/Data/MeleeView_ProtoKnife_Candidate.asset")
    with open(os.path.join(ROOT, "Assets/Game/Art/Weapons/Knife/knife_candidate.placement.json"), encoding="utf-8") as f:
        place = json.load(f)
    ppu = place["screenHeightPixels"] / (2 * HALF)
    ref, follow = view["referenceAspect"], view["rightEdgeFollow"]
    rest = view["gripPosition"]

    def edge(aspect):
        return (aspect - ref) * HALF * follow

    # ---- per pose: canvas extents in pixels relative to the grip, forearm length needed to be off screen everywhere
    poses = {p["name"]: p for p in POSES}
    windows = {}
    for phase, k0, k1, img, o0, o1, r0, r1 in TIMELINE:
        windows.setdefault(img, []).append((phase, k0, k1, o0, o1, r0, r1))

    report = {"pixelsPerUnit": round(ppu, 3), "timing": {k: timing[k] for k in ("windup", "activeTime", "recovery")},
              "rightEdgeFollow": follow, "referenceAspect": ref, "poses": [], "timeline": []}

    def pose_points(p, aspect, off, roll, length):
        """Screen points of the pose with an in-window offset / roll (roll around the grip) on a screen."""
        e = edge(aspect)
        gx, gy = p["grip"][0] + e + off[0], p["grip"][1] + off[1]
        r = math.radians(roll)

        def tr(pt):
            dx, dy = pt[0] - p["grip"][0], pt[1] - p["grip"][1]
            return gx + dx * math.cos(r) - dy * math.sin(r), gy + dx * math.sin(r) + dy * math.cos(r)
        d = p["elbowDir"]
        L = math.hypot(*d)
        d = (d[0] / L, d[1] / L)
        n = (-d[1], d[0])
        w0 = p["forearmWidth"] / 2
        w1 = w0 * 1.35
        end = (p["wrist"][0] + d[0] * length, p["wrist"][1] + d[1] * length)
        band = [(p["wrist"][0] + n[0] * w0, p["wrist"][1] + n[1] * w0), (end[0] + n[0] * w1, end[1] + n[1] * w1),
                (end[0] - n[0] * w1, end[1] - n[1] * w1), (p["wrist"][0] - n[0] * w0, p["wrist"][1] - n[1] * w0)]
        cut = [(end[0] + n[0] * w1 * t, end[1] + n[1] * w1 * t) for t in [i / 5 - 1 for i in range(11)]]
        return tr(p["grip"]), tr(p["tip"]), tr(p["wrist"]), [tr(q) for q in band], [tr(q) for q in cut]

    for p in POSES:
        need = 0.0
        for name, sw, sh in SCREENS:
            a = sw / sh
            hw = HALF * a
            for phase, k0, k1, o0, o1, r0, r1 in windows.get(p["name"], []):
                for i in range(11):
                    t = i / 10
                    off = (o0[0] + (o1[0] - o0[0]) * t, o0[1] + (o1[1] - o0[1]) * t)
                    roll = r0 + (r1 - r0) * t
                    L = 0.05
                    while True:
                        cut = pose_points(p, a, off, roll, L)[4]
                        if all(not (-hw < x < hw and -HALF < y < HALF) for x, y in cut):
                            break
                        L += 0.01
                    need = max(need, L)
        p["forearmLength"] = round(need + 0.06, 3)  # + margin so the cut never touches the screen edge
        report["poses"].append({"id": p["id"], "name": p["name"]})

    # ---- common canvas: union of every pose's content (tip, fist, forearm band to its end) around the grip
    hand_r = 0.14
    exts = []
    for p in POSES:
        gp = p["grip"]
        pts = [p["tip"], (gp[0] - hand_r, gp[1] - hand_r), (gp[0] + hand_r, gp[1] + hand_r)]
        pts += pose_points(p, ref, (0, 0), 0, p["forearmLength"])[3]
        pts = [(x - gp[0], y - gp[1]) for x, y in pts]
        exts.append((min(x for x, _ in pts), max(x for x, _ in pts), min(y for _, y in pts), max(y for _, y in pts)))
    margin = 0.04
    left = max(-e[0] for e in exts) + margin
    right = max(e[1] for e in exts) + margin
    up = max(e[3] for e in exts) + margin
    down = max(-e[2] for e in exts) + margin
    W = int(math.ceil((left + right) * ppu / 4.0)) * 4
    H = int(math.ceil((up + down) * ppu / 4.0)) * 4
    grip_px = (round(left * ppu), round(up * ppu))
    report["canvas"] = {"size": [W, H], "gripPixel": list(grip_px), "note": "same canvas and same grip pixel for all three poses; origin top-left, x right, y down"}

    def to_px(p, pt):
        return (grip_px[0] + (pt[0] - p["grip"][0]) * ppu, grip_px[1] - (pt[1] - p["grip"][1]) * ppu)

    idle_img = Image.open(os.path.join(ROOT, "Assets/Game/Art/Weapons/Knife/knife_candidate_opaque.png")).convert("RGBA")
    idle_grip = place["grip"]
    f26, f22 = font(26), font(22)

    for p in POSES:
        band = pose_points(p, ref, (0, 0), 0, p["forearmLength"])[3]
        info = {
            "id": p["id"], "name": p["name"], "label": p["label"], "view": p["view"],
            "screenGrip16x9": p["grip"], "screenTip16x9": p["tip"],
            "gripPixel": list(grip_px), "tipPixel": [round(v) for v in to_px(p, p["tip"])],
            "wristPixel": [round(v) for v in to_px(p, p["wrist"])],
            "forearmEndPixel": [round(v) for v in to_px(p, ((band[1][0] + band[2][0]) / 2, (band[1][1] + band[2][1]) / 2))],
            "forearmLengthUnits": p["forearmLength"],
            "forearmBandPixels": [[round(v) for v in to_px(p, q)] for q in band],
        }
        report["poses"][p["id"] - 1] = info
        # canvas guide
        img = Image.new("RGBA", (W, H), (248, 248, 248, 255))
        d = ImageDraw.Draw(img, "RGBA")
        for x in range(0, W, 100):
            d.line([x, 0, x, H], fill=(228, 228, 228, 255))
        for y in range(0, H, 100):
            d.line([0, y, W, y], fill=(228, 228, 228, 255))
        bp = [to_px(p, q) for q in band]
        d.polygon(bp, fill=(110, 120, 60, 70), outline=(70, 80, 30, 255))
        g, t, w = to_px(p, p["grip"]), to_px(p, p["tip"]), to_px(p, p["wrist"])
        d.line([g, t], fill=(90, 100, 120, 255), width=10)
        d.ellipse([g[0] - hand_r * ppu * 0.8, g[1] - hand_r * ppu * 0.8, g[0] + hand_r * ppu * 0.8, g[1] + hand_r * ppu * 0.8], outline=(150, 90, 50, 255), width=5)
        for (x, y), col, lab in ((g, (220, 0, 0), f"握りの中心（ピボット）{tuple(round(v) for v in g)}"), (t, (40, 80, 200), f"刃先 {tuple(round(v) for v in t)}"), (w, (20, 120, 40), f"手首の帯 {tuple(round(v) for v in w)}")):
            d.line([x - 26, y, x + 26, y], fill=col + (255,), width=4)
            d.line([x, y - 26, x, y + 26], fill=col + (255,), width=4)
            d.text((x + 30, y - 16), lab, fill=col + (255,), font=f22, stroke_width=3, stroke_fill=(255, 255, 255))
        end = info["forearmEndPixel"]
        d.text((end[0] - 420, min(H - 70, end[1] + 10)), "袖はここまで途切れずに描く（先の切り口は画面に出ない）", fill=(60, 70, 20, 255), font=f22, stroke_width=3, stroke_fill=(255, 255, 255))
        d.text((16, 12), f"ポーズ{p['id']}：{p['label']}（{p['name']}）　キャンバス {W}×{H}（3ポーズ共通）　構図ガイド：図形は位置の目安で、絵ではない", fill=(0, 0, 0, 255), font=f26)
        d.text((16, 50), f"見え方：{p['view']}", fill=(0, 0, 0, 255), font=f22)
        img.convert("RGB").save(os.path.join(out, f"multipose_pose{p['id']}_{p['name']}_canvas_guide.png"))

    # ---- timeline sampled every 1/60 s: which image, where (16:9 reference), blade tip on screen
    total = timing["windup"] + timing["activeTime"] + timing["recovery"]
    phases = [("windup", 0.0, timing["windup"]), ("active", timing["windup"], timing["windup"] + timing["activeTime"]),
              ("recovery", timing["windup"] + timing["activeTime"], total)]
    samples = []
    steps = int(round(total * 60))
    for i in range(steps + 1):
        tt = i / 60
        ph, a0, a1 = next(x for x in phases if tt <= x[2] + 1e-9)
        k = 0.0 if a1 == a0 else min(1.0, (tt - a0) / (a1 - a0))
        seg = next(s for s in TIMELINE if s[0] == ph and s[1] - 1e-9 <= k <= s[2] + 1e-9)
        u = 0.0 if seg[2] == seg[1] else (k - seg[1]) / (seg[2] - seg[1])
        u = 1 - (1 - u) ** 2 if ph == "active" else u * u * (3 - 2 * u)
        off = (seg[4][0] + (seg[5][0] - seg[4][0]) * u, seg[4][1] + (seg[5][1] - seg[4][1]) * u)
        roll = seg[6] + (seg[7] - seg[6]) * u
        samples.append((round(tt, 4), ph, round(k, 3), seg[3], off, roll))
    report["timeline"] = [{"t": s[0], "phase": s[1], "k": s[2], "image": s[3], "offset": [round(v, 4) for v in s[4]], "roll": round(s[5], 2)} for s in samples]

    # blade crossing the centre in the hit window (16:9): tip / grip x of center_pass and follow_through samples
    cross = []
    for s in samples:
        if s[1] == "active" and s[3] in poses:
            g, t, *_ = pose_points(poses[s[3]], ref, s[4], s[5], 0.1)
            cross.append({"t": s[0], "image": s[3], "gripX": round(g[0], 3), "tipX": round(t[0], 3), "tipY": round(t[1], 3)})
    report["hitWindowBlade"] = cross

    # ---- screen layouts: every pose sample on each screen, cut-edge check on the real samples
    report["cutEdgeCheck"] = []
    for name, sw, sh in SCREENS:
        a = sw / sh
        hw = HALF * a
        cols = 4
        picks = [s for s in samples if s[3] != "idle"][::2]
        tw = 520
        th = int(tw * sh / sw)
        rows = (len(picks) + cols - 1) // cols
        sheet = Image.new("RGB", (cols * (tw + 8), 60 + rows * (th + 30)), "white")
        ds = ImageDraw.Draw(sheet)
        ds.text((6, 10), f"{name} {sw}×{sh}：配置予定図（1/30秒ごと。色面は位置の目安で絵ではない。白十字＝画面中央、黄線＝共通キャンバスの外周）", fill="black", font=f22)
        bad = 0
        for s in samples:  # every 1/60 s sample that shows a new pose
            if s[3] == "idle":
                continue
            p = poses[s[3]]
            cut = pose_points(p, a, s[4], s[5], p["forearmLength"])[4]
            if any(-hw < q[0] < hw and -HALF < q[1] < HALF for q in cut):
                bad += 1
        for i, s in enumerate(picks):
            p = poses[s[3]]
            g, t, w, band, cut = pose_points(p, a, s[4], s[5], p["forearmLength"])
            vis = [q for q in cut if -hw < q[0] < hw and -HALF < q[1] < HALF]
            im = Image.new("RGB", (sw, sh), (52, 54, 62))
            dd = ImageDraw.Draw(im, "RGBA")

            def S(q):
                return ((q[0] / hw + 1) / 2 * sw, (1 - (q[1] / HALF + 1) / 2) * sh)
            dd.polygon([S(q) for q in band], fill=(110, 120, 60, 255))
            gs = S(g)
            rr = hand_r * 0.8 / HALF * sh / 2
            dd.ellipse([gs[0] - rr, gs[1] - rr, gs[0] + rr, gs[1] + rr], fill=(150, 90, 50, 255))
            dd.line([S(g), S(t)], fill=(180, 190, 205, 255), width=max(4, int(sh * 0.012)))
            # canvas outline
            corners = [(0, 0), (W, 0), (W, H), (0, H)]
            def from_px(px):
                ux, uy = (px[0] - grip_px[0]) / ppu, (grip_px[1] - px[1]) / ppu
                r = math.radians(s[5])
                return (g[0] + ux * math.cos(r) - uy * math.sin(r), g[1] + ux * math.sin(r) + uy * math.cos(r))
            dd.line([S(from_px(c)) for c in corners + [corners[0]]], fill=(255, 210, 0, 255), width=3)
            dd.line([sw / 2 - 18, sh / 2, sw / 2 + 18, sh / 2], fill=(255, 255, 255, 255), width=3)
            dd.line([sw / 2, sh / 2 - 18, sw / 2, sh / 2 + 18], fill=(255, 255, 255, 255), width=3)
            tile = im.resize((tw, th), Image.LANCZOS)
            x = (i % cols) * (tw + 8)
            y = 60 + (i // cols) * (th + 30)
            ds.text((x + 4, y + 4), f"{s[0]:.3f}s {s[1]} {poses[s[3]]['label']}" + ("  ※切り口が画面内" if vis else ""), fill="black", font=font(18))
            sheet.paste(tile, (x, y + 26))
        sheet.save(os.path.join(out, f"multipose_screen_layout_{name}_{sw}x{sh}.png"))
        report["cutEdgeCheck"].append({"screen": name, "samplesWithCutOnScreen": bad, "samples": sum(1 for s in samples if s[3] != "idle")})

    # ---- timeline diagram
    tl = Image.new("RGB", (1500, 300), "white")
    dt = ImageDraw.Draw(tl)
    x0, x1 = 60, 1440
    def X(t):
        return x0 + (x1 - x0) * t / total
    dt.text((10, 8), f"表示する画像の時間割（全{total:.2f}秒、命中時間帯 {timing['windup']:.2f}〜{timing['windup'] + timing['activeTime']:.2f}秒）", fill="black", font=f26)
    dt.rectangle([X(timing["windup"]), 60, X(timing["windup"] + timing["activeTime"]), 250], fill=(255, 220, 220))
    dt.text((X(timing["windup"]) + 4, 64), "命中時間帯", fill=(200, 0, 0), font=f22)
    colors = {"idle": (180, 180, 180), "windup": (120, 160, 230), "center_pass": (230, 140, 60), "follow_through": (120, 190, 120)}
    names = {"idle": "待機（既存）", "windup": "1 振りかぶり", "center_pass": "2 中央通過", "follow_through": "3 振り抜き"}
    for ph, a0, a1 in phases:
        for seg in [s for s in TIMELINE if s[0] == ph]:
            s0, s1 = a0 + (a1 - a0) * seg[1], a0 + (a1 - a0) * seg[2]
            dt.rectangle([X(s0), 110, X(s1), 170], fill=colors[seg[3]], outline=(60, 60, 60))
            dt.text((X(s0) + 4, 124), names[seg[3]], fill="black", font=font(18))
    for tt in [i * 0.04 for i in range(int(total / 0.04) + 1)]:
        dt.line([X(tt), 180, X(tt), 190], fill="black")
        dt.text((X(tt) - 12, 194), f"{tt:.2f}", fill="black", font=font(16))
    dt.text((60, 230), "戻り：振り抜きの画像のまま下へ沈め、待機の画像を下から戻す（攻撃画像の逆再生はしない）", fill="black", font=f22)
    tl.save(os.path.join(out, "multipose_timeline.png"))

    with open(os.path.join(out, "multipose_layout_report.json"), "w", encoding="utf-8") as f:
        json.dump(report, f, indent=2, ensure_ascii=False)
    print(json.dumps({k: report[k] for k in ("canvas", "cutEdgeCheck")}, ensure_ascii=False, indent=1))
    for p in report["poses"]:
        print(p["id"], p["name"], "grip", p["gripPixel"], "tip", p["tipPixel"], "wrist", p["wristPixel"], "forearm end", p["forearmEndPixel"], "len", p["forearmLengthUnits"])
    print("hit window blade:", [(c["t"], c["image"], c["tipX"]) for c in cross])


if __name__ == "__main__":
    main()
