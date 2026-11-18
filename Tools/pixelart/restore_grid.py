#!/usr/bin/env python3
"""
Restores the 4 x 4 dot grid of an approved first-person image (Stage 38 spec 4-2, method B; the original is kept
untouched).

Some delivered images were resampled after they were drawn: their dots are no longer whole 4 x 4 cells (colours change
from one pixel to the next and the outline carries semi-transparent pixels). The game image is then a derivative where
every 4 x 4 cell of the canvas, from the top-left (0, 0), is one colour and one alpha:
    alpha  255 when at least half of the cell's pixels have alpha >= 128, else 0 (fully transparent)
    colour the most common colour among the cell's pixels with alpha >= 128 (no averaging, no blur, no interpolation)
The cells of the right and bottom edges are 2 or 3 pixels wide / high (1774 x 887 is not a multiple of 4) and follow the
same rule. Same canvas size, so the placement's pixel coordinates still hold.

Run by hand (needs Pillow):  python3 Tools/pixelart/restore_grid.py SOURCE.png DERIVED.png [--cell 4]
Prints how many cells changed. The art definition names the derivative ("image") and the original ("source").
"""
import sys
from collections import Counter

from PIL import Image

CELL = 4
SOLID = 128


def main():
    src, dst = sys.argv[1], sys.argv[2]
    n = int(sys.argv[sys.argv.index("--cell") + 1]) if "--cell" in sys.argv else CELL
    img = Image.open(src).convert("RGBA")
    w, h = img.size
    px = img.load()
    out = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    op = out.load()
    changed = cells = 0
    for cy in range(0, h, n):
        for cx in range(0, w, n):
            xs, ys = range(cx, min(cx + n, w)), range(cy, min(cy + n, h))
            pixels = [px[x, y] for x in xs for y in ys]
            solid = [p[:3] for p in pixels if p[3] >= SOLID]
            colour = Counter(solid).most_common(1)[0][0] + (255,) if 2 * len(solid) >= len(pixels) else (0, 0, 0, 0)
            cells += 1
            if any(p != colour and not (p[3] == 0 and colour[3] == 0) for p in pixels):
                changed += 1
            for x in xs:
                for y in ys:
                    op[x, y] = colour
    out.save(dst, optimize=True)
    print(f"{src} -> {dst}: {w}x{h}, {n}x{n} cells: {cells}, changed: {changed}")


if __name__ == "__main__":
    main()
