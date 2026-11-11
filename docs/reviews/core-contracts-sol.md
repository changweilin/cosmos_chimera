# Core contracts 對抗式審查 · SOL

2026-11-10 · SOL（ChatGPT Sol，Codex，唯讀審查）。對象 `git show d3efca6`；實跑基準 `8e182da2e8ea636f44d2f8ceba95385894ab86e6`，分支 `review/sol-core-contracts`。六支受審實作／測試檔與 d3efca6 相同（`git diff d3efca6 HEAD -- <six paths>` 為空）。已讀 AGENTS、契約／測試規約與指定前三份來源報告；D0-E4 另讀 r5。

**Verdict：pass（六項程式修正；0 high／0 med／1 low 測試缺口）。** 未找到合法正式呼叫流程的新增回歸。R9／R3-04 是 DbC 前置條件，Release 仍由呼叫者負責；不能把它們描述為所有 build 都會拒絕錯誤呼叫。R5-01 的配置拒絕才是本次要求的全組態防護。

| ID | severity | file:line | finding | reproducing input / probe output | 建議 |
|---|---|---|---|---|---|
| CC-01 | low | `Assets/_Project/Features/Creatures/Tests/EditMode/ProgressionTests.cs:202`、`:203`；實作 `Assets/_Project/Features/Creatures/Api/Progression.cs:29`、`:30` | R9 建構子修正沒有獨立回歸保護。既有斷言同時呼叫建構子與 Settle，兩層任何一層拋例外都會通過；無法證明建構時已驗 Pre。現行實作正確，未升為功能缺陷。 | 僅在 scratch 移除 ctor 的兩個 Requires，保留 Settle：`P.Settle(1, new[] { new XpRecipient(0,0,false,true,-1) })` 與負 Exp 版本仍拋 ContractException。實跑 UNITY_EDITOR：`R9 existing combined assertions=2/2; ctor(0,0)=accepted; ctor(1,-1)=accepted`。這是精確重放兩個斷言的 CLR 突變見證，未宣稱整套 NUnit 突變已跑。 | 分開以 `Assert.Throws<ContractException>(() => new XpRecipient(...))` 驗 ID=0 與 Exp=-1，另保留 Settle 對 default struct 等不合法名冊的保護。 |

## 六項關閉程度

下列位置相對 `Assets/_Project/Features/`；表中「通過」限定已寫出的契約與輸入域。

| 原 finding | 位置 | 契約啟用與剝除版的結果／證據 |
|---|---|---|
| R5-01 | `Creatures/Api/Progression.cs:140`、`:143`、`:186`、`:221`、`:227` | **通過。** 所有原 Requires 配置檢查改走普通 Check，沒有 Conditional 或副作用依賴。原 wild／elite 各 Int32.MaxValue 反例被 elite 包絡擋住；合法 elite=100 時，wild base=280858 可載入、LV100 回 2147483476，base=280859 拋 InvalidDataException。其餘四來源各 base=28085802 可載入、回 2147483629，base+1 全拒絕；三種編譯相同。缺段、null／空字串、負 base、非法權重／曲線／divisor／reference／starter、單級及累積門檻溢位亦拒絕。 |
| R6 | `WorldGen/Api/QuestTracker.cs:85`、`:90`、`:124`、`:138` | **通過。** 同一 Wood spawn 在 DefeatWild 需 2 隻時重送三次，Progress 保持 1；不同第二隻才推進。提早倒下 elite 仍由 Advance 讀 `_down` 補記；elite 本身補滿 DefeatWild 的案例亦到 CaptureOne。收服走 OnCaptured，不再報同一 OnKnockedOut；boss 首次擊倒即終止，重送不改 earned 位元。完整順序五步全 earned，boss shortcut 只 earned 最後一步；三版皆同。 |
| R7 | `Creatures/Api/Progression.cs:66`、`:67`、`:427` | **通過。** Paid／Assigned 皆為 Array.AsReadOnly；Settle 的陣列只在本次呼叫配置，無其他公開可變別名。兩個清單的泛型／非泛型 IList setter 與 Clear 均拋 NotSupportedException，值仍為 10；不能轉回 int[]。三版皆同。 |
| R8 | `Creatures/Api/Progression.cs:74`、`:316`、`:430` | **通過。** 輸入 Exp=Int32.MaxValue、pool=1：Paid=0、MainLoss=1；新 Post `min(Exp, MaxExp)+Paid <= MaxExp` 成立（正式 MaxExp=248528），不再承諾修改呼叫者的 Exp。三版皆同。 |
| R9 | `Creatures/Api/Progression.cs:27` | **程式通過；測試缺口 CC-01。** 直接構造 (id,exp)=(0,0)、(1,-1)、(0,-1)，UNITY_EDITOR 與 DEVELOPMENT_BUILD 均拋 ContractException；無兩符號的 Release 均接受，符合 code-contracts.md 的剝除規則。合法建構與 Settle 無回歸；Owner 的名冊關係仍在 Settle 檢查。default struct 不會呼叫 ctor，仍須由 Settle 驗證。 |
| R3-04 | `Combat/Runtime/Model/Encounter.cs:330`、`:348`、`:362` | **通過（DbC）。** 真實 Tick 產生存活 Engaged 野生單位後 SetLevel，Editor／Development 拒絕，Release 接受，符合前置條件剝除規則。擊倒後保留 Engaged=true、IsActive=false，SetLevel 到 LV12 三版均成功。新增 EncounterTests.cs:121 的斷言能區分此 guard 的原樣回退，但本身不證明 RunSession 的生命週期整合；下文另驗。 |

