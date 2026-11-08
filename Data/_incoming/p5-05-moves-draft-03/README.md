# Moves Draft Batch 03 (P5-05)

> 2026-11-07 P5-08 A19：形狀已遷移為九形狀（`shape` ∈ front/sweep/burst/line/cone，加 `origin`、`movement`）。形狀統計以下方「現行形狀分佈」為準（由 `moves/moves.json` 腳本產生）；其餘段落提到的舊四形狀（line/cone/aoe/dash）是初稿歷史描述。

## Overview
This batch contains the third draft of 40 placeholder damage moves for COSMIMERA (`moves/moves.json`), completing roadmap item **P5-05**'s 120-move foundational damage pool.
- **Scope**: Exactly 40 new moves across all 10 elements (`metal`, `wood`, `water`, `fire`, `earth`, `ice`, `wind`, `thunder`, `light`, `shadow`), exactly 4 moves per element.
- **Move Kind**: `damage` only (no `effect` block in this batch; the effect vocabulary was closed by P2-01).
- **Batch Role & Focus**:
  - Bridges the extremes of Batch 01 and Batch 02 by providing core mid-band "utility" attacks (`power 50–75`) for every element.
  - Surgically eliminates remaining spatial deficits in the (element × shape) and (element × range_class) matrices.
- **Concept & ID Freshness**: Every ID is newly authored with zero reuse from Batch 01, Batch 02, or `Data/retired-ids.txt`. No near-duplicates exist (any move sharing element, shape, and range_class with an existing move differs in power by at least 25 points, well beyond the ±10 threshold).
- **Combined Milestone**: With Batches 01, 02, and 03 combined (120 total moves), every element now has:
  - Every shape (`line`, `cone`, `aoe`, `dash`) at least 3 times.
  - Every range class (`melee`, `mid`, `long`) exactly 4 times (fully exceeding the requirement of at least 2).

## Number Bands & Balance Guidelines
All numerical attributes are strictly integers obeying monotonic trade-offs (higher power dictates longer cooldown times and equal or lower accuracy):
- **Power**: 20–120 (Batch 03 span: 35–110)
- **Cooldown**: 800–10,000 ms (Batch 03 span: 1,000–8,200 ms)
- **Accuracy**: 70–100% (Batch 03 span: 75–100%)

### Power Tiers in Batch 03
1. **Low / Fast Poke (Power 35–45, CD 1,000–1,800 ms, Acc 95–100%)**: Low commitment harassment tools.
2. **Mid-Band Utility (Power 50–75, CD 2,100–4,400 ms, Acc 90–95%)**: Reliable backbone skills offering solid DPS rhythm and prime future candidates for secondary effects.
3. **Upper-Mid Heavy (Power 80–95, CD 5,000–7,000 ms, Acc 80–85%)**: High-impact mid/long zoning attacks.
4. **High Finisher (Power 100–110, CD 7,400–8,200 ms, Acc 75–80%)**: Decisive burst options requiring significant telegraphing and commitment.

## Element to Moves Mapping

| Element | Move 1 | Move 2 | Move 3 | Move 4 | Mid-Band Utility (50–75 P) | Spatial Niches Filled in Batch 03 |
|---|---|---|---|---|---|---|
| **metal** | `metal_drill_drive` | `metal_flechette_beam` | `metal_shard_volley` | `metal_anvil_drop` | `metal_flechette_beam` (70) | dash/mid, line/mid, cone/long, aoe/long |
| **wood** | `wood_bramble_whip` | `wood_pollen_jet` | `wood_vine_lunge` | `wood_timber_fall` | `wood_pollen_jet` (60) | cone/melee, line/mid, dash/long, aoe/long |
| **water** | `water_cascade_jab` | `water_geyser_pool` | `water_hydro_spray` | `water_tsunami_charge` | `water_geyser_pool` (65) | line/melee, aoe/mid, cone/long, dash/long |
| **fire** | `fire_ash_fan` | `fire_flare_nova` | `fire_searing_ray` | `fire_meteor_rush` | `fire_flare_nova` (55), `fire_searing_ray` (75) | cone/mid, aoe/melee, line/mid, dash/long |
| **earth** | `earth_stone_skate` | `earth_dust_cloud` | `earth_stalagmite_field` | `earth_tectonic_lance` | `earth_dust_cloud` (55), `earth_stalagmite_field` (75) | dash/melee, cone/melee, aoe/long, line/long |
| **ice** | `ice_claw_cleave` | `ice_permafrost_surge` | `ice_icicle_beam` | `ice_avalanche_charge` | `ice_permafrost_surge` (65) | cone/melee, aoe/melee, line/mid, dash/long |
| **wind** | `wind_zephyr_cutter` | `wind_whirlwind_burst` | `wind_shear_fan` | `wind_tailwind_dive` | `wind_whirlwind_burst` (50), `wind_shear_fan` (65) | line/mid, aoe/melee, cone/melee, dash/long |
| **thunder** | `thunder_shock_prod` | `thunder_volt_drift` | `thunder_storm_cell` | `thunder_fork_barrage` | `thunder_volt_drift` (55), `thunder_storm_cell` (75) | line/melee, dash/mid, aoe/mid, cone/long |
| **light** | `light_photon_flash` | `light_luminous_glide` | `light_halo_burst` | `light_zenith_lance` | `light_luminous_glide` (55), `light_halo_burst` (75) | cone/melee, dash/melee, aoe/mid, line/long |
| **shadow** | `shadow_dusk_fang` | `shadow_twilight_phase` | `shadow_abyssal_shroud` | `shadow_void_well` | `shadow_twilight_phase` (55), `shadow_abyssal_shroud` (70) | line/melee, dash/mid, cone/mid, aoe/long |

