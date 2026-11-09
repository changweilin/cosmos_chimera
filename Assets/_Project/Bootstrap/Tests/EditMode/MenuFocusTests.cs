using Cosmimera.Bootstrap.Demo;
using Cosmimera.Core.Contracts;
using NUnit.Framework;

namespace Cosmimera.Bootstrap.Tests
{
    public sealed class MenuFocusTests
    {
        [Test]
        public void Wraps_and_handles_empty_and_changed_counts()
        {
            var focus = new MenuFocus();
            focus.Move(1);
            Assert.That(focus.Index, Is.EqualTo(-1));
            focus.SetCount(3);
            focus.Move(-1);
            Assert.That(focus.Index, Is.EqualTo(2));
            focus.Move(1);
            Assert.That(focus.Index, Is.EqualTo(0));
            focus.Move(2);
            focus.SetCount(2);
            Assert.That(focus.Index, Is.EqualTo(1));
            focus.SetCount(0);
            Assert.That(focus.Index, Is.EqualTo(-1));
            focus.SetCount(4);
            Assert.That(focus.Index, Is.EqualTo(0));
            Assert.Throws<ContractException>(() => focus.SetCount(-1));
        }
    }
}
