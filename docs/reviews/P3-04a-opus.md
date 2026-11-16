# P3-04a 當地環境與自然天氣接線 — OPUS 跨廠商審查

2026-11-16 · Claude Opus 5.5（OPUS）· 基準 `95a52ef`，受審變更 `c101461`。範圍：`TemplatePlanner.SurfaceProfile`、`AtmosphereDriver.SetVisibilityRadius`、`RunSession` 的地形／天氣／翻滾／HUD 接線與相關測試。未修改程式。

**結論：pass with debt；high 0 / med 1 / low 3。** med 是規格未寫明的行為，已開 Q-C39，不阻擋結案。人工試玩（README「現在」第 8 條）不在本次範圍。

## 核對結果
- `SurfaceProfile`：每個印章的 profile 預先算好；查詢時用 `TemplateView.TryGetStamp`（強度最高的印章），不配置記憶體；印章外或沒有模板時回星球類型的 profile。火山／洞窟印章走一般地表對應，不會變成進階地城。Pre（unit direction）與 `Encounter` 傳入的 `Up(position)` 一致。
- `Encounter` 的地形移速與天氣 provider 都以地面方向查詢，與 `RunSession` 用同一個 `_nowMs`；角色、宙獸、翻滾、HUD 用同一份 `_playerWeather`。
- 生活能力切換改走 `ApplyPlayerEnvironment`，地形 × 能力 × 天氣只在一處組合，切換能力後不會把天氣或地形修正弄丟。
- `SetVisibilityRadius`：取 `min(radius, 晴天 fog end)`、start 依比例縮放、不會累乘；`OnDestroy` 還原晴天 fog，避免天氣帶回大廳。全專案只有 `Configure`、`SetVisibilityRadius`、`OnDestroy` 寫 fog 距離。初始上限在散佈與遠景設定之後才套用，遠景與水面保持晴天串流距離。
- HUD 翻滾倒數與 `PlayerVitals.TryRoll` 用同一公式（天氣倍率 × 食物修正，取 `RollDurationMs` 下限、無條件進位）。

## 發現

| ID | 嚴重度 | 位置 | 問題 | 建議 |
|---|---|---|---|---|
| P3-04a-O1 | med | `Combat/Runtime/Model/PlayerVitals.cs:129–131`；`Bootstrap/Demo/RunSession.cs` `StepCombat`／`DrawTeamPanel` | 翻滾冷卻在**每次嘗試**時用當下天氣重算，翻滾時沒有固定下來。本 commit 前天氣恆為 Clear，看不出來；現在天氣會因時段、跨印章、P3-04b 覆寫而改變：翻滾後走進倍率較高的天氣，已經轉好的冷卻會再鎖住，HUD 倒數也會往回跳。規格（`planet-world.md#天氣影響`）沒寫冷卻依哪個時間點計算。 | 已開 `open-questions.html` Q-C39。若選「翻滾當下固定」：`TryRoll` 在 Ok 時存下有效冷卻，之後的判斷與 HUD 都讀存下的值（屬 `Combat` 核心，改契約需同步測試）。 |
| P3-04a-O2 | low | `RunSession.WeatherAt` | 天氣依「最強印章的地貌 id」排程，走過印章邊界時天氣（含視野）會瞬間切換。符合「seed × 地貌 × 時段」的規格，但視野跳變可能很明顯。 | 列入人工試玩觀察；需要的話之後在表現層對 fog 距離做短時間內插（不影響規則層）。 |
| P3-04a-O3 | low | `TemplatePlanner.SurfaceProfile` → `TemplateView.TryGetStamp` | 印章歸屬由浮點方向判斷，邊界附近不同後端可能選到不同印章，連帶天氣與移速不同。屬既有 ADR-0007 風險。 | 沿用 `tech-debt.md` Q-T6-R1，不另立案；P6 握手時要把地貌歸屬也納入比對。 |
| P3-04a-O4 | low | `RunSession.UpdateEnvironment` | 每個戰鬥 tick 都呼叫 `SetVisibilityRadius` 和 `SetTerrainModifiers`，即使值沒變。成本可忽略。 | 無須處理。 |

## 驗證
與 P3-12／P3-04b／P1-15 同批的完整測試：`Logs/opus-review-editmode.xml`、`Logs/opus-review-playmode.xml`。
- Unity 6000.3.28f1 batchmode，基準 `95a52ef`：EditMode **3555/3555 通過**；PlayMode **147 通過、0 失敗、8 略過**（略過的都是需要 `COSMIMERA_CAPTURE=1` 的截圖測試，未當作目視驗收）；無 C# 編譯警告或錯誤。
