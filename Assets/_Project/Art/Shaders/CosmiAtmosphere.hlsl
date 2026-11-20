// P1-07 / P1-08: lighting, cloud and aerial-perspective helpers shared by every scenery shader (terrain, grass, water,
// sky, toon props), so the ground, what grows on it and the sky above it are shaded by one model and never drift apart.
// REQ: docs/design/vision.md#畫風與沉浸感 (柔和明暗、大氣散射遠景、風場). Asset-free on purpose: all patterns are hashed
// from world position, so a planet needs no texture set and looks the same on every client.
#ifndef COSMI_ATMOSPHERE_INCLUDED
#define COSMI_ATMOSPHERE_INCLUDED

// Written every frame by AtmosphereDriver. All default to 0 when nothing drives them (sandbox scenes, tests), and
// every use below is scaled by one of them, so an undriven scene falls back to plain cel shading with stock fog.
float4 _CosmiPlanet;      // xyz = planet centre, w = radius (0 = no planet: cloud and wind patterns off)
float4 _CosmiPoleAxis;    // xyz = spin axis (wind blows east around it)
float4 _CosmiSkyHorizon;  // rgb = sky colour at the horizon (== fog colour), a = haze strength
float4 _CosmiSkyZenith;   // rgb = sky colour overhead
float4 _CosmiSunColor;    // rgb = sun tint, a = daylight 0..1
float4 _CosmiCloud;       // x = coverage 0..1, y = shadow strength, z = drift speed, w = cloud cell size (m)
float4 _CosmiWind;        // x = strength, y = gust speed, z = gust wavelength (m), w = heading (rad from local east toward north)
float4 _CosmiPlayer;      // xyz = player feet, w = push radius (m); grass leans away inside it
float4 _CosmiFarFocus;    // xyz = centre of the near scatter field (FarScenery): far silhouettes shrink away inside it

float CosmiHash(float3 p)
{
    p = frac(p * 0.1031);
    p += dot(p, p.zyx + 31.32);
    return frac((p.x + p.y) * p.z);
}

// Trilinear value noise in [0, 1]. 3D rather than 2D + triplanar: a sphere has no seam-free 2D parameterisation.
float CosmiNoise(float3 p)
{
    float3 i = floor(p);
    float3 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = lerp(lerp(CosmiHash(i), CosmiHash(i + float3(1, 0, 0)), f.x),
                   lerp(CosmiHash(i + float3(0, 1, 0)), CosmiHash(i + float3(1, 1, 0)), f.x), f.y);
    float b = lerp(lerp(CosmiHash(i + float3(0, 0, 1)), CosmiHash(i + float3(1, 0, 1)), f.x),
                   lerp(CosmiHash(i + float3(0, 1, 1)), CosmiHash(i + float3(1, 1, 1)), f.x), f.y);
    return lerp(a, b, f.z);
}

float CosmiFbm2(float3 p)
{
    return CosmiNoise(p) * 0.65 + CosmiNoise(p * 2.13 + 17.0) * 0.35;
}

// Local up at a world position. Never world +Y: "up" is the planet's radial direction (CLAUDE.md#硬性規則).
float3 CosmiUp(float3 positionWS)
{
    return normalize(positionWS - _CosmiPlanet.xyz);
}

// Unit tangent the wind blows along at a point whose up is given: the rule-layer heading (weather.md#風向) measured
// from local east (PlanetFrame.East = up × pole) toward north, so it matches WorldGen.WindField.Tangent. Degenerate
// within a few metres of the poles, where the fallback axis keeps it finite (the blades there just sway another way).
float3 CosmiWindDir(float3 up)
{
    float3 e = cross(up, _CosmiPoleAxis.xyz);
    float l = dot(e, e);
    e = l > 1e-4 ? e * rsqrt(l) : normalize(cross(up, float3(1, 0, 0)));
    float3 n = cross(e, up);
    return e * cos(_CosmiWind.w) + n * sin(_CosmiWind.w);
}

// Cloud density 0..1 above the surface point in direction `up`. One field feeds both the sky's clouds and the
// shadows they drop, so a shadow on the ground always has a cloud above it.
float CosmiCloudDensity(float3 up)
{
    float cell = max(_CosmiCloud.w, 1.0);
    float3 p = up * (_CosmiPlanet.w / cell) + _CosmiPoleAxis.yzx * (_Time.y * _CosmiCloud.z);
    float n = CosmiFbm2(p);
    float edge = 1.0 - _CosmiCloud.x;
    return smoothstep(edge - 0.08, edge + 0.08, n);
}

