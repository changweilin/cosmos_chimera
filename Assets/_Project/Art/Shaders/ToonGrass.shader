// P1-09: GPU-instanced grass clumps bent by the wind field. REQ: docs/design/vision.md (風場驅動草、GPU instancing).
// Each instance is one clump whose local +Y is the planet's up at its root (set by GrassField); the blade colour at
// the root is the terrain's own biome colour (CosmiBiome.hlsl), so grass grows out of the ground instead of sitting
// on it, and lightens toward the tip.
Shader "Cosmimera/ToonGrass"
{
    Properties
    {
        _GrassHeight ("Blade Height Scale", Float) = 1
        _GrassFade ("Fade Distance (m)", Float) = 32
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
        [HideInInspector] _SeaLevel ("Sea Level (m)", Float) = -0.12
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry+10" }

        Pass
        {
            Name "GrassForward"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "CosmiBiome.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0; // x = per-blade random, y = 0 at the root … 1 at the tip
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 rootWS : TEXCOORD1;
                float3 tip : TEXCOORD2; // x = height fraction, y = gust 0..1, z = fog factor
            };

            Varyings Vert(Attributes i)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(i);
                float3 root = TransformObjectToWorld(float3(0, 0, 0));
                float3 up = normalize(root - _PlanetCenter.xyz);
                float h = i.uv.y;

                // Shrink toward the streaming edge so clumps grow in rather than pop.
                float dist = distance(root, _WorldSpaceCameraPos);
                float size = saturate((_GrassFade - dist) / max(_GrassFade * 0.3, 1.0)) * _GrassHeight;
                float3 local = i.positionOS.xyz;
                local.y *= size;
                float3 ws = TransformObjectToWorld(local);

                // Wind: a slow sway every blade has, plus gust fronts that travel downwind across the field (the
                // light bands that roll over a meadow). Bend grows with h² so roots stay planted.
                float3 wind = CosmiWindDir(up);
                float along = dot(root, wind);
                float across = dot(root, cross(up, wind));
                float wave = max(_CosmiWind.z, 1.0);
                float gust = CosmiNoise(float3(along / wave - _Time.y * _CosmiWind.y, across / (wave * 1.7), 0.5));
                gust = smoothstep(0.35, 0.8, gust);
                float sway = sin(_Time.y * 2.1 + along * 0.9 + i.uv.x * 6.28) * 0.5 + 0.5;
                float bend = (0.03 + sway * 0.04 + gust * 0.2) * _CosmiWind.x;

                // The player parts the grass: blades inside the push radius lean away and lie lower.
                float3 away = root - _CosmiPlayer.xyz;
                away -= up * dot(away, up);
                float push = saturate(1.0 - length(away) / max(_CosmiPlayer.w, 1e-3));
                float3 lean = wind * bend + normalize(away + wind * 1e-3) * push * 0.4;

                float h2 = h * h * size;
                ws += lean * h2 - up * (dot(lean, lean) * h2 * 0.45 + push * h2 * 0.25);

                o.positionWS = ws;
                o.rootWS = root;
                o.positionCS = TransformWorldToHClip(ws);
                o.tip = float3(h, gust * _CosmiWind.x, ComputeFogFactor(o.positionCS.z));
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                // The ground's colour and normal at the root: a blade has no meaningful normal of its own, and
                // shading every blade like the ground under it is what makes the field read as one soft surface.
                float3 up = normalize(i.rootWS - _PlanetCenter.xyz);
                CosmiGround g = CosmiGroundAt(i.rootWS, up);
                float grass;
                half3 ground = CosmiBiomeColor(i.rootWS, g, grass);
                half3 tipColor = ground * half3(1.16, 1.2, 0.9) + half3(0.012, 0.024, 0.0);
                half3 albedo = lerp(ground * 0.93, tipColor, i.tip.x); // mean ≈ ground: no edge where grass streams out
                albedo += i.tip.y * i.tip.x * 0.07; // gust fronts flash the pale underside

                // Shadow lookup from blade height, not the buried root (which the ground itself would shadow).
                Light light = GetMainLight(TransformWorldToShadowCoord(i.rootWS + up * 0.25));
                half3 lit = CosmiToon(albedo, up, i.rootWS, light.color, light.direction, light.shadowAttenuation,
                    _Shadow.rgb, _Bands, SampleSH(up));
                return half4(CosmiAtmosphere(lit, i.positionWS, i.tip.z, light.direction), 1);
            }
            ENDHLSL
        }
    }
}
