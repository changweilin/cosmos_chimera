using System.Collections.Generic;
using Cosmimera.Codex;
using Cosmimera.Combat;
using Cosmimera.Core.Random;
using Cosmimera.Creatures;
using Cosmimera.GameData;
using Cosmimera.Planet;
using Cosmimera.WorldGen;
using Unity.Mathematics;
using UnityEngine;

namespace Cosmimera.Bootstrap.Demo
{
    // REQ: docs/design/trainer-battles.md#地形試煉, planet-world.md#進階地形地城, ADR-0011 — the planet's one dungeon
    // entrance hosts its terrain trial: talk to the master → the indoor room dungeon (a flat sphere of the planet's
    // radius with the RoomDungeonPlanner walls) → guard elite unseals the arena → master + dungeon boss vs the player.
    public sealed partial class RunSession
    {
        private const float TrialMasterReachM = 3.5f;
        private const float TrialExitReachM = 3f;
        private const float TrialHintRadiusM = 7f;
        // Master stands this far in front of the wonder's footprint, on the side facing the region centre (roads).
        private const float TrialMasterGapM = 3f;
        private const float TrialReturnGapM = 6f;
        // Own streams and individual keys: the indoor spawns never shift a surface spawn's draws.
        private const ulong TrialSpawnStream = 0x54524931UL;
        private const ulong TrialIndividualKeyBase = 0x7000_0000UL;

        private sealed class TrialRun
        {
            public AdvancedTerrainSpec Spec;
            public TerrainTrialPlan Plan;
            public TrialProgress Progress;
            public RoomDungeonGeometry Geometry;
            public PlanetWalls Walls;
            public Material TerrainMaterial;
            public int Region;
            public int Tier;
            public float3 MasterDir;
            public float3 MasterFacing;
            public float3 ReturnDir;
            public bool BadgeHeld;
            public GameObject InteriorRoot;
            public PlanetTerrain InteriorTerrain;
            public GameObject MasterRoot;
            public readonly List<Object> Owned = new List<Object>();
            public bool Inside;
            public bool Spawned;
            public bool TransitPending;
            public float3 TransitDir;
            public bool HintShown;
            public bool ExitHintShown;
            public int GuardUnit = -1;
            public int TrainerId = -1;
            public int PendingTrainerId = -1;
            public readonly HashSet<int> InteriorUnits = new HashSet<int>();
            public readonly HashSet<int> FinalUnits = new HashSet<int>();
            public readonly List<int> Suspended = new List<int>();
            public readonly List<int> Parked = new List<int>();
            public readonly List<GameObject> HiddenRoots = new List<GameObject>();
            public readonly List<Behaviour> DisabledBehaviours = new List<Behaviour>();
        }

        private TrialRun _trial;

        private bool TrialInside => _trial != null && _trial.Inside;

