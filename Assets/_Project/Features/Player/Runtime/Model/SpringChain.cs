using Cosmimera.Core.Contracts;
using Unity.Mathematics;

namespace Cosmimera.Player
{
    /// <summary>A body part the cloth must stay outside of: the segment A..B swept by Radius.</summary>
    /// <remarks>Pre: Radius >= 0; A, B finite. Post: plain value. Side effects: none.</remarks>
    internal struct Capsule
    {
        /// <summary>Segment start.</summary>
        /// <remarks>Pre: finite. Post: none. Side effects: none.</remarks>
        public float3 A;

        /// <summary>Segment end (may equal A for a sphere).</summary>
        /// <remarks>Pre: finite. Post: none. Side effects: none.</remarks>
        public float3 B;

        /// <summary>Distance from the segment the cloth is kept outside of, metres.</summary>
        /// <remarks>Pre: >= 0. Post: none. Side effects: none.</remarks>
        public float Radius;

        /// <summary>Returns <paramref name="p"/> moved to the capsule surface if it lies inside, else p.</summary>
        /// <remarks>Pre: p finite. Post: distance(result, segment) >= Radius - 1e-6 unless p is on the axis
        /// (then it is pushed along the axis-perpendicular <paramref name="fallback"/>). Side effects: none.</remarks>
        public readonly float3 PushOut(float3 p, float3 fallback)
        {
            float3 ab = B - A;
            float t = math.lengthsq(ab) > 1e-12f ? math.saturate(math.dot(p - A, ab) / math.lengthsq(ab)) : 0f;
            float3 c = A + ab * t;
            float3 d = p - c;
            float len = math.length(d);
            if (len >= Radius)
            {
                return p;
            }

            return c + (len > 1e-6f ? d / len : fallback) * Radius;
        }
    }

    /// <summary>One hanging cloth chain (sleeve panel, sash tail) swung by gravity and inertia with Verlet steps.</summary>
    /// <remarks>
    /// Pre: built from at least two joints with distinct consecutive positions.
    /// Post: after every Step, joint 0 is the given root and each segment keeps its rest length.
    /// Side effects: none outside this instance (mutable state).
    /// REQ: docs/design/characters.md#靜止姿勢 (ACG cloth swings; open-questions Q-T11 → own spring bones).
    /// </remarks>
    internal sealed class SpringChain
    {
        private readonly float3[] _pos;
        private readonly float3[] _prev;
        private readonly float[] _length;

        /// <summary>Joint count, root and tip included.</summary>
        /// <remarks>Pre: none. Post: >= 2, constant. Side effects: none.</remarks>
        public int Count => _pos.Length;

        /// <summary>Starts the chain at rest on <paramref name="joints"/> (root first), at zero velocity.</summary>
        /// <remarks>
        /// Pre: joints.Length >= 2; consecutive joints at least 1e-5 m apart.
        /// Post: Joint(i) == joints[i]; segment lengths are fixed from these positions.
        /// Side effects: allocates the state arrays.
        /// </remarks>
        public SpringChain(float3[] joints)
        {
            Contract.Requires(joints != null && joints.Length >= 2, "a chain needs a root and a tip");
            _pos = (float3[])joints.Clone();
            _prev = (float3[])joints.Clone();
            _length = new float[joints.Length - 1];
            for (int i = 0; i < _length.Length; i++)
            {
                _length[i] = math.distance(joints[i], joints[i + 1]);
                Contract.Requires(_length[i] > 1e-5f, "consecutive joints must not coincide");
            }
        }

        /// <summary>Current position of joint <paramref name="i"/>.</summary>
        /// <remarks>Pre: 0 <= i < Count. Post: finite after valid steps. Side effects: none.</remarks>
        public float3 Joint(int i)
        {
            Contract.Requires(i >= 0 && i < _pos.Length, "joint index out of range");
            return _pos[i];
        }

