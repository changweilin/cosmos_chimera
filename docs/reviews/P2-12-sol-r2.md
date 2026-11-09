# P2-12 對抗式複審 · SOL · round 2

2026-11-09 · SOL（ChatGPT Sol）· 僅複審前報告 R1／R2 的修正及其新增風險。R3–R9 依 D0 規則留在 `docs/tech-debt.md`，本輪不重提；未發現這兩項修正使其惡化。未審查其他工作樹變更。

下表 `Demo/` 相對於 `Assets/_Project/Bootstrap/`。

| ID | severity (high/med/low) | file:line | finding | evidence (concrete witness input) | suggested fix |
|---|---|---|---|---|---|
| R1 | high | `Demo/RunSession.cs:370`、`:663`、`:669` | **已修正。** 雙方倒下事件在非戰鬥狀態下先清除舊紀錄、開始戰鬥並記錄在場者，再處理倒下／發獎。同 tick 開始並結束不會額外發獎；後續戰鬥重置紀錄，既有擊倒快照保留。 | 原見證：六主力 ID 1–6、各 XP=1106，舊紀錄 `{1,2}`、脫戰後在場 `{3,4}`，唯一 LV10 普通敵人首 tick 倒下。CLR 實發 **`[10,10,20,20,10,10]`**，總額 80；空舊紀錄亦相同。空閒下一 tick 不再給獎；換成 `{5,6}` 後下一場實發 **`[10,10,10,10,20,20]`**。用第一隻的快照結算 LV10 收服池 40，實發 **`[5,5,10,10,5,5]`**。另驗同 tick 兩敵倒下只發兩池、玩家／敵人同 tick 倒下的兩種事件順序均排除死者、玩家單獨倒下不發 XP、既有戰鬥保留先前參與者、任務步驟不重付。 | 無；本輪見證通過。 |
| R2 | high | `tools/repro/compare.py:19`、`:99`、`:156`、`tools/tests/test_repro_compare.py:105` | **已修正。** policy b 對兩份 dump 各自驗證每個報告鍵的完整、唯一 k 集合，再比較幾何差異；原假 PASS 路徑均被拒絕，真實 producer 輸出仍 PASS。 | repo 新增四案（空、單筆、同筆重複 2048 次、外來 type／seed）均 **exit 2／NOT COMPARABLE**；原本高度 hash 不同的報告搭配這四案亦全部 exit 2。檢查 **`SAMPLES_PER_ROW == ReproProbe.SampleCount == 2048`** 通過。真實 `Logs/repro-mono.txt`／`Logs/repro-il2cpp.txt` 各 30 列、各 `.samples` 61,440 筆：**exit 0／Q-T6(b) PASS**，沒有假 NOT COMPARABLE。 | 無；本輪見證通過。 |

**Verdict：pass。** R1／R2 原見證均已封住；限定修正範圍內未發現新缺陷。本判定不代表完成 Unity 場景／引擎驗收。

驗證環境及實際命令（PowerShell；暫存檔均在 repo 外）：

```powershell
$py = 'C:/Program Files/Blender Foundation/Blender 5.2/5.2/python/bin/python.exe'
$taskTemp = Join-Path $env:TEMP 'cosmimera-p2-12-sol-r2'
& $py -B -m pytest --version
& $py -B -m pytest tools/tests/test_repro_compare.py -q -p no:cacheprovider --basetemp "$taskTemp/pytest"
& $py -B "$taskTemp/flow_gen.py"
dotnet restore "$taskTemp/FlowProbe.csproj" --ignore-failed-sources --verbosity quiet
dotnet run --project "$taskTemp/FlowProbe.csproj" --no-restore -c Release -p:ProbeSymbols=UNITY_EDITOR
dotnet run --project "$taskTemp/FlowProbe.csproj" --no-restore -c Release -p:ProbeSymbols=RELEASE_PROBE
& $py -B -m unittest discover -s "$taskTemp" -p test_repro_r2.py -v
& $py -B tools/repro/compare.py Logs/repro-mono.txt Logs/repro-il2cpp.txt --policy b
```

- Python **3.13.13** 環境缺 pytest：兩個 pytest 命令均顯示 `No module named pytest`，未宣稱原 pytest 套件整套通過。改用暫存 unittest：**18/18 通過，exit 0**。其中以 AST 載入 repo 四個 witness 的原始輸入與未改動的測試函式本體，提供真實暫存 `Path` 執行；沒有安裝或偽造 pytest。其餘涵蓋完整單列／多列、缺第二列、單側缺樣本、缺 sidecar、額外鍵、負值／越界 k、常數一致性、幾何容差通過／超標、離散內容改變及非有限高度拒絕。
- .NET SDK **9.0.304**；restore 成功。CLR flow probe 從本次 `RunSession.cs` 原樣擷取 `StepCombat`、`OnFaint`、`BeginCombat`、`RecordAppearances`、`Reward`、`PayQuest`、`AnyEngaged`、`BossUnit`，編譯正式 `TeamState`、`Progression`、`QuestTracker`、`RunPlan` 與 Contract 原始檔。兩種編譯各 **8 組流程／45 項斷言通過、exit 0**；額外斷言確認 UNITY_EDITOR 契約啟用、RELEASE_PROBE 契約呼叫剝除。
- Encounter tick／事件、Unity 型別及場景操作採最小替身；JsonUtility 以 System.Text.Json／reflection 替身讀核定 balance。收服檢查使用正式 `Reward` 加擊倒時快照，未執行完整 `TryCapture` 的空間選取／擲骰流程。另靜態確認正式 `RefillField` 會跳過 HP 已倒下者，立即 EndCombat 清除 retirement 不會讓它重新上場。這些是 CLR 接線探針，不能代替 Unity serializer、AI／物理及場景測試。
- 暫存探針初版將 LV10 收服池誤設為 120，斷言失敗；核對 `balance.json` 的核定值 40 後只更正暫存 oracle，重跑通過。其間一次建置因前次探針程序鎖住 apphost 失敗；終止該探針後重跑成功。未為了通過探針修改 repo 實作。
- 真實 policy b 比對：`seaCount`／`planDiscrete` 均 **0/30** 列不同；base 最大差 **0.00217152 m**、surface 最大差 **0.00149727 m**，均在 0.003 m 內。原始 hash 比對顯示 `DIFFERENT`，最後 policy verdict 正確為 **`Q-T6(b) PASS`**。
- **Unity 不可用：未執行 EditMode、PlayMode、重新建置 Mono／IL2CPP probe 或實機遊玩。** 真實輸出比對使用現有 Logs 檔案；未引用作者的 Unity／pytest 成績作為本輪實跑結果。

本輪 repo 寫入僅本報告與 `docs/roadmap/phase-2.md` 的一行交接；既有實作、測試、前報告與其他文件均未修改。
