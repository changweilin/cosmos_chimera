using System;
using System.Collections.Generic;
using System.IO;
using Cosmimera.Capture;
using Cosmimera.Codex;
using Cosmimera.Core.Contracts;
using Cosmimera.Creatures;
using Cosmimera.Crafting;
using Cosmimera.GameData;
using UnityEngine;

namespace Cosmimera.Save
{
    /// <summary>Cumulative battle count recorded against one wandering trainer profession ID.</summary>
    /// <remarks>Pre: professionId valid snake_case id; count &gt;= 1. Post: immutable. Side effects: none.</remarks>
    public readonly struct TrainerBattleCount
    {
        /// <summary>Creates a battle count entry for one profession.</summary>
        /// <remarks>Pre: professionId valid snake_case id; count &gt;= 1. Post: fields equal arguments. Side effects: none.</remarks>
        public TrainerBattleCount(string professionId, int count)
        {
            Contract.Requires(CraftInventory.ValidId(professionId), "professionId must be valid snake_case id");
            Contract.Requires(count >= 1, "count must be >= 1");
            ProfessionId = professionId;
            Count = count;
        }

        /// <summary>Trainer profession identifier (<c>trv_*</c>, <c>era_*</c> or <c>cul_*</c>).</summary>
        /// <remarks>Pre: none. Post: valid snake_case id. Side effects: none.</remarks>
        public string ProfessionId { get; }

        /// <summary>Cumulative recorded battles across non-abandoned runs.</summary>
        /// <remarks>Pre: none. Post: &gt;= 1. Side effects: none.</remarks>
        public int Count { get; }
    }

    /// <summary>
    /// Lobby save: owned creatures and party. REQ: lobby-meta.md#進度保存 — Q-M4 roguelite: no mid-planet state is
    /// ever saved; a run only adds creatures here when it ends (victory, defeat or abandon).
    /// </summary>
    /// <remarks>
    /// Format: UTF-8 JSON, <see cref="FormatVersion"/> at the top. P4-06 (migrations, retired ids) builds on this; a
    /// newer version than we know is refused instead of silently dropping fields.
    /// Version 2 (P2-12): <c>exp</c> is cumulative level XP. Version 1 stored the old evolution XP, whose meaning differs,
    /// so it is not converted: a v1 creature restarts at T(stage floor) (option A, level-progression.md#獎勵量).
    /// Version 3 (P2-05, Q-C32): root <c>materials</c> — the lobby-wide evolution material stock. Older saves load with an
    /// empty stock; no past run's drops are reconstructed.
    /// Version 4 (D0-B2): root <c>player_exp</c> — cumulative player XP. Older saves load with 0 (player LV1); no past
    /// run's XP is reconstructed.
    /// Version 5: root <c>partners</c> stores main/sub instance-id pairs; formats 1–4 start unequipped.
    /// Version 6 (P4-05): root <c>eggs</c> — eggs incubating in the hatchery; formats 1–5 load with none.
    /// Version 7: root <c>craft_stock</c> stores gathered materials and crafted items; formats 1–6 load with none.
    /// Version 8: creature entries add <c>visual_vessel</c> (appearance preserved on late-game vessel conversion); formats 1–7 default <c>VisualVessel</c> to <c>Vessel</c>.
    /// Version 9 (D0-U3): equipment slots become main_hand/off_hand/upper/lower/accessory; formats 7–8 migrate
    /// weapon → main_hand, armor → upper, accessory → accessory (crafting.md#穿戴與數值).
    /// Version 10: root codex stores permanent knowledge; older saves recover owned forms.
    /// Version 11 (P3-31): creature entries add <c>niche_id</c> and <c>habit</c>; formats 1–10 leave <c>NicheId</c> empty
    /// until migrated once per individual via <c>SetEcology</c>.
    /// Version 12: root <c>presets</c> — up to MaxPresets favourite loadouts (party, subs, equipment, appearance);
    /// formats 1–11 load with none.
    /// Version 14 (P1-17b, gear-looks.md#外觀與數值): craft stacks add <c>looks</c> (tagged copies, oldest first), equipment
    /// entries add <c>look</c>, root <c>last_planet</c> names the planet lobby crafting takes its style from. Formats 1–13
    /// load untagged (each copy reads ItemLook.Default) with the cradle planet as last planet.
    /// Version 15 (P4-10, trainer-battles.md#職業圖鑑): root <c>trainer_battles</c> (per-profession battle counts
    /// unlocking character codex entries at 1 and 10 battles) and <c>completed_trials</c> (terrain trial badges).
    /// Formats 1–14 load with empty counts and no completed trials.
    /// Pre: main thread. Side effects: instance methods mutate this object only; Load/Write do file I/O.
    /// </remarks>
    public sealed class LobbySave
    {
        /// <summary>Current save format version.</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const int FormatVersion = 15;

        /// <summary>Cumulative battles against one trainer profession required for <see cref="CodexLevel.Complete"/>.</summary>
        /// <remarks>REQ: docs/design/trainer-battles.md#職業圖鑑. Pre: none. Post: constant 10. Side effects: none.</remarks>
        public const int ProfessionCompleteBattles = 10;

        /// <summary>Number of favourite-loadout slots.</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const int MaxPresets = 5;

        private readonly LoadoutPreset[] _presets = new LoadoutPreset[MaxPresets];

        /// <summary>Persistent encyclopedia knowledge, including known subjects no longer owned.</summary>
        /// <remarks>Pre: main thread. Post: same instance for this save's lifetime. Side effects: mutating it changes this save.</remarks>
        public CodexProgress Codex { get; } = new CodexProgress();

        /// <summary>Party size cap (creatures.md#隊伍規則: 6 主力).</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const int MaxParty = 6;

        private readonly List<OwnedCreature> _owned = new List<OwnedCreature>();
        private readonly List<int> _party = new List<int>();
        private readonly List<PartnerPair> _partners = new List<PartnerPair>();
        private readonly List<IncubatingEgg> _eggs = new List<IncubatingEgg>();
        private int _nextInstanceId = 1;
        private readonly MaterialInventory _materials = new MaterialInventory();
        private readonly CraftInventory _craftStock = new CraftInventory();
        private readonly EquipmentLoadout _equipment = new EquipmentLoadout();
        private readonly Dictionary<string, int> _trainerBattles = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly HashSet<string> _completedTrials = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Cumulative battle count recorded for <paramref name="professionId"/> (0 if never battled).</summary>
        /// <remarks>Pre: professionId non-empty. Post: &gt;= 0. Side effects: none.</remarks>
        public int TrainerBattleCountOf(string professionId)
        {
            Contract.Requires(!string.IsNullOrEmpty(professionId), "professionId must be non-empty");
            return _trainerBattles.TryGetValue(professionId, out int count) ? count : 0;
        }

        /// <summary>Sorted snapshot of all trainer professions with at least one recorded battle.</summary>
        /// <remarks>Pre: none. Post: sorted by ProfessionId ordinal. Side effects: allocates.</remarks>
        public IReadOnlyList<TrainerBattleCount> TrainerBattlesSnapshot()
        {
            if (_trainerBattles.Count == 0)
            {
                return Array.Empty<TrainerBattleCount>();
            }

            var keys = new List<string>(_trainerBattles.Keys);
            keys.Sort(StringComparer.Ordinal);
            var list = new List<TrainerBattleCount>(keys.Count);
            foreach (string key in keys)
            {
                list.Add(new TrainerBattleCount(key, _trainerBattles[key]));
            }

            return list;
        }

        /// <summary>
        /// Adds <paramref name="delta"/> completed battles against <paramref name="professionId"/> and advances the
        /// corresponding <see cref="CodexKind.Character"/> entry (Basic at 1, Complete at <see cref="ProfessionCompleteBattles"/>).
        /// </summary>
        /// <remarks>
        /// REQ: docs/design/trainer-battles.md#職業圖鑑.
        /// Pre: professionId valid snake_case id; delta &gt;= 1.
        /// Post: TrainerBattleCountOf(professionId) increased by delta (saturating at int.MaxValue); Codex updated.
        /// Side effects: mutates trainer battle counts and Codex.
        /// </remarks>
        public void AddTrainerBattles(string professionId, int delta)
        {
            Contract.Requires(CraftInventory.ValidId(professionId), "professionId must be valid snake_case id");
            Contract.Requires(delta >= 1, "delta must be >= 1");

            _trainerBattles.TryGetValue(professionId, out int existing);
            long sum = (long)existing + delta;
            int total = sum >= int.MaxValue ? int.MaxValue : (int)sum;
            _trainerBattles[professionId] = total;

            Codex.Record(CodexEvent.Dialogue, professionId);
            if (total >= ProfessionCompleteBattles)
            {
                Codex.Record(CodexEvent.CharacterQuest, professionId);
            }
        }

