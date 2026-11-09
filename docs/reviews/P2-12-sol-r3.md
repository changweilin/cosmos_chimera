# P2-12 對抗式複審 · SOL · round 3

2026-11-09 · SOL（ChatGPT Sol）。基準為 `P2-12-sol-r2.md` 已審狀態；本輪只審 Q-C31 的 `Rescale`／`SetLevel`、戰後套用、任務紀錄傳遞及兩項新增測試。未重審其他工作樹差異或既有技術債。high 依 D0 定義為錯誤結果或阻塞遊玩；low 延後。

| ID | severity | file:line | finding | evidence | suggested fix |
|---|---|---|---|---|---|
| R3-01 | high | `Assets/_Project/Bootstrap/Demo/RunSession.cs:343`、`:365`、`:717` | **戰外取得的升級會延後至下一場戰鬥結束。** 唯一套用點在 `Encounter.Tick` 之後；該 tick 若開始新戰鬥，`ApplyLevelUps` 直接返回，已在戰外取得的能力成長整場不生效。 | CLR：六主力 XP=1261、LV10；上一場已結束，成功收服舊倒下者使主力達 LV11；下一 tick 有另一敵人交戰。實際輸出 `earned=11 levelAtNextTick=10 afterTick=10 InCombat=True`。戰外 ReachPoi 完成後同 tick 開戰亦重現。無新戰鬥的對照會在下一 tick 升級。正式能力公式確認基準 `(25,30,30,15)` 的 LV10→11 使 STR／SPI 由 5→6，1500 ms 基礎 CD 由 1458→1451 ms，故會影響戰鬥結果。 | 戰外獎勵完成後，在下一次 `Encounter.Tick` 前套用待結算升級；區域事件發獎也須涵蓋。保留進行中戰鬥的延後結算。 |
| R3-02 | high | `Assets/_Project/Bootstrap/Demo/RunSession.cs:336`、`:350`、`:662` | **戰鬥中換人後立即完成區域任務，漏算剛上場者。** 輸入換人先於 `StepCombat`；`UpdateRegion` 發獎又先於 `RecordAppearances`，所以傳入的是前一 tick 的出場集合。 | CLR：ID 1–6 全存活，戰鬥紀錄 `{3,4}`；成功換 `3→5` 後同 tick 抵達 ReachPoi。步驟池 80，應保留換下者並加入新成員 `{3,4,5}`，實際 XP `[10,10,20,20,10,10]`，應為 `[9,9,18,18,17,9]`。這只需一次合法換人，不依賴連續換人。 | 在戰鬥中的區域事件發獎前記錄當前 Active，或在成功上場／換人時立即加入出場集合；不可刪除換下者。 |
| R3-03 | high | `Assets/_Project/Bootstrap/Demo/RunSession.cs:405`、`:736`、`:751` | **預先擊倒的精英，其補記任務獎勵被分給後續事件的戰鬥成員。** `QuestTracker.Advance` 可連續完成當前步驟及已擊倒精英步驟；新 `PayQuest(appeared)` 卻對所有新完成步驟共用同一集合，忽略精英既有擊倒快照。 | CLR 使用正式任務順序 ReachPoi→DefeatWild→DefeatElite→CaptureOne→DefeatBoss：A 場 `{3,4}` 先擊倒目標精英，當時尚未抵達 POI；抵達後，B 場 `{5,6}` 完成 DefeatWild，並自動補記精英。B 場普通擊倒池 80＋兩個步驟各 80，實發 `[30,30,30,30,60,60]`，依兩場各自紀錄應為 `[30,30,40,40,50,50]`。精英快照仍正確保存 `{3,4}`，錯誤發生在選取分配紀錄。`QuestTracker.cs:131` 的補記行為沿用既有 Q-E1，本輪缺陷在新增 Q-C31 接線。 | 每個新完成步驟分別選取來源紀錄；補記 DefeatElite 必須使用該 `TargetSpawn` 的擊倒快照，其餘步驟才用本次事件紀錄。 |
| R3-04 | low（延後） | `Assets/_Project/Features/Combat/Runtime/Model/Encounter.cs:303` | **`SetLevel` 的 DbC 未列出戰鬥生命週期限制。** summary 承諾 between battles，但 Pre 只有索引、存活與等級範圍；符合所列 Pre 的呼叫者仍可在交戰中改能力值。 | 契約啟用的正式 Encounter CLR 探針：`Tick` 後仍有存活、Engaged 的敵人，直接 `SetLevel(playerId,11)` 成功。現有 `ApplyLevelUps` 呼叫端有 `InCombat` gate，因此此列只算公開契約缺口。 | 在 Pre 明載呼叫者必須處於戰鬥結束後／戰鬥外，且不得於 tick 中套用；由持有戰鬥狀態的呼叫端維護此保證。 |

**Verdict：fail。** 3 high 尚未封住；1 low 依 D0 延後。未修改實作、測試或其他技術債。

驗證環境及實際執行命令（PowerShell；`$taskTemp` 位於 repo 外）：

