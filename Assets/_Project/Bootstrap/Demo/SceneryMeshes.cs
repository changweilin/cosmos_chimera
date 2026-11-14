using System;
using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.Core.Random;
using Unity.Mathematics;
using UnityEngine;

namespace Cosmimera.Bootstrap.Demo
{
    /// <summary>Silhouette family of a procedural scenery mesh.</summary>
    /// <remarks>Pre: none. Post: stable values (they seed the mesh streams). Side effects: none.</remarks>
    public enum SceneryArchetype
    {
        /// <summary>Round-crowned tree: clustered foliage lobes on a flared, slightly bent trunk.</summary>
        Broadleaf = 0,

        /// <summary>Flat-topped tree: a thin layered canopy on a tall forked trunk.</summary>
        Umbrella = 1,

        /// <summary>Tiered needle tree.</summary>
        Conifer = 2,

        /// <summary>Chiselled boulder with one or two half-buried companions.</summary>
        Boulder = 3,

        /// <summary>Cluster of pointed hexagonal shafts.</summary>
        CrystalCluster = 4,

        /// <summary>Low bush (灌木): short stems under merged foliage lobes.</summary>
        Shrub = 5,

        /// <summary>Clump of thin jointed canes with drooping leaf whorls (箭竹).</summary>
        CaneClump = 6,

        /// <summary>Tussock of arching blades under plumed flower stalks (芒草).</summary>
        PlumeGrass = 7,
    }

    /// <summary>
    /// Asset-free meshes for scattered scenery (P3-09 presentation): trees, boulders, crystal clusters and understory plants shaped in code,
    /// so every planet draws them without an art import and every client builds the same ones.
    /// REQ: vision.md#畫風與沉浸感; planet-world.md (7. 散佈). Drawn with Cosmimera/ToonScenery, whose channel layout
    /// (colour rgb = tint, a = 1 − accent mask; UV1 = sway weight, flutter phase) these meshes fill.
    /// </summary>
    /// <remarks>
    /// Every mesh is one unit tall-ish (a tree's, cane clump's or grass tussock's top is near y = 1, a boulder or
    /// shrub is ~1 wide, a crystal's main shaft is 1 tall) with its ground contact at y = 0 and a skirt below it, so a prop scaled by ScatterItem.SizeM matches the
    /// rule layer's collider and volume and never shows a gap on a slope.
    /// Pre: main thread (creates Unity objects). Post: results depend only on (archetype, variant). Side effects: allocates.
    /// </remarks>
    public static class SceneryMeshes
    {
        private const ulong Salt = 0x5CE7E12Dul;
        private const float FacetCos = 0.8f; // faces within ~37° share a smooth normal; sharper edges stay crisp

        /// <summary>Number of distinct meshes of <paramref name="archetype"/>.</summary>
        /// <remarks>Pre: defined archetype. Post: ≥ 1, constant per archetype. Side effects: none.</remarks>
        public static int VariantCount(SceneryArchetype archetype) => archetype switch
        {
            SceneryArchetype.Broadleaf => 4,
            SceneryArchetype.Umbrella => 3,
            SceneryArchetype.Conifer => 3,
            SceneryArchetype.Boulder => 5,
            SceneryArchetype.CrystalCluster => 4,
            SceneryArchetype.Shrub => 4,
            SceneryArchetype.CaneClump => 3,
            SceneryArchetype.PlumeGrass => 4,
            _ => throw new ArgumentOutOfRangeException(nameof(archetype)),
        };

        /// <summary>Builds variant <paramref name="variant"/> of <paramref name="archetype"/>.</summary>
        /// <remarks>
        /// Pre: 0 ≤ variant &lt; VariantCount(archetype); main thread.
        /// Post: a new mesh with normals, colours and UV1; plants have two sub-meshes (trees and shrubs: 0 = wood,
        ///       1 = foliage; cane clumps: 0 = canes, 1 = leaves; plume grass: 0 = blades and stalks, 1 = plumes),
        ///       boulders and crystals one; the same arguments always give the same vertices; bounds include the wind
        ///       sway.
        /// Side effects: allocates a Mesh the caller owns (and must destroy).
        /// </remarks>
        public static Mesh Build(SceneryArchetype archetype, int variant)
        {
            Contract.Requires(variant >= 0 && variant < VariantCount(archetype), "variant out of range");
            var rng = new Pcg32(Salt ^ ((ulong)archetype << 32), (ulong)variant);
            var data = new MeshData();
            switch (archetype)
            {
                case SceneryArchetype.Broadleaf:
                    BuildBroadleaf(data, ref rng);
                    break;
                case SceneryArchetype.Umbrella:
                    BuildUmbrella(data, ref rng);
                    break;
                case SceneryArchetype.Conifer:
                    BuildConifer(data, ref rng);
                    break;
                case SceneryArchetype.Boulder:
                    BuildBoulder(data, ref rng, variant);
                    break;
                case SceneryArchetype.Shrub:
                    BuildShrub(data, ref rng);
                    break;
                case SceneryArchetype.CaneClump:
                    BuildCaneClump(data, ref rng);
                    break;
                case SceneryArchetype.PlumeGrass:
                    BuildPlumeGrass(data, ref rng);
                    break;
                default:
                    BuildCrystals(data, ref rng);
                    break;
            }

            return data.ToMesh("Scenery_" + archetype + "_" + variant);
        }

        // ---- trees ---------------------------------------------------------------------------------------------

