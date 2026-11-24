// Painted anime iris for NPC UMA eyes, drawn from parameters instead of a texture so every resident's eye type, size
// and colours vary without baking an atlas (docs/design/npc-uma-looks.md#眼睛). UV is the iris disc: centre (0.5, 0.5),
// rim at distance 0.5 (forge acg_face.drawn_eyes).
Shader "Cosmimera/NpcEye"
{
    Properties
    {
        _BaseColor ("Iris", Color) = (0.24, 0.16, 0.1, 1)
        _IrisRight ("Iris of the eye at positive x (odd eyes)", Color) = (0.24, 0.16, 0.1, 1)
        _PupilColor ("Pupil", Color) = (0.08, 0.05, 0.05, 1)
        _ScleraColor ("Sclera", Color) = (0.96, 0.95, 0.94, 1)
        _PupilType ("Pupil type (0 round, 1 slit, 2 none, 3 bead, 4 ring)", Float) = 0
        _IrisScale ("Iris scale", Range(0.5, 1)) = 0.9
        _PupilScale ("Pupil scale", Range(0.3, 1.5)) = 1
        _Glow ("Glow", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" "RenderType"="Opaque" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor, _IrisRight, _PupilColor, _ScleraColor;
            half _PupilType, _IrisScale, _PupilScale, _Glow;
            CBUFFER_END

            struct Attributes { float4 position : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 position : SV_POSITION; float2 uv : TEXCOORD0; float side : TEXCOORD1; };

            Varyings Vert(Attributes i)
            {
                Varyings o;
                o.position = TransformObjectToHClip(i.position.xyz);
                o.uv = i.uv * 2.0 - 1.0;
                // Both irises share one sub-mesh, so the side of the body picks the colour.
                o.side = i.position.x;
                return o;
            }

            // 1 inside, 0 outside, with a one-texel-wide edge at any distance.
            half Inside(half d)
            {
                half w = max(fwidth(d), 1e-4);
                return saturate(0.5 - d / w);
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float2 p = i.uv / _IrisScale;
                half r = length(p);
                int type = (int)round(_PupilType);
                half3 iris = i.side > 0 ? _IrisRight.rgb : _BaseColor.rgb;
                // Toon iris: the lid shades the top, light pools in the lower half, a dark limbal ring holds the edge.
                half3 col = lerp(iris * 1.25 + 0.05, iris * 0.5, saturate(p.y * 0.6 + 0.4));
                col = lerp(col, iris * 0.35, smoothstep(0.78, 0.95, r));
                half ps = _PupilScale;
                half pupil = 0;
                if (type == 0)
                    pupil = Inside(length(p / (half2(0.42, 0.5) * ps)) - 1);
                else if (type == 1)
                    pupil = Inside(length(p / (half2(0.1, 0.82) * ps)) - 1);
                else if (type == 3)
                {
                    // A bead eye is nearly all pupil; the iris shows only as a thin rim.
                    pupil = Inside(r - 0.8 * ps);
                }
                else if (type == 4)
                {
                    half ring = abs(r - 0.42 * ps) - 0.09 * ps;
                    pupil = Inside(ring);
                    col = lerp(col, iris * 1.4 + 0.15, Inside(r - 0.3 * ps));
                }
                else
                {
                    // No pupil: a light that brightens toward its core.
                    col = lerp(col, iris * 1.6 + 0.25, 1 - smoothstep(0.0, 0.7, r));
                }
                col = lerp(col, _PupilColor.rgb, pupil);
                half shine = type == 2 ? 0.35 : 1;
                half glints = max(Inside(length(p - half2(-0.34, 0.38)) - 0.26), Inside(length(p - half2(0.3, -0.44)) - 0.11));
                col = lerp(col, 1, glints * shine);
                col *= 1 + 1.5 * _Glow;
                col = lerp(_ScleraColor.rgb, col, Inside(r - 1));
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
