using System;
using System.Collections.Generic;
using Cosmimera.Codex;
using Cosmimera.Food;
using Cosmimera.GameData;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Cosmimera.Bootstrap.Demo
{
    internal sealed class CodexPanel
    {
        private const int PageSize = 8;
        private const int RecipeTab = 5;
        private readonly GameDatabase _database;
        private readonly DemoCopy _copy;
        private readonly Func<string, bool> _owned;
        private readonly ConceptPortraitTable _portraits;
        private readonly CodexProgress _progress;
        private readonly EncyclopediaCatalog _catalog = new EncyclopediaCatalog();
        private readonly List<JObject> _entries = new List<JObject>();
        private int _kind;
        private int _page;
        private bool _ownedOnly;
        private string _search = string.Empty;
        private JObject _selected;
        private GUIStyle _nodeStyle;
        private readonly List<JObject> _knownRelations = new List<JObject>();
        private readonly List<string> _hiddenNames = new List<string>();
        private readonly FoodCatalog _foods;
        private readonly Func<string, bool> _recipeKnown;
        private readonly List<RecipeSpec> _recipes = new List<RecipeSpec>();

        internal CodexPanel(GameDatabase database, DemoCopy copy, Func<string, bool> owned,
            ConceptPortraitTable portraits = null, CodexProgress progress = null,
            FoodCatalog foods = null, Func<string, bool> recipeKnown = null)
        {
            _database = database; _copy = copy; _owned = owned; _portraits = portraits;
            _progress = progress ?? new CodexProgress();
            _foods = foods; _recipeKnown = recipeKnown ?? (_ => false);
            if (_foods != null)
            {
                _recipes.AddRange(_foods.Recipes);
                // Catalog enumeration order is not a contract; a fixed order keeps pages stable between openings.
                _recipes.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            }
            Refresh();
        }

        internal CodexLevel Level(JObject entry) => _progress.Get(EncyclopediaCatalog.Kind(entry), EncyclopediaCatalog.Subject(entry));

        internal string Title(JObject entry)
        {
            if (Level(entry) == CodexLevel.Unknown) return _copy.Ui("dex.unknown");
            return (string)entry["basic"]?["name_zh"] ?? (string)entry["basic"]?["name_en"] ?? _copy.Ui("dex.missing");
        }

        private void Refresh()
        {
            _entries.Clear();
            _hiddenNames.Clear();
            var knownNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (JObject entry in _catalog.Entries)
                if (Level(entry) != CodexLevel.Unknown)
                    foreach (string key in new[] { "name_zh", "name_en" })
                    {
                        string name = (string)entry["basic"]?[key];
                        if (!string.IsNullOrEmpty(name)) knownNames.Add(name);
                    }
            foreach (JObject entry in _catalog.Entries)
                if (Level(entry) == CodexLevel.Unknown)
                    foreach (string key in new[] { "name_zh", "name_en" })
                    {
                        string name = (string)entry["basic"]?[key];
                        if (!string.IsNullOrEmpty(name) && !knownNames.Contains(name)) _hiddenNames.Add(name);
                    }
            _hiddenNames.Sort((a, b) => b.Length.CompareTo(a.Length));
            foreach (JObject entry in _catalog.Entries)
            {
                string kind = (string)entry["kind"], skill = (string)entry["skill_type"];
                bool matches = _kind == 4 ? kind == "character" : _kind == 1 ? kind == "skill" && skill == "move"
                    : _kind == 2 ? kind == "skill" && skill != "move" : kind == "creature";
                if (!matches || (_ownedOnly && kind == "creature" && !_owned((string)entry["game_id"]))) continue;
                // Unknown names and hidden prose must not become an oracle through search or raw IDs.
                if (!string.IsNullOrEmpty(_search) && (Level(entry) == CodexLevel.Unknown ||
                    Title(entry).IndexOf(_search, StringComparison.OrdinalIgnoreCase) < 0)) continue;
                _entries.Add(entry);
            }
            _page = 0;
            _selected = _entries.Count > 0 ? _entries[0] : null;
        }

        internal void Draw(AdventurePanel ui)
        {
            GUILayout.BeginHorizontal();
            int tabs = _foods != null ? RecipeTab + 1 : RecipeTab;
            for (int i = 0; i < tabs; i++)
            {
                string label = i == RecipeTab ? "dex.recipes" : i == 4 ? "dex.characters" : "ui.codex.kind." + i;
                if (!ui.Button(_copy.Ui(label))) continue;
                _kind = i; Refresh(); GUIUtility.ExitGUI();
            }
            GUILayout.EndHorizontal();
            if (_kind == RecipeTab) { DrawRecipes(ui); return; }
            ui.Label(_copy.Ui("ui.codex.search"));
            string search = ui.TextField("CodexSearch", _search);
            if (search != _search) { _search = search; Refresh(); GUIUtility.ExitGUI(); }
            if ((_kind == 0 || _kind == 3) && ui.Button(_copy.Ui(_ownedOnly ? "ui.codex.owned_only" : "ui.codex.all")))
            { _ownedOnly = !_ownedOnly; Refresh(); GUIUtility.ExitGUI(); }
            int pages = Math.Max(1, (_entries.Count + PageSize - 1) / PageSize);
            GUILayout.BeginHorizontal();
            if (ui.Button("◀", _page > 0)) { _page--; GUIUtility.ExitGUI(); }
            ui.Label($"{_page + 1}/{pages} · {_entries.Count}");
            if (ui.Button("▶", _page + 1 < pages)) { _page++; GUIUtility.ExitGUI(); }
            GUILayout.EndHorizontal();
            for (int i = _page * PageSize; i < Math.Min(_entries.Count, (_page + 1) * PageSize); i++)
            {
                JObject entry = _entries[i];
                if (ui.Button(Title(entry) + " · " + _copy.Ui("dex.level." + Level(entry))))
                { _selected = entry; GUIUtility.ExitGUI(); }
            }
            if (_selected == null) { ui.Label(_copy.Ui("ui.journal.empty")); return; }
            CodexLevel level = Level(_selected);
            ui.Heading(Title(_selected));
            if (level == CodexLevel.Unknown) { ui.Label(_copy.Ui("dex.hint." + (string)_selected["kind"])); return; }
            if (_selected["authored_fields"] is JArray authored && authored.Count > 0) ui.Label(_copy.Ui("dex.authored"));
            DrawPortrait(_selected);
            foreach (JProperty field in ((JObject)_selected["basic"]).Properties())
                if (field.Name != "name_zh" && field.Name != "name_en") DrawField(ui, field.Name, field.Value);
            if (level != CodexLevel.Complete) { ui.Label(_copy.Ui("dex.complete_hint." + (string)_selected["kind"])); return; }
            foreach (JProperty field in ((JObject)_selected["details"]).Properties())
                if (field.Name != "value_refs") DrawField(ui, field.Name, field.Value);
            foreach (JObject value in (_catalog.Values[(string)_selected["id"]] as JArray) ?? new JArray())
            {
                string pointer = (string)value["pointer"];
                string key = pointer.Substring(pointer.LastIndexOf('/') + 1);
                string label = _copy.Ui("dex.value." + key);
                if (label == "dex.value." + key) label = _copy.Ui("dex.value");
                ui.Label(label + "：" + (string)value["value"] + " " + Unit((string)value["unit"]));
            }
            string gameplayId = (string)_selected["game_id"];
            if (!string.IsNullOrEmpty(gameplayId) && _database.TryGet(gameplayId, out CreatureDef creature))
            {
                CreatureStats stats = creature.Stats;
                ui.Label($"HP {stats.Hp} · SPD {stats.Speed} · {_copy.Ui("dex.yin_atk")} {stats.YinAtk} · {_copy.Ui("dex.yang_atk")} {stats.YangAtk} · {_copy.Ui("dex.yin_def")} {stats.YinDef} · {_copy.Ui("dex.yang_def")} {stats.YangDef} (LV100)");
                string mastery = _copy.Ui("ui.codex.mastery") + "：";
                foreach (ElementValue value in creature.Mastery) mastery += _copy.Element(value.Element.ToString()) + $" {value.Value}%  ";
                ui.Label(mastery);
                string resist = _copy.Ui("ui.codex.resist") + "：";
                foreach (Element element in Enum.GetValues(typeof(Element))) resist += _copy.Element(element.ToString()) + $" {creature.GetResist(element)}%  ";
                ui.Label(resist);
                Link(ui, CodexKind.Skill, creature.SignatureMove.Id);
                Link(ui, CodexKind.Skill, creature.Trait.Id);
                DrawKnownRelations(creature);
                foreach (RelationLink link in _database.GetRelations(creature.Id)) Link(ui, CodexKind.Creature, link.Other.Id);
            }
        }

        // REQ: docs/design/cuisine.md#6 — the codex recipe page; unknown entries reveal only method and one category.
        private void DrawRecipes(AdventurePanel ui)
        {
            int known = 0;
            foreach (RecipeSpec recipe in _recipes) if (_recipeKnown(recipe.Id)) known++;
            int pages = Math.Max(1, (_recipes.Count + PageSize - 1) / PageSize);
            if (_page >= pages) _page = 0;
            GUILayout.BeginHorizontal();
            if (ui.Button("◀", _page > 0)) { _page--; GUIUtility.ExitGUI(); }
            ui.Label($"{_page + 1}/{pages} · {known}/{_recipes.Count}");
            if (ui.Button("▶", _page + 1 < pages)) { _page++; GUIUtility.ExitGUI(); }
            GUILayout.EndHorizontal();
            for (int i = _page * PageSize; i < Math.Min(_recipes.Count, (_page + 1) * PageSize); i++)
            {
                RecipeSpec recipe = _recipes[i];
                string method = _copy.Ui("dish.method." + recipe.Method);
                if (!_recipeKnown(recipe.Id))
                {
                    ui.Label(_copy.Ui("ui.skills.recipe_unknown").Replace("{method}", method).Replace("{category}",
                        _copy.Ui("ingredient.category." + CookingRules.RecipeHintCategory(recipe, _foods))));
                    continue;
                }
                ui.Heading(_copy.Ui(recipe.OutputFoodId) + " · " + method);
                var parts = new List<string>(recipe.Requirements.Count);
                foreach (RecipeIngredientRequirement requirement in recipe.Requirements)
                    parts.Add((requirement.IsCategoryMatch ? _copy.Ui("ingredient.category." + requirement.Category)
                        : _copy.Ui(requirement.IngredientId)) + " ×" + requirement.Quantity);
                ui.Label(string.Join(" + ", parts));
            }
        }

        private string Unit(string unit) => unit == "percent" ? "%" : unit == "ms" || unit == "mm" ? unit : string.Empty;

        private void DrawField(AdventurePanel ui, string key, JToken value)
        {
            if (value == null || value.Type == JTokenType.Null) { ui.Label(_copy.Ui("dex.field." + key) + "：" + _copy.Ui("dex.missing")); return; }
            string content = value is JArray array ? string.Join("、", array.ToObject<string[]>()) : (string)value;
            foreach (string hidden in _hiddenNames) content = content.Replace(hidden, _copy.Ui("dex.unknown"));
            ui.Heading(_copy.Ui("dex.field." + key)); ui.Label(content);
        }

        private void DrawKnownRelations(CreatureDef creature)
        {
            _knownRelations.Clear();
            foreach (RelationLink link in _database.GetRelations(creature.Id))
            {
                JObject other = _catalog.Find(CodexKind.Creature, link.Other.Id);
                if (other != null && Level(other) != CodexLevel.Unknown) _knownRelations.Add(other);
            }
            if (_knownRelations.Count == 0) return;
            _nodeStyle ??= new GUIStyle(GUI.skin.box) { wordWrap = true, fontSize = 13, alignment = TextAnchor.MiddleCenter };
            Rect area = GUILayoutUtility.GetRect(200, 240, GUILayout.ExpandWidth(true));
            Vector2 center = area.center;
            int count = Math.Min(_knownRelations.Count, 8);
            for (int i = 0; i < count; i++)
            {
                float angle = 2 * Mathf.PI * i / count;
                Vector2 node = center + new Vector2(Mathf.Cos(angle) * (area.width - 140) / 2, Mathf.Sin(angle) * 90);
                GUI.DrawTexture(new Rect(Mathf.Min(center.x, node.x), center.y, Mathf.Max(1, Mathf.Abs(node.x - center.x)), 2), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(node.x, Mathf.Min(center.y, node.y), 2, Mathf.Max(1, Mathf.Abs(node.y - center.y))), Texture2D.whiteTexture);
                GUI.Box(new Rect(node.x - 62, node.y - 22, 124, 44), Title(_knownRelations[i]), _nodeStyle);
            }
            GUI.Box(new Rect(center.x - 62, center.y - 22, 124, 44), Title(_selected), _nodeStyle);
        }

        private void Link(AdventurePanel ui, CodexKind kind, string id)
        {
            JObject entry = _catalog.Find(kind, id);
            if (entry == null || Level(entry) == CodexLevel.Unknown) return;
            if (ui.Button(Title(entry))) { _selected = entry; GUIUtility.ExitGUI(); }
        }

        private void DrawPortrait(JObject entry)
        {
            if (_portraits == null) return;
            Texture2D portrait = null;
            if ((string)entry["kind"] == "character") portrait = _portraits.FindCharacter((string)entry["game_id"]);
            else if ((string)entry["kind"] == "creature" && Enum.TryParse((string)entry["stage"], true, out CreatureStage stage))
                portrait = _portraits.FindCreature((string)entry["subject_id"], stage);
            if (portrait != null)
                GUI.DrawTexture(GUILayoutUtility.GetRect(160, 160, GUILayout.ExpandWidth(false)), portrait, ScaleMode.ScaleToFit, true);
        }
    }
}
