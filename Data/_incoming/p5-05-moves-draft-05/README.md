# Moves Draft Batch 05 (P5-05)

## Overview
This batch contains the fifth draft of 30 moves for COSMIMERA (`moves/moves.json`), delivering the foundational **status, support, and secondary-effect** move set under roadmap item **P5-05**. This complements the 150 foundational pure-damage moves drafted in Batches 01–04, establishing tactical depth for party synergy, team buffs, enemy disruption, and hybrid damage-plus-status playstyles.

- **Scope**: Exactly 30 moves across all 10 elements (`metal`, `wood`, `water`, `fire`, `earth`, `ice`, `wind`, `thunder`, `light`, `shadow`), exactly 3 moves per element.
- **Move Kind**:
  - `effect`: Exactly 20 moves (2 per element) with `power: 0`.
  - `both`: Exactly 10 moves (1 per element) with `power: 20–70` (damage-plus-debuff moves; the debuff applies only on a successful hit, handling pending P2-09).
- **Targeting & Shape Architecture**:
  - `self` (12 moves): Caster buffs or personal sustain heals. Strict constraints: `range_class: "melee"`, `base_range_mm: 0`, `accuracy: 100`, positive modifiers (`+5..+30`) or faster cooldowns (`cd_mod_percent -5..-25`).
  - `ally` (6 moves): Targeted teammate buffs or burst healing. Strict constraints: `range_class: "mid"`, `base_range_mm: 12000`, `accuracy: 100`, positive modifiers (`+5..+30`) or faster cooldowns (`cd_mod_percent -5..-25`).
  - Enemy-targeting (12 moves: 10 `both` + 2 `effect`): Negative modifiers (`-5..-30`), slower cooldowns (`cd_mod_percent +5..+25`), `accuracy: 80–100%`. Geometric shapes strictly use standard combat range bands: `melee` (2000 mm), `mid` (12000 mm), and `long` (24000 mm).
- **Closed Effect Vocabulary**:
  - All 7 effect types defined in `Data/schemas/common.schema.json` are utilized.
  - Every effect type is used at least 3 times across the batch (actual range: 4 to 5 times each).
  - `heal_percent_max_hp` is capped at 4 moves (below the hard ceiling of 5).
  - `effect_duration_ms` is strictly included for all non-instant effects (6000–12000 ms, within the 3000–15000 ms band) and strictly omitted for instant heals (`heal_percent_max_hp`).
- **ID Hygiene**: All 30 IDs are strictly lowercase `snake_case`, match `^[a-z][a-z0-9]*(_[a-z0-9]+)*$`, prefixed with their element, and are 100% unique against Batches 01–04 and `Data/retired-ids.txt`.

---

## Number Bands & Balance Guidelines

Values are strictly integers obeying monotonic trade-offs:
- **Power**:
  - `effect` moves: strictly `0`.
  - `both` moves: `40–65` (within the `20–70` band).
- **Cooldown**:
  - `effect` moves: `6,500–15,000 ms` (within the `4,000–15,000 ms` band).
  - `both` moves: `2,400–4,200 ms` (within the `2,000–8,000 ms` band).
- **Accuracy**:
  - `self` and `ally` support moves: strictly `100%`.
  - Enemy-targeting moves: `80–100%` (Melee: 95–100%, Mid: 90%, Long: 80–85%).
- **Duration (`effect_duration_ms`)**:
  - Non-heal buffs & debuffs: `6,000–12,000 ms` (band: 3,000–15,000 ms).
  - `heal_percent_max_hp`: Omitted (instantaneous recovery).
- **Effect Magnitudes**:
  - Stat modifiers (`mastery_mod`, `resist_mod`, `evasion_mod_percent`, `move_speed_mod_percent`, `range_mod_percent`): Buffs `+20%` to `+30%`; Debuffs `-20%` to `-25%`.
  - Cooldown modifiers (`cd_mod_percent`): Buffs `-15%` to `-25%` (hasten); Debuffs `+20%` (delay).
  - Max HP Heals (`heal_percent_max_hp`): `20%` to `35%`.

