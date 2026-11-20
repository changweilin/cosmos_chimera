# 生活技對應戰鬥效果（Field Utilities Combat Effects）

本文件定義全部 **72 項**生活技（36 主動技 Move＋36 被動技 Trait）對應的戰鬥效果規格。真實資料來源為 `Data/field-utilities/field-utilities.json`（schema：`Data/schemas/field-utility.schema.json`，由 `tools/validate-data` 驗證）；場外功能、器皿生效規則與屬性契合表見 [field-utilities.md](field-utilities.md)。

## 1. 設計與平衡規則

- **同級定位與星級分布（72 : 24 : 4）**：生活技的戰鬥效果與一般戰鬥主動技／被動技採相同數值基準，全 72 項依稀有度與戰術定位分為 **★ 52 項（72.2%）／★★ 17 項（23.6%）／★★★ 3 項（4.2%）**（主動 26/8/2、被動 26/9/1）。★★ 與 ★★★ 均附帶 `downgraded`（★ 版），副手提供時依 [creatures.md#技能星級](creatures.md#技能星級) 降格為 ★。
- **生活功能與屬性契合**：每項生活主動技的戰鬥屬性必屬於其 `IsElementAligned`（`aligned_elements`）之一；多屬性生活技指定最具代表性且兼顧十屬性分布的主屬性。陰陽判定依 [creatures.md#六大能力值](creatures.md#六大能力值)（陽＝切割／灼燒／穿刺等由外而內；陰＝震盪／重壓／電擊／寒凍／侵蝕等由內而外）。
- **四象限均衡（主動技 36 項）**：允許 4 項純輔助技（`kind: effect`，無陰陽屬性：`weather_control`、`campfire_revive`、`return_village`、`party_stealth`），其餘 **32 項**傷害／混合技精確均分為 **陽×近 8／陽×遠 8／陰×近 8／陰×遠 8**（各佔 25%）。
- **詞彙與前搖**：完全沿用 `common.schema.json` 封閉觸發／效果詞彙與 [combat.md#前搖](combat.md#前搖) 預設前搖（近戰 front/sweep/burst 300 ms、line/cone 450 ms、arc/burst(target) 600 ms、field 700 ms、self/ally 200 ms）。戰鬥執行期接線屬後續實作項目。

## 2. 生活主動技戰鬥效果（36 項，符籙卡片副手可提供）

| `utility` | 星 | 屬性 | 象限 | 形狀／射程(m) | 威力 / CD(s) | 命中 | 戰鬥效果（★★/★★★ 括號內為副手 ★ 版） |
|---|---:|---|---|---|---|---:|---|
| `hunt_ground` | ★ | 雷 | 陽×近 | line(dash) 3.0 | 42 / 2.4 | 95 | 敵雷抗 −12（4s） |
| `steal_item` | ★★ | 影 | 陽×近 | front 2.2 | 52 / 2.4 | 100 | 敵閃避 −18% 5s（★：P40 / 2.6s / −12% 4s） |
| `harvest_crops` | ★ | 木 | 陽×近 | sweep 2.4 | 48 / 2.0 | 95 | 純傷害橫掃 |
| `craft_smith_fire` | ★ | 火 | 陽×近 | sweep 2.3 | 54 / 3.0 | 95 | 敵火抗 −12（4.5s） |
| `craft_tailor_shadow` | ★ | 影 | 陽×近 | front 2.0 | 40 / 2.2 | 100 | 敵命中 −12（4s） |
| `craft_carpentry_earth` | ★ | 土 | 陽×近 | front 2.2 | 58 / 2.4 | 95 | 純傷害重劈 |
| `craft_weave_shadow` | ★ | 影 | 陽×近 | sweep 2.4 | 44 / 2.6 | 95 | 敵移速 −15%（4s） |
| `cook_fire` | ★ | 火 | 陽×近 | burst(self) 2.8 | 48 / 2.8 | 95 | 敵火抗 −12（4s） |
| `hunt_sky` | ★★ | 風 | 陽×遠 | line 22.0 | 72 / 3.4 | 100 | 敵閃避 −16% 5s（★：P56 / 3.6s / −10% 4s） |
| `fly` | ★★ | 光 | 陽×遠 | line(dash) 16.0 | 82 / 3.6 | 95 | 純傷害高空俯衝（★：P64 / 3.8s） |
| `fishing` | ★ | 冰 | 陽×遠 | line 16.0 | 46 / 2.8 | 95 | 敵移速 −15%（4s） |
| `cast_net` | ★ | 水 | 陽×遠 | arc 14.0 | 48 / 3.4 | 90 | 敵移速 −18%（4.5s） |
| `craft_carpentry_wind` | ★ | 風 | 陽×遠 | cone 14.0 | 60 / 3.0 | 95 | 純傷害扇形齊射 |
| `craft_weave_wind` | ★ | 風 | 陽×遠 | line 20.0 | 66 / 3.2 | 95 | 純傷害遠距風刃 |
| `craft_jewelcraft_ice` | ★ | 冰 | 陽×遠 | arc 18.0 | 52 / 3.4 | 95 | 敵冰抗 −12（4.5s） |
| `craft_tailor_light` | ★ | 光 | 陽×遠 | line 22.0 | 62 / 3.0 | 100 | 純傷害高命中光絲 |
| `open_obstacle` | ★★ | 金 | 陰×近 | front 2.4 | 78 / 3.6 | 95 | 敵金抗 −18 5s（★：P60 / 3.8s / −12 4s） |
| `mount` | ★ | 雷 | 陰×近 | line(dash) 3.2 | 56 / 2.4 | 95 | 純傷害突進電衝 |
| `swim` | ★ | 水 | 陰×近 | line(dash) 2.8 | 42 / 2.4 | 95 | 敵移速 −12%（4s） |
| `dive` | ★★ | 水 | 陰×近 | burst(self) 3.2 | 72 / 3.8 | 95 | 敵水抗 −16 5s（★：P56 / 4.0s / −10 4s） |
| `quick_escape` | ★ | 雷 | 陰×近 | burst(self) 2.6 | 36 / 2.4 | 100 | 敵移速 −20%（3.5s） |
| `craft_smelt_metal` | ★ | 金 | 陰×近 | burst(self) 2.6 | 62 / 2.8 | 95 | 純傷害震錘爆發 |
| `craft_smith_earth` | ★ | 土 | 陰×近 | front 2.2 | 54 / 3.0 | 95 | 敵失衡槽容量 −15%（5s） |
| `craft_enchant_fire` | ★ | 火 | 陰×近 | burst(self) 2.8 | 52 / 3.0 | 95 | 敵火抗 −14（4.5s） |
| `levitate` | ★★★ | 風 | 陰×遠 | field(6s) 18.0 | 16 / 11.0 | 95 | 敵移速 −22% 3s（★：P11 / 12.5s / −14% 2.5s） |
| `set_trap` | ★ | 土 | 陰×遠 | field(6s) 12.0 | 11 / 11.5 | 95 | 敵移速 −15%（3s） |
| `chill_brew` | ★★ | 冰 | 陰×遠 | burst(tgt) 15.0 | 66 / 4.0 | 95 | 敵全 CD +18% 5s（★：P50 / 4.2s / +10% 4s） |
| `craft_smelt_thunder` | ★ | 雷 | 陰×遠 | line 20.0 | 58 / 3.4 | 95 | 敵雷抗 −12（4.5s） |
| `craft_jewelcraft_earth` | ★ | 土 | 陰×遠 | burst(tgt) 14.0 | 70 / 3.8 | 90 | 純傷害晶爆 |
| `craft_dye_water` | ★ | 水 | 陰×遠 | cone 13.0 | 48 / 3.2 | 95 | 敵命中 −14（4.5s） |
| `craft_dye_light` | ★ | 光 | 陰×遠 | burst(tgt) 20.0 | 54 / 3.8 | 95 | 敵命中 −16（4.5s） |
| `craft_enchant_shadow` | ★ | 影 | 陰×遠 | field(5s) 16.0 | 12 / 12.0 | 95 | 敵影抗 −14（3s） |
| `weather_control` | ★★★ | 風 | 純輔助 | self 0 | 0 / 12.0 | 100 | 自身全 CD −24% 8s（★：13.0s / −14% 6s） |
| `campfire_revive` | ★★ | 火 | 純輔助 | ally 12.0 | 0 / 11.0 | 100 | 治療隊友 28% HP（★：12.0s / 18%） |
| `return_village` | ★ | 木 | 純輔助 | self 0 | 0 / 8.0 | 100 | 自身移速 +24%（5s） |
| `party_stealth` | ★★ | 影 | 純輔助 | self 0 | 0 / 9.0 | 100 | 自身閃避 +28% 5s（★：9.5s / +18% 4s） |

## 3. 生活被動技戰鬥效果（36 項，琉璃珠副手可提供）

| `utility` | 星 | 契合屬性 | 觸發條件 | 戰鬥效果（★★/★★★ 括號內為副手 ★ 版） |
|---|---:|---|---|---|
| `immune_laboratory` | ★ | 金 | `always` | 金抗 +14、命中 +8 |
| `immune_rainforest` | ★ | 木 | `on_summon` | 回復 9% 最大 HP |
| `immune_deep_sea` | ★ | 水 | `always` | 水抗 +14、失衡槽容量 +12% |
| `immune_volcano` | ★ | 火 | `on_hit_taken(fire, 5s)` | 火專精 +18 |
| `immune_cave` | ★ | 土 | `always` | 土抗 +14、格擋減免 +8 |
| `immune_ice_cap` | ★★ | 冰 | `always` | 冰抗 +20、水抗 +12（★：+14 / +6） |
| `immune_tower` | ★★ | 雷 | `on_move_used(thunder, 4s)` | 雷 CD −14%、移速 +12%（★：−8% / +6%） |
| `immune_space` | ★★ | 風 | `always` | 射程 +15%、閃避 +12%（★：+9% / +6%） |
| `immune_labyrinth` | ★★ | 影 | `on_ally_swap(6s)` | 閃避 +20%、爆率 +12（★：+12% / +6） |
| `immune_temple` | ★★★ | 光 | `on_hp_above(65%)` | 光專精 +24、全 CD −14%（★：+14 / −6%） |
| `scout_potential` | ★★ | 光/金/雷 | `on_summon(6s)` | 命中 +16、爆率 +12（★：+10 / +6） |
| `prevent_escape` | ★★ | 冰/影 | `on_move_used(ice, 4s)` | 射程 +14%、命中 +12（★：+8% / +6） |
| `magnet_pickup_double` | ★★ | 金/雷 | `on_hit_taken(any, 4s)` | 金專精 +10（3層）、失衡槽 +12%（★：+7×2層 / +6%） |
| `weather_ward` | ★★ | 冰/雷/水 | `always` | 冰／雷／水抗各 +12（★：各 +7） |
| `egg_hatch_boost` | ★★ | 火/光 | `on_hp_cross_below(40%)` | 回復 20% 最大 HP（★：12%） |
| `catch_rate_up` | ★ | 金/光/木 | `on_hp_below(50%)` | 命中 +15 |
| `drop_rate_up` | ★ | 土/金/影 | `on_move_used(earth, 4s)` | 爆傷 +18 |
| `forage_berries` | ★ | 木 | `on_ally_swap` | 回復 8% 最大 HP |
| `produce_milk` | ★ | 木/土 | `on_hp_cross_below(50%)` | 回復 12% 最大 HP |
| `culinary_prep` | ★ | 冰/木/金/水 | `on_summon(5s)` | 全 CD −12% |
| `gather_essence` | ★ | 土/冰/金 | `on_move_used(earth, 4s)` | 土專精 +10（2層） |
| `freeze_prep` | ★ | 冰 | `on_hit_taken(any, 4s)` | 火抗 +18 |
| `forage_fungi` | ★ | 影/土 | `on_hp_below(45%)` | 閃避 +18% |
| `ferment_cure` | ★ | 水/木/影 | `on_hit_taken(any, 5s)` | 水抗 +10、木抗 +10 |
| `smoke_dry` | ★ | 風/火 | `on_hp_above(70%)` | 移速 +14% |
| `ice_stride` | ★ | 冰/水 | `always` | 移速 +12% |
| `sense_treasure` | ★ | 金/雷/光 | `always` | 爆率 +9 |
| `camp_beacon` | ★ | 雷/火/金 | `on_ally_swap(5s)` | 格擋減免 +14 |
| `craft_smelt_fire` | ★ | 火 | `on_move_used(fire, 4s)` | 火專精 +16 |
| `craft_weave_wood` | ★ | 木 | `always` | 閃避 +10%、移速 +6% |
| `craft_tailor_wood` | ★ | 木 | `always` | 木抗 +12、格擋減免 +6 |
| `craft_smith_metal` | ★ | 金 | `always` | 格擋減免 +12、失衡槽容量 +8% |
| `craft_carpentry_wood` | ★ | 木 | `always` | 射程 +12% |
| `craft_jewelcraft_light` | ★ | 光 | `always` | 爆傷 +16 |
| `craft_dye_wood` | ★ | 木 | `on_hit_taken(any, 4s)` | 閃避 +16% |
| `craft_enchant_light` | ★ | 光 | `on_move_used(light, 4s)` | 光 CD −12% |
