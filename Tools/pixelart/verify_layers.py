#!/usr/bin/env python3
"""
Checks part layers and masks made by split_layers.py against the base image (read only; writes nothing but the
optional report / diff image).

Recomposition: the layers are alpha-composited in compositeOrder at their original position and compared with the
base image:
    alpha mismatches            pixels whose alpha differs
    visible RGB mismatches      pixels visible in the base or the composite whose RGB differs
    transparent RGB differences pixels transparent in both whose stored RGB differs (no visible effect; reported apart)
    overlaps                    pixels opaque in more than one layer
    missing                     pixels opaque in the base but in no layer
    extra                       pixels opaque in a layer but transparent in the base
    layer pixels changed        opaque layer pixels whose RGBA differs from the base pixel
Masks: overlaps between masks of a hand, mask pixels outside their hand's layer, mask pixels on the weapon layer,
mask pixels on line-black pixels (max channel < maskLineMax; they keep their colour), non-white / non-binary mask pixels.

Run (needs Pillow):  python3 Tools/pixelart/verify_layers.py DEFINITION.json [--report R.json] [--diff DIFF.png]
Exit code 1 when the recomposition or a mask check fails.
"""
import json
import os
import sys

from PIL import Image

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))


def main():
    definition = json.load(open(sys.argv[1], encoding="utf-8"))
    base = Image.open(os.path.join(ROOT, definition["base"])).convert("RGBA")
    W, H = base.size
    bp = base.load()
    out_dir = os.path.join(ROOT, definition["outDir"])
    layers = {p: Image.open(os.path.join(out_dir, f)).convert("RGBA") for p, f in definition["layerFiles"].items()}
    for p, layer in layers.items():
        if layer.size != base.size:
            raise SystemExit(f"layer {p} is {layer.size}, base is {base.size}")
    composite = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    for p in definition["compositeOrder"]:
        composite = Image.alpha_composite(composite, layers[p])
    cp = composite.load()
    lps = {p: layer.load() for p, layer in layers.items()}

    alpha_mismatch = visible_rgb = transparent_rgb = overlaps = missing = extra = changed = 0
    owner = {}
    diff = Image.new("RGB", (W, H), (0, 0, 0))
    dp = diff.load()
    for y in range(H):
        for x in range(W):
            b, c = bp[x, y], cp[x, y]
            present = [p for p in layers if lps[p][x, y][3] > 0]
            if len(present) > 1:
                overlaps += 1
            for p in present:
                if lps[p][x, y] != b:
                    changed += 1
            if b[3] > 0 and not present:
                missing += 1
            if b[3] == 0 and present:
                extra += 1
            if present:
                owner[(x, y)] = present[0]
            if b[3] != c[3]:
                alpha_mismatch += 1
                dp[x, y] = (255, 0, 0)
            if b[3] > 0 or c[3] > 0:
                if b[:3] != c[:3]:
                    visible_rgb += 1
                    dp[x, y] = (255, 255, 0)
            elif b[:3] != c[:3]:
                transparent_rgb += 1
            if b[3] > 0 and dp[x, y] == (0, 0, 0):
                dp[x, y] = (40, 40, 40)

    dark_max = definition["darkMax"]
    mask_dir = os.path.join(out_dir, definition["maskDir"])
    mask_report = {}
    mask_ok = True
    for hand in definition["hands"]:
        seen = {}
        hand_report = {}
        for m in definition["maskNames"]:
            path = os.path.join(mask_dir, definition["maskFile"].format(hand=hand, mask=m))
            mask = Image.open(path).convert("RGBA")
            mp = mask.load()
            n = outside = on_weapon = on_outline = bad_value = overlap = 0
            for y in range(H):
                for x in range(W):
                    v = mp[x, y]
                    if v[3] == 0:
                        continue
                    if v != (255, 255, 255, 255):
                        bad_value += 1
                    n += 1
                    o = owner.get((x, y))
                    if o != hand:
                        outside += 1
                    if o == "weapon":
                        on_weapon += 1
                    if max(bp[x, y][:3]) < definition["maskLineMax"]:
                        on_outline += 1
                    if (x, y) in seen:
                        overlap += 1
                    seen[(x, y)] = m
            hand_report[m] = {"pixels": n, "outsideHandLayer": outside, "onWeapon": on_weapon, "onOutline": on_outline,
                              "overlapWithOtherMask": overlap, "notWhiteOpaque": bad_value}
            mask_ok &= outside == 0 and on_weapon == 0 and overlap == 0 and bad_value == 0 and on_outline == 0
        mask_report[hand] = hand_report

    weapon_same = all(lps["weapon"][x, y] == bp[x, y] for y in range(H) for x in range(W) if lps["weapon"][x, y][3] > 0)
    report = {
        "base": definition["base"],
        "size": [W, H],
        "baseOpaquePixels": sum(1 for y in range(H) for x in range(W) if bp[x, y][3] > 0),
        "layerOpaquePixels": {p: sum(1 for y in range(H) for x in range(W) if lps[p][x, y][3] > 0) for p in layers},
        "recomposition": {
            "alphaMismatches": alpha_mismatch,
            "visibleRgbMismatches": visible_rgb,
            "transparentRgbDifferences": transparent_rgb,
            "overlaps": overlaps,
            "missing": missing,
            "extra": extra,
            "layerPixelsChanged": changed,
        },
        "weaponLayerRgbaSameAsBase": weapon_same,
        "masks": mask_report,
    }
    ok = alpha_mismatch == 0 and visible_rgb == 0 and overlaps == 0 and missing == 0 and extra == 0 and changed == 0 and mask_ok
    report["ok"] = ok
    if "--report" in sys.argv:
        with open(sys.argv[sys.argv.index("--report") + 1], "w", encoding="utf-8") as f:
            json.dump(report, f, indent=2, ensure_ascii=False)
    if "--diff" in sys.argv:
        diff.save(sys.argv[sys.argv.index("--diff") + 1])
    print(json.dumps(report, indent=2, ensure_ascii=False))
    sys.exit(0 if ok else 1)


if __name__ == "__main__":
    main()
