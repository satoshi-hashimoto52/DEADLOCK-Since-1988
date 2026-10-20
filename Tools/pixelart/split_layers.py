#!/usr/bin/env python3
"""
Splits a finished first-person weapon image into part layers (weapon / left hand+arm / right hand+arm) and writes
colour-change masks, using a hand-written definition file (regions, seeds, rules) so the result is reproducible.

Only the visible pixels are divided: nothing is redrawn, moved, scaled or recoloured. Every opaque pixel of the base
image goes to exactly one layer with its RGB and alpha unchanged; layers keep the base image's size (no trimming).

How pixels are assigned (all parameters live in the definition file):
1. "Cells": 4-connected areas of non-outline pixels (outline = all channels below darkMax). The black outlines of the
   art separate the cells, so a cell is one painted area (a finger, a band piece, a gun part...).
2. Each cell gets a part: cellOverrides (by a seed pixel) first; otherwise a cell whose main colour is a hand colour
   (glove brown / sleeve olive) and whose seed lies in that hand's region goes to that hand; every other cell is the
   weapon. Colour alone never decides between weapon and hand: position (region) and the outline structure do.
3. Outline pixels: the hands are in front of the gun, so a hand owns the outline around it -- an outline pixel within
   outlineWidth (path through outline pixels) of a hand cell goes to that hand unless the weapon is closer by more than
   outlineWidth; all other outline pixels go to the nearest cell's part. Then small weapon pieces that touch only one
   hand and not the rest of the weapon (fewer than orphanMaxPixels) join that hand. pixelOverrides (polygons) last.
4. Masks (hands only; line-black pixels -- max channel below maskLineMax -- are excluded so they keep their colour;
   dark fills between maskLineMax and darkMax join the nearest mask of the same hand): glove body = glove-coloured cells, wrist band
   = cells marked "wrist_band", sleeve base / camo = sleeve-coloured pixels split by local contrast (camo = clearly
   darker than the light tone around it, so a shadowed base is not taken for camo). Masks that cannot be identified
   in the image (e.g. palm reinforcement) are written empty and listed as such in the report.

Run by hand (needs Pillow):  python3 Tools/pixelart/split_layers.py DEFINITION.json [--force] [--report REPORT.json]
Existing outputs are not overwritten unless --force is given; the base image is never written.
"""
import collections
import colorsys
import json
import math
import os
import sys

from PIL import Image

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))


def load_definition(path):
    with open(path, encoding="utf-8") as f:
        return json.load(f)


def colour_class(c, rules):
    r, g, b = c[:3]
    h, l, s = colorsys.rgb_to_hls(r / 255, g / 255, b / 255)
    hue = h * 360
    for name, rule in rules.items():
        if rule.get("satMax") is not None and s > rule["satMax"]:
            continue
        if rule.get("satMin") is not None and s < rule["satMin"]:
            continue
        if rule.get("hue") is not None and not (rule["hue"][0] <= hue <= rule["hue"][1]):
            continue
        if rule.get("lightMax") is not None and l > rule["lightMax"]:
            continue
        if rule.get("lightMin") is not None and l < rule["lightMin"]:
            continue
        return name
    return "other"


def in_region(x, y, region):
    if "rect" in region:
        x0, y0, x1, y1 = region["rect"]
        return x0 <= x <= x1 and y0 <= y <= y1
    return point_in_polygon(x, y, region["polygon"])


def point_in_polygon(x, y, poly):
    inside = False
    n = len(poly)
    for i in range(n):
        (x0, y0), (x1, y1) = poly[i], poly[(i + 1) % n]
        if (y0 > y) != (y1 > y) and x < x0 + (y - y0) * (x1 - x0) / (y1 - y0):
            inside = not inside
    return inside


