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
    float _FrozenGround;
    float _SnowLine;
    float _PolarLat;
    float _Bands;
    float _Foam;         // 0 = no surf line (lava, dry worlds)
    float _SeaLevel;     // altitude of the liquid surface (m, ≤ 0: the shore band above it stays dry sand)
    half4 _WaterTint;    // rgb = surface tint, a = opacity looking straight down
    float _WaterGlow;    // 0 = reflective water, 1 = self-lit (lava)
    float _GrassHeight;  // blade height multiplier
    float _GrassFade;    // distance (m) at which blades have shrunk to nothing
    float _StrokeStrength; // 0 = no brush texture (unset materials, tests): every stroke term below scales with it
    float _StrokeScale;    // tiles per metre of the fine stroke layer
CBUFFER_END

// Brush-stroke texture generated in code (Bootstrap/Demo/StrokeTexture; vision.md#美術投資: 允許程式生成的筆觸貼圖).
// r = soft round dabs, g = long streaks, b = fine grain; each channel averages 0.5.
TEXTURE2D(_StrokeTex);
SAMPLER(sampler_StrokeTex);

// P3-24(a) bedrock: each landform stamp (and the ground between stamps) may override the rock colour. Globals, not
// material properties: one planet is live at a time and terrain, grass and props must agree (PlanetLook.SetGeology).
// Unset (count 0, base alpha 0) leaves every material's own _Rock.
#define COSMI_GEO_MAX 8
float4 _CosmiGeoCenter[COSMI_GEO_MAX]; // xyz = stamp centre direction, w = 1 / tan(half angle)
float4 _CosmiGeoRight[COSMI_GEO_MAX];  // stamp +x tangent
float4 _CosmiGeoUp[COSMI_GEO_MAX];     // stamp +y tangent
half4 _CosmiGeoRock[COSMI_GEO_MAX];    // rgb = rock colour, a = 1 when the stamp's bedrock overrides _Rock
half4 _CosmiGeoBase;                   // the same for the ground between stamps
// P3-28 cold landforms: x = snow line (normalised altitude, like _SnowLine) inside the stamp; 0 = the planet's own.
float4 _CosmiGeoSnow[COSMI_GEO_MAX];
float _CosmiGeoCount;

// Rock colour at a point. Same gnomonic square and 65 % blend start as Planet.TemplateView.TryGetStamp, so the colour
// changes where the scatter's rock species do; `jitter` (0..1 noise) breaks the edge into patches the way the
// planner's per-cell roll interleaves species. snowLine is the strongest stamp's own snow line, eased in over the
// blend band with the unjittered weight: snow creeps down toward a glacier, it does not start at a patchy edge.
half3 CosmiBedrock(float3 up, float jitter, out float snowLine)
{
    half4 pick = _CosmiGeoBase;
    float best = 0.0;
    snowLine = _SnowLine;
    for (int i = 0; i < COSMI_GEO_MAX; i++)
    {
        float3 centre = _CosmiGeoCenter[i].xyz;
        float c = dot(up, centre);
        float3 p = up / max(c, 0.5);
        float m = max(abs(dot(p, _CosmiGeoRight[i].xyz)), abs(dot(p, _CosmiGeoUp[i].xyz))) * _CosmiGeoCenter[i].w;
        float w = (1.0 - smoothstep(0.65, 1.0, m)) * step(0.5, c) * step(i + 0.5, _CosmiGeoCount);
        if (w > best)
        {
            best = w;
            pick = lerp(_CosmiGeoBase, _CosmiGeoRock[i], smoothstep(jitter - 0.06, jitter + 0.06, w));
            float own = _CosmiGeoSnow[i].x;
            snowLine = lerp(_SnowLine, own > 0.0 ? own : _SnowLine, w);
        }
    }

    return lerp(_Rock.rgb, pick.rgb, pick.a);
}

struct CosmiGround
{
    float3 up;
    float height;   // altitude above the datum radius (m)
    float slope;    // 0 = flat, 1 = vertical
    float lat;      // |sin(latitude)|
    float3 normal;  // surface normal the ground was measured with
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
    g.normal = normalWS;
    return g;
}

