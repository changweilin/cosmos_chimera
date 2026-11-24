using Cosmimera.Core.Contracts;
using Cosmimera.Core.Random;

namespace Cosmimera.WorldGen
{
    /// <summary>
    /// Deterministic earthquake-and-eruption schedule: when each region's quake starts, how long it erupts, where its
    /// cracks open and where its volcanic bombs fall. REQ: docs/design/planet-world.md#地震噴發.
    /// </summary>
    /// <remarks>
    /// Pre: none. Post: pure and integer-only (host and clients agree bit for bit). Side effects: none.
    /// Time is cut into fixed windows with one quake each, placed at the window's middle ± a jitter, so any moment's
    /// quake is found in O(1) without state and consecutive quakes are WindowMs ± 2·JitterMs apart.
    /// </remarks>
    public static class QuakeSchedule
    {
        /// <summary>Version of the schedule; bump whenever the same seed would quake differently.</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const int GeneratorVersion = 1;

        /// <summary>Foreshock length: the ground shakes this long before the cracks open.</summary>
        /// <remarks>Pre: none. Post: constant &gt; 0. Side effects: none.</remarks>
        public const int ForeshockMs = 3000;

        /// <summary>Time for the lava left beside the cracks to cool into black rock after the eruption ends.</summary>
        /// <remarks>Pre: none. Post: constant &gt; 0. Side effects: none.</remarks>
        public const int LavaCoolMs = 60_000;

        /// <summary>Length of one eruption decision slot (bombs) and one lava damage step.</summary>
        /// <remarks>Pre: none. Post: constant &gt; 0. Side effects: none.</remarks>
        public const int SlotMs = 1000;

        /// <summary>Time from a bomb's warning circle appearing to its landing.</summary>
        /// <remarks>Pre: none. Post: constant &gt; 0. Side effects: none.</remarks>
        public const int BombWarningMs = 1500;

        /// <summary>Radius around a bomb's landing point that is hurt and set alight.</summary>
        /// <remarks>Pre: none. Post: constant &gt; 0. Side effects: none.</remarks>
        public const float BombRadiusM = 2.5f;

        /// <summary>Half width of the opening crack: anything this close when it opens is thrown and hurt.</summary>
        /// <remarks>Pre: none. Post: constant &gt; 0. Side effects: none.</remarks>
        public const float CrackHalfWidthM = 1.5f;

        /// <summary>Half width of the lava left beside a crack.</summary>
        /// <remarks>Pre: none. Post: constant &gt; CrackHalfWidthM. Side effects: none.</remarks>
        public const float LavaHalfWidthM = 3f;

        /// <summary>Damage (permille of max HP) to whoever stands on a crack as it opens; Earth element.</summary>
        /// <remarks>Pre: none. Post: constant in (0, 1000]. Side effects: none.</remarks>
        public const int CrackPermille = 120;

        /// <summary>Damage (permille of max HP) from a bomb landing; Fire element.</summary>
        /// <remarks>Pre: none. Post: constant in (0, 1000]. Side effects: none.</remarks>
        public const int BombPermille = 100;

        /// <summary>Damage (permille of max HP) per second standing in lava; Fire element (fire-immune units take none).</summary>
        /// <remarks>Pre: none. Post: constant in (0, 1000]. Side effects: none.</remarks>
        public const int LavaPermillePerS = 30;

        /// <summary>Most cracks one quake opens.</summary>
        /// <remarks>Pre: none. Post: constant ≥ 2. Side effects: none.</remarks>
        public const int MaxCracks = 4;

        /// <summary>Crack placements tried per quake; a placement off volcanic ground or out of the region is dropped.</summary>
        /// <remarks>Pre: none. Post: constant ≥ MaxCracks. Side effects: none.</remarks>
        public const int CrackAttempts = 10;

        /// <summary>Bombs one crack may throw in one slot.</summary>
        /// <remarks>Pre: none. Post: constant ≥ 1. Side effects: none.</remarks>
        public const int BombsPerCrackSlot = 2;

        /// <summary>Window length: one quake per window per region.</summary>
        /// <remarks>
        /// Pre: none. Post: 240 s on a lava planet (quakes 3–5 min apart), 480 s on volcanic ground elsewhere (6–10 min).
        /// Side effects: none.
        /// </remarks>
        public static long WindowMs(bool lavaPlanet) => lavaPlanet ? 240_000L : 480_000L;

