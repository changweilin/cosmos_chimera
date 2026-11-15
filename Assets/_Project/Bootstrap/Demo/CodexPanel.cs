using System;
using System.Collections.Generic;
using Cosmimera.GameData;
using UnityEngine;

namespace Cosmimera.Bootstrap.Demo
{
    internal sealed class CodexPanel
    {
        private const int PageSize = 8;
        private readonly GameDatabase _database;
        private readonly DemoCopy _copy;
        private readonly Func<string, bool> _owned;
        private readonly ConceptPortraitTable _portraits;
        private readonly List<GameDef> _entries = new List<GameDef>();
        private int _kind;
        private int _page;
        private bool _ownedOnly;
        private string _search = string.Empty;
        private GameDef _selected;
        private GUIStyle _nodeStyle;

        internal CodexPanel(GameDatabase database, DemoCopy copy, Func<string, bool> owned, ConceptPortraitTable portraits = null)
        {
            _database = database;
            _copy = copy;
            _owned = owned;
            _portraits = portraits;
            Refresh();
        }

        private void Refresh()
        {
            _entries.Clear();
            foreach (GameDef def in _database.All)
            {
                bool kindMatches = _kind == 1 ? def is MoveDef : _kind == 2 ? def is TraitDef : def is CreatureDef;
                if (!kindMatches || (_ownedOnly && def is CreatureDef && !_owned(def.Id))) continue;
                if (_copy.Name(def.Id).IndexOf(_search, StringComparison.OrdinalIgnoreCase) < 0 &&
                    def.Id.IndexOf(_search, StringComparison.OrdinalIgnoreCase) < 0) continue;
                _entries.Add(def);
            }
            _entries.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            _page = 0;
            _selected = _entries.Count > 0 ? _entries[0] : null;
        }

        internal void Draw(AdventurePanel ui)
        {
            GUILayout.BeginHorizontal();
            for (int i = 0; i < 4; i++)
            {
                if (!ui.Button(_copy.Ui("ui.codex.kind." + i))) continue;
                _kind = i;
                Refresh();
                GUIUtility.ExitGUI();
            }
            GUILayout.EndHorizontal();
            ui.Label(_copy.Ui("ui.codex.search"));
            string search = ui.TextField("CodexSearch", _search);
            if (search != _search)
            {
                _search = search;
                Refresh();
                GUIUtility.ExitGUI();
            }
            if ((_kind == 0 || _kind == 3) && ui.Button(_copy.Ui(_ownedOnly ? "ui.codex.owned_only" : "ui.codex.all")))
            {
                _ownedOnly = !_ownedOnly;
                Refresh();
                GUIUtility.ExitGUI();
            }
            int pages = Math.Max(1, (_entries.Count + PageSize - 1) / PageSize);
            GUILayout.BeginHorizontal();
            if (ui.Button("◀", _page > 0)) { _page--; GUIUtility.ExitGUI(); }
            ui.Label($"{_page + 1}/{pages} · {_entries.Count}");
            if (ui.Button("▶", _page + 1 < pages)) { _page++; GUIUtility.ExitGUI(); }
            GUILayout.EndHorizontal();
            for (int i = _page * PageSize; i < Math.Min(_entries.Count, (_page + 1) * PageSize); i++)
            {
                GameDef def = _entries[i];
                string mark = def is CreatureDef && _owned(def.Id) ? "★ " : string.Empty;
                if (ui.Button(mark + _copy.Name(def.Id))) { _selected = def; GUIUtility.ExitGUI(); }
            }
            if (_selected == null) { ui.Label(_copy.Ui("ui.journal.empty")); return; }
            ui.Heading(_copy.Name(_selected.Id));
            if (_selected is CreatureDef creature)
            {
                Texture2D portrait = _portraits != null ? _portraits.FindCreature(creature.LineId, creature.Stage) : null;
                if (portrait != null)
                    GUI.DrawTexture(GUILayoutUtility.GetRect(120, 120, GUILayout.ExpandWidth(false)), portrait, ScaleMode.ScaleToFit, true);
                ui.Label(_copy.Ui("ui.stage." + creature.Stage.ToString().ToLowerInvariant()));
                ui.Label($"CON {creature.Stats.Con} · STR {creature.Stats.Str} · SPI {creature.Stats.Spi} · AGI {creature.Stats.Agi} (LV100)");
                string mastery = _copy.Ui("ui.codex.mastery") + "：";
                foreach (ElementValue value in creature.Mastery) mastery += _copy.Element(value.Element.ToString()) + $" {value.Value}%  ";
                ui.Label(mastery);
                string resist = _copy.Ui("ui.codex.resist") + "：";
                foreach (Element element in Enum.GetValues(typeof(Element)))
                    resist += _copy.Element(element.ToString()) + $" {creature.GetResist(element)}%  ";
                ui.Label(resist);
                if (ui.Button(_copy.Ui("ui.codex.move") + "：" + _copy.Name(creature.SignatureMove.Id)))
                { _selected = creature.SignatureMove; GUIUtility.ExitGUI(); }
                if (ui.Button(_copy.Ui("ui.codex.trait") + "：" + _copy.Name(creature.Trait.Id)))
                { _selected = creature.Trait; GUIUtility.ExitGUI(); }
                ui.Label(_copy.Ui("ui.journal.tab.Skills") + "：" + _copy.Ui("utility." + creature.FieldUtility));
                ui.Heading(_copy.Ui("ui.codex.kind.3"));
                IReadOnlyList<RelationLink> links = _database.GetRelations(creature.Id);
                if (links.Count == 0) ui.Label(_copy.Ui("ui.journal.empty"));
                else DrawRelations(creature, links);
                foreach (RelationLink link in links)
                {
                    if (ui.Button(_copy.Name(creature.Id) + " ↔ " + _copy.Name(link.Other.Id) + " · " + _copy.Ui("relation." + link.Kind)))
                    { _selected = link.Other; GUIUtility.ExitGUI(); }
                }
            }
            else if (_selected is MoveDef move)
            {
                ui.Label(_copy.Element(move.Element.ToString()) + $" · {_copy.Ui("ui.codex.power")} {move.Power} · CD {move.CooldownMs / 1000f:0.0}s · {_copy.Ui("ui.codex.accuracy")} {move.Accuracy}%");
                ui.Label(_copy.Ui("shape." + move.Shape) + $" · {move.BaseRangeMm / 1000f:0.0}m");
                if (move.Effect != null) ui.Label(EffectLabel(move.Effect));
            }
            else if (_selected is TraitDef trait)
            {
                ui.Label(_copy.Ui("trigger." + trait.Trigger.Type));
                if (trait.Trigger.HasElement)
                    ui.Label(trait.Trigger.AnyElement ? _copy.Ui("ui.codex.any_element") : _copy.Element(trait.Trigger.Element.ToString()));
                if (trait.Trigger.Type == TriggerType.OnHpBelow || trait.Trigger.Type == TriggerType.OnHpAbove ||
                    trait.Trigger.Type == TriggerType.OnHpCrossBelow) ui.Label($"HP {trait.Trigger.Percent}%");
                if (trait.Trigger.HasDuration) ui.Label($"{trait.Trigger.DurationMs / 1000f:0.0}s");
                foreach (EffectDef effect in trait.Effects) ui.Label(EffectLabel(effect));
            }
        }

