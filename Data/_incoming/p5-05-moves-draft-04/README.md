# Moves Draft Batch 04 (P5-05)

> 2026-11-07 P5-08 A19：形狀已遷移為九形狀（`shape` ∈ front/sweep/burst/line/cone，加 `origin`、`movement`）。形狀統計以下方「現行形狀分佈」為準（由 `moves/moves.json` 腳本產生）；其餘段落提到的舊四形狀（line/cone/aoe/dash）是初稿歷史描述。

## Overview
This batch contains the fourth draft of 30 placeholder damage moves for COSMIMERA (`moves/moves.json`), completing roadmap item **P5-05** and bringing the total foundational damage move pool to exactly **150 moves** (15 moves per element across all 10 elements).
- **Scope**: Exactly 30 new moves across all 10 elements (`metal`, `wood`, `water`, `fire`, `earth`, `ice`, `wind`, `thunder`, `light`, `shadow`), exactly 3 moves per element.
- **Move Kind**: `damage` only (no `effect` block in this batch; the effect vocabulary was closed by P2-01).
- **Batch Role & Milestone**:
  - Reaches the 150-move milestone for P5-05.
  - Balances power bands across all elements such that every element has at least 3 moves each in low (`<=45`), mid (`46–85`), and high (`>=86`) power tiers.
  - Fills the final spatial niches across all 10 elements so that no element repeats any `(shape, range_class)` pair more than 2 times (well below the maximum limit of 3).
  - Maintains strict uniform range distribution: exactly 1 `melee`, 1 `mid`, and 1 `long` move per element in this batch, yielding an exact 5/5/5 range distribution per element across the 150-move pool.
- **ID & Content Hygiene**: All 30 IDs are strictly lowercase `snake_case`, validated against `^[a-z][a-z0-9]*(_[a-z0-9]+)*$`, unique against Batches 01–03 and `Data/retired-ids.txt`.
- **Near-Duplicate Rule**: Zero near-duplicates exist. For any move sharing element, shape, and range class with an existing move, the power differential is strictly greater than 10 (minimum observed delta is 15).

## Number Bands & Balance Guidelines
All values are strictly integers obeying monotonic trade-offs (higher power dictates longer cooldown times and lower or equal accuracy):
- **Power**: 20–120 (Batch 04 span: 30–115)
- **Cooldown**: 800–10,000 ms (Batch 04 span: 850–8,800 ms)
- **Accuracy**: 70–100% (Batch 04 span: 75–100%)

### Monotonic Progression in Batch 04
- **Power 30**: Cooldown 850–950 ms, Accuracy 100% (`wind_zephyr_pierce`, `wood_splinter_thrust`, `ice_glaze_slide`, `thunder_pulse_ring`)
- **Power 35**: Cooldown 1,000–1,100 ms, Accuracy 100% (`metal_blade_rush`, `fire_pyre_lunge`, `light_beacon_lunge`, `shadow_gloom_pulse`, `water_froth_swipe`, `earth_flint_thrust`)
- **Power 50**: Cooldown 2,100 ms, Accuracy 95% (`metal_tungsten_pierce`)
- **Power 55**: Cooldown 2,500 ms, Accuracy 95% (`ice_hoarfrost_ray`)
- **Power 60**: Cooldown 2,700–2,800 ms, Accuracy 95% (`wood_tendril_vault`, `earth_tremor_ring`)
- **Power 65**: Cooldown 3,000–3,200 ms, Accuracy 90–95% (`water_undertow_rush`, `fire_furnace_vent`, `light_optic_lance`, `shadow_penumbra_spike`)
- **Power 70**: Cooldown 3,800 ms, Accuracy 90% (`wind_jetstream_glide`)
- **Power 80**: Cooldown 5,000 ms, Accuracy 85% (`thunder_overload_spray`)
- **Power 90**: Cooldown 6,200–6,400 ms, Accuracy 85% (`wood_pine_salvo`, `water_depth_charge`, `earth_scree_spray`, `ice_frostfall_impact`, `wind_hurricane_sweep`, `light_chromatic_fan`)
- **Power 95**: Cooldown 7,000 ms, Accuracy 80% (`fire_pyroclast_sweep`)
- **Power 100**: Cooldown 7,500 ms, Accuracy 80% (`shadow_entropy_wave`)
- **Power 105**: Cooldown 7,800 ms, Accuracy 80% (`thunder_dynamo_charge`)
- **Power 115**: Cooldown 8,800 ms, Accuracy 75% (`metal_siege_bombard`)

## Element to Moves Mapping

