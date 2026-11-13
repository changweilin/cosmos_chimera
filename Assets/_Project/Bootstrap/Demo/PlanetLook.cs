using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.Planet;
using Unity.Mathematics;
using UnityEngine;

namespace Cosmimera.Bootstrap.Demo
{
    /// <summary>
    /// D0 visual identity per planet type: toon terrain palette (P3-04 biome colours) and the atmosphere driver
    /// (P1-08 sky/day-night). REQ: planet-world.md#星球類型, vision.md. Colours are placeholders pending art review.
    /// </summary>
    /// <remarks>Pre: shaders non-null. Post: materials are new instances owned by the caller. Side effects: allocates.</remarks>
    public static class PlanetLook
    {
        private sealed class Palette
        {
            public Color Deep, Shallow, Shore, Low, Mid, High, Rock, Snow, Day, Dusk, Night;
            public float SnowLine = 0.8f;
            public float PolarLat = 0.94f; // sin(latitude) where polar snow starts (~70°)
            public Color Patch = new Color(1.14f, 1.1f, 0.8f); // multiplier on sunlit ground patches (ToonTerrain _Patch)
            public float Foam = 1f;          // surf line strength; 0 where the "sea" is not water
            public float WaterOpacity = 0.62f;
            public float WaterGlow;          // 1 = molten, self-lit surface
            public float Grass;              // share of grass clump sites that grow (0 = barren world)
            public float GrassHeight = 1f;
            public float Cloud = 0.42f;      // sky fraction under cloud
            public float Wind = 1f;
        }

        private static readonly Dictionary<string, Palette> Palettes = new Dictionary<string, Palette>
        {
            ["verdant_planet"] = new Palette
            {
                Deep = Hex(0x1C3F73), Shallow = Hex(0x3C8DB0), Shore = Hex(0xD9CC98), Low = Hex(0x6DB04A), Mid = Hex(0x3F7F36),
                High = Hex(0x8A8060), Rock = Hex(0x6E6A62), Snow = Hex(0xF2F6FF), Day = Hex(0x8CC8F0), Dusk = Hex(0xF0A070),
                Night = Hex(0x0B1430),
                Grass = 1f,
            },
            ["lava_planet"] = new Palette
            {
                // "Sea" on a lava world reads as molten rock: the same height mask, hot colours.
                Deep = Hex(0xC23A0C), Shallow = Hex(0xFF8A1E), Shore = Hex(0x2A1E1C), Low = Hex(0x3A2B28), Mid = Hex(0x4A3530),
                High = Hex(0x5E4038), Rock = Hex(0x2E2422), Snow = Hex(0x8C8076), Day = Hex(0xD9825A), Dusk = Hex(0xB0402A),
                Night = Hex(0x1A0808), SnowLine = 1.1f,
                Foam = 0f, WaterOpacity = 0.96f, WaterGlow = 1f, Patch = new Color(1.16f, 0.96f, 0.86f), Cloud = 0.3f, Wind = 0.6f,
            },
            ["ice_crystal_planet"] = new Palette
            {
                Deep = Hex(0x1B3D66), Shallow = Hex(0x5FA8C8), Shore = Hex(0xCFE3EE), Low = Hex(0xDCEBF2), Mid = Hex(0xB8D4E6),
                High = Hex(0xEEF6FF), Rock = Hex(0x7C8C99), Snow = Hex(0xFFFFFF), Day = Hex(0xBFE2FF), Dusk = Hex(0xE7B8D8),
                Night = Hex(0x0A1630), SnowLine = 0.45f, PolarLat = 0.7f,
                Foam = 0.6f, Patch = new Color(1.03f, 1.04f, 1.06f), Cloud = 0.35f,
            },
            ["storm_planet"] = new Palette
            {
                Deep = Hex(0x23304F), Shallow = Hex(0x47668A), Shore = Hex(0xA7A08A), Low = Hex(0x6E8A5A), Mid = Hex(0x5E7050),
                High = Hex(0x80786E), Rock = Hex(0x5C5A60), Snow = Hex(0xE6E8F0), Day = Hex(0x7E8EA8), Dusk = Hex(0x9A7FA8),
                Night = Hex(0x0E1020),
                Grass = 0.8f, GrassHeight = 1.25f, Cloud = 0.72f, Wind = 1.8f,
            },
            ["mineral_planet"] = new Palette
            {
                Deep = Hex(0x2E4E5E), Shallow = Hex(0x4E8A8A), Shore = Hex(0xD8B884), Low = Hex(0xC08850), Mid = Hex(0xA8643C),
                High = Hex(0x8C4E36), Rock = Hex(0x6A4434), Snow = Hex(0xE8D8C0), Day = Hex(0xE8C898), Dusk = Hex(0xE08858),
                Night = Hex(0x1A1020), SnowLine = 1.1f,
                Grass = 0.22f, GrassHeight = 0.7f, Patch = new Color(1.12f, 1.02f, 0.86f), Cloud = 0.25f, Wind = 0.8f,
            },
            ["twilight_planet"] = new Palette
            {
                Deep = Hex(0x241A4A), Shallow = Hex(0x4A3E8A), Shore = Hex(0xC8B890), Low = Hex(0x5A7A6A), Mid = Hex(0x4A5A6A),
                High = Hex(0x7A6A8A), Rock = Hex(0x4E4658), Snow = Hex(0xF0E8FF), Day = Hex(0xF0C878), Dusk = Hex(0xC870A0),
                Night = Hex(0x120A28),
                Grass = 0.7f, Cloud = 0.5f, Wind = 0.7f,
            },
        };

