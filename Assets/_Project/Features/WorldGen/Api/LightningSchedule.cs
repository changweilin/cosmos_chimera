using Cosmimera.Core.Contracts;
using Cosmimera.Core.Random;

namespace Cosmimera.WorldGen
{
    /// <summary>
    /// Deterministic lightning-strike schedule: whether a region gets a strike in a one-second slot, and the two rolls
    /// that place it. REQ: docs/design/weather.md#落雷.
    /// </summary>
    /// <remarks>
    /// Pre: none. Post: pure and integer-only (host and clients agree bit for bit). Side effects: none.
    /// The schedule is decided per slot rather than as a list of strike times so that storm strength, which changes
    /// over time, can set each slot's odds without any state.
    /// </remarks>
    public static class LightningSchedule
    {
        /// <summary>Version of the strike schedule; bump whenever the same seed would strike differently.</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const int GeneratorVersion = 1;

        /// <summary>Length of one decision slot.</summary>
        /// <remarks>Pre: none. Post: constant &gt; 0. Side effects: none.</remarks>
        public const int SlotMs = 1000;

        /// <summary>Time from the warning circle appearing to the strike landing.</summary>
        /// <remarks>Pre: none. Post: constant &gt; 0. Side effects: none.</remarks>
        public const int WarningMs = 1500;

        /// <summary>Radius of the warning circle; units inside it when the strike lands are hit.</summary>
        /// <remarks>Pre: none. Post: constant &gt; 0. Side effects: none.</remarks>
        public const float WarningRadiusM = 3f;

        /// <summary>Damage to a unit, in permille of its max HP, before thunder resistance.</summary>
        /// <remarks>Pre: none. Post: constant in (0, 1000]. Side effects: none.</remarks>
        public const int DamagePermilleOfMaxHp = 80;

        /// <summary>Radius around the rolled point inside which the best lightning rod takes the strike.</summary>
        /// <remarks>Pre: none. Post: constant &gt; 0. Side effects: none.</remarks>
        public const float RodSearchM = 12f;

        /// <summary>A rod with no other object this close counts as isolated (weight ×2).</summary>
        /// <remarks>Pre: none. Post: constant &gt; 0. Side effects: none.</remarks>
        public const float IsolationM = 8f;

        /// <summary>Thunder power used when the strike hits an object.</summary>
        /// <remarks>Pre: none. Post: constant within DamageModel's power range. Side effects: none.</remarks>
        public const int ObjectPower = 120;

        /// <summary>Chance (permille) that a slot holds a strike.</summary>
        /// <remarks>
        /// Pre: thunderPermille, ionPermille in [0, 1000] (effective strengths, weather.md#生效).
        /// Post: 0 when both are 0; otherwise SlotMs / mean interval summed over the two sources, capped at 1000;
        ///       thunderstorm mean 20 s → 6 s, ion storm 12 s → 4 s as strength rises.
        /// Side effects: none.
        /// </remarks>
        public static int ChancePermille(int thunderPermille, int ionPermille)
        {
            Contract.Requires(thunderPermille >= 0 && thunderPermille <= 1000, "thunderPermille must be in [0, 1000]");
            Contract.Requires(ionPermille >= 0 && ionPermille <= 1000, "ionPermille must be in [0, 1000]");
            int chance = 0;
            if (thunderPermille > 0) chance += SlotMs * 1000 / (20_000 - 14 * thunderPermille);
            if (ionPermille > 0) chance += SlotMs * 1000 / (12_000 - 8 * ionPermille);
            return chance > 1000 ? 1000 : chance;
        }

        /// <summary>Whether <paramref name="slot"/> holds a strike in <paramref name="region"/>, and where to roll it.</summary>
        /// <remarks>
        /// Pre: region &gt;= 0; slot &gt;= 0; strengths as <see cref="ChancePermille"/>.
        /// Post: when true, angle and reach are in [0, 1000) (bearing around the region centre and the square-root
        ///       fraction of the region radius); same inputs give the same answer on every platform.
        /// Side effects: none.
        /// </remarks>
        public static bool TryStrike(ulong seed, int region, long slot, int thunderPermille, int ionPermille,
            out int angle, out int reach)
        {
            Contract.Requires(region >= 0, "region must be >= 0");
            Contract.Requires(slot >= 0, "slot must be >= 0");
            int chance = ChancePermille(thunderPermille, ionPermille);
            angle = 0;
            reach = 0;
            if (chance == 0) return false;
            ulong key = seed ^ ((ulong)(region + 1) * 0x9E3779B97F4A7C15UL) ^ 0x424F4C54UL; // "BOLT"
            var rng = new Pcg32(key, (ulong)slot);
            if (rng.NextInt(0, 1000) >= chance) return false;
            angle = rng.NextInt(0, 1000);
            reach = rng.NextInt(0, 1000);
            return true;
        }
    }
}
