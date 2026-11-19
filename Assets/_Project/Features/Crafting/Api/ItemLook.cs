using System;
using System.Collections.Generic;
using Cosmimera.Core.Contracts;

namespace Cosmimera.Crafting
{
    /// <summary>The planet a crafted item was made on; its style axes decide the item's region and era.</summary>
    /// <remarks>
    /// REQ: docs/design/gear-looks.md#外觀與數值 — lobby crafting uses the most recently departed planet, else the cradle planet.
    /// Pre: built by the constructor or <see cref="Cradle"/>. Post: immutable. Side effects: none.
    /// </remarks>
    public readonly struct GearOrigin : IEquatable<GearOrigin>
    {
        /// <summary>Planet type of the starter (cradle) planet, used before the first departure and for pre-look items.</summary>
        /// <remarks>Pre: none. Post: constant snake_case id. Side effects: none.</remarks>
        public const string CradlePlanetTypeId = "cradle_planet";

        /// <summary>Names a planet by type and generation seed.</summary>
        /// <remarks>Pre: planetTypeId snake_case, at most 64 characters. Post: properties return the arguments. Side effects: none.</remarks>
        public GearOrigin(string planetTypeId, ulong planetSeed)
        {
            Contract.Requires(CraftInventory.ValidId(planetTypeId), "planet type id must be snake_case");
            PlanetTypeId = planetTypeId;
            PlanetSeed = planetSeed;
        }

        /// <summary>The cradle planet with seed 0 (no departure recorded yet).</summary>
        /// <remarks>Pre: none. Post: PlanetTypeId == CradlePlanetTypeId; PlanetSeed == 0. Side effects: none.</remarks>
        public static GearOrigin Cradle => new GearOrigin(CradlePlanetTypeId, 0);

        /// <summary>Planet type id (style region).</summary>
        /// <remarks>Pre: none. Post: valid id (null only for default(GearOrigin)). Side effects: none.</remarks>
        public string PlanetTypeId { get; }

        /// <summary>Planet seed (style era and secondaries).</summary>
        /// <remarks>Pre: none. Post: any value. Side effects: none.</remarks>
        public ulong PlanetSeed { get; }

        /// <summary>Value equality.</summary>
        /// <remarks>Pre: none. Post: true iff type and seed match. Side effects: none.</remarks>
        public bool Equals(GearOrigin other) => string.Equals(PlanetTypeId, other.PlanetTypeId, StringComparison.Ordinal) &&
            PlanetSeed == other.PlanetSeed;

        /// <summary>Value equality.</summary>
        /// <remarks>Pre: none. Post: true iff obj is an equal GearOrigin. Side effects: none.</remarks>
        public override bool Equals(object obj) => obj is GearOrigin other && Equals(other);

        /// <summary>Hash consistent with equality.</summary>
        /// <remarks>Pre: none. Post: equal origins hash equal. Side effects: none.</remarks>
        public override int GetHashCode() => (PlanetTypeId?.GetHashCode() ?? 0) ^ PlanetSeed.GetHashCode();
    }

