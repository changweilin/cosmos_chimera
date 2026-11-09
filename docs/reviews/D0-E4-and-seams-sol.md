# D0-E4 與 RunSession 測試接縫複審 · SOL

2026-11-10 · SOL（ChatGPT Sol，Codex，唯讀審查）。分支 `review/sol-r2`，基準 `ad0e37d8b462429db32f2187045f8bdcc982eebb`；審查 `ad0e37d`（OPUS）與 `4b282faf7dd13fb4cd06069c9075d55b51907a5b`（TERRA）。行號均依此 HEAD。

**Verdict：pass（D0 gate；本輪 0 high／2 med／0 low）。** 未發現現行正式流程阻礙遊玩的回歸；完整測試驗收仍有下列缺口，V-01 不宜宣稱全部關閉。既有 R5-05 的可變 accessor 問題仍依 tech-debt 保留，不重複計入本輪 finding。只新增本報告並追加一行 P2-12 交接，不修改 Assets／tools。

| ID | severity | file:line | finding／重現 | 處置 |
|---|---|---|---|---|
| DS-01 | med | `Assets/_Project/Features/Combat/Tests/EditMode/TangentTowardTests.cs:50`、`:73`；正式門檻 `Assets/_Project/Features/Combat/Runtime/Model/Encounter.cs:745` | **提案第 2 項的 tau 邊界尚未封住。** overhead 與明顯高於門檻的案例無法保護保守門檻本身。repo 外僅將係數 `16.0` 改成 `1.0`，直接執行原測試方法仍 **10/10 pass**。額外見證：R=640、center=0，from=(0,641,0)、to=(0.0003056526184082031,641,0)、fallback=(0,0,1)；正式 tau≈0.0006113055283 m，應保留 +Z；突變卻轉成 +X，探針失敗。現行實作對此見證正確。 | OPUS 補可控制的 tau 下方／邊界／上方斷言，包含世界座標尺度與不同半徑；用獨立方位 oracle。現有測試亦未完整涵蓋提案列出的精確極軸／近正負軸，不應把第 2 項列為全驗收。D0 可延後。 |
| DS-02 | med | `Assets/_Project/Bootstrap/Tests/PlayMode/RunSessionProgressionTests.cs:138`、`:880`；正式接線 `Assets/_Project/Bootstrap/Demo/RunSession.cs:438` | **V-01 的逐幀接線仍未保護。** Launch 停用 session，再直接呼叫 `SyncViewsForTests()`；這能驗 SyncViews 內部，不能驗 Update 有呼叫它。外部副本只刪 `Update` 的 `SyncViews();`，保留測試接縫；Unity GPU PlayMode 的 RunSessionProgressionTests＋CreatureViewsTests 仍 **13/13 pass**。若此回退進入正式碼，view 不再逐幀更新位置／可見性／倒下外觀。現行接線存在，這是測試缺口。 | TERRA 補保留 session 啟用、跨 frame 觀察 view 位置及倒下狀態的整合案例，確保移除此呼叫會失敗；手動同步測試可保留。V-01 其餘主要項目已補，整項僅部分關閉。D0 可延後。 |

## OPUS：數值語義與回歸

`Encounter.cs:732` 在 from−center、to−from **相減之前**提升 double；雙重 cross、S 的世界座標尺度及 `16*u*S*(1+|d|/|r|)` 均與提案一致。`:746` 僅在 `length > tau` 採目標；`<=` 使用 fallback；`:748` 兩分支都經既有 ProjectOnTangent，再正交化／正規化。零或近徑向 fallback 由既有 frame 規則轉 North。沒有改碰撞容差、RNG 或位置；internal 僅供既有測試組件取用，未新增 public API。

原兩個固定見證確實檢查 finite／unit／double true-up dot（`TangentTowardTests.cs:28`、`:36`），不依賴被剝除的 Contract，也不以命中結果代替方向 oracle。repo 外將正式 Encounter 換回父提交舊函式 body，保留 internal，直接執行這份 NUnit 測試方法：**5/10 fail**，包含兩個見證，true-up dot 分別為 **0.013517568310782047／0.009395319125467769**；現行 body **10/10 pass**。這是 .NET 9.0.8＋本分支 Unity.Mathematics DLL 的重放，並非聲稱跑過 Unity 中的舊碼突變。

