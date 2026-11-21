using System;
using Cosmimera.Core.Contracts;
using Cosmimera.Creatures;
using Cosmimera.GameData;

namespace Cosmimera.Lobby
{
    /// <summary>
    /// Progression unlock and elemental material cost rules for star-sea random planet selection.
    /// REQ: docs/design/lobby-meta.md#地球大廳 — initial access is Asteroid only; larger sizes unlock along the
    /// main story (completed terrain trials), and departing to a random planet spends matching main-element material.
    /// </summary>
    /// <remarks>
    /// Pre: valid PlanetSize / Element / MaterialInventory values.
    /// Post: pure rule evaluation except <see cref="TryPayLaunchCost"/> which atomically deducts from stock.
    /// Side effects: none except <see cref="TryPayLaunchCost"/>.
    /// </remarks>
    public static class StarSelectRules
    {
        /// <summary>Completed terrain trial count required to unlock Moon (Dwarf Planet, R = 640 m).</summary>
        /// <remarks>Pre: none. Post: constant 1. Side effects: none.</remarks>
        public const int MoonUnlockTrials = 1;

        /// <summary>Completed terrain trial count required to unlock Planet (R = 960 m).</summary>
        /// <remarks>Pre: none. Post: constant 3. Side effects: none.</remarks>
        public const int PlanetUnlockTrials = 3;

        /// <summary>Completed terrain trial count required to unlock Supergiant (R = 1280 m).</summary>
        /// <remarks>Pre: none. Post: constant 6. Side effects: none.</remarks>
        public const int SupergiantUnlockTrials = 6;

        /// <summary>Largest random planet size currently unlocked by main-story progression.</summary>
        /// <remarks>
        /// Pre: completedTrials &gt;= 0.
        /// Post: Asteroid initially (or when tutorialDone is false); Moon at &gt;= 1 trial; Planet at &gt;= 3 trials;
        ///       Supergiant at &gt;= 6 trials.
        /// Side effects: none.
        /// </remarks>
        public static PlanetSize MaxUnlockedSize(bool tutorialDone, int completedTrials)
        {
            Contract.Requires(completedTrials >= 0, "completedTrials must be >= 0");
            if (!tutorialDone || completedTrials < MoonUnlockTrials)
            {
                return PlanetSize.Asteroid;
            }

            if (completedTrials < PlanetUnlockTrials)
            {
                return PlanetSize.Moon;
            }

            if (completedTrials < SupergiantUnlockTrials)
            {
                return PlanetSize.Planet;
            }

            return PlanetSize.Supergiant;
        }

        /// <summary>Highest boss stage corresponding to <paramref name="maxUnlockedSize"/> for star-card drawing.</summary>
        /// <remarks>
        /// Pre: maxUnlockedSize is a defined PlanetSize.
        /// Post: Asteroid → Juvenile, Moon → Adolescent, Planet/Supergiant → Adult (Overload boss awaits P2-05).
        /// Side effects: none.
        /// </remarks>
        public static CreatureStage MaxBossStageForSize(PlanetSize maxUnlockedSize)
        {
            switch (maxUnlockedSize)
            {
                case PlanetSize.Asteroid:
                    return CreatureStage.Juvenile;
                case PlanetSize.Moon:
                    return CreatureStage.Adolescent;
                case PlanetSize.Planet:
                case PlanetSize.Supergiant:
                    return CreatureStage.Adult;
                default:
                    throw new ArgumentOutOfRangeException(nameof(maxUnlockedSize), maxUnlockedSize, "undefined PlanetSize");
            }
        }

        /// <summary>Elemental material stack required to launch a random planet of <paramref name="size"/> and <paramref name="mainElement"/>.</summary>
        /// <remarks>
        /// Pre: size and mainElement are defined enum values.
        /// Post: tier-1 material of mainElement; Qty is 1 (Asteroid), 2 (Moon), 4 (Planet), or 6 (Supergiant).
        /// Side effects: none.
        /// </remarks>
        public static MaterialStack LaunchCost(PlanetSize size, Element mainElement)
        {
            Contract.Requires(Enum.IsDefined(typeof(Element), mainElement), "mainElement must be a defined Element");
            int qty;
            switch (size)
            {
                case PlanetSize.Asteroid:
                    qty = 1;
                    break;
                case PlanetSize.Moon:
                    qty = 2;
                    break;
                case PlanetSize.Planet:
                    qty = 4;
                    break;
                case PlanetSize.Supergiant:
                    qty = 6;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(size), size, "undefined PlanetSize");
            }

            return new MaterialStack(new MaterialKey(mainElement, 1), qty);
        }

        /// <summary>True when <paramref name="stock"/> holds enough elemental material to launch.</summary>
        /// <remarks>
        /// Pre: stock != null; size and mainElement defined.
        /// Post: result == (stock.Count(LaunchCost(size, mainElement).Key) &gt;= LaunchCost(size, mainElement).Qty).
        /// Side effects: none.
        /// </remarks>
        public static bool CanLaunch(MaterialInventory stock, PlanetSize size, Element mainElement)
        {
            Contract.Requires(stock != null, "stock must not be null");
            MaterialStack cost = LaunchCost(size, mainElement);
            return stock.Count(cost.Key) >= cost.Qty;
        }

        /// <summary>Atomically consumes the launch material cost from <paramref name="stock"/>.</summary>
        /// <remarks>
        /// Pre: stock != null; size and mainElement defined.
        /// Post: returns true and deducts LaunchCost(size, mainElement) iff CanLaunch was true; returns false leaving stock untouched.
        /// Side effects: mutates stock on true.
        /// </remarks>
        public static bool TryPayLaunchCost(MaterialInventory stock, PlanetSize size, Element mainElement)
        {
            Contract.Requires(stock != null, "stock must not be null");
            MaterialStack cost = LaunchCost(size, mainElement);
            return stock.TryConsume(new[] { cost });
        }
    }
}