### Monotonic Scaling Observations
1. **Heal Scaling**: Monotonic cooldown progression with heal potency:
   - 20% max HP (`earth_mineral_communion`): 9,500 ms CD
   - 25% max HP (`wood_sunlight_mend`): 11,000 ms CD
   - 30% max HP (`water_healing_spring`): 13,000 ms CD
   - 35% max HP (`light_radiant_benediction`): 15,000 ms CD
2. **Buff Duration & Magnitude**:
   - `earth_granite_stance` (+30% Earth resist for 12,000 ms): 10,500 ms CD vs. `metal_ferro_temper` (+25% Metal resist for 10,000 ms): 8,500 ms CD.
3. **Hybrid "both" Moves**:
   - Follow Batches 01–04 monotonic damage curves with added cooldown tax for secondary debuff application:
   - Low-tier combo strikes: `ice_brittle_strike` (40 P / 2400 ms / 100% / Melee), `metal_magnet_strike` (45 P / 2600 ms / 95% / Melee), `light_dazzling_crescent` (45 P / 2600 ms / 95% / Melee).
   - Mid-tier tactical strikes: `wood_thorn_snare` (50 P / 3000 ms / 90% / Mid), `earth_tremor_stomp` (50 P / 3200 ms / 95% / Melee), `water_drenching_surge` (55 P / 3400 ms / 90% / Mid), `shadow_umbra_curse` (55 P / 3600 ms / 80% / Long), `fire_cinder_slash` (60 P / 3800 ms / 95% / Melee).
   - High-tier control strike: `thunder_draining_bolt` (65 P / 4200 ms / 85% / Long).

---

## Element to Moves Mapping

