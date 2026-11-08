# Moves Draft Batch 02 (P5-05)

> 2026-11-07 P5-08 A19：形狀已遷移為九形狀（`shape` ∈ front/sweep/burst/line/cone，加 `origin`、`movement`）。形狀統計以下方「現行形狀分佈」為準（由 `moves/moves.json` 腳本產生）；其餘段落提到的舊四形狀（line/cone/aoe/dash）是初稿歷史描述。

## Overview
This batch contains the second draft of 40 placeholder damage moves for COSMIMERA (`moves/moves.json`), corresponding to roadmap item **P5-05**.
- **Scope**: Exactly 40 new moves across all 10 elements (`metal`, `wood`, `water`, `fire`, `earth`, `ice`, `wind`, `thunder`, `light`, `shadow`), 4 moves per element.
- **Move Kind**: `damage` only (no `effect` block in this batch; the effect vocabulary was closed by P2-01).
- **Target Niches**: Expands on coverage gaps from Batch 01 by ensuring every element includes at least one low-power fast "poke" (`power <= 40`, `cooldown_ms <= 1500`) and at least one high-commitment finisher (`power >= 90`).
- **Concept & ID Freshness**: Every ID is newly introduced with zero reuse from Batch 01 or retired IDs, avoiding near-duplicate mechanics or concepts from the first batch.

## Number Bands & Balance Guidelines
All values are strictly integers following monotonic trade-offs (higher power corresponds to longer cooldowns and lower accuracy):
- **Power**: 20–120
- **Cooldown**: 800–10,000 ms
- **Accuracy**: 70–100%

## Element to Moves Mapping

| Element | Move 1 | Move 2 | Move 3 | Move 4 | Fast Poke (<=40 P / <=1500ms CD) | High Finisher (>=90 P) |
|---|---|---|---|---|---|---|
| **metal** | `metal_razor_spin` | `metal_mercury_drift` | `metal_shrapnel_spray` | `metal_rail_slug` | `metal_razor_spin` (35 / 1000ms) | `metal_rail_slug` (110) |
| **wood** | `wood_sap_splash` | `wood_bamboo_surge` | `wood_acorn_bombard` | `wood_thorn_ballista` | `wood_sap_splash` (35 / 1050ms) | `wood_thorn_ballista` (105) |
| **water** | `water_bubble_pop` | `water_rip_current` | `water_brine_spray` | `water_maelstrom_lance` | `water_bubble_pop` (30 / 900ms) | `water_maelstrom_lance` (110) |
| **fire** | `fire_spark_jab` | `fire_magma_wave` | `fire_cinder_slide` | `fire_caldera_burst` | `fire_spark_jab` (35 / 1000ms) | `fire_caldera_burst` (115) |
| **earth** | `earth_fault_dash` | `earth_mud_sling` | `earth_boulder_roll` | `earth_monolith_quarry` | `earth_mud_sling` (35 / 1100ms) | `earth_monolith_quarry` (105) |
| **ice** | `ice_frost_tap` | `ice_hail_burst` | `ice_floe_crash` | `ice_polar_sweep` | `ice_frost_tap` (30 / 900ms) | `ice_polar_sweep` (100) |
| **wind** | `wind_draft_step` | `wind_breeze_gust` | `wind_squall_vortex` | `wind_typhoon_spire` | `wind_draft_step` (30 / 850ms) | `wind_typhoon_spire` (105) |
| **thunder** | `thunder_static_prick` | `thunder_piercing_needle` | `thunder_fulgur_dive` | `thunder_megavolt_burst` | `thunder_static_prick` (35 / 1000ms) | `thunder_megavolt_burst` (115) |
| **light** | `light_glint_jab` | `light_corona_sweep` | `light_aurora_drift` | `light_stellar_nova` | `light_glint_jab` (30 / 900ms) | `light_stellar_nova` (110) |
| **shadow** | `shadow_nightmare_cleave` | `shadow_shade_dart` | `shadow_wraith_charge` | `shadow_singularity_maw` | `shadow_shade_dart` (30 / 900ms) | `shadow_singularity_maw` (120) |

## Distribution Counts (Hand-Verified)

### Per Element
- 4 moves per element across 10 elements:
  - `metal`: 4
  - `wood`: 4
  - `water`: 4
  - `fire`: 4
  - `earth`: 4
  - `ice`: 4
  - `wind`: 4
  - `thunder`: 4
  - `light`: 4
  - `shadow`: 4
- **Total**: 40 moves.

## 現行形狀分佈（由 moves.json 產生）

