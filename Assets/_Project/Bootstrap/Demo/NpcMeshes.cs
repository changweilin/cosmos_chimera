using System;
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
        /// Pre: defined enums; textile01, age01 and health01 in [0, 1]; statureZ and bulkZ in [−3.5, 3.5]; borrowShare in [0, 1].
        /// Post: properties return the arguments. Side effects: none.
        /// </remarks>
        public NpcSpec(NpcRace race, NpcRole role, NpcSex sex, float age01, float health01, float statureZ, float bulkZ,
            ArchRegion region, ArchEra era, ArchRegion borrowRegion, ArchEra borrowEra, float borrowShare, ulong seed, float textile01 = 0.5f)
        {
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
    public static class NpcMeshes
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
        ///       about mix.TertiaryShare of a settlement borrows from the tertiary style, the rest from the secondary.
        ///       Same arguments, same spec. Side effects: none.
        /// </remarks>
        public static NpcSpec SpecFor(in RunResident resident, ArchRegion region, ArchEra era, in ArchMix mix)
        {
            var coin = new Pcg32(Salt ^ resident.Seed, 0x544552ul);
            bool third = coin.NextFloat() < mix.TertiaryShare;
            float share = mix.Weight > 0f ? math.saturate(mix.Weight + 0.18f * resident.StyleZMilli / 1000f) : 0f;
            return new NpcSpec(resident.Race, resident.Role, resident.Sex, resident.AgePermille / 1000f,
                resident.HealthPermille / 1000f, math.clamp(resident.StatureZMilli / 1000f, -3.5f, 3.5f),
                math.clamp(resident.BulkZMilli / 1000f, -3.5f, 3.5f), region, era,
                third ? mix.TertiaryRegion : mix.SecondaryRegion, third ? mix.TertiaryEra : mix.SecondaryEra, share, resident.Seed, resident.TextilePermille / 1000f);
        }

        /// <summary>Standing height of <paramref name="spec"/> to the top of the skull, metres.</summary>
        /// <remarks>Pre: none. Post: in [0.5, 2.7]; children are shorter, the old and the ill a little. Side effects: none.</remarks>
        public static float HeightM(in NpcSpec spec)
        {
            Traits t = TraitsFor(spec.Race);
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
        public static Mesh Build(in NpcSpec spec)
        {
            var rng = new Pcg32(Salt ^ spec.Seed, ((ulong)spec.Race << 16) | ((ulong)spec.Role << 8) | (ulong)spec.Sex);
            var body = new Fig();
            var glow = new Fig();
            Traits traits = TraitsFor(spec.Race);
            Look look = Look.Draw(spec, traits, ref rng);
            Rig rig = Rig.Pose(spec, traits, look, ref rng);
            Flesh(body, glow, spec, traits, rig, look);
            Head(body, glow, spec, traits, rig, look, ref rng);
            Dress(body, glow, spec, rig, look, ref rng);
            Trade(body, glow, spec, rig, look);
            Ailments(body, spec, rig, look, ref rng);

            float top = HeightM(spec) + 1.4f;
            Confine(body, top);
            Confine(glow, top);
            var data = new SceneryMeshes.MeshData(SubMeshCount);
            data.EmitFaceted(body.Part, BodySubMesh, FacetCos, null);
            data.EmitFaceted(glow.Part, GlowSubMesh, FacetCos, null);
            return data.ToMesh("Npc_" + spec.Race + "_" + spec.Role);
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

        private struct Traits
        {
            public float HeightM;   // adult male, crown of the skull
            public float Head;      // head size relative to a human's
            public float LegShare;  // legs as a share of everything below the neck
            public float Shoulder;  // half shoulder width / height
            public float Bulk;      // limb and trunk thickness
            public float Arm;       // arm length relative to a human's
            public float Hunch;     // forward lean of the spine, radians
            public bool Furred;     // no head hair, no bare-skin garments logic (fur, feathers, scales, bone)
        }

        private static Traits TraitsFor(NpcRace race) => race switch
        {
            NpcRace.Elf => new Traits { HeightM = 1.8f, Head = 0.96f, LegShare = 0.62f, Shoulder = 0.115f, Bulk = 0.86f, Arm = 1.02f },
            NpcRace.Dwarf => new Traits { HeightM = 1.28f, Head = 1.3f, LegShare = 0.5f, Shoulder = 0.175f, Bulk = 1.4f, Arm = 1f },
            NpcRace.Goblin => new Traits { HeightM = 1.12f, Head = 1.4f, LegShare = 0.52f, Shoulder = 0.13f, Bulk = 0.9f, Arm = 1.15f, Hunch = 0.12f },
            NpcRace.Orc => new Traits { HeightM = 1.98f, Head = 1.05f, LegShare = 0.56f, Shoulder = 0.165f, Bulk = 1.35f, Arm = 1.15f, Hunch = 0.14f },
            NpcRace.Avian => new Traits { HeightM = 1.66f, Head = 1f, LegShare = 0.6f, Shoulder = 0.115f, Bulk = 0.85f, Arm = 1f, Furred = true },
            NpcRace.Merfolk => new Traits { HeightM = 1.76f, Head = 1f, LegShare = 0.6f, Shoulder = 0.125f, Bulk = 0.95f, Arm = 1.03f, Furred = true },
            NpcRace.Lizardfolk => new Traits { HeightM = 1.82f, Head = 1f, LegShare = 0.57f, Shoulder = 0.125f, Bulk = 1f, Arm = 1.05f, Hunch = 0.2f, Furred = true },
            NpcRace.Dragonkin => new Traits { HeightM = 2.08f, Head = 1.02f, LegShare = 0.58f, Shoulder = 0.16f, Bulk = 1.3f, Arm = 1.05f, Hunch = 0.1f, Furred = true },
            NpcRace.Zombie => new Traits { HeightM = 1.68f, Head = 1f, LegShare = 0.6f, Shoulder = 0.12f, Bulk = 0.92f, Arm = 1f, Hunch = 0.3f },
            NpcRace.Skeleton => new Traits { HeightM = 1.7f, Head = 1f, LegShare = 0.6f, Shoulder = 0.12f, Bulk = 0.42f, Arm = 1f, Hunch = 0.05f, Furred = true },
            NpcRace.Werewolf => new Traits { HeightM = 1.96f, Head = 1.08f, LegShare = 0.56f, Shoulder = 0.16f, Bulk = 1.25f, Arm = 1.18f, Hunch = 0.24f, Furred = true },
            NpcRace.Vampire => new Traits { HeightM = 1.84f, Head = 0.97f, LegShare = 0.62f, Shoulder = 0.12f, Bulk = 0.88f, Arm = 1.02f },
            NpcRace.Angel => new Traits { HeightM = 1.86f, Head = 0.97f, LegShare = 0.62f, Shoulder = 0.125f, Bulk = 0.95f, Arm = 1f },
            NpcRace.Demon => new Traits { HeightM = 1.92f, Head = 1f, LegShare = 0.6f, Shoulder = 0.14f, Bulk = 1.1f, Arm = 1.05f },
            _ => new Traits { HeightM = 1.72f, Head = 1f, LegShare = 0.6f, Shoulder = 0.125f, Bulk = 1f, Arm = 1f },
        };

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

        private static readonly int[] RegionGlow = { 0xFFD27A, 0xFF8A3C, 0xFFE0A0, 0xBFE8FF, 0x9FF0E0, 0xFFC86A };

        // ---- one resident's draw -------------------------------------------------------------------------------

        private struct Look
        {
            public Color Skin, Hair, Cloth, Cloth2, Accent, Leather, Metal, Glow, Bone;
            public ArchEra UpperEra, LowerEra, HeadEra;
            public ArchRegion PieceRegion;
            public bool Skirt;       // a skirt or robe instead of trousers
            public bool Piece;       // wears the region's signature piece
            public bool Hat;         // wears the era's headwear
            public int HairStyle;    // 0 cropped, 1 long, 2 bun, 3 tail, 4 bald
            public bool Beard;
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
                l.UpperEra = rng.NextFloat() < spec.BorrowShare ? borrowEra : era;
                l.LowerEra = rng.NextFloat() < spec.BorrowShare ? borrowEra : era;
                l.HeadEra = rng.NextFloat() < spec.BorrowShare ? borrowEra : era;
                l.PieceRegion = rng.NextFloat() < spec.BorrowShare ? spec.BorrowRegion : spec.Region;
                ArchRegion palette = rng.NextFloat() < spec.BorrowShare * 0.7f ? spec.BorrowRegion : spec.Region;

                int[] tones = SkinTones[(int)spec.Race];
                Color skin = Hex(tones[rng.NextUInt((uint)tones.Length)]);
                // Illness drains colour before it bends the back.
                float pallor = (1f - spec.Health01) * 0.4f;
                l.Skin = Color.Lerp(skin, Hex(0xB8B4A8), pallor);
                l.Bone = Hex(0xE8E2D0);

                int hairPick = (int)rng.NextUInt((uint)HairTones.Length);
                Color hair = spec.Race switch
                {
                    NpcRace.Elf => rng.NextUInt(3) == 0 ? Hex(0xE8E4D8) : Hex(HairTones[hairPick]),
                    NpcRace.Angel => rng.NextUInt(2) == 0 ? Hex(0xF4EFE0) : Hex(0xE0C070),
                    NpcRace.Vampire => rng.NextUInt(3) == 0 ? Hex(0xE4E0E8) : Hex(0x1A1A20),
                    NpcRace.Demon => Hex(0x1A1A20),
                    NpcRace.Goblin => Hex(0x2A2018),
                    NpcRace.Zombie => Color.Lerp(Hex(HairTones[hairPick]), Hex(0x6A6A60), 0.5f),
                    _ => Hex(HairTones[hairPick]),
                };
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

                // Dye lots never match: a bounded-normal shift in value and warmth per person.
                float value = math.clamp(1f + 0.07f * Gauss(ref rng), 0.8f, 1.15f);
                float warm = math.clamp(0.03f * Gauss(ref rng), -0.07f, 0.07f);
                var tint = new Color(value * (1f + warm), value, value * (1f - warm), 1f);
                l.Cloth = cloth * tint;
                l.Cloth2 = cloth2 * tint;
                l.Accent = accent;
                l.Leather = Shade(Hex(0x5A3E2B), math.clamp(1f + 0.1f * Gauss(ref rng), 0.7f, 1.3f));
                l.Metal = l.UpperEra == ArchEra.Steampunk ? Hex(0xC8963C)
                    : l.UpperEra == ArchEra.Future ? Hex(0xB8C4D0)
                    : l.UpperEra == ArchEra.Wasteland ? Hex(0x7A5A44)
                    : Hex(0x8A8E96);
                l.Glow = l.UpperEra == ArchEra.Future ? Color.Lerp(Hex(0x7FF2FF), Hex(RegionGlow[(int)palette]), 0.35f)
                    : Hex(RegionGlow[(int)palette]);

                bool robed = spec.Role == NpcRole.Scholar || spec.Role == NpcRole.Cleric;
                float skirtOdds = spec.Sex == NpcSex.Female
                    ? l.LowerEra == ArchEra.Industrial ? 0.8f : l.LowerEra == ArchEra.Future ? 0.1f : 0.6f
                    : 0.06f;
                l.Skirt = rng.NextFloat() < skirtOdds || robed;
                l.Piece = rng.NextFloat() < 0.7f;
                l.Hat = rng.NextFloat() < 0.55f;
                l.HairStyle = (int)rng.NextUInt(spec.Sex == NpcSex.Female ? 4u : 5u);
                if (spec.Sex == NpcSex.Male && l.HairStyle == 1 && rng.NextUInt(2) == 0)
                {
                    l.HairStyle = 0;
                }

                l.Grown = math.lerp(0.52f, 1f, math.smoothstep(0.03f, 0.25f, spec.Age01));
                l.Child = spec.Age01 < 0.16f;
                float beardOdds = spec.Race == NpcRace.Dwarf ? 0.95f : spec.Race == NpcRace.Human ? 0.3f + 0.4f * grey : 0f;
                l.Beard = spec.Sex == NpcSex.Male && !l.Child && spec.Age01 > 0.25f && rng.NextFloat() < beardOdds;

                float old = math.smoothstep(0.62f, 0.92f, spec.Age01);
                float ill = 1f - math.smoothstep(0.2f, 0.65f, spec.Health01);
                l.Stoop = (ageless ? 0f : 0.2f * old) + 0.22f * ill;
                l.Cane = !l.Child && (spec.Health01 < 0.35f || (old > 0.5f && spec.Health01 < 0.6f));
                l.Bandaged = spec.Health01 < 0.45f && spec.Race != NpcRace.Skeleton;
                return l;
            }
        }

        private static ArchEra Worn(ArchEra era) => era == ArchEra.Ancient ? ArchEra.Primal : era;

        private enum ArmPose
        {
            Hang, Hold, Reach, Raise, Lean,
        }

        private struct Rig
        {
            public float H, HeadR, ArmR, LegR, TrunkX, TrunkZ, Bulk;
            public float3 Hip, Waist, Chest, Neck, Head, Fwd;
            public float3 ShoulderL, ShoulderR, ElbowL, ElbowR, HandL, HandR;
            public float3 HipL, HipR, KneeL, KneeR, AnkleL, AnkleR;
            public ArmPose Left, Right;

            public static Rig Pose(in NpcSpec spec, in Traits t, in Look look, ref Pcg32 rng)
            {
                var r = default(Rig);
                r.H = HeightM(spec);
                float head = t.Head * math.lerp(1.3f, 1f, math.smoothstep(0.52f, 1f, look.Grown));
                r.HeadR = 0.086f * r.H * head;
                float neck = 0.03f * r.H;
                float below = r.H - 2f * r.HeadR - neck;
                float leg = below * t.LegShare, torso = below - leg;
                bool female = spec.Sex == NpcSex.Female;
                r.Bulk = t.Bulk * math.clamp(1f + 0.1f * spec.BulkZ, 0.7f, 1.4f) * (look.Child ? 0.9f : 1f);
                float shoulder = t.Shoulder * r.H * (female ? 0.9f : 1f) * math.clamp(1f + 0.04f * spec.BulkZ, 0.85f, 1.15f);
                float hip = 0.08f * r.H * (female ? 1.08f : 1f) * math.sqrt(r.Bulk);
                r.ArmR = 0.027f * r.H * r.Bulk;
                r.LegR = 0.04f * r.H * r.Bulk;
                r.TrunkX = shoulder * 0.92f;
                r.TrunkZ = 0.07f * r.H * math.sqrt(r.Bulk) * (1f + math.max(0f, 0.12f * spec.BulkZ));

                float lean = math.min(t.Hunch + look.Stoop, 0.6f);
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
                b.Tube(hip, knee, r.LegR * 1.15f, r.LegR * 0.9f, skin, dark, 7);
                b.Tube(knee, ankle, r.LegR * 0.9f, r.LegR * 0.62f, dark, skin, 7);
                if (bones)
                {
                    b.Ball(knee, new float3(r.LegR * 1.5f), skin, 6, 4);
                }

                // Digitigrade races stand on a longer foot; everyone gets a wedge that reads as a shoe from above.
                float len = spec.Race == NpcRace.Werewolf || spec.Race == NpcRace.Avian || spec.Race == NpcRace.Lizardfolk ||
                            spec.Race == NpcRace.Dragonkin ? 2.6f : 2f;
                Color footTone = spec.Race == NpcRace.Avian ? Hex(0xE0A83A) : dark;
                b.Ball(new float3(ankle.x, foot * 0.55f, ankle.z + foot * (len - 1.2f) * 0.5f),
                    new float3(foot * 0.95f, foot * 0.6f, foot * len * 0.6f), footTone, 7, 4, -0.5f);

                float3 shoulder = s < 0 ? r.ShoulderL : r.ShoulderR, elbow = s < 0 ? r.ElbowL : r.ElbowR, hand = s < 0 ? r.HandL : r.HandR;
                b.Tube(shoulder, elbow, r.ArmR * 1.15f, r.ArmR * 0.9f, skin, dark, 6);
                b.Tube(elbow, hand, r.ArmR * 0.9f, r.ArmR * 0.7f, dark, skin, 6);
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
                float belly = 1f + math.max(0f, 0.1f * spec.BulkZ);
                b.Loft(new[]
                {
                    new Ring(r.Hip - new float3(0f, 0.03f * r.H, 0f), r.TrunkX * 0.82f, r.TrunkZ * 0.95f, dark),
                    new Ring(r.Waist, r.TrunkX * 0.8f * belly, r.TrunkZ * belly, skin),
                    new Ring(r.Chest - r.Fwd * (0.05f * r.H), r.TrunkX, r.TrunkZ * 1.08f, skin),
                    new Ring(r.Chest, r.TrunkX * 0.86f, r.TrunkZ * 0.9f, skin),
                }, 8, true, true);
            }

            b.Tube(r.Chest, r.Head, r.HeadR * 0.42f * math.sqrt(t.Bulk), r.HeadR * 0.38f * math.sqrt(t.Bulk), dark, skin, 6);
            Tail(b, spec, r, look);
            Wings(b, glow, spec, r, look);
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
                        float3 knee = s < 0 ? r.KneeL : r.KneeR, ankle = s < 0 ? r.AnkleL : r.AnkleR;
                        b.Panel(knee, ankle, (knee + ankle) * 0.5f + new float3(0f, -0.02f * r.H, -0.1f * r.H), Shade(look.Accent, 1.05f));
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
            var skull = new float3(0.92f, 1f, 0.97f) * hr;
            b.Ball(c, skull, skin, 9, 6);
            Color eye = Hex(0x20181A);
            bool lit = false;
            float eyeSize = 0.12f, eyeX = 0.4f, eyeY = 0.05f, eyeZ = 0.84f;
            float3 muzzle = c + new float3(0f, -0.2f * hr, 0.6f * hr);
            switch (spec.Race)
            {
                case NpcRace.Elf:
                    Ears(b, c, hr, skin, 0.75f, 0.5f, 0.25f);
                    break;
                case NpcRace.Dwarf:
                    b.Ball(c + new float3(0f, -0.12f * hr, 0.95f * hr), new float3(0.2f, 0.18f, 0.2f) * hr, Shade(skin, 0.94f), 6, 4);
                    Ears(b, c, hr, skin, 0.2f, 0.2f, 0.05f);
                    break;
                case NpcRace.Goblin:
                    Ears(b, c, hr, skin, 0.35f, 0.15f, 1.1f);
                    b.Tube(c + new float3(0f, -0.1f * hr, 0.85f * hr), c + new float3(0f, -0.22f * hr, 1.5f * hr), 0.15f * hr, 0.03f, skin,
                        Shade(skin, 0.9f), 5);
                    eye = Hex(0xF0D040);
                    eyeSize = 0.2f;
                    lit = true;
                    break;
                case NpcRace.Orc:
                    b.Ball(c + new float3(0f, -0.42f * hr, 0.3f * hr), new float3(0.8f, 0.5f, 0.78f) * hr, Shade(skin, 0.95f), 8, 5);
                    for (int s = -1; s <= 1; s += 2)
                    {
                        b.Tube(c + new float3(s * 0.32f * hr, -0.55f * hr, 0.82f * hr), c + new float3(s * 0.36f * hr, -0.2f * hr, 0.9f * hr),
                            0.08f * hr, 0.02f, look.Bone, look.Bone, 5);
                    }

                    Ears(b, c, hr, skin, 0.3f, 0.3f, 0.2f);
                    break;
                case NpcRace.Avian:
                    b.Tube(muzzle + new float3(0f, 0.1f * hr, 0.1f * hr), muzzle + new float3(0f, -0.1f * hr, 0.95f * hr), 0.3f * hr, 0.02f,
                        Hex(0xE8B03A), Hex(0xC8882A), 5);
                    for (int i = 0; i < 3; i++)
                    {
                        float3 root = c + new float3(0f, 0.9f * hr, (0.2f - 0.35f * i) * hr);
                        b.Panel(root + new float3(0f, 0f, 0.2f * hr), root + new float3(0f, 0f, -0.2f * hr),
                            root + new float3(0f, (0.7f - 0.1f * i) * hr, -(0.4f + 0.2f * i) * hr), Shade(look.Accent, 1f - 0.08f * i));
                    }

                    eyeX = 0.6f;
                    eyeZ = 0.62f;
                    eyeSize = 0.15f;
                    break;
                case NpcRace.Merfolk:
                    b.Panel(c + new float3(0f, 0.9f * hr, 0.5f * hr), c + new float3(0f, 0.6f * hr, -0.9f * hr),
                        c + new float3(0f, 1.6f * hr, -0.45f * hr), Shade(look.Accent, 1.05f));
                    for (int s = -1; s <= 1; s += 2)
                    {
                        float3 ear = c + new float3(s * 0.88f * hr, 0f, 0f);
                        b.Panel(ear + new float3(0f, 0.25f * hr, 0.1f * hr), ear + new float3(0f, -0.3f * hr, 0.1f * hr),
                            ear + new float3(s * 0.7f * hr, 0.25f * hr, -0.4f * hr), Shade(look.Accent, 0.95f));
                    }

                    eyeSize = 0.16f;
                    break;
                case NpcRace.Lizardfolk:
                case NpcRace.Dragonkin:
                {
                    bool dragon = spec.Race == NpcRace.Dragonkin;
                    b.Tube(muzzle, muzzle + new float3(0f, -0.08f * hr, (dragon ? 1.1f : 0.9f) * hr), 0.55f * hr, 0.3f * hr, skin,
                        Shade(skin, 0.92f), 7);
                    b.Ball(muzzle + new float3(0f, -0.3f * hr, 0.35f * hr), new float3(0.42f, 0.2f, 0.6f) * hr, Shade(skin, 1.12f), 6, 4);
                    if (dragon)
                    {
                        for (int s = -1; s <= 1; s += 2)
                        {
                            float3 root = c + new float3(s * 0.5f * hr, 0.7f * hr, -0.2f * hr);
                            float3 mid = root + new float3(s * 0.2f * hr, 0.5f * hr, -0.5f * hr);
                            b.Tube(root, mid, 0.17f * hr, 0.11f * hr, look.Bone, look.Bone, 5);
                            b.Tube(mid, mid + new float3(s * 0.05f * hr, 0.5f * hr, -0.25f * hr), 0.11f * hr, 0.02f, look.Bone, look.Bone, 5);
                        }
                    }

                    for (int i = 0; i < 4; i++)
                    {
                        float3 root = c + new float3(0f, (0.95f - 0.25f * i) * hr, -(0.2f + 0.25f * i) * hr);
                        b.Panel(root + new float3(0f, 0f, 0.14f * hr), root + new float3(0f, -0.1f * hr, -0.14f * hr),
                            root + new float3(0f, 0.4f * hr, -0.3f * hr), Shade(look.Accent, 0.95f));
                    }

                    eye = Hex(0xF0C030);
                    lit = true;
                    eyeX = 0.55f;
                    eyeZ = 0.7f;
                    eyeY = 0.2f;
                    break;
                }
                case NpcRace.Zombie:
                    eye = Hex(0xD8E890);
                    lit = true;
                    b.Ball(c + new float3(0.45f * hr, 0.55f * hr, 0.4f * hr), new float3(0.3f, 0.25f, 0.3f) * hr, look.Bone, 5, 3);
                    break;
                case NpcRace.Skeleton:
                    b.Ball(c + new float3(0f, -0.55f * hr, 0.25f * hr), new float3(0.55f, 0.35f, 0.6f) * hr, Shade(skin, 0.92f), 7, 4);
                    for (int s = -1; s <= 1; s += 2)
                    {
                        b.Ball(c + new float3(s * 0.38f * hr, 0.05f * hr, 0.8f * hr), new float3(0.24f, 0.26f, 0.16f) * hr, Hex(0x1A1618), 6, 4);
                    }

                    b.Ball(c + new float3(0f, -0.25f * hr, 0.9f * hr), new float3(0.1f, 0.14f, 0.08f) * hr, Hex(0x1A1618), 5, 3);
                    eye = Color.Lerp(look.Glow, Hex(0x9FF0FF), 0.5f);
                    lit = true;
                    eyeSize = 0.09f;
                    eyeZ = 0.86f;
                    break;
                case NpcRace.Werewolf:
                    b.Tube(muzzle, muzzle + new float3(0f, -0.12f * hr, 0.95f * hr), 0.5f * hr, 0.26f * hr, skin, Shade(skin, 1.1f), 7);
                    b.Ball(muzzle + new float3(0f, -0.06f * hr, 1f * hr), new float3(0.16f) * hr, Hex(0x1A1618), 5, 3);
                    for (int s = -1; s <= 1; s += 2)
                    {
                        float3 root = c + new float3(s * 0.5f * hr, 0.75f * hr, -0.1f * hr);
                        b.Tube(root, root + new float3(s * 0.15f * hr, 0.7f * hr, -0.05f * hr), 0.28f * hr, 0.02f, skin, Shade(skin, 0.8f), 4);
                    }

                    // The ruff: what turns a tall dog-headed man into a wolf from across a square.
                    b.Ball(r.Neck + new float3(0f, -0.01f * r.H, -0.01f * r.H), new float3(r.TrunkX * 1.02f, 0.07f * r.H, r.TrunkZ * 1.5f),
                        Shade(skin, 1.12f), 8, 4);
                    eye = Hex(0xF0C030);
                    lit = true;
                    eyeY = 0.2f;
                    break;
                case NpcRace.Vampire:
                    Ears(b, c, hr, skin, 0.4f, 0.4f, 0.1f);
                    eye = Hex(0xE83030);
                    lit = true;
                    for (int s = -1; s <= 1; s += 2)
                    {
                        b.Tube(c + new float3(s * 0.14f * hr, -0.42f * hr, 0.86f * hr), c + new float3(s * 0.14f * hr, -0.58f * hr, 0.86f * hr),
                            0.035f * hr, 0.005f, Color.white, Color.white, 4);
                    }

                    break;
                case NpcRace.Demon:
                    for (int s = -1; s <= 1; s += 2)
                    {
                        float3 root = c + new float3(s * 0.55f * hr, 0.65f * hr, 0.2f * hr);
                        float3 mid = root + new float3(s * 0.45f * hr, 0.35f * hr, 0f);
                        b.Tube(root, mid, 0.2f * hr, 0.14f * hr, Hex(0x2A2226), Hex(0x3A3034), 5);
                        b.Tube(mid, mid + new float3(-s * 0.1f * hr, 0.6f * hr, 0.15f * hr), 0.14f * hr, 0.02f, Hex(0x3A3034), Hex(0x5A4A48), 5);
                    }

                    Ears(b, c, hr, skin, 0.45f, 0.3f, 0.35f);
                    eye = Hex(0xFFD040);
                    lit = true;
                    break;
                default:
                    Ears(b, c, hr, skin, 0.18f, 0.2f, 0.05f);
                    break;
            }

            bool plainFace = spec.Race == NpcRace.Human || spec.Race == NpcRace.Elf || spec.Race == NpcRace.Orc ||
                             spec.Race == NpcRace.Zombie || spec.Race == NpcRace.Vampire || spec.Race == NpcRace.Angel ||
                             spec.Race == NpcRace.Demon;
            if (plainFace)
            {
                b.Ball(c + new float3(0f, -0.14f * hr, 0.93f * hr), new float3(0.11f, 0.14f, 0.13f) * hr, Shade(skin, 0.93f), 5, 3);
                Color lip = Color.Lerp(skin, Hex(0x653F46), 0.56f);
                float3 mouth = c + new float3(0f, -0.42f, 0.88f) * hr;
                b.Tube(mouth - new float3(0.19f, -0.025f, 0.05f) * hr, mouth, hr * 0.024f, hr * 0.04f, lip, lip, 5);
                b.Tube(mouth, mouth + new float3(0.19f, 0.025f, -0.05f) * hr, hr * 0.04f, hr * 0.024f, lip, lip, 5);
                // Brows: the cheapest feature that gives a face a direction to look in.
                for (int s = -1; s <= 1; s += 2)
                {
                    b.Slab(c + new float3(s * 0.22f, eyeY + 0.26f, eyeZ + 0.06f) * hr, c + new float3(s * 0.58f, eyeY + 0.22f, eyeZ - 0.04f) * hr,
                        0.07f * hr, 0.05f * hr, Shade(look.Hair, 0.8f));
                }
            }

            if (look.Child)
            {
                eyeSize *= 1.25f;
            }

            for (int s = -1; s <= 1; s += 2)
            {
                float3 socket = c + new float3(s * eyeX, eyeY, eyeZ) * hr;
                if (plainFace && !lit)
                {
                    b.Ball(socket, new float3(0.22f, 0.14f, 0.11f) * hr, Hex(0xEEE2CF), 8, 4);
                    socket.z += 0.095f * hr;
                }

                (lit ? glow : b).Ball(socket, new float3(eyeSize, eyeSize * 1.15f, eyeSize * 0.6f) * hr, eye, 6, 4);
            }

            if (!t.Furred)
            {
                Hair(b, spec, r, look, ref rng);
            }

            if (look.Beard)
            {
                float len = spec.Race == NpcRace.Dwarf ? 1.9f : 0.8f;
                b.Loft(new[]
                {
                    new Ring(c + new float3(0f, -0.25f * hr, 0.45f * hr), 0.72f * hr, 0.55f * hr, look.Hair),
                    new Ring(c + new float3(0f, -0.75f * hr, 0.55f * hr), 0.6f * hr, 0.45f * hr, Shade(look.Hair, 0.93f)),
                    new Ring(c + new float3(0f, -(0.75f + len) * hr, 0.6f * hr), 0.12f * hr, 0.1f * hr, Shade(look.Hair, 0.85f)),
                }, 7, false, true);
            }
        }

        // `up` and `back` sweep the tip; `out` splays it: an elf's ear points back and up, a goblin's straight out.
        private static void Ears(Fig b, float3 c, float hr, Color skin, float up, float back, float outward)
        {
            for (int s = -1; s <= 1; s += 2)
            {
                float3 root = c + new float3(s * 0.86f * hr, 0f, -0.05f * hr);
                b.Panel(root + new float3(0f, 0.18f * hr, 0.1f * hr), root + new float3(0f, -0.2f * hr, 0.05f * hr),
                    root + new float3(s * (0.15f + outward) * hr, up * hr, -back * hr), Shade(skin, 0.93f));
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
            // A cap cut at the brow and pushed back, so the face stays open.
            b.Ball(c + new float3(0f, 0.1f * hr, -0.12f * hr), new float3(1f, 1.02f, 1.03f) * hr, hair, 9, 6, 0.05f);
            b.Ball(c + new float3(0f, 0.55f * hr, 0.55f * hr), new float3(0.8f, 0.42f, 0.5f) * hr, hair, 7, 4);
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
            }
        }

        // ---- clothes -------------------------------------------------------------------------------------------

        private static void Dress(Fig b, Fig glow, in NpcSpec spec, in Rig r, in Look look, ref Pcg32 rng)
        {
            float h = r.H;
            Color cloth = look.Cloth, cloth2 = look.Cloth2;
            bool robed = spec.Role == NpcRole.Scholar || spec.Role == NpcRole.Cleric;
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
            if (robed)
            {
                hem = 0.9f;
                sleeve = 2;
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
                if (!bareLegs && !look.Skirt)
                {
                    float loose = low == ArchEra.Mercantile || low == ArchEra.Future ? 1.08f : 1.3f;
                    b.Tube(hip, knee, r.LegR * 1.15f * loose, r.LegR * 0.9f * loose, legCloth, Shade(legCloth, 0.92f), 7);
                    b.Tube(knee, math.lerp(knee, ankle, 0.9f), r.LegR * 0.9f * loose, r.LegR * 0.7f * loose, Shade(legCloth, 0.92f),
                        Shade(legCloth, 0.85f), 7);
                }

                if (boot > 0f)
                {
                    float3 top = math.lerp(ankle, knee, boot * 1.6f);
                    b.Tube(top, ankle - new float3(0f, 0.02f * h, 0f), r.LegR * 1.02f, r.LegR * 0.8f, bootTone, Shade(bootTone, 0.85f), 7);
                    float foot = r.LegR * 1.3f;
                    b.Ball(new float3(ankle.x, foot * 0.55f, ankle.z + foot * 0.45f), new float3(foot, foot * 0.62f, foot * 1.3f),
                        Shade(bootTone, 0.9f), 7, 4, -0.5f);
                    if (low == ArchEra.Future)
                    {
                        glow.Hoop(math.lerp(ankle, knee, 0.5f), r.LegR * 1.05f, 0.008f * h, look.Glow);
                    }
                }
            }

            if (look.Skirt)
            {
                float fall = robed ? 0.93f : low == ArchEra.Steampunk || low == ArchEra.Primal ? 0.5f : 0.9f;
                float flare = low == ArchEra.Industrial ? 2f : low == ArchEra.Future ? 1.2f : 1.6f;
                Color skirt = robed ? cloth : cloth2;
                float skirtX = math.abs(r.HipR.x) + r.LegR * 1.5f, skirtZ = math.max(r.TrunkZ * 1.1f, r.LegR * 1.6f);
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
            float thighX = math.abs(r.HipR.x) + r.LegR * 1.75f, thighZ = r.LegR * 1.9f;
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
                new Ring(r.Waist, r.TrunkX * 0.84f * fit * (1f + math.max(0f, 0.1f * spec.BulkZ)), r.TrunkZ * fit * (1f + math.max(0f, 0.1f * spec.BulkZ)), cloth),
                new Ring(r.Chest - r.Fwd * (0.05f * h), r.TrunkX * fit, r.TrunkZ * 1.1f * fit, cloth),
                new Ring(r.Chest + new float3(0f, 0.005f * h, 0f), r.TrunkX * 0.8f * fit, r.TrunkZ * 0.85f * fit, Shade(cloth, 1.04f)),
            }, 16, true, false, math.lerp(0.025f, 0.085f, spec.Textile01));
            // Belt: the one line every era agrees on, and where pouches and lanterns hang.
            Color belt = look.UpperEra == ArchEra.Future ? look.Glow : look.Leather;
            (look.UpperEra == ArchEra.Future ? glow : b).Hoop(math.lerp(r.Hip, r.Waist, 0.6f), r.TrunkX * 0.9f * fit, 0.012f * h, belt,
                r.TrunkZ * 1.08f * fit);

            for (int s = -1; s <= 1; s += 2)
            {
                float3 shoulder = s < 0 ? r.ShoulderL : r.ShoulderR, elbow = s < 0 ? r.ElbowL : r.ElbowR, hand = s < 0 ? r.HandL : r.HandR;
                int cover = look.UpperEra == ArchEra.Wasteland && s > 0 ? 2 : sleeve;
                if (cover >= 1)
                {
                    b.Tube(shoulder, cover == 1 ? math.lerp(shoulder, elbow, 0.75f) : elbow, r.ArmR * 1.5f, r.ArmR * 1.25f, cloth,
                        Shade(cloth, 0.92f), 6);
                    b.Ball(shoulder, new float3(r.ArmR * (look.UpperEra == ArchEra.Mercantile ? 2.2f : 1.6f)), cloth, 6, 4);
                }

                if (cover >= 2)
                {
                    float cuff = look.Piece && look.PieceRegion == ArchRegion.Twilight ? 2.6f : robed ? 1.9f : 1.15f;
                    b.Tube(elbow, math.lerp(elbow, hand, 0.88f), r.ArmR * 1.25f, r.ArmR * cuff, Shade(cloth, 0.92f), Shade(cloth, 0.85f), 6);
                    if (look.UpperEra == ArchEra.Future)
                    {
                        glow.Hoop(math.lerp(elbow, hand, 0.8f), r.ArmR * 1.2f, 0.007f * h, look.Glow);
                    }
                }
            }

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
                        b.Ball(s < 0 ? r.HandL : r.HandR, new float3(r.ArmR * 1.6f), Shade(look.Leather, 1.25f), 6, 4);
                    }

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
                    Brim(b, crown + new float3(0f, 0.15f * hr, 0f), hr * 1.9f, hr * 0.8f, hr * 0.6f, Hex(0xD8BC6A), Hex(0xB89A4A));
                    Pole(b, hand, h * 1.02f, wood);
                    b.Slab(new float3(hand.x, h * 1.02f, hand.z), new float3(hand.x, h * 1.02f, hand.z + 0.16f * h), 0.07f * h, 0.012f * h, look.Metal);
                    break;
                case NpcRole.Forager:
                {
                    // Bow slung across the back, quiver over the shoulder.
                    float3 top = backAt + new float3(-0.12f * h, 0.26f * h, 0f), bottom = backAt + new float3(0.14f * h, -0.3f * h, 0f);
                    float3 belly = (top + bottom) * 0.5f + new float3(0.05f * h, 0.02f * h, -0.08f * h);
                    b.Tube(top, belly, 0.008f * h, 0.012f * h, wood, wood, 4);
                    b.Tube(belly, bottom, 0.012f * h, 0.008f * h, wood, wood, 4);
                    b.Tube(top, bottom, 0.003f * h, 0.003f * h, look.Bone, look.Bone, 3);
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
                    Pole(b, hand, h * 0.62f, wood);
                    b.Tube(new float3(hand.x - 0.16f * h, h * 0.57f, hand.z), new float3(hand.x, h * 0.63f, hand.z), 0.004f * h, 0.018f * h,
                        look.Metal, look.Metal, 4);
                    b.Tube(new float3(hand.x, h * 0.63f, hand.z), new float3(hand.x + 0.16f * h, h * 0.57f, hand.z), 0.018f * h, 0.004f * h,
                        look.Metal, look.Metal, 4);
                    break;
                case NpcRole.Artisan:
                    // Apron, and a hammer held where the work would be.
                    b.Slab(r.Chest + new float3(0f, -0.06f * h, r.TrunkZ * 1.35f), r.Hip + new float3(0f, -0.22f * h, r.TrunkZ * 1.5f),
                        r.TrunkX * 1.35f, 0.008f * h, look.Leather);
                    b.Tube(hand - new float3(0f, 0.03f * h, 0f), hand + new float3(0f, 0.16f * h, 0.02f * h), 0.009f * h, 0.009f * h, wood, wood, 4);
                    b.Slab(hand + new float3(-0.045f * h, 0.16f * h, 0.02f * h), hand + new float3(0.045f * h, 0.16f * h, 0.02f * h), 0.035f * h,
                        0.035f * h, look.Metal);
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
                    Pole(b, hand, reach, late ? look.Metal : wood);
                    float3 head = new float3(hand.x, reach, hand.z);
                    (late ? glow : b).Tube(head, head + new float3(0f, 0.16f * h, 0f), 0.022f * h, 0.002f, late ? look.Glow : look.Metal,
                        late ? look.Glow : look.Metal, 4);
                    // Shield on the forearm, face outward.
                    float3 boss = (r.ElbowL + r.HandL) * 0.5f + new float3(-0.035f * h, 0f, 0.03f * h);
                    b.Loft(new[]
                    {
                        new Ring(boss - new float3(0f, 0.16f * h, 0f), 0.02f * h, 0.07f * h, Shade(look.Cloth, 0.8f)),
                        new Ring(boss - new float3(0f, 0.04f * h, 0f), 0.028f * h, 0.14f * h, look.Cloth),
                        new Ring(boss + new float3(0f, 0.12f * h, 0f), 0.028f * h, 0.15f * h, look.Cloth),
                        new Ring(boss + new float3(0f, 0.17f * h, 0f), 0.02f * h, 0.11f * h, look.Accent),
                    }, 8, true, true);
                    b.Ball(boss + new float3(-0.03f * h, 0.03f * h, 0f), new float3(0.03f * h), look.Metal, 6, 4);
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
                    Pole(b, hand, reach, late ? look.Metal : wood);
                    glow.Ball(new float3(hand.x, reach + 0.05f * h, hand.z), new float3(0.045f * h), look.Glow, 7, 5);
                    b.Hoop(new float3(hand.x, reach + 0.05f * h, hand.z), 0.07f * h, 0.008f * h, look.Metal);
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
                    Brim(b, crown + new float3(0f, 0.2f * hr, 0f), hr * 1.35f, hr * 0.8f, hr * 0.7f, look.Accent, look.Cloth2);
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
                    Pole(b, hand, math.min(h * 0.95f, h + 0.3f), late ? look.Metal : Hex(0x3A2A20));
                    b.Ball(new float3(hand.x, math.min(h * 0.95f, h + 0.3f), hand.z), new float3(0.03f * h), Hex(0xE0B840), 6, 4);
                    break;
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
                            b.Tube(math.lerp(r.KneeL, r.AnkleL, 0.1f), math.lerp(r.KneeL, r.AnkleL, 0.6f), r.LegR * 1.45f, r.LegR * 1.3f, linen,
                                Shade(linen, 0.9f), 7);
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

        /// <summary>One sub-mesh under construction; every face is wound by the direction it must look.</summary>
        private sealed class Fig
        {
            public readonly SceneryMeshes.Part Part = new SceneryMeshes.Part();

            private int V(float3 p, Color c)
            {
                c.a = 1f;
                return Part.Add(p, c, float2.zero);
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
