# P4 Meta 循環

**出口條件**：地球大廳 → 星海選星 → 遊玩 → 收服/得蛋 → 返回大廳孵化/進化/編隊 → 再出發，完整循環並可存讀檔。
讀：`../design/lobby-meta.md`、`../design/creatures.md`
**開工前需定案**：Q-M4（Q-M3 已定案）

| ID | 項目 | 主責 | 審查 | 驗收條件 | 狀態 |
|---|---|---|---|---|---|
| P4-01 | 地球大廳場景（觀測站、夜空） | SONNET | 人工 | 沉浸感審查通過 | 🟦 |
| P4-02 | 星海選星 UI：星座=類型、預覽資訊、seed（Q-M1） | SONNET | 人工 | 選星到載入 < 15 s | 🟦 |
| P4-03 | 收服流程與蛋取得 | SONNET | OPUS | 任務完成/BOSS 擊敗皆觸發 | ✅ |
| P4-04 | 圖鑑、主動技表、被動技表、關係表（關係網視覺化） | SONNET | 人工 | 全部由 Data 自動生成 | ✅ |
| P4-05 | 孵蛋所（依 Q-M3 定案規則） | SONNET | SOL（存檔 v6） | 孵化結果依資料表 | 🟨 |
| P4-06 | 存檔：格式、版本號、遷移（依 Q-M4） | OPUS | SONNET | 舊版存檔可遷移；ID 退役不壞檔 | 🟦 |
| P4-07 | 隊伍編成 UI：6 主力、4 輪替、副手配對提示 | SONNET | 人工 | 不合法配對有清楚理由 | 🟨 |
| P4-08 | UI 文案與本地化（繁中/英/日） | TERRA | HYDROGEN（交叉）+ 人工 | 字串全部外部化 | 🟦 |
| P4-09 | 角色／宙獸／技能圖鑑內容與分階解鎖 | SOL | 人工 | 與 Forge 共用結構、Drive 來源可追溯，遭遇／對話／目擊與捕獲／任務／取得分階解鎖 | 🟨 |
| P4-10 | 旅人職業圖鑑與試煉進度存檔（[trainer-battles.md](../design/trainer-battles.md)#旅人圖鑑）：`LobbySave` v15 每職業對戰計數（首戰 basic、累積 10 次 complete）、`completed_trials` 試煉章；結算寫回（放棄 run 不計）、v1–v14 舊存檔遷移與 `Clone()` 修復 | SONNET | SOL（存檔格式必審） | 計數跨 run 累積；放棄 run 不計；遷移測試 | 🟨 |

## 未收尾紀錄
[log/phase-4.md](log/phase-4.md)（partial／blocked 才寫；舊紀錄為封存）。
