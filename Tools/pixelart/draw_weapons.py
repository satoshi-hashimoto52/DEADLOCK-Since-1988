#!/usr/bin/env python3
"""
Pixel art for the first-person weapon (rifle hip / ADS, knife + gloved hand, muzzle flash).

- Knife + gloved hand, muzzle flash: drawn in code by hand -- every part placed in pixel coordinates (polygons,
  capsules, single pixels) and coloured from small per-material palettes (outline / dark / mid / light / highlight)
  with clustered bands and hand-placed highlights. No blur, no smooth gradients, no noise.
- Rifle hip / ADS: traced from our own 3D bake (Assets/Game/Art/Generated/Spr_WeaponHip / Spr_WeaponAds) into the
  same palettes by rule (see trace_bake), so both views stay exactly the same gun with the same muzzle / sight
  geometry. Hand-drawn hip attempts (perspective drawn by hand) read worse than the bake and were dropped.

Output (git-managed, NOT touched by the scene builder or Bake Weapon Sprites):
    Assets/Game/Art/Weapons/Spr_PixelRifleHip.png    1/3 of the bake (pivot: muzzle, same as the bake)
    Assets/Game/Art/Weapons/Spr_PixelRifleAds.png    1/3 of the bake (pivot: front sight, same as the bake)
    Assets/Game/Art/Weapons/Spr_PixelKnife.png       186 x 169  (pivot: grip)
    Assets/Game/Art/Weapons/Spr_PixelMuzzleFlash.png 48 x 48
    Assets/Game/Art/Weapons/pixel_weapons.json       pivots / pixels-per-unit (read by the Unity importer)

One art pixel = 3 pixels of the old bake (the importer uses a third of the bake's pixels-per-unit), so on screen the
sprites have the old size and every art pixel shows as about 3 screen pixels at 1080p.

Run by hand (needs Pillow):  python3 Tools/pixelart/draw_weapons.py [--preview DIR] [--force]
It refuses to overwrite a pixel_weapons.json it did not write (authored art in place) unless --force is given.
Re-run it after re-baking the rifle (the traced sprites follow the bake); nothing runs it automatically.
"""
import colorsys
import json
import math
import os
import sys
from collections import Counter

from PIL import Image

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.environ.get("DEADLOCK_ART_OUT") or os.path.join(ROOT, "Assets", "Game", "Art", "Weapons")

# ---------------------------------------------------------------------------------------------------------- palettes
# index: 0 outline, 1 dark, 2 mid, 3 light, 4 highlight
STEEL = [(16, 18, 22), (40, 45, 54), (66, 74, 86), (98, 109, 124), (156, 168, 184)]
DARK_STEEL = [(12, 13, 16), (30, 33, 39), (48, 52, 60), (72, 78, 90), (120, 128, 142)]
FDE = [(30, 23, 15), (84, 66, 44), (116, 94, 64), (146, 121, 84), (190, 164, 118)]
GLOVE = [(24, 19, 15), (58, 44, 32), (86, 66, 46), (116, 90, 62), (160, 128, 90)]
GLOVE_PAD = [(24, 19, 15), (44, 40, 38), (64, 60, 56), (88, 84, 78), (124, 118, 110)]
SLEEVE = [(26, 30, 16), (60, 70, 38), (86, 100, 54), (112, 128, 70), (150, 168, 96)]
BLADE = [(18, 20, 24), (78, 86, 98), (122, 132, 146), (170, 180, 192), (226, 232, 238)]
CORD = [(14, 14, 14), (34, 34, 34), (52, 52, 50), (74, 72, 68), (104, 100, 94)]