| Element | Move 1 (`effect`) | Move 2 (`effect`) | Move 3 (`both`) | Combat Niche & Roles |
|---|---|---|---|---|
| **metal** | `metal_ferro_temper`<br>(0 P / 8500ms / 100% / self / melee)<br>`resist_mod` metal +25 (10s) | `metal_alloy_polish`<br>(0 P / 7000ms / 100% / self / melee)<br>`mastery_mod` metal +20 (8s) | `metal_magnet_strike`<br>(45 P / 2600ms / 95% / front / melee)<br>`move_speed_mod_percent` -20 (6s) | Defensive hardening, offensive hone, close-range magnetic snare |
| **wood** | `wood_sunlight_mend`<br>(0 P / 11000ms / 100% / self / melee)<br>`heal_percent_max_hp` 25% | `wood_verdant_infusion`<br>(0 P / 9000ms / 100% / ally / mid)<br>`cd_mod_percent` any -15 (8s) | `wood_thorn_snare`<br>(50 P / 3000ms / 90% / line / mid)<br>`evasion_mod_percent` -20 (6s) | Personal sustain, ally cooldown acceleration, evasion stripping |
| **water** | `water_healing_spring`<br>(0 P / 13000ms / 100% / ally / mid)<br>`heal_percent_max_hp` 30% | `water_vapor_veil`<br>(0 P / 7500ms / 100% / self / melee)<br>`evasion_mod_percent` +25 (8s) | `water_drenching_surge`<br>(55 P / 3400ms / 90% / cone / mid)<br>`resist_mod` water -20 (7s) | Teammate triage, evasive mist shroud, elemental drench vulnerability |
| **fire** | `fire_kindle_ward`<br>(0 P / 8000ms / 100% / self / melee)<br>`mastery_mod` fire +25 (9s) | `fire_thermal_sprint`<br>(0 P / 6500ms / 100% / self / melee)<br>`move_speed_mod_percent` +25 (7s) | `fire_cinder_slash`<br>(60 P / 3800ms / 95% / sweep / melee)<br>`range_mod_percent` -20 (6s) | Fire amplification, thermal mobility rush, ash sweep that reduces enemy attack range by 20% for 6 s |
| **earth** | `earth_mineral_communion`<br>(0 P / 9500ms / 100% / self / melee)<br>`heal_percent_max_hp` 20% | `earth_granite_stance`<br>(0 P / 10500ms / 100% / self / melee)<br>`resist_mod` earth +30 (12s) | `earth_tremor_stomp`<br>(50 P / 3200ms / 95% / burst / melee)<br>`cd_mod_percent` any +20 (7s) | Mineral self-repair, heavy armor hardening, seismic cadence disruption |
| **ice** | `ice_glacial_mirror`<br>(0 P / 6500ms / 100% / self / melee)<br>`evasion_mod_percent` +20 (8s) | `ice_numbing_haze`<br>(0 P / 9000ms / 80% / cone / long)<br>`move_speed_mod_percent` -25 (8s) | `ice_brittle_strike`<br>(40 P / 2400ms / 100% / front / melee)<br>`resist_mod` ice -25 (6s) | Frost refraction dodging, long-range blizzard slow, brittle freeze vulnerability |
| **wind** | `wind_tailwind_boon`<br>(0 P / 7000ms / 100% / self / melee)<br>`range_mod_percent` +25 (8s) | `wind_haste_current`<br>(0 P / 8500ms / 100% / ally / mid)<br>`move_speed_mod_percent` +30 (8s) | `wind_turbulent_arc`<br>(45 P / 2800ms / 90% / arc / mid)<br>`evasion_mod_percent` -20 (6s) | Ballistic range expansion, targeted gale haste, destabilizing crosswind |
| **thunder** | `thunder_overclock_charge`<br>(0 P / 9000ms / 100% / self / melee)<br>`cd_mod_percent` thunder -25 (8s) | `thunder_amplifying_spark`<br>(0 P / 8500ms / 100% / ally / mid)<br>`range_mod_percent` +25 (8s) | `thunder_draining_bolt`<br>(65 P / 4200ms / 85% / line / long)<br>`cd_mod_percent` any +20 (6s) | Internal cycle acceleration, ally electric conductance range, neural cooldown drain |
| **light** | `light_radiant_benediction`<br>(0 P / 15000ms / 100% / ally / mid)<br>`heal_percent_max_hp` 35% | `light_aureole_halo`<br>(0 P / 8000ms / 100% / self / melee)<br>`mastery_mod` light +25 (9s) | `light_dazzling_crescent`<br>(45 P / 2600ms / 95% / sweep / melee)<br>`resist_mod` light -20 (6s) | High-potency single-target blessing, divine solar halo, blinding crescent sweep |
| **shadow** | `shadow_veiling_mist`<br>(0 P / 7500ms / 100% / ally / mid)<br>`move_speed_mod_percent` +25 (8s) | `shadow_enervating_mire`<br>(0 P / 9500ms / 90% / burst / mid)<br>`mastery_mod` shadow -25 (8s) | `shadow_umbra_curse`<br>(55 P / 3600ms / 80% / arc / long)<br>`range_mod_percent` -25 (7s) | Cloaked ally speed buff, ranged mire enervation, long-range perception atrophy |

---

## Distribution Counts (Hand-Verified)

### Per Element
- Exactly 3 moves per element across all 10 elements:
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
- **Total**: Exactly 30 moves.

### Per Kind
- `effect` (Power 0): Exactly 20 moves (2 per element).
- `both` (Power 20–70): Exactly 10 moves (1 per element).
- **Total**: Exactly 30 moves.

