FAIL · high 0 / med 1 / low 0

# Q-D1 窄範圍複審 · ChatGPT Sol

2026-11-11。基準 HEAD `b29039fb49dd0b9e70fe6c9b1d1685b22011f3eb`；審查目前工作樹 `git diff` 與未追蹤的 `DemoPlanetSizeTests.cs`。僅涵蓋 IMPL-01、IMPL-02、RunPlanner v4；不重報 `docs/tech-debt.md` 已列項目。僅新增本報告，不修改其他專案檔、不 commit、不開 sub-agent。

## Q-D1-IMPL-R2-01 · med · 有陸地仍被環掃描判為全海，合法輸入生成失敗

- **位置**：`Assets/_Project/Features/WorldGen/Runtime/Model/RunPlanner.cs:540`、`:558`；遮罩前置條件 `:56` 只要求 deterministic，沒有最小陸地寬度。
- **原因與影響**：有限的 0.01 rad 採樣不能證明連續球面沒有陸地。位於相鄰環之間的小島可能完全漏過，最後卻拋出 `the land predicate accepts no direction on the planet`。因此 v4 修掉海面出生後，仍會拒絕一部分具有合法出生點的遮罩。這與舊版返回海上 spawn 的缺陷不同。
- **實跑見證**：沿用 `RunPlannerTests` fixture，seed=1、R=320、Adult；先以無遮罩計畫取得 `c = dry.Regions[0].Center`，`t = PlanetFrame.FromUp(c).East`，建立：

  ```csharp
  float3 island = math.normalize(c * math.cos(.005f) + t * math.sin(.005f));
  bool IsLand(float3 d) => math.dot(d, island) > math.cos(.001f);
  ```

  `island=(-0.38487113,-0.6766045,0.6277585)`，`IsLand(island)==true`；這是具非零面積、確定性的球冠遮罩，名義半徑 0.001 rad（R320 約 0.32 m）。正式 `Generate` 經 **125844** 次 predicate 呼叫後拋 `ContractException`，未返回計畫。`r2 world`、`safety` 都重現；移除 `UNITY_EDITOR`／`DEVELOPMENT_BUILD` 的 Release 亦同。這是合乎目前 API Pre 的合成遮罩反例，未宣稱現有原生 template 已重現同一失敗。
- **建議修法**：不要把掃描耗盡當成「全星球無陸地」。讓地形端提供可枚舉的合法陸地候選／陸地見證，掃描找不到時仍能確定性地選取；若要保留純 predicate API，須明訂可驗證的最小陸地尺寸與覆蓋保證，再使採樣密度符合該契約。任意 deterministic 黑箱 predicate 無法靠有限次查詢證明全海；只縮小 Step 或修改錯誤文字不能完成現有承諾。保留真正全海時所有 build 明確失敗的行為。

## IMPL-01 複驗：關閉

**位置**：`Combat/Runtime/Model/Cast.cs:470`、`Combat/Runtime/Model/Encounter.cs:1167`（路徑皆在 `Assets/_Project/Features/`）。`r2 combat` **90 個檢查通過**，結果如下。

- 遞迴讀取匯出的 `Data/**/*.json`：符合 `on_move_used` 且包含 `range_mod_percent` 的 **8 筆／4 個唯一 trait**（來源與 `_demo` 各一份）全為正值：`light_heliostat_focus +6`、`wind_cyclone_instability +20`、`thunder_fulgur_reach +15`、`water_torrential_deluge +15`。沒有非正值反例。
- 對四個正式 trait 的射程成分實跑：連續 10 次預覽皆為 0，不觸發效果；首次 BeginCast 分別得到 6／20／15／15；再次施招得到 12／20／15／15；最後一次觸發的到期瞬間皆回到 0。搭配目前 ApplyRange 與最後 planet cap 的單調性，XML doc 的「當招新增效果只會讓預覽偏短」論證符合現有 Data。這不是對未來負射程事件特性的保證。
- 直接套用定時 ±50%：t=0、999 ms 仍有效；t=1000、1001 ms 恢復 0，無殘留。無觸發射程效果的 BeginCast 與 RangePercent 相等。負時間、watermark 前、超過 CombatTime.MaxMs 三種非法輸入均拋 `ContractException`。
- 暖機後，以 `GC.GetAllocatedBytesForCurrentThread` 量測，**RangePercent 1000000 次＝0 bytes；實際 FillOptions 1000000 次＝0 bytes**。FillOptions 透過預先建立的 bound delegate 呼叫，量測迴圈沒有反射參數陣列或 logging；buffer 已暖機。這不宣稱整個 Encounter.Tick 無配置。

`Encounter.Tick` 實跑，seed=5、Hold／Apex、全陸地，每 50 ms Tick 共 10 s；使用公開 Book.Apply，不注入內部狀態：

| R / 起始距離 / 效果 | 施放／命中 | 球面移動距離 |
|---|---:|---:|
| 320 / 23 m / −50% | 6／6 | 6.559165 m |
| 320 / 15 m / −50% | 7／7 | 0 m |
| 640 / 23 m / −50% | 6／6 | 6.350721 m |
| 640 / 15 m / −50% | 7／7 | 0 m |

