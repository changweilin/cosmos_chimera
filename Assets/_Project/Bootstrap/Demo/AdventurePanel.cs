using System;
using System.Collections.Generic;
using Cosmimera.Bootstrap.Demo.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Cosmimera.Bootstrap.Demo
{
    /// <summary>
    /// UI Toolkit-backed Adventurer's Handbook &amp; Backpack panel (Partners / Items / Skills / Codex).
    /// Preserves the immediate-mode builder API used by DemoGame, RunSession, CraftJournal, and CodexPanel
    /// while rendering into a styled UIDocument instead of legacy IMGUI.
    /// </summary>
    internal sealed class AdventurePanel : IDisposable
    {
        private static readonly string[] NavigationActions = { "Up", "Down", "Left", "Right" };
        internal enum Page { Partners, Items, Skills, Codex, System }

        private readonly DemoCopy _copy;
        private readonly MenuFocus _focus = new MenuFocus();
        private int _count;
        private bool _submit;
        private bool _scrollToFocus;
        private bool _editingText;
        private bool _clearTextFocus;
        private Vector2 _scroll;

        private UIDocument _doc;
        private VisualElement _root;
        private Button[] _tabButtons;
        private Button _closeButton;
        private Label _navHintLabel;
        private Label _messageBanner;
        private ScrollView _contentScroll;
        private VisualElement _currentSection;

        private readonly List<Button> _renderedButtons = new List<Button>();
        private readonly Dictionary<string, TextField> _textFields = new Dictionary<string, TextField>();
        private readonly Dictionary<string, string> _pendingTextEdits = new Dictionary<string, string>();
        private int _clickedButtonIndex = -1;
        private int _clickedTabIndex = -1;
        private bool _closeRequested;
        private bool _dirty = true;
        private Action _onClose;

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
            if (Current != page)
            {
                Current = page;
                Message = string.Empty;
                _focus.SetCount(0);
                _dirty = true;
                if (_contentScroll != null)
                {
                    _contentScroll.scrollOffset = Vector2.zero;
                }
            }
        }

        internal void MarkDirty() => _dirty = true;

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
                    if (input.FindAction("Submit").WasPressedThisFrame())
                    {
                        _clearTextFocus = true;
                        _editingText = false;
                    }
                    return;
                }
            }

            if (input.FindAction("Up").WasPressedThisFrame() || input.FindAction("Left")?.WasPressedThisFrame() == true)
            {
                _focus.Move(-1);
                _scrollToFocus = true;
                _clearTextFocus = true;
                UpdateFocusHighlight();
            }
            else if (input.FindAction("Down").WasPressedThisFrame() || input.FindAction("Right")?.WasPressedThisFrame() == true)
            {
                _focus.Move(1);
                _scrollToFocus = true;
                _clearTextFocus = true;
                UpdateFocusHighlight();
            }

            if (input.FindAction("Submit").WasPressedThisFrame())
            {
                _submit = true;
                _dirty = true;
            }
        }

        /// <summary>Synchronizes the UI Toolkit document with the current page content.</summary>
        internal void Draw(Action<AdventurePanel> content, Action close)
        {
            EnsureDocument(close);

            // Handle tab switch or close requested by pointer click.
            if (_closeRequested)
            {
                _closeRequested = false;
                close();
                return;
            }

            if (_clickedTabIndex >= 0)
            {
                Page next = (Page)_clickedTabIndex;
                _clickedTabIndex = -1;
                Current = next;
                Message = string.Empty;
                _focus.SetCount(0);
                _contentScroll.scrollOffset = Vector2.zero;
                _dirty = true;
            }

            // Only rebuild when dirty, when a button/submit occurred, or when text changed.
            // Note: on first open _dirty is true; subsequent frames stay lightweight until interaction.
            if (!_dirty && _clickedButtonIndex < 0 && !_submit && _pendingTextEdits.Count == 0)
            {
                if (_clearTextFocus)
                {
                    foreach (TextField tf in _textFields.Values)
                    {
                        tf.Blur();
                    }
                    _clearTextFocus = false;
                }
                return;
            }

            _dirty = false;
            int activeClick = _clickedButtonIndex;
            _clickedButtonIndex = -1;

            RebuildTree(content, activeClick);

            // If a button click or submit triggered an action, run one more pass immediately so state reflects it.
            if (activeClick >= 0 || _submit)
            {
                _submit = false;
                RebuildTree(content, -1);
            }
        }

        private void RebuildTree(Action<AdventurePanel> content, int activeClick)
        {
            UpdateHeaderState();
            _count = 0;
            _renderedButtons.Clear();
            _currentSection = null;
            _rowContainer = null;

            // Preserve any active TextField so typing does not lose focus or cursor position.
            string focusedFieldName = null;
            foreach (KeyValuePair<string, TextField> kv in _textFields)
            {
                if (kv.Value.panel != null && kv.Value.focusController?.focusedElement == kv.Value)
                {
                    focusedFieldName = kv.Key;
                    break;
                }
            }

            _contentScroll.Clear();
            _activeClickIndex = activeClick;
            try
            {
                content(this);
            }
            catch (ExitGUIException)
            {
                // Legacy callers (e.g. CodexPanel) may call GUIUtility.ExitGUI() after mutating state.
                _dirty = true;
            }
            _activeClickIndex = -1;

            _focus.SetCount(_count);
            UpdateFocusHighlight();

            if (!string.IsNullOrEmpty(focusedFieldName) && _textFields.TryGetValue(focusedFieldName, out TextField activeTf))
            {
                activeTf.Focus();
            }
        }

        private int _activeClickIndex = -1;

        private void EnsureDocument(Action close)
        {
            _onClose = close;
            if (_doc != null)
            {
                return;
            }

            _doc = UitkRuntimeHost.CreateDocument("AdventureHandbookUI", "UI/AdventureHandbook", 200);
            _root = _doc.rootVisualElement;
            _tabButtons = new[]
            {
                _root.Q<Button>("tabPartners"),
                _root.Q<Button>("tabItems"),
                _root.Q<Button>("tabSkills"),
                _root.Q<Button>("tabCodex"),
                _root.Q<Button>("tabSystem"),
            };

            for (int i = 0; i < _tabButtons.Length; i++)
            {
                int idx = i;
                _tabButtons[i].clicked += () => { _clickedTabIndex = idx; _dirty = true; };
            }

            _closeButton = _root.Q<Button>("closeButton");
            _closeButton.clicked += () => { _closeRequested = true; };

            _navHintLabel = _root.Q<Label>("navHintLabel");
            _messageBanner = _root.Q<Label>("messageBanner");
            _contentScroll = _root.Q<ScrollView>("contentScroll");
            _dirty = true;
        }

        private void UpdateHeaderState()
        {
            for (int i = 0; i < _tabButtons.Length; i++)
            {
                Page page = (Page)i;
                _tabButtons[i].text = _copy.Ui("ui.journal.tab." + page);
                if (Current == page)
                {
                    _tabButtons[i].AddToClassList("tab-btn-active");
                }
                else
                {
                    _tabButtons[i].RemoveFromClassList("tab-btn-active");
                }
            }

            _closeButton.text = _copy.Ui("ui.journal.close");
            _navHintLabel.text = _copy.Ui("ui.journal.navigation");

            if (string.IsNullOrEmpty(Message))
            {
                _messageBanner.AddToClassList("hidden");
            }
            else
            {
                _messageBanner.text = Message;
                _messageBanner.RemoveFromClassList("hidden");
            }
        }

        private VisualElement _rowContainer;

        private VisualElement CurrentContainer()
        {
            if (_rowContainer != null)
            {
                return _rowContainer;
            }

            if (_currentSection != null)
            {
                return _currentSection;
            }

            _currentSection = new VisualElement();
            _currentSection.AddToClassList("handbook-section");
            _contentScroll.Add(_currentSection);
            return _currentSection;
        }

        internal void BeginRow()
        {
            var row = new VisualElement();
            row.AddToClassList("handbook-row");
            CurrentContainer().Add(row);
            _rowContainer = row;
        }

        internal void EndRow()
        {
            _rowContainer = null;
        }

        internal void Image(Texture texture, float width, float height)
        {
            if (texture == null) return;
            var img = new Image { image = texture, scaleMode = ScaleMode.ScaleToFit };
            img.style.width = width;
            img.style.height = height;
            CurrentContainer().Add(img);
        }

        private void UpdateFocusHighlight()
        {
            for (int i = 0; i < _renderedButtons.Count; i++)
            {
                if (i == _focus.Index)
                {
                    _renderedButtons[i].AddToClassList("btn-focused");
                    if (_scrollToFocus && _contentScroll != null)
                    {
                        _contentScroll.ScrollTo(_renderedButtons[i]);
                    }
                }
                else
                {
                    _renderedButtons[i].RemoveFromClassList("btn-focused");
                }
            }
            _scrollToFocus = false;
        }

        internal void Label(string value)
        {
            var lbl = new Label(value);
            lbl.AddToClassList("handbook-item-label");
            CurrentContainer().Add(lbl);
        }

        internal void Heading(string value)
        {
            _currentSection = new VisualElement();
            _currentSection.AddToClassList("handbook-section");
            _contentScroll.Add(_currentSection);

            var lbl = new Label(value);
            lbl.AddToClassList("heading-lg");
            _currentSection.Add(lbl);
        }

        internal string TextField(string name, string value)
        {
            if (!_textFields.TryGetValue(name, out TextField tf))
            {
                tf = new TextField { name = name };
                tf.AddToClassList("text-input");
                tf.RegisterValueChangedCallback(evt =>
                {
                    _pendingTextEdits[name] = evt.newValue;
                    _dirty = true;
                });
                tf.RegisterCallback<FocusInEvent>(_ => _editingText = true);
                tf.RegisterCallback<FocusOutEvent>(_ => _editingText = false);
                _textFields[name] = tf;
            }

            if (_pendingTextEdits.TryGetValue(name, out string edited))
            {
                _pendingTextEdits.Remove(name);
                value = edited;
            }

            tf.SetValueWithoutNotify(value ?? string.Empty);
            CurrentContainer().Add(tf);
            return value ?? string.Empty;
        }

        internal bool Button(string label, bool enabled = true)
        {
            int index = enabled ? _count++ : -1;
            var btn = new Button();
            btn.text = label;
            btn.AddToClassList("btn");
            btn.AddToClassList("handbook-action-btn");
            if (enabled)
            {
                btn.AddToClassList("btn-primary");
            }
            btn.SetEnabled(enabled);

            if (index >= 0)
            {
                int capturedIndex = index;
                btn.clicked += () =>
                {
                    _clickedButtonIndex = capturedIndex;
                    _dirty = true;
                };
                _renderedButtons.Add(btn);
            }

            CurrentContainer().Add(btn);

            bool clicked = index >= 0 && index == _activeClickIndex;
            bool submitted = index >= 0 && index == _focus.Index && _submit;
            return clicked || submitted;
        }

        public void Dispose()
        {
            if (_doc != null)
            {
                UnityEngine.Object.Destroy(_doc.gameObject);
                _doc = null;
                _root = null;
            }
            _textFields.Clear();
            _renderedButtons.Clear();
            _pendingTextEdits.Clear();
        }
    }
}
