using System;
using Cosmimera.Core.Contracts;
using Cosmimera.Core.Random;
using Unity.Mathematics;
using UnityEngine;

namespace Cosmimera.Bootstrap.Demo
{
    /// <summary>Silhouette family of a small procedural prop.</summary>
    /// <remarks>Pre: none. Post: stable values (they seed the mesh streams). Side effects: none.</remarks>
    public enum PropShape
    {
        /// <summary>Splintered chunk a destroyed object breaks into.</summary>
        Shard = 0,

        /// <summary>Rounded lump of raw material lying on the ground.</summary>
        Nugget = 1,

        /// <summary>Cut gemstone.</summary>
        Gem = 2,

        /// <summary>Six-pointed spark of a floating light or shadow material.</summary>
        Wisp = 3,

        /// <summary>Banded treasure chest with a barrel lid.</summary>
        Chest = 4,

        /// <summary>Thicket of crystal spires plugging a dungeon gate.</summary>
        Seal = 5,
    }

    /// <summary>
    /// Asset-free meshes for the small things a run leaves around: debris, ground materials, gems, treasure chests and
    /// the boss-arena seal. Companion of <see cref="SceneryMeshes"/> and drawn by the same shader (Cosmimera/ToonScenery:
    /// colour rgb = tint, a = 1 − accent mask; UV1 unused, so nothing here moves in the wind).
    /// REQ: vision.md#畫風與沉浸感; destructibles.md#破壞與掉落, #地上的素材; planet-world.md#地表地牢.
    /// </summary>
    /// <remarks>
    /// Shard, Nugget, Gem and Wisp are centred on the origin and exactly fill a unit cube along their longest axis, so
    /// a unit BoxCollider and a uniform scale of the old placeholder's edge keep mass and collision as they were.
    /// Chest stands on y = 0 (with a skirt below it) and is one unit wide. Seal fills a cylinder of radius 1 from
    /// y = 0 to y = 1 and is meant to be scaled to the plug it draws.
    /// Pre: main thread (creates Unity objects). Post: results depend only on (shape, variant). Side effects: allocates.
    /// </remarks>
    public static class PropMeshes
    {
        private const ulong Salt = 0x9E0B5A11ul;
        private const float FacetCos = 0.8f; // same crease angle as SceneryMeshes: cut faces flat, rounded stretches round

        private static readonly Color Wood = new Color(0.4f, 0.24f, 0.13f, 1f);
        private static readonly Color WoodDark = new Color(0.26f, 0.15f, 0.09f, 1f);
        private static readonly Color Band = new Color(0.92f, 0.64f, 0.18f, 1f);
        // a = 0: fully the material's accent colour, so the clasp is the one spot that takes the glow tint.
        private static readonly Color Clasp = new Color(1f, 1f, 1f, 0f);

        /// <summary>Number of distinct meshes of <paramref name="shape"/>.</summary>
        /// <remarks>Pre: defined shape. Post: ≥ 1, constant per shape. Side effects: none.</remarks>
        public static int VariantCount(PropShape shape) => shape switch
        {
            PropShape.Shard => 5,
            PropShape.Nugget => 3,
            PropShape.Gem => 3,
            PropShape.Wisp => 1,
            PropShape.Chest => 3,
            PropShape.Seal => 2,
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };

        /// <summary>Builds variant <paramref name="variant"/> of <paramref name="shape"/>.</summary>
        /// <remarks>
        /// Pre: 0 ≤ variant &lt; VariantCount(shape); main thread.
        /// Post: a new single-sub-mesh mesh with normals, colours and UV1 (all zero); the same arguments always give
        ///       the same vertices; extents as described on the class.
        /// Side effects: allocates a Mesh the caller owns (and must destroy).
        /// </remarks>
        public static Mesh Build(PropShape shape, int variant)
        {
            Contract.Requires(variant >= 0 && variant < VariantCount(shape), "variant out of range");
            var rng = new Pcg32(Salt ^ ((ulong)shape << 32), (ulong)variant);
            var data = new SceneryMeshes.MeshData();
            switch (shape)
            {
                case PropShape.Shard:
                    // Twelve corners and deep cuts: a splinter, not a pebble.
                    Lump(data, Shuffled(new float3(0.5f, 0.24f + rng.NextFloat() * 0.1f, 0.33f + rng.NextFloat() * 0.1f), ref rng),
                        0, 3, 0.55f, 0f, ref rng);
                    break;
                case PropShape.Nugget:
                    Lump(data, new float3(0.5f, 0.36f + rng.NextFloat() * 0.08f, 0.42f + rng.NextFloat() * 0.08f),
                        1, 4, 0.72f, 1f, ref rng);
                    break;
                case PropShape.Gem:
                    BuildGem(data, ref rng);
                    break;
                case PropShape.Wisp:
                    BuildWisp(data);
                    break;
                case PropShape.Chest:
                    BuildChest(data, ref rng);
                    break;
                default:
                    BuildSeal(data, ref rng);
                    break;
            }

            return data.ToMesh("Prop_" + shape + "_" + variant);
        }

