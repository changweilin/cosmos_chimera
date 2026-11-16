using System;
using System.Collections.Generic;
using Cosmimera.Crafting;
using Cosmimera.Combat;
using Cosmimera.Creatures;
using Cosmimera.Food;
using Cosmimera.GameData;
using Cosmimera.WorldGen;
using UnityEngine;

namespace Cosmimera.Bootstrap.Demo
{
    public sealed partial class RunSession
    {
        private CraftCatalog _craftCatalog;
        private readonly CraftInventory _craftInventory = new CraftInventory();
        private readonly CraftGathering _craftGathering = new CraftGathering();
        private readonly List<GatheringNode> _gatheringNodes = new List<GatheringNode>();
        private readonly List<CraftStack> _craftYields = new List<CraftStack>();
        private string _craftedVessel;
        private EquipmentModifiers _playerEquipment;

        private void ConfigureCrafting()
        {
            _craftCatalog = CraftJournal.LoadCatalog();
            if (_setup.CraftStock != null && !_craftInventory.TryExchange(Array.Empty<CraftStack>(), _setup.CraftStock))
                throw new InvalidOperationException("Departure crafting stock exceeds capacity");
        }

        private void DrawCrafting(AdventurePanel ui)
        {
            ui.Heading(_copy.Ui("ui.craft.gather"));
            ui.Label(_copy.Ui("ui.craft.once"));
            _gatheringNodes.Clear();
            _worldObjects?.QueryGathering(_rig.Config.ToDirection(PlayerPosition()), Encounter.InteractRadiusM, _gatheringNodes);
            bool any = false;
            string season = (_setup.Season ?? Seasons.For(_setup.Seed)).ToString().ToLowerInvariant();
            foreach (GatheringNode node in _gatheringNodes)
            {
                CraftResult result = _craftGathering.Preview(node.Id, node.Source, season, _craftCatalog,
                    _enabledUtilities, _craftInventory, _craftYields);
                if (result == CraftResult.NoResource || (result == CraftResult.MissingSkill && !_showLockedGathering)) continue;
                any = true;
                ui.Label(_copy.Ui("ui.craft.node").Replace("{source}", _copy.Ui("ui.object." + node.Source))
                    .Replace("{result}", _copy.Ui("craft.result." + result)));
                foreach (CraftStack stack in _craftYields) ui.Label("· " + _copy.Ui(stack.Id) + " ×" + stack.Quantity);
                if (!ui.Button(_copy.Ui("ui.skills.use"), result == CraftResult.Success)) continue;
                result = _craftGathering.TryGather(node.Id, node.Source, season, _craftCatalog, _enabledUtilities,
                    _craftInventory, _craftYields);
                ui.Message = _copy.Ui("craft.result." + result);
                GUIUtility.ExitGUI();
            }
            if (!any) ui.Label(_copy.Ui("ui.craft.no_nodes"));
            CraftRecipe selected = CraftJournal.DrawRecipes(ui, _copy, _craftCatalog, _craftInventory, _enabledUtilities, _showLockedGathering);
            if (selected == null) return;
            ui.Message = _copy.Ui("craft.result." + CraftRules.TryCraft(selected, _enabledUtilities, _craftInventory));
            GUIUtility.ExitGUI();
        }

