using System.Collections.Generic;
using System.IO;
using Cosmimera.Core.Contracts;
using Cosmimera.GameData;
using Cosmimera.Planet;
using NUnit.Framework;
using Unity.Mathematics;

namespace Cosmimera.Combat.Tests
{
    // REQ: docs/design/destructibles.md#延燒 — fire spreads between flammables, faster downwind, slowed and doused by rain.
    public sealed class FireSpreadTests
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
        private static readonly int[] None = new int[10];
        private static readonly Substance Plant = new Substance("plant", Element.Wood, Flammability.Fast, false, Brittleness.Never, 500, 400, 300, None);
        private static readonly Substance Wood = new Substance("wood", Element.Wood, Flammability.Slow, false, Brittleness.Never, 900, 900, 1000, None);
        private static readonly Substance Kerosene = new Substance("kerosene", Element.Fire, Flammability.Blast, false, Brittleness.Never, 500, 800, 300, None);
        private static readonly Substance Stone = new Substance("stone", Element.Earth, Flammability.None, false, Brittleness.Frozen, 900, 2500, 2000, None);
        private static readonly Substance Wire = new Substance("wire", Element.Thunder, Flammability.None, true, Brittleness.Never, 500, 900, 300, None);
        private PlanetConfig _planet;
        private CombatBalance _balance;

        private static EnvMaterials Env()
        {
            Substance S(string id, Element e) => new Substance(id, e, Flammability.None, false, Brittleness.Never, 500, 900, 300, None);
            var ground = new[] { S("metal", Element.Metal), Wood, S("pectin", Element.Water), S("charcoal", Element.Fire), Stone,
                S("ice", Element.Ice), S("cloth", Element.Wind), S("chip", Element.Thunder), null, null };
            var buildings = new Composition[EnvMaterials.BuildingMaterialCount];
            for (int i = 0; i < buildings.Length; i++) buildings[i] = Composition.Of(Stone);
            return new EnvMaterials(100, 250, 150, 5000, 200, 150, 200, new VolumeChance(1000, 0, 1000),
                new VolumeChance(0, 0, 0), 30, 500, buildings, ground, Stone);
        }

        [SetUp]
        public void SetUp()
        {
            _planet = new PlanetConfig(R, float3.zero);
            _balance = CombatBalance.FromJson(File.ReadAllText(BalanceFixture.RepoPath("Data", "balance.json")));
        }

        // East of the start along the equator-like great circle used by the other encounter tests.
        private static float3 At(float alongM, float sideM = 0f) =>
            math.normalize(new float3(math.sin(alongM / R), math.cos(alongM / R), sideM / R));

        private DestructibleCandidate Item(long id, float alongM, Substance s, float sideM = 0f, int volumeL = 100_000) =>
            new DestructibleCandidate(id, _planet.ToWorld(At(alongM, sideM), 0.5f), 0.5f, Composition.Of(s), volumeL);

        private (Encounter e, DestructibleTable table, ListIndex index, PlayerVitals vitals) World(int2 wind, int wet)
        {
            var e = new Encounter(_planet, _balance, new FlatSurface(), 7);
            var vitals = new PlayerVitals(new PlayerVitalsConfig(1000, 600, 400, 800));
            e.AttachPlayer(vitals, 100, 100);
            var index = new ListIndex();
            var table = new DestructibleTable(Env());
            e.AttachDestructibles(table, index, d => false);
            e.SetFireEnvironment(5, (d, t) => wind, (d, t) => wet);
            return (e, table, index, vitals);
        }

        private static void Light(DestructibleTable table, in DestructibleCandidate c) =>
            table.Ignite(table.Register(c.Id, c.Composition, c.VolumeL, c.Center), 0);

        private static List<ObjectEvent> Run(Encounter e, float3 player, long untilMs, List<EncounterEvent> events = null)
        {
            var all = new List<ObjectEvent>();
            for (long now = Dt; now <= untilMs; now += Dt)
            {
                e.Tick(now, Dt, player, Free, CombatAi.NoTarget);
                all.AddRange(e.ObjectEvents);
                events?.AddRange(e.Events);
            }

            return all;
        }

        private static bool Ignited(List<ObjectEvent> events, long id) =>
            events.Exists(ev => ev.ObjectId == id && (ev.Reactions & ObjectReaction.Ignited) != 0);

