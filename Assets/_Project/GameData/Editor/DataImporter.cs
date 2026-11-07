using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Cosmimera.Core.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Cosmimera.GameData.Editor
{
    /// <summary>Counts of assets touched by one import.</summary>
    /// <remarks>Pre: none. Post: all counts >= 0. Side effects: none.</remarks>
    public readonly struct DataImportReport
    {
        /// <summary>Creates a report.</summary>
        /// <remarks>Pre: counts >= 0. Post: fields set. Side effects: none.</remarks>
        public DataImportReport(int created, int updated, int deleted)
        {
            Created = created;
            Updated = updated;
            Deleted = deleted;
        }

        /// <summary>Definition assets created.</summary>
        public int Created { get; }

        /// <summary>Existing definition assets overwritten in place (GUID kept).</summary>
        public int Updated { get; }

        /// <summary>Definition assets deleted because their id left the source data.</summary>
        public int Deleted { get; }

        /// <inheritdoc />
        public override string ToString() => $"created {Created}, updated {Updated}, deleted {Deleted}";
    }

    /// <summary>Imports Data/**/*.json into definition assets plus one <see cref="GameDatabase"/>. REQ: P0-06, ADR-0004.</summary>
    /// <remarks>
    /// Two phases: parse + resolve everything, then write. Any error aborts before the first write so a bad
    /// batch never leaves a half-updated database (assets referencing ids that no longer exist).
    /// Existing assets are overwritten in place rather than recreated so their GUIDs, and therefore every
    /// scene/prefab reference to them, survive a re-import.
    /// </remarks>
    public static class DataImporter
    {
        /// <summary>Where generated assets live (overview.md: Assets/_Project/Data, never hand-edited).</summary>
        public const string DefaultOutputFolder = "Assets/_Project/Data";

        /// <summary>File name of the registry asset inside the output folder.</summary>
        public const string DatabaseFileName = "GameDatabase.asset";

        // Source folder (tools/validate-data KINDS) → asset subfolder.
        private const string MovesSrc = "moves";
        private const int MaxDurationMs = 600000; // common.schema.json#/$defs/durationMs
        private const string TraitsSrc = "traits";
        private const string PlanetTypesSrc = "planet-types";
        private const string CreaturesSrc = "creatures";
        private const string RelationsSrc = "relations";
        private const string MovesOut = "Moves";
        private const string TraitsOut = "Traits";
        private const string PlanetTypesOut = "PlanetTypes";
        private const string CreaturesOut = "Creatures";

        private static readonly Regex SnakeCase = new Regex("^[a-z][a-z0-9]*(_[a-z0-9]+)*$");

        private static readonly JsonLoadSettings LoadSettings = new JsonLoadSettings
        {
            DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
        };

        private static readonly JsonSerializer Serializer = JsonSerializer.Create(new JsonSerializerSettings
        {
            MissingMemberHandling = MissingMemberHandling.Error,
            DateParseHandling = DateParseHandling.None,
        });

        /// <summary>The repo-root Data/ folder.</summary>
        /// <remarks>Pre: running in the Editor. Post: absolute path (may not exist). Side effects: none.</remarks>
        public static string DefaultSourceRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Data"));

        [MenuItem("Cosmimera/Data/Import All")]
        private static void ImportFromMenu()
        {
            try
            {
                DataImportReport report = Import(DefaultSourceRoot, DefaultOutputFolder);
                Debug.Log("Data import: " + report);
            }
            catch (DataImportException e)
            {
                Debug.LogError(e.Message);
            }
        }

        /// <summary>Batchmode entry: <c>-executeMethod Cosmimera.GameData.Editor.DataImporter.ImportAllBatch [-dataRoot DIR]</c>.</summary>
        /// <remarks>
        /// Pre: Unity started with -batchmode. Post: exits the Editor with 0 on success, 1 on any error.
        /// Side effects: same as <see cref="Import"/>; terminates the process.
        /// </remarks>
        public static void ImportAllBatch()
        {
            string[] args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-dataRoot");
            string root = i >= 0 && i + 1 < args.Length ? Path.GetFullPath(args[i + 1]) : DefaultSourceRoot;
            try
            {
                DataImportReport report = Import(root, DefaultOutputFolder);
                Debug.Log($"Data import from {root}: {report}");
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError(e);
                EditorApplication.Exit(1);
            }
        }

        /// <summary>Imports every definition under <paramref name="sourceRoot"/> into <paramref name="outputFolder"/>.</summary>
        /// <remarks>
        /// Pre: sourceRoot is an existing directory laid out like Data/ (creatures/ moves/ traits/ relations/
        ///      planet-types/; missing folders mean "none"); the data passed tools/validate-data (this importer
        ///      re-checks only what it needs to build references, not every schema rule);
        ///      outputFolder is an "Assets/..." project path; not called during asset editing batches.
        /// Post: outputFolder holds one asset per id and GameDatabase.asset listing them; assets whose id left the
        ///       source are deleted; existing assets keep their GUID.
        /// Side effects: creates/overwrites/deletes assets under outputFolder and saves the AssetDatabase.
        ///       Throws <see cref="DataImportException"/> with every problem found, before writing anything.
        /// </remarks>
        public static DataImportReport Import(string sourceRoot, string outputFolder)
        {
            Contract.Requires(sourceRoot != null && Directory.Exists(sourceRoot), "sourceRoot must be an existing directory");
            Contract.Requires(outputFolder != null && outputFolder.StartsWith("Assets/", StringComparison.Ordinal),
                "outputFolder must be a project path under Assets/");

            var errors = new List<string>();
            List<Sourced<MoveDto>> moves = ReadKind<MoveDto>(sourceRoot, MovesSrc, errors);
            List<Sourced<TraitDto>> traits = ReadKind<TraitDto>(sourceRoot, TraitsSrc, errors);
            List<Sourced<PlanetTypeDto>> planets = ReadKind<PlanetTypeDto>(sourceRoot, PlanetTypesSrc, errors);
            List<Sourced<CreatureDto>> creatures = ReadKind<CreatureDto>(sourceRoot, CreaturesSrc, errors);
            List<Sourced<EdgeDto>> edges = ReadKind<EdgeDto>(sourceRoot, RelationsSrc, errors);
            if (errors.Count == 0)
            {
                Resolve(moves, traits, planets, creatures, edges, errors);
            }

            if (errors.Count > 0)
            {
                throw new DataImportException(errors);
            }

            return Write(outputFolder, moves, traits, planets, creatures, edges);
        }

        private readonly struct Sourced<T>
        {
            public Sourced(string where, T dto)
            {
                Where = where;
                Dto = dto;
            }

            public string Where { get; }
            public T Dto { get; }
        }

        private static List<Sourced<T>> ReadKind<T>(string sourceRoot, string folder, List<string> errors)
        {
            var result = new List<Sourced<T>>();
            string dir = Path.Combine(sourceRoot, folder);
            if (!Directory.Exists(dir))
            {
                return result;
            }

            // Ordinal sort: import order (and therefore GameDatabase order) must not depend on OS locale.
            string[] files = Directory.GetFiles(dir, "*.json", SearchOption.TopDirectoryOnly);
            Array.Sort(files, StringComparer.Ordinal);
            foreach (string file in files)
            {
                string rel = folder + "/" + Path.GetFileName(file);
                JToken root;
                try
                {
                    // DateParseHandling.None: the default would turn date-like strings (e.g. in a note) into DateTime
                    // and re-format them, silently changing the text.
                    using (var reader = new JsonTextReader(new StringReader(File.ReadAllText(file))) { DateParseHandling = DateParseHandling.None })
                    {
                        root = JToken.ReadFrom(reader, LoadSettings);
                    }
                }
                catch (JsonException e)
                {
                    errors.Add($"{rel}: {e.Message}");
                    continue;
                }

                if (!(root is JArray array))
                {
                    errors.Add($"{rel}: top level must be an array");
                    continue;
                }

                // Float literals would be rounded into int fields by the converter; data-content.md bans them.
                JToken flt = array.Descendants().FirstOrDefault(t => t.Type == JTokenType.Float);
                if (flt != null)
                {
                    errors.Add($"{rel}: float literal at {flt.Path}; numbers must be integers");
                    continue;
                }

                for (int i = 0; i < array.Count; i++)
                {
                    try
                    {
                        result.Add(new Sourced<T>($"{rel}#{i}", array[i].ToObject<T>(Serializer)));
                    }
                    catch (JsonException e)
                    {
                        errors.Add($"{rel}#{i}: {e.Message}");
                    }
                }
            }

            return result;
        }

        private static void Resolve(List<Sourced<MoveDto>> moves, List<Sourced<TraitDto>> traits,
            List<Sourced<PlanetTypeDto>> planets, List<Sourced<CreatureDto>> creatures, List<Sourced<EdgeDto>> edges,
            List<string> errors)
        {
            var kindById = new Dictionary<string, string>(StringComparer.Ordinal);

            void Claim(string where, string id, string kind)
            {
                if (id == null || !SnakeCase.IsMatch(id))
                {
                    errors.Add($"{where}: id '{id}' is not snake_case");
                }
                else if (kindById.ContainsKey(id))
                {
                    errors.Add($"{where}: duplicate id '{id}' (ids are global)");
                }
                else
                {
                    kindById.Add(id, kind);
                }
            }

            void RequireRef(string where, string field, string target, string kind)
            {
                if (target == null || !kindById.TryGetValue(target, out string actual) || actual != kind)
                {
                    errors.Add($"{where}: {field} '{target}' is not a known {kind} id");
                }
            }

            void RequireEnum<TEnum>(string where, string field, string value) where TEnum : struct, Enum
            {
                if (!TryParseEnum(value, out TEnum _))
                {
                    errors.Add($"{where}: {field} '{value}' is not a valid {typeof(TEnum).Name}");
                }
            }

            foreach (Sourced<MoveDto> m in moves)
            {
                Claim(m.Where, m.Dto.Id, "move");
                RequireEnum<Element>(m.Where, "element", m.Dto.Element);
                RequireEnum<MoveKind>(m.Where, "kind", m.Dto.Kind);
                RequireEnum<RangeClass>(m.Where, "range_class", m.Dto.RangeClass);
                RequireEnum<MoveShape>(m.Where, "shape", m.Dto.Shape);
                RequireEnum<MoveMovement>(m.Where, "movement", m.Dto.Movement ?? "none");
                if (m.Dto.Shape == "burst")
                {
                    RequireEnum<BurstOrigin>(m.Where, "origin", m.Dto.Origin);
                    if (m.Dto.Origin == "target" && m.Dto.RangeClass == "melee")
                    {
                        errors.Add($"{m.Where}: a target-point burst must be mid or long range (combat.md 目標點爆發)");
                    }
                }
                else if (m.Dto.Origin != null)
                {
                    errors.Add($"{m.Where}: origin is only allowed for burst moves");
                }

                if (m.Dto.BaseRangeMm < 0 || (m.Dto.Movement == "dash"
                    && (m.Dto.Shape != "line" || m.Dto.BaseRangeMm == 0 || m.Dto.BaseRangeMm > MoveDef.MaxDashRangeMm)))
                {
                    errors.Add($"{m.Where}: base_range_mm must be nonnegative; dash requires line shape and range 1..{MoveDef.MaxDashRangeMm}");
                }
                if ((m.Dto.Kind == "damage") != (m.Dto.Effect == null))
                {
                    errors.Add($"{m.Where}: effect must be present iff kind is not 'damage'");
                }

                if (m.Dto.Effect != null)
                {
                    CheckRule(m.Where, "effect", m.Dto.Effect, EffectParams, errors);
                }

                // REQ: docs/design/combat.md#招式效果 — heal is instant, every other effect is a timed window.
                bool needsDuration = m.Dto.Effect != null && m.Dto.Effect.Type != "heal_percent_max_hp";
                if (needsDuration != m.Dto.EffectDurationMs.HasValue)
                {
                    errors.Add($"{m.Where}: effect_duration_ms is required iff the move has a non-heal effect");
                }
                else if (needsDuration && (m.Dto.EffectDurationMs < 1 || m.Dto.EffectDurationMs > MaxDurationMs))
                {
                    errors.Add($"{m.Where}: effect_duration_ms must be 1..{MaxDurationMs}");
                }

                // REQ: docs/design/combat.md#場地 (Q-C17) — a field's lifetime is its own field, not effect_duration_ms.
                bool isField = m.Dto.Shape == "field";
                if (isField != m.Dto.FieldDurationMs.HasValue)
                {
                    errors.Add($"{m.Where}: field_duration_ms is required iff shape is 'field'");
                }
                else if (isField && (m.Dto.FieldDurationMs < 1 || m.Dto.FieldDurationMs > MaxDurationMs))
                {
                    errors.Add($"{m.Where}: field_duration_ms must be 1..{MaxDurationMs}");
                }
            }

            foreach (Sourced<TraitDto> t in traits)
            {
                Claim(t.Where, t.Dto.Id, "trait");
                CheckRule(t.Where, "trigger", t.Dto.Trigger, TriggerParams, errors);
                if (t.Dto.Effects == null || t.Dto.Effects.Count == 0 || t.Dto.Effects.Count > TraitDef.MaxEffects)
                {
                    errors.Add($"{t.Where}: effects must have 1..{TraitDef.MaxEffects} entries");
                    continue;
                }

                // REQ: docs/design/combat.md#特性觸發 — a heal is instant; a continuous trigger has no moment to fire it
                // (SOL P2-09 E4: the schema accepted it and EffectBook then rejected the imported trait).
                bool continuous = t.Dto.Trigger != null
                    && (t.Dto.Trigger.Type == "always" || t.Dto.Trigger.Type == "on_hp_below" || t.Dto.Trigger.Type == "on_hp_above");
                for (int i = 0; i < t.Dto.Effects.Count; i++)
                {
                    CheckRule(t.Where, $"effects[{i}]", t.Dto.Effects[i], EffectParams, errors);
                    if (continuous && t.Dto.Effects[i]?.Type == "heal_percent_max_hp")
                    {
                        errors.Add($"{t.Where}: effects[{i}] heal_percent_max_hp needs an event trigger, not '{t.Dto.Trigger.Type}'");
                    }
                }
            }

            foreach (Sourced<PlanetTypeDto> p in planets)
            {
                Claim(p.Where, p.Dto.Id, "planet-type");
                RequireEnum<PlanetSize>(p.Where, "size", p.Dto.Size);
                foreach (string e in p.Dto.ElementBias)
                {
                    RequireEnum<Element>(p.Where, "element_bias", e);
                }
            }

            foreach (Sourced<CreatureDto> c in creatures)
            {
                Claim(c.Where, c.Dto.Id, "creature");
            }

            foreach (Sourced<CreatureDto> c in creatures)
            {
                CreatureDto d = c.Dto;
                RequireEnum<CreatureStage>(c.Where, "stage", d.Stage);
                if (!TryParseSizeClass(d.SizeClass, out _))
                {
                    errors.Add($"{c.Where}: size_class '{d.SizeClass}' must be S, M, L or XL");
                }
                RequireRef(c.Where, "signature_move", d.SignatureMove, "move");
                RequireRef(c.Where, "trait", d.Trait, "trait");
                if ((d.EvolvesTo == null) != (d.Evolution == null))
                {
                    errors.Add($"{c.Where}: evolves_to and evolution must both be null or both be set");
                }

                if (d.EvolvesTo != null)
                {
                    RequireRef(c.Where, "evolves_to", d.EvolvesTo, "creature");
                    foreach (MaterialDto mat in d.Evolution?.Materials ?? new List<MaterialDto>())
                    {
                        RequireEnum<Element>(c.Where, "evolution.materials.element", mat.Element);
                    }
                }

                foreach (string key in d.Resist.Keys.Concat(d.Mastery.Keys))
                {
                    RequireEnum<Element>(c.Where, "resist/mastery key", key);
                }

                // A missing resist key would silently read as 0 (creature.schema.json requires all ten).
                foreach (string name in Enum.GetNames(typeof(Element)))
                {
                    if (!d.Resist.ContainsKey(ToSnake(name)))
                    {
                        errors.Add($"{c.Where}: resist is missing '{ToSnake(name)}'");
                    }
                }
            }

            foreach (Sourced<EdgeDto> e in edges)
            {
                RequireEnum<RelationKind>(e.Where, "kind", e.Dto.Kind);
                RequireRef(e.Where, "a", e.Dto.A, "creature");
                RequireRef(e.Where, "b", e.Dto.B, "creature");
                if (string.CompareOrdinal(e.Dto.A, e.Dto.B) >= 0)
                {
                    errors.Add($"{e.Where}: edge must satisfy a < b (data-content.md)");
                }
            }
        }

        private static DataImportReport Write(string outputFolder, List<Sourced<MoveDto>> moves,
            List<Sourced<TraitDto>> traits, List<Sourced<PlanetTypeDto>> planets, List<Sourced<CreatureDto>> creatures,
            List<Sourced<EdgeDto>> edges)
        {
            int created = 0;
            int updated = 0;
            int deleted = 0;

            EnsureFolder(outputFolder);
            deleted += DeleteOrphans(outputFolder + "/" + MovesOut, moves.Select(m => m.Dto.Id));
            deleted += DeleteOrphans(outputFolder + "/" + TraitsOut, traits.Select(t => t.Dto.Id));
            deleted += DeleteOrphans(outputFolder + "/" + PlanetTypesOut, planets.Select(p => p.Dto.Id));
            deleted += DeleteOrphans(outputFolder + "/" + CreaturesOut, creatures.Select(c => c.Dto.Id));

            // Create every asset before filling any, so cross references (creature → move) point at persisted objects.
            var moveDefs = new Dictionary<string, MoveDef>(StringComparer.Ordinal);
            var traitDefs = new Dictionary<string, TraitDef>(StringComparer.Ordinal);
            var planetDefs = new Dictionary<string, PlanetTypeDef>(StringComparer.Ordinal);
            var creatureDefs = new Dictionary<string, CreatureDef>(StringComparer.Ordinal);
            foreach (Sourced<MoveDto> m in moves)
            {
                moveDefs.Add(m.Dto.Id, GetOrCreate<MoveDef>(outputFolder + "/" + MovesOut, m.Dto.Id, ref created, ref updated));
            }

            foreach (Sourced<TraitDto> t in traits)
            {
                traitDefs.Add(t.Dto.Id, GetOrCreate<TraitDef>(outputFolder + "/" + TraitsOut, t.Dto.Id, ref created, ref updated));
            }

            foreach (Sourced<PlanetTypeDto> p in planets)
            {
                planetDefs.Add(p.Dto.Id, GetOrCreate<PlanetTypeDef>(outputFolder + "/" + PlanetTypesOut, p.Dto.Id, ref created, ref updated));
            }

            foreach (Sourced<CreatureDto> c in creatures)
            {
                creatureDefs.Add(c.Dto.Id, GetOrCreate<CreatureDef>(outputFolder + "/" + CreaturesOut, c.Dto.Id, ref created, ref updated));
            }

            foreach (Sourced<MoveDto> s in moves)
            {
                MoveDto d = s.Dto;
                moveDefs[d.Id].Set(ParseEnum<Element>(d.Element), ParseEnum<MoveKind>(d.Kind), d.Power, ToEffect(d.Effect),
                    d.EffectDurationMs ?? 0, d.FieldDurationMs ?? 0, d.CooldownMs, d.Accuracy, ParseEnum<RangeClass>(d.RangeClass), ParseEnum<MoveShape>(d.Shape),
                    d.BaseRangeMm, ParseEnum<MoveMovement>(d.Movement ?? "none"),
                    d.Shape == "burst" ? ParseEnum<BurstOrigin>(d.Origin) : default);
            }

            foreach (Sourced<TraitDto> s in traits)
            {
                traitDefs[s.Dto.Id].Set(ToTrigger(s.Dto.Trigger), s.Dto.Effects.Select(ToEffect).ToList());
            }

            foreach (Sourced<PlanetTypeDto> s in planets)
            {
                PlanetTypeDto d = s.Dto;
                planetDefs[d.Id].Set(ParseEnum<PlanetSize>(d.Size), new List<string>(d.TerrainTags),
                    d.ElementBias.Select(ParseEnum<Element>).ToList());
            }

            foreach (Sourced<CreatureDto> s in creatures)
            {
                CreatureDto d = s.Dto;
                var resist = new int[Enum.GetValues(typeof(Element)).Length];
                foreach (KeyValuePair<string, int> kv in d.Resist)
                {
                    resist[(int)ParseEnum<Element>(kv.Key)] = kv.Value;
                }

                // Sorted by element so asset YAML does not churn when JSON key order changes.
                List<ElementValue> mastery = d.Mastery
                    .Select(kv => new ElementValue(ParseEnum<Element>(kv.Key), kv.Value))
                    .OrderBy(ev => ev.Element)
                    .ToList();
                List<EvolutionMaterial> materials = d.Evolution?.Materials
                    .Select(m => new EvolutionMaterial(ParseEnum<Element>(m.Element), m.Tier, m.Qty))
                    .ToList() ?? new List<EvolutionMaterial>();
                string sensitivity = (string)d.Concept["sensitivity"];

                TryParseSizeClass(d.SizeClass, out CreatureSizeClass sizeClass);
                creatureDefs[d.Id].Set(d.LineId, ParseEnum<CreatureStage>(d.Stage), sizeClass,
                    d.EvolvesTo != null ? creatureDefs[d.EvolvesTo] : null, d.Evolution?.Exp ?? 0, materials, resist,
                    mastery, new CreatureStats(d.Stats.Con, d.Stats.Str, d.Stats.Spi, d.Stats.Agi),
                    moveDefs[d.SignatureMove], traitDefs[d.Trait], d.Concept.ToString(Formatting.None),
                    sensitivity == "review");
            }

            var allDefs = new List<GameDef>();
            allDefs.AddRange(moveDefs.Values.OrderBy(x => x.Id, StringComparer.Ordinal));
            allDefs.AddRange(traitDefs.Values.OrderBy(x => x.Id, StringComparer.Ordinal));
            allDefs.AddRange(planetDefs.Values.OrderBy(x => x.Id, StringComparer.Ordinal));
            allDefs.AddRange(creatureDefs.Values.OrderBy(x => x.Id, StringComparer.Ordinal));
            foreach (GameDef def in allDefs)
            {
                EditorUtility.SetDirty(def);
            }

            List<RelationEdge> relations = edges
                .Select(e => new RelationEdge(creatureDefs[e.Dto.A], creatureDefs[e.Dto.B],
                    ParseEnum<RelationKind>(e.Dto.Kind), e.Dto.Note ?? string.Empty))
                .ToList();

            string dbPath = outputFolder + "/" + DatabaseFileName;
            GameDatabase db = AssetDatabase.LoadAssetAtPath<GameDatabase>(dbPath);
            if (db == null)
            {
                db = ScriptableObject.CreateInstance<GameDatabase>();
                AssetDatabase.CreateAsset(db, dbPath);
            }

            db.SetContents(allDefs, relations);
            EditorUtility.SetDirty(db);
            AssetDatabase.SaveAssets();
            return new DataImportReport(created, updated, deleted);
        }

        private static T GetOrCreate<T>(string folder, string id, ref int created, ref int updated) where T : GameDef
        {
            EnsureFolder(folder);
            string path = folder + "/" + id + ".asset";
            T existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null)
            {
                updated++;
                return existing;
            }

            var def = ScriptableObject.CreateInstance<T>();
            def.SetId(id);
            AssetDatabase.CreateAsset(def, path);
            created++;
            return def;
        }

        private static int DeleteOrphans(string folder, IEnumerable<string> keepIds)
        {
            if (!AssetDatabase.IsValidFolder(folder))
            {
                return 0;
            }

            var keep = new HashSet<string>(keepIds, StringComparer.Ordinal);
            int deleted = 0;
            foreach (string file in Directory.GetFiles(folder, "*.asset", SearchOption.TopDirectoryOnly))
            {
                string id = Path.GetFileNameWithoutExtension(file);
                if (!keep.Contains(id) && AssetDatabase.DeleteAsset(folder + "/" + Path.GetFileName(file)))
                {
                    deleted++;
                }
            }

            return deleted;
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
            {
                return;
            }

            int slash = folder.LastIndexOf('/');
            string parent = folder.Substring(0, slash);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, folder.Substring(slash + 1));
        }

        private enum ParamKind
        {
            Percent, DurationMs, Delta, Element, ElementOrAny,
        }

        // Mirrors common.schema.json#/$defs/trigger and #/$defs/effect. Re-checked here because a wrong param
        // would otherwise import as 0 and only surface when P2-09 reads it mid-battle.
        private static readonly Dictionary<string, (string Key, ParamKind Kind)[]> TriggerParams =
            new Dictionary<string, (string Key, ParamKind Kind)[]>(StringComparer.Ordinal)
            {
                ["always"] = new (string Key, ParamKind Kind)[0],
                ["on_hp_below"] = new[] { ("percent", ParamKind.Percent) },
                ["on_hp_above"] = new[] { ("percent", ParamKind.Percent) },
                ["on_summon"] = new[] { ("duration_ms", ParamKind.DurationMs) },
                ["on_hit_taken"] = new[] { ("element", ParamKind.ElementOrAny), ("duration_ms", ParamKind.DurationMs) },
                ["on_move_used"] = new[] { ("element", ParamKind.Element), ("duration_ms", ParamKind.DurationMs) },
                ["on_ally_swap"] = new[] { ("duration_ms", ParamKind.DurationMs) },
                ["on_hp_cross_below"] = new[] { ("percent", ParamKind.Percent), ("duration_ms", ParamKind.DurationMs) },
            };

        private static readonly Dictionary<string, (string Key, ParamKind Kind)[]> EffectParams =
            new Dictionary<string, (string Key, ParamKind Kind)[]>(StringComparer.Ordinal)
            {
                ["mastery_mod"] = new[] { ("element", ParamKind.Element), ("delta", ParamKind.Delta) },
                ["resist_mod"] = new[] { ("element", ParamKind.Element), ("delta", ParamKind.Delta) },
                ["cd_mod_percent"] = new[] { ("element", ParamKind.ElementOrAny), ("delta", ParamKind.Delta) },
                ["evasion_mod_percent"] = new[] { ("delta", ParamKind.Delta) },
                ["move_speed_mod_percent"] = new[] { ("delta", ParamKind.Delta) },
                ["range_mod_percent"] = new[] { ("delta", ParamKind.Delta) },
                ["heal_percent_max_hp"] = new[] { ("percent", ParamKind.Percent) },
            };

        private static void CheckRule(string where, string field, RuleDto dto,
            Dictionary<string, (string Key, ParamKind Kind)[]> vocabulary, List<string> errors)
        {
            if (dto == null || dto.Type == null || !vocabulary.TryGetValue(dto.Type, out (string Key, ParamKind Kind)[] spec))
            {
                errors.Add($"{where}: {field} type '{dto?.Type}' is not in the closed vocabulary");
                return;
            }

            // REQ: docs/design/combat.md#效果疊加 — stacking is an effect property; a heal is instant and never stacks.
            if (dto.MaxStacks.HasValue)
            {
                if (vocabulary != EffectParams || dto.Type == "heal_percent_max_hp")
                {
                    errors.Add($"{where}: {field} '{dto.Type}' does not accept max_stacks");
                }
                else if (dto.MaxStacks < 1 || dto.MaxStacks > EffectDef.MaxStacksLimit)
                {
                    errors.Add($"{where}: {field} max_stacks must be 1..{EffectDef.MaxStacksLimit}");
                }
            }

            JObject ps = dto.Params ?? new JObject();
            foreach (JProperty p in ps.Properties())
            {
                if (Array.FindIndex(spec, s => s.Key == p.Name) < 0)
                {
                    errors.Add($"{where}: {field} '{dto.Type}' has unknown param '{p.Name}'");
                }
            }

            foreach ((string key, ParamKind kind) in spec)
            {
                JToken token = ps[key];
                if (token == null)
                {
                    errors.Add($"{where}: {field} '{dto.Type}' is missing param '{key}'");
                }
                else if (!ParamValid(token, kind))
                {
                    errors.Add($"{where}: {field} '{dto.Type}' param '{key}' = {token.ToString(Formatting.None)} is not a valid {kind}");
                }
            }
        }

        private static bool ParamValid(JToken token, ParamKind kind)
        {
            switch (kind)
            {
                case ParamKind.Percent:
                    return IsIntegerInRange(token, 1, 100);
                case ParamKind.DurationMs:
                    return IsIntegerInRange(token, 1, MaxDurationMs);
                case ParamKind.Delta:
                    return IsIntegerInRange(token, -100, 100);
                case ParamKind.Element:
                    return token.Type == JTokenType.String && TryParseEnum((string)token, out Element _);
                case ParamKind.ElementOrAny:
                    return token.Type == JTokenType.String
                        && ((string)token == "any" || TryParseEnum((string)token, out Element _));
                default:
                    return false;
            }
        }

        private static bool IsIntegerInRange(JToken token, int minimum, int maximum)
        {
            return token.Type == JTokenType.Integer
                && long.TryParse(token.ToString(Formatting.None), NumberStyles.Integer, CultureInfo.InvariantCulture, out long value)
                && value >= minimum && value <= maximum;
        }

        // Pre (all three readers): CheckRule passed for dto, so present params are in range.
        private static void ReadElement(JObject ps, out bool any, out Element element)
        {
            string raw = (string)ps["element"];
            any = raw == "any";
            element = raw == null || any ? default : ParseEnum<Element>(raw);
        }

        private static TriggerDef ToTrigger(RuleDto dto)
        {
            JObject ps = dto.Params ?? new JObject();
            ReadElement(ps, out bool any, out Element element);
            return new TriggerDef(ParseEnum<TriggerType>(dto.Type), any, element,
                (int?)ps["percent"] ?? 0, (int?)ps["duration_ms"] ?? 0);
        }

        private static EffectDef ToEffect(RuleDto dto)
        {
            if (dto == null)
            {
                return null;
            }

            JObject ps = dto.Params ?? new JObject();
            ReadElement(ps, out bool any, out Element element);
            return new EffectDef(ParseEnum<EffectType>(dto.Type), any, element,
                (int?)ps["delta"] ?? 0, (int?)ps["percent"] ?? 0, dto.MaxStacks ?? 1);
        }

        private static T ParseEnum<T>(string snake) where T : struct, Enum
        {
            bool ok = TryParseEnum(snake, out T value);
            Contract.Requires(ok, $"'{snake}' was not checked in Resolve");
            return value;
        }

        private static bool TryParseSizeClass(string value, out CreatureSizeClass sizeClass)
        {
            sizeClass = default;
            return (value == "S" || value == "M" || value == "L" || value == "XL")
                && Enum.TryParse(value, out sizeClass);
        }

        private static bool TryParseEnum<T>(string snake, out T value) where T : struct, Enum
        {
            value = default;
            if (snake == null || !SnakeCase.IsMatch(snake))
            {
                return false;
            }

            // Exact name match instead of Enum.TryParse, which also accepts numbers ("3") and flag lists ("a, b").
            foreach (string name in Enum.GetNames(typeof(T)))
            {
                if (ToSnake(name) == snake)
                {
                    value = (T)Enum.Parse(typeof(T), name);
                    return true;
                }
            }

            return false;
        }

        private static string ToSnake(string pascal)
        {
            return Regex.Replace(pascal, "(?<!^)([A-Z])", "_$1").ToLowerInvariant();
        }
    }
}
