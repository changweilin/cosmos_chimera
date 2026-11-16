using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Unity.Mathematics;

namespace Cosmimera.WorldGen
{
    /// <summary>People a settlement can be home to (種族).</summary>
    /// <remarks>
    /// REQ: docs/design/characters.md#路人-npc (種族).
    /// Pre: none. Post: stable values (they are hashed and seed the appearance streams); append only. Side effects: none.
    /// </remarks>
    public enum NpcRace
    {
        Human = 0,
        Elf = 1,
        Dwarf = 2,
        Goblin = 3,
        Orc = 4,
        /// <summary>鳥人: feathered, winged, beaked.</summary>
        Avian = 5,
        /// <summary>魚人: finned and scaled, at home on coasts and riverbanks.</summary>
        Merfolk = 6,
        Lizardfolk = 7,
        /// <summary>龍人: horned, scaled, with a heavy tail and vestigial wings.</summary>
        Dragonkin = 8,
        Zombie = 9,
        Skeleton = 10,
        Werewolf = 11,
        Vampire = 12,
        Angel = 13,
        Demon = 14,
    }

    /// <summary>What a resident does for a living (職業); how it is dressed for it depends on the era, which the presentation owns.</summary>
    /// <remarks>Pre: none. Post: stable values; append only. Side effects: none.</remarks>
    public enum NpcRole
    {
        /// <summary>Children, and adults without a trade.</summary>
        None = 0,
        Farmer = 1,
        /// <summary>Hunters and gatherers.</summary>
        Forager = 2,
        Fisher = 3,
        Miner = 4,
        Artisan = 5,
        Merchant = 6,
        Guard = 7,
        Scholar = 8,
        Cleric = 9,
        Healer = 10,
        Laborer = 11,
        Performer = 12,
        /// <summary>Chiefs, mayors, nobles.</summary>
        Leader = 13,
    }

    /// <summary>Where a resident is in its people's lifespan.</summary>
    /// <remarks>Pre: none. Post: stable, ascending with age. Side effects: none.</remarks>
    public enum NpcLifeStage
    {
        Child = 0,
        Youth = 1,
        Adult = 2,
        MiddleAged = 3,
        Elder = 4,
    }

    /// <summary>Body frame a resident is drawn on.</summary>
    /// <remarks>Pre: none. Post: stable values. Side effects: none.</remarks>
    public enum NpcSex
    {
        Male = 0,
        Female = 1,
    }

    /// <summary>
    /// How the planet's era shifts the age and health its settlements average. The era is a presentation axis
    /// (ArchitectureMeshes.PlanetStyleFor), so the caller translates it into these two numbers.
    /// </summary>
    /// <remarks>Pre: built by the constructor. Post: immutable. Side effects: none.</remarks>
    public readonly struct PopulaceTemper
    {
        /// <summary>Describes an era's pull on age and health.</summary>
        /// <remarks>Pre: both in [−200, 200] (permille). Post: properties return the arguments. Side effects: none.</remarks>
        public PopulaceTemper(int ageShiftPermille, int healthShiftPermille)
        {
            Contract.Requires(ageShiftPermille >= -200 && ageShiftPermille <= 200 && healthShiftPermille >= -200 &&
                              healthShiftPermille <= 200, "shifts must be in [-200, 200]");
            AgeShiftPermille = ageShiftPermille;
            HealthShiftPermille = healthShiftPermille;
        }

        /// <summary>Added to every settlement's mean age (permille of lifespan).</summary>
        /// <remarks>Pre: none. Post: in [−200, 200]. Side effects: none.</remarks>
        public int AgeShiftPermille { get; }

        /// <summary>Added to every settlement's mean health (permille).</summary>
        /// <remarks>Pre: none. Post: in [−200, 200]. Side effects: none.</remarks>
        public int HealthShiftPermille { get; }
    }

