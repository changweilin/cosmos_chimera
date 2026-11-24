using System;
using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.Core.Random;
using Cosmimera.GameData;
using Cosmimera.WorldGen;
using Unity.Mathematics;

namespace Cosmimera.Lobby
{
    /// <summary>
    /// One crystal rift node spawned on the main world (Tera-raid crystal style random planet portal).
    /// REQ: docs/design/lobby-meta.md#探索與聚落服務.
    /// </summary>
    /// <remarks>
    /// Pre: constructed via <see cref="StarRiftRules"/> or explicit valid parameters.
    /// Post: immutable snapshot of a rift's random planet parameters and spherical placement.
    /// Side effects: none.
    /// </remarks>
    public readonly struct StarRiftEntry
    {
        /// <summary>Zero-based rift slot index in the current refresh epoch (-1 for custom seed entries).</summary>
        /// <remarks>Pre: none. Post: &gt;= -1. Side effects: none.</remarks>
        public int RiftIndex { get; }

        /// <summary>Deterministic planet generation seed.</summary>
        /// <remarks>Pre: none. Post: &gt; 0. Side effects: none.</remarks>
        public ulong Seed { get; }

        /// <summary>Planet type definition id.</summary>
        /// <remarks>Pre: none. Post: non-empty string. Side effects: none.</remarks>
        public string PlanetTypeId { get; }

        /// <summary>Size tier of the target random planet.</summary>
        /// <remarks>Pre: none. Post: defined <see cref="PlanetSize"/>. Side effects: none.</remarks>
        public PlanetSize Size { get; }

        /// <summary>Boss evolution stage on the target random planet.</summary>
        /// <remarks>Pre: none. Post: Juvenile..Adult. Side effects: none.</remarks>
        public CreatureStage BossStage { get; }

        /// <summary>Dominant element controlling the crystal pillar color and material entry cost.</summary>
        /// <remarks>Pre: none. Post: defined <see cref="Element"/>. Side effects: none.</remarks>
        public Element MainElement { get; }

        /// <summary>Full elemental distribution drawn for this rift's planet.</summary>
        /// <remarks>Pre: none. Post: non-null. Side effects: none.</remarks>
        public PlanetElements Elements { get; }

        /// <summary>Unit sphere direction of the rift on the main world.</summary>
        /// <remarks>Pre: none. Post: unit length (or zero for non-placed custom seed entries). Side effects: none.</remarks>
        public float3 Direction { get; }

        /// <summary>True once the player has cleared this rift in the current refresh epoch.</summary>
        /// <remarks>Pre: none. Post: immutable flag. Side effects: none.</remarks>
        public bool Cleared { get; }

        /// <summary>Constructs a star rift entry.</summary>
        /// <remarks>
        /// Pre: seed &gt; 0; planetTypeId non-empty; size/bossStage/mainElement defined; elements non-null.
        /// Post: all properties initialized. Side effects: none.
        /// </remarks>
        public StarRiftEntry(
            int riftIndex,
            ulong seed,
            string planetTypeId,
            PlanetSize size,
            CreatureStage bossStage,
            Element mainElement,
            PlanetElements elements,
            float3 direction,
            bool cleared = false)
        {
            Contract.Requires(riftIndex >= -1, "riftIndex must be >= -1");
            Contract.Requires(seed > 0UL, "seed must be > 0");
            Contract.Requires(!string.IsNullOrEmpty(planetTypeId), "planetTypeId must be non-empty");
            Contract.Requires(Enum.IsDefined(typeof(PlanetSize), size), "size must be defined");
            Contract.Requires(bossStage >= CreatureStage.Juvenile && bossStage <= CreatureStage.Adult, "bossStage must be Juvenile..Adult");
            Contract.Requires(Enum.IsDefined(typeof(Element), mainElement), "mainElement must be defined");
            Contract.Requires(elements != null, "elements must be non-null");

            RiftIndex = riftIndex;
            Seed = seed;
            PlanetTypeId = planetTypeId;
            Size = size;
            BossStage = bossStage;
            MainElement = mainElement;
            Elements = elements;
            Direction = direction;
            Cleared = cleared;
        }

        /// <summary>Returns a copy of this entry with <see cref="Cleared"/> set to <paramref name="cleared"/>.</summary>
        /// <remarks>Pre: none. Post: only Cleared differs. Side effects: none.</remarks>
        public StarRiftEntry WithCleared(bool cleared) =>
            new StarRiftEntry(RiftIndex, Seed, PlanetTypeId, Size, BossStage, MainElement, Elements, Direction, cleared);
    }