        /// <summary>Rest length of segment <paramref name="i"/> (joint i to i + 1).</summary>
        /// <remarks>Pre: 0 <= i < Count - 1. Post: > 0. Side effects: none.</remarks>
        public float SegmentLength(int i)
        {
            Contract.Requires(i >= 0 && i < _length.Length, "segment index out of range");
            return _length[i];
        }

        /// <summary>Advances the chain by <paramref name="dt"/> seconds.</summary>
        /// <remarks>
        /// Pre: dt > 0; restDirections.Length == Count - 1 with unit vectors (where each segment points in the
        /// anchor bone's rest pose, world space); 0 <= stiffness <= 1; 0 <= drag <= 1; all inputs finite.
        /// Post: Joint(0) == root; |Joint(i + 1) - Joint(i)| == SegmentLength(i) (to float precision).
        /// Side effects: mutates the joint state.
        /// stiffness pulls each joint toward its rest direction per step (0 = hangs free, 1 = rigid);
        /// drag removes that fraction of the carried velocity per step.
        /// </remarks>
        public void Step(float3 root, float3[] restDirections, float3 gravity, float stiffness, float drag, float dt)
        {
            Step(root, restDirections, gravity, stiffness, drag, dt, null, 0);
        }

        /// <summary>As <see cref="Step(float3, float3[], float3, float, float, float)"/>, then keeps every joint
        /// but the root outside the first <paramref name="colliderCount"/> <paramref name="colliders"/>.</summary>
        /// <remarks>
        /// Pre: as the overload; 0 <= colliderCount <= colliders.Length (colliders may be null when the count is 0).
        /// Post: as the overload; a joint ends outside each collider unless keeping its segment length forbids it
        /// (the length constraint wins, so cloth never stretches).
        /// Side effects: mutates the joint state.
        /// </remarks>
        public void Step(float3 root, float3[] restDirections, float3 gravity, float stiffness, float drag, float dt,
            Capsule[] colliders, int colliderCount)
        {
            Contract.Requires(colliderCount >= 0 && (colliderCount == 0 || (colliders != null && colliderCount <= colliders.Length)),
                "collider count within the array");
            Contract.Requires(dt > 0f && !float.IsInfinity(dt), "dt must be positive and finite");
            Contract.Requires(restDirections != null && restDirections.Length == _length.Length, "one rest direction per segment");
            Contract.Requires(stiffness >= 0f && stiffness <= 1f, "stiffness in [0, 1]");
            Contract.Requires(drag >= 0f && drag <= 1f, "drag in [0, 1]");
            Contract.Requires(math.all(math.isfinite(root)) && math.all(math.isfinite(gravity)), "root and gravity finite");

            // Body translation moves the whole chain rigidly: only the anchor's rotation and the cloth's own
            // swing count as velocity, so walking neither flings the cloth nor (with drag) makes it trail.
            float3 shift = root - _pos[0];
            for (int i = 0; i < _pos.Length; i++)
            {
                _pos[i] += shift;
                _prev[i] += shift;
            }

            float3 pull = gravity * (dt * dt);
            for (int i = 1; i < _pos.Length; i++)
            {
                float3 carried = (_pos[i] - _prev[i]) * (1f - drag);
                float3 target = _pos[i - 1] + restDirections[i - 1] * _length[i - 1];
                float3 next = _pos[i] + carried + (target - _pos[i]) * stiffness + pull;
                float3 d = next - _pos[i - 1];
                float len = math.length(d);
                d = len > 1e-6f ? d / len : restDirections[i - 1];
                next = _pos[i - 1] + d * _length[i - 1];
                if (colliderCount > 0)
                {
                    for (int k = 0; k < colliderCount; k++)
                    {
                        next = colliders[k].PushOut(next, d);
                    }

                    // Re-impose the length after the push; one pass is enough for capsules thicker than a segment step.
                    float3 e = next - _pos[i - 1];
                    float el = math.length(e);
                    next = _pos[i - 1] + (el > 1e-6f ? e / el : d) * _length[i - 1];
                }
                _prev[i] = _pos[i];
                _pos[i] = next;
            }
        }
    }
}
