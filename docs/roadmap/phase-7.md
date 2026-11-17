# P7 打磨與發行

**出口條件**：目標硬體穩定 60 fps；新手引導完成；音效音樂到位；可安裝的發行建置。

| ID | 項目 | 主責 | 審查 | 驗收條件 | 狀態 |
|---|---|---|---|---|---|
| P7-01 | 效能剖析與最佳化（GPU/CPU/記憶體） | OPUS | SONNET | PC 最低規格 60 fps；Steam Deck 800p 穩定 30–40 fps；無 GC 尖峰 | ⬜ |
| P7-07 | Steam Deck Verified 檢查（手把、字級、預設檔、Proton 相容） | SONNET | 人工（實機） | 通過 Valve Deck 相容性檢查清單 | 🟦 |
| P7-02 | 新手任務（規格 [beginner-quest.md](../design/beginner-quest.md)；子項 P7-02a～i） | SONNET | 人工 | 新玩家 10 分鐘內理解召喚與輪替 | 🟦 |
| P7-02a | 新手存檔與流程狀態機：角色／好友／宿敵／三御三家／`tutorial_done`、0～7 階段、大廳鎖定、舊存檔遷移 | SONNET | SOL（存檔格式必審） | 新存檔進新手、舊存檔直入大廳；遷移測試 | ⬜ |
| P7-02b | 新手小行星生成：隨機 seed、固定小行星、排除進階地形地城與奇觀地城 | OPUS | SOL＋ARGON（重現性必審） | 同 seed 同結果；`ReproProbe` 覆蓋；無進階地形 POI | ⬜ |
| P7-02c | 選角畫面（三預設角色，背景為生成的星球；捏臉後置） | SONNET | — | 三語、實體手把可選 | ⬜ |
| P7-02d | 最低限度對話系統（對話框、說話者、文案 key、確認推進）＋世界觀佔位台詞 | HYDROGEN → SONNET | OPUS | 手把／鍵鼠推進；缺 key 時測試失敗 | ⬜ |
| P7-02e | 宿敵登場與御三家選擇：剋制分配、`vessel`＝主人類別幼年階 | SONNET | — | 三種選擇的分配表測試 | ⬜ |
| P7-02f | 初次對戰：訓練家所有宙獸（野生 AI、不可收服、不可脫戰）、勝敗皆續與全回復 | SONNET | OPUS（Combat 契約） | 勝／敗兩路 PlayMode 測試 | ⬜ |
| P7-02g | 好友引導任務鏈：交付器皿、收服、輪替教學（新步驟）、擊倒指定屬性、抵達巢穴、BOSS；全滅回營地 | SONNET | OPUS | 任務鏈 PlayMode 測試；`RunPlan` 契約變更 | ⬜ |
| P7-02h | 完成結算：解鎖自身角色圖鑑、開放大廳、移除大廳首次選御三家；更新 `demo-runbook.md` | SONNET | — | 完成後大廳可選星 | ⬜ |
| P7-02i | 文案：好友／宿敵台詞、任務提示（世界觀後續待補） | TERRA（zh-TW/en）→ HYDROGEN（ja） | 交叉＋人工 | `validate-data` 0 error | ⬜ |
| P7-03 | 音效與音樂整合、屬性音效辨識 | SONNET | 人工 | 十屬性音效可區分 | ⬜ |
| P7-04 | 商店頁文案、署名/授權頁（含地圖資料署名） | TERRA | ARGON（授權核對）+ 人工 | 授權條款全數滿足 | ⬜ |
| P7-05 | 發行建置管線與版本號 | SONNET | OPUS | CI 產出可安裝建置 | 🟦 |
| P7-05a | D0 打包發布失敗回復（P7-05 子項） | Codex | — | checksum／目錄／ZIP 發布失敗後可重試；競態版本保留檔不被誤刪 | ✅ |
| P7-06 | 全域程式碼審查：契約、FENCE 標記、測試覆蓋 | OPUS | SOL | 無未標記 workaround；public API 契約 100% | ⬜ |

## 未收尾紀錄
[log/phase-7.md](log/phase-7.md)（partial／blocked 才寫；舊紀錄為封存）。
