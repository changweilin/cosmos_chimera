using Cosmimera.Core.Contracts;
using Cosmimera.Core.Random;
using Cosmimera.GameData;

namespace Cosmimera.WorldGen
{
    /// <summary>
    /// Decides a planet's boss stage before anything else, and the planet size that follows from it.
    /// REQ: docs/design/planet-world.md#boss-尺寸 (D0-B2, 2026-11-10): juvenile → small, adolescent → medium,
    /// adult → large; stages unlock with player level and are drawn uniformly among the unlocked ones.
    /// </summary>
    /// <remarks>
    /// Own stream Pcg32(seed, 'B') so the draw never shifts RunPlanner's 'R' or TemplatePlanner's 'T' sequences.
    /// The same seed under a different unlock cap may draw a different stage; the star card therefore carries the drawn
    /// stage into RunSetup instead of re-deriving it at launch.
    /// </remarks>
    public static class BossDraw
    {
        /// <summary>Boss stage for <paramref name="seed"/>, uniform over Juvenile..<paramref name="maxStage"/>.</summary>
        /// <remarks>
        /// Pre: maxStage is Juvenile, Adolescent or Adult. Post: Juvenile &lt;= result &lt;= maxStage; same inputs → same
        /// result. Side effects: none.
        /// </remarks>
        public static CreatureStage Stage(ulong seed, CreatureStage maxStage)
        {
            Contract.Requires(maxStage >= CreatureStage.Juvenile && maxStage <= CreatureStage.Adult,
                "maxStage must be juvenile..adult");
            var rng = new Pcg32(seed, 'B');
            return (CreatureStage)rng.NextUInt((uint)maxStage + 1u);
        }

        /// <summary>Planet size implied by the boss stage.</summary>
        /// <remarks>
        /// Pre: stage is Juvenile, Adolescent or Adult. Post: Small / Medium / Large respectively. Side effects: none.
        /// </remarks>
        public static PlanetSize SizeFor(CreatureStage stage)
        {
            Contract.Requires(stage >= CreatureStage.Juvenile && stage <= CreatureStage.Adult, "stage must be juvenile..adult");
            switch (stage)
            {
                case CreatureStage.Juvenile:
                    return PlanetSize.Small;
                case CreatureStage.Adolescent:
                    return PlanetSize.Medium;
                default:
                    return PlanetSize.Large;
            }
        }
    }
}
