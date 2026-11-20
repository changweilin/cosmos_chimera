Shader "Cosmimera/LightningBolt"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+10" "RenderType"="Transparent" }
        Pass
        {
            // Additive: a bolt is light, so it must brighten rain streaks and fog behind it, never darken them.
            Blend One One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 position : POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings { float4 position : SV_POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; };
            Varyings Vert(Attributes i)
            {
                Varyings o;
                o.position = TransformObjectToHClip(i.position.xyz);
                o.color = i.color;
                o.uv = i.uv;
                return o;
            }
            half4 Frag(Varyings i) : SV_Target
            {
                // LineRenderer puts v across the ribbon: a white-hot core inside a coloured glow.
                half across = abs(i.uv.y * 2.0 - 1.0);
                half glow = 1.0 - smoothstep(0.0, 1.0, across);
                half core = 1.0 - smoothstep(0.0, 0.28, across);
                half3 rgb = i.color.rgb * glow * 0.9 + core * 1.6;
                return half4(rgb * i.color.a, 1.0);
            }
            ENDHLSL
        }
    }
}
