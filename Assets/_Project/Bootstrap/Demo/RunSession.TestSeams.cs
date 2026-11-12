#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using Cosmimera.Combat;
using Cosmimera.Core.Contracts;
using Cosmimera.Core.Random;
using Cosmimera.WorldGen;
using Unity.Mathematics;
using UnityEngine;

namespace Cosmimera.Bootstrap.Demo
{
    public sealed partial class RunSession
    {
        // Values only: even casting a snapshot collection back to its array cannot reach run-owned objects.
        internal readonly struct UnitSnapshot
        {
            internal readonly int Id;
            internal readonly string CreatureId;
            internal readonly EncounterSide Side;
            internal readonly int Level;
            internal readonly float BodyRadiusM;
            internal readonly float3 Position;
            internal readonly bool Fielded;
            internal readonly bool IsFainted;
            internal readonly int Hp;
            internal readonly int MaxHp;

            internal UnitSnapshot(EncounterUnit unit)
            {
                Id = unit.Id;
                CreatureId = unit.Def.Id;
                Side = unit.Side;
                Level = unit.Level;
                BodyRadiusM = unit.BodyRadiusM;
                Position = unit.Position;
                Fielded = unit.Fielded;
                IsFainted = unit.Combatant.IsFainted;
                Hp = unit.Combatant.Hp;
                MaxHp = unit.Combatant.MaxHp;
            }
        }

        /// <summary>Copies one unit's observable values without its combatant or definition.</summary>
        /// <remarks>Pre: Configure ran; id indexes the encounter. Post: detached value. Side effects: none.</remarks>
        internal UnitSnapshot UnitForTests(int id)
        {
            Contract.Requires(_encounter != null && id >= 0 && id < _encounter.Units.Count, "unknown unit");
            return new UnitSnapshot(_encounter.Units[id]);
        }

        /// <summary>Copies every unit's observable values in encounter order.</summary>
        /// <remarks>Pre: Configure ran. Post: no live references. Side effects: allocates a snapshot.</remarks>
        internal IReadOnlyList<UnitSnapshot> UnitsForTests
        {
            get
            {
                var units = new UnitSnapshot[_encounter.Units.Count];
                for (int i = 0; i < units.Length; i++)
                {
                    units[i] = UnitForTests(i);
                }

                return units;
            }
        }

        /// <summary>Copies the last tick's immutable events.</summary>
        /// <remarks>Pre: Configure ran. Post: detached list. Side effects: allocates a snapshot.</remarks>
        internal IReadOnlyList<EncounterEvent> EventsForTests => new List<EncounterEvent>(_encounter.Events).ToArray();

        /// <summary>Copies the plan's immutable spawn values.</summary>
        /// <remarks>Pre: Configure ran. Post: detached list. Side effects: allocates a snapshot.</remarks>
        internal IReadOnlyList<RunSpawn> SpawnsForTests => new List<RunSpawn>(_plan.Spawns).ToArray();

        /// <summary>Observes the landing direction.</summary>
        /// <remarks>Pre: Configure ran. Post: unit direction. Side effects: none.</remarks>
        internal float3 LandingDirectionForTests => _plan.LandingDirection;

        /// <summary>Copies a quest step's immutable value.</summary>
        /// <remarks>Pre: Configure ran; index indexes Quest. Post: detached value. Side effects: none.</remarks>
        internal RunQuestStep QuestStepForTests(int index) => _plan.Quest[index];

        /// <summary>Observes a region's center without exposing its neighbors array.</summary>
        /// <remarks>Pre: Configure ran; index indexes Regions. Post: unit direction. Side effects: none.</remarks>
        internal float3 RegionCenterForTests(int index) => _plan.Regions[index].Center;

        /// <summary>Locates a direction in the generated plan.</summary>
        /// <remarks>Pre: Configure ran; direction unit. Post: region index. Side effects: none.</remarks>
        internal int RegionAtForTests(float3 direction) => RunPlanner.RegionAt(_plan, direction);

