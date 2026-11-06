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

| `hit_volume.height_mm` | 所有判定體的總高度（melee 乘線性倍率） |
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
