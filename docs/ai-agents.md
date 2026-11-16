# AI Agent 分工

## 角色表
> 層級依據：`research/ai-agent-evidence.md`。
> 模型版本會更新（例：GPT-5.6 → 6.1、Gemini 4 Hydrogen → 下一版），roadmap 只引用**代號**；換版時只改本表與「依據」。
> 成本欄為相對值（Artificial Analysis 每任務成本，2026-11）：$ ≈ <$2、$$ ≈ $2–8、$$$ ≈ >$8。

| 代號 | 模型（2026-11 版本） | 成本 | 實測層級 | 擅長 | 不派 |
|---|---|---|---|---|---|
| **OPUS** | Claude Opus 5.5 | $$$ | 最高：repo 級程式與審查、長 session 最穩 | 架構、ADR、契約設計、球面數學、網路同步、生成演算法核心、**最終程式審查**、高風險變更 | 大量重複資料填充 |
| **SONNET** | Claude Sonnet 5.5 | $$$ | 主力實作；終端機/電腦操作強於 HYDROGEN | C# 功能實作、測試、importer/工具、需要跑 shell/Unity 的任務、文件維護、例行 PR 審查 | 未定案的架構決策、高風險變更的唯一審查者 |
| **SONNET-AGY** | Claude Sonnet（經 agy CLI） | 同 SONNET（走 agy 額度） | 同 SONNET，但 headless 只能跑 allow 清單內指令 | 與 SONNET 同類型的**唯寫檔**工作（Claude 額度不足時分流） | 需要自由執行 shell / Unity 的任務；**不算不同模型的審查者** |
| **SOL** | ChatGPT Sol（GPT-6.1） | $$ | 高推理，agentic 長時程強 | 數值平衡模型、演算法交叉驗證、對抗式審查、資安/權限稽核、長時程終端機任務 | 直接改核心程式碼 |
| **TERRA** | ChatGPT Terra（GPT-6.1） | $（最省） | 日常主力；與 HYDROGEN 同分、每任務成本約一半 | 命名、描述文案、本地化、概念圖 prompt、**批次 JSON 初稿**、文件摘要、資料批次第二審 | 數值定案 |
| **LUNA** | ChatGPT Luna | $ | 最便宜最快；長上下文回想差 | 短上下文的大量分類/標記、格式轉換、單檔 JSON 修格式 | 長文件、任何推理或審查 |
| **ARGON** | Gemini 4 Argon（agy） | $$ | 前沿，長上下文/除錯/研究強，寫碼不及 Opus | 文化原型考據、整份資料集一致性掃描、大型規格摘要、除錯分析、**第三方審查**（安全/遷移/重現性） | 直接寫入核心程式碼 |
| **HYDROGEN** | Gemini 4 Hydrogen（agy，`gemini-4-hydrogen-{low…xhigh}`） | $ | 日常寫碼/子代理、快（約 Opus 的 1/3 時間）；多語生成好；長 session 與系統行為推論弱於 OPUS | 非核心程式的唯寫檔實作與除錯初查、批次 JSON 初稿、ja/多語翻譯、文件與 PDF 讀取、SONNET 實作的**跨廠商第二意見**（非必審） | 核心程式直接寫入、必審類的唯一審查者、最難推理（交 OPUS/ARGON） |

**已退役：FLASH（Gemini 3.8 Flash）** — 2026-11-16 起不再派工；同價位由 HYDROGEN 取代（每任務成本更低、捏造率官方稱降 5 倍以上）。`Data/_incoming/*flash*` 為歷史草稿，不需重做。

## 審查政策（省 token、不阻塞）
- **必審**（完成後標 🟨）：ADR／Api 契約、球面數學、生成重現性、netcode、存檔格式／遷移、資安、數值定案。其餘項目測試全綠即 ✅，審查欄僅表示「有空時誰可抽查」。
- **一輪為原則**：只有 high 阻擋；med／low 進 `tech-debt.md`。修完 high 後的複核只看該 diff，最多再一輪。
- 突變測試只用於重現性與 netcode。
- 🟨 不阻擋下游項目開工；審查與實作平行。

## 任務類型 → 派工矩陣
**一個任務可以由多個模型分段完成**（例：OPUS 定契約 → HYDROGEN 寫唯寫檔草稿 → SONNET 跑測試合併）。「可用」欄依**效率排序**：能力足夠時選最前面的；卡住或品質不足再往後換。必審類別的審查者必須與該段寫入者**不同廠商**（同廠不同層級不算）。

