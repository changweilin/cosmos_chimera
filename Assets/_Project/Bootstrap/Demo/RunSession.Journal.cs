using System;
using System.Collections.Generic;
using Cosmimera.Combat;
using Cosmimera.Codex;
using Cosmimera.Core.Random;
using Cosmimera.Creatures;
using Cosmimera.Crafting;
using Cosmimera.Food;
using Cosmimera.GameData;
using Cosmimera.WorldGen;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Cosmimera.Bootstrap.Demo
{
    public sealed partial class RunSession
    {
        private AdventurePanel _journal;
        private CodexPanel _codex;
        private readonly HashSet<int> _codexEncountered = new HashSet<int>();
        private InputAction _journalAction;
        private InputAction _codexAction;
        private InputActionMap _globalInput;
        private InputAction _commandModifierAction;
        private InputHints _inputHints;
        private float _previousTimeScale;
        private bool _previousPlayerInput;
        private CursorLockMode _previousCursorLock;
        private bool _previousCursorVisible;
        private FoodCatalog _foodCatalog;
        private readonly FoodInventory _foodInventory = new FoodInventory();
        private readonly ActiveFoodBuffs _foodBuffs = new ActiveFoodBuffs();
        private readonly TrapManager _traps = new TrapManager();
        private readonly List<GatheredStack> _gathered = new List<GatheredStack>();
        private readonly List<Combatant> _foodTargets = new List<Combatant>(6);
        private readonly Dictionary<string, Queue<CookingQuality>> _foodQualities = new Dictionary<string, Queue<CookingQuality>>();
        private readonly HashSet<FieldUtility> _utilities = new HashSet<FieldUtility>();
        private readonly HashSet<FieldUtility> _enabledUtilities = new HashSet<FieldUtility>();
        private readonly HashSet<Element> _partyElements = new HashSet<Element>();
        private readonly HashSet<int> _overloadUnlocked = new HashSet<int>();
        private TeamUtilityModifiers _utilityModifiers;
        private Pcg32 _foodRng;
        private bool _showLockedGathering;
        private readonly List<string> _pot = new List<string>(PotCookingRules.MaxPotSize);
        private CookingMethod _potMethod = CookingMethod.Grill;

        private EncounterUnit AddEquippedPartyCreature(int slot)
        {
            var moves = new List<MoveDef>();
            var traits = new List<TraitDef>();
            var partnerFieldMoves = new List<MoveDef>();
            var partnerFieldTraits = new List<TraitDef>();
            MoveDef mainFieldMove = null;
            TraitDef mainFieldTrait = null;
            CreatureDef mainDef = _setup.Party[slot];
            if (_setup.Database != null && mainDef.FieldUtility != FieldUtility.None)
            {
                if (FieldUtilityRules.GetKind(mainDef.FieldUtility) == FieldUtilityKind.Move
                    && _setup.Database.TryGetFieldUtilityMove(mainDef.FieldUtility, out MoveDef fuMove))
                {
                    mainFieldMove = fuMove;
                }
                else if (FieldUtilityRules.GetKind(mainDef.FieldUtility) == FieldUtilityKind.Trait
                    && _setup.Database.TryGetFieldUtilityTrait(mainDef.FieldUtility, out TraitDef fuTrait))
                {
                    mainFieldTrait = fuTrait;
                }
            }

            bool overload = false;
            if (_setup.Partners != null)
            {
                Core.Contracts.Contract.Requires(_setup.Database != null && _setup.Owned != null,
                    "equipped runs need database and owned individuals");
                foreach (int subId in _setup.Partners.SubsOf(_setup.PartyIds[slot]))
                {
                    OwnedCreature sub = OwnedAtDeparture(subId);
                    CreatureDef def = RequireCreature(sub.SpeciesId);
                    if (sub.Vessel == VesselKind.TalismanCard)
                    {
                        moves.Add(def.SignatureMove);
                        if (def.FieldUtility != FieldUtility.None
                            && FieldUtilityRules.GetKind(def.FieldUtility) == FieldUtilityKind.Move
                            && _setup.Database.TryGetFieldUtilityMove(def.FieldUtility, out MoveDef subFuMove))
                        {
                            partnerFieldMoves.Add(subFuMove);
                        }
                    }
                    else if (sub.Vessel == VesselKind.GlassBead)
                    {
                        traits.Add(def.Trait);
                        if (def.FieldUtility != FieldUtility.None
                            && FieldUtilityRules.GetKind(def.FieldUtility) == FieldUtilityKind.Trait
                            && _setup.Database.TryGetFieldUtilityTrait(def.FieldUtility, out TraitDef subFuTrait))
                        {
                            partnerFieldTraits.Add(subFuTrait);
                        }
                    }
                    else overload = true;
                }
            }
            EncounterUnit unit = _encounter.AddEquippedCreature(mainDef, moves, traits, _plan.LandingDirection,
                _progression.LevelOf(_partyExp[slot]), mainFieldMove, partnerFieldMoves, mainFieldTrait, partnerFieldTraits);
            _encounter.SetHarvestUtilities(unit.Id, HarvestUtilitiesOf(slot));
            EquipmentModifiers equipment = EquipmentRules.Collect(_setup.PartyIds[slot],
                _setup.Equipment ?? Array.Empty<EquippedItem>(), _craftCatalog, GripsAtDeparture(_setup.PartyIds[slot]), _balance.Grips);
            var combinedEffects = new List<EffectSpec>(equipment.CreatureEffects);
            var cubeFloors = new List<Element>();
            if (_setup.Partners != null)
            {
                foreach (int subId in _setup.Partners.SubsOf(_setup.PartyIds[slot]))
                {
                    OwnedCreature cube = OwnedAtDeparture(subId);
                    if (cube.Vessel != VesselKind.MagicCube) continue;
                    // REQ: crafting.md#魔術方塊副手 — full-strength accessory-style grant from the sub's own specialties.
                    CreatureDef cubeDef = RequireCreature(cube.SpeciesId);
                    Core.Contracts.Contract.Requires(cubeDef.Mastery.Count > 0, "cube sub species has no mastery element");
                    ElementValue strongest = cubeDef.Mastery[0];
                    foreach (ElementValue m in cubeDef.Mastery) if (m.Value > strongest.Value) strongest = m;
                    var resists = new int[10];
                    for (int e = 0; e < 10; e++) resists[e] = cubeDef.GetResist((Element)e);
                    IReadOnlyList<EffectSpec> grant = CraftingExpansionRules.CubePartnerEffects(strongest.Element, resists);
                    if (combinedEffects.Count + grant.Count > 32) continue;
                    combinedEffects.AddRange(grant);
                    cubeFloors.Add(strongest.Element);
                }
                foreach (int subId in _setup.Partners.SubsOf(_setup.PartyIds[slot]))
                {
                    // REQ: crafting.md#穿戴與數值 — only the sub's effect rows reach the main; its weapons stay with it.
                    EquipmentModifiers subEquipment = EquipmentRules.Collect(subId,
                        _setup.Equipment ?? Array.Empty<EquippedItem>(), _craftCatalog, GripsAtDeparture(subId), _balance.Grips);
                    foreach (EffectSpec effect in subEquipment.CreatureEffects)
                    {
                        if (combinedEffects.Count < 32)
                        {
                            combinedEffects.Add(EquipmentRules.ScalePartner(effect));
                        }
                    }
                }
            }
            unit.Combatant.Book.SetEquipmentEffects(combinedEffects);
            foreach (Element floorElement in cubeFloors) unit.Combatant.SetElementMasteryFloor(floorElement, true);
            unit.Combatant.SetWeapons(equipment.Weapons);
            if (_setup.PartyIds != null && slot < _setup.PartyIds.Count)
            {
                _unitSaveIds[unit.Id] = _setup.PartyIds[slot];
            }
            if (overload) _overloadUnlocked.Add(unit.Id);
            return unit;
        }

        // Runs without owned individuals (smoke tests) have no species to look up; they hold no equipment either.
        private IReadOnlyCollection<GripMode> GripsAtDeparture(int ownedId) =>
            _setup.Owned == null ? (IReadOnlyCollection<GripMode>)Array.Empty<GripMode>() : _craftCatalog.GripsOf(OwnedAtDeparture(ownedId).SpeciesId);

        private OwnedCreature OwnedAtDeparture(int id)
        {
            foreach (OwnedCreature creature in _setup.Owned) if (creature.InstanceId == id) return creature;
            throw new InvalidOperationException("Departure loadout refers to an unowned creature: " + id);
        }

        private CreatureDef RequireCreature(string id)
        {
            if (_setup.Database.TryGet(id, out CreatureDef def)) return def;
            throw new InvalidOperationException("Departure species is missing: " + id);
        }

        private void ConfigureJournal()
        {
            if (_setup.Codex != null) _outcome.Codex.Merge(_setup.Codex);
            if (_setup.DiscoveredRecipes != null) _outcome.DiscoveredRecipes.UnionWith(_setup.DiscoveredRecipes);
            foreach (CreatureDef creature in _setup.Party) EncyclopediaCatalog.RecordOwned(_outcome.Codex, creature);
            if (_setup.Database != null && _setup.Owned != null)
                foreach (OwnedCreature creature in _setup.Owned)
                    if (_setup.Database.TryGet(creature.SpeciesId, out CreatureDef definition))
                        EncyclopediaCatalog.RecordOwned(_outcome.Codex, definition);
            _globalInput = _actions.FindActionMap("DemoGlobal", true).Clone();
            _journalAction = _globalInput.FindAction("Journal", true);
            _codexAction = _globalInput.FindAction("Codex", true);
            _commandModifierAction = _actions.FindAction("Player/CommandModifier", true);
            _inputHints = new InputHints(_actions.FindActionMap("Player", true), _runInput, _menuInput, _globalInput);
            if (isActiveAndEnabled) _globalInput.Enable();
            _foodCatalog = FoodCatalog.CreateDefault();
            _foodRng = new Pcg32(_setup.Seed, 'F');
            foreach (CreatureDef creature in _setup.Party)
            {
                if (creature.FieldUtility != FieldUtility.None) _utilities.Add(creature.FieldUtility);
                foreach (ElementValue mastery in creature.Mastery) _partyElements.Add(mastery.Element);
            }
            if (_setup.Partners != null)
            {
                var mains = new List<OwnedCreature>();
                var subs = new List<OwnedCreature>();
                foreach (int main in _setup.Partners.Mains)
                {
                    mains.Add(OwnedAtDeparture(main));
                    foreach (int sub in _setup.Partners.SubsOf(main))
                    {
                        OwnedCreature creature = OwnedAtDeparture(sub);
                        subs.Add(creature);
                        _outcome.PartnerExp.Add(sub, creature.Exp);
                    }
                }
                _utilities.UnionWith(FieldUtilityRules.CollectTeamUtilities(mains, subs,
                    new GameDatabaseSpeciesLookup(_setup.Database)));
            }
            foreach (FieldUtility utility in _utilities)
                if (utility != FieldUtility.Mount && utility != FieldUtility.Fly && utility != FieldUtility.Levitate &&
                    utility != FieldUtility.PartyStealth) _enabledUtilities.Add(utility);
            ApplyUtilities();
            if (_setup.Database != null) _codex = new CodexPanel(_setup.Database, _copy, OwnsSpecies, _setup.Portraits, _outcome.Codex,
                _foodCatalog, _outcome.DiscoveredRecipes.Contains);
        }

        private bool OwnsSpecies(string species)
        {
            if (_setup.Owned != null)
                foreach (OwnedCreature creature in _setup.Owned) if (creature.SpeciesId == species) return true;
            foreach (var capture in _outcome.Captured) if (capture.speciesId == species) return true;
            return false;
        }

        private bool ReadJournalInput()
        {
            if (_journal != null)
            {
                // The same D-pad now navigates the open journal; only keyboard shortcuts toggle it closed.
                if (_menuCancel.WasPressedThisFrame() ||
                    (!_journal.EditingText &&
                        ((_journalAction.WasPressedThisFrame() && _journalAction.activeControl?.device is Keyboard) ||
                         (_codexAction.WasPressedThisFrame() && _codexAction.activeControl?.device is Keyboard)))) CloseJournal();
                else _journal.ReadInput(_menuInput);
                return true;
            }
            if (_interactTarget < 0 && _journalAction != null)
            {
                if (PressedWithoutCommand(_journalAction)) { OpenJournal(); return true; }
                if (PressedWithoutCommand(_codexAction)) { OpenJournal(AdventurePanel.Page.Codex); return true; }
            }
            return false;
        }

        private void OpenJournal(AdventurePanel.Page page = AdventurePanel.Page.Partners)
        {
            if (_journal != null || !_started || _ended || _interactTarget >= 0) return;
            _journal = new AdventurePanel(_copy, page);
            _previousTimeScale = Time.timeScale;
            _previousPlayerInput = _rig.Input.enabled;
            _previousCursorLock = Cursor.lockState;
            _previousCursorVisible = Cursor.visible;
            _rig.Input.enabled = false;
            _rig.Character.SetInput(Vector2.zero, _rig.Character.Facing, false, false);
            _rollRequested = false;
            _overloadRequested = false;
            _abandonArmedUntilMs = -1;
            _runInput.Disable();
            _menuInput.Enable();
            Time.timeScale = 0;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void CloseJournal()
        {
            if (_journal == null) return;
            _journal = null;
            Time.timeScale = _previousTimeScale;
            Cursor.lockState = _previousCursorLock;
            Cursor.visible = _previousCursorVisible;
            _menuInput?.Disable();
            if (_rig?.Input != null) _rig.Input.enabled = _previousPlayerInput;
            if (isActiveAndEnabled && !_ended) _runInput?.Enable();
        }

        private void DrawJournal(AdventurePanel ui)
        {
            switch (ui.Current)
            {
                case AdventurePanel.Page.Partners:
                    ui.Heading(_copy.Ui("ui.journal.tab.Partners"));
                    ui.Label(_copy.Ui("ui.partner.lobby_only"));
                    for (int i = 0; i < _partyUnits.Count; i++)
                    {
                        EncounterUnit unit = _encounter.Units[_partyUnits[i]];
                        ui.Heading(_copy.Name(unit.Def.Id));
                        if (_setup.Partners == null) continue;
                        foreach (int subId in _setup.Partners.SubsOf(_setup.PartyIds[i]))
                        {
                            OwnedCreature sub = OwnedAtDeparture(subId);
                            CreatureDef def = RequireCreature(sub.SpeciesId);
                            ui.Label(_copy.Name(sub.SpeciesId) + " · " + _copy.Ui("ui.vessel." + VesselKey(sub.Vessel)) + " → " +
                                (sub.Vessel == VesselKind.TalismanCard ? _copy.Name(def.SignatureMove.Id)
                                : sub.Vessel == VesselKind.GlassBead ? _copy.Name(def.Trait.Id) : _copy.Ui("ui.partner.overload")));
                        }
                    }
                    break;
                case AdventurePanel.Page.Items: DrawItems(ui); break;
                case AdventurePanel.Page.Skills: DrawSkills(ui); break;
                case AdventurePanel.Page.Codex:
                    if (_codex != null) _codex.Draw(ui);
                    else ui.Label(_copy.Ui("ui.journal.empty"));
                    break;
            }
        }

        private void DrawItems(AdventurePanel ui)
        {
            ui.Heading(_copy.Ui("ui.journal.tab.Items"));
            DrawCraftedItems(ui);
            ui.Label(_copy.Ui("ui.items.run_stock"));
            foreach (VesselKind vessel in Enum.GetValues(typeof(VesselKind)))
            {
                if (!ui.Button((_vessel == vessel ? "● " : "") + _copy.Ui("ui.vessel." + VesselKey(vessel)))) continue;
                _vessel = vessel;
                _craftedVessel = null;
                GUIUtility.ExitGUI();
            }
            bool canCapture = FindCaptureTarget(CaptureReachM) >= 0;
            if (!canCapture) ui.Label(_copy.Ui("ui.items.no_capture_target"));
            if (ui.Button(_copy.Ui("ui.items.capture"), canCapture))
            { TryCapture(); ui.Message = _toast; GUIUtility.ExitGUI(); }
            foreach (MaterialStack stack in _outcome.Materials) ui.Label(_copy.Material(stack.Key) + " ×" + stack.Qty);
            bool anyFood = false;
            foreach (IngredientSpec ingredient in _foodCatalog.Ingredients)
            {
                int count = _foodInventory.CountIngredient(ingredient.Id);
                if (count == 0) continue;
                anyFood = true;
                ui.Label(_copy.Ui(ingredient.Id) + $" ×{count} · HP +{ingredient.RawHealPercent}%");
                if (ui.Button(_copy.Ui("ui.items.eat"), ingredient.IsEdibleRaw))
                {
                    EatIngredient(ingredient);
                    GUIUtility.ExitGUI();
                }
            }
            foreach (FoodItemSpec food in _foodCatalog.Foods)
            {
                int count = _foodInventory.CountFood(food.Id);
                if (count == 0) continue;
                anyFood = true;
                ui.Label(FoodName(food) + $" ×{count} · " + FoodDescription(food));
                if (ui.Button(_copy.Ui("ui.items.eat")))
                { EatFood(food); GUIUtility.ExitGUI(); }
            }
            if (!anyFood) ui.Label(_copy.Ui("ui.items.empty_food"));
        }

        private string FoodDescription(FoodItemSpec food) => _copy.Ui("food.target." + food.Target) + " · " +
            _copy.Ui("ui.items.heal") + $" {food.Effect.CreatureHealPercent}% / {food.Effect.PlayerHealPercent}% · " +
            _copy.Ui("ui.items.revive") + $" {food.Effect.RevivePercent}% · {food.Effect.DurationMs / 1000}s";

        private void EatIngredient(IngredientSpec ingredient)
        {
            Combatant main = _mainId >= 0 ? _encounter.Units[_mainId].Combatant : null;
            ConsumeRefusalReason reason = FoodConsumptionRules.TryEatRawIngredient(ingredient.Id, _foodCatalog,
                _foodInventory, _vitals, main, _nowMs, out ConsumeOutcome outcome);
            ConsumptionMessage(reason, outcome);
        }

        private void EatFood(FoodItemSpec food)
        {
            _foodTargets.Clear();
            if (food.Target == FoodTarget.ActiveCreatures)
                foreach (int id in _team.Active) _foodTargets.Add(_encounter.Units[id].Combatant);
            else if (food.Target != FoodTarget.Player)
                foreach (int id in _partyUnits) _foodTargets.Add(_encounter.Units[id].Combatant);
            PlayerVitals player = food.Target == FoodTarget.Player || food.Target == FoodTarget.PlayerAndParty ? _vitals : null;
            if (player == null && _foodTargets.Count == 0)
            { _journal.Message = _copy.Ui("consume.NoEligibleTarget"); return; }
            _foodQualities.TryGetValue(food.Id, out Queue<CookingQuality> qualities);
            CookingQuality quality = qualities != null && qualities.Count > 0 ? qualities.Peek() : CookingQuality.Normal;
            var before = new HashSet<int>();
            foreach (int id in _partyUnits) if (_encounter.Units[id].Combatant.IsFainted) before.Add(id);
            ConsumeRefusalReason reason = FoodConsumptionRules.TryEatFood(food.Id, _foodCatalog, _foodInventory,
                player, _foodTargets, _foodBuffs, _nowMs, out ConsumeOutcome outcome, quality);
            if (reason == ConsumeRefusalReason.None)
            {
                if (qualities != null && qualities.Count > 0) qualities.Dequeue();
                foreach (int id in before)
                    if (!_encounter.Units[id].Combatant.IsFainted) _team.TryClearRetired(id);
            }
            ConsumptionMessage(reason, outcome);
        }

        private void ConsumptionMessage(ConsumeRefusalReason reason, ConsumeOutcome outcome)
        {
            _journal.Message = reason == ConsumeRefusalReason.None
                ? _copy.Ui("ui.items.consumed").Replace("{player}", outcome.PlayerHpRestored.ToString())
                    .Replace("{creature}", outcome.CreatureHpRestored.ToString()).Replace("{revived}", outcome.CreaturesRevived.ToString())
                : _copy.Ui("consume." + reason);
        }

        private GatheringContext GatheringEnvironment()
        {
            float3 direction = _rig.Config.ToDirection(PlayerPosition());
            bool sea = _rig.IsSea(direction);
            bool nearWater = sea;
            var frame = Planet.PlanetFrame.FromUp(direction);
            // Use rendered sea samples at interaction reach, rather than granting fishing anywhere on a wet planet.
            foreach (float3 tangent in new[] { frame.North, -frame.North, frame.East, -frame.East })
                nearWater |= _rig.IsSea(math.normalize(direction + tangent * Encounter.InteractRadiusM / _rig.Config.Radius));
            bool foliage = _worldObjects != null && _worldObjects.HasFoliage(direction, Encounter.InteractRadiusM);
            // Crop fields are not generated yet; settlement proximity alone must not fabricate a crop source.
            return new GatheringContext(_utilityModifiers, !sea, true, nearWater, foliage, false,
                _foodBuffs.GetDropRateBonusPercent(_nowMs));
        }

        private string WeatherName(WeatherKind kind) => _copy.Ui("ui.weather." + kind.ToString().ToLowerInvariant());

        private string WeatherStatusText()
        {
            float3 direction = _rig.Config.ToDirection(PlayerPosition());
            int pinned = _weatherControl.PinnedRemainingMs(_weather.RegionAt(direction), _nowMs);
            string weather = _playerWeather.Kind == WeatherKind.Clear && _playerWeatherState.Overcast
                ? _copy.Ui("ui.weather.overcast") : WeatherName(_playerWeather.Kind);
            if (_playerWeather.Exotic != WeatherKind.Clear && _playerWeatherState.BaseKind != WeatherKind.Clear)
                weather += " + " + WeatherName(_playerWeatherState.BaseKind);
            string source = pinned > 0
                ? _copy.Ui("ui.weather.controlled").Replace("{seconds}",
                    Math.Ceiling(pinned / 1000d).ToString(System.Globalization.CultureInfo.InvariantCulture))
                : _copy.Ui("ui.weather.natural");
            return _copy.Ui("ui.weather.status").Replace("{weather}", weather)
                .Replace("{phase}", _copy.Ui("ui.phase." + _weather.PhaseAt(direction, _nowMs).ToString().ToLowerInvariant()))
                .Replace("{source}", source);
        }

        private string WeatherGroundText()
        {
            float ground = _playerWeather.PlayerGroundMultiplier;
            int percent = (int)Math.Round((1f - ground) / 0.125f * 100f);
            return _copy.Ui("ui.weather.ground").Replace("{percent}", percent.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .Replace("{mult}", ground.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
        }

        private string WeatherMovementText() => _copy.Ui("ui.weather.movement")
            .Replace("{move}", _playerWeather.PlayerMoveSpeedMultiplier.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture))
            .Replace("{sprint}", _playerWeather.PlayerSprintSpeedMultiplier.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture))
            .Replace("{roll}", _playerWeather.PlayerRollCooldownMultiplier.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));

        private void DrawWeatherDetails(AdventurePanel ui)
        {
            ui.Heading(_copy.Ui("ui.weather.title"));
            ui.Label(WeatherStatusText());
            ui.Label(WeatherMovementText());
            if (_playerWeather.PlayerGroundMultiplier < 1f) ui.Label(WeatherGroundText());
            ui.Label(_copy.Ui("ui.weather.visibility").Replace("{meters}", RenderSettings.fogEndDistance.ToString("0")));
            ui.Label(_copy.Ui("ui.weather.creatures")
                .Replace("{base}", _playerWeather.CreatureMoveSpeedMultiplier.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture))
                .Replace("{adapted}", _playerWeather.AdaptedCreatureMoveSpeedMultiplier.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)));
            ui.Label(_copy.Ui("ui.weather.territory")
                .Replace("{radius}", (_playerWeather.TerritoryRadiusPermille / 1000f).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture))
                .Replace("{drift}", _playerWeather.ProjectileDriftDegrees.ToString("0", System.Globalization.CultureInfo.InvariantCulture)));
            if (_playerWeather.StealthEnabled)
                ui.Label(_copy.Ui("ui.weather.stealth").Replace("{speed}", Encounter.SlowStealthSpeedThresholdMs.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)));
            bool hasElementEffects = false;
            for (int i = (int)Element.Metal; i <= (int)Element.Shadow; i++)
            {
                Element element = (Element)i;
                int power = _playerWeather.PowerDeltaPercent(element), cooldown = _playerWeather.CdDeltaPercent(element);
                if (power == 0 && cooldown == 0) continue;
                hasElementEffects = true;
                ui.Label(_copy.Ui("ui.weather.element").Replace("{element}", _copy.Element(element.ToString()))
                    .Replace("{power}", power.ToString("+0;-0;0", System.Globalization.CultureInfo.InvariantCulture))
                    .Replace("{cooldown}", cooldown.ToString("+0;-0;0", System.Globalization.CultureInfo.InvariantCulture)));
            }
            if (!hasElementEffects) ui.Label(_copy.Ui("ui.weather.no_element_effects"));
            if (!_utilityModifiers.CanWeatherControl)
            {
                ui.Label(_copy.Ui("ui.weather.missing_skill"));
                return;
            }
            ui.Heading(_copy.Ui("ui.weather.control"));
            int remaining = _weatherControl.CooldownRemainingMs(_nowMs);
            ui.Label(remaining == 0 ? _copy.Ui("ui.hud.cd_ready") : _copy.Ui("ui.weather.cooldown")
                .Replace("{seconds}", Math.Ceiling(remaining / 1000d).ToString(System.Globalization.CultureInfo.InvariantCulture)));
            float3 here = _rig.Config.ToDirection(PlayerPosition());
            bool indoor = _weather.IsIndoor(here);
            foreach (WeatherProfile weather in WeatherProfiles.All)
            {
                // REQ: weather.md#天氣操縱 — exotics only where native; indoors only exotics.
                bool exotic = WeatherProfiles.IsExotic(weather.Kind);
                if (exotic ? !_weather.IsNativeExotic(here, weather.Kind) : indoor) continue;
                if (!ui.Button(WeatherName(weather.Kind), remaining == 0)) continue;
                ShowControlResult(ui, TryControlWeather(weather.Kind), WeatherName(weather.Kind), "ui.weather.applied");
            }

            if (ui.Button(_copy.Ui("ui.weather.dispel"), remaining == 0))
                ShowControlResult(ui, TryDispelExotics(), "", "ui.weather.dispelled");
        }

        private void ShowControlResult(AdventurePanel ui, WeatherControlResult result, string weatherName, string appliedKey)
        {
            string key = result switch
            {
                WeatherControlResult.Applied => appliedKey,
                WeatherControlResult.MissingSkill => "ui.weather.missing_skill",
                WeatherControlResult.NotHere => "ui.weather.not_here",
                _ => "ui.weather.on_cooldown",
            };
            ui.Message = _copy.Ui(key).Replace("{weather}", weatherName);
            GUIUtility.ExitGUI();
        }

        private void DrawSkills(AdventurePanel ui)
        {
            ui.Heading(_copy.Ui("ui.journal.tab.Skills"));
            ui.Label(_copy.Ui("ui.skills.pause_hint"));
            DrawWeatherDetails(ui);
            if (ui.Button(_copy.Ui(_showLockedGathering ? "ui.skills.hide_locked" : "ui.skills.show_locked")))
            { _showLockedGathering = !_showLockedGathering; GUIUtility.ExitGUI(); }
            DrawCrafting(ui);
            GatheringContext environment = GatheringEnvironment();
            foreach (FoodAcquisitionMethod method in Enum.GetValues(typeof(FoodAcquisitionMethod)))
            {
                GatheringRefusalReason refusal = FoodGatheringRules.CanGather(method, environment);
                if (refusal == GatheringRefusalReason.MissingSkill && !_showLockedGathering) continue;
                ui.Label(_copy.Ui("gather." + method) + " · " + _copy.Ui("gather.reason." + refusal));
                if (ui.Button(_copy.Ui("ui.skills.use"), refusal == GatheringRefusalReason.None))
                {
                    if (method == FoodAcquisitionMethod.SetTrap)
                    {
                        refusal = _traps.TryPlaceTrap(environment, _nowMs, out TrapState _);
                        ui.Message = _copy.Ui(refusal == GatheringRefusalReason.None ? "ui.skills.trap_placed" : "gather.reason." + refusal);
                    }
                    else
                    {
                        refusal = FoodGatheringRules.TryGather(method, environment, _foodCatalog, _foodInventory, ref _foodRng, _gathered);
                        ui.Message = refusal == GatheringRefusalReason.None ? GatheredMessage() : _copy.Ui("gather.reason." + refusal);
                    }
                    GUIUtility.ExitGUI();
                }
            }
            foreach (TrapState trap in _traps.ActiveTraps)
                ui.Label(_copy.Ui("gather.SetTrap") + $" #{trap.Id} · {Math.Max(0, trap.ReadyAtMs - _nowMs) / 1000f:0.0}s");
            bool ready = false;
            foreach (TrapState trap in _traps.ActiveTraps) ready |= trap.IsReady(_nowMs);
            if (ui.Button(_copy.Ui("ui.skills.collect_traps"), ready))
            {
                ui.Message = TryCollectTrapMaterials() ? GatheredMessage() : _copy.Ui("craft.result.CapacityExceeded");
                GUIUtility.ExitGUI();
            }
            foreach (FieldUtility utility in _utilities)
            {
                ui.Label(_copy.Ui("utility." + utility) + " · " + _copy.Ui("utility.desc." + utility));
                bool toggle = utility == FieldUtility.Mount || utility == FieldUtility.Fly || utility == FieldUtility.Levitate ||
                              utility == FieldUtility.Swim || utility == FieldUtility.Dive || utility == FieldUtility.PartyStealth;
                if (toggle && ui.Button(_copy.Ui(_enabledUtilities.Contains(utility) ? "ui.skills.stop" : "ui.skills.use")))
                {
                    if (!_enabledUtilities.Remove(utility)) _enabledUtilities.Add(utility);
                    ApplyUtilities();
                    GUIUtility.ExitGUI();
                }
                else if (utility == FieldUtility.ReturnVillage && _layout != null)
                {
                    for (int sIdx = 0; sIdx < _layout.Settlements.Count; sIdx++)
                    {
                        if (!_discoveredSettlements.Contains(sIdx)) continue;
                        RunSettlement target = _layout.Settlements[sIdx];
                        string targetName = SettlementDisplayName(target);
                        if (ui.Button(_copy.Ui("utility.ReturnVillage") + " · " + targetName))
                        {
                            ui.Message = TryReturnToSettlement(sIdx, targetName);
                            GUIUtility.ExitGUI();
                        }
                    }
                }
                else if (utility == FieldUtility.QuickEscape && ui.Button(_copy.Ui("ui.skills.use"), CanQuickEscape()))
                {
                    QuickEscape();
                    GUIUtility.ExitGUI();
                }
                else if (utility < FieldUtility.GatherEssence && FieldUtilityRules.GetKind(utility) == FieldUtilityKind.Move && !toggle && !IsGatheringOrCooking(utility)
                    && utility != FieldUtility.QuickEscape && utility != FieldUtility.WeatherControl && utility != FieldUtility.ReturnVillage) ui.Label(_copy.Ui("ui.skills.unavailable"));
                else if (utility < FieldUtility.GatherEssence && FieldUtilityRules.GetKind(utility) == FieldUtilityKind.Trait &&
                    utility != FieldUtility.CatchRateUp && utility != FieldUtility.DropRateUp &&
                    utility != FieldUtility.PreventEscape && utility != FieldUtility.MagnetPickupDouble &&
                    utility != FieldUtility.ForageBerries && utility != FieldUtility.ProduceMilk &&
                    utility != FieldUtility.CulinaryPrep) ui.Label(_copy.Ui("ui.skills.unavailable"));
            }
            CookingStationKind station = CookingStation();
            var cooking = new CookingContext(station, _utilityModifiers, _partyElements);
            if (station == CookingStationKind.Kitchen) ui.Label(_copy.Ui("ui.skills.kitchen"));
            DrawProcessing(ui, cooking);
            DrawPot(ui, cooking);
            ui.Heading(_copy.Ui("ui.skills.recipes"));
            foreach (RecipeSpec recipe in _foodCatalog.Recipes)
            {
                // REQ: docs/design/cuisine.md#6 — undiscovered recipes show only their method and one ingredient category.
                if (!_outcome.DiscoveredRecipes.Contains(recipe.Id))
                {
                    ui.Label(RecipeHintText(recipe));
                    continue;
                }
                CookRefusalReason reason = CookingRules.CanCook(recipe, _foodCatalog, _foodInventory, cooking);
                ui.Label(_copy.Ui(recipe.OutputFoodId) + " · " + _copy.Ui("dish.method." + recipe.Method) + " · " + _copy.Ui("cook." + reason));
                foreach (RecipeIngredientRequirement requirement in recipe.Requirements)
                    ui.Label("· " + (requirement.IsCategoryMatch ? _copy.Ui("ingredient.category." + requirement.Category)
                        : _copy.Ui(requirement.IngredientId)) + " ×" + requirement.Quantity);
                if (ui.Button(_copy.Ui("ui.skills.recipe_fill")))
                {
                    if (_foodInventory.TryPlanRecipePot(recipe, _foodCatalog, _pot)) _potMethod = recipe.Method;
                    else ui.Message = _copy.Ui("cook." + CookRefusalReason.MissingIngredients);
                    GUIUtility.ExitGUI();
                }
                if (ui.Button(_copy.Ui("ui.skills.cook"), reason == CookRefusalReason.None))
                {
                    reason = CookingRules.TryCook(recipe, _foodCatalog, _foodInventory, cooking, out CookOutcome outcome);
                    if (reason == CookRefusalReason.None)
                    {
                        EnqueueQuality(outcome.Food.Id, outcome.QuantityProduced, outcome.Quality);
                        ui.Message = _copy.Ui(outcome.Food.Id) + " ×" + outcome.QuantityProduced + " · " + _copy.Ui("quality." + outcome.Quality);
                    }
                    else ui.Message = _copy.Ui("cook." + reason);
                    GUIUtility.ExitGUI();
                }
            }
        }

        private string RecipeHintText(RecipeSpec recipe) => _copy.Ui("ui.skills.recipe_unknown")
            .Replace("{method}", _copy.Ui("dish.method." + recipe.Method))
            .Replace("{category}", _copy.Ui("ingredient.category." + CookingRules.RecipeHintCategory(recipe, _foodCatalog)));

        private void EnqueueQuality(string foodId, int quantity, CookingQuality quality)
        {
            if (!_foodQualities.TryGetValue(foodId, out Queue<CookingQuality> qualities))
            { qualities = new Queue<CookingQuality>(); _foodQualities.Add(foodId, qualities); }
            for (int i = 0; i < quantity; i++) qualities.Enqueue(quality);
        }

        // REQ: docs/design/cuisine.md#1 — a discovered inhabited settlement doubles as a kitchen that unlocks every method and process.
        private CookingStationKind CookingStation()
        {
            if (_layout == null) return CookingStationKind.Field;
            float3 player = PlayerPosition();
            foreach (int index in _discoveredSettlements)
                if (IsWithinSettlement(_layout.Settlements[index], player)) return CookingStationKind.Kitchen;
            return CookingStationKind.Field;
        }

        private void DrawProcessing(AdventurePanel ui, CookingContext cooking)
        {
            ui.Heading(_copy.Ui("ui.skills.processing"));
            bool any = false;
            foreach (ProcessSpec process in _foodCatalog.Processes)
            {
                int held = _foodInventory.CountIngredient(process.InputIngredientId);
                ProcessRefusalReason reason = ProcessingRules.CanProcess(process, _foodInventory, cooking);
                if ((held == 0 || reason == ProcessRefusalReason.MissingSkill) && !_showLockedGathering) continue;
                any = true;
                ui.Label(_copy.Ui("process.kind." + process.Kind) + " · " + _copy.Ui(process.InputIngredientId) + $" ×{held} → " +
                         _copy.Ui(process.OutputIngredientId) + " · " + _copy.Ui("process.reason." + reason));
                if (!ui.Button(_copy.Ui("ui.skills.process"), reason == ProcessRefusalReason.None)) continue;
                reason = ProcessingRules.TryProcess(process, _foodInventory, cooking);
                ui.Message = reason == ProcessRefusalReason.None
                    ? _copy.Ui(process.OutputIngredientId) + " ×1"
                    : _copy.Ui("process.reason." + reason);
                GUIUtility.ExitGUI();
            }
            if (!any) ui.Label(_copy.Ui("ui.skills.processing_empty"));
        }

        private void DrawPot(AdventurePanel ui, CookingContext cooking)
        {
            ui.Heading(_copy.Ui("ui.skills.pot"));
            foreach (CookingMethod method in Enum.GetValues(typeof(CookingMethod)))
            {
                bool unlocked = CookingRules.HasSkillForMethod(method, cooking);
                if (!unlocked && !_showLockedGathering) continue;
                if (!ui.Button((_potMethod == method ? "● " : "") + _copy.Ui("dish.method." + method), unlocked)) continue;
                _potMethod = method;
                GUIUtility.ExitGUI();
            }
            // Units can leave the bag elsewhere (eaten raw, processed); the pot only lists what is still held.
            for (int i = _pot.Count - 1; i >= 0; i--)
                if (CountInPot(_pot[i]) > _foodInventory.CountIngredient(_pot[i])) _pot.RemoveAt(i);
            var contents = new List<string>(_pot.Count);
            foreach (string id in _pot) contents.Add(_copy.Ui(id));
            ui.Label(_pot.Count == 0 ? _copy.Ui("ui.skills.pot_empty") : string.Join(" + ", contents));
            foreach (IngredientSpec ingredient in _foodCatalog.Ingredients)
            {
                int spare = _foodInventory.CountIngredient(ingredient.Id) - CountInPot(ingredient.Id);
                if (spare <= 0) continue;
                if (!ui.Button("+ " + _copy.Ui(ingredient.Id) + $" ×{spare}", _pot.Count < PotCookingRules.MaxPotSize)) continue;
                _pot.Add(ingredient.Id);
                GUIUtility.ExitGUI();
            }
            if (_pot.Count == 0) return;
            if (ui.Button(_copy.Ui("ui.skills.pot_clear"))) { _pot.Clear(); GUIUtility.ExitGUI(); }
            PotResolution preview = PotCookingRules.Resolve(_potMethod, _pot, _foodCatalog, cooking);
            ui.Label(PotPreviewText(preview));
            if (!ui.Button(_copy.Ui("ui.skills.cook"), preview.Refusal == CookRefusalReason.None)) return;
            PotResolution cooked = PotCookingRules.TryCook(_potMethod, _pot, _foodCatalog, _foodInventory, cooking);
            if (cooked.Refusal == CookRefusalReason.None)
            {
                EnqueueQuality(cooked.Food.Id, cooked.Quantity, cooked.Quality);
                _pot.Clear();
                ui.Message = FoodName(cooked.Food) + " ×" + cooked.Quantity + " · " + _copy.Ui("quality." + cooked.Quality);
                // Only a free-form pot can discover a recipe: the recipe list shows undiscovered ones as hints without buttons.
                if (cooked.Kind == PotDishKind.Signature && _outcome.DiscoveredRecipes.Add(cooked.Recipe.Id))
                    ui.Message += " · " + _copy.Ui("ui.skills.recipe_new");
            }
            else ui.Message = _copy.Ui("cook." + cooked.Refusal);
            GUIUtility.ExitGUI();
        }

        private int CountInPot(string ingredientId)
        {
            int count = 0;
            foreach (string id in _pot) if (id == ingredientId) count++;
            return count;
        }

        private string PotPreviewText(PotResolution preview)
        {
            if (preview.Refusal != CookRefusalReason.None) return _copy.Ui("cook." + preview.Refusal);
            string key = preview.Kind == PotDishKind.Signature ? "ui.skills.pot_signature"
                : preview.Kind == PotDishKind.Free ? "ui.skills.pot_free" : "ui.skills.pot_dubious";
            return _copy.Ui(key).Replace("{dish}", FoodName(preview.Food)) + " · " + _copy.Ui("quality." + preview.Quality) +
                   " · " + FoodDescription(preview.Food);
        }

        private string FoodName(FoodItemSpec food)
        {
            if (!food.FreeDish.HasValue) return _copy.Ui(food.Id);
            FreeDishDescriptor dish = food.FreeDish.Value;
            string family = dish.Family == FoodEffectFamily.None ? ""
                : _copy.Ui("dish.family." + dish.Family).Replace("{element}", _copy.Element(dish.FamilyElement.ToString()));
            string name = _copy.Ui("dish.free_name").Replace("{family}", family)
                .Replace("{method}", _copy.Ui("dish.method." + dish.Method)).Replace("{theme}", _copy.Ui("dish.theme." + dish.Theme));
            while (name.Contains("  ")) name = name.Replace("  ", " ");
            name = name.Trim();
            return dish.Level > 0 ? name + " Lv" + dish.Level : name;
        }

        private static bool IsGatheringOrCooking(FieldUtility utility) => utility == FieldUtility.Fishing ||
            utility == FieldUtility.CookFire || utility == FieldUtility.HuntGround || utility == FieldUtility.HuntSky ||
            utility == FieldUtility.SetTrap || utility == FieldUtility.CastNet || utility == FieldUtility.HarvestCrops ||
            utility == FieldUtility.ChillBrew;

        private bool CanQuickEscape()
        {
            if (!_utilityModifiers.CanQuickEscape && !_utilities.Contains(FieldUtility.QuickEscape)) return false;
            bool engaged = false;
            foreach (EncounterUnit unit in _encounter.Units)
            {
                if (unit.Side != EncounterSide.Wild || !unit.IsActive || !unit.Engaged) continue;
                if (unit.IsBoss) return false;
                engaged = true;
            }
            return engaged;
        }

        private string SettlementDisplayName(RunSettlement settlement)
        {
            if (settlement.Index == 0) return _copy.Ui("ui.skills.return_start");
            return settlement.Kind switch
            {
                SettlementKind.SkyTribe => _copy.Ui("ui.skills.return_sky"),
                SettlementKind.DeepSeaCity => _copy.Ui("ui.skills.return_deep_sea"),
                SettlementKind.IslandSettlement => _copy.Ui("ui.skills.return_island"),
                _ => _copy.Ui("ui.skills.return_village").Replace("{index}", settlement.Index.ToString()),
            };
        }

        private string TryReturnToSettlement(int settlementIndex, string targetName)
        {
            if (_layout == null || settlementIndex < 0 || settlementIndex >= _layout.Settlements.Count)
            {
                return _copy.Ui("ui.skills.unavailable");
            }

            RunSettlement target = _layout.Settlements[settlementIndex];
            ReturnSettlementResult access = SpatialTraversalRules.CanReturnToSettlement(
                _utilityModifiers, target.Domain, _discoveredSettlements.Contains(settlementIndex));
            if (access.RequiresDive)
            {
                return _copy.Ui("ui.skills.return_needs_dive").Replace("{target}", targetName);
            }

            if (!access.Allowed)
            {
                return _copy.Ui("ui.skills.unavailable");
            }

            RelocatePlayerAndParty(target.Center, SettlementAltitudeAt(target) + 1f);
            return _copy.Ui("ui.skills.returned").Replace("{target}", targetName);
        }

        private void RelocatePlayerAndParty(float3 destination, float altitudeM)
        {
            _encounter.ForceDisengageAllWild();
            _encounter.RelocateParty(destination);
            Vector3 position = _rig.Config.ToWorld(destination, altitudeM);
            _rig.Character.Teleport(position);
            _command = new AiCommand(AiCommandKind.Follow, CombatAi.NoTarget);
            _lockedTarget = -1;
            _lockedObjectId = -1;
            _encounter.ClearObjectFocus();
            UpdateEnvironment(position);
        }

        private void QuickEscape()
        {
            if (!CanQuickEscape()) { _journal.Message = _copy.Ui("ui.skills.no_battle"); return; }
            float3 origin = _rig.Config.ToDirection(PlayerPosition());
            var frame = Planet.PlanetFrame.FromUp(origin);
            float angle = 2f * Encounter.DisengageRadiusM / _rig.Config.Radius;
            for (int i = 0; i < 8; i++)
            {
                float bearing = i * math.PI / 4;
                float3 tangent = frame.North * math.cos(bearing) + frame.East * math.sin(bearing);
                float3 destination = math.normalize(origin * math.cos(angle) + tangent * math.sin(angle));
                float clearance = _balance.Collision.CharacterMm * 0.001f;
                foreach (int id in _team.Active) clearance = math.max(clearance, _encounter.Units[id].CollisionRadiusM);
                if (_rig.IsSea(destination) || _surface.IsBlocked(destination, clearance)) continue;
                bool safe = true;
                foreach (EncounterUnit unit in _encounter.Units)
                {
                    if (unit.Side == EncounterSide.Wild && unit.IsActive &&
                        math.distance(_rig.Config.ToDirection(unit.Position), destination) * _rig.Config.Radius < Encounter.DisengageRadiusM)
                    { safe = false; break; }
                }
                if (!safe) continue;
                RelocatePlayerAndParty(destination, _rig.SurfaceHeight(destination) + 1f);
                _journal.Message = _copy.Ui("ui.skills.escaped");
                return;
            }
            _journal.Message = _copy.Ui("ui.skills.no_safe_ground");
        }

        private string GatheredMessage()
        {
            string message = _copy.Ui("ui.skills.gathered");
            foreach (GatheredStack stack in _gathered) message += " " + _copy.Ui(stack.Ingredient.Id) + " ×" + stack.Quantity;
            return message;
        }

        private void ApplyUtilities()
        {
            _utilityModifiers = FieldUtilityRules.ComputeTeamModifiers(_enabledUtilities);
            _encounter.SetExplorationUtilityModifiers(_utilityModifiers.WildDetectionPermille, _utilityModifiers.PreventsWildEscape);
            ApplyPlayerEnvironment();
        }
    }
}
