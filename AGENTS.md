# AGENTS.md — 給所有 AI agent（ChatGPT / Codex、Gemini、Claude 皆適用）

本專案：**宙獸紀 · COSMIMERA**，Unity 6 LTS + URP + C#。專案概要與文件索引在 `CLAUDE.md`，本檔只放「跨工具通用」的工作協定。

## 1. 開工前必讀（依序，只讀需要的）
1. `CLAUDE.md` — 專案概要、架構邊界、硬性規則、文件索引
2. `docs/roadmap/README.md` — 找到你被指派的項目 ID（例：`P1-03`），再讀該 `phase-N.md`
3. `docs/ai-agents.md` — 確認該項目由你負責或審查，以及交接格式
4. `.claude/rules/*.md` — **非 Claude 工具不會自動載入，必須手動讀**：
   - `code-contracts.md`（永遠適用）
   - `unity-csharp.md`（改 `Assets/**/*.cs` 時）
   - `data-content.md`（改 `Data/**` 時）
   - `testing.md`（寫或改測試時）
   - `docs-and-progress.md`（改 `docs/**` 時）

## 2. 工作邊界
- 只改你的項目範圍內的檔案；不順手重構、不清理無關程式碼，diff 越小越好。
- 不覆寫其他 agent 標記為 `🔒 <agent>` 的 roadmap 項目。
- 遇到註解標 `// FENCE:` 的程式碼：不得修改，除非任務明確要求（見 code-contracts.md）。
- 需求不明：寫入 `docs/open-questions.md` 並停止，不要猜。

## 3. 交接（每次結束工作都要做）
在對應 `phase-N.md` 的項目下方追加一行：
```
- YYYY-MM-DD · <agent/model> · <done|partial|blocked> · <一句話結果> · 下一步：<誰/做什麼>
```
並把狀態欄更新為 `⬜ 未開始 / 🟦 進行中 / 🟨 待審 / ✅ 完成 / ⛔ 阻塞`。

## 4. 輸出格式偏好
- 程式碼優先於說明；只解釋非顯而易見的決策（為何，而非做什麼）。
- 公開 API 一律附 DbC 契約註解（格式見 code-contracts.md）。
- 資料產出（宙獸、招式、特性）必須是符合 `Data/schemas/*.schema.json` 的 JSON，不要輸出散文表格。
