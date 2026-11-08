# P5-03 宙獸批次草稿 01 (p5-03-creatures-draft-01)

FLASH 產出之 7 條完整三段進化線（共 21 隻宙獸）初稿。包含 `creatures/creatures.json` 與 `relations/edges.json`。
嚴格遵循 `Data/schemas/creature.schema.json`、`relation.schema.json`、`Data/balance.json` 之 `stage_bands` 數值規範，以及 `docs/design/creature-concept-pipeline.md` 文化原型意象準則。

---

## 1. 七條進化線總覽

| line_id | 主要屬性 | 地區 | 文化原型 ID | 外觀基底 (概念公式) | 戰鬥定位 | 精通招式（幼年 / 少年 / 成年） | 共用特性 |
|---|---|---|---|---|---|---|---|
| `line_metal_gearclock` | metal | 西亞/中東 | `arch_al_jazari` | 器械 (machine) | 前排坦克（近戰→遠距突進） | `metal_needle_thrust` (30) / `metal_iron_cleave` (60) / `metal_lance_charge` (110) | `trait_metal_dense_alloy` |
| `line_wood_vinecrest` | wood | 東南亞 | `arch_sarimanok` | 生物 (creature) | 中排輸出，上場自我治療 | `wood_briar_dash` (35) / `wood_root_spear` (85) / `wood_canopy_crush` (105) | `trait_wood_photosynthesis` |
| `line_water_tidecraft` | water | 大洋洲 | `arch_edai_siabo_lakatoi` | 生物 × 載具 | 中遠距輸出，連續水招縮 CD | `water_crest_slicer` (40) / `water_torrent_burst` (75) / `water_deluge_crater` (115) | `trait_water_torrent_flow` |
| `line_fire_blazelion` | fire | 非洲 | `arch_apedemak` | 生物 (creature) | 近戰→突進→遠距重砲 | `fire_ember_lash` (35) / `fire_blaze_charge` (65) / `fire_inferno_cataclysm` (120) | `trait_fire_thermal_recharge` |
| `line_earth_gildedgolem` | earth | 美洲 | `arch_el_dorado_muisca` | 人形 (humanoid) | 中前排，受擊提高土專精 | `earth_pebble_shot` (30) / `earth_seismic_slam` (60) / `earth_rampart_rush` (100) | `trait_earth_tectonic_rebound` |
| `line_ice_frostbeast` | ice | 中亞/北亞 | `arch_pazyryk_antlered_griffin` | 生物 × 器械 | 中遠距輸出，上場暫提冰抗 | `ice_shard_flurry` (35) / `ice_glacier_lance` (85) / `ice_blizzard_vortex` (110) | `trait_ice_flash_freeze` |
| `line_wind_fluteglider` | wind | 南亞 | `arch_heer_ranjha` | 載具 (vehicle) | 高敏捷游擊，換手上場加速 | `wind_gale_claw` (30) / `wind_vacuum_blade` (80) / `wind_tempest_sphere` (105) | `trait_wind_tailwind_glide` |

---

## 2. 21 隻宙獸詳細能力與數值表

依據 `Data/balance.json` 規定：
- 幼年（juvenile）：總點數 100，單項能力值範圍 10–50，專精為主要屬性 15，抗性本屬性 +20 / 弱點屬性 -20。
- 少年（adolescent）：總點數 200，單項能力值範圍 25–100，專精為主要屬性 25，抗性本屬性 +35 / 弱點屬性 -30。
- 成年（adult）：總點數 400，單項能力值範圍 50–200，專精為主要屬性 40（金、水、冰三線加 15 點次要專精），抗性本屬性 +50 / 弱點屬性 -40。
- 各項能力值隨階段嚴格單調不減（$S_{\text{adult}} \ge S_{\text{adolescent}} \ge S_{\text{juvenile}}$）。