class Canvas:
    """Paints into an RGBA image while remembering which part owns each pixel (for outlines)."""

    def __init__(self, w, h):
        self.w, self.h = w, h
        self.img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
        self.px = self.img.load()
        self.owner = [[None] * w for _ in range(h)]

    def inside(self, x, y):
        return 0 <= x < self.w and 0 <= y < self.h

    def put(self, x, y, color, owner=None):
        x, y = int(x), int(y)
        if self.inside(x, y):
            self.px[x, y] = color + (255,) if len(color) == 3 else color
            if owner is not None:
                self.owner[y][x] = owner

    def get_owner(self, x, y):
        return self.owner[y][x] if self.inside(x, y) else None

    # --- shapes ------------------------------------------------------------------------------------------------
    def polygon(self, pts, shade, owner):
        """Fills a polygon; shade(x, y) returns a colour per pixel."""
        ys = [p[1] for p in pts]
        for y in range(max(0, int(math.floor(min(ys)))), min(self.h, int(math.ceil(max(ys))) + 1)):
            cy = y + 0.5
            xs = []
            n = len(pts)
            for i in range(n):
                (x0, y0), (x1, y1) = pts[i], pts[(i + 1) % n]
                if (y0 <= cy < y1) or (y1 <= cy < y0):
                    xs.append(x0 + (cy - y0) * (x1 - x0) / (y1 - y0))
            xs.sort()
            for a, b in zip(xs[0::2], xs[1::2]):
                for x in range(max(0, int(math.ceil(a - 0.5))), min(self.w, int(math.floor(b - 0.5)) + 1)):
                    self.put(x, y, shade(x + 0.5, y + 0.5), owner)

    def capsule(self, p0, p1, r0, r1, shade, owner):
        """Rounded segment (finger, knuckle, barrel end); shade(x, y, u, v) with u along 0..1, v across -1..1."""
        (x0, y0), (x1, y1) = p0, p1
        dx, dy = x1 - x0, y1 - y0
        length2 = dx * dx + dy * dy or 1e-6
        rmax = max(r0, r1)
        for y in range(int(min(y0, y1) - rmax - 1), int(max(y0, y1) + rmax + 2)):
            for x in range(int(min(x0, x1) - rmax - 1), int(max(x0, x1) + rmax + 2)):
                cx, cy = x + 0.5, y + 0.5
                t = max(0.0, min(1.0, ((cx - x0) * dx + (cy - y0) * dy) / length2))
                qx, qy = x0 + dx * t, y0 + dy * t
                r = r0 + (r1 - r0) * t
                dist = math.hypot(cx - qx, cy - qy)
                if dist <= r:
                    # signed across: positive on the "upper-left" side of the segment
                    side = ((cx - qx) * -dy + (cy - qy) * dx) / math.sqrt(length2)
                    self.put(x, y, shade(cx, cy, t, side / max(r, 1e-6)), owner)

    def outline(self, owners, color, against_background_only=False):
        """1-px outline on the parts' pixels that touch the background (or, optionally, other parts)."""
        marks = []
        for y in range(self.h):
            for x in range(self.w):
                o = self.owner[y][x]
                if o not in owners:
                    continue
                for nx, ny in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)):
                    no = self.get_owner(nx, ny)
                    if no is None or (not against_background_only and no not in owners and no is not None and False):
                        marks.append((x, y))
                        break
        for x, y in marks:
            self.px[x, y] = color + (255,)

    def seam(self, front, color):
        """Dark 1-px line where part `front` overlaps any other part (reads the overlap)."""
        marks = []
        for y in range(self.h):
            for x in range(self.w):
                if self.owner[y][x] != front:
                    continue
                for nx, ny in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)):
                    no = self.get_owner(nx, ny)
                    if no is not None and no != front:
                        marks.append((x, y))
                        break
        for x, y in marks:
            self.px[x, y] = color + (255,)


def band(value, cuts):
    """Clustered shading: value 0..1 → ramp index via fixed cut points (no smooth gradients)."""
    for i, c in enumerate(cuts):
        if value < c:
            return i
    return len(cuts)


