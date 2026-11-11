using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.Core.Random;
using Cosmimera.GameData;
using NUnit.Framework;

namespace Cosmimera.Combat.Tests
{
    // Tuning mirrors the tentative Data/env-materials draft; assertions test reactions and invariants, not balance.
    public sealed class DestructiblesTests
    {
        private static Substance S(string id, Element el, Flammability f, bool cond, Brittleness b, int opacity, int density, int hp)
        {
            var resist = new int[10];
            if (f != Flammability.None || el == Element.Ice) resist[(int)Element.Fire] = -50;
            if (cond) resist[(int)Element.Thunder] = -50;
            if (b == Brittleness.Always)
            {
                resist[(int)Element.Earth] = -50;
                resist[(int)Element.Metal] = -50;
            }

            return new Substance(id, el, f, cond, b, opacity, density, hp, resist);
        }

        private static readonly Substance Metal = S("metal", Element.Metal, Flammability.None, true, Brittleness.Never, 900, 7800, 3000);
        private static readonly Substance Wood = S("wood", Element.Wood, Flammability.Slow, false, Brittleness.Never, 900, 900, 1000);
        private static readonly Substance Plant = S("plant", Element.Wood, Flammability.Fast, false, Brittleness.Never, 500, 400, 300);
        private static readonly Substance Pectin = S("pectin", Element.Water, Flammability.None, false, Brittleness.Never, 250, 900, 300);
        private static readonly Substance Kerosene = S("kerosene", Element.Fire, Flammability.Blast, false, Brittleness.Never, 500, 900, 400);
        private static readonly Substance Charcoal = S("charcoal", Element.Fire, Flammability.Slow, false, Brittleness.Never, 900, 400, 300);
        private static readonly Substance Stone = S("stone", Element.Earth, Flammability.None, false, Brittleness.Frozen, 900, 2500, 2000);
        private static readonly Substance Crystal = S("crystal", Element.Earth, Flammability.None, false, Brittleness.Always, 250, 900, 800);
        private static readonly Substance Ice = S("ice", Element.Ice, Flammability.None, false, Brittleness.Always, 250, 900, 500);
        private static readonly Substance Glass = S("glass", Element.Ice, Flammability.None, false, Brittleness.Always, 50, 900, 200);
        private static readonly Substance Cloth = S("cloth", Element.Wind, Flammability.Fast, false, Brittleness.Never, 500, 100, 150);
        private static readonly Substance Chip = S("chip", Element.Thunder, Flammability.None, true, Brittleness.Always, 900, 900, 600);

        private static Substance[] Ground() => new[] { Metal, Wood, Pectin, Charcoal, Stone, Ice, Cloth, Chip, null, null };

        private static Composition[] Buildings() => new[]
        {
            new Composition(new[] { Cloth, Wood }, new[] { 3, 1 }),
            new Composition(new[] { Wood, Stone, Glass }, new[] { 4, 3, 2 }),
            new Composition(new[] { Wood, Stone }, new[] { 4, 4 }),
            new Composition(new[] { Stone, Metal }, new[] { 4, 3 }),
            new Composition(new[] { Stone, Crystal }, new[] { 4, 1 }),
            new Composition(new[] { Stone, Metal, Chip }, new[] { 5, 2, 1 }),
        };

        private static EnvMaterials Env(int shadowCap = 500) => new EnvMaterials(100, 250, 150, 5000, 200, 150, 200,
            new VolumeChance(50, 20, 600), new VolumeChance(0, 1, 20), 30, shadowCap, Buildings(), Ground(), Crystal);

        private static DestructibleTable Table() => new DestructibleTable(Env());

        [Test]
        public void Hp_scales_with_volume_and_has_a_floor_of_one()
        {
            DestructibleTable t = Table();
            int a = t.Register(1, Composition.Of(Wood), 2000);
            int b = t.Register(2, Composition.Of(Cloth), 1);
            Assert.AreEqual(2000, t.MaxHp(a));
            Assert.AreEqual(t.MaxHp(a), t.Hp(a));
            Assert.AreEqual(1, t.MaxHp(b));
            Assert.AreEqual(1800, t.MassKg(a));
        }

