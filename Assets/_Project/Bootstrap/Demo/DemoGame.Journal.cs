using System;
using System.Collections.Generic;
using Cosmimera.Creatures;
using Cosmimera.Codex;
using Cosmimera.Food;
using Cosmimera.GameData;
using Cosmimera.Save;
using UnityEngine;

namespace Cosmimera.Bootstrap.Demo
{
    public sealed partial class DemoGame
    {
        private AdventurePanel _journal;
        private CodexPanel _codex;
        private int _partnerMain;

        /// <summary>Persists the first successful conversation with a named encyclopedia character.</summary>
        /// <remarks>Pre: characterId is a mapped character; game initialized. Post: basic knowledge survives restart. Side effects: transactional save I/O; throws on write failure.</remarks>
        public void RecordCharacterDialogue(string characterId) => RecordCharacterKnowledge(characterId, CodexEvent.Dialogue);

        /// <summary>Persists completion of a named character's quest.</summary>
        /// <remarks>Pre: characterId mapped; quest successfully completed. Post: full knowledge survives restart. Side effects: transactional save I/O; throws on write failure.</remarks>
        public void RecordCharacterQuest(string characterId) => RecordCharacterKnowledge(characterId, CodexEvent.CharacterQuest);

        private void RecordCharacterKnowledge(string characterId, CodexEvent trigger)
        {
            Core.Contracts.Contract.Requires(!string.IsNullOrEmpty(characterId) &&
                new EncyclopediaCatalog().Find(CodexKind.Character, characterId) != null, "character must exist in the encyclopedia");
            var candidate = _save.Clone();
            if (!candidate.Codex.Record(trigger, characterId)) return;
            candidate.Write(_savePath);
            _save = candidate;
        }

        private void OpenJournal() => OpenJournalAt(AdventurePanel.Page.Partners);

        private void OpenJournalAt(AdventurePanel.Page initialPage)
        {
            foreach (OwnedCreature creature in _save.Owned)
                if (_database.TryGet(creature.SpeciesId, out CreatureDef definition))
                    EncyclopediaCatalog.RecordOwned(_save.Codex, definition);
            _journal = new AdventurePanel(_copy, initialPage);
            _codex = new CodexPanel(_database, _copy, IsOwnedSpecies, _portraits, _save.Codex,
                FoodCatalog.CreateDefault(), _save.IsRecipeDiscovered);
            _partnerMain = _save.Party.Count > 0 ? _save.Party[0] : 0;
            _confirmPending = false;
        }

        private void CloseJournal()
        {
            _paperdollPreview?.Dispose();
            _paperdollPreview = null;
            _journal?.Dispose();
            _journal = null;
            _confirmPending = false;
            if (_screen == Screen.Results)
            {
                ShowResultsOverlay();
            }
        }

        private bool IsOwnedSpecies(string species)
        {
            foreach (OwnedCreature creature in _save.Owned)
                if (creature.SpeciesId == species) return true;
            return false;
        }

        private bool IsSub(int id)
        {
            foreach (PartnerPair pair in _save.Partners)
                if (pair.SubInstanceId == id) return true;
            return false;
        }

        private bool TryBuildPartners(out PartnerLoadout loadout)
        {
            loadout = new PartnerLoadout(_save.Party);
            var graph = new GameDatabaseRelationGraph(_database);
            var species = new GameDatabaseSpeciesLookup(_database);
            bool valid = true;
            foreach (int id in _save.Party)
                if (!_database.TryGet(_save.Get(id).SpeciesId, out CreatureDef _)) valid = false;
            foreach (PartnerPair pair in _save.Partners)
            {
                OwnedCreature main = _save.Get(pair.MainInstanceId);
                OwnedCreature sub = _save.Get(pair.SubInstanceId);
                if (!_database.TryGet(main.SpeciesId, out CreatureDef _) || !_database.TryGet(sub.SpeciesId, out CreatureDef _))
                { valid = false; continue; }
                valid &= loadout.TryAssign(main, sub, graph, species) == PartnerResult.Ok;
            }
            return valid;
        }

        private void DrawJournal(AdventurePanel ui)
        {
            switch (ui.Current)
            {
                case AdventurePanel.Page.Partners:
                    DrawPartnersInMenu(ui, allowEdit: true, onPartyChanged: null);
                    break;
                case AdventurePanel.Page.Codex: _codex.Draw(ui); break;
                case AdventurePanel.Page.Items:
                    ui.Heading(_copy.Ui("ui.journal.tab.Items"));
                    DrawMaterialsInMenu(ui);
                    CraftJournal.DrawStock(ui, _copy, _save.CraftStock);
                    DrawEquipment(ui);
                    ui.Label(_copy.Ui("ui.journal.run_items"));
                    break;
                case AdventurePanel.Page.Skills:
                    ui.Heading(_copy.Ui("ui.journal.tab.Skills"));
                    var mains = new List<OwnedCreature>();
                    var subs = new List<OwnedCreature>();
                    foreach (int id in _save.Party) mains.Add(_save.Get(id));
                    if (TryBuildPartners(out PartnerLoadout _))
                        foreach (PartnerPair pair in _save.Partners) subs.Add(_save.Get(pair.SubInstanceId));
                    var utilities = FieldUtilityRules.CollectTeamUtilities(mains, subs, new GameDatabaseSpeciesLookup(_database));
                    foreach (FieldUtility utility in utilities)
                        ui.Label(_copy.Ui("utility." + utility) + " · " + _copy.Ui("utility.desc." + utility));
                    ui.Label("配方製作請前往主世界各聚落尋找「工匠師傅」NPC。");
                    ui.Label(_copy.Ui("ui.journal.run_skills"));
                    break;
                case AdventurePanel.Page.System:
                    DrawSystemMenuPage(ui);
                    break;
            }
        }

