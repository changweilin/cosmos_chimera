using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Unity.Mathematics;

namespace Cosmimera.Combat
{
    /// <summary>A generated scene object a move may touch. REQ: docs/design/destructibles.md#範圍.</summary>
    /// <remarks>Pre: none. Post: immutable value. Side effects: none.</remarks>
    public readonly struct DestructibleCandidate
    {
        /// <summary>Creates a candidate.</summary>
        /// <remarks>
        /// Pre: hitRadiusM &gt; 0; composition non-null; volumeL &gt;= 1. Post: fields echo arguments. Side effects: none.
        /// </remarks>
        public DestructibleCandidate(long id, float3 center, float hitRadiusM, Composition composition, int volumeL)
        {
            Contract.Requires(hitRadiusM > 0f && composition != null && volumeL >= 1,
                "hitRadiusM > 0, composition non-null and volumeL >= 1 required");
            Id = id;
            Center = center;
            HitRadiusM = hitRadiusM;
            Composition = composition;
            VolumeL = volumeL;
        }

        /// <summary>Generation-derived id, stable across LOD rebuilds and peers.</summary>
        /// <remarks>Pre: none. Post: stable. Side effects: none.</remarks>
        public long Id { get; }

        /// <summary>World-space centre of the hit sphere.</summary>
        /// <remarks>Pre: none. Post: finite. Side effects: none.</remarks>
        public float3 Center { get; }

        /// <summary>Hit sphere radius (m) that move volumes are tested against.</summary>
        /// <remarks>Pre: none. Post: &gt; 0. Side effects: none.</remarks>
        public float HitRadiusM { get; }

        /// <summary>What the object is made of.</summary>
        /// <remarks>Pre: none. Post: non-null. Side effects: none.</remarks>
        public Composition Composition { get; }

        /// <summary>Volume in litres.</summary>
        /// <remarks>Pre: none. Post: &gt;= 1. Side effects: none.</remarks>
        public int VolumeL { get; }
    }

    /// <summary>
    /// Spatial lookup of generated destructible objects (scatter items, buildings), so the encounter can hit them without
    /// owning world generation. Implemented by the composition root.
    /// </summary>
    /// <remarks>Pre: implementations are deterministic for one host. Post: see members. Side effects: see members.</remarks>
    public interface IDestructibleIndex
    {
        /// <summary>Largest <see cref="DestructibleCandidate.HitRadiusM"/> this index can return (m).</summary>
        /// <remarks>Pre: none. Post: &gt; 0; constant. Side effects: none.</remarks>
        float MaxHitRadiusM { get; }

        /// <summary>Appends every object whose centre lies within <paramref name="radiusM"/> of <paramref name="worldPoint"/>.</summary>
        /// <remarks>
        /// Pre: worldPoint finite; radiusM finite and &gt;= 0; into non-null.
        /// Post: appends in ascending Id order, including destroyed objects (the caller filters them); a superset is fine.
        /// Side effects: appends to into; may cache generation results.
        /// </remarks>
        void Query(float3 worldPoint, float radiusM, List<DestructibleCandidate> into);
    }

    /// <summary>What happened to an object during one encounter tick (presentation, drops, networking).</summary>
    public enum ObjectEventKind
    {
        /// <summary>A move touched the object and dealt <see cref="ObjectEvent.Damage"/> (possibly 0 when immune).</summary>
        Hit = 0,
        /// <summary>The object broke; <see cref="ObjectEvent.Drop"/> holds what it dropped.</summary>
        Destroyed = 1,
    }

    /// <summary>One object event of the last <see cref="Encounter.Tick"/>.</summary>
    /// <remarks>Pre: produced by Encounter. Post: immutable value. Side effects: none.</remarks>
    public readonly struct ObjectEvent
    {
        internal ObjectEvent(ObjectEventKind kind, long objectId, int sourceId, int damage, ObjectReaction reactions,
            DestroyCause cause, ObjectDrop drop)
        {
            Kind = kind;
            ObjectId = objectId;
            SourceId = sourceId;
            Damage = damage;
            Reactions = reactions;
            Cause = cause;
            Drop = drop;
        }

        /// <summary>Event kind.</summary>
        /// <remarks>Pre: none. Post: defined. Side effects: none.</remarks>
        public ObjectEventKind Kind { get; }

        /// <summary>Object id (see <see cref="DestructibleCandidate.Id"/>).</summary>
        /// <remarks>Pre: none. Post: stable. Side effects: none.</remarks>
        public long ObjectId { get; }

        /// <summary>Casting unit id, or <see cref="Encounter.NoSource"/> for burn-outs.</summary>
        /// <remarks>Pre: none. Post: a unit id or NoSource. Side effects: none.</remarks>
        public int SourceId { get; }

        /// <summary>HP removed (Hit only).</summary>
        /// <remarks>Pre: none. Post: &gt;= 0. Side effects: none.</remarks>
        public int Damage { get; }

        /// <summary>Reactions of the hit (Hit only).</summary>
        /// <remarks>Pre: none. Post: stable. Side effects: none.</remarks>
        public ObjectReaction Reactions { get; }

        /// <summary>Why it broke (Destroyed only).</summary>
        /// <remarks>Pre: none. Post: stable. Side effects: none.</remarks>
        public DestroyCause Cause { get; }

        /// <summary>Drop roll result (Destroyed only).</summary>
        /// <remarks>Pre: none. Post: stable. Side effects: none.</remarks>
        public ObjectDrop Drop { get; }
    }
}