| id | 階段 | 體型 | CON | STR | SPI | AGI | 總計 | 專精 (Mastery) | 抗性 (Resist，非零項，由高到低) | 精通招式 (Signature Move) |
|---|---|---|---|---|---|---|---|---|---|---|
| `metal_clock_chick` | 幼年 | S | 40 | 28 | 18 | 14 | 100 | metal: 15 | metal +20, earth +10, wood +5, fire -20 | `metal_needle_thrust` (30) |
| `metal_gear_strider` | 少年 | M | 80 | 55 | 37 | 28 | 200 | metal: 25 | metal +35, earth +15, wood +10, fire -30 | `metal_iron_cleave` (60) |
| `metal_chronos_engine` | 成年 | L | 165 | 110 | 70 | 55 | 400 | metal: 40, earth: 15 | metal +50, earth +15, wood +10, fire -40 | `metal_lance_charge` (110) |
| `wood_fern_chick` | 幼年 | S | 28 | 16 | 40 | 16 | 100 | wood: 15 | wood +20, water +10, earth +5, metal -20 | `wood_briar_dash` (35) |
| `wood_plume_peafowl` | 少年 | M | 55 | 32 | 81 | 32 | 200 | wood: 25 | wood +35, water +15, earth +10, metal -30 | `wood_root_spear` (85) |
| `wood_canopy_regent` | 成年 | L | 110 | 65 | 160 | 65 | 400 | wood: 40 | wood +50, water +15, earth +10, metal -40 | `wood_canopy_crush` (105) |
| `water_wave_elver` | 幼年 | S | 20 | 38 | 18 | 24 | 100 | water: 15 | water +20, fire +10, ice +5, thunder -20 | `water_crest_slicer` (40) |
| `water_skiff_eel` | 少年 | M | 40 | 75 | 35 | 50 | 200 | water: 25 | water +35, fire +15, ice +10, thunder -30 | `water_torrent_burst` (75) |
| `water_catamaran_serpent` | 成年 | L | 80 | 150 | 70 | 100 | 400 | water: 40, wind: 15 | water +50, fire +15, ice +10, thunder -40 | `water_deluge_crater` (115) |
| `fire_ember_cub` | 幼年 | S | 30 | 40 | 12 | 18 | 100 | fire: 15 | fire +20, wood +10, metal +5, water -20 | `fire_ember_lash` (35) |
| `fire_mane_stalker` | 少年 | M | 60 | 80 | 25 | 35 | 200 | fire: 25 | fire +35, wood +15, metal +10, water -30 | `fire_blaze_charge` (65) |
| `fire_pyre_monarch` | 成年 | L | 120 | 155 | 55 | 70 | 400 | fire: 40 | fire +50, wood +15, metal +10, water -40 | `fire_inferno_cataclysm` (120) |
| `earth_pebble_pawn` | 幼年 | S | 36 | 22 | 30 | 12 | 100 | earth: 15 | earth +20, thunder +10, fire +5, wind -20 | `earth_pebble_shot` (30) |
| `earth_gilded_warden` | 少年 | M | 72 | 44 | 59 | 25 | 200 | earth: 25 | earth +35, thunder +15, fire +10, wind -30 | `earth_seismic_slam` (60) |
| `earth_auric_sovereign` | 成年 | L | 145 | 85 | 120 | 50 | 400 | earth: 40 | earth +50, thunder +15, fire +10, wind -40 | `earth_rampart_rush` (100) |
| `ice_frost_fledgling` | 幼年 | S | 26 | 24 | 20 | 30 | 100 | ice: 15 | ice +20, water +10, wind +5, earth -20 | `ice_shard_flurry` (35) |
| `ice_rime_strider` | 少年 | M | 52 | 48 | 40 | 60 | 200 | ice: 25 | ice +35, water +15, wind +10, earth -30 | `ice_glacier_lance` (85) |
| `ice_tundra_dreadnought` | 成年 | L | 105 | 95 | 80 | 120 | 400 | ice: 40, metal: 15 | ice +50, water +15, wind +10, earth -40 | `ice_blizzard_vortex` (110) |
| `wind_reed_skiff` | 幼年 | S | 14 | 28 | 18 | 40 | 100 | wind: 15 | wind +20, wood +10, thunder -5, ice -20 | `wind_gale_claw` (30) |
| `wind_flute_cruiser` | 少年 | M | 28 | 56 | 36 | 80 | 200 | wind: 25 | wind +35, wood +15, thunder -10, ice -30 | `wind_vacuum_blade` (80) |
| `wind_monsoon_galeon` | 成年 | L | 56 | 114 | 70 | 160 | 400 | wind: 40 | wind +50, wood +15, thunder -10, ice -40 | `wind_tempest_sphere` (105) |

