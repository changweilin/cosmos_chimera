// P1-07 / P3-04 (D0): cel-shaded planet terrain coloured by altitude, slope and latitude.
// Biome inputs are derived per pixel from the sphere itself (height = |p − centre| − R, up = normalize(p − centre)),
// never from world +Y: on a planet "up" changes everywhere (CLAUDE.md#硬性規則).
Shader "Cosmimera/ToonTerrain"
{
    Properties
    {
        _PlanetCenter ("Planet Center", Vector) = (0, 0, 0, 0)
        _Radius ("Radius", Float) = 640
        _Relief ("Relief (m)", Float) = 30
        _PoleAxis ("Pole Axis", Vector) = (0, 1, 0, 0)
        _DeepWater ("Deep Water", Color) = (0.08, 0.22, 0.45, 1)
        _ShallowWater ("Shallow Water", Color) = (0.2, 0.55, 0.75, 1)
        _Shore ("Shore", Color) = (0.86, 0.8, 0.6, 1)
        _Low ("Low", Color) = (0.35, 0.62, 0.28, 1)
        _Mid ("Mid", Color) = (0.27, 0.48, 0.22, 1)
        _High ("High", Color) = (0.55, 0.5, 0.42, 1)
        _Rock ("Rock", Color) = (0.45, 0.42, 0.4, 1)
        _Snow ("Snow", Color) = (0.94, 0.96, 1, 1)
        _SnowLine ("Snow Line (0..1 of relief)", Float) = 0.8
        _PolarLat ("Polar Latitude (sin)", Float) = 0.85
        _Bands ("Light Bands", Float) = 3
        _Shadow ("Shadow Tint", Color) = (0.55, 0.6, 0.78, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ToonForward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _PlanetCenter;
                float _Radius;
                float _Relief;
                float4 _PoleAxis;
                half4 _DeepWater, _ShallowWater, _Shore, _Low, _Mid, _High, _Rock, _Snow, _Shadow;
                float _SnowLine;
                float _PolarLat;
                float _Bands;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float fog : TEXCOORD2;
            };

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

            half3 Biome(float h, float slope, float lat)
            {
                float t = saturate(h / max(_Relief, 1e-3));
                if (h < 0.0)
                {
                    return lerp(_ShallowWater.rgb, _DeepWater.rgb, saturate(-h / (_Relief * 0.15)));
                }

                half3 c = h < 0.6 ? _Shore.rgb : (t < 0.35 ? lerp(_Low.rgb, _Mid.rgb, t / 0.35) : lerp(_Mid.rgb, _High.rgb, (t - 0.35) / 0.65));
                c = lerp(c, _Rock.rgb, smoothstep(0.25, 0.4, slope));
                float snow = max(step(_SnowLine, t), step(_PolarLat, lat));
                return lerp(c, _Snow.rgb, snow * (1.0 - smoothstep(0.35, 0.55, slope)));
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float3 rel = i.positionWS - _PlanetCenter.xyz;
                float r = length(rel);
                float3 up = rel / r;
                float3 n = normalize(i.normalWS);
                float h = r - _Radius;
                float slope = 1.0 - saturate(dot(n, up));
                float lat = abs(dot(up, normalize(_PoleAxis.xyz)));
                half3 albedo = Biome(h, slope, lat);

                Light light = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                float ndl = saturate(dot(n, light.direction)) * light.shadowAttenuation;
                float band = floor(ndl * _Bands + 0.5) / _Bands; // cel steps instead of a smooth ramp (vision.md: 卡通)
                half3 lit = albedo * lerp(_Shadow.rgb * 0.6, light.color, band) + albedo * SampleSH(n) * 0.25;
                return half4(MixFog(lit, i.fog), 1);
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }
}
