using System.Collections.Generic;
using Cosmimera.Codex;
using Cosmimera.Combat;
using Cosmimera.Creatures;
using Cosmimera.GameData;
using Cosmimera.WorldGen;
using Unity.Mathematics;
using UnityEngine;

namespace Cosmimera.Bootstrap.Demo
{
    // REQ: docs/design/beginner-quest.md — in-planet stages 4..6 of the P7-02 beginner quest:
    // Stage 4: Friend 1vs1 + combat tutorial → Stage 5: Friend field tutorial (adventure, 1vs1, capture, heal) →
    // Stage 6: Boss + Rival vs Player + Friend (first 2vs2 tutorial).
    public sealed partial class RunSession
    {
        private const float BqBossTriggerDistM = 18f;

        private BeginnerQuestTracker _bqTracker;
        private int _bqFriendBattleTrainerId = -1;
        private int _bqFriendOpponentUnitId = -1;
        private int _bqBossTrainerId = -1;
        private int _bqBossUnitId = -1;
        private int _bqRivalUnitId = -1;
        private int _bqFriendAllyUnitId = -1;
        private int _bqBossSpawnIndex = -1;
        private CreatureDef _bqBossDef;
        private int _bqBossLevel;
        private float3 _bqBossDirection;
        private WildIndividual _bqBossIndividual;
        private int _bqTutorialVesselsRemaining;

        private bool HasActiveBeginnerTrainerBattle =>
            _bqFriendBattleTrainerId >= 0 || _bqBossTrainerId >= 0;

        internal BeginnerQuestTracker BeginnerTrackerForTests => _bqTracker;
        internal int BeginnerFriendBattleTrainerIdForTests => _bqFriendBattleTrainerId;
        internal int BeginnerFriendOpponentUnitIdForTests => _bqFriendOpponentUnitId;
        internal int BeginnerBossTrainerIdForTests => _bqBossTrainerId;
        internal int BeginnerFriendAllyUnitIdForTests => _bqFriendAllyUnitId;
        internal int BeginnerBossUnitIdForTests => _bqBossUnitId;
        internal int BeginnerRivalUnitIdForTests => _bqRivalUnitId;
        internal int BeginnerVesselsRemainingForTests => _bqTutorialVesselsRemaining;
        internal int BeginnerHealFoodCountForTests => _foodInventory.CountFood(BeginnerQuestRules.TutorialHealItemId);
        internal void TriggerBeginnerBoss2v2ForTests() => StartBeginnerBossAndRival2v2();
        internal void CompleteBeginnerFieldStepsForTests()
        {
            NotifyBeginnerAdventureControl();
            NotifyBeginnerWildDefeated();
            NotifyBeginnerWildCaptured();
            NotifyBeginnerHealUsed();
        }

        private void ConfigureBeginnerQuest()
        {
            if (!_setup.IsBeginnerQuest)
            {
                return;
            }

            _bqTracker = new BeginnerQuestTracker();
        }

        private void ReserveBeginnerBossSpawn(int spawnIndex, CreatureDef def, int level, float3 direction,
            WildIndividual individual)
        {
            _bqBossSpawnIndex = spawnIndex;
            _bqBossDef = def;
            _bqBossLevel = level;
            _bqBossDirection = direction;
            _bqBossIndividual = individual;
        }

        private void StartBeginnerFriendBattle(float3 player)
        {
            CreatureDef opponentDef = _setup.FriendStarter ?? _setup.RivalStarter;
            if (!_setup.IsBeginnerQuest || opponentDef == null)
            {
                return;
            }

            _bqFriendBattleTrainerId = _encounter.RegisterTrainer();
            EncounterUnit friendOpp = _encounter.AddTrainerCreature(opponentDef, null,
                BesidePlayer(player, 2), _progression.StarterLevel, _bqFriendBattleTrainerId, 0);
            _bqFriendOpponentUnitId = friendOpp.Id;
            CreateView(friendOpp, SpawnRole.Normal);
            _encounter.StartTrainerBattle(_bqFriendBattleTrainerId);
            _vessel = _setup.PlayerStarterVessel;

            List<DialogueLine> intro = BeginnerQuestRules.FriendBattleIntroDialogue(_setup.FriendId ?? string.Empty);
            if (intro.Count > 0)
            {
                Toast($"{intro[0].Speaker}：{intro[0].FallbackText}");
            }
        }

