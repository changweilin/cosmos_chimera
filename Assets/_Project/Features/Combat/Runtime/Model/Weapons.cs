using Cosmimera.Core.Contracts;
using Cosmimera.GameData;

namespace Cosmimera.Combat
{
    /// <summary>Engagement distance band of a weapon (docs/design/crafting.md#武器數值模型).</summary>
    public enum WeaponRange
    {
        Melee = 0,
        Ranged = 1,
    }

    /// <summary>Yin/Yang polarity of a weapon; 1★/2★ weapons are Balanced and match both.</summary>
    public enum WeaponPolarity
    {
        Balanced = 0,
        Yin = 1,
        Yang = 2,
    }

    /// <summary>Per-hand offensive affixes that only apply when the weapon matches the cast move's distance and polarity.</summary>
    /// <remarks>Pre: constructed with bounded deltas. Post: immutable. Side effects: none.</remarks>
    public readonly struct WeaponAffixes
    {
        public WeaponAffixes(int accuracyDelta, int critRateDelta, int critDamageDelta, int cdPercentDelta)
        {
            AccuracyDelta = accuracyDelta;
            CritRateDelta = critRateDelta;
            CritDamageDelta = critDamageDelta;
            CdPercentDelta = cdPercentDelta;
        }

        public int AccuracyDelta { get; }
        public int CritRateDelta { get; }
        public int CritDamageDelta { get; }
        public int CdPercentDelta { get; }
    }

    /// <summary>One held weapon's base numbers (docs/design/crafting.md#武器數值模型).</summary>
    /// <remarks>Pre: built through the constructor. Post: immutable. Side effects: none.</remarks>
    public readonly struct WeaponProfile
    {
        /// <summary>Creates a melee balanced weapon profile (backward-compatible overload).</summary>
        /// <remarks>
        /// Pre: element defined; 0 &lt;= elementMastery &lt;= DamageModel.MaxMasteryPercent;
        ///      1 &lt;= powerPermille &lt;= DamageModel.MaxWeaponPermille; 100 &lt;= speedPermille &lt;= 10000.
        /// Post: properties equal the arguments; Range == Melee, Polarity == Balanced. Side effects: none.
        /// </remarks>
        public WeaponProfile(Element element, int elementMastery, int powerPermille, int speedPermille)
            : this(element, elementMastery, powerPermille, speedPermille, WeaponRange.Melee, WeaponPolarity.Balanced, default)
        {
        }

        /// <summary>Creates a weapon profile with explicit range band, polarity, and optional offensive affixes.</summary>
        /// <remarks>
        /// Pre: element, range, polarity defined; 0 &lt;= elementMastery &lt;= DamageModel.MaxMasteryPercent;
        ///      1 &lt;= powerPermille &lt;= DamageModel.MaxWeaponPermille; 100 &lt;= speedPermille &lt;= 10000.
        /// Post: properties equal the arguments. Side effects: none.
        /// </remarks>
        public WeaponProfile(Element element, int elementMastery, int powerPermille, int speedPermille,
            WeaponRange range, WeaponPolarity polarity, in WeaponAffixes affixes = default)
        {
            Contract.Requires(System.Enum.IsDefined(typeof(Element), element), "weapon element must be defined");
            Contract.Requires(System.Enum.IsDefined(typeof(WeaponRange), range), "weapon range must be defined");
            Contract.Requires(System.Enum.IsDefined(typeof(WeaponPolarity), polarity), "weapon polarity must be defined");
            Contract.Requires(elementMastery >= 0 && elementMastery <= DamageModel.MaxMasteryPercent, "elementMastery out of range");
            Contract.Requires(powerPermille >= 1 && powerPermille <= DamageModel.MaxWeaponPermille, "powerPermille out of range");
            Contract.Requires(speedPermille >= 100 && speedPermille <= 10000, "speedPermille must be 100..10000");
            Element = element;
            ElementMastery = elementMastery;
            PowerPermille = powerPermille;
            SpeedPermille = speedPermille;
            Range = range;
            Polarity = polarity;
            Affixes = affixes;
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

        /// <summary>Melee or ranged engagement band.</summary>
        /// <remarks>Pre: none. Post: defined. Side effects: none.</remarks>
        public WeaponRange Range { get; }

        /// <summary>Yin, Yang, or Balanced (1★/2★ weapons match both).</summary>
        /// <remarks>Pre: none. Post: defined. Side effects: none.</remarks>
        public WeaponPolarity Polarity { get; }

        /// <summary>Per-hand offensive affixes applied only when the weapon matches the move.</summary>
        /// <remarks>Pre: none. Post: none. Side effects: none.</remarks>
        public WeaponAffixes Affixes { get; }

        /// <summary>
        /// Returns true iff this weapon matches <paramref name="rangeClass"/> and (when <paramref name="dealsDamage"/>)
        /// <paramref name="polarity"/>.
        /// </summary>
        /// <remarks>
        /// REQ: docs/design/crafting.md#武器數值模型 — pure effect moves check distance only.
        /// Pre: rangeClass and polarity defined.
        /// Post: true iff distance matches and (not dealsDamage or Balanced or polarity matches).
        /// Side effects: none.
        /// </remarks>
        public bool MatchesMove(RangeClass rangeClass, bool dealsDamage, Polarity polarity)
        {
            bool rangeMatch = (Range == WeaponRange.Melee) == (rangeClass == RangeClass.Melee);
            if (!rangeMatch) return false;
            if (!dealsDamage || Polarity == WeaponPolarity.Balanced) return true;
            return (Polarity == WeaponPolarity.Yin && polarity == GameData.Polarity.Yin) ||
                   (Polarity == WeaponPolarity.Yang && polarity == GameData.Polarity.Yang);
        }
    }

    /// <summary>Weapons a combatant swings, with grip bonuses folded in; default is unarmed (no change to casts).</summary>
    /// <remarks>
    /// REQ: docs/design/crafting.md#穿戴與數值 — two-handed +35% damage, dual wield alternates hands with +25% speed,
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
