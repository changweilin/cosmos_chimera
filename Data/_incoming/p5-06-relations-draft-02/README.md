# P5-06 關係表建議與連通性分析（草稿 02）

對象：`p5-03-creatures-draft-02`（第二批擴充 10 線 30 型態、既有線內邊 20），並橋接首批 30 型態（`p2-08-starters-draft-01` 御三家 3 線 9 型態＋`p5-03-creatures-draft-01` 7 線 21 型態＋`p5-06-relations-draft-01` 跨線邊 22），全池合計 **20 條進化線、60 個型態**。`relations/edges.json` 僅收錄本批新增之 **30 條跨線關係邊**（批內跨線 20 條＋跨批橋接 10 條；不含既有 `evolution_family` 與 `draft-01` 邊）。

## 1. 各類型邊數與度數統計

| 關係種類 (`kind`) | 既有邊數（含 draft-01） | 本批新增（批內 / 跨批） | 60 型態聯集總邊數 | 依據準則 |
|---|---:|---:|---:|---|
| `evolution_family` | 40 | 0 (0 / 0) | 40 | 維持各線內部幼年→少年→成年進化邊 |
| `myth_kin` | 1 | 2 (1 / 1) | 3 | 具真實共同文化／語族史詩或神廟造像源流 |
| `rival_symbiosis` | 7 | 10 (6 / 4) | 17 | 同生態域競獵、林鳥覓蟲、水火對峙或古廟叢林共生 |
| `tool_kin` | 7 | 10 (7 / 3) | 17 | 雙端皆具科學觀測儀、木造船艦、金屬獸甲或建築石柱雕像構造 |
| `mentor` | 7 | 8 (6 / 2) | 15 | 他線成年長者引導幼年學徒（突進、固守、控距、續戰；對齊 `ai_style`） |
| **合計** | **62** | **30 (20 / 10)** | **92** | 60 型態總度數全體介於 **2–4**（2 度 4 隻／3 度 48 隻／4 度 8 隻，平均 3.07） |

- **單批獨立性與全池完整性雙重保證**：
  - 僅看第二批 30 型態（20 條進化邊＋20 條批內跨線邊）：度數全體介於 **2–4**（2 度 12／3 度 16／4 度 2，平均 2.67），10 線商圖為單一 2-邊連通分量（無橋邊）。
  - 併入全池 60 型態（92 條邊）：60/60 型態皆至少有 1 條跨線邊（補足首批 `moon_shade_wolf`、`earth_gilded_warden`、`wind_flute_cruiser` 三隻少年期零跨線邊缺口），且 **20/20 隻幼年型態皆至少擁有 1 條 `juvenile ↔ juvenile` 跨線邊**，在 P5-08 N3 方案 c（`副手階段 ≤ 主力階段`）下 100% 幼年主力皆有合法同階副手可配。

## 2. 第二批 30 型態總度數表（既有進化邊 + 本批新增）

