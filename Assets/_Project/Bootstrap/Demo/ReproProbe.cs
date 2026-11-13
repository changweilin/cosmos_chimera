using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Cosmimera.Core.Random;
using Cosmimera.GameData;
using Cosmimera.Planet;
using Cosmimera.WorldGen;
using Unity.Mathematics;

namespace Cosmimera.Bootstrap.Demo
{
    /// <summary>
    /// P3-10 cross-machine / cross-backend reproducibility probe: digests everything a client generates locally
    /// from a seed, so two runs of the same build (or Mono vs IL2CPP) can be diffed line by line.
    /// REQ: docs/roadmap/phase-3.md#P3-10, docs/open-questions.md#Q-T6.
    /// </summary>
    /// <remarks>
    /// Each line carries two height digests on purpose: "bits" (raw float bits, Q-T6 option a) and "mm"
    /// (rounded to 1 mm, Q-T6 option b). Which of the two survives across machines/backends is the evidence Q-T6 needs.
    /// The RunPlan digest is the content that gameplay and netcode must agree on whichever option wins.
    /// </remarks>
    public static class ReproProbe
    {
        /// <summary>Seeds probed for every planet type (cover small, 32-bit edge and >32-bit values).</summary>
        /// <remarks>Pre: none. Post: fixed list; changing it invalidates stored reports. Side effects: none.</remarks>
        public static readonly ulong[] Seeds = { 1UL, 42UL, 12345UL, 0xFFFFFFFFUL, 0x9E3779B97F4A7C15UL };

        /// <summary>Height samples per seed.</summary>
        /// <remarks>Pre: none. Post: > 0. Side effects: none.</remarks>
        public const int SampleCount = 2048;

