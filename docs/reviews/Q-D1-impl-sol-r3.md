FAIL · high 0 / med 0 / low 2

# Q-D1-IMPL-R2-01 窄範圍複審 r3 · ChatGPT Sol

2026-11-11。基準 HEAD `b29039fb49dd0b9e70fe6c9b1d1685b22011f3eb`；對象為目前工作樹的 `RunPlanner.cs`、`RunPlannerTests.cs`、`combat.md#只在陸地` 新句與 `tech-debt.md` 的 Q-D1-R5。**原 R2-01 med 關閉**：未找到符合新 Pre、卻漏掉 0.02 rad 圓帽的反例；兩項新 finding 都是註解的錯誤數學承諾，沒有重現出生點生成失敗。只新增本報告，不修改其他專案檔、不 commit、不開 sub-agent。

## Q-D1-IMPL-R3-01 · low · Demo 模板面積的「不足三分之一」不成立

- **位置**：`Assets/_Project/Features/WorldGen/Runtime/Model/RunPlanner.cs:30`。
- **實跑見證**：`r3 demo` **584773 checks pass**。0.45 rad 是 gnomonic **方形的邊中點**半角；單片面積為 `4·asin(sin²(h))`，六片不重疊時占球面 **0.3635269812**，超過三分之一。正式 `TemplatePlanner.Choose` 的 seed=0、count=6，中心最小距離 **1.3629742615 rad**，大於兩倍包覆圓帽半徑 **1.1986454563 rad**，確實不重疊。用正式 `PlanetTemplates.Create`／`TemplateView.IsSea`，將每片的四個像素全設為海，1000000 個等面積 Fibonacci 點得到 **363544 個海點**。這是模板 footprint／允許的最壞水遮罩見證，**不是宣稱 Demo 現有烘焙像素的海面占比為 36.35%**。
- **影響**：不推翻 Demo Pre；錯的是 XML 的面積數字。僅有「陸地面積很多」本身也不能保證包含指定半徑的完整圓帽。
- **建議修法**：刪掉 `under a third`，改用外擴 footprint 的證明：每片位於半徑 `α = atan(√2·tan(0.45)) = 0.5993227282` 的圓帽內；六個 `α + 0.02` 圓帽面積總和至多占球面 **0.5571846338 < 1**。因此必有點在所有外擴圓帽之外，其 0.02 rad 鄰域完整落在所有模板外，`IsSea` 必為 false。另加 0.0001 rad 數值裕量後仍只有 **0.5573587912 < 1**。

## Q-D1-IMPL-R3-02 · low · NearestLand 的額外 Post 有合法反例

- **位置**：`Assets/_Project/Features/WorldGen/Runtime/Model/RunPlanner.cs:555`。
- **實跑見證**：`r3 post` 在 Debug／Release 各 **4 checks pass**，其中包含確認此 Post 為假的檢查。取 `center=(1,0,0)`，以正式 frame、偏角 `0.0298f`、方位角 `math.PI/7f` 建立圓帽中心；`isLand(d) = dot(d, capCenter) >= cos(0.02f)`。中心在海上、predicate deterministic，符合 Pre。掃描返回陸地，實測：

  ```text
  capCenter = (0.9995561, 0.012927824, -0.026844902)
  distance(center, capCenter) = 0.029800000500457217 rad
  result ring               = 0.01999999881088643 rad
  result ring + Step        = 0.029999998810886427 rad
  ```

  圓帽中心確實比 `result ring + 1 step` 更近，違反 XML 的 `no cap ... lies closer than result's ring + 1 step`。不是因為直接呼叫時略過了 Scatter 的中心檢查；此例 `isLand(center)==false`。
- **影響**：返回點仍合法；錯的是新增的最近圓帽 Post，非最小圓帽覆蓋承諾。
- **建議修法**：移除這個額外 Post，保留 `isLand(result)`；若要承諾近似最近性，重新明訂比較的是圓帽中心或邊界，再證明帶數值裕量的不等式。

## 覆蓋與 float 邊界實跑