        [Test]
        public void Register_contract_violations_throw()
        {
            DestructibleTable t = Table();
            Assert.Throws<ContractException>(() => t.Register(1, null, 10));
            Assert.Throws<ContractException>(() => t.Register(1, Composition.Of(Wood), 0));
            t.Register(1, Composition.Of(Wood), 10);
            Assert.Throws<ContractException>(() => t.Register(1, Composition.Of(Wood), 10));
            Assert.Throws<ContractException>(() => t.Unregister(99));
            Assert.Throws<ContractException>(() => new DestructibleTable(null));
        }

        [Test]
        public void Destroyed_id_is_not_regrown_after_a_lod_rebuild()
        {
            DestructibleTable t = Table();
            int i = t.Register(7, Composition.Of(Wood), 100);
            ObjectHitResult r = t.ApplyHit(i, Element.Earth, 100_000, 0, 0);
            Assert.IsTrue(r.Destroyed);
            t.Unregister(7);
            Assert.AreEqual(-1, t.Register(7, Composition.Of(Wood), 100));
            Assert.IsTrue(t.IsDestroyed(7));
            Assert.AreEqual(-1, t.IndexOf(7));
        }

        [Test]
        public void Damaged_object_keeps_its_hp_across_unload_and_reload()
        {
            DestructibleTable t = Table();
            int i = t.Register(3, Composition.Of(Stone), 1000);
            int dealt = t.ApplyHit(i, Element.Wind, 50, 0, 0).Damage;
            t.Unregister(3);
            Assert.AreEqual(-1, t.IndexOf(3));
            int j = t.Register(3, Composition.Of(Stone), 1000);
            Assert.AreEqual(i, j);
            Assert.AreEqual(t.MaxHp(j) - dealt, t.Hp(j));
        }

        [Test]
        public void Plain_hit_uses_the_damage_formula_with_substance_resist()
        {
            DestructibleTable t = Table();
            int i = t.Register(1, Composition.Of(Stone), 10_000);
            ObjectHitResult r = t.ApplyHit(i, Element.Wind, 60, 50, 0);
            Assert.AreEqual(DamageModel.Compute(60, 50, 0), r.Damage);
            Assert.AreEqual(ObjectReaction.Gust, r.Reactions);
            Assert.IsFalse(r.Destroyed);
        }

        [Test]
        public void Fire_ignites_wood_and_burning_destroys_it_as_charcoal()
        {
            DestructibleTable t = Table();
            int i = t.Register(1, Composition.Of(Wood), 1000);
            ObjectHitResult r = t.ApplyHit(i, Element.Fire, 10, 0, 0);
            Assert.IsTrue((r.Reactions & ObjectReaction.Ignited) != 0);
            Assert.IsTrue(t.IsBurning(i));

            var destroyed = new List<int>();
            t.Tick(999, destroyed);
            int afterNoTick = t.Hp(i);
            Assert.AreEqual(t.MaxHp(i) - r.Damage, afterNoTick);
            t.Tick(1000, destroyed);
            Assert.AreEqual(afterNoTick - t.MaxHp(i) / 10, t.Hp(i));
            t.Tick(20_000, destroyed);
            CollectionAssert.AreEqual(new[] { i }, destroyed);
            Assert.AreEqual(0, t.Hp(i));
            Assert.IsFalse(t.IsBurning(i));

            var rng = new Pcg32(1, 1);
            Assert.AreEqual(Element.Fire, t.RollDrop(i, false, ref rng).Element);
        }

        [Test]
        public void Fast_burner_burns_faster_than_slow()
        {
            DestructibleTable t = Table();
            int slow = t.Register(1, Composition.Of(Wood), 1000);
            int fast = t.Register(2, Composition.Of(Plant), 3334);
            t.ApplyHit(slow, Element.Fire, 1, 0, 0);
            t.ApplyHit(fast, Element.Fire, 1, 0, 0);
            var destroyed = new List<int>();
            t.Tick(1000, destroyed);
            Assert.Greater((t.MaxHp(fast) - t.Hp(fast)) * 1000L / t.MaxHp(fast),
                (t.MaxHp(slow) - t.Hp(slow)) * 1000L / t.MaxHp(slow));
        }

