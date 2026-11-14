using System.Collections.Generic;
using System.IO;
using Cosmimera.Core.Contracts;
using Cosmimera.Core.Random;
using Cosmimera.GameData;
using Cosmimera.Planet;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Cosmimera.Combat.Tests
{
    public sealed class ReviveRulesTests
    {
        private sealed class FlatSurface : IEncounterSurface
        {
            public float GroundAltitude(float3 direction) => 0f;
            public bool IsSea(float3 direction) => false;
            public bool IsBlocked(float3 direction, float clearanceM) => false;
        }

        private readonly List<Object> _created = new List<Object>();
        private PlanetConfig _planet;
        private CombatBalance _balance;

        [SetUp]
        public void SetUp()
        {
            _planet = new PlanetConfig(640f, float3.zero);
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

        private CreatureDef MakeCreature(string id)
        {
            var m = ScriptableObject.CreateInstance<MoveDef>();
            _created.Add(m);
            m.SetId(id + "_m");
            m.Set(Element.Fire, MoveKind.Damage, 9999, null, 0, 0, 100, 100, RangeClass.Melee, MoveShape.Front, 2000,
                MoveMovement.None, BurstOrigin.Caster);
            var c = ScriptableObject.CreateInstance<CreatureDef>();
            _created.Add(c);
            c.SetId(id);
            c.Set("line_" + id, CreatureStage.Adult, CreatureSizeClass.M, AiStyle.Charge, Temperament.Fierce, null, 0,
                new List<EvolutionMaterial>(), new int[10], new List<ElementValue> { new ElementValue(Element.Fire, 15) },
                new CreatureStats(25, 25, 25, 25), m, null, "{}", false);
            return c;
        }

        [Test]
        public void RecoveryPercent_matches_Q_C36_tiers_and_combat_halving()
        {
            Assert.That(ReviveRules.RecoveryPercent(PlanetSize.Asteroid, inCombat: false), Is.EqualTo(30));
            Assert.That(ReviveRules.RecoveryPercent(PlanetSize.Moon, inCombat: false), Is.EqualTo(30));
            Assert.That(ReviveRules.RecoveryPercent(PlanetSize.Planet, inCombat: false), Is.EqualTo(60));
            Assert.That(ReviveRules.RecoveryPercent(PlanetSize.Supergiant, inCombat: false), Is.EqualTo(100));

            Assert.That(ReviveRules.RecoveryPercent(PlanetSize.Asteroid, inCombat: true), Is.EqualTo(15));
            Assert.That(ReviveRules.RecoveryPercent(PlanetSize.Moon, inCombat: true), Is.EqualTo(15));
            Assert.That(ReviveRules.RecoveryPercent(PlanetSize.Planet, inCombat: true), Is.EqualTo(30));
            Assert.That(ReviveRules.RecoveryPercent(PlanetSize.Supergiant, inCombat: true), Is.EqualTo(50));

            Assert.Throws<ContractException>(() => ReviveRules.RecoveryPercent((PlanetSize)99, false));
            Assert.Throws<ContractException>(() => ReviveRules.ComputeReviveHp(0, PlanetSize.Moon, false));
        }

        [Test]
        public void TryRevive_and_TeamState_TryClearRetired_enforce_cooldown_and_faint_state()
        {
            var enc = new Encounter(_planet, _balance, new FlatSurface(), 1UL);
            var up = new float3(0f, 1f, 0f);
            EncounterUnit mine = enc.AddCreature(EncounterSide.Player, MakeCreature("mine"), null, up, 10);
            EncounterUnit wild = enc.AddCreature(EncounterSide.Wild, MakeCreature("wild"), null, up, 10);

            Assert.That(enc.TryRevive(mine.Id, ReviveSource.ConsumableItem, PlanetSize.Moon, false, 1000),
                Is.EqualTo(ReviveResult.NotFainted));
            Assert.That(enc.TryRevive(wild.Id, ReviveSource.ConsumableItem, PlanetSize.Moon, false, 1000),
                Is.EqualTo(ReviveResult.InvalidUnit));

            // Faint mine via a hit from wild
            enc.Field(mine.Id, up, false, 1000);
            var rng = new Pcg32(1UL, 1UL);
            Assert.That(wild.Combatant.BeginCast(0, CastMove.FromDef(wild.Def.SignatureMove), 1000, out CastContext ctx1),
                Is.EqualTo(CastResult.Accepted));
            mine.Combatant.ReceiveHostile(ctx1, true, ref rng, 1000);
            Assert.That(mine.Combatant.IsFainted, Is.True);

            Assert.That(enc.TryRevive(mine.Id, ReviveSource.ConsumableItem, PlanetSize.Planet, inCombat: true, nowMs: 2000),
                Is.EqualTo(ReviveResult.Ok));
            Assert.That(mine.Combatant.Hp, Is.EqualTo(ReviveRules.ComputeReviveHp(mine.Combatant.MaxHp, PlanetSize.Planet, true)));
            Assert.That(enc.ReviveCooldownRemainingMs(2000), Is.EqualTo(ReviveRules.CombatCooldownMs));

            // Faint again; second combat revive within 60 s is blocked
            Assert.That(wild.Combatant.BeginCast(0, CastMove.FromDef(wild.Def.SignatureMove), 2000, out CastContext ctx2),
                Is.EqualTo(CastResult.Accepted));
            mine.Combatant.ReceiveHostile(ctx2, true, ref rng, 2000);
            Assert.That(enc.TryRevive(mine.Id, ReviveSource.ConsumableItem, PlanetSize.Planet, inCombat: true, nowMs: 61_999),
                Is.EqualTo(ReviveResult.OnCooldown));
            Assert.That(enc.TryRevive(mine.Id, ReviveSource.ConsumableItem, PlanetSize.Supergiant, inCombat: true, nowMs: 62_000),
                Is.EqualTo(ReviveResult.Ok));

            var team = new TeamState(new[] { mine.Id }, 3000);
            team.SetPool(new[] { mine.Id });
            team.Summon(mine.Id, 0);
            team.BeginCombat();
            team.Faint(mine.Id);
            Assert.That(team.TryClearRetired(mine.Id), Is.True);
            Assert.That(team.TryClearRetired(mine.Id), Is.False);
            Assert.Throws<ContractException>(() => team.TryClearRetired(999));
        }
    }
}
