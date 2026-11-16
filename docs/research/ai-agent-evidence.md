# AI 模型選型依據

`docs/ai-agents.md` 角色表與派工矩陣的證據。只在換模型版本或質疑派工時讀。

## 2026-11-06 查詢
分數多為廠商自報、各來源數字不一，只用來排**相對**層級；換版時重查。
- **Opus vs Sonnet 5.5**：SWE-Bench Pro 89.9% vs 81.3%、FrontierCode 54.4% vs 46.2%；Terminal-Bench 4.0 反而 Sonnet 較高（70.6% vs 66.4%）。CodeRabbit 實測 13 個已知問題 Opus 抓到 8–10、Sonnet 6 → Sonnet 適合例行審查，高風險留 Opus。
- **Sol / Terra / Luna（GPT-5.6）**：Terminal-Bench 2.1 88.8 / 87.4 / 84.7%；長上下文回想 MRCR 91.5 / 89.6 / **41.3%**；SWE-Bench Pro Sol 64.6%（低於 Claude）。社群結論「Sol 強在 agentic，不是寫碼」，Terra 是預設。GPT-6.1 Sol 獨立排行 Terminal-Bench 4.0 約 58%（Opus 5.5 64.8%）。
- **Gemini 4 Argon**：Google 定位為複雜軟工/除錯/遷移/資安的前沿模型（內部回報 rollback −30%）；但 Terminal-Bench 4.0 57%，低於 Opus/Sonnet 5.5，評論標題「贏 12/18 項，寫碼不是其中之一」。分數為 Google 自算。
- **Gemini 3.8 Flash**：DeepSWE ~74%、飽和的 Terminal-Bench 2.1 89%，但 Terminal-Bench 4.0 僅 ~19%；AA-Omniscience 不幻覺率約 45%；評論一致指出「品質依賴 harness」。本專案 agy headless 不能跑 shell（見 phase-5 P5-05 交接）。
- **agy 內的 Claude Sonnet**：Antigravity 付費方案提供 Claude Sonnet/Opus 5.5；可用模型以 `agy models` 為準。
- **agy headless 實務（2026-11-06）**：`--mode accept-edits` 單獨使用時，模型一嘗試 shell 就整個回合中止（"no output produced"）；加 `--settings '{"permissions":{"deny":["command(*)"]}}'` 讓 shell 被明確拒絕，模型會改用讀寫檔工具繼續。個人額度約可支撐兩個中型唯寫檔任務並行跑到一半，派工時把任務切小、讓每個段落都能獨立驗收。
- **agy headless 自行驗收（2026-11-11）**：`--settings '{"permissions":{"allow":["command(<完整指令>)"]}}'` 只做**逐字完全比對**（`command(python*)` 無效）；`deny` 優先於 `allow`（`deny command(*)` 會連允許的也擋）；`--mode auto` 本帳號不可用；`--sandbox` 不放行。做法：prompt 開頭明令「除列出的驗證指令外不准呼叫 shell，用讀寫檔工具」，並把驗證指令逐字列入 allow。P3-12a（FLASH）以此方式自跑 unittest／validate-data 通過。

