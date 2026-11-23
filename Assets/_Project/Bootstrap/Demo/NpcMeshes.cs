using System;
using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.Core.Random;
using Cosmimera.WorldGen;
using Unity.Mathematics;
using UnityEngine;

namespace Cosmimera.Bootstrap.Demo
{
    /// <summary>Everything that decides one resident's mesh.</summary>
    /// <remarks>Pre: built by the constructor or <see cref="NpcMeshes.SpecFor"/>. Post: immutable. Side effects: none.</remarks>
    public readonly struct NpcSpec
    {
        /// <summary>Describes one resident.</summary>
        /// <remarks>
        /// Pre: defined enums; textile01, age01 and health01 in [0, 1]; statureZ and bulkZ in [−3.5, 3.5]; borrowShare in [0, 1];
        ///      no blood line of heritage is race.
        /// Post: properties return the arguments. Side effects: none.
        /// </remarks>
        public NpcSpec(NpcRace race, NpcRole role, NpcSex sex, float age01, float health01, float statureZ, float bulkZ,
            ArchRegion region, ArchEra era, ArchRegion borrowRegion, ArchEra borrowEra, float borrowShare, ulong seed, float textile01 = 0.5f,
            ulong cultureSeed = 0ul, NpcHeritage heritage = default)
        {
            for (int i = 0; i < heritage.BloodLines; i++)
            {
                Contract.Requires(heritage.BloodRace(i) != race, "a blood line differs from the resident's own race");
            }

            Heritage = heritage;
            Contract.Requires(Enum.IsDefined(typeof(NpcRace), race) && Enum.IsDefined(typeof(NpcRole), role) &&
                              Enum.IsDefined(typeof(NpcSex), sex), "race, role and sex must be defined");
            Contract.Requires(Enum.IsDefined(typeof(ArchRegion), region) && Enum.IsDefined(typeof(ArchEra), era) &&
                              Enum.IsDefined(typeof(ArchRegion), borrowRegion) && Enum.IsDefined(typeof(ArchEra), borrowEra),
                "regions and eras must be defined");
            Contract.Requires(age01 >= 0f && age01 <= 1f && health01 >= 0f && health01 <= 1f && borrowShare >= 0f &&
                              borrowShare <= 1f, "age01, health01 and borrowShare in [0, 1]");
            Contract.Requires(math.abs(statureZ) <= 3.5f && math.abs(bulkZ) <= 3.5f, "statureZ and bulkZ in [-3.5, 3.5]");
            Contract.Requires(textile01 >= 0f && textile01 <= 1f, "textile01 in [0, 1]");
            Textile01 = textile01;
            CultureSeed = cultureSeed;
            Race = race;
            Role = role;
            Sex = sex;
            Age01 = age01;
            Health01 = health01;
            StatureZ = statureZ;
            BulkZ = bulkZ;
            Region = region;
            Era = era;
            BorrowRegion = borrowRegion;
            BorrowEra = borrowEra;
            BorrowShare = borrowShare;
            Seed = seed;
        }

        /// <summary>Race.</summary>
        /// <remarks>Pre: none. Post: defined. Side effects: none.</remarks>
        public NpcRace Race { get; }

        /// <summary>Width and dye contrast of the local woven border.</summary>
        /// <remarks>Pre: none. Post: in [0, 1]. Side effects: none.</remarks>
        public float Textile01 { get; }

        /// <summary>Trade.</summary>
        /// <remarks>Pre: none. Post: defined. Side effects: none.</remarks>
        public NpcRole Role { get; }

        /// <summary>Body frame.</summary>
        /// <remarks>Pre: none. Post: defined. Side effects: none.</remarks>
        public NpcSex Sex { get; }

        /// <summary>Age as a share of lifespan.</summary>
        /// <remarks>Pre: none. Post: in [0, 1]. Side effects: none.</remarks>
        public float Age01 { get; }

        /// <summary>Health; low values stoop, bandage and pale the figure.</summary>
        /// <remarks>Pre: none. Post: in [0, 1]. Side effects: none.</remarks>
        public float Health01 { get; }

        /// <summary>Height relative to the race's usual, in standard deviations.</summary>
        /// <remarks>Pre: none. Post: in [−3.5, 3.5]. Side effects: none.</remarks>
        public float StatureZ { get; }

        /// <summary>Girth relative to the race's usual, in standard deviations.</summary>
        /// <remarks>Pre: none. Post: in [−3.5, 3.5]. Side effects: none.</remarks>
        public float BulkZ { get; }

        /// <summary>Tradition the outfit is cut in.</summary>
        /// <remarks>Pre: none. Post: defined. Side effects: none.</remarks>
        public ArchRegion Region { get; }

        /// <summary>Era the outfit is cut in (Ancient dresses as Primal).</summary>
        /// <remarks>Pre: none. Post: defined. Side effects: none.</remarks>
        public ArchEra Era { get; }

        /// <summary>Tradition borrowed garments come from.</summary>
        /// <remarks>Pre: none. Post: defined. Side effects: none.</remarks>
        public ArchRegion BorrowRegion { get; }

        /// <summary>Era borrowed garments come from.</summary>
        /// <remarks>Pre: none. Post: defined. Side effects: none.</remarks>
        public ArchEra BorrowEra { get; }

        /// <summary>Odds that each garment is taken from the borrowed style instead of the settlement's own.</summary>
        /// <remarks>Pre: none. Post: in [0, 1]. Side effects: none.</remarks>
        public float BorrowShare { get; }

        /// <summary>Variation seed (hair, colours, pose, which garments are borrowed).</summary>
        /// <remarks>Pre: none. Post: as constructed. Side effects: none.</remarks>
        public ulong Seed { get; }

        /// <summary>Seed shared by everyone of one settlement: its dye shift, favoured cut, hairstyle and hat habit.</summary>
        /// <remarks>Pre: none. Post: as constructed (0 = a neutral culture). Side effects: none.</remarks>
        public ulong CultureSeed { get; }

        /// <summary>Mixed ancestry and the family cultures the resident carries besides the settlement's.</summary>
        /// <remarks>Pre: none. Post: as constructed (default = pure blood, no family culture); no blood line repeats Race. Side effects: none.</remarks>
        public NpcHeritage Heritage { get; }
    }

    /// <summary>
    /// A resident's other blood lines (混血) and family cultures (跨文化), up to <see cref="MaxLines"/> of each.
    /// REQ: characters.md#路人-npc (混血與跨文化).
    /// </summary>
    /// <remarks>
    /// Blood lines are races besides NpcSpec.Race, which keeps the remaining (dominant) share. Culture lines are a
    /// region × era the family came from; each line's share is the odds that one garment is cut in it.
    /// Pre: built from <see cref="Pure"/> and the With* methods. Post: immutable. Side effects: none.
    /// </remarks>
    public readonly struct NpcHeritage
    {
        /// <summary>Most extra lines of each kind.</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const int MaxLines = 3;

        /// <summary>Most share all blood lines together may take: the resident's own race always keeps half or more.</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const float MaxBloodShare = 0.5f;

        /// <summary>Most share all culture lines together may take: the settlement's styles always keep the rest.</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const float MaxCultureShare = 0.6f;

        private readonly NpcRace _r0, _r1, _r2;
        private readonly float _b0, _b1, _b2;
        private readonly ArchRegion _g0, _g1, _g2;
        private readonly ArchEra _e0, _e1, _e2;
        private readonly float _c0, _c1, _c2;

        private NpcHeritage(int blood, NpcRace r0, NpcRace r1, NpcRace r2, float b0, float b1, float b2, int culture,
            ArchRegion g0, ArchRegion g1, ArchRegion g2, ArchEra e0, ArchEra e1, ArchEra e2, float c0, float c1, float c2)
        {
            BloodLines = blood;
            _r0 = r0;
            _r1 = r1;
            _r2 = r2;
            _b0 = b0;
            _b1 = b1;
            _b2 = b2;
            CultureLines = culture;
            _g0 = g0;
            _g1 = g1;
            _g2 = g2;
            _e0 = e0;
            _e1 = e1;
            _e2 = e2;
            _c0 = c0;
            _c1 = c1;
            _c2 = c2;
        }

        /// <summary>No other blood, no family culture.</summary>
        /// <remarks>Pre: none. Post: BloodLines == CultureLines == 0. Side effects: none.</remarks>
        public static NpcHeritage Pure => default;

        /// <summary>Extra blood lines.</summary>
        /// <remarks>Pre: none. Post: in [0, MaxLines]. Side effects: none.</remarks>
        public int BloodLines { get; }

        /// <summary>Extra culture lines.</summary>
        /// <remarks>Pre: none. Post: in [0, MaxLines]. Side effects: none.</remarks>
        public int CultureLines { get; }

        /// <summary>Sum of the blood shares.</summary>
        /// <remarks>Pre: none. Post: in [0, MaxBloodShare]. Side effects: none.</remarks>
        public float BloodShareTotal => _b0 + _b1 + _b2;

        /// <summary>Sum of the culture shares.</summary>
        /// <remarks>Pre: none. Post: in [0, MaxCultureShare]. Side effects: none.</remarks>
        public float CultureShareTotal => _c0 + _c1 + _c2;

        /// <summary>Race of blood line <paramref name="i"/>.</summary>
        /// <remarks>Pre: 0 ≤ i &lt; BloodLines. Post: defined; differs from every other line. Side effects: none.</remarks>
        public NpcRace BloodRace(int i)
        {
            Contract.Requires(i >= 0 && i < BloodLines, "i in [0, BloodLines)");
            return i == 0 ? _r0 : i == 1 ? _r1 : _r2;
        }

        /// <summary>Share of blood line <paramref name="i"/>.</summary>
        /// <remarks>Pre: 0 ≤ i &lt; BloodLines. Post: in (0, MaxBloodShare]. Side effects: none.</remarks>
        public float BloodShare(int i)
        {
            Contract.Requires(i >= 0 && i < BloodLines, "i in [0, BloodLines)");
            return i == 0 ? _b0 : i == 1 ? _b1 : _b2;
        }

        /// <summary>Region of culture line <paramref name="i"/>.</summary>
        /// <remarks>Pre: 0 ≤ i &lt; CultureLines. Post: defined. Side effects: none.</remarks>
        public ArchRegion CultureRegion(int i)
        {
            Contract.Requires(i >= 0 && i < CultureLines, "i in [0, CultureLines)");
            return i == 0 ? _g0 : i == 1 ? _g1 : _g2;
        }

        /// <summary>Era of culture line <paramref name="i"/>.</summary>
        /// <remarks>Pre: 0 ≤ i &lt; CultureLines. Post: defined. Side effects: none.</remarks>
        public ArchEra CultureEra(int i)
        {
            Contract.Requires(i >= 0 && i < CultureLines, "i in [0, CultureLines)");
            return i == 0 ? _e0 : i == 1 ? _e1 : _e2;
        }

        /// <summary>Share of culture line <paramref name="i"/>.</summary>
        /// <remarks>Pre: 0 ≤ i &lt; CultureLines. Post: in (0, MaxCultureShare]. Side effects: none.</remarks>
        public float CultureShare(int i)
        {
            Contract.Requires(i >= 0 && i < CultureLines, "i in [0, CultureLines)");
            return i == 0 ? _c0 : i == 1 ? _c1 : _c2;
        }

        /// <summary>This heritage plus one blood line.</summary>
        /// <remarks>
        /// Pre: BloodLines &lt; MaxLines; defined race not yet among the lines; share &gt; 0 and BloodShareTotal + share ≤ MaxBloodShare.
        /// Post: a copy with the line appended. Side effects: none.
        /// </remarks>
        public NpcHeritage WithBlood(NpcRace race, float share)
        {
            Contract.Requires(BloodLines < MaxLines, "at most MaxLines blood lines");
            Contract.Requires(Enum.IsDefined(typeof(NpcRace), race), "race must be defined");
            Contract.Requires(share > 0f && BloodShareTotal + share <= MaxBloodShare + 1e-5f, "blood shares in (0, MaxBloodShare] together");
            for (int i = 0; i < BloodLines; i++)
            {
                Contract.Requires(BloodRace(i) != race, "a blood line appears once");
            }

            return BloodLines switch
            {
                0 => new NpcHeritage(1, race, _r1, _r2, share, 0f, 0f, CultureLines, _g0, _g1, _g2, _e0, _e1, _e2, _c0, _c1, _c2),
                1 => new NpcHeritage(2, _r0, race, _r2, _b0, share, 0f, CultureLines, _g0, _g1, _g2, _e0, _e1, _e2, _c0, _c1, _c2),
                _ => new NpcHeritage(3, _r0, _r1, race, _b0, _b1, share, CultureLines, _g0, _g1, _g2, _e0, _e1, _e2, _c0, _c1, _c2),
            };
        }

        /// <summary>This heritage plus one family culture.</summary>
        /// <remarks>
        /// Pre: CultureLines &lt; MaxLines; defined region and era; share &gt; 0 and CultureShareTotal + share ≤ MaxCultureShare.
        /// Post: a copy with the line appended. Side effects: none.
        /// </remarks>
        public NpcHeritage WithCulture(ArchRegion region, ArchEra era, float share)
        {
            Contract.Requires(CultureLines < MaxLines, "at most MaxLines culture lines");
            Contract.Requires(Enum.IsDefined(typeof(ArchRegion), region) && Enum.IsDefined(typeof(ArchEra), era), "region and era must be defined");
            Contract.Requires(share > 0f && CultureShareTotal + share <= MaxCultureShare + 1e-5f, "culture shares in (0, MaxCultureShare] together");
            return CultureLines switch
            {
                0 => new NpcHeritage(BloodLines, _r0, _r1, _r2, _b0, _b1, _b2, 1, region, _g1, _g2, era, _e1, _e2, share, 0f, 0f),
                1 => new NpcHeritage(BloodLines, _r0, _r1, _r2, _b0, _b1, _b2, 2, _g0, region, _g2, _e0, era, _e2, _c0, share, 0f),
                _ => new NpcHeritage(BloodLines, _r0, _r1, _r2, _b0, _b1, _b2, 3, _g0, _g1, region, _e0, _e1, era, _c0, _c1, share),
            };
        }

        /// <summary>Share of <paramref name="race"/> in a resident whose own race is <paramref name="own"/>.</summary>
        /// <remarks>Pre: none. Post: in [0, 1]; own plus every blood line sums to 1. Side effects: none.</remarks>
        public float ShareOf(NpcRace race, NpcRace own)
        {
            float share = race == own ? 1f - BloodShareTotal : 0f;
            for (int i = 0; i < BloodLines; i++)
            {
                share += BloodRace(i) == race ? BloodShare(i) : 0f;
            }

            return share;
        }
    }

    /// <summary>
    /// Asset-free resident figures (P3-18 presentation): one standing figure per RunResident, its silhouette decided by
    /// race, its clothes by the settlement's region × era style mix and the resident's trade, its bearing by age and health.
    /// REQ: characters.md#路人-npc; vision.md#畫風與沉浸感 (chunky silhouettes, flat tones).
    /// </summary>
    /// <remarks>
    /// Stand-in until the art base models arrive (P1-16): the rule layer and this spec stay, the mesh source changes.
    /// Meshes are in metres: +Z = the way the figure faces, +Y = up, origin between the feet. Colours are baked into the
    /// vertices, so every resident shares one white Cosmimera/ToonScenery material (and one glowing one).
    /// Pre: main thread (creates Unity objects). Post: results depend only on the spec. Side effects: allocates.
    /// </remarks>
    public static partial class NpcMeshes
    {
        /// <summary>Sub-mesh of everything that is not self-lit.</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const int BodySubMesh = 0;

        /// <summary>Sub-mesh of self-lit parts (eyes, halos, lamps, light bands).</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const int GlowSubMesh = 1;

        /// <summary>Sub-meshes of every resident mesh.</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const int SubMeshCount = 2;

        /// <summary>Farthest any wing, tail or carried pole reaches from the figure's axis, metres.</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const float MaxReachM = 2.6f;

        private const ulong Salt = 0x4E50C3A11ul;
        // Rounded limbs shade smooth across their 6–8 sides (≥ 0.62 between neighbours); box edges stay hard.
        private const float FacetCos = 0.5f;

        /// <summary>How an era shifts the age and health its settlements average.</summary>
        /// <remarks>
        /// REQ: characters.md#路人-npc (年齡、健康依時代).
        /// Pre: defined era. Post: a valid temper; Ancient reads as neutral. Side effects: none.
        /// </remarks>
        public static PopulaceTemper TemperFor(ArchEra era) => era switch
        {
            ArchEra.Primal => new PopulaceTemper(-40, -40),
            ArchEra.Mercantile => new PopulaceTemper(10, 20),
            ArchEra.Industrial => new PopulaceTemper(0, -30),
            ArchEra.Future => new PopulaceTemper(40, 80),
            ArchEra.Wasteland => new PopulaceTemper(-60, -150),
            _ => new PopulaceTemper(0, 0),
        };

        /// <summary>The mesh spec of <paramref name="resident"/> living in a settlement of the given style.</summary>
        /// <remarks>
        /// Pre: defined region and era; mix built by ArchitectureMeshes.MixFor or the ArchMix constructor.
        /// Post: BorrowShare is the settlement's Weight moved by the resident's StyleZMilli (σ 0.18, clamped to [0, 1]);
        ///       about mix.TertiaryShare of a settlement borrows from the tertiary style, the rest from the secondary;
        ///       CultureSeed is equal for residents of one settlement and mix. Same arguments, same spec. Side effects: none.
        /// </remarks>
        public static NpcSpec SpecFor(in RunResident resident, ArchRegion region, ArchEra era, in ArchMix mix)
        {
            var coin = new Pcg32(Salt ^ resident.Seed, 0x544552ul);
            bool third = coin.NextFloat() < mix.TertiaryShare;
            float share = mix.Weight > 0f ? math.saturate(mix.Weight + 0.18f * resident.StyleZMilli / 1000f) : 0f;
            // The mix is drawn from the planet seed per settlement, so hashing it with the index separates two towns of
            // one planet as well as the same index on two planets.
            ulong key = ((ulong)(uint)resident.Settlement << 32) ^ math.asuint(mix.Weight) ^ ((ulong)math.asuint(mix.AgeMean) << 7) ^
                        ((ulong)region << 40) ^ ((ulong)era << 44) ^ ((ulong)mix.SecondaryRegion << 48) ^ ((ulong)mix.SecondaryEra << 52);
            var mixer = new Pcg32(Salt ^ key, 0x43554Cul);
            ulong culture = ((ulong)mixer.NextUInt() << 32) | mixer.NextUInt();
            return new NpcSpec(resident.Race, resident.Role, resident.Sex, resident.AgePermille / 1000f,
                resident.HealthPermille / 1000f, math.clamp(resident.StatureZMilli / 1000f, -3.5f, 3.5f),
                math.clamp(resident.BulkZMilli / 1000f, -3.5f, 3.5f), region, era,
                third ? mix.TertiaryRegion : mix.SecondaryRegion, third ? mix.TertiaryEra : mix.SecondaryEra, share, resident.Seed, resident.TextilePermille / 1000f, culture,
                HeritageFor(resident.Race, region, era, resident.Seed));
        }

        /// <summary>
        /// Deterministically builds an <see cref="NpcSpec"/> for wandering battle trainer <paramref name="trainer"/>
        /// using the planet's architecture region/era and borrowing the profession's prop <see cref="RunTrainer.PropRole"/>.
        /// </summary>
        /// <remarks>
        /// REQ: docs/design/trainer-battles.md#外觀佔位.
        /// Pre: trainer non-null; region and era defined.
        /// Post: valid NpcSpec with Role == trainer.PropRole and deterministic per trainer.Seed and planetSeed.
        /// Side effects: none.
        /// </remarks>
        public static NpcSpec SpecForTrainer(RunTrainer trainer, ArchRegion region, ArchEra era, ulong planetSeed)
        {
            Contract.Requires(trainer != null, "trainer must not be null");
            Contract.Requires(Enum.IsDefined(typeof(ArchRegion), region) && Enum.IsDefined(typeof(ArchEra), era),
                "region and era must be defined");
            var rng = new Pcg32(trainer.Seed ^ planetSeed, 'T');
            var race = (NpcRace)rng.NextUInt((uint)RaceCount);
            var sex = (NpcSex)rng.NextUInt(2);
            float age01 = 0.22f + rng.NextFloat() * 0.50f;
            float health01 = 0.75f + rng.NextFloat() * 0.25f;
            float statureZ = (rng.NextFloat() * 2f - 1f) * 1.5f;
            float bulkZ = (rng.NextFloat() * 2f - 1f) * 1.5f;
            var borrowRegion = (ArchRegion)rng.NextUInt(RegionCount);
            ArchEra borrowEra = WornEras[rng.NextUInt((uint)WornEras.Length)];
            float borrowShare = 0.1f + rng.NextFloat() * 0.2f;
            float textile01 = rng.NextFloat();
            ulong culture = ((ulong)rng.NextUInt() << 32) | rng.NextUInt();
            return new NpcSpec(race, trainer.PropRole, sex, age01, health01, statureZ, bulkZ,
                region, era, borrowRegion, borrowEra, borrowShare, trainer.Seed, textile01, culture,
                HeritageFor(race, region, era, trainer.Seed));
        }

        /// <summary>Placeholder figure of a terrain trial master: the same on every planet.</summary>
        /// <remarks>
        /// REQ: docs/design/trainer-battles.md#外觀佔位 — seeded by the character id hash, so region and era come from the
        /// look seed rather than the planet.
        /// Pre: none. Post: valid NpcSpec, an adult Guard in good health; depends on lookSeed only. Side effects: none.
        /// </remarks>
        public static NpcSpec SpecForTrialMaster(ulong lookSeed)
        {
            var rng = new Pcg32(lookSeed, 0x54524Dul);
            var race = (NpcRace)rng.NextUInt((uint)RaceCount);
            var sex = (NpcSex)rng.NextUInt(2);
            var region = (ArchRegion)rng.NextUInt(RegionCount);
            ArchEra era = WornEras[rng.NextUInt((uint)WornEras.Length)];
            float age01 = 0.35f + rng.NextFloat() * 0.35f;
            float statureZ = 0.5f + rng.NextFloat();
            float bulkZ = (rng.NextFloat() * 2f - 1f) * 1.2f;
            var borrowRegion = (ArchRegion)rng.NextUInt(RegionCount);
            ArchEra borrowEra = WornEras[rng.NextUInt((uint)WornEras.Length)];
            float textile01 = rng.NextFloat();
            ulong culture = ((ulong)rng.NextUInt() << 32) | rng.NextUInt();
            return new NpcSpec(race, NpcRole.Guard, sex, age01, 1f, statureZ, bulkZ, region, era, borrowRegion, borrowEra,
                0.2f, lookSeed, textile01, culture, HeritageFor(race, region, era, lookSeed));
        }

        /// <summary>Odds a resident carries one or more other blood lines (混血).</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const float MixedBloodOdds = 0.15f;

        /// <summary>Odds a resident's family carries one or more cultures from elsewhere (跨文化).</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const float FamilyCultureOdds = 0.25f;

        /// <summary>Odds of each further line once one is drawn: every added element is rarer than the last.</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const float FurtherLineOdds = 0.3f;

        /// <summary>The blood lines and family cultures of the resident with <paramref name="seed"/>.</summary>
        /// <remarks>
        /// REQ: characters.md#路人-npc (混血與跨文化).
        /// Pre: defined race, region and era.
        /// Post: P(k blood lines) = MixedBloodOdds × FurtherLineOdds^(k−1) × (1 − FurtherLineOdds) for k &lt; MaxLines (likewise
        ///       culture lines with FamilyCultureOdds); undead (Zombie, Skeleton) neither carry nor give blood; blood races
        ///       are distinct, differ from <paramref name="race"/> and come with shares largest first, each ≥ 1/16; no
        ///       culture line equals (region, Worn(era)) and none is Ancient. Same arguments, same heritage.
        /// Side effects: none.
        /// </remarks>
        public static NpcHeritage HeritageFor(NpcRace race, ArchRegion region, ArchEra era, ulong seed)
        {
            Contract.Requires(Enum.IsDefined(typeof(NpcRace), race) && Enum.IsDefined(typeof(ArchRegion), region) &&
                              Enum.IsDefined(typeof(ArchEra), era), "race, region and era must be defined");
            // Own stream: adding heritage did not reshuffle any other draw of the resident.
            var rng = new Pcg32(Salt ^ seed, 0x484552ul);
            NpcHeritage h = NpcHeritage.Pure;
            int blood = Lines(MixedBloodOdds, ref rng);
            if (Undead(race))
            {
                blood = 0;
            }

            float[] bloodShares = Shares(blood, NpcHeritage.MaxBloodShare, ref rng);
            var taken = new bool[RaceCount];
            taken[(int)race] = true;
            for (int i = 0; i < blood; i++)
            {
                NpcRace pick = PickLiving(taken, ref rng);
                taken[(int)pick] = true;
                h = h.WithBlood(pick, bloodShares[i]);
            }

            int cultures = Lines(FamilyCultureOdds, ref rng);
            float[] cultureShares = Shares(cultures, NpcHeritage.MaxCultureShare, ref rng);
            for (int i = 0; i < cultures; i++)
            {
                ArchRegion g;
                ArchEra e;
                do
                {
                    g = (ArchRegion)rng.NextUInt(RegionCount);
                    e = WornEras[rng.NextUInt((uint)WornEras.Length)];
                }
                while (g == region && e == Worn(era));

                h = h.WithCulture(g, e, cultureShares[i]);
            }

            return h;
        }

        private static readonly int RaceCount = Enum.GetValues(typeof(NpcRace)).Length;
        private static readonly uint RegionCount = (uint)Enum.GetValues(typeof(ArchRegion)).Length;

        private static readonly ArchEra[] WornEras =
        {
            ArchEra.Primal, ArchEra.Agrarian, ArchEra.Mercantile, ArchEra.Industrial, ArchEra.Steampunk, ArchEra.Future, ArchEra.Wasteland,
        };

        private static bool Undead(NpcRace race) => race == NpcRace.Zombie || race == NpcRace.Skeleton;

        private static int Lines(float first, ref Pcg32 rng)
        {
            if (rng.NextFloat() >= first)
            {
                return 0;
            }

            int lines = 1;
            while (lines < NpcHeritage.MaxLines && rng.NextFloat() < FurtherLineOdds)
            {
                lines++;
            }

            return lines;
        }

        // Each line keeps at least 1/16 (a great-great-grandparent) so a third line is never invisible noise; the rest is
        // split by uniform weights and sorted so line 0 is always the strongest.
        private static float[] Shares(int lines, float cap, ref Pcg32 rng)
        {
            var shares = new float[lines];
            if (lines == 0)
            {
                return shares;
            }

            const float Floor = 1f / 16f;
            float total = math.lerp(Floor * (lines + 1), cap, rng.NextFloat());
            float sum = 0f;
            for (int i = 0; i < lines; i++)
            {
                shares[i] = 0.5f + rng.NextFloat();
                sum += shares[i];
            }

            for (int i = 0; i < lines; i++)
            {
                shares[i] = Floor + (total - Floor * lines) * shares[i] / sum;
            }

            Array.Sort(shares);
            Array.Reverse(shares);
            return shares;
        }

        private static NpcRace PickLiving(bool[] taken, ref Pcg32 rng)
        {
            uint open = 0;
            for (int r = 0; r < RaceCount; r++)
            {
                open += taken[r] || Undead((NpcRace)r) ? 0u : 1u;
            }

            uint pick = rng.NextUInt(open);
            for (int r = 0; r < RaceCount; r++)
            {
                if (taken[r] || Undead((NpcRace)r))
                {
                    continue;
                }

                if (pick-- == 0)
                {
                    return (NpcRace)r;
                }
            }

            return NpcRace.Human;
        }