    /// <summary>One resident: who it is and where it stands.</summary>
    /// <remarks>
    /// Pre: produced by PopulacePlanner. Post: immutable.
    /// The *ZMilli fields are bounded standard-normal draws × 1000 (|z| ≤ 3464): the presentation scales them into
    /// height, girth and how far the outfit strays toward the settlement's secondary style, so most residents sit near
    /// their settlement's mean and a few stand out.
    /// Side effects: none.
    /// </remarks>
    public readonly struct RunResident
    {
        internal RunResident(int settlement, int building, NpcRace race, NpcRole role, NpcSex sex, int agePermille,
            int ageYears, NpcLifeStage stage, int healthPermille, int statureZMilli, int bulkZMilli, int styleZMilli,
            ulong seed, float3 direction, float3 facing)
        {
            Settlement = settlement;
            Building = building;
            Race = race;
            Role = role;
            Sex = sex;
            AgePermille = agePermille;
            AgeYears = ageYears;
            LifeStage = stage;
            HealthPermille = healthPermille;
            StatureZMilli = statureZMilli;
            BulkZMilli = bulkZMilli;
            StyleZMilli = styleZMilli;
            Seed = seed;
            Direction = direction;
            Facing = facing;
        }

        /// <summary>Index of the settlement in RunLayout.Settlements.</summary>
        /// <remarks>Pre: none. Post: valid index of an inhabited settlement. Side effects: none.</remarks>
        public int Settlement { get; }

        /// <summary>Index in RunLayout.Buildings of the building the resident stands in front of.</summary>
        /// <remarks>Pre: none. Post: valid index; that building belongs to Settlement. Side effects: none.</remarks>
        public int Building { get; }

        /// <summary>Race.</summary>
        /// <remarks>Pre: none. Post: defined. Side effects: none.</remarks>
        public NpcRace Race { get; }

        /// <summary>Trade.</summary>
        /// <remarks>Pre: none. Post: defined; None for every Child. Side effects: none.</remarks>
        public NpcRole Role { get; }

        /// <summary>Body frame.</summary>
        /// <remarks>Pre: none. Post: defined. Side effects: none.</remarks>
        public NpcSex Sex { get; }

        /// <summary>Age as a share of the race's lifespan.</summary>
        /// <remarks>Pre: none. Post: in [40, 980]. Side effects: none.</remarks>
        public int AgePermille { get; }

        /// <summary>Age in years (the undead count the years they lived).</summary>
        /// <remarks>Pre: none. Post: ≥ 1; AgePermille × PopulacePlanner.LifespanYears(Race) / 1000, rounded down, at least 1. Side effects: none.</remarks>
        public int AgeYears { get; }

        /// <summary>Life stage AgePermille falls in.</summary>
        /// <remarks>Pre: none. Post: == PopulacePlanner.StageFor(AgePermille). Side effects: none.</remarks>
        public NpcLifeStage LifeStage { get; }

        /// <summary>Health: 1000 = hale, low = stooped, bandaged, leaning on a cane. Appearance only (characters.md#路人-npc).</summary>
        /// <remarks>Pre: none. Post: in [50, 1000]. Side effects: none.</remarks>
        public int HealthPermille { get; }

        /// <summary>Height relative to the race's usual, standard deviations × 1000.</summary>
        /// <remarks>Pre: none. Post: in [−3464, 3464]. Side effects: none.</remarks>
        public int StatureZMilli { get; }

        /// <summary>Girth relative to the race's usual, standard deviations × 1000.</summary>
        /// <remarks>Pre: none. Post: in [−3464, 3464]. Side effects: none.</remarks>
        public int BulkZMilli { get; }

        /// <summary>How far the outfit strays from the settlement's mean style mix, standard deviations × 1000.</summary>
        /// <remarks>Pre: none. Post: in [−3464, 3464]. Side effects: none.</remarks>
        public int StyleZMilli { get; }

        /// <summary>Seed of everything else the presentation varies (hair, colours, pose, which garments are borrowed).</summary>
        /// <remarks>Pre: none. Post: as generated; differs between residents of one run with overwhelming probability. Side effects: none.</remarks>
        public ulong Seed { get; }

        /// <summary>Where the resident stands (unit direction).</summary>
        /// <remarks>Pre: none. Post: unit length; outside every building footprint. Side effects: none.</remarks>
        public float3 Direction { get; }

        /// <summary>Unit tangent the resident faces.</summary>
        /// <remarks>Pre: none. Post: unit length; |dot(Facing, Direction)| &lt; 1e-3. Side effects: none.</remarks>
        public float3 Facing { get; }
    }