正效果另用 base=24000 mm、+50%、2000 ms 到期、目標 30 m：直接觀察正式 FillOptions，R320 的包絡在 t=0／1999／2000 為 **25602／25602／24558 mm**，R640 為 **36004／36004／24558 mm**。隨後 Tick 全流程分別 **6／6、7／7**，均有走近。證明正效果消失後 AI 恢復未增益射程，而非保留舊快取。建議修法：本項無需追加修正。

## IMPL-02 複驗：關閉

**位置**：`Assets/_Project/Features/Combat/Tests/EditMode/PlanetRangeCapTests.cs:150`、`:268`；`Assets/_Project/Bootstrap/Tests/EditMode/DemoPlanetSizeTests.cs:14`。

相關 repository 測試 **278 pass／0 fail**，包含新測試；逐次僅改暫存副本的一個位置、重新編譯，以同一組 278 案執行：

| 突變 | pass／fail | 失敗見證 |
|---|---:|---|
| 額外：AI 的 rangePercent 固定 0 | 277／1 | 23 m 預期 7 命中，實得 0 |
| M3：僅 TryCast 改為 cap → ApplyRange | 276／2 | 23 m：6 次施放／0 命中；15 m：7／0 |
| M4：castIsRange 僅取 ranged，移除 melee AimPoint 分支 | 277／1 | melee Arc 的 CastDistance 實得 41184，預期 25597 |
| M5：正式 RadiusFor 的 Moon 改為 320 | 277／1 | 原樣 DemoPlanetSizeTests 預期 640，實得 320 |

四個突變均 killed；還原重建後 **278／0**。M5 編譯的是正式 RadiusFor 的原樣完整本體，放在原 namespace/type；Bootstrap 測試原檔未改寫。

另外 `probe coverage` **2／2**：melee Arc／Field 的 CastDistance 均由 **41184 → 25597 mm**，落點半徑各保持 **2288／3432 mm**。正式測試亦驗證四級 **320／640／960／1280** 與未定義 enum 拋例外。建議修法：本項無需追加修正。

## RunPlanner v4 其餘驗證

`r2 world` **28536 個檢查通過**。其中小島反例的檢查是確認缺陷可重現，不能將探針退出碼 0 解讀成產品通過。

- **v3 相容性**：從 b29039f 取完整 RunPlanner，僅改 class 名稱供同一程序並列比較。seeds 0..999 × R=320／640／960／1280 × 無 isLand／永真，共 **8000 個完整計畫**，全部欄位及每個方向的原始 float 位元（含 LandingDirection）相同。另用 `d.y<0.8`、R320、1000 seeds，以 callback 記錄是否有 Scatter 超過 8 次；**522 個全程在前 8 次成功的完整計畫**亦逐位元相同。直接呼叫兩版原始 Scatter，10000 seeds、非平凡 `d.y<0.2` 遮罩，**9997 次在前 8 次成功**，返回 float 位元及下一個 PCG32 輸出均相同；其餘 3 次不納入相容保證。
- **舊 seed1 見證**：Juvenile、`d.y<0.2`，R320 的 v3 有 **6／32** 隻在海上，v4 **0／33**；降落點亦在陸地。seed1／3 × R320／640／1280 六組全部 spawn 與降落點均在陸地。需要 fallback 的計畫不承諾維持舊 spawn 數或後續 RNG 結果。
- **真正全海**：R320／640／1280 均在 **125844** 次 callback 後拋 `ContractException`；Debug 與未定義 UNITY_EDITOR／DEVELOPMENT_BUILD 的 Release 各三組通過。例外確實沒有被 Conditional 剝除。`safety` 每個 build 各 **7 個檢查通過**，包含小島反例及下列成本見證。
- **成本**：單次耗盡含 8 次隨機、1 次中心、**125835** 個環掃描點；約 1.3e5 是單次搜尋上界。全海會在第一次失敗即終止。非空、稀少陸地可讓多個搜尋重複掃描：R1280、seed1、`d.y>cos(0.02)`，正式 Generate 實得 **5406238 次 callback／157 spawns**，全部落陸地；本機廉價 predicate 的 Debug 約 **295 ms**、Release 約 **88 ms**。32 區按目前最多 3 packs／3 members、30 elite、boss、landing，可有最多 404 次 Scatter，保守 callback 上界 **50840976**（結構推導，非實測達到）。成本為 O(Scatter 數 × Step⁻² × predicate 成本)，不能將 1.3e5 當整份非空計畫上界。本次沒有既定生成時間預算，也未量測原生 TemplateView，故不另列效能 finding。
- **BOSS 跨區與任務**：`d.y<0.2` 的 seed3／R640，BOSS 邏輯 region=1、`RegionAt(boss.Direction)=6`；seed1／R1280 為 29→24。六組以正式 `QuestTracker.OnKnockedOut(bossSpawn, …)` 均完成主線並賺得 DefeatBoss。另以 seed1／R320、BOSS site 周圍 1.2 rad 為海，明確得到 **5→7** 跨區；依序輸入正式 RegionAt／OnRegionEntered、野生擊倒、菁英擊倒、捕獲、BOSS 擊倒，**5 步皆 earned**。在目前已明示允許跨 Voronoi 邊界的規格下，這種 BOSS 移動對任務可接受；不能再把 MainRoute 末區當 BOSS 的物理位置保證。
- **ReachPoi 保持幾何區域語意**：RunSession `:1023` 以玩家位置呼叫 RegionAt；`:1611` 的 ReachPoi 導航仍指區域中心，`:1634` 的 BOSS 導航按 TargetSpawn 跟隨實體；QuestTracker `:66`、`:91` 分別按 region 與 boss spawn 身分完成。實跑 5→7 見證的 ReachPoi 仍能由指定 region 推進。半球遮罩 seed3／R320 的 ReachPoi 中心本身位於海上，移動 BOSS 不會將它遷往新海岸；玩家與角色依現行規則可進海，不能據此宣稱任務必卡死。本輪驗證止於 managed 事件接線，不宣稱原生尋路／場景可達性已驗收。

