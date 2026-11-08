# D0 初始 Demo（優先於各 phase 出口條件）

**出口條件**：地球大廳選御三家 → 星海選一顆星球（seed）→ 降落在真實地貌骨架 + 噪聲細節的 cube-sphere → 依區域圖探索、遇野生宙獸戰鬥（2 隻在場、輪替、施招、HUD）→ 擊倒後可投擲道具收服 → 完成主線任務鏈 → 擊敗星球 BOSS（野外巢穴）→ 結算返回大廳，收服的宙獸存入大廳存檔。
定案日：2026-11-08（使用者）。各項仍以原 phase 項目 ID 追蹤狀態與交接；本檔只定義 demo 子集與簡化。

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
- 2026-11-08 · ChatGPT Terra（Codex CLI） · done · D0 繁中及英文文案初稿與自行驗收完成 · 下一步：SONNET 審核、FLASH 製作日文初稿
- 2026-11-08 · Gemini Flash（agy） · done · D0 日文翻譯初稿 5 檔與 check.py 完成，123 處本地化欄位人工比對通過 · 下一步：OPUS 驗收
- 2026-11-08 · Claude Opus（驗收 Flash）· done · 重跑 `d0-copy-flash-ja/check.py`：5 檔 123 欄 ALL PASS；抽查術語（交代、捕獲、CT、〜の稱號）符合 brief · 下一步：人工定稿
