using System;
using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.Core.Random;
using Cosmimera.GameData;
using Cosmimera.Planet;
using Unity.Mathematics;

namespace Cosmimera.WorldGen
{
    /// <summary>
    /// Peoples the inhabited settlements of a run: which races live on the planet, each settlement's own mix, and every
    /// resident's race, trade, age, health and build.
    /// REQ: docs/design/characters.md#路人-npc.
    /// </summary>
    /// <remarks>
    /// Determinism: one Pcg32(seed, 'N') stream decides every discrete field with integer arithmetic only (bounded
    /// normals are sums of four integer draws), so what <see cref="Hash"/> covers never depends on float rounding.
    /// Where a resident stands is drawn from that resident's own stream afterwards: a rejected standing spot drops the
    /// resident but cannot shift anyone else's identity.
    /// The populace is separate from <see cref="RunLayout"/>, so adding people does not move a road or a building.
    /// </remarks>
    public static class PopulacePlanner
    {
        /// <summary>Bump when any output for the same inputs changes (snapshot tests pin it).</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const int GeneratorVersion = 1;

        /// <summary>Radius of the circle a resident blocks (m).</summary>
        /// <remarks>Pre: none. Post: constant > 0. Side effects: none.</remarks>
        public const float BodyRadiusM = 0.4f;

        /// <summary>Most residents one settlement holds.</summary>
        /// <remarks>Pre: none. Post: constant > 0. Side effects: none.</remarks>
        public const int MaxPerSettlement = 60;

        /// <summary>Number of races.</summary>
        /// <remarks>Pre: none. Post: constant; every NpcRace value is below it. Side effects: none.</remarks>
        public const int RaceCount = 15;

        /// <summary>Number of roles.</summary>
        /// <remarks>Pre: none. Post: constant; every NpcRole value is below it. Side effects: none.</remarks>
        public const int RoleCount = 14;

        private const float ResidentGapM = 1f;
        private const int StandAttempts = 12;
        // Individual spread around the settlement mean, permille per standard deviation.
        private const int AgeSigma = 170;
        private const int HealthSigma = 130;

        // How common each people is across all planets; humans lead without drowning out the rest.
        private static readonly int[] BaseWeight = { 20, 10, 10, 8, 8, 6, 6, 6, 4, 3, 3, 4, 3, 3, 3 };

        private static readonly int[] Lifespan = { 80, 600, 250, 45, 60, 60, 120, 90, 400, 80, 80, 80, 900, 1000, 800 };

