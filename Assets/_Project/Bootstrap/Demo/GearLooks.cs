using System;
using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.Core.Random;
using Cosmimera.Crafting;
using Cosmimera.WorldGen;
using Unity.Mathematics;

namespace Cosmimera.Bootstrap.Demo
{
    /// <summary>What a gear item is (器物類別): the 16 weapon types of crafting.md, shields, garments and ornaments.</summary>
    /// <remarks>REQ: docs/design/gear-looks.md#部件槽. Pre: none. Post: stable values (they seed the look stream); append only. Side effects: none.</remarks>
    public enum GearKind
    {
        Dagger = 0,
        Sword = 1,
        Axe = 2,
        Mace = 3,
        Twinblade = 4,
        Spear = 5,
        Greatsword = 6,
        GreataxeHammer = 7,
        ThrowingBlade = 8,
        HandCrossbow = 9,
        Wand = 10,
        Javelin = 11,
        RepeaterBow = 12,
        Longbow = 13,
        MagicStaff = 14,
        HeavyCrossbow = 15,
        Shield = 16,
        Upper = 17,
        Lower = 18,
        Headwear = 19,
        Accessory = 20,
    }

    /// <summary>Part layout shared by placeholder meshes and the hand-made part library.</summary>
    /// <remarks>REQ: docs/design/gear-looks.md#部件槽. Pre: none. Post: stable values. Side effects: none.</remarks>
    public enum GearFamily
    {
        Blade = 0,
        Blunt = 1,
        Bow = 2,
        Focus = 3,
        Thrown = 4,
        Shield = 5,
        Garment = 6,
        Ornament = 7,
    }

    /// <summary>What the item is used for (用途情境): drives silhouette sharpness and ornament density.</summary>
    /// <remarks>REQ: docs/design/gear-looks.md#功能用途情境. Pre: none. Post: stable values. Side effects: none.</remarks>
    public enum GearContext
    {
        Combat = 0,
        Labor = 1,
        Ceremony = 2,
        Travel = 3,
        Status = 4,
    }

    /// <summary>One region × era pair an aspect of an item is drawn in.</summary>
    /// <remarks>Pre: none. Post: immutable. Side effects: none.</remarks>
    public readonly struct GearStyle : IEquatable<GearStyle>
    {
        /// <summary>Pairs a tradition with an era.</summary>
        /// <remarks>Pre: defined enums. Post: properties return the arguments. Side effects: none.</remarks>
        public GearStyle(ArchRegion region, ArchEra era)
        {
            Contract.Requires(Enum.IsDefined(typeof(ArchRegion), region) && Enum.IsDefined(typeof(ArchEra), era),
                "region and era must be defined");
            Region = region;
            Era = era;
        }

        /// <summary>Regional tradition (palette, motif).</summary>
        /// <remarks>Pre: none. Post: defined. Side effects: none.</remarks>
        public ArchRegion Region { get; }

        /// <summary>Era (form language, craft).</summary>
        /// <remarks>Pre: none. Post: defined. Side effects: none.</remarks>
        public ArchEra Era { get; }

        /// <summary>Value equality.</summary>
        /// <remarks>Pre: none. Post: true iff both axes match. Side effects: none.</remarks>
        public bool Equals(GearStyle other) => Region == other.Region && Era == other.Era;

        /// <summary>Value equality.</summary>
        /// <remarks>Pre: none. Post: true iff obj is an equal GearStyle. Side effects: none.</remarks>
        public override bool Equals(object obj) => obj is GearStyle other && Equals(other);

        /// <summary>Hash consistent with <see cref="Equals(GearStyle)"/>.</summary>
        /// <remarks>Pre: none. Post: equal styles hash equal. Side effects: none.</remarks>
        public override int GetHashCode() => ((int)Region << 8) | (int)Era;
    }

