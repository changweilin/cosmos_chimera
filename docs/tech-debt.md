# 技術債（D0 期間延後的審查項目）

D0 demo 前 Sol 審查只修 high 與阻礙遊玩的問題；其餘 med/low 記在這裡，demo 後處理。每列：來源報告 · ID · 嚴重度 · 一句話。

| 來源 | ID | 嚴重度 | 內容 |
|---|---|---|---|
| `docs/reviews/P1-review-r17-sol.md` 之後 | r18 | — | P1 第 18 輪複審因 Codex 額度中斷未執行；p1/r5-fixes 已於 2026-11-08 合併（EditMode 825/825） |
| `Combat/Runtime/Model/Encounter.cs` | D0-E2 | med | 玩家角色不是野生宙獸的目標（`PlayerVitals` 尚未接線） |
| 同上 | D0-E3 | low | steering 為直線大圓移動，無障礙迴避；移速未套 move_speed 效果 |
| 同上 | D0-E4 | low | `TangentToward` 在目標近乎正上／下方（側向偏移 ≲ 0.1 mm，世界座標 ~640 m）時，去掉徑向分量後的 float 殘差（例：4.36e-6）仍超過門檻並被正規化，產生非切向 forward，觸發 `MoveGeometry` 的 unit-tangent Pre（Release 剝除後靜默使用）。單純改相對門檻無效（見 `docs/reviews/P2-12-sol-r5.md` R5-07 數值見證）；需含世界座標精度的退化判定＋再次正交化，並補獨立幾何測試。P2-12 回歸測試以 6 m 停位隔離此缺陷 |
| `Data/balance.json#capture` | D0-B1 | — | 收服率 60% / BOSS 100% 為佔位值，待 SOL 平衡模型（P5-07）定案 |
| `Bootstrap/Demo/DemoGame.cs` | D0-U1 | med | 探索及大廳／選星／結算已支援手把；尚無遊戲內重綁，仍需實體手把與 Steam Deck／Proton 實機驗收（P7-07） |
| 同上 | D0-R1 | med | 倒下的宙獸整場 run 不能復活、戰鬥外不回血（無設計規則；可能過難） |
| 同上 | D0-R2 | low | BOSS 只是成年型態，沒有 HP/數值加成 |
| `Bootstrap/Demo/PlanetLook.cs` | D0-V1 | low | 生物群系只在 shader 依高度/坡度/緯度上色；無規則層 biome 表、無散佈（P3-09） |
| `docs/reviews/D0-r2-sol.md` | S5 | low | `ScatterField` 半格距離圓盤取樣在 cube-face 接縫附近可漏掉 90 m 內的格（裝飾漏生/跳現）；改為保守枚舉 face/cell 範圍後再距離篩選 |
| `docs/reviews/P3-10-sol.md` | S6 | med | 探針沒有摘要 `LandingDirection`（`RunPlanner.Hash` 也不含），降落點改變不會被偵測 |
| 同上 | S7 | low | `ReproProbe.Report` 未檢查 null 型別元素／缺 Adult 的野生池；`ReproBuild.BuildAll` 未檢查 batchmode |
| `Bootstrap/Demo/RunSession.cs` | D0-X2 | low | 副手經驗只在 `Progression.Settle` 規則層；D0 無副手編成，run 只結算主力 |
| 同上 | D0-X3 | low | 星球難度 d 尚無資料欄（planet-type schema），D0 固定 d=1；野生等級只隨區域 tier 與角色變化 |
| `docs/decisions/ADR-0007-generation-reproducibility.md` | Q-T6-R1 | med | 生成點選陸地（`IsSea(direction)`）與 Scatter 讀浮點方向，格界附近跨後端可能翻轉；P6 握手比對 `planDiscrete` 偵測，不一致時改以主機計畫下發 |
| `docs/reviews/P2-12-sol-r5.md` | R5-01 | med | `RunSession.InjectFaintForTests` 未去重：同 id 排入兩次會重複發擊倒 XP（僅測試接縫，正式路徑不呼叫） |
| 同上 | R5-02 | med | XP 回歸測試的擊倒全走注入，未經正式 `Encounter.Tick` Faint 分派；移除正式 `OnFaint` 呼叫不會被測出。需補一例真實 Tick 擊倒 |
| 同上 | R5-03 | med | R3-01 測試只看 Step 後等級，無法證明升級在首個 Tick 前生效（把 `ApplyLevelUps` 移到 Tick 後仍通過） |
| 同上 | R5-04 | med | RunSession XP oracle 多處重算 `Split`／共用 `WildLevel`，且只測全存活、ID＝槽位＋1；死亡分母與永久 ID 平手接線無守護 |
| 同上 | R5-05 | low | 測試接縫未以 `UNITY_EDITOR` 排除於正式 build；accessor 回傳可變 live 物件 |
| 同上 | R5-06 | low | 回歸測試讀正式 balance 卻斷言精確 XP 值，平衡調整會誤報；改用合成 progression fixture |
| `docs/reviews/P2-05-wiring-sol.md` | W-03 | med | 素材接線 PlayMode 仍只涵蓋普通＋BOSS 注入擊倒：精英、真實 Tick 分派、End 回呼經 `DemoGame.OnRunEnded` 入帳、勝／敗／放棄各路徑，以及 JsonUtility 對 qty/tier 小數／數字字串／超出 Int32 的嚴格性未測（進化落盤、拒絕不變、W-01／W-02 交易已由 `LobbyTransactionsTests` 涵蓋） |
| `docs/reviews/P2-05-views-sol.md` | V-01 | med | view 接線測試未經 SyncViews：RunSession 不呼叫 `ShowFainted` 不會被測出；佔位外觀（primitive、十色、toon 外框 0.03 m）未以固定基準斷言 |