```powershell
$py = 'C:/Program Files/Blender Foundation/Blender 5.2/5.2/python/bin/python.exe'
$taskTemp = Join-Path $env:TEMP 'cosmimera-p2-12-sol-r3'
& $py -B -m pytest --version
& $py -X utf8 -B -m unittest discover -s tools/balance -p 'test_*.py' -v
& $py -X utf8 -B "$taskTemp/setup.py"
dotnet restore "$taskTemp/model/ModelProbe.csproj" --ignore-failed-sources --verbosity quiet
dotnet restore "$taskTemp/flow/FlowProbe.csproj" --ignore-failed-sources --verbosity quiet
& $py -X utf8 -B "$taskTemp/refine.py"
& $py -X utf8 -B "$taskTemp/flow_gen.py"
& $py -X utf8 -B "$taskTemp/dbc_probe.py"
dotnet run --project "$taskTemp/model/ModelProbe.csproj" --no-restore -c Release -p:ProbeSymbols=UNITY_EDITOR
dotnet run --project "$taskTemp/model/ModelProbe.csproj" --no-restore -c Release -p:ProbeSymbols=RELEASE_PROBE
dotnet run --project "$taskTemp/flow/FlowProbe.csproj" --no-restore -c Release -p:ProbeSymbols=UNITY_EDITOR
dotnet run --project "$taskTemp/flow/FlowProbe.csproj" --no-restore -c Release -p:ProbeSymbols=RELEASE_PROBE
git diff --check -- Assets/_Project/Features/Combat/Runtime/Model/Cast.cs Assets/_Project/Features/Combat/Runtime/Model/Encounter.cs Assets/_Project/Bootstrap/Demo/RunSession.cs Assets/_Project/Features/Combat/Tests/EditMode/EncounterTests.cs
git diff --check -- docs/reviews/P2-12-sol-r3.md
```

- Python 3.13.13 缺 pytest（`No module named pytest`）；改跑現有 balance unittest，**54/54、exit 0**。.NET SDK 9.0.304；兩個 restore 成功。上列列出實際用過的驗證命令；探針建置過程有修正與重跑，不代表只執行一次。
- Model probe 直接編譯正式 Combat model、GameData 定義、PlanetConfig／PlanetFrame／VectorMath、Contract 與 Pcg32，引用現有 managed `Unity.Mathematics.dll`；沒有替換 `Rescale`、`SetLevel`、能力公式、幾何或命中流程。**486 組**（9 形狀×3 射程×6 基準值×3 目標等級），涵蓋兩招槽、目標點 Burst、副手招、基準 0／Int32.MaxValue、HP 保留傷害、CD／位置保留、SPI CD、AGI 命中及同種子 RNG 消耗一致。另 **9,999 組**百分比門檻與實際下穿測試。最終契約啟用版 **68,957** 項斷言；剝除版 **68,949** 項，均 exit 0。
- HP 結果：合法升級時 `Hp' = Hp + MaxHp' − MaxHp`，故 `0 < Hp' <= MaxHp'`，無溢位。固定傷害 D 時比例為 `1 − D/MaxHp`，升級只會提高比例；不呼叫 `NotifyHpChanged` 不會漏掉 `on_hp_cross_below`，也不應在此補送「以新上限衡量舊 HP」的虛構下穿。探針確認之後的真實下穿仍只觸發一次；未發現本 delta 的門檻功能錯誤。
- Flow probe 原樣擷取本次 `StepCombat`、`OnFaint`、`BeginCombat`、`RecordAppearances`、`Reward`、`PayQuest`、`AnyEngaged`、`BossUnit`、`ApplyLevelUps`、`UpdateRegion`、`TryCapture`，編譯正式 TeamState／Progression／QuestTracker／RunPlan／Contract。兩種編譯各 **36** 項斷言、exit 0；**4 條缺陷見證**對應 R3-01 的兩條路徑及 R3-02／03，成功重現缺陷不等於功能通過。另確認 R1 首 tick 開始並結束戰鬥後立即成長、後備成長、戰中不套用、雙方倒下事件兩種順序均不復活死者、跨場收服及其任務使用舊快照、戰外區域任務全員後備、任務不重付。
- 限制：Model 的 ScriptableObject／序列化入口為最小 CLR 替身，JsonUtility 以 System.Text.Json／reflection 讀核定 balance。Flow 的 Encounter tick／事件、空間與區域選取、場景及 UI 為替身；收服擲骰強制成功以測成功分支接線。Flow 只追蹤 SetLevel 呼叫／Level，實際能力重建另由 Model probe 驗證。不能據此宣稱 Unity、Mono／IL2CPP 跨後端結果。
- 暫存探針初版遇到 CP950 讀取失敗、誤用不存在的 `BurstOrigin.AimPoint`，以及將 IReadOnlyList 當 int[] 的編譯錯誤；僅更正 repo 外探針後重跑。Flow 替身有 3 個編譯警告（小寫 `math`、兩個未賦值欄位），未隱藏。未為使探針通過而改 repo 實作。
- **Unity 不可用：未執行新增 NUnit 測試、EditMode、PlayMode 或場景驗收；未引用作者成績作為本輪實跑結果。** repo 寫入只限本報告及 phase-2 一行交接；依本次明確要求，未另改狀態欄。
