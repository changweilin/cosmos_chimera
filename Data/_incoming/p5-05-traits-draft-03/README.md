# Traits Draft Batch 03 (P5-05)

## Overview
This batch contains the third draft of 30 new placeholder passive traits for COSMIMERA (`traits/traits.json`), continuing roadmap item **P5-05**.
- **Scope**: Exactly 30 brand new traits across all 10 elements (`metal`, `wood`, `water`, `fire`, `earth`, `ice`, `wind`, `thunder`, `light`, `shadow`), exactly 3 traits per element theme.
- **ID & Collision Rules**: All IDs follow `trait_<element>_<snake_name>`, with zero collisions against Batch 01 IDs, Batch 02 IDs, or `Data/retired-ids.txt`.
- **Mechanics Non-Duplication**: No trait shares both the same trigger and the same effect type combination with any Batch 01 or Batch 02 trait of the same element.
- **Data Model**: Conforms strictly to `trait.schema.json` and `common.schema.json`: `{ "id", "trigger": { "type", "params" }, "effects": [{ "type", "params" }] }` with no additional properties.
- **Closed Vocabulary**: Strictly restricted to the 8 trigger types (7 + `on_hp_cross_below`, Q-C18) and 7 effect types closed in P2-01 (`common.schema.json`). Integers only, no floats. Max 2 effects per trait.
- **Power/STR Rule Compliance**: Adheres to Q-C2; no trait affects base power or STR scaling directly.
- **Magnitude Balance**: Permanent (`always`) traits generally exhibit lower modifier magnitudes than conditional or temporary ones; the cooldown reduction matches one conditional trait.
- **Side-Grade Balance**: Exactly 10 out of 30 traits (33.3%, roughly 1/3) incorporate explicit drawbacks (a negative delta for the holder), distributed evenly across the 10 elements (exactly 1 drawback trait per element).
- **Target Gap Coverage**: Specifically targets low-frequency (trigger, effect) combinations from Batch 01 and Batch 02, reinforcing 18 combinations that had ≤ 1 prior occurrence (including resolving the 0-occurrence combination `on_ally_swap` + `range_mod_percent`). `on_summon` appears twice and `on_hp_cross_below` once; every effect type appears ≥ 3 times.

---

## Vocabulary & Parameter Ranges

### Trigger Types

| Trigger Type | Parameter Schema | Parameter Range | Batch Count | Description |
|---|---|---|---|---|
| `always` | `{}` | N/A | 3 | Permanent passive effect active while creature is deployed. |
| `on_hp_below` | `{"percent": <int>}` | 10–50% | 5 | Activates when current HP falls below threshold. |
| `on_hp_above` | `{"percent": <int>}` | 50–100% | 4 | Active only while current HP remains above threshold. |
| `on_summon` | `{"duration_ms": <int>}` | 3,000–15,000 ms | 2 | Temporary effect triggered upon deployment to the battlefield. |
| `on_hit_taken` | `{"element": <element\|"any">, "duration_ms": <int>}` | 2,000–8,000 ms | 6 | Temporary buff upon receiving damage of the specified element or any element. |
| `on_move_used` | `{"element": <element>, "duration_ms": <int>}` | 2,000–8,000 ms | 6 | Temporary buff triggered after executing a move of the specified element. |
| `on_ally_swap` | `{"duration_ms": <int>}` | 2,000–8,000 ms | 3 | Temporary effect when swapping into battle or rotating allies. |
| `on_hp_cross_below` | `{"percent": <int>, "duration_ms": 1000}` | 10–50% | 1 | One-shot when HP crosses below the threshold (Q-C18, 2026-11-07); instant heals only. |
| **Total** | | | **30** | Not every trigger type reaches 3 (see counts; on_hp_cross_below / migrations). |

### Effect Types

| Effect Type | Parameter Schema | Observed Parameter Range | Batch Count | Description |
|---|---|---|---|---|
| `mastery_mod` | `{"element": <element>, "delta": <int>}` | +15 to +25 | 6 | Modifies elemental mastery percentage for damage calculation. |
| `resist_mod` | `{"element": <element>, "delta": <int>}` | -15 to +30 | 8 | Modifies elemental resistance percentage (clamped to ±100 in combat). |
| `cd_mod_percent` | `{"element": <element\|"any">, "delta": <int>}` | -20 to -10% | 4 | Percentage cooldown modifier (negative = faster, positive = slower drawback). |
| `evasion_mod_percent` | `{"delta": <int>}` | -6 to +10% | 5 | Flat modifier to derived evasion roll. |
| `move_speed_mod_percent` | `{"delta": <int>}` | -10 to +20% | 7 | Percentage modifier to base movement speed. |
| `range_mod_percent` | `{"delta": <int>}` | +8 to +20% | 7 | Percentage modifier to attack reach and projectile range. |
| `heal_percent_max_hp` | `{"percent": <int>}` | 4 to 12% | 3 | Restores an integer percentage of maximum HP upon activation. |
| **Total** | | | **40** | Max 2 effects per trait (20 traits with 1 effect, 10 traits with 2 effects). Every effect type ≥ 3. |

