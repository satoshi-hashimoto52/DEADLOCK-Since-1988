#!/usr/bin/env python3
"""
Skin preview from a region map (make_skin_regions.py), the same colour change the game's skin shader does: for every
pixel of a chosen region the HLS lightness stays and hue / saturation are replaced; all other pixels are unchanged.
Writes only the preview image given on the command line (never into Assets).

Run (needs Pillow):
  python3 Tools/pixelart/skin_preview.py IMAGE.png REGIONS.png OUT.png [REGION=HUE,SAT ...]
  regions: weapon_body glove_body glove_reinforcement wrist_band sleeve_base sleeve_camo (hue in degrees, saturation 0..1)
"""
import colorsys
import os
import sys

from PIL import Image

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
REGIONS = {"weapon_body": 1, "glove_body": 2, "glove_reinforcement": 3, "wrist_band": 4, "sleeve_base": 5, "sleeve_camo": 6}
STEP = 32


def main():
    image_path, regions_path, out_path = sys.argv[1:4]
    if os.path.abspath(out_path).startswith(os.path.abspath(os.path.join(ROOT, "Assets"))):
        raise SystemExit("previews are not written into Assets")
    colours = {}
    for spec in sys.argv[4:]:
        name, value = spec.split("=")
        hue, sat = (float(v) for v in value.split(","))
        colours[REGIONS[name]] = (hue / 360, sat)
    img = Image.open(image_path).convert("RGBA")
    reg = Image.open(regions_path).convert("L")
    if img.size != reg.size:
        raise SystemExit(f"image {img.size} and region map {reg.size} differ")
    ip, rp = img.load(), reg.load()
    W, H = img.size
    for y in range(H):
        for x in range(W):
            c = colours.get(int(rp[x, y] / STEP + 0.5))
            if c is None:
                continue
            r, g, b, a = ip[x, y]
            _, l, _ = colorsys.rgb_to_hls(r / 255, g / 255, b / 255)
            nr, ng, nb = colorsys.hls_to_rgb(c[0], l, c[1])
            ip[x, y] = (round(nr * 255), round(ng * 255), round(nb * 255), a)
    img.save(out_path)
    print("written", out_path)


if __name__ == "__main__":
    main()
