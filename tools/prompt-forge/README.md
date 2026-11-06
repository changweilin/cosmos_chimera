# Prompt Forge

離線單頁工具：產生交給 ChatGPT 對話版 / Gemini Argon 任務模式的美術任務 prompt。直接用瀏覽器開 `index.html`（不需伺服器）。
規格與交付流程：`docs/design/art-asset-pipeline.md`。

| 檔案 | 內容 | 何時改 |
|---|---|---|
| `catalog.js` | 選項池、權重、原型清單、動作 clip、特效分類 | 加選項、調權重、改動作 |
| `tasks.js` | 各任務的 prompt 模板（產出清單、檔名、JSON 欄位） | 新任務、改交付規格 |
| `forge.js` | UI、隨機、組裝、任務標頭 | 改介面行為 |

## 版本（SemVer）
每個 prompt 第一行是任務標頭 `[COSMIMERA-TASK spec tool catalog template@ver task_id seed]`，交付的 `manifest.json` 必須原樣抄回，所以任何產出都能追溯到產生它的規格版本。

| 版本號 | 位置 | major | minor | patch |
|---|---|---|---|---|
| `spec` | `forge.js` `SPEC` | manifest 結構改變 | — | — |
| `tool` | `forge.js` `TOOL_VERSION` | 任務標頭格式改變 | 新 UI 功能 | 修 bug |
| `catalog` | `catalog.js` `version` | 改名/刪除既有選項 ID、改動作 clip ID 或事件 | 新增選項、調權重 | 改中文措辭 |
| `template` | `tasks.js` 各任務 `version` | 改產出清單、檔名、JSON 欄位 | 新增選填項目 | 改措辭 |

- 每次 bump 都在 `CHANGELOG.md` 記一行。
- catalog major 變動時，瀏覽器內記住的舊選項會自動重抽（`forge.js` `load()`）。
- 動作 clip ID 會被 Unity Animator 引用，改 ID 等於破壞性變更，要同步改 `art-asset-pipeline.md` 與已產出的資產。

## 擴充
- **加選項**：在 `catalog.js` 對應陣列加一筆 → bump catalog minor。
- **加任務**：在 `tasks.js` 的 `TASKS` 加一個物件：`id`、`version`、`zh`、`prefix`（task_id 前綴）、`platform`（建議平台）、`needs`（`character` / `creature` / `either` / `null`）、`drop`（解壓位置）、`options`、`build(ctx)`。`build` 用 `common()` + `deliverySection()` 組字串，確保標頭、平台指示、manifest 規格一致。
- **加概念欄位**：`forge.js` 的 `FIELDS`，並在 `characterConcept()` / `creatureConcept()` 輸出。

## 已知限制
- 對話式 AI 不保證能產出 3D 網格與 FBX 動畫；模板把它們列為 L2，必交的是三視圖、骨架規格與 Blender 腳本（Q-T3）。
- ChatGPT 生成的圖片不一定能被它的 Python 環境打包；manifest 會列出檔名讓你手動存放。
