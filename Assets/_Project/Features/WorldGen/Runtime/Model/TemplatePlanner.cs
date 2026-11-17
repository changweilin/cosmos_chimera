using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.Core.Random;
using Cosmimera.Planet;
using Unity.Mathematics;

namespace Cosmimera.WorldGen
{
    /// <summary>
    /// Chooses which procedural landforms a planet uses and where they sit. REQ: planet-world.md#1
    /// (骨架層程序化地貌; user 2026-11-11 replaced the real-world geo-templates), docs/roadmap/demo.md (P3-03 D0).
    /// </summary>
    /// <remarks>
    /// Pure and deterministic: Pcg32(seed, 'T'). Landforms whose terrain tags overlap the planet type's come first, so
    /// a fjord planet is mostly fjords. Centers come from an evenly spaced, randomly rotated lattice, which keeps
    /// stamps apart without rejection sampling.
    /// </remarks>
    public static class TemplatePlanner
    {
        /// <summary>|y| of a stamp centre above which the stamp lies in the cold band and becomes a cold landform.</summary>
        /// <remarks>
        /// Pre: none. Post: constant in (0, 1); the same latitude where forest stands turn cold (planet-world.md#林型).
        /// Side effects: none.
        /// </remarks>
        public const float ColdBandY = 0.7f;

        // REQ: planet-world.md#1-骨架層程序化地貌 (親和標籤; 暫定) — the planet terrain tag a landform that is not
        // itself a planet tag also answers to, so it joins that planet type's preferred landforms.
        private static readonly Dictionary<string, string> Affinity = new Dictionary<string, string>
        {
            ["grassland"] = "forest",
            ["swamp"] = "river_valley",
            ["desert"] = "canyon",
            ["mountain"] = "plateau",
            ["tundra"] = "ice_field",
            ["glacier"] = "ice_field",
            ["cirque"] = "ice_field",
            ["moraine"] = "ice_field",
            ["karst"] = "cave",
            ["basalt_terrace"] = "volcano",
            ["granite_domes"] = "plateau",
            ["slate_cliffs"] = "eternal_night",
        };

        // REQ: planet-world.md#寒帶地形 (Q-W18: 冰晶星＋所有星球的寒帶) — what a cold-band stamp is redrawn from.
        private static readonly string[] ColdKinds = { "tundra", "glacier", "cirque", "moraine", "ice_field" };

        /// <summary>Default angular half extent of a stamp (rad); ≈ 288 m at R = 640.</summary>
        /// <remarks>Pre: none. Post: constant within TemplatePlacement's (0, 0.6]. Side effects: none.</remarks>
        public const float DefaultHalfAngleRad = 0.45f;

        /// <summary>Template count for a planet size (planet-world.md: 3–8 per planet).</summary>
        /// <remarks>Pre: none. Post: 4 for small/medium, 6 for large. Side effects: none.</remarks>
        public static int CountFor(GameData.PlanetSize size) => size >= GameData.PlanetSize.Planet ? 6 : 4;

        /// <summary>Synthesizes and places the landform stamps of one planet.</summary>
        /// <remarks>
        /// Pre: config valid; typeTags non-null, no null entries.
        /// Post: CountFor(size) stamps; stamp i is LandformSynth.Generate(Kinds(...)[i], seed, i) at Choose's i-th
        ///       placement. Same inputs → same stamps.
        /// Side effects: allocates persistent native memory owned by the result (Dispose required).
        /// </remarks>
        public static PlanetTemplates Build(in PlanetConfig config, ulong seed, IReadOnlyList<string> typeTags,
            GameData.PlanetSize size)
        {
            Contract.Requires(config.IsValid, "config must be valid");
            Contract.Requires(typeTags != null, "typeTags must not be null");
            string[] kinds = Kinds(seed, typeTags, CountFor(size), out List<TemplatePlacement> chosen);
            var sources = new List<TemplateSource>(chosen.Count);
            var placements = new List<TemplatePlacement>(chosen.Count);
            for (int i = 0; i < chosen.Count; i++)
            {
                TemplatePlacement c = chosen[i];
                // One grid per stamp (not per kind): two volcanoes on one planet must not be copies.
                sources.Add(LandformSynth.Generate(kinds[i], seed, i));
                placements.Add(new TemplatePlacement(i, c.Center, c.RotationRad, c.HalfAngleRad));
            }

            return PlanetTemplates.Create(config, sources, placements);
        }

