using System;
using System.Collections.Generic;
using System.IO;
using Cosmimera.Combat;
using Cosmimera.Core.Contracts;

namespace Cosmimera.Crafting
{
    /// <summary>Combined departure bonuses for one wearer.</summary>
    /// <remarks>Pre: created by EquipmentRules. Post: immutable. Side effects: none.</remarks>
    public readonly struct EquipmentModifiers
    {
        internal EquipmentModifiers(int hp, int speed, int roll, List<EffectSpec> effects, in WeaponLoadout weapons, bool handsValid)
        {
            PlayerHpPercent = hp; PlayerSpeedPercent = speed; PlayerRollPercent = roll; CreatureEffects = effects.AsReadOnly();
            Weapons = weapons; HandsValid = handsValid;
        }
        /// <summary>Combined player maximum HP bonus.</summary>
        /// <remarks>Pre: none. Post: 0..500. Side effects: none.</remarks>
        public int PlayerHpPercent { get; }
        /// <summary>Combined player move-speed bonus, clamped to prevent nonpositive speed.</summary>
        /// <remarks>Pre: none. Post: -90..500. Side effects: none.</remarks>
        public int PlayerSpeedPercent { get; }
        /// <summary>Combined player roll cooldown modifier.</summary>
        /// <remarks>Pre: none. Post: -90..500. Side effects: none.</remarks>
        public int PlayerRollPercent { get; }
        /// <summary>Creature effects to apply without using partner slots.</summary>
        /// <remarks>Pre: constructed modifiers. Post: immutable, at most 32 entries. Side effects: none.</remarks>
        public IReadOnlyList<EffectSpec> CreatureEffects { get; }
        /// <summary>Weapons and shield for a creature wearer; unarmed (default) for the player or invalid hands.</summary>
        /// <remarks>Pre: none. Post: none. Side effects: none.</remarks>
        public WeaponLoadout Weapons { get; }
        /// <summary>False when the held hands no longer match the wearer's grips (e.g. after evolving); those hands then give nothing.</summary>
        /// <remarks>Pre: none. Post: always true for the player. Side effects: none.</remarks>
        public bool HandsValid { get; }
    }

    /// <summary>Resolves saved equipment against current definitions while retaining retired ids in the loadout.</summary>
    /// <remarks>Pre: valid departure assignments. Post: unknown retired items contribute no effects; incompatible known items are refused. Side effects: allocates.</remarks>
    public static class EquipmentRules
    {
        private static readonly GripMode[] TwoHandedOnly = { GripMode.TwoHanded };
        private static readonly GripMode[] DualOnly = { GripMode.DualWield };
        private static readonly GripMode[] ShieldOnly = { GripMode.OneHandShield };
        private static readonly GripMode[] SingleOneHanded = { GripMode.DualWield, GripMode.OneHandShield };

        /// <summary>The player may use every slot (crafting.md#穿戴與數值).</summary>
        /// <remarks>Pre: none. Post: all three grips. Side effects: none.</remarks>
        public static IReadOnlyCollection<GripMode> PlayerGrips { get; } =
            Array.AsReadOnly(new[] { GripMode.TwoHanded, GripMode.DualWield, GripMode.OneHandShield });

        /// <summary>Grips any one of which permits the given hands.</summary>
        /// <remarks>
        /// Pre: none (null means the hand is empty or holds a retired item).
        /// Post: null when both hands are empty; empty when no grip permits the pair; otherwise the permitting grips
        ///       (one one-handed weapon alone is allowed by dual wield or one hand + shield).
        /// Side effects: none.
        /// </remarks>
        public static GripMode[] RequiredGrips(EquipmentKind? main, EquipmentKind? off)
        {
            if (main == null && off == null) return null;
            if (main == EquipmentKind.TwoHanded) return off == null ? TwoHandedOnly : Array.Empty<GripMode>();
            if (off == EquipmentKind.Shield) return main == null || main == EquipmentKind.OneHanded ? ShieldOnly : Array.Empty<GripMode>();
            if (main == EquipmentKind.OneHanded && off == EquipmentKind.OneHanded) return DualOnly;
            if (main == EquipmentKind.OneHanded && off == null) return SingleOneHanded;
            if (main == null && off == EquipmentKind.OneHanded) return DualOnly;
            return Array.Empty<GripMode>();
        }

