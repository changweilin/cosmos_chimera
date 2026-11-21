using System;
using System.Collections.Generic;
using Cosmimera.Crafting;
using Cosmimera.Creatures;
using Cosmimera.GameData;
using Cosmimera.WorldGen;
using UnityEngine;

namespace Cosmimera.Bootstrap.Demo
{
    public sealed partial class DemoGame
    {
        private BeginnerQuestStage _bqStage = BeginnerQuestStage.CharacterSelect;
        private string _bqCharacterId = string.Empty;
        private string _bqFriendId = string.Empty;
        private string _bqRivalId = string.Empty;
        private string _bqPlayerStarterLine = string.Empty;
        private string _bqPlayerStarterId = string.Empty;
        private string _bqRivalStarterId = string.Empty;
        private string _bqFriendStarterId = string.Empty;
        private readonly List<DialogueLine> _bqDialogue = new List<DialogueLine>();
        private int _bqDialogueIndex;
        private bool _activeRunIsBeginnerQuest;
        private readonly List<MainStoryTrack> _mainStoryTracks = new List<MainStoryTrack>();

        private void EnterBeginnerQuest()
        {
            _bqStage = BeginnerQuestStage.CharacterSelect;
            _bqCharacterId = string.Empty;
            _bqFriendId = string.Empty;
            _bqRivalId = string.Empty;
            _bqPlayerStarterLine = string.Empty;
            _bqPlayerStarterId = string.Empty;
            _bqRivalStarterId = string.Empty;
            _bqFriendStarterId = string.Empty;
            _bqDialogue.Clear();
            _bqDialogueIndex = 0;
            _screen = Screen.BeginnerQuest;
            _menuFocus.SetCount(0);
        }

        private int BeginnerQuestButtonCount() => _bqStage switch
        {
            BeginnerQuestStage.CharacterSelect => BeginnerQuestRules.CharacterRing.Length,
            BeginnerQuestStage.FriendWorldIntro => 1,
            BeginnerQuestStage.StarterSelect => _starters.Count,
            _ => 1,
        };

        private void DrawBeginnerQuest()
        {
            GUILayout.Label("新手任務 · 搖籃星 (R=320 m)", _title, GUILayout.Height(56));
            switch (_bqStage)
            {
                case BeginnerQuestStage.CharacterSelect:
                    DrawBqCharacterSelect();
                    break;
                case BeginnerQuestStage.FriendWorldIntro:
                    DrawBqDialogue();
                    break;
                case BeginnerQuestStage.StarterSelect:
                    DrawBqStarterSelect();
                    break;
            }
        }

        private void DrawBqCharacterSelect()
        {
            GUILayout.Label("階段 1/8：選擇你的冒險者（好友與宿敵將依環狀順序自動決定）", _text);
            GUILayout.Space(12);
            foreach (string cid in BeginnerQuestRules.CharacterRing)
            {
                VesselKind vessel = BeginnerQuestRules.SignatureVessel(cid);
                string vesselLabel = vessel switch
                {
                    VesselKind.GlassBead => "琉璃珠",
                    VesselKind.MagicCube => "魔術方塊",
                    VesselKind.TalismanCard => "符籙卡片",
                    _ => vessel.ToString(),
                };
                if (MenuButton($"{BeginnerQuestRules.CharacterDisplayName(cid)}  （慣用器皿：{vesselLabel}）", GUILayout.Height(52)))
                {
                    _bqCharacterId = cid;
                    SyncSelectedCharacterModel(cid);
                    (_bqFriendId, _bqRivalId) = BeginnerQuestRules.AssignCompanions(cid);
                    _bqDialogue.Clear();
                    _bqDialogue.AddRange(BeginnerQuestRules.FriendIntroDialogue(_bqFriendId));
                    _bqDialogueIndex = 0;
                    _bqStage = BeginnerQuestStage.FriendWorldIntro;
                    _menuFocus.SetCount(0);
                    GUIUtility.ExitGUI();
                }
            }
        }