    /// <summary>The sampled look of one gear item; meshes (placeholder now, part library later) are built from this alone.</summary>
    /// <remarks>
    /// REQ: docs/design/gear-looks.md#同類內的隨機性.
    /// Pre: built by <see cref="GearLooks.Sample"/>. Post: immutable; every scalar within its documented range. Side effects: none.
    /// </remarks>
    public sealed class GearLookSpec
    {
        internal GearLookSpec(GearKind kind, GearContext context, GearStyle palette, GearStyle motif, GearStyle parts,
            int[] partVariants, float length, float width, float thickness, float curvature, float motifDensity,
            float hueShift, float valueShift, float age, float damage)
        {
            Kind = kind;
            Context = context;
            Palette = palette;
            Motif = motif;
            Parts = parts;
            PartVariants = Array.AsReadOnly(partVariants);
            Length = length;
            Width = width;
            Thickness = thickness;
            Curvature = curvature;
            MotifDensity = motifDensity;
            HueShift = hueShift;
            ValueShift = valueShift;
            Age = age;
            Damage = damage;
        }

        /// <summary>Item kind.</summary>
        /// <remarks>Pre: none. Post: defined. Side effects: none.</remarks>
        public GearKind Kind { get; }

        /// <summary>Use context.</summary>
        /// <remarks>Pre: none. Post: defined. Side effects: none.</remarks>
        public GearContext Context { get; }

        /// <summary>Style the colours come from.</summary>
        /// <remarks>Pre: none. Post: primary, secondary or tertiary style of the source. Side effects: none.</remarks>
        public GearStyle Palette { get; }

        /// <summary>Style the surface motifs come from.</summary>
        /// <remarks>Pre: none. Post: primary, secondary or tertiary style of the source. Side effects: none.</remarks>
        public GearStyle Motif { get; }

        /// <summary>Style the part shapes come from.</summary>
        /// <remarks>Pre: none. Post: primary, secondary or tertiary style of the source. Side effects: none.</remarks>
        public GearStyle Parts { get; }

        /// <summary>Base-shape index per part slot of <see cref="GearLooks.FamilyOf"/>(Kind).</summary>
        /// <remarks>Pre: none. Post: Count == GearLooks.SlotCount(family); each in [0, GearLooks.VariantsPerSlot). Side effects: none.</remarks>
        public IReadOnlyList<int> PartVariants { get; }

        /// <summary>Length multiplier (blade, haft, shield diameter, garment length).</summary>
        /// <remarks>Pre: none. Post: in [0.7, 1.3]. Side effects: none.</remarks>
        public float Length { get; }

        /// <summary>Width multiplier.</summary>
        /// <remarks>Pre: none. Post: in [0.65, 1.35]. Side effects: none.</remarks>
        public float Width { get; }

        /// <summary>Thickness multiplier.</summary>
        /// <remarks>Pre: none. Post: in [0.58, 1.42]. Side effects: none.</remarks>
        public float Thickness { get; }

        /// <summary>Blade sweep, limb recurve, hem arc: 0 straight … 1 strongly curved.</summary>
        /// <remarks>Pre: none. Post: in [0, 1]. Side effects: none.</remarks>
        public float Curvature { get; }

        /// <summary>Amount of motif and ornament pieces: 0 bare … 1 dense.</summary>
        /// <remarks>Pre: none. Post: in [0, 1]. Side effects: none.</remarks>
        public float MotifDensity { get; }

        /// <summary>Hue offset applied to the palette (turns).</summary>
        /// <remarks>Pre: none. Post: in [-0.105, 0.105]. Side effects: none.</remarks>
        public float HueShift { get; }

        /// <summary>Value (brightness) offset applied to the palette.</summary>
        /// <remarks>Pre: none. Post: in [-0.21, 0.21]. Side effects: none.</remarks>
        public float ValueShift { get; }

        /// <summary>Age: 0 new … 1 faded, grimy, grip polished by use.</summary>
        /// <remarks>Pre: none. Post: in [0, 1]; exactly 0 when the source's AgeMean is 0. Side effects: none.</remarks>
        public float Age { get; }

        /// <summary>Damage: 0 intact … 1 chipped, cracked, broken haft, patched, rusted or moth-eaten.</summary>
        /// <remarks>Pre: none. Post: in [0, 1]; exactly 0 when the source's DamageMean is 0. Side effects: none.</remarks>
        public float Damage { get; }
    }

