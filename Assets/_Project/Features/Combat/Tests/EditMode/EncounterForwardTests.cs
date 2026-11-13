using System.Collections.Generic;
using System.IO;
using Cosmimera.GameData;
using Cosmimera.Planet;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Cosmimera.Combat.Tests
{
    // REQ: tech-debt D0-E4 · docs/reviews/D0-E4-proposal-sol.md acceptance item 3. TangentTowardTests pins the helper;
    // this drives the same degenerate geometry through the real Encounter.Tick so the fallback and attack paths, and
    // MoveGeometry consuming the result, are covered together (the original bug surfaced only inside Tick).
    public sealed class EncounterForwardTests
    {
        private sealed class FlatSurface : IEncounterSurface
        {
            public float GroundAltitude(float3 direction) => 0f;

            public bool IsSea(float3 direction) => false;

            public bool IsBlocked(float3 direction, float clearanceM) => false;
        }

        private readonly List<Object> _created = new List<Object>();
        private CombatBalance _balance;
        private static readonly AiCommand Free = new AiCommand(AiCommandKind.Free, CombatAi.NoTarget);

        [SetUp]
        public void SetUp()
        {
            _balance = CombatBalance.FromJson(File.ReadAllText(BalanceFixture.RepoPath("Data", "balance.json")));
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in _created)
            {
                Object.DestroyImmediate(o);
            }

            _created.Clear();
        }

        private CreatureDef Creature(string id, CreatureSizeClass size)
        {
            var m = ScriptableObject.CreateInstance<MoveDef>();
            _created.Add(m);
            m.SetId("bite_" + id);
            m.Set(Element.Fire, MoveKind.Damage, 40, null, 0, 0, 1500, 100, RangeClass.Melee, MoveShape.Front, 2000,
                MoveMovement.None, BurstOrigin.Caster);
            var c = ScriptableObject.CreateInstance<CreatureDef>();
            _created.Add(c);
            c.SetId(id);
            c.Set("line_" + id, CreatureStage.Juvenile, size, AiStyle.Charge,
                Temperament.Fierce, null, 0, new List<EvolutionMaterial>(), new int[10],
                new List<ElementValue> { new ElementValue(Element.Fire, 15) }, new CreatureStats(25, 25, 25, 25), m, null,
                "{}", false);
            return c;
        }

        private static void AssertTangentUnit(EncounterUnit u, float3 center, string when)
        {
            double3 up = math.normalize((double3)u.Position - (double3)center);
            Assert.That(math.all(math.isfinite(u.Forward)), Is.True, when + ": finite");
            Assert.That(math.abs(math.length(u.Forward) - 1f), Is.LessThan(1e-5f), when + ": unit");
            Assert.That(math.abs(math.dot((double3)u.Forward, up)), Is.LessThan(1e-5), when + ": tangent at Position");
        }

        // Same direction, different body radius → the target sits straight above/below the caster (no bearing).
        // Directions are the R5-07 / D0-E4 witnesses, where the old float projection left Forward 0.0135 off-plane.
        [TestCase(640f, 0f, 0f, 0f, 413.63202f, 96.05307f, -479.63602f, CreatureSizeClass.S, CreatureSizeClass.XL)]
        [TestCase(640f, 0f, 0f, 0f, 392.2076f, 506.4875f, 3.457169f, CreatureSizeClass.XL, CreatureSizeClass.XS)]
        [TestCase(1280f, 16000f, -16000f, 8000f, 0.31f, 0.8f, -0.51f, CreatureSizeClass.M, CreatureSizeClass.L)]
        public void Stacked_units_keep_a_tangent_forward_through_real_ticks(float radius, float cx, float cy, float cz,
            float dx, float dy, float dz, CreatureSizeClass partySize, CreatureSizeClass wildSize)
        {
            var center = new float3(cx, cy, cz);
            var planet = new PlanetConfig(radius, center);
            var e = new Encounter(planet, _balance, new FlatSurface(), 7);
            float3 dir = math.normalize(new float3(dx, dy, dz));
            EncounterUnit party = e.AddCreature(EncounterSide.Player, Creature("p", partySize), null, dir, 10);
            EncounterUnit wild = e.AddCreature(EncounterSide.Wild, Creature("w", wildSize), null, dir, 10);
            e.Field(party.Id, dir, false, 0);
            // Player 30 m away: outside melee, inside the fierce territory, so the first tick runs the no-target
            // (fallback) branch for the wild before it engages and both sides take the attack branch.
            float3 player = planet.ToWorld(math.normalize(dir * radius + PlanetFrame.FromUp(dir).East * 30f), 0f);

            AssertTangentUnit(party, center, "spawn party");
            AssertTangentUnit(wild, center, "spawn wild");
            int casts = 0;
            long now = 0;
            for (int t = 0; t < 40 && !wild.Combatant.IsFainted && !party.Combatant.IsFainted; t++)
            {
                now += 50;
                e.Tick(now, 50, player, Free, party.Id);
                foreach (EncounterEvent ev in e.Events)
                {
                    if (ev.Kind == EncounterEventKind.Cast)
                    {
                        casts++;
                    }
                }

                AssertTangentUnit(party, center, "tick " + t + " party");
                AssertTangentUnit(wild, center, "tick " + t + " wild");
            }

            Assert.That(casts, Is.GreaterThan(0), "MoveGeometry consumed the stacked forward at least once");
        }
    }
}
