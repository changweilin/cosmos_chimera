using System;
using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.GameData;

namespace Cosmimera.Save
{
    /// <summary>Which world tier an active checkpoint belongs to.</summary>
    /// <remarks>Pre: none. Post: valid enum value. Side effects: none.</remarks>
    public enum SaveWorldKind
    {
        CradlePlanet = 0,
        LobbyPlanet = 1,
        RandomPlanet = 2,
    }

    /// <summary>Trigger category of a persisted mid-run or overworld checkpoint.</summary>
    /// <remarks>
    /// REQ: docs/design/lobby-meta.md#進度保存.
    /// Pre: none. Post: valid enum value. Side effects: none.
    /// </remarks>
    public enum SaveCheckpointKind
    {
        /// <summary>Automatic overworld save on Cradle or Mother Planet outside dungeons.</summary>
        OverworldAuto = 0,

        /// <summary>Random planet checkpoint upon arriving at a village/settlement.</summary>
        RandomVillage = 1,

        /// <summary>Random planet checkpoint at a wonder before entering its dungeon.</summary>
        RandomWonderPreDungeon = 2,

        /// <summary>Random planet checkpoint right before engaging the surface BOSS.</summary>
        RandomPreSurfaceBoss = 3,

        /// <summary>Random planet checkpoint right before engaging the dungeon-end BOSS.</summary>
        RandomPreDungeonBoss = 4,
    }

    /// <summary>
    /// Pure rules governing when overworld auto-saves and random-planet checkpoints are allowed.
    /// </summary>
    /// <remarks>
    /// REQ: docs/design/lobby-meta.md#進度保存.
    /// Pre: valid enum values. Post: deterministic boolean result. Side effects: none.
    /// </remarks>
    public static class SaveCheckpointPolicy
    {
        /// <summary>True when overworld auto-save is active (Cradle or Mother Planet, outside any dungeon).</summary>
        /// <remarks>
        /// Pre: worldKind is a defined SaveWorldKind.
        /// Post: true iff (worldKind == CradlePlanet || worldKind == LobbyPlanet) &amp;&amp; !insideDungeon.
        /// Side effects: none.
        /// </remarks>
        public static bool ShouldAutoSaveOnOverworld(SaveWorldKind worldKind, bool insideDungeon)
        {
            Contract.Requires(Enum.IsDefined(typeof(SaveWorldKind), worldKind), "worldKind must be defined");
            return !insideDungeon && (worldKind == SaveWorldKind.CradlePlanet || worldKind == SaveWorldKind.LobbyPlanet);
        }

        /// <summary>True when <paramref name="checkpointKind"/> is a valid random-planet checkpoint trigger.</summary>
        /// <remarks>
        /// Pre: worldKind and checkpointKind are defined enum values.
        /// Post: true iff worldKind == RandomPlanet and checkpointKind is Village, WonderPreDungeon, PreSurfaceBoss, or PreDungeonBoss.
        /// Side effects: none.
        /// </remarks>
        public static bool IsAllowedRandomCheckpoint(SaveWorldKind worldKind, SaveCheckpointKind checkpointKind)
        {
            Contract.Requires(Enum.IsDefined(typeof(SaveWorldKind), worldKind), "worldKind must be defined");
            Contract.Requires(Enum.IsDefined(typeof(SaveCheckpointKind), checkpointKind), "checkpointKind must be defined");
            if (worldKind != SaveWorldKind.RandomPlanet)
            {
                return false;
            }

            return checkpointKind == SaveCheckpointKind.RandomVillage
                || checkpointKind == SaveCheckpointKind.RandomWonderPreDungeon
                || checkpointKind == SaveCheckpointKind.RandomPreSurfaceBoss
                || checkpointKind == SaveCheckpointKind.RandomPreDungeonBoss;
        }

