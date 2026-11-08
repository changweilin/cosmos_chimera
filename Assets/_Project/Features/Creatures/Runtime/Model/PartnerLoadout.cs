using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Cosmimera.Core.Contracts;

namespace Cosmimera.Creatures
{
    /// <summary>
    /// Which sub-partners serve which main: max 4 per main, a relation edge required, one creature serves at most
    /// one main, a main is never a sub, and a sub's stage never exceeds its main's. Sub-partners do not level or grow.
    /// REQ: docs/design/creatures.md#隊伍規則 (Q-C5), Q-C10 decided 2026-11-06; stage rule decided 2026-11-08 (P5-08 N3, option c).
    /// </summary>
    /// <remarks>
    /// Stores instance ids only. Species, vessel and experience live with the caller's creature collection, so an
    /// evolution (or a later vessel conversion) can never leave a stale copy here. Relation edges are per form id,
    /// hence an evolution can invalidate an existing pairing: <see cref="FindInvalidPairs"/> reports those and
    /// nothing is removed automatically (the player decides in the lobby).
    /// Invariant: every main has 0..MaxSubsPerMain subs; no sub is also a main; no sub serves two mains.
    /// Exposed lists are read-only views.
    /// Pre: construct through the public constructor; main-thread or externally synchronized (not thread-safe).
    /// Post: the invariant above holds after every public call.
    /// Side effects: only TryAssign and Unassign mutate the assignment.
    /// </remarks>
    public sealed class PartnerLoadout
    {
        /// <summary>Most mains a loadout covers (the run's carried roster; mirrors the team rule, Q-C4).</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const int MaxMains = 6;

        /// <summary>Most sub-partners per main (Q-C5).</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const int MaxSubsPerMain = 4;

        private readonly List<int> _mains;
        private readonly ReadOnlyCollection<int> _mainsView;
        private readonly Dictionary<int, List<int>> _subsByMain = new Dictionary<int, List<int>>();
        private readonly Dictionary<int, ReadOnlyCollection<int>> _subViewsByMain = new Dictionary<int, ReadOnlyCollection<int>>();
        private readonly Dictionary<int, int> _mainOfSub = new Dictionary<int, int>();

        /// <summary>Creates a loadout for the given mains, with nobody assigned.</summary>
        /// <remarks>
        /// Pre: mains != null; 1 &lt;= mains.Count &lt;= MaxMains; ids unique and &gt;= 1.
        /// Post: Mains equals mains in order; every main has no subs.
        /// Side effects: copies mains.
        /// </remarks>
        public PartnerLoadout(IReadOnlyList<int> mains)
        {
            Contract.Requires(mains != null, "mains is null");
            Contract.Requires(mains.Count >= 1 && mains.Count <= MaxMains, "mains count must be 1..6");
            _mains = new List<int>(mains);
            foreach (int id in _mains)
            {
                Contract.Requires(id >= 1, "main instance ids must be >= 1");
                Contract.Requires(!_subsByMain.ContainsKey(id), "main instance ids must be unique");
                var subs = new List<int>(MaxSubsPerMain);
                _subsByMain.Add(id, subs);
                _subViewsByMain.Add(id, subs.AsReadOnly());
            }

            _mainsView = _mains.AsReadOnly();
        }

        /// <summary>The mains this loadout covers, fixed at construction.</summary>
        /// <remarks>Pre: none. Post: 1..MaxMains unique ids; read-only view. Side effects: none.</remarks>
        public IReadOnlyList<int> Mains => _mainsView;

        /// <summary>The subs serving <paramref name="mainInstanceId"/>, in assignment order.</summary>
        /// <remarks>Pre: mainInstanceId is one of Mains. Post: 0..MaxSubsPerMain ids; live read-only view. Side effects: none.</remarks>
        public IReadOnlyList<int> SubsOf(int mainInstanceId)
        {
            Contract.Requires(_subViewsByMain.ContainsKey(mainInstanceId), "mainInstanceId is not one of Mains");
            return _subViewsByMain[mainInstanceId];
        }

