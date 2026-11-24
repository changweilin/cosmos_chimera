using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.GameData;

namespace Cosmimera.WorldGen
{
    /// <summary>Who fights in one advanced terrain's trial on one planet (trainer-battles.md#地形試煉).</summary>
    /// <remarks>Pre: produced by <see cref="TrialPlanner.TryPlan"/>. Post: immutable. Side effects: none.</remarks>
    public sealed class TerrainTrialPlan
    {
        internal TerrainTrialPlan(AdvancedTerrainSpec terrain, CreatureStage stage, bool bossOverloads, string bossCreatureId,
            int level, TrainerTeamMember[] masterTeam, ulong masterLookSeed)
        {
            Terrain = terrain;
            Stage = stage;
            BossOverloads = bossOverloads;
            BossCreatureId = bossCreatureId;
            Level = level;
            MasterTeam = masterTeam;
            MasterLookSeed = masterLookSeed;
        }

        /// <summary>The advanced terrain this trial belongs to.</summary>
        /// <remarks>Pre: none. Post: one of AdvancedTerrains.All. Side effects: none.</remarks>
        public AdvancedTerrainSpec Terrain { get; }

        /// <summary>Form of the boss and of every master creature: the planet's boss tier.</summary>
        /// <remarks>Pre: none. Post: AdvancedTerrains.TrialStageFor(planet size). Side effects: none.</remarks>
        public CreatureStage Stage { get; }

        /// <summary>Whether the boss fights overloaded (supergiant planets).</summary>
        /// <remarks>Pre: none. Post: AdvancedTerrains.TrialBossOverloadsOn(planet size). Side effects: none.</remarks>
        public bool BossOverloads { get; }

        /// <summary>Dungeon boss species: holds the terrain's immunity at <see cref="Stage"/>.</summary>
        /// <remarks>Pre: none. Post: id of a pool creature with FieldUtility == Terrain.Immunity and Stage == Stage. Side effects: none.</remarks>
        public string BossCreatureId { get; }

        /// <summary>Level of the boss and of every master creature: the planet boss level.</summary>
        /// <remarks>Pre: none. Post: 1..100. Side effects: none.</remarks>
        public int Level { get; }

        /// <summary>Trial master's team in relay order.</summary>
        /// <remarks>
        /// Pre: none. Post: Count == Terrain.TrialTeamSize; every member's main element is Terrain.Element, its stage is
        ///       Stage and its level is Level. Side effects: none.
        /// </remarks>
        public IReadOnlyList<TrainerTeamMember> MasterTeam { get; }

        /// <summary>Appearance seed of the trial master: the same on every planet (hash of its character id).</summary>
        /// <remarks>Pre: none. Post: depends on Terrain.Id only. Side effects: none.</remarks>
        public ulong MasterLookSeed { get; }
    }

    /// <summary>Steps of one terrain trial within a run (trainer-battles.md#地形試煉).</summary>
    /// <remarks>Pre: none. Post: stable values; numeric order is progress order. Side effects: none.</remarks>
    public enum TrialStep
    {
        /// <summary>The master waits at the dungeon entrance.</summary>
        NotStarted = 0,
        /// <summary>Talked to the master; the master leads the player in (immunity waived).</summary>
        Guided = 1,
        /// <summary>Step 1 done: the guard elite is knocked out.</summary>
        GuardDefeated = 2,
        /// <summary>Final battle running: master + dungeon boss vs the player.</summary>
        FinalBattle = 3,
        /// <summary>Final battle won; the boss can be captured with certainty.</summary>
        Won = 4,
    }

    /// <summary>What a won final battle grants.</summary>
    /// <remarks>Pre: returned by <see cref="TrialProgress.OnFinalBattleEnded"/>. Post: immutable. Side effects: none.</remarks>
    public readonly struct TrialVictory
    {
        internal TrialVictory(bool firstWin, Element vesselElement, string badgeTerrainId)
        {
            FirstWin = firstWin;
            VesselElement = vesselElement;
            BadgeTerrainId = badgeTerrainId;
        }

        /// <summary>True when this is the account's first win of this terrain: grant the vessel recipe and the badge.</summary>
        /// <remarks>Pre: none. Post: as passed by the caller's badge check. Side effects: none.</remarks>
        public bool FirstWin { get; }

        /// <summary>Element of the advanced vessel recipe granted on a first win.</summary>
        /// <remarks>Pre: none. Post: the terrain's element. Side effects: none.</remarks>
        public Element VesselElement { get; }

        /// <summary>Terrain id to record with LobbySave.RecordTrialCompleted.</summary>
        /// <remarks>Pre: none. Post: canonical terrain id. Side effects: none.</remarks>
        public string BadgeTerrainId { get; }
    }