        /// <summary>True when saving a checkpoint of <paramref name="checkpointKind"/> is legal in the current state.</summary>
        /// <remarks>
        /// Pre: worldKind and checkpointKind are defined enum values.
        /// Post: for Cradle/Lobby, requires OverworldAuto and !insideDungeon; for RandomPlanet, requires a valid random-planet trigger.
        /// Side effects: none.
        /// </remarks>
        public static bool CanSaveCheckpoint(SaveWorldKind worldKind, SaveCheckpointKind checkpointKind, bool insideDungeon)
        {
            Contract.Requires(Enum.IsDefined(typeof(SaveWorldKind), worldKind), "worldKind must be defined");
            Contract.Requires(Enum.IsDefined(typeof(SaveCheckpointKind), checkpointKind), "checkpointKind must be defined");
            if (worldKind == SaveWorldKind.RandomPlanet)
            {
                return IsAllowedRandomCheckpoint(worldKind, checkpointKind);
            }

            return checkpointKind == SaveCheckpointKind.OverworldAuto && ShouldAutoSaveOnOverworld(worldKind, insideDungeon);
        }
    }

    /// <summary>
    /// Snapshot of an active overworld auto-save or random-planet checkpoint stored inside <see cref="LobbySave"/>.
    /// </summary>
    /// <remarks>
    /// REQ: docs/design/lobby-meta.md#進度保存.
    /// Pre: constructed with valid world/checkpoint kinds and non-empty planetTypeId.
    /// Post: immutable after construction except via <see cref="Clone"/>.
    /// Side effects: none.
    /// </remarks>
    public sealed class RunCheckpointSave
    {
        private readonly int[] _discoveredSettlements;

        /// <summary>World tier this checkpoint belongs to.</summary>
        /// <remarks>Pre: none. Post: defined SaveWorldKind. Side effects: none.</remarks>
        public SaveWorldKind WorldKind { get; }

        /// <summary>Trigger kind that produced this checkpoint.</summary>
        /// <remarks>Pre: none. Post: defined SaveCheckpointKind. Side effects: none.</remarks>
        public SaveCheckpointKind CheckpointKind { get; }

        /// <summary>Deterministic planet generation seed.</summary>
        /// <remarks>Pre: none. Post: unsigned 64-bit seed. Side effects: none.</remarks>
        public ulong PlanetSeed { get; }

        /// <summary>Planet type identifier (e.g. "cradle_planet", "verdant_planet").</summary>
        /// <remarks>Pre: none. Post: non-empty string. Side effects: none.</remarks>
        public string PlanetTypeId { get; }

        /// <summary>Planet size tier.</summary>
        /// <remarks>Pre: none. Post: defined PlanetSize. Side effects: none.</remarks>
        public PlanetSize PlanetSize { get; }

        /// <summary>BOSS stage tier for the run.</summary>
        /// <remarks>Pre: none. Post: defined CreatureStage. Side effects: none.</remarks>
        public CreatureStage BossStage { get; }

        /// <summary>Player unit sphere direction X component.</summary>
        /// <remarks>Pre: none. Post: finite float. Side effects: none.</remarks>
        public float PlayerDirX { get; }

        /// <summary>Player unit sphere direction Y component.</summary>
        /// <remarks>Pre: none. Post: finite float. Side effects: none.</remarks>
        public float PlayerDirY { get; }

        /// <summary>Player unit sphere direction Z component.</summary>
        /// <remarks>Pre: none. Post: finite float. Side effects: none.</remarks>
        public float PlayerDirZ { get; }

        /// <summary>Beginner-quest phase index when <see cref="WorldKind"/> is <see cref="SaveWorldKind.CradlePlanet"/>.</summary>
        /// <remarks>Pre: none. Post: &gt;= 0. Side effects: none.</remarks>
        public int BeginnerQuestPhase { get; }

        /// <summary>Indices of settlements discovered so far in this world.</summary>
        /// <remarks>Pre: none. Post: sorted unique non-negative indices. Side effects: none.</remarks>
        public IReadOnlyList<int> DiscoveredSettlements => _discoveredSettlements;

        /// <summary>True when the planet surface BOSS has already been defeated in this run.</summary>
        /// <remarks>Pre: none. Post: boolean state. Side effects: none.</remarks>
        public bool SurfaceBossDefeated { get; }

