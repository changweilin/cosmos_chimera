using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace Cosmimera.Bootstrap.Demo
{
    internal sealed class FieldSpecimenView : MonoBehaviour
    {
        private IReadOnlyList<FieldSpecimen> _items;
        private GameObject[] _roots;
        private readonly Dictionary<int, GameObject> _tracks = new Dictionary<int, GameObject>();
        private readonly List<Mesh> _meshes = new List<Mesh>();
        private readonly List<(Transform root, Transform part, Vector3 axis, float speed, bool wing, int phase)> _motion
            = new List<(Transform, Transform, Vector3, float, bool, int)>();
        private Material _material;
        private bool _initialized;

        internal void Configure(IReadOnlyList<FieldSpecimen> items, Shader shader)
        {
            _items = items;
            _material = PlanetLook.SceneryMaterial(shader, Color.white, Color.white, 0, 0, 0, 0);
            _roots = new GameObject[items.Count];
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                var root = new GameObject(item.Form.ToString());
                root.transform.SetParent(transform, false);
                _roots[i] = root;
                var builder = new FieldMeshes(item);
                Mesh body = builder.Build(true);
                Draw(root, body);
                Bounds bounds = body.bounds;
                if (item.Form == FieldForm.WindPump)
                    Attach(root, root.transform, new FieldMeshes(item).BuildMovingPart(), builder.RotorPivot, Vector3.forward, 65f, false, item.Style, ref bounds);
                if (item.Form == FieldForm.Heron)
                    for (int side = -1; side <= 1; side += 2)
                        Attach(root, root.transform, new FieldMeshes(item).BuildMovingPart(side), new Vector3(side * 0.16f,0.43f,0),
                            Vector3.forward, side * 25f, true, item.Style, ref bounds);
                if (item.Form == FieldForm.Wagon || item.Form == FieldForm.MineCart || item.Form == FieldForm.SteamCar)
                {
                    Mesh wheel = new FieldMeshes(item).BuildMovingPart();
                    for (int side = -1; side <= 1; side += 2)
                        for (int axle = -1; axle <= 1; axle += 2)
                            Attach(root, root.transform, wheel, new Vector3(side * 0.6496f,0.35f,axle * 0.73f), Vector3.right, 340f / item.Scale,
                                false, item.Style, ref bounds);
                }
                if (item.Form == FieldForm.MineCart) Track(i, item);
                root.transform.localScale = Vector3.one * item.Scale;
                if (item.Solid)
                {
                    var collider = root.AddComponent<BoxCollider>();
                    collider.center = bounds.center;
                    collider.size = bounds.size;
                }
            }
            Sync(0);
        }

        private void Draw(GameObject root, Mesh mesh)
        {
            if (!_meshes.Contains(mesh)) _meshes.Add(mesh);
            root.AddComponent<MeshFilter>().sharedMesh = mesh;
            root.AddComponent<MeshRenderer>().sharedMaterial = _material;
        }

        private void Attach(GameObject root, Transform owner, Mesh mesh, Vector3 pivot, Vector3 axis, float speed, bool wing, int phase, ref Bounds bounds)
        {
            var part = new GameObject(wing ? "Wing" : "Rotor");
            part.transform.SetParent(root.transform, false);
            part.transform.localPosition = pivot;
            Draw(part, mesh);
            _motion.Add((owner, part.transform, axis, speed, wing, phase));
            bounds.Encapsulate(pivot + mesh.bounds.min);
            bounds.Encapsulate(pivot + mesh.bounds.max);
        }

        // Rails are scenery, not part of the destructible cart: they outlive it and never block (spec: carts block, track does not).
        private void Track(int index, FieldSpecimen item)
        {
            Mesh mesh = new FieldMeshes(item).BuildTrack(out float3 origin);
            var track = new GameObject("Track");
            track.transform.SetParent(transform, false);
            track.transform.SetPositionAndRotation((Vector3)origin, Quaternion.identity);
            Draw(track, mesh);
            _tracks[index] = track;
        }

        internal GameObject Root(int index) => _roots[index];
        internal GameObject TrackOf(int index) => _tracks.TryGetValue(index, out var track) ? track : null;

        internal void Sync(long nowMs) => Sync(nowMs, float3.zero, float.PositiveInfinity);

        internal void Sync(long nowMs, float3 focus, float motionCullM)
        {
            bool cullMotion = float.IsFinite(motionCullM);
            float motionCullSq = motionCullM * motionCullM;
            var focusV = new Vector3(focus.x, focus.y, focus.z);
            foreach (var m in _motion)
            {
                if (!m.part) continue;
                if (cullMotion && (m.root.position - focusV).sqrMagnitude > motionCullSq) continue;
                float angle = m.wing ? m.speed * math.sin((nowMs % 1000) * (2f * math.PI / 1000f) + m.phase) :
                    (float)((nowMs * 0.001 * m.speed + m.phase) % 360.0);
                m.part.localRotation = Quaternion.AngleAxis(angle, m.axis);
            }
            for (int i = 0; i < _items.Count; i++)
            {
                if (!_roots[i]) continue;
                var item = _items[i];
                if (_initialized && item.Form <= FieldForm.Hoist) continue;
                float phase = (nowMs % 4000) * (math.PI / 2000f) + item.Style;
                float bob = item.Form == FieldForm.Hare ? 0.12f * math.abs(math.sin(phase * 3)) :
                    item.Form == FieldForm.Heron || item.Form == FieldForm.Balloon ? 0.08f * math.sin(phase) : 0f;
                _roots[i].transform.SetPositionAndRotation((Vector3)(item.Position + item.Direction * bob),
                    Quaternion.LookRotation(item.Facing, item.Direction));
            }
            _initialized = true;
        }

        private void OnDestroy()
        {
            if (_meshes != null) foreach (var mesh in _meshes) if (mesh) Destroy(mesh);
            if (_material) Destroy(_material);
        }
    }
}
