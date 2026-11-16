# D0-U1 冒險手冊：副手存檔 v5、副手經驗與 API 接線 — OPUS 跨廠商審查

2026-11-16 · Claude Opus 5.5（OPUS）· 基準 `28e4991`，受審變更 `b6499cc`（Codex）。範圍：`Save/Api/LobbySave` 的 `partners`（v5）讀寫與驗證、`Combat` 的 `EffectBook.AddPartnerTrait`／`Encounter.AddEquippedCreature`／`RelocateParty`／`PlayerVitals.TryRoll` 料理冷卻、`RunSession` 副手出發與經驗結算、`LobbyTransactions.Settle` 的 `PartnerExp`。手冊 IMGUI 排版、三語文案與 `DataImporter` 欄位只核對接線，未審譯文與版面。未修改程式。

**結論：pass；high 0 / med 0 / low 3。** 實體手把與 Steam Deck 操作的人工驗收（README「現在」第 6 條）不在本次範圍。

## 核對結果（對照 `creatures.md#隊伍規則`、`level-progression.md#獎勵量`、`lobby-meta.md`）
- **存檔 v5**：`partners` 只在 `version >= 5` 讀取；主力必須在隊伍、副手必須已擁有且不在隊伍、副手不重複、每隻主力最多 4 隻，違反時在任何建置都丟 `InvalidDataException`。v1–v4 讀成未裝備。`SetParty` 移除主力時一併釋放其副手；副手不能直接升為主力。`Clone` 複製 `_partners`。
- **出發驗證**：`DemoGame.TryBuildPartners` 每次出發都以 `PartnerLoadout.TryAssign` 重新檢查關係與「副手階段 ≤ 主力階段」，所以存檔中因進化而失效的配對不會進場；物種缺資料時整組拒絕。
- **副手經驗**：`RunSession.Reward` 把副手當成 `XpRecipient`（不會陣亡、不計出場），走同一個 `Progression.Settle`，主力實得之外額外發放；`_playerExp` 只累加主力 `Paid`，副手份額不計入玩家經驗。`Settle` 在套用前檢查每個 `PartnerExp` 鍵都是出發主力的副手、數值不倒退且不超過 `MaxExp`。
- **副手特性**：每個玻璃珠副手的特性有自己的 `EffectBook`（獨立觸發與充能），所有通知（召喚、換入、受擊、出招、HP 變化）、淨化與脫戰清理都轉發；同 id 特性只掛一次；只能在戰鬥時間開始前加入（`_watermarkMs == 0`）。
- **符籙卡招式**：`AddUnit` 依 `1 + partnerMoves.Count` 建槽，最多 5 槽，與 `MoveCooldowns.MaxSlots` 一致。
- **翻滾冷卻**：料理修正限 −100..100，結果不短於翻滾持續時間。

## 發現

| ID | 嚴重度 | 位置 | 問題 | 建議 |
|---|---|---|---|---|
| D0-U1-O1 | low | `Save/Api/LobbySave.cs` `FromJson` 的 `<remarks>` | 摘要仍寫「migrating formats 1–4」，Post 只列到 v5；v6 蛋、v7 背包與裝備、v8 `visual_vessel` 會拒絕哪些輸入沒有寫進契約（程式有檢查）。 | 下次動到存檔時補齊 Post：v6 蛋數／進度範圍、v7 物品 id／重複堆疊／裝備欄位與擁有者、v8 未定義的 `visual_vessel`。 |
| D0-U1-O2 | low | `Combat/Runtime/Model/PlayerVitals.cs` `TryRoll` | 冷卻用 `float` 天氣倍率 × `double` 料理倍率後取 ceiling。單機沒有問題，但規則層其他戰鬥數值都是整數；P6 若由權威端計算、客戶端預測，不同後端可能在邊界差 1 ms。 | P6 改成整數千分比（天氣倍率已在 `WeatherProfile` 有固定表，可存成 permille）。 |
| D0-U1-O3 | low | `Combat/Runtime/Model/Effects.cs` `AddPartnerTrait` | 兩個副手帶同一個特性時，第二個靜默忽略（主力天生特性與副手相同時也一樣）。規格沒有寫同名特性能否疊加；目前行為與「每個效果只加一次」的精神一致，但玩家在手冊看不到第二隻沒有作用。 | 人工試玩時確認；若要保留此規則，在手冊副手頁標示「特性重複，不生效」。 |

## 驗證
見本批共同結果（`D0-U2-opus.md` 末段）。
