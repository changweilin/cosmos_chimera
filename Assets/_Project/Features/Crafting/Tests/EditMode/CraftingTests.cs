using System;
using System.Collections.Generic;
using System.IO;
using Cosmimera.Combat;
using Cosmimera.Core.Contracts;
using Cosmimera.GameData;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Cosmimera.Crafting.Tests
{
    public sealed class CraftingTests
    {
        private CraftCatalog _catalog;
        private CraftInventory _bag;
        private static readonly CraftStack[] Empty = Array.Empty<CraftStack>();
        private static readonly HashSet<FieldUtility> AllSkills = new HashSet<FieldUtility>((FieldUtility[])Enum.GetValues(typeof(FieldUtility)));
        private static readonly HashSet<FieldUtility> NoSkills = new HashSet<FieldUtility>();
        private static string Data(string folder) => File.ReadAllText(Path.Combine(Application.dataPath,
            "../Data/_incoming/life-crafting-draft-01", folder, folder + ".json"));

        [SetUp]
        public void SetUp() { _catalog = new CraftCatalog(Data("craft-items"), Data("craft-recipes")); _bag = new CraftInventory(); }
        private void Add(string id, int count) => Assert.That(_bag.TryExchange(Empty, new[] { new CraftStack(id, count) }), Is.True);
        private CraftRecipe Recipe(string id)
        {
            foreach (CraftRecipe recipe in _catalog.Recipes) if (recipe.Id == id) return recipe;
            throw new InvalidOperationException(id);
        }

        [Test]
        public void Exchange_sums_repeated_costs_and_cannot_pay_costs_with_outputs()
        {
            Add("raw_hemp", 3);
            var costs = new[] { new CraftStack("raw_hemp", 2), new CraftStack("raw_hemp", 2) };
            Assert.That(_bag.TryExchange(costs, new[] { new CraftStack("raw_hemp", 10) }), Is.False);
            Assert.That(_bag.Count("raw_hemp"), Is.EqualTo(3));
            Assert.That(_bag.TryExchange(new[] { new CraftStack("raw_hemp", 3) }, new[] { new CraftStack("raw_hemp", 2) }), Is.True);
            Assert.That(_bag.Count("raw_hemp"), Is.EqualTo(2));
        }

        [Test]
        public void Overflow_never_consumes_inputs_or_truncates_outputs()
        {
            Add("raw_hemp", 2); Add("craft_linen", int.MaxValue);
            CraftRecipe recipe = Recipe("recipe_weave_linen");
            Assert.That(CraftRules.TryCraft(recipe, AllSkills, _bag), Is.EqualTo(CraftResult.CapacityExceeded));
            Assert.That(_bag.Count("raw_hemp"), Is.EqualTo(2));
            Assert.That(_bag.Count("craft_linen"), Is.EqualTo(int.MaxValue));
        }

        [Test]
        public void Inventory_preconditions_and_snapshots_are_independent()
        {
            Assert.Throws<ContractException>(() => new CraftStack("bad-id", 1));
            Assert.Throws<ContractException>(() => new CraftStack("a", 0));
            Assert.Throws<ContractException>(() => _bag.Count(null));
            Assert.Throws<ContractException>(() => _bag.TryExchange(null, Empty));
            Assert.Throws<ContractException>(() => _bag.TryExchange(Empty, new[] { default(CraftStack) }));
            Add("z", 1); Add("a", 1);
            IReadOnlyList<CraftStack> snapshot = _bag.Snapshot(); Add("a", 1);
            Assert.That(snapshot[0].Id, Is.EqualTo("a")); Assert.That(snapshot[0].Quantity, Is.EqualTo(1));
            Assert.That(_bag.TryExchange(new[] { new CraftStack("a", 2) }, Empty), Is.True);
            Assert.That(_bag.Count("a"), Is.Zero);
        }

        [Test]
        public void Nodes_require_skill_season_and_only_deplete_after_success()
        {
            var gathering = new CraftGathering(); var yields = new List<CraftStack>();
            Assert.That(gathering.TryGather(1, "shrub", "summer", _catalog, NoSkills, _bag, yields), Is.EqualTo(CraftResult.MissingSkill));
            Assert.That(gathering.TryGather(1, "shrub", "winter", _catalog, AllSkills, _bag, yields), Is.EqualTo(CraftResult.NoResource));
            Assert.That(gathering.TryGather(1, "shrub", "summer", _catalog, AllSkills, _bag, yields), Is.EqualTo(CraftResult.Success));
            int count = _bag.Count("raw_cotton"); Assert.That(count, Is.Positive);
            Assert.That(gathering.TryGather(1, "shrub", "summer", _catalog, AllSkills, _bag, yields), Is.EqualTo(CraftResult.Depleted));
            Assert.That(yields, Is.Empty); Assert.That(_bag.Count("raw_cotton"), Is.EqualTo(count));
            Assert.Throws<ContractException>(() => gathering.Preview(-1, "shrub", "summer", _catalog, AllSkills, _bag, yields));
            Assert.Throws<ContractException>(() => gathering.Preview(1, "shrub", "wrong", _catalog, AllSkills, _bag, yields));
        }

        [Test]
        public void Full_bag_keeps_node_available_and_pickaxe_replaces_mining_skill()
        {
            var gathering = new CraftGathering(); var yields = new List<CraftStack>();
            Add("craft_pickaxe", 1); Add("raw_iron_ore", int.MaxValue);
            Assert.That(gathering.TryGather(2, "ore_iron", "spring", _catalog, NoSkills, _bag, yields), Is.EqualTo(CraftResult.CapacityExceeded));
            Assert.That(_bag.TryExchange(new[] { new CraftStack("raw_iron_ore", int.MaxValue) }, Empty), Is.True);
            Assert.That(gathering.TryGather(2, "ore_iron", "spring", _catalog, NoSkills, _bag, yields), Is.EqualTo(CraftResult.Success));
            Assert.That(_bag.Count("craft_pickaxe"), Is.EqualTo(1));
        }

        [Test]
        public void Recipe_checks_skills_materials_and_reusable_tools_before_consumption()
        {
            CraftRecipe recipe = Recipe("recipe_linen_outfit"); Add("craft_linen", 2); Add("craft_dye", 1);
            Assert.That(CraftRules.TryCraft(recipe, NoSkills, _bag), Is.EqualTo(CraftResult.MissingSkill));
            Assert.That(CraftRules.TryCraft(recipe, AllSkills, _bag), Is.EqualTo(CraftResult.MissingTool));
            Assert.That(_bag.Count("craft_linen"), Is.EqualTo(2)); Add("craft_needle", 1);
            Assert.That(CraftRules.TryCraft(recipe, AllSkills, _bag), Is.EqualTo(CraftResult.Success));
            Assert.That(_bag.Count("craft_needle"), Is.EqualTo(1)); Assert.That(_bag.Count("craft_linen_outfit"), Is.EqualTo(1));
            Assert.That(CraftRules.TryCraft(recipe, AllSkills, _bag), Is.EqualTo(CraftResult.MissingMaterials));
            Assert.Throws<ContractException>(() => CraftRules.CanCraft(null, AllSkills, _bag));
        }

        private static readonly GripTuning Grips = new GripTuning(30, 15);
        private static readonly GripMode[] TwoHand = { GripMode.TwoHanded };
        private static readonly GripMode[] Dual = { GripMode.DualWield };
        private static readonly GripMode[] Shield = { GripMode.OneHandShield };

        private static CraftCatalog GearCatalog()
        {
            JArray items = JArray.Parse(Data("craft-items"));
            foreach (string json in new[]
            {
                @"{ ""id"": ""test_greatsword"", ""category"": ""equipment"", ""sources"": [], ""utility"": ""none"", ""quantity"": 1, ""season"": ""all"",
                    ""equipment"": { ""kind"": ""two_handed"", ""target"": ""creature"", ""player"": { ""max_hp_percent"": 0, ""move_speed_percent"": 0, ""roll_cooldown_percent"": 0 },
                    ""creature_effects"": [ { ""type"": ""range_mod_percent"", ""params"": { ""delta"": 10 } } ],
                    ""weapon"": { ""element"": ""fire"", ""element_mastery"": 20, ""power_permille"": 2000, ""speed_permille"": 900 } } }",
                @"{ ""id"": ""test_dagger"", ""category"": ""equipment"", ""sources"": [], ""utility"": ""none"", ""quantity"": 1, ""season"": ""all"",
                    ""equipment"": { ""kind"": ""one_handed"", ""target"": ""creature"", ""player"": { ""max_hp_percent"": 0, ""move_speed_percent"": 0, ""roll_cooldown_percent"": 0 },
                    ""creature_effects"": [], ""weapon"": { ""element"": ""water"", ""element_mastery"": 5, ""power_permille"": 900, ""speed_permille"": 1100 } } }",
                @"{ ""id"": ""test_shield"", ""category"": ""equipment"", ""sources"": [], ""utility"": ""none"", ""quantity"": 1, ""season"": ""all"",
                    ""equipment"": { ""kind"": ""shield"", ""target"": ""both"", ""player"": { ""max_hp_percent"": 5, ""move_speed_percent"": 0, ""roll_cooldown_percent"": 0, ""yin_defense"": 30, ""yang_defense"": 12 },
                    ""creature_effects"": [ { ""type"": ""resist_mod"", ""params"": { ""delta"": 5, ""element"": ""earth"" } } ] } }",
            }) items.Add(JObject.Parse(json));
            return new CraftCatalog(items.ToString(), Data("craft-recipes"),
                @"[{ ""species"": ""s_two"", ""grips"": [""two_handed"", ""one_hand_shield""] }]");
        }

        [Test]
        public void Equipment_reserves_copies_and_refuses_wrong_target_slot_and_overflowing_returns()
        {
            var gear = new EquipmentLoadout(); Add("craft_iron_blade", 1);
            Assert.That(gear.TryEquip(0, "craft_iron_blade", "main_hand", EquipmentRules.PlayerGrips, _catalog, _bag), Is.False, "weapons are creature-only");
            Assert.That(gear.TryEquip(1, "craft_iron_blade", "upper", Dual, _catalog, _bag), Is.False, "a weapon does not fit armor");
            Assert.That(gear.TryEquip(1, "craft_iron_blade", "main_hand", TwoHand, _catalog, _bag), Is.False, "grip must permit one hand");
            Assert.That(gear.TryEquip(1, "craft_iron_blade", "main_hand", Dual, _catalog, _bag), Is.True);
            Assert.That(gear.TryEquip(2, "craft_iron_blade", "main_hand", Dual, _catalog, _bag), Is.False);
            Add("craft_iron_blade", int.MaxValue);
            Assert.That(gear.TryUnequip(1, "main_hand", _bag), Is.False);
            Assert.That(gear.Snapshot().Count, Is.EqualTo(1));
            Assert.That(_bag.TryExchange(new[] { new CraftStack("craft_iron_blade", 1) }, Empty), Is.True);
            Assert.That(gear.TryUnequip(1, "main_hand", _bag), Is.True);
            Assert.That(_bag.Count("craft_iron_blade"), Is.EqualTo(int.MaxValue));
            Assert.Throws<ContractException>(() => gear.Restore(new[] { new EquippedItem(0, "upper", "a"), new EquippedItem(0, "upper", "b") }));
            Assert.Throws<ContractException>(() => new EquippedItem(0, "armor", "a"));
            Assert.Throws<ContractException>(() => gear.TryEquip(1, "craft_iron_blade", "main_hand", null, _catalog, _bag));
        }

        [Test]
        public void Two_handed_weapon_frees_the_off_hand_and_off_hand_displaces_it()
        {
            CraftCatalog catalog = GearCatalog(); var gear = new EquipmentLoadout();
            Add("craft_iron_blade", 2); Add("test_greatsword", 1); Add("test_shield", 1);
            var both = new[] { GripMode.TwoHanded, GripMode.OneHandShield };
            Assert.That(gear.TryEquip(1, "craft_iron_blade", "main_hand", both, catalog, _bag), Is.True);
            Assert.That(gear.TryEquip(1, "test_shield", "off_hand", both, catalog, _bag), Is.True);
            Assert.That(gear.TryEquip(1, "craft_iron_blade", "off_hand", both, catalog, _bag), Is.False, "two blades need dual wield");
            Assert.That(gear.TryEquip(1, "test_greatsword", "main_hand", both, catalog, _bag), Is.True);
            Assert.That(gear.ItemIn(1, "main_hand"), Is.EqualTo("test_greatsword"));
            Assert.That(gear.ItemIn(1, "off_hand"), Is.Null);
            Assert.That(_bag.Count("craft_iron_blade"), Is.EqualTo(2)); Assert.That(_bag.Count("test_shield"), Is.EqualTo(1));
            Assert.That(gear.TryEquip(1, "test_shield", "off_hand", both, catalog, _bag), Is.True);
            Assert.That(gear.ItemIn(1, "main_hand"), Is.Null, "a shield displaces the two-handed weapon");
            Assert.That(_bag.Count("test_greatsword"), Is.EqualTo(1));
            Add("test_shield", int.MaxValue);
            Assert.That(gear.TryEquip(1, "craft_iron_blade", "main_hand", both, catalog, _bag), Is.True);
            Assert.That(gear.TryEquip(1, "test_greatsword", "main_hand", both, catalog, _bag), Is.False, "full bag cannot take back the displaced shield");
            Assert.That(gear.ItemIn(1, "main_hand"), Is.EqualTo("craft_iron_blade"));
            Assert.That(gear.ItemIn(1, "off_hand"), Is.EqualTo("test_shield"));
        }

        [Test]
        public void Required_grips_cover_every_hand_pair()
        {
            Assert.That(EquipmentRules.RequiredGrips(null, null), Is.Null);
            Assert.That(EquipmentRules.RequiredGrips(EquipmentKind.TwoHanded, null), Is.EqualTo(TwoHand));
            Assert.That(EquipmentRules.RequiredGrips(EquipmentKind.TwoHanded, EquipmentKind.Shield), Is.Empty);
            Assert.That(EquipmentRules.RequiredGrips(EquipmentKind.OneHanded, EquipmentKind.OneHanded), Is.EqualTo(Dual));
            Assert.That(EquipmentRules.RequiredGrips(null, EquipmentKind.OneHanded), Is.EqualTo(Dual));
            Assert.That(EquipmentRules.RequiredGrips(EquipmentKind.OneHanded, EquipmentKind.Shield), Is.EqualTo(Shield));
            Assert.That(EquipmentRules.RequiredGrips(null, EquipmentKind.Shield), Is.EqualTo(Shield));
            Assert.That(EquipmentRules.RequiredGrips(EquipmentKind.OneHanded, null), Is.EquivalentTo(new[] { GripMode.DualWield, GripMode.OneHandShield }));
        }

        [Test]
        public void Collect_builds_weapon_loadouts_from_grips()
        {
            CraftCatalog catalog = GearCatalog();
            WeaponLoadout Weapons(params EquippedItem[] e) => EquipmentRules.Collect(1, e, catalog, EquipmentRules.PlayerGrips, Grips).Weapons;
            WeaponLoadout two = Weapons(new EquippedItem(1, "main_hand", "test_greatsword"));
            Assert.That(two.HandCount, Is.EqualTo(1)); Assert.That(two.PowerBonusPercent, Is.EqualTo(30));
            Assert.That(two.PowerPermille(two.First), Is.EqualTo(2600));
            WeaponLoadout dual = Weapons(new EquippedItem(1, "main_hand", "craft_iron_blade"), new EquippedItem(1, "off_hand", "test_dagger"));
            Assert.That(dual.HandCount, Is.EqualTo(2)); Assert.That(dual.SpeedBonusPercent, Is.EqualTo(15));
            Assert.That(dual.Hand(0).Element, Is.EqualTo(Element.Metal)); Assert.That(dual.Hand(1).Element, Is.EqualTo(Element.Water));
            Assert.That(dual.SpeedPermille(dual.Hand(1)), Is.EqualTo(1265));
            WeaponLoadout shield = Weapons(new EquippedItem(1, "main_hand", "craft_iron_blade"), new EquippedItem(1, "off_hand", "test_shield"));
            Assert.That(shield.HasShield && shield.HandCount == 1 && shield.PowerBonusPercent == 0 && shield.SpeedBonusPercent == 0, Is.True);
        }

        [Test]
        public void Hands_outside_the_wearer_grips_give_nothing_but_armor_still_counts()
        {
            CraftCatalog catalog = GearCatalog();
            var entries = new[] { new EquippedItem(1, "main_hand", "test_greatsword"), new EquippedItem(1, "upper", "craft_down_coat") };
            EquipmentModifiers ok = EquipmentRules.Collect(1, entries, catalog, catalog.GripsOf("s_two"), Grips);
            Assert.That(ok.HandsValid, Is.True); Assert.That(ok.CreatureEffects.Count, Is.EqualTo(2));
            EquipmentModifiers bad = EquipmentRules.Collect(1, entries, catalog, Dual, Grips);
            Assert.That(bad.HandsValid, Is.False); Assert.That(bad.Weapons.HandCount, Is.Zero);
            Assert.That(bad.CreatureEffects.Count, Is.EqualTo(1), "armor still applies");
            Assert.That(catalog.GripsOf("unknown"), Is.Empty);
            Assert.Throws<ContractException>(() => EquipmentRules.Collect(1, entries, catalog, null, Grips));
        }

        [Test]
        public void Player_takes_shield_and_armor_effects_but_never_weapons()
        {
            CraftCatalog catalog = GearCatalog();
            var entries = new[] { new EquippedItem(0, "off_hand", "test_shield"), new EquippedItem(0, "upper", "craft_iron_guard") };
            EquipmentModifiers mods = EquipmentRules.Collect(0, entries, catalog, EquipmentRules.PlayerGrips, Grips);
            Assert.That(mods.PlayerHpPercent, Is.EqualTo(20));
            Assert.That(mods.PlayerYinDefence, Is.EqualTo(30), "combat.md#玩家角色的守: shield defence reaches the player");
            Assert.That(mods.PlayerYangDefence, Is.EqualTo(12));
            Assert.That(mods.Weapons.HasShield, Is.False); Assert.That(mods.CreatureEffects, Is.Empty);
        }

        [Test]
        public void Equipment_stats_apply_without_changing_roll_duration_or_granting_retired_item_bonuses()
        {
            EquipmentModifiers Collect(params EquippedItem[] e) => EquipmentRules.Collect(0, e, _catalog, EquipmentRules.PlayerGrips, Grips);
            var baseline = new PlayerVitalsConfig(100, 300, 200, 1000);
            PlayerVitalsConfig modified = EquipmentRules.ApplyPlayer(baseline,
                Collect(new EquippedItem(0, "upper", "craft_iron_guard"), new EquippedItem(0, "accessory", "craft_gem_pendant")));
            Assert.That(modified.MaxHp, Is.GreaterThan(baseline.MaxHp));
            Assert.That(modified.RollCooldownMs, Is.LessThan(baseline.RollCooldownMs));
            Assert.That(modified.RollDurationMs, Is.EqualTo(baseline.RollDurationMs));
            Assert.That(EquipmentRules.ApplyPlayer(baseline, Collect(new EquippedItem(0, "upper", "retired_outfit"))).MaxHp, Is.EqualTo(baseline.MaxHp));
            Assert.Throws<InvalidDataException>(() => Collect(new EquippedItem(0, "upper", "craft_iron_blade")));
        }

        [Test]
        public void Catalog_rejects_weapon_data_on_the_wrong_kind_and_player_bonuses_on_weapons()
        {
            JArray items = JArray.Parse(Data("craft-items"));
            JToken blade = null; foreach (JToken i in items) if ((string)i["id"] == "craft_iron_blade") blade = i;
            blade["equipment"]["player"]["max_hp_percent"] = 5;
            Assert.Throws<InvalidDataException>(() => new CraftCatalog(items.ToString(), Data("craft-recipes")));
            blade["equipment"]["player"]["max_hp_percent"] = 0;
            blade["equipment"]["player"]["yin_defense"] = 10;
            Assert.Throws<InvalidDataException>(() => new CraftCatalog(items.ToString(), Data("craft-recipes")), "weapons give the player no defence");
            ((JObject)blade["equipment"]["player"]).Remove("yin_defense"); blade["equipment"]["kind"] = "upper";
            Assert.Throws<InvalidDataException>(() => new CraftCatalog(items.ToString(), Data("craft-recipes")));
            blade["equipment"]["kind"] = "one_handed"; ((JObject)blade["equipment"]).Remove("weapon");
            Assert.Throws<InvalidDataException>(() => new CraftCatalog(items.ToString(), Data("craft-recipes")));
            foreach (string grips in new[] { @"[{""species"":""a"",""grips"":[]}]", @"[{""species"":""a"",""grips"":[""dual_wield"",""dual_wield""]}]",
                @"[{""species"":""a"",""grips"":[""sword""]}]", @"[{""species"":""a"",""grips"":[""dual_wield""]},{""species"":""a"",""grips"":[""two_handed""]}]" })
                Assert.Throws<InvalidDataException>(() => new CraftCatalog(Data("craft-items"), Data("craft-recipes"), grips));
        }

        [Test]
        public void Legacy_slots_migrate_and_unknown_ones_do_not()
        {
            Assert.That(EquipmentSlots.MigrateLegacy("weapon"), Is.EqualTo("main_hand"));
            Assert.That(EquipmentSlots.MigrateLegacy("armor"), Is.EqualTo("upper"));
            Assert.That(EquipmentSlots.MigrateLegacy("accessory"), Is.EqualTo("accessory"));
            Assert.That(EquipmentSlots.MigrateLegacy("hat"), Is.Null);
        }

        [Test]
        public void Shipped_grips_cover_every_staged_species()
        {
            var catalog = new CraftCatalog(Data("craft-items"), Data("craft-recipes"), Data("craft-grips"));
            Assert.That(catalog.GripsOf("star_blaze_foal"), Is.EqualTo(new[] { GripMode.TwoHanded }));
            Assert.That(catalog.GripsOf("sun_array_skyheron"), Is.EquivalentTo(new[] { GripMode.OneHandShield, GripMode.TwoHanded }));
        }

        [Test]
        public void Partner_equipment_scales_each_effect_to_a_quarter_rounding_away_from_zero()
        {
            EffectSpec Scale(int delta) => EquipmentRules.ScalePartner(
                new EffectSpec(EffectType.ResistMod, false, Element.Ice, delta, 0, 1));
            Assert.That(Scale(15).Delta, Is.EqualTo(4));
            Assert.That(Scale(10).Delta, Is.EqualTo(3));
            Assert.That(Scale(5).Delta, Is.EqualTo(2));
            Assert.That(Scale(4).Delta, Is.EqualTo(1));
            Assert.That(Scale(1).Delta, Is.EqualTo(1));
            Assert.That(Scale(-5).Delta, Is.EqualTo(-2));
            Assert.That(Scale(0).Delta, Is.EqualTo(0));
            Assert.That(Scale(100).Delta, Is.EqualTo(25));
            EffectSpec any = EquipmentRules.ScalePartner(new EffectSpec(EffectType.CdModPercent, true, default, -5, 0, 2));
            Assert.That(any.AnyElement && any.MaxStacks == 2 && any.Type == EffectType.CdModPercent, Is.True);
            Assert.Throws<ContractException>(() => EquipmentRules.ScalePartner(
                new EffectSpec(EffectType.HealPercentMaxHp, false, default, 0, 10, 1)));
        }

        [Test]
        public void Catalog_refuses_bad_references_and_duplicate_ids()
        {
            JArray items = JArray.Parse(Data("craft-items")); items.Add(items[0].DeepClone());
            Assert.Throws<InvalidDataException>(() => new CraftCatalog(items.ToString(), Data("craft-recipes")));
            JArray recipes = JArray.Parse(Data("craft-recipes")); recipes[0]["costs"][0]["item"] = "missing_item";
            Assert.Throws<InvalidDataException>(() => new CraftCatalog(Data("craft-items"), recipes.ToString()));
            Assert.Throws<InvalidDataException>(() => new CraftCatalog("[{", "[]"));
            Assert.Throws<ContractException>(() => new CraftCatalog(null, "[]"));
        }

        [Test]
        public void Capture_vessels_parse_tiered_and_advanced_specs_and_require_inventory_stock()
        {
            const string itemsJson = @"
            [
              { ""id"": ""raw_ore"", ""category"": ""raw"", ""sources"": [], ""utility"": ""none"", ""quantity"": 1, ""season"": ""all"" },
              { ""id"": ""basic_card_t2"", ""category"": ""capture"", ""sources"": [], ""utility"": ""none"", ""quantity"": 1, ""season"": ""all"",
                ""capture"": { ""mode"": ""basic"", ""tier"": ""adolescent"", ""base_percent"": 55 } },
              { ""id"": ""adv_card_fire"", ""category"": ""capture"", ""sources"": [], ""utility"": ""none"", ""quantity"": 1, ""season"": ""all"",
                ""capture"": { ""mode"": ""advanced"", ""element"": ""fire"", ""base_percent"": 50, ""advanced_bonus_percent"": 25 } }
            ]";
            const string recipesJson = @"
            [
              { ""id"": ""recipe_adv_card_fire"", ""utility"": ""forage_berries"", ""costs"": [{ ""item"": ""raw_ore"", ""quantity"": 1 }], ""gains"": [{ ""item"": ""adv_card_fire"", ""quantity"": 1 }] }
            ]";
            var catalog = new CraftCatalog(itemsJson, recipesJson);
            Assert.That(catalog.TryGetItem("basic_card_t2", out CraftItem basic), Is.True);
            Assert.That(basic.Capture.Mode, Is.EqualTo("basic"));
            Assert.That(basic.Capture.Tier, Is.EqualTo(CreatureStage.Adolescent));
            Assert.That(basic.Capture.BasePercent, Is.EqualTo(55));

            Assert.That(catalog.TryGetItem("adv_card_fire", out CraftItem adv), Is.True);
            Assert.That(adv.Capture.Mode, Is.EqualTo("advanced"));
            Assert.That(adv.Capture.TerrainElement, Is.EqualTo(Element.Fire));
            Assert.That(adv.Capture.AdvancedBonusPercent, Is.EqualTo(25));
            Assert.That(adv.Capture.CraftedOnly, Is.True);

            var bag = new CraftInventory();
            Assert.That(CraftRules.TryConsumeCaptureVessel(adv, bag), Is.EqualTo(CraftResult.MissingMaterials));
            Assert.That(bag.TryExchange(Empty, new[] { new CraftStack("raw_ore", 1) }), Is.True);
            Assert.That(CraftRules.TryCraft(catalog.Recipes[0], AllSkills, bag), Is.EqualTo(CraftResult.Success));
            Assert.That(CraftRules.TryConsumeCaptureVessel(adv, bag), Is.EqualTo(CraftResult.Success));
            Assert.That(bag.Count("adv_card_fire"), Is.Zero);

            Assert.That(catalog.TryGetItem("raw_ore", out CraftItem raw), Is.True);
            Assert.Throws<ContractException>(() => CraftRules.TryConsumeCaptureVessel(raw, bag));

            const string badCapture = @"[{ ""id"": ""bad_cap"", ""category"": ""raw"", ""sources"": [], ""utility"": ""none"", ""quantity"": 1, ""season"": ""all"",
                ""capture"": { ""mode"": ""advanced"", ""element"": ""fire"", ""base_percent"": 50, ""advanced_bonus_percent"": 20 } }]";
            Assert.Throws<InvalidDataException>(() => new CraftCatalog(badCapture, "[]"));
        }

        // REQ: destructibles.md#傷害 擊倒＝收成 (Q-C77).
        [Test]
        public void Knockdown_harvest_uses_only_caster_skills_spends_the_node_and_reports_a_full_bag()
        {
            var gathering = new CraftGathering();
            var yields = new List<CraftStack>();
            var forage = new[] { FieldUtility.ForageBerries };
            Assert.That(gathering.HarvestOnKnockdown(1, "log", "spring", _catalog, new FieldUtility[0], _bag, yields),
                Is.EqualTo(CraftResult.MissingSkill));
            Assert.That(yields, Is.Empty);
            Assert.That(gathering.HarvestOnKnockdown(1, "log", "spring", _catalog, forage, _bag, yields), Is.EqualTo(CraftResult.Success),
                "a refused harvest did not spend the node");
            Assert.That(yields, Is.Not.Empty);
            foreach (CraftStack stack in yields) Assert.That(_bag.Count(stack.Id), Is.GreaterThanOrEqualTo(stack.Quantity));
            Assert.That(gathering.HarvestOnKnockdown(1, "log", "spring", _catalog, forage, _bag, yields), Is.EqualTo(CraftResult.Depleted));
            Assert.That(gathering.Preview(1, "log", "spring", _catalog, forage, _bag, yields), Is.EqualTo(CraftResult.Depleted),
                "the knockdown harvest is the node's one harvest");

            // Capacity is int.MaxValue per id: one stack just below it leaves no room for the harvest.
            var full = new CraftInventory();
            Assert.That(full.TryExchange(Empty, new[] { new CraftStack("raw_timber", int.MaxValue - 1) }), Is.True);
            Assert.That(gathering.HarvestOnKnockdown(100, "log", "spring", _catalog, forage, full, yields), Is.EqualTo(CraftResult.CapacityExceeded));
            Assert.That(yields, Is.Not.Empty, "the caller drops these on the ground");
            Assert.That(full.Count("raw_timber"), Is.EqualTo(int.MaxValue - 1), "inventory unchanged");
            Assert.That(gathering.Preview(100, "log", "spring", _catalog, forage, full, yields), Is.EqualTo(CraftResult.Depleted));
            Assert.That(gathering.HarvestOnKnockdown(5, "no_such_source", "spring", _catalog, forage, _bag, yields), Is.EqualTo(CraftResult.NoResource));
            foreach (string tree in new[] { "broadleaf", "conifer", "umbrella", "swamp_tree" })
            {
                Assert.That(gathering.HarvestOnKnockdown(200 + tree.Length, tree, "spring", _catalog, forage, _bag, yields),
                    Is.EqualTo(CraftResult.Success), tree + " is a timber source");
                Assert.That(yields.Exists(y => y.Id == "raw_timber"), Is.True, tree);
            }
            Assert.Throws<ContractException>(() => gathering.HarvestOnKnockdown(-1, "log", "spring", _catalog, forage, _bag, yields));
            Assert.Throws<ContractException>(() => gathering.HarvestOnKnockdown(2, "log", "monsoon", _catalog, forage, _bag, yields));
            Assert.Throws<ContractException>(() => gathering.HarvestOnKnockdown(2, "log", "spring", _catalog, null, _bag, yields));
        }
    }
}
