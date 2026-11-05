# AI Agent 分工

## 角色表
> ⚠️ 「Terra/Sol」「Argon」的能力層級是依名稱排列的**假設**（Q-T2）。若不符，只需修改本表的「層級」欄，roadmap 引用的是代號，不必逐項修改。

| 代號 | 模型 | 假設層級 | 擅長派工 | 不派 |
|---|---|---|---|---|
| **OPUS** | Claude Opus | 最高推理 | 架構、ADR、球面數學、網路同步、生成演算法核心、最終程式審查、契約設計 | 大量重複資料填充 |
| **SONNET** | Claude Sonnet | 主力實作 | C# 功能實作、測試、importer/工具、重構、文件維護、資料一致性檢查 | 未定案的架構決策 |
| **SOL** | ChatGPT Sol | 高推理（第二意見） | 數值平衡模型、演算法交叉驗證、Opus 方案的對抗式審查 | 直接改核心程式碼 |
| **TERRA** | ChatGPT Terra | 通用/快速 | 命名、描述文案、本地化草稿、概念圖 prompt、批次 JSON 初稿 | 數值定案 |
| **ARGON** | Gemini Argon | 長上下文/研究 | 文化原型考據、整份資料集一致性掃描、大型文件/規格閱讀摘要 | 寫遊戲程式碼 |
| **FLASH** | Gemini Flash | 低成本大量 | 宙獸/招式/特性 JSON 大量初稿、格式轉換、翻譯、資料清洗 | 任何需要判斷的審核 |

## 派工原則
1. **一個項目一個主責**；審查者必須是不同模型（降低同源盲點）。
2. 核心程式碼（`Core/`、`Planet/`、`Combat/Runtime/Model`、`Net/`）只由 OPUS/SONNET 寫入；其他模型產出以「提案/patch」形式交 SONNET 合併。
3. 資料批次：FLASH/TERRA 產初稿 → `tools/validate-data` → SONNET 一致性 → 人工定稿。
4. 一切涉及「定案」（ADR、數值、文化敏感）都要人工最後確認。

## 標準流程
```
設計/契約      OPUS ──▶ ADR / Api 介面 + DbC
實作           SONNET ──▶ 程式 + 測試
審查           OPUS（核心）或 SOL（演算法/數值）
資料           FLASH/TERRA 初稿 → SONNET 驗證 → 人工
研究           ARGON 摘要 → OPUS 決策
```

## 交接格式
見 `AGENTS.md` §3。非 Claude 工具務必先讀 `AGENTS.md`，因為它們不會自動載入 `.claude/rules/`。

## Claude Code 執行建議
- 長任務用 `docs/roadmap/phase-N.md` 項目 ID 開新 session，只載入該項目需要的文件（遵循 CLAUDE.md 索引）。
- 平行工作：不同 feature 的項目可各開 session（各自 worktree），同一 feature 一次只有一個寫入者。