        /// <summary>Which main a sub serves, if any.</summary>
        /// <remarks>Pre: none. Post: true iff the creature is currently someone's sub; then mainInstanceId is that main, else 0. Side effects: none.</remarks>
        public bool TryGetMainOf(int subInstanceId, out int mainInstanceId)
        {
            return _mainOfSub.TryGetValue(subInstanceId, out mainInstanceId);
        }

        /// <summary>Assigns <paramref name="sub"/> to <paramref name="main"/>.</summary>
        /// <remarks>
        /// Pre: main.InstanceId &gt;= 1 and sub.InstanceId &gt;= 1 (constructed values; main/sub carry their CURRENT
        ///      species, which the relation and stage checks use); relations != null; species != null and knows both
        ///      species once checks 1–5 pass (a missing species is data/save corruption, so it throws).
        /// Post: Ok iff all of the following hold, refusals reported in exactly this order and leaving state unchanged:
        ///       1. UnknownMain — main is one of Mains.
        ///       2. SubIsAMain — sub is not any of Mains (this includes sub == main).
        ///       3. AlreadyAssignedToThisMain / AlreadySubOfAnotherMain — sub serves nobody yet (a sub already serving
        ///          this main is reported as ...ToThisMain even when that main is full).
        ///       4. MainFull — main has fewer than MaxSubsPerMain subs.
        ///       5. NoRelation — relations.AreRelated(main.SpeciesId, sub.SpeciesId); only consulted if 1–4 passed.
        ///       6. SubStageAboveMain — sub's stage &lt;= main's stage (species lookup); only consulted if 1–5 passed.
        ///       On Ok the sub is appended to SubsOf(main).
        /// Side effects: mutates the assignment on Ok.
        /// </remarks>
        public PartnerResult TryAssign(in OwnedCreature main, in OwnedCreature sub, IRelationGraph relations, ISpeciesLookup species)
        {
            Contract.Requires(main.InstanceId >= 1, "main must be a constructed OwnedCreature (InstanceId >= 1)");
            Contract.Requires(sub.InstanceId >= 1, "sub must be a constructed OwnedCreature (InstanceId >= 1)");
            Contract.Requires(relations != null, "relations is null");
            Contract.Requires(species != null, "species is null");

            if (!_subsByMain.TryGetValue(main.InstanceId, out List<int> subs))
            {
                return PartnerResult.UnknownMain;
            }

            if (_subsByMain.ContainsKey(sub.InstanceId))
            {
                return PartnerResult.SubIsAMain;
            }

            if (_mainOfSub.TryGetValue(sub.InstanceId, out int currentMain))
            {
                return currentMain == main.InstanceId ? PartnerResult.AlreadyAssignedToThisMain : PartnerResult.AlreadySubOfAnotherMain;
            }

            if (subs.Count >= MaxSubsPerMain)
            {
                return PartnerResult.MainFull;
            }

            if (!relations.AreRelated(main.SpeciesId, sub.SpeciesId))
            {
                return PartnerResult.NoRelation;
            }

            // REQ: P5-08 N3 option c — applies to all three vessels, since damage ignores stats and a juvenile main
            // would otherwise borrow an adult's 95–120 power finisher (or trait / overload) on day one.
            bool mainKnown = species.TryGet(main.SpeciesId, out SpeciesInfo mainInfo);
            Contract.Requires(mainKnown, "main species '" + main.SpeciesId + "' is not in the species data");
            bool subKnown = species.TryGet(sub.SpeciesId, out SpeciesInfo subInfo);
            Contract.Requires(subKnown, "sub species '" + sub.SpeciesId + "' is not in the species data");
            if (subInfo.Stage > mainInfo.Stage)
            {
                return PartnerResult.SubStageAboveMain;
            }

            subs.Add(sub.InstanceId);
            _mainOfSub.Add(sub.InstanceId, main.InstanceId);
            return PartnerResult.Ok;
        }

        /// <summary>Releases a sub-partner so it can serve any main again.</summary>
        /// <remarks>
        /// Pre: subInstanceId &gt;= 1.
        /// Post: Ok iff the creature is currently someone's sub; then it is removed from that main (the other subs keep
        ///       their order). Otherwise NotAssigned and state unchanged.
        /// Side effects: mutates the assignment on Ok.
        /// </remarks>
        public PartnerResult Unassign(int subInstanceId)
        {
            Contract.Requires(subInstanceId >= 1, "subInstanceId must be >= 1");
            if (!_mainOfSub.TryGetValue(subInstanceId, out int main))
            {
                return PartnerResult.NotAssigned;
            }

            _subsByMain[main].Remove(subInstanceId);
            _mainOfSub.Remove(subInstanceId);
            return PartnerResult.Ok;
        }

