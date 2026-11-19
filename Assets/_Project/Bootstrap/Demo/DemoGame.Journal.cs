using System.Collections.Generic;
using Cosmimera.Creatures;
using Cosmimera.Codex;
using Cosmimera.Food;
using Cosmimera.GameData;
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
            _partnerMain = _save.Party[0];
            _confirmPending = false;
        }

        private void CloseJournal()
        {
            _paperdollPreview?.Dispose();
            _paperdollPreview = null;
            _journal = null;
            _confirmPending = false;
            _menuFocus.SetCount(0);
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
                case AdventurePanel.Page.Partners: DrawPartners(ui); break;
                case AdventurePanel.Page.Codex: _codex.Draw(ui); break;
                case AdventurePanel.Page.Items:
                    ui.Heading(_copy.Ui("ui.journal.tab.Items"));
                    DrawMaterials();
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
                    DrawLobbyCrafting(ui, utilities);
                    ui.Label(_copy.Ui("ui.journal.run_skills"));
                    break;
            }
        }

        private void DrawPartners(AdventurePanel ui)
        {
            ui.Heading(_copy.Ui("ui.journal.tab.Partners"));
            ui.Label(_copy.Ui("ui.partner.hint"));
            foreach (int id in _save.Party)
            {
                if (ui.Button((id == _partnerMain ? "★ " : "") + PartyLabel(_save.Get(id))))
                { _partnerMain = id; GUIUtility.ExitGUI(); }
            }
            bool valid = TryBuildPartners(out PartnerLoadout loadout);
            if (!valid) ui.Label(_copy.Ui("ui.partner.repair"));
            int count = 0;
            foreach (PartnerPair pair in _save.Partners)
            {
                if (pair.MainInstanceId != _partnerMain) continue;
                count++;
                OwnedCreature sub = _save.Get(pair.SubInstanceId);
                ui.Label(PartyLabel(sub) + " · " + ProvisionLabel(sub));
                if (ui.Button(_copy.Ui("ui.partner.remove") + " #" + sub.InstanceId))
                {
                    var pairs = new List<PartnerPair>(_save.Partners);
                    pairs.RemoveAll(p => p.SubInstanceId == sub.InstanceId);
                    var candidate = _save.Clone();
                    candidate.SetPartners(pairs);
                    ui.Message = CommitLobby(candidate) ? string.Empty : _partyMsg;
                    GUIUtility.ExitGUI();
                }
            }
            ui.Label($"{count}/{PartnerLoadout.MaxSubsPerMain}");
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
                    // TryAssign is the rule's single authority; undo a successful preview before drawing the next row.
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
                    ui.Message = CommitLobby(candidate) ? string.Empty : _partyMsg;
                    GUIUtility.ExitGUI();
                }
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
