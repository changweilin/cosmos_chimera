# Moves Draft Batch 06 (P5-05)

## Overview
This batch contains the sixth draft of 20 moves for COSMIMERA (`moves/moves.json`), delivering foundational **persistent field (area-denial / hazard zoning)** and **stacking buff / debuff** move archetypes under roadmap item **P5-05**. This expands combat depth beyond instant strikes and static duration buffs, introducing positional area control and cumulative tactical momentum.

- **Scope**: Exactly 20 moves across all 10 elements (`metal`, `wood`, `water`, `fire`, `earth`, `ice`, `wind`, `thunder`, `light`, `shadow`), exactly 2 moves per element (1 Field move + 1 Stacking move).
- **Sub-Batch A: 10 FIELD Moves (1 per element)**:
  - Shape: Strictly `"field"`.
  - Origin: Omitted (no `origin` key, conforming to `move.schema.json`).
  - Placement Range: Exactly 5 `"mid"` (`base_range_mm: 12000`) and 5 `"long"` (`base_range_mm: 24000`).
  - Field Duration: `field_duration_ms: 3000–8000`.
  - Field Cadence: In accordance with `docs/design/combat.md#場地` (Q-C17), a field executes an initial resolution at $t = 0$ on placement, and then ticks every `tick_ms: 1000` (total $\lceil \text{duration} / 1000 \rceil$ resolutions). Each tick executes a full geometric check and hit roll ($80\text{--}95\%$ accuracy). To prevent degenerate tick damage or chain-stun loops, per-tick numbers are strictly dampened.
  - Move Kind Breakdown:
    - `damage` (4 moves): Power 8–20 per tick, no secondary effect (`metal_caltrop_zone`, `fire_ember_carpet`, `ice_tundra_expanse`, `thunder_conductive_grid`).
    - `both` (4 moves): Power 5–15 per tick plus an enemy debuff (delta -5..-15 or cd_mod_percent +5..+15), `effect_duration_ms: 2000–4000` (`wood_tangle_grove`, `water_vortex_basin`, `earth_quicksand_sink`, `shadow_stygian_pit`).
    - `effect` (2 moves): Power 0, enemy debuff only (delta -10..-20), `effect_duration_ms: 2000–4000` (`wind_monsoon_domain`, `light_prismatic_exposure`). Fields strictly hit opposing units; ally buff zones are disallowed.
  - Cooldowns: Long cooldown windows ($9000\text{--}16000\text{ ms}$) with strict duration-monotonic scaling within each kind.
- **Sub-Batch B: 10 STACKING Moves (1 per element)**:
  - All 10 moves contain an `effect` object with explicit `max_stacks` between 2 and 5.
  - Total potential magnitude bounded: strictly $|\text{delta}| \times \text{max\_stacks} \le 40$ to prevent stat runaway under P2-09 `EffectMath`.
  - `effect_duration_ms: 4000–10000`.
  - Mix Breakdown:
    - 5 kind `both`: Power 25–50, enemy debuff, cooldown $1800\text{--}3500\text{ ms}$, accuracy $85\text{--}100\%$, utilizing distinct projectile/melee shapes (`front`, `sweep`, `cone`, `line`, `arc`).
    - 5 kind `effect`: Power 0, shape `"self"`, `range_class: "melee"`, `base_range_mm: 0`, accuracy $100\%$, cooldown $3000\text{--}6000\text{ ms}$, delivering cumulative positive buffs (or negative `cd_mod_percent` haste).
- **Closed Effect Vocabulary**:
  - Excludes `heal_percent_max_hp` entirely (0 heal moves in this batch; heals cannot stack under `common.schema.json`).
  - Utilizes all 6 non-heal effect types (`mastery_mod`, `resist_mod`, `cd_mod_percent`, `evasion_mod_percent`, `move_speed_mod_percent`, `range_mod_percent`).
  - Each non-heal effect type is used at least 2 times across the 16 effect-bearing moves.
- **ID Hygiene**: All 20 IDs are strictly lowercase `snake_case`, match `^[a-z][a-z0-9]*(_[a-z0-9]+)*$`, prefixed with their element, and are 100% unique against Batches 01–05, traits drafts, and `Data/retired-ids.txt`.