        /// <summary>Standing height of <paramref name="spec"/> to the top of the skull, metres.</summary>
        /// <remarks>Pre: none. Post: in [0.5, 2.7]; children are shorter, the old and the ill a little. Side effects: none.</remarks>
        public static float HeightM(in NpcSpec spec)
        {
            Traits t = TraitsOf(spec);
            float grown = math.lerp(0.52f, 1f, math.smoothstep(0.03f, 0.25f, spec.Age01));
            float sex = spec.Sex == NpcSex.Female ? 0.95f : 1f;
            float old = 1f - 0.04f * math.smoothstep(0.6f, 0.9f, spec.Age01);
            return math.clamp(t.HeightM * grown * sex * old * (1f + 0.05f * spec.StatureZ), 0.5f, 2.7f);
        }

        /// <summary>Builds the mesh of <paramref name="spec"/>.</summary>
        /// <remarks>
        /// Pre: main thread.
        /// Post: a new mesh with normals and colours and exactly <see cref="SubMeshCount"/> sub-meshes; the same spec
        ///       always gives the same vertices; nothing reaches below the origin, above HeightM + 1.4 m (hats, horns,
        ///       halos, carried poles) or farther than <see cref="MaxReachM"/> from the vertical axis.
        /// Side effects: allocates a Mesh the caller owns (and must destroy).
        /// </remarks>
        public static Mesh Build(in NpcSpec spec) => Build(spec, null, out _);

        internal static Mesh Build(in NpcSpec spec, Transform root, out ResidentStride stride) => Build(spec, root, out stride, false, out _);

        /// <summary>Builds only what a UMA body does not carry: hair, beard, clothes, race parts, wings, tail, props.</summary>
        /// <remarks>
        /// REQ: docs/design/npc-uma-looks.md#多樣性對照 (items 10–13, 15, 17, 20). Beast races keep their whole procedural head.
        /// Pre: main thread. Post: the triangles of Build(spec) that are not body or (plain-faced) face, at the same
        /// positions; <paramref name="rig"/> is the pose they were built around. Side effects: allocates a Mesh the caller owns.
        /// </remarks>
        internal static Mesh BuildParts(in NpcSpec spec, out Rig rig) => Build(spec, null, out _, true, out rig);

        private static Mesh Build(in NpcSpec spec, Transform root, out ResidentStride stride, bool partsOnly, out Rig posed)
        {
            var rng = new Pcg32(Salt ^ spec.Seed, ((ulong)spec.Race << 16) | ((ulong)spec.Role << 8) | (ulong)spec.Sex);
            var body = new Fig();
            var glow = new Fig();
            Traits traits = TraitsOf(spec);
            Look look = Look.Draw(spec, traits, ref rng);
            Rig rig = Rig.Pose(spec, traits, look, ref rng);
            posed = rig;
            Mark(body, glow, false);
            Flesh(body, glow, spec, traits, rig, look);
            Head(body, glow, spec, traits, rig, look, ref rng);
            Mark(body, glow, true);
            Ride(body, glow, RideBody);
            Dress(body, glow, spec, rig, look, ref rng);
            Ride(body, glow, RideFree);
            Trade(body, glow, spec, rig, look);
            Ailments(body, spec, rig, look, ref rng);

            float top = HeightM(spec) + 1.4f;
            Confine(body, top);
            Confine(glow, top);
            if (partsOnly)
            {
                body.KeepPartsOnly();
                glow.KeepPartsOnly();
            }

            var data = new SceneryMeshes.MeshData(SubMeshCount);
            data.EmitFaceted(body.Part, BodySubMesh, FacetCos, null);
            data.EmitFaceted(glow.Part, GlowSubMesh, FacetCos, null);
            Mesh mesh = data.ToMesh("Npc_" + spec.Race + "_" + spec.Role);
            stride = root == null ? null : new ResidentStride(root, mesh, rig.HipL, rig.HipR, rig.KneeL, rig.KneeR, rig.AnkleL, rig.AnkleR, spec);
            // UV1 carries construction tags through facet splitting. It belongs to wind in the scenery shader; a parts
            // mesh keeps it as the rider tag its consumer reads.
            if (!partsOnly)
            {
                mesh.SetUVs(1, Array.Empty<Vector2>());
            }

            return mesh;
        }

        private static void Mark(Fig body, Fig glow, bool part)
        {
            body.Mark(part);
            glow.Mark(part);
        }

        /// <summary>Rider tag (UV1.x of a BuildParts mesh): the part may ride any bone.</summary>
        internal const byte RideFree = 0;

        /// <summary>Rider tag: clothing of the trunk and legs, which never rides an arm however close it hangs.</summary>
        internal const byte RideBody = 1;

        /// <summary>Rider tag: a sleeve, cuff or glove of the left (negative x) arm.</summary>
        internal const byte RideArmLeft = 2;

        /// <summary>Rider tag: a sleeve, cuff or glove of the right arm.</summary>
        internal const byte RideArmRight = 3;

        private static void Ride(Fig body, Fig glow, byte rider)
        {
            body.Ride(rider);
            glow.Ride(rider);
        }

        // The contract's envelope, enforced rather than hoped for: every pose and prop is authored inside it, and a
        // stray combination (tall race × long pole × lean) is pulled back instead of poking through a collider budget.
        private static void Confine(Fig fig, float top)
        {
            for (int i = 0; i < fig.Part.P.Count; i++)
            {
                float3 p = fig.Part.P[i];
                p.y = math.clamp(p.y, 0f, top);
                float reach = math.length(p.xz);
                if (reach > MaxReachM)
                {
                    p.xz *= MaxReachM / reach;
                }

                fig.Part.P[i] = p;
            }
        }

        // ---- races ---------------------------------------------------------------------------------------------

        internal struct Traits
        {
            public float HeightM;   // adult male, crown of the skull
            public float Head;      // head size relative to a human's
            public float LegShare;  // legs as a share of everything below the neck
            public float Shoulder;  // half shoulder width / height
            public float Bulk;      // limb and trunk thickness
            public float Arm;       // arm length relative to a human's
            public float Hunch;     // forward lean of the spine, radians
            public bool Furred;     // no head hair, no bare-skin garments logic (fur, feathers, scales, bone)
            public float Fat;       // mean body-fat share of an adult male; women carry ~6 points more
        }

        private static Traits TraitsFor(NpcRace race) => race switch
        {
            NpcRace.Elf => new Traits { HeightM = 1.8f, Head = 0.96f, LegShare = 0.62f, Shoulder = 0.115f, Bulk = 0.86f, Arm = 1.02f, Fat = 0.14f },
            NpcRace.Dwarf => new Traits { HeightM = 1.28f, Head = 1.3f, LegShare = 0.5f, Shoulder = 0.175f, Bulk = 1.4f, Arm = 1f, Fat = 0.28f },
            NpcRace.Goblin => new Traits { HeightM = 1.12f, Head = 1.4f, LegShare = 0.52f, Shoulder = 0.13f, Bulk = 0.9f, Arm = 1.15f, Hunch = 0.12f, Fat = 0.16f },
            NpcRace.Orc => new Traits { HeightM = 1.98f, Head = 1.05f, LegShare = 0.56f, Shoulder = 0.165f, Bulk = 1.35f, Arm = 1.15f, Hunch = 0.14f, Fat = 0.2f },
            NpcRace.Avian => new Traits { HeightM = 1.66f, Head = 1f, LegShare = 0.6f, Shoulder = 0.115f, Bulk = 0.85f, Arm = 1f, Furred = true, Fat = 0.12f },
            NpcRace.Merfolk => new Traits { HeightM = 1.76f, Head = 1f, LegShare = 0.6f, Shoulder = 0.125f, Bulk = 0.95f, Arm = 1.03f, Furred = true, Fat = 0.24f },
            NpcRace.Lizardfolk => new Traits { HeightM = 1.82f, Head = 1f, LegShare = 0.57f, Shoulder = 0.125f, Bulk = 1f, Arm = 1.05f, Hunch = 0.2f, Furred = true, Fat = 0.12f },
            NpcRace.Dragonkin => new Traits { HeightM = 2.08f, Head = 1.02f, LegShare = 0.58f, Shoulder = 0.16f, Bulk = 1.3f, Arm = 1.05f, Hunch = 0.1f, Furred = true, Fat = 0.16f },
            NpcRace.Zombie => new Traits { HeightM = 1.68f, Head = 1f, LegShare = 0.6f, Shoulder = 0.12f, Bulk = 0.92f, Arm = 1f, Hunch = 0.3f, Fat = 0.1f },
            NpcRace.Skeleton => new Traits { HeightM = 1.7f, Head = 1f, LegShare = 0.6f, Shoulder = 0.12f, Bulk = 0.42f, Arm = 1f, Hunch = 0.05f, Furred = true, Fat = 0.18f },
            NpcRace.Werewolf => new Traits { HeightM = 1.96f, Head = 1.08f, LegShare = 0.56f, Shoulder = 0.16f, Bulk = 1.25f, Arm = 1.18f, Hunch = 0.24f, Furred = true, Fat = 0.15f },
            NpcRace.Vampire => new Traits { HeightM = 1.84f, Head = 0.97f, LegShare = 0.62f, Shoulder = 0.12f, Bulk = 0.88f, Arm = 1.02f, Fat = 0.1f },
            NpcRace.Angel => new Traits { HeightM = 1.86f, Head = 0.97f, LegShare = 0.62f, Shoulder = 0.125f, Bulk = 0.95f, Arm = 1f, Fat = 0.16f },
            NpcRace.Demon => new Traits { HeightM = 1.92f, Head = 1f, LegShare = 0.6f, Shoulder = 0.14f, Bulk = 1.1f, Arm = 1.05f, Fat = 0.17f },
            _ => new Traits { HeightM = 1.72f, Head = 1f, LegShare = 0.6f, Shoulder = 0.125f, Bulk = 1f, Arm = 1f, Fat = 0.22f },
        };

        // A mixed resident's frame is the share-weighted mean of every blood line's; scales and fur (Furred) stay the own
        // race's, since a half-elf dwarf still grows a beard where a half-dwarf avian has nowhere to grow one.
        // REQ: characters.md#路人-npc (混血與跨文化).
        private static Traits TraitsOf(in NpcSpec spec)
        {
            Traits t = TraitsFor(spec.Race);
            NpcHeritage h = spec.Heritage;
            if (h.BloodLines == 0)
            {
                return t;
            }

            float own = 1f - h.BloodShareTotal;
            var m = new Traits
            {
                HeightM = t.HeightM * own, Head = t.Head * own, LegShare = t.LegShare * own, Shoulder = t.Shoulder * own,
                Bulk = t.Bulk * own, Arm = t.Arm * own, Hunch = t.Hunch * own, Fat = t.Fat * own, Furred = t.Furred,
            };
            for (int i = 0; i < h.BloodLines; i++)
            {
                Traits o = TraitsFor(h.BloodRace(i));
                float k = h.BloodShare(i);
                m.HeightM += o.HeightM * k;
                m.Head += o.Head * k;
                m.LegShare += o.LegShare * k;
                m.Shoulder += o.Shoulder * k;
                m.Bulk += o.Bulk * k;
                m.Arm += o.Arm * k;
                m.Hunch += o.Hunch * k;
                m.Fat += o.Fat * k;
            }

            return m;
        }

        private static readonly int[][] SkinTones =
        {
            new[] { 0xF2D2B6, 0xE0B08A, 0xC68E63, 0x9A6541, 0x6E4630, 0xF6DEC8 },
            new[] { 0xF4E3CF, 0xE8CDB0, 0xC9A27A, 0xD8E2C8 },
            new[] { 0xE8BE9A, 0xD49B74, 0xB37A55 },
            new[] { 0x8FB04A, 0x6E9A3E, 0xA9B85A, 0x5F8F6A },
            new[] { 0x6F8F4F, 0x587A52, 0x7F8A6A, 0x8A6F5A },
            new[] { 0xF0E6D2, 0x8A5A3A, 0x3F4E6A, 0xC9483A, 0xE8C24A, 0x4E8A7A },
            new[] { 0x5FB4C9, 0x3F8FB8, 0x6FC9A8, 0x8A7AC9 },
            new[] { 0x6FA05A, 0xB8A05A, 0x4F8A7A, 0xA8683A },
            new[] { 0xB8402E, 0x2E5FA8, 0x3A3A44, 0xC9A23A, 0x4E9A5A },
            new[] { 0x8FA08A, 0x7A8F7F, 0xA0A58A },
            new[] { 0xE8E2D0, 0xD8D0B8 },
            new[] { 0x6A5A4A, 0x8A8A8E, 0x3E3A3A, 0xB89A6A, 0xE4E0D8 },
            new[] { 0xE6E0E4, 0xD8D4E0, 0xC9C0CC },
            new[] { 0xF8E8D8, 0xF0D8C0, 0xE0B890, 0xB88A60 },
            new[] { 0xB83A3A, 0x7A3A8A, 0x3A3A5A, 0xC96A3A },
        };

        private static readonly int[] HairTones = { 0x2A2018, 0x4A2E1C, 0x7A4A26, 0xB8863C, 0xE0C070, 0xC8553A, 0x1A1A20 };

        // Per region: three wardrobes of (cloth, second cloth, accent), taken from the roofs, walls and trim of the
        // same region's buildings so a crowd belongs to its street.
        private static readonly int[][] Wardrobe =
        {
            new[] { 0x5E7F45, 0xD8C8A0, 0xA4553A, 0x8A5A3A, 0xE8DCC0, 0x6E8F45, 0x3F6A5A, 0xC9A95A, 0xE8DCC0 },
            new[] { 0x8E3B2E, 0x3A2E2E, 0xFF8A3C, 0x5A4E50, 0xC07A3A, 0x8E3B2E, 0x2E2E33, 0x7A5A48, 0xE0A040 },
            new[] { 0x2F5D6B, 0xE6EEF2, 0x8A6A52, 0x7C8A96, 0xF4F8FB, 0x34424E, 0x8A6A52, 0xE9F1F6, 0x2F5D6B },
            new[] { 0x3B4470, 0x8E97A3, 0x4FA08A, 0x6F7785, 0x2B3040, 0xD0A040, 0x4FA08A, 0xA89C86, 0x3B4470 },
            new[] { 0x6A4FA8, 0xD9B98A, 0x3E8E9A, 0xC9A070, 0x4A3A78, 0xE0A8C8, 0x3E8E9A, 0xD9B98A, 0x6A4FA8 },
            new[] { 0x232A52, 0x6E6488, 0xD8B45A, 0x544C6A, 0xD8B45A, 0x232A52, 0x6E6488, 0x1C2040, 0xFFC86A },
        };

        // Natural fibres before dye: linen, oat, ash wool, walnut, charcoal felt.
        private static readonly int[] Undyed = { 0xE6DCC4, 0xC9BFA8, 0x8A8478, 0x6A5442, 0x3A3430 };

        // Hair dyes a town might take up: rust, plum, teal, ink blue, bleach, moss.
        private static readonly int[] DyeTones = { 0xB8462E, 0x6A2E5A, 0x2E8A8A, 0x2A3A7A, 0xE8E0C0, 0x5A7A3A };

        private static readonly int[] RegionGlow = { 0xFFD27A, 0xFF8A3C, 0xFFE0A0, 0xBFE8FF, 0x9FF0E0, 0xFFC86A };

        // ---- one resident's draw -------------------------------------------------------------------------------

        internal struct Look
        {
            public Color Skin, Hair, Cloth, Cloth2, Accent, Leather, Metal, Glow, Bone;
            public ArchEra UpperEra, LowerEra, HeadEra;
            public ArchRegion PieceRegion;
            public Cut Cut;
            public float Muscle;     // z, relative to the race
            public float Fat;        // body-fat share
            public bool Skirt;       // a skirt or robe instead of trousers
            public float Fall;       // skirt length as a share of the leg
            public bool Piece;       // wears the region's signature piece
            public bool Hat;         // wears the era's headwear
            public int HairStyle;    // 0 cropped, 1 long, 2 bun, 3 tail, 4 bald, 5 braids, 6 crest, 7 topknot, 8 volume
            public bool Beard;
            public int BeardStyle;   // 0 full, 1 close-trimmed, 2 goatee, 3 forked, 4 braided, 5 moustache
            public float BeardLength; // head radii below the chin
            public float Stoop;      // extra forward lean from age and ill health, radians
            public bool Cane;
            public bool Bandaged;
            public float Grown;      // 0.52 small child .. 1 adult
            public bool Child;

            public static Look Draw(in NpcSpec spec, in Traits traits, ref Pcg32 rng)
            {
                var l = default(Look);
                ArchEra era = Worn(spec.Era), borrowEra = Worn(spec.BorrowEra);
                // Each garment on its own coin, so two people of one mixed town still differ in what they borrowed.
                // A family culture (跨文化) claims a garment before the town's mix is consulted: grandmother's shawl is
                // worn whatever the street wears. Pure residents draw nothing extra here.
                l.UpperEra = GarmentEra(spec, era, borrowEra, ref rng);
                l.LowerEra = GarmentEra(spec, era, borrowEra, ref rng);
                l.HeadEra = GarmentEra(spec, era, borrowEra, ref rng);
                int pieceLine = FamilyLine(spec.Heritage, ref rng);
                l.PieceRegion = pieceLine >= 0 ? spec.Heritage.CultureRegion(pieceLine)
                    : rng.NextFloat() < spec.BorrowShare ? spec.BorrowRegion : spec.Region;
                int paletteLine = FamilyLine(spec.Heritage, ref rng);
                ArchRegion palette = paletteLine >= 0 ? spec.Heritage.CultureRegion(paletteLine)
                    : rng.NextFloat() < spec.BorrowShare * 0.7f ? spec.BorrowRegion : spec.Region;

                Culture culture = Culture.For(spec.CultureSeed, l.UpperEra);
                bool young = spec.Age01 < 0.16f;
                int[] tones = SkinTones[(int)spec.Race];
                // A blend of two neighbouring tones: a continuum, not six paint chips.
                int tone = (int)rng.NextUInt((uint)tones.Length);
                Color skin = Color.Lerp(Hex(tones[tone]), Hex(tones[math.min(tone + 1, tones.Length - 1)]), rng.NextFloat() * 0.6f);
                NpcHeritage blood = spec.Heritage;
                for (int i = 0; i < blood.BloodLines; i++)
                {
                    // Each blood line pulls the skin toward one of its own tones by its share; 0.85 keeps the own race's
                    // tone readable even at half blood, so a half-merfolk human is sea-tinted, not merfolk blue.
                    skin = Color.Lerp(skin, Pick(SkinTones[(int)blood.BloodRace(i)], ref rng), 0.85f * blood.BloodShare(i));
                }
                // Illness drains colour before it bends the back.
                float pallor = (1f - spec.Health01) * 0.4f;
                l.Skin = Color.Lerp(skin, Hex(0xB8B4A8), pallor);
                l.Bone = Hex(0xE8E2D0);

                int hairPick = (int)rng.NextUInt((uint)HairTones.Length);
                Color hair = RaceHairTone(spec.Race, hairPick, ref rng);
                for (int i = 0; i < blood.BloodLines; i++)
                {
                    // Hair is inherited whole, not mixed: a quarter-elf is dark-haired or silver-haired, never grey-ish.
                    if (rng.NextFloat() < blood.BloodShare(i))
                    {
                        hair = RaceHairTone(blood.BloodRace(i), hairPick, ref rng);
                    }
                }
                // Dyed hair is an adult choice, and the undead and the celestial keep the colour they were made with.
                bool dyeable = !young && spec.Race != NpcRace.Zombie && spec.Race != NpcRace.Angel;
                if (dyeable && rng.NextFloat() < culture.DyedHairOdds)
                {
                    hair = Retint(Hex(DyeTones[rng.NextUInt((uint)DyeTones.Length)]), culture.HueShift, culture.SatScale, 0f);
                }

                bool ageless = spec.Race == NpcRace.Vampire || spec.Race == NpcRace.Angel || spec.Race == NpcRace.Demon;
                float grey = ageless ? 0f : math.smoothstep(0.5f, 0.82f, spec.Age01);
                l.Hair = Color.Lerp(hair, Hex(0xD8D6D0), grey);

                int set = math.min(2, (int)(3f * math.saturate(spec.Textile01 + 0.10f * Gauss(ref rng)))) * 3;
                int[] wardrobe = Wardrobe[(int)palette];
                Color cloth = Hex(wardrobe[set]), cloth2 = Hex(wardrobe[set + 1]), accent = Hex(wardrobe[set + 2]);
                // The era decides what the cloth is made of; the region still shows through as its dye.
                switch (l.UpperEra)
                {
                    case ArchEra.Primal:
                        cloth = Color.Lerp(cloth, Hex(0x9A7350), 0.6f);
                        cloth2 = Color.Lerp(cloth2, Hex(0xC8B08A), 0.5f);
                        break;
                    case ArchEra.Industrial:
                        cloth = Color.Lerp(cloth, Hex(0x3A3A40), 0.35f);
                        break;
                    case ArchEra.Steampunk:
                        cloth2 = Color.Lerp(cloth2, Hex(0x6A4428), 0.45f);
                        accent = Color.Lerp(accent, Hex(0xC8963C), 0.6f);
                        break;
                    case ArchEra.Future:
                        cloth = Color.Lerp(cloth, Hex(0xE4EAEE), 0.55f);
                        cloth2 = Color.Lerp(cloth2, Hex(0x36405A), 0.5f);
                        break;
                    case ArchEra.Wasteland:
                        cloth = Color.Lerp(cloth, Hex(0x8A7A66), 0.5f);
                        cloth2 = Color.Lerp(cloth2, Hex(0x5A4E44), 0.5f);
                        break;
                }

                // Undyed cloth is what most of a crowd wears under the colour; without it everyone is in full livery.
                float roll = rng.NextFloat();
                if (roll < 0.22f)
                {
                    (cloth, cloth2) = (cloth2, cloth);
                }
                else if (roll < 0.42f)
                {
                    cloth2 = Hex(Undyed[rng.NextUInt((uint)Undyed.Length)]);
                }

                // The town shifts the whole wardrobe one way; dye lots never match, so each person drifts a little more.
                float hue = culture.HueShift + math.clamp(0.012f * Gauss(ref rng), -0.035f, 0.035f);
                float value = math.clamp(0.07f * Gauss(ref rng), -0.2f, 0.15f);
                l.Cloth = Retint(cloth, hue, culture.SatScale, value);
                l.Cloth2 = Retint(cloth2, hue * 0.5f, culture.SatScale, value);
                l.Accent = Retint(accent, culture.HueShift, culture.SatScale, 0f);
                // Same-value top and bottom merge into one sack at 40 m: keep a readable step between them.
                if (math.abs(Value(l.Cloth) - Value(l.Cloth2)) < 0.12f)
                {
                    l.Cloth2 = Value(l.Cloth) > 0.45f ? Shade(l.Cloth2, 0.68f) : Color.Lerp(l.Cloth2, Hex(0xE6DCC4), 0.45f);
                }

                l.Leather = Shade(Hex(0x5A3E2B), math.clamp(1f + 0.1f * Gauss(ref rng), 0.7f, 1.3f));
                l.Metal = l.UpperEra == ArchEra.Steampunk ? Hex(0xC8963C)
                    : l.UpperEra == ArchEra.Future ? Hex(0xB8C4D0)
                    : l.UpperEra == ArchEra.Wasteland ? Hex(0x7A5A44)
                    : Hex(0x8A8E96);
                l.Glow = l.UpperEra == ArchEra.Future ? Color.Lerp(Hex(0x7FF2FF), Hex(RegionGlow[(int)palette]), 0.35f)
                    : Hex(RegionGlow[(int)palette]);

                l.Grown = math.lerp(0.52f, 1f, math.smoothstep(0.03f, 0.25f, spec.Age01));
                l.Child = young;
                l.Cut = PickCut(spec, l, culture, ref rng);
                l.Skirt = l.Cut == Cut.Robe || l.Cut == Cut.Dress || l.Cut == Cut.Wrap;
                l.Fall = l.Cut == Cut.Robe ? 0.93f
                    : l.Cut == Cut.Wrap ? math.lerp(0.3f, 0.45f, rng.NextFloat())
                    : math.lerp(0.5f, 0.92f, rng.NextFloat());
                l.Piece = rng.NextFloat() < 0.7f;
                l.Hat = rng.NextFloat() < culture.HatOdds;
                l.HairStyle = PickHair(spec, culture, grey, ref rng);
                (l.Muscle, l.Fat) = Physique(spec, traits, young, ageless, ref rng);
                // Beards follow the blood that grows them (a half-dwarf elf may well wear one); fur and scales grow none.
                float humanBeard = 0.3f + 0.4f * grey;
                float beardOdds = traits.Furred ? 0f
                    : 0.95f * blood.ShareOf(NpcRace.Dwarf, spec.Race) + humanBeard * blood.ShareOf(NpcRace.Human, spec.Race);
                l.Beard = spec.Sex == NpcSex.Male && !l.Child && spec.Age01 > 0.25f && rng.NextFloat() < beardOdds;
                if (l.Beard)
                {
                    bool dwarf = blood.ShareOf(NpcRace.Dwarf, spec.Race) >= 0.5f;
                    // A dwarf's beard is worn long and dressed; a human's is as often trimmed to nothing much.
                    l.BeardStyle = Weighted(dwarf ? BeardOddsDwarf : BeardOddsHuman, ref rng);
                    l.BeardLength = dwarf ? math.lerp(1.3f, 2.4f, rng.NextFloat()) : math.lerp(0.5f, 1f, rng.NextFloat());
                }

                float old = math.smoothstep(0.62f, 0.92f, spec.Age01);
                float ill = 1f - math.smoothstep(0.2f, 0.65f, spec.Health01);
                l.Stoop = (ageless ? 0f : 0.2f * old) + 0.22f * ill;
                l.Cane = !l.Child && (spec.Health01 < 0.35f || (old > 0.5f && spec.Health01 < 0.6f));
                l.Bandaged = spec.Health01 < 0.45f && spec.Race != NpcRace.Skeleton;
                return l;
            }
        }

        private static Color RaceHairTone(NpcRace race, int hairPick, ref Pcg32 rng) => race switch
        {
            NpcRace.Elf => rng.NextUInt(3) == 0 ? Hex(0xE8E4D8) : Hex(HairTones[hairPick]),
            NpcRace.Angel => rng.NextUInt(2) == 0 ? Hex(0xF4EFE0) : Hex(0xE0C070),
            NpcRace.Vampire => rng.NextUInt(3) == 0 ? Hex(0xE4E0E8) : Hex(0x1A1A20),
            NpcRace.Demon => Hex(0x1A1A20),
            NpcRace.Goblin => Hex(0x2A2018),
            NpcRace.Zombie => Color.Lerp(Hex(HairTones[hairPick]), Hex(0x6A6A60), 0.5f),
            _ => Hex(HairTones[hairPick]),
        };

        // Index of the family culture that claims one garment, or −1 for the town's own mix. No draw for a pure family.
        private static int FamilyLine(in NpcHeritage h, ref Pcg32 rng)
        {
            if (h.CultureLines == 0)
            {
                return -1;
            }

            float u = rng.NextFloat();
            for (int i = 0; i < h.CultureLines; i++)
            {
                u -= h.CultureShare(i);
                if (u < 0f)
                {
                    return i;
                }
            }

            return -1;
        }

        private static ArchEra GarmentEra(in NpcSpec spec, ArchEra era, ArchEra borrowEra, ref Pcg32 rng)
        {
            int line = FamilyLine(spec.Heritage, ref rng);
            if (line >= 0)
            {
                return Worn(spec.Heritage.CultureEra(line));
            }

            return rng.NextFloat() < spec.BorrowShare ? borrowEra : era;
        }

        // ---- culture: what one settlement shares ---------------------------------------------------------------

        internal enum Cut
        {
            Tunic, Robe, Dress, Coat, Mantle, Vest, Wrap,
        }

        private const int CutCount = 7;
        private const int HairCount = 9;

        private struct Culture
        {
            public float HueShift, SatScale, HatOdds, DyedHairOdds;
            public int FavouredCut, FavouredHair; // −1 = none

