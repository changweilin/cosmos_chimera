# P5 內容擴量

**出口條件**：≥ 60 條進化線（≥ 180 隻）、≥ 150 招式、≥ 60 特性、關係表連通；屬性/地區分布平均；通過平衡試玩。
讀：`../design/creature-concept-pipeline.md`、`../design/creatures.md`
**開工前需定案**：Q-T3（圖像/3D 工具）

| ID | 項目 | 主責 | 審查 | 驗收條件 | 狀態 |
|---|---|---|---|---|---|
| P5-01 | 批次 prompt 模板 `tools/prompts/*.md` + 配額矩陣 | SONNET | OPUS | 模板產出可直接通過 schema | ⬜ |
| P5-02 | 文化原型考據庫（各地區、敏感度標記） | ARGON | OPUS + 人工 | 每地區 ≥ 15 原型 | ⬜ |
| P5-03 | 宙獸批次初稿（每批 20–50） | FLASH / TERRA | SONNET | 每批通過驗證工具 | ⬜ |
| P5-04 | 概念圖 prompt 與美術生產（依 Q-T3），經 `tools/prompt-forge` | TERRA | 人工 | 每線 4 型態（含超載）概念圖 | 🟦 |
| P5-05 | 招式/特性批次 | FLASH | SONNET + SOL | 無重複效果、平衡在範圍內 | ⬜ |
| P5-06 | 關係表建議與連通性分析 | ARGON | SONNET | 每隻至少 2 條邊；無孤島 | ⬜ |
| P5-07 | 數值平衡模擬（自動對戰 Monte Carlo） | SOL（模型）+ SONNET（工具） | OPUS | 屬性勝率差 < 10% | ⬜ |
| P5-08 | 全資料集一致性掃描 | ARGON | 人工 | 命名/設定無矛盾報告 | ⬜ |
| P5-09 | 美術交付驗收工具：檢查 `Art/_incoming/<task_id>/manifest.json`、命名、尺寸，搬入 `Assets/_Project/Art/` | SONNET | 人工 | 不合規交付有清楚報告；合規者一鍵搬移 | ⬜ |

## 交接紀錄
- 2026-11-06 · Claude Opus · partial · P5-04：建立 `tools/prompt-forge`（7 種任務 prompt、版本標頭、交付規格），設計文件補上超載、體型級距、收服道具三類別 · 下一步：人工用工具試跑一輪 creature-concept；Q-T3 選定 3D 工具；Q-C12/Q-C13/Q-A1 待定
- 2026-11-06 · Gemini Flash（agy，Claude Opus 驗收）· partial · P5-05 招式草稿 batch 01：`Data/_incoming/p5-05-moves-draft-01/`，共 40 招，全部是 `damage`（P2 之前 effect 詞彙未定），每個屬性 4 招，近／中／遠三種距離都有，四種形狀各 10 招。數值是佔位帶：威力 20–120、CD 800–10000 ms、命中率 70–100。`validate-data` 0 錯誤。Opus 已重新驗算數量、分佈與數值帶。README 列出 4 個待決問題（形狀幾何參數、哪些招式應改為 both、球面上的距離刻度、招牌招式的指派）。agy headless 模式不能執行 shell 指令，所以這批改為「只寫檔、驗證交給主 session」 · 下一步：P2 effect 詞彙定案後由人工審查；數值由 SOL 做平衡模型
- 2026-11-06 · Gemini Flash（agy，Claude Opus 驗收）· partial · P5-05 特性草稿 batch 01：`Data/_incoming/p5-05-traits-draft-01/`，30 個特性，每個屬性 3 個；只使用暫定詞彙（7 種觸發、7 種效果，皆為 `{type, params}` 殼，P2-09 收斂 enum 時對應）。每種觸發至少 3 次（always 5、on_hit_taken 5，其餘各 4），11 個帶副作用（side-grade），每個最多 2 個效果，`always` 的幅度較低。Opus 以腳本重驗：schema、`validate-data --root` 0 錯誤、參數範圍、id 不與既有資料衝突、無浮點字面值。詞彙刻意不含威力/STR 類效果（Q-C2）。README 的待決問題：疊加/重觸發語意、門檻觸發的內部冷卻、副手提供特性時的繼承 · 下一步：P2-09 定案 enum 後由 SONNET 對應詞彙並審查；數值交 SOL 平衡模型
