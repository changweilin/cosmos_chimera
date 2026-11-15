using System;
using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.Planet;
using Cosmimera.WorldGen;
using Unity.Mathematics;
using UnityEngine;

namespace Cosmimera.Bootstrap.Demo
{
    /// <summary>
    /// D0 scatter (P3-09, P3-25): draws the trees, rocks, mineral veins, spires and understory plants <see cref="ScatterPlanner"/> places around the player,
    /// as procedural meshes (<see cref="SceneryMeshes"/>) shaded by Cosmimera/ToonScenery.
    /// REQ: planet-world.md (7. 散佈); destructibles.md (placement lives in the rule layer, items block and can break).
    /// </summary>
    /// <remarks>
    /// Pre: Configure once. Post: the set of live objects depends only on (seed, planet type, player position), so a
    /// cell always yields the same item whenever it streams in; items never float (altitude comes from the same
    /// ground function the terrain mesh uses) and never stand in water or on steep slopes.
    /// Why streaming and not baking the whole planet: R=640 m is ~5 Mm² of surface; only the horizon (~47 m) is visible.
    /// The trunk, rock body, crystal shaft, shrub core or cane clump keeps its primitive collider (destructibles.md#碰撞:
    /// trees, shrubs and rocks block the character) on a "Block" child; the drawn "Model" child has none, and neither
    /// does grass, fern, reed or a flower patch (草不擋). Destroyed items (isDestroyed) are never drawn.
    /// REQ: Q-W15 — the landform decides the species; the planet type only recolours it (Theme).
    /// Side effects: creates/destroys GameObjects under this component; owns its materials and meshes.
    /// </remarks>
    public sealed class ScatterField : MonoBehaviour
    {
        private readonly struct Palette
        {
            public readonly Color Color;  // crown, rock body, crystal, leaves or grass blades
            public readonly Color Accent; // trunk, shrub stems or canes
            public readonly Color Top;    // what settles on upward faces: sunlit leaves, moss, snow, ash; crystal tips; grass plumes
            public readonly float Glow;   // self-lit crystals
            public readonly float Sway;   // metres of lean per metre of height for parts that ride on a plant (Role.Bloom)

            public Palette(Color color, Color accent, Color top, float glow = 0f, float sway = 0f)
            {
                Sway = sway;
                Color = color;
                Accent = accent;
                Top = top;
                Glow = glow;
            }
        }

        private enum Role
        {
            Bark = 0,
            Leaf = 1,
            Rock = 2,
            Crystal = 3,
            Cane = 4,
            Blade = 5,
            Plume = 6,

            /// <summary>Fleshy or woody surface that never sways: cactus, mushroom stalk, fallen log.</summary>
            Still = 7,

            /// <summary>Small bright parts that ride on a plant: blossoms, seed heads, mushroom caps.</summary>
            Bloom = 8,
        }

        /// <summary>Distance (m) from the focus within which every item is a full object with its collider.</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const float RadiusM = 90f;

        // Rocks smaller than this are not worth a silhouette past RadiusM: under fog they are a few pixels.
        private const float FarMinRockM = 1.5f;
        private const float RefreshMoveM = 6f;
        private const float RefreshSeconds = 0.5f;
        // Metres of lean per metre of height in a full gust: a 6 m tree's crown travels ~0.4 m.
        private const float TreeSway = 0.06f;
        // Thin stems travel further than a trunk: a 2 m tussock's plumes swing ~0.3 m.
        private const float CaneSway = 0.1f;
        private const float GrassSway = 0.16f;

