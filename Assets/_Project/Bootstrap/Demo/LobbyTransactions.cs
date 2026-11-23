using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cosmimera.Capture;
using Cosmimera.Core.Contracts;
using Cosmimera.Core.Random;
using Cosmimera.Creatures;
using Cosmimera.Crafting;
using Cosmimera.GameData;
using Cosmimera.Save;
using Cosmimera.WorldGen;

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

    /// <summary>How eggs hatch while a run is settled (lobby-meta.md#????, Q-M3).</summary>
    /// <remarks>
    /// Pre: Catalog non-null. Post: Hatched lists (eggId, speciesId) for the last Committed settlement only.
    /// Side effects: Settle rewrites Hatched.
    /// </remarks>
    internal sealed class HatchSettlement
    {
        /// <summary>Egg definitions; an egg whose id is missing stays incubating (retired id never breaks a save).</summary>
        public EggCatalog Catalog;

        /// <summary>Seed of the hatch roll; the same save, outcome and seed always hatch the same species.</summary>
        public ulong Seed;

        /// <summary>Eggs hatched by the last Committed settlement, in hatch order.</summary>
        public readonly List<(string eggId, string speciesId)> Hatched = new List<(string, string)>();
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
            Progression progression, string path, out LobbySave next, out EvolutionResult result, out Exception error,
            CreatureDef evolvedDef = null)
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

            if (evolvedDef != null && evolvedDef.Niches.Count > 0)
            {
                WildIndividual ind = WildIndividual.Draw(evolvedDef, null,
                    WildIndividual.KeyFor(Fnv1a64(evolvedDef.Id), (ulong)instanceId));
                candidate.SetSpecies(instanceId, result.Creature.SpeciesId, ind.Niche.Id, ind.Habit);
            }
            else
            {
                candidate.SetSpecies(instanceId, result.Creature.SpeciesId);
            }

            if (!TryWrite(candidate, path, out error))
            {
                return CommitStatus.WriteFailed;
            }

            next = candidate;
            return CommitStatus.Committed;
        }

        /// <summary>
        /// One-time migration for legacy saves whose owned creatures have no drawn niche/habit yet.
        /// REQ: docs/design/creature-ecology.md#戰鬥、障礙與重現性.
        /// </summary>
        /// <remarks>
        /// Pre: live and defOf non-null; path non-empty.
        /// Post: Committed with next == live (no disk write) when every owned creature already has ecology or lacks
        ///       niche candidates; otherwise next has ecology drawn deterministically from each creature's InstanceId
        ///       (no habitat filter) and persisted to path. WriteFailed leaves next == live and sets error.
        /// Side effects: writes path only when at least one creature is migrated.
        /// </remarks>
        public static CommitStatus MigrateEcology(LobbySave live, Func<string, CreatureDef> defOf, string path,
            out LobbySave next, out Exception error)
        {
            Contract.Requires(live != null && defOf != null, "live and defOf are required");
            Contract.Requires(!string.IsNullOrEmpty(path), "path must not be empty");
            next = live;
            error = null;
            LobbySave candidate = null;
            foreach (OwnedCreature o in live.Owned)
            {
                if (o.HasEcology)
                {
                    continue;
                }

                CreatureDef def = defOf(o.SpeciesId);
                if (def == null || def.Niches.Count == 0)
                {
                    continue;
                }

                candidate ??= live.Clone();
                WildIndividual ind = WildIndividual.Draw(def, null,
                    WildIndividual.KeyFor(0x4D494752UL, (ulong)o.InstanceId));
                candidate.SetEcology(o.InstanceId, ind.Niche.Id, ind.Habit);
            }

            if (candidate == null)
            {
                return CommitStatus.Committed;
            }

            if (!TryWrite(candidate, path, out error))
            {
                return CommitStatus.WriteFailed;
            }

            next = candidate;
            return CommitStatus.Committed;
        }

        private static ulong Fnv1a64(string text)
        {
            ulong h = 14695981039346656037UL;
            for (int i = 0; i < text.Length; i++)
            {
                h = unchecked((h ^ text[i]) * 1099511628211UL);
            }

            return h;
        }

        /// <summary>Banks a finished run (XP, captures, materials) into the lobby save and persists it, or changes nothing.</summary>
        /// <remarks>
        /// Pre: live, partyIds, outcome and progression non-null; outcome.PlayerExp &lt;= progression.PlayerMaxExp; partyIds owned by live and outcome.PartyExp.Count == partyIds.Count;
        ///      outcome.PlayerExp &gt;= live.PlayerExp (a run never takes player XP away); path non-empty.
        ///      PartnerExp keys are departure subs, and their values never decrease or exceed progression.MaxExp.
        /// Post: Committed → <paramref name="next"/> holds the outcome and the file holds next. StockFull → banking would
        ///       push <paramref name="overflow"/> past int.MaxValue; nothing applied, nothing written (never truncated).
        ///       WriteFailed → <paramref name="error"/> set. Otherwise next == live; live is never mutated, so after a
        ///       non-Committed result calling again with the same live and outcome is a safe retry. Not idempotent after
        ///       Committed: settling the same outcome into next banks it again (no settlement token, SOL r2 R2-01) —
        ///       callers submit an outcome until its first Committed and never after.
        ///       Eggs: outcome.EggProgressPpm is added to every egg incubating in live (eggs placed by this settlement never
        ///       gain from the run that produced them); with <paramref name="hatch"/> non-null, ready eggs with a known
        ///       definition hatch into new owned creatures (LV1, vessel inherited), otherwise they wait in the hatchery.
        /// Side effects: writes files on success; rewrites hatch.Hatched when hatch is non-null.
        /// </remarks>
        public static CommitStatus Settle(LobbySave live, IReadOnlyList<int> partyIds, RunOutcome outcome, Progression progression,
            string path, out LobbySave next, out MaterialKey overflow, out Exception error, HatchSettlement hatch = null)
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
            foreach (KeyValuePair<int, int> partner in outcome.PartnerExp)
            {
                Contract.Requires(live.Partners.Any(p => p.SubInstanceId == partner.Key && partyIds.Contains(p.MainInstanceId)),
                    "partner XP must belong to an equipped sub of a departing main");
                Contract.Requires(partner.Value >= live.Get(partner.Key).Exp && partner.Value <= progression.MaxExp,
                    "partner XP must not decrease or exceed MaxExp");
            }
            foreach (KeyValuePair<string, int> kv in outcome.TrainerBattles)
            {
                Contract.Requires(CraftInventory.ValidId(kv.Key) && kv.Value >= 1,
                    "trainer battle entries must use valid profession ids and count >= 1");
            }
            foreach (string trialId in outcome.CompletedTrials)
            {
                Contract.Requires(CraftInventory.ValidId(trialId), "completed trial ids must be valid snake_case");
            }
            foreach (string recipeId in outcome.DiscoveredRecipes)
            {
                Contract.Requires(CraftInventory.ValidId(recipeId), "discovered recipe ids must be valid snake_case");
            }
            Contract.Requires(outcome.GoldEarned >= 0, "GoldEarned must be non-negative");
            if (live.WouldOverflow(outcome.Materials, out overflow))
            {
                return CommitStatus.StockFull;
            }

            LobbySave candidate = live.Clone();
            List<(string eggId, string speciesId)> hatched = null;
            // XP before captures: a new capture never receives its own capture reward (Q-M4).
            for (int i = 0; i < partyIds.Count; i++)
            {
                candidate.SetExp(partyIds[i], outcome.PartyExp[i]);
            }

            candidate.SetPlayerExp(outcome.PlayerExp, progression);
            foreach (KeyValuePair<int, int> partner in outcome.PartnerExp) candidate.SetExp(partner.Key, partner.Value);

            try
            {
                foreach ((string speciesId, VesselKind vessel, int exp, string nicheId, Habit habit) in outcome.Captured)
                {
                    candidate.Add(speciesId, vessel, exp, nicheId, habit);
                }

                hatched = ApplyHatchery(candidate, outcome.EggProgressPpm, hatch);
            }
            catch (InvalidOperationException e)
            {
                // Instance ids exhausted: no retry can fix it, but the live save stays intact and the error is shown.
                error = e;
                return CommitStatus.WriteFailed;
            }

            candidate.AddMaterials(outcome.Materials);
            if (outcome.GoldEarned > 0)
            {
                candidate.AddGold(outcome.GoldEarned);
            }
            candidate.Codex.Merge(outcome.Codex);
            // Recipe discovery is knowledge, like the codex: kept on abandon (cuisine.md#6).
            var recipeIds = new List<string>(outcome.DiscoveredRecipes);
            recipeIds.Sort(StringComparer.Ordinal);
            foreach (string recipeId in recipeIds) candidate.RecordRecipeDiscovered(recipeId);
            if (!outcome.Abandoned)
            {
                if (outcome.TrainerBattles.Count > 0)
                {
                    var profIds = new List<string>(outcome.TrainerBattles.Keys);
                    profIds.Sort(StringComparer.Ordinal);
                    foreach (string profId in profIds)
                    {
                        candidate.AddTrainerBattles(profId, outcome.TrainerBattles[profId]);
                    }
                }

                if (outcome.CompletedTrials.Count > 0)
                {
                    var trialIds = new List<string>(outcome.CompletedTrials);
                    trialIds.Sort(StringComparer.Ordinal);
                    foreach (string trialId in trialIds)
                    {
                        candidate.RecordTrialCompleted(trialId);
                    }
                }
            }
            // The run already spent from its departure copy: adding its final stock would duplicate old items.
            if (outcome.CraftStock != null && !candidate.CraftStock.TryExchange(candidate.CraftStock.Snapshot(), outcome.CraftStock))
            {
                error = new InvalidOperationException("Final crafting stock exceeds capacity");
                return CommitStatus.WriteFailed;
            }
            if (outcome.CraftStock != null && outcome.CraftLooks != null) candidate.CraftStock.RestoreLooks(outcome.CraftLooks);
            if (!TryWrite(candidate, path, out error))
            {
                return CommitStatus.WriteFailed;
            }

            if (hatch != null)
            {
                hatch.Hatched.Clear();
                hatch.Hatched.AddRange(hatched);
            }

            next = candidate;
            return CommitStatus.Committed;
        }

        // Hatch creatures start at exp 0 (LV1): the egg table lists juveniles only (validate-data egg_outcome_stage).
        private static List<(string eggId, string speciesId)> ApplyHatchery(LobbySave candidate, long progressPpm,
            HatchSettlement hatch)
        {
            var hatched = new List<(string eggId, string speciesId)>();
            if (candidate.Eggs.Count == 0)
            {
                return hatched;
            }

            var hatchery = new EggHatchery(HatchConfig.Default);
            foreach (IncubatingEgg egg in candidate.Eggs)
            {
                hatchery.TryPlace(egg);
            }

            hatchery.AddProgressPpm(progressPpm);
            if (hatch != null)
            {
                var rng = new Pcg32(hatch.Seed, 'H');
                foreach (IncubatingEgg egg in new List<IncubatingEgg>(hatchery.Eggs))
                {
                    if (!egg.IsReady || !hatch.Catalog.TryGet(egg.EggId, out EggDef def))
                    {
                        continue;
                    }

                    HatchResult result = hatchery.Hatch(egg, def, ref rng);
                    candidate.Add(result.SpeciesId, (VesselKind)result.Vessel, 0);
                    hatched.Add((egg.EggId, result.SpeciesId));
                }
            }

            candidate.SetEggs(hatchery.Eggs);
            return hatched;
        }
    }
}