    /// <summary>
    /// Samples gear looks from the same style axes as buildings and residents, so a settlement's swords, coats and
    /// shields read as made by the people who live there.
    /// </summary>
    /// <remarks>
    /// REQ: docs/design/gear-looks.md. Presentation only: never hashed into RunLayout, never read by combat or crafting.
    /// Pre: none. Post: pure. Side effects: none.
    /// </remarks>
    public static class GearLooks
    {
        /// <summary>Bumped whenever the same inputs would sample a different look (invalidates the snapshot).</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const int GeneratorVersion = 2;

        /// <summary>Base shapes per part slot; the part library may ship 3–6, placeholders use this many.</summary>
        /// <remarks>
        /// Pre: none. Post: constant &gt; 0; every placeholder slot catalogue in GearMeshes has exactly this many entries.
        /// Side effects: none.
        /// </remarks>
        public const int VariantsPerSlot = 6;

        private const ulong Stream = 0x6765_6172_6C6F_6F6Bul; // "gearlook"

        // REQ: gear-looks.md#同類內的隨機性 — σ per facet.
        private const float MixSigma = 0.18f;
        private const float LengthSigma = 0.08f;
        private const float WidthSigma = 0.10f;
        private const float ThicknessSigma = 0.12f;
        private const float ShapeSigma = 0.15f;
        private const float HueSigma = 0.03f;
        private const float ValueSigma = 0.06f;
        private const float AgeSigma = 0.15f;
        private const float DamageSigma = 0.12f;

        /// <summary>Part layout of <paramref name="kind"/>.</summary>
        /// <remarks>Pre: kind defined. Post: defined family. Side effects: none.</remarks>
        public static GearFamily FamilyOf(GearKind kind)
        {
            Contract.Requires(Enum.IsDefined(typeof(GearKind), kind), "kind must be defined");
            switch (kind)
            {
                case GearKind.Dagger:
                case GearKind.Sword:
                case GearKind.Axe:
                case GearKind.Twinblade:
                case GearKind.Spear:
                case GearKind.Greatsword:
                    return GearFamily.Blade;
                case GearKind.Mace:
                case GearKind.GreataxeHammer:
                    return GearFamily.Blunt;
                case GearKind.HandCrossbow:
                case GearKind.RepeaterBow:
                case GearKind.Longbow:
                case GearKind.HeavyCrossbow:
                    return GearFamily.Bow;
                case GearKind.Wand:
                case GearKind.MagicStaff:
                    return GearFamily.Focus;
                case GearKind.ThrowingBlade:
                case GearKind.Javelin:
                    return GearFamily.Thrown;
                case GearKind.Shield:
                    return GearFamily.Shield;
                case GearKind.Upper:
                case GearKind.Lower:
                    return GearFamily.Garment;
                default:
                    return GearFamily.Ornament;
            }
        }

        /// <summary>Number of part slots of <paramref name="family"/>.</summary>
        /// <remarks>REQ: gear-looks.md#部件槽. Pre: family defined. Post: in [3, 6]. Side effects: none.</remarks>
        public static int SlotCount(GearFamily family)
        {
            Contract.Requires(Enum.IsDefined(typeof(GearFamily), family), "family must be defined");
            switch (family)
            {
                case GearFamily.Blade: return 5;
                case GearFamily.Garment: return 6;
                case GearFamily.Ornament: return 3;
                case GearFamily.Thrown: return 3;
                default: return 4;
            }
        }

        /// <summary>Use context a resident's gear is drawn for.</summary>
        /// <remarks>REQ: gear-looks.md#功能用途情境. Pre: role defined. Post: defined context. Side effects: none.</remarks>
        public static GearContext ContextFor(NpcRole role)
        {
            Contract.Requires(Enum.IsDefined(typeof(NpcRole), role), "role must be defined");
            switch (role)
            {
                case NpcRole.Guard:
                    return GearContext.Combat;
                case NpcRole.Cleric:
                    return GearContext.Ceremony;
                case NpcRole.Merchant:
                case NpcRole.Performer:
                case NpcRole.Healer:
                    return GearContext.Travel;
                case NpcRole.Leader:
                case NpcRole.Scholar:
                    return GearContext.Status;
                default:
                    return GearContext.Labor;
            }
        }

