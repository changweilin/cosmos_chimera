# ADR-0004 · 遊戲資料匯入：GameData 組件與 Newtonsoft.Json

## 狀態
提議（2026-11-06，Claude Opus 於 P0-06 實作；待使用者確認）

## 背景
P0-06 要求「改 JSON 後重新匯入，registry 可依 id 查詢」。資料流（`overview.md#資料流`）是 `Data/*.json → importer(Editor) → Assets/_Project/Data/*.asset → registry`。有兩個問題原本沒有定案：
1. **定義型別放哪個組件。** `CreatureDef` 要引用 `MoveDef`（招牌招式）和 `TraitDef`。如果把它們分別放進 Creatures 和 Combat 的 `Api/`，Creatures 就得引用 Combat，但這兩個 feature 在依賴圖上是同一層，不能互相引用。
2. **JSON 解析器。** `JsonUtility` 不支援頂層陣列和字典（`resist`、`mastery`），也無法保留 `params` 這種自由形狀的物件。

## 決定
1. **新增 `Cosmimera.GameData` 組件**（`Assets/_Project/GameData/`），只引用 Core。內容包括：`GameDef` 基底（id）、`MoveDef`、`TraitDef`、`CreatureDef`、`PlanetTypeDef`、`RuleDef`、`RelationEdge`/`RelationLink`、各 enum，以及 registry `GameDatabase`。
   - 允許引用它的組件：WorldGen、Creatures、Combat、Capture、Lobby、Codex、Net、Save。**Planet 和 Player 不允許**，以維持「Planet 不知道宙獸存在」這條規則。白名單寫在 `ArchitectureTests`。
   - 所有 setter 都是 `internal`，只對 `Cosmimera.GameData.Editor`（importer）和測試開 `InternalsVisibleTo`。執行期程式碼只能讀，不能改。
   - trait／effect 的 `params` 先以壓縮 JSON 字串存放（`RuleDef.ParamsJson`）。P2 收斂 enum 時，再由擁有該型別的 feature 轉成具型別的資料。
2. **Importer** 是 `Cosmimera.GameData.Editor.DataImporter`，有選單（`Cosmimera/Data/Import All`）和 batchmode 入口（`-executeMethod Cosmimera.GameData.Editor.DataImporter.ImportAllBatch [-dataRoot DIR]`）。
   - 分兩階段：先解析並解析所有引用，任何錯誤都會**在寫入任何資產前**中止，並一次列出所有錯誤。
   - 每個 id 對應一個資產，路徑為 `Assets/_Project/Data/<Kind>/<id>.asset`。已存在的資產直接覆寫內容，**GUID 不變**，所以場景和 prefab 的引用不會斷。id 從來源資料消失時，刪除對應資產。
   - 不取代 `tools/validate-data`。importer 只重新檢查建立引用時需要的部分（id 唯一、引用存在、enum、浮點字面值、未知欄位、resist 十鍵、邊 a<b）。
3. **JSON 解析使用 `com.unity.nuget.newtonsoft-json` 3.2.2**（Unity 官方套件，MIT）。它原本已經是 URP／Test Framework 的遞移依賴，所以這次只是在 `manifest.json` 明確釘選，**沒有新增下載**。只有 Editor 組件以 `precompiledReferences` 明確引用，執行期組件不依賴它。

## 後果
- 新增依賴邊或新 feature 時，需要同步更新 `overview.md` 和 `ArchitectureTests`（與 ADR-0003 相同）。
- 產生的 `.asset` 會納入版控（它們是 `Assets/` 下的資產），但**不准手改**，重新匯入就會被覆寫。
- `GameDatabase` 目前只是一個 ScriptableObject 資產；執行期如何注入（Bootstrap 組合根）留給第一個使用它的 feature（P2）。

## 替代方案
- **定義型別放在各 feature 的 Api**：會造成同層 feature 互相引用，或必須到處用字串 id 而不是資產引用，失去編輯器內的引用完整性。
- **自寫 JSON 解析器**：大約 300 行而且要自己維護，不值得，因為現成的官方套件已經在專案裡。
- **ScriptedImporter 直接匯入 `.json`**：`Data/` 位於 repo 根目錄（Python 工具與 AI 批次都使用它），不在 `Assets/` 下，Unity 不會自動匯入。如果搬進 `Assets/`，資料管線會被綁在 Unity 上。
