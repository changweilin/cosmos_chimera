# P2-05 接線對抗式複審 r3 · SOL

2026-11-09 · SOL（ChatGPT Sol，Codex）。對象：HEAD `9d1f2f889ff4108b583c76ded6bed7877afce0d9`，相對 `97df75bb000c87fe3694b540845e36b6d9d1644f` 的完整 delta；行號均依 HEAD。已讀 AGENTS.md、CLAUDE.md、code-contracts／unity-csharp／testing／docs-and-progress 規則、roadmap／分工及 r2 報告。唯讀複審，唯一新增 repo 檔案為本報告；依本次限制不更新 phase／tech-debt。

**Verdict：pass（D0 gate；0 high／2 med／0 low 未結案）。** R2-01～R2-04 均可關閉，未發現新 high。新增 **R3-01 med：計數共用的需求建構在 Update 配置記憶體**；另保留 r2 的 **W-03 med** 整合驗證缺口。兩者依 D0 規則可延後。此 pass 不代表原生 Unity 全測試已由本輪重跑。

| ID | severity | file:line | finding | evidence / failure scenario | suggested fix |
|---|---|---|---|---|---|
| R2-01 | med | `Assets/_Project/Bootstrap/Demo/LobbyTransactions.cs:94`、`:98` | **fixed：契約明載僅非 Committed 後可安全重試，成功後不保證冪等。** | 原交易程式實跑：三次鎖檔失敗皆保留 live／磁碟；解鎖成功只入帳一次。刻意將同一 outcome 再交給成功的 next，仍由 owned 2→3、素材 5→7；這與新文件明載的「成功後再次提交會再入帳」一致，已沒有過廣的防重承諾。現行 TrySettle／Results 成功後改為 return 的呼叫端未變。 | 關閉；成功後不再提交仍是呼叫者責任，沒有新增 settlement token。 |
| R2-02 | med | `Assets/_Project/Bootstrap/Demo/DemoGame.cs:395` | **fixed：御三家分支 return 前顯示 _partyMsg。** | 以真正原 DrawLobby → ChooseStarter → CommitLobby 方法文字執行：鎖住既有空 lobby 檔，點御三家，live.StarterLine 保持 null、磁碟位元組不變；下一次 DrawLobby 記錄到 `ui.lobby.save_failed` label。解鎖再點同分支可成功，訊息清空。 | 關閉；這是繪製呼叫見證，未替 IMGUI 的實際字體、裁切或版面背書。 |
| R2-03 | high | `Assets/_Project/Bootstrap/Demo/DemoGame.cs:197`、`:221`、`:515`、`:529`；`Assets/_Project/Bootstrap/Tests/PlayMode/DemoMenuPlayTests.cs:59`、`:81` | **fixed：主力及後備的進化按鈕均納入預算；無圖形路徑仍能殺死原回退。** | 原計數、繪製、Update 與 MenuFocus 實跑 5,241 組狀態：每組 budget == _menuButtonCount == GUILayout 的 enabled 按鈕數，Down 繞行可到所有索引。原新增測試的 iterator 在完全不呼叫 OnGUI 的 facade 中通過（24、drawn=0）；只換回 `97df75b` 的 MenuButtonCount，原斷言失敗 `Expected: 24 / But was: 16`，程序以預期失敗碼 23 結束。 | 關閉 high；新測試能防原漏算，但不是實際裝置導航或全狀態繪製回歸的完整替代。 |
| R2-04 | low | `Assets/_Project/Bootstrap/Demo/LobbyTransactions.cs:69`、`:110`、`:111`；`Assets/_Project/Bootstrap/Tests/EditMode/LobbyTransactionsTests.cs:185` | **fixed：空 path 與所有 party ID 的持有檢查均位於早退前。** | UNITY_EDITOR／DEVELOPMENT_BUILD 下，素材不足的 Evolve＋空 path、滿庫存的 Settle＋未持有 ID、滿庫存的 Settle＋空 path，均先拋 ContractException；新增三個正式 Pre 斷言也通過。Release 剝除條件呼叫，未誤稱 Release 必須拒絕非法 Pre。 | 關閉；Settle 的外層迴圈留在 Release，Any 與 lambda 的求值隨 Conditional 呼叫剝除，不造成交易語意變化。 |
| R3-01 | med | `Assets/_Project/Bootstrap/Demo/DemoGame.cs:163`、`:221`、`:523`；`Assets/_Project/Features/Creatures/Api/EvolutionRequirement.cs:47`、`:48`、`:67`、`:103` | **新增：大廳 Update 每幀為所有非最終型態建構完整需求，違反 unity-csharp.md 的熱路徑不配置規則。** | 原來 MenuButtonCount 不配置；新路徑 Update → MenuButtonCount → EvolveRequirementFor → FromDef，建立 stacks、merged、Dictionary、ReadOnlyCollection 及底層陣列。相同 8 隻非最終型態、每隻一種素材的 CLR 暖機探針，10,000 次：parent **0 bytes**，HEAD **32,000,848 bytes**（約 3,200 bytes／次）；Release 也有約 3,008 bytes／次。精確大小屬 CLR 見證，配置來源可由原碼直接確認，並非 Unity Profiler 數值。未證明 D0 的小編隊會造成可感知停頓，故為 med。 | 計數只需讀資料庫存在且 `def.EvolvesTo != null`；可共用無配置的 definition 判定，完整 FromDef 留到點擊需使用需求時，或於資料／存檔變更時快取。保持計數與繪製判定一致；D0 可延後。 |
| W-03 | med | `Assets/_Project/Bootstrap/Tests/PlayMode/DemoMenuPlayTests.cs:59`；詳見 r2 | **partial、沿用：新增了會殺原回退的焦點預算測試，原整合缺口未全部覆蓋。** | delta 沒有新增真實 Tick → End → DemoGame 入帳的勝／敗／放棄、精英掉落、Results 重試導航或原生 JsonUtility 數值型別驗證。此輪額外探針也不取代這些 Unity 整合測試。 | 繼續保留縮小後的 W-03，依 D0 延後；不重開已關閉的 W-01／W-02／W-04／W-05。 |

