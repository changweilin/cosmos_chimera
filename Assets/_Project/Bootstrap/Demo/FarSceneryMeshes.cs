using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using UnityEngine;

namespace Cosmimera.Bootstrap.Demo
{
    /// <summary>Low-polygon silhouette a scatter item is drawn with beyond the near field.</summary>
    /// <remarks>Pre: none. Post: stable values. Side effects: none.</remarks>
    public enum FarShape
    {
        /// <summary>Not drawn in the distance (small rocks, logs, undergrowth).</summary>
        None = -1,
        Conifer = 0,
        Broadleaf = 1,
        Umbrella = 2,
        Snag = 3,
        Mushroom = 4,

        /// <summary>Upright column: cactus, basalt columns.</summary>
        Column = 5,
        Spire = 6,
        Boulder = 7,
        Crystal = 8,
    }

    /// <summary>
    /// Silhouette meshes of the far scenery layer (P1-15): 20–50 triangles per item, merged many to a mesh.
    /// REQ: vision.md#美術投資 第 5 項 (遠景 LOD／impostor); phase-1.md P1-15 (90 m 外仍看得到樹與建築輪廓).
    /// </summary>
    /// <remarks>
    /// Real geometry rather than camera-facing cards: on a small planet the camera sees distant trees from above as
    /// often as from the side, and flat colour under fog needs no baked texture to read as the same tree.
    /// Proportions follow the rule layer's placeholder volumes (ScatterItem.VolumeL), like the near meshes do.
    /// Each shape is unit height (or unit width for boulders), standing on y = 0, +Y up.
    /// Pre: main thread not required; shapes are built once. Post: pure. Side effects: none after type initialisation.
    /// </remarks>
    public static class FarSceneryMeshes
    {
        private sealed class Template
        {
            public readonly List<Vector3> Positions = new List<Vector3>();
            public readonly List<Vector3> Normals = new List<Vector3>();
            // 0 = accent (trunk, stalk); otherwise body, blended toward the top colour by this share (1..255 → 0..1).
            public readonly List<byte> Paint = new List<byte>();
            public readonly List<int> Triangles = new List<int>();
        }

        private const int Sides = 5;
        private static readonly Template[] Templates = BuildTemplates();

        /// <summary>Number of vertices <see cref="Append"/> adds for <paramref name="shape"/>.</summary>
        /// <remarks>Pre: shape is not None. Post: &gt; 0. Side effects: none.</remarks>
        public static int VertexCount(FarShape shape)
        {
            Contract.Requires(shape > FarShape.None && (int)shape < Templates.Length, "shape must be a drawable far shape");
            return Templates[(int)shape].Positions.Count;
        }

        /// <summary>Appends one item's silhouette to mesh buffers.</summary>
        /// <remarks>
        /// Pre: shape is not None; all lists non-null and (the four vertex lists) of equal length; scale components &gt; 0.
        /// Post: VertexCount(shape) vertices appended to each vertex list, their triangles to triangles; every new
        ///       anchor equals origin (the point the far layer's shader shrinks the item toward); colours are accent on
        ///       the trunk and body shading into top on upward faces, alpha 255 (ToonScenery: no accent mask).
        /// Side effects: mutates the lists.
        /// </remarks>
        public static void Append(FarShape shape, List<Vector3> vertices, List<Vector3> normals, List<Color32> colors,
            List<Vector3> anchors, List<int> triangles, Vector3 origin, Quaternion rotation, Vector3 scale, Color body,
            Color accent, Color top)
        {
            Contract.Requires(shape > FarShape.None && (int)shape < Templates.Length, "shape must be a drawable far shape");
            Contract.Requires(vertices != null && normals != null && colors != null && anchors != null && triangles != null,
                "buffers are required");
            Contract.Requires(vertices.Count == normals.Count && vertices.Count == colors.Count && vertices.Count == anchors.Count,
                "vertex buffers must be the same length");
            Contract.Requires(scale.x > 0f && scale.y > 0f && scale.z > 0f, "scale must be positive");
            Template t = Templates[(int)shape];
            int first = vertices.Count;
            var inverse = new Vector3(1f / scale.x, 1f / scale.y, 1f / scale.z);
            for (int i = 0; i < t.Positions.Count; i++)
            {
                vertices.Add(origin + rotation * Vector3.Scale(t.Positions[i], scale));
                // Normals transform by the inverse scale, or a squashed crown would be lit like a sphere.
                normals.Add(rotation * Vector3.Scale(t.Normals[i], inverse).normalized);
                byte paint = t.Paint[i];
                Color c = paint == 0 ? accent : Color.Lerp(body, top, paint / 255f);
                c.a = 1f;
                colors.Add(c);
                anchors.Add(origin);
            }

            foreach (int index in t.Triangles)
            {
                triangles.Add(first + index);
            }
        }