        private static void BuildBroadleaf(MeshData data, ref Pcg32 rng)
        {
            float3 lean = new float3(Signed(ref rng) * 0.07f, 0f, Signed(ref rng) * 0.07f);
            float3[] spine = TrunkSpine(lean, 0.66f, ref rng);
            float3 top = spine[spine.Length - 1];
            float3 crownCentre = top + new float3(0f, 0.06f, 0f);

            var lobes = new List<Lobe> { new Lobe(crownCentre, new float3(0.29f, 0.23f, 0.29f), 2) };
            int ring = 4 + (int)rng.NextUInt(2);
            float turn = rng.NextFloat() * math.PI * 2f;
            for (int i = 0; i < ring; i++)
            {
                float a = turn + (i + Signed(ref rng) * 0.25f) * math.PI * 2f / ring;
                float r = 0.18f + rng.NextFloat() * 0.05f;
                float reach = 0.17f + rng.NextFloat() * 0.05f;
                lobes.Add(new Lobe(top + new float3(math.cos(a) * reach, -0.07f + rng.NextFloat() * 0.09f, math.sin(a) * reach),
                    new float3(r, r * 0.8f, r), 2));
            }

            lobes.Add(new Lobe(top + new float3(Signed(ref rng) * 0.05f, 0.24f, Signed(ref rng) * 0.05f),
                new float3(0.16f, 0.135f, 0.16f), 2));

            var wood = new Part();
            Tube(wood, spine, 7, (i, k) => TrunkRadius(0.062f, i / (float)(spine.Length - 1), k), BarkColor);
            for (int b = 0; b < 3; b++)
            {
                Limb(wood, spine, 0.5f + b * 0.12f, lobes[1 + b * (ring - 1) / 2].Centre, 0.03f);
            }

            data.EmitSmooth(wood, 0, null);
            EmitCrown(data, lobes, crownCentre, 0.42f, 1.02f, 0.17f, ref rng);
        }

        private static void BuildUmbrella(MeshData data, ref Pcg32 rng)
        {
            float3 lean = new float3(Signed(ref rng) * 0.1f, 0f, Signed(ref rng) * 0.1f);
            float3[] spine = TrunkSpine(lean, 0.74f, ref rng);
            float3 top = spine[spine.Length - 1];
            float3 crownCentre = top + new float3(0f, 0.08f, 0f);

            var lobes = new List<Lobe> { new Lobe(crownCentre + new float3(0f, 0.03f, 0f), new float3(0.27f, 0.1f, 0.27f), 2) };
            int ring = 5 + (int)rng.NextUInt(2);
            float turn = rng.NextFloat() * math.PI * 2f;
            for (int i = 0; i < ring; i++)
            {
                float a = turn + (i + Signed(ref rng) * 0.2f) * math.PI * 2f / ring;
                float r = 0.17f + rng.NextFloat() * 0.06f;
                float reach = 0.23f + rng.NextFloat() * 0.06f;
                lobes.Add(new Lobe(top + new float3(math.cos(a) * reach, 0.04f + rng.NextFloat() * 0.06f, math.sin(a) * reach),
                    new float3(r, r * 0.45f, r), 2));
            }

            var wood = new Part();
            Tube(wood, spine, 7, (i, k) => TrunkRadius(0.05f, i / (float)(spine.Length - 1), k), BarkColor);
            // The canopy is thin, so the fork under it shows: one limb per other lobe.
            for (int b = 1; b < lobes.Count; b += 2)
            {
                Limb(wood, spine, 0.55f + 0.06f * (b % 3), lobes[b].Centre, 0.028f);
            }

            data.EmitSmooth(wood, 0, null);
            EmitCrown(data, lobes, crownCentre, 0.68f, 0.98f, 0.1f, ref rng);
        }

        private static void BuildConifer(MeshData data, ref Pcg32 rng)
        {
            float3 lean = new float3(Signed(ref rng) * 0.03f, 0f, Signed(ref rng) * 0.03f);
            float3[] spine = TrunkSpine(lean, 0.96f, ref rng);
            var wood = new Part();
            Tube(wood, spine, 6, (i, k) => TrunkRadius(0.048f, i / (float)(spine.Length - 1), k), BarkColor);
            data.EmitSmooth(wood, 0, null);

            int tiers = 4 + (int)rng.NextUInt(2);
            const int sides = 10;
            for (int t = 0; t < tiers; t++)
            {
                float f = t / (float)(tiers - 1);
                float baseY = math.lerp(0.2f, 0.8f, f);
                float radius = math.lerp(0.31f, 0.1f, f) * (0.94f + rng.NextFloat() * 0.12f);
                float apexY = baseY + math.lerp(0.3f, 0.24f, f);
                float3 axis = SpineAt(spine, baseY / 0.96f);
                axis.y = 0f;
                float phase = 0.15f + 0.8f * rng.NextFloat();
                float turn = rng.NextFloat() * math.PI * 2f;
                float tone = 0.94f + rng.NextFloat() * 0.1f;

                var skirt = new Part();
                var under = new Part();
                int apex = skirt.Add(axis + new float3(0f, apexY, 0f), Tint(1.02f * tone, 1f), Wind(apexY, 0f, phase));
                int hub = under.Add(axis + new float3(0f, baseY + 0.07f, 0f), Tint(0.5f, 1f), Wind(baseY, 0f, phase));
                var rim = new int[sides];
                var rimUnder = new int[sides];
                for (int k = 0; k < sides; k++)
                {
                    // Alternate long and short points: a saw-toothed hem reads as boughs, a round one as a cone.
                    bool tip = (k & 1) == 0;
                    float r = radius * (tip ? 1f : 0.74f) * (0.92f + rng.NextFloat() * 0.16f);
                    float a = turn + k * math.PI * 2f / sides;
                    float3 p = axis + new float3(math.cos(a) * r, baseY - (tip ? 0.035f : 0f), math.sin(a) * r);
                    rim[k] = skirt.Add(p, Tint((tip ? 1.08f : 0.86f) * tone, tip ? 0.15f : 1f), Wind(p.y, r, phase));
                    rimUnder[k] = under.Add(p, Tint(0.62f, 1f), Wind(p.y, r, phase));
                }

                float3 inside = axis + new float3(0f, baseY + 0.12f, 0f);
                for (int k = 0; k < sides; k++)
                {
                    int next = (k + 1) % sides;
                    skirt.Tri(apex, rim[k], rim[next], inside);
                    under.Tri(hub, rimUnder[k], rimUnder[next], inside);
                }

                // Separate parts so the hem between the lit top and the dark underside stays a hard crease.
                data.EmitSmooth(skirt, 1, null);
                data.EmitSmooth(under, 1, null);
            }
        }

