using System;
using System.Collections.Generic;
using System.Text;
using Cosmimera.Crafting;
using Cosmimera.Creatures;
using Cosmimera.GameData;
using Cosmimera.Save;
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
            DrawPresetBar(ui);
            GUILayout.Space(8f);

            IReadOnlyCollection<GripMode> grips = WearerGrips(_equipmentOwner);
            if (_equipmentOwner != 0)
            {
                var names = new List<string>();
                foreach (GripMode mode in grips) names.Add(_copy.Ui("ui.craft.grip." + mode));
                ui.Label(_copy.Ui("ui.craft.grips").Replace("{grips}", string.Join(" / ", names)));
            }

            UpdatePaperdollPreview(EquippedGear(_equipmentOwner));

            GUILayout.BeginHorizontal();

            // Left: surrounding equipment slots + 3D paperdoll preview
            GUILayout.BeginVertical(GUILayout.Width(500f));
            GUILayout.BeginHorizontal();

            GUILayout.BeginVertical(GUILayout.Width(148f));
            DrawPaperdollSlot(ui, EquipmentSlots.MainHand);
            DrawPaperdollSlot(ui, EquipmentSlots.OffHand);
            DrawPaperdollSlot(ui, EquipmentSlots.Accessory);
            GUILayout.EndVertical();

            GUILayout.BeginVertical(GUILayout.Width(196f));
            DrawPaperdollViewport(ui);
            GUILayout.EndVertical();

            GUILayout.BeginVertical(GUILayout.Width(148f));
            DrawPaperdollSlot(ui, EquipmentSlots.Upper);
            DrawPaperdollSlot(ui, EquipmentSlots.Lower);
            GUILayout.Space(8f);
            DrawWearerBonusSummary(ui, grips);
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
                ui.Label("[" + _copy.Ui("ui.craft.kind." + item.Equipment.Kind) + "] " + _copy.Ui(item.Id)
                    + " ×" + _save.CraftStock.Count(item.Id));
                CraftJournal.DrawGearStats(ui, _copy, item);
                int copy = DrawCopyPicker(ui, item.Id);
                foreach (string slot in EquipmentSlots.All)
                {
                    if (!item.Equipment.Fits(slot) || !ui.Button(_copy.Ui("ui.craft.equip_to").Replace("{slot}", _copy.Ui("ui.craft.slot." + slot))))
                    {
                        continue;
                    }

                    var candidate = _save.Clone();
                    ui.Message = candidate.Equipment.TryEquip(_equipmentOwner, item.Id, slot, grips, CraftCatalog, candidate.CraftStock, copy)
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

        // REQ: gear-looks.md#外觀與數值 — with several copies of one item, the player flips through them and picks one.
        private readonly Dictionary<string, int> _copyPick = new Dictionary<string, int>(StringComparer.Ordinal);

        private int DrawCopyPicker(AdventurePanel ui, string id)
        {
            int total = _save.CraftStock.Count(id);
            _copyPick.TryGetValue(id, out int pick);
            pick = Mathf.Clamp(pick, 0, total - 1);
            ItemLook look = _save.CraftStock.LookOf(id, pick);
            string origin = look.Origin.PlanetTypeId == GearOrigin.CradlePlanetTypeId
                ? string.Empty : " · " + _copy.Ui("ui.paperdoll.copy_origin").Replace("{planet}", _copy.Planet(look.Origin.PlanetTypeId));
            string label = _copy.Ui("ui.paperdoll.copy_pick").Replace("{n}", (pick + 1).ToString())
                .Replace("{total}", total.ToString()) + origin;
            if (total <= 1)
            {
                ui.Label(label);
            }
            else
            {
                GUILayout.BeginHorizontal();
                if (ui.Button("◀")) pick = (pick + total - 1) % total;
                ui.Label(label);
                if (ui.Button("▶")) pick = (pick + 1) % total;
                GUILayout.EndHorizontal();
            }
            _copyPick[id] = pick;
            return pick;
        }

        private int _presetEditSlot = -1;
        private string _presetEditText = string.Empty;

        private void DrawPresetBar(AdventurePanel ui)
        {
            ui.Heading(_copy.Ui("ui.preset.title"));
            for (int slot = 0; slot < LobbySave.MaxPresets; slot++)
            {
                LoadoutPreset preset = _save.GetPreset(slot);
                if (preset == null && _presetEditSlot == slot) _presetEditSlot = -1;
                GUILayout.BeginHorizontal();
                if (_presetEditSlot == slot)
                {
                    _presetEditText = GUILayout.TextField(_presetEditText, LoadoutPreset.MaxNameLength, GUILayout.Width(200f));
                    bool valid = !string.IsNullOrWhiteSpace(_presetEditText);
                    bool wasEnabled = GUI.enabled;
                    GUI.enabled = wasEnabled && valid;
                    if (ui.Button(_copy.Ui("ui.preset.rename_ok")))
                    {
                        var candidate = _save.Clone();
                        candidate.RenamePreset(slot, _presetEditText.Trim());
                        ui.Message = CommitLobby(candidate) ? string.Empty : _partyMsg;
                        _presetEditSlot = -1;
                        GUI.enabled = wasEnabled;
                        GUIUtility.ExitGUI();
                    }

                    GUI.enabled = wasEnabled;
                    if (ui.Button(_copy.Ui("ui.preset.rename_cancel")))
                    {
                        _presetEditSlot = -1;
                        GUIUtility.ExitGUI();
                    }

                    GUILayout.EndHorizontal();
                    continue;
                }

                ui.Label((slot + 1) + ". " + (preset != null ? preset.Name : _copy.Ui("ui.preset.empty")));
                if (preset != null && ui.Button(_copy.Ui("ui.preset.rename")))
                {
                    _presetEditSlot = slot;
                    _presetEditText = preset.Name;
                    GUIUtility.ExitGUI();
                }

                if (ui.Button(_copy.Ui("ui.preset.save")))
                {
                    var candidate = _save.Clone();
                    candidate.SavePreset(slot, preset != null ? preset.Name
                        : _copy.Ui("ui.preset.default_name").Replace("{n}", (slot + 1).ToString()));
                    if (CommitLobby(candidate)) ui.Message = string.Empty; else ui.Message = _partyMsg;
                    GUIUtility.ExitGUI();
                }

                if (preset != null && ui.Button(_copy.Ui("ui.preset.apply")))
                {
                    var candidate = _save.Clone();
                    PresetApplyResult result = candidate.TryApplyPreset(slot, CraftCatalog);
                    if (!result.Applied) ui.Message = _copy.Ui("ui.preset.failed");
                    else if (!CommitLobby(candidate)) ui.Message = _partyMsg;
                    else ui.Message = (result.MissingCreatures + result.MissingItems == 0) ? string.Empty
                        : _copy.Ui("ui.preset.applied").Replace("{creatures}", result.MissingCreatures.ToString())
                            .Replace("{items}", result.MissingItems.ToString());
                    GUIUtility.ExitGUI();
                }

                if (preset != null && ui.Button(_copy.Ui("ui.preset.clear")))
                {
                    var candidate = _save.Clone();
                    candidate.ClearPreset(slot);
                    if (CommitLobby(candidate)) ui.Message = string.Empty; else ui.Message = _partyMsg;
                    GUIUtility.ExitGUI();
                }

                GUILayout.EndHorizontal();
            }
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

        private void DrawPaperdollSlot(AdventurePanel ui, string slot)
        {
            string equippedId = FindEquippedItem(_equipmentOwner, slot);
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

        private void DrawWearerBonusSummary(AdventurePanel ui, IReadOnlyCollection<GripMode> grips)
        {
            EquipmentModifiers mods = EquipmentRules.Collect(_equipmentOwner, _save.Equipment.Snapshot(), CraftCatalog, grips,
                _combatBalance.Grips);
            GUILayout.BeginVertical(GUI.skin.box);
            if (!mods.HandsValid)
            {
                ui.Label(_copy.Ui("ui.craft.hands_invalid"));
            }

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

        private IReadOnlyCollection<GripMode> WearerGrips(int owner) =>
            owner == 0 ? EquipmentRules.PlayerGrips : CraftCatalog.GripsOf(_save.Get(owner).SpeciesId);

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

        private static readonly string[] PaperdollSlots =
        {
            EquipmentSlots.MainHand, EquipmentSlots.OffHand, EquipmentSlots.Upper, EquipmentSlots.Lower, EquipmentSlots.Accessory,
        };

        private readonly StringBuilder _gearKeyBuilder = new StringBuilder();
        private string _previewGearKey;
        private List<PaperdollGear> _previewGear = new List<PaperdollGear>();

        // REQ: gear-looks.md#外觀與數值 — each worn copy is drawn from its own look tag. Sampling runs only when the
        // worn set changes, not on every OnGUI pass.
        private IReadOnlyList<PaperdollGear> EquippedGear(int owner)
        {
            _gearKeyBuilder.Clear().Append(owner);
            foreach (string slot in PaperdollSlots)
            {
                if (!_save.Equipment.TryGetEntry(owner, slot, out EquippedItem entry)) continue;
                _gearKeyBuilder.Append('|').Append(slot).Append(':').Append(entry.Item).Append(':').Append(entry.Look.Origin.PlanetTypeId)
                    .Append(':').Append(entry.Look.Origin.PlanetSeed).Append(':').Append(entry.Look.Seed);
            }
            string key = _gearKeyBuilder.ToString();
            if (key == _previewGearKey) return _previewGear;

            _previewGearKey = key;
            _previewGear = new List<PaperdollGear>();
            foreach (string slot in PaperdollSlots)
            {
                if (!_save.Equipment.TryGetEntry(owner, slot, out EquippedItem entry)) continue;
                // An item dropped from the catalog has no kind to draw; the slot UI still lists it by id.
                if (!CraftCatalog.TryGetItem(entry.Item, out CraftItem item) || item.Equipment == null) continue;
                GearLookSpec spec = GearLooks.ForCopy(GearLooks.KindFor(item.Equipment), entry.Look);
                _previewGear.Add(new PaperdollGear(slot, slot + ":" + entry.Item + ":" + entry.Look.Origin.PlanetTypeId + ":" +
                    entry.Look.Origin.PlanetSeed + ":" + entry.Look.Seed, spec));
            }
            return _previewGear;
        }

        private void UpdatePaperdollPreview(IReadOnlyList<PaperdollGear> gear)
        {
            _paperdollPreview ??= new PaperdollPreview(_toonShader, _sceneryShader);
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
                    gear);
            }
            else
            {
                OwnedCreature owned = _save.Get(_equipmentOwner);
                _database.TryGet(owned.SpeciesId, out CreatureDef def);
                _paperdollPreview.SetCreatureTarget(def, _equipmentOwner, gear);
            }

            _paperdollPreview.SyncPose();
        }

        private void DrawLobbyCrafting(AdventurePanel ui, HashSet<GameData.FieldUtility> utilities)
        {
            if (ui.Button(_copy.Ui(_showLockedCrafts ? "ui.skills.hide_locked" : "ui.skills.show_locked")))
            { _showLockedCrafts = !_showLockedCrafts; GUIUtility.ExitGUI(); }
            CraftRecipe recipe = CraftJournal.DrawRecipes(ui, _copy, CraftCatalog, _save.CraftStock, utilities, _showLockedCrafts);
            if (recipe == null) return;
            var candidate = _save.Clone();
            // REQ: gear-looks.md#外觀與數值 — lobby crafting takes its style from the most recently departed planet.
            CraftResult result = CraftRules.TryCraft(recipe, utilities, candidate.CraftStock, CraftCatalog, _save.LastPlanet);
            ui.Message = result == CraftResult.Success
                ? (CommitLobby(candidate) ? _copy.Ui("craft.result.Success") : _partyMsg) : _copy.Ui("craft.result." + result);
            GUIUtility.ExitGUI();
        }
    }
}
