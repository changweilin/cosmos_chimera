using Cosmimera.Core.Contracts;
using Unity.Mathematics;

namespace Cosmimera.Planet
{
    /// <summary>One of the six cube faces, named by its outward normal in planet-local axes.</summary>
    /// <remarks>
    /// Pre: only the six named values are defined; APIs taking a CubeFace reject others with ContractException.
    /// Post: the numeric values are part of <see cref="ChunkId.Key"/> (save data, packets) and must never change.
    /// Side effects: none.
    /// </remarks>
    public enum CubeFace : byte
    {
        /// <summary>Normal +X.</summary>
        PosX = 0,

        /// <summary>Normal −X.</summary>
        NegX = 1,

        /// <summary>Normal +Y (contains <see cref="PlanetFrame.NorthPole"/>).</summary>
        PosY = 2,

        /// <summary>Normal −Y.</summary>
        NegY = 3,

        /// <summary>Normal +Z.</summary>
        PosZ = 4,

        /// <summary>Normal −Z.</summary>
        NegZ = 5,
    }

    /// <summary>One side of a square region in face (u, v) parameters.</summary>
    /// <remarks>
    /// Pre: only the four named values are defined; APIs taking a CubeEdge reject others with ContractException.
    /// Post: (int)edge is the bit index in coarser-neighbour masks (ChunkQuadtree, ChunkStitching); never renumber.
    /// Side effects: none.
    /// </remarks>
    public enum CubeEdge : byte
    {
        /// <summary>u == min.</summary>
        UMin = 0,

        /// <summary>u == max.</summary>
        UMax = 1,

        /// <summary>v == min.</summary>
        VMin = 2,

        /// <summary>v == max.</summary>
        VMax = 3,
    }

    /// <summary>Where an edge of one face continues on the adjacent face.</summary>
    /// <remarks>
    /// Pre: obtain from <see cref="CubeSphere.Adjacent"/>.
    /// Post: Face != the source face and != its opposite; Reversed tells whether the along-edge parameter flips sign.
    /// Side effects: none (immutable value).
    /// </remarks>
    public readonly struct CubeEdgeLink
    {
        /// <summary>Face on the other side of the edge.</summary>
        /// <remarks>Pre: none. Post: see type. Side effects: none.</remarks>
        public readonly CubeFace Face;

        /// <summary>The same physical edge, named from <see cref="Face"/>.</summary>
        /// <remarks>Pre: none. Post: see type. Side effects: none.</remarks>
        public readonly CubeEdge Edge;

        /// <summary>True when the along-edge parameter on <see cref="Face"/> equals minus the source parameter.</summary>
        /// <remarks>Pre: none. Post: see type. Side effects: none.</remarks>
        public readonly bool Reversed;

        internal CubeEdgeLink(CubeFace face, CubeEdge edge, bool reversed)
        {
            Face = face;
            Edge = edge;
            Reversed = reversed;
        }
    }

