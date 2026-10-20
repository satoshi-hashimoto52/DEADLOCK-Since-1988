#!/usr/bin/env python3
"""
Colour-change preview for split part layers (writes only the preview image given on the command line).

Shading is kept: for every pixel in the chosen masks the lightness (HLS) stays as it is and only hue / saturation are
replaced, so the painted light and dark bands of the original survive. Line-black pixels are not in any mask and keep
their colour. The layers are composited in the definition's order after recolouring.

Run (needs Pillow):
  python3 Tools/pixelart/recolor_preview.py DEFINITION.json OUT.png HAND:MASK=HUE,SAT [HAND:MASK=HUE,SAT ...]
  e.g. left:glove_body=215,0.45 right:glove_body=215,0.45   (hue in degrees, saturation 0..1)
"""
import colorsys
import json
import os
import sys

from PIL import Image

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))


def main():
    definition = json.load(open(sys.argv[1], encoding="utf-8"))
    out_path = sys.argv[2]
    out_dir = os.path.join(ROOT, definition["outDir"])
    if os.path.abspath(out_path).startswith(os.path.abspath(os.path.join(ROOT, "Assets"))):
        raise SystemExit("previews are not written into Assets")
    layers = {p: Image.open(os.path.join(out_dir, f)).convert("RGBA") for p, f in definition["layerFiles"].items()}
    for spec in sys.argv[3:]:
        target, value = spec.split("=")
        hand, mask_name = target.split(":")
        hue, sat = (float(v) for v in value.split(","))
        mask = Image.open(os.path.join(out_dir, definition["maskDir"], definition["maskFile"].format(hand=hand, mask=mask_name))).convert("RGBA")
        lp, mp = layers[hand].load(), mask.load()
        W, H = mask.size
        for y in range(H):
            for x in range(W):
                if mp[x, y][3]:
                    r, g, b, a = lp[x, y]
                    _, l, _ = colorsys.rgb_to_hls(r / 255, g / 255, b / 255)
                    nr, ng, nb = colorsys.hls_to_rgb(hue / 360, l, sat)
                    lp[x, y] = (round(nr * 255), round(ng * 255), round(nb * 255), a)
    W, H = next(iter(layers.values())).size
    composite = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    for p in definition["compositeOrder"]:
        composite = Image.alpha_composite(composite, layers[p])
    composite.save(out_path)
    print("written", out_path)


if __name__ == "__main__":
    main()