## 2026-11-16 查詢：Gemini 4 Hydrogen（取代 FLASH）
- **官方**：GA 2026-11-12，`gemini-4-hydrogen`；定位「日常寫碼與 agentic 工作流」；1M token 統一上下文；思考層級 LOW/MEDIUM/HIGH(預設)/XHIGH，`MINIMAL` 與 temperature 等取樣參數已移除，格式用 `response_schema` 控制。從 3.8 Flash 遷移：預設思考由 MEDIUM 改 HIGH、不支援顯式 context cache。官方稱捏造率比 3.8 Flash 降 5 倍以上，但無端拒答率 +5.6 個百分點。agy 已列 `gemini-4-hydrogen-{low,medium,high,xhigh}`（2026-11-16 `agy models` 實查）。
- **官方 model card 分數**：Terminal-Bench 4.1 57.0%（Sonnet 5.5 63.9%）、OSWorld-2.1 77.6%（Sonnet 83.9%）、AutomationBench-AA 76.1%（Sonnet 71.8%、Terra 60.2%）。
- **獨立評測**：Bito 實際工作 session（Claude Code harness）總分 Opus 86.9 / Carbon 80.7 / **Hydrogen 77.6** / Sol 77.5 / Sonnet 72.3；Hydrogen 每 session $4.37（Opus $18.38），速度比 Opus 快 65%；Gemini CLI harness 下降到 66.1%（harness 敏感）。弱點：長 session 的需求變更、忽略既有系統行為、全 4 次對同一 Redis 行為做出自信錯誤結論；誤導性陳述 0.8/答（Opus 0.3、Sonnet 1.1）。Artificial Analysis 指數 Hydrogen 51 / $1.87 每任務、Terra 51 / $0.96、Sonnet 55 / $10.02、3.8 Flash 31 / $7.77。
- **使用者評價**：MindStudio 實測除錯能定位根因、多語命名（82 語）品質好，但中日名稱相似度偏高；eesel 評 4/5，「Hydrogen 是工人不是規劃者」，最便宜批次處理推 Terra；早期使用者回報會做超過要求。HN/Reddit 討論多為存取與方案限制（部分 Pro 帳號顯示 Unavailable、Orchestrator 預設 turbo 權限），尚無系統性幻覺回報。
- **結論**：FLASH 全面退役；HYDROGEN 接手批次初稿、多語翻譯、非核心唯寫檔實作與除錯初查；純成本導向的批次以 TERRA 優先。

來源：[Google Cloud: Gemini 4 Hydrogen](https://docs.cloud.google.com/gemini-enterprise-agent-platform/models/gemini/4-hydrogen) · [Developer's guide](https://docs.cloud.google.com/gemini-enterprise-agent-platform/models/guides/gemini-4-hydrogen) · [DeepMind model card](https://deepmind.google/models/model-cards/gemini-4-hydrogen/) · [Google blog](https://blog.google/innovation-and-ai/technology/developers-tools/build-with-gemini-4-hydrogen/) · [Bito benchmark](https://bito.ai/blog/gemini-4-hydrogen-vs-claude-opus-sonnet-haiku-sol/) · [eesel review](https://www.eesel.ai/blog/gemini-4-hydrogen-review) · [MindStudio review](https://www.mindstudio.ai/blog/gemini-4-hydrogen-hands-on-review) · [HN](https://news.ycombinator.com/item?id=50471431) · [SmartScope: Unavailable 回報](https://smartscope.blog/en/blog/antigravity-gemini-4-hydrogen-showing-unavailable-jio-google-2026/)

2026-11-06 來源：[OpenAI GPT-5.6](https://openai.com/index/gpt-5-6/) · [Vellum: Sol vs Terra vs Luna](https://www.vellum.ai/blog/gpt-5-6-sol-terra-luna-explained) · [Codex GPT-6.1 Sol tracker](https://marginlab.ai/trackers/codex/) · [Antigravity: Gemini 4 Argon](https://antigravity.google/blog/gemini-4-argon-in-google-antigravity/) · [DEV: Argon wins 12 of 18](https://dev.to/max_quimby/gemini-4-argon-wins-12-of-18-benchmarks-code-isnt-one-2h70) · [Vellum: Gemini 3.8 Flash](https://www.vellum.ai/blog/gemini-3-8-flash-benchmarks-explained) · [MindStudio: Flash harness-dependent](https://www.mindstudio.ai/blog/gemini-3-8-flash-harness-demos) · [CodeRabbit: Sonnet 5.5 vs Opus 5.5](https://www.coderabbit.ai/blog/sonnet-5-5-model-review) · [Antigravity CLI models](https://www.codeagentswarm.com/en/guides/how-to-use-antigravity-cli)
