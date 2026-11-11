using System;
using Cosmimera.Core.Contracts;
using Cosmimera.GameData;

namespace Cosmimera.Combat
{
    /// <summary>How a substance reacts to a fire hit. Mirrors env-materials.schema.json <c>flammable</c>.</summary>
    public enum Flammability
    {
        None = 0,
        /// <summary>Burns at <see cref="EnvMaterials.BurnSlowPermillePerS"/>.</summary>
        Slow = 1,
        /// <summary>Burns at <see cref="EnvMaterials.BurnFastPermillePerS"/>.</summary>
        Fast = 2,
        /// <summary>Destroys itself the moment it ignites (kerosene).</summary>
        Blast = 3,
    }

    /// <summary>When a substance counts as brittle. Mirrors env-materials.schema.json <c>brittle</c>.</summary>
    public enum Brittleness
    {
        Never = 0,
        /// <summary>Only while frozen. Any frozen object is brittle anyway; this documents the design table.</summary>
        Frozen = 1,
        Always = 2,
    }

    /// <summary>One substance row of Data/env-materials. REQ: docs/design/destructibles.md#物質.</summary>
    /// <remarks>Pre: built by the importer or tests from validated data. Post: immutable. Side effects: none.</remarks>
    public sealed class Substance
    {
        private readonly int[] _resist;

        /// <summary>Creates a substance row.</summary>
        /// <remarks>
        /// Pre: id non-empty; element != Shadow; 0 &lt;= opacityPermille &lt;= 1000; densityKgM3 &gt;= 1; hpPerM3 &gt;= 1;
        ///      resist has exactly 10 entries indexed by <see cref="Element"/>.
        /// Post: properties echo the arguments; resist is copied.
        /// Side effects: allocates.
        /// </remarks>
        public Substance(string id, Element element, Flammability flammable, bool conductive, Brittleness brittle,
            int opacityPermille, int densityKgM3, int hpPerM3, int[] resist)
        {
            Contract.Requires(!string.IsNullOrEmpty(id), "id must be non-empty");
            Contract.Requires(element != Element.Shadow, "shadow comes only from the night rule, never a substance");
            Contract.Requires(opacityPermille >= 0 && opacityPermille <= 1000, "opacityPermille must be in 0..1000");
            Contract.Requires(densityKgM3 >= 1 && hpPerM3 >= 1, "density and hp per m3 must be >= 1");
            Contract.Requires(resist != null && resist.Length == 10, "resist must have one entry per element");
            Id = id;
            Element = element;
            Flammable = flammable;
            Conductive = conductive;
            Brittle = brittle;
            OpacityPermille = opacityPermille;
            DensityKgM3 = densityKgM3;
            HpPerM3 = hpPerM3;
            _resist = (int[])resist.Clone();
        }

        /// <summary>Data id.</summary>
        /// <remarks>Pre: none. Post: non-empty. Side effects: none.</remarks>
        public string Id { get; }

        /// <summary>Element of the material this substance drops.</summary>
        /// <remarks>Pre: none. Post: never Shadow. Side effects: none.</remarks>
        public Element Element { get; }

        /// <summary>Fire reaction class.</summary>
        /// <remarks>Pre: none. Post: stable. Side effects: none.</remarks>
        public Flammability Flammable { get; }

        /// <summary>Whether thunder hits get <see cref="EnvMaterials.ConductPercent"/>.</summary>
        /// <remarks>Pre: none. Post: stable. Side effects: none.</remarks>
        public bool Conductive { get; }

        /// <summary>Brittleness class.</summary>
        /// <remarks>Pre: none. Post: stable. Side effects: none.</remarks>
        public Brittleness Brittle { get; }

        /// <summary>Opacity used by the night shadow roll.</summary>
        /// <remarks>Pre: none. Post: 0..1000. Side effects: none.</remarks>
        public int OpacityPermille { get; }

        /// <summary>Density; mass = density × volume (presentation rigidbodies).</summary>
        /// <remarks>Pre: none. Post: &gt;= 1. Side effects: none.</remarks>
        public int DensityKgM3 { get; }

        /// <summary>HP per cubic metre; object HP = this × volume.</summary>
        /// <remarks>Pre: none. Post: &gt;= 1. Side effects: none.</remarks>
        public int HpPerM3 { get; }