        /// <summary>Observes whether the team is in combat.</summary>
        /// <remarks>Pre: Configure ran. Post: current flag. Side effects: none.</remarks>
        internal bool InCombatForTests => _team.InCombat;

        /// <summary>Starts a fight with one wild unit, as Focus does for the nearest one (D0-E2 territory).</summary>
        /// <remarks>Pre: Configure ran; id is an active wild unit. Post: queued as Provoked; Engaged from the next Tick (Encounter.Provoke). Side effects: encounter state.</remarks>
        internal void ProvokeForTests(int id) => _encounter.Provoke(id);

        /// <summary>Runs the Focus input's action (nearest wild, provoking it if needed).</summary>
        /// <remarks>Pre: the run landed. Post: as a Focus press. Side effects: command and encounter state.</remarks>
        internal void FocusNearestForTests() => FocusNearest();

        /// <summary>Runs the CycleTarget input's action (combat.md#指定目標, D0-W4).</summary>
        /// <remarks>Pre: the run landed. Post: as a CycleTarget press. Side effects: lock, command and encounter state.</remarks>
        internal void CycleTargetForTests() => CycleTarget();

        /// <summary>Observes the locked wild unit id.</summary>
        /// <remarks>Pre: Configure ran. Post: a wild unit id, or -1 when nothing is locked. Side effects: none.</remarks>
        internal int LockedTargetForTests => _lockedTarget;

        /// <summary>Observes the locked scene object id (destructibles.md#誰能打).</summary>
        /// <remarks>Pre: Configure ran. Post: an object id, or -1; never >= 0 together with LockedTargetForTests. Side effects: none.</remarks>
        internal long LockedObjectForTests => _lockedObjectId;

        /// <summary>Observes the object the encounter's object focus points at.</summary>
        /// <remarks>Pre: Configure ran. Post: Encounter.FocusedObjectId. Side effects: none.</remarks>
        internal long FocusedObjectForTests => _encounter.FocusedObjectId;

        /// <summary>Observes the main creature's current AI target.</summary>
        /// <remarks>Pre: Configure ran. Post: a unit id, PlayerCharacterId, ObjectTargetId or NoTarget. Side effects: none.</remarks>
        internal int MainTargetForTests => _mainId >= 0 ? _encounter.Units[_mainId].TargetId : CombatAi.NoTarget;

        /// <summary>Whether a move has touched (or destroyed) object <paramref name="id"/>.</summary>
        /// <remarks>Pre: destructibles attached (RunSetup.EnvMaterials set). Post: true once registered or destroyed. Side effects: none.</remarks>
        internal bool ObjectTouchedForTests(long id) => _objectTable.IndexOf(id) >= 0 || _objectTable.IsDestroyed(id);

        /// <summary>Observes the current command's target.</summary>
        /// <remarks>Pre: Configure ran. Post: >= 0 iff CommandForTests == Focus. Side effects: none.</remarks>
        internal int CommandTargetForTests => _command.TargetId;

        /// <summary>Observes the player character's feet position.</summary>
        /// <remarks>Pre: the run landed. Post: world position. Side effects: none.</remarks>
        internal float3 PlayerPositionForTests => PlayerPosition();

        /// <summary>Observes the idle wild offered by the interaction prompt (combat.md#近身互動, D0-W3).</summary>
        /// <remarks>Pre: Configure ran. Post: a wild unit id, or -1 (none, or a menu is open). Side effects: none.</remarks>
        internal int InteractableForTests => _interactable;

        /// <summary>Observes the wild unit whose interaction menu is open.</summary>
        /// <remarks>Pre: Configure ran. Post: a wild unit id, or -1 when closed. Side effects: none.</remarks>
        internal int InteractTargetForTests => _interactTarget;

        /// <summary>Observes the text the Observe choice put in the menu.</summary>
        /// <remarks>Pre: Configure ran. Post: null until Observe was chosen in the open menu. Side effects: none.</remarks>
        internal string ObserveTextForTests => _observeText;