        // ---- loose pieces --------------------------------------------------------------------------------------

        // A noise-swollen icosphere cut by random planes (SceneryMeshes.Rock's recipe), then fitted to the unit cube.
        // Unlike a boulder it has no up: it tumbles, so there is no contact darkening and the accent is in veins.
        private static void Lump(SceneryMeshes.MeshData data, float3 radii, int detail, int cuts, float keepMin, float veins,
            ref Pcg32 rng)
        {
            SceneryMeshes.Ico ico = SceneryMeshes.Ico.Get(detail);
            var local = new float3[ico.Vertices.Length];
            float3 offset = new float3(rng.NextFloat(), rng.NextFloat(), rng.NextFloat()) * 53f;
            for (int i = 0; i < local.Length; i++)
            {
                float3 d = ico.Vertices[i];
                local[i] = d * radii * (1f + 0.22f * noise.snoise(d * 1.4f + offset));
            }

            for (int c = 0; c < cuts; c++)
            {
                float3 dir = math.normalize(new float3(Signed(ref rng), Signed(ref rng), Signed(ref rng)) + 1e-3f);
                float reach = 0f;
                foreach (float3 v in local)
                {
                    reach = math.max(reach, math.dot(v, dir));
                }

                float keep = reach * (keepMin + rng.NextFloat() * 0.2f);
                for (int i = 0; i < local.Length; i++)
                {
                    float over = math.dot(local[i], dir) - keep;
                    if (over > 0f)
                    {
                        local[i] -= dir * over;
                    }
                }
            }

            Fit(local);
            var part = new SceneryMeshes.Part();
            foreach (float3 v in local)
            {
                part.Add(v, Tint(1f, 1f), float2.zero);
            }

            for (int t = 0; t < ico.Triangles.Length; t += 3)
            {
                Face(part, ico.Triangles[t], ico.Triangles[t + 1], ico.Triangles[t + 2], float3.zero,
                    0.84f + rng.NextFloat() * 0.28f);
            }

            data.EmitFaceted(part, 0, FacetCos, (p, n, c) =>
            {
                float vein = noise.snoise(p * 3.1f + offset) * 0.5f + 0.5f;
                c.a = 1f - veins * math.smoothstep(0.52f, 0.66f, vein);
                return (n, c);
            });
        }

        // A brilliant cut: flat table, a ring of crown facets, and a pavilion drawn down to a point. Every facet gets
        // its own tone, which under a cel ramp is what makes it read as cut stone rather than a bead.
        private static void BuildGem(SceneryMeshes.MeshData data, ref Pcg32 rng)
        {
            const int sides = 8;
            float spin = rng.NextFloat() * math.PI;
            float crown = 0.15f + rng.NextFloat() * 0.08f;
            float table = 0.27f + rng.NextFloat() * 0.08f;
            float depth = 0.42f + rng.NextFloat() * 0.16f;
            float oval = 0.72f + rng.NextFloat() * 0.28f;
            var local = new float3[sides * 2 + 2];
            for (int k = 0; k < sides; k++)
            {
                float a = spin + k * math.PI * 2f / sides;
                local[k] = new float3(math.cos(a) * table, crown, math.sin(a) * table * oval);
                local[sides + k] = new float3(math.cos(a) * 0.5f, 0f, math.sin(a) * 0.5f * oval);
            }

            int culet = sides * 2, centre = sides * 2 + 1;
            local[culet] = new float3(0f, -depth, 0f);
            local[centre] = new float3(0f, crown, 0f);
            Fit(local);

            var part = new SceneryMeshes.Part();
            for (int i = 0; i < local.Length; i++)
            {
                bool top = i < sides || i == centre;
                part.Add(local[i], Tint(1f, top ? 0.35f : i == culet ? 1f : 0.8f), float2.zero);
            }

            for (int k = 0; k < sides; k++)
            {
                int next = (k + 1) % sides;
                float crownTone = (k & 1) == 0 ? 1.12f : 0.92f;
                float pavilionTone = ((k & 1) == 0 ? 0.74f : 0.9f) + rng.NextFloat() * 0.06f;
                Face(part, centre, k, next, float3.zero, 1.2f);
                Face(part, k, next, sides + k, float3.zero, crownTone);
                Face(part, next, sides + next, sides + k, float3.zero, crownTone);
                Face(part, sides + k, sides + next, culet, float3.zero, pavilionTone);
            }

            data.EmitFaceted(part, 0, 0.99f, null);
        }

