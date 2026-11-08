using Cosmimera.Core.Contracts;
using Cosmimera.GameData;

namespace Cosmimera.WorldGen
{
    /// <summary>Advances a run's main quest from gameplay events. REQ: planet-world.md#目標與結束條件.</summary>
    /// <remarks>
    /// Steps complete strictly in order; events that do not match the current step are ignored (no pre-credit), so a
    /// player who kills the boss early still has to finish the chain — except DefeatBoss, which also completes every
    /// remaining step because the run ends there.
    /// Pre: one tracker per plan. Side effects: mutates its own counters only.
    /// </remarks>
    public sealed class QuestTracker
    {
        private readonly RunPlan _plan;

        /// <summary>Starts at step 0.</summary>
        /// <remarks>Pre: plan non-null. Post: StepIndex == 0, Progress == 0. Side effects: none.</remarks>
        public QuestTracker(RunPlan plan)
        {
            Contract.Requires(plan != null && plan.Quest.Count > 0, "plan with a quest is required");
            _plan = plan;
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
        /// DefeatElite/DefeatBoss on their target; DefeatBoss on the boss completes the quest. Side effects: may advance.
        /// </remarks>
        public void OnKnockedOut(int spawnIndex, Element mainElement)
        {
            Contract.Requires(spawnIndex >= 0 && spawnIndex < _plan.Spawns.Count, "unknown spawn");
            if (_plan.Spawns[spawnIndex].Role == SpawnRole.Boss)
            {
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
            StepIndex++;
            Progress = 0;
        }
    }
}