        private void StepBeginnerQuest()
        {
            if (!_setup.IsBeginnerQuest || _bqTracker == null)
            {
                return;
            }

            if (_bqTracker.Stage != BeginnerQuestStage.FriendFieldTutorial || _bqBossDef == null)
            {
                return;
            }

            if (_team.InCombat || _encounter.HasActiveTrainerBattle || _bqBossTrainerId >= 0)
            {
                return;
            }

            float3 here = _rig.Config.ToDirection(PlayerPosition());
            float distM = math.acos(math.clamp(math.dot(here, _bqBossDirection), -1f, 1f)) * _rig.Config.Radius;
            if (_region == _plan.BossRegion || distM <= BqBossTriggerDistM)
            {
                StartBeginnerBossAndRival2v2();
            }
        }

        private void StartBeginnerBossAndRival2v2()
        {
            if (_bqTracker == null || !_bqTracker.OnBossLairReached())
            {
                return;
            }

            float3 player = PlayerPosition();
            // Keep strictly 1 player creature + 1 friend AI ally on the field for the 2vs2 tutorial.
            while (_team.Active.Count > 1)
            {
                int extra = _team.Active[_team.Active.Count - 1];
                if (_team.Recall(extra, _nowMs) == TeamResult.Ok)
                {
                    _encounter.Unfield(extra);
                }
                else
                {
                    break;
                }
            }

            CreatureDef friendDef = _setup.FriendStarter ?? _setup.RivalStarter;
            if (friendDef != null)
            {
                EncounterUnit ally = _encounter.AddCreature(EncounterSide.Player, friendDef, null,
                    BesidePlayer(player, 1), _progression.StarterLevel);
                _encounter.Field(ally.Id, BesidePlayer(player, 1), false, _nowMs);
                _bqFriendAllyUnitId = ally.Id;
                CreateView(ally, SpawnRole.Normal);
            }

            _bqBossTrainerId = _encounter.RegisterTrainer();
            EncounterUnit boss = _encounter.AddTrialBoss(_bqBossDef, null, _bqBossDirection, _bqBossLevel,
                _bqBossIndividual, _bqBossTrainerId, false);
            _bqBossUnitId = boss.Id;
            CreateView(boss, SpawnRole.Boss);

            CreatureDef rivalDef = _setup.RivalStarter ?? _bqBossDef;
            EncounterUnit rival = _encounter.AddTrainerCreature(rivalDef, null,
                BesidePlayer(_bqBossDirection, 2), _progression.StarterLevel, _bqBossTrainerId, 1);
            _bqRivalUnitId = rival.Id;
            CreateView(rival, SpawnRole.Normal);

            if (!_encounter.StartTrainerBattle(_bqBossTrainerId))
            {
                CleanupBeginner2v2Units();
                _bqBossTrainerId = -1;
                _bqTracker.OnBossAndRivalWipeout();
                return;
            }

            List<DialogueLine> intro = BeginnerQuestRules.BossAndRivalIntroDialogue(
                _setup.FriendId ?? string.Empty, _setup.RivalId ?? string.Empty);
            if (intro.Count > 0)
            {
                DialogueLine last = intro[intro.Count - 1];
                Toast($"{last.Speaker}：{last.FallbackText}");
            }
        }