        private static float3[] TrunkSpine(float3 lean, float height, ref Pcg32 rng)
        {
            const int rings = 7;
            var spine = new float3[rings];
            float wobble = Signed(ref rng) * 0.025f;
            for (int i = 0; i < rings; i++)
            {
                float t = i / (float)(rings - 1);
                // Starts below the ground: the item is sunk 0.1 m, and a slope must never show under the root flare.
                spine[i] = lean * (t * t) + new float3(wobble * math.sin(t * math.PI), -0.05f + t * (height + 0.05f),
                    -wobble * math.sin(t * math.PI * 1.5f));
            }

            return spine;
        }

        private static float3 SpineAt(float3[] spine, float t)
        {
            float x = math.saturate(t) * (spine.Length - 1);
            int i = math.min((int)x, spine.Length - 2);
            return math.lerp(spine[i], spine[i + 1], x - i);
        }

        // Root flare with three buttress lobes at the ground, tapering to under half at the top.
        private static float TrunkRadius(float radius, float t, int side)
        {
            float flare = math.exp(-t * 9f);
            float lobes = 0.5f + 0.5f * math.sin(side * 2.7f + 0.6f);
            return radius * (1f + flare * (0.55f + 0.6f * lobes)) * math.lerp(1f, 0.42f, t);
        }

        private static Color BarkColor(float3 p, int side)
        {
            // Contact darkening at the ground, then lighter/darker ridges running up the trunk.
            float tone = math.lerp(0.66f, 1f, math.saturate(p.y / 0.2f)) * (1f + 0.07f * math.sin(side * 2.1f + p.y * 9f));
            // Accent (moss / ground colour) creeps up the root flare so the trunk grows out of the ground.
            return Tint(tone, math.saturate(0.35f + p.y / 0.16f));
        }

        private static void Limb(Part wood, float3[] spine, float t, float3 toward, float radius)
        {
            float3 from = SpineAt(spine, t);
            float3 end = math.lerp(from, toward, 0.78f);
            var limb = new[]
            {
                from - new float3(0f, 0.02f, 0f),
                math.lerp(from, end, 0.5f) + new float3(0f, -0.025f, 0f),
                end,
            };
            Tube(wood, limb, 5, (i, k) => radius * math.lerp(1f, 0.4f, i * 0.5f), (p, k) => Tint(0.82f, 1f));
        }

        // A closed tube along `spine`, ending in a point. Sway weight grows with height² so roots stay planted.
        private static void Tube(Part part, float3[] spine, int sides, Func<int, int, float> radius, Func<float3, int, Color> color)
        {
            int first = part.P.Count;
            float3 reference = new float3(1f, 0f, 0f);
            for (int i = 0; i < spine.Length; i++)
            {
                float3 tangent = math.normalize(spine[math.min(i + 1, spine.Length - 1)] - spine[math.max(i - 1, 0)]);
                // Limbs can run along X; any axis not parallel to the tangent gives a stable frame.
                float3 axis = math.abs(math.dot(tangent, reference)) > 0.9f ? new float3(0f, 0f, 1f) : reference;
                float3 side = math.normalize(math.cross(tangent, axis));
                float3 forward = math.cross(side, tangent);
                for (int k = 0; k < sides; k++)
                {
                    float a = k * math.PI * 2f / sides;
                    float3 p = spine[i] + (side * math.cos(a) + forward * math.sin(a)) * radius(i, k);
                    part.Add(p, color(p, k), new float2(WoodSway(p.y), 0f));
                }
            }

            for (int i = 0; i < spine.Length - 1; i++)
            {
                float3 inside = (spine[i] + spine[i + 1]) * 0.5f;
                for (int k = 0; k < sides; k++)
                {
                    int a = first + i * sides + k;
                    int b = first + i * sides + (k + 1) % sides;
                    part.Tri(a, b, a + sides, inside);
                    part.Tri(b, b + sides, a + sides, inside);
                }
            }

            float3 last = spine[spine.Length - 1];
            float3 tipDir = math.normalize(last - spine[spine.Length - 2]);
            int tip = part.Add(last + tipDir * 0.02f, color(last, 0), new float2(WoodSway(last.y), 0f));
            int ring = first + (spine.Length - 1) * sides;
            for (int k = 0; k < sides; k++)
            {
                part.Tri(ring + k, ring + (k + 1) % sides, tip, last - tipDir * 0.05f);
            }
        }

        private static float WoodSway(float y) => math.saturate(y) * math.saturate(y);

        private static float2 Wind(float y, float radial, float phase) =>
            new float2(math.saturate(y) * math.saturate(y) + 0.6f * radial, phase);

