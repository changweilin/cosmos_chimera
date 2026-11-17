using System;
using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using UnityEngine;

namespace Cosmimera.GameData
{
    /// <summary>
    /// Bounded normal distribution of one wander quantity: niche mean, between-individual SD, per-action SD, clamp.
    /// REQ: docs/design/creature-ecology.md#平均值與變異度.
    /// </summary>
    /// <remarks>Pre: filled by the importer. Post: 0 &lt; Min &lt;= Mean &lt;= Max; both SDs &gt;= 0. Side effects: none.</remarks>
    [Serializable]
    public struct WanderDistribution
    {
        [SerializeField] private int mean;
        [SerializeField] private int individualSd;
        [SerializeField] private int actionSd;
        [SerializeField] private int min;
        [SerializeField] private int max;

        /// <summary>Creates a distribution.</summary>
        /// <remarks>
        /// Pre: 0 &lt; min &lt;= mean &lt;= max; individualSd, actionSd &gt;= 0.
        /// Post: fields as given. Side effects: none.
        /// </remarks>
        public WanderDistribution(int mean, int individualSd, int actionSd, int min, int max)
        {
            Contract.Requires(min > 0 && min <= mean && mean <= max, "distribution needs 0 < min <= mean <= max");
            Contract.Requires(individualSd >= 0 && actionSd >= 0, "distribution SDs must be >= 0");
            this.mean = mean;
            this.individualSd = individualSd;
            this.actionSd = actionSd;
            this.min = min;
            this.max = max;
        }

        /// <remarks>Pre: none. Post: Min..Max. Side effects: none.</remarks>
        public int Mean => mean;

        /// <remarks>Pre: none. Post: >= 0. Side effects: none.</remarks>
        public int IndividualSd => individualSd;

        /// <remarks>Pre: none. Post: >= 0. Side effects: none.</remarks>
        public int ActionSd => actionSd;

        /// <remarks>Pre: none. Post: > 0. Side effects: none.</remarks>
        public int Min => min;

        /// <remarks>Pre: none. Post: >= Min. Side effects: none.</remarks>
        public int Max => max;
    }

    /// <summary>One allowed habit of a niche with its positive integer weight.</summary>
    /// <remarks>Pre: filled by the importer. Post: Weight > 0. Side effects: none.</remarks>
    [Serializable]
    public struct HabitWeight
    {
        [SerializeField] private Habit habit;
        [SerializeField] private int weight;

        /// <remarks>Pre: weight > 0. Post: fields as given. Side effects: none.</remarks>
        public HabitWeight(Habit habit, int weight)
        {
            Contract.Requires(weight > 0, "habit weight must be > 0");
            this.habit = habit;
            this.weight = weight;
        }

        /// <remarks>Pre: none. Post: defined. Side effects: none.</remarks>
        public Habit Habit => habit;

        /// <remarks>Pre: none. Post: > 0. Side effects: none.</remarks>
        public int Weight => weight;
    }

    /// <summary>One candidate niche of a creature form with its positive integer weight.</summary>
    /// <remarks>Pre: filled by the importer. Post: Niche non-null; Weight > 0. Side effects: none.</remarks>
    [Serializable]
    public struct NicheCandidate
    {
        [SerializeField] private NicheDef niche;
        [SerializeField] private int weight;

        /// <remarks>Pre: niche non-null; weight > 0. Post: fields as given. Side effects: none.</remarks>
        public NicheCandidate(NicheDef niche, int weight)
        {
            Contract.Requires(niche != null && weight > 0, "niche candidate needs a niche and a weight > 0");
            this.niche = niche;
            this.weight = weight;
        }

        /// <remarks>Pre: none. Post: non-null for importer-written data. Side effects: none.</remarks>
        public NicheDef Niche => niche;

        /// <remarks>Pre: none. Post: > 0. Side effects: none.</remarks>
        public int Weight => weight;
    }

