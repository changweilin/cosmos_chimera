using System;
using System.Collections.Generic;
using Cosmimera.Combat;
using Cosmimera.Core.Contracts;
using Cosmimera.Planet;
using Unity.Mathematics;
using UnityEngine;

namespace Cosmimera.Bootstrap.Demo
{
    /// <summary>Presentation tuning for object rigidbodies (ADR-0009). Placeholder feel values, tuned by playtest.</summary>
    /// <remarks>Pre: build through <see cref="Default"/> or keep every value finite and &gt; 0. Post: plain data. Side effects: none.</remarks>
    public struct ObjectPhysicsTuning
    {
        /// <summary>Impulse (N·s) per point of move power; a 270 kg tree hit by power 60 leaves at ~3 m/s.</summary>
        /// <remarks>Pre: none. Post: plain data. Side effects: none.</remarks>
        public float ImpulsePerPowerNs;

        /// <summary>Upward share of the impulse direction (tangent part normalised to 1 first), so hits lift and topple.</summary>
        /// <remarks>Pre: none. Post: plain data. Side effects: none.</remarks>
        public float Lift;

        /// <summary>Objects heavier than this (kg) take damage but never move (destructibles.md#物理: halls, boulders).</summary>
        /// <remarks>Pre: none. Post: plain data. Side effects: none.</remarks>
        public float MaxMovableKg;

        /// <summary>At most this many woken objects simulate at once; the oldest is frozen in place beyond it (ADR-0009 後果).</summary>
        /// <remarks>Pre: none. Post: plain data. Side effects: none.</remarks>
        public int MaxAwake;

        /// <summary>At most this many debris pieces exist at once; the oldest disappears beyond it.</summary>
        /// <remarks>Pre: none. Post: plain data. Side effects: none.</remarks>
        public int MaxDebris;

        /// <summary>A debris piece disappears after resting this long (s).</summary>
        /// <remarks>Pre: none. Post: plain data. Side effects: none.</remarks>
        public float DebrisRestSeconds;

        /// <summary>A debris piece disappears this long after it spawned (s), resting or not.</summary>
        /// <remarks>Pre: none. Post: plain data. Side effects: none.</remarks>
        public float DebrisMaxSeconds;

        /// <summary>Placeholder values.</summary>
        /// <remarks>Pre: none. Post: <see cref="IsValid"/>. Side effects: none.</remarks>
        public static ObjectPhysicsTuning Default => new ObjectPhysicsTuning
        {
            ImpulsePerPowerNs = 15f,
            Lift = 0.35f,
            MaxMovableKg = 20000f,
            MaxAwake = 32,
            MaxDebris = 96,
            DebrisRestSeconds = 3f,
            DebrisMaxSeconds = 15f,
        };

        /// <summary>Whether every value is usable.</summary>
        /// <remarks>Pre: none. Post: true iff all floats finite and &gt; 0 (Lift &gt;= 0) and both caps &gt;= 1. Side effects: none.</remarks>
        public bool IsValid =>
            Positive(ImpulsePerPowerNs) && math.isfinite(Lift) && Lift >= 0f && Positive(MaxMovableKg) && MaxAwake >= 1 &&
            MaxDebris >= 1 && Positive(DebrisRestSeconds) && Positive(DebrisMaxSeconds);

        private static bool Positive(float v) => math.isfinite(v) && v > 0f;
    }

    /// <summary>How hard a move shoves a scene object. REQ: destructibles.md#物理.</summary>
    /// <remarks>Pure; presentation only (ADR-0009 §5: nothing here feeds the rule layer). Side effects: none.</remarks>
    public static class ObjectImpulse
    {
        /// <summary>Mass (kg) of <paramref name="volumeL"/> litres of <paramref name="composition"/> (weight-averaged density).</summary>
        /// <remarks>Pre: composition non-null; volumeL &gt;= 0. Post: &gt;= 0. Side effects: none.</remarks>
        public static float MassKg(Composition composition, int volumeL)
        {
            Contract.Requires(composition != null && volumeL >= 0, "composition non-null, volumeL >= 0");
            long weighted = 0;
            for (int i = 0; i < composition.Count; i++)
            {
                weighted += (long)composition.Part(i).DensityKgM3 * composition.Weight(i);
            }

            return (float)weighted / composition.TotalWeight * volumeL * 0.001f;
        }

