# 技術債（D0 期間延後的審查項目）

D0 demo 前 Sol 審查只修 high 與阻礙遊玩的問題；其餘 med/low 記在這裡，demo 後處理。每列：來源報告 · ID · 嚴重度 · 一句話。

| 來源 | ID | 嚴重度 | 內容 |
|---|---|---|---|
| `docs/reviews/P1-review-r17-sol.md` 之後 | r18 | — | P1 第 18 輪複審因 Codex 額度中斷未執行；p1/r5-fixes 已於 2026-11-08 合併（EditMode 825/825） |
| `Combat/Runtime/Model/Encounter.cs` | D0-E1 | med | 場地招式在施放瞬間結算一次，未依 `field.tick_ms` 持續判定 |
| 同上 | D0-E2 | med | 玩家角色不是野生宙獸的目標（`PlayerVitals` 尚未接線） |
| 同上 | D0-E3 | low | steering 為直線大圓移動，無障礙迴避；移速未套 move_speed 效果 |
| `Data/balance.json#capture` | D0-B1 | — | 收服率 60% / BOSS 100% 為佔位值，待 SOL 平衡模型（P5-07）定案 |
| `Bootstrap/Demo/RunSession.cs` | D0-U1 | med | 戰鬥動詞讀 `Keyboard.current`（1/2/Tab/F/R/T/G/V/C/Esc），未接 Input System action 與手把（P7-07） |
| 同上 | D0-R1 | med | 倒下的宙獸整場 run 不能復活、戰鬥外不回血（無設計規則；可能過難） |
| 同上 | D0-R2 | low | BOSS 只是成年型態，沒有 HP/數值加成 |
| `Bootstrap/Demo/PlanetLook.cs` | D0-V1 | low | 生物群系只在 shader 依高度/坡度/緯度上色；無規則層 biome 表、無散佈（P3-09） |
| `docs/reviews/D0-r2-sol.md` | S5 | low | `ScatterField` 半格距離圓盤取樣在 cube-face 接縫附近可漏掉 90 m 內的格（裝飾漏生/跳現）；改為保守枚舉 face/cell 範圍後再距離篩選 |