// Three noise scales: ~25 m meadow patches, ~3 m clumps, ~0.6 m brush marks. They jitter every biome boundary (no
// contour-line edges) and quantise into flat painterly patches, which is what reads as hand-painted rather than
// as a height-map gradient.
float3 CosmiGroundNoise(float3 positionWS)
{
    return float3(CosmiNoise(positionWS * 0.04), CosmiNoise(positionWS * 0.33 + 7.0), CosmiNoise(positionWS * 1.6 + 3.0));
}

// Triplanar lookup of the stroke texture, as an offset from its 0.5 mean scaled by _StrokeStrength. Triplanar over
// world axes because a sphere has no seam-free 2D parameterisation. Projection weights follow local up on gentle
// ground and the real normal only on steep faces: grass shades with normal == up, so meadow and the ground under
// it sample the same strokes, while cliffs do not get a stretched top-down projection.
float3 CosmiStroke(float3 positionWS, CosmiGround g, float tilesPerMetre)
{
    float3 axis = normalize(lerp(g.up, g.normal, smoothstep(0.1, 0.3, g.slope)));
    float3 w = axis * axis;
    w *= w;
    w /= (w.x + w.y + w.z);
    float3 p = positionWS * tilesPerMetre;
    float3 s = SAMPLE_TEXTURE2D(_StrokeTex, sampler_StrokeTex, p.zy).rgb * w.x
             + SAMPLE_TEXTURE2D(_StrokeTex, sampler_StrokeTex, p.xz).rgb * w.y
             + SAMPLE_TEXTURE2D(_StrokeTex, sampler_StrokeTex, p.xy).rgb * w.z;
    return (s - 0.5) * _StrokeStrength;
}