        /// <summary>The landform kind of each stamp <see cref="Build"/> places, and where.</summary>
        /// <remarks>
        /// REQ: planet-world.md#1-骨架層程序化地貌, #寒帶地形.
        /// Pre: typeTags non-null, no null entries; 1 &lt;= count &lt;= 8.
        /// Post: count kinds of <see cref="LandformSynth.Kinds"/>, one per placement (placement.Source indexes an
        ///       internal catalog and is not meaningful to callers). A kind is preferred when it, or the planet tag it
        ///       has an affinity to, is in typeTags. The four glacial kinds (tundra, glacier, cirque, moraine) appear
        ///       only on planets that prefer them and on stamps whose centre has |y| > <see cref="ColdBandY"/>; such
        ///       a stamp is always a cold kind (those four or ice_field) or a fjord. Same inputs → same output.
        /// Side effects: allocates the results.
        /// </remarks>
        public static string[] Kinds(ulong seed, IReadOnlyList<string> typeTags, int count,
            out List<TemplatePlacement> placements)
        {
            Contract.Requires(typeTags != null, "typeTags must not be null");
            IReadOnlyList<string> all = LandformSynth.Kinds;
            var catalogKinds = new List<string>(all.Count);
            var catalog = new List<IReadOnlyList<string>>(all.Count);
            foreach (string kind in all)
            {
                IReadOnlyList<string> tags = Affinity.TryGetValue(kind, out string tag) ? new[] { kind, tag } : new[] { kind };
                // Glacial landforms never fill a spare slot at random: off an ice world they belong to the cold band.
                if (IsGlacial(kind) && !Overlaps(tags, typeTags))
                {
                    continue;
                }

                catalogKinds.Add(kind);
                catalog.Add(tags);
            }

            placements = Choose(seed, typeTags, catalog, count);
            // Own stream (P for polar), one draw per stamp whether used or not: a stamp's cold kind does not depend
            // on which other stamps are cold, and the T stream is untouched.
            var polar = new Pcg32(seed, 'P');
            var kinds = new string[placements.Count];
            for (int i = 0; i < kinds.Length; i++)
            {
                string kind = catalogKinds[placements[i].Source];
                string cold = ColdKinds[(int)polar.NextUInt((uint)ColdKinds.Length)];
                // Float compare on a lattice centre: a peer one ulp off disagrees only for a centre within an ulp of
                // the band edge, the same exposure the lattice rotation already has.
                bool inColdBand = math.abs(placements[i].Center.y) > ColdBandY;
                kinds[i] = inColdBand && kind != "fjord" && System.Array.IndexOf(ColdKinds, kind) < 0 ? cold : kind;
            }

            return kinds;
        }

        private static bool IsGlacial(string kind) =>
            kind == "tundra" || kind == "glacier" || kind == "cirque" || kind == "moraine";

