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
    /// D0 presentation of a <see cref="DungeonLayout"/>: a thicket of crystal spires plugging the boss arena gate and a
    /// treasure chest in each treasure room (<see cref="PropMeshes"/>). The walls themselves are terrain (PlanetWalls).
    /// REQ: docs/design/planet-world.md#地表地牢.
    /// </summary>
    /// <remarks>
    /// Pre: Configure once. Post: one seal while the dungeon is locked, until <see cref="OpenLocks"/>; one marker per
    /// Treasure room. The seal carries a collider of the same radius the rule layer blocks
    /// (DungeonPlanner.ArenaGateOuterM), so the character stops where creatures stop. Markers have no collider and no
    /// reward yet (Q-W3).
    /// Side effects: creates GameObjects and materials under this component and destroys them with it.
    /// </remarks>
    public sealed class DungeonView : MonoBehaviour
    {
        private const float SealSinkM = 4f;
        private const float MarkerM = 1.2f;
        // Golden angle: consecutive chests face unrelated ways without a random stream.
        private const float MarkerTurnDeg = 137.5f;

        private static readonly Color SealColor = new Color(0.36f, 0.22f, 0.52f);
        private static readonly Color SealGlint = new Color(0.82f, 0.62f, 1f);
        private static readonly Color MarkerColor = new Color(0.95f, 0.78f, 0.25f);
        private static readonly Color MarkerGlint = new Color(0.75f, 1f, 0.95f);

        private readonly List<GameObject> _seals = new List<GameObject>();
        private readonly List<GameObject> _markers = new List<GameObject>();
        private readonly Dictionary<int, GameObject> _markersByRegion = new Dictionary<int, GameObject>();
        private readonly List<Material> _materials = new List<Material>();
        private readonly List<Mesh> _meshes = new List<Mesh>();

        /// <summary>Seals currently standing: one when the dungeon has a lock; empty after OpenLocks.</summary>
        /// <remarks>Pre: none. Post: ≥ 0. Side effects: none.</remarks>
        public IReadOnlyList<GameObject> Seals => _seals;

        /// <summary>Treasure-room markers, in region order.</summary>
        /// <remarks>Pre: none. Post: ≥ 0. Side effects: none.</remarks>
        public IReadOnlyList<GameObject> Markers => _markers;

        /// <summary>Builds the seals and markers of <paramref name="dungeon"/>.</summary>
        /// <remarks>
        /// Pre: config valid and the one plan was generated for; plan, dungeon (generated from plan), groundAltitude and
        ///      shader non-null; propsShader (Cosmimera/ToonScenery) may be null (both are then drawn flat-coloured
        ///      with shader); called once.
        /// Post: Seals.Count == (dungeon.HasLock ? 1 : 0); Markers.Count == number of Treasure rooms. The seal collides
        ///       as an upright cylinder of radius ArenaGateOuterM reaching from below the ground to the wall top, and
        ///       its spires stay inside that cylinder. A marker stands on the sampled ground at its room's centre.
        /// Side effects: creates GameObjects, meshes and materials; calls groundAltitude.
        /// </remarks>
        public void Configure(in PlanetConfig config, RunPlan plan, DungeonLayout dungeon, Func<float3, float> groundAltitude,
            Shader shader, Shader propsShader = null)
        {
            Contract.Requires(config.IsValid, "config must be valid");
            Contract.Requires(plan != null && dungeon != null && groundAltitude != null && shader != null,
                "plan, dungeon, groundAltitude and shader are required");
            Contract.Requires(_seals.Count == 0 && _markers.Count == 0, "Configure must be called once");
            // The chest carries its wood and gold in vertex colours, which only the props shader reads.
            Material sealMaterial = propsShader != null
                ? PlanetLook.SceneryMaterial(propsShader, SealColor, SealGlint, 0.9f, 0f, 0f, 0.8f)
                : PlanetLook.CreatureMaterial(shader, SealColor, 0.05f);
            Material markerMaterial = propsShader != null
                ? PlanetLook.SceneryMaterial(propsShader, Color.white, MarkerGlint, 1f, 0f, 0f, 0.08f)
                : PlanetLook.CreatureMaterial(shader, MarkerColor, 0.03f);
            _materials.Add(sealMaterial);
            _materials.Add(markerMaterial);

            if (dungeon.HasLock)
            {
                // Unity's cylinder is 2 m tall and 1 m wide; its capsule collider cannot be this flat, so a convex mesh
                // collider takes its place. The cylinder is only the collider now: the spires below are what is drawn.
                GameObject seal = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                seal.name = "ArenaSeal";
                Destroy(seal.GetComponent<Collider>());
                seal.AddComponent<MeshCollider>().convex = true;
                // Disabled first: Destroy waits for the end of the frame, and the bare cylinder must never be drawn.
                Renderer plug = seal.GetComponent<Renderer>();
                plug.enabled = false;
                Destroy(plug);
                float height = DungeonPlanner.WallHeightM + SealSinkM;
                float mid = groundAltitude(dungeon.ArenaGate) + (DungeonPlanner.WallHeightM - SealSinkM) * 0.5f;
                Place(seal, config, dungeon.ArenaGate, mid);
                float diameter = DungeonPlanner.ArenaGateOuterM * 2f;
                seal.transform.localScale = new Vector3(diameter, height * 0.5f, diameter);
                // The seal mesh fills radius 1, y 0..1; the cylinder's own space is radius 0.5, y -1..1.
                GameObject spires = Draw("Spires", PropMeshes.Build(PropShape.Seal, 0), sealMaterial);
                spires.transform.SetParent(seal.transform, false);
                spires.transform.localPosition = new Vector3(0f, -1f, 0f);
                spires.transform.localScale = new Vector3(0.5f, 2f, 0.5f);
                _seals.Add(seal);
            }

            for (int r = 0; r < dungeon.Rooms.Count; r++)
            {
                if (dungeon.Rooms[r] != RoomRole.Treasure)
                {
                    continue;
                }

                GameObject marker = Draw("TreasureMarker",
                    PropMeshes.Build(PropShape.Chest, r % PropMeshes.VariantCount(PropShape.Chest)), markerMaterial);
                float3 at = plan.Regions[r].Center;
                Place(marker, config, at, groundAltitude(at));
                marker.transform.localRotation *= Quaternion.AngleAxis(r * MarkerTurnDeg, Vector3.up);
                marker.transform.localScale = Vector3.one * MarkerM;
                _markers.Add(marker);
                _markersByRegion[r] = marker;
            }
        }

        /// <summary>Treasure-room container GameObject for <paramref name="region"/>, or null if absent/destroyed.</summary>
        /// <remarks>Pre: none. Post: null if not a treasure room or already removed. Side effects: none.</remarks>
        public GameObject MarkerFor(int region) =>
            _markersByRegion.TryGetValue(region, out GameObject go) ? go : null;

        /// <summary>Removes the treasure container for <paramref name="region"/> once broken (Q-W3).</summary>
        /// <remarks>Pre: none. Post: MarkerFor(region) == null. Side effects: destroys the marker object; idempotent.</remarks>
        public void RemoveMarker(int region)
        {
            if (_markersByRegion.TryGetValue(region, out GameObject go))
            {
                _markersByRegion.Remove(region);
                _markers.Remove(go);
                if (go != null)
                {
                    Destroy(go);
                }
            }
        }

        /// <summary>Removes every seal (the key elite was knocked out).</summary>
        /// <remarks>Pre: none. Post: Seals is empty. Side effects: destroys the seal objects; idempotent.</remarks>
        public void OpenLocks()
        {
            foreach (GameObject seal in _seals)
            {
                Destroy(seal);
            }

            _seals.Clear();
        }

        private GameObject Draw(string name, Mesh mesh, Material material)
        {
            _meshes.Add(mesh);
            var go = new GameObject(name);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }

        private void Place(GameObject go, in PlanetConfig config, float3 direction, float altitude)
        {
            go.transform.SetParent(transform, false);
            go.transform.SetPositionAndRotation(config.ToWorld(direction, altitude),
                quaternion.LookRotationSafe(PlanetFrame.FromUp(direction).North, direction));
        }

        private void OnDestroy()
        {
            foreach (Material m in _materials)
            {
                Destroy(m);
            }

            foreach (Mesh mesh in _meshes)
            {
                Destroy(mesh);
            }
        }
    }
}