        /// <summary>Resist percent against <paramref name="element"/> (DamageModel clamps it).</summary>
        /// <remarks>Pre: element is a defined Element. Post: the data value. Side effects: none.</remarks>
        public int Resist(Element element) => _resist[(int)element];
    }

    /// <summary>
    /// Substances an object is made of, with roll weights. A scatter object has one entry; a building several.
    /// REQ: docs/design/destructibles.md#建築的組成.
    /// </summary>
    /// <remarks>
    /// Reactions and resist of a composite use its <see cref="Dominant"/> substance (highest weight, first on ties);
    /// the design table gives buildings composition only, and a per-substance damage split would need per-part HP
    /// the design does not have. Opacity is the weight-averaged value, as the shadow rule specifies.
    /// </remarks>
    public sealed class Composition
    {
        private readonly Substance[] _parts;
        private readonly int[] _weights;

        /// <summary>Creates a composition.</summary>
        /// <remarks>
        /// Pre: parts and weights non-null, same non-zero length; every part non-null; every weight &gt;= 1.
        /// Post: <see cref="Dominant"/>, <see cref="OpacityPermille"/> and <see cref="TotalWeight"/> are fixed.
        /// Side effects: allocates (copies both arrays).
        /// </remarks>
        public Composition(Substance[] parts, int[] weights)
        {
            Contract.Requires(parts != null && weights != null && parts.Length > 0 && parts.Length == weights.Length,
                "parts and weights must be non-empty and the same length");
            _parts = (Substance[])parts.Clone();
            _weights = (int[])weights.Clone();
            int best = 0;
            long total = 0, opacity = 0;
            for (int i = 0; i < _parts.Length; i++)
            {
                Contract.Requires(_parts[i] != null && _weights[i] >= 1, "every part non-null with weight >= 1");
                total += _weights[i];
                opacity += (long)_weights[i] * _parts[i].OpacityPermille;
                if (_weights[i] > _weights[best])
                {
                    best = i;
                }
            }

            Contract.Requires(total <= int.MaxValue, "total weight must fit Int32");
            Dominant = _parts[best];
            TotalWeight = (int)total;
            OpacityPermille = (int)(opacity / total);
        }

        /// <summary>Single-substance composition.</summary>
        /// <remarks>Pre: substance non-null. Post: Count == 1, Dominant == substance. Side effects: allocates.</remarks>
        public static Composition Of(Substance substance) => new Composition(new[] { substance }, new[] { 1 });

        /// <summary>Number of parts.</summary>
        /// <remarks>Pre: none. Post: &gt;= 1. Side effects: none.</remarks>
        public int Count => _parts.Length;

        /// <summary>Part <paramref name="i"/>.</summary>
        /// <remarks>Pre: 0 &lt;= i &lt; Count. Post: non-null. Side effects: none.</remarks>
        public Substance Part(int i) => _parts[i];

        /// <summary>Weight of part <paramref name="i"/>.</summary>
        /// <remarks>Pre: 0 &lt;= i &lt; Count. Post: &gt;= 1. Side effects: none.</remarks>
        public int Weight(int i) => _weights[i];

        /// <summary>Sum of weights.</summary>
        /// <remarks>Pre: none. Post: &gt;= Count. Side effects: none.</remarks>
        public int TotalWeight { get; }

        /// <summary>Substance that decides reactions and resist.</summary>
        /// <remarks>Pre: none. Post: one of the parts. Side effects: none.</remarks>
        public Substance Dominant { get; }

        /// <summary>Weight-averaged opacity.</summary>
        /// <remarks>Pre: none. Post: 0..1000. Side effects: none.</remarks>
        public int OpacityPermille { get; }
    }

