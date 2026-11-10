# D0 初始 Demo（優先於各 phase 出口條件）

**出口條件**：地球大廳選御三家 → 星海選一顆星球（seed）→ 降落在真實地貌骨架 + 噪聲細節的 cube-sphere → 依區域圖探索、遇野生宙獸戰鬥（2 隻在場、輪替、施招、HUD）→ 擊倒後可投擲道具收服 → 完成主線任務鏈 → 擊敗星球 BOSS（野外巢穴）→ 結算返回大廳，收服的宙獸存入大廳存檔。
定案日：2026-11-08（使用者）。各項仍以原 phase 項目 ID 追蹤狀態與交接；本檔只定義 demo 子集與簡化。

2026-11-09 使用者優先順序：可 demo 的工作 → 遊戲系統完整性 → 宙獸／角色文案、畫面及 3D 建模。操作提示與任務引導屬 demo 必需 UI；內容擴量與美術製作後置。

## 範圍決定
| 決定 | 內容 | 寫入 |
|---|---|---|
| 不做 | 地城室內（P3-07）；BOSS 在野外巢穴 | — |
| 不做 | 孵蛋（P4-05，Q-M3 延後）：蛋只進背包顯示 | — |
| 佔位 | 宙獸/角色模型用卡通著色幾何體 + 屬性色 + 體型尺寸；程式端保留模型替換介面 | `art-asset-pipeline.md` |
| 定案 | Q-M2 野外可收服（擊倒後投擲）；Q-M4 roguelite 一場到底；Q-W1 方案 A；Q-T1 steering | 各設計文件 |
| 審查 | demo 前 Sol 審查只修 **high** 與阻礙遊玩的問題；med/low 記入 `docs/tech-debt.md`，demo 後處理 | — |

## Demo 子集
| 群組 | 項目 | 主責（本次） | 備註 |
|---|---|---|---|
| 地形 | P3-02 地理模板烘焙工具 | SOL（Codex） | 方案 A 資料；獨立 Python 工具 |
| 地形 | P3-03 模板拼貼、P3-04 生物群系、P3-09 散佈 | OPUS | 依 P3-02 輸出格式 |
| 渲染 | P1-07 卡通著色（最低限度）、P1-08 天空 | OPUS | 不含 P1-09 大量草 |
| 導航 | P1-06 → steering | OPUS | 不做 NavMesh 原型 |
| 世界 | P3-05 區域圖 + 詞綴、P3-06 POI/野生、P3-08 任務鏈 + BOSS | OPUS | |
| 戰鬥 | P2-05/07/09/10 表現層接線、P2-11 HUD | OPUS | 規則層已完成 |
| Meta | P4-01 大廳（最低限度）、P4-02 選星、P4-03 收服、P4-06 存檔（只存大廳）、P4-07 隊伍編成（最低限度） | OPUS | |
| 文案 | 區域主題/詞綴/任務/BOSS/UI 字串 zh-TW + en | TERRA（Codex） | `Data/_incoming/d0-copy-terra/` |
| 文案 | 上項 ja 翻譯初稿 | FLASH（agy） | `Data/_incoming/d0-copy-flash-ja/` |

