# D0 戰鬥 action map 與手把 · TERRA

2026-11-09 · ChatGPT Terra（Codex），延續 `D0-demo-terra.md` 的工作區修改。

## 變更

`RunSession` 不再直接讀取 `Keyboard.current`。十個操作移至既有 `InputSystem_Actions.inputactions` 新增的 `DemoRun` map，保留鍵盤配置並提供手把綁定，詳見 `../demo-runbook.md#探索中使用手把`。

每場探索 clone 自己的 map，停用時 disable、重啟時 enable、銷毀時 dispose。Unity 的專案全域 action asset 可能已自動啟用，因此探索保留共享 map 的原有狀態。這能避免結束一場探索時停用其他輸入使用者。

**契約變更**：`RunSession.Configure` 的 actions 必須包含 `DemoRun` map 及十個必需 action。配置前以 Contract 檢查，既有 `DemoGame` 與煙霧測試皆使用已更新的資產。

HUD 在偵測到手把時顯示手把 binding 名稱與操作說明；收服、任務完成、放棄確認與指令提示使用 binding 佔位符。繁中／英文已 staging，日文僅初稿，未新增宙獸／角色內容或美術。

## 驗證

- EditMode **896/896**：`Logs/terra2-editmode.xml`，包含缺少 action asset／map／必要 action 的契約檢查。
- GPU PlayMode **62/62**：`Logs/terra2-playmode.xml`。既有降落、交戰、造成傷害測試保留；新增合成 Gamepad 驗證道具切換、左右選主力、RB 輪替、跟隨／撤退／自由、停用後不處理與重啟後恢復，以及共享 map 狀態不變。鍵盤 B／V 互斥仍通過。
- Demo staging 工具：4/4；D0 資料驗證 0 error、0 warning。Python 工具／數值邏輯未修改，完整 171 項＋194 子案例的前輪證據見 `D0-demo-terra.md`。
- 本地化：Terra 六檔通過；ja 六檔 **163** 個欄位的結構、數字與佔位符檢查通過。
- Windows Mono／IL2CPP：均 `Succeeded, 0 errors`，`Logs/terra2-build.log`。新版 IL2CPP 的內建生成探針 exit 0，與前輪 IL2CPP 的 30 列摘要及兩組各 61,440 個高度樣本逐位元相同。

批次 Game view 的 ScreenCapture 沒有輸出 HUD 圖片；已移除該擷取步驟。上述測試證明合成輸入與程式接線，不能代替實體手把、排版或 Steam Deck／Proton 驗收。既有三處 UNT0008 warning 與 Python 宿主診斷仍未改動。

## Demo 包

`Builds/Demo/COSMIMERA-D0-20261109-86e591d-terra2-win64.zip`（46,693,716 bytes），含更新後手冊、署名、版本資訊與逐檔雜湊；既有打包工具的 CRC／SHA-256 驗證通過，證據 `Logs/terra2-package.log`。

ZIP SHA-256：`b12fe8780ecdbab035128e97d07f9a8fd411ae60de9cbedc2d7f726af91348e6`。前一版候選包保留。Unity 自動重寫的渲染／專案設定已還原。

## 後續範圍

P7-07 維持進行中：大廳、選星與結算仍需鍵鼠，尚無遊戲內重綁，也尚未做 Steam Deck Verified 實機檢查。完整任務鏈／BOSS、初始難度與 HUD 目視仍待驗收；正式數值、核心規則與模型素材均未更動。
