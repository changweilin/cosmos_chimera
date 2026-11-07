using System.Collections.Generic;
using Cosmimera.Core.Contracts;

namespace Cosmimera.Creatures
{
    /// <summary>One material the player does not have enough of. REQ: docs/design/creatures.md#進化 (說明缺什麼).</summary>
    /// <remarks>
    /// Pre: construct through the public constructor. Post: immutable; Have &lt; Need.
    /// Side effects: none.
    /// </remarks>
    public readonly struct MaterialShortfall
    {
        /// <summary>Creates a shortfall line.</summary>
        /// <remarks>Pre: key.Tier &gt;= 1; 0 &lt;= have &lt; need. Post: properties equal the arguments. Side effects: none.</remarks>
        public MaterialShortfall(MaterialKey key, int have, int need)
        {
            Contract.Requires(key.Tier >= 1, "key must be a constructed MaterialKey");
            Contract.Requires(have >= 0 && have < need, "a shortfall needs 0 <= have < need");
            Key = key;
            Have = have;
            Need = need;
        }

        /// <summary>Which material is short.</summary>
        /// <remarks>Pre: none. Post: none. Side effects: none.</remarks>
        public MaterialKey Key { get; }

        /// <summary>How many the player holds.</summary>
        /// <remarks>Pre: none. Post: &gt;= 0 and &lt; Need. Side effects: none.</remarks>
        public int Have { get; }

        /// <summary>How many the evolution consumes.</summary>
        /// <remarks>Pre: none. Post: &gt; Have. Side effects: none.</remarks>
        public int Need { get; }

        /// <summary>How many more are needed.</summary>
        /// <remarks>Pre: none. Post: &gt;= 1 unless default(MaterialShortfall) (then 0). Side effects: none.</remarks>
        public int Missing => Need - Have;
    }

    /// <summary>
    /// Everything that currently blocks an evolution, all at once, so the UI can say what is missing.
    /// REQ: docs/design/creatures.md#進化 (經驗或素材任一不足時拒絕並說明缺什麼).
    /// </summary>
    /// <remarks>
    /// Pre: produced only by <see cref="EvolutionRules.Check"/>.
    /// Post: immutable snapshot; <see cref="CanEvolve"/> is true iff no blocker is reported.
    /// Side effects: none.
    /// </remarks>
    public sealed class EvolutionCheck
    {
        private static readonly MaterialShortfall[] NoShortfalls = new MaterialShortfall[0];

        private readonly IReadOnlyList<MaterialShortfall> _shortfalls;

        internal EvolutionCheck(string nextSpeciesId, bool notInLobby, bool alreadyFinal, int expShortBy, List<MaterialShortfall> shortfalls)
        {
            NextSpeciesId = nextSpeciesId;
            NotInLobby = notInLobby;
            AlreadyFinal = alreadyFinal;
            ExpShortBy = expShortBy;
            _shortfalls = shortfalls == null ? (IReadOnlyList<MaterialShortfall>)NoShortfalls : shortfalls.AsReadOnly();
        }

        /// <summary>Form the creature would become; null when <see cref="AlreadyFinal"/>.</summary>
        /// <remarks>Pre: none. Post: null iff AlreadyFinal. Side effects: none.</remarks>
        public string NextSpeciesId { get; }

        /// <summary>The attempt is not in the lobby (Q-C9).</summary>
        /// <remarks>Pre: none. Post: none. Side effects: none.</remarks>
        public bool NotInLobby { get; }

        /// <summary>The creature is an adult: there is nothing to evolve into.</summary>
        /// <remarks>Pre: none. Post: when true, ExpShortBy == 0 and Shortfalls is empty (no requirement exists). Side effects: none.</remarks>
        public bool AlreadyFinal { get; }

        /// <summary>How much experience is still missing.</summary>
        /// <remarks>Pre: none. Post: &gt;= 0; 0 means the threshold is reached. Side effects: none.</remarks>
        public int ExpShortBy { get; }

        /// <summary>Every material held in too small a quantity, in requirement order.</summary>
        /// <remarks>Pre: none. Post: never null; read-only; one entry per short material. Side effects: none.</remarks>
        public IReadOnlyList<MaterialShortfall> Shortfalls => _shortfalls;

        /// <summary>True iff nothing blocks the evolution.</summary>
        /// <remarks>Pre: none. Post: !NotInLobby &amp;&amp; !AlreadyFinal &amp;&amp; ExpShortBy == 0 &amp;&amp; Shortfalls is empty. Side effects: none.</remarks>
        public bool CanEvolve => !NotInLobby && !AlreadyFinal && ExpShortBy == 0 && _shortfalls.Count == 0;
    }

    /// <summary>Outcome of <see cref="EvolutionRules.TryEvolve"/>.</summary>
    /// <remarks>
    /// Pre: produced only by <see cref="EvolutionRules.TryEvolve"/>.
    /// Post: immutable; Succeeded == Check.CanEvolve.
    /// Side effects: none.
    /// </remarks>
    public sealed class EvolutionResult
    {
        internal EvolutionResult(EvolutionCheck check, OwnedCreature creature)
        {
            Check = check;
            Creature = creature;
        }

        /// <summary>The check this attempt was decided by (the reasons when it failed).</summary>
        /// <remarks>Pre: none. Post: non-null. Side effects: none.</remarks>
        public EvolutionCheck Check { get; }

        /// <summary>The evolved individual on success; the unchanged input on failure.</summary>
        /// <remarks>Pre: none. Post: same InstanceId, Vessel and Exp as the input; new SpeciesId iff Succeeded. Side effects: none.</remarks>
        public OwnedCreature Creature { get; }

        /// <summary>Whether the creature evolved and the materials were consumed.</summary>
        /// <remarks>Pre: none. Post: equals Check.CanEvolve. Side effects: none.</remarks>
        public bool Succeeded => Check.CanEvolve;
    }
}
