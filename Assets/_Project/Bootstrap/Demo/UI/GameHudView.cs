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

        internal readonly struct KeyPromptDisplay
        {
            internal readonly string Key;
            internal readonly string Label;

            internal KeyPromptDisplay(string key, string label)
            {
                Key = key ?? string.Empty;
                Label = label ?? string.Empty;
            }
        }

        private readonly UIDocument _doc;
        private readonly VisualElement _hudRoot;
        private readonly VisualElement _loadingOverlay;
        private readonly Label _loadingLabel;
        private readonly VisualElement _questCard;
        private readonly Label _questTag;
        private readonly Label _questCompass;
        private readonly Label _questTitle;
        private readonly Label _questBody;
        private readonly Label _questMeta;
        private readonly VisualElement _playerCombatCard;
        private readonly Label _playerCombatTitle;
        private readonly Label _playerCombatDetail;
        private readonly VisualElement _playerHpFill;
        private readonly VisualElement _bossCard;
        private readonly Label _bossNameLabel;
        private readonly Label _bossHpLabel;
        private readonly VisualElement _bossHpFill;
        private readonly VisualElement _tutorialBanner;
        private readonly Label _tutorialStepBadge;
        private readonly Label _tutorialTitle;
        private readonly VisualElement _tutorialKeys;
        private readonly Button _journalBtn;
        private readonly VisualElement _teamList;
        private readonly Label _toastLabel;
        private readonly VisualElement _interactPrompt;
        private readonly Label _interactPromptKey;
        private readonly Label _interactPromptText;
        private readonly VisualElement _interactMenu;
        private readonly Label _interactTitle;
        private readonly VisualElement _interactButtons;
        private readonly VisualElement _dialogueBox;
        private readonly Label _dialogueSpeaker;
        private readonly Label _dialogueBody;
        private readonly VisualElement _keyPromptsRow;
        private readonly Label _controlsText;

        private struct TeamCardState
        {
            internal bool Initialized;
            internal string Name;
            internal int Hp;
            internal int MaxHp;
            internal string Detail;
            internal bool Active;
        }

        private readonly List<(VisualElement card, Label name, Label hpLabel, VisualElement hpFill, Label detail)> _teamCards
            = new List<(VisualElement, Label, Label, VisualElement, Label)>();
        private readonly List<TeamCardState> _teamStates = new List<TeamCardState>(6);

        private readonly List<(VisualElement pill, Label key, Label label)> _keyPromptItems
            = new List<(VisualElement, Label, Label)>();
        private readonly List<KeyPromptDisplay> _keyPromptStates = new List<KeyPromptDisplay>(8);

        private readonly List<(VisualElement pill, Label key, Label label)> _tutorialKeyItems
            = new List<(VisualElement, Label, Label)>();
        private readonly List<KeyPromptDisplay> _tutorialKeyStates = new List<KeyPromptDisplay>(6);

        private bool _questInitialized;
        private bool _questIsMain;
        private string _questTagText;
        private string _questTitleText;
        private string _questBodyText;
        private string _questCompassText;
        private string _questMetaText;

        private bool _bossInitialized;
        private bool _bossVisible;
        private string _bossTitle;
        private int _bossHp = int.MinValue;
        private int _bossMaxHp = int.MinValue;

        private bool _tutorialInitialized;
        private bool _tutorialVisible;
        private string _tutorialBadgeText;
        private string _tutorialTitleText;

        private bool _dialogueInitialized;
        private bool _dialogueVisible;
        private string _dialogueSpeakerText;
        private string _dialogueBodyText;

        internal GameHudView(Action onOpenJournal, Transform parent = null)
        {
            _doc = UitkRuntimeHost.CreateDocument("GameHudUI", "UI/GameHud", 50, parent);
            VisualElement root = _doc.rootVisualElement;
            _hudRoot = root.Q<VisualElement>("hudRoot");
            _loadingOverlay = root.Q<VisualElement>("loadingOverlay");
            _loadingLabel = root.Q<Label>("loadingLabel");
            _questCard = root.Q<VisualElement>("questCard");
            _questTag = root.Q<Label>("questTag");
            _questCompass = root.Q<Label>("questCompass");
            _questTitle = root.Q<Label>("questTitle");
            _questBody = root.Q<Label>("questBody");
            _questMeta = root.Q<Label>("questMeta");
            _playerCombatCard = root.Q<VisualElement>("playerCombatCard");
            _playerCombatTitle = root.Q<Label>("playerCombatTitle");
            _playerCombatDetail = root.Q<Label>("playerCombatDetail");
            _playerHpFill = root.Q<VisualElement>("playerHpFill");
            _bossCard = root.Q<VisualElement>("bossCard");
            _bossNameLabel = root.Q<Label>("bossNameLabel");
            _bossHpLabel = root.Q<Label>("bossHpLabel");
            _bossHpFill = root.Q<VisualElement>("bossHpFill");
            _tutorialBanner = root.Q<VisualElement>("tutorialBanner");
            _tutorialStepBadge = root.Q<Label>("tutorialStepBadge");
            _tutorialTitle = root.Q<Label>("tutorialTitle");
            _tutorialKeys = root.Q<VisualElement>("tutorialKeys");
            _journalBtn = root.Q<Button>("journalBtn");
            _teamList = root.Q<VisualElement>("teamList");
            _toastLabel = root.Q<Label>("toastLabel");
            _interactPrompt = root.Q<VisualElement>("interactPrompt");
            _interactPromptKey = root.Q<Label>("interactPromptKey");
            _interactPromptText = root.Q<Label>("interactPromptText");
            _interactMenu = root.Q<VisualElement>("interactMenu");
            _interactTitle = root.Q<Label>("interactTitle");
            _interactButtons = root.Q<VisualElement>("interactButtons");
            _dialogueBox = root.Q<VisualElement>("dialogueBox");
            _dialogueSpeaker = root.Q<Label>("dialogueSpeaker");
            _dialogueBody = root.Q<Label>("dialogueBody");
            _keyPromptsRow = root.Q<VisualElement>("keyPromptsRow");
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
                SetText(_loadingLabel, text);
            }
        }

        internal void SetQuest(string title, string body)
        {
            SetQuest(null, title, body, null, null, false);
        }

        /// <summary>Updates the top-left PLA-style quest tracker card only when content changes.</summary>
        internal void SetQuest(string tag, string title, string objective, string compass, string meta, bool isMainStory)
        {
            string safeTag = tag ?? string.Empty;
            string safeTitle = title ?? string.Empty;
            string safeBody = objective ?? string.Empty;
            string safeCompass = compass ?? string.Empty;
            string safeMeta = meta ?? string.Empty;

            if (_questInitialized &&
                _questIsMain == isMainStory &&
                string.Equals(_questTagText, safeTag, StringComparison.Ordinal) &&
                string.Equals(_questTitleText, safeTitle, StringComparison.Ordinal) &&
                string.Equals(_questBodyText, safeBody, StringComparison.Ordinal) &&
                string.Equals(_questCompassText, safeCompass, StringComparison.Ordinal) &&
                string.Equals(_questMetaText, safeMeta, StringComparison.Ordinal))
            {
                return;
            }

            _questInitialized = true;
            _questIsMain = isMainStory;
            _questTagText = safeTag;
            _questTitleText = safeTitle;
            _questBodyText = safeBody;
            _questCompassText = safeCompass;
            _questMetaText = safeMeta;

            if (_questCard != null)
            {
                if (isMainStory) _questCard.AddToClassList("hud-quest-card-main");
                else _questCard.RemoveFromClassList("hud-quest-card-main");
            }

            SetText(_questTag, safeTag);
            SetShown(_questTag, safeTag.Length > 0);
            SetText(_questCompass, safeCompass);
            SetShown(_questCompass, safeCompass.Length > 0);
            SetText(_questTitle, safeTitle);
            SetText(_questBody, safeBody);
            SetText(_questMeta, safeMeta);
            SetShown(_questMeta, safeMeta.Length > 0);
        }

        internal void SetPlayerStats(string title, string detail, int hp = -1, int maxHp = -1)
        {
            bool show = !string.IsNullOrEmpty(title);
            SetShown(_playerCombatCard, show);
            if (!show) return;

            SetText(_playerCombatTitle, title);
            SetText(_playerCombatDetail, detail ?? string.Empty);

            if (_playerHpFill != null)
            {
                bool showBar = maxHp > 0 && hp >= 0;
                SetShown(_playerHpFill.parent, showBar);
                if (showBar)
                {
                    float pct = Mathf.Clamp01((float)hp / maxHp) * 100f;
                    _playerHpFill.style.width = Length.Percent(pct);
                }
            }
        }

        internal void SetBossBar(bool visible, string title = null, int hp = 0, int maxHp = 1)
        {
            if (!_bossInitialized || _bossVisible != visible)
            {
                SetShown(_bossCard, visible);
                _bossVisible = visible;
                _bossInitialized = true;
            }

            if (!visible) return;
            string safeTitle = title ?? string.Empty;
            if (!string.Equals(_bossTitle, safeTitle, StringComparison.Ordinal))
            {
                _bossTitle = safeTitle;
                SetText(_bossNameLabel, safeTitle);
            }

            if (_bossHp != hp || _bossMaxHp != maxHp)
            {
                _bossHp = hp;
                _bossMaxHp = maxHp;
                SetText(_bossHpLabel, $"{hp} / {maxHp}");
                float pct = maxHp > 0 ? Mathf.Clamp01((float)hp / maxHp) * 100f : 0f;
                _bossHpFill.style.width = Length.Percent(pct);
            }
        }

        internal void SetJournalButtonText(string text)
        {
            string value = text ?? string.Empty;
            if (!string.Equals(_journalBtn.text, value, StringComparison.Ordinal))
            {
                _journalBtn.text = value;
            }
        }

        internal void SetTeam(IReadOnlyList<TeamMemberDisplay> members)
        {
            while (_teamCards.Count < members.Count)
            {
                var card = new VisualElement { pickingMode = PickingMode.Ignore };
                card.AddToClassList("hud-member-card");
                var row = new VisualElement { pickingMode = PickingMode.Ignore };
                row.AddToClassList("hud-member-row");
                var nameLbl = new Label { pickingMode = PickingMode.Ignore };
                nameLbl.AddToClassList("hud-member-name");
                var hpLbl = new Label { pickingMode = PickingMode.Ignore };
                hpLbl.AddToClassList("hud-member-hp-label");
                row.Add(nameLbl);
                row.Add(hpLbl);
                var track = new VisualElement { pickingMode = PickingMode.Ignore };
                track.AddToClassList("bar-track");
                var fill = new VisualElement { pickingMode = PickingMode.Ignore };
                fill.AddToClassList("bar-fill-hp");
                track.Add(fill);
                var detailLbl = new Label { pickingMode = PickingMode.Ignore };
                detailLbl.AddToClassList("muted-text");
                card.Add(row);
                card.Add(track);
                card.Add(detailLbl);
                _teamList.Add(card);
                _teamCards.Add((card, nameLbl, hpLbl, fill, detailLbl));
                _teamStates.Add(default);
            }

            for (int i = 0; i < _teamCards.Count; i++)
            {
                bool show = i < members.Count;
                SetShown(_teamCards[i].card, show);
                if (!show) continue;
                TeamMemberDisplay m = members[i];
                TeamCardState prev = _teamStates[i];
                string detail = m.Detail ?? string.Empty;
                if (!prev.Initialized || !string.Equals(prev.Name, m.Name, StringComparison.Ordinal))
                {
                    SetText(_teamCards[i].name, m.Name);
                    prev.Name = m.Name;
                }

                if (!prev.Initialized || prev.Hp != m.Hp || prev.MaxHp != m.MaxHp)
                {
                    SetText(_teamCards[i].hpLabel, m.MaxHp > 0 ? $"{m.Hp}/{m.MaxHp}" : string.Empty);
                    float pct = m.MaxHp > 0 ? Mathf.Clamp01((float)m.Hp / m.MaxHp) * 100f : 0f;
                    _teamCards[i].hpFill.style.width = Length.Percent(pct);
                    prev.Hp = m.Hp;
                    prev.MaxHp = m.MaxHp;
                }

                if (!prev.Initialized || !string.Equals(prev.Detail, detail, StringComparison.Ordinal))
                {
                    SetText(_teamCards[i].detail, detail);
                    SetShown(_teamCards[i].detail, detail.Length > 0);
                    prev.Detail = detail;
                }

                if (!prev.Initialized || prev.Active != m.Active)
                {
                    if (m.Active) _teamCards[i].card.AddToClassList("hud-member-active");
                    else _teamCards[i].card.RemoveFromClassList("hud-member-active");
                    prev.Active = m.Active;
                }

                prev.Initialized = true;
                _teamStates[i] = prev;
            }
        }

        /// <summary>Updates the bottom BotW-style contextual key prompt bar using pooled elements.</summary>
        internal void SetKeyPrompts(IReadOnlyList<KeyPromptDisplay> prompts)
        {
            SyncKeyPillList(_keyPromptsRow, _keyPromptItems, _keyPromptStates, prompts);
        }

        /// <summary>Updates the non-blocking top-center BotW tutorial banner.</summary>
        internal void SetTutorial(bool visible, string badge, string title, IReadOnlyList<KeyPromptDisplay> keys)
        {
            string safeBadge = badge ?? string.Empty;
            string safeTitle = title ?? string.Empty;

            if (!_tutorialInitialized || _tutorialVisible != visible)
            {
                SetShown(_tutorialBanner, visible);
                _tutorialVisible = visible;
                _tutorialInitialized = true;
            }

            if (!visible) return;

            if (!string.Equals(_tutorialBadgeText, safeBadge, StringComparison.Ordinal))
            {
                _tutorialBadgeText = safeBadge;
                SetText(_tutorialStepBadge, safeBadge);
                SetShown(_tutorialStepBadge, safeBadge.Length > 0);
            }

            if (!string.Equals(_tutorialTitleText, safeTitle, StringComparison.Ordinal))
            {
                _tutorialTitleText = safeTitle;
                SetText(_tutorialTitle, safeTitle);
            }

            SyncKeyPillList(_tutorialKeys, _tutorialKeyItems, _tutorialKeyStates, keys);
        }

        /// <summary>Updates the bottom-center PLA-style character dialogue box.</summary>
        internal void SetDialogue(bool visible, string speaker, string body)
        {
            string safeSpeaker = speaker ?? string.Empty;
            string safeBody = body ?? string.Empty;

            if (_dialogueInitialized &&
                _dialogueVisible == visible &&
                string.Equals(_dialogueSpeakerText, safeSpeaker, StringComparison.Ordinal) &&
                string.Equals(_dialogueBodyText, safeBody, StringComparison.Ordinal))
            {
                return;
            }

            _dialogueInitialized = true;
            _dialogueVisible = visible;
            _dialogueSpeakerText = safeSpeaker;
            _dialogueBodyText = safeBody;

            SetShown(_dialogueBox, visible);
            if (!visible) return;

            SetText(_dialogueSpeaker, safeSpeaker);
            SetShown(_dialogueSpeaker, safeSpeaker.Length > 0);
            SetText(_dialogueBody, safeBody);
        }

        internal void SetControlsHint(string text)
        {
            SetText(_controlsText, text ?? string.Empty);
        }

        internal void SetToast(string text)
        {
            bool show = !string.IsNullOrEmpty(text);
            SetShown(_toastLabel, show);
            if (show) SetText(_toastLabel, text);
        }

        internal void SetInteractPrompt(string text)
        {
            SetInteractPrompt(null, text);
        }

        /// <summary>Shows a compact two-part interaction bubble (key glyph + action text).</summary>
        internal void SetInteractPrompt(string key, string text)
        {
            bool show = !string.IsNullOrEmpty(text);
            SetShown(_interactPrompt, show);
            if (!show) return;

            if (_interactPromptText != null)
            {
                bool hasKey = !string.IsNullOrEmpty(key);
                SetShown(_interactPromptKey, hasKey);
                if (hasKey) SetText(_interactPromptKey, key);
                SetText(_interactPromptText, text);
            }
            else if (_interactPrompt is Label fallbackLabel)
            {
                SetText(fallbackLabel, string.IsNullOrEmpty(key) ? text : $"[{key}] {text}");
            }
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

        private static void SyncKeyPillList(
            VisualElement container,
            List<(VisualElement pill, Label key, Label label)> items,
            List<KeyPromptDisplay> states,
            IReadOnlyList<KeyPromptDisplay> next)
        {
            if (container == null) return;
            int count = next != null ? next.Count : 0;
            bool same = states.Count == count;
            if (same)
            {
                for (int i = 0; i < count; i++)
                {
                    if (!string.Equals(states[i].Key, next[i].Key, StringComparison.Ordinal) ||
                        !string.Equals(states[i].Label, next[i].Label, StringComparison.Ordinal))
                    {
                        same = false;
                        break;
                    }
                }
            }

            if (same) return;

            states.Clear();
            for (int i = 0; i < count; i++)
            {
                states.Add(next[i]);
            }

            while (items.Count < count)
            {
                var pill = new VisualElement { pickingMode = PickingMode.Ignore };
                pill.AddToClassList("key-pill");
                var keyLbl = new Label { pickingMode = PickingMode.Ignore };
                keyLbl.AddToClassList("key-glyph");
                var actionLbl = new Label { pickingMode = PickingMode.Ignore };
                actionLbl.AddToClassList("key-label");
                pill.Add(keyLbl);
                pill.Add(actionLbl);
                container.Add(pill);
                items.Add((pill, keyLbl, actionLbl));
            }

            for (int i = 0; i < items.Count; i++)
            {
                bool show = i < count;
                SetShown(items[i].pill, show);
                if (!show) continue;
                KeyPromptDisplay entry = next[i];
                SetShown(items[i].key, entry.Key.Length > 0);
                SetText(items[i].key, entry.Key);
                SetText(items[i].label, entry.Label);
            }
        }

        private static void SetText(Label el, string text)
        {
            if (el == null) return;
            string value = text ?? string.Empty;
            if (!string.Equals(el.text, value, StringComparison.Ordinal))
            {
                el.text = value;
            }
        }

        private static void SetShown(VisualElement el, bool shown)
        {
            if (el == null) return;
            bool hidden = el.ClassListContains("hidden");
            if (shown)
            {
                if (hidden) el.RemoveFromClassList("hidden");
            }
            else if (!hidden)
            {
                el.AddToClassList("hidden");
            }
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