---

## Number Bands & Balance Guidelines

### 1. Field Moves (`shape: "field"`)
- **Per-Tick Power**:
  - `damage` fields: 12 to 18 (e.g. 4 ticks $\times 12 = 48$ total potential raw damage over 4 s; 7 ticks $\times 18 = 126$ total potential raw damage over 7 s).
  - `both` fields: 8 to 14 (low tick damage coupled with short lingering debuffs).
  - `effect` fields: strictly 0.
- **Field Duration**: 4000 to 7000 ms (generating 4 to 7 resolution ticks).
- **Cooldowns & Monotonic Scaling Within Kind**:
  - Longer field duration commands longer recovery windows to enforce clear downtime and tactical windows for counter-pushing:
    - Kind `damage`:
      - `metal_caltrop_zone`: 4000 ms duration $\rightarrow$ 10,000 ms CD (Power 12)
      - `fire_ember_carpet`: 5000 ms duration $\rightarrow$ 11,500 ms CD (Power 14)
      - `ice_tundra_expanse`: 6000 ms duration $\rightarrow$ 13,000 ms CD (Power 16)
      - `thunder_conductive_grid`: 7000 ms duration $\rightarrow$ 15,000 ms CD (Power 18)
    - Kind `both`:
      - `wood_tangle_grove`: 4000 ms duration $\rightarrow$ 10,500 ms CD (Power 8)
      - `water_vortex_basin`: 5000 ms duration $\rightarrow$ 12,000 ms CD (Power 10)
      - `earth_quicksand_sink`: 6000 ms duration $\rightarrow$ 13,500 ms CD (Power 12)
      - `shadow_stygian_pit`: 7000 ms duration $\rightarrow$ 15,500 ms CD (Power 14)
    - Kind `effect`:
      - `wind_monsoon_domain`: 5000 ms duration $\rightarrow$ 11,000 ms CD (Power 0)
      - `light_prismatic_exposure`: 6000 ms duration $\rightarrow$ 13,000 ms CD (Power 0)

### 2. Stacking Moves (`max_stacks: 2..5`)
- **Stacking Cap Bound**: $|\text{delta}| \times \text{max\_stacks} \le 40$.
  - `metal_corrosion_edge`: resist delta $-8 \times 4$ stacks = $-32$ points max.
  - `fire_slag_rake`: mastery delta $-7 \times 5$ stacks = $-35$ points max.
  - `ice_frigid_breath`: speed delta $-8 \times 4$ stacks = $-32\%$ max.
  - `thunder_voltage_tracer`: cooldown delta $+8 \times 4$ stacks = $+32\%$ max.
  - `shadow_pitch_lob`: evasion delta $-10 \times 3$ stacks = $-30\%$ max.
  - `wood_vernal_growth`: mastery delta $+8 \times 4$ stacks = $+32$ points max.
  - `water_flow_state`: evasion delta $+7 \times 5$ stacks = $+35\%$ max.
  - `earth_lithic_plating`: resist delta $+10 \times 3$ stacks = $+30$ points max.
  - `wind_ballistic_focus`: range delta $+8 \times 4$ stacks = $+32\%$ max.
  - `light_harmonic_cadence`: cooldown delta $-8 \times 4$ stacks = $-32\%$ max.
- **Rhythm & Refresh Window**:
  - Kind `both` moves carry low-to-medium cooldowns ($2200\text{--}3200\text{ ms}$) against durations of $5000\text{--}6000\text{ ms}$. This allows an aggressive attacker to build and maintain stacks only if they land consecutive hits with minimal rotation delay.
  - Kind `effect` self-buff moves have cooldowns ($3500\text{--}4500\text{ ms}$) within durations of $7000\text{--}8000\text{ ms}$, requiring deliberate active uptime maintenance.

---

## Element to Moves Mapping