        private float3 Far => _planet.ToWorld(At(200f), 0f);

        [Test]
        public void Fire_reaches_a_near_flammable_but_not_a_far_one_or_stone()
        {
            var (e, table, index, _) = World(int2.zero, 0);
            DestructibleCandidate source = Item(1, 0f, Plant);
            index.Items.Add(source);
            index.Items.Add(Item(2, 2f, Plant));
            index.Items.Add(Item(3, 10f, Plant));
            index.Items.Add(Item(4, 1.5f, Stone));
            Light(table, source);
            List<ObjectEvent> events = Run(e, Far, 6_000);
            Assert.That(Ignited(events, 2), Is.True);
            Assert.That(Ignited(events, 3), Is.False, "10 m is beyond grass fire's 4 m reach");
            Assert.That(events.Exists(ev => ev.ObjectId == 4), Is.False, "stone never catches");
        }

        [Test]
        public void Wind_carries_fire_downwind_and_holds_it_upwind()
        {
            // Heading 0 = local east; At(+x) lies east of At(0) here, so +5 m is downwind and -5 m upwind.
            var (e, table, index, _) = World(new int2(0, 1000), 0);
            DestructibleCandidate source = Item(1, 0f, Wood);
            index.Items.Add(source);
            // At(0) is the pole, where PlanetFrame's east falls back to +z: put the neighbours along z.
            float3 east = Cosmimera.WorldGen.WindField.Tangent(At(0f), 0);
            float3 toPlus = math.normalize(_planet.ToWorld(At(0f, 5f), 0.5f) - source.Center);
            Assert.That(math.abs(math.dot(east, toPlus)), Is.GreaterThan(0.9f));
            float sign = math.dot(east, toPlus) > 0f ? 1f : -1f;
            index.Items.Add(Item(2, 0f, Plant, 5f * sign));
            index.Items.Add(Item(3, 0f, Plant, -5f * sign));
            Light(table, source);
            List<ObjectEvent> events = Run(e, Far, 12_000);
            Assert.That(Ignited(events, 2), Is.True, "downwind reach 7 × 1.6 = 11.2 m");
            Assert.That(Ignited(events, 3), Is.False, "upwind reach 7 × 0.5 = 3.5 m");
        }

        [Test]
        public void Rain_slows_spread_and_puts_fires_out()
        {
            long FirstSpreadMs(int wet)
            {
                var (e, table, index, _) = World(int2.zero, wet);
                DestructibleCandidate source = Item(1, 0f, Wood);
                index.Items.Add(source);
                index.Items.Add(Item(2, 2f, Wood));
                Light(table, source);
                for (long now = Dt; now <= 30_000; now += Dt)
                {
                    e.Tick(now, Dt, Far, Free, CombatAi.NoTarget);
                    if (Ignited(new List<ObjectEvent>(e.ObjectEvents), 2)) return now;
                }

                return long.MaxValue;
            }

            Assert.That(FirstSpreadMs(500), Is.GreaterThan(FirstSpreadMs(0)));

            var (wetE, wetTable, wetIndex, _) = World(int2.zero, 1000);
            for (int i = 0; i < 6; i++)
            {
                // 40 m apart: each burns alone, so every one is a separate chance for the rain.
                DestructibleCandidate burning = Item(10 + i, 40f * i, Wood);
                wetIndex.Items.Add(burning);
                Light(wetTable, burning);
            }

            List<ObjectEvent> doused = Run(wetE, Far, 12_000);
            Assert.That(doused.Exists(ev => (ev.Reactions & ObjectReaction.Extinguished) != 0), Is.True);
        }

        [Test]
        public void Kerosene_blasts_ignite_neighbours_and_hurt_the_player()
        {
            var (e, table, index, vitals) = World(int2.zero, 0);
            DestructibleCandidate grass = Item(1, 0f, Plant);
            index.Items.Add(grass);
            index.Items.Add(Item(2, 2f, Kerosene));
            index.Items.Add(Item(3, 5f, Wood));
            Light(table, grass);
            var unitEvents = new List<EncounterEvent>();
            List<ObjectEvent> events = Run(e, _planet.ToWorld(At(3.5f), 0f), 6_000, unitEvents);
            Assert.That(events.Exists(ev => ev.ObjectId == 2 && (ev.Reactions & ObjectReaction.Blasted) != 0), Is.True);
            Assert.That(table.IsDestroyed(2), Is.True);
            Assert.That(Ignited(events, 3), Is.True, "the blast reaches 4 m past the drum (3 m away)");
            Assert.That(unitEvents.Exists(ev => ev.Kind == EncounterEventKind.Scorched && ev.TargetId == Encounter.PlayerCharacterId
                && ev.Amount == 150), Is.True, "15 % of 1000 max HP");
        }

