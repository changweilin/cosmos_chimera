using System;
using System.Collections.Generic;
using System.Text;
using Cosmimera.Crafting;
using Cosmimera.Core.Contracts;
using Cosmimera.GameData;
using Cosmimera.WorldGen;
using UnityEngine;

namespace Cosmimera.Bootstrap.Demo
{
    /// <summary>One equipped copy as the paperdoll preview draws it.</summary>
    /// <remarks>
    /// Pre: EquipmentSlots.IsValid(slot); key non-empty and unique per (slot, item, look); spec non-null.
    /// Post: immutable. Side effects: none.
    /// </remarks>
    internal readonly struct PaperdollGear
    {
        internal PaperdollGear(string slot, string key, GearLookSpec spec)
        {
            Contract.Requires(EquipmentSlots.IsValid(slot) && !string.IsNullOrEmpty(key) && spec != null, "invalid paperdoll gear");
            Slot = slot;
            Key = key;
            Spec = spec;
        }

        internal string Slot { get; }
        internal string Key { get; }
        internal GearLookSpec Spec { get; }
    }

    /// <summary>
    /// Isolated 3D RenderTexture stage for the lobby paperdoll preview.
    /// Displays the selected player character or creature with procedural gear attachments.
    /// </summary>
    /// <remarks>
    /// Pre: created on the main thread; toonShader may be null (falls back to URP Lit); sceneryShader
    /// (Cosmimera/ToonScenery) may be null (gear then draws without its baked vertex colours).
    /// Post: Dispose releases the RenderTexture, stage GameObjects and transient Materials.
    /// Side effects: allocates offscreen RenderTexture, Camera, Light and preview GameObjects.
    /// </remarks>
    internal sealed class PaperdollPreview : IDisposable
    {
        private const int PreviewWidth = 320;
        private const int PreviewHeight = 440;
        private static readonly Vector3 StageOrigin = new Vector3(0f, -500f, 0f);

        private readonly Shader _toonShader;
        private readonly Shader _sceneryShader;
        private readonly List<Mesh> _gearMeshes = new List<Mesh>();
        private readonly List<Material> _gearMaterials = new List<Material>();
        private readonly StringBuilder _keyBuilder = new StringBuilder();
        private readonly List<Material> _materials = new List<Material>();
        private readonly GameObject _stageRoot;
        private readonly Transform _pivot;
        private readonly Camera _camera;
        private readonly RenderTexture _rt;

        private GameObject _model;
        private ICreatureView _creatureView;
        private Transform _gearRoot;
        private string _targetKey = string.Empty;
        private string _gearKey = string.Empty;
        private float _yaw;
        private bool _disposed;

        internal Texture Texture => _rt;
        internal float Yaw => _yaw;

        internal PaperdollPreview(Shader toonShader, Shader sceneryShader)
        {
            _toonShader = toonShader;
            _sceneryShader = sceneryShader;
            _stageRoot = new GameObject("PaperdollStage");
            _stageRoot.hideFlags = HideFlags.DontSave;
            _stageRoot.transform.position = StageOrigin;

            var pivotGo = new GameObject("Pivot");
            pivotGo.transform.SetParent(_stageRoot.transform, false);
            _pivot = pivotGo.transform;

            var camGo = new GameObject("PaperdollCamera");
            camGo.transform.SetParent(_stageRoot.transform, false);
            camGo.transform.localPosition = new Vector3(0f, 1.05f, 3.1f);
            camGo.transform.LookAt(_stageRoot.transform.position + new Vector3(0f, 0.95f, 0f));

            _rt = new RenderTexture(PreviewWidth, PreviewHeight, 24, RenderTextureFormat.ARGB32)
            {
                name = "PaperdollRT"
            };
            _rt.Create();

            _camera = camGo.AddComponent<Camera>();
            // URP renders this camera itself each frame (targetTexture set, drawn before the main camera
            // via lower depth). A manual Camera.Render() from OnGUI runs nested inside
            // UniversalRenderPipeline.Render and corrupts its frame data (UniversalCameraData "already created").
            _camera.enabled = true;
            _camera.depth = -100f;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0.05f, 0.07f, 0.12f, 1f);
            _camera.fieldOfView = 32f;
            _camera.nearClipPlane = 0.1f;
            _camera.farClipPlane = 20f;
            _camera.targetTexture = _rt;

