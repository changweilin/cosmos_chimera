// P3-04: the liquid surface (sea, or molten rock on lava worlds) at altitude _SeaLevel.
// The mesh is a polar disc in (distance along the surface, bearing) that the vertex shader wraps onto the sphere
// directly under the camera. Why not a tessellated sphere: a chord of length L sags L²/8R below the true surface, so
// a whole-planet mesh fine enough to keep the shoreline put needs ~10⁵ triangles; the disc is dense only near the
// viewer, where the sag would be visible, and its surface pattern is hashed from world position, so nothing swims
// when the disc follows the camera.
Shader "Cosmimera/ToonWater"
{
    Properties
    {
        _WaterTint ("Tint (a = opacity)", Color) = (0.2, 0.55, 0.75, 0.6)
        _WaterGlow ("Self-lit (lava)", Range(0, 1)) = 0
        _SeaLevel ("Sea Level (m)", Float) = -0.12
        // Biome layout shared with ToonTerrain (CosmiBiome.hlsl). Declared here, not just in the CBUFFER: a property
        // missing from this block reads as 0 on instanced draws, which turned every blade black.
        [HideInInspector] _PlanetCenter ("Planet Center", Vector) = (0, 0, 0, 0)
        [HideInInspector] _Radius ("Radius", Float) = 640
        [HideInInspector] _Relief ("Relief (m)", Float) = 30
        [HideInInspector] _PoleAxis ("Pole Axis", Vector) = (0, 1, 0, 0)
        [HideInInspector] _DeepWater ("Deep Water", Color) = (0.08, 0.22, 0.45, 1)
        [HideInInspector] _ShallowWater ("Shallow Water", Color) = (0.2, 0.55, 0.75, 1)
        [HideInInspector] _Shore ("Shore", Color) = (0.86, 0.8, 0.6, 1)
        [HideInInspector] _Low ("Low", Color) = (0.35, 0.62, 0.28, 1)
        [HideInInspector] _Mid ("Mid", Color) = (0.27, 0.48, 0.22, 1)
        [HideInInspector] _High ("High", Color) = (0.55, 0.5, 0.42, 1)
        [HideInInspector] _Rock ("Rock", Color) = (0.45, 0.42, 0.4, 1)
        [HideInInspector] _Snow ("Snow", Color) = (0.94, 0.96, 1, 1)
        [HideInInspector] _Patch ("Sunlit Patch Tint", Color) = (1.1, 1.06, 0.84, 1)
        [HideInInspector] _SnowLine ("Snow Line", Float) = 0.8
        [HideInInspector] _PolarLat ("Polar Latitude (sin)", Float) = 0.85
        [HideInInspector] _Bands ("Light Bands", Float) = 3
        [HideInInspector] _Shadow ("Shadow Tint", Color) = (0.55, 0.6, 0.78, 1)
        [HideInInspector] _Foam ("Surf Foam", Range(0, 1)) = 0
    }

    SubShader
    {
        // After the stars (Transparent-100): the sea blends over them at the horizon instead of under.
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-50" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Water"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off // also seen from below when the camera dips under the surface

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "CosmiBiome.hlsl"

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; }; // uv = (distance m, bearing rad)
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float fog : TEXCOORD1;
            };

            Varyings Vert(Attributes i)
            {
                Varyings o;
                float level = _Radius + _SeaLevel;
                float3 up = normalize(_WorldSpaceCameraPos - _PlanetCenter.xyz);
                float3 helper = abs(up.y) < 0.9 ? float3(0, 1, 0) : float3(1, 0, 0);
                float3 e1 = normalize(cross(helper, up));
                float3 e2 = cross(up, e1);
                float a = min(i.uv.x / level, 3.0);
                float s, c, sb, cb;
                sincos(a, s, c);
                sincos(i.uv.y, sb, cb);
                float3 dir = up * c + (e1 * cb + e2 * sb) * s;
                o.positionWS = _PlanetCenter.xyz + dir * level;
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float3 up = normalize(i.positionWS - _PlanetCenter.xyz);
                float3 view = normalize(_WorldSpaceCameraPos - i.positionWS);
                float t = _Time.y;
                // Two drifting noise layers stand in for a wave normal map.
                float3 p = i.positionWS;
                float3 ripple = float3(CosmiNoise(p * 0.55 + float3(t * 0.31, 0, t * 0.17)),
                                       CosmiNoise(p * 0.55 + float3(11.0, t * 0.23, 5.0)),
                                       CosmiNoise(p * 0.55 + float3(3.0, 7.0, -t * 0.27))) - 0.5;
                float3 n = normalize(up + (ripple - up * dot(ripple, up)) * 0.22);

                Light light = GetMainLight();
                float ndv = saturate(dot(n, view));
                float fresnel = pow(1.0 - ndv, 4.0);
                float ramp = CosmiRamp(saturate(dot(up, light.direction)) * CosmiCloudLight(i.positionWS) + 0.2, 3.0);

                half3 sky = lerp(_CosmiSkyZenith.rgb, _CosmiSkyHorizon.rgb, pow(1.0 - ndv, 2.0));
                half3 body = _WaterTint.rgb * lerp(_Shadow.rgb * 0.7, light.color, ramp);
                half3 col = lerp(body, sky, fresnel * 0.75 * _CosmiSkyHorizon.a);

                // Flat cel wave crests, and hard-edged sun glitter along the reflection.
                float crest = CosmiNoise(p * 0.35 + float3(t * 0.2, t * 0.11, 0.0));
                col += smoothstep(0.66, 0.69, crest) * 0.09 * ramp;
                float spec = pow(saturate(dot(n, normalize(light.direction + view))), 260.0);
                col += step(0.45, spec) * light.color * ramp * 0.8;

                // Molten surface: lit from within, with slow dark crust plates.
                float crust = smoothstep(0.42, 0.6, CosmiFbm2(p * 0.18 + float3(0.0, t * 0.03, 0.0)));
                half3 glow = _WaterTint.rgb * lerp(1.45, 0.42, crust);
                col = lerp(col, glow, _WaterGlow);

                float alpha = lerp(_WaterTint.a, 1.0, saturate(fresnel + _WaterGlow));
                return half4(CosmiAtmosphere(col, i.positionWS, i.fog, light.direction), alpha);
            }
            ENDHLSL
        }
    }
}