            // A neutral culture (seed 0) reproduces the region × era wardrobe unshifted: specs built by hand stay comparable.
            public static Culture For(ulong seed, ArchEra era)
            {
                float dyed = era == ArchEra.Future ? 0.3f : era == ArchEra.Wasteland ? 0.2f : 0.03f;
                if (seed == 0ul)
                {
                    return new Culture { SatScale = 1f, HatOdds = 0.55f, DyedHairOdds = dyed, FavouredCut = -1, FavouredHair = -1 };
                }

                var rng = new Pcg32(seed, 0x43554C54ul);
                return new Culture
                {
                    HueShift = math.clamp(0.035f * Gauss(ref rng), -0.08f, 0.08f),
                    SatScale = math.clamp(1f + 0.18f * Gauss(ref rng), 0.65f, 1.35f),
                    HatOdds = math.lerp(0.25f, 0.85f, rng.NextFloat()),
                    DyedHairOdds = dyed * math.lerp(0.3f, 2f, rng.NextFloat()),
                    FavouredCut = (int)rng.NextUInt(CutCount),
                    FavouredHair = (int)rng.NextUInt(HairCount),
                };
            }
        }

        // Base odds of each cut (Tunic, Robe, Dress, Coat, Mantle, Vest, Wrap), one row per worn era.
        private static readonly float[][] CutOdds =
        {
            new[] { 2f, 0.3f, 1f, 0f, 1.5f, 1f, 3f },       // Primal (and Ancient)
            new[] { 3f, 1f, 2f, 0.3f, 1f, 1.5f, 0.3f },     // Agrarian
            new[] { 3f, 1f, 2f, 1.5f, 0.5f, 1.5f, 0f },     // Mercantile
            new[] { 2f, 0.3f, 1.5f, 3f, 0.5f, 1.5f, 0f },   // Industrial
            new[] { 1.5f, 0.3f, 1f, 3f, 1f, 1.5f, 0f },     // Steampunk
            new[] { 3f, 1f, 0.6f, 1.5f, 0.6f, 1.5f, 0.2f }, // Future
            new[] { 2f, 0.5f, 0.5f, 1.5f, 3f, 1.5f, 1f },   // Wasteland
        };

        private static int EraRow(ArchEra era) => era switch
        {
            ArchEra.Agrarian => 1,
            ArchEra.Mercantile => 2,
            ArchEra.Industrial => 3,
            ArchEra.Steampunk => 4,
            ArchEra.Future => 5,
            ArchEra.Wasteland => 6,
            _ => 0,
        };

        private static Cut PickCut(in NpcSpec spec, in Look l, in Culture culture, ref Pcg32 rng)
        {
            if (spec.Role == NpcRole.Scholar || spec.Role == NpcRole.Cleric)
            {
                return Cut.Robe;
            }

            float[] odds = (float[])CutOdds[EraRow(l.UpperEra)].Clone();
            switch (l.PieceRegion)
            {
                case ArchRegion.Ice:
                    odds[(int)Cut.Mantle] *= 2f;
                    odds[(int)Cut.Coat] *= 1.5f;
                    odds[(int)Cut.Wrap] *= 0.2f;
                    odds[(int)Cut.Vest] *= 0.5f;
                    break;
                case ArchRegion.Lava:
                    odds[(int)Cut.Vest] *= 2f;
                    odds[(int)Cut.Wrap] *= 1.5f;
                    odds[(int)Cut.Mantle] *= 0.5f;
                    break;
                case ArchRegion.Storm:
                    odds[(int)Cut.Mantle] *= 1.6f;
                    break;
                case ArchRegion.Crystal:
                    odds[(int)Cut.Coat] *= 1.4f;
                    break;
                case ArchRegion.Twilight:
                    odds[(int)Cut.Robe] *= 2f;
                    break;
                default:
                    odds[(int)Cut.Tunic] *= 1.3f;
                    break;
            }

            odds[(int)Cut.Dress] *= spec.Sex == NpcSex.Male ? 0.04f : 1.6f;
            if (spec.Role == NpcRole.Laborer || spec.Role == NpcRole.Miner)
            {
                odds[(int)Cut.Vest] *= 3f;
                odds[(int)Cut.Robe] = 0f;
            }

            if (spec.Role == NpcRole.Leader || spec.Role == NpcRole.Merchant)
            {
                odds[(int)Cut.Coat] *= 2f;
            }

            if (l.Child)
            {
                odds[(int)Cut.Coat] *= 0.3f;
                odds[(int)Cut.Robe] *= 0.3f;
            }

            // A favoured cut is a town fashion, not a uniform: it only multiplies what the era already allows.
            if (culture.FavouredCut >= 0 && odds[culture.FavouredCut] > 0f)
            {
                odds[culture.FavouredCut] = odds[culture.FavouredCut] * 3f + 1f;
            }

            return (Cut)Weighted(odds, ref rng);
        }

        // Odds of each hairstyle (cropped, long, bun, tail, bald, braids, crest, topknot, volume).
        private static readonly float[] HairOddsFemale = { 1f, 3f, 2f, 2f, 0.05f, 2f, 0.3f, 1f, 1.5f };
        private static readonly float[] HairOddsMale = { 4f, 0.8f, 0.3f, 1f, 1.2f, 0.4f, 1f, 1f, 1f };

        // Odds of each beard (full, close-trimmed, goatee, forked, braided, moustache).
        private static readonly float[] BeardOddsDwarf = { 3f, 0.5f, 0f, 2f, 3f, 0f };
        private static readonly float[] BeardOddsHuman = { 1f, 2f, 1.5f, 0.3f, 0.3f, 1.5f };

        // Per race, multipliers on the hairstyle odds above: a people's fashion leans one way without forbidding the rest.
        private static readonly float[][] RaceHair =
        {
            null,                                                     // Human
            new[] { 1f, 2.5f, 1f, 1f, 0.1f, 2f, 0.3f, 1f, 1f },       // Elf
            new[] { 1f, 1f, 1f, 1f, 1.5f, 2f, 0.5f, 1.5f, 1f },       // Dwarf
            new[] { 1f, 0.4f, 0.5f, 1f, 2f, 0.5f, 3f, 2f, 0.5f },     // Goblin
            new[] { 0.8f, 0.5f, 0.5f, 1.5f, 1.5f, 1f, 3f, 3f, 0.5f }, // Orc
            null, null, null, null,                                   // Avian, Merfolk, Lizardfolk, Dragonkin (no hair)
            null,                                                     // Zombie
            null,                                                     // Skeleton
            null,                                                     // Werewolf
            new[] { 1.5f, 2f, 1f, 1f, 0.3f, 0.3f, 0.3f, 0.5f, 0.3f }, // Vampire
            new[] { 1f, 2f, 1f, 1f, 0.1f, 1f, 0.1f, 0.5f, 2.5f },     // Angel
            new[] { 1f, 1.5f, 1f, 1.5f, 1f, 1f, 2f, 1f, 1f },         // Demon
        };

        private static int PickHair(in NpcSpec spec, in Culture culture, float grey, ref Pcg32 rng)
        {
            float[] odds = (float[])(spec.Sex == NpcSex.Female ? HairOddsFemale : HairOddsMale).Clone();
            if (spec.Sex == NpcSex.Male)
            {
                odds[4] *= 1f + 4f * grey;
            }

            float[] lean = (int)spec.Race < RaceHair.Length ? RaceHair[(int)spec.Race] : null;
            for (int i = 0; lean != null && i < odds.Length; i++)
            {
                odds[i] *= lean[i];
            }

            if (culture.FavouredHair >= 0)
            {
                odds[culture.FavouredHair] = odds[culture.FavouredHair] * 3f + 0.8f;
            }

            return Weighted(odds, ref rng);
        }

        // Weight (the planner's girth draw) splits into muscle and fat, each a bounded normal around the race's mean, so
        // two equally heavy people can be a docker and a baker.
        // REQ: characters.md#路人-npc (體型).
        private static (float muscle, float fat) Physique(in NpcSpec spec, in Traits t, bool child, bool ageless, ref Pcg32 rng)
        {
            bool female = spec.Sex == NpcSex.Female;
            float sexFat = female ? 0.06f : 0f;
            if (spec.Race == NpcRace.Skeleton)
            {
                return (0f, t.Fat + sexFat);
            }

            bool toil = spec.Role == NpcRole.Laborer || spec.Role == NpcRole.Guard || spec.Role == NpcRole.Miner ||
                        spec.Role == NpcRole.Artisan || spec.Role == NpcRole.Forager || spec.Role == NpcRole.Farmer;
            float habit = toil ? 0.6f : spec.Role == NpcRole.Scholar ? -0.3f : 0f;
            float muscle = child ? 0.4f * Gauss(ref rng) : 0.55f * spec.BulkZ + 0.8f * Gauss(ref rng) + habit;
            // Middle age adds fat; old age and long illness take it away again.
            float spread = ageless ? 0f : 0.05f * math.smoothstep(0.38f, 0.62f, spec.Age01) * (1f - math.smoothstep(0.78f, 0.98f, spec.Age01));
            float ill = 0.06f * (1f - math.smoothstep(0.25f, 0.7f, spec.Health01));
            float fat = t.Fat + sexFat + (child ? 0.03f : 0f) + 0.035f * spec.BulkZ + 0.045f * Gauss(ref rng) + spread - ill;
            return (math.clamp(muscle, -3f, 3f), math.clamp(fat, 0.03f, 0.55f));
        }

        private static int Weighted(float[] odds, ref Pcg32 rng)
        {
            float total = 0f;
            for (int i = 0; i < odds.Length; i++)
            {
                total += odds[i];
            }

            float pick = rng.NextFloat() * total;
            int last = 0;
            for (int i = 0; i < odds.Length; i++)
            {
                if (odds[i] <= 0f)
                {
                    continue;
                }

                last = i;
                pick -= odds[i];
                if (pick < 0f)
                {
                    return i;
                }
            }

            return last;
        }

        private static ArchEra Worn(ArchEra era) => era == ArchEra.Ancient ? ArchEra.Primal : era;

        internal enum ArmPose
        {
            Hang, Hold, Reach, Raise, Lean,
        }

        internal struct Rig
        {
            public float H, HeadR, ArmR, LegR, TrunkX, TrunkZ, Bulk, Belly, NeckR;
            // Limb radii at the root, the belly of the upper segment, the joint, the belly of the lower segment and the
            // far joint, metres. Clothes are lofted through the same nodes, so a sleeve never cuts into a heavy arm.
            public float ThighR, ThighMidR, KneeRad, CalfR, AnkleRad;
            public float UpperR, UpperMidR, ElbowRad, ForeR, WristR;
            public float3 Hip, Waist, Chest, Neck, Head, Fwd;
            public float3 ShoulderL, ShoulderR, ElbowL, ElbowR, HandL, HandR;
            public float3 HipL, HipR, KneeL, KneeR, AnkleL, AnkleR;
            public ArmPose Left, Right;

            public static Rig Pose(in NpcSpec spec, in Traits t, in Look look, ref Pcg32 rng)
            {
                var r = default(Rig);
                r.H = HeightM(spec);
                float head = t.Head * math.lerp(1.3f, 1f, math.smoothstep(0.52f, 1f, look.Grown)) * math.clamp(1f + 0.03f * Gauss(ref rng), 0.9f, 1.1f);
                r.HeadR = 0.086f * r.H * head;
                float neck = 0.03f * r.H;
                float below = r.H - 2f * r.HeadR - neck;
                // Same height, different build: leg share varies by a bounded normal around the race's.
                float leg = below * math.clamp(t.LegShare + 0.012f * Gauss(ref rng), t.LegShare - 0.04f, t.LegShare + 0.04f), torso = below - leg;
                bool female = spec.Sex == NpcSex.Female;
                r.Bulk = t.Bulk * math.clamp(1f + 0.1f * spec.BulkZ, 0.7f, 1.4f) * (look.Child ? 0.9f : 1f);
                float shoulder = t.Shoulder * r.H * (female ? 0.9f : 1f) * math.clamp(1f + 0.04f * spec.BulkZ, 0.85f, 1.15f);
                float hip = 0.08f * r.H * (female ? 1.08f : 1f) * math.sqrt(r.Bulk);
                // Proportions follow muscle and fat relative to the race and sex mean: the race's own build (a dwarf's
                // girth) stays in Traits, and only the person's departure from it reshapes the figure.
                float fatDev = look.Fat - t.Fat - (female ? 0.06f : 0f) - (look.Child ? 0.03f : 0f);
                float m = look.Muscle;
                float fShoulder = math.clamp(1f + 0.045f * m, 0.88f, 1.15f);
                float fArm = math.clamp(1f + 0.09f * m + 0.9f * fatDev, 0.75f, 1.4f);
                float fLeg = math.clamp(1f + 0.07f * m + 0.8f * fatDev, 0.8f, 1.35f);
                float fDepth = math.clamp(1f + 0.04f * m + 0.9f * fatDev, 0.85f, 1.3f);
                float fBelly = math.clamp(1f + (female ? 1.8f : 2.6f) * fatDev - 0.03f * m, 0.8f, 1.65f);
                float fPosture = math.clamp(-0.015f * m, -0.04f, 0.03f);
                hip *= math.clamp(1f + (female ? 1f : 0.5f) * fatDev, 0.9f, 1.25f);
                bool ageless = spec.Race == NpcRace.Vampire || spec.Race == NpcRace.Angel || spec.Race == NpcRace.Demon;
                float frail = ageless ? 0f : math.smoothstep(0.7f, 0.98f, spec.Age01);
                // Long illness takes the flesh off the limbs before it takes the colour from the skin.
                float wasting = 0.2f * (1f - math.smoothstep(0.25f, 0.7f, spec.Health01)) + 0.1f * frail;
                shoulder *= fShoulder * (1f - 0.06f * frail);
                r.ArmR = 0.027f * r.H * r.Bulk * fArm * (1f - wasting);
                r.LegR = 0.04f * r.H * r.Bulk * fLeg * (1f - wasting);
                r.TrunkX = shoulder * 0.92f;
                r.TrunkZ = 0.07f * r.H * math.sqrt(r.Bulk) * (1f + math.max(0f, 0.12f * spec.BulkZ)) * fDepth;
                r.Belly = fBelly * (1f - 0.5f * wasting);

                // Where the weight sits along a limb. Fat gathers on the upper arm and thigh, hardly at the joints; muscle
                // fills the forearm and calf. Women carry it lower (thigh, hip), men higher (belly, upper arm). So a lean
                // figure shows its knees and elbows, a heavy one tapers to slim wrists and ankles.
                // REQ: characters.md#路人-npc (體型).
                float low = female ? 1.25f : 0.85f, high = female ? 1f : 1.15f;
                float thighK = math.clamp(1f + 0.65f * low * fatDev + 0.02f * m, 0.88f, 1.25f);
                float kneeK = math.clamp(1f - 0.4f * fatDev - 0.015f * m, 0.9f, 1.08f);
                float calfK = math.clamp(1f - 0.15f * fatDev + 0.05f * m, 0.88f, 1.15f);
                float ankleK = math.clamp(1f - 0.45f * fatDev - 0.015f * m, 0.88f, 1.08f);
                float upperK = math.clamp(1f + 0.6f * high * fatDev + 0.025f * m, 0.88f, 1.22f);
                float foreK = math.clamp(1f - 0.15f * fatDev + 0.06f * m, 0.88f, 1.15f);
                float wristK = math.clamp(1f - 0.45f * fatDev - 0.015f * m, 0.88f, 1.08f);
                r.ThighR = r.LegR * 1.15f * thighK;
                r.ThighMidR = r.LegR * 1.1f * thighK;
                r.KneeRad = r.LegR * 0.86f * kneeK;
                r.CalfR = r.LegR * 0.92f * calfK;
                r.AnkleRad = r.LegR * 0.6f * ankleK;
                r.UpperR = r.ArmR * 1.15f * upperK;
                r.UpperMidR = r.ArmR * 1.1f * upperK;
                r.ElbowRad = r.ArmR * 0.86f * kneeK;
                r.ForeR = r.ArmR * 0.95f * foreK;
                r.WristR = r.ArmR * 0.68f * wristK;
                r.NeckR = r.HeadR * 0.42f * math.sqrt(t.Bulk) * math.clamp(1f + 0.9f * fatDev + 0.04f * m, 0.88f, 1.3f) * (1f - 0.5f * wasting);

                float lean = math.clamp(t.Hunch + look.Stoop + fPosture, 0f, 0.6f);
                // Leaning shortens the figure; the legs keep their length so the feet stay on the ground.
                r.Fwd = new float3(0f, math.cos(lean), math.sin(lean));
                r.Hip = new float3(0f, leg, 0f);
                r.Waist = r.Hip + r.Fwd * (torso * 0.35f);
                r.Chest = r.Hip + r.Fwd * torso;
                r.Neck = r.Chest + new float3(0f, neck, 0f);
                r.Head = r.Neck + new float3(0f, r.HeadR * 0.95f, r.HeadR * (0.1f + 0.5f * math.sin(lean)));

                float sway = 0.02f * r.H * (rng.NextFloat() - 0.5f);
                r.HipL = r.Hip + new float3(-hip, 0f, 0f);
                r.HipR = r.Hip + new float3(hip, 0f, 0f);
                r.AnkleL = new float3(-hip * 1.15f, 0.045f * r.H, sway);
                r.AnkleR = new float3(hip * 1.15f, 0.045f * r.H, -sway);
                float bend = 0.015f * r.H + 0.08f * r.H * math.sin(lean);
                r.KneeL = (r.HipL + r.AnkleL) * 0.5f + new float3(0f, 0f, bend);
                r.KneeR = (r.HipR + r.AnkleR) * 0.5f + new float3(0f, 0f, bend);

                r.Right = spec.Race == NpcRace.Zombie ? ArmPose.Reach
                    : spec.Role == NpcRole.Laborer ? ArmPose.Raise
                    : spec.Role == NpcRole.None || spec.Role == NpcRole.Merchant || spec.Role == NpcRole.Healer ||
                      spec.Role == NpcRole.Forager ? (rng.NextUInt(3) == 0 ? ArmPose.Hold : ArmPose.Hang)
                    : ArmPose.Hold;
                r.Left = spec.Race == NpcRace.Zombie ? ArmPose.Reach
                    : look.Cane ? ArmPose.Lean
                    : spec.Role == NpcRole.Guard ? ArmPose.Hold
                    : ArmPose.Hang;
                float upper = 0.17f * r.H * t.Arm, fore = 0.15f * r.H * t.Arm;
                r.ShoulderL = r.Chest + new float3(-shoulder, -0.02f * r.H, 0f);
                r.ShoulderR = r.Chest + new float3(shoulder, -0.02f * r.H, 0f);
                Arm(r.ShoulderL, -1f, r.Left, upper, fore, lean, rng.NextFloat(), out r.ElbowL, out r.HandL);
                Arm(r.ShoulderR, 1f, r.Right, upper, fore, lean, rng.NextFloat(), out r.ElbowR, out r.HandR);
                return r;
            }

            // Angles are from straight down, swinging forward; a hanging arm stays plumb however far the spine leans.
            private static void Arm(float3 shoulder, float side, ArmPose pose, float upper, float fore, float lean, float jitter,
                out float3 elbow, out float3 hand)
            {
                (float a1, float a2, float splay) = pose switch
                {
                    ArmPose.Hold => (0.15f, 1.3f + 0.2f * jitter, 0.05f),
                    ArmPose.Reach => (1.25f + 0.2f * jitter, 1.4f, 0.02f),
                    ArmPose.Raise => (0.35f, 2.7f, 0.1f),
                    ArmPose.Lean => (0.3f, 0.75f, 0.16f),
                    _ => (0.06f + 0.08f * jitter, 0.22f + 0.15f * jitter, 0.1f),
                };
                a1 += lean * 0.3f;
                elbow = shoulder + new float3(side * splay * upper, -math.cos(a1) * upper, math.sin(a1) * upper);
                hand = elbow + new float3(side * splay * fore * 0.5f, -math.cos(a2) * fore, math.sin(a2) * fore);
            }
        }

        // ---- body ----------------------------------------------------------------------------------------------

        private static void Flesh(Fig b, Fig glow, in NpcSpec spec, in Traits t, in Rig r, in Look look)
        {
            Color skin = spec.Race == NpcRace.Skeleton ? look.Bone : look.Skin;
            Color dark = Shade(skin, 0.86f);
            bool bones = spec.Race == NpcRace.Skeleton;
            float foot = r.LegR * 1.15f;
            for (int s = -1; s <= 1; s += 2)
            {
                float3 hip = s < 0 ? r.HipL : r.HipR, knee = s < 0 ? r.KneeL : r.KneeR, ankle = s < 0 ? r.AnkleL : r.AnkleR;
                b.Leg = s;
                float3 thigh = math.lerp(hip, knee, 0.32f), calf = math.lerp(knee, ankle, 0.3f);
                b.Tube(hip, thigh, r.ThighR, r.ThighMidR, skin, skin, 7);
                b.Tube(thigh, knee, r.ThighMidR, r.KneeRad, skin, dark, 7);
                b.Tube(knee, calf, r.KneeRad, r.CalfR, dark, dark, 7);
                b.Tube(calf, ankle, r.CalfR, r.AnkleRad, dark, skin, 7);
                if (bones)
                {
                    b.Ball(knee, new float3(r.LegR * 1.5f), skin, 6, 4);
                }

                // Digitigrade races stand on a longer foot; everyone gets a wedge that reads as a shoe from above.
                float len = spec.Race == NpcRace.Werewolf || spec.Race == NpcRace.Avian || spec.Race == NpcRace.Lizardfolk ||
                            spec.Race == NpcRace.Dragonkin ? 2.6f : 2f;
                Color footTone = spec.Race == NpcRace.Avian ? Hex(0xE0A83A) : dark;
                b.Leg = 2 * s;
                b.Ball(new float3(ankle.x, foot * 0.55f, ankle.z + foot * (len - 1.2f) * 0.5f),
                    new float3(foot * 0.95f, foot * 0.6f, foot * len * 0.6f), footTone, 7, 4, -0.5f);

                b.Leg = 0;
                float3 shoulder = s < 0 ? r.ShoulderL : r.ShoulderR, elbow = s < 0 ? r.ElbowL : r.ElbowR, hand = s < 0 ? r.HandL : r.HandR;
                float3 upper = math.lerp(shoulder, elbow, 0.38f), fore = math.lerp(elbow, hand, 0.25f);
                b.Tube(shoulder, upper, r.UpperR, r.UpperMidR, skin, skin, 6);
                b.Tube(upper, elbow, r.UpperMidR, r.ElbowRad, skin, dark, 6);
                b.Tube(elbow, fore, r.ElbowRad, r.ForeR, dark, dark, 6);
                b.Tube(fore, hand, r.ForeR, r.WristR, dark, skin, 6);
                b.Ball(hand, new float3(r.ArmR * 1.25f), skin, 6, 4);
                b.Ball(shoulder, new float3(r.ArmR * 1.3f), skin, 6, 4);
            }

            if (bones)
            {
                // Spine, a rib basket of four hoops, and a pelvis: gaps are what make it read as a skeleton.
                b.Tube(r.Hip, r.Neck, r.ArmR * 0.9f, r.ArmR * 0.8f, skin, skin, 6);
                for (int i = 0; i < 4; i++)
                {
                    float3 c = math.lerp(r.Waist, r.Chest, 0.25f + 0.25f * i);
                    float w = r.TrunkX * (0.75f + 0.08f * i);
                    b.Loft(new[]
                    {
                        new Ring(c - new float3(0f, 0.012f * r.H, 0f), w, r.TrunkZ * 1.5f, dark),
                        new Ring(c + new float3(0f, 0.012f * r.H, 0f), w, r.TrunkZ * 1.5f, skin),
                    }, 8, true, true);
                }

                b.Ball(r.Hip, new float3(r.TrunkX * 0.8f, 0.035f * r.H, r.TrunkZ * 1.3f), skin, 7, 4);
            }
            else
            {
                float belly = r.Belly;
                b.Loft(new[]
                {
                    new Ring(r.Hip - new float3(0f, 0.03f * r.H, 0f), r.TrunkX * 0.82f, r.TrunkZ * 0.95f, dark),
                    new Ring(r.Waist, r.TrunkX * 0.8f * belly, r.TrunkZ * belly, skin),
                    new Ring(r.Chest - r.Fwd * (0.05f * r.H), r.TrunkX, r.TrunkZ * 1.08f, skin),
                    new Ring(r.Chest, r.TrunkX * 0.86f, r.TrunkZ * 0.9f, skin),
                }, 8, true, true);
            }

            b.Tube(r.Chest, r.Head, r.NeckR, r.NeckR * 0.9f, dark, skin, 6);
            Mark(b, glow, true);
            // Wings and tails grow from the back: they ride the trunk, never the arm hanging beside them.
            Ride(b, glow, RideBody);
            Tail(b, spec, r, look);
            Wings(b, glow, spec, r, look);
            Ride(b, glow, RideFree);
        }

        private static void Tail(Fig b, in NpcSpec spec, in Rig r, in Look look)
        {
            float root = r.TrunkZ * 0.8f;
            float3 from = r.Hip + new float3(0f, 0.02f * r.H, -root);
            switch (spec.Race)
            {
                case NpcRace.Lizardfolk:
                case NpcRace.Dragonkin:
                {
                    // Three segments sagging to just above the ground: weight behind the hips balances the long snout.
                    float thick = r.LegR * (spec.Race == NpcRace.Dragonkin ? 1.5f : 1.15f);
                    float3 a = from + new float3(0f, -0.1f * r.H, -0.16f * r.H);
                    float3 c = a + new float3(0.04f * r.H, -0.14f * r.H, -0.2f * r.H);
                    float3 d = new float3(c.x + 0.05f * r.H, math.max(0.04f, c.y - 0.08f * r.H), c.z - 0.2f * r.H);
                    b.Tube(from, a, thick, thick * 0.8f, look.Skin, Shade(look.Skin, 0.9f), 7);
                    b.Tube(a, c, thick * 0.8f, thick * 0.5f, Shade(look.Skin, 0.9f), Shade(look.Skin, 0.85f), 7);
                    b.Tube(c, d, thick * 0.5f, 0.01f, Shade(look.Skin, 0.85f), Shade(look.Skin, 0.8f), 6);
                    break;
                }
                case NpcRace.Werewolf:
                {
                    float3 tip = from + new float3(0f, -0.12f * r.H, -0.3f * r.H);
                    b.Ball((from + tip) * 0.5f, new float3(r.LegR * 1.3f, 0.1f * r.H, 0.17f * r.H), Shade(look.Skin, 0.92f), 7, 5);
                    break;
                }
                case NpcRace.Demon:
                {
                    float3 a = from + new float3(0.05f * r.H, -0.2f * r.H, -0.2f * r.H);
                    float3 c = a + new float3(0.08f * r.H, 0.06f * r.H, -0.16f * r.H);
                    b.Tube(from, a, r.ArmR * 0.6f, r.ArmR * 0.4f, look.Skin, look.Skin, 5);
                    b.Tube(a, c, r.ArmR * 0.4f, r.ArmR * 0.3f, look.Skin, look.Skin, 5);
                    b.Panel(c + new float3(0f, 0.05f * r.H, 0f), c + new float3(0.03f * r.H, -0.03f * r.H, -0.05f * r.H),
                        c + new float3(-0.03f * r.H, -0.03f * r.H, 0.02f * r.H), Shade(look.Skin, 0.8f));
                    break;
                }
                case NpcRace.Avian:
                {
                    for (int i = -2; i <= 2; i++)
                    {
                        float3 tip = from + new float3(i * 0.05f * r.H, -0.14f * r.H, -0.24f * r.H + math.abs(i) * 0.03f * r.H);
                        b.Panel(from + new float3(i * 0.015f * r.H - 0.02f * r.H, 0f, 0f), from + new float3(i * 0.015f * r.H + 0.02f * r.H, 0f, 0f),
                            tip, Shade(look.Skin, (i & 1) == 0 ? 0.8f : 0.92f));
                    }

                    break;
                }
                case NpcRace.Merfolk:
                {
                    // Calf fins: the swimmer's silhouette without taking the legs away from someone who walks to market.
                    for (int s = -1; s <= 1; s += 2)
                    {
                        b.Leg = s;
                        float3 knee = s < 0 ? r.KneeL : r.KneeR, ankle = s < 0 ? r.AnkleL : r.AnkleR;
                        b.Panel(knee, ankle, (knee + ankle) * 0.5f + new float3(0f, -0.02f * r.H, -0.1f * r.H), Shade(look.Accent, 1.05f));
                        b.Leg = 0;
                    }

                    break;
                }
            }
        }