        // REQ: planet-world.md#散佈 (Q-W15) — what a planet type contributes: colours only. Species, sizes and
        // substances come from the rule layer (ScatterBiomes).
        private readonly struct Theme
        {
            public readonly Color Leaf, LeafTop, Bark;
            public readonly Color Rock, RockTop;
            public readonly Color Crystal, CrystalTop;
            public readonly float CrystalGlow;
            public readonly Color Shrub, ShrubTop;
            public readonly Color Grass, Plume;
            public readonly Color Cane, CaneLeaf;
            public readonly Color Cap, Bloom;
            // Evergreen foliage (conifer needles, cactus flesh): the planet's leaf colour in every season.
            public readonly Color Needle, NeedleTop;
            // Winter: deciduous crowns are down (planet-world.md#季節 冬：落葉枯枝).
            public readonly bool Bare;

            public Theme(int leaf, int leafTop, int bark, int rock, int rockTop, int crystal, int crystalTop, float crystalGlow,
                int shrub, int shrubTop, int grass, int plume, int cane, int caneLeaf, int cap, int bloom)
            {
                Leaf = C(leaf);
                LeafTop = C(leafTop);
                Bark = C(bark);
                Rock = C(rock);
                RockTop = C(rockTop);
                Crystal = C(crystal);
                CrystalTop = C(crystalTop);
                CrystalGlow = crystalGlow;
                Shrub = C(shrub);
                ShrubTop = C(shrubTop);
                Grass = C(grass);
                Plume = C(plume);
                Cane = C(cane);
                CaneLeaf = C(caneLeaf);
                Cap = C(cap);
                Bloom = C(bloom);
                Needle = Leaf;
                NeedleTop = LeafTop;
                Bare = false;
            }

            private Theme(in Theme b, Color leaf, Color leafTop, Color needleTop, Color shrub, Color shrubTop, Color grass,
                Color plume, Color caneLeaf, Color rockTop, bool bare)
            {
                this = b;
                Leaf = leaf;
                LeafTop = leafTop;
                NeedleTop = needleTop;
                Shrub = shrub;
                ShrubTop = shrubTop;
                Grass = grass;
                Plume = plume;
                CaneLeaf = caneLeaf;
                RockTop = rockTop;
                Bare = bare;
            }

            // REQ: planet-world.md#季節 (Q-W16; 配色暫定). Summer is the look the themes were authored in. Every shift is
            // a blend toward a seasonal tone, so a planet keeps its own hue (twilight stays violet, only warmer in
            // autumn). `snow` is what settles on upward faces in winter: the planet type's own snow colour.
            public Theme In(Season season, Color snow)
            {
                switch (season)
                {
                    case Season.Spring:
                        return new Theme(this, Mix(Leaf, 0x8CCB52, 0.4f), Mix(LeafTop, 0xF48FB8, 0.8f), Mix(LeafTop, 0xA8D878, 0.3f),
                            Mix(Shrub, 0x86C24E, 0.4f), Mix(ShrubTop, 0xFFE9A0, 0.6f), Mix(Grass, 0x9CCB5A, 0.4f), Plume,
                            Mix(CaneLeaf, 0x9CCB5A, 0.3f), RockTop, false);
                    case Season.Autumn:
                        return new Theme(this, Mix(Leaf, 0xC2502A, 0.75f), Mix(LeafTop, 0xF2B030, 0.8f), NeedleTop,
                            Mix(Shrub, 0xB8642A, 0.65f), Mix(ShrubTop, 0xE8C040, 0.7f), Mix(Grass, 0xC8A858, 0.6f),
                            Mix(Plume, 0xF4E8C8, 0.5f), Mix(CaneLeaf, 0xC8B050, 0.35f), RockTop, false);
                    case Season.Winter:
                        return new Theme(this, Mix(Leaf, 0x7A705A, 0.6f), snow, snow, Mix(Shrub, 0x6A5A48, 0.7f), snow,
                            Mix(Grass, 0xB8A888, 0.7f), Mix(Plume, 0xEDE6D8, 0.6f), Mix(CaneLeaf, 0x9A9A6A, 0.4f), snow, true);
                    default:
                        return this;
                }
            }
        }

        private static readonly Dictionary<string, Theme> Themes = new Dictionary<string, Theme>
        {
            ["verdant_planet"] = new Theme(0x3F7F36, 0x9CBF4A, 0x6B4A2B, 0x6E6A62, 0x5E8A3A, 0x7FD8D0, 0xE8FFF8, 0.2f,
                0x4C8A3A, 0xA8C858, 0x7FA04A, 0xEFE6CF, 0x9DB45A, 0x6FA03C, 0xC8402A, 0xF06A9A),
            ["lava_planet"] = new Theme(0x5A4A3A, 0x8A6A4A, 0x2E2422, 0x2E2422, 0x5A4640, 0xFF8A1E, 0xFFE08A, 0.9f,
                0x5A4A3A, 0x8A6A4A, 0x8A7A4A, 0xF0D8B0, 0x6A5A3A, 0x7A6A40, 0xE0702A, 0xFF9A3A),
            ["ice_crystal_planet"] = new Theme(0x5F7F78, 0xE8F4F8, 0x4A4038, 0xDCEBF2, 0xFFFFFF, 0x9FD8F0, 0xF2FBFF, 0.2f,
                0x5F7F78, 0xE8F4F8, 0x9AB0A8, 0xF4FAFC, 0x7A9088, 0x8FA89C, 0x8FB8D8, 0xE8F0FF),
            ["storm_planet"] = new Theme(0x5E7050, 0x8A9A6A, 0x4A4038, 0x5C5A60, 0x7A8A5A, 0x8AA0FF, 0xE0E8FF, 0.4f,
                0x55684A, 0x8A9A6A, 0x8A9460, 0xE6E0D0, 0x8C9460, 0x66784A, 0x9A8A5A, 0xF0E080),
            ["mineral_planet"] = new Theme(0x8A8048, 0xC8B070, 0x6A4A34, 0x8C4E36, 0xC89868, 0xE0B060, 0xFFF0C0, 0.3f,
                0x8A8048, 0xC8B070, 0xB09A5A, 0xF4E4C0, 0xA08A50, 0x8A8048, 0xD0A040, 0xF0C060),
            ["twilight_planet"] = new Theme(0x8A6AC8, 0xD0A8F0, 0x3E3050, 0x5A5070, 0x8A78C0, 0xE070B0, 0xFFD0F0, 0.6f,
                0x7A5AB8, 0xC8A0E8, 0x9A7AC8, 0xFFE0F8, 0x8A78C0, 0x6A58B0, 0xC060E0, 0xFFB0F0),
        };