| Element | Move 1: Persistent Field (`shape: "field"`) | Move 2: Stacking Move (`max_stacks: 2..5`) | Tactical Roles & Combat Niches |
|---|---|---|---|
| **metal** | `metal_caltrop_zone`<br>(12 P / 10000ms / 90% / field / mid)<br>duration: 4000ms (4 ticks), pure damage | `metal_corrosion_edge`<br>(35 P / 2200ms / 95% / front / melee)<br>`resist_mod` metal -8 (max 4, 6s) | Mid-range metallic shrapnel hazard; close-range armor-stripping flurry |
| **wood** | `wood_tangle_grove`<br>(8 P / 10500ms / 90% / field / mid)<br>duration: 4000ms (4 ticks), `evasion_mod_percent` -10 (2.5s) | `wood_vernal_growth`<br>(0 P / 4000ms / 100% / self / melee)<br>`mastery_mod` wood +8 (max 4, 8s) | Entangling root field lowering evasion (no root/immobilize status); cumulative vitality surging spellpower |
| **water** | `water_vortex_basin`<br>(10 P / 12000ms / 85% / field / long)<br>duration: 5000ms (5 ticks), `move_speed_mod_percent` -15 (3s) | `water_flow_state`<br>(0 P / 3500ms / 100% / self / melee)<br>`evasion_mod_percent` +7 (max 5, 7s) | Long-range whirlpool slowing enemy movement; fluid evasive posture dodging incoming fire |
| **fire** | `fire_ember_carpet`<br>(14 P / 11500ms / 90% / field / mid)<br>duration: 5000ms (5 ticks), pure damage | `fire_slag_rake`<br>(40 P / 2500ms / 95% / sweep / melee)<br>`mastery_mod` fire -7 (max 5, 6s) | Persistent ground blaze zone; wide sweeping rake dampening enemy fire offense |
| **earth** | `earth_quicksand_sink`<br>(12 P / 13500ms / 90% / field / mid)<br>duration: 6000ms (6 ticks), `cd_mod_percent` any +10 (3s) | `earth_lithic_plating`<br>(0 P / 4500ms / 100% / self / melee)<br>`resist_mod` earth +10 (max 3, 8s) | Quicksand trap disrupting opponent attack cadence; hardening rocky shell against earth attacks |
| **ice** | `ice_tundra_expanse`<br>(16 P / 13000ms / 85% / field / long)<br>duration: 6000ms (6 ticks), pure damage | `ice_frigid_breath`<br>(30 P / 2400ms / 90% / cone / mid)<br>`move_speed_mod_percent` -8 (max 4, 5s) | Long-range arctic freeze territory; cone-shaped frost breath chaining progressive chills |
| **wind** | `wind_monsoon_domain`<br>(0 P / 11000ms / 85% / field / long)<br>duration: 5000ms (5 ticks), `range_mod_percent` -15 (3s) | `wind_ballistic_focus`<br>(0 P / 3800ms / 100% / self / melee)<br>`range_mod_percent` +8 (max 4, 8s) | Atmospheric gale suppressing enemy firing distance; sniper stance stacking extreme projectile reach |
| **thunder** | `thunder_conductive_grid`<br>(18 P / 15000ms / 80% / field / long)<br>duration: 7000ms (7 ticks), pure damage | `thunder_voltage_tracer`<br>(45 P / 3200ms / 85% / line / long)<br>`cd_mod_percent` any +8 (max 4, 5s) | High-voltage electric grid area denial; long-distance laser tracer frying enemy cooldown recovery |
| **light** | `light_prismatic_exposure`<br>(0 P / 13000ms / 90% / field / mid)<br>duration: 6000ms (6 ticks), `resist_mod` light -15 (3s) | `light_harmonic_cadence`<br>(0 P / 4200ms / 100% / self / melee)<br>`cd_mod_percent` light -8 (max 4, 8s) | Luminous prism field reducing enemy light resistance; harmonic chanting hasting light rotations |
| **shadow** | `shadow_stygian_pit`<br>(14 P / 15500ms / 85% / field / long)<br>duration: 7000ms (7 ticks), `mastery_mod` shadow -10 (3.5s) | `shadow_pitch_lob`<br>(35 P / 2800ms / 90% / arc / mid)<br>`evasion_mod_percent` -10 (max 3, 6s) | Void pit draining shadowy potency; lobbed dark pitch adhering and stripping target agility |

