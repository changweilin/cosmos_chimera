# Moves Draft Batch 01 (P5-05)

> 2026-11-07 P5-08 A19：形狀已遷移為九形狀（`shape` ∈ front/sweep/burst/line/cone，加 `origin`、`movement`）。形狀統計以下方「現行形狀分佈」為準（由 `moves/moves.json` 腳本產生）；其餘段落提到的舊四形狀（line/cone/aoe/dash）是初稿歷史描述。

## Overview
This batch contains the initial draft of 40 placeholder damage moves for COSMIMERA (`moves/moves.json`), corresponding to roadmap item **P5-05**.
- **Scope**: Exactly 40 moves across all 10 elements (`metal`, `wood`, `water`, `fire`, `earth`, `ice`, `wind`, `thunder`, `light`, `shadow`), 4 moves per element.
- **Move Kind**: `damage` only (no `effect` block in this batch; the effect vocabulary was closed by P2-01).
- **Draft Status**: All values are placeholder numbers for architecture and validation readiness; final balance and tuning will be executed by human designers.

## Number Bands & Balance Guidelines
All values are strictly integers following monotonic trade-offs (higher power corresponds to longer cooldowns and lower accuracy):
- **Power**: 20–120
- **Cooldown**: 800–10,000 ms
- **Accuracy**: 70–100%

## 現行形狀分佈（由 moves.json 產生）

| shape(@origin)(+movement) | melee | mid | long | 合計 |
|---|---|---|---|---|
| `burst@caster` | 3 | 0 | 0 | 3 |
| `burst@target` | 0 | 3 | 4 | 7 |
| `cone` | 0 | 6 | 0 | 6 |
| `front` | 1 | 0 | 0 | 1 |
| `line` | 0 | 2 | 7 | 9 |
| `line+dash` | 5 | 3 | 2 | 10 |
| `sweep` | 4 | 0 | 0 | 4 |
| **合計** | 13 | 14 | 13 | **40** |

同屬性＋同形狀＋同射程的招式組（近似重複候選，需靠數值或效果區隔）：無。

## Element to Moves Mapping

| Element | Move 1 | Move 2 | Move 3 | Move 4 |
|---|---|---|---|---|
| **metal** | `metal_needle_thrust` | `metal_iron_cleave` | `metal_scrap_barrage` | `metal_lance_charge` |
| **wood** | `wood_briar_dash` | `wood_spore_scatter` | `wood_root_spear` | `wood_canopy_crush` |
| **water** | `water_aqua_cutter` | `water_tidal_surge` | `water_torrent_burst` | `water_deluge_crater` |
| **fire** | `fire_ember_lash` | `fire_blaze_charge` | `fire_scorch_beam` | `fire_inferno_cataclysm` |
| **earth** | `earth_pebble_shot` | `earth_seismic_slam` | `earth_sand_sweep` | `earth_rampart_rush` |
| **ice** | `ice_shard_flurry` | `ice_rime_skate` | `ice_glacier_lance` | `ice_blizzard_vortex` |
| **wind** | `wind_gale_claw` | `wind_cyclone_dart` | `wind_vacuum_blade` | `wind_tempest_sphere` |
| **thunder** | `thunder_spark_dash` | `thunder_arc_discharge` | `thunder_plasma_rail` | `thunder_clap_nova` |
| **light** | `light_prism_beam` | `light_dawn_flash` | `light_solar_burst` | `light_radiant_step` |
| **shadow** | `shadow_umbral_fang` | `shadow_void_step` | `shadow_abyss_ray` | `shadow_eclipse_collapse` |

## Open Issues
1. **Geometric / Spatial Parameters**: ~~The current `move.schema.json` only tracks abstract `shape` and `range_class`.~~ 已由 P2-04 結案：`base_range_mm`, `origin` and `movement` are defined. Collision box dimensions, cone angles and movement/projectile speeds still need combat definitions.
2. **Transition to Hybrid / Status Moves**: Several moves designed here (such as `ice_blizzard_vortex`, `thunder_spark_dash`, or `wood_spore_scatter`) naturally suggest crowd-control or status effects (e.g. freeze, paralyze, slow, poison/entangle). The vocabulary has been closed by P2-01; human designers should review whether to convert these into `kind: "both"` with explicit effect definitions.
3. **Range Metric Standardization on Spherical Terrain**: Absolute unit boundaries between `melee`, `mid`, and `long` in relation to spherical planetary surface coordinates remain to be finalized in the combat design specification.
4. **Signature Move Assignments**: These 40 moves serve as a broad element pool. Once creature evolutionary lines and archetypes are authored in P5, signature moves (`signature_move`) can be selected and linked from this pool.
