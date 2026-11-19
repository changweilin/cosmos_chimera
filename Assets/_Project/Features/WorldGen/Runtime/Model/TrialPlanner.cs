using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.Core.Random;
using Cosmimera.GameData;

namespace Cosmimera.WorldGen
{
    /// <summary>
    /// Picks one terrain trial's dungeon boss and trial master team for a planet.
    /// REQ: docs/design/trainer-battles.md#地形試煉 — the boss is the immunity-holding line at the planet's boss tier
    /// (moon adolescent, planet adult, supergiant adult + overload; capturable and evolvable like any line), and the
    /// master's team is element specialists of the same form and level.
    /// </summary>
    /// <remarks>
    /// Determinism: the boss is the lowest id among matching forms (no draw); the team draws from Pcg32(seed,
    /// TrialStreamBase + terrain index) over candidates sorted by id, so pool order never matters.
    /// </remarks>
    public static class TrialPlanner
    {
        /// <summary>Bump when any output for the same inputs changes.</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const int GeneratorVersion = 1;

        private const ulong TrialStreamBase = 0x54524C00UL;

        /// <summary>Plans the trial of <paramref name="terrainId"/> on a planet of <paramref name="size"/>.</summary>
        /// <remarks>
        /// Pre: terrainId names an advanced terrain appearing on size; pool non-null without null entries; level 1..100.
        /// Post: true iff pool holds a creature with the terrain's immunity at TrialStageFor(size) and at least one other
        ///       line of that element and form; then plan satisfies every TerrainTrialPlan postcondition, the team
        ///       excludes the boss line and repeats a species only when fewer candidates than TrialTeamSize exist.
        ///       False leaves plan null. Same inputs → same plan.
        /// Side effects: allocates.
        /// </remarks>
        public static bool TryPlan(ulong seed, string terrainId, PlanetSize size, IReadOnlyList<CreatureDef> pool, int level,
            out TerrainTrialPlan plan)
        {
            Contract.Requires(AdvancedTerrains.TryGet(terrainId, out AdvancedTerrainSpec spec), "unknown advanced terrain");
            Contract.Requires(AdvancedTerrains.AppearsOn(spec, size), "terrain does not appear on this planet size");
            Contract.Requires(pool != null, "pool must not be null");
            Contract.Requires(level >= 1 && level <= 100, "level must be 1..100");
            plan = null;
            CreatureStage stage = AdvancedTerrains.TrialStageFor(size);

            CreatureDef boss = null;
            var candidates = new List<CreatureDef>();
            foreach (CreatureDef c in pool)
            {
                Contract.Requires(c != null, "pool must not contain null");
                if (c.Stage != stage)
                {
                    continue;
                }

                if (c.FieldUtility == spec.Immunity &&
                    (boss == null || string.CompareOrdinal(c.Id, boss.Id) < 0))
                {
                    boss = c;
                }
            }

            if (boss == null)
            {
                return false;
            }

            foreach (CreatureDef c in pool)
            {
                if (c.Stage == stage && c.LineId != boss.LineId && c.Mastery.Count > 0 &&
                    RunPlanner.MainElement(c) == spec.Element)
                {
                    candidates.Add(c);
                }
            }

            if (candidates.Count == 0)
            {
                return false;
            }

            candidates.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            var rng = new Pcg32(seed, TrialStreamBase + (ulong)AdvancedTerrains.IndexOf(spec.Id));
            var team = new TrainerTeamMember[spec.TrialTeamSize];
            var remaining = new List<CreatureDef>(candidates);
            for (int i = 0; i < team.Length; i++)
            {
                if (remaining.Count == 0)
                {
                    remaining.AddRange(candidates);
                }

                int k = (int)rng.NextUInt((uint)remaining.Count);
                team[i] = new TrainerTeamMember(remaining[k].Id, level);
                remaining.RemoveAt(k);
            }

            plan = new TerrainTrialPlan(spec, stage, AdvancedTerrains.TrialBossOverloadsOn(size), boss.Id, level, team,
                LookSeed(spec.TrialMasterId));
            return true;
        }

        // FNV-1a of the character id: fixed per master across planets and runs (trainer-battles.md#外觀佔位).
        private static ulong LookSeed(string characterId)
        {
            ulong h = 14695981039346656037UL;
            unchecked
            {
                foreach (char c in characterId)
                {
                    h = (h ^ c) * 1099511628211UL;
                }
            }

            return h;
        }
    }
}