## Harness 來源、指令與通過數

暫存根目錄 `$h = "$env:TEMP/cosmimera-sol-qd1-impl-r2"`。從**目前工作樹**複製選入來源與全部 Data JSON；`manifest.json` 記錄 **181 檔**的 repo path、匯出路徑及 SHA256。交付前逐一重算原檔與副本，**0 差異**；兩份 RadiusFor projection 與正式本體逐字相同。v3 對照只允許 class rename。manifest SHA256：`0A90D5C38A410105D4AA1FACB4F820A75AC9225537E2DDD209659AD4F0683594`。所有突變只發生於暫存副本且已還原。

.NET SDK 9.0.304／net9.0；沿用前輪 NUnit、Unity.Mathematics、Newtonsoft.Json DLL、最小 UnityEngine shim 與逐案例 SetUp／TearDown runner。ScriptableObject 是 managed 實例，JsonUtility 由 Newtonsoft 代行；DemoGame 與 TemplatePlacement 只編譯原樣抽出本體，完整原檔另保存。Unity.Mathematics SHA256：`48049AFDD1E1FBA73BFFAE24A6DAB5E8B32BC63E387EC8BCEA53CB2A5382D12E`。

實際指令（腳本、探針、log 均保留在 `$h`）：

```powershell
$h = "$env:TEMP/cosmimera-sol-qd1-impl-r2"
powershell -NoProfile -ExecutionPolicy Bypass -File "$h/export-r2.ps1"
dotnet build "$h/Review.csproj" --nologo -v:q
dotnet "$h/bin/Debug/net9.0/Review.dll" PlanetRangeCapTests CombatAiTests CombatReviewSol MoveGeometryTests DashDisplacementTests Encounter TangentToward DerivedStatsTests DemoPlanetSizeTests
powershell -NoProfile -ExecutionPolicy Bypass -File "$h/mutate-r2.ps1"
dotnet "$h/bin/Debug/net9.0/Review.dll" r2 combat
dotnet "$h/bin/Debug/net9.0/Review.dll" r2 world
dotnet "$h/bin/Debug/net9.0/Review.dll" probe coverage
dotnet "$h/bin/Debug/net9.0/Review.dll" RunPlannerTests QuestTrackerTests
dotnet "$h/bin/Debug/net9.0/Review.dll"
dotnet "$h/bin/Debug/net9.0/Review.dll" safety
dotnet build "$h/Review.csproj" -c Release -p:DefineConstants=UNITY_INCLUDE_TESTS --nologo -v:q
dotnet "$h/bin/Release/net9.0/Review.dll" safety
powershell -NoProfile -ExecutionPolicy Bypass -File "$h/audit-r2.ps1"
```

| 自跑項目 | 結果 |
|---|---|
| Debug／Release build | 各 0 warnings／0 errors |
| 相關 repository 測試與突變還原 | **278 pass／0 fail** |
| RunPlannerTests＋QuestTrackerTests | **19 pass／1 fail** |
| 完整選入 managed 套件 | **397 pass／1 fail** |
| 射程／Data／配置探針 | **90 checks pass** |
| v3／v4、陸地與任務探針 | **28536 checks pass**，包含確認缺陷的反例檢查 |
| melee Arc／Field 獨立探針 | **2 checks pass** |
| Debug／Release safety | 各 **7 checks pass**，包含確認缺陷的反例檢查 |
| 工作樹來源 SHA256 | **181／181 相符** |

兩組 repository suite 的唯一失敗均為同一個既有 managed raw-bits snapshot：預期 **1601242826727298640**、實得 **9836354999031547987**，與前輪相同；未改 snapshot，未算成此次新 finding。同後端 v3／v4 的上述逐位元比較已通過。使用者提供的 Opus 原生 EditMode **1112／1112**、PlayMode **89／89**、0 警告，未冒充本輪自跑數。