---

## Distribution Counts (Hand-Verified)

### Per Element
- Exactly 2 moves per element across all 10 elements:
  - `metal`: 2
  - `wood`: 2
  - `water`: 2
  - `fire`: 2
  - `earth`: 2
  - `ice`: 2
  - `wind`: 2
  - `thunder`: 2
  - `light`: 2
  - `shadow`: 2
- **Total**: Exactly 20 moves.

### Per Kind
- `damage`: Exactly 4 moves (all Field: `metal_caltrop_zone`, `fire_ember_carpet`, `ice_tundra_expanse`, `thunder_conductive_grid`).
- `both`: Exactly 9 moves (4 Field: `wood_tangle_grove`, `water_vortex_basin`, `earth_quicksand_sink`, `shadow_stygian_pit`; 5 Stacking: `metal_corrosion_edge`, `fire_slag_rake`, `ice_frigid_breath`, `thunder_voltage_tracer`, `shadow_pitch_lob`).
- `effect`: Exactly 7 moves (2 Field: `wind_monsoon_domain`, `light_prismatic_exposure`; 5 Stacking: `wood_vernal_growth`, `water_flow_state`, `earth_lithic_plating`, `wind_ballistic_focus`, `light_harmonic_cadence`).
- **Total**: Exactly 20 moves.

### Per Shape & Targeting Category
- `field` (Persistent ground area): Exactly 10 moves (1 per element).
- `self` (Self-buff stance / focus): Exactly 5 moves (`wood_vernal_growth`, `water_flow_state`, `earth_lithic_plating`, `wind_ballistic_focus`, `light_harmonic_cadence`).
- Directional offensive shapes (5 moves):
  - `front`: 1 move (`metal_corrosion_edge`)
  - `sweep`: 1 move (`fire_slag_rake`)
  - `cone`: 1 move (`ice_frigid_breath`)
  - `line`: 1 move (`thunder_voltage_tracer`)
  - `arc`: 1 move (`shadow_pitch_lob`)
- **Total**: Exactly 20 moves.

### Per Range Class
- `melee`: Exactly 7 moves (5 `self` at 0 mm + 2 directional melee strikes at 2000 mm: `metal_corrosion_edge`, `fire_slag_rake`).
- `mid`: Exactly 7 moves (5 fields at 12000 mm + 2 mid-range attacks at 12000 mm: `ice_frigid_breath`, `shadow_pitch_lob`).
- `long`: Exactly 6 moves (5 fields at 24000 mm + 1 long-range projectile at 24000 mm: `thunder_voltage_tracer`).
- **Total**: Exactly 20 moves.

### Per Effect Type Breakdown (16 Effect-Bearing Moves)
| Effect Type | Field Debuffs | Stacking Debuffs | Stacking Self Buffs | Total Count | Moves |
|---|---|---|---|---|---|
| `mastery_mod` | 1 | 1 | 1 | **3** | `shadow_stygian_pit` (-10), `fire_slag_rake` (-7 $\times 5$), `wood_vernal_growth` (+8 $\times 4$) |
| `resist_mod` | 1 | 1 | 1 | **3** | `light_prismatic_exposure` (-15), `metal_corrosion_edge` (-8 $\times 4$), `earth_lithic_plating` (+10 $\times 3$) |
| `cd_mod_percent` | 1 | 1 | 1 | **3** | `earth_quicksand_sink` (+10), `thunder_voltage_tracer` (+8 $\times 4$), `light_harmonic_cadence` (-8 $\times 4$) |
| `evasion_mod_percent` | 1 | 1 | 1 | **3** | `wood_tangle_grove` (-10), `shadow_pitch_lob` (-10 $\times 3$), `water_flow_state` (+7 $\times 5$) |
| `move_speed_mod_percent` | 1 | 1 | 0 | **2** | `water_vortex_basin` (-15), `ice_frigid_breath` (-8 $\times 4$) |
| `range_mod_percent` | 1 | 0 | 1 | **2** | `wind_monsoon_domain` (-15), `wind_ballistic_focus` (+8 $\times 4$) |
| `heal_percent_max_hp` | 0 | 0 | 0 | **0** | *Excluded by specification* |
| **Total** | **6** | **5** | **5** | **16** | *Every non-heal effect type $\ge 2$* |

