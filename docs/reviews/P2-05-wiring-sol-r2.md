# P2-05 接線對抗式複審 r2 · SOL

2026-11-09 · SOL（ChatGPT Sol，Codex）。對象：目前 HEAD `97df75bb000c87fe3694b540845e36b6d9d1644f`；行號均依該 commit。已讀 AGENTS.md、CLAUDE.md、code-contracts／testing／docs-and-progress 規則、roadmap／分工、r1 報告及本次完整 diff；另核對 vision.md 的手把要求。唯讀審查，唯一新增 repo 檔案為本報告，沒有更新 phase／tech-debt。

**Verdict：fail（D0 gate；未結案 1 high／3 med／1 low）。** W-01、W-02、W-04、W-05 的原問題已封住；W-03 部分補齊。新交易程式沒有重現失敗後重試重複入帳或 Clone 遺漏現有狀態。必須立即處理的是 **R2-03：大廳漏算進化按鈕，手把無法以兩隻可進化宙獸的編隊開始下一場**；它已存在於 HEAD 的 parent，是本輪新發現，並非交易修正引入。R2-01／R2-02／R2-04 是此次新增程式的契約／錯誤呈現問題，依 D0 規則可延後。

以下 W 項保留原 severity；已關閉四項不計入上方未結案數量。

