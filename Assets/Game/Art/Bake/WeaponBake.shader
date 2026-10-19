// Editor-only shader used by WeaponSpriteBaker to pre-render the first-person weapon sprites.
// _BakeMode: 0 = cel-shaded colour, 1 = part id (R channel), 2 = world normal.
Shader "Hidden/Deadlock/WeaponBake"
{
    Properties
    {
        _BaseColor("Base Colour", Color) = (1, 1, 1, 1)
        _PartId("Part Id", Vector) = (0, 0, 0, 1)
        _Spec("Specular", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _PartId;
                float _Spec;
            CBUFFER_END

            float _BakeMode;
            float4 _BakeLightDir;
            float4 _BakeCameraPos;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float3 positionWS : TEXCOORD1; };

            Varyings vert(Attributes input)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return o;
            }

            float4 frag(Varyings input) : SV_Target
            {
                float3 n = normalize(input.normalWS);
                if (_BakeMode > 1.5)
                    return float4(n * 0.5 + 0.5, 1);
                if (_BakeMode > 0.5)
                    return float4(_PartId.xyz, 1);

                // Hard light bands (no gradients) so the sprite reads crisp, with one light direction for every part.
                float3 l = normalize(_BakeLightDir.xyz);
                float ndl = dot(n, l);
                float shade = ndl > 0.5 ? 1.0 : (ndl > 0.1 ? 0.8 : (ndl > -0.3 ? 0.6 : 0.45));
                // Stepped depth falloff: near parts slightly brighter than far ones, in visible bands instead of a gradient.
                float dist = distance(_BakeCameraPos.xyz, input.positionWS);
                shade *= dist < 0.45 ? 1.0 : (dist < 0.7 ? 0.92 : 0.84);
                float3 c = _BaseColor.rgb * shade;

                float3 v = normalize(_BakeCameraPos.xyz - input.positionWS);
                float3 h = normalize(l + v);
                if (_Spec > 0 && dot(n, h) > 0.94)
                    c = lerp(c, float3(1, 1, 1), 0.5 * _Spec);
                return float4(c, 1);
            }
            ENDHLSL
        }
    }
}