        private static readonly NpcDutySpec[] DutySpecs =
        {
            new NpcDutySpec(NpcDuty.IdlePlay, WanderMode.BoundedRandom, 6000,
                new WanderDistribution(240, 40, 50, 80, 600), new WanderDistribution(2500, 700, 1000, 500, 10000),
                new WanderDistribution(3000, 900, 1200, 500, 15000)),
            new NpcDutySpec(NpcDuty.FieldTending, WanderMode.Shuttle, 8000,
                new WanderDistribution(220, 30, 40, 80, 500), new WanderDistribution(4000, 800, 1200, 500, 15000),
                new WanderDistribution(5000, 1200, 1800, 500, 20000)),
            new NpcDutySpec(NpcDuty.CatchSorting, WanderMode.BoundedRandom, 7000,
                new WanderDistribution(250, 35, 45, 80, 600), new WanderDistribution(3500, 800, 1100, 500, 12000),
                new WanderDistribution(4500, 1100, 1500, 500, 18000)),
            new NpcDutySpec(NpcDuty.NetMending, WanderMode.BoundedRandom, 6000,
                new WanderDistribution(200, 30, 35, 80, 450), new WanderDistribution(3000, 600, 900, 500, 10000),
                new WanderDistribution(6000, 1500, 2000, 500, 24000)),
            new NpcDutySpec(NpcDuty.OreSorting, WanderMode.Shuttle, 7000,
                new WanderDistribution(220, 30, 40, 80, 500), new WanderDistribution(3500, 700, 1000, 500, 12000),
                new WanderDistribution(5000, 1200, 1600, 500, 20000)),
            new NpcDutySpec(NpcDuty.WorkshopCraft, WanderMode.BoundedRandom, 5000,
                new WanderDistribution(200, 25, 35, 80, 450), new WanderDistribution(2500, 600, 800, 500, 9000),
                new WanderDistribution(6500, 1500, 2000, 500, 25000)),
            new NpcDutySpec(NpcDuty.MarketHawking, WanderMode.Shuttle, 8000,
                new WanderDistribution(250, 35, 45, 80, 550), new WanderDistribution(3500, 700, 1000, 500, 12000),
                new WanderDistribution(4500, 1000, 1500, 500, 18000)),
            new NpcDutySpec(NpcDuty.TownPatrol, WanderMode.Loop, 12000,
                new WanderDistribution(300, 40, 50, 100, 700), new WanderDistribution(5500, 1000, 1400, 800, 18000),
                new WanderDistribution(3000, 800, 1000, 400, 12000)),
            new NpcDutySpec(NpcDuty.ArchiveStudy, WanderMode.BoundedRandom, 5000,
                new WanderDistribution(180, 25, 30, 80, 400), new WanderDistribution(2500, 500, 750, 500, 9000),
                new WanderDistribution(7500, 1800, 2200, 500, 28000)),
            new NpcDutySpec(NpcDuty.ShrineBlessing, WanderMode.BoundedRandom, 6000,
                new WanderDistribution(190, 25, 30, 80, 420), new WanderDistribution(3000, 600, 850, 500, 10000),
                new WanderDistribution(7000, 1600, 2000, 500, 25000)),
            new NpcDutySpec(NpcDuty.HerbTriage, WanderMode.BoundedRandom, 6000,
                new WanderDistribution(230, 30, 40, 80, 520), new WanderDistribution(3000, 650, 900, 500, 11000),
                new WanderDistribution(5500, 1300, 1800, 500, 20000)),
            new NpcDutySpec(NpcDuty.CargoHauling, WanderMode.Shuttle, 10000,
                new WanderDistribution(260, 35, 45, 100, 600), new WanderDistribution(5000, 1000, 1300, 700, 16000),
                new WanderDistribution(4000, 1000, 1300, 500, 15000)),
            new NpcDutySpec(NpcDuty.StreetBusking, WanderMode.Loop, 7000,
                new WanderDistribution(240, 35, 45, 80, 550), new WanderDistribution(3500, 800, 1000, 500, 12000),
                new WanderDistribution(5500, 1300, 1800, 500, 22000)),
            new NpcDutySpec(NpcDuty.CivicInspect, WanderMode.Loop, 11000,
                new WanderDistribution(260, 35, 45, 90, 600), new WanderDistribution(4500, 900, 1200, 600, 15000),
                new WanderDistribution(4500, 1100, 1500, 500, 18000)),
        };

        /// <summary>Settlement duty corresponding to <paramref name="role"/>.</summary>
        /// <remarks>Pre: role defined. Post: defined NpcDuty. Side effects: none.</remarks>
        public static NpcDuty DutyFor(NpcRole role)
        {
            Contract.Requires(Enum.IsDefined(typeof(NpcRole), role), "role must be defined");
            return (NpcDuty)(int)role;
        }

        /// <summary>Bounded wander specification for <paramref name="duty"/>.</summary>
        /// <remarks>Pre: duty defined. Post: valid NpcDutySpec with Duty == duty. Side effects: none.</remarks>
        public static NpcDutySpec DutySpecFor(NpcDuty duty)
        {
            Contract.Requires(Enum.IsDefined(typeof(NpcDuty), duty), "duty must be defined");
            return DutySpecs[(int)duty];
        }

