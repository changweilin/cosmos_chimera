using System.Collections.Generic;
using Cosmimera.Crafting;
using Cosmimera.Creatures;
using Cosmimera.GameData;
using UnityEngine;

namespace Cosmimera.Bootstrap.Demo
{
    public sealed partial class DemoGame
    {
        private CraftCatalog _craftCatalog;
        private int _equipmentOwner;
        private bool _showLockedCrafts;
        private PaperdollPreview _paperdollPreview;

        private CraftCatalog CraftCatalog => _craftCatalog ??= CraftJournal.LoadCatalog();

        private void DrawEquipment(AdventurePanel ui)
        {
            if (_equipmentOwner != 0 && !ContainsParty(_equipmentOwner) && !IsSub(_equipmentOwner))
            {
                _equipmentOwner = 0;
            }

            ui.Heading(_copy.Ui("ui.paperdoll.title"));
            ui.Label(_copy.Ui("ui.craft.gear_preview"));

            DrawWearerSelector(ui);
            GUILayout.Space(8f);

            string weaponId = FindEquippedItem(_equipmentOwner, "weapon");
            string armorId = FindEquippedItem(_equipmentOwner, "armor");
            string accessoryId = FindEquippedItem(_equipmentOwner, "accessory");

            UpdatePaperdollPreview(weaponId, armorId, accessoryId);

            GUILayout.BeginHorizontal();

            // Left: surrounding equipment slots + 3D paperdoll preview
            GUILayout.BeginVertical(GUILayout.Width(500f));
            GUILayout.BeginHorizontal();

            GUILayout.BeginVertical(GUILayout.Width(148f));
            DrawPaperdollSlot(ui, "weapon", weaponId);
            GUILayout.Space(8f);
            DrawPaperdollSlot(ui, "accessory", accessoryId);
            GUILayout.EndVertical();

            GUILayout.BeginVertical(GUILayout.Width(196f));
            DrawPaperdollViewport(ui);
            GUILayout.EndVertical();

            GUILayout.BeginVertical(GUILayout.Width(148f));
            DrawPaperdollSlot(ui, "armor", armorId);
            GUILayout.Space(8f);
            DrawWearerBonusSummary(ui);
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            GUILayout.Space(10f);

            // Right: equippable gear in bag
            GUILayout.BeginVertical();
            ui.Heading(_copy.Ui("ui.paperdoll.available_gear"));
            bool anyAvailable = false;
            foreach (CraftItem item in CraftCatalog.Items)
            {
                if (item.Equipment == null || !item.Equipment.Allows(_equipmentOwner) || _save.CraftStock.Count(item.Id) == 0)
                {
                    continue;
                }

                anyAvailable = true;
                GUILayout.BeginVertical(GUI.skin.box);
                ui.Label("[" + _copy.Ui("ui.craft.slot." + item.Equipment.Slot) + "] " + _copy.Ui(item.Id)
                    + " ×" + _save.CraftStock.Count(item.Id));
                CraftJournal.DrawGearStats(ui, _copy, item);
                if (ui.Button(_copy.Ui("ui.craft.equip")))
                {
                    var candidate = _save.Clone();
                    ui.Message = candidate.Equipment.TryEquip(_equipmentOwner, item.Id, CraftCatalog, candidate.CraftStock)
                        ? (CommitLobby(candidate) ? string.Empty : _partyMsg) : _copy.Ui("ui.craft.equip_failed");
                    GUIUtility.ExitGUI();
                }
                GUILayout.EndVertical();
            }

            if (!anyAvailable)
            {
                ui.Label(_copy.Ui("ui.paperdoll.none_available"));
            }
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
        }

        private void DrawWearerSelector(AdventurePanel ui)
        {
            string playerName = _characterModels != null && _selectedCharacterIndex < _characterModels.Entries.Count
                ? _characterModels.Entries[_selectedCharacterIndex].displayName
                : _copy.Ui("ui.craft.player");

            GUILayout.BeginHorizontal();
            if (ui.Button((_equipmentOwner == 0 ? "● " : "") + _copy.Ui("ui.craft.player") + " (" + playerName + ")"))
            {
                _equipmentOwner = 0;
                GUIUtility.ExitGUI();
            }
            if (ui.Button(_copy.Ui("ui.paperdoll.manage_partners")))
            {
                ui.SetPage(AdventurePanel.Page.Partners);
                GUIUtility.ExitGUI();
            }
            GUILayout.EndHorizontal();

            foreach (int mainId in _save.Party)
            {
                if (ui.Button((_equipmentOwner == mainId ? "● " : "") + "★ " + PartyLabel(_save.Get(mainId))))
                {
                    _equipmentOwner = mainId;
                    GUIUtility.ExitGUI();
                }

                foreach (PartnerPair pair in _save.Partners)
                {
                    if (pair.MainInstanceId != mainId) continue;
                    OwnedCreature sub = _save.Get(pair.SubInstanceId);
                    if (ui.Button((_equipmentOwner == sub.InstanceId ? "● " : "") + "  └ "
                        + _copy.Ui("ui.paperdoll.sub_prefix") + " " + PartyLabel(sub)))
                    {
                        _equipmentOwner = sub.InstanceId;
                        GUIUtility.ExitGUI();
                    }
                }
            }
        }

