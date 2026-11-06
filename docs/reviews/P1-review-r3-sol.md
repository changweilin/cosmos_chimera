# P1 第三輪對抗式複審 · SOL

2026-11-06 · ChatGPT Sol（SOL）。總判定：**fail**；A：**fail**；B（本輪 B2–B4）：**pass**。B1 依 Q-T6 待使用者決策，不列入本輪否決理由。

對象：分支 `p1/terrain-mesh-height`、HEAD `b14c062` 上未提交的工作樹修正；依指定 `git diff HEAD -- Assets/_Project/Features/Planet docs/architecture/planet-tech.md`，另讀未追蹤的 R2 regression 與兩份 v3 快照。下列程式路徑相對於 `Assets/_Project/Features/Planet/`。已讀指定規範、r2 報告、修正交接與 Q-T6；本次單檔限制優先，不修改 roadmap。

## 方法與限制

未啟動 Unity／Unity.exe；所有探針與變異均在 repo 外暫存副本執行。唯一 repo 寫入為本報告；忽略其他 agent 的 Player／Data 工作。Windows x64、SDK 9.0.304、.NET 9 Release、`UNITY_EDITOR`，引用真實 Mathematics 1.3.3 DLL 與 NUnit，不替換 float3/math。DLL SHA-256：`48049AFDD1E1FBA73BFFAE24A6DAB5E8B32BC63E387EC8BCEA53CB2A5382D12E`。

執行六組 fixtures（PlanetFrame／CubeSphere／Quadtree／Height／ReviewRegression／ReviewR2Regression），實際執行 **63 pass / 2 fail**；失敗僅兩份高度快照：預設提交值 `0xB7C5CA249FD8C22D`、CLR 值 `0x383CA51ADC74C561`；8 octave 提交值 `0xAF9DD87B0574B16F`、CLR 值 `0xB94911EBA33AB6D5`。這屬已知 Q-T6 的執行環境差異，不能當成新變異被殺死。

為隔離該差異，**只在暫存目錄**建立上述 CLR 基線快照，再逐一套用 31 種變異；校準後基線與全部還原後皆 **65/65**。測試副本只替換 TestContext 輸出、移除未使用的 UnityEngine using、替換 Application.dataPath；正式快照未改。變異的 kill 必須相對於這個全綠基線。本結果不是 Unity Test Runner／Burst／IL2CPP／ARM64 驗證，也不涵蓋 Mesh／PlayMode fixtures。

可重跑 harness：`%TEMP%/cosmimera-r3-sol-1917c6a9169140aba290acfad92064ff`。設 `DOTNET_CLI_HOME=<目錄>/.dotnet` 後執行 `dotnet run --project <目錄>/Probe.csproj -c Release -- tests` 或 `-- probe`；保留 `mutate.ps1`、`mutations.json`、`probes.log`、`old-graph-mutation.log`。harness 的快照已校準為 CLR 值，只供本機變異比較。

## A · A1–A3 複審

| Finding | 判定 | 證據 |
|---|---|---|
| A1 | partially fixed | `Runtime/Model/ChunkQuadtree.cs:57` 正確拒絕原 D7／D9、margin=313.6 的反例；預設遲滯、合法 margin 上限、D5/7/9/11/16/20 首次 Update 得 1,248/2,124/2,244/2,328/744/732 葉。固定 margin 的原成長路徑已封住，但合法大遲滯仍可累積全球，見 A1-R3。 |
| A2 | fixed | `Api/PlanetFrame.cs:105`、`:118` 拒絕巨大有限輸入，2× slack 足以接住最大約 √3× 的投影。10,000 個隨機框架與 ±1e18 分量探針，最大 local/M=1.7319556、round-trip 最大分量誤差/M=6.184753e−7；直接 2M local 輸出亦有限。新測試仍有缺口，見 N2。 |
| A3 | fixed | `Tests/EditMode/PlanetReviewR2RegressionTests.cs:66`、`:73` 鎖住負 margin 與兩個實際 dictionary comparer；允許負數、兩字典同時／各自恢復預設 hash 均被殺死。 |

### A1-R3 · med · 合法遲滯仍讓葉集合累積成整球最細層

位置：`Runtime/Model/ChunkQuadtree.cs:57`、`:228`；`Runtime/Model/ChunkLodSettings.cs:54`。

重現：R=640、Center=0，settings=(D,32,2,1e6,0)，符合所有新 Pre。令 n=2^(D−3)，依 face=0..5、x/y=0..n−1，依次 Update camera=`641.7 * FaceToDirection(face, -1+(x+.5)*2/n, -1+(y+.5)*2/n)`。D=4/5/6，經 24/96/384 步後分別 **1,536 / 6,144 / 24,576 葉**，恰為 **6·4^D**；最後移至 (10000,0,0) 仍完全保留。