        // Foliage lobes: displaced icospheres with flattened undersides. Their normals are pulled toward the crown's
        // centre so the cel bands wrap the whole crown as one mass instead of shading every lobe as its own ball.
        private static void EmitCrown(MeshData data, List<Lobe> lobes, float3 crownCentre, float bottomY, float topY,
            float bump, ref Pcg32 rng)
        {
            foreach (Lobe lobe in lobes)
            {
                Ico ico = Ico.Get(lobe.Detail);
                var part = new Part();
                float3 offset = new float3(rng.NextFloat(), rng.NextFloat(), rng.NextFloat()) * 37f;
                float tone = 0.95f + rng.NextFloat() * 0.1f;
                float phase = 0.1f + 0.9f * rng.NextFloat();
                foreach (float3 unit in ico.Vertices)
                {
                    float3 d = unit;
                    float swell = 1f + bump * noise.snoise(d * 2.1f + offset) + bump * 0.4f * noise.snoise(d * 4.7f + offset);
                    float3 local = d * lobe.Radii * swell;
                    if (local.y < 0f)
                    {
                        local.y *= 0.6f;
                    }

                    float3 p = lobe.Centre + local;
                    float h = math.saturate((p.y - bottomY) / (topY - bottomY));
                    float under = d.y < 0f ? math.lerp(1f, 0.8f, -d.y) : 1f;
                    float radial = math.length(p.xz - crownCentre.xz);
                    part.Add(p, Tint(math.lerp(0.7f, 1.07f, h) * under * tone, 1f), Wind(p.y, radial, phase));
                }

                for (int t = 0; t < ico.Triangles.Length; t += 3)
                {
                    part.Tri(ico.Triangles[t], ico.Triangles[t + 1], ico.Triangles[t + 2], lobe.Centre);
                }

                data.EmitSmooth(part, 1, (p, n, c) =>
                {
                    float3 outward = math.normalize(p - (crownCentre - new float3(0f, 0.08f, 0f)));
                    float3 soft = math.normalize(math.lerp(n, outward, 0.55f));
                    float h = math.saturate((p.y - bottomY) / (topY - bottomY));
                    // Accent = the sunlit top tone: only upward faces high in the crown take it.
                    float sunlit = math.smoothstep(0.3f, 0.85f, soft.y) * math.smoothstep(0.35f, 0.8f, h);
                    c.a = 1f - sunlit;
                    return (soft, c);
                });
            }
        }

        // ---- understory ----------------------------------------------------------------------------------------

        private const float GoldenAngle = 2.39996323f; // successive feet at this turn fill a clump without rows

        private static void BuildShrub(MeshData data, ref Pcg32 rng)
        {
            // Unit = width: the foliage fills ScatterItem's shrub ellipsoid (semi-axes 0.5, 0.4, 0.5).
            float3 crownCentre = new float3(0f, 0.3f, 0f);
            var lobes = new List<Lobe> { new Lobe(crownCentre, new float3(0.32f, 0.27f, 0.32f), 2) };
            int ring = 4 + (int)rng.NextUInt(3);
            float turn = rng.NextFloat() * math.PI * 2f;
            for (int i = 0; i < ring; i++)
            {
                float a = turn + (i + Signed(ref rng) * 0.25f) * math.PI * 2f / ring;
                float r = 0.17f + rng.NextFloat() * 0.07f;
                float reach = 0.22f + rng.NextFloat() * 0.08f;
                lobes.Add(new Lobe(new float3(math.cos(a) * reach, 0.13f + rng.NextFloat() * 0.14f, math.sin(a) * reach),
                    new float3(r, r * 0.85f, r), 1));
            }

            lobes.Add(new Lobe(new float3(Signed(ref rng) * 0.08f, 0.5f, Signed(ref rng) * 0.08f),
                new float3(0.18f, 0.15f, 0.18f), 1));

            // The lobes almost reach the ground; the stems only show in the gaps between them.
            var wood = new Part();
            for (int s = 0; s < 3; s++)
            {
                float3 end = lobes[1 + s * ring / 3].Centre;
                var spine = new[]
                {
                    new float3(end.x * 0.15f, -0.06f, end.z * 0.15f),
                    new float3(end.x * 0.5f, end.y * 0.45f, end.z * 0.5f),
                    end,
                };
                Tube(wood, spine, 5, (i, k) => 0.03f * math.lerp(1f, 0.45f, i * 0.5f), BarkColor);
            }

            data.EmitSmooth(wood, 0, null);
            EmitCrown(data, lobes, crownCentre, 0.02f, 0.68f, 0.2f, ref rng);
        }

        private static void BuildCaneClump(MeshData data, ref Pcg32 rng)
        {
            const int nodes = 5;
            const float buried = 0.06f;
            int canes = 9 + (int)rng.NextUInt(5);
            float turn = rng.NextFloat() * math.PI * 2f;
            var wood = new Part();
            var leaves = new Part();
            for (int c = 0; c < canes; c++)
            {
                float a = turn + c * GoldenAngle;
                var outward = new float3(math.cos(a), 0f, math.sin(a));
                // Feet stay inside the rule layer's 0.2 clump radius; outer canes lean further out.
                float foot = 0.03f + 0.15f * math.sqrt((c + 0.5f) / canes);
                float height = 0.62f + rng.NextFloat() * 0.36f;
                float lean = 0.05f + foot * 0.9f + rng.NextFloat() * 0.06f;
                float radius = 0.011f + rng.NextFloat() * 0.006f;

                // Two rings per joint: one just under it at the cane's width, one on it, swollen and dark. Vertex
                // colour alone cannot draw a joint between rings a fifth of the cane apart.
                var spine = new float3[nodes * 2];
                var swell = new float[nodes * 2];
                for (int i = 0; i < spine.Length; i++)
                {
                    bool joint = (i & 1) == 0 && i > 0;
                    float t = i == 0 ? 0f : i == spine.Length - 1 ? 1f : ((i + 1) / 2) / (float)nodes - (joint ? 0f : 0.035f);
                    spine[i] = outward * (foot + lean * t * t) + new float3(0f, -buried + t * (height + buried), 0f);
                    swell[i] = (joint ? 1.3f : 1f) * math.lerp(1f, 0.5f, t);
                }

                Tube(wood, spine, 5, (i, k) => radius * swell[i], (p, k) =>
                {
                    float along = (p.y + buried) / (height + buried) * nodes;
                    bool joint = along > 0.5f && math.abs(along - math.round(along)) < 0.1f;
                    float tone = math.lerp(0.72f, 1f, math.saturate(p.y / 0.15f)) * (joint ? 0.66f : 1f);
                    return Tint(tone, joint ? 0.35f : 1f);
                });

                float phase = 0.1f + 0.9f * rng.NextFloat();
                for (int w = 0; w < 3; w++)
                {
                    float3 node = SpineAt(spine, 0.55f + w * 0.17f + rng.NextFloat() * 0.05f);
                    int count = 2 + (int)rng.NextUInt(2);
                    float spin = rng.NextFloat() * math.PI * 2f;
                    for (int l = 0; l < count; l++)
                    {
                        float la = spin + l * math.PI * 2f / count + Signed(ref rng) * 0.3f;
                        float length = 0.13f + rng.NextFloat() * 0.07f;
                        float tone = 0.9f + rng.NextFloat() * 0.16f;
                        Blade(leaves, Arc(node, la, length, 0.35f, 0.75f, 4), Across(la), length * 0.13f, phase,
                            t => Tint(tone * math.lerp(0.82f, 1.06f, t), 1f - 0.5f * t));
                    }
                }
            }

            data.EmitSmooth(wood, 0, null);
            data.EmitFaceted(leaves, 1, FacetCos, null);
        }

