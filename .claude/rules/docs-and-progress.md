---
paths:
  - "docs/**"
  - "CLAUDE.md"
  - "AGENTS.md"
---
# 文件與進度規則（漸進式揭露）

- 入口檔（`CLAUDE.md`、`AGENTS.md`、`docs/roadmap/README.md`）只放索引與觸發條件，各 < 150 行。細節寫在下一層文件並以連結指向。
- 單一文件 > 200 行時拆分；一個概念只在一處定義，其他地方連結過去。
- 進度只寫在 `docs/roadmap/phase-N.md` 的項目表與交接紀錄；不寫進 CLAUDE.md。
- 設計決定一旦定案：從 `docs/open-questions.md` 移除，寫進對應設計文件，必要時新增 ADR。
- ADR 格式：`docs/decisions/ADR-XXXX-<slug>.md`，章節：狀態 / 背景 / 決定 / 後果 / 替代方案。