        /// <summary>Collects one owner's equipment modifiers, adding each slot once.</summary>
        /// <remarks>
        /// REQ: crafting.md#穿戴與數值 — weapons give offense only to creatures; the player takes armor, shield and
        /// accessory effects only; two-handed adds grips.TwoHandedDamagePercent, dual wield alternates hands with
        /// grips.DualWieldSpeedPercent.
        /// Pre: owner nonnegative; entries, catalog, wearerGrips and tuning non-null.
        /// Post: additive bonuses; duplicate slots or a known item in a slot that does not fit it throw InvalidDataException;
        ///       if the hands do not match wearerGrips, HandsValid is false and both hands contribute nothing.
        /// Side effects: allocates.
        /// </remarks>
        public static EquipmentModifiers Collect(int owner, IReadOnlyList<EquippedItem> entries, CraftCatalog catalog,
            IReadOnlyCollection<GripMode> wearerGrips, GripTuning tuning)
        {
            Contract.Requires(owner >= 0 && entries != null && catalog != null && wearerGrips != null && tuning != null,
                "valid owner, entries, catalog, grips and tuning required");
            var slots = new HashSet<string>(StringComparer.Ordinal);
            var known = new List<(string Slot, EquipmentSpec Spec)>();
            EquipmentSpec main = null, off = null;
            foreach (EquippedItem entry in entries)
            {
                if (entry.Owner != owner) continue;
                if (!EquipmentSlots.IsValid(entry.Slot) || !CraftInventory.ValidId(entry.Item) || !slots.Add(entry.Slot))
                    throw new InvalidDataException("Invalid or duplicate equipment slot");
                if (!catalog.TryGetItem(entry.Item, out CraftItem item)) continue;
                EquipmentSpec spec = item.Equipment;
                if (spec == null || !spec.Fits(entry.Slot) || !spec.Allows(owner))
                    throw new InvalidDataException("Equipment does not match saved slot or wearer");
                known.Add((entry.Slot, spec));
                if (entry.Slot == EquipmentSlots.MainHand) main = spec;
                else if (entry.Slot == EquipmentSlots.OffHand) off = spec;
            }

            GripMode[] required = RequiredGrips(main?.Kind, off?.Kind);
            bool handsValid = owner == 0 || required == null || Allowed(required, wearerGrips);
            int hp = 0, speed = 0, roll = 0;
            var effects = new List<EffectSpec>();
            foreach ((string slot, EquipmentSpec spec) in known)
            {
                bool hand = slot == EquipmentSlots.MainHand || slot == EquipmentSlots.OffHand;
                if (hand && !handsValid) continue;
                hp += spec.PlayerHpPercent; speed += spec.PlayerSpeedPercent; roll += spec.PlayerRollPercent;
                if (owner != 0) effects.AddRange(spec.CreatureEffects);
            }
            if (effects.Count > 32) throw new InvalidDataException("Combined equipment has more than 32 effects");
            WeaponLoadout weapons = owner == 0 || !handsValid ? default : BuildWeapons(main, off, tuning);
            return new EquipmentModifiers(hp, Math.Max(-90, speed), Math.Max(-90, roll), effects, weapons, handsValid);
        }

        private static WeaponLoadout BuildWeapons(EquipmentSpec main, EquipmentSpec off, GripTuning tuning)
        {
            bool shield = off != null && off.Kind == EquipmentKind.Shield;
            bool mainWeapon = main != null && main.IsWeapon, offWeapon = off != null && off.IsWeapon;
            if (mainWeapon && offWeapon)
                return new WeaponLoadout(2, main.Weapon, off.Weapon, 0, tuning.DualWieldSpeedPercent, false);
            if (mainWeapon || offWeapon)
            {
                EquipmentSpec hand = mainWeapon ? main : off;
                int power = hand.Kind == EquipmentKind.TwoHanded ? tuning.TwoHandedDamagePercent : 0;
                return new WeaponLoadout(1, hand.Weapon, default, power, 0, shield);
            }
            return new WeaponLoadout(0, default, default, 0, 0, shield);
        }

        private static bool Allowed(GripMode[] required, IReadOnlyCollection<GripMode> grips)
        {
            foreach (GripMode mode in required) foreach (GripMode g in grips) if (g == mode) return true;
            return false;
        }

        /// <summary>Share of a sub-partner's creature equipment effect that reaches its main, in percent.</summary>
        /// <remarks>REQ: docs/design/crafting.md#穿戴與數值. Pre: none. Post: constant. Side effects: none.</remarks>
        public const int PartnerSharePercent = 25;

        /// <summary>Scales one sub-partner equipment effect to <see cref="PartnerSharePercent"/>, rounding away from zero.</summary>
        /// <remarks>
        /// REQ: crafting.md#穿戴與數值 (Q-C42: each item scaled on its own, then rounded away from zero, so +5 → +2, −5 → −2).
        /// Grip bonuses and weapon base numbers are not effects and never reach the main.
        /// Pre: effect constructed and non-instant. Post: same type/element/stacks; |Delta| = ceil(|effect.Delta| × 25 / 100),
        ///      sign kept; 0 stays 0. Side effects: none.
        /// </remarks>
        public static EffectSpec ScalePartner(in EffectSpec effect)
        {
            Contract.Requires(!effect.IsInstant && effect.MaxStacks >= 1, "partner equipment needs constructed non-instant effects");
            int magnitude = (Math.Abs(effect.Delta) * PartnerSharePercent + 99) / 100;
            int delta = effect.Delta < 0 ? -magnitude : magnitude;
            return new EffectSpec(effect.Type, effect.AnyElement, effect.Element, delta, 0, effect.MaxStacks);
        }

        /// <summary>Applies player HP and roll bonuses to the base configuration without shortening roll duration or invulnerability.</summary>
        /// <remarks>Pre: baseline constructed. Post: max HP positive, cooldown at least roll duration; overflow saturates. Side effects: none.</remarks>
        public static PlayerVitalsConfig ApplyPlayer(in PlayerVitalsConfig baseline, in EquipmentModifiers modifiers)
        {
            Contract.Requires(baseline.MaxHp > 0, "baseline must be constructed");
            long hp = (long)baseline.MaxHp * (100 + modifiers.PlayerHpPercent) / 100;
            long cooldown = ((long)baseline.RollCooldownMs * (100 + modifiers.PlayerRollPercent) + 99) / 100;
            return new PlayerVitalsConfig((int)Math.Min(int.MaxValue, hp), baseline.RollDurationMs, baseline.RollInvulnerableMs,
                (int)Math.Min(int.MaxValue, Math.Max(baseline.RollDurationMs, cooldown)));
        }
    }
}