## Coverage Analysis: Before vs. After Batch 03

## 現行形狀分佈（由 moves.json 產生）

| shape(@origin)(+movement) | melee | mid | long | 合計 |
|---|---|---|---|---|
| `burst@caster` | 3 | 0 | 0 | 3 |
| `burst@target` | 0 | 3 | 4 | 7 |
| `cone` | 0 | 2 | 3 | 5 |
| `front` | 3 | 0 | 0 | 3 |
| `line` | 0 | 5 | 2 | 7 |
| `line+dash` | 2 | 3 | 5 | 10 |
| `sweep` | 5 | 0 | 0 | 5 |
| **合計** | 13 | 13 | 14 | **40** |

同屬性＋同形狀＋同射程的招式組（近似重複候選，需靠數值或效果區隔）：無。

### 2. Element × Range Class Coverage

Before Batch 03, several elements sat at the minimum threshold of 2 moves for specific range classes (e.g., Metal at 2 mid / 2 long, Earth at 2 melee / 2 long). Batch 03 strategically targeted these specific deficits, equalizing all elements to a uniform 4/4/4 distribution.

| Element | Melee (Pre → Post) | Mid (Pre → Post) | Long (Pre → Post) | Total (Pre → Post) | Gaps Filled by Batch 03 |
|---|---|---|---|---|---|
| **metal** | 4 → **4** (+0) | 2 → **4** (+2) | 2 → **4** (+2) | 8 → **12** | Mid & Long deficits equalized |
| **wood** | 3 → **4** (+1) | 3 → **4** (+1) | 2 → **4** (+2) | 8 → **12** | Long deficit equalized |
| **water** | 3 → **4** (+1) | 3 → **4** (+1) | 2 → **4** (+2) | 8 → **12** | Long deficit equalized |
| **fire** | 3 → **4** (+1) | 2 → **4** (+2) | 3 → **4** (+1) | 8 → **12** | Mid deficit equalized |
| **earth** | 2 → **4** (+2) | 4 → **4** (+0) | 2 → **4** (+2) | 8 → **12** | Melee & Long deficits equalized |
| **ice** | 2 → **4** (+2) | 3 → **4** (+1) | 3 → **4** (+1) | 8 → **12** | Melee deficit equalized |
| **wind** | 2 → **4** (+2) | 3 → **4** (+1) | 3 → **4** (+1) | 8 → **12** | Melee deficit equalized |
| **thunder** | 3 → **4** (+1) | 2 → **4** (+2) | 3 → **4** (+1) | 8 → **12** | Mid deficit equalized |
| **light** | 2 → **4** (+2) | 3 → **4** (+1) | 3 → **4** (+1) | 8 → **12** | Melee deficit equalized |
| **shadow** | 3 → **4** (+1) | 2 → **4** (+2) | 3 → **4** (+1) | 8 → **12** | Mid deficit equalized |
| **Total** | **27 → 40** (+13) | **27 → 40** (+13) | **26 → 40** (+14) | **80 → 120** | **All 10 elements achieve 4/4/4** |

