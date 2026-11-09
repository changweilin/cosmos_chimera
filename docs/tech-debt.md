# 技術債（D0 期間延後的審查項目）

D0 demo 前 Sol 審查只修 high 與阻礙遊玩的問題；其餘 med/low 記在這裡，demo 後處理。每列：來源報告 · ID · 嚴重度 · 一句話。

| 來源 | ID | 嚴重度 | 內容 |
|---|---|---|---|
| `docs/reviews/P1-review-r17-sol.md` 之後 | r18 | — | P1 第 18 輪複審因 Codex 額度中斷未執行；p1/r5-fixes 已於 2026-11-08 合併（EditMode 825/825） |
| `Combat/Runtime/Model/Encounter.cs` | D0-E2 | med | 玩家角色不是野生宙獸的目標（`PlayerVitals` 尚未接線） |
| 同上 | D0-E3 | low | steering 為直線大圓移動，無障礙迴避；移速未套 move_speed 效果 |
| `Data/balance.json#capture` | D0-B1 | — | 收服率 60% / BOSS 100% 為佔位值，待 SOL 平衡模型（P5-07）定案 |
| `Bootstrap/Demo/DemoGame.cs` | D0-U1 | med | 戰鬥操作已接 `DemoRun` action map 與手把綁定；大廳／選星／結算仍需鍵鼠，尚無遊戲內重綁或 Steam Deck 實機驗收（P7-07） |
| 同上 | D0-R1 | med | 倒下的宙獸整場 run 不能復活、戰鬥外不回血（無設計規則；可能過難） |
| 同上 | D0-R2 | low | BOSS 只是成年型態，沒有 HP/數值加成 |
| `Bootstrap/Demo/PlanetLook.cs` | D0-V1 | low | 生物群系只在 shader 依高度/坡度/緯度上色；無規則層 biome 表、無散佈（P3-09） |
| `docs/reviews/D0-r2-sol.md` | S5 | low | `ScatterField` 半格距離圓盤取樣在 cube-face 接縫附近可漏掉 90 m 內的格（裝飾漏生/跳現）；改為保守枚舉 face/cell 範圍後再距離篩選 |
| `docs/reviews/P3-10-sol.md` | S6 | med | 探針沒有摘要 `LandingDirection`（`RunPlanner.Hash` 也不含），降落點改變不會被偵測 |
| 同上 | S7 | low | `ReproProbe.Report` 未檢查 null 型別元素／缺 Adult 的野生池；`ReproBuild.BuildAll` 未檢查 batchmode |
| `Bootstrap/Demo/RunSession.cs` | D0-X2 | low | 副手經驗只在 `Progression.Settle` 規則層；D0 無副手編成，run 只結算主力 |
| 同上 | D0-X3 | low | 星球難度 d 尚無資料欄（planet-type schema），D0 固定 d=1；野生等級只隨區域 tier 與角色變化 |
| `docs/decisions/ADR-0007-generation-reproducibility.md` | Q-T6-R1 | med | 生成點選陸地（`IsSea(direction)`）與 Scatter 讀浮點方向，格界附近跨後端可能翻轉；P6 握手比對 `planDiscrete` 偵測，不一致時改以主機計畫下發 |
| `docs/reviews/P2-12-sol.md` | R6 | low | `QuestTracker.OnKnockedOut` 未忽略同一 spawn 重送；D0 Encounter 不會重送，屬防禦缺口 |
| `docs/reviews/D0-E1-R3-R5-sol.md` | R5-01 | med | `Progression.FromJson` 的配置包絡與 Int32 上限是 `Contract.Requires`，Release 剝除後非核定配置仍可讓 `SourceXp` 為負；核定值安全。打包前以契約啟用版驗證同一 balance.json，或改不剝除的拒絕路徑 |
| `docs/reviews/P2-12-sol.md` | R7 | low | `XpSettlement.Paid/Assigned` 回傳可轉型的原始陣列 |
| 同上 | R8 | low | `Settle` Post 應寫成 `min(Exp, MaxExp) + Paid <= MaxExp` |
| 同上 | R9 | low | `XpRecipient` 建構子未以 Contract 檢查 id/exp（由 `Settle` 攔截） |
| `docs/reviews/P2-12-sol-r3.md` | R3-04 | low | `Encounter.SetLevel` 的戰鬥外限制只寫在 Pre，未檢查（呼叫端 `RunSession` 以 `InCombat` 把關） |
