using System;
using System.Collections.Generic;
using Cosmimera.Bootstrap.Demo.UI;
using Cosmimera.Combat;
using Cosmimera.Creatures;
using Cosmimera.WorldGen;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Cosmimera.Bootstrap.Demo
{
    public sealed partial class RunSession
    {
        private GameHudView _hud;
        private readonly List<GameHudView.TeamMemberDisplay> _hudTeamBuffer = new List<GameHudView.TeamMemberDisplay>(6);
        private int _lastInteractTargetHud = int.MinValue;
        private int _lastInteractChoiceHud = int.MinValue;
        private string _lastObserveTextHud;

        private InputAction _hudJumpAction;
        private InputAction _hudToggleViewAction;
        private int _hudControlsGamepad = -1;
        private int _hudPlayerGamepad = -1;
        private int _hudPlayerHp = int.MinValue;
        private int _hudPlayerMaxHp = int.MinValue;
        private long _hudPlayerRollCd = long.MinValue;

        private bool _hudBossVisible;
        private bool _hudBossIsTrial;
        private int _hudBossId = int.MinValue;
        private int _hudBossHp = int.MinValue;
        private int _hudBossMaxHp = int.MinValue;

        private struct HudMemberKey
        {
            public int UnitId;
            public string DefId;
            public int Level;
            public int Hp;
            public int MaxHp;
            public bool Active;
            public bool IsMain;
            public bool Overloaded;
            public bool Fainted;
            public string Detail;
        }

        private readonly List<HudMemberKey> _hudTeamKeys = new List<HudMemberKey>(6);
        private int _hudJournalGamepad = -1;

        private int _hudWeatherPinnedSec = int.MinValue;
        private DayPhase _hudWeatherPhase;
        private WeatherKind _hudWeatherKind;
        private WeatherKind _hudWeatherExotic;
        private WeatherKind _hudWeatherBase;
        private bool _hudWeatherOvercast;
        private string _hudWeatherStatusCached;
        private float _hudMoveMult = -1f, _hudSprintMult = -1f, _hudRollMult = -1f;
        private string _hudWeatherMoveCached;

        private string _hudQuestWeatherRef;
        private string _hudQuestMoveRef;
        private bool _hudQuestBeginner;
        private int _hudQuestStepIndex = int.MinValue;
        private int _hudQuestProgress = int.MinValue;
        private bool _hudQuestComplete;
        private int _hudQuestRegion = int.MinValue;
        private int _hudQuestGamepad = -1;
        private bool _hudQuestHasTarget;
        private int _hudQuestMeters = int.MinValue;
        private bool _hudQuestHasBearing;
        private int _hudQuestBearingKind = int.MinValue;
        private int _hudQuestAngle = int.MinValue;

        private int _hudPromptCaptureId = int.MinValue;
        private int _hudPromptCaptureTenths = int.MinValue;
        private int _hudPromptInteractableId = int.MinValue;
        private int _hudPromptGamepad = -1;

        private void LateUpdate() => SyncHud();

        private void SyncHud()
        {
            if (_hud == null)
            {
                _hud = new GameHudView(() => OpenJournal(), transform);
            }

            if (_rig == null || _ended || _journal != null)
            {
                _hud.SetVisible(false);
                return;
            }

            _hud.SetVisible(true);
            if (!_started)
            {
                _hud.SetLoading(true, _copy.Ui("ui.star_select.loading"));
                return;
            }

            _hud.SetLoading(false);
            SyncQuestHud();
            SyncBossHud();
            SyncTeamHud();
            SyncPlayerHud();
            SyncControlsAndPromptsHud();
        }

        private void RefreshCachedWeatherStrings()
        {
            float3 direction = _rig.Config.ToDirection(PlayerPosition());
            int pinned = _weatherControl.PinnedRemainingMs(_weather.RegionAt(direction), _nowMs);
            int pinnedSec = pinned > 0 ? (int)Math.Ceiling(pinned / 1000d) : 0;
            DayPhase phase = _weather.PhaseAt(direction, _nowMs);
            if (_hudWeatherStatusCached == null ||
                _hudWeatherPinnedSec != pinnedSec ||
                _hudWeatherPhase != phase ||
                _hudWeatherKind != _playerWeather.Kind ||
                _hudWeatherExotic != _playerWeather.Exotic ||
                _hudWeatherBase != _playerWeatherState.BaseKind ||
                _hudWeatherOvercast != _playerWeatherState.Overcast)
            {
                _hudWeatherPinnedSec = pinnedSec;
                _hudWeatherPhase = phase;
                _hudWeatherKind = _playerWeather.Kind;
                _hudWeatherExotic = _playerWeather.Exotic;
                _hudWeatherBase = _playerWeatherState.BaseKind;
                _hudWeatherOvercast = _playerWeatherState.Overcast;
                _hudWeatherStatusCached = WeatherStatusText();
            }

            if (_hudWeatherMoveCached == null ||
                _hudMoveMult != _playerWeather.PlayerMoveSpeedMultiplier ||
                _hudSprintMult != _playerWeather.PlayerSprintSpeedMultiplier ||
                _hudRollMult != _playerWeather.PlayerRollCooldownMultiplier)
            {
                _hudMoveMult = _playerWeather.PlayerMoveSpeedMultiplier;
                _hudSprintMult = _playerWeather.PlayerSprintSpeedMultiplier;
                _hudRollMult = _playerWeather.PlayerRollCooldownMultiplier;
                _hudWeatherMoveCached = WeatherMovementText();
            }
        }

        private void SyncQuestHud()
        {
            RefreshCachedWeatherStrings();
            bool beginner = _setup.IsBeginnerQuest && _bqTracker != null;
            int stepIndex = _quest.StepIndex;
            int progress = _quest.Progress;
            bool complete = _quest.IsComplete;
            int usesGamepad = _inputHints.UsesGamepad ? 1 : 0;

            bool hasTarget = false;
            int metersRounded = 0;
            bool hasBearing = false;
            int bearingKind = 0;
            int angleRounded = 0;
            float angleRaw = 0f;
            if (!beginner && !complete && TryQuestTarget(_quest.Current, out float3 target))
            {
                hasTarget = true;
                float3 here = _rig.Config.ToDirection(PlayerPosition());
                float meters = math.acos(math.clamp(math.dot(here, target), -1f, 1f)) * _rig.Config.Radius;
                metersRounded = (int)Math.Round(meters, MidpointRounding.AwayFromZero);
                hasBearing = DemoNavigation.TryBearing(here, target, _rig.CameraRig.Heading, out angleRaw);
                if (hasBearing)
                {
                    bearingKind = math.abs(angleRaw) < 10f ? 1 : (angleRaw < 0f ? 2 : 3);
                    angleRounded = bearingKind >= 2 ? (int)Math.Round(math.abs(angleRaw), MidpointRounding.AwayFromZero) : 0;
                }
            }

            if (ReferenceEquals(_hudQuestWeatherRef, _hudWeatherStatusCached) &&
                ReferenceEquals(_hudQuestMoveRef, _hudWeatherMoveCached) &&
                _hudQuestBeginner == beginner &&
                _hudQuestStepIndex == stepIndex &&
                _hudQuestProgress == progress &&
                _hudQuestComplete == complete &&
                _hudQuestRegion == _region &&
                _hudQuestGamepad == usesGamepad &&
                _hudQuestHasTarget == hasTarget &&
                _hudQuestMeters == metersRounded &&
                _hudQuestHasBearing == hasBearing &&
                _hudQuestBearingKind == bearingKind &&
                _hudQuestAngle == angleRounded)
            {
                return;
            }

            _hudQuestWeatherRef = _hudWeatherStatusCached;
            _hudQuestMoveRef = _hudWeatherMoveCached;
            _hudQuestBeginner = beginner;
            _hudQuestStepIndex = stepIndex;
            _hudQuestProgress = progress;
            _hudQuestComplete = complete;
            _hudQuestRegion = _region;
            _hudQuestGamepad = usesGamepad;
            _hudQuestHasTarget = hasTarget;
            _hudQuestMeters = metersRounded;
            _hudQuestHasBearing = hasBearing;
            _hudQuestBearingKind = bearingKind;
            _hudQuestAngle = angleRounded;

            string title = _copy.ChainTitle(_plan.ChainId);
            string body = _hudWeatherStatusCached;
            if (!string.IsNullOrEmpty(_hudWeatherMoveCached))
            {
                body += "\n" + _hudWeatherMoveCached;
            }

            if (beginner)
            {
                body += "\n" + _copy.Ui("ui.hud.quest_tracker") + "：新手教學進行中";
            }
            else if (!complete)
            {
                RunQuestStep s = _quest.Current;
                string stepText = _copy.Step(StepType(s.Kind), (int)(_plan.Seed % 3))
                    .Replace("{count}", (s.Count - progress).ToString())
                    .Replace("{element}", _copy.Element(s.Element.ToString()))
                    .Replace("{poi}", _copy.Theme(_plan.Regions[s.Region].ThemeId))
                    .Replace("{boss}", _copy.Name(_plan.Spawns[_plan.Quest[_plan.Quest.Count - 1].TargetSpawn].CreatureId));
                body += "\n" + _copy.Ui("ui.hud.quest_tracker") + "：" + stepText;

                if (hasTarget)
                {
                    string bearing = !hasBearing ? _copy.Ui("ui.hud.bearing_unavailable")
                        : bearingKind == 1 ? _copy.Ui("ui.hud.bearing_ahead")
                        : _copy.Ui(bearingKind == 2 ? "ui.hud.bearing_left" : "ui.hud.bearing_right")
                            .Replace("{angle}", angleRounded.ToString());
                    body += $"\n{bearing} · {metersRounded} m";
                }
            }
            else
            {
                body += "\n" + _copy.Ui("ui.hud.quest_complete").Replace("{capture}", BindingLabel(_captureAction));
            }

            if (_region >= 0)
            {
                RunRegion r = _plan.Regions[_region];
                body += "\n" + _copy.Theme(r.ThemeId) + (r.AffixId != null ? " · " + _copy.AffixName(r.AffixId) : string.Empty);
            }

            _hud.SetQuest(title, body);
        }

        private void SyncBossHud()
        {
            EncounterUnit trialBoss = TrialFinalBoss(out int _, out int _);
            if (trialBoss != null)
            {
                int hp = trialBoss.Combatant.Hp;
                int maxHp = trialBoss.Combatant.MaxHp;
                if (!_hudBossVisible || !_hudBossIsTrial || _hudBossId != trialBoss.Id ||
                    _hudBossHp != hp || _hudBossMaxHp != maxHp)
                {
                    _hudBossVisible = true;
                    _hudBossIsTrial = true;
                    _hudBossId = trialBoss.Id;
                    _hudBossHp = hp;
                    _hudBossMaxHp = maxHp;
                    string tTitle = _copy.Ui("ui.trial.boss_title").Replace("{terrain}", TrialTerrainName) + " · " + _copy.Name(trialBoss.Def.Id);
                    _hud.SetBossBar(true, tTitle, hp, maxHp);
                }
                return;
            }

            int bossId = BossUnit();
            if (bossId >= 0 && _wild.TryGetValue(bossId, out WildInfo info) && !info.Gone)
            {
                EncounterUnit boss = _encounter.Units[bossId];
                if (_region == _plan.BossRegion || boss.Engaged)
                {
                    int hp = boss.Combatant.Hp;
                    int maxHp = boss.Combatant.MaxHp;
                    if (!_hudBossVisible || _hudBossIsTrial || _hudBossId != bossId ||
                        _hudBossHp != hp || _hudBossMaxHp != maxHp)
                    {
                        _hudBossVisible = true;
                        _hudBossIsTrial = false;
                        _hudBossId = bossId;
                        _hudBossHp = hp;
                        _hudBossMaxHp = maxHp;
                        string title = _copy.Epithet(_plan.PlanetTypeId, (int)(_plan.Seed % 3)) + _copy.Name(boss.Def.Id);
                        _hud.SetBossBar(true, title, hp, maxHp);
                    }
                    return;
                }
            }

            if (_hudBossVisible)
            {
                _hudBossVisible = false;
                _hudBossId = int.MinValue;
                _hud.SetBossBar(false);
            }
        }

        private void SyncTeamHud()
        {
            int usesGamepad = _inputHints.UsesGamepad ? 1 : 0;
            if (_hudJournalGamepad != usesGamepad)
            {
                _hudJournalGamepad = usesGamepad;
                _hud.SetJournalButtonText(_copy.Ui("ui.journal.open") + " [" + BindingLabel(_journalAction) + "]");
            }

            int poolCount = _team.Pool.Count;
            bool dirty = _hudTeamKeys.Count != poolCount;
            for (int i = 0; i < poolCount; i++)
            {
                int id = _team.Pool[i];
                EncounterUnit u = _encounter.Units[id];
                bool active = In(_team.Active, id);
                bool isMain = id == _mainId;
                bool overloaded = u.Combatant.IsOverloaded;
                bool fainted = u.Combatant.IsFainted;
                string detail = string.Empty;
                if (fainted)
                {
                    detail = _copy.Ui("ui.hud.knocked_out");
                }
                else if (active)
                {
                    int slots = u.Combatant.Cooldowns.SlotCount;
                    bool allReady = true;
                    for (int s = 0; s < slots; s++)
                    {
                        if (!u.Combatant.Cooldowns.IsReady(s, _nowMs))
                        {
                            allReady = false;
                            break;
                        }
                    }

                    if (allReady)
                    {
                        // Avoid allocating cooldown strings every tick during exploration when every slot is ready.
                        detail = ReadySlotsDetail(slots);
                    }
                    else
                    {
                        for (int s = 0; s < slots; s++)
                        {
                            if (s > 0) detail += " ";
                            detail += u.Combatant.Cooldowns.IsReady(s, _nowMs)
                                ? $"S{s + 1}:{_copy.Ui("ui.hud.cd_ready")}"
                                : $"S{s + 1}:{(u.Combatant.Cooldowns.ReadyAtMs(s) - _nowMs) / 1000f:0.0}s";
                        }
                    }
                }

                if (!dirty)
                {
                    HudMemberKey k = _hudTeamKeys[i];
                    if (k.UnitId != id ||
                        !string.Equals(k.DefId, u.Def.Id, StringComparison.Ordinal) ||
                        k.Level != u.Level ||
                        k.Hp != u.Combatant.Hp ||
                        k.MaxHp != u.Combatant.MaxHp ||
                        k.Active != active ||
                        k.IsMain != isMain ||
                        k.Overloaded != overloaded ||
                        k.Fainted != fainted ||
                        !string.Equals(k.Detail, detail, StringComparison.Ordinal))
                    {
                        dirty = true;
                    }
                }

                var nextKey = new HudMemberKey
                {
                    UnitId = id,
                    DefId = u.Def.Id,
                    Level = u.Level,
                    Hp = u.Combatant.Hp,
                    MaxHp = u.Combatant.MaxHp,
                    Active = active,
                    IsMain = isMain,
                    Overloaded = overloaded,
                    Fainted = fainted,
                    Detail = detail,
                };
                if (i < _hudTeamKeys.Count) _hudTeamKeys[i] = nextKey;
                else _hudTeamKeys.Add(nextKey);
            }

            if (_hudTeamKeys.Count > poolCount)
            {
                _hudTeamKeys.RemoveRange(poolCount, _hudTeamKeys.Count - poolCount);
            }

            if (!dirty)
            {
                return;
            }

            _hudTeamBuffer.Clear();
            for (int i = 0; i < poolCount; i++)
            {
                HudMemberKey k = _hudTeamKeys[i];
                string mark = k.IsMain ? "★ " : k.Active ? "● " : "○ ";
                string overloadTag = k.Overloaded ? $" {_copy.Ui("ui.hud.overload_active")}" : string.Empty;
                _hudTeamBuffer.Add(new GameHudView.TeamMemberDisplay(
                    $"{mark}{_copy.Name(k.DefId)} LV{k.Level}{overloadTag}",
                    k.Hp,
                    k.MaxHp,
                    k.Detail,
                    k.Active));
            }

            _hud.SetTeam(_hudTeamBuffer);
        }

        private string _cachedReadyDetail4;
        private string _cachedReadyDetail3;
        private string _cachedReadyDetail2;
        private string _cachedReadyDetail1;

        private string ReadySlotsDetail(int slots)
        {
            string ready = _copy.Ui("ui.hud.cd_ready");
            string Build(int n)
            {
                string s = string.Empty;
                for (int i = 0; i < n; i++)
                {
                    if (i > 0) s += " ";
                    s += $"S{i + 1}:{ready}";
                }
                return s;
            }

            return slots switch
            {
                4 => _cachedReadyDetail4 ??= Build(4),
                3 => _cachedReadyDetail3 ??= Build(3),
                2 => _cachedReadyDetail2 ??= Build(2),
                1 => _cachedReadyDetail1 ??= Build(1),
                _ => Build(slots),
            };
        }

        private void SyncPlayerHud()
        {
            long rollCdLeft = math.max(0L, _lastRollAtMs + math.max(_balance.Player.RollDurationMs,
                (long)Math.Ceiling(_balance.Player.RollCooldownMs * _playerWeather.PlayerRollCooldownMultiplier *
                    (100 + _foodBuffs.GetRollCooldownModPercent(_nowMs)) / 100d)) - _nowMs);
            int usesGamepad = _inputHints.UsesGamepad ? 1 : 0;
            if (_hudPlayerHp == _vitals.Hp &&
                _hudPlayerMaxHp == _vitals.MaxHp &&
                _hudPlayerRollCd == rollCdLeft &&
                _hudPlayerGamepad == usesGamepad)
            {
                return;
            }

            _hudPlayerHp = _vitals.Hp;
            _hudPlayerMaxHp = _vitals.MaxHp;
            _hudPlayerRollCd = rollCdLeft;
            _hudPlayerGamepad = usesGamepad;
            string rollText = _copy.Ui("ui.hud.roll_prompt").Replace("{key}", BindingLabel(_rollAction)) +
                (rollCdLeft > 0 ? $" ({rollCdLeft / 1000f:0.0}s)" : $" {_copy.Ui("ui.hud.cd_ready")}");
            _hud.SetPlayerStats($"◆ {_copy.Ui("ui.hud.hp")} {_vitals.Hp}/{_vitals.MaxHp}", rollText);
        }

        private void SyncControlsAndPromptsHud()
        {
            int usesGamepad = _inputHints.UsesGamepad ? 1 : 0;
            if (_hudControlsGamepad != usesGamepad)
            {
                _hudControlsGamepad = usesGamepad;
                _hudJumpAction ??= _actions.FindAction("Player/Jump");
                _hudToggleViewAction ??= _actions.FindAction("Player/ToggleView");
                string help = _copy.Ui(usesGamepad != 0 ? "ui.hud.gamepad_help" : "ui.hud.exploration_help")
                    .Replace("{jump}", BindingLabel(_hudJumpAction))
                    .Replace("{roll}", BindingLabel(_rollAction))
                    .Replace("{view}", BindingLabel(_hudToggleViewAction))
                    .Replace("{interact}", BindingLabel(_interactAction))
                    .Replace("{target}", BindingLabel(_cycleTargetAction))
                    .Replace("{journal}", BindingLabel(_journalAction)).Replace("{codex}", BindingLabel(_codexAction))
                    .Replace("{capture}", BindingLabel(_captureAction)).Replace("{cycle}", BindingLabel(_cycleVesselAction))
                    .Replace("{abandon}", BindingLabel(_abandonAction));
                _hud.SetControlsHint(help);
            }

            _hud.SetToast(_nowMs < _toastUntilMs ? _toast : null);

            if (_interactTarget >= 0)
            {
                _hud.SetInteractPrompt(null);
                _hudPromptCaptureId = int.MinValue;
                _hudPromptInteractableId = int.MinValue;
                if (_lastInteractTargetHud != _interactTarget ||
                    _lastInteractChoiceHud != (int)_interactChoice ||
                    _lastObserveTextHud != _observeText)
                {
                    _lastInteractTargetHud = _interactTarget;
                    _lastInteractChoiceHud = (int)_interactChoice;
                    _lastObserveTextHud = _observeText;
                    bool standoff = _encounter.IsConfrontationStandoff(_interactTarget);
                    string[] keys = { "ui.interact.observe", "ui.interact.provoke", "ui.interact.pet", "ui.interact.close" };
                    var options = new List<(string, System.Action)>(keys.Length);
                    for (int i = 0; i < keys.Length; i++)
                    {
                        string text = standoff && i == (int)InteractChoice.Pet ? "協助此方" : _copy.Ui(keys[i]);
                        string label = (int)_interactChoice == i ? "▶ " + text : text;
                        int choiceIdx = i;
                        options.Add((label, (System.Action)(() =>
                        {
                            _interactChoice = (InteractChoice)choiceIdx;
                            ChooseInteract(_interactChoice);
                        })));
                    }
                    string title = _copy.Name(_encounter.Units[_interactTarget].Def.Id);
                    if (!string.IsNullOrEmpty(_observeText)) title += "\n" + _observeText;
                    _hud.SetInteractMenu(true, title, options);
                }
                return;
            }

            _lastInteractTargetHud = int.MinValue;
            _hud.SetInteractMenu(false, null, null);

            int capture = FindCaptureTarget(CaptureReachM);
            if (capture >= 0)
            {
                float seconds = (KnockedOutWindowMs - (_nowMs - _wild[capture].KnockedOutAtMs)) / 1000f;
                int tenths = (int)Math.Round(seconds * 10f, MidpointRounding.AwayFromZero);
                if (_hudPromptCaptureId != capture || _hudPromptCaptureTenths != tenths || _hudPromptGamepad != usesGamepad)
                {
                    _hudPromptCaptureId = capture;
                    _hudPromptCaptureTenths = tenths;
                    _hudPromptInteractableId = int.MinValue;
                    _hudPromptGamepad = usesGamepad;
                    string prompt = _copy.Ui("ui.hud.capture_available")
                        .Replace("{capture}", BindingLabel(_captureAction)).Replace("{cycle}", BindingLabel(_cycleVesselAction))
                        .Replace("{name}", _copy.Name(_encounter.Units[capture].Def.Id))
                        .Replace("{seconds}", (tenths / 10f).ToString("0.0"));
                    _hud.SetInteractPrompt(prompt);
                }
                return;
            }

            _hudPromptCaptureId = int.MinValue;
            if (_interactable >= 0)
            {
                if (_hudPromptInteractableId != _interactable || _hudPromptGamepad != usesGamepad)
                {
                    _hudPromptInteractableId = _interactable;
                    _hudPromptGamepad = usesGamepad;
                    string prompt = _copy.Ui("ui.hud.interact_prompt").Replace("{key}", BindingLabel(_interactAction))
                        .Replace("{target}", _copy.Name(_encounter.Units[_interactable].Def.Id));
                    _hud.SetInteractPrompt(prompt);
                }
                return;
            }

            _hudPromptInteractableId = int.MinValue;
            _hud.SetInteractPrompt(null);
        }

        private void DisposeHud()
        {
            _hud?.Dispose();
            _hud = null;
        }
    }
}
