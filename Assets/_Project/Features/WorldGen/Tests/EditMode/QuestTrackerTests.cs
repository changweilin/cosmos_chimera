using Cosmimera.GameData;
using NUnit.Framework;
using Unity.Mathematics;

namespace Cosmimera.WorldGen.Tests
{
    public sealed class QuestTrackerTests
    {
        private static RunPlan Plan()
        {
            var regions = new[]
            {
                new RunRegion(0, new float3(0, 1, 0), new[] { 1 }, "forest", null, 0, 0),
                new RunRegion(1, new float3(1, 0, 0), new[] { 0, 2 }, "forest", null, 1, 1),
                new RunRegion(2, new float3(0, -1, 0), new[] { 1 }, "crater", "affix_fog", 2, 2),
            };
            var spawns = new[]
            {
                new RunSpawn(1, 0, new float3(1, 0, 0), "w", SpawnRole.Normal),
                new RunSpawn(1, 1, new float3(1, 0, 0), "e", SpawnRole.Elite),
                new RunSpawn(2, 2, new float3(0, -1, 0), "b", SpawnRole.Boss),
            };
            var quest = new[]
            {
                new RunQuestStep(QuestStepKind.ReachPoi, 1, 1, Element.Wood, -1),
                new RunQuestStep(QuestStepKind.DefeatWild, 1, 2, Element.Wood, -1),
                new RunQuestStep(QuestStepKind.DefeatElite, 1, 1, Element.Wood, 1),
                new RunQuestStep(QuestStepKind.CaptureOne, 1, 1, Element.Wood, -1),
                new RunQuestStep(QuestStepKind.DefeatBoss, 2, 1, Element.Wood, 2),
            };
            return new RunPlan(1, "t", "c", regions, 0, 2, new[] { 0, 1, 2 }, spawns, quest, new float3(0, 1, 0));
        }

        [Test]
        public void Steps_complete_in_order()
        {
            var q = new QuestTracker(Plan());
            q.OnCaptured();
            Assert.That(q.StepIndex, Is.EqualTo(0), "out-of-order events give no credit");
            q.OnRegionEntered(1);
            q.OnKnockedOut(0, Element.Fire);
            Assert.That(q.Progress, Is.EqualTo(0), "wrong element does not count");
            q.OnKnockedOut(0, Element.Wood);
            q.OnKnockedOut(0, Element.Wood);
            Assert.That(q.Current.Kind, Is.EqualTo(QuestStepKind.DefeatElite));
            q.OnKnockedOut(0, Element.Wood);
            Assert.That(q.Current.Kind, Is.EqualTo(QuestStepKind.DefeatElite), "only the target elite counts");
            q.OnKnockedOut(1, Element.Wood);
            q.OnCaptured();
            Assert.That(q.Current.Kind, Is.EqualTo(QuestStepKind.DefeatBoss));
            q.OnKnockedOut(2, Element.Fire);
            Assert.That(q.IsComplete, Is.True);
        }

        [Test]
        public void Boss_knockout_ends_the_quest_at_any_step()
        {
            var q = new QuestTracker(Plan());
            q.OnKnockedOut(2, Element.Fire);
            Assert.That(q.IsComplete, Is.True);
            Assert.That(!q.IsEarned(0) && !q.IsEarned(1) && !q.IsEarned(3), Is.True, "skipped steps are not earned");
            Assert.That(q.IsEarned(4), Is.True, "the boss step itself was genuinely done");
        }

        // REQ: Q-E1 (2026-11-09) — an elite knocked out before its step still counts.
        [Test]
        public void Elite_knocked_out_early_completes_its_step_when_it_becomes_current()
        {
            var q = new QuestTracker(Plan());
            q.OnKnockedOut(1, Element.Fire);
            Assert.That(q.StepIndex, Is.EqualTo(0));
            q.OnRegionEntered(1);
            q.OnKnockedOut(0, Element.Wood);
            q.OnKnockedOut(0, Element.Wood);
            Assert.That(q.Current.Kind, Is.EqualTo(QuestStepKind.CaptureOne));
            Assert.That(q.IsEarned(2), Is.True);
        }

        // SOL P3-10 S1 witness shape: the elite is one of the DefeatWild kills.
        [Test]
        public void Elite_counted_toward_defeat_wild_still_completes_the_elite_step()
        {
            var q = new QuestTracker(Plan());
            q.OnRegionEntered(1);
            q.OnKnockedOut(0, Element.Wood);
            q.OnKnockedOut(1, Element.Wood);
            Assert.That(q.Current.Kind, Is.EqualTo(QuestStepKind.CaptureOne));
            q.OnCaptured();
            q.OnKnockedOut(2, Element.Wood);
            for (int i = 0; i < 5; i++)
            {
                Assert.That(q.IsEarned(i), Is.True, $"step {i}");
            }
        }

        [Test]
        public void IsEarned_rejects_out_of_range_steps()
        {
            var q = new QuestTracker(Plan());
            Assert.Throws<Cosmimera.Core.Contracts.ContractException>(() => q.IsEarned(-1));
            Assert.Throws<Cosmimera.Core.Contracts.ContractException>(() => q.IsEarned(5));
        }
    }
}
