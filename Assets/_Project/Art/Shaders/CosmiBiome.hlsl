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
#define COSMI_GEO_MAX 24
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
    int count = min((int)_CosmiGeoCount, COSMI_GEO_MAX);
    for (int i = 0; i < count; i++)
    {
        float3 centre = _CosmiGeoCenter[i].xyz;
        float c = dot(up, centre);
        if (c <= 0.5)
        {
            continue;
        }
        float3 p = up / c;
        float m = max(abs(dot(p, _CosmiGeoRight[i].xyz)), abs(dot(p, _CosmiGeoUp[i].xyz))) * _CosmiGeoCenter[i].w;
        float w = 1.0 - smoothstep(0.65, 1.0, m);
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
    // REQ: planet-world.md#海浪 岸邊碎浪 — each arriving swell runs up the slope and drains back: the waterline
    // climbs sharply with the crest and sinks slowly through the trough, by up to 70 % of the wave height.
    float swash = pow(saturate(CosmiSwell(positionWS) * 0.5 + 0.5), 1.6) * _CosmiSea.z * 0.7;
    float d = height - (_SeaLevel + tide + swash);
    float band = smoothstep(0.16, 0.04, abs(d - 0.03)) * step(0.35, n.z + n.y * 0.5);
    float lace = smoothstep(0.3, 0.0, abs(d + 0.22 + sin(_Time.y * 0.5 + n.y * 9.0) * 0.06)) * step(0.62, n.z);
    return saturate(band + lace * 0.6) * _Foam;
}

// P1-12 flower meadows (vision.md#美術投資 第 1 項 花海). Globals set by GrassField: x = meadow strength 0..1 for this
// planet and season (0 = none); four bloom colours. CosmiMeadow mirrors GrassField.MeadowAt, so the flowers it plants
// stand on the patches this tints.
float4 _CosmiMeadow;
float4 _CosmiBloom[4];

float CosmiMeadow(float3 up)
{
    if (_CosmiMeadow.x <= 0.0)
    {
        return 0.0;
    }

    float3 plan = _PlanetCenter.xyz + up * _Radius;
    float n = CosmiNoise(plan * 0.012) * 0.75 + CosmiNoise(plan * 0.07 + 31.0) * 0.25;
    return smoothstep(0.5, 0.62, n) * _CosmiMeadow.x;
}

// Drifts of one colour with a few of the next mixed in, as real meadows grow: `jitter` 0..1 per flower.
half3 CosmiBloomColor(float3 up, float jitter)
{
    float3 plan = _PlanetCenter.xyz + up * _Radius;
    float pick = saturate(CosmiNoise(plan * 0.05 + 57.0) + (jitter - 0.5) * 0.35);
    return _CosmiBloom[(int)min(floor(pick * 4.0), 3.0)].rgb;
}

// Terrain only: beyond the 30 m grass ring the flowers are gone and this tint is the meadow; up close it is a faint
// dappled wash under the drawn blossoms. Not applied to grass blades, which keep the plain turf colour.
half3 CosmiMeadowTint(half3 c, float3 positionWS, CosmiGround g, float turf)
{
    float m = CosmiMeadow(g.up) * turf;
    if (m <= 0.001)
    {
        return c;
    }

    float far = smoothstep(8.0, 32.0, distance(positionWS, _WorldSpaceCameraPos));
    float speck = CosmiNoise(positionWS * 1.9);
    half3 bloom = CosmiBloomColor(g.up, speck);
    return lerp(c, bloom, m * lerp(0.12, 0.32, far) * smoothstep(0.45, 0.65, speck + 0.2));
}