        [Test]
        public void Water_and_ice_extinguish()
        {
            DestructibleTable t = Table();
            int a = t.Register(1, Composition.Of(Wood), 1000);
            int b = t.Register(2, Composition.Of(Wood), 1000);
            t.ApplyHit(a, Element.Fire, 1, 0, 0);
            t.ApplyHit(b, Element.Fire, 1, 0, 0);
            Assert.IsTrue((t.ApplyHit(a, Element.Water, 1, 0, 10).Reactions & ObjectReaction.Extinguished) != 0);
            Assert.IsTrue((t.ApplyHit(b, Element.Ice, 1, 0, 10).Reactions & ObjectReaction.Extinguished) != 0);
            Assert.IsFalse(t.IsBurning(a));
            Assert.IsFalse(t.IsBurning(b));
            int hp = t.Hp(a);
            t.Tick(10_000, new List<int>());
            Assert.AreEqual(hp, t.Hp(a));
        }

        [Test]
        public void Kerosene_blasts_on_ignition()
        {
            DestructibleTable t = Table();
            int i = t.Register(1, Composition.Of(Kerosene), 100_000);
            ObjectHitResult r = t.ApplyHit(i, Element.Fire, 1, 0, 0);
            Assert.IsTrue(r.Destroyed);
            Assert.AreEqual(DestroyCause.Burnt, r.Cause);
            Assert.IsTrue((r.Reactions & ObjectReaction.Blasted) != 0);
            Assert.AreEqual(t.MaxHp(i), r.Damage);
        }

        [Test]
        public void Fire_melts_ice_for_bonus_damage_and_drops_water()
        {
            DestructibleTable t = Table();
            int ice = t.Register(1, Composition.Of(Ice), 100_000);
            int glass = t.Register(2, Composition.Of(Glass), 100_000);
            ObjectHitResult melt = t.ApplyHit(ice, Element.Fire, 40, 0, 0);
            ObjectHitResult plain = t.ApplyHit(glass, Element.Fire, 40, 0, 0);
            Assert.IsTrue((melt.Reactions & ObjectReaction.Melted) != 0);
            Assert.AreEqual(ObjectReaction.None, plain.Reactions);
            Assert.AreEqual(plain.Damage * 3 / 2, melt.Damage);

            int small = t.Register(3, Composition.Of(Ice), 10);
            ObjectHitResult kill = t.ApplyHit(small, Element.Fire, 40, 0, 0);
            Assert.AreEqual(DestroyCause.Melted, kill.Cause);
            var rng = new Pcg32(5, 5);
            Assert.AreEqual(Element.Water, t.RollDrop(small, false, ref rng).Element);
        }

        [Test]
        public void Freeze_makes_stone_brittle_only_inside_the_window()
        {
            DestructibleTable t = Table();
            int i = t.Register(1, Composition.Of(Stone), 1_000_000);
            Assert.AreEqual(ObjectReaction.None, t.ApplyHit(i, Element.Earth, 10, 0, 0).Reactions);

            ObjectHitResult freeze = t.ApplyHit(i, Element.Ice, 10, 0, 100);
            Assert.IsTrue((freeze.Reactions & ObjectReaction.Froze) != 0);
            Assert.IsTrue(t.IsFrozen(i, 100));
            ObjectHitResult shatter = t.ApplyHit(i, Element.Earth, 10, 0, 5099);
            Assert.IsTrue((shatter.Reactions & ObjectReaction.Shattered) != 0);
            Assert.AreEqual(DamageModel.Compute(10, 0, 0) * 2, shatter.Damage);
            Assert.AreEqual(ObjectReaction.None, t.ApplyHit(i, Element.Metal, 10, 0, 5100).Reactions);
        }

