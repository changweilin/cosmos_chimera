# Traits Draft Batch 01 (P5-05)

## Overview
This batch contains the initial draft of 30 placeholder passive traits for COSMIMERA (`traits/traits.json`), corresponding to roadmap item **P5-05**.
- **Scope**: Exactly 30 traits across all 10 elements (`metal`, `wood`, `water`, `fire`, `earth`, `ice`, `wind`, `thunder`, `light`, `shadow`), exactly 3 traits per element theme.
- **Data Model**: Follows `trait.schema.json` strictly: `{ "id", "trigger": { "type", "params" }, "effects": [{ "type", "params" }] }` with no additional properties.
- **Closed Vocabulary**: Trigger and effect types and their parameters were closed in P2-01 (`common.schema.json`).
- **Side-Grade Balance**: 11 of the 30 traits incorporate explicit drawbacks (a negative delta for the holder) to provide meaningful trade-offs and build variety.
- **Placeholder Values**: All values are integers adhering strictly to specification ranges; unconditional (`always`) traits have lower magnitudes than conditional triggers.

## Vocabulary Tables

### Trigger Types

| Trigger Type | Parameter Schema | Parameter Range | Batch Count | Description |
|---|---|---|---|---|
| `always` | `{}` | N/A | 5 | Permanent passive effect while creature is active. |
| `on_hp_below` | `{"percent": <int>}` | 10–50% | 3 | Activates when current HP falls below threshold. |
| `on_hp_above` | `{"percent": <int>}` | 50–100% | 4 | Active only while current HP remains above threshold. |
| `on_summon` | `{"duration_ms": <int>}` | 3,000–15,000 ms | 4 | Temporary effect triggered upon deployment to the battlefield. |
| `on_hit_taken` | `{"element": <element\|"any">, "duration_ms": <int>}` | 2,000–8,000 ms | 5 | Temporary buff upon receiving damage of the specified element or any element. |
| `on_move_used` | `{"element": <element>, "duration_ms": <int>}` | 2,000–8,000 ms | 4 | Temporary buff triggered after executing a move of the specified element. |
| `on_ally_swap` | `{"duration_ms": <int>}` | 2,000–8,000 ms | 4 | Temporary effect when swapping into battle or rotating allies. |
| `on_hp_cross_below` | `{"percent": <int>, "duration_ms": 1000}` | 10–50% | 1 | One-shot when HP crosses below the threshold (Q-C18, 2026-11-07); instant heals only. |
| **Total** | | | **30** | Not every trigger type reaches 3 (see counts; on_hp_cross_below / migrations). |

### Effect Types

| Effect Type | Parameter Schema | Observed Parameter Range | Batch Count | Description |
|---|---|---|---|---|
| `mastery_mod` | `{"element": <element>, "delta": <int>}` | +15 to +30 | 7 | Modifies elemental mastery percentage for damage calculation. |
| `resist_mod` | `{"element": <element>, "delta": <int>}` | -20 to +35 | 12 | Modifies elemental resistance percentage (clamped to ±100 in combat). |
| `cd_mod_percent` | `{"element": <element\|"any">, "delta": <int>}` | -25 to +10% | 5 | Percentage cooldown modifier (negative = faster, positive = slower drawback). |
| `evasion_mod_percent` | `{"delta": <int>}` | -6 to +10% | 5 | Flat modifier to derived evasion roll. |
| `move_speed_mod_percent` | `{"delta": <int>}` | -10 to +20% | 5 | Percentage modifier to base movement speed. |
| `range_mod_percent` | `{"delta": <int>}` | -10 to +20% | 4 | Percentage modifier to attack reach and projectile range (governed by STR scale). |
| `heal_percent_max_hp` | `{"percent": <int>}` | 6 to 12% | 3 | Restores an integer percentage of maximum HP upon activation. |
| **Total** | | | **41** | Max 2 effects per trait (19 traits with 1 effect, 11 traits with 2 effects). |

## Element and Trigger Distribution

