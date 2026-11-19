using System;
using System.Collections.Generic;
using Cosmimera.Crafting;
using Cosmimera.Save;
using Cosmimera.WorldGen;
using UnityEngine;

namespace Cosmimera.Bootstrap.Demo
{
    /// <summary>
    /// Shared creature gear attachment builder for both the lobby <see cref="PaperdollPreview"/> and in-run
    /// <see cref="RunSession"/> views: virtual big hands holding weapons/shields, front accessory charm, and
    /// block shield VFX (P1-17c / P1-17d).
    /// </summary>
    internal static class CreatureGearAttachments
    {
        private static readonly string[] AllSlots =
        {
            EquipmentSlots.MainHand, EquipmentSlots.OffHand, EquipmentSlots.Upper, EquipmentSlots.Lower, EquipmentSlots.Accessory,
        };

        /// <summary>Collects the <see cref="PaperdollGear"/> entries equipped by <paramref name="owner"/>.</summary>
        internal static List<PaperdollGear> CollectFor(int owner, IReadOnlyList<EquippedItem> equipment, CraftCatalog catalog)
        {
            var result = new List<PaperdollGear>();
            if (equipment == null || catalog == null) return result;
            foreach (string slot in AllSlots)
            {
                for (int i = 0; i < equipment.Count; i++)
                {
                    EquippedItem entry = equipment[i];
                    if (entry.Owner != owner || entry.Slot != slot) continue;
                    if (!catalog.TryGetItem(entry.Item, out CraftItem item) || item.Equipment == null) continue;
                    GearLookSpec spec = GearLooks.ForCopy(GearLooks.KindFor(item.Equipment), entry.Look);
                    result.Add(new PaperdollGear(slot, slot + ":" + entry.Item + ":" + entry.Look.Origin.PlanetTypeId + ":" +
                        entry.Look.Origin.PlanetSeed + ":" + entry.Look.Seed, spec));
                    break;
                }
            }
            return result;
        }

        /// <summary>Creates the vertex-colour material used by procedural <see cref="GearMeshes"/>.</summary>
        internal static Material CreateGearMaterial(Shader sceneryShader, Shader fallbackToonShader)
        {
            if (sceneryShader != null)
            {
                return PlanetLook.SceneryMaterial(sceneryShader, Color.white, Color.white, 0f, 0f, 0f, 0f);
            }
            return CreateFlatMaterial(fallbackToonShader, Color.white);
        }

        /// <summary>Creates the pale glove material for virtual big hands.</summary>
        internal static Material CreateHandMaterial(Shader toonShader) =>
            CreateFlatMaterial(toonShader, new Color(0.9f, 0.92f, 0.98f));

        private static Material CreateFlatMaterial(Shader toonShader, Color color)
        {
            Shader shader = toonShader != null ? toonShader : Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            return new Material(shader) { color = color };
        }

        /// <summary>
        /// Attaches a creature's held weapon/shield (with virtual big hands) and front accessory charm under
        /// <paramref name="gearRoot"/>. Armour slots (Upper/Lower) are skipped per gear-looks.md#範圍.
        /// </summary>
        internal static void AttachCreatureGear(Transform modelRoot, Transform gearRoot, in PaperdollGear gear,
            Material gearMat, Material handMat, Bounds body, List<Mesh> ownedMeshes)
        {
            if (gear.Slot == EquipmentSlots.Upper || gear.Slot == EquipmentSlots.Lower) return;
            float height = Mathf.Max(0.3f, body.size.y);
            GearLookSpec spec = gear.Spec;
            if (gear.Slot == EquipmentSlots.Accessory)
            {
                // REQ: gear-looks.md#宙獸飾品 — upright bodies wear it high (Socket_Head), low wide ones mid-body (Socket_Core_FX).
                bool upright = body.size.y >= body.size.z * 0.8f;
                float y = upright ? body.max.y - height * 0.3f : body.center.y;
                var socket = new Vector3(body.center.x, y, body.max.z + 0.02f);
                Mount(modelRoot, gearRoot, spec, gearMat, socket, Quaternion.identity,
                    Vector3.one * Mathf.Clamp(height / 0.8f, 0.6f, 4f), null, ownedMeshes);
                return;
            }

            float scale = Mathf.Clamp(height, 0.6f, 2.5f);
            float side = gear.Slot == EquipmentSlots.MainHand ? 1f : -1f;
            var hand = new Vector3(body.center.x + side * (body.extents.x + 0.22f * scale), body.center.y, body.center.z + 0.1f * scale);
            AddVirtualHand(gearRoot, hand, side, scale, handMat);
            Quaternion grip = spec.Kind == GearKind.Shield ? Quaternion.Euler(0f, side * 25f, 0f) : Quaternion.Euler(55f, 0f, -side * 10f);
            Mount(modelRoot, gearRoot, spec, gearMat, hand, grip, Vector3.one * scale, null, ownedMeshes);
        }