    /// <summary>The make-up of one settlement's population: residents are drawn around these means.</summary>
    /// <remarks>Pre: produced by PopulacePlanner. Post: immutable. Side effects: none.</remarks>
    public readonly struct SettlementPopulace
    {
        internal SettlementPopulace(int settlement, NpcRace primary, NpcRace secondary, int secondaryPermille,
            int minorityPermille, int meanAgePermille, int meanHealthPermille, int firstResident, int residentCount)
        {
            Settlement = settlement;
            PrimaryRace = primary;
            SecondaryRace = secondary;
            SecondarySharePermille = secondaryPermille;
            MinoritySharePermille = minorityPermille;
            MeanAgePermille = meanAgePermille;
            MeanHealthPermille = meanHealthPermille;
            FirstResident = firstResident;
            ResidentCount = residentCount;
        }

        /// <summary>Index in RunLayout.Settlements.</summary>
        /// <remarks>Pre: none. Post: valid index; never Ruins or Lair. Side effects: none.</remarks>
        public int Settlement { get; }

        /// <summary>Race most residents belong to.</summary>
        /// <remarks>Pre: none. Post: defined. Side effects: none.</remarks>
        public NpcRace PrimaryRace { get; }

        /// <summary>Second most common race.</summary>
        /// <remarks>Pre: none. Post: defined, != PrimaryRace. Side effects: none.</remarks>
        public NpcRace SecondaryRace { get; }

        /// <summary>Expected share of SecondaryRace.</summary>
        /// <remarks>Pre: none. Post: in [0, 480]. Side effects: none.</remarks>
        public int SecondarySharePermille { get; }

        /// <summary>Expected share of residents of any other race (travellers, settlers).</summary>
        /// <remarks>Pre: none. Post: in [0, 250]; SecondarySharePermille + MinoritySharePermille ≤ 730. Side effects: none.</remarks>
        public int MinoritySharePermille { get; }

        /// <summary>Mean age the residents were drawn around, as a share of lifespan.</summary>
        /// <remarks>Pre: none. Post: in [200, 650]. Side effects: none.</remarks>
        public int MeanAgePermille { get; }

        /// <summary>Mean health the residents were drawn around.</summary>
        /// <remarks>Pre: none. Post: in [300, 950]. Side effects: none.</remarks>
        public int MeanHealthPermille { get; }

        /// <summary>First index of this settlement's residents in RunPopulace.Residents.</summary>
        /// <remarks>Pre: none. Post: ≥ 0. Side effects: none.</remarks>
        public int FirstResident { get; }

        /// <summary>Number of residents (a settlement with no building on land has none).</summary>
        /// <remarks>Pre: none. Post: ≥ 0. Side effects: none.</remarks>
        public int ResidentCount { get; }
    }

    /// <summary>Everyone who lives on the planet of one run.</summary>
    /// <remarks>
    /// Pre: produced by <see cref="PopulacePlanner.Generate"/>. Post: immutable; same inputs → same populace.
    /// Side effects: none.
    /// </remarks>
    public sealed class RunPopulace
    {
        private readonly float _radius;
        private readonly RunResident[] _residents;

        internal RunPopulace(NpcRace primary, NpcRace secondary, SettlementPopulace[] settlements, RunResident[] residents,
            float radius)
        {
            PrimaryRace = primary;
            SecondaryRace = secondary;
            Settlements = settlements;
            Residents = residents;
            _residents = residents;
            _radius = radius;
        }

        /// <summary>Race most of the planet's settlements are built by.</summary>
        /// <remarks>Pre: none. Post: defined. Side effects: none.</remarks>
        public NpcRace PrimaryRace { get; }

        /// <summary>The planet's usual second race.</summary>
        /// <remarks>Pre: none. Post: defined, != PrimaryRace. Side effects: none.</remarks>
        public NpcRace SecondaryRace { get; }

        /// <summary>One entry per inhabited settlement (everything but Ruins and Lair), in RunLayout order.</summary>
        /// <remarks>Pre: none. Post: ascending Settlement. Side effects: none.</remarks>
        public IReadOnlyList<SettlementPopulace> Settlements { get; }

        /// <summary>All residents, grouped by settlement in Settlements order.</summary>
        /// <remarks>Pre: none. Post: Residents[FirstResident .. +ResidentCount) belong to that settlement. Side effects: none.</remarks>
        public IReadOnlyList<RunResident> Residents { get; }

        /// <summary>True when a body of radius <paramref name="clearanceM"/> centred at <paramref name="direction"/> would overlap a resident.</summary>
        /// <remarks>
        /// REQ: characters.md#路人-npc (規則層實體，有碰撞).
        /// Pre: direction finite and non-zero (normalized internally); clearanceM finite and ≥ 0.
        /// Post: true iff some resident stands within clearanceM + PopulacePlanner.BodyRadiusM (chord distance at the
        ///       planet's radius). Side effects: none.
        /// </remarks>
        public bool IsBlocked(float3 direction, float clearanceM)
        {
            Contract.Requires(math.all(math.isfinite(direction)) && math.lengthsq(direction) > 0f,
                "direction must be finite and non-zero");
            Contract.Requires(math.isfinite(clearanceM) && clearanceM >= 0f, "clearanceM must be finite and >= 0");
            float3 d = math.normalize(direction);
            float reach = (clearanceM + PopulacePlanner.BodyRadiusM) / _radius;
            float reachSq = reach * reach;
            for (int i = 0; i < _residents.Length; i++)
            {
                if (math.distancesq(_residents[i].Direction, d) < reachSq)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
