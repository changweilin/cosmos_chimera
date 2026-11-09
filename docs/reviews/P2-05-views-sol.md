# P2-05 模型替換介面對抗式審查 · SOL

2026-11-09 · SOL（ChatGPT Sol，Codex）。對象：`b7266b92ecfc3c0af33d202fb893822039706c5b` 相對 `874ea8d` 的完整 delta（6 檔）；下列 finding 行號均依 `b7266b9`。已讀 AGENTS.md、CLAUDE.md、code-contracts／unity-csharp／testing／docs-and-progress、roadmap／分工，以及 art-asset-pipeline.md〈佔位模型（D0）〉。唯一新增檔案為本報告；依本次限制不更新 roadmap、不改程式、不 commit。

**Verdict：pass（D0 gate；0 high／1 med／1 low，另 1 doc）。** 未發現阻礙遊玩的行為差異；材質生命週期較舊碼完整。以下項目可依「只修 D0 high 與阻礙遊玩問題」原則延後。此結論基於完整 diff、原碼與測試控制流程的閱讀證明；**本輪未執行 Unity Test Runner、CLR 探針或 mutation tests，沒有宣稱 PlayMode 通過或完成 GPU 外觀驗收**。

| ID | severity | file:line | finding | evidence / failure scenario | suggested fix |
|---|---|---|---|---|---|
| V-01 | med | `Assets/_Project/Bootstrap/Tests/PlayMode/RunSessionProgressionTests.cs:90`、`:113`、`:804`；`Assets/_Project/Bootstrap/Tests/PlayMode/CreatureViewsTests.cs:67`、`:75`、`:78` | **接線與外觀回歸仍有測試盲點。** 單獨 view 的變灰／釋放有測，RunSession 的逐幀同步沒有被驗證。 | `RunSession.Update:399` 首次成功降落只做 SummonOpening，於 `:405` return；Launch 見到 Started 後立即在 `:805` 停用 session。RecordingViews 只記 request／root，沒有 ShowFainted／Dispose 計數。因此僅刪掉 `RunSession:1076` 的 ShowFainted 呼叫，不影響新增測試的任何斷言；將 `:1366` 改回直接 Destroy(root)，也可滿足 `:113`，但會漏掉材質清理。獨立 view 測試直接呼叫 ShowFainted／Dispose，不能補上接線證明。另未斷言 primitive mesh、shader 或 `_OutlineWidth`；色彩期望值又取自被測的 ColorOf。改為全用 sphere、將外框 0.03 改零，或改錯色盤，都有未被斷言約束的回退。以上是閱讀推導，未執行 mutation。 | 以包裝 view 記錄 ShowFainted／Dispose，實際經過 SyncViews，涵蓋 fielded／收回／Gone／倒下與重複 Dispose；在 session 銷毀後同時斷言每個 view 的材質已銷毀。以固定基準驗證 capsule／sphere、十屬性色及 toon shader／0.03 m 外框，避免由同一 helper 產生期望值。D0 可延後。 |
| V-02 | low | `Assets/_Project/Bootstrap/Demo/CreatureViews.cs:110`、`:113`；`Assets/_Project/Bootstrap/Tests/PlayMode/CreatureViewsTests.cs:55` | **非法半徑在配置場景物件後才拒絕，留下無 owner 的 primitive。** | Editor／Development 中，以非 null Def、BodyRadiusM=0 呼叫 Create：`:110` 已建立 GameObject，`:111` 只排程刪 Collider；`:113` → Scale 的 `:93` 拋 ContractException，尚未回傳 view，也未進入 RunSession 的 `_views`。該 GameObject 無人 Dispose。現有 Pre 測試只對 Scale 傳零半徑，對 Create 只測 null Def，抓不到這條副作用路徑。D0 的 Encounter.BodyRadiusFor 提供正半徑，故不是現行遊玩洩漏。 | 將 Scale(request) 移到 CreatePrimitive 前；補 Create 非法半徑案例，確認拋錯前後不增加 scene root。不要求 Release 接受違反 Pre 的輸入。 |
| V-03 | doc | `Assets/_Project/Bootstrap/Demo/CreatureViews.cs:44`、`:58`、`:111`、`:175`；`Assets/_Project/Bootstrap/Demo/RunSession.cs:234` | **契約對 Destroy 的完成時點及注入副作用描述過強。** | Create 的 Post 寫回傳時 Root「has no Collider」，實際使用延遲 Destroy；Dispose 的 Post 寫回傳後沒有物件／材質，實際也只排程 Destroy。測試 `CreatureViewsTests:71`／`:84` 都先 yield 才驗證，未證明同步 Post。相反地 `_root = null` 在 Dispose 中立即發生，之後 Root 會拋 ObjectDisposedException。UseViewFactory 寫 Side effects:none，`:239` 卻更新 `_viewFactory`。這些沒有改變舊版 Collider 移除時序。 | 明載「排程銷毀、於 Unity 完成 Destroy 後不再存在；Dispose 後立即禁止 Root／ShowFainted」，Create 的 Collider 保證也標明時點。UseViewFactory 記載修改所選 factory。若要求同幀不參與物理，可先停用 Collider 再 Destroy，但那是另行決定的行為變更。 |