    /// <summary>
    /// Progress of one terrain trial during a run: talk → guided in → guard elite → arena → final battle → won, with a
    /// loss sending the player back to the entrance while keeping completed steps.
    /// REQ: docs/design/trainer-battles.md#地形試煉.
    /// </summary>
    /// <remarks>Pre: none. Post: Step only moves as documented per method. Side effects: per method.</remarks>
    public sealed class TrialProgress
    {
        /// <summary>Starts a trial for <paramref name="plan"/> at <see cref="TrialStep.NotStarted"/>.</summary>
        /// <remarks>Pre: plan non-null. Post: Step == NotStarted; BossCaptured false. Side effects: allocates.</remarks>
        public TrialProgress(TerrainTrialPlan plan)
        {
            Contract.Requires(plan != null, "plan must not be null");
            Plan = plan;
        }

        /// <summary>The trial being run.</summary>
        /// <remarks>Pre: none. Post: non-null. Side effects: none.</remarks>
        public TerrainTrialPlan Plan { get; }

        /// <summary>Current step.</summary>
        /// <remarks>Pre: none. Post: defined value. Side effects: none.</remarks>
        public TrialStep Step { get; private set; }

        /// <summary>Whether the guaranteed boss capture has been used.</summary>
        /// <remarks>Pre: none. Post: true only after Step == Won. Side effects: none.</remarks>
        public bool BossCaptured { get; private set; }

        /// <summary>Number of final battles lost in this run.</summary>
        /// <remarks>Pre: none. Post: &gt;= 0. Side effects: none.</remarks>
        public int Losses { get; private set; }

        /// <summary>
        /// Whether the party may enter the terrain without its immunity: only while the master leads the trial.
        /// REQ: planet-world.md#進階地形與通行限制 (exception), trainer-battles.md#地形試煉 (試煉外仍需免疫).
        /// </summary>
        /// <remarks>Pre: none. Post: true iff Guided &lt;= Step &lt;= FinalBattle. Side effects: none.</remarks>
        public bool WaivesImmunity => Step >= TrialStep.Guided && Step <= TrialStep.FinalBattle;

        /// <summary>Whether the guard elite still seals the arena.</summary>
        /// <remarks>Pre: none. Post: true iff Step &lt;= Guided. Side effects: none.</remarks>
        public bool ArenaSealed => Step <= TrialStep.Guided;

        /// <summary>Talking to the master starts the trial (codex basic).</summary>
        /// <remarks>
        /// Pre: none. Post: true and Step == Guided iff Step was NotStarted; otherwise false and nothing changes.
        /// Side effects: mutates Step.
        /// </remarks>
        public bool Talk()
        {
            if (Step != TrialStep.NotStarted)
            {
                return false;
            }

            Step = TrialStep.Guided;
            return true;
        }

        /// <summary>Step 1: the guard elite was knocked out.</summary>
        /// <remarks>
        /// Pre: none. Post: true and Step == GuardDefeated iff Step was Guided; otherwise false and nothing changes
        ///      (knocking it out outside a trial does not start one).
        /// Side effects: mutates Step.
        /// </remarks>
        public bool OnGuardDefeated()
        {
            if (Step != TrialStep.Guided)
            {
                return false;
            }

            Step = TrialStep.GuardDefeated;
            return true;
        }

        /// <summary>Step 2: the player reached the boss arena; the final battle starts.</summary>
        /// <remarks>
        /// Pre: none. Post: true and Step == FinalBattle iff Step was GuardDefeated; otherwise false and nothing changes.
        /// Side effects: mutates Step.
        /// </remarks>
        public bool OnArenaReached()
        {
            if (Step != TrialStep.GuardDefeated)
            {
                return false;
            }

            Step = TrialStep.FinalBattle;
            return true;
        }

        /// <summary>Ends the final battle.</summary>
        /// <remarks>
        /// Pre: Step == FinalBattle.
        /// Post: won → Step == Won and returns the victory with FirstWin == !alreadyHasBadge; lost → Step ==
        ///       GuardDefeated (completed steps kept, the caller returns the player to the entrance), Losses + 1, and
        ///       returns null.
        /// Side effects: mutates Step and Losses.
        /// </remarks>
        public TrialVictory? OnFinalBattleEnded(bool playerWon, bool alreadyHasBadge)
        {
            Contract.Requires(Step == TrialStep.FinalBattle, "no final battle is running");
            if (!playerWon)
            {
                Step = TrialStep.GuardDefeated;
                Losses++;
                return null;
            }

            Step = TrialStep.Won;
            return new TrialVictory(!alreadyHasBadge, Plan.Terrain.Element, Plan.Terrain.Id);
        }

        /// <summary>Uses the guaranteed capture of the dungeon boss (capture always succeeds).</summary>
        /// <remarks>
        /// Pre: none. Post: true iff Step == Won and the capture was unused; then BossCaptured. Otherwise false.
        /// Side effects: mutates BossCaptured.
        /// </remarks>
        public bool TryCaptureBoss()
        {
            if (Step != TrialStep.Won || BossCaptured)
            {
                return false;
            }

            BossCaptured = true;
            return true;
        }
    }
}
