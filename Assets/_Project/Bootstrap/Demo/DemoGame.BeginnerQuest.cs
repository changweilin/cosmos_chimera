using System;
using System.Collections.Generic;
using Cosmimera.Bootstrap.Demo.UI;
using Cosmimera.Crafting;
using Cosmimera.Creatures;
using Cosmimera.GameData;
using Cosmimera.Save;
using Cosmimera.WorldGen;
using UnityEngine;

namespace Cosmimera.Bootstrap.Demo
{
    public sealed partial class DemoGame
    {
        private string _bqCharacterId = string.Empty;
        private string _bqFriendId = string.Empty;
        private string _bqRivalId = string.Empty;
        private string _bqPlayerStarterLine = string.Empty;
        private string _bqPlayerStarterId = string.Empty;
        private string _bqRivalStarterId = string.Empty;
        private string _bqFriendStarterId = string.Empty;
        private bool _activeRunIsBeginnerQuest;

        /// <summary>
        /// Opens the UI Toolkit Character Select screen directly for a new save (no standalone lobby screen).
        /// REQ: docs/design/beginner-quest.md#流程.
        /// </summary>
        private void EnterCharacterSelect()
        {
            _bqCharacterId = string.Empty;
            _bqFriendId = string.Empty;
            _bqRivalId = string.Empty;
            _bqPlayerStarterLine = string.Empty;
            _bqPlayerStarterId = string.Empty;
            _bqRivalStarterId = string.Empty;
            _bqFriendStarterId = string.Empty;
            _screen = Screen.CharacterSelect;
            ShowCharacterSelectOverlay();
        }

        private void ShowCharacterSelectOverlay()
        {
            _overlay ??= new GameOverlayView(transform);
            _overlay.Show(
                _copy.Ui("ui.char_select.title"),
                _copy.Ui("ui.char_select.subtitle"),
                _copy.Ui("ui.lobby.language") + ": " + _language,
                () =>
                {
                    CycleLanguage();
                    ShowCharacterSelectOverlay();
                });

            foreach (string cid in BeginnerQuestRules.CharacterRing)
            {
                string chosenId = cid;
                VesselKind vessel = BeginnerQuestRules.SignatureVessel(chosenId);
                string vesselLabel = _copy.Ui("ui.vessel." + VesselKey(vessel));
                Texture2D portrait = _portraits != null ? _portraits.FindCharacter(chosenId) : null;
                string display = BeginnerQuestRules.CharacterDisplayName(chosenId);
                _overlay.AddCardWithImage(
                    display,
                    $"慣用收服器皿：{vesselLabel}",
                    portrait,
                    $"選擇 {display} 出發",
                    () => ConfirmCharacterAndEnterVillage(chosenId),
                    primary: true);
            }

            _overlay.AddFooterButton("開發者測試存檔 (Dev F9)", OpenDevTestSavesOverlay, primary: false);
            _overlay.AddFooterButton(_copy.Ui("ui.lobby.quit"), Application.Quit, primary: false);
        }

        private void ConfirmCharacterAndEnterVillage(string characterId)
        {
            _bqCharacterId = characterId;
            SyncSelectedCharacterModel(characterId);
            (_bqFriendId, _bqRivalId) = BeginnerQuestRules.AssignCompanions(characterId);
            _overlay?.Hide();
            LaunchBeginnerQuestVillage();
        }