// P1-18 cliff faces: the terrain mesh is a smooth ramp at ~1 m spacing, so faces get their planes from the
// lighting normal. Stepped noise keyed to the map position turns each patch of a face a fixed amount about local up
// (and tips some toward the sky), so a cliff shades as a few flat, hard-edged planes. Colour still uses the true
// normal; only lighting sees this one. Fades out with distance before the facets alias.
float3 CosmiCliffNormal(float3 positionWS, float3 n, float3 up)
{
    float w = smoothstep(0.22, 0.4, 1.0 - saturate(dot(n, up)));
    w *= 1.0 - smoothstep(60.0, 140.0, distance(positionWS, _WorldSpaceCameraPos));
    if (w <= 0.0)
    {
        return n;
    }

    float3 plan = _PlanetCenter.xyz + up * _Radius;
    float turn = (floor(CosmiNoise(plan * 0.38) * 6.0) / 6.0 - 0.5) * 1.1;
    float tilt = (floor(CosmiNoise(positionWS * 0.21 + 21.0) * 4.0) / 4.0 - 0.4) * 0.5;
    float s = sin(turn), c = cos(turn);
    float3 r = n * c + cross(up, n) * s + up * dot(up, n) * (1.0 - c);
    r = normalize(r + up * tilt);
    return normalize(lerp(n, r, w));
}

// Cellular pebbles: distance to the nearest stone's edge (0 in the gaps between stones) and that stone's id 0..1.
float CosmiPebbles(float3 p, out float id)
{
    float3 cell = floor(p);
    float3 f = frac(p);
    float d1 = 8.0, d2 = 8.0;
    id = 0.0;
    for (int z = -1; z <= 1; z++)
    for (int y = -1; y <= 1; y++)
    for (int x = -1; x <= 1; x++)
    {
        float3 o = float3(x, y, z);
        float3 c = cell + o;
        float3 h = float3(CosmiHash(c), CosmiHash(c + 17.0), CosmiHash(c + 43.0));
        float3 r = o + h - f;
        float d = dot(r, r);
        if (d < d1)
        {
            d2 = d1;
            d1 = d;
            id = h.x;
        }
        else if (d < d2)
        {
            d2 = d;
        }
    }

    return sqrt(d2) - sqrt(d1);
}

