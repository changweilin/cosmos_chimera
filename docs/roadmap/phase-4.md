# P4 Meta 循環

**出口條件**：地球大廳 → 星海選星 → 遊玩 → 收服/得蛋 → 返回大廳孵化/進化/編隊 → 再出發，完整循環並可存讀檔。
讀：`../design/lobby-meta.md`、`../design/creatures.md`
**開工前需定案**：Q-M1、Q-M3、Q-M4

| ID | 項目 | 主責 | 審查 | 驗收條件 | 狀態 |
|---|---|---|---|---|---|
| P4-01 | 地球大廳場景（觀測站、夜空） | SONNET | 人工 | 沉浸感審查通過 | 🟦 |
| P4-02 | 星海選星 UI：星座=類型、預覽資訊、seed（Q-M1） | SONNET | 人工 | 選星到載入 < 15 s | 🟦 |
| P4-03 | 收服流程與蛋取得 | SONNET | OPUS | 任務完成/BOSS 擊敗皆觸發 | 🟨 |
| P4-04 | 圖鑑、招式表、特性表、關係表（關係網視覺化） | SONNET | 人工 | 全部由 Data 自動生成 | ⬜ |
| P4-05 | 孵蛋所（依 Q-M3） | SONNET | — | 孵化結果依資料表 | ⬜ |
| P4-06 | 存檔：格式、版本號、遷移（依 Q-M4） | OPUS | SONNET | 舊版存檔可遷移；ID 退役不壞檔 | 🟦 |
| P4-07 | 隊伍編成 UI：6 主力、4 輪替、副手配對提示 | SONNET | 人工 | 不合法配對有清楚理由 | 🟦 |
| P4-08 | UI 文案與本地化（繁中/英/日） | TERRA | FLASH（交叉）+ 人工 | 字串全部外部化 | 🟦 |

## 交接紀錄
- 2026-11-09 · ChatGPT Terra（Codex） · partial · P4-08：新增手把操作及 binding 提示，三語 163 欄結構／數字／佔位符檢查通過，繁中／英文已 staging；日文仍未接執行期 · 下一步：FLASH 交叉審核日文；人工驗收按鈕提示與排版
- 2026-11-09 · ChatGPT Terra（Codex） · partial · P4-08 新增九個 demo 操作／導航／收服鍵並修正前 2／4／6 隊伍提示，繁中／英文已接線、日文初稿同步；驗收腳本納入 supplement 與跨語佔位符，三語 160 欄通過。日文執行期與跨廠商審查尚未完成 · 下一步：FLASH 交叉審核本次日文；SONNET 後續接線日文，人工試玩
- 2026-11-08 · Claude Opus · partial · D0：`Bootstrap/Demo.unity`（`DemoGame`：大廳選御三家 → 三選一星球 → run → 結算）；P4-03 `Capture/Api/CaptureRules`（擊倒後投擲、每隻一次機會、bounty +10）；P4-06 `Save/Api/LobbySave`（Q-M4：只存大廳、原子寫入；Sol D0 審查 H1 SetParty 自我別名、H2 Release 剝除版本檢查，已修並補測試）。P4-07 隊伍編成 UI 未做（收服自動入隊至 6 隻） · 下一步：P4-07；人工試玩
- 2026-11-09 · ChatGPT Codex · partial · P4-08：依 D0 實際畫面補星球／屬性名稱與操作提示雙語草稿，記錄內部 ID、按鍵說明和日文接線缺口；尚未接入遊戲或翻譯日文 · 下一步：SONNET 接線、FLASH 翻譯及交叉審查、人工試玩
- 2026-11-09 · Claude Sonnet · partial · P4-07 最低限度：大廳 DemoGame.DrawPartyEditor 可調整隊伍順序（首位為出戰主力）、移出／加入待命區（1..6 隻，變更即寫存檔）；未做輪替／副手配對規則與提示；字串在 demo-ui-supplement.json（zh-TW 為 Sonnet 草稿）；未編譯、未測試（Editor 開啟中） · 下一步：人工在 Editor 編譯試玩；TERRA 審字串、FLASH 補日文
- 2026-11-09 · ChatGPT Sol（唯讀 D0-r2）→ ChatGPT Terra 修正 → Claude Opus 驗收 · partial · P4-07 七個隊伍編成 zh-TW 字串在 `a4554be` 寫成問號（Sol S2），Terra 已同步修正 supplement 與 `strings.json`；Flash 補 ja（`d0-copy-flash-ja/demo-ui-supplement.json`，check.py 6 檔 150 欄 PASS）。批次模式驗證：EditMode 862/862、PlayMode 62/62。報告 `docs/reviews/D0-r2-sol.md` · 下一步：人工試玩隊伍編成
