using System.Collections.Generic;
using Cosmimera.GameData;
using Unity.Mathematics;

namespace Cosmimera.WorldGen
{
    /// <summary>Role of a spawned wild creature.</summary>
    /// <remarks>Pre: none. Post: stable values. Side effects: none.</remarks>
    public enum SpawnRole
    {
        /// <summary>Ordinary wild creature; capturable after knock-out (Q-M2).</summary>
        Normal = 0,
        /// <summary>Elite: a quest target, one tier above its region.</summary>
        Elite = 1,
        /// <summary>The planet boss in its lair (planet-world.md#目標與結束條件).</summary>
        Boss = 2,
    }

    /// <summary>Main-quest step kinds (Terra copy: quest-templates.json).</summary>
    /// <remarks>Pre: none. Post: stable values. Side effects: none.</remarks>
    public enum QuestStepKind
    {
        /// <summary>Walk into a region.</summary>
        ReachPoi = 0,
        /// <summary>Knock out Count wild creatures whose main element is Element.</summary>
        DefeatWild = 1,
        /// <summary>Knock out the elite spawn TargetSpawn.</summary>
        DefeatElite = 2,
        /// <summary>Capture any one wild creature.</summary>
        CaptureOne = 3,
        /// <summary>Knock out the boss spawn TargetSpawn.</summary>
        DefeatBoss = 4,
    }

    /// <summary>One region of the planet's region graph.</summary>
    /// <remarks>Pre: produced by RunPlanner. Post: immutable. Side effects: none.</remarks>
    public sealed class RunRegion
    {
        internal RunRegion(int index, float3 center, int[] neighbors, string themeId, string affixId, int tier, int hops)
        {
            Index = index;
            Center = center;
            Neighbors = neighbors;
            ThemeId = themeId;
            AffixId = affixId;
            Tier = tier;
            Hops = hops;
        }

        /// <summary>Index in RunPlan.Regions.</summary>
        /// <remarks>Pre: none. Post: >= 0. Side effects: none.</remarks>
        public int Index { get; }

        /// <summary>Unit direction of the Voronoi site.</summary>
        /// <remarks>Pre: none. Post: unit length. Side effects: none.</remarks>
        public float3 Center { get; }

        /// <summary>Adjacent regions (Gabriel graph of the sites), ascending.</summary>
        /// <remarks>Pre: none. Post: symmetric relation; graph connected. Side effects: none.</remarks>
        public IReadOnlyList<int> Neighbors { get; }

        /// <summary>Region theme id (region-themes.json).</summary>
        /// <remarks>Pre: none. Post: non-null. Side effects: none.</remarks>
        public string ThemeId { get; }

        /// <summary>Region affix id (affixes.json), or null.</summary>
        /// <remarks>Pre: none. Post: null for the landing region; non-null for the boss region. Side effects: none.</remarks>
        public string AffixId { get; }

        /// <summary>Danger tier: 0 juvenile, 1 adolescent, 2 adult (by graph distance from landing).</summary>
        /// <remarks>Pre: none. Post: 0..2; landing 0, boss 2. Side effects: none.</remarks>
        public int Tier { get; }

        /// <summary>Graph distance (edges) from the landing region.</summary>
        /// <remarks>Pre: none. Post: >= 0. Side effects: none.</remarks>
        public int Hops { get; }
    }

    /// <summary>A wild creature placement.</summary>
    /// <remarks>Pre: produced by RunPlanner. Post: immutable. Side effects: none.</remarks>
    public readonly struct RunSpawn
    {
        internal RunSpawn(int region, int group, float3 direction, string creatureId, SpawnRole role)
        {
            Region = region;
            Group = group;
            Direction = direction;
            CreatureId = creatureId;
            Role = role;
        }

        /// <summary>Region index.</summary>
        /// <remarks>Pre: none. Post: valid index. Side effects: none.</remarks>
        public int Region { get; }

        /// <summary>Pack index within the plan (members of one pack share it).</summary>
        /// <remarks>Pre: none. Post: >= 0. Side effects: none.</remarks>
        public int Group { get; }

        /// <summary>Unit direction of the spawn point.</summary>
        /// <remarks>Pre: none. Post: unit length. Side effects: none.</remarks>
        public float3 Direction { get; }

        /// <summary>Creature form id.</summary>
        /// <remarks>Pre: none. Post: an id from the wild pool. Side effects: none.</remarks>
        public string CreatureId { get; }

        /// <summary>Role.</summary>
        /// <remarks>Pre: none. Post: defined. Side effects: none.</remarks>
        public SpawnRole Role { get; }
    }

    /// <summary>One step of the main quest chain.</summary>
    /// <remarks>Pre: produced by RunPlanner. Post: immutable. Side effects: none.</remarks>
    public readonly struct RunQuestStep
    {
        internal RunQuestStep(QuestStepKind kind, int region, int count, Element element, int targetSpawn)
        {
            Kind = kind;
            Region = region;
            Count = count;
            Element = element;
            TargetSpawn = targetSpawn;
        }

        /// <summary>Step kind.</summary>
        /// <remarks>Pre: none. Post: defined. Side effects: none.</remarks>
        public QuestStepKind Kind { get; }

        /// <summary>Region the step points the player to.</summary>
        /// <remarks>Pre: none. Post: on the main route. Side effects: none.</remarks>
        public int Region { get; }

        /// <summary>Required count (DefeatWild) or 1.</summary>
        /// <remarks>Pre: none. Post: >= 1. Side effects: none.</remarks>
        public int Count { get; }

        /// <summary>Element filter for DefeatWild; otherwise unused.</summary>
        /// <remarks>Pre: none. Post: defined. Side effects: none.</remarks>
        public Element Element { get; }

        /// <summary>Spawn index for DefeatElite/DefeatBoss, else -1.</summary>
        /// <remarks>Pre: none. Post: valid index or -1. Side effects: none.</remarks>
        public int TargetSpawn { get; }
    }