來源 XP 的 Int64 安全性亦成立：載入已用 BigInteger 保證 `floor(N/Cref) <= Int32.MaxValue`，故非負整數乘積 `N < (Int32.MaxValue+1)*Cref < 2^62`。曲線成長且成本為正，最大值在 C(MaxLevel−1)，Wild 使用最大合法 elite 倍率。不能只因最終 cast 為 int 就宣稱乘法安全；這裡由載入包絡先保證。

## SetLevel 與正式呼叫端

`Assets/_Project/Bootstrap/Demo/RunSession.cs:375`–`:381` 建立的每個野生 unit 都立即登記 `_wild`；`EncounterUnit.Engaged` 只用於 Wild（Encounter.cs:153），UpdateEngagement（:617、:633）也只寫活躍 Wild。Player 預設 false，正式程式沒有把它設 true 的路徑。因此 AnyActiveEngaged 與 RunSession.cs:945 的 AnyEngaged 在此名冊上等價。

StepCombat（RunSession.cs:459、:461、:485、:490）的關鍵不變式：

- 進 Tick 前，若 Team.InCombat=false，上一 tick 結束已無活躍 Engaged Wild；其後的收服只會 Remove（:750），區域步驟只發 XP（:816）。這兩者不會把 Wild 從 inactive／unengaged 改成 active＋engaged。第一次 Tick 前所有 Engaged 預設 false。
- Tick 後先取 engaged；後續 OnFaint 會擊倒／Unfield，收服 Remove 會清 Fielded／Engaged（Encounter.cs:419）。這些操作只會減少活躍交戰集合。舊集合為空才 EndCombat，因此戰後 ApplyLevelUps 不會遇到新的活躍交戰者。
- 最後敵人倒下時，Engaged 可以暫留 true，但 IsActive 是 `Fielded && !IsFainted`（Encounter.cs:163），guard 不會把倒下者算成仍在戰鬥。RefillField 在升級後只召喚 Player；無復活倒下 Wild 的路徑。

使用實際 Encounter／TeamState，原文擷取 StepCombat、OnFaint、ApplyLevelUps、Reward、PayQuest、UpdateRegion、TryCapture 等 13 個 RunSession 方法，在三種編譯均得到：

```text
FLOW first Tick actual Faint dispatch: Team=false level=11
FLOW end tick: Team=false wild Engaged=true Active=false; level=11
FLOW capture: Remove cleared engagement; pending LV12 applied before next Tick
FLOW region: pending level=11 with Team=true after Tick
FLOW live XP deferred; last-wild removal permits growth
```