原因：新限制只界住 HeightMargin；已分裂節點的合併半徑仍乘上無上限的 `(1+MergeHysteresis)`。這是原工作量問題的殘留路徑，並非本次新增的回歸。以最細邊長界住 margin，尚不足以保證所有合法設定都只保留局部葉集合。

建議：同時限制有效合併係數 `SplitDistanceFactor * (1+MergeHysteresis)`，或加入維持平衡與原子發布的硬工作量預算；新增跨多個相機位置、合法極限遲滯的累積測試。

## B · B2–B4 複審

| Finding | 判定 | 證據 |
|---|---|---|
| B2 | fixed | `Api/PlanetHeight.cs:85`–`:89` 限制 amplitude 與最高 octave frequency；原 MaxValue 反例被拒，極限合法參數的有限／界限測試通過。octaves 先於 frequency 檢查；額外測 int.MinValue、−33、−1、0、9、33、int.MaxValue，搭配 MaxValue frequency，皆先拋 octave ContractException。後續 clamp 不會提前在無效 octave 上求值；Release 的 Contract 呼叫連參數求值都會剝除。 |
| B3 | fixed | `Api/PlanetHeight.cs:68` 明說 clamp 後上界；R2 regression `:141` 的 raw simplex>1 見證會殺死移除 clamp；新增 oct8 快照會獨立殺死 `_o7→_o0`。零 amplitude、1／8 octave 與極限參數皆有實際取樣。 |
| B4 | fixed | `Api/PlanetHeight.cs:138`、`:147` 共用一次 normalize；R2 regression `:175` 對 4096 個單位方向及其 1.0004 倍比較 Sample/Core 原始位元，全過。復原舊 Sample/Core 運算圖會獨立殺死該測試。CLR 位移接縫亦全過，限制見下。 |

SampleCore 新 normalize 對同後端、同方向位元施加同一運算。額外比對 6 面×4 邊×2049 個 dyadic 參數，經 Core 取樣並算 `dir*(R+height)` 後，位置位元不一致為 **0/49,176**。這支持本機接縫；未重新執行原 `ChunkMeshTests` 的 Burst 接縫／跨層／法線測試，不能把 CLR 結果延伸成 Burst 證書。

`GeneratorVersion` 2→3 正確：公開 Sample 的運算圖不變，預設 hash 沿用 v2 合理；網格 Core 多做一次 normalize，輸出會改，所以仍須 bump。交接已明記網格改變、公開 Sample 不變及新增 oct8 快照。未發現本次修正使 B1 惡化的可驗證證據；Q-T6 維持待決。

## 新增 findings

### N1 · med · 新 margin 前置條件未同步到公開 Configure

位置：`Runtime/Model/ChunkQuadtree.cs:57`；`Runtime/PlanetTerrain.cs:97`、`:116`。

重現：`config=new PlanetConfig(640,float3.zero)`、`height=PlanetHeight.ForPlanet(config,42)`，依 Configure 的 1 m 間距、quadsPerEdge=2 建立 settings，得到 D=9、margin=37.8 m；新上限為 31.415928 m，建立 quadtree 拋 ContractException。兩個輸入皆有效，2 也符合 Configure 明文允許的 [2,128] 二次冪。這是純 C# 組合呼叫重現，未啟動 MonoBehaviour。

| R | quadsPerEdge | 自動 D | margin／最細邊長 | 結果 |
|---|---|---|---|---|
| 320 / 640 / 1280 | 32 | 4 / 5 / 6 | 0.6016 / 1.2032 / 2.4064 | 全通過 |
| 640 | 2 | 9 | 19.2514 | 新上限拒絕 |
| 1280 | 2 / 4 | 10 / 9 | 38.5028 / 19.2514 | 新上限拒絕 |
| 8000（Config 合法上限） | 32 | 9 | 19.2514 | 新上限拒絕 |

更深自訂樹被拒屬新增支援邊界；既有 Configure 契約與呼叫端需要同步此邊界。目前 Editor／Development 對契約合法的公開輸入拋錯，Release 則因 Requires 被剝除而繼續建立超出新支援域的樹。

建議：把相容性條件提升到 Configure 的 Pre，並在修改物件狀態前驗證；或在保留 1 m 間距承諾的前提下調整配置／採每節點高度界。補上 R=320/640/1280 與 q=2/4/32 的組合測試。單純降低高度 margin 會破壞地形保守界。