        private void ConfigureTrial(IReadOnlyList<CreatureDef> pool, Material terrainMaterial, Shader toonShader)
        {
            if (_setup.IsBeginnerQuest || _landmarks == null || _landmarks.EntranceIndex < 0 || pool == null ||
                pool.Count == 0 || terrainMaterial == null)
            {
                return;
            }

            if (!AdvancedTerrains.TryGet(_landmarks.EntranceTerrainId, out AdvancedTerrainSpec spec))
            {
                Debug.LogError("RunSession: dungeon entrance names an unknown advanced terrain " + _landmarks.EntranceTerrainId);
                return;
            }

            if (spec.Form != AdvancedDungeonForm.Rooms)
            {
                // REQ: planet-world.md#進階地形地城 — the surface maze around the entrance wonder is not specified yet
                // (docs/open-questions.html Q-W45); its trial waits for that answer.
                Debug.Log("RunSession: surface-maze trial not wired yet for " + spec.Id);
                return;
            }

            PlanetSize size = BossDraw.SizeFor(_setup.BossStage);
            int maxTier = 1;
            for (int r = 0; r < _plan.Regions.Count; r++)
            {
                maxTier = math.max(maxTier, _plan.Regions[r].Tier);
            }

            int level = _progression.WildLevel(D0Difficulty, maxTier, SpawnRole.Boss);
            if (!TrialPlanner.TryPlan(_setup.Seed, spec.Id, size, pool, level, out TerrainTrialPlan plan))
            {
                Debug.LogWarning($"RunSession: no {spec.Immunity} holder at {AdvancedTerrains.TrialStageFor(size)} in the pool; " +
                                 $"the {spec.Id} trial is skipped this run.");
                return;
            }

            if (_copy.GenericAffixIds.Count == 0)
            {
                Debug.LogError("RunSession: no elite affix vocabulary; the room dungeon cannot be generated.");
                return;
            }

            RunLandmark gate = _landmarks.Landmarks[_landmarks.EntranceIndex];
            float radius = _rig.Config.Radius;
            RoomDungeon dungeon = RoomDungeonPlanner.Generate(_setup.Seed, spec.Id, size, _copy.GenericAffixIds);
            var trial = new TrialRun
            {
                Spec = spec,
                Plan = plan,
                Progress = new TrialProgress(plan),
                Geometry = RoomDungeonGeometry.Build(dungeon, radius, gate.Direction),
                TerrainMaterial = terrainMaterial,
                Region = gate.Region,
                Tier = maxTier,
                MasterDir = math.normalize(gate.Direction * radius + gate.Facing * (gate.FootprintRadiusM + TrialMasterGapM)),
                MasterFacing = gate.Facing,
                ReturnDir = math.normalize(gate.Direction * radius + gate.Facing * (gate.FootprintRadiusM + TrialReturnGapM)),
                BadgeHeld = HasDepartureBadge(spec.Id),
            };
            trial.Walls = trial.Geometry.BuildWalls(_rig.Config);
            _trial = trial;
            if (toonShader != null)
            {
                BuildTrialMaster(toonShader);
            }
        }

        private bool HasDepartureBadge(string terrainId)
        {
            if (_setup.TrialBadges == null)
            {
                return false;
            }

            foreach (string id in _setup.TrialBadges)
            {
                if (id == terrainId)
                {
                    return true;
                }
            }

            return false;
        }

        private void BuildTrialMaster(Shader toonShader)
        {
            NpcSpec spec = NpcMeshes.SpecForTrialMaster(_trial.Plan.MasterLookSeed);
            var root = new GameObject("TrialMaster_" + _trial.Spec.Id);
            Mesh mesh = NpcMeshes.Build(spec, root.transform, out ResidentStride stride);
            var renderer = root.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;
            renderer.bones = stride.Bones;
            renderer.rootBone = root.transform;
            Material body = PlanetLook.CreatureMaterial(toonShader, new Color(0.78f, 0.7f, 0.62f), 0.02f);
            Material glow = PlanetLook.CreatureMaterial(toonShader, new Color(1f, 0.9f, 0.6f), 0f);
            renderer.sharedMaterials = new[] { body, glow };

            // REQ: trainer-battles.md#外觀佔位 — an element-coloured halo tells the master apart from passers-by.
            GameObject halo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.Destroy(halo.GetComponent<Collider>());
            halo.transform.SetParent(root.transform, false);
            halo.transform.localPosition = new Vector3(0f, 0.02f, 0f);
            halo.transform.localScale = new Vector3(1.6f, 0.01f, 1.6f);
            Material haloMat = PlanetLook.CreatureMaterial(toonShader, PlaceholderCreatureViewFactory.ColorOf(_trial.Spec.Element), 0f);
            halo.GetComponent<Renderer>().sharedMaterial = haloMat;
            _trial.Owned.Add(mesh);
            _trial.Owned.Add(body);
            _trial.Owned.Add(glow);
            _trial.Owned.Add(haloMat);
            _trial.MasterRoot = root;
            SyncTrialMaster();
        }

        private string TrialTerrainName => _copy.Ui("ui.trial.terrain." + _trial.Spec.Id);