        /// <summary>Deterministically draws individual duty wander means for <paramref name="role"/> from <paramref name="residentSeed"/>.</summary>
        /// <remarks>
        /// REQ: docs/design/characters.md#職業與聚落職務.
        /// Pre: role defined.
        /// Post: returns DutyFor(role); outputs match DutySpecFor(duty) bounds; independent stream ('D').
        /// Side effects: none.
        /// </remarks>
        public static NpcDuty DrawDuty(NpcRole role, ulong residentSeed, out WanderMode mode, out int radiusMm,
            out int speedPermilleMean, out int moveMsMean, out int pauseMsMean)
        {
            NpcDuty duty = DutyFor(role);
            NpcDutySpec spec = DutySpecFor(duty);
            var rng = new Pcg32(residentSeed, 'D');
            mode = spec.Mode;
            radiusMm = spec.RadiusMm;
            speedPermilleMean = BoundedNormal.Sample(ref rng, spec.SpeedPermille.Mean, spec.SpeedPermille.IndividualSd,
                spec.SpeedPermille.Min, spec.SpeedPermille.Max);
            moveMsMean = BoundedNormal.Sample(ref rng, spec.MoveMs.Mean, spec.MoveMs.IndividualSd,
                spec.MoveMs.Min, spec.MoveMs.Max);
            pauseMsMean = BoundedNormal.Sample(ref rng, spec.PauseMs.Mean, spec.PauseMs.IndividualSd,
                spec.PauseMs.Min, spec.PauseMs.Max);
            return duty;
        }

        // Rows: Tribe, Town, City, CityState, Village (SettlementKind order); columns: NpcRole.
        private static readonly int[][] RoleWeight =
        {
            new[] { 4, 4, 12, 6, 2, 6, 2, 6, 1, 5, 4, 4, 3, 2 },
            new[] { 4, 8, 3, 3, 4, 10, 8, 5, 3, 4, 3, 8, 3, 2 },
            new[] { 5, 3, 1, 2, 3, 12, 12, 7, 6, 4, 4, 12, 5, 3 },
            new[] { 4, 3, 1, 2, 3, 10, 10, 12, 7, 6, 4, 9, 4, 5 },
            new[] { 4, 14, 6, 5, 3, 6, 3, 3, 1, 3, 3, 6, 2, 1 },
        };

        /// <summary>Years a member of <paramref name="race"/> lives (the undead: the life they had).</summary>
        /// <remarks>Pre: race defined. Post: > 0. Side effects: none.</remarks>
        public static int LifespanYears(NpcRace race)
        {
            Contract.Requires(Enum.IsDefined(typeof(NpcRace), race), "race must be defined");
            return Lifespan[(int)race];
        }

        /// <summary>Life stage of an age given as a share of lifespan.</summary>
        /// <remarks>Pre: 0 ≤ agePermille ≤ 1000. Post: non-decreasing in agePermille. Side effects: none.</remarks>
        public static NpcLifeStage StageFor(int agePermille)
        {
            Contract.Requires(agePermille >= 0 && agePermille <= 1000, "agePermille must be in [0, 1000]");
            return agePermille < 160 ? NpcLifeStage.Child
                : agePermille < 250 ? NpcLifeStage.Youth
                : agePermille < 500 ? NpcLifeStage.Adult
                : agePermille < 720 ? NpcLifeStage.MiddleAged
                : NpcLifeStage.Elder;
        }

        /// <summary>Relative odds of each race being a planet's people, by planet type.</summary>
        /// <remarks>
        /// Pre: none (unknown or null ids get the base odds). Post: RaceCount entries, all > 0; a new array each call.
        /// Side effects: allocates.
        /// </remarks>
        public static int[] RaceWeights(string planetTypeId)
        {
            var w = (int[])BaseWeight.Clone();
            void Favour(NpcRace race, int times) => w[(int)race] *= times;
            switch (planetTypeId)
            {
                case "verdant_planet":
                    Favour(NpcRace.Elf, 2);
                    Favour(NpcRace.Werewolf, 2);
                    break;
                case "lava_planet":
                    Favour(NpcRace.Dragonkin, 3);
                    Favour(NpcRace.Demon, 3);
                    Favour(NpcRace.Orc, 2);
                    Favour(NpcRace.Lizardfolk, 2);
                    break;
                case "ice_crystal_planet":
                    Favour(NpcRace.Dwarf, 2);
                    Favour(NpcRace.Werewolf, 2);
                    Favour(NpcRace.Skeleton, 2);
                    break;
                case "storm_planet":
                    Favour(NpcRace.Avian, 3);
                    Favour(NpcRace.Angel, 3);
                    Favour(NpcRace.Merfolk, 2);
                    break;
                case "mineral_planet":
                    Favour(NpcRace.Dwarf, 3);
                    Favour(NpcRace.Goblin, 2);
                    Favour(NpcRace.Skeleton, 2);
                    break;
                case "twilight_planet":
                    Favour(NpcRace.Vampire, 3);
                    Favour(NpcRace.Zombie, 2);
                    Favour(NpcRace.Demon, 2);
                    Favour(NpcRace.Elf, 2);
                    break;
            }

            return w;
        }

