using Cosmimera.Core.Random;

namespace Cosmimera.WorldGen
{
    /// <summary>The season a planet is in for a whole run.</summary>
    /// <remarks>Pre: none. Post: stable values (saved runs and peers compare them). Side effects: none.</remarks>
    public enum Season
    {
        /// <summary>春：新綠與開花。</summary>
        Spring = 0,

        /// <summary>夏：深綠。 The look every palette was authored in.</summary>
        Summer = 1,

        /// <summary>秋：紅葉／黃葉。</summary>
        Autumn = 2,

        /// <summary>冬：落葉枯枝、積雪覆蓋樹冠與岩石。</summary>
        Winter = 3,
    }

    /// <summary>
    /// Draws a planet's season. REQ: docs/design/planet-world.md#季節 (Q-W16: 每顆星球由 seed 抽一個季節，整場不變).
    /// </summary>
    /// <remarks>Pre: none. Post: pure and deterministic. Side effects: none.</remarks>
    public static class Seasons
    {
        /// <summary>Number of <see cref="Season"/> values.</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const int Count = 4;

        /// <summary>The season of the planet generated from <paramref name="seed"/>.</summary>
        /// <remarks>
        /// Pre: none.
        /// Post: a defined <see cref="Season"/>; the same seed gives the same season on every client; the four seasons
        ///       are equally likely over seeds (機率暫定, Q-W19).
        /// Side effects: none.
        /// </remarks>
        public static Season For(ulong seed)
        {
            // Own stream (Y for year): drawing the season never shifts any other generator's sequence.
            var rng = new Pcg32(seed, 'Y');
            return (Season)rng.NextUInt(Count);
        }
    }
}
