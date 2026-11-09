using System;
using System.Collections.Generic;
using Cosmimera.Core.Contracts;

namespace Cosmimera.Creatures
{
    /// <summary>
    /// Lobby-only evolution rules: level gate + element materials, materials consumed on success.
    /// REQ: docs/design/creatures.md#進化 (Q-C3), Q-C9 decided 2026-11-06; level replaces the old experience gate
    /// (docs/design/level-progression.md, P2-12).
    /// </summary>
    /// <remarks>
    /// Pre: callers pass the requirement of the creature's CURRENT form (<see cref="EvolutionRequirement.FromDef"/>);
    ///      the rules cannot verify that without data access.
    /// Post: experience is never reduced (evolving keeps the level); evolving never changes InstanceId, Vessel or Exp.
    /// Side effects: only <see cref="TryEvolve"/> mutates anything (the material inventory).
    /// </remarks>
    public static class EvolutionRules
    {
        /// <summary>Reports every reason the creature cannot evolve right now (empty set of reasons = it can).</summary>
        /// <remarks>
        /// Pre: creature.InstanceId &gt;= 1; materials != null; venue is a defined EvolutionVenue; progression != null.
        /// Post: NotInLobby iff venue != Lobby; AlreadyFinal iff requirement.IsFinal; for a non-final requirement
        ///       LevelShortBy = max(0, RequiredLevel - progression.LevelOf(creature.Exp)) and one shortfall per material whose held count is
        ///       below the need (exact element AND tier; higher tiers do not substitute). All blockers are reported
        ///       together, not just the first.
        /// Side effects: none (pure; the inventory is only read).
        /// </remarks>
        public static EvolutionCheck Check(in OwnedCreature creature, in EvolutionRequirement requirement, MaterialInventory materials, EvolutionVenue venue,
            Progression progression)
        {
            Contract.Requires(creature.InstanceId >= 1, "creature must be a constructed OwnedCreature (InstanceId >= 1)");
            Contract.Requires(materials != null, "materials is null");
            Contract.Requires(progression != null, "progression is null");
            Contract.Requires(Enum.IsDefined(typeof(EvolutionVenue), venue), "venue must be a defined EvolutionVenue");

            bool alreadyFinal = requirement.IsFinal;
            int levelShortBy = 0;
            List<MaterialShortfall> shortfalls = null;
            if (!alreadyFinal)
            {
                int level = progression.LevelOf(creature.Exp);
                if (level < requirement.RequiredLevel)
                {
                    levelShortBy = requirement.RequiredLevel - level;
                }

                IReadOnlyList<MaterialStack> needs = requirement.Materials;
                for (int i = 0; i < needs.Count; i++)
                {
                    int have = materials.Count(needs[i].Key);
                    if (have < needs[i].Qty)
                    {
                        shortfalls = shortfalls ?? new List<MaterialShortfall>();
                        shortfalls.Add(new MaterialShortfall(needs[i].Key, have, needs[i].Qty));
                    }
                }
            }

            return new EvolutionCheck(requirement.NextSpeciesId, venue != EvolutionVenue.Lobby, alreadyFinal, levelShortBy, shortfalls);
        }

        /// <summary>Evolves the creature if <see cref="Check"/> passes, consuming the materials.</summary>
        /// <remarks>
        /// Pre: same as <see cref="Check"/>.
        /// Post: when Succeeded, Creature has SpeciesId == requirement.NextSpeciesId (InstanceId, Vessel, Exp
        ///       unchanged) and exactly requirement.Materials were removed from the inventory; otherwise Creature is
        ///       the input and the inventory is untouched (all-or-nothing). Result.Check explains a refusal.
        /// Side effects: mutates <paramref name="materials"/> on success only.
        /// </remarks>
        public static EvolutionResult TryEvolve(in OwnedCreature creature, in EvolutionRequirement requirement, MaterialInventory materials, EvolutionVenue venue,
            Progression progression)
        {
            EvolutionCheck check = Check(creature, requirement, materials, venue, progression);
            if (!check.CanEvolve)
            {
                return new EvolutionResult(check, creature);
            }

            bool consumed = materials.TryConsume(requirement.Materials);
            Contract.Ensures(consumed, "a passed check guarantees the materials can be consumed");
            return new EvolutionResult(check, creature.WithSpecies(requirement.NextSpeciesId));
        }
    }
}