        private static void Wings(Fig b, Fig glow, in NpcSpec spec, in Rig r, in Look look)
        {
            float3 back = r.Chest + new float3(0f, -0.04f * r.H, -r.TrunkZ * 0.95f);
            switch (spec.Race)
            {
                case NpcRace.Angel:
                case NpcRace.Avian:
                {
                    // Folded: three overlapping rows of flight feathers from a shoulder-high wrist down past the hips.
                    bool angel = spec.Race == NpcRace.Angel;
                    Color feather = angel ? Hex(0xF6F2E8) : look.Skin;
                    float span = (angel ? 0.34f : 0.2f) * r.H, drop = (angel ? 0.62f : 0.42f) * r.H;
                    for (int s = -1; s <= 1; s += 2)
                    {
                        float3 wrist = back + new float3(s * span, 0.16f * r.H, -0.1f * r.H);
                        b.Tube(back, wrist, r.ArmR * 0.9f, r.ArmR * 0.6f, feather, feather, 5);
                        const int Feathers = 7;
                        float3 lastRoot = back, lastTip = back + new float3(0f, -drop * 0.5f, -0.03f * r.H);
                        for (int i = 1; i <= Feathers; i++)
                        {
                            float k = i / (float)Feathers;
                            float3 root = math.lerp(back, wrist, k);
                            // Primaries lengthen toward the wrist; every other one is a touch shorter, which scallops the edge.
                            float reach = drop * (0.5f + 0.5f * k) * ((i & 1) == 0 ? 1f : 0.93f);
                            float3 tip = root + new float3(s * 0.04f * k * r.H, -reach, -(0.04f + 0.07f * k) * r.H);
                            Color tone = Shade(feather, (i & 1) == 0 ? 0.98f : 0.88f);
                            b.Panel(lastRoot, root, tip, tone);
                            b.Panel(lastRoot, tip, lastTip, tone);
                            lastRoot = root;
                            lastTip = tip;
                        }
                    }

                    break;
                }
                case NpcRace.Demon:
                case NpcRace.Dragonkin:
                {
                    // Membrane between three fingers; a dragonkin's are too small to fly on, a demon's are not.
                    float size = (spec.Race == NpcRace.Demon ? 0.46f : 0.3f) * r.H;
                    Color membrane = Shade(look.Skin, 0.62f), finger = Shade(look.Skin, 0.8f);
                    for (int s = -1; s <= 1; s += 2)
                    {
                        float3 wrist = back + new float3(s * size * 0.55f, size * 0.7f, -size * 0.3f);
                        b.Tube(back, wrist, r.ArmR * 0.8f, r.ArmR * 0.5f, finger, finger, 5);
                        float3 last = wrist + new float3(s * size * 0.15f, size * 0.25f, -size * 0.05f);
                        for (int i = 0; i < 3; i++)
                        {
                            float3 tip = wrist + new float3(s * size * (0.75f - 0.3f * i), -size * (0.35f + 0.42f * i), -size * (0.25f + 0.05f * i));
                            b.Tube(wrist, tip, r.ArmR * 0.45f, 0.008f, finger, finger, 4);
                            b.Panel(wrist, last, tip, membrane);
                            last = tip;
                        }

                        b.Panel(wrist, last, back + new float3(0f, -size * 0.5f, 0f), membrane);
                    }

                    break;
                }
            }

            if (spec.Race == NpcRace.Angel)
            {
                glow.Hoop(r.Head + new float3(0f, r.HeadR * 1.55f, 0f), r.HeadR * 0.85f, r.HeadR * 0.09f, Hex(0xFFF0B0));
            }
        }

        // ---- head ----------------------------------------------------------------------------------------------

        private static void Head(Fig b, Fig glow, in NpcSpec spec, in Traits t, in Rig r, in Look look, ref Pcg32 rng)
        {
            float3 c = r.Head;
            float hr = r.HeadR;
            Color skin = spec.Race == NpcRace.Skeleton ? look.Bone : look.Skin;
            // Its own stream: head variants can grow without reshuffling the clothes and poses of every existing resident.
            var hv = new Pcg32(Salt ^ spec.Seed, 0x48454144ul);
            bool ageless = spec.Race == NpcRace.Vampire || spec.Race == NpcRace.Angel || spec.Race == NpcRace.Demon;
            Face f = Face.Draw(spec, look, ageless, ref hv);
            // Ears, like noses, keep growing for life.
            float ear = math.clamp(1f + 0.15f * Gauss(ref hv), 0.75f, 1.35f) * (ageless ? 1f : 1f + 0.12f * math.smoothstep(0.6f, 0.95f, spec.Age01));
            var hs = new HeadShape(f);
            // Beasts keep the whole procedural head (UMA head slot stand-in); everyone else wears only the parts that
            // stand off the UMA face.
            bool beast = HeadSlotOf(spec.Race) != null;
            Mark(b, glow, beast);
            if (spec.Race == NpcRace.Skeleton)
            {
                // Bone is a cranium plus a separate jaw (drawn below), not a fleshed face shape.
                b.Ball(c, new float3(0.92f * f.SkullW, f.SkullH, 0.97f * f.SkullD) * hr, skin, 14, 8);
            }
            else
            {
                HeadShell(b, c, hr, hs, skin);
            }

            Mark(b, glow, true);

            // Rooted a little inside the outline, as on the old sphere: a plain ear shows its rim, not a triangle on a stalk.
            float earSide = 0.93f * hs.Width(-0.05f);
            Color eye = Pick(HumanIris, ref hv);
            bool lit = false, eyes = true;
            float eyeSize = 0.12f, eyeX = 0.4f, eyeY = 0.05f, eyeZ = 0.84f;
            float3 muzzle = c + new float3(0f, -0.2f * hr, 0.6f * hr);
            // Each race keeps its signature part (ear, beak, horn, fin, tusk); what varies is that part's shape, size and
            // colour, so a crowd of one people still reads as one people.
            switch (spec.Race)
            {
                case NpcRace.Elf:
                    if (hv.NextUInt(4) != 0)
                    {
                        Ears(b, c, hr, earSide, skin, math.lerp(0.55f, 1.1f, hv.NextFloat()) * ear, math.lerp(0.3f, 0.75f, hv.NextFloat()),
                            math.lerp(0.1f, 0.4f, hv.NextFloat()));
                    }
                    else
                    {
                        // The long level ear of the old lineages.
                        Ears(b, c, hr, earSide, skin, 0.2f * ear, 0.35f, math.lerp(0.6f, 0.95f, hv.NextFloat()) * ear);
                    }

                    eye = Pick(ElfIris, ref hv);
                    break;
                case NpcRace.Dwarf:
                {
                    float bulb = math.lerp(0.17f, 0.3f, hv.NextFloat()) * f.NoseW;
                    Color ruddy = Color.Lerp(Shade(skin, 0.94f), Hex(0xC86A5A), 0.3f * hv.NextFloat());
                    // Nose, brow and round ears are the face's own surface, which a UMA head models itself.
                    Mark(b, glow, beast);
                    b.Ball(c + new float3(0f, -0.15f, hs.Front(0f, -0.15f) - 0.04f) * hr, new float3(bulb, bulb * 0.9f, bulb) * hr, ruddy, 6, 4);
                    // A shelf of brow: a dwarf's face is framed by hair above and below.
                    float brow = math.lerp(0.08f, 0.15f, hv.NextFloat()) * f.BrowThick, tilt = f.BrowSlant;
                    for (int s = -1; s <= 1; s += 2)
                    {
                        b.Slab(c + new float3(s * 0.12f, eyeY + 0.25f, 0.88f) * hr, c + new float3(s * 0.62f, eyeY + 0.22f + tilt, 0.7f) * hr,
                            brow * hr, 0.09f * hr, Shade(look.Hair, 0.9f));
                    }

                    Ears(b, c, hr, earSide, skin, 0.2f * ear, 0.2f, 0.05f * ear);
                    Mark(b, glow, true);
                    break;
                }
                case NpcRace.Goblin:
                {
                    uint style = hv.NextUInt(3);
                    float size = math.lerp(0.8f, 1.4f, hv.NextFloat());
                    (float up, float back, float outward) = style == 0 ? (0.35f, 0.15f, 1.1f) : style == 1 ? (-0.35f, 0.25f, 0.95f) : (0.95f, 0.35f, 0.55f);
                    Ears(b, c, hr, earSide, skin, up * size, back, outward * size, hv.NextUInt(5) == 0 ? 0.6f : 1f);
                    float len = math.lerp(0.45f, 0.9f, hv.NextFloat()), droop = math.lerp(-0.15f, 0.3f, hv.NextFloat());
                    b.Tube(c + new float3(0f, -0.1f * hr, 0.85f * hr), c + new float3(0f, (-0.1f - droop) * hr, (0.85f + len) * hr), 0.15f * hr, 0.03f,
                        skin, Shade(skin, 0.9f), 5);
                    if (hv.NextUInt(10) < 3)
                    {
                        b.Tube(c + new float3(0.15f, -0.48f, hs.Front(0.15f, -0.48f) - 0.02f) * hr, c + new float3(0.16f, -0.32f, hs.Front(0.16f, -0.32f) + 0.02f) * hr, 0.05f * hr, 0.01f * hr, look.Bone,
                            look.Bone, 4);
                    }

                    eye = Pick(GoblinEyes, ref hv);
                    eyeSize = 0.2f * math.lerp(0.85f, 1.15f, hv.NextFloat());
                    lit = true;
                    break;
                }
                case NpcRace.Orc:
                {
                    float jaw = math.lerp(0.85f, 1.15f, hv.NextFloat());
                    float len = math.lerp(0.22f, 0.38f, hv.NextFloat()), splay = math.lerp(0f, 0.12f, hv.NextFloat());
                    int broken = hv.NextUInt(5) == 0 ? (hv.NextUInt(2) == 0 ? -1 : 1) : 0;
                    for (int s = -1; s <= 1; s += 2)
                    {
                        float l = len * (broken == s ? 0.45f : 1f);
                        b.Tube(c + new float3(s * 0.32f * jaw, -0.55f, hs.Front(0.32f * jaw, -0.55f) - 0.04f) * hr,
                            c + new float3(s * (0.34f * jaw + splay), -0.55f + l, hs.Front(0.32f * jaw, -0.55f) + 0.04f) * hr,
                            0.08f * hr, broken == s ? 0.05f * hr : 0.02f, look.Bone, look.Bone, 5);
                    }

                    if (hv.NextUInt(5) < 3)
                    {
                        Mark(b, glow, beast);
                        b.Slab(c + new float3(-0.5f, 0.32f, 0.8f) * hr, c + new float3(0.5f, 0.32f, 0.8f) * hr, 0.12f * hr, 0.1f * hr, Shade(skin, 0.9f));
                        Mark(b, glow, true);
                    }

                    if (hv.NextUInt(10) < 3)
                    {
                        // War paint under the eyes in the clan's colour.
                        for (int s = -1; s <= 1; s += 2)
                        {
                            b.Slab(c + new float3(s * 0.42f, -0.05f, hs.Front(0.42f, -0.05f)) * hr, c + new float3(s * 0.4f, -0.36f, hs.Front(0.4f, -0.36f)) * hr, 0.07f * hr, 0.03f * hr,
                                Shade(look.Accent, 0.8f));
                        }
                    }

                    Ears(b, c, hr, earSide, skin, math.lerp(0.15f, 0.5f, hv.NextFloat()) * ear, 0.3f, 0.2f * ear);
                    eye = Pick(OrcIris, ref hv);
                    break;
                }
                case NpcRace.Avian:
                    Beak(b, muzzle, hr, Pick(BeakTones, ref hv), hv.NextUInt(4));
                    Crest(b, c, hr, hv.NextUInt(5) == 0 ? Shade(skin, 1.15f) : look.Accent, hv.NextUInt(5));
                    if (hv.NextUInt(2) == 0)
                    {
                        // A crown of a second colour cut level above the eyes: plumage that reads at a glance.
                        b.Ball(c + new float3(0f, 0.15f * hr, -0.05f * hr), new float3(0.95f, 0.9f, 0.99f) * hr, Pick(PlumeCrowns, ref hv), 9, 5, 0.2f);
                    }

                    eyeX = 0.6f;
                    eyeZ = 0.62f;
                    eyeSize = 0.15f;
                    eye = Hex(0x20181A);
                    break;
                case NpcRace.Merfolk:
                {
                    Color fin = Shade(look.Accent, 1.05f);
                    switch (hv.NextUInt(3))
                    {
                        case 0:
                            b.Panel(c + new float3(0f, 0.9f * hr, 0.5f * hr), c + new float3(0f, 0.6f * hr, -0.9f * hr),
                                c + new float3(0f, 1.6f * hr, -0.45f * hr), fin);
                            break;
                        case 1:
                            for (int i = 0; i < 3; i++)
                            {
                                float3 root = c + new float3(0f, (0.95f - 0.2f * i) * hr, (0.3f - 0.4f * i) * hr);
                                b.Panel(root + new float3(0f, 0f, 0.2f * hr), root + new float3(0f, -0.1f * hr, -0.22f * hr),
                                    root + new float3(0f, 0.35f * hr, -0.25f * hr), Shade(fin, 1f - 0.06f * i));
                            }

                            break;
                        default:
                            // A frill of three spines fanning out over the crown.
                            for (int i = -1; i <= 1; i++)
                            {
                                float3 root = c + new float3(i * 0.25f * hr, 0.85f * hr, -0.1f * hr);
                                b.Panel(root + new float3(0f, 0f, 0.18f * hr), root + new float3(0f, 0f, -0.18f * hr),
                                    root + new float3(i * 0.45f * hr, 0.75f * hr, -0.35f * hr), Shade(fin, 1f - 0.08f * math.abs(i)));
                            }

                            break;
                    }

                    float earFin = math.lerp(0.5f, 1f, hv.NextFloat());
                    for (int s = -1; s <= 1; s += 2)
                    {
                        float3 root = c + new float3(s * 0.88f * hr, 0f, 0f);
                        b.Panel(root + new float3(0f, 0.25f * hr, 0.1f * hr), root + new float3(0f, -0.3f * hr, 0.1f * hr),
                            root + new float3(s * 0.7f * earFin * hr, 0.25f * earFin * hr, -0.4f * earFin * hr), Shade(look.Accent, 0.95f));
                    }

                    if (hv.NextUInt(5) < 3)
                    {
                        Color slit = Shade(skin, 0.62f);
                        for (int s = -1; s <= 1; s += 2)
                        {
                            for (int i = 0; i < 3; i++)
                            {
                                float3 g = c + new float3(s * 0.8f, -0.38f - 0.13f * i, 0.18f) * hr;
                                b.Slab(g, g + new float3(0f, 0f, -0.25f * hr), 0.03f * hr, 0.03f * hr, slit);
                            }
                        }
                    }

                    if (hv.NextUInt(5) < 2)
                    {
                        Color spot = Color.Lerp(skin, Color.white, 0.4f);
                        for (int i = 0; i < 5; i++)
                        {
                            float a = (i - 2) * 0.35f;
                            b.Ball(c + new float3(math.sin(a) * 0.6f, 0.62f + 0.06f * (i & 1), math.cos(a) * 0.6f) * hr, new float3(0.07f * hr), spot, 5, 3);
                        }
                    }

                    eye = Pick(MerfolkEyes, ref hv);
                    eyeSize = 0.16f;
                    break;
                }
                case NpcRace.Lizardfolk:
                case NpcRace.Dragonkin:
                {
                    bool dragon = spec.Race == NpcRace.Dragonkin;
                    float snout = dragon ? math.lerp(0.95f, 1.25f, hv.NextFloat()) : math.lerp(0.75f, 1.05f, hv.NextFloat());
                    b.Tube(muzzle, muzzle + new float3(0f, -0.08f * hr, snout * hr), 0.55f * hr, 0.3f * hr, skin, Shade(skin, 0.92f), 7);
                    b.Ball(muzzle + new float3(0f, -0.3f * hr, 0.35f * hr), new float3(0.42f, 0.2f, 0.6f) * hr, Shade(skin, 1.12f), 6, 4);
                    if (dragon)
                    {
                        Horns(b, c, hr, look.Bone, Shade(look.Bone, 0.85f), hv.NextUInt(4), math.lerp(0.8f, 1.3f, hv.NextFloat()));
                        if (hv.NextUInt(10) < 3)
                        {
                            // Barbels from the upper lip: the long-whiskered river dragons.
                            for (int s = -1; s <= 1; s += 2)
                            {
                                float3 root = muzzle + new float3(s * 0.25f * hr, -0.1f * hr, snout * 0.8f * hr);
                                b.Tube(root, root + new float3(s * 0.35f * hr, -0.7f * hr, 0.1f * hr), 0.05f * hr, 0.01f * hr, look.Accent, look.Accent, 4);
                            }
                        }
                    }

                    uint crest = dragon ? 0u : hv.NextUInt(3);
                    int spines = dragon ? 3 + (int)hv.NextUInt(4) : 4;
                    switch (crest)
                    {
                        case 1:
                            // Neck frill fanned behind the head.
                            for (int i = -2; i <= 2; i++)
                            {
                                float a = i * 0.45f;
                                float3 root = c + new float3(math.sin(a) * 0.6f, -0.2f, -math.cos(a) * 0.55f) * hr;
                                b.Panel(root + new float3(0f, 0.25f * hr, 0f), root - new float3(0f, 0.25f * hr, 0f),
                                    root + new float3(math.sin(a) * 0.75f, 0.3f, -math.cos(a) * 0.6f) * hr, Shade(look.Accent, (i & 1) == 0 ? 1f : 0.85f));
                            }

                            break;
                        case 2:
                            for (int i = 0; i < 5; i++)
                            {
                                b.Ball(c + new float3(0f, 0.95f - 0.22f * i, 0.25f - 0.28f * i) * hr, new float3(0.1f * hr), Shade(look.Accent, 0.9f), 5, 3);
                            }

                            break;
                        default:
                            for (int i = 0; i < spines; i++)
                            {
                                float k = i / (float)math.max(1, spines - 1);
                                float3 root = c + new float3(0f, (0.95f - 0.75f * k) * hr, -(0.2f + 0.75f * k) * hr);
                                b.Panel(root + new float3(0f, 0f, 0.14f * hr), root + new float3(0f, -0.1f * hr, -0.14f * hr),
                                    root + new float3(0f, 0.4f * hr, -0.3f * hr), Shade(look.Accent, 0.95f));
                            }

                            break;
                    }

                    if (!dragon && hv.NextUInt(10) < 3)
                    {
                        b.Panel(muzzle + new float3(0f, -0.35f * hr, 0.4f * hr), muzzle + new float3(0f, -0.4f * hr, -0.3f * hr),
                            muzzle + new float3(0f, -0.95f * hr, -0.05f * hr), Shade(look.Accent, 1.1f));
                    }

                    eye = Pick(ReptileEyes, ref hv);
                    lit = true;
                    eyeX = 0.55f;
                    eyeZ = 0.7f;
                    eyeY = 0.2f;
                    break;
                }
                case NpcRace.Zombie:
                {
                    eye = Hex(0xD8E890);
                    lit = true;
                    float a = math.lerp(-1.2f, 1.2f, hv.NextFloat());
                    b.Ball(c + new float3(math.sin(a) * 0.55f, 0.55f, math.cos(a) * 0.5f) * hr, new float3(0.3f, 0.25f, 0.3f) * hr, look.Bone, 5, 3);
                    if (hv.NextUInt(2) == 0)
                    {
                        // Stitches across the face, darker than the skin they hold together.
                        float3 from = c + new float3(-0.5f, 0.35f, hs.Front(-0.5f, 0.35f) + 0.01f) * hr, to = c + new float3(0.1f, -0.3f, hs.Front(0.1f, -0.3f) + 0.01f) * hr;
                        b.Slab(from, to, 0.03f * hr, 0.03f * hr, Shade(skin, 0.5f));
                        for (int i = 1; i <= 3; i++)
                        {
                            float3 p = math.lerp(from, to, i / 4f);
                            b.Slab(p + new float3(-0.08f, -0.06f, 0f) * hr, p + new float3(0.08f, 0.06f, 0f) * hr, 0.025f * hr, 0.025f * hr, Shade(skin, 0.45f));
                        }
                    }

                    if (hv.NextUInt(10) < 4)
                    {
                        b.Ball(c + new float3(0f, -0.48f, hs.Front(0f, -0.48f) - 0.04f) * hr, new float3(0.2f, 0.14f, 0.08f) * hr, Hex(0x2A1E20), 6, 3);
                    }

                    Mark(b, glow, beast);
                    Ears(b, c, hr, earSide, skin, 0.18f * ear, 0.2f, 0.05f, hv.NextUInt(4) == 0 ? 0f : 1f);
                    Mark(b, glow, true);
                    break;
                }
                case NpcRace.Skeleton:
                {
                    bool open = hv.NextUInt(10) < 4;
                    b.Ball(c + new float3(0f, open ? -0.67f : -0.55f, 0.25f) * hr, new float3(0.55f, 0.35f, 0.6f) * hr, Shade(skin, 0.92f), 7, 4);
                    for (int s = -1; s <= 1; s += 2)
                    {
                        b.Ball(c + new float3(s * 0.38f * hr, 0.05f * hr, 0.8f * hr), new float3(0.24f, 0.26f, 0.16f) * hr, Hex(0x1A1618), 6, 4);
                    }

                    b.Ball(c + new float3(0f, -0.25f * hr, 0.9f * hr), new float3(0.1f, 0.14f, 0.08f) * hr, Hex(0x1A1618), 5, 3);
                    if (hv.NextUInt(10) < 4)
                    {
                        float3 at = c + new float3(math.lerp(-0.4f, 0.4f, hv.NextFloat()), 0.75f, 0.45f) * hr;
                        b.Slab(at, at + new float3(0.12f, -0.3f, 0.2f) * hr, 0.03f * hr, 0.03f * hr, Hex(0x3A3430));
                    }

                    eye = Color.Lerp(Pick(BoneGlow, ref hv), look.Glow, 0.25f);
                    eyes = hv.NextUInt(7) != 0;
                    lit = true;
                    eyeSize = 0.09f;
                    eyeZ = 0.86f;
                    break;
                }
                case NpcRace.Werewolf:
                {
                    float len = math.lerp(0.8f, 1.15f, hv.NextFloat()) * 0.95f;
                    Color snoutTone = hv.NextUInt(2) == 0 ? Shade(skin, 1.25f) : Shade(skin, 0.8f);
                    b.Tube(muzzle, muzzle + new float3(0f, -0.12f * hr, len * hr), 0.5f * hr, 0.26f * hr, skin, snoutTone, 7);
                    b.Ball(muzzle + new float3(0f, -0.06f * hr, (len + 0.05f) * hr), new float3(0.16f) * hr, Hex(0x1A1618), 5, 3);
                    float earSize = math.lerp(0.75f, 1.25f, hv.NextFloat()), splay = math.lerp(0.05f, 0.4f, hv.NextFloat());
                    int notch = hv.NextUInt(5) == 0 ? 1 : 0;
                    for (int s = -1; s <= 1; s += 2)
                    {
                        float3 root = c + new float3(s * 0.5f * hr, 0.75f * hr, -0.1f * hr);
                        float k = earSize * (notch == s ? 0.65f : 1f);
                        b.Tube(root, root + new float3(s * splay * hr, 0.7f * k * hr, -0.05f * hr), 0.28f * hr, 0.02f, skin, Shade(skin, 0.8f), 4);
                    }

                    // The ruff: what turns a tall dog-headed man into a wolf from across a square.
                    float ruff = math.lerp(0.85f, 1.25f, hv.NextFloat());
                    b.Ball(r.Neck + new float3(0f, -0.01f * r.H, -0.01f * r.H), new float3(r.TrunkX * 1.02f * ruff, 0.07f * r.H * ruff, r.TrunkZ * 1.5f),
                        Shade(skin, 1.12f), 8, 4);
                    eye = Pick(WolfEyes, ref hv);
                    lit = true;
                    eyeY = 0.2f;
                    break;
                }
                case NpcRace.Vampire:
                {
                    Ears(b, c, hr, earSide, skin, math.lerp(0.15f, 0.6f, hv.NextFloat()) * ear, 0.4f, 0.1f);
                    eye = Pick(VampireEyes, ref hv);
                    lit = true;
                    float fang = math.lerp(0.12f, 0.22f, hv.NextFloat());
                    for (int s = -1; s <= 1; s += 2)
                    {
                        b.Tube(c + new float3(s * 0.14f, -0.42f, hs.Front(0.14f, -0.42f) - 0.02f) * hr, c + new float3(s * 0.14f, -0.42f - fang, hs.Front(0.14f, -0.42f) - 0.02f) * hr,
                            0.035f * hr, 0.005f, Color.white, Color.white, 4);
                    }

                    break;
                }
                case NpcRace.Demon:
                {
                    Color horn = Pick(DemonHorns, ref hv);
                    Horns(b, c, hr, horn, Color.Lerp(horn, Hex(0x5A4A48), 0.4f), 4u + hv.NextUInt(5), math.lerp(0.8f, 1.25f, hv.NextFloat()));
                    Ears(b, c, hr, earSide, skin, math.lerp(0.3f, 0.6f, hv.NextFloat()) * ear, 0.3f, 0.35f);
                    eye = Pick(DemonEyes, ref hv);
                    lit = true;
                    break;
                }
                case NpcRace.Angel:
                    Mark(b, glow, beast);
                    Ears(b, c, hr, earSide, skin, 0.18f * ear, 0.2f, 0.05f);
                    Mark(b, glow, true);
                    eye = Pick(AngelIris, ref hv);
                    break;
                default:
                    Mark(b, glow, beast);
                    Ears(b, c, hr, earSide, skin, 0.18f * ear, 0.2f, 0.05f * ear);
                    Mark(b, glow, true);
                    break;
            }

            bool plainFace = spec.Race == NpcRace.Human || spec.Race == NpcRace.Elf || spec.Race == NpcRace.Orc ||
                             spec.Race == NpcRace.Zombie || spec.Race == NpcRace.Vampire || spec.Race == NpcRace.Angel ||
                             spec.Race == NpcRace.Demon;
            eyeX *= f.EyeSpread;
            eyeSize *= f.EyeSize;
            // Eyes sit in the skull, not on it: from here eyeZ is the skull's own front at the eye, and every eye below is
            // placed so only a shallow cap shows past it. A skeleton's lights stay deep in their sockets.
            if (spec.Race != NpcRace.Skeleton)
            {
                eyeZ = hs.Front(eyeX, eyeY);
            }

            Blood(b, spec, c, hr, hs, earSide, skin, ear, ref eye, out Color odd, ref hv);
            Mark(b, glow, beast);
            if (plainFace)
            {
                float fl = f.FaceLen;
                Color shade = Shade(skin, 0.93f);
                // Cheekbones, jaw, chin and soft cheeks are the head's own surface (HeadShape); what is added below is
                // only what stands off it: nose, lips, brows, eyes.

                // Nose: a bridge from between the eyes to a tip, plus the wings of the nostrils. Length, width,
                // projection and the tip's tilt are drawn apart, so a long nose can be narrow or broad, hooked or snub.
                float tipY = -0.1f - 0.1f * f.NoseLen + f.Tip;
                float3 tip = c + new float3(0f, tipY, hs.Front(0f, tipY) + 0.08f + 0.1f * (f.NoseLen - 1f) + 0.08f * f.Bridge) * hr;
                b.Tube(c + new float3(0f, eyeY - 0.02f, hs.Front(0f, eyeY) - 0.1f) * hr, tip, 0.055f * f.NoseW * hr, 0.08f * f.NoseW * hr, shade, shade, 5);
                b.Ball(tip, new float3(0.1f * f.NoseW, 0.09f, 0.1f) * hr, shade, 6, 3);
                for (int s = -1; s <= 1; s += 2)
                {
                    b.Ball(tip + new float3(s * 0.09f * f.NoseW, -0.03f, -0.05f) * hr, new float3(0.06f * f.NoseW, 0.05f, 0.06f) * hr, Shade(skin, 0.9f), 5, 3);
                }

                if (spec.Health01 < 0.5f || spec.Age01 > 0.75f)
                {
                    // Hollows under the eyes: tiredness or age, read before any bandage.
                    Color hollow = Color.Lerp(skin, Hex(0x5A4048), 0.35f);
                    for (int s = -1; s <= 1; s += 2)
                    {
                        b.Ball(c + new float3(s * eyeX, eyeY - 0.17f, hs.Front(eyeX, eyeY - 0.17f) - 0.07f) * hr, new float3(0.2f, 0.07f, 0.1f) * hr, hollow, 6, 3);
                    }
                }

                if ((spec.Race == NpcRace.Human || spec.Race == NpcRace.Elf) && Value(skin) > 0.75f && hv.NextUInt(7) == 0)
                {
                    Color freckle = Color.Lerp(skin, Hex(0x8A5030), 0.45f);
                    for (int i = 0; i < 8; i++)
                    {
                        float s = (i & 1) == 0 ? -1f : 1f;
                        float fx = s * (0.22f + 0.07f * (i % 3)), fy = -0.12f - 0.05f * ((i >> 1) % 2);
                        float3 at = c + new float3(fx, fy, hs.Front(fx, fy) + 0.005f) * hr;
                        b.Ball(at, new float3(0.025f * hr), freckle, 4, 2);
                    }
                }

                // Lips: upper and lower drawn apart (thin over full, or the reverse); the corners carry the mood.
                // The mouth never rises into the nose however short the face.
                float mouthY = math.min(-0.42f * math.lerp(1f, fl, 0.5f) + f.MouthY, (tip.y - c.y) / hr - 0.16f);
                Color lip = Color.Lerp(skin, Hex(0x653F46), 0.56f), upperLip = Shade(lip, 0.9f);
                float3 mouth = c + new float3(0f, mouthY, hs.Front(0f, mouthY)) * hr;
                float w = 0.19f * f.LipW;
                // The corners follow the face round: a wide jaw keeps the mouth flat, a narrow one wraps it back.
                float cornerZ = hs.Front(w, mouthY) - 0.01f;
                float3 cornerL = c + new float3(-w, mouthY + f.Curve, cornerZ) * hr, cornerR = c + new float3(w, mouthY + f.Curve, cornerZ) * hr;
                float3 top = mouth + new float3(0f, 0.022f, 0.012f) * hr, bottom = mouth + new float3(0f, -0.03f, 0.012f) * hr;
                float up = 0.035f * f.LipUp * hr, low = 0.045f * f.LipLow * hr, corner = 0.018f * hr;
                b.Tube(cornerL, top, corner, up, upperLip, upperLip, 5);
                b.Tube(top, cornerR, up, corner, upperLip, upperLip, 5);
                b.Tube(cornerL, bottom, corner, low, lip, lip, 5);
                b.Tube(bottom, cornerR, low, corner, lip, lip, 5);

                // Brows: two strokes meeting at the arch. Weight, arch, slant, length and the gap between them give a
                // face its mood before anything else does.
                Color browTone = Shade(look.Hair, 0.8f);
                float thick = 0.06f * f.BrowThick * hr;
                for (int s = -1; s <= 1; s += 2)
                {
                    float ix = eyeX - 0.18f + f.BrowGap, iy = eyeY + 0.25f + f.BrowSlant;
                    float ox = eyeX + 0.18f * f.BrowLen, oy = eyeY + 0.21f - f.BrowSlant;
                    float3 inner = c + new float3(s * ix, iy, hs.Front(ix, iy) + 0.03f) * hr;
                    float3 outer = c + new float3(s * ox, oy, hs.Front(ox, oy) + 0.02f) * hr;
                    float3 peak = math.lerp(inner, outer, 0.55f) + new float3(0f, 0.02f + f.BrowArch, 0.01f) * hr;
                    b.Slab(inner, peak, 0.07f * hr, thick, browTone);
                    b.Slab(peak, outer, 0.07f * hr, thick * 0.8f, browTone);
                }
            }

            if (look.Child)
            {
                eyeSize *= 1.25f;
            }

            for (int s = -1; s <= 1 && eyes; s += 2)
            {
                // The front of the skull at this eye; whites, iris and lid are all set against it, so a wide or narrow
                // skull keeps the same shallow eye instead of one that floats in front of the face.
                float3 surface = c + new float3(s * eyeX, eyeY, eyeZ) * hr;
                float open = 1f;
                float depth = 0.32f * eyeSize;
                float3 iris;
                if (plainFace && !lit)
                {
                    // Two overlapping halves, the outer one raised or lowered: almond, upturned, downturned or round
                    // eyes from ellipsoids alone. Whites are flattened lenses that clear the skin by 0.015 head radii;
                    // the upper lid overhangs them and thickens as the eye narrows (hooded, sleepy, old).
                    open = f.EyeOpen;
                    float tilt = f.EyeTilt;
                    const float WhiteDepth = 0.06f, Clear = 0.015f;
                    Color white = Hex(0xEEE2CF);
                    float3 whites = surface + new float3(0f, 0f, Clear - WhiteDepth) * hr;
                    b.Ball(whites + new float3(-s * 0.06f, -tilt * 0.35f, 0f) * hr, new float3(0.15f, 0.13f * open, WhiteDepth) * hr, white, 8, 4);
                    b.Ball(whites + new float3(s * 0.07f, tilt * 0.35f, 0f) * hr, new float3(0.15f, 0.12f * open, WhiteDepth) * hr, white, 8, 4);
                    b.Slab(surface + new float3(-s * 0.2f, 0.12f * open - tilt * 0.35f, Clear) * hr, surface + new float3(s * 0.21f, 0.11f * open + tilt * 0.35f, Clear) * hr,
                        0.045f * hr, 0.03f * hr * (2.2f - open), Shade(skin, 0.82f));
                    // The iris clears the whites by a hair, never a hemisphere: a painted iris, as the toon look wants.
                    iris = surface + new float3(0f, 0f, Clear + 0.006f) * hr - new float3(0f, 0f, depth * hr);
                }
                else
                {
                    iris = surface + new float3(0f, 0f, 0.025f) * hr - new float3(0f, 0f, depth * hr);
                }

                if (spec.Race == NpcRace.Skeleton)
                {
                    iris = surface;
                    depth = 0.6f * eyeSize;
                }

                Color tone = s > 0 && odd.a > 0f ? odd : eye;
                (lit ? glow : b).Ball(iris, new float3(eyeSize, eyeSize * 1.15f * math.min(1f, open), depth) * hr, tone, 6, 4);
            }

            Mark(b, glow, true);
            if (!t.Furred)
            {
                Hair(b, spec, r, look, ref rng);
            }

            if (look.Beard)
            {
                Beard(b, c, hr, look);
            }
        }

