using System;
using System.Collections.Generic;
using Cosmimera.Combat;
using Cosmimera.Core.Contracts;
using Cosmimera.Crafting;
using Cosmimera.Creatures;
using Cosmimera.GameData;
using Cosmimera.Lobby;
using Cosmimera.Save;
using Cosmimera.WorldGen;
using UnityEngine;

namespace Cosmimera.Bootstrap.Demo
{
    /// <summary>
    /// Live bridge exposing persistent save, menu services, Star Rift launches, and starter selection between
    /// <see cref="DemoGame"/> and an active <see cref="RunSession"/>.
    /// REQ: docs/design/lobby-meta.md, docs/design/beginner-quest.md.
    /// </summary>
    /// <remarks>
    /// Pre: constructed by DemoGame after LoadData completes.
    /// Post: provides valid references and transactional save commits without requiring a separate lobby screen.
    /// Side effects: save mutations commit to disk via DemoGame except deferred beginner-quest starter previews.
    /// </remarks>
    public sealed class MainWorldServices
    {
        private readonly DemoGame _game;

        internal MainWorldServices(DemoGame game)
        {
            Contract.Requires(game != null, "game is required");
            _game = game;
        }

        /// <summary>Current live save snapshot.</summary>
        /// <remarks>Pre: none. Post: non-null. Side effects: none.</remarks>
        public LobbySave Save => _game.LiveSave;

        /// <summary>Game database asset.</summary>
        /// <remarks>Pre: none. Post: non-null. Side effects: none.</remarks>
        public GameDatabase Database => _game.LiveDatabase;

        /// <summary>Player and creature progression table.</summary>
        /// <remarks>Pre: none. Post: non-null. Side effects: none.</remarks>
        public Progression Progression => _game.LiveProgression;

        /// <summary>Combat balance rules.</summary>
        /// <remarks>Pre: none. Post: non-null. Side effects: none.</remarks>
        public CombatBalance Balance => _game.LiveBalance;

        /// <summary>Crafting catalog.</summary>
        /// <remarks>Pre: none. Post: non-null. Side effects: none.</remarks>
        public CraftCatalog CraftCatalog => _game.LiveCraftCatalog;

        /// <summary>Current localization copy helper.</summary>
        /// <remarks>Pre: none. Post: non-null. Side effects: none.</remarks>
        public DemoCopy Copy => _game.LiveCopy;

        /// <summary>Current active language code (zh-TW / en / ja).</summary>
        /// <remarks>Pre: none. Post: non-empty string. Side effects: none.</remarks>
        public string Language => _game.LiveLanguage;

        /// <summary>Available selectable non-cradle planet types.</summary>
        /// <remarks>Pre: none. Post: non-null list. Side effects: allocates returned list.</remarks>
        public List<PlanetTypeDef> SelectablePlanetTypes() => _game.BuildSelectablePlanetTypes();

        /// <summary>Resolves a planet type definition by <paramref name="planetTypeId"/>.</summary>
        /// <remarks>Pre: planetTypeId non-empty. Post: matching PlanetTypeDef or null. Side effects: none.</remarks>
        public PlanetTypeDef FindPlanetType(string planetTypeId)
        {
            Contract.Requires(!string.IsNullOrEmpty(planetTypeId), "planetTypeId is required");
            return _game.FindPlanetTypeById(planetTypeId);
        }

        /// <summary>Commits <paramref name="candidate"/> to disk and updates the live save reference.</summary>
        /// <remarks>
        /// Pre: candidate != null.
        /// Post: returns true and replaces LiveSave on write success; returns false with localized error on failure.
        /// Side effects: atomic file I/O; updates DemoGame party id tracking.
        /// </remarks>
        public bool CommitSave(LobbySave candidate, out string error)
        {
            Contract.Requires(candidate != null, "candidate is required");
            return _game.CommitFromServices(candidate, out error);
        }

        /// <summary>Cycles the active UI language (zh-TW → en → ja) and returns the rebuilt copy.</summary>
        /// <remarks>Pre: none. Post: returned DemoCopy reflects new Language. Side effects: mutates DemoGame language.</remarks>
        public DemoCopy CycleLanguage() => _game.CycleLanguageFromServices();

        /// <summary>Opens the developer test-save switcher overlay.</summary>
        /// <remarks>Pre: none. Post: overlay shown. Side effects: opens UI overlay.</remarks>
        public void OpenDevTestSaves() => _game.OpenDevTestSavesOverlay();

        /// <summary>Quits the application.</summary>
        /// <remarks>Pre: none. Post: requests application exit. Side effects: Application.Quit.</remarks>
        public void QuitGame() => Application.Quit();

        /// <summary>Verifies and deducts elemental material cost to enter a Star Rift.</summary>
        /// <remarks>
        /// Pre: planetType != null.
        /// Post: returns true and persists updated material stock iff materials were sufficient and save commit succeeded.
        /// Side effects: mutates Save.Materials and writes save on true.
        /// </remarks>
        public bool TryPayForStarRift(StarRiftEntry rift, PlanetTypeDef planetType, out string error)
        {
            Contract.Requires(planetType != null, "planetType is required");
            return _game.TryPayStarSelectCostFromServices(rift.Size, rift.MainElement, out error);
        }

        /// <summary>
        /// Records the in-world Stage 3 starter choice in memory (persisted on Stage 6 victory) and resolves Friend/Rival starters.
        /// </summary>
        /// <remarks>
        /// Pre: !string.IsNullOrEmpty(characterId) and starter != null.
        /// Post: returns true; friendStarter counters/is countered per ring; rivalStarter counters player; starterInstanceId &gt;= 1.
        /// Side effects: updates in-memory save copy for this beginner quest session.
        /// </remarks>
        public bool RecordStarterChoice(
            string characterId,
            CreatureDef starter,
            out CreatureDef friendStarter,
            out CreatureDef rivalStarter,
            out int starterInstanceId)
        {
            Contract.Requires(!string.IsNullOrEmpty(characterId), "characterId is required");
            Contract.Requires(starter != null, "starter is required");
            return _game.RecordStarterChoiceFromServices(characterId, starter, out friendStarter, out rivalStarter, out starterInstanceId);
        }

        /// <summary>Draws the full Party / Sub-Partner / Evolution UI into <paramref name="ui"/>.</summary>
        /// <remarks>Pre: ui != null. Post: UI populated. Side effects: may commit save and invoke onPartyChanged on edit.</remarks>
        internal void DrawPartnersTab(AdventurePanel ui, bool allowEdit, Action onPartyChanged) =>
            _game.DrawPartnersInMenu(ui, allowEdit, onPartyChanged);

        /// <summary>Draws the Equipment Paperdoll &amp; NG+ Cosmetics UI into <paramref name="ui"/>.</summary>
        /// <remarks>Pre: ui != null. Post: UI populated. Side effects: may commit save and update preview.</remarks>
        internal void DrawEquipmentTab(AdventurePanel ui, Action onEquipmentChanged) =>
            _game.DrawEquipmentInMenu(ui, onEquipmentChanged);
    }
}
