using System.Text;
using Cosmimera.Core.Contracts;
using Cosmimera.Core.Random;
using Cosmimera.GameData;

namespace Cosmimera.WorldGen
{
    /// <summary>
    /// Integer bounded normal draw shared by individual and per-action wander sampling.
    /// REQ: docs/design/creature-ecology.md#平均值與變異度.
    /// </summary>
    /// <remarks>
    /// Irwin–Hall: the sum of 12 uniform 16-bit integers minus 6·65536 approximates N(0, 65536²) with no floating point,
    /// so every backend draws the same value and every call consumes exactly 12 numbers even when sd is 0 (the stream
    /// position must not depend on data). Pre: none beyond the method's. Side effects: none.
    /// </remarks>
    public static class BoundedNormal
    {
        private const int Draws = 12;
        private const long One = 65536;

        /// <summary>Draws round_toward_zero(mean + sd·z) clamped to [min, max].</summary>
        /// <remarks>
        /// Pre: sd &gt;= 0; min &lt;= max.
        /// Post: min &lt;= result &lt;= max; sd == 0 → clamp(mean).
        /// Side effects: advances rng by exactly 12 draws.
        /// </remarks>
        public static int Sample(ref Pcg32 rng, long mean, int sd, int min, int max)
        {
            Contract.Requires(sd >= 0 && min <= max, "BoundedNormal needs sd >= 0 and min <= max");
            long sum = 0;
            for (int i = 0; i < Draws; i++)
            {
                sum += rng.NextUInt() & 0xFFFFu;
            }

            long z = sum - Draws / 2 * One;
            long v = mean + sd * z / One;
            return (int)(v < min ? min : v > max ? max : v);
        }
    }

    /// <summary>
    /// One wild individual's ecology: niche, habit and the individual means of its wander quantities.
    /// REQ: docs/design/creature-ecology.md#生成順序.
    /// </summary>
    /// <remarks>Pre: build through the constructor or <see cref="Draw"/>. Post: immutable. Side effects: none.</remarks>
    public sealed class WildIndividual
    {
        // Distinct per-purpose stream ids so niche/habit, individual means and wander actions never share numbers.
        private const ulong NicheStream = 'N';
        private const ulong IndividualStream = 'I';

        /// <summary>Creates an individual from already-drawn values (tests, saves).</summary>
        /// <remarks>
        /// Pre: niche non-null; habit is one of niche.Habits; each mean inside its niche distribution's [Min, Max].
        /// Post: properties as given. Side effects: allocates.
        /// </remarks>
        public WildIndividual(NicheDef niche, Habit habit, int speedPermilleMean, int moveMsMean, int pauseMsMean,
            ulong streamKey)
        {
            Contract.Requires(niche != null, "niche must not be null");
            Contract.Requires(Allows(niche, habit), "habit must be allowed by the niche");
            Contract.Requires(InRange(niche.SpeedPermille, speedPermilleMean) && InRange(niche.MoveMs, moveMsMean) &&
                InRange(niche.PauseMs, pauseMsMean), "individual means must lie inside the niche distributions");
            Niche = niche;
            Habit = habit;
            SpeedPermilleMean = speedPermilleMean;
            MoveMsMean = moveMsMean;
            PauseMsMean = pauseMsMean;
            StreamKey = streamKey;
        }

        /// <summary>The individual's niche.</summary>
        /// <remarks>Pre: none. Post: non-null. Side effects: none.</remarks>
        public NicheDef Niche { get; }

        /// <summary>The individual's habit (sets its territory radius).</summary>
        /// <remarks>Pre: none. Post: one of Niche.Habits. Side effects: none.</remarks>
        public Habit Habit { get; }

        /// <summary>Individual mean wander speed (permille of combat base speed).</summary>
        /// <remarks>Pre: none. Post: inside Niche.SpeedPermille [Min, Max]. Side effects: none.</remarks>
        public int SpeedPermilleMean { get; }

        /// <summary>Individual mean move budget per leg (ms).</summary>
        /// <remarks>Pre: none. Post: inside Niche.MoveMs [Min, Max]. Side effects: none.</remarks>
        public int MoveMsMean { get; }

