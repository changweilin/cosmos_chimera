# 技術債（D0 期間延後的審查項目）

D0 demo 前 Sol 審查只修 high 與阻礙遊玩的問題；其餘 med/low 記在這裡，demo 後處理。每列：來源報告 · ID · 嚴重度 · 一句話。

| 來源 | ID | 嚴重度 | 內容 |
|---|---|---|---|
| `docs/reviews/P1-review-r17-sol.md` 之後 | r18 | — | P1 第 18 輪複審因 Codex 額度中斷未執行；p1/r5-fixes 已於 2026-11-08 合併（EditMode 825/825） |
| `Combat/Runtime/Model/Encounter.cs` | D0-E3 | low | steering 為直線大圓移動，無障礙迴避；移速未套 move_speed 效果 |
| `Combat/Runtime/Model/Encounter.cs` | D0-W1 | low | 遊蕩目的地不檢查海面／障礙（與 D0-E3 相同的直線 steering）；生成點靠海時可能走進水面 |
| `Combat/Runtime/Model/Encounter.cs` | D0-W2 | low | 推開在每 tick 全部移動與施招之後執行：本 tick 招式用的是推開前位置（偏差 ≤ 一個身體寬）；推開不看海面／地形，可把單位推進水面；人群每 tick 只掃 2 輪，重疊靠後續 tick 收斂 |
| `Bootstrap/Demo/RunSession.cs` | D0-W2 | low | 角色被推開是直接改 Rigidbody／transform 位置（瞬移），不經物理；夥伴 Follow 貼身時角色可能被持續推擠，待人工試玩確認手感 |
| `Bootstrap/Demo/RunSession.cs` | D0-W3 | low | 互動選單為 IMGUI，開啟時停用角色移動輸入但戰鬥時間照走；目標遊蕩超過 2× 互動距離即自動關閉 |
| `Data/balance.json#capture` | D0-B1 | — | 收服率 60% / BOSS 100% 為佔位值，待 SOL 平衡模型（P5-07）定案 |
| `Bootstrap/Demo/DemoGame.cs` | D0-U1 | med | 探索及大廳／選星／結算已支援手把；尚無遊戲內重綁，仍需實體手把與 Steam Deck／Proton 實機驗收（P7-07） |
| 同上 | D0-R1 | low | 昏厥持續整場 run 已定案為規則（combat.md）；治療道具／NPC 待 Q-C36 |
| `Bootstrap/Demo/PlanetLook.cs` | D0-V1 | low | 生物群系只在 shader 依高度/坡度/緯度上色；無規則層 biome 表、無散佈（P3-09） |
| `Bootstrap/Demo/RunSession.cs` | D0-X2 | low | 副手經驗只在 `Progression.Settle` 規則層；D0 無副手編成，run 只結算主力 |
| 同上 | D0-X3 | low | 星球難度 d 尚無資料欄（planet-type schema），D0 固定 d=1（BOSS LV5、普通野生 LV1）；等級公式已實作 |
| `docs/decisions/ADR-0007-generation-reproducibility.md` | Q-T6-R1 | med | 生成點選陸地（`IsSea(direction)`）與 Scatter 讀浮點方向，格界附近跨後端可能翻轉；P6 握手比對 `planDiscrete` 偵測，不一致時改以主機計畫下發 |
