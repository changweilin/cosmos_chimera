using System.Diagnostics;

namespace Cosmimera.Core.Contracts
{
    /// <summary>Runtime checks for Design-by-Contract Pre:/Post: conditions (see .claude/rules/code-contracts.md).</summary>
    /// <remarks>
    /// Pre: none.
    /// Post: in Editor / Development builds a false condition throws <see cref="ContractException"/>;
    ///       in Release builds every call (including argument evaluation) is removed by the compiler.
    /// Side effects: none unless the check fails.
    /// </remarks>
    public static class Contract
    {
        // [Conditional] instead of #if inside the body: the compiler strips the call site itself, so the
        // condition expression and any string formatting the caller wrote are never evaluated in Release.
        // Debug.Assert is not used because it only logs and would not fail an EditMode test.
        // The flip side: conditions MUST be side-effect free, or Release behaviour silently diverges.

        /// <summary>Asserts a precondition the caller is responsible for.</summary>
        /// <remarks>
        /// Pre: <paramref name="condition"/> has no side effects; <paramref name="message"/> should name the violated rule.
        /// Post: returns normally iff condition is true (Editor/Development); always returns (Release, call stripped).
        /// Side effects: throws <see cref="ContractException"/> on violation.
        /// </remarks>
        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        public static void Requires(bool condition, string message)
        {
            if (condition)
            {
                return;
            }

            throw new ContractException("Precondition failed: " + message);
        }

        /// <summary>Asserts a postcondition the callee is responsible for.</summary>
        /// <remarks>
        /// Pre: <paramref name="condition"/> has no side effects; <paramref name="message"/> should name the violated rule.
        /// Post: returns normally iff condition is true (Editor/Development); always returns (Release, call stripped).
        /// Side effects: throws <see cref="ContractException"/> on violation.
        /// </remarks>
        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        public static void Ensures(bool condition, string message)
        {
            if (condition)
            {
                return;
            }

            throw new ContractException("Postcondition failed: " + message);
        }
    }
}
