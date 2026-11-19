using System;
using System.Collections.Generic;
using Cosmimera.Combat;
using Cosmimera.Core.Contracts;
using Cosmimera.Core.Random;
using Cosmimera.GameData;

namespace Cosmimera.Crafting
{
    /// <summary>Material tiers for D0-U4 (1★/2★/3★; docs/design/crafting.md#製造擴充).</summary>
    public enum CraftMaterialTier
    {
        Tier1 = 1,
        Tier2 = 2,
        Tier3 = 3,
    }

    /// <summary>Eight crafting life-skill categories (docs/design/crafting.md#製造擴充).</summary>
    public enum CraftSkillCategory
    {
        Smelt = 0,
        Weave = 1,
        Tailor = 2,
        Smith = 3,
        Carpentry = 4,
        Jewelcraft = 5,
        Dye = 6,
        Enchant = 7,
    }

    /// <summary>Sixteen weapon types across melee/ranged and one-handed/two-handed (docs/design/crafting.md#武器數值模型).</summary>
    public enum WeaponType
    {
        // Melee 1H
        DaggerClaw = 0,
        SwordScimitar = 1,
        HandAxe = 2,
        Mace = 3,
        // Melee 2H
        TwinbladeStaff = 4,
        SpearHalberd = 5,
        Greatsword = 6,
        GreataxeHammer = 7,
        // Ranged 1H
        ThrowingBlade = 8,
        HandCrossbow = 9,
        WandPistol = 10,
        Javelin = 11,
        // Ranged 2H
        RepeatingCrossbow = 12,
        Longbow = 13,
        Staff = 14,
        HeavyCrossbowCannon = 15,
    }

    /// <summary>Cosmetic armor variants within the same tier and slot (docs/design/crafting.md#製造擴充).</summary>
    public enum ArmorVariant
    {
        RoundShield = 0,
        KiteShield = 1,
        TowerShield = 2,
        WoodenRoundShield = 3,
        RuneShield = 4,
        RectangularWoodShield = 5,
        Chestplate = 6,
        ScaleMail = 7,
        Jacket = 8,
        Robe = 9,
        Greaves = 10,
        PlateSkirt = 11,
        Trousers = 12,
        BattleSkirt = 13,
    }

    /// <summary>Where equipment durability repair is attempted.</summary>
    public enum RepairLocation
    {
        Lobby = 0,
        FieldWithSkill = 1,
        VillageNpc = 2,
    }

    /// <summary>Quality grade inherited from primary crafting material.</summary>
    public enum ItemQuality
    {
        Common = 0,
        Fine = 1,
        Masterwork = 2,
        Mythic = 3,
    }

    /// <summary>Weapon type base multipliers from docs/design/crafting.md#武器數值模型.</summary>
    /// <remarks>Pre: constructed by CraftingExpansionRules. Post: immutable. Side effects: none.</remarks>
    public readonly struct WeaponTypeSpec
    {
        public WeaponTypeSpec(WeaponRange range, bool isTwoHanded, int powerPermille, int speedPermille)
        {
            Range = range;
            IsTwoHanded = isTwoHanded;
            PowerPermille = powerPermille;
            SpeedPermille = speedPermille;
        }

        public WeaponRange Range { get; }
        public bool IsTwoHanded { get; }
        public int PowerPermille { get; }
        public int SpeedPermille { get; }
    }

    /// <summary>Six-stat flat bonus granted by 1★/2★/3★ crafted equipment.</summary>
    /// <remarks>Pre: all values nonnegative. Post: immutable. Side effects: none.</remarks>
    public readonly struct SixStatBonus
    {
        public SixStatBonus(int hp, int speed, int yangAtk, int yinAtk, int yangDef, int yinDef)
        {
            Contract.Requires(hp >= 0 && speed >= 0 && yangAtk >= 0 && yinAtk >= 0 && yangDef >= 0 && yinDef >= 0,
                "equipment six-stat bonuses must be nonnegative");
            Hp = hp;
            Speed = speed;
            YangAtk = yangAtk;
            YinAtk = yinAtk;
            YangDef = yangDef;
            YinDef = yinDef;
        }

        public int Hp { get; }
        public int Speed { get; }
        public int YangAtk { get; }
        public int YinAtk { get; }
        public int YangDef { get; }
        public int YinDef { get; }

        public int Total => Hp + Speed + YangAtk + YinAtk + YangDef + YinDef;
    }

