# P5-03 宙獸批次 03 — 已填配額的 prompt

由 `tools/prompts/creature-batch.md` 填入；配額依 `quota.py`（20/60 線）：10 屬性各 1 條、弱點 10 屬性互不重複、地區補缺。
原型避開 P5-02 README 標為邊界待人工決定者（`la_galigo`／`lafaek_diak`／`meduu_ribtal`／`adaro`／`kaassassuk`／`palraiyuk`／`douglas_mawson`），以及保守起見不用的神祇與薩米傳統條目（`dushara_petra`／`melqart`／`stallu`／`johan_turi_account`）。
派工 FLASH/TERRA 時從下方「PROMPT 開始」貼到「PROMPT 結束」；輸出放本資料夾 `creatures/creatures.json`、`relations/edges.json`。

---

## PROMPT 開始

先讀 `AGENTS.md`。你要產出 10 條完整三段進化線（幼年 `juvenile`／少年 `adolescent`／成年 `adult`，共 30 隻宙獸）的 JSON 初稿。
**只輸出兩個純 JSON 陣列（creatures、edges），不要散文、不要 markdown 說明。**

### 配額列（每條線一列，不得更動）
| line_id（你命名） | 主屬性 | 地區 | 文化原型 ID | 弱點屬性 |
|---|---|---|---|---|
| `line_metal_clockscribe` | metal | 美洲 | `arch_sor_juana` | thunder |
| `line_water_whirlpoolotter` | water | 美洲 | `arch_ahuizotl` | wood |
| `line_wood_bamboolion` | wood | 東南亞 | `arch_sang_sinxay` | ice |
| `line_fire_embertail` | fire | 西亞/中東 | `arch_simurgh` | earth |
| `line_earth_graniteraptor` | earth | 非洲 | `arch_great_zimbabwe_bird` | water |
| `line_ice_fjordisle` | ice | 極地 | `arch_hafgufa` | metal |
| `line_wind_gorgehawk` | wind | 大洋洲 | `arch_pouakai` | shadow |
| `line_thunder_copperdrake` | thunder | 非洲 | `arch_mwindo` | fire |
| `line_light_starwayfarer` | light | 大洋洲 | `arch_tupaia` | wind |
| `line_shadow_banyanbat` | shadow | 南亞 | `arch_vikram_vetala` | light |

弱點屬性在本批內須讓每種屬性至多出現 1 次。

### 輸入
- 已存在的 ID（不得重用；含 `Data/retired-ids.txt`）：（以下 60 個宙獸 id＋退役 6 個）
  - 宙獸：earth_auric_sovereign, earth_gilded_warden, earth_lotus_cub, earth_pebble_pawn, earth_pillar_chimera, earth_temple_tuskbeast, fire_ember_crocodilian, fire_ember_cub, fire_hearth_monolith, fire_mane_stalker, fire_pyre_monarch, fire_spark_barkling, ice_frost_fledgling, ice_hull_cutter, ice_polar_icebreaker, ice_rime_strider, ice_sled_buoy, ice_tundra_dreadnought, light_foil_spark, light_orbit_beacon, light_radiant_cyclotron, metal_chronos_engine, metal_citadel_colossus, metal_clock_chick, metal_gate_calf, metal_gear_strider, metal_winged_bull, moon_longnight_wolf, moon_night_pup, moon_shade_wolf, shadow_iron_cavalier, shadow_reverb_charger, shadow_tundra_foal, star_blaze_foal, star_skyhoof_steed, star_storm_colt, sun_array_skyheron, sun_heliostat_heron, sun_mirror_chick, thunder_crimson_crawler, thunder_dune_larva, thunder_gobi_voltwyrm, water_abyssal_leviathan, water_catamaran_serpent, water_harpoon_cetacean, water_ivory_calf, water_skiff_eel, water_wave_elver, wind_bergglider_gull, wind_crossbow_fledgling, wind_flute_cruiser, wind_monsoon_galeon, wind_pelagic_soarer, wind_reed_skiff, wood_betel_sprout, wood_canopy_regent, wood_fern_chick, wood_garland_serpent, wood_lotus_wyrm, wood_plume_peafowl
  - 退役：light_aureole_halo, thunder_chain_needle, trait_light_blinding_arrival, water_aqua_cutter, wind_air_slash, wood_vine_grapple
 
