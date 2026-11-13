using Cosmimera.Core.Contracts;
using Cosmimera.Planet;
using Unity.Mathematics;
using UnityEngine;

namespace Cosmimera.Bootstrap.Demo
{
    /// <summary>
    /// P1-08 (D0): the daytime sky — gradient, sun and clouds drawn by Cosmimera/ToonSky behind everything else.
    /// REQ: vision.md (天空：日夜循環；遠景靠大氣與天空). Colours come from AtmosphereDriver's shader globals.
    /// </summary>
    /// <remarks>
    /// Pre: Configure once; an AtmosphereDriver is running (without it the sky renders black: the globals are zero).
    /// Post: one "Sky" renderer exists until this component is destroyed; the shader keeps it centred on whichever
    /// camera renders it, so it needs no per-frame update.
    /// Side effects: creates one root GameObject with a mesh and a material instance; destroys them with itself.
    /// </remarks>
    public sealed class SkyDome : MonoBehaviour
    {
        private GameObject _root;
        private Mesh _mesh;
        private Material _material;

        /// <summary>The sky renderer's object (null before Configure). For tests.</summary>
        /// <remarks>Pre: none. Post: after Configure, non-null until this component is destroyed. Side effects: none.</remarks>
        public GameObject Root => _root;

        /// <summary>Builds the dome.</summary>
        /// <remarks>
        /// Pre: shader (Cosmimera/ToonSky) non-null; called once.
        /// Post: the renderer is never frustum-culled anywhere around <paramref name="planet"/>.
        /// Side effects: allocates a mesh, a material and a GameObject.
        /// </remarks>
        public void Configure(in PlanetConfig planet, Shader shader)
        {
            Contract.Requires(shader != null, "shader required");
            Contract.Requires(_root == null, "SkyDome.Configure called twice");
            _material = new Material(shader);
            _mesh = BuildBox(4f * (planet.Radius + math.cmax(math.abs(planet.Center))));
            // Not parented under this component: ScatterField treats every child of the run object as a scatter root.
            _root = new GameObject("Sky");
            _root.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var r = _root.AddComponent<MeshRenderer>();
            r.sharedMaterial = _material;
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

        // A unit box is enough: the fragment shader only uses each pixel's direction from the camera.
        private static Mesh BuildBox(float boundsExtent)
        {
            var vertices = new Vector3[8];
            for (int k = 0; k < 8; k++)
            {
                vertices[k] = new Vector3((k & 1) == 0 ? -1f : 1f, (k & 2) == 0 ? -1f : 1f, (k & 4) == 0 ? -1f : 1f);
            }

            var mesh = new Mesh { name = "SkyBox" };
            mesh.vertices = vertices;
            mesh.triangles = new[]
            {
                0, 2, 1, 1, 2, 3, 4, 5, 6, 5, 7, 6, 0, 1, 4, 1, 5, 4,
                2, 6, 3, 3, 6, 7, 0, 4, 2, 2, 4, 6, 1, 3, 5, 3, 7, 5,
            };
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * boundsExtent);
            return mesh;
        }
    }
}
