using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Cosmimera.Core.Contracts;
using Cosmimera.Core.Random;
using Cosmimera.WorldGen;
using Unity.Mathematics;
using UnityEngine;

namespace Cosmimera.Bootstrap.Demo
{
    /// <summary>Which UMA race a resident is assembled on.</summary>
    /// <remarks>REQ: docs/design/npc-uma-looks.md#種族覆蓋. Pre: none. Post: closed set. Side effects: none.</remarks>
    internal enum NpcUmaBase
    {
        Humanoid,
        Skeleton,
    }

    /// <summary>How a painted iris is drawn (Cosmimera/NpcEye _PupilType).</summary>
    /// <remarks>REQ: docs/design/npc-uma-looks.md#眼睛. Pre: none. Post: closed set; values match the shader. Side effects: none.</remarks>
    internal enum NpcEyeType
    {
        Round,
        Slit,

        /// <summary>No pupil: the whole iris is a light.</summary>
        Glow,

        /// <summary>Nearly all pupil, the iris a thin rim (birds, fish).</summary>
        Bead,

        /// <summary>A dark ring around a bright core.</summary>
        Ring,
    }

    /// <summary>One resident's eyes: type, per-eye iris colour, sclera, sizes and glow.</summary>
    /// <remarks>
    /// REQ: docs/design/npc-uma-looks.md#眼睛. Pre: built by NpcMeshes.EyesOf. Post: immutable; IrisScale in [0.6, 1],
    /// PupilScale in [0.7, 1.3], Glow in [0, 1]. Side effects: none.
    /// </remarks>
    internal readonly struct NpcEyes
    {
        public NpcEyes(NpcEyeType type, Color left, Color right, Color sclera, float irisScale, float pupilScale, float glow)
        {
            Type = type;
            Left = left;
            Right = right;
            Sclera = sclera;
            IrisScale = irisScale;
            PupilScale = pupilScale;
            Glow = glow;
        }

        public NpcEyeType Type { get; }

        /// <summary>Iris colour of the left eye.</summary>
        public Color Left { get; }

        /// <summary>Iris colour of the right eye; differs from Left only for odd eyes.</summary>
        public Color Right { get; }

        public Color Sclera { get; }

        /// <summary>Iris radius as a share of the painted iris disc.</summary>
        public float IrisScale { get; }

        public float PupilScale { get; }

        public float Glow { get; }
    }

    /// <summary>One resident's look as UMA would need it: DNA values, wardrobe slots and colours.</summary>
    /// <remarks>
    /// REQ: docs/design/npc-uma-looks.md, ADR-0015 decision 1 — the description is plain data so the gates can run in
    /// EditMode before any UMA race exists; slot ids name the parts the race must provide.
    /// Pre: built by <see cref="NpcMeshes.UmaRecipeOf"/>. Post: immutable; Dna is indexed like
    /// <see cref="NpcMeshes.UmaChannels"/>. Side effects: none.
    /// </remarks>
    internal sealed class NpcUmaRecipe
    {
        /// <summary>Wraps an assembled recipe; slot ids are null where the resident wears nothing in that place.</summary>
        /// <remarks>
        /// Pre: dna.Length == UmaChannels.Count; colours.Length == ColourNames.Length.
        /// Post: the properties return the arguments unchanged. Side effects: keeps the arrays (callers must not edit them).
        /// </remarks>
        public NpcUmaRecipe(NpcUmaBase umaBase, NpcSex sex, string headSlot, float[] dna, string hair, string beard,
            string upper, string lower, string headwear, string piece, bool cane, bool bandaged, Color[] colours, NpcEyes eyes)
        {
            Contract.Requires(dna != null && dna.Length == NpcMeshes.UmaChannels.Count, "dna must cover every channel");
            Contract.Requires(colours != null && colours.Length == ColourNames.Length, "colours must cover every slot colour");
            Base = umaBase;
            Sex = sex;
            HeadSlot = headSlot;
            _dna = dna;
            Hair = hair;
            Beard = beard;
            Upper = upper;
            Lower = lower;
            Headwear = headwear;
            Piece = piece;
            Cane = cane;
            Bandaged = bandaged;
            _colours = colours;
            Eyes = eyes;
        }

