---
paths:
  - "docs/**"
  - "CLAUDE.md"
  - "AGENTS.md"
---
# 文件與進度規則（漸進式揭露）

## 分層
- 入口檔（`CLAUDE.md`、`AGENTS.md`、`docs/roadmap/README.md`）只放索引與觸發條件（「何時讀哪份」），各 < 80 行。細節寫在下一層並以連結指向。
- 單一文件 > 200 行時拆分；一個概念只在一處定義，其他地方連結過去，不複製。
- 只在特定情境才需要的內容（證據、歷史、推導）放第三層：`docs/research/`、`docs/reviews/`、`docs/roadmap/log/`。

## 進度（流程見 `AGENTS.md` §3；每個事實只寫一處）
| 寫什麼 | 寫在哪 |
|---|---|
| 「現在」的下一步 | `docs/roadmap/README.md`（只寫未來，完成即改寫） |
| 項目定義與狀態欄 | `docs/roadmap/phase-N.md`、`demo.md` 的項目表 |
| 做了什麼、驗證數字 | commit message（`git log -- <路徑>` 即歷史） |
| 未收尾的工作（partial／blocked） | `docs/roadmap/log/<同名>.md` 一行，只追加 |
| 延後的審查項目 | `docs/tech-debt.md` |

- 進度不寫進 `CLAUDE.md`／`AGENTS.md`。
- `log/` 既有內容為封存，不改寫、不必讀。

## 決策
- `docs/design/` 只寫現行規格（不寫誰在何時決定）；其內容皆已定案，要改先問使用者。
- 設計決定一旦定案：從 `docs/open-questions.md` 移除，寫進對應設計文件，必要時新增 ADR。
- ADR 格式：`docs/decisions/ADR-XXXX-<slug>.md`，章節：狀態 / 背景 / 決定 / 後果 / 替代方案。