        // How one species is drawn: which mesh family, and the material of each of its (one or two) sub-meshes.
        private readonly struct Look
        {
            public readonly SceneryArchetype Archetype;
            public readonly Role RoleA, RoleB;
            public readonly Palette A, B;
            public readonly Palette[] VariantsB; // per-item choice for sub-mesh 1 (gem and blossom colours); null = B

            public Look(SceneryArchetype archetype, Role roleA, Palette a, Role roleB = Role.Leaf, Palette b = default,
                Palette[] variantsB = null)
            {
                Archetype = archetype;
                RoleA = roleA;
                A = a;
                RoleB = roleB;
                B = b;
                VariantsB = variantsB;
            }
        }

        private static Palette P(Color color, Color top, float glow = 0f, float sway = 0f) => new Palette(color, color, top, glow, sway);

        private static Color Mix(Color a, int rgb, float t) => Color.Lerp(a, C(rgb), t);

        // A named stone keeps most of its own colour on every planet: marble must still read as marble on a lava world.
        private static Look Stone(Theme theme, int rgb, SceneryArchetype archetype = SceneryArchetype.Boulder) =>
            new Look(archetype, Role.Rock, P(Mix(theme.Rock, rgb, PlanetLook.StoneOwnColour), theme.RockTop));

        private static Look Ore(Theme theme, int host, int ore, int oreTop, float glow) =>
            new Look(SceneryArchetype.Outcrop, Role.Rock, P(Mix(theme.Rock, host, 0.5f), theme.RockTop),
                Role.Crystal, P(C(ore), C(oreTop), glow));

        private static Look Tree(Theme theme, SceneryArchetype archetype, Color leaf, Color top) =>
            new Look(archetype, Role.Bark, P(theme.Bark, theme.Bark), Role.Leaf, P(leaf, top));