        /// <summary>Whether the character's movement input driver is enabled.</summary>
        /// <remarks>Pre: the run landed. Post: false exactly while a menu is open. Side effects: none.</remarks>
        internal bool CharacterInputEnabledForTests => _rig.Input.enabled;

        /// <summary>Runs the Interact input's action with no menu open.</summary>
        /// <remarks>Pre: the run landed; no menu open. Post: as an Interact press. Side effects: menu and input state.</remarks>
        internal void OpenInteractForTests()
        {
            Contract.Requires(_started && _interactTarget < 0, "landed run with no open menu required");
            OpenInteract();
        }

        /// <summary>Submits menu entry <paramref name="choice"/> (0 observe, 1 provoke, 2 pet, 3 close).</summary>
        /// <remarks>Pre: a menu is open; 0 &lt;= choice &lt;= 3. Post: as Submit on that entry. Side effects: as the choice.</remarks>
        internal void ChooseInteractForTests(int choice)
        {
            Contract.Requires(_interactTarget >= 0 && choice >= 0 && choice <= 3, "open menu and choice 0..3 required");
            ChooseInteract((InteractChoice)choice);
        }

        /// <summary>Observes the player character's HP.</summary>
        /// <remarks>Pre: Configure ran. Post: 0..max HP. Side effects: none.</remarks>
        internal int PlayerHpForTests => _vitals.Hp;

        /// <summary>Observes the player character's max HP.</summary>
        /// <remarks>Pre: Configure ran. Post: >= 1. Side effects: none.</remarks>
        internal int PlayerMaxHpForTests => _vitals.MaxHp;

        /// <summary>Lowers the player character to <paramref name="hp"/> outside any Tick, so the next real hit downs it.</summary>
        /// <remarks>
        /// Pre: Configure ran; 1 &lt;= hp &lt;= current Hp. Post: PlayerHpForTests == hp; no PlayerDowned event is raised.
        /// Side effects: player HP; advances the vitals time watermark to NowMsForTests.
        /// </remarks>
        internal void WoundPlayerForTests(int hp)
        {
            Contract.Requires(_vitals != null && hp >= 1 && hp <= _vitals.Hp, "1 <= hp <= current player HP");
            _vitals.ApplyDamage(_vitals.Hp - hp, _nowMs);
        }

        /// <summary>Copies the active team ids.</summary>
        /// <remarks>Pre: Configure ran. Post: detached list. Side effects: allocates a snapshot.</remarks>
        internal IReadOnlyList<int> ActiveUnitsForTests => new List<int>(_team.Active).ToArray();

        /// <summary>Observes the current quest step index.</summary>
        /// <remarks>Pre: Configure ran. Post: current index. Side effects: none.</remarks>
        internal int QuestIndexForTests => _quest.StepIndex;

        /// <summary>Observes progress in the current quest step.</summary>
        /// <remarks>Pre: Configure ran. Post: current count. Side effects: none.</remarks>
        internal int QuestProgressForTests => _quest.Progress;

        /// <summary>Observes whether a quest step was earned.</summary>
        /// <remarks>Pre: Configure ran; index indexes Quest. Post: current earned flag. Side effects: none.</remarks>
        internal bool QuestEarnedForTests(int index) => _quest.IsEarned(index);

        /// <summary>Observes the camera mode.</summary>
        /// <remarks>Pre: the run landed. Post: current mode. Side effects: none.</remarks>
        internal bool FirstPersonForTests => _rig.CameraRig.IsFirstPerson;

        /// <summary>Observes the render camera's position.</summary>
        /// <remarks>Pre: Configure ran. Post: copied world position. Side effects: none.</remarks>
        internal Vector3 CameraPositionForTests => _rig.Camera.transform.position;

        /// <summary>Observes whether terrain streaming has settled.</summary>
        /// <remarks>Pre: Configure ran. Post: current flag. Side effects: none.</remarks>
        internal bool TerrainSettledForTests => _rig.Terrain.IsSettled;

