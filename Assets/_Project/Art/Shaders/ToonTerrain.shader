// P1-07 / P3-04 (D0): cel-shaded planet terrain coloured by altitude, slope and latitude.
// Biome inputs are derived per pixel from the sphere itself (height = |p − centre| − R, up = normalize(p − centre)),
// never from world +Y: on a planet "up" changes everywhere (CLAUDE.md#硬性規則).
// Colour field: CosmiBiome.hlsl (shared with the grass). Light, cloud shadow, aerial perspective: CosmiAtmosphere.hlsl.
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
        _Patch ("Sunlit Patch Tint", Color) = (1.1, 1.06, 0.84, 1)
        _SnowLine ("Snow Line (0..1 of relief)", Float) = 0.8
        _PolarLat ("Polar Latitude (sin)", Float) = 0.85
        _Bands ("Light Bands", Float) = 3
        _Shadow ("Shadow Tint", Color) = (0.55, 0.6, 0.78, 1)
        _Foam ("Surf Foam", Range(0, 1)) = 0
        _SeaLevel ("Sea Level (m)", Float) = -0.12
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
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "CosmiBiome.hlsl"

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

            half4 Frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                CosmiGround g = CosmiGroundAt(i.positionWS, n);
                float grass;
                half3 albedo = CosmiBiomeColor(i.positionWS, g, grass);

                Light light = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                // Hemisphere ambient: faces turned to the sky take the zenith colour, undersides the ground bounce.
                half3 ambient = SampleSH(n) * lerp(0.75, 1.25, saturate(dot(n, g.up)));
                half3 lit = CosmiToon(albedo, n, i.positionWS, light.color, light.direction, light.shadowAttenuation,
                    _Shadow.rgb, _Bands, ambient);
                return half4(CosmiAtmosphere(lit, i.positionWS, i.fog, light.direction), 1);
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }
}