---

### Shape × Range Class Cross-Tabulation

| Shape | Melee (0 mm / 2000 mm) | Mid (12000 mm) | Long (24000 mm) | Total |
|---|---|---|---|---|
| **field** | 0 | 5 (`metal_caltrop_zone`, `wood_tangle_grove`, `fire_ember_carpet`, `earth_quicksand_sink`, `light_prismatic_exposure`) | 5 (`water_vortex_basin`, `ice_tundra_expanse`, `wind_monsoon_domain`, `thunder_conductive_grid`, `shadow_stygian_pit`) | **10** |
| **self** | 5 (`wood_vernal_growth`, `water_flow_state`, `earth_lithic_plating`, `wind_ballistic_focus`, `light_harmonic_cadence`) | 0 | 0 | **5** |
| **front** | 1 (`metal_corrosion_edge`) | 0 | 0 | **1** |
| **sweep** | 1 (`fire_slag_rake`) | 0 | 0 | **1** |
| **cone** | 0 | 1 (`ice_frigid_breath`) | 0 | **1** |
| **line** | 0 | 0 | 1 (`thunder_voltage_tracer`) | **1** |
| **arc** | 0 | 1 (`shadow_pitch_lob`) | 0 | **1** |
| **Total** | **7** | **7** | **6** | **20** |

---

## Effect Design & Mechanics Analysis

1. **Area Denial & Space Control**:
   - Field moves introduce an objective-based zoning meta. A player chimera deploying `metal_caltrop_zone` or `water_vortex_basin` forces enemy chimeras to either vacate the area or suffer continuous periodic attrition and mobility penalties.
   - Because field ticks evaluate independent hit rolls every 1000 ms, high evasion lowers the chance of being hit by each resolution (the hit floor still applies), and a well-timed dodge roll avoids only the resolutions that fall inside its 400 ms invulnerability window.
2. **Cumulative Battle Momentum**:
   - Stacking abilities shift gameplay from burst trades to sustained pressure. An agile caster maintaining `wood_vernal_growth` (+8 Wood mastery per stack up to 4 stacks) earns up to +32 Wood mastery percentage points; the relative damage gain depends on the base mastery (e.g. from 100 → 132 mastery, raw damage goes 2.00× → 2.32×, +16%), rewarding sustained aggressive presence.
   - Conversely, stacking debuffs like `thunder_voltage_tracer` (+8% enemy cooldown delay per stack up to 4 stacks) severely cripple an enemy carry's rotation if left unchecked, making evasion or immediate counter-disengagement critical.
3. **P2-09 Integration Readiness**:
   - Every stacking effect is strictly capped ($|\text{delta}| \times \text{max\_stacks} \le 40$), which limits each single move's effect magnitude; cross-source totals are bounded by the effect multiplier clamp (balance.json#effects), not by this cap.
   - All effects strictly follow the closed parameter schemas from `common.schema.json`.

---

## Open Questions

1. **Field Placement Overlap & Stacking**:
   - If two identical field moves (e.g. two `fire_ember_carpet` fields) are placed overlapping by different allied or enemy chimeras, do targets inside the overlap zone take damage and status ticks from both instances simultaneously, or does a unique (source ID, effect ID) rule suppress duplicate ticks?
2. ~~Field Radius STR Scaling~~ — resolved by `docs/design/combat.md#判定體`: STR scales only the cast distance; the 3000 mm field radius is fixed.
3. ~~Stack Expiration and Falloff~~ — resolved by `docs/design/combat.md#效果疊加`: re-application refreshes the whole stack and the whole stack expires together.
4. ~~Field Tick vs. Dodge Invulnerability~~ — each resolution is an ordinary hit resolution at its own instant: a resolution inside the invulnerability window misses that target; there is no re-check when the window ends.