| 進化線 | 型態 ID | 階段 | 既有 | 新增 | 總度數 | 新增跨線鄰點（種類） |
|---|---|---|---:|---:|---:|---|
| `line_metal_citadelbull` | `metal_gate_calf` | 幼年 | 1 | 2 | 3 | `earth_temple_tuskbeast`(mentor), `shadow_tundra_foal`(tool_kin) |
| `line_metal_citadelbull` | `metal_winged_bull` | 少年 | 2 | 1 | 3 | `earth_pillar_chimera`(tool_kin) |
| `line_metal_citadelbull` | `metal_citadel_colossus` | 成年 | 1 | 2 | 3 | `light_foil_spark`(mentor), `shadow_iron_cavalier`(tool_kin) |
| `line_wood_crownvine` | `wood_betel_sprout` | 幼年 | 1 | 2 | 3 | `earth_lotus_cub`(rival_symbiosis), `water_wave_elver`(rival_symbiosis) |
| `line_wood_crownvine` | `wood_garland_serpent` | 少年 | 2 | 2 | 4 | `earth_pillar_chimera`(myth_kin), `fire_ember_crocodilian`(rival_symbiosis) |
| `line_wood_crownvine` | `wood_lotus_wyrm` | 成年 | 1 | 2 | 3 | `earth_temple_tuskbeast`(rival_symbiosis), `water_ivory_calf`(mentor) |
| `line_water_whalemaelstrom` | `water_ivory_calf` | 幼年 | 1 | 2 | 3 | `fire_spark_barkling`(rival_symbiosis), `wood_lotus_wyrm`(mentor) |
| `line_water_whalemaelstrom` | `water_harpoon_cetacean` | 少年 | 2 | 1 | 3 | `ice_hull_cutter`(tool_kin) |
| `line_water_whalemaelstrom` | `water_abyssal_leviathan` | 成年 | 1 | 1 | 2 | `wind_pelagic_soarer`(tool_kin) |
| `line_fire_baobabhearth` | `fire_spark_barkling` | 幼年 | 1 | 2 | 3 | `water_ivory_calf`(rival_symbiosis), `wood_canopy_regent`(mentor) |
| `line_fire_baobabhearth` | `fire_ember_crocodilian` | 少年 | 2 | 1 | 3 | `wood_garland_serpent`(rival_symbiosis) |
| `line_fire_baobabhearth` | `fire_hearth_monolith` | 成年 | 1 | 1 | 2 | `thunder_gobi_voltwyrm`(rival_symbiosis) |
| `line_earth_pillarbeast` | `earth_lotus_cub` | 幼年 | 1 | 2 | 3 | `earth_auric_sovereign`(mentor), `wood_betel_sprout`(rival_symbiosis) |
| `line_earth_pillarbeast` | `earth_pillar_chimera` | 少年 | 2 | 2 | 4 | `metal_winged_bull`(tool_kin), `wood_garland_serpent`(myth_kin) |
| `line_earth_pillarbeast` | `earth_temple_tuskbeast` | 成年 | 1 | 2 | 3 | `metal_gate_calf`(mentor), `wood_lotus_wyrm`(rival_symbiosis) |
| `line_ice_driftvessel` | `ice_sled_buoy` | 幼年 | 1 | 2 | 3 | `light_foil_spark`(tool_kin), `wind_reed_skiff`(tool_kin) |
| `line_ice_driftvessel` | `ice_hull_cutter` | 少年 | 2 | 2 | 4 | `water_harpoon_cetacean`(tool_kin), `wind_flute_cruiser`(tool_kin) |
| `line_ice_driftvessel` | `ice_polar_icebreaker` | 成年 | 1 | 2 | 3 | `light_radiant_cyclotron`(tool_kin), `wind_crossbow_fledgling`(mentor) |
| `line_wind_mistwing` | `wind_crossbow_fledgling` | 幼年 | 1 | 2 | 3 | `ice_frost_fledgling`(rival_symbiosis), `ice_polar_icebreaker`(mentor) |
| `line_wind_mistwing` | `wind_bergglider_gull` | 少年 | 2 | 1 | 3 | `moon_shade_wolf`(rival_symbiosis) |
| `line_wind_mistwing` | `wind_pelagic_soarer` | 成年 | 1 | 1 | 2 | `water_abyssal_leviathan`(tool_kin) |
| `line_thunder_redburrower` | `thunder_dune_larva` | 幼年 | 1 | 2 | 3 | `light_radiant_cyclotron`(mentor), `wood_fern_chick`(rival_symbiosis) |
| `line_thunder_redburrower` | `thunder_crimson_crawler` | 少年 | 2 | 1 | 3 | `shadow_reverb_charger`(rival_symbiosis) |
| `line_thunder_redburrower` | `thunder_gobi_voltwyrm` | 成年 | 1 | 2 | 3 | `fire_hearth_monolith`(rival_symbiosis), `shadow_tundra_foal`(mentor) |
| `line_light_foilprism` | `light_foil_spark` | 幼年 | 1 | 2 | 3 | `ice_sled_buoy`(tool_kin), `metal_citadel_colossus`(mentor) |
| `line_light_foilprism` | `light_orbit_beacon` | 少年 | 2 | 1 | 3 | `earth_gilded_warden`(tool_kin) |
| `line_light_foilprism` | `light_radiant_cyclotron` | 成年 | 1 | 2 | 3 | `ice_polar_icebreaker`(tool_kin), `thunder_dune_larva`(mentor) |
| `line_shadow_ironsteed` | `shadow_tundra_foal` | 幼年 | 1 | 2 | 3 | `metal_gate_calf`(tool_kin), `thunder_gobi_voltwyrm`(mentor) |
| `line_shadow_ironsteed` | `shadow_reverb_charger` | 少年 | 2 | 1 | 3 | `thunder_crimson_crawler`(rival_symbiosis) |
| `line_shadow_ironsteed` | `shadow_iron_cavalier` | 成年 | 1 | 2 | 3 | `metal_citadel_colossus`(tool_kin), `star_skyhoof_steed`(myth_kin) |

### 跨批橋接更新之首批 10 型態（度數皆維持 ≤ 4）

| 進化線 | 型態 ID | 階段 | 原度數 | 新增 | 新總度數 | 新增跨批鄰點（種類） |
|---|---|---|---:|---:|---:|---|
| `starter_moon_line` | `moon_shade_wolf` | 少年 | 2 | 1 | 3 | `wind_bergglider_gull`(rival_symbiosis) |
| `starter_star_line` | `star_skyhoof_steed` | 成年 | 3 | 1 | 4 | `shadow_iron_cavalier`(myth_kin) |
| `line_wood_vinecrest` | `wood_fern_chick` | 幼年 | 2 | 1 | 3 | `thunder_dune_larva`(rival_symbiosis) |
| `line_wood_vinecrest` | `wood_canopy_regent` | 成年 | 2 | 1 | 3 | `fire_spark_barkling`(mentor) |
| `line_water_tidecraft` | `water_wave_elver` | 幼年 | 3 | 1 | 4 | `wood_betel_sprout`(rival_symbiosis) |
| `line_earth_gildedgolem` | `earth_gilded_warden` | 少年 | 2 | 1 | 3 | `light_orbit_beacon`(tool_kin) |
| `line_earth_gildedgolem` | `earth_auric_sovereign` | 成年 | 2 | 1 | 3 | `earth_lotus_cub`(mentor) |
| `line_ice_frostbeast` | `ice_frost_fledgling` | 幼年 | 2 | 1 | 3 | `wind_crossbow_fledgling`(rival_symbiosis) |
| `line_wind_fluteglider` | `wind_reed_skiff` | 幼年 | 2 | 1 | 3 | `ice_sled_buoy`(tool_kin) |
| `line_wind_fluteglider` | `wind_flute_cruiser` | 少年 | 2 | 1 | 3 | `ice_hull_cutter`(tool_kin) |