        // Species ids are ScatterBiomes' catalog. An unknown id (a species added there but not here) fails loudly.
        private static Look LookFor(string speciesId, Theme theme, string planetTypeId)
        {
            switch (speciesId)
            {
                case "broadleaf":
                    if (theme.Bare)
                    {
                        // The bare-limbed mesh is the snag's; living bark (not a snag's grey) and snow on the limbs
                        // tell a dormant tree from a dead one.
                        return new Look(SceneryArchetype.Snag, Role.Bark, P(theme.Bark, theme.RockTop));
                    }

                    // The twilight planet keeps its flat-topped silhouette: its trees are part of the planet's look.
                    return Tree(theme, planetTypeId == "twilight_planet" ? SceneryArchetype.Umbrella : SceneryArchetype.Broadleaf,
                        theme.Leaf, theme.LeafTop);
                case "conifer":
                    return Tree(theme, SceneryArchetype.Conifer, Mix(theme.Needle, 0x1F4A3A, 0.45f),
                        theme.Bare ? theme.NeedleTop : Mix(theme.NeedleTop, 0x6FA07A, 0.4f));
                case "umbrella":
                    return Tree(theme, SceneryArchetype.Umbrella, Mix(theme.Leaf, 0x7A9A3A, 0.35f), theme.LeafTop);
                case "swamp_tree":
                    return new Look(SceneryArchetype.SwampTree, Role.Bark, P(Tone(theme.Bark, 0.8f), theme.Bark),
                        Role.Leaf, P(Mix(theme.Leaf, 0x4A6A3A, 0.4f), Mix(theme.LeafTop, 0x9AB070, 0.4f)));
                case "snag":
                    return new Look(SceneryArchetype.Snag, Role.Bark, P(Mix(theme.Bark, 0x9A9488, 0.5f), theme.Bark));
                case "log":
                    return new Look(SceneryArchetype.Log, Role.Still, P(Tone(theme.Bark, 0.9f), theme.RockTop));
                case "cactus":
                    return new Look(SceneryArchetype.Cactus, Role.Still, P(Mix(theme.Needle, 0x4F8A4A, 0.5f), Mix(theme.NeedleTop, 0xA8C878, 0.5f)),
                        Role.Bloom, P(theme.Bloom, C(0xFFE070)));
                case "giant_mushroom":
                    return new Look(SceneryArchetype.Mushroom, Role.Still, P(Mix(theme.Bark, 0xE8DCC0, 0.85f), C(0xE8DCC0)),
                        Role.Bloom, P(theme.Cap, C(0xF8F0E0), theme.CrystalGlow * 0.5f));
                case "granite":
                case "granite_tor":
                    return Stone(theme, PlanetLook.StoneRgb("granite"));
                case "limestone":
                    return Stone(theme, PlanetLook.StoneRgb("limestone"));
                case "marble":
                    return Stone(theme, 0xF0EEE8);
                case "slate":
                    return Stone(theme, PlanetLook.StoneRgb("slate"));
                case "sandstone":
                    return Stone(theme, 0xC8926A);
                case "basalt":
                    return Stone(theme, PlanetLook.StoneRgb("basalt"));
                case "reef_rock":
                    return new Look(SceneryArchetype.Boulder, Role.Rock, P(Mix(theme.Rock, 0x6A6258, 0.65f), C(0x5A8A70)));
                case "moraine":
                    return Stone(theme, 0x8A8C90);
                case "basalt_column":
                    return Stone(theme, 0x34343A, SceneryArchetype.Columns);
                case "stone_spire":
                    return Stone(theme, 0xCFC8B0, SceneryArchetype.Spire);
                case "ice_spire":
                    return new Look(SceneryArchetype.Spire, Role.Crystal, P(C(0x9FD8F0), C(0xF2FBFF), 0.15f));
                case "crystal":
                    return new Look(SceneryArchetype.CrystalCluster, Role.Crystal, P(theme.Crystal, theme.CrystalTop, theme.CrystalGlow));
                case "ore_gold":
                    return Ore(theme, 0xB8A890, 0xFFC830, 0xFFF2A0, 0.15f);
                case "ore_silver":
                    return Ore(theme, 0x70747C, 0xDCE4EC, 0xFFFFFF, 0.1f);
                case "ore_copper":
                    // Top = verdigris on the weathered faces.
                    return Ore(theme, 0x8A6A5A, 0xC8703A, 0x4FB09A, 0f);
                case "ore_iron":
                    return Ore(theme, 0x6A4A44, 0x8A3A2A, 0xC86A3A, 0f);
                case "coal_seam":
                    return Ore(theme, 0x4A4640, 0x1C1C20, 0x3A3A44, 0f);
                case "oil_shale":
                    return Ore(theme, 0x5A4A38, 0x141008, 0x5A4A20, 0f);
                case "gem_geode":
                    return new Look(SceneryArchetype.Outcrop, Role.Rock, P(Mix(theme.Rock, 0x5A5560, 0.5f), theme.RockTop),
                        Role.Crystal, default, new[]
                        {
                            P(C(0xE0304A), C(0xFFB0C0), 0.5f), // ruby
                            P(C(0x3060E0), C(0xB0D0FF), 0.5f), // sapphire
                            P(C(0x30C070), C(0xB0FFD0), 0.5f), // emerald
                            P(C(0xA050E0), C(0xE8C0FF), 0.5f), // amethyst
                            P(C(0xFFB030), C(0xFFF0B0), 0.5f), // topaz
                        });
                case "shrub":
                    return new Look(SceneryArchetype.Shrub, Role.Bark, P(theme.Bark, theme.Bark), Role.Leaf, P(theme.Shrub, theme.ShrubTop));
                case "dwarf_shrub":
                    return new Look(SceneryArchetype.Shrub, Role.Bark, P(theme.Bark, theme.Bark),
                        Role.Leaf, P(Mix(theme.Shrub, 0x6A5A4A, 0.25f), Mix(theme.ShrubTop, 0xC87AA0, 0.3f)));
                case "bamboo":
                    return new Look(SceneryArchetype.CaneClump, Role.Cane, P(theme.Cane, theme.Cane),
                        Role.Leaf, P(theme.CaneLeaf, Color.Lerp(theme.CaneLeaf, theme.Cane, 0.5f)));
                case "tall_grass":
                    return new Look(SceneryArchetype.PlumeGrass, Role.Blade, P(theme.Grass, theme.Plume), Role.Plume, P(theme.Plume, theme.Plume));
                case "fern":
                    return new Look(SceneryArchetype.Fern, Role.Blade, P(Color.Lerp(theme.Shrub, theme.Leaf, 0.5f), theme.LeafTop));
                case "reed":
                    return new Look(SceneryArchetype.Reed, Role.Blade, P(Mix(theme.Grass, 0x6A9A5A, 0.3f), theme.Plume),
                        Role.Bloom, P(C(0x6A4A2A), C(0x9A7A4A), 0f, GrassSway));
                case "flowers":
                    return new Look(SceneryArchetype.FlowerPatch, Role.Blade, P(theme.Shrub, theme.ShrubTop), Role.Bloom, default, new[]
                    {
                        P(theme.Bloom, C(0xFFE070), 0f, GrassSway),
                        P(C(0xF8F4E8), C(0xFFD040), 0f, GrassSway),
                        P(C(0xF0C830), C(0xC87020), 0f, GrassSway),
                        P(Color.Lerp(theme.Bloom, C(0x7060E0), 0.6f), C(0xFFF0A0), 0f, GrassSway),
                    });
                default:
                    throw new ArgumentOutOfRangeException(nameof(speciesId), speciesId, "scatter species has no look");
            }
        }