        /// <summary>Eye type, colours and sizes, passed to the eye shader.</summary>
        /// <remarks>Pre: none. Post: as constructed. Side effects: none.</remarks>
        public NpcEyes Eyes { get; }

        /// <summary>Names of <see cref="Colour"/> indices, in order.</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public static readonly string[] ColourNames = { "skin", "hair", "cloth", "cloth2", "accent", "leather", "metal", "glow", "bone" };

        private readonly float[] _dna;
        private readonly Color[] _colours;

        /// <summary>UMA race to assemble on.</summary>
        /// <remarks>Pre: none. Post: as constructed. Side effects: none.</remarks>
        public NpcUmaBase Base { get; }

        /// <summary>Sex of the body mesh.</summary>
        /// <remarks>Pre: none. Post: as constructed. Side effects: none.</remarks>
        public NpcSex Sex { get; }

        /// <summary>Head-replacing slot of a beast race; null keeps the humanoid head.</summary>
        /// <remarks>Pre: none. Post: as constructed. Side effects: none.</remarks>
        public string HeadSlot { get; }

        /// <summary>Hair slot; null when bald or furred.</summary>
        /// <remarks>Pre: none. Post: as constructed. Side effects: none.</remarks>
        public string Hair { get; }

        /// <summary>Beard slot; null when beardless.</summary>
        /// <remarks>Pre: none. Post: as constructed. Side effects: none.</remarks>
        public string Beard { get; }

        /// <summary>Top slot, named by cut and era.</summary>
        /// <remarks>Pre: none. Post: as constructed, never null. Side effects: none.</remarks>
        public string Upper { get; }

        /// <summary>Skirt or legwear slot, named by cut (skirts) and era.</summary>
        /// <remarks>Pre: none. Post: as constructed, never null. Side effects: none.</remarks>
        public string Lower { get; }

        /// <summary>Era headwear slot; null when bareheaded.</summary>
        /// <remarks>Pre: none. Post: as constructed. Side effects: none.</remarks>
        public string Headwear { get; }

        /// <summary>Region signature-piece slot; null when not worn.</summary>
        /// <remarks>Pre: none. Post: as constructed. Side effects: none.</remarks>
        public string Piece { get; }

        /// <summary>Carries a cane (procedural, held in the hand).</summary>
        /// <remarks>Pre: none. Post: as constructed. Side effects: none.</remarks>
        public bool Cane { get; }

        /// <summary>Wears bandages (decal or small slot).</summary>
        /// <remarks>Pre: none. Post: as constructed. Side effects: none.</remarks>
        public bool Bandaged { get; }

        /// <summary>Value of DNA channel <paramref name="i"/> in the channel's own unit.</summary>
        /// <remarks>Pre: 0 ≤ i &lt; UmaChannels.Count. Post: finite. Side effects: none.</remarks>
        public float Dna(int i)
        {
            Contract.Requires(i >= 0 && i < _dna.Length, "channel index out of range");
            return _dna[i];
        }

        /// <summary>Colour <paramref name="i"/> (see <see cref="ColourNames"/>), passed to the shader rather than baked.</summary>
        /// <remarks>Pre: 0 ≤ i &lt; ColourNames.Length. Post: as drawn. Side effects: none.</remarks>
        public Color Colour(int i)
        {
            Contract.Requires(i >= 0 && i < _colours.Length, "colour index out of range");
            return _colours[i];
        }

        /// <summary>Canonical text of the recipe at save precision (DNA in permille of its range, colours in 8 bits).</summary>
        /// <remarks>
        /// REQ: characters.md#捏臉範圍 — looks are stored as integer permille sliders, so two recipes equal at that
        /// precision are the same person on screen.
        /// Pre: none. Post: equal recipes give equal keys. Side effects: allocates a string.
        /// </remarks>
        public string Key()
        {
            var sb = new StringBuilder(512);
            sb.Append(Base).Append('|').Append(Sex).Append('|').Append(HeadSlot).Append('|').Append(Hair).Append('|')
                .Append(Beard).Append('|').Append(Upper).Append('|').Append(Lower).Append('|').Append(Headwear).Append('|')
                .Append(Piece).Append('|').Append(Cane ? 1 : 0).Append(Bandaged ? 1 : 0);
            for (int i = 0; i < _dna.Length; i++)
            {
                sb.Append(',').Append(NpcMeshes.UmaChannels[i].Permille(_dna[i]));
            }

            foreach (Color c in new[] { Eyes.Left, Eyes.Right, Eyes.Sclera }.Concat(_colours))
            {
                Color32 c32 = c;
                sb.Append('#').Append(c32.r).Append('.').Append(c32.g).Append('.').Append(c32.b);
            }

            sb.Append('|').Append(Eyes.Type).Append(',').Append((int)math.round(1000f * Eyes.IrisScale)).Append(',')
                .Append((int)math.round(1000f * Eyes.PupilScale)).Append(',').Append((int)math.round(1000f * Eyes.Glow));

            return sb.ToString();
        }
    }

