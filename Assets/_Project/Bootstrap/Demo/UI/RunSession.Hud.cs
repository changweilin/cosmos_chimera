using System;
using System.Collections.Generic;
using Cosmimera.Bootstrap.Demo.UI;
using Cosmimera.Combat;
using Cosmimera.Creatures;
using Cosmimera.WorldGen;
using Unity.Mathematics;
using UnityEngine;

namespace Cosmimera.Bootstrap.Demo
{
    public sealed partial class RunSession
    {
        private GameHudView _hud;
        private readonly List<GameHudView.TeamMemberDisplay> _hudTeamBuffer = new List<GameHudView.TeamMemberDisplay>(6);
        private int _lastInteractTargetHud = int.MinValue;
        private int _lastInteractChoiceHud = int.MinValue;
        private string _lastObserveTextHud;

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

        private void SyncQuestHud()
        {
            string title = _copy.ChainTitle(_plan.ChainId);
            string body = WeatherStatusText();
            string moveText = WeatherMovementText();
            if (!string.IsNullOrEmpty(moveText))
            {
                body += "\n" + moveText;
            }

            if (_setup.IsBeginnerQuest && _bqTracker != null)
            {
                body += "\n" + _copy.Ui("ui.hud.quest_tracker") + "：新手教學進行中";
            }
            else if (!_quest.IsComplete)
            {
                RunQuestStep s = _quest.Current;
                string stepText = _copy.Step(StepType(s.Kind), (int)(_plan.Seed % 3))
                    .Replace("{count}", (s.Count - _quest.Progress).ToString())
                    .Replace("{element}", _copy.Element(s.Element.ToString()))
                    .Replace("{poi}", _copy.Theme(_plan.Regions[s.Region].ThemeId))
                    .Replace("{boss}", _copy.Name(_plan.Spawns[_plan.Quest[_plan.Quest.Count - 1].TargetSpawn].CreatureId));
                body += "\n" + _copy.Ui("ui.hud.quest_tracker") + "：" + stepText;

                if (TryQuestTarget(s, out float3 target))
                {
                    float3 here = _rig.Config.ToDirection(PlayerPosition());
                    float meters = math.acos(math.clamp(math.dot(here, target), -1f, 1f)) * _rig.Config.Radius;
                    bool hasBearing = DemoNavigation.TryBearing(here, target, _rig.CameraRig.Heading, out float angle);
                    string bearing = !hasBearing ? _copy.Ui("ui.hud.bearing_unavailable")
                        : math.abs(angle) < 10f ? _copy.Ui("ui.hud.bearing_ahead")
                        : _copy.Ui(angle < 0f ? "ui.hud.bearing_left" : "ui.hud.bearing_right")
                            .Replace("{angle}", math.abs(angle).ToString("0"));
                    body += $"\n{bearing} · {meters:0} m";
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
                string tTitle = _copy.Ui("ui.trial.boss_title").Replace("{terrain}", TrialTerrainName) + " · " + _copy.Name(trialBoss.Def.Id);
                _hud.SetBossBar(true, tTitle, trialBoss.Combatant.Hp, trialBoss.Combatant.MaxHp);
                return;
            }

            int bossId = BossUnit();
            if (bossId >= 0 && _wild.TryGetValue(bossId, out WildInfo info) && !info.Gone)
            {
                EncounterUnit boss = _encounter.Units[bossId];
                if (_region == _plan.BossRegion || boss.Engaged)
                {
                    string title = _copy.Epithet(_plan.PlanetTypeId, (int)(_plan.Seed % 3)) + _copy.Name(boss.Def.Id);
                    _hud.SetBossBar(true, title, boss.Combatant.Hp, boss.Combatant.MaxHp);
                    return;
                }
            }

            _hud.SetBossBar(false);
        }

        private void SyncTeamHud()
        {
            _hudTeamBuffer.Clear();
            foreach (int id in _team.Pool)
            {
                EncounterUnit u = _encounter.Units[id];
                bool active = In(_team.Active, id);
                string mark = id == _mainId ? "★ " : active ? "● " : "○ ";
                string overloadTag = u.Combatant.IsOverloaded ? $" {_copy.Ui("ui.hud.overload_active")}" : string.Empty;
                string detail = string.Empty;
                if (active)
                {
                    int slots = u.Combatant.Cooldowns.SlotCount;
                    for (int s = 0; s < slots; s++)
                    {
                        if (s > 0) detail += " ";
                        detail += u.Combatant.Cooldowns.IsReady(s, _nowMs)
                            ? $"S{s + 1}:{_copy.Ui("ui.hud.cd_ready")}"
                            : $"S{s + 1}:{(u.Combatant.Cooldowns.ReadyAtMs(s) - _nowMs) / 1000f:0.0}s";
                    }
                }
                if (u.Combatant.IsFainted)
                {
                    detail = _copy.Ui("ui.hud.knocked_out");
                }

                _hudTeamBuffer.Add(new GameHudView.TeamMemberDisplay(
                    $"{mark}{_copy.Name(u.Def.Id)} LV{u.Level}{overloadTag}",
                    u.Combatant.Hp,
                    u.Combatant.MaxHp,
                    detail,
                    active));
            }

            _hud.SetTeam(_hudTeamBuffer);
            _hud.SetJournalButtonText(_copy.Ui("ui.journal.open") + " [" + BindingLabel(_journalAction) + "]");
        }

        private void SyncPlayerHud()
        {
            long rollCdLeft = math.max(0L, _lastRollAtMs + math.max(_balance.Player.RollDurationMs,
                (long)Math.Ceiling(_balance.Player.RollCooldownMs * _playerWeather.PlayerRollCooldownMultiplier *
                    (100 + _foodBuffs.GetRollCooldownModPercent(_nowMs)) / 100d)) - _nowMs);
            string rollText = _copy.Ui("ui.hud.roll_prompt").Replace("{key}", BindingLabel(_rollAction)) +
                (rollCdLeft > 0 ? $" ({rollCdLeft / 1000f:0.0}s)" : $" {_copy.Ui("ui.hud.cd_ready")}");
            _hud.SetPlayerStats($"◆ {_copy.Ui("ui.hud.hp")} {_vitals.Hp}/{_vitals.MaxHp}", rollText);
        }

        private void SyncControlsAndPromptsHud()
        {
            string help = _copy.Ui(_inputHints.UsesGamepad ? "ui.hud.gamepad_help" : "ui.hud.exploration_help")
                .Replace("{jump}", BindingLabel(_actions.FindAction("Player/Jump")))
                .Replace("{roll}", BindingLabel(_rollAction))
                .Replace("{view}", BindingLabel(_actions.FindAction("Player/ToggleView")))
                .Replace("{interact}", BindingLabel(_interactAction))
                .Replace("{target}", BindingLabel(_cycleTargetAction))
                .Replace("{journal}", BindingLabel(_journalAction)).Replace("{codex}", BindingLabel(_codexAction))
                .Replace("{capture}", BindingLabel(_captureAction)).Replace("{cycle}", BindingLabel(_cycleVesselAction))
                .Replace("{abandon}", BindingLabel(_abandonAction));
            _hud.SetControlsHint(help);

            _hud.SetToast(_nowMs < _toastUntilMs ? _toast : null);

            if (_interactTarget >= 0)
            {
                _hud.SetInteractPrompt(null);
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
                string prompt = _copy.Ui("ui.hud.capture_available")
                    .Replace("{capture}", BindingLabel(_captureAction)).Replace("{cycle}", BindingLabel(_cycleVesselAction))
                    .Replace("{name}", _copy.Name(_encounter.Units[capture].Def.Id))
                    .Replace("{seconds}", seconds.ToString("0.0"));
                _hud.SetInteractPrompt(prompt);
                return;
            }

            if (_interactable >= 0)
            {
                string prompt = _copy.Ui("ui.hud.interact_prompt").Replace("{key}", BindingLabel(_interactAction))
                    .Replace("{target}", _copy.Name(_encounter.Units[_interactable].Def.Id));
                _hud.SetInteractPrompt(prompt);
                return;
            }

            _hud.SetInteractPrompt(null);
        }

        private void DisposeHud()
        {
            _hud?.Dispose();
            _hud = null;
        }
    }
}
