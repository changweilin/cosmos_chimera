# D0 初始隊伍難度與打包工具 · SOL

2026-11-09 · ChatGPT Sol。基準 `d19d13f` 加既有玩家材質修正。本輪只新增 Editor 探針、Python 打包工具與驗證；未調整正式數值或核心規則。

## 難度證據

既有 `RunSessionSmokeTests` 用三隻成年御三家驗證降落、交戰與造成傷害，未覆蓋首次遊玩的單隻幼年 LV10。

新增 `Bootstrap/Demo/DemoBalanceProbe.cs`，直接使用正式 `Encounter`、`CombatBalance`、`Progression` 與匯入的 D0 `GameDatabase.asset`，沒有另寫傷害公式。共 84 種配對 × 32 seed＝2,688 場。敵方先加入、我方後加入，符合 `RunSession` 的 tick 順序。

條件：R=640 m、平地、我方 LV10 且每場滿血、敵人距離 10 m、第二隻相距 2 m；玩家固定在主力後方 4 m，Free 指令；每場最多 600 s。普通怪為幼年 LV10，菁英為少年 LV13（tier 0），BOSS 為成年 LV19（tier 2）。七條野生進化線等權抽樣，並非星球實際遇敵分布。

| 御三家 | 單普通怪勝場／224 | 雙普通怪勝場／224 | 菁英勝場／224 | BOSS 勝／負／逾時（共 224） |
|---|---:|---:|---:|---:|
| 夜痕仔 `moon_night_pup` | 52（23.2%） | 0 | 69（30.8%） | 32／191／1 |
| 星蹄駒 `star_blaze_foal` | 193（86.2%） | 0 | 92（41.1%） | 32／191／1 |
| 鏡芽雀 `sun_mirror_chick` | 128（57.1%） | 128（57.1%） | 200（89.3%） | 79／133／12 |

重點：夜痕仔對漏輪雛、潮帆鰻、笛風舟的單怪對局均 0/32；星蹄駒對除土系 `earth_pebble_pawn` 外的六種單怪均 32/32，對土系為 1/32。這是相同控制條件下的比較，不是玩家勝率估計。每種配對僅 32 次，0 勝也不能證明任何操作下都不可能勝利。

14 場逾時均已脫戰；最後一次傷害發生於 21.35–52.85 s。600 s 時主力距第一隻敵人約 185–356 m，敵人 `Engaged=false`。玩家固定不跟隨是重要限制，不能將這些逾時判為 AI 死鎖。

本探針沒有成長、收服後編隊、連戰耗血、區域詞綴、地形、手動指令或副手，不能取代完整任務鏈／BOSS 實玩，亦不能宣稱已滿足 P5-07「十屬性勝率差 <10%」。目前可確認的是：初始隊伍與敵人數量值得優先人工驗收；原煙霧測試的成年三隻不能代表這個風險。

建議交 OPUS／SONNET：先比較實際移動指令下的單怪／雙怪與三隻御三家，再提出出生區遭遇密度、補給／恢復及初始招式的調整方案。它們涉及生成或設計定案，本輪沒有擅改。視覺素材仍可沿用幾何佔位。

## 重跑

關閉專案 Editor 後執行（完整 Unity 路徑見 `CLAUDE.md`）：

```text
Unity.exe -batchmode -nographics -projectPath <repo> -executeMethod Cosmimera.EditorTools.DemoBalanceProbe.RunBatch -logFile Logs/sol-demo-balance.log
```

輸出 `Logs/d0-balance.json` 包含逐配對勝負、每場耗時／剩餘 HP、逾時 seed／最後傷害時點／敵人距離及交戰狀態。報告 SHA-256：`cb7cb9c5c73b10aa1727d70469f4f21799dbd43fd3cad86bf46e12d78e3ab497`。最終版重跑 2,688 場後摘要逐位元相同。

## 打包

新增 `tools/demo/package.py`，重用既有 `ReproBuild.BuildAll` 的 IL2CPP 輸出，附操作手冊、地圖署名與可選預覽。要求成功的 IL2CPP build log 與非空、零失敗、零跳過的 EditMode／PlayMode XML，拒絕缺檔、連結、名稱衝突及覆寫既有版本。排除兩種 Unity DoNotShip 資料夾，ZIP 建立後重讀 CRC 並逐檔比對 SHA-256。

版本名、receipt 位置由 CLI 明示，測試數量由 XML 讀取；沒有寫死日期或測試通過數。`build-info.json` 記錄打包時 checkout 與 receipt 雜湊，**不宣稱能證明 binary 與 checkout 相符**。操作者仍須在同一 checkout 建置、測試再打包。

```text
python -B tools/demo/package.py --name COSMIMERA-D0-<version>-win64 --build-log Logs/sol-demo-build.log --editmode Logs/sol-followup-editmode.xml --playmode Logs/sol-demo-playmode.xml --preview Logs/demo-shot.png
```

`tools/tests/test_demo_package.py`：14/14，覆蓋壓縮包、逐檔雜湊、debug 排除、原 player 不變、拒絕覆寫、缺 player／署名、失敗建置／測試、空／跳過測試、保留檔名碰撞與路徑穿越。最後一項在任何檔案讀寫之前拒絕；建置紀錄曾成功但最後失敗時也會拒絕。

## 驗證界線

- Python：171 passed、194 subtests passed（`Logs/sol-followup-python.log`）。宿主 requests 相依版本警告與 pyreadline 結束診斷仍存在。
- EditMode：887/887（`Logs/sol-followup-editmode.xml`）。新增探針編譯成功；既有煙霧測試的三處 UNT0008 warning 未在本輪修改。
- PlayMode／Mono／IL2CPP／跨後端證據沿用 [`D0-demo-package-sol.md`](D0-demo-package-sol.md)；本輪沒有更動 player runtime 行為，未重做硬體或完整通關驗收。
- P7-05 仍缺發行 CI／安裝程式；P5-07 已完成 D0 初始難度基線，完整平衡驗收尚未完成。

本輪以正式工具產出 `Builds/Demo/COSMIMERA-D0-20261109-d19d13f-sol2-win64.zip`（46,734,729 bytes），CRC 與 30 檔 SHA-256 通過；26 個 player 檔與 sol1 逐位元相同，僅手冊與打包 metadata 更新。ZIP SHA-256：`9e7d8b9eb49dbe64474d40ab3fe8e04bbeba31c907fa53eb30d3e62be21ba401`，證據 `Logs/sol-followup-package.log`。可直接執行同名資料夾的 `Cosmimera.exe`。
