# 宙獸紀 · COSMIMERA

開放世界宙獸收集動作 RPG。每場遊戲隨機生成一顆**球形星球**（地貌、植被、道路、建築全部程序化隨機生成，暗黑破壞神式），玩家召喚宙獸輔助戰鬥。單機為主，可 LAN / 玩家自建伺服器 1–4 人合作或對抗。
引擎：Unity 6 LTS · URP（卡通渲染）· C#。非 Claude 的 AI agent 入口見 `AGENTS.md`。

> **目前階段：Phase 0。** 進度與下一步：`docs/roadmap/README.md`

# 常用指令
Editor：Unity **6000.3.28f1**（`ProjectSettings/ProjectVersion.txt`），路徑 `C:\Program Files\Unity\Hub\Editor\6000.3.28f1\Editor\Unity.exe`（下稱 `$UNITY`）。也可用 Unity CLI `unity`。
Batchmode 需要專案**沒有**在 Editor 中開啟（同一專案只能有一個 Editor 實例）。
- 開啟：`unity open .`
- EditMode 測試：`unity test . --mode EditMode --output Logs/editmode.xml`，或 `"$UNITY" -batchmode -nographics -projectPath . -runTests -testPlatform EditMode -testResults Logs/editmode.xml -logFile Logs/editmode.log`
- PlayMode 測試：同上，`PlayMode`
- 重新套用專案基準設定：`"$UNITY" -batchmode -nographics -projectPath . -executeMethod Cosmimera.EditorTools.ProjectBaseline.Apply -quit -logFile Logs/baseline.log`
- 資料驗證：`tools/validate-data`（P0-05 建立）

# 架構邊界（細節：`docs/architecture/overview.md`）
- **Feature-first**：功能程式碼放在 `Assets/_Project/Features/<Feature>/`，每個 feature 一個 `.asmdef`。
- **封裝**：跨 feature 只能引用對方 `Api/` 資料夾內的型別；不准引用 `Internal/`。
- **規則與表現分離**：戰鬥、屬性計算、生成演算法寫成純 C#（不繼承 `MonoBehaviour`），以便 EditMode 測試與伺服器權威模擬。
- **資料真實來源**：`Data/**/*.json`（含 AI 批次產出）→ importer → ScriptableObject。不手改產生的 `.asset`。

# 文件索引（需要時才讀）
| 何時 | 讀 |
|---|---|
| 不確定現在該做什麼 / 要更新進度 | `docs/roadmap/README.md` → 對應 `phase-N.md` |
| 選擇哪個 AI 執行任務、交接 | `docs/ai-agents.md` |
| 動到球形星球、重力、地形、區塊 LOD | `docs/architecture/planet-tech.md` + `docs/decisions/ADR-0001-cube-sphere.md` |
| 動到星球內容生成（地貌、道路建築、程序化、BOSS、任務） | `docs/design/planet-world.md` |
| 宙獸資料、屬性、能力值、進化、副手、關係表 | `docs/design/creatures.md` |
| 戰鬥、招式、CD、命中、召喚輪替 | `docs/design/combat.md` |
| 大廳、星海選星、圖鑑、收服/取蛋 | `docs/design/lobby-meta.md` |
| 連線、同步、存檔 | `docs/design/multiplayer.md` + `docs/architecture/netcode.md` |
| AI 批次設計宙獸概念 | `docs/design/creature-concept-pipeline.md` |
| 角色設計概念、角色動作 | `docs/design/characters.md` |
| 美術外包 prompt、驗收 `Art/_incoming/<task_id>` | `docs/design/art-asset-pipeline.md` + `tools/prompt-forge/README.md` |
| 畫風、渲染、沉浸感 | `docs/design/vision.md` |
| 需求有歧義 | `docs/open-questions.md`（先查，沒有就新增並停下來問） |

# 硬性規則
- `.claude/rules/code-contracts.md` 永遠適用：**DbC、Why Not What、Chesterton's Fence**。
- 球面世界中**禁止**用 `Vector3.up` / `Physics.gravity` 表示「上方」或重力；一律透過 `PlanetFrame` 取當地 up（見 planet-tech.md）。
- 星球半徑只有一個來源：`PlanetConfig.Radius`。其他尺寸由它推導。
- 程序生成必須**可重現**：同 seed + 同 `GeneratorVersion` → 同結果（連線時客戶端各自生成）。禁用 `UnityEngine.Random` 於生成邏輯。
- 需求不明確時：停下、記錄到 `docs/open-questions.md`、詢問使用者。不寫「先湊合」的 fallback。
- 完成 roadmap 項目後更新該項狀態欄；不要在本檔堆積進度內容。

# 已知陷阱
- 參考作 sakura-crossing（Three.js, R=160 m）用平面→等距圓柱投影包球，極區會壓縮，因此把玩家緯度夾在 ±0.24 圓周內。本作要全球可走 → 改用 cube-sphere（ADR-0001）。不要「沿用參考作投影」。
- R ≈ 640 m 時地平線很近（眼高 1.7 m 約 47 m 可見地面）；遠景靠大氣/天空盒與高地標維持開闊感，而不是靠拉遠攝影機。