        /// <summary>Source for gear the player crafts: pure planet style, brand new, never wearing down.</summary>
        /// <remarks>REQ: gear-looks.md#外觀與數值. Pre: defined enums. Post: Weight, AgeMean and DamageMean are 0. Side effects: none.</remarks>
        public static ArchMix Crafted(ArchRegion region, ArchEra era) => new ArchMix(region, era, 0f, 0f, 0f);

        // Indexed by WeaponType: crafting.md#武器數值模型 and gear-looks.md#部件槽 list the same sixteen types.
        private static readonly GearKind[] WeaponKinds =
        {
            GearKind.Dagger, GearKind.Sword, GearKind.Axe, GearKind.Mace,
            GearKind.Twinblade, GearKind.Spear, GearKind.Greatsword, GearKind.GreataxeHammer,
            GearKind.ThrowingBlade, GearKind.HandCrossbow, GearKind.Wand, GearKind.Javelin,
            GearKind.RepeaterBow, GearKind.Longbow, GearKind.MagicStaff, GearKind.HeavyCrossbow,
        };

        /// <summary>Gear kind an equipment item is drawn as.</summary>
        /// <remarks>
        /// REQ: gear-looks.md#範圍.
        /// Pre: spec non-null; spec.WeaponType defined when spec.HasWeaponType.
        /// Post: the named weapon type's kind; a weapon without one draws as Sword (one-handed) or Greatsword (two-handed);
        ///       Shield, Upper, Lower and Accessory map to the same-named kind. Side effects: none.
        /// </remarks>
        public static GearKind KindFor(EquipmentSpec spec)
        {
            Contract.Requires(spec != null, "spec non-null");
            switch (spec.Kind)
            {
                case EquipmentKind.OneHanded:
                case EquipmentKind.TwoHanded:
                    if (!spec.HasWeaponType) return spec.Kind == EquipmentKind.TwoHanded ? GearKind.Greatsword : GearKind.Sword;
                    Contract.Requires(Enum.IsDefined(typeof(WeaponType), spec.WeaponType), "weapon type defined");
                    return WeaponKinds[(int)spec.WeaponType];
                case EquipmentKind.Shield: return GearKind.Shield;
                case EquipmentKind.Upper: return GearKind.Upper;
                case EquipmentKind.Lower: return GearKind.Lower;
                default: return GearKind.Accessory;
            }
        }

        /// <summary>Look of one crafted copy: its origin planet's style, combat context, brand new.</summary>
        /// <remarks>
        /// REQ: gear-looks.md#外觀與數值 (風格由星球推得; 玩家與宙獸成品固定戰鬥情境; 出廠全新).
        /// Pre: kind defined; look constructed (non-empty origin planet id).
        /// Post: same (kind, look) → equal spec on every client; Age == 0 and Damage == 0. Side effects: none.
        /// </remarks>
        public static GearLookSpec ForCopy(GearKind kind, ItemLook look)
        {
            Contract.Requires(!string.IsNullOrEmpty(look.Origin.PlanetTypeId), "look must be constructed");
            ArchPlanetStyle style = ArchitectureMeshes.PlanetStyleFor(look.Origin.PlanetTypeId, look.Origin.PlanetSeed);
            return Sample(kind, GearContext.Combat, style.Region, style.Era, Crafted(style.Region, style.Era), look.Seed);
        }