    public static partial class NpcMeshes
    {
        /// <summary>Everything Build draws before it starts meshing, replayed in Build's own order.</summary>
        /// <remarks>Pre: built by <see cref="UmaSampleOf"/>. Post: plain copy. Side effects: none.</remarks>
        internal struct UmaSample
        {
            public NpcSpec Spec;
            public Traits Traits;
            public Look Look;
            public Rig Rig;
            public Face Face;
            public float Ear;
        }

        /// <summary>One DNA channel the UMA race must provide, with the range its converter has to cover.</summary>
        /// <remarks>
        /// REQ: docs/design/npc-uma-looks.md#原則 — Min/Max are the contract for the DNA converter: every value the
        /// sampling can produce lies inside, so nothing is clamped away.
        /// Pre: Min &lt; Max. Post: immutable. Side effects: none.
        /// </remarks>
        internal sealed class UmaChannel
        {
            internal UmaChannel(string name, string source, float min, float max, Func<UmaSample, float> read)
            {
                Name = name;
                Source = source;
                Min = min;
                Max = max;
                Read = read;
            }

            /// <summary>DNA name the converter binds to.</summary>
            /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
            public string Name { get; }

            /// <summary>Sample fields the channel carries, as "Struct.Field" separated by commas.</summary>
            /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
            public string Source { get; }

            /// <summary>Lowest value the converter must accept.</summary>
            /// <remarks>Pre: none. Post: constant, &lt; Max. Side effects: none.</remarks>
            public float Min { get; }

            /// <summary>Highest value the converter must accept.</summary>
            /// <remarks>Pre: none. Post: constant, &gt; Min. Side effects: none.</remarks>
            public float Max { get; }

            /// <summary>Reads the channel's value from a sample.</summary>
            /// <remarks>Pre: sample non-default. Post: pure function of the sample. Side effects: none.</remarks>
            public Func<UmaSample, float> Read { get; }

            /// <summary>The value as integer permille of the channel's range, as a slider would store it.</summary>
            /// <remarks>Pre: finite value. Post: in [0, 1000] when the value lies in [Min, Max]. Side effects: none.</remarks>
            public int Permille(float value) => (int)math.round(1000f * (value - Min) / (Max - Min));

            /// <summary>The value as a UMA DNA value: 0 at Min, 1 at Max.</summary>
            /// <remarks>Pre: finite value. Post: in [0, 1] when the value lies in [Min, Max]. Side effects: none.</remarks>
            public float Dna01(float value) => (value - Min) / (Max - Min);
        }

        private static float Over(float a, float b) => b > 0f ? a / b : 0f;