`r3 coverage` 在 Debug／Release 各 **12097644 checks pass**，結果相同：

- 12 個掃描中心：六個座標軸方向、兩個一般方向、四個跨 `PlanetFrame` 極點 fallback 門檻的方向。每個中心透過**正式 NearestLand** 的永假 predicate 收集 **125835 個實際 float 點**，共 **1510020 點**。
- 在每對相鄰環的中點，按該環點數枚舉方位角的整點、1/4、1/2、3/4 相位；另測 0、subnormal、1e-8、0.0001、0.005 附近、首環，以及 π−0.02、π−0.01、π−0.001、π、π+0.005。總計 **6045876 個位置**。π+0.005 是跨過對跖點的參數化位置，沒有把角度截斷成 π。
- 使用獨立 KD-tree，以 double 三維弦長對實際採樣點搜尋最近鄰；每個 frame 再以六個位置全點 brute force 核對，**72／72 相同**。所有取得鄰點的 geodesic 距離與正式 float-dot 圓帽判定都通過；另直接執行 **5796 次正式 NearestLand**，全部找到陸地。新增 repository 測試本身另涵蓋 **3000 個 seeded 隨機中心／圓帽**與 **20 份單島完整計畫**。
- 最大觀測鄰點角距離 **0.00999999982222418 rad**；環間中點網格上的最大鄰點角距離 **0.007069105664287669 rad**。這是上述弦長鄰點提供的覆蓋上界，沒有冒充所有 float 中心的全域最大值。
- 對這 12 個 frame，網格的球面覆蓋半徑可用「半個徑向間隔＋半個方位弧段」界定：`0.005000114440917969 + 0.0016289052105272798`。加上網格最大最近距離與 **2e-6 rad** 裕量，得到連續球面的保守上界 **0.013700125315732918 rad < 0.02**。逐點量得 float 建點相對 double 正交 frame 的最大角誤差 **1.8776660609618513e-7 rad**，點長最大誤差 **1.5847580470129685e-7**；裕量涵蓋本次建點與方位參數取整誤差。此界限針對上述 frame，並非任意後端的 IEEE 浮點形式證明。

原行內論證應讀作理想幾何簡寫，不能逐字套到 float 與退化環：實測相鄰 `k*Step` 最大間隔 **0.010000228881835938 rad**，不是嚴格 ≤0.01；首環 **n=7**、末環 **n=6**，原點到首環約 **0.01 rad**，也不是 Step/2。`r3 post` 實跑確認首／末環與原點案例。

覆蓋可用更直接的邊界拆分補足：離原點 <Step/2 的圓帽中心，到首環點至多約 1.5 Step；離對跖點 <Step/2 時，末環退化點直接在圓帽內，跨 π 同理；其餘位置選最近環，再沿該環至最近採樣點，以球面路徑長的三角不等式得到約 Step 的界。三者都與 2 Step 留有裕量。這避開在 d≈0／π 把退化環當作具有固定長度相交弦的問題；本輪未找到漏島反例。

## v3／v4 相容性

`r3 compat` Debug／Release 各 **68516 checks pass**。基準版是 `git show b29039f:.../RunPlanner.cs` 的完整檔案，只將 class 名稱改成 `RunPlannerV3`，交付前逐字核對。

| 比較 | 實跑結果 |
|---|---|
| seeds 0..999 × R320／640／960／1280 × null／永真 predicate | **8000 份完整計畫**，所有非浮點欄位與全部方向原始位元相同，含 LandingDirection |
| `d.y<0.8`、R320、1000 seeds，以 callback 追蹤是否超過前八抽 | **522 份**從未 fallback 的完整計畫逐位元相同 |
| 直接呼叫兩版 Scatter；maxAngle=0／0.1／0.6，各 10000 seeds，`d.y<0.2` | **29997／30000 次**在前八抽成功，返回方向位元與下一個 PCG32 輸出相同；另三次不屬於相容承諾 |

NearestLand 內重算 t／b 沒有改動成功抽樣分支或消耗 RNG。`r3 snapshot` 另 **2 checks pass**：seed12345 的完整 v3／v4 計畫相同，兩版 raw digest 都為 **9836354999031547987**。

