#!/usr/bin/env python3
"""
Reuses an existing skin region map for a recoloured version of the same image (same canvas, same shapes, same alpha).

The map is copied as is, except that pixels which are line black in the NEW image (max channel below --line-max, the
maskLineMax of the definition) or not opaque in it are dropped: line black keeps its colour under every skin. Nothing is
added, so a region never grows. Also writes the per-region masks for the regions that changed (white / transparent,
like the other masks) when --mask-dir / --mask-file are given.

Run (needs Pillow):
  python3 Tools/pixelart/reuse_region_map.py OLD_MAP.png NEW_IMAGE.png OUT_MAP.png --line-max 18
      [--mask-dir DIR --mask-file "name_{mask}.png"]
"""
import os
import sys

from PIL import Image

REGIONS = {1: "weapon_body", 2: "glove_body", 3: "glove_reinforcement", 4: "wrist_band", 5: "sleeve_base", 6: "sleeve_camo"}


def opt(name, default=None):
    return sys.argv[sys.argv.index(name) + 1] if name in sys.argv else default


def main():
    old_map, image_path, out = sys.argv[1:4]
    line_max = int(opt("--line-max", "18"))
    reg = Image.open(old_map).convert("L")
    img = Image.open(image_path).convert("RGBA")
    if reg.size != img.size:
        raise SystemExit(f"map {reg.size} and image {img.size} differ")
    rp, ip = reg.load(), img.load()
    W, H = reg.size
    dropped, kept = {}, {}
    for y in range(H):
        for x in range(W):
            v = rp[x, y]
            if not v:
                continue
            name = REGIONS[v // 32]
            c = ip[x, y]
            if c[3] < 128 or max(c[:3]) < line_max:
                rp[x, y] = 0
                dropped[name] = dropped.get(name, 0) + 1
            else:
                kept[name] = kept.get(name, 0) + 1
    reg.save(out)
    print(f"written {out}: kept {kept}, dropped (line black / transparent in the new image) {dropped}")
    mask_dir, mask_file = opt("--mask-dir"), opt("--mask-file")
    if mask_dir and mask_file:
        for name in dropped:
            idx = next(k for k, n in REGIONS.items() if n == name)
            m = Image.new("RGBA", (W, H), (0, 0, 0, 0))
            mp = m.load()
            for y in range(H):
                for x in range(W):
                    if rp[x, y] == idx * 32:
                        mp[x, y] = (255, 255, 255, 255)
            path = os.path.join(mask_dir, mask_file.format(mask=name))
            m.save(path)
            print("written", path)


if __name__ == "__main__":
    main()
