# Traits Draft Batch 02 (P5-05)

## Overview
This batch contains the second draft of 30 new placeholder passive traits for COSMIMERA (`traits/traits.json`), continuing roadmap item **P5-05**.
- **Scope**: Exactly 30 brand new traits across all 10 elements (`metal`, `wood`, `water`, `fire`, `earth`, `ice`, `wind`, `thunder`, `light`, `shadow`), exactly 3 traits per element theme.
- **ID & Collision Rules**: All IDs follow `trait_<element>_<snake_name>`, with zero collisions against Batch 01 IDs or `Data/retired-ids.txt`.
- **Mechanics Non-Duplication**: No trait shares both the same trigger and the same effect type with any Batch 01 trait of the same element.
- **Data Model**: Conforms strictly to `trait.schema.json` and `common.schema.json`: `{ "id", "trigger": { "type", "params" }, "effects": [{ "type", "params" }] }` with no additional properties.
- **Closed Vocabulary**: Strictly restricted to the 8 trigger types (7 + `on_hp_cross_below`, Q-C18) and 7 effect types closed by P2-01 (`common.schema.json`). Integers only, no floats. Max 2 effects per trait.
- **Power/STR Rule Compliance**: Adheres to Q-C2; no trait affects base power or STR scaling directly.
- **Magnitude Balance**: Permanent (`always`) traits exhibit lower modifier magnitudes than conditional or temporary ones.
- **Side-Grade Balance**: Exactly 10 out of 30 traits (33.3%, roughly 1/3) incorporate explicit drawbacks (a negative delta for the holder), distributed evenly across the 10 elements (1 drawback trait per element).
- **Gap Coverage**: Designed specifically to fill coverage gaps from Batch 01, introducing 18 previously unrepresented (trigger, effect) combinations. Seven trigger types appear ≥ 3 times; `on_hp_cross_below` is absent. Every effect type appears ≥ 3 times.

---

## Vocabulary & Parameter Ranges

### Trigger Types

| Trigger Type | Parameter Schema | Parameter Range | Batch Count | Description |
|---|---|---|---|---|
| `always` | `{}` | N/A | 4 | Permanent passive effect active while creature is deployed. |
| `on_hp_below` | `{"percent": <int>}` | 10–50% | 4 | Activates when current HP falls below threshold. |
| `on_hp_above` | `{"percent": <int>}` | 50–100% | 3 | Active only while current HP remains above threshold. |
| `on_summon` | `{"duration_ms": <int>}` | 3,000–15,000 ms | 5 | Temporary effect triggered upon deployment to the battlefield. |
| `on_hit_taken` | `{"element": <element\|"any">, "duration_ms": <int>}` | 2,000–8,000 ms | 5 | Temporary buff upon receiving damage of the specified element or any element. |
| `on_move_used` | `{"element": <element>, "duration_ms": <int>}` | 2,000–8,000 ms | 5 | Temporary buff triggered after executing a move of the specified element. |
| `on_ally_swap` | `{"duration_ms": <int>}` | 2,000–8,000 ms | 4 | Temporary effect when swapping into battle or rotating allies. |
| `on_hp_cross_below` | `{"percent": <int>, "duration_ms": 1000}` | 10–50% | 0 | One-shot when HP crosses below the threshold. |
| **Total** | | | **30** | Seven trigger types occur at least 3 times; `on_hp_cross_below` is absent. |

### Effect Types

The original evasion design band is -10 to +15%; the table shows the observed batch values.

| Effect Type | Parameter Schema | Observed Parameter Range | Batch Count | Description |
|---|---|---|---|---|
| `mastery_mod` | `{"element": <element>, "delta": <int>}` | +10 to +25 | 5 | Modifies elemental mastery percentage for damage calculation. |
| `resist_mod` | `{"element": <element>, "delta": <int>}` | -15 to +35 | 7 | Modifies elemental resistance percentage (clamped to ±100 in combat). |
| `cd_mod_percent` | `{"element": <element\|"any">, "delta": <int>}` | -25 to +10% | 7 | Percentage cooldown modifier (negative = faster, positive = slower drawback). |
| `evasion_mod_percent` | `{"delta": <int>}` | -6 to +15% | 7 | Flat modifier to derived evasion roll. |
| `move_speed_mod_percent` | `{"delta": <int>}` | -10 to +20% | 5 | Percentage modifier to base movement speed. |
| `range_mod_percent` | `{"delta": <int>}` | -10 to +20% | 6 | Percentage modifier to attack reach and projectile range. |
| `heal_percent_max_hp` | `{"percent": <int>}` | 5 to 7% | 3 | Restores an integer percentage of maximum HP upon activation. |
| **Total** | | | **40** | Max 2 effects per trait (20 traits with 1 effect, 10 traits with 2 effects). Every effect type ≥ 3. |

