# 收服失敗逃跑／外觀器皿（存檔 v8）、分階收服器皿、星球主副屬性 — OPUS 審查

2026-11-16 · Claude Opus 5.5（OPUS）· 基準 `28e4991`。受審變更（皆無審查紀錄、未掛 roadmap 項目 ID）：
- `b0954b0` 收服失敗時只做外觀逃跑；`OwnedCreature.VisualVessel`、`LobbySave.SetVessel`、存檔 v8。
- `f4acede` `CaptureRules` 四階基本器皿／十屬性進階器皿規則、`CraftCaptureSpec`、`CraftRules.TryConsumeCaptureVessel`。
- `28e4991` `PlanetElementDraw`／`PlanetElements`、`RunPlanner` v6→v7（生成權重、屬性詞綴、任務）、`MaterialDrops` 加權掉落、選星畫面。

未修改程式。

**結論：PlayMode 有 4 個回歸（high 1，見 CE-H1，已修測試）。**`b0954b0` pass（low 2）；`f4acede` 需使用者確認規格後才能接線（med 1、low 1）；`28e4991` pass with debt（med 1、low 3）。

## CE-H1（high）PlayMode 回歸
`28e4991` 的 commit 寫「PlayMode not run」。本次在 `28e4991` 跑完整 PlayMode：**144 通過、4 失敗**（前一次 OPUS 審查 `95a52ef` 時為 147/0）。全部在 `Bootstrap/Tests/PlayMode/RunSessionProgressionTests.cs`：

| 測試 | 失敗訊息 | 判斷 |
|---|---|---|
| `Real_tick_knockout_pays_xp` | 期望 Wood/T1，實際 Water/T1 | 測試過期：oracle 仍呼叫舊的 `TryRoll(role, ElementBias, …)` 等機率版，遊戲已改用主副屬性加權（`28e4991`）。改用 `plan.Elements` 的加權多載即可。 |
| `Elite_knockout_drops_elite_material` | 期望 Wind/T2，實際 Thunder/T2 | 同上。 |
| `Every_end_path_hands_the_run_materials_to_the_lobby(Downed)` | 「run 因玩家倒下而結束」期望 HP 0，實際 1 | 未確認原因：run 有結束但不是因為玩家倒下。`RunPlanner` v7 改變了生成分佈，測試挑的野生目標或周邊情況可能改變；需要重現後判斷是測試假設失效還是結算路徑有誤。 |
| `Failed_capture_flee_moves_visual_away_without_interaction` | 未處理的 log：場景中有 2 個 AudioListener | `b0954b0` 新增的測試以 `keepEnabled: true` 啟動，場景多出一個 AudioListener；測試本身的環境問題，不是逃跑邏輯錯誤。 |

依 `AGENTS.md` 完成定義，這兩個 commit 在 PlayMode 修綠前不算完成。

**已修（同日，OPUS，只改測試）**：
- 兩個掉落 oracle 及同檔其他三處改用 `plan.Elements` 加權多載；「掉落屬性在偏好清單內」的斷言改為「在本星球主副屬性內」（副屬性可不在 `ElementBias`）。
- Downed：追蹤確認 `RunPlanner` v7 下新 seed 挑到的野生是 `timid`（`ice_frost_fledgling`）。依 Q-C35，膽小個體只在被打中後才反擊，所以 20 秒都沒攻擊玩家，BOSS 收服時限到期後 run 以勝利結束。測試改為挑非 `timid` 的野生。遊戲邏輯正確。
- 逃跑測試：移除結尾的 `LogAssert.NoUnexpectedReceived()`。失敗來自引擎的一般 log「2 audio listeners」，只在整套執行時、其他測試載入 `Demo.unity` 後出現；錯誤與例外本來就會讓測試失敗。
- 修後完整 PlayMode：**148 通過、0 失敗、8 略過**；EditMode 程式未動（修前 3571/3571）。

## b0954b0 — 逃跑外觀與外觀器皿
- 收服失敗時規則層照舊：`w.Gone = true`、`_encounter.Remove`，不能再瞄準或重擲；只有 view 以 10 m/s 沿球面跑 2.5 秒後縮小消失。逃跑方向用當地 up 投影，退化時有兩層備援。物理推擠在逃跑判斷前同步，被移除的單位不再推人。
- `VisualVessel` 只在 v8 讀取，v1–v7 等於 `Vessel`；未定義值在任何建置都拒絕。`WithSpecies`、`SetExp`、`SetSpecies` 都保留 `VisualVessel`。

