# P3-10 — SOL 對抗式審查

2026-11-09 · ChatGPT Sol（SOL）。範圍為指定的未提交程式、測試與新檔；未修改其他檔案，未執行 Unity。**high 1 / med 5 / low 1**。依 D0 規則，S1 必須先修，其餘可延後。

| ID | severity | file:line | finding | evidence | suggested fix |
|---|---|---|---|---|---|
| S1 | high | `Assets/_Project/Features/WorldGen/Runtime/Model/RunPlanner.cs:164`、`:189`；`Assets/_Project/Features/WorldGen/Tests/EditMode/RunPlannerTests.cs:194` | **修補仍未保證任務能依序完成**：DefeatWild 把後續 DefeatElite 的唯一目標也算入可用數量。有些 D0 seed 必須先殺掉該菁英才能湊滿 Count，之後便無法再完成菁英步驟。這是本次可完成性修補尚未處理的既有漏洞，非宣稱 v2 才引入。 | W1：翠綠星 seed `16756`，木屬性 Count=3，僅 spawn `2`、`20`（Normal）及 `40`（Elite）符合；下一步 TargetSpawn 正是 `40`。`QuestTracker.cs:78–88` 的 if/else-if 只推進當前步驟，同一擊倒不會再完成下一步。新測試只驗證三隻非 BOSS 的總數，這個失敗案例仍會通過。 | 先決定後續 TargetSpawn，計算 DefeatWild 候選時保留它；或只用 Normal 計算可用數。加上 W1 的回歸測試，以每個 spawn 最多擊倒一次的事件序列，驗證能依序到達 CaptureOne／DefeatBoss。 |
| S2 | med | `Assets/_Project/Bootstrap/Demo/ReproProbe.cs:88` | 每列建立的 PlanetTemplates 沒有 Dispose；Report 正常返回及中途例外都洩漏 Persistent native arrays。正式 RunSession 由 PlanetRig 接管並釋放，探針沒有這個 owner。 | `PlanetTemplates.cs:280–305` 明訂必須 Dispose，且每次配置三個 Persistent buffers。既有 `Logs/repro-editor.log:438` 實際記錄 `Persistent allocates 90 individual allocations`，正好是 30 列 × 3；該 log 的 NativeArray 洩漏約 27.5 MB。 | 在 AppendLine 用 try/finally 包住整段生成與取樣，finally 呼叫 `planetTemplates?.Dispose()`。 |
| S3 | med | `tools/repro/compare.py:31`、`:47` | 缺少報告格式及完整性檢查，兩份空檔、只有相同標頭的檔案，或相同的截斷前綴都能得到 IDENTICAL／exit 0；不是有效的重現性驗證。 | 靜態見證：`compare("", "")[0] == 0`；兩側只有相同標頭也如此。parse 不要求 magic/version、任何資料列或完整的 type×seed 集合；現有測試只檢查兩邊列集合不同，沒有檢查兩邊同樣缺列。未能執行 Python，這些是原碼控制流程推導。 | 驗證必要標頭、欄位型別及非空列；在格式中記錄預期列數／type、seed manifest，拒絕雙側同樣截斷的輸入，回傳 2。補空檔、標頭檔、同前綴截斷測例。 |
| S4 | med | `tools/repro/compare.py:66–75` | 樣本解析沒有拒絕 NaN／Infinity，也沒有先驗證五欄。非有限高度可被報成「最大差 0、超過 1 mm 的樣本 0」，誤導容差判讀；相同的無效 hex 甚至因字串相等直接跳過解析。 | 靜態見證：base 位元 `7fc00000` 對 `3f800000` 時 d=NaN，`max(0.0, d)` 留下 0，`d > 1e-3` 為 False；只增加 bit-different。相同三欄短列會在 `pa[3]` 觸發 IndexError，違反 docstring 的 ValueError 契約。現有測試未涵蓋這些輸入。 | 先檢查恰好五欄、合法 uint32 hex、唯一樣本鍵及有限 float，再計算統計；統一以 ValueError 拒絕格式錯誤，CLI 將無效輸入轉成 exit 2。 |
| S5 | med | `Assets/_Project/Bootstrap/Demo/DemoGame.cs:130–131` | 玩家探針的失敗路徑不保證退出。輸出目錄不存在、無寫入權限，或生成過程拋例外時，WriteReproReport 中斷 Awake，後面的 Application.Quit 永遠不執行；文件所列的 batchmode 命令可能持續掛著。Editor 入口已有 catch，玩家入口沒有。 | 例如 `-reproProbe <不存在的父目錄>/report.txt`，`File.WriteAllText` 在 `DemoGame.cs:140` 拋例外；TryRunReproProbe 沒有 catch/finally 或非零失敗退出路徑。此項為原碼推導，未啟動玩家。 | 在玩家入口捕捉生成／I/O 失敗，記錄例外並明確 Quit(1)；成功才 Quit(0)。輸出前驗證路徑，避免留下可誤認為本次產物的舊報告／樣本配對。 |
| S6 | med | `Assets/_Project/Bootstrap/Demo/ReproProbe.cs:129–130` | 探針完全漏測 LandingDirection。planDiscrete 刻意忽略方向合理，但另一欄 RunPlanner.Hash 也沒有混入 LandingDirection；固定的高度樣本同樣不依賴它。因此玩家實際降落位置改變時，所有欄位仍可能不變。 | `RunPlanner.cs:199–202` 在其他內容完成後才用 Scatter 生成 landingDir；`:207–246` 的 Hash 不讀它。`RunSession.cs:199` 將該方向傳給 PlanetRig，而 `ReproProbe` 沒有其他降落點摘要。 | 為 LandingDirection 增加獨立的位元／量化摘要或樣本，保留現有離散摘要用途；驗證只改降落方向時會被探針偵測。 |
| S7 | low | `Assets/_Project/Bootstrap/Demo/ReproProbe.cs:35–55`；`Assets/_Project/Editor/ReproBuild.cs:20–24` | 新公開 API 的 Pre 不完整或未檢查。Report 的已宣告 Pre 允許 `types=[null]`、空 wildPool，隨後卻在取 Size 或 Generate 失敗；也未宣告 wildPool 必須有 Adult 等下游條件。BuildAll 宣告 batchmode，卻未檢查便最終退出 Editor。 | Report 只檢查集合本身及平行長度，沒有檢查 type 元素或完整的生成前提；Generate 的必要條件列在 `RunPlanner.cs:51–74`。BuildAll 未檢查 `Application.isBatchMode`。正常 D0 呼叫目前滿足條件，未發現這些輸入已流入正式路徑。 | 補齊公開 Pre，並在配置 native 資源或切換 backend 前驗證；BuildAll 在 Editor 先要求 batchmode。補針對缺少 Adult／null type 的契約測例即可。 |