        private bool CheckBeginnerQuestEnd()
        {
            if (!_setup.IsBeginnerQuest || _bqTracker == null)
            {
                return false;
            }

            if (_bqFriendBattleTrainerId >= 0)
            {
                TrainerBattleState state = _encounter.TrainerState(_bqFriendBattleTrainerId);
                bool poolAlive = AnyPartyPoolAlive();
                if (state == TrainerBattleState.Defeated || state == TrainerBattleState.Idle || !poolAlive)
                {
                    bool won = state == TrainerBattleState.Defeated;
                    _bqFriendBattleTrainerId = -1;
                    RemoveBeginnerUnit(ref _bqFriendOpponentUnitId);

                    RestoreBeginnerParty();
                    RefillField(PlayerPosition());
                    GrantBeginnerTutorialSupplies();
                    _bqTracker.OnFriendBattleEnded(won);

                    List<DialogueLine> lines = BeginnerQuestRules.FriendBattleOutcomeDialogue(
                        _setup.FriendId ?? string.Empty, won);
                    if (lines.Count > 0)
                    {
                        Toast($"{lines[0].Speaker}：{lines[0].FallbackText}");
                    }

                    return true;
                }
            }

            if (_bqBossTrainerId >= 0)
            {
                TrainerBattleState state = _encounter.TrainerState(_bqBossTrainerId);
                bool poolAlive = AnyPartyPoolAlive();
                if (state == TrainerBattleState.Defeated)
                {
                    _bqBossTrainerId = -1;
                    CleanupBeginner2v2Units();
                    CaptureBeginnerBoss();
                    if (_bqBossSpawnIndex >= 0 && _bqBossDef != null)
                    {
                        _quest.OnKnockedOut(_bqBossSpawnIndex, RunPlanner.MainElement(_bqBossDef));
                        PayQuest(new HashSet<int>(_appeared));
                    }

                    _bqTracker.OnBossAndRivalDefeated();
                    List<DialogueLine> victory = BeginnerQuestRules.BossAndRivalVictoryDialogue(
                        _setup.FriendId ?? string.Empty, _setup.RivalId ?? string.Empty);
                    if (victory.Count > 0)
                    {
                        DialogueLine last = victory[victory.Count - 1];
                        Toast($"{last.Speaker}：{last.FallbackText}");
                    }

                    End(true, false);
                    return true;
                }

                if (state == TrainerBattleState.Idle || !poolAlive)
                {
                    RecoverBeginnerWipeout();
                    return true;
                }
            }

            return false;
        }

        private bool TryRecoverBeginnerWipeout()
        {
            if (!_setup.IsBeginnerQuest || _bqTracker == null)
            {
                return false;
            }

            // Stage 4 loss continues to Stage 5 instead of ending the run.
            if (_bqFriendBattleTrainerId >= 0)
            {
                _encounter.CancelTrainerBattle(_bqFriendBattleTrainerId, _nowMs);
                _bqFriendBattleTrainerId = -1;
                RemoveBeginnerUnit(ref _bqFriendOpponentUnitId);
                RestoreBeginnerParty();
                RefillField(PlayerPosition());
                GrantBeginnerTutorialSupplies();
                _bqTracker.OnFriendBattleEnded(false);
                List<DialogueLine> lines = BeginnerQuestRules.FriendBattleOutcomeDialogue(
                    _setup.FriendId ?? string.Empty, false);
                if (lines.Count > 0)
                {
                    Toast($"{lines[0].Speaker}：{lines[0].FallbackText}");
                }

                return true;
            }

            RecoverBeginnerWipeout();
            return true;
        }

        private void RecoverBeginnerWipeout()
        {
            if (_bqBossTrainerId >= 0)
            {
                _encounter.CancelTrainerBattle(_bqBossTrainerId, _nowMs);
                _bqBossTrainerId = -1;
                CleanupBeginner2v2Units();
                _bqTracker?.OnBossAndRivalWipeout();
            }

            RestoreBeginnerParty();
            if (_rig != null && _rig.Character != null)
            {
                float3 campDir = _plan.LandingDirection;
                Vector3 campWorld = _rig.Config.ToWorld(campDir, _surface.GroundAltitude(campDir) + 0.3f);
                _rig.Character.Teleport(campWorld);
            }

            RefillField(PlayerPosition());
            Toast("全隊倒下，已返回降落營地回復！任務進度與已收服宙獸保留。");
        }

        private void GrantBeginnerTutorialSupplies()
        {
            _bqTutorialVesselsRemaining = BeginnerQuestRules.StarterVesselGrantCount;
            _vessel = _setup.PlayerStarterVessel;
            _foodInventory.AddFood(BeginnerQuestRules.TutorialHealItemId, BeginnerQuestRules.TutorialHealGrantCount);
        }

        private void CaptureBeginnerBoss()
        {
            if (_bqBossDef == null)
            {
                return;
            }

            _outcome.Captured.Add((_bqBossDef.Id, _vessel, _progression.XpThreshold(_bqBossLevel),
                _bqBossIndividual != null ? _bqBossIndividual.Niche.Id : null,
                _bqBossIndividual != null ? _bqBossIndividual.Habit : Habit.Tolerant));
            EncyclopediaCatalog.RecordOwned(_outcome.Codex, _bqBossDef);
            _quest.OnCaptured();
        }

        private void RestoreBeginnerParty()
        {
            foreach (int id in _partyUnits)
            {
                Combatant c = _encounter.Units[id].Combatant;
                if (c.IsFainted)
                {
                    c.ReviveByPercent(100, _nowMs);
                    _team.TryClearRetired(id);
                }
                else
                {
                    c.HealByPercent(100, _nowMs);
                }
            }

            _vitals.Revive(_nowMs);
        }

