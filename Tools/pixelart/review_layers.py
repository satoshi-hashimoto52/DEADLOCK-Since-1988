#!/usr/bin/env python3
"""
Review images for part layers made by split_layers.py (reads the definition, the base image, the layers and masks;
writes only into the given review folder, never into Assets).

Writes:
    layers_overview.png         each layer on a checkerboard + the base
    recomposition_compare.png   base | layers composited in order | difference (grey = identical visible pixel,
                                red = alpha differs, yellow = RGB differs)
    recomposition_diff.png      the difference image at full size
    classification.png / _full  weapon in grey, left hand+arm cyan, right hand+arm magenta (shading kept)
    contact_<A..>.png           base and classification side by side at 4x for each definition review area
    masks_overview.png          masks in colours over the grey base
    recolor_<name>_full.png     recolour previews (lightness kept, hue / saturation replaced inside the masks)
    recolor_examples.png        base and the previews side by side

Run (needs Pillow):
  python3 Tools/pixelart/review_layers.py DEFINITION.json OUT_DIR [NAME=HAND:MASK=HUE,SAT;HAND:MASK=HUE,SAT ...]
  e.g. glove="left:glove_body=215,0.40;right:glove_body=215,0.40"
The definition's "review" block gives the crop of the overview images and the zoom areas.
"""
import colorsys
import json
import os
import sys

from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
FONT_PATH = "/System/Library/Fonts/ヒラギノ角ゴシック W6.ttc"
MASK_COLOURS = {"glove_body": (230, 120, 40), "glove_reinforcement": (255, 0, 0), "wrist_band": (60, 90, 255),
                "sleeve_base": (140, 220, 60), "sleeve_camo": (20, 110, 40)}


def font(size=24):
    return ImageFont.truetype(FONT_PATH, size) if os.path.exists(FONT_PATH) else ImageFont.load_default()


def load(definition):
    base = Image.open(os.path.join(ROOT, definition["base"])).convert("RGBA")
    out_dir = os.path.join(ROOT, definition["outDir"])
    layers = {p: Image.open(os.path.join(out_dir, f)).convert("RGBA") for p, f in definition["layerFiles"].items()}
    masks = {(h, m): Image.open(os.path.join(out_dir, definition["maskDir"], definition["maskFile"].format(hand=h, mask=m))).convert("RGBA")
             for h in definition["hands"] for m in definition["maskNames"]}
    return base, layers, masks


def composite(definition, layers):
    W, H = next(iter(layers.values())).size
    out = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    for p in definition["compositeOrder"]:
        out = Image.alpha_composite(out, layers[p])
    return out


