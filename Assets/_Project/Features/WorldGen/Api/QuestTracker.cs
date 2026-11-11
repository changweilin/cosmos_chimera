using Cosmimera.Core.Contracts;
using Cosmimera.GameData;

namespace Cosmimera.WorldGen
{
    /// <summary>Advances a run's main quest from gameplay events. REQ: planet-world.md#目標與結束條件.</summary>
    /// <remarks>
    /// Steps complete strictly in order and events that do not match the current step are ignored, with one exception
    /// (Q-E1, 2026-11-09): a DefeatElite step whose target was already knocked out completes as soon as it becomes
    /// current, because spawns never respawn and the chain would otherwise stall. A boss knockout ends the quest at any
    /// step; steps it skips are not <see cref="IsEarned"/> (REQ: level-progression.md#獎勵量 — skipped steps pay no XP).
    /// Pre: one tracker per plan. Side effects: mutates its own counters only.
    /// </remarks>
    public sealed class QuestTracker
    {
        private readonly RunPlan _plan;
        private readonly bool[] _down;
        private readonly bool[] _earned;

        /// <summary>Starts at step 0.</summary>
        /// <remarks>Pre: plan non-null. Post: StepIndex == 0, Progress == 0. Side effects: none.</remarks>
        public QuestTracker(RunPlan plan)
        {
            Contract.Requires(plan != null && plan.Quest.Count > 0, "plan with a quest is required");
            _plan = plan;
            _down = new bool[plan.Spawns.Count];
            _earned = new bool[plan.Quest.Count];
        }

        /// <summary>Index of the current step; == Quest.Count when complete.</summary>
        /// <remarks>Pre: none. Post: 0..Quest.Count. Side effects: none.</remarks>
        public int StepIndex { get; private set; }

        /// <summary>Count toward the current step.</summary>
        /// <remarks>Pre: none. Post: 0..Current.Count. Side effects: none.</remarks>
        public int Progress { get; private set; }

        /// <summary>Whether every step is done.</summary>
        /// <remarks>Pre: none. Post: StepIndex == Quest.Count. Side effects: none.</remarks>
        public bool IsComplete => StepIndex >= _plan.Quest.Count;

        /// <summary>The current step.</summary>
        /// <remarks>Pre: !IsComplete. Post: Quest[StepIndex]. Side effects: none.</remarks>
        public RunQuestStep Current
        {
            get
            {
                Contract.Requires(!IsComplete, "quest is complete");
                return _plan.Quest[StepIndex];
            }
        }

        /// <summary>Whether step <paramref name="step"/> was completed by its own condition (not skipped by the boss).</summary>
        /// <remarks>
        /// Pre: 0 &lt;= step &lt; Quest.Count. Post: once true, stays true; false for steps the boss knockout skipped.
        /// Side effects: none.
        /// </remarks>
        public bool IsEarned(int step)
        {
            Contract.Requires(step >= 0 && step < _earned.Length, "step out of range");
            return _earned[step];
        }

        /// <summary>The player entered <paramref name="region"/>.</summary>
        /// <remarks>Pre: valid region. Post: completes a ReachPoi step for that region. Side effects: may advance.</remarks>
        public void OnRegionEntered(int region)
        {
            if (!IsComplete && Current.Kind == QuestStepKind.ReachPoi && Current.Region == region)
            {
                Advance();
            }
        }

        /// <summary>A wild creature (spawn index, main element) was knocked out by the player's side.</summary>
        /// <remarks>
        /// Pre: spawnIndex valid for the plan. Post: counts toward DefeatWild (matching element, any role), completes
        /// DefeatElite on its target now or when that step becomes current; the boss completes the quest, earning only
        /// the DefeatBoss step (if any) besides those already earned. A repeat for an
        /// already knocked-out spawn is a no-op. Side effects: may advance.
        /// </remarks>
        public void OnKnockedOut(int spawnIndex, Element mainElement)
        {
            Contract.Requires(spawnIndex >= 0 && spawnIndex < _plan.Spawns.Count, "unknown spawn");
            // A spawn falls once per run; a resent event must not count twice toward DefeatWild (SOL P2-12 R6).
            if (_down[spawnIndex])
            {
                return;
            }

            _down[spawnIndex] = true;
            if (_plan.Spawns[spawnIndex].Role == SpawnRole.Boss)
            {
                for (int i = StepIndex; i < _earned.Length; i++)
                {
                    _earned[i] |= _plan.Quest[i].Kind == QuestStepKind.DefeatBoss;
                }

                StepIndex = _plan.Quest.Count;
                Progress = 0;
                return;
            }

            if (IsComplete)
            {
                return;
            }

            RunQuestStep s = Current;
            if (s.Kind == QuestStepKind.DefeatWild && mainElement == s.Element)
            {
                if (++Progress >= s.Count)
                {
                    Advance();
                }
            }
            else if (s.Kind == QuestStepKind.DefeatElite && s.TargetSpawn == spawnIndex)
            {
                Advance();
            }
        }

        /// <summary>The player captured a wild creature.</summary>
        /// <remarks>Pre: none. Post: completes a CaptureOne step. Side effects: may advance.</remarks>
        public void OnCaptured()
        {
            if (!IsComplete && Current.Kind == QuestStepKind.CaptureOne)
            {
                Advance();
            }
        }

        private void Advance()
        {
            _earned[StepIndex] = true;
            StepIndex++;
            Progress = 0;
            // Q-E1: the elite was killed before its step; credit it now (it cannot be killed again).
            if (!IsComplete && Current.Kind == QuestStepKind.DefeatElite && _down[Current.TargetSpawn])
            {
                Advance();
            }
        }
    }
}