        private static void BuildPlumeGrass(MeshData data, ref Pcg32 rng)
        {
            var blades = new Part();
            var stalkWood = new Part();
            var plumes = new Part();
            float turn = rng.NextFloat() * math.PI * 2f;
            int count = 26 + (int)rng.NextUInt(9);
            for (int b = 0; b < count; b++)
            {
                float a = turn + b * GoldenAngle + Signed(ref rng) * 0.2f;
                var outward = new float3(math.cos(a), 0f, math.sin(a));
                float rise = 0.42f + rng.NextFloat() * 0.3f;
                float reach = 0.22f + rng.NextFloat() * 0.26f;
                float foot = 0.02f + rng.NextFloat() * 0.05f;
                const int points = 5;
                var spine = new float3[points];
                for (int i = 0; i < points; i++)
                {
                    float s = i / (float)(points - 1);
                    // A parabola peaking at s = 0.62: the outer third of the blade droops under its own weight.
                    float fall = (s - 0.62f) / 0.62f;
                    spine[i] = outward * (foot + reach * (0.5f * s + 0.5f * s * s)) +
                               new float3(0f, -0.05f + (rise + 0.05f) * (1f - fall * fall), 0f);
                }

                float tone = 0.88f + rng.NextFloat() * 0.2f;
                // Accent = the dry tone: tips bleach first.
                Blade(blades, spine, Across(a), 0.03f + rng.NextFloat() * 0.014f, 0.1f + 0.9f * rng.NextFloat(),
                    t => Tint(tone * math.lerp(0.74f, 1.06f, t), 1f - 0.85f * math.smoothstep(0.55f, 1f, t)));
            }

            int stalks = 4 + (int)rng.NextUInt(3);
            for (int s = 0; s < stalks; s++)
            {
                float a = turn + 1.1f + s * math.PI * 2f / stalks + Signed(ref rng) * 0.5f;
                var outward = new float3(math.cos(a), 0f, math.sin(a));
                float height = 0.74f + rng.NextFloat() * 0.2f;
                float lean = 0.06f + rng.NextFloat() * 0.12f;
                var spine = new float3[5];
                for (int i = 0; i < spine.Length; i++)
                {
                    float t = i / (float)(spine.Length - 1);
                    spine[i] = outward * (0.02f + lean * t * t) + new float3(0f, -0.05f + t * (height + 0.05f), 0f);
                }

                Tube(stalkWood, spine, 4, (i, k) => 0.008f * math.lerp(1f, 0.6f, i / (float)(spine.Length - 1)),
                    (p, k) => Tint(0.9f, 0.6f));

                // The plume: a fan of drooping racemes, all swept to the side the stalk leans to.
                int racemes = 7 + (int)rng.NextUInt(3);
                float phase = 0.1f + 0.9f * rng.NextFloat();
                for (int r = 0; r < racemes; r++)
                {
                    float ra = a + (r - (racemes - 1) * 0.5f) * 0.36f + Signed(ref rng) * 0.1f;
                    float length = 0.2f + rng.NextFloat() * 0.09f;
                    Blade(plumes, Arc(spine[spine.Length - 1], ra, length, 0.3f + rng.NextFloat() * 0.5f, 0.9f, 4),
                        Across(ra), 0.024f, phase, t => Tint(math.lerp(0.9f, 1.1f, t), 1f - 0.7f * t));
                }
            }

            data.EmitFaceted(blades, 0, FacetCos, null);
            data.EmitSmooth(stalkWood, 0, null);
            data.EmitFaceted(plumes, 1, FacetCos, null);
        }

        // Horizontal unit vector square to azimuth `a`: the width axis of a blade that grows outward along `a`.
        private static float3 Across(float a) => new float3(-math.sin(a), 0f, math.cos(a));

        // `points` along a leaf of `length` leaving `from` toward azimuth `a`: it lifts by `lift` and then falls by `droop`.
        private static float3[] Arc(float3 from, float a, float length, float lift, float droop, int points)
        {
            var outward = new float3(math.cos(a), 0f, math.sin(a));
            var spine = new float3[points];
            for (int i = 0; i < points; i++)
            {
                float s = i / (float)(points - 1);
                spine[i] = from + outward * (length * s) + new float3(0f, length * (lift * s - droop * s * s), 0f);
            }

            return spine;
        }

