# P3-04b 主動操縱天氣與戰術資訊 — OPUS 跨廠商審查

2026-11-16 · Claude Opus 5.5（OPUS）· 基準 `95a52ef`，受審變更 `07f0879`。範圍：`WorldGen/Api/WeatherControl`、`BalanceIntegerGuard.ReadPositiveInteger`、`RunSession` 的區域覆寫與 HUD／手冊接線、相關測試。三語文案只核對 key 是否存在，未審譯文品質。未修改程式。

**結論：pass；high 0 / med 0 / low 3。** 實體手把與三語排版的人工驗收（README「現在」第 9 條）不在本次範圍。

## 核對結果（對照 `field-utilities.md#主動操縱天氣`）
- 沒有能力 → `MissingSkill`；冷卻中 → `OnCooldown`；兩者都不改覆寫也不改冷卻。成功後冷卻跨區域、跨天氣共用。
- 覆寫只記 `(區域, 天氣, 施放時的時段)`，以時段比對判斷是否到期，不用時間戳相加，所以沒有溢位問題；同時段回到原區域仍有效、離開區域不帶走。到期後回到自然天氣，冷卻不受影響。
- `RunSession.WeatherAt` 先查區域覆寫再查自然排程，`Encounter`（雙方宙獸）與玩家共用同一個 provider，符合「同區域內各地貌及雙方宙獸皆使用覆寫」。成功施放後立刻 `UpdateEnvironment`，手冊暫停時（`_nowMs` 不前進）也會更新。
- 冷卻值從 `balance.json#field_utilities.weather_control_cooldown_ms` 讀取，要求是正的 Int32 整數，不接受 JsonUtility 的型別轉換；覆寫與冷卻不存檔，與規格一致。
- `CooldownRemainingMs` 的 `(int)` 轉型只在差值 < 冷卻值時發生，不會溢位。

## 發現

| ID | 嚴重度 | 位置 | 問題 | 建議 |
|---|---|---|---|---|
| P3-04b-O1 | low | `WorldGen/Api/WeatherControl.cs` `TryApply`／`TryGetOverride` | 時段長度寫死為 `WeatherPlanner.DefaultSlotDurationMs`，但 `WeatherPlanner.WeatherAt` 可以傳入 `slotDurationMs`。若以後有呼叫端用非預設時段，覆寫會在錯的邊界到期。目前所有呼叫端都用預設值。 | 下次動到這裡時，在類別 Pre 寫明「只配合預設時段」，或由建構子傳入時段長度。 |
| P3-04b-O2 | low | 規則本身 | 在時段最後一刻施放，覆寫只維持極短時間，但仍消耗整整 180 秒冷卻。這是已定案的規則；HUD 有顯示距下一個時段的倒數，玩家看得到。 | 列入人工試玩觀察；如果手感不好再回頭討論規則。 |
| P3-04b-O3 | low | `RunSession._weatherControl` | 覆寫與冷卻只存在本機。連線時（P6）要由權威端保存並同步，否則各端的天氣、命中與 CD 會不同。 | P6 實作時處理；記入 `tech-debt.md`。 |

## 驗證
與 P3-12／P3-04a／P1-15 同批的完整測試：`Logs/opus-review-editmode.xml`、`Logs/opus-review-playmode.xml`。
- Unity 6000.3.28f1 batchmode，基準 `95a52ef`：EditMode **3555/3555 通過**；PlayMode **147 通過、0 失敗、8 略過**（略過的都是需要 `COSMIMERA_CAPTURE=1` 的截圖測試，未當作目視驗收）；無 C# 編譯警告或錯誤。
