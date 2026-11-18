using Cosmimera.Core.Contracts;
using NUnit.Framework;

namespace Cosmimera.Codex.Tests
{
    public sealed class CodexProgressTests
    {
        [TestCase(CodexEvent.Encounter, CodexEvent.Capture, CodexKind.Creature)]
        [TestCase(CodexEvent.Dialogue, CodexEvent.CharacterQuest, CodexKind.Character)]
        [TestCase(CodexEvent.SkillSeen, CodexEvent.SkillAcquired, CodexKind.Skill)]
        public void Events_advance_only_the_matching_subject_and_never_regress(CodexEvent basic, CodexEvent complete, CodexKind kind)
        {
            var progress = new CodexProgress();
            Assert.That(progress.Get(kind, "form"), Is.EqualTo(CodexLevel.Unknown));
            Assert.That(progress.Record(basic, "form"), Is.True);
            Assert.That(progress.Get(kind, "form"), Is.EqualTo(CodexLevel.Basic));
            Assert.That(progress.Record(basic, "form"), Is.False);
            Assert.That(progress.Record(complete, "form"), Is.True);
            Assert.That(progress.Record(basic, "form"), Is.False);
            Assert.That(progress.Get(kind, "form"), Is.EqualTo(CodexLevel.Complete));
            Assert.That(progress.Get(kind, "other_form"), Is.EqualTo(CodexLevel.Unknown));
        }

        [Test]
        public void Acquisition_before_observation_is_complete_and_snapshots_are_independent()
        {
            var progress = new CodexProgress();
            progress.Record(CodexEvent.Capture, "form");
            var snapshot = progress.Snapshot();
            var restored = new CodexProgress();
            restored.Restore(snapshot);
            progress.Record(CodexEvent.SkillAcquired, "move");
            Assert.That(restored.Get(CodexKind.Creature, "form"), Is.EqualTo(CodexLevel.Complete));
            Assert.That(restored.Get(CodexKind.Skill, "move"), Is.EqualTo(CodexLevel.Unknown));
            restored.Merge(progress);
            restored.Merge(restored);
            Assert.That(restored.Get(CodexKind.Skill, "move"), Is.EqualTo(CodexLevel.Complete));
        }

        [Test]
        public void Invalid_inputs_fail_and_invalid_restore_is_atomic()
        {
            var progress = new CodexProgress();
            progress.Record(CodexEvent.Dialogue, "person");
            Assert.Throws<ContractException>(() => progress.Get((CodexKind)99, "x"));
            Assert.Throws<ContractException>(() => progress.Get(CodexKind.Skill, ""));
            Assert.Throws<ContractException>(() => progress.Record((CodexEvent)99, "x"));
            Assert.Throws<ContractException>(() => progress.Record(CodexEvent.Capture, " "));
            Assert.Throws<ContractException>(() => progress.Merge(null));
            Assert.Throws<ContractException>(() => progress.Restore(null));
            Assert.Throws<ContractException>(() => progress.Restore(new[] { default(CodexRecord) }));
            var record = new CodexRecord(CodexKind.Skill, "x", CodexLevel.Basic);
            Assert.Throws<ContractException>(() => progress.Restore(new[] { record, record }));
            Assert.That(progress.Get(CodexKind.Character, "person"), Is.EqualTo(CodexLevel.Basic));
            Assert.Throws<ContractException>(() => new CodexRecord((CodexKind)99, "x", CodexLevel.Basic));
            Assert.Throws<ContractException>(() => new CodexRecord(CodexKind.Skill, "", CodexLevel.Basic));
            Assert.Throws<ContractException>(() => new CodexRecord(CodexKind.Skill, "x", CodexLevel.Unknown));
        }
    }
}
