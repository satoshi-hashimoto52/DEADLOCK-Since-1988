// Glow layer of a first-person sprite (Stage 38, EPIC SMG): URP's Sprite-Unlit-Default, added to what is under it
// instead of painted over it, so a soft, mostly transparent glow lightens the gun and the background like light does
// (no post-processing, no bloom). colour x alpha x _Gain is added; the renderer's alpha (glow intensity, pulse) scales it.
// A faint glow image (alpha a few / 255) needs a high gain to show its spread (EPIC SMG: 14).
Shader "Deadlock/Sprite Additive Glow"
{
    Properties
    {
        _MainTex ("Sprite Texture", 2D) = "white" {}
        _Gain ("Gain", Range(0, 24)) = 3

        [HideInInspector] _Color ("Tint", Color) = (1,1,1,1)
        [HideInInspector] PixelSnap ("Pixel snap", Float) = 0
        [HideInInspector] _RendererColor ("RendererColor", Color) = (1,1,1,1)
        [HideInInspector] _AlphaTex ("External Alpha", 2D) = "white" {}
        [HideInInspector] _EnableExternalAlpha ("Enable External Alpha", Float) = 0
    }

    SubShader
    {
        Tags {"Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Blend One One, Zero One
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

            #pragma vertex GlowVertex
            #pragma fragment GlowFragment

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
                half _Gain;
            CBUFFER_END

            Varyings GlowVertex(Attributes input)
            {
                UNITY_SKINNED_VERTEX_COMPUTE(input);
                SetUpSpriteInstanceProperties();
                input.positionOS = UnityFlipSprite(input.positionOS, unity_SpriteProps.xy);

                Varyings o = CommonUnlitVertex(input);
                o.color = input.color * _Color * unity_SpriteColor;
                return o;
            }

            half4 GlowFragment(Varyings input) : SV_Target
            {
                half4 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv) * input.color;
                return half4(c.rgb * c.a * _Gain, 0);
            }
            ENDHLSL
        }
    }
}