第一例由正式 AI／Tick 產生 Faint，再由擷取的正式事件迴圈結算；其他擊倒控制例以正式 Combatant.ReceiveSupport 的傷害建立倒下狀態。收服使用正式 CaptureRules／PCG，失敗時換全新 session／seed 找成功分支，沒有對同一 spawn 重擲。每個檢查點另比對活躍 Engaged 集合與 AnyEngaged；沒有用手設 Player.Engaged 製造正式流程不可能的反例。

R6 的合法重報路徑核對：TryCapture（RunSession.cs:757）只報 OnCaptured；boss 的 IsComplete 終態不需要第二個擊倒；Q-E1 是 Advance 查 `_down[target]`（QuestTracker.cs:138），不依賴重送事件。新 guard 因而不封住這些合法後續流程。第一個事件就是該 spawn 的擊倒事實，不能靠日後換一個 mainElement 重報來修正它；正式 CreatureId／屬性在此流程不變。

## 測試證據與限制

- Unity **6000.3.28f1** 實跑一次 EditMode：**948/948 passed、0 failed、0 skipped**。相較原 `Logs/editmode.xml` 的 947 個完整 test-case 名稱，唯一新增為 `Cosmimera.Creatures.Tests.ProgressionTests.Settlement_lists_cannot_be_cast_back_and_mutated`，沒有任何舊案例消失。故要求的原 947 項全部通過，實際總數應記 948，不能填成 947/947。
- Unity 原樣命令（PowerShell 的 `$taskScratch` 為下列 repo 外目錄）：

```powershell
& 'C:\Program Files\Unity\Hub\Editor\6000.3.28f1\Editor\Unity.exe' -batchmode -nographics -projectPath . -runTests -testPlatform EditMode -testResults "$taskScratch/editmode.xml" -logFile "$taskScratch/editmode.log"
```

- Scratch：`C:/Users/user/AppData/Local/Temp/cosmimera-core-contracts-sol/`。`prepare.py` 從目前 worktree 複製正式來源，留存 SHA-256；核心／幾何／契約／PCG 未改。`flowgen.py` 原文擷取上述 RunSession 方法；區域定位固定為 fixture 區域、地面高度為 0、表現／補人／結束與掉落服務為替身。不是整個 MonoBehaviour／場景／PlayMode 驗收。
- .NET SDK 9.0.304／CLR 9.0.8，引用本機 Unity.Mathematics.dll；ScriptableObject 為普通物件，JsonUtility 是 System.Text.Json／reflection 欄位替身。因此配置 probe 驗證的是明確的結構／數值檢查，沒有證明 Unity 原生 JSON 語法錯誤的例外型別。
- `dotnet run --project <scratch>/Probe.csproj -c Release -p:ProbeSymbols=<symbol> -p:UseAppHost=false`：UNITY_EDITOR、DEVELOPMENT_BUILD、RELEASE_PROBE 三版各 **86 項斷言通過、exit 0**。最後一版沒有 Editor／Development 符號；另直接觀察到非法 ctor／mid-fight SetLevel 被接受，證明不是誤用契約啟用編譯。
- CC-01 突變只移除 scratch ctor 的兩個 Requires，重放兩個既有斷言。復原 scratch 原始碼後重建，再跑三版正常驗證。初版探針曾誤用不存在的 ReceiveDamage、超域 power 及少 Capture namespace；只改 scratch，未改 repo。未跑整套 NUnit 突變、PlayMode 或 Mono／IL2CPP Release Player。
- FromJson 測試能守其覆蓋的配置拒絕；Editor 測試單獨不能證明 Release 不剝除。R7 新測試守 Paid 的 IList setter 與兩個 raw-array cast，Assigned 的 setter 另由本輪 probe 驗證。R6 原有 early-elite／elite-last／boss 案例仍通過，新增重送斷言可抓原樣回退。R8 文字修正以 above-cap 輸入直接核對。
- 提交前 `git status --short -- Assets/ tools/` 為空；受限目錄無殘留變更。repo 寫入只含本報告、[D0-E4 提案](D0-E4-proposal-sol.md) 與 P2-12 一行交接；既有 P2-12 的 🟦 狀態仍適用，未擴大宣稱整個項目完成。

CC-01 可由 OPUS 後續補測；不重開已符合專案 DbC 規約的六項程式修正。D0-E4 仍是既有 low 技術債，本輪只提案，未計為 d3efca6 新缺陷。
