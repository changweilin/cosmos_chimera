using System;
using System.Collections.Generic;
using Cosmimera.Capture;
using Cosmimera.Codex;
using Cosmimera.Creatures;
using Cosmimera.GameData;
using Cosmimera.Save;
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

        private void DrawBeginnerQuest()
        {
            GUILayout.Label("新手任務 · 搖籃星 (R=320 m)", _title, GUILayout.Height(56));
            switch (_bqStage)
            {
                case BeginnerQuestStage.CharacterSelect:
                    DrawBqCharacterSelect();
                    break;
                case BeginnerQuestStage.FriendChat:
                    DrawBqDialogue();
                    break;
                case BeginnerQuestStage.RivalAndStarter:
                    DrawBqRivalAndStarter();
                    break;
            }
        }

        private void DrawBqCharacterSelect()
        {
            GUILayout.Label("階段 1/7：選擇你的冒險者（好友與宿敵將依環狀順序自動決定）", _text);
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
                    (_bqFriendId, _bqRivalId) = BeginnerQuestRules.AssignCompanions(cid);
                    _bqDialogue.Clear();
                    _bqDialogue.AddRange(BeginnerQuestRules.FriendIntroDialogue(_bqFriendId));
                    _bqDialogueIndex = 0;
                    _bqStage = BeginnerQuestStage.FriendChat;
                    _menuFocus.SetCount(0);
                    GUIUtility.ExitGUI();
                }
            }
        }

        private void DrawBqDialogue()
        {
            if (_bqDialogueIndex >= _bqDialogue.Count)
            {
                _bqStage = BeginnerQuestStage.RivalAndStarter;
                _menuFocus.SetCount(0);
                return;
            }

            DialogueLine line = _bqDialogue[_bqDialogueIndex];
            GUILayout.Label($"好友 {line.Speaker}：", _title, GUILayout.Height(40));
            GUILayout.Space(8);
            GUILayout.Label(line.FallbackText, _text);
            GUILayout.Space(20);
            if (MenuButton("繼續 [Enter / Space / A]", GUILayout.Height(48)))
            {
                _bqDialogueIndex++;
                if (_bqDialogueIndex >= _bqDialogue.Count)
                {
                    _bqStage = BeginnerQuestStage.RivalAndStarter;
                    _menuFocus.SetCount(0);
                }

                GUIUtility.ExitGUI();
            }
        }

        private void DrawBqRivalAndStarter()
        {
            string rivalName = BeginnerQuestRules.CharacterDisplayName(_bqRivalId);
            string friendName = BeginnerQuestRules.CharacterDisplayName(_bqFriendId);
            GUILayout.Label($"階段 3/7：宿敵「{rivalName}」登場！選擇你的初始御三家（LV10）", _text);
            GUILayout.Label($"宿敵會選擇剋制你的屬性；好友「{friendName}」會拿走剩下那一隻。", _text);
            GUILayout.Space(12);

            foreach (CreatureDef s in _starters)
            {
                Texture2D portrait = _portraits != null ? _portraits.FindCreature(s.LineId, s.Stage) : null;
                if (portrait != null)
                {
                    Rect r = GUILayoutUtility.GetRect(64f, 64f, GUILayout.ExpandWidth(false));
                    GUI.DrawTexture(r, portrait, ScaleMode.ScaleToFit, true);
                }

                if (MenuButton($"{_copy.Name(s.Id)}  —  選擇此御三家並展開初戰", GUILayout.Height(52)))
                {
                    LaunchBeginnerQuestRun(s);
                    GUIUtility.ExitGUI();
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
                    Equipment = _save.Equipment.Snapshot(),
                    IsBeginnerQuest = true,
                    RivalId = _bqRivalId,
                    RivalStarter = rivalDef,
                    PlayerStarterVessel = vessel,
                }, _wildPool, OnRunEnded, _terrainShader, _toonShader, _starShader,
                new SceneryShaders(_skyShader, _grassShader, _waterShader, _sceneryShader));
            _run.Hints.Inherit(_menuHints);
            _screen = Screen.Running;
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