        /// <summary>Impulse (N·s) a move of <paramref name="power"/> cast from <paramref name="source"/> gives the object at <paramref name="target"/>.</summary>
        /// <remarks>
        /// Pre: tuning.IsValid; source and target finite and off the planet centre; power &gt;= 0; gustPercent &gt;= 0
        ///      (100 = no gust, from EnvMaterials.GustImpulsePercent when the hit reacted Gust); massKg finite and &gt;= 0.
        /// Post: zero when massKg &gt; MaxMovableKg or power == 0. Otherwise |result| = power · ImpulsePerPowerNs ·
        ///       gustPercent / 100, along the tangent from source to target (the planet frame's north when they are
        ///       radially aligned) plus Lift times the target's local up, normalised. Mass does not scale the impulse:
        ///       the body's velocity change (impulse / mass) is what makes heavy objects move less.
        /// Side effects: none.
        /// </remarks>
        public static float3 Compute(in PlanetConfig planet, float3 source, float3 target, int power, int gustPercent,
            float massKg, in ObjectPhysicsTuning tuning)
        {
            Contract.Requires(tuning.IsValid, "tuning must be valid");
            Contract.Requires(math.all(math.isfinite(source)) && math.all(math.isfinite(target)), "positions must be finite");
            Contract.Requires(power >= 0 && gustPercent >= 0, "power and gustPercent must be >= 0");
            Contract.Requires(math.isfinite(massKg) && massKg >= 0f, "massKg must be finite and >= 0");
            if (massKg > tuning.MaxMovableKg || power == 0)
            {
                return float3.zero;
            }

            float3 up = planet.ToDirection(target);
            float3 rel = target - source;
            float3 tangent = rel - math.dot(rel, up) * up;
            tangent = math.lengthsq(tangent) > 1e-8f ? math.normalize(tangent) : PlanetFrame.FromUp(up).North;
            float3 dir = math.normalize(tangent + up * tuning.Lift);
            return dir * (power * tuning.ImpulsePerPowerNs * gustPercent * 0.01f);
        }
    }

    /// <summary>
    /// Rigidbody side of destructible objects: wakes hit objects, shatters destroyed ones into debris, and moves the
    /// creatures' kinematic pushers. REQ: destructibles.md#物理, #破壞與掉落; ADR-0009.
    /// </summary>
    /// <remarks>
    /// Pre: <see cref="Configure"/> before any other member; main thread. Post: per member.
    /// Side effects: creates and destroys GameObjects and Rigidbodies. Never reports back to the rule layer (ADR-0009 §5).
    /// </remarks>
    public sealed class ObjectPhysics : MonoBehaviour
    {
        // Bodies start sunk a little into the terrain (ScatterField and SettlementView sink their bases); unlimited
        // depenetration would launch them on wake.
        private const float MaxDepenetrationMs = 1f;
        // Below the sampled ground by more than this, the terrain under the body has no collider (streamed out).
        private const float FallThroughM = 2f;
        private const int MaxPiecesPerObject = 8;

        private readonly struct Awake
        {
            internal Awake(long id, Rigidbody body)
            {
                Id = id;
                Body = body;
            }

            internal readonly long Id;
            internal readonly Rigidbody Body;
        }

        private struct Piece
        {
            internal Rigidbody Body;
            internal float BornAt;
            internal float RestingSince;
        }

        private readonly List<Awake> _awake = new List<Awake>(32);
        private readonly Dictionary<long, Rigidbody> _byId = new Dictionary<long, Rigidbody>();
        private readonly List<Piece> _debris = new List<Piece>(96);
        private readonly Dictionary<int, Rigidbody> _pushers = new Dictionary<int, Rigidbody>();
        private PlanetConfig _planet;
        private PlanetGravity _gravity;
        private Func<float3, float> _groundAltitude;
        private Collider[] _ignore;
        private ObjectPhysicsTuning _tuning;
        private bool _configured;

