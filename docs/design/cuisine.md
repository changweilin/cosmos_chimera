# 料理系統：原料 → 加工品 → 料理（Cuisine）

參考《曠野之息》的「自由投料、食材決定效果、同類疊加、異類抵銷」，接上本作的生活技路線與屬性。生活技的持有、器皿生效規則見 [field-utilities.md](field-utilities.md)；採集節點規則見 [crafting.md](crafting.md#採集與技能來源)。食材、加工品與料理都屬 `Food` 背包，**僅本場有效**。

> 規則層：`Food`（`CookingRules`／`FoodDefinitions`）。數值均為**暫定**，實作時收斂至 `Data/balance.json#food` 與 `Data/food/**`。

## 1. 三層物品與兩段工序

```
採集技（原料）──加工技（加工品，可選）──料理技（料理）
fishing / hunt_* /…   freeze / grind / press /    grill / stew / stir_fry /
forage_* / harvest /  churn / ferment / smoke     deep_fry / steam / cold_mix /
produce_milk                                       chill_brew
```

- **原料**（`ingredient`）：採集技直接取得。可生吃（`raw_heal_percent`，生肉／生禽為 0）。
- **加工品**（`processed`）：原料經一道加工，**1 份原料 → 1 份加工品**，改變食材性質（§3）。加工品不可再加工（燻乾品除外：可再發酵，見 §3）。
- **料理**（`food`）：在鍋中投入 **1–5 份**原料／加工品並選擇料理方式，由 §4 判定成品。
- 加工與料理都在冒險手冊「生活技能」操作；**野外**依隊伍生效的生活技解鎖，**聚落廚房**（`CookingStationKind.Kitchen`）開放全部加工與料理方式。

## 2. 食材屬性（每份原料／加工品皆有）

| 欄位 | 意義 |
|---|---|
| `role` | `base` 主食材 · `effect` 效果食材 · `seasoning` 調味 · `critter` 藥材（蟲／菌） |
| `tags` | 判定料理方式與食譜用：`red_meat` `poultry` `fish` `shellfish` `egg` `dairy` `grain` `flour` `vegetable` `fruit` `herb` `mushroom` `nut` `oil` `salt` `sugar` `spice` `critter`，及狀態 `raw` `chilled` `smoked` `fermented` `powder` |
| `cook_heal_percent` | 入鍋後貢獻的回復量（HP 上限 %） |
| `effect` | 至多 1 個效果家族＋元素（僅 `effect`／`critter` 食材）與 `points`（1★=1、2★=2、3★=3） |
| `duration_add_ms` | 每份延長料理效果的時間（一般 30 000；發酵品 60 000） |

`category` 沿用既有八類，新增 `fungi`（菌菇）與 `critter`（蟲類）；來源 `source` 新增 `forage_fungi`。

## 3. 加工（加工技與加工品）

| 加工 | 技能（屬性） | 輸入 tag → 輸出 | 性質變化 |
|---|---|---|---|
| 急凍 `freeze` | `freeze_prep`（冰） | `fish`/`shellfish`/`red_meat`/`poultry`/`fruit` → 加 `chilled` | 可用於 `cold_mix`、`chill_brew`（生肉冰鮮後即可涼拌）；用於 `steam`／`cold_mix` 時品質分 +1 |
| 研磨 `grind` | `culinary_prep`（冰／木／金／水） | `grain` → `flour`；`herb`/`nut`/`spice` → 加 `powder` | 粉末效果 `points` ×2（濃縮，佔 1 格） |
| 榨油 `press` | `culinary_prep` | `nut`/油料作物 → `oil` | `stir_fry`、`deep_fry` 必要材料 |
| 攪乳 `churn` | `culinary_prep` | 鮮乳 → 奶油（`dairy`+`oil`） | 可代替食用油；`cook_heal_percent` +5 |
| 發酵 `ferment` | `ferment_cure`（水／木／影） | 乳 → 起司；魚 → 魚露（`seasoning`）；穀 → 酒麴；果蔬 → 醃漬；燻肉 → 熟成肉 | 加 `fermented`；`duration_add_ms` 改 60 000 |
| 燻乾 `smoke` | `smoke_dry`（風／火） | 肉／魚 → 燻肉／燻魚；果 → 果乾 | 加 `smoked`；視為熟食，可直接食用（回復 = `cook_heal_percent` × 50%）並可入 `cold_mix` |

- 加工是**確定性**的一對一轉換：先驗證技能與背包容量，再扣原料給成品；拒絕時不變（同 [crafting.md](crafting.md#加工與製作) 交易規則）。
- 加工品保留原料的 `effect` 與元素；`effect` 只因研磨翻倍，其他加工不改效果。

## 4. 料理判定（`PotCookingRules`，純函式）

輸入：料理方式、1–5 份食材（含重複）、`CookingContext`。輸出固定，**無隨機**（取代原作的隨機大成功，確保連線各端一致）。

1. **方式可用**：沿用 `HasSkillForMethod`，另加 `chill_brew` → 需 `chill_brew`（冰／水）。不可用 → 拒絕，不扣料。
2. **方式條件**（不符 → **失敗料理**）：

   | 方式 | 野外需求 | 必要條件 | 修正 |
   |---|---|---|---|
   | 烤 `grill` | `cook_fire` | ≥1 份 `base` | 回復 ×1.0 |
   | 燉 `stew` | `cook_fire` | ≥3 份，或含 `dairy`／魚露 | 時間 ×1.5 |
   | 炒 `stir_fry` | `cook_fire` | 含 `oil` | 效果 `points` +1 |
   | 炸 `deep_fry` | `cook_fire` | 含 `oil` 與 `flour` | 效果 `points` +1、回復 ×1.1 |
   | 蒸 `steam` | `cook_fire` 或 `culinary_prep` | ≥1 份 `base` | 回復 ×1.3 |
   | 涼拌 `cold_mix` | `culinary_prep` | 不含 `raw` 的肉／禽／魚 | 回復 ×0.9、附淨化 |
   | 冰飲 `chill_brew` | `chill_brew` | 只含 `fruit`/`dairy`/`herb`/`sugar`/`critter`/`chilled` | 回復 ×0.8、可於戰鬥中食用 |
   | 研磨 `grind` | `culinary_prep` | 只含 `powder`／`herb`／`egg`／`grain`／`mushroom`／`critter` | 藥粉類，回復 ×0.8 |

3. **招牌料理**：若投料**恰好**滿足某食譜（方式相同、各需求數量一致；`category` 需求可由任一同類食材填），產出該料理並使用**食譜固定效果**（§6）。多個食譜皆符合時，取需求最具體者（指定 ID 需求數多者優先），再依 ID 字典序。
4. **自由料理**（無食譜符合）：
   - **回復** = Σ `cook_heal_percent` × 方式倍率，上限 100%。
   - **效果**：收集所有效果食材的（家族, 元素）。**恰好一種** → 該效果，`points` 加總（含研磨、炒／炸 +1）決定等級；**兩種以上 → 全部抵銷**，只剩回復（原作同規則）。
   - **時間** = 60 000 + Σ `duration_add_ms`（效果食材與調味皆計），× 方式倍率，上限 600 000。
   - **名稱** = 方式 + 主題（主食材 tag 一致時取該 tag，否則「什錦」；無主食材為「藥膳」），有效果時加家族前綴與等級（「火專精・烤肉 Lv1」）。
   - **ID**：`food_free_<method>_<theme>[_<family>[_<element>]_lv<N>]_h<回復>_d<時間ms>[_c]`。ID 編入成品的全部數值，所以同 ID 必為同一道料理，可直接堆疊；首次做出時登錄進本場料理目錄。
5. **失敗料理** `food_dubious_mash`：方式條件不符、只投調味料，或冰飲含肉。**食材照扣**，回復 5%，無效果。
6. **品質**：沿用 `EvaluateQuality`（廚房 +1、火＋備料同在 +2／擇一 +1、隊伍有料理元素 +1），另加：投入 `chilled` 食材且方式為 `steam`／`cold_mix` +1。自由料理的「料理元素」= 效果食材元素；無效果時取首份 `base` 的元素。

### 效果家族與等級（`points` 1–3 → Lv1、4–6 → Lv2、≥7 → Lv3）

| 家族 | 對應既有欄位／效果 | Lv1／Lv2／Lv3 | 典型食材 |
|---|---|---|---|
| 剛烈・專精 `mastery:<元素>` | `mastery_mod` | +5／+10／+15 | 該屬性 2★ 以上肉類、香料 |
| 守護・抗性 `resist:<元素>` | `resist_mod` | +10／+20／+30 | 剋制屬性的果實、根菜（火椒→抗冰） |
| 迅捷 `swift` | `move_speed_mod_percent` | +5／+10／+15% | 風屬禽肉、疾風菇 |
| 靈巧 `nimble` | `roll_cd_mod_percent`、`evasion_mod_percent` | −5／−10／−15%、+3／+6／+9 | 雷屬蝦、跳蟲 |
| 冷卻 `focus` | `cd_mod_percent`（any） | −4／−8／−12% | 光屬穀物、冰薄荷 |
| 銳眼 `keen` | `accuracy_mod`、`crit_rate_mod` | +3／+6／+9 | 金屬性貝類、鷹眼莓 |
| 會心 `fierce` | `crit_damage_mod` | +10／+20／+30 | 影屬菇、熟成肉 |
| 堅守 `guard` | `stagger_capacity_mod_percent`、`block_reduction_mod` | +10／+20／+30 | 土屬根莖、堅殼栗 |
| 豐收 `bounty` | `drop_rate_bonus_percent` | +5／+10／+15 | 金色油籽、松露 |
| 馴服 `tame` | `catch_rate_bonus_percent` | +3／+6／+9 | 蜂蜜、月光花蜜 |
| 甦生 `revive` | `revive_percent` | 20／40／60 | 光屬羽蛋、復甦蕨（**只能**用於 `grind`／`stew`） |

> 抵銷規則讓「專一路線」有價值：想做 Lv3 抗冰燉肉，就要把 5 格都填同家族或中性主食材；混搭只得純回復。

## 5. 路線組合（採集 × 加工 × 料理）

| 路線 | 採集 | 加工（可選） | 料理 | 代表成品 | 涵蓋屬性 |
|---|---|---|---|---|---|
| 鮮魚主線 | `fishing`（水） | 急凍（冰） | 蒸（火） | 清蒸鱸魚 | 水・冰・火 |
| 漁獲冷盤 | `cast_net`（水） | 急凍（冰） | 涼拌（備料） | 冰鎮海鮮沙拉 | 水・冰・木／金 |
| 獵肉燒烤 | `hunt_ground`（火／土／影／雷） | — | 烤（火） | 炭烤野豬串 | 火・土 |
| 熟成肉 | `hunt_ground` | 燻乾（風）→ 發酵（影） | 燉（火） | 熟成獸肉濃燉 | 土・風・影・火 |
| 乾糧行囊 | `set_trap`（土／影） | 燻乾（風／火） | 直接食用 | 燻兔肉乾 | 土・影・風 |
| 禽蛋麵點 | `hunt_sky`（風／雷）＋`harvest_crops` | 研磨麵粉、榨油（備料） | 炸（火） | 疾風炸雞、金穗天婦羅 | 風・雷・木・金 |
| 乳品濃湯 | `produce_milk`（木／土）＋`harvest_crops` | 攪乳、發酵起司 | 燉（火） | 牧野起司濃湯 | 木・土・水 |
| 無火冷廚 | `forage_berries`（木） | 急凍、研磨 | 冰飲／涼拌（冰／水） | 霜莓冰沙 | 木・冰・水 |
| 菌蟲藥膳 | `forage_fungi`（影／土）＋`set_trap` 蟲 | 研磨、發酵 | 研磨／燉 | 復甦草藥粉、夜光菇藥湯 | 影・土・木・光 |

- 同一原料多做：生鱸魚 →（烤）烤鱸魚／（急凍＋蒸）清蒸鱸魚／（急凍＋涼拌）鱸魚冷盤／（燻）燻鱸魚乾糧／（發酵）魚露調味。
- **無火隊伍**仍可完成冷廚與藥膳兩條路線；火只是最多方式的入口，不是必要條件。

## 6. 招牌料理表（固定效果；`F`=既有 ID 沿用）

| ID | 名稱 | 方式 | 投料（恰好） | 效果（品質加成另計） |
|---|---|---|---|---|
| `food_charred_boar_skewer` F | 炭烤野豬串 | grill | 野豬五花 1＋任一 `berry_herb` 1 | 火專精 +15 |
| `food_hearty_beast_stew` F | 豐收獸肉燉 | stew | 上等肋排 1＋獸乳 1＋任一 `grain_crop` 1 | 火抗 +15、土抗 +15 |
| `food_gale_wing_stir_fry` F | 疾風翅炒 | stir_fry | 天禽胸 1＋金色油籽 1 | 全 CD −12%、移速 +15% |
| `food_crispy_shrimp_tempura` F | 酥炸蝦天婦羅 | deep_fry | 網蝦 2＋金色油籽 1＋太陽麥 1 | 閃避 +12、雷專精 +15 |
| `food_steamed_abyssal_salmon` F | 清蒸深淵鮭 | steam | 深淵鮭 1＋活力蕨 1 | 水抗 +20、淨化 |
| `food_chilled_herb_salad` F | 冰鎮香草沙拉 | cold_mix | 野莓 2＋海帶扇貝 1 | 射程 +10% |
| `food_revival_herb_powder` F | 復甦草藥粉 | grind | 活力蕨 1＋太陽麥 1＋天巢蛋 1 | 甦生 40、淨化 |
| `food_steamed_sea_bass` | 清蒸鱸魚（主線） | steam | 冰鮮鱸魚 1＋任一 `berry_herb` 1 | 水專精 +10、隊伍回復 +10 |
| `food_sea_bass_crudo` | 鱸魚冷盤 | cold_mix | 冰鮮鱸魚 1＋鹽 1 | 冰抗 +15、淨化 |
| `food_aged_beast_ragout` | 熟成獸肉濃燉 | stew | 熟成肉 1＋獸乳 1＋任一根菜 1 | 會心 +20、土專精 +10 |
| `food_gale_fried_fowl` | 疾風炸雞 | deep_fry | 天禽胸 1＋麵粉 1＋食用油 1 | 迅捷 Lv2、風專精 +10 |
| `food_meadow_cheese_soup` | 牧野起司濃湯 | stew | 起司 1＋獸乳 1＋任一 `grain_crop` 1 | 堅守 Lv2、HP 回復 +10 |
| `food_frostberry_smoothie` | 霜莓冰沙 | chill_brew | 冰鮮野莓 2＋蜂蜜 1 | 火抗 +20、可戰鬥中飲用 |
| `food_glowcap_tonic` | 夜光菇藥湯 | stew | 夜光菇 2＋發酵酒麴 1 | 影專精 +10、會心 +10 |
| `food_truffle_butter_grill` | 松露奶油烤菇 | grill | 松露 1＋奶油 1＋任一 `fungi` 1 | 豐收 +15 |
| `food_honey_glazed_nuts` | 蜜漬堅果 | cold_mix | 堅殼栗 2＋蜂蜜 1 | 馴服 +6、堅守 Lv1 |

- 招牌料理在首次做出時記入圖鑑「食譜」頁（[encyclopedia.md](encyclopedia.md)）；之後在料理介面可一鍵帶入投料。未發現的食譜顯示為「？」，提示所需方式與一項食材類別。
- 燻乾品（如燻兔肉乾 `ing_smoked_hare`）屬加工食材，可直接食用，不另設料理 ID。
- 金色油籽為堅果（`nut`），須先榨油才能用於自由炒／炸；既有招牌料理仍直接指定油籽。
- 招牌食譜的類別需求（「任一 X」）用「帶入食譜」時優先扣原料，再扣加工品。
- 新增原料（暫定）：鱸魚（水，1★）、海鹽（`cast_net`，調味）、蜂蜜（`forage_berries`，`sugar`，馴服 1）、野生根菜（`harvest_crops`，土，堅守 1）、堅殼栗（`forage_berries`，土，堅守 1）、火椒（`forage_berries`，火，抗冰 1）、冰薄荷（`forage_berries`，冰，冷卻 1）、疾風菇（`forage_fungi`，風，迅捷 1）、夜光菇（`forage_fungi`，影，會心 1）、松露（`forage_fungi`，土，豐收 2）、跳蟲（`set_trap`，雷，靈巧 1，`critter`）。季節限定採集物沿用 [planet-world.md#季節](planet-world.md#季節)。

## 7. 實作對照

- 規則層：`CuisineTraits`／`ProcessSpec`（`CuisineDefinitions.cs`）、`ProcessingRules`（加工）、`PotCookingRules`（§4 判定：`Resolve` 預覽、`TryCook` 扣料）。既有 `RecipeSpec` 即招牌料理，7 道既有料理 ID、投料與效果不變；`CookingRules.TryCook(recipe)` 保留為「帶入食譜」的捷徑。
- Schema：`ingredient.schema.json` 加 `role`、`tags`、`cook_heal_percent`、`effect`、`duration_add_ms`、`processed_from`／`process`；`category` 加 `fungi`、`critter`；`source` 加 `forage_fungi`。`food.schema.json` 的 `cooking_method` 加 `chill_brew`。新 ID 一律 `snake_case`、永不重用。
- 示範目錄目前寫在 `FoodCatalog.CreateDefault()`，尚未有 `Data/food/**` importer。
- 冒險手冊「生活技能」：加工清單、鍋（選方式 → 加入食材 → 預覽成品 → 料理）。玩家位於已發現的有人聚落內時視為聚落廚房。
- `chill_brew`、`freeze_prep`、`ferment_cure`、`smoke_dry`、`forage_fungi` 的持有物種尚為 P5 預留；在此之前只能在聚落廚房使用這些加工與料理方式（`forage_fungi` 尚無法採集）。