        // The head as one surface, not a sphere with a jaw and cheekbones stuck on: horizontal superellipse sections from
        // the underside of the chin to the crown. Width is set at four heights (chin, angle of the jaw, cheekbones,
        // temples), each section also has a depth, a forward shift (the jaw and chin sit in front of the neck) and a
        // squareness exponent (2 = round, larger = flat sides and front, the square jaw). Between the keys a monotone
        // cubic keeps the outline smooth without bulging past either key; above the temples the cranium is an
        // elliptical dome. Units are head radii with the origin at the head centre.
        // REQ: characters.md#路人-npc (臉型).
        private readonly struct HeadShape
        {
            private const float TempleY = 0.2f;
            private readonly float[] _y, _w, _d, _z, _n;

            public readonly float Top, Bottom;

            public HeadShape(in Face f)
            {
                float fl = f.FaceLen, sq = f.JawAngle, fat = f.Cheek, jowl = f.Jowl;
                Top = f.SkullH;
                Bottom = -(0.78f * fl + 0.2f * f.ChinH) - 0.06f * jowl;
                float jawY = -0.62f * fl;
                float chinY = math.min(Bottom + 0.14f * f.ChinH, jawY - 0.06f);
                float cranium = 0.97f * f.SkullD;
                float chin = 0.3f * f.ChinW * (1f + 0.4f * jowl);
                _y = new[] { Bottom, chinY, jawY, -0.18f * fl, TempleY };
                // Soft cheeks and a double chin widen the lower face; they belong to the outline, not to extra balls.
                _w = new[] { 0.45f * chin, chin, 0.74f * f.JawW * (1f + 0.25f * fat), 0.9f * f.CheekW * (1f + 0.3f * fat), 0.9f * f.SkullW };
                _d = new[] { 0.12f + 0.08f * jowl, 0.36f + 0.1f * jowl, 0.6f + 0.1f * fat, 0.9f * cranium, cranium };
                _z = new[] { 0.5f + f.ChinJut, 0.5f + f.ChinJut, 0.2f, 0.05f + 0.04f * fat, 0f };
                _n = new[] { 2f, 2f + 1.2f * sq, 2f + 2.6f * sq, 2f + 0.6f * sq, 2f };
            }

            // Half width of the section at height y.
            public float Width(float y) => y > TempleY ? _w[4] * Dome(y) : Pchip(_y, _w, y);

            // Half depth of the section at height y.
            public float Depth(float y) => y > TempleY ? _d[4] * Dome(y) : Pchip(_y, _d, y);

            // Forward shift of the section's centre at height y.
            public float Shift(float y) => y > TempleY ? 0f : Pchip(_y, _z, y);

            // Superellipse exponent of the section at height y.
            public float Exponent(float y) => y > TempleY ? 2f : Pchip(_y, _n, y);

            // Depth of the front surface at (x, y); past the section's edge, its centre (a side-set eye stays in the head).
            public float Front(float x, float y)
            {
                float w = Width(y), n = Exponent(y);
                if (w < 1e-4f)
                {
                    return Shift(y);
                }

                float u = math.min(1f, math.abs(x) / w);
                return Shift(y) + Depth(y) * math.pow(math.max(0f, 1f - math.pow(u, n)), 1f / n);
            }

            private float Dome(float y)
            {
                float t = math.saturate((y - TempleY) / math.max(1e-3f, Top - TempleY));
                return math.sqrt(1f - t * t);
            }

            // Monotone cubic (Fritsch–Butland slopes), level at both end keys: the temple is the widest point of the
            // cranium and the chin's underside levels off.
            private static float Pchip(float[] xs, float[] ys, float x)
            {
                int last = xs.Length - 1;
                if (x <= xs[0])
                {
                    return ys[0];
                }

                if (x >= xs[last])
                {
                    return ys[last];
                }

                int i = 0;
                while (x > xs[i + 1])
                {
                    i++;
                }

                float h = xs[i + 1] - xs[i], t = (x - xs[i]) / h, t2 = t * t, t3 = t2 * t;
                return (2f * t3 - 3f * t2 + 1f) * ys[i] + (t3 - 2f * t2 + t) * h * Slope(xs, ys, i) +
                       (-2f * t3 + 3f * t2) * ys[i + 1] + (t3 - t2) * h * Slope(xs, ys, i + 1);
            }

            private static float Slope(float[] xs, float[] ys, int i)
            {
                if (i == 0 || i == xs.Length - 1)
                {
                    return 0f;
                }

                float d0 = (ys[i] - ys[i - 1]) / (xs[i] - xs[i - 1]), d1 = (ys[i + 1] - ys[i]) / (xs[i + 1] - xs[i]);
                return d0 * d1 <= 0f ? 0f : 2f / (1f / d0 + 1f / d1);
            }
        }

        // 18 sections × 16 around, spaced by a cosine so the chin and crown, where the outline turns fastest, get the
        // most rows. Shading darkens toward the chin like a Ball's.
        private static void HeadShell(Fig b, float3 c, float hr, in HeadShape hs, Color tone)
        {
            const int Rows = 18, Sides = 16;
            var points = new float3[Rows * Sides];
            var tones = new Color[Rows];
            Color low = Shade(tone, 0.88f);
            for (int i = 0; i < Rows; i++)
            {
                float u = (i + 1f) / (Rows + 1f);
                float y = math.lerp(hs.Bottom, hs.Top, 0.5f - 0.5f * math.cos(math.PI * u));
                float w = hs.Width(y), d = hs.Depth(y), z = hs.Shift(y), e = 2f / hs.Exponent(y);
                tones[i] = Color.Lerp(low, tone, math.saturate((y - hs.Bottom) / (hs.Top - hs.Bottom)));
                for (int k = 0; k < Sides; k++)
                {
                    float a = k * math.PI * 2f / Sides, ca = math.cos(a), sa = math.sin(a);
                    float x = w * math.sign(ca) * math.pow(math.abs(ca), e), zz = z + d * math.sign(sa) * math.pow(math.abs(sa), e);
                    points[i * Sides + k] = c + new float3(x, y, zz) * hr;
                }
            }

            b.Shell(points, tones, Rows, Sides, c + new float3(0f, hs.Bottom, hs.Shift(hs.Bottom)) * hr, c + new float3(0f, hs.Top, 0f) * hr, low, tone);
        }

        /// <summary>The face shape drawn for <paramref name="spec"/> and its head's half widths at temple, cheekbone, jaw and chin, and chin-to-crown height (head radii).</summary>
        /// <remarks>Pre: none. Post: widths &gt; 0; same spec, same result. Side effects: none.</remarks>
        internal static (FaceShape shape, float temple, float cheek, float jaw, float chin, float height) HeadOf(in NpcSpec spec)
        {
            Face f = FaceOf(spec);
            var hs = new HeadShape(f);
            float fl = f.FaceLen;
            return (f.Shape, hs.Width(TempleProbe), hs.Width(-0.18f * fl), hs.Width(-0.62f * fl), hs.Width(-0.78f * fl), hs.Top - hs.Bottom);
        }

        private const float TempleProbe = 0.2f;

        // Mixed blood shows as a trace of the other race's signature part, sized by its share: a quarter-elf's ear is
        // barely pointed, a half-orc's tusks just clear the lip. Where the own race already grows that kind of part
        // (horns, big ears, fangs) the own one stands alone. Eyes take a blood line's colour by its share, and about one
        // mixed resident in ten has one eye of each.
        // REQ: characters.md#路人-npc (混血與跨文化).
        private static void Blood(Fig b, in NpcSpec spec, float3 c, float hr, in HeadShape skull, float earSide, Color skin, float ear, ref Color eye,
            out Color odd, ref Pcg32 hv)
        {
            odd = Color.clear;
            NpcHeritage h = spec.Heritage;
            if (h.BloodLines == 0)
            {
                return;
            }

            NpcRace own = spec.Race;
            bool plainEars = own == NpcRace.Human || own == NpcRace.Dwarf || own == NpcRace.Orc || own == NpcRace.Vampire ||
                             own == NpcRace.Angel || own == NpcRace.Demon;
            bool horned = own == NpcRace.Demon || own == NpcRace.Dragonkin;
            for (int i = 0; i < h.BloodLines; i++)
            {
                NpcRace race = h.BloodRace(i);
                float share = h.BloodShare(i);
                // Half blood draws the trace at 0.8 of the pure part, a 1/16 line at about a third.
                float k = 0.25f + 1.1f * share;
                int[] palette = EyePalette(race);
                if (palette != null && hv.NextFloat() < share)
                {
                    eye = Pick(palette, ref hv);
                }

                switch (race)
                {
                    case NpcRace.Elf when plainEars:
                        Ears(b, c, hr, earSide, skin, 0.85f * k * ear, 0.5f * k, 0.12f * k * ear);
                        break;
                    case NpcRace.Goblin when plainEars:
                        Ears(b, c, hr, earSide, skin, 0.3f * k * ear, 0.2f * k, 1f * k * ear);
                        break;
                    case NpcRace.Werewolf when plainEars:
                        for (int s = -1; s <= 1; s += 2)
                        {
                            float3 root = c + new float3(s * 0.55f, 0.65f, -0.1f) * hr;
                            b.Tube(root, root + new float3(s * 0.15f, 0.55f * k, -0.05f) * hr, 0.22f * k * hr, 0.02f, skin, Shade(skin, 0.8f), 4);
                        }

                        break;
                    case NpcRace.Merfolk when plainEars:
                        for (int s = -1; s <= 1; s += 2)
                        {
                            float3 root = c + new float3(s * earSide, 0f, 0f) * hr;
                            b.Panel(root + new float3(0f, 0.2f * hr, 0.08f * hr), root + new float3(0f, -0.22f * hr, 0.08f * hr),
                                root + new float3(s * 0.6f * k, 0.2f * k, -0.35f * k) * hr, Shade(Pick(SkinTones[(int)NpcRace.Merfolk], ref hv), 1.05f));
                        }

                        break;
                    case NpcRace.Orc when own != NpcRace.Orc && own != NpcRace.Avian && own != NpcRace.Lizardfolk && own != NpcRace.Dragonkin && own != NpcRace.Werewolf:
                        for (int s = -1; s <= 1; s += 2)
                        {
                            float3 at = c + new float3(s * 0.3f, -0.55f, skull.Front(0.3f, -0.55f) + 0.04f) * hr;
                            b.Tube(at, at + new float3(s * 0.02f, 0.22f * k, 0.04f) * hr, 0.06f * k * hr, 0.015f, Hex(0xE8E2D0), Hex(0xE8E2D0), 5);
                        }

                        break;
                    case NpcRace.Vampire when own != NpcRace.Vampire && own != NpcRace.Avian:
                        for (int s = -1; s <= 1; s += 2)
                        {
                            float3 at = c + new float3(s * 0.13f, -0.42f, skull.Front(0.13f, -0.42f) + 0.02f) * hr;
                            b.Tube(at, at - new float3(0f, 0.13f * k, 0f) * hr, 0.03f * hr, 0.005f, Color.white, Color.white, 4);
                        }

                        break;
                    case NpcRace.Demon when !horned:
                    case NpcRace.Dragonkin when !horned:
                    {
                        // Horn buds: the roots grow with the share, not only the length, or a trace reads as two pins.
                        Color root = race == NpcRace.Demon ? Pick(DemonHorns, ref hv) : Hex(0xE8E2D0);
                        float sweep = race == NpcRace.Demon ? 0.15f : -0.4f;
                        for (int s = -1; s <= 1; s += 2)
                        {
                            float3 a = c + new float3(s * 0.42f, 0.72f, 0.15f) * hr;
                            b.Tube(a, a + new float3(s * 0.12f, 0.5f, sweep) * hr * k, 0.13f * k * hr, 0.015f, root, Shade(root, 0.8f), 5);
                        }

                        if (race == NpcRace.Dragonkin)
                        {
                            Scales(b, c, hr, skull, Shade(skin, 0.86f), k);
                        }

                        break;
                    }
                    case NpcRace.Lizardfolk when !horned && own != NpcRace.Lizardfolk:
                        Scales(b, c, hr, skull, Shade(skin, 0.86f), k);
                        break;
                    case NpcRace.Avian when own != NpcRace.Avian:
                    {
                        // A tuft of crown feathers swept back: plumage in place of, or through, the hair.
                        Color plume = Pick(PlumeCrowns, ref hv);
                        for (int j = -1; j <= 1; j++)
                        {
                            float3 root = c + new float3(j * 0.18f, 0.85f, -0.35f) * hr;
                            b.Panel(root + new float3(-0.06f, 0f, 0.1f) * hr, root + new float3(0.06f, 0f, -0.1f) * hr,
                                root + new float3(j * 0.2f, 0.35f, -0.5f) * hr * k, Shade(plume, 1f - 0.08f * math.abs(j)));
                        }

                        break;
                    }
                    case NpcRace.Angel when own != NpcRace.Angel:
                    {
                        // Gilt freckles across the cheekbones: the celestial line shows in the skin before the eyes.
                        Color gilt = Color.Lerp(skin, Hex(0xF0C860), 0.7f);
                        for (int j = 0; j < 6; j++)
                        {
                            float x = ((j & 1) == 0 ? -1f : 1f) * (0.24f + 0.08f * (j % 3)), y = -0.08f - 0.05f * (j / 3);
                            b.Ball(c + new float3(x, y, skull.Front(x, y) + 0.005f) * hr, new float3(0.022f * hr), gilt, 4, 2);
                        }

                        break;
                    }
                }
            }

            if (hv.NextUInt(10) == 0)
            {
                int[] palette = EyePalette(h.BloodRace((int)hv.NextUInt((uint)h.BloodLines)));
                odd = palette != null ? Pick(palette, ref hv) : Color.clear;
            }
        }

        // A patch of five overlapping scales on each temple, flattened against the skull.
        private static void Scales(Fig b, float3 c, float hr, in HeadShape skull, Color tone, float k)
        {
            for (int s = -1; s <= 1; s += 2)
            {
                for (int j = 0; j < 5; j++)
                {
                    float x = s * (0.62f + 0.06f * (j % 2)), y = 0.32f - 0.12f * j;
                    float z = skull.Front(x, y);
                    b.Ball(c + new float3(x, y, z - 0.02f) * hr, new float3(0.09f, 0.07f, 0.04f) * k * hr, tone, 5, 3);
                }
            }
        }

        private static int[] EyePalette(NpcRace race) => race switch
        {
            NpcRace.Human => HumanIris,
            NpcRace.Dwarf => HumanIris,
            NpcRace.Elf => ElfIris,
            NpcRace.Orc => OrcIris,
            NpcRace.Angel => AngelIris,
            NpcRace.Goblin => GoblinEyes,
            NpcRace.Lizardfolk => ReptileEyes,
            NpcRace.Dragonkin => ReptileEyes,
            NpcRace.Werewolf => WolfEyes,
            NpcRace.Vampire => VampireEyes,
            NpcRace.Demon => DemonEyes,
            NpcRace.Merfolk => MerfolkEyes,
            _ => null,
        };

        // One face: every field is a bounded normal around the race's mean (set by FaceMean), shifted by sex and age.
        // Multipliers sit near 1, offsets near 0 (head radii).
        // REQ: characters.md#路人-npc (臉).
        internal struct Face
        {
            public float SkullW, SkullH, SkullD, CheekW, Cheek, FaceLen, JawW, ChinW, ChinJut, ChinH, Jowl;
            public float LipW, LipUp, LipLow, MouthY, Curve;
            public float NoseLen, NoseW, Bridge, Tip;
            public float EyeSize, EyeSpread, EyeTilt, EyeOpen;
            public float BrowThick, BrowArch, BrowSlant, BrowLen, BrowGap;
            public float JawAngle;  // 0 a rounded jaw .. 1 a squared corner at the angle of the jaw
            public FaceShape Shape;

            public static Face Draw(in NpcSpec spec, in Look look, bool ageless, ref Pcg32 rng)
            {
                float sex = spec.Sex == NpcSex.Male ? 1f : -1f;
                bool child = look.Child;
                float elder = ageless ? 0f : math.smoothstep(0.6f, 0.95f, spec.Age01);
                float fat = look.Fat - (spec.Sex == NpcSex.Female ? 0.06f : 0f);
                FaceShape shape = PickShape(spec, child, fat, ref rng);
                // Race mean, then the face shape on top: the shape is a family resemblance, the normals below are the person.
                Face m = MeanOf(spec);
                Add(ref m, ShapeMean(shape), 1f);
                var f = default(Face);
                f.Shape = shape;
                f.JawAngle = math.saturate(m.JawAngle + 0.15f * Gauss(ref rng));
                f.SkullW = N(1f + m.SkullW, 0.04f, ref rng);
                f.SkullH = N(1f + m.SkullH, 0.04f, ref rng);
                f.SkullD = N(1f + m.SkullD, 0.04f, ref rng);
                f.CheekW = N(1f + m.CheekW, 0.08f, ref rng);
                f.Cheek = math.clamp(2.5f * (fat - 0.2f) + (child ? 0.25f : 0f) + m.Cheek + 0.06f * Gauss(ref rng), 0f, 0.45f);
                f.FaceLen = N(1f + m.FaceLen - (child ? 0.1f : 0f), 0.06f, ref rng);
                f.JawW = N(1f + m.JawW + 0.06f * sex - (child ? 0.12f : 0f), 0.08f, ref rng);
                f.ChinW = N(1f + m.ChinW + 0.08f * sex, 0.12f, ref rng);
                f.ChinJut = N(m.ChinJut - (child ? 0.05f : 0f), 0.06f, ref rng);
                f.ChinH = N(1f + m.ChinH, 0.12f, ref rng);
                f.Jowl = math.saturate((fat - 0.3f) * 5f + 0.4f * elder - 0.2f);
                f.LipW = N(1f + m.LipW, 0.1f, ref rng);
                f.LipUp = Rel(1f + m.LipUp - 0.1f * sex - 0.25f * elder, 0.2f, ref rng);
                f.LipLow = Rel(1f + m.LipLow - 0.05f * sex - 0.2f * elder, 0.2f, ref rng);
                f.MouthY = N(m.MouthY, 0.03f, ref rng);
                f.Curve = N(m.Curve - 0.03f * elder, 0.035f, ref rng);
                f.NoseLen = N(1f + m.NoseLen + 0.18f * elder - (child ? 0.25f : 0f), 0.12f, ref rng);
                f.NoseW = N(1f + m.NoseW + 0.05f * sex, 0.14f, ref rng);
                f.Bridge = N(m.Bridge - (child ? 0.15f : 0f), 0.15f, ref rng);
                f.Tip = math.clamp(N(m.Tip, 0.05f, ref rng), -0.08f, 0.1f);
                f.EyeSize = N(1f + m.EyeSize - 0.03f * sex, 0.08f, ref rng);
                f.EyeSpread = N(1f + m.EyeSpread, 0.06f, ref rng);
                f.EyeTilt = math.clamp(N(m.EyeTilt, 0.03f, ref rng), -0.07f, 0.09f);
                f.EyeOpen = math.clamp(N(1f + m.EyeOpen - 0.12f * elder, 0.12f, ref rng), 0.55f, 1.4f);
                f.BrowThick = Rel(1f + m.BrowThick + 0.25f * sex, 0.2f, ref rng);
                f.BrowArch = N(m.BrowArch, 0.04f, ref rng);
                f.BrowSlant = N(m.BrowSlant, 0.045f, ref rng);
                f.BrowLen = N(1f + m.BrowLen, 0.1f, ref rng);
                f.BrowGap = N(m.BrowGap, 0.04f, ref rng);
                return f;
            }

            // Bounded normal (|z| ≤ 3.46) never reaches zero or flips a multiplier for these σ, so no clamp is needed
            // beyond the few fields that can collide with a neighbour (tip, lids).
            private static float N(float mean, float sigma, ref Pcg32 rng) => mean + sigma * Gauss(ref rng);

            // Thickness means fall well below 1 (an elf woman's brow 0.45, an old zombie's lip 0.35), where an absolute
            // σ 0.2 at |z| 3.46 turns them negative and the mesh inside out. Read as a share of the person's own mean,
            // σ 20 % keeps them above 0.3 × the mean with the same single draw, so no later field shifts.
            // REQ: characters.md#路人-npc (上唇與下唇厚度各 σ 20 %、眉毛粗細 σ 20 %).
            private static float Rel(float mean, float share, ref Pcg32 rng) => mean * (1f + share * Gauss(ref rng));

