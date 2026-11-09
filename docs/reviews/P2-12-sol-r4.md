# P2-12 對抗式複審 · SOL · round 4

2026-11-09 · SOL（ChatGPT Sol）。只複審 `P2-12-sol-r3.md` 的修正及其直接回歸風險；不重審延後項目或其他工作樹差異。

| ID | severity | file:line | finding | evidence | suggested fix |
|---|---|---|---|---|---|
| R3-01 | high（已修正） | `Assets/_Project/Bootstrap/Demo/RunSession.cs:347`、`:370` | 戰外成長在下一場模擬前生效，進行中戰鬥仍延後。 | 原 CLR 收服與 ReachPoi 兩條見證均為 `earned=11 levelAtNextTick=11 afterTick=11`。另測前一 tick 已開戰後，區域獎勵／收服輸入使 XP 達 LV11：後續 tick 與結束戰鬥的 tick 均仍以 LV10 模擬，結束後才套用 LV11。所有 `SetLevel` 呼叫均檢查 `!InCombat` 且單位未倒下。 | 無。 |
| R3-02 | high（已修正） | `Assets/_Project/Bootstrap/Demo/RunSession.cs:337`、`:555` | 區域發獎前記入剛換上場者，保留換下者。 | 見證直接呼叫正式 `SwitchMain`，確認 3 退場、5 實際 Fielded，Active 與 Fielded 一致；同 tick ReachPoi 的 XP 為 `[9,9,18,18,17,9]`，符合 `{3,4,5}` 的分配。另驗證戰外不增記、開新戰清掉舊集合、未上場的 pool／roster 成員不加入。 | 無。 |
| R3-03 | high（已修正） | `Assets/_Project/Bootstrap/Demo/RunSession.cs:757`、`:770` | 補記精英步驟使用該目標的擊倒快照。 | 原 ReachPoi→DefeatWild→DefeatElite→CaptureOne→DefeatBoss 見證為 `[30,30,40,40,50,50]`，符合 A 場精英 `{3,4}`、B 場普通敵 `{5,6}`。另驗證其他 spawn 的快照不會誤選、目標無快照時不借用其他目標、Gone 不丟失既有快照、其餘步驟沿用事件紀錄；實際走完收服與 BOSS／整鏈，再重複 PayQuest 與 tick，沒有重付。BOSS 捷徑只付自身擊倒池與步驟，無跳過步驟或整鏈獎勵。 | 無。 |

**Verdict：pass。** 三項 high 的 CLR 見證均已封住；本輪未發現這些修正引入的新缺陷。R3-04 的 `Encounter.cs:303` Pre 已明載戰鬥外、兩個 Tick 之間的限制，`docs/tech-debt.md` 已記錄；不重提延後的執行期檢查。

實際執行的驗證命令（PowerShell；所有探針及日誌均在 repo 外）：

```powershell
$py = 'C:/Program Files/Blender Foundation/Blender 5.2/5.2/python/bin/python.exe'
$taskTemp = Join-Path $env:TEMP 'cosmimera-p2-12-sol-r4'
dotnet --version
& $py -X utf8 -B (Join-Path $taskTemp 'prepare.py')
& $py -X utf8 -B (Join-Path $taskTemp 'augment.py')
dotnet restore (Join-Path $taskTemp 'flow/FlowProbe.csproj') --ignore-failed-sources --verbosity quiet
dotnet run --project (Join-Path $taskTemp 'flow/FlowProbe.csproj') --no-restore -c Release -p:ProbeSymbols=UNITY_EDITOR 2>&1 | Tee-Object -FilePath (Join-Path $taskTemp 'editor.log')
dotnet run --project (Join-Path $taskTemp 'flow/FlowProbe.csproj') --no-restore -c Release -p:ProbeSymbols=RELEASE_PROBE 2>&1 | Tee-Object -FilePath (Join-Path $taskTemp 'release.log')
& $py -X utf8 -B (Join-Path $taskTemp 'field_probe.py')
dotnet run --project (Join-Path $taskTemp 'flow/FlowProbe.csproj') --no-restore -c Release -p:ProbeSymbols=UNITY_EDITOR 2>&1 | Tee-Object -FilePath (Join-Path $taskTemp 'editor-final.log')
dotnet run --project (Join-Path $taskTemp 'flow/FlowProbe.csproj') --no-restore -c Release -p:ProbeSymbols=RELEASE_PROBE 2>&1 | Tee-Object -FilePath (Join-Path $taskTemp 'release-final.log')
& $py -X utf8 -B (Join-Path $taskTemp 'audit.py')
git diff --check -- docs/reviews/P2-12-sol-r4.md docs/roadmap/phase-2.md
```

- .NET SDK **9.0.304**；restore 成功。初版兩設定各 **174 項斷言**通過；加入正式換人路徑與 Fielded 追蹤後，最終兩設定各 **175 項斷言、exit 0**。三個 R3 見證中，R3-01 有收服／區域兩條路徑。保留首 tick 清場、雙方倒下兩種事件順序、後備成長、跨場收服及戰外全員後備的控制案例。
- 沿用 R3 的 Flow harness，從當前正式 `RunSession.cs` 原樣擷取 `StepCombat`、`OnFaint`、`BeginCombat`、`RecordAppearances`、`Reward`、`PayQuest`、`RecordFor`、`AnyEngaged`、`BossUnit`、`ApplyLevelUps`、`UpdateRegion`、`TryCapture`、`SwitchMain`、`In`。編譯正式 `TeamState`／`Progression`／`QuestTracker`／`RunPlan`／`Contract`；`prepare.py` 保存初始檔案雜湊，`audit.py` 核對擷取內容及本輪寫入範圍。
- 限制：Encounter Tick／事件、SetLevel、Field／Unfield、空間／區域選取、場景與 UI 是 CLR 替身；收服擲骰固定成功。Fielded 替身只追蹤狀態，未模擬 Unity 物理。SetLevel 只追蹤呼叫／Level，本輪未重跑 R3 的正式 Model probe 或 Python balance 測試。最終有 **2 個替身編譯警告**（CS8981 小寫 `math`、CS0649 未賦值 `_vessel`）；初版另有 Position 未賦值警告，加入 Field 追蹤後消失。
- **Unity 不可用：未跑 NUnit、EditMode、PlayMode 或場景驗收，未將作者測試成績列為本輪實跑結果。** CLR 結果不代表 Unity／Mono／IL2CPP 跨後端驗收。
- repo 只新增本報告，並在 `phase-2.md` 追加一行交接；依本次要求未改狀態欄、實作、測試或其他文件。
