# AGENTS.md — 給所有 AI agent（Codex / Gemini / Claude 通用）

專案概要、指令、硬性規則與文件索引在 `CLAUDE.md`；本檔只放跨工具的工作協定。

## 1. 開工（只讀需要的）
1. `CLAUDE.md`
2. `docs/roadmap/README.md` 的「現在」→ 被指派項目所在的 `phase-N.md`／`demo.md` 那一列。歷史只在卡住時查 `git log -- <路徑>`。
3. `.claude/rules/` — Claude 依路徑自動載入；**其他工具依改動路徑手動讀**：
   `Assets/**/*.cs` → `code-contracts.md`、`unity-csharp.md`、`testing.md` · `Data/**`、`tools/**` → `data-content.md` · `docs/**` → `docs-and-progress.md`

派工／審查分工（`docs/ai-agents.md`）只在**你要派工或被指定審查**時讀。

## 2. 工作邊界
- 只改項目範圍內的檔案；diff 越小越好，不順手重構。
- 同一 feature 同時只有一個寫入者；不動標記 `🔒 <agent>` 的項目。
- 需求不明：查 `docs/open-questions.md`。有「暫定」且只影響 `Data/` 數值 → 照暫定做並在 commit 註明；否則新增問題、**只停該項目**，改做下一個不受影響的項目。

## 3. 完成定義與交接
完成 = 相關測試全綠（EditMode；動到場景／物理加 PlayMode；動到 `Data/` 加 `tools/validate-data`）。然後：
1. **commit message 就是交接**：做了什麼、為何、驗證數字、未做／下一步。
2. 項目表狀態欄改 `✅`；屬於 `ai-agents.md`「必審」類別才改 `🟨`。🟨 不阻擋下游項目開工。
3. 「下一步」改變時才改寫 `docs/roadmap/README.md`「現在」。
4. 只有 `partial`／`blocked`（工作沒收尾、commit 說不清狀態）才在 `docs/roadmap/log/<phase-N|demo>.md` 追加一行：
   `- YYYY-MM-DD · <agent> · <partial|blocked> · <卡在哪> · 下一步：<誰/做什麼>`

## 4. 輸出
- 程式碼優先，只解釋非顯而易見的決策。
- 資料產出必須是符合 `Data/schemas/*.schema.json` 的 JSON，不輸出散文表格。