        private PlanetConfig _config;
        private ScatterPlanner _planner;
        private Look[] _looks;
        private int _cellsPerFace;
        private Func<long, bool> _isDestroyed;
        private Func<float3> _focus;
        private Shader _shader;
        private Shader _sceneryShader;
        private readonly Dictionary<long, List<GameObject>> _live = new Dictionary<long, List<GameObject>>();
        private readonly HashSet<long> _wanted = new HashSet<long>();
        private readonly List<long> _stale = new List<long>();
        // Items handed to ObjectPhysics (woken rigidbodies): no longer streamed, never rebuilt at their home spot.
        private readonly HashSet<long> _detached = new HashSet<long>();
        private readonly Dictionary<(Color, Color, Role), Material> _materials = new Dictionary<(Color, Color, Role), Material>();
        private readonly Dictionary<int, Mesh> _meshes = new Dictionary<int, Mesh>();
        private float3 _lastFocus;
        private float _nextRefresh;
        private bool _configured;

        /// <summary>Number of live scattered objects (roots), for tests and diagnostics.</summary>
        /// <remarks>Pre: none. Post: ≥ 0. Side effects: none.</remarks>
        public int LiveCount
        {
            get
            {
                int n = 0;
                foreach (List<GameObject> l in _live.Values)
                {
                    n += l.Count;
                }

                return n;
            }
        }

        /// <summary>Starts scattering around <paramref name="focus"/>.</summary>
        /// <remarks>
        /// Pre: groundAltitude, focus and shader non-null; isSea may be null (no sea mask on pure-noise planets);
        ///      isClear may be null (no roads or buildings to avoid) and must be deterministic; sceneryShader as in the
        ///      other overload.
        /// Post: Update streams cells in and out; no item stands where isClear is false. Side effects: none until Update.
        /// </remarks>
        public void Configure(in PlanetConfig config, ulong seed, string planetTypeId, Func<float3, float> groundAltitude,
            Func<float3, bool> isSea, Func<float3> focus, Shader shader, Func<float3, bool> isClear = null,
            Shader sceneryShader = null, Season season = Season.Summer)
        {
            Contract.Requires(groundAltitude != null && focus != null && shader != null,
                "groundAltitude, focus and shader are required");
            Configure(config, new ScatterPlanner(config, seed, planetTypeId, groundAltitude, isSea, isClear), planetTypeId,
                focus, shader, null, sceneryShader, season);
        }

        /// <summary>Starts drawing <paramref name="planner"/>'s items around <paramref name="focus"/>.</summary>
        /// <remarks>
        /// Pre: config is the planner's planet; planner, focus and shader non-null; planetTypeId is the one the planner was
        ///      built with; isDestroyed may be null (nothing destroyed) and, once true for a key, stays true;
        ///      sceneryShader is Cosmimera/ToonScenery, or null to draw the same meshes with shader (flat colour, no
        ///      wind: the optional-layer convention of SceneryShaders); season defined (Summer = the authored look).
        /// Post: Update streams cells in and out; destroyed items are never drawn; foliage, grass and what settles on
        ///       upward faces are coloured for season, and in winter broadleaf trees are drawn bare (appearance only:
        ///       colliders and rule-layer items are the same in every season).
        /// Side effects: none until Update.
        /// </remarks>
        public void Configure(in PlanetConfig config, ScatterPlanner planner, string planetTypeId, Func<float3> focus,
            Shader shader, Func<long, bool> isDestroyed, Shader sceneryShader = null, Season season = Season.Summer)
        {
            Contract.Requires(planner != null && focus != null && shader != null, "planner, focus and shader are required");
            Contract.Requires(season >= Season.Spring && season <= Season.Winter, "season must be defined");
            _config = config;
            _planner = planner;
            Theme theme = planetTypeId != null && Themes.TryGetValue(planetTypeId, out Theme t) ? t : Themes["verdant_planet"];
            theme = theme.In(season, PlanetLook.SnowColor(planetTypeId));
            _looks = new Look[planner.Kinds.Count];
            for (int k = 0; k < _looks.Length; k++)
            {
                _looks[k] = LookFor(planner.Kinds[k].Id, theme, planetTypeId);
            }

            _cellsPerFace = planner.CellsPerFace;
            _isDestroyed = isDestroyed;
            _focus = focus;
            _shader = shader;
            _sceneryShader = sceneryShader;
            _configured = true;
            _lastFocus = new float3(float.NaN);
            _nextRefresh = 0f;
        }