        private string TrialMasterName => _copy.Ui("ui.trial.master").Replace("{terrain}", TrialTerrainName);

        private string TrialLine(string key) => _copy.Ui(key)
            .Replace("{master}", TrialMasterName)
            .Replace("{terrain}", TrialTerrainName)
            .Replace("{interact}", BindingLabel(_interactAction));

        // Waits for ground colliders under the arrival point after a scene swap; the body stays kinematic meanwhile so
        // it cannot fall through the not-yet-baked floor. Returns true while the swap is still pending.
        private bool StepTrialTransit()
        {
            if (_trial == null || !_trial.TransitPending)
            {
                return false;
            }

            PlanetTerrain ground = _trial.Inside ? _trial.InteriorTerrain : _rig.Terrain;
            float3 dir = _trial.TransitDir;
            Vector3 origin = _rig.Config.ToWorld(dir, _surface.GroundAltitude(dir) + 2f);
            if (!ground.IsSettled || !Physics.Raycast(origin, -(Vector3)dir, 6f))
            {
                return true;
            }

            _rig.Character.GetComponent<Rigidbody>().isKinematic = false;
            _rig.Character.enabled = true;
            _trial.TransitPending = false;
            return false;
        }

        private void StepTrial(float3 player)
        {
            if (_trial == null || _trial.TransitPending)
            {
                return;
            }

            float3 dir = _rig.Config.ToDirection(player);
            float radius = _rig.Config.Radius;
            if (!_trial.Inside)
            {
                float toMaster = math.distance(dir, _trial.MasterDir) * radius;
                if (toMaster <= TrialHintRadiusM && !_trial.HintShown)
                {
                    _trial.HintShown = true;
                    Toast(TrialLine(_trial.Progress.Step == TrialStep.Won ? "ui.trial.done" : "ui.trial.hint"));
                }
                else if (toMaster > 2f * TrialHintRadiusM)
                {
                    _trial.HintShown = false;
                }

                return;
            }

            float toExit = math.distance(dir, _trial.Geometry.Exit) * radius;
            if (toExit <= TrialHintRadiusM && !_trial.ExitHintShown)
            {
                _trial.ExitHintShown = true;
                Toast(TrialLine("ui.trial.exit_hint"));
            }
            else if (toExit > 2f * TrialHintRadiusM)
            {
                _trial.ExitHintShown = false;
            }

            if (_trial.GuardUnit >= 0 && _encounter.Units[_trial.GuardUnit].Combatant.IsFainted &&
                _trial.Progress.OnGuardDefeated())
            {
                Toast(TrialLine("ui.trial.guard_down"));
            }

            _surface.InteriorSealed = _trial.Progress.ArenaSealed;
            if (_trial.Progress.Step == TrialStep.GuardDefeated && _trial.TrainerId < 0 && !_team.InCombat &&
                !_encounter.HasActiveTrainerBattle && _trial.Geometry.RoomAt(dir) == _trial.Geometry.Dungeon.BossRoom)
            {
                StartTrialFinal();
            }
        }

        // Called from OpenInteract before wild and trainer interactions: the master and the exit pad are fixed points.
        private bool TryTrialInteract()
        {
            if (_trial == null || _trial.TransitPending || _team.InCombat || _encounter.HasActiveTrainerBattle ||
                _bqTrainerId >= 0 || _activeEncounterTrainerId >= 0)
            {
                return false;
            }

            float3 dir = _rig.Config.ToDirection(PlayerPosition());
            float radius = _rig.Config.Radius;
            if (_trial.Inside)
            {
                if (math.distance(dir, _trial.Geometry.Exit) * radius > TrialExitReachM)
                {
                    return false;
                }

                ExitInterior();
                Toast(TrialLine("ui.trial.exit"));
                return true;
            }

            if (math.distance(dir, _trial.MasterDir) * radius > TrialMasterReachM)
            {
                return false;
            }

            if (_trial.Progress.Step == TrialStep.Won)
            {
                Toast(TrialLine("ui.trial.done"));
                return true;
            }

            if (_trial.Progress.Talk())
            {
                Toast(TrialLine("ui.trial.talk"));
            }

            EnterInterior();
            Toast(TrialLine("ui.trial.enter"));
            return true;
        }