        [Test]
        public void Standing_in_flames_burns_the_player_each_second()
        {
            var (e, table, index, vitals) = World(int2.zero, 0);
            DestructibleCandidate tree = Item(1, 0f, Wood, 0f, 10_000_000);
            index.Items.Add(tree);
            Light(table, tree);
            var unitEvents = new List<EncounterEvent>();
            Run(e, _planet.ToWorld(At(1f), 0f), 3_000, unitEvents);
            int burns = unitEvents.FindAll(ev => ev.Kind == EncounterEventKind.Scorched).Count;
            Assert.That(burns, Is.EqualTo(3));
            Assert.That(vitals.Hp, Is.EqualTo(1000 - 3 * 40));
        }

        [Test]
        public void Fire_kinds_and_wind_stretch_follow_the_table()
        {
            Assert.That(FireRules.KindOf(Composition.Of(Plant)), Is.EqualTo(FireKind.Grass));
            Assert.That(FireRules.KindOf(Composition.Of(Wood)), Is.EqualTo(FireKind.Crown));
            Assert.That(FireRules.KindOf(Composition.Of(Kerosene)), Is.EqualTo(FireKind.Chemical));
            Assert.That(FireRules.KindOf(new Composition(new[] { Wood, Stone }, new[] { 4, 3 })), Is.EqualTo(FireKind.Structure));
            Assert.That(FireRules.KindOf(new Composition(new[] { Wood, Wire }, new[] { 3, 1 })), Is.EqualTo(FireKind.Electrical));
            Assert.That(FireRules.WindStretch(1f, 0), Is.EqualTo(1f));
            Assert.That(FireRules.WindStretch(1f, 1000), Is.EqualTo(FireRules.DownwindStretch).Within(1e-5f));
            Assert.That(FireRules.WindStretch(-1f, 1000), Is.EqualTo(FireRules.UpwindStretch).Within(1e-5f));
            Assert.That(FireRules.WetSlowPermille(0), Is.EqualTo(1000));
            Assert.That(FireRules.WetSlowPermille(1000), Is.EqualTo(300));
            Assert.That(FireRules.DouseChancePermille(500), Is.Zero);
            Assert.That(FireRules.DouseChancePermille(1000), Is.EqualTo(120));
            Assert.Throws<ContractException>(() => FireRules.KindOf(null));
        }

        [Test]
        public void Table_ignite_and_extinguish_keep_their_contracts()
        {
            var table = new DestructibleTable(Env());
            int stone = table.Register(1, Composition.Of(Stone), 100, float3.zero);
            int wood = table.Register(2, Composition.Of(Wood), 100, float3.zero);
            int drum = table.Register(3, Composition.Of(Kerosene), 100, float3.zero);
            Assert.That(table.Ignite(stone, 0).Reactions, Is.EqualTo(ObjectReaction.None));
            Assert.That(table.Ignite(wood, 0).Reactions, Is.EqualTo(ObjectReaction.Ignited));
            Assert.That(table.Ignite(wood, 0).Reactions, Is.EqualTo(ObjectReaction.None), "already burning");
            ObjectHitResult blast = table.Ignite(drum, 0);
            Assert.That(blast.Destroyed && (blast.Reactions & ObjectReaction.Blasted) != 0, Is.True);
            Assert.That(table.CauseOf(drum), Is.EqualTo(DestroyCause.Burnt));
            Assert.That(table.Extinguish(wood), Is.True);
            Assert.That(table.Extinguish(wood), Is.False);
            Assert.Throws<ContractException>(() => table.Ignite(drum, 0));
            Assert.Throws<ContractException>(() => table.Ignite(stone, -1));
            Assert.Throws<ContractException>(() => table.Extinguish(9));
        }
    }
}
