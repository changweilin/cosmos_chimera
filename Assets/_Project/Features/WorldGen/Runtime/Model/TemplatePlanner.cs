using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.Core.Random;
using Cosmimera.Planet;
using Unity.Mathematics;

namespace Cosmimera.WorldGen
{
    /// <summary>
    /// Chooses which real-world geo-templates a planet uses and where they sit. REQ: planet-world.md#1
    /// ("一顆星球由 3–8 塊模板拼貼"), docs/roadmap/demo.md (P3-03 D0).
    /// </summary>
    /// <remarks>
    /// Pure and deterministic: Pcg32(seed, 'T'). Templates whose terrain tags overlap the planet type's come first, so
    /// a fjord planet is mostly fjords. Centers come from an evenly spaced, randomly rotated lattice, which keeps
    /// stamps apart without rejection sampling.
    /// </remarks>
    public static class TemplatePlanner
    {
        /// <summary>Default angular half extent of a stamp (rad); ≈ 288 m at R = 640.</summary>
        /// <remarks>Pre: none. Post: constant within TemplatePlacement's (0, 0.6]. Side effects: none.</remarks>
        public const float DefaultHalfAngleRad = 0.45f;

        /// <summary>Template count for a planet size (planet-world.md: 3–8 per planet).</summary>
        /// <remarks>Pre: none. Post: 4 for small/medium, 6 for large. Side effects: none.</remarks>
        public static int CountFor(GameData.PlanetSize size) => size >= GameData.PlanetSize.Planet ? 6 : 4;

        /// <summary>Places <paramref name="count"/> templates.</summary>
        /// <remarks>
        /// Pre: catalogTags non-empty, no null entries; typeTags non-null; 1 &lt;= count &lt;= 8.
        /// Post: count placements; Source indexes catalogTags; matching templates are used before non-matching ones,
        ///       and no template repeats until every candidate was used once. Same inputs → same output.
        /// Side effects: allocates the result.
        /// </remarks>
        public static List<TemplatePlacement> Choose(ulong seed, IReadOnlyList<string> typeTags,
            IReadOnlyList<IReadOnlyList<string>> catalogTags, int count)
        {
            Contract.Requires(catalogTags != null && catalogTags.Count > 0 && typeTags != null, "catalog and tags required");
            Contract.Requires(count >= 1 && count <= 8, "count must be 1..8");
            var rng = new Pcg32(seed, 'T');
            var matching = new List<int>();
            var others = new List<int>();
            for (int i = 0; i < catalogTags.Count; i++)
            {
                Contract.Requires(catalogTags[i] != null, "catalog tags must not be null");
                (Overlaps(catalogTags[i], typeTags) ? matching : others).Add(i);
            }

            Shuffle(matching, ref rng);
            Shuffle(others, ref rng);
            matching.AddRange(others);

            quaternion rot = quaternion.AxisAngle(math.normalize(new float3(rng.NextFloat() - 0.5f, rng.NextFloat() - 0.5f,
                rng.NextFloat() - 0.5f) + new float3(0f, 0f, 1e-3f)), rng.NextFloat() * 2f * math.PI);
            float golden = math.PI * (3f - math.sqrt(5f));
            var result = new List<TemplatePlacement>(count);
            for (int i = 0; i < count; i++)
            {
                float y = 1f - 2f * (i + 0.5f) / count;
                float r = math.sqrt(1f - y * y);
                float3 c = math.normalize(math.mul(rot, new float3(math.cos(golden * i) * r, y, math.sin(golden * i) * r)));
                result.Add(new TemplatePlacement(matching[i % matching.Count], c, rng.NextFloat() * 2f * math.PI,
                    DefaultHalfAngleRad));
            }

            return result;
        }

        private static bool Overlaps(IReadOnlyList<string> a, IReadOnlyList<string> b)
        {
            foreach (string x in a)
            {
                foreach (string y in b)
                {
                    if (x == y)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static void Shuffle(List<int> list, ref Pcg32 rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = (int)rng.NextUInt((uint)(i + 1));
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