        /// <summary>Canopy obstacle density of the landform under each point of a planet built by <see cref="Build"/>.</summary>
        /// <remarks>
        /// Pre: templates is a live <see cref="Build"/> result and outlives every call of the returned function;
        ///      baseDensity finite in [0, 1] (the planet type's own density, used between stamps).
        /// Post: the function maps a unit direction to a value in [0, 1]: baseDensity outside every stamp, the stamp's
        ///       landform <see cref="TerrainProfile.ScatterDensity"/> in its core, and a blend of the two over the same
        ///       outer band where the stamp's height blends into the noise. Deterministic.
        /// Side effects: allocates the function and one float per stamp.
        /// </remarks>
        public static System.Func<float3, float> ScatterDensity(PlanetTemplates templates, float baseDensity)
        {
            Contract.Requires(templates != null, "templates must not be null");
            Contract.Requires(math.isfinite(baseDensity) && baseDensity >= 0f && baseDensity <= 1f,
                "baseDensity must be in [0, 1]");
            TemplateView view = templates.View;
            var perStamp = new float[templates.StampIds.Count];
            for (int i = 0; i < perStamp.Length; i++)
            {
                perStamp[i] = TerrainProfiles.For(SurfaceTerrainId(LandformSynth.KindOf(templates.StampIds[i]))).ScatterDensity;
            }

            return dir => view.TryGetStamp(dir, out int i, out float w)
                ? math.lerp(baseDensity, perStamp[i], w)
                : baseDensity;
        }

        /// <summary>Surface terrain profile under each ground direction, for movement and local weather.</summary>
        /// <remarks>
        /// Pre: templates may be null (no landforms); otherwise a live Build result that outlives the returned function.
        ///      The returned function accepts finite unit directions. planetTypeId may be null or unknown.
        /// Post: the strongest contributing stamp supplies its general surface profile; outside all stamps (or without
        ///       templates), returns the planet type's profile. Volcano/cave stamps do not become advanced dungeons.
        /// Side effects: allocates a callback and one cached profile per stamp; queries allocate nothing.
        /// </remarks>
        public static System.Func<float3, TerrainProfile> SurfaceProfile(PlanetTemplates templates, string planetTypeId)
        {
            TerrainProfile baseline = TerrainProfiles.ForPlanetType(planetTypeId);
            TemplateView view = templates != null ? templates.View : default;
            var profiles = new TerrainProfile[templates != null ? templates.StampIds.Count : 0];
            for (int i = 0; i < profiles.Length; i++)
            {
                profiles[i] = TerrainProfiles.For(SurfaceTerrainId(LandformSynth.KindOf(templates.StampIds[i])));
            }

            return direction =>
            {
                Contract.Requires(math.all(math.isfinite(direction)) && math.abs(math.lengthsq(direction) - 1f) <= 2e-3f,
                    "direction must be finite and unit length");
                return view.TryGetStamp(direction, out int i, out _) ? profiles[i] : baseline;
            };
        }

        /// <summary>Landform kind (a LandformSynth id) a wild creature born at each direction lives in.</summary>
        /// <remarks>
        /// REQ: creature-ecology.md#生成順序 (出生點環境).
        /// Pre: templates null (no stamps) or a live <see cref="Build"/> result that outlives the returned function;
        ///      planetTypeId may be null or unknown. The function accepts finite unit directions.
        /// Post: the strongest stamp's landform where its blend weight is &gt;= 0.5; elsewhere the planet type's own
        ///       surface landform (grassland for an unknown type). Always one of <see cref="LandformSynth.Kinds"/>.
        /// Side effects: allocates the callback and one string reference per stamp; queries allocate nothing.
        /// </remarks>
        public static System.Func<float3, string> HabitatAt(PlanetTemplates templates, string planetTypeId)
        {
            string baseKind = LandformOfTerrain(TerrainProfiles.ForPlanetType(planetTypeId).Id);
            TemplateView view = templates != null ? templates.View : default;
            var perStamp = new string[templates != null ? templates.StampIds.Count : 0];
            for (int i = 0; i < perStamp.Length; i++)
            {
                perStamp[i] = LandformSynth.KindOf(templates.StampIds[i]);
            }

            // Half weight: the blend band is where the stamp is still fading in; its core decides the habitat.
            return dir => perStamp.Length > 0 && view.TryGetStamp(dir, out int i, out float w) && w >= 0.5f
                ? perStamp[i]
                : baseKind;
        }

