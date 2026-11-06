using Unity.Mathematics;

namespace Cosmimera.Planet
{
    /// <summary>Scale guards shared by the planet geometry so normalisation works for every finite non-zero input.</summary>
    /// <remarks>
    /// Pre: none. Post: see members. Side effects: none (pure).
    /// REQ: docs/reviews/P1-01-P1-02-sol.md#F3 — lengthsq of (1e20, 0, 0) overflows to ∞ and of (1e-30, 0, 0)
    /// underflows to 0, so "finite and non-zero" alone did not make normalize valid.
    /// </remarks>
    internal static class VectorMath
    {
        // 2^-40 and 2^40: squaring stays well inside float range, so ordinary inputs are returned untouched and
        // keep bit-identical results (the cube-seam contract depends on that); only extreme inputs are divided.
        private const float MinUnscaled = 9.094947e-13f;
        private const float MaxUnscaled = 1.0995116e12f;

        /// <summary>A positive multiple of <paramref name="v"/> whose largest component is within [2^-40, 2^40].</summary>
        /// <remarks>
        /// Pre: v finite and non-zero. Post: result == v when cmax(|v|) is already in range; same direction otherwise.
        /// Side effects: none.
        /// </remarks>
        internal static float3 Rescale(float3 v)
        {
            float m = math.cmax(math.abs(v));
            return m >= MinUnscaled && m <= MaxUnscaled ? v : v / m;
        }
    }
}
