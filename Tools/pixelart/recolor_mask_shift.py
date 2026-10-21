#!/usr/bin/env python3
"""
Colour-candidate tool: shifts the colour of the pixels inside a mask, keeping the drawn shading steps.

For every pixel inside the mask (and only there): hue += hueShift (degrees), saturation *= satScale, lightness *=
lightScale (HLS, on the 8-bit sRGB value). One factor per channel for the whole mask, so the order and the ratios of
the painted tones stay (dark stays darker than light, the steps keep their spacing); alpha, size and every pixel
outside the mask are unchanged. Unlike skin_preview.py (which replaces hue / saturation with one value), the variation
inside the area is kept. The source image is only read; the output must not be inside Assets.

Run (needs Pillow):
  python3 Tools/pixelart/recolor_mask_shift.py SOURCE.png MASK.png OUT.png --hue-shift D --sat-scale S --light-scale L
Prints the number of changed pixels and the number of distinct colours inside the mask before / after.
"""
import colorsys
import os
import sys

from PIL import Image

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))


def arg(name):
    return float(sys.argv[sys.argv.index(name) + 1])


def main():
    src, mask_path, out = sys.argv[1:4]
    if os.path.abspath(out).startswith(os.path.abspath(os.path.join(ROOT, "Assets"))):
        raise SystemExit("candidates are not written into Assets")
    hue_shift, sat_scale, light_scale = arg("--hue-shift"), arg("--sat-scale"), arg("--light-scale")
    img = Image.open(src).convert("RGBA")
    mask = Image.open(mask_path).convert("RGBA")
    if img.size != mask.size:
        raise SystemExit(f"image {img.size} and mask {mask.size} differ")
    px, mp = img.load(), mask.load()
    before, after, changed = set(), set(), 0
    for y in range(img.height):
        for x in range(img.width):
            if not mp[x, y][3]:
                continue
            r, g, b, a = px[x, y]
            before.add((r, g, b))
            h, l, s = colorsys.rgb_to_hls(r / 255, g / 255, b / 255)
            nr, ng, nb = colorsys.hls_to_rgb((h + hue_shift / 360) % 1.0, min(1.0, l * light_scale), min(1.0, s * sat_scale))
            c = (round(nr * 255), round(ng * 255), round(nb * 255))
            after.add(c)
            if c != (r, g, b):
                changed += 1
            px[x, y] = c + (a,)
    img.save(out)
    print(f"written {out}: changed {changed} pixels, colours in the mask {len(before)} -> {len(after)}")


if __name__ == "__main__":
    main()