        // Body channels read the posed rig rather than the traits: the rig already folds race, blood, sex, age, muscle and
        // fat into lengths and radii, which is what UMA bones and blendshapes have to reproduce. Ratios to height keep the
        // channels independent of overall scale.
        // Ranges: where the sampling clamps (shares, fat, muscle, eye opening, limb-node factors, lean, height) the
        // clamp bounds themselves; elsewhere the span of 20 000 random residents (UmaRecipeGateTests.ProbeChannelSpans)
        // widened by a quarter on each side, floored at 0 for sizes and thicknesses. The gate test re-checks them on
        // fresh seeds.
        private static readonly UmaChannel[] Channels =
        {
            new UmaChannel("body.height", "Rig.H,Traits.HeightM", 0.5f, 2.7f, s => s.Rig.H),
            new UmaChannel("body.headRatio", "Rig.HeadR,Traits.Head,Look.Grown", 0.05315f, 0.189f, s => Over(s.Rig.HeadR, s.Rig.H)),
            new UmaChannel("body.legShare", "Rig.Hip,Traits.LegShare", 0.2638f, 0.5838f, s => Over(s.Rig.Hip.y, s.Rig.H)),
            new UmaChannel("body.shoulder", "Rig.ShoulderR,Traits.Shoulder", 0.03697f, 0.2635f, s => Over(s.Rig.ShoulderR.x, s.Rig.H)),
            new UmaChannel("body.trunkWidth", "Rig.TrunkX", 0.03401f, 0.2424f, s => Over(s.Rig.TrunkX, s.Rig.H)),
            new UmaChannel("body.trunkDepth", "Rig.TrunkZ,Traits.Bulk", 0f, 0.2118f, s => Over(s.Rig.TrunkZ, s.Rig.H)),
            new UmaChannel("body.hipWidth", "Rig.HipR", 0.0148f, 0.1692f, s => Over(s.Rig.HipR.x, s.Rig.H)),
            new UmaChannel("body.belly", "Rig.Belly,Look.Fat", 0.4443f, 1.859f, s => s.Rig.Belly),
            new UmaChannel("body.bulk", "Rig.Bulk", 0f, 2.287f, s => s.Rig.Bulk),
            new UmaChannel("body.neck", "Rig.NeckR", 0.1277f, 0.7497f, s => Over(s.Rig.NeckR, s.Rig.HeadR)),
            new UmaChannel("body.armLength", "Traits.Arm", 1f, 1.18f, s => s.Traits.Arm),
            new UmaChannel("body.armRadius", "Rig.ArmR", 0f, 0.08755f, s => Over(s.Rig.ArmR, s.Rig.H)),
            new UmaChannel("body.legRadius", "Rig.LegR", 0f, 0.125f, s => Over(s.Rig.LegR, s.Rig.H)),
            new UmaChannel("limb.thigh", "Rig.ThighR", 1.012f, 1.438f, s => Over(s.Rig.ThighR, s.Rig.LegR)),
            new UmaChannel("limb.thighMid", "Rig.ThighMidR", 0.968f, 1.375f, s => Over(s.Rig.ThighMidR, s.Rig.LegR)),
            new UmaChannel("limb.knee", "Rig.KneeRad", 0.774f, 0.9288f, s => Over(s.Rig.KneeRad, s.Rig.LegR)),
            new UmaChannel("limb.calf", "Rig.CalfR", 0.8096f, 1.058f, s => Over(s.Rig.CalfR, s.Rig.LegR)),
            new UmaChannel("limb.ankle", "Rig.AnkleRad", 0.528f, 0.648f, s => Over(s.Rig.AnkleRad, s.Rig.LegR)),
            new UmaChannel("limb.upperArm", "Rig.UpperR", 1.012f, 1.403f, s => Over(s.Rig.UpperR, s.Rig.ArmR)),
            new UmaChannel("limb.upperArmMid", "Rig.UpperMidR", 0.968f, 1.342f, s => Over(s.Rig.UpperMidR, s.Rig.ArmR)),
            new UmaChannel("limb.elbow", "Rig.ElbowRad", 0.774f, 0.9288f, s => Over(s.Rig.ElbowRad, s.Rig.ArmR)),
            new UmaChannel("limb.forearm", "Rig.ForeR", 0.836f, 1.093f, s => Over(s.Rig.ForeR, s.Rig.ArmR)),
            new UmaChannel("limb.wrist", "Rig.WristR", 0.5984f, 0.7344f, s => Over(s.Rig.WristR, s.Rig.ArmR)),
            new UmaChannel("spine.lean", "Rig.Fwd,Traits.Hunch,Look.Stoop", 0f, 0.6f, s => math.asin(math.clamp(s.Rig.Fwd.z, -1f, 1f))),
            new UmaChannel("body.muscle", "Look.Muscle", -3f, 3f, s => s.Look.Muscle),
            new UmaChannel("body.fat", "Look.Fat,Traits.Fat", 0.03f, 0.55f, s => s.Look.Fat),
            new UmaChannel("body.grown", "Look.Grown,Look.Child", 0.52f, 1f, s => s.Look.Grown),

            new UmaChannel("face.skullWidth", "Face.SkullW", 0.6519f, 1.355f, s => s.Face.SkullW),
            new UmaChannel("face.skullHeight", "Face.SkullH", 0.7954f, 1.274f, s => s.Face.SkullH),
            new UmaChannel("face.skullDepth", "Face.SkullD", 0.809f, 1.184f, s => s.Face.SkullD),
            new UmaChannel("face.cheekboneWidth", "Face.CheekW", 0.4647f, 1.577f, s => s.Face.CheekW),
            new UmaChannel("face.cheekFat", "Face.Cheek", 0f, 0.45f, s => s.Face.Cheek),
            new UmaChannel("face.length", "Face.FaceLen", 0.3419f, 1.623f, s => s.Face.FaceLen),
            new UmaChannel("face.jawWidth", "Face.JawW", 0.02898f, 2.054f, s => s.Face.JawW),
            new UmaChannel("face.jawAngle", "Face.JawAngle", 0f, 1f, s => s.Face.JawAngle),
            new UmaChannel("face.chinWidth", "Face.ChinW", -0.3668f, 2.491f, s => s.Face.ChinW),
            new UmaChannel("face.chinJut", "Face.ChinJut", -0.3348f, 0.3247f, s => s.Face.ChinJut),
            new UmaChannel("face.chinHeight", "Face.ChinH", 0.2023f, 1.851f, s => s.Face.ChinH),
            new UmaChannel("face.jowl", "Face.Jowl", 0f, 1f, s => s.Face.Jowl),
            new UmaChannel("mouth.width", "Face.LipW", 0.5144f, 1.481f, s => s.Face.LipW),
            new UmaChannel("mouth.upperLip", "Face.LipUp", 0f, 2.398f, s => s.Face.LipUp),
            new UmaChannel("mouth.lowerLip", "Face.LipLow", 0f, 2.616f, s => s.Face.LipLow),
            new UmaChannel("mouth.height", "Face.MouthY", -0.1439f, 0.1447f, s => s.Face.MouthY),
            new UmaChannel("mouth.corners", "Face.Curve", -0.1981f, 0.168f, s => s.Face.Curve),
            new UmaChannel("nose.length", "Face.NoseLen", 0.01289f, 1.867f, s => s.Face.NoseLen),
            new UmaChannel("nose.width", "Face.NoseW", 0.0325f, 2.17f, s => s.Face.NoseW),
            new UmaChannel("nose.bridge", "Face.Bridge", -1.157f, 0.8518f, s => s.Face.Bridge),
            new UmaChannel("nose.tip", "Face.Tip", -0.08f, 0.1f, s => s.Face.Tip),
            new UmaChannel("eye.size", "Face.EyeSize", 0.4654f, 1.542f, s => s.Face.EyeSize),
            new UmaChannel("eye.spread", "Face.EyeSpread", 0.721f, 1.295f, s => s.Face.EyeSpread),
            new UmaChannel("eye.tilt", "Face.EyeTilt", -0.07f, 0.09f, s => s.Face.EyeTilt),
            new UmaChannel("eye.open", "Face.EyeOpen", 0.55f, 1.4f, s => s.Face.EyeOpen),
            new UmaChannel("brow.thickness", "Face.BrowThick", 0f, 3.266f, s => s.Face.BrowThick),
            new UmaChannel("brow.arch", "Face.BrowArch", -0.2036f, 0.2373f, s => s.Face.BrowArch),
            new UmaChannel("brow.slant", "Face.BrowSlant", -0.2308f, 0.264f, s => s.Face.BrowSlant),
            new UmaChannel("brow.length", "Face.BrowLen", 0.5208f, 1.473f, s => s.Face.BrowLen),
            new UmaChannel("brow.gap", "Face.BrowGap", -0.1866f, 0.196f, s => s.Face.BrowGap),
            new UmaChannel("ear.scale", "Ear", 0.75f, 1.512f, s => s.Ear),
            // REQ: npc-uma-looks.md 第 16 項 — blood-line ear traits grow with the share, so they are weights, not slots.
            new UmaChannel("ear.elfShare", "Spec.Heritage", 0f, 1f, s => s.Spec.Heritage.ShareOf(NpcRace.Elf, s.Spec.Race)),
            new UmaChannel("ear.goblinShare", "Spec.Heritage", 0f, 1f, s => s.Spec.Heritage.ShareOf(NpcRace.Goblin, s.Spec.Race)),
            new UmaChannel("ear.wolfShare", "Spec.Heritage", 0f, 1f, s => s.Spec.Heritage.ShareOf(NpcRace.Werewolf, s.Spec.Race)),
            new UmaChannel("ear.finShare", "Spec.Heritage", 0f, 1f, s => s.Spec.Heritage.ShareOf(NpcRace.Merfolk, s.Spec.Race)),

            new UmaChannel("beard.length", "Look.BeardLength", 0f, 2.4f, s => s.Look.Beard ? s.Look.BeardLength : 0f),
            new UmaChannel("cloth.fall", "Look.Fall", 0.3f, 0.93f, s => s.Look.Fall),
            new UmaChannel("cloth.textile", "Spec.Textile01", 0f, 1f, s => s.Spec.Textile01),
        };