        // A closed, flattened blade along `spine` (whose last point is the tip): a diamond cross-section `halfWidth`
        // across `across` and about a quarter of that thick. Two broad faces, yet nothing is single-sided, so the
        // scenery shader needs no cull-off variant. `across` must not be parallel to the spine. Emit it faceted: the
        // two halves of a broad face are within the crease angle and shade as one, while a smooth edge vertex would
        // average the upper and lower faces into a normal lying in the blade's plane.
        private static void Blade(Part part, float3[] spine, float3 across, float halfWidth, float phase, Func<float, Color> color)
        {
            int rings = spine.Length - 1;
            int first = part.P.Count;
            for (int i = 0; i < rings; i++)
            {
                float t = i / (float)rings;
                float3 tangent = math.normalize(spine[i + 1] - spine[math.max(i - 1, 0)]);
                float3 wide = math.normalize(across - tangent * math.dot(across, tangent));
                float3 thick = math.cross(tangent, wide);
                // Widest a third of the way up, like a leaf; the root keeps some width or its ring collapses.
                float w = halfWidth * math.lerp(0.55f, 1f, math.saturate(t * 3f)) * (1f - 0.55f * t * t);
                for (int k = 0; k < 4; k++)
                {
                    float3 offset = k switch { 0 => wide * w, 1 => thick * (w * 0.28f), 2 => -wide * w, _ => -thick * (w * 0.28f) };
                    float3 p = spine[i] + offset;
                    part.Add(p, color(t), Wind(p.y, math.length(p.xz), phase));
                }
            }

            for (int i = 0; i < rings - 1; i++)
            {
                float3 inside = (spine[i] + spine[i + 1]) * 0.5f;
                for (int k = 0; k < 4; k++)
                {
                    int a = first + i * 4 + k;
                    int b = first + i * 4 + (k + 1) % 4;
                    part.Tri(a, b, a + 4, inside);
                    part.Tri(b, b + 4, a + 4, inside);
                }
            }

            float3 end = spine[rings];
            int tip = part.Add(end, color(1f), Wind(end.y, math.length(end.xz), phase));
            int last = first + (rings - 1) * 4;
            for (int k = 0; k < 4; k++)
            {
                part.Tri(last + k, last + (k + 1) % 4, tip, spine[rings - 1]);
            }
        }

        // ---- rocks ---------------------------------------------------------------------------------------------

        private static void BuildBoulder(MeshData data, ref Pcg32 rng, int variant)
        {
            // One variant stands taller than it is wide; the rest lie low (ScatterItem's collider is a low ellipsoid).
            bool standing = variant == 3;
            float3 radii = standing ? new float3(0.4f, 0.46f, 0.36f) : new float3(0.5f, 0.34f, 0.42f);
            radii *= new float3(0.92f + rng.NextFloat() * 0.16f, 0.92f + rng.NextFloat() * 0.16f, 0.92f + rng.NextFloat() * 0.16f);
            float3 centre = new float3(0f, radii.y * 0.62f, 0f);
            Rock(data, centre, radii, 2, 6 + (int)rng.NextUInt(3), ref rng);

            int companions = 1 + (int)rng.NextUInt(2);
            float turn = rng.NextFloat() * math.PI * 2f;
            for (int i = 0; i < companions; i++)
            {
                float a = turn + i * (2.2f + rng.NextFloat());
                float r = 0.19f + rng.NextFloat() * 0.09f;
                float reach = radii.x * 0.78f + r * 0.5f;
                Rock(data, new float3(math.cos(a) * reach, r * 0.3f, math.sin(a) * reach * 0.85f),
                    new float3(r, r * 0.72f, r * 0.88f), 1, 4, ref rng);
            }
        }

        // A noise-displaced ellipsoid cut by random planes: the cuts are the flat chiselled faces, what is left
        // between them stays rounded, and the angle-limited normals keep the edges between the two crisp.
        private static void Rock(MeshData data, float3 centre, float3 radii, int detail, int cuts, ref Pcg32 rng)
        {
            Ico ico = Ico.Get(detail);
            var local = new float3[ico.Vertices.Length];
            float3 offset = new float3(rng.NextFloat(), rng.NextFloat(), rng.NextFloat()) * 53f;
            for (int i = 0; i < local.Length; i++)
            {
                float3 d = ico.Vertices[i];
                float swell = 1f + 0.2f * noise.snoise(d * 1.3f + offset) + 0.07f * noise.snoise(d * 3.4f + offset);
                local[i] = d * radii * swell;
            }

            for (int c = 0; c < cuts; c++)
            {
                float3 dir = math.normalize(new float3(Signed(ref rng), Signed(ref rng) * 0.8f + 0.2f, Signed(ref rng)) + 1e-3f);
                float reach = 0f;
                foreach (float3 v in local)
                {
                    reach = math.max(reach, math.dot(v, dir));
                }

                float keep = reach * (0.7f + rng.NextFloat() * 0.2f);
                for (int i = 0; i < local.Length; i++)
                {
                    float over = math.dot(local[i], dir) - keep;
                    if (over > 0f)
                    {
                        local[i] -= dir * over;
                    }
                }
            }

            var part = new Part();
            float top = centre.y + radii.y;
            foreach (float3 v in local)
            {
                float3 p = centre + v;
                // Contact darkening where the rock meets the ground, and soft strata bands up its side.
                float tone = math.lerp(0.62f, 1f, math.saturate(p.y / (top * 0.45f)));
                float bed = math.frac(p.y / top * 3.2f + noise.snoise(p * 2.3f + offset) * 0.25f);
                tone *= math.lerp(0.93f, 1.05f, math.smoothstep(0.4f, 0.6f, bed));
                part.Add(p, Tint(tone, 1f), float2.zero);
            }

            for (int t = 0; t < ico.Triangles.Length; t += 3)
            {
                part.Tri(ico.Triangles[t], ico.Triangles[t + 1], ico.Triangles[t + 2], centre);
            }

            for (int f = 0; f < part.T.Count / 3; f++)
            {
                part.FaceTone.Add(0.97f + rng.NextFloat() * 0.06f);
            }

            data.EmitFaceted(part, 0, FacetCos, (p, n, c) =>
            {
                // Accent (moss, snow, ash) settles in patches on faces that look up.
                float patch = noise.snoise(p * 2.6f + offset) * 0.5f + 0.5f;
                float settled = math.smoothstep(0.55f, 0.8f, n.y) * math.smoothstep(0.3f, 0.5f, patch);
                c.a = 1f - settled;
                return (n, c);
            });
        }

        // ---- crystals ------------------------------------------------------------------------------------------

