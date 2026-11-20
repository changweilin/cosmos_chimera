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
    // REQ: docs/design/weather.md#落雷 — strikes draw to tall/conductive/isolated objects, hurt units in the circle, ignite.
    public sealed class EncounterLightningTests
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
                    if (math.distance(c.Center, worldPoint) <= radiusM) into.Add(c);
                }
            }
        }

        private const float R = 640f;
        private const int Dt = 50;
        private static readonly AiCommand Free = new AiCommand(AiCommandKind.Free, CombatAi.NoTarget);
        private PlanetConfig _planet;
        private CombatBalance _balance;

        private static Substance Wood() =>
            new Substance("wood", Element.Wood, Flammability.Slow, false, Brittleness.Never, 900, 900, 1000, new int[10]);

        private static EnvMaterials Env()
        {
            var none = new int[10];
            var stone = new Substance("stone", Element.Earth, Flammability.None, false, Brittleness.Frozen, 900, 2500, 2000, none);
            Substance S(string id, Element e) => new Substance(id, e, Flammability.None, false, Brittleness.Never, 500, 900, 300, none);
            var ground = new[] { S("metal", Element.Metal), Wood(), S("pectin", Element.Water), S("charcoal", Element.Fire), stone,
                S("ice", Element.Ice), S("cloth", Element.Wind), S("chip", Element.Thunder), null, null };
            var buildings = new Composition[EnvMaterials.BuildingMaterialCount];
            for (int i = 0; i < buildings.Length; i++) buildings[i] = Composition.Of(stone);
            return new EnvMaterials(100, 250, 150, 5000, 200, 150, 200, new VolumeChance(1000, 0, 1000),
                new VolumeChance(0, 0, 0), 30, 500, buildings, ground, stone);
        }

        private readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in _created) Object.DestroyImmediate(o);
            _created.Clear();
        }

        private CreatureDef Creature(string id, int thunderResist)
        {
            var m = ScriptableObject.CreateInstance<MoveDef>();
            _created.Add(m);
            m.SetId("move_" + id);
            m.Set(Element.Fire, MoveKind.Damage, 40, null, 0, 0, 1500, 100, RangeClass.Melee, MoveShape.Front, 2000,
                MoveMovement.None, BurstOrigin.Caster);
            var c = ScriptableObject.CreateInstance<CreatureDef>();
            _created.Add(c);
            c.SetId(id);
            var resist = new int[10];
            resist[(int)Element.Thunder] = thunderResist;
            TestWild.WithHabit(c, Habit.Avoidant).Set("line_" + id, CreatureStage.Juvenile, CreatureSizeClass.M, AiStyle.Charge, null, null, 0,
                new List<EvolutionMaterial>(), resist, new List<ElementValue> { new ElementValue(Element.Fire, 15) },
                new CreatureStats(78, 25, 25, 25, 25, 25), m, null, "{}", false);
            return c;
        }

        [Test]
        public void A_creature_in_the_circle_loses_a_share_of_max_hp_unless_it_resists_thunder()
        {
            Encounter e = Storm(new int2(1000, 1000), 1f, out _);
            EncounterUnit plain = e.AddWildT(Creature("plain", 0), null, At(0f), 10);
            EncounterUnit grounded = e.AddWildT(Creature("grounded", 100), null, At(0.5f), 10);
            int max = plain.Combatant.MaxHp;
            var (_, events, _) = Run(e, _planet.ToWorld(At(300f), 0f), 6_000);
            EncounterEvent hit = events.Find(ev => ev.Kind == EncounterEventKind.Struck && ev.TargetId == plain.Id);
            Assert.That(hit.Kind, Is.EqualTo(EncounterEventKind.Struck), "a strike must land within 6 s");
            Assert.That(hit.SourceId, Is.EqualTo(Encounter.NoSource));
            Assert.That(hit.Amount, Is.InRange(max * 80 / 1000 - 1, max * 80 / 1000 + 1));
            EncounterEvent immune = events.Find(ev => ev.Kind == EncounterEventKind.Struck && ev.TargetId == grounded.Id);
            Assert.That(immune.Amount, Is.Zero, "resist >= 100 % is immune");
            Assert.That(grounded.Combatant.Hp, Is.EqualTo(grounded.Combatant.MaxHp));
        }

        [SetUp]
        public void SetUp()
        {
            _planet = new PlanetConfig(R, float3.zero);
            _balance = CombatBalance.FromJson(File.ReadAllText(BalanceFixture.RepoPath("Data", "balance.json")));
        }

        private static float3 At(float alongM) => new float3(math.sin(alongM / R), math.cos(alongM / R), 0f);

        private Encounter Storm(int2 storm, float regionRadiusM, out PlayerVitals vitals, ulong seed = 11)
        {
            var e = new Encounter(_planet, _balance, new FlatSurface(), 7);
            vitals = new PlayerVitals(new PlayerVitalsConfig(1000, 600, 400, 800));
            e.AttachPlayer(vitals, 100, 100);
            e.AttachLightning(seed, _ => 0, _ => At(0f), regionRadiusM, (d, t) => storm);
            return e;
        }

        private static (List<LightningStrike> landed, List<EncounterEvent> events, List<ObjectEvent> objects) Run(
            Encounter e, float3 player, long untilMs)
        {
            var landed = new List<LightningStrike>();
            var events = new List<EncounterEvent>();
            var objects = new List<ObjectEvent>();
            for (long now = Dt; now <= untilMs; now += Dt)
            {
                e.Tick(now, Dt, player, Free, CombatAi.NoTarget);
                foreach (LightningStrike s in e.PendingStrikes) Assert.That(s.LandsAtMs, Is.GreaterThan(now));
                landed.AddRange(e.LandedStrikes);
                events.AddRange(e.Events);
                objects.AddRange(e.ObjectEvents);
            }

            return (landed, events, objects);
        }

        [Test]
        public void The_tallest_object_near_the_point_takes_the_strike_and_wood_ignites()
        {
            Encounter e = Storm(new int2(1000, 0), 4f, out _);
            var index = new ListIndex();
            index.Items.Add(new DestructibleCandidate(7, _planet.ToWorld(At(2f), 3f), 0.8f, Composition.Of(Wood()), 1_000_000));
            index.Items.Add(new DestructibleCandidate(8, _planet.ToWorld(At(-2f), 0.5f), 0.4f, Composition.Of(Wood()), 50));
            e.AttachDestructibles(new DestructibleTable(Env()), index, d => false);

            var (landed, _, objects) = Run(e, _planet.ToWorld(At(200f), 0f), 60_000);
            Assert.That(landed.Count, Is.GreaterThan(3), "a full thunderstorm strikes every few seconds");
            // The big tree takes every strike while it stands; after it burns down the small tree, then open ground.
            int lastBig = landed.FindLastIndex(s => s.ObjectId == 7);
            Assert.That(lastBig, Is.GreaterThanOrEqualTo(2));
            for (int i = 0; i <= lastBig; i++) Assert.That(landed[i].ObjectId, Is.EqualTo(7));
            ObjectEvent first = objects.Find(ev => ev.Kind == ObjectEventKind.Hit);
            Assert.That(first.ObjectId, Is.EqualTo(7));
            Assert.That(first.SourceId, Is.EqualTo(Encounter.NoSource));
            Assert.That((first.Reactions & ObjectReaction.Ignited) != 0, Is.True);
        }

        [Test]
        public void Only_a_player_inside_the_circle_is_hurt()
        {
            Encounter near = Storm(new int2(1000, 1000), 2f, out PlayerVitals inside);
            var (landedNear, eventsNear, _) = Run(near, _planet.ToWorld(At(0f), 0f), 30_000);
            Assert.That(landedNear.Count, Is.GreaterThan(0));
            EncounterEvent struck = eventsNear.Find(ev => ev.Kind == EncounterEventKind.Struck);
            Assert.That(struck.TargetId, Is.EqualTo(Encounter.PlayerCharacterId));
            Assert.That(struck.Amount, Is.EqualTo(80), "8 % of 1000 max HP");
            Assert.That(inside.Hp, Is.LessThan(1000));

            Encounter far = Storm(new int2(1000, 1000), 2f, out PlayerVitals outside);
            var (_, eventsFar, _) = Run(far, _planet.ToWorld(At(60f), 0f), 30_000);
            Assert.That(eventsFar.Exists(ev => ev.Kind == EncounterEventKind.Struck), Is.False);
            Assert.That(outside.Hp, Is.EqualTo(1000));
        }

        [Test]
        public void Same_seed_gives_the_same_strikes_and_a_calm_sky_none()
        {
            List<LightningStrike> A(ulong seed) =>
                Run(Storm(new int2(700, 0), 40f, out _, seed), _planet.ToWorld(At(300f), 0f), 60_000).landed;
            List<LightningStrike> a = A(3), b = A(3), c = A(4);
            Assert.That(a.Count, Is.GreaterThan(0));
            Assert.That(b.Count, Is.EqualTo(a.Count));
            for (int i = 0; i < a.Count; i++)
            {
                Assert.That(b[i].LandsAtMs, Is.EqualTo(a[i].LandsAtMs));
                Assert.That(math.all(b[i].Direction == a[i].Direction), Is.True);
            }

            Assert.That(c.Count != a.Count || (c.Count > 0 && c[0].LandsAtMs != a[0].LandsAtMs), Is.True);
            Assert.That(Run(Storm(new int2(0, 0), 40f, out _), _planet.ToWorld(At(0f), 0f), 60_000).landed, Is.Empty);
        }

        [Test]
        public void Bad_attachment_is_rejected()
        {
            var e = new Encounter(_planet, _balance, new FlatSurface(), 7);
            Assert.Throws<ContractException>(() => e.AttachLightning(1, null, r => At(0f), 5f, (d, t) => default));
            Assert.Throws<ContractException>(() => e.AttachLightning(1, d => 0, r => At(0f), 0f, (d, t) => default));
            e.AttachLightning(1, d => 0, r => At(0f), 5f, (d, t) => default);
            Assert.Throws<ContractException>(() => e.AttachLightning(1, d => 0, r => At(0f), 5f, (d, t) => default));
        }
    }
}