| 任務類型 | 可用（效率序） | 審查 | 備註 |
|---|---|---|---|
| 架構 / ADR / Api 契約 | OPUS | SOL 或 ARGON | 人工定案 |
| 核心程式（`Core/`、`Planet/`、`Combat/Runtime/Model`、`Net/`） | SONNET → OPUS（難題） | OPUS；高風險加 SOL | 只有 Claude 直接寫入；HYDROGEN/SOL 可出 patch 提案 |
| 非核心功能、工具、importer、測試 | HYDROGEN（唯寫檔，agy）/ SONNET-AGY → SONNET | OPUS 或 SOL | agy 段只寫檔；需要跑 Unity/測試的驗證回主 session（SONNET） |
| 球面數學、決定性/重現性、netcode | OPUS | SOL（對抗式）＋ ARGON（跨後端） | |
| 數值平衡模型 | SOL | OPUS | 結果寫成 `Data/` 提案，人工定案 |
| 效能剖析、疑難除錯分析 | HYDROGEN（初查）→ SONNET / OPUS | ARGON 第二意見 | 初查只出報告；改碼回 Claude |
| 資安 / 權限 / 存檔竄改稽核 | SOL | OPUS | |
| 資料 JSON 大量初稿 | TERRA / HYDROGEN（並行切批）| `validate-data` + `check_batch` → 另一家模型一致性（SONNET 或 TERRA/HYDROGEN 互審）→ 人工 | 初稿一律進 `Data/_incoming/` |
| 文案、命名 | TERRA | SONNET（術語一致）→ 人工 | |
| 本地化（ja 等多語） | HYDROGEN / TERRA | 另一家交叉 → 人工 | 譯者與交叉審不同廠商 |
| 大量分類 / 標記 / 格式轉換 | LUNA → HYDROGEN（`low`） | 腳本驗證 | 單筆輸入保持短 |
| 文化考據、外部研究 | ARGON | OPUS 決策；事實須附來源 | HYDROGEN 只做附來源的摘要，不做定論 |
| 整份資料集 / 長文件掃描 | ARGON → SOL/TERRA | SONNET 抽查 | 不派 LUNA |
| 美術 prompt、概念圖 | TERRA（圖像）/ ARGON | 人工 | 3D 工具選型已移交其他專案 |

## 派工原則
1. **能力足夠 + 效率**：先選矩陣中最前面、能勝任的模型；同一任務可拆段交給不同模型，每段都要能獨立驗收（測試、腳本或 diff）。
2. 必審項目的審查者必須是不同廠商的模型（降低同源盲點）。SONNET-AGY 與 SONNET/OPUS 同源；HYDROGEN 與 ARGON 同源。
3. 核心程式碼只由 OPUS/SONNET 寫入；其他模型產出以「提案/patch」形式交 SONNET 合併。
4. 資料批次：TERRA/HYDROGEN/LUNA 產初稿 → `tools/validate-data` → 不同廠商一致性審 → 人工定稿。
5. 一切涉及「定案」（ADR、數值、文化敏感）都要人工最後確認。
6. 便宜模型的品質依賴 harness：一律給 schema、範例 fixture、明確數量與分佈目標，產出後以腳本重驗（P5-05 的做法）。HYDROGEN 有「做超過要求」傾向，prompt 明列允許修改的檔案。
7. HYDROGEN 思考層級：分類/格式 `low`；翻譯、文案、一般寫碼 `medium`；批次 JSON、除錯、多檔實作 `high`；`xhigh` 很少需要（改派 OPUS/ARGON 通常更划算）。

## 標準流程
```
設計/契約      OPUS ──▶ ADR / Api 介面 + DbC
實作           SONNET（核心）· HYDROGEN / SONNET-AGY（非核心唯寫檔）──▶ SONNET 跑測試合併
審查           OPUS（核心）· SOL（演算法/數值/資安）· ARGON（除錯/長文件/重現性）
資料           TERRA/HYDROGEN/LUNA 初稿 → validate-data → 不同廠商一致性 → 人工
研究           ARGON 摘要（附來源）→ OPUS 決策
```

## 依據
換版或質疑層級時才讀：`research/ai-agent-evidence.md`（2026-11-06、11-16 查詢；含 agy headless 實務）。

## 交接格式
見 `AGENTS.md` §3（commit message 即交接）。派工給非 Claude 工具時，prompt 第一句要求它先讀 `AGENTS.md`。

## Claude Code 執行建議
- 長任務用 `docs/roadmap/phase-N.md` 項目 ID 開新 session，只載入該項目需要的文件（遵循 CLAUDE.md 索引）。
- 平行工作：不同 feature 的項目可各開 session（各自 worktree），同一 feature 一次只有一個寫入者。
