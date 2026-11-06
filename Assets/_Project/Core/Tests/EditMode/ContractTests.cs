using Cosmimera.Core.Contracts;
using NUnit.Framework;

namespace Cosmimera.Core.Tests
{
    public sealed class ContractTests
    {
        [Test]
        public void Requires_True_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => Contract.Requires(true, "ok"));
        }

        [Test]
        public void Requires_False_ThrowsWithPreconditionPrefix()
        {
            var ex = Assert.Throws<ContractException>(() => Contract.Requires(false, "x > 0"));
            StringAssert.StartsWith("Precondition failed: x > 0", ex.Message);
        }

        [Test]
        public void Ensures_True_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => Contract.Ensures(true, "ok"));
        }

        [Test]
        public void Ensures_False_ThrowsWithPostconditionPrefix()
        {
            var ex = Assert.Throws<ContractException>(() => Contract.Ensures(false, "result >= 0"));
            StringAssert.StartsWith("Postcondition failed: result >= 0", ex.Message);
        }

        [Test]
        public void ContractException_NullMessage_HasFallbackText()
        {
            Assert.IsNotNull(new ContractException(null).Message);
        }
    }
}
