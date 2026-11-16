using System;
using System.Collections.Generic;
using System.IO;
using Cosmimera.Combat;
using Cosmimera.Core.Contracts;
using Cosmimera.GameData;
using Newtonsoft.Json.Linq;

namespace Cosmimera.Crafting
{
    /// <summary>One persistent equipment slot; owner zero is the player, other owners are owned-creature instance ids.</summary>
    /// <remarks>Pre: valid owner, slot and item. Post: immutable. Side effects: none.</remarks>
    public readonly struct EquippedItem
    {
        /// <summary>Creates a saved equipment assignment.</summary>
        /// <remarks>Pre: owner nonnegative; slot weapon/armor/accessory; item valid id. Post: arguments retained. Side effects: none.</remarks>
        public EquippedItem(int owner, string slot, string item)
        {
            Contract.Requires(owner >= 0 && ValidSlot(slot) && CraftInventory.ValidId(item), "invalid equipment assignment");
            Owner = owner; Slot = slot; Item = item;
        }
        internal static bool ValidSlot(string slot) => slot == "weapon" || slot == "armor" || slot == "accessory";
        /// <summary>Player zero or owned-creature instance id.</summary>
        /// <remarks>Pre: none. Post: nonnegative. Side effects: none.</remarks>
        public int Owner { get; }
        /// <summary>Equipment slot id.</summary>
        /// <remarks>Pre: constructed entry. Post: weapon/armor/accessory. Side effects: none.</remarks>
        public string Slot { get; }
        /// <summary>Equipped item, reserved outside the material bag.</summary>
        /// <remarks>Pre: constructed entry. Post: valid id. Side effects: none.</remarks>
        public string Item { get; }
    }

    /// <summary>Equipment bonuses supplied by crafting data.</summary>
    /// <remarks>Pre: parsed through CraftCatalog. Post: immutable; player modifiers bounded and creature effects non-instant. Side effects: none.</remarks>
    public sealed class EquipmentSpec
    {
        internal EquipmentSpec(JToken json)
        {
            Slot = (string)json["slot"];
            Target = (string)json["target"];
            if (!EquippedItem.ValidSlot(Slot) || (Target != "player" && Target != "creature" && Target != "both"))
                throw new InvalidDataException("Invalid equipment slot or target");
            PlayerHpPercent = ReadDelta(json["player"], "max_hp_percent", 0, 100);
            PlayerSpeedPercent = ReadDelta(json["player"], "move_speed_percent", -50, 100);
            PlayerRollPercent = ReadDelta(json["player"], "roll_cooldown_percent", -50, 100);
            if (!(json["creature_effects"] is JArray effects) || effects.Count > 32)
                throw new InvalidDataException("Equipment requires at most 32 effects");
            var list = new List<EffectSpec>();
            foreach (JToken effect in effects)
            {
                if (!(effect is JObject) || (effect["max_stacks"] != null && (int)effect["max_stacks"] != 1))
                    throw new InvalidDataException("Equipment modifiers do not stack");
                EffectType type;
                switch ((string)effect["type"])
                {
                    case "mastery_mod": type = EffectType.MasteryMod; break;
                    case "resist_mod": type = EffectType.ResistMod; break;
                    case "cd_mod_percent": type = EffectType.CdModPercent; break;
                    case "evasion_mod_percent": type = EffectType.EvasionModPercent; break;
                    case "move_speed_mod_percent": type = EffectType.MoveSpeedModPercent; break;
                    case "range_mod_percent": type = EffectType.RangeModPercent; break;
                    default: throw new InvalidDataException("Equipment effect must be a continuous modifier");
                }
                int delta = ReadDelta(effect["params"], "delta", -100, 100);
                string elementId = (string)effect["params"]?["element"];
                Element element = default;
                bool any = elementId == "any";
                if (type == EffectType.MasteryMod || type == EffectType.ResistMod || type == EffectType.CdModPercent)
                {
                    if (any && type != EffectType.CdModPercent) throw new InvalidDataException("Only cooldown accepts any element");
                    if (!any && (!Enum.TryParse(elementId, true, out element) || elementId != element.ToString().ToLowerInvariant() ||
                        !Enum.IsDefined(typeof(Element), element))) throw new InvalidDataException("Invalid equipment element");
                }
                list.Add(new EffectSpec(type, any, element, delta, 0, 1));
            }
            CreatureEffects = list.AsReadOnly();
        }

        private static int ReadDelta(JToken value, string name, int min, int max)
        {
            JToken field = value?[name];
            if (field?.Type != JTokenType.Integer || (long)field < min || (long)field > max)
                throw new InvalidDataException("Equipment modifier out of range: " + name);
            return (int)field;
        }

