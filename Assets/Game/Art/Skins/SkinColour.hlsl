// Shared colour change of the skin shaders (Deadlock/Skinned Sprite for the first-person sprites, Deadlock/Skinned UI
// for the equipment screen). One place for the maths, so both views of a skin give the same colour.
// Region map value = region index x 32 (Tools/pixelart/make_skin_regions.py / make_image_regions.py). Table entry:
// x = hue 0..1, y = saturation, z = 1 when recoloured. A recoloured pixel keeps its HLS lightness (the drawn shading)
// and gets the entry's hue / saturation, computed on the sRGB value like Tools/pixelart/skin_preview.py.
#ifndef DEADLOCK_SKIN_COLOUR_INCLUDED
#define DEADLOCK_SKIN_COLOUR_INCLUDED

int SkinRegionIndex(float regionValue)
{
    return clamp((int)floor(regionValue * 255.0 / 32.0 + 0.5), 0, 7);
}

float SkinHueChannel(float m1, float m2, float h)
{
    h = frac(h);
    if (h < 1.0 / 6.0) return m1 + (m2 - m1) * h * 6.0;
    if (h < 0.5) return m2;
    if (h < 2.0 / 3.0) return m1 + (m2 - m1) * (2.0 / 3.0 - h) * 6.0;
    return m1;
}

// Python colorsys.hls_to_rgb.
float3 SkinHlsToRgb(float h, float l, float s)
{
    if (s <= 0.0) return float3(l, l, l);
    float m2 = l <= 0.5 ? l * (1.0 + s) : l + s - l * s;
    float m1 = 2.0 * l - m2;
    return float3(SkinHueChannel(m1, m2, h + 1.0 / 3.0), SkinHueChannel(m1, m2, h), SkinHueChannel(m1, m2, h - 1.0 / 3.0));
}

float3 SkinLinearToSrgb(float3 c)
{
    c = max(c, 0.0);
    return float3(
        c.r <= 0.0031308 ? c.r * 12.92 : 1.055 * pow(c.r, 1.0 / 2.4) - 0.055,
        c.g <= 0.0031308 ? c.g * 12.92 : 1.055 * pow(c.g, 1.0 / 2.4) - 0.055,
        c.b <= 0.0031308 ? c.b * 12.92 : 1.055 * pow(c.b, 1.0 / 2.4) - 0.055);
}

float3 SkinSrgbToLinear(float3 c)
{
    return float3(
        c.r <= 0.04045 ? c.r / 12.92 : pow((c.r + 0.055) / 1.055, 2.4),
        c.g <= 0.04045 ? c.g / 12.92 : pow((c.g + 0.055) / 1.055, 2.4),
        c.b <= 0.04045 ? c.b / 12.92 : pow((c.b + 0.055) / 1.055, 2.4));
}

// rgb = the sampled texture colour as the shader sees it (linear in a linear-space project).
float3 SkinRecolour(float3 rgb, float4 skin)
{
    if (skin.z <= 0.5)
        return rgb;
#if defined(UNITY_COLORSPACE_GAMMA)
    float3 srgb = rgb;
#else
    float3 srgb = SkinLinearToSrgb(rgb);
#endif
    float l = 0.5 * (max(srgb.r, max(srgb.g, srgb.b)) + min(srgb.r, min(srgb.g, srgb.b)));
    float3 result = SkinHlsToRgb(skin.x, l, skin.y);
#if defined(UNITY_COLORSPACE_GAMMA)
    return result;
#else
    return SkinSrgbToLinear(result);
#endif
}

#endif