## 新測試的退化偵測

三個新測試在 Debug／Release 與突變還原後均 **3 pass／0 fail**。每次只改暫存來源的一處、重建，再執行原樣三個測試：

| 暫存突變 | pass／fail | 偵測結果 |
|---|---:|---|
| M1：NearestLand 的區域 Step 改 0.03，MinLandCapRad 仍為 0.02 | **2／1** | `The_sweep_finds_every_minimum_cap` 拋 ContractException；重現 Opus 的退化見證 |
| M2：NearestLand 的 b 軸歸零，掃描塌成一條方向 | **1／2** | sweep 與 lone-island 兩項均失敗 |
| M3：耗盡時改為 `return center` | **2／1** | narrow-island 預期 ContractException 卻未拋出 |
| M4：共用 LandSweepStepRad 改 0.03，MinLandCapRad 隨之變為 0.06 | **3／0** | 測試跟隨宣告的最小半徑；它們沒有獨立釘住文件的 0.02 數值 |

前三個退化均 killed。M4 是測試界限：它改了契約常數本身，並未證明新契約的覆蓋失敗；不可把 M1 結果描述成「修改任何步長常數都會被抓到」。窄島測試驗證指定漏島 fixture 的耗盡行為，也不表示所有小於最小半徑的島都必須被拒絕；小島恰巧被掃到仍可返回。

## Demo Pre、失敗模式與 Q-D1-R5

`RunSession.cs:371` 實際採 `CountFor`，`:380` 使用 `!view.IsSea(d)`。本輪直接編譯完整正式 `PlanetTemplates.cs`，沒有重寫 IsSea。依 R3-01 的**外擴面積上界**，任意排列的 ≤6 個 0.45 rad 模板都留下至少一個完整 0.02 rad 陸地圓帽；此結論不依賴 seed、模板像素或模板之間是否重疊。null predicate 則全陸地。

`r3 demo` 遍歷四種 PlanetSize × seeds 0..999，共 **4000 份正式 placements**，使用全海像素作最壞遮罩：**512000 次位置查詢**、**288000 次圓帽見證查詢**均符合預期；每份獨立尋得圓帽見證，其中心到模板包覆圓帽的最小淨空至少 **0.3755258479 rad**。另由模板中心呼叫正式掃描，均找到陸地。NativeArray 以 managed 陣列 shim 代行，不宣稱驗證原生配置器或 Burst。

`r3 safety` Debug／Release 各 **8 checks pass**：R320／640／1280 的全海輸入均於 **125844 次 callback**後拋出 `Precondition failed: isLand must contain a land cap of radius MinLandCapRad`；舊 0.001 rad 漏島也以同一 Pre 訊息失敗。Release 明確未定義 UNITY_EDITOR／DEVELOPMENT_BUILD，沒有剝除例外。R1280、seed1、0.02 rad 極小陸地再次得到 **5406238 次 callback／157 spawns**，所有 spawn 與 landing 均在陸地；Debug **303.089 ms**、Release **87.903 ms**。Q-D1-R5 的量級與歷史見證一致；未將單次掃描上界當整份計畫上界，未重報該效能債。

## Harness、指令與來源一致性

沿用 r2 managed harness，另存於 `$env:TEMP/cosmimera-sol-qd1-impl-r3`；.NET SDK **9.0.304／net9.0**。初始以 r2 export 腳本刷新工作樹來源；NUnit、Unity.Mathematics、Newtonsoft.Json、UnityEngine shim 與逐案例 SetUp／TearDown runner 沿用。新增 NativeArray shim 僅為編譯完整正式 PlanetTemplates。腳本、探針、逐項 log 均保留；所有突變僅作用於暫存副本，已還原。

實際執行的核心指令（`r3-*.log` 保存輸出）：

