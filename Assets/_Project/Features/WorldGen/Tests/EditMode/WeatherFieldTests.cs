using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.GameData;
using NUnit.Framework;
using Unity.Mathematics;

namespace Cosmimera.WorldGen.Tests
{
    public sealed class WeatherFieldTests
    {
        private static readonly float3 Equator = new float3(1f, 0f, 0f);

        private static readonly string[] Planets =
        {
            "verdant_planet", "lava_planet", "ice_crystal_planet", "storm_planet", "mineral_planet", "twilight_planet",
        };

        private static readonly string[] Terrains =
        {
            "grassland", "forest", "swamp", "desert", "volcano", "volcano_foothills", "rift_valley", "basalt_terrace",
            "canyon", "karst", "ice_field", "ice_cap", "glacier", "tundra", "plateau", "mountain", "archipelago", "fjord",
            "eternal_day", "eternal_night", "laboratory", "rainforest", "deep_sea", "cave", "tower", "space", "labyrinth", "temple",
        };

        private static WeatherSite Site(string planet, string terrain, ulong seed = 12345, int region = 3) =>
            new WeatherSite(seed, planet, Seasons.ForPlanet(seed, planet), region, Equator, terrain);

        private static int[] At(in WeatherSite site, long t, IReadOnlyList<WeatherOverride> overrides = null)
        {
            var v = new int[WeatherField.AttrCount];
            WeatherField.Evaluate(site, t, overrides, v);
            return v;
        }

        [Test]
        public void Same_inputs_give_the_same_vector_and_seeds_differ()
        {
            WeatherSite a = Site("storm_planet", "plateau", 7);
            WeatherSite b = Site("storm_planet", "plateau", 8);
            bool differs = false;
            for (long t = 0; t < 3_600_000; t += 37_000)
            {
                Assert.That(At(a, t), Is.EqualTo(At(a, t)));
                differs |= !At(a, t).AsSpanEquals(At(b, t));
            }

            Assert.That(differs, Is.True);
        }

        [Test]
        public void Every_attribute_stays_in_range_and_changes_over_time()
        {
            foreach (string planet in Planets)
            foreach (string terrain in Terrains)
            {
                WeatherSite site = Site(planet, terrain);
                int[] first = At(site, 0);
                bool moved = false;
                for (long t = 0; t < 7_200_000; t += 61_000)
                {
                    int[] v = At(site, t);
                    foreach (int x in v) Assert.That(x, Is.InRange(0, 1000), $"{planet}/{terrain}@{t}");
                    moved |= !v.AsSpanEquals(first);
                }

                if (!site.Indoor) Assert.That(moved, Is.True, $"{planet}/{terrain} never changes");
            }
        }

        [Test]
        public void Exotics_never_appear_off_their_terrain()
        {
            foreach (string planet in Planets)
            foreach (string terrain in Terrains)
            {
                WeatherSite site = Site(planet, terrain);
                for (long t = 0; t < 3_600_000; t += 47_000)
                {
                    int[] v = At(site, t);
                    for (int i = (int)WeatherKind.RadDust; i <= (int)WeatherKind.Miasma; i++)
                    {
                        if (WeatherTables.ExoticBias((WeatherKind)i, planet, terrain) == 0)
                            Assert.That(v[i], Is.Zero, $"{(WeatherKind)i} on {planet}/{terrain}");
                    }
                }
            }

            Assert.That(WeatherTables.ExoticBias(WeatherKind.CrystalDust, "verdant_planet", "canyon"), Is.Zero, "planet-restricted");
            Assert.That(WeatherTables.ExoticBias(WeatherKind.CrystalDust, "mineral_planet", "canyon"), Is.GreaterThan(0));
        }

        [Test]
        public void Phase_locked_exotics_vanish_outside_their_phase()
        {
            WeatherSite swamp = Site("verdant_planet", "swamp");
            WeatherSite temple = Site("verdant_planet", "temple");
            for (long t = 0; t < 1_800_000; t += 5_000)
            {
                bool allDay = true, allNight = true;
                for (long d = -15_000; d <= 15_000; d += 5_000)
                {
                    DayPhase p = SolarClock.SunPhaseAt(Equator, math.max(0, t + d));
                    allDay &= p == DayPhase.Day;
                    allNight &= p == DayPhase.Night;
                }

                if (allDay) Assert.That(At(swamp, t)[(int)WeatherKind.Miasma], Is.Zero, "miasma only at dusk and night");
                if (allNight) Assert.That(At(temple, t)[(int)WeatherKind.SolarFlare], Is.Zero, "solar flare only by day");
            }
        }

