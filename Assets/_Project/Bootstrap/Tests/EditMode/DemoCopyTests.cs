using Cosmimera.Bootstrap.Demo;
using NUnit.Framework;
using Cosmimera.WorldGen;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Cosmimera.Bootstrap.Tests
{
    public sealed class DemoCopyTests
    {
        [TestCase("zh-TW")]
        [TestCase("en")]
        [TestCase("ja")]
        public void Shipped_weather_copy_resolves_every_profile_and_control_message(string language)
        {
            string strings = File.ReadAllText(Path.Combine(Application.dataPath, "_Project", "Data", "Copy", "strings.json"));
            var copy = new DemoCopy(language, strings, "{\"entries\":[]}", "[]", "[]", "{\"steps\":[],\"chains\":[]}", "[]");
            foreach (WeatherProfile weather in WeatherProfiles.All)
            {
                string key = "ui.weather." + weather.Kind.ToString().ToLowerInvariant();
                Assert.That(copy.Ui(key), Is.Not.EqualTo(key));
                Assert.That(copy.Ui(key), Is.Not.Empty);
            }
            foreach (string name in new[] { "status", "natural", "controlled", "movement", "title", "visibility", "creatures",
                         "territory", "stealth", "element", "no_element_effects", "control", "cooldown", "applied", "missing_skill", "on_cooldown" })
            {
                string key = "ui.weather." + name;
                Assert.That(copy.Ui(key), Is.Not.EqualTo(key));
                Assert.That(copy.Ui(key), Is.Not.Empty);
            }
            foreach (string name in new[] { "banner", "planet_tag", "hint_lobby" })
            {
                string key = "ui.beginner_quest." + name;
                Assert.That(copy.Ui(key), Is.Not.EqualTo(key));
                Assert.That(copy.Ui(key), Is.Not.Empty);
            }
        }

        [Test]
        public void Japanese_names_use_translation_or_english_fallback()
        {
            var copy = new DemoCopy("ja", "{}",
                "{\"entries\":[{\"id\":\"creature\",\"zh\":\"\u7378\",\"en\":\"Beast\",\"ja\":\"\u7363\"},{\"id\":\"move\",\"en\":\"Move\"},{\"id\":\"trait\",\"en\":\"Trait\",\"ja\":\"\"}]}",
                "[]", "[]", "{\"steps\":[],\"chains\":[]}", "[]");
            Assert.That(copy.Name("creature"), Is.EqualTo("\u7363"));
            Assert.That(copy.Name("move"), Is.EqualTo("Move"));
            Assert.That(copy.Name("trait"), Is.EqualTo("Trait"));
            Assert.That(copy.Name("unknown"), Is.EqualTo("unknown"));
        }

        [Test]
        public void Japanese_falls_back_per_entry_to_English()
        {
            var copy = new DemoCopy("ja", "{\"translated\":{\"en\":\"English\",\"ja\":\"日本語\"},\"missing\":{\"en\":\"Fallback\"}}",
                "{\"entries\":[{\"id\":\"creature\",\"en\":\"Companion\"},{\"id\":\"move\",\"en\":\"Strike\",\"ja\":\"技\"}]}",
                "[{\"id\":\"affix\",\"name\":{\"en\":\"Name\"},\"desc\":{\"en\":\"Description\"}}]",
                "[{\"id\":\"theme\",\"name\":{\"en\":\"Theme\"}}]",
                "{\"steps\":[{\"type\":\"step\",\"variants\":[{\"en\":\"Step\"}]}],\"chains\":[{\"id\":\"chain\",\"title\":{\"en\":\"Chain\"}}]}",
                "[{\"planet_type\":\"planet\",\"epithets\":[{\"en\":\"Epithet\"}]}]");
            Assert.That(copy.Ui("translated"), Is.EqualTo("日本語"));
            Assert.That(copy.Ui("missing"), Is.EqualTo("Fallback"));
            Assert.That(copy.Name("creature"), Is.EqualTo("Companion"));
            Assert.That(copy.Name("move"), Is.EqualTo("技"));
            Assert.That(copy.AffixName("affix"), Is.EqualTo("Name"));
            Assert.That(copy.AffixDesc("affix"), Is.EqualTo("Description"));
            Assert.That(copy.Theme("theme"), Is.EqualTo("Theme"));
            Assert.That(copy.Step("step", 0), Is.EqualTo("Step"));
            Assert.That(copy.ChainTitle("chain"), Is.EqualTo("Chain"));
            Assert.That(copy.Epithet("planet", 0), Is.EqualTo("Epithet"));
        }

        [TestCase("zh-TW", "的", "星")]
        [TestCase("en", " ", "Planet")]
        [TestCase("ja", "の", "星")]
        public void Shipped_planet_affixes_generate_diverse_names_for_all_planet_types(string language, string mid, string suffix)
        {
            string strings = File.ReadAllText(Path.Combine(Application.dataPath, "_Project", "Data", "Copy", "strings.json"));
            var copy = new DemoCopy(language, strings, "{\"entries\":[]}", "[]", "[]", "{\"steps\":[],\"chains\":[]}", "[]");
            string[] types =
            {
                "verdant_planet", "lava_planet", "ice_crystal_planet",
                "storm_planet", "mineral_planet", "twilight_planet",
            };
            foreach (string planetType in types)
            {
                var seen = new HashSet<string>();
                for (ulong seed = 0; seed < 40; seed++)
                {
                    string a = copy.PlanetName(planetType, seed);
                    string b = copy.PlanetName(planetType, seed);
                    Assert.That(b, Is.EqualTo(a), "deterministic");
                    Assert.That(a, Does.Contain(mid));
                    Assert.That(a, Does.EndWith(suffix));
                    seen.Add(a);
                }

                Assert.That(seen.Count, Is.GreaterThanOrEqualTo(30), planetType);
            }

            Assert.That(copy.PlanetName("unknown_planet", 1), Is.EqualTo(copy.Planet("unknown_planet")));
        }
    }
}
