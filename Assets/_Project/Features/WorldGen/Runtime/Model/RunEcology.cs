using System;
using System.Collections.Generic;
using System.Text;
using Cosmimera.Core.Contracts;
using Cosmimera.Core.Random;
using Cosmimera.GameData;
using Unity.Mathematics;

namespace Cosmimera.WorldGen
{
    /// <summary>
    /// Gives every spawn of a run plan its individual ecology (niche → habit → wander means) and keeps the activity
    /// areas of different species sharing a niche apart. REQ: docs/design/creature-ecology.md (P3-31, Q-C62).
    /// </summary>
    /// <remarks>
    /// Runs after <see cref="RunPlanner.Generate"/> with its own version so RunPlanner's snapshots stay valid. Each spawn's
    /// draws use a stream derived from (seed, spawn index) only, so one spawn's draw never shifts another's.
    /// </remarks>
    public static class RunEcology
    {
        /// <summary>Bump when any output for the same inputs changes (snapshot tests pin it).</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const int GeneratorVersion = 2;

        /// <summary>Low-probability chance (%) for an eligible pair to form a wild confrontation.</summary>
        /// <remarks>Pre: none. Post: constant in 1..100. Side effects: none.</remarks>
        public const int ConfrontationChancePercent = 15;

        /// <summary>Maximum wild confrontations generated per planet.</summary>
        /// <remarks>Pre: none. Post: constant >= 1. Side effects: none.</remarks>
        public const int MaxConfrontationsPerPlan = 2;

        private const float SameNicheAdjacencyM = 30f;
        private const float TrophicEncounterM = 25f;
        private const float StandoffHalfSpanM = 2.5f;
        private const float ReserveStepM = 1.6f;

        /// <summary>Returns <paramref name="plan"/> with every spawn's Individual set and exclusion-relocated directions.</summary>
        /// <remarks>
        /// Pre: plan from RunPlanner.Generate with the same wildPool (every CreatureId resolves); planetRadius &gt; 0;
        ///      habitatAt deterministic, mapping unit directions to landform ids; isLand null (all land) or the predicate
        ///      the plan was generated with.
        /// Post: same spawn count, order, regions, groups, ids and roles; Individual non-null; for every pair of spawns
        ///       with the same niche and different line ids the activity caps (Niche.ActivityRadiusMm) do not overlap; a
        ///       moved spawn is the first ring point (RunPlanner.NearestLand order) that is land, allowed by its niche and
        ///       clear of every earlier same-niche other-line cap. Same inputs → same output.
        /// Side effects: allocates. Throws ContractException in every build when a form has no candidate for its habitat
        ///          or no clear point exists (data / configuration errors, creature-ecology.md).
        /// </remarks>
        public static RunPlan Apply(RunPlan plan, IReadOnlyList<CreatureDef> wildPool, float planetRadius,
            Func<float3, string> habitatAt, Func<float3, bool> isLand)
        {
            Contract.Requires(plan != null && wildPool != null && habitatAt != null, "plan, wildPool and habitatAt are required");
            Contract.Requires(planetRadius > 0f, "planetRadius must be > 0");
            var byId = new Dictionary<string, CreatureDef>(StringComparer.Ordinal);
            foreach (CreatureDef c in wildPool)
            {
                byId[c.Id] = c;
            }

            var spawns = new List<RunSpawn>(plan.Spawns.Count + 8);
            var placed = new List<Placed>(plan.Spawns.Count);
            for (int i = 0; i < plan.Spawns.Count; i++)
            {
                RunSpawn s = plan.Spawns[i];
                Contract.Requires(byId.ContainsKey(s.CreatureId), "plan spawn not in wildPool");
                CreatureDef form = byId[s.CreatureId];
                WildIndividual ind = WildIndividual.Draw(form, habitatAt(s.Direction), WildIndividual.KeyFor(plan.Seed, (ulong)i));
                float capRad = ind.Niche.ActivityRadiusMm * 0.001f / planetRadius;
                float3 dir = s.Direction;
                if (Conflicts(placed, dir, capRad, ind.Niche, form.LineId))
                {
                    NicheDef niche = ind.Niche;
                    string line = form.LineId;
                    bool Clear(float3 p) => (isLand == null || isLand(p)) && niche.Allows(habitatAt(p)) &&
                        !Conflicts(placed, p, capRad, niche, line);
                    if (!RunPlanner.FirstInRings(dir, Clear, out dir))
                    {
                        throw new ContractException(new StringBuilder("no clear spawn point for ").Append(form.Id)
                            .Append(" (").Append(niche.Id).Append(") near spawn ").Append(i).ToString());
                    }
                }

                placed.Add(new Placed(dir, capRad, ind.Niche, form.LineId));
                spawns.Add(new RunSpawn(s.Region, s.Group, dir, s.CreatureId, s.Role, ind));
            }

            RunConfrontation[] confrontations = BuildConfrontations(plan, spawns, placed, byId, planetRadius, habitatAt, isLand);
            return plan.WithSpawns(spawns.ToArray(), confrontations);
        }

