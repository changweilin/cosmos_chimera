using System;
using Cosmimera.Core.Contracts;

namespace Cosmimera.Food
{
    /// <summary>Why a processing attempt was accepted or refused.</summary>
    /// <remarks>Pre: none. Post: stable values. Side effects: none.</remarks>
    public enum ProcessRefusalReason
    {
        None = 0,
        /// <summary>Field processing lacks the life skill for this <see cref="ProcessKind"/>.</summary>
        MissingSkill = 1,
        /// <summary>Inventory holds fewer input units than requested.</summary>
        MissingInput = 2,
    }

    /// <summary>
    /// Pure rules for one-to-one ingredient processing (freeze, grind, press, churn, ferment, smoke).
    /// REQ: docs/design/cuisine.md#3-加工加工技與加工品
    /// </summary>
    /// <remarks>Pre: valid catalog, spec, inventory. Post: deterministic. Side effects: TryProcess mutates inventory.</remarks>
    public static class ProcessingRules
    {
        /// <summary>Checks whether <paramref name="kind"/> is available in <paramref name="context"/>.</summary>
        /// <remarks>
        /// Pre: kind defined.
        /// Post: true at Kitchen; in Field, Freeze needs FreezePrep, Grind/Press/Churn need CulinaryPrep,
        ///       Ferment needs FermentCure, Smoke needs SmokeDry.
        /// Side effects: none.
        /// </remarks>
        public static bool HasSkillForProcess(ProcessKind kind, in CookingContext context)
        {
            Contract.Requires(Enum.IsDefined(typeof(ProcessKind), kind), "kind must be defined");
            if (context.Station == CookingStationKind.Kitchen)
            {
                return true;
            }

            switch (kind)
            {
                case ProcessKind.Freeze: return context.TeamModifiers.CanFreezePrep;
                case ProcessKind.Grind:
                case ProcessKind.Press:
                case ProcessKind.Churn: return context.TeamModifiers.CanCulinaryPrep;
                case ProcessKind.Ferment: return context.TeamModifiers.CanFermentCure;
                case ProcessKind.Smoke: return context.TeamModifiers.CanSmokeDry;
                default: return false;
            }
        }

        /// <summary>Evaluates whether <paramref name="quantity"/> units can be processed by <paramref name="spec"/>.</summary>
        /// <remarks>
        /// Pre: spec, inventory non-null; quantity &gt;= 1.
        /// Post: None iff skill available and at least quantity inputs held; skill is checked before stock.
        /// Side effects: none.
        /// </remarks>
        public static ProcessRefusalReason CanProcess(ProcessSpec spec, FoodInventory inventory, in CookingContext context, int quantity = 1)
        {
            Contract.Requires(spec != null, "spec must not be null");
            Contract.Requires(inventory != null, "inventory must not be null");
            Contract.Requires(quantity >= 1, "quantity must be >= 1");

            if (!HasSkillForProcess(spec.Kind, context))
            {
                return ProcessRefusalReason.MissingSkill;
            }

            return inventory.CountIngredient(spec.InputIngredientId) >= quantity
                ? ProcessRefusalReason.None
                : ProcessRefusalReason.MissingInput;
        }

        /// <summary>Converts <paramref name="quantity"/> input units into the same number of output units.</summary>
        /// <remarks>
        /// Pre: same as <see cref="CanProcess"/>.
        /// Post: on None, input count decreased by quantity and output count increased by quantity; otherwise inventory unchanged.
        /// Side effects: mutates inventory on success.
        /// </remarks>
        public static ProcessRefusalReason TryProcess(ProcessSpec spec, FoodInventory inventory, in CookingContext context, int quantity = 1)
        {
            ProcessRefusalReason refusal = CanProcess(spec, inventory, context, quantity);
            if (refusal != ProcessRefusalReason.None)
            {
                return refusal;
            }

            bool consumed = inventory.TryConsumeIngredient(spec.InputIngredientId, quantity);
            Contract.Requires(consumed, "CanProcess passed so consumption must succeed");
            inventory.AddIngredient(spec.OutputIngredientId, quantity);
            return ProcessRefusalReason.None;
        }
    }
}