大廳計數核對（完成一次繪製、沒有觸發 ExitGUI 的穩定畫面）：

- 尚未選御三家：`1 + _starters.Count`，只有語言與御三家選項；已實跑 0～3 個候選。
- 已選御三家：令 N＝持有數、P＝主力數、E＝資料庫可取得且 `EvolvesTo != null` 的持有數。合法存檔有 1≤P≤6 且主力為不重複的持有 ID，故實際可選數為 `1 + P + (P−1) + (N−P) + E + 2 = N + P + E + 2`。每隻持有宙獸恰在主力或後備繪製一次；新迴圈也恰計一次。
- 第一隻的 move-up 有畫出但 `GUI.enabled=false`，MenuButton 不配置索引，故不算；後續 move-up 皆啟用。「移除唯一主力」與「已滿六隻時加入後備」仍是 enabled，點後回覆拒絕，應計入。「等級／素材不足」的進化按鈕同樣仍 enabled，不能用可成功進化數取代 E。
- 最終型態與資料庫缺物種：共用判定均不畫進化按鈕、也不計數；其餘編隊按鈕仍照常。GUI 停用見證包含第一列 move-up 與直接 disabled MenuButton 不增加計數；不把外部任意將整個 DrawLobby 包在 GUI.enabled=false 的非現行呼叫方式當成受支援狀態。

| 狀態 | budget／實際可選 | 實際 Button 呼叫數 |
|---|---|---|
| 未選御三家、3 個候選 | 4／4 | 4 |
| 1 隻非最終型態主力、無後備 | 5／5 | 6（含停用 move-up） |
| 2 隻非最終型態主力、無後備 | 8／8 | 9 |
| 6 主力＋2 後備、全部非最終型態 | 24／24 | 25 |
| 6 主力＋2 後備、全部最終型態或物種全缺 | 16／16 | 17 |
| 混合非最終／最終／缺物種 | N＋P＋E＋2，均相等 | 可選數＋1 |

5,241 組涵蓋 N=1..8、P=1..min(6,N)；N≤5 對三種物種狀態完整列舉，N=6..8 每個 P 用固定 seed 203 抽取 200 組，主力以反向持有次序選取，含主力／後備交錯及滿隊。每組先記錄 enabled 控制項，再以原 Update／MenuFocus 遍歷全部索引，避免把 GUILayout 的全部呼叫誤當成 selectable 數。上述公式另涵蓋合法狀態的任意持有數與順序；未測試竄改內部清單造成重複主力等違反 LobbySave 不變式的物件。

