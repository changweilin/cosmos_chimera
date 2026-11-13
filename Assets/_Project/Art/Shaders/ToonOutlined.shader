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
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include "CosmiAtmosphere.hlsl"
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float fog : TEXCOORD1; float3 positionWS : TEXCOORD2; };
            Varyings Vert(Attributes i)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(i.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }
            half4 Frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                Light light = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                // Same light, cloud shadow and haze as the ground they stand on (CosmiAtmosphere.hlsl), so props and
                // creatures sit in the scene instead of on top of it.
                half3 lit = CosmiToon(_BaseColor.rgb, n, i.positionWS, light.color, light.direction, light.shadowAttenuation,
                    _Shadow.rgb, _Bands, SampleSH(n));
                // Rim: a thin sky-coloured edge on the sunward side separates silhouettes from hazy backgrounds.
                float3 view = normalize(_WorldSpaceCameraPos - i.positionWS);
                float rim = smoothstep(0.62, 0.78, 1.0 - saturate(dot(n, view))) * saturate(dot(n, light.direction) + 0.3);
                lit += _CosmiSkyHorizon.rgb * (rim * 0.22 * _CosmiSkyHorizon.a);
                return half4(CosmiAtmosphere(lit, i.positionWS, i.fog, light.direction), 1);
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