        /// <summary>Equipment slot.</summary>
        /// <remarks>Pre: none. Post: weapon/armor/accessory. Side effects: none.</remarks>
        public string Slot { get; }
        /// <summary>Eligible wearer category.</summary>
        /// <remarks>Pre: none. Post: player/creature/both. Side effects: none.</remarks>
        public string Target { get; }
        /// <summary>Player maximum HP bonus in percent.</summary>
        /// <remarks>Pre: none. Post: 0..100. Side effects: none.</remarks>
        public int PlayerHpPercent { get; }
        /// <summary>Player movement bonus in percent.</summary>
        /// <remarks>Pre: none. Post: -50..100. Side effects: none.</remarks>
        public int PlayerSpeedPercent { get; }
        /// <summary>Player roll cooldown modifier in percent.</summary>
        /// <remarks>Pre: none. Post: -50..100. Side effects: none.</remarks>
        public int PlayerRollPercent { get; }
        /// <summary>Permanent creature modifiers, applied independently of partner traits.</summary>
        /// <remarks>Pre: none. Post: immutable non-instant effects. Side effects: none.</remarks>
        public IReadOnlyList<EffectSpec> CreatureEffects { get; }

        /// <summary>Whether an owner category may wear this item.</summary>
        /// <remarks>Pre: owner nonnegative. Post: true iff target matches player zero or creature positive. Side effects: none.</remarks>
        public bool Allows(int owner)
        {
            Contract.Requires(owner >= 0, "owner nonnegative");
            return Target == "both" || (owner == 0 ? Target == "player" : Target == "creature");
        }
    }

    /// <summary>Persistent equipment; equipped copies are held outside crafting stock so recipes cannot consume them.</summary>
    /// <remarks>Pre: single-threaded lobby access; caller validates creature ownership. Post: one item per owner/slot, at most three per owner. Side effects: instance and bag mutation.</remarks>
    public sealed class EquipmentLoadout
    {
        private readonly List<EquippedItem> _entries = new List<EquippedItem>();
        /// <summary>Copies assignments in owner and slot order.</summary>
        /// <remarks>Pre: none. Post: immutable independent snapshot. Side effects: allocates.</remarks>
        public IReadOnlyList<EquippedItem> Snapshot()
        {
            var result = new List<EquippedItem>(_entries);
            result.Sort((a, b) => a.Owner != b.Owner ? a.Owner.CompareTo(b.Owner) : string.CompareOrdinal(a.Slot, b.Slot));
            return result.AsReadOnly();
        }
        /// <summary>Restores saved assignments without consulting current content, retaining retired item ids.</summary>
        /// <remarks>Pre: entries non-null, valid constructed entries with unique owner/slot. Post: copies assignments or fails before mutation. Side effects: replaces assignments.</remarks>
        public void Restore(IReadOnlyList<EquippedItem> entries)
        {
            Contract.Requires(entries != null, "entries required");
            var copy = new List<EquippedItem>();
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (EquippedItem entry in entries)
            {
                Contract.Requires(entry.Owner >= 0 && EquippedItem.ValidSlot(entry.Slot) && CraftInventory.ValidId(entry.Item), "invalid entry");
                Contract.Requires(keys.Add(entry.Owner + ":" + entry.Slot), "duplicate equipment slot");
                copy.Add(entry);
            }
            _entries.Clear(); _entries.AddRange(copy);
        }
        /// <summary>Equips one bag item, returning the previous slot item to the bag atomically.</summary>
        /// <remarks>Pre: owner nonnegative; catalog, inventory and item id non-null. Post: false leaves bag and equipment unchanged. Side effects: mutates both on success.</remarks>
        public bool TryEquip(int owner, string id, CraftCatalog catalog, CraftInventory inventory)
        {
            Contract.Requires(owner >= 0 && id != null && catalog != null && inventory != null, "valid arguments required");
            if (!catalog.TryGetItem(id, out CraftItem item) || item.Equipment == null || !item.Equipment.Allows(owner)) return false;
            int index = _entries.FindIndex(e => e.Owner == owner && e.Slot == item.Equipment.Slot);
            CraftStack[] gains = index < 0 ? Array.Empty<CraftStack>() : new[] { new CraftStack(_entries[index].Item, 1) };
            if (!inventory.TryExchange(new[] { new CraftStack(id, 1) }, gains)) return false;
            var entry = new EquippedItem(owner, item.Equipment.Slot, id);
            if (index < 0) _entries.Add(entry); else _entries[index] = entry;
            return true;
        }
        /// <summary>Returns one equipped item to stock, refusing full stacks without removing equipment.</summary>
        /// <remarks>Pre: owner nonnegative; slot valid; inventory non-null. Post: success removes exactly one assignment. Side effects: mutates both on success.</remarks>
        public bool TryUnequip(int owner, string slot, CraftInventory inventory)
        {
            Contract.Requires(owner >= 0 && EquippedItem.ValidSlot(slot) && inventory != null, "valid arguments required");
            int index = _entries.FindIndex(e => e.Owner == owner && e.Slot == slot);
            if (index < 0 || !inventory.TryExchange(Array.Empty<CraftStack>(), new[] { new CraftStack(_entries[index].Item, 1) })) return false;
            _entries.RemoveAt(index); return true;
        }
    }
}