        /// <summary>Converts a world position to a planet direction.</summary>
        /// <remarks>Pre: Configure ran; position differs from center. Post: unit direction. Side effects: none.</remarks>
        internal float3 DirectionForTests(float3 position) => _rig.Config.ToDirection(position);

        /// <summary>Measures altitude above the planet radius.</summary>
        /// <remarks>Pre: Configure ran; position finite. Post: finite altitude. Side effects: none.</remarks>
        internal float AltitudeForTests(float3 position) => _rig.Config.Altitude(position);

        /// <summary>Samples the rendered ground including templates.</summary>
        /// <remarks>Pre: Configure ran; direction unit. Post: finite altitude. Side effects: none.</remarks>
        internal float SurfaceHeightForTests(float3 direction) => _rig.SurfaceHeight(direction);

        /// <summary>Toggles the camera using its normal view-switch operation.</summary>
        /// <remarks>Pre: the run landed. Post: camera mode toggled. Side effects: camera rig state.</remarks>
        internal void ToggleViewForTests() => _rig.CameraRig.ToggleView();

        /// <summary>Observes the render camera's right axis.</summary>
        /// <remarks>Pre: Configure ran. Post: copied unit vector. Side effects: none.</remarks>
        internal Vector3 CameraRightForTests => _rig.Camera.transform.right;

        /// <summary>Observes whether the character motor stands on ground.</summary>
        /// <remarks>Pre: the run landed. Post: current flag. Side effects: none.</remarks>
        internal bool GroundedForTests => _rig.Character.IsGrounded;

        /// <summary>The planet radius this run was built with.</summary>
        /// <remarks>Pre: Configure ran. Post: > 0. Side effects: none.</remarks>
        internal float RadiusForTests => _rig.Config.Radius;

        /// <summary>World point <paramref name="above"/> metres over the rendered ground (templates included).</summary>
        /// <remarks>Pre: Configure ran; direction unit; above finite. Post: on the ray from the center through direction. Side effects: none.</remarks>
        internal float3 SurfacePointForTests(float3 direction, float above) =>
            _rig.Config.ToWorld(direction, _rig.SurfaceHeight(direction) + above);

        /// <summary>Places the character one metre above the rendered ground, at rest.</summary>
        /// <remarks>
        /// Unlike MovePlayerForTests (bare height field), this follows template terrain, so a staged walk over landform
        /// stamps never starts inside the ground; velocity is cleared so a frame-by-frame walk does not build up fall speed.
        /// Pre: the run landed; direction unit. Post: body and transform agree; body velocity zero. Side effects: player position.
        /// </remarks>
        internal void PlaceCharacterForTests(float3 direction)
        {
            Contract.Requires(_started && math.abs(math.lengthsq(direction) - 1f) < 1e-4f, "landed run and unit direction required");
            var body = _rig.Character.GetComponent<Rigidbody>();
            Vector3 at = SurfacePointForTests(direction, 1f);
            body.linearVelocity = Vector3.zero;
            body.position = at;
            _rig.Character.transform.position = at;
        }

        /// <summary>Disables camera continuity checks before a staged cross-planet teleport.</summary>
        /// <remarks>Pre: the run landed. Post: camera rig disabled. Side effects: disables camera updates.</remarks>
        internal void SuspendCameraForTests() => _rig.CameraRig.enabled = false;

        /// <summary>Teleports the player to one meter above the height field, matching the regression fixtures.</summary>
        /// <remarks>Pre: the run landed; direction unit. Post: body and transform agree. Side effects: player position.</remarks>
        internal void MovePlayerForTests(float3 direction)
        {
            Contract.Requires(_started && math.abs(math.lengthsq(direction) - 1f) < 1e-4f, "landed run and unit direction required");
            var body = _rig.Character.GetComponent<Rigidbody>();
            Vector3 at = _rig.Config.ToWorld(direction, _rig.Height.Sample(direction) + 1f);
            body.position = at;
            _rig.Character.transform.position = at;
        }