        [Test]
        public void Native_exotics_do_take_effect_over_a_long_run()
        {
            bool seen = false;
            for (ulong seed = 1; seed <= 6 && !seen; seed++)
            {
                WeatherSite site = Site("lava_planet", "volcano", seed);
                for (long t = 0; t < 7_200_000 && !seen; t += 10_000)
                    seen = WeatherDynamics.Resolve(At(site, t)).Exotic == WeatherKind.VolcanicAsh;
            }

            Assert.That(seen, Is.True);
        }

        [Test]
        public void Indoor_sites_have_no_base_weather_and_the_cradle_is_always_clear()
        {
            WeatherSite lab = Site("mineral_planet", "laboratory");
            Assert.That(lab.Indoor, Is.True);
            WeatherSite cradle = Site("cradle_planet", "forest");
            int[] clear = At(cradle, 0);
            for (long t = 0; t < 3_600_000; t += 53_000)
            {
                int[] v = At(lab, t);
                for (int i = 0; i < WeatherTables.BaseCount; i++) Assert.That(v[i], Is.Zero);
                Assert.That(At(cradle, t), Is.EqualTo(clear));
                Assert.That(WeatherDynamics.Resolve(At(cradle, t)).BaseKind, Is.EqualTo(WeatherKind.Clear));
            }
        }

        [Test]
        public void Override_pins_for_the_hold_then_relaxes_and_never_leaks_an_exotic()
        {
            var control = new WeatherControl(1);
            control.TryApply(3, WeatherKind.Blizzard, 60_000, true, false, false);
            WeatherSite site = Site("verdant_planet", "grassland");
            IReadOnlyList<WeatherOverride> pins = control.OverridesFor(3);
            Assert.That(WeatherDynamics.Resolve(At(site, 60_000, pins)).BaseKind, Is.EqualTo(WeatherKind.Blizzard));
            Assert.That(At(site, 60_000 + WeatherField.OverrideHoldMs, pins)[(int)WeatherAttr.Snow], Is.EqualTo(920));
            Assert.That(At(site, 59_999, pins), Is.EqualTo(At(site, 59_999)), "nothing changes before the cast");
            long late = 60_000 + WeatherField.OverrideHoldMs + 3_600_000;
            Assert.That(At(site, late, pins)[(int)WeatherAttr.Snow],
                Is.EqualTo(At(site, late)[(int)WeatherAttr.Snow]).Within(5), "after release it relaxes back to nature");

            var exotic = new WeatherControl(1);
            exotic.TryApply(3, WeatherKind.Miasma, 0, true, true, false);
            WeatherSite grassland = Site("verdant_planet", "grassland");
            Assert.That(At(grassland, 1000, exotic.OverridesFor(3))[(int)WeatherKind.Miasma], Is.Zero,
                "a summon on the swamp does not spread to the region's grassland");
            Assert.That(At(Site("verdant_planet", "swamp"), 1000, exotic.OverridesFor(3))[(int)WeatherKind.Miasma], Is.EqualTo(1000));
        }

        [Test]
        public void Relax_curve_is_monotone_from_zero()
        {
            Assert.That(WeatherField.RelaxPermille(0), Is.Zero);
            Assert.That(WeatherField.RelaxPermille(240_000), Is.EqualTo(750));
            int last = 0;
            for (long t = 0; t < 10_000_000; t += 9_000)
            {
                int r = WeatherField.RelaxPermille(t);
                Assert.That(r, Is.GreaterThanOrEqualTo(last));
                Assert.That(r, Is.LessThanOrEqualTo(1000));
                last = r;
            }
        }