        private string EffectLabel(EffectDef effect)
        {
            string scope = effect.HasElement ? (effect.AnyElement ? _copy.Ui("ui.codex.any_element") :
                _copy.Element(effect.Element.ToString())) + " · " : string.Empty;
            return scope + _copy.Ui("effect." + effect.Type) + " " +
                (effect.Type == EffectType.HealPercentMaxHp ? effect.Percent.ToString() : effect.Delta.ToString()) + "%";
        }

        private void DrawRelations(CreatureDef creature, IReadOnlyList<RelationLink> links)
        {
            _nodeStyle ??= new GUIStyle(GUI.skin.box) { wordWrap = true, fontSize = 13, alignment = TextAnchor.MiddleCenter };
            Rect area = GUILayoutUtility.GetRect(200, 240, GUILayout.ExpandWidth(true));
            Vector2 center = area.center;
            int count = Math.Min(links.Count, 8);
            for (int i = 0; i < count; i++)
            {
                float angle = 2 * Mathf.PI * i / count;
                Vector2 node = center + new Vector2(Mathf.Cos(angle) * (area.width - 140) / 2, Mathf.Sin(angle) * 90);
                Color color = GUI.color;
                GUI.color = new Color(0.55f, 0.85f, 1f);
                // Axis-aligned branches retain the scroll view's clip space under DPI scaling.
                GUI.DrawTexture(new Rect(Mathf.Min(center.x, node.x), center.y,
                    Mathf.Max(1, Mathf.Abs(node.x - center.x)), 2), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(node.x, Mathf.Min(center.y, node.y),
                    2, Mathf.Max(1, Mathf.Abs(node.y - center.y))), Texture2D.whiteTexture);
                GUI.color = color;
                GUI.Box(new Rect(node.x - 62, node.y - 22, 124, 44), _copy.Name(links[i].Other.Id), _nodeStyle);
            }
            GUI.Box(new Rect(center.x - 62, center.y - 22, 124, 44), _copy.Name(creature.Id), _nodeStyle);
        }
    }
}