        /// <summary>True when the terrain trial badge for <paramref name="terrainId"/> has been earned.</summary>
        /// <remarks>Pre: terrainId non-empty. Post: boolean badge state. Side effects: none.</remarks>
        public bool HasTrialBadge(string terrainId)
        {
            Contract.Requires(!string.IsNullOrEmpty(terrainId), "terrainId must be non-empty");
            return _completedTrials.Contains(terrainId);
        }

        /// <summary>Sorted snapshot of completed terrain trial identifiers.</summary>
        /// <remarks>Pre: none. Post: sorted ordinal list. Side effects: allocates.</remarks>
        public IReadOnlyList<string> CompletedTrialsSnapshot()
        {
            if (_completedTrials.Count == 0)
            {
                return Array.Empty<string>();
            }

            var list = new List<string>(_completedTrials);
            list.Sort(StringComparer.Ordinal);
            return list;
        }

        /// <summary>Records completion of the terrain trial for <paramref name="terrainId"/>.</summary>
        /// <remarks>Pre: terrainId valid snake_case id. Post: HasTrialBadge(terrainId) is true; returns true iff newly earned. Side effects: mutates completed trial set.</remarks>
        public bool RecordTrialCompleted(string terrainId)
        {
            Contract.Requires(CraftInventory.ValidId(terrainId), "terrainId must be valid snake_case id");
            return _completedTrials.Add(terrainId);
        }

        /// <summary>Persistent gathering and crafting inventory, separate from evolution materials and run-only food.</summary>
        /// <remarks>Pre: main thread. Post: same inventory for this save's lifetime. Side effects: mutating it changes this save.</remarks>
        public CraftInventory CraftStock => _craftStock;

        /// <summary>Player and owned-creature equipment; item copies are reserved outside CraftStock.</summary>
        /// <remarks>Pre: main thread; callers validate owner ids against Owned. Post: same instance. Side effects: mutations change this save.</remarks>
        public EquipmentLoadout Equipment => _equipment;

        /// <summary>Planet lobby crafting takes its gear style from: the most recently departed planet, else the cradle planet.</summary>
        /// <remarks>REQ: gear-looks.md#外觀與數值. Pre: none. Post: constructed origin. Side effects: none.</remarks>
        public GearOrigin LastPlanet { get; private set; } = GearOrigin.Cradle;

        /// <summary>Records the planet just departed for.</summary>
        /// <remarks>Pre: origin constructed. Post: LastPlanet == origin. Side effects: mutates this save.</remarks>
        public void SetLastPlanet(GearOrigin origin)
        {
            Contract.Requires(CraftInventory.ValidId(origin.PlanetTypeId), "origin must be constructed");
            LastPlanet = origin;
        }

        /// <summary>Cumulative player XP (level-progression.md#玩家等級).</summary>
        /// <remarks>Pre: none. Post: &gt;= 0. Side effects: none.</remarks>
        public int PlayerExp { get; private set; }

        /// <summary>Format version the save was read from (FormatVersion for a new save); lower means it was migrated.</summary>
        /// <remarks>Pre: none. Post: 1..FormatVersion. Side effects: none.</remarks>
        public int LoadedFormatVersion { get; private set; } = FormatVersion;

        /// <summary>Starter line chosen in the lobby, or null before the first choice.</summary>
        /// <remarks>Pre: none. Post: none. Side effects: none.</remarks>
        public string StarterLine { get; private set; }

        /// <summary>Chosen player character id (P7-02a, beginner-quest.md).</summary>
        /// <remarks>Pre: none. Post: non-null (empty before selection). Side effects: none.</remarks>
        public string CharacterId { get; private set; } = string.Empty;

        /// <summary>Assigned friend character id (next in ring order from CharacterId).</summary>
        /// <remarks>Pre: none. Post: non-null (empty before selection). Side effects: none.</remarks>
        public string FriendId { get; private set; } = string.Empty;

        /// <summary>Assigned rival character id (second in ring order from CharacterId).</summary>
        /// <remarks>Pre: none. Post: non-null (empty before selection). Side effects: none.</remarks>
        public string RivalId { get; private set; } = string.Empty;

        /// <summary>Rival's starter species id (counters player starter).</summary>
        /// <remarks>Pre: none. Post: non-null (empty before tutorial completion). Side effects: none.</remarks>
        public string RivalStarterId { get; private set; } = string.Empty;

        /// <summary>Friend's starter species id (remaining starter).</summary>
        /// <remarks>Pre: none. Post: non-null (empty before tutorial completion). Side effects: none.</remarks>
        public string FriendStarterId { get; private set; } = string.Empty;

        /// <summary>True once the beginner quest is finished (or migrated from an existing v1..v12 save with creatures).</summary>
        /// <remarks>Pre: none. Post: none. Side effects: none.</remarks>
        public bool TutorialDone { get; private set; }

