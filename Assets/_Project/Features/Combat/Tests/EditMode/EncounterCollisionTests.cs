using System.Collections.Generic;
using System.IO;
using Cosmimera.Core.Contracts;
using Cosmimera.GameData;
using Cosmimera.Planet;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Cosmimera.Combat.Tests
{
    // REQ: docs/design/combat.md#實體碰撞 (D0-W2, decided 2026-11-10; radii provisional, Q-D2).
    public sealed class EncounterCollisionTests
    {
        private sealed class FlatSurface : IEncounterSurface
        {
            public float GroundAltitude(float3 direction) => 0f;
        }

        private const float R = 640f;
        private const int Dt = 50;
        // Positions are float world coordinates (ulp ≈ 6e-5 m at R = 640 m); 5 mm leaves room for re-grounding.
        private const float ArcTolM = 0.005f;
        private readonly List<Object> _created = new List<Object>();
        private PlanetConfig _planet;
        private CombatBalance _balance;
        private static readonly AiCommand Free = new AiCommand(AiCommandKind.Free, CombatAi.NoTarget);

        [SetUp]
        public void SetUp()
        {
            _planet = new PlanetConfig(R, float3.zero);
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
            // Timid: never engages on its own, so the only motion besides separation is wandering (paused at spawn).
            c.Set("line_" + id, CreatureStage.Juvenile, size, AiStyle.Charge, Temperament.Timid, null, 0,
                new List<EvolutionMaterial>(), new int[10], new List<ElementValue> { new ElementValue(Element.Fire, 15) },
                new CreatureStats(25, 25, 25, 25), m, null, "{}", false);
            return c;
        }

        private static float3 AtMeters(float meters)
        {
            float a = meters / R;
            return math.normalize(new float3(math.sin(a), math.cos(a), 0f));
        }

        private float3 FarPlayer => _planet.ToWorld(AtMeters(300f), 0f);

        private float Arc(float3 a, float3 b)
        {
            float3 da = _planet.ToDirection(a);
            float3 db = _planet.ToDirection(b);
            return math.atan2(math.length(math.cross(da, db)), math.dot(da, db)) * R;
        }

        private float BodyM(string key) => (int)BalanceFixture.BalanceJson()["ai"]["body_mm"][key] * 0.001f;

        [Test]
        public void Collision_radius_reads_balance_per_size_and_boss_scales_it()
        {
            var e = new Encounter(_planet, _balance, new FlatSurface(), 1);
            CreatureSizeClass[] sizes =
            {
                CreatureSizeClass.XS, CreatureSizeClass.S, CreatureSizeClass.M, CreatureSizeClass.L, CreatureSizeClass.XL,
            };
            string[] keys = { "xs", "s", "m", "l", "xl" };
            for (int i = 0; i < sizes.Length; i++)
            {
                EncounterUnit u = e.AddCreature(EncounterSide.Wild, Creature("c" + i, sizes[i]), null, AtMeters(30f * i), 10);
                Assert.That(u.CollisionRadiusM, Is.EqualTo(BodyM(keys[i])).Within(1e-5f), keys[i]);
            }

            EncounterUnit boss = e.AddBoss(Creature("boss", CreatureSizeClass.L), null, AtMeters(200f), 10);
            Assert.That(boss.CollisionRadiusM,
                Is.EqualTo(BodyM("l") * Encounter.BossSizePermille / 1000f).Within(1e-5f));
        }

        [Test]
        public void Collision_tuning_rejects_out_of_range_radii()
        {
            Assert.Throws<ContractException>(() => _ = new CollisionTuning(0, 1, 1, 1, 1, 1), "xs 0");
            Assert.Throws<ContractException>(() => _ = new CollisionTuning(1, 1, 1, 1, 10001, 1), "xl too big");
            Assert.Throws<ContractException>(() => _ = new CollisionTuning(1, 1, 1, 1, 1, 0), "character 0");
            var t = new CollisionTuning(1, 2, 3, 4, 10000, 5);
            Assert.That(t.BodyMm(CreatureSizeClass.XS), Is.EqualTo(1));
            Assert.That(t.BodyMm(CreatureSizeClass.S), Is.EqualTo(2));
            Assert.That(t.BodyMm(CreatureSizeClass.M), Is.EqualTo(3));
            Assert.That(t.BodyMm(CreatureSizeClass.L), Is.EqualTo(4));
            Assert.That(t.BodyMm(CreatureSizeClass.XL), Is.EqualTo(10000));
            Assert.That(t.CharacterMm, Is.EqualTo(5));
            Assert.Throws<ContractException>(() => t.BodyMm((CreatureSizeClass)99));
        }

        [Test]
        public void Missing_body_mm_section_fails_loudly()
        {
            JObject json = BalanceFixture.BalanceJson();
            ((JObject)json["ai"]).Remove("body_mm");
            Assert.Throws<ContractException>(() => CombatBalance.FromJson(json.ToString()));
        }

        [Test]
        public void Overlapping_pair_separates_to_touching_in_one_tick()
        {
            var e = new Encounter(_planet, _balance, new FlatSurface(), 7);
            EncounterUnit a = e.AddCreature(EncounterSide.Wild, Creature("a", CreatureSizeClass.M), null, AtMeters(0f), 10);
            EncounterUnit b = e.AddCreature(EncounterSide.Wild, Creature("b", CreatureSizeClass.M), null, AtMeters(0.3f), 10);
            float3 midBefore = math.normalize(_planet.ToDirection(a.Position) + _planet.ToDirection(b.Position));

            e.Tick(Dt, Dt, FarPlayer, Free, CombatAi.NoTarget);

            Assert.That(Arc(a.Position, b.Position), Is.EqualTo(a.CollisionRadiusM + b.CollisionRadiusM).Within(ArcTolM));
            // Equal footprints yield equally: the midpoint stays put.
            float3 midAfter = math.normalize(_planet.ToDirection(a.Position) + _planet.ToDirection(b.Position));
            Assert.That(math.atan2(math.length(math.cross(midBefore, midAfter)), math.dot(midBefore, midAfter)) * R,
                Is.LessThan(ArcTolM));
            Assert.That(e.PlayerPush, Is.EqualTo(float3.zero));
        }

        [Test]
        public void Units_stay_on_the_ground_with_tangent_forward_after_a_push()
        {
            var e = new Encounter(_planet, _balance, new FlatSurface(), 7);
            EncounterUnit a = e.AddCreature(EncounterSide.Wild, Creature("a", CreatureSizeClass.L), null, AtMeters(0f), 10);
            EncounterUnit b = e.AddCreature(EncounterSide.Wild, Creature("b", CreatureSizeClass.S), null, AtMeters(0.5f), 10);

            e.Tick(Dt, Dt, FarPlayer, Free, CombatAi.NoTarget);

            foreach (EncounterUnit u in new[] { a, b })
            {
                Assert.That(math.length(u.Position), Is.EqualTo(R + u.BodyRadiusM).Within(1e-3f));
                Assert.That(math.dot(u.Forward, _planet.ToDirection(u.Position)), Is.EqualTo(0f).Within(1e-4f));
                Assert.That(math.length(u.Forward), Is.EqualTo(1f).Within(1e-4f));
            }
        }

        [Test]
        public void Separated_pair_is_untouched()
        {
            var e = new Encounter(_planet, _balance, new FlatSurface(), 7);
            EncounterUnit a = e.AddCreature(EncounterSide.Wild, Creature("a", CreatureSizeClass.M), null, AtMeters(0f), 10);
            EncounterUnit b = e.AddCreature(EncounterSide.Wild, Creature("b", CreatureSizeClass.M), null, AtMeters(1.5f), 10);
            float3 pa = a.Position;
            float3 pb = b.Position;

            e.Tick(Dt, Dt, FarPlayer, Free, CombatAi.NoTarget);

            Assert.That(a.Position, Is.EqualTo(pa));
            Assert.That(b.Position, Is.EqualTo(pb));
        }

        [Test]
        public void Coincident_spawns_still_separate_deterministically()
        {
            float3[] Run()
            {
                var e = new Encounter(_planet, _balance, new FlatSurface(), 3);
                EncounterUnit a = e.AddCreature(EncounterSide.Wild, Creature("a", CreatureSizeClass.M), null, AtMeters(5f), 10);
                EncounterUnit b = e.AddCreature(EncounterSide.Wild, Creature("b", CreatureSizeClass.M), null, AtMeters(5f), 10);
                e.Tick(Dt, Dt, FarPlayer, Free, CombatAi.NoTarget);
                Assert.That(Arc(a.Position, b.Position), Is.EqualTo(a.CollisionRadiusM + b.CollisionRadiusM).Within(ArcTolM));
                return new[] { a.Position, b.Position };
            }

            float3[] first = Run();
            float3[] second = Run();
            Assert.That(second[0], Is.EqualTo(first[0]));
            Assert.That(second[1], Is.EqualTo(first[1]));
        }

        [Test]
        public void Larger_footprint_yields_less()
        {
            var e = new Encounter(_planet, _balance, new FlatSurface(), 7);
            EncounterUnit big = e.AddCreature(EncounterSide.Wild, Creature("big", CreatureSizeClass.XL), null, AtMeters(0f), 10);
            EncounterUnit small = e.AddCreature(EncounterSide.Wild, Creature("small", CreatureSizeClass.XS), null,
                AtMeters(0.5f), 10);
            float3 bigBefore = big.Position;
            float3 smallBefore = small.Position;

            e.Tick(Dt, Dt, FarPlayer, Free, CombatAi.NoTarget);

            float bigMoved = Arc(bigBefore, big.Position);
            float smallMoved = Arc(smallBefore, small.Position);
            float rb = big.CollisionRadiusM;
            float rs = small.CollisionRadiusM;
            Assert.That(bigMoved, Is.LessThan(smallMoved));
            // Split by inverse footprint: moved_big / moved_small == rs² / rb².
            Assert.That(bigMoved / smallMoved, Is.EqualTo(rs * rs / (rb * rb)).Within(0.01f));
        }

        [Test]
        public void Character_overlap_is_reported_as_player_push_and_the_unit_yields_its_share()
        {
            var e = new Encounter(_planet, _balance, new FlatSurface(), 7);
            EncounterUnit wild = e.AddCreature(EncounterSide.Wild, Creature("w", CreatureSizeClass.M), null,
                AtMeters(0.3f), 10);
            float3 wildBefore = wild.Position;
            float3 feet = _planet.ToWorld(AtMeters(0f), 0f);

            e.Tick(Dt, Dt, feet, Free, CombatAi.NoTarget);

            float3 pushed = feet + e.PlayerPush;
            float character = BodyM("character");
            Assert.That(math.lengthsq(e.PlayerPush), Is.GreaterThan(0f));
            Assert.That(math.length(pushed), Is.EqualTo(math.length(feet)).Within(1e-3f), "push keeps altitude");
            Assert.That(Arc(pushed, wild.Position), Is.EqualTo(character + wild.CollisionRadiusM).Within(ArcTolM));
            // The character's footprint is smaller than an M body, so it yields more.
            Assert.That(Arc(feet, pushed), Is.GreaterThan(Arc(wildBefore, wild.Position)));
            // It is pushed away from the unit (towards negative arc on this meridian).
            Assert.That(math.dot(e.PlayerPush, _planet.ToDirection(wild.Position) - _planet.ToDirection(feet)),
                Is.LessThan(0f));
        }

        [Test]
        public void Inactive_units_are_not_bodies()
        {
            var e = new Encounter(_planet, _balance, new FlatSurface(), 7);
            EncounterUnit wild = e.AddCreature(EncounterSide.Wild, Creature("w", CreatureSizeClass.M), null, AtMeters(0f), 10);
            // A player-side creature that was never summoned (Fielded == false) overlaps exactly.
            EncounterUnit benched = e.AddCreature(EncounterSide.Player, Creature("p", CreatureSizeClass.M), null,
                AtMeters(0.1f), 10);
            float3 wildBefore = wild.Position;
            float3 benchedBefore = benched.Position;
            Assert.That(benched.IsActive, Is.False);

            e.Tick(Dt, Dt, FarPlayer, Free, CombatAi.NoTarget);

            Assert.That(wild.Position, Is.EqualTo(wildBefore));
            Assert.That(benched.Position, Is.EqualTo(benchedBefore));
        }

        [Test]
        public void Crowd_overlap_vanishes_within_a_few_ticks()
        {
            var e = new Encounter(_planet, _balance, new FlatSurface(), 11);
            var units = new List<EncounterUnit>();
            for (int i = 0; i < 6; i++)
            {
                units.Add(e.AddCreature(EncounterSide.Wild, Creature("c" + i, CreatureSizeClass.M), null,
                    AtMeters(0.1f * i), 10));
            }

            // Spawn pauses last >= 2 s (wander_pause_min_ms = 40 ticks), so thirty ticks isolate separation from wandering.
            for (int t = 1; t <= 30; t++)
            {
                e.Tick(t * Dt, Dt, FarPlayer, Free, CombatAi.NoTarget);
            }

            for (int i = 0; i < units.Count; i++)
            {
                for (int j = i + 1; j < units.Count; j++)
                {
                    Assert.That(Arc(units[i].Position, units[j].Position),
                        Is.GreaterThanOrEqualTo(units[i].CollisionRadiusM + units[j].CollisionRadiusM - ArcTolM),
                        $"{i}-{j}");
                }
            }
        }
    }
}
