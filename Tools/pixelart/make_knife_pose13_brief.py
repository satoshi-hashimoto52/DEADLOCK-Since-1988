#!/usr/bin/env python3
"""
Canvas / sleeve requirements for knife pose 1 (windup) and pose 3 (follow-through), re-checked after the pose 2 review.

Pose 2 showed that a delivered image can differ from the guide: the canvas was cropped smaller and the sleeve ran in
another direction (about 29 deg below horizontal instead of 41 deg). So for each pose this script does not assume the
guide's exact forearm direction: it tries the nominal direction and +/- 15 deg (5 deg steps), finds for each the
forearm length (from the wrist) that keeps the sleeve's cut edge off screen at every 1/60 s sample of the timeline in
which the pose is shown (iPhone 16 Pro, 16:9, iPad 4:3 -- real positions / rolls, not "any angle"), and sizes the
pose's own canvas so the sleeve can be drawn to that length in any of those directions (+ margin). The canvas and grip
pixel are per pose (pose 2's 1680x1400 / (610, 638) are not reused).

Pose geometry (grip / tip / wrist / forearm width / timeline) is read from make_knife_pose_guide.py.

Writes into OUTPUT_DIR:
    pose<N>_<name>_brief_guide.png   canvas at full size: grip, tip, wrist, the sleeve fan (where the sleeve may run) and
                                     the minimum sleeve length, the area that is ever on screen. GUIDE ONLY (not art).
    pose13_requirements.json         numbers
Run by hand (needs Pillow):  python3 Tools/pixelart/make_knife_pose13_brief.py OUTPUT_DIR
"""
import json
import math
import os
import sys

from PIL import Image, ImageDraw, ImageFont

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import make_knife_pose_guide as G  # noqa: E402

HALF = G.HALF
SPREAD = range(-15, 16, 5)          # forearm direction variations, degrees
LENGTH_MARGIN = 0.08                # units added to the worst required length
CANVAS_MARGIN = 0.05                # units around everything
HAND_R = 0.14                       # fist radius (units), as the multipose guide


def rotate(v, deg):
    r = math.radians(deg)
    return v[0] * math.cos(r) - v[1] * math.sin(r), v[0] * math.sin(r) + v[1] * math.cos(r)