        private void EnterInterior()
        {
            if (_trial.InteriorRoot == null)
            {
                // Not under this transform: ScatterField treats every child of the run object as one of its roots.
                _trial.InteriorRoot = new GameObject("InteriorDungeon_" + _trial.Spec.Id);
                _trial.InteriorTerrain = _trial.InteriorRoot.AddComponent<PlanetTerrain>();
                var flat = new PlanetHeight(_setup.Seed, 0f, 1f, 1);
                _trial.InteriorTerrain.Configure(_rig.Config, flat, _rig.Character.transform, _trial.TerrainMaterial,
                    default, 0f, _trial.Walls.View, _trial.Walls.HeightM);
            }

            SetSurfaceShown(false);
            _trial.InteriorRoot.SetActive(true);
            _surface.Interior = _trial.Walls;
            _surface.InteriorSealed = _trial.Progress.ArenaSealed;
            _trial.Inside = true;

            _trial.Suspended.Clear();
            foreach (EncounterUnit u in _encounter.Units)
            {
                if (u.Side == EncounterSide.Wild && !u.IsTrainerOwned && u.Fielded && !_trial.InteriorUnits.Contains(u.Id))
                {
                    _encounter.Remove(u.Id);
                    _trial.Suspended.Add(u.Id);
                }
            }

            if (!_trial.Spawned)
            {
                SpawnInterior();
            }
            else
            {
                foreach (int id in _trial.Parked)
                {
                    _encounter.Restore(id);
                }

                _trial.Parked.Clear();
            }

            MoveThroughTrialGate(_trial.Geometry.Arrival);
        }

        private void ExitInterior()
        {
            _trial.Parked.Clear();
            foreach (int id in _trial.InteriorUnits)
            {
                if (_encounter.Units[id].Fielded)
                {
                    _encounter.Remove(id);
                    _trial.Parked.Add(id);
                }
            }

            foreach (int id in _trial.Suspended)
            {
                if (!_wild[id].Gone && !_encounter.Units[id].Fielded)
                {
                    _encounter.Restore(id);
                }
            }

            _trial.Suspended.Clear();
            _surface.Interior = null;
            _trial.Inside = false;
            _trial.InteriorRoot.SetActive(false);
            SetSurfaceShown(true);
            MoveThroughTrialGate(_trial.ReturnDir);
        }

        private void SetSurfaceShown(bool shown)
        {
            if (!shown)
            {
                _trial.HiddenRoots.Clear();
                _trial.DisabledBehaviours.Clear();
                Hide(_rig.Terrain.gameObject);
                foreach (Transform child in transform)
                {
                    Hide(child.gameObject);
                }

                WaterSurface water = GetComponent<WaterSurface>();
                if (water != null)
                {
                    Hide(water.Root);
                }

                // Streamers would keep spawning surface roots (grass, scatter, far silhouettes) around the player.
                Disable(GetComponent<GrassField>());
                Disable(GetComponent<ScatterField>());
                Disable(GetComponent<FarScenery>());
                return;
            }

            foreach (GameObject go in _trial.HiddenRoots)
            {
                if (go != null)
                {
                    go.SetActive(true);
                }
            }

            foreach (Behaviour b in _trial.DisabledBehaviours)
            {
                if (b != null)
                {
                    b.enabled = true;
                }
            }

            _trial.HiddenRoots.Clear();
            _trial.DisabledBehaviours.Clear();
        }

        private void Hide(GameObject go)
        {
            if (go != null && go.activeSelf)
            {
                go.SetActive(false);
                _trial.HiddenRoots.Add(go);
            }
        }

        private void Disable(Behaviour b)
        {
            if (b != null && b.enabled)
            {
                b.enabled = false;
                _trial.DisabledBehaviours.Add(b);
            }
        }

