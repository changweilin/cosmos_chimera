using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.Core.Random;
using NUnit.Framework;

namespace Cosmimera.Capture.Tests
{
    public sealed class EggHatcheryTests
    {
        private static EggHatchery Make(out IncubatingEgg egg)
        {
            var h = new EggHatchery(HatchConfig.Default);
            egg = new IncubatingEgg("egg_a", 1);
            Assert.That(h.TryPlace(egg), Is.True);
            return h;
        }

        private static EggDef Def(params (string id, int w)[] pool)
        {
            var list = new List<EggOutcome>();
            foreach (var p in pool) list.Add(new EggOutcome(p.id, p.w));
            return new EggDef("egg_a", list);
        }

        [Test]
        public void Walking_one_circumference_is_full_progress()
        {
            var h = Make(out var egg);
            h.AddWalk(2_010_000, 2_010_000, false);
            Assert.That(egg.IsReady, Is.True);
        }

        [Test]
        public void Walk_accumulates_exactly_in_small_steps()
        {
            var h = Make(out var egg);
            for (int i = 0; i < 100; i++) h.AddWalk(1000, 100_000, false);
            Assert.That(egg.ProgressPpm, Is.EqualTo(HatchConfig.FullProgressPpm));
        }

        [Test]
        public void Battle_is_two_percent_and_boss_is_ten_percent()
        {
            var h = Make(out var egg);
            h.AddBattles(1, false);
            Assert.That(egg.ProgressPpm, Is.EqualTo(20_000));
            h.AddBossVictory(false);
            Assert.That(egg.ProgressPpm, Is.EqualTo(120_000));
        }

        [Test]
        public void Boost_scales_gain_and_progress_caps_at_full()
        {
            var h = Make(out var egg);
            h.AddBattles(1, true);
            Assert.That(egg.ProgressPpm, Is.EqualTo(30_000));
            h.AddBattles(1000, true);
            Assert.That(egg.ProgressPpm, Is.EqualTo(HatchConfig.FullProgressPpm));
        }

        [Test]
        public void Zero_events_change_nothing()
        {
            var h = Make(out var egg);
            h.AddWalk(0, 1000, true);
            h.AddBattles(0, true);
            Assert.That(egg.ProgressPpm, Is.EqualTo(0));
        }

        [Test]
        public void Place_fails_when_full_and_leaves_state_unchanged()
        {
            var h = new EggHatchery(new HatchConfig(1, 2, 10, 0));
            Assert.That(h.TryPlace(new IncubatingEgg("egg_a", 0)), Is.True);
            Assert.That(h.TryPlace(new IncubatingEgg("egg_b", 0)), Is.False);
            Assert.That(h.Eggs.Count, Is.EqualTo(1));
        }

        [Test]
        public void Hatch_requires_ready_egg()
        {
            var h = Make(out var egg);
            var rng = new Pcg32(1, 1);
            Assert.Throws<ContractException>(() => h.Hatch(egg, Def(("sp_a", 1)), ref rng));
        }

        [Test]
        public void Hatch_removes_egg_and_inherits_vessel()
        {
            var h = Make(out var egg);
            h.AddBossVictory(false);
            h.AddWalk(1, 1, false);
            var rng = new Pcg32(7, 3);
            var result = h.Hatch(egg, Def(("sp_a", 1)), ref rng);
            Assert.That(result.SpeciesId, Is.EqualTo("sp_a"));
            Assert.That(result.Vessel, Is.EqualTo(1));
            Assert.That(h.Eggs.Count, Is.EqualTo(0));
        }

        [Test]
        public void Roll_is_deterministic_and_respects_weights()
        {
            var def = Def(("sp_a", 1), ("sp_b", 3));
            var r1 = new Pcg32(42, 1);
            var r2 = new Pcg32(42, 1);
            int b = 0;
            for (int i = 0; i < 4000; i++)
            {
                string s1 = def.Roll(ref r1);
                Assert.That(def.Roll(ref r2), Is.EqualTo(s1));
                if (s1 == "sp_b") b++;
            }
            Assert.That(b, Is.InRange(2850, 3150));
        }

        [Test]
        public void Def_rejects_duplicate_species_and_empty_pool()
        {
            Assert.Throws<ContractException>(() => Def(("sp_a", 1), ("sp_a", 2)));
            Assert.Throws<ContractException>(() => new EggDef("egg_a", new List<EggOutcome>()));
        }
    }
}