def checker(size, c=24):
    im = Image.new("RGBA", size, (255, 255, 255, 255))
    d = ImageDraw.Draw(im)
    for y in range(0, size[1], c):
        for x in range(0, size[0], c):
            if (x // c + y // c) % 2:
                d.rectangle([x, y, x + c - 1, y + c - 1], fill=(225, 225, 225, 255))
    return im


def tile(im, label, crop, on_checker=True):
    if on_checker:
        bg = checker(im.size)
        bg.alpha_composite(im.convert("RGBA"))
        im = bg
    t = im.convert("RGB").crop(crop)
    out = Image.new("RGB", (t.width, t.height + 36), "white")
    out.paste(t, (0, 36))
    ImageDraw.Draw(out).text((6, 6), label, fill="black", font=font())
    return out


def row(tiles, gap=10):
    w = sum(t.width for t in tiles) + gap * (len(tiles) - 1)
    h = max(t.height for t in tiles)
    out = Image.new("RGB", (w, h), "white")
    x = 0
    for t in tiles:
        out.paste(t, (x, 0))
        x += t.width + gap
    return out


def grey(c):
    return int(0.3 * c[0] + 0.59 * c[1] + 0.11 * c[2])


def classification(definition, base, layers):
    W, H = base.size
    out = Image.new("RGB", (W, H), (255, 255, 255))
    op = out.load()
    lps = {p: l.load() for p, l in layers.items()}
    for y in range(H):
        for x in range(W):
            for p, lp in lps.items():
                c = lp[x, y]
                if not c[3]:
                    continue
                v = grey(c)
                if p == "weapon":
                    op[x, y] = (v, v, v)
                elif p == "left":
                    op[x, y] = (0, min(255, 60 + v * 2), min(255, 90 + v * 2))
                else:
                    op[x, y] = (min(255, 90 + v * 2), 0, min(255, 60 + v * 2))
                break
    return out


def mask_view(base, masks):
    W, H = base.size
    bp = base.load()
    out = Image.new("RGB", (W, H), (255, 255, 255))
    op = out.load()
    for y in range(H):
        for x in range(W):
            if bp[x, y][3]:
                v = grey(bp[x, y])
                op[x, y] = (v, v, v)
    for (h, m), mask in masks.items():
        mp = mask.load()
        col = MASK_COLOURS.get(m, (255, 0, 255))
        for y in range(H):
            for x in range(W):
                if mp[x, y][3]:
                    k = 0.45 + 0.9 * grey(bp[x, y]) / 255
                    op[x, y] = tuple(min(255, int(c * k)) for c in col)
    return out


def recolour(definition, layers, masks, spec):
    layers = {p: l.copy() for p, l in layers.items()}
    for item in spec.split(";"):
        target, value = item.split("=")
        hand, mask_name = target.split(":")
        hue, sat = (float(v) for v in value.split(","))
        lp, mp = layers[hand].load(), masks[(hand, mask_name)].load()
        W, H = layers[hand].size
        for y in range(H):
            for x in range(W):
                if mp[x, y][3]:
                    r, g, b, a = lp[x, y]
                    _, l, _ = colorsys.rgb_to_hls(r / 255, g / 255, b / 255)
                    nr, ng, nb = colorsys.hls_to_rgb(hue / 360, l, sat)
                    lp[x, y] = (round(nr * 255), round(ng * 255), round(nb * 255), a)
    return composite(definition, layers)


def main():
    definition = json.load(open(sys.argv[1], encoding="utf-8"))
    out = sys.argv[2]
    if os.path.abspath(out).startswith(os.path.abspath(os.path.join(ROOT, "Assets"))):
        raise SystemExit("review images are not written into Assets")
    os.makedirs(out, exist_ok=True)
    base, layers, masks = load(definition)
    review = definition.get("review", {})
    crop = tuple(review.get("crop", (0, 0) + base.size))
    names = {"weapon": "銃", "left": "左手・腕", "right": "右手・腕"}

    tiles = [tile(layers[p], f"{names.get(p, p)}（{definition['layerFiles'][p]}）", crop) for p in definition["compositeOrder"]]
    tiles.append(tile(base, f"基準：{os.path.basename(definition['base'])}", crop))
    top, bottom = row(tiles[:2]), row(tiles[2:])
    overview = Image.new("RGB", (max(top.width, bottom.width), top.height + bottom.height + 10), "white")
    overview.paste(top, (0, 0))
    overview.paste(bottom, (0, top.height + 10))
    overview.save(os.path.join(out, "layers_overview.png"))

    comp = composite(definition, layers)
    W, H = base.size
    diff = Image.new("RGB", (W, H), (0, 0, 0))
    dp, bp, cp = diff.load(), base.load(), comp.load()
    for y in range(H):
        for x in range(W):
            b, c = bp[x, y], cp[x, y]
            if b[3] != c[3]:
                dp[x, y] = (255, 0, 0)
            elif (b[3] or c[3]) and b[:3] != c[:3]:
                dp[x, y] = (255, 255, 0)
            elif b[3]:
                dp[x, y] = (40, 40, 40)
    diff.save(os.path.join(out, "recomposition_diff.png"))
    row([tile(base, "基準画像", crop), tile(comp, "レイヤーを元の位置で合成", crop),
         tile(diff, "差分（灰＝一致、赤＝アルファ不一致、黄＝RGB不一致）", crop, on_checker=False)]).save(os.path.join(out, "recomposition_compare.png"))

    cls = classification(definition, base, layers)
    cls.save(os.path.join(out, "classification_full.png"))
    tile(cls, "分類確認：水色＝左手・腕、赤紫＝右手・腕、灰＝銃（暗い縁は各部位に割り当てた黒い輪郭）", crop, on_checker=False).save(os.path.join(out, "classification.png"))
    flat = Image.new("RGBA", base.size, (255, 255, 255, 255))
    flat.alpha_composite(base)
    flat = flat.convert("RGB")
    for i, area in enumerate(review.get("areas", [])):
        x0, y0, x1, y1 = area["box"]
        z = 4
        a = flat.crop((x0, y0, x1, y1)).resize(((x1 - x0) * z, (y1 - y0) * z), Image.NEAREST)
        b = cls.crop((x0, y0, x1, y1)).resize(((x1 - x0) * z, (y1 - y0) * z), Image.NEAREST)
        t = Image.new("RGB", (a.width * 2 + 10, a.height + 34), "white")
        ImageDraw.Draw(t).text((4, 4), f"{area['name']}  ({x0},{y0})-({x1},{y1})  左：基準画像  右：分類", fill="black", font=font(20))
        t.paste(a, (0, 34))
        t.paste(b, (a.width + 10, 34))
        t.save(os.path.join(out, f"contact_{chr(65 + i)}.png"))
    tile(mask_view(base, masks), "マスク：橙＝グローブ本体、青＝手首の帯、黄緑＝袖の地色、濃緑＝袖の迷彩（灰＝銃・色を保つ黒い線）", crop, on_checker=False).save(os.path.join(out, "masks_overview.png"))

    previews = [tile(base, "基準画像", crop)]
    for arg in sys.argv[3:]:
        name, spec = arg.split("=", 1)
        im = recolour(definition, layers, masks, spec)
        flat_im = Image.new("RGBA", im.size, (255, 255, 255, 255))
        flat_im.alpha_composite(im)
        flat_im.convert("RGB").save(os.path.join(out, f"recolor_{name}_full.png"))
        im.save(os.path.join(out, f"recolor_{name}_rgba.png"))
        previews.append(tile(im, f"{name}：{spec}", crop))
    if len(previews) > 1:
        row(previews).save(os.path.join(out, "recolor_examples.png"))
    print("written to", out)


if __name__ == "__main__":
    main()
