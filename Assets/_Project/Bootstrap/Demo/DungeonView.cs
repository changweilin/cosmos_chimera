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
    /// D0 presentation of a <see cref="DungeonLayout"/>: a placeholder seal plugging the boss arena gate and a marker in
    /// each treasure room. The walls themselves are terrain (PlanetWalls). REQ: docs/design/planet-world.md#地表地牢.
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

        private static readonly Color SealColor = new Color(0.36f, 0.22f, 0.52f);
        private static readonly Color MarkerColor = new Color(0.95f, 0.78f, 0.25f);

        private readonly List<GameObject> _seals = new List<GameObject>();
        private readonly List<GameObject> _markers = new List<GameObject>();
        private readonly List<Material> _materials = new List<Material>();

        /// <summary>Seals currently standing: one when the dungeon has a lock; empty after OpenLocks.</summary>
        /// <remarks>Pre: none. Post: ≥ 0. Side effects: none.</remarks>
        public IReadOnlyList<GameObject> Seals => _seals;

        /// <summary>Treasure-room markers, in region order.</summary>
        /// <remarks>Pre: none. Post: ≥ 0. Side effects: none.</remarks>
        public IReadOnlyList<GameObject> Markers => _markers;

        /// <summary>Builds the seals and markers of <paramref name="dungeon"/>.</summary>
        /// <remarks>
        /// Pre: config valid and the one plan was generated for; plan, dungeon (generated from plan), groundAltitude and
        ///      shader non-null; called once.
        /// Post: Seals.Count == (dungeon.HasLock ? 1 : 0); Markers.Count == number of Treasure rooms. The seal is an
        ///       upright cylinder of radius ArenaGateOuterM reaching from below the ground to the wall top.
        /// Side effects: creates GameObjects and materials; calls groundAltitude.
        /// </remarks>
        public void Configure(in PlanetConfig config, RunPlan plan, DungeonLayout dungeon, Func<float3, float> groundAltitude,
            Shader shader)
        {
            Contract.Requires(config.IsValid, "config must be valid");
            Contract.Requires(plan != null && dungeon != null && groundAltitude != null && shader != null,
                "plan, dungeon, groundAltitude and shader are required");
            Contract.Requires(_seals.Count == 0 && _markers.Count == 0, "Configure must be called once");
            Material sealMaterial = PlanetLook.CreatureMaterial(shader, SealColor, 0.05f);
            Material markerMaterial = PlanetLook.CreatureMaterial(shader, MarkerColor, 0.03f);
            _materials.Add(sealMaterial);
            _materials.Add(markerMaterial);

            if (dungeon.HasLock)
            {
                // Unity's cylinder is 2 m tall and 1 m wide; its capsule collider cannot be this flat, so a convex mesh
                // collider takes its place.
                GameObject seal = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                seal.name = "ArenaSeal";
                Destroy(seal.GetComponent<Collider>());
                seal.AddComponent<MeshCollider>().convex = true;
                float height = DungeonPlanner.WallHeightM + SealSinkM;
                float mid = groundAltitude(dungeon.ArenaGate) + (DungeonPlanner.WallHeightM - SealSinkM) * 0.5f;
                Place(seal, config, dungeon.ArenaGate, mid);
                float diameter = DungeonPlanner.ArenaGateOuterM * 2f;
                seal.transform.localScale = new Vector3(diameter, height * 0.5f, diameter);
                seal.GetComponent<Renderer>().sharedMaterial = sealMaterial;
                _seals.Add(seal);
            }

            for (int r = 0; r < dungeon.Rooms.Count; r++)
            {
                if (dungeon.Rooms[r] != RoomRole.Treasure)
                {
                    continue;
                }

                GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
                marker.name = "TreasureMarker";
                Destroy(marker.GetComponent<Collider>());
                float3 at = plan.Regions[r].Center;
                Place(marker, config, at, groundAltitude(at) + MarkerM * 0.5f);
                marker.transform.localScale = Vector3.one * MarkerM;
                marker.GetComponent<Renderer>().sharedMaterial = markerMaterial;
                _markers.Add(marker);
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
        }
    }
}
