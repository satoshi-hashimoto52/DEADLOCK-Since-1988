#!/usr/bin/env python3
"""
Skin region map for one first-person image (the game's skin shader reads it; see Deadlock.View.SkinRegion).

Inputs are the part layers / masks made by split_layers.py (same canvas as the displayed image, no trimming) and the
same definition file. Two steps:
1. Weapon masks (only those listed under "weaponMasks" in the definition): on the weapon layer, cells = 4-connected
   areas of non-outline pixels (outline = all channels below darkMax, as split_layers.py). A cell belongs to a weapon
   mask when at least minShare of its pixels match one of the mask's colour rules (definition "weaponColours"); the mask is
   then every pixel of the cell whose max channel is at least maskLineMax (line black keeps its colour). Cells named by
   a pixelSeeds entry (furniture painted against steel with no outline between them) are split pixel by pixel instead:
   only their pixels that match the colour rules join the mask. Written as
   <maskDir>/<maskFile with hand = "weapon"> (white / transparent, like the hand masks).
2. Region map: one 8-bit grey PNG (skinRegionsFile) with value = region index x 32 for every masked pixel, 0 elsewhere.
   Regions: 1 weapon_body, 2 glove_body, 3 glove_reinforcement, 4 wrist_band, 5 sleeve_base, 6 sleeve_camo; the left
   and right hands share the hand regions (one player skin for both hands).

Checks (exit code 1 when one fails; nothing is written then): mask sizes = base size, no pixel in two masks, hand mask
pixels only on that hand's layer, weapon mask pixels only on the weapon layer (no colour leaks between weapon and
hands), no mask pixel on line black.

Run by hand (needs Pillow):  python3 Tools/pixelart/make_skin_regions.py DEFINITION.json [--force] [--report REPORT.json]
Existing outputs are not overwritten unless --force is given. The base image, the layers and the hand masks are only read.
"""
import collections
import json
import os
import sys

from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from split_layers import colour_class  # noqa: E402

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
REGIONS = {"weapon_body": 1, "glove_body": 2, "glove_reinforcement": 3, "wrist_band": 4, "sleeve_base": 5, "sleeve_camo": 6}
STEP = 32


def weapon_mask(layer, rules, dark_max, line_max, min_share, pixel_seeds=()):
    W, H = layer.size
    lp = layer.load()
    seen = bytearray(W * H)
    mask = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    mp = mask.load()
    cells = 0
    for y0 in range(H):
        for x0 in range(W):
            if seen[y0 * W + x0]:
                continue
            c = lp[x0, y0]
            if c[3] < 128 or max(c[:3]) < dark_max:
                continue
            stack, cell = [(x0, y0)], []
            seen[y0 * W + x0] = 1
            while stack:
                x, y = stack.pop()
                cell.append((x, y))
                for nx, ny in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)):
                    if 0 <= nx < W and 0 <= ny < H and not seen[ny * W + nx]:
                        q = lp[nx, ny]
                        if q[3] >= 128 and max(q[:3]) >= dark_max:
                            seen[ny * W + nx] = 1
                            stack.append((nx, ny))
            if pixel_seeds and not set(map(tuple, pixel_seeds)).isdisjoint(cell):
                cells += 1
                for x, y in cell:
                    if colour_class(lp[x, y], rules) in rules and max(lp[x, y][:3]) >= line_max:
                        mp[x, y] = (255, 255, 255, 255)
                continue
            hits = sum(1 for x, y in cell if colour_class(lp[x, y], rules) in rules)
            if hits >= min_share * len(cell):
                cells += 1
                for x, y in cell:
                    if max(lp[x, y][:3]) >= line_max:
                        mp[x, y] = (255, 255, 255, 255)
    return mask, cells


def main():
    args = sys.argv[1:]
    force = "--force" in args
    report_path = args[args.index("--report") + 1] if "--report" in args else None
    with open(args[0], encoding="utf-8") as f:
        d = json.load(f)
    out_dir = os.path.join(ROOT, d["outDir"])
    base = Image.open(os.path.join(ROOT, d["base"])).convert("RGBA")
    W, H = base.size
    bp = base.load()
    layers = {p: Image.open(os.path.join(out_dir, f)).convert("RGBA") for p, f in d["layerFiles"].items()}
    line_max = d["maskLineMax"]
    problems, report = [], {"definition": args[0], "size": [W, H], "weaponMasks": {}, "regions": {}}

    masks = {}  # (part, mask name) -> image
    new_files = {}
    for name, rule in d.get("weaponMasks", {}).items():
        rules = {k: {f: v for f, v in d["weaponColours"][k].items() if f != "why"} for k in rule["colours"]}
        m, cells = weapon_mask(layers["weapon"], rules, d["darkMax"], line_max, rule["minShare"],
                               [p["seed"] for p in rule.get("pixelSeeds", [])])
        masks[("weapon", name)] = m
        rel = os.path.join(d["maskDir"], d["maskFile"].format(hand="weapon", mask=name))
        new_files[rel] = m
        report["weaponMasks"][name] = {"file": rel, "cells": cells}
    for hand in d["layerFiles"]:
        if hand == "weapon":
            continue
        for name in d["maskNames"]:
            path = os.path.join(out_dir, d["maskDir"], d["maskFile"].format(hand=hand, mask=name))
            masks[(hand, name)] = Image.open(path).convert("RGBA")

    region = Image.new("L", (W, H), 0)
    rp = region.load()
    counts = collections.Counter()
    for (part, name), m in masks.items():
        if m.size != (W, H):
            problems.append(f"mask {part}:{name} is {m.size}, base is {(W, H)}")
            continue
        key = "weapon_body" if part == "weapon" and name == "body" else name
        if key not in REGIONS:
            problems.append(f"mask {part}:{name} has no skin region")
            continue
        value = REGIONS[key] * STEP
        mp, lp = m.load(), layers[part].load()
        for y in range(H):
            for x in range(W):
                if mp[x, y][3] == 0:
                    continue
                if rp[x, y]:
                    problems.append(f"pixel {(x, y)} is in two masks ({part}:{name} and region {rp[x, y] // STEP})")
                    continue
                if lp[x, y][3] < 128:
                    problems.append(f"mask {part}:{name} pixel {(x, y)} is not on the {part} layer (colour leak)")
                    continue
                if max(bp[x, y][:3]) < line_max:
                    problems.append(f"mask {part}:{name} pixel {(x, y)} is line black")
                    continue
                rp[x, y] = value
                counts[key] += 1
    report["regions"] = {k: counts.get(k, 0) for k in REGIONS}
    report["problems"] = problems[:50]
    report["problemCount"] = len(problems)

    if report_path:
        with open(report_path, "w", encoding="utf-8") as f:
            json.dump(report, f, indent=2, ensure_ascii=False)
    print(json.dumps({k: report[k] for k in ("weaponMasks", "regions", "problemCount")}, ensure_ascii=False))
    if problems:
        print("\n".join(problems[:20]))
        raise SystemExit(1)

    new_files[d["skinRegionsFile"]] = region
    for rel, img in new_files.items():
        path = os.path.join(out_dir, rel)
        if os.path.exists(path) and not force:
            raise SystemExit(f"{path} exists (use --force to overwrite)")
    for rel, img in new_files.items():
        img.save(os.path.join(out_dir, rel))
        print("written", os.path.join(d["outDir"], rel))


if __name__ == "__main__":
    main()
