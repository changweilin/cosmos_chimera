using System;
using System.Collections.Generic;
using System.Text;
using Cosmimera.Core.Contracts;
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
        public const int GeneratorVersion = 1;

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

            var spawns = new RunSpawn[plan.Spawns.Count];
            var placed = new List<Placed>(spawns.Length);
            for (int i = 0; i < spawns.Length; i++)
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
                spawns[i] = new RunSpawn(s.Region, s.Group, dir, s.CreatureId, s.Role, ind);
            }

            return plan.WithSpawns(spawns);
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
            }

            return h;
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
