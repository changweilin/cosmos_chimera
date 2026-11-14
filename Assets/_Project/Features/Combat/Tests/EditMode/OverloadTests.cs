using System.Collections.Generic;
using System.IO;
using Cosmimera.GameData;
using Cosmimera.Planet;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Cosmimera.Combat.Tests
{
    public sealed class OverloadTests
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

        private MoveDef MakeMove(string id)
        {
            var m = ScriptableObject.CreateInstance<MoveDef>();
            _created.Add(m);
            m.SetId(id);
            m.Set(Element.Fire, MoveKind.Damage, 40, null, 0, 0, 1500, 100, RangeClass.Melee, MoveShape.Front, 2000,
                MoveMovement.None, BurstOrigin.Caster);
            return m;
        }

        private CreatureDef MakeCreature(string id, CreatureStage stage)
        {
            MoveDef move = MakeMove(id + "_m");
            var c = ScriptableObject.CreateInstance<CreatureDef>();
            _created.Add(c);
            c.SetId(id);
            c.Set("line_" + id, stage, CreatureSizeClass.L, AiStyle.Charge, Temperament.Normal, null, 0,
                new List<EvolutionMaterial>(), new int[10], new List<ElementValue> { new ElementValue(Element.Fire, 15) },
                new CreatureStats(25, 25, 25, 25), move, null, "{}", false);
            return c;
        }

        [Test]
        public void TryTriggerOverload_enforces_stage_unlock_combat_and_once_per_combat()
        {
            var enc = new Encounter(_planet, _balance, new FlatSurface(), 1UL);
            var up = new float3(0f, 1f, 0f);
            EncounterUnit adult = enc.AddCreature(EncounterSide.Player, MakeCreature("adult", CreatureStage.Adult), null, up, 10);
            EncounterUnit juvenile = enc.AddCreature(EncounterSide.Player, MakeCreature("juv", CreatureStage.Juvenile), null, up, 10);
            EncounterUnit wild = enc.AddCreature(EncounterSide.Wild, MakeCreature("wild", CreatureStage.Adult), null, up, 10);

            Assert.That(enc.TryTriggerOverload(adult.Id, inCombat: false, overloadUnlocked: true), Is.EqualTo(OverloadResult.NotInCombat));
            Assert.That(enc.TryTriggerOverload(wild.Id, inCombat: true, overloadUnlocked: true), Is.EqualTo(OverloadResult.NotPlayerSide));
            Assert.That(enc.TryTriggerOverload(adult.Id, inCombat: true, overloadUnlocked: true), Is.EqualTo(OverloadResult.NotFielded));

            enc.Field(adult.Id, up, false, 1000);
            enc.Field(juvenile.Id, up, false, 1000);

            Assert.That(enc.TryTriggerOverload(juvenile.Id, inCombat: true, overloadUnlocked: true), Is.EqualTo(OverloadResult.NotAdult));
            Assert.That(enc.TryTriggerOverload(adult.Id, inCombat: true, overloadUnlocked: false), Is.EqualTo(OverloadResult.NotUnlocked));

            int baseMaxHp = adult.Combatant.MaxHp;
            Assert.That(enc.TryTriggerOverload(adult.Id, inCombat: true, overloadUnlocked: true), Is.EqualTo(OverloadResult.Ok));
            Assert.That(adult.Combatant.IsOverloaded, Is.True);
            Assert.That(adult.Combatant.MaxHp, Is.GreaterThan(baseMaxHp));
            Assert.That(enc.TryTriggerOverload(adult.Id, inCombat: true, overloadUnlocked: true), Is.EqualTo(OverloadResult.AlreadyOverloaded));

            enc.EndCombatOverload();
            Assert.That(adult.Combatant.IsOverloaded, Is.False);
            Assert.That(adult.Combatant.MaxHp, Is.EqualTo(baseMaxHp));

            Assert.That(enc.TryTriggerOverload(adult.Id, inCombat: true, overloadUnlocked: true), Is.EqualTo(OverloadResult.Ok));
        }
    }
}
