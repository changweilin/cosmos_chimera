# D0 demo 建置與畫面驗收 · SOL

2026-11-09 · ChatGPT Sol（Codex）。基準 `d19d13f`，加本輪 `Bootstrap/Demo/RunSession.cs` 的玩家材質修正。範圍為本機 Windows demo 候選包，非發行驗收。

## 修正

獨立 IL2CPP 版實際降落晶礦星（seed `1191574379`）時，玩家膠囊整體呈粉紅色。`PlanetRig` 動態建立的 primitive 使用內建預設材質，D0 未替玩家指定 URP 材質。既有無圖形測試仍全數通過，沒有涵蓋此可見問題。

僅在 D0 `RunSession` 接線現有 `PlanetLook.CreatureMaterial`：玩家改用白色卡通描邊材質，session 結束時釋放配置的材質。實作 diff 為 9 行新增；不增加公開 API 或更動生成／戰鬥規則。修正前候選包已撤下，避免誤用。

另記錄 D0-U2：V 同時輪換收服道具與切換視角。依 D0 規則延後，在操作手冊揭露。

## 自動驗證

| 驗證 | 本輪結果 | 證據 |
|---|---|---|
| Python 工具／平衡 | 157 passed、194 subtests passed | `Logs/sol-demo-python.log` |
| Demo 資料 | 0 errors、0 warnings | `python tools/validate_data.py --root Data/_demo` |
| 修正後 EditMode | 887/887，exit 0 | `Logs/sol-demo-editmode.xml`、`.log` |
| 修正後 PlayMode（GPU） | 62/62，exit 0 | `Logs/sol-demo-playmode.xml`、`.log` |
| GPU 戰鬥截圖 | 已目視：白色玩家、屬性色宙獸、地形與散佈正常，無粉紅玩家 | `Logs/demo-shot.png` |
| 最終 Windows 建置 | Mono、IL2CPP 各 Succeeded、0 errors，Editor exit 0 | `Logs/sol-demo-build.log` |
| 最終跨後端探針 | 30/30 離散內容相同；每欄 61,440 樣本，高度最大差 2.17152 mm，policy b PASS | `Logs/sol-demo-repro-compare.log`、`sol-demo-{mono,il2cpp}.txt` 與 `.samples` |
| 壓縮包完整性 | ZIP CRC 與 30 個內容檔 SHA-256 逐檔比對通過；連同 SHA256SUMS 共 31 檔 | `Logs/sol-demo-package.log` |

實際使用既有測試、建置與探針入口，未新增測試替身：

```text
python -X utf8 -B -m pytest tools/tests tools/balance -q --tb=short --basetemp=Logs/sol-demo-pytest
python -X utf8 -B tools/validate_data.py --root Data/_demo
Unity.exe -batchmode -nographics -projectPath <repo> -runTests -testPlatform EditMode -testResults Logs/sol-demo-editmode.xml -logFile Logs/sol-demo-editmode.log
COSMIMERA_CAPTURE=1
Unity.exe -batchmode -projectPath <repo> -runTests -testPlatform PlayMode -testResults Logs/sol-demo-playmode.xml -logFile Logs/sol-demo-playmode.log
Unity.exe -batchmode -nographics -projectPath <repo> -executeMethod Cosmimera.EditorTools.ReproBuild.BuildAll -logFile Logs/sol-demo-build.log
```

Python 首次因沙箱 TEMP 權限產生大量失敗；將 TEMP／TMP 改為本次 `Logs/sol-demo-temp` 後重跑通過。Python 全程仍有宿主 requests 相依版本警告與 pyreadline 關閉診斷，未宣稱工具輸出零警告。Unity 在沙箱內無法連接既有授權服務，改在沙箱外執行後成功。

## 獨立版畫面驗證

以 computer-use 操作實際 IL2CPP player：首次選擇夜宸仔 LV10、進入大廳與選星畫面、降落；關閉後由最終包重開，仍讀到同一隻 LV10 御三家。最終包降落翠綠星（seed `2234878700`），玩家已呈白色卡通材質，地形、樹木、岩石與 HUD 可見。`Logs/sol-demo-ui-final.log` 無 managed exception／shader error 記錄。

自動注入 V／Esc 後未觀察到可確認的遊戲反應，不能據此判定實體鍵盤正常或故障；放棄提示／結算返回及鍵盤手感未完成實機驗收。以視窗關閉按鈕正常退出，並移除本輪建立的單一測試存檔，恢復測試前無存檔的狀態；測試存檔副本保留於 `Logs/sol-demo-lobby-test.json`。

## 交付與驗收界線

本機交付：`Builds/Demo/COSMIMERA-D0-20261109-d19d13f-sol1-win64.zip`（46,734,023 bytes），同名資料夾內可直接執行 `Cosmimera.exe`。包含操作手冊、既有地形署名、GPU 截圖、版本與原始碼修正雜湊；排除 Unity 的兩類 DoNotShip 偵錯資料夾。ZIP SHA-256：`b5e3e484b3d60e5ead2f454e9a6ce2723aad0d735b07a2d1d625a544eede0113`。

Unity 建置自動更新的三個 URP 資產及 PlayerSettings 已還原至本輪開始的版本；未將產生的測試資源、Python 快取或建置二進位納入原始碼變更。

操作及展示流程見 [`../demo-runbook.md`](../demo-runbook.md)。完整任務鏈／BOSS 通關、難度、輸入手感、第二台 Windows 與 Steam Deck 均需後續實機驗收。P7-05 尚缺 CI 發行管線及安裝程式，維持進行中；P1-07 仍待人工美術驗收。