---

## Element and Trait Distribution

| Element | Trait ID | Trigger Type | Trigger Params | Effects Summary | Drawback |
|---|---|---|---|---|---|
| **metal** | `trait_metal_siege_anchoring` | `on_summon` | 6000ms | `range_mod_percent: +20%`, `move_speed_mod_percent: -10%` | Yes (`move_speed -10%`) |
| **metal** | `trait_metal_clockwork_precision` | `on_hp_above` | 80% | `cd_mod_percent: metal -20%` | None |
| **metal** | `trait_metal_reinforce_protocol` | `on_ally_swap` | 5000ms | `cd_mod_percent: any -15%` | None |
| **wood** | `trait_wood_canopy_reach` | `always` | `{}` | `range_mod_percent: +8%`, `evasion_mod_percent: -5%` | Yes (`evasion -5%`) |
| **wood** | `trait_wood_bramble_wall` | `on_hit_taken` | `wood`, 4000ms | `resist_mod: earth +25` | None |
| **wood** | `trait_wood_spore_regeneration` | `on_move_used` | `wood`, 3000ms | `heal_percent_max_hp: 5%` | None |
| **water** | `trait_water_calm_waters` | `on_hp_above` | 75% | `cd_mod_percent: water -20%`, `resist_mod: thunder -15` | Yes (`resist thunder -15`) |
| **water** | `trait_water_undertow_escape` | `on_hp_below` | 20% | `evasion_mod_percent: +15%` | None |
| **water** | `trait_water_spring_surge` | `on_ally_swap` | 5000ms | `heal_percent_max_hp: 7%` | None |
| **fire** | `trait_fire_pyroclastic_burst` | `on_summon` | 5000ms | `mastery_mod: fire +25`, `evasion_mod_percent: -6%` | Yes (`evasion -6%`) |
| **fire** | `trait_fire_wildfire_momentum` | `on_hit_taken` | `fire`, 4000ms | `move_speed_mod_percent: +20%` | None |
| **fire** | `trait_fire_conflagration_flare` | `on_hp_below` | 30% | `range_mod_percent: +20%` | None |
| **earth** | `trait_earth_crag_attunement` | `always` | `{}` | `mastery_mod: earth +12`, `move_speed_mod_percent: -8%` | Yes (`move_speed -8%`) |
| **earth** | `trait_earth_landslide_charge` | `on_move_used` | `earth`, 4000ms | `move_speed_mod_percent: +15%` | None |
| **earth** | `trait_earth_unyielding_mantle` | `on_hit_taken` | `earth`, 5000ms | `resist_mod: water +25` | None |
| **ice** | `trait_ice_glacier_bastion` | `on_hp_below` | 30% | `resist_mod: ice +35`, `cd_mod_percent: any +10%` | Yes (`cd any +10%`) |
| **ice** | `trait_ice_rime_shatter` | `on_ally_swap` | 4000ms | `cd_mod_percent: ice -20%` | None |
| **ice** | `trait_ice_frostbite_edge` | `on_move_used` | `ice`, 4000ms | `mastery_mod: ice +20` | None |
| **wind** | `trait_wind_breeze_cadence` | `always` | `{}` | `cd_mod_percent: wind -10%`, `resist_mod: ice -15` | Yes (`resist ice -15`) |
| **wind** | `trait_wind_aloft_vigilance` | `on_hp_above` | 75% | `evasion_mod_percent: +8%` | None |
| **wind** | `trait_wind_gale_gust` | `on_summon` | 5000ms | `range_mod_percent: +20%` | None |
| **thunder** | `trait_thunder_lightning_pivot` | `on_ally_swap` | 4000ms | `evasion_mod_percent: +10%`, `resist_mod: earth -15` | Yes (`resist earth -15`) |
| **thunder** | `trait_thunder_discharge_sprint` | `on_hit_taken` | `thunder`, 4000ms | `move_speed_mod_percent: +20%` | None |
| **thunder** | `trait_thunder_fulgur_reach` | `on_move_used` | `thunder`, 3000ms | `range_mod_percent: +15%` | None |
| **light** | `trait_light_radiant_core` | `always` | `{}` | `mastery_mod: light +10`, `resist_mod: shadow -15` | Yes (`resist shadow -15`) |
| **light** | `trait_light_blinding_arrival` | `on_summon` | 5000ms | `evasion_mod_percent: +8%` | None |
| **light** | `trait_light_halo_rebound` | `on_move_used` | `light`, 3000ms | `heal_percent_max_hp: 6%` | None |
| **shadow** | `trait_shadow_nightfall_creed` | `on_summon` | 6000ms | `mastery_mod: shadow +25`, `range_mod_percent: -10%` | Yes (`range -10%`) |
| **shadow** | `trait_shadow_smoke_veil` | `on_hit_taken` | `any`, 4000ms | `evasion_mod_percent: +10%` | None |
| **shadow** | `trait_shadow_abyssal_haste` | `on_hp_below` | 30% | `cd_mod_percent: shadow -25%` | None |

