# `Data/balance.json` 鍵值說明

2026-11-06 使用者定案（SOL 主責、OPUS 審查）。完整公式、曲線與原因見 [derived-stats.md](../derived-stats.md)。

JSON 所有葉節點均為整數。率採千分比（1000=100%），招式 accuracy 保留整數百分比；距離 mm、速度 mm/s、時間 ms。版本與統計點數也為整數。

| Key | 說明 |
|---|---|
| `model_version` | 公式 / 取整規約版本，現為 1；不是隨機 seed |
| `stat_effective_cap` | 每個 stat 進曲線前的飽和值；接受更大原始輸入但結果相同 |
| `hp.base` | CON=0 的底血 |
| `hp.gain` | 曲線可增加的漸近 HP，最大 HP 不超過 base+gain |
| `hp.half_stat` | HP 增益達一半時的 CON |
| `melee_volume.gain_permille` | 近戰體積在基礎 1000 之上的漸近增益，不是線性尺寸增益 |
| `melee_volume.half_stat` | 近戰體積增益達一半時的 STR |
| `ranged_distance.gain_permille` | 遠程距離在基礎 1000 之上的漸近增益 |
| `ranged_distance.half_stat` | 遠程距離增益達一半時的 STR |
| `ranged_distance.cap_mm` | 乘完倍率後的最終距離硬上限；包含過大的基礎射程 |
| `ranged_distance.horizon_factor_permille` | 星球射程上限 K：可及距離占眼高地平線的千分比（1..1000，定案 775）；見 [星球射程上限](../derived-stats.md#星球射程上限) |
| `ranged_distance.horizon_eye_height_mm` | 星球射程上限 H：平衡基準眼高（1..10000 mm，定案 1700），非即時攝影機高度 |
| `cd.min_permille` | SPI 很大時 CD 倍率的下界；不是固定減掉的毫秒數 |
| `cd.half_stat` | CD 倍率減少量達一半時的 SPI |
| `cd.floor_ms` | 乘完倍率、向上取整後的絕對 CD 下限，原始 CD=0 也適用 |
| `speed.base_mm_s` | AGI=0 的移速 |
| `speed.gain_mm_s` | 移速可增加的漸近量 |
| `speed.half_stat` | 移速增益達一半時的 AGI |
| `evasion.cap_permille` | 閃避漸近上限，400 表 40% |
| `evasion.half_stat` | 閃避達上限一半時的 AGI |
| `hit.floor_permille` | 幾何命中後擲骰的最低機率；50 表 5% |
| `reference_move.cooldown_ms` | 工具預設 / 範例用招式 CD，正式執行時須傳招式自己的值 |
| `reference_move.accuracy_percent` | 工具預設 / 範例用招式命中百分比，100 表 100% |
| `reference_move.base_range_mm` | 工具預設 / 範例用遠程基礎距離，非現有 schema 的新欄位 |
| `stage_bands.<stage>.min_stat` | 該階段每項能力的軟製作下界，非 runtime clamp |
| `stage_bands.<stage>.max_stat` | 該階段每項能力的軟製作上界，非 schema maximum |
| `stage_bands.<stage>.total_stats` | 該階段四項總點數預算；不是單項上限 |

| `hit_volume.height_mm` | 所有判定體的總高度（melee 乘線性倍率）；下限 2，讓半高至少 1 mm |
| `hit_volume.front_width_mm` / `line_width_mm` | front / line 矩形寬度 |
| `hit_volume.sweep_angle_deg` / `cone_angle_deg` | 扇形全角（度） |
| `hit_volume.arc_landing_radius_mm` / `field_radius_mm` | arc 落點與 field 的圓半徑 |
| `hit_volume.target_cast_mid_mm` / `target_cast_long_mm` | 目標點爆發的基礎施法距離，再乘 g(STR) |

`hit_volume` 為 P2-04 的全域預設（2026-11-06 使用者確認），規則見 [combat.md#判定體](../combat.md)。

`stage` 為 `juvenile / adolescent / adult`。階段不參與衍生值計算，避免進化時再乘一次倍率。半飽和值不必與軟設計帶上界相等。
`P=1000` 與百分比單位 `100` 是移植協定，非可調平衡係數。參考實作對可移植係數範圍與正下限有明確檢查，見設計文件。

重產黃金向量：`python tools/balance/derived_stats.py --write-golden`。
測試：`python -m unittest discover -s tools/balance -p "test_*.py"`。
繪圖：`python tools/balance/plot_curves.py`（直接寫 SVG，僅標準函式庫）。

`python tools/validate_data.py` 不掃描 `balance.json`；係數型別 / 算術包絡由參考 loader（`tools/balance` 測試）與 C# `DerivedStatsCoefficients` 建構子檢查。
| `field.tick_ms` | Q-C17：場地兩次結算的間隔；放置當下即結算第一次 |
| `effects.multiplier_min_percent` / `multiplier_max_percent` | P2-09：相對百分比效果（cd/evasion/speed/range）加總後的倍率 100+Σ 夾在此區間（暫定 10–300） |
| `team.switch_cooldown_ms` | P2-06 共用切換冷卻：換手或主動收回後開始；開場召喚與倒下後補位不觸發，但會等冷卻結束 |
| `player.max_hp` | 玩家角色固定 HP，不由能力值推導（P2-06 暫定） |
| `player.roll_duration_ms` | 一次翻滾全長 |
| `player.roll_invulnerable_ms` | 從翻滾開始算的無敵幀長度，≤ 翻滾全長；區間 [開始, 開始+長度) |
| `player.roll_cooldown_ms` | 兩次翻滾開始的最短間隔，≥ 翻滾全長（避免連滾得到永久無敵） |
| `ai.keep_distance_permille` | P2-10 保距站位 = 最長敵對可及距離 × 此值 / 1000（1–1000） |
| `ai.close_in_permille` | P2-10 突進 / 繞背站位 = 最短敵對可及距離 × 此值 / 1000（1–1000） |
| `ai.hold_permille` | P2-10 站樁：目標在最長可及距離 × 此值 / 1000 內不移動（1–1000） |
| `ai.follow_leash_mm` | P2-10 跟隨待命：離玩家超過此地表距離才往回走（> 0） |
| `ai.territory_mm.{timid,cautious,normal,territorial,fierce,apex}` | D0-E2 野生領域半徑（依物種 `temperament` 取 0/4/8/14/20/28 m）；入侵者進入即開戰；0＝不主動開戰（Q-C35；0 ≤ 值 < 40 m 脫戰距離） |
| `ai.wander_mm.{timid,cautious,normal,territorial,fierce,apex}` | D0-W1 野生未開戰時在生成點附近的遊蕩半徑（3/4/6/8/10/12 m，Q-D2 2026-11-10 定案）；0＝原地不動（0 ≤ 值 < 40 m 脫戰距離） |
| `ai.wander_pause_min_ms`／`ai.wander_pause_max_ms` | D0-W1 每段遊蕩之間的停頓（含兩端，2000–5000，Q-D2 2026-11-10 定案；0 ≤ min ≤ max） |
| `ai.wander_speed_permille` | D0-W1 遊蕩步行速度 = 戰鬥移速 × 此值 / 1000（400，Q-D2 2026-11-10 定案；1–1000） |

## progression

P2-12，2026-11-09 定案（Q-C28 曲線、Q-C29／Q-C30 方案 A）。規則與公式見 [level-progression.md](../level-progression.md)；C# `Creatures/Api/Progression.cs`。

| Key | 說明 |
|---|---|
| `progression.level_max` | 最高等級（100）；累計經驗上限 T(level_max) |
| `progression.starter_level` | 新取得御三家的等級（10），初始經驗 T(10) |
| `progression.xp_first_level_cost` / `xp_growth_numerator` / `xp_growth_denominator` | C(L) = ceil(first × (num/den)^(L−1))，以精確整數計算 |
| `progression.evolution_level_bands` | 進化等級門檻允許區間（含端點）；schema 與 `CreatureDef.EvolutionLevelMin/Max` 鏡像，測試檢查一致 |
| `progression.migration_stage_floor_levels` | 存檔格式 1→2 遷移：幼年／少年／成年改為 T(10／20／35) |
| `progression.appeared_weight` / `reserve_weight` | 主力分配權重：出場 4、後備 2；死亡 0 且移出分母 |
| `progression.rewards.base_xp.*` | 來源基數：wild／capture／quest_step／quest_chain／boss |
| `progression.rewards.reference_level` | E = floor(base × C(min(L,99)) × r / C(reference_level)) 的分母等級（10） |
| `progression.rewards.elite_multiplier` | 擊倒菁英的 r（2）；只用於野戰擊倒 |
| `progression.rewards.wild_level.*` | BOSS 等級 B(d) = boss_level_min + round_half_up((boss_level_max − boss_level_min)×(d−1)/9)；野生等級 = clamp(B(d) − role_bonus.boss − per_tier×(2−tier) + role_bonus, 1, 100)（D0-X3） |
| `progression.rewards.sidekick_share_divisor` | 副手組共分 floor(主力實得 / 此值)；目前只支援 2 |
| `progression.player.xp_multiplier` | 玩家等級曲線 = 此值 × T(L)（6；D0-B2，[level-progression.md#玩家等級](../level-progression.md#玩家等級)） |
| `progression.player.boss_stage_unlock_levels` | `[1, 少年解鎖, 成年解鎖]` = `[1, 20, 35]`；已解鎖 BOSS 階段均等抽（[planet-world.md#boss-尺寸](../planet-world.md#boss-尺寸)） |

## materials

P2-05，2026-11-09 定案（Q-C32 方案 B）。規則見 [creatures.md#進化](../creatures.md#進化)；C# `Creatures/Api/MaterialDrops.cs`。

| Key | 說明 |
|---|---|
| `materials.normal` / `elite` / `boss` | 擊倒該角色野生時：`tier`（1–3）、`chance_percent`（0–100）、成功時 `qty`（1–1000）。屬性從星球類型 `element_bias` 等機率抽一種；每個 spawn 只結算一次，角色不疊加 |