| ID | severity | file:line | finding | evidence / failure scenario | suggested fix |
|---|---|---|---|---|---|
| R2-03 | high | `Assets/_Project/Bootstrap/Demo/DemoGame.cs:163`、`:197`、`:202`、`:431`、`:459` | **既有、此次新發現：MenuButtonCount 未計入任何進化按鈕，正常編隊可使開始探索無法由手把選到。** Results 的固定 1 個按鈕計數正確；問題在 Lobby。 | 兩隻非最終型態、都在主力、沒有後備：實際 selectable 順序為 language(0)、evolve(1)、remove(2)、move-up(3)、evolve(4)、remove(5)、start-run(6)、quit(7)。預算卻只有 `1+2+1+2=6`。每次 Update 在移動前 SetCount(6)，焦點只能循環 0..5；Repaint 補回 8 也無法修復下一次 Update。抽出原方法及原 MenuFocus 實跑 Down×24，確實只到 0..5。初始一隻時為預算 4／實際 5，quit 已不可達。 | 在輸入處理前，以與繪製相同的「資料庫存在且非最終型態」條件，把主力及後備進化按鈕算入。補兩隻非最終型態的導航回歸，驗證開始／離開都可取得焦點。`docs/design/vision.md:32` 明訂不能有只靠滑鼠的操作；滑鼠或縮編雖可繞過，不能取代原編隊的手把流程。 |
| W-01 | med | `Assets/_Project/Bootstrap/Demo/LobbyTransactions.cs:69`、`:79`、`:80`、`:85`；`Assets/_Project/Bootstrap/Demo/DemoGame.cs:520` | **fixed：進化只修改候選；寫檔失敗不替換 live，且有可見錯誤。** | LV23、light/t1×3、需求同素材×3，先寫舊檔，再以 `FileStream(path, Open, Read, FileShare.Read)` 鎖最終檔。實跑 Evolve 回 WriteFailed／IOException，next 與 live 同一物件，記憶體仍 a/3、磁碟位元組不變；解鎖重試得到 a_1/0 並可讀回。另以原 EvolveButton／DrawPartyEditor 驗證失敗訊息可見。 | 可關閉原 W-01；判定交易成功須讀 CommitStatus，不能單看候選的 `EvolutionResult.Succeeded`（寫檔失敗時它仍可為 true）。 |
| W-02 | med | `Assets/_Project/Bootstrap/Demo/LobbyTransactions.cs:107`；`Assets/_Project/Bootstrap/Demo/DemoGame.cs:754`、`:760`、`:767`、`:781` | **fixed：素材溢位不再把 run 留在 Running，也不會半套用 XP／收服。** | light/t1=int.MaxValue，outcome 含同料×2、XP、收服：StockFull 指出 Light/T1，live／磁碟完全不變。原 OnRunEnded／TrySettle 抽出實跑後為 Results、run=null、相機啟用、menu 啟用、錯誤＋唯一 retry。Cancel（含同幀 Submit）不離開。另實跑連續 3 次鎖檔失敗後成功，僅增加一隻收服、素材 3→5、XP 為 outcome 的累積值。 | 可關閉原 W-02；Q-C34 的庫存滿額策略仍待人工決定，現行停留＋重試行為符合本次要求。 |
| W-03 | med | `Assets/_Project/Bootstrap/Tests/EditMode/LobbyTransactionsTests.cs:133`；`Assets/_Project/Bootstrap/Tests/PlayMode/RunSessionProgressionTests.cs:130`、`:147` | **partial：交易層測試已補；Unity 整合與 JSON 數值型別缺口仍在。** | 新增七個交易測試，涵蓋進化落盤／拒絕／鎖檔、結算成功／鎖檔重試／庫存滿及部分 Pre；本輪 CLR 實跑通過。但本 commit 沒有改 PlayMode 測試；素材測試仍是普通＋BOSS 的 InjectFaint、未驗證精英掉落、真實 Tick→End→DemoGame 入帳的勝／敗／放棄各路徑、Results 導航或 JsonUtility 對小數／數字字串／Int32 外數值的行為。 | 保留縮小後的 W-03；補真實 Unity 整合測試，納入 R2-03 的導航案例。不能以本輪 CLR facade 取代此項。 |
| W-04 | low | `Assets/_Project/Features/Save/Api/LobbySave.cs:122`、`:140`、`:289` | **fixed：入帳前拒絕域外 stack；ToJson 阻止 tier4 庫存落盤。** | AddMaterials 的 `[合法 stack, tier4]`／`[合法 stack, default]` 均 ArgumentException 且庫存仍空；WouldOverflow(default) 也拒絕。直接 Materials.Add(tier4,1) 後 ToJson 拋 InvalidOperationException，TryWrite=false 且既有檔案不變。移除契約的 Release 另測未定義 Element，仍整批 ArgumentException、無部分入帳。 | 可關閉原 W-04；直接存取 Materials 仍可建立 tier4 庫存，但已不能寫出自身拒讀的檔案。 |
| W-05 | low | `Assets/_Project/Features/Creatures/Api/MaterialDrops.cs:96`、`:109` | **fixed：Editor／Development 在抽樣前檢查整份 bias，播種責任也已明載。** | 三種 SpawnRole×100 seeds，bias=`[Light,(Element)99]`：300/300 在 RNG 推進前拋 ContractException，包含原先會不掉落的 Normal 路徑；Editor、Development 都成立。既有有效輸入測試亦通過。Release 剝除契約、default(Pcg32) 仍由呼叫者負責，符合 Pre 的適用方式，並非宣稱所有 build 都拒絕非法 bias。 | 可關閉原 W-05。 |
| R2-01 | med | `Assets/_Project/Bootstrap/Demo/LobbyTransactions.cs:91`、`:95`、`:123`、`:133` | **新增：Settle 的「same outcome…cannot bank twice」契約過廣；函式沒有成功後去重能力。** | `Settle(live,o)→Committed,next` 後，再 `Settle(next,o)→Committed,twice`：同一 outcome 的 owned 1→2→3、素材 3→5→7；XP 是絕對值，未再加 XP。目前 Pre 沒禁止成功後再次提交；現有測試只驗證失敗後重試。實際 Demo 在成功後切換為 return 按鈕並清掉 confirm，未找到可由正常 Results 輸入觸發的重複入帳。 | 若只支援失敗重試，明載「僅可在先前非 Committed 時重試；成功 outcome 不可再提交」，並收窄 Post；若要承諾成功後也可安全重送，需保存 settlement token／去重狀態。不要把正常 UI 現在單次提交誤稱為 API 冪等。 |
| R2-02 | med | `Assets/_Project/Bootstrap/Demo/DemoGame.cs:373`、`:387`、`:479`、`:491` | **新增：選御三家存檔失敗的錯誤訊息沒有畫出來。** | 鎖住已存在的空 lobby 檔，對候選 ChooseStarter 後 CommitLobby=false；_partyMsg 設成 ui.lobby.save_failed，live.StarterLine 仍 null。下一次原 DrawLobby 在御三家分支 return，從不進 DrawPartyEditor 的訊息區。CLR 繪製記錄證實 `rendered=false`；玩家只看到選擇沒有反應，console 才有例外。進化／編隊已選御三家後的錯誤訊息正常。 | 在御三家分支 return 前也畫出 _partyMsg，或把共用訊息區移到兩分支都會抵達的位置；補首選寫檔失敗呈現的驗證。 |
| R2-04 | low | `Assets/_Project/Bootstrap/Demo/LobbyTransactions.cs:55`、`:66`、`:74`、`:91`、`:102`、`:107` | **新增：早退會略過文件承諾應檢查的部分 Pre。** | Editor／Development 實跑：`Evolve(素材不足, path="")` 回 Refused；`Settle(庫存滿, partyIds=[未持有ID], path="")` 回 StockFull，兩者均未拋 ContractException。路徑只在 TryWrite 才檢查、持有 ID 只在候選 SetExp 才檢查；資料的拒絕結果掩蓋呼叫錯誤。未造成正常 Demo 資料遺失。 | 在所有早退前檢查 path；Settle 先檢查 partyIds 的持有關係。補拒絕／滿額分支的 Pre 違反案例，符合 code-contracts.md 的 Editor／Development 規則。 |

