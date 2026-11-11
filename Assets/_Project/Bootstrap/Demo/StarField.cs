using Cosmimera.Core.Contracts;
using Cosmimera.Core.Random;
using Cosmimera.Planet;
using Unity.Mathematics;
using UnityEngine;

namespace Cosmimera.Bootstrap.Demo
{
    /// <summary>
    /// D0 night sky (P1-08 星空): a star shell that fades in when the local sun sets and turns with the sun.
    /// REQ: docs/roadmap/phase-1.md P1-08; vision.md (遠景靠天空維持開闊感).
    /// </summary>
    /// <remarks>
    /// Pre: Configure once. Post: the star layout depends only on the seed; the shell is centred on the camera every
    /// frame (stars at infinity, no parallax) and visible only where the sun is below the local horizon.
    /// Why not a skybox: Unity skyboxes assume world +Y is up, which is false on the sphere (see AtmosphereDriver).
    /// Why rotate with the sun: the demo spins the sun about the pole instead of spinning the planet, so the stars must
    /// share that rotation or they would appear fixed to the ground while the sun moves.
    /// Side effects: creates one root GameObject ("Stars") with a mesh and a material instance; destroys them with itself.
    /// </remarks>
    public sealed class StarField : MonoBehaviour
    {
        /// <summary>Number of star quads in the shell.</summary>
        public const int StarCount = 1200;

        // Fraction of the far plane: inside it so the shell is never clipped, far enough that terrain occludes it.
        private const float ShellFraction = 0.9f;
        private const ulong Stream = 0x57A25ul;

        private Camera _camera;
        private Light _sun;
        private PlanetConfig _planet;
        private Quaternion _sunStart;
        private Transform _shell;
        private Material _material;
        private Mesh _mesh;
        private float _visibility;

        /// <summary>Current star opacity in [0, 1] (0 in daylight). For tests and debugging.</summary>
        /// <remarks>Pre: none. Post: 0 &lt;= result &lt;= 1. Side effects: none.</remarks>
        public float Visibility => _visibility;

        /// <summary>The star shell's transform (null before Configure). For tests.</summary>
        /// <remarks>Pre: none. Post: after Configure, non-null until this component is destroyed. Side effects: none.</remarks>
        public Transform Shell => _shell;

        /// <summary>Builds the shell and starts following <paramref name="camera"/>.</summary>
        /// <remarks>
        /// Pre: camera, sun, shader non-null; called once.
        /// Post: a "Stars" renderer exists with <see cref="StarCount"/> quads.
        /// Side effects: allocates a mesh and a material.
        /// </remarks>
        public void Configure(Camera camera, Light sun, in PlanetConfig planet, ulong seed, Shader shader)
        {
            Contract.Requires(camera != null && sun != null && shader != null, "camera, sun and shader required");
            Contract.Requires(_shell == null, "StarField.Configure called twice");
            _camera = camera;
            _sun = sun;
            _planet = planet;
            _sunStart = sun.transform.rotation;
            _mesh = BuildMesh(seed);
            _material = new Material(shader);
            // Not parented under this component: ScatterField treats every child of the run object as a scatter root.
            var go = new GameObject("Stars");
            go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = _material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            _shell = go.transform;
        }

        private void LateUpdate()
        {
            if (_shell == null)
            {
                return;
            }

            float3 cam = _camera.transform.position;
            _shell.SetPositionAndRotation(cam, _sun.transform.rotation * Quaternion.Inverse(_sunStart));
            _shell.localScale = Vector3.one * (_camera.farClipPlane * ShellFraction);
            if (math.distance(cam, _planet.Center) < _planet.Radius * 0.5f)
            {
                return; // same guard as AtmosphereDriver: no local up before the camera is placed
            }

            float3 up = _planet.ToDirection(cam);
            float e = math.dot(up, -(float3)_sun.transform.forward); // sin(sun elevation)
            // Stars appear through civil dusk (e from +0.1 down to -0.15), matching AtmosphereDriver's night ramp.
            _visibility = math.saturate((0.1f - e) / 0.25f);
            _material.SetFloat("_Visibility", _visibility);
        }

        private void OnEnable()
        {
            UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering += OnBeginCamera;
        }

        private void OnDisable()
        {
            UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
        }

        // FENCE: Cinemachine moves the camera later in LateUpdate than this component, so the LateUpdate placement
        //   lags one frame (~5 cm at walking speed, measured in RunSessionSmokeTests) and stars would swim. Re-pinning
        //   just before the camera renders removes the lag. · Verify: delete this, walk at night, watch stars jitter.
        //   · Ref: docs/roadmap/log/phase-1.md P1-08 handoff 2026-11-09.
        private void OnBeginCamera(UnityEngine.Rendering.ScriptableRenderContext context, Camera camera)
        {
            if (_shell != null && camera == _camera)
            {
                _shell.position = camera.transform.position;
            }
        }

        private void OnDestroy()
        {
            if (_shell != null)
            {
                Destroy(_shell.gameObject);
            }

            Destroy(_mesh);
            Destroy(_material);
        }

        private static Mesh BuildMesh(ulong seed)
        {
            var rng = new Pcg32(seed, Stream);
            var vertices = new Vector3[StarCount * 4];
            var colors = new Color[StarCount * 4];
            var uvs = new Vector2[StarCount * 4];
            var indices = new int[StarCount * 6];
            for (int s = 0; s < StarCount; s++)
            {
                // Uniform on the unit sphere (Archimedes: uniform z and longitude).
                float z = rng.NextFloat() * 2f - 1f;
                float phi = rng.NextFloat() * 2f * math.PI;
                float rxy = math.sqrt(math.max(0f, 1f - z * z));
                var dir = new float3(rxy * math.cos(phi), rxy * math.sin(phi), z);
                float3 helper = math.abs(dir.y) < 0.9f ? new float3(0f, 1f, 0f) : new float3(1f, 0f, 0f);
                float3 t1 = math.normalize(math.cross(dir, helper));
                float3 t2 = math.cross(dir, t1);

                // Cubed brightness: most stars faint, a few bright — reads as a sky instead of uniform noise.
                float u = rng.NextFloat();
                float b = 0.15f + 0.85f * u * u * u;
                float size = 0.0018f + 0.0032f * b; // angular half-size in radians on the unit shell
                float tint = rng.NextFloat();
                var c = new Color(b * (0.85f + 0.15f * tint), b * 0.92f, b * (1f - 0.15f * tint), 1f);

                int v = s * 4;
                vertices[v] = (Vector3)(dir + (-t1 - t2) * size);
                vertices[v + 1] = (Vector3)(dir + (t1 - t2) * size);
                vertices[v + 2] = (Vector3)(dir + (t1 + t2) * size);
                vertices[v + 3] = (Vector3)(dir + (-t1 + t2) * size);
                uvs[v] = new Vector2(-1f, -1f);
                uvs[v + 1] = new Vector2(1f, -1f);
                uvs[v + 2] = new Vector2(1f, 1f);
                uvs[v + 3] = new Vector2(-1f, 1f);
                colors[v] = colors[v + 1] = colors[v + 2] = colors[v + 3] = c;
                int i = s * 6;
                indices[i] = v;
                indices[i + 1] = v + 1;
                indices[i + 2] = v + 2;
                indices[i + 3] = v;
                indices[i + 4] = v + 2;
                indices[i + 5] = v + 3;
            }

            var mesh = new Mesh { name = "StarShell" };
            mesh.vertices = vertices;
            mesh.colors = colors;
            mesh.uv = uvs;
            mesh.triangles = indices;
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 2.2f);
            return mesh;
        }
    }
}
