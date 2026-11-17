using System;
using System.Collections.Generic;
using System.IO;
using Cosmimera.Combat;
using Cosmimera.Core.Contracts;
using Cosmimera.GameData;
using Newtonsoft.Json.Linq;

namespace Cosmimera.Crafting
{
    /// <summary>Saved equipment slot ids and the pre-D0-U3 migration (docs/design/crafting.md#穿戴與數值).</summary>
    /// <remarks>Pre: none. Post: constants. Side effects: none.</remarks>
    public static class EquipmentSlots
    {
        /// <summary>Main hand: one- or two-handed weapon.</summary>
        public const string MainHand = "main_hand";
        /// <summary>Off hand: one-handed weapon (dual wield) or shield; empty while a two-handed weapon is held.</summary>
        public const string OffHand = "off_hand";
        /// <summary>Upper-body armor.</summary>
        public const string Upper = "upper";
        /// <summary>Lower-body armor.</summary>
        public const string Lower = "lower";
        /// <summary>Accessory.</summary>
        public const string Accessory = "accessory";

        /// <summary>All slots in display order.</summary>
        /// <remarks>Pre: none. Post: five distinct ids. Side effects: none.</remarks>
        public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(new[] { MainHand, OffHand, Upper, Lower, Accessory });

        /// <summary>Whether <paramref name="slot"/> is a current slot id.</summary>
        /// <remarks>Pre: none. Post: true iff one of <see cref="All"/>. Side effects: none.</remarks>
        public static bool IsValid(string slot) =>
            slot == MainHand || slot == OffHand || slot == Upper || slot == Lower || slot == Accessory;

        /// <summary>Maps a save-format ≤8 slot to its current slot (weapon → main hand, armor → upper).</summary>
        /// <remarks>
        /// REQ: crafting.md#穿戴與數值 舊存檔遷移.
        /// Pre: none. Post: a current slot id for weapon/armor/accessory; null for anything else. Side effects: none.
        /// </remarks>
        public static string MigrateLegacy(string slot)
        {
            switch (slot)
            {
                case "weapon": return MainHand;
                case "armor": return Upper;
                case "accessory": return Accessory;
                default: return null;
            }
        }
    }

    /// <summary>How a species may hold its hands (crafting.md#穿戴與數值).</summary>
    public enum GripMode
    {
        /// <summary>One two-handed weapon.</summary>
        TwoHanded,
        /// <summary>Two alternating one-handed weapons.</summary>
        DualWield,
        /// <summary>One one-handed weapon and a shield.</summary>
        OneHandShield,
    }

    /// <summary>What an equipment item is and therefore which slots accept it.</summary>
    public enum EquipmentKind
    {
        /// <summary>Main or off hand.</summary>
        OneHanded,
        /// <summary>Main hand; occupies the off hand too.</summary>
        TwoHanded,
        /// <summary>Off hand.</summary>
        Shield,
        /// <summary>Upper-body armor.</summary>
        Upper,
        /// <summary>Lower-body armor.</summary>
        Lower,
        /// <summary>Accessory.</summary>
        Accessory,
    }

    /// <summary>One persistent equipment slot; owner zero is the player, other owners are owned-creature instance ids.</summary>
    /// <remarks>Pre: valid owner, slot and item. Post: immutable. Side effects: none.</remarks>
    public readonly struct EquippedItem
    {
        /// <summary>Creates a saved equipment assignment.</summary>
        /// <remarks>Pre: owner nonnegative; EquipmentSlots.IsValid(slot); item valid id. Post: arguments retained. Side effects: none.</remarks>
        public EquippedItem(int owner, string slot, string item)
        {
            Contract.Requires(owner >= 0 && EquipmentSlots.IsValid(slot) && CraftInventory.ValidId(item), "invalid equipment assignment");
            Owner = owner; Slot = slot; Item = item;
        }
        /// <summary>Player zero or owned-creature instance id.</summary>
        /// <remarks>Pre: none. Post: nonnegative. Side effects: none.</remarks>
        public int Owner { get; }
        /// <summary>Equipment slot id.</summary>
        /// <remarks>Pre: constructed entry. Post: EquipmentSlots.IsValid. Side effects: none.</remarks>
        public string Slot { get; }
        /// <summary>Equipped item, reserved outside the material bag.</summary>
        /// <remarks>Pre: constructed entry. Post: valid id. Side effects: none.</remarks>
        public string Item { get; }
    }