        [Test]
        public void Contracts_reject_invalid_arguments()
        {
            WeatherSite site = Site("verdant_planet", "forest");
            Assert.Throws<ContractException>(() => WeatherField.Evaluate(site, -1, null, new int[WeatherField.AttrCount]));
            Assert.Throws<ContractException>(() => WeatherField.Evaluate(site, 0, null, new int[3]));
            Assert.Throws<ContractException>(() => WeatherField.RelaxPermille(-1));
            Assert.Throws<ContractException>(() => new WeatherSite(1, "", null, 0, Equator, "forest"));
            Assert.Throws<ContractException>(() => new WeatherSite(1, "verdant_planet", null, -1, Equator, "forest"));
            Assert.Throws<ContractException>(() => new WeatherSite(1, "verdant_planet", null, 0, float3.zero, "forest"));
        }
    }

    public sealed class WeatherDynamicsTests
    {
        private static int[] Vector(params (int attr, int tenths)[] values)
        {
            var v = new int[WeatherField.AttrCount];
            foreach ((int a, int x) in values) v[a] = x;
            return v;
        }

        [Test]
        public void Threshold_and_dark_cloud_gate_effective_strength()
        {
            Assert.That(WeatherDynamics.Effective(Vector(((int)WeatherAttr.Fog, 749)), (int)WeatherAttr.Fog), Is.Zero);
            Assert.That(WeatherDynamics.Effective(Vector(((int)WeatherAttr.Fog, 1000)), (int)WeatherAttr.Fog), Is.EqualTo(1000));
            Assert.That(WeatherDynamics.Effective(Vector(((int)WeatherAttr.Rain, 1000), ((int)WeatherAttr.Clouds, 500)),
                (int)WeatherAttr.Rain), Is.Zero, "rain needs dark cloud");
            Assert.That(WeatherDynamics.Effective(Vector(((int)WeatherAttr.Rain, 1000), ((int)WeatherAttr.Clouds, 1000)),
                (int)WeatherAttr.Rain), Is.EqualTo(1000));
        }

        [Test]
        public void Resolve_picks_by_priority_and_the_strongest_exotic()
        {
            WeatherState s = WeatherDynamics.Resolve(Vector(((int)WeatherAttr.Fog, 1000), ((int)WeatherAttr.Sand, 900),
                ((int)WeatherKind.Miasma, 900), ((int)WeatherKind.SporeHaze, 950), ((int)WeatherAttr.Clouds, 600)));
            Assert.That(s.BaseKind, Is.EqualTo(WeatherKind.Sandstorm), "sandstorm outranks fog");
            Assert.That(s.BaseStrengthPermille, Is.EqualTo(600));
            Assert.That(s.Exotic, Is.EqualTo(WeatherKind.SporeHaze));
            Assert.That(s.Overcast, Is.True);
            WeatherState calm = WeatherDynamics.Resolve(Vector(((int)WeatherAttr.Wind, 840)));
            Assert.That(calm.BaseKind, Is.EqualTo(WeatherKind.Clear), "below 400 ‰ nothing dominates");
            Assert.That(calm.WindPermille, Is.EqualTo(360));
        }

        [Test]
        public void Compose_scales_with_strength_and_stacks_with_an_exotic()
        {
            WeatherState full = WeatherDynamics.Resolve(Vector(((int)WeatherAttr.Snow, 1000), ((int)WeatherAttr.Clouds, 1000)));
            WeatherProfile f = WeatherDynamics.Compose(full, DayPhase.Dawn, false, null);
            Assert.That(f.PlayerMoveSpeedMultiplier, Is.EqualTo(WeatherProfiles.Blizzard.PlayerMoveSpeedMultiplier).Within(1e-6f));
            Assert.That(f.PowerDeltaPercent(Element.Ice), Is.EqualTo(25));

            WeatherState half = WeatherDynamics.Resolve(Vector(((int)WeatherAttr.Snow, 875), ((int)WeatherAttr.Clouds, 1000)));
            WeatherProfile h = WeatherDynamics.Compose(half, DayPhase.Dawn, false, null);
            Assert.That(h.PlayerMoveSpeedMultiplier, Is.EqualTo(0.9f).Within(1e-6f));
            Assert.That(h.PowerDeltaPercent(Element.Ice), Is.EqualTo(13), "12.5 rounds away from zero");

            WeatherState both = WeatherDynamics.Resolve(Vector(((int)WeatherAttr.Snow, 1000), ((int)WeatherAttr.Clouds, 1000),
                ((int)WeatherKind.DiamondDust, 1000)));
            WeatherProfile b = WeatherDynamics.Compose(both, DayPhase.Dawn, false, null);
            Assert.That(b.Kind, Is.EqualTo(WeatherKind.DiamondDust));
            Assert.That(b.Exotic, Is.EqualTo(WeatherKind.DiamondDust));
            Assert.That(b.PowerDeltaPercent(Element.Ice), Is.EqualTo(45));
            Assert.That(b.PlayerSprintSpeedMultiplier,
                Is.EqualTo(WeatherProfiles.Blizzard.PlayerSprintSpeedMultiplier * 0.9f).Within(1e-6f));
        }