行為與生命週期核對：

| 面向 | 證據與結論 |
|---|---|
| 幾何、尺寸、色彩 | 舊 `RunSession:1044–1058` 對應新 `CreatureViews:72–77`、`:94–95`、`:110–127`：玩家側 capsule、野生 sphere；名稱不變；`BodyRadiusM × 2 × (Boss 2.5 / Elite 1.4 / 其他 1)` 不變；十色陣列逐字相同；同一 PlanetLook.CreatureMaterial、同一 shader／color／0.03f。Collider 仍使用延遲 Destroy，沒有新增時序差異。 |
| 啟用、定位、倒下 | 新 `RunSession:1047` 保持建立後 SetActive(Fielded)。`:1050–1078` 的可見性仍為 Fielded 或未 Gone 的野生，Gone 強制隱藏；隱藏者跳過定位與變灰；可見者以同一球面 up／LookRotationSafe 定位，倒下時每幀呼叫冪等 ShowFainted。新 `CreatureViews:159–168` 與舊碼使用相同灰色／材質欄位。沒有新增復活還原色行為，與基線一致。 |
| sharedMaterial／material | Toon 分支每次建立一個新 Material，以 sharedMaterial 掛到 renderer；這不代表跨 view 共用同一個 instance。ShowFainted 直接操作該 instance，不再透過 renderer.material getter。Fallback 仍在建立時讀 renderer.material 取得該 renderer 的 instance，並保存供清理。沒有修改 primitive 的共用預設材質資產。舊 OnDestroy 只 Destroy GameObject，沒有處理執行期 Material；新 `CreatureViews:180–182` 明確釋放兩分支各自持有的材質。 |
| Dispose／OnDestroy | `RunSession:1350–1367` 遍歷所有已登錄 view，包括 inactive view；僅呼叫 Dispose，不再存取 Root。`CreatureViews:173–176` 立即清空 `_root`；故 Dispose 後 Root 拋例外符合其 not disposed 前置條件。第二次 Dispose 不會再處理 root；同幀仍可能再次對尚待銷毀的材質呼叫 Destroy，下一幀材質的 Unity null 判定會跳過。未發現目前流程因重複 Dispose 而存取已銷毀 Root 的路徑。即使 root 被外部先銷毀，材質清理仍位於 root 判定外。 |
| 血條 | `RunSession:1112–1156` 相對舊 DrawWorldBars，唯一替換為 `_views[id].Root.gameObject.activeSelf`；其餘邏輯相同，包括未 engaged 活野生隱藏、距離篩選及依 BodyRadiusM 算高度。沒有意外改成 renderer 可見性或骨骼查找。 |
| 每幀配置 | 新 SyncViews 只增加介面呼叫與 class reference 讀取；Root getter 不配置，ShowFainted 只寫已保存 Material。沒有新增 LINQ、閉包、容器或 renderer.material getter。`CreatureViewRequest` 是 readonly struct，且只在 CreateView 建立。原有 `foreach (_encounter.Units)` 經 IReadOnlyList 列舉的潛在配置仍存在於兩版；本結論是「delta 未新增配置」，不是整個 SyncViews 已達零配置，也不是 Unity Profiler 量測。 |

