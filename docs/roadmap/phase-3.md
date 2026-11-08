# P3 世界生成

**出口條件**：選定 seed 與星球類型 → 生成含真實地貌骨架、生物群系、區域圖、地城、BOSS 的星球；可從降落玩到 BOSS 結束。
讀：`../design/planet-world.md`、`../architecture/planet-tech.md`
**開工前需定案**：Q-W1（授權）、Q-M2

| ID | 項目 | 主責 | 審查 | 驗收條件 | 狀態 |
|---|---|---|---|---|---|
| P3-01 | 真實地圖資料授權調查與署名方案（Q-W1） | ARGON | OPUS + 人工 | 授權摘要與採用清單 | 🟨 |
| P3-02 | 離線烘焙工具：DEM（Copernicus GLO-90，Q-W1 方案 A）→ 地理模板包（遮罩/高程/水系） | SONNET | OPUS | 產出 ≥ 12 個模板，含預覽圖 | 🟨 |
| P3-03 | 模板 gnomonic 投影拼貼 + 接縫山脈 | OPUS | SOL | 模板內真實地貌可辨識；接縫無斷崖 | 🟨 |
| P3-04 | 生物群系（緯度×海拔×濕度×星球類型） | SONNET | SOL | 6 種星球類型視覺差異明顯 | 🟦 |
| P3-05 | 區域圖（Voronoi + 連通保證）+ 區域詞綴 | OPUS | SONNET | 1000 個 seed 測試：100% 可達 BOSS | 🟨 |
| P3-06 | POI 佈置、宙獸棲地、野生宙獸生成（依 Q-M2） | SONNET | OPUS | POI 密度符合每 km² 預算 | 🟨 |
| P3-07 | 暗黑式地城：房間模組 + 拼接規則 + 詞綴 | OPUS（規則）+ SONNET（實作） | SOL | 快照測試；無封死房間 | ⬜ |
| P3-08 | 任務鏈生成與 BOSS 戰框架 | SONNET | OPUS | 每星球至少 1 主線 + 1 BOSS | 🟨 |
| P3-09 | 散佈系統（植被/岩石/互動物件 per biome） | SONNET | — | 無浮空/穿地物件 | 🟨 |
| P3-10 | 生成可重現性測試（跨機器雜湊一致） | SONNET | OPUS | Windows 兩台機器同 seed 雜湊相同 | ⬜ |

