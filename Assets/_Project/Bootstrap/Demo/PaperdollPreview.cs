using System;
using System.Collections.Generic;
using Cosmimera.GameData;
using Cosmimera.WorldGen;
using UnityEngine;
using UnityEngine.Rendering;

namespace Cosmimera.Bootstrap.Demo
{
    /// <summary>
    /// Isolated 3D RenderTexture stage for the lobby paperdoll preview.
    /// Displays the selected player character or creature with procedural gear attachments.
    /// </summary>
    /// <remarks>
    /// Pre: created on the main thread; toonShader may be null (falls back to URP Lit).
    /// Post: Dispose releases the RenderTexture, stage GameObjects and transient Materials.
    /// Side effects: allocates offscreen RenderTexture, Camera, Light and preview GameObjects.
    /// </remarks>
    internal sealed class PaperdollPreview : IDisposable
    {
        private const int PreviewWidth = 320;
        private const int PreviewHeight = 440;
        private static readonly Vector3 StageOrigin = new Vector3(0f, -500f, 0f);

        private readonly Shader _toonShader;
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
        private float _yaw = 180f;
        private bool _disposed;

        internal Texture Texture => _rt;
        internal float Yaw => _yaw;

        internal PaperdollPreview(Shader toonShader)
        {
            _toonShader = toonShader;
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
            _camera.enabled = false;
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
            string weaponId, string armorId, string accessoryId)
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

            SyncGear(weaponId, armorId, accessoryId, isCreature: false);
        }

        internal void SetCreatureTarget(CreatureDef def, int instanceId,
            string weaponId, string armorId, string accessoryId)
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