        private static void BuildCrystals(MeshData data, ref Pcg32 rng)
        {
            float turn = rng.NextFloat() * math.PI * 2f;
            // The main shaft keeps the old placeholder's footprint and tilt (ScatterItem.BlockRadiusM, VolumeL).
            Shaft(data, float3.zero, turn, math.radians(6f + rng.NextFloat() * 5f), 0.135f, 1f, ref rng);
            int count = 3 + (int)rng.NextUInt(3);
            for (int i = 0; i < count; i++)
            {
                float a = turn + 0.9f + (i + Signed(ref rng) * 0.3f) * math.PI * 2f / count;
                float reach = 0.13f + rng.NextFloat() * 0.08f;
                float height = 0.3f + rng.NextFloat() * 0.34f;
                Shaft(data, new float3(math.cos(a) * reach, 0f, math.sin(a) * reach), a,
                    math.radians(18f + rng.NextFloat() * 22f), 0.055f + rng.NextFloat() * 0.04f, height, ref rng);
            }
        }

        // A hexagonal prism with an off-centre point, tilted away from the cluster along azimuth `away`.
        private static void Shaft(MeshData data, float3 foot, float away, float tilt, float radius, float height, ref Pcg32 rng)
        {
            const int sides = 6;
            float3 outward = new float3(math.cos(away), 0f, math.sin(away));
            quaternion lean = quaternion.AxisAngle(math.normalize(math.cross(new float3(0f, 1f, 0f), outward)), tilt);
            float spin = rng.NextFloat() * math.PI;
            float shoulder = height * (0.74f + rng.NextFloat() * 0.1f);
            var part = new Part();
            for (int ring = 0; ring < 2; ring++)
            {
                for (int k = 0; k < sides; k++)
                {
                    float a = spin + k * math.PI * 2f / sides;
                    float r = radius * (ring == 0 ? 1.05f : 0.92f);
                    // Buried foot: the shaft leans, so its low side must still reach under the ground.
                    float3 p = new float3(math.cos(a) * r, ring == 0 ? -0.14f : shoulder, math.sin(a) * r);
                    part.Add(foot + math.mul(lean, p), Tint(ring == 0 ? 0.6f : 1.02f, ring == 0 ? 1f : 0.55f), float2.zero);
                }
            }

            float3 point = new float3(Signed(ref rng) * radius * 0.3f, height, Signed(ref rng) * radius * 0.3f);
            int tip = part.Add(foot + math.mul(lean, point), Tint(1.18f, 0f), float2.zero);
            float3 inside = foot + math.mul(lean, new float3(0f, shoulder * 0.5f, 0f));
            for (int k = 0; k < sides; k++)
            {
                int next = (k + 1) % sides;
                float tone = 0.88f + rng.NextFloat() * 0.24f; // each facet catches the light differently
                part.Tri(k, next, sides + k, inside);
                part.FaceTone.Add(tone);
                part.Tri(next, sides + next, sides + k, inside);
                part.FaceTone.Add(tone);
                part.Tri(sides + k, sides + next, tip, inside);
                part.FaceTone.Add(tone * 1.06f);
            }

            data.EmitFaceted(part, 0, FacetCos, null);
        }

        // ---- shared --------------------------------------------------------------------------------------------

        private static float Signed(ref Pcg32 rng) => rng.NextFloat() * 2f - 1f;

        // rgb = multiplicative tone, a = 1 − accent mask (ToonScenery's vertex colour layout).
        private static Color Tint(float tone, float accentFree) => new Color(tone, tone, tone, accentFree);

        private readonly struct Lobe
        {
            public readonly float3 Centre;
            public readonly float3 Radii;
            public readonly int Detail;

            public Lobe(float3 centre, float3 radii, int detail)
            {
                Centre = centre;
                Radii = radii;
                Detail = detail;
            }
        }

        /// <summary>An indexed piece of geometry before its normals are decided.</summary>
        internal sealed class Part
        {
            public readonly List<float3> P = new List<float3>();
            public readonly List<Color> C = new List<Color>();
            public readonly List<float2> W = new List<float2>();
            public readonly List<int> T = new List<int>();
            public readonly List<float> FaceTone = new List<float>(); // per triangle; empty = 1

            public int Add(float3 position, Color color, float2 wind)
            {
                P.Add(position);
                C.Add(color);
                W.Add(wind);
                return P.Count - 1;
            }

            // Winds the triangle so it faces away from `inside` (Unity: clockwise is front, cross(b−a, c−a) is its
            // normal), which spares every caller from tracking handedness through mirrored and rotated pieces.
            public void Tri(int a, int b, int c, float3 inside)
            {
                float3 normal = math.cross(P[b] - P[a], P[c] - P[a]);
                float3 centroid = (P[a] + P[b] + P[c]) / 3f;
                if (math.dot(normal, centroid - inside) < 0f)
                {
                    (b, c) = (c, b);
                }

                T.Add(a);
                T.Add(b);
                T.Add(c);
            }
        }

        internal delegate (float3 normal, Color color) Finish(float3 position, float3 normal, Color color);

        internal sealed class MeshData
        {
            private readonly List<Vector3> _positions = new List<Vector3>();
            private readonly List<Vector3> _normals = new List<Vector3>();
            private readonly List<Color> _colors = new List<Color>();
            private readonly List<Vector2> _wind = new List<Vector2>();
            private readonly List<int>[] _triangles;
            private readonly int _fixedSubMeshes;

            // fixedSubMeshes 0 = scenery props: wood, plus foliage only when a tree filled it. ArchitectureMeshes passes
            // its own count so every building has the same material slots, used or not.
            public MeshData(int fixedSubMeshes = 0)
            {
                _fixedSubMeshes = fixedSubMeshes;
                _triangles = new List<int>[math.max(2, fixedSubMeshes)];
                for (int i = 0; i < _triangles.Length; i++)
                {
                    _triangles[i] = new List<int>();
                }
            }