# ------------------------------------------------------------------------------------------ the rifle (hip / ADS)
def trace_bake(name, scale=3):
    """
    Pixel-art version of a baked rifle sprite (Assets/Game/Art/Generated/<name>.png, our own 3D bake), so hip and ADS
    stay exactly the same gun with the same muzzle / front-sight geometry. Each art pixel is a 3x3 bake cell; the
    cell's material (steel / FDE polymer / glove / sleeve) picks a palette ramp, its brightness picks one of four
    clustered bands (fixed cut points per material, no gradients), then a 1-px dark outline around the silhouette
    and between materials and a 1-px highlight on the lit (upper) edges are drawn by rule.
    """
    src = Image.open(os.path.join(ROOT, "Assets", "Game", "Art", "Generated", name + ".png")).convert("RGBA")
    W, H = src.width // scale, src.height // scale
    px = src.load()
    out = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    o = out.load()

    def material(r, g, b):
        h, l, s = colorsys.rgb_to_hls(r / 255, g / 255, b / 255)
        hue = h * 360
        if l < 0.13 or s < 0.06 or 150 < hue < 300:
            return "steel"
        if 55 < hue < 150:
            return "sleeve"
        return "glove" if hue < 36 else "fde"

    ramps = {"steel": STEEL, "fde": FDE, "glove": GLOVE, "sleeve": SLEEVE}
    lum, mat = {}, {}
    for y in range(H):
        for x in range(W):
            cells = [px[x * scale + i, y * scale + j] for i in range(scale) for j in range(scale)]
            solid = [c for c in cells if c[3] > 127]
            if len(solid) * 2 < len(cells):
                continue
            mat[x, y] = Counter(material(*c[:3]) for c in solid).most_common(1)[0][0]
            lum[x, y] = sum(0.3 * c[0] + 0.59 * c[1] + 0.11 * c[2] for c in solid) / len(solid) / 255
    for m, ramp in ramps.items():
        vals = sorted(v for key, v in lum.items() if mat[key] == m)
        if not vals:
            continue
        lo, hi = vals[int(len(vals) * 0.03)], vals[int(len(vals) * 0.97)]
        cuts = [lo + (hi - lo) * f for f in (0.25, 0.55, 0.82)]
        for key, v in lum.items():
            if mat[key] == m:
                o[key] = ramp[min(4, 1 + sum(v > c for c in cuts))] + (255,)
    for (x, y), m in mat.items():  # outline: silhouette, and the darker side of a material boundary
        for n in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)):
            if not (0 <= n[0] < W and 0 <= n[1] < H):
                continue
            if n not in mat or (mat[n] != m and lum[x, y] < lum[n]):
                o[x, y] = ramps[m][0] + (255,)
                break
    for (x, y), m in mat.items():  # highlight on the upper edge (just inside the outline)
        if o[x, y][:3] == ramps[m][0]:
            continue
        up = (x, y - 1)
        if up not in mat or (o[up][:3] == ramps[mat[up]][0] and (x, y - 2) not in mat):
            o[x, y] = ramps[m][4] + (255,)
    return out


def bake_pivot(name):
    """The bake's sprite pivot (normalised), read from its .meta, so the traced sprite lines up identically."""
    with open(os.path.join(ROOT, "Assets", "Game", "Art", "Generated", name + ".png.meta")) as f:
        for line in f:
            line = line.strip()
            if line.startswith("spritePivot:"):
                x = float(line.split("x:")[1].split(",")[0])
                y = float(line.split("y:")[1].split("}")[0])
                return [x, y]
    raise RuntimeError("no spritePivot in " + name)


def draw_hip():
    return trace_bake("Spr_WeaponHip"), {"pivot": bake_pivot("Spr_WeaponHip"), "source": "traced from Spr_WeaponHip"}


def baked_ads_muzzle_offset():
    """adsMuzzleOffset (units, y up) from the baked rifle view profile."""
    with open(os.path.join(ROOT, "Assets", "Game", "Data", "WeaponView_ProtoRifle.asset")) as f:
        for line in f:
            line = line.strip()
            if line.startswith("adsMuzzleOffset:"):
                return float(line.split("x:")[1].split(",")[0]), float(line.split("y:")[1].split("}")[0])
    raise RuntimeError("no adsMuzzleOffset")


def draw_ads():
    img = trace_bake("Spr_WeaponAds")
    pivot = bake_pivot("Spr_WeaponAds")
    ppu = PIXELS_PER_UNIT
    ox, oy = baked_ads_muzzle_offset()
    px, py = pivot[0] * img.width, (1 - pivot[1]) * img.height
    # Muzzle tip in pixels from the top-left (the importer turns it back into the flash offset).
    muzzle = [round(px + ox * ppu, 4), round(py - oy * ppu, 4)]
    return img, {"pivot": pivot, "muzzle": muzzle, "source": "traced from Spr_WeaponAds"}


