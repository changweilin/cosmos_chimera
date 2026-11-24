# 生活技對應戰鬥效果（Field Utilities Combat Effects）

本文件定義全部 **72 項**生活技（36 主動技 Move＋36 被動技 Trait）對應的戰鬥效果規格。真實資料來源為 `Data/field-utilities/field-utilities.json`（schema：`Data/schemas/field-utility.schema.json`，由 `tools/validate-data` 驗證）；場外功能、器皿生效規則與屬性契合表見 [field-utilities.md](field-utilities.md)，十元素異常狀態定義見 [status-ailments.md](status-ailments.md)。

## 1. 設計與多維平衡規則

- **同級定位與星級分布（72 : 24 : 4）**：與一般戰鬥技同級，全 72 項分為 **★ 52（72.2%）／★★ 17（23.6%）／★★★ 3（4.2%）**（主動 26/8/2、被動 26/9/1）。★★ 約 **1.18×**、★★★ 約 **1.35×** 預算，且均附 `downgraded`（★ 版 1.00×）供副手降格。
- **主動技 eDPS 預算公式**（對齊 `Data/_demo/moves/` 基準；`eDPS = (power / cd_s) × (accuracy / 100)`，場地以 `ceil(field_duration_ms / 1000)` 次結算計總 eDPS）：
  - **射程 × 幾何／目標型態基準（★ 純傷害）**：
    - 近攻 `melee`：單體窄前方 `front` **32.0** ＞ 扇形 `sweep` **27.0** ＞ 突進位移 `line+dash` **26.0** ＞ 周圍 360° `burst(caster)` **23.5**
    - 中距 `mid`：直線 `line` **21.0** ＞ 扇形 `cone` **18.5** ＞ 中距突進追擊 `line+dash` **17.5** ＞ 拋物線落點 `arc` **16.5** ＞ 目標點爆炸 `burst(target)` **15.5** ＞ 持續地面場地 `field` **5.2**
    - 遠距 `long`：直線 `line` **14.5** ＞ 扇形 `cone` **12.8** ＞ 拋物線落點 `arc` **12.0** ＞ 目標點爆炸 `burst(target)` **11.2** ＞ 持續地面場地 `field` **5.8**
  - **附加狀態／異常／控制折算**：純傷害 **1.00×**；軟性減益（降抗／命中／閃避）**0.74–0.80×**；異常狀態累積（`apply_ailment` 單發 45–55 / ★★ 65–70，或場地每 tick 28）與控制／節奏干擾採 **0.66–0.76×**（強控／高累積如 `frostbite`／`weighted`／`shock`／`sunder` 扣較多傷害預算）。
