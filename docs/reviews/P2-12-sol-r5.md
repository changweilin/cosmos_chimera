# P2-12 對抗式複審 · SOL · round 5

2026-11-09 · SOL（ChatGPT Sol）。對象為 `7792d14fc6070abf50e172f11bb5b07b53095242` 新增的 RunSession 測試接縫及回歸測試。commit 中測試的實際路徑是 `Assets/_Project/Bootstrap/Tests/PlayMode/RunSessionProgressionTests.cs`；下文行號均依該 commit。工作樹內這兩支程式及 `docs/tech-debt.md` 與受審 commit 相同，已逐檔核對。

**Verdict：fail（本輪接縫與測試驗收；0 high／4 med／3 low）。** 四個原缺陷的回退都有可辨識的失敗見證，但尚不足以滿足獨立 oracle、真實事件接線與模擬前升級的驗收要求。未發現佇列保持空白時新增的正式流程回歸；本結論不撤銷 r4 對三項 high 修正的 pass。

| ID | severity | file:line | finding | evidence / failure scenario | suggested fix |
|---|---|---|---|---|---|
| R5-01 | med | `Assets/_Project/Bootstrap/Demo/RunSession.cs:238`、`:456` | **同一個擊倒可合法重複排入，重複發放擊倒 XP。** | 將一隻未結算野生單位打至 0 HP，連續呼叫兩次 `InjectFaintForTests(id)` 再 Step。兩次呼叫都符合現有 Pre，因為排入時未更新 `KnockedOutAtMs`；兩次都進入 `OnFaint`。CLR 在契約啟用／剝除版均重現：一次排入共發 240，兩次共發 400（fixture：擊倒池 160、任務步驟 80；任務沒重付，多付的是擊倒池）。在 R1 的普通敵人／非任務區域配置中，同樣操作會把單次 80 池重付一次。 | 排入時拒絕或去重相同 id，並明載「未排入」條件；或在結算處略過已結算的 spawn。新增重複排入見證，不能只檢查排入當下的結算時間。 |
| R5-02 | med | `Assets/_Project/Bootstrap/Demo/RunSession.cs:447`、`:455`；`Assets/_Project/Bootstrap/Tests/PlayMode/RunSessionProgressionTests.cs:599` | **測試擊倒繞過真實 Faint 事件分派，無法保護該接線。** | 拿掉正式 `OnFaint(ev.TargetId)` 呼叫，保留注入迴圈。CLR 同一 fixture 的真實 Tick 事件由 240 XP 變成 0，注入路徑仍為 240。新增測試的受控擊倒全部使用 `Kill`＋`InjectFaintForTests`，XP 預期不依賴被拿掉的分派；R3-01 更完全不需要擊倒。這是接線突變的漏網條件，不是宣稱已跑完整 NUnit 突變測試。 | 至少加入一例由正式 `Encounter.Tick` 產生 Faint 並驗證 RunSession XP 的測試；可配置確定命中及傷害的 fixture。注入若仍保留，應與真實事件通過同一個分派入口，避免平行的結算通道。 |
| R5-03 | med | `Assets/_Project/Bootstrap/Tests/PlayMode/RunSessionProgressionTests.cs:153`、`:162` | **R3-01 的斷言不能證明升級發生在模擬之前。** | 只把 `StepCombat` 中第一個 `ApplyLevelUps()` 從 `Encounter.Tick` 前移到 Tick 後、`bool engaged`／`BeginCombat` 前。此時 Team 尚未標記開戰，仍能升級。CLR 基準為 `seenAtTick=11, after=11`，突變為 `seenAtTick=10, after=11`，兩者都滿足測試目前的「InCombat 且六隻最終 LV11」斷言，XP 也相同；突變的首 tick 已使用舊能力。 | 驗證首個 Tick 的能力使用結果，例如受控首發招式的 CD／判定體，或在測試專用的模擬入口觀測邊界直接檢查等級。另補已有戰鬥時取得 XP 不立即成長的對照；只看 Step 返回後的等級不夠。 |
| R5-04 | med | `Assets/_Project/Bootstrap/Tests/PlayMode/RunSessionProgressionTests.cs:418`、`:430`、`:435`、`:684` | **oracle 僅部分獨立，且輸入族群無法覆蓋死亡分母與永久 ID 接線。** | R1 的手填向量有獨立性；其餘多處由 `Split` 重算 floor／餘額／排序，再共用正式 `WildLevel`、`XpToNext` 求池。四案受控路徑都是六隻存活，ID 恰為槽位＋1。CLR 的死亡延後剔除突變仍通過全部 12 組全存活 oracle 比對；死亡見證應得 `[0,27,14,13,13,13]`，突變卻得 `[0,20,10,10,10,10]`。改用隊伍順序平手也通過這 12 組，只有反轉永久 ID 才分辨出錯。共用規則錯誤的共變結果另列於下表。 | 使用獨立核定向量／既有黃金資料，或以不同推導方式產出的 fixture 取代整份分配演算法副本。加入主力死亡及非遞增永久 ID 的 RunSession 案例，直接驗 XP 與存活者池守恆；至少一案以已知來源等級／池值驗證，不能全部由正式規則函式計算預期。既有 Progression 黃金測試可守規則層，但不能替代 RunSession 是否正確傳入死亡狀態與 PartyIds 的測試。 |
| R5-05 | low | `Assets/_Project/Bootstrap/Demo/RunSession.cs:154`、`:202`、`:206`、`:214`、`:226`、`:238` | **接縫未從 Release 編譯排除，且「read-only accessors」不是不可變視圖。** | RunSession 沒有編譯條件；Bootstrap asmdef 也沒有測試限定。Release CLR 仍有 internal 注入方法，Pre 呼叫剝除後，對仍存活的野生單位注入亦發出 240 XP。Team／Quest 回傳可變的 live 物件；PartyUnits 回傳的實體仍是 `List<int>`，可轉型修改。現有正式呼叫點沒有使用這些接縫，佇列預設空白；未驗證 Unity linker 是否剝除未使用成員，亦不能把此結果描述成玩家已可利用的漏洞。 | 若要求正式 build 不帶接縫，以一致的 Editor／測試編譯條件包住方法、佇列及其處理段；本測試檔已限 `UNITY_EDITOR`。需要強制只讀時回傳觀測值／只讀包裝，或把「僅供受信任測試讀取」的界線說清楚。internal 本身只限制組件存取。 |
| R5-06 | low | `Assets/_Project/Bootstrap/Tests/PlayMode/RunSessionProgressionTests.cs:34`、`:93`、`:109`、`:646` | **回歸測試把目前平衡值鎖成精確斷言。** | `LoadEnv` 讀正式 Copy balance，R1 卻固定 80 池及 `[20,20,10,10,10,10]`。合法調整野戰基數，即使參與者接線完全正確也會失敗。這部分不符合 testing.md「數值平衡不寫成斷言精確值」；精確檢查整數分配及餘額規則本身有價值，不應一概刪掉 XP 斷言。 | 用固定的合成 progression fixture 驗證精確分配，讓平衡資料獨立演進；對正式 balance 的整合案例以比例、池守恆、參與者差異等不變式驗收。獨立黃金數值核對保留在規則層的核定資料測試。 |
| R5-07 | low（既有缺陷之記錄修正） | `docs/tech-debt.md:10`；`Assets/_Project/Features/Combat/Runtime/Model/Encounter.cs:718`；`Assets/_Project/Bootstrap/Tests/PlayMode/RunSessionProgressionTests.cs:525` | **D0-E4 的成因方向正確，但觸發範圍與建議修法不完整。** | 實作已在第 720 行去掉徑向分量；再次建議「先去掉徑向分量」不是修法。下方實測例的殘差長 `4.356876e-6`，大於絕對門檻及 `1e-6 × |to−from|`，單純改為此相對門檻仍失敗。偏離同方向 0.1 mm 的樣本也有非切向 forward，故不必完全同方向。6 m 停位避開了真實存在的幾何缺陷，不能當作該缺陷已驗收；作為隔離 XP 流程的 fixture 是合理的，且註解已揭露。 | 修正 debt 的觸發範圍及建議，加入獨立幾何見證，再設計包含世界座標精度與再次正交化的處理。不要以單一相對門檻草率結案。此輪不要求把 XP 測試搬回退化點，也不把既有幾何問題算成這個 commit 新引入的 high。 |