        /// <summary>Removes the drawn item of cell <paramref name="key"/> (it was destroyed); it never streams back.</summary>
        /// <remarks>
        /// Pre: Configure called with an isDestroyed that now returns true for key.
        /// Post: no GameObject for key. Side effects: destroys GameObjects.
        /// </remarks>
        public void Remove(long key)
        {
            ItemGone?.Invoke(key);
            if (!_live.TryGetValue(key, out List<GameObject> objects))
            {
                return;
            }

            foreach (GameObject go in objects)
            {
                Destroy(go);
            }

            objects.Clear();
        }

        /// <summary>Hands the drawn item of cell <paramref name="key"/> to the caller; the cell never draws it again.</summary>
        /// <remarks>
        /// REQ: ADR-0009 §3 — a woken object becomes a rigidbody that may roll out of its cell, so streaming must neither
        /// destroy it with the cell nor rebuild a second copy at home. Pre: Configure called.
        /// Post: returns the item's root (null when the cell is not drawn or holds nothing); key is never built again;
        ///       the caller owns the root. Side effects: stops tracking it.
        /// </remarks>
        public GameObject Detach(long key)
        {
            _detached.Add(key);
            ItemGone?.Invoke(key);
            if (!_live.TryGetValue(key, out List<GameObject> objects) || objects.Count == 0)
            {
                return null;
            }

            GameObject root = objects[0];
            objects.Clear();
            return root;
        }

        /// <summary>Raised with a cell key when its item stops being drawn at home (destroyed or handed to physics).</summary>
        /// <remarks>Pre: none. Post: raised once per <see cref="Remove"/> / <see cref="Detach"/> call. Side effects: none.</remarks>
        internal event Action<long> ItemGone;

        /// <summary>The far-layer silhouette of canopy cell <paramref name="key"/>'s item (P1-15), if it has one.</summary>
        /// <remarks>
        /// Pre: Configure called; key is a canopy cell key of the planner.
        /// Post: false when the cell is empty, its item was destroyed or detached, or the item is too small to show in
        ///       the distance; otherwise shape is not None and the colours are the ones the near object is drawn with
        ///       (body = crown / rock / cap, accent = trunk or stalk, top = what settles on upward faces).
        /// Side effects: caches the cell in the planner.
        /// </remarks>
        internal bool TryGetFarItem(long key, out ScatterItem item, out FarShape shape, out Color body, out Color accent,
            out Color top)
        {
            shape = FarShape.None;
            body = accent = top = default;
            if (_detached.Contains(key) || !_planner.TryGetItem(key, out item) || (_isDestroyed != null && _isDestroyed(key)))
            {
                item = default;
                return false;
            }

            Look look = _looks[item.KindIndex];
            shape = look.Archetype switch
            {
                SceneryArchetype.Broadleaf => FarShape.Broadleaf,
                SceneryArchetype.SwampTree => FarShape.Broadleaf,
                SceneryArchetype.Conifer => FarShape.Conifer,
                SceneryArchetype.Umbrella => FarShape.Umbrella,
                SceneryArchetype.Snag => FarShape.Snag,
                SceneryArchetype.Mushroom => FarShape.Mushroom,
                SceneryArchetype.Cactus => FarShape.Column,
                SceneryArchetype.Columns => FarShape.Column,
                SceneryArchetype.Spire => FarShape.Spire,
                SceneryArchetype.CrystalCluster => FarShape.Crystal,
                SceneryArchetype.Boulder => item.SizeM >= FarMinRockM ? FarShape.Boulder : FarShape.None,
                _ => FarShape.None,
            };
            if (shape == FarShape.None)
            {
                return false;
            }

            // Two-part looks (trunk + crown, stalk + cap) carry the body in part B, exactly as Build picks it.
            bool twoPart = shape == FarShape.Broadleaf || shape == FarShape.Conifer || shape == FarShape.Umbrella ||
                           shape == FarShape.Mushroom;
            if (twoPart)
            {
                uint hash = Hash(item.CellKey);
                Palette second = look.VariantsB != null ? look.VariantsB[(int)((hash >> 4) % (uint)look.VariantsB.Length)] : look.B;
                body = second.Color;
                top = second.Top;
                accent = look.A.Color;
            }
            else
            {
                body = look.A.Color;
                top = look.A.Top;
                accent = look.A.Color;
            }

            return true;
        }

