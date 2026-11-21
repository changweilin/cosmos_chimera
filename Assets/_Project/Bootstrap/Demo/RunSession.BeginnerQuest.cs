using System.Collections.Generic;
using Cosmimera.Codex;
using Cosmimera.Combat;
using Cosmimera.Creatures;
using Cosmimera.GameData;
using Cosmimera.Planet;
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

        private sealed class LobbyGatekeeperState
        {
            public int SettlementIndex;
            public LobbyGatekeeper Role;
            public float3 Dir;
            public float3 Facing;
            public float BlockRadiusM;
            public GameObject Root;
            public readonly List<Object> Owned = new List<Object>();
        }

        private readonly List<LobbyGatekeeperState> _lobbyGatekeepers = new List<LobbyGatekeeperState>();
        private int _activeGatekeeperSettlementIndex = -1;
        private long _gatekeeperCooldownUntilMs;

        internal IReadOnlyCollection<int> DiscoveredSettlementsForTests => _discoveredSettlements;
        internal int ActiveGatekeeperSettlementForTests => _activeGatekeeperSettlementIndex;

        internal bool TriggerLobbyGatekeeperForTests(int settlementIndex)
        {
            for (int i = 0; i < _lobbyGatekeepers.Count; i++)
            {
                if (_lobbyGatekeepers[i].SettlementIndex == settlementIndex)
                {
                    return StartGatekeeperBattle(_lobbyGatekeepers[i]);
                }
            }

            return false;
        }

        private void ConfigureLobbyGatekeepers(Shader toonShader)
        {
            if (!_setup.IsLobbyPlanet || _layout == null)
            {
                return;
            }

            float radius = _rig.Config.Radius;
            for (int i = 0; i < _layout.Settlements.Count; i++)
            {
                LobbyGatekeeper role = i < LobbyPlanet.SettlementRoster.Count
                    ? LobbyPlanet.GatekeeperForSettlement(i)
                    : LobbyGatekeeper.None;
                if (role == LobbyGatekeeper.None || _discoveredSettlements.Contains(i))
                {
                    continue;
                }

                RunSettlement s = _layout.Settlements[i];
                float blockRadiusM = math.max(24f, LayoutClearance.MinSettlementRadiusM + 8f);
                float3 towardLanding = SphericalTangent(s.Center,
                    _layout.Settlements[0].Center - s.Center * math.dot(_layout.Settlements[0].Center, s.Center),
                    PlanetFrame.FromUp(s.Center).North);
                float3 preferred = math.normalize(s.Center * radius + towardLanding * (blockRadiusM + 2.5f));
                float3 gateDir = FindClearTrialGround(preferred, s.Center, blockRadiusM + 1.5f);
                var gate = new LobbyGatekeeperState
                {
                    SettlementIndex = i,
                    Role = role,
                    Dir = gateDir,
                    Facing = SphericalTangent(gateDir, -towardLanding, PlanetFrame.FromUp(gateDir).North),
                    BlockRadiusM = blockRadiusM,
                };

                if (toonShader != null)
                {
                    BuildGatekeeperView(gate, toonShader);
                }

                _lobbyGatekeepers.Add(gate);
            }
        }

        private void BuildGatekeeperView(LobbyGatekeeperState gate, Shader toonShader)
        {
            NpcSpec spec = NpcMeshes.SpecForTrialMaster(_setup.Seed ^ (ulong)(gate.SettlementIndex + 101));
            var root = new GameObject($"Gatekeeper_{gate.SettlementIndex + 1}_{gate.Role}");
            Mesh mesh = NpcMeshes.Build(spec, root.transform, out ResidentStride stride);
            var renderer = root.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;
            renderer.bones = stride.Bones;
            renderer.rootBone = root.transform;
            Color primary = gate.Role == LobbyGatekeeper.Friend
                ? new Color(0.28f, 0.72f, 0.92f)
                : new Color(0.86f, 0.25f, 0.32f);
            Material body = PlanetLook.CreatureMaterial(toonShader, primary, 0.02f);
            Material accent = PlanetLook.CreatureMaterial(toonShader, new Color(0.95f, 0.92f, 0.8f), 0f);
            renderer.sharedMaterials = new[] { body, accent };

            GameObject halo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.Destroy(halo.GetComponent<Collider>());
            halo.transform.SetParent(root.transform, false);
            halo.transform.localPosition = new Vector3(0f, 0.02f, 0f);
            halo.transform.localScale = new Vector3(1.5f, 0.01f, 1.5f);
            Material haloMat = PlanetLook.CreatureMaterial(toonShader, primary, 0f);
            halo.GetComponent<Renderer>().sharedMaterial = haloMat;

            float3 world = _rig.Config.ToWorld(gate.Dir, _surface.GroundAltitude(gate.Dir) - 0.03f);
            root.transform.SetPositionAndRotation(new Vector3(world.x, world.y, world.z),
                Quaternion.LookRotation((Vector3)gate.Facing, (Vector3)gate.Dir));

            gate.Owned.Add(mesh);
            gate.Owned.Add(body);
            gate.Owned.Add(accent);
            gate.Owned.Add(haloMat);
            gate.Root = root;
        }

        private void StepLobbyGatekeepers(float3 player)
        {
            if (!_setup.IsLobbyPlanet || _layout == null || _lobbyGatekeepers.Count == 0)
            {
                return;
            }

            float3 playerUp = _rig.Config.ToDirection(player);
            float playerAlt = _rig.Config.Altitude(player);
            float radius = _rig.Config.Radius;
            for (int i = 0; i < _lobbyGatekeepers.Count; i++)
            {
                LobbyGatekeeperState gate = _lobbyGatekeepers[i];
                if (_discoveredSettlements.Contains(gate.SettlementIndex))
                {
                    continue;
                }

                RunSettlement s = _layout.Settlements[gate.SettlementIndex];
                if (math.abs(playerAlt - SettlementAltitudeAt(s)) > 25f)
                {
                    continue;
                }

                float distM = math.distance(playerUp, s.Center) * radius;
                if (distM > gate.BlockRadiusM)
                {
                    continue;
                }

                // Prevent entering the gated settlement before defeating its Friend/Rival gatekeeper.
                float3 radialOut = SphericalTangent(s.Center, playerUp - s.Center * math.dot(playerUp, s.Center),
                    SphericalTangent(s.Center, gate.Dir, PlanetFrame.FromUp(s.Center).North));
                float3 pushTarget = math.normalize(s.Center * radius + radialOut * (gate.BlockRadiusM + 3f));
                float3 safeOutside = FindClearTrialGround(pushTarget, s.Center, gate.BlockRadiusM + 2f);
                if (_rig != null && _rig.Character != null)
                {
                    Vector3 worldPos = _rig.Config.ToWorld(safeOutside, _surface.GroundAltitude(safeOutside) + 0.1f);
                    _rig.Character.Teleport(worldPos);
                    UpdateEnvironment(worldPos);
                }

                if (!_team.InCombat && !_encounter.HasActiveTrainerBattle && !HasActiveBeginnerTrainerBattle &&
                    _activeEncounterTrainerId < 0 && _nowMs >= _gatekeeperCooldownUntilMs)
                {
                    StartGatekeeperBattle(gate);
                }

                return;
            }
        }

        private bool StartGatekeeperBattle(LobbyGatekeeperState gate)
        {
            if (_wildPool == null || _wildPool.Count == 0 || _activeEncounterTrainerId >= 0)
            {
                return false;
            }

            float3 player = PlayerPosition();
            RefillField(player);
            if (_team.Active.Count == 0)
            {
                return false;
            }

            if (_interactTarget >= 0)
            {
                CloseInteract();
            }

            RunSettlement s = _layout.Settlements[gate.SettlementIndex];
            int level = math.max(10, _progression.WildLevel(D0Difficulty, _plan.Regions[s.Region].Tier, SpawnRole.Boss));
            int teamCount = gate.SettlementIndex >= 4 ? 2 : 1;
            int trainerId = _encounter.RegisterTrainer();
            _activeTrainerUnitIds.Clear();

            CreatureDef primaryDef = gate.Role == LobbyGatekeeper.Friend
                ? (_setup.FriendStarter ?? _wildPool[gate.SettlementIndex % _wildPool.Count])
                : (_setup.RivalStarter ?? _wildPool[(gate.SettlementIndex + 1) % _wildPool.Count]);

            for (int m = 0; m < teamCount; m++)
            {
                CreatureDef def = m == 0 ? primaryDef : _wildPool[(gate.SettlementIndex * 3 + m) % _wildPool.Count];
                EncounterUnit u = _encounter.AddTrainerCreature(def, null, BesidePlayer(player, m + 1), level, trainerId, m);
                _activeTrainerUnitIds.Add(u.Id);
                CreateView(u, SpawnRole.Normal);
            }

            if (!_encounter.StartTrainerBattle(trainerId))
            {
                CleanupActiveTrainerUnits();
                return false;
            }

            _activeEncounterTrainerId = trainerId;
            _activeGatekeeperSettlementIndex = gate.SettlementIndex;
            if (!_team.InCombat)
            {
                BeginCombat();
            }

            string who = GatekeeperDisplayName(gate.Role);
            Toast($"第 {gate.SettlementIndex + 1} 號聚落守門戰：{who} 攔住了去路！擊敗他才能進入聚落。");
            return true;
        }

        private bool ResolveGatekeeperBattleEnd(bool won)
        {
            if (_activeGatekeeperSettlementIndex < 0)
            {
                return false;
            }

            int settlementIndex = _activeGatekeeperSettlementIndex;
            _activeGatekeeperSettlementIndex = -1;
            LobbyGatekeeper role = settlementIndex < LobbyPlanet.SettlementRoster.Count
                ? LobbyPlanet.GatekeeperForSettlement(settlementIndex)
                : LobbyGatekeeper.None;
            string who = GatekeeperDisplayName(role);

            RestoreBeginnerParty();
            if (won)
            {
                _discoveredSettlements.Add(settlementIndex);
                for (int i = 0; i < _lobbyGatekeepers.Count; i++)
                {
                    if (_lobbyGatekeepers[i].SettlementIndex == settlementIndex && _lobbyGatekeepers[i].Root != null)
                    {
                        _lobbyGatekeepers[i].Root.SetActive(false);
                    }
                }

                RefillField(PlayerPosition());
                Toast($"擊敗第 {settlementIndex + 1} 號聚落的{who}！聚落已開放進入並可用「候鳥歸途」傳送。");
            }
            else
            {
                _gatekeeperCooldownUntilMs = _nowMs + 3000;
                for (int i = 0; i < _lobbyGatekeepers.Count; i++)
                {
                    if (_lobbyGatekeepers[i].SettlementIndex == settlementIndex)
                    {
                        float3 safeDir = _lobbyGatekeepers[i].Dir;
                        if (_rig != null && _rig.Character != null)
                        {
                            Vector3 worldPos = _rig.Config.ToWorld(safeDir, _surface.GroundAltitude(safeDir) + 0.1f);
                            _rig.Character.Teleport(worldPos);
                            UpdateEnvironment(worldPos);
                        }
                        break;
                    }
                }

                RefillField(PlayerPosition());
                Toast($"未能擊敗第 {settlementIndex + 1} 號聚落的{who}，無法進入該聚落。");
            }

            return true;
        }

        private bool TryRecoverLobbyWipeout()
        {
            if (!_setup.IsLobbyPlanet)
            {
                return false;
            }

            if (_activeEncounterTrainerId >= 0)
            {
                int trainerId = _activeEncounterTrainerId;
                _activeEncounterTrainerId = -1;
                _activeWanderingTrainerIndex = -1;
                _encounter.CancelTrainerBattle(trainerId, _nowMs);
                CleanupActiveTrainerUnits();
                if (ResolveGatekeeperBattleEnd(false))
                {
                    return true;
                }
            }

            RecoverLobbyAtNearestTown();
            return true;
        }

        private void RecoverLobbyAtNearestTown()
        {
            RestoreBeginnerParty();
            float3 recoveryDir = _plan.LandingDirection;
            float recoveryAlt = _surface.GroundAltitude(recoveryDir) + 0.3f;
            if (_layout != null && _rig != null)
            {
                float3 pDir = _rig.Config.ToDirection(PlayerPosition());
                float bestDot = -2f;
                foreach (int idx in _discoveredSettlements)
                {
                    if (idx < 0 || idx >= _layout.Settlements.Count)
                    {
                        continue;
                    }

                    RunSettlement s = _layout.Settlements[idx];
                    float d = math.dot(pDir, s.Center);
                    if (d > bestDot)
                    {
                        bestDot = d;
                        recoveryDir = s.Center;
                        recoveryAlt = SettlementAltitudeAt(s) + 0.5f;
                    }
                }
            }

            if (_rig != null && _rig.Character != null)
            {
                RelocatePlayerAndParty(recoveryDir, recoveryAlt);
            }

            RefillField(PlayerPosition());
            Toast("全隊倒下，已返回最近已開放的聚落回復。");
        }

        private string GatekeeperDisplayName(LobbyGatekeeper role)
        {
            if (role == LobbyGatekeeper.Friend)
            {
                return string.IsNullOrEmpty(_setup.FriendId)
                    ? "好友"
                    : $"好友「{BeginnerQuestRules.CharacterDisplayName(_setup.FriendId)}」";
            }

            if (role == LobbyGatekeeper.Rival)
            {
                return string.IsNullOrEmpty(_setup.RivalId)
                    ? "宿敵"
                    : $"宿敵「{BeginnerQuestRules.CharacterDisplayName(_setup.RivalId)}」";
            }

            return string.Empty;
        }

        private void DisposeLobbyGatekeepers()
        {
            for (int i = 0; i < _lobbyGatekeepers.Count; i++)
            {
                LobbyGatekeeperState gate = _lobbyGatekeepers[i];
                if (gate.Root != null)
                {
                    Destroy(gate.Root);
                }

                for (int o = 0; o < gate.Owned.Count; o++)
                {
                    if (gate.Owned[o] != null)
                    {
                        Destroy(gate.Owned[o]);
                    }
                }
            }

            _lobbyGatekeepers.Clear();
        }
    }
}
