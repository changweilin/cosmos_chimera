using Cosmimera.Core.Contracts;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Cosmimera.Player
{
    /// <summary>Reads the Player action map and forwards intent to <see cref="SphericalCharacter"/> and <see cref="PlayerCameraRig"/>.</summary>
    /// <remarks>
    /// Pre: <see cref="Configure"/> once; the asset has a "Player" map with Move, Look, Jump, Sprint, ToggleView and CommandModifier.
    /// Post: while enabled, every Update pushes the latest input; nothing else reads devices for the player.
    /// Side effects: enables/disables the actions it uses.
    /// </remarks>
    public sealed class PlayerInputDriver : MonoBehaviour
    {
        private SphericalCharacter _character;
        private PlayerCameraRig _rig;
        private InputAction _move;
        private InputAction _look;
        private InputAction _jump;
        private InputAction _sprint;
        private InputAction _toggleView;
        private InputAction _commandModifier;

        /// <summary>When true, Move/Jump/Sprint are not forwarded to SphericalCharacter (e.g. direct creature control).</summary>
        /// <remarks>Pre: none. Post: none. Side effects: none.</remarks>
        public bool SuppressCharacterInput { get; set; }

        /// <summary>Current movement input vector from Player/Move.</summary>
        /// <remarks>Pre: none. Post: finite 2D vector. Side effects: none.</remarks>
        public Vector2 MoveInput => _move != null ? _move.ReadValue<Vector2>() : Vector2.zero;

        /// <summary>True on frames where Player/Jump was pressed.</summary>
        /// <remarks>Pre: none. Post: none. Side effects: none.</remarks>
        public bool JumpPressedThisFrame => _jump != null && _jump.WasPressedThisFrame();

        /// <summary>Binds actions and targets.</summary>
        /// <remarks>
        /// Pre: actions, character, rig non-null; actions contains Player/Move, Player/Look, Player/Jump, Player/Sprint,
        /// Player/ToggleView and Player/CommandModifier.
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
            _toggleView = actions.FindAction("Player/ToggleView");
            _commandModifier = actions.FindAction("Player/CommandModifier");
            Contract.Requires(_move != null && _look != null && _jump != null && _sprint != null &&
                _toggleView != null && _commandModifier != null,
                "actions must contain Player/Move, Player/Look, Player/Jump, Player/Sprint, Player/ToggleView and Player/CommandModifier");

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

            foreach (InputAction action in new[] { _move, _look, _jump, _sprint, _toggleView, _commandModifier })
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
            // Player and run maps have independent owners; a command chord must not also toggle the camera.
            if (_toggleView.WasPressedThisFrame() &&
                (!(_toggleView.activeControl?.device is Gamepad) || !_commandModifier.IsPressed()))
            {
                _rig.ToggleView();
            }

            if (!SuppressCharacterInput)
            {
                _character.SetInput(_move.ReadValue<Vector2>(), _rig.Heading, _sprint.IsPressed(), _jump.WasPressedThisFrame());
            }
        }
    }
}
