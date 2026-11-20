// P3-11 roads (planet-world.md#道路與建築; settlement-structures.md 地區×時代): a ribbon draped on the terrain whose
// surface is drawn procedurally per vertex style, so one mesh can pass from a cobbled village street to a dirt track,
// a swamp boardwalk or snow-packed ruts without seams. Asset-free like the other scenery shaders.
// uv0: x = metres along the road, y = across (−1..1; |y| > 1 is the worn verge). uv1: x = style id (see Style*),
// y = grade (rise per metre along the road; stone styles cut steps when steep), z = wear 0..1 (cracks, missing stones,
// grass in the joints), w = road width (m). Vertex colour: rgb = overlay (sand, snow, leaf litter), a = its strength.
Shader "Cosmimera/ToonRoad"
{
    Properties
    {
        _Stone ("Stone / Surface", Color) = (0.6, 0.56, 0.5, 1)
        _Joint ("Joint / Soil", Color) = (0.35, 0.3, 0.25, 1)
        _Verge ("Verge (ground)", Color) = (0.42, 0.62, 0.3, 1)
        _Glow ("Lane Glow", Color) = (0.4, 0.9, 1, 1)
        _Shadow ("Shadow Tint", Color) = (0.55, 0.6, 0.78, 1)
        _Bands ("Light Bands", Float) = 3
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry+10" }

        Pass
        {
            Name "RoadForward"
            Tags { "LightMode" = "UniversalForward" }
            // The ribbon rides only a few centimetres above the ground; pull it forward in depth instead of lifting
            // it, so it reads as part of the ground rather than a strip laid on top.
            Offset -1, -6

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "../../Art/Shaders/CosmiAtmosphere.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Stone;
                half4 _Joint;
                half4 _Verge;
                half4 _Glow;
                half4 _Shadow;
                float _Bands;
            CBUFFER_END

            // Style ids (SettlementView.RoadStyle).
            #define StyleDirt 0
            #define StyleGravel 1
            #define StyleCobble 2
            #define StyleFlagstone 3
            #define StyleBrick 4
            #define StylePaved 5
            #define StyleBoardwalk 6
            #define StyleGlowLane 7

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                float4 road : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 uvFog : TEXCOORD2; // xy = along, across; z = fog
                float4 road : TEXCOORD3;
                half4 color : COLOR;
            };

            Varyings Vert(Attributes i)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(i.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.uvFog = float4(i.uv, ComputeFogFactor(p.positionCS.z), 0);
                o.road = i.road;
                o.color = i.color;
                return o;
            }

            float2 Hash2(float2 p)
            {
                p = float2(dot(p, float2(127.1, 311.7)), dot(p, float2(269.5, 183.3)));
                return frac(sin(p) * 43758.5453);
            }

            // Distance to the nearest cell edge of a jittered grid (F2 − F1) and the cell id: cobbles and gravel.
            float2 Cells(float2 p, out float2 id)
            {
                float2 g = floor(p);
                float f1 = 8.0, f2 = 8.0;
                id = g;
                [unroll] for (int y = -1; y <= 1; y++)
                [unroll] for (int x = -1; x <= 1; x++)
                {
                    float2 c = g + float2(x, y);
                    float2 q = c + 0.15 + 0.7 * Hash2(c);
                    float d = distance(p, q);
                    if (d < f1) { f2 = f1; f1 = d; id = c; }
                    else if (d < f2) { f2 = d; }
                }
                return float2(f1, f2 - f1);
            }

            // Running-bond rectangles: flagstones, bricks, boards. Returns edge distance (0 at a joint) and the block id.
            float Blocks(float2 p, float2 size, float shift, out float2 id)
            {
                float row = floor(p.y / size.y);
                p.x += row * shift * size.x;
                id = float2(floor(p.x / size.x), row);
                float2 f = frac(p / size) * size;
                float2 e = min(f, size - f);
                return min(e.x, e.y);
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float along = i.uvFog.x;
                float across = i.uvFog.y;
                int style = (int)round(i.road.x);
                float grade = i.road.y;
                float wear = i.road.z;
                float width = max(i.road.w, 1.0);
                float3 wp = i.positionWS;
                float ax = abs(across);

                // Ragged edge: the verge thins out in tufts, so the road never ends on a ruler line.
                float edgeNoise = CosmiNoise(float3(along * 0.9, across * 2.0, 3.1)) * 0.6 + CosmiNoise(float3(along * 3.7, 0.0, 7.7)) * 0.4;
                clip(1.0 + 0.35 * (edgeNoise - 0.35) - ax * (style == StyleBoardwalk || style == StyleGlowLane ? 0.86 : 1.0));

                // Metres across the paved width, for patterns that should not stretch with road width.
                float2 m = float2(along, across * width * 0.5);
                half3 stone = _Stone.rgb;
                half3 joint = _Joint.rgb;
                half3 albedo = stone;
                float paved = 1.0 - smoothstep(0.86, 1.0, ax + (edgeNoise - 0.5) * 0.2);

                if (style == StyleDirt)
                {
                    // Two wheel ruts and a grassy crown between them on long-used tracks.
                    float rut = exp(-pow((ax - 0.42) / 0.12, 2.0));
                    float crown = 1.0 - smoothstep(0.0, 0.18, ax);
                    float pebbles = step(0.93, Hash2(floor(m * 7.0)).x);
                    albedo = lerp(stone, joint, rut * 0.55 + pebbles * 0.3);
                    albedo = lerp(albedo, _Verge.rgb * 0.9, crown * 0.45 * (0.4 + wear));
                }
                else if (style == StyleGravel)
                {
                    float2 id;
                    float2 c = Cells(m * 5.0, id);
                    float tone = Hash2(id).x;
                    albedo = lerp(joint, stone * (0.82 + 0.3 * tone), smoothstep(0.02, 0.12, c.y));
                    float rut = exp(-pow((ax - 0.42) / 0.14, 2.0));
                    albedo *= 1.0 - rut * 0.18;
                }
                else if (style == StyleCobble)
                {
                    float2 id;
                    float2 c = Cells(m * float2(3.2, 3.6), id);
                    float tone = Hash2(id + 3.1).x;
                    // Domed setts: lit centre, dark rim, wide mortar joints that wear wider.
                    float joints = smoothstep(0.05 + wear * 0.06, 0.16 + wear * 0.08, c.y);
                    half3 sett = stone * (0.78 + 0.34 * tone) * (0.86 + 0.2 * saturate(1.0 - c.x * 1.4));
                    albedo = lerp(joint, sett, joints);
                    // Missing setts on worn streets.
                    albedo = lerp(albedo, joint * 1.1, step(1.0 - wear * 0.22, Hash2(id + 9.0).y));
                }
                else if (style == StyleFlagstone)
                {
                    float2 id;
                    float2 c = Cells(m * float2(0.9, 1.1), id);
                    float tone = Hash2(id).y;
                    float joints = smoothstep(0.02, 0.07, c.y);
                    albedo = lerp(joint, stone * (0.84 + 0.24 * tone), joints);
                    // Hairline cracks across old slabs.
                    float crack = 1.0 - smoothstep(0.0, 0.025, abs(CosmiNoise(float3(m * 1.7, tone * 9.0)) - 0.5));
                    albedo = lerp(albedo, joint, crack * wear * 0.8);
                }
                else if (style == StyleBrick)
                {
                    float2 id;
                    float e = Blocks(float2(m.y, m.x), float2(0.42, 0.2), 0.5, id);
                    float tone = Hash2(id).x;
                    albedo = lerp(joint, stone * (0.8 + 0.3 * tone), smoothstep(0.012, 0.03, e));
                    // Kerb stones along both edges.
                    float kerb = smoothstep(0.8, 0.84, ax) * (1.0 - smoothstep(0.96, 1.0, ax));
                    albedo = lerp(albedo, joint * 1.6 + 0.12, kerb);
                }
                else if (style == StylePaved)
                {
                    float grain = CosmiNoise(float3(m * 4.0, 1.0)) * 0.12;
                    albedo = stone * (0.92 + grain);
                    float dash = step(0.5, frac(along / 3.0)) * (1.0 - smoothstep(0.02, 0.05, ax)) * (1.0 - wear);
                    float edgeLine = smoothstep(0.84, 0.86, ax) * (1.0 - smoothstep(0.89, 0.91, ax)) * (1.0 - wear * 0.7);
                    albedo = lerp(albedo, half3(0.92, 0.86, 0.6), saturate(dash + edgeLine) * 0.85);
                    // Wasteland asphalt: crazed cracks and potholes.
                    float crack = 1.0 - smoothstep(0.0, 0.03, abs(CosmiNoise(float3(m * 0.9, 4.0)) - 0.5));
                    float hole = step(0.88, CosmiNoise(float3(m * 0.9, 8.0)));
                    albedo = lerp(albedo, joint * 0.8, saturate(crack * wear + hole * wear));
                }
                else if (style == StyleBoardwalk)
                {
                    float2 id;
                    float e = Blocks(float2(m.y + width, m.x), float2(width * 1.4, 0.32), 0.0, id);
                    float tone = Hash2(id).x;
                    albedo = lerp(joint * 0.6, stone * (0.78 + 0.3 * tone), smoothstep(0.015, 0.04, e));
                    // Wood grain along each board.
                    albedo *= 0.94 + 0.08 * sin(m.y * 40.0 + tone * 30.0 + CosmiNoise(float3(m * 3.0, 2.0)) * 6.0);
                    albedo = lerp(albedo, joint * 0.5, step(1.0 - wear * 0.15, Hash2(id + 4.0).y));
                }
                else
                {
                    float grain = CosmiNoise(float3(m * 2.0, 5.0)) * 0.06;
                    albedo = stone * (0.96 + grain);
                    float seams = 1.0 - smoothstep(0.0, 0.03, abs(frac(along / 4.0) - 0.5) - 0.47);
                    albedo = lerp(albedo, joint, seams * 0.6);
                }

                // Steps on steep stone paths: a dark riser every ~0.5 m of climb.
                bool stony = style == StyleCobble || style == StyleFlagstone || style == StyleBrick;
                if (stony && abs(grade) > 0.18)
                {
                    float stepLen = 0.5 / max(abs(grade), 0.2);
                    float riser = 1.0 - smoothstep(0.0, 0.06, frac(along / stepLen));
                    albedo = lerp(albedo, joint * 0.7, riser * 0.8);
                }

                // Grass and moss in the joints and along the verge: more on older roads.
                // Tufts are small (high-frequency mask) so a worn road reads as cracked, not camouflaged.
                float growth = saturate((1.0 - paved) * 0.9 + wear * 0.4 * step(0.78, CosmiNoise(float3(m * 7.0, 6.0))));
                albedo = lerp(albedo, _Verge.rgb * (0.85 + 0.2 * edgeNoise), growth);
                // Sand, snow or leaf litter carried over the road by the land it crosses.
                float drift = i.color.a * smoothstep(0.25, 0.75, CosmiNoise(float3(m * 0.8, 11.0)) + ax * 0.35);
                albedo = lerp(albedo, i.color.rgb, saturate(drift));

                float3 n = normalize(i.normalWS);
                Light light = GetMainLight(TransformWorldToShadowCoord(wp));
                half3 lit = CosmiToon(albedo, n, wp, light.color, light.direction, light.shadowAttenuation,
                    _Shadow.rgb, _Bands, SampleSH(n));
                lit += CosmiPointLights(albedo, n, wp, i.positionCS);
                if (style == StyleGlowLane)
                {
                    float lane = smoothstep(0.78, 0.8, ax) * (1.0 - smoothstep(0.84, 0.86, ax));
                    float pulse = 0.6 + 0.4 * sin(along * 0.6 - _Time.y * 2.0);
                    lit += _Glow.rgb * lane * pulse * (1.0 - wear * 0.8);
                }

                return half4(CosmiAtmosphere(lit, wp, i.uvFog.z, light.direction), 1);
            }
            ENDHLSL
        }
    }
}
