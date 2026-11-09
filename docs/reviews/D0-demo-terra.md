# D0 操作與任務引導 · TERRA

2026-11-09 · ChatGPT Terra（Codex）。基準 `86e591d` 加本輪工作區修改；依使用者要求優先 demo，其次系統完整性，內容擴量及美術後置。

## 本輪結果

- 修正 D0-U2：B 輪換收服道具，V 保留第一／第三人稱切換。
- 任務 HUD 顯示相對攝影機朝向的球面轉向與距離。沿用 `PlanetFrame` 的切平面投影，避開尚未定案的地理東西方向 Q-T5；不是路徑規劃或避障。
- 區域步驟導向區域中心；討伐導向實際存活且符合條件的目標；收服優先導向尚在時限內的倒地目標，否則導向存活野生宙獸。沒有符合條件的目標時顯示明確提示。
- 8 m 內可收服目標顯示名稱、剩餘秒數及 C／B 操作。重用原有最近目標選取，補齊與既有消失時限相同的檢查：20 秒整仍可收服，超過後拒絕。
- 常駐基本操作提示，隊伍說明改為前 2 隻開場、前 4 隻可輪替、第五／六隻待命。
- 九個新增 UI 鍵及隊伍說明同步繁中／英／日初稿；繁中與英文已經 staging，日文仍未接入執行期，待 FLASH 交叉審核。

未改正式數值、核心規則層、生成版本、存檔格式或美術素材。Unity 建置自動重寫的渲染／專案設定已還原，避免混入無關 diff。

## 驗證

| 驗證 | 結果／證據 |
|---|---|
| Python 工具與數值測試 | 171 passed、194 subtests passed；`Logs/terra-python.log` |
| D0 資料 | `python -B tools/validate_data.py --root Data/_demo`：0 error、0 warning |
| 文案檢查 | Terra 六檔通過；ja 六檔共 160 個本地化欄位通過，包含數字及佔位符對齊 |
| EditMode | 895/895；`Logs/terra-demo-editmode.xml`，新增方向測試覆蓋南北極、cube 面交界、鏡頭俯仰、跨地平線與無唯一方向的退化情形 |
| GPU PlayMode | 62/62；`Logs/terra-demo-playmode.xml`，煙霧測試包含降落、任務區域目標、B／V 合成鍵盤事件互斥、交戰與傷害 |
| Windows Mono／IL2CPP | 均 `Succeeded, 0 errors`；`Logs/terra-demo-build.log` |
| 獨立版啟動／跨後端 | 新建兩個 player 執行 `-reproProbe` 均 exit 0；`tools/repro/compare.py ... --policy b` PASS，30 列離散內容一致，最大基礎高度差 2.172 mm、表面高度差 1.498 mm |
| 打包 | 既有 `tools/demo/package.py` 的 ZIP CRC、逐檔 SHA-256 與必要檔案檢查通過；`Logs/terra-demo-package.log` |

PlayMode 測試端明確設定隱藏 Game view 的輸入路由，結束時還原；第一輪失敗是合成事件未送入 Game view，修正限於測試端。既有三處 UNT0008 warning 未改。Python 的宿主 requests 相依警告與 pyreadline 結束診斷仍存在。

## Demo 包

`Builds/Demo/COSMIMERA-D0-20261109-86e591d-terra1-win64.zip`（46,692,232 bytes）。解壓整個資料夾後啟動 `Cosmimera.exe`，操作見 `docs/demo-runbook.md`。

SHA-256：`31e1adf63d047e5d04737b14ae8dc03fa313367fe16300b6256b60771b379cef`。

含操作手冊、地圖署名、build-info、逐檔雜湊；本機候選包不代表發行 CI 或安裝程式已完成。

## 驗收界線與下一步

畫面自動化啟動核准逾時；後續本機隱藏啟動無可操作視窗，未完成獨立版 HUD 目視驗收。測試程序已結束，未選御三家或寫入大廳存檔。完整任務鏈／BOSS、收服倒數的實際畫面、低解析度排版及輸入手感仍待人工試玩；自動測試與生成探針不能代替它們。

後續仍依 demo 優先：人工實玩檢查初始單隻隊伍的難度與遭遇密度，交 OPUS／SONNET 決定並接續恢復、玩家受擊、副手等核心缺口。既有難度證據見 `D0-playability-sol.md`，其餘系統缺口在 `../tech-debt.md`。宙獸／角色內容、畫面與 3D 製作維持後置。