    /// <summary>
    /// Deterministic generation and Tera-raid style crystal rules for main-world Star Rifts.
    /// REQ: docs/design/lobby-meta.md#探索與聚落服務.
    /// </summary>
    /// <remarks>
    /// Pre: valid planet types, unlock caps, and non-zero planet radius.
    /// Post: reproducible rift placement and planet roll using Pcg32.
    /// Side effects: none (pure).
    /// </remarks>
    public static class StarRiftRules
    {
        /// <summary>Default number of Star Rifts spawned per refresh epoch on the main world.</summary>
        /// <remarks>Pre: none. Post: constant 8. Side effects: none.</remarks>
        public const int DefaultRiftCount = 8;

        /// <summary>Interaction distance (m) from the player to a Star Rift crystal.</summary>
        /// <remarks>Pre: none. Post: constant 6 m. Side effects: none.</remarks>
        public const float InteractRadiusM = 6f;

        /// <summary>Minimum surface separation (m) from settlement centers and other rifts.</summary>
        /// <remarks>Pre: none. Post: constant 45 m. Side effects: none.</remarks>
        public const float MinClearanceFromSettlementM = 45f;

        /// <summary>Converts <paramref name="size"/> to a 1★–4★ star rating for rift UI and pillar height.</summary>
        /// <remarks>Pre: size is a defined PlanetSize. Post: 1..4. Side effects: none.</remarks>
        public static int StarRating(PlanetSize size)
        {
            Contract.Requires(Enum.IsDefined(typeof(PlanetSize), size), "size must be defined");
            switch (size)
            {
                case PlanetSize.Asteroid: return 1;
                case PlanetSize.Moon: return 2;
                case PlanetSize.Planet: return 3;
                case PlanetSize.Supergiant: return 4;
                default:
                    throw new ArgumentOutOfRangeException(nameof(size), size, "undefined PlanetSize");
            }
        }

        /// <summary>Generates deterministic Star Rift portals scattered across the main world.</summary>
        /// <remarks>
        /// Pre: refreshEpoch &gt;= 0; maxUnlockedSize defined; maxBossStage Juvenile..Adult; planetTypes non-null/non-empty
        ///      with at least one non-cradle planet type; planetRadiusM &gt; 0; count &gt;= 0.
        /// Post: returns list of length count, excluding cradle_planet, respecting maxUnlockedSize &amp; maxBossStage.
        /// Side effects: allocates returned list and PlanetElements instances.
        /// </remarks>
        public static List<StarRiftEntry> GenerateRifts(
            ulong worldSeed,
            int refreshEpoch,
            PlanetSize maxUnlockedSize,
            CreatureStage maxBossStage,
            IReadOnlyList<PlanetTypeDef> planetTypes,
            IReadOnlyList<float3> avoidDirs,
            float planetRadiusM,
            int count = DefaultRiftCount)
        {
            Contract.Requires(refreshEpoch >= 0, "refreshEpoch must be >= 0");
            Contract.Requires(Enum.IsDefined(typeof(PlanetSize), maxUnlockedSize), "maxUnlockedSize must be defined");
            Contract.Requires(maxBossStage >= CreatureStage.Juvenile && maxBossStage <= CreatureStage.Adult,
                "maxBossStage must be Juvenile..Adult");
            Contract.Requires(planetTypes != null && planetTypes.Count > 0, "planetTypes must be non-empty");
            Contract.Requires(planetRadiusM > 0f, "planetRadiusM must be > 0");
            Contract.Requires(count >= 0, "count must be >= 0");

            var selectable = new List<PlanetTypeDef>(planetTypes.Count);
            for (int i = 0; i < planetTypes.Count; i++)
            {
                PlanetTypeDef t = planetTypes[i];
                if (t != null && t.Id != "cradle_planet")
                {
                    selectable.Add(t);
                }
            }

            Contract.Requires(selectable.Count > 0, "planetTypes must include at least one non-cradle type");

            var result = new List<StarRiftEntry>(count);
            if (count == 0)
            {
                return result;
            }

            CreatureStage storyCap = StarSelectRules.MaxBossStageForSize(maxUnlockedSize);
            CreatureStage effectiveMaxStage = (int)maxBossStage < (int)storyCap ? maxBossStage : storyCap;

            ulong epochSalt = unchecked((ulong)refreshEpoch * 0x9E3779B97F4A7C15UL);
            var rng = new Pcg32(worldSeed ^ epochSalt, 'S');
            float minAngle = math.min(0.45f, MinClearanceFromSettlementM / planetRadiusM);
            float minDot = math.cos(minAngle);

            for (int i = 0; i < count; i++)
            {
                PlanetTypeDef chosen = selectable[(int)rng.NextUInt((uint)selectable.Count)];
                ulong seed = ((ulong)rng.NextUInt() << 32) | rng.NextUInt();
                if (seed == 0UL)
                {
                    seed = (ulong)(i + 1);
                }

                CreatureStage stage = BossDraw.Stage(seed, effectiveMaxStage);
                PlanetSize size = ResolveRiftSize(stage, maxUnlockedSize, seed);
                PlanetElements elements = PlanetElementDraw.Draw(seed, size, chosen.ElementBias);
                Element mainElement = elements.Shares[0].Element;
                float3 dir = SampleClearDirection(ref rng, avoidDirs, result, minDot);

                result.Add(new StarRiftEntry(i, seed, chosen.Id, size, stage, mainElement, elements, dir));
            }

            return result;
        }