        /// <summary>Records beginner-quest completion and character/rival/friend assignments (P7-02a).</summary>
        /// <remarks>
        /// Pre: all ids non-null and non-whitespace.
        /// Post: TutorialDone == true and all five ids match the arguments.
        /// Side effects: mutates this save.
        /// </remarks>
        public void CompleteTutorial(string characterId, string friendId, string rivalId,
            string rivalStarterId, string friendStarterId)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(characterId) && !string.IsNullOrWhiteSpace(friendId) &&
                !string.IsNullOrWhiteSpace(rivalId) && !string.IsNullOrWhiteSpace(rivalStarterId) &&
                !string.IsNullOrWhiteSpace(friendStarterId), "all tutorial ids are required");
            CharacterId = characterId;
            FriendId = friendId;
            RivalId = rivalId;
            RivalStarterId = rivalStarterId;
            FriendStarterId = friendStarterId;
            TutorialDone = true;
        }

        /// <summary>Assigns player/friend/rival character ids for an existing pre-v13 save that skipped tutorial.</summary>
        /// <remarks>
        /// Pre: all three ids non-null and non-whitespace.
        /// Post: CharacterId, FriendId, RivalId match the arguments.
        /// Side effects: mutates this save.
        /// </remarks>
        public void SetCharacters(string characterId, string friendId, string rivalId)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(characterId) && !string.IsNullOrWhiteSpace(friendId) &&
                !string.IsNullOrWhiteSpace(rivalId), "character ids are required");
            CharacterId = characterId;
            FriendId = friendId;
            RivalId = rivalId;
        }

        /// <summary>Owned creatures in acquisition order.</summary>
        /// <remarks>Pre: none. Post: unique InstanceIds. Side effects: none.</remarks>
        public IReadOnlyList<OwnedCreature> Owned => _owned;

        /// <summary>Party instance ids (≤ 6, each owned, no duplicates).</summary>
        /// <remarks>Pre: none. Post: as stated. Side effects: none.</remarks>
        public IReadOnlyList<int> Party => _party;

        /// <summary>Persisted sub-partner assignments, including pairs invalidated by evolution.</summary>
        /// <remarks>Pre: none. Post: each sub serves one main and is outside Party. Side effects: none.</remarks>
        public IReadOnlyList<PartnerPair> Partners => _partners.AsReadOnly();

        /// <summary>Replaces assignments after the caller validates relations and stages through PartnerLoadout.</summary>
        /// <remarks>
        /// Pre: pairs non-null; mains in Party; subs owned and outside Party; each sub unique; at most four subs per main.
        /// Post: Partners equals pairs in order; invalid input leaves this save unchanged. Side effects: copies pairs.
        /// </remarks>
        public void SetPartners(IReadOnlyList<PartnerPair> pairs)
        {
            Contract.Requires(pairs != null, "pairs are required");
            var copy = new List<PartnerPair>(pairs);
            var seen = new HashSet<int>();
            var counts = new Dictionary<int, int>();
            foreach (PartnerPair pair in copy)
            {
                Contract.Requires(_party.Contains(pair.MainInstanceId), "partner main must be in party");
                Contract.Requires(!_party.Contains(pair.SubInstanceId) && _owned.Exists(o => o.InstanceId == pair.SubInstanceId),
                    "sub must be owned and outside party");
                Contract.Requires(seen.Add(pair.SubInstanceId), "sub must be unique");
                counts.TryGetValue(pair.MainInstanceId, out int count);
                Contract.Requires(count < PartnerLoadout.MaxSubsPerMain, "main has too many subs");
                counts[pair.MainInstanceId] = count + 1;
            }
            _partners.Clear();
            _partners.AddRange(copy);
        }

        /// <summary>The favourite loadout in <paramref name="slot"/>, or null when the slot is empty.</summary>
        /// <remarks>Pre: 0 &lt;= slot &lt; MaxPresets. Post: immutable preset or null. Side effects: none.</remarks>
        public LoadoutPreset GetPreset(int slot)
        {
            Contract.Requires(slot >= 0 && slot < MaxPresets, "preset slot out of range");
            return _presets[slot];
        }

        /// <summary>Stores the current party, subs, equipment and appearances in <paramref name="slot"/>, replacing its content.</summary>
        /// <remarks>
        /// Pre: 0 &lt;= slot &lt; MaxPresets; name non-blank, at most LoadoutPreset.MaxNameLength; Party non-empty.
        /// Post: GetPreset(slot) captures Party, Partners, the equipment of the player, every main and every sub, and their
        ///       VisualVessel; nothing else in this save changes. Side effects: mutates this save.
        /// </remarks>
        public void SavePreset(int slot, string name)
        {
            Contract.Requires(slot >= 0 && slot < MaxPresets, "preset slot out of range");
            Contract.Requires(_party.Count >= 1, "no party to store");
            var covered = new HashSet<int> { 0 };
            covered.UnionWith(_party);
            foreach (PartnerPair p in _partners) covered.Add(p.SubInstanceId);

            var equipment = new List<EquippedItem>();
            foreach (EquippedItem e in _equipment.Snapshot()) if (covered.Contains(e.Owner)) equipment.Add(e);
            var visuals = new List<VisualChoice>();
            foreach (int id in covered) if (id != 0) visuals.Add(new VisualChoice(id, Get(id).VisualVessel));
            _presets[slot] = new LoadoutPreset(name, _party, _partners, equipment, visuals);
        }

        /// <summary>Renames the preset in <paramref name="slot"/> without touching its content.</summary>
        /// <remarks>
        /// Pre: 0 &lt;= slot &lt; MaxPresets; slot non-empty; name non-blank, at most LoadoutPreset.MaxNameLength.
        /// Post: GetPreset(slot).Name == name; party, partners, equipment and visuals unchanged. Side effects: mutates this save.
        /// </remarks>
        public void RenamePreset(int slot, string name)
        {
            Contract.Requires(slot >= 0 && slot < MaxPresets, "preset slot out of range");
            LoadoutPreset p = _presets[slot];
            Contract.Requires(p != null, "preset slot is empty");
            _presets[slot] = new LoadoutPreset(name, p.Party, p.Partners, p.Equipment, p.Visuals);
        }

        /// <summary>Empties a preset slot.</summary>
        /// <remarks>Pre: 0 &lt;= slot &lt; MaxPresets. Post: GetPreset(slot) == null. Side effects: mutates this save.</remarks>
        public void ClearPreset(int slot)
        {
            Contract.Requires(slot >= 0 && slot < MaxPresets, "preset slot out of range");
            _presets[slot] = null;
        }

        /// <summary>Switches to the favourite loadout in <paramref name="slot"/>, all-or-nothing.</summary>
        /// <remarks>
        /// REQ: crafting.md#穿戴與數值 grip rules still apply — each entry goes through EquipmentLoadout.TryEquip.
        /// Pre: 0 &lt;= slot &lt; MaxPresets; slot non-empty; catalog non-null.
        /// Post: Applied false leaves this save untouched (no preset creature left in the roster, or a covered wearer's
        ///       gear could not return to a full stack). Applied true: Party/Partners/VisualVessel follow the preset for creatures
        ///       still owned; every covered wearer (player, preset mains and subs) is stripped to stock and re-equipped with
        ///       what stock still holds. Entries that no longer resolve are counted in the result, never silently dropped
        ///       from the stored preset. Creatures outside the preset keep their gear.
        /// Side effects: mutates this save on success (party, partners, owned, equipment, craft stock).
        /// </remarks>
        public PresetApplyResult TryApplyPreset(int slot, CraftCatalog catalog)
        {
            Contract.Requires(slot >= 0 && slot < MaxPresets, "preset slot out of range");
            Contract.Requires(catalog != null, "catalog is required");
            LoadoutPreset preset = _presets[slot];
            Contract.Requires(preset != null, "preset slot is empty");

            // Work on a copy: equipment moves items through the bag, and a mid-way refusal must not leak.
            LobbySave c = Clone();
            int missingCreatures = 0, missingItems = 0;

            var party = new List<int>();
            foreach (int id in preset.Party)
            {
                if (c._owned.Exists(o => o.InstanceId == id)) party.Add(id); else missingCreatures++;
            }

            if (party.Count == 0) return new PresetApplyResult(false, missingCreatures, 0);

            c._partners.Clear(); // SetParty refuses to promote a current sub
            c.SetParty(party);
            var pairs = new List<PartnerPair>();
            var subs = new HashSet<int>();
            var perMain = new Dictionary<int, int>();
            foreach (PartnerPair pair in preset.Partners)
            {
                perMain.TryGetValue(pair.MainInstanceId, out int n);
                bool fits = party.Contains(pair.MainInstanceId) && !party.Contains(pair.SubInstanceId) &&
                    c._owned.Exists(o => o.InstanceId == pair.SubInstanceId) && n < PartnerLoadout.MaxSubsPerMain &&
                    subs.Add(pair.SubInstanceId);
                if (!fits) { missingCreatures++; continue; }
                perMain[pair.MainInstanceId] = n + 1;
                pairs.Add(pair);
            }

            c.SetPartners(pairs);

            foreach (VisualChoice v in preset.Visuals)
            {
                int i = c._owned.FindIndex(o => o.InstanceId == v.InstanceId);
                if (i < 0) { missingCreatures++; continue; }
                OwnedCreature o = c._owned[i];
                c._owned[i] = new OwnedCreature(o.InstanceId, o.SpeciesId, o.Vessel, o.Exp, v.Vessel, o.NicheId, o.Habit);
            }

            var covered = new HashSet<int> { 0 };
            covered.UnionWith(party);
            covered.UnionWith(subs);
            foreach (EquippedItem e in c._equipment.Snapshot())
            {
                if (covered.Contains(e.Owner) && !c._equipment.TryUnequip(e.Owner, e.Slot, c._craftStock))
                    return new PresetApplyResult(false, missingCreatures, 0);
            }

            foreach (EquippedItem e in preset.Equipment)
            {
                if (e.Owner != 0 && !covered.Contains(e.Owner)) continue; // creature already counted as missing
                IReadOnlyCollection<GripMode> grips = e.Owner == 0
                    ? EquipmentRules.PlayerGrips : catalog.GripsOf(c.Get(e.Owner).SpeciesId);
                // The preset remembers which copy was worn; fall back to the newest copy if that one is gone.
                int copy = c._craftStock.Count(e.Item) > 0 ? c._craftStock.FindCopy(e.Item, e.Look) : -1;
                if (!c._equipment.TryEquip(e.Owner, e.Item, e.Slot, grips, catalog, c._craftStock, copy)) missingItems++;
            }

            _craftStock.CopyFrom(c._craftStock);
            _owned.Clear(); _owned.AddRange(c._owned);
            _party.Clear(); _party.AddRange(c._party);
            _partners.Clear(); _partners.AddRange(c._partners);
            _equipment.Restore(c._equipment.Snapshot());
            return new PresetApplyResult(true, missingCreatures, missingItems);
        }

        /// <summary>Eggs incubating in the hatchery, with their progress.</summary>
        /// <remarks>Pre: none. Post: at most HatchConfig.DefaultCapacity entries. Side effects: none.</remarks>
        public IReadOnlyList<IncubatingEgg> Eggs => _eggs.AsReadOnly();

        /// <summary>Replaces the incubating eggs (call with <c>EggHatchery.Eggs</c> after run events or hatching).</summary>
        /// <remarks>
        /// Pre: eggs non-null; count &lt;= HatchConfig.DefaultCapacity.
        /// Post: Eggs equals copies of the input in order, so later hatchery mutation never leaks into the save;
        ///       invalid input leaves this save unchanged. Side effects: allocates copies.
        /// </remarks>
        public void SetEggs(IReadOnlyList<IncubatingEgg> eggs)
        {
            Contract.Requires(eggs != null, "eggs are required");
            Contract.Requires(eggs.Count <= HatchConfig.DefaultCapacity, "too many eggs for the hatchery");
            var copy = new List<IncubatingEgg>(eggs.Count);
            foreach (IncubatingEgg e in eggs) copy.Add(new IncubatingEgg(e.EggId, e.Vessel, e.ProgressPpm));
            _eggs.Clear();
            _eggs.AddRange(copy);
        }

        /// <summary>Lobby-wide evolution material stock (Q-C32: shared by every creature, not per individual).</summary>
        /// <remarks>
        /// Pre: none. Post: non-null; the same instance for the save's lifetime, so <c>EvolutionRules.TryEvolve</c> can
        ///      consume from it directly. Side effects: none (mutating the returned inventory mutates this save).
        /// </remarks>
        public MaterialInventory Materials => _materials;

        /// <summary>Finds the first material key that banking <paramref name="stacks"/> would push past int.MaxValue.</summary>
        /// <remarks>
        /// Pre: stacks non-null.
        /// Post: true iff AddMaterials(stacks) would overflow; then <paramref name="overflow"/> is the lowest such key in
        ///       Snapshot order (element, then tier), so the UI names the same key every retry. false sets it to default.
        ///       <see cref="ArgumentException"/> in every build for a stack outside the save domain (see AddMaterials).
        /// Side effects: allocates.
        /// </remarks>
        public bool WouldOverflow(IReadOnlyList<MaterialStack> stacks, out MaterialKey overflow)
        {
            Contract.Requires(stacks != null, "stacks must not be null");
            var keys = new List<MaterialKey>();
            Dictionary<MaterialKey, long> totals = SumInDomain(stacks);
            foreach (KeyValuePair<MaterialKey, long> t in totals)
            {
                if (_materials.Count(t.Key) + t.Value > int.MaxValue)
                {
                    keys.Add(t.Key);
                }
            }

            overflow = default;
            if (keys.Count == 0)
            {
                return false;
            }

            keys.Sort((a, b) => a.Element != b.Element ? ((int)a.Element).CompareTo((int)b.Element) : a.Tier.CompareTo(b.Tier));
            overflow = keys[0];
            return true;
        }

        /// <summary>Banks the materials a run brought back, all or nothing.</summary>
        /// <remarks>
        /// Pre: stacks non-null.
        /// Post: each key's count grew by the sum of its stacks. <see cref="InvalidOperationException"/> in every build,
        ///       with the stock unchanged, when any key would exceed int.MaxValue (summed in long first, so a partial
        ///       deposit never happens). <see cref="ArgumentException"/> in every build, stock unchanged, for a stack
        ///       outside the save domain — a tier outside 1..MaterialDrops.MaxTier, a qty below 1 or an undefined
        ///       element (default(MaterialStack) included) — because ToJson could not write it back (SOL W-04).
        /// Side effects: mutates this save.
        /// </remarks>
        public void AddMaterials(IReadOnlyList<MaterialStack> stacks)
        {
            Contract.Requires(stacks != null, "stacks must not be null");
            Dictionary<MaterialKey, long> totals = SumInDomain(stacks);
            foreach (KeyValuePair<MaterialKey, long> t in totals)
            {
                if (_materials.Count(t.Key) + t.Value > int.MaxValue)
                {
                    throw new InvalidOperationException("lobby save: material stock for " + t.Key + " would overflow");
                }
            }

            foreach (KeyValuePair<MaterialKey, long> t in totals)
            {
                _materials.Add(t.Key, (int)t.Value);
            }
        }

        private static Dictionary<MaterialKey, long> SumInDomain(IReadOnlyList<MaterialStack> stacks)
        {
            var totals = new Dictionary<MaterialKey, long>();
            foreach (MaterialStack st in stacks)
            {
                if (!InSaveDomain(st.Key) || st.Qty < 1)
                {
                    throw new ArgumentException("lobby save: material " + st.Key + " x" + st.Qty + " is outside the save domain");
                }

                totals.TryGetValue(st.Key, out long sum);
                totals[st.Key] = sum + st.Qty;
            }

            return totals;
        }

        // The domain FromJson accepts; ToJson must never produce anything outside it (round-trip Post).
        private static bool InSaveDomain(MaterialKey key) =>
            Enum.IsDefined(typeof(Element), key.Element) && key.Tier >= 1 && key.Tier <= MaterialDrops.MaxTier;

        /// <summary>An independent deep copy, used as the candidate of an all-or-nothing lobby change.</summary>
        /// <remarks>
        /// Pre: none.
        /// Post: the copy's ToJson equals this one's and LoadedFormatVersion matches; mutating either never affects the
        ///       other (OwnedCreature is an immutable struct, the lists and stock are copied).
        /// Side effects: allocates.
        /// </remarks>
        public LobbySave Clone()
        {
            var c = new LobbySave
            {
                StarterLine = StarterLine, _nextInstanceId = _nextInstanceId, LoadedFormatVersion = LoadedFormatVersion,
                PlayerExp = PlayerExp, LastPlanet = LastPlanet,
                CharacterId = CharacterId, FriendId = FriendId, RivalId = RivalId,
                RivalStarterId = RivalStarterId, FriendStarterId = FriendStarterId, TutorialDone = TutorialDone,
            };
            c._owned.AddRange(_owned);
            c._party.AddRange(_party);
            c._partners.AddRange(_partners);
            foreach (IncubatingEgg e in _eggs) c._eggs.Add(new IncubatingEgg(e.EggId, e.Vessel, e.ProgressPpm));
            foreach (MaterialStack m in _materials.Snapshot())
            {
                c._materials.Add(m.Key, m.Qty);
            }
            c._craftStock.CopyFrom(_craftStock);
            c._equipment.Restore(_equipment.Snapshot());
            c.Codex.Restore(Codex.Snapshot());
            foreach (KeyValuePair<string, int> kv in _trainerBattles)
            {
                c._trainerBattles[kv.Key] = kv.Value;
            }
            foreach (string trial in _completedTrials)
            {
                c._completedTrials.Add(trial);
            }
            Array.Copy(_presets, c._presets, MaxPresets); // LoadoutPreset is immutable

            return c;
        }

        /// <summary>Records the starter choice and adds the starter.</summary>
        /// <remarks>
        /// Pre: StarterLine == null; lineId and speciesId non-empty; exp &gt;= 0 (T(starter level), LV10).
        /// Post: StarterLine == lineId; the starter is owned with that exp (vessel magic_cube is irrelevant for a main
        ///       creature, lobby-meta.md: "作為主力 無差別"; talisman_card chosen as default) and in the party.
        /// Side effects: mutates this save.
        /// </remarks>
        public OwnedCreature ChooseStarter(string lineId, string speciesId, int exp)
            => ChooseStarter(lineId, speciesId, exp, null, Habit.Tolerant);

        /// <summary>Records the starter choice and adds the starter with its drawn ecology.</summary>
        /// <remarks>
        /// Pre: StarterLine == null; lineId and speciesId non-empty; exp &gt;= 0; habit is a defined Habit.
        /// Post: StarterLine == lineId; the starter is owned with that exp and ecology, and is in the party.
        /// Side effects: mutates this save.
        /// </remarks>
        public OwnedCreature ChooseStarter(string lineId, string speciesId, int exp, string nicheId, Habit habit)
        {
            Contract.Requires(StarterLine == null, "starter already chosen");
            Contract.Requires(!string.IsNullOrEmpty(lineId), "lineId must be non-empty");
            StarterLine = lineId;
            return Add(speciesId, VesselKind.TalismanCard, exp, nicheId, habit);
        }

        /// <summary>Adds a captured creature; it joins the party if there is room.</summary>
        /// <remarks>
        /// Pre: speciesId non-empty; vessel defined; exp &gt;= 0 (a capture keeps its encounter level: T(L)).
        /// Post: one more owned creature with a fresh InstanceId and that exp; in Party iff Party had &lt; 6 before.
        ///       <see cref="InvalidOperationException"/> in every build, with the save unchanged, once instance ids are
        ///       exhausted (the next id would be int.MaxValue and the cursor could not advance past it).
        /// Side effects: mutates this save.
        /// </remarks>
        public OwnedCreature Add(string speciesId, VesselKind vessel, int exp)
            => Add(speciesId, vessel, exp, null, Habit.Tolerant);

        /// <summary>Adds a captured creature with its individual ecology; it joins the party if there is room.</summary>
        /// <remarks>
        /// Pre: speciesId non-empty; vessel defined; exp &gt;= 0; habit is a defined Habit.
        /// Post: one more owned creature with a fresh InstanceId, that exp, and that niche/habit; in Party iff Party had &lt; 6 before.
        ///       <see cref="InvalidOperationException"/> in every build, with the save unchanged, once instance ids are
        ///       exhausted.
        /// Side effects: mutates this save.
        /// </remarks>
        public OwnedCreature Add(string speciesId, VesselKind vessel, int exp, string nicheId, Habit habit)
        {
            // Checked before any mutation: incrementing first would overflow the cursor and write a save FromJson rejects
            // (SOL P2-12 R4).
            if (_nextInstanceId >= int.MaxValue)
            {
                throw new InvalidOperationException("lobby save: creature instance ids are exhausted");
            }

            var c = new OwnedCreature(_nextInstanceId, speciesId, vessel, exp, vessel, nicheId, habit);
            _nextInstanceId++;
            _owned.Add(c);
            Codex.Record(CodexEvent.Capture, speciesId);
            if (_party.Count < MaxParty)
            {
                _party.Add(c.InstanceId);
            }

            return c;
        }

        /// <summary>Replaces the party.</summary>
        /// <remarks>
        /// Pre: 1..6 distinct owned instance ids; none currently assigned as a sub (unassign before promotion).
        /// Post: Party equals instanceIds in order; assignments of removed mains are released. Side effects: mutates this save.
        /// </remarks>
        public void SetParty(IReadOnlyList<int> instanceIds)
        {
            Contract.Requires(instanceIds != null && instanceIds.Count >= 1 && instanceIds.Count <= MaxParty,
                "party must hold 1..6 creatures");
            // Copy first: callers may pass Party itself, and clearing _party would empty the input (SOL D0 H1).
            var copy = new List<int>(instanceIds);
            var seen = new HashSet<int>();
            foreach (int id in copy)
            {
                Contract.Requires(seen.Add(id), "party ids must be distinct");
                Contract.Requires(_owned.Exists(o => o.InstanceId == id), "party ids must be owned");
                Contract.Requires(!_partners.Exists(p => p.SubInstanceId == id), "unassign sub before making it a main");
            }

            _party.Clear();
            _party.AddRange(copy);
            _partners.RemoveAll(p => !_party.Contains(p.MainInstanceId));
        }

        /// <summary>Replaces an owned creature's cumulative XP (run rewards are applied in the lobby, Q-M4).</summary>
        /// <remarks>
        /// Pre: instanceId owned; exp &gt;= 0. Post: Get(instanceId).Exp == exp; species, vessel, visual vessel and order unchanged.
        /// Side effects: mutates this save.
        /// </remarks>
        public void SetExp(int instanceId, int exp)
        {
            int i = _owned.FindIndex(o => o.InstanceId == instanceId);
            Contract.Requires(i >= 0, "instance id is not owned");
            OwnedCreature o = _owned[i];
            _owned[i] = new OwnedCreature(o.InstanceId, o.SpeciesId, o.Vessel, exp, o.VisualVessel, o.NicheId, o.Habit);
        }

        /// <summary>Replaces an owned creature's form after a successful lobby evolution.</summary>
        /// <remarks>
        /// Pre: instanceId owned; speciesId non-empty (the caller resolved it from the evolution requirement).
        /// Post: Get(instanceId).SpeciesId == speciesId; id, vessel, visual vessel, exp, ecology, party membership and order unchanged.
        /// Side effects: mutates this save.
        /// </remarks>
        public void SetSpecies(int instanceId, string speciesId)
        {
            Contract.Requires(!string.IsNullOrEmpty(speciesId), "speciesId must be non-empty");
            int i = _owned.FindIndex(o => o.InstanceId == instanceId);
            Contract.Requires(i >= 0, "instance id is not owned");
            OwnedCreature o = _owned[i];
            _owned[i] = o.WithSpecies(speciesId);
            Codex.Record(CodexEvent.Capture, speciesId);
        }

        /// <summary>Replaces an owned creature's form and redrawn ecology after a successful lobby evolution.</summary>
        /// <remarks>
        /// Pre: instanceId owned; speciesId and nicheId non-empty; habit is a defined Habit.
        /// Post: Get(instanceId) has the new SpeciesId, NicheId and Habit; id, vessel, visual vessel, exp, party membership and order unchanged.
        /// Side effects: mutates this save.
        /// </remarks>
        public void SetSpecies(int instanceId, string speciesId, string nicheId, Habit habit)
        {
            Contract.Requires(!string.IsNullOrEmpty(speciesId), "speciesId must be non-empty");
            Contract.Requires(!string.IsNullOrEmpty(nicheId), "nicheId must be non-empty");
            int i = _owned.FindIndex(o => o.InstanceId == instanceId);
            Contract.Requires(i >= 0, "instance id is not owned");
            _owned[i] = _owned[i].WithSpecies(speciesId, nicheId, habit);
            Codex.Record(CodexEvent.Capture, speciesId);
        }

        /// <summary>Assigns an owned creature's ecology (e.g. one-time legacy save migration).</summary>
        /// <remarks>
        /// Pre: instanceId owned; nicheId non-empty; habit is a defined Habit.
        /// Post: Get(instanceId).NicheId == nicheId and Habit == habit; all other fields unchanged.
        /// Side effects: mutates this save.
        /// </remarks>
        public void SetEcology(int instanceId, string nicheId, Habit habit)
        {
            Contract.Requires(!string.IsNullOrEmpty(nicheId), "nicheId must be non-empty");
            int i = _owned.FindIndex(o => o.InstanceId == instanceId);
            Contract.Requires(i >= 0, "instance id is not owned");
            _owned[i] = _owned[i].WithEcology(nicheId, habit);
        }

        /// <summary>Replaces an owned creature's functional capture-item category without changing its appearance (Q-C13).</summary>
        /// <remarks>
        /// Pre: instanceId owned; vessel is a defined VesselKind.
        /// Post: Get(instanceId).Vessel == vessel; id, species, exp, VisualVessel, party membership and order unchanged.
        /// Side effects: mutates this save.
        /// </remarks>
        public void SetVessel(int instanceId, VesselKind vessel)
        {
            Contract.Requires(Enum.IsDefined(typeof(VesselKind), vessel), "vessel must be a defined VesselKind");
            int i = _owned.FindIndex(o => o.InstanceId == instanceId);
            Contract.Requires(i >= 0, "instance id is not owned");
            _owned[i] = _owned[i].WithVessel(vessel);
        }

        /// <summary>Changes only an owned creature's appearance; its functional vessel is untouched.</summary>
        /// <remarks>
        /// Pre: instanceId owned; vessel is a defined VesselKind.
        /// Post: Get(instanceId).VisualVessel == vessel; every other field and party membership unchanged. Side effects: mutates this save.
        /// </remarks>
        public void SetVisualVessel(int instanceId, VesselKind vessel)
        {
            Contract.Requires(Enum.IsDefined(typeof(VesselKind), vessel), "vessel must be a defined VesselKind");
            int i = _owned.FindIndex(o => o.InstanceId == instanceId);
            Contract.Requires(i >= 0, "instance id is not owned");
            OwnedCreature o = _owned[i];
            _owned[i] = new OwnedCreature(o.InstanceId, o.SpeciesId, o.Vessel, o.Exp, vessel, o.NicheId, o.Habit);
        }

        /// <summary>Owned creature by instance id.</summary>
        /// <remarks>Pre: id owned. Post: InstanceId == id. Side effects: none.</remarks>
        public OwnedCreature Get(int instanceId)
        {
            int i = _owned.FindIndex(o => o.InstanceId == instanceId);
            Contract.Requires(i >= 0, "instance id is not owned");
            return _owned[i];
        }

        /// <summary>Serializes to JSON.</summary>
        /// <remarks>
        /// Pre: none. Post: FromJson(ToJson()) reproduces this save. <see cref="InvalidOperationException"/> in every
        ///      build, with nothing produced, when the stock holds a key outside the save domain (reachable only by
        ///      calling Materials.Add directly with a tier above MaterialDrops.MaxTier — SOL W-04): writing it would make
        ///      a file this class refuses to load. Side effects: allocates.
        /// </remarks>
        public string ToJson()
        {
            var dto = new Dto
            {
                version = FormatVersion,
                next_instance_id = _nextInstanceId,
                starter_line = StarterLine ?? string.Empty,
                party = _party.ToArray(),
                owned = new CreatureDto[_owned.Count],
                player_exp = PlayerExp,
                partners = new PartnerDto[_partners.Count],
                eggs = new EggDto[_eggs.Count],
                character_id = CharacterId ?? string.Empty,
                friend_id = FriendId ?? string.Empty,
                rival_id = RivalId ?? string.Empty,
                rival_starter_id = RivalStarterId ?? string.Empty,
                friend_starter_id = FriendStarterId ?? string.Empty,
                tutorial_done = TutorialDone,
            };
            IReadOnlyList<TrainerBattleCount> trainerCounts = TrainerBattlesSnapshot();
            dto.trainer_battles = new TrainerCountDto[trainerCounts.Count];
            for (int i = 0; i < trainerCounts.Count; i++)
            {
                dto.trainer_battles[i] = new TrainerCountDto { id = trainerCounts[i].ProfessionId, count = trainerCounts[i].Count };
            }

            IReadOnlyList<string> completedTrials = CompletedTrialsSnapshot();
            dto.completed_trials = new string[completedTrials.Count];
            for (int i = 0; i < completedTrials.Count; i++)
            {
                dto.completed_trials[i] = completedTrials[i];
            }
            var presetDtos = new List<PresetDto>();
            for (int slot = 0; slot < MaxPresets; slot++)
            {
                LoadoutPreset p = _presets[slot];
                if (p == null) continue;
                var pd = new PresetDto
                {
                    slot = slot, name = p.Name, party = new List<int>(p.Party).ToArray(),
                    partners = new PartnerDto[p.Partners.Count], equipment = new EquipmentDto[p.Equipment.Count],
                    visuals = new VisualDto[p.Visuals.Count],
                };
                for (int i = 0; i < pd.partners.Length; i++)
                    pd.partners[i] = new PartnerDto { main = p.Partners[i].MainInstanceId, sub = p.Partners[i].SubInstanceId };
                for (int i = 0; i < pd.equipment.Length; i++)
                    pd.equipment[i] = new EquipmentDto { owner = p.Equipment[i].Owner, slot = p.Equipment[i].Slot, item = p.Equipment[i].Item, look = ToDto(p.Equipment[i].Look) };
                for (int i = 0; i < pd.visuals.Length; i++)
                    pd.visuals[i] = new VisualDto { instance_id = p.Visuals[i].InstanceId, vessel = (int)p.Visuals[i].Vessel };
                presetDtos.Add(pd);
            }

            dto.presets = presetDtos.ToArray();
            IReadOnlyList<CodexRecord> knowledge = Codex.Snapshot();
            dto.codex = new CodexDto[knowledge.Count];
            for (int i = 0; i < knowledge.Count; i++)
                dto.codex[i] = new CodexDto { kind = (int)knowledge[i].Kind, id = knowledge[i].Id, level = (int)knowledge[i].Level };
            IReadOnlyList<CraftStack> craftStock = _craftStock.Snapshot();
            var tagged = new Dictionary<string, IReadOnlyList<ItemLook>>(StringComparer.Ordinal);
            foreach (ItemLookList list in _craftStock.LookSnapshot()) tagged[list.Id] = list.Looks;
            dto.craft_stock = new CraftDto[craftStock.Count];
            for (int i = 0; i < craftStock.Count; i++)
            {
                var cd = new CraftDto { id = craftStock[i].Id, qty = craftStock[i].Quantity, looks = Array.Empty<LookDto>() };
                if (tagged.TryGetValue(cd.id, out IReadOnlyList<ItemLook> looks))
                {
                    cd.looks = new LookDto[looks.Count];
                    for (int k = 0; k < looks.Count; k++) cd.looks[k] = ToDto(looks[k]);
                }
                dto.craft_stock[i] = cd;
            }
            dto.last_planet = new OriginDto { planet = LastPlanet.PlanetTypeId, seed = unchecked((long)LastPlanet.PlanetSeed) };
            IReadOnlyList<EquippedItem> equipment = _equipment.Snapshot();
            dto.equipment = new EquipmentDto[equipment.Count];
            for (int i = 0; i < equipment.Count; i++)
            {
                EquippedItem item = equipment[i];
                if (item.Owner != 0 && !_owned.Exists(o => o.InstanceId == item.Owner))
                    throw new InvalidOperationException("Equipment owner is not owned");
                dto.equipment[i] = new EquipmentDto { owner = item.Owner, slot = item.Slot, item = item.Item, look = ToDto(item.Look) };
            }
            for (int i = 0; i < _eggs.Count; i++)
            {
                dto.eggs[i] = new EggDto { egg_id = _eggs[i].EggId, vessel = _eggs[i].Vessel, progress_ppm = _eggs[i].ProgressPpm };
            }
            for (int i = 0; i < _partners.Count; i++)
            {
                dto.partners[i] = new PartnerDto { main = _partners[i].MainInstanceId, sub = _partners[i].SubInstanceId };
            }
            IReadOnlyList<MaterialStack> stock = _materials.Snapshot();
            dto.materials = new MaterialDto[stock.Count];
            for (int i = 0; i < stock.Count; i++)
            {
                if (!InSaveDomain(stock[i].Key))
                {
                    throw new InvalidOperationException("lobby save: material " + stock[i].Key + " is outside the save domain");
                }

                dto.materials[i] = new MaterialDto
                {
                    element = ElementId(stock[i].Key.Element), tier = stock[i].Key.Tier, qty = stock[i].Qty,
                };
            }

            for (int i = 0; i < _owned.Count; i++)
            {
                OwnedCreature o = _owned[i];
                dto.owned[i] = new CreatureDto
                {
                    instance_id = o.InstanceId, species_id = o.SpeciesId, vessel = (int)o.Vessel,
                    visual_vessel = (int)o.VisualVessel, exp = o.Exp,
                    niche_id = o.NicheId ?? string.Empty, habit = (int)o.Habit,
                };
            }

            return JsonUtility.ToJson(dto, true);
        }

        /// <summary>Parses a save, migrating formats 1–4 to the current format.</summary>
        /// <remarks>
        /// Pre: json non-empty; progression non-null; stageOf non-null (returns null for an unknown species).
        /// Post: the save it encodes with every Exp in 0..progression.MaxExp (higher values are clipped, never earned);
        ///       a format-1 creature gets progression.MigrationExp(stage of its species) and LoadedFormatVersion == 1.
        ///       <see cref="InvalidDataException"/> in every build (Contract checks are stripped from Release, and a
        ///       Release that loaded and re-wrote a newer save would silently drop its fields — SOL D0 H2) for: not a
        ///       save, a newer FormatVersion, duplicate/non-positive instance ids, next id below 1 or not above every owned
        ///       id, an undefined vessel, a negative exp, a party entry that is not owned or listed twice, a party over 6,
        ///       a party empty while creatures are owned (or non-empty without any), or a format-1 species stageOf
        ///       does not know (it cannot be migrated without guessing), and — format 3 only — a material entry with an
        ///       unknown element id, a tier outside 1..MaterialDrops.MaxTier, a qty below 1, or an (element, tier) listed
        ///       twice, and — format 4 only — a negative player_exp. Formats 1–2 load with an empty material stock (their
        ///       materials field is ignored); formats 1–3 load with PlayerExp 0. PlayerExp above
        ///       progression.PlayerMaxExp is clipped.
        ///       Format 5 refuses unowned/non-party mains, unowned/party subs, duplicate subs, and overfull assignments.
        /// Side effects: allocates.
        /// </remarks>
        public static LobbySave FromJson(string json, Progression progression, Func<string, CreatureStage?> stageOf)
        {
            Contract.Requires(progression != null && stageOf != null, "progression and stageOf are required");
            Check(!string.IsNullOrEmpty(json), "save json is empty");
            Dto dto = JsonUtility.FromJson<Dto>(json);
            Check(dto != null && dto.version >= 1, "not a lobby save");
            Check(dto.version <= FormatVersion, "save is from a newer game version");
            var save = new LobbySave
            {
                StarterLine = string.IsNullOrEmpty(dto.starter_line) ? null : dto.starter_line,
                _nextInstanceId = dto.next_instance_id,
                LoadedFormatVersion = dto.version,
            };
            // Independent of owned: with an empty roster the per-creature check below never runs (SOL P2-12 R4).
            Check(dto.next_instance_id >= 1, "next_instance_id must be >= 1");
            var ids = new HashSet<int>();
            foreach (CreatureDto c in dto.owned ?? new CreatureDto[0])
            {
                Check(c.instance_id >= 1 && ids.Add(c.instance_id), "duplicate or non-positive instance id");
                Check(c.instance_id < dto.next_instance_id, "next_instance_id must exceed every owned id");
                Check(Enum.IsDefined(typeof(VesselKind), c.vessel) && !string.IsNullOrEmpty(c.species_id),
                    "creature entry has an undefined vessel or empty species");
                Check(c.exp >= 0, "creature entry has a negative exp");
                int exp = c.exp;
                if (dto.version == 1)
                {
                    CreatureStage? stage = stageOf(c.species_id);
                    Check(stage.HasValue, "format-1 creature '" + c.species_id + "' is unknown and cannot be migrated");
                    exp = progression.MigrationExp(stage.Value);
                }

                VesselKind vessel = (VesselKind)c.vessel;
                VesselKind visualVessel = vessel;
                if (dto.version >= 8)
                {
                    Check(Enum.IsDefined(typeof(VesselKind), c.visual_vessel),
                        "creature entry has an undefined visual_vessel");
                    visualVessel = (VesselKind)c.visual_vessel;
                }

                string nicheId = string.Empty;
                Habit habit = Habit.Tolerant;
                if (dto.version >= 11)
                {
                    Check(Enum.IsDefined(typeof(Habit), c.habit), "creature entry has an undefined habit");
                    nicheId = c.niche_id ?? string.Empty;
                    habit = (Habit)c.habit;
                }

                save._owned.Add(new OwnedCreature(c.instance_id, c.species_id, vessel,
                    Math.Min(exp, progression.MaxExp), visualVessel, nicheId, habit));
            }

            // Party is the XP settlement roster, so a duplicate would be paid twice (SOL P2-12 R3). An owned roster always
            // has a party: ChooseStarter/Add put the first creature in it and SetParty never empties it.
            int[] party = dto.party ?? new int[0];
            Check(party.Length <= MaxParty, "party holds more than 6 creatures");
            Check(party.Length > 0 == ids.Count > 0, "party must be non-empty exactly when creatures are owned");
            var inParty = new HashSet<int>();
            foreach (int p in party)
            {
                Check(ids.Contains(p), "party entry is not owned");
                Check(inParty.Add(p), "party lists a creature twice");
            }

            save._party.AddRange(party);
            if (dto.version >= 6)
            {
                Check((dto.eggs?.Length ?? 0) <= HatchConfig.DefaultCapacity, "more eggs than hatchery slots");
                foreach (EggDto egg in dto.eggs ?? Array.Empty<EggDto>())
                {
                    Check(egg != null && !string.IsNullOrEmpty(egg.egg_id), "egg entry has no id");
                    Check(Enum.IsDefined(typeof(VesselKind), egg.vessel), "egg entry has an unknown vessel");
                    Check(egg.progress_ppm >= 0 && egg.progress_ppm <= HatchConfig.FullProgressPpm, "egg progress out of range");
                    save._eggs.Add(new IncubatingEgg(egg.egg_id, egg.vessel, egg.progress_ppm));
                }
            }
            if (dto.version >= 5)
            {
                var subs = new HashSet<int>();
                var counts = new Dictionary<int, int>();
                foreach (PartnerDto pair in dto.partners ?? Array.Empty<PartnerDto>())
                {
                    Check(pair != null && inParty.Contains(pair.main), "partner main must be in party");
                    Check(ids.Contains(pair.sub) && !inParty.Contains(pair.sub), "sub must be owned and outside party");
                    Check(subs.Add(pair.sub), "sub assigned twice");
                    counts.TryGetValue(pair.main, out int count);
                    Check(count < PartnerLoadout.MaxSubsPerMain, "main has too many subs");
                    counts[pair.main] = count + 1;
                    save._partners.Add(new PartnerPair(pair.main, pair.sub));
                }
            }
            if (dto.version >= 4)
            {
                Check(dto.player_exp >= 0, "player_exp must be >= 0");
                save.PlayerExp = Math.Min(dto.player_exp, progression.PlayerMaxExp);
            }

            if (dto.version >= 3)
            {
                var keys = new HashSet<MaterialKey>();
                foreach (MaterialDto m in dto.materials ?? new MaterialDto[0])
                {
                    Element element = default;
                    Check(m != null && TryParseElement(m.element, out element), "material entry has an unknown element");
                    Check(m.tier >= 1 && m.tier <= MaterialDrops.MaxTier, "material entry has a tier outside 1..3");
                    Check(m.qty >= 1, "material entry has a qty below 1");
                    var key = new MaterialKey(element, m.tier);
                    Check(keys.Add(key), "material entry lists an element and tier twice");
                    save._materials.Add(key, m.qty);
                }
            }

            if (dto.version >= 14 && dto.last_planet != null && !string.IsNullOrEmpty(dto.last_planet.planet))
            {
                Check(CraftInventory.ValidId(dto.last_planet.planet), "invalid last planet");
                save.LastPlanet = new GearOrigin(dto.last_planet.planet, unchecked((ulong)dto.last_planet.seed));
            }

            if (dto.version >= 7)
            {
                var craftIds = new HashSet<string>(StringComparer.Ordinal);
                var stacks = new List<CraftStack>();
                var lookLists = new List<ItemLookList>();
                foreach (CraftDto item in dto.craft_stock ?? Array.Empty<CraftDto>())
                {
                    Check(item != null && CraftInventory.ValidId(item.id), "invalid crafting item id");
                    Check(item.qty > 0 && craftIds.Add(item.id), "invalid or duplicate crafting stack");
                    stacks.Add(new CraftStack(item.id, item.qty));
                    if (dto.version < 14 || item.looks == null || item.looks.Length == 0) continue;
                    Check(item.looks.Length <= item.qty, "crafting stack has more looks than copies");
                    var looks = new List<ItemLook>(item.looks.Length);
                    foreach (LookDto l in item.looks)
                    {
                        Check(TryFromDto(l, out ItemLook look), "invalid item look");
                        looks.Add(look);
                    }
                    lookLists.Add(new ItemLookList(item.id, looks));
                }
                Check(save._craftStock.TryExchange(Array.Empty<CraftStack>(), stacks), "crafting inventory overflow");
                save._craftStock.RestoreLooks(lookLists);
                var entries = new List<EquippedItem>();
                var slots = new HashSet<string>(StringComparer.Ordinal);
                foreach (EquipmentDto item in dto.equipment ?? Array.Empty<EquipmentDto>())
                {
                    Check(item != null && item.owner >= 0 &&
                        (item.owner == 0 || save._owned.Exists(o => o.InstanceId == item.owner)), "unknown equipment owner");
                    string slot = dto.version >= 9 ? item.slot : EquipmentSlots.MigrateLegacy(item.slot);
                    Check(EquipmentSlots.IsValid(slot), "invalid equipment slot");
                    Check(CraftInventory.ValidId(item.item) && slots.Add(item.owner + ":" + slot), "invalid or duplicate equipment entry");
                    entries.Add(EntryFromDto(dto.version, item.owner, slot, item.item, item.look));
                }
                save._equipment.Restore(entries);
            }
            if (dto.version >= 10)
            {
                Check(dto.codex != null, "missing codex records");
                var knowledge = new List<CodexRecord>();
                var knowledgeKeys = new HashSet<string>(StringComparer.Ordinal);
                foreach (CodexDto item in dto.codex)
                {
                    Check(item != null && Enum.IsDefined(typeof(CodexKind), item.kind) && !string.IsNullOrWhiteSpace(item.id)
                        && (item.level == (int)CodexLevel.Basic || item.level == (int)CodexLevel.Complete), "invalid codex record");
                    Check(knowledgeKeys.Add(item.kind + ":" + item.id), "duplicate codex record");
                    knowledge.Add(new CodexRecord((CodexKind)item.kind, item.id, (CodexLevel)item.level));
                }
                save.Codex.Restore(knowledge);
            }
            if (dto.version >= 12)
            {
                foreach (PresetDto pd in dto.presets ?? Array.Empty<PresetDto>())
                {
                    Check(pd != null && pd.slot >= 0 && pd.slot < MaxPresets && save._presets[pd.slot] == null, "invalid or duplicate preset slot");
                    Check(!string.IsNullOrWhiteSpace(pd.name) && pd.name.Length <= LoadoutPreset.MaxNameLength, "invalid preset name");
                    int[] pparty = pd.party ?? new int[0];
                    Check(pparty.Length >= 1 && pparty.Length <= MaxParty, "preset party must hold 1..6");
                    var mains = new HashSet<int>();
                    foreach (int id in pparty) Check(id >= 1 && mains.Add(id), "preset party ids must be distinct and >= 1");
                    var pairs = new List<PartnerPair>();
                    var psubs = new HashSet<int>();
                    var perMain = new Dictionary<int, int>();
                    foreach (PartnerDto pair in pd.partners ?? Array.Empty<PartnerDto>())
                    {
                        Check(pair != null && mains.Contains(pair.main) && !mains.Contains(pair.sub) && pair.sub >= 1 && psubs.Add(pair.sub),
                            "invalid preset partner");
                        perMain.TryGetValue(pair.main, out int n);
                        Check(n < PartnerLoadout.MaxSubsPerMain, "preset main has too many subs");
                        perMain[pair.main] = n + 1;
                        pairs.Add(new PartnerPair(pair.main, pair.sub));
                    }

                    var gear = new List<EquippedItem>();
                    var gearKeys = new HashSet<string>(StringComparer.Ordinal);
                    foreach (EquipmentDto e in pd.equipment ?? Array.Empty<EquipmentDto>())
                    {
                        Check(e != null && (e.owner == 0 || mains.Contains(e.owner) || psubs.Contains(e.owner)), "preset equipment owner outside preset");
                        Check(EquipmentSlots.IsValid(e.slot) && CraftInventory.ValidId(e.item) && gearKeys.Add(e.owner + ":" + e.slot),
                            "invalid or duplicate preset equipment");
                        gear.Add(EntryFromDto(dto.version, e.owner, e.slot, e.item, e.look));
                    }

                    var looks = new List<VisualChoice>();
                    var lookIds = new HashSet<int>();
                    foreach (VisualDto v in pd.visuals ?? Array.Empty<VisualDto>())
                    {
                        Check(v != null && v.instance_id >= 1 && lookIds.Add(v.instance_id) && Enum.IsDefined(typeof(VesselKind), v.vessel),
                            "invalid preset visual");
                        looks.Add(new VisualChoice(v.instance_id, (VesselKind)v.vessel));
                    }

                    save._presets[pd.slot] = new LoadoutPreset(pd.name, pparty, pairs, gear, looks);
                }
            }
            if (dto.version >= 13)
            {
                save.CharacterId = dto.character_id ?? string.Empty;
                save.FriendId = dto.friend_id ?? string.Empty;
                save.RivalId = dto.rival_id ?? string.Empty;
                save.RivalStarterId = dto.rival_starter_id ?? string.Empty;
                save.FriendStarterId = dto.friend_starter_id ?? string.Empty;
                save.TutorialDone = dto.tutorial_done;
            }
            else
            {
                save.TutorialDone = save._owned.Count > 0 || !string.IsNullOrEmpty(save.StarterLine);
            }

            if (dto.version >= 15)
            {
                var seenProf = new HashSet<string>(StringComparer.Ordinal);
                foreach (TrainerCountDto t in dto.trainer_battles ?? Array.Empty<TrainerCountDto>())
                {
                    Check(t != null && CraftInventory.ValidId(t.id) && t.count >= 1 && seenProf.Add(t.id),
                        "invalid trainer battle entry");
                    save.AddTrainerBattles(t.id, t.count);
                }

                var seenTrials = new HashSet<string>(StringComparer.Ordinal);
                foreach (string trialId in dto.completed_trials ?? Array.Empty<string>())
                {
                    Check(CraftInventory.ValidId(trialId) && seenTrials.Add(trialId), "invalid completed trial entry");
                    save.RecordTrialCompleted(trialId);
                }
            }

            foreach (OwnedCreature creature in save.Owned) save.Codex.Record(CodexEvent.Capture, creature.SpeciesId);
            return save;
        }

        // Lower-case enum name ("light"): readable in the file and stable across enum reordering. Enum.Parse is not used
        // because it also accepts numeric strings ("8"), which would make a hand-edited save silently remap elements.
        private static string ElementId(Element e) => e.ToString().ToLowerInvariant();

        private static bool TryParseElement(string id, out Element element)
        {
            foreach (Element e in (Element[])Enum.GetValues(typeof(Element)))
            {
                if (ElementId(e) == id)
                {
                    element = e;
                    return true;
                }
            }

            element = default;
            return false;
        }

        private static void Check(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidDataException("lobby save: " + message);
            }
        }

        /// <summary>Loads from <paramref name="path"/>, or returns a new empty save when the file does not exist.</summary>
        /// <remarks>
        /// Pre: path non-empty; see FromJson. Post: see FromJson; when the file was an older format, the original bytes
        ///      exist at <paramref name="path"/> + ".v{N}.bak" (an existing backup is never overwritten).
        /// Side effects: reads the file; may write the backup.
        /// </remarks>
        public static LobbySave Load(string path, Progression progression, Func<string, CreatureStage?> stageOf)
        {
            Contract.Requires(!string.IsNullOrEmpty(path), "path must not be empty");
            if (!File.Exists(path))
            {
                return new LobbySave();
            }

            LobbySave save = FromJson(File.ReadAllText(path), progression, stageOf);
            // Migration is lossy by design (old evolution XP is dropped), so keep the original before Write replaces it.
            string backup = path + ".v" + save.LoadedFormatVersion + ".bak";
            if (save.LoadedFormatVersion < FormatVersion && !File.Exists(backup))
            {
                File.Copy(path, backup);
            }

            return save;
        }

        /// <summary>Sets cumulative player XP.</summary>
        /// <remarks>
        /// Pre: progression non-null; 0 &lt;= exp &lt;= progression.PlayerMaxExp (the same domain FromJson keeps, so
        ///      Write/Load and Clone agree — SOL W-B2-SOL-04). Post: PlayerExp == exp. Side effects: mutates this save.
        /// </remarks>
        public void SetPlayerExp(int exp, Progression progression)
        {
            Contract.Requires(progression != null, "progression is required");
            Contract.Requires(exp >= 0 && exp <= progression.PlayerMaxExp, "player exp must be in [0, PlayerMaxExp]");
            PlayerExp = exp;
        }

        /// <summary>Writes atomically to <paramref name="path"/> (temp file + replace, so a crash never leaves half a save).</summary>
        /// <remarks>Pre: path non-empty; directory writable. Post: Load(path) equals this. Side effects: writes files.</remarks>
        public void Write(string path)
        {
            Contract.Requires(!string.IsNullOrEmpty(path), "path must not be empty");
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string tmp = path + ".tmp";
            File.WriteAllText(tmp, ToJson());
            if (File.Exists(path))
            {
                File.Replace(tmp, path, null);
            }
            else
            {
                File.Move(tmp, path);
            }
        }

        private static LookDto ToDto(ItemLook look) => new LookDto
        {
            planet = look.Origin.PlanetTypeId, planet_seed = unchecked((long)look.Origin.PlanetSeed), seed = unchecked((long)look.Seed),
        };

        // JsonUtility fills a missing nested object with an empty instance, so "no look" is an empty planet id.
        private static bool TryFromDto(LookDto dto, out ItemLook look)
        {
            look = default;
            if (dto == null || !CraftInventory.ValidId(dto.planet)) return false;
            look = new ItemLook(new GearOrigin(dto.planet, unchecked((ulong)dto.planet_seed)), unchecked((ulong)dto.seed));
            return true;
        }

        private static EquippedItem EntryFromDto(int version, int owner, string slot, string item, LookDto look)
        {
            if (version >= 14 && look != null && !string.IsNullOrEmpty(look.planet))
            {
                Check(TryFromDto(look, out ItemLook parsed), "invalid equipment look");
                return new EquippedItem(owner, slot, item, parsed);
            }
            return new EquippedItem(owner, slot, item);
        }