            // A mixed face averages the race means by blood share: a half-orc has half an orc's jaw, not a coin-flip one.
            private static Face MeanOf(in NpcSpec spec)
            {
                NpcHeritage h = spec.Heritage;
                Face m = default;
                Add(ref m, FaceMean(spec.Race), 1f - h.BloodShareTotal);
                for (int i = 0; i < h.BloodLines; i++)
                {
                    Add(ref m, FaceMean(h.BloodRace(i)), h.BloodShare(i));
                }

                return m;
            }

            private static void Add(ref Face a, in Face b, float k)
            {
                a.SkullW += k * b.SkullW;
                a.SkullH += k * b.SkullH;
                a.SkullD += k * b.SkullD;
                a.CheekW += k * b.CheekW;
                a.Cheek += k * b.Cheek;
                a.FaceLen += k * b.FaceLen;
                a.JawW += k * b.JawW;
                a.ChinW += k * b.ChinW;
                a.ChinJut += k * b.ChinJut;
                a.ChinH += k * b.ChinH;
                a.Jowl += k * b.Jowl;
                a.LipW += k * b.LipW;
                a.LipUp += k * b.LipUp;
                a.LipLow += k * b.LipLow;
                a.MouthY += k * b.MouthY;
                a.Curve += k * b.Curve;
                a.NoseLen += k * b.NoseLen;
                a.NoseW += k * b.NoseW;
                a.Bridge += k * b.Bridge;
                a.Tip += k * b.Tip;
                a.EyeSize += k * b.EyeSize;
                a.EyeSpread += k * b.EyeSpread;
                a.EyeTilt += k * b.EyeTilt;
                a.EyeOpen += k * b.EyeOpen;
                a.BrowThick += k * b.BrowThick;
                a.BrowArch += k * b.BrowArch;
                a.BrowSlant += k * b.BrowSlant;
                a.BrowLen += k * b.BrowLen;
                a.BrowGap += k * b.BrowGap;
                a.JawAngle += k * b.JawAngle;
            }

            // Fat rounds a face and children's faces are round before bone sets them; men run square, women oval or heart.
            private static FaceShape PickShape(in NpcSpec spec, bool child, float fat, ref Pcg32 rng)
            {
                float[] odds = (float[])ShapeOdds.Clone();
                bool male = spec.Sex == NpcSex.Male;
                odds[(int)FaceShape.Square] *= male ? 1.8f : 0.6f;
                odds[(int)FaceShape.Oval] *= male ? 1f : 1.3f;
                odds[(int)FaceShape.InvertedTriangle] *= male ? 0.8f : 1.4f;
                float plump = math.saturate((fat - 0.24f) * 8f);
                odds[(int)FaceShape.Round] *= (child ? 3f : 1f) * (1f + 3f * plump);
                odds[(int)FaceShape.Long] *= 1f - 0.7f * plump;
                odds[(int)FaceShape.InvertedTriangle] *= 1f - 0.7f * plump;
                NpcHeritage h = spec.Heritage;
                Lean(odds, spec.Race, 1f - h.BloodShareTotal);
                for (int i = 0; i < h.BloodLines; i++)
                {
                    Lean(odds, h.BloodRace(i), h.BloodShare(i));
                }

                return (FaceShape)Weighted(odds, ref rng);
            }

            private static void Lean(float[] odds, NpcRace race, float share)
            {
                float[] lean = (int)race < RaceShape.Length ? RaceShape[(int)race] : null;
                for (int i = 0; lean != null && i < odds.Length; i++)
                {
                    odds[i] *= math.lerp(1f, lean[i], share);
                }
            }
        }

        /// <summary>The six face shapes (臉型).</summary>
        internal enum FaceShape
        {
            Oval, Round, Long, Square, Triangle, InvertedTriangle,
        }

        // Base odds of each face shape (oval, round, long, square, triangle, inverted triangle).
        private static readonly float[] ShapeOdds = { 3f, 1.5f, 1.2f, 1.2f, 0.6f, 1f };

        // Per race, multipliers on the face-shape odds; null = the human odds.
        private static readonly float[][] RaceShape =
        {
            null,                                         // Human
            new[] { 1.5f, 0.3f, 2f, 0.4f, 0.2f, 2f },     // Elf
            new[] { 0.6f, 2f, 0.3f, 2.5f, 1.2f, 0.3f },   // Dwarf
            new[] { 0.6f, 1.2f, 0.4f, 0.5f, 0.6f, 3f },   // Goblin: a big cranium over a small chin
            new[] { 0.5f, 0.8f, 0.6f, 3f, 2.5f, 0.2f },   // Orc
            null, null, null, null,                       // Avian, Merfolk, Lizardfolk, Dragonkin
            new[] { 1f, 0.3f, 2f, 1f, 1f, 1f },           // Zombie: the flesh has fallen in
            null,                                         // Skeleton
            null,                                         // Werewolf
            new[] { 1.2f, 0.2f, 2.5f, 0.6f, 0.3f, 2f },   // Vampire
            new[] { 2f, 1.5f, 0.6f, 0.4f, 0.2f, 1f },     // Angel
            new[] { 1f, 0.4f, 1.2f, 1.2f, 0.8f, 2f },     // Demon
        };

        // What each shape adds to the race mean. Width is read at three heights (temples = skull, cheekbones, jaw); length
        // and the chin finish it. Triangle = narrow brow over a wide jaw; inverted triangle = wide brow tapering to a point.
        // REQ: characters.md#路人-npc (臉型).
        private static Face ShapeMean(FaceShape shape) => shape switch
        {
            FaceShape.Round => new Face { FaceLen = -0.14f, SkullW = 0.07f, CheekW = 0.06f, JawW = 0.12f, ChinW = 0.2f, ChinH = -0.15f, Cheek = 0.15f },
            FaceShape.Long => new Face { FaceLen = 0.2f, SkullW = -0.06f, SkullH = 0.07f, CheekW = -0.04f, JawW = -0.06f, ChinH = 0.22f },
            FaceShape.Square => new Face { FaceLen = -0.03f, JawW = 0.18f, ChinW = 0.36f, ChinH = -0.1f, JawAngle = 0.9f },
            FaceShape.Triangle => new Face { SkullW = -0.08f, CheekW = -0.04f, JawW = 0.2f, ChinW = 0.25f, JawAngle = 0.6f },
            FaceShape.InvertedTriangle => new Face { SkullW = 0.06f, CheekW = 0.1f, JawW = -0.18f, ChinW = -0.4f, ChinH = 0.12f, ChinJut = 0.025f },
            _ => new Face { JawW = -0.03f, ChinW = -0.06f, JawAngle = 0.1f },
        };

        /// <summary>The face shape drawn for <paramref name="spec"/> (what Build gives it).</summary>
        /// <remarks>Pre: none. Post: defined; same spec, same shape. Side effects: none.</remarks>
        internal static FaceShape ShapeOf(in NpcSpec spec) => FaceOf(spec).Shape;

        // Replays Build's draws up to the face: the main stream through the look, then the head stream's first draws.
        private static Face FaceOf(in NpcSpec spec)
        {
            var rng = new Pcg32(Salt ^ spec.Seed, ((ulong)spec.Race << 16) | ((ulong)spec.Role << 8) | (ulong)spec.Sex);
            Traits traits = TraitsOf(spec);
            Look look = Look.Draw(spec, traits, ref rng);
            var hv = new Pcg32(Salt ^ spec.Seed, 0x48454144ul);
            bool ageless = spec.Race == NpcRace.Vampire || spec.Race == NpcRace.Angel || spec.Race == NpcRace.Demon;
            return Face.Draw(spec, look, ageless, ref hv);
        }

        // Each race's departure from the human mean face (0 = same as a human).
        private static Face FaceMean(NpcRace race) => race switch
        {
            NpcRace.Elf => new Face
            {
                SkullW = -0.06f, FaceLen = 0.06f, JawW = -0.08f, ChinW = -0.1f, LipUp = -0.15f, LipLow = -0.15f, NoseW = -0.12f,
                NoseLen = 0.05f, EyeTilt = 0.035f, EyeOpen = -0.1f, BrowArch = 0.03f, BrowThick = -0.3f,
            },
            NpcRace.Dwarf => new Face { SkullW = 0.06f, NoseW = 0.2f, BrowThick = 0.4f, FaceLen = -0.05f },
            NpcRace.Goblin => new Face { SkullW = 0.04f, EyeSize = 0.1f },
            NpcRace.Orc => new Face
            {
                JawW = 0.25f, ChinW = 0.25f, JawAngle = 0.3f, ChinJut = 0.04f, NoseW = 0.35f, NoseLen = -0.15f, Bridge = -0.3f, LipUp = 0.2f, LipLow = 0.3f,
                BrowThick = 0.4f, EyeSize = -0.1f, EyeOpen = -0.1f,
            },
            NpcRace.Zombie => new Face { CheekW = -0.1f, LipUp = -0.3f, LipLow = -0.3f, EyeOpen = 0.1f, Curve = -0.03f },
            NpcRace.Vampire => new Face
            {
                SkullW = -0.04f, FaceLen = 0.05f, CheekW = 0.08f, JawW = -0.04f, LipUp = -0.2f, LipLow = -0.15f, NoseLen = 0.08f,
                NoseW = -0.1f, BrowArch = 0.05f, BrowSlant = 0.03f,
            },
            NpcRace.Angel => new Face { EyeSize = 0.1f, EyeOpen = 0.1f, LipUp = 0.05f, LipLow = 0.05f, NoseW = -0.08f, BrowThick = -0.2f, FaceLen = -0.03f },
            NpcRace.Demon => new Face { EyeTilt = 0.05f, BrowSlant = 0.06f, BrowArch = 0.04f, JawW = 0.05f, CheekW = 0.06f, Bridge = 0.1f },
            _ => default,
        };

        private static Color Pick(int[] tones, ref Pcg32 rng) => Hex(tones[rng.NextUInt((uint)tones.Length)]);

        // Pupils sit on whites for plain faces, so these are the iris read from a few metres: dark enough to hold.
        private static readonly int[] HumanIris = { 0x2A1A12, 0x3E2616, 0x5A4022, 0x3A5A7A, 0x3E6A44, 0x5A6066 };
        private static readonly int[] ElfIris = { 0x3A6A9A, 0x3E7A50, 0x6A4A8A, 0x7A8A90, 0x8A6A2A };
        private static readonly int[] OrcIris = { 0x3A2414, 0x8A5A1A, 0x8A2A1A };
        private static readonly int[] AngelIris = { 0x5A90C8, 0xB89A30, 0x5A9A8A, 0x7A6AB8 };
        private static readonly int[] GoblinEyes = { 0xF0D040, 0xF08A30, 0xE04030, 0xA0E040 };
        private static readonly int[] ReptileEyes = { 0xF0C030, 0xE06030, 0x60D060, 0xC040C0 };
        private static readonly int[] WolfEyes = { 0xF0C030, 0x9FD8F0, 0xA0E060 };
        private static readonly int[] VampireEyes = { 0xE83030, 0xA040E0, 0xE0B030 };
        private static readonly int[] DemonEyes = { 0xFFD040, 0xFF4030, 0xB060FF };
        private static readonly int[] BoneGlow = { 0x9FF0FF, 0x9FFF9F, 0xC090FF, 0xFFC060 };
        private static readonly int[] MerfolkEyes = { 0x20181A, 0x1A4A5A, 0x5A4A1A };
        private static readonly int[] BeakTones = { 0xE8B03A, 0xE07A2A, 0x3A3A40, 0xE8DCC0 };
        private static readonly int[] PlumeCrowns = { 0x2A2A30, 0xE8E4DA, 0xB8402E, 0x3A6AA8, 0x8A6A3A };
        private static readonly int[] DemonHorns = { 0x2A2226, 0xD8CCB0, 0x5A1A1A };

        // Finch cone, raptor hook, heron spear, broad bill: the four beak shapes that read at a distance.
        private static void Beak(Fig b, float3 muzzle, float hr, Color tone, uint style)
        {
            float3 root = muzzle + new float3(0f, 0.1f * hr, 0.1f * hr);
            Color tip = Shade(tone, 0.82f);
            switch (style)
            {
                case 1:
                {
                    float3 mid = muzzle + new float3(0f, 0.02f * hr, 0.7f * hr);
                    b.Tube(root, mid, 0.3f * hr, 0.14f * hr, tone, tone, 5);
                    b.Tube(mid, mid + new float3(0f, -0.3f * hr, 0.12f * hr), 0.14f * hr, 0.02f, tone, tip, 5);
                    break;
                }
                case 2:
                    b.Tube(root, muzzle + new float3(0f, -0.15f * hr, 1.4f * hr), 0.22f * hr, 0.015f, tone, tip, 5);
                    break;
                case 3:
                    b.Ball(muzzle + new float3(0f, -0.06f * hr, 0.6f * hr), new float3(0.32f, 0.1f, 0.55f) * hr, tone, 7, 4);
                    break;
                default:
                    b.Tube(root, muzzle + new float3(0f, -0.1f * hr, 0.95f * hr), 0.3f * hr, 0.02f, tone, tip, 5);
                    break;
            }
        }

        // Three upright feathers, a fan, one long plume, a swept-back tuft, or a smooth crown.
        private static void Crest(Fig b, float3 c, float hr, Color tone, uint style)
        {
            switch (style)
            {
                case 1:
                    for (int i = -2; i <= 2; i++)
                    {
                        float3 root = c + new float3(i * 0.12f * hr, 0.9f * hr, -0.1f * hr);
                        b.Panel(root + new float3(0f, 0f, 0.15f * hr), root + new float3(0f, 0f, -0.15f * hr),
                            root + new float3(i * 0.25f * hr, 0.75f * hr, -0.3f * hr), Shade(tone, 1f - 0.06f * math.abs(i)));
                    }

                    break;
                case 2:
                {
                    float3 root = c + new float3(0f, 0.9f * hr, 0.1f * hr);
                    b.Panel(root + new float3(0f, 0f, 0.1f * hr), root + new float3(0f, 0f, -0.15f * hr), root + new float3(0f, 0.8f * hr, -0.9f * hr), tone);
                    b.Panel(root + new float3(0f, 0.4f * hr, -0.45f * hr), root + new float3(0f, 0.3f * hr, -0.5f * hr),
                        root + new float3(0f, 0.2f * hr, -1.4f * hr), Shade(tone, 0.85f));
                    break;
                }
                case 3:
                    for (int i = 0; i < 4; i++)
                    {
                        float3 root = c + new float3(0f, (0.85f - 0.12f * i) * hr, (0.1f - 0.3f * i) * hr);
                        b.Panel(root + new float3(-0.2f * hr, 0f, 0f), root + new float3(0.2f * hr, 0f, 0f),
                            root + new float3(0f, 0.15f * hr, -0.6f * hr), Shade(tone, 1f - 0.07f * i));
                    }

                    break;
                case 4:
                    break;
                default:
                    for (int i = 0; i < 3; i++)
                    {
                        float3 root = c + new float3(0f, 0.9f * hr, (0.2f - 0.35f * i) * hr);
                        b.Panel(root + new float3(0f, 0f, 0.2f * hr), root + new float3(0f, 0f, -0.2f * hr),
                            root + new float3(0f, (0.7f - 0.1f * i) * hr, -(0.4f + 0.2f * i) * hr), Shade(tone, 1f - 0.08f * i));
                    }

                    break;
            }
        }

        // 0 swept back, 1 ram curl, 2 straight up and forward, 3 two pairs; demons add 4 curved out-up (their original),
        // 5–8 repeat 0–3 so every demon style has equal odds. Size scales length, not thickness at the root.
        private static void Horns(Fig b, float3 c, float hr, Color root, Color tip, uint style, float size)
        {
            uint shape = style >= 5 ? style - 5 : style;
            for (int s = -1; s <= 1; s += 2)
            {
                switch (shape)
                {
                    case 1:
                    {
                        // Ram: out, back, down and forward again around the ear.
                        float3 a = c + new float3(s * 0.55f, 0.6f, -0.1f) * hr;
                        float3 p = a + new float3(s * 0.35f, 0.25f, -0.35f) * hr * size;
                        float3 q = p + new float3(s * 0.25f, -0.45f, -0.05f) * hr * size;
                        float3 e = q + new float3(-s * 0.05f, -0.2f, 0.35f) * hr * size;
                        b.Tube(a, p, 0.2f * hr, 0.17f * hr, root, root, 6);
                        b.Tube(p, q, 0.17f * hr, 0.12f * hr, root, tip, 6);
                        b.Tube(q, e, 0.12f * hr, 0.02f, tip, tip, 5);
                        break;
                    }
                    case 2:
                    {
                        float3 a = c + new float3(s * 0.4f, 0.75f, 0.2f) * hr;
                        b.Tube(a, a + new float3(s * 0.1f, 0.85f, 0.3f) * hr * size, 0.2f * hr, 0.02f, root, tip, 6);
                        break;
                    }
                    case 3:
                        for (int k = 0; k < 2; k++)
                        {
                            float3 a = c + new float3(s * (0.35f + 0.25f * k), 0.75f - 0.15f * k, 0.05f - 0.3f * k) * hr;
                            b.Tube(a, a + new float3(s * 0.15f, 0.55f - 0.15f * k, -0.25f) * hr * size, (0.14f - 0.03f * k) * hr, 0.02f, root, tip, 5);
                        }

                        break;
                    case 4:
                    {
                        float3 a = c + new float3(s * 0.55f, 0.65f, 0.2f) * hr;
                        float3 mid = a + new float3(s * 0.45f, 0.35f, 0f) * hr * size;
                        b.Tube(a, mid, 0.2f * hr, 0.14f * hr, root, Color.Lerp(root, tip, 0.5f), 5);
                        b.Tube(mid, mid + new float3(-s * 0.1f, 0.6f, 0.15f) * hr * size, 0.14f * hr, 0.02f, Color.Lerp(root, tip, 0.5f), tip, 5);
                        break;
                    }
                    default:
                    {
                        float3 a = c + new float3(s * 0.5f, 0.7f, -0.2f) * hr;
                        float3 mid = a + new float3(s * 0.2f, 0.5f, -0.5f) * hr * size;
                        b.Tube(a, mid, 0.17f * hr, 0.11f * hr, root, root, 5);
                        b.Tube(mid, mid + new float3(s * 0.05f, 0.5f, -0.25f) * hr * size, 0.11f * hr, 0.02f, root, tip, 5);
                        break;
                    }
                }
            }
        }

        // 0 full, 1 close-trimmed, 2 goatee, 3 forked, 4 braided, 5 moustache.
        private static void Beard(Fig b, float3 c, float hr, in Look look)
        {
            Color hair = look.Hair, deep = Shade(look.Hair, 0.88f);
            float len = look.BeardLength;
            int style = look.BeardStyle;
            if (style == 2 || style == 5 || style >= 3 && len < 1.2f)
            {
                // Moustache: two drooping strokes under the nose.
                for (int s = -1; s <= 1; s += 2)
                {
                    b.Tube(c + new float3(s * 0.04f, -0.3f, 0.95f) * hr, c + new float3(s * 0.32f, -0.48f, 0.82f) * hr, 0.09f * hr, 0.04f * hr, hair, deep, 5);
                }
            }

            switch (style)
            {
                case 1:
                    b.Loft(new[]
                    {
                        new Ring(c + new float3(0f, -0.25f * hr, 0.45f * hr), 0.74f * hr, 0.56f * hr, hair),
                        new Ring(c + new float3(0f, -0.7f * hr, 0.5f * hr), 0.62f * hr, 0.5f * hr, deep),
                        new Ring(c + new float3(0f, -0.98f * hr, 0.58f * hr), 0.28f * hr, 0.25f * hr, deep),
                    }, 7, false, true);
                    break;
                case 2:
                    b.Ball(c + new float3(0f, -0.88f, 0.72f) * hr, new float3(0.2f, 0.28f + 0.1f * len, 0.16f) * hr, hair, 6, 4);
                    break;
                case 5:
                    break;
                default:
                {
                    float body = style == 0 ? len : math.min(len, 0.7f);
                    b.Loft(new[]
                    {
                        new Ring(c + new float3(0f, -0.25f * hr, 0.45f * hr), 0.72f * hr, 0.55f * hr, hair),
                        new Ring(c + new float3(0f, -0.75f * hr, 0.55f * hr), 0.6f * hr, 0.45f * hr, Shade(hair, 0.93f)),
                        new Ring(c + new float3(0f, -(0.75f + body) * hr, 0.6f * hr), style == 0 ? 0.12f * hr : 0.4f * hr, style == 0 ? 0.1f * hr : 0.3f * hr,
                            deep),
                    }, 7, false, true);
                    float3 from = c + new float3(0f, -(0.75f + body) * hr, 0.6f * hr);
                    if (style == 3)
                    {
                        for (int s = -1; s <= 1; s += 2)
                        {
                            b.Tube(from + new float3(s * 0.18f * hr, 0f, 0f), from + new float3(s * 0.3f * hr, -(len - body + 0.4f) * hr, 0.1f * hr),
                                0.16f * hr, 0.05f * hr, deep, deep, 5);
                        }
                    }
                    else if (style == 4)
                    {
                        float3 end = from - new float3(0f, (len - body + 0.5f) * hr, -0.05f * hr);
                        b.Tube(from, end, 0.2f * hr, 0.1f * hr, deep, deep, 5);
                        for (int i = 1; i <= 2; i++)
                        {
                            b.Hoop(math.lerp(from, end, i / 3f), (0.2f - 0.04f * i) * hr, 0.04f * hr, look.Accent);
                        }
                    }

                    break;
                }
            }
        }

        // `up` and `back` sweep the tip; `out` splays it: an elf's ear points back and up, a goblin's straight out. A right
        // ear scaled below 1 is torn; at 0 it is gone.
        private static void Ears(Fig b, float3 c, float hr, float side, Color skin, float up, float back, float outward, float rightScale = 1f)
        {
            for (int s = -1; s <= 1; s += 2)
            {
                float k = s > 0 ? rightScale : 1f;
                if (k <= 0f)
                {
                    continue;
                }

                float3 root = c + new float3(s * side * hr, 0f, -0.05f * hr);
                b.Panel(root + new float3(0f, 0.18f * hr, 0.1f * hr), root + new float3(0f, -0.2f * hr, 0.05f * hr),
                    root + new float3(s * (0.15f + outward * k) * hr, up * k * hr, -back * k * hr), Shade(skin, 0.93f));
            }
        }

        private static void Hair(Fig b, in NpcSpec spec, in Rig r, in Look look, ref Pcg32 rng)
        {
            if (look.HairStyle == 4 || spec.Race == NpcRace.Zombie && rng.NextUInt(2) == 0)
            {
                return;
            }

            float3 c = r.Head;
            float hr = r.HeadR;
            Color hair = look.Hair, deep = Shade(look.Hair, 0.86f);
            if (look.HairStyle == 6)
            {
                // Crest: shaved sides leave a single ridge from brow to nape, the boldest profile a head can have.
                b.Ball(c + new float3(0f, 0.82f * hr, -0.15f * hr), new float3(0.2f, 0.5f, 1.02f) * hr, hair, 7, 5);
                b.Ball(c + new float3(0f, 0.2f * hr, -0.1f * hr), new float3(0.97f, 0.9f, 0.99f) * hr, Color.Lerp(look.Skin, deep, 0.35f), 8, 5, 0.25f);
                return;
            }

            // A cap cut at the brow and pushed back, so the face stays open; volume puffs it out all round.
            float puff = look.HairStyle == 8 ? 1.22f : 1f;
            b.Ball(c + new float3(0f, 0.1f * hr + (puff - 1f) * 0.4f * hr, -0.12f * hr), new float3(1f, 1.02f, 1.03f) * hr * puff, hair, 9, 6, 0.05f);
            b.Ball(c + new float3(0f, 0.55f * hr, 0.55f * hr), new float3(0.8f, 0.42f, 0.5f) * hr * math.sqrt(puff), hair, 7, 4);
            switch (look.HairStyle)
            {
                case 1:
                    b.Loft(new[]
                    {
                        new Ring(c + new float3(0f, 0.2f * hr, -0.35f * hr), 0.95f * hr, 0.75f * hr, hair),
                        new Ring(c + new float3(0f, -0.8f * hr, -0.5f * hr), 0.9f * hr, 0.5f * hr, deep),
                        new Ring(c + new float3(0f, -1.9f * hr, -0.55f * hr), 0.55f * hr, 0.25f * hr, deep),
                    }, 7, false, true);
                    break;
                case 2:
                    b.Ball(c + new float3(0f, 0.75f * hr, -0.75f * hr), new float3(0.42f) * hr, deep, 6, 4);
                    break;
                case 3:
                    b.Tube(c + new float3(0f, 0.3f * hr, -0.95f * hr), c + new float3(0f, -1.3f * hr, -1.25f * hr), 0.28f * hr, 0.08f * hr,
                        hair, deep, 5);
                    break;
                case 5:
                    // Two braids brought forward over the shoulders, tied off with a bead of the town's accent.
                    for (int s = -1; s <= 1; s += 2)
                    {
                        float3 root = c + new float3(s * 0.8f * hr, -0.3f * hr, -0.25f * hr);
                        float3 mid = root + new float3(s * 0.25f * hr, -1f * hr, 0.25f * hr);
                        float3 end = mid + new float3(0f, -1.1f * hr, 0.25f * hr);
                        b.Tube(root, mid, 0.2f * hr, 0.16f * hr, hair, deep, 5);
                        b.Tube(mid, end, 0.16f * hr, 0.1f * hr, deep, deep, 5);
                        b.Ball(end, new float3(0.13f * hr), look.Accent, 5, 3);
                    }

                    break;
                case 7:
                    b.Ball(c + new float3(0f, 1.08f * hr, -0.2f * hr), new float3(0.34f, 0.3f, 0.34f) * hr, deep, 6, 4);
                    b.Hoop(c + new float3(0f, 0.9f * hr, -0.18f * hr), 0.2f * hr, 0.05f * hr, look.Accent);
                    break;
            }
        }

        // ---- clothes -------------------------------------------------------------------------------------------

