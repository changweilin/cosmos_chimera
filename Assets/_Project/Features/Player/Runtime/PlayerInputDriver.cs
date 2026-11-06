using Cosmimera.Core.Contracts;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Cosmimera.Player
{
    /// <summary>Reads the Player action map and forwards intent to <see cref="SphericalCharacter"/> and <see cref="PlayerCameraRig"/>.</summary>
    /// <remarks>
    /// Pre: <see cref="Configure"/> once; the asset has a "Player" map with Move, Look, Jump and Sprint.
    /// Post: while enabled, every Update pushes the latest input; nothing else reads devices for the player.
    /// Side effects: enables/disables the actions it uses.
    /// </remarks>
    public sealed class PlayerInputDriver : MonoBehaviour
    {
        // The shared actions asset has no view-toggle action yet; an embedded action keeps that asset untouched until
        // P7 key rebinding decides where toggles live.
        [SerializeField] private InputAction _toggleView = new InputAction("ToggleView", InputActionType.Button, "<Keyboard>/v");

        private SphericalCharacter _character;
        private PlayerCameraRig _rig;
        private InputAction _move;
        private InputAction _look;
        private InputAction _jump;
        private InputAction _sprint;

        /// <summary>Binds actions and targets.</summary>
        /// <remarks>
        /// Pre: actions, character, rig non-null; actions contains Player/Move, Player/Look, Player/Jump, Player/Sprint.
        /// Post: actions enabled. Side effects: enables input actions.
        /// </remarks>
        public void Configure(InputActionAsset actions, SphericalCharacter character, PlayerCameraRig rig)
        {
            Contract.Requires(actions != null && character != null && rig != null, "all references are required");
            _character = character;
            _rig = rig;
            _move = actions.FindAction("Player/Move");
            _look = actions.FindAction("Player/Look");
            _jump = actions.FindAction("Player/Jump");
            _sprint = actions.FindAction("Player/Sprint");
            Contract.Requires(_move != null && _look != null && _jump != null && _sprint != null,
                "actions must contain Player/Move, Player/Look, Player/Jump and Player/Sprint");
            if (_toggleView.bindings.Count == 1)
            {
                _toggleView.AddBinding("<Gamepad>/rightStickPress");
            }

            SetEnabled(true);
        }

        private void OnEnable() => SetEnabled(true);

        private void OnDisable() => SetEnabled(false);

        private void SetEnabled(bool enabled)
        {
            if (_move == null)
            {
                return;
            }

            foreach (InputAction action in new[] { _move, _look, _jump, _sprint, _toggleView })
            {
                if (enabled)
                {
                    action.Enable();
                }
                else
                {
                    action.Disable();
                }
            }
        }

        private void Update()
        {
            if (_move == null)
            {
                return;
            }

            Vector2 look = _look.ReadValue<Vector2>();
            // Mouse delta is already per-frame; stick deflection must be scaled by time. One action carries both.
            bool pointer = _look.activeControl?.device is Pointer;
            _rig.AddLook(pointer ? look : Vector2.zero, pointer ? Vector2.zero : look, Time.deltaTime);
            if (_toggleView.WasPressedThisFrame())
            {
                _rig.ToggleView();
            }

            _character.SetInput(_move.ReadValue<Vector2>(), _rig.Heading, _sprint.IsPressed(), _jump.WasPressedThisFrame());
        }
    }
}
