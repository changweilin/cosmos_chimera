using Cosmimera.Core.Contracts;
using Cosmimera.Core.Random;

namespace Cosmimera.Combat
{
    /// <summary>Result of resolving one move against one target.</summary>
    /// <remarks>Presentation keys off this: Miss must show the MISS text / afterimage / sound (combat.md 命中與閃避).</remarks>
    public enum HitOutcome
    {
        /// <summary>Geometry did not touch the target; nothing happens and no roll is made.</summary>
        NoContact = 0,
        /// <summary>Touched a target inside dodge-roll i-frames; no roll is made (player only, Q-C8).</summary>
        Invulnerable = 1,
        /// <summary>Touched, but the evasion roll failed. Needs explicit MISS feedback.</summary>
        Miss = 2,
        /// <summary>Touched and the roll succeeded; apply damage / effect.</summary>
        Hit = 3,
    }

    /// <summary>
    /// Second stage of the two-stage hit test: geometry first, then one roll on the host's combat stream.
    /// REQ: docs/design/combat.md#命中與閃避 (Q-C7).
    /// </summary>
    public static class HitResolver
    {
        /// <summary>Resolves one move-target pair.</summary>
        /// <remarks>
        /// Pre: rng was built from a seed/stream (see Pcg32) and is the host's combat stream, separate from generation;
        ///      1 &lt;= hitChancePermille &lt;= 1000 (normally <see cref="DerivedStats.HitChancePermille"/>, whose floor is >= 1).
        /// Post: NoContact iff !geometricContact; Invulnerable iff contact and invulnerable; otherwise Hit with
        ///       probability hitChancePermille / 1000 (exact: one uniform draw in [0, 1000)), else Miss;
        ///       hitChancePermille == 1000 always Hits.
        /// Side effects: advances rng by exactly one bounded draw iff the result is Hit or Miss. Rolls are consumed
        ///       only on contact so every peer replaying the same contacts in the same order stays in lockstep.
        /// </remarks>
        public static HitOutcome Resolve(bool geometricContact, bool invulnerable, int hitChancePermille, ref Pcg32 rng)
        {
            Contract.Requires(hitChancePermille >= 1 && hitChancePermille <= DerivedStats.Permille,
                "hitChancePermille must be 1..1000");
            if (!geometricContact)
            {
                return HitOutcome.NoContact;
            }

            if (invulnerable)
            {
                return HitOutcome.Invulnerable;
            }

            return rng.NextUInt(DerivedStats.Permille) < (uint)hitChancePermille ? HitOutcome.Hit : HitOutcome.Miss;
        }
    }
}
