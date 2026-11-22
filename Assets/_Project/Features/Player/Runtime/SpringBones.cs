using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.Planet;
using Unity.Mathematics;
using UnityEngine;

namespace Cosmimera.Player
{
    /// <summary>Swings an ACG character's hanging cloth: every "sw_" bone chain from tools/acg-character
    /// follows gravity and inertia after the Animator has posed the body.</summary>
    /// <remarks>
    /// Pre: on the character root, whose hierarchy holds chains named "sw_..." ending in a "..._end" bone;
    /// the owner calls <see cref="UsePlanet"/> once (gravity then follows the local down every frame) or sets
    /// <see cref="Gravity"/> itself, since "down" differs at every point of the spherical world.
    /// Post: after LateUpdate, each chain bone points along its simulated segment.
    /// Side effects: rewrites the chain bones' rotations every frame.
    /// REQ: docs/design/characters.md#靜止姿勢 (cloth swings; open-questions Q-T11 → own spring bones).
    /// </remarks>
    public sealed class SpringBones : MonoBehaviour
    {
        /// <summary>Prefix of the swing bones written by tools/acg-character/acg_swing.py.</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const string BonePrefix = "sw_";

        [SerializeField, Range(0f, 1f), Tooltip("Pull back toward the modelled shape per step (0 hangs free).")]
        private float _stiffness = 0.04f;

        [SerializeField, Range(0f, 1f), Tooltip("Fraction of swing velocity lost per step.")]
        private float _drag = 0.12f;

        /// <summary>Stiffness and drag for chains whose root bone name starts with <see cref="Prefix"/>.</summary>
        /// <remarks>Pre: Stiffness and Drag in [0, 1]. Post: plain value. Side effects: none.</remarks>
        [System.Serializable]
        public struct ChainSettings
        {
            /// <summary>Root bone name prefix, e.g. "sw_hair_".</summary>
            /// <remarks>Pre: none. Post: none. Side effects: none.</remarks>
            public string Prefix;

            /// <summary>Pull toward the modelled shape per step.</summary>
            /// <remarks>Pre: [0, 1]. Post: none. Side effects: none.</remarks>
            [Range(0f, 1f)] public float Stiffness;

            /// <summary>Fraction of swing velocity lost per step.</summary>
            /// <remarks>Pre: [0, 1]. Post: none. Side effects: none.</remarks>
            [Range(0f, 1f)] public float Drag;
        }

        // First matching prefix wins; others use the defaults above. Hair keeps its sculpted shape (anime
        // hair sways rather than hanging like rope); a skirt keeps its flare, or gravity folds it onto the legs.
        [SerializeField]
        private ChainSettings[] _overrides =
        {
            new ChainSettings { Prefix = BonePrefix + "hair_", Stiffness = 0.2f, Drag = 0.2f },
            new ChainSettings { Prefix = BonePrefix + "skirt", Stiffness = 0.3f, Drag = 0.15f },
        };

        // Body capsules written at import (AcgSpringImport) from the body mesh: bone pairs and radii.
        [SerializeField] private Transform[] _capsuleStart = new Transform[0];
        [SerializeField] private Transform[] _capsuleEnd = new Transform[0];
        [SerializeField] private float[] _capsuleRadius = new float[0];

        private const float EarthGravity = 9.81f;

        // Longer frames are split so a hitch does not launch the cloth; 1/60 s keeps Verlet stable
        // at the stiffness range above.
        private const float MaxStep = 1f / 60f;

        private sealed class Chain
        {
            public Transform[] Bones;          // root .. end, end excluded from rotation writes
            public Quaternion[] RestLocal;     // localRotation of each bone in the modelled pose
            public Vector3[] ChildLocal;       // child bone offset in each bone's own space
            public float3[] RestDir;           // per step: world direction of each segment at rest
            public SpringChain Sim;
            public float Stiffness;
            public float Drag;
            public Capsule[] Colliders;        // body capsules minus the ones around the chain's own anchor
            public int[] ColliderIndex;
        }

        private PlanetConfig _planet;
        private bool _hasPlanet;
        private float _gravityAccel;

        private readonly List<Chain> _chains = new List<Chain>();
        private bool _built;

        /// <summary>World gravity acceleration (m/s²) the cloth falls along; zero until the owner sets it.</summary>
        /// <remarks>
        /// Pre: finite; set from the planet's local down (PlanetFrame), never a fixed world axis.
        /// Post: used by the next simulation step. Side effects: none.
        /// </remarks>
        public Vector3 Gravity { get; set; }

        /// <summary>Makes the cloth fall toward the planet centre at <paramref name="acceleration"/> m/s² from now on.</summary>
        /// <remarks>
        /// Pre: planet constructed (Radius > 0); acceleration finite and >= 0.
        /// Post: every simulation step sets <see cref="Gravity"/> to the local down at the character's position.
        /// Side effects: overrides any Gravity set by hand.
        /// </remarks>
        public void UsePlanet(in PlanetConfig planet, float acceleration = EarthGravity)
        {
            Contract.Requires(planet.Radius > 0f, "planet must be constructed");
            Contract.Requires(acceleration >= 0f && !float.IsInfinity(acceleration), "acceleration finite and >= 0");
            _planet = planet;
            _gravityAccel = acceleration;
            _hasPlanet = true;
        }

        /// <summary>Sets the body capsules cloth is kept out of (import-time data from the body mesh).</summary>
        /// <remarks>
        /// Pre: the three arrays have equal length; transforms non-null and under this character; radii >= 0.
        /// Post: the next built chain list collides with these capsules.
        /// Side effects: replaces the serialized capsule data; forces the chain list to rebuild.
        /// </remarks>
        public void SetBodyCapsules(Transform[] start, Transform[] end, float[] radius)
        {
            Contract.Requires(start != null && end != null && radius != null, "arrays non-null");
            Contract.Requires(start.Length == end.Length && end.Length == radius.Length, "arrays of equal length");
            _capsuleStart = start;
            _capsuleEnd = end;
            _capsuleRadius = radius;
            _built = false;
            _chains.Clear();
        }