---

## 3. 弱點屬性分佈（無重疊檢驗）

七條線之**主要**弱點屬性（−20／−30／−40）各自唯一。例外：風線另帶次要弱點 `thunder` −5／−10／−10，與水線主要弱點重疊（其餘 6 線無第二弱點；是否保留見 `docs/reviews/P2-05-P2-07-r3-P5-03-sol.md` B6，待人工）：
1. `line_metal_gearclock` (metal)：弱點 `fire`
2. `line_wood_vinecrest` (wood)：弱點 `metal`
3. `line_water_tidecraft` (water)：弱點 `thunder`
4. `line_fire_blazelion` (fire)：弱點 `water`
5. `line_earth_gildedgolem` (earth)：弱點 `wind`
6. `line_ice_frostbeast` (ice)：弱點 `earth`
7. `line_wind_fluteglider` (wind)：弱點 `ice`

---

## 4. 待決問題

1. **精通招式之狀態/位移升級**：本批 21 隻宙獸精選之招式均為 P5-05 Batch 01 基礎傷害技。效果詞彙已由 P2-01 定案、P5-05 batch 05／06 提供 effect 招式；部分高階招式（如 `ice_blizzard_vortex`、`wood_canopy_crush`）可評估轉為 `kind: "both"` 帶控制減益。
2. **成年副專精之後續招式池搭配**：本批有 3 條線（金加土、水加風、冰加金）在成年開放次要專精（15%），其餘 4 條線保持純單屬性。副手與超載機制解鎖後，是否需額外配置次要屬性之招式，留待 P5-06 關係網與 P5-07 平衡模擬驗證。
3. **跨線關係邊構建**：`relations/edges.json` 目前僅包含 14 條線內 `evolution_family` 關係邊。跨文化與生態關係（如 `myth_kin`、`rival_symbiosis`、`tool_kin`）將在 P5-06 統一進行全圖景連通性配對。

## 命名與文案（TERRA）
中英名稱與圖鑑文案在 `copy-terra.json`。TERRA 指出 15 隻的 `concept.role` 承諾了戰鬥系統沒有的機制（牽引、屏障、聚怪、減速、持續治癒）或與精通招式射程不符；Opus 已把 21 隻的 `role` 改寫成與實際招式／特性一致（上表同步）。若要保留原本的定位構想，需換精通招式或等 effect 招式（P5-05 batch 05 類）指派，**待人工決定**。

## ai_style（2026-11-07，P2-10 / Q-C21，待人工）
Claude Opus 依精通招式機械式指定（非設計定稿）：dash → `charge`；long → `keep_distance`；melee 非 dash → AGI ≥ STR 為 `flank`、否則 `hold`；mid 非 dash → `hold`。結果：金 hold/hold/charge、木 charge/keep_distance/hold、水 hold/hold/keep_distance、火 hold/charge/keep_distance、土 hold/hold/charge、冰 hold/keep_distance/keep_distance、風 flank/keep_distance/keep_distance。同一線跨階換風格是因為精通招式射程本身逐階改變；若希望每線一致，需人工選定並換招。
