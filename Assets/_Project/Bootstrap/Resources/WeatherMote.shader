Shader "Cosmimera/WeatherMote"
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
                half radius = dot(i.uv, i.uv);
                return half4(i.color.rgb, i.color.a * (1.0 - smoothstep(0.2, 1.0, radius)));
            }
            ENDHLSL
        }
    }
}
