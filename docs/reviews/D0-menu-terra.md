# D0 選單與日文接線 · TERRA

2026-11-09 · ChatGPT Terra（Codex）

## 變更與取捨

- 新增 `DemoMenu` action map，與 `DemoRun` 分開 clone 管理，避免選單與探索的按鍵及啟停互相影響；不沿用含指標、點擊與追蹤設備動作的通用 `UI` map。大廳、選星及結算可用鍵盤或手把逐項導航、確認及返回；滑鼠仍可點擊。隊伍清單隨焦點捲動，按鈕數變動時夾住焦點。
- 大廳語言按鈕循環切換繁中、英文、日文；本次不寫入存檔。`DemoCopy` 逐欄以英文補缺少的翻譯，現有宙獸／招式名稱無日文時顯示英文。三語手把提示和語言按鈕字串已 staging，日文新增／變更的兩鍵交 FLASH 複核。
- `merge.py` 在寫出前檢查日文結構、衝突和重複鍵，仍就地更新 GeoTemplates；12 組共 36 個資產的 `.meta` 均保留。
- `DemoRunInputTests` 原先假設共用 map 一定停用，但 `EditorBuildSettings` 將同一 action asset 設為 Input System project-wide actions；測試現先停用並驗證，再還原原狀。原有 PlayMode 記錄使用 Unity 物件 null 傳播引發的三個 UNT0008 編譯警告也已修正。

## 驗證

- GPU PlayMode：63/63（`Logs/terra-menu-accept-play-gpu.xml`），包含合成 Gamepad 在大廳移動焦點、確認語言，以及選單 map 停用／重啟。
- 無圖形與 GPU EditMode：各 898/898（`Logs/terra-menu-accept-edit-nographics.xml`、`Logs/terra-menu-accept-edit-gpu.xml`）。純 C# 焦點的空清單、環繞、數量變化，及 `DemoCopy` 日文逐項 fallback 均有測試。
- 三次 Unity 驗收的編譯警告數為 0；未留下 `InitTestScene` 測試場景或 ProjectSettings／PC_RPAsset 遷移 diff。
- Python：175 passed、194 subtests；日文檢查 164/164；`Data/_demo` 0 error、0 warning。

## 未做

實體手把、Steam Deck／Proton、遊戲內重綁及日文畫面字型與排版仍待人工驗收；本次未重新打包 demo。