        /// <summary>Re-fields only the currently fielded party at a staged direction.</summary>
        /// <remarks>
        /// Pre: the run landed; component disabled; direction unit; fielded party alive.
        /// Post: fielded party relocated through Encounter.Unfield/Field. Side effects: encounter positions and cast state.
        /// </remarks>
        internal void RepositionPartyForTests(float3 direction)
        {
            Contract.Requires(_started && !enabled && math.abs(math.lengthsq(direction) - 1f) < 1e-4f,
                "disabled landed run and unit direction required");
            foreach (int id in _partyUnits)
            {
                Contract.Requires(!_encounter.Units[id].Fielded || !_encounter.Units[id].Combatant.IsFainted,
                    "fielded party must be conscious before repositioning");
            }

            foreach (int id in _partyUnits)
            {
                if (_encounter.Units[id].Fielded)
                {
                    _encounter.Unfield(id);
                    _encounter.Field(id, direction, false, _nowMs);
                }
            }
        }

        /// <summary>Applies a staged hostile hit through Combatant, without generating a Tick event.</summary>
        /// <remarks>
        /// Pre: the run landed; id indexes the encounter; cast meets ReceiveHostile's contract at NowMsForTests.
        /// Post: target receives the hit; no faint settlement queued. Side effects: target combat state and supplied RNG.
        /// </remarks>
        internal void ReceiveHostileForTests(int id, in CastContext cast, ref Pcg32 rng)
        {
            Contract.Requires(_started && id >= 0 && id < _encounter.Units.Count, "landed run and known unit required");
            _encounter.Units[id].Combatant.ReceiveHostile(cast, true, ref rng, _nowMs);
        }

        /// <summary>Renders the optional smoke-test fight screenshot without exposing the live camera.</summary>
        /// <remarks>Pre: the run landed; path writable. Post: PNG written. Side effects: camera pose, GPU allocation, log and file I/O.</remarks>
        internal void CaptureFightForTests(string path)
        {
            float3 player = PlayerPosition();
            float3 look = player;
            float best = float.MaxValue;
            foreach (EncounterUnit unit in _encounter.Units)
            {
                float distance = math.distance(unit.Position, player);
                if (unit.Side == EncounterSide.Wild && unit.Fielded && distance < best)
                {
                    best = distance;
                    look = unit.Position;
                }
            }

            float3 up = _rig.Config.ToDirection(player);
            Renderer terrain = _rig.Terrain.GetComponentInChildren<Renderer>();
            Shader shader = terrain != null && terrain.sharedMaterial != null ? terrain.sharedMaterial.shader : null;
            string shaderName = shader != null ? shader.name : null;
            Debug.Log($"CAPTURE type={_setup.PlanetType.Id} lat={math.abs(up.y):0.000} h={_rig.SurfaceHeight(up):0.00} " +
                      $"relief={_rig.Height.MaxAbsHeight:0.0} sea={_rig.IsSea(up)} mat={shaderName}");
            float3 toward = math.normalize(look - player - math.dot(look - player, up) * up + up * 1e-4f);
            _rig.Camera.transform.position = player - toward * 7f + up * 4f;
            _rig.Camera.transform.rotation = Quaternion.LookRotation(look - (float3)_rig.Camera.transform.position, up);
            var rt = new RenderTexture(1280, 720, 24);
            var texture = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;
            try
            {
                _rig.Camera.targetTexture = rt;
                _rig.Camera.Render();
                RenderTexture.active = rt;
                texture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                texture.Apply();
                System.IO.File.WriteAllBytes(path, texture.EncodeToPNG());
            }
            finally
            {
                _rig.Camera.targetTexture = null;
                RenderTexture.active = previous;
                Destroy(texture);
                Destroy(rt);
            }
        }
    }
}
#endif
