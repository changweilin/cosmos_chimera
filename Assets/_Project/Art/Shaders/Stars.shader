// P1-08 (D0): night-sky star shell. Additive, unlit, and deliberately NOT fogged: the shell sits near the far plane,
// where linear fog is fully opaque and would erase every star.
Shader "Cosmimera/Stars"
{
    Properties
    {
        _Visibility ("Visibility", Range(0, 1)) = 0
    }

    SubShader
    {
        // Transparent queue: drawn after opaque terrain, so ZTest against its depth hides stars behind hills.
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-100" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Stars"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Visibility;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; };

            Varyings Vert(Attributes i)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.color = i.color;
                o.uv = i.uv;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                // Round soft dot from a quad: cheaper than a texture and keeps the shader asset-free.
                float d = saturate(1.0 - dot(i.uv, i.uv));
                return half4(i.color.rgb * (d * d * _Visibility), 0);
            }
            ENDHLSL
        }
    }
}