            // Shared vertices, area-weighted normals: one continuous surface.
            public void EmitSmooth(Part part, int subMesh, Finish finish)
            {
                var normals = new float3[part.P.Count];
                for (int t = 0; t < part.T.Count; t += 3)
                {
                    float3 n = FaceNormal(part, t);
                    normals[part.T[t]] += n;
                    normals[part.T[t + 1]] += n;
                    normals[part.T[t + 2]] += n;
                }

                int first = _positions.Count;
                for (int i = 0; i < part.P.Count; i++)
                {
                    Push(part.P[i], math.normalizesafe(normals[i], new float3(0f, 1f, 0f)), part.C[i], part.W[i], finish);
                }

                foreach (int index in part.T)
                {
                    _triangles[subMesh].Add(first + index);
                }
            }

            // Three vertices per triangle; a corner's normal averages only the neighbouring faces within the crease
            // angle, so flat cuts shade flat and rounded stretches shade round.
            public void EmitFaceted(Part part, int subMesh, float creaseCos, Finish finish)
            {
                int faces = part.T.Count / 3;
                var faceNormals = new float3[faces];
                var around = new List<int>[part.P.Count];
                for (int f = 0; f < faces; f++)
                {
                    faceNormals[f] = FaceNormal(part, f * 3);
                    for (int k = 0; k < 3; k++)
                    {
                        int v = part.T[f * 3 + k];
                        (around[v] ??= new List<int>()).Add(f);
                    }
                }

                for (int f = 0; f < faces; f++)
                {
                    float3 flat = math.normalizesafe(faceNormals[f], new float3(0f, 1f, 0f));
                    float tone = part.FaceTone.Count > 0 ? part.FaceTone[f] : 1f;
                    for (int k = 0; k < 3; k++)
                    {
                        int v = part.T[f * 3 + k];
                        float3 sum = float3.zero;
                        foreach (int other in around[v])
                        {
                            if (math.dot(math.normalizesafe(faceNormals[other]), flat) >= creaseCos)
                            {
                                sum += faceNormals[other];
                            }
                        }

                        Color c = part.C[v];
                        c.r *= tone;
                        c.g *= tone;
                        c.b *= tone;
                        _triangles[subMesh].Add(_positions.Count);
                        Push(part.P[v], math.normalizesafe(sum, flat), c, part.W[v], finish);
                    }
                }
            }

            // Area-weighted (unnormalised) so slivers barely steer the vertex normal.
            private static float3 FaceNormal(Part part, int t) =>
                math.cross(part.P[part.T[t + 1]] - part.P[part.T[t]], part.P[part.T[t + 2]] - part.P[part.T[t]]);

            private void Push(float3 position, float3 normal, Color color, float2 wind, Finish finish)
            {
                if (finish != null)
                {
                    (normal, color) = finish(position, normal, color);
                }

                _positions.Add(position);
                _normals.Add(normal);
                _colors.Add(color);
                _wind.Add(wind);
            }

            public Mesh ToMesh(string name)
            {
                var mesh = new Mesh { name = name };
                mesh.SetVertices(_positions);
                mesh.SetNormals(_normals);
                mesh.SetColors(_colors);
                mesh.SetUVs(1, _wind);
                mesh.subMeshCount = _fixedSubMeshes > 0 ? _fixedSubMeshes : _triangles[1].Count > 0 ? 2 : 1;
                for (int s = 0; s < mesh.subMeshCount; s++)
                {
                    mesh.SetTriangles(_triangles[s], s);
                }

                mesh.RecalculateBounds();
                // The vertex shader leans crowns downwind; static bounds must already contain that, or a tree at the
                // screen edge is culled while its swaying crown is still in view.
                Bounds bounds = mesh.bounds;
                bounds.Expand(0.35f);
                mesh.bounds = bounds;
                return mesh;
            }
        }

        /// <summary>Unit icosphere, subdivided; shared read-only data.</summary>
        internal sealed class Ico
        {
            private static readonly Dictionary<int, Ico> Cache = new Dictionary<int, Ico>();

            public float3[] Vertices;
            public int[] Triangles;

            public static Ico Get(int detail)
            {
                if (Cache.TryGetValue(detail, out Ico ico))
                {
                    return ico;
                }

                float g = (1f + math.sqrt(5f)) * 0.5f;
                var vertices = new List<float3>
                {
                    new float3(-1, g, 0), new float3(1, g, 0), new float3(-1, -g, 0), new float3(1, -g, 0),
                    new float3(0, -1, g), new float3(0, 1, g), new float3(0, -1, -g), new float3(0, 1, -g),
                    new float3(g, 0, -1), new float3(g, 0, 1), new float3(-g, 0, -1), new float3(-g, 0, 1),
                };
                for (int i = 0; i < vertices.Count; i++)
                {
                    vertices[i] = math.normalize(vertices[i]);
                }

                var triangles = new List<int>
                {
                    0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                    3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
                };
                for (int d = 0; d < detail; d++)
                {
                    var midpoints = new Dictionary<long, int>();
                    var next = new List<int>(triangles.Count * 4);
                    for (int t = 0; t < triangles.Count; t += 3)
                    {
                        int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
                        int ab = Midpoint(vertices, midpoints, a, b);
                        int bc = Midpoint(vertices, midpoints, b, c);
                        int ca = Midpoint(vertices, midpoints, c, a);
                        next.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
                    }

                    triangles = next;
                }

                ico = new Ico { Vertices = vertices.ToArray(), Triangles = triangles.ToArray() };
                Cache[detail] = ico;
                return ico;
            }

            private static int Midpoint(List<float3> vertices, Dictionary<long, int> midpoints, int a, int b)
            {
                long key = ((long)math.min(a, b) << 32) | (uint)math.max(a, b);
                if (!midpoints.TryGetValue(key, out int index))
                {
                    index = vertices.Count;
                    vertices.Add(math.normalize(vertices[a] + vertices[b]));
                    midpoints[key] = index;
                }

                return index;
            }
        }
    }
}
