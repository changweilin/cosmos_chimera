using System.Collections.Generic;
using System.IO;
using Cosmimera.Core.Contracts;
using Cosmimera.GameData;
using Cosmimera.Planet;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Cosmimera.Combat.Tests
{
    public sealed class DirectControlTests
    {
        private sealed class FlatSurface : IEncounterSurface
        {
            public float GroundAltitude(float3 direction) => 0f;
            public bool IsSea(float3 direction) => false;
            public bool IsBlocked(float3 direction, float clearanceM) => false;
        }

        private const float R = 640f;
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
            foreach (Object o in _created) Object.DestroyImmediate(o);
            _created.Clear();
        }

        private MoveDef MakeMove(string id, int power, int cooldownMs)
        {
            var m = ScriptableObject.CreateInstance<MoveDef>();
            _created.Add(m);
            m.SetId(id);
            m.Set(Element.Fire, MoveKind.Damage, power, null, 0, 0, cooldownMs, 100, RangeClass.Melee, MoveShape.Front, 5000,
                MoveMovement.None, BurstOrigin.Caster);
            return m;
        }

        private CreatureDef MakeCreature(string id, MoveDef sig)
        {
            var c = ScriptableObject.CreateInstance<CreatureDef>();
            _created.Add(c);
            c.SetId(id);
            TestWild.WithHabit(c, Habit.Tolerant).Set("line_" + id, CreatureStage.Juvenile, CreatureSizeClass.M, AiStyle.Hold, null, null, 0,
                new List<EvolutionMaterial>(), new int[10], new List<ElementValue> { new ElementValue(Element.Fire, 15) },
                new CreatureStats(200, 25, 25, 25, 25, 25), sig, null, "{}", false);
            return c;
        }

        [Test]
        public void Direct_control_moves_main_without_ai_autocast_and_shields_player_character()
        {
            MoveDef sig = MakeMove("m_sig", 25, 1000);
            CreatureDef def = MakeCreature("c_test", sig);
            var enc = new Encounter(_planet, _balance, new FlatSurface(), 123UL);
            var vitals = new PlayerVitals(_balance.Player);
            enc.AttachPlayer(vitals, 100, 100);

            float3 playerDir = math.normalize(new float3(0f, 1f, 0f));
            float3 playerPos = _planet.ToWorld(playerDir, 0f);
            EncounterUnit main = enc.AddCreature(EncounterSide.Player, def, null, playerDir, 10);
            enc.Field(main.Id, playerDir, false, 1000);

            float3 wildDir = math.normalize(new float3(0.005f, 1f, 0f));
            EncounterUnit wild = enc.AddWildT(def, null, wildDir, 10);
            enc.Provoke(wild.Id);

            enc.SetDirectControl(true);
            float3 east = new float3(1f, 0f, 0f);
            enc.SetDirectInput(east, east, wild.Id);

            float3 before = main.Position;
            enc.Tick(1050, 50, playerPos, Free, main.Id);

            Assert.That(math.length(main.Position - before), Is.GreaterThan(0.01f), "direct input moves main");
            Assert.That(math.dot(main.Forward, east), Is.GreaterThan(0.95f), "direct facing updates forward");
            Assert.That(vitals.Hp, Is.EqualTo(_balance.Player.MaxHp), "player character is untargetable under direct control");

            bool cast = enc.TryDirectCast(main.Id, 0, wild.Id, 1050);
            Assert.That(cast, Is.True, "manual cast succeeds when off cooldown");
            Assert.That(enc.TryDirectCast(main.Id, 0, wild.Id, 1050), Is.False, "manual cast respects cooldown");

            enc.SetDirectControl(false);
            Assert.That(enc.DirectControl, Is.False);
        }

        [Test]
        public void Direct_control_contract_preconditions_are_enforced()
        {
            MoveDef sig = MakeMove("m_sig2", 20, 500);
            CreatureDef def = MakeCreature("c_test2", sig);
            var enc = new Encounter(_planet, _balance, new FlatSurface(), 124UL);
            float3 dir = math.normalize(new float3(0f, 1f, 0f));
            EncounterUnit main = enc.AddCreature(EncounterSide.Player, def, null, dir, 10);

            Assert.Throws<ContractException>(() => enc.SetDirectInput(new float3(float.NaN, 0f, 0f), float3.zero, -1));
            Assert.Throws<ContractException>(() => enc.TryDirectCast(-1, 0, -1, 1000));
            Assert.Throws<ContractException>(() => enc.TryDirectCast(main.Id, -1, -1, 1000));
            // LastTickMs starts at long.MinValue, so the "nowMs precedes LastTickMs" guard needs a real tick first.
            enc.Tick(1000, 50, _planet.ToWorld(dir, 0f), Free, main.Id);
            Assert.Throws<ContractException>(() => enc.TryDirectCast(main.Id, 0, -1, 999));
        }
    }
}
