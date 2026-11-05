# Roadmap 索引

只列階段與目前位置。項目細節、驗收條件、交接紀錄在各 `phase-N.md`。
AI 代號（OPUS / SONNET / SOL / TERRA / ARGON / FLASH）定義在 `../ai-agents.md`。
狀態：⬜ 未開始 · 🟦 進行中 · 🟨 待審 · ✅ 完成 · ⛔ 阻塞

| 階段 | 目標（出口條件） | 狀態 | 檔案 |
|---|---|---|---|
| P0 基礎建設 | Unity 專案、規範、資料 schema、CI 可跑測試 | 🟦 | [phase-0.md](phase-0.md) |
| P1 星球垂直切片 | 可在 R=640 m 的程序化 cube-sphere 上繞一圈，卡通渲染 | ⬜ | [phase-1.md](phase-1.md) |
| P2 宙獸與戰鬥核心 | 御三家可召喚、輪替、施招，對木樁/簡單敵人戰鬥 | ⬜ | [phase-2.md](phase-2.md) |
| P3 世界生成 | 真實地圖模板 + 程序化區域/地城/BOSS，一場完整 run | ⬜ | [phase-3.md](phase-3.md) |
| P4 Meta 循環 | 地球大廳選星、圖鑑/各表、收服/蛋/進化、存檔 | ⬜ | [phase-4.md](phase-4.md) |
| P5 內容擴量 | AI 批次宙獸管線產出 ≥ 60 條進化線並平衡 | ⬜ | [phase-5.md](phase-5.md) |
| P6 多人 | LAN/自建伺服器 1–4 人合作 + 對抗 | ⬜ | [phase-6.md](phase-6.md) |
| P7 打磨與發行 | 效能、音效、新手引導、建置發行 | ⬜ | [phase-7.md](phase-7.md) |

## 依賴
```
P0 ─▶ P1 ─▶ P3 ─▶ P4 ─▶ P7
  └─▶ P2 ─┘      ▲
        └─▶ P5 ──┘
P1+P2 ─▶ P6（可與 P4/P5 平行）
```
P1 與 P2 可平行（不同 feature、不同 session）。

## 現在
下一個可開工項目：**P0-01**（見 phase-0.md）。阻塞中的待決問題：`../open-questions.md`。