    /// <summary>The look of one copy of a crafted item: where it was made and its own look seed.</summary>
    /// <remarks>
    /// REQ: docs/design/gear-looks.md#外觀與數值 — presentation only; no rule reads it.
    /// Pre: built by the constructor or <see cref="Default"/>. Post: immutable. Side effects: none.
    /// </remarks>
    public readonly struct ItemLook : IEquatable<ItemLook>
    {
        /// <summary>Tags one copy.</summary>
        /// <remarks>Pre: origin built by its constructor. Post: properties return the arguments. Side effects: none.</remarks>
        public ItemLook(GearOrigin origin, ulong seed)
        {
            Contract.Requires(CraftInventory.ValidId(origin.PlanetTypeId), "origin must be constructed");
            Origin = origin;
            Seed = seed;
        }

        /// <summary>Planet the copy was made on.</summary>
        /// <remarks>Pre: none. Post: constructed origin. Side effects: none.</remarks>
        public GearOrigin Origin { get; }

        /// <summary>Per-copy look seed.</summary>
        /// <remarks>Pre: none. Post: any value. Side effects: none.</remarks>
        public ulong Seed { get; }

        /// <summary>Look of a copy that was never tagged (saved before looks existed): cradle planet, seed from id and index.</summary>
        /// <remarks>
        /// REQ: gear-looks.md#外觀與數值 (舊存檔依物品 id 與序號補標記，取搖籃星).
        /// Pre: itemId valid; index &gt;= 0. Post: same arguments → same look on every client. Side effects: none.
        /// </remarks>
        public static ItemLook Default(string itemId, int index)
        {
            Contract.Requires(CraftInventory.ValidId(itemId) && index >= 0, "valid item id and nonnegative index required");
            return new ItemLook(GearOrigin.Cradle, Mix(Fnv(itemId), (ulong)index));
        }

        /// <summary>Look of the <paramref name="ordinal"/>-th copy of <paramref name="itemId"/> crafted on <paramref name="origin"/>.</summary>
        /// <remarks>
        /// Pre: itemId valid; origin constructed. Post: deterministic; differs between ordinals and planets.
        /// Side effects: none.
        /// </remarks>
        public static ItemLook Crafted(string itemId, GearOrigin origin, ulong ordinal)
        {
            Contract.Requires(CraftInventory.ValidId(itemId), "valid item id required");
            return new ItemLook(origin, Mix(Mix(Fnv(itemId), origin.PlanetSeed), ordinal));
        }

        /// <summary>Value equality.</summary>
        /// <remarks>Pre: none. Post: true iff origin and seed match. Side effects: none.</remarks>
        public bool Equals(ItemLook other) => Origin.Equals(other.Origin) && Seed == other.Seed;

        /// <summary>Value equality.</summary>
        /// <remarks>Pre: none. Post: true iff obj is an equal ItemLook. Side effects: none.</remarks>
        public override bool Equals(object obj) => obj is ItemLook other && Equals(other);

        /// <summary>Hash consistent with equality.</summary>
        /// <remarks>Pre: none. Post: equal looks hash equal. Side effects: none.</remarks>
        public override int GetHashCode() => Origin.GetHashCode() ^ Seed.GetHashCode();

        // string.GetHashCode is randomised per process; looks must match across clients and sessions.
        private static ulong Fnv(string s)
        {
            ulong h = 14695981039346656037ul;
            foreach (char c in s) { h ^= c; h *= 1099511628211ul; }
            return h;
        }

        // SplitMix64 finaliser: neighbouring ordinals land far apart.
        private static ulong Mix(ulong a, ulong b)
        {
            ulong z = a + 0x9E3779B97F4A7C15ul * (b + 1);
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9ul;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBul;
            return z ^ (z >> 31);
        }
    }

    /// <summary>One concrete copy of an item: its id and look.</summary>
    /// <remarks>Pre: built by the constructor. Post: immutable. Side effects: none.</remarks>
    public readonly struct ItemCopy
    {
        /// <summary>Pairs an id with a look.</summary>
        /// <remarks>Pre: id valid; look constructed. Post: properties return the arguments. Side effects: none.</remarks>
        public ItemCopy(string id, ItemLook look)
        {
            Contract.Requires(CraftInventory.ValidId(id) && CraftInventory.ValidId(look.Origin.PlanetTypeId), "valid copy required");
            Id = id;
            Look = look;
        }

        /// <summary>Item id.</summary>
        /// <remarks>Pre: none. Post: valid id. Side effects: none.</remarks>
        public string Id { get; }

        /// <summary>Copy look.</summary>
        /// <remarks>Pre: none. Post: constructed look. Side effects: none.</remarks>
        public ItemLook Look { get; }
    }

    /// <summary>The tagged copies of one item id, oldest first (untagged copies precede them and are not listed).</summary>
    /// <remarks>Pre: built by <see cref="CraftInventory.LookSnapshot"/> or the constructor. Post: immutable. Side effects: none.</remarks>
    public sealed class ItemLookList
    {
        /// <summary>Lists tagged looks for an id.</summary>
        /// <remarks>Pre: id valid; looks non-null and constructed. Post: copies the list. Side effects: allocates.</remarks>
        public ItemLookList(string id, IReadOnlyList<ItemLook> looks)
        {
            Contract.Requires(CraftInventory.ValidId(id) && looks != null, "valid id and looks required");
            Id = id;
            Looks = new List<ItemLook>(looks).AsReadOnly();
        }

        /// <summary>Item id.</summary>
        /// <remarks>Pre: none. Post: valid id. Side effects: none.</remarks>
        public string Id { get; }

        /// <summary>Tagged looks, oldest first.</summary>
        /// <remarks>Pre: none. Post: non-null. Side effects: none.</remarks>
        public IReadOnlyList<ItemLook> Looks { get; }
    }
}