        internal static void AddVirtualHand(Transform gearRoot, Vector3 at, float side, float scale, Material handMat)
        {
            GameObject palm = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            palm.name = side > 0f ? "VirtualHand_R" : "VirtualHand_L";
            palm.transform.SetParent(gearRoot, true);
            palm.transform.position = at;
            palm.transform.localScale = WorldToLocal(gearRoot, new Vector3(0.16f, 0.13f, 0.11f) * scale);
            StripColliders(palm);
            palm.GetComponent<Renderer>().sharedMaterial = handMat;

            GameObject thumb = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            thumb.transform.SetParent(palm.transform, false);
            thumb.transform.localPosition = new Vector3(-side * 0.45f, 0.25f, 0.35f);
            thumb.transform.localRotation = Quaternion.Euler(40f, 0f, side * 35f);
            thumb.transform.localScale = new Vector3(0.35f, 0.4f, 0.35f);
            StripColliders(thumb);
            thumb.GetComponent<Renderer>().sharedMaterial = handMat;
        }

        internal static GameObject Mount(Transform modelRoot, Transform gearRoot, GearLookSpec spec, Material mat,
            Vector3 at, Quaternion rotation, Vector3 scale, Transform follow, List<Mesh> ownedMeshes)
        {
            Mesh mesh = GearMeshes.Build(spec);
            ownedMeshes.Add(mesh);
            var go = new GameObject(mesh.name);
            go.transform.SetParent(gearRoot, false);
            go.transform.position = at;
            go.transform.rotation = modelRoot.rotation * rotation;
            go.transform.localScale = WorldToLocal(gearRoot, scale);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            if (follow != null) go.transform.SetParent(follow, true);
            return go;
        }

        internal static GameObject AttachBlockShield(Transform parent, in BlockShieldSpec spec, Material gearMat,
            float bodyRadiusM, List<Mesh> ownedMeshes)
        {
            Mesh mesh = GearMeshes.BuildBlockShield(spec);
            ownedMeshes.Add(mesh);
            var go = new GameObject("BlockShield");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, bodyRadiusM * 0.9f, bodyRadiusM + 0.35f);
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one * Mathf.Max(0.75f, bodyRadiusM * 1.15f);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = gearMat;
            go.SetActive(false);
            return go;
        }

        internal static Bounds ComputeBounds(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return new Bounds(root.transform.position + Vector3.up, Vector3.one);
            }

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }
            return bounds;
        }

        internal static void StripColliders(GameObject root)
        {
            Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                DestroyObject(colliders[i]);
            }
        }

        private static Vector3 WorldToLocal(Transform gearRoot, Vector3 worldScale)
        {
            Vector3 parent = gearRoot.lossyScale;
            return new Vector3(worldScale.x / parent.x, worldScale.y / parent.y, worldScale.z / parent.z);
        }

        internal static void DestroyObject(UnityEngine.Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(obj);
            else UnityEngine.Object.DestroyImmediate(obj);
        }
    }
}
