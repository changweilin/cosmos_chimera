using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Cosmimera.Bootstrap.Demo.UI
{
    /// <summary>
    /// UI Toolkit controller for the in-game exploration &amp; combat HUD (GameHud.uxml).
    /// </summary>
    internal sealed class GameHudView : IDisposable
    {
        internal readonly struct TeamMemberDisplay
        {
            internal readonly string Name;
            internal readonly int Hp;
            internal readonly int MaxHp;
            internal readonly string Detail;
            internal readonly bool Active;

            internal TeamMemberDisplay(string name, int hp, int maxHp, string detail, bool active)
            {
                Name = name;
                Hp = hp;
                MaxHp = maxHp;
                Detail = detail;
                Active = active;
            }
        }

        private readonly UIDocument _doc;
        private readonly VisualElement _hudRoot;
        private readonly VisualElement _loadingOverlay;
        private readonly Label _loadingLabel;
        private readonly Label _questTitle;
        private readonly Label _questBody;
        private readonly VisualElement _playerCombatCard;
        private readonly Label _playerCombatTitle;
        private readonly Label _playerCombatDetail;
        private readonly VisualElement _bossCard;
        private readonly Label _bossNameLabel;
        private readonly Label _bossHpLabel;
        private readonly VisualElement _bossHpFill;
        private readonly Button _journalBtn;
        private readonly VisualElement _teamList;
        private readonly Label _toastLabel;
        private readonly Label _interactPrompt;
        private readonly VisualElement _interactMenu;
        private readonly Label _interactTitle;
        private readonly VisualElement _interactButtons;
        private readonly Label _controlsText;

        private readonly List<(VisualElement card, Label name, Label hpLabel, VisualElement hpFill, Label detail)> _teamCards
            = new List<(VisualElement, Label, Label, VisualElement, Label)>();

        internal GameHudView(Action onOpenJournal, Transform parent = null)
        {
            _doc = UitkRuntimeHost.CreateDocument("GameHudUI", "UI/GameHud", 50, parent);
            VisualElement root = _doc.rootVisualElement;
            _hudRoot = root.Q<VisualElement>("hudRoot");
            _loadingOverlay = root.Q<VisualElement>("loadingOverlay");
            _loadingLabel = root.Q<Label>("loadingLabel");
            _questTitle = root.Q<Label>("questTitle");
            _questBody = root.Q<Label>("questBody");
            _playerCombatCard = root.Q<VisualElement>("playerCombatCard");
            _playerCombatTitle = root.Q<Label>("playerCombatTitle");
            _playerCombatDetail = root.Q<Label>("playerCombatDetail");
            _bossCard = root.Q<VisualElement>("bossCard");
            _bossNameLabel = root.Q<Label>("bossNameLabel");
            _bossHpLabel = root.Q<Label>("bossHpLabel");
            _bossHpFill = root.Q<VisualElement>("bossHpFill");
            _journalBtn = root.Q<Button>("journalBtn");
            _teamList = root.Q<VisualElement>("teamList");
            _toastLabel = root.Q<Label>("toastLabel");
            _interactPrompt = root.Q<Label>("interactPrompt");
            _interactMenu = root.Q<VisualElement>("interactMenu");
            _interactTitle = root.Q<Label>("interactTitle");
            _interactButtons = root.Q<VisualElement>("interactButtons");
            _controlsText = root.Q<Label>("controlsText");

            _journalBtn.clicked += () => onOpenJournal?.Invoke();
        }

        internal void SetVisible(bool visible)
        {
            SetShown(_hudRoot, visible);
        }

        internal void SetLoading(bool loading, string text = null)
        {
            SetShown(_loadingOverlay, loading);
            if (!string.IsNullOrEmpty(text))
            {
                _loadingLabel.text = text;
            }
        }

        internal void SetQuest(string title, string body)
        {
            _questTitle.text = title ?? string.Empty;
            _questBody.text = body ?? string.Empty;
        }

        internal void SetPlayerStats(string title, string detail)
        {
            bool show = !string.IsNullOrEmpty(title);
            SetShown(_playerCombatCard, show);
            if (show)
            {
                _playerCombatTitle.text = title;
                _playerCombatDetail.text = detail ?? string.Empty;
            }
        }

        internal void SetBossBar(bool visible, string title = null, int hp = 0, int maxHp = 1)
        {
            SetShown(_bossCard, visible);
            if (!visible) return;
            _bossNameLabel.text = title ?? string.Empty;
            _bossHpLabel.text = $"{hp} / {maxHp}";
            float pct = maxHp > 0 ? Mathf.Clamp01((float)hp / maxHp) * 100f : 0f;
            _bossHpFill.style.width = Length.Percent(pct);
        }

        internal void SetJournalButtonText(string text)
        {
            _journalBtn.text = text;
        }

        internal void SetTeam(IReadOnlyList<TeamMemberDisplay> members)
        {
            while (_teamCards.Count < members.Count)
            {
                var card = new VisualElement();
                card.AddToClassList("hud-member-card");
                var row = new VisualElement();
                row.AddToClassList("hud-member-row");
                var nameLbl = new Label();
                nameLbl.AddToClassList("hud-member-name");
                var hpLbl = new Label();
                hpLbl.AddToClassList("hud-member-hp-label");
                row.Add(nameLbl);
                row.Add(hpLbl);
                var track = new VisualElement();
                track.AddToClassList("bar-track");
                var fill = new VisualElement();
                fill.AddToClassList("bar-fill-hp");
                track.Add(fill);
                var detailLbl = new Label();
                detailLbl.AddToClassList("muted-text");
                card.Add(row);
                card.Add(track);
                card.Add(detailLbl);
                _teamList.Add(card);
                _teamCards.Add((card, nameLbl, hpLbl, fill, detailLbl));
            }

            for (int i = 0; i < _teamCards.Count; i++)
            {
                bool show = i < members.Count;
                SetShown(_teamCards[i].card, show);
                if (!show) continue;
                TeamMemberDisplay m = members[i];
                _teamCards[i].name.text = m.Name;
                _teamCards[i].hpLabel.text = m.MaxHp > 0 ? $"{m.Hp}/{m.MaxHp}" : string.Empty;
                float pct = m.MaxHp > 0 ? Mathf.Clamp01((float)m.Hp / m.MaxHp) * 100f : 0f;
                _teamCards[i].hpFill.style.width = Length.Percent(pct);
                _teamCards[i].detail.text = m.Detail ?? string.Empty;
                SetShown(_teamCards[i].detail, !string.IsNullOrEmpty(m.Detail));
                if (m.Active) _teamCards[i].card.AddToClassList("hud-member-active");
                else _teamCards[i].card.RemoveFromClassList("hud-member-active");
            }
        }

        internal void SetControlsHint(string text)
        {
            _controlsText.text = text ?? string.Empty;
        }

        internal void SetToast(string text)
        {
            bool show = !string.IsNullOrEmpty(text);
            SetShown(_toastLabel, show);
            if (show) _toastLabel.text = text;
        }

        internal void SetInteractPrompt(string text)
        {
            bool show = !string.IsNullOrEmpty(text);
            SetShown(_interactPrompt, show);
            if (show) _interactPrompt.text = text;
        }

        internal void SetInteractMenu(bool visible, string title, IReadOnlyList<(string label, Action onClick)> options)
        {
            SetShown(_interactMenu, visible);
            if (!visible) return;
            _interactTitle.text = title ?? string.Empty;
            _interactButtons.Clear();
            if (options == null) return;
            for (int i = 0; i < options.Count; i++)
            {
                Action cb = options[i].onClick;
                var btn = new Button(() => cb?.Invoke()) { text = options[i].label };
                btn.AddToClassList("btn");
                btn.AddToClassList("btn-primary");
                _interactButtons.Add(btn);
            }
        }

        private static void SetShown(VisualElement el, bool shown)
        {
            if (el == null) return;
            if (shown) el.RemoveFromClassList("hidden");
            else el.AddToClassList("hidden");
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