        /// <summary>Largest shift of a quake from its window's middle.</summary>
        /// <remarks>Pre: none. Post: a quarter of WindowMs, so quakes stay WindowMs ± 2·JitterMs apart. Side effects: none.</remarks>
        public static long JitterMs(bool lavaPlanet) => WindowMs(lavaPlanet) / 8;

        /// <summary>When the quake of <paramref name="window"/> starts, how long it erupts, and how many cracks it wants.</summary>
        /// <remarks>
        /// Pre: region ≥ 0; window ≥ 0.
        /// Post: startMs in window·W + W/2 ± JitterMs (≥ 0); eruptMs in [6000, 10000]; cracks in [2, MaxCracks].
        /// Side effects: none.
        /// </remarks>
        public static void Timing(ulong seed, int region, long window, bool lavaPlanet, out long startMs, out int eruptMs, out int cracks)
        {
            Contract.Requires(region >= 0 && window >= 0, "region and window must be >= 0");
            var rng = new Pcg32(Key(seed, region, window), 0UL);
            long w = WindowMs(lavaPlanet);
            long j = JitterMs(lavaPlanet);
            startMs = window * w + w / 2 + rng.NextInt(-(int)j, (int)j + 1);
            eruptMs = rng.NextInt(6000, 10_001);
            cracks = rng.NextInt(2, MaxCracks + 1);
        }

        /// <summary>One crack placement try: where around the region centre, which way it runs, how long.</summary>
        /// <remarks>
        /// Pre: region ≥ 0; window ≥ 0; attempt in [0, CrackAttempts).
        /// Post: angle and reach in [0, 1000) (bearing, square-root fraction of the region radius); headingTenths in
        ///       [0, 1800) (a crack has no direction); lengthM in [15, 40].
        /// Side effects: none.
        /// </remarks>
        public static void Crack(ulong seed, int region, long window, int attempt, out int angle, out int reach,
            out int headingTenths, out int lengthM)
        {
            Contract.Requires(region >= 0 && window >= 0, "region and window must be >= 0");
            Contract.Requires(attempt >= 0 && attempt < CrackAttempts, "attempt out of range");
            var rng = new Pcg32(Key(seed, region, window), (ulong)(1 + attempt));
            angle = rng.NextInt(0, 1000);
            reach = rng.NextInt(0, 1000);
            headingTenths = rng.NextInt(0, 1800);
            lengthM = rng.NextInt(15, 41);
        }

        /// <summary>Whether crack <paramref name="crack"/> throws bomb <paramref name="bomb"/> in eruption slot <paramref name="slot"/>, and where it lands.</summary>
        /// <remarks>
        /// Pre: region ≥ 0; window ≥ 0; slot ≥ 0; crack in [0, MaxCracks); bomb in [0, BombsPerCrackSlot).
        /// Post: when true, along in [0, 1000) (fraction along the crack) and sideDm in ±[40, 140] (landing offset
        ///       across it, decimetres). The first bomb of a slot is thrown 70 % of the time, the second 35 %.
        /// Side effects: none.
        /// </remarks>
        public static bool TryBomb(ulong seed, int region, long window, long slot, int crack, int bomb, out int along, out int sideDm)
        {
            Contract.Requires(region >= 0 && window >= 0 && slot >= 0, "region, window and slot must be >= 0");
            Contract.Requires(crack >= 0 && crack < MaxCracks && bomb >= 0 && bomb < BombsPerCrackSlot, "crack or bomb out of range");
            var rng = new Pcg32(Key(seed, region, window) ^ 0x424F4D42UL, (ulong)(slot * (MaxCracks * BombsPerCrackSlot) + crack * BombsPerCrackSlot + bomb)); // "BOMB"
            along = 0;
            sideDm = 0;
            if (rng.NextInt(0, 1000) >= (bomb == 0 ? 700 : 350)) return false;
            along = rng.NextInt(0, 1000);
            int side = rng.NextInt(40, 141);
            sideDm = rng.NextInt(0, 2) == 0 ? side : -side;
            return true;
        }

        private static ulong Key(ulong seed, int region, long window) =>
            seed ^ ((ulong)(region + 1) * 0x9E3779B97F4A7C15UL) ^ ((ulong)(window + 1) * 0xC2B2AE3D27D4EB4FUL) ^ 0x5155414BUL; // "QUAK"
    }
}