### Per Shape & Targeting Category
- `self` (Caster buff / self sustain): Exactly 12 moves (`metal_ferro_temper`, `metal_alloy_polish`, `wood_sunlight_mend`, `water_vapor_veil`, `fire_kindle_ward`, `fire_thermal_sprint`, `earth_mineral_communion`, `earth_granite_stance`, `ice_glacial_mirror`, `wind_tailwind_boon`, `thunder_overclock_charge`, `light_aureole_halo`).
- `ally` (Targeted teammate buff / heal): Exactly 6 moves (`wood_verdant_infusion`, `water_healing_spring`, `wind_haste_current`, `thunder_amplifying_spark`, `light_radiant_benediction`, `shadow_veiling_mist`).
- Enemy-targeting (Offensive debuff / damage + debuff): Exactly 12 moves:
  - `front`: 2 moves (`metal_magnet_strike`, `ice_brittle_strike`)
  - `sweep`: 2 moves (`fire_cinder_slash`, `light_dazzling_crescent`)
  - `burst`: 2 moves (`earth_tremor_stomp` [origin: caster], `shadow_enervating_mire` [origin: target])
  - `line`: 2 moves (`wood_thorn_snare`, `thunder_draining_bolt`)
  - `cone`: 2 moves (`water_drenching_surge`, `ice_numbing_haze`)
  - `arc`: 2 moves (`wind_turbulent_arc`, `shadow_umbra_curse`)
- **Total**: Exactly 30 moves.

### Per Range Class
- `melee`: Exactly 17 moves (12 `self` at 0 mm + 5 enemy melee moves at 2,000 mm).
- `mid`: Exactly 9 moves (6 `ally` at 12,000 mm + 3 enemy mid moves at 12,000 mm).
- `long`: Exactly 4 moves (4 enemy long moves at 24,000 mm).
- **Total**: Exactly 30 moves.

### Per Effect Type Breakdown
| Effect Type | Buffs (`self` / `ally`) | Debuffs (Enemy) | Total Count | Specific Moves |
|---|---|---|---|---|
| `heal_percent_max_hp` | 4 (2 self, 2 ally) | 0 | **4** | `wood_sunlight_mend`, `water_healing_spring`, `earth_mineral_communion`, `light_radiant_benediction` |
| `mastery_mod` | 3 (3 self, 0 ally) | 1 | **4** | `metal_alloy_polish` (+20), `fire_kindle_ward` (+25), `light_aureole_halo` (+25), `shadow_enervating_mire` (-25) |
| `resist_mod` | 2 (2 self, 0 ally) | 3 | **5** | `metal_ferro_temper` (+25), `earth_granite_stance` (+30), `water_drenching_surge` (-20), `ice_brittle_strike` (-25), `light_dazzling_crescent` (-20) |
| `cd_mod_percent` | 2 (1 self, 1 ally) | 2 | **4** | `wood_verdant_infusion` (-15), `thunder_overclock_charge` (-25), `earth_tremor_stomp` (+20), `thunder_draining_bolt` (+20) |
| `evasion_mod_percent` | 2 (2 self, 0 ally) | 2 | **4** | `water_vapor_veil` (+25), `ice_glacial_mirror` (+20), `wood_thorn_snare` (-20), `wind_turbulent_arc` (-20) |
| `move_speed_mod_percent` | 3 (1 self, 2 ally) | 2 | **5** | `fire_thermal_sprint` (+25), `wind_haste_current` (+30), `shadow_veiling_mist` (+25), `metal_magnet_strike` (-20), `ice_numbing_haze` (-25) |
| `range_mod_percent` | 2 (1 self, 1 ally) | 2 | **4** | `wind_tailwind_boon` (+25), `thunder_amplifying_spark` (+25), `fire_cinder_slash` (-20), `shadow_umbra_curse` (-25) |
| **Total** | **18** | **12** | **30** | *Every effect type >= 3, heal <= 5* |

---

### Batch 05 Shape × Range Class Cross-Tabulation