def split(definition):
    base_path = os.path.join(ROOT, definition["base"])
    img = Image.open(base_path).convert("RGBA")
    W, H = img.size
    px = img.load()
    dark_max = definition["darkMax"]
    hands = definition["hands"]                      # {"left": {...}, "right": {...}}
    rules = definition["colours"]                    # colour class rules
    hand_colours = definition["handColours"]         # {"glove": [...classes], "sleeve": [...]}

    def is_outline(c):
        return c[3] > 0 and max(c[:3]) < dark_max

    # ---- 1. cells
    cell = [[0] * W for _ in range(H)]
    cells = {}
    for y in range(H):
        for x in range(W):
            c = px[x, y]
            if c[3] == 0 or is_outline(c) or cell[y][x]:
                continue
            cid = len(cells) + 1
            q = collections.deque([(x, y)])
            cell[y][x] = cid
            pts = []
            counts = collections.Counter()
            while q:
                u, v = q.popleft()
                pts.append((u, v))
                counts[colour_class(px[u, v], rules)] += 1
                for du, dv in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                    X, Y = u + du, v + dv
                    if 0 <= X < W and 0 <= Y < H and not cell[Y][X]:
                        d = px[X, Y]
                        if d[3] and not is_outline(d):
                            cell[Y][X] = cid
                            q.append((X, Y))
            cells[cid] = {"pixels": pts, "main": counts.most_common(1)[0][0], "seed": pts[0], "tags": set()}

    # ---- 2. cell parts
    part_of_cell = {}
    for cid, c in cells.items():
        part = "weapon"
        sx, sy = c["seed"]
        kind = next((k for k, classes in hand_colours.items() if c["main"] in classes), None)
        if kind is not None:
            for hand, info in hands.items():
                if in_region(sx, sy, info["region"]):
                    part = hand
                    c["tags"].add(kind)
                    break
        part_of_cell[cid] = part
    override_log = []
    for o in definition.get("cellOverrides", []):
        x, y = o["seed"]
        cid = cell[y][x]
        if not cid:
            raise SystemExit(f"cellOverride seed {o['seed']} is not inside a painted cell (outline or transparent)")
        part_of_cell[cid] = o["part"]
        cells[cid]["tags"] = set(o.get("tags", []))
        override_log.append({"seed": o["seed"], "cellPixels": len(cells[cid]["pixels"]), "part": o["part"], "tags": o.get("tags", []), "why": o.get("why", "")})

    part = [[None] * W for _ in range(H)]
    for cid, c in cells.items():
        for (x, y) in c["pixels"]:
            part[y][x] = part_of_cell[cid]

    # ---- 3. outline pixels: geodesic distance (through outline pixels) to each part
    width = definition["outlineWidth"]
    INF = 1 << 30

    def distances(target):
        dist = {}
        q = collections.deque()
        for y in range(H):
            for x in range(W):
                if part[y][x] == target:
                    for du, dv in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                        X, Y = x + du, y + dv
                        if 0 <= X < W and 0 <= Y < H and is_outline(px[X, Y]) and (X, Y) not in dist:
                            dist[(X, Y)] = 1
                            q.append((X, Y))
        while q:
            u, v = q.popleft()
            d = dist[(u, v)]
            for du, dv in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                X, Y = u + du, v + dv
                if 0 <= X < W and 0 <= Y < H and is_outline(px[X, Y]) and (X, Y) not in dist:
                    dist[(X, Y)] = d + 1
                    q.append((X, Y))
        return dist

    parts = ["weapon"] + list(hands.keys())
    dist = {p: distances(p) for p in parts}
    unreached = 0
    for y in range(H):
        for x in range(W):
            if not is_outline(px[x, y]):
                continue
            dw = dist["weapon"].get((x, y), INF)
            best_hand, dh = None, INF
            for hand in hands:
                d = dist[hand].get((x, y), INF)
                if d < dh:
                    best_hand, dh = hand, d
            if best_hand is not None and dh <= width and dh <= dw + width:
                part[y][x] = best_hand
            elif min(dw, dh) < INF:
                part[y][x] = "weapon" if dw <= dh else best_hand
            else:
                part[y][x] = "weapon"
                unreached += 1

    # Orphan pieces: a small piece of one part (8-connected) that does not touch the rest of that part and touches only
    # one hand (e.g. dark anti-aliased pixels on a glove's outer outline that formed a tiny grey cell) joins that hand.
    orphan_log = []
    limit = definition.get("orphanMaxPixels", 0)
    if limit:
        seen = set()
        for y in range(H):
            for x in range(W):
                p_ = part[y][x]
                if p_ != "weapon" or (x, y) in seen:
                    continue
                q = collections.deque([(x, y)])
                seen.add((x, y))
                comp = []
                while q:
                    u, v = q.popleft()
                    comp.append((u, v))
                    for du in (-1, 0, 1):
                        for dv in (-1, 0, 1):
                            X, Y = u + du, v + dv
                            if 0 <= X < W and 0 <= Y < H and (X, Y) not in seen and part[Y][X] == "weapon":
                                seen.add((X, Y))
                                q.append((X, Y))
                if len(comp) >= limit:
                    continue
                touching = set()
                for (u, v) in comp:
                    for du, dv in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                        X, Y = u + du, v + dv
                        if 0 <= X < W and 0 <= Y < H and part[Y][X] not in (None, "weapon"):
                            touching.add(part[Y][X])
                if len(touching) == 1:
                    hand = touching.pop()
                    for (u, v) in comp:
                        part[v][u] = hand
                    xs = [c[0] for c in comp]
                    ys = [c[1] for c in comp]
                    orphan_log.append({"pixels": len(comp), "bbox": [min(xs), min(ys), max(xs), max(ys)], "to": hand})

    pixel_log = []
    for o in definition.get("pixelOverrides", []):
        n = 0
        xs = [p[0] for p in o["polygon"]]
        ys = [p[1] for p in o["polygon"]]
        for y in range(max(0, min(ys)), min(H, max(ys) + 1)):
            for x in range(max(0, min(xs)), min(W, max(xs) + 1)):
                if px[x, y][3] and point_in_polygon(x + 0.5, y + 0.5, o["polygon"]) and (o.get("outlineOnly", True) is False or is_outline(px[x, y])):
                    if part[y][x] != o["part"]:
                        part[y][x] = o["part"]
                        n += 1
        pixel_log.append({"polygon": o["polygon"], "part": o["part"], "changed": n, "why": o.get("why", "")})

    # ---- 4. masks
    sleeve_rule = definition["sleeveCamo"]
    lum = lambda c: colorsys.rgb_to_hls(c[0] / 255, c[1] / 255, c[2] / 255)[1]
    masks = {hand: {m: set() for m in definition["maskNames"]} for hand in hands}
    for cid, c in cells.items():
        hand = part_of_cell[cid]
        if hand not in hands:
            continue
        tags = c["tags"]
        for (x, y) in c["pixels"]:
            if "wrist_band" in tags:
                masks[hand]["wrist_band"].add((x, y))
            elif "glove" in tags:
                masks[hand]["glove_body"].add((x, y))
            elif "sleeve" in tags:
                masks[hand]["sleeve_base"].add((x, y))   # split below
    r = sleeve_rule["window"]
    for hand in hands:
        sleeve = masks[hand]["sleeve_base"]
        if not sleeve:
            continue
        light = {}
        pts = list(sleeve)
        # local light tone: the given percentile of the lightness of sleeve pixels in a window (grid-sampled)
        step = sleeve_rule["sampleStep"]
        grid = {}
        for gy in range(0, H + step, step):
            for gx in range(0, W + step, step):
                vals = sorted(lum(px[x, y]) for x in range(gx - r, gx + r + 1, 2) for y in range(gy - r, gy + r + 1, 2)
                              if (x, y) in sleeve)
                if vals:
                    grid[(gx, gy)] = vals[min(len(vals) - 1, int(len(vals) * sleeve_rule["lightPercentile"]))]
        camo = set()
        for (x, y) in pts:
            gx, gy = int(round(x / step)) * step, int(round(y / step)) * step
            local = grid.get((gx, gy))
            if local is None:
                continue
            if lum(px[x, y]) < local - sleeve_rule["darkerBy"]:
                camo.add((x, y))
        masks[hand]["sleeve_camo"] = camo
        masks[hand]["sleeve_base"] = sleeve - camo
    # Dark fills: hand pixels darker than darkMax but not line-black (maskLineMax <= max channel < darkMax), e.g. the
    # shadowed side of the wrist band, belong to the nearest mask of the same hand (path through such dark pixels).
    # Line-black pixels (max channel < maskLineMax) stay out of every mask: they keep their colour.
    line_max = definition["maskLineMax"]
    dark_fill = 0
    for hand in hands:
        owner = {}
        q = collections.deque()
        for m, pts in masks[hand].items():
            for p_ in pts:
                owner[p_] = m
                q.append(p_)
        while q:
            u, v = q.popleft()
            for du, dv in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                X, Y = u + du, v + dv
                if 0 <= X < W and 0 <= Y < H and (X, Y) not in owner and part[Y][X] == hand:
                    c = px[X, Y]
                    if line_max <= max(c[:3]) < dark_max:
                        owner[(X, Y)] = owner[(u, v)]
                        masks[hand][owner[(u, v)]].add((X, Y))
                        dark_fill += 1
                        q.append((X, Y))
    return img, part, masks, cells, {"cellOverrides": override_log, "pixelOverrides": pixel_log, "unreachedOutline": unreached, "orphanPiecesMoved": orphan_log, "darkFillPixelsAddedToMasks": dark_fill}


