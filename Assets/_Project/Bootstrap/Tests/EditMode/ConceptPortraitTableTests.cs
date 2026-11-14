using System.Collections.Generic;
using Cosmimera.Bootstrap.Demo;
using Cosmimera.GameData;
using NUnit.Framework;
using UnityEngine;

namespace Cosmimera.Bootstrap.Tests.EditMode
{
    public sealed class ConceptPortraitTableTests
    {
        [Test]
        public void FindCharacter_returns_registered_texture_and_null_for_missing()
        {
            var table = ScriptableObject.CreateInstance<ConceptPortraitTable>();
            var tex = new Texture2D(4, 4);
            try
            {
                table.SetEntries(
                    new List<CharacterPortraitEntry>
                    {
                        new CharacterPortraitEntry("chr_amemori_sumiha", tex),
                    },
                    null);

                Assert.That(table.FindCharacter("chr_amemori_sumiha"), Is.SameAs(tex));
                Assert.That(table.FindCharacter("chr_missing"), Is.Null);
                Assert.That(table.FindCharacter(string.Empty), Is.Null);
                Assert.That(table.FindCharacter(null), Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(tex);
                Object.DestroyImmediate(table);
            }
        }

        [Test]
        public void FindCreature_matches_line_and_stage_only()
        {
            var table = ScriptableObject.CreateInstance<ConceptPortraitTable>();
            var juv = new Texture2D(4, 4);
            var adult = new Texture2D(4, 4);
            try
            {
                table.SetEntries(
                    null,
                    new List<CreaturePortraitEntry>
                    {
                        new CreaturePortraitEntry("starter_sun_line", CreatureStage.Juvenile, juv),
                        new CreaturePortraitEntry("starter_sun_line", CreatureStage.Adult, adult),
                    });

                Assert.That(table.FindCreature("starter_sun_line", CreatureStage.Juvenile), Is.SameAs(juv));
                Assert.That(table.FindCreature("starter_sun_line", CreatureStage.Adult), Is.SameAs(adult));
                Assert.That(table.FindCreature("starter_sun_line", CreatureStage.Adolescent), Is.Null);
                Assert.That(table.FindCreature("starter_moon_line", CreatureStage.Juvenile), Is.Null);
                Assert.That(table.FindCreature(string.Empty, CreatureStage.Juvenile), Is.Null);
                Assert.That(table.FindCreature(null, CreatureStage.Juvenile), Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(juv);
                Object.DestroyImmediate(adult);
                Object.DestroyImmediate(table);
            }
        }
    }
}
