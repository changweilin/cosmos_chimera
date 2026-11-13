using System;
using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.GameData;
using NUnit.Framework;

namespace Cosmimera.WorldGen.Tests
{
    public sealed class WeatherTests
    {
        [Test]
        public void All_profiles_have_valid_positive_multipliers_and_cover_every_kind()
        {
            var seen = new HashSet<WeatherKind>();
            foreach (WeatherProfile p in WeatherProfiles.All)
            {
                seen.Add(p.Kind);
                Assert.That(p.PlayerMoveSpeedMultiplier, Is.GreaterThan(0f), p.Kind.ToString());
                Assert.That(p.PlayerSprintSpeedMultiplier, Is.GreaterThan(0f), p.Kind.ToString());
                Assert.That(p.PlayerRollCooldownMultiplier, Is.GreaterThan(0f), p.Kind.ToString());
                Assert.That(p.PlayerVisibilityRadiusM, Is.GreaterThan(0f), p.Kind.ToString());
                Assert.That(p.CreatureMoveSpeedMultiplier, Is.GreaterThan(0f), p.Kind.ToString());
                Assert.That(p.AdaptedCreatureMoveSpeedMultiplier, Is.GreaterThan(0f), p.Kind.ToString());
                Assert.That(p.TerritoryRadiusPermille, Is.GreaterThan(0), p.Kind.ToString());
                Assert.That(p.ProjectileDriftDegrees, Is.GreaterThanOrEqualTo(0f), p.Kind.ToString());
                Assert.That(WeatherProfiles.For(p.Kind).Kind, Is.EqualTo(p.Kind));
            }

            Assert.That(seen.Count, Is.EqualTo(Enum.GetValues(typeof(WeatherKind)).Length));
        }

        [Test]
        public void For_rejects_undefined_weather_kind()
        {
            Assert.Throws<ContractException>(() => WeatherProfiles.For((WeatherKind)99));
            Assert.Throws<ContractException>(() => WeatherProfiles.For((WeatherKind)(-1)));
        }

        [Test]
        public void Weather_differentiates_player_and_creature_effects()
        {
            WeatherProfile blizzard = WeatherProfiles.Blizzard;
            Assert.That(blizzard.PlayerMoveSpeedMultiplier, Is.LessThan(1f));
            Assert.That(blizzard.PlayerRollCooldownMultiplier, Is.GreaterThan(1f));
            Assert.That(blizzard.CreatureMoveSpeedMultiplier, Is.LessThan(1f));
            Assert.That(blizzard.AdaptedCreatureMoveSpeedMultiplier, Is.GreaterThan(1f));
            Assert.That(blizzard.IsFavouredElement(Element.Ice), Is.True);
            Assert.That(blizzard.IsFavouredElement(Element.Fire), Is.False);
            Assert.That(blizzard.PowerDeltaPercent(Element.Ice), Is.GreaterThan(0));
            Assert.That(blizzard.PowerDeltaPercent(Element.Fire), Is.LessThan(0));
            Assert.That(blizzard.CdDeltaPercent(Element.Ice), Is.LessThan(0));
            Assert.That(blizzard.CdDeltaPercent(Element.Fire), Is.GreaterThan(0));

            WeatherProfile fog = WeatherProfiles.Fog;
            Assert.That(fog.StealthEnabled, Is.True);
            Assert.That(fog.TerritoryRadiusPermille, Is.LessThan(1000));
            Assert.That(fog.PlayerVisibilityRadiusM, Is.LessThan(WeatherProfiles.Clear.PlayerVisibilityRadiusM));
            Assert.That(WeatherProfiles.Clear.StealthEnabled, Is.False);

            WeatherProfile gale = WeatherProfiles.Gale;
            Assert.That(gale.ProjectileDriftDegrees, Is.GreaterThan(0f));
            Assert.That(gale.IsFavouredElement(Element.Wind), Is.True);
        }

        [Test]
        public void Element_queries_reject_invalid_element()
        {
            Assert.Throws<ContractException>(() => WeatherProfiles.Rain.PowerDeltaPercent((Element)99));
            Assert.Throws<ContractException>(() => WeatherProfiles.Rain.CdDeltaPercent((Element)(-1)));
            Assert.Throws<ContractException>(() => WeatherProfiles.Rain.IsFavouredElement((Element)10));
        }

        [Test]
        public void WeatherPlanner_is_deterministic_and_varies_by_time_planet_and_terrain()
        {
            WeatherProfile a = WeatherPlanner.WeatherAt(12345UL, "ice_crystal_planet", "ice_field", 0);
            WeatherProfile b = WeatherPlanner.WeatherAt(12345UL, "ice_crystal_planet", "ice_field", 0);
            WeatherProfile c = WeatherPlanner.WeatherAt(12345UL, "ice_crystal_planet", "ice_field",
                WeatherPlanner.DefaultSlotDurationMs - 1);
            Assert.That(b.Kind, Is.EqualTo(a.Kind));
            Assert.That(c.Kind, Is.EqualTo(a.Kind));

            // Across many time slots, dynamic weather changes rather than staying locked forever.
            var iceKinds = new HashSet<WeatherKind>();
            var lavaKinds = new HashSet<WeatherKind>();
            for (int slot = 0; slot < 24; slot++)
            {
                long t = (long)slot * WeatherPlanner.DefaultSlotDurationMs;
                iceKinds.Add(WeatherPlanner.WeatherAt(12345UL, "ice_crystal_planet", "ice_field", t).Kind);
                lavaKinds.Add(WeatherPlanner.WeatherAt(12345UL, "lava_planet", "volcano", t).Kind);
            }

            Assert.That(iceKinds.Count, Is.GreaterThan(1));
            Assert.That(lavaKinds.Count, Is.GreaterThan(1));
            Assert.That(iceKinds.Contains(WeatherKind.Blizzard), Is.True);
            Assert.That(lavaKinds.Contains(WeatherKind.Heatwave), Is.True);
        }

        [Test]
        public void WeatherPlanner_rejects_invalid_time_or_slot_duration()
        {
            Assert.Throws<ContractException>(() => WeatherPlanner.WeatherAt(1UL, "verdant_planet", "forest", -1));
            Assert.Throws<ContractException>(() => WeatherPlanner.WeatherAt(1UL, "verdant_planet", "forest", 0, 0));
            Assert.Throws<ContractException>(() => WeatherPlanner.WeatherAt(1UL, "verdant_planet", "forest", 0, -10));
        }
    }
}
