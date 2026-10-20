#!/usr/bin/env python3
"""
Minimal alpha clean-up for approved pixel-art images (the originals are kept untouched).

The approved hip / ADS images draw their pixels at alpha 250-254 (about 99 %, slightly see-through) and carry a thin
semi-transparent fringe outside the outline. For crisp pixel art in the game, alpha is made binary:
    alpha >= THRESHOLD -> 255 (colour kept as is: dark outlines stay)
    alpha <  THRESHOLD -> 0   (the faint fringe outside the outline goes away)
Nothing else changes: same size, same colours, no blur, no resampling, no colour keying (dark pixels are not removed).

Run by hand (needs Pillow):  python3 Tools/pixelart/make_opaque.py SOURCE.png DERIVED.png [--threshold 128]
Prints how many pixels changed. The placement file names the derived image ("image") and the original ("source").
"""
import sys

from PIL import Image

THRESHOLD = 128


def main():
    src, dst = sys.argv[1], sys.argv[2]
    threshold = int(sys.argv[sys.argv.index("--threshold") + 1]) if "--threshold" in sys.argv else THRESHOLD
    img = Image.open(src).convert("RGBA")
    px = img.load()
    raised = cleared = 0
    for y in range(img.height):
        for x in range(img.width):
            r, g, b, a = px[x, y]
            if a == 0 or a == 255:
                continue
            if a >= threshold:
                px[x, y] = (r, g, b, 255)
                raised += 1
            else:
                px[x, y] = (0, 0, 0, 0)
                cleared += 1
    img.save(dst)
    print(f"{src} -> {dst}: {img.width}x{img.height}, alpha raised to 255: {raised}, fringe cleared: {cleared}")


if __name__ == "__main__":
    main()