接縫與契約判讀：

- 注入確實在 Tick 完成、真實 Faint 全部處理完之後，且在 EndCombat／戰後升級／補人之前呼叫同一個 `OnFaint`。它沒有提早於 Tick 發獎；空佇列只增加迴圈檢查與 Clear，不新增事件或改變 XP／升級順序。
- 這只保證相同的結算階段。正式 Events 依場地／行動單位順序排列；注入固定追加於其後。`Kill` 已在 Tick 前改變存活狀態，因此當次目標選擇、行動與 RNG 消耗亦不同。現有案例可測受控的結算情境，不能據此聲稱完全等價於 Tick 內擊倒，尤其不能延伸到雙方同 tick 倒下等順序情境。
- code-contracts.md 強制 XML DbC 的對象是 public／protected 成員及 feature Api 型別。新增接縫為 Bootstrap 的 internal 成員，**沒有強制補 public 契約的違規**，而且作者已主動寫出契約。R5-01 是已寫契約未能排除的重複事件；不是要求 internal 成員一律加 Contract.Requires。
- `NowMsForTests` 的 Post「等於最後一次 Encounter.Tick」在 Configure 後尚未 Tick 時不成立；`StepCombatForTests` 的 Pre 也宜補 Started 且未 Ended，避免以尚未召喚的空 Active 建立假見證。屬契約文字精度，併 R5-05 交接，不另列缺陷。
- 測試使用真實星球及 Encounter，沒有 mock Unity 物理；放 PlayMode 合理。規則層 public 方法每條 Pre／Post 的測試要求，不直接套用到這些 internal 接縫。`#if UNITY_EDITOR` 表示這四案只驗 Editor PlayMode，不涵蓋 Player／IL2CPP。

