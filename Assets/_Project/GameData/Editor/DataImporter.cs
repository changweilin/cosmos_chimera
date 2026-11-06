using System;
using System.Collections.Generic;
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
                if ((m.Dto.Kind == "damage") != (m.Dto.Effect == null))
                {
                    errors.Add($"{m.Where}: effect must be present iff kind is not 'damage'");
                }
            }

            foreach (Sourced<TraitDto> t in traits)
            {
                Claim(t.Where, t.Dto.Id, "trait");
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
                moveDefs[d.Id].Set(ParseEnum<Element>(d.Element), ParseEnum<MoveKind>(d.Kind), d.Power, ToRule(d.Effect),
                    d.CooldownMs, d.Accuracy, ParseEnum<RangeClass>(d.RangeClass), ParseEnum<MoveShape>(d.Shape));
            }

            foreach (Sourced<TraitDto> s in traits)
            {
                traitDefs[s.Dto.Id].Set(ToRule(s.Dto.Trigger), s.Dto.Effects.Select(ToRule).ToList());
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

                creatureDefs[d.Id].Set(d.LineId, ParseEnum<CreatureStage>(d.Stage),
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

        private static RuleDef ToRule(RuleDto dto)
        {
            if (dto == null)
            {
                return null;
            }

            return new RuleDef(dto.Type, (dto.Params ?? new JObject()).ToString(Formatting.None));
        }

        private static T ParseEnum<T>(string snake) where T : struct, Enum
        {
            bool ok = TryParseEnum(snake, out T value);
            Contract.Requires(ok, $"'{snake}' was not checked in Resolve");
            return value;
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
