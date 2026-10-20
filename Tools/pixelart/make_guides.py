#!/usr/bin/env python3
"""
Composition guides for the first-person weapon art (for an external artist / image-generation brief).

Draws the current sprites from Assets/Game/Art/Weapons at 4x with their reference points (pivot, muzzle, hold point /
rotation centre, wrist) and the motion limits, all read from the project data -- pixel_weapons.json (sizes, pivots,
pixels-per-unit) and the view profiles (Assets/Game/Data/WeaponView_ProtoRifle.asset, MeleeView_ProtoKnife.asset).

Run by hand (needs Pillow):  python3 Tools/pixelart/make_guides.py OUTPUT_DIR
"""
import json
import os
import re
import sys

from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
ART = os.path.join(ROOT, "Assets", "Game", "Art", "Weapons")
DATA = os.path.join(ROOT, "Assets", "Game", "Data")
FONT = "/System/Library/Fonts/ヒラギノ角ゴシック W6.ttc"


def profile(name):
    """Scalar / Vector2 fields of a ScriptableObject .asset (YAML) as floats / (x, y)."""
    values = {}
    with open(os.path.join(DATA, name), encoding="utf-8") as f:
        for line in f:
            m = re.match(r"\s+(\w+): \{x: ([-\d.e]+), y: ([-\d.e]+)\}", line)
            if m:
                values[m.group(1)] = (float(m.group(2)), float(m.group(3)))
                continue
            m = re.match(r"\s+(\w+): ([-\d.e]+)\s*$", line)
            if m:
                values[m.group(1)] = float(m.group(2))
    return values


