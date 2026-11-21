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
            // Surface maze (Q-W45): the trial runs on the planet surface, no scene swap; the rig owns the walls.
            public bool Maze;
            public bool WallsOwnedByRig;
            public Shader SealShader;
            public bool SealShaderIsProps;
            public GameObject SealRoot;
            public List<DungeonProp> Props;
            public GameObject[] PropRoots;
            public GameObject ArenaSeal;
            public GameObject OuterSeal;
            public int DressingMeshes;
        }

        private const float SealSinkM = 4f;
        private static readonly Color TrialSealColor = new Color(0.36f, 0.22f, 0.52f);
        private static readonly Color TrialSealGlint = new Color(0.82f, 0.62f, 1f);

        private TrialRun _trial;
        private readonly List<TrialRun> _lobbyTrials = new List<TrialRun>();
        private DungeonAtmosphere _dungeonMood;
        private RoomDungeonGeometry _mazeGeometry;
        private PlanetWalls _mazeWalls;

        // REQ: planet-world.md#進階地形地城 (Q-W45) — rainforest / volcano / ice cap / labyrinth entrances stand in the
        // plaza of a surface maze laid around them. Runs right after the landmarks so everything placed later keeps off it.
        private void PrepareSurfaceMaze(in PlanetConfig config, HeightSurface surface)
        {
            if (_setup.IsBeginnerQuest || _setup.IsLobbyPlanet || _setup.Dungeon || _landmarks == null ||
                _landmarks.EntranceIndex < 0 ||
                !AdvancedTerrains.TryGet(_landmarks.EntranceTerrainId, out AdvancedTerrainSpec spec) ||
                spec.Form != AdvancedDungeonForm.SurfaceMaze)
            {
                return;
            }

            if (_copy.GenericAffixIds.Count == 0)
            {
                Debug.LogError("RunSession: no elite affix vocabulary; the surface maze cannot be generated.");
                return;
            }

            RunLandmark gate = _landmarks.Landmarks[_landmarks.EntranceIndex];
            RoomDungeon maze = RoomDungeonPlanner.GenerateSurfaceMaze(_setup.Seed, spec.Id, BossDraw.SizeFor(_setup.BossStage),
                _copy.GenericAffixIds, gate.FootprintRadiusM);
            _mazeGeometry = RoomDungeonGeometry.Build(maze, config.Radius, gate.Direction, gate.FootprintRadiusM);
            _mazeWalls = _mazeGeometry.BuildWalls(config);
            surface.Maze = _mazeWalls;
            surface.MazeArea = _mazeGeometry;
            surface.MazeOuterSealM = maze.Style.DoorInnerM + 1.5f;
        }

        private bool TrialInside => _trial != null && _trial.Inside;

        private void ConfigureTrial(IReadOnlyList<CreatureDef> pool, Material terrainMaterial, Shader toonShader,
            Shader propsShader)
        {
            if (_setup.IsBeginnerQuest || _landmarks == null || _landmarks.Entrances.Count == 0 || pool == null ||
                pool.Count == 0)
            {
                return;
            }

            if (_setup.IsLobbyPlanet)
            {
                ConfigureLobbyTrials(pool, terrainMaterial, toonShader, propsShader);
                return;
            }

            if (!AdvancedTerrains.TryGet(_landmarks.EntranceTerrainId, out AdvancedTerrainSpec spec))
            {
                Debug.LogError("RunSession: dungeon entrance names an unknown advanced terrain " + _landmarks.EntranceTerrainId);
                return;
            }

            bool maze = spec.Form == AdvancedDungeonForm.SurfaceMaze;
            if (maze ? _mazeGeometry == null : terrainMaterial == null)
            {
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
            var trial = new TrialRun
            {
                Spec = spec,
                Plan = plan,
                Progress = new TrialProgress(plan),
                TerrainMaterial = terrainMaterial,
                Region = gate.Region,
                Tier = maxTier,
                BadgeHeld = HasDepartureBadge(spec.Id),
                Maze = maze,
                SealShader = propsShader ?? toonShader,
                SealShaderIsProps = propsShader != null,
            };
            if (maze)
            {
                // The master waits outside the maze's outer door, facing out toward the approaching player.
                RoomDungeonGeometry g = _mazeGeometry;
                trial.Geometry = g;
                trial.Walls = _mazeWalls;
                trial.WallsOwnedByRig = true;
                trial.MasterDir = g.OuterDoor;
                trial.MasterFacing = g.OuterFacing;
                trial.ReturnDir = math.normalize(g.OuterDoor * radius + g.OuterFacing * TrialMasterGapM);
            }
            else
            {
                RoomDungeon dungeon = RoomDungeonPlanner.Generate(_setup.Seed, spec.Id, size, _copy.GenericAffixIds);
                trial.Geometry = RoomDungeonGeometry.Build(dungeon, radius, gate.Direction);
                trial.Walls = trial.Geometry.BuildWalls(_rig.Config);
                float3 rawMaster = math.normalize(gate.Direction * radius + gate.Facing * (gate.FootprintRadiusM + TrialMasterGapM));
                float3 rawReturn = math.normalize(gate.Direction * radius + gate.Facing * (gate.FootprintRadiusM + TrialReturnGapM));
                trial.MasterDir = FindClearTrialGround(rawMaster, gate.Direction, gate.FootprintRadiusM + 1.5f);
                trial.MasterFacing = gate.Facing;
                trial.ReturnDir = FindClearTrialGround(rawReturn, gate.Direction, gate.FootprintRadiusM + 1.5f);
            }

            _trial = trial;
            AttachTrialProps();
            if (toonShader != null)
            {
                BuildTrialMaster(toonShader);
            }

            if (maze)
            {
                SpawnInterior();
                trial.SealRoot = new GameObject("MazeSeals_" + spec.Id);
                BuildTrialSeals(trial.SealRoot.transform);
                SyncTrialSeals();
                // The panels follow the terrain under the maze; its own wall height is left out of the floor.
                BuildDungeonDressing(trial.SealRoot.transform,
                    d => _surface.GroundAltitude(d) - _mazeWalls.View.Height(math.normalize(d)));
                _dungeonMood = gameObject.AddComponent<DungeonAtmosphere>();
                _dungeonMood.Configure(_rig.Camera, _rig.Sun, _rig.Character.transform, DungeonMood.For(spec.Id));
            }
        }

        // REQ: docs/design/lobby-meta.md#地球大廳 — the fixed lobby planet hosts all 10 advanced terrain wonders and
        // their 10 trial masters as fixed indoor-sphere dungeons.
        private void ConfigureLobbyTrials(IReadOnlyList<CreatureDef> pool, Material terrainMaterial, Shader toonShader,
            Shader propsShader)
        {
            if (terrainMaterial == null || _copy.GenericAffixIds.Count == 0)
            {
                return;
            }

            int maxTier = 1;
            for (int r = 0; r < _plan.Regions.Count; r++)
            {
                maxTier = math.max(maxTier, _plan.Regions[r].Tier);
            }

            int level = _progression.WildLevel(D0Difficulty, maxTier, SpawnRole.Boss);
            float radius = _rig.Config.Radius;
            for (int i = 0; i < _landmarks.Entrances.Count; i++)
            {
                DungeonEntranceSite entry = _landmarks.Entrances[i];
                if (!AdvancedTerrains.TryGet(entry.TerrainId, out AdvancedTerrainSpec spec))
                {
                    continue;
                }

                // Supergiant size allows all 10 advanced terrains to plan their full trial roster.
                if (!TrialPlanner.TryPlan(_setup.Seed, spec.Id, PlanetSize.Supergiant, pool, level, out TerrainTrialPlan plan))
                {
                    continue;
                }

                RunLandmark gate = _landmarks.Landmarks[entry.LandmarkIndex];
                RoomDungeon dungeon = RoomDungeonPlanner.Generate(_setup.Seed, spec.Id, PlanetSize.Supergiant, _copy.GenericAffixIds);
                float3 rawMaster = math.normalize(gate.Direction * radius + gate.Facing * (gate.FootprintRadiusM + TrialMasterGapM));
                float3 rawReturn = math.normalize(gate.Direction * radius + gate.Facing * (gate.FootprintRadiusM + TrialReturnGapM));
                var trial = new TrialRun
                {
                    Spec = spec,
                    Plan = plan,
                    Progress = new TrialProgress(plan),
                    TerrainMaterial = terrainMaterial,
                    Region = gate.Region,
                    Tier = maxTier,
                    BadgeHeld = HasDepartureBadge(spec.Id),
                    Maze = false,
                    SealShader = propsShader ?? toonShader,
                    SealShaderIsProps = propsShader != null,
                    Geometry = RoomDungeonGeometry.Build(dungeon, radius, gate.Direction),
                    MasterDir = FindClearTrialGround(rawMaster, gate.Direction, gate.FootprintRadiusM + 1.5f),
                    MasterFacing = gate.Facing,
                    ReturnDir = FindClearTrialGround(rawReturn, gate.Direction, gate.FootprintRadiusM + 1.5f),
                };
                trial.Walls = trial.Geometry.BuildWalls(_rig.Config);
                _trial = trial;
                AttachTrialProps();
                if (toonShader != null)
                {
                    BuildTrialMaster(toonShader);
                }

                _lobbyTrials.Add(trial);
            }

            _trial = _lobbyTrials.Count > 0 ? _lobbyTrials[0] : null;
        }

        // Physical seals the character bumps into; the rule layer blocks creatures with the same radii (HeightSurface).
        private void BuildTrialSeals(Transform parent)
        {
            if (_trial.SealShader == null)
            {
                return;
            }

            Material mat = _trial.SealShaderIsProps
                ? PlanetLook.SceneryMaterial(_trial.SealShader, TrialSealColor, TrialSealGlint, 0.9f, 0f, 0f, 0.8f)
                : PlanetLook.CreatureMaterial(_trial.SealShader, TrialSealColor, 0.05f);
            _trial.Owned.Add(mat);
            RoomDungeonGeometry g = _trial.Geometry;
            _trial.ArenaSeal = BuildSeal("ArenaSeal", g.ArenaGate, RoomDungeonGeometry.ArenaGateOuterM, mat, parent);
            if (_trial.Maze)
            {
                _trial.OuterSeal = BuildSeal("OuterSeal", g.OuterGate, _surface.MazeOuterSealM, mat, parent);
            }
        }

        private GameObject BuildSeal(string name, float3 dir, float radiusM, Material mat, Transform parent)
        {
            // As DungeonView: a flat cylinder cannot carry a capsule, so a convex mesh collider stands in; only the
            // spires are drawn.
            GameObject seal = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            seal.name = name;
            Object.Destroy(seal.GetComponent<Collider>());
            seal.AddComponent<MeshCollider>().convex = true;
            Renderer plug = seal.GetComponent<Renderer>();
            plug.enabled = false;
            Object.Destroy(plug);
            seal.transform.SetParent(parent, false);
            float height = RoomDungeonGeometry.WallHeightM + SealSinkM;
            float mid = _surface.GroundAltitude(dir) + (RoomDungeonGeometry.WallHeightM - SealSinkM) * 0.5f;
            float3 at = _rig.Config.ToWorld(dir, mid);
            seal.transform.SetPositionAndRotation(new Vector3(at.x, at.y, at.z),
                Quaternion.LookRotation((Vector3)PlanetFrame.FromUp(dir).North, (Vector3)dir));
            float diameter = radiusM * 2f;
            seal.transform.localScale = new Vector3(diameter, height * 0.5f, diameter);
            Mesh spiresMesh = PropMeshes.Build(PropShape.Seal, 0);
            _trial.Owned.Add(spiresMesh);
            var spires = new GameObject("Spires");
            spires.AddComponent<MeshFilter>().sharedMesh = spiresMesh;
            spires.AddComponent<MeshRenderer>().sharedMaterial = mat;
            spires.transform.SetParent(seal.transform, false);
            // The seal mesh fills radius 1, y 0..1; the cylinder's own space is radius 0.5, y -1..1.
            spires.transform.localPosition = new Vector3(0f, -1f, 0f);
            spires.transform.localScale = new Vector3(0.5f, 2f, 0.5f);
            return seal;
        }

        private void SyncTrialSeals()
        {
            bool arena = _trial.Progress.ArenaSealed;
            if (_trial.Maze)
            {
                _surface.MazeSealed = arena;
                // REQ: planet-world.md#進階地形地城 — the outer door stays sealed until the master leads the player in.
                _surface.MazeOuterSealed = _trial.Progress.Step == TrialStep.NotStarted;
            }
            else
            {
                _surface.InteriorSealed = arena;
            }

            if (_trial.ArenaSeal != null && _trial.ArenaSeal.activeSelf != arena)
            {
                _trial.ArenaSeal.SetActive(arena);
            }

            if (_trial.OuterSeal != null && _trial.OuterSeal.activeSelf != _surface.MazeOuterSealed)
            {
                _trial.OuterSeal.SetActive(_surface.MazeOuterSealed);
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
            if (_lobbyTrials.Count > 0 && (_trial == null || (!_trial.Inside && !_trial.TransitPending)))
            {
                float3 pDir = _rig.Config.ToDirection(player);
                TrialRun nearest = _lobbyTrials[0];
                float bestDot = math.dot(pDir, nearest.MasterDir);
                for (int i = 1; i < _lobbyTrials.Count; i++)
                {
                    float d = math.dot(pDir, _lobbyTrials[i].MasterDir);
                    if (d > bestDot)
                    {
                        bestDot = d;
                        nearest = _lobbyTrials[i];
                    }
                }

                _trial = nearest;
            }

            if (_trial == null || _trial.TransitPending)
            {
                return;
            }

            float3 dir = _rig.Config.ToDirection(player);
            float radius = _rig.Config.Radius;
            if (_trial.Maze)
            {
                _dungeonMood?.SetInside(_trial.Geometry.Contains(dir));
            }

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

                if (!_trial.Maze)
                {
                    return;
                }
            }
            else
            {
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
            }

            if (_trial.GuardUnit >= 0 && _encounter.Units[_trial.GuardUnit].Combatant.IsFainted &&
                _trial.Progress.OnGuardDefeated())
            {
                Toast(TrialLine("ui.trial.guard_down"));
            }

            SyncTrialSeals();
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
                HasActiveBeginnerTrainerBattle || _activeEncounterTrainerId >= 0)
            {
                return false;
            }

            float3 dir = _rig.Config.ToDirection(PlayerPosition());
            float radius = _rig.Config.Radius;
            if (_trial.Maze)
            {
                if (math.distance(dir, _trial.MasterDir) * radius > TrialMasterReachM)
                {
                    return false;
                }

                if (_trial.Progress.Step == TrialStep.Won)
                {
                    Toast(TrialLine("ui.trial.done"));
                }
                else
                {
                    _trial.Progress.Talk();
                    SyncTrialSeals();
                    Toast(TrialLine("ui.trial.talk"));
                }

                return true;
            }

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
            if (_worldObjects != null)
            {
                _worldObjects.InsideDungeon = true;
            }

            if (_objectPhysics != null)
            {
                _objectPhysics.SpawnParent = _trial.InteriorRoot.transform;
            }

            _surface.Interior = _trial.Walls;
            _trial.Inside = true;
            EnterDungeonMood();
            if (_trial.ArenaSeal == null)
            {
                BuildTrialSeals(_trial.InteriorRoot.transform);
            }

            SyncTrialSeals();

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

        // REQ: combat-feel.md#地城氛圍, planet-world.md#進階地形地城 牆與頂的美術 — built on the first entry, under the
        // interior root so walls, ceiling and lamps hide with it on the way out. The interior floor is flat (altitude 0).
        private void EnterDungeonMood()
        {
            if (_dungeonMood == null)
            {
                BuildDungeonDressing(_trial.InteriorRoot.transform, _ => 0f);
                _dungeonMood = gameObject.AddComponent<DungeonAtmosphere>();
                _dungeonMood.Configure(_rig.Camera, _rig.Sun, _rig.Character.transform, DungeonMood.For(_trial.Spec.Id));
                Mesh lamp = null;
                float lightHeight = 3f;
                Material[] mats = null;
                if (_trial.SealShader != null)
                {
                    lamp = DungeonDressing.BuildLamp(_trial.Spec.Id, out lightHeight);
                    _trial.Owned.Add(lamp);
                    mats = DressingMaterials(DungeonDressing.PaletteFor(_trial.Spec.Id, TrialCulture()));
                }

                _dungeonMood.BuildRoomLights(_trial.Geometry, _surface.GroundAltitude, _rig.Config, lamp, mats, lightHeight,
                    _trial.InteriorRoot.transform);
            }

            _dungeonMood.SetInside(true);
        }

        private void BuildDungeonDressing(Transform parent, System.Func<float3, float> floor)
        {
            if (_trial.SealShader == null)
            {
                return;
            }

            string id = _trial.Spec.Id;
            DungeonCulture culture = TrialCulture();
            Material[] mats = DressingMaterials(DungeonDressing.PaletteFor(id, culture));
            float3 anchor = _trial.Geometry.Anchor;
            float3 origin = _rig.Config.ToWorld(anchor, floor(anchor));
            foreach (Mesh mesh in DungeonDressing.BuildWalls(_trial.Geometry, id, culture, _rig.Config, floor, origin))
            {
                _trial.Owned.Add(mesh);
                var go = new GameObject("DungeonWalls");
                go.transform.SetParent(parent, false);
                go.transform.SetPositionAndRotation((Vector3)origin, Quaternion.identity);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterials = mats;
                _trial.DressingMeshes++;
            }

            BuildTrialPropViews(parent, floor, mats);
        }

        // REQ: planet-world.md#地城文化 (牆腳物件可破壞) — the wall-foot props are rule-layer destructibles from the start
        // (ids fixed by the plan), drawn when the dressing is built. Interior props stand on the flat floor (altitude 0);
        // surface-maze props on the terrain, clear of the wall ridge.
        private void AttachTrialProps()
        {
            _trial.Props = DungeonDressing.PlanProps(_trial.Geometry, _trial.Spec.Id, TrialCulture());
            _trial.PropRoots = new GameObject[_trial.Props.Count];
            if (_worldObjects == null)
            {
                return;
            }

            bool interior = !_trial.Maze;
            var bodies = new List<WorldObjects.PropBody>(_trial.Props.Count);
            foreach (DungeonProp p in _trial.Props)
            {
                float3 dir = _trial.Geometry.LocalToDirection(p.Local);
                (float radius, float height, int volume, string substance) = DungeonProp.Body(p.Kind);
                float cube = p.Scale * p.Scale * p.Scale;
                bodies.Add(new WorldObjects.PropBody(dir, interior ? 0f : _surface.GroundAltitude(dir), radius * p.Scale,
                    height * p.Scale, substance, math.max(1, (int)math.round(volume * cube)), interior, DungeonProp.NameKey(p.Kind)));
            }

            _worldObjects.AttachDungeonProps(bodies);
        }

        private void BuildTrialPropViews(Transform parent, System.Func<float3, float> floor, Material[] mats)
        {
            var meshes = new Dictionary<DungeonPropKind, Mesh>();
            RoomDungeonGeometry g = _trial.Geometry;
            for (int i = 0; i < _trial.Props.Count; i++)
            {
                if (_worldObjects != null && _worldObjects.IsPropDestroyed(i))
                {
                    continue;
                }

                DungeonProp p = _trial.Props[i];
                if (!meshes.TryGetValue(p.Kind, out Mesh mesh))
                {
                    mesh = DungeonDressing.BuildProp(p.Kind);
                    meshes[p.Kind] = mesh;
                    _trial.Owned.Add(mesh);
                }

                float3 up = g.LocalToDirection(p.Local);
                float3 ahead = g.LocalToDirection(p.Local + p.Facing) - up;
                float3 facing = math.normalizesafe(ahead - up * math.dot(ahead, up), PlanetFrame.FromUp(up).North);
                Quaternion turn = Quaternion.AngleAxis(math.degrees(p.Yaw), (Vector3)up);
                var go = new GameObject("DungeonProp_" + p.Kind);
                go.transform.SetParent(parent, false);
                go.transform.SetPositionAndRotation((Vector3)_rig.Config.ToWorld(up, floor(up)),
                    turn * Quaternion.LookRotation((Vector3)facing, (Vector3)up));
                go.transform.localScale = Vector3.one * p.Scale;
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterials = mats;
                // The character is stopped by this box; creatures by the same body in WorldObjects.IsBlockedByDungeonProps.
                // Sized from the body, not mesh.bounds (SceneryMeshes pads those for wind sway).
                (float radius, float height, int _, string _) = DungeonProp.Body(p.Kind);
                var box = go.AddComponent<BoxCollider>();
                box.center = new Vector3(0f, 0.5f * height, 0f);
                box.size = new Vector3(1.6f * radius, height, 1.6f * radius);
                _trial.PropRoots[i] = go;
            }
        }

        private GameObject TrialPropRoot(long id)
        {
            long i = id - WorldObjects.DungeonPropIdBase;
            return _trial?.PropRoots != null && i >= 0 && i < _trial.PropRoots.Length ? _trial.PropRoots[(int)i] : null;
        }

        // REQ: planet-world.md#進階地形地城 (地城文化) — drawn from the planet's building culture, so every client agrees.
        private DungeonCulture TrialCulture() =>
            DungeonCulture.For(ArchitectureMeshes.PlanetStyleFor(_setup.PlanetType.Id, _setup.Seed), _trial.Spec.Id, _setup.Seed);

        // Body, trim and glow, in DungeonDressing sub-mesh order. The toon fallback (no props shader) cannot self-glow.
        private Material[] DressingMaterials(in DungeonDressing.Palette pal)
        {
            Shader shader = _trial.SealShader;
            bool props = _trial.SealShaderIsProps;
            var mats = new Material[DungeonDressing.SubCount];
            mats[DungeonDressing.BodySub] = props ? PlanetLook.SceneryMaterial(shader, pal.Body, Color.white, 0f, 0f, 0f, 0f)
                : PlanetLook.CreatureMaterial(shader, pal.Body, 0f);
            mats[DungeonDressing.TrimSub] = props ? PlanetLook.SceneryMaterial(shader, pal.Trim, Color.white, 0f, 0f, 0f, 0f)
                : PlanetLook.CreatureMaterial(shader, pal.Trim, 0f);
            mats[DungeonDressing.GlowSub] = props
                ? PlanetLook.SceneryMaterial(shader, pal.Glow, Color.white, 0f, 0f, 0f, pal.GlowStrength)
                : PlanetLook.CreatureMaterial(shader, pal.Glow, 0f);
            foreach (Material m in mats)
            {
                _trial.Owned.Add(m);
            }

            return mats;
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
            if (_worldObjects != null)
            {
                _worldObjects.InsideDungeon = false;
            }

            if (_objectPhysics != null)
            {
                _objectPhysics.SpawnParent = null;
            }

            _dungeonMood?.SetInside(false);
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
                int first = _trial.Maze ? 1 : 0;
                EncounterUnit bossUnit = _encounter.AddTrialBoss(bossDef, null, g.SpawnPoint(boss, first), plan.Level,
                    bossInd, tid, plan.BossOverloads);
                _trial.FinalUnits.Add(bossUnit.Id);
                CreateView(bossUnit, SpawnRole.Boss);
                for (int m = 0; m < plan.MasterTeam.Count; m++)
                {
                    TrainerTeamMember member = plan.MasterTeam[m];
                    EncounterUnit u = _encounter.AddTrainerCreature(FindForm(_wildPool, member.CreatureId), null,
                        g.SpawnPoint(boss, first + m + 1), member.Level, tid, m);
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

            if (_trial.Maze)
            {
                MoveThroughTrialGate(_trial.ReturnDir);
            }
            else
            {
                ExitInterior();
            }

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
            if (_trial == null || _trial.Maze || u.Side != EncounterSide.Wild)
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
            bool leading = _trial.Inside || (_trial.Maze && _trial.Progress.WaivesImmunity &&
                                             _trial.Geometry.Contains(_rig.Config.ToDirection(PlayerPosition()), 0f));
            if (leading)
            {
                // REQ: trainer-battles.md#地形試煉 — inside, the master follows the player without fighting.
                float3 player = PlayerPosition();
                float3 up = _rig.Config.ToDirection(player);
                PlanetFrame f = PlanetFrame.FromUp(up);
                float3 back = SphericalTangent(up, -(float3)_rig.Character.transform.forward, f.North);
                float clearanceM = PopulacePlanner.BodyRadiusM + 0.2f;
                dir = math.normalize(up * _rig.Config.Radius + back * 2.5f + f.East * 1.2f);
                if (_surface != null && (_surface.IsBlocked(dir, clearanceM) || _surface.IsSea(dir)))
                {
                    for (int a = 0; a < 8; a++)
                    {
                        float ang = a * (math.PI * 0.25f);
                        float3 side = back * math.cos(ang) + f.East * math.sin(ang);
                        float3 cand = math.normalize(up * _rig.Config.Radius + side * 2.5f);
                        if (!_surface.IsBlocked(cand, clearanceM) && !_surface.IsSea(cand))
                        {
                            dir = cand;
                            break;
                        }
                    }
                }

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

        private float3 FindClearTrialGround(float3 preferredDir, float3 gateDir, float minRadiusM)
        {
            if (_surface == null)
            {
                return preferredDir;
            }

            float clearanceM = PopulacePlanner.BodyRadiusM + 0.25f;
            float radius = _rig.Config.Radius;
            bool IsClearDry(float3 d) =>
                !_surface.IsSea(d) && _surface.GroundAltitude(d) >= 0.2f && !_surface.IsBlocked(d, clearanceM) &&
                math.distance(d, gateDir) * radius >= minRadiusM;

            if (IsClearDry(preferredDir))
            {
                return preferredDir;
            }

            PlanetFrame frame = PlanetFrame.FromUp(preferredDir);
            for (float distM = 2f; distM <= 18f; distM += 2f)
            {
                int samples = 12;
                for (int i = 0; i < samples; i++)
                {
                    float a = i * (2f * math.PI / samples);
                    float3 cand = math.normalize(preferredDir * radius +
                        (frame.North * math.cos(a) + frame.East * math.sin(a)) * distM);
                    if (IsClearDry(cand))
                    {
                        return cand;
                    }
                }
            }

            return preferredDir;
        }

        private static float3 SphericalTangent(float3 up, float3 v, float3 fallback)
        {
            float3 t = v - up * math.dot(v, up);
            return math.lengthsq(t) > 1e-8f ? math.normalize(t) : fallback;
        }

        private void DisposeTrial()
        {
            if (_lobbyTrials.Count > 0)
            {
                for (int i = 0; i < _lobbyTrials.Count; i++)
                {
                    DisposeOneTrial(_lobbyTrials[i]);
                }

                _lobbyTrials.Clear();
                _trial = null;
                return;
            }

            if (_trial != null)
            {
                DisposeOneTrial(_trial);
                _trial = null;
            }
        }

        private static void DisposeOneTrial(TrialRun trial)
        {
            if (trial.InteriorRoot != null)
            {
                // Immediate so its OnDestroy completes the mesh jobs reading the wall buffers before they are freed.
                DestroyImmediate(trial.InteriorRoot);
            }

            if (!trial.WallsOwnedByRig)
            {
                trial.Walls?.Dispose();
            }

            if (trial.SealRoot != null)
            {
                Destroy(trial.SealRoot);
            }
            if (trial.MasterRoot != null)
            {
                Destroy(trial.MasterRoot);
            }

            foreach (Object o in trial.Owned)
            {
                if (o != null)
                {
                    Destroy(o);
                }
            }
        }
    }
}
