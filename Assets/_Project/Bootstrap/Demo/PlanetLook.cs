using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.Planet;
using Cosmimera.WorldGen;
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
            public float Stroke = 1f;        // brush-stroke contrast on the ground (ToonTerrain _StrokeStrength)
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
        /// Pre: shader non-null; reliefM > 0. Post: material with that type's palette (verdant when unknown), its turf
        ///      tones shifted for season (Summer = as authored).
        /// Side effects: allocates a Material.
        /// </remarks>
        public static Material TerrainMaterial(Shader shader, string planetTypeId, in PlanetConfig config, float reliefM,
            Season season = Season.Summer)
        {
            Contract.Requires(shader != null && reliefM > 0f, "shader required; relief > 0");
            var m = new Material(shader);
            Fill(m, PaletteFor(planetTypeId), config, reliefM, season);
            m.SetFloat("_FrozenGround", planetTypeId == "ice_crystal_planet" ? 1f : 0f);
            return m;
        }

        /// <summary>What settles on upward faces in winter on a planet type: its own snow (ash on a lava world).</summary>
        /// <remarks>Pre: none (unknown ids read as verdant). Post: the terrain palette's snow colour. Side effects: none.</remarks>
        public static Color SnowColor(string planetTypeId) => PaletteFor(planetTypeId).Snow;

        internal static Color RoadColor(string id, bool main)
        {
            Palette p = PaletteFor(id);
            return Color.Lerp(p.Rock, p.Shore, main ? 0.38f : 0.65f);
        }

        // REQ: planet-world.md#季節 (草地隨季節變色; 配色暫定). Summer is the authored palette. The shift scales with how
        // much of the ground is turf (Palette.Grass), so ice and lava worlds, whose "low ground" is not grass, keep
        // their colours. Ground snow is not a season effect here (Q-W22).
        private static void SeasonGround(Palette p, Season season, out Color low, out Color mid)
        {
            float turf = Mathf.Clamp01(p.Grass);
            switch (season)
            {
                case Season.Spring:
                    low = Color.Lerp(p.Low, Hex(0x8CCB52), 0.3f * turf);
                    mid = Color.Lerp(p.Mid, Hex(0x5FA846), 0.3f * turf);
                    break;
                case Season.Autumn:
                    low = Color.Lerp(p.Low, Hex(0xC8A858), 0.5f * turf);
                    mid = Color.Lerp(p.Mid, Hex(0xA8803A), 0.45f * turf);
                    break;
                case Season.Winter:
                    low = Color.Lerp(p.Low, Hex(0xB8A888), 0.7f * turf);
                    mid = Color.Lerp(p.Mid, Hex(0x8A8068), 0.65f * turf);
                    break;
                default:
                    low = p.Low;
                    mid = p.Mid;
                    break;
            }
        }

        /// <summary>Share of its own colour a named stone keeps on any planet; the rest is the planet's rock colour.</summary>
        /// <remarks>Pre: none. Post: constant in (0, 1). Side effects: none.</remarks>
        public const float StoneOwnColour = 0.65f;

        /// <summary>Own colour (0xRRGGBB) of the stone <paramref name="speciesId"/> that gives a bedrock its look.</summary>
        /// <remarks>
        /// Pre: speciesId is one of ScatterGeology.MainRock's ids (granite, limestone, slate, basalt).
        /// Post: the colour both the scattered boulders and the ground's rock faces mix from. Side effects: none.
        /// </remarks>
        public static int StoneRgb(string speciesId)
        {
            switch (speciesId)
            {
                case "granite": return 0xA89C94;
                case "limestone": return 0xD8D2BC;
                case "slate": return 0x4A5560;
                case "basalt": return 0x3A3A40;
                default:
                    Contract.Requires(false, "not a bedrock stone: " + speciesId);
                    return 0;
            }
        }

        private const int GeologyStamps = 8; // CosmiBiome.hlsl COSMI_GEO_MAX; TemplatePlanner places at most 8
        private static readonly int GeoCountId = Shader.PropertyToID("_CosmiGeoCount");
        private static readonly int GeoCenterId = Shader.PropertyToID("_CosmiGeoCenter");
        private static readonly int GeoRightId = Shader.PropertyToID("_CosmiGeoRight");
        private static readonly int GeoUpId = Shader.PropertyToID("_CosmiGeoUp");
        private static readonly int GeoRockId = Shader.PropertyToID("_CosmiGeoRock");
        private static readonly int GeoBaseId = Shader.PropertyToID("_CosmiGeoBase");
        private static readonly int GeoSnowId = Shader.PropertyToID("_CosmiGeoSnow");

        /// <summary>Snow line (share of the terrain relief) the ground of landform <paramref name="kind"/> is drawn with.</summary>
        /// <remarks>
        /// REQ: planet-world.md#寒帶地形 (表現層; 數值暫定).
        /// Pre: none (null or unknown kinds have no snow line of their own).
        /// Post: 0 = the planet type's own snow line; otherwise in (0, 1): a glacier is white from its tongue up, a
        ///       cirque from its headwalls up, tundra and moraine carry patchy snow on every rise.
        /// Side effects: none.
        /// </remarks>
        public static float LandformSnowLine(string kind)
        {
            switch (kind)
            {
                case "glacier": return 0.30f;
                case "cirque": return 0.34f;
                case "moraine": return 0.22f;
                case "tundra": return 0.12f;
                default: return 0f;
            }
        }

        /// <summary>Tells the ground shaders which bedrock lies where, so rock faces take its colour.</summary>
        /// <remarks>
        /// REQ: planet-world.md#地質變體 (P3-24 a: 地表配色).
        /// Pre: view.Count ≤ 8; perStamp has one defined entry per stamp (TemplatePlanner.Geologies); baseGeology defined;
        ///      landforms null, or one landform kind per stamp (LandformSynth.KindOf of each stamp id).
        /// Post: terrain and grass-root rock colour is the bedrock's stone mixed into the planet type's rock colour,
        ///       inside each stamp as far as its height blend reaches; Native bedrock keeps the planet's own. With
        ///       landforms, each stamp's snow line is <see cref="LandformSnowLine"/> of its kind.
        /// Side effects: writes the _CosmiGeo* shader globals until <see cref="ClearGeology"/>.
        /// </remarks>
        public static void SetGeology(string planetTypeId, TemplateView view, IReadOnlyList<Geology> perStamp, Geology baseGeology,
            IReadOnlyList<string> landforms = null)
        {
            Contract.Requires(perStamp != null && perStamp.Count == view.Count && view.Count <= GeologyStamps,
                "one geology per stamp, at most 8 stamps");
            Contract.Requires(landforms == null || landforms.Count == view.Count, "one landform kind per stamp");
            var snow = new Vector4[GeologyStamps];
            Color rock = PaletteFor(planetTypeId).Rock;
            // Unity fixes a global array's length at its first Set: always send all eight.
            var center = new Vector4[GeologyStamps];
            var right = new Vector4[GeologyStamps];
            var up = new Vector4[GeologyStamps];
            var colour = new Vector4[GeologyStamps];
            for (int i = 0; i < view.Count; i++)
            {
                TemplateStamp s = view.Stamp(i);
                center[i] = new Vector4(s.Center.x, s.Center.y, s.Center.z, 1f / s.TanHalf);
                right[i] = new Vector4(s.Right.x, s.Right.y, s.Right.z, 0f);
                up[i] = new Vector4(s.Up.x, s.Up.y, s.Up.z, 0f);
                colour[i] = GeologyRock(rock, perStamp[i]);
                snow[i] = new Vector4(landforms != null ? LandformSnowLine(landforms[i]) : 0f, 0f, 0f, 0f);
            }

            Shader.SetGlobalVectorArray(GeoSnowId, snow);

            Shader.SetGlobalVectorArray(GeoCenterId, center);
            Shader.SetGlobalVectorArray(GeoRightId, right);
            Shader.SetGlobalVectorArray(GeoUpId, up);
            Shader.SetGlobalVectorArray(GeoRockId, colour);
            Shader.SetGlobalVector(GeoBaseId, GeologyRock(rock, baseGeology));
            Shader.SetGlobalFloat(GeoCountId, view.Count);
        }

        /// <summary>Returns the ground shaders to the planet type's own rock colour everywhere.</summary>
        /// <remarks>Pre: none. Post: _CosmiGeoCount and _CosmiGeoBase are zero. Side effects: writes shader globals.</remarks>
        public static void ClearGeology()
        {
            Shader.SetGlobalFloat(GeoCountId, 0f);
            Shader.SetGlobalVector(GeoBaseId, Vector4.zero);
        }

        // rgb = rock colour, a = 1 where the bedrock overrides the material's _Rock.
        private static Vector4 GeologyRock(Color planetRock, Geology geology)
        {
            string stone = ScatterGeology.MainRock(geology);
            if (stone == null)
            {
                return Vector4.zero;
            }

            int rgb = StoneRgb(stone);
            Color c = Color.Lerp(planetRock, Hex(rgb), StoneOwnColour);
            return new Vector4(c.r, c.g, c.b, 1f);
        }

        /// <summary>Altitude (m) of the liquid surface; ground above it up to the shore band stays dry sand.</summary>
        /// <remarks>Pre: none. Post: constant ≤ 0. Side effects: none.</remarks>
        public const float SeaLevelM = -0.12f;

        /// <summary>Instanced grass material whose root colour is the terrain's own (same palette, same shader include).</summary>
        /// <remarks>
        /// Pre: shader (Cosmimera/ToonGrass) non-null; reliefM > 0 and equal to the terrain material's; fadeM > 0;
        ///      season equal to the terrain material's.
        /// Post: material with GPU instancing enabled. Side effects: allocates a Material.
        /// </remarks>
        public static Material GrassMaterial(Shader shader, string planetTypeId, in PlanetConfig config, float reliefM, float fadeM,
            Season season = Season.Summer)
        {
            Contract.Requires(shader != null && reliefM > 0f && fadeM > 0f, "shader required; relief and fade > 0");
            Palette p = PaletteFor(planetTypeId);
            var m = new Material(shader) { enableInstancing = true };
            Fill(m, p, config, reliefM, season);
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

        private static void Fill(Material m, Palette p, in PlanetConfig config, float reliefM, Season season = Season.Summer)
        {
            SeasonGround(p, season, out Color low, out Color mid);
            m.SetVector("_PlanetCenter", (Vector3)config.Center);
            m.SetFloat("_Radius", config.Radius);
            m.SetFloat("_Relief", reliefM);
            m.SetVector("_PoleAxis", new Vector4(0f, 1f, 0f, 0f));
            m.SetColor("_DeepWater", p.Deep);
            m.SetColor("_ShallowWater", p.Shallow);
            m.SetColor("_Shore", p.Shore);
            m.SetColor("_Low", low);
            m.SetColor("_Mid", mid);
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
            // REQ: vision.md#美術投資 — terrain and grass read the same strokes at the same scale, so a meadow and
            // the ground under it stay one painted surface.
            m.SetTexture("_StrokeTex", StrokeTexture.Shared);
            m.SetFloat("_StrokeStrength", p.Stroke);
            m.SetFloat("_StrokeScale", StrokeTilesPerMetre);
        }

        // One tile every 6.25 m: a round dab is 0.25–0.55 m across up close, and the shader's broad layer
        // (0.27× this) gives 0.9–2 m marks that still read from across a valley.
        private const float StrokeTilesPerMetre = 0.16f;

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

        /// <summary>Unoutlined toon material for a scenery prop built by <see cref="SceneryMeshes"/>.</summary>
        /// <remarks>
        /// Pre: shader (Cosmimera/ToonScenery) non-null; accentStrength and translucency in [0, 1]; swayM and glow ≥ 0.
        /// Post: new material; accent is the colour the mesh's accent mask blends toward. Side effects: allocates a Material.
        /// </remarks>
        public static Material SceneryMaterial(Shader shader, Color color, Color accent, float accentStrength, float swayM,
            float translucency, float glow)
        {
            Contract.Requires(shader != null, "shader required");
            Contract.Requires(accentStrength >= 0f && accentStrength <= 1f && translucency >= 0f && translucency <= 1f &&
                              swayM >= 0f && glow >= 0f, "strengths in [0,1]; sway and glow >= 0");
            var m = new Material(shader);
            m.SetColor("_BaseColor", color);
            accent.a = accentStrength;
            m.SetColor("_Accent", accent);
            m.SetFloat("_Sway", swayM);
            m.SetFloat("_Translucency", translucency);
            m.SetFloat("_Glow", glow);
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

    /// <summary>Shaders of the optional scenery layers (sky dome, grass, liquid surface, props); any may be null to skip that layer.</summary>
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

        /// <summary>Cosmimera/ToonScenery (trees, rocks, crystals), or null to draw them with the toon shader.</summary>
        /// <remarks>Pre: none. Post: as constructed. Side effects: none.</remarks>
        public Shader Props { get; }

        /// <summary>Bundles the scenery shaders.</summary>
        /// <remarks>Pre: none (each may be null). Post: properties return the arguments. Side effects: none.</remarks>
        public SceneryShaders(Shader sky, Shader grass, Shader water, Shader props = null)
        {
            Props = props;
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
    /// shader to its undriven fallback; clear-weather fog distances are restored so local weather cannot leak into the lobby.
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
        private long _simMs;
        private float _sinceSimS;
        private float _cloudCover = 0.42f;
        private float _wind = 1f;
        private float _stormDark;
        private float _windHeadingRad;
        private float _windTargetRad;
        private float _flash;
        private float _baseFogStartM;
        private float _baseFogEndM;

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

        /// <summary>The eased wind heading the shaders are drawing (radians from local east toward north).</summary>
        /// <remarks>Pre: none. Post: in [0, 2π). Side effects: none.</remarks>
        public float WindHeadingRad => _windHeadingRad;

        /// <summary>Sets the wind heading the scenery shaders bend toward.</summary>
        /// <remarks>
        /// REQ: weather.md#風向. Pre: headingTenths any integer (tenths of a degree from local east toward north).
        /// Post: applied from the next LateUpdate. Side effects: none until then.
        /// </remarks>
        public void SetWindHeading(int headingTenths)
        {
            _windTargetRad = headingTenths * (math.PI / 1800f);
        }

        /// <summary>Sets how dark the storm sky is and the current lightning flash.</summary>
        /// <remarks>
        /// Pre: darkness and flash in [0, 1].
        /// Post: from the next LateUpdate sky, fog, ambient and sun are dimmed by up to darkness × 55 % and then pulled
        ///       toward a cold white by flash. Side effects: none until then.
        /// </remarks>
        public void SetStorm(float darkness, float flash)
        {
            Contract.Requires(darkness >= 0f && darkness <= 1f && flash >= 0f && flash <= 1f, "darkness and flash in [0,1]");
            _stormDark = darkness;
            _flash = flash;
        }

        /// <summary>Sets the simulation clock the sun position is drawn from.</summary>
        /// <remarks>
        /// Pre: simMs &gt;= 0. Post: the next LateUpdate places the sun at WorldGen.SolarClock's angle for simMs.
        /// Side effects: none until then.
        /// </remarks>
        public void SetSimulationTime(long simMs)
        {
            Contract.Requires(simMs >= 0, "simMs must be >= 0");
            if (simMs != _simMs) _sinceSimS = 0f;
            _simMs = simMs;
        }

        /// <summary>Caps visibility for local weather while retaining the planet's clear-weather fog range.</summary>
        /// <remarks>
        /// Pre: Configure has run; radiusM finite and greater than zero.
        /// Post: fog end is min(radiusM, clear-weather end); start retains the clear-weather fade proportion.
        ///       Increasing the cap restores visibility without compounding previous caps.
        /// Side effects: writes RenderSettings fog distances immediately.
        /// </remarks>
        public void SetVisibilityRadius(float radiusM)
        {
            Contract.Requires(_camera != null && _baseFogEndM > 0f, "Configure must run before setting visibility");
            Contract.Requires(math.isfinite(radiusM) && radiusM > 0f, "visibility radius must be finite and > 0");
            RenderSettings.fogEndDistance = math.min(radiusM, _baseFogEndM);
            RenderSettings.fogStartDistance = _baseFogStartM * RenderSettings.fogEndDistance / _baseFogEndM;
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
            _baseFogStartM = RenderSettings.fogStartDistance;
            _baseFogEndM = RenderSettings.fogEndDistance;
        }

        private void LateUpdate()
        {
            if (_camera == null)
            {
                return;
            }

            // REQ: weather.md#時段 — the drawn sun follows simulation time on the same axis and angle as WorldGen.SolarClock,
            // so day phases in the rules match what is on screen. Frame time only interpolates between 50 ms ticks.
            _sinceSimS = math.min(_sinceSimS + Time.deltaTime, 0.1f);
            double turns = (_simMs + _sinceSimS * 1000.0) / (_dayLengthS * 1000.0);
            float angle = (float)((turns - math.floor(turns)) * 2.0 * math.PI_DBL);
            var toSun = new float3(math.cos(angle), 0f, math.sin(angle));
            _sun.transform.rotation = Quaternion.LookRotation(-(Vector3)toSun, (Vector3)_poleAxis);
            // Crossing into another region can swing the heading by up to 180°; turn the field over a few seconds
            // instead of snapping every blade at once.
            float turn = math.fmod(_windTargetRad - _windHeadingRad + 3f * math.PI, 2f * math.PI) - math.PI;
            _windHeadingRad += turn * math.saturate(Time.deltaTime * 0.6f);
            _windHeadingRad = math.fmod(_windHeadingRad + 2f * math.PI, 2f * math.PI);
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
            // A storm sky is slate, not just cloudy: dimming everything makes the lightning flash read as a flash.
            float dim = 1f - 0.55f * _stormDark;
            var slate = new Color(0.36f, 0.4f, 0.47f);
            horizon = Color.Lerp(horizon, slate * horizon.grayscale * 1.6f, 0.5f * _stormDark) * dim;
            zenith = Color.Lerp(zenith, slate * zenith.grayscale * 1.6f, 0.5f * _stormDark) * dim;
            sky *= dim;
            sunTint *= dim;
            var flashColor = new Color(0.82f, 0.88f, 1f);
            horizon = Color.Lerp(horizon, flashColor, 0.85f * _flash);
            zenith = Color.Lerp(zenith, flashColor * 0.9f, 0.85f * _flash);
            sky = Color.Lerp(sky, flashColor, 0.7f * _flash);
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
            Shader.SetGlobalVector(WindId, new Vector4(_wind, 0.22f * (0.5f + _wind), 9f, _windHeadingRad));
            _sun.intensity = math.saturate(e * 4f + 0.3f) * 1.2f * dim;
        }

        private void OnDestroy()
        {
            // A run's short weather cap must not carry over into the lobby after its atmosphere is gone.
            if (_baseFogEndM > 0f)
            {
                RenderSettings.fogStartDistance = _baseFogStartM;
                RenderSettings.fogEndDistance = _baseFogEndM;
            }
            Shader.SetGlobalVector(PlanetId, Vector4.zero);
            Shader.SetGlobalColor(SkyHorizonId, Color.clear);
            Shader.SetGlobalColor(SkyZenithId, Color.clear);
            Shader.SetGlobalColor(SunColorId, Color.clear);
            Shader.SetGlobalVector(CloudId, Vector4.zero);
            Shader.SetGlobalVector(WindId, Vector4.zero);
        }
    }
}
