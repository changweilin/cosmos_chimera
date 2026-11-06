# Moves Draft Batch 03 (P5-05)

## Overview
This batch contains the third draft of 40 placeholder damage moves for COSMIMERA (`moves/moves.json`), completing roadmap item **P5-05**'s 120-move foundational damage pool.
- **Scope**: Exactly 40 new moves across all 10 elements (`metal`, `wood`, `water`, `fire`, `earth`, `ice`, `wind`, `thunder`, `light`, `shadow`), exactly 4 moves per element.
- **Move Kind**: `damage` only (status and secondary effect schema/vocabularies are deferred to P2; no `effect` block is included).
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
| **wood** | `wood_bramble_whip` | `wood_pollen_jet` | `wood_vine_grapple` | `wood_timber_fall` | `wood_pollen_jet` (60) | cone/melee, line/mid, dash/long, aoe/long |
| **water** | `water_cascade_jab` | `water_geyser_pool` | `water_hydro_spray` | `water_tsunami_charge` | `water_geyser_pool` (65) | line/melee, aoe/mid, cone/long, dash/long |
| **fire** | `fire_ash_fan` | `fire_flare_nova` | `fire_searing_ray` | `fire_meteor_rush` | `fire_flare_nova` (55), `fire_searing_ray` (75) | cone/mid, aoe/melee, line/mid, dash/long |
| **earth** | `earth_stone_skate` | `earth_dust_cloud` | `earth_stalagmite_field` | `earth_tectonic_lance` | `earth_dust_cloud` (55), `earth_stalagmite_field` (75) | dash/melee, cone/melee, aoe/long, line/long |
| **ice** | `ice_claw_cleave` | `ice_permafrost_surge` | `ice_icicle_beam` | `ice_avalanche_charge` | `ice_permafrost_surge` (65) | cone/melee, aoe/melee, line/mid, dash/long |
| **wind** | `wind_air_slash` | `wind_whirlwind_burst` | `wind_shear_fan` | `wind_tailwind_dive` | `wind_whirlwind_burst` (50), `wind_shear_fan` (65) | line/mid, aoe/melee, cone/melee, dash/long |
| **thunder** | `thunder_shock_prod` | `thunder_volt_drift` | `thunder_storm_cell` | `thunder_fork_barrage` | `thunder_volt_drift` (55), `thunder_storm_cell` (75) | line/melee, dash/mid, aoe/mid, cone/long |
| **light** | `light_photon_flash` | `light_luminous_glide` | `light_halo_burst` | `light_zenith_lance` | `light_luminous_glide` (55), `light_halo_burst` (75) | cone/melee, dash/melee, aoe/mid, line/long |
| **shadow** | `shadow_dusk_fang` | `shadow_twilight_phase` | `shadow_abyssal_shroud` | `shadow_void_well` | `shadow_twilight_phase` (55), `shadow_abyssal_shroud` (70) | line/melee, dash/mid, cone/mid, aoe/long |

## Coverage Analysis: Before vs. After Batch 03

### 1. Element × Shape Coverage

Every element in Batch 01 and Batch 02 received exactly 2 moves per shape. Batch 03 contributes exactly 1 new move per shape to each element.

| Element | Line (Pre → Post) | Cone (Pre → Post) | AoE (Pre → Post) | Dash (Pre → Post) | Total (Pre → Post) |
|---|---|---|---|---|---|
| **metal** | 2 → **3** | 2 → **3** | 2 → **3** | 2 → **3** | 8 → **12** |
| **wood** | 2 → **3** | 2 → **3** | 2 → **3** | 2 → **3** | 8 → **12** |
| **water** | 2 → **3** | 2 → **3** | 2 → **3** | 2 → **3** | 8 → **12** |
| **fire** | 2 → **3** | 2 → **3** | 2 → **3** | 2 → **3** | 8 → **12** |
| **earth** | 2 → **3** | 2 → **3** | 2 → **3** | 2 → **3** | 8 → **12** |
| **ice** | 2 → **3** | 2 → **3** | 2 → **3** | 2 → **3** | 8 → **12** |
| **wind** | 2 → **3** | 2 → **3** | 2 → **3** | 2 → **3** | 8 → **12** |
| **thunder** | 2 → **3** | 2 → **3** | 2 → **3** | 2 → **3** | 8 → **12** |
| **light** | 2 → **3** | 2 → **3** | 2 → **3** | 2 → **3** | 8 → **12** |
| **shadow** | 2 → **3** | 2 → **3** | 2 → **3** | 2 → **3** | 8 → **12** |
| **Total** | **20 → 30** | **20 → 30** | **20 → 30** | **20 → 30** | **80 → 120** |

*Post-Batch 03 Status*: Every element contains every shape at least 3 times (requirement was at least 1).

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