        private bool AnyPartyPoolAlive()
        {
            foreach (int id in _team.Pool)
            {
                if (!_encounter.Units[id].Combatant.IsFainted)
                {
                    return true;
                }
            }

            return false;
        }

        private void CleanupBeginner2v2Units()
        {
            RemoveBeginnerUnit(ref _bqBossUnitId);
            RemoveBeginnerUnit(ref _bqRivalUnitId);
            RemoveBeginnerUnit(ref _bqFriendAllyUnitId);
        }

        private void RemoveBeginnerUnit(ref int unitId)
        {
            if (unitId < 0)
            {
                return;
            }

            int id = unitId;
            unitId = -1;
            if (_lockedTarget == id)
            {
                _lockedTarget = -1;
            }

            if (_encounter.Units[id].Side == EncounterSide.Wild)
            {
                _encounter.Remove(id);
            }
            else if (_encounter.Units[id].Fielded)
            {
                _encounter.Unfield(id);
            }
            if (_views.TryGetValue(id, out ICreatureView view))
            {
                view.Dispose();
                _views.Remove(id);
                _viewDefs.Remove(id);
                _viewRoles.Remove(id);
            }
        }

        private void NotifyBeginnerAdventureControl()
        {
            if (_bqTracker != null && _bqTracker.OnAdventureControlUsed())
            {
                ShowBeginnerStepToast(_bqTracker.FieldStep);
            }
        }

        private void NotifyBeginnerWildDefeated()
        {
            if (_bqTracker != null && _bqTracker.OnWildDefeated())
            {
                ShowBeginnerStepToast(_bqTracker.FieldStep);
            }
        }

        private void NotifyBeginnerWildCaptured()
        {
            if (_bqTutorialVesselsRemaining > 0)
            {
                _bqTutorialVesselsRemaining--;
            }

            if (_bqTracker != null && _bqTracker.OnWildCaptured())
            {
                ShowBeginnerStepToast(_bqTracker.FieldStep);
            }
        }

        private void NotifyBeginnerHealUsed()
        {
            if (_bqTracker != null && _bqTracker.OnHealItemUsed())
            {
                ShowBeginnerStepToast(_bqTracker.FieldStep);
            }
        }

        private void ShowBeginnerStepToast(BeginnerFieldStep step)
        {
            DialogueLine line = BeginnerQuestRules.FieldStepDialogue(_setup.FriendId ?? string.Empty, step);
            Toast($"{line.Speaker}：{ResolveBeginnerDialogue(line)}");
        }

        private string ResolveBeginnerDialogue(DialogueLine line)
        {
            if (_copy != null && !string.IsNullOrEmpty(line.TextKey))
            {
                string localized = _copy.Ui(line.TextKey);
                if (localized != line.TextKey)
                {
                    return localized;
                }
            }

            return line.FallbackText;
        }

        private void DrawBeginnerQuestHud()
        {
            if (!_setup.IsBeginnerQuest || _bqTracker == null)
            {
                return;
            }

            string instruction = _bqTracker.CurrentInstruction(
                _setup.FriendId ?? string.Empty, _setup.RivalId ?? string.Empty);
            GUILayout.Label($"<b>[新手教學]</b> {instruction}", new GUIStyle(_label) { wordWrap = true });
            if (_bqTracker.Stage == BeginnerQuestStage.FriendFieldTutorial &&
                _bqTracker.FieldStep == BeginnerFieldStep.ReachBossLair &&
                _bqBossDef != null)
            {
                float3 here = _rig.Config.ToDirection(PlayerPosition());
                float meters = math.acos(math.clamp(math.dot(here, _bqBossDirection), -1f, 1f)) * _rig.Config.Radius;
                bool hasBearing = DemoNavigation.TryBearing(here, _bqBossDirection, _rig.CameraRig.Heading, out float angle);
                string bearing = !hasBearing ? _copy.Ui("ui.hud.bearing_unavailable")
                    : math.abs(angle) < 10f ? _copy.Ui("ui.hud.bearing_ahead")
                    : _copy.Ui(angle < 0f ? "ui.hud.bearing_left" : "ui.hud.bearing_right")
                        .Replace("{angle}", math.abs(angle).ToString("0"));
                GUILayout.Label($"BOSS 巢穴：{bearing} · {meters:0} m", _label);
            }
        }
    }
}
