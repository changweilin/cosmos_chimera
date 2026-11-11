using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Unity.Mathematics;
using UnityEngine;

namespace Cosmimera.Planet
{
    /// <summary>
    /// Pulls registered rigidbodies toward the planet centre every physics step. REQ: ADR-0009 §2,
    /// docs/architecture/planet-tech.md — Physics.gravity has no meaning on a sphere.
    /// </summary>
    /// <remarks>
    /// Pre: <see cref="Configure"/> before the first FixedUpdate; main thread. Post: per member.
    /// Side effects: adds forces to registered bodies. One instance serves every body, so thousands of debris pieces cost
    /// one component rather than one FixedUpdate each.
    /// </remarks>
    public sealed class PlanetGravity : MonoBehaviour
    {
        private readonly List<Rigidbody> _bodies = new List<Rigidbody>(64);
        private PlanetConfig _planet;
        private float _acceleration;
        private bool _configured;

        /// <summary>Gravity acceleration (m/s²) at <paramref name="position"/>: magnitude g, toward the centre.</summary>
        /// <remarks>
        /// Pre: position finite and not at the centre; g finite and &gt;= 0.
        /// Post: |result| == g (±1e-4·g); result is anti-parallel to the local up of PlanetFrame. Side effects: none.
        /// Uniform magnitude, not 1/r²: objects only fall a few metres, and the character's motor uses the same constant.
        /// </remarks>
        public static float3 AccelerationAt(in PlanetConfig planet, float3 position, float g)
        {
            Contract.Requires(math.all(math.isfinite(position)), "position must be finite");
            Contract.Requires(math.isfinite(g) && g >= 0f, "g must be finite and >= 0");
            Contract.Requires(math.lengthsq(position - planet.Center) > 1e-12f, "position must not be the planet centre");
            return -planet.ToDirection(position) * g;
        }

        /// <summary>Sets the planet and gravity magnitude.</summary>
        /// <remarks>
        /// Pre: g finite and &gt; 0. Post: subsequent physics steps use them. Side effects: stores the values.
        /// </remarks>
        public void Configure(in PlanetConfig planet, float g)
        {
            Contract.Requires(math.isfinite(g) && g > 0f, "g must be finite and > 0");
            _planet = planet;
            _acceleration = g;
            _configured = true;
        }

        /// <summary>Number of registered bodies (destroyed ones are pruned on the next physics step).</summary>
        /// <remarks>Pre: none. Post: &gt;= 0. Side effects: none.</remarks>
        public int Count => _bodies.Count;

        /// <summary>Starts pulling <paramref name="body"/>.</summary>
        /// <remarks>
        /// Pre: body non-null. Post: body.useGravity == false (Physics.gravity would add a flat-world pull); registered once.
        /// Side effects: mutates body.useGravity.
        /// </remarks>
        public void Register(Rigidbody body)
        {
            Contract.Requires(body != null, "body must not be null");
            body.useGravity = false;
            if (!_bodies.Contains(body))
            {
                _bodies.Add(body);
            }
        }

        /// <summary>Stops pulling <paramref name="body"/>.</summary>
        /// <remarks>Pre: none (null or unknown bodies are ignored). Post: not registered. Side effects: none beyond that.</remarks>
        public void Unregister(Rigidbody body) => _bodies.Remove(body);

        private void FixedUpdate()
        {
            if (!_configured)
            {
                return;
            }

            for (int i = _bodies.Count - 1; i >= 0; i--)
            {
                Rigidbody b = _bodies[i];
                if (b == null)
                {
                    _bodies.RemoveAt(i);
                    continue;
                }

                // Sleeping bodies stay asleep: AddForce would wake every resting piece every step.
                if (b.isKinematic || b.IsSleeping() || !b.gameObject.activeInHierarchy)
                {
                    continue;
                }

                b.AddForce((Vector3)AccelerationAt(_planet, b.worldCenterOfMass, _acceleration), ForceMode.Acceleration);
            }
        }
    }
}