        /// <summary>Builds the probe report.</summary>
        /// <remarks>
        /// Pre: all arguments non-null (environment and sampleDump optional); types non-empty with no null elements;
        ///      wildPool non-empty, no nulls, each with mastery, including a form of every stage in <see cref="BossStages"/>;
        ///      radiusFor returns a radius RunPlanner accepts for every boss size.
        /// Post: deterministic text for identical inputs and identical floating-point behaviour; one header block,
        ///      then one line per (planet type sorted by Id ordinal, boss stage in <see cref="BossStages"/> order, seed in
        ///      <see cref="Seeds"/> order).
        ///       When <paramref name="sampleDump"/> is non-null, one "type stage seed k baseBits surfBits" line per sample is
        ///       appended to it (raw float bits as hex) so the size of a divergence can be measured, not just detected.
        /// Side effects: allocates (planet generation per line); appends to sampleDump; no I/O.
        /// </remarks>
        public static string Report(IReadOnlyList<PlanetTypeDef> types, IReadOnlyList<CreatureDef> wildPool,
            RunPlanVocabulary vocabulary, Func<PlanetSize, float> radiusFor, string environment,
            StringBuilder sampleDump = null)
        {
            Core.Contracts.Contract.Requires(types != null && types.Count > 0, "types must not be empty");
            Core.Contracts.Contract.Requires(wildPool != null && vocabulary != null && radiusFor != null,
                "wildPool, vocabulary and radiusFor are required");
            foreach (PlanetTypeDef type in types)
            {
                Core.Contracts.Contract.Requires(type != null, "types must not contain null elements");
            }

            var poolStages = new HashSet<CreatureStage>();
            foreach (CreatureDef creature in wildPool)
            {
                Core.Contracts.Contract.Requires(creature != null && creature.Mastery.Count > 0,
                    "wild pool entries need a main element");
                poolStages.Add(creature.Stage);
            }

            foreach (CreatureStage stage in BossStages)
            {
                Core.Contracts.Contract.Requires(poolStages.Contains(stage), "wild pool needs a form of every probed boss stage");
            }

            var sorted = new List<PlanetTypeDef>(types);
            sorted.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            var sb = new StringBuilder();
            sb.Append("# cosmimera repro-probe v6\n");
            sb.Append("# env ").Append(environment ?? "-").Append('\n');
            sb.Append("# PlanetHeight.GeneratorVersion ").Append(PlanetHeight.GeneratorVersion.ToString(CultureInfo.InvariantCulture))
                .Append(" RunPlanner.GeneratorVersion ").Append(RunPlanner.GeneratorVersion.ToString(CultureInfo.InvariantCulture))
                .Append(" LandformSynth.GeneratorVersion ").Append(LandformSynth.GeneratorVersion.ToString(CultureInfo.InvariantCulture))
                .Append(" wildPool ").Append(wildPool.Count.ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("# landingBits: x,y,z raw float32 hex; policy b compares normalized directions at radius (chord metres), tolerance 0.003 m\n");
            sb.Append("# type stage seed radius baseBits baseMm surfBits surfMm seaCount plan planDiscrete landingBits\n");
            foreach (PlanetTypeDef type in sorted)
            {
                foreach (CreatureStage stage in BossStages)
                {
                    foreach (ulong seed in Seeds)
                    {
                        AppendLine(sb, type, stage, seed, radiusFor(BossDraw.SizeFor(stage)), wildPool, vocabulary,
                            sampleDump);
                    }
                }
            }

            return sb.ToString();
        }

        /// <summary>Boss stages probed for every (type, seed); each one fixes a planet size, template count and form cap.</summary>
        /// <remarks>
        /// Pre: none. Post: every stage BossDraw can return, ascending; changing it invalidates stored reports.
        /// Side effects: none.
        /// </remarks>
        // v5 (tech-debt D0-B2): v4 pinned the adult boss, so the R=640 asteroid/moon planets and the capped wild pools that
        // low-level players actually get were never measured. All stages are probed rather than mirroring the player-level
        // draw, which would make the report depend on save state.
        public static readonly CreatureStage[] BossStages =
            { CreatureStage.Juvenile, CreatureStage.Adolescent, CreatureStage.Adult };

        private static void AppendLine(StringBuilder sb, PlanetTypeDef type, CreatureStage stage, ulong seed, float radius,
            IReadOnlyList<CreatureDef> wildPool, RunPlanVocabulary vocabulary, StringBuilder sampleDump)
        {
            // Mirrors RunSession.Configure exactly; if that composition changes, this must change with it or the
            // probe stops measuring what clients actually generate.
            var config = new PlanetConfig(radius, float3.zero);
            PlanetHeight height = PlanetHeight.ForPlanet(config, seed);
            PlanetTemplates planetTemplates = TemplatePlanner.Build(config, seed, type.TerrainTags, BossDraw.SizeFor(stage));

            // PlanetTemplates owns Persistent native buffers; in a run PlanetRig disposes them, here nobody else does.
            try
            {
                Func<float3, bool> isLand = null;
                if (planetTemplates != null)
                {
                    TemplateView view = planetTemplates.View;
                    isLand = d => !view.IsSea(d);
                }

                RunPlan plan = RunPlanner.Generate(config, seed, type, stage, wildPool, vocabulary, isLand);

                ulong baseBits = Fnv.Offset, baseMm = Fnv.Offset, surfBits = Fnv.Offset, surfMm = Fnv.Offset;
                int sea = 0;
                var rng = new Pcg32(seed, 0x50333130UL); // stream id "P310": independent of every generator stream
                for (int k = 0; k < SampleCount; k++)
                {
                    float3 d = RandomDirection(ref rng);
                    float b = height.Sample(d);
                    float s = planetTemplates != null ? planetTemplates.Sample(height, d) : b;
                    Fnv.Mix(ref baseBits, math.asuint(b));
                    Fnv.Mix(ref baseMm, (uint)(int)math.round(b * 1000f));
                    Fnv.Mix(ref surfBits, math.asuint(s));
                    Fnv.Mix(ref surfMm, (uint)(int)math.round(s * 1000f));
                    sampleDump?.Append(type.Id).Append(' ').Append(StageToken(stage)).Append(' ').Append(seed.ToString(CultureInfo.InvariantCulture)).Append(' ')
                        .Append(k.ToString(CultureInfo.InvariantCulture)).Append(' ')
                        .Append(math.asuint(b).ToString("x8")).Append(' ').Append(math.asuint(s).ToString("x8")).Append('\n');
                    if (planetTemplates != null && planetTemplates.View.IsSea(d))
                    {
                        sea++;
                    }
                }

                sb.Append(type.Id).Append(' ')
                    .Append(StageToken(stage)).Append(' ')
                    .Append(seed.ToString(CultureInfo.InvariantCulture)).Append(' ')
                    .Append(radius.ToString("R", CultureInfo.InvariantCulture)).Append(' ')
                    .Append(baseBits.ToString("x16")).Append(' ')
                    .Append(baseMm.ToString("x16")).Append(' ')
                    .Append(surfBits.ToString("x16")).Append(' ')
                    .Append(surfMm.ToString("x16")).Append(' ')
                    .Append(sea.ToString(CultureInfo.InvariantCulture)).Append(' ')
                    .Append(RunPlanner.Hash(plan).ToString("x16")).Append(' ')
                    .Append(DiscreteHash(plan).ToString("x16")).Append(' ')
                    // ADR-0007: landing is geometry, so preserve bits for same-backend checks and let policy b
                    // measure displacement without contaminating the authoritative discrete digest.
                    .Append(math.asuint(plan.LandingDirection.x).ToString("x8")).Append(',')
                    .Append(math.asuint(plan.LandingDirection.y).ToString("x8")).Append(',')
                    .Append(math.asuint(plan.LandingDirection.z).ToString("x8")).Append('\n');
            }
            finally
            {
                planetTemplates?.Dispose();
            }
        }

        // Spelled out rather than Enum.ToString so renaming an enum member cannot silently change the report format.
        private static string StageToken(CreatureStage stage)
        {
            switch (stage)
            {
                case CreatureStage.Juvenile: return "juvenile";
                case CreatureStage.Adolescent: return "adolescent";
                case CreatureStage.Adult: return "adult";
                default: throw new ArgumentOutOfRangeException(nameof(stage), stage, "not a boss stage");
            }
        }

        // Same sampler as PlanetHeightTests. The directions themselves go through float sqrt/divide, so a backend
        // difference can already show up here; that is part of what the probe measures, not a flaw in it.
        private static float3 RandomDirection(ref Pcg32 rng)
        {
            while (true)
            {
                var p = new float3(rng.NextFloat(), rng.NextFloat(), rng.NextFloat()) * 2f - 1f;
                float l = math.lengthsq(p);
                if (l > 1e-4f && l <= 1f)
                {
                    return p / math.sqrt(l);
                }
            }
        }

        // RunPlanner.Hash includes directions rounded to 1e-4, so a sub-ulp difference near a rounding edge flips it
        // even when every gameplay decision is identical. This digest keeps only the discrete content (ids, indices,
        // roles, quest), which is what clients must agree on for netcode (Q-T6 option b).
        private static ulong DiscreteHash(RunPlan plan)
        {
            ulong h = Fnv.Offset;
            Fnv.Mix(ref h, plan.ChainId);
            Fnv.Mix(ref h, (uint)plan.LandingRegion);
            Fnv.Mix(ref h, (uint)plan.BossRegion);
            foreach (RunRegion r in plan.Regions)
            {
                Fnv.Mix(ref h, r.ThemeId);
                Fnv.Mix(ref h, r.AffixId ?? "-");
                Fnv.Mix(ref h, (uint)r.Tier);
                foreach (int n in r.Neighbors)
                {
                    Fnv.Mix(ref h, (uint)n);
                }
            }

            foreach (RunSpawn s in plan.Spawns)
            {
                Fnv.Mix(ref h, (uint)s.Region);
                Fnv.Mix(ref h, (uint)s.Group);
                Fnv.Mix(ref h, s.CreatureId);
                Fnv.Mix(ref h, (uint)s.Role);
            }

            foreach (RunQuestStep q in plan.Quest)
            {
                Fnv.Mix(ref h, (uint)q.Kind);
                Fnv.Mix(ref h, (uint)q.Region);
                Fnv.Mix(ref h, (uint)q.Count);
                Fnv.Mix(ref h, (uint)q.Element);
                Fnv.Mix(ref h, (uint)(q.TargetSpawn + 1));
            }

            return h;
        }

        private static class Fnv
        {
            public const ulong Offset = 14695981039346656037UL;

            public static void Mix(ref ulong h, string v)
            {
                foreach (char c in v)
                {
                    Mix(ref h, c);
                }

                Mix(ref h, 0xFFFFFFFFu); // terminator: "ab"+"c" must not collide with "a"+"bc"
            }

            public static void Mix(ref ulong h, uint v)
            {
                for (int b = 0; b < 4; b++)
                {
                    h = unchecked((h ^ ((v >> (8 * b)) & 0xFF)) * 1099511628211UL);
                }
            }
        }
    }
}
