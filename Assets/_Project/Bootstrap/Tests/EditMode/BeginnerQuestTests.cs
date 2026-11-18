using Cosmimera.Bootstrap.Demo;
using Cosmimera.Capture;
using Cosmimera.Core.Contracts;
using Cosmimera.GameData;
using Cosmimera.Save;
using NUnit.Framework;
using UnityEngine;

namespace Cosmimera.Bootstrap.Tests
{
    public sealed class BeginnerQuestTests
    {
        [Test]
        public void Companion_ring_and_starter_counter_picks_assign_all_three_distinctly()
        {
            (string f1, string r1) = BeginnerQuestRules.AssignCompanions("ye_chengxi");
            Assert.That(f1, Is.EqualTo("luo_en"));
            Assert.That(r1, Is.EqualTo("yumori_sumiha"));
            Assert.That(BeginnerQuestRules.SignatureVessel("ye_chengxi"), Is.EqualTo(VesselKind.GlassOrb));
            Assert.That(BeginnerQuestRules.SignatureVessel("luo_en"), Is.EqualTo(VesselKind.MagicCube));
            Assert.That(BeginnerQuestRules.SignatureVessel("yumori_sumiha"), Is.EqualTo(VesselKind.TalismanCard));

            (string rivalSun, string friendSun) = BeginnerQuestRules.AssignStarterLines("starter_sun");
            Assert.That(rivalSun, Is.EqualTo("starter_moon"));
            Assert.That(friendSun, Is.EqualTo("starter_star"));

            Assert.Throws<ContractException>(() => BeginnerQuestRules.AssignCompanions("unknown"));
        }

        [Test]
        public void Lobby_save_v13_persists_tutorial_fields_and_migrates_existing_saves()
        {
            var prog = Progression.FromJson(Resources.Load<TextAsset>("balance") != null
                ? Resources.Load<TextAsset>("balance").text
                : System.IO.File.ReadAllText("Data/balance.json"));
            var fresh = new LobbySave();
            Assert.That(fresh.TutorialDone, Is.False);
            fresh.CompleteTutorial("ye_chengxi", "luo_en", "yumori_sumiha", "starter_moon", "starter_star");
            Assert.That(fresh.TutorialDone, Is.True);

            LobbySave roundtrip = LobbySave.FromJson(fresh.ToJson(), prog, _ => CreatureStage.Juvenile);
            Assert.That(roundtrip.TutorialDone, Is.True);
            Assert.That(roundtrip.CharacterId, Is.EqualTo("ye_chengxi"));
            Assert.That(roundtrip.FriendId, Is.EqualTo("luo_en"));
            Assert.That(roundtrip.RivalId, Is.EqualTo("yumori_sumiha"));
            Assert.That(roundtrip.RivalStarterId, Is.EqualTo("starter_moon"));
            Assert.That(roundtrip.FriendStarterId, Is.EqualTo("starter_star"));

            Assert.Throws<ContractException>(() => fresh.CompleteTutorial("", "a", "b", "c", "d"));
        }
    }
}