        /// <summary>Individual mean pause (ms).</summary>
        /// <remarks>Pre: none. Post: inside Niche.PauseMs [Min, Max]. Side effects: none.</remarks>
        public int PauseMsMean { get; }

        /// <summary>Seed of this individual's own route/action stream (independent of every other unit and of combat).</summary>
        /// <remarks>Pre: none. Post: as given / derived. Side effects: none.</remarks>
        public ulong StreamKey { get; }

        /// <summary>Derives the per-individual seed from the run seed and a stable individual key (spawn index, instance id).</summary>
        /// <remarks>Pre: none. Post: same inputs → same value; SplitMix64 finaliser so nearby keys decorrelate. Side effects: none.</remarks>
        public static ulong KeyFor(ulong seed, ulong individualKey)
        {
            unchecked
            {
                ulong z = seed + 0x9E3779B97F4A7C15UL * (individualKey + 1);
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }

        /// <summary>
        /// Draws niche (weighted among <paramref name="form"/>'s candidates allowed in <paramref name="habitat"/>), then
        /// habit from that niche, then the individual means. REQ: creature-ecology.md#生成順序.
        /// </summary>
        /// <remarks>
        /// Pre: form non-null; habitat null (no habitat filter: evolution redraws and old-save migration) or a landform id.
        /// Post: same (form, habitat, streamKey) → same individual; StreamKey == streamKey.
        /// Side effects: allocates. Throws ContractException in every build when no candidate is allowed in the habitat
        ///          (a data error: creature-ecology.md forbids a default niche).
        /// </remarks>
        public static WildIndividual Draw(CreatureDef form, string habitat, ulong streamKey)
        {
            Contract.Requires(form != null, "form must not be null");
            var rng = new Pcg32(streamKey, NicheStream);
            long total = 0;
            foreach (NicheCandidate c in form.Niches)
            {
                if (habitat == null || c.Niche.Allows(habitat))
                {
                    total += c.Weight;
                }
            }

            if (total == 0)
            {
                // Not Contract.Requires: Release strips it, and a silent default niche is exactly what the spec forbids.
                throw new ContractException(new StringBuilder("no candidate niche for ").Append(form.Id)
                    .Append(" in habitat '").Append(habitat).Append('\'').ToString());
            }

            NicheDef niche = null;
            long pick = PickBelow(ref rng, total);
            foreach (NicheCandidate c in form.Niches)
            {
                if (habitat != null && !c.Niche.Allows(habitat))
                {
                    continue;
                }

                if (pick < c.Weight)
                {
                    niche = c.Niche;
                    break;
                }

                pick -= c.Weight;
            }

            long habitTotal = 0;
            foreach (HabitWeight h in niche.Habits)
            {
                habitTotal += h.Weight;
            }

            Habit habit = niche.Habits[0].Habit;
            long habitPick = PickBelow(ref rng, habitTotal);
            foreach (HabitWeight h in niche.Habits)
            {
                if (habitPick < h.Weight)
                {
                    habit = h.Habit;
                    break;
                }

                habitPick -= h.Weight;
            }

            var ind = new Pcg32(streamKey, IndividualStream);
            int speed = Individual(ref ind, niche.SpeedPermille);
            int move = Individual(ref ind, niche.MoveMs);
            int pause = Individual(ref ind, niche.PauseMs);
            return new WildIndividual(niche, habit, speed, move, pause, streamKey);
        }

        private static int Individual(ref Pcg32 rng, WanderDistribution d)
            => BoundedNormal.Sample(ref rng, d.Mean, d.IndividualSd, d.Min, d.Max);

        // Weights are Int32 each but a sum may exceed uint; two draws make a 64-bit value and the modulo bias is < 2^-31.
        private static long PickBelow(ref Pcg32 rng, long total)
        {
            ulong r = ((ulong)rng.NextUInt() << 32) | rng.NextUInt();
            return (long)(r % (ulong)total);
        }

        private static bool Allows(NicheDef niche, Habit habit)
        {
            foreach (HabitWeight h in niche.Habits)
            {
                if (h.Habit == habit)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool InRange(WanderDistribution d, int v) => v >= d.Min && v <= d.Max;
    }
}
