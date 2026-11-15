using System;
using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.Core.Random;

namespace Cosmimera.Capture
{
    /// <summary>One weighted hatch outcome of an egg.</summary>
    /// <remarks>Pre: speciesId non-empty, weight &gt; 0. Post: immutable. Side effects: none.</remarks>
    public readonly struct EggOutcome
    {
        /// <summary>Species that may hatch.</summary>
        public string SpeciesId { get; }

        /// <summary>Relative weight in the egg's pool.</summary>
        public int Weight { get; }

        /// <summary>Creates an outcome.</summary>
        /// <remarks>Pre: speciesId non-empty; weight &gt; 0. Post: properties set. Side effects: none.</remarks>
        public EggOutcome(string speciesId, int weight)
        {
            Contract.Requires(!string.IsNullOrEmpty(speciesId), "speciesId required");
            Contract.Requires(weight > 0, "weight must be > 0");
            SpeciesId = speciesId;
            Weight = weight;
        }
    }

    /// <summary>Static egg definition: which species it can hatch into.</summary>
    /// <remarks>Source is the egg data table (Data/eggs, not yet imported — tech-debt). Immutable.</remarks>
    public sealed class EggDef
    {
        private readonly EggOutcome[] _outcomes;
        private readonly int _totalWeight;

        /// <summary>Stable egg id (snake_case, never reused).</summary>
        public string Id { get; }

        /// <summary>Weighted outcomes.</summary>
        /// <remarks>Pre: none. Post: non-empty. Side effects: none.</remarks>
        public IReadOnlyList<EggOutcome> Outcomes => _outcomes;

        /// <summary>Creates a definition.</summary>
        /// <remarks>
        /// Pre: id non-empty; outcomes non-empty with unique species ids.
        /// Post: outcomes copied. Side effects: none.
        /// </remarks>
        public EggDef(string id, IReadOnlyList<EggOutcome> outcomes)
        {
            Contract.Requires(!string.IsNullOrEmpty(id), "id required");
            Contract.Requires(outcomes != null && outcomes.Count > 0, "outcomes required");
            var seen = new HashSet<string>();
            _outcomes = new EggOutcome[outcomes.Count];
            for (int i = 0; i < outcomes.Count; i++)
            {
                Contract.Requires(seen.Add(outcomes[i].SpeciesId), "duplicate species in egg pool");
                _outcomes[i] = outcomes[i];
                _totalWeight += outcomes[i].Weight;
            }
            Id = id;
        }

        /// <summary>Picks a species by weight.</summary>
        /// <remarks>
        /// Pre: rng seeded; caller owns the stream so peers roll identically.
        /// Post: a species id from <see cref="Outcomes"/>. Side effects: advances rng by exactly one draw.
        /// </remarks>
        public string Roll(ref Pcg32 rng)
        {
            uint pick = rng.NextUInt((uint)_totalWeight);
            for (int i = 0; i < _outcomes.Length; i++)
            {
                if (pick < (uint)_outcomes[i].Weight) return _outcomes[i].SpeciesId;
                pick -= (uint)_outcomes[i].Weight;
            }
            throw new InvalidOperationException("unreachable: pick is below total weight");
        }
    }

