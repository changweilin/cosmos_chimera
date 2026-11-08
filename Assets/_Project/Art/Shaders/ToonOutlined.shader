// P1-07 (D0): cel-shaded character/creature material with an inverted-hull outline (vision.md 角色反向外殼描邊).
Shader "Cosmimera/ToonOutlined"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        _Shadow ("Shadow Tint", Color) = (0.55, 0.6, 0.78, 1)
        _Bands ("Light Bands", Float) = 2
        _OutlineColor ("Outline Color", Color) = (0.08, 0.06, 0.1, 1)
        _OutlineWidth ("Outline Width (m)", Float) = 0.025
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half4 _Shadow;
            float _Bands;
            half4 _OutlineColor;
            float _OutlineWidth;
        CBUFFER_END
        struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
        ENDHLSL

        Pass
        {
            Name "ToonForward"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float fog : TEXCOORD1; };
            Varyings Vert(Attributes i)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(i.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }
            half4 Frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                Light light = GetMainLight();
                float band = floor(saturate(dot(n, light.direction)) * _Bands + 0.5) / _Bands;
                half3 lit = _BaseColor.rgb * lerp(_Shadow.rgb * 0.6, light.color, band) + _BaseColor.rgb * SampleSH(n) * 0.25;
                return half4(MixFog(lit, i.fog), 1);
            }
            ENDHLSL
        }

        Pass
        {
            // SRPDefaultUnlit is the second pass URP draws for an opaque object without a custom renderer feature.
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            float4 Vert(Attributes i) : SV_POSITION
            {
                float3 ws = TransformObjectToWorld(i.positionOS.xyz);
                ws += TransformObjectToWorldNormal(i.normalOS) * _OutlineWidth;
                return TransformWorldToHClip(ws);
            }
            half4 Frag() : SV_Target { return _OutlineColor; }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
    }
}