        // Six spikes on a small core, the vertical pair longest: reads as a spark from any side, and its points keep
        // the silhouette alive where a sphere would be a flat disc under cel shading.
        private static void BuildWisp(SceneryMeshes.MeshData data)
        {
            var part = new SceneryMeshes.Part();
            const float core = 0.085f;
            float3[] tips =
            {
                new float3(0.3f, 0f, 0f), new float3(-0.3f, 0f, 0f), new float3(0f, 0.5f, 0f),
                new float3(0f, -0.5f, 0f), new float3(0f, 0f, 0.3f), new float3(0f, 0f, -0.3f),
            };
            for (int t = 0; t < tips.Length; t++)
            {
                float3 axis = math.normalize(tips[t]);
                // Two axes spanning the plane across the spike; the four core corners around it are axis ± u ± v.
                float3 u = math.abs(axis.y) > 0.5f ? new float3(1f, 0f, 0f) : new float3(0f, 1f, 0f);
                float3 v = math.cross(axis, u);
                int tip = part.Add(tips[t], Tint(1.25f, 0f), float2.zero);
                int first = part.P.Count;
                part.Add((axis + u + v) * core, Tint(0.8f, 1f), float2.zero);
                part.Add((axis + u - v) * core, Tint(0.8f, 1f), float2.zero);
                part.Add((axis - u - v) * core, Tint(0.8f, 1f), float2.zero);
                part.Add((axis - u + v) * core, Tint(0.8f, 1f), float2.zero);
                for (int k = 0; k < 4; k++)
                {
                    Face(part, tip, first + k, first + (k + 1) % 4, float3.zero, (k & 1) == 0 ? 1.08f : 0.9f);
                }
            }

            data.EmitFaceted(part, 0, 0.99f, null);
        }

        // ---- treasure chest ------------------------------------------------------------------------------------

        private static void BuildChest(SceneryMeshes.MeshData data, ref Pcg32 rng)
        {
            const float halfWidth = 0.5f;
            const float skirt = 0.08f;
            float halfDepth = 0.3f + rng.NextFloat() * 0.05f;
            float body = 0.4f + rng.NextFloat() * 0.1f;
            float arch = halfDepth * (0.72f + rng.NextFloat() * 0.28f);
            int bands = 2 + (int)rng.NextUInt(2);
            var part = new SceneryMeshes.Part();

            // Narrower at the foot than at the rim: the slight flare is what separates a chest from a crate.
            Box(part, new float3(0f, (body - skirt) * 0.5f, 0f), new float3(halfWidth, (body + skirt) * 0.5f, halfDepth), 0.92f,
                Wood, 1f);
            Barrel(part, 0f, halfWidth, body, arch, halfDepth, Wood, true);
            // A dark seam where the lid closes.
            Box(part, new float3(0f, body, 0f), new float3(halfWidth + 0.006f, 0.012f, halfDepth + 0.006f), 1f, WoodDark, 0.9f);

            for (int b = 0; b < bands; b++)
            {
                float x = bands == 2 ? (b == 0 ? -0.3f : 0.3f) : (b - 1) * 0.36f;
                Box(part, new float3(x, (body - skirt) * 0.5f, 0f), new float3(0.045f, (body + skirt) * 0.5f, halfDepth + 0.02f),
                    1f, Band, 1f);
                Barrel(part, x, 0.045f, body, arch + 0.02f, halfDepth + 0.02f, Band, false);
            }

            foreach (float sx in new[] { -1f, 1f })
            {
                // Corner plates wrap the ends, so the gold reads from the side as well as the front.
                Box(part, new float3(sx * (halfWidth - 0.02f), 0.06f, 0f), new float3(0.04f, 0.09f, halfDepth + 0.025f), 1f, Band, 0.95f);
            }

            Box(part, new float3(0f, body - 0.03f, halfDepth + 0.02f), new float3(0.075f, 0.1f, 0.03f), 1f, Band, 1.08f);
            Box(part, new float3(0f, body - 0.05f, halfDepth + 0.05f), new float3(0.035f, 0.035f, 0.012f), 1f, Clasp, 1f);
            data.EmitFaceted(part, 0, FacetCos, null);
        }

