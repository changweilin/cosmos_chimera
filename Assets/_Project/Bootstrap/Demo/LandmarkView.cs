using System;
using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.Planet;
using Cosmimera.WorldGen;
using Unity.Mathematics;
using UnityEngine;

namespace Cosmimera.Bootstrap.Demo
{
    /// <summary>
    /// D0 presentation of a <see cref="LandmarkLayout"/>: one procedural mesh per wonder, styled by the planet's
    /// tradition and each wonder's own era and age.
    /// REQ: docs/design/planet-world.md#奇觀地標.
    /// </summary>
    /// <remarks>
    /// Pre: Configure once. Post: one root per landmark, all built immediately (a planet has ≤ 12, and their whole
    /// point is to be visible from anywhere, so they are never streamed). Solid wonders carry one capsule on the rule
    /// layer's footprint circle, so the character stops where creatures do.
    /// Side effects: creates GameObjects, meshes and materials under this component and destroys them with it.
    /// </remarks>
    public sealed class LandmarkView : MonoBehaviour
    {
        private const int RimSamples = 8;

        private readonly List<GameObject> _roots = new List<GameObject>();
        private readonly List<Mesh> _meshes = new List<Mesh>();
        private readonly List<Material> _materials = new List<Material>();

        /// <summary>Landmark roots built, one per LandmarkLayout landmark, in layout order.</summary>
        /// <remarks>Pre: none. Post: ≥ 0. Side effects: none.</remarks>
        public IReadOnlyList<GameObject> Landmarks => _roots;

        /// <summary>Builds every landmark of <paramref name="layout"/>.</summary>
        /// <remarks>
        /// Pre: config valid and the one the layout was generated for; layout, groundAltitude and shader non-null;
        ///      sceneryShader is Cosmimera/ToonScenery, or null to draw with shader (flat colours, no weathering or
        ///      glow); planetTypeId picks the regional tradition (null or unknown: verdant) and seed the styles; called
        ///      once.
        /// Post: Landmarks.Count == layout.Landmarks.Count. Each root's +Y is the local up and +Z the landmark's
        ///       Facing. A ground wonder's origin is at most LandmarkMeshes.SinkM − 0.3 m above the lowest ground
        ///       under its footprint, so its base never floats; a floating one sits AltitudeM above the ground at its
        ///       centre (the station: above its elevator's origin, so it caps the tether). Solid ones hold one
        ///       CapsuleCollider of the footprint radius; the others hold no collider. The same arguments draw the same
        ///       wonders on every client.
        /// Side effects: creates GameObjects, meshes and materials; calls groundAltitude.
        /// </remarks>
        public void Configure(in PlanetConfig config, LandmarkLayout layout, Func<float3, float> groundAltitude, Shader shader,
            Shader sceneryShader = null, string planetTypeId = null, ulong seed = 0)
        {
            Contract.Requires(config.IsValid, "config must be valid");
            Contract.Requires(layout != null && groundAltitude != null && shader != null,
                "layout, groundAltitude and shader are required");
            Contract.Requires(_roots.Count == 0, "Configure must be called once");
            ArchPlanetStyle style = ArchitectureMeshes.PlanetStyleFor(planetTypeId, seed);
            float elevatorOrigin = 0f;
            foreach (RunLandmark landmark in layout.Landmarks)
            {
                LandmarkSpec spec = LandmarkMeshes.SpecFor(style, landmark, seed);
                float altitude;
                if (landmark.Kind == LandmarkKind.SpaceStation)
                {
                    altitude = elevatorOrigin + landmark.AltitudeM;
                }
                else if (landmark.Solid)
                {
                    altitude = BaseAltitude(config, landmark, groundAltitude);
                }
                else
                {
                    altitude = groundAltitude(landmark.Direction) + landmark.AltitudeM;
                }

                if (landmark.Kind == LandmarkKind.SpaceElevator)
                {
                    elevatorOrigin = altitude;
                }

                float3 world = config.ToWorld(landmark.Direction, altitude);
                var root = new GameObject("Landmark_" + landmark.Kind);
                root.transform.SetParent(transform, false);
                root.transform.SetPositionAndRotation(new Vector3(world.x, world.y, world.z),
                    Quaternion.LookRotation((Vector3)landmark.Facing, (Vector3)landmark.Direction));
                Mesh mesh = LandmarkMeshes.Build(spec);
                _meshes.Add(mesh);
                root.AddComponent<MeshFilter>().sharedMesh = mesh;
                root.AddComponent<MeshRenderer>().sharedMaterials = MaterialsFor(spec, shader, sceneryShader);
                if (landmark.Solid)
                {
                    // The cylinder part runs from the mesh's sunk foot to the top of the solid body; the end caps
                    // lie below the ground and above anything the character can reach.
                    float body = LandmarkMeshes.BodyHeightM(spec);
                    float r = landmark.FootprintRadiusM;
                    var capsule = root.AddComponent<CapsuleCollider>();
                    capsule.direction = 1;
                    capsule.radius = r;
                    capsule.height = body + LandmarkMeshes.SinkM + 2f * r;
                    capsule.center = new Vector3(0f, (body - LandmarkMeshes.SinkM) * 0.5f, 0f);
                }

                _roots.Add(root);
            }
        }