    /// <summary>Content-independent ids the planner chooses from (loaded from the D0 copy files).</summary>
    /// <remarks>
    /// Pre: build through the constructor. Post: immutable copies. Side effects: none.
    /// </remarks>
    public sealed class RunPlanVocabulary
    {
        /// <summary>Creates the vocabulary.</summary>
        /// <remarks>
        /// Pre: themeIds non-empty; affixIds contains every "affix_&lt;element&gt;_surge" for the 10 elements plus ≥ 1
        ///      generic affix; chainIds non-empty; no null entries.
        /// Post: lists copied. Side effects: allocates.
        /// </remarks>
        public RunPlanVocabulary(IReadOnlyList<string> themeIds, IReadOnlyList<string> genericAffixIds,
            IReadOnlyList<string> chainIds)
        {
            Core.Contracts.Contract.Requires(themeIds != null && themeIds.Count > 0, "themeIds must not be empty");
            Core.Contracts.Contract.Requires(genericAffixIds != null && genericAffixIds.Count > 0, "need a generic affix");
            Core.Contracts.Contract.Requires(chainIds != null && chainIds.Count > 0, "chainIds must not be empty");
            ThemeIds = new List<string>(themeIds);
            GenericAffixIds = new List<string>(genericAffixIds);
            ChainIds = new List<string>(chainIds);
        }

        /// <summary>Region theme ids.</summary>
        /// <remarks>Pre: none. Post: non-empty. Side effects: none.</remarks>
        public IReadOnlyList<string> ThemeIds { get; }

        /// <summary>Affix ids that are not element surges.</summary>
        /// <remarks>Pre: none. Post: non-empty. Side effects: none.</remarks>
        public IReadOnlyList<string> GenericAffixIds { get; }

        /// <summary>Main-quest chain ids.</summary>
        /// <remarks>Pre: none. Post: non-empty. Side effects: none.</remarks>
        public IReadOnlyList<string> ChainIds { get; }

        /// <summary>The surge affix id of an element.</summary>
        /// <remarks>Pre: element defined. Post: "affix_&lt;lowercase element&gt;_surge". Side effects: allocates a string.</remarks>
        public static string SurgeAffixId(Element element) => "affix_" + element.ToString().ToLowerInvariant() + "_surge";
    }

    /// <summary>Everything that makes one planet run, generated from (seed, planet type, GeneratorVersion).</summary>
    /// <remarks>
    /// Pre: produced by <see cref="RunPlanner.Generate"/>. Post: immutable; same inputs → same plan and same Hash.
    /// Side effects: none.
    /// </remarks>
    public sealed class RunPlan
    {
        internal RunPlan(ulong seed, string planetTypeId, string chainId, RunRegion[] regions, int landing, int boss,
            int[] route, RunSpawn[] spawns, RunQuestStep[] quest, float3 landingDirection)
        {
            Seed = seed;
            PlanetTypeId = planetTypeId;
            ChainId = chainId;
            Regions = regions;
            LandingRegion = landing;
            BossRegion = boss;
            MainRoute = route;
            Spawns = spawns;
            Quest = quest;
            LandingDirection = landingDirection;
        }

        /// <summary>Planet seed.</summary>
        /// <remarks>Pre: none. Post: as given. Side effects: none.</remarks>
        public ulong Seed { get; }

        /// <summary>Planet type id.</summary>
        /// <remarks>Pre: none. Post: non-null. Side effects: none.</remarks>
        public string PlanetTypeId { get; }

        /// <summary>Main-quest chain id.</summary>
        /// <remarks>Pre: none. Post: one of the vocabulary's chain ids. Side effects: none.</remarks>
        public string ChainId { get; }

        /// <summary>Regions.</summary>
        /// <remarks>Pre: none. Post: Regions[i].Index == i. Side effects: none.</remarks>
        public IReadOnlyList<RunRegion> Regions { get; }

        /// <summary>Where the player lands.</summary>
        /// <remarks>Pre: none. Post: valid index. Side effects: none.</remarks>
        public int LandingRegion { get; }

        /// <summary>Boss lair region: the region farthest (in edges) from landing.</summary>
        /// <remarks>Pre: none. Post: valid index != LandingRegion. Side effects: none.</remarks>
        public int BossRegion { get; }

        /// <summary>Shortest region path landing → boss, inclusive.</summary>
        /// <remarks>Pre: none. Post: consecutive entries are neighbors; first landing, last boss. Side effects: none.</remarks>
        public IReadOnlyList<int> MainRoute { get; }

        /// <summary>All wild placements, normal then elite then boss.</summary>
        /// <remarks>Pre: none. Post: exactly one Boss spawn. Side effects: none.</remarks>
        public IReadOnlyList<RunSpawn> Spawns { get; }

        /// <summary>Main quest steps in order, ending with DefeatBoss.</summary>
        /// <remarks>Pre: none. Post: non-empty; last Kind == DefeatBoss. Side effects: none.</remarks>
        public IReadOnlyList<RunQuestStep> Quest { get; }

        /// <summary>Exact landing point (unit direction) inside the landing region.</summary>
        /// <remarks>Pre: none. Post: unit length. Side effects: none.</remarks>
        public float3 LandingDirection { get; }
    }
}