| Element | Trait ID | Trigger Type | Trigger Params | Effects Summary | Drawback |
|---|---|---|---|---|---|
| **metal** | `trait_metal_dense_alloy` | `always` | `{}` | `resist_mod: metal +15`, `move_speed_mod_percent: -8%` | Yes (`move_speed -8%`) |
| **metal** | `trait_metal_magnetic_recoil` | `on_hit_taken` | `metal`, 4000ms | `range_mod_percent: +15%` | None |
| **metal** | `trait_metal_tempered_edge` | `on_move_used` | `metal`, 3000ms | `mastery_mod: metal +25`, `cd_mod_percent: any +10%` | Yes (`cd +10%`) |
| **wood** | `trait_wood_photosynthesis` | `on_summon` | 6000ms | `heal_percent_max_hp: 8%` | None |
| **wood** | `trait_wood_bark_reinforcement` | `always` | `{}` | `resist_mod: earth +20`, `resist_mod: fire -15` | Yes (`resist fire -15%`) |
| **wood** | `trait_wood_verdant_resonance` | `on_ally_swap` | 5000ms | `mastery_mod: wood +20` | None |
| **water** | `trait_water_fluid_stance` | `always` | `{}` | `evasion_mod_percent: +6%`, `resist_mod: thunder -15` | Yes (`resist thunder -15%`) |
| **water** | `trait_water_torrent_flow` | `on_move_used` | `water`, 4000ms | `cd_mod_percent: water -20%` | None |
| **water** | `trait_water_cleansing_tide` | `on_hp_cross_below` (Q-C18, 2026-11-07; was `on_hp_below`) | 30%, 1000ms | `heal_percent_max_hp: 12%` | None |
| **fire** | `trait_fire_blazing_fury` | `on_hp_below` | 35% | `mastery_mod: fire +30`, `resist_mod: water -20` | Yes (`resist water -20%`) |
| **fire** | `trait_fire_heat_exhaust` | `on_summon` | 5000ms | `move_speed_mod_percent: +20%` | None |
| **fire** | `trait_fire_thermal_recharge` | `on_hit_taken` | `fire`, 5000ms | `cd_mod_percent: fire -25%` | None |
| **earth** | `trait_earth_monolith_posture` | `always` | `{}` | `resist_mod: earth +25`, `evasion_mod_percent: -6%` | Yes (`evasion -6%`) |
| **earth** | `trait_earth_tectonic_rebound` | `on_hit_taken` | `any`, 4000ms | `mastery_mod: earth +15` | None |
| **earth** | `trait_earth_stone_bastion` | `on_hp_above` | 80% | `resist_mod: fire +20` | None |
| **ice** | `trait_ice_permafrost_shell` | `always` | `{}` | `resist_mod: ice +20`, `move_speed_mod_percent: -10%` | Yes (`move_speed -10%`) |
| **ice** | `trait_ice_glacial_patience` | `on_hp_above` | 75% | `range_mod_percent: +15%` | None |
| **ice** | `trait_ice_flash_freeze` | `on_summon` | 4000ms | `resist_mod: ice +35` | None |
| **wind** | `trait_wind_tailwind_glide` | `on_ally_swap` | 6000ms | `move_speed_mod_percent: +20%` | None |
| **wind** | `trait_wind_cyclone_instability` | `on_move_used` | `wind`, 4000ms | `range_mod_percent: +20%`, `evasion_mod_percent: -5%` | Yes (`evasion -5%`) |
| **wind** | `trait_wind_feather_grace` | `on_hp_below` | 40% | `evasion_mod_percent: +10%` | None |
| **thunder** | `trait_thunder_static_overload` | `on_move_used` | `thunder`, 3000ms | `mastery_mod: thunder +25`, `resist_mod: thunder -15` | Yes (`resist thunder -15%`) |
| **thunder** | `trait_thunder_grounding_circuit` | `on_hit_taken` | `thunder`, 5000ms | `heal_percent_max_hp: 6%` | None |
| **thunder** | `trait_thunder_spark_stride` | `on_summon` | 4000ms | `cd_mod_percent: any -20%` | None |
| **light** | `trait_light_aurora_veil` | `on_ally_swap` | 5000ms | `resist_mod: shadow +25` | None |
| **light** | `trait_light_solar_overdrive` | `on_hp_above` | 70% | `mastery_mod: light +20`, `range_mod_percent: -10%` | Yes (`range -10%`) |
| **light** | `trait_light_dawn_renewal` | `on_hit_taken` | `shadow`, 3000ms | `cd_mod_percent: light -15%` | None |
| **shadow** | `trait_shadow_void_embrace` | `on_hp_below` | 25% | `evasion_mod_percent: +15%` | None |
| **shadow** | `trait_shadow_umbral_ambush` | `on_ally_swap` | 4000ms | `mastery_mod: shadow +25`, `resist_mod: light -20` | Yes (`resist light -20%`) |
| **shadow** | `trait_shadow_dusk_flicker` | `on_hp_above` | 90% | `move_speed_mod_percent: +20%` | None |

### Trigger Distribution Summary
- `always`: 5 traits
- `on_hp_below`: 3 traits
- `on_hp_above`: 4 traits
- `on_summon`: 4 traits
- `on_hit_taken`: 5 traits
- `on_move_used`: 4 traits
- `on_ally_swap`: 4 traits
- `on_hp_cross_below`: 1 trait
- **Total**: 30 traits (on_hp_cross_below appears once)

## Open Issues
1. ~~**P2 Enum Closure and Schema Validation**: Trigger and effect types use open string IDs and need closed enums and parameter validation.~~ 已由 P2-01 結案：`common.schema.json` defines closed types and per-type parameters.
2. **Buff Stacking and Re-Trigger Semantics**: When a timed trigger fires repeatedly (e.g., `on_hit_taken` while already under the buff duration), the combat engine must specify whether the duration refreshes, stacks additively, or creates parallel independent buff instances.
3. **Internal Cooldowns on Conditional Triggers**: Triggers like `on_hp_below` or `on_hit_taken` that provide healing (`heal_percent_max_hp`) need clear internal cooldown or state machine boundaries to prevent repeated firing if HP fluctuates around the threshold.
4. **Target Scope for Ally/Aura Passives**: Current trait schema models effects applying directly to the trait holder. Once team synergies and secondary creature (`副手`) passives are implemented, an explicit target scope property (e.g. `self`, `active_ally`, `party`) may be required.
5. **Secondary Creature Trait Borrowing**: As established in `docs/design/creatures.md#隊伍規則`, primary creatures can inherit one trait from a linked secondary creature. Stacking behavior between a creature's innate trait and an inherited secondary trait sharing the same modifier type will need design tuning.
