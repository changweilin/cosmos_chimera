using Cosmimera.Bootstrap.Demo;
using NUnit.Framework;

namespace Cosmimera.Bootstrap.Tests
{
    public sealed class DemoCopyTests
    {
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
    }
}
