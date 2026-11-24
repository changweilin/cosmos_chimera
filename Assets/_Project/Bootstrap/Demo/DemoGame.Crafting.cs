using System;
using System.Collections.Generic;
using System.Text;
using Cosmimera.Codex;
using Cosmimera.Crafting;
using Cosmimera.Creatures;
using Cosmimera.GameData;
using Cosmimera.Lobby;
using Cosmimera.Save;
using Cosmimera.WorldGen;
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

        private Action _onEquipmentChanged;

        internal void DrawEquipmentInMenu(AdventurePanel ui, Action onEquipmentChanged)
        {
            _onEquipmentChanged = onEquipmentChanged;
            try
            {
                DrawEquipment(ui);
            }
            finally
            {
                _onEquipmentChanged = null;
            }
        }

        private void DrawEquipment(AdventurePanel ui)
        {
            if (_equipmentOwner != 0 && !ContainsParty(_equipmentOwner) && !IsSub(_equipmentOwner))
            {
                _equipmentOwner = 0;
            }

            ui.Heading(_copy.Ui("ui.paperdoll.title"));
            ui.Label(_copy.Ui("ui.craft.gear_preview"));

            DrawWearerSelector(ui);
            DrawPresetBar(ui);

            IReadOnlyCollection<GripMode> grips = WearerGrips(_equipmentOwner);
            if (_equipmentOwner != 0)
            {
                var names = new List<string>();
                foreach (GripMode mode in grips) names.Add(_copy.Ui("ui.craft.grip." + mode));
                ui.Label(_copy.Ui("ui.craft.grips").Replace("{grips}", string.Join(" / ", names)));
            }

            UpdatePaperdollPreview(EquippedGear(_equipmentOwner));

            ui.Heading(_copy.Ui("ui.paperdoll.preview"));
            DrawPaperdollViewport(ui);
            DrawPaperdollSlot(ui, EquipmentSlots.MainHand);
            DrawPaperdollSlot(ui, EquipmentSlots.OffHand);
            DrawPaperdollSlot(ui, EquipmentSlots.Accessory);
            DrawPaperdollSlot(ui, EquipmentSlots.Upper);
            DrawPaperdollSlot(ui, EquipmentSlots.Lower);
            DrawWearerBonusSummary(ui, grips);

            ui.Heading(_copy.Ui("ui.paperdoll.available_gear"));
            bool anyAvailable = false;
            foreach (CraftItem item in CraftCatalog.Items)
            {
                if (item.Equipment == null || !item.Equipment.Allows(_equipmentOwner) || _save.CraftStock.Count(item.Id) == 0)
                {
                    continue;
                }

                anyAvailable = true;
                ui.Label("[" + _copy.Ui("ui.craft.kind." + item.Equipment.Kind) + "] " + _copy.Ui(item.Id)
                    + " ×" + _save.CraftStock.Count(item.Id));
                CraftJournal.DrawGearStats(ui, _copy, item);
                int copy = DrawCopyPicker(ui, item.Id);
                ui.BeginRow();
                foreach (string slot in EquipmentSlots.All)
                {
                    if (!item.Equipment.Fits(slot) || !ui.Button(_copy.Ui("ui.craft.equip_to").Replace("{slot}", _copy.Ui("ui.craft.slot." + slot))))
                    {
                        continue;
                    }

                    var candidate = _save.Clone();
                    bool equipped = candidate.Equipment.TryEquip(_equipmentOwner, item.Id, slot, grips, CraftCatalog, candidate.CraftStock, copy);
                    if (equipped && CommitLobby(candidate))
                    {
                        ui.Message = string.Empty;
                        _onEquipmentChanged?.Invoke();
                    }
                    else
                    {
                        ui.Message = equipped ? _partyMsg : _copy.Ui("ui.craft.equip_failed");
                    }
                    ui.EndRow();
                    GUIUtility.ExitGUI();
                }
                ui.EndRow();
            }

            if (!anyAvailable)
            {
                ui.Label(_copy.Ui("ui.paperdoll.none_available"));
            }

            if (_equipmentOwner == 0)
            {
                DrawNgPlusCosmeticsSection(ui);
            }
        }

        private void DrawNgPlusCosmeticsSection(AdventurePanel ui)
        {
            if (!_save.NgPlusUnlocked)
            {
                return;
            }

            ui.Heading("二周目純外觀更換與我的最愛");
            string activeChar = string.IsNullOrEmpty(_save.CosmeticCharacterId) ? _save.CharacterId : _save.CosmeticCharacterId;
            string activeOutfit = string.IsNullOrEmpty(_save.CosmeticOutfitCharacterId) ? activeChar : _save.CosmeticOutfitCharacterId;
            string activeAcc = string.IsNullOrEmpty(_save.CosmeticAccessoryCharacterId) ? activeChar : _save.CosmeticAccessoryCharacterId;

            ui.Label($"目前角色外觀：{BeginnerQuestRules.CharacterDisplayName(activeChar)} · 服裝：{BeginnerQuestRules.CharacterDisplayName(activeOutfit)} · 飾品：{BeginnerQuestRules.CharacterDisplayName(activeAcc)}");

            ui.BeginRow();
            foreach (string cid in BeginnerQuestRules.CharacterRing)
            {
                bool obtained = string.Equals(cid, _save.CharacterId, StringComparison.Ordinal) ||
                    _save.Codex.Get(CodexKind.Character, cid) != CodexLevel.Unknown;
                if (!obtained) continue;

                string label = (string.Equals(cid, activeChar, StringComparison.Ordinal) ? "● " : "") +
                    BeginnerQuestRules.CharacterDisplayName(cid);
                if (ui.Button("角色：" + label))
                {
                    LobbySave candidate = _save.Clone();
                    candidate.SetCosmeticAppearance(cid, activeOutfit, activeAcc);
                    if (CommitLobby(candidate)) SyncSelectedCharacterModel(cid);
                    ui.EndRow();
                    GUIUtility.ExitGUI();
                }
                if (ui.Button("服裝：" + BeginnerQuestRules.CharacterDisplayName(cid)))
                {
                    LobbySave candidate = _save.Clone();
                    candidate.SetCosmeticAppearance(activeChar, cid, activeAcc);
                    CommitLobby(candidate);
                    ui.EndRow();
                    GUIUtility.ExitGUI();
                }
                if (ui.Button("飾品：" + BeginnerQuestRules.CharacterDisplayName(cid)))
                {
                    LobbySave candidate = _save.Clone();
                    candidate.SetCosmeticAppearance(activeChar, activeOutfit, cid);
                    CommitLobby(candidate);
                    ui.EndRow();
                    GUIUtility.ExitGUI();
                }
            }
            ui.EndRow();

            ui.BeginRow();
            if (ui.Button("儲存至純外觀我的最愛", _save.CosmeticFavorites.Count < LobbySave.MaxCosmeticFavorites))
            {
                LobbySave candidate = _save.Clone();
                string presetName = $"最愛 {_save.CosmeticFavorites.Count + 1}";
                candidate.SaveCosmeticFavorite(new LobbySave.CosmeticFavoritePreset(
                    presetName, activeChar, activeOutfit, activeAcc));
                CommitLobby(candidate);
                ui.EndRow();
                GUIUtility.ExitGUI();
            }

            for (int i = 0; i < _save.CosmeticFavorites.Count; i++)
            {
                LobbySave.CosmeticFavoritePreset fav = _save.CosmeticFavorites[i];
                if (ui.Button($"套用：{fav.Name}"))
                {
                    LobbySave candidate = _save.Clone();
                    candidate.SetCosmeticAppearance(fav.CharacterId, fav.OutfitCharacterId, fav.AccessoryCharacterId);
                    if (CommitLobby(candidate) && !string.IsNullOrEmpty(fav.CharacterId))
                    {
                        SyncSelectedCharacterModel(fav.CharacterId);
                    }
                    ui.EndRow();
                    GUIUtility.ExitGUI();
                }
                if (ui.Button($"×{i + 1}"))
                {
                    LobbySave candidate = _save.Clone();
                    candidate.RemoveCosmeticFavorite(i);
                    CommitLobby(candidate);
                    ui.EndRow();
                    GUIUtility.ExitGUI();
                }
            }
            ui.EndRow();
        }

        // Shop & material selling live on Main World Merchant NPCs (RunSession.MainWorldNpcs.cs).

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
                ui.BeginRow();
                if (ui.Button("◀")) pick = (pick + total - 1) % total;
                ui.Label(label);
                if (ui.Button("▶")) pick = (pick + 1) % total;
                ui.EndRow();
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
                ui.BeginRow();
                if (_presetEditSlot == slot)
                {
                    _presetEditText = ui.TextField("preset_rename_" + slot, _presetEditText);
                    if (_presetEditText.Length > LoadoutPreset.MaxNameLength)
                    {
                        _presetEditText = _presetEditText.Substring(0, LoadoutPreset.MaxNameLength);
                    }
                    bool valid = !string.IsNullOrWhiteSpace(_presetEditText);
                    if (ui.Button(_copy.Ui("ui.preset.rename_ok"), valid))
                    {
                        var candidate = _save.Clone();
                        candidate.RenamePreset(slot, _presetEditText.Trim());
                        ui.Message = CommitLobby(candidate) ? string.Empty : _partyMsg;
                        _presetEditSlot = -1;
                        ui.EndRow();
                        GUIUtility.ExitGUI();
                    }

                    if (ui.Button(_copy.Ui("ui.preset.rename_cancel")))
                    {
                        _presetEditSlot = -1;
                        ui.EndRow();
                        GUIUtility.ExitGUI();
                    }

                    ui.EndRow();
                    continue;
                }

                ui.Label((slot + 1) + ". " + (preset != null ? preset.Name : _copy.Ui("ui.preset.empty")));
                if (preset != null && ui.Button(_copy.Ui("ui.preset.rename")))
                {
                    _presetEditSlot = slot;
                    _presetEditText = preset.Name;
                    ui.EndRow();
                    GUIUtility.ExitGUI();
                }

                if (ui.Button(_copy.Ui("ui.preset.save")))
                {
                    var candidate = _save.Clone();
                    candidate.SavePreset(slot, preset != null ? preset.Name
                        : _copy.Ui("ui.preset.default_name").Replace("{n}", (slot + 1).ToString()));
                    if (CommitLobby(candidate)) ui.Message = string.Empty; else ui.Message = _partyMsg;
                    ui.EndRow();
                    GUIUtility.ExitGUI();
                }

                if (preset != null && ui.Button(_copy.Ui("ui.preset.apply")))
                {
                    var candidate = _save.Clone();
                    PresetApplyResult result = candidate.TryApplyPreset(slot, CraftCatalog);
                    if (!result.Applied) ui.Message = _copy.Ui("ui.preset.failed");
                    else if (!CommitLobby(candidate)) ui.Message = _partyMsg;
                    else
                    {
                        ui.Message = (result.MissingCreatures + result.MissingItems == 0) ? string.Empty
                            : _copy.Ui("ui.preset.applied").Replace("{creatures}", result.MissingCreatures.ToString())
                                .Replace("{items}", result.MissingItems.ToString());
                        _onEquipmentChanged?.Invoke();
                    }
                    ui.EndRow();
                    GUIUtility.ExitGUI();
                }

                if (preset != null && ui.Button(_copy.Ui("ui.preset.clear")))
                {
                    var candidate = _save.Clone();
                    candidate.ClearPreset(slot);
                    if (CommitLobby(candidate)) ui.Message = string.Empty; else ui.Message = _partyMsg;
                    ui.EndRow();
                    GUIUtility.ExitGUI();
                }

                ui.EndRow();
            }
        }

        private void DrawWearerSelector(AdventurePanel ui)
        {
            string playerName = _characterModels != null && _selectedCharacterIndex < _characterModels.Entries.Count
                ? _characterModels.Entries[_selectedCharacterIndex].displayName
                : _copy.Ui("ui.craft.player");

            ui.BeginRow();
            if (ui.Button((_equipmentOwner == 0 ? "● " : "") + _copy.Ui("ui.craft.player") + " (" + playerName + ")"))
            {
                _equipmentOwner = 0;
                ui.EndRow();
                GUIUtility.ExitGUI();
            }
            if (ui.Button(_copy.Ui("ui.paperdoll.manage_partners")))
            {
                ui.SetPage(AdventurePanel.Page.Partners);
                ui.EndRow();
                GUIUtility.ExitGUI();
            }
            ui.EndRow();

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
                ui.Image(_paperdollPreview.Texture, 192f, 264f);
            }

            ui.BeginRow();
            if (ui.Button(_copy.Ui("ui.paperdoll.rotate_left")))
            {
                _paperdollPreview?.Rotate(-30f);
                ui.EndRow();
                GUIUtility.ExitGUI();
            }
            if (ui.Button(_copy.Ui("ui.paperdoll.rotate_right")))
            {
                _paperdollPreview?.Rotate(30f);
                ui.EndRow();
                GUIUtility.ExitGUI();
            }
            ui.EndRow();
        }

        private void DrawPaperdollSlot(AdventurePanel ui, string slot)
        {
            string equippedId = FindEquippedItem(_equipmentOwner, slot);
            ui.Label(_copy.Ui("ui.craft.slot." + slot) + "：" + (string.IsNullOrEmpty(equippedId) ? _copy.Ui("ui.paperdoll.empty_slot") : _copy.Ui(equippedId)));
            if (!string.IsNullOrEmpty(equippedId))
            {
                if (CraftCatalog.TryGetItem(equippedId, out CraftItem item))
                {
                    CraftJournal.DrawGearStats(ui, _copy, item);
                }
                if (ui.Button(_copy.Ui("ui.craft.unequip") + " (" + _copy.Ui("ui.craft.slot." + slot) + ")"))
                {
                    var candidate = _save.Clone();
                    bool unequipped = candidate.Equipment.TryUnequip(_equipmentOwner, slot, candidate.CraftStock);
                    if (unequipped && CommitLobby(candidate))
                    {
                        ui.Message = string.Empty;
                        _onEquipmentChanged?.Invoke();
                    }
                    else
                    {
                        ui.Message = unequipped ? _partyMsg : _copy.Ui("ui.craft.equip_failed");
                    }
                    GUIUtility.ExitGUI();
                }
            }
        }

        private void DrawWearerBonusSummary(AdventurePanel ui, IReadOnlyCollection<GripMode> grips)
        {
            EquipmentModifiers mods = EquipmentRules.Collect(_equipmentOwner, _save.Equipment.Snapshot(), CraftCatalog, grips,
                _combatBalance.Grips);
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