            var lightGo = new GameObject("PaperdollLight");
            lightGo.transform.SetParent(_stageRoot.transform, false);
            lightGo.transform.localRotation = Quaternion.Euler(28f, 150f, 0f);
            Light light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.35f;
            light.color = Color.white;
        }

        internal void Rotate(float deltaDegrees)
        {
            _yaw = Mathf.Repeat(_yaw + deltaDegrees, 360f);
        }

        internal void SetPlayerTarget(GameObject prefab, RuntimeAnimatorController controller, string characterKey,
            IReadOnlyList<PaperdollGear> gear)
        {
            string nextKey = "player:" + (characterKey ?? "capsule");
            if (_targetKey != nextKey)
            {
                _targetKey = nextKey;
                _gearKey = string.Empty;
                ClearModel();
                if (prefab != null)
                {
                    _model = UnityEngine.Object.Instantiate(prefab, _pivot, false);
                    StripColliders(_model);
                    Animator anim = _model.GetComponentInChildren<Animator>();
                    if (anim != null && controller != null)
                    {
                        anim.runtimeAnimatorController = controller;
                    }
                }
                else
                {
                    _model = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                    _model.transform.SetParent(_pivot, false);
                    _model.transform.localPosition = new Vector3(0f, 1f, 0f);
                    StripColliders(_model);
                    Renderer r = _model.GetComponentInChildren<Renderer>();
                    if (r != null) r.sharedMaterial = CreateMaterial(new Color(0.68f, 0.78f, 0.92f));
                }

                FrameModel(isCreature: false);
            }

            SyncGear(gear, isCreature: false);
        }

        internal void SetCreatureTarget(CreatureDef def, int instanceId, IReadOnlyList<PaperdollGear> gear)
        {
            string speciesId = def != null ? def.Id : "unknown";
            string nextKey = "creature:" + instanceId + ":" + speciesId;
            if (_targetKey != nextKey)
            {
                _targetKey = nextKey;
                _gearKey = string.Empty;
                ClearModel();
                if (def != null)
                {
                    var factory = new ProceduralCreatureViewFactory(_toonShader);
                    _creatureView = factory.Create(new CreatureViewRequest(def, ally: true, SpawnRole.Normal, 0.5f));
                    _model = _creatureView.Root.gameObject;
                    _model.transform.SetParent(_pivot, false);
                    StripColliders(_model);
                }
                else
                {
                    _model = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    _model.transform.SetParent(_pivot, false);
                    _model.transform.localPosition = new Vector3(0f, 0.6f, 0f);
                    StripColliders(_model);
                }

                FrameModel(isCreature: true);
            }

            SyncGear(gear, isCreature: true);
        }

        /// <summary>Applies the current yaw to the stage so the next pipeline frame shows it.</summary>
        /// <remarks>
        /// Pre: main thread; safe to call from OnGUI (it never triggers a render).
        /// Post: the preview pivot faces <see cref="Yaw"/>; <see cref="Texture"/> updates on the next URP frame.
        /// Side effects: modifies the pivot transform; no-op after Dispose.
        /// </remarks>
        internal void SyncPose()
        {
            if (_disposed || _pivot == null) return;
            _pivot.localRotation = Quaternion.Euler(0f, _yaw, 0f);
        }