        /// <summary>Every DNA channel the UMA race must provide, in recipe order.</summary>
        /// <remarks>REQ: docs/design/npc-uma-looks.md#多樣性對照. Pre: none. Post: constant, names unique. Side effects: none.</remarks>
        internal static IReadOnlyList<UmaChannel> UmaChannels => Channels;

        private static readonly string[] HairSlots =
        {
            "hair_cropped", "hair_long", "hair_bun", "hair_tail", null, "hair_braids", "hair_crest", "hair_topknot", "hair_volume",
        };

        private static readonly string[] BeardSlots =
        {
            "beard_full", "beard_trimmed", "beard_goatee", "beard_forked", "beard_braided", "beard_moustache",
        };

        /// <summary>Replays Build's draws for <paramref name="spec"/> up to the head, without meshing.</summary>
        /// <remarks>
        /// Pre: none. Post: the same traits, look, rig, face and ear scale Build uses for this spec.
        /// Side effects: none.
        /// </remarks>
        internal static UmaSample UmaSampleOf(in NpcSpec spec) => UmaSampleOf(spec, TraitsOf(spec), null);

        /// <summary>As <see cref="UmaSampleOf(in NpcSpec)"/> but with given traits and, optionally, a given look.</summary>
        /// <remarks>
        /// Pre: none. Post: the rig and face are re-derived from what was given, through Build's own random streams.
        /// Side effects: none.
        /// </remarks>
        internal static UmaSample UmaSampleOf(in NpcSpec spec, in Traits traits, Look? look)
        {
            // Same streams and order as Build: look and rig on the main stream, face and ear on the head stream.
            var rng = new Pcg32(Salt ^ spec.Seed, ((ulong)spec.Race << 16) | ((ulong)spec.Role << 8) | (ulong)spec.Sex);
            Look drawn = Look.Draw(spec, traits, ref rng);
            Look used = look ?? drawn;
            Rig rig = Rig.Pose(spec, traits, used, ref rng);
            var hv = new Pcg32(Salt ^ spec.Seed, 0x48454144ul);
            bool ageless = spec.Race == NpcRace.Vampire || spec.Race == NpcRace.Angel || spec.Race == NpcRace.Demon;
            Face face = Face.Draw(spec, used, ageless, ref hv);
            float ear = math.clamp(1f + 0.15f * Gauss(ref hv), 0.75f, 1.35f) * (ageless ? 1f : 1f + 0.12f * math.smoothstep(0.6f, 0.95f, spec.Age01));
            return new UmaSample { Spec = spec, Traits = traits, Look = used, Rig = rig, Face = face, Ear = ear };
        }