        // Teleports the character (held kinematic until the ground under it has colliders) and brings the fielded
        // party along, so nobody is left on the other sphere.
        private void MoveThroughTrialGate(float3 dir)
        {
            if (_interactTarget >= 0)
            {
                CloseInteract();
            }

            Vector3 at = _rig.Config.ToWorld(dir, _surface.GroundAltitude(dir) + 0.3f);
            // Teleport first: it zeroes the velocity, which a kinematic body would reject with a warning.
            _rig.Character.Teleport(at);
            // The motor writes velocity every FixedUpdate, which a kinematic body rejects: pause it with the body.
            _rig.Character.enabled = false;
            _rig.Character.GetComponent<Rigidbody>().isKinematic = true;
            _trial.TransitDir = dir;
            _trial.TransitPending = true;
            float3 player = new float3(at.x, at.y, at.z);
            int slot = 0;
            foreach (int id in _partyUnits)
            {
                if (_encounter.Units[id].Fielded)
                {
                    _encounter.Unfield(id);
                    _encounter.Field(id, BesidePlayer(player, slot++), false, _nowMs);
                }
            }

            SyncTrialMaster();
        }

        private void SpawnInterior()
        {
            _trial.Spawned = true;
            var candidates = new List<CreatureDef>();
            foreach (CreatureDef c in _wildPool)
            {
                if (c.Stage <= _setup.BossStage && RunPlanner.MainElement(c) == _trial.Spec.Element)
                {
                    candidates.Add(c);
                }
            }

            // As the trainer planner does: an element missing from the pool falls back to the whole pool.
            if (candidates.Count == 0)
            {
                foreach (CreatureDef c in _wildPool)
                {
                    if (c.Stage <= _setup.BossStage)
                    {
                        candidates.Add(c);
                    }
                }
            }

            candidates.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            if (candidates.Count == 0)
            {
                return;
            }

            var rng = new Pcg32(_setup.Seed, TrialSpawnStream + (ulong)_trial.Spec.Element);
            RoomDungeon dungeon = _trial.Geometry.Dungeon;
            string habitat = _habitatAt(_trial.Geometry.Anchor);
            ulong key = TrialIndividualKeyBase;
            for (int r = 0; r < dungeon.Rooms.Count; r++)
            {
                DungeonRoomRole role = dungeon.Rooms[r].Role;
                int count = role == DungeonRoomRole.Normal ? 2 :
                    role == DungeonRoomRole.Elite || role == DungeonRoomRole.Guard || role == DungeonRoomRole.Treasure ? 1 : 0;
                SpawnRole spawnRole = role == DungeonRoomRole.Normal ? SpawnRole.Normal : SpawnRole.Elite;
                for (int k = 0; k < count; k++)
                {
                    CreatureDef def = candidates[(int)rng.NextUInt((uint)candidates.Count)];
                    float3 at = _trial.Geometry.SpawnPoint(r, k + 1);
                    int level = _progression.WildLevel(D0Difficulty, _trial.Tier, spawnRole);
                    WildIndividual ind = WildIndividual.Draw(def, habitat, WildIndividual.KeyFor(_setup.Seed, key++));
                    EncounterUnit u = _encounter.AddWild(def, null, at, level, ind);
                    _wild[u.Id] = new WildInfo { Spawn = -1, TreasureRegion = _trial.Region, Level = level };
                    _trial.InteriorUnits.Add(u.Id);
                    CreateView(u, spawnRole);
                    if (role == DungeonRoomRole.Guard)
                    {
                        _trial.GuardUnit = u.Id;
                    }
                }
            }
        }

