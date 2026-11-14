using Cosmimera.Core.Contracts;
using Cosmimera.GameData;

namespace Cosmimera.Combat
{
    /// <summary>How a fainted creature is revived (Q-C36).</summary>
    /// <remarks>Pre: none. Post: stable values. Side effects: none.</remarks>
    public enum ReviveSource
    {
        /// <summary>Consumable revive item (wild drop, shop, quest reward).</summary>
        ConsumableItem = 0,
        /// <summary>Settlement NPC healer.</summary>
        NpcHeal = 1,
        /// <summary>Fixed campfire or spring.</summary>
        CampfireOrSpring = 2,
        /// <summary>Ally interaction rescue.</summary>
        AllyRescue = 3,
    }

    /// <summary>Outcome of a revive attempt.</summary>
    /// <remarks>Pre: none. Post: stable values. Side effects: none.</remarks>
    public enum ReviveResult
    {
        /// <summary>Fainted creature revived with restored HP.</summary>
        Ok = 0,
        /// <summary>Target unit is not fainted.</summary>
        NotFainted = 1,
        /// <summary>Combat revive cooldown (60 s) has not elapsed.</summary>
        OnCooldown = 2,
        /// <summary>Unit cannot be revived (unknown id or not player side).</summary>
        InvalidUnit = 3,
    }

    /// <summary>
    /// Faint recovery percentages and combat cooldown (Q-C36): Asteroid/Moon (dwarf tier) 30%, Planet 60%, Supergiant 100%;
    /// halved when used in combat, with a 60 s combat cooldown.
    /// REQ: docs/design/combat.md#召喚與輪替.
    /// </summary>
    /// <remarks>Pre: none. Post: pure helpers. Side effects: none.</remarks>
    public static class ReviveRules
    {
        /// <summary>Shared cooldown when using a faint-recovery item in combat (60 s).</summary>
        /// <remarks>Pre: none. Post: constant 60000. Side effects: none.</remarks>
        public const int CombatCooldownMs = 60_000;

        /// <summary>HP percentage restored for <paramref name="sourceTier"/> and combat state.</summary>
        /// <remarks>
        /// Pre: sourceTier is a defined PlanetSize (Asteroid, Moon, Planet, Supergiant).
        /// Post: 30/30/60/100 out of combat, 15/15/30/50 in combat.
        /// Side effects: none.
        /// </remarks>
        public static int RecoveryPercent(PlanetSize sourceTier, bool inCombat)
        {
            int full;
            switch (sourceTier)
            {
                case PlanetSize.Asteroid:
                case PlanetSize.Moon:
                    full = 30;
                    break;
                case PlanetSize.Planet:
                    full = 60;
                    break;
                case PlanetSize.Supergiant:
                    full = 100;
                    break;
                default:
                    Contract.Requires(false, "sourceTier must be a defined PlanetSize");
                    return 0;
            }

            return inCombat ? full / 2 : full;
        }

        /// <summary>Actual HP restored for a creature with <paramref name="maxHp"/>.</summary>
        /// <remarks>
        /// Pre: maxHp >= 1; sourceTier is a defined PlanetSize.
        /// Post: 1 &lt;= result &lt;= maxHp.
        /// Side effects: none.
        /// </remarks>
        public static int ComputeReviveHp(int maxHp, PlanetSize sourceTier, bool inCombat)
        {
            Contract.Requires(maxHp >= 1, "maxHp must be >= 1");
            int percent = RecoveryPercent(sourceTier, inCombat);
            int healed = EffectMath.HealAmount(maxHp, percent);
            if (healed < 1)
            {
                return 1;
            }

            return healed > maxHp ? maxHp : healed;
        }
    }
}