        [Test]
        public void Ice_does_not_freeze_ice_or_glass_but_they_are_brittle()
        {
            DestructibleTable t = Table();
            int g = t.Register(1, Composition.Of(Glass), 1_000_000);
            Assert.IsTrue((t.ApplyHit(g, Element.Ice, 10, 0, 0).Reactions & ObjectReaction.Froze) == 0);
            Assert.IsTrue((t.ApplyHit(g, Element.Metal, 10, 0, 0).Reactions & ObjectReaction.Shattered) != 0);
        }

        [Test]
        public void Thunder_conducts_through_metal()
        {
            DestructibleTable t = Table();
            int m = t.Register(1, Composition.Of(Metal), 1_000_000);
            int s = t.Register(2, Composition.Of(Stone), 1_000_000);
            ObjectHitResult rm = t.ApplyHit(m, Element.Thunder, 100, 0, 0);
            ObjectHitResult rs = t.ApplyHit(s, Element.Thunder, 100, 0, 0);
            Assert.IsTrue((rm.Reactions & ObjectReaction.Conducted) != 0);
            Assert.AreEqual(ObjectReaction.None, rs.Reactions);
            Assert.Greater(rm.Damage, rs.Damage);
        }

        [Test]
        public void ApplyHit_contract_violations_throw()
        {
            DestructibleTable t = Table();
            int i = t.Register(1, Composition.Of(Cloth), 1);
            Assert.Throws<ContractException>(() => t.ApplyHit(5, Element.Fire, 1, 0, 0));
            Assert.Throws<ContractException>(() => t.ApplyHit(i, Element.Fire, -1, 0, 0));
            Assert.Throws<ContractException>(() => t.ApplyHit(i, Element.Fire, 1, 0, -1));
            t.ApplyHit(i, Element.Wind, 100, 0, 0);
            Assert.Throws<ContractException>(() => t.ApplyHit(i, Element.Wind, 1, 0, 0));
            Assert.Throws<ContractException>(() => t.Tick(0, null));
        }

        [Test]
        public void RollDrop_requires_a_destroyed_object()
        {
            DestructibleTable t = Table();
            int i = t.Register(1, Composition.Of(Stone), 100);
            var rng = new Pcg32(1, 1);
            Assert.Throws<ContractException>(() => t.RollDrop(i, false, ref rng));
        }

        [Test]
        public void Building_drop_element_comes_from_its_parts_and_is_reproducible()
        {
            Composition house = Env().Building(BuildingMaterial.House);
            var seen = new HashSet<Element>();
            for (int seed = 0; seed < 200; seed++)
            {
                DestructibleTable a = Table(), b = Table();
                int ia = a.Register(1, house, 50_000), ib = b.Register(1, house, 50_000);
                a.ApplyHit(ia, Element.Wind, 100_000, 0, 0);
                b.ApplyHit(ib, Element.Wind, 100_000, 0, 0);
                var ra = new Pcg32((ulong)seed, 9);
                var rb = new Pcg32((ulong)seed, 9);
                ObjectDrop da = a.RollDrop(ia, true, ref ra), db = b.RollDrop(ib, true, ref rb);
                Assert.AreEqual(da.Element, db.Element);
                Assert.AreEqual(da.Material, db.Material);
                Assert.AreEqual(da.Gem, db.Gem);
                Assert.AreEqual(da.Shadow, db.Shadow);
                seen.Add(da.Element);
            }

            CollectionAssert.AreEquivalent(new[] { Element.Wood, Element.Earth, Element.Ice }, seen);
        }

        [Test]
        public void No_shadow_by_day_even_at_full_chance()
        {
            var env = new EnvMaterials(100, 250, 150, 5000, 200, 150, 200, new VolumeChance(1000, 0, 1000),
                new VolumeChance(1000, 0, 1000), 1000, 1000, Buildings(), Ground(), Crystal);
            var t = new DestructibleTable(env);
            int i = t.Register(1, Composition.Of(Cloth), 1_000_000);
            t.ApplyHit(i, Element.Wind, 100_000, 0, 0);
            Assert.IsTrue(t.ApplyHit(i, Element.Wind, 100_000, 0, 0).Destroyed);
            var rng = new Pcg32(3, 3);
            ObjectDrop day = t.RollDrop(i, false, ref rng);
            Assert.IsTrue(day.Material);
            Assert.IsTrue(day.Gem);
            Assert.IsFalse(day.Shadow);
            Assert.IsTrue(t.RollDrop(i, true, ref rng).Shadow);
        }

