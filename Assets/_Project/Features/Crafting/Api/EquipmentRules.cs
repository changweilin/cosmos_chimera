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
        internal EquipmentModifiers(int hp, int speed, int roll, List<EffectSpec> effects)
        { PlayerHpPercent = hp; PlayerSpeedPercent = speed; PlayerRollPercent = roll; CreatureEffects = effects.AsReadOnly(); }
        /// <summary>Combined player maximum HP bonus.</summary>
        /// <remarks>Pre: none. Post: 0..300. Side effects: none.</remarks>
        public int PlayerHpPercent { get; }
        /// <summary>Combined player move-speed bonus, clamped to prevent nonpositive speed.</summary>
        /// <remarks>Pre: none. Post: -90..300. Side effects: none.</remarks>
        public int PlayerSpeedPercent { get; }
        /// <summary>Combined player roll cooldown modifier.</summary>
        /// <remarks>Pre: none. Post: -90..300. Side effects: none.</remarks>
        public int PlayerRollPercent { get; }
        /// <summary>Creature effects to apply without using partner slots.</summary>
        /// <remarks>Pre: constructed modifiers. Post: immutable, at most 32 entries. Side effects: none.</remarks>
        public IReadOnlyList<EffectSpec> CreatureEffects { get; }
    }

    /// <summary>Resolves saved equipment against current definitions while retaining retired ids in the loadout.</summary>
    /// <remarks>Pre: valid departure assignments. Post: unknown retired items contribute no effects; incompatible known items are refused. Side effects: allocates.</remarks>
    public static class EquipmentRules
    {
        /// <summary>Collects one owner's equipment modifiers, adding each slot once.</summary>
        /// <remarks>Pre: owner nonnegative; entries and catalog non-null. Post: additive bonuses; duplicate or incompatible slots throw InvalidDataException. Side effects: allocates.</remarks>
        public static EquipmentModifiers Collect(int owner, IReadOnlyList<EquippedItem> entries, CraftCatalog catalog)
        {
            Contract.Requires(owner >= 0 && entries != null && catalog != null, "valid owner, entries and catalog required");
            int hp = 0, speed = 0, roll = 0;
            var slots = new HashSet<string>(StringComparer.Ordinal);
            var effects = new List<EffectSpec>();
            foreach (EquippedItem entry in entries)
            {
                if (entry.Owner != owner) continue;
                if (!EquippedItem.ValidSlot(entry.Slot) || !CraftInventory.ValidId(entry.Item) || !slots.Add(entry.Slot))
                    throw new InvalidDataException("Invalid or duplicate equipment slot");
                if (!catalog.TryGetItem(entry.Item, out CraftItem item)) continue;
                EquipmentSpec spec = item.Equipment;
                if (spec == null || spec.Slot != entry.Slot || !spec.Allows(owner))
                    throw new InvalidDataException("Equipment does not match saved slot or wearer");
                hp += spec.PlayerHpPercent; speed += spec.PlayerSpeedPercent; roll += spec.PlayerRollPercent;
                if (owner != 0) effects.AddRange(spec.CreatureEffects);
            }
            if (effects.Count > 32) throw new InvalidDataException("Combined equipment has more than 32 effects");
            return new EquipmentModifiers(hp, Math.Max(-90, speed), Math.Max(-90, roll), effects);
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
