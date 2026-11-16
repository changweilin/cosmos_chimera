using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Cosmimera.Bootstrap.Demo
{
    internal sealed class AdventurePanel
    {
        private static readonly string[] NavigationActions = { "Up", "Down", "Left", "Right" };
        internal enum Page { Partners, Items, Skills, Codex }

        private readonly DemoCopy _copy;
        private readonly MenuFocus _focus = new MenuFocus();
        private int _count;
        private bool _submit;
        private bool _scrollToFocus;
        private Vector2 _scroll;
        private GUIStyle _text;
        private GUIStyle _heading;
        private GUIStyle _button;
        private float _scale = 1;
        private bool _editingText;
        private bool _clearTextFocus;
        internal Page Current { get; private set; }
        internal bool EditingText => _editingText;
        internal string Message { get; set; } = string.Empty;

        internal AdventurePanel(DemoCopy copy, Page page = Page.Partners)
        {
            _copy = copy;
            Current = page;
        }

        internal void SetPage(Page page)
        {
            Current = page;
            _scroll = Vector2.zero;
            Message = string.Empty;
            _focus.SetCount(4);
        }

        internal void ReadInput(InputActionMap input)
        {
            if (_editingText)
            {
                bool padNavigation = false;
                foreach (string name in NavigationActions)
                {
                    InputAction action = input.FindAction(name);
                    padNavigation |= action != null && action.WasPressedThisFrame() && action.activeControl?.device is Gamepad;
                }
                if (!padNavigation)
                {
                    if (input.FindAction("Submit").WasPressedThisFrame()) _clearTextFocus = true;
                    return;
                }
            }
            if (input.FindAction("Up").WasPressedThisFrame() || input.FindAction("Left")?.WasPressedThisFrame() == true)
            {
                _focus.Move(-1);
                _scrollToFocus = true;
                _clearTextFocus = true;
            }
            else if (input.FindAction("Down").WasPressedThisFrame() || input.FindAction("Right")?.WasPressedThisFrame() == true)
            {
                _focus.Move(1);
                _scrollToFocus = true;
                _clearTextFocus = true;
            }
            _submit |= input.FindAction("Submit").WasPressedThisFrame();
        }

        internal void Draw(Action<AdventurePanel> content, Action close)
        {
            Matrix4x4 matrix = GUI.matrix;
            _scale = Mathf.Max(1, Mathf.Min(Screen.width / 1280f, Screen.height / 720f));
            GUI.matrix = matrix * Matrix4x4.Scale(new Vector3(_scale, _scale, 1));
            try { DrawContent(content, close); }
            finally { GUI.matrix = matrix; }
        }

        private void DrawContent(Action<AdventurePanel> content, Action close)
        {
            if (_clearTextFocus)
            {
                GUI.FocusControl(null);
                _clearTextFocus = false;
            }
            _editingText = false;
            _text ??= new GUIStyle(GUI.skin.label) { fontSize = 16, wordWrap = true };
            _heading ??= new GUIStyle(_text) { fontSize = 24, fontStyle = FontStyle.Bold };
            _button ??= new GUIStyle(GUI.skin.button) { fontSize = 16, wordWrap = true };
            float width = Mathf.Min(1000, Screen.width / _scale - 24);
            var bounds = new Rect((Screen.width / _scale - width) / 2, 12, width, Screen.height / _scale - 24);
            Color color = GUI.color;
            GUI.color = new Color(0.025f, 0.035f, 0.055f, 0.98f);
            GUI.DrawTexture(bounds, Texture2D.whiteTexture);
            GUI.color = color;
            GUI.Box(bounds, GUIContent.none);
            GUILayout.BeginArea(new Rect(bounds.x + 14, bounds.y + 10, bounds.width - 28, bounds.height - 20));
            _count = 0;
            if (Event.current.type == EventType.MouseDown || Event.current.type == EventType.MouseUp) _submit = false;
            GUILayout.BeginHorizontal();
            for (int i = 0; i < 4; i++)
            {
                Page page = (Page)i;
                if (Button((Current == page ? "● " : "") + _copy.Ui("ui.journal.tab." + page)))
                {
                    Current = page;
                    _scroll = Vector2.zero;
                    Message = string.Empty;
                    _focus.SetCount(4);
                    GUIUtility.ExitGUI();
                }
            }
            if (Button(_copy.Ui("ui.journal.close")))
            {
                close();
                GUIUtility.ExitGUI();
            }
            GUILayout.EndHorizontal();
            Label(_copy.Ui("ui.journal.navigation"));
            if (!string.IsNullOrEmpty(Message)) Label(Message);
            _scroll = GUILayout.BeginScrollView(_scroll);
            content(this);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
            if (Event.current.type == EventType.Repaint)
            {
                _focus.SetCount(_count);
                _submit = false;
                _scrollToFocus = false;
            }
        }

        internal void Label(string value) => GUILayout.Label(value, _text);
        internal void Heading(string value) => GUILayout.Label(value, _heading);

        internal string TextField(string name, string value)
        {
            GUI.SetNextControlName(name);
            string edited = GUILayout.TextField(value, GUILayout.MinHeight(30));
            _editingText = GUI.GetNameOfFocusedControl() == name;
            return edited;
        }

        internal bool Button(string label, bool enabled = true)
        {
            bool previous = GUI.enabled;
            GUI.enabled = previous && enabled;
            int index = GUI.enabled ? _count++ : -1;
            bool focused = index >= 0 && index == _focus.Index;
            Color color = GUI.backgroundColor;
            if (focused) GUI.backgroundColor = new Color(0.55f, 0.85f, 1f);
            bool clicked = GUILayout.Button((focused ? "> " : "") + label, _button, GUILayout.MinHeight(36));
            GUI.backgroundColor = color;
            GUI.enabled = previous;
            if (focused && _scrollToFocus && Event.current.type == EventType.Repaint)
                GUI.ScrollTo(GUILayoutUtility.GetLastRect());
            bool confirmed = focused && _submit && Event.current.type == EventType.Repaint;
            if (clicked || confirmed) _submit = false;
            return clicked || confirmed;
        }
    }
}
