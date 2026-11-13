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
                new BgmEntry("bgm_space", null),
            });

            Assert.That(table.Entries.Count, Is.EqualTo(2));
            Assert.That(table.Find("bgm_town"), Is.Null);
            Assert.That(table.Find("non_existent"), Is.Null);
            Assert.That(table.Find(null), Is.Null);
        }
    }
}