### 3. Specific Spatial Matrix Gaps Filled by Batch 03
Beyond generic range and shape categories, Batch 03 introduces combinations of (shape × range_class) that were previously vacant within specific elements:
- **Metal**: Added mid-range line (`metal_flechette_beam`), mid-range dash (`metal_drill_drive`), long-range cone (`metal_shard_volley`), and long-range orbital AoE (`metal_anvil_drop`). Completes the entire 4×3 matrix for Metal.
- **Wood**: Added close-quarters cone lash (`wood_bramble_whip`), mid-range linear jet (`wood_pollen_jet`), long-range sling dash (`wood_vine_grapple`), and long-range ballistic drop AoE (`wood_timber_fall`).
- **Water**: Added point-blank melee thrust line (`water_cascade_jab`), mid-range eruptive AoE (`water_geyser_pool`), long-range dispersion cone (`water_hydro_spray`), and long-range surging dash (`water_tsunami_charge`).
- **Fire**: Added point-blank self-centered detonation AoE (`fire_flare_nova`), mid-range ash cone (`fire_ash_fan`), mid-range beam line (`fire_searing_ray`), and long-range projectile dash (`fire_meteor_rush`).
- **Earth**: Added close-range rock skate dash (`earth_stone_skate`), close-range dust cone (`earth_dust_cloud`), long-range seismic field AoE (`earth_stalagmite_field`), and long-range tectonic beam line (`earth_tectonic_lance`).
- **Ice**: Added close-range frosted claw sweep cone (`ice_claw_cleave`), close-range perimeter freeze AoE (`ice_permafrost_surge`), mid-range ray line (`ice_icicle_beam`), and long-range charging avalanche dash (`ice_avalanche_charge`).
- **Wind**: Added close-range vortex burst AoE (`wind_whirlwind_burst`), close-range wind shear cone (`wind_shear_fan`), mid-range cutting air line (`wind_air_slash`), and long-range diving dash (`wind_tailwind_dive`).
- **Thunder**: Added close-range shock lance line (`thunder_shock_prod`), mid-range electromagnetic slip dash (`thunder_volt_drift`), mid-range lingering electric cell AoE (`thunder_storm_cell`), and long-range branching lightning cone (`thunder_fork_barrage`).
- **Light**: Added close-range blinding flash cone (`light_photon_flash`), close-range swift dash slip (`light_luminous_glide`), mid-range radial aura AoE (`light_halo_burst`), and long-range piercing beam finisher (`light_zenith_lance`).
- **Shadow**: Added close-range needle pierce line (`shadow_dusk_fang`), mid-range phantom phase dash (`shadow_twilight_phase`), mid-range cone veil (`shadow_abyssal_shroud`), and long-range gravitational pull AoE (`shadow_void_well`).

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

### Per Shape (Batch 03)
- `line`: 10 moves (`metal_flechette_beam`, `wood_pollen_jet`, `water_cascade_jab`, `fire_searing_ray`, `earth_tectonic_lance`, `ice_icicle_beam`, `wind_air_slash`, `thunder_shock_prod`, `light_zenith_lance`, `shadow_dusk_fang`)
- `cone`: 10 moves (`metal_shard_volley`, `wood_bramble_whip`, `water_hydro_spray`, `fire_ash_fan`, `earth_dust_cloud`, `ice_claw_cleave`, `wind_shear_fan`, `thunder_fork_barrage`, `light_photon_flash`, `shadow_abyssal_shroud`)
- `aoe`: 10 moves (`metal_anvil_drop`, `wood_timber_fall`, `water_geyser_pool`, `fire_flare_nova`, `earth_stalagmite_field`, `ice_permafrost_surge`, `wind_whirlwind_burst`, `thunder_storm_cell`, `light_halo_burst`, `shadow_void_well`)
- `dash`: 10 moves (`metal_drill_drive`, `wood_vine_grapple`, `water_tsunami_charge`, `fire_meteor_rush`, `earth_stone_skate`, `ice_avalanche_charge`, `wind_tailwind_dive`, `thunder_volt_drift`, `light_luminous_glide`, `shadow_twilight_phase`)
- **Total**: Exactly 10 per shape (40 moves total).

