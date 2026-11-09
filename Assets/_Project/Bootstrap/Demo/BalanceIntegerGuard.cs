using System.IO;
using Newtonsoft.Json.Linq;

namespace Cosmimera.Bootstrap.Demo
{
    /// <summary>Rejects balance.json sections whose numbers are not exact Int32 literals.</summary>
    /// <remarks>
    /// Why: JsonUtility maps a JSON number onto an int field by truncating and wrapping — "qty": 2.5, "2", 1e1 and
    /// 4294967298 load as 2, 2, 10 and 2 (probed 2026-11-10, Unity 6000.3.28f1) — so the range checks in
    /// MaterialDrops/Progression.FromJson cannot see the typo. REQ: docs/reviews/P2-05-wiring-sol.md W-03.
    /// Pre: none. Post: none. Side effects: none.
    /// </remarks>
    internal static class BalanceIntegerGuard
    {
        /// <summary>Throws unless every leaf of each named top-level section is a JSON integer within Int32.</summary>
        /// <remarks>
        /// Pre: json non-null. Post: returns normally iff each section exists and all its leaves are Int32 integer tokens;
        ///      otherwise <see cref="InvalidDataException"/> naming the offending path, in every build.
        /// Side effects: allocates the parsed tree.
        /// </remarks>
        internal static void RequireIntegers(string json, params string[] sections)
        {
            JObject root;
            try
            {
                root = JObject.Parse(json);
            }
            catch (Newtonsoft.Json.JsonReaderException e)
            {
                throw new InvalidDataException("balance json is not valid JSON: " + e.Message, e);
            }

            foreach (string name in sections)
            {
                JToken section = root[name];
                if (section == null || section.Type != JTokenType.Object)
                {
                    throw new InvalidDataException("balance json is missing the " + name + " section");
                }

                Walk(section);
            }
        }

        private static void Walk(JToken token)
        {
            if (token is JContainer container)
            {
                foreach (JToken child in container.Children())
                {
                    Walk(child is JProperty p ? p.Value : child);
                }

                return;
            }

            // Json.NET reads integers beyond Int64 as BigInteger and still tags them Integer, hence the range test.
            bool isInt32 = token.Type == JTokenType.Integer &&
                ((JValue)token).Value is long v && v >= int.MinValue && v <= int.MaxValue;
            if (!isInt32)
            {
                throw new InvalidDataException("balance json: " + token.Path + " must be an Int32 integer, got " +
                    token.ToString(Newtonsoft.Json.Formatting.None));
            }
        }
    }
}