// P1-18 riverbed (planet-world.md#河床): paints the channel the landform stamps carve. `river` is the stamp river
// mask (TemplateView.River): 1 on the centre line, 0 one grid cell out. From the bank inward: damp, darker soil;
// a bed of rounded pebbles with pale sand bars where the low noise swells; and a thin sheet of moving water along
// the centre. Painted, not simulated: the channel holds no water volume, so walking it is unchanged.
half3 CosmiRiverbed(half3 c, float3 positionWS, CosmiGround g, float river)
{
    // Where the river meets the sea, the sea's own colouring takes over.
    river *= smoothstep(_SeaLevel - 0.2, _SeaLevel + 0.3, g.height);
    if (river <= 0.002)
    {
        return c;
    }

    float3 n = CosmiGroundNoise(positionWS);
    float3 fine = CosmiStroke(positionWS, g, _StrokeScale);
    // Jittered so the stream, the sand and the wet line wander instead of following the mask's grid.
    float r = saturate(river + (n.y - 0.5) * 0.3 + fine.r * 0.15);

    float wet = smoothstep(0.04, 0.3, r);
    c = lerp(c, c * half3(0.6, 0.62, 0.66), wet);

    // Two stone sizes: large cobbles with gravel between them, so the bed does not read as crazy paving.
    float id, idSmall;
    float edge = CosmiPebbles(positionWS * 2.0, id);
    float edgeSmall = CosmiPebbles(positionWS * 5.3 + 11.0, idSmall);
    bool gravel = edge < 0.1;
    float e = gravel ? edgeSmall : edge;
    float stone = gravel ? idSmall : id;
    half3 pebble = lerp(_Rock.rgb, _Shore.rgb, stone * 0.7) * lerp(0.78, 1.12, frac(stone * 7.31));
    // Domed: each stone darkens toward its rim and its crown catches the light.
    pebble *= lerp(0.45, 1.12, smoothstep(0.0, gravel ? 0.25 : 0.4, e));
    half3 sand = _Shore.rgb * (1.0 + CosmiDab(fine.g) * 0.08 * _StrokeStrength) * 0.92;
    float bar = smoothstep(0.52, 0.66, n.x * 0.65 + CosmiNoise(positionWS * 0.11 + 5.0) * 0.35);
    half3 bed = lerp(pebble, sand, bar);
    float onBed = smoothstep(0.28, 0.42, r);
    c = lerp(c, bed * lerp(1.0, 0.72, smoothstep(0.5, 0.75, r)), onBed); // wet stones darken toward the water

    float water = smoothstep(0.7, 0.82, r) * (1.0 - bar * 0.7);
    float ripple = CosmiNoise(positionWS * 1.7 + float3(_Time.y * 0.9, _Time.y * 0.4, -_Time.y * 0.7));
    half3 stream = lerp(bed * _ShallowWater.rgb * 1.5, _ShallowWater.rgb, 0.5);
    stream += smoothstep(0.66, 0.74, ripple) * 0.18 * (1.0 - _WaterGlow);
    return lerp(c, stream, water);
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
    float crisp = 1.0 - smoothstep(0.03, 0.35, fwidth(shelfPhase));
    float shelf = smoothstep(0.70, 0.91, frac(shelfPhase)) * crisp;
    rockColor *= 1.0 - shelf * 0.13;

    // P1-18 cliff faces (planet-world.md#懸崖): patterns keyed to the distance along the face (its horizontal
    // tangent), so they run down the face whatever its tilt: broken joints that split it into columns, and rain
    // stains hanging below each ledge. Ledge lips catch the light and, on living worlds, carry tufts of moss;
    // lichen crusts in patches.
    float3 strike = cross(g.up, g.normal);
    float along = dot(positionWS, strike * rsqrt(max(dot(strike, strike), 1e-6)));
    float jointPhase = along * 0.33 + CosmiNoise(positionWS * 0.15) * 0.8;
    float jointGap = abs(frac(jointPhase + 0.5) - 0.5);
    float joint = (1.0 - smoothstep(0.03, 0.055 + fwidth(jointPhase), jointGap)) * step(0.38, CosmiNoise(positionWS * 0.3 + 5.0)) * crisp;
    rockColor *= 1.0 - joint * 0.5;
    float below = smoothstep(0.05, 0.7, frac(shelfPhase));
    float stain = smoothstep(0.56, 0.76, CosmiNoise(float3(along * 1.1, g.height * 0.06, 7.0))) * below * crisp;
    rockColor *= 1.0 - stain * 0.3;
    float f = frac(shelfPhase);
    float lip = smoothstep(0.88, 0.92, f) * (1.0 - smoothstep(0.96, 0.995, f)) * crisp;
    lip *= smoothstep(0.42, 0.58, CosmiNoise(positionWS * 0.8 + 13.0));
    float living = (1.0 - _WaterGlow) * (1.0 - _FrozenGround);
    rockColor = lerp(rockColor * (1.0 + lip * 0.18), _Low.rgb * 0.9, lip * 0.45 * living);
    float lichen = smoothstep(0.62, 0.72, CosmiNoise(positionWS * 0.7 + 9.0)) * living;
    rockColor = lerp(rockColor, rockColor * half3(1.12, 1.1, 0.72) + 0.02, lichen * 0.35);

    // Scree: the band between turf and face is the rubble the cliff has shed, not bare soil.
    float scree = smoothstep(0.13, 0.22, g.slope + (n.z - 0.5) * 0.1) * (1.0 - rock) * (1.0 - sand);
    float near = 1.0 - smoothstep(40.0, 90.0, distance(positionWS, _WorldSpaceCameraPos));
    if (scree * near > 0.01)
    {
        float sid;
        float se = CosmiPebbles(positionWS * 3.5, sid);
        half3 rubble = bedrock * lerp(0.75, 1.1, sid) * lerp(0.55, 1.0, smoothstep(0.02, 0.15, se));
        c = lerp(c, rubble, scree * near * 0.8);
    }

    grass *= 1.0 - scree;
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