---

## Element and Trait Distribution

| Element | Trait ID | Trigger Type | Trigger Params | Effects Summary | Drawback |
|---|---|---|---|---|---|
| **metal** | `trait_metal_scrap_overdrive` | `on_hp_below` | 30% | `mastery_mod: metal +25`, `move_speed_mod_percent: -10%` | Yes (`move_speed -10%`) |
| **metal** | `trait_metal_ballistic_pivot` | `on_ally_swap` | 4000ms | `range_mod_percent: +20%` | None |
| **metal** | `trait_metal_conductive_plating` | `on_hit_taken` | `thunder`, 4000ms | `cd_mod_percent: metal -20%` | None |
| **wood** | `trait_wood_overgrowth_surge` | `on_hp_above` | 75% | `move_speed_mod_percent: +15%`, `resist_mod: fire -15` | Yes (`resist fire -15`) |
| **wood** | `trait_wood_thorn_lash` | `on_hp_below` | 35% | `range_mod_percent: +20%` | None |
| **wood** | `trait_wood_sap_absorption` | `on_hit_taken` | `water`, 4000ms | `heal_percent_max_hp: 6%` | None |
| **water** | `trait_water_torrential_deluge` | `on_move_used` | `water`, 4000ms | `range_mod_percent: +15%`, `move_speed_mod_percent: -10%` | Yes (`move_speed -10%`) |
| **water** | `trait_water_revitalizing_spring` | `on_move_used` (water) | 5000ms | `heal_percent_max_hp: 4%` | None |
| **water** | `trait_water_rippling_counter` | `on_hit_taken` | `water`, 4000ms | `mastery_mod: water +20` | None |
| **fire** | `trait_fire_smoldering_core` | `always` | `{}` | `cd_mod_percent: fire -10%`, `resist_mod: water -15` | Yes (`resist water -15`) |
| **fire** | `trait_fire_scorching_momentum` | `on_hp_above` | 80% | `move_speed_mod_percent: +15%` | None |
| **fire** | `trait_fire_cinder_screen` | `on_move_used` | `fire`, 3000ms | `evasion_mod_percent: +8%` | None |
| **earth** | `trait_earth_shattered_carapace` | `on_hp_below` | 30% | `range_mod_percent: +30%`, `resist_mod: earth -15` | Yes (`resist earth -15`) |
| **earth** | `trait_earth_grounding_mantle` | `on_summon` | 6000ms | `resist_mod: thunder +30` | None |
| **earth** | `trait_earth_faultline_shift` | `on_ally_swap` | 5000ms | `range_mod_percent: +15%` | None |
| **ice** | `trait_ice_hoarfrost_tread` | `on_move_used` | `ice`, 4000ms | `move_speed_mod_percent: +15%`, `resist_mod: fire -15` | Yes (`resist fire -15`) |
| **ice** | `trait_ice_mirror_frost` | `on_hit_taken` | `any`, 4000ms | `range_mod_percent: +15%` | None |
| **ice** | `trait_ice_subzero_focus` | `on_hp_above` | 80% | `mastery_mod: ice +20` | None |
| **wind** | `trait_wind_tempest_headway` | `on_summon` | 5000ms | `cd_mod_percent: wind -20%`, `evasion_mod_percent: -6%` | Yes (`evasion -6%`) |
| **wind** | `trait_wind_slipstream_dodge` | `on_hit_taken` | `wind`, 4000ms | `evasion_mod_percent: +10%` | None |
| **wind** | `trait_wind_desperation_updraft` | `on_hp_below` | 35% | `move_speed_mod_percent: +20%` | None |
| **thunder** | `trait_thunder_plasma_charge` | `always` | `{}` | `range_mod_percent: +8%`, `resist_mod: earth -15` | Yes (`resist earth -15`) |
| **thunder** | `trait_thunder_crisis_arc` | `on_hp_below` | 30% | `mastery_mod: thunder +25` | None |
| **thunder** | `trait_thunder_supercharged_stride` | `on_hp_above` | 80% | `cd_mod_percent: thunder -10%` | None |
| **light** | `trait_light_luminous_barrier` | `on_move_used` | `light`, 4000ms | `resist_mod: shadow +25`, `evasion_mod_percent: -5%` | Yes (`evasion -5%`) |
| **light** | `trait_light_saving_grace` | `on_hp_cross_below` (Q-C18, 2026-11-07; was `on_hp_below`) | 25%, 1000ms | `heal_percent_max_hp: 15%` | None |
| **light** | `trait_light_guiding_beam` | `on_ally_swap` | 5000ms | `mastery_mod: light +15` | None |
| **shadow** | `trait_shadow_umbral_cloak` | `always` | `{}` | `evasion_mod_percent: +5%`, `resist_mod: light -15` | Yes (`resist light -15`) |
| **shadow** | `trait_shadow_fade_step` | `on_move_used` | `shadow`, 3000ms | `move_speed_mod_percent: +15%` | None |
| **shadow** | `trait_shadow_dark_retaliation` | `on_hit_taken` | `shadow`, 4000ms | `mastery_mod: shadow +20` | None |

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
- `always`: 3 traits
- `on_hp_below`: 5 traits
- `on_hp_above`: 4 traits
- `on_summon`: 2 traits
- `on_hit_taken`: 6 traits
- `on_move_used`: 6 traits
- `on_ally_swap`: 3 traits
- `on_hp_cross_below`: 1 trait
- **Total Triggers**: **30**