        /// <summary>Spawns the item of one cell, or none.</summary>
        /// <remarks>
        /// Pre: Configure called; key encodes (face, j, i). Post: ≤ 1 item rooted at the ground of that cell.
        /// Side effects: creates GameObjects.
        /// </remarks>
        private void BuildCell(long key)
        {
            var objects = new List<GameObject>();
            _live[key] = objects;
            if (_detached.Contains(key) || !_planner.TryGetItem(key, out ScatterItem item) ||
                (_isDestroyed != null && _isDestroyed(key)))
            {
                return;
            }

            float3 world = _config.ToWorld(item.Direction, item.AltitudeM - 0.1f); // sink slightly so slopes never expose the base
            PlanetFrame frame = PlanetFrame.FromUp(item.Direction);
            var root = new GameObject("Scatter_" + item.Shape);
            root.transform.SetParent(transform, false);
            root.transform.position = new Vector3(world.x, world.y, world.z);
            // Local Y is already the planet's up here (LookRotation above), so Vector3.up is only the local yaw axis.
            root.transform.rotation = Quaternion.LookRotation((Vector3)frame.North, (Vector3)frame.Up) *
                                      Quaternion.AngleAxis(item.YawDeg, Vector3.up);
            Build(root.transform, item, _looks[item.KindIndex]);
            objects.Add(root);
        }

        private void Build(Transform parent, in ScatterItem item, Look look)
        {
            float size = item.SizeM;
            // Presentation-only variety from the cell key: which mesh, and a little girth. The rule layer's size, yaw
            // and collider are untouched, so the same item blocks the same way whatever it looks like.
            uint hash = Hash(item.CellKey);
            float girth = 0.9f + ((hash >> 8) & 0xFF) / 255f * 0.24f;
            // Colliders keep the primitive shapes ScatterItem.BlockRadiusM / VolumeL were derived from.
            switch (item.Shape)
            {
                case ScatterShape.Tree:
                    Capsule(parent, size * 0.25f, size * 0.06f, size * 0.5f, 1);
                    break;
                case ScatterShape.Rock:
                    Sphere(parent, size * 0.25f, size * 0.5f);
                    break;
                case ScatterShape.Shrub:
                    Sphere(parent, size * 0.3f, size * 0.3f);
                    break;
                case ScatterShape.Bamboo:
                    Capsule(parent, size * 0.45f, size * 0.18f, size * 0.9f, 1);
                    break;
                case ScatterShape.Log:
                    // Lying along local X, like the mesh.
                    Capsule(parent, size * 0.08f, size * 0.1f, size * 0.8f, 0);
                    break;
                case ScatterShape.Cactus:
                    Capsule(parent, size * 0.5f, size * 0.12f, size, 1);
                    break;
                case ScatterShape.Mushroom:
                    Capsule(parent, size * 0.35f, size * 0.08f, size * 0.7f, 1);
                    break;
                case ScatterShape.Spire:
                    Capsule(parent, size * 0.5f, size * 0.2f, size, 1);
                    break;
                case ScatterShape.OreVein:
                    Sphere(parent, size * 0.2f, size * 0.4f);
                    break;
                case ScatterShape.Crystal:
                {
                    BoxCollider shaft = Block<BoxCollider>(parent, new Vector3(0f, size * 0.5f, 0f), Quaternion.Euler(0f, 45f, 8f));
                    shaft.size = new Vector3(size * 0.28f, size, size * 0.28f);
                    break;
                }

                // REQ: destructibles.md#碰撞 — grass, ferns, reeds and flowers never block (ScatterItem.Blocks).
            }

            Mesh mesh = MeshFor(look.Archetype, hash);
            // Rocks keep the rule layer's size on every axis; everything that grows varies a little in girth.
            Vector3 scale = item.Shape == ScatterShape.Rock || item.Shape == ScatterShape.OreVein
                ? Vector3.one * size
                : new Vector3(size * girth, size, size * girth);
            Material first = MaterialFor(look.A.Color, look.RoleA, look.A);
            if (mesh.subMeshCount < 2)
            {
                Model(parent, mesh, scale, first);
                return;
            }

            Palette second = look.VariantsB != null ? look.VariantsB[(int)((hash >> 4) % (uint)look.VariantsB.Length)] : look.B;
            Model(parent, mesh, scale, first, MaterialFor(second.Color, look.RoleB, second));
        }

        private static void Capsule(Transform parent, float centreY, float radius, float height, int direction)
        {
            CapsuleCollider capsule = Block<CapsuleCollider>(parent, new Vector3(0f, centreY, 0f), Quaternion.identity);
            capsule.radius = radius;
            capsule.height = height;
            capsule.direction = direction;
        }

        private static void Sphere(Transform parent, float centreY, float radius)
        {
            SphereCollider sphere = Block<SphereCollider>(parent, new Vector3(0f, centreY, 0f), Quaternion.identity);
            sphere.radius = radius;
        }