        /// <summary>Binds the planet, gravity and terrain.</summary>
        /// <remarks>
        /// Pre: gravity and groundAltitude non-null; tuning.IsValid; ignore non-null (colliders pushers must not touch:
        ///      the character's, whose body collision is rule-side, combat.md#實體碰撞); called once.
        /// Post: the other members work. Side effects: stores references.
        /// </remarks>
        public void Configure(in PlanetConfig planet, PlanetGravity gravity, Func<float3, float> groundAltitude,
            Collider[] ignore, in ObjectPhysicsTuning tuning)
        {
            Contract.Requires(gravity != null && groundAltitude != null && ignore != null, "gravity, groundAltitude, ignore required");
            Contract.Requires(tuning.IsValid, "tuning must be valid");
            Contract.Requires(!_configured, "Configure must be called once");
            _planet = planet;
            _gravity = gravity;
            _groundAltitude = groundAltitude;
            _ignore = ignore;
            _tuning = tuning;
            _configured = true;
        }

        /// <summary>The tuning in use.</summary>
        /// <remarks>Pre: Configure ran. Post: as configured. Side effects: none.</remarks>
        public ObjectPhysicsTuning Tuning => _tuning;

        /// <summary>Simulating woken objects (frozen ones excluded).</summary>
        /// <remarks>Pre: none. Post: 0..MaxAwake. Side effects: none.</remarks>
        public int AwakeCount => _awake.Count;

        /// <summary>Live debris pieces.</summary>
        /// <remarks>Pre: none. Post: 0..MaxDebris. Side effects: none.</remarks>
        public int DebrisCount => _debris.Count;

        /// <summary>The woken root of object <paramref name="id"/>, if it was woken (and not yet shattered).</summary>
        /// <remarks>Pre: none. Post: null when never woken. Side effects: none.</remarks>
        public GameObject WokenRoot(long id) => _byId.TryGetValue(id, out Rigidbody b) && b != null ? b.gameObject : null;

        /// <summary>Applies a hit's impulse to object <paramref name="id"/>, turning it into a rigidbody on its first hit.</summary>
        /// <remarks>
        /// Pre: Configure ran; root non-null, the object's drawn root (its colliders become the body's); massKg &gt; 0;
        ///      impulse from <see cref="ObjectImpulse.Compute"/>; hitPoint finite (the generated hit centre).
        /// Post: false and nothing changes when impulse is zero (too heavy, or no power). Otherwise root carries a
        ///       non-kinematic Rigidbody of massKg under planet gravity and the impulse is applied at hitPoint on the first
        ///       wake (off the centre of mass, so tall objects topple), at the centre of mass afterwards; a frozen body
        ///       wakes again. When more than MaxAwake objects simulate, the oldest is frozen
        ///       where it lies (kinematic, no gravity).
        /// Side effects: adds a Rigidbody; may freeze another object.
        /// </remarks>
        public bool Wake(long id, GameObject root, float massKg, float3 impulse, float3 hitPoint)
        {
            Contract.Requires(_configured && root != null, "Configure first; root required");
            Contract.Requires(math.isfinite(massKg) && massKg > 0f, "massKg must be finite and > 0");
            if (math.lengthsq(impulse) == 0f)
            {
                return false;
            }

            bool first = !_byId.TryGetValue(id, out Rigidbody body) || body == null;
            if (first)
            {
                // Not `??`: a missing component is Unity's fake null in the Editor, which `??` treats as present.
                body = root.GetComponent<Rigidbody>();
                if (body == null)
                {
                    body = root.AddComponent<Rigidbody>();
                }

                body.mass = massKg;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                body.maxDepenetrationVelocity = MaxDepenetrationMs;
                _byId[id] = body;
            }

            if (body.isKinematic || IndexOfAwake(id) < 0)
            {
                body.isKinematic = false;
                _gravity.Register(body);
                _awake.Add(new Awake(id, body));
                if (_awake.Count > _tuning.MaxAwake)
                {
                    Freeze(_awake[0].Body);
                    _awake.RemoveAt(0);
                }
            }

            // hitPoint is the generated spot; once the body has moved it no longer lies on it, so later hits push its centre.
            body.AddForceAtPosition((Vector3)impulse, first ? (Vector3)hitPoint : body.worldCenterOfMass, ForceMode.Impulse);
            return true;
        }

