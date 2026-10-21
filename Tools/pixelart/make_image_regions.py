#!/usr/bin/env python3
"""
Skin region map for ONE finished image that is shown as a whole (equipment screen: rifle side view, player front view),
without splitting it into part layers. The same region numbers as the first-person art (make_skin_regions.py,
Deadlock.View.SkinRegion): 1 weapon_body, 2 glove_body, 3 glove_reinforcement, 4 wrist_band, 5 sleeve_base,
6 sleeve_camo; region map value = index x 32, 0 = keep the drawn colour.

Everything is decided by a hand-written definition file (reproducible); the image is only read:
1. Cells: 4-connected areas of non-outline pixels (outline = all channels below darkMax), as split_layers.py. The black
   outlines of the art separate the cells, so a cell is one painted area.
2. Each region lists areas (rect / polygon, pixel coordinates from the top-left) and colour rules. A cell joins the
   region when at least insideShare of its pixels lie in the region's areas and at least minShare of its pixels match
   the colours; then all its pixels join. So colour alone never selects (the same olive on the sleeve, the jacket and
   the trousers is told apart by the areas and the outlines). pixelSeeds: a cell containing a seed is taken pixel by
   pixel instead (only matching pixels), for colours painted against others with no outline between them.
3. Line black (max channel below maskLineMax) is never in a mask. Dark fills (maskLineMax <= max channel < darkMax)
   inside a region's areas join the region they touch (shadowed sides of a band, etc.).
4. "split": a region can be divided into base / camo by local contrast (camo = clearly darker than the local light tone),
   as split_layers.py does for sleeves; tiny pieces (speckleMaxPixels) join the other side.

Checks (exit 1, nothing written): no pixel in two regions, no region pixel on line black / transparent / outside the
region's areas. Writes: <maskDir>/<maskFile with {mask}> per region (white / transparent) and the region map (8-bit grey).

Run by hand (needs Pillow):  python3 Tools/pixelart/make_image_regions.py DEFINITION.json [--force] [--report REPORT.json]
"""
import collections
import colorsys
import json
import os
import sys

from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from split_layers import colour_class, in_region  # noqa: E402

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
REGIONS = {"weapon_body": 1, "glove_body": 2, "glove_reinforcement": 3, "wrist_band": 4, "sleeve_base": 5, "sleeve_camo": 6}
STEP = 32


def lightness(c):
    return colorsys.rgb_to_hls(c[0] / 255, c[1] / 255, c[2] / 255)[1]