        /// <summary>The UMA recipe of <paramref name="spec"/>.</summary>
        /// <remarks>
        /// REQ: ADR-0015 decision 1. Pre: none. Post: same spec, same recipe. Side effects: allocates.
        /// </remarks>
        internal static NpcUmaRecipe UmaRecipeOf(in NpcSpec spec) => UmaRecipeOf(UmaSampleOf(spec));

        /// <summary>The UMA recipe of an already drawn sample.</summary>
        /// <remarks>Pre: sample from <see cref="UmaSampleOf(in NpcSpec)"/> or a field-wise edit of one. Post: deterministic. Side effects: allocates.</remarks>
        internal static NpcUmaRecipe UmaRecipeOf(in UmaSample s)
        {
            var dna = new float[Channels.Length];
            for (int i = 0; i < Channels.Length; i++)
            {
                dna[i] = Channels[i].Read(s);
            }

            Look l = s.Look;
            NpcRace race = s.Spec.Race;
            // Fur, feathers, scales and bone grow no head hair (Traits.Furred), as in the procedural head.
            string hair = s.Traits.Furred ? null : HairSlots[l.HairStyle];
            string beard = l.Beard ? BeardSlots[l.BeardStyle] : null;
            string upper = "top_" + l.Cut + "_" + l.UpperEra;
            // Robes, dresses and wraps hang from the waist as one piece with the top; the rest wear legwear of their own era.
            string lower = l.Skirt ? "skirt_" + l.Cut + "_" + l.LowerEra : "legs_" + l.LowerEra;
            string headwear = l.Hat ? "hat_" + l.HeadEra : null;
            string piece = l.Piece ? "piece_" + l.PieceRegion : null;
            var colours = new[] { l.Skin, l.Hair, l.Cloth, l.Cloth2, l.Accent, l.Leather, l.Metal, l.Glow, l.Bone };
            return new NpcUmaRecipe(race == NpcRace.Skeleton ? NpcUmaBase.Skeleton : NpcUmaBase.Humanoid, s.Spec.Sex,
                HeadSlotOf(race), dna, hair, beard, upper, lower, headwear, piece, l.Cane, l.Bandaged, colours, EyesOf(s.Spec, l));
        }

