# P3-12 草叢隱蔽接線 — OPUS 跨廠商審查

2026-11-16 · Claude Opus 5.5（OPUS）· 基準 `95a52ef`，受審變更 `442e577`（`WorldObjects.HasGrassCover`）、`3f980af`（`Encounter.SetGrassCoverProvider` 與 `RunSession` 接線）。範圍：上述兩個 commit 的程式與測試。未修改程式。

**結論：pass；high 0 / med 0 / low 3。** P3-12 的草叢接線部分可結案；人工試玩（README「現在」第 7 條）不在本次程式審查範圍。

## 核對結果
- 規格 `destructibles.md#碰撞`：「在草或迷霧內靜止或緩行時縮小野生領域距離，玩家宙獸也受益」。實作取單次減半（草與迷霧不疊乘），與「草**或**迷霧」一致。
- 隱蔽以**入侵者**的位置與速度判斷（玩家用 `_playerMoveSpeedMs`，己方宙獸用上一 tick 的 `LastMoveSpeedMs`），每 tick 每個入侵者查一次，與觀察者數量無關。已開戰與挑釁路徑未受影響。
- `HasGrassCover`：只認完整（未破壞）的 `TallGrass`；`radius = 0`（星球無芒草時）經 `ScatterPlanner.CollectCells` 的 `radiusM >= 0` 契約允許，不會擲例外。方向由 `Encounter.Up`（`PlanetConfig.ToDirection`，已正規化）提供，符合 unit-length Pre。
- 共用的 `_items` 緩衝在 `Tick` 內與 `Query` 依序使用，無重入。
- 重現性：只讀規則層散佈與破壞集合，不動 RNG 串流、生成版本與存檔。

## 發現

| ID | 嚴重度 | 位置 | 問題 | 建議 |
|---|---|---|---|---|
| P3-12-O1 | low | `Bootstrap/Demo/WorldObjects.cs` `HasGrassCover` | 規格寫「草」，實作只認 `TallGrass`（芒草）。同為「不擋」的 `Reed`（蘆葦，禾本科）、`Fern`、`Flowers` 不給隱蔽。屬規格解讀，非錯誤。 | 已開 `open-questions.html` Q-C38 待使用者確認；定案前維持現狀。 |
| P3-12-O2 | low | `Bootstrap/Demo/RunSession.cs` `StepCombat` | 玩家速度取自表現層 `_rig.Character.Velocity`（沿用迷霧的既有做法），不是規則層模擬值。單機無影響；連線時各端速度不同會讓偵測結果分歧。 | P6 連線時改由權威端提供速度；記入 `tech-debt.md`。 |
| P3-12-O3 | low | `Combat/Runtime/Model/Encounter.cs` `UpdateEngagement` | 每 tick 都對玩家與在場宙獸查詢草叢，即使附近沒有野生。每次查詢只是幾個格子，成本可忽略。 | 無須處理；若之後幀率預算吃緊，可改為只在有野生在領域 ×2 內時才查。 |

## 驗證
見本次 OPUS 審查批次的完整測試結果（與 P3-04a／P3-04b／P1-15 同批）：`Logs/opus-review-editmode.xml`、`Logs/opus-review-playmode.xml`。
- Unity 6000.3.28f1 batchmode，基準 `95a52ef`：EditMode **3555/3555 通過**；PlayMode **147 通過、0 失敗、8 略過**（略過的都是需要 `COSMIMERA_CAPTURE=1` 的截圖測試，未當作目視驗收）；無 C# 編譯警告或錯誤。