```powershell
$h = "$env:TEMP/cosmimera-sol-qd1-impl-r3"
dotnet build "$h/Review.csproj" --nologo -v:q
dotnet "$h/bin/Debug/net9.0/Review.dll" RunPlannerTests
dotnet "$h/bin/Debug/net9.0/Review.dll" The_sweep_finds_every_minimum_cap A_lone_island_of_the_minimum_cap_is_always_found Land_narrower_than_the_minimum_cap_violates_the_pre
dotnet "$h/bin/Debug/net9.0/Review.dll" r3 coverage
dotnet "$h/bin/Debug/net9.0/Review.dll" r3 compat
dotnet "$h/bin/Debug/net9.0/Review.dll" r3 demo
dotnet "$h/bin/Debug/net9.0/Review.dll" r3 safety
dotnet "$h/bin/Debug/net9.0/Review.dll" r3 post
dotnet "$h/bin/Debug/net9.0/Review.dll" r3 snapshot
powershell -NoProfile -ExecutionPolicy Bypass -File "$h/mutate-r3.ps1"
dotnet build "$h/Review.csproj" -c Release -p:DefineConstants=UNITY_INCLUDE_TESTS --nologo -v:q
dotnet "$h/bin/Release/net9.0/Review.dll" The_sweep_finds_every_minimum_cap A_lone_island_of_the_minimum_cap_is_always_found Land_narrower_than_the_minimum_cap_violates_the_pre
dotnet "$h/bin/Release/net9.0/Review.dll" r3 coverage
dotnet "$h/bin/Release/net9.0/Review.dll" r3 compat
dotnet "$h/bin/Release/net9.0/Review.dll" r3 safety
dotnet "$h/bin/Release/net9.0/Review.dll" r3 post
powershell -NoProfile -ExecutionPolicy Bypass -File "$h/audit-r3.ps1"
```

| 自跑項目 | 通過數／限制 |
|---|---|
| Debug／Release build | 各 **0 warnings／0 errors** |
| 原樣 RunPlannerTests | **22 pass／1 fail**；唯一失敗為既有 managed raw-bits snapshot |
| 新增三個測試 | Debug／Release／突變還原各 **3 pass／0 fail** |
| coverage | Debug／Release 各 **12097644 checks pass** |
| compat | Debug／Release 各 **68516 checks pass** |
| demo | **584773 checks pass**，含確認 R3-01 的反例檢查 |
| safety | Debug／Release 各 **8 checks pass** |
| post／極點 | Debug／Release 各 **4 checks pass**，含確認 R3-02 的反例檢查 |
| baseline snapshot 對照 | **2 checks pass** |
| 工作樹與來源副本 SHA256 | **183／183 相符，0 差異**；v3 只改 class 名稱 |

既有 snapshot 預期 **1601242826727298640**、實得 **9836354999031547987**，與 r2 相同；本輪同後端 v3 控制組也得到實得值，故不列為本修正回歸。使用者提供的 Opus 原生 EditMode **1115／1115**、PlayMode **89／89**、0 警告，未冒充本輪自跑數。

`manifest-r3.json` SHA256：`4BDB12DB8380EB9D13A61407837E100E6B5FC83A5FC9F29229E8AEDA24FD58D2`。交付前 audit 同時重算工作樹原檔與暫存副本，沒有只檢查副本。

| 審查檔案 | SHA256 |
|---|---|
| RunPlanner.cs | `03D192A5ECEA4001E22B81AF5642228248F8929EC6FC84530C385F9FEC557655` |
| RunPlannerTests.cs | `767F81B863F6BEB492EBCB3C984D8732EF447ED4118CF9D2F8ECA659C345C11A` |
| combat.md | `EAB86F31497271DAED89424E9E86392C2F0078323553C25DA344E57AC78C7A9D` |
| tech-debt.md | `D7498E34B11B01F288AAAC84B7BBE9AE292CEA99008BBF338C1D44383B2600B5` |

Unity.Mathematics DLL SHA256：`48049AFDD1E1FBA73BFFAE24A6DAB5E8B32BC63E387EC8BCEA53CB2A5382D12E`。`R3Probe.cs` SHA256：`5BDEEEE3D0FB8012E56CAF3B92D48FA61AF4338DD6B99EF81C159613C05A8C9A`。