        // REQ: npc-uma-looks.md#眼睛 — each race leans to its own pupil types; who gets which is drawn per resident.
        // Weights per type in NpcEyeType order (Round, Slit, Glow, Bead, Ring).
        private static int[] EyeTypeOdds(NpcRace race) => race switch
        {
            NpcRace.Elf => new[] { 85, 0, 0, 0, 15 },
            NpcRace.Orc => new[] { 70, 30, 0, 0, 0 },
            NpcRace.Goblin => new[] { 30, 50, 20, 0, 0 },
            NpcRace.Vampire => new[] { 30, 40, 30, 0, 0 },
            NpcRace.Demon => new[] { 20, 45, 35, 0, 0 },
            NpcRace.Angel => new[] { 40, 0, 0, 0, 60 },
            NpcRace.Zombie => new[] { 40, 0, 60, 0, 0 },
            NpcRace.Skeleton => new[] { 0, 0, 100, 0, 0 },
            NpcRace.Werewolf => new[] { 60, 0, 40, 0, 0 },
            NpcRace.Lizardfolk => new[] { 0, 85, 15, 0, 0 },
            NpcRace.Dragonkin => new[] { 0, 85, 15, 0, 0 },
            NpcRace.Avian => new[] { 20, 0, 0, 80, 0 },
            NpcRace.Merfolk => new[] { 40, 0, 0, 60, 0 },
            _ => new[] { 100, 0, 0, 0, 0 },
        };

        // Race mean of the iris share of the painted disc: goblins and birds are all eye, orcs and the dead squint.
        private static float IrisMean(NpcRace race) => race switch
        {
            NpcRace.Elf => 0.9f,
            NpcRace.Dwarf => 0.84f,
            NpcRace.Orc => 0.78f,
            NpcRace.Goblin => 0.98f,
            NpcRace.Angel => 0.94f,
            NpcRace.Demon => 0.9f,
            NpcRace.Zombie => 0.74f,
            NpcRace.Skeleton => 0.8f,
            NpcRace.Lizardfolk => 0.95f,
            NpcRace.Dragonkin => 0.95f,
            NpcRace.Avian => 1f,
            NpcRace.Merfolk => 0.96f,
            _ => 0.86f,
        };

        // Races whose eyes the procedural head draws self-lit keep a glow on every pupil type.
        private static bool LitEyes(NpcRace race) => race == NpcRace.Goblin || race == NpcRace.Werewolf || race == NpcRace.Vampire ||
                                                    race == NpcRace.Demon || race == NpcRace.Lizardfolk || race == NpcRace.Dragonkin ||
                                                    race == NpcRace.Zombie || race == NpcRace.Skeleton;

