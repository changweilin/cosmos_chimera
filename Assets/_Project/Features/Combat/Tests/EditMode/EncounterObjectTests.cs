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
    // REQ: docs/design/destructibles.md#誰能打 — move volumes hit scene objects; objects never roll on the combat stream.
    public sealed class EncounterObjectTests
    {
        private sealed class FlatSurface : IEncounterSurface
        {
            public float GroundAltitude(float3 direction) => 0f;

            public bool IsSea(float3 direction) => false;

            public bool IsBlocked(float3 direction, float clearanceM) => false;
        }

        private sealed class ListIndex : IDestructibleIndex
        {
            public readonly List<DestructibleCandidate> Items = new List<DestructibleCandidate>();

            public float MaxHitRadiusM => 1f;

            public void Query(float3 worldPoint, float radiusM, List<DestructibleCandidate> into)
            {
                foreach (DestructibleCandidate c in Items)
                {
                    if (math.distance(c.Center, worldPoint) <= radiusM)
                    {
                        into.Add(c);
                    }
                }
            }
        }

        private const float R = 640f;
        private const int Dt = 50;
        private static readonly AiCommand Free = new AiCommand(AiCommandKind.Free, CombatAi.NoTarget);
        private readonly List<Object> _created = new List<Object>();
        private PlanetConfig _planet;
        private CombatBalance _balance;

        private static Substance Wood()
        {
            var resist = new int[10];
            resist[(int)Element.Fire] = -50;
            return new Substance("wood", Element.Wood, Flammability.Slow, false, Brittleness.Never, 900, 900, 1000, resist);
        }

        private static EnvMaterials Env()
        {
            Substance wood = Wood();
            var stone = new Substance("stone", Element.Earth, Flammability.None, false, Brittleness.Frozen, 900, 2500, 2000, new int[10]);
            var none = new int[10];
            Substance S(string id, Element e) => new Substance(id, e, Flammability.None, false, Brittleness.Never, 500, 900, 300, none);
            var ground = new[] { S("metal", Element.Metal), wood, S("pectin", Element.Water), S("charcoal", Element.Fire), stone,
                S("ice", Element.Ice), S("cloth", Element.Wind), S("chip", Element.Thunder), null, null };
            var buildings = new Composition[EnvMaterials.BuildingMaterialCount];
            for (int i = 0; i < buildings.Length; i++)
            {
                buildings[i] = Composition.Of(stone);
            }

            return new EnvMaterials(100, 250, 150, 5000, 200, 150, 200, new VolumeChance(1000, 0, 1000),
                new VolumeChance(0, 0, 0), 30, 500, buildings, ground, stone);
        }

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

        private CreatureDef FireBrawler()
        {
            var m = ScriptableObject.CreateInstance<MoveDef>();
            _created.Add(m);
            m.SetId("move_fire_front");
            m.Set(Element.Fire, MoveKind.Damage, 40, null, 0, 0, 1500, 100, RangeClass.Melee, MoveShape.Front, 2000,
                MoveMovement.None, BurstOrigin.Caster);
            var c = ScriptableObject.CreateInstance<CreatureDef>();
            _created.Add(c);
            c.SetId("brawler");
            c.Set("line_brawler", CreatureStage.Juvenile, CreatureSizeClass.M, AiStyle.Charge, Temperament.Apex, null, 0,
                new List<EvolutionMaterial>(), new int[10], new List<ElementValue> { new ElementValue(Element.Fire, 15) },
                new CreatureStats(25, 25, 25, 25), m, null, "{}", false);
            return c;
        }

        private static float3 At(float alongM) => new float3(math.sin(alongM / R), math.cos(alongM / R), 0f);

        private float3 Ground(float alongM, float upM = 0.5f) => _planet.ToWorld(At(alongM), upM);

        private Encounter Fight(out float3 player)
        {
            var e = new Encounter(_planet, _balance, new FlatSurface(), 7);
            EncounterUnit wild = e.AddCreature(EncounterSide.Wild, FireBrawler(), null, At(0f), 10);
            e.AttachPlayer(new PlayerVitals(new PlayerVitalsConfig(1_000_000, 600, 400, 800)));
            e.Provoke(wild.Id);
            player = _planet.ToWorld(At(3f), 0f);
            return e;
        }

        private static List<ObjectEvent> Run(Encounter e, float3 player, long untilMs)
        {
            var all = new List<ObjectEvent>();
            for (long now = Dt; now <= untilMs; now += Dt)
            {
                e.Tick(now, Dt, player, Free, CombatAi.NoTarget);
                all.AddRange(e.ObjectEvents);
            }

            return all;
        }

        [Test]
        public void A_fire_move_burns_a_small_tree_in_its_path_and_it_drops_a_fire_material()
        {
            Encounter e = Fight(out float3 player);
            var index = new ListIndex();
            index.Items.Add(new DestructibleCandidate(42, Ground(1.5f), 0.5f, Composition.Of(Wood()), 50));
            var table = new DestructibleTable(Env());
            e.AttachDestructibles(table, index, _ => false);

            List<ObjectEvent> events = Run(e, player, 6000);
            int destroyed = events.FindIndex(ev => ev.Kind == ObjectEventKind.Destroyed);
            Assert.That(destroyed, Is.GreaterThan(0), "the swing must reach the tree");
            ObjectEvent hit = events[destroyed - 1];
            Assert.That(hit.Kind, Is.EqualTo(ObjectEventKind.Hit));
            Assert.That(hit.ObjectId, Is.EqualTo(42));
            Assert.That(hit.Damage, Is.GreaterThan(0));
            ObjectEvent d = events[destroyed];
            Assert.That(d.Cause, Is.EqualTo(DestroyCause.Burnt));
            Assert.That(d.Drop.Element, Is.EqualTo(Element.Fire));
            Assert.That(d.Drop.Material, Is.True, "fixture drop chance is 100%");
            Assert.That(d.Drop.Shadow, Is.False, "daytime");
            Assert.That(events.Count, Is.EqualTo(destroyed + 1), "a destroyed object is never hit again");
            Assert.That(table.IsDestroyed(42), Is.True);
        }

        [Test]
        public void A_big_burning_tree_burns_out_on_its_own()
        {
            Encounter e = Fight(out float3 player);
            var index = new ListIndex();
            index.Items.Add(new DestructibleCandidate(7, Ground(1.5f), 0.5f, Composition.Of(Wood()), 100_000));
            var table = new DestructibleTable(Env());
            e.AttachDestructibles(table, index, _ => true);

            // One tick of fighting ignites it; then the character leaves so nothing hits it again.
            List<ObjectEvent> events = Run(e, player, 3000);
            Assert.That(events.Exists(ev => (ev.Reactions & ObjectReaction.Ignited) != 0), Is.True);
            index.Items.Clear();
            var later = new List<ObjectEvent>();
            for (long now = 3000 + Dt; now <= 20_000; now += Dt)
            {
                e.Tick(now, Dt, player, Free, CombatAi.NoTarget);
                later.AddRange(e.ObjectEvents);
            }

            ObjectEvent burnt = later.Find(ev => ev.Kind == ObjectEventKind.Destroyed);
            Assert.That(burnt.ObjectId, Is.EqualTo(7));
            Assert.That(burnt.SourceId, Is.EqualTo(Encounter.NoSource));
            Assert.That(burnt.Cause, Is.EqualTo(DestroyCause.Burnt));
        }

        [Test]
        public void Objects_do_not_change_the_combat_roll_order()
        {
            Encounter plain = Fight(out float3 player);
            Encounter withObjects = Fight(out _);
            var index = new ListIndex();
            index.Items.Add(new DestructibleCandidate(1, Ground(1.5f), 0.5f, Composition.Of(Wood()), 100_000));
            withObjects.AttachDestructibles(new DestructibleTable(Env()), index, _ => false);
            Assert.That(Run(withObjects, player, 4000).Count, Is.GreaterThan(0));
            Run(plain, player, 4000);
            Assert.That(withObjects.RngForTests.NextUInt(), Is.EqualTo(plain.RngForTests.NextUInt()));
            Assert.That(withObjects.Units[0].Combatant.Hp, Is.EqualTo(plain.Units[0].Combatant.Hp));
        }

        [Test]
        public void Attach_contract_violations_throw()
        {
            Encounter e = Fight(out float3 player);
            var table = new DestructibleTable(Env());
            var index = new ListIndex();
            Assert.Throws<ContractException>(() => e.AttachDestructibles(null, index, _ => false));
            Assert.Throws<ContractException>(() => e.AttachDestructibles(table, null, _ => false));
            Assert.Throws<ContractException>(() => e.AttachDestructibles(table, index, null));
            e.AttachDestructibles(table, index, _ => false);
            Assert.Throws<ContractException>(() => e.AttachDestructibles(table, index, _ => false));

            Encounter started = Fight(out _);
            started.Tick(Dt, Dt, player, Free, CombatAi.NoTarget);
            Assert.Throws<ContractException>(() => started.AttachDestructibles(table, index, _ => false));
            Assert.That(started.ObjectEvents.Count, Is.EqualTo(0));
        }
    }
}