    /// <summary><c>clamp(base + perM3 × volume, 0, cap)</c> in per-mille. REQ: destructibles.md#掉落機率.</summary>
    /// <remarks>Pre: none. Post: immutable value. Side effects: none.</remarks>
    public readonly struct VolumeChance
    {
        /// <summary>Creates the curve.</summary>
        /// <remarks>Pre: all three in 0..1000. Post: fields echo arguments. Side effects: none.</remarks>
        public VolumeChance(int basePermille, int perM3Permille, int capPermille)
        {
            Contract.Requires(basePermille >= 0 && basePermille <= 1000 && perM3Permille >= 0 && perM3Permille <= 1000
                              && capPermille >= 0 && capPermille <= 1000, "chance terms must be in 0..1000");
            BasePermille = basePermille;
            PerM3Permille = perM3Permille;
            CapPermille = capPermille;
        }

        /// <summary>Chance at zero volume.</summary>
        /// <remarks>Pre: none. Post: 0..1000. Side effects: none.</remarks>
        public int BasePermille { get; }

        /// <summary>Added chance per cubic metre.</summary>
        /// <remarks>Pre: none. Post: 0..1000. Side effects: none.</remarks>
        public int PerM3Permille { get; }

        /// <summary>Upper bound.</summary>
        /// <remarks>Pre: none. Post: 0..1000. Side effects: none.</remarks>
        public int CapPermille { get; }

        /// <summary>Chance for an object of <paramref name="volumeL"/> litres.</summary>
        /// <remarks>Pre: volumeL &gt;= 0. Post: 0 &lt;= result &lt;= CapPermille; non-decreasing in volume. Side effects: none.</remarks>
        public int ChancePermille(int volumeL)
        {
            Contract.Requires(volumeL >= 0, "volumeL must be >= 0");
            long c = BasePermille + (long)PerM3Permille * volumeL / 1000;
            return c > CapPermille ? CapPermille : (int)c;
        }
    }

    /// <summary>
    /// Everything Data/env-materials holds: substances, reaction tuning, drop curves, building compositions and the
    /// representative substance of each ground material. REQ: docs/design/destructibles.md; schema
    /// Data/schemas/env-materials.schema.json.
    /// </summary>
    /// <remarks>Pre: built from validated data. Post: immutable. Side effects: none.</remarks>
    public sealed class EnvMaterials
    {
        private readonly Composition[] _buildings;
        private readonly Substance[] _ground;

        /// <summary>Creates the table.</summary>
        /// <remarks>
        /// Pre: burn rates 1..1000; melt/shatter/conduct/gust percent 100..1000; freezeMs &gt;= 1;
        ///      shadow terms 0..1000; buildings has one non-null entry per <see cref="BuildingMaterial"/> value;
        ///      ground has 10 entries indexed by Element: non-null for Metal..Thunder (each of that element), null for
        ///      Light and Shadow (they float and cannot be destroyed); gem non-null.
        /// Post: properties echo the arguments.
        /// Side effects: allocates (copies arrays).
        /// </remarks>
        public EnvMaterials(int burnSlowPermillePerS, int burnFastPermillePerS, int meltPercent, int freezeMs,
            int shatterPercent, int conductPercent, int gustImpulsePercent, VolumeChance material, VolumeChance gem,
            int shadowPerM2Permille, int shadowCapPermille, Composition[] buildings, Substance[] ground, Substance gemSubstance)
        {
            Contract.Requires(burnSlowPermillePerS >= 1 && burnSlowPermillePerS <= 1000
                              && burnFastPermillePerS >= 1 && burnFastPermillePerS <= 1000, "burn rates must be in 1..1000");
            Contract.Requires(InBoost(meltPercent) && InBoost(shatterPercent) && InBoost(conductPercent) && InBoost(gustImpulsePercent),
                "reaction percents must be in 100..1000");
            Contract.Requires(freezeMs >= 1, "freezeMs must be >= 1");
            Contract.Requires(shadowPerM2Permille >= 0 && shadowPerM2Permille <= 1000 && shadowCapPermille >= 0 && shadowCapPermille <= 1000,
                "shadow terms must be in 0..1000");
            Contract.Requires(buildings != null && buildings.Length == BuildingMaterialCount && Array.IndexOf(buildings, null) < 0,
                "buildings must have one composition per BuildingMaterial");
            Contract.Requires(ground != null && ground.Length == 10, "ground must be indexed by Element");
            for (int e = 0; e < 10; e++)
            {
                bool floats = e == (int)Element.Light || e == (int)Element.Shadow;
                Contract.Requires(floats ? ground[e] == null : ground[e] != null && (int)ground[e].Element == e,
                    "ground[e] must be a substance of element e, and null for light/shadow");
            }

            Contract.Requires(gemSubstance != null, "gemSubstance must be non-null");
            BurnSlowPermillePerS = burnSlowPermillePerS;
            BurnFastPermillePerS = burnFastPermillePerS;
            MeltPercent = meltPercent;
            FreezeMs = freezeMs;
            ShatterPercent = shatterPercent;
            ConductPercent = conductPercent;
            GustImpulsePercent = gustImpulsePercent;
            Material = material;
            Gem = gem;
            ShadowPerM2Permille = shadowPerM2Permille;
            ShadowCapPermille = shadowCapPermille;
            _buildings = (Composition[])buildings.Clone();
            _ground = (Substance[])ground.Clone();
            GemSubstance = gemSubstance;
        }