        // Inverse of SurfaceTerrainId for the planet types' base surfaces.
        private static string LandformOfTerrain(string terrainId) => terrainId switch
        {
            "volcano_foothills" => "volcano",
            "cavern_outer" => "cave",
            _ => terrainId,
        };

        /// <summary>Scatter biome (landform species table) under each point of a planet built by <see cref="Build"/>.</summary>
        /// <remarks>
        /// REQ: planet-world.md#散佈 (Q-W15: 依地貌印章選種).
        /// Pre: templates is a live <see cref="Build"/> result and outlives every call of the returned function;
        ///      planetTypeId may be null or unknown (as <see cref="TerrainProfiles.ForPlanetType"/>).
        /// Post: the function maps (unit direction, roll) to a ScatterBiomes biome index: the planet type's own surface
        ///       landform outside every stamp, the stamp's landform in its core, and over the stamp's outer blend band
        ///       one or the other in proportion to the blend weight, chosen by roll — so species interleave across the
        ///       edge instead of switching along a line. Deterministic.
        /// Side effects: allocates the function and one int per stamp.
        /// </remarks>
        public static System.Func<float3, uint, int> ScatterBiome(PlanetTemplates templates, string planetTypeId)
        {
            Contract.Requires(templates != null, "templates must not be null");
            TemplateView view = templates.View;
            int baseBiome = ScatterBiomes.BiomeOf(TerrainProfiles.ForPlanetType(planetTypeId).Id);
            var perStamp = new int[templates.StampIds.Count];
            for (int i = 0; i < perStamp.Length; i++)
            {
                perStamp[i] = ScatterBiomes.BiomeOf(LandformSynth.KindOf(templates.StampIds[i]));
            }

            // 16-bit compare: the weight is a float, but a peer that rounds it one ulp differently disagrees on at
            // most one roll in 65536, the same exposure the altitude-band and slope tests already have.
            return (dir, roll) => view.TryGetStamp(dir, out int i, out float w) && (roll & 0xFFFFu) < (uint)(w * 65536f)
                ? perStamp[i]
                : baseBiome;
        }

        /// <summary>Bedrock of the ground between stamps and of each stamp of a planet built by <see cref="Build"/>.</summary>
        /// <remarks>
        /// REQ: planet-world.md#地質變體 (P3-24 a: 只換岩石散佈與地表配色).
        /// Pre: templates is a <see cref="Build"/> result (may be disposed); planetTypeId may be null or unknown.
        /// Post: baseGeology is drawn for the planet type's own surface landform; perStamp[i] for stamp i's landform
        ///       (ScatterGeology.For); Pcg32(seed, 'G'), base first, so a stamp's bedrock does not depend on how many
        ///       stamps follow it. Same inputs → same output.
        /// Side effects: allocates the array.
        /// </remarks>
        public static Geology[] Geologies(PlanetTemplates templates, ulong seed, string planetTypeId, out Geology baseGeology)
        {
            Contract.Requires(templates != null, "templates must not be null");
            var rng = new Pcg32(seed, 'G');
            baseGeology = ScatterGeology.For(ScatterBiomes.BiomeOf(TerrainProfiles.ForPlanetType(planetTypeId).Id), rng.NextUInt());
            var perStamp = new Geology[templates.StampIds.Count];
            for (int i = 0; i < perStamp.Length; i++)
            {
                perStamp[i] = ScatterGeology.For(ScatterBiomes.BiomeOf(LandformSynth.KindOf(templates.StampIds[i])), rng.NextUInt());
            }

            return perStamp;
        }

