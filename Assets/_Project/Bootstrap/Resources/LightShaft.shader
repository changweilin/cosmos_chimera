Shader "Cosmimera/LightShaft"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            // Additive: shafts are scattered sunlight, they only ever brighten the water behind them.
            Blend SrcAlpha One
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
                // Soft across the beam, brightest near the surface end and fading out into the depth.
                half across = 1.0 - abs(i.uv.y * 2.0 - 1.0);
                half along = smoothstep(0.0, 0.3, i.uv.x) * (1.0 - smoothstep(0.6, 1.0, i.uv.x));
                return half4(i.color.rgb, i.color.a * across * along);
            }
            ENDHLSL
        }
    }
}