        private void FrameModel(bool isCreature)
        {
            if (_model == null || _camera == null) return;
            Bounds bounds = ComputeBounds(_model);
            float height = Mathf.Max(0.8f, Mathf.Max(bounds.size.y, Mathf.Max(bounds.size.x, bounds.size.z) * 0.75f));
            float centerY = bounds.center.y - _stageRoot.transform.position.y;
            float dist = isCreature ? Mathf.Max(2.2f, height * 2.5f) : Mathf.Max(2.6f, height * 2.0f);
            _camera.transform.localPosition = new Vector3(0f, centerY + height * 0.1f, dist);
            _camera.transform.LookAt(_stageRoot.transform.position + new Vector3(0f, centerY, 0f));
        }

        private void SyncGear(IReadOnlyList<PaperdollGear> gear, bool isCreature)
        {
            _keyBuilder.Clear().Append(isCreature ? 'c' : 'p');
            int count = gear?.Count ?? 0;
            for (int i = 0; i < count; i++) _keyBuilder.Append('|').Append(gear[i].Key);
            string nextGear = _keyBuilder.ToString();
            if (_gearKey == nextGear || _model == null) return;
            _gearKey = nextGear;

            ClearGear();
            var rootGo = new GameObject("PaperdollGear");
            rootGo.transform.SetParent(_model.transform, false);
            _gearRoot = rootGo.transform;
            if (count == 0) return;

            // Mount points are measured with the turntable at rest so they sit in the model's own frame (+Z front);
            // a yawed AABB would put the creature's side sockets on the wrong axis.
            Quaternion turned = _pivot.localRotation;
            _pivot.localRotation = Quaternion.identity;
            Material gearMat = GearMaterial();
            // Measured before any gear joins the hierarchy.
            Bounds body = isCreature ? ComputeBounds(_model) : default;
            PlayerRig rig = isCreature ? default : MeasurePlayer();
            for (int i = 0; i < count; i++)
            {
                if (isCreature) AttachCreatureGear(gear[i], gearMat, body);
                else AttachPlayerGear(gear[i], gearMat, rig);
            }
            // Side-held weapons widen the silhouette; frame the whole outfit.
            FrameModel(isCreature);
            _pivot.localRotation = turned;
        }

        // Where player gear goes, measured once per gear change in the model's rest frame.
        private struct PlayerRig
        {
            internal Vector3 Ground, Hips, Neck, HandR, HandL;
            internal float ShoulderHalf, HipHalf, HandScale;
            internal Transform HipsBone, NeckBone, HandRBone, HandLBone;
        }