回退及其他突變的驗證：

| 突變 | 本輪實際結果／覆蓋判讀 |
|---|---|
| 回退 R1：移除 OnFaint 的非戰鬥開戰補記 | CLR 見證應付 `[20,20,40,40,20,20]`，實付 `[40,40,20,20,20,20]`；失敗。新增 NUnit 的第二場手填向量也能區分新舊集合。 |
| 回退 R3-01：移除模擬前 ApplyLevelUps | CLR 最終等級見證失敗；新增 NUnit 最終 LV11 斷言可以殺死這個原樣回退。 |
| 回退 R3-02：移除區域事件前 RecordAppearances | CLR 應付 `[9,9,18,18,17,9]`，實付 `[10,10,20,20,10,10]`；失敗。 |
| 回退 R3-03：PayQuest 不使用 RecordFor | CLR 應付 `[30,30,40,40,50,50]`，實付 `[30,30,30,30,60,60]`；失敗。 |
| 不逐場清除 appeared | CLR R1 見證失敗；新增 NUnit 第二場把 `{1,2}` 換成累積 `{0,1,2}`，80 池由 `[10,20,20,10,10,10]` 變成 `[18,18,17,9,9,9]`，有可分辨的 XP 斷言。 |
| 出場／後備 4／2 互換 | 直接編譯突變 Progression：全存活 oracle 比對僅 3/12 相同；80 池兩出場由 `[20,20,10,10,10,10]` 變為 `[8,8,16,16,16,16]`。有覆蓋。 |
| 死亡者先留在分母、分完才不發給死者 | 直接編譯突變 Progression：全存活比對 12/12 相同，死亡向量錯誤，見 R5-04。RunSession 若把 IsFainted 一律傳 false，同樣缺乏受控死亡案例來守接線。 |
| 平手依隊伍順序取代永久 ID | 直接編譯突變 Progression：12/12 相同；反轉 ID 後，正確為 `[17,18,18,9,9,9]`，突變為 `[18,18,17,9,9,9]`。新增測試 ID 固定 1..6，無法區分。 |
| WildLevel 忽略 tier／曲線 ceil 改 floor | 兩種直接編譯突變各為 12/12 相同。tier2 的正常 LV14／96 池，共變成 LV10／80 池；floor 曲線的 C10 由 156 變 155、T11 由 1262 變 1254、tier2 池變 97，動態預期同步跟著錯。這證明此 oracle 的獨立性限制，不表示既有 Progression 黃金測試也漏掉它們。 |
| 升級移到 Tick 後、開戰標記前／刪除正式 Faint 分派 | CLR 分別重現「Tick 用 LV10 但最終 LV11」及「真實事件不付、注入仍付」；見 R5-02／03。 |

上表的全存活 oracle 比對為：tier 0／1／2 × 出場集合 `{}`、`{0,1}`、`{1,2}`、`{0,1,2}`，共 12 組。原樣擷取新增測試的 `Pool`／`Split`，與正式／突變 Progression 比對。這些是 CLR 結算與斷言投影，**沒有執行四支完整 Unity coroutine，也不把投影存活稱為全專案測試存活**。四個回退的 CLR 場景亦不是 NUnit 場景的逐行重播；例如 R3-03 使用一隻完成 DefeatWild 的最小 fixture，回退 R1 在該精簡場景也會連帶影響 R3-03，不能冒稱「各自恰好只使一案失敗」。

D0-E4 數值見證（直接編譯正式 Encounter／PlanetConfig／PlanetFrame／MoveGeometry，引用現有 Unity.Mathematics managed DLL）：

```text
R=640，Center=(0,0,0)
from=(413.63202,96.05307,-479.63602)
to=(414.2777,96.20301,-480.38474)
TangentToward forward=(0.7524326,-0.05130222,0.6566684)
projected length=4.356876e-6，|to-from|=0.999989
|dot(forward,trueUp)|=0.0135175683 > 0.001
UNITY_EDITOR: Overlaps 拋 ContractException（forward must be a unit tangent at the anchor）
RELEASE_PROBE: 相同非切向 forward，Pre 被剝除，Overlaps 返回 True
```