        private static readonly int[] ZombieEyes = { 0xD8E890, 0xC8C8B0, 0xA8B8C8 };
        // The procedural bird head draws one dark eye; birds' own irises run from black through amber to raptor yellow.
        private static readonly int[] AvianEyes = { 0x20181A, 0x3A2A1A, 0x8A5A1A, 0xE8B03A, 0xC04020 };
        private static readonly int[] EyeSclera = { 0xEEE2CF, 0x1A1418, 0xF0E0A0, 0xC8C8A8, 0xF4D8D8 };

        /// <summary>The eyes of <paramref name="spec"/>.</summary>
        /// <remarks>
        /// REQ: docs/design/npc-uma-looks.md#眼睛. Own stream, so adding eyes reshuffled no earlier draw.
        /// Pre: look drawn for spec. Post: deterministic; type from EyeTypeOdds(race); iris from the race palette (a blood
        /// line's palette with probability equal to its share), shifted ±0.03 hue and ±12 % value; odd eyes for one mixed
        /// resident in ten; sizes and glow within the NpcEyes ranges. Side effects: none.
        /// </remarks>
        internal static NpcEyes EyesOf(in NpcSpec spec, in Look look)
        {
            var rng = new Pcg32(Salt ^ spec.Seed, 0x455945ul);
            NpcRace race = spec.Race;
            int[] odds = EyeTypeOdds(race);
            int roll = (int)rng.NextUInt(100u), type = 0;
            while (roll >= odds[type])
            {
                roll -= odds[type];
                type++;
            }

            int[] palette = race == NpcRace.Zombie ? ZombieEyes : race == NpcRace.Skeleton ? BoneGlow : race == NpcRace.Avian ? AvianEyes : EyePalette(race);
            NpcHeritage h = spec.Heritage;
            for (int i = 0; i < h.BloodLines; i++)
            {
                int[] blood = EyePalette(h.BloodRace(i));
                if (blood != null && rng.NextFloat() < h.BloodShare(i))
                {
                    palette = blood;
                }
            }

            Color left = Jitter(Pick(palette, ref rng), ref rng);
            Color right = left;
            if (h.BloodLines > 0 && rng.NextUInt(10) == 0)
            {
                int[] odd = EyePalette(h.BloodRace((int)rng.NextUInt((uint)h.BloodLines)));
                right = odd != null ? Jitter(Pick(odd, ref rng), ref rng) : right;
            }

            int sclera = race switch
            {
                NpcRace.Demon => rng.NextUInt(2) == 0 ? 1 : 0,
                NpcRace.Goblin => 2,
                NpcRace.Zombie => 3,
                NpcRace.Vampire => 4,
                _ => 0,
            };
            float iris = math.clamp(IrisMean(race) * (1f + 0.05f * Gauss(ref rng)) + (look.Child ? 0.05f : 0f), 0.6f, 1f);
            float pupil = math.clamp(1f + 0.1f * Gauss(ref rng), 0.7f, 1.3f);
            float glow = (NpcEyeType)type == NpcEyeType.Glow ? math.lerp(0.6f, 1f, rng.NextFloat()) : LitEyes(race) ? 0.3f : 0f;
            return new NpcEyes((NpcEyeType)type, left, right, Hex(EyeSclera[sclera]), iris, pupil, glow);
        }

        private static Color Jitter(Color c, ref Pcg32 rng) =>
            Retint(c, 0.03f * (2f * rng.NextFloat() - 1f), 1f, 0.12f * (2f * rng.NextFloat() - 1f));

        // REQ: npc-uma-looks.md#種族覆蓋 — beasts whose whole face is reshaped get a head of their own.
        private static string HeadSlotOf(NpcRace race) => race switch
        {
            NpcRace.Avian => "head_avian",
            NpcRace.Werewolf => "head_werewolf",
            NpcRace.Lizardfolk => "head_lizardfolk",
            NpcRace.Dragonkin => "head_dragonkin",
            NpcRace.Merfolk => "head_merfolk",
            _ => null,
        };
    }
}