    /// <summary>Hatchery tuning. REQ: lobby-meta.md#收服與蛋 (Q-M3). </summary>
    /// <remarks>Pre: see constructor. Post: immutable. Side effects: none.</remarks>
    public readonly struct HatchConfig
    {
        /// <summary>Progress that hatches an egg: 100% in parts per million.</summary>
        public const long FullProgressPpm = 1_000_000;

        /// <summary>Q-M3: one battle = 2%.</summary>
        public const int DefaultBattlePercent = 2;

        /// <summary>Q-M3: defeating a boss = +10%.</summary>
        public const int DefaultBossPercent = 10;

        /// <summary>Hatchery slot count (user-decided 2026-11-16).</summary>
        public const int DefaultCapacity = 4;

        /// <summary>Extra gain from egg_hatch_boost: +50% (user-decided 2026-11-16).</summary>
        public const int DefaultBoostPercent = 50;

        /// <summary>Hatchery slot count.</summary>
        public int Capacity { get; }

        /// <summary>Progress per battle, percent.</summary>
        public int BattlePercent { get; }

        /// <summary>Progress per boss victory, percent.</summary>
        public int BossPercent { get; }

        /// <summary>Extra gain percent while the team has egg_hatch_boost.</summary>
        public int BoostPercent { get; }

        /// <summary>Creates a config.</summary>
        /// <remarks>Pre: capacity &gt; 0; percents &gt;= 0. Post: properties set. Side effects: none.</remarks>
        public HatchConfig(int capacity, int battlePercent, int bossPercent, int boostPercent)
        {
            Contract.Requires(capacity > 0, "capacity must be > 0");
            Contract.Requires(battlePercent >= 0 && bossPercent >= 0 && boostPercent >= 0, "percents must be >= 0");
            Capacity = capacity;
            BattlePercent = battlePercent;
            BossPercent = bossPercent;
            BoostPercent = boostPercent;
        }

        /// <summary>Q-M3 values plus the user-decided capacity and boost.</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public static HatchConfig Default =>
            new HatchConfig(DefaultCapacity, DefaultBattlePercent, DefaultBossPercent, DefaultBoostPercent);
    }

    /// <summary>An egg incubating in the hatchery.</summary>
    /// <remarks>
    /// Mutated only through <see cref="EggHatchery"/>. Vessel is the parent's capture vessel (Q-C13: hatched creatures
    /// inherit it) kept as the raw <c>VesselKind</c> value because Capture does not reference Creatures.
    /// </remarks>
    public sealed class IncubatingEgg
    {
        /// <summary>Definition id.</summary>
        public string EggId { get; }

        /// <summary>Parent's vessel (<c>VesselKind</c> value) inherited by the hatchling.</summary>
        public int Vessel { get; }

        /// <summary>Progress in parts per million; hatches at <see cref="HatchConfig.FullProgressPpm"/>.</summary>
        /// <remarks>Pre: none. Post: 0..FullProgressPpm. Side effects: none.</remarks>
        public long ProgressPpm { get; internal set; }

        /// <summary>True once progress reached 100%.</summary>
        /// <remarks>Pre: none. Post: ProgressPpm == FullProgressPpm. Side effects: none.</remarks>
        public bool IsReady => ProgressPpm >= HatchConfig.FullProgressPpm;

        /// <summary>Creates an egg at the given progress (0 for a new egg; nonzero when restoring a save).</summary>
        /// <remarks>Pre: eggId non-empty; vessel &gt;= 0; 0 &lt;= progressPpm &lt;= FullProgressPpm. Post: fields set. Side effects: none.</remarks>
        public IncubatingEgg(string eggId, int vessel, long progressPpm = 0)
        {
            Contract.Requires(!string.IsNullOrEmpty(eggId), "eggId required");
            Contract.Requires(vessel >= 0, "vessel must be >= 0");
            Contract.Requires(progressPpm >= 0 && progressPpm <= HatchConfig.FullProgressPpm, "progress out of range");
            EggId = eggId;
            Vessel = vessel;
            ProgressPpm = progressPpm;
        }
    }

    /// <summary>Result of hatching one egg.</summary>
    /// <remarks>Pre: none. Post: immutable. Side effects: none.</remarks>
    public readonly struct HatchResult
    {
        /// <summary>Species of the hatchling.</summary>
        public string SpeciesId { get; }

        /// <summary>Inherited vessel (<c>VesselKind</c> value).</summary>
        public int Vessel { get; }

        /// <summary>Creates a result.</summary>
        /// <remarks>Pre: none. Post: properties set. Side effects: none.</remarks>
        public HatchResult(string speciesId, int vessel)
        {
            SpeciesId = speciesId;
            Vessel = vessel;
        }
    }

    /// <summary>Lobby hatchery: accumulates run events into egg progress and resolves hatching. REQ: lobby-meta.md (Q-M3).</summary>
    /// <remarks>
    /// Pure and deterministic; the caller reports run events and owns the RNG. Progress is integer ppm so repeated
    /// small walks never lose precision to floats. Persistence (LobbySave) is deliberately not wired here: the save
    /// format is P4-06's and a format bump is review-required.
    /// </remarks>
    public sealed class EggHatchery
    {
        private readonly List<IncubatingEgg> _eggs = new List<IncubatingEgg>();
        private readonly HatchConfig _config;