        /// <summary>Toon terrain material for a planet type.</summary>
        /// <remarks>
        /// Pre: shader non-null; reliefM > 0. Post: material with that type's palette (verdant when unknown).
        /// Side effects: allocates a Material.
        /// </remarks>
        public static Material TerrainMaterial(Shader shader, string planetTypeId, in PlanetConfig config, float reliefM)
        {
            Contract.Requires(shader != null && reliefM > 0f, "shader required; relief > 0");
            var m = new Material(shader);
            Fill(m, PaletteFor(planetTypeId), config, reliefM);
            return m;
        }

        /// <summary>Altitude (m) of the liquid surface; ground above it up to the shore band stays dry sand.</summary>
        /// <remarks>Pre: none. Post: constant ≤ 0. Side effects: none.</remarks>
        public const float SeaLevelM = -0.12f;

        /// <summary>Instanced grass material whose root colour is the terrain's own (same palette, same shader include).</summary>
        /// <remarks>
        /// Pre: shader (Cosmimera/ToonGrass) non-null; reliefM > 0 and equal to the terrain material's; fadeM > 0.
        /// Post: material with GPU instancing enabled. Side effects: allocates a Material.
        /// </remarks>
        public static Material GrassMaterial(Shader shader, string planetTypeId, in PlanetConfig config, float reliefM, float fadeM)
        {
            Contract.Requires(shader != null && reliefM > 0f && fadeM > 0f, "shader required; relief and fade > 0");
            Palette p = PaletteFor(planetTypeId);
            var m = new Material(shader) { enableInstancing = true };
            Fill(m, p, config, reliefM);
            m.SetFloat("_GrassHeight", p.GrassHeight);
            m.SetFloat("_GrassFade", fadeM);
            return m;
        }

        /// <summary>Liquid surface material (sea; molten rock on lava worlds) for a planet type.</summary>
        /// <remarks>Pre: shader (Cosmimera/ToonWater) non-null; reliefM > 0. Post: new material. Side effects: allocates a Material.</remarks>
        public static Material WaterMaterial(Shader shader, string planetTypeId, in PlanetConfig config, float reliefM)
        {
            Contract.Requires(shader != null && reliefM > 0f, "shader required; relief > 0");
            var m = new Material(shader);
            Fill(m, PaletteFor(planetTypeId), config, reliefM);
            return m;
        }

        /// <summary>Scenery rules that follow the planet type's look: where grass may grow and the weather overhead.</summary>
        /// <remarks>
        /// Pre: none (unknown ids read as verdant). Post: grassDensity, cloudCover in [0, 1]; snowLine and polarLat are the
        /// terrain shader's own thresholds; wind ≥ 0. Side effects: none.
        /// </remarks>
        public static (float grassDensity, float snowLine, float polarLat, float cloudCover, float wind) Scenery(string planetTypeId)
        {
            Palette p = PaletteFor(planetTypeId);
            return (p.Grass, p.SnowLine, p.PolarLat, p.Cloud, p.Wind);
        }