        private void LaunchBeginnerQuestVillage()
        {
            PlanetTypeDef bType = ResolveCradlePlanetType();
            ulong randomSeed = (ulong)Environment.TickCount ^ ((ulong)_menuRng.NextUInt() << 16);
            if (randomSeed == 0)
            {
                randomSeed = BeginnerQuestSeed;
            }

            _runPartyIds.Clear();
            _activeRunIsBeginnerQuest = true;
            _activeRiftIndex = -1;
            _menuInput.Disable();
            _overlay?.Hide();
            _save.SetLastPlanet(new GearOrigin(bType.Id, randomSeed));

            var setup = new RunSetup
            {
                PlanetType = bType,
                Seed = randomSeed,
                Radius = RadiusFor(PlanetSize.Asteroid),
                BossStage = CreatureStage.Juvenile,
                Party = new List<CreatureDef>(),
                PlayerExp = _save.PlayerExp,
                Codex = _save.Codex,
                DiscoveredRecipes = _save.DiscoveredRecipesSnapshot(),
                PartyIds = new List<int>(),
                PartyExp = new List<int>(),
                Progression = _progression,
                Materials = _materialDrops,
                EnvMaterials = _env,
                WeatherControlCooldownMs = _weatherControlCooldownMs,
                CharacterModelPrefab = SelectedCharacterPrefab(),
                Portraits = _portraits,
                VolumeProfile = _volumeProfile,
                Database = _database,
                Owned = new List<OwnedCreature>(_save.Owned),
                Partners = null,
                CraftStock = _save.CraftStock.Snapshot(),
                CraftLooks = _save.CraftStock.LookSnapshot(),
                Equipment = _save.Equipment.Snapshot(),
                TrialBadges = _save.CompletedTrialsSnapshot(),
                IsBeginnerQuest = true,
                InitialBeginnerStage = BeginnerQuestStage.FriendWorldIntro,
                StarterChoices = new List<CreatureDef>(_starters),
                Services = Services,
                PlayerCharacterId = _bqCharacterId,
                FriendId = _bqFriendId,
                RivalId = _bqRivalId,
                PlayerStarterVessel = BeginnerQuestRules.SignatureVessel(_bqCharacterId),
            };

            StartRunSession(setup);
        }

        internal bool RecordStarterChoiceFromServices(
            string characterId,
            CreatureDef starter,
            out CreatureDef friendStarter,
            out CreatureDef rivalStarter,
            out int starterInstanceId)
        {
            _bqCharacterId = characterId;
            if (string.IsNullOrEmpty(_bqFriendId) || string.IsNullOrEmpty(_bqRivalId))
            {
                (_bqFriendId, _bqRivalId) = BeginnerQuestRules.AssignCompanions(characterId);
            }

            _bqPlayerStarterLine = starter.LineId;
            _bqPlayerStarterId = starter.Id;
            (string rivalLine, string friendLine) = BeginnerQuestRules.AssignStarterLines(starter.LineId);
            rivalStarter = FindStarterByLine(rivalLine) ?? starter;
            friendStarter = FindStarterByLine(friendLine) ?? starter;
            _bqRivalStarterId = rivalStarter.Id;
            _bqFriendStarterId = friendStarter.Id;

            int starterExp = _progression.XpThreshold(_progression.StarterLevel);
            if (_save.StarterLine == null)
            {
                if (starter.Niches.Count > 0)
                {
                    WildIndividual ind = WildIndividual.Draw(starter, null, WildIndividual.KeyFor(0x53545254UL, 1UL));
                    _save.ChooseStarter(starter.LineId, starter.Id, starterExp, ind.Niche.Id, ind.Habit);
                }
                else
                {
                    _save.ChooseStarter(starter.LineId, starter.Id, starterExp);
                }
            }

            SyncRunPartyIdsFromSave();
            starterInstanceId = _save.Party.Count > 0 ? _save.Party[0] : 1;
            return true;
        }

        private void SyncSelectedCharacterModel(string characterId)
        {
            if (_characterModels == null || _characterModels.Entries.Count == 0)
            {
                return;
            }

            string targetName = BeginnerQuestRules.CharacterDisplayName(characterId);
            for (int i = 0; i < _characterModels.Entries.Count; i++)
            {
                CharacterModelEntry entry = _characterModels.Entries[i];
                if (string.Equals(entry.id, characterId, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(entry.displayName, targetName, StringComparison.Ordinal))
                {
                    _selectedCharacterIndex = i;
                    return;
                }
            }
        }

        private CreatureDef FindStarterByLine(string lineId)
        {
            foreach (CreatureDef s in _starters)
            {
                if (s.LineId == lineId)
                {
                    return s;
                }
            }

            return null;
        }
    }
}