        /// <summary>True when the dungeon-end BOSS has already been defeated in this run.</summary>
        /// <remarks>Pre: none. Post: boolean state. Side effects: none.</remarks>
        public bool DungeonBossDefeated { get; }

        /// <summary>Creates a validated checkpoint snapshot.</summary>
        /// <remarks>
        /// Pre: worldKind, checkpointKind, planetSize, bossStage defined; planetTypeId non-whitespace;
        ///      playerDir components finite; beginnerQuestPhase &gt;= 0; checkpointKind allowed for worldKind.
        /// Post: all properties initialized; discoveredSettlements copied, deduplicated, and sorted.
        /// Side effects: allocates settlement array copy.
        /// </remarks>
        public RunCheckpointSave(
            SaveWorldKind worldKind,
            SaveCheckpointKind checkpointKind,
            ulong planetSeed,
            string planetTypeId,
            PlanetSize planetSize,
            CreatureStage bossStage,
            float playerDirX,
            float playerDirY,
            float playerDirZ,
            int beginnerQuestPhase = 0,
            IReadOnlyList<int> discoveredSettlements = null,
            bool surfaceBossDefeated = false,
            bool dungeonBossDefeated = false)
        {
            Contract.Requires(Enum.IsDefined(typeof(SaveWorldKind), worldKind), "worldKind must be defined");
            Contract.Requires(Enum.IsDefined(typeof(SaveCheckpointKind), checkpointKind), "checkpointKind must be defined");
            Contract.Requires(!string.IsNullOrWhiteSpace(planetTypeId), "planetTypeId must be non-empty");
            Contract.Requires(Enum.IsDefined(typeof(PlanetSize), planetSize), "planetSize must be defined");
            Contract.Requires(Enum.IsDefined(typeof(CreatureStage), bossStage), "bossStage must be defined");
            Contract.Requires(SaveCheckpointPolicy.CanSaveCheckpoint(worldKind, checkpointKind, insideDungeon: false),
                "checkpointKind is not valid for worldKind");
            Contract.Requires(!float.IsNaN(playerDirX) && !float.IsInfinity(playerDirX), "playerDirX must be finite");
            Contract.Requires(!float.IsNaN(playerDirY) && !float.IsInfinity(playerDirY), "playerDirY must be finite");
            Contract.Requires(!float.IsNaN(playerDirZ) && !float.IsInfinity(playerDirZ), "playerDirZ must be finite");
            Contract.Requires(beginnerQuestPhase >= 0, "beginnerQuestPhase must be >= 0");

            WorldKind = worldKind;
            CheckpointKind = checkpointKind;
            PlanetSeed = planetSeed;
            PlanetTypeId = planetTypeId;
            PlanetSize = planetSize;
            BossStage = bossStage;
            PlayerDirX = playerDirX;
            PlayerDirY = playerDirY;
            PlayerDirZ = playerDirZ;
            BeginnerQuestPhase = beginnerQuestPhase;
            SurfaceBossDefeated = surfaceBossDefeated;
            DungeonBossDefeated = dungeonBossDefeated;

            if (discoveredSettlements == null || discoveredSettlements.Count == 0)
            {
                _discoveredSettlements = Array.Empty<int>();
            }
            else
            {
                var set = new SortedSet<int>();
                for (int i = 0; i < discoveredSettlements.Count; i++)
                {
                    int idx = discoveredSettlements[i];
                    Contract.Requires(idx >= 0, "discovered settlement index must be >= 0");
                    set.Add(idx);
                }

                _discoveredSettlements = new int[set.Count];
                set.CopyTo(_discoveredSettlements);
            }
        }

        /// <summary>Returns an independent copy of this checkpoint.</summary>
        /// <remarks>Pre: none. Post: equal checkpoint with independent array. Side effects: allocates.</remarks>
        public RunCheckpointSave Clone() => new RunCheckpointSave(
            WorldKind, CheckpointKind, PlanetSeed, PlanetTypeId, PlanetSize, BossStage,
            PlayerDirX, PlayerDirY, PlayerDirZ, BeginnerQuestPhase,
            _discoveredSettlements, SurfaceBossDefeated, DungeonBossDefeated);
    }
}
