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
    /// D0 presentation of a <see cref="RunPopulace"/> (P3-18): one standing figure per resident, shaped by
    /// <see cref="NpcMeshes"/> and dressed in its settlement's region × era style mix.
    /// REQ: docs/design/characters.md#路人-npc.
    /// </summary>
    /// <remarks>
    /// Pre: Configure once. Post: one root per resident, all built immediately; geometry depends only on (populace,
    /// layout, ground function, planet type, seed, plan), so every client sees the same people. Each root carries one
    /// capsule on the rule layer's body circle (the character stops where creatures do). Figures farther than
    /// <see cref="DrawDistanceM"/> from the focus are not drawn; their colliders stay.
    /// Side effects: creates GameObjects, meshes and materials under this component and destroys them with it.
    /// </remarks>
    public sealed class PopulaceView : MonoBehaviour
    {
        /// <summary>Distance from the focus beyond which a figure is not drawn (m).</summary>
        /// <remarks>Pre: none. Post: constant > 0. Side effects: none.</remarks>
        public const float DrawDistanceM = 140f;

        // People sink a finger's width so soles never hover over the terrain's interpolated triangles.
        private const float SinkM = 0.03f;
        private const float CullEveryS = 0.25f;

        private readonly List<GameObject> _roots = new List<GameObject>();
        private readonly List<Renderer> _renderers = new List<Renderer>();
        private readonly List<Mesh> _meshes = new List<Mesh>();
        private readonly List<Material> _materials = new List<Material>();
        private Func<float3> _focus;
        private float _nextCull;

        /// <summary>Figure roots built, one per RunPopulace resident, in populace order.</summary>
        /// <remarks>Pre: none. Post: ≥ 0. Side effects: none.</remarks>
        public IReadOnlyList<GameObject> Residents => _roots;

        /// <summary>Builds every resident of <paramref name="populace"/>.</summary>
        /// <remarks>
        /// Pre: config valid and the one the populace was generated for; populace, layout (the one the populace was
        ///      generated from), groundAltitude and shader non-null; sceneryShader is Cosmimera/ToonScenery, or null to
        ///      draw with shader (one flat colour, no glow); planetTypeId, seed and plan as given to SettlementView, so
        ///      people are dressed in the style their houses are built in; focus returns the world position figures are
        ///      culled around, or null to always draw everyone; called once.
        /// Post: Residents.Count == populace.Residents.Count. Each root's +Y is the local up and +Z the resident's
        ///       Facing; its origin is on the ground; it holds one mesh with NpcMeshes.SubMeshCount materials and one
        ///       CapsuleCollider of PopulacePlanner.BodyRadiusM as tall as the figure.
        /// Side effects: creates GameObjects, meshes and materials; calls groundAltitude.
        /// </remarks>
        public void Configure(in PlanetConfig config, RunPopulace populace, RunLayout layout, Func<float3, float> groundAltitude,
            Shader shader, Shader sceneryShader = null, string planetTypeId = null, ulong seed = 0, RunPlan plan = null,
            Func<float3> focus = null)
        {
            Contract.Requires(config.IsValid, "config must be valid");
            Contract.Requires(populace != null && layout != null && groundAltitude != null && shader != null,
                "populace, layout, groundAltitude and shader are required");
            Contract.Requires(_roots.Count == 0, "Configure must be called once");
            _focus = focus;
            ArchPlanetStyle style = ArchitectureMeshes.PlanetStyleFor(planetTypeId, seed);
            Material[] materials = sceneryShader != null
                ? new[]
                {
                    Own(PlanetLook.SceneryMaterial(sceneryShader, Color.white, Color.white, 0f, 0f, 0f, 0f)),
                    Own(PlanetLook.SceneryMaterial(sceneryShader, Color.white, Color.white, 0f, 0f, 0f, 1.2f)),
                }
                : new[]
                {
                    Own(PlanetLook.CreatureMaterial(shader, new Color(0.78f, 0.7f, 0.62f), 0.02f)),
                    Own(PlanetLook.CreatureMaterial(shader, new Color(1f, 0.9f, 0.6f), 0f)),
                };

            foreach (SettlementPopulace home in populace.Settlements)
            {
                RunSettlement settlement = layout.Settlements[home.Settlement];
                string theme = plan != null && settlement.Region < plan.Regions.Count ? plan.Regions[settlement.Region].ThemeId : null;
                ArchMix mix = ArchitectureMeshes.MixFor(style, settlement.Kind, theme, seed, settlement.Index);
                ArchEra era = ArchitectureMeshes.SettlementEra(style, settlement.Kind);
                for (int i = home.FirstResident; i < home.FirstResident + home.ResidentCount; i++)
                {
                    RunResident resident = populace.Residents[i];
                    NpcSpec spec = NpcMeshes.SpecFor(resident, style.Region, era, mix);
                    Mesh mesh = NpcMeshes.Build(spec);
                    _meshes.Add(mesh);
                    float3 world = config.ToWorld(resident.Direction, groundAltitude(resident.Direction) - SinkM);
                    var root = new GameObject("Resident_" + resident.Race + "_" + resident.Role);
                    root.transform.SetParent(transform, false);
                    root.transform.SetPositionAndRotation(new Vector3(world.x, world.y, world.z),
                        Quaternion.LookRotation((Vector3)resident.Facing, (Vector3)resident.Direction));
                    root.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var renderer = root.AddComponent<MeshRenderer>();
                    renderer.sharedMaterials = materials;
                    float height = NpcMeshes.HeightM(spec);
                    var capsule = root.AddComponent<CapsuleCollider>();
                    capsule.radius = PopulacePlanner.BodyRadiusM;
                    capsule.height = math.max(height, PopulacePlanner.BodyRadiusM * 2f);
                    capsule.center = new Vector3(0f, capsule.height * 0.5f, 0f);
                    _roots.Add(root);
                    _renderers.Add(renderer);
                }
            }
        }

        private void Update()
        {
            if (_focus == null || Time.unscaledTime < _nextCull)
            {
                return;
            }

            _nextCull = Time.unscaledTime + CullEveryS;
            Vector3 focus = _focus();
            for (int i = 0; i < _roots.Count; i++)
            {
                bool near = (_roots[i].transform.position - focus).sqrMagnitude < DrawDistanceM * DrawDistanceM;
                if (_renderers[i].enabled != near)
                {
                    _renderers[i].enabled = near;
                }
            }
        }

        private Material Own(Material material)
        {
            _materials.Add(material);
            return material;
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