        private static void Fill(Material m, Palette p, in PlanetConfig config, float reliefM)
        {
            m.SetVector("_PlanetCenter", (Vector3)config.Center);
            m.SetFloat("_Radius", config.Radius);
            m.SetFloat("_Relief", reliefM);
            m.SetVector("_PoleAxis", new Vector4(0f, 1f, 0f, 0f));
            m.SetColor("_DeepWater", p.Deep);
            m.SetColor("_ShallowWater", p.Shallow);
            m.SetColor("_Shore", p.Shore);
            m.SetColor("_Low", p.Low);
            m.SetColor("_Mid", p.Mid);
            m.SetColor("_High", p.High);
            m.SetColor("_Rock", p.Rock);
            m.SetColor("_Snow", p.Snow);
            m.SetColor("_Patch", p.Patch);
            m.SetColor("_Shadow", new Color(0.55f, 0.6f, 0.78f));
            m.SetFloat("_Bands", 3f);
            m.SetFloat("_SnowLine", p.SnowLine);
            m.SetFloat("_PolarLat", p.PolarLat);
            m.SetFloat("_Foam", p.Foam);
            m.SetFloat("_SeaLevel", SeaLevelM);
            Color tint = Color.Lerp(p.Shallow, p.Deep, 0.35f);
            tint.a = p.WaterOpacity;
            m.SetColor("_WaterTint", tint);
            m.SetFloat("_WaterGlow", p.WaterGlow);
        }

        /// <summary>Outlined toon material for a creature placeholder.</summary>
        /// <remarks>Pre: shader non-null. Post: new material. Side effects: allocates a Material.</remarks>
        public static Material CreatureMaterial(Shader shader, Color color, float outlineWidthM)
        {
            Contract.Requires(shader != null, "shader required");
            var m = new Material(shader);
            m.SetColor("_BaseColor", color);
            m.SetFloat("_OutlineWidth", outlineWidthM);
            return m;
        }

        /// <summary>Sky colours (day, dusk, night) for a planet type.</summary>
        /// <remarks>Pre: none. Post: three colours. Side effects: none.</remarks>
        public static (Color day, Color dusk, Color night) Sky(string planetTypeId)
        {
            Palette p = PaletteFor(planetTypeId);
            return (p.Day, p.Dusk, p.Night);
        }

        private static Palette PaletteFor(string id) => id != null && Palettes.TryGetValue(id, out Palette p) ? p : Palettes["verdant_planet"];

