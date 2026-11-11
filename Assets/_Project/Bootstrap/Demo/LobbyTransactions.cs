using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cosmimera.Core.Contracts;
using Cosmimera.Creatures;
using Cosmimera.Save;

namespace Cosmimera.Bootstrap.Demo
{
    /// <summary>How a lobby save transaction ended.</summary>
    /// <remarks>Pre: none. Post: none. Side effects: none.</remarks>
    internal enum CommitStatus
    {
        Committed,
        Refused,
        StockFull,
        WriteFailed,
    }

    /// <summary>
    /// All-or-nothing lobby save changes: every change is applied to a <see cref="LobbySave.Clone"/> candidate, written,
    /// and only then handed back as the new live save. A failed write leaves the live save exactly as it was, so memory
    /// and disk never disagree (SOL P2-05 W-01, W-02).
    /// </summary>
    /// <remarks>Pre: main thread. Side effects: file I/O at the given path; never mutates the live save.</remarks>
    internal static class LobbyTransactions
    {
        /// <summary>Writes <paramref name="candidate"/>; reports failure instead of throwing for recoverable write errors.</summary>
        /// <remarks>
        /// Pre: candidate non-null; path non-empty.
        /// Post: true iff the file now holds candidate. false iff writing threw an I/O, access or save-domain error
        ///       (<paramref name="error"/> holds it); File.Replace keeps the previous file whole in that case.
        /// Side effects: writes files.
        /// </remarks>
        public static bool TryWrite(LobbySave candidate, string path, out Exception error)
        {
            Contract.Requires(candidate != null && !string.IsNullOrEmpty(path), "candidate and path are required");
            error = null;
            try
            {
                candidate.Write(path);
                return true;
            }
            // InvalidOperationException: ToJson refuses a stock outside the save domain (W-04); treated like a failed
            // write so the live save is kept rather than the lobby crashing mid-frame.
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is InvalidOperationException)
            {
                error = e;
                return false;
            }
        }

        /// <summary>Evolves one owned creature in the lobby and persists it, or changes nothing.</summary>
        /// <remarks>
        /// Pre: live non-null; instanceId owned by live; requirement built from that creature's species; progression
        ///      non-null; path non-empty.
        /// Post: Committed → <paramref name="next"/> is a new save with the creature's species replaced and the
        ///       materials consumed, and the file holds it. Refused → requirement not met (<paramref name="result"/>
        ///       lists every shortfall), nothing written. WriteFailed → <paramref name="error"/> set. In every case
        ///       except Committed, next == live. live itself is never mutated.
        /// Side effects: writes files on success.
        /// </remarks>
        public static CommitStatus Evolve(LobbySave live, int instanceId, in EvolutionRequirement requirement,
            Progression progression, string path, out LobbySave next, out EvolutionResult result, out Exception error)
        {
            Contract.Requires(live != null && progression != null, "live and progression are required");
            // Checked here, not only in TryWrite: a Refused early return would otherwise hide a bad path (SOL r2 R2-04).
            Contract.Requires(!string.IsNullOrEmpty(path), "path must not be empty");
            next = live;
            error = null;
            LobbySave candidate = live.Clone();
            // TryEvolve consumes from the inventory it is given: hand it the candidate's, so a refused or unwritten
            // evolution never touches the live stock.
            result = EvolutionRules.TryEvolve(candidate.Get(instanceId), requirement, candidate.Materials,
                EvolutionVenue.Lobby, progression);
            if (!result.Succeeded)
            {
                return CommitStatus.Refused;
            }

            candidate.SetSpecies(instanceId, result.Creature.SpeciesId);
            if (!TryWrite(candidate, path, out error))
            {
                return CommitStatus.WriteFailed;
            }

            next = candidate;
            return CommitStatus.Committed;
        }

        /// <summary>Banks a finished run (XP, captures, materials) into the lobby save and persists it, or changes nothing.</summary>
        /// <remarks>
        /// Pre: live, partyIds, outcome and progression non-null; outcome.PlayerExp &lt;= progression.PlayerMaxExp; partyIds owned by live and outcome.PartyExp.Count == partyIds.Count;
        ///      outcome.PlayerExp &gt;= live.PlayerExp (a run never takes player XP away); path non-empty.
        /// Post: Committed → <paramref name="next"/> holds the outcome and the file holds next. StockFull → banking would
        ///       push <paramref name="overflow"/> past int.MaxValue; nothing applied, nothing written (never truncated).
        ///       WriteFailed → <paramref name="error"/> set. Otherwise next == live; live is never mutated, so after a
        ///       non-Committed result calling again with the same live and outcome is a safe retry. Not idempotent after
        ///       Committed: settling the same outcome into next banks it again (no settlement token, SOL r2 R2-01) —
        ///       callers submit an outcome until its first Committed and never after.
        /// Side effects: writes files on success.
        /// </remarks>
        public static CommitStatus Settle(LobbySave live, IReadOnlyList<int> partyIds, RunOutcome outcome, Progression progression,
            string path, out LobbySave next, out MaterialKey overflow, out Exception error)
        {
            Contract.Requires(live != null && partyIds != null && outcome != null && progression != null,
                "live, partyIds, outcome and progression are required");
            Contract.Requires(outcome.PlayerExp <= progression.PlayerMaxExp, "outcome.PlayerExp must not exceed PlayerMaxExp");
            Contract.Requires(outcome.PartyExp.Count == partyIds.Count, "one PartyExp entry per party slot");
            Contract.Requires(outcome.PlayerExp >= live.PlayerExp, "outcome.PlayerExp must not be below live.PlayerExp");
            // Before the StockFull early return, so a caller bug is never masked by a data refusal (SOL r2 R2-04).
            Contract.Requires(!string.IsNullOrEmpty(path), "path must not be empty");
            for (int i = 0; i < partyIds.Count; i++)
            {
                Contract.Requires(live.Owned.Any(o => o.InstanceId == partyIds[i]), "party ids must be owned by live");
            }

            next = live;
            error = null;
            // Checked before any change so the player is told which key is full (W-02): AddMaterials would only throw.
            if (live.WouldOverflow(outcome.Materials, out overflow))
            {
                return CommitStatus.StockFull;
            }

            LobbySave candidate = live.Clone();
            // XP before captures: a new capture never receives its own capture reward (Q-M4).
            for (int i = 0; i < partyIds.Count; i++)
            {
                candidate.SetExp(partyIds[i], outcome.PartyExp[i]);
            }

            candidate.SetPlayerExp(outcome.PlayerExp, progression);

            try
            {
                foreach ((string speciesId, VesselKind vessel, int exp) in outcome.Captured)
                {
                    candidate.Add(speciesId, vessel, exp);
                }
            }
            catch (InvalidOperationException e)
            {
                // Instance ids exhausted: no retry can fix it, but the live save stays intact and the error is shown.
                error = e;
                return CommitStatus.WriteFailed;
            }

            candidate.AddMaterials(outcome.Materials);
            if (!TryWrite(candidate, path, out error))
            {
                return CommitStatus.WriteFailed;
            }

            next = candidate;
            return CommitStatus.Committed;
        }
    }
}
