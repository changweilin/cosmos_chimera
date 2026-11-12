# P5-03 宙獸批次草稿 02 (p5-03-creatures-draft-02)

FLASH 產出之 10 條完整三段進化線（共 30 隻宙獸）初稿。包含 `creatures/creatures.json` 與 `relations/edges.json`。
嚴格遵循 `Data/schemas/creature.schema.json`、`relation.schema.json`、`Data/balance.json` 之 `stage_bands` 與 `evolution_level_bands` 規範，以及 `docs/design/creature-concept-pipeline.md` 文化原型意象準則（僅採用 `p5-02-archetypes-draft-01` 內 `sensitivity: "ok"` 且無待決邊界爭議之條目）。

---

## 1. 十條進化線總覽（涵蓋全部 10 屬性、8 個 P5-02 地區）

| line_id | 主屬性 | 地區 | 文化原型 ID | 弱點 | 精通招式（幼年 / 少年 / 成年） | 共用特性 |
|---|---|---|---|---|---|---|
| `line_metal_citadelbull` | metal | 西亞/中東 | `arch_lamassu` | `fire` | `metal_corrosion_edge` (35) / `metal_scrap_barrage` (80) / `metal_anvil_drop` (100) | `trait_metal_clockwork_precision` |
| `line_wood_crownvine` | wood | 東南亞 | `arch_neang_neak` | `metal` | `wood_splinter_thrust` (30) / `wood_thorn_snare` (50) / `wood_timber_fall` (95) | `trait_wood_spore_regeneration` |
| `line_water_whalemaelstrom` | water | 美洲 | `arch_moby_dick` | `thunder` | `water_cascade_jab` (35) / `water_undertow_rush` (65) / `water_tsunami_charge` (100) | `trait_water_cleansing_tide` |
| `line_fire_baobabhearth` | fire | 非洲 | `arch_ibonia` | `water` | `fire_spark_jab` (35) / `fire_cinder_slash` (60) / `fire_caldera_burst` (115) | `trait_fire_cinder_screen` |
| `line_earth_pillarbeast` | earth | 南亞 | `arch_yali` | `wind` | `earth_stone_skate` (35) / `earth_fault_dash` (70) / `earth_tectonic_lance` (105) | `trait_earth_landslide_charge` |
| `line_ice_driftvessel` | ice | 極地 | `arch_fridtjof_nansen` | `earth` | `ice_glaze_slide` (30) / `ice_floe_crash` (75) / `ice_avalanche_charge` (110) | `trait_ice_frostbite_edge` |
| `line_wind_mistwing` | wind | 極地 | `arch_ancient_mariner_albatross` | `ice` | `wind_draft_step` (30) / `wind_cyclone_dart` (55) / `wind_hurricane_sweep` (90) | `trait_wind_aloft_vigilance` |
| `line_thunder_redburrower` | thunder | 中亞/北亞 | `arch_olgoi_khorkhoi` | `wood` | `thunder_pulse_ring` (30) / `thunder_arc_discharge` (60) / `thunder_plasma_rail` (90) | `trait_thunder_fulgur_reach` |
| `line_light_foilprism` | light | 大洋洲 | `arch_ernest_rutherford` | `shadow` | `light_glint_jab` (30) / `light_optic_lance` (65) / `light_chromatic_fan` (90) | `trait_light_halo_rebound` |
| `line_shadow_ironsteed` | shadow | 中亞/北亞 | `arch_nyurgun_bootur` | `light` | `shadow_dusk_fang` (35) / `shadow_twilight_phase` (55) / `shadow_wraith_charge` (85) | `trait_shadow_fade_step` |

---

## 2. 30 隻宙獸數值與特性表