def write_outputs(definition, img, part, masks, force):
    W, H = img.size
    px = img.load()
    out_dir = os.path.join(ROOT, definition["outDir"])
    base = os.path.abspath(os.path.join(ROOT, definition["base"]))
    files = {p: os.path.join(out_dir, f) for p, f in definition["layerFiles"].items()}
    mask_dir = os.path.join(out_dir, definition["maskDir"])
    mask_files = {(h, m): os.path.join(mask_dir, definition["maskFile"].format(hand=h, mask=m))
                  for h in masks for m in definition["maskNames"]}
    targets = list(files.values()) + list(mask_files.values())
    if any(os.path.abspath(t) == base for t in targets):
        raise SystemExit("an output path is the base image: refusing")
    existing = [t for t in targets if os.path.exists(t)]
    if existing and not force:
        raise SystemExit("outputs exist (use --force to regenerate):\n  " + "\n  ".join(os.path.relpath(t, ROOT) for t in existing))
    os.makedirs(mask_dir, exist_ok=True)
    for p, path in files.items():
        layer = Image.new("RGBA", (W, H), (0, 0, 0, 0))
        lp = layer.load()
        for y in range(H):
            for x in range(W):
                if part[y][x] == p:
                    lp[x, y] = px[x, y]
        layer.save(path)
    for (h, m), path in mask_files.items():
        mask = Image.new("RGBA", (W, H), (0, 0, 0, 0))
        mp = mask.load()
        for (x, y) in masks[h][m]:
            mp[x, y] = (255, 255, 255, 255)
        mask.save(path)
    return files, mask_files


def main():
    definition_path = sys.argv[1]
    definition = load_definition(definition_path)
    img, part, masks, cells, log = split(definition)
    files, mask_files = write_outputs(definition, img, part, masks, "--force" in sys.argv)
    counts = collections.Counter(part[y][x] for y in range(img.height) for x in range(img.width) if part[y][x])
    report = {
        "base": definition["base"],
        "size": list(img.size),
        "cells": len(cells),
        "layerPixels": dict(counts),
        "maskPixels": {f"{h}.{m}": len(v) for h in masks for m, v in masks[h].items()},
        "emptyMasks": [f"{h}.{m}" for h in masks for m, v in masks[h].items() if not v],
        **log,
    }
    if "--report" in sys.argv:
        with open(sys.argv[sys.argv.index("--report") + 1], "w", encoding="utf-8") as f:
            json.dump(report, f, indent=2, ensure_ascii=False)
    print(json.dumps(report, indent=2, ensure_ascii=False))


if __name__ == "__main__":
    main()
