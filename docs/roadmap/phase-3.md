# P3 世界生成

**出口條件**：選定 seed 與星球類型 → 生成含程序化地貌、道路與聚落、生物群系、區域圖、地城、BOSS 的星球；可從降落玩到 BOSS 結束。
讀：`../design/planet-world.md`、`../architecture/planet-tech.md`
**開工前需定案**：Q-M2（Q-W1 已因 ADR-0008 不再需要）

| ID | 項目 | 主責 | 審查 | 驗收條件 | 狀態 |
|---|---|---|---|---|---|
| P3-01 | ~~真實地圖資料授權調查與署名方案（Q-W1）~~ 退役：地貌改程序化（ADR-0008） | ARGON | OPUS + 人工 | — | ⛔ |
| P3-02 | 程序化地貌合成 `LandformSynth`（12 種地貌標籤 → 高程/水系格網，整數運算；取代 DEM 烘焙，ADR-0008） | OPUS | SOL | 每種標籤可辨識；同輸入跨後端逐位元相同 | 🟨 |
| P3-03 | 地貌印章 gnomonic 投影拼貼 + 外緣混合 | OPUS | SOL | 印章內地貌可辨識；接縫無斷崖 | 🟨 |
| P3-11 | 道路與建築：規則層 `SettlementPlanner`（生成樹道路、聚落地塊）＋表現層 | OPUS（規則）+ SONNET（表現） | SOL | 每區域道路可達；建築不落海、不互相重疊；快照測試 | 🟨 |
| P3-04 | 生物群系（緯度×海拔×濕度×星球類型） | SONNET | SOL | 6 種星球類型視覺差異明顯 | 🟦 |
| P3-05 | 區域圖（Voronoi + 連通保證）+ 區域詞綴 | OPUS | SONNET | 1000 個 seed 測試：100% 可達 BOSS | 🟨 |
| P3-06 | POI 佈置、宙獸棲地、野生宙獸生成（依 Q-M2） | SONNET | OPUS | POI 密度符合每 km² 預算 | 🟨 |
| P3-07 | 暗黑式地城：房間模組 + 拼接規則 + 詞綴 | OPUS（規則）+ SONNET（實作） | SOL | 快照測試；無封死房間 | ⬜ |
| P3-08 | 任務鏈生成與 BOSS 戰框架 | SONNET | OPUS | 每星球至少 1 主線 + 1 BOSS | 🟨 |
| P3-09 | 散佈系統（植被/岩石/互動物件 per biome） | SONNET | — | 無浮空/穿地物件 | 🟨 |
| P3-10 | 生成可重現性測試（跨機器雜湊一致） | SONNET | OPUS | Windows 兩台機器 `tools/repro/compare.py --policy b` PASS（離散內容相同、高度差 ≤ 3 mm，ADR-0007） | 🟦 |

## 交接紀錄
見 [log/phase-3.md](log/phase-3.md)（只追加）。
