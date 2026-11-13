# P7 打磨與發行

**出口條件**：目標硬體穩定 60 fps；新手引導完成；音效音樂到位；可安裝的發行建置。

| ID | 項目 | 主責 | 審查 | 驗收條件 | 狀態 |
|---|---|---|---|---|---|
| P7-01 | 效能剖析與最佳化（GPU/CPU/記憶體） | OPUS | SONNET | PC 最低規格 60 fps；Steam Deck 800p 穩定 30–40 fps；無 GC 尖峰 | ⬜ |
| P7-07 | Steam Deck Verified 檢查（手把、字級、預設檔、Proton 相容） | SONNET | 人工（實機） | 通過 Valve Deck 相容性檢查清單 | 🟦 |
| P7-02 | 新手引導（御三家選擇、第一顆星球） | SONNET | 人工 | 新玩家 10 分鐘內理解召喚與輪替 | 🟦 |
| P7-03 | 音效與音樂整合、屬性音效辨識 | SONNET | 人工 | 十屬性音效可區分 | ⬜ |
| P7-04 | 商店頁文案、署名/授權頁（含地圖資料署名） | TERRA | ARGON（授權核對）+ 人工 | 授權條款全數滿足 | ⬜ |
| P7-05 | 發行建置管線與版本號 | SONNET | OPUS | CI 產出可安裝建置 | 🟦 |
| P7-06 | 全域程式碼審查：契約、FENCE 標記、測試覆蓋 | OPUS | SOL | 無未標記 workaround；public API 契約 100% | ⬜ |

## 未收尾紀錄
[log/phase-7.md](log/phase-7.md)（partial／blocked 才寫；舊紀錄為封存）。
