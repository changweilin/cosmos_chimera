using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Cosmimera.GameData;
using Cosmimera.WorldGen;
using Unity.Mathematics;
using UnityEngine;

namespace Cosmimera.Combat.Tests
{
    /// <summary>
    /// Test-side bridge from "this def's habit" to a wild individual: tests build a def once and add it many times, so
    /// the habit is remembered per def and turned into a fixed, zero-variance bounded_random individual.
    /// </summary>
    internal static class TestWild
    {
        private static readonly ConditionalWeakTable<CreatureDef, object> Habits = new ConditionalWeakTable<CreatureDef, object>();
        private static readonly Dictionary<Habit, NicheDef> Niches = new Dictionary<Habit, NicheDef>();

        // The pre-P3-31 wander radii per habit (3/4/6/8/10/12 m), so wander-sensitive tests keep their geometry.
        private static int RadiusMm(Habit habit) => habit switch
        {
            Habit.Avoidant => 3000,
            Habit.Vigilant => 4000,
            Habit.Tolerant => 6000,
            Habit.Territorial => 8000,
            Habit.Aggressive => 10000,
            _ => 12000,
        };

        /// <summary>Speed (permille), move budget and pause (ms) of every test niche; zero SDs make legs predictable.</summary>
        internal const int SpeedPermille = 400;
        internal const int MoveMs = 60000;
        internal const int PauseMs = 3500;

        internal static CreatureDef WithHabit(CreatureDef def, Habit habit)
        {
            Habits.Remove(def);
            Habits.Add(def, habit);
            return def;
        }

        internal static Habit HabitOf(CreatureDef def)
            => Habits.TryGetValue(def, out object h) ? (Habit)h
                : throw new InvalidOperationException($"{def.Id}: build the def through a helper that calls TestWild.WithHabit");

        internal static NicheDef Niche(Habit habit, WanderMode mode = WanderMode.BoundedRandom, int radiusMm = -1)
        {
            if (mode == WanderMode.BoundedRandom && radiusMm < 0 && Niches.TryGetValue(habit, out NicheDef cached) && cached != null)
            {
                return cached;
            }

            NicheDef n = ScriptableObject.CreateInstance<NicheDef>();
            n.SetId("niche_test_" + habit.ToString().ToLowerInvariant() + "_" + mode.ToString().ToLowerInvariant());
            int r = radiusMm >= 0 ? radiusMm : RadiusMm(habit);
            bool unbounded = mode == WanderMode.UnboundedRandom;
            n.Set("test", new List<string> { "grassland" }, new List<HabitWeight> { new HabitWeight(habit, 1) }, mode,
                unbounded ? 0 : r, unbounded ? r : 0,
                new WanderDistribution(SpeedPermille, 0, 0, 1, 1000),
                new WanderDistribution(MoveMs, 0, 0, 1, MoveMs),
                new WanderDistribution(PauseMs, 0, 0, 1, 60000));
            if (mode == WanderMode.BoundedRandom && radiusMm < 0)
            {
                Niches[habit] = n;
            }

            return n;
        }

        internal static WildIndividual Individual(Habit habit, ulong key = 0)
            => new WildIndividual(Niche(habit), habit, SpeedPermille, MoveMs, PauseMs, key);

        internal static WildIndividual For(CreatureDef def, ulong key = 0) => Individual(HabitOf(def), key);

        internal static EncounterUnit AddWildT(this Encounter e, CreatureDef def, MoveDef partnerMove, float3 direction, int level)
            => e.AddWild(def, partnerMove, direction, level, def == null ? null : For(def, (ulong)e.Units.Count));

        internal static EncounterUnit AddBossT(this Encounter e, CreatureDef def, MoveDef partnerMove, float3 direction, int level)
            => e.AddBoss(def, partnerMove, direction, level, def == null ? null : For(def, (ulong)e.Units.Count));
    }
}