        /// <summary>Peoples every inhabited settlement of <paramref name="layout"/>.</summary>
        /// <remarks>
        /// Pre: planet valid; plan and layout non-null and layout generated from plan; isStandable null (anywhere) or a
        ///      pure predicate on unit directions (land that no wall or landmark owns).
        /// Post: one SettlementPopulace per settlement that is not Ruins or Lair, in layout order; each holds at most
        ///       MaxPerSettlement residents, every one standing outside all building footprints, on isStandable ground,
        ///       at least 1 m from its neighbours, in front of a building of its own settlement. Residents are drawn
        ///       around their settlement's race mix, mean age and mean health with bounded normals; every Child has
        ///       role None. Same inputs → same populace.
        /// Side effects: allocates; calls isStandable.
        /// </remarks>
        public static RunPopulace Generate(in PlanetConfig planet, RunPlan plan, RunLayout layout, in PopulaceTemper temper,
            Func<float3, bool> isStandable)
        {
            Contract.Requires(planet.IsValid, "planet must be valid");
            Contract.Requires(plan != null && layout != null, "plan and layout must not be null");
            var rng = new Pcg32(plan.Seed, 'N');
            int[] weights = RaceWeights(plan.PlanetTypeId);
            NpcRace primary = Pick(weights, -1, ref rng);
            NpcRace secondary = Pick(weights, (int)primary, ref rng);
            int planetSecondary = 100 + (int)rng.NextUInt(301);
            int planetMinority = 30 + (int)rng.NextUInt(91);

            var clearance = new LayoutClearance(planet, layout, 0f);
            var profiles = new List<SettlementPopulace>();
            var residents = new List<RunResident>();
            var placed = new List<float3>();
            foreach (RunSettlement s in layout.Settlements)
            {
                if (s.Kind == SettlementKind.Ruins || s.Kind == SettlementKind.Lair)
                {
                    continue;
                }

                string theme = s.Region < plan.Regions.Count ? plan.Regions[s.Region].ThemeId : null;
                // Every draw is taken whether or not it is used, so one settlement's theme never shifts the next one's stream.
                uint mixRoll = rng.NextUInt(100);
                NpcRace other = Pick(weights, (int)primary, ref rng);
                uint enclaveRoll = rng.NextUInt(2);
                NpcRace local = primary, second = secondary;
                if (mixRoll >= 85)
                {
                    // An enclave: the planet's minority people are the majority here, among neighbours of the usual kind.
                    local = enclaveRoll == 0 ? secondary : other;
                    second = primary;
                }
                else if (mixRoll >= 60)
                {
                    int themed = ThemeRace(theme);
                    second = themed >= 0 && themed != (int)primary ? (NpcRace)themed : other;
                }

                int secondaryShare = math.clamp(planetSecondary + GaussMilli(ref rng) * 80 / 1000, 0, 480);
                int minorityShare = math.clamp(planetMinority + GaussMilli(ref rng) * 40 / 1000, 0, 250);
                int meanAge = math.clamp(KindAge(s.Kind) + temper.AgeShiftPermille + GaussMilli(ref rng) * 40 / 1000, 200, 650);
                int meanHealth = math.clamp(
                    KindHealth(s.Kind) + ThemeHealth(theme) + temper.HealthShiftPermille + GaussMilli(ref rng) * 50 / 1000, 300, 950);

                int first = residents.Count;
                placed.Clear();
                int[] roles = RoleWeights(s.Kind, theme);
                for (int b = s.FirstBuilding; b < s.FirstBuilding + s.BuildingCount; b++)
                {
                    RunBuilding building = layout.Buildings[b];
                    int household = Household(building.Kind, ref rng);
                    for (int i = 0; i < household; i++)
                    {
                        // Drawn even when the settlement is full: the cap must not shift later settlements' streams.
                        Identity id = Identity.Draw(local, second, secondaryShare, minorityShare, meanAge, meanHealth, weights, roles,
                            ref rng);
                        if (residents.Count - first >= MaxPerSettlement)
                        {
                            continue;
                        }

                        if (Stand(planet, building, id.Seed, clearance, isStandable, placed, out float3 at, out float3 facing))
                        {
                            placed.Add(at);
                            NpcDuty duty = DrawDuty(id.Role, id.Seed, out WanderMode wanderMode, out int wanderRadiusMm,
                                out int speedPermilleMean, out int moveMsMean, out int pauseMsMean);
                            residents.Add(new RunResident(s.Index, b, id.Race, id.Role, id.Sex, id.AgePermille,
                                math.max(1, id.AgePermille * Lifespan[(int)id.Race] / 1000), StageFor(id.AgePermille),
                                id.HealthPermille, id.Stature, id.Bulk, id.Style, id.Seed, at, facing,
                                duty, wanderMode, wanderRadiusMm, speedPermilleMean, moveMsMean, pauseMsMean));
                        }
                    }
                }

                profiles.Add(new SettlementPopulace(s.Index, local, second, secondaryShare, minorityShare, meanAge, meanHealth,
                    first, residents.Count - first));
            }

            return new RunPopulace(primary, secondary, profiles.ToArray(), residents.ToArray(), planet.Radius,
                clearance, isStandable);
        }

