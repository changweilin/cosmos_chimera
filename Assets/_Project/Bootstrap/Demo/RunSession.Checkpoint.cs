using System;
using System.Collections.Generic;
using Cosmimera.Creatures;
using Cosmimera.GameData;
using Cosmimera.Save;
using Cosmimera.WorldGen;
using Unity.Mathematics;
using UnityEngine;

namespace Cosmimera.Bootstrap.Demo
{
    public sealed partial class RunSession
    {
        private bool _surfaceBossPreSaved;
        private bool _surfaceBossDefeated;
        private bool _dungeonBossDefeated;
        private float _nextOverworldAutoSaveTime;

        /// <summary>Raised whenever an overworld auto-save or random-planet checkpoint should be persisted.</summary>
        /// <remarks>
        /// REQ: docs/design/lobby-meta.md#進度保存.
        /// Pre: none. Post: payload is a valid RunCheckpointSave for the current world. Side effects: subscriber writes save.
        /// </remarks>
        public event Action<RunCheckpointSave> CheckpointTriggered;

        internal SaveWorldKind CurrentWorldKind =>
            _setup.IsBeginnerQuest || _setup.PlanetType?.Id == "cradle_planet"
                ? SaveWorldKind.CradlePlanet
                : (_setup.IsLobbyPlanet ? SaveWorldKind.LobbyPlanet : SaveWorldKind.RandomPlanet);

        internal bool TryEmitCheckpoint(SaveCheckpointKind kind)
        {
            SaveWorldKind worldKind = CurrentWorldKind;
            if (!SaveCheckpointPolicy.CanSaveCheckpoint(worldKind, kind, TrialInside))
            {
                return false;
            }

            if (_rig == null || _setup.PlanetType == null)
            {
                return false;
            }

            float3 dir = _rig.Config.ToDirection(PlayerPosition());
            var settlements = new List<int>(_discoveredSettlements);
            settlements.Sort();

            int bqPhase = _bqTracker != null ? (int)_bqTracker.FieldStep : 0;
            var cp = new RunCheckpointSave(
                worldKind,
                kind,
                _setup.Seed,
                _setup.PlanetType.Id,
                BossDraw.SizeFor(_setup.BossStage),
                _setup.BossStage,
                dir.x,
                dir.y,
                dir.z,
                beginnerQuestPhase: math.max(0, bqPhase),
                discoveredSettlements: settlements,
                surfaceBossDefeated: _surfaceBossDefeated,
                dungeonBossDefeated: _dungeonBossDefeated);

            CheckpointTriggered?.Invoke(cp);
            return true;
        }

        private void TickCheckpoints()
        {
            SaveWorldKind worldKind = CurrentWorldKind;
            if (worldKind == SaveWorldKind.RandomPlanet)
            {
                if (!_surfaceBossPreSaved && !_surfaceBossDefeated && _team != null && _team.InCombat)
                {
                    int bossId = BossUnit();
                    if (bossId >= 0)
                    {
                        _surfaceBossPreSaved = true;
                        TryEmitCheckpoint(SaveCheckpointKind.RandomPreSurfaceBoss);
                    }
                }
                return;
            }

            if (SaveCheckpointPolicy.ShouldAutoSaveOnOverworld(worldKind, TrialInside) &&
                (_team == null || !_team.InCombat) &&
                Time.unscaledTime >= _nextOverworldAutoSaveTime)
            {
                _nextOverworldAutoSaveTime = Time.unscaledTime + 30f;
                TryEmitCheckpoint(SaveCheckpointKind.OverworldAuto);
            }
        }

        private void ApplyResumeCheckpointIfAny()
        {
            RunCheckpointSave cp = _setup.ResumeCheckpoint;
            if (cp == null)
            {
                return;
            }

            _surfaceBossDefeated = cp.SurfaceBossDefeated;
            _dungeonBossDefeated = cp.DungeonBossDefeated;
            if (cp.DiscoveredSettlements != null)
            {
                foreach (int idx in cp.DiscoveredSettlements)
                {
                    if (_layout != null && idx >= 0 && idx < _layout.Settlements.Count)
                    {
                        _discoveredSettlements.Add(idx);
                    }
                }
            }

            if (_rig != null && _surface != null)
            {
                float3 dir = math.normalizesafe(new float3(cp.PlayerDirX, cp.PlayerDirY, cp.PlayerDirZ), new float3(0f, 1f, 0f));
                _rig.Character.Teleport(_rig.Config.ToWorld(dir, _surface.GroundAltitude(dir) + 1f));
            }

            if (_bqTracker != null && cp.BeginnerQuestPhase > 0)
            {
                _bqTracker.RestoreFieldStepForResume((BeginnerFieldStep)cp.BeginnerQuestPhase);
            }
        }
    }
}