def main():
    out = sys.argv[1]
    os.makedirs(out, exist_ok=True)
    with open(os.path.join(ART, "pixel_weapons.json")) as f:
        art = json.load(f)
    ppu = art["pixelsPerUnit"]
    sprites = {s["name"]: s for s in art["sprites"]}
    rifle = profile("WeaponView_ProtoRifle.asset")
    knife = profile("MeleeView_ProtoKnife.asset")
    font = ImageFont.truetype(FONT, 18) if os.path.exists(FONT) else None
    half_h = 0.4663  # tan(25 deg): view-model camera half height at z = 1

    def pivot_px(name):
        s = sprites[name]
        w, h = s["size"]
        return s["pivot"][0] * w, (1 - s["pivot"][1]) * h

    def guide(name, marks, notes, filename, scale=4):
        im = Image.open(os.path.join(ART, name + ".png")).convert("RGBA")
        w, h = im.size
        pad = 40
        width = max(w * scale + 2 * pad, 1400)
        canvas = Image.new("RGBA", (width, h * scale + 2 * pad + 26 * len(notes) + 20), (255, 255, 255, 255))
        frame = Image.new("RGBA", (w * scale, h * scale), (60, 62, 70, 255))
        frame.alpha_composite(im.resize((w * scale, h * scale), Image.NEAREST))
        canvas.paste(frame, (pad, pad))
        d = ImageDraw.Draw(canvas)
        d.rectangle([pad - 1, pad - 1, pad + w * scale, pad + h * scale], outline=(0, 0, 0), width=1)
        for x, y, color, label in marks:
            X, Y = pad + x * scale, pad + y * scale
            d.line([X - 14, Y, X + 14, Y], fill=color, width=3)
            d.line([X, Y - 14, X, Y + 14], fill=color, width=3)
            d.ellipse([X - 5, Y - 5, X + 5, Y + 5], outline=color, width=2)
            d.text((X + 10, Y + 6), label, fill=color, font=font, stroke_width=2, stroke_fill=(255, 255, 255))
        y = pad + h * scale + pad // 2
        for text in notes:
            d.text((pad, y), text, fill=(0, 0, 0), font=font)
            y += 26
        canvas.convert("RGB").save(os.path.join(out, filename))

    red, orange, blue, green = (220, 30, 30), (230, 120, 0), (30, 90, 220), (20, 140, 40)

    # ---- hip
    name = "Spr_PixelRifleHip"
    w, h = sprites[name]["size"]
    px, py = pivot_px(name)
    hold = rifle["hipHoldOffset"]
    muzzle = rifle["hipMuzzlePosition"]
    aspect = rifle["referenceAspect"]
    right = (half_h * aspect - muzzle[0]) * ppu + px
    bottom = (muzzle[1] + half_h) * ppu + py
    centre = (px - muzzle[0] * ppu, py + muzzle[1] * ppu)
    guide(name, [
        (px, py, red, "銃口＝ピボット（銃口炎の位置）"),
        (px + hold[0] * ppu, py - hold[1] * ppu, blue, "持つ位置（反動・リロードの回転の中心）"),
    ], [
        f"腰だめ：{w}×{h}ドット、1ドット＝{1 / ppu:.5f}単位（1080p画面で約{1080 / (2 * half_h * ppu):.1f}px）。",
        f"16:9のとき画面の右端はx={right:.1f}、下端はy={bottom:.1f}（キャンバスの右端・下端とほぼ一致）。左上に余白なし。",
        f"画面中央（照準）はキャンバス座標({centre[0]:.1f}, {centre[1]:.1f})＝銃口の左上の画面外。銃は中央にかけない。",
        f"反動：({rifle['kickOffset'][0]}, {rifle['kickOffset'][1]})単位・{rifle['kickRollDegrees']}°・{rifle['kickScale'] * 100:.0f}%拡大。"
        f"リロード：{rifle['reloadDrop']}単位（約{rifle['reloadDrop'] * ppu:.0f}ドット）下げて{rifle['reloadRollDegrees']}°傾ける（持つ位置中心）。",
        f"持ち替え：0.45単位（約{0.45 * ppu:.0f}ドット）下へ沈む。iPad（4:3）は{rifle['hipScaleAt4x3']}倍で右端へ{rifle['hipRightEdgeFollow']}追従。",
        "右端・下端は、動いたときに切れ目が見えないよう、キャンバスの端まで描き切る（腕・銃床は画面外へ続く形）。",
    ], "guide_hip.png")

    # ---- ADS
    name = "Spr_PixelRifleAds"
    w, h = sprites[name]["size"]
    px, py = pivot_px(name)
    m = rifle["adsMuzzleOffset"]
    hold = rifle["adsHoldOffset"]
    guide(name, [
        (px, py, red, "フロントサイト先端＝照準点＝ピボット（画面中央）"),
        (px + m[0] * ppu, py - m[1] * ppu, orange, "銃口（銃口炎の位置）"),
        (px + hold[0] * ppu, py - hold[1] * ppu, blue, "持つ位置（回転の中心）"),
    ], [
        f"ADS：{w}×{h}ドット。ピボット({px:.1f}, {py:.1f})が画面中央＝実際の弾の方向。下端はピボットの{h - py:.0f}ドット下＝画面の下端（{half_h * ppu:.0f}ドット）。",
        f"銃口はピボットの真下{-m[1] * ppu:.1f}ドット。現在の素材（ベイク由来）には手・グローブが描かれていない。",
        f"腰だめ→ADSは構えの{rifle['crossfadeStart']}〜{rifle['crossfadeEnd']}で切り替え（クロスフェード）、ADSは下から{-rifle['adsStartOffset'][1]}単位せり上がる。",
        f"ADS中の反動は腰だめの{rifle['adsKickMultiplier']}倍。リロードは腰だめと同じ動き（下げる・傾ける）。",
    ], "guide_ads.png")

    # ---- knife
    name = "Spr_PixelKnife"
    w, h = sprites[name]["size"]
    px, py = pivot_px(name)
    guide(name, [
        (px, py, red, "ピボット（握りの基準）"),
    ], [
        f"ナイフ：{w}×{h}ドット。ピボット({px:.1f}, {py:.1f})が画面の({knife['gripPosition'][0]}, {knife['gripPosition'][1]})単位に置かれる。刃先は左上、手と袖は右下の画面端から入る。",
        f"振り：構え {knife['windupRoll']}°・({knife['windupOffset'][0]}, {knife['windupOffset'][1]})、振り抜き {knife['strikeRoll']}°・"
        f"({knife['strikeOffset'][0]}, {knife['strikeOffset'][1]})・{knife['strikeScale']}倍、その後戻る。持ち替え時は{knife['lowered']}単位下げる。",
        "回転・拡大しても袖の切れ目が画面内に出ないよう、右端・下端はキャンバスの端まで描き切る。",
    ], "guide_knife.png")
    print("written to", out)


if __name__ == "__main__":
    main()
