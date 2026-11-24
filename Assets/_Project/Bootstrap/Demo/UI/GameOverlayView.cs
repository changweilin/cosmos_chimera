using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Cosmimera.Bootstrap.Demo.UI
{
    /// <summary>
    /// UI Toolkit controller for full-screen modal overlays (Results, Star Select, Quick Menu).
    /// </summary>
    internal sealed class GameOverlayView : IDisposable
    {
        private readonly UIDocument _doc;
        private readonly VisualElement _backdrop;
        private readonly Label _titleLabel;
        private readonly Button _headerActionBtn;
        private readonly Label _subtitleLabel;
        private readonly ScrollView _bodyScroll;
        private readonly VisualElement _footer;

        internal GameOverlayView(Transform parent = null)
        {
            _doc = UitkRuntimeHost.CreateDocument("GameOverlayUI", "UI/GameOverlay", 150, parent);
            VisualElement root = _doc.rootVisualElement;
            _backdrop = root.Q<VisualElement>("backdrop");
            _titleLabel = root.Q<Label>("titleLabel");
            _headerActionBtn = root.Q<Button>("headerActionBtn");
            _subtitleLabel = root.Q<Label>("subtitleLabel");
            _bodyScroll = root.Q<ScrollView>("bodyScroll");
            _footer = root.Q<VisualElement>("footer");
        }

        internal bool IsVisible => _backdrop != null && !_backdrop.ClassListContains("hidden");

        internal void Hide()
        {
            _backdrop.AddToClassList("hidden");
        }

        internal void Show(string title, string subtitle = null, string headerActionLabel = null, Action onHeaderAction = null)
        {
            _backdrop.RemoveFromClassList("hidden");
            _titleLabel.text = title ?? string.Empty;

            if (string.IsNullOrEmpty(subtitle))
            {
                _subtitleLabel.AddToClassList("hidden");
            }
            else
            {
                _subtitleLabel.text = subtitle;
                _subtitleLabel.RemoveFromClassList("hidden");
            }

            _headerActionBtn.clickable = null;
            if (string.IsNullOrEmpty(headerActionLabel))
            {
                _headerActionBtn.AddToClassList("hidden");
            }
            else
            {
                _headerActionBtn.text = headerActionLabel;
                _headerActionBtn.RemoveFromClassList("hidden");
                _headerActionBtn.clicked += () => onHeaderAction?.Invoke();
            }

            _bodyScroll.Clear();
            _footer.Clear();
            _footer.AddToClassList("hidden");
        }

        internal void AddCard(string heading, string body, string buttonLabel = null, Action onClick = null, bool primary = true)
        {
            var card = new VisualElement();
            card.AddToClassList("card");
            if (!string.IsNullOrEmpty(heading))
            {
                var h = new Label(heading);
                h.AddToClassList("card-title");
                card.Add(h);
            }
            if (!string.IsNullOrEmpty(body))
            {
                var b = new Label(body);
                b.AddToClassList("body-text");
                card.Add(b);
            }
            if (!string.IsNullOrEmpty(buttonLabel))
            {
                var btn = new Button(() => onClick?.Invoke()) { text = buttonLabel };
                btn.AddToClassList("btn");
                btn.AddToClassList(primary ? "btn-primary" : "btn-accent");
                card.Add(btn);
            }
            _bodyScroll.Add(card);
        }

        internal void AddCardWithImage(string heading, string body, Texture2D portrait, string buttonLabel = null,
            Action onClick = null, bool primary = true, bool enabled = true)
        {
            var card = new VisualElement();
            card.AddToClassList("card");
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            if (portrait != null)
            {
                var img = new Image { image = portrait, scaleMode = ScaleMode.ScaleToFit };
                img.style.width = 72f;
                img.style.height = 72f;
                img.style.marginRight = 12f;
                row.Add(img);
            }
            var textCol = new VisualElement();
            textCol.style.flexGrow = 1f;
            if (!string.IsNullOrEmpty(heading))
            {
                var h = new Label(heading);
                h.AddToClassList("card-title");
                h.AddToClassList("heading-lg");
                textCol.Add(h);
            }
            if (!string.IsNullOrEmpty(body))
            {
                var b = new Label(body);
                b.AddToClassList("body-text");
                textCol.Add(b);
            }
            row.Add(textCol);
            card.Add(row);

            if (!string.IsNullOrEmpty(buttonLabel))
            {
                var btn = new Button(() => onClick?.Invoke()) { text = buttonLabel };
                btn.AddToClassList("btn");
                btn.AddToClassList(primary ? "btn-primary" : "btn-accent");
                btn.style.marginTop = 8f;
                btn.SetEnabled(enabled);
                card.Add(btn);
            }
            _bodyScroll.Add(card);
        }

        internal void AddActionCard(string heading, string body, params (string label, Action onClick, bool enabled, bool primary)[] actions)
        {
            var card = new VisualElement();
            card.AddToClassList("card");
            if (!string.IsNullOrEmpty(heading))
            {
                var h = new Label(heading);
                h.AddToClassList("heading-lg");
                card.Add(h);
            }
            if (!string.IsNullOrEmpty(body))
            {
                var b = new Label(body);
                b.AddToClassList("body-text");
                card.Add(b);
            }
            if (actions != null && actions.Length > 0)
            {
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.flexWrap = Wrap.Wrap;
                row.style.marginTop = 8f;
                for (int i = 0; i < actions.Length; i++)
                {
                    (string label, Action onClick, bool enabled, bool primary) act = actions[i];
                    var btn = new Button(() => act.onClick?.Invoke()) { text = act.label };
                    btn.AddToClassList("btn");
                    btn.AddToClassList(act.primary ? "btn-primary" : "btn-accent");
                    btn.style.marginRight = 8f;
                    btn.SetEnabled(act.enabled);
                    row.Add(btn);
                }
                card.Add(row);
            }
            _bodyScroll.Add(card);
        }

        internal void AddSeedInputCard(
            string heading,
            string body,
            string seedValue,
            Action<string> onSeedChanged,
            string cyclePlanetLabel,
            Action onCyclePlanet,
            string launchLabel,
            Action onLaunch)
        {
            var card = new VisualElement();
            card.AddToClassList("card-accent");
            if (!string.IsNullOrEmpty(heading))
            {
                var h = new Label(heading);
                h.AddToClassList("heading-lg");
                card.Add(h);
            }
            if (!string.IsNullOrEmpty(body))
            {
                var b = new Label(body);
                b.AddToClassList("body-text");
                card.Add(b);
            }
            var tf = new TextField { value = seedValue ?? string.Empty };
            tf.AddToClassList("text-input");
            tf.RegisterValueChangedCallback(evt => onSeedChanged?.Invoke(evt.newValue));
            card.Add(tf);

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.flexWrap = Wrap.Wrap;
            var cycleBtn = new Button(() => onCyclePlanet?.Invoke()) { text = cyclePlanetLabel };
            cycleBtn.AddToClassList("btn");
            cycleBtn.style.marginRight = 8f;
            row.Add(cycleBtn);

            var launchBtn = new Button(() => onLaunch?.Invoke()) { text = launchLabel };
            launchBtn.AddToClassList("btn");
            launchBtn.AddToClassList("btn-primary");
            row.Add(launchBtn);

            card.Add(row);
            _bodyScroll.Add(card);
        }

        internal void AddFooterButton(string label, Action onClick, bool primary = false)
        {
            _footer.RemoveFromClassList("hidden");
            var btn = new Button(() => onClick?.Invoke()) { text = label };
            btn.AddToClassList("btn");
            btn.AddToClassList("overlay-footer-btn");
            if (primary) btn.AddToClassList("btn-primary");
            _footer.Add(btn);
        }

        public void Dispose()
        {
            if (_doc != null)
            {
                UnityEngine.Object.Destroy(_doc.gameObject);
            }
        }
    }
}
