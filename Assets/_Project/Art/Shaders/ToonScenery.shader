// P1-07 / P3-09: cel-shaded scenery props (trees, rocks, crystals) built by SceneryMeshes.
// REQ: docs/design/vision.md#畫風與沉浸感 — 風場驅動樹; 描邊只用於角色與宙獸, so there is no outline pass here.
// Asset-free like the rest of the scenery: shape detail comes from the mesh, colour detail from vertex colours
// (rgb = baked tint: contact darkening, height gradient, facets; a = 1 − accent mask) and wind weights from UV1
// (x = sway weight, y = flutter phase in (0, 1], 0 = rigid wood). A mesh with neither channel (debris cubes that inherit this material) reads
// as colour 1, accent 0, weight 0: plain _BaseColor, no sway.
Shader "Cosmimera/ToonScenery"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        _Accent ("Accent Color (a = strength)", Color) = (1, 1, 1, 0)
        _Shadow ("Shadow Tint", Color) = (0.55, 0.6, 0.78, 1)
        _Bands ("Light Bands", Float) = 3
        _Sway ("Wind Sway (m per m of height)", Float) = 0
        _Translucency ("Backlight Through Leaves", Range(0, 1)) = 0
        _Glow ("Self Glow", Range(0, 2)) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        #include "CosmiAtmosphere.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half4 _Accent;
            half4 _Shadow;
            float _Bands;
            float _Sway;
            float _Translucency;
            float _Glow;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            half4 color : COLOR;
            float2 wind : TEXCOORD1;
        };

        // World position after wind. Shared by the lit and shadow passes so a swaying crown and its shadow move as one.
        float3 SceneryWorld(Attributes i)
        {
            float3 ws = TransformObjectToWorld(i.positionOS.xyz);
            float weight = i.wind.x * _Sway * _CosmiWind.x;
            if (weight <= 0.0)
            {
                return ws;
            }

            // The prop's own +Y is the planet's up where it stands (ScatterField), so no world axis is assumed.
            float3 axis = mul((float3x3)UNITY_MATRIX_M, float3(0, 1, 0));
            float scale = length(axis);
            float3 up = axis / max(scale, 1e-4);
            float3 root = TransformObjectToWorld(float3(0, 0, 0));
            float3 wind = CosmiWindDir(up);

            // The same gust field as ToonGrass: a front that flattens the meadow reaches the tree above it together.
            float along = dot(root, wind);
            float across = dot(root, cross(up, wind));
            float wave = max(_CosmiWind.z, 1.0);
            float gust = CosmiNoise(float3(along / wave - _Time.y * _CosmiWind.y, across / (wave * 1.7), 0.5));
            gust = smoothstep(0.35, 0.8, gust);
            float sway = sin(_Time.y * 1.3 + along * 0.21 + across * 0.13);
            float lean = (0.25 + sway * 0.35 + gust * 0.9) * weight * scale;

            // Leaves shiver faster than the crown leans; the per-lobe phase keeps neighbouring lobes out of step.
            // Phase 0 marks wood (trunk, limbs): it leans with the crown but must not swell along its normals.
            float flutter = sin(_Time.y * 4.3 + i.wind.y * 6.2832 + along * 1.7) * (0.25 + gust * 0.75) * weight * scale * 0.22
                * step(1e-4, i.wind.y);
            float3 n = TransformObjectToWorldNormal(i.normalOS);
            ws += wind * lean + n * flutter - up * (lean * lean / max(scale, 1e-4) * 0.5);
            return ws;
        }
        ENDHLSL

        Pass
        {
            Name "SceneryForward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float4 paint : TEXCOORD2; // xyz = object-space position in metres (pattern anchor), w = fog factor
                half4 color : COLOR;
            };

            Varyings Vert(Attributes i)
            {
                Varyings o;
                float3 ws = SceneryWorld(i);
                o.positionWS = ws;
                o.positionCS = TransformWorldToHClip(ws);
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                // Anchored to the object, not the world: brush patches must not swim across a swaying crown or a
                // tumbling boulder.
                float scale = length(mul((float3x3)UNITY_MATRIX_M, float3(0, 1, 0)));
                o.paint = float4(i.positionOS.xyz * scale + TransformObjectToWorld(float3(0, 0, 0)) * 0.37,
                    ComputeFogFactor(o.positionCS.z));
                o.color = i.color;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 view = normalize(_WorldSpaceCameraPos - i.positionWS);

                half3 albedo = _BaseColor.rgb;
                albedo = lerp(albedo, _Accent.rgb, saturate(1.0 - i.color.a) * _Accent.a);
                albedo *= i.color.rgb;
                // Two flat brush tones (the terrain's own painterly patches, CosmiBiome.hlsl) instead of a gradient.
                float patch = CosmiNoise(i.paint.xyz * 1.9) * 0.7 + CosmiNoise(i.paint.xyz * 6.1 + 5.0) * 0.3;
                albedo *= lerp(0.93, 1.06, smoothstep(0.42, 0.58, patch));

                Light light = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half3 lit = CosmiToon(albedo, n, i.positionWS, light.color, light.direction, light.shadowAttenuation,
                    _Shadow.rgb, _Bands, SampleSH(n));

                // Sun through the crown: leaves facing away from the light still glow when seen against it.
                float through = pow(saturate(dot(view, -light.direction)), 3.0) * saturate(0.6 - dot(n, light.direction));
                lit += albedo * light.color * half3(1.05, 1.1, 0.6) * (through * _Translucency * CosmiCloudLight(i.positionWS));

                float fresnel = 1.0 - saturate(dot(n, view));
                float rim = smoothstep(0.62, 0.78, fresnel) * saturate(dot(n, light.direction) + 0.3);
                lit += _CosmiSkyHorizon.rgb * (rim * 0.18 * _CosmiSkyHorizon.a);

                // Crystals carry their own light, so they stay readable at night and inside cloud shadow.
                lit += albedo * (_Glow * (0.3 + fresnel * fresnel * 0.9));

                return half4(CosmiAtmosphere(lit, i.positionWS, i.paint.w, light.direction), 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            // Set by URP's shadow pass; its stock ShadowCasterPass.hlsl declares the same two names.
            float3 _LightDirection;
            float3 _LightPosition;

            float4 Vert(Attributes i) : SV_POSITION
            {
                float3 ws = SceneryWorld(i);
                float3 n = TransformObjectToWorldNormal(i.normalOS);
            #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                float3 lightDir = normalize(_LightPosition - ws);
            #else
                float3 lightDir = _LightDirection;
            #endif
                float4 cs = TransformWorldToHClip(ApplyShadowBias(ws, n, lightDir));
            #if UNITY_REVERSED_Z
                cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
            #else
                cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                return cs;
            }

            half4 Frag() : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
