using System.IO;
using Cosmimera.Core.Contracts;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Cosmimera.Combat.Tests
{
    // Golden vectors pin bit-exact parity with tools/balance/derived_stats.py; the rest are invariants
    // (testing.md: balance values are never asserted as exact numbers except through the golden file).
    public sealed class DerivedStatsTests
    {
        private DerivedStats _m;

        [SetUp]
        public void SetUp() => _m = new DerivedStats(BalanceFixture.Coefficients());

        [Test]
        public void Matches_every_python_golden_vector()
        {
            JObject balance = BalanceFixture.BalanceJson();
            JObject golden = JObject.Parse(File.ReadAllText(
                BalanceFixture.RepoPath("tools", "balance", "golden_derived_stats.json")));
            Assert.AreEqual((int)balance["model_version"], (int)golden["model_version"]);
            JToken reference = balance["reference_move"];
            int count = 0;
            foreach (JToken row in golden["vectors"])
            {
                JToken i = row["input"];
                JToken o = row["output"];
                int con = (int)i["con"], str = (int)i["str"], spi = (int)i["spi"], agi = (int)i["agi"];
                int cd = (int)(i["cooldown_ms"] ?? reference["cooldown_ms"]);
                int acc = (int)(i["accuracy_percent"] ?? reference["accuracy_percent"]);
                int range = (int)(i["base_range_mm"] ?? reference["base_range_mm"]);
                string at = $"vector {count} ({con},{str},{spi},{agi})";

                Assert.AreEqual((int)o["max_hp"], _m.MaxHp(con), at);
                Assert.AreEqual((int)o["melee_volume_permille"], _m.MeleeVolumePermille(str), at);
                Assert.AreEqual((int)o["melee_linear_permille"], _m.MeleeLinearPermille(str), at);
                Assert.AreEqual((int)o["ranged_multiplier_permille"], _m.RangedPermille(str), at);
                Assert.AreEqual((int)o["ranged_distance_mm"], _m.RangedDistanceMm(range, str), at);
                Assert.AreEqual((int)o["cd_multiplier_permille"], _m.CooldownPermille(spi), at);
                Assert.AreEqual((int)o["effective_cd_ms"], _m.EffectiveCooldownMs(cd, spi), at);
                Assert.AreEqual((int)o["move_speed_mm_s"], _m.MoveSpeedMmS(agi), at);
                Assert.AreEqual((int)o["evasion_permille"], _m.EvasionPermille(agi), at);
                Assert.AreEqual((int)o["hit_chance_permille"], _m.HitChancePermille(acc, _m.EvasionPermille(agi)), at);
                count++;
            }

            Assert.AreEqual(40, count);
        }

        [Test]
        public void Curves_are_monotone_bounded_and_saturate()
        {
            DerivedStatsCoefficients c = _m.Coefficients;
            int cap = c.StatEffectiveCap;
            int prevHp = 0, prevVol = 0, prevLin = 0, prevRng = 0, prevSpd = 0, prevEva = -1;
            int prevCd = int.MaxValue, prevHit = int.MaxValue;
            for (int x = 0; x <= cap; x++)
            {
                int hp = _m.MaxHp(x), vol = _m.MeleeVolumePermille(x), lin = _m.MeleeLinearPermille(x);
                int rng = _m.RangedPermille(x), cd = _m.CooldownPermille(x), spd = _m.MoveSpeedMmS(x);
                int eva = _m.EvasionPermille(x), hit = _m.HitChancePermille(100, eva);
                Assert.That(hp >= prevHp && hp >= c.HpBase && hp <= c.HpBase + c.HpGain, $"hp at {x}");
                Assert.That(vol >= prevVol && vol >= 1000 && vol <= 1000 + c.MeleeVolumeGainPermille, $"vol at {x}");
                Assert.That(lin >= prevLin && lin >= 1000 && (long)lin * lin * lin <= (long)vol * 1_000_000, $"lin at {x}");
                Assert.That((long)(lin + 1) * (lin + 1) * (lin + 1) > (long)vol * 1_000_000, $"lin floor at {x}");
                Assert.That(rng >= prevRng && rng >= 1000 && rng <= 1000 + c.RangedGainPermille, $"rng at {x}");
                Assert.That(cd <= prevCd && cd >= c.CdMinPermille && cd <= 1000, $"cd at {x}");
                Assert.That(spd >= prevSpd && spd >= c.SpeedBaseMmS && spd <= c.SpeedBaseMmS + c.SpeedGainMmS, $"spd at {x}");
                Assert.That(eva >= prevEva && eva >= 0 && eva <= c.EvasionCapPermille, $"eva at {x}");
                Assert.That(hit <= prevHit && hit >= c.HitFloorPermille && hit <= 1000, $"hit at {x}");
                prevHp = hp; prevVol = vol; prevLin = lin; prevRng = rng; prevCd = cd;
                prevSpd = spd; prevEva = eva; prevHit = hit;
            }

            foreach (int big in new[] { cap + 1, 1_000_000, int.MaxValue })
            {
                Assert.AreEqual(_m.MaxHp(cap), _m.MaxHp(big));
                Assert.AreEqual(_m.MeleeLinearPermille(cap), _m.MeleeLinearPermille(big));
                Assert.AreEqual(_m.RangedPermille(cap), _m.RangedPermille(big));
                Assert.AreEqual(_m.CooldownPermille(cap), _m.CooldownPermille(big));
                Assert.AreEqual(_m.MoveSpeedMmS(cap), _m.MoveSpeedMmS(big));
                Assert.AreEqual(_m.EvasionPermille(cap), _m.EvasionPermille(big));
            }
        }

        [Test]
        public void Range_and_cooldown_respect_cap_and_floor_at_int32_extremes()
        {
            DerivedStatsCoefficients c = _m.Coefficients;
            Assert.AreEqual(c.RangedCapMm, _m.RangedDistanceMm(int.MaxValue, int.MaxValue));
            Assert.AreEqual(0, _m.RangedDistanceMm(0, int.MaxValue));
            Assert.AreEqual(c.CdFloorMs, _m.EffectiveCooldownMs(0, 0));
            Assert.AreEqual(int.MaxValue, _m.EffectiveCooldownMs(int.MaxValue, 0));
            Assert.That(_m.EffectiveCooldownMs(int.MaxValue, int.MaxValue), Is.LessThan(int.MaxValue));
            Assert.AreEqual(c.HitFloorPermille, _m.HitChancePermille(0, 0));
            Assert.AreEqual(c.HitFloorPermille, _m.HitChancePermille(100, 1000));
            Assert.AreEqual(1000, _m.HitChancePermille(100, 0));
        }

        [Test]
        public void Negative_inputs_violate_preconditions()
        {
            Assert.Throws<ContractException>(() => _m.MaxHp(-1));
            Assert.Throws<ContractException>(() => _m.MeleeVolumePermille(-1));
            Assert.Throws<ContractException>(() => _m.MeleeLinearPermille(-1));
            Assert.Throws<ContractException>(() => _m.RangedPermille(-1));
            Assert.Throws<ContractException>(() => _m.RangedDistanceMm(-1, 0));
            Assert.Throws<ContractException>(() => _m.RangedDistanceMm(0, -1));
            Assert.Throws<ContractException>(() => _m.CooldownPermille(-1));
            Assert.Throws<ContractException>(() => _m.EffectiveCooldownMs(-1, 0));
            Assert.Throws<ContractException>(() => _m.EffectiveCooldownMs(0, -1));
            Assert.Throws<ContractException>(() => _m.MoveSpeedMmS(-1));
            Assert.Throws<ContractException>(() => _m.EvasionPermille(-1));
            Assert.Throws<ContractException>(() => _m.HitChancePermille(-1, 0));
            Assert.Throws<ContractException>(() => _m.HitChancePermille(101, 0));
            Assert.Throws<ContractException>(() => _m.HitChancePermille(100, -1));
            Assert.Throws<ContractException>(() => _m.HitChancePermille(100, 1001));
            Assert.Throws<ContractException>(() => new DerivedStats(null));
        }

        [TestCase("model_version", 2)]
        [TestCase("stat_effective_cap", 0)]
        [TestCase("hp.base", 0)]
        [TestCase("hp.half_stat", 0)]
        [TestCase("melee_volume.gain_permille", 1001)]
        [TestCase("ranged_distance.gain_permille", 1001)]
        [TestCase("ranged_distance.cap_mm", 0)]
        [TestCase("cd.min_permille", 0)]
        [TestCase("cd.floor_ms", 0)]
        [TestCase("speed.base_mm_s", 0)]
        [TestCase("speed.gain_mm_s", 1_000_001)]
        [TestCase("evasion.cap_permille", 1000)]
        [TestCase("hit.floor_permille", 0)]
        public void Coefficients_outside_the_envelope_are_rejected(string path, int value)
        {
            JObject b = BalanceFixture.BalanceJson();
            b.SelectToken(path).Replace(value);

            Assert.Throws<ContractException>(() => BalanceFixture.FromJson(b));
        }
    }
}
