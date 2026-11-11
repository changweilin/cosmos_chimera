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

來源：[OpenAI GPT-5.6](https://openai.com/index/gpt-5-6/) · [Vellum: Sol vs Terra vs Luna](https://www.vellum.ai/blog/gpt-5-6-sol-terra-luna-explained) · [Codex GPT-6.1 Sol tracker](https://marginlab.ai/trackers/codex/) · [Antigravity: Gemini 4 Argon](https://antigravity.google/blog/gemini-4-argon-in-google-antigravity/) · [DEV: Argon wins 12 of 18](https://dev.to/max_quimby/gemini-4-argon-wins-12-of-18-benchmarks-code-isnt-one-2h70) · [Vellum: Gemini 3.8 Flash](https://www.vellum.ai/blog/gemini-3-8-flash-benchmarks-explained) · [MindStudio: Flash harness-dependent](https://www.mindstudio.ai/blog/gemini-3-8-flash-harness-demos) · [CodeRabbit: Sonnet 5.5 vs Opus 5.5](https://www.coderabbit.ai/blog/sonnet-5-5-model-review) · [Antigravity CLI models](https://www.codeagentswarm.com/en/guides/how-to-use-antigravity-cli)