Clone／重試／例外與畫面狀態核對：

- **Clone 現有狀態完整。** 反射列出全部 6 個 instance fields：_owned、_party、_nextInstanceId、_materials、LoadedFormatVersion backing field、StarterLine backing field。清單／庫存物件各自獨立；OwnedCreature 是 readonly struct，內部字串不可變。另以 v2 遷移檔、ID 2／7、next cursor=99、倒序 party 驗證 JSON 等價與 LoadedFormatVersion=2，兩份各自 Add 都取得 99，之後雙向改 XP／形態／party／庫存互不影響。沒有現行遺漏；**未來風險**在手寫 Clone 的欄位白名單：新欄位若也沒寫進 ToJson，現有 JSON 相等測試無法抓到。建議將本輪欄位清單／稀疏 ID／遷移 metadata 見證移入正式回歸，新增欄位時同步審 Clone。
- **失敗重試安全，成功重送不保證。** 三次失敗都以未變的 live 重建候選，捕獲 ID 游標也不提前前進。成功後 _settleError=null，MenuButton 先清 _confirmPending，ExitGUI 結束該次繪製；下一次 Submit 執行 return，未再呼叫 Settle。RunSession.End 的 _ended 防重仍在。outcome 與 _runPartyIds 在待重試時維持原值。
- **例外分類。** TryWrite 僅把 IOException、UnauthorizedAccessException、InvalidOperationException 轉成 false 並保留原 error；鎖檔與檔案唯讀分別實測為 IOException、UnauthorizedAccessException。當前 Write 路徑上的 InvalidOperationException 來源是 ToJson 存檔域檢查，並非任意吞掉契約錯誤；Settle 另外只在捕獲 Add 區塊攔截 ID 耗盡的 InvalidOperationException，整份 live 不變，回 WriteFailed。這個分類也包含無法靠原樣重試修復的情況，不能把 WriteFailed 理解為一定暫時故障。
- **應冒泡的程式錯誤沒有被 blanket catch。** 已實測 TryWrite 的 NUL 路徑 ArgumentException、Settle 的 default 素材 ArgumentException；Editor／Development 的未知持有 ID ContractException 也冒泡。NullReferenceException、OverflowException、OutOfMemoryException 等不在 catch filter 內。非法 outcome／壞 Pre 不應假裝是玩家能重試的 I/O 失敗；R2-04 是早退漏檢查的例外。寫檔失敗仍可能留下完整 `.tmp`，磁碟保證針對最終檔，並非沒有檔案副作用。
- **Results 失敗畫面一致。** _settleError 非 null 時 DrawResults 只有 retry，MenuButtonCount=1；Cancel 同幀 Submit 也以 Cancel 分支清 confirm 而留在 Results。成功後只有 return，Cancel 才能回 Lobby。_xpLines 於首次 OnRunEnded 以舊 XP 和 outcome.PartyExp 建立，失敗不修改 live，重試不重建預覽；LV23→LV24 行在失敗／成功間完全相同，獎勵尚未入帳的錯誤另列於下方，未找到預覽飄移或 XP 重加。未入帳 outcome 仍是記憶體狀態，沒有新增跨程序重啟續結算的承諾（Q-M4 不存 run）。
- **DbC 結論。** AddMaterials／WouldOverflow 的 ArgumentException、ToJson 的域外拒絕、Clone 的現有等價／隔離、TryRoll 的 bias／seed Pre 與正常行為一致；Settle 的過廣去重 Post 與早退 Pre 差異列為 R2-01／R2-04。不能僅以作者 Unity 測試總數推導這些契約已全覆蓋。

實跑驗證表：