        /// <summary>Breaks object <paramref name="id"/> into debris where <paramref name="root"/> stands.</summary>
        /// <remarks>
        /// Pre: Configure ran; root null (not drawn: nothing to show) or the object's drawn root; hitRadiusM &gt; 0;
        ///      massKg &gt;= 0; impulse from ObjectImpulse.Compute (zero for burn-outs and immovable objects).
        /// Post: 3..8 pieces (more for bigger objects) in the root's first material spread inside the hit sphere,
        ///       flying outward plus impulse / massKg (capped); the object is no longer tracked as woken. The root is
        ///       left for the caller to remove. Oldest pieces beyond MaxDebris disappear.
        /// Side effects: creates GameObjects; UnityEngine.Random for spread (presentation-only, not replayed).
        /// </remarks>
        public void Shatter(long id, GameObject root, float3 center, float hitRadiusM, float massKg, float3 impulse)
        {
            Contract.Requires(_configured, "Configure first");
            Contract.Requires(math.isfinite(hitRadiusM) && hitRadiusM > 0f && math.isfinite(massKg) && massKg >= 0f,
                "hitRadiusM > 0, massKg >= 0");
            Forget(id);
            if (root == null)
            {
                return;
            }

            Renderer look = root.GetComponentInChildren<Renderer>();
            Material material = look != null ? look.sharedMaterial : null;
            int count = math.clamp((int)math.ceil(hitRadiusM * 2.5f), 3, MaxPiecesPerObject);
            float edge = math.clamp(hitRadiusM * 0.45f, 0.15f, 1.5f);
            float pieceKg = math.max(1f, massKg / count);
            float3 up = _planet.ToDirection(center);
            // Cap the inherited speed: a 1 kg pebble taking a full move's impulse would leave at hundreds of m/s.
            float3 carried = massKg > 0f ? impulse / massKg : float3.zero;
            if (math.length(carried) > 6f)
            {
                carried = math.normalize(carried) * 6f;
            }

            for (int i = 0; i < count; i++)
            {
                float3 offset = (float3)UnityEngine.Random.insideUnitSphere * (hitRadiusM * 0.6f);
                GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "Debris";
                go.transform.SetParent(transform, false);
                go.transform.SetPositionAndRotation((Vector3)(center + offset), UnityEngine.Random.rotationUniform);
                go.transform.localScale = Vector3.one * edge;
                if (material != null)
                {
                    go.GetComponent<Renderer>().sharedMaterial = material;
                }

                Rigidbody body = go.AddComponent<Rigidbody>();
                body.mass = pieceKg;
                body.maxDepenetrationVelocity = MaxDepenetrationMs;
                float3 outward = math.lengthsq(offset) > 1e-6f ? math.normalize(offset) : up;
                body.linearVelocity = (Vector3)(carried + outward * 1.5f + up);
                _gravity.Register(body);
                _debris.Add(new Piece { Body = body, BornAt = Time.time, RestingSince = -1f });
            }

            while (_debris.Count > _tuning.MaxDebris)
            {
                DestroyPiece(0);
            }
        }