        // An axis-aligned box whose foot is scaled by `flare` in x and z.
        private static void Box(SceneryMeshes.Part part, float3 centre, float3 half, float flare, Color color, float tone)
        {
            int first = part.P.Count;
            for (int i = 0; i < 8; i++)
            {
                bool up = (i & 2) != 0;
                float spread = up ? 1f : flare;
                Color c = color;
                // Contact darkening up the lower half, like every scenery prop.
                float shade = up ? 1f : 0.8f;
                c.r *= shade;
                c.g *= shade;
                c.b *= shade;
                part.Add(centre + new float3(((i & 1) != 0 ? half.x : -half.x) * spread, up ? half.y : -half.y,
                    ((i & 4) != 0 ? half.z : -half.z) * spread), c, float2.zero);
            }

            Quad(part, first, 0, 2, 4, 6, centre, tone * 0.94f);
            Quad(part, first, 1, 3, 5, 7, centre, tone * 0.94f);
            Quad(part, first, 0, 1, 4, 5, centre, tone * 0.8f);
            Quad(part, first, 2, 3, 6, 7, centre, tone * 1.06f);
            Quad(part, first, 0, 1, 2, 3, centre, tone);
            Quad(part, first, 4, 5, 6, 7, centre, tone);
        }

        private static void Quad(SceneryMeshes.Part part, int first, int a, int b, int c, int d, float3 inside, float tone)
        {
            Face(part, first + a, first + b, first + c, inside, tone);
            Face(part, first + b, first + d, first + c, inside, tone);
        }

        // Half a barrel lying along x, open underneath. `planks` alternates the tone of its staves.
        private static void Barrel(SceneryMeshes.Part part, float x, float halfX, float y, float rise, float halfDepth, Color color,
            bool planks)
        {
            const int segments = 6;
            int first = part.P.Count;
            for (int end = 0; end < 2; end++)
            {
                for (int k = 0; k <= segments; k++)
                {
                    float a = math.PI * k / segments;
                    part.Add(new float3(x + (end == 0 ? -halfX : halfX), y + math.sin(a) * rise, math.cos(a) * halfDepth), color,
                        float2.zero);
                }
            }

            int left = part.Add(new float3(x - halfX, y, 0f), color, float2.zero);
            int right = part.Add(new float3(x + halfX, y, 0f), color, float2.zero);
            var inside = new float3(x, y + rise * 0.3f, 0f);
            int stride = segments + 1;
            for (int k = 0; k < segments; k++)
            {
                float tone = planks ? ((k & 1) == 0 ? 1.04f : 0.93f) : 1.02f;
                Face(part, first + k, first + k + 1, first + stride + k, inside, tone);
                Face(part, first + k + 1, first + stride + k + 1, first + stride + k, inside, tone);
                Face(part, left, first + k, first + k + 1, inside, 0.9f);
                Face(part, right, first + stride + k, first + stride + k + 1, inside, 0.9f);
            }
        }

        // ---- arena seal ----------------------------------------------------------------------------------------

