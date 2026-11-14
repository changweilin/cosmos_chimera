// P1-08: sky dome — zenith→horizon gradient, sun disc and halo, flat-shaded drifting clouds.
// Why not a skybox: Unity skyboxes assume world +Y is up; here "up" is the camera's radial direction on the planet
// (see AtmosphereDriver). The box is re-centred on the camera in the vertex shader, so it cannot lag a frame behind
// Cinemachine the way a transform-following shell does (StarField's FENCE).
Shader "Cosmimera/ToonSky"
{
    SubShader
    {
        Tags { "RenderType" = "Background" "RenderPipeline" = "UniversalPipeline" "Queue" = "Background" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Sky"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "CosmiAtmosphere.hlsl"

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            Varyings Vert(Attributes i)
            {
                Varyings o;
                o.dir = i.positionOS.xyz;
                // Half the far plane: inside the frustum from any view angle (box corners reach 0.87 × far).
                float3 ws = _WorldSpaceCameraPos + i.positionOS.xyz * (_ProjectionParams.z * 0.5);
                o.positionCS = TransformWorldToHClip(ws);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float3 dir = normalize(i.dir);
                float3 up = CosmiUp(_WorldSpaceCameraPos);
                float3 sun = GetMainLight().direction;
                float e = dot(dir, up); // sin(elevation of this pixel)

                half3 horizon = CosmiFogColor(dir, _CosmiSkyHorizon.rgb, sun);
                // Most of the gradient sits in the first ~25° above the horizon, as in a real (and a painted) sky.
                half3 col = lerp(horizon, _CosmiSkyZenith.rgb, pow(saturate(e), 0.5));

                float toSun = saturate(dot(dir, sun));
                col += _CosmiSunColor.rgb * (pow(toSun, 28.0) * 0.22 + pow(toSun, 220.0) * 0.5);
                col = lerp(col, _CosmiSunColor.rgb * 1.6 + 0.25, smoothstep(0.99955, 0.9997, toSun));

                if (e > 0.0 && _CosmiPlanet.w > 0.0)
                {
                    // Where this view ray meets the cloud deck (height `cell`·0.6 above the camera), as a surface
                    // direction — the same lookup CosmiCloudLight makes from the ground.
                    float deck = max(_CosmiCloud.w, 1.0) * 0.6;
                    float3 hit = _WorldSpaceCameraPos + dir * (deck / (e + 0.06));
                    float3 hitUp = CosmiUp(hit);
                    float d = CosmiCloudDensity(hitUp);
                    // Two flat tones: sunlit tops where the field thins toward the sun, shaded bases elsewhere.
                    float lit = smoothstep(0.0, 0.5, d - CosmiCloudDensity(normalize(hitUp + sun * 0.025)) + 0.12);
                    half3 cloud = lerp(_CosmiSkyHorizon.rgb * 0.7 + _CosmiSkyZenith.rgb * 0.22,
                        _CosmiSunColor.rgb * 0.9 + _CosmiSkyHorizon.rgb * 0.4, lit);
                    col = lerp(col, cloud, d * smoothstep(0.015, 0.12, e) * 0.95);
                }

                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
