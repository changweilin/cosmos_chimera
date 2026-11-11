# AI Agent 分工

## 角色表
> 2026-11-06 依官方發表、獨立評測與社群評論訂定（證據：`research/ai-agent-evidence.md`）。原 Q-T2 的層級假設已以此取代。
> 模型版本會更新（例：GPT-5.6 → 6.1、Gemini 3.8 Flash），roadmap 只引用**代號**；換版時只改本表與「依據」。

| 代號 | 模型（2026-11 版本） | 實測層級 | 擅長派工 | 不派 |
|---|---|---|---|---|
| **OPUS** | Claude Opus 5.5 | 最高：repo 級程式與審查 | 架構、ADR、契約設計、球面數學、網路同步、生成演算法核心、**最終程式審查**、高風險變更 | 大量重複資料填充 |
| **SONNET** | Claude Sonnet 5.5 | 主力實作（Opus 的 ~1/2 價） | C# 功能實作、測試、importer/工具、重構、文件維護、資料一致性檢查、例行 PR 審查、終端機/建置腳本 | 未定案的架構決策、高風險變更的唯一審查者 |
| **SONNET-AGY** | Claude Sonnet（經 agy CLI） | 同 SONNET，但不同 harness | 與 SONNET 相同類型的**唯寫檔**工作（在 Claude 額度不足時分流） | 需要執行 shell / Unity 的任務（headless 不能跑指令）；**不算不同模型的審查者** |
| **SOL** | ChatGPT Sol（GPT-5.6 / 6.1） | 高推理，agentic 最強 | 數值平衡模型、演算法交叉驗證、Opus 方案的對抗式審查、資安/權限稽核、長時程終端機任務 | 直接改核心程式碼 |
| **TERRA** | ChatGPT Terra | 日常主力（距 Sol 2–3 分、價格一半以下） | 命名、描述文案、本地化草稿、概念圖 prompt、批次 JSON 初稿、文件摘要、**資料批次的第二審**（與 FLASH 不同源） | 數值定案 |
| **LUNA** | ChatGPT Luna | 最便宜、最快 | 短上下文的大量分類/標記、格式轉換、單檔 JSON 修格式、翻譯初稿 | 長文件（長上下文回想 41%）、任何推理或審查 |
| **ARGON** | Gemini 4 Argon（agy） | 前沿，長上下文/除錯強，寫碼不及 Opus | 文化原型考據、整份資料集一致性掃描、大型規格閱讀摘要、效能剖析報告的除錯分析、**第三方審查**（安全/遷移/重現性） | 直接寫入核心程式碼 |
| **FLASH** | Gemini Flash（3.8，agy） | 低成本大量，需要 harness 與驗證 | 宙獸/招式/特性 JSON 大量初稿（在 schema 約束下）、格式轉換、翻譯、資料清洗 | 任何需要判斷的審核、長時程 agentic 任務、未經查證的事實考據 |

## 審查政策（省 token、不阻塞）
P1 曾跑到 17+ 輪複審，成本遠高於它抓到的問題；因此：
- **必審**（完成後標 🟨）：ADR／Api 契約、球面數學、生成重現性、netcode、存檔格式／遷移、資安、數值定案。其餘項目測試全綠即 ✅，審查欄僅表示「有空時誰可抽查」。
- **一輪為原則**：只有 high 阻擋；med／low 進 `tech-debt.md`。修完 high 後的複核只看該 diff，最多再一輪。
- 突變測試只用於重現性與 netcode。
- 🟨 不阻擋下游項目開工；審查與實作平行。

## 任務類型 → 派工矩陣
主責 = 寫；必審類別的審查者必須與主責**不同廠商**（同廠不同層級不算）。

| 任務類型 | 主責 | 審查 | 備註 |
|---|---|---|---|
| 架構 / ADR / Api 契約 | OPUS | SOL 或 ARGON | 人工定案 |
| 核心程式（`Core/`、`Planet/`、`Combat/Runtime/Model`、`Net/`） | SONNET（難題 OPUS） | OPUS；高風險加 SOL | 只有 Claude 直接寫入 |
| 非核心功能、工具、importer、測試 | SONNET / SONNET-AGY | OPUS 或 SOL | SONNET-AGY 只寫檔，驗證回主 session |
| 球面數學、決定性/重現性、netcode | OPUS | SOL（對抗式）＋ ARGON（跨後端） | |
| 數值平衡模型 | SOL | OPUS | 結果寫成 `Data/` 提案，人工定案 |
| 效能剖析、疑難除錯分析 | OPUS / SONNET | ARGON 第二意見 | ARGON 只出報告，不改碼 |
| 資安 / 權限 / 存檔竄改稽核 | SOL | OPUS | |
| 資料 JSON 大量初稿 | FLASH（量大）/ TERRA（需要文筆） | `validate-data` → TERRA 或 SONNET 一致性 → 人工 | 初稿一律進 `Data/_incoming/` |
| 文案、命名、本地化 | TERRA | SONNET（術語一致）→ 人工 | |
| 大量分類 / 標記 / 格式轉換 | LUNA 或 FLASH | 腳本驗證 | 單筆輸入保持短 |
| 文化考據、外部研究 | ARGON | OPUS 決策；事實須附來源 | FLASH 不做事實考據（幻覺率高） |
| 整份資料集 / 長文件掃描 | ARGON（或 SOL/TERRA） | SONNET 抽查 | 不派 LUNA、FLASH |
| 美術 prompt、概念圖 | TERRA（圖像）/ ARGON | 人工 | 見 Q-T3 |

## 派工原則
1. **一個項目一個主責**；必審項目的審查者必須是不同廠商的模型（降低同源盲點）。SONNET-AGY 與 SONNET/OPUS 同源。
2. 核心程式碼只由 OPUS/SONNET 寫入；其他模型產出以「提案/patch」形式交 SONNET 合併。
3. 資料批次：FLASH/TERRA/LUNA 產初稿 → `tools/validate-data` → SONNET 一致性 → 人工定稿。
4. 一切涉及「定案」（ADR、數值、文化敏感）都要人工最後確認。
5. 便宜模型的品質高度依賴 harness：FLASH/LUNA 一律給 schema、範例 fixture、明確數量與分佈目標，產出後以腳本重驗（P5-05 的做法）。

## 標準流程
```
設計/契約      OPUS ──▶ ADR / Api 介面 + DbC
實作           SONNET（或 SONNET-AGY 唯寫檔）──▶ 程式 + 測試
審查           OPUS（核心）· SOL（演算法/數值/資安）· ARGON（除錯/長文件/重現性）
資料           FLASH/TERRA/LUNA 初稿 → validate-data → SONNET 驗證 → 人工
研究           ARGON 摘要（附來源）→ OPUS 決策
```

## 依據
換版或質疑層級時才讀：`research/ai-agent-evidence.md`（2026-11-06 查詢；含 agy headless 實務）。

## 交接格式
見 `AGENTS.md` §3（commit message 即交接）。派工給非 Claude 工具時，prompt 第一句要求它先讀 `AGENTS.md`。

## Claude Code 執行建議
- 長任務用 `docs/roadmap/phase-N.md` 項目 ID 開新 session，只載入該項目需要的文件（遵循 CLAUDE.md 索引）。
- 平行工作：不同 feature 的項目可各開 session（各自 worktree），同一 feature 一次只有一個寫入者。