        /// <summary>Number of <see cref="BuildingMaterial"/> values.</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const int BuildingMaterialCount = 6;

        /// <summary>Burn loss per second, flammable = slow, as per-mille of max HP.</summary>
        /// <remarks>Pre: none. Post: 1..1000. Side effects: none.</remarks>
        public int BurnSlowPermillePerS { get; }

        /// <summary>Burn loss per second, flammable = fast.</summary>
        /// <remarks>Pre: none. Post: 1..1000. Side effects: none.</remarks>
        public int BurnFastPermillePerS { get; }

        /// <summary>Damage percent of a fire hit on ice.</summary>
        /// <remarks>Pre: none. Post: 100..1000. Side effects: none.</remarks>
        public int MeltPercent { get; }

        /// <summary>Brittle window after an ice hit.</summary>
        /// <remarks>Pre: none. Post: &gt;= 1. Side effects: none.</remarks>
        public int FreezeMs { get; }

        /// <summary>Damage percent of an earth/metal hit on a brittle object.</summary>
        /// <remarks>Pre: none. Post: 100..1000. Side effects: none.</remarks>
        public int ShatterPercent { get; }

        /// <summary>Damage percent of a thunder hit on a conductive object.</summary>
        /// <remarks>Pre: none. Post: 100..1000. Side effects: none.</remarks>
        public int ConductPercent { get; }

        /// <summary>Impulse percent of a wind hit (presentation only).</summary>
        /// <remarks>Pre: none. Post: 100..1000. Side effects: none.</remarks>
        public int GustImpulsePercent { get; }

        /// <summary>Tier-1 material roll.</summary>
        /// <remarks>Pre: none. Post: stable. Side effects: none.</remarks>
        public VolumeChance Material { get; }

        /// <summary>Tier-3 gem roll.</summary>
        /// <remarks>Pre: none. Post: stable. Side effects: none.</remarks>
        public VolumeChance Gem { get; }

        /// <summary>Night shadow roll: per-mille per m² of shadow area at full opacity.</summary>
        /// <remarks>Pre: none. Post: 0..1000. Side effects: none.</remarks>
        public int ShadowPerM2Permille { get; }

        /// <summary>Night shadow roll cap.</summary>
        /// <remarks>Pre: none. Post: 0..1000. Side effects: none.</remarks>
        public int ShadowCapPermille { get; }

        /// <summary>Substance every gem lies on the ground as (indestructible regardless of its flags).</summary>
        /// <remarks>Pre: none. Post: non-null. Side effects: none.</remarks>
        public Substance GemSubstance { get; }

        /// <summary>The "ice" substance: the ground substance of the ice element; fire on it melts (→ water).</summary>
        /// <remarks>Pre: none. Post: non-null. Side effects: none.</remarks>
        public Substance Ice => _ground[(int)Element.Ice];

        /// <summary>Composition of a building class.</summary>
        /// <remarks>Pre: kind is a defined value. Post: non-null. Side effects: none.</remarks>
        public Composition Building(BuildingMaterial kind) => _buildings[(int)kind];

        /// <summary>Substance a ground material of <paramref name="element"/> behaves as.</summary>
        /// <remarks>Pre: element is defined. Post: null iff Light or Shadow (non-colliding, indestructible). Side effects: none.</remarks>
        public Substance Ground(Element element) => _ground[(int)element];

        private static bool InBoost(int p) => p >= 100 && p <= 1000;
    }

    /// <summary>
    /// Building material class (env-materials.schema.json building.kind). RuinWall covers every building of a Ruins
    /// settlement, whatever its WorldGen BuildingKind.
    /// </summary>
    public enum BuildingMaterial
    {
        Tent = 0,
        House = 1,
        Hall = 2,
        Tower = 3,
        Pillar = 4,
        RuinWall = 5,
    }
}