        private static Color Hex(int rgb) => new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);
    }

    /// <summary>Shaders of the optional scenery layers (sky dome, grass, liquid surface); any may be null to skip that layer.</summary>
    /// <remarks>Pre: none. Post: immutable. Side effects: none.</remarks>
    public sealed class SceneryShaders
    {
        /// <summary>Cosmimera/ToonSky, or null.</summary>
        /// <remarks>Pre: none. Post: as constructed. Side effects: none.</remarks>
        public Shader Sky { get; }

        /// <summary>Cosmimera/ToonGrass, or null.</summary>
        /// <remarks>Pre: none. Post: as constructed. Side effects: none.</remarks>
        public Shader Grass { get; }

        /// <summary>Cosmimera/ToonWater, or null.</summary>
        /// <remarks>Pre: none. Post: as constructed. Side effects: none.</remarks>
        public Shader Water { get; }

        /// <summary>Bundles the scenery shaders.</summary>
        /// <remarks>Pre: none (each may be null). Post: properties return the arguments. Side effects: none.</remarks>
        public SceneryShaders(Shader sky, Shader grass, Shader water)
        {
            Sky = sky;
            Grass = grass;
            Water = water;
        }
    }

    /// <summary>
    /// Sky and fog from the sun's elevation at the camera, plus a slow sun orbit (P1-08 day/night, D0 version).
    /// </summary>
    /// <remarks>
    /// A skybox's gradient assumes world +Y is up, which is wrong almost everywhere on a sphere; colouring the clear
    /// colour and fog by the local sun elevation is correct at every point and costs nothing.
    /// It also publishes the sky, sun, cloud and wind state as shader globals (CosmiAtmosphere.hlsl) so terrain, grass,
    /// water and sky dome are lit by one atmosphere; they are zeroed again on destroy, which returns every scenery
    /// shader to its undriven fallback.
    /// Pre: Configure once. Side effects: writes RenderSettings fog/ambient, the camera background, the sun's colour and
    /// intensity, and the _Cosmi* shader globals every frame.
    /// </remarks>
    public sealed class AtmosphereDriver : MonoBehaviour
    {
        private Camera _camera;
        private Light _sun;
        private PlanetConfig _planet;
        private float3 _poleAxis;
        private Color _day;
        private Color _dusk;
        private Color _night;
        private float _dayLengthS;
        private float _cloudCover = 0.42f;
        private float _wind = 1f;

        private static readonly int PlanetId = Shader.PropertyToID("_CosmiPlanet");
        private static readonly int PoleAxisId = Shader.PropertyToID("_CosmiPoleAxis");
        private static readonly int SkyHorizonId = Shader.PropertyToID("_CosmiSkyHorizon");
        private static readonly int SkyZenithId = Shader.PropertyToID("_CosmiSkyZenith");
        private static readonly int SunColorId = Shader.PropertyToID("_CosmiSunColor");
        private static readonly int CloudId = Shader.PropertyToID("_CosmiCloud");
        private static readonly int WindId = Shader.PropertyToID("_CosmiWind");

        // Cloud cells ~70 m across: several fit inside the R=320 horizon, so their shadows visibly cross the ground.
        private const float CloudCellM = 70f;

        /// <summary>Sets the weather the scenery shaders read (CosmiAtmosphere.hlsl).</summary>
        /// <remarks>
        /// Pre: cloudCover in [0, 1]; wind ≥ 0 (1 = the breeze the grass was tuned for).
        /// Post: applied from the next LateUpdate. Side effects: none until then.
        /// </remarks>
        public void SetWeather(float cloudCover, float wind)
        {
            Contract.Requires(cloudCover >= 0f && cloudCover <= 1f && wind >= 0f, "cloudCover in [0,1]; wind >= 0");
            _cloudCover = cloudCover;
            _wind = wind;
        }

        /// <summary>Fog start on planets of R ≥ 640 m, where it was tuned.</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const float FogStartM = 40f;

        /// <summary>Largest share of the eye-height horizon the fog may start at.</summary>
        /// <remarks>
        /// Pre: none. Post: constant in (0, 1). Side effects: none.
        /// 40 m sat at 86% of R=640's 46.7 m horizon; keeping that share keeps a fade band before the horizon on
        /// smaller planets, where a fixed 40 m would start past it (R=320 horizon 33 m, SOL Q-D1-R3).
        /// </remarks>
        public const float FogStartHorizonFraction = 0.86f;

        /// <summary>Linear fog start distance on <paramref name="planet"/>.</summary>
        /// <remarks>
        /// REQ: docs/architecture/planet-tech.md#地平線.
        /// Pre: planet.IsValid; eyeHeight satisfies PlanetConfig.HorizonDistance's Pre.
        /// Post: result == min(FogStartM, FogStartHorizonFraction × planet.HorizonDistance(eyeHeight)); so result == FogStartM
        ///       for R ≥ 640 at eyeHeight 1.7 m, and result &lt; the horizon whenever eyeHeight &gt; 0.
        /// Side effects: none.
        /// </remarks>
        public static float FogStartDistance(in PlanetConfig planet, float eyeHeight) =>
            math.min(FogStartM, FogStartHorizonFraction * planet.HorizonDistance(eyeHeight));

        /// <summary>Starts driving the atmosphere.</summary>
        /// <remarks>
        /// Pre: camera, sun non-null; poleAxis unit; dayLengthS > 0; eyeHeight satisfies PlanetConfig.HorizonDistance's Pre.
        /// Post: fog enabled (linear, FogStartDistance(planet, eyeHeight) → horizon-scaled far distance). Side effects: RenderSettings.
        /// </remarks>
        public void Configure(Camera camera, Light sun, in PlanetConfig planet, float3 poleAxis, (Color day, Color dusk, Color night) sky,
            float dayLengthS, float eyeHeight)
        {
            Contract.Requires(camera != null && sun != null && dayLengthS > 0f, "camera, sun and day length required");
            _camera = camera;
            _sun = sun;
            _planet = planet;
            _poleAxis = poleAxis;
            (_day, _dusk, _night) = sky;
            _dayLengthS = dayLengthS;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            // Cast shadows anchor trees, rocks and buildings to the ground; the cel ramp turns the soft edge into a band.
            _sun.shadows = LightShadows.Soft;
            _sun.shadowStrength = 1f;
            _sun.shadowBias = 0.15f;
            _sun.shadowNormalBias = 1.2f;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            // Fog end well past the horizon keeps distant landmarks readable while hiding LOD pop at the far end.
            RenderSettings.fogStartDistance = FogStartDistance(planet, eyeHeight);
            RenderSettings.fogEndDistance = math.max(250f, planet.Radius * 0.5f);
        }

        private void LateUpdate()
        {
            if (_camera == null)
            {
                return;
            }

            _sun.transform.Rotate((Vector3)_poleAxis, 360f * Time.deltaTime / _dayLengthS, Space.World);
            float3 cam = _camera.transform.position;
            if (math.distance(cam, _planet.Center) < _planet.Radius * 0.5f)
            {
                return; // camera not placed by Cinemachine yet (first frames): no local "up" to measure against
            }

            float3 up = _planet.ToDirection(cam);
            float e = math.dot(up, -(float3)_sun.transform.forward); // sin(sun elevation)
            Color sky = e > 0.15f ? Color.Lerp(_dusk, _day, math.saturate((e - 0.15f) / 0.35f))
                : Color.Lerp(_night, _dusk, math.saturate((e + 0.15f) / 0.3f));
            float day = math.saturate((e + 0.05f) / 0.3f);
            // The horizon is paler than the sky overhead (more air along the line of sight); fog takes the horizon
            // colour so the far terrain fades into the sky dome without an edge.
            Color horizon = Color.Lerp(sky, Color.white, 0.06f * day);
            var zenith = new Color(sky.r * 0.24f, sky.g * 0.48f, sky.b * 0.96f);
            Color sunTint = Color.Lerp(new Color(1f, 0.56f, 0.3f), new Color(1f, 0.96f, 0.88f), math.saturate((e - 0.05f) / 0.4f));
            _camera.backgroundColor = horizon;
            RenderSettings.fogColor = horizon;
            RenderSettings.ambientLight = sky * 0.6f;
            _sun.color = sunTint;
            horizon.a = 1f;
            Shader.SetGlobalVector(PlanetId, new Vector4(_planet.Center.x, _planet.Center.y, _planet.Center.z, _planet.Radius));
            Shader.SetGlobalVector(PoleAxisId, new Vector4(_poleAxis.x, _poleAxis.y, _poleAxis.z, 0f));
            Shader.SetGlobalColor(SkyHorizonId, horizon);
            Shader.SetGlobalColor(SkyZenithId, zenith);
            Shader.SetGlobalColor(SunColorId, new Color(sunTint.r * day, sunTint.g * day, sunTint.b * day, day));
            Shader.SetGlobalVector(CloudId, new Vector4(_cloudCover, 0.38f * day, 0.012f * (0.5f + _wind), CloudCellM));
            Shader.SetGlobalVector(WindId, new Vector4(_wind, 0.22f * (0.5f + _wind), 9f, 0f));
            _sun.intensity = math.saturate(e * 4f + 0.3f) * 1.2f;
        }

        private void OnDestroy()
        {
            Shader.SetGlobalVector(PlanetId, Vector4.zero);
            Shader.SetGlobalColor(SkyHorizonId, Color.clear);
            Shader.SetGlobalColor(SkyZenithId, Color.clear);
            Shader.SetGlobalColor(SunColorId, Color.clear);
            Shader.SetGlobalVector(CloudId, Vector4.zero);
            Shader.SetGlobalVector(WindId, Vector4.zero);
        }
    }
}