| Shape | Melee (0 mm / 2000 mm) | Mid (12000 mm) | Long (24000 mm) | Total |
|---|---|---|---|---|
| **self** | 12 (`metal_ferro_temper`, `metal_alloy_polish`, `wood_sunlight_mend`, `water_vapor_veil`, `fire_kindle_ward`, `fire_thermal_sprint`, `earth_mineral_communion`, `earth_granite_stance`, `ice_glacial_mirror`, `wind_tailwind_boon`, `thunder_overclock_charge`, `light_aureole_halo`) | 0 | 0 | **12** |
| **ally** | 0 | 6 (`wood_verdant_infusion`, `water_healing_spring`, `wind_haste_current`, `thunder_amplifying_spark`, `light_radiant_benediction`, `shadow_veiling_mist`) | 0 | **6** |
| **front** | 2 (`metal_magnet_strike`, `ice_brittle_strike`) | 0 | 0 | **2** |
| **sweep** | 2 (`fire_cinder_slash`, `light_dazzling_crescent`) | 0 | 0 | **2** |
| **burst** | 1 (`earth_tremor_stomp` [caster]) | 1 (`shadow_enervating_mire` [target]) | 0 | **2** |
| **line** | 0 | 1 (`wood_thorn_snare`) | 1 (`thunder_draining_bolt`) | **2** |
| **cone** | 0 | 1 (`water_drenching_surge`) | 1 (`ice_numbing_haze`) | **2** |
| **arc** | 0 | 1 (`wind_turbulent_arc`) | 1 (`shadow_umbra_curse`) | **2** |
| **Total** | **17** | **9** | **4** | **30** |

---

## Effect Design & Mechanics Analysis

1. **Party Coordination & Rotation Synergy**:
   - In 2-creature combat teams, player chimeras can alternate between an active carry and a tactical buffer. For example, a Wood Chimera casting `wood_verdant_infusion` (-15% CD) on a high-cooldown Thunder attacker drastically compresses DPS rotation windows.
   - Elemental vulnerability chains become viable: an Ice chimera opens with `ice_brittle_strike` (-25% Ice resist) before swapping or signaling the partner chimera to fire `ice_blizzard_vortex` or `ice_avalanche_charge` into the weakened target.
2. **Speed & Range Control**:
   - Movement speed modifiers (`move_speed_mod_percent`) enable kiting vs. swarming tactics. `wind_haste_current` (+30% move speed) gives allies the agility to disengage from dangerous AoE telegraphs, while `metal_magnet_strike` (-20% move speed for 6 s) slows agile opponents, helping keep them within melee range.
   - Ballistic scaling (`range_mod_percent`) synergizes with STR scaling from `combat.md`: casting `thunder_amplifying_spark` (+25% range) allows long-range railgun and cannon abilities to command larger battlefield zones.
3. **Sustain vs. Tempo Trade-offs**:
   - Healing moves deliberately carry the longest cooldown windows in the game (9.5s to 15.0s), preventing spam sustain and keeping high-stakes evasion as the primary defensive mechanic.

---

## Open Questions

1. **Stacking & Re-application Semantics (P2-09)**:
   - When a status effect is re-applied before its `effect_duration_ms` elapses (e.g., repeatedly hitting with `metal_magnet_strike` within 6 seconds), does the duration refresh, stack additively, or apply diminishing returns?
2. **Multiple Simultaneous Attribute Modifiers**:
   - If an ally receives `move_speed_mod_percent` from both `wind_haste_current` (+30%) and `shadow_veiling_mist` (+25%), should movement speed bonuses stack additively (+55%), multiplicatively, or take only the highest modifier?
3. **Healing Power vs. Heal Modifiers**:
   - `heal_percent_max_hp` currently computes directly against target max HP. Should spirit (SPI) or elemental mastery scale the effective heal amount, or is it strictly locked to the fixed percentage?
4. **Telegraph Windows for Status Bursts**:
   - Ranged debuffs like `shadow_enervating_mire` (Mid Burst) will require distinct visual telegraph indicators (e.g. purple miasma ring on ground) so players and AI opponents have counterplay opportunities to roll or dash out of the radius before detonation.

## 修訂 2026-11-07（P5-08 A1，使用者裁決）
- `wood_photosynthesis` 更名 `wood_sunlight_mend`（與特性 `trait_wood_photosynthesis` 撞詞幹，TERRA 命名）；數值不變。