CC-01 已關閉：`Assets/_Project/Features/Creatures/Tests/EditMode/ProgressionTests.cs:210` 直接構造 ID=0／Exp=-1 並要求 ContractException，另驗合法構造；不再由 Settle 的驗證代為滿足。實作的兩個 Requires 位於 `Creatures/Api/Progression.cs:29`、`:30`，刪除任一檢查會使對應 ctor 斷言失敗。

提案第 3 項仍缺**提交於 repo 的 Unity Tick 回歸**。本輪額外用逐檔複製的正式純 C# Combat 原碼驗證：R=640／1280、center=0／(16000,−16000,8000)、兩個見證方向及精確北極，S／L 合法不同身體半徑、相同徑向位置、零威力正式攻擊維持存活；共 **1,200 次 Tick、96 次施招**。每 tick 對雙方當前 Position 驗 finite／unit／true-up dot，並呼叫正式 MoveGeometry.Overlaps，契約啟用／剝除兩版皆通過；徑向目標也會走 carried-heading fallback。另 8 組軸向 tau 下方／上方案例兩版皆通過。這減少 D0 的風險，不能取代 Unity／Mono／IL2CPP 的持久回歸。**目前不需要以此阻擋 demo**，demo 後由 OPUS 補入測試。

提案第 5 項仍缺**Unity 有敵人熱路徑的 Profiler 與前後對照**。方法新增的運算與 frame 均為值型別，沒有新增 managed 容器／閉包；repo 外 Release CLR 暖機後 100 萬次實際 TangentToward 兩次取樣約 **123–162 ms、0 B managed allocation**。這只是單方法 CLR 量測，不是 Unity Tick／Mono／IL2CPP 預算或 FPS 證明。未重現遊玩卡住，也沒有量測證據支持 high；**不必在 demo 前補效能 gate**，後續在目標 player、有敵人與暖機後量測整體 Tick。

## TERRA：逐項關閉核對

| 原 ID | 判定 | 本輪證據（RunSessionProgressionTests.cs，除另註） |
|---|---|---|
| R5-01 | 關閉 | RunSession.cs:270 以 Contains 去重；`:184`／`:185` 連續注入同一 id，`:188` 精確驗只支付一次 `[20,20,10,10,10,10]`。不同 id 保留排入順序。真實事件與注入仍分兩段呼叫同一 OnFaint，R5-02 另有實際路徑保護。 |
| R5-02 | 關閉 | `:228` 不用 Kill／注入，實際 Step→Encounter.Tick，觀察野生 Faint 並驗當步 XP>0；地點選在任務區外。外部 Unity 副本移除 RunSession.cs:487 的 `OnFaint(ev.TargetId)`，對應測試 **0/1**、CLI exit 8，失敗訊息為「the wild creature was not knocked out by Encounter.Tick」。不能將此重放描述成 XP 斷言失敗；它仍確實抓到接線回退。 |
| R5-03 | 關閉主要缺陷 | RunSession.cs:473 的 hook 緊貼真正 Tick 前，`:333`／`:336` 驗六隻首 Tick 已 LV11。外部 Unity 副本把第一次 ApplyLevelUps 移至 Tick 後、BeginCombat 前，對應測試 **0/1**；本 HEAD 額外由 SetLevel 的 mid-fight Pre 提早攔截。hook 的 LV 斷言本身亦不依賴 Step 返回後的等級。未新增「戰中取得 XP，待戰後才升級」獨立對照，未宣稱該對照已完成。 |
| R5-04 | 關閉 | `:609` 以固定 cost 表、`:614` 以 12 組向量取代分配算法副本；`:209` 同時覆蓋死亡移出分母與倒序永久 ID，預期 `[0,27,13,13,13,14]`、總額 80。另以獨立 Python Decimal ceil 驗 10 個 costs、Fraction 驗全部 12 組 split 與死亡／ID 向量，皆一致；未共用正式 WildLevel／XpToNext／Settle 求預期。 |
| R5-06 | 關閉 | `:818` 的 Progression 改為明載合成 JSON，`:869` 注入 RunSetup；精確 XP 斷言不再鎖正式 balance 的 progression 數值。CombatBalance／物種／任務生成仍取正式資料，這些是整合依賴，不能解讀成整份測試與正式內容完全隔離。 |
| V-01 | 部分關閉 | `:136` 真正經 SyncViews 內部驗 fielded 倒下變灰，`:144` 驗每個 view 的 Dispose 次數；CreatureViewsTests.cs:67 的固定十色、雙 primitive collider、shader 與 0.03 m 外框，以及 `:108` 的 toon／fallback 材質生命週期皆通過。Update→SyncViews 缺口見 DS-02；未宣稱所有收回／Gone／骨架或實機畫面情境已驗收。 |
| R5-05（既有 low） | 維持部分關閉 | guard 已排除一般 player，見下節；Team／Quest／PartyUnits 仍是可變 live 資料（RunSession.cs:212、:216、:224），符合現存 tech-debt，未在本輪偷列為已關閉。 |