def main():
    args = sys.argv[1:]
    force = "--force" in args
    report_path = args[args.index("--report") + 1] if "--report" in args else None
    with open(args[0], encoding="utf-8") as f:
        d = json.load(f)
    img = Image.open(os.path.join(ROOT, d["base"])).convert("RGBA")
    W, H = img.size
    px = img.load()
    dark_max, line_max = d["darkMax"], d["maskLineMax"]
    rules_all = {k: {f: v for f, v in r.items() if f != "why"} for k, r in d["colours"].items()}
    areas = d["areas"]

    def inside(x, y, names):
        return any(in_region(x, y, areas[n]) for n in names)

    # 1. cells
    cell = [0] * (W * H)
    cells = {}
    for y0 in range(H):
        for x0 in range(W):
            c = px[x0, y0]
            if c[3] < 128 or max(c[:3]) < dark_max or cell[y0 * W + x0]:
                continue
            cid = len(cells) + 1
            cell[y0 * W + x0] = cid
            stack, pts = [(x0, y0)], []
            while stack:
                x, y = stack.pop()
                pts.append((x, y))
                for X, Y in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)):
                    if 0 <= X < W and 0 <= Y < H and not cell[Y * W + X]:
                        q = px[X, Y]
                        if q[3] >= 128 and max(q[:3]) >= dark_max:
                            cell[Y * W + X] = cid
                            stack.append((X, Y))
            cells[cid] = pts

    # 2. cells -> regions (in the order of the definition; a cell is taken once)
    owner = {}           # (x, y) -> region name (before split)
    taken = set()
    log = {}
    for name, r in d["regions"].items():
        rules = {k: rules_all[k] for k in r["colours"]}
        seeds = {tuple(s["seed"]) for s in r.get("pixelSeeds", [])}
        n_cells, n_seed = 0, 0
        for cid, pts in cells.items():
            if cid in taken:
                continue
            if seeds and not seeds.isdisjoint(pts):
                for (x, y) in pts:
                    if inside(x, y, r["areas"]) and colour_class(px[x, y], rules) in rules and max(px[x, y][:3]) >= line_max:
                        owner[(x, y)] = name
                taken.add(cid)
                n_seed += 1
                continue
            ins = sum(1 for (x, y) in pts if inside(x, y, r["areas"]))
            if ins < r.get("insideShare", 0.9) * len(pts):
                continue
            hits = sum(1 for (x, y) in pts if colour_class(px[x, y], rules) in rules)
            if hits < r["minShare"] * len(pts):
                continue
            taken.add(cid)
            n_cells += 1
            for (x, y) in pts:
                if max(px[x, y][:3]) >= line_max and inside(x, y, r["areas"]):
                    owner[(x, y)] = name
        log[name] = {"cells": n_cells, "pixelSeedCells": n_seed}

    # 3. dark fills inside the areas join the region they touch
    q = collections.deque(owner.keys())
    added = collections.Counter()
    while q:
        x, y = q.popleft()
        name = owner[(x, y)]
        for X, Y in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)):
            if 0 <= X < W and 0 <= Y < H and (X, Y) not in owner:
                c = px[X, Y]
                if c[3] >= 128 and line_max <= max(c[:3]) < dark_max and inside(X, Y, d["regions"][name]["areas"]):
                    owner[(X, Y)] = name
                    added[name] += 1
                    q.append((X, Y))

    # 4. split (base / camo by local contrast)
    final = {}
    for name, r in d["regions"].items():
        pts = {p for p, n in owner.items() if n == name}
        sp = r.get("split")
        if not sp:
            for p in pts:
                final[p] = r.get("region", name)
            continue
        rad, step = sp["window"], sp["sampleStep"]
        grid = {}
        for gy in range(0, H + step, step):
            for gx in range(0, W + step, step):
                vals = sorted(lightness(px[x, y]) for x in range(gx - rad, gx + rad + 1, 2) for y in range(gy - rad, gy + rad + 1, 2)
                              if (x, y) in pts)
                if vals:
                    grid[(gx, gy)] = vals[min(len(vals) - 1, int(len(vals) * sp["lightPercentile"]))]
        camo = set()
        for (x, y) in pts:
            local = grid.get((int(round(x / step)) * step, int(round(y / step)) * step))
            if local is not None and lightness(px[x, y]) < local - sp["darkerBy"]:
                camo.add((x, y))
        base = pts - camo
        moved = 0
        for src, dst in ((camo, base), (base, camo)):
            seen = set()
            for p0 in list(src):
                if p0 in seen or p0 not in src:
                    continue
                comp, st = [], [p0]
                seen.add(p0)
                while st:
                    x, y = st.pop()
                    comp.append((x, y))
                    for nb in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)):
                        if nb in src and nb not in seen:
                            seen.add(nb)
                            st.append(nb)
                if len(comp) < sp.get("speckleMaxPixels", 0):
                    for p in comp:
                        src.discard(p)
                        dst.add(p)
                    moved += len(comp)
        for p in base:
            final[p] = sp["base"]
        for p in camo:
            final[p] = sp["camo"]
        log[name]["speckleMoved"] = moved

    # checks
    problems = []
    region_of = {v: k for k, v in REGIONS.items()}
    area_of = {}
    for name, r in d["regions"].items():
        for out in ([r["split"]["base"], r["split"]["camo"]] if r.get("split") else [r.get("region", name)]):
            area_of[out] = r["areas"]
    for (x, y), reg in final.items():
        if reg not in REGIONS:
            problems.append(f"unknown region {reg}")
            break
        c = px[x, y]
        if c[3] < 128:
            problems.append(f"{reg} on transparent pixel {(x, y)}")
        if max(c[:3]) < line_max:
            problems.append(f"{reg} on line black {(x, y)}")
        if not inside(x, y, area_of[reg]):
            problems.append(f"{reg} outside its areas {(x, y)}")
    counts = collections.Counter(final.values())
    report = {"definition": args[0], "size": [W, H], "cells": len(cells), "regions": {k: counts.get(k, 0) for k in REGIONS},
              "log": log, "darkFillAdded": dict(added), "problemCount": len(problems), "problems": problems[:30]}
    if report_path:
        with open(report_path, "w", encoding="utf-8") as f:
            json.dump(report, f, indent=2, ensure_ascii=False)
    print(json.dumps({k: report[k] for k in ("regions", "log", "darkFillAdded", "problemCount")}, ensure_ascii=False))
    if problems:
        print("\n".join(problems[:20]))
        raise SystemExit(1)

    out_dir = os.path.join(ROOT, d["outDir"])
    files = {}
    region_img = Image.new("L", (W, H), 0)
    rp = region_img.load()
    for (x, y), reg in final.items():
        rp[x, y] = REGIONS[reg] * STEP
    for reg in sorted(set(final.values()), key=lambda k: REGIONS[k]):
        m = Image.new("RGBA", (W, H), (0, 0, 0, 0))
        mp = m.load()
        for (x, y), r_ in final.items():
            if r_ == reg:
                mp[x, y] = (255, 255, 255, 255)
        files[os.path.join(d["maskDir"], d["maskFile"].format(mask=reg))] = m
    files[d["skinRegionsFile"]] = region_img
    for rel in files:
        if os.path.exists(os.path.join(out_dir, rel)) and not force:
            raise SystemExit(f"{os.path.join(out_dir, rel)} exists (use --force to overwrite)")
    os.makedirs(os.path.join(out_dir, d["maskDir"]), exist_ok=True)
    for rel, im in files.items():
        im.save(os.path.join(out_dir, rel))
        print("written", os.path.join(d["outDir"], rel))


if __name__ == "__main__":
    main()