        /// <summary>Moves (or shows / hides) the kinematic pusher of creature <paramref name="unitId"/>.</summary>
        /// <remarks>
        /// REQ: destructibles.md#碰撞 — creatures push loose bodies aside but are never stopped by them (the rule layer
        /// moves creatures). Pre: Configure ran; radiusM &gt; 0 when active. Post: an active pusher is a kinematic sphere of
        /// radiusM heading to position this physics step; it ignores the configured colliders. Side effects: creates the
        /// pusher on first use; toggles it.
        /// </remarks>
        public void SyncPusher(int unitId, bool active, float3 position, float radiusM)
        {
            Contract.Requires(_configured, "Configure first");
            if (!_pushers.TryGetValue(unitId, out Rigidbody body))
            {
                if (!active)
                {
                    return;
                }

                var go = new GameObject("Pusher_" + unitId);
                go.transform.SetParent(transform, false);
                go.transform.position = (Vector3)position;
                SphereCollider sphere = go.AddComponent<SphereCollider>();
                foreach (Collider c in _ignore)
                {
                    if (c != null)
                    {
                        Physics.IgnoreCollision(sphere, c);
                    }
                }

                body = go.AddComponent<Rigidbody>();
                body.isKinematic = true;
                body.useGravity = false;
                _pushers[unitId] = body;
            }

            if (body.gameObject.activeSelf != active)
            {
                body.gameObject.SetActive(active);
                if (active)
                {
                    body.position = (Vector3)position; // reappearing (summoned elsewhere) must not sweep the gap
                }
            }

            if (!active)
            {
                return;
            }

            Contract.Requires(math.isfinite(radiusM) && radiusM > 0f, "radiusM must be > 0");
            body.GetComponent<SphereCollider>().radius = radiusM;
            body.MovePosition((Vector3)position);
        }

        private void Update()
        {
            if (!_configured)
            {
                return;
            }

            float now = Time.time;
            for (int i = _debris.Count - 1; i >= 0; i--)
            {
                Piece p = _debris[i];
                if (p.Body == null)
                {
                    _debris.RemoveAt(i);
                    continue;
                }

                if (p.Body.IsSleeping())
                {
                    if (p.RestingSince < 0f)
                    {
                        p.RestingSince = now;
                        _debris[i] = p;
                    }
                }
                else if (p.RestingSince >= 0f)
                {
                    p.RestingSince = -1f;
                    _debris[i] = p;
                }

                if (now - p.BornAt >= _tuning.DebrisMaxSeconds ||
                    (p.RestingSince >= 0f && now - p.RestingSince >= _tuning.DebrisRestSeconds))
                {
                    DestroyPiece(i);
                }
            }
        }

        private void FixedUpdate()
        {
            if (!_configured)
            {
                return;
            }

            for (int i = _debris.Count - 1; i >= 0; i--)
            {
                Rigidbody b = _debris[i].Body;
                if (b != null && FellThrough(b.position))
                {
                    DestroyPiece(i);
                }
            }

            for (int i = _awake.Count - 1; i >= 0; i--)
            {
                Rigidbody b = _awake[i].Body;
                if (b == null)
                {
                    _awake.RemoveAt(i);
                    continue;
                }

                if (FellThrough(b.position))
                {
                    // No terrain collider there: park it on the sampled ground instead of letting it fall to the core.
                    float3 dir = _planet.ToDirection(b.position);
                    b.position = (Vector3)_planet.ToWorld(dir, _groundAltitude(dir));
                    Freeze(b);
                    _awake.RemoveAt(i);
                }
            }
        }

        private bool FellThrough(Vector3 position)
        {
            float3 p = position;
            float3 dir = _planet.ToDirection(p);
            return _planet.Altitude(p) < _groundAltitude(dir) - FallThroughM;
        }

        private void Freeze(Rigidbody body)
        {
            if (body == null)
            {
                return;
            }

            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.isKinematic = true;
            _gravity.Unregister(body);
        }

        private void Forget(long id)
        {
            int at = IndexOfAwake(id);
            if (at >= 0)
            {
                _awake.RemoveAt(at);
            }

            if (_byId.TryGetValue(id, out Rigidbody body))
            {
                _gravity.Unregister(body);
                _byId.Remove(id);
            }
        }

        private int IndexOfAwake(long id)
        {
            for (int i = 0; i < _awake.Count; i++)
            {
                if (_awake[i].Id == id)
                {
                    return i;
                }
            }

            return -1;
        }

        private void DestroyPiece(int index)
        {
            Rigidbody b = _debris[index].Body;
            _debris.RemoveAt(index);
            if (b != null)
            {
                _gravity.Unregister(b);
                Destroy(b.gameObject);
            }
        }
    }
}
