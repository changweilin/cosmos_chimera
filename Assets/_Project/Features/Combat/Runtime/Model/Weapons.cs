using Cosmimera.Core.Contracts;
using Cosmimera.GameData;

namespace Cosmimera.Combat
{
    /// <summary>One held weapon's base numbers (docs/design/crafting.md#武器數值模型).</summary>
    /// <remarks>Pre: built through the constructor. Post: immutable. Side effects: none.</remarks>
    public readonly struct WeaponProfile
    {
        /// <summary>Creates a weapon profile.</summary>
        /// <remarks>
        /// Pre: element defined; 0 &lt;= elementMastery &lt;= DamageModel.MaxMasteryPercent;
        ///      1 &lt;= powerPermille &lt;= DamageModel.MaxWeaponPermille; 100 &lt;= speedPermille &lt;= 10000.
        /// Post: properties equal the arguments. Side effects: none.
        /// </remarks>
        public WeaponProfile(Element element, int elementMastery, int powerPermille, int speedPermille)
        {
            Contract.Requires(System.Enum.IsDefined(typeof(Element), element), "weapon element must be defined");
            Contract.Requires(elementMastery >= 0 && elementMastery <= DamageModel.MaxMasteryPercent, "elementMastery out of range");
            Contract.Requires(powerPermille >= 1 && powerPermille <= DamageModel.MaxWeaponPermille, "powerPermille out of range");
            Contract.Requires(speedPermille >= 100 && speedPermille <= 10000, "speedPermille must be 100..10000");
            Element = element;
            ElementMastery = elementMastery;
            PowerPermille = powerPermille;
            SpeedPermille = speedPermille;
        }

        /// <summary>Weapon element; its mastery bonus applies only to moves of this element.</summary>
        /// <remarks>Pre: none. Post: defined. Side effects: none.</remarks>
        public Element Element { get; }

        /// <summary>Mastery added to a cast of <see cref="Element"/> while this weapon is the active hand.</summary>
        /// <remarks>Pre: none. Post: 0..MaxMasteryPercent. Side effects: none.</remarks>
        public int ElementMastery { get; }

        /// <summary>Base attack multiplier on move power, permille (1000 = ×1).</summary>
        /// <remarks>Pre: none. Post: 1..MaxWeaponPermille. Side effects: none.</remarks>
        public int PowerPermille { get; }

        /// <summary>Base attack speed, permille; every cooldown is divided by it.</summary>
        /// <remarks>Pre: none. Post: 100..10000. Side effects: none.</remarks>
        public int SpeedPermille { get; }
    }

    /// <summary>Weapons a combatant swings, with grip bonuses folded in; default is unarmed (no change to casts).</summary>
    /// <remarks>
    /// REQ: docs/design/crafting.md#穿戴與數值 — two-handed +30% damage, dual wield alternates hands with +15% speed,
    /// a shield raises the block value. Grip bonuses arrive as data (Data/balance.json#equipment).
    /// Pre: built through the constructor or default. Post: immutable. Side effects: none.
    /// </remarks>
    public readonly struct WeaponLoadout
    {
        /// <summary>Creates a loadout of zero, one or two alternating hands.</summary>
        /// <remarks>
        /// Pre: handCount 0..2; second hand only when handCount == 2; 0 &lt;= powerBonusPercent &lt;= 500;
        ///      0 &lt;= speedBonusPercent &lt;= 500.
        /// Post: properties equal the arguments. Side effects: none.
        /// </remarks>
        public WeaponLoadout(int handCount, in WeaponProfile first, in WeaponProfile second, int powerBonusPercent,
            int speedBonusPercent, bool hasShield)
        {
            Contract.Requires(handCount >= 0 && handCount <= 2, "handCount must be 0..2");
            Contract.Requires(powerBonusPercent >= 0 && powerBonusPercent <= 500, "powerBonusPercent must be 0..500");
            Contract.Requires(speedBonusPercent >= 0 && speedBonusPercent <= 500, "speedBonusPercent must be 0..500");
            HandCount = handCount;
            First = first;
            Second = second;
            PowerBonusPercent = powerBonusPercent;
            SpeedBonusPercent = speedBonusPercent;
            HasShield = hasShield;
        }

        /// <summary>Number of weapons; two means casts alternate first/second.</summary>
        /// <remarks>Pre: none. Post: 0..2. Side effects: none.</remarks>
        public int HandCount { get; }

        /// <summary>First (main-hand) weapon; meaningless when HandCount == 0.</summary>
        /// <remarks>Pre: none. Post: none. Side effects: none.</remarks>
        public WeaponProfile First { get; }

        /// <summary>Second (off-hand) weapon; meaningless unless HandCount == 2.</summary>
        /// <remarks>Pre: none. Post: none. Side effects: none.</remarks>
        public WeaponProfile Second { get; }

        /// <summary>Grip damage bonus applied on top of the weapon multiplier (two-handed).</summary>
        /// <remarks>Pre: none. Post: 0..500. Side effects: none.</remarks>
        public int PowerBonusPercent { get; }

        /// <summary>Grip attack-speed bonus applied on top of the weapon speed (dual wield).</summary>
        /// <remarks>Pre: none. Post: 0..500. Side effects: none.</remarks>
        public int SpeedBonusPercent { get; }

        /// <summary>Whether a shield is held, raising block to block.shield_permille.</summary>
        /// <remarks>Pre: none. Post: none. Side effects: none.</remarks>
        public bool HasShield { get; }

        /// <summary>Hand used by the <paramref name="castIndex"/>-th accepted cast.</summary>
        /// <remarks>Pre: HandCount >= 1; castIndex >= 0. Post: First for even indexes or one hand; Second otherwise. Side effects: none.</remarks>
        public WeaponProfile Hand(long castIndex)
        {
            Contract.Requires(HandCount >= 1 && castIndex >= 0, "needs a weapon and a nonnegative cast index");
            return HandCount == 2 && (castIndex & 1) == 1 ? Second : First;
        }

        /// <summary>Power multiplier of <paramref name="hand"/> with the grip bonus, permille.</summary>
        /// <remarks>Pre: hand constructed. Post: 1..DamageModel.MaxWeaponPermille. Side effects: none.</remarks>
        public int PowerPermille(in WeaponProfile hand)
        {
            long p = ((long)hand.PowerPermille * (100 + PowerBonusPercent) + 50) / 100;
            return p < 1 ? 1 : p > DamageModel.MaxWeaponPermille ? DamageModel.MaxWeaponPermille : (int)p;
        }

        /// <summary>Attack speed of <paramref name="hand"/> with the grip bonus, permille.</summary>
        /// <remarks>Pre: hand constructed. Post: >= 100. Side effects: none.</remarks>
        public int SpeedPermille(in WeaponProfile hand)
        {
            long s = ((long)hand.SpeedPermille * (100 + SpeedBonusPercent) + 50) / 100;
            return s > int.MaxValue ? int.MaxValue : (int)s;
        }
    }
}
