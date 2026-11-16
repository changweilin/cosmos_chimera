# 技術債（D0 期間延後的審查項目）

D0 demo 前 Sol 審查只修 high 與阻礙遊玩的問題；其餘 med/low 記在這裡，demo 後處理。每列：來源報告 · ID · 嚴重度 · 一句話。

| 來源 | ID | 嚴重度 | 內容 |
|---|---|---|---|
| `docs/reviews/P3-25-sol.md` | P3-25-S1 | med | 人工林限制只套樹位；地貌表直接列出的傘形樹、倒木仍生成，違反同林班單一樹種／無死木；原主責修正並升 ScatterPlanner 版本 |
| `docs/reviews/P3-25-sol.md` | P3-25-S2 | med | 倒木規則阻擋半徑 0.3S，小於實體 CapsuleCollider 長軸半長 0.4S；規則／物理阻擋與下層避讓須同步，改半徑需升生成版本 |
| `docs/reviews/P3-04a-opus.md` | P3-04a-O1 | med | 翻滾冷卻每次嘗試時用當下天氣重算，翻滾後換天氣會讓冷卻再鎖、HUD 倒數往回跳；待 Q-C39 定案後修 `PlayerVitals` |
| `docs/reviews/P3-12-grass-opus.md`、`P3-04b-opus.md` | P3-12-O2／P3-04b-O3 | low | 隱蔽用的玩家速度取自表現層物理、天氣覆寫與冷卻只存本機；P6 須由權威端提供並同步 |
| `docs/reviews/P3-04b-opus.md` | P3-04b-O1 | low | `WeatherControl` 時段長度寫死為預設 180 秒，與 `WeatherPlanner` 可傳入的 `slotDurationMs` 脫鉤 |
| `docs/reviews/P1-15-opus.md` | P1-15-O1／O2 | low | 遠景淡入用 3D 距離，大高差處與近景真物件疊畫；天氣縮短視野時霧中 tile 仍照常繪製（量 Steam Deck 幀率時一併看） |
| `docs/reviews/D0-U2-opus.md` | D0-U2-O1 | med | 副手裝備效果目前以 100% 加到主力；Q-C40 已定案為 25%（`crafting.md#穿戴與數值`），待 Q-C42 定取整／合併／上限後修改 `RunSession.AddEquippedPartyCreature` 並補測試 |
| `docs/reviews/D0-U2-opus.md` | D0-U2-O2 | med | 資料更新使存檔裝備欄位／穿戴者不符時 `EquipmentRules.Collect` 丟例外，大廳裝備頁與出發都卡死；應比照退役物品不給加成並可卸下 |
| `docs/reviews/D0-U2-opus.md` | D0-U2-O3／O4 | low | 三件裝備效果合計可合法超過 32 而丟例外（資料驗證補檢查）；`CraftRules.TryConsumeCaptureVessel` 與 `RunSession` 器皿扣除重複 |
| `docs/reviews/capture-elements-opus.md` | CE-O3 | med | 分階器皿規則已定案寫入 `lobby-meta.md#收服器皿分階`（Q-C41）；+15／−25 仍寫死於 `CaptureRules.DefaultStageCurve`，接線時移到 `balance.json`；接線待 Q-C43（與三類別的關係） |
| `docs/reviews/capture-elements-opus.md` | CE-O5 | med | `creatures.md` 掉落規則仍寫依 `element_bias` 等機率，與主副屬性加權掉落矛盾；需使用者同意後改為連結 `planet-world.md#星球屬性` |
| `docs/reviews/capture-elements-opus.md` | CE-O4／O6／O7／O8 | low | 收服率加總可溢位；`RunPlanner`／`SettlementPlanner` 以半徑反推尺寸（應用 `BossDraw.SizeFor`，下次升版時改）；`RunPlan.Elements` 預設 null 違反 Post；`PlanetElements` 回傳可變內部陣列 |
| `docs/reviews/D0-U1-opus.md`、`capture-elements-opus.md` | D0-U1-O1～O3／CE-O1～O2 | low | `LobbySave.FromJson` 契約未列 v6–v8 拒絕條件；翻滾冷卻用浮點倍率（P6 改 permille）；同名副手特性靜默不生效；逃跑動畫不隨手冊暫停；`VisualVessel` 尚無讀取端、轉換後孵化承襲哪個類別未定 |
| `docs/reviews/P1-review-r17-sol.md` 之後 | r18 | — | P1 第 18 輪複審因 Codex 額度中斷未執行；p1/r5-fixes 已於 2026-11-08 合併（EditMode 825/825） |
| `Combat/Runtime/Model/Encounter.cs` | D0-E3 | low | steering 為直線大圓移動，無障礙迴避：野生遇海只停在岸邊，不繞行海灣（2026-11-11 已接 move_speed 效果、野生只在陸地） |
| `Combat/Runtime/Model/Encounter.cs` | D0-W2 | low | 推開在每 tick 全部移動與施招之後執行：本 tick 招式用的是推開前位置（偏差 ≤ 一個身體寬）；推開不看地形起伏（海面已擋野生，玩家宙獸仍可被推入水面）；人群每 tick 只掃 2 輪，重疊靠後續 tick 收斂 |
| `Bootstrap/Demo/RunSession.cs` | D0-W2 | low | 角色被推開是直接改 Rigidbody／transform 位置（瞬移），不經物理；夥伴 Follow 貼身時角色可能被持續推擠，待人工試玩確認手感 |
| `Bootstrap/Demo/RunSession.cs` | D0-W3 | low | 互動選單為 IMGUI，開啟時停用角色移動輸入但戰鬥時間照走；目標遊蕩超過 2× 互動距離即自動關閉 |
| `Bootstrap/Demo/DemoGame.cs` | D0-B2 | low | BOSS 等級仍由固定 d=1 決定，不隨階段變化（小行星已於 2026-11-11 改以 R=320 遊玩，Q-D1） |
| `Data/balance.json#capture` | D0-B1 | — | 收服率 60% / BOSS 100% 為佔位值，待 SOL 平衡模型（P5-07）定案 |
| `Bootstrap/Demo/DemoGame.cs` | D0-U1 | med | 探索及大廳／選星／結算已支援手把；尚無遊戲內重綁，仍需實體手把與 Steam Deck／Proton 實機驗收（P7-07） |
| 同上 | D0-R1 | low | 昏厥持續整場 run 已定案為規則（combat.md）；Q-C36 規則層（`ReviveRules`／`Encounter.TryRevive`）已實作，D0 道具／NPC／營火／救援表現層尚未實作 |
| `Bootstrap/Demo/PlanetLook.cs` | D0-V1 | low | 生物群系地表配色仍由 shader 依高度/坡度/緯度決定；完整生物群系規則仍待 P3-04（散佈已由 `ScatterPlanner`／`ScatterBiomes` 實作，P3-09 完成） |
| `Bootstrap/Demo/RunSession.cs` | D0-X3 | low | 星球難度 d 尚無資料欄（planet-type schema），D0 固定 d=1（BOSS LV5、普通野生 LV1）；等級公式已實作 |
| `docs/decisions/ADR-0007-generation-reproducibility.md` | Q-T6-R1 | med | 生成點選陸地（`IsSea(direction)`）與 Scatter 讀浮點方向，格界附近跨後端可能翻轉；P6 握手比對 `planDiscrete` 偵測，不一致時改以主機計畫下發 |
| `docs/reviews/Q-D1-R320-sol.md` | Q-D1-R2 | low | 領地／脫戰以 3D 弦長量測（含高度差），非球面弧長；R=320、40 m 短 26 mm。若要精確弧距門檻須先定量測語意 |
| `docs/reviews/Q-D1-impl-sol-r2.md` | Q-D1-R5 | low | `RunPlanner` 最近陸地掃描成本 O(落海 Scatter 數 × 1.3e5 次 isLand)：陸地稀少時整份計畫可達數百萬次呼叫（SOL 實測 R1280 極小陸地 5.4e6 次／88–295 ms，結構上界 5e7）；Demo 遮罩海面有限、未量測原生 TemplateView，尚無生成時間預算 |