        // Low enough that the downhill edge of the footprint is still inside the mesh's sunk base, but never above the
        // centre's own ground (a wonder on a knoll sits on the knoll, not over it).
        private static float BaseAltitude(in PlanetConfig config, in RunLandmark landmark, Func<float3, float> groundAltitude)
        {
            float centre = groundAltitude(landmark.Direction);
            float lo = centre;
            PlanetFrame frame = PlanetFrame.FromUp(landmark.Direction);
            for (int i = 0; i < RimSamples; i++)
            {
                float a = i * (2f * math.PI / RimSamples);
                float3 rim = math.normalize(landmark.Direction +
                    (frame.East * math.cos(a) + frame.North * math.sin(a)) * (landmark.FootprintRadiusM / config.Radius));
                lo = math.min(lo, groundAltitude(rim));
            }

            return math.min(centre, lo + LandmarkMeshes.SinkM - 0.3f);
        }

        private Material[] MaterialsFor(in LandmarkSpec spec, Shader shader, Shader sceneryShader)
        {
            ArchPalette p = LandmarkMeshes.PaletteFor(spec);
            var set = new Material[ArchitectureMeshes.SubMeshCount];
            if (sceneryShader != null)
            {
                set[ArchitectureMeshes.WallSubMesh] = PlanetLook.SceneryMaterial(sceneryShader, p.Wall, p.Weathering, p.WeatheringStrength, 0f, 0f, 0f);
                set[ArchitectureMeshes.RoofSubMesh] = PlanetLook.SceneryMaterial(sceneryShader, p.Roof, p.Weathering, p.WeatheringStrength, 0f, 0f, 0f);
                set[ArchitectureMeshes.TrimSubMesh] = PlanetLook.SceneryMaterial(sceneryShader, p.Trim, p.Trim, 0f, 0f, 0f, 0f);
                // Brighter than a building's lamps: a wonder's lights are how it is found at night from across the planet.
                set[ArchitectureMeshes.GlowSubMesh] = PlanetLook.SceneryMaterial(sceneryShader, p.Glow, p.Glow, 0f, 0f, 0f, 1.4f);
            }
            else
            {
                set[ArchitectureMeshes.WallSubMesh] = PlanetLook.CreatureMaterial(shader, p.Wall, 0.03f);
                set[ArchitectureMeshes.RoofSubMesh] = PlanetLook.CreatureMaterial(shader, p.Roof, 0.03f);
                set[ArchitectureMeshes.TrimSubMesh] = PlanetLook.CreatureMaterial(shader, p.Trim, 0.03f);
                set[ArchitectureMeshes.GlowSubMesh] = PlanetLook.CreatureMaterial(shader, p.Glow, 0.03f);
            }

            _materials.AddRange(set);
            return set;
        }

        private void OnDestroy()
        {
            foreach (Material m in _materials)
            {
                if (m != null)
                {
                    Destroy(m);
                }
            }

            foreach (Mesh mesh in _meshes)
            {
                if (mesh != null)
                {
                    Destroy(mesh);
                }
            }
        }
    }
}