        // The one collider of an item (destructibles.md#碰撞: only the trunk / body / shaft blocks).
        private static T Block<T>(Transform parent, Vector3 localPos, Quaternion rotation) where T : Collider
        {
            var go = new GameObject("Block");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = rotation;
            return go.AddComponent<T>();
        }

        private static void Model(Transform parent, Mesh mesh, Vector3 scale, params Material[] materials)
        {
            var go = new GameObject("Model");
            go.transform.SetParent(parent, false);
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = materials;
        }

        private Mesh MeshFor(SceneryArchetype archetype, uint hash)
        {
            int variant = (int)(hash % (uint)SceneryMeshes.VariantCount(archetype));
            int id = (int)archetype * 16 + variant;
            if (!_meshes.TryGetValue(id, out Mesh mesh))
            {
                mesh = SceneryMeshes.Build(archetype, variant);
                _meshes[id] = mesh;
            }

            return mesh;
        }

        private Material MaterialFor(Color color, Role role, Palette kind)
        {
            if (!_materials.TryGetValue((color, kind.Top, role), out Material m))
            {
                if (_sceneryShader == null)
                {
                    // Hairline only: vision.md keeps outlines for characters and creatures; scenery stays soft.
                    m = PlanetLook.CreatureMaterial(_shader, color, 0.012f);
                }
                else
                {
                    m = role switch
                    {
                        // Bark's accent is the ground-hugging tone at the root flare; a darker bark reads as damp wood.
                        Role.Bark => PlanetLook.SceneryMaterial(_sceneryShader, color, Tone(color, 0.72f), 0.8f, TreeSway, 0f, 0f),
                        Role.Leaf => PlanetLook.SceneryMaterial(_sceneryShader, color, kind.Top, 0.85f, TreeSway, 0.55f, 0f),
                        Role.Rock => PlanetLook.SceneryMaterial(_sceneryShader, color, kind.Top, 0.8f, 0f, 0f, 0f),
                        Role.Cane => PlanetLook.SceneryMaterial(_sceneryShader, color, Tone(color, 0.6f), 0.7f, CaneSway, 0.2f, 0f),
                        // Accent = the dry tone the blade tips fade to.
                        Role.Blade => PlanetLook.SceneryMaterial(_sceneryShader, color, Color.Lerp(color, kind.Top, 0.6f), 0.8f, GrassSway, 0.6f, 0f),
                        Role.Plume => PlanetLook.SceneryMaterial(_sceneryShader, color, Color.white, 0.5f, GrassSway, 0.8f, 0f),
                        Role.Still => PlanetLook.SceneryMaterial(_sceneryShader, color, kind.Top, 0.8f, 0f, 0.15f, 0f),
                        Role.Bloom => PlanetLook.SceneryMaterial(_sceneryShader, color, kind.Top, 0.9f, kind.Sway, 0.5f, kind.Glow),
                        _ => PlanetLook.SceneryMaterial(_sceneryShader, color, kind.Top, 0.7f, 0f, 0f, kind.Glow),
                    };
                }

                _materials[(color, kind.Top, role)] = m;
            }

            return m;
        }

        // SplitMix64 finaliser: neighbouring cell keys must not pick neighbouring variants.
        private static uint Hash(long key)
        {
            ulong z = (ulong)key + 0x9E3779B97F4A7C15ul;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9ul;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBul;
            return (uint)((z ^ (z >> 31)) >> 16);
        }

        private void CollectCells(float3 up) => _planner.CollectCells(up, RadiusM, _wanted);

        private void Update()
        {
            if (!_configured)
            {
                return;
            }

            float3 focus = _focus();
            bool moved = !math.all(math.isfinite(_lastFocus)) || math.distance(focus, _lastFocus) > RefreshMoveM;
            if (!moved && Time.time < _nextRefresh)
            {
                return;
            }

            _lastFocus = focus;
            _nextRefresh = Time.time + RefreshSeconds;
            Refresh(focus);
        }

        private void Refresh(float3 focus)
        {
            float3 up = _config.ToDirection(focus);
            CollectCells(up);

            _stale.Clear();
            foreach (long key in _live.Keys)
            {
                if (!_wanted.Contains(key))
                {
                    _stale.Add(key);
                }
            }

            foreach (long key in _stale)
            {
                foreach (GameObject go in _live[key])
                {
                    Destroy(go);
                }

                _live.Remove(key);
            }

            foreach (long key in _wanted)
            {
                if (!_live.ContainsKey(key))
                {
                    BuildCell(key);
                }
            }
        }

        private void OnDestroy()
        {
            foreach (Material m in _materials.Values)
            {
                if (m != null)
                {
                    Destroy(m);
                }
            }

            foreach (Mesh mesh in _meshes.Values)
            {
                if (mesh != null)
                {
                    Destroy(mesh);
                }
            }
        }

        private static Color Tone(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, 1f);

        private static Color C(int rgb) =>
            new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);
    }
}