| Element | Move 1 (Low: <=45) | Move 2 (Mid: 46–85) | Move 3 (High: >=86) | Niches Covered in Batch 04 |
|---|---|---|---|---|
| **metal** | `metal_blade_rush`<br>(35 P / 1000ms / 100% / melee / line+dash) | `metal_tungsten_pierce`<br>(50 P / 2100ms / 95% / mid / line) | `metal_siege_bombard`<br>(115 P / 8800ms / 75% / long / burst@target) | dash/melee, line/mid, aoe/long |
| **wood** | `wood_splinter_thrust`<br>(30 P / 900ms / 100% / melee / front) | `wood_tendril_vault`<br>(60 P / 2700ms / 95% / mid / line+dash) | `wood_pine_salvo`<br>(90 P / 6200ms / 85% / long / cone) | line/melee, dash/mid, cone/long |
| **water** | `water_froth_swipe`<br>(35 P / 1100ms / 100% / melee / sweep) | `water_undertow_rush`<br>(65 P / 3000ms / 95% / mid / line+dash) | `water_depth_charge`<br>(90 P / 6200ms / 85% / long / burst@target) | cone/melee, dash/mid, aoe/long |
| **fire** | `fire_pyre_lunge`<br>(35 P / 1000ms / 100% / melee / line+dash) | `fire_furnace_vent`<br>(65 P / 3100ms / 95% / mid / burst@target) | `fire_pyroclast_sweep`<br>(95 P / 7000ms / 80% / long / cone) | dash/melee, aoe/mid, cone/long |
| **earth** | `earth_flint_thrust`<br>(35 P / 1100ms / 100% / melee / front) | `earth_tremor_ring`<br>(60 P / 2800ms / 95% / mid / burst@target) | `earth_scree_spray`<br>(90 P / 6400ms / 85% / long / cone) | line/melee, aoe/mid, cone/long |
| **ice** | `ice_glaze_slide`<br>(30 P / 900ms / 100% / melee / line+dash) | `ice_hoarfrost_ray`<br>(55 P / 2500ms / 95% / mid / line) | `ice_frostfall_impact`<br>(90 P / 6400ms / 85% / long / burst@target) | dash/melee, line/mid, aoe/long |
| **wind** | `wind_zephyr_pierce`<br>(30 P / 850ms / 100% / melee / front) | `wind_jetstream_glide`<br>(70 P / 3800ms / 90% / mid / line+dash) | `wind_hurricane_sweep`<br>(90 P / 6400ms / 85% / long / cone) | line/melee, dash/mid, cone/long |
| **thunder** | `thunder_pulse_ring`<br>(30 P / 950ms / 100% / melee / burst@caster) | `thunder_overload_spray`<br>(80 P / 5000ms / 85% / mid / cone) | `thunder_dynamo_charge`<br>(105 P / 7800ms / 80% / long / line+dash) | aoe/melee, cone/mid, dash/long |
| **light** | `light_beacon_lunge`<br>(35 P / 1000ms / 100% / melee / line+dash) | `light_optic_lance`<br>(65 P / 3200ms / 90% / mid / line) | `light_chromatic_fan`<br>(90 P / 6400ms / 85% / long / cone) | dash/melee, line/mid, cone/long |
| **shadow** | `shadow_gloom_pulse`<br>(35 P / 1050ms / 100% / melee / burst@caster) | `shadow_penumbra_spike`<br>(65 P / 3200ms / 90% / mid / line) | `shadow_entropy_wave`<br>(100 P / 7500ms / 80% / long / cone) | aoe/melee, line/mid, cone/long |

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

## 現行形狀分佈（由 moves.json 產生）

| shape(@origin)(+movement) | melee | mid | long | 合計 |
|---|---|---|---|---|
| `burst@caster` | 2 | 0 | 0 | 2 |
| `burst@target` | 0 | 2 | 3 | 5 |
| `cone` | 0 | 1 | 6 | 7 |
| `front` | 3 | 0 | 0 | 3 |
| `line` | 0 | 4 | 0 | 4 |
| `line+dash` | 4 | 3 | 1 | 8 |
| `sweep` | 1 | 0 | 0 | 1 |
| **合計** | 10 | 10 | 10 | **30** |

同屬性＋同形狀＋同射程的招式組（近似重複候選，需靠數值或效果區隔）：無。

### Per Range Class (Batch 04)
- `melee`: 10 moves (exactly 1 per element)
- `mid`: 10 moves (exactly 1 per element)
- `long`: 10 moves (exactly 1 per element)
- **Total**: Exactly 30 moves.

## Before vs. After Batch 04 Analysis

### 1. Power Distribution Comparison: Low / Mid / High
Requirements:
- Low: `power <= 45`
- Mid: `power 46–85`
- High: `power >= 86`
- Goal: Combined count >= 3 for each tier in every element.