        /// <summary>Order-sensitive digest of the ecology part of a plan (niche, habit, means, direction) for snapshots.</summary>
        /// <remarks>Pre: plan non-null with every Individual set. Post: same plan → same value. Side effects: none.</remarks>
        public static ulong Hash(RunPlan plan)
        {
            Contract.Requires(plan != null, "plan must not be null");
            ulong h = RunPlanner.Hash(plan);
            foreach (RunSpawn s in plan.Spawns)
            {
                Contract.Requires(s.Individual != null, "spawn has no individual");
                h = Mix(h, s.Individual.Niche.Id);
                h = Mix(h, ((ulong)(int)s.Individual.Habit << 48) ^ ((ulong)s.Individual.SpeedPermilleMean << 32) ^
                    ((ulong)s.Individual.MoveMsMean << 16) ^ (ulong)s.Individual.PauseMsMean);
                h = Mix(h, (ulong)(s.ConfrontationId + 1));
                h = Mix(h, (ulong)(s.ConfrontationSide + 1));
                h = Mix(h, (ulong)s.ConfrontationOrder);
            }

            foreach (RunConfrontation c in plan.Confrontations)
            {
                h = Mix(h, (ulong)c.Id);
                h = Mix(h, (ulong)(int)c.Kind);
            }

            return h;
        }

        private static RunConfrontation[] BuildConfrontations(RunPlan plan, List<RunSpawn> spawns, List<Placed> placed,
            Dictionary<string, CreatureDef> byId, float planetRadius, Func<float3, string> habitatAt, Func<float3, bool> isLand)
        {
            var rng = new Pcg32(plan.Seed, 'C');
            var confrontations = new List<RunConfrontation>(MaxConfrontationsPerPlan);
            int baseCount = spawns.Count;
            float sameMarginRad = SameNicheAdjacencyM / planetRadius;
            float trophicMarginRad = TrophicEncounterM / planetRadius;

            for (int i = 0; i < baseCount && confrontations.Count < MaxConfrontationsPerPlan; i++)
            {
                if (spawns[i].Role != SpawnRole.Normal || spawns[i].ConfrontationId >= 0)
                {
                    continue;
                }

                for (int j = i + 1; j < baseCount && confrontations.Count < MaxConfrontationsPerPlan; j++)
                {
                    if (spawns[j].Role != SpawnRole.Normal || spawns[j].ConfrontationId >= 0)
                    {
                        continue;
                    }

                    NicheDef ni = placed[i].Niche;
                    NicheDef nj = placed[j].Niche;
                    float distRad = math.acos(math.clamp(math.dot(placed[i].Dir, placed[j].Dir), -1f, 1f));
                    bool sameNiche = ni == nj && !string.Equals(placed[i].Line, placed[j].Line, StringComparison.Ordinal) &&
                        distRad <= placed[i].CapRad + placed[j].CapRad + sameMarginRad;
                    bool trophic = !sameNiche &&
                        ni.TrophicGroup == TrophicGroup.FoodWeb && nj.TrophicGroup == TrophicGroup.FoodWeb &&
                        ni.TrophicTier != nj.TrophicTier &&
                        distRad <= placed[i].CapRad + placed[j].CapRad + trophicMarginRad;
                    if (!sameNiche && !trophic)
                    {
                        continue;
                    }

                    if (rng.NextUInt() % 100u >= (uint)ConfrontationChancePercent)
                    {
                        continue;
                    }

                    ConfrontationKind kind = sameNiche ? ConfrontationKind.SameNiche : ConfrontationKind.Trophic;
                    float3 center = ComputeConfrontationCenter(placed[i], placed[j], sameNiche, isLand);
                    int confId = confrontations.Count;
                    confrontations.Add(new RunConfrontation(confId, kind, center));

                    float3 axis = TangentToward(center, placed[j].Dir);
                    float3 side = math.normalize(math.cross(center, axis));

                    PlaceConfrontationSide(plan.Seed, confId, 0, i, center, -axis, side, spawns, byId, planetRadius, habitatAt, isLand);
                    PlaceConfrontationSide(plan.Seed, confId, 1, j, center, axis, side, spawns, byId, planetRadius, habitatAt, isLand);
                    break;
                }
            }

            return confrontations.ToArray();
        }

