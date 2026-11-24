using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;

namespace Cosmimera.Bootstrap.Demo
{
    internal sealed class InputHints : IDisposable
    {
        private readonly InputActionMap[] _maps;
        private readonly Dictionary<InputAction, string> _kbLabels = new Dictionary<InputAction, string>(16);
        private readonly Dictionary<InputAction, string> _padLabels = new Dictionary<InputAction, string>(16);
        private InputDevice _lastDevice;

        internal InputHints(params InputActionMap[] maps)
        {
            _maps = maps;
            InputSystem.onEvent += ReadInput;
            InputSystem.onDeviceChange += DeviceChanged;
            InputSystem.onActionChange += ActionChanged;
        }

        internal bool UsesGamepad => _lastDevice is Gamepad && _lastDevice.added && _lastDevice.enabled;

        internal void Inherit(InputHints previous) => _lastDevice = previous._lastDevice;

        internal string BindingLabel(InputAction action)
        {
            if (action == null) return string.Empty;
            bool pad = UsesGamepad;
            Dictionary<InputAction, string> cache = pad ? _padLabels : _kbLabels;
            if (!cache.TryGetValue(action, out string label))
            {
                label = action.GetBindingDisplayString(group: pad ? "Gamepad" : "Keyboard&Mouse");
                cache[action] = label;
            }

            return label;
        }

        private void ReadInput(InputEventPtr input, InputDevice device)
        {
            if (input.handled || !device.enabled || !(device is Gamepad || device is Keyboard || device is Mouse) ||
                !(input.IsA<StateEvent>() || input.IsA<DeltaStateEvent>())) return;

            // Action callbacks omit a second device's press while an equally strong control is held on the first.
            // Only meaningful changes to enabled bindings count; releases and stick jitter cannot steal prompts.
            foreach (InputControl control in input.EnumerateChangedControls(device, magnitudeThreshold: 0.25f))
            {
                if (control is AxisControl axis &&
                    Mathf.Abs(axis.ReadValueFromEvent(input) - axis.ReadValue()) < 0.1f) continue;
                if (!IsBound(control)) continue;
                _lastDevice = device;
                return;
            }
        }

        private bool IsBound(InputControl control)
        {
            foreach (InputActionMap map in _maps)
            {
                if (!map.enabled) continue;
                // IMGUI consumes pointer clicks and scrolling without an InputAction binding.
                if (control.device is Mouse mouse && (control == mouse.leftButton || control.parent == mouse.scroll)) return true;
                foreach (InputAction action in map.actions)
                {
                    if (!action.enabled) continue;
                    foreach (InputControl binding in action.controls)
                        for (InputControl parent = control; parent != null; parent = parent.parent)
                            if (parent == binding) return true;
                }
            }
            return false;
        }

        private void DeviceChanged(InputDevice device, InputDeviceChange change)
        {
            if (device == _lastDevice && (change == InputDeviceChange.Removed ||
                change == InputDeviceChange.Disconnected || change == InputDeviceChange.Disabled)) _lastDevice = null;
        }

        private void ActionChanged(object obj, InputActionChange change)
        {
            if (change == InputActionChange.BoundControlsChanged)
            {
                _kbLabels.Clear();
                _padLabels.Clear();
            }
        }

        /// <summary>Stops observing the owned session's actions.</summary>
        /// <remarks>Pre: none. Post: subsequent input cannot change hints. Side effects: removes input callbacks.</remarks>
        public void Dispose()
        {
            InputSystem.onEvent -= ReadInput;
            InputSystem.onDeviceChange -= DeviceChanged;
            InputSystem.onActionChange -= ActionChanged;
        }
    }
}