        /// <summary>Samples the look of one item.</summary>
        /// <remarks>
        /// REQ: gear-looks.md#五軸.
        /// Pre: kind, context, region and era defined; mix built by its constructor, <see cref="ArchMix.Pure"/>,
        /// <see cref="ArchitectureMeshes.MixFor"/> or <see cref="Crafted"/>.
        /// Post: same arguments → equal spec on every client (Pcg32 only, no transcendental functions); when
        /// mix.Weight == 0 every facet uses (region, era); Age / Damage are exactly 0 when the mean is 0.
        /// Side effects: allocates the spec.
        /// </remarks>
        public static GearLookSpec Sample(GearKind kind, GearContext context, ArchRegion region, ArchEra era, in ArchMix mix, ulong seed)
        {
            Contract.Requires(Enum.IsDefined(typeof(GearContext), context), "context must be defined");
            GearFamily family = FamilyOf(kind);
            var primary = new GearStyle(region, era);
            var rng = new Pcg32(seed, Stream ^ (ulong)kind);

            // A pure source must stay pure: an unclamped σ draw would let crafted gear borrow a style it never had.
            float share = mix.Weight > 0f ? math.saturate(mix.Weight + MixSigma * Gauss(ref rng)) : 0f;
            GearStyle palette = Borrow(primary, mix, share, ref rng);
            GearStyle motif = Borrow(primary, mix, share, ref rng);
            GearStyle parts = Borrow(primary, mix, share, ref rng);

            var variants = new int[SlotCount(family)];
            for (int i = 0; i < variants.Length; i++) variants[i] = rng.NextInt(0, VariantsPerSlot);

            // Irwin–Hall is bounded at ±3.47σ, so these never need clamping; the documented ranges follow from it.
            float length = 1f + LengthSigma * Gauss(ref rng);
            float width = 1f + WidthSigma * Gauss(ref rng);
            float thickness = 1f + ThicknessSigma * Gauss(ref rng);
            (float curveBase, float motifBase) = ContextBase(context);
            float curvature = math.saturate(curveBase + ShapeSigma * Gauss(ref rng));
            float motifDensity = math.saturate(motifBase + ShapeSigma * Gauss(ref rng));
            float hue = HueSigma * Gauss(ref rng);
            float value = ValueSigma * Gauss(ref rng);
            float age = mix.AgeMean > 0f ? math.saturate(mix.AgeMean + AgeSigma * Gauss(ref rng)) : 0f;
            float damage = mix.DamageMean > 0f ? math.saturate(mix.DamageMean + DamageSigma * Gauss(ref rng)) : 0f;

            return new GearLookSpec(kind, context, palette, motif, parts, variants, length, width, thickness,
                curvature, motifDensity, hue, value, age, damage);
        }

        // REQ: gear-looks.md#功能用途情境 — (curvature, motif density) baselines.
        private static (float curve, float motif) ContextBase(GearContext context) => context switch
        {
            GearContext.Combat => (0.35f, 0.2f),
            GearContext.Labor => (0.2f, 0.05f),
            GearContext.Ceremony => (0.5f, 0.8f),
            GearContext.Travel => (0.3f, 0.3f),
            _ => (0.45f, 0.6f),
        };

        // REQ: characters.md#外觀 (混搭) — each facet flips its own coin; borrowed facets go to the third style at its share.
        private static GearStyle Borrow(GearStyle primary, in ArchMix mix, float share, ref Pcg32 rng)
        {
            float coin = rng.NextFloat();
            float pick = rng.NextFloat();
            if (coin >= share) return primary;
            return pick < mix.TertiaryShare
                ? new GearStyle(mix.TertiaryRegion, mix.TertiaryEra)
                : new GearStyle(mix.SecondaryRegion, mix.SecondaryEra);
        }

        // Irwin–Hall(4) scaled to unit variance, same as ArchitectureMeshes: bounded and bit-stable across clients.
        private static float Gauss(ref Pcg32 rng) =>
            (rng.NextFloat() + rng.NextFloat() + rng.NextFloat() + rng.NextFloat() - 2f) * 1.7320508f;