        private static float3 ComputeConfrontationCenter(in Placed a, in Placed b, bool sameNiche, Func<float3, bool> isLand)
        {
            float t = sameNiche ? a.CapRad / math.max(1e-6f, a.CapRad + b.CapRad) : 0.5f;
            float3 sum = math.lerp(a.Dir, b.Dir, t);
            float3 center = math.lengthsq(sum) > 1e-8f ? math.normalize(sum) : a.Dir;
            if (isLand != null && !isLand(center) && RunPlanner.FirstInRings(center, isLand, out float3 land))
            {
                center = land;
            }

            return center;
        }

        private static float3 TangentToward(float3 origin, float3 target)
        {
            float3 d = target - origin * math.dot(origin, target);
            if (math.lengthsq(d) > 1e-8f)
            {
                return math.normalize(d);
            }

            float3 fallback = math.abs(origin.y) < 0.9f ? new float3(0f, 1f, 0f) : new float3(1f, 0f, 0f);
            return math.normalize(math.cross(fallback, origin));
        }

        private static void PlaceConfrontationSide(ulong seed, int confId, int sideIndex, int leadSpawnIndex,
            float3 center, float3 outward, float3 perp, List<RunSpawn> spawns, Dictionary<string, CreatureDef> byId,
            float planetRadius, Func<float3, string> habitatAt, Func<float3, bool> isLand)
        {
            RunSpawn lead = spawns[leadSpawnIndex];
            int count = lead.Individual.Niche.ConfrontationGroupSize;
            float3 leadPos = OffsetOnSphere(center, outward, perp, StandoffHalfSpanM, 0f, planetRadius, isLand);
            spawns[leadSpawnIndex] = new RunSpawn(lead.Region, lead.Group, leadPos, lead.CreatureId, lead.Role,
                lead.Individual, confId, sideIndex, 0);

            CreatureDef form = byId[lead.CreatureId];
            for (int k = 1; k < count; k++)
            {
                float backM = StandoffHalfSpanM + k * ReserveStepM;
                float latM = (k % 2 == 1 ? 1f : -1f) * 1.2f;
                float3 pos = OffsetOnSphere(center, outward, perp, backM, latM, planetRadius, isLand);
                int idx = spawns.Count;
                WildIndividual ind = WildIndividual.Draw(form, habitatAt(pos), WildIndividual.KeyFor(seed, (ulong)idx));
                spawns.Add(new RunSpawn(lead.Region, lead.Group, pos, lead.CreatureId, SpawnRole.Normal,
                    ind, confId, sideIndex, k));
            }
        }

        private static float3 OffsetOnSphere(float3 origin, float3 forward, float3 right, float forwardM, float rightM,
            float planetRadius, Func<float3, bool> isLand)
        {
            float3 p = math.normalize(origin + (forward * forwardM + right * rightM) / planetRadius);
            if (isLand != null && !isLand(p) && RunPlanner.FirstInRings(p, isLand, out float3 land))
            {
                return land;
            }

            return p;
        }

        private static bool Conflicts(List<Placed> placed, float3 dir, float capRad, NicheDef niche, string line)
        {
            foreach (Placed p in placed)
            {
                if (p.Niche == niche && !string.Equals(p.Line, line, StringComparison.Ordinal) &&
                    math.acos(math.clamp(math.dot(p.Dir, dir), -1f, 1f)) < p.CapRad + capRad)
                {
                    return true;
                }
            }

            return false;
        }

        private static ulong Mix(ulong h, string s)
        {
            foreach (char c in s)
            {
                h = Mix(h, c);
            }

            return Mix(h, 0xFFUL);
        }

        private static ulong Mix(ulong h, ulong v)
        {
            unchecked
            {
                for (int i = 0; i < 8; i++)
                {
                    h ^= (v >> (i * 8)) & 0xFFUL;
                    h *= 1099511628211UL;
                }
            }

            return h;
        }

        private readonly struct Placed
        {
            public Placed(float3 dir, float capRad, NicheDef niche, string line)
            {
                Dir = dir;
                CapRad = capRad;
                Niche = niche;
                Line = line;
            }

            public float3 Dir { get; }
            public float CapRad { get; }
            public NicheDef Niche { get; }
            public string Line { get; }
        }
    }
}