        private static Template[] BuildTemplates()
        {
            var all = new Template[9];

            // Trunk r 0.06, crown from 0.22 up: two stacked cones read as tiers from any angle.
            var conifer = new Template();
            Prism(conifer, 0.06f, 0.05f, 0f, 0.3f, 0);
            Prism(conifer, 0.3f, 0f, 0.22f, 0.68f, 40);
            Prism(conifer, 0.21f, 0f, 0.5f, 1f, 110);
            all[(int)FarShape.Conifer] = conifer;

            // Trunk r 0.06 h 0.5; crown ellipsoid semi-axes 0.3, 0.275, 0.3 (ScatterItem.VolumeL).
            var broadleaf = new Template();
            Prism(broadleaf, 0.07f, 0.05f, 0f, 0.5f, 0);
            Ellipsoid(broadleaf, 0.7f, 0.3f, 0.275f, 0.3f);
            all[(int)FarShape.Broadleaf] = broadleaf;

            var umbrella = new Template();
            Prism(umbrella, 0.06f, 0.04f, 0f, 0.8f, 0);
            Ellipsoid(umbrella, 0.86f, 0.46f, 0.13f, 0.46f);
            all[(int)FarShape.Umbrella] = umbrella;

            var snag = new Template();
            Prism(snag, 0.07f, 0.025f, 0f, 1f, 0);
            all[(int)FarShape.Snag] = snag;

            // Stalk r 0.08 h 0.7; cap ellipsoid semi-axes 0.35, 0.12, 0.35.
            var mushroom = new Template();
            Prism(mushroom, 0.08f, 0.07f, 0f, 0.74f, 0);
            Ellipsoid(mushroom, 0.8f, 0.35f, 0.14f, 0.35f);
            all[(int)FarShape.Mushroom] = mushroom;

            var column = new Template();
            Prism(column, 0.14f, 0.12f, 0f, 0.95f, 30);
            Prism(column, 0.12f, 0f, 0.95f, 1f, 150);
            all[(int)FarShape.Column] = column;

            // Blunt cone r 0.22.
            var spire = new Template();
            Prism(spire, 0.22f, 0.07f, 0f, 0.92f, 20);
            Prism(spire, 0.07f, 0f, 0.92f, 1f, 150);
            all[(int)FarShape.Spire] = spire;

            // Ellipsoid semi-axes 0.5, 0.325, 0.425, a third of it buried.
            var boulder = new Template();
            Ellipsoid(boulder, 0.22f, 0.5f, 0.325f, 0.425f);
            all[(int)FarShape.Boulder] = boulder;

            var crystal = new Template();
            Prism(crystal, 0.1f, 0.14f, 0f, 0.7f, 60);
            Prism(crystal, 0.14f, 0f, 0.7f, 1f, 200);
            all[(int)FarShape.Crystal] = crystal;
            return all;
        }

        // Open frustum (a cone when r1 == 0) with flat-shaded sides: a facet per side keeps the silhouette crisp.
        private static void Prism(Template t, float r0, float r1, float y0, float y1, byte paint)
        {
            for (int s = 0; s < Sides; s++)
            {
                float a0 = s * Mathf.PI * 2f / Sides;
                float a1 = (s + 1) * Mathf.PI * 2f / Sides;
                var d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0));
                var d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                Vector3 b0 = d0 * r0 + Vector3.up * y0;
                Vector3 b1 = d1 * r0 + Vector3.up * y0;
                Vector3 c0 = d0 * r1 + Vector3.up * y1;
                Vector3 c1 = d1 * r1 + Vector3.up * y1;
                Vector3 normal = Vector3.Cross(c0 - b0, b1 - b0).normalized;
                int first = t.Positions.Count;
                t.Positions.Add(b0);
                t.Positions.Add(b1);
                t.Positions.Add(c0);
                t.Triangles.Add(first);
                t.Triangles.Add(first + 2);
                t.Triangles.Add(first + 1);
                int corners = 3;
                if (r1 > 0f)
                {
                    t.Positions.Add(c1);
                    t.Triangles.Add(first + 1);
                    t.Triangles.Add(first + 2);
                    t.Triangles.Add(first + 3);
                    corners = 4;
                }

                for (int k = 0; k < corners; k++)
                {
                    t.Normals.Add(normal);
                    t.Paint.Add(paint);
                }
            }
        }

        // Octahedron subdivided once (18 vertices, 32 triangles), smooth normals: a soft blob under the cel ramp.
        private static void Ellipsoid(Template t, float centreY, float rx, float ry, float rz)
        {
            var unit = new List<Vector3>
            {
                Vector3.up, Vector3.down, Vector3.right, Vector3.forward, Vector3.left, Vector3.back,
            };
            var faces = new List<int>();
            var midpoints = new Dictionary<(int, int), int>();

            int Mid(int a, int b)
            {
                (int, int) key = a < b ? (a, b) : (b, a);
                if (!midpoints.TryGetValue(key, out int m))
                {
                    m = unit.Count;
                    unit.Add((unit[a] + unit[b]).normalized);
                    midpoints[key] = m;
                }

                return m;
            }

            for (int s = 0; s < 4; s++)
            {
                int a = 2 + s;
                int b = 2 + (s + 1) % 4;
                foreach ((int p, int q, int r) in new[] { (0, b, a), (1, a, b) })
                {
                    int pq = Mid(p, q), qr = Mid(q, r), rp = Mid(r, p);
                    faces.AddRange(new[] { p, pq, rp, q, qr, pq, r, rp, qr, pq, qr, rp });
                }
            }

            int first = t.Positions.Count;
            foreach (Vector3 u in unit)
            {
                t.Positions.Add(new Vector3(u.x * rx, centreY + u.y * ry, u.z * rz));
                t.Normals.Add(new Vector3(u.x / rx, u.y / ry, u.z / rz).normalized);
                // Sunlit top tone on upward faces, as the near meshes' accent mask does; never 0 (that means trunk).
                t.Paint.Add((byte)(1 + Mathf.RoundToInt(Mathf.Clamp01(u.y) * 150f)));
            }

            foreach (int index in faces)
            {
                t.Triangles.Add(first + index);
            }
        }
    }
}