        private void DrawMaterialsInMenu(AdventurePanel ui)
        {
            ui.Label(_copy.Ui("ui.lobby.materials"));
            IReadOnlyList<MaterialStack> stock = _save.Materials.Snapshot();
            if (stock.Count == 0)
            {
                ui.Label("· " + _copy.Ui("ui.lobby.materials_empty"));
                return;
            }

            var parts = new List<string>(stock.Count);
            foreach (MaterialStack m in stock)
            {
                parts.Add(_copy.Material(m.Key) + " ×" + m.Qty);
            }

            ui.Label("· " + string.Join("  ", parts));
        }

        private void DrawSystemMenuPage(AdventurePanel ui)
        {
            ui.Heading(_copy.Ui("ui.journal.tab.System"));
            if (ui.Button(_copy.Ui("ui.lobby.language") + ": " + _language))
            {
                CycleLanguage();
                GUIUtility.ExitGUI();
            }

            if (ui.Button("開發者測試存檔 (Dev F9)"))
            {
                CloseJournal();
                OpenDevTestSavesOverlay();
                GUIUtility.ExitGUI();
            }

            if (ui.Button(_copy.Ui("ui.lobby.quit")))
            {
                Application.Quit();
                GUIUtility.ExitGUI();
            }
        }

        internal void DrawPartnersInMenu(AdventurePanel ui, bool allowEdit, Action onPartyChanged)
        {
            ui.Heading(PlayerLevelLabel());
            ui.Heading($"{_copy.Ui("ui.lobby.team")} ({_save.Party.Count}/{LobbySave.MaxParty})");
            if (!allowEdit)
            {
                ui.Label(_copy.Ui("ui.partner.lobby_only"));
            }
            else
            {
                ui.Label(_copy.Ui("ui.lobby.party_hint"));
            }

            if (_save.Party.Count > 0 && !ContainsParty(_partnerMain))
            {
                _partnerMain = _save.Party[0];
            }

            var party = new List<int>(_save.Party);
            for (int i = 0; i < party.Count; i++)
            {
                OwnedCreature o = _save.Get(party[i]);
                ui.BeginRow();
                ui.Label((party[i] == _partnerMain ? "★ " : "· ") + PartyLabel(o));
                if (ui.Button("設為副手配置主力"))
                {
                    _partnerMain = party[i];
                    ui.EndRow();
                    GUIUtility.ExitGUI();
                }

                if (allowEdit)
                {
                    if (ui.Button(_copy.Ui("ui.lobby.move_up"), i > 0))
                    {
                        (party[i - 1], party[i]) = (party[i], party[i - 1]);
                        if (CommitParty(party)) onPartyChanged?.Invoke();
                        ui.EndRow();
                        GUIUtility.ExitGUI();
                    }

                    if (HasEvolveButton(o) && ui.Button(_copy.Ui("ui.lobby.evolve")))
                    {
                        TryEvolveInMenu(ui, o, onPartyChanged);
                        ui.EndRow();
                        GUIUtility.ExitGUI();
                    }

                    if (ui.Button(_copy.Ui("ui.lobby.remove_from_party"), party.Count > 1))
                    {
                        party.RemoveAt(i);
                        if (CommitParty(party)) onPartyChanged?.Invoke();
                        ui.EndRow();
                        GUIUtility.ExitGUI();
                    }
                }
                ui.EndRow();
            }

            ui.Heading(_copy.Ui("ui.lobby.reserve"));
            foreach (OwnedCreature o in _save.Owned)
            {
                if (party.Contains(o.InstanceId)) continue;
                ui.BeginRow();
                ui.Label("· " + PartyLabel(o));
                if (allowEdit)
                {
                    if (HasEvolveButton(o) && ui.Button(_copy.Ui("ui.lobby.evolve")))
                    {
                        TryEvolveInMenu(ui, o, onPartyChanged);
                        ui.EndRow();
                        GUIUtility.ExitGUI();
                    }

                    bool canAdd = !IsSub(o.InstanceId) && party.Count < LobbySave.MaxParty;
                    if (ui.Button(_copy.Ui("ui.lobby.add_to_party"), canAdd))
                    {
                        party.Add(o.InstanceId);
                        if (CommitParty(party)) onPartyChanged?.Invoke();
                        ui.EndRow();
                        GUIUtility.ExitGUI();
                    }
                }
                ui.EndRow();
            }

            if (_save.Party.Count == 0)
            {
                return;
            }

            ui.Heading(_copy.Ui("ui.journal.tab.Partners") + " · " + PartyLabel(_save.Get(_partnerMain)));
            ui.Label(_copy.Ui("ui.partner.hint"));
            bool valid = TryBuildPartners(out PartnerLoadout loadout);
            if (!valid) ui.Label(_copy.Ui("ui.partner.repair"));
            int count = 0;
            foreach (PartnerPair pair in _save.Partners)
            {
                if (pair.MainInstanceId != _partnerMain) continue;
                count++;
                OwnedCreature sub = _save.Get(pair.SubInstanceId);
                ui.Label(PartyLabel(sub) + " · " + ProvisionLabel(sub));
                if (allowEdit && ui.Button(_copy.Ui("ui.partner.remove") + " #" + sub.InstanceId))
                {
                    var pairs = new List<PartnerPair>(_save.Partners);
                    pairs.RemoveAll(p => p.SubInstanceId == sub.InstanceId);
                    var candidate = _save.Clone();
                    candidate.SetPartners(pairs);
                    bool ok = CommitLobby(candidate);
                    ui.Message = ok ? string.Empty : _partyMsg;
                    if (ok) onPartyChanged?.Invoke();
                    GUIUtility.ExitGUI();
                }
            }
            ui.Label($"{count}/{PartnerLoadout.MaxSubsPerMain}");
            if (!allowEdit)
            {
                return;
            }

            OwnedCreature main = _save.Get(_partnerMain);
            var graph = new GameDatabaseRelationGraph(_database);
            var species = new GameDatabaseSpeciesLookup(_database);
            foreach (OwnedCreature sub in _save.Owned)
            {
                if (ContainsParty(sub.InstanceId)) continue;
                bool known = _database.TryGet(sub.SpeciesId, out CreatureDef _);
                PartnerResult result = PartnerResult.NoRelation;
                if (valid && known)
                {
                    result = loadout.TryAssign(main, sub, graph, species);
                    if (result == PartnerResult.Ok) loadout.Unassign(sub.InstanceId);
                }
                ui.Label(PartyLabel(sub) + " · " + ProvisionLabel(sub));
                if (result != PartnerResult.Ok) ui.Label(_copy.Ui("ui.partner.reason." + result));
                if (ui.Button(_copy.Ui("ui.partner.equip") + " #" + sub.InstanceId, valid && known && result == PartnerResult.Ok))
                {
                    var candidate = _save.Clone();
                    var pairs = new List<PartnerPair>(_save.Partners) { new PartnerPair(_partnerMain, sub.InstanceId) };
                    candidate.SetPartners(pairs);
                    bool ok = CommitLobby(candidate);
                    ui.Message = ok ? string.Empty : _partyMsg;
                    if (ok) onPartyChanged?.Invoke();
                    GUIUtility.ExitGUI();
                }
            }
        }

