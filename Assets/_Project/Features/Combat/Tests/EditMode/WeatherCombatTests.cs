using System.Collections.Generic;
using System.IO;
using Cosmimera.Core.Contracts;
using Cosmimera.Core.Random;
using Cosmimera.GameData;
using Cosmimera.Planet;
using Cosmimera.WorldGen;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Cosmimera.Combat.Tests
{
    public sealed class WeatherCombatTests
    {
        private sealed class FlatSurface : IEncounterSurface
        {
            public float GroundAltitude(float3 direction) => 0f;
            public bool IsSea(float3 direction) => false;
            public bool IsBlocked(float3 direction, float clearanceM) => false;
        }

        private const float R = 640f;
        private const int Dt = 50;
        private readonly List<Object> _created = new List<Object>();
        private PlanetConfig _planet;
        private CombatBalance _balance;

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

        private CreatureDef MakeCreature(Element masteryElement, Temperament temperament = Temperament.Apex)
        {
            var m = ScriptableObject.CreateInstance<MoveDef>();
            _created.Add(m);
            m.SetId("wx_move_" + masteryElement);
            m.Set(masteryElement, MoveKind.Damage, 40, null, 0, 0, 1500, 100, RangeClass.Melee, MoveShape.Front, 2000,
                MoveMovement.None, BurstOrigin.Caster);
            var c = ScriptableObject.CreateInstance<CreatureDef>();
            _created.Add(c);
            c.SetId("wx_creature_" + masteryElement);
            c.Set("line_wx", CreatureStage.Juvenile, CreatureSizeClass.M, AiStyle.Charge, temperament, null, 0,
                new List<EvolutionMaterial>(), new int[10],
                new List<ElementValue> { new ElementValue(masteryElement, 15) },
                new CreatureStats(25, 25, 25, 25), m, null, "{}", false);
            return c;
        }

        private static float3 AtMeters(float meters)
        {
            float a = meters / R;
            return math.normalize(new float3(math.sin(a), math.cos(a), 0f));
        }

        [Test]
        public void DamageModel_ScalePower_scales_and_clamps_correctly()
        {
            Assert.That(DamageModel.ScalePower(0, 25), Is.EqualTo(0));
            Assert.That(DamageModel.ScalePower(100, 0), Is.EqualTo(100));
            Assert.That(DamageModel.ScalePower(100, 25), Is.EqualTo(125));
            Assert.That(DamageModel.ScalePower(100, -20), Is.EqualTo(80));
            Assert.That(DamageModel.ScalePower(1, -90), Is.EqualTo(1));

            Assert.Throws<ContractException>(() => DamageModel.ScalePower(-1, 0));
            Assert.Throws<ContractException>(() => DamageModel.ScalePower(10, -91));
            Assert.Throws<ContractException>(() => DamageModel.ScalePower(10, 101));
        }

        private Combatant MakeCombatant() =>
            new Combatant(_balance.Stats, _balance.Effects, 30, 20, 20, new int[10], new int[10], new EffectBook(), 2);

        [Test]
        public void BeginCast_and_ReceiveHostile_apply_weather_cooldown_and_power_modifiers()
        {
            Combatant attacker = MakeCombatant();
            Combatant waterTarget = MakeCombatant();
            Combatant fireTarget = MakeCombatant();
            Combatant clearTarget = MakeCombatant();

            var waterMove = new CastMove("w", Element.Water, MoveKind.Damage, 100, 100, 2000, false, default, default);
            var fireMove = new CastMove("f", Element.Fire, MoveKind.Damage, 100, 100, 2000, false, default, default);

            Assert.That(attacker.BeginCast(0, waterMove, 0, WeatherProfiles.Clear, out CastContext clearCtx), Is.EqualTo(CastResult.Accepted));
            attacker = MakeCombatant();
            Assert.That(attacker.BeginCast(0, waterMove, 0, WeatherProfiles.Rain, out CastContext rainWaterCtx), Is.EqualTo(CastResult.Accepted));
            attacker = MakeCombatant();
            Assert.That(attacker.BeginCast(0, fireMove, 0, WeatherProfiles.Rain, out CastContext rainFireCtx), Is.EqualTo(CastResult.Accepted));

            Assert.That(rainWaterCtx.CooldownMs, Is.LessThan(clearCtx.CooldownMs));
            Assert.That(rainFireCtx.CooldownMs, Is.GreaterThan(clearCtx.CooldownMs));

            var rngA = new Pcg32(42UL, 7UL);
            var rngB = new Pcg32(42UL, 7UL);
            var rngC = new Pcg32(42UL, 7UL);
            HitReport clearHit = clearTarget.ReceiveHostile(clearCtx, true, ref rngA, 0);
            HitReport rainWaterHit = waterTarget.ReceiveHostile(rainWaterCtx, true, ref rngB, 0);
            HitReport rainFireHit = fireTarget.ReceiveHostile(rainFireCtx, true, ref rngC, 0);

            Assert.That(rainWaterHit.Damage, Is.GreaterThan(clearHit.Damage));
            Assert.That(rainFireHit.Damage, Is.LessThan(clearHit.Damage));
        }

        [Test]
        public void PlayerVitals_TryRoll_scales_cooldown_in_extreme_weather()
        {
            var cfg = new PlayerVitalsConfig(100, 400, 300, 1000);
            var clearVitals = new PlayerVitals(cfg);
            var heatVitals = new PlayerVitals(cfg);

            Assert.That(clearVitals.TryRoll(0, WeatherProfiles.Clear), Is.EqualTo(RollResult.Ok));
            Assert.That(heatVitals.TryRoll(0, WeatherProfiles.Heatwave), Is.EqualTo(RollResult.Ok));

            // At 1000 ms, clear roll is ready (1000 ms * 1.0), while heatwave roll (1000 ms * 1.15 = 1150 ms) is still on cooldown.
            Assert.That(clearVitals.TryRoll(1000, WeatherProfiles.Clear), Is.EqualTo(RollResult.Ok));
            Assert.That(heatVitals.TryRoll(1000, WeatherProfiles.Heatwave), Is.EqualTo(RollResult.OnCooldown));
            Assert.That(heatVitals.TryRoll(1150, WeatherProfiles.Heatwave), Is.EqualTo(RollResult.Ok));
        }

        [Test]
        public void Encounter_Fog_shrinks_territory_and_halves_detection_for_slow_player()
        {
            // Apex base territory is 28 m. Fog scales to 60% = 16.8 m; slow-walk stealth halves it to 8.4 m.
            // Place player at 12 m: outside stealth radius (8.4 m) but inside unstealthed fog radius (16.8 m).
            var slowEncounter = new Encounter(_planet, _balance, new FlatSurface(), 7);
            EncounterUnit slowWild = slowEncounter.AddCreature(EncounterSide.Wild, MakeCreature(Element.Fire, Temperament.Apex),
                null, AtMeters(0f), 10);
            slowEncounter.AttachPlayer(new PlayerVitals(new PlayerVitalsConfig(1000, 600, 400, 800)));
            slowEncounter.SetWeather(WeatherProfiles.Fog);
            slowEncounter.SetPlayerMoveSpeedMs(1.0f); // <= SlowStealthSpeedThresholdMs (2.0 m/s)
            slowEncounter.Tick(Dt, Dt, AtMeters(12f) * R, new AiCommand(AiCommandKind.Free, CombatAi.NoTarget), CombatAi.NoTarget);
            Assert.That(slowWild.Engaged, Is.False);

            var fastEncounter = new Encounter(_planet, _balance, new FlatSurface(), 7);
            EncounterUnit fastWild = fastEncounter.AddCreature(EncounterSide.Wild, MakeCreature(Element.Fire, Temperament.Apex),
                null, AtMeters(0f), 10);
            fastEncounter.AttachPlayer(new PlayerVitals(new PlayerVitalsConfig(1000, 600, 400, 800)));
            fastEncounter.SetWeather(WeatherProfiles.Fog);
            fastEncounter.SetPlayerMoveSpeedMs(5.0f); // > SlowStealthSpeedThresholdMs
            fastEncounter.Tick(Dt, Dt, AtMeters(12f) * R, new AiCommand(AiCommandKind.Free, CombatAi.NoTarget), CombatAi.NoTarget);
            Assert.That(fastWild.Engaged, Is.True);
        }

        [Test]
        public void Encounter_Blizzard_slows_fire_creature_and_boosts_ice_creature()
        {
            float StepFor(Element mastery, WeatherProfile weather)
            {
                var e = new Encounter(_planet, _balance, new FlatSurface(), 3);
                EncounterUnit wild = e.AddCreature(EncounterSide.Wild, MakeCreature(mastery, Temperament.Apex),
                    null, AtMeters(0f), 10);
                e.AttachPlayer(new PlayerVitals(new PlayerVitalsConfig(100000, 600, 400, 800)));
                e.SetWeather(weather);
                e.Tick(Dt, Dt, AtMeters(15f) * R, new AiCommand(AiCommandKind.Free, CombatAi.NoTarget), CombatAi.NoTarget);
                return math.distance(AtMeters(0f) * R, wild.Position);
            }

            float clearFire = StepFor(Element.Fire, WeatherProfiles.Clear);
            float blizzardFire = StepFor(Element.Fire, WeatherProfiles.Blizzard);
            float blizzardIce = StepFor(Element.Ice, WeatherProfiles.Blizzard);

            Assert.That(blizzardFire, Is.LessThan(clearFire));
            Assert.That(blizzardIce, Is.GreaterThan(clearFire));
        }

        [Test]
        public void Encounter_SetWeatherProvider_applies_dynamic_weather_and_validates_player_speed()
        {
            var e = new Encounter(_planet, _balance, new FlatSurface(), 9);
            EncounterUnit wild = e.AddCreature(EncounterSide.Wild, MakeCreature(Element.Fire, Temperament.Apex),
                null, AtMeters(0f), 10);
            e.AttachPlayer(new PlayerVitals(new PlayerVitalsConfig(1000, 600, 400, 800)));
            e.SetWeatherProvider((_, t) => t < 1000 ? WeatherProfiles.Fog : WeatherProfiles.Clear);
            e.SetPlayerMoveSpeedMs(1.0f);

            // At t = 50 ms (Fog + slow stealth), 12 m is outside 8.4 m.
            e.Tick(50, 50, AtMeters(12f) * R, new AiCommand(AiCommandKind.Free, CombatAi.NoTarget), CombatAi.NoTarget);
            Assert.That(wild.Engaged, Is.False);

            // At t = 1050 ms (Clear), 12 m is inside 28 m territory.
            e.Tick(1050, 50, AtMeters(12f) * R, new AiCommand(AiCommandKind.Free, CombatAi.NoTarget), CombatAi.NoTarget);
            Assert.That(wild.Engaged, Is.True);

            Assert.Throws<ContractException>(() => e.SetPlayerMoveSpeedMs(-0.1f));
        }

        [Test]
        public void Encounter_SetExplorationUtilityModifiers_and_ForceDisengageAllWild_apply_stealth_and_escape()
        {
            var e = new Encounter(_planet, _balance, new FlatSurface(), 17);
            EncounterUnit wild = e.AddCreature(EncounterSide.Wild, MakeCreature(Element.Fire, Temperament.Apex),
                null, AtMeters(0f), 10);
            EncounterUnit boss = e.AddBoss(MakeCreature(Element.Fire, Temperament.Apex),
                null, AtMeters(200f), 10);
            e.AttachPlayer(new PlayerVitals(new PlayerVitalsConfig(1000, 600, 400, 800)));

            // PartyStealth halves Apex 28 m territory to 14 m. At 18 m, wild does not engage.
            e.SetExplorationUtilityModifiers(500, preventWildEscape: true);
            e.Tick(Dt, Dt, AtMeters(18f) * R, new AiCommand(AiCommandKind.Free, CombatAi.NoTarget), CombatAi.NoTarget);
            Assert.That(wild.Engaged, Is.False);

            // Within 14 m (10 m), wild engages; with preventWildEscape=true, moving to 45 m stays engaged (< 60 m).
            e.Tick(Dt * 2, Dt, AtMeters(10f) * R, new AiCommand(AiCommandKind.Free, CombatAi.NoTarget), CombatAi.NoTarget);
            Assert.That(wild.Engaged, Is.True);
            e.Tick(Dt * 3, Dt, AtMeters(45f) * R, new AiCommand(AiCommandKind.Free, CombatAi.NoTarget), CombatAi.NoTarget);
            Assert.That(wild.Engaged, Is.True);

            // QuickEscape disengages non-boss wild units, leaving boss untouched.
            Assert.That(e.ForceDisengageAllWild(), Is.True);
            Assert.That(wild.Engaged, Is.False);

            e.Tick(Dt * 4, Dt, AtMeters(205f) * R, new AiCommand(AiCommandKind.Free, CombatAi.NoTarget), CombatAi.NoTarget);
            Assert.That(boss.Engaged, Is.True);
            Assert.That(e.ForceDisengageAllWild(), Is.False);
            Assert.That(boss.Engaged, Is.True);

            Assert.Throws<ContractException>(() => e.SetExplorationUtilityModifiers(50, false));
        }
    }
}