        /// <summary>Order-sensitive FNV-1a digest of the populace's discrete content (no float directions).</summary>
        /// <remarks>
        /// Pre: populace non-null. Post: same populace → same value; covers the planet's races, every settlement
        ///      profile and every resident's identity fields and seed.
        /// Side effects: none.
        /// </remarks>
        public static ulong Hash(RunPopulace populace)
        {
            Contract.Requires(populace != null, "populace must not be null");
            ulong h = 14695981039346656037UL;
            void Mix(ulong v)
            {
                for (int i = 0; i < 8; i++)
                {
                    h = (h ^ (v & 0xFF)) * 1099511628211UL;
                    v >>= 8;
                }
            }

            Mix((ulong)populace.PrimaryRace);
            Mix((ulong)populace.SecondaryRace);
            foreach (SettlementPopulace s in populace.Settlements)
            {
                Mix((ulong)s.Settlement);
                Mix((ulong)s.PrimaryRace);
                Mix((ulong)s.SecondaryRace);
                Mix((ulong)s.SecondarySharePermille);
                Mix((ulong)s.MinoritySharePermille);
                Mix((ulong)s.MeanAgePermille);
                Mix((ulong)s.MeanHealthPermille);
                Mix((ulong)s.ResidentCount);
            }

            foreach (RunResident r in populace.Residents)
            {
                Mix((ulong)r.Settlement);
                Mix((ulong)r.Building);
                Mix((ulong)r.Race);
                Mix((ulong)r.Role);
                Mix((ulong)r.Sex);
                Mix((ulong)r.AgePermille);
                Mix((ulong)r.HealthPermille);
                Mix(unchecked((ulong)r.StatureZMilli));
                Mix(unchecked((ulong)r.BulkZMilli));
                Mix(unchecked((ulong)r.StyleZMilli));
                Mix(r.Seed);
            }

            return h;
        }

        private struct Identity
        {
            public NpcRace Race;
            public NpcRole Role;
            public NpcSex Sex;
            public int AgePermille;
            public int HealthPermille;
            public int Stature;
            public int Bulk;
            public int Style;
            public ulong Seed;

            public static Identity Draw(NpcRace primary, NpcRace secondary, int secondaryShare, int minorityShare, int meanAge,
                int meanHealth, int[] raceWeights, int[] roleWeights, ref Pcg32 rng)
            {
                var id = default(Identity);
                uint raceRoll = rng.NextUInt(1000);
                NpcRace stranger = Pick(raceWeights, -1, ref rng);
                id.Race = raceRoll < minorityShare ? stranger
                    : raceRoll < minorityShare + secondaryShare ? secondary
                    : primary;
                id.Sex = (NpcSex)rng.NextUInt(2);
                id.AgePermille = math.clamp(meanAge + GaussMilli(ref rng) * AgeSigma / 1000, 40, 980);
                NpcLifeStage stage = StageFor(id.AgePermille);
                int wear = stage == NpcLifeStage.Elder ? 120 : stage == NpcLifeStage.MiddleAged ? 40 : 0;
                id.HealthPermille = math.clamp(meanHealth - wear + GaussMilli(ref rng) * HealthSigma / 1000, 50, 1000);
                NpcRole role = PickRole(roleWeights, id.Race, ref rng);
                id.Role = stage == NpcLifeStage.Child ? NpcRole.None : role;
                id.Stature = GaussMilli(ref rng);
                id.Bulk = GaussMilli(ref rng);
                id.Style = GaussMilli(ref rng);
                id.Seed = ((ulong)rng.NextUInt() << 32) | rng.NextUInt();
                return id;
            }
        }