def main():
    out = sys.argv[1]
    os.makedirs(out, exist_ok=True)
    timing = G.read_asset("Assets/Game/Data/Melee_ProtoKnife.asset")
    view = G.read_asset("Assets/Game/Data/MeleeView_ProtoKnife_Candidate.asset")
    with open(os.path.join(G.ROOT, "Assets/Game/Art/Weapons/Knife/knife_candidate.placement.json"), encoding="utf-8") as f:
        ppu = json.load(f)["screenHeightPixels"] / (2 * HALF)
    ref, follow = view["referenceAspect"], view["rightEdgeFollow"]
    phases = {"windup": (0.0, timing["windup"]), "active": (timing["windup"], timing["windup"] + timing["activeTime"]),
              "recovery": (timing["windup"] + timing["activeTime"], timing["windup"] + timing["activeTime"] + timing["recovery"])}

    def samples(name):
        """(offset, roll) of every 1/60 s sample in which the pose is shown."""
        res = []
        for phase, k0, k1, img, o0, o1, r0, r1 in G.TIMELINE:
            if img != name:
                continue
            a0, a1 = phases[phase]
            t0, t1 = a0 + (a1 - a0) * k0, a0 + (a1 - a0) * k1
            n = max(2, int(round((t1 - t0) * 60)) + 1)
            for i in range(n):
                u = i / (n - 1)
                u = 1 - (1 - u) ** 2 if phase == "active" else u * u * (3 - 2 * u)
                res.append(((o0[0] + (o1[0] - o0[0]) * u, o0[1] + (o1[1] - o0[1]) * u), r0 + (r1 - r0) * u))
        return res

    def on_screen(p, aspect, off, roll, q):
        e = (aspect - ref) * HALF * follow
        dx, dy = q[0] - p["grip"][0], q[1] - p["grip"][1]
        x, y = rotate((dx, dy), roll)
        X, Y = p["grip"][0] + e + off[0] + x, p["grip"][1] + off[1] + y
        return -HALF * aspect < X < HALF * aspect and -HALF < Y < HALF

    report = {"pixelsPerUnit": round(ppu, 3), "directionSpreadDeg": [min(SPREAD), max(SPREAD)], "poses": []}
    font = ImageFont.truetype(G.FONT, 24) if os.path.exists(G.FONT) else ImageFont.load_default()
    f20 = ImageFont.truetype(G.FONT, 20) if os.path.exists(G.FONT) else ImageFont.load_default()

    for p in [q for q in G.POSES if q["name"] in ("windup", "follow_through")]:
        smp = samples(p["name"])
        base = p["elbowDir"]
        L0 = math.hypot(*base)
        base = (base[0] / L0, base[1] / L0)
        w0 = p["forearmWidth"] / 2
        w1 = w0 * 1.35
        need = {}
        for deg in SPREAD:
            d = rotate(base, deg)
            n = (-d[1], d[0])
            L = 0.05
            while L < 3.0:
                end = (p["wrist"][0] + d[0] * L, p["wrist"][1] + d[1] * L)
                cut = [(end[0] + n[0] * w1 * t, end[1] + n[1] * w1 * t) for t in [i / 5 - 1 for i in range(11)]]
                if not any(on_screen(p, a, off, roll, q) for _, sw, sh in G.SCREENS for a in [sw / sh] for off, roll in smp for q in cut):
                    break
                L += 0.01
            need[deg] = round(L, 3)
        length = max(need.values()) + LENGTH_MARGIN

        # canvas: tip, fist, and the sleeve band to `length` in every direction of the spread
        pts = [p["tip"], (p["grip"][0] - HAND_R, p["grip"][1] - HAND_R), (p["grip"][0] + HAND_R, p["grip"][1] + HAND_R)]
        fan = []
        for deg in SPREAD:
            d = rotate(base, deg)
            n = (-d[1], d[0])
            end = (p["wrist"][0] + d[0] * length, p["wrist"][1] + d[1] * length)
            band = [(p["wrist"][0] + n[0] * w0, p["wrist"][1] + n[1] * w0), (end[0] + n[0] * w1, end[1] + n[1] * w1),
                    (end[0] - n[0] * w1, end[1] - n[1] * w1), (p["wrist"][0] - n[0] * w0, p["wrist"][1] - n[1] * w0)]
            fan.append((deg, band, end))
            pts += band
        xs = [q[0] - p["grip"][0] for q in pts]
        ys = [q[1] - p["grip"][1] for q in pts]
        left, right = -min(xs) + CANVAS_MARGIN, max(xs) + CANVAS_MARGIN
        up, down = max(ys) + CANVAS_MARGIN, -min(ys) + CANVAS_MARGIN
        W = int(math.ceil((left + right) * ppu / 4.0)) * 4
        H = int(math.ceil((up + down) * ppu / 4.0)) * 4
        grip_px = (round(left * ppu), round(up * ppu))

        def px(q):
            return (grip_px[0] + (q[0] - p["grip"][0]) * ppu, grip_px[1] - (q[1] - p["grip"][1]) * ppu)

        def dpx(q):  # drawing position (canvas drawn below a header)
            x, y = px(q)
            return x, y + 110

        info = {
            "id": p["id"], "name": p["name"], "label": p["label"], "view": p["view"],
            "canvas": [W, H], "gripPixel": list(grip_px),
            "tipPixel": [round(v) for v in px(p["tip"])], "wristPixel": [round(v) for v in px(p["wrist"])],
            "nominalForearmDirectionDeg": round(math.degrees(math.atan2(-base[1], base[0])), 1),
            "requiredForearmLengthUnitsByDirection": need,
            "minimumSleeveLengthFromWristPx": round(length * ppu),
            "sleeveEndPixelsByDirection": {str(deg): [round(v) for v in px(end)] for deg, _, end in fan},
            "samplesChecked": len(smp) * len(G.SCREENS),
        }
        report["poses"].append(info)

        HEAD = 110
        img = Image.new("RGBA", (max(W, 1500), H + HEAD), (255, 255, 255, 255))
        dr = ImageDraw.Draw(img, "RGBA")
        dr.rectangle([0, HEAD, W - 1, HEAD + H - 1], fill=(248, 248, 248, 255), outline=(0, 120, 255, 255), width=3)
        for x in range(0, W, 100):
            dr.line([x, HEAD, x, HEAD + H], fill=(228, 228, 228, 255))
        for y in range(0, H, 100):
            dr.line([0, HEAD + y, W, HEAD + y], fill=(228, 228, 228, 255))
        for deg, band, end in fan:
            col = (110, 120, 60, 110 if deg == 0 else 35)
            dr.polygon([dpx(q) for q in band], fill=col, outline=(70, 80, 30, 255 if deg == 0 else 90))
        g, t, w = dpx(p["grip"]), dpx(p["tip"]), dpx(p["wrist"])
        gl, tl, wl = px(p["grip"]), px(p["tip"]), px(p["wrist"])
        dr.line([g, t], fill=(90, 100, 120, 255), width=10)
        rr = HAND_R * ppu * 0.8
        dr.ellipse([g[0] - rr, g[1] - rr, g[0] + rr, g[1] + rr], outline=(150, 90, 50, 255), width=5)
        for (x, y), col, lab in ((g, (220, 0, 0), f"握りの中心（ピボット）{tuple(round(v) for v in gl)}"), (t, (40, 80, 200), f"刃先 {tuple(round(v) for v in tl)}"), (w, (20, 120, 40), f"手首の帯 {tuple(round(v) for v in wl)}")):
            dr.line([x - 26, y, x + 26, y], fill=col + (255,), width=4)
            dr.line([x, y - 26, x, y + 26], fill=col + (255,), width=4)
            dr.text((x + 30, y - 14), lab, fill=col + (255,), font=f20, stroke_width=3, stroke_fill=(255, 255, 255))
        dr.text((16, 10), f"ポーズ{p['id']}：{p['label']}　キャンバス {W}×{H}（青枠、このポーズ専用、座標は青枠の左上が原点）　構図ガイド：図形は位置の目安で、絵ではない", fill=(0, 0, 0, 255), font=font)
        dr.text((16, 44), f"濃い緑＝推奨の袖の向き（{info['nominalForearmDirectionDeg']}°）、薄い緑＝±15°の許容範囲。どの向きでも手首から{info['minimumSleeveLengthFromWristPx']}px以上、キャンバスの端まで途切れずに描く", fill=(0, 0, 0, 255), font=f20)
        dr.text((16, 72), f"見え方：{p['view']}", fill=(0, 0, 0, 255), font=f20)
        img.convert("RGB").save(os.path.join(out, f"pose{p['id']}_{p['name']}_brief_guide.png"))

    with open(os.path.join(out, "pose13_requirements.json"), "w", encoding="utf-8") as f:
        json.dump(report, f, indent=2, ensure_ascii=False)
    for p in report["poses"]:
        print(p["id"], p["name"], "canvas", p["canvas"], "grip", p["gripPixel"], "tip", p["tipPixel"], "wrist", p["wristPixel"],
              "dir", p["nominalForearmDirectionDeg"], "need", p["requiredForearmLengthUnitsByDirection"], "min sleeve px", p["minimumSleeveLengthFromWristPx"])


if __name__ == "__main__":
    main()