## 3. 連通性與無橋邊證明（20 線商圖生成樹）

1. **第二批 10 線內部生成樹（9 條批內邊）**：
   - `line_metal_citadelbull` — `line_earth_pillarbeast`：`(earth_pillar_chimera, metal_winged_bull)` [`tool_kin`]
   - `line_earth_pillarbeast` — `line_wood_crownvine`：`(earth_pillar_chimera, wood_garland_serpent)` [`myth_kin`]
   - `line_wood_crownvine` — `line_fire_baobabhearth`：`(fire_ember_crocodilian, wood_garland_serpent)` [`rival_symbiosis`]
   - `line_fire_baobabhearth` — `line_thunder_redburrower`：`(fire_hearth_monolith, thunder_gobi_voltwyrm)` [`rival_symbiosis`]
   - `line_thunder_redburrower` — `line_shadow_ironsteed`：`(shadow_reverb_charger, thunder_crimson_crawler)` [`rival_symbiosis`]
   - `line_thunder_redburrower` — `line_light_foilprism`：`(light_radiant_cyclotron, thunder_dune_larva)` [`mentor`]
   - `line_light_foilprism` — `line_ice_driftvessel`：`(ice_polar_icebreaker, light_radiant_cyclotron)` [`tool_kin`]
   - `line_ice_driftvessel` — `line_water_whalemaelstrom`：`(ice_hull_cutter, water_harpoon_cetacean)` [`tool_kin`]
   - `line_water_whalemaelstrom` — `line_wind_mistwing`：`(water_abyssal_leviathan, wind_pelagic_soarer)` [`tool_kin`]
2. **第一批 ↔ 第二批橋接**：由 10 條跨批邊將第二批 8 條線分別連往第一批 7 條線，使全池 20 線商圖滿足 **2-邊連通（無任何橋邊，刪去任一跨線邊仍保持 60 型態全圖連通）**。

## 4. 考據邊界與副手平衡分析

- **`myth_kin` 嚴格篩選（2 條）**：
  1. `(shadow_iron_cavalier, star_skyhoof_steed)`：薩哈族（雅庫特）《奧隆霍》英雄史詩黑駒與哈薩克圖爾帕爾天馬同屬突厥語族口傳英雄史詩神駒傳統（Reichl 1992）。
  2. `(earth_pillar_chimera, wood_garland_serpent)`：南印度泰米爾神廟石柱雅利與高棉七頭那伽花冠同屬印度化神廟建築守護造像傳統（Michell 1995; Chandler 2008）。
  3. 備註：`(water_abyssal_leviathan, wind_pelagic_soarer)` 雖有近代英語海洋文學典故淵源（梅爾維爾《白鯨記》第 42 章明引柯勒律治《老水手行》信天翁），但因非傳統神話同源，優先依雙端「船桅殘骨斷叉 × 古帆船雙桅翼骨與十字弩尾舵」歸入 `tool_kin`。
- **御三家同主屬性隔離維持成立**：全池 60 型態中，御三家日／月／星三線仍**零直接跨線邊**連往同主屬性他線（`line_light_foilprism`、`line_shadow_ironsteed`、`line_thunder_redburrower`）。
- **副專精與跨線協同警示（P5-08 N3 方案 c `副手階段 ≤ 主力階段` 下）**：
  - `(fire_hearth_monolith, thunder_gobi_voltwyrm)`：具 `fire: 15` 副專精的戈壁電漿巨蟲可裝備 `fire_hearth_monolith` 符籙取得遠距爆發 `fire_caldera_burst`（威力 115），同時吃火副專精並反制唯一弱點木屬（`wood: -40`）。
  - `(light_radiant_cyclotron, thunder_dune_larva)`：具 `thunder: 15` 副專精的遠距磁環聖像可裝備幼年 `thunder_dune_larva` 符籙（`thunder_pulse_ring` 近身爆發）或琉璃珠（`trait_thunder_fulgur_reach` 雷招延長射程 +15%）。
  - `(water_ivory_calf, wood_lotus_wyrm)`：具 `water: 15` 副專精的水林巨龍可借幼年白鯨的 `water_cascade_jab` 或 `trait_water_cleansing_tide`（跌破 30% HP 回血 12%），疊加自身木招回血形成雙重續戰。
- **文化敏感度**：本批涉及原型皆為 `sensitivity: "ok"`，未觸及任何 `review` 條目。