    /// <summary>Cube ↔ sphere mapping and cube-face topology shared by LOD, meshing and world generation.</summary>
    /// <remarks>
    /// Mapping: equi-angular ("tangent-adjusted") cube map. A face parameter u ∈ [−1, 1] is an angle
    /// θ = u·π/4 from the face centre, and the cube-plane coordinate is tan θ. This cuts the cell-area
    /// spread from 5.2× (plain normalised cube) to √2 ≈ 1.41×, with a closed-form inverse.
    /// REQ: docs/architecture/planet-tech.md#幾何：Cube-sphere 四叉樹 (「等面積修正映射」). An exactly equal-area
    /// cube map (QSC) was rejected: its derivative is discontinuous along the face diagonals, which shows up as
    /// creases in shading, and its inverse needs iteration.
    ///
    /// Seam contract: for any face edge and any parameter s exactly representable as k/2^n, the two faces sharing
    /// the edge produce bit-identical directions from <see cref="FaceToDirection"/>. This holds because the
    /// cube point is assembled from ±1, ±Warp(|s|) and 0 only, and every face axis is a signed unit basis vector, so
    /// both faces build the same float3 component by component. Breaking it (e.g. computing tan(π/4) instead of
    /// returning exactly 1, or warping −s separately from s) re-opens hairline cracks between faces.
    /// FENCE: Warp special-cases ±1 and mirrors negative input · on Mono a naive tan(s·π/4) happens to pass
    ///        CubeSphereTests.Shared_edges_are_bit_identical too (mutation probe 2026-11-06: float tan(π/4) rounds to 1
    ///        and the CRT tan is odd), so the guard is for Burst / reduced FloatPrecision, whose tan gives neither
    ///        guarantee; verify in P1-03 by running the seam test against the Burst-compiled path · ref: ADR-0005.
    ///        Mesh code in Burst must call this same function, not a copy.
    ///        Verified 2026-11-06 (P1-03): ChunkMeshTests.Face_seams_are_bit_identical_in_burst passes under Burst
    ///        FloatMode.Default / FloatPrecision.Standard on x64 with a runtime face index; Fast mode and ARM64 untested.
    ///
    /// Pre: none (static, stateless). Post: every member is a pure function of its arguments.
    /// Side effects: none; the axis/link tables are built once by the static initialiser.
    /// </remarks>
    public static class CubeSphere
    {
        private static readonly int3[] Normals =
        {
            new int3(1, 0, 0), new int3(-1, 0, 0),
            new int3(0, 1, 0), new int3(0, -1, 0),
            new int3(0, 0, 1), new int3(0, 0, -1),
        };

        // Chosen so cross(U, V) == Normal for every face (consistent outward winding for mesh code).
        private static readonly int3[] AxesU =
        {
            new int3(0, 0, -1), new int3(0, 0, 1),
            new int3(1, 0, 0), new int3(1, 0, 0),
            new int3(1, 0, 0), new int3(-1, 0, 0),
        };

        private static readonly int3[] AxesV =
        {
            new int3(0, 1, 0), new int3(0, 1, 0),
            new int3(0, 0, -1), new int3(0, 0, 1),
            new int3(0, 1, 0), new int3(0, 1, 0),
        };

        private static readonly CubeEdgeLink[] Links = BuildLinks();

        /// <summary>Number of cube faces.</summary>
        /// <remarks>Pre: none. Post: 6. Side effects: none.</remarks>
        public const int FaceCount = 6;

        /// <summary>Outward normal of <paramref name="face"/> as an integer axis.</summary>
        /// <remarks>Pre: face is a defined CubeFace. Post: a signed unit basis vector. Side effects: none.</remarks>
        public static int3 Normal(CubeFace face) => Normals[CheckFace(face)];

        /// <summary>Direction in which the u parameter increases on <paramref name="face"/>.</summary>
        /// <remarks>Pre: face is defined. Post: unit basis vector, orthogonal to Normal(face). Side effects: none.</remarks>
        public static int3 AxisU(CubeFace face) => AxesU[CheckFace(face)];

        /// <summary>Direction in which the v parameter increases on <paramref name="face"/>.</summary>
        /// <remarks>Pre: face is defined. Post: cross(AxisU, AxisV) == Normal. Side effects: none.</remarks>
        public static int3 AxisV(CubeFace face) => AxesV[CheckFace(face)];

        /// <summary>Equi-angular warp: face parameter → cube-plane coordinate, tan(s·π/4).</summary>
        /// <remarks>
        /// Pre: s is finite and |s| ≤ 1.
        /// Post: Warp(±1) == ±1 exactly; Warp(0) == 0; Warp(−s) == −Warp(s) exactly; non-decreasing. (Strictly
        ///       increasing in exact arithmetic, but adjacent floats near |s| = 1 can share a result, e.g. 0.983702f
        ///       and its successor — sol review F7. The grid s = −1 + k/2^19 is tested strictly increasing.)
        /// Side effects: none.
        /// </remarks>
        public static float Warp(float s)
        {
            Contract.Requires(math.isfinite(s) && math.abs(s) <= 1f, "s must be finite and within [-1, 1]");
            return WarpCore(s);
        }

