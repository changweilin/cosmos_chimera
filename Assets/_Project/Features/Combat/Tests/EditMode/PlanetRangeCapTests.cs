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
    // REQ: docs/design/derived-stats.md#星球射程上限 (Q-D1, user 2026-11-11; k = 775‰, h = 1700 mm proposed by SOL).
    public sealed class PlanetRangeCapTests
    {
        private sealed class FlatSurface : IEncounterSurface
        {
            public float GroundAltitude(float3 direction) => 0f;

            public bool IsSea(float3 direction) => false;

            public bool IsBlocked(float3 direction, float clearanceM) => false;
        }

        private const int Dt = 50;
        private readonly List<Object> _created = new List<Object>();
        private CombatBalance _balance;

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

        // Expected values come from SOL's independent BigInteger isqrt (docs/reviews/Q-D1-R320-sol.md §A), not this code.
        // 25597 also pins the rounding order: rounding the horizon to whole mm first gives 25596.
        [TestCase(320f, 25597)]
        [TestCase(640f, 36000)]
        [TestCase(960f, 36000)]
        [TestCase(1280f, 36000)]
        public void Shipped_coefficients_give_the_proposed_caps(float radius, int expected)
        {
            Assert.That(_balance.Horizon.CapMm(radius), Is.EqualTo(expected));
        }

        // SOL: R >= 640 keeps 36 m iff k >= 771.23‰, so 772 is the smallest integer factor and 771 already bites.
        [Test]
        public void Factor_772_is_the_smallest_that_keeps_640_at_the_global_cap()
        {
            Assert.That(new HorizonRangeTuning(772, 1700, 36000).CapMm(640f), Is.EqualTo(36000));
            Assert.That(new HorizonRangeTuning(771, 1700, 36000).CapMm(640f), Is.LessThan(36000));
            Assert.That(new HorizonRangeTuning(772, 1700, 36000).CapMm(320f), Is.EqualTo(25498));
            Assert.That(new HorizonRangeTuning(1000, 1700, 36000).CapMm(320f), Is.EqualTo(33028));
        }

        [Test]
        public void Cap_is_non_decreasing_in_radius_and_never_above_the_global_cap()
        {
            int previous = 0;
            for (float r = 1f; r <= 8000f; r += 7.25f)
            {
                int cap = _balance.Horizon.CapMm(r);
                Assert.That(cap, Is.GreaterThanOrEqualTo(previous), $"R={r}");
                Assert.That(cap, Is.LessThanOrEqualTo(36000), $"R={r}");
                previous = cap;
            }
        }

        [Test]
        public void Out_of_contract_inputs_throw()
        {
            Assert.Throws<ContractException>(() => new HorizonRangeTuning(0, 1700, 36000));
            Assert.Throws<ContractException>(() => new HorizonRangeTuning(1001, 1700, 36000));
            Assert.Throws<ContractException>(() => new HorizonRangeTuning(775, 0, 36000));
            Assert.Throws<ContractException>(() => new HorizonRangeTuning(775, HorizonRangeTuning.MaxEyeHeightMm + 1, 36000));
            Assert.Throws<ContractException>(() => new HorizonRangeTuning(775, 1700, 0));
            Assert.Throws<ContractException>(() => _balance.Horizon.CapMm(0f));
            Assert.Throws<ContractException>(() => _balance.Horizon.CapMm(8000.5f));
            // Largest legal product stays inside Int64 (checked arithmetic would throw OverflowException otherwise).
            Assert.DoesNotThrow(() => new HorizonRangeTuning(1000, HorizonRangeTuning.MaxEyeHeightMm, 36000).CapMm(8000f));
        }

        [TestCase("horizon_factor_permille")]
        [TestCase("horizon_eye_height_mm")]
        public void A_missing_horizon_key_fails_loudly(string key)
        {
            JObject json = BalanceFixture.BalanceJson();
            ((JObject)json["ranged_distance"]).Remove(key);
            Assert.Throws<ContractException>(() => CombatBalance.FromJson(json.ToString()));
        }

        private HitVolume Build(MoveShape shape, RangeClass rc, int baseMm, int str, MoveMovement movement = MoveMovement.None,
            BurstOrigin origin = BurstOrigin.Caster) =>
            MoveGeometry.Build(shape, movement, rc, origin, baseMm, str, _balance.Stats, _balance.Hit);

        [Test]
        public void Ranged_reach_and_cast_distance_are_capped_but_aoe_sizes_are_not()
        {
            const int cap = 25597;
            HitVolume line = MoveGeometry.ApplyPlanetCap(Build(MoveShape.Line, RangeClass.Long, 36000, 100), RangeClass.Long, cap);
            Assert.That(line.ReachMm, Is.EqualTo(cap));
            Assert.That(line.WidthMm, Is.EqualTo(_balance.Hit.LineWidthMm));

            HitVolume cone = MoveGeometry.ApplyPlanetCap(Build(MoveShape.Cone, RangeClass.Mid, 30000, 0), RangeClass.Mid, cap);
            Assert.That(cone.ReachMm, Is.EqualTo(cap));
            Assert.That(cone.HalfAngleDeciDeg, Is.EqualTo(_balance.Hit.ConeAngleDeg * 5));

            HitVolume rawArc = Build(MoveShape.Arc, RangeClass.Long, 36000, 0);
            HitVolume arc = MoveGeometry.ApplyPlanetCap(rawArc, RangeClass.Long, cap);
            Assert.That(arc.CastDistanceMm, Is.EqualTo(cap));
            Assert.That(arc.ReachMm, Is.EqualTo(rawArc.ReachMm), "landing radius is an AoE size");

            HitVolume rawBurst = Build(MoveShape.Burst, RangeClass.Long, 2000, 10000, origin: BurstOrigin.Target);
            HitVolume burst = MoveGeometry.ApplyPlanetCap(rawBurst, RangeClass.Long, cap);
            Assert.That(rawBurst.CastDistanceMm, Is.GreaterThan(cap));
            Assert.That(burst.CastDistanceMm, Is.EqualTo(cap));
            Assert.That(burst.ReachMm, Is.EqualTo(2000));

            HitVolume ally = MoveGeometry.ApplyPlanetCap(Build(MoveShape.Ally, RangeClass.Mid, 36000, 0), RangeClass.Mid, cap);
            Assert.That(ally.CastDistanceMm, Is.EqualTo(cap));
        }

        [Test]
        public void Melee_volumes_keep_their_size_and_a_dash_shrinks_path_and_displacement_together()
        {
            const int cap = 25597;
            HitVolume front = Build(MoveShape.Front, RangeClass.Melee, 30000, 10000);
            Assert.That(MoveGeometry.ApplyPlanetCap(front, RangeClass.Melee, cap), Is.EqualTo(front));

            foreach (RangeClass rc in new[] { RangeClass.Melee, RangeClass.Mid, RangeClass.Long })
            {
                HitVolume dash = MoveGeometry.ApplyPlanetCap(Build(MoveShape.Line, rc, 36000, 10000, MoveMovement.Dash), rc, cap);
                Assert.That(dash.DashDistanceMm, Is.EqualTo(cap), rc.ToString());
                Assert.That(dash.ReachMm, Is.EqualTo(cap), rc.ToString());
            }

            HitVolume shortDash = Build(MoveShape.Line, RangeClass.Melee, 2000, 0, MoveMovement.Dash);
            Assert.That(MoveGeometry.ApplyPlanetCap(shortDash, RangeClass.Melee, cap), Is.EqualTo(shortDash));

            // SOL Q-D1-IMPL-02 M4: a melee-tagged arc/field is still thrown to an aim point; only that distance is capped.
            foreach (MoveShape shape in new[] { MoveShape.Arc, MoveShape.Field })
            {
                HitVolume raw = Build(shape, RangeClass.Melee, 36000, 100);
                Assert.That(raw.CastDistanceMm, Is.GreaterThan(cap), shape.ToString());
                HitVolume capped = MoveGeometry.ApplyPlanetCap(raw, RangeClass.Melee, cap);
                Assert.That(capped.CastDistanceMm, Is.EqualTo(cap), shape.ToString());
                Assert.That(capped.ReachMm, Is.EqualTo(raw.ReachMm), shape + " landing radius is an AoE size");
            }
        }

        // SOL Q-D1 A: the planet cap comes after range modifiers; capping first would make a −50 % debuff land at 12.8 m.
        [Test]
        public void Range_modifiers_apply_before_the_planet_cap()
        {
            const int cap = 25597;
            HitVolume line = Build(MoveShape.Line, RangeClass.Long, 36000, 0);
            HitVolume debuffed = EffectMath.ApplyRange(line, RangeClass.Long, -50, _balance.Stats, _balance.Effects);
            Assert.That(MoveGeometry.ApplyPlanetCap(debuffed, RangeClass.Long, cap).ReachMm, Is.EqualTo(18000));
            HitVolume buffed = EffectMath.ApplyRange(Build(MoveShape.Line, RangeClass.Long, 24000, 0), RangeClass.Long, 100,
                _balance.Stats, _balance.Effects);
            Assert.That(MoveGeometry.ApplyPlanetCap(buffed, RangeClass.Long, cap).ReachMm, Is.EqualTo(cap), "a buff never breaks the cap");
        }

        [Test]
        public void Planets_from_640_m_up_are_unchanged()
        {
            int cap = _balance.Horizon.CapMm(640f);
            var volumes = new[]
            {
                Build(MoveShape.Line, RangeClass.Long, 36000, 10000), Build(MoveShape.Cone, RangeClass.Mid, 30000, 50),
                Build(MoveShape.Arc, RangeClass.Long, 36000, 10000), Build(MoveShape.Field, RangeClass.Mid, 12000, 10000),
                Build(MoveShape.Burst, RangeClass.Long, 2000, 10000, origin: BurstOrigin.Target),
                Build(MoveShape.Ally, RangeClass.Mid, 36000, 10000), Build(MoveShape.Line, RangeClass.Melee, 36000, 0, MoveMovement.Dash),
                Build(MoveShape.Sweep, RangeClass.Melee, 3000, 10000),
            };
            var classes = new[]
            {
                RangeClass.Long, RangeClass.Mid, RangeClass.Long, RangeClass.Mid, RangeClass.Long, RangeClass.Mid,
                RangeClass.Melee, RangeClass.Melee,
            };
            for (int i = 0; i < volumes.Length; i++)
            {
                Assert.That(MoveGeometry.ApplyPlanetCap(volumes[i], classes[i], cap), Is.EqualTo(volumes[i]), $"#{i}");
            }
        }

        [Test]
        public void ApplyPlanetCap_rejects_out_of_contract_inputs()
        {
            HitVolume line = Build(MoveShape.Line, RangeClass.Long, 1000, 0);
            Assert.Throws<ContractException>(() => MoveGeometry.ApplyPlanetCap(line, RangeClass.Long, 0));
            Assert.Throws<ContractException>(() => MoveGeometry.ApplyPlanetCap(line, (RangeClass)9, 1000));
        }

        private CreatureDef LongDasher()
        {
            var m = ScriptableObject.CreateInstance<MoveDef>();
            _created.Add(m);
            m.SetId("dash_far");
            m.Set(Element.Fire, MoveKind.Damage, 40, null, 0, 0, 1500, 100, RangeClass.Melee, MoveShape.Line, 36000,
                MoveMovement.Dash, BurstOrigin.Caster);
            var c = ScriptableObject.CreateInstance<CreatureDef>();
            _created.Add(c);
            c.SetId("far");
            c.Set("line_far", CreatureStage.Juvenile, CreatureSizeClass.M, AiStyle.Charge, Temperament.Apex, null, 0,
                new List<EvolutionMaterial>(), new int[10], new List<ElementValue> { new ElementValue(Element.Fire, 15) },
                new CreatureStats(25, 25, 25, 25), m, null, "{}", false);
            return c;
        }

        private CreatureDef Sniper()
        {
            var m = ScriptableObject.CreateInstance<MoveDef>();
            _created.Add(m);
            m.SetId("snipe");
            m.Set(Element.Fire, MoveKind.Damage, 40, null, 0, 0, 1500, 100, RangeClass.Long, MoveShape.Line, 36000,
                MoveMovement.None, BurstOrigin.Caster);
            var c = ScriptableObject.CreateInstance<CreatureDef>();
            _created.Add(c);
            c.SetId("sniper");
            c.Set("line_sniper", CreatureStage.Juvenile, CreatureSizeClass.M, AiStyle.Hold, Temperament.Apex, null, 0,
                new List<EvolutionMaterial>(), new int[10], new List<ElementValue> { new ElementValue(Element.Fire, 15) },
                new CreatureStats(25, 25, 25, 25), m, null, "{}", false);
            return c;
        }

        // SOL Q-D1 B: the AI must judge reach on the capped volume, or it fires a 36 m line from 30 m on an asteroid.
        [TestCase(320f, false)]
        [TestCase(640f, true)]
        public void The_ai_does_not_fire_from_beyond_the_planet_cap(float radius, bool expectCast)
        {
            var planet = new PlanetConfig(radius, float3.zero);
            float3 At(float meters)
            {
                float a = meters / radius;
                return math.normalize(new float3(math.sin(a), math.cos(a), 0f));
            }

            var e = new Encounter(planet, _balance, new FlatSurface(), 5);
            EncounterUnit wild = e.AddCreature(EncounterSide.Wild, Sniper(), null, At(0f), 10);
            e.AttachPlayer(new PlayerVitals(new PlayerVitalsConfig(100000, 600, 400, 800)));
            e.Provoke(wild.Id);
            e.Tick(Dt, Dt, planet.ToWorld(At(30f), 0f), new AiCommand(AiCommandKind.Free, CombatAi.NoTarget),
                CombatAi.NoTarget);
            bool cast = false;
            foreach (EncounterEvent ev in e.Events)
            {
                cast |= ev.Kind == EncounterEventKind.Cast && ev.SourceId == wild.Id;
            }

            Assert.That(cast, Is.EqualTo(expectCast));
        }

        // SOL Q-D1-IMPL-01: with −50 % range the sniper reaches 18 m. At 23 m the AI used to judge 25.6 m, stand still and
        // fire short forever; at 15 m (inside 18 m, outside the 12.8 m a cap-before-modifier order gives) every cast must
        // land, which also pins TryCast's order end to end (M3).
        [TestCase(23f)]
        [TestCase(15f)]
        public void A_range_debuffed_ai_closes_in_and_lands_every_cast(float startM)
        {
            const float radius = 320f;
            var planet = new PlanetConfig(radius, float3.zero);
            float3 At(float meters)
            {
                float a = meters / radius;
                return math.normalize(new float3(math.sin(a), math.cos(a), 0f));
            }

            var e = new Encounter(planet, _balance, new FlatSurface(), 5);
            EncounterUnit wild = e.AddCreature(EncounterSide.Wild, Sniper(), null, At(0f), 10);
            wild.Combatant.Book.Apply("short", 0, new EffectSpec(EffectType.RangeModPercent, false, Element.Fire, -50, 0, 1),
                600_000, 0);
            e.AttachPlayer(new PlayerVitals(new PlayerVitalsConfig(100000, 600, 400, 800)));
            e.Provoke(wild.Id);
            float3 player = planet.ToWorld(At(startM), 0f);
            var free = new AiCommand(AiCommandKind.Free, CombatAi.NoTarget);
            int casts = 0, hits = 0;
            for (long now = Dt; now <= 10_000; now += Dt)
            {
                e.Tick(now, Dt, player, free, CombatAi.NoTarget);
                foreach (EncounterEvent ev in e.Events)
                {
                    casts += ev.Kind == EncounterEventKind.Cast && ev.SourceId == wild.Id ? 1 : 0;
                    hits += ev.Kind == EncounterEventKind.Hit && ev.SourceId == wild.Id ? 1 : 0;
                }
            }

            Assert.That(casts, Is.GreaterThan(0));
            Assert.That(hits, Is.EqualTo(casts));
        }

        // End to end through Encounter.Tick: a 36 m dash travels 36 m on a moon but only the cap on an asteroid.
        [TestCase(320f, 25.597f)]
        [TestCase(640f, 36f)]
        public void A_dash_travels_at_most_the_planet_cap(float radius, float expectedM)
        {
            var planet = new PlanetConfig(radius, float3.zero);
            float3 At(float meters)
            {
                float a = meters / radius;
                return math.normalize(new float3(math.sin(a), math.cos(a), 0f));
            }

            var e = new Encounter(planet, _balance, new FlatSurface(), 3);
            Assert.That(e.RangeCapMm, Is.EqualTo(_balance.Horizon.CapMm(radius)));
            EncounterUnit wild = e.AddCreature(EncounterSide.Wild, LongDasher(), null, At(0f), 10);
            e.AttachPlayer(new PlayerVitals(new PlayerVitalsConfig(100000, 600, 400, 800)));
            e.Provoke(wild.Id);
            float3 player = planet.ToWorld(At(20f), 0f);
            var free = new AiCommand(AiCommandKind.Free, CombatAi.NoTarget);
            float jump = -1f;
            for (long now = Dt; now <= 2_000 && jump < 0f; now += Dt)
            {
                float3 before = wild.Position;
                e.Tick(now, Dt, player, free, CombatAi.NoTarget);
                foreach (EncounterEvent ev in e.Events)
                {
                    if (ev.Kind == EncounterEventKind.Cast && ev.SourceId == wild.Id)
                    {
                        float3 a = planet.ToDirection(before);
                        float3 b = planet.ToDirection(wild.Position);
                        jump = math.atan2(math.length(math.cross(a, b)), math.dot(a, b)) * radius;
                    }
                }
            }

            // One tick of steering (≤ ~0.3 m at this AGI) rides along with the dash.
            Assert.That(jump, Is.EqualTo(expectedM).Within(0.3f));
        }
    }
}
