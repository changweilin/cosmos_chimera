using System.Collections.Generic;
using Cosmimera.Bootstrap.Demo;
using Cosmimera.GameData;
using Cosmimera.Planet;
using NUnit.Framework;
using UnityEngine;

namespace Cosmimera.Bootstrap.Tests
{
    public sealed class BgmResolverTests
    {
        [Test]
        public void Boss_bgm_scales_with_planet_size()
        {
            Assert.That(BgmResolver.ResolveBossBgm(PlanetSize.Asteroid), Is.EqualTo(BgmResolver.BossAsteroid));
            Assert.That(BgmResolver.ResolveBossBgm(PlanetSize.Moon), Is.EqualTo(BgmResolver.BossDwarf));
            Assert.That(BgmResolver.ResolveBossBgm(PlanetSize.Planet), Is.EqualTo(BgmResolver.BossPlanet));
            Assert.That(BgmResolver.ResolveBossBgm(PlanetSize.Supergiant), Is.EqualTo(BgmResolver.BossSupergiant));
        }

        [Test]
        public void Region_theme_resolves_to_expected_track()
        {
            Assert.That(BgmResolver.ResolveRegionBgm("forest"), Is.EqualTo(BgmResolver.Forest));
            Assert.That(BgmResolver.ResolveRegionBgm("canyon"), Is.EqualTo(BgmResolver.Canyon));
            Assert.That(BgmResolver.ResolveRegionBgm("ruins"), Is.EqualTo(BgmResolver.Ruins));
            Assert.That(BgmResolver.ResolveRegionBgm("coast"), Is.EqualTo(BgmResolver.Beach));
            Assert.That(BgmResolver.ResolveRegionBgm("riverbank"), Is.EqualTo(BgmResolver.Village));
            Assert.That(BgmResolver.ResolveRegionBgm("highland"), Is.EqualTo(BgmResolver.Grassland));
            Assert.That(BgmResolver.ResolveRegionBgm("crater"), Is.EqualTo(BgmResolver.Desert));
            Assert.That(BgmResolver.ResolveRegionBgm("ice_shelf"), Is.EqualTo(BgmResolver.IceCap));
            Assert.That(BgmResolver.ResolveRegionBgm("temple"), Is.EqualTo(BgmResolver.Temple));
            Assert.That(BgmResolver.ResolveRegionBgm("shrine"), Is.EqualTo(BgmResolver.Temple));
        }

        [Test]
        public void Extended_synonyms_resolve_correctly()
        {
            Assert.That(BgmResolver.ResolveRegionBgm("deep_mountain"), Is.EqualTo(BgmResolver.Mountain));
            Assert.That(BgmResolver.ResolveRegionBgm("wetland"), Is.EqualTo(BgmResolver.Swamp));
            Assert.That(BgmResolver.ResolveRegionBgm("lake"), Is.EqualTo(BgmResolver.Swamp));
            Assert.That(BgmResolver.ResolveRegionBgm("mine"), Is.EqualTo(BgmResolver.Cave));
            Assert.That(BgmResolver.ResolveRegionBgm("underground"), Is.EqualTo(BgmResolver.Cave));
            Assert.That(BgmResolver.ResolveRegionBgm("skyscraper"), Is.EqualTo(BgmResolver.Tower));
            Assert.That(BgmResolver.ResolveRegionBgm("factory"), Is.EqualTo(BgmResolver.Laboratory));
            Assert.That(BgmResolver.ResolveRegionBgm("floating_island"), Is.EqualTo(BgmResolver.Space));
        }

        [Test]
        public void Lobby_bgm_is_town_not_space()
        {
            Assert.That(BgmResolver.Lobby, Is.EqualTo(BgmResolver.Town));
            Assert.That(BgmResolver.Lobby, Is.Not.EqualTo(BgmResolver.Space));
        }

