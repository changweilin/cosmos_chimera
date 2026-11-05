using System;

namespace Cosmimera.Core.Contracts
{
    /// <summary>Signals that a caller or callee broke a documented Pre:/Post: contract.</summary>
    /// <remarks>
    /// Pre: none.
    /// Post: Message is non-null.
    /// Side effects: none.
    /// Never catch this to recover: a broken contract is a programming error, not a runtime condition.
    /// </remarks>
    public sealed class ContractException : Exception
    {
        /// <summary>Creates the exception with a human-readable description of the violated condition.</summary>
        /// <remarks>
        /// Pre: none (null message is replaced with a generic text).
        /// Post: Message is non-null.
        /// Side effects: allocates.
        /// </remarks>
        public ContractException(string message)
            : base(message ?? "Contract violated.")
        {
        }
    }
}