    /// <summary>Equipment bonuses supplied by crafting data.</summary>
    /// <remarks>
    /// Pre: parsed through CraftCatalog. Post: immutable; player modifiers bounded and creature effects non-instant;
    /// weapons carry a profile and no player modifiers (the player only takes defensive and elemental effects).
    /// Side effects: none.
    /// </remarks>
    public sealed class EquipmentSpec
    {
        internal EquipmentSpec(JToken json)
        {
            Kind = ParseKind((string)json["kind"]);
            Target = (string)json["target"];
            if (Target != "player" && Target != "creature" && Target != "both")
                throw new InvalidDataException("Invalid equipment target");
            PlayerHpPercent = ReadDelta(json["player"], "max_hp_percent", 0, 100);
            PlayerSpeedPercent = ReadDelta(json["player"], "move_speed_percent", -50, 100);
            PlayerRollPercent = ReadDelta(json["player"], "roll_cooldown_percent", -50, 100);
            IsWeapon = Kind == EquipmentKind.OneHanded || Kind == EquipmentKind.TwoHanded;
            if (IsWeapon != (json["weapon"] != null)) throw new InvalidDataException("Weapon data must match a weapon kind");
            if (IsWeapon)
            {
                if (Target == "player" || PlayerHpPercent != 0 || PlayerSpeedPercent != 0 || PlayerRollPercent != 0)
                    throw new InvalidDataException("Weapons give the player nothing");
                JToken w = json["weapon"];
                Weapon = new WeaponProfile(ParseElement((string)w["element"]), ReadDelta(w, "element_mastery", 0, 100),
                    ReadDelta(w, "power_permille", 1, DamageModel.MaxWeaponPermille), ReadDelta(w, "speed_permille", 100, 10000));
            }
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
                    case "accuracy_mod": type = EffectType.AccuracyMod; break;
                    case "crit_rate_mod": type = EffectType.CritRateMod; break;
                    case "crit_damage_mod": type = EffectType.CritDamageMod; break;
                    case "stagger_capacity_mod_percent": type = EffectType.StaggerCapacityModPercent; break;
                    case "block_reduction_mod": type = EffectType.BlockReductionMod; break;
                    default: throw new InvalidDataException("Equipment effect must be a continuous modifier");
                }
                int delta = ReadDelta(effect["params"], "delta", -100, 100);
                string elementId = (string)effect["params"]?["element"];
                Element element = default;
                bool any = elementId == "any";
                if (type == EffectType.MasteryMod || type == EffectType.ResistMod || type == EffectType.CdModPercent)
                {
                    if (any && type != EffectType.CdModPercent) throw new InvalidDataException("Only cooldown accepts any element");
                    if (!any) element = ParseElement(elementId);
                }
                list.Add(new EffectSpec(type, any, element, delta, 0, 1));
            }
            CreatureEffects = list.AsReadOnly();
        }

        private static EquipmentKind ParseKind(string id)
        {
            switch (id)
            {
                case "one_handed": return EquipmentKind.OneHanded;
                case "two_handed": return EquipmentKind.TwoHanded;
                case "shield": return EquipmentKind.Shield;
                case "upper": return EquipmentKind.Upper;
                case "lower": return EquipmentKind.Lower;
                case "accessory": return EquipmentKind.Accessory;
                default: throw new InvalidDataException("Invalid equipment kind: " + id);
            }
        }

        private static Element ParseElement(string id)
        {
            if (id == null || !Enum.TryParse(id, true, out Element element) || id != element.ToString().ToLowerInvariant() ||
                !Enum.IsDefined(typeof(Element), element)) throw new InvalidDataException("Invalid equipment element");
            return element;
        }

        private static int ReadDelta(JToken value, string name, int min, int max)
        {
            JToken field = value?[name];
            if (field?.Type != JTokenType.Integer || (long)field < min || (long)field > max)
                throw new InvalidDataException("Equipment modifier out of range: " + name);
            return (int)field;
        }

        /// <summary>Item kind.</summary>
        /// <remarks>Pre: none. Post: defined. Side effects: none.</remarks>
        public EquipmentKind Kind { get; }
        /// <summary>Eligible wearer category.</summary>
        /// <remarks>Pre: none. Post: player/creature/both; never player for weapons. Side effects: none.</remarks>
        public string Target { get; }
        /// <summary>Whether this is a one- or two-handed weapon.</summary>
        /// <remarks>Pre: none. Post: true iff Kind is OneHanded or TwoHanded. Side effects: none.</remarks>
        public bool IsWeapon { get; }
        /// <summary>Weapon base numbers; meaningless unless IsWeapon.</summary>
        /// <remarks>Pre: none. Post: constructed iff IsWeapon. Side effects: none.</remarks>
        public WeaponProfile Weapon { get; }
        /// <summary>Player maximum HP bonus in percent.</summary>
        /// <remarks>Pre: none. Post: 0..100; 0 for weapons. Side effects: none.</remarks>
        public int PlayerHpPercent { get; }
        /// <summary>Player movement bonus in percent.</summary>
        /// <remarks>Pre: none. Post: -50..100; 0 for weapons. Side effects: none.</remarks>
        public int PlayerSpeedPercent { get; }
        /// <summary>Player roll cooldown modifier in percent.</summary>
        /// <remarks>Pre: none. Post: -50..100; 0 for weapons. Side effects: none.</remarks>
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

        /// <summary>Whether <paramref name="slot"/> accepts this kind.</summary>
        /// <remarks>Pre: none. Post: main hand ← one/two-handed; off hand ← one-handed/shield; armor and accessory ← same name. Side effects: none.</remarks>
        public bool Fits(string slot)
        {
            switch (Kind)
            {
                case EquipmentKind.OneHanded: return slot == EquipmentSlots.MainHand || slot == EquipmentSlots.OffHand;
                case EquipmentKind.TwoHanded: return slot == EquipmentSlots.MainHand;
                case EquipmentKind.Shield: return slot == EquipmentSlots.OffHand;
                case EquipmentKind.Upper: return slot == EquipmentSlots.Upper;
                case EquipmentKind.Lower: return slot == EquipmentSlots.Lower;
                default: return slot == EquipmentSlots.Accessory;
            }
        }
    }

    /// <summary>Persistent equipment; equipped copies are held outside crafting stock so recipes cannot consume them.</summary>
    /// <remarks>Pre: single-threaded lobby access; caller validates creature ownership. Post: one item per owner/slot, at most five per owner. Side effects: instance and bag mutation.</remarks>
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
                Contract.Requires(entry.Owner >= 0 && EquipmentSlots.IsValid(entry.Slot) && CraftInventory.ValidId(entry.Item), "invalid entry");
                Contract.Requires(keys.Add(entry.Owner + ":" + entry.Slot), "duplicate equipment slot");
                copy.Add(entry);
            }
            _entries.Clear(); _entries.AddRange(copy);
        }

        /// <summary>Item in an owner's slot, or null.</summary>
        /// <remarks>Pre: owner nonnegative; slot valid. Post: null iff empty. Side effects: none.</remarks>
        public string ItemIn(int owner, string slot)
        {
            Contract.Requires(owner >= 0 && EquipmentSlots.IsValid(slot), "valid owner and slot required");
            int index = _entries.FindIndex(e => e.Owner == owner && e.Slot == slot);
            return index < 0 ? null : _entries[index].Item;
        }

        /// <summary>Equips one bag item into <paramref name="slot"/>, returning every displaced item to the bag atomically.</summary>
        /// <remarks>
        /// REQ: crafting.md#穿戴與數值 — a two-handed weapon occupies both hands; the resulting hands must match one of
        /// <paramref name="grips"/> (pass <see cref="EquipmentRules.PlayerGrips"/> for the player).
        /// Pre: owner nonnegative; id, slot, grips, catalog and inventory non-null.
        /// Post: false leaves bag and equipment unchanged (unknown/unfit item, wrong wearer, grip not allowed, full bag);
        ///       true moves one copy out of the bag and puts the displaced items (the slot's item; both hands for a
        ///       two-handed weapon; the two-handed weapon when filling the off hand) back.
        /// Side effects: mutates both on success.
        /// </remarks>
        public bool TryEquip(int owner, string id, string slot, IReadOnlyCollection<GripMode> grips, CraftCatalog catalog,
            CraftInventory inventory)
        {
            Contract.Requires(owner >= 0 && id != null && slot != null && grips != null && catalog != null && inventory != null,
                "valid arguments required");
            if (!catalog.TryGetItem(id, out CraftItem item) || item.Equipment == null || !item.Equipment.Allows(owner) ||
                !item.Equipment.Fits(slot)) return false;
            var displaced = new List<string>();
            string main = ItemIn(owner, EquipmentSlots.MainHand), off = ItemIn(owner, EquipmentSlots.OffHand);
            string newMain = main, newOff = off;
            if (slot == EquipmentSlots.MainHand)
            {
                if (main != null) displaced.Add(main);
                newMain = id;
                if (item.Equipment.Kind == EquipmentKind.TwoHanded && off != null) { displaced.Add(off); newOff = null; }
            }
            else if (slot == EquipmentSlots.OffHand)
            {
                if (off != null) displaced.Add(off);
                newOff = id;
                if (main != null && KindOf(main, catalog) == EquipmentKind.TwoHanded) { displaced.Add(main); newMain = null; }
            }
            else
            {
                string current = ItemIn(owner, slot);
                if (current != null) displaced.Add(current);
            }
            if (EquipmentRules.RequiredGrips(KindOf(newMain, catalog), KindOf(newOff, catalog)) is GripMode[] required &&
                !AnyAllowed(required, grips)) return false;
            var gains = new CraftStack[displaced.Count];
            for (int i = 0; i < gains.Length; i++) gains[i] = new CraftStack(displaced[i], 1);
            if (!inventory.TryExchange(new[] { new CraftStack(id, 1) }, gains)) return false;
            _entries.RemoveAll(e => e.Owner == owner && (e.Slot == slot ||
                (e.Slot == EquipmentSlots.MainHand && newMain == null) || (e.Slot == EquipmentSlots.OffHand && newOff == null)));
            _entries.Add(new EquippedItem(owner, slot, id));
            return true;
        }

        /// <summary>Returns one equipped item to stock, refusing full stacks without removing equipment.</summary>
        /// <remarks>Pre: owner nonnegative; slot valid; inventory non-null. Post: success removes exactly one assignment. Side effects: mutates both on success.</remarks>
        public bool TryUnequip(int owner, string slot, CraftInventory inventory)
        {
            Contract.Requires(owner >= 0 && EquipmentSlots.IsValid(slot) && inventory != null, "valid arguments required");
            int index = _entries.FindIndex(e => e.Owner == owner && e.Slot == slot);
            if (index < 0 || !inventory.TryExchange(Array.Empty<CraftStack>(), new[] { new CraftStack(_entries[index].Item, 1) })) return false;
            _entries.RemoveAt(index); return true;
        }

        // Retired ids have no kind; they hold their slot but never constrain the grip.
        private static EquipmentKind? KindOf(string id, CraftCatalog catalog) =>
            id != null && catalog.TryGetItem(id, out CraftItem item) && item.Equipment != null ? item.Equipment.Kind : (EquipmentKind?)null;

        private static bool AnyAllowed(GripMode[] required, IReadOnlyCollection<GripMode> grips)
        {
            foreach (GripMode mode in required) foreach (GripMode g in grips) if (g == mode) return true;
            return false;
        }
    }
}