        // Irwin–Hall(4) on integers, scaled to unit variance × 1000: bounded (|z| ≤ 3461) and free of float rounding.
        // One draw of 0..1023 has variance (1024² − 1) / 12; four of them have standard deviation 591.2.
        private static int GaussMilli(ref Pcg32 rng)
        {
            int sum = (int)(rng.NextUInt(1024) + rng.NextUInt(1024) + rng.NextUInt(1024) + rng.NextUInt(1024)) - 2046;
            return sum * 1000 / 591;
        }

        private static NpcRace Pick(int[] weights, int exclude, ref Pcg32 rng)
        {
            int total = 0;
            for (int i = 0; i < weights.Length; i++)
            {
                total += i == exclude ? 0 : weights[i];
            }

            int roll = (int)rng.NextUInt((uint)total);
            for (int i = 0; i < weights.Length; i++)
            {
                if (i == exclude)
                {
                    continue;
                }

                roll -= weights[i];
                if (roll < 0)
                {
                    return (NpcRace)i;
                }
            }

            throw new InvalidOperationException("weights must sum to more than zero");
        }

        private static NpcRole PickRole(int[] weights, NpcRace race, ref Pcg32 rng)
        {
            // What each people is known for doubles its odds of taking that trade; nobody is barred from any.
            (NpcRole a, NpcRole b) bent = race switch
            {
                NpcRace.Elf => (NpcRole.Scholar, NpcRole.Forager),
                NpcRace.Dwarf => (NpcRole.Miner, NpcRole.Artisan),
                NpcRace.Goblin => (NpcRole.Merchant, NpcRole.Artisan),
                NpcRace.Orc => (NpcRole.Guard, NpcRole.Laborer),
                NpcRace.Avian => (NpcRole.Performer, NpcRole.Forager),
                NpcRace.Merfolk => (NpcRole.Fisher, NpcRole.Healer),
                NpcRace.Lizardfolk => (NpcRole.Forager, NpcRole.Guard),
                NpcRace.Dragonkin => (NpcRole.Guard, NpcRole.Leader),
                NpcRace.Zombie => (NpcRole.Laborer, NpcRole.None),
                NpcRace.Skeleton => (NpcRole.Guard, NpcRole.Laborer),
                NpcRace.Werewolf => (NpcRole.Forager, NpcRole.Guard),
                NpcRace.Vampire => (NpcRole.Leader, NpcRole.Scholar),
                NpcRace.Angel => (NpcRole.Cleric, NpcRole.Healer),
                NpcRace.Demon => (NpcRole.Merchant, NpcRole.Performer),
                _ => (NpcRole.None, NpcRole.None),
            };
            int total = 0;
            for (int i = 0; i < weights.Length; i++)
            {
                total += Weight(i);
            }

            int roll = (int)rng.NextUInt((uint)total);
            for (int i = 0; i < weights.Length; i++)
            {
                roll -= Weight(i);
                if (roll < 0)
                {
                    return (NpcRole)i;
                }
            }

            throw new InvalidOperationException("role weights must sum to more than zero");

            int Weight(int i) =>
                race != NpcRace.Human && i != 0 && (i == (int)bent.a || i == (int)bent.b) ? weights[i] * 2 : weights[i];
        }

