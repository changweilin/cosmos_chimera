# AGENTS.md — 給所有 AI agent（ChatGPT / Codex、Gemini、Claude 皆適用）

本專案：**宙獸紀 · COSMIMERA**，Unity 6 LTS + URP + C#。專案概要與文件索引在 `CLAUDE.md`，本檔只放「跨工具通用」的工作協定。

## 1. 開工前必讀（依序，只讀需要的）
1. `CLAUDE.md` — 專案概要、架構邊界、硬性規則、文件索引
2. `docs/roadmap/README.md` — 現在該做什麼；找到被指派的項目 ID（例：`P1-03`），再讀該 `phase-N.md`。需要歷史時才讀 `docs/roadmap/log/`
3. `docs/ai-agents.md` — 確認該項目由你負責或審查
4. `.claude/rules/*.md` — **非 Claude 工具不會自動載入，必須手動讀**：
   - `code-contracts.md`（永遠適用）
   - `unity-csharp.md`（改 `Assets/**/*.cs` 時）
   - `data-content.md`（改 `Data/**`、`tools/**` 時）
   - `testing.md`（寫或改測試時）
   - `docs-and-progress.md`（改 `docs/**` 或寫交接時）

## 2. 工作邊界
- 只改你的項目範圍內的檔案；不順手重構、不清理無關程式碼，diff 越小越好。
- 不覆寫其他 agent 標記為 `🔒 <agent>` 的 roadmap 項目。
- 需求不明：寫入 `docs/open-questions.md` 並停止，不要猜。
- 註解與架構三原則（細節與範例：`code-contracts.md`）：
  - **Design by Contract**：所有 public 介面寫明 Pre / Post / Side effects。
  - **Why, Not What**：行內註解只寫原因；禁止複述語法。
  - **Chesterton's Fence**：硬體、效能、引擎 bug、舊版相容的 workaround（`// FENCE:`）未經明確指示不得修改。

## 3. 交接（每次結束工作都要做）
1. 在 `docs/roadmap/log/<phase-N|demo>.md` 末尾追加一行（≤ 3 句；細節放 commit 或 `docs/reviews/`）：
   ```
   - YYYY-MM-DD · <agent/model> · <done|partial|blocked> · <結果> · 下一步：<誰/做什麼>
   ```
2. 把 `phase-N.md` 項目表的狀態欄更新為 `⬜ 未開始 / 🟦 進行中 / 🟨 待審 / ✅ 完成 / ⛔ 阻塞`。
3. 若改變了「下一步」，改寫 `docs/roadmap/README.md` 的「現在」一節。

## 4. 輸出格式偏好
- 程式碼優先於說明；只解釋非顯而易見的決策。
- 資料產出（宙獸、招式、特性）必須是符合 `Data/schemas/*.schema.json` 的 JSON，不要輸出散文表格。