        private void StartTrialFinal()
        {
            float3 player = PlayerPosition();
            RefillField(player);
            if (_trial.PendingTrainerId < 0)
            {
                // Built once per attempt: a refused start (below) retries with the same idle trainer on a later tick.
                TerrainTrialPlan plan = _trial.Plan;
                RoomDungeonGeometry g = _trial.Geometry;
                int boss = g.Dungeon.BossRoom;
                int tid = _encounter.RegisterTrainer();
                CreatureDef bossDef = FindForm(_wildPool, plan.BossCreatureId);
                WildIndividual bossInd = WildIndividual.Draw(bossDef, _habitatAt(g.Anchor),
                    WildIndividual.KeyFor(_setup.Seed, TrialIndividualKeyBase - 1));
                EncounterUnit bossUnit = _encounter.AddTrialBoss(bossDef, null, g.SpawnPoint(boss, 0), plan.Level, bossInd,
                    tid, plan.BossOverloads);
                _trial.FinalUnits.Add(bossUnit.Id);
                CreateView(bossUnit, SpawnRole.Boss);
                for (int m = 0; m < plan.MasterTeam.Count; m++)
                {
                    TrainerTeamMember member = plan.MasterTeam[m];
                    EncounterUnit u = _encounter.AddTrainerCreature(FindForm(_wildPool, member.CreatureId), null,
                        g.SpawnPoint(boss, m + 1), member.Level, tid, m);
                    _trial.FinalUnits.Add(u.Id);
                    CreateView(u, SpawnRole.Normal);
                }

                _trial.PendingTrainerId = tid;
            }

            // Refused while a dungeon wild is still engaged: the units wait off-field and the next tick tries again.
            if (!_encounter.StartTrainerBattle(_trial.PendingTrainerId))
            {
                return;
            }

            _trial.Progress.OnArenaReached();
            _trial.TrainerId = _trial.PendingTrainerId;
            _trial.PendingTrainerId = -1;
            if (!_team.InCombat)
            {
                BeginCombat();
            }

            RecordAppearances();
            Toast(TrialLine("ui.trial.final"));
        }

        // Settles the final battle before CheckEnd's party-wipe rule: a trial loss sends the party back to the entrance
        // instead of ending the run (trainer-battles.md#地形試煉 落敗). Returns true when it settled this tick.
        private bool CheckTrialEnd()
        {
            if (_trial == null || _trial.TrainerId < 0)
            {
                return false;
            }

            TrainerBattleState state = _encounter.TrainerState(_trial.TrainerId);
            bool poolAlive = false;
            foreach (int id in _team.Pool)
            {
                poolAlive |= !_encounter.Units[id].Combatant.IsFainted;
            }

            if (state == TrainerBattleState.Active && poolAlive)
            {
                return false;
            }

            bool won = state == TrainerBattleState.Defeated;
            TrialVictory? victory = _trial.Progress.OnFinalBattleEnded(won, _trial.BadgeHeld);
            _trial.TrainerId = -1;
            foreach (int id in _trial.FinalUnits)
            {
                if (_lockedTarget == id)
                {
                    _lockedTarget = -1;
                }

                _encounter.Remove(id);
                if (_views.TryGetValue(id, out ICreatureView view))
                {
                    view.Dispose();
                    _views.Remove(id);
                    _viewDefs.Remove(id);
                    _viewRoles.Remove(id);
                }
            }

            _trial.FinalUnits.Clear();
            RestoreTrialParty();
            if (victory.HasValue)
            {
                TrialVictory v = victory.Value;
                if (v.FirstWin)
                {
                    _outcome.CompletedTrials.Add(v.BadgeTerrainId);
                    _trial.BadgeHeld = true;
                }

                Toast(TrialLine(v.FirstWin ? "ui.trial.victory_first" : "ui.trial.victory"));
                CaptureTrialBoss();
                RefillField(PlayerPosition());
                return true;
            }

            ExitInterior();
            Toast(TrialLine("ui.trial.defeat"));
            return true;
        }