// Sunlight left after drifting cloud shadow, 1 = clear. 1 everywhere when no planet is driven.
float CosmiCloudLight(float3 positionWS)
{
    if (_CosmiPlanet.w <= 0.0 || _CosmiCloud.y <= 0.0)
    {
        return 1.0;
    }

    return 1.0 - CosmiCloudDensity(CosmiUp(positionWS)) * _CosmiCloud.y;
}

// Cel ramp with a soft edge: `bands` flat steps (vision.md: 2–3 階柔和明暗) joined by a narrow smoothstep instead
// of a hard floor(), which aliased into crawling stair-steps on low-poly terrain.
float CosmiRamp(float light, float bands)
{
    float y = saturate(light) * bands + 0.5;
    return saturate((floor(y) + smoothstep(0.86, 1.0, frac(y))) / bands);
}

// Diffuse cel lighting. `shadowTint` is the colour of unlit surfaces; it is pulled toward the zenith colour so
// shadows pick up the sky (the cool-shadow / warm-light split that makes outdoor toon scenes read as sunlit).
half3 CosmiToon(half3 albedo, float3 normalWS, float3 positionWS, half3 lightColor, float3 lightDir, float shadow,
    half3 shadowTint, float bands, half3 ambient)
{
    float ramp = CosmiRamp(saturate(dot(normalWS, lightDir)) * shadow * CosmiCloudLight(positionWS), bands);
    half3 shade = lerp(shadowTint * 0.6, shadowTint * 0.8 + _CosmiSkyZenith.rgb * 0.08, _CosmiSkyHorizon.a);
    return albedo * lerp(shade, lightColor, ramp) + albedo * ambient * 0.25;
}

#if defined(UNIVERSAL_LIGHTING_INCLUDED)
// Point lights (interior dungeon lamps, combat-feel.md#地城氛圍) in two hard bands, so a lamp reads as a cel-shaded pool
// instead of a smooth gradient. Zero lights on the planet surface, so the loop costs nothing there. The caller's pass
// must declare `_ADDITIONAL_LIGHTS` and `_CLUSTER_LIGHT_LOOP` (Forward / Forward+); without them this returns 0.
half3 CosmiPointLights(half3 albedo, float3 normalWS, float3 positionWS, float4 positionCS)
{
    half3 sum = 0;
#if defined(_ADDITIONAL_LIGHTS)
    // LIGHT_LOOP_BEGIN reads a local named `inputData` in Forward+.
    InputData inputData = (InputData)0;
    inputData.positionWS = positionWS;
    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(positionCS);
    uint lightCount = GetAdditionalLightsCount();
    LIGHT_LOOP_BEGIN(lightCount)
        Light lamp = GetAdditionalLight(lightIndex, positionWS);
        // Facing matters: a pool that ignored the normal read as a glowing bubble hanging round the lamp.
        half reach = lamp.distanceAttenuation * saturate(dot(normalWS, lamp.direction));
        half band = step(0.03, reach) * 0.35 + step(0.18, reach) * 0.45;
        sum += albedo * lamp.color * band;
    LIGHT_LOOP_END
#endif
    return sum;
}
#endif

// Fog colour seen along `viewDir` (camera → point): the horizon colour, warmed toward the sun. The sky dome uses
// the same function at the horizon, so distant terrain dissolves into the sky with no seam.
half3 CosmiFogColor(float3 viewDir, half3 fogColor, float3 sunDir)
{
    float toSun = pow(saturate(dot(viewDir, sunDir)), 6.0);
    return lerp(fogColor, fogColor * 0.6 + _CosmiSunColor.rgb * 0.5, toSun * 0.3 * _CosmiSkyHorizon.a);
}

// Aerial perspective then the scene's linear fog. The exponential term tints from the first metre (layered depth,
// BotW-style空氣透視); the linear term (RenderSettings, tuned to the horizon by AtmosphereDriver) hides the far LOD.
half3 CosmiAtmosphere(half3 color, float3 positionWS, float fogFactor, float3 sunDir)
{
    float3 toPoint = positionWS - _WorldSpaceCameraPos;
    float dist = length(toPoint);
    float3 viewDir = toPoint / max(dist, 1e-4);
    half3 fogColor = CosmiFogColor(viewDir, unity_FogColor.rgb, sunDir);
    float haze = (1.0 - exp(-dist * 0.0028)) * 0.28 * _CosmiSkyHorizon.a;
    color = lerp(color, fogColor, haze);
#if defined(FOG_LINEAR) || defined(FOG_EXP) || defined(FOG_EXP2)
    color = lerp(fogColor, color, ComputeFogIntensity(fogFactor));
#endif
    return color;
}

#endif
