Shader "Cosmimera/Bubble"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
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
                o.uv = i.uv * 2.0 - 1.0;
                return o;
            }
            half4 Frag(Varyings i) : SV_Target
            {
                // A bubble reads by its bright rim and one specular glint; its middle is nearly clear.
                half r = length(i.uv);
                half rim = smoothstep(0.62, 0.8, r) * (1.0 - smoothstep(0.86, 1.0, r));
                half glint = 1.0 - smoothstep(0.08, 0.2, length(i.uv - half2(-0.32, 0.34)));
                half a = saturate(rim * 0.85 + glint + 0.1 * step(r, 0.8));
                return half4(i.color.rgb, i.color.a * a);
            }
            ENDHLSL
        }
    }
}