新 PlayMode 測試與 `-nographics` 的判定：`Assert.That(budget, Is.EqualTo(24))` 在 `if (drawn > 0)` **之外**，所以 drawn=0 只跳過第二條繪製一致性斷言，不跳過 24。`ChooseStarter` 建立 1 隻，再 Add `MaxParty+1=7` 隻，得到 6 主力＋2 後備；本場景選取的御三家幼年型態均有後續形態。沒有素材或足夠等級也照畫進化按鈕。探針執行的是該 iterator 的原始方法文字與 NUnit 斷言，僅場景／物件查找／輸入等引擎邊界以 facade 代替；不能稱為原生 Unity 執行結果。

實跑驗證表：

| 驗證 | 結果／限制 |
|---|---|
| HEAD 完整 delta、asmdef／測試／交接 diff | 已核對；新增 Save assembly reference 對應測試直接使用 LobbySave，無不相干實作變更。所有探針來源由 `git show HEAD:<path>` 快照至 repo 外。 |
| 原三個 fixture，在 .NET SDK 9.0.304 以反射執行 NUnit 方法 | Editor **39/39**、Development **39/39**；Release **33/33**，略過 6 個要求 ContractException 的方法。包含新的 R2-04 斷言；不是 Unity Test Runner。 |
| 額外見證 | Editor／Development 各 **10,830** 個自訂斷言；Release **10,527**。包含 5,241 組計數／繪製／導航、首選鎖檔訊息、交易失敗重試、成功重送與既有存檔／Clone／例外邊界。配置量測另列 R3-01。 |
| 回退突變 | 僅替換 repo 外快照的 MenuButtonCount 為 `97df75b` 原文；原新增測試在 drawn=0 時失敗 24≠16。每次切換皆強制非增量重建；還原 HEAD 後同一測試再次通過 24、drawn=0。 |
| Unity 6000.3.28f1 原生嘗試 | 在 repo 外由 HEAD archive 建立獨立專案並複製 PackageCache，執行 `-batchmode -nographics -runTests -testPlatform PlayMode -testFilter Cosmimera.Bootstrap.Tests.DemoMenuPlayTests.Lobby_focus_budget_matches_drawn_buttons_with_evolvable_creatures`。停於 licensing channel refused／`Licensing is not yet initialized`，沒有 XML、尚未跑到測試；已停止本輪啟動的 Unity 程序。未繞過環境權限。 |
| 作者驗收宣告 | commit 與交接宣告 **EditMode 940/940、PlayMode 69/69**；本輪未重現這兩組原生結果，不拿它們當自身實測。 |
| 引擎範圍 | JsonUtility 使用 System.Text.Json DTO facade；ScriptableObject、GUI、場景與輸入有替身。沒有測實體手把、實際 IMGUI 可視區域／裁切、Unity GC 大小、Mono／IL2CPP。臨時 facade 的兩個 CS0649 樣式欄位警告不是 repo 警告。 |
| 唯讀與交付自驗收 | Python 成功嚴格以 UTF-8 讀取報告、無 BOM／U+FFFD；基線 **1,655** 個 tracked 檔案 SHA-256 全部一致，HEAD 與 tracked 清單未變。`git diff --check` 通過；`git status --short` 唯一差異為 `?? docs/reviews/P2-05-wiring-sol-r3.md`。 |

探針、快照、baseline 與輸出均保留在 `%TEMP%/cosmimera-p2-05-wiring-sol-r3/`。`run.py` 針對保留的 HEAD／parent 快照依序強制重建三種設定、執行見證、殺突變並還原 HEAD；`verify.py` 檢查唯讀與報告交付條件：

```powershell
$reviewTemp = Join-Path $env:TEMP 'cosmimera-p2-05-wiring-sol-r3'
$py = 'C:/Program Files/Blender Foundation/Blender 5.2/5.2/python/bin/python.exe'
dotnet restore (Join-Path $reviewTemp 'model/Probe.csproj') --ignore-failed-sources --verbosity quiet
& $py -X utf8 -B (Join-Path $reviewTemp 'run.py')
& $py -X utf8 -B (Join-Path $reviewTemp 'verify.py')
```

交接：SOL r3 **done／D0 pass**；OPUS 可關閉 R2-01～R2-04，後續處理 R3-01 配置與 W-03 整合測試；具可用授權的 Unity 環境補原生 headless 回退突變驗證。本輪不修改程式、phase 或 tech-debt。