模型替換能力：目前介面足以在**不修改 RunSession** 的情況下換成多 renderer、SkinnedMeshRenderer／骨架或 prefab：factory 依 request.Def 辨識物種／階段，回傳一個穩定 wrapper Root；模型子階層的軸向、pivot、比例、各材質及骨架由 view 持有。RunSession 不再假設 root 有 Renderer 或單一材質。新 view 的 ShowFainted 可逐一處理自己擁有的材質；Dispose 必須清理自己建立的 instance，不可銷毀共用資產。這不需要先定案任何載入技術。

現有承諾是模型替換；介面沒有攻擊／移動動畫狀態。血條也沒有模型提供的骨骼 anchor，仍依碰撞半徑定位（`RunSession:1128`）。若未來驗收要求血條跟隨真模型頭骨，最小缺口是 view 提供 WorldBarAnchor，由 DrawWorldBars 讀取；若要求招式動畫同步，另需明確的表現事件。兩者均不是目前 D0 替換契約中的阻塞要求，沒有因此判為缺陷，也未要求現在實作。

測試防線的閱讀判定（均非實跑結果）：

| 刻意回退 | 現有斷言能否攔截 |
|---|---|
| Configure 完全忽略注入 factory | 能：RecordingViews.Requests 維持空，`:98` 與 encounter 單位數不符。 |
| OnDestroy 完全移除 view 清理 | 能：placeholder root 未 parent 至 session，刪除 session 不會連帶刪它們，`:113` 應失敗。 |
| PlaceholderView.ShowFainted 變成 no-op | 能：獨立 view 測試 `CreatureViewsTests:80` 應讀到原色而非 Color.gray；兩個材質分支各有案例。 |
| RunSession 不呼叫 ShowFainted | 不能：見 V-01，獨立 view 測試仍自行呼叫。 |
| PlaceholderView.Dispose 漏刪 Material | 能：獨立 view 測試 `:86` 檢查保存的 material reference。 |
| OnDestroy 只 Destroy root、不呼叫 Dispose | 不能：整合測試只保存 roots；獨立 Dispose 測試不會受到影響，見 V-01。 |
| Dispose 同幀呼叫兩次 | 有呼叫覆蓋：獨立測試 `:82–83`，之後檢查 root／material 已銷毀；沒有 Dispose → session OnDestroy 的組合或 Dispose 後 Root 存取測試。 |
| Configure 後更換 factory | 能：`RunSessionProgressionTests:108` 預期 ContractException。Configure 前傳 null 尚無測試。 |

實際驗證：對 git 物件執行 7 項不寫檔的 PowerShell 原碼比對，全部通過：十色色盤相同；SyncViews 在 view 取用／ShowFainted 呼叫替換後相同；DrawWorldBars 僅 Root 取用不同；capsule／sphere 條件對應；外框仍 0.03f；OnDestroy 不讀 Root；完整 delta 恰為上述 6 檔。另外核對 commit 的 manifest／lock 及本機對應 Test Framework 1.6.0 原碼：UnityTestAttribute 使用 ParameterDataSourceProvider／CombiningStrategyAttribute 建構參數案例，未發現本測試使用 `[Values(true, false)]` 的不相容證據。這些是來源比對，沒有模擬 Unity 的材質 cloning、延遲 Destroy、GC 或實際畫面。為維持本輪唯讀範圍，未啟動會匯入／寫入專案的 Unity Editor；原生 PlayMode 與 GPU 驗收留給具可寫測試副本的環境。

交接：SOL done，D0 pass；OPUS 可依 D0 原則延後 V-01／V-02／V-03，後續補原生 PlayMode 與外觀驗收。所有 finding 已重新核對 `b7266b9` 原碼行號及證據。