        /// <summary>Builds a deterministic Star Rift entry from a player-entered custom seed.</summary>
        /// <remarks>
        /// Pre: planetType != null; maxUnlockedSize defined; maxBossStage Juvenile..Adult.
        /// Post: returns true and populates entry iff customSeed &gt; 0 and planetType is not cradle_planet.
        /// Side effects: allocates PlanetElements when true.
        /// </remarks>
        public static bool TryBuildCustomSeedRift(
            ulong customSeed,
            PlanetTypeDef planetType,
            PlanetSize maxUnlockedSize,
            CreatureStage maxBossStage,
            out StarRiftEntry entry)
        {
            Contract.Requires(planetType != null, "planetType must be non-null");
            Contract.Requires(Enum.IsDefined(typeof(PlanetSize), maxUnlockedSize), "maxUnlockedSize must be defined");
            Contract.Requires(maxBossStage >= CreatureStage.Juvenile && maxBossStage <= CreatureStage.Adult,
                "maxBossStage must be Juvenile..Adult");

            if (customSeed == 0UL || planetType.Id == "cradle_planet")
            {
                entry = default;
                return false;
            }

            CreatureStage storyCap = StarSelectRules.MaxBossStageForSize(maxUnlockedSize);
            CreatureStage effectiveMaxStage = (int)maxBossStage < (int)storyCap ? maxBossStage : storyCap;
            CreatureStage stage = BossDraw.Stage(customSeed, effectiveMaxStage);
            PlanetSize size = ResolveRiftSize(stage, maxUnlockedSize, customSeed);
            PlanetElements elements = PlanetElementDraw.Draw(customSeed, size, planetType.ElementBias);
            Element mainElement = elements.Shares[0].Element;

            entry = new StarRiftEntry(-1, customSeed, planetType.Id, size, stage, mainElement, elements, float3.zero);
            return true;
        }

        private static PlanetSize ResolveRiftSize(CreatureStage stage, PlanetSize maxUnlockedSize, ulong seed)
        {
            PlanetSize size = BossDraw.SizeFor(stage);
            if (maxUnlockedSize == PlanetSize.Supergiant && stage == CreatureStage.Adult && (seed & 1UL) == 1UL)
            {
                return PlanetSize.Supergiant;
            }

            return (int)size > (int)maxUnlockedSize ? maxUnlockedSize : size;
        }

        private static float3 SampleClearDirection(
            ref Pcg32 rng,
            IReadOnlyList<float3> avoidDirs,
            List<StarRiftEntry> placed,
            float minDot)
        {
            float3 bestDir = new float3(0f, 0f, 1f);
            float bestWorstDot = 2f;

            for (int attempt = 0; attempt < 64; attempt++)
            {
                float z = rng.NextFloat() * 1.8f - 0.9f;
                float phi = rng.NextFloat() * (2f * math.PI);
                float r = math.sqrt(math.max(0f, 1f - z * z));
                float3 candidate = math.normalize(new float3(r * math.cos(phi), z, r * math.sin(phi)));

                float worstDot = -1f;
                if (avoidDirs != null)
                {
                    for (int a = 0; a < avoidDirs.Count; a++)
                    {
                        float lenSq = math.lengthsq(avoidDirs[a]);
                        if (lenSq <= 1e-8f) continue;
                        float d = math.dot(candidate, avoidDirs[a] / math.sqrt(lenSq));
                        if (d > worstDot) worstDot = d;
                    }
                }

                for (int p = 0; p < placed.Count; p++)
                {
                    float d = math.dot(candidate, placed[p].Direction);
                    if (d > worstDot) worstDot = d;
                }

                if (worstDot <= minDot)
                {
                    return candidate;
                }

                if (worstDot < bestWorstDot)
                {
                    bestWorstDot = worstDot;
                    bestDir = candidate;
                }
            }

            return bestDir;
        }
    }
}