### N2 · low · 新 round-trip 測試未覆蓋需要 √3 slack 的框架

位置：`Tests/EditMode/PlanetReviewR2RegressionTests.cs:48`、`:54`；`Api/PlanetFrame.cs:105`。

重現：只把 LocalToWorld 上限 `2f * MaxVectorComponent` 改為 `1.5f * MaxVectorComponent`，全部 **65/65** 通過。現有新測試只用 up=normalize(1,1,0)，其分量投影最多 √2M。改用 up=normalize(1,1,1)、world=(M,M,M)，實測 local.z=`1.732051e18`；此變異會讓合法 WorldToLocal 輸出無法 round-trip。

目前 2× 實作正確，此項是新增測試的保護缺口。建議：補該對角框架及直接 ±2M local 的合法邊界；維持相對誤差斷言。

## 變異查核

每項分開套用、還原；以下 pass/fail 使用暫存 CLR 快照基線。31 項共 **26 killed / 5 survived**（其中一項為合法輸入域上的等價變異）。

| ID／暫存副本變異 | 結果／殺死者 |
|---|---|
| M01 Warp 去端點特判 | **survived，65/65**；沿用既知存活，不構成移除 FENCE 的證據。 |
| M02 Warp 完整改直接 tan | **survived，65/65**；同上，無新 Burst 證據。 |
| M03 atan2→asin | killed，64/1；近極點 latitude regression。 |
| M04 同時移除三份差異 Sort | killed，64/1；排序 regression step 0。 |
| M05 FromKey 去 X/Y 檢查 | killed，64/1；缺漏 Pre regression。 |
| M06 ForPlanet 起始 L4 | killed，64/1；最小深度 regression。 |
| M07 UMin 子順序交換 | killed，64/1；沿邊順序 regression。 |
| M08 X tie 改嚴格 `>` | killed，64/1；face tie regression。 |
| M09a/b/c 各自移除 added／removed／maskChanged Sort（3 項） | 各 killed，64/1；step 0／1／1。 |
| M10 退化門檻 1e−6→1e−12 | killed，64/1；F1 尺度／退化 regression。 |
| M11 移除目前的 Core normalize | killed，62/3；F2 與兩份快照。兩入口同錯時，B4 equality 本身不會殺死。 |
| M12 兩字典恢復預設 hash | killed，64/1；R2 A3 comparer。 |
| M13 允許負 margin | killed，64/1；R2 A3 負 margin。 |
| M14 移除高度 clamp | killed，64/1；R2 B3 raw 峰值見證。 |
| M15 第八 octave 用 `_o0` | killed，64/1；oct8 快照。 |
| M16 只要求 frequency>0，允許 infinity | killed，64/1；R2 B2 數值域。 |
| N01 移除 finest-edge margin cap | killed，64/1；R2 A1 原反例拒絕測試。 |
| N02 margin cap 的 `≤` 改 `<` | killed，64/1；R2 A1 合法上限測試。 |
| N03 margin cap 常數 16→32 | **survived，65/65**；測試跟著常數重算，沒有鎖住文件的 16。32 仍是常數界，存活本身不表示恢復原指數成長。 |
| N04 local slack 2→1.5 | **survived，65/65**；有實際合法 round-trip 反例，見 N2。 |
| N05 移除 amplitude 上限 | killed，64/1；R2 B2。 |
| N06 frequency 上限忽略 octave 倍數 | killed，64/1；R2 B2。 |
| N07 移除 frequency 表達式內的 octave clamp | **survived，65/65；等價**。octave Pre 先拒絕非法值；合法域內 clamp 是恆等。 |
| N08 Sample 再額外 normalize 一次 | killed，62/3；B4 unit #7 與兩份快照。 |
| N09／N10 各自只改一個字典為預設 hash（2 項） | 各 killed，64/1；R2 A3 comparer。 |
| N11／N12 各自移除 world／local 分量上限（2 項） | 各 killed，64/1；R2 A2 違約測試。 |
| N13 復原舊運算圖：Sample normalize，Core 不 normalize | killed，64/1；B4 loose #0；公開快照不變。 |

## 交接

2026-11-06 · SOL · done · r3 fail：A1 partially fixed；A2/A3/B2/B3/B4 fixed；殘留 A1-R3（med），新增 N1（med）與 N2（low）；31 種變異 26 killed／5 survived；B1 依 Q-T6 待決 · 下一步：OPUS/SONNET 補遲滯工作量界、同步 Configure 支援域及對角 round-trip 測試，再交 SOL 複審；Editor 可用時重跑 v3 的實際 Burst 網格驗證。