| shape(@origin)(+movement) | melee | mid | long | 合計 |
|---|---|---|---|---|
| `burst@caster` | 3 | 0 | 0 | 3 |
| `burst@target` | 0 | 2 | 5 | 7 |
| `cone` | 0 | 6 | 1 | 7 |
| `front` | 3 | 0 | 0 | 3 |
| `line` | 0 | 3 | 4 | 7 |
| `line+dash` | 5 | 2 | 3 | 10 |
| `sweep` | 3 | 0 | 0 | 3 |
| **合計** | 14 | 13 | 13 | **40** |

同屬性＋同形狀＋同射程的招式組（近似重複候選，需靠數值或效果區隔）：無。

### Per Range Class
- `melee`: 14 moves (metal: 2, wood: 2, water: 2, fire: 2, earth: 1, ice: 1, wind: 1, thunder: 1, light: 1, shadow: 1)
- `mid`: 13 moves (metal: 1, wood: 1, water: 1, fire: 1, earth: 2, ice: 2, wind: 2, thunder: 1, light: 1, shadow: 1)
- `long`: 13 moves (metal: 1, wood: 1, water: 1, fire: 1, earth: 1, ice: 1, wind: 1, thunder: 2, light: 2, shadow: 2)
- **Total**: 40 moves (each range class >= 12).

## Prospective Kind "both" Candidates (P2 Integration)

The effect vocabulary has been closed by P2-01 (`common.schema.json`); human designers can review whether to convert these candidate moves with strong thematic affinities into `kind: "both"` with explicit effect definitions:
1. **Damage + Crowd Control (Hard / Soft)**:
   - `metal_shrapnel_spray`: Inflicts `bleed` / physical vulnerability over time.
   - `wood_sap_splash`: Leaves sticky resin inflicting `slow` / movement penalty.
   - `water_rip_current`: Applies a directional `pull` / drag along the surge trajectory.
   - `earth_mud_sling`: Splatters mud causing temporary `blind` / accuracy reduction.
   - `ice_frost_tap`: Chills the target to induce an attack/cast speed debuff.
   - `ice_polar_sweep`: Extended cone of freezing winds triggering full `freeze` / immobilization on direct hits.
   - `wind_squall_vortex`: Depressive vortex creating an inward centripetal `vacuum_pull`.
   - `thunder_static_prick`: Stinging shock producing intermittent `paralysis` / micro-stutters.
   - `thunder_megavolt_burst`: Catastrophic ground lightning delivering high `stun` duration.
   - `light_corona_sweep`: Intense glare inflicting target `blind` / vision obstruction.
   - `shadow_nightmare_cleave`: Haunting phantom slash inducing `fear` / silence.
   - `shadow_singularity_maw`: Gravitational black hole collapsing enemies toward its center with defense reduction.
2. **Damage + Self Buff / Mobility**:
   - `wind_draft_step`: Grants temporary `evasion_boost` or movement acceleration post-dash.
   - `metal_mercury_drift`: Grants brief `damage_reduction` or collision pass-through during the slip.
3. **Damage + Environmental / Terrain Hazards (Post-P5)**:
   - `fire_caldera_burst`: Ignites the blast radius into a temporary `burning_ground` zone.
   - `earth_monolith_quarry`: Leaves temporary geological basalt pillars blocking linear projectiles.

## Open Issues
1. **Spatial & Movement Collision Tuning**: `dash` moves (`water_rip_current`, `wind_draft_step`, `thunder_fulgur_dive`, etc.) need collision volume dimensions, pass-through permissions (whether the user stops on enemy collision or clips through), and maximum dash distance calibrated during combat implementation.
2. **Finisher Windup / Recovery Telegraphing**: Moves with `power >= 100` and `cooldown_ms >= 7500` (`metal_rail_slug`, `thunder_megavolt_burst`, `shadow_singularity_maw`, etc.) require clear cast windup animations and telegraph decals on the ground to give players and AI viable dodge windows.
3. **Spherical Coordinate Scaling for Long-Range Lines**: Long-range line attacks (`metal_rail_slug`, `water_maelstrom_lance`, `wind_typhoon_spire`) on small planets could exceed horizon curvature if modeled as pure Euclidean rays. The combat engine needs clear clamping or geodesic tangent projection.
4. **Effect Vocabulary Schema Migration**: ~~The P2 effect vocabulary needs to be finalized.~~ 已由 P2-01 結案：`common.schema.json` defines closed types and per-type parameters; human designers can review whether to convert candidates above into `kind: "both"`.