        private void TryEvolveInMenu(AdventurePanel ui, OwnedCreature o, Action onPartyChanged)
        {
            if (!HasEvolveButton(o)) return;
            _database.TryGet(o.SpeciesId, out CreatureDef def);
            EvolutionRequirement req = EvolutionRequirement.FromDef(def);
            _database.TryGet(req.NextSpeciesId, out CreatureDef nextDef);
            CommitStatus status = LobbyTransactions.Evolve(_save, o.InstanceId, req, _progression, _savePath,
                out LobbySave next, out EvolutionResult r, out System.Exception error, nextDef);
            if (status == CommitStatus.Committed)
            {
                _save = next;
                SyncRunPartyIdsFromSave();
                ui.Message = _copy.Ui("ui.lobby.evolve_success").Replace("{from}", _copy.Name(o.SpeciesId))
                    .Replace("{to}", _copy.Name(r.Creature.SpeciesId));
                onPartyChanged?.Invoke();
            }
            else if (status == CommitStatus.Refused)
            {
                ui.Message = DescribeRefusal(req, r.Check);
            }
            else
            {
                Debug.LogException(error);
                ui.Message = _copy.Ui("ui.lobby.save_failed");
            }
        }

        private bool ContainsParty(int id)
        {
            foreach (int member in _save.Party) if (member == id) return true;
            return false;
        }

        private string ProvisionLabel(OwnedCreature sub)
        {
            if (!_database.TryGet(sub.SpeciesId, out CreatureDef def)) return _copy.Ui("ui.partner.unknown");
            string provision = sub.Vessel == VesselKind.TalismanCard ? _copy.Name(def.SignatureMove.Id)
                : sub.Vessel == VesselKind.GlassBead ? _copy.Name(def.Trait.Id) : _copy.Ui("ui.partner.overload");
            return _copy.Ui("ui.vessel." + VesselKey(sub.Vessel)) + " → " + provision;
        }
    }
}