        /// <summary>Bedrock under each point of a planet built by <see cref="Build"/>.</summary>
        /// <remarks>
        /// Pre: templates is a live <see cref="Build"/> result and outlives every call of the returned function;
        ///      perStamp has one defined entry per stamp (<see cref="Geologies"/>); baseGeology defined.
        /// Post: the function maps (unit direction, roll) to baseGeology outside every stamp, the stamp's geology in its
        ///       core, and one or the other over the outer blend band — for the same roll, the same side
        ///       <see cref="ScatterBiome"/> picks, so a cell's species table and bedrock always belong together.
        /// Side effects: allocates the function.
        /// </remarks>
        public static System.Func<float3, uint, Geology> ScatterGeologyAt(PlanetTemplates templates,
            IReadOnlyList<Geology> perStamp, Geology baseGeology)
        {
            Contract.Requires(templates != null, "templates must not be null");
            Contract.Requires(perStamp != null && perStamp.Count == templates.StampIds.Count, "one geology per stamp");
            TemplateView view = templates.View;
            return (dir, roll) => view.TryGetStamp(dir, out int i, out float w) && (roll & 0xFFFFu) < (uint)(w * 65536f)
                ? perStamp[i]
                : baseGeology;
        }

        // REQ: planet-world.md#1-骨架層程序化地貌 — on the open surface a volcano or cave stamp is the general landform
        // around it (volcano_foothills, cavern_outer); `volcano` and `cave` profiles are the advanced dungeons.
        private static string SurfaceTerrainId(string kind) => kind switch
        {
            "volcano" => "volcano_foothills",
            "cave" => "cavern_outer",
            _ => kind,
        };

        /// <summary>Places <paramref name="count"/> templates.</summary>
        /// <remarks>
        /// Pre: catalogTags non-empty, no null entries; typeTags non-null; 1 &lt;= count &lt;= 8.
        /// Post: count placements; Source indexes catalogTags; matching templates are used before non-matching ones,
        ///       and no template repeats until every candidate was used once. Same inputs → same output.
        /// Side effects: allocates the result.
        /// </remarks>
        public static List<TemplatePlacement> Choose(ulong seed, IReadOnlyList<string> typeTags,
            IReadOnlyList<IReadOnlyList<string>> catalogTags, int count)
        {
            Contract.Requires(catalogTags != null && catalogTags.Count > 0 && typeTags != null, "catalog and tags required");
            Contract.Requires(count >= 1 && count <= 8, "count must be 1..8");
            var rng = new Pcg32(seed, 'T');
            var matching = new List<int>();
            var others = new List<int>();
            for (int i = 0; i < catalogTags.Count; i++)
            {
                Contract.Requires(catalogTags[i] != null, "catalog tags must not be null");
                (Overlaps(catalogTags[i], typeTags) ? matching : others).Add(i);
            }

            Shuffle(matching, ref rng);
            Shuffle(others, ref rng);
            matching.AddRange(others);

            quaternion rot = quaternion.AxisAngle(math.normalize(new float3(rng.NextFloat() - 0.5f, rng.NextFloat() - 0.5f,
                rng.NextFloat() - 0.5f) + new float3(0f, 0f, 1e-3f)), rng.NextFloat() * 2f * math.PI);
            float golden = math.PI * (3f - math.sqrt(5f));
            var result = new List<TemplatePlacement>(count);
            for (int i = 0; i < count; i++)
            {
                float y = 1f - 2f * (i + 0.5f) / count;
                float r = math.sqrt(1f - y * y);
                float3 c = math.normalize(math.mul(rot, new float3(math.cos(golden * i) * r, y, math.sin(golden * i) * r)));
                result.Add(new TemplatePlacement(matching[i % matching.Count], c, rng.NextFloat() * 2f * math.PI,
                    DefaultHalfAngleRad));
            }

            return result;
        }

        private static bool Overlaps(IReadOnlyList<string> a, IReadOnlyList<string> b)
        {
            foreach (string x in a)
            {
                foreach (string y in b)
                {
                    if (x == y)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static void Shuffle(List<int> list, ref Pcg32 rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = (int)rng.NextUInt((uint)(i + 1));
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
