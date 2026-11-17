using System;
using System.Collections.Generic;
using Cosmimera.Combat;
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

        private EncounterUnit AddEquippedPartyCreature(int slot)
        {
            var moves = new List<MoveDef>();
            var traits = new List<TraitDef>();
            bool overload = false;
            if (_setup.Partners != null)
            {
                Core.Contracts.Contract.Requires(_setup.Database != null && _setup.Owned != null,
                    "equipped runs need database and owned individuals");
                foreach (int subId in _setup.Partners.SubsOf(_setup.PartyIds[slot]))
                {
                    OwnedCreature sub = OwnedAtDeparture(subId);
                    CreatureDef def = RequireCreature(sub.SpeciesId);
                    if (sub.Vessel == VesselKind.TalismanCard) moves.Add(def.SignatureMove);
                    else if (sub.Vessel == VesselKind.GlassBead) traits.Add(def.Trait);
                    else overload = true;
                }
            }
            EncounterUnit unit = _encounter.AddEquippedCreature(_setup.Party[slot], moves, traits, _plan.LandingDirection,
                _progression.LevelOf(_partyExp[slot]));
            EquipmentModifiers equipment = EquipmentRules.Collect(_setup.PartyIds[slot],
                _setup.Equipment ?? Array.Empty<EquippedItem>(), _craftCatalog, GripsAtDeparture(_setup.PartyIds[slot]), _balance.Grips);
            var combinedEffects = new List<EffectSpec>(equipment.CreatureEffects);
            if (_setup.Partners != null)
            {
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
            unit.Combatant.SetWeapons(equipment.Weapons);
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
            if (_setup.Database != null) _codex = new CodexPanel(_setup.Database, _copy, OwnsSpecies, _setup.Portraits);
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
                ui.Label(_copy.Ui(food.Id) + $" ×{count} · " + FoodDescription(food));
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
            int region = RunPlanner.RegionAt(_plan, _rig.Config.ToDirection(PlayerPosition()));
            bool controlled = _weatherControl.TryGetOverride(region, _nowMs, out _);
            long remaining = WeatherPlanner.DefaultSlotDurationMs - _nowMs % WeatherPlanner.DefaultSlotDurationMs;
            return _copy.Ui("ui.weather.status").Replace("{weather}", WeatherName(_playerWeather.Kind))
                .Replace("{source}", _copy.Ui(controlled ? "ui.weather.controlled" : "ui.weather.natural"))
                .Replace("{seconds}", Math.Ceiling(remaining / 1000d).ToString(System.Globalization.CultureInfo.InvariantCulture));
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
            foreach (WeatherProfile weather in WeatherProfiles.All)
            {
                if (!ui.Button(WeatherName(weather.Kind), remaining == 0)) continue;
                WeatherControlResult result = TryControlWeather(weather.Kind);
                string key = result == WeatherControlResult.Applied ? "ui.weather.applied"
                    : result == WeatherControlResult.MissingSkill ? "ui.weather.missing_skill" : "ui.weather.on_cooldown";
                ui.Message = _copy.Ui(key).Replace("{weather}", WeatherName(weather.Kind));
                GUIUtility.ExitGUI();
            }
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
                bool toggle = utility == FieldUtility.Mount || utility == FieldUtility.Levitate || utility == FieldUtility.PartyStealth;
                if (toggle && ui.Button(_copy.Ui(_enabledUtilities.Contains(utility) ? "ui.skills.stop" : "ui.skills.use")))
                {
                    if (!_enabledUtilities.Remove(utility)) _enabledUtilities.Add(utility);
                    ApplyUtilities();
                    GUIUtility.ExitGUI();
                }
                else if (utility == FieldUtility.QuickEscape && ui.Button(_copy.Ui("ui.skills.use"), CanQuickEscape()))
                {
                    QuickEscape();
                    GUIUtility.ExitGUI();
                }
                else if (FieldUtilityRules.GetKind(utility) == FieldUtilityKind.Move && !toggle && !IsGatheringOrCooking(utility)
                    && utility != FieldUtility.QuickEscape && utility != FieldUtility.WeatherControl) ui.Label(_copy.Ui("ui.skills.unavailable"));
                else if (FieldUtilityRules.GetKind(utility) == FieldUtilityKind.Trait &&
                    utility != FieldUtility.CatchRateUp && utility != FieldUtility.DropRateUp &&
                    utility != FieldUtility.PreventEscape && utility != FieldUtility.MagnetPickupDouble &&
                    utility != FieldUtility.ForageBerries && utility != FieldUtility.ProduceMilk &&
                    utility != FieldUtility.CulinaryPrep) ui.Label(_copy.Ui("ui.skills.unavailable"));
            }
            var cooking = new CookingContext(CookingStationKind.Field, _utilityModifiers, _partyElements);
            ui.Heading(_copy.Ui("ui.skills.recipes"));
            foreach (RecipeSpec recipe in _foodCatalog.Recipes)
            {
                CookRefusalReason reason = CookingRules.CanCook(recipe, _foodCatalog, _foodInventory, cooking);
                ui.Label(_copy.Ui(recipe.OutputFoodId) + " · " + _copy.Ui("cook." + reason));
                foreach (RecipeIngredientRequirement requirement in recipe.Requirements)
                    ui.Label("· " + (requirement.IsCategoryMatch ? _copy.Ui("ingredient.category." + requirement.Category)
                        : _copy.Ui(requirement.IngredientId)) + " ×" + requirement.Quantity);
                if (ui.Button(_copy.Ui("ui.skills.cook"), reason == CookRefusalReason.None))
                {
                    reason = CookingRules.TryCook(recipe, _foodCatalog, _foodInventory, cooking, out CookOutcome outcome);
                    if (reason == CookRefusalReason.None)
                    {
                        if (!_foodQualities.TryGetValue(outcome.Food.Id, out Queue<CookingQuality> qualities))
                        { qualities = new Queue<CookingQuality>(); _foodQualities.Add(outcome.Food.Id, qualities); }
                        for (int i = 0; i < outcome.QuantityProduced; i++) qualities.Enqueue(outcome.Quality);
                        ui.Message = _copy.Ui(outcome.Food.Id) + " ×" + outcome.QuantityProduced + " · " + _copy.Ui("quality." + outcome.Quality);
                    }
                    else ui.Message = _copy.Ui("cook." + reason);
                    GUIUtility.ExitGUI();
                }
            }
        }

        private static bool IsGatheringOrCooking(FieldUtility utility) => utility == FieldUtility.Fishing ||
            utility == FieldUtility.CookFire || utility == FieldUtility.HuntGround || utility == FieldUtility.HuntSky ||
            utility == FieldUtility.SetTrap || utility == FieldUtility.CastNet || utility == FieldUtility.HarvestCrops;

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
                _encounter.ForceDisengageAllWild();
                _encounter.RelocateParty(destination);
                var body = _rig.Character.GetComponent<Rigidbody>();
                Vector3 position = _rig.Config.ToWorld(destination, _rig.SurfaceHeight(destination) + 1f);
                body.position = position;
                body.linearVelocity = Vector3.zero;
                _rig.Character.transform.position = position;
                _command = new AiCommand(AiCommandKind.Follow, CombatAi.NoTarget);
                _lockedTarget = -1;
                _lockedObjectId = -1;
                _encounter.ClearObjectFocus();
                UpdateEnvironment(position);
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