    /// <summary>
    /// Imported ecological niche: habitat filter, allowed habits and wander parameters.
    /// REQ: docs/design/creature-ecology.md (P3-31).
    /// </summary>
    /// <remarks>
    /// Pre: filled by the importer. Post: HabitatKinds and Habits non-empty; RadiusMm > 0 unless Mode is UnboundedRandom,
    /// where ExclusionRadiusMm > 0 instead. Side effects: none.
    /// </remarks>
    public sealed class NicheDef : GameDef
    {
        [SerializeField] private string nameZh;
        [SerializeField] private List<string> habitatKinds = new List<string>();
        [SerializeField] private List<HabitWeight> habits = new List<HabitWeight>();
        [SerializeField] private WanderMode mode;
        [SerializeField] private int radiusMm;
        [SerializeField] private int exclusionRadiusMm;
        [SerializeField] private WanderDistribution speedPermille;
        [SerializeField] private WanderDistribution moveMs;
        [SerializeField] private WanderDistribution pauseMs;

        /// <summary>Display name (zh).</summary>
        /// <remarks>Pre: none. Post: non-empty. Side effects: none.</remarks>
        public string NameZh => nameZh;

        /// <summary>Landform kinds (LandformSynth ids) where an individual may take this niche.</summary>
        /// <remarks>Pre: none. Post: Count >= 1, unique. Side effects: none.</remarks>
        public IReadOnlyList<string> HabitatKinds => habitatKinds;

        /// <summary>Allowed habits and weights, in data order.</summary>
        /// <remarks>Pre: none. Post: Count >= 1, unique habits. Side effects: none.</remarks>
        public IReadOnlyList<HabitWeight> Habits => habits;

        /// <remarks>Pre: none. Post: defined. Side effects: none.</remarks>
        public WanderMode Mode => mode;

        /// <summary>Route / area radius around the home point (mm); 0 for UnboundedRandom.</summary>
        /// <remarks>Pre: none. Post: > 0 unless Mode is UnboundedRandom. Side effects: none.</remarks>
        public int RadiusMm => radiusMm;

        /// <summary>
        /// Radius (mm) of the cap other species of this niche may not overlap: RadiusMm, or the exclusion radius of an
        /// UnboundedRandom niche (spawn area only). REQ: creature-ecology.md#同棲位不同種不共用活動區.
        /// </summary>
        /// <remarks>Pre: none. Post: > 0. Side effects: none.</remarks>
        public int ActivityRadiusMm => mode == WanderMode.UnboundedRandom ? exclusionRadiusMm : radiusMm;

        /// <summary>Wander speed as permille of the combat base move speed.</summary>
        /// <remarks>Pre: none. Post: valid distribution. Side effects: none.</remarks>
        public WanderDistribution SpeedPermille => speedPermille;

        /// <summary>Move time budget of one leg (ms).</summary>
        /// <remarks>Pre: none. Post: valid distribution. Side effects: none.</remarks>
        public WanderDistribution MoveMs => moveMs;

        /// <summary>Pause between legs (ms).</summary>
        /// <remarks>Pre: none. Post: valid distribution. Side effects: none.</remarks>
        public WanderDistribution PauseMs => pauseMs;

        /// <summary>True when <paramref name="kind"/> is one of <see cref="HabitatKinds"/>.</summary>
        /// <remarks>Pre: kind non-null. Post: ordinal comparison. Side effects: none.</remarks>
        public bool Allows(string kind)
        {
            Contract.Requires(kind != null, "kind must not be null");
            foreach (string k in habitatKinds)
            {
                if (string.Equals(k, kind, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        internal void Set(string nameZh, List<string> habitatKinds, List<HabitWeight> habits, WanderMode mode,
            int radiusMm, int exclusionRadiusMm, WanderDistribution speedPermille, WanderDistribution moveMs,
            WanderDistribution pauseMs)
        {
            Contract.Requires(habitatKinds != null && habitatKinds.Count > 0, "niche needs habitat kinds");
            Contract.Requires(habits != null && habits.Count > 0, "niche needs habits");
            Contract.Requires(mode == WanderMode.UnboundedRandom ? exclusionRadiusMm > 0 && radiusMm == 0 : radiusMm > 0,
                "bounded niches need radius_mm; unbounded ones exclusion_radius_mm only");
            this.nameZh = nameZh;
            this.habitatKinds = habitatKinds;
            this.habits = habits;
            this.mode = mode;
            this.radiusMm = radiusMm;
            this.exclusionRadiusMm = exclusionRadiusMm;
            this.speedPermille = speedPermille;
            this.moveMs = moveMs;
            this.pauseMs = pauseMs;
        }
    }
}
