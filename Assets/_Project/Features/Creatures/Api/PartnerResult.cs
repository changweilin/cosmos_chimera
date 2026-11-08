namespace Cosmimera.Creatures
{
    /// <summary>Why a sub-partner command was accepted or refused; refusals are player-facing reasons for the lobby UI.</summary>
    /// <remarks>
    /// Player input can legally ask for any of these, so they are results, not contract violations.
    /// Pre: none. Post: stable values (UI strings key off them; never renumber). Side effects: none.
    /// </remarks>
    public enum PartnerResult
    {
        /// <summary>Accepted and applied.</summary>
        Ok = 0,

        /// <summary>The main is not one of this loadout's mains.</summary>
        UnknownMain = 1,

        /// <summary>The would-be sub is a main (a creature cannot be both; Q-C10).</summary>
        SubIsAMain = 2,

        /// <summary>The sub already serves a different main (one creature, one main at a time; Q-C10).</summary>
        AlreadySubOfAnotherMain = 3,

        /// <summary>The sub already serves this main.</summary>
        AlreadyAssignedToThisMain = 4,

        /// <summary>The main already has the maximum number of sub-partners.</summary>
        MainFull = 5,

        /// <summary>The relation table has no edge between the two forms.</summary>
        NoRelation = 6,

        /// <summary>(Unassign) the creature is not a sub-partner of anyone.</summary>
        NotAssigned = 7,

        /// <summary>The sub is at a later evolution stage than the main (sub stage must be &lt;= main stage, for every vessel kind).</summary>
        SubStageAboveMain = 8,
    }

    /// <summary>One main/sub assignment, identified by instance ids.</summary>
    /// <remarks>Pre: none. Post: immutable; both ids &gt;= 1 when produced by <see cref="PartnerLoadout"/>. Side effects: none.</remarks>
    public readonly struct PartnerPair
    {
        /// <summary>Creates a pair.</summary>
        /// <remarks>Pre: none. Post: properties equal the arguments. Side effects: none.</remarks>
        public PartnerPair(int mainInstanceId, int subInstanceId)
        {
            MainInstanceId = mainInstanceId;
            SubInstanceId = subInstanceId;
        }

        /// <summary>Instance id of the main.</summary>
        /// <remarks>Pre: none. Post: none. Side effects: none.</remarks>
        public int MainInstanceId { get; }

        /// <summary>Instance id of the sub-partner.</summary>
        /// <remarks>Pre: none. Post: none. Side effects: none.</remarks>
        public int SubInstanceId { get; }
    }
}