        /// <summary>Creates an empty hatchery.</summary>
        /// <remarks>Pre: none. Post: no eggs. Side effects: none.</remarks>
        public EggHatchery(HatchConfig config) { _config = config; }

        /// <summary>Eggs currently incubating.</summary>
        /// <remarks>Pre: none. Post: count &lt;= capacity. Side effects: none.</remarks>
        public IReadOnlyList<IncubatingEgg> Eggs => _eggs;

        /// <summary>Puts an egg into a free slot.</summary>
        /// <remarks>
        /// Pre: egg non-null and not already placed.
        /// Post: returns true and the egg is listed iff a slot was free; false changes nothing.
        /// Side effects: mutates the egg list.
        /// </remarks>
        public bool TryPlace(IncubatingEgg egg)
        {
            Contract.Requires(egg != null, "egg required");
            Contract.Requires(!_eggs.Contains(egg), "egg already placed");
            if (_eggs.Count >= _config.Capacity) return false;
            _eggs.Add(egg);
            return true;
        }

        /// <summary>Adds progress for distance walked on a planet.</summary>
        /// <remarks>
        /// Pre: distanceMm &gt;= 0; circumferenceMm &gt; 0 (planet equator; 超巨星繞一圈 = 100%).
        /// Post: every egg gains distance/circumference (plus boost), capped at 100%. Side effects: mutates eggs.
        /// </remarks>
        public void AddWalk(long distanceMm, long circumferenceMm, bool hasBoost)
        {
            Contract.Requires(distanceMm >= 0, "distance must be >= 0");
            Contract.Requires(circumferenceMm > 0, "circumference must be > 0");
            // distanceMm * 1e6 stays below 2^63 for any distance under ~9e12 mm (9 million km).
            Gain(distanceMm * HatchConfig.FullProgressPpm / circumferenceMm, hasBoost);
        }

        /// <summary>Adds progress for finished battles.</summary>
        /// <remarks>Pre: count &gt;= 0. Post: every egg gains count × BattlePercent (plus boost). Side effects: mutates eggs.</remarks>
        public void AddBattles(int count, bool hasBoost)
        {
            Contract.Requires(count >= 0, "count must be >= 0");
            Gain((long)count * _config.BattlePercent * (HatchConfig.FullProgressPpm / 100), hasBoost);
        }

        /// <summary>Adds progress for a defeated boss.</summary>
        /// <remarks>Pre: none. Post: every egg gains BossPercent (plus boost). Side effects: mutates eggs.</remarks>
        public void AddBossVictory(bool hasBoost) =>
            Gain((long)_config.BossPercent * (HatchConfig.FullProgressPpm / 100), hasBoost);

        /// <summary>Hatches an egg that reached 100%.</summary>
        /// <remarks>
        /// Pre: egg is placed here, IsReady, and its definition's id equals EggId; rng seeded.
        /// Post: egg removed from the hatchery; result species drawn by weight; vessel inherited.
        /// Side effects: removes the egg; advances rng by exactly one draw.
        /// </remarks>
        public HatchResult Hatch(IncubatingEgg egg, EggDef def, ref Pcg32 rng)
        {
            Contract.Requires(egg != null && def != null, "egg and def required");
            Contract.Requires(_eggs.Contains(egg), "egg not in this hatchery");
            Contract.Requires(egg.IsReady, "egg is not ready");
            Contract.Requires(def.Id == egg.EggId, "definition does not match egg");
            _eggs.Remove(egg);
            return new HatchResult(def.Roll(ref rng), egg.Vessel);
        }

        private void Gain(long ppm, bool hasBoost)
        {
            if (ppm <= 0) return;
            if (hasBoost) ppm = ppm * (100 + _config.BoostPercent) / 100;
            for (int i = 0; i < _eggs.Count; i++)
            {
                long next = _eggs[i].ProgressPpm + ppm;
                _eggs[i].ProgressPpm = next > HatchConfig.FullProgressPpm ? HatchConfig.FullProgressPpm : next;
            }
        }
    }
}
