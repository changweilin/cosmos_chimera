# D0-U2 採集／製作與穿戴 — OPUS 跨廠商審查

2026-11-16 · Claude Opus 5.5（OPUS）· 基準 `28e4991`，受審變更 `9e0bcf2`（採集、配方、裝備、存檔 v7）、`654dd99`（大廳裝備捷徑、副手穿戴、紙娃娃預覽）。範圍：`Crafting/Api`（`CraftInventory`、`CraftRules`、`CraftGathering`、`EquipmentLoadout`、`EquipmentRules`、`EquipmentSpec`）、`LobbySave` v7、`LobbyTransactions.Settle` 的背包取代、`RunSession` 的出發複製／採集／製作器皿／裝備效果接線、`DemoGame.Crafting` 的換裝交易。`PaperdollPreview` 只看 `Dispose` 釋放 `RenderTexture` 與舞台，三語文案只核對 key。未修改程式。

**結論：pass with debt；high 0 / med 2 / low 2。** 人工試玩（README「現在」第 11 條）不在本次範圍。

## 核對結果（對照 `design/crafting.md`）
- **原子交換**：`CraftInventory.Plan` 先加總重複 id，再以 `long` 檢查持有量與 `int.MaxValue` 容量；成本不能由同次產出支付。拒絕時任何數量都不變。`CraftRules.TryCraft` 依序檢查技能 → 工具（只檢查持有、不扣）→ 材料 → 容量。
- **採集**：同一節點每場只成功一次；滿背包回 `CapacityExceeded` 且不耗掉節點；礦鎬可代替採礦資格且不消耗；季節以 `Seasons.For(seed)` 決定。不改散佈、網格與碰撞。
- **製作器皿**：只有找得到收服目標時才扣；成功或失敗都扣一個；切換器皿取消選用；扣除失敗時不擲骰。
- **裝備**：裝上時從背包扣一份，換下的舊裝備放回背包，容量不足整次拒絕；退役（目錄裡找不到）的裝備保留在欄位、不給加成、仍可卸下。玩家加成套在 HP／翻滾冷卻／移速，速度與冷卻下限 −90%，翻滾冷卻不短於翻滾時間。宙獸效果走 `SetEquipmentEffects` 獨立層，`EquipmentSpec` 只接受持續型修正、不疊層。
- **存檔 v7**：`craft_stock` 與 `equipment` 只在 `version >= 7` 讀取；物品 id 只驗格式（保留退役 id）、數量為正、不重複；裝備擁有者必須是 0（玩家）或已擁有宙獸、欄位合法且每個擁有者每欄一件。`ToJson` 拒絕寫出擁有者不存在的裝備。
- **結算**：出發時複製背包，結算以最終背包**取代**大廳背包（不是加回），勝利、失敗、放棄都會帶回；存檔寫入成功才換掉大廳狀態。

## 發現

| ID | 嚴重度 | 位置 | 問題 | 建議 |
|---|---|---|---|---|
| D0-U2-O1 | med | `RunSession.Journal.cs` `AddEquippedPartyCreature`（`654dd99`） | 副手身上的裝備效果會加到主力上（上限 32 個，超過的靜默丟棄）。`crafting.md#穿戴與數值` 只寫「本輪介面以玩家與目前主力為穿戴對象」，沒有「副手裝備轉給主力」這條規則，commit 也沒有更新規格。這等於一隻主力最多可以吃到 5 套裝備（自己＋4 副手），數值影響大；超過 32 個時哪些被丟掉取決於副手順序。 | Q-C40 已定案：副手裝備以 25% 生效（`crafting.md#穿戴與數值`）；取整、合併與上限待 Q-C42，定案後再改程式。 |
| D0-U2-O2 | med | `Crafting/Api/EquipmentRules.Collect`；呼叫端 `DemoGame.Crafting.DrawWearerBonusSummary`、`RunSession` 出發 | 存檔裡的裝備若在資料更新後變成「欄位或穿戴者不符」，`Collect` 丟 `InvalidDataException`。大廳紙娃娃每個 OnGUI 都呼叫 `Collect`，出發時也呼叫，兩處都沒有接住：玩家打不開裝備頁（無法卸下）、也無法出發，等於存檔卡死。退役 id 有容錯，但「改欄位／改穿戴對象」沒有。 | `Collect` 對不符的已知物品比照退役處理（不給加成、保留在欄位、可卸下），或讀檔時把不符的裝備退回背包；兩者都要補測試。 |
| D0-U2-O3 | low | `Crafting/Api/EquipmentRules.Collect` | 單一穿戴者三件裝備效果合計超過 32 個時丟例外；每件資料上限 32，所以三件合計可以合法地超過。目前資料最多 2 個，實際不會發生。 | 資料驗證（`tools/validate_data.py`）檢查「同穿戴者三欄最大組合 ≤ 32」，或把每件上限改成 10。 |
| D0-U2-O4 | low | `Crafting/Api/CraftRules.TryConsumeCaptureVessel`（`f4acede` 新增） | 與 `RunSession.TryConsumeCraftedVessel` 功能重複，目前沒有呼叫端；Post 寫「stock ≥ 1 才成功」，但還多了 `item.Category == "capture"` 的 Pre。兩條路徑以後可能分歧。 | 接線時讓 `RunSession` 改用 `CraftRules.TryConsumeCaptureVessel`，刪掉 glue 裡的重複邏輯。 |

## 驗證
本批（D0-U1、D0-U2、收服器皿／星球屬性）共同的完整測試，基準 `28e4991`：

- Unity 6000.3.28f1 batchmode：EditMode **3571/3571 通過**；PlayMode **144 通過、4 失敗、8 略過**（略過的是需 `COSMIMERA_CAPTURE=1` 的截圖測試）；無 C# 編譯警告或錯誤。
- 4 個失敗都在 `RunSessionProgressionTests`，來自 `b0954b0`／`28e4991`，不是 D0-U1／D0-U2 的測試（`JournalPlayTests`、`DemoMenuPlayTests`、`LobbyTransactionsTests` 全過）；細節見 `capture-elements-opus.md` CE-H1。結果檔 `Logs/opus-r2-editmode.xml`、`Logs/opus-r2-playmode.xml`。