| ID | 嚴重度 | 位置 | 問題 | 建議 |
|---|---|---|---|---|
| CE-O1 | low | `RunSession.UpdateFleeingViews` | 用 `Time.deltaTime`，手冊暫停（戰鬥時間不前進）時逃跑動畫仍在跑。純外觀。 | 人工試玩時看；要一致就改用戰鬥時間差。 |
| CE-O2 | low | `OwnedCreature.VisualVessel`、`LobbySave.SetVessel` | 目前沒有任何呼叫端讀 `VisualVessel` 或呼叫 `SetVessel`（後期轉換與外觀都還沒接）。孵化「承襲親代類別」時蛋只記一個 `Vessel`，轉換過的親代要承襲功能還是外觀，規格沒寫。 | 接 Q-C13 轉換時一併決定；現在不用改。 |

## f4acede — 分階收服器皿
- 規則本身自洽：基本器皿依「器皿階 − 目標階」每階 +15／−25；基本器皿階 ≥ 星球 BOSS 階時 100%；進階器皿在相同屬性進階地形對同屬性地城 BOSS 100%；結果夾在 0..100。舊的 `ChancePercent` 多載保留，`RunSession` 仍用舊規則。

| ID | 嚴重度 | 位置 | 問題 | 建議 |
|---|---|---|---|---|
| CE-O3 | med | `Capture/Api/CaptureRules.cs` | **沒有規格來源**：`docs/design/`、`open-questions` 都找不到「四階基本器皿」「十屬性進階器皿」「BOSS 保證 100%」的定案，commit 也沒有更新設計文件；+15／−25 是寫死在程式裡的平衡數值，不在 `Data/balance.json`。另外與既有的三種器皿類別（符籙卡／琉璃珠／魔術方塊，決定副手提供什麼）是什麼關係也沒有說明。 | Q-C41 已定案，規則寫入 `lobby-meta.md#收服器皿分階`；數值接線時移到 `balance.json`；與三類別的關係待 Q-C43。 |
| CE-O4 | low | `CaptureRules.ChancePercent` | `CaptureStageCurve` 與 `CaptureEnvironment.BonusPercent` 只要求 ≥ 0、沒有上限，極大值會讓加總溢位成負數，被夾成 0（應該是 100）。 | 加總改用 `long`，或在 Pre 限制在 0..100。 |

## 28e4991 — 星球主副屬性
- `PlanetElementDraw` 用獨立的 `Pcg32(seed, 'E')` 串流，全專案沒有其他 `'E'`；抽數、比例範圍、每個屬性至少 1%、總和 100 都與 `planet-world.md#星球屬性` 一致，`PlanetElementDrawTests` 有涵蓋。`RunPlanner.GeneratorVersion` 6→7，快照同步更新；`Hash` 納入屬性比例。選星畫面與 `RunPlanner` 各自呼叫 `Draw`，輸入相同所以結果一致。
- `PickAffix` 依比例抽詞綴，比例總和恆為 100，所以退路分支實際不會走到；`MaterialDrops` 加權版先擲機率再擲屬性，失敗只消耗 1 次亂數，與原版串流位置規則相同。

| ID | 嚴重度 | 位置 | 問題 | 建議 |
|---|---|---|---|---|
| CE-O5 | med | `docs/design/creatures.md` 第 66 行 | 仍寫「屬性從該星球類型的 `element_bias` 等機率抽一種」，與現在的依主副屬性比例加權掉落矛盾。`design/` 是現行規格，一個概念只能有一處定義。 | 改成連結 `planet-world.md#星球屬性`。屬於規格文字，需使用者同意後再改（本次未改）。 |
| CE-O6 | low | `RunPlanner.SizeForRadius` | 由半徑反推星球尺寸，但 `Generate` 已經拿到 `bossStage`，專案慣例是 `BossDraw.SizeFor(bossStage)`（選星畫面就是這樣算）。目前半徑都由 `RadiusFor(SizeFor(stage))` 產生，兩者一致；若測試或日後設定用了不同組合（例如 R=640 搭幼年 BOSS），選星顯示與實際生成的屬性會不同。`SettlementPlanner` 有同樣的寫法。 | 下次升 `GeneratorVersion` 時改用 `BossDraw.SizeFor(bossStage)`。 |
| CE-O7 | low | `WorldGen/Api/RunPlan.cs` 建構子 | `Elements` 的 Post 寫「non-null」，但建構子參數預設 `null`（`QuestTrackerTests` 就這樣建）；用這種 plan 跑 `RunSession` 會在掉落時 NullReference。 | 移除預設值，測試補上 `PlanetElements`；或 Post 改寫為「由 `RunPlanner` 產生時非 null」。 |
| CE-O8 | low | `WorldGen/Api/PlanetElements` | `Shares`／`Elements`／`Percents` 直接回傳內部陣列，呼叫端轉型成陣列就能改掉生成結果。 | 回傳 `Array.AsReadOnly`。 |

## 驗證
見 `D0-U2-opus.md` 末段（同批完整測試）；PlayMode 失敗明細見上方 CE-H1。