        /// <summary>Derives a creature block shield's visual spec from its upper/lower armour looks and element colour.</summary>
        /// <remarks>
        /// REQ: docs/design/gear-looks.md#宙獸格擋護盾.
        /// Pre: none (upper and lower may be null).
        /// Post: HasPattern is true iff at least one of upper/lower is non-null; when both are non-null, Palette/Motif come
        ///       from upper and Border from lower; when only one is given, all three come from it; Damage is the max.
        /// Side effects: none (pure).
        /// </remarks>
        public static BlockShieldSpec BlockShieldFor(GearLookSpec upper, GearLookSpec lower, UnityEngine.Color elementColor)
        {
            if (upper == null && lower == null)
            {
                return new BlockShieldSpec(false, elementColor, default, default, default, 0f, 0f, 0f, 0f, 0f);
            }

            GearLookSpec main = upper ?? lower;
            GearLookSpec border = lower ?? upper;
            float damage = upper != null && lower != null ? math.max(upper.Damage, lower.Damage) : main.Damage;
            return new BlockShieldSpec(true, elementColor, main.Palette, main.Motif, border.Motif,
                main.HueShift, main.ValueShift, main.Age, damage, main.MotifDensity);
        }
    }

    /// <summary>Visual specification of a creature's block shield VFX (P1-17d).</summary>
    /// <remarks>
    /// REQ: docs/design/gear-looks.md#宙獸格擋護盾.
    /// Pre: built by <see cref="GearLooks.BlockShieldFor"/>. Post: immutable. Side effects: none.
    /// </remarks>
    public readonly struct BlockShieldSpec
    {
        internal BlockShieldSpec(bool hasPattern, UnityEngine.Color elementColor, GearStyle palette, GearStyle motif,
            GearStyle border, float hueShift, float valueShift, float age, float damage, float motifDensity)
        {
            HasPattern = hasPattern;
            ElementColor = elementColor;
            Palette = palette;
            Motif = motif;
            Border = border;
            HueShift = hueShift;
            ValueShift = valueShift;
            Age = age;
            Damage = damage;
            MotifDensity = motifDensity;
        }

        /// <summary>True when at least one armour piece (upper or lower) is equipped; false for an unpatterned element shield.</summary>
        /// <remarks>Pre: none. Post: boolean. Side effects: none.</remarks>
        public bool HasPattern { get; }

        /// <summary>Creature main-element colour used when <see cref="HasPattern"/> is false.</summary>
        /// <remarks>Pre: none. Post: RGBA colour. Side effects: none.</remarks>
        public UnityEngine.Color ElementColor { get; }

        /// <summary>Shield palette style (from upper armour, or lower when upper is absent).</summary>
        /// <remarks>Pre: none. Post: defined when <see cref="HasPattern"/> is true. Side effects: none.</remarks>
        public GearStyle Palette { get; }

        /// <summary>Center era motif style (from upper armour, or lower when upper is absent).</summary>
        /// <remarks>Pre: none. Post: defined when <see cref="HasPattern"/> is true. Side effects: none.</remarks>
        public GearStyle Motif { get; }

        /// <summary>Border motif style (from lower armour, or upper when lower is absent).</summary>
        /// <remarks>Pre: none. Post: defined when <see cref="HasPattern"/> is true. Side effects: none.</remarks>
        public GearStyle Border { get; }

        /// <summary>Hue offset applied to the palette.</summary>
        /// <remarks>Pre: none. Post: bounded scalar. Side effects: none.</remarks>
        public float HueShift { get; }

        /// <summary>Value offset applied to the palette.</summary>
        /// <remarks>Pre: none. Post: bounded scalar. Side effects: none.</remarks>
        public float ValueShift { get; }

        /// <summary>Wear age in [0, 1].</summary>
        /// <remarks>Pre: none. Post: in [0, 1]. Side effects: none.</remarks>
        public float Age { get; }

        /// <summary>Damage in [0, 1]; high values add crack lines to the shield face.</summary>
        /// <remarks>Pre: none. Post: in [0, 1]. Side effects: none.</remarks>
        public float Damage { get; }

        /// <summary>Motif density in [0, 1].</summary>
        /// <remarks>Pre: none. Post: in [0, 1]. Side effects: none.</remarks>
        public float MotifDensity { get; }
    }
}
