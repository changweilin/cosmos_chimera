using Cosmimera.Core.Contracts;
using Cosmimera.Planet;
using Unity.Mathematics;
using UnityEngine;

namespace Cosmimera.Bootstrap.Demo
{
    /// <summary>
    /// The planet's liquid surface (P3-04): a polar disc mesh that Cosmimera/ToonWater wraps onto the sphere under
    /// the camera at altitude <see cref="PlanetLook.SeaLevelM"/>. Presentation only; the sea rule is PlanetTemplates.
    /// </summary>
    /// <remarks>
    /// Pre: Configure once. Post: one "Water" renderer exists until this component is destroyed.
    /// Why the shader places the vertices: see ToonWater.shader (chord sag on a whole-planet mesh).
    /// Side effects: creates one root GameObject with a mesh; destroys both (and the material it was given) with itself.
    /// </remarks>
    public sealed class WaterSurface : MonoBehaviour
    {
        private const int Rings = 72;
        private const int Sectors = 96;
        // Ring spacing grows e⁵ ≈ 150× from the centre to the rim: ~3 m rings underfoot, coarse ones in the fog.
        private const float RingGrowth = 5f;

        private GameObject _root;
        private Mesh _mesh;
        private Material _material;

        /// <summary>The water renderer's object (null before Configure). For tests.</summary>
        /// <remarks>Pre: none. Post: after Configure, non-null until this component is destroyed. Side effects: none.</remarks>
        public GameObject Root => _root;

        /// <summary>Builds the surface out to <paramref name="reachM"/> along the ground from the camera.</summary>
        /// <remarks>
        /// Pre: material (Cosmimera/ToonWater, from PlanetLook.WaterMaterial) non-null; reachM > 0; called once.
        /// Post: the renderer is never frustum-culled (its real position is decided in the vertex shader); this
        ///       component owns material. Side effects: allocates a mesh and a GameObject.
        /// </remarks>
        public void Configure(in PlanetConfig planet, Material material, float reachM)
        {
            Contract.Requires(material != null && reachM > 0f, "material required; reach > 0");
            Contract.Requires(_root == null, "WaterSurface.Configure called twice");
            _material = material;
            // Past a quarter of the way round, the disc would wrap behind the planet for nothing.
            _mesh = BuildMesh(math.min(reachM, planet.Radius * math.PI * 0.5f), planet);
            // Not parented under this component: ScatterField treats every child of the run object as a scatter root.
            _root = new GameObject("Water");
            _root.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var r = _root.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        private void OnDestroy()
        {
            if (_root != null)
            {
                Destroy(_root);
            }

            Destroy(_mesh);
            Destroy(_material);
        }

        private static Mesh BuildMesh(float reachM, in PlanetConfig planet)
        {
            var vertices = new Vector3[1 + Rings * Sectors];
            var uvs = new Vector2[vertices.Length];
            var indices = new int[(Sectors + (Rings - 1) * Sectors * 2) * 3];
            uvs[0] = Vector2.zero;
            float norm = math.exp(RingGrowth) - 1f;
            for (int ring = 0; ring < Rings; ring++)
            {
                float distance = reachM * (math.exp(RingGrowth * (ring + 1) / Rings) - 1f) / norm;
                for (int s = 0; s < Sectors; s++)
                {
                    uvs[1 + ring * Sectors + s] = new Vector2(distance, 2f * math.PI * s / Sectors);
                }
            }

            int t = 0;
            for (int s = 0; s < Sectors; s++)
            {
                indices[t++] = 0;
                indices[t++] = 1 + s;
                indices[t++] = 1 + (s + 1) % Sectors;
            }

            for (int ring = 0; ring < Rings - 1; ring++)
            {
                int inner = 1 + ring * Sectors;
                int outer = inner + Sectors;
                for (int s = 0; s < Sectors; s++)
                {
                    int next = (s + 1) % Sectors;
                    indices[t++] = inner + s;
                    indices[t++] = outer + s;
                    indices[t++] = outer + next;
                    indices[t++] = inner + s;
                    indices[t++] = outer + next;
                    indices[t++] = inner + next;
                }
            }

            var mesh = new Mesh { name = "WaterDisc" };
            mesh.vertices = vertices; // all zero: the shader places them from uv (distance, bearing)
            mesh.uv = uvs;
            mesh.triangles = indices;
            float extent = 4f * (planet.Radius + math.cmax(math.abs(planet.Center)));
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * extent);
            return mesh;
        }
    }
}
