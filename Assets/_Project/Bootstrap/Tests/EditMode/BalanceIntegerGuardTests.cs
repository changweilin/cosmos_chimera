using System.IO;
using Cosmimera.Bootstrap.Demo;
using NUnit.Framework;
using UnityEngine;

namespace Cosmimera.Bootstrap.Tests
{
    // REQ: docs/reviews/P2-05-wiring-sol.md W-03 — JsonUtility truncates/wraps these silently.
    public sealed class BalanceIntegerGuardTests
    {
        private static string Materials(string eliteQty) =>
            "{\"materials\":{\"normal\":{\"tier\":1,\"chance_percent\":10,\"qty\":1}," +
            "\"elite\":{\"tier\":2,\"chance_percent\":60,\"qty\":" + eliteQty + "}," +
            "\"boss\":{\"tier\":3,\"chance_percent\":100,\"qty\":1}}}";

        [TestCase("2.5")]
        [TestCase("2.0")]
        [TestCase("1e1")]
        [TestCase("\"2\"")]
        [TestCase("4294967298")]
        [TestCase("99999999999999999999999")]
        [TestCase("null")]
        [TestCase("true")]
        public void Non_int32_numbers_are_refused(string qty)
        {
            var e = Assert.Throws<InvalidDataException>(
                () => BalanceIntegerGuard.RequireIntegers(Materials(qty), "materials"));
            StringAssert.Contains("materials.elite.qty", e.Message);
        }

        [Test]
        public void Integers_and_nested_arrays_pass()
        {
            Assert.DoesNotThrow(() => BalanceIntegerGuard.RequireIntegers(Materials("2"), "materials"));
            Assert.DoesNotThrow(() => BalanceIntegerGuard.RequireIntegers("{\"p\":{\"a\":[1,[2,-3]]}}", "p"));
        }

        [Test]
        public void Missing_section_or_bad_json_is_refused()
        {
            Assert.Throws<InvalidDataException>(() => BalanceIntegerGuard.RequireIntegers("{}", "materials"));
            Assert.Throws<InvalidDataException>(() => BalanceIntegerGuard.RequireIntegers("{", "materials"));
        }

        // The shipped file must pass the same guard DemoGame applies at load.
        [Test]
        public void Shipped_balance_json_passes()
        {
            string path = Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath, "..")), "Data", "balance.json");
            Assert.DoesNotThrow(() => BalanceIntegerGuard.RequireIntegers(File.ReadAllText(path), "materials", "progression"));
        }
    }
}
