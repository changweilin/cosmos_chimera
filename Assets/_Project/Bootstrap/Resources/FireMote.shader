Shader "Cosmimera/FireMote"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            // Additive: overlapping flame tongues pile up into a white-hot core the way real fire does, and the fire
            // never darkens what is behind it.
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
                o.uv = i.uv * 2.0 - 1.0;
                return o;
            }
            half4 Frag(Varyings i) : SV_Target
            {
                // A tongue, not a disc: narrower toward the top of the (velocity-stretched) quad.
                half2 p = i.uv;
                p.x *= 1.0 + saturate(p.y) * 0.8;
                half r = dot(p, p);
                half soft = 1.0 - smoothstep(0.15, 1.0, r);
                return half4(i.color.rgb, i.color.a * soft);
            }
            ENDHLSL
        }
    }
}