        // Burst entry point (ADR-0006): no Contract calls, no managed statics. Callers validate inputs first.
        internal static float WarpCore(float s)
        {
            float a = math.abs(s);
            // FENCE: see type remarks — exact endpoints and mirrored evaluation make face seams bit-identical.
            float w = a >= 1f ? 1f : math.tan(a * (math.PI * 0.25f));
            return s < 0f ? -w : w;
        }

        /// <summary>Inverse of <see cref="Warp"/>: cube-plane coordinate → face parameter.</summary>
        /// <remarks>
        /// Pre: c is finite.
        /// Post: result ∈ [−1, 1]; Unwarp(Warp(s)) ≈ s.
        /// Side effects: none.
        /// </remarks>
        public static float Unwarp(float c)
        {
            Contract.Requires(math.isfinite(c), "c must be finite");
            return math.clamp(math.atan(c) * (4f / math.PI), -1f, 1f);
        }

        /// <summary>Unit direction of face parameters (u, v).</summary>
        /// <remarks>
        /// Pre: face is defined; u, v finite and within [−1, 1].
        /// Post: result is unit length; seam contract in type remarks.
        /// Side effects: none.
        /// </remarks>
        public static float3 FaceToDirection(CubeFace face, float u, float v)
        {
            int f = CheckFace(face);
            Contract.Requires(math.isfinite(u) && math.abs(u) <= 1f && math.isfinite(v) && math.abs(v) <= 1f,
                "u, v must be finite and within [-1, 1]");
            return FaceToDirectionCore(f, u, v);
        }

        // Burst entry point shared with FaceToDirection, so the seam contract has one implementation (FENCE above).
        // Axes come from switches, not the static arrays: touching CubeSphere's static fields would make Burst
        // evaluate the whole static constructor, including BuildLinks, which it cannot.
        internal static float3 FaceToDirectionCore(int face, float u, float v)
        {
            float3 cube = AxisNormalCore(face) + AxisUCore(face) * WarpCore(u) + AxisVCore(face) * WarpCore(v);
            return math.normalize(cube);
        }

        private static float3 AxisNormalCore(int f)
        {
            switch (f)
            {
                case 0: return new float3(1f, 0f, 0f);
                case 1: return new float3(-1f, 0f, 0f);
                case 2: return new float3(0f, 1f, 0f);
                case 3: return new float3(0f, -1f, 0f);
                case 4: return new float3(0f, 0f, 1f);
                default: return new float3(0f, 0f, -1f);
            }
        }

        private static float3 AxisUCore(int f)
        {
            switch (f)
            {
                case 0: return new float3(0f, 0f, -1f);
                case 1: return new float3(0f, 0f, 1f);
                case 2:
                case 3:
                case 4: return new float3(1f, 0f, 0f);
                default: return new float3(-1f, 0f, 0f);
            }
        }

        private static float3 AxisVCore(int f)
        {
            switch (f)
            {
                case 2: return new float3(0f, 0f, -1f);
                case 3: return new float3(0f, 0f, 1f);
                default: return new float3(0f, 1f, 0f);
            }
        }

        /// <summary>Face and parameters (u, v) whose <see cref="FaceToDirection"/> is <paramref name="direction"/>.</summary>
        /// <remarks>
        /// Pre: direction is finite and non-zero (need not be unit).
        /// Post: u, v ∈ [−1, 1]; FaceToDirection(face, u, v) ≈ normalize(direction). On a seam the face with the
        ///       lower enum value among the dominant axes wins (X before Y before Z), so the choice is deterministic.
        /// Side effects: none.
        /// </remarks>
        public static CubeFace DirectionToFace(float3 direction, out float u, out float v)
        {
            Contract.Requires(math.all(math.isfinite(direction)) && math.any(direction != 0f),
                "direction must be finite and non-zero");
            // REQ: sol review F3 — (1e-30, 0, 0) is finite and non-zero but its lengthsq underflows to 0.
            direction = VectorMath.Rescale(direction);
            float3 a = math.abs(direction);
            CubeFace face;
            if (a.x >= a.y && a.x >= a.z)
            {
                face = direction.x >= 0f ? CubeFace.PosX : CubeFace.NegX;
            }
            else if (a.y >= a.z)
            {
                face = direction.y >= 0f ? CubeFace.PosY : CubeFace.NegY;
            }
            else
            {
                face = direction.z >= 0f ? CubeFace.PosZ : CubeFace.NegZ;
            }

            int f = (int)face;
            float n = math.dot(direction, (float3)Normals[f]);
            u = Unwarp(math.dot(direction, (float3)AxesU[f]) / n);
            v = Unwarp(math.dot(direction, (float3)AxesV[f]) / n);
            return face;
        }