        [Test]
        public void Shadow_chance_grows_with_size_and_opacity_and_respects_the_cap()
        {
            DestructibleTable t = Table();
            Assert.AreEqual(0, t.ShadowChancePermille(0, 900));
            Assert.Less(t.ShadowChancePermille(1000, 900), t.ShadowChancePermille(8000, 900));
            Assert.Less(t.ShadowChancePermille(8000, 50), t.ShadowChancePermille(8000, 900));
            // 8 m³ → 2 m side → 4 m² × 30‰ × 0.9 = 108‰.
            Assert.AreEqual(108, t.ShadowChancePermille(8000, 900));
            Assert.AreEqual(500, t.ShadowChancePermille(int.MaxValue, 1000));
            Assert.Throws<ContractException>(() => t.ShadowChancePermille(-1, 0));
            Assert.Throws<ContractException>(() => t.ShadowChancePermille(1, 1001));
        }

        [Test]
        public void Volume_chance_is_monotone_and_capped()
        {
            var c = new VolumeChance(50, 20, 600);
            Assert.AreEqual(50, c.ChancePermille(0));
            Assert.AreEqual(70, c.ChancePermille(1000));
            Assert.AreEqual(600, c.ChancePermille(int.MaxValue));
            Assert.Throws<ContractException>(() => c.ChancePermille(-1));
            Assert.Throws<ContractException>(() => new VolumeChance(0, 0, 1001));
        }

        [Test]
        public void Composition_dominant_and_opacity()
        {
            var c = new Composition(new[] { Wood, Glass }, new[] { 1, 3 });
            Assert.AreSame(Glass, c.Dominant);
            Assert.AreEqual((900 + 50 * 3) / 4, c.OpacityPermille);
            Assert.AreEqual(4, c.TotalWeight);
            Assert.AreSame(Wood, new Composition(new[] { Wood, Stone }, new[] { 2, 2 }).Dominant);
            Assert.Throws<ContractException>(() => new Composition(new[] { Wood }, new[] { 0 }));
            Assert.Throws<ContractException>(() => new Composition(new Substance[0], new int[0]));
        }

        [Test]
        public void Data_contracts_reject_impossible_rows()
        {
            Assert.Throws<ContractException>(() =>
                new Substance("x", Element.Shadow, Flammability.None, false, Brittleness.Never, 0, 1, 1, new int[10]));
            Assert.Throws<ContractException>(() =>
                new Substance("x", Element.Wood, Flammability.None, false, Brittleness.Never, 0, 1, 1, new int[9]));
            Substance[] wrong = Ground();
            wrong[(int)Element.Water] = Wood;
            Assert.Throws<ContractException>(() => new EnvMaterials(100, 250, 150, 5000, 200, 150, 200,
                new VolumeChance(0, 0, 0), new VolumeChance(0, 0, 0), 0, 0, Buildings(), wrong, Crystal));
            Assert.Throws<ContractException>(() => new EnvMaterials(100, 250, 99, 5000, 200, 150, 200,
                new VolumeChance(0, 0, 0), new VolumeChance(0, 0, 0), 0, 0, Buildings(), Ground(), Crystal));
        }

        [Test]
        public void Every_dropping_element_has_a_ground_substance_except_light_and_shadow()
        {
            EnvMaterials env = Env();
            Assert.IsNull(env.Ground(Element.Light));
            Assert.IsNull(env.Ground(Element.Shadow));
            Assert.AreSame(Ice, env.Ice);
            Assert.AreSame(Charcoal, env.Ground(Element.Fire));
        }
    }
}