- 原型資料（只用上表指定的 ID，取**意象**，id／名稱不得含神祇或人物真名）：
- `arch_sor_juana`（美洲／歷史名人）：鵝毛筆詩卷、渾天星盤、古典藏書閣、齒輪鐘錶
- `arch_ahuizotl`（美洲／神話生物）：濕亮黑毛水獸、尾端靈巧掌爪、深潭漩渦、綠松石水沫
- `arch_sang_sinxay`（東南亞／文學）：象鼻金獅奇獸、金色法螺甲殼、誕生神弓、湄公河竹林
- `arch_simurgh`（西亞/中東／神話生物）：孔雀虹彩長尾、獅爪巨翼、厄爾布爾士神山、燃燒金羽
- `arch_great_zimbabwe_bird`（非洲／神話生物）：皂石神鷹立柱、人字紋花崗岩高牆、攀柱鱷魚浮雕、五趾鷹爪
- `arch_hafgufa`（極地／傳奇）：島嶼級偽裝巨獸、峽灣巨顎、群魚漩渦、極光寒海
- `arch_pouakai`（大洋洲／神話生物）：遮天巨翼猛禽、高山岩壁巨巢、利爪狂風、峽谷迷霧
- `arch_mwindo`（非洲／英雄）：鑲銅水牛尾神拂、鍛鐵短斧、地底七頭閃電龍、雨林激流
- `arch_tupaia`（大洋洲／歷史名人）：群島星辰海圖、雙體遠洋獨木舟、信風湧浪、珊瑚環礁航標
- `arch_vikram_vetala`（南亞／英雄）：拔劍王者、倒懸樹枝蝙蝠靈、月夜古榕樹、樺樹皮卷軸
- 可引用的招式 ID（**未被任何宙獸使用**的）：（只列 `damage` 類且排除 `field` 形狀；格式 id(威力,形狀/距離/位移)，依威力排序；每線從自己屬性挑 3 招，威力須逐階遞增）
- metal：`metal_razor_spin`(35,burst/melee)、`metal_blade_rush`(35,line/melee/dash)、`metal_drill_drive`(45,line/mid/dash)、`metal_tungsten_pierce`(50,line/mid)、`metal_mercury_drift`(60,line/melee/dash)、`metal_flechette_beam`(70,line/mid)、`metal_shrapnel_spray`(75,cone/mid)、`metal_shard_volley`(85,cone/long)、`metal_rail_slug`(110,line/long)、`metal_siege_bombard`(115,burst/long)
- water：`water_bubble_pop`(30,burst/melee)、`water_froth_swipe`(35,sweep/melee)、`water_tidal_surge`(55,line/melee/dash)、`water_rip_current`(60,line/melee/dash)、`water_geyser_pool`(65,burst/mid)、`water_brine_spray`(75,cone/mid)、`water_hydro_spray`(85,cone/long)、`water_depth_charge`(90,burst/long)、`water_maelstrom_lance`(110,line/long)
- wood：`wood_sap_splash`(35,burst/melee)、`wood_bramble_whip`(40,sweep/melee)、`wood_spore_scatter`(55,cone/mid)、`wood_pollen_jet`(60,line/mid)、`wood_tendril_vault`(60,line/mid/dash)、`wood_bamboo_surge`(65,line/melee/dash)、`wood_acorn_bombard`(75,cone/mid)、`wood_vine_lunge`(80,line/long/dash)、`wood_pine_salvo`(90,cone/long)、`wood_thorn_ballista`(105,line/long)
- fire：`fire_pyre_lunge`(35,line/melee/dash)、`fire_ash_fan`(40,cone/mid)、`fire_flare_nova`(55,burst/melee)、`fire_magma_wave`(60,sweep/melee)、`fire_furnace_vent`(65,burst/mid)、`fire_cinder_slide`(75,line/mid/dash)、`fire_searing_ray`(75,line/mid)、`fire_scorch_beam`(80,line/long)、`fire_pyroclast_sweep`(95,cone/long)、`fire_meteor_rush`(105,line/long/dash)
- earth：`earth_mud_sling`(35,cone/mid)、`earth_flint_thrust`(35,front/melee)、`earth_dust_cloud`(55,sweep/melee)、`earth_tremor_ring`(60,burst/mid)、`earth_boulder_roll`(65,line/mid)、`earth_sand_sweep`(75,cone/mid)、`earth_stalagmite_field`(75,burst/long)、`earth_scree_spray`(90,cone/long)、`earth_monolith_quarry`(105,burst/long)
- ice：`ice_frost_tap`(30,front/melee)、`ice_claw_cleave`(35,sweep/melee)、`ice_rime_skate`(50,line/melee/dash)、`ice_hoarfrost_ray`(55,line/mid)、`ice_hail_burst`(60,burst/mid)、`ice_permafrost_surge`(65,burst/melee)、`ice_icicle_beam`(80,line/mid)、`ice_frostfall_impact`(90,burst/long)、`ice_polar_sweep`(100,cone/long)
- wind：`wind_zephyr_pierce`(30,front/melee)、`wind_zephyr_cutter`(40,line/mid)、`wind_whirlwind_burst`(50,burst/melee)、`wind_breeze_gust`(55,cone/mid)、`wind_shear_fan`(65,sweep/melee)、`wind_jetstream_glide`(70,line/mid/dash)、`wind_squall_vortex`(75,burst/mid)、`wind_tailwind_dive`(95,line/long/dash)、`wind_typhoon_spire`(105,line/long)
- thunder：`thunder_static_prick`(35,sweep/melee)、`thunder_shock_prod`(35,front/melee)、`thunder_spark_dash`(40,line/melee/dash)、`thunder_volt_drift`(55,line/mid/dash)、`thunder_piercing_needle`(60,line/mid)、`thunder_storm_cell`(75,burst/mid)、`thunder_fulgur_dive`(80,line/long/dash)、`thunder_overload_spray`(80,cone/mid)、`thunder_fork_barrage`(95,cone/long)、`thunder_dynamo_charge`(105,line/long/dash)、`thunder_clap_nova`(110,burst/melee)、`thunder_megavolt_burst`(115,burst/long)
- light：`light_photon_flash`(35,sweep/melee)、`light_beacon_lunge`(35,line/melee/dash)、`light_prism_beam`(45,line/long)、`light_dawn_flash`(50,burst/melee)、`light_luminous_glide`(55,line/melee/dash)、`light_corona_sweep`(60,cone/mid)、`light_solar_burst`(75,cone/mid)、`light_aurora_drift`(75,line/long/dash)、`light_halo_burst`(75,burst/mid)、`light_radiant_step`(95,line/mid/dash)、`light_zenith_lance`(105,line/long)、`light_stellar_nova`(110,burst/long)
- shadow：`shadow_shade_dart`(30,line/mid)、`shadow_umbral_fang`(35,sweep/melee)、`shadow_gloom_pulse`(35,burst/melee)、`shadow_void_step`(55,line/melee/dash)、`shadow_penumbra_spike`(65,line/mid)、`shadow_nightmare_cleave`(70,sweep/melee)、`shadow_abyssal_shroud`(70,cone/mid)、`shadow_abyss_ray`(80,line/long)、`shadow_void_well`(85,burst/long)、`shadow_entropy_wave`(100,cone/long)、`shadow_eclipse_collapse`(115,burst/mid)、`shadow_singularity_maw`(120,burst/long)
- 可引用的特性 ID（**未被任何宙獸使用**的）：（依主屬性分組；已排除單屬性 `on_hit_taken` 觸發，避免跨線永不觸發的死特性）
- metal：`trait_metal_ballistic_pivot`[on_ally_swap]、`trait_metal_reinforce_protocol`[on_ally_swap]、`trait_metal_scrap_overdrive`[on_hp_below]、`trait_metal_siege_anchoring`[on_summon]、`trait_metal_tempered_edge`[on_move_used]
- water：`trait_water_calm_waters`[on_hp_above]、`trait_water_fluid_stance`[always]、`trait_water_revitalizing_spring`[on_move_used]、`trait_water_spring_surge`[on_ally_swap]、`trait_water_torrential_deluge`[on_move_used]、`trait_water_undertow_escape`[on_hp_below]
- wood：`trait_wood_bark_reinforcement`[always]、`trait_wood_canopy_reach`[always]、`trait_wood_overgrowth_surge`[on_hp_above]、`trait_wood_thorn_lash`[on_hp_below]、`trait_wood_verdant_resonance`[on_ally_swap]
- fire：`trait_fire_blazing_fury`[on_hp_below]、`trait_fire_conflagration_flare`[on_hp_below]、`trait_fire_heat_exhaust`[on_summon]、`trait_fire_pyroclastic_burst`[on_summon]、`trait_fire_scorching_momentum`[on_hp_above]、`trait_fire_smoldering_core`[always]
- earth：`trait_earth_crag_attunement`[always]、`trait_earth_faultline_shift`[on_ally_swap]、`trait_earth_grounding_mantle`[on_summon]、`trait_earth_monolith_posture`[always]、`trait_earth_shattered_carapace`[on_hp_below]、`trait_earth_stone_bastion`[on_hp_above]
- ice：`trait_ice_glacial_patience`[on_hp_above]、`trait_ice_glacier_bastion`[on_hp_below]、`trait_ice_hoarfrost_tread`[on_move_used]、`trait_ice_permafrost_shell`[always]、`trait_ice_rime_shatter`[on_ally_swap]、`trait_ice_subzero_focus`[on_hp_above]
- wind：`trait_wind_breeze_cadence`[always]、`trait_wind_cyclone_instability`[on_move_used]、`trait_wind_desperation_updraft`[on_hp_below]、`trait_wind_feather_grace`[on_hp_below]、`trait_wind_gale_gust`[on_summon]、`trait_wind_tempest_headway`[on_summon]
- thunder：`trait_thunder_crisis_arc`[on_hp_below]、`trait_thunder_lightning_pivot`[on_ally_swap]、`trait_thunder_plasma_charge`[always]、`trait_thunder_spark_stride`[on_summon]、`trait_thunder_static_overload`[on_move_used]、`trait_thunder_supercharged_stride`[on_hp_above]
- light：`trait_light_aurora_veil`[on_ally_swap]、`trait_light_guiding_beam`[on_ally_swap]、`trait_light_luminous_barrier`[on_move_used]、`trait_light_mirage_arrival`[on_summon]、`trait_light_radiant_core`[always]、`trait_light_saving_grace`[on_hp_cross_below]、`trait_light_solar_overdrive`[on_hp_above]
- shadow：`trait_shadow_abyssal_haste`[on_hp_below]、`trait_shadow_dusk_flicker`[on_hp_above]、`trait_shadow_nightfall_creed`[on_summon]、`trait_shadow_umbral_ambush`[on_ally_swap]、`trait_shadow_umbral_cloak`[always]、`trait_shadow_void_embrace`[on_hp_below]
- 命名風格：英文 snake_case、`<屬性>_<名詞>[_<名詞>]`，同線三階詞根可辨識；id 全域唯一、`^[a-z][a-z0-9]*(_[a-z0-9]+)*$`、≤ 64 字元。