        // One tall spire, a ring of shoulders packed against it and a fringe of leaning shards: no gap to see through
        // from the corridor, and a jagged top instead of the flat lid of a plug.
        private static void BuildSeal(SceneryMeshes.MeshData data, ref Pcg32 rng)
        {
            float turn = rng.NextFloat() * math.PI * 2f;
            Spire(data, float2.zero, turn, math.radians(2f), 0.27f, 1f, ref rng);
            const int ring = 9;
            for (int i = 0; i < ring; i++)
            {
                float a = turn + (i + Signed(ref rng) * 0.12f) * math.PI * 2f / ring;
                Spire(data, new float2(math.cos(a), math.sin(a)) * 0.58f, a, math.radians(3f + rng.NextFloat() * 5f),
                    0.22f + rng.NextFloat() * 0.05f, 0.68f + rng.NextFloat() * 0.24f, ref rng);
            }

            // Low enough to stay inside the collider's rim once they lean, tall enough to clear the 4 m the seal is sunk.
            int fringe = 11 + (int)rng.NextUInt(3);
            for (int i = 0; i < fringe; i++)
            {
                float a = turn + 0.4f + (i + Signed(ref rng) * 0.25f) * math.PI * 2f / fringe;
                Spire(data, new float2(math.cos(a), math.sin(a)) * 0.8f, a, math.radians(3f + rng.NextFloat() * 5f),
                    0.14f + rng.NextFloat() * 0.05f, 0.48f + rng.NextFloat() * 0.24f, ref rng);
            }
        }

        // A hexagonal shaft with an off-centre point, leaning away from the seal's axis along azimuth `away`.
        private static void Spire(SceneryMeshes.MeshData data, float2 foot, float away, float tilt, float radius, float height,
            ref Pcg32 rng)
        {
            const int sides = 6;
            var outward = new float3(math.cos(away), 0f, math.sin(away));
            quaternion lean = quaternion.AxisAngle(math.normalize(math.cross(new float3(0f, 1f, 0f), outward)), tilt);
            var at = new float3(foot.x, 0f, foot.y);
            float spin = rng.NextFloat() * math.PI;
            // A long point: under the 13 : 12 squash of the plug a short one reads as a block, not a crystal.
            float shoulder = height * (0.55f + rng.NextFloat() * 0.13f);
            var part = new SceneryMeshes.Part();
            for (int level = 0; level < 2; level++)
            {
                for (int k = 0; k < sides; k++)
                {
                    float a = spin + k * math.PI * 2f / sides;
                    float r = radius * (level == 0 ? 1.06f : 0.9f);
                    var p = new float3(math.cos(a) * r, level == 0 ? 0f : shoulder, math.sin(a) * r);
                    part.Add(at + math.mul(lean, p), Tint(level == 0 ? 0.5f : 1f, level == 0 ? 1f : 0.45f), float2.zero);
                }
            }

            var point = new float3(Signed(ref rng) * radius * 0.35f, height, Signed(ref rng) * radius * 0.35f);
            int tip = part.Add(at + math.mul(lean, point), Tint(1.2f, 0f), float2.zero);
            float3 inside = at + math.mul(lean, new float3(0f, shoulder * 0.5f, 0f));
            for (int k = 0; k < sides; k++)
            {
                int next = (k + 1) % sides;
                float tone = 0.86f + rng.NextFloat() * 0.26f;
                Face(part, k, next, sides + k, inside, tone);
                Face(part, next, sides + next, sides + k, inside, tone);
                Face(part, sides + k, sides + next, tip, inside, tone * 1.07f);
            }

            data.EmitFaceted(part, 0, FacetCos, null);
        }

        // ---- shared --------------------------------------------------------------------------------------------

        private static float Signed(ref Pcg32 rng) => rng.NextFloat() * 2f - 1f;

        private static Color Tint(float tone, float accentFree) => new Color(tone, tone, tone, accentFree);

        private static float3 Shuffled(float3 v, ref Pcg32 rng) => rng.NextUInt(3) switch
        {
            0 => v,
            1 => v.yzx,
            _ => v.zxy,
        };

        // EmitFaceted reads one tone per triangle or none at all, so every triangle goes through here.
        private static void Face(SceneryMeshes.Part part, int a, int b, int c, float3 inside, float tone)
        {
            part.Tri(a, b, c, inside);
            part.FaceTone.Add(tone);
        }

        // Recentres on the bounding box and scales its longest side to 1.
        private static void Fit(float3[] points)
        {
            float3 min = points[0], max = points[0];
            foreach (float3 p in points)
            {
                min = math.min(min, p);
                max = math.max(max, p);
            }

            float3 centre = (min + max) * 0.5f;
            float scale = 1f / math.cmax(max - min);
            for (int i = 0; i < points.Length; i++)
            {
                points[i] = (points[i] - centre) * scale;
            }
        }
    }
}