            SyncGear(weaponId, armorId, accessoryId, isCreature: true);
        }

        internal void Render()
        {
            if (_disposed || _camera == null || _rt == null) return;
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;
            _pivot.localRotation = Quaternion.Euler(0f, _yaw, 0f);
            _camera.Render();
        }

        private void FrameModel(bool isCreature)
        {
            if (_model == null || _camera == null) return;
            Bounds bounds = ComputeBounds(_model);
            float height = Mathf.Max(0.8f, bounds.size.y);
            float centerY = bounds.center.y - _stageRoot.transform.position.y;
            float dist = isCreature ? Mathf.Max(2.2f, height * 2.5f) : Mathf.Max(2.6f, height * 2.0f);
            _camera.transform.localPosition = new Vector3(0f, centerY + height * 0.1f, dist);
            _camera.transform.LookAt(_stageRoot.transform.position + new Vector3(0f, centerY, 0f));
        }

        private void SyncGear(string weaponId, string armorId, string accessoryId, bool isCreature)
        {
            string nextGear = (weaponId ?? "") + "|" + (armorId ?? "") + "|" + (accessoryId ?? "") + "|" + (isCreature ? "c" : "p");
            if (_gearKey == nextGear || _model == null) return;
            _gearKey = nextGear;

            if (_gearRoot != null)
            {
                DestroyObject(_gearRoot.gameObject);
                _gearRoot = null;
            }

            var rootGo = new GameObject("PaperdollGear");
            rootGo.transform.SetParent(_model.transform, false);
            _gearRoot = rootGo.transform;

            Animator anim = isCreature ? null : _model.GetComponentInChildren<Animator>();

            if (!string.IsNullOrEmpty(weaponId))
            {
                Transform hand = anim != null && anim.isHuman ? anim.GetBoneTransform(HumanBodyBones.RightHand) : null;
                AttachWeapon(hand, weaponId, isCreature);
            }

            if (!string.IsNullOrEmpty(armorId))
            {
                Transform chest = anim != null && anim.isHuman ? anim.GetBoneTransform(HumanBodyBones.Chest) : null;
                AttachArmor(chest, armorId, isCreature);
            }

            if (!string.IsNullOrEmpty(accessoryId))
            {
                Transform neck = anim != null && anim.isHuman
                    ? (anim.GetBoneTransform(HumanBodyBones.Neck) ?? anim.GetBoneTransform(HumanBodyBones.Head))
                    : null;
                AttachAccessory(neck, accessoryId, isCreature);
            }
        }

        private void AttachWeapon(Transform bone, string itemId, bool isCreature)
        {
            var holder = new GameObject("WeaponVisual");
            if (bone != null)
            {
                holder.transform.SetParent(bone, false);
                holder.transform.localPosition = new Vector3(0f, 0.04f, 0.04f);
                holder.transform.localRotation = Quaternion.Euler(75f, 0f, 0f);
            }
            else
            {
                holder.transform.SetParent(_gearRoot, false);
                holder.transform.localPosition = isCreature ? new Vector3(0.42f, 0.55f, 0.25f) : new Vector3(0.42f, 0.92f, 0.16f);
                holder.transform.localRotation = Quaternion.Euler(20f, 0f, -12f);
            }

            Color bladeColor = itemId.Contains("copper") ? new Color(0.88f, 0.54f, 0.26f)
                : itemId.Contains("iron") ? new Color(0.76f, 0.84f, 0.94f)
                : new Color(0.62f, 0.72f, 0.85f);

            GameObject blade = GameObject.CreatePrimitive(PrimitiveType.Cube);
            blade.transform.SetParent(holder.transform, false);
            blade.transform.localPosition = new Vector3(0f, 0.28f, 0f);
            blade.transform.localScale = new Vector3(0.06f, 0.58f, 0.03f);
            StripColliders(blade);
            blade.GetComponent<Renderer>().sharedMaterial = CreateMaterial(bladeColor);

            GameObject guard = GameObject.CreatePrimitive(PrimitiveType.Cube);
            guard.transform.SetParent(holder.transform, false);
            guard.transform.localPosition = new Vector3(0f, 0.02f, 0f);
            guard.transform.localScale = new Vector3(0.16f, 0.04f, 0.06f);
            StripColliders(guard);
            guard.GetComponent<Renderer>().sharedMaterial = CreateMaterial(new Color(0.32f, 0.28f, 0.24f));
        }

        private void AttachArmor(Transform bone, string itemId, bool isCreature)
        {
            var holder = new GameObject("ArmorVisual");
            if (bone != null)
            {
                holder.transform.SetParent(bone, false);
                holder.transform.localPosition = new Vector3(0f, 0.06f, 0f);
            }
            else
            {
                holder.transform.SetParent(_gearRoot, false);
                holder.transform.localPosition = isCreature ? new Vector3(0f, 0.52f, 0f) : new Vector3(0f, 1.08f, 0f);
            }

            Color armorColor = itemId.Contains("linen") ? new Color(0.88f, 0.84f, 0.72f)
                : itemId.Contains("iron") ? new Color(0.46f, 0.58f, 0.72f)
                : new Color(0.55f, 0.52f, 0.48f);

            Material mat = CreateMaterial(armorColor);
            for (int side = -1; side <= 1; side += 2)
            {
                GameObject pauldron = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                pauldron.transform.SetParent(holder.transform, false);
                pauldron.transform.localPosition = new Vector3(side * 0.22f, 0.08f, 0f);
                pauldron.transform.localScale = new Vector3(0.16f, 0.12f, 0.16f);
                StripColliders(pauldron);
                pauldron.GetComponent<Renderer>().sharedMaterial = mat;
            }
        }

        private void AttachAccessory(Transform bone, string itemId, bool isCreature)
        {
            var holder = new GameObject("AccessoryVisual");
            if (bone != null)
            {
                holder.transform.SetParent(bone, false);
                holder.transform.localPosition = new Vector3(0f, 0.02f, 0.12f);
            }
            else
            {
                holder.transform.SetParent(_gearRoot, false);
                holder.transform.localPosition = isCreature ? new Vector3(0f, 0.78f, 0.32f) : new Vector3(0f, 1.32f, 0.16f);
            }

            Color gemColor = itemId.Contains("gem") ? new Color(0.25f, 0.92f, 0.78f) : new Color(0.95f, 0.82f, 0.32f);
            GameObject gem = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            gem.transform.SetParent(holder.transform, false);
            gem.transform.localScale = new Vector3(0.1f, 0.12f, 0.06f);
            StripColliders(gem);
            gem.GetComponent<Renderer>().sharedMaterial = CreateMaterial(gemColor);
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
            if (_gearRoot != null)
            {
                DestroyObject(_gearRoot.gameObject);
                _gearRoot = null;
            }
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
