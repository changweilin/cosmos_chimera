// P3-22 (planet-world.md#地震噴發): a quake crack and the lava beside it, drawn as a ribbon on the ground.
// uv.x runs along the crack (0..1), uv.y across it (0..1, 0.5 = the fissure). _Spread masks how far the lava has
// welled out from the fissure; _Heat cools it from molten (1) through crusting plates to black rock (0).
Shader "Cosmimera/LavaFlow"
{
    Properties
    {
        _Heat ("Heat", Range(0, 1)) = 1
        _Spread ("Spread", Range(0, 1)) = 0.2
        _Seed ("Seed", Float) = 0
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent-60" "RenderType"="Transparent" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Offset -2, -2
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Heat;
                float _Spread;
                float _Seed;
            CBUFFER_END

            struct Attributes { float4 position : POSITION; float3 normal : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                float fog : TEXCOORD3;
            };

            float Hash(float2 p)
            {
                p = frac(p * float2(0.1031, 0.1030));
                p += dot(p, p.yx + 33.33);
                return frac((p.x + p.y) * p.x);
            }

            float Noise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), f.x), lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), f.x), f.y);
            }

            Varyings Vert(Attributes i)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(i.position.xyz);
                o.position = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(i.normal);
                o.uv = i.uv;
                o.fog = ComputeFogFactor(o.position.z);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float across = abs(i.uv.y - 0.5) * 2.0;
                // Ragged lobes: the lava front is not a straight edge.
                float edge = _Spread * (0.75 + 0.35 * Noise(float2(i.uv.x * 22.0 + _Seed, 3.0)));
                float inside = 1.0 - smoothstep(edge - 0.06, edge, across);
                clip(inside - 0.01);

                float2 q = i.positionWS.xz * 0.9 + i.positionWS.y * 0.37 + _Seed;
                float plates = Noise(q) * 0.65 + Noise(q * 2.7 + 9.0) * 0.35;
                float t = _Time.y;
                // Molten where the plates have not crusted yet; the fissure itself stays hottest longest.
                float molten = smoothstep(0.0, 0.08, _Heat * 1.25 * (1.0 - across * 0.4) - plates);
                float flow = Noise(float2(i.uv.x * 30.0 - t * 0.6, across * 6.0 + _Seed));
                half3 hot = lerp(half3(1.0, 0.32, 0.05), half3(1.0, 0.85, 0.35), saturate(flow * _Heat * (1.0 - across)));

                Light light = GetMainLight();
                half lit = saturate(dot(normalize(i.normalWS), light.direction)) * 0.7 + 0.3;
                half3 rock = half3(0.016, 0.014, 0.015) * (1.0 + plates * 0.6) * lit * light.color + unity_AmbientSky.rgb * 0.05;
                // Linear-space values: basalt ~0.12 in sRGB. Cooling rock glows dull red through its cracks before it goes black.
                half3 ember = half3(0.55, 0.08, 0.02) * smoothstep(0.55, 0.8, plates) * saturate(_Heat * 3.0);
                half3 col = lerp(rock + ember, hot * (1.4 + 0.6 * _Heat), molten);
                float fissure = (1.0 - smoothstep(0.0, 0.08, across)) * saturate(_Heat * 2.0);
                col = lerp(col, half3(1.0, 0.95, 0.7) * 1.6, fissure);
                col = MixFog(col, i.fog);
                return half4(col, inside);
            }
            ENDHLSL
        }
    }
}
