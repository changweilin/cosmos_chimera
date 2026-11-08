using Cosmimera.Planet;
using Cosmimera.Player;
using Unity.Cinemachine;
using Unity.Mathematics;
using UnityEngine;

namespace Cosmimera.Bootstrap
{
    /// <summary>Planet terrain + walkable character + cameras + sun, shared by the P1 sandbox and the D0 run.</summary>
    /// <remarks>
    /// Pre: build through <see cref="Build"/>. Post: character held kinematic until <see cref="TrySpawn"/> succeeds.
    /// Side effects: owns the created GameObjects; <see cref="Dispose"/> destroys them.
    /// </remarks>
    public sealed class PlanetRig
    {
        private readonly GameObject[] _owned;
        private bool _spawned;

        private PlanetRig(PlanetConfig config, PlanetHeight height, float3 spawnDir, PlanetTerrain terrain,
            SphericalCharacter character, PlayerCameraRig rig, PlayerInputDriver input, Camera camera, GameObject[] owned)
        {
            Config = config;
            Height = height;
            SpawnDirection = spawnDir;
            Terrain = terrain;
            Character = character;
            CameraRig = rig;
            Input = input;
            Camera = camera;
            _owned = owned;
        }

        /// <summary>Planet geometry.</summary>
        /// <remarks>Pre: none. Post: valid. Side effects: none.</remarks>
        public PlanetConfig Config { get; }

        /// <summary>Terrain height function.</summary>
        /// <remarks>Pre: none. Post: valid. Side effects: none.</remarks>
        public PlanetHeight Height { get; }

        /// <summary>Where the character starts.</summary>
        /// <remarks>Pre: none. Post: unit length. Side effects: none.</remarks>
        public float3 SpawnDirection { get; }

        /// <summary>Streaming terrain.</summary>
        /// <remarks>Pre: none. Post: non-null until Dispose. Side effects: none.</remarks>
        public PlanetTerrain Terrain { get; }

        /// <summary>Player character.</summary>
        /// <remarks>Pre: none. Post: non-null until Dispose. Side effects: none.</remarks>
        public SphericalCharacter Character { get; }

        /// <summary>Camera rig on the character.</summary>
        /// <remarks>Pre: none. Post: non-null until Dispose. Side effects: none.</remarks>
        public PlayerCameraRig CameraRig { get; }

        /// <summary>Input driver on the character.</summary>
        /// <remarks>Pre: none. Post: non-null until Dispose. Side effects: none.</remarks>
        public PlayerInputDriver Input { get; }

        /// <summary>Main camera.</summary>
        /// <remarks>Pre: none. Post: non-null until Dispose. Side effects: none.</remarks>
        public Camera Camera { get; }

        /// <summary>Whether the character is on the ground and controllable.</summary>
        /// <remarks>Pre: none. Post: none. Side effects: none.</remarks>
        public bool IsSpawned => _spawned;

        /// <summary>Creates the rig.</summary>
        /// <remarks>
        /// Pre: config/height valid; spawnDir unit length; terrainMaterial non-null.
        /// Post: objects exist; character kinematic at the spawn point.
        /// Side effects: creates GameObjects (planet, player, cameras, sun).
        /// </remarks>
        public static PlanetRig Build(in PlanetConfig config, in PlanetHeight height, float3 spawnDir, Material terrainMaterial)
        {
            var terrain = new GameObject("Planet").AddComponent<PlanetTerrain>();

            var body = new GameObject("Player");
            var capsule = body.AddComponent<CapsuleCollider>();
            capsule.height = 1.8f;
            capsule.radius = 0.35f;
            capsule.center = new Vector3(0f, 0.9f, 0f);
            body.AddComponent<Rigidbody>().isKinematic = true; // held until the ground colliders exist
            var character = body.AddComponent<SphericalCharacter>();
            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            Object.Destroy(visual.GetComponent<Collider>());
            visual.transform.SetParent(body.transform, false);
            visual.transform.localPosition = capsule.center;
            visual.transform.localScale = new Vector3(0.7f, 0.9f, 0.7f);

            body.transform.SetPositionAndRotation(SpawnPoint(config, height, spawnDir),
                quaternion.LookRotationSafe(PlanetFrame.FromUp(spawnDir).North, spawnDir));
            terrain.Configure(config, height, body.transform, terrainMaterial);

            var pivot = new GameObject("ViewPivot").transform;
            var cameraGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var camera = cameraGo.AddComponent<Camera>();
            camera.nearClipPlane = 0.1f;
            cameraGo.AddComponent<AudioListener>();
            var brain = cameraGo.AddComponent<CinemachineBrain>();

            var third = new GameObject("CM ThirdPerson").AddComponent<CinemachineCamera>();
            var follow = third.gameObject.AddComponent<CinemachineThirdPersonFollow>();
            follow.ShoulderOffset = new Vector3(0.4f, 0.2f, 0f);
            follow.CameraDistance = 4f;
            follow.Damping = new Vector3(0.1f, 0.3f, 0.3f);
            var first = new GameObject("CM FirstPerson").AddComponent<CinemachineCamera>();
            first.gameObject.AddComponent<CinemachineHardLockToTarget>();
            first.gameObject.AddComponent<CinemachineRotateWithFollowTarget>();

            var rig = body.AddComponent<PlayerCameraRig>();
            rig.Configure(config, body.transform, pivot, third, first, brain);
            var input = body.AddComponent<PlayerInputDriver>();

            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.transform.rotation = Quaternion.LookRotation(-(Vector3)spawnDir + new Vector3(0.3f, 0f, 0.2f));

            var owned = new[] { terrain.gameObject, body, pivot.gameObject, cameraGo, third.gameObject, first.gameObject, sun.gameObject };
            return new PlanetRig(config, height, spawnDir, terrain, character, rig, input, camera, owned);
        }

        /// <summary>Releases the character once a ground collider exists under the spawn point.</summary>
        /// <remarks>
        /// Pre: not disposed. Post: returns IsSpawned; when it becomes true the character is dynamic and, if actions is
        ///      non-null, controllable.
        /// Side effects: configures character/input on success.
        /// </remarks>
        public bool TrySpawn(UnityEngine.InputSystem.InputActionAsset actions)
        {
            if (_spawned || !Terrain.IsSettled)
            {
                return _spawned;
            }

            // Ground colliders are baked asynchronously after the meshes settle; wait for one under the spawn point.
            Vector3 origin = SpawnPoint(Config, Height, SpawnDirection);
            if (!Physics.Raycast(origin, -(Vector3)SpawnDirection, 5f))
            {
                return false;
            }

            Character.GetComponent<Rigidbody>().isKinematic = false;
            Character.Configure(Config);
            if (actions != null)
            {
                Input.Configure(actions, Character, CameraRig);
            }
            else
            {
                Debug.LogError("PlanetRig: no InputActionAsset; the player cannot be controlled.");
            }

            _spawned = true;
            return true;
        }

        /// <summary>Destroys every object the rig created.</summary>
        /// <remarks>Pre: none. Post: objects destroyed (end of frame). Side effects: Object.Destroy.</remarks>
        public void Dispose()
        {
            foreach (GameObject go in _owned)
            {
                if (go != null)
                {
                    Object.Destroy(go);
                }
            }
        }

        private static Vector3 SpawnPoint(in PlanetConfig config, in PlanetHeight height, float3 dir) =>
            config.ToWorld(dir, height.Sample(dir) + 0.3f);
    }
}