        private static void Dress(Fig b, Fig glow, in NpcSpec spec, in Rig r, in Look look, ref Pcg32 rng)
        {
            float h = r.H;
            Color cloth = look.Cloth, cloth2 = look.Cloth2;
            bool robed = look.Cut == Cut.Robe;
            float legLen = r.Hip.y;

            // Upper garment: how far it falls below the hips (share of the leg) and how much arm it covers.
            (float hem, int sleeve, float fit) = look.UpperEra switch
            {
                ArchEra.Primal => (0.4f, 0, 1.12f),
                ArchEra.Agrarian => (0.42f, 1, 1.16f),
                ArchEra.Mercantile => (0.25f, 2, 1.15f),
                ArchEra.Industrial => (spec.Sex == NpcSex.Female ? 0.15f : 0.8f, 2, 1.14f),
                ArchEra.Steampunk => (0.6f, 2, 1.15f),
                ArchEra.Future => (0.06f, 2, 1.07f),
                _ => (0.5f, 1, 1.2f),
            };
            hem *= math.lerp(0.78f, 1.14f, spec.Textile01);
            // Under a vest the arms show the shirt, not the vest's cloth.
            Color sleeveCloth = cloth;
            switch (look.Cut)
            {
                case Cut.Robe:
                    hem = 0.9f;
                    sleeve = 2;
                    break;
                case Cut.Dress:
                    hem = math.min(hem, 0.2f);
                    break;
                case Cut.Coat:
                    hem = math.max(hem, 0.72f);
                    sleeve = 2;
                    fit = math.max(fit, 1.17f);
                    break;
                case Cut.Vest:
                    hem = math.min(hem, 0.12f);
                    sleeveCloth = cloth2;
                    break;
                case Cut.Wrap:
                    hem = 0.02f;
                    sleeve = math.min(sleeve, 1);
                    break;
            }

            if (spec.Role == NpcRole.Laborer)
            {
                sleeve = 0;
            }

            // Lower garment first, so the upper one overlaps its waistband.
            ArchEra low = look.LowerEra;
            bool bareLegs = low == ArchEra.Primal && !look.Skirt;
            Color legCloth = low == ArchEra.Future ? cloth : cloth2;
            float boot = low switch
            {
                ArchEra.Primal => 0f,
                ArchEra.Agrarian => 0.12f,
                ArchEra.Mercantile => 0.5f,
                ArchEra.Steampunk => 0.5f,
                ArchEra.Future => 0.3f,
                ArchEra.Wasteland => 0.4f,
                _ => 0.15f,
            };
            Color bootTone = low == ArchEra.Future ? Shade(cloth2, 0.9f) : look.Leather;
            for (int s = -1; s <= 1; s += 2)
            {
                float3 hip = s < 0 ? r.HipL : r.HipR, knee = s < 0 ? r.KneeL : r.KneeR, ankle = s < 0 ? r.AnkleL : r.AnkleR;
                b.Leg = s;
                if (!bareLegs && !look.Skirt)
                {
                    float loose = low == ArchEra.Mercantile || low == ArchEra.Future ? 1.08f : 1.3f;
                    // Same nodes as the leg beneath (Flesh), each scaled by the cut's ease, so cloth clears flesh everywhere.
                    float3 thigh = math.lerp(hip, knee, 0.32f), calf = math.lerp(knee, ankle, 0.3f), cuffAt = math.lerp(knee, ankle, 0.9f);
                    float kneeCloth = math.max(r.KneeRad, r.CalfR) * loose;
                    float cuffR = math.lerp(r.CalfR, r.AnkleRad, 0.6f / 0.7f) * loose * 1.1f;
                    b.Tube(hip, thigh, r.ThighR * loose, r.ThighMidR * loose, legCloth, legCloth, 7);
                    b.Tube(thigh, knee, r.ThighMidR * loose, kneeCloth, legCloth, Shade(legCloth, 0.92f), 7);
                    b.Tube(knee, calf, kneeCloth, r.CalfR * loose, Shade(legCloth, 0.92f), Shade(legCloth, 0.9f), 7);
                    b.Tube(calf, cuffAt, r.CalfR * loose, cuffR, Shade(legCloth, 0.9f), Shade(legCloth, 0.85f), 7);
                }

                if (boot > 0f)
                {
                    float3 top = math.lerp(ankle, knee, boot * 1.6f);
                    b.Tube(top, ankle - new float3(0f, 0.02f * h, 0f), r.LegR * 1.02f, r.LegR * 0.8f, bootTone, Shade(bootTone, 0.85f), 7);
                    float foot = r.LegR * 1.3f;
                    b.Leg = 2 * s;
                    b.Ball(new float3(ankle.x, foot * 0.55f, ankle.z + foot * 0.45f), new float3(foot, foot * 0.62f, foot * 1.3f),
                        Shade(bootTone, 0.9f), 7, 4, -0.5f);
                    if (low == ArchEra.Future)
                    {
                        glow.Leg = s;
                        glow.Hoop(math.lerp(ankle, knee, 0.5f), r.LegR * 1.05f, 0.008f * h, look.Glow);
                        glow.Leg = 0;
                    }
                }
            }

            b.Leg = 0;
            b.ClothHipWidth = r.HipR.x;
            if (look.Skirt)
            {
                float fall = look.Fall;
                float flare = look.Cut == Cut.Wrap ? 0.6f : low == ArchEra.Industrial ? 2f : low == ArchEra.Future ? 1.2f : 1.6f;
                Color skirt = robed ? cloth : look.Cut == Cut.Wrap ? look.Accent : cloth2;
                float skirtX = math.abs(r.HipR.x) + math.max(r.LegR * 1.5f, r.ThighR * 1.2f), skirtZ = math.max(r.TrunkZ * 1.1f, math.max(r.LegR * 1.6f, r.ThighR * 1.3f));
                b.Loft(new[]
                {
                    new Ring(r.Hip + new float3(0f, 0.03f * h, 0f), skirtX * 0.95f, skirtZ * 0.95f, skirt),
                    new Ring(r.Hip - new float3(0f, legLen * fall * 0.45f, 0f), skirtX * (1f + 0.3f * flare * 0.45f),
                        skirtZ * (1.1f + 0.5f * flare * 0.45f), Shade(skirt, 0.95f)),
                    new Ring(new float3(0f, legLen * (1f - fall), 0f), skirtX * (1f + 0.3f * flare), skirtZ * (1.1f + 0.5f * flare),
                        Shade(skirt, 0.85f)),
                }, 9, false, true);
            }

            // The trunk of the upper garment.
            float3 hemAt = r.Hip - new float3(0f, legLen * hem, 0f);
            float hemFlare = 1f + hem * 0.5f;
            // Cloth hangs outside the legs it covers: never narrower than the thighs (plus their trousers) at the hip.
            float thighX = math.abs(r.HipR.x) + math.max(r.LegR * 1.75f, r.ThighR * 1.38f), thighZ = math.max(r.LegR * 1.9f, r.ThighR * 1.5f);
            Color lowTone = look.UpperEra == ArchEra.Wasteland ? Shade(cloth2, 1.05f) : Shade(cloth, 0.88f);
            float hemX = math.max(r.TrunkX * 0.9f * fit * hemFlare, thighX * (1f + 0.12f * hem));
            float hemZ = math.max(r.TrunkZ * 1.05f * fit * hemFlare, thighZ);
            float border = math.lerp(0.07f, 0.32f, spec.Textile01);
            Color braid = Color.Lerp(lowTone, look.Accent, math.lerp(0.32f, 0.85f, spec.Textile01));
            b.Loft(new[]
            {
                new Ring(hemAt, hemX, hemZ, Shade(braid, 0.76f)),
                new Ring(math.lerp(hemAt, r.Hip, border * 0.74f), hemX, hemZ, braid),
                new Ring(math.lerp(hemAt, r.Hip, border), hemX, hemZ, lowTone),
                new Ring(r.Hip, math.max(r.TrunkX * 0.86f * fit, thighX), math.max(r.TrunkZ * 1.02f * fit, thighZ),
                    look.UpperEra == ArchEra.Wasteland ? lowTone : Shade(cloth, 0.94f)),
                new Ring(r.Waist, r.TrunkX * 0.84f * fit * r.Belly, r.TrunkZ * fit * r.Belly, cloth),
                new Ring(r.Chest - r.Fwd * (0.05f * h), r.TrunkX * fit, r.TrunkZ * 1.1f * fit, cloth),
                new Ring(r.Chest + new float3(0f, 0.005f * h, 0f), r.TrunkX * 0.8f * fit, r.TrunkZ * 0.85f * fit, Shade(cloth, 1.04f)),
            }, 16, true, false, math.lerp(0.025f, 0.085f, spec.Textile01));
            b.ClothHipWidth = 0f;
            // Belt: the one line every era agrees on, and where pouches and lanterns hang.
            Color belt = look.UpperEra == ArchEra.Future ? look.Glow : look.Leather;
            (look.UpperEra == ArchEra.Future ? glow : b).Hoop(math.lerp(r.Hip, r.Waist, 0.6f), r.TrunkX * 0.9f * fit, 0.012f * h, belt,
                r.TrunkZ * 1.08f * fit);

            for (int s = -1; s <= 1; s += 2)
            {
                float3 shoulder = s < 0 ? r.ShoulderL : r.ShoulderR, elbow = s < 0 ? r.ElbowL : r.ElbowR, hand = s < 0 ? r.HandL : r.HandR;
                Ride(b, glow, s < 0 ? RideArmLeft : RideArmRight);
                int cover = look.UpperEra == ArchEra.Wasteland && s > 0 ? 2 : sleeve;
                if (cover >= 1)
                {
                    b.Tube(shoulder, cover == 1 ? math.lerp(shoulder, elbow, 0.75f) : elbow, r.ArmR * 1.5f, r.ArmR * 1.25f, sleeveCloth,
                        Shade(sleeveCloth, 0.92f), 6);
                    b.Ball(shoulder, new float3(r.ArmR * (look.UpperEra == ArchEra.Mercantile ? 2.2f : 1.6f)), sleeveCloth, 6, 4);
                }

                if (cover >= 2)
                {
                    float cuff = look.Piece && look.PieceRegion == ArchRegion.Twilight ? 2.6f : robed ? 1.9f : 1.15f;
                    b.Tube(elbow, math.lerp(elbow, hand, 0.88f), r.ArmR * 1.25f, r.ArmR * cuff, Shade(sleeveCloth, 0.92f), Shade(sleeveCloth, 0.85f), 6);
                    if (look.UpperEra == ArchEra.Future)
                    {
                        glow.Hoop(math.lerp(elbow, hand, 0.8f), r.ArmR * 1.2f, 0.007f * h, look.Glow);
                    }
                }
            }

            Ride(b, glow, RideBody);
            Overgarment(b, spec, r, look, fit, hem);
            float3 front = r.Chest + new float3(0f, -0.07f * h, r.TrunkZ * 1.1f * fit);
            switch (look.UpperEra)
            {
                case ArchEra.Primal:
                    for (int i = -2; i <= 2; i++)
                    {
                        b.Ball(r.Chest + new float3(i * 0.028f * h, -0.02f * h - 0.006f * h * (4 - i * i), r.TrunkZ * fit), new float3(0.011f * h),
                            look.Bone, 5, 3);
                    }

                    break;
                case ArchEra.Industrial:
                case ArchEra.Steampunk:
                    for (int i = 0; i < 3; i++)
                    {
                        b.Ball(front - new float3(0f, i * 0.045f * h, -0.004f * h * i), new float3(0.01f * h), look.Metal, 5, 3);
                    }

                    if (look.UpperEra == ArchEra.Steampunk)
                    {
                        b.Ball(r.ShoulderL + new float3(0f, 0.015f * h, 0f), new float3(r.ArmR * 2.3f, r.ArmR * 1.6f, r.ArmR * 2.3f), look.Metal, 7, 4, -0.1f);
                    }

                    break;
                case ArchEra.Future:
                    glow.Panel(front + new float3(-0.03f * h, 0.03f * h, 0.004f), front + new float3(0.03f * h, 0.03f * h, 0.004f),
                        front + new float3(0f, -0.02f * h, 0.004f), look.Glow);
                    break;
                case ArchEra.Wasteland:
                    // A scarf worth more than the coat: wound thick, one end left to hang.
                    b.Hoop(r.Neck + new float3(0f, -0.005f * h, 0f), r.HeadR * 0.75f, 0.022f * h, look.Accent);
                    b.Tube(r.Neck + new float3(r.HeadR * 0.5f, 0f, r.HeadR * 0.5f), r.Neck + new float3(r.HeadR * 0.7f, -0.2f * h, r.HeadR * 0.7f),
                        0.02f * h, 0.012f * h, look.Accent, Shade(look.Accent, 0.8f), 5);
                    b.Ball(r.ShoulderR + new float3(0f, 0.02f * h, 0f), new float3(r.ArmR * 2.4f, r.ArmR * 1.4f, r.ArmR * 2.4f), Hex(0x3A3632), 7, 4, -0.1f);
                    break;
            }

            if (look.Piece)
            {
                Signature(b, glow, spec, r, look, fit);
            }

            if (spec.Race == NpcRace.Vampire)
            {
                // Cape with a standing collar: the silhouette is the race.
                Cape(b, r, Hex(0x1E1820), Hex(0x7A1E28), 0.9f, true);
            }

            bool roleHat = spec.Role == NpcRole.Farmer || spec.Role == NpcRole.Miner || spec.Role == NpcRole.Guard ||
                           spec.Role == NpcRole.Performer || spec.Role == NpcRole.Leader || spec.Role == NpcRole.Healer;
            // Horns, crests and upright ears leave no room for a hat; goggles and bands still fit.
            bool crowned = spec.Race == NpcRace.Demon || spec.Race == NpcRace.Dragonkin || spec.Race == NpcRace.Avian ||
                           spec.Race == NpcRace.Werewolf || spec.Race == NpcRace.Merfolk || spec.Race == NpcRace.Angel ||
                           spec.Race == NpcRace.Lizardfolk;
            if (look.Hat && !roleHat)
            {
                Headwear(b, glow, r, look, crowned, ref rng);
            }
        }

        // The pieces that make a cut read from across a square: lapels, a poncho's wide shoulders, a vest's open front,
        // a wrap's sash.
        private static void Overgarment(Fig b, in NpcSpec spec, in Rig r, in Look look, float fit, float hem)
        {
            float h = r.H;
            float front = r.TrunkZ * 1.12f * fit;
            switch (look.Cut)
            {
                case Cut.Coat:
                {
                    // Lapels folded back from the collar to the second button, in the lining's colour.
                    Color lining = Shade(look.Cloth2, 0.95f);
                    for (int s = -1; s <= 1; s += 2)
                    {
                        float3 neck = r.Neck + new float3(s * r.HeadR * 0.45f, -0.01f * h, r.TrunkZ * 0.75f);
                        float3 tip = r.Chest + new float3(s * r.TrunkX * 0.55f, -0.04f * h, front + 0.006f * h);
                        float3 low = r.Chest + new float3(s * 0.01f * h, -0.14f * h, front + 0.004f * h);
                        b.Panel(neck, tip, low, lining);
                    }

                    // An open coat parts below the belt: a dark wedge where the trousers show through.
                    float3 hemAt = r.Hip - new float3(0f, r.Hip.y * hem, 0f);
                    b.Panel(math.lerp(r.Hip, r.Waist, 0.6f) + new float3(0f, 0f, front * 1.02f),
                        hemAt + new float3(-0.06f * h, 0f, front * 1.25f), hemAt + new float3(0.06f * h, 0f, front * 1.25f),
                        Shade(look.Cloth2, 0.6f));
                    break;
                }
                case Cut.Mantle:
                {
                    // A poncho slopes from the collar over the shoulders and falls straight past the hips: the slope
                    // and the long fall are what read as cloth, a wide drum at the waist reads as a barrel.
                    Color tone = spec.Textile01 > 0.5f ? look.Accent : look.Cloth2;
                    float wide = r.TrunkX * fit + r.ArmR * 1.9f;
                    float deep = r.TrunkZ * 1.35f * fit;
                    float3 hemAt = r.Hip - new float3(0f, 0.08f * h, 0f);
                    b.Loft(new[]
                    {
                        new Ring(hemAt, wide * 1.08f, deep * 1.2f, Shade(tone, 0.8f)),
                        new Ring(math.lerp(hemAt, r.Chest, 0.12f), wide * 1.07f, deep * 1.18f, look.Accent),
                        new Ring(math.lerp(hemAt, r.Chest, 0.2f), wide * 1.06f, deep * 1.16f, Shade(tone, 0.9f)),
                        new Ring(r.Chest - new float3(0f, 0.02f * h, 0f), wide, deep, tone),
                        new Ring(r.Chest + new float3(0f, 0.025f * h, 0f), r.HeadR * 0.95f, r.HeadR * 0.9f, Shade(tone, 1.05f)),
                    }, 12, false, false);
                    break;
                }
                case Cut.Vest:
                    for (int s = -1; s <= 1; s += 2)
                    {
                        b.Slab(r.Chest + new float3(s * r.TrunkX * 0.42f, 0.0f, front), math.lerp(r.Hip, r.Waist, 0.3f) + new float3(s * r.TrunkX * 0.5f, 0f, front * r.Belly),
                            r.TrunkX * 0.5f, 0.012f * h, Shade(look.Cloth, 0.8f));
                    }

                    break;
                case Cut.Wrap:
                    // Bare shoulder on one side, the cloth over the other: the oldest way to wear a length of cloth.
                    b.Tube(r.ShoulderR + new float3(-0.01f * h, 0.015f * h, 0f), r.HipL + new float3(0f, 0.04f * h, r.TrunkZ * 0.5f),
                        0.045f * h, 0.04f * h, look.Accent, Shade(look.Accent, 0.85f), 6);
                    break;
            }
        }

        // What a region is known for wearing, whatever the era.
        private static void Signature(Fig b, Fig glow, in NpcSpec spec, in Rig r, in Look look, float fit)
        {
            float h = r.H, hr = r.HeadR;
            switch (look.PieceRegion)
            {
                case ArchRegion.Verdant:
                    // Shoulder cape with the hood down.
                    b.Loft(new[]
                    {
                        new Ring(r.Chest - new float3(0f, 0.13f * h, 0f), r.TrunkX * 1.12f * fit + r.ArmR * 2.2f, r.TrunkZ * 1.55f * fit, Shade(look.Accent, 0.8f)),
                        new Ring(r.Chest - new float3(0f, 0.035f * h, 0f), r.TrunkX * 1.08f * fit + r.ArmR * 1.9f, r.TrunkZ * 1.4f * fit, Shade(look.Accent, 0.92f)),
                        new Ring(r.Chest + new float3(0f, 0.02f * h, 0f), r.TrunkX * 0.62f * fit, r.TrunkZ * 0.95f * fit, look.Accent),
                    }, 10, false, false);
                    b.Ball(r.Neck + new float3(0f, 0f, -hr * 0.9f), new float3(0.7f, 0.55f, 0.6f) * hr, Shade(look.Accent, 0.9f), 6, 4);
                    break;
                case ArchRegion.Lava:
                    // A sash from shoulder to hip and a brow band, both the colour of the forge.
                    b.Tube(r.ShoulderL + new float3(0.02f * h, 0.01f * h, 0f), r.HipR + new float3(0.01f * h, 0.02f * h, r.TrunkZ * 0.6f),
                        0.03f * h, 0.03f * h, look.Accent, Shade(look.Accent, 0.85f), 5);
                    b.Hoop(r.Head + new float3(0f, 0.35f * hr, 0f), hr * 0.97f, 0.012f * h, look.Accent);
                    break;
                case ArchRegion.Ice:
                    b.Hoop(r.Neck + new float3(0f, -0.008f * h, 0f), r.TrunkX * 0.72f * fit, 0.03f * h, Hex(0xEFEBE2), r.TrunkZ * 1.05f * fit);
                    for (int s = -1; s <= 1; s += 2)
                    {
                        Ride(b, glow, s < 0 ? RideArmLeft : RideArmRight);
                        b.Ball(s < 0 ? r.HandL : r.HandR, new float3(r.ArmR * 1.6f), Shade(look.Leather, 1.25f), 6, 4);
                    }

                    Ride(b, glow, RideBody);

                    break;
                case ArchRegion.Storm:
                    Cape(b, r, Shade(look.Accent, 0.9f), Shade(look.Accent, 0.7f), 0.75f, false);
                    break;
                case ArchRegion.Crystal:
                    b.Hoop(math.lerp(r.Hip, r.Waist, 0.6f), r.TrunkX * 0.93f * fit, 0.024f * h, look.Accent, r.TrunkZ * 1.12f * fit);
                    b.Tube(r.HipL + new float3(0f, 0.03f * h, r.TrunkZ * fit), r.HipL + new float3(-0.01f * h, -0.2f * h, r.TrunkZ * fit),
                        0.022f * h, 0.03f * h, look.Accent, Shade(look.Accent, 0.85f), 5);
                    glow.Ball(r.Head + new float3(0f, 0.42f * hr, 0.9f * hr), new float3(0.11f, 0.13f, 0.06f) * hr, look.Glow, 5, 3);
                    break;
                default:
                    // Twilight: wide sleeves are cut in Dress; here the sash knot and a hip lantern for the long dusk.
                    b.Hoop(math.lerp(r.Hip, r.Waist, 0.7f), r.TrunkX * 0.92f * fit, 0.022f * h, look.Accent, r.TrunkZ * 1.1f * fit);
                    float3 hang = r.HipR + new float3(r.TrunkX * 0.35f, -0.03f * h, r.TrunkZ * 0.5f);
                    b.Tube(hang, hang - new float3(0f, 0.07f * h, 0f), 0.004f * h, 0.004f * h, look.Leather, look.Leather, 4);
                    glow.Ball(hang - new float3(0f, 0.1f * h, 0f), new float3(0.03f, 0.04f, 0.03f) * h, look.Glow, 6, 4);
                    break;
            }
        }

        private static void Cape(Fig b, in Rig r, Color outer, Color lining, float fall, bool collar)
        {
            float h = r.H;
            float3 l = r.ShoulderL + new float3(0f, 0.02f * h, -r.TrunkZ * 0.4f), rr = r.ShoulderR + new float3(0f, 0.02f * h, -r.TrunkZ * 0.4f);
            float y = math.max(0.05f * h, r.Chest.y - r.Chest.y * fall);
            float3 bl = new float3(l.x * 1.5f, y, l.z - 0.14f * h), br = new float3(rr.x * 1.5f, y, rr.z - 0.14f * h);
            float3 mid = (bl + br) * 0.5f + new float3(0f, -0.03f * h, -0.05f * h);
            b.Panel(l, rr, mid, outer);
            b.Panel(l, bl, mid, Shade(outer, 0.9f));
            b.Panel(rr, br, mid, Shade(outer, 0.9f));
            if (collar)
            {
                float hr = r.HeadR;
                for (int s = -1; s <= 1; s += 2)
                {
                    float3 root = r.Neck + new float3(s * hr * 0.75f, -0.01f * h, -hr * 0.2f);
                    b.Panel(root + new float3(0f, 0f, hr * 0.7f), root + new float3(-s * hr * 0.6f, 0f, -hr * 0.6f),
                        root + new float3(s * hr * 0.45f, hr * 1.25f, -hr * 0.1f), lining);
                }
            }
        }

        private static void Headwear(Fig b, Fig glow, in Rig r, in Look look, bool crowned, ref Pcg32 rng)
        {
            float3 c = r.Head;
            float hr = r.HeadR, h = r.H;
            float3 crown = c + new float3(0f, 0.55f * hr, -0.05f * hr);
            uint pick = rng.NextUInt(3);
            switch (look.HeadEra)
            {
                case ArchEra.Primal:
                    b.Hoop(c + new float3(0f, 0.4f * hr, 0f), hr * 0.97f, 0.01f * h, look.Leather);
                    b.Panel(c + new float3(0.8f * hr, 0.4f * hr, -0.3f * hr), c + new float3(0.9f * hr, 0.4f * hr, -0.5f * hr),
                        c + new float3(1.1f * hr, 1.5f * hr, -0.8f * hr), look.Accent);
                    break;
                case ArchEra.Agrarian:
                    if (crowned)
                    {
                        break;
                    }

                    // Kerchief tied under the chin, or a soft felt cap.
                    b.Ball(c + new float3(0f, 0.12f * hr, -0.1f * hr), new float3(1.06f, 1.04f, 1.08f) * hr, pick == 0 ? look.Accent : Shade(look.Leather, 1.35f), 8, 5, 0.1f);
                    break;
                case ArchEra.Mercantile:
                    if (crowned)
                    {
                        break;
                    }

                    b.Ball(crown + new float3(0.25f * hr, 0.25f * hr, 0f), new float3(1.25f, 0.4f, 1.2f) * hr, look.Accent, 8, 4);
                    b.Panel(crown + new float3(0.9f * hr, 0.3f * hr, 0f), crown + new float3(1f * hr, 0.3f * hr, -0.2f * hr),
                        crown + new float3(1.5f * hr, 1.2f * hr, -0.7f * hr), look.Cloth2);
                    break;
                case ArchEra.Industrial:
                    if (crowned)
                    {
                        break;
                    }

                    if (pick == 0)
                    {
                        Brim(b, crown + new float3(0f, 0.2f * hr, 0f), hr * 1.45f, hr * 0.85f, hr * 1.5f, Hex(0x2A2A30), look.Accent);
                    }
                    else
                    {
                        // Flat cap: a low crown and a peak over the eyes.
                        b.Ball(crown + new float3(0f, 0.22f * hr, 0.05f * hr), new float3(1.08f, 0.36f, 1.15f) * hr, look.Cloth2, 8, 4);
                        b.Ball(crown + new float3(0f, 0.12f * hr, 0.95f * hr), new float3(0.7f, 0.08f, 0.45f) * hr, Shade(look.Cloth2, 0.8f), 6, 3);
                    }

                    break;
                case ArchEra.Steampunk:
                    if (!crowned && pick != 0)
                    {
                        Brim(b, crown + new float3(0f, 0.2f * hr, 0f), hr * 1.4f, hr * 0.85f, hr * 1.15f, look.Leather, look.Metal);
                    }

                    Goggles(b, glow, c + new float3(0f, (crowned || pick == 0 ? 0.5f : 0.85f) * hr, 0.72f * hr), hr, look.Metal, Hex(0x9FE0D0));
                    break;
                case ArchEra.Future:
                    // A light strip across the eyes, anchored by a band.
                    b.Hoop(c + new float3(0f, 0.12f * hr, 0f), hr * 0.98f, 0.01f * h, look.Metal);
                    glow.Ball(c + new float3(0f, 0.08f * hr, 0.86f * hr), new float3(0.72f, 0.14f, 0.2f) * hr, look.Glow, 7, 3);
                    break;
                default:
                    if (!crowned && pick != 0)
                    {
                        b.Ball(c + new float3(0f, 0.1f * hr, -0.15f * hr), new float3(1.12f, 1.1f, 1.15f) * hr, look.Cloth2, 8, 5, -0.05f);
                    }

                    Goggles(b, glow, c + new float3(0f, 0.5f * hr, 0.75f * hr), hr, Hex(0x4A4038), Hex(0xE0B060));
                    break;
            }
        }

        private static void Goggles(Fig b, Fig glow, float3 at, float hr, Color rim, Color lens)
        {
            for (int s = -1; s <= 1; s += 2)
            {
                float3 p = at + new float3(s * 0.36f * hr, 0f, 0f);
                b.Tube(p, p + new float3(0f, 0f, 0.2f * hr), 0.26f * hr, 0.24f * hr, rim, rim, 6);
                glow.Ball(p + new float3(0f, 0f, 0.2f * hr), new float3(0.19f, 0.19f, 0.06f) * hr, lens, 6, 3);
            }
        }

        private static void Brim(Fig b, float3 at, float brimR, float crownR, float crownH, Color tone, Color band)
        {
            b.Loft(new[]
            {
                new Ring(at, brimR, brimR, Shade(tone, 0.9f)),
                new Ring(at + new float3(0f, 0.04f * crownR, 0f), brimR, brimR, tone),
                new Ring(at + new float3(0f, 0.05f * crownR, 0f), crownR, crownR, band),
                new Ring(at + new float3(0f, 0.3f * crownR, 0f), crownR, crownR, band),
                new Ring(at + new float3(0f, 0.31f * crownR, 0f), crownR, crownR, tone),
                new Ring(at + new float3(0f, crownH, 0f), crownR * 1.04f, crownR * 1.04f, tone),
            }, 9, true, true);
        }

        // ---- trades --------------------------------------------------------------------------------------------