    /// <summary>Reproducible affix rolled from item quality or enchanting.</summary>
    public readonly struct CraftAffix
    {
        public CraftAffix(string statId, int bonusPercent)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(statId), "statId required");
            StatId = statId;
            BonusPercent = bonusPercent;
        }

        public string StatId { get; }
        public int BonusPercent { get; }
    }

    /// <summary>Result of rolling one accessory enchant slot (1 elemental + 100% floor, or 3 distinct resistances).</summary>
    /// <remarks>Pre: constructed by CraftingExpansionRules. Post: immutable. Side effects: none.</remarks>
    public readonly struct AccessoryEnchantResult
    {
        internal AccessoryEnchantResult(bool isElemental, Element? elementFloor100, EffectSpec[] effects)
        {
            IsElemental = isElemental;
            ElementFloor100 = elementFloor100;
            Effects = Array.AsReadOnly(effects);
        }

        public bool IsElemental { get; }
        public Element? ElementFloor100 { get; }
        public IReadOnlyList<EffectSpec> Effects { get; }
    }

    /// <summary>Durability tracker for D0-U4 equipment; broken items keep their slot with zero bonuses.</summary>
    public struct DurabilityState
    {
        public DurabilityState(int max)
        {
            Contract.Requires(max >= 1, "max durability must be >= 1");
            Max = max;
            Current = max;
        }

        public int Current { get; private set; }
        public int Max { get; }
        public bool IsBroken => Current <= 0;

        /// <summary>Reduces durability by <paramref name="amount"/>, clamped at 0.</summary>
        /// <remarks>Pre: amount >= 0. Post: 0 &lt;= Current &lt;= Max. Side effects: mutates Current.</remarks>
        public void Degrade(int amount)
        {
            Contract.Requires(amount >= 0, "amount must be non-negative");
            Current = Math.Max(0, Current - amount);
        }

        /// <summary>Restores durability to Max.</summary>
        /// <remarks>Pre: none. Post: Current == Max. Side effects: mutates Current.</remarks>
        public void Repair() => Current = Max;
    }

    /// <summary>Per-item mutable instance state for durability, enchant slots (up to 2), and dyed fashion overlay.</summary>
    /// <remarks>Pre: valid item id and max durability >= 1. Post: bounded state. Side effects: mutates instance.</remarks>
    public sealed class EquipmentInstance
    {
        private readonly List<EffectSpec> _slot0 = new List<EffectSpec>(3);
        private readonly List<EffectSpec> _slot1 = new List<EffectSpec>(3);
        private DurabilityState _durability;

        public EquipmentInstance(string itemId, int maxDurability)
        {
            Contract.Requires(CraftInventory.ValidId(itemId), "valid itemId required");
            Contract.Requires(maxDurability >= 1, "maxDurability must be >= 1");
            ItemId = itemId;
            _durability = new DurabilityState(maxDurability);
        }

        public string ItemId { get; }
        public int CurrentDurability => _durability.Current;
        public int MaxDurability => _durability.Max;
        public bool IsBroken => _durability.IsBroken;
        public Element? FloorElement0 { get; private set; }
        public Element? FloorElement1 { get; private set; }
        public string FashionItemId { get; private set; }
        public string DyeColorId { get; private set; }
        public IReadOnlyList<EffectSpec> EnchantSlot0 => _slot0;
        public IReadOnlyList<EffectSpec> EnchantSlot1 => _slot1;

        /// <summary>Reduces durability on hit, block, or attack.</summary>
        /// <remarks>Pre: amount >= 0. Post: 0 &lt;= CurrentDurability &lt;= MaxDurability. Side effects: mutates durability.</remarks>
        public void Degrade(int amount) => _durability.Degrade(amount);

        /// <summary>Restores full durability.</summary>
        /// <remarks>Pre: none. Post: CurrentDurability == MaxDurability. Side effects: mutates durability.</remarks>
        public void Repair() => _durability.Repair();

        /// <summary>Overwrites enchant slot <paramref name="slotIndex"/> (0 or 1) with new affixes.</summary>
        /// <remarks>Pre: slotIndex is 0 or 1; effects non-null. Post: slot holds a copy of effects. Side effects: mutates slot.</remarks>
        public void ApplyEnchant(int slotIndex, IReadOnlyList<EffectSpec> effects, Element? floorElement = null)
        {
            Contract.Requires(slotIndex == 0 || slotIndex == 1, "slotIndex must be 0 or 1");
            Contract.Requires(effects != null, "effects required");
            List<EffectSpec> target = slotIndex == 0 ? _slot0 : _slot1;
            target.Clear();
            target.AddRange(effects);
            if (slotIndex == 0) FloorElement0 = floorElement;
            else FloorElement1 = floorElement;
        }

        /// <summary>Applies or clears a cosmetic fashion overlay on this armor piece.</summary>
        /// <remarks>Pre: fashionItemId null or valid id. Post: FashionItemId updated; DyeColorId cleared if unequipped. Side effects: mutates fashion.</remarks>
        public void SetFashion(string fashionItemId)
        {
            Contract.Requires(fashionItemId == null || CraftInventory.ValidId(fashionItemId), "invalid fashionItemId");
            FashionItemId = fashionItemId;
            if (fashionItemId == null) DyeColorId = null;
        }

        /// <summary>Dyes the equipped fashion item.</summary>
        /// <remarks>Pre: FashionItemId != null; dyeColorId valid id. Post: DyeColorId set. Side effects: mutates dye.</remarks>
        public void ApplyDye(string dyeColorId)
        {
            Contract.Requires(FashionItemId != null, "fashion item must be equipped before dyeing");
            Contract.Requires(CraftInventory.ValidId(dyeColorId), "invalid dyeColorId");
            DyeColorId = dyeColorId;
        }
    }

    /// <summary>Pure D0-U4 rules for crafting life-skills, material tiers, weapon types, enchanting, and repair.</summary>
    public static class CraftingExpansionRules
    {
        /// <summary>Unified raw-to-processed conversion ratio (10 raw -> 1 processed).</summary>
        public const int ProcessingInputRatio = 10;

        private static readonly string[] AffixPool = { "str", "agi", "int", "spi", "vit", "crit_percent" };

        /// <summary>Maps a crafting category to its interim FieldUtility before P5 species roll out.</summary>
        /// <remarks>Pre: category defined. Post: valid FieldUtility. Side effects: none.</remarks>
        public static FieldUtility FallbackUtility(CraftSkillCategory category) => category switch
        {
            CraftSkillCategory.Smelt => FieldUtility.CookFire,
            CraftSkillCategory.Weave => FieldUtility.ForageBerries,
            CraftSkillCategory.Tailor => FieldUtility.ForageBerries,
            CraftSkillCategory.Carpentry => FieldUtility.ForageBerries,
            CraftSkillCategory.Dye => FieldUtility.ForageBerries,
            CraftSkillCategory.Smith => FieldUtility.MagnetPickupDouble,
            CraftSkillCategory.Jewelcraft => FieldUtility.ScoutPotential,
            CraftSkillCategory.Enchant => FieldUtility.ScoutPotential,
            _ => FieldUtility.CookFire,
        };

        /// <summary>Extracts the crafting category of a <c>craft_*</c> FieldUtility.</summary>
        /// <remarks>Pre: none. Post: true iff utility is one of the 24 craft_* skills. Side effects: none.</remarks>
        public static bool TryGetCategory(FieldUtility utility, out CraftSkillCategory category)
        {
            switch (utility)
            {
                case FieldUtility.CraftSmeltFire:
                case FieldUtility.CraftSmeltMetal:
                case FieldUtility.CraftSmeltThunder:
                    category = CraftSkillCategory.Smelt; return true;
                case FieldUtility.CraftWeaveWood:
                case FieldUtility.CraftWeaveWind:
                case FieldUtility.CraftWeaveShadow:
                    category = CraftSkillCategory.Weave; return true;
                case FieldUtility.CraftTailorWood:
                case FieldUtility.CraftTailorShadow:
                case FieldUtility.CraftTailorLight:
                    category = CraftSkillCategory.Tailor; return true;
                case FieldUtility.CraftSmithMetal:
                case FieldUtility.CraftSmithFire:
                case FieldUtility.CraftSmithEarth:
                    category = CraftSkillCategory.Smith; return true;
                case FieldUtility.CraftCarpentryWood:
                case FieldUtility.CraftCarpentryEarth:
                case FieldUtility.CraftCarpentryWind:
                    category = CraftSkillCategory.Carpentry; return true;
                case FieldUtility.CraftJewelcraftLight:
                case FieldUtility.CraftJewelcraftEarth:
                case FieldUtility.CraftJewelcraftIce:
                    category = CraftSkillCategory.Jewelcraft; return true;
                case FieldUtility.CraftDyeWood:
                case FieldUtility.CraftDyeWater:
                case FieldUtility.CraftDyeLight:
                    category = CraftSkillCategory.Dye; return true;
                case FieldUtility.CraftEnchantLight:
                case FieldUtility.CraftEnchantFire:
                case FieldUtility.CraftEnchantShadow:
                    category = CraftSkillCategory.Enchant; return true;
                default:
                    category = default; return false;
            }
        }

        /// <summary>
        /// Checks whether <paramref name="utilities"/> satisfies <paramref name="required"/>, allowing any
        /// same-category <c>craft_*</c> skill, its P5-interim fallback, or GatherEssence fallbacks.
        /// </summary>
        /// <remarks>Pre: utilities non-null. Post: bool. Side effects: none.</remarks>
        public static bool SatisfiesSkill(IReadOnlyCollection<FieldUtility> utilities, FieldUtility required)
        {
            Contract.Requires(utilities != null, "utilities required");
            if (required == FieldUtility.GatherEssence)
            {
                foreach (FieldUtility u in utilities)
                    if (u == FieldUtility.GatherEssence || u == FieldUtility.DropRateUp || u == FieldUtility.ScoutPotential)
                        return true;
                return false;
            }

            if (TryGetCategory(required, out CraftSkillCategory requiredCategory))
            {
                FieldUtility fallback = FallbackUtility(requiredCategory);
                foreach (FieldUtility u in utilities)
                {
                    if (u == fallback) return true;
                    if (TryGetCategory(u, out CraftSkillCategory heldCategory) && heldCategory == requiredCategory)
                        return true;
                }
                return false;
            }

            foreach (FieldUtility u in utilities)
                if (u == required) return true;
            return false;
        }

        /// <summary>Deterministically rolls the material tier yielded by a gathering node (16:4:1 economy).</summary>
        /// <remarks>
        /// REQ: docs/design/crafting.md#製造擴充 — T1: 100% T1; T2: 80% T1 / 20% T2; T3: 16/21 T1 / 4/21 T2 / 1/21 T3.
        /// Pre: nodeTier defined. Post: 1..nodeTier. Side effects: none.
        /// </remarks>
        public static CraftMaterialTier RollNodeMaterialTier(CraftMaterialTier nodeTier, ulong seed, long nodeId, int rollIndex = 0)
        {
            Contract.Requires(Enum.IsDefined(typeof(CraftMaterialTier), nodeTier), "invalid nodeTier");
            if (nodeTier == CraftMaterialTier.Tier1) return CraftMaterialTier.Tier1;
            var rng = new Pcg32(seed ^ (ulong)nodeId ^ ((ulong)(uint)rollIndex * 0x9E3779B97F4A7C15UL), 0);
            if (nodeTier == CraftMaterialTier.Tier2)
                return rng.NextUInt(100) < 80 ? CraftMaterialTier.Tier1 : CraftMaterialTier.Tier2;

            uint roll = rng.NextUInt(21);
            if (roll < 16) return CraftMaterialTier.Tier1;
            return roll < 20 ? CraftMaterialTier.Tier2 : CraftMaterialTier.Tier3;
        }

        /// <summary>Returns processed material counts (T1, T2, T3) required to craft an equipment item.</summary>
        /// <remarks>Pre: tier defined. Post: (4,0,0), (16,4,0), or (64,16,4), doubled when isTwoHanded. Side effects: none.</remarks>
        public static (int Tier1, int Tier2, int Tier3) GetEquipmentRecipeCosts(CraftMaterialTier tier, bool isTwoHanded)
        {
            Contract.Requires(Enum.IsDefined(typeof(CraftMaterialTier), tier), "invalid tier");
            int mult = isTwoHanded ? 2 : 1;
            return tier switch
            {
                CraftMaterialTier.Tier1 => (4 * mult, 0, 0),
                CraftMaterialTier.Tier2 => (16 * mult, 4 * mult, 0),
                CraftMaterialTier.Tier3 => (64 * mult, 16 * mult, 4 * mult),
                _ => (4 * mult, 0, 0),
            };
        }

        /// <summary>Returns the base power/speed multipliers and hand/range flags for a <see cref="WeaponType"/>.</summary>
        /// <remarks>Pre: type defined. Post: valid WeaponTypeSpec. Side effects: none.</remarks>
        public static WeaponTypeSpec GetWeaponTypeSpec(WeaponType type)
        {
            Contract.Requires(Enum.IsDefined(typeof(WeaponType), type), "undefined weaponType");
            return type switch
            {
                WeaponType.DaggerClaw => new WeaponTypeSpec(WeaponRange.Melee, false, 820, 1200),
                WeaponType.SwordScimitar => new WeaponTypeSpec(WeaponRange.Melee, false, 1000, 1000),
                WeaponType.HandAxe => new WeaponTypeSpec(WeaponRange.Melee, false, 1130, 900),
                WeaponType.Mace => new WeaponTypeSpec(WeaponRange.Melee, false, 1300, 800),
                WeaponType.TwinbladeStaff => new WeaponTypeSpec(WeaponRange.Melee, true, 860, 1150),
                WeaponType.SpearHalberd => new WeaponTypeSpec(WeaponRange.Melee, true, 1000, 1000),
                WeaponType.Greatsword => new WeaponTypeSpec(WeaponRange.Melee, true, 1200, 850),
                WeaponType.GreataxeHammer => new WeaponTypeSpec(WeaponRange.Melee, true, 1400, 750),
                WeaponType.ThrowingBlade => new WeaponTypeSpec(WeaponRange.Ranged, false, 790, 1250),
                WeaponType.HandCrossbow => new WeaponTypeSpec(WeaponRange.Ranged, false, 1000, 1000),
                WeaponType.WandPistol => new WeaponTypeSpec(WeaponRange.Ranged, false, 1130, 900),
                WeaponType.Javelin => new WeaponTypeSpec(WeaponRange.Ranged, false, 1260, 820),
                WeaponType.RepeatingCrossbow => new WeaponTypeSpec(WeaponRange.Ranged, true, 860, 1150),
                WeaponType.Longbow => new WeaponTypeSpec(WeaponRange.Ranged, true, 1000, 1000),
                WeaponType.Staff => new WeaponTypeSpec(WeaponRange.Ranged, true, 1200, 850),
                WeaponType.HeavyCrossbowCannon => new WeaponTypeSpec(WeaponRange.Ranged, true, 1400, 750),
                _ => new WeaponTypeSpec(WeaponRange.Melee, false, 1000, 1000),
            };
        }

        /// <summary>Computes the six-stat budget for a crafted weapon (15/30/45, ×2 for two-handed).</summary>
        /// <remarks>Pre: tier and polarity defined. Post: Total == 15 * tier * (isTwoHanded ? 2 : 1). Side effects: none.</remarks>
        public static SixStatBonus ComputeWeaponSixStats(CraftMaterialTier tier, bool isTwoHanded, WeaponPolarity polarity)
        {
            Contract.Requires(Enum.IsDefined(typeof(CraftMaterialTier), tier), "invalid tier");
            Contract.Requires(Enum.IsDefined(typeof(WeaponPolarity), polarity), "invalid polarity");
            int budget = 15 * (int)tier * (isTwoHanded ? 2 : 1);
            if (polarity == WeaponPolarity.Balanced)
            {
                int third = budget / 3;
                return new SixStatBonus(0, third, third, budget - 2 * third, 0, 0);
            }

            int speed = budget / 5;
            int primary = budget * 3 / 5;
            int secondary = budget - speed - primary;
            return polarity == WeaponPolarity.Yang
                ? new SixStatBonus(0, speed, primary, secondary, 0, 0)
                : new SixStatBonus(0, speed, secondary, primary, 0, 0);
        }

        /// <summary>Computes the six-stat budget for a crafted armor piece (15/30/45 across HP/YangDef/YinDef).</summary>
        /// <remarks>Pre: tier and polarity defined. Post: Total == 15 * tier. Side effects: none.</remarks>
        public static SixStatBonus ComputeArmorSixStats(CraftMaterialTier tier, WeaponPolarity polarity)
        {
            Contract.Requires(Enum.IsDefined(typeof(CraftMaterialTier), tier), "invalid tier");
            Contract.Requires(Enum.IsDefined(typeof(WeaponPolarity), polarity), "invalid polarity");
            int budget = 15 * (int)tier;
            if (polarity == WeaponPolarity.Balanced)
            {
                int third = budget / 3;
                return new SixStatBonus(third, 0, 0, 0, third, budget - 2 * third);
            }

            int hp = budget / 5;
            int primary = budget * 3 / 5;
            int secondary = budget - hp - primary;
            return polarity == WeaponPolarity.Yang
                ? new SixStatBonus(hp, 0, 0, 0, primary, secondary)
                : new SixStatBonus(hp, 0, 0, 0, secondary, primary);
        }

        /// <summary>Deterministically rolls 1 offensive enchant affix for a weapon.</summary>
        /// <remarks>Pre: tier and weaponElement defined. Post: 1 offensive EffectSpec (×2 if two-handed). Side effects: none.</remarks>
        public static EffectSpec RollWeaponEnchant(ulong seed, CraftMaterialTier tier, Element weaponElement, bool isTwoHanded)
        {
            Contract.Requires(Enum.IsDefined(typeof(CraftMaterialTier), tier), "invalid tier");
            Contract.Requires(Enum.IsDefined(typeof(Element), weaponElement), "invalid element");
            var rng = new Pcg32(seed ^ ((ulong)tier * 0x517CC1B727220A95UL), 0);
            int tierBase = tier switch { CraftMaterialTier.Tier1 => 3, CraftMaterialTier.Tier2 => 6, _ => 10 };
            int mult = isTwoHanded ? 2 : 1;
            int pick = (int)rng.NextUInt(5);
            return pick switch
            {
                0 => new EffectSpec(EffectType.AccuracyMod, false, default, tierBase * mult, 0, 1),
                1 => new EffectSpec(EffectType.CritRateMod, false, default, tierBase * mult, 0, 1),
                2 => new EffectSpec(EffectType.CritDamageMod, false, default, tierBase * 2 * mult, 0, 1),
                3 => new EffectSpec(EffectType.MasteryMod, false, weaponElement, tierBase * 2 * mult, 0, 1),
                _ => new EffectSpec(EffectType.CdModPercent, true, weaponElement, -tierBase * mult, 0, 1),
            };
        }

        /// <summary>Deterministically rolls 1 defensive enchant affix for an armor or shield.</summary>
        /// <remarks>Pre: tier defined. Post: 1 defensive EffectSpec. Side effects: none.</remarks>
        public static EffectSpec RollArmorEnchant(ulong seed, CraftMaterialTier tier)
        {
            Contract.Requires(Enum.IsDefined(typeof(CraftMaterialTier), tier), "invalid tier");
            var rng = new Pcg32(seed ^ ((ulong)tier * 0x94D049BB133111EBUL), 0);
            int tierBase = tier switch { CraftMaterialTier.Tier1 => 4, CraftMaterialTier.Tier2 => 8, _ => 12 };
            int pick = (int)rng.NextUInt(4);
            return pick switch
            {
                0 => new EffectSpec(EffectType.StaggerCapacityModPercent, false, default, tierBase * 2, 0, 1),
                1 => new EffectSpec(EffectType.BlockReductionMod, false, default, tierBase, 0, 1),
                2 => new EffectSpec(EffectType.EvasionModPercent, false, default, tierBase, 0, 1),
                _ => new EffectSpec(EffectType.MoveSpeedModPercent, false, default, tierBase, 0, 1),
            };
        }

        /// <summary>
        /// Deterministically rolls 1 accessory enchant slot: either 1 elemental mastery + 100% floor,
        /// or 3 distinct resistance affixes (primary + 2 unique others).
        /// </summary>
        /// <remarks>Pre: slotIndex is 0 or 1; primaryElement defined. Post: valid AccessoryEnchantResult. Side effects: allocates.</remarks>
        public static AccessoryEnchantResult RollAccessoryEnchant(ulong seed, int slotIndex, Element primaryElement,
            bool? forceElemental = null)
        {
            Contract.Requires(slotIndex == 0 || slotIndex == 1, "slotIndex must be 0 or 1");
            Contract.Requires(Enum.IsDefined(typeof(Element), primaryElement), "invalid element");
            var rng = new Pcg32(seed ^ ((ulong)(slotIndex + 1) * 0xBF58476D1CE4E5B9UL), 0);
            bool elemental = forceElemental ?? (rng.NextUInt(2) == 0);
            if (elemental)
            {
                return new AccessoryEnchantResult(true, primaryElement, new[]
                {
                    new EffectSpec(EffectType.MasteryMod, false, primaryElement, AccessoryElementMastery, 0, 1),
                });
            }

            int secondIdx = (int)rng.NextUInt(9);
            if (secondIdx >= (int)primaryElement) secondIdx++;
            int thirdIdx = (int)rng.NextUInt(8);
            int a = Math.Min((int)primaryElement, secondIdx);
            int b = Math.Max((int)primaryElement, secondIdx);
            if (thirdIdx >= a) thirdIdx++;
            if (thirdIdx >= b) thirdIdx++;
            return new AccessoryEnchantResult(false, null, new[]
            {
                new EffectSpec(EffectType.ResistMod, false, primaryElement, AccessoryResist, 0, 1),
                new EffectSpec(EffectType.ResistMod, false, (Element)secondIdx, AccessoryResist, 0, 1),
                new EffectSpec(EffectType.ResistMod, false, (Element)thirdIdx, AccessoryResist, 0, 1),
            });
        }

        /// <summary>Mastery points of one accessory elemental enchant slot (also the magic-cube sub's element grant).</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const int AccessoryElementMastery = 12;

        /// <summary>Resist points per resistance of one accessory resist enchant slot (also the magic-cube sub's grant).</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const int AccessoryResist = 8;

        /// <summary>
        /// Effects a magic-cube sub-partner gives its main: the same two slots as an enchanted accessory, derived from the
        /// sub species instead of rolled — slot 1 = its strongest mastery element (+ 100% floor), slot 2 = its three
        /// strongest resistances. REQ: docs/design/crafting.md#魔術方塊副手.
        /// </summary>
        /// <remarks>
        /// Pre: masteryElement defined (the sub's highest-mastery element; caller resolves ties); resistByElement has
        ///      10 entries indexed by Element.
        /// Post: 4 effects (1 MasteryMod on masteryElement, 3 ResistMod on distinct elements) at full strength (no 25%
        ///       partner share); resists ranked by value, ties to the lower Element index. The caller also sets the
        ///       100% mastery floor on masteryElement.
        /// Side effects: none.
        /// </remarks>
        public static IReadOnlyList<EffectSpec> CubePartnerEffects(Element masteryElement, IReadOnlyList<int> resistByElement)
        {
            Contract.Requires(Enum.IsDefined(typeof(Element), masteryElement), "invalid element");
            Contract.Requires(resistByElement != null && resistByElement.Count == 10, "need 10 resist entries");
            var best = new EffectSpec(EffectType.MasteryMod, false, masteryElement, AccessoryElementMastery, 0, 1);
            var picked = new List<Element>(3);
            while (picked.Count < 3)
            {
                Element top = default;
                int topValue = int.MinValue;
                for (int e = (int)Element.Metal; e <= (int)Element.Shadow; e++)
                {
                    if (picked.Contains((Element)e)) continue;
                    int value = resistByElement[e];
                    if (value > topValue) { top = (Element)e; topValue = value; }
                }

                picked.Add(top);
            }

            var effects = new List<EffectSpec>(4)
            {
                best,
            };
            foreach (Element e in picked)
            {
                effects.Add(new EffectSpec(EffectType.ResistMod, false, e, AccessoryResist, 0, 1));
            }

            return effects.AsReadOnly();
        }

        /// <summary>Checks whether equipment can be repaired at <paramref name="location"/>.</summary>
        /// <remarks>Pre: utilities non-null when location == FieldWithSkill. Post: bool. Side effects: none.</remarks>
        public static bool CanRepair(RepairLocation location, CraftSkillCategory materialCategory,
            IReadOnlyCollection<FieldUtility> utilities)
        {
            if (location == RepairLocation.Lobby || location == RepairLocation.VillageNpc) return true;
            Contract.Requires(utilities != null, "utilities required for field repair");
            FieldUtility fallback = FallbackUtility(materialCategory);
            foreach (FieldUtility u in utilities)
            {
                if (u == fallback) return true;
                if (TryGetCategory(u, out CraftSkillCategory cat) && cat == materialCategory) return true;
            }
            return false;
        }

        /// <summary>Number of random affixes rolled for <paramref name="quality"/>.</summary>
        /// <remarks>Pre: none. Post: 0..3. Side effects: none.</remarks>
        public static int AffixCountFor(ItemQuality quality) => quality switch
        {
            ItemQuality.Common => 0,
            ItemQuality.Fine => 1,
            ItemQuality.Masterwork => 2,
            ItemQuality.Mythic => 3,
            _ => 0,
        };

        /// <summary>Deterministically rolls affixes for an item from <paramref name="seed"/> and <paramref name="quality"/>.</summary>
        /// <remarks>
        /// Pre: none.
        /// Post: returned list length == AffixCountFor(quality), reproducible for identical (seed, quality).
        /// Side effects: allocates result list.
        /// </remarks>
        public static List<CraftAffix> RollAffixes(ulong seed, ItemQuality quality)
        {
            int count = AffixCountFor(quality);
            var result = new List<CraftAffix>(count);
            var rng = new Pcg32(seed ^ ((ulong)quality + 0x9E3779B97F4A7C15UL), 0);
            for (int i = 0; i < count; i++)
            {
                string stat = AffixPool[(int)rng.NextUInt((uint)AffixPool.Length)];
                int bonus = 2 + (int)rng.NextUInt(5) + (int)quality * 2;
                result.Add(new CraftAffix(stat, bonus));
            }

            return result;
        }
    }
}