        [Test]
        public void Advanced_terrain_rules_match_10_elements_and_planet_tiers()
        {
            // Dwarf Planet (Moon): Metal, Wood, Water, Fire, Earth
            Assert.That(BgmResolver.TryGetAdvancedTerrainRule("laboratory", out var lab), Is.True);
            Assert.That(lab.RequiredElement, Is.EqualTo(Element.Metal));
            Assert.That(lab.MinPlanetTier, Is.EqualTo(PlanetSize.Moon));

            Assert.That(BgmResolver.TryGetAdvancedTerrainRule("rainforest", out var rain), Is.True);
            Assert.That(rain.RequiredElement, Is.EqualTo(Element.Wood));
            Assert.That(rain.MinPlanetTier, Is.EqualTo(PlanetSize.Moon));

            Assert.That(BgmResolver.TryGetAdvancedTerrainRule("deep_sea", out var sea), Is.True);
            Assert.That(sea.RequiredElement, Is.EqualTo(Element.Water));
            Assert.That(sea.MinPlanetTier, Is.EqualTo(PlanetSize.Moon));

            Assert.That(BgmResolver.TryGetAdvancedTerrainRule("volcano", out var vol), Is.True);
            Assert.That(vol.RequiredElement, Is.EqualTo(Element.Fire));
            Assert.That(vol.MinPlanetTier, Is.EqualTo(PlanetSize.Moon));

            Assert.That(BgmResolver.TryGetAdvancedTerrainRule("cave", out var cave), Is.True);
            Assert.That(cave.RequiredElement, Is.EqualTo(Element.Earth));
            Assert.That(cave.MinPlanetTier, Is.EqualTo(PlanetSize.Moon));

            // Planet: Ice, Thunder, Wind
            Assert.That(BgmResolver.TryGetAdvancedTerrainRule("ice_cap", out var ice), Is.True);
            Assert.That(ice.RequiredElement, Is.EqualTo(Element.Ice));
            Assert.That(ice.MinPlanetTier, Is.EqualTo(PlanetSize.Planet));

            Assert.That(BgmResolver.TryGetAdvancedTerrainRule("tower", out var tower), Is.True);
            Assert.That(tower.RequiredElement, Is.EqualTo(Element.Thunder));
            Assert.That(tower.MinPlanetTier, Is.EqualTo(PlanetSize.Planet));

            Assert.That(BgmResolver.TryGetAdvancedTerrainRule("space", out var space), Is.True);
            Assert.That(space.RequiredElement, Is.EqualTo(Element.Wind));
            Assert.That(space.MinPlanetTier, Is.EqualTo(PlanetSize.Planet));

            // Supergiant: Shadow, Light
            Assert.That(BgmResolver.TryGetAdvancedTerrainRule("labyrinth", out var laby), Is.True);
            Assert.That(laby.RequiredElement, Is.EqualTo(Element.Shadow));
            Assert.That(laby.MinPlanetTier, Is.EqualTo(PlanetSize.Supergiant));

            Assert.That(BgmResolver.TryGetAdvancedTerrainRule("temple", out var temple), Is.True);
            Assert.That(temple.RequiredElement, Is.EqualTo(Element.Light));
            Assert.That(temple.MinPlanetTier, Is.EqualTo(PlanetSize.Supergiant));
        }

        [Test]
        public void Unknown_theme_falls_back_to_terrain_tag()
        {
            var tags = new[] { "volcano", "rift_valley" };
            Assert.That(BgmResolver.ResolveRegionBgm("unknown_theme", tags), Is.EqualTo(BgmResolver.Volcano));
        }

        [Test]
        public void Unknown_theme_without_tags_falls_back_to_grassland()
        {
            Assert.That(BgmResolver.ResolveRegionBgm("unknown_theme"), Is.EqualTo(BgmResolver.Grassland));
            Assert.That(BgmResolver.ResolveRegionBgm(null), Is.EqualTo(BgmResolver.Grassland));
        }

        [Test]
        public void BgmTable_finds_registered_clip_or_returns_null()
        {
            var table = ScriptableObject.CreateInstance<BgmTable>();
            table.SetEntries(new List<BgmEntry>
            {
                new BgmEntry("bgm_town", null),
                new BgmEntry("bgm_temple", null),
            });

            Assert.That(table.Entries.Count, Is.EqualTo(2));
            Assert.That(table.Find("bgm_town"), Is.Null);
            Assert.That(table.Find("bgm_temple"), Is.Null);
            Assert.That(table.Find("non_existent"), Is.Null);
            Assert.That(table.Find(null), Is.Null);
        }
    }
}