#pragma warning disable 0649, IDE1006
        [Serializable] private sealed class Dto
        {
            public int version;
            public int next_instance_id;
            public string starter_line;
            public int[] party;
            public CreatureDto[] owned;
            public MaterialDto[] materials;
            public int player_exp;
            public PartnerDto[] partners;
            public EggDto[] eggs;
            public CraftDto[] craft_stock;
            public EquipmentDto[] equipment;
            public CodexDto[] codex;
            public PresetDto[] presets;
            public OriginDto last_planet;
            public string character_id;
            public string friend_id;
            public string rival_id;
            public string rival_starter_id;
            public string friend_starter_id;
            public bool tutorial_done;
            public TrainerCountDto[] trainer_battles;
            public string[] completed_trials;
        }

        [Serializable] private sealed class TrainerCountDto
        {
            public string id;
            public int count;
        }

        [Serializable] private sealed class PresetDto
        {
            public int slot;
            public string name;
            public int[] party;
            public PartnerDto[] partners;
            public EquipmentDto[] equipment;
            public VisualDto[] visuals;
        }

        [Serializable] private sealed class VisualDto
        {
            public int instance_id;
            public int vessel;
        }

        [Serializable] private sealed class CodexDto
        {
            public int kind;
            public string id;
            public int level;
        }

        [Serializable] private sealed class EquipmentDto
        {
            public int owner;
            public string slot;
            public string item;
            public LookDto look;
        }

        [Serializable] private sealed class CraftDto
        {
            public string id;
            public int qty;
            public LookDto[] looks;
        }

        // Seeds are stored as their 64-bit pattern in a long: JsonUtility round-trips long exactly.
        [Serializable] private sealed class LookDto
        {
            public string planet;
            public long planet_seed;
            public long seed;
        }

        [Serializable] private sealed class OriginDto
        {
            public string planet;
            public long seed;
        }

        [Serializable] private sealed class EggDto
        {
            public string egg_id;
            public int vessel;
            public long progress_ppm;
        }

        [Serializable] private sealed class PartnerDto
        {
            public int main;
            public int sub;
        }

        [Serializable] private sealed class MaterialDto
        {
            public string element;
            public int tier;
            public int qty;
        }

        [Serializable] private sealed class CreatureDto
        {
            public int instance_id;
            public string species_id;
            public int vessel;
            public int visual_vessel;
            public int exp;
            public string niche_id;
            public int habit;
        }
#pragma warning restore 0649, IDE1006
    }
}
