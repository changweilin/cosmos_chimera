using Cosmimera.Player;
using UnityEngine;

namespace Cosmimera.Bootstrap.Demo
{
    /// <summary>Drives a character Animator's locomotion parameters from <see cref="SphericalCharacter"/>.</summary>
    /// <remarks>
    /// Pre: <see cref="Configure"/> is called with non-null character and animator before Update runs.
    /// Post: updates Speed (tangent speed m/s) and IsGrounded each frame.
    /// Side effects: mutates Animator float/bool parameters only.
    /// </remarks>
    public sealed class CharacterAnimatorDriver : MonoBehaviour
    {
        private static readonly int SpeedHash = Animator.StringToHash("Speed");
        private static readonly int GroundedHash = Animator.StringToHash("IsGrounded");

        private SphericalCharacter _character;
        private Animator _animator;

        /// <summary>Binds the driver to <paramref name="character"/> and <paramref name="animator"/>.</summary>
        /// <remarks>
        /// Pre: character != null; animator != null.
        /// Post: driver is ready to update animator parameters on each Update.
        /// Side effects: stores references.
        /// </remarks>
        public void Configure(SphericalCharacter character, Animator animator)
        {
            Core.Contracts.Contract.Requires(character != null && animator != null,
                "character and animator are required");
            _character = character;
            _animator = animator;
        }

        private void Update()
        {
            if (_character == null || _animator == null)
            {
                return;
            }

            // Project onto the local tangent plane using transform.up (planet up) rather than Vector3.up.
            Vector3 tangentVelocity = Vector3.ProjectOnPlane(_character.Velocity, transform.up);
            _animator.SetFloat(SpeedHash, tangentVelocity.magnitude);
            _animator.SetBool(GroundedHash, _character.IsGrounded);
        }
    }
}
