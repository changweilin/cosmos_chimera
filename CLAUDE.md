# 宙獸紀 · COSMIMERA

開放世界宙獸收集動作 RPG。每場遊戲隨機生成一顆**球形星球**（地貌、植被、道路、建築全部程序化隨機生成，暗黑破壞神式），玩家召喚宙獸輔助戰鬥。單機為主，可 LAN / 玩家自建伺服器 1–4 人合作或對抗。
引擎：Unity 6 LTS · URP（卡通渲染）· C#。非 Claude 的 AI agent 入口見 `AGENTS.md`。

> 進度與下一步只在 `docs/roadmap/README.md`（本檔不記進度）。

# 常用指令
Editor：Unity **6000.3.28f1**（`ProjectSettings/ProjectVersion.txt`），路徑 `C:\Program Files\Unity\Hub\Editor\6000.3.28f1\Editor\Unity.exe`（下稱 `$UNITY`）。也可用 Unity CLI `unity`。
Batchmode 需要專案**沒有**在 Editor 中開啟（同一專案只能有一個 Editor 實例）。
- 開啟：`unity open .`
- EditMode 測試：`unity test . --mode EditMode --output Logs/editmode.xml`，或 `"$UNITY" -batchmode -nographics -projectPath . -runTests -testPlatform EditMode -testResults Logs/editmode.xml -logFile Logs/editmode.log`
- PlayMode 測試：同上，`PlayMode`
- 重新套用專案基準設定：`"$UNITY" -batchmode -nographics -projectPath . -executeMethod Cosmimera.EditorTools.ProjectBaseline.Apply -quit -logFile Logs/baseline.log`
- 資料驗證：`tools/validate-data`

# 架構邊界（細節：`docs/architecture/overview.md`）
- **Feature-first**：功能程式碼放在 `Assets/_Project/Features/<Feature>/`，每個 feature 一個 `.asmdef`。
- **封裝**：跨 feature 只能引用對方 `Api/` 資料夾內的型別；不准引用 `Internal/`。
- **規則與表現分離**：戰鬥、屬性計算、生成演算法寫成純 C#（不繼承 `MonoBehaviour`），以便 EditMode 測試與伺服器權威模擬。
- **資料真實來源**：`Data/**/*.json`（含 AI 批次產出）→ importer → ScriptableObject。不手改產生的 `.asset`。

# 文件索引（需要時才讀）
| 何時 | 讀 |
|---|---|
| 不確定現在該做什麼 / 要更新進度、交接 | `docs/roadmap/README.md` → `phase-N.md` → `log/`（分層見該檔） |
| 選擇哪個 AI 執行任務、派工 | `docs/ai-agents.md` |
| 跑 demo、人工試玩 | `docs/demo-runbook.md` |
| 動到球形星球、重力、地形、區塊 LOD、地平線 | `docs/architecture/planet-tech.md` + `docs/decisions/ADR-0001-cube-sphere.md` |
| 動到星球內容生成（地貌、道路建築、程序化、BOSS、任務） | `docs/design/planet-world.md` |
| 宙獸資料、屬性、能力值、進化、副手、關係表 | `docs/design/creatures.md` |
| 生活招式／特性與生活—屬性—戰鬥契合設計 | `docs/design/field-utilities.md` |
| 戰鬥、招式、CD、命中、召喚輪替 | `docs/design/combat.md` |
| 場景物件受擊、材質、破壞、剛體 | `docs/design/destructibles.md` + `docs/decisions/ADR-0009-destructible-physics.md` |
| 大廳、星海選星、圖鑑、收服/取蛋 | `docs/design/lobby-meta.md` |
| 連線、同步、存檔 | `docs/design/multiplayer.md` + `docs/architecture/netcode.md` |
| AI 批次設計宙獸概念 | `docs/design/creature-concept-pipeline.md` |
| 角色設計概念、角色動作 | `docs/design/characters.md` |
| 美術外包 prompt、驗收 `Art/_incoming/<task_id>` | `docs/design/art-asset-pipeline.md` + `tools/prompt-forge/README.md` |
| 畫風、渲染、沉浸感 | `docs/design/vision.md` |
| 需求有歧義 | `docs/open-questions.html`（先查，沒有就新增並停下來問） |

# 硬性規則
- 程式碼三原則（改 `.cs` 時自動載入 `.claude/rules/code-contracts.md`）：public 介面寫 Pre/Post/Side effects · 註解只寫 Why · `// FENCE:` 未經指示不重構。
- 其餘規則依路徑自動載入（`.claude/rules/`：`unity-csharp`、`testing`、`data-content`、`docs-and-progress`）。
- 球面世界中**禁止**用 `Vector3.up` / `Physics.gravity` 表示「上方」或重力；一律透過 `PlanetFrame` 取當地 up（見 planet-tech.md）。
- 星球半徑只有一個來源：`PlanetConfig.Radius`。其他尺寸由它推導。
- 程序生成必須**可重現**：同 seed + 同 `GeneratorVersion` → 同結果（連線時客戶端各自生成）。禁用 `UnityEngine.Random` 於生成邏輯。
- 需求不明確時：記錄到 `docs/open-questions.html`、詢問使用者，只停該項目、改做其他項目。不寫「先湊合」的 fallback（例外：該問題已有「暫定」且只影響 `Data/` 數值，見 `AGENTS.md` §2）。
- 不沿用參考作 sakura-crossing 的等距圓柱投影（極區壓縮）；本作用 cube-sphere。遠景靠大氣與高地標，不靠拉遠攝影機。理由見 planet-tech.md。
- 完成定義與交接見 `AGENTS.md` §3（commit message 即交接；只更新狀態欄）。
