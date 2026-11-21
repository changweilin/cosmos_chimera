using Cosmimera.Core.Contracts;
using Cosmimera.Creatures;
using Cosmimera.GameData;
using Cosmimera.Lobby;
using NUnit.Framework;

namespace Cosmimera.Bootstrap.Tests
{
    public sealed class StarSelectRulesTests
    {
        [Test]
        public void MaxUnlockedSize_starts_at_asteroid_and_scales_with_main_story_trials()
        {
            Assert.That(StarSelectRules.MaxUnlockedSize(false, 0), Is.EqualTo(PlanetSize.Asteroid));
            Assert.That(StarSelectRules.MaxUnlockedSize(false, 10), Is.EqualTo(PlanetSize.Asteroid));
            Assert.That(StarSelectRules.MaxUnlockedSize(true, 0), Is.EqualTo(PlanetSize.Asteroid));
            Assert.That(StarSelectRules.MaxUnlockedSize(true, StarSelectRules.MoonUnlockTrials), Is.EqualTo(PlanetSize.Moon));
            Assert.That(StarSelectRules.MaxUnlockedSize(true, StarSelectRules.PlanetUnlockTrials), Is.EqualTo(PlanetSize.Planet));
            Assert.That(StarSelectRules.MaxUnlockedSize(true, StarSelectRules.SupergiantUnlockTrials), Is.EqualTo(PlanetSize.Supergiant));

            // Full main-story milestone overload: food quest (#2) -> Moon, 5 trials + migratory return (#4) -> Planet,
            // 10 trials + rival finale (#8) -> Supergiant.
            Assert.That(StarSelectRules.MaxUnlockedSize(false, true, 10, true, true), Is.EqualTo(PlanetSize.Asteroid));
            Assert.That(StarSelectRules.MaxUnlockedSize(true, false, 0, false, false), Is.EqualTo(PlanetSize.Asteroid));
            Assert.That(StarSelectRules.MaxUnlockedSize(true, true, 0, false, false), Is.EqualTo(PlanetSize.Moon));
            Assert.That(StarSelectRules.MaxUnlockedSize(true, true, 5, false, false), Is.EqualTo(PlanetSize.Moon));
            Assert.That(StarSelectRules.MaxUnlockedSize(true, true, 5, true, false), Is.EqualTo(PlanetSize.Planet));
            Assert.That(StarSelectRules.MaxUnlockedSize(true, true, 10, true, false), Is.EqualTo(PlanetSize.Planet));
            Assert.That(StarSelectRules.MaxUnlockedSize(true, true, 10, true, true), Is.EqualTo(PlanetSize.Supergiant));
        }

        [Test]
        public void LaunchCost_scales_with_planet_size_and_matches_main_element()
        {
            MaterialStack ast = StarSelectRules.LaunchCost(PlanetSize.Asteroid, Element.Fire);
            MaterialStack moon = StarSelectRules.LaunchCost(PlanetSize.Moon, Element.Water);
            MaterialStack planet = StarSelectRules.LaunchCost(PlanetSize.Planet, Element.Wood);
            MaterialStack super = StarSelectRules.LaunchCost(PlanetSize.Supergiant, Element.Light);

            Assert.That(ast.Key, Is.EqualTo(new MaterialKey(Element.Fire, 1)));
            Assert.That(ast.Qty, Is.EqualTo(1));
            Assert.That(moon.Key, Is.EqualTo(new MaterialKey(Element.Water, 1)));
            Assert.That(moon.Qty, Is.EqualTo(2));
            Assert.That(planet.Key, Is.EqualTo(new MaterialKey(Element.Wood, 1)));
            Assert.That(planet.Qty, Is.EqualTo(4));
            Assert.That(super.Key, Is.EqualTo(new MaterialKey(Element.Light, 1)));
            Assert.That(super.Qty, Is.EqualTo(6));
        }

        [Test]
        public void TryPayLaunchCost_refuses_when_short_and_consumes_when_sufficient()
        {
            var stock = new MaterialInventory();
            var key = new MaterialKey(Element.Wood, 1);

            Assert.That(StarSelectRules.CanLaunch(stock, PlanetSize.Moon, Element.Wood), Is.False);
            Assert.That(StarSelectRules.TryPayLaunchCost(stock, PlanetSize.Moon, Element.Wood), Is.False);
            Assert.That(stock.Count(key), Is.EqualTo(0));

            stock.Add(key, 3);
            Assert.That(StarSelectRules.CanLaunch(stock, PlanetSize.Moon, Element.Wood), Is.True);
            Assert.That(StarSelectRules.TryPayLaunchCost(stock, PlanetSize.Moon, Element.Wood), Is.True);
            Assert.That(stock.Count(key), Is.EqualTo(1));
        }

        [Test]
        public void Invalid_preconditions_throw_contract_exception()
        {
            Assert.Throws<ContractException>(() => StarSelectRules.MaxUnlockedSize(true, -1));
            Assert.Throws<ContractException>(() => StarSelectRules.MaxUnlockedSize(true, true, -1, true, true));
            Assert.Throws<ContractException>(() => StarSelectRules.CanLaunch(null, PlanetSize.Asteroid, Element.Fire));
            Assert.Throws<ContractException>(() => StarSelectRules.TryPayLaunchCost(null, PlanetSize.Asteroid, Element.Fire));
        }
    }
}
