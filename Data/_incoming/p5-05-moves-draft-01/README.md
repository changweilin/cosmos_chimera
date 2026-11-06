# Moves Draft Batch 01 (P5-05)

## Overview
This batch contains the initial draft of 40 placeholder damage moves for COSMIMERA (`moves/moves.json`), corresponding to roadmap item **P5-05**.
- **Scope**: Exactly 40 moves across all 10 elements (`metal`, `wood`, `water`, `fire`, `earth`, `ice`, `wind`, `thunder`, `light`, `shadow`), 4 moves per element.
- **Move Kind**: `damage` only (status / effect schema and vocabularies are deferred to P2; no `effect` block is included).
- **Draft Status**: All values are placeholder numbers for architecture and validation readiness; final balance and tuning will be executed by human designers.

## Number Bands & Balance Guidelines
All values are strictly integers following monotonic trade-offs (higher power corresponds to longer cooldowns and lower accuracy):
- **Power**: 20–120
- **Cooldown**: 800–10,000 ms
- **Accuracy**: 70–100%

### Range & Shape Distribution
- **Range Classes**: Each element includes at least one `melee`, one `mid`, and one `long` attack.
- **Shapes**: The four primary targeting shapes (`line`, `cone`, `aoe`, `dash`) are distributed equally across the batch (10 moves per shape, meeting the requirement of at least 5 each).

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
1. **Geometric / Spatial Parameters**: The current `move.schema.json` only tracks abstract `shape` and `range_class`. Concrete geometric dimensions (e.g., collision box dimensions, cone arc angles, dash distances/speeds, projectile speeds, and AoE radii) will need schema extensions or combat engine parameter definitions during P2/P3.
2. **Transition to Hybrid / Status Moves**: Several moves designed here (such as `ice_blizzard_vortex`, `thunder_spark_dash`, or `wood_spore_scatter`) naturally suggest crowd-control or status effects (e.g. freeze, paralyze, slow, poison/entangle). When the P2 effect vocabulary is finalized, human designers should review whether to convert these into `kind: "both"` with explicit effect definitions.
3. **Range Metric Standardization on Spherical Terrain**: Absolute unit boundaries between `melee`, `mid`, and `long` in relation to spherical planetary surface coordinates remain to be finalized in the combat design specification.
4. **Signature Move Assignments**: These 40 moves serve as a broad element pool. Once creature evolutionary lines and archetypes are authored in P5, signature moves (`signature_move`) can be selected and linked from this pool.
