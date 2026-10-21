// First-person sprite with skins: URP's Sprite-Unlit-Default plus a colour change by region map.
// _SkinRegions (8-bit, value = region index x 32, point-sampled on the same canvas as the sprite) picks an entry of
// _SkinColours (x = hue 0..1, y = saturation, z = 1 when recoloured). A recoloured pixel keeps its HLS lightness (the
// drawn shading) and gets the entry's hue / saturation, computed on the sRGB value like Tools/pixelart/skin_preview.py.
// Region 0 (line black, steel, unmasked) and entries with z = 0 keep the drawn colour exactly.
Shader "Deadlock/Skinned Sprite"
{
    Properties
    {
        _MainTex ("Sprite Texture", 2D) = "white" {}
        [NoScaleOffset] _SkinRegions ("Skin Regions", 2D) = "black" {}
        [MaterialToggle] _ZWrite("ZWrite", Float) = 0

        [HideInInspector] _Color ("Tint", Color) = (1,1,1,1)
        [HideInInspector] PixelSnap ("Pixel snap", Float) = 0
        [HideInInspector] _RendererColor ("RendererColor", Color) = (1,1,1,1)
        [HideInInspector] _AlphaTex ("External Alpha", 2D) = "white" {}
        [HideInInspector] _EnableExternalAlpha ("Enable External Alpha", Float) = 0
    }

    SubShader
    {
        Tags {"Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
        Cull Off
        ZWrite [_ZWrite]

        Pass
        {
            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

            #pragma vertex SkinVertex
            #pragma fragment SkinFragment

            struct Attributes
            {
                COMMON_2D_INPUTS
                half4 color : COLOR;
                UNITY_SKINNED_VERTEX_INPUTS
            };

            struct Varyings
            {
                COMMON_2D_OUTPUTS
                half4 color : COLOR;
            };

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/2DCommon.hlsl"

            #pragma multi_compile_instancing
            #pragma multi_compile _ SKINNED_SPRITE

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
            CBUFFER_END

            // Per renderer (property block), outside the per-material buffer.
            TEXTURE2D(_SkinRegions);
            SAMPLER(sampler_point_clamp);
            float4 _SkinColours[8];

            float HueChannel(float m1, float m2, float h)
            {
                h = frac(h);
                if (h < 1.0 / 6.0) return m1 + (m2 - m1) * h * 6.0;
                if (h < 0.5) return m2;
                if (h < 2.0 / 3.0) return m1 + (m2 - m1) * (2.0 / 3.0 - h) * 6.0;
                return m1;
            }

            // Python colorsys.hls_to_rgb.
            float3 HlsToRgb(float h, float l, float s)
            {
                if (s <= 0.0) return float3(l, l, l);
                float m2 = l <= 0.5 ? l * (1.0 + s) : l + s - l * s;
                float m1 = 2.0 * l - m2;
                return float3(HueChannel(m1, m2, h + 1.0 / 3.0), HueChannel(m1, m2, h), HueChannel(m1, m2, h - 1.0 / 3.0));
            }

            Varyings SkinVertex(Attributes input)
            {
                UNITY_SKINNED_VERTEX_COMPUTE(input);
                SetUpSpriteInstanceProperties();
                input.positionOS = UnityFlipSprite(input.positionOS, unity_SpriteProps.xy);

                Varyings o = CommonUnlitVertex(input);
                o.color = input.color * _Color * unity_SpriteColor;
                return o;
            }

            half4 SkinFragment(Varyings input) : SV_Target
            {
                float4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                float region = SAMPLE_TEXTURE2D(_SkinRegions, sampler_point_clamp, input.uv).r;
                int index = clamp((int)floor(region * 255.0 / 32.0 + 0.5), 0, 7);
                float4 skin = _SkinColours[index];
                if (skin.z > 0.5)
                {
                #if UNITY_COLORSPACE_GAMMA
                    float3 srgb = tex.rgb;
                #else
                    float3 srgb = LinearToSRGB(tex.rgb);
                #endif
                    float l = 0.5 * (max(srgb.r, max(srgb.g, srgb.b)) + min(srgb.r, min(srgb.g, srgb.b)));
                    float3 rgb = HlsToRgb(skin.x, l, skin.y);
                #if UNITY_COLORSPACE_GAMMA
                    tex.rgb = rgb;
                #else
                    tex.rgb = SRGBToLinear(rgb);
                #endif
                }
                return tex * input.color;
            }
            ENDHLSL
        }
    }
}
