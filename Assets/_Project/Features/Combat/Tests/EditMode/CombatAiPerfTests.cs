using System.Diagnostics;
using Cosmimera.GameData;
using Cosmimera.Planet;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine.Profiling;

namespace Cosmimera.Combat.Tests
{
    public sealed class CombatAiPerfTests
    {
        [Test]
        public void Eight_simultaneous_ai_units_run_within_frame_budget_with_zero_allocations()
        {
            var planet = new PlanetConfig(640f, float3.zero);
            var tuning = new AiTuning(800, 500, 900, 6000);
            float3 player = new float3(0f, 640f, 0f);

            var moves = new[]
            {
                new AiMoveOption(0, AiMoveTargeting.Hostile, 12000, 80, true),
                new AiMoveOption(1, AiMoveTargeting.Hostile, 2400, 45, true),
                new AiMoveOption(2, AiMoveTargeting.Hostile, 8000, 60, false),
                new AiMoveOption(3, AiMoveTargeting.Self, 0, 0, true, AiMoveCategory.Heal, 25),
            };
            var enemies = new[]
            {
                EnemyAt(10, math.normalize(new float3(0.02f, 1f, 0f)) * 640f),
                EnemyAt(11, math.normalize(new float3(-0.02f, 1f, 0.01f)) * 640f),
                EnemyAt(12, math.normalize(new float3(0.01f, 1f, -0.02f)) * 640f),
                EnemyAt(13, math.normalize(new float3(-0.01f, 1f, -0.02f)) * 640f),
            };
            var styles = new[] { AiStyle.KeepDistance, AiStyle.Flank, AiStyle.Charge, AiStyle.Hold };
            var cmd = new AiCommand(AiCommandKind.Free, CombatAi.NoTarget);

            // Warmup JIT and Stopwatch before measuring allocations and elapsed time.
            var warmup = new AiInput(math.normalize(new float3(0.005f, 1f, 0f)) * 640f, AiStyle.Charge, AiRole.Main,
                in cmd, CombatAi.NoTarget, player, false, default, 80, 100, 90, 100);
            CombatAi.Decide(in planet, tuning, in warmup, enemies, moves);
            var sw = new Stopwatch();

            long beforeBytes = Profiler.GetTotalAllocatedMemoryLong();
            sw.Start();
            int ticks = 200;
            bool allUnit = true;
            for (int t = 0; t < ticks; t++)
            {
                for (int u = 0; u < 8; u++)
                {
                    var input = new AiInput(math.normalize(new float3(0.003f * (u + 1), 1f, 0.002f * u)) * 640f,
                        styles[u % 4], u % 2 == 0 ? AiRole.Main : AiRole.Support, in cmd, CombatAi.NoTarget,
                        player, false, default, 70, 100, 90, 100);
                    AiDecision d = CombatAi.Decide(in planet, tuning, in input, enemies, moves);
                    if (math.lengthsq(d.DestinationDirection) <= 0.99f)
                    {
                        allUnit = false;
                    }
                }
            }

            sw.Stop();
            long allocated = Profiler.GetTotalAllocatedMemoryLong() - beforeBytes;
            Assert.IsTrue(allUnit, "all destinations must be unit length");
            Assert.That(allocated, Is.EqualTo(0), "CombatAi.Decide must allocate 0 bytes on the hot path");
            Assert.That(sw.Elapsed.TotalMilliseconds / ticks, Is.LessThan(2.0),
                "8-unit AI tick should stay well below 2 ms per tick");
        }

        private static AiEnemy EnemyAt(int id, float3 pos)
        {
            float3 up = math.normalize(pos);
            float3 fwd = PlanetFrame.FromUp(up).North;
            return new AiEnemy(id, pos, fwd, true);
        }
    }
}