        private void DrawPaperdollViewport(AdventurePanel ui)
        {
            if (_paperdollPreview?.Texture != null)
            {
                Rect previewRect = GUILayoutUtility.GetRect(192f, 264f, GUILayout.ExpandWidth(false));
                GUI.Box(previewRect, GUIContent.none);
                GUI.DrawTexture(previewRect, _paperdollPreview.Texture, ScaleMode.ScaleToFit, false);

                Event evt = Event.current;
                if (evt.type == EventType.MouseDrag && previewRect.Contains(evt.mousePosition))
                {
                    _paperdollPreview.Rotate(-evt.delta.x * 1.2f);
                    evt.Use();
                }
            }

            GUILayout.BeginHorizontal();
            if (ui.Button(_copy.Ui("ui.paperdoll.rotate_left")))
            {
                _paperdollPreview?.Rotate(-30f);
                GUIUtility.ExitGUI();
            }
            if (ui.Button(_copy.Ui("ui.paperdoll.rotate_right")))
            {
                _paperdollPreview?.Rotate(30f);
                GUIUtility.ExitGUI();
            }
            GUILayout.EndHorizontal();
            ui.Label(_copy.Ui("ui.paperdoll.drag_hint"));
        }

        private void DrawPaperdollSlot(AdventurePanel ui, string slot, string equippedId)
        {
            GUILayout.BeginVertical(GUI.skin.box);
            ui.Label(_copy.Ui("ui.craft.slot." + slot));
            if (string.IsNullOrEmpty(equippedId))
            {
                ui.Label("· " + _copy.Ui("ui.paperdoll.empty_slot"));
            }
            else
            {
                ui.Label(_copy.Ui(equippedId));
                if (CraftCatalog.TryGetItem(equippedId, out CraftItem item))
                {
                    CraftJournal.DrawGearStats(ui, _copy, item);
                }
                if (ui.Button(_copy.Ui("ui.craft.unequip")))
                {
                    var candidate = _save.Clone();
                    ui.Message = candidate.Equipment.TryUnequip(_equipmentOwner, slot, candidate.CraftStock)
                        ? (CommitLobby(candidate) ? string.Empty : _partyMsg) : _copy.Ui("ui.craft.equip_failed");
                    GUIUtility.ExitGUI();
                }
            }
            GUILayout.EndVertical();
        }

        private void DrawWearerBonusSummary(AdventurePanel ui)
        {
            EquipmentModifiers mods = EquipmentRules.Collect(_equipmentOwner, _save.Equipment.Snapshot(), CraftCatalog);
            GUILayout.BeginVertical(GUI.skin.box);
            if (_equipmentOwner == 0)
            {
                ui.Label(_copy.Ui("ui.craft.player_stats")
                    .Replace("{hp}", mods.PlayerHpPercent.ToString("+0;-0;0"))
                    .Replace("{speed}", mods.PlayerSpeedPercent.ToString("+0;-0;0"))
                    .Replace("{roll}", mods.PlayerRollPercent.ToString("+0;-0;0")));
            }
            else if (mods.CreatureEffects.Count == 0)
            {
                ui.Label(_copy.Ui("ui.paperdoll.empty_slot"));
            }
            else
            {
                foreach (Combat.EffectSpec effect in mods.CreatureEffects)
                {
                    bool scoped = effect.Type == EffectType.MasteryMod
                        || effect.Type == EffectType.ResistMod
                        || effect.Type == EffectType.CdModPercent;
                    ui.Label(_copy.Ui("ui.craft.effect")
                        .Replace("{effect}", _copy.Ui("ui.craft.effect." + effect.Type))
                        .Replace("{delta}", effect.Delta.ToString("+0;-0;0"))
                        .Replace("{element}", !scoped ? "" : effect.AnyElement ? _copy.Ui("ui.craft.any_element") : _copy.Element(effect.Element.ToString())));
                }
            }
            GUILayout.EndVertical();
        }

        private string FindEquippedItem(int owner, string slot)
        {
            foreach (EquippedItem entry in _save.Equipment.Snapshot())
            {
                if (entry.Owner == owner && entry.Slot == slot)
                {
                    return entry.Item;
                }
            }
            return string.Empty;
        }

        private void UpdatePaperdollPreview(string weaponId, string armorId, string accessoryId)
        {
            _paperdollPreview ??= new PaperdollPreview(_toonShader);
            if (_equipmentOwner == 0)
            {
                CharacterModelEntry? entry = _characterModels != null && _selectedCharacterIndex >= 0
                    && _selectedCharacterIndex < _characterModels.Entries.Count
                    ? _characterModels.Entries[_selectedCharacterIndex]
                    : (CharacterModelEntry?)null;
                _paperdollPreview.SetPlayerTarget(
                    entry?.prefab,
                    entry?.animatorController,
                    entry?.id ?? "capsule",
                    weaponId, armorId, accessoryId);
            }
            else
            {
                OwnedCreature owned = _save.Get(_equipmentOwner);
                _database.TryGet(owned.SpeciesId, out CreatureDef def);
                _paperdollPreview.SetCreatureTarget(def, _equipmentOwner, weaponId, armorId, accessoryId);
            }

            if (Event.current.type == EventType.Repaint)
            {
                _paperdollPreview.Render();
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