        [Test]
        public void Day_and_night_shift_light_and_shadow_except_indoors()
        {
            WeatherState clear = WeatherDynamics.Resolve(new int[WeatherField.AttrCount]);
            WeatherProfile day = WeatherDynamics.Compose(clear, DayPhase.Day, false, null);
            WeatherProfile night = WeatherDynamics.Compose(clear, DayPhase.Night, false, null);
            WeatherProfile dusk = WeatherDynamics.Compose(clear, DayPhase.Dusk, false, null);
            WeatherProfile indoorNight = WeatherDynamics.Compose(clear, DayPhase.Night, true, null);
            Assert.That(day.PowerDeltaPercent(Element.Light), Is.EqualTo(10));
            Assert.That(day.CdDeltaPercent(Element.Shadow), Is.EqualTo(5));
            Assert.That(night.PowerDeltaPercent(Element.Shadow), Is.EqualTo(10));
            Assert.That(night.CdDeltaPercent(Element.Light), Is.EqualTo(5));
            Assert.That(night.PlayerVisibilityRadiusM, Is.EqualTo(WeatherDynamics.NightVisibilityM));
            Assert.That(dusk.PowerDeltaPercent(Element.Light), Is.Zero);
            Assert.That(indoorNight.PowerDeltaPercent(Element.Shadow), Is.Zero);
            Assert.That(indoorNight.PlayerVisibilityRadiusM, Is.EqualTo(WeatherProfiles.ClearVisibilityM));
        }

        [Test]
        public void Ground_cover_slows_with_element_immunities()
        {
            WeatherState clear = WeatherDynamics.Resolve(new int[WeatherField.AttrCount]);
            WeatherProfile p = WeatherDynamics.Compose(clear, DayPhase.Dawn, false, new[] { 0, 1000, 0, 400 });
            Assert.That(p.GroundCoverPermille(GroundDeposit.Snow), Is.EqualTo(1000));
            Assert.That(p.PlayerGroundMultiplier, Is.EqualTo(0.875f).Within(1e-6f));
            Assert.That(p.CreatureGroundMultiplier(new[] { new ElementValue(Element.Ice, 50) }),
                Is.EqualTo(1f - 0.125f * 0.4f).Within(1e-6f), "ice ignores snow but not ash");
            Assert.That(p.CreatureGroundMultiplier(null), Is.EqualTo(0.875f).Within(1e-6f));
            Assert.Throws<ContractException>(() => p.GroundCoverPermille((GroundDeposit)4));
            Assert.Throws<ContractException>(() => WeatherDynamics.Compose(clear, DayPhase.Day, false, new int[3]));
            Assert.Throws<ContractException>(() => WeatherDynamics.Resolve(new int[3]));
            Assert.Throws<ContractException>(() => WeatherDynamics.Effective(new int[WeatherField.AttrCount], 18));
        }
    }

    public sealed class WeatherServiceTests
    {
        private static WeatherService Service(WeatherControl control, string terrain = "grassland") =>
            new WeatherService(99, "verdant_planet", Season.Winter, _ => 0, _ => new float3(1f, 0f, 0f), _ => terrain, control);

