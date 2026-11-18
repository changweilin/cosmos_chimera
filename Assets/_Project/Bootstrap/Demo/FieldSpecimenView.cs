using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace Cosmimera.Bootstrap.Demo
{
    internal sealed class FieldSpecimenView : MonoBehaviour
    {
        private IReadOnlyList<FieldSpecimen> _items;
        private GameObject[] _roots;
        private readonly List<Mesh> _meshes = new List<Mesh>();
        private readonly List<(Transform part, Vector3 axis, float speed, bool wing, int phase)> _motion = new List<(Transform,Vector3,float,bool,int)>();
        private Material _material;

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
                    Attach(root, new FieldMeshes(item).BuildMovingPart(), builder.RotorPivot, Vector3.forward, 65f, false, item.Style, ref bounds);
                if (item.Form == FieldForm.Heron)
                    for (int side = -1; side <= 1; side += 2)
                        Attach(root, new FieldMeshes(item).BuildMovingPart(side), new Vector3(side * 0.16f,0.43f,0),
                            Vector3.forward, side * 25f, true, item.Style, ref bounds);
                if (item.Form == FieldForm.Wagon || item.Form == FieldForm.MineCart || item.Form == FieldForm.SteamCar)
                {
                    Mesh wheel = new FieldMeshes(item).BuildMovingPart();
                    for (int side = -1; side <= 1; side += 2)
                        for (int axle = -1; axle <= 1; axle += 2)
                            Attach(root, wheel, new Vector3(side * 0.6496f,0.35f,axle * 0.73f), Vector3.right, 340f / item.Scale,
                                false, item.Style, ref bounds);
                }
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

        private void Attach(GameObject root, Mesh mesh, Vector3 pivot, Vector3 axis, float speed, bool wing, int phase, ref Bounds bounds)
        {
            var part = new GameObject(wing ? "Wing" : "Rotor");
            part.transform.SetParent(root.transform, false);
            part.transform.localPosition = pivot;
            Draw(part, mesh);
            _motion.Add((part.transform, axis, speed, wing, phase));
            bounds.Encapsulate(pivot + mesh.bounds.min);
            bounds.Encapsulate(pivot + mesh.bounds.max);
        }

        internal GameObject Root(int index) => _roots[index];

        internal void Sync(long nowMs)
        {
            foreach (var m in _motion)
            {
                if (!m.part) continue;
                float angle = m.wing ? m.speed * math.sin((nowMs % 1000) * (2f * math.PI / 1000f) + m.phase) :
                    (float)((nowMs * 0.001 * m.speed + m.phase) % 360.0);
                m.part.localRotation = Quaternion.AngleAxis(angle, m.axis);
            }
            for (int i = 0; i < _items.Count; i++)
            {
                if (!_roots[i]) continue;
                var item = _items[i];
                float phase = (nowMs % 4000) * (math.PI / 2000f) + item.Style;
                float bob = item.Form == FieldForm.Hare ? 0.12f * math.abs(math.sin(phase * 3)) :
                    item.Form == FieldForm.Heron || item.Form == FieldForm.Balloon ? 0.08f * math.sin(phase) : 0f;
                _roots[i].transform.SetPositionAndRotation((Vector3)(item.Position + item.Direction * bob),
                    Quaternion.LookRotation(item.Facing, item.Direction));
            }
        }

        private void OnDestroy()
        {
            if (_meshes != null) foreach (var mesh in _meshes) if (mesh) Destroy(mesh);
            if (_material) Destroy(_material);
        }
    }
}