// Two flat tones split along the stroke's edge: reads as overlapping brush marks instead of a blurred gradient.
float CosmiDab(float v)
{
    return smoothstep(-0.03, 0.03, v) - 0.5;
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
    // Fine marks (~0.5 m dabs) carry the close-up; broad ones (~2 m) stay readable at mid distance.
    float3 fine = CosmiStroke(positionWS, g, _StrokeScale);
    float3 broad = CosmiStroke(positionWS + 37.0, g, _StrokeScale * 0.27);
    float t = saturate(g.height / max(_Relief, 1e-3) + (n.x - 0.5) * 0.14 + (n.y - 0.5) * 0.05 + broad.r * 0.06);

    half3 c = lerp(_Low.rgb, _Mid.rgb, smoothstep(0.03, 0.38, t));
    c = lerp(c, _High.rgb, smoothstep(0.4, 0.95, t));
    // Flat patches of a warmer, lighter tone: sunlit meadow against deeper green.
    float patch = n.x * 0.6 + n.y * 0.3 + n.z * 0.1;
    float stepped = (floor(patch * 4.0) + smoothstep(0.35, 0.65, frac(patch * 4.0))) * 0.25;
    c = lerp(c * 0.84, c * _Patch.rgb, stepped);
    // Meadow: broad light/dark dabs under short fine ones, both two-tone.
    c *= 1.0 + CosmiDab(broad.r) * 0.12 * _StrokeStrength + CosmiDab(fine.r + broad.r * 0.5) * 0.08 * _StrokeStrength;
    grass = 1.0;

    float sand = 1.0 - smoothstep(0.45, 1.5, g.height + (n.y - 0.5) * 0.9 + (n.z - 0.5) * 0.3 + fine.r * 0.4);
    half3 shore = _Shore.rgb * lerp(0.86, 1.04, n.z);
    // Sand: thin wind streaks and grain, much lower contrast than grass so beaches stay bright and calm.
    shore *= 1.0 + CosmiDab(fine.g) * 0.07 * _StrokeStrength + fine.b * 0.08;
    shore *= lerp(0.7, 1.0, smoothstep(_SeaLevel - 0.05, _SeaLevel + 0.45, g.height)); // wet sand by the water
    c = lerp(c, shore, sand);
    grass *= 1.0 - sand;

    // Bare earth where the slope is too steep for turf but not yet a rock face: without it grass butts straight
    // against cliff grey. The stroke offset makes the turf edge ragged, like grass tufts overhanging soil.
    float snowLine;
    half3 bedrock = CosmiBedrock(g.up, n.x * 0.7 + n.y * 0.3, snowLine);
    float earth = smoothstep(0.11, 0.19, g.slope + (n.y - 0.5) * 0.08 + fine.r * 0.10) * (1.0 - sand);
    half3 earthColor = lerp(_High.rgb, bedrock, 0.35) * (1.0 + CosmiDab(fine.g + broad.r) * 0.12 * _StrokeStrength);
    c = lerp(c, earthColor, earth * 0.85);
    grass *= 1.0 - earth;

    float rock = smoothstep(0.2, 0.36, g.slope + (n.y - 0.5) * 0.12 + broad.g * 0.10);
    // Strata: thin lighter/darker beds that follow altitude, bent by the large noise so they never look ruled.
    float bed = frac(g.height * 0.3 + n.x * 3.0);
    half3 rockColor = bedrock * lerp(0.84, 1.12, smoothstep(0.42, 0.58, bed)) * lerp(0.94, 1.05, n.z);
    // Rock: hard-edged streaks (chisel marks) over the strata, plus sparse dark cracks from the grain channel.
    rockColor *= 1.0 + CosmiDab(broad.g) * 0.20 * _StrokeStrength + CosmiDab(fine.g) * 0.10 * _StrokeStrength;
    rockColor *= 1.0 - smoothstep(0.28, 0.4, fine.b) * 0.25;
    // Broken ledges cast narrow bands over their foot; fine strata disappear before they alias at distance.
    float shelfPhase = g.height * 1.1 + n.y * 0.49 + n.x * 2.1;
    float shelf = smoothstep(0.70, 0.91, frac(shelfPhase)) * (1.0 - smoothstep(0.03, 0.35, fwidth(shelfPhase)));
    rockColor *= 1.0 - shelf * 0.13;
    c = lerp(c, rockColor, rock);
    grass *= 1.0 - rock;

    float snowAlt = smoothstep(snowLine - 0.04, snowLine + 0.04, t + (n.y - 0.5) * 0.08 + fine.r * 0.05);
    float snowPole = smoothstep(_PolarLat - 0.02, _PolarLat + 0.02, g.lat + (n.x - 0.5) * 0.04);
    float snow = max(snowAlt, snowPole) * (1.0 - smoothstep(0.32, 0.55, g.slope));
    // Snow: only broad, faint, cool dabs — drifts, not texture.
    half3 snowColor = _Snow.rgb * lerp(0.95, 1.0, n.z) * (1.0 + CosmiDab(broad.r) * half3(0.07, 0.05, 0.02) * _StrokeStrength);
    c = lerp(c, snowColor, snow);
    grass *= 1.0 - snow;

    if (_FrozenGround > 0.5)
    {
        float exposure = smoothstep(0.04, 0.31, g.slope + (n.x - 0.48) * 1.1 + broad.g * 0.20);
        float seam = 1.0 - smoothstep(0.015, 0.065, abs(n.y + n.x * 0.22 - 0.56));
        half3 ice = lerp(_ShallowWater.rgb * 0.76, _Mid.rgb, n.x * 0.65 + seam * 0.3);
        float grazing = 1.0 - saturate(dot(g.normal, GetWorldSpaceNormalizeViewDir(positionWS)));
        ice = lerp(ice, _Snow.rgb, grazing * grazing * 0.25);
        c = lerp(snowColor, ice, exposure);
        grass = 0.0;
    }

    if (_WaterGlow > 0.5 && g.height > _SeaLevel)
    {
        // Pale deposits pick out the edge of cooling basalt, with a narrow iron-red seam inside it.
        float crust = n.y * 0.70 + n.x * 0.30 + fine.g * 0.035;
        float fissure = abs(crust - 0.46);
        float hotPatch = smoothstep(0.45, 0.64, n.x) * (1.0 - rock);
        float rim = (1.0 - smoothstep(0.012, 0.043, fissure)) * hotPatch;
        float core = (1.0 - smoothstep(0.002, 0.014 + fwidth(crust), fissure)) * hotPatch;
        c = lerp(c, _Snow.rgb * 0.7, rim * 0.45);
        c = lerp(c, _ShallowWater.rgb, core * 0.68);
    }

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