- **生活功能、屬性與異常匹配**：主動技屬性必屬 `IsElementAligned`；陰陽依 [creatures.md#六大能力值](creatures.md#六大能力值) 判定。36 主動技含 **4 項純輔助**，其餘 **32 項**精確均分為 **陽×近 8／陽×遠 8／陰×近 8／陰×遠 8**；其中 **14 項**高度契合之招式附帶異常狀態累積（**十屬性各至少 1 招**），其餘保留純傷害或數值減益；3 項被動技附帶 `ailment_resist_mod`。
- **被動技觸發覆蓋率校準**：常駐 `always` 權重最保守，條件／風險觸發依觸發門檻給予對等加值。戰鬥執行期接線屬後續實作項目。

## 2. 生活主動技戰鬥效果（36 項，符籙卡片副手可提供）

| `utility` | 星 | 屬性 | 象限 | 型態／射程(m) | 威力 / CD(s) | 命中 | eDPS | 戰鬥效果（★★/★★★ 括號為副手 ★ 版） |
|---|---:|---|---|---|---:|---:|---:|---|
| `craft_carpentry_earth` | ★ | 土 | 陽×近 | front 2.2 | 42 / 1.3 | 95 | 30.7 | 純傷害單體重劈 |
| `harvest_crops` | ★ | 木 | 陽×近 | sweep 2.4 | 48 / 2.3 | 95 | 19.8 | 異常：**纏毒 `toxin` +45** |
| `steal_item` | ★★ | 影 | 陽×近 | front 2.2 | 46 / 1.6 | 100 | 28.8 | 敵閃避 −18% 5s（★：P38 / 1.6s / −12% 4s） |
| `craft_tailor_shadow` | ★ | 影 | 陽×近 | front 2.0 | 36 / 1.5 | 100 | 24.0 | 敵命中 −12（4s） |
| `hunt_ground` | ★ | 雷 | 陽×近 | line+dash 3.0 | 48 / 2.3 | 95 | 19.8 | 突進追擊；敵雷抗 −12（4s） |
| `craft_smith_fire` | ★ | 火 | 陽×近 | sweep 2.3 | 54 / 2.5 | 95 | 20.5 | 敵火抗 −14（4.5s） |
| `craft_weave_shadow` | ★ | 影 | 陽×近 | sweep 2.4 | 46 / 2.3 | 95 | 19.0 | 敵移速 −15%（4s） |
| `cook_fire` | ★ | 火 | 陽×近 | burst(self) 2.8 | 47 / 2.6 | 95 | 17.2 | 周圍爆燃；異常：**灼燒 `burn` +50** |
| `fly` | ★★ | 光 | 陽×遠 | line+dash 16.0 | 76 / 3.5 | 95 | 20.6 | 純傷害中距俯衝追擊（★：P64 / 3.5s） |
| `craft_carpentry_wind` | ★ | 風 | 陽×遠 | cone 14.0 | 58 / 3.0 | 95 | 18.4 | 純傷害中距扇形齊射 |
| `fishing` | ★ | 冰 | 陽×遠 | line 16.0 | 44 / 2.8 | 95 | 14.9 | 敵移速 −15%（4s） |
| `cast_net` | ★ | 水 | 陽×遠 | arc 14.0 | 44 / 3.4 | 90 | 11.6 | 落點拋網；敵移速 −18%（4.5s） |
| `craft_weave_wind` | ★ | 風 | 陽×遠 | line 20.0 | 52 / 3.4 | 95 | 14.5 | 純傷害遠距直線風刃 |
| `craft_tailor_light` | ★ | 光 | 陽×遠 | line 22.0 | 46 / 3.2 | 100 | 14.4 | 純傷害高命中遠距光絲 |
| `hunt_sky` | ★★ | 風 | 陽×遠 | line 22.0 | 50 / 3.8 | 100 | 13.2 | 敵閃避 −16% 5s（★：P42 / 3.8s / −10% 4s） |
| `craft_jewelcraft_ice` | ★ | 冰 | 陽×遠 | arc 18.0 | 34 / 3.6 | 95 | 9.0 | 遠距落點冰稜；異常：**霜凍 `frostbite` +45** |
| `craft_smelt_metal` | ★ | 金 | 陰×近 | burst(self) 2.6 | 62 / 2.5 | 95 | 23.6 | 純傷害周圍震錘爆發 |
| `mount` | ★ | 雷 | 陰×近 | line+dash 3.2 | 58 / 2.1 | 95 | 26.2 | 純傷害近戰突進電衝 |
| `open_obstacle` | ★★ | 金 | 陰×近 | front 2.4 | 77 / 2.7 | 95 | 27.1 | 異常：**裂甲 `sunder` +65**（★：P65 / 2.7s / +45） |
| `craft_smith_earth` | ★ | 土 | 陰×近 | front 2.2 | 64 / 2.8 | 95 | 21.7 | 異常：**重壓 `weighted` +55** |
| `swim` | ★ | 水 | 陰×近 | line+dash 2.8 | 47 / 2.3 | 95 | 19.4 | 突進追擊；異常：**潮濕 `soaked` +50** |
| `dive` | ★★ | 水 | 陰×近 | burst(self) 3.2 | 72 / 3.2 | 95 | 21.4 | 周圍深壓；敵水抗 −16 5s（★：P60 / 3.2s / −10 4s） |
| `quick_escape` | ★ | 雷 | 陰×近 | burst(self) 2.6 | 44 / 2.7 | 100 | 16.3 | 周圍麻痺脈衝；異常：**感電 `shock` +50** |
| `craft_enchant_fire` | ★ | 火 | 陰×近 | burst(self) 2.8 | 49 / 2.8 | 95 | 16.6 | 周圍炎紋內爆；異常：**灼燒 `burn` +55** |
| `craft_jewelcraft_earth` | ★ | 土 | 陰×遠 | burst(tgt) 14.0 | 68 / 4.0 | 90 | 15.3 | 純傷害中距目標點晶爆 |
| `chill_brew` | ★★ | 冰 | 陰×遠 | burst(tgt) 15.0 | 54 / 4.2 | 95 | 12.2 | 異常：**霜凍 `frostbite` +70**（★：P46 / 4.2s / +50） |
| `craft_dye_water` | ★ | 水 | 陰×遠 | cone 13.0 | 47 / 3.2 | 95 | 14.0 | 中距扇形染霧；異常：**潮濕 `soaked` +55** |
| `set_trap` | ★ | 土 | 陰×遠 | field(6s) 12.0 | 8 / 11.5 | 95 | 4.0 | 地面持續陷阱（6 tick）；敵移速 −15%（3s） |
| `craft_smelt_thunder` | ★ | 雷 | 陰×遠 | line 20.0 | 40 / 3.6 | 95 | 10.6 | 遠距電弧；異常：**感電 `shock` +50** |
| `craft_dye_light` | ★ | 光 | 陰×遠 | burst(tgt) 20.0 | 36 / 4.2 | 95 | 8.1 | 遠距閃光；異常：**炫盲 `dazzle` +55** |
| `craft_enchant_shadow` | ★ | 影 | 陰×遠 | field(5s) 16.0 | 11 / 12.5 | 95 | 4.2 | 遠距咒陣（5 tick）；異常：**蝕影 `eclipse` +28/tick** |
| `levitate` | ★★★ | 風 | 陰×遠 | field(6s) 18.0 | 11 / 11.5 | 95 | 5.5 | 失重氣域（6 tick）；異常：**紊流 `turbulent` +28/tick**（★：P8 / 12.0s / +20） |
| `weather_control` | ★★★ | 風 | 純輔助 | self 0 | 0 / 12.0 | 100 | — | 自身全 CD −24% 8s（★：13.0s / −14% 6s） |
| `campfire_revive` | ★★ | 火 | 純輔助 | ally 12.0 | 0 / 12.0 | 100 | — | 治療隊友 28% HP（★：12.0s / 18%） |
| `return_village` | ★ | 木 | 純輔助 | self 0 | 0 / 8.5 | 100 | — | 自身移速 +22%（5s） |
| `party_stealth` | ★★ | 影 | 純輔助 | self 0 | 0 / 9.0 | 100 | — | 自身閃避 +28% 5s（★：9.5s / +18% 4s） |

## 3. 生活被動技戰鬥效果（36 項，琉璃珠副手可提供）

| `utility` | 星 | 契合屬性 | 觸發條件 | 戰鬥效果（★★/★★★ 括號內為副手 ★ 版） |
|---|---:|---|---|---|
| `immune_laboratory` | ★ | 金 | `always` | 金抗 +14、命中 +8 |
| `immune_rainforest` | ★ | 木 | `on_summon` | 回復 9% 最大 HP |
| `immune_deep_sea` | ★ | 水 | `always` | 水抗 +14、失衡槽容量 +12% |
| `immune_volcano` | ★ | 火 | `on_hit_taken(fire, 5s)` | 火專精 +22（單屬性受擊高補償） |
| `immune_cave` | ★ | 土 | `always` | 土抗 +14、格擋減免 +8 |
| `immune_ice_cap` | ★★ | 冰 | `always` | 冰抗 +20、水抗 +12（★：+14 / +6） |
| `immune_tower` | ★★ | 雷 | `on_move_used(thunder, 4s)` | 雷 CD −14%、移速 +12%（★：−8% / +6%） |
| `immune_space` | ★★ | 風 | `always` | 射程 +15%、閃避 +12%（★：+9% / +6%） |
| `immune_labyrinth` | ★★ | 影 | `on_ally_swap(6s)` | 閃避 +20%、爆率 +12（★：+12% / +6） |
| `immune_temple` | ★★★ | 光 | `on_hp_above(65%)` | 光專精 +24、全 CD −14%（★：+14 / −6%） |
| `scout_potential` | ★★ | 光/金/雷 | `on_summon(6s)` | 命中 +16、爆率 +12（★：+10 / +6） |
| `prevent_escape` | ★★ | 冰/影 | `on_move_used(ice, 4s)` | 射程 +14%、命中 +12（★：+8% / +6） |
| `magnet_pickup_double` | ★★ | 金/雷 | `on_hit_taken(any, 4s)` | 金專精 +10（3層）、失衡槽 +12%（★：+7×2層 / +6%） |
| `weather_ward` | ★★ | 冰/雷/水 | `always` | 冰／雷／水抗各 +10、**全異常抗性 +20**（★：各 +6 / 全異常 +12） |
| `egg_hatch_boost` | ★★ | 火/光 | `on_hp_cross_below(40%)` | 回復 20% 最大 HP（★：12%） |
| `catch_rate_up` | ★ | 金/光/木 | `on_hp_below(50%)` | 命中 +15 |
| `drop_rate_up` | ★ | 土/金/影 | `on_move_used(earth, 4s)` | 爆傷 +18 |
| `forage_berries` | ★ | 木 | `on_ally_swap` | 回復 8% 最大 HP |
| `produce_milk` | ★ | 木/土 | `on_hp_cross_below(50%)` | 回復 12% 最大 HP |
| `culinary_prep` | ★ | 冰/木/金/水 | `on_summon(5s)` | 全 CD −12% |
| `gather_essence` | ★ | 土/冰/金 | `on_move_used(earth, 4s)` | 土專精 +10（2層） |
| `freeze_prep` | ★ | 冰 | `always` | **灼燒抗性 +35**、火抗 +10 |
| `forage_fungi` | ★ | 影/土 | `on_hp_below(45%)` | 閃避 +18% |
| `ferment_cure` | ★ | 水/木/影 | `always` | **纏毒抗性 +35**、水抗 +8 |
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