| Element | Pre Low | Pre Mid | Pre High | Pre Total | Batch 04 (L / M / H) | Post Low | Post Mid | Post High | Post Total | Balance Goal Met? |
|---|---|---|---|---|---|---|---|---|---|---|
| **metal** | 3 | 6 | 3 | 12 | +1 / +1 / +1 | **4** | **7** | **4** | **15** | Yes (>=3 each) |
| **wood** | 3 | 6 | 3 | 12 | +1 / +1 / +1 | **4** | **7** | **4** | **15** | Yes (>=3 each) |
| **water** | 3 | 6 | 3 | 12 | +1 / +1 / +1 | **4** | **7** | **4** | **15** | Yes (>=3 each) |
| **fire** | 3 | 6 | 3 | 12 | +1 / +1 / +1 | **4** | **7** | **4** | **15** | Yes (>=3 each) |
| **earth** | 3 | 6 | 3 | 12 | +1 / +1 / +1 | **4** | **7** | **4** | **15** | Yes (>=3 each) |
| **ice** | 3 | 6 | 3 | 12 | +1 / +1 / +1 | **4** | **7** | **4** | **15** | Yes (>=3 each) |
| **wind** | 3 | 6 | 3 | 12 | +1 / +1 / +1 | **4** | **7** | **4** | **15** | Yes (>=3 each) |
| **thunder** | 3 | 5 | 4 | 12 | +1 / +1 / +1 | **4** | **6** | **5** | **15** | Yes (>=3 each) |
| **light** | 3 | 6 | 3 | 12 | +1 / +1 / +1 | **4** | **7** | **4** | **15** | Yes (>=3 each) |
| **shadow** | 3 | 7 | 2 | 12 | +1 / +1 / +1 | **4** | **8** | **3** | **15** | Yes (>=3 each) |
| **Total** | **30** | **60** | **30** | **120** | **+10 / +10 / +10** | **40** | **70** | **40** | **150** | **All >= 3** |

*Key Correction*: Prior to Batch 04, Shadow held only 2 high-tier moves (`shadow_eclipse_collapse` at 115 and `shadow_singularity_maw` at 120), falling short of the required threshold of 3. Batch 04 introduced `shadow_entropy_wave` (100 P), successfully lifting Shadow to 3 high-tier moves and satisfying the criteria across all 10 elements.

---

## Prospective Kind "both" Candidates (P2 Integration)

As with earlier batches, several moves authored in Batch 04 embody combat sensations that naturally accept secondary effects. The vocabulary has been closed by P2-01 (`common.schema.json`); human designers should review whether to convert these candidates into `kind: "both"` with explicit effect definitions:
1. **Control & Terrain Disruption**:
   - `metal_siege_bombard`: Extreme kinetic impact creating physical stagger, target `knockdown`, or brief armor reduction.
   - `water_depth_charge`: Submerged concussion wave launching targets into an airborne juggle state.
   - `earth_tremor_ring`: Radial shockwave destabilizing target footing, reducing target movement speed (`stumble` / `slow`).
   - `ice_frostfall_impact`: Frigid perimeter freeze leaving a frosted floor zone that reduces friction or induces `chill`.
   - `thunder_pulse_ring`: Close-range electrostatic discharge interrupting light attack windups (`micro_stun`).
2. **Defensive Mobility & Phasing**:
   - `wind_jetstream_glide`: Accelerated mid-range dash leaving behind a tailwind corridor granting movement buffs to allies.
   - `ice_glaze_slide`: Low-friction melee dash providing brief collision ignore or dodge-frame properties.
   - `light_beacon_lunge`: Instantaneous luminescence blinding close targets while closing the gap.
3. **Zoning & Persistent Spread**:
   - `wood_pine_salvo`: High-spread conical volley scattering persistent sharp needles or sap slows.
   - `fire_pyroclast_sweep`: Incandescent volcanic plume inflicting `burn` over time across a wide sector.
   - `shadow_entropy_wave`: Expansive dark energy cone eroding target defense or siphoning stamina.

---

## Open Questions
1. **Curvature Clamping for Long-Range Cones**: Batch 04 introduces several long-range conical attacks (`wood_pine_salvo`, `fire_pyroclast_sweep`, `earth_scree_spray`, `wind_hurricane_sweep`, `light_chromatic_fan`, `shadow_entropy_wave`). On small-radius spherical planets, broad cone angles over long distances cover significant circumferential area; combat collision systems must clarify whether projectile raycasts adhere to planetary geodesics or project tangent into sub-orbital space.
2. **AoE Ground Targeting vs. Air/Juggle Trajectories**: Moves such as `water_depth_charge` and `metal_siege_bombard` depict high-angle ballistic impacts. If a target is currently in mid-air (due to knockback or an aerial state), clarification is needed on whether AoE cylinder hitboxes extend vertically upward to hit airborne units.
3. **Audio-Visual Telegraph Window Standard**: Finishing attacks with cooldowns exceeding 7,500 ms (`metal_siege_bombard`, `thunder_dynamo_charge`, `shadow_entropy_wave`) will need standardized pre-cast telegraph decals (red ground zones) to ensure clear gameplay counterplay in PvP and co-op environments.
4. **Transition from Placeholder Numbers to Tunable Curves**: With 150 foundational damage moves drafted across Batches 01–04, designers now have a comprehensive baseline dataset for ingestion and combat simulation testing once combat prototype phase P2 commences.