## 交接紀錄
- 2026-11-06 · Gemini Flash（agy，Claude Opus 驗收）· partial · P3-04 資料準備：`Data/_incoming/p3-planet-types-draft-01/`，planet-world.md 表中 6 種星球類型轉成 JSON（只有 id/size/terrain_tags/element_bias，無數值），12 個共用地貌標籤，README 附繁中/英/日名稱。Opus 驗收：`validate-data --root` 0 錯誤、尺寸與屬性逐列對照設計表一致。待決：`eternal_day`/`eternal_night` 是光照遮罩而不是地形模板，P3-03/P3-04 要決定怎麼處理 · 下一步：人工確認 id 與名稱後搬到 `Data/planet-types/`
- 2026-11-07 · Gemini Argon（agy） · partial · 完成 P3-01 真實地圖資料授權調查與署名方案（`docs/research/P3-01-map-data-licensing.md`），比較 OSM ODbL、6 項高程/水深與 WorldCover/HydroSHEDS 商用再散布條件，提出零 Share-Alike 首選組合、中英署名範本與待律師確認風險點 · 下一步：OPUS + 人工審查並定案 Q-W1
- 2026-11-07 · Claude Opus（驗收 Argon）· partial · P3-01 報告結構與範圍符合任務；抽查官方來源：OSMF Substantial Guideline「Less than 100 Features」屬實；HydroSHEDS 使用條款頁本身沒有寫產品授權（各產品另附），報告的「避免 HydroSHEDS v1」結論保守、不受影響；「HydroSHEDS v2 自 2026-07-09 分區釋出」**未能查證**。推薦方案 A（Copernicus GLO-30 + NASADEM + ETOPO/GEBCO + ESA WorldCover + DEM 自算水系，不用 OSM）會改變 P3-02 的項目名稱（「DEM + OSM」），**待人工決定**是否採用 · 下一步：人工（必要時律師）確認 Q-W1 與 §4.4 四個風險點；定案後更新 planet-world.md 與 P3-02
- 2026-11-08 · ChatGPT Sol（Codex CLI） · blocked · P3-02 已讀規格並確認原分支；沙箱內 `python` 無法辨識，直接執行 `C:/Users/user/AppData/Local/Programs/Python/Python313/python.exe` 回傳 `Access is denied`，且本工作階段禁止權限提升；無法檢查 tifffile COG 解碼或執行驗收，尚未實作，實際產出 0 區、下載 0 圖磚／0 MB · 下一步：人工恢復 Python 執行權限後由 SOL 完成實作與全部驗收，再交 OPUS 驗收並接 P3-03
- 2026-11-08 · ChatGPT Sol（Codex CLI） · blocked · P3-02 full-access 已確認 Python 3.13.1、numpy 2.3.5、Pillow 12.2.0、tifffile 2024.12.12 可用且維持原分支；下載 Olympic 森林候選區所需 `Copernicus_DSM_COG_30_N47_00_W124_00_DEM.tif` 至 `tools/geodata/_cache/`（1 圖磚，5,575,062 bytes／5.575062 MB），其 COG 為 Deflate（compression=8）及浮點預測器（predictor=3），`tifffile` 解碼回報 `<PREDICTOR.FLOATINGPOINT: 3> requires the 'imagecodecs' package`；依 brief 停止，未安裝套件、未繼續下載，實際產出 0 區，烘焙與測試驗收未執行，保留 ⛔ · 下一步：人工調整依賴限制以解除 COG 解碼阻塞，SOL 完成全部驗收後交 OPUS 驗收並接 P3-03
- 2026-11-08 · ChatGPT Sol（Codex CLI） · done · P3-02 以 tifffile 標籤／tile offsets + stdlib zlib + numpy 自行還原 predictor-3，未安裝套件；產出 12 區 256×256 模板、涵蓋 12 tags，含中英署名與預覽；合成 predictor-3 位元精確 round-trip、地形／水文／格式／決定性測試 23/23，tools/tests 73/73 通過；真實烘焙後 --offline 重跑零下載，49/49 輸出 SHA-256 相同；23 張真實圖磚 GeoKeys／高程範圍檢查通過，12 張預覽已檢視且皆非全平／全海、河道斷流 receiver 0；本輪下載 22 圖磚／75.653772 MB，重用前輪 1 圖磚／5.575062 MB，快取合計 23 圖磚／81.228834 MB；逐區高程與海／河佔比及輸出雜湊見 tools/geodata/acceptance.json；保留原分支、未 commit／push · 下一步：OPUS 驗收並接 P3-03
- 2026-11-08 · Claude Opus（驗收 Sol P3-02）· done · 重跑 `pytest tools/tests` 73/73；`bake.py --offline` 前後 49 檔 sha256 相同；目視 sognefjord／etna 預覽，峽灣與火山錐可辨識。D0 內不再要求 OSM（Q-W1 方案 A）· 下一步：OPUS 做 P3-03 模板拼貼
- 2026-11-08 · Claude Opus · partial · D0：P3-03 `Planet/Api/PlanetTemplates`（gnomonic 投影、外緣 35% smoothstep 混入噪聲、海/河雙線性下切；Burst 路徑經 `ChunkMeshJob`；LOD margin 加上模板界）+ `WorldGen/TemplatePlanner`（依星球地貌標籤選 4/6 塊、Pcg32(seed,'T')）；Q-T5 未決，貼模板時以 −East 反鏡像（可逆）。P3-04 只有 shader 層生物群系上色（`PlanetLook` 每種星球類型一組色盤），無規則層 biome 表。P3-05/06/08 `RunPlanner`（Gabriel 圖保證連通、1000 seed 全可達 BOSS、主題/詞綴/野生群/菁英/BOSS/任務鏈，快照 GeneratorVersion 1）+ `QuestTracker`。P3-09 散佈未做。Sol D0 唯讀審查此範圍 0 high · 下一步：人工試玩；P3-09
- 2026-11-09 · Claude Sonnet · partial · P3-09 D0 版：Bootstrap/Demo/ScatterField 以 cube-face 格網、每格獨立 Pcg32 串流（同 seed 同結果）在玩家 90 m 內串流生成樹/岩/水晶佔位幾何（依星球類型配色；排除海面與坡度 > 0.6）；已接入 RunSession 並在 PlayMode 煙霧測試加入「有散佈且貼地」斷言。未做：依 biome 表（P3-04 資料化）、互動物件、GPU instancing；未編譯/未測試（Editor 開啟中） · 下一步：人工在 Editor 跑 PlayMode 並目視；OPUS 審查