---

## Hand-Counted Totals

### Traits per Element (Exactly 3 per Element)
- `metal`: 3
- `wood`: 3
- `water`: 3
- `fire`: 3
- `earth`: 3
- `ice`: 3
- `wind`: 3
- `thunder`: 3
- `light`: 3
- `shadow`: 3
- **Total Traits**: **30**

### Triggers Distribution
- `always`: 4 traits
- `on_hp_below`: 4 traits
- `on_hp_above`: 3 traits
- `on_summon`: 5 traits
- `on_hit_taken`: 5 traits
- `on_move_used`: 5 traits
- `on_ally_swap`: 4 traits
- `on_hp_cross_below`: 0 traits
- **Total Triggers**: **30**

### Effects Distribution (All ≥ 3)
- `mastery_mod`: 5 instances
- `resist_mod`: 7 instances
- `cd_mod_percent`: 7 instances
- `evasion_mod_percent`: 7 instances
- `move_speed_mod_percent`: 5 instances
- `range_mod_percent`: 6 instances
- `heal_percent_max_hp`: 3 instances
- **Total Effect Instances**: **40** (20 traits with 1 effect, 10 traits with 2 effects)

### Side-Grade / Drawback Breakdown (Exactly 10 of 30, 33.3%)
- `move_speed_mod_percent`: 2 drawbacks (`trait_metal_siege_anchoring`: -10%, `trait_earth_crag_attunement`: -8%)
- `evasion_mod_percent`: 2 drawbacks (`trait_wood_canopy_reach`: -5%, `trait_fire_pyroclastic_burst`: -6%)
- `resist_mod`: 4 drawbacks (`trait_water_calm_waters`: -15 thunder, `trait_wind_breeze_cadence`: -15 ice, `trait_thunder_lightning_pivot`: -15 earth, `trait_light_radiant_core`: -15 shadow)
- `cd_mod_percent`: 1 drawback (`trait_ice_glacier_bastion`: +10% any)
- `range_mod_percent`: 1 drawback (`trait_shadow_nightfall_creed`: -10%)
- **Total Side-Grade Traits**: **10** (exactly 1 per element)

### Permanent (`always`) Magnitude Check
All permanent traits utilize strictly conservative values compared to conditional activations:
- `trait_wood_canopy_reach`: `range_mod_percent: +8%` (conditional ranges are +15% to +20%)
- `trait_earth_crag_attunement`: `mastery_mod: +12` (conditional masteries are +20 to +25)
- `trait_wind_breeze_cadence`: `cd_mod_percent: -10%` (conditional cooldown reductions are -20% to -25%)
- `trait_light_radiant_core`: `mastery_mod: +10` (conditional masteries are +20 to +25)

---

## Coverage Gap Analysis (Batch 01 vs. Batch 02)

In Batch 01, out of 56 theoretical combinations (8 triggers × 7 effects), 27 were utilized. Batch 02 specifically addresses this by introducing **18 new (trigger, effect) combinations** that Batch 01 completely lacked:

| Trigger Type | Effect Type | Lacked in Batch 01? | Added in Batch 02? | Trait IDs in Batch 02 |
|---|---|:---:|:---:|---|
| `always` | `cd_mod_percent` | **YES** | **YES** | `trait_wind_breeze_cadence` |
| `always` | `mastery_mod` | **YES** | **YES** | `trait_earth_crag_attunement`, `trait_light_radiant_core` |
| `always` | `range_mod_percent` | **YES** | **YES** | `trait_wood_canopy_reach` |
| `on_ally_swap` | `cd_mod_percent` | **YES** | **YES** | `trait_metal_reinforce_protocol`, `trait_ice_rime_shatter` |
| `on_ally_swap` | `evasion_mod_percent` | **YES** | **YES** | `trait_thunder_lightning_pivot` |
| `on_ally_swap` | `heal_percent_max_hp` | **YES** | **YES** | `trait_water_spring_surge` |
| `on_hit_taken` | `evasion_mod_percent` | **YES** | **YES** | `trait_shadow_smoke_veil` |
| `on_hit_taken` | `move_speed_mod_percent` | **YES** | **YES** | `trait_fire_wildfire_momentum`, `trait_thunder_discharge_sprint` |
| `on_hit_taken` | `resist_mod` | **YES** | **YES** | `trait_wood_bramble_wall`, `trait_earth_unyielding_mantle` |
| `on_hp_above` | `cd_mod_percent` | **YES** | **YES** | `trait_metal_clockwork_precision`, `trait_water_calm_waters` |
| `on_hp_above` | `evasion_mod_percent` | **YES** | **YES** | `trait_wind_aloft_vigilance` |
| `on_hp_below` | `cd_mod_percent` | **YES** | **YES** | `trait_ice_glacier_bastion`, `trait_shadow_abyssal_haste` |
| `on_hp_below` | `range_mod_percent` | **YES** | **YES** | `trait_fire_conflagration_flare` |
| `on_move_used` | `heal_percent_max_hp` | **YES** | **YES** | `trait_wood_spore_regeneration`, `trait_light_halo_rebound` |
| `on_move_used` | `move_speed_mod_percent` | **YES** | **YES** | `trait_earth_landslide_charge` |
| `on_summon` | `evasion_mod_percent` | **YES** | **YES** | `trait_fire_pyroclastic_burst`, `trait_light_blinding_arrival` |
| `on_summon` | `mastery_mod` | **YES** | **YES** | `trait_fire_pyroclastic_burst`, `trait_shadow_nightfall_creed` |
| `on_summon` | `range_mod_percent` | **YES** | **YES** | `trait_metal_siege_anchoring`, `trait_wind_gale_gust`, `trait_shadow_nightfall_creed` |

---

## Open Issues

1. **State vs. Event Semantics for Healing**: Healing effects (`heal_percent_max_hp`) are now paired with event triggers (`on_move_used`, `on_ally_swap`, `on_hit_taken`, `on_summon`). Unlike continuous HP checks, these have discrete firing instants. However, combat engine specifications must define whether move-based heals can trigger multiple times within a short duration if moves are spammed, or if an internal cooldown (ICD) is enforced per trait.
2. **Compound Multi-Effect Timing**: Traits with both positive buffs and drawbacks (e.g., `range_mod_percent: +20%` and `move_speed_mod_percent: -10%` on summon) apply both effects over the same trigger window (`duration_ms: 6000`). If one effect is cleansed or modified by status effects, does the linked drawback persist independently?
3. ~~**P2 Formal Schema Constraints**: Trigger/effect parameters need strict per-type validation.~~ 已由 P2-01 結案：`common.schema.json` validates parameters for each type.
4. **Range Modifier Interaction with Melee Moves**: `range_mod_percent` modifies attack reach and projectile distance. Combat logic will need to clamp minimum and maximum hitbox scaling for melee shapes (e.g., slash arcs and point-blank bursts) to prevent animation distortion or collision clipping.

## 修訂 2026-11-07（P5-08 A1/A14，使用者裁決）
執行期完全同效的特性改綁各自主題；分佈與覆蓋統計已於 2026-11-08 從 `traits.json` 重新產生：
- `trait_water_undertow_escape`：HP<35% 移速 +20% → HP<20% 閃避 +15%（原與 `trait_wind_desperation_updraft` 完全相同；門檻 20% 依 SOL 複核，避免嚴格支配 `trait_shadow_void_embrace` HP<25%／+10%）。