        // REQ: trainer-battles.md#地形試煉 — winning lets the player capture the dungeon boss with certainty. Done at once
        // (no vessel throw) because the trainer-owned boss never becomes a capturable wild.
        private void CaptureTrialBoss()
        {
            if (!_trial.Progress.TryCaptureBoss())
            {
                return;
            }

            TerrainTrialPlan plan = _trial.Plan;
            CreatureDef bossDef = FindForm(_wildPool, plan.BossCreatureId);
            WildIndividual ind = WildIndividual.Draw(bossDef, _habitatAt(_trial.Geometry.Anchor),
                WildIndividual.KeyFor(_setup.Seed, TrialIndividualKeyBase - 1));
            _outcome.Captured.Add((plan.BossCreatureId, _vessel, _progression.XpThreshold(plan.Level),
                ind != null ? ind.Niche.Id : null, ind != null ? ind.Habit : Habit.Tolerant));
            EncyclopediaCatalog.RecordOwned(_outcome.Codex, bossDef);
            _quest.OnCaptured();
            Toast(_copy.Ui("ui.trial.capture").Replace("{creature}", _copy.Name(plan.BossCreatureId)));
        }

        // REQ: trainer-battles.md#共用規則 — both sides fully recover after a trainer battle.
        private void RestoreTrialParty()
        {
            foreach (int id in _partyUnits)
            {
                Combatant c = _encounter.Units[id].Combatant;
                if (c.IsFainted)
                {
                    c.ReviveByPercent(100, _nowMs);
                }
                else
                {
                    c.HealByPercent(100, _nowMs);
                }
            }

            if (!_vitals.IsDowned)
            {
                _vitals.Heal(_vitals.MaxHp, _nowMs);
            }
        }

        // Indoors only the dungeon's own creatures are drawn; on the surface the parked dungeon creatures are not.
        private bool IsHiddenByTrial(EncounterUnit u)
        {
            if (_trial == null || u.Side != EncounterSide.Wild)
            {
                return false;
            }

            bool dungeon = _trial.InteriorUnits.Contains(u.Id) || _trial.FinalUnits.Contains(u.Id);
            return _trial.Inside ? !dungeon : dungeon;
        }

        private void SyncTrialMaster()
        {
            if (_trial?.MasterRoot == null)
            {
                return;
            }

            float3 dir;
            float3 facing;
            if (_trial.Inside)
            {
                // REQ: trainer-battles.md#地形試煉 — inside, the master follows the player without fighting.
                float3 player = PlayerPosition();
                float3 up = _rig.Config.ToDirection(player);
                PlanetFrame f = PlanetFrame.FromUp(up);
                float3 back = SphericalTangent(up, -(float3)_rig.Character.transform.forward, f.North);
                dir = math.normalize(up * _rig.Config.Radius + back * 2.5f + f.East * 1.2f);
                facing = -back;
            }
            else
            {
                dir = _trial.MasterDir;
                facing = _trial.MasterFacing;
            }

            float3 world = _rig.Config.ToWorld(dir, _surface.GroundAltitude(dir) - 0.03f);
            float3 tangent = SphericalTangent(dir, facing, PlanetFrame.FromUp(dir).North);
            _trial.MasterRoot.transform.SetPositionAndRotation(new Vector3(world.x, world.y, world.z),
                Quaternion.LookRotation((Vector3)tangent, (Vector3)dir));
        }

        private static float3 SphericalTangent(float3 up, float3 v, float3 fallback)
        {
            float3 t = v - up * math.dot(v, up);
            return math.lengthsq(t) > 1e-8f ? math.normalize(t) : fallback;
        }

        private void DisposeTrial()
        {
            if (_trial == null)
            {
                return;
            }

            if (_trial.InteriorRoot != null)
            {
                // Immediate so its OnDestroy completes the mesh jobs reading the wall buffers before they are freed.
                DestroyImmediate(_trial.InteriorRoot);
            }

            _trial.Walls?.Dispose();
            if (_trial.MasterRoot != null)
            {
                Destroy(_trial.MasterRoot);
            }

            foreach (Object o in _trial.Owned)
            {
                if (o != null)
                {
                    Destroy(o);
                }
            }

            _trial = null;
        }
    }
}
