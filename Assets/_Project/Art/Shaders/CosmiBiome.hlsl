// P3-04: the planet's ground colour as a function of position, shared by the terrain and by everything that must
// match it (grass takes its root colour from here, so a meadow and the ground under it are one colour field).
// Declares UnityPerMaterial itself: every shader including this file has the same material layout, which keeps them
// SRP-batcher compatible and lets PlanetLook fill terrain, grass and water materials with one routine.
#ifndef COSMI_BIOME_INCLUDED
#define COSMI_BIOME_INCLUDED

#include "CosmiAtmosphere.hlsl"

CBUFFER_START(UnityPerMaterial)
    float4 _PlanetCenter;
    float _Radius;
    float _Relief;
    float4 _PoleAxis;
    half4 _DeepWater, _ShallowWater, _Shore, _Low, _Mid, _High, _Rock, _Snow, _Shadow, _Patch;
    float _SnowLine;
    float _PolarLat;
    float _Bands;
    float _Foam;         // 0 = no surf line (lava, dry worlds)
    float _SeaLevel;     // altitude of the liquid surface (m, ≤ 0: the shore band above it stays dry sand)
    half4 _WaterTint;    // rgb = surface tint, a = opacity looking straight down
    float _WaterGlow;    // 0 = reflective water, 1 = self-lit (lava)
    float _GrassHeight;  // blade height multiplier
    float _GrassFade;    // distance (m) at which blades have shrunk to nothing
CBUFFER_END

struct CosmiGround
{
    float3 up;
    float height;   // altitude above the datum radius (m)
    float slope;    // 0 = flat, 1 = vertical
    float lat;      // |sin(latitude)|
};

CosmiGround CosmiGroundAt(float3 positionWS, float3 normalWS)
{
    CosmiGround g;
    float3 rel = positionWS - _PlanetCenter.xyz;
    float r = length(rel);
    g.up = rel / r;
    g.height = r - _Radius;
    g.slope = 1.0 - saturate(dot(normalWS, g.up));
    g.lat = abs(dot(g.up, normalize(_PoleAxis.xyz)));
    return g;
}

// Three noise scales: ~25 m meadow patches, ~3 m clumps, ~0.6 m brush marks. They jitter every biome boundary (no
// contour-line edges) and quantise into flat painterly patches, which is what reads as hand-painted rather than
// as a height-map gradient.
float3 CosmiGroundNoise(float3 positionWS)
{
    return float3(CosmiNoise(positionWS * 0.04), CosmiNoise(positionWS * 0.33 + 7.0), CosmiNoise(positionWS * 1.6 + 3.0));
}

// Surf line: 0..1 foam where the ground crosses the (slowly breathing) waterline.
float CosmiFoam(float3 positionWS, float height, float3 n)
{
    float tide = sin(_Time.y * 0.7 + n.x * 12.0) * 0.07;
    float d = height - (_SeaLevel + tide);
    float band = smoothstep(0.16, 0.04, abs(d - 0.03)) * step(0.35, n.z + n.y * 0.5);
    float lace = smoothstep(0.3, 0.0, abs(d + 0.22 + sin(_Time.y * 0.5 + n.y * 9.0) * 0.06)) * step(0.62, n.z);
    return saturate(band + lace * 0.6) * _Foam;
}

half3 CosmiBiomeColor(float3 positionWS, CosmiGround g, out float grass)
{
    float3 n = CosmiGroundNoise(positionWS);
    float t = saturate(g.height / max(_Relief, 1e-3) + (n.x - 0.5) * 0.14 + (n.y - 0.5) * 0.05);

    half3 c = lerp(_Low.rgb, _Mid.rgb, smoothstep(0.03, 0.38, t));
    c = lerp(c, _High.rgb, smoothstep(0.4, 0.95, t));
    // Flat patches of a warmer, lighter tone: sunlit meadow against deeper green.
    float patch = n.x * 0.6 + n.y * 0.3 + n.z * 0.1;
    float stepped = (floor(patch * 4.0) + smoothstep(0.35, 0.65, frac(patch * 4.0))) * 0.25;
    c = lerp(c * 0.84, c * _Patch.rgb, stepped);
    grass = 1.0;

    float sand = 1.0 - smoothstep(0.45, 1.5, g.height + (n.y - 0.5) * 0.9 + (n.z - 0.5) * 0.3);
    half3 shore = _Shore.rgb * lerp(0.86, 1.04, n.z);
    shore *= lerp(0.7, 1.0, smoothstep(_SeaLevel - 0.05, _SeaLevel + 0.45, g.height)); // wet sand by the water
    c = lerp(c, shore, sand);
    grass *= 1.0 - sand;

    float rock = smoothstep(0.2, 0.36, g.slope + (n.y - 0.5) * 0.12);
    // Strata: thin lighter/darker beds that follow altitude, bent by the large noise so they never look ruled.
    float bed = frac(g.height * 0.3 + n.x * 3.0);
    half3 rockColor = _Rock.rgb * lerp(0.84, 1.12, smoothstep(0.42, 0.58, bed)) * lerp(0.94, 1.05, n.z);
    c = lerp(c, rockColor, rock);
    grass *= 1.0 - rock;

    float snowAlt = smoothstep(_SnowLine - 0.04, _SnowLine + 0.04, t + (n.y - 0.5) * 0.08);
    float snowPole = smoothstep(_PolarLat - 0.02, _PolarLat + 0.02, g.lat + (n.x - 0.5) * 0.04);
    float snow = max(snowAlt, snowPole) * (1.0 - smoothstep(0.32, 0.55, g.slope));
    c = lerp(c, _Snow.rgb * lerp(0.95, 1.0, n.z), snow);
    grass *= 1.0 - snow;

    if (g.height < _SeaLevel + 0.1)
    {
        float depth = saturate((_SeaLevel - g.height) / max(_Relief * 0.15, 0.5));
        half3 bed2 = lerp(_ShallowWater.rgb, _DeepWater.rgb, depth);
        // Caustic-like light net on the shallows only; it fades with depth as it would under real water.
        float net = CosmiNoise(positionWS * 0.9 + float3(0.0, _Time.y * 0.35, 0.0));
        bed2 += smoothstep(0.52, 0.6, net) * (1.0 - depth) * 0.1 * (1.0 - _WaterGlow);
        c = lerp(c, bed2, smoothstep(_SeaLevel + 0.1, _SeaLevel - 0.5, g.height));
        grass = 0.0;
    }

    c = lerp(c, half3(1, 1, 1), CosmiFoam(positionWS, g.height, n));
    return c;
}

#endif