        private void DrawBqDialogue()
        {
            if (_bqDialogueIndex >= _bqDialogue.Count)
            {
                _bqStage = BeginnerQuestStage.StarterSelect;
                _menuFocus.SetCount(0);
                return;
            }

            DialogueLine line = _bqDialogue[_bqDialogueIndex];
            GUILayout.Label($"階段 2/8：好友「{line.Speaker}」介紹世界觀", _title, GUILayout.Height(40));
            GUILayout.Space(8);
            GUILayout.Label(ResolveBqDialogue(line), _text);
            GUILayout.Space(20);
            if (MenuButton("繼續 [Enter / Space / A]", GUILayout.Height(48)))
            {
                _bqDialogueIndex++;
                if (_bqDialogueIndex >= _bqDialogue.Count)
                {
                    _bqStage = BeginnerQuestStage.StarterSelect;
                    _menuFocus.SetCount(0);
                }

                GUIUtility.ExitGUI();
            }
        }

        private void DrawBqStarterSelect()
        {
            string friendName = BeginnerQuestRules.CharacterDisplayName(_bqFriendId);
            GUILayout.Label("階段 3/8：選擇你的初始御三家（LV10）", _text);
            GUILayout.Label($"選定後，好友「{friendName}」會拿走被你剋制的御三家，與你展開 1vs1 對戰教學。", _text);
            GUILayout.Space(12);

            foreach (CreatureDef s in _starters)
            {
                Texture2D portrait = _portraits != null ? _portraits.FindCreature(s.LineId, s.Stage) : null;
                if (portrait != null)
                {
                    Rect r = GUILayoutUtility.GetRect(64f, 64f, GUILayout.ExpandWidth(false));
                    GUI.DrawTexture(r, portrait, ScaleMode.ScaleToFit, true);
                }

                if (MenuButton($"{_copy.Name(s.Id)}  —  選擇此御三家並展開與好友的 1vs1 教學戰", GUILayout.Height(52)))
                {
                    LaunchBeginnerQuestRun(s);
                    GUIUtility.ExitGUI();
                }
            }
        }

        private void OpenMainStorySelect()
        {
            _mainStoryTracks.Clear();
            _mainStoryTracks.AddRange(BeginnerQuestRules.NextMainStoryTracks(
                _save.Codex, _save.CompletedTrialsSnapshot()));
            _screen = Screen.MainStorySelect;
            _menuFocus.SetCount(0);
        }

        private void DrawMainStorySelect()
        {
            GUILayout.Label("主世界（大廳） · 下一個主線劇情", _title, GUILayout.Height(56));
            GUILayout.Label("選擇一條主線劇情立即出發，或返回大廳改選隨機世界（星海選星）。", _text);
            GUILayout.Space(10);

            foreach (MainStoryTrack track in _mainStoryTracks)
            {
                GUILayout.Label($"<b>{track.Title}</b>\n{track.Summary}", _text);
                if (MenuButton($"出發：{track.Title}", GUILayout.Height(44)))
                {
                    LaunchMainStoryTrack(track);
                    GUIUtility.ExitGUI();
                }

                GUILayout.Space(6);
            }

            GUILayout.Space(10);
            if (MenuButton(_copy.Ui("ui.star_select.back"), GUILayout.Height(40)))
            {
                _screen = Screen.Lobby;
                _menuFocus.SetCount(0);
                GUIUtility.ExitGUI();
            }
        }