        private static void Trade(Fig b, Fig glow, in NpcSpec spec, in Rig r, in Look look)
        {
            float h = r.H, hr = r.HeadR;
            float3 hand = r.HandR, crown = r.Head + new float3(0f, 0.55f * hr, -0.05f * hr);
            float3 backAt = r.Chest + new float3(0f, -0.08f * h, -r.TrunkZ * 1.5f);
            Color wood = Hex(0x7A5A3A);
            bool late = look.UpperEra == ArchEra.Future || look.UpperEra == ArchEra.Steampunk;
            switch (spec.Role)
            {
                case NpcRole.Farmer:
                    AppendGear(b, GearKind.Headwear, GearContext.Labor, spec, crown + new float3(0f, 0.12f * hr, 0f),
                        Quaternion.identity, hr * 4.5f);
                    Pole(b, hand, h * 1.02f, wood);
                    b.Slab(new float3(hand.x, h * 1.02f, hand.z), new float3(hand.x, h * 1.02f, hand.z + 0.16f * h), 0.07f * h, 0.012f * h, look.Metal);
                    break;
                case NpcRole.Forager:
                {
                    // Bow slung across the back, quiver over the shoulder.
                    AppendGear(b, GearKind.Longbow, GearContext.Travel, spec, backAt + new float3(0f, -0.25f * h, -0.03f * h),
                        Quaternion.Euler(0f, 180f, 25f), h * 0.45f);
                    b.Tube(backAt + new float3(0.1f * h, 0.2f * h, 0.02f * h), backAt + new float3(-0.04f * h, -0.12f * h, 0.02f * h), 0.03f * h,
                        0.025f * h, look.Leather, Shade(look.Leather, 0.85f), 6);
                    break;
                }
                case NpcRole.Fisher:
                {
                    float3 tip = hand + new float3(0.1f * h, 0.85f * h, 0.75f * h);
                    b.Tube(hand - new float3(0f, 0.1f * h, 0.08f * h), tip, 0.009f * h, 0.003f * h, wood, Shade(wood, 1.2f), 4);
                    b.Tube(tip, tip - new float3(0f, 0.5f * h, 0f), 0.002f * h, 0.002f * h, look.Bone, look.Bone, 3);
                    b.Loft(new[]
                    {
                        new Ring(r.HipL + new float3(-r.TrunkX * 0.5f, -0.1f * h, 0f), 0.05f * h, 0.045f * h, Hex(0xB89A5A)),
                        new Ring(r.HipL + new float3(-r.TrunkX * 0.5f, 0.02f * h, 0f), 0.065f * h, 0.055f * h, Hex(0xC8AA6A)),
                    }, 7, true, true);
                    break;
                }
                case NpcRole.Miner:
                    b.Ball(crown, new float3(1.08f, 0.62f, 1.1f) * hr, look.Metal, 8, 4, -0.15f);
                    glow.Ball(crown + new float3(0f, 0.15f * hr, 1.05f * hr), new float3(0.2f, 0.2f, 0.1f) * hr, look.Glow, 6, 3);
                    AppendGear(b, GearKind.Axe, GearContext.Labor, spec, hand - new float3(0f, 0.15f * h, 0f),
                        Quaternion.Euler(35f, 0f, -12f), h * 0.65f);
                    break;
                case NpcRole.Artisan:
                    // Apron, and a hammer held where the work would be.
                    b.Slab(r.Chest + new float3(0f, -0.06f * h, r.TrunkZ * 1.35f), r.Hip + new float3(0f, -0.22f * h, r.TrunkZ * 1.5f),
                        r.TrunkX * 1.35f, 0.008f * h, look.Leather);
                    AppendGear(b, GearKind.Mace, GearContext.Labor, spec, hand - new float3(0f, 0.05f * h, 0f),
                        Quaternion.Euler(45f, 0f, -10f), h * 0.5f);
                    break;
                case NpcRole.Merchant:
                    // A pack taller than the shoulders with a bedroll on top: wares travel on the back.
                    b.Slab(backAt + new float3(0f, -0.2f * h, -0.06f * h), backAt + new float3(0f, 0.16f * h, -0.06f * h), 0.2f * h, 0.13f * h,
                        look.Leather);
                    b.Tube(backAt + new float3(-0.13f * h, 0.2f * h, -0.06f * h), backAt + new float3(0.13f * h, 0.2f * h, -0.06f * h), 0.05f * h,
                        0.05f * h, look.Accent, Shade(look.Accent, 0.9f), 7);
                    b.Ball(r.HipR + new float3(r.TrunkX * 0.4f, -0.03f * h, r.TrunkZ * 0.6f), new float3(0.03f, 0.04f, 0.03f) * h, look.Cloth2, 6, 4);
                    break;
                case NpcRole.Guard:
                {
                    b.Ball(crown + new float3(0f, -0.05f * hr, 0f), new float3(1.1f, 0.85f, 1.12f) * hr, look.Metal, 8, 5, -0.1f);
                    b.Tube(crown + new float3(0f, 0.75f * hr, 0f), crown + new float3(0f, 1.25f * hr, -0.1f * hr), 0.1f * hr, 0.02f * hr, look.Accent,
                        look.Accent, 4);
                    float reach = math.min(h * 1.25f, h + 0.9f);
                    AppendGear(b, GearKind.Spear, GearContext.Combat, spec, new float3(hand.x, 0.02f, hand.z),
                        Quaternion.identity, reach * 0.6f);
                    if (late)
                    {
                        float3 head = new float3(hand.x, reach, hand.z);
                        glow.Tube(head, head + new float3(0f, 0.14f * h, 0f), 0.02f * h, 0.002f, look.Glow, look.Glow, 4);
                    }
                    // Shield on the forearm, face outward.
                    float3 boss = (r.ElbowL + r.HandL) * 0.5f + new float3(-0.035f * h, 0f, 0.03f * h);
                    AppendGear(b, GearKind.Shield, GearContext.Combat, spec, boss, Quaternion.Euler(0f, -75f, 0f), h * 0.26f);
                    break;
                }
                case NpcRole.Scholar:
                    b.Slab(hand + new float3(-0.05f * h, 0.03f * h, 0.03f * h), hand + new float3(0.05f * h, 0.03f * h, 0.03f * h), 0.13f * h,
                        0.035f * h, look.Accent);
                    b.Slab(hand + new float3(-0.045f * h, 0.03f * h, 0.035f * h), hand + new float3(0.045f * h, 0.03f * h, 0.035f * h), 0.12f * h,
                        0.025f * h, Hex(0xEFE8D0));
                    break;
                case NpcRole.Cleric:
                {
                    float reach = math.min(h * 1.1f, h + 0.5f);
                    AppendGear(b, GearKind.MagicStaff, GearContext.Ceremony, spec, new float3(hand.x, 0.02f, hand.z),
                        Quaternion.identity, reach * 0.6f);
                    glow.Ball(new float3(hand.x, reach + 0.05f * h, hand.z), new float3(0.045f * h), look.Glow, 7, 5);
                    for (int s = -1; s <= 1; s += 2)
                    {
                        b.Slab(r.Chest + new float3(s * r.TrunkX * 0.45f, 0f, r.TrunkZ * 1.3f),
                            r.Hip + new float3(s * r.TrunkX * 0.5f, -0.2f * h, r.TrunkZ * 1.45f), 0.035f * h, 0.006f * h, look.Accent);
                    }

                    break;
                }
                case NpcRole.Healer:
                    b.Ball(r.Head + new float3(0f, 0.12f * hr, -0.1f * hr), new float3(1.07f, 1.05f, 1.1f) * hr, Hex(0xF0ECE0), 8, 5, 0.12f);
                    b.Hoop(math.lerp(r.ShoulderL, r.ElbowL, 0.5f), r.ArmR * 1.6f, 0.012f * h, look.Accent);
                    b.Slab(r.HipL + new float3(-r.TrunkX * 0.55f, -0.1f * h, 0f), r.HipL + new float3(-r.TrunkX * 0.55f, 0.02f * h, 0f), 0.1f * h,
                        0.05f * h, look.Leather);
                    break;
                case NpcRole.Laborer:
                {
                    float3 load = r.ShoulderR + new float3(0.02f * h, 0.09f * h, 0f);
                    b.Slab(load + new float3(0f, -0.06f * h, 0f), load + new float3(0f, 0.08f * h, 0f), 0.18f * h, 0.17f * h, Hex(0x9A7A4A));
                    b.Slab(load + new float3(0f, -0.005f * h, 0f), load + new float3(0f, 0.025f * h, 0f), 0.185f * h, 0.175f * h, Hex(0x6A5030));
                    break;
                }
                case NpcRole.Performer:
                {
                    AppendGear(b, GearKind.Headwear, GearContext.Travel, spec, crown + new float3(0f, 0.15f * hr, 0f),
                        Quaternion.identity, hr * 4.2f);
                    b.Panel(crown + new float3(0.7f * hr, 0.5f * hr, 0f), crown + new float3(0.8f * hr, 0.5f * hr, -0.25f * hr),
                        crown + new float3(1.3f * hr, 1.9f * hr, -0.9f * hr), Hex(0xE8E0D0));
                    // Lute held across the body.
                    float3 bowl = hand + new float3(-0.1f * h, -0.02f * h, 0.03f * h);
                    b.Ball(bowl, new float3(0.085f, 0.1f, 0.045f) * h, Hex(0xB8763A), 7, 5);
                    b.Tube(bowl, bowl + new float3(-0.24f * h, 0.16f * h, 0f), 0.014f * h, 0.011f * h, Hex(0x5A3A22), Hex(0x5A3A22), 4);
                    break;
                }
                case NpcRole.Leader:
                    Cape(b, r, look.Accent, look.Cloth2, 0.8f, false);
                    b.Hoop(r.Head + new float3(0f, 0.55f * hr, 0f), hr * 0.92f, 0.012f * h, Hex(0xE0B840));
                    glow.Ball(r.Head + new float3(0f, 0.62f * hr, 0.88f * hr), new float3(0.1f, 0.13f, 0.06f) * hr, look.Glow, 5, 3);
                    AppendGear(b, GearKind.MagicStaff, GearContext.Status, spec, new float3(hand.x, 0.02f, hand.z),
                        Quaternion.identity, math.min(h * 0.95f, h + 0.3f) * 0.55f);
                    break;
            }
        }

        private static void AppendGear(Fig b, GearKind kind, GearContext ctx, in NpcSpec spec, float3 pos, Quaternion rot, float scale)
        {
            ArchMix mix = spec.BorrowShare > 0f
                ? new ArchMix(spec.BorrowRegion, spec.BorrowEra, spec.BorrowShare, 0f, 0f)
                : ArchMix.Pure(spec.Region, spec.Era);
            GearLookSpec look = GearLooks.Sample(kind, ctx, spec.Region, spec.Era, mix,
                spec.Seed ^ ((ulong)kind + 1UL) * 0x9E3779B97F4A7C15UL);
            SceneryMeshes.Part src = GearMeshes.BuildPart(look);
            var mat = float4x4.TRS(pos, rot, new float3(scale));
            int baseIdx = b.Part.P.Count;
            for (int i = 0; i < src.P.Count; i++)
            {
                b.Part.Add(math.transform(mat, src.P[i]), src.C[i], float2.zero);
            }
            for (int i = 0; i < src.T.Count; i += 3)
            {
                b.Part.T.Add(baseIdx + src.T[i]);
                b.Part.T.Add(baseIdx + src.T[i + 1]);
                b.Part.T.Add(baseIdx + src.T[i + 2]);
            }
        }

        // A shaft standing on the ground through the hand.
        private static void Pole(Fig b, float3 hand, float top, Color tone) =>
            b.Tube(new float3(hand.x, 0.01f, hand.z), new float3(hand.x, top, hand.z), 0.012f + 0.004f * top, 0.011f + 0.003f * top, Shade(tone, 0.85f),
                tone, 5);

        // ---- age and health ------------------------------------------------------------------------------------

        private static void Ailments(Fig b, in NpcSpec spec, in Rig r, in Look look, ref Pcg32 rng)
        {
            float h = r.H;
            Color linen = Hex(0xEDE6D6);
            if (look.Cane)
            {
                b.Tube(new float3(r.HandL.x, 0.01f, r.HandL.z), r.HandL + new float3(0f, 0.02f * h, 0f), 0.011f, 0.013f, Hex(0x4A3424), Hex(0x6A4A30), 5);
                b.Tube(r.HandL + new float3(0f, 0.02f * h, -0.04f * h), r.HandL + new float3(0f, 0.02f * h, 0.05f * h), 0.014f, 0.014f, Hex(0x6A4A30),
                    Hex(0x6A4A30), 5);
            }

            if (look.Bandaged)
            {
                // Where the wrap goes is the person's own story; how many there are follows how ill they are.
                int wraps = spec.Health01 < 0.25f ? 3 : spec.Health01 < 0.35f ? 2 : 1;
                uint first = rng.NextUInt(3);
                for (int i = 0; i < wraps; i++)
                {
                    switch ((first + i) % 3)
                    {
                        case 0:
                            b.Hoop(r.Head + new float3(0f, 0.38f * r.HeadR, 0f), r.HeadR * 1f, 0.016f * h, linen);
                            break;
                        case 1:
                            b.Tube(math.lerp(r.ElbowR, r.HandR, 0.15f), math.lerp(r.ElbowR, r.HandR, 0.8f), r.ArmR * 1.35f, r.ArmR * 1.2f, linen,
                                Shade(linen, 0.9f), 6);
                            break;
                        default:
                            b.Leg = -1;
                            b.Tube(math.lerp(r.KneeL, r.AnkleL, 0.1f), math.lerp(r.KneeL, r.AnkleL, 0.6f), r.LegR * 1.45f, r.LegR * 1.3f, linen,
                                Shade(linen, 0.9f), 7);
                            b.Leg = 0;
                            break;
                    }
                }
            }
        }

        // ---- geometry ------------------------------------------------------------------------------------------

        private readonly struct Ring
        {
            public readonly float3 Centre;
            public readonly float Rx, Rz;
            public readonly Color Tone;

            public Ring(float3 centre, float rx, float rz, Color tone)
            {
                Centre = centre;
                Rx = rx;
                Rz = rz;
                Tone = tone;
            }
        }

        // Same bounded normal as ArchitectureMeshes.Gauss: no log or sqrt whose last bit could differ between clients.
        private static float Gauss(ref Pcg32 rng) =>
            (rng.NextFloat() + rng.NextFloat() + rng.NextFloat() + rng.NextFloat() - 2f) * 1.7320508f;

        private static Color Hex(int rgb) => new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);

        private static Color Shade(Color c, float f) => new Color(c.r * f, c.g * f, c.b * f, 1f);

        private static float Value(Color c) => math.max(c.r, math.max(c.g, c.b));

        // Hue rotation and saturation in HSV keep a dye recognisable; an RGB multiply would grey or clip it.
        private static Color Retint(Color c, float hueShift, float satScale, float valueShift)
        {
            Color.RGBToHSV(c, out float h, out float s, out float v);
            Color o = Color.HSVToRGB(math.frac(h + hueShift + 1f), math.saturate(s * satScale), math.saturate(v * (1f + valueShift)));
            o.a = 1f;
            return o;
        }

        /// <summary>One sub-mesh under construction; every face is wound by the direction it must look.</summary>
        private sealed class Fig
        {
            public readonly SceneryMeshes.Part Part = new SceneryMeshes.Part();

            internal int Leg;
            internal float ClothHipWidth;

            // Vertex index where each run of geometry starts, with whether it is body or part (NpcMeshes.Uma: the UMA
            // body replaces the body runs, the part runs ride on its bones) and which bones it may ride (Ride*). Kept as
            // runs so direct Part.Add calls are tagged too.
            private readonly List<int> _runStart = new List<int>();
            private readonly List<bool> _runIsPart = new List<bool>();
            private readonly List<byte> _runRider = new List<byte>();
            private bool _part;
            private byte _rider;

            public void Mark(bool part)
            {
                _part = part;
                Run();
            }

            public void Ride(byte rider)
            {
                _rider = rider;
                Run();
            }

            private void Run()
            {
                _runStart.Add(Part.P.Count);
                _runIsPart.Add(_part);
                _runRider.Add(_rider);
            }

            // Drops every triangle that touches body geometry (vertices stay, so indices remain valid) and writes each
            // vertex's rider to its construction tag (UV1.x), which facet splitting carries to the mesh.
            public void KeepPartsOnly()
            {
                var isPart = new bool[Part.P.Count];
                for (int run = 0; run < _runStart.Count; run++)
                {
                    int end = run + 1 < _runStart.Count ? _runStart[run + 1] : isPart.Length;
                    for (int i = _runStart[run]; i < end; i++)
                    {
                        isPart[i] = _runIsPart[run];
                        Part.W[i] = new float2(_runRider[run], 0f);
                    }
                }

                var kept = new List<int>(Part.T.Count);
                for (int t = 0; t + 2 < Part.T.Count; t += 3)
                {
                    if (isPart[Part.T[t]] && isPart[Part.T[t + 1]] && isPart[Part.T[t + 2]])
                    {
                        kept.Add(Part.T[t]);
                        kept.Add(Part.T[t + 1]);
                        kept.Add(Part.T[t + 2]);
                    }
                }

                Part.T.Clear();
                Part.T.AddRange(kept);
            }

            private int V(float3 p, Color c)
            {
                c.a = 1f;
                float side = ClothHipWidth > 0f ? math.clamp(p.x / ClothHipWidth, -1f, 1f) : Leg;
                return Part.Add(p, c, new float2(side, ClothHipWidth > 0f ? 0.7f : 1f));
            }

            private void Tri(int a, int b, int c, float3 outward)
            {
                float3 n = math.cross(Part.P[b] - Part.P[a], Part.P[c] - Part.P[a]);
                if (math.lengthsq(n) < 1e-12f)
                {
                    return;
                }

                if (math.dot(n, outward) < 0f)
                {
                    (b, c) = (c, b);
                }

                Part.T.Add(a);
                Part.T.Add(b);
                Part.T.Add(c);
            }

            // A tapered limb between two points, closed at both ends.
            public void Tube(float3 from, float3 to, float r0, float r1, Color c0, Color c1, int sides)
            {
                float3 axis = to - from;
                float length = math.length(axis);
                if (length < 1e-5f)
                {
                    return;
                }

                axis /= length;
                float3 u = math.normalize(math.cross(axis, math.abs(axis.y) < 0.9f ? new float3(0f, 1f, 0f) : new float3(1f, 0f, 0f)));
                float3 v = math.cross(axis, u);
                int first = Part.P.Count;
                for (int end = 0; end < 2; end++)
                {
                    for (int k = 0; k < sides; k++)
                    {
                        float a = k * math.PI * 2f / sides;
                        float3 radial = u * math.cos(a) + v * math.sin(a);
                        V((end == 0 ? from : to) + radial * (end == 0 ? r0 : r1), end == 0 ? c0 : c1);
                    }
                }

                int capA = V(from, c0), capB = V(to, c1);
                for (int k = 0; k < sides; k++)
                {
                    int n = (k + 1) % sides;
                    float a = (k + 0.5f) * math.PI * 2f / sides;
                    float3 radial = u * math.cos(a) + v * math.sin(a);
                    Tri(first + k, first + n, first + sides + n, radial);
                    Tri(first + k, first + sides + n, first + sides + k, radial);
                    Tri(capA, first + k, first + n, -axis);
                    Tri(capB, first + sides + k, first + sides + n, axis);
                }
            }

            // An ellipsoid; with cut > −1 everything below that latitude (−1..1) is flattened onto it (caps, helmets, soles).
            public void Ball(float3 centre, float3 radii, Color tone, int slices, int stacks, float cut = -1f)
            {
                int first = Part.P.Count;
                Color low = Shade(tone, 0.88f);
                for (int i = 0; i <= stacks; i++)
                {
                    float lat = math.PI * (i / (float)stacks - 0.5f);
                    float y = math.max(math.sin(lat), cut), ring = math.cos(lat);
                    for (int k = 0; k < slices; k++)
                    {
                        float a = k * math.PI * 2f / slices;
                        V(centre + new float3(math.cos(a) * ring, y, math.sin(a) * ring) * radii, Color.Lerp(low, tone, y * 0.5f + 0.5f));
                    }
                }

                for (int i = 0; i < stacks; i++)
                {
                    for (int k = 0; k < slices; k++)
                    {
                        int n = (k + 1) % slices;
                        int a = first + i * slices + k, b = first + i * slices + n, c = first + (i + 1) * slices + n, d = first + (i + 1) * slices + k;
                        float3 outward = (Part.P[a] + Part.P[b] + Part.P[c] + Part.P[d]) * 0.25f - centre;
                        // The flattened part is a floor facing down, wherever it sits relative to the centre.
                        if (math.sin(math.PI * ((i + 1) / (float)stacks - 0.5f)) <= cut)
                        {
                            outward = new float3(0f, -1f, 0f);
                        }
                        Tri(a, b, c, outward);
                        Tri(a, c, d, outward);
                    }
                }
            }

            // Closed rows of `sides` points (bottom to top, each counter-clockwise seen from above) joined into one surface,
            // closed by a pole at each end. Winding follows the row order, so overhangs (the back of a chin) face out too.
            public void Shell(float3[] points, Color[] rowTones, int rows, int sides, float3 bottom, float3 top, Color bottomTone, Color topTone)
            {
                int first = Part.P.Count;
                for (int i = 0; i < rows; i++)
                {
                    for (int k = 0; k < sides; k++)
                    {
                        V(points[i * sides + k], rowTones[i]);
                    }
                }

                int poleA = V(bottom, bottomTone), poleB = V(top, topTone);
                for (int i = 0; i + 1 < rows; i++)
                {
                    for (int k = 0; k < sides; k++)
                    {
                        int n = (k + 1) % sides;
                        int a = first + i * sides + k, b = first + i * sides + n, c = first + (i + 1) * sides + n, d = first + (i + 1) * sides + k;
                        float3 outward = math.cross(Part.P[d] - Part.P[a], Part.P[b] - Part.P[a]) + math.cross(Part.P[b] - Part.P[c], Part.P[d] - Part.P[c]);
                        Tri(a, b, c, outward);
                        Tri(a, c, d, outward);
                    }
                }

                int topRow = first + (rows - 1) * sides;
                for (int k = 0; k < sides; k++)
                {
                    int n = (k + 1) % sides;
                    Tri(poleA, first + k, first + n, Part.P[first + k] + Part.P[first + n] - 2f * bottom + new float3(0f, -0.5f, 0f) * math.length(Part.P[first + k] - bottom));
                    Tri(poleB, topRow + k, topRow + n, new float3(0f, 1f, 0f));
                }
            }

            // Horizontal elliptical rings joined in order; `open` ends are left without a cap (hems, cuffs, capes).
            public void Loft(Ring[] rings, int sides, bool capBottom, bool capTop, float hemPleat = 0f)
            {
                int first = Part.P.Count;
                for (int row = 0; row < rings.Length; row++)
                {
                    Ring ring = rings[row];
                    for (int k = 0; k < sides; k++)
                    {
                        float a = k * math.PI * 2f / sides;
                        // Alternate folds open below the waist. Raising the inner stripe at each ridge weaves a chevron.
                        float ridge = (k & 1) == 0 ? hemPleat : 0f;
                        float flare = 1f + ridge * math.saturate(1f - row / 4f);
                        float peak = row == 1 && hemPleat > 0f && (k & 1) == 0
                            ? (rings[2].Centre.y - rings[0].Centre.y) * 0.24f : 0f;
                        V(ring.Centre + new float3(math.cos(a) * ring.Rx * flare, peak, math.sin(a) * ring.Rz * flare), ring.Tone);
                    }
                }

                for (int i = 0; i + 1 < rings.Length; i++)
                {
                    float3 mid = (rings[i].Centre + rings[i + 1].Centre) * 0.5f;
                    for (int k = 0; k < sides; k++)
                    {
                        int n = (k + 1) % sides;
                        int a = first + i * sides + k, b = first + i * sides + n, c = first + (i + 1) * sides + n, d = first + (i + 1) * sides + k;
                        float3 radial = (Part.P[a] + Part.P[b] + Part.P[c] + Part.P[d]) * 0.25f - mid;
                        radial.y = 0f;
                        // A frustum's normal: sideways by its height, and up or down by how much it narrows. The
                        // centroid alone cannot tell a flat brim's top from its underside.
                        float rise = rings[i + 1].Centre.y - rings[i].Centre.y;
                        float narrows = rings[i].Rx + rings[i].Rz - rings[i + 1].Rx - rings[i + 1].Rz;
                        float3 outward = math.normalizesafe(radial) * math.abs(rise) +
                                         new float3(0f, (rise >= 0f ? 0.5f : -0.5f) * narrows, 0f);
                        Tri(a, b, c, outward);
                        Tri(a, c, d, outward);
                        if (!capBottom && i == 0 || !capTop && i + 2 == rings.Length)
                        {
                            // An open hem shows its inside: the lining is the same cloth, facing the other way.
                            Tri(a, b, c, -outward);
                            Tri(a, c, d, -outward);
                        }
                    }
                }

                for (int end = 0; end < 2; end++)
                {
                    if (end == 0 ? !capBottom : !capTop)
                    {
                        continue;
                    }

                    Ring ring = rings[end == 0 ? 0 : rings.Length - 1];
                    int centre = V(ring.Centre, ring.Tone), row = first + (end == 0 ? 0 : (rings.Length - 1) * sides);
                    for (int k = 0; k < sides; k++)
                    {
                        Tri(centre, row + k, row + (k + 1) % sides, new float3(0f, end == 0 ? -1f : 1f, 0f));
                    }
                }
            }

            // A flat torus lying level: belts, bands, halos, collars. rz defaults to a circle.
            public void Hoop(float3 centre, float radius, float thick, Color tone, float rz = -1f)
            {
                const int Sides = 10;
                rz = rz < 0f ? radius : rz;
                int first = Part.P.Count;
                for (int k = 0; k < Sides; k++)
                {
                    float a = k * math.PI * 2f / Sides;
                    var radial = new float3(math.cos(a), 0f, math.sin(a));
                    float3 p = centre + new float3(radial.x * radius, 0f, radial.z * rz);
                    V(p + radial * thick, tone);
                    V(p + new float3(0f, thick, 0f), tone);
                    V(p - radial * thick, Shade(tone, 0.85f));
                    V(p - new float3(0f, thick, 0f), Shade(tone, 0.85f));
                }

                for (int k = 0; k < Sides; k++)
                {
                    int n = (k + 1) % Sides;
                    float a = (k + 0.5f) * math.PI * 2f / Sides;
                    var radial = new float3(math.cos(a), 0f, math.sin(a));
                    for (int q = 0; q < 4; q++)
                    {
                        int q1 = (q + 1) & 3;
                        float3 outward = q == 0 ? radial + new float3(0f, 1f, 0f)
                            : q == 1 ? -radial + new float3(0f, 1f, 0f)
                            : q == 2 ? -radial - new float3(0f, 1f, 0f)
                            : radial - new float3(0f, 1f, 0f);
                        Tri(first + k * 4 + q, first + n * 4 + q, first + n * 4 + q1, outward);
                        Tri(first + k * 4 + q, first + n * 4 + q1, first + k * 4 + q1, outward);
                    }
                }
            }

            // A two-sided triangle: ears, fins, feathers, membranes, cape cloth.
            public void Panel(float3 a, float3 b, float3 c, Color tone)
            {
                float3 n = math.cross(b - a, c - a);
                for (int side = 0; side < 2; side++)
                {
                    // Separate vertices per side, or the crease pass would average two opposite faces to nothing.
                    int i = V(a, tone), j = V(b, tone), k = V(c, Shade(tone, 0.9f));
                    Tri(i, j, k, side == 0 ? n : -n);
                }
            }

            // A box between two points with the given width (across) and depth (toward the viewer's Z or up).
            public void Slab(float3 from, float3 to, float width, float depth, Color tone)
            {
                float3 axis = math.normalize(to - from);
                // Width runs along X wherever the slab is not itself lying along X: aprons, packs and books face the viewer.
                float3 across = math.abs(axis.x) < 0.9f ? new float3(1f, 0f, 0f) : new float3(0f, 0f, 1f);
                float3 w = math.normalize(across - axis * math.dot(across, axis)) * (width * 0.5f);
                float3 d = math.normalize(math.cross(axis, w)) * (depth * 0.5f);
                int first = Part.P.Count;
                Color dark = Shade(tone, 0.85f);
                for (int end = 0; end < 2; end++)
                {
                    float3 p = end == 0 ? from : to;
                    Color c = end == 0 ? dark : tone;
                    V(p - w - d, c);
                    V(p + w - d, c);
                    V(p + w + d, c);
                    V(p - w + d, c);
                }

                float3 centre = (from + to) * 0.5f;
                for (int k = 0; k < 4; k++)
                {
                    int n = (k + 1) & 3;
                    float3 outward = (Part.P[first + k] + Part.P[first + n]) * 0.5f - from;
                    Tri(first + k, first + n, first + 4 + n, outward);
                    Tri(first + k, first + 4 + n, first + 4 + k, outward);
                }

                Tri(first, first + 1, first + 2, from - centre);
                Tri(first, first + 2, first + 3, from - centre);
                Tri(first + 4, first + 5, first + 6, to - centre);
                Tri(first + 4, first + 6, first + 7, to - centre);
            }
        }
    }
}