### Per Range Class (Batch 03)
- `melee`: 13 moves (`wood_bramble_whip`, `water_cascade_jab`, `fire_flare_nova`, `earth_stone_skate`, `earth_dust_cloud`, `ice_claw_cleave`, `ice_permafrost_surge`, `wind_whirlwind_burst`, `wind_shear_fan`, `thunder_shock_prod`, `light_photon_flash`, `light_luminous_glide`, `shadow_dusk_fang`)
- `mid`: 13 moves (`metal_drill_drive`, `metal_flechette_beam`, `wood_pollen_jet`, `water_geyser_pool`, `fire_ash_fan`, `fire_searing_ray`, `ice_icicle_beam`, `wind_air_slash`, `thunder_volt_drift`, `thunder_storm_cell`, `light_halo_burst`, `shadow_twilight_phase`, `shadow_abyssal_shroud`)
- `long`: 14 moves (`metal_shard_volley`, `metal_anvil_drop`, `wood_vine_grapple`, `wood_timber_fall`, `water_hydro_spray`, `water_tsunami_charge`, `fire_meteor_rush`, `earth_stalagmite_field`, `earth_tectonic_lance`, `ice_avalanche_charge`, `wind_tailwind_dive`, `thunder_fork_barrage`, `light_zenith_lance`, `shadow_void_well`)
- **Total**: Exactly 40 moves.

### Batch 03 Shape × Range Class Cross-Tabulation

| Shape | Melee | Mid | Long | Total |
|---|---|---|---|---|
| **line** | 3 (`water_cascade_jab`, `thunder_shock_prod`, `shadow_dusk_fang`) | 5 (`metal_flechette_beam`, `wood_pollen_jet`, `fire_searing_ray`, `ice_icicle_beam`, `wind_air_slash`) | 2 (`earth_tectonic_lance`, `light_zenith_lance`) | **10** |
| **cone** | 5 (`wood_bramble_whip`, `earth_dust_cloud`, `ice_claw_cleave`, `wind_shear_fan`, `light_photon_flash`) | 2 (`fire_ash_fan`, `shadow_abyssal_shroud`) | 3 (`metal_shard_volley`, `water_hydro_spray`, `thunder_fork_barrage`) | **10** |
| **aoe** | 3 (`fire_flare_nova`, `ice_permafrost_surge`, `wind_whirlwind_burst`) | 3 (`water_geyser_pool`, `thunder_storm_cell`, `light_halo_burst`) | 4 (`metal_anvil_drop`, `wood_timber_fall`, `earth_stalagmite_field`, `shadow_void_well`) | **10** |
| **dash** | 2 (`earth_stone_skate`, `light_luminous_glide`) | 3 (`metal_drill_drive`, `thunder_volt_drift`, `shadow_twilight_phase`) | 5 (`wood_vine_grapple`, `water_tsunami_charge`, `fire_meteor_rush`, `ice_avalanche_charge`, `wind_tailwind_dive`) | **10** |
| **Total** | **13** | **13** | **14** | **40** |

### Combined Batches 01 + 02 + 03 Grand Totals (120 Moves)

| Shape | Melee | Mid | Long | Total |
|---|---|---|---|---|
| **line** | 8 | 11 | 11 | **30** |
| **cone** | 12 | 11 | 7 | **30** |
| **aoe** | 10 | 8 | 12 | **30** |
| **dash** | 10 | 10 | 10 | **30** |
| **Total** | **40** | **40** | **40** | **120** |

## Prospective Kind "both" Candidates (P2 Integration)

As with earlier batches, several moves authored in Batch 03 were deliberately designed around gameplay sensations that will naturally accept secondary effects once the P2 closed rule vocabulary is established:
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
   - `wood_vine_grapple`: Pulls the user to the impact coordinate (or pulls light targets backward).
   - `thunder_volt_drift`: Teleport/drift motion granting momentary evasion window.
   - `shadow_twilight_phase`: Shadow phase shifting granting partial collision intangibility.

## Open Questions
1. **Targeting Origin Distinction for AoE**: Currently, `shape: "aoe"` encompasses both caster-centered novas (`fire_flare_nova`, `ice_permafrost_surge`, `wind_whirlwind_burst`) and targeted remote ground reticles (`metal_anvil_drop`, `earth_stalagmite_field`, `shadow_void_well`). As combat prototyping advances in P2, consider extending the schema or metadata to distinguish `aoe_self` from `aoe_ground`.
2. **Dash Trajectory vs. Teleport**: Skills like `shadow_twilight_phase` and `light_luminous_glide` straddle the border between high-speed physical translation and short-range blink/teleport. The collision subsystem will need parameters indicating whether hitboxes remain active and vulnerable along the traversed path.
3. **Horizon Horizon Cut-off for Planetary Long-Range Projectiles**: Long-range beam and volley attacks (`light_zenith_lance`, `earth_tectonic_lance`, `metal_shard_volley`) will require curvature-aware tracing on smaller celestial bodies to ensure raycasts follow planetary geodesic terrain without flying into orbit prematurely.
4. **Integration with P2-10 AI Combat Evaluator**: With 120 total damage moves spanning all 10 elements, AI combat behavior trees will need clear heuristics to pick between pokes, mid-band utility, and heavy finishers based on enemy distance and vulnerability windows.