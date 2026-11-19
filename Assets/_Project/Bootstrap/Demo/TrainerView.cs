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
    /// D0 presentation of <see cref="RunTrainers"/> (P3-32): one standing/wandering procedural figure per outdoor
    /// trainer, shaped by <see cref="NpcMeshes.SpecForTrainer"/> using its borrowed <see cref="RunTrainer.PropRole"/>.
    /// REQ: docs/design/trainer-battles.md#外觀佔位.
    /// </summary>
    /// <remarks>
    /// Pre: Configure once.
    /// Post: Trainers.Count == trainers.Trainers.Count. Each root holds one SkinnedMeshRenderer with stride bones
    ///       and one CapsuleCollider; figures beyond <see cref="DrawDistanceM"/> from focus hide renderers while
    ///       colliders stay active.
    /// Side effects: creates GameObjects, meshes and materials under this component and destroys them with it.
    /// </remarks>
    public sealed class TrainerView : MonoBehaviour
    {
        /// <summary>Distance from the focus beyond which a trainer figure is not drawn (m).</summary>
        /// <remarks>Pre: none. Post: constant &gt; 0. Side effects: none.</remarks>
        public const float DrawDistanceM = 140f;

        // Soles sink slightly so feet do not hover over interpolated terrain triangles.
        private const float SinkM = 0.03f;
        private const float CullEveryS = 0.25f;

        private readonly List<GameObject> _roots = new List<GameObject>();
        private readonly List<Renderer> _renderers = new List<Renderer>();
        private readonly List<ResidentStride> _strides = new List<ResidentStride>();
        private readonly List<Mesh> _meshes = new List<Mesh>();
        private readonly List<Material> _materials = new List<Material>();
        private PlanetConfig _config;
        private RunTrainers _trainers;
        private Func<float3, float> _groundAltitude;
        private Func<float3> _focus;
        private int _syncedStepVersion = -1;
        private float _cullCooldown;

        /// <summary>Created trainer root GameObjects, in <see cref="RunTrainers.Trainers"/> order.</summary>
        /// <remarks>Pre: none. Post: read-only, non-null. Side effects: none.</remarks>
        public IReadOnlyList<GameObject> Trainers => _roots;

        /// <summary>Builds every wandering trainer figure for <paramref name="trainers"/>.</summary>
        /// <remarks>
        /// Pre: config valid; trainers, groundAltitude and shader non-null; called once.
        /// Post: Trainers.Count == trainers.Trainers.Count.
        /// Side effects: allocates child GameObjects, skinned meshes and materials; evaluates groundAltitude.
        /// </remarks>
        public void Configure(in PlanetConfig config, RunTrainers trainers, Func<float3, float> groundAltitude,
            Shader shader, Shader sceneryShader = null, string planetTypeId = null, ulong seed = 0,
            Func<float3> focus = null)
        {
            Contract.Requires(config.IsValid, "config must be valid");
            Contract.Requires(trainers != null && groundAltitude != null && shader != null,
                "trainers, groundAltitude and shader are required");
            Contract.Requires(_roots.Count == 0, "Configure must be called once");

            _config = config;
            _trainers = trainers;
            _groundAltitude = groundAltitude;
            _focus = focus;
            _syncedStepVersion = trainers.StepVersion;

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

            for (int i = 0; i < trainers.Trainers.Count; i++)
            {
                RunTrainer t = trainers.Trainers[i];
                NpcSpec spec = NpcMeshes.SpecForTrainer(t, style.Region, style.Era, seed);
                float3 dir = trainers.CurrentDirection(i);
                float3 facing = trainers.CurrentFacing(i);
                float alt = groundAltitude(dir);
                float3 world = config.ToWorld(dir, alt - SinkM);

                var root = new GameObject($"Trainer_{i}_{t.ProfessionId}");
                root.transform.SetParent(transform, false);
                root.transform.SetPositionAndRotation(new Vector3(world.x, world.y, world.z),
                    Quaternion.LookRotation((Vector3)facing, (Vector3)dir));

                Mesh mesh = NpcMeshes.Build(spec, root.transform, out ResidentStride stride);
                var renderer = root.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMesh = mesh;
                renderer.bones = stride.Bones;
                renderer.rootBone = root.transform;
                renderer.sharedMaterials = materials;

                stride.PreviousDirection = dir;
                _meshes.Add(mesh);
                _renderers.Add(renderer);
                _strides.Add(stride);

                float height = NpcMeshes.HeightM(spec);
                var capsule = root.AddComponent<CapsuleCollider>();
                capsule.radius = PopulacePlanner.BodyRadiusM;
                capsule.height = math.max(height, PopulacePlanner.BodyRadiusM * 2f);
                capsule.center = new Vector3(0f, capsule.height * 0.5f, 0f);
                _roots.Add(root);
            }
        }

        /// <summary>Synchronizes all trainer root transforms and stride poses to the latest rule-layer positions.</summary>
        /// <remarks>Pre: none (no-op before Configure). Post: root positions match RunTrainers directions. Side effects: mutates transforms and bones.</remarks>
        public void Sync()
        {
            if (_trainers == null || _syncedStepVersion == _trainers.StepVersion)
            {
                return;
            }

            _syncedStepVersion = _trainers.StepVersion;
            for (int i = 0; i < _roots.Count; i++)
            {
                float3 dir = _trainers.CurrentDirection(i);
                float3 facing = _trainers.CurrentFacing(i);
                float alt = _groundAltitude(dir);
                float3 world = _config.ToWorld(dir, alt - SinkM);
                ResidentStride stride = _strides[i];
                stride.Advance(math.distance(dir, stride.PreviousDirection) * _config.Radius, _renderers[i].enabled);
                stride.PreviousDirection = dir;
                _roots[i].transform.SetPositionAndRotation(new Vector3(world.x, world.y, world.z),
                    Quaternion.LookRotation((Vector3)facing, (Vector3)dir));
            }
        }

        private void LateUpdate()
        {
            if (_trainers == null)
            {
                return;
            }

            Sync();
            if (_focus == null)
            {
                return;
            }

            _cullCooldown -= Time.unscaledDeltaTime;
            if (_cullCooldown > 0f)
            {
                return;
            }

            _cullCooldown = CullEveryS;
            float3 f = _focus();
            var focus = new Vector3(f.x, f.y, f.z);
            float maxDistSq = DrawDistanceM * DrawDistanceM;
            for (int i = 0; i < _roots.Count; i++)
            {
                bool near = (_roots[i].transform.position - focus).sqrMagnitude < maxDistSq;
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