## 一般 player 與實跑紀錄

檢索 Assets/_Project 全部 C# 的 ForTests／InjectFaint／_injectedFaints 引用：RunSession 以外只有 Tests；正式 RunSession 的宣告、queue 處理及 hook 呼叫均位於同樣的 `UNITY_EDITOR || UNITY_INCLUDE_TESTS` guard（`:159`、`:192`、`:248`、`:472`、`:492`）。正式 UseViewFactory 保留在 guard 外，沒有生產呼叫依賴 guarded 成員。

為不更動 repo 設定，複製 Assets／Packages／ProjectSettings／Data 至外部副本，**建置前核對 1,267 個 Assets 檔 SHA-256 完全一致**，僅改副本的 Standalone backend 為 Mono。以 Unity **6000.3.28f1** 的 `-buildWindows64Player`（由 unity build 呼叫）建置 `BuildOptions.None` 等價的一般 player，**exit 0、Build Finished, Result: Success**。player 編譯 response file `Library/Bee/artifacts/1900b0aP.dag/Cosmimera.Bootstrap.rsp` 確認 `ENABLE_MONO`，且**沒有 UNITY_EDITOR／UNITY_INCLUDE_TESTS／DEVELOPMENT_BUILD**。再直接讀出建置成品 Cosmimera.Bootstrap.dll 的 PE metadata：RunSession 共 102 個方法／欄位，**0 個 ForTests／injectedFaints 成員**。這是實際 player 編譯與成品檢查，不只靠 Editor 測試推定。

| 驗證 | 實際結果 |
|---|---|
| 本分支完整 EditMode，6000.3.28f1，-nographics | **959/959**；0 failed／skipped |
| 本分支完整 GPU PlayMode，同版 Editor，未用 -nographics | **81/81**；0 failed／skipped；Direct3D 12、NVIDIA GeForce RTX 3060 |
| 外部同源副本 Windows x64 Mono player，一般 build | 成功；編譯符號與 metadata 如上 |
| 外部 Unity 突變：移除 Update→SyncViews | **13/13 pass**，證實 DS-02 |
| 外部 Unity 突變：移除真實 Faint 分派／升級延至 Tick 後 | 各 **0/1**，均 exit 8；每次分開修改並復原 |
| repo 外 CLR：舊 TangentToward／tau 係數 16→1 | 舊碼 **5/10 fail**；錯誤 tau **10/10 pass** 但額外 tau 見證失敗 |

正常兩套測試及 player build 未見 C# 編譯錯誤／警告。啟動有先失敗後恢復的 licensing handshake 訊息，結束有 debugger-agent／mono_thread_manage 訊息；不宣稱整份 log 零訊息。未實跑 IL2CPP 幾何比較、完整 player 遊玩或視覺截圖驗收。

所有實驗位於 `C:/Users/user/Documents/app/sol-r2-scratch/`：`editmode.xml`／`playmode.xml` 與同名 log；`player-build.log`、`mono/Cosmimera.exe`；`probe/`（正式原碼 SHA 清單、NUnit 方法重放及 Tick／tau／配置探針）、`mutate.py`、`tau-mutation.py`、`golden.py`、`metadata/`、`unity-mutations.py` 及三組突變 XML／log。Unity 副本的 RunSession 最後已復原並逐 byte 核對。重放入口：

```powershell
python -X utf8 C:/Users/user/Documents/app/sol-r2-scratch/mutate.py
python -X utf8 C:/Users/user/Documents/app/sol-r2-scratch/tau-mutation.py
python -X utf8 C:/Users/user/Documents/app/sol-r2-scratch/unity-mutations.py no-update-sync
```

提交前檢查 `git status --porcelain -- Assets tools` 為空；僅本報告及 phase-2.md 一行交接進入提交。兩檔以嚴格 UTF-8 解碼檢查。
