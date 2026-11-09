# P7 打磨與發行

**出口條件**：目標硬體穩定 60 fps；新手引導完成；音效音樂到位；可安裝的發行建置。

| ID | 項目 | 主責 | 審查 | 驗收條件 | 狀態 |
|---|---|---|---|---|---|
| P7-01 | 效能剖析與最佳化（GPU/CPU/記憶體） | OPUS | SONNET | PC 最低規格 60 fps；Steam Deck 800p 穩定 30–40 fps；無 GC 尖峰 | ⬜ |
| P7-07 | Steam Deck Verified 檢查（手把、字級、預設檔、Proton 相容） | SONNET | 人工（實機） | 通過 Valve Deck 相容性檢查清單 | ⬜ |
| P7-02 | 新手引導（御三家選擇、第一顆星球） | SONNET | 人工 | 新玩家 10 分鐘內理解召喚與輪替 | ⬜ |
| P7-03 | 音效與音樂整合、屬性音效辨識 | SONNET | 人工 | 十屬性音效可區分 | ⬜ |
| P7-04 | 商店頁文案、署名/授權頁（含地圖資料署名） | TERRA | ARGON（授權核對）+ 人工 | 授權條款全數滿足 | ⬜ |
| P7-05 | 發行建置管線與版本號 | SONNET | OPUS | CI 產出可安裝建置 | 🟦 |
| P7-06 | 全域程式碼審查：契約、FENCE 標記、測試覆蓋 | OPUS | SOL | 無未標記 workaround；public API 契約 100% | ⬜ |

## 交接紀錄
- 2026-11-09 · SOL（ChatGPT Sol） · partial · P7-05 補正式 `tools/demo/package.py`（14 項防退化驗證），產出 `COSMIMERA-D0-20261109-d19d13f-sol2-win64.zip`（46.7 MB，CRC／逐檔 SHA-256 PASS，26 個 player 檔與 sol1 一致），更新展示手冊與 `docs/reviews/D0-playability-sol.md`；完整 Python 171 項＋194 子案例、EditMode 887/887 通過；P5-07 已提供 2688 場初始難度基線，最終重跑摘要一致 · 下一步：人工試玩實體輸入／完整任務鏈／BOSS；OPUS／SONNET 審核難度方案及接續發行 CI／安裝程式；文本、美術及 3D 後置
- 2026-11-09 · SOL（ChatGPT Sol） · partial · P7-05 D0 本機 IL2CPP demo 包完成（`Builds/Demo/COSMIMERA-D0-20261109-d19d13f-sol1-win64.zip`，46.7 MB），含操作手冊／署名／版本與雜湊；最終 Mono、IL2CPP 建置成功，EditMode 887/887、GPU PlayMode 62/62、Python 157 項＋194 子案例通過，跨後端 policy b PASS；獨立版選御三家／讀檔／選星／降落與修正後材質已目視，詳見 `docs/reviews/D0-demo-package-sol.md` · 下一步：人工驗證實體鍵盤、完整任務鏈／BOSS 與難度；SONNET／OPUS 接續發行 CI、安裝程式及審查