        private static int[] RoleWeights(SettlementKind kind, string theme)
        {
            int row = kind == SettlementKind.Village ? 4 : (int)kind;
            var w = (int[])RoleWeight[row].Clone();
            void Favour(NpcRole role, int times) => w[(int)role] *= times;
            // The land decides the work: a harbour fishes, a canyon digs.
            switch (theme)
            {
                case "coast":
                    Favour(NpcRole.Fisher, 3);
                    break;
                case "riverbank":
                    Favour(NpcRole.Fisher, 2);
                    Favour(NpcRole.Farmer, 2);
                    break;
                case "canyon":
                case "crater":
                    Favour(NpcRole.Miner, 3);
                    break;
                case "forest":
                    Favour(NpcRole.Forager, 3);
                    break;
                case "highland":
                    Favour(NpcRole.Forager, 2);
                    Favour(NpcRole.Miner, 2);
                    break;
                case "ice_shelf":
                    Favour(NpcRole.Fisher, 2);
                    Favour(NpcRole.Forager, 2);
                    break;
                case "ruins":
                    Favour(NpcRole.Scholar, 3);
                    break;
            }

            return w;
        }

        private static int ThemeRace(string theme) => theme switch
        {
            "forest" => (int)NpcRace.Elf,
            "riverbank" => (int)NpcRace.Merfolk,
            "coast" => (int)NpcRace.Merfolk,
            "highland" => (int)NpcRace.Avian,
            "crater" => (int)NpcRace.Dragonkin,
            "ice_shelf" => (int)NpcRace.Werewolf,
            "canyon" => (int)NpcRace.Dwarf,
            "ruins" => (int)NpcRace.Skeleton,
            _ => -1,
        };

        // Frontier camps are young; long-settled farmland keeps its grandparents.
        private static int KindAge(SettlementKind kind) => kind switch
        {
            SettlementKind.Tribe => 360,
            SettlementKind.Village => 430,
            SettlementKind.Town => 400,
            SettlementKind.City => 370,
            _ => 390,
        };

        private static int KindHealth(SettlementKind kind) => kind switch
        {
            SettlementKind.Tribe => 760,
            SettlementKind.Village => 740,
            SettlementKind.Town => 780,
            SettlementKind.City => 800,
            _ => 830,
        };

        private static int ThemeHealth(string theme) => theme switch
        {
            "crater" => -60,
            "ruins" => -80,
            "ice_shelf" => -30,
            "forest" => 20,
            "coast" => 20,
            _ => 0,
        };

        private static int Household(BuildingKind kind, ref Pcg32 rng)
        {
            switch (kind)
            {
                case BuildingKind.Tent:
                    return 1 + (int)rng.NextUInt(2);
                case BuildingKind.House:
                    return 1 + (int)rng.NextUInt(3);
                case BuildingKind.Hall:
                    return 3 + (int)rng.NextUInt(4);
                case BuildingKind.Tower:
                    return 1 + (int)rng.NextUInt(2);
                default:
                    return 0;
            }
        }

        // Somewhere on the apron in front of the building's entrance, turned loosely toward the street.
        private static bool Stand(in PlanetConfig planet, in RunBuilding building, ulong seed, LayoutClearance clearance,
            Func<float3, bool> isStandable, List<float3> placed, out float3 at, out float3 facing)
        {
            var rng = new Pcg32(seed, 'P');
            float3 up = building.Direction;
            float3 across = math.normalize(math.cross(up, building.Facing));
            float gap = ResidentGapM / planet.Radius;
            for (int attempt = 0; attempt < StandAttempts; attempt++)
            {
                float forward = building.DepthM * 0.5f + BodyRadiusM + 0.6f + rng.NextFloat() * 3f;
                float lateral = (rng.NextFloat() - 0.5f) * (building.WidthM + 3f);
                float turn = (rng.NextFloat() - 0.5f) * 3.5f;
                float3 d = math.normalize(up + (building.Facing * forward + across * lateral) / planet.Radius);
                if ((isStandable != null && !isStandable(d)) || clearance.IsInsideBuilding(d, BodyRadiusM + 0.2f) ||
                    !clearance.IsContainedInSettlement(building.Settlement, d, BodyRadiusM))
                {
                    continue;
                }

                bool crowded = false;
                for (int i = 0; i < placed.Count && !crowded; i++)
                {
                    crowded = math.distancesq(placed[i], d) < gap * gap;
                }

                if (crowded)
                {
                    continue;
                }

                float3 look = building.Facing * math.cos(turn) + across * math.sin(turn);
                at = d;
                facing = math.normalize(look - d * math.dot(look, d));
                return true;
            }

            at = default;
            facing = default;
            return false;
        }
    }
}
