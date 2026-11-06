using Cosmimera.Planet;
using Cosmimera.Player;
using Unity.Cinemachine;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Cosmimera.Bootstrap
{
    /// <summary>P1 composition root: one procedural planet, a walkable character and its cameras.</summary>
    /// <remarks>
    /// Pre: placed alone in an otherwise empty scene; _actions references InputSystem_Actions.
    /// Post: once the terrain under the spawn point has colliders, the character is controllable.
    /// Side effects: creates the planet, character, cameras and sun at Awake; owns them for the scene's lifetime.
    /// Placeholder for P1 acceptance (P1-05 walking, later P1-07/P1-08 visuals) until the lobby → planet flow exists.
    /// </remarks>
    public sealed class PlanetSandbox : MonoBehaviour
    {
        [SerializeField] private float _radius = 640f;
        [SerializeField] private int _seed = 42;
        [SerializeField] private InputActionAsset _actions;
        [SerializeField] private Material _terrainMaterial;

        private PlanetTerrain _terrain;
        private SphericalCharacter _character;
        private PlayerCameraRig _rig;
        private PlayerInputDriver _input;
        private PlanetConfig _config;
        private PlanetHeight _height;
        private float3 _spawnDir;
        private bool _spawned;

        private void Awake()
        {
            _config = new PlanetConfig(_radius, float3.zero);
            _height = PlanetHeight.ForPlanet(_config, (ulong)(uint)_seed);
            _spawnDir = math.normalize(new float3(0.2f, 1f, 0.1f));

            _terrain = new GameObject("Planet").AddComponent<PlanetTerrain>();

            var body = new GameObject("Player");
            var capsule = body.AddComponent<CapsuleCollider>();
            capsule.height = 1.8f;
            capsule.radius = 0.35f;
            capsule.center = new Vector3(0f, 0.9f, 0f);
            body.AddComponent<Rigidbody>().isKinematic = true; // held until the ground colliders exist
            _character = body.AddComponent<SphericalCharacter>();
            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            Destroy(visual.GetComponent<Collider>());
            visual.transform.SetParent(body.transform, false);
            visual.transform.localPosition = capsule.center;
            visual.transform.localScale = new Vector3(0.7f, 0.9f, 0.7f);

            float3 up = _spawnDir;
            body.transform.SetPositionAndRotation(SpawnPoint(), quaternion.LookRotationSafe(PlanetFrame.FromUp(up).North, up));
            _terrain.Configure(_config, _height, body.transform, _terrainMaterial);

            var pivot = new GameObject("ViewPivot").transform;
            var cameraGo = new GameObject("Main Camera") { tag = "MainCamera" };
            cameraGo.AddComponent<Camera>().nearClipPlane = 0.1f;
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

            _rig = body.AddComponent<PlayerCameraRig>();
            _rig.Configure(_config, body.transform, pivot, third, first, brain);
            _input = body.AddComponent<PlayerInputDriver>();

            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.transform.rotation = Quaternion.LookRotation(-(Vector3)_spawnDir + new Vector3(0.3f, 0f, 0.2f));
        }

        private Vector3 SpawnPoint() => _config.ToWorld(_spawnDir, _height.Sample(_spawnDir) + 0.3f);

        private void Update()
        {
            if (_spawned || !_terrain.IsSettled)
            {
                return;
            }

            // Ground colliders are baked asynchronously after the meshes settle; wait for one under the spawn point.
            Vector3 origin = SpawnPoint();
            if (!Physics.Raycast(origin, -(Vector3)_spawnDir, 5f))
            {
                return;
            }

            _character.GetComponent<Rigidbody>().isKinematic = false;
            _character.Configure(_config);
            if (_actions != null)
            {
                _input.Configure(_actions, _character, _rig);
            }
            else
            {
                Debug.LogError("PlanetSandbox: _actions is not assigned; the player cannot be controlled.", this);
            }

            _spawned = true;
        }

#if UNITY_EDITOR
        // Lives here rather than in a Bootstrap.Editor asmdef: one menu item does not justify a new node in the
        // architecture graph (ArchitectureTests.AllowedReferences).
        [UnityEditor.MenuItem("Cosmimera/Create Planet Sandbox Scene")]
        public static void CreateScene()
        {
            const string scenePath = "Assets/_Project/Bootstrap/PlanetSandbox.unity";
            const string materialPath = "Assets/_Project/Bootstrap/SandboxTerrain.mat";
            var material = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(0.45f, 0.6f, 0.35f) };
                UnityEditor.AssetDatabase.CreateAsset(material, materialPath);
            }

            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
            var sandbox = new GameObject("PlanetSandbox").AddComponent<PlanetSandbox>();
            sandbox._actions = UnityEditor.AssetDatabase.LoadAssetAtPath<InputActionAsset>(
                "Assets/_Project/Settings/Input/InputSystem_Actions.inputactions");
            sandbox._terrainMaterial = material;
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene, scenePath);
            UnityEditor.AssetDatabase.SaveAssets();
        }
#endif
    }
}