        // REQ: art-asset-pipeline.md (角色掛點 Socket_HandR_Prop／Socket_HandL_Prop) — the provisional characters are Generic
        // rigs with humanoid bone names, so bones and sockets are found by name; a Humanoid avatar is the second source,
        // and the capsule stand-in (2 m tall, centred one metre up, facing +Z) the last.
        private PlayerRig MeasurePlayer()
        {
            Transform root = _model.transform;
            Animator anim = _model.GetComponentInChildren<Animator>();
            bool human = anim != null && anim.isHuman;
            // Pose the rig at idle frame zero before measuring, so bone-followed gear only inherits the idle sway
            // rather than the bind-pose-to-idle swing of the hands.
            if (anim != null && anim.runtimeAnimatorController != null) anim.Update(0f);
            Transform Find(string name, HumanBodyBones bone)
            {
                Transform t = FindDeep(root, name);
                return t != null ? t : human ? anim.GetBoneTransform(bone) : null;
            }

            Vector3 c = root.position;
            var rig = new PlayerRig
            {
                HipsBone = Find("Hips", HumanBodyBones.Hips),
                NeckBone = Find("Neck", HumanBodyBones.Neck),
                HandRBone = FindDeep(root, "Socket_HandR_Prop") ?? Find("RightHand", HumanBodyBones.RightHand),
                HandLBone = FindDeep(root, "Socket_HandL_Prop") ?? Find("LeftHand", HumanBodyBones.LeftHand),
            };
            Transform footL = Find("LeftFoot", HumanBodyBones.LeftFoot), footR = Find("RightFoot", HumanBodyBones.RightFoot);
            Transform armL = Find("LeftUpperArm", HumanBodyBones.LeftUpperArm), armR = Find("RightUpperArm", HumanBodyBones.RightUpperArm);
            Transform legL = Find("LeftUpperLeg", HumanBodyBones.LeftUpperLeg), legR = Find("RightUpperLeg", HumanBodyBones.RightUpperLeg);

            if (rig.HipsBone == null || rig.NeckBone == null)
            {
                rig.Ground = c - Vector3.up;
                rig.Hips = c;
                rig.Neck = c + Vector3.up * 0.75f;
                rig.HandR = c + new Vector3(0.45f, -0.05f, 0.1f);
                rig.HandL = c + new Vector3(-0.45f, -0.05f, 0.1f);
                rig.ShoulderHalf = 0.4f;
                rig.HipHalf = 0.35f;
                rig.HandScale = 1f;
                rig.HipsBone = rig.NeckBone = rig.HandRBone = rig.HandLBone = null;
                return rig;
            }

            rig.Hips = rig.HipsBone.position;
            rig.Neck = rig.NeckBone.position;
            rig.Ground = new Vector3(rig.Hips.x, footL != null && footR != null ? Mathf.Min(footL.position.y, footR.position.y) : c.y, rig.Hips.z);
            rig.HandR = rig.HandRBone != null ? rig.HandRBone.position : rig.Hips + new Vector3(0.3f, 0f, 0f);
            rig.HandL = rig.HandLBone != null ? rig.HandLBone.position : rig.Hips - new Vector3(0.3f, 0f, 0f);
            // Joint spacing understates the flesh around it; the factors put cloth just outside a slim build.
            rig.ShoulderHalf = armL != null && armR != null ? Vector3.Distance(armL.position, armR.position) * 0.5f * 1.2f : 0.2f;
            rig.HipHalf = legL != null && legR != null ? Vector3.Distance(legL.position, legR.position) * 0.5f * 1.9f : 0.17f;
            // Gear is modelled at adult size (neck ~1.45 m above the soles); the provisional cast is shorter.
            rig.HandScale = Mathf.Clamp((rig.Neck.y - rig.Ground.y) / 1.45f, 0.5f, 1.5f);
            return rig;
        }

        // REQ: gear-looks.md#範圍 — player gear is a paperdoll on the rig: weapons in the hand sockets, garments from hips and feet.
        // Placeholder: garments are layered over the body; the part library swaps body parts instead (P1-17e).
        private void AttachPlayerGear(in PaperdollGear gear, Material mat, in PlayerRig rig)
        {
            GearLookSpec spec = gear.Spec;
            float len = GearMeshes.NominalLength(spec.Kind) * spec.Length;
            switch (gear.Slot)
            {
                case EquipmentSlots.MainHand:
                {
                    // Held forward and up from the fist, rolled outward so the blade clears the torso.
                    float outward = rig.HandR.x >= rig.Hips.x ? 1f : -1f;
                    Mount(spec, mat, rig.HandR, Quaternion.Euler(55f, 0f, -outward * 25f), Vector3.one * rig.HandScale, rig.HandRBone);
                    break;
                }
                case EquipmentSlots.OffHand:
                {
                    // Outward is wherever this hand actually is: imported rigs may mirror left and right across X.
                    float outward = rig.HandL.x >= rig.Hips.x ? 1f : -1f;
                    if (spec.Kind == GearKind.Shield)
                        Mount(spec, mat, rig.HandL + new Vector3(outward * 0.06f, 0f, 0.04f) * rig.HandScale, Quaternion.Euler(0f, outward * 35f, 0f),
                            Vector3.one * rig.HandScale, rig.HandLBone);
                    else
                        Mount(spec, mat, rig.HandL, Quaternion.Euler(55f, 0f, -outward * 25f), Vector3.one * rig.HandScale, rig.HandLBone);
                    break;
                }
                case EquipmentSlots.Upper:
                {
                    // Built from the waist (y 0) to the collar (y len) with 0.2·Width half-shoulders: stretched to this body.
                    float across = Mathf.Clamp(rig.ShoulderHalf / (0.2f * spec.Width), 0.3f, 3f);
                    Mount(spec, mat, rig.Hips, Quaternion.identity,
                        new Vector3(across, Mathf.Max(0.2f, rig.Neck.y - rig.Hips.y) / len, across), rig.HipsBone);
                    break;
                }
                case EquipmentSlots.Lower:
                {
                    float across = Mathf.Clamp(rig.HipHalf / (0.17f * spec.Width), 0.3f, 3f);
                    Mount(spec, mat, rig.Ground, Quaternion.identity,
                        new Vector3(across, Mathf.Max(0.2f, rig.Hips.y - rig.Ground.y) / len, across), rig.HipsBone);
                    break;
                }
                default:
                    Mount(spec, mat, rig.Neck + new Vector3(0f, -0.06f, 0.1f), Quaternion.identity, Vector3.one, rig.NeckBone);
                    break;
            }
        }