        /// <summary>Lists assignments whose two forms no longer have a relation edge (typically after an evolution).</summary>
        /// <remarks>
        /// The stage rule is not re-checked: mains only evolve upward and subs never grow (Q-C10), so a pair accepted
        /// by TryAssign cannot later violate sub stage &lt;= main stage.
        /// Pre: relations != null; currentSpeciesOf != null and returns a non-empty species id for every assigned sub and
        ///      for every main that has subs (the caller owns the creature collection; release a sub via Unassign first).
        /// Post: one entry per assignment with !relations.AreRelated(currentSpeciesOf(main), currentSpeciesOf(sub)),
        ///       ordered by Mains order then assignment order; empty when all pairs are still valid.
        /// Side effects: none — assignments are NOT removed; the player decides.
        /// </remarks>
        public IReadOnlyList<PartnerPair> FindInvalidPairs(IRelationGraph relations, Func<int, string> currentSpeciesOf)
        {
            Contract.Requires(relations != null, "relations is null");
            Contract.Requires(currentSpeciesOf != null, "currentSpeciesOf is null");

            var invalid = new List<PartnerPair>();
            foreach (int main in _mains)
            {
                List<int> subs = _subsByMain[main];
                if (subs.Count == 0)
                {
                    continue;
                }

                string mainSpecies = currentSpeciesOf(main);
                Contract.Requires(!string.IsNullOrEmpty(mainSpecies), "currentSpeciesOf returned no species for main " + main);
                foreach (int sub in subs)
                {
                    string subSpecies = currentSpeciesOf(sub);
                    Contract.Requires(!string.IsNullOrEmpty(subSpecies), "currentSpeciesOf returned no species for sub " + sub);
                    if (!relations.AreRelated(mainSpecies, subSpecies))
                    {
                        invalid.Add(new PartnerPair(main, sub));
                    }
                }
            }

            return invalid.AsReadOnly();
        }

        /// <summary>What each of a main's subs provides, in assignment order.</summary>
        /// <remarks>
        /// Pre: mainInstanceId is one of Mains; mainSpeciesId non-empty (the main's current form); creatureOf != null and
        ///      returns, for every sub of this main, the individual whose InstanceId equals the requested id; species != null;
        ///      the other <see cref="Provision.Resolve"/> preconditions hold for each sub.
        /// Post: one Provision per sub (SubsOf(main).Count entries): a talisman_card sub gives a move, a glass_bead
        ///       sub its trait, a magic_cube sub an Overload unlock. Several magic_cube subs yield several Overload
        ///       entries; "overload unlocked" means at least one (the overload rules themselves are Q-C12).
        /// Side effects: none.
        /// </remarks>
        public IReadOnlyList<Provision> ResolveProvisions(int mainInstanceId, string mainSpeciesId, Func<int, OwnedCreature> creatureOf, ISpeciesLookup species)
        {
            Contract.Requires(_subsByMain.ContainsKey(mainInstanceId), "mainInstanceId is not one of Mains");
            Contract.Requires(!string.IsNullOrEmpty(mainSpeciesId), "mainSpeciesId must be non-empty");
            Contract.Requires(creatureOf != null, "creatureOf is null");
            // Checked here, not only inside Provision.Resolve: a main with no subs would otherwise skip the Pre.
            Contract.Requires(species != null, "species is null");

            List<int> subs = _subsByMain[mainInstanceId];
            var provisions = new List<Provision>(subs.Count);
            foreach (int subId in subs)
            {
                OwnedCreature sub = creatureOf(subId);
                Contract.Requires(sub.InstanceId == subId, "creatureOf returned the wrong individual for sub " + subId);
                provisions.Add(Provision.Resolve(sub, mainSpeciesId, species));
            }

            return provisions.AsReadOnly();
        }
    }
}
