using System.Collections.Generic;
using System.IO;
using Cosmimera.Crafting;
using Cosmimera.Combat;
using Cosmimera.GameData;
using UnityEngine;

namespace Cosmimera.Bootstrap.Demo
{
    internal readonly struct GatheringNode
    {
        internal GatheringNode(long id, string source) { Id = id; Source = source; }
        internal long Id { get; }
        internal string Source { get; }
    }

    internal static class CraftJournal
    {
        internal static CraftCatalog LoadCatalog()
        {
            TextAsset items = Resources.Load<TextAsset>("Crafting/items");
            TextAsset recipes = Resources.Load<TextAsset>("Crafting/recipes");
            if (items == null || recipes == null) throw new InvalidDataException("Missing staged crafting catalog");
            return new CraftCatalog(items.text, recipes.text);
        }

        internal static void DrawStock(AdventurePanel ui, DemoCopy copy, CraftInventory inventory)
        {
            ui.Heading(copy.Ui("ui.craft.stock"));
            IReadOnlyList<CraftStack> stock = inventory.Snapshot();
            if (stock.Count == 0) ui.Label(copy.Ui("ui.journal.empty"));
            foreach (CraftStack stack in stock) ui.Label(copy.Ui(stack.Id) + " ×" + stack.Quantity);
        }

        internal static CraftRecipe DrawRecipes(AdventurePanel ui, DemoCopy copy, CraftCatalog catalog,
            CraftInventory inventory, IReadOnlyCollection<FieldUtility> utilities, bool showLocked)
        {
            ui.Heading(copy.Ui("ui.craft.title"));
            foreach (CraftRecipe recipe in catalog.Recipes)
            {
                CraftResult result = CraftRules.CanCraft(recipe, utilities, inventory);
                if (!showLocked && result == CraftResult.MissingSkill) continue;
                foreach (CraftStack output in recipe.Gains) ui.Label(copy.Ui(output.Id) + " ×" + output.Quantity);
                foreach (CraftStack output in recipe.Gains)
                    if (catalog.TryGetItem(output.Id, out CraftItem item)) DrawGearStats(ui, copy, item);
                ui.Label(copy.Ui("ui.craft.requires").Replace("{skill}", copy.Ui("utility." + recipe.Utility)) + " · " + copy.Ui("craft.result." + result));
                foreach (CraftStack cost in recipe.Costs)
                    ui.Label("· " + copy.Ui(cost.Id) + " " + inventory.Count(cost.Id) + "/" + cost.Quantity);
                foreach (string tool in recipe.Tools)
                    ui.Label(copy.Ui("ui.craft.tool").Replace("{tool}", copy.Ui(tool)));
                if (ui.Button(copy.Ui("ui.craft.make"), result == CraftResult.Success)) return recipe;
            }
            return null;
        }

        internal static void DrawGearStats(AdventurePanel ui, DemoCopy copy, CraftItem item)
        {
            EquipmentSpec spec = item.Equipment;
            if (spec == null) return;
            if (spec.Target != "creature")
                ui.Label(copy.Ui("ui.craft.player_stats").Replace("{hp}", spec.PlayerHpPercent.ToString("+0;-0;0"))
                    .Replace("{speed}", spec.PlayerSpeedPercent.ToString("+0;-0;0"))
                    .Replace("{roll}", spec.PlayerRollPercent.ToString("+0;-0;0")));
            if (spec.Target == "player") return;
            foreach (EffectSpec effect in spec.CreatureEffects)
            {
                bool scoped = effect.Type == EffectType.MasteryMod || effect.Type == EffectType.ResistMod || effect.Type == EffectType.CdModPercent;
                ui.Label(copy.Ui("ui.craft.effect").Replace("{effect}", copy.Ui("ui.craft.effect." + effect.Type))
                    .Replace("{delta}", effect.Delta.ToString("+0;-0;0"))
                    .Replace("{element}", !scoped ? "" : effect.AnyElement ? copy.Ui("ui.craft.any_element") : copy.Element(effect.Element.ToString())));
            }
        }
    }
}