*Post-Batch 03 Status*: Every element contains every range class exactly 4 times (fully exceeding the requirement of at least 2).

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
- **Total**: Exactly 40 moves.

### Per Range Class (Batch 03)
- `melee`: 13 moves (`wood_bramble_whip`, `water_cascade_jab`, `fire_flare_nova`, `earth_stone_skate`, `earth_dust_cloud`, `ice_claw_cleave`, `ice_permafrost_surge`, `wind_whirlwind_burst`, `wind_shear_fan`, `thunder_shock_prod`, `light_photon_flash`, `light_luminous_glide`, `shadow_dusk_fang`)
- `mid`: 13 moves (`metal_drill_drive`, `metal_flechette_beam`, `wood_pollen_jet`, `water_geyser_pool`, `fire_ash_fan`, `fire_searing_ray`, `ice_icicle_beam`, `wind_zephyr_cutter`, `thunder_volt_drift`, `thunder_storm_cell`, `light_halo_burst`, `shadow_twilight_phase`, `shadow_abyssal_shroud`)
- `long`: 14 moves (`metal_shard_volley`, `metal_anvil_drop`, `wood_vine_lunge`, `wood_timber_fall`, `water_hydro_spray`, `water_tsunami_charge`, `fire_meteor_rush`, `earth_stalagmite_field`, `earth_tectonic_lance`, `ice_avalanche_charge`, `wind_tailwind_dive`, `thunder_fork_barrage`, `light_zenith_lance`, `shadow_void_well`)
- **Total**: Exactly 40 moves.

## Prospective Kind "both" Candidates (P2 Integration)

As with earlier batches, several moves authored in Batch 03 were deliberately designed around gameplay sensations that naturally accept secondary effects. The vocabulary has been closed by P2-01 (`common.schema.json`); human designers should review whether to convert these candidates into `kind: "both"` with explicit effect definitions:
1. **Control / Area Denial**:
   - `metal_anvil_drop`: Heavy impact producing high stagger or target `knockdown`.
   - `wood_bramble_whip`: Thorny entanglement inflicting brief `root` or movement speed reduction.
   - `wood_timber_fall`: Crushing tree trunk creating temporary pathing blockage or physical `stun`.
   - `water_geyser_pool`: Rising column creating vertical `launch` / airborne juggle status.
   - `earth_dust_cloud`: Obscuring dust particle spread inflicting `blind` (accuracy reduction).
   - `ice_permafrost_surge`: Radial freezing burst inflicting `frostbite` (action speed debuff) or short `freeze`.
   - `wind_whirlwind_burst`: Centered tempest causing directional radial `knockback`.
   - `thunder_storm_cell`: Lingering electric dome delivering recurring micro-stutters (`paralysis`).
   - `shadow_void_well`: Gravitational nexus creating centripetal inward `suction` / vortex pull.
2. **Mobility Utility / Repositioning**:
   - `metal_drill_drive`: Mid-range thrust granting frontal collision armor during drilling.
   - `wood_vine_lunge`: Dashes the user along the vine line to the impact point (no target pull; combat has no pull mechanic).
   - `thunder_volt_drift`: Teleport/drift motion granting momentary evasion window.
   - `shadow_twilight_phase`: Shadow phase shifting granting partial collision intangibility.

## Open Questions
1. ~~**Targeting Origin Distinction for AoE**: Currently, `shape: "aoe"` encompasses both caster-centered novas and targeted remote ground reticles; extend the schema to distinguish them.~~ 已由 P2-04 結案：`shape: "burst"` requires `origin: "caster"` or `"target"`.
2. **Dash Trajectory vs. Teleport**: Skills like `shadow_twilight_phase` and `light_luminous_glide` straddle the border between high-speed physical translation and short-range blink/teleport. The collision subsystem will need parameters indicating whether hitboxes remain active and vulnerable along the traversed path.
3. **Horizon Horizon Cut-off for Planetary Long-Range Projectiles**: Long-range beam and volley attacks (`light_zenith_lance`, `earth_tectonic_lance`, `metal_shard_volley`) will require curvature-aware tracing on smaller celestial bodies to ensure raycasts follow planetary geodesic terrain without flying into orbit prematurely.
4. **Integration with P2-10 AI Combat Evaluator**: With 120 total damage moves spanning all 10 elements, AI combat behavior trees will need clear heuristics to pick between pokes, mid-band utility, and heavy finishers based on enemy distance and vulnerability windows.