        /// <summary>The face and edge on the other side of <paramref name="edge"/> of <paramref name="face"/>.</summary>
        /// <remarks>
        /// Pre: face and edge are defined.
        /// Post: Adjacent(result.Face, result.Edge) leads back to (face, edge) with the same Reversed flag.
        /// Side effects: none.
        /// </remarks>
        public static CubeEdgeLink Adjacent(CubeFace face, CubeEdge edge)
        {
            int f = CheckFace(face);
            Contract.Requires((uint)edge < 4u, "edge must be a defined CubeEdge");
            return Links[f * 4 + (int)edge];
        }

        /// <summary>True when the along-edge coordinate of <paramref name="edge"/> is u (V edges) rather than v (U edges).</summary>
        /// <remarks>Pre: edge is defined. Post: true for VMin/VMax. Side effects: none.</remarks>
        public static bool RunsAlongU(CubeEdge edge)
        {
            Contract.Requires((uint)edge < 4u, "edge must be a defined CubeEdge");
            return edge == CubeEdge.VMin || edge == CubeEdge.VMax;
        }

        private static int CheckFace(CubeFace face)
        {
            Contract.Requires((uint)face < FaceCount, "face must be a defined CubeFace");
            return (int)face;
        }

        // Derived from the axis tables instead of hand-written: 24 hand-typed entries are easy to get subtly wrong,
        // while this derivation is checked by the round-trip test.
        private static CubeEdgeLink[] BuildLinks()
        {
            var links = new CubeEdgeLink[FaceCount * 4];
            for (int f = 0; f < FaceCount; f++)
            {
                for (int e = 0; e < 4; e++)
                {
                    var edge = (CubeEdge)e;
                    bool alongU = RunsAlongU(edge);
                    int sign = edge == CubeEdge.UMax || edge == CubeEdge.VMax ? 1 : -1;
                    int3 outward = (alongU ? AxesV[f] : AxesU[f]) * sign;
                    int3 along = alongU ? AxesU[f] : AxesV[f];

                    int g = FaceOfNormal(outward);
                    // On face g our normal is one of ±U_g / ±V_g; that names the shared edge from g's side.
                    int3 back = Normals[f];
                    CubeEdge otherEdge;
                    int3 otherAlong;
                    if (math.all(back == AxesU[g]))
                    {
                        otherEdge = CubeEdge.UMax;
                        otherAlong = AxesV[g];
                    }
                    else if (math.all(back == -AxesU[g]))
                    {
                        otherEdge = CubeEdge.UMin;
                        otherAlong = AxesV[g];
                    }
                    else if (math.all(back == AxesV[g]))
                    {
                        otherEdge = CubeEdge.VMax;
                        otherAlong = AxesU[g];
                    }
                    else
                    {
                        otherEdge = CubeEdge.VMin;
                        otherAlong = AxesU[g];
                    }

                    bool reversed = math.dot(along, otherAlong) < 0;
                    links[f * 4 + e] = new CubeEdgeLink((CubeFace)g, otherEdge, reversed);
                }
            }

            return links;
        }

        private static int FaceOfNormal(int3 n)
        {
            for (int g = 0; g < FaceCount; g++)
            {
                if (math.all(Normals[g] == n))
                {
                    return g;
                }
            }

            throw new System.InvalidOperationException("axis tables are inconsistent");
        }
    }
}
