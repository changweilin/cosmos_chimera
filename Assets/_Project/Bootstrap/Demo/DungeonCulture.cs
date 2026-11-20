using System;
using Cosmimera.Core.Contracts;
using Cosmimera.Core.Random;
using Unity.Mathematics;

namespace Cosmimera.Bootstrap.Demo
{
    /// <summary>
    /// Who built (or last held) one advanced-terrain dungeon: a regional tradition, an era, a minor accent style, a
    /// look variant of the terrain's dressing, and how worn it is. The terrain keeps its wall shapes; the culture
    /// changes the tint, the props on and along the walls, the pillar caps and the wear.
    /// </summary>
    /// <remarks>
    /// REQ: planet-world.md#進階地形地城 (地城文化). Presentation only: never enters a layout hash.
    /// Pre: built by <see cref="For"/> (or default, which reads as a pure Verdant Ancient dungeon, look 0, unworn).
    /// Post: immutable. Side effects: none.
    /// </remarks>
    public readonly struct DungeonCulture
    {
        /// <summary>Number of look variants each terrain's dressing has.</summary>
        public const int Looks = 3;

        private const ulong Salt = 0xD06C0171ul;
        // REQ: planet-world.md#進階地形地城 (地城文化) — 暫定 shares. A dungeon outlives its builders, so Ancient is the
        // single most likely era; the rest lean on the planet's own culture so a dungeon still belongs to its world.
        private const float PlanetRegionShare = 0.55f;
        private const float SecondaryRegionShare = 0.25f;
        private const float AncientShare = 0.40f;
        private const float PlanetEraShare = 0.30f;
        private const float SecondaryEraShare = 0.15f;

        private static readonly ArchEra[] AllEras =
        {
            ArchEra.Primal, ArchEra.Agrarian, ArchEra.Mercantile, ArchEra.Industrial, ArchEra.Ancient, ArchEra.Steampunk,
            ArchEra.Future, ArchEra.Wasteland,
        };

        private DungeonCulture(ArchRegion region, ArchEra era, ArchRegion accentRegion, ArchEra accentEra, float accentShare,
            int look, float age, float damage, ulong seed)
        {
            Region = region;
            Era = era;
            AccentRegion = accentRegion;
            AccentEra = accentEra;
            AccentShare = accentShare;
            Look = look;
            Age = age;
            Damage = damage;
            Seed = seed;
        }

        /// <summary>Regional tradition of the builders (地區文化).</summary>
        /// <remarks>Pre: none. Post: defined. Side effects: none.</remarks>
        public ArchRegion Region { get; }

        /// <summary>Era of the builders (年代文化); Ancient for a dungeon whose builders are long gone.</summary>
        /// <remarks>Pre: none. Post: defined. Side effects: none.</remarks>
        public ArchEra Era { get; }

        /// <summary>Tradition a minority of the props borrow from (later occupants, trade goods).</summary>
        /// <remarks>Pre: none. Post: defined; != Region when built by For. Side effects: none.</remarks>
        public ArchRegion AccentRegion { get; }

        /// <summary>Era a minority of the props borrow from.</summary>
        /// <remarks>Pre: none. Post: defined; != Era when built by For. Side effects: none.</remarks>
        public ArchEra AccentEra { get; }

        /// <summary>Share of era props drawn from the accent style instead of the main one.</summary>
        /// <remarks>Pre: none. Post: in [0, 0.35]. Side effects: none.</remarks>
        public float AccentShare { get; }

        /// <summary>Which of the terrain's <see cref="Looks"/> dressing variants (外觀風格) is used.</summary>
        /// <remarks>Pre: none. Post: in [0, Looks). Side effects: none.</remarks>
        public int Look { get; }

        /// <summary>0 = freshly kept, 1 = centuries of grime (darker, greyer tint).</summary>
        /// <remarks>Pre: none. Post: in [0, 1]. Side effects: none.</remarks>
        public float Age { get; }

        /// <summary>0 = intact, 1 = rubble at every foot, broken tops (share of wall modules that show damage).</summary>
        /// <remarks>Pre: none. Post: in [0, 1]. Side effects: none.</remarks>
        public float Damage { get; }

        /// <summary>Seed the per-module prop draws mix in, so two dungeons of one culture still differ.</summary>
        /// <remarks>Pre: none. Post: none. Side effects: none.</remarks>
        public ulong Seed { get; }

        /// <summary>Draws the culture of the <paramref name="terrainId"/> dungeon on a planet of <paramref name="planet"/>.</summary>
        /// <remarks>
        /// REQ: planet-world.md#進階地形地城 (地城文化).
        /// Pre: terrainId non-null. Post: same arguments → same culture on every client; the region is the planet's
        ///      (55 %), its secondary (25 %) or one of the other four (20 %); the era is Ancient (40 %), the planet's
        ///      (30 %), its secondary (15 %) or any other (15 %); AccentRegion != Region, AccentEra != Era;
        ///      AccentShare in [0.10, 0.35]; Look in [0, Looks); Age and Damage in [0, 1] around the era's usual wear.
        ///      Different terrains on one seed draw independently.
        /// Side effects: none.
        /// </remarks>
        public static DungeonCulture For(in ArchPlanetStyle planet, string terrainId, ulong seed)
        {
            Contract.Requires(terrainId != null, "terrainId required");
            var rng = new Pcg32(Salt ^ seed, Fnv(terrainId));
            // Every draw is taken whether or not it is used, so one axis never shifts the stream of the next.
            float regionRoll = rng.NextFloat();
            int foreignSkip = (int)rng.NextUInt(4);
            float eraRoll = rng.NextFloat();
            int otherEraSkip = (int)rng.NextUInt((uint)AllEras.Length);
            int accentRegionSkip = (int)rng.NextUInt(5);
            int accentEraSkip = (int)rng.NextUInt((uint)AllEras.Length - 1);
            float accentShare = 0.10f + 0.25f * rng.NextFloat();
            int look = (int)rng.NextUInt(Looks);
            float ageNoise = Gauss(ref rng);
            float damageNoise = Gauss(ref rng);
            ulong propSeed = ((ulong)rng.NextUInt() << 32) | rng.NextUInt();

            ArchRegion region = planet.Region;
            if (regionRoll >= PlanetRegionShare + SecondaryRegionShare)
            {
                region = Other(planet.Region, planet.SecondaryRegion, foreignSkip);
            }
            else if (regionRoll >= PlanetRegionShare)
            {
                region = planet.SecondaryRegion;
            }

            ArchEra era;
            if (eraRoll < AncientShare)
            {
                era = ArchEra.Ancient;
            }
            else if (eraRoll < AncientShare + PlanetEraShare)
            {
                era = planet.Era;
            }
            else if (eraRoll < AncientShare + PlanetEraShare + SecondaryEraShare)
            {
                era = planet.SecondaryEra;
            }
            else
            {
                era = OtherEra(otherEraSkip, ArchEra.Ancient, planet.Era, planet.SecondaryEra);
            }

            var accentRegion = (ArchRegion)(((int)region + 1 + accentRegionSkip) % 6);
            ArchEra accentEra = OtherEra(accentEraSkip, era, era, era);
            (float age, float damage) wear = ArchitectureMeshes.EraWear(era);
            // Ancient wear is the ruin's (0.9, 0.9); a dungeon still has standing walls, so it is pulled back.
            if (era == ArchEra.Ancient)
            {
                wear = (0.75f, 0.45f);
            }

            return new DungeonCulture(region, era, accentRegion, accentEra, accentShare, look,
                math.saturate(wear.age + 0.12f * ageNoise), math.saturate(wear.damage + 0.12f * damageNoise), propSeed);
        }

        private static ArchRegion Other(ArchRegion a, ArchRegion b, int skip)
        {
            for (var r = (ArchRegion)(((int)a + 1) % 6); ; r = (ArchRegion)(((int)r + 1) % 6))
            {
                if (r != a && r != b && skip-- == 0)
                {
                    return r;
                }
            }
        }

        private static ArchEra OtherEra(int skip, ArchEra a, ArchEra b, ArchEra c)
        {
            int allowed = 0;
            foreach (ArchEra e in AllEras)
            {
                if (e != a && e != b && e != c)
                {
                    allowed++;
                }
            }

            skip %= allowed;
            foreach (ArchEra e in AllEras)
            {
                if (e != a && e != b && e != c && skip-- == 0)
                {
                    return e;
                }
            }

            throw new InvalidOperationException("unreachable: at least five eras remain");
        }

        // Same bounded Irwin–Hall(4) as ArchitectureMeshes: no log/sqrt whose last bit could differ between clients.
        private static float Gauss(ref Pcg32 rng) =>
            (rng.NextFloat() + rng.NextFloat() + rng.NextFloat() + rng.NextFloat() - 2f) * 1.7320508f;

        // FNV-1a over UTF-16 units: string.GetHashCode is randomised per process and would differ between clients.
        private static ulong Fnv(string s)
        {
            ulong h = 0xCBF29CE484222325ul;
            foreach (char ch in s)
            {
                h = (h ^ ch) * 0x100000001B3ul;
            }

            return h;
        }
    }
}
