using System;
using System.Collections.Generic;

namespace Cosmimera.GameData.Editor
{
    /// <summary>Thrown when source JSON cannot be imported; no asset was written.</summary>
    /// <remarks>Pre: errors non-empty. Post: <see cref="Errors"/> lists every problem found. Side effects: none.</remarks>
    public sealed class DataImportException : Exception
    {
        /// <summary>Creates the exception from the collected error lines.</summary>
        /// <remarks>Pre: errors non-null. Post: Message contains every error line. Side effects: none.</remarks>
        public DataImportException(IReadOnlyList<string> errors)
            : base("Data import aborted, no assets written:\n" + string.Join("\n", errors))
        {
            Errors = errors;
        }

        /// <summary>One line per problem: "file#index (id): message".</summary>
        public IReadOnlyList<string> Errors { get; }
    }
}
