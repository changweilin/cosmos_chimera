# P3 世界生成

**出口條件**：選定 seed 與星球類型 → 生成含真實地貌骨架、生物群系、區域圖、地城、BOSS 的星球；可從降落玩到 BOSS 結束。
讀：`../design/planet-world.md`、`../architecture/planet-tech.md`
**開工前需定案**：Q-W1（授權）、Q-M2

| ID | 項目 | 主責 | 審查 | 驗收條件 | 狀態 |
|---|---|---|---|---|---|
| P3-01 | 真實地圖資料授權調查與署名方案（Q-W1） | ARGON | OPUS + 人工 | 授權摘要與採用清單 | ⬜ |
| P3-02 | 離線烘焙工具：DEM + OSM → 地理模板包（遮罩/高程/水系） | SONNET | OPUS | 產出 ≥ 12 個模板，含預覽圖 | ⬜ |
| P3-03 | 模板 gnomonic 投影拼貼 + 接縫山脈 | OPUS | SOL | 模板內真實地貌可辨識；接縫無斷崖 | ⬜ |
| P3-04 | 生物群系（緯度×海拔×濕度×星球類型） | SONNET | SOL | 6 種星球類型視覺差異明顯 | ⬜ |
| P3-05 | 區域圖（Voronoi + 連通保證）+ 區域詞綴 | OPUS | SONNET | 1000 個 seed 測試：100% 可達 BOSS | ⬜ |
| P3-06 | POI 佈置、宙獸棲地、野生宙獸生成（依 Q-M2） | SONNET | OPUS | POI 密度符合每 km² 預算 | ⬜ |
| P3-07 | 暗黑式地城：房間模組 + 拼接規則 + 詞綴 | OPUS（規則）+ SONNET（實作） | SOL | 快照測試；無封死房間 | ⬜ |
| P3-08 | 任務鏈生成與 BOSS 戰框架 | SONNET | OPUS | 每星球至少 1 主線 + 1 BOSS | ⬜ |
| P3-09 | 散佈系統（植被/岩石/互動物件 per biome） | SONNET | — | 無浮空/穿地物件 | ⬜ |
| P3-10 | 生成可重現性測試（跨機器雜湊一致） | SONNET | OPUS | Windows 兩台機器同 seed 雜湊相同 | ⬜ |

## 交接紀錄
- 2026-11-06 · Gemini Flash（agy，Claude Opus 驗收）· partial · P3-04 資料準備：`Data/_incoming/p3-planet-types-draft-01/`，planet-world.md 表中 6 種星球類型轉成 JSON（只有 id/size/terrain_tags/element_bias，無數值），12 個共用地貌標籤，README 附繁中/英/日名稱。Opus 驗收：`validate-data --root` 0 錯誤、尺寸與屬性逐列對照設計表一致。待決：`eternal_day`/`eternal_night` 是光照遮罩而不是地形模板，P3-03/P3-04 要決定怎麼處理 · 下一步：人工確認 id 與名稱後搬到 `Data/planet-types/`