        /// <summary>Number of body capsules the cloth collides with.</summary>
        /// <remarks>Pre: none. Post: >= 0. Side effects: none.</remarks>
        public int CapsuleCount => _capsuleRadius.Length;

        /// <summary>Number of swing chains found under this character.</summary>
        /// <remarks>Pre: none. Post: >= 0; builds the chain list on first use. Side effects: may build the list.</remarks>
        public int ChainCount
        {
            get
            {
                Build();
                return _chains.Count;
            }
        }

        private void LateUpdate()
        {
            Simulate(Time.deltaTime);
        }

        /// <summary>Advances every chain by <paramref name="deltaTime"/> seconds and writes the bone rotations.</summary>
        /// <remarks>
        /// Pre: deltaTime >= 0 and finite; the Animator (if any) has already posed the body this frame.
        /// Post: each chain bone's child direction matches its simulated segment.
        /// Side effects: rewrites chain bone rotations; builds the chain list on first call.
        /// Called by LateUpdate; tools may call it directly to settle the cloth without the player loop.
        /// </remarks>
        public void Simulate(float deltaTime)
        {
            Contract.Requires(deltaTime >= 0f && !float.IsInfinity(deltaTime), "deltaTime must be finite and >= 0");
            Build();
            if (deltaTime <= 0f)
            {
                return;
            }

            int steps = Mathf.CeilToInt(deltaTime / MaxStep);
            float dt = deltaTime / steps;
            if (_hasPlanet)
            {
                Gravity = -(Vector3)PlanetFrame.At(_planet, transform.position).Up * _gravityAccel;
            }

            float3 g = Gravity;
            foreach (var c in _chains)
            {
                for (int k = 0; k < c.ColliderIndex.Length; k++)
                {
                    int j = c.ColliderIndex[k];
                    c.Colliders[k] = new Capsule
                    {
                        A = _capsuleStart[j].position,
                        B = _capsuleEnd[j].position,
                        Radius = _capsuleRadius[j] * _capsuleStart[j].lossyScale.x,
                    };
                }

                // Reset to the modelled pose so the rest directions come from the anchor's current pose.
                for (int i = 0; i < c.RestLocal.Length; i++)
                {
                    c.Bones[i].localRotation = c.RestLocal[i];
                }

                for (int i = 0; i < c.RestDir.Length; i++)
                {
                    c.RestDir[i] = (c.Bones[i].rotation * c.ChildLocal[i]).normalized;
                }

                for (int s = 0; s < steps; s++)
                {
                    c.Sim.Step(c.Bones[0].position, c.RestDir, g, c.Stiffness, c.Drag, dt, c.Colliders, c.Colliders.Length);
                }

                for (int i = 0; i < c.RestDir.Length; i++)
                {
                    Transform b = c.Bones[i];
                    Vector3 have = b.rotation * c.ChildLocal[i];
                    Vector3 want = (Vector3)(c.Sim.Joint(i + 1) - c.Sim.Joint(i));
                    b.rotation = Quaternion.FromToRotation(have, want) * b.rotation;
                }
            }
        }

        private void Build()
        {
            if (_built)
            {
                return;
            }

            _built = true;
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                if (!t.name.StartsWith(BonePrefix) || (t.parent != null && t.parent.name.StartsWith(BonePrefix)))
                {
                    continue;
                }

                var bones = new List<Transform> { t };
                Transform cur = t;
                while (true)
                {
                    Transform next = null;
                    for (int i = 0; i < cur.childCount; i++)
                    {
                        if (cur.GetChild(i).name.StartsWith(BonePrefix))
                        {
                            next = cur.GetChild(i);
                            break;
                        }
                    }

                    if (next == null)
                    {
                        break;
                    }

                    bones.Add(next);
                    cur = next;
                }

                if (bones.Count < 2)
                {
                    Debug.LogWarning($"[SpringBones] chain {t.name} has no end bone; skipped", this);
                    continue;
                }

                int n = bones.Count - 1;
                var c = new Chain
                {
                    Bones = bones.ToArray(),
                    RestLocal = new Quaternion[n],
                    ChildLocal = new Vector3[n],
                    RestDir = new float3[n],
                };
                var joints = new float3[bones.Count];
                for (int i = 0; i < bones.Count; i++)
                {
                    joints[i] = bones[i].position;
                }

                for (int i = 0; i < n; i++)
                {
                    c.RestLocal[i] = bones[i].localRotation;
                    c.ChildLocal[i] = bones[i + 1].localPosition;
                }

                c.Sim = new SpringChain(joints);
                c.Stiffness = _stiffness;
                c.Drag = _drag;
                foreach (var o in _overrides)
                {
                    if (!string.IsNullOrEmpty(o.Prefix) && t.name.StartsWith(o.Prefix))
                    {
                        c.Stiffness = o.Stiffness;
                        c.Drag = o.Drag;
                        break;
                    }
                }
                // A chain hanging from a limb starts on that limb's surface; colliding with it would
                // push the first joint off the arm, so the anchor bone's own capsules are skipped.
                var use = new List<int>();
                for (int j = 0; j < _capsuleRadius.Length; j++)
                {
                    if (_capsuleStart[j] != t.parent && _capsuleEnd[j] != t.parent)
                    {
                        use.Add(j);
                    }
                }

                c.ColliderIndex = use.ToArray();
                c.Colliders = new Capsule[use.Count];
                _chains.Add(c);
            }
        }
    }
}