        private void LaunchMainStoryTrack(MainStoryTrack track)
        {
            var types = new List<PlanetTypeDef>();
            foreach (GameDef def in _database.All)
            {
                if (def is PlanetTypeDef t && t.Id != "cradle_planet")
                {
                    types.Add(t);
                }
            }

            if (types.Count == 0)
            {
                return;
            }

            PlanetTypeDef chosen = types[0];
            if (!string.IsNullOrEmpty(track.PreferredPlanetTypeId))
            {
                for (int i = 0; i < types.Count; i++)
                {
                    if (types[i].Id == track.PreferredPlanetTypeId)
                    {
                        chosen = types[i];
                        break;
                    }
                }
            }

            ulong seed = _menuRng.NextUInt();
            if (seed == 0)
            {
                seed = 1;
            }

            CreatureStage maxStage = _progression.MaxBossStage(_progression.PlayerLevelOf(_save.PlayerExp));
            CreatureStage stage = BossDraw.Stage(seed, maxStage);
            PlanetSize size = BossDraw.SizeFor(stage);
            Launch(new PlanetOption
            {
                Type = chosen,
                Seed = seed,
                BossStage = stage,
                Radius = RadiusFor(size),
                Elements = PlanetElementDraw.Draw(seed, size, chosen.ElementBias),
            });
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

        private void LaunchBeginnerQuestRun(CreatureDef playerStarter)
        {
            _bqPlayerStarterLine = playerStarter.LineId;
            _bqPlayerStarterId = playerStarter.Id;
            (string rivalLine, string friendLine) = BeginnerQuestRules.AssignStarterLines(playerStarter.LineId);
            CreatureDef rivalDef = FindStarterByLine(rivalLine) ?? playerStarter;
            CreatureDef friendDef = FindStarterByLine(friendLine) ?? playerStarter;
            _bqRivalStarterId = rivalDef.Id;
            _bqFriendStarterId = friendDef.Id;

            var types = new List<PlanetTypeDef>();
            foreach (GameDef def in _database.All)
            {
                if (def is PlanetTypeDef t)
                {
                    types.Add(t);
                }
            }

            (PlanetTypeDef bType, _, CreatureStage bStage, float bRadius, _) = BuildBeginnerOption(types);
            ulong randomSeed = (ulong)Environment.TickCount ^ ((ulong)_menuRng.NextUInt() << 16);
            if (randomSeed == 0)
            {
                randomSeed = BeginnerQuestSeed;
            }

            int starterExp = _progression.XpThreshold(_progression.StarterLevel);
            VesselKind vessel = BeginnerQuestRules.SignatureVessel(_bqCharacterId);

            // Prepare a temporary save copy if the player doesn't have a starter yet; persisted only on Victory (P7-02a).
            if (_save.StarterLine == null)
            {
                _save.ChooseStarter(playerStarter.LineId, playerStarter.Id, starterExp);
            }

            _runPartyIds.Clear();
            var party = new List<CreatureDef>();
            var partyExp = new List<int>();
            foreach (int id in _save.Party)
            {
                if (_database.TryGet(_save.Get(id).SpeciesId, out CreatureDef def))
                {
                    party.Add(def);
                    partyExp.Add(_save.Get(id).Exp);
                    _runPartyIds.Add(id);
                }
            }

            _activeRunIsBeginnerQuest = true;
            _menuInput.Disable();
            _confirmPending = false;
            _save.SetLastPlanet(new GearOrigin(bType.Id, randomSeed));
            _lobbyCamera.gameObject.SetActive(false);
            _run = new GameObject("Run").AddComponent<RunSession>();
            _run.Configure(_copy, _combatBalance, _actions, _terrainMaterial,
                new RunSetup
                {
                    PlanetType = bType,
                    Seed = randomSeed,
                    Radius = bRadius,
                    BossStage = bStage,
                    Party = party,
                    PlayerExp = _save.PlayerExp,
                    Codex = _save.Codex,
                    DiscoveredRecipes = _save.DiscoveredRecipesSnapshot(),
                    PartyIds = new List<int>(_runPartyIds),
                    PartyExp = partyExp,
                    Progression = _progression,
                    Materials = _materialDrops,
                    EnvMaterials = _env,
                    WeatherControlCooldownMs = _weatherControlCooldownMs,
                    CharacterModelPrefab = SelectedCharacterPrefab(),
                    Portraits = _portraits,
                    VolumeProfile = _volumeProfile,
                    Database = _database,
                    Owned = new List<OwnedCreature>(_save.Owned),
                    Partners = new PartnerLoadout(_runPartyIds),
                    CraftStock = _save.CraftStock.Snapshot(),
                    CraftLooks = _save.CraftStock.LookSnapshot(),
                    Equipment = _save.Equipment.Snapshot(),
                    IsBeginnerQuest = true,
                    FriendId = _bqFriendId,
                    FriendStarter = friendDef,
                    RivalId = _bqRivalId,
                    RivalStarter = rivalDef,
                    PlayerStarterVessel = vessel,
                }, _wildPool, OnRunEnded, _terrainShader, _toonShader, _starShader,
                new SceneryShaders(_skyShader, _grassShader, _waterShader, _sceneryShader));
            _run.Hints.Inherit(_menuHints);
            _screen = Screen.Running;
        }

        private string ResolveBqDialogue(DialogueLine line)
        {
            if (_copy != null && !string.IsNullOrEmpty(line.TextKey))
            {
                string localized = _copy.Ui(line.TextKey);
                if (localized != line.TextKey)
                {
                    return localized;
                }
            }

            return line.FallbackText;
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
