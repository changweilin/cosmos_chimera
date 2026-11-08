using Cosmimera.Planet;
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

        private PlanetRig _rig;

        private void Awake()
        {
            var config = new PlanetConfig(_radius, float3.zero);
            _rig = PlanetRig.Build(config, PlanetHeight.ForPlanet(config, (ulong)(uint)_seed),
                math.normalize(new float3(0.2f, 1f, 0.1f)), _terrainMaterial);
        }

        private void Update()
        {
            _rig.TrySpawn(_actions);
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