# --------------------------------------------------------------------------------------------- knife + gloved hand
def draw_knife():
    W, H = 186, 169
    c = Canvas(W, H)
    # blade: tip at the upper left, edge on the lower side, spine with a sharp highlight
    tip, base = (6, 6), (62, 84)
    a = math.atan2(base[1] - tip[1], base[0] - tip[0])
    d = (math.cos(a), math.sin(a))
    n = (math.sin(a), -math.cos(a))  # spine side (upper right)

    def P(t, v):
        return (tip[0] + d[0] * t + n[0] * v, tip[1] + d[1] * t + n[1] * v)

    L = math.hypot(base[0] - tip[0], base[1] - tip[1])
    blade = [P(0, 0), P(10, 6), P(L, 7), P(L, -6), P(14, -4)]

    def blade_shade(x, y):
        rx, ry = x - tip[0], y - tip[1]
        v = rx * n[0] + ry * n[1]
        if v > 5.2:
            return BLADE[4]            # spine highlight
        if v < -3.6:
            return BLADE[3]            # bevelled edge catches light
        return BLADE[2] if v > 0.5 else BLADE[1]

    c.polygon(blade, blade_shade, "blade")
    c.outline({"blade"}, BLADE[0])
    # guard (dark steel) and handle (black cord-wrapped grip)
    gx, gy = base
    c.polygon([P(L - 2, 12), P(L + 6, 12), P(L + 6, -11), P(L - 2, -11)], lambda x, y: DARK_STEEL[3] if (x + y) < gx + gy else DARK_STEEL[2], "guard")
    handle = [P(L + 6, 7.5), P(L + 74, 9.5), P(L + 74, -9.5), P(L + 6, -7.5)]
    c.polygon(handle, lambda x, y: CORD[2], "handle")
    for k in range(0, 70, 6):  # cord wraps: lit ridge + dark gap
        for v in range(-9, 10):
            x, y = P(L + 8 + k, v)
            c.put(round(x), round(y), CORD[1], "handle")
            x, y = P(L + 10 + k, v)
            c.put(round(x), round(y), CORD[3] if v > 0 else CORD[2], "handle")
    c.outline({"guard", "handle"}, CORD[0])

    # gloved hand: palm block behind, four fingers wrapped around the grip (front), thumb along the spine side
    palm = [(96, 92), (150, 104), (176, 150), (150, 169), (102, 169), (88, 130)]

    def palm_shade(x, y):
        f = (x - 88) / 88.0 - (y - 92) / 160.0
        return GLOVE[1 + band(f, (0.2, 0.5))]

    c.polygon(palm, palm_shade, "palm")
    for (x0, y0, x1, y1) in ((118, 104, 132, 150), (132, 108, 150, 150)):   # tendons on the back of the glove
        n = max(abs(x1 - x0), abs(y1 - y0))
        for q in range(n):
            c.put(x0 + (x1 - x0) * q // n, y0 + (y1 - y0) * q // n, GLOVE[1], "palm")
    for q in range(20):                                                      # wrist strap
        c.put(150 + q // 2, 158 - q, GLOVE_PAD[2], "palm")
        c.put(151 + q // 2, 158 - q, GLOVE_PAD[1], "palm")
    c.seam("palm", GLOVE[0])
    for i, (p0, p1) in enumerate((((100, 112), (76, 112)), ((104, 126), (80, 128)), ((108, 140), (85, 144)), ((112, 154), (92, 159)))):
        def fs(x, y, u, v):
            idx = 2 + (1 if v > 0.35 else 0) - (1 if v < -0.45 else 0)
            return (GLOVE_PAD if u > 0.85 else GLOVE)[max(1, min(3, idx))]
        c.capsule(p0, p1, 6.5, 5.5, fs, f"kf{i}")
        c.put(p0[0] - 4, p0[1] - 4, GLOVE[4], f"kf{i}")
        c.put(p0[0] - 5, p0[1] - 4, GLOVE[4], f"kf{i}")
    # thumb lying along the handle toward the guard
    c.capsule((140, 112), (98, 93), 7.5, 6.0, lambda x, y, u, v: (GLOVE_PAD if u > 0.82 else GLOVE)[3 if v > 0.2 else (2 if v > -0.5 else 1)], "thumb")
    for q in range(5):                                                       # thumb joint crease
        c.put(118 + q // 2, 96 + q, GLOVE[1], "thumb")
    for q in range(6):                                                       # lit ridge of the thumb
        c.put(108 + q * 3, 91 + q, GLOVE[4], "thumb")
    for g in ("kf0", "kf1", "kf2", "kf3", "thumb"):
        c.seam(g, GLOVE[0])
    c.outline({"palm", "thumb", "kf0", "kf1", "kf2", "kf3"}, GLOVE[0])
    # sleeve cuff at the lower-right corner
    c.polygon([(150, 169), (176, 150), (186, 150), (186, 169)], lambda x, y: SLEEVE[2] if x < 178 else SLEEVE[1], "cuff")
    c.outline({"cuff"}, SLEEVE[0])

    # Same frame as the baked knife (same size, same pivot), so the knife sits where the profile's grip position
    # expects it; the hand is drawn at the bottom right of that frame.
    return c.img, {"pivot": bake_pivot("Spr_Knife")}


# ------------------------------------------------------------------------------------------------------ muzzle flash
def draw_flash():
    W = H = 48
    c = Canvas(W, H)
    cx = cy = 24
    core = [(255, 252, 220), (255, 224, 120), (248, 160, 40), (196, 84, 18)]
    # four-point star with a bright core, clustered (no gradient)
    for y in range(H):
        for x in range(W):
            dx, dy = x + 0.5 - cx, y + 0.5 - cy
            r = math.hypot(dx, dy)
            star = min(abs(dx), abs(dy))
            diag = min(abs(dx - dy), abs(dx + dy)) / math.sqrt(2)
            if r < 5:
                col = core[0]
            elif r < 9:
                col = core[1]
            elif star < 2.2 and r < 23:
                col = core[1] if r < 15 else core[2]
            elif diag < 1.6 and r < 14:
                col = core[2]
            elif r < 12 and star < 5:
                col = core[3]
            else:
                continue
            c.put(x, y, col)
    return c.img, {"pivot": [0.5, 0.5]}


GENERATOR = "Tools/pixelart/draw_weapons.py"
PIXELS_PER_UNIT = 1158.0337 / 3.0  # 1 art px = 3 bake px (bake PPU 1158.0337)


def main():
    os.makedirs(OUT, exist_ok=True)
    # Never overwrite art made elsewhere (external artist / image tool): its pixel_weapons.json has no "generator"
    # entry of this script. --force overrides.
    existing = os.path.join(OUT, "pixel_weapons.json")
    if os.path.exists(existing) and "--force" not in sys.argv:
        with open(existing) as f:
            if json.load(f).get("generator") != GENERATOR:
                sys.exit(f"{existing} was not written by {GENERATOR} (authored art?): not overwriting. Use --force.")
    sprites = []
    for name, fn in (("Spr_PixelRifleHip", draw_hip), ("Spr_PixelRifleAds", draw_ads), ("Spr_PixelKnife", draw_knife), ("Spr_PixelMuzzleFlash", draw_flash)):
        img, info = fn()
        img.save(os.path.join(OUT, name + ".png"))
        sprites.append(dict(name=name, size=list(img.size), **info))
    # Same on-screen size as the old bake: 1 art px = 3 bake px (bake PPU 1158.0337), flash 48 px = old 256 px @ 1024.
    meta = {
        "generator": GENERATOR,
        "sprites": sprites,
        "pixelsPerUnit": PIXELS_PER_UNIT,
        "flashPixelsPerUnit": 48 / (256 / 1024.0),
    }
    with open(os.path.join(OUT, "pixel_weapons.json"), "w") as f:
        json.dump(meta, f, indent=2)
    if "--preview" in sys.argv:
        pdir = sys.argv[sys.argv.index("--preview") + 1]
        os.makedirs(pdir, exist_ok=True)
        for sprite in sprites:
            name = sprite["name"]
            img = Image.open(os.path.join(OUT, name + ".png"))
            for bgname, bg in (("dark", (28, 30, 36)), ("light", (170, 160, 140))):
                big = img.resize((img.width * 4, img.height * 4), Image.NEAREST)
                canvas = Image.new("RGBA", big.size, bg + (255,))
                canvas.alpha_composite(big)
                canvas.save(os.path.join(pdir, f"{name}_{bgname}.png"))
    print(json.dumps(meta, indent=2))


if __name__ == "__main__":
    main()