- 幼年（`juvenile`）：總點數 100（單項 10–50）、演化等級 20–27、主專精 15、主抗 +20／唯一弱點 -20。
- 少年（`adolescent`）：總點數 200（單項 25–100）、演化等級 37–47、主專精 25、主抗 +35／唯一弱點 -30。
- 成年（`adult`）：總點數 400（單項 50–200）、主專精 40（金加土、木加水、雷加火、光加雷四線加 15 副專精）、主抗 +50／唯一弱點 -40。
- 10 條線主要弱點形成 10 屬性完整對射（每種屬性恰出現 1 次，無次要弱點）。
- 性格（`temperament`）同線單調不減，且未使用 `apex`。`ai_style` 嚴格依現行精通招式之 `movement`/`range_class`/能力值對齊。

| id | 階段 | 體型 | ai_style | temperament | CON/STR/SPI/AGI | 專精 | 抗性（非零） | 精通招式 |
|---|---|---|---|---|---|---|---|---|
| `metal_gate_calf` | 幼年 | S | hold | cautious | 38/26/22/14 | metal:15 | metal +20, earth +10, light +5, fire -20 | `metal_corrosion_edge` (35) |
| `metal_winged_bull` | 少年 | M | hold | normal | 76/52/44/28 | metal:25 | metal +35, earth +15, light +10, fire -30 | `metal_scrap_barrage` (80) |
| `metal_citadel_colossus` | 成年 | XL | keep_distance | territorial | 152/104/88/56 | metal:40, earth:15 | metal +50, earth +20, light +15, fire -40 | `metal_anvil_drop` (100) |
| `wood_betel_sprout` | 幼年 | XS | hold | timid | 26/24/32/18 | wood:15 | wood +20, water +10, light +5, metal -20 | `wood_splinter_thrust` (30) |
| `wood_garland_serpent` | 少年 | M | hold | cautious | 52/48/64/36 | wood:25 | wood +35, water +15, light +10, metal -30 | `wood_thorn_snare` (50) |
| `wood_lotus_wyrm` | 成年 | L | keep_distance | normal | 104/96/128/72 | wood:40, water:15 | wood +50, water +20, light +15, metal -40 | `wood_timber_fall` (95) |
| `water_ivory_calf` | 幼年 | S | hold | cautious | 34/32/16/18 | water:15 | water +20, ice +10, metal +5, thunder -20 | `water_cascade_jab` (35) |
| `water_harpoon_cetacean` | 少年 | L | charge | territorial | 68/64/32/36 | water:25 | water +35, ice +15, metal +10, thunder -30 | `water_undertow_rush` (65) |
| `water_abyssal_leviathan` | 成年 | XL | charge | fierce | 136/128/64/72 | water:40 | water +50, ice +20, metal +15, thunder -40 | `water_tsunami_charge` (100) |
| `fire_spark_barkling` | 幼年 | S | hold | cautious | 30/28/22/20 | fire:15 | fire +20, wood +10, earth +5, water -20 | `fire_spark_jab` (35) |
| `fire_ember_crocodilian` | 少年 | M | hold | normal | 60/56/44/40 | fire:25 | fire +35, wood +15, earth +10, water -30 | `fire_cinder_slash` (60) |
| `fire_hearth_monolith` | 成年 | L | keep_distance | territorial | 120/112/88/80 | fire:40 | fire +50, wood +20, earth +15, water -40 | `fire_caldera_burst` (115) |
| `earth_lotus_cub` | 幼年 | S | charge | normal | 28/34/16/22 | earth:15 | earth +20, metal +10, fire +5, wind -20 | `earth_stone_skate` (35) |
| `earth_pillar_chimera` | 少年 | M | charge | territorial | 56/68/32/44 | earth:25 | earth +35, metal +15, fire +10, wind -30 | `earth_fault_dash` (70) |
| `earth_temple_tuskbeast` | 成年 | L | keep_distance | fierce | 112/136/64/88 | earth:40 | earth +50, metal +20, fire +15, wind -40 | `earth_tectonic_lance` (105) |
| `ice_sled_buoy` | 幼年 | XS | charge | timid | 22/28/20/30 | ice:15 | ice +20, water +10, wood +5, earth -20 | `ice_glaze_slide` (30) |
| `ice_hull_cutter` | 少年 | M | charge | cautious | 44/56/40/60 | ice:25 | ice +35, water +15, wood +10, earth -30 | `ice_floe_crash` (75) |
| `ice_polar_icebreaker` | 成年 | XL | charge | normal | 88/112/80/120 | ice:40 | ice +50, water +20, wood +15, earth -40 | `ice_avalanche_charge` (110) |
| `wind_crossbow_fledgling` | 幼年 | S | charge | timid | 18/24/24/34 | wind:15 | wind +20, water +10, wood +5, ice -20 | `wind_draft_step` (30) |
| `wind_bergglider_gull` | 少年 | M | charge | cautious | 36/48/48/68 | wind:25 | wind +35, water +15, wood +10, ice -30 | `wind_cyclone_dart` (55) |
| `wind_pelagic_soarer` | 成年 | L | keep_distance | normal | 72/96/96/136 | wind:40 | wind +50, water +20, wood +15, ice -40 | `wind_hurricane_sweep` (90) |
| `thunder_dune_larva` | 幼年 | XS | flank | cautious | 26/24/24/26 | thunder:15 | thunder +20, fire +10, earth +5, wood -20 | `thunder_pulse_ring` (30) |
| `thunder_crimson_crawler` | 少年 | M | hold | normal | 52/48/48/52 | thunder:25 | thunder +35, fire +15, earth +10, wood -30 | `thunder_arc_discharge` (60) |
| `thunder_gobi_voltwyrm` | 成年 | XL | keep_distance | fierce | 104/96/96/104 | thunder:40, fire:15 | thunder +50, fire +20, earth +15, wood -40 | `thunder_plasma_rail` (90) |
| `light_foil_spark` | 幼年 | XS | hold | timid | 20/28/34/18 | light:15 | light +20, thunder +10, metal +5, shadow -20 | `light_glint_jab` (30) |
| `light_orbit_beacon` | 少年 | M | hold | cautious | 40/56/68/36 | light:25 | light +35, thunder +15, metal +10, shadow -30 | `light_optic_lance` (65) |
| `light_radiant_cyclotron` | 成年 | L | keep_distance | normal | 80/112/136/72 | light:40, thunder:15 | light +50, thunder +20, metal +15, shadow -40 | `light_chromatic_fan` (90) |
| `shadow_tundra_foal` | 幼年 | S | flank | cautious | 24/26/22/28 | shadow:15 | shadow +20, ice +10, metal +5, light -20 | `shadow_dusk_fang` (35) |
| `shadow_reverb_charger` | 少年 | M | charge | normal | 48/52/44/56 | shadow:25 | shadow +35, ice +15, metal +10, light -30 | `shadow_twilight_phase` (55) |
| `shadow_iron_cavalier` | 成年 | L | charge | territorial | 96/104/88/112 | shadow:40 | shadow +50, ice +20, metal +15, light -40 | `shadow_wraith_charge` (85) |

---

## 3. 驗證與待續工作

- **驗證**：合併 `p2-08-starters-draft-01`、`p5-03-creatures-draft-01`、`p5-05-moves-draft-01..06`、`p5-05-traits-draft-01..03`、`p5-06-relations-draft-01`、`p3-planet-types-draft-01`、`p3-12-env-materials-draft-01` 後執行 `tools/validate_data.py`：**0 error(s), 0 warning(s)**；全數語意／數值帶／原型／招式／特性約束腳本驗收通過。
- **待續**：
  1. TERRA：中英顯示名稱與圖鑑文案（`copy-terra.json`）及 forge `gen_designed.py` 納入。
  2. SOL：30 隻宙獸數值與招式／特性平衡模擬（P5-07）。
  3. ARGON：與既有 30 隻跨線關係邊建議（P5-06）。
  4. SONNET + 人工：原型敏感度與設計定稿審核。