| 驗證 | 結果／限制 |
|---|---|
| `git show HEAD`、HEAD／parent 差異與現有測試 | 審閱完成；正式來源全部由 `git show 97df75b:<path>` 複製到 repo 外，沒有編譯會變動的工作樹來源。parent 中已有 MenuButtonCount 漏算與進化按鈕兩個呼叫點，確認 R2-03 的來源。 |
| .NET SDK 9.0.304，三個 fixture 的 NUnit 測試以反射在 CLR 呼叫 | UNITY_EDITOR **39/39**；DEVELOPMENT_BUILD **39/39**；契約剝除的 Release **33/33**，明確略過 6 個含 ContractException 斷言的方法。不是 Unity Test Runner。 |
| 額外對抗見證 | Editor **348** 個自訂斷言通過；Development **348**；Release **46**。包含鎖檔／唯讀、重試、再次成功提交、全欄位 Clone、存檔域、例外與原 Demo 方法的狀態／導航；這些是觀察性斷言，包含確認已列出的缺陷存在，並不代表產品無缺陷。 |
| W-05 全 bias 檢查 | Editor／Development 各 **300/300** 非法尾端元素皆在 RNG 推進前拒絕；Release 不以契約拒絕作驗收條件。 |
| DemoGame 原方法抽出執行 | OnRunEnded、TrySettle、Update、MenuButtonCount、MenuButton、DrawResults、DrawLobby、CommitLobby、DrawPartyEditor、DrawMaterials、EvolveButton、DescribeRefusal、CommitParty 使用原方法文字；MenuFocus 使用完整原檔。Unity UI／輸入／相機／Destroy 以記錄 facade 代替，未測實際 IMGUI 版面、裝置事件、延後銷毀或場景。R2-03 用 selectable 計數，已排除 disabled move-up 按鈕。 |
| JSON／引擎範圍 | JsonUtility 是 System.Text.Json 對應 DTO 欄位的替身；ScriptableObject 等有最小替身，數學使用現有 Unity.Mathematics managed DLL。**沒有用這個替身替 Unity 數值型別嚴格性背書**；Mono／IL2CPP／實體手把未驗。臨時 facade 編譯的兩個 CS0649 是空樣式欄位，不是 repo 編譯警告。 |
| Unity batchmode EditMode／PlayMode | 本輪未執行，避免唯讀審查寫入專案的導入／Library／產物。commit 訊息作者宣告 **940/940、68/68**；僅確認文字，未當作本輪實測。 |
| 唯讀與交付自驗收 | Python 成功讀取本報告；UTF-8 無 BOM、無 U+FFFD；基線 **1,654** 個 tracked 檔案 SHA-256 全部一致。`git diff --check` 通過，`git status --short` 唯一差異為 `?? docs/reviews/P2-05-wiring-sol-r2.md`。 |

探針、來源快照與輸出位於 `%TEMP%/cosmimera-p2-05-wiring-sol-r2/`：`model/Program.cs`、`model/DemoProbe.cs`、`editor.log`、`development.log`、`release.log`、`baseline.json`。保留了額外腳本與最終快照；可直接重跑最終快照（所有路徑均在 repo 外）：

```powershell
$reviewTemp = Join-Path $env:TEMP 'cosmimera-p2-05-wiring-sol-r2'
dotnet restore (Join-Path $reviewTemp 'model/Probe.csproj') --ignore-failed-sources --verbosity quiet
dotnet run --project (Join-Path $reviewTemp 'model/Probe.csproj') --no-restore -c Release -p:UseAppHost=false -p:ProbeSymbols=UNITY_EDITOR
dotnet run --project (Join-Path $reviewTemp 'model/Probe.csproj') --no-restore -c Release -p:UseAppHost=false -p:ProbeSymbols=DEVELOPMENT_BUILD
dotnet run --project (Join-Path $reviewTemp 'model/Probe.csproj') --no-restore -c Release -p:UseAppHost=false -p:ProbeSymbols=RELEASE_PROBE -- release
$py = 'C:/Program Files/Blender Foundation/Blender 5.2/5.2/python/bin/python.exe'
& $py -X utf8 -B (Join-Path $reviewTemp 'verify.py')
```

交接：OPUS 立即修 R2-03 並補大廳可選按鈕導航回歸；W-03、R2-01／R2-02／R2-04 依 D0 規則登錄／延後。SOL 本輪不修改程式、資料、phase 或 tech-debt。
