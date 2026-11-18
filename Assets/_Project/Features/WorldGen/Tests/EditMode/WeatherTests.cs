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
    }
}
