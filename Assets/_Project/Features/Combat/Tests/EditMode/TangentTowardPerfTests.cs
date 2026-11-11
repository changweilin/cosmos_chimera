using System;
using System.Diagnostics;
using System.IO;
using Cosmimera.Planet;
using NUnit.Framework;
using Unity.Mathematics;

namespace Cosmimera.Combat.Tests
{
    // REQ: tech-debt D0-E4 · docs/reviews/D0-E4-proposal-sol.md acceptance item 5 (hot-path cost, no managed allocation).
    // Timing is logged, not asserted (testing.md: no exact balance/perf values); the allocation invariant is asserted.
    public sealed class TangentTowardPerfTests
    {
        private sealed class FlatSurface : IEncounterSurface
        {
            public float GroundAltitude(float3 direction) => 0f;

            public bool IsSea(float3 direction) => false;

            public bool IsBlocked(float3 direction, float clearanceM) => false;
        }

        private const int Calls = 2_000_000;

        // The pre-D0-E4 single-pass float projection, kept only as the timing baseline.
        private static float3 OldFloat(float3 center, float3 from, float3 to, float3 fallback)
        {
            float3 up = math.normalize(from - center);
            float3 t = to - from;
            t -= math.dot(t, up) * up;
            float len = math.length(t);
            if (len > 1e-6f)
            {
                return t / len;
            }

            float3 f = fallback - math.dot(fallback, up) * up;
            return math.normalize(f);
        }

        [Test]
        public void TangentToward_cost_and_allocation()
        {
            var balance = CombatBalance.FromJson(File.ReadAllText(BalanceFixture.RepoPath("Data", "balance.json")));
            var center = new float3(0f, 0f, 0f);
            var e = new Encounter(new PlanetConfig(640f, center), balance, new FlatSurface(), 1);
            var rng = new Unity.Mathematics.Random(559);
            var froms = new float3[256];
            var tos = new float3[256];
            for (int i = 0; i < froms.Length; i++)
            {
                float3 dir = math.normalize(rng.NextFloat3(-1f, 1f));
                PlanetFrame f = PlanetFrame.FromUp(dir);
                froms[i] = dir * 640.6f;
                tos[i] = dir * 640.6f + f.East * rng.NextFloat(-6f, 6f) + f.North * rng.NextFloat(-6f, 6f) + dir * rng.NextFloat(-1f, 1f);
            }

            float3 sink = float3.zero;
            for (int i = 0; i < 100_000; i++)
            {
                sink += e.TangentToward(froms[i & 255], tos[i & 255], froms[(i + 1) & 255]);
                sink += OldFloat(center, froms[i & 255], tos[i & 255], froms[(i + 1) & 255]);
            }

            GC.Collect();
            long a0 = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < Calls; i++)
            {
                sink += e.TangentToward(froms[i & 255], tos[i & 255], froms[(i + 1) & 255]);
            }

            sw.Stop();
            long newAlloc = GC.GetAllocatedBytesForCurrentThread() - a0;
            double newNs = sw.Elapsed.TotalMilliseconds * 1e6 / Calls;

            sw.Restart();
            for (int i = 0; i < Calls; i++)
            {
                sink += OldFloat(center, froms[i & 255], tos[i & 255], froms[(i + 1) & 255]);
            }

            sw.Stop();
            double oldNs = sw.Elapsed.TotalMilliseconds * 1e6 / Calls;

            UnityEngine.Debug.Log($"[D0-E4 perf] TangentToward new={newNs:F1} ns/call, old-float baseline={oldNs:F1} ns/call, " +
                                  $"new allocated={newAlloc} B over {Calls} calls (sink {sink.x:F1})");
            Assert.That(newAlloc, Is.EqualTo(0L), "TangentToward must not allocate managed memory");
        }
    }
}