### Effects Distribution (All ≥ 3)
- `mastery_mod`: 6 instances
- `resist_mod`: 8 instances
- `cd_mod_percent`: 4 instances
- `evasion_mod_percent`: 5 instances
- `move_speed_mod_percent`: 7 instances
- `range_mod_percent`: 7 instances
- `heal_percent_max_hp`: 3 instances
- **Total Effect Instances**: **40** (20 traits with 1 effect, 10 traits with 2 effects)

### Side-Grade / Drawback Breakdown (Exactly 10 of 30, 33.3%)
- `resist_mod`: 6 drawbacks (`trait_wood_overgrowth_surge`: fire -15, `trait_fire_smoldering_core`: water -15, `trait_earth_shattered_carapace`: earth -15, `trait_ice_hoarfrost_tread`: fire -15, `trait_thunder_plasma_charge`: earth -15, `trait_shadow_umbral_cloak`: light -15)
- `move_speed_mod_percent`: 2 drawbacks (`trait_metal_scrap_overdrive`: -10%, `trait_water_torrential_deluge`: -10%)
- `evasion_mod_percent`: 2 drawbacks (`trait_wind_tempest_headway`: -6%, `trait_light_luminous_barrier`: -5%)
- **Total Side-Grade Traits**: **10** (exactly 1 per element)

### Permanent (`always`) Magnitude Check
Permanent traits use conservative values; the cooldown reduction equals the smallest conditional reduction:
- `trait_fire_smoldering_core`: `cd_mod_percent: fire -10%` (conditional cooldown reductions are -10% to -20%)
- `trait_thunder_plasma_charge`: `range_mod_percent: +8%` (conditional ranges are +15% to +20%)
- `trait_shadow_umbral_cloak`: `evasion_mod_percent: +5%` (conditional evasions are +8% to +10%)

---

## Coverage Gap Analysis (Batch 01 + Batch 02 vs. Batch 03)

In Batch 01 and Batch 02 combined, several (trigger, effect) combinations remained unrepresented (0 occurrences) or severely under-represented (only 1 occurrence). Batch 03 explicitly reinforces **18 low-frequency combinations** (count ≤ 1 across B01+B02), achieving balanced matrix coverage:

| Trigger Type | Effect Type | B01+B02 Count | Added in B03 | B03 Trait IDs | New Combined Count |
|---|---|:---:|:---:|---|:---:|
| `always` | `cd_mod_percent` | **1** | **+1** | `trait_fire_smoldering_core` | **2** |
| `always` | `range_mod_percent` | **1** | **+1** | `trait_thunder_plasma_charge` | **2** |
| `on_ally_swap` | `range_mod_percent` | **0** | **+2** | `trait_metal_ballistic_pivot`, `trait_earth_faultline_shift` | **2** |
| `on_hit_taken` | `evasion_mod_percent` | **1** | **+1** | `trait_wind_slipstream_dodge` | **2** |
| `on_hit_taken` | `heal_percent_max_hp` | **1** | **+1** | `trait_wood_sap_absorption` | **2** |
| `on_hit_taken` | `mastery_mod` | **1** | **+2** | `trait_water_rippling_counter`, `trait_shadow_dark_retaliation` | **3** |
| `on_hit_taken` | `range_mod_percent` | **1** | **+1** | `trait_ice_mirror_frost` | **2** |
| `on_hp_above` | `mastery_mod` | **1** | **+1** | `trait_ice_subzero_focus` | **2** |
| `on_hp_above` | `move_speed_mod_percent` | **1** | **+2** | `trait_wood_overgrowth_surge`, `trait_fire_scorching_momentum` | **3** |
| `on_hp_below` | `mastery_mod` | **1** | **+2** | `trait_metal_scrap_overdrive`, `trait_thunder_crisis_arc` | **3** |
| `on_hp_below` | `move_speed_mod_percent` | **0** | **+2** | `trait_metal_scrap_overdrive`, `trait_wind_desperation_updraft` | **2** |
| `on_hp_below` | `range_mod_percent` | **1** | **+2** | `trait_wood_thorn_lash`, `trait_earth_shattered_carapace` | **3** |
| `on_hp_cross_below` | `heal_percent_max_hp` | **1** | **+1** | `trait_light_saving_grace` | **2** |
| `on_move_used` | `evasion_mod_percent` | **1** | **+2** | `trait_fire_cinder_screen`, `trait_light_luminous_barrier` | **3** |
| `on_move_used` | `move_speed_mod_percent` | **1** | **+3** | `trait_water_torrential_deluge`, `trait_ice_hoarfrost_tread`, `trait_shadow_fade_step` | **4** |
| `on_move_used` | `resist_mod` | **1** | **+2** | `trait_ice_hoarfrost_tread`, `trait_light_luminous_barrier` | **3** |
| `on_summon` | `cd_mod_percent` | **1** | **+1** | `trait_wind_tempest_headway` | **2** |
| `on_summon` | `resist_mod` | **1** | **+1** | `trait_earth_grounding_mantle` | **2** |

Other revised combinations have more than one prior occurrence:

| Trigger Type | Effect Type | B01+B02 Count | Added in B03 | B03 Trait IDs | New Combined Count |
|---|---|:---:|:---:|---|:---:|
| `on_ally_swap` | `mastery_mod` | **2** | **+1** | `trait_light_guiding_beam` | **3** |
| `on_hp_above` | `cd_mod_percent` | **2** | **+1** | `trait_thunder_supercharged_stride` | **3** |
| `on_move_used` | `heal_percent_max_hp` | **2** | **+1** | `trait_water_revitalizing_spring` | **3** |

---

## Open Issues

1. ~~**Healing on Discrete Event Triggers**~~ — closed 2026-11-07 by Q-C18: schema forbids `on_hp_below` + `heal_percent_max_hp`; `trait_light_saving_grace` now uses one-shot `on_hp_cross_below`. Original note: `heal_percent_max_hp` in this batch is strictly bound to discrete event triggers (`on_summon`, `on_hit_taken`, `on_hp_below`). For `on_hp_below` (e.g., `trait_light_saving_grace`), combat state logic must ensure single-shot execution per health gate crossing or apply an internal cooldown (ICD) so that rapid damage/healing cycles around the 25% threshold do not re-trigger healing loops.
2. **Hitbox Scaling for High Range Modifiers**: Several traits provide +15% to +20% `range_mod_percent`. Combat system integration will need to test visual clipping and projectile collision shape boundaries when melee swings or close-range bursts are scaled by these factors.
3. **Compound Buff/Drawback Resolution on Buff Expiration**: Traits like `trait_metal_scrap_overdrive` or `trait_water_torrential_deluge` combine a positive modifier with a speed reduction. If an external cleanse or purge mechanic removes positive buffs, game rules must clarify whether the drawback expires simultaneously or tracks its own independent duration.

## 修訂 2026-11-07（P5-08 A1/A14，使用者裁決）
執行期完全同效的特性改綁各自主題；分佈與覆蓋統計已於 2026-11-08 從 `traits.json` 重新產生：
- `trait_thunder_supercharged_stride`：移速 +15% → 雷招 CD −10%（原與 `trait_fire_scorching_momentum` 完全相同）。
- `trait_water_revitalizing_spring`：上場治療 8% → 使用水招時治療 4%（原與 `trait_wood_photosynthesis` 同效；先改的「受水擊治療 6%」又與 `trait_wood_sap_absorption` 相同，故再改）。
- `trait_light_guiding_beam`：換手移速 +20% → 換手光專精 +15（原與 `trait_wind_tailwind_glide` 僅差 1 s）。
- `trait_thunder_arc_discharge` 更名 `trait_thunder_crisis_arc`（與招式 `thunder_arc_discharge` 撞詞幹，TERRA 命名）。