### creatures 規則（每隻都必須符合，違反者整批退回）
欄位完全依 `creature.schema.json`（不得多欄位）。數值為整數，不得出現小數。

| | 幼年 | 少年 | 成年 |
|---|---|---|---|
| 四維總和（con+str+spi+agi） | 100 | 200 | 400 |
| 單項範圍 | 10–50 | 25–100 | 50–200 |
| 主屬性專精（`mastery`） | 15 | 25 | 40，可再加**一個**副屬性 15 |
| 本屬性抗性 | +20 | +35 | +50 |
| 弱點（**恰好一個**負值） | −20 | −30 | −40 |
| `evolution.level` | 20–30 | 35–50 | `evolution: null` |

- 四維每一項隨階段**單調不減**；`resist` 十個鍵全填；其餘抗性為 0 或小幅正值（≤ 本屬性抗性的 1/2），不得再有負值。
- `evolves_to` 指向下一階 id，成年為 `null`；`evolution.materials` 為主屬性素材：幼年 `tier 1`、少年 `tier 2`，`qty` 3–5。
- `size_class`：幼年 XS／S、少年 M（可 L）、成年 L／XL；以身長界線為準（`creatures.md#體型級距`）。
- `ai_style` 對齊 `signature_move` 的 `movement`／`range_class`（遠距→`keep_distance`、突進→`charge`、繞背→`flank`、近戰定點→`hold`）。
- `temperament` 逐隻指定，同線只升不降；**不得使用 `apex`**。批內分布大致：timid 20%／cautious 25%／normal 25%／territorial 20%／fierce 10%。
- `signature_move`：從「可引用的招式」挑，**屬性與主屬性相同**，威力逐階遞增，整批不重複；`trait`：同線三階共用一個特性，整批不重複。
- `concept` 必填鍵：`base`（外觀基底，生物／人形／器械／載具可複合）、`stage_look`、`archetype`（上表 ID）、`region`（上表地區原文）、`motifs_used`（陣列，僅意象）、`role`、`palette`。**不得填 `sensitivity`**，原型為 `review` 者本來就不在配額內。
- 外觀意象與專精／抗性各自設計，不必強制一一對應（Q-C27）。

### edges 規則
- 每條線 2 條 `evolution_family`（幼→少、少→成），其餘跨線關係邊留給 ARGON（P5-06），本批不產。
- 格式 `{"a","b","kind","note"}`，**`a` 字典序小於 `b`**，無重複；`note` 為開發備註，說明同線進化即可。

### 自檢清單（輸出前逐項確認，不輸出）
四維總和／範圍 · 弱點恰一個 · 抗性十鍵 · id 唯一且未被使用 · 招式屬性與威力遞增 · 特性整批不重複 · 無 `apex` · 無真名 id · edges `a < b`。

## PROMPT 結束
