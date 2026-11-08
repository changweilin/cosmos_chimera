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
        }
    }
}