W1 的見證來自唯讀 Node.js float32 鏡像：PCG 使用 BigInt 做 64 位元截斷，幾何逐步 Math.fround；讀取 Data/_demo、實際 Copy 詞彙及 12 個模板的水域資料，依相同選模板／IsSea／Scatter 重試順序生成。wildPool 依 importer 的 Id 次序排列，排除 starter_，半徑使用 DemoGame.RadiusFor 的 D0 規則。鏡像先核對既有 `Logs/repro-mono.txt`，**30/30 個 planDiscrete 相同**。這是交叉驗證，不是 Unity 執行結果；維護者仍應在 EditMode 重現以下固定 seed。

| 星球 / seed | DefeatWild | 所有符合的非 BOSS spawn | 下一步菁英 | 鏡像 planDiscrete |
|---|---|---|---|---|
| verdant_planet / 16756 | wood × 3 | Normal 2、20；Elite 40 | 40 | `ed9b222b7bb23703` |
| lava_planet / 20308 | fire × 3 | Normal 19、39；Elite 42 | 42 | `15b340c9daf08773` |
| ice_crystal_planet / 14116 | water × 3 | Normal 23、27；Elite 46 | 46 | `b3be25f8623738ef` |

以第一列為例，先進入 ReachPoi 的 region 6，再依序擊倒 2、20、40，tracker 會停在 StepIndex=2、DefeatElite(TargetSpawn=40)。把菁英提前擊倒只會更早消耗唯一目標；`RunSession.cs:393–405` 會移除過期的昏厥野生個體，沒有重生。直接擊倒 BOSS 仍會讓 tracker 完成並結束 run，因此這是**無法正常依序完成任務鏈**，不是無法用 BOSS 捷徑結算。

其他核對結果：

- D0 六種星球、21 個野生型態的主屬性只有 metal／wood／water／fire／earth／ice／wind，各屬性三階段皆有。最小 D0 星球有 12 區，至少 22 隻 Normal；七種主屬性至少一種有四隻。偏好候選已要求至少三隻，fallback 最多者也至少三隻，故有效 D0 輸入下 Count 固定為 3，**不會是 0，BOSS 也不可能是唯一可用匹配**。問題是 S1 的後續目標被提前消耗。
- 新計數字典只做查找；MostCommon 明確按 Element 0..9 掃描且同數保留較小值。候選順序沿用已序列化 ElementBias，詞彙沿用 JSON 陣列；radius／seed 使用 InvariantCulture，hex 輸出沒有文化相關分隔符。未找到這次新增邏輯的字典迭代或文化相依分支。
- 探針的 PlanetConfig、ForPlanet、Choose／CountFor、Create、isLand、Generate 與 RunSession.Configure 的生成組裝相符。它測的是 managed 高度路徑，不能據此宣稱已覆蓋 Burst 地形網格、裝飾散佈或全部生成狀態；S2、S6 是具體缺口。
- 唯讀重算既有報告：Editor 與 Mono 除 env 外相同；Mono 對 IL2CPP 的 baseBits/baseMm/surfBits/surfMm 各 30/30 列不同，seaCount、planDiscrete 各 0/30 不同，plan 為曦影星 seed 1、42、11400714819323198485 三列不同。61,440 個樣本中，base 最大差 `0.0021715164184570312 m`、116 個超過 1 mm；surface 最大差 `0.0014972686767578125 m`、76 個超過 1 mm，皆無非有限值。這支持「已觀察到跨後端浮點差異」，**不能證明所有 seed 的離散決策相同，也不能證明 plan 差異只涉及方向**，因為目前沒有逐欄 plan dump 可定位雜湊差異。這是既有 Q-T6 的量測結果，不另冒充本次新增的 high 回歸；第二台機器驗收仍未完成。
- GeneratorVersion 1→2 合理：即使只改部分輸入的任務，仍改變相同 seed 的輸出；fallback 路徑還會改變後續 RNG 消耗。舊 fixture 若兩個偏好屬性都有至少三隻非 BOSS，候選與 RNG 呼叫可完全相同，故版本升級但 snapshot 數值不變並不矛盾。僅「偏好屬性在池內」不是充分理由；應另固定一個確實走 fallback 的 D0 快照。未在本環境重跑該 Unity 快照。
- 已嘗試 `python -m pytest tools/tests/test_repro_compare.py -q`，python 不在 PATH；改用已知 Python313 絕對路徑仍為 Access denied，故**未能重跑 pytest**，沒有沿用 Opus 的 8/8 當成本次驗證。資料與現存報告改用 PowerShell／Node.js 唯讀核對；Python 邊界案例為靜態分析。

verdict: **fail**
