using System.Collections.Generic;
using Cosmimera.Crafting;
using Cosmimera.Creatures;
using UnityEngine;

namespace Cosmimera.Bootstrap.Demo
{
    public sealed partial class DemoGame
    {
        private CraftCatalog _craftCatalog;
        private int _equipmentOwner;
        private bool _showLockedCrafts;

        private CraftCatalog CraftCatalog => _craftCatalog ??= CraftJournal.LoadCatalog();

        private void DrawEquipment(AdventurePanel ui)
        {
            if (_equipmentOwner != 0 && !ContainsParty(_equipmentOwner)) _equipmentOwner = 0;
            ui.Heading(_copy.Ui("ui.craft.equipment"));
            ui.Label(_copy.Ui("ui.craft.gear_preview"));
            if (ui.Button((_equipmentOwner == 0 ? "● " : "") + _copy.Ui("ui.craft.player")))
            { _equipmentOwner = 0; GUIUtility.ExitGUI(); }
            foreach (int owner in _save.Party)
                if (ui.Button((_equipmentOwner == owner ? "● " : "") + PartyLabel(_save.Get(owner))))
                { _equipmentOwner = owner; GUIUtility.ExitGUI(); }
            foreach (EquippedItem entry in _save.Equipment.Snapshot())
            {
                if (entry.Owner != _equipmentOwner) continue;
                ui.Label(_copy.Ui("ui.craft.slot." + entry.Slot) + " · " + _copy.Ui(entry.Item));
                if (CraftCatalog.TryGetItem(entry.Item, out CraftItem item)) CraftJournal.DrawGearStats(ui, _copy, item);
                if (!ui.Button(_copy.Ui("ui.craft.unequip"))) continue;
                var candidate = _save.Clone();
                ui.Message = candidate.Equipment.TryUnequip(entry.Owner, entry.Slot, candidate.CraftStock)
                    ? (CommitLobby(candidate) ? string.Empty : _partyMsg) : _copy.Ui("ui.craft.equip_failed");
                GUIUtility.ExitGUI();
            }
            foreach (CraftItem item in CraftCatalog.Items)
            {
                if (item.Equipment == null || !item.Equipment.Allows(_equipmentOwner) || _save.CraftStock.Count(item.Id) == 0) continue;
                ui.Label(_copy.Ui(item.Id));
                CraftJournal.DrawGearStats(ui, _copy, item);
                if (!ui.Button(_copy.Ui("ui.craft.equip"))) continue;
                var candidate = _save.Clone();
                ui.Message = candidate.Equipment.TryEquip(_equipmentOwner, item.Id, CraftCatalog, candidate.CraftStock)
                    ? (CommitLobby(candidate) ? string.Empty : _partyMsg) : _copy.Ui("ui.craft.equip_failed");
                GUIUtility.ExitGUI();
            }
        }

        private void DrawLobbyCrafting(AdventurePanel ui, HashSet<GameData.FieldUtility> utilities)
        {
            if (ui.Button(_copy.Ui(_showLockedCrafts ? "ui.skills.hide_locked" : "ui.skills.show_locked")))
            { _showLockedCrafts = !_showLockedCrafts; GUIUtility.ExitGUI(); }
            CraftRecipe recipe = CraftJournal.DrawRecipes(ui, _copy, CraftCatalog, _save.CraftStock, utilities, _showLockedCrafts);
            if (recipe == null) return;
            var candidate = _save.Clone();
            CraftResult result = CraftRules.TryCraft(recipe, utilities, candidate.CraftStock);
            ui.Message = result == CraftResult.Success
                ? (CommitLobby(candidate) ? _copy.Ui("craft.result.Success") : _partyMsg) : _copy.Ui("craft.result." + result);
            GUIUtility.ExitGUI();
        }
    }
}