        private static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform hit = FindDeep(root.GetChild(i), name);
                if (hit != null) return hit;
            }
            return null;
        }

        // REQ: gear-looks.md#範圍 — creatures hold weapons and shields in the virtual big hand at Socket_Weapon_R/_L,
        // never draw armour on the body (only on the block shield, P1-17d), and hang the charm at Socket_Head or
        // Socket_Core_FX scaled by height. Placeholder bodies have no sockets yet, so both come from the body bounds.
        private void AttachCreatureGear(in PaperdollGear gear, Material mat, Bounds body)
        {
            if (gear.Slot == EquipmentSlots.Upper || gear.Slot == EquipmentSlots.Lower) return;
            float height = Mathf.Max(0.3f, body.size.y);
            GearLookSpec spec = gear.Spec;
            if (gear.Slot == EquipmentSlots.Accessory)
            {
                // Upright bodies wear it high (Socket_Head), low wide ones mid-body (Socket_Core_FX); hung just proud of the
                // front surface because the bounds cannot tell where the placeholder's skin is.
                bool upright = body.size.y >= body.size.z * 0.8f;
                float y = upright ? body.max.y - height * 0.3f : body.center.y;
                Vector3 socket = new Vector3(body.center.x, y, body.max.z + 0.02f);
                Mount(spec, mat, socket, Quaternion.identity, Vector3.one * Mathf.Clamp(height / 0.8f, 0.6f, 4f), null);
                return;
            }

            // The hand is sized to the body so a weapon reads at any creature scale.
            float scale = Mathf.Clamp(height, 0.6f, 2.5f);
            float side = gear.Slot == EquipmentSlots.MainHand ? 1f : -1f;
            var hand = new Vector3(body.center.x + side * (body.extents.x + 0.22f * scale), body.center.y, body.center.z + 0.1f * scale);
            AddVirtualHand(hand, side, scale);
            Quaternion grip = spec.Kind == GearKind.Shield ? Quaternion.Euler(0f, side * 25f, 0f) : Quaternion.Euler(55f, 0f, -side * 10f);
            Mount(spec, mat, hand, grip, Vector3.one * scale, null);
        }

        private void AddVirtualHand(Vector3 at, float side, float scale)
        {
            // Pale placeholder glove so the floating weapon reads as held, not dropped.
            Material skin = CreateMaterial(new Color(0.9f, 0.92f, 0.98f));
            _materials.Remove(skin);
            _gearMaterials.Add(skin);
            GameObject palm = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            palm.name = side > 0f ? "VirtualHand_R" : "VirtualHand_L";
            palm.transform.SetParent(_gearRoot, true);
            palm.transform.position = at;
            palm.transform.localScale = WorldToLocal(new Vector3(0.16f, 0.13f, 0.11f) * scale);
            StripColliders(palm);
            palm.GetComponent<Renderer>().sharedMaterial = skin;
            GameObject thumb = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            thumb.transform.SetParent(palm.transform, false);
            thumb.transform.localPosition = new Vector3(-side * 0.45f, 0.25f, 0.35f);
            thumb.transform.localRotation = Quaternion.Euler(40f, 0f, side * 35f);
            thumb.transform.localScale = new Vector3(0.35f, 0.4f, 0.35f);
            StripColliders(thumb);
            thumb.GetComponent<Renderer>().sharedMaterial = skin;
        }

        private void Mount(GearLookSpec spec, Material mat, Vector3 at, Quaternion rotation, Vector3 scale, Transform follow)
        {
            Mesh mesh = GearMeshes.Build(spec);
            _gearMeshes.Add(mesh);
            var go = new GameObject(mesh.name);
            go.transform.SetParent(_gearRoot, false);
            go.transform.position = at;
            go.transform.rotation = _model.transform.rotation * rotation;
            // Scales are world sizes: creature view roots are scaled by body class, which gear must not inherit.
            go.transform.localScale = WorldToLocal(scale);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            // Follow the bone so idle animation carries the gear, keeping the upright pose set above.
            if (follow != null) go.transform.SetParent(follow, true);
        }

        private Material GearMaterial()
        {
            // Gear colours are baked into vertices, which only the scenery toon shader reads.
            Material mat = _sceneryShader != null
                ? PlanetLook.SceneryMaterial(_sceneryShader, Color.white, Color.white, 0f, 0f, 0f, 0f)
                : CreateMaterial(Color.white);
            _materials.Remove(mat);
            _gearMaterials.Add(mat);
            return mat;
        }

        private Vector3 WorldToLocal(Vector3 worldScale)
        {
            Vector3 parent = _gearRoot.lossyScale;
            return new Vector3(worldScale.x / parent.x, worldScale.y / parent.y, worldScale.z / parent.z);
        }

        private void ClearGear()
        {
            if (_gearRoot != null)
            {
                DestroyObject(_gearRoot.gameObject);
                _gearRoot = null;
            }
            // Bone-followed gear lives under the rig, not the gear root.
            if (_model != null)
            {
                foreach (MeshFilter filter in _model.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (_gearMeshes.Contains(filter.sharedMesh)) DestroyObject(filter.gameObject);
                }
            }
            for (int i = 0; i < _gearMeshes.Count; i++) DestroyObject(_gearMeshes[i]);
            _gearMeshes.Clear();
            for (int i = 0; i < _gearMaterials.Count; i++) DestroyObject(_gearMaterials[i]);
            _gearMaterials.Clear();
        }

        private Material CreateMaterial(Color color)
        {
            Shader shader = _toonShader != null ? _toonShader : Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            var mat = new Material(shader) { color = color };
            _materials.Add(mat);
            return mat;
        }

        private static Bounds ComputeBounds(GameObject root)
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

        private static void StripColliders(GameObject root)
        {
            Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                DestroyObject(colliders[i]);
            }
        }

        private void ClearModel()
        {
            ClearGear();
            if (_creatureView != null)
            {
                _creatureView.Dispose();
                _creatureView = null;
                _model = null;
            }
            else if (_model != null)
            {
                DestroyObject(_model);
                _model = null;
            }
            for (int i = 0; i < _materials.Count; i++)
            {
                if (_materials[i] != null) DestroyObject(_materials[i]);
            }
            _materials.Clear();
        }

        private static void DestroyObject(UnityEngine.Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(obj);
            else UnityEngine.Object.DestroyImmediate(obj);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            ClearModel();
            if (_camera != null) _camera.targetTexture = null;
            if (_rt != null)
            {
                _rt.Release();
                DestroyObject(_rt);
            }
            DestroyObject(_stageRoot);
        }
    }
}