        [Test]
        public void Pinned_snow_builds_ground_cover_that_slows_and_then_clears()
        {
            var control = new WeatherControl(1);
            WeatherService service = Service(control);
            var here = new float3(0f, 0f, 1f);
            Assert.That(control.TryApply(0, WeatherKind.Blizzard, 0, true, false, false), Is.EqualTo(WeatherControlResult.Applied));
            WeatherProfile early = service.ProfileAt(here, 1_000);
            WeatherProfile later = service.ProfileAt(here, 170_000);
            Assert.That(later.GroundCoverPermille(GroundDeposit.Snow), Is.GreaterThan(early.GroundCoverPermille(GroundDeposit.Snow)));
            Assert.That(later.PlayerGroundMultiplier, Is.LessThan(1f));
            Assert.That(later.PlayerGroundMultiplier, Is.GreaterThanOrEqualTo(0.875f));
            WeatherProfile thawed = service.ProfileAt(here, 170_000 + 3_600_000);
            Assert.That(thawed.GroundCoverPermille(GroundDeposit.Snow), Is.LessThan(later.GroundCoverPermille(GroundDeposit.Snow)));
        }

        [Test]
        public void Control_in_the_same_tick_is_seen_immediately()
        {
            var control = new WeatherControl(1);
            WeatherService service = Service(control);
            var here = new float3(0f, 0f, 1f);
            service.ProfileAt(here, 5_000);
            control.TryApply(0, WeatherKind.Blizzard, 5_000, true, false, false);
            Assert.That(service.StateAt(here, 5_000).BaseKind, Is.EqualTo(WeatherKind.Blizzard));
            Assert.That(service.ProfileAt(here, 5_000).Kind, Is.EqualTo(WeatherKind.Blizzard).Or.Matches<WeatherKind>(WeatherProfiles.IsExotic));
        }

        [Test]
        public void A_late_joiner_reproduces_the_live_deposits()
        {
            var liveControl = new WeatherControl(1);
            var lateControl = new WeatherControl(1);
            liveControl.TryApply(0, WeatherKind.Rain, 30_000, true, false, false);
            lateControl.TryApply(0, WeatherKind.Rain, 30_000, true, false, false);
            WeatherService live = Service(liveControl, "swamp");
            WeatherService late = Service(lateControl, "swamp");
            var here = new float3(0f, 0f, 1f);
            for (long t = 0; t <= 200_000; t += 1_000) live.ProfileAt(here, t);
            WeatherProfile a = live.ProfileAt(here, 200_000);
            WeatherProfile b = late.ProfileAt(here, 200_000);
            for (int i = 0; i < 4; i++)
                Assert.That(b.GroundCoverPermille((GroundDeposit)i), Is.EqualTo(a.GroundCoverPermille((GroundDeposit)i)));
            Assert.That(a.GroundCoverPermille(GroundDeposit.Water), Is.GreaterThan(0));
            Assert.That(live.ProfileAt(here, 10_000).GroundCoverPermille(GroundDeposit.Water),
                Is.EqualTo(Service(new WeatherControl(1), "swamp").ProfileAt(here, 10_000).GroundCoverPermille(GroundDeposit.Water)),
                "asking about an earlier time recomputes from the start");
        }

        [Test]
        public void Indoor_and_native_queries_follow_the_terrain()
        {
            var control = new WeatherControl(1);
            Assert.That(Service(control, "laboratory").IsIndoor(new float3(1f, 0f, 0f)), Is.True);
            Assert.That(Service(control, "volcano").IsNativeExotic(new float3(1f, 0f, 0f), WeatherKind.VolcanicAsh), Is.True);
            Assert.That(Service(control, "grassland").IsNativeExotic(new float3(1f, 0f, 0f), WeatherKind.VolcanicAsh), Is.False);
            Assert.Throws<ContractException>(() => Service(control).IsNativeExotic(new float3(1f, 0f, 0f), WeatherKind.Rain));
            Assert.Throws<ContractException>(() => Service(control).ProfileAt(new float3(1f, 0f, 0f), -1));
            Assert.Throws<ContractException>(() => new WeatherService(1, "", null, _ => 0, _ => float3.zero, _ => "x", control));
            Assert.Throws<ContractException>(() => new WeatherService(1, "verdant_planet", null, null, _ => float3.zero, _ => "x", control));
        }
    }

    internal static class ArrayCompare
    {
        internal static bool AsSpanEquals(this int[] a, int[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i]) return false;
            return true;
        }
    }
}