        private void DrawCraftedItems(AdventurePanel ui)
        {
            CraftJournal.DrawStock(ui, _copy, _craftInventory);
            ui.Heading(_copy.Ui("ui.craft.equipment"));
            foreach (EquippedItem entry in _setup.Equipment ?? Array.Empty<EquippedItem>())
            {
                string wearer = entry.Owner == 0 ? _copy.Ui("ui.craft.player") : _copy.Name(OwnedAtDeparture(entry.Owner).SpeciesId);
                ui.Label(wearer + " · " + _copy.Ui("ui.craft.slot." + entry.Slot) + " · " + _copy.Ui(entry.Item));
                if (_craftCatalog.TryGetItem(entry.Item, out CraftItem gear)) CraftJournal.DrawGearStats(ui, _copy, gear);
            }
            if (_craftedVessel != null)
            {
                ui.Label(_copy.Ui("ui.craft.capture_armed").Replace("{item}", _copy.Ui(_craftedVessel)));
                if (ui.Button(_copy.Ui("ui.craft.capture_cancel"))) { _craftedVessel = null; GUIUtility.ExitGUI(); }
            }
            foreach (VesselKind vessel in Enum.GetValues(typeof(VesselKind)))
            {
                string id = CraftedVesselId(vessel);
                if (_craftInventory.Count(id) == 0) continue;
                if (!_craftCatalog.TryGetItem(id, out CraftItem item)) continue;
                string text = _copy.Ui("ui.craft.capture_use").Replace("{item}", _copy.Ui(id))
                    .Replace("{bonus}", item.CaptureBonusPercent.ToString());
                if (ui.Button(text)) { _vessel = vessel; _craftedVessel = id; GUIUtility.ExitGUI(); }
            }
            if (_craftInventory.Count("craft_snare") > 0 && ui.Button(_copy.Ui("ui.craft.trap_use")))
            {
                ui.Message = _copy.Ui("gather.reason." + TryUseCraftedTrap());
                GUIUtility.ExitGUI();
            }
        }

        private static string CraftedVesselId(VesselKind vessel)
        {
            switch (vessel)
            {
                case VesselKind.TalismanCard: return "craft_talisman_card";
                case VesselKind.GlassBead: return "craft_glass_bead";
                case VesselKind.MagicCube: return "craft_magic_cube";
                default: throw new ArgumentOutOfRangeException(nameof(vessel));
            }
        }

        private bool TryConsumeCraftedVessel(out int bonus)
        {
            bonus = 0;
            if (_craftedVessel == null) return true;
            if (_craftedVessel != CraftedVesselId(_vessel) || !_craftCatalog.TryGetItem(_craftedVessel, out CraftItem item) ||
                !_craftInventory.TryExchange(new[] { new CraftStack(_craftedVessel, 1) }, Array.Empty<CraftStack>())) return false;
            bonus = item.CaptureBonusPercent;
            _craftedVessel = null;
            return true;
        }

        private GatheringRefusalReason TryUseCraftedTrap()
        {
            if (_craftInventory.Count("craft_snare") == 0) return GatheringRefusalReason.NoPoolAvailable;
            GatheringContext environment = GatheringEnvironment();
            var utilities = new HashSet<FieldUtility>(_enabledUtilities) { FieldUtility.SetTrap };
            var context = new GatheringContext(FieldUtilityRules.ComputeTeamModifiers(utilities), environment.OnGround,
                environment.HasOpenSky, environment.NearWater, environment.HasFoliage, environment.HasFarmland, environment.ExtraDropBonusPercent);
            GatheringRefusalReason result = _traps.TryPlaceTrap(context, _nowMs, out _);
            if (result == GatheringRefusalReason.None)
                _craftInventory.TryExchange(new[] { new CraftStack("craft_snare", 1) }, Array.Empty<CraftStack>());
            return result;
        }

        private bool TryCollectTrapMaterials()
        {
            var utilities = new HashSet<FieldUtility>(_enabledUtilities) { FieldUtility.SetTrap };
            var materials = new List<CraftStack>();
            string season = (_setup.Season ?? Seasons.For(_setup.Seed)).ToString().ToLowerInvariant();
            foreach (TrapState trap in _traps.ActiveTraps)
            {
                if (!trap.IsReady(_nowMs)) continue;
                CraftResult result = _craftGathering.Preview((1L << 50) + trap.Id, "set_trap", season, _craftCatalog,
                    utilities, _craftInventory, _craftYields);
                if (result == CraftResult.CapacityExceeded) return false;
                if (result == CraftResult.Success) materials.AddRange(_craftYields);
            }
            if (!_craftInventory.CanExchange(Array.Empty<CraftStack>(), materials)) return false;
            int collected = _traps.CollectReadyTraps(_foodCatalog, _foodInventory, _nowMs, ref _foodRng, _gathered);
            if (collected > 0) _craftInventory.TryExchange(Array.Empty<CraftStack>(), materials);
            return true;
        }
    }
}