固定 `System.Random(559)`，對每種側向距離各產生 10,000 組方向，兩端分別在海拔 0.6／1.6 m；用正式 ToWorld 產生 float 座標。失去切向保證的樣本數：0 m 為 8610、0.01 mm 為 8442、0.1 mm 為 2085；1 mm、1 cm、6 m 各為 0。Editor 與剝除版相同。這是該組 CLR 輸入的數值結果，不是自然遊玩發生率或跨後端統計。Release 的單一 True 結果亦不能推出所有退化輸入都安全。

實際執行的驗證與限制：

```powershell
$py = 'C:/Program Files/Blender Foundation/Blender 5.2/5.2/python/bin/python.exe'
$taskTemp = Join-Path $env:TEMP 'cosmimera-p2-12-sol-r5'
git show 7792d14
dotnet --version
& $py -X utf8 -B (Join-Path $taskTemp 'prepare.py')
dotnet restore (Join-Path $taskTemp 'model/ModelProbe.csproj') --ignore-failed-sources --verbosity quiet
dotnet run --project (Join-Path $taskTemp 'model/ModelProbe.csproj') --no-restore -c Release -p:ProbeSymbols=UNITY_EDITOR -- geometry
& $py -X utf8 -B (Join-Path $taskTemp 'oracle_mutations.py')
& $py -X utf8 -B (Join-Path $taskTemp 'flow_probe.py')
& $py -X utf8 -B (Join-Path $taskTemp 'rollback_probe.py')
dotnet run --project (Join-Path $taskTemp 'model/ModelProbe.csproj') --no-restore -c Release -p:ProbeSymbols=RELEASE_PROBE -- geometry
& $py -X utf8 -B (Join-Path $taskTemp 'audit.py')
git diff --check -- Assets/_Project/Bootstrap/Demo/RunSession.cs Assets/_Project/Bootstrap/Tests/PlayMode/RunSessionProgressionTests.cs
Set-Alias -Name python -Value $py
python -c "open('docs/reviews/P2-12-sol-r5.md',encoding='utf-8').read()"
git diff --check -- docs/reviews/P2-12-sol-r5.md
```

- .NET SDK **9.0.304**；兩個專案 restore 成功。Model 的基準及五個突變設定均完成、exit 0；程式列印相同／不同的觀測，不把「程式 exit 0」誤算成突變通過驗收。
- Flow 沿用 r4 的 repo 外 fixture，重新擷取本次 StepCombat、OnFaint、BeginCombat、RecordAppearances、Reward、PayQuest、RecordFor、AnyEngaged、BossUnit、ApplyLevelUps、UpdateRegion、TryCapture、FindCaptureTarget、SwitchMain、In、InjectFaintForTests。正式 TeamState／Progression／QuestTracker／RunPlan／Contract 直接編譯；擷取段未改寫。新增所需的 Core namespace alias 及 UI 替身位於 wrapper。
- 空注入佇列的 r4 控制案例在 UNITY_EDITOR／RELEASE_PROBE 各 **175 項斷言通過、exit 0**。接縫三種設定各跑兩種符號；四回退及不重置紀錄的設定亦各跑兩種符號，結果如上。回退觀測程式會捕捉並列印個案 FAIL，因此整個探針正常 exit 0 不代表修正可回退。
- Flow 的 Encounter.Tick、事件產生、SetLevel、空間／區域、場景與 UI 是替身；收服固定成功。它能驗結算接線、排入重複及升級呼叫時序，不能驗真實戰鬥、Unity 協程／物理排程或 Player build。正式幾何另由 Model 直接編譯驗證；Model 的 ScriptableObject／JsonUtility 是 CLR 替身，後者依欄位讀取核定 balance。
- 探針初稿有 `Random` 名稱歧義、方法擷取未涵蓋 static，以及缺少 FindCaptureTarget／UI 替身／Core alias 的編譯錯誤；只修正 scratch 後重跑。Flow 最終有 CS8981（math）及 CS0649（未賦值替身欄位）警告，未隱藏；回退版另有未賦值 BeforeTick 欄位警告。
- **Unity 不可用：未執行 NUnit、EditMode、PlayMode、Mono／IL2CPP 或正式 build。** 未將作者的 67/67 或其突變成績列為本輪實跑結果；本輪未重跑 balance unittest，因受審 delta 沒改數值實作。
- 寫入前、完稿後對 1,637 個既有 tracked 檔案核對 SHA-256，變動均為 0；所有探針、突變副本及日誌均在 repo 外。本輪唯一寫入的 repo 檔案為本報告，未改 phase-2.md 或任何既有檔案。最後 status 另顯示與本輪無關的 material-proposal／material_drops 未追蹤檔案，未讀寫或處理。
- 完稿自驗：已重讀每項發現，沒有無見證的 high（本輪 0 high）；指定 Python `-c` 讀取成功，UTF-8 無 BOM、替代字元為 0，diff whitespace 檢查通過。