### 2026-11-10 使用者新增需求
| ID | 項目 | 主責 | 規格 | 狀態 |
|---|---|---|---|---|
| D0-B2 | BOSS 階段決定星球尺寸（幼→小、少→中、成/超載→大）；選星卡片顯示 BOSS 階段與尺寸；`planet-types.size` 退役 | OPUS | `planet-world.md#boss-尺寸`；R=320 阻塞於 Q-D1 | ⬜ |
| D0-W1 | 野生在生成點附近依性格遊蕩 | OPUS | `combat.md#遊蕩`；數值 Q-D2 暫定 | 🟨 |
| D0-W2 | 角色／宙獸實體碰撞 | OPUS | `combat.md#實體碰撞` | ⬜ |
| D0-W3 | 近身互動：觀察／挑釁／撫摸 | OPUS（規則）＋TERRA（文案） | `combat.md#近身互動` | ⬜ |
| D0-W4 | 指定攻擊目標：目標切換＋鎖定標記 | OPUS | `combat.md#指定目標` | 🟨 |
- 2026-11-08 · ChatGPT Terra（Codex CLI） · done · D0 繁中及英文文案初稿與自行驗收完成 · 下一步：SONNET 審核、FLASH 製作日文初稿
- 2026-11-08 · Gemini Flash（agy） · done · D0 日文翻譯初稿 5 檔與 check.py 完成，123 處本地化欄位人工比對通過 · 下一步：OPUS 驗收
- 2026-11-08 · Claude Opus（驗收 Flash）· done · 重跑 `d0-copy-flash-ja/check.py`：5 檔 123 欄 ALL PASS；抽查術語（交代、捕獲、CT、〜の稱號）符合 brief · 下一步：人工定稿
- 2026-11-08 · Claude Opus · partial · D0 可完整跑一場：`Bootstrap/Demo.unity` 大廳→選星→真實地貌星球（12 個 GLO-90 模板）→野生戰鬥/收服/任務→BOSS→結算存檔。EditMode 862/862、PlayMode 62/62（含 run 煙霧測試：降落、交戰、造成傷害、無錯誤 log）。Sol 唯讀審查 2 high（LobbySave）已修。未做：P3-09 散佈、P4-07 隊伍編成 UI、P2-05 進化接線、星空；**未經人工實機試玩**（輸入手感、難度）。待決 Q-D1 · 下一步：人工試玩 `Demo.unity`，回報手感/難度
- 2026-11-09 · Claude Sonnet · partial · P4-08 接線：`merge.py` 併入 `demo-ui-supplement.json`（鍵衝突即失敗）；選星/任務顯示星球與屬性名稱，HUD 補主力選擇/指令鍵/收服道具說明，放棄提示改為三秒內再按 Esc。未驗證編譯與測試（Unity Editor 開啟中，batchmode 無法執行）；ja 仍未接線 · 下一步：人工在 Editor 跑 EditMode/PlayMode 並試玩；FLASH 翻補 supplement 日文。注意：`merge.py` 會 rmtree GeoTemplates 而刪除 .meta，執行後需 `git checkout -- Assets/_Project/Data/GeoTemplates`
- 2026-11-09 · Claude Opus（派工/驗收）＋ ChatGPT Sol/Terra（Codex）＋ Gemini Flash（agy） · partial · 批次模式補驗 P4-08/P4-07/P3-09：EditMode 862/862、PlayMode 62/62。Sol 唯讀審查 `14ebb9e..ba6ea89`（`docs/reviews/D0-r2-sol.md`）fail：S1 high `merge.py` rmtree GeoTemplates 刪 .meta → Terra 改為就地更新、只清退役產生檔＋其 .meta，並一併修 S3（先驗證後寫入）、S4（拒絕同檔重複鍵），新增 `tools/tests/test_demo_merge.py`（Python 104/104）；Opus 實跑 merge：36 個 .meta 保留、strings.json 與手修版逐位元相同。S2 七個隊伍字串問號 → Terra 修 zh-TW；Flash 補 supplement ja（check.py 150 欄 PASS）。S5 low 記入 tech-debt。P1-08 星空完成（見 phase-1）。P2-05 進化接線**阻塞於 Q-C29**（經驗獎勵量與素材來源未定）。merge.py 不再需要 `git checkout -- GeoTemplates` · 下一步：人工試玩（夜空、隊伍編成）；決定 Q-C29
- 2026-11-10 · Claude Opus · done（待審）· D0-W1 遊蕩：`Encounter` 未開戰野生依性格在生成點附近遊蕩（`WanderTuning`、`balance.json#ai.wander_*` 暫定值 Q-D2），獨立亂數串流 (seed,'W') 不動戰鬥擲骰；脫戰在半徑外先直接走回；開戰取消路段。新增 `EncounterWanderTests`（12 案例）。EditMode 1021/1021、PlayMode 87/87。未人工試玩；遊蕩不避海面（tech-debt D0-W1）· 下一步：人工試玩看遊蕩手感；SOL 審查；D0-W4／W2
- 2026-11-10 · Claude Opus · done（待審）· D0-W4 指定目標：`RunSession` 新增 `CycleTarget` 動作（Q／手把 LB），依（距離, id）循環鎖定、集火打鎖定目標、集火中切換即改打、失效自動解除；OnGUI 黃色 ▼ 標記與 HUD「切換目標：名稱」。新字串 `ui.hud.target_cycle`／`target_none`（zh-TW／en／ja 初稿，Opus 撰寫，待 TERRA／FLASH 校稿），`merge.py` 重產、ja check.py 182 欄 PASS。新增 PlayMode `W4_cycle_locks_by_distance_and_focus_attacks_the_lock`。EditMode 1021/1021、PlayMode 88/88。未人工試玩 · 下一步：人工試玩；SOL 審查；D0-W2 實體碰撞
