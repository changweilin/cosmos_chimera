# P1 第二輪對抗式審查 · SOL

2026-11-06 · ChatGPT Sol（SOL）。A：**fail**；B：**fail**。

對象：`01d2402`（HEAD `41d9a45` 的 Planet 內容相同）。下列路徑相對於 `Assets/_Project/Features/Planet/`。已讀指定規範、planet-tech、P1 roadmap 與前次 review；本次明確的單檔輸出限制優先，不修改 roadmap。

## 方法與限制

未啟動 Unity。只在暫存副本執行變異，逐項還原；專案只新增本報告，保留工作期間出現的其他變更。探針：Windows x64、SDK 9.0.304、.NET 9 Release、`UNITY_EDITOR`，引用真實 Mathematics 1.3.3 DLL 與 NUnit；不替換 float3/math。DLL SHA-256：`48049AFDD1E1FBA73BFFAE24A6DAB5E8B32BC63E387EC8BCEA53CB2A5382D12E`。

執行 PlanetFrame／LatLon／Config、CubeSphere／ChunkId、Quadtree、Height、Regression fixtures：**53 pass / 1 fail**；唯一失敗是高度快照。Regression 單獨 **16/16**。暫存測試只將 TestContext 輸出改 Console、Application.dataPath 換成專案 Assets 絕對路徑。排除快照時基線 **53/53**；全部變異還原後再次得到相同結果。這不是 Unity Test Runner／Burst／IL2CPP 的結果。

可重跑暫存 harness：`%TEMP%/cosmimera-r2-sol-cea474c0808e41a5af432f819a2b7db1`，`dotnet run --project <目錄>/Probe.csproj -c Release -- tests`；`height`、`edge`、`lod` 執行本文數值探針。

## A · F1–F7 複審

| Finding | 判定 | 證據 |
|---|---|---|
| F1 | fixed | `Api/PlanetFrame.cs:126` 先安全正規化、用角度退化門檻，再正交化；Regression `:18`、`:33` 通過原平行／近平行／多尺度案例。門檻改回 `1e-12` 被殺死。 |
| F2 | fixed | `Api/PlanetFrame.cs:77`、`Api/PlanetConfig.cs:95`、`Api/PlanetHeight.cs:116` 正規化近單位輸入；Regression `:60` 通過。移除 Sample 正規化被殺死。 |
| F3 | partially fixed | 原四個反例已被 Rescale、Radius／Center 範圍修正及 Regression `:82`、`:93` 覆蓋；但新增的 finite 向量契約仍容許運算溢位，見 A2。 |
| F4 | partially fixed | `Runtime/Model/ChunkQuadtree.cs:30`、`:31` 的兩個字典確實使用 MixHash comparer；但 R/2 與 factor 上限沒有工作量保證，見 A1。 |
| F5 | fixed | `Runtime/Model/ChunkQuadtree.cs:13` 與 planet-tech 已明確限定 LOD 是本機表現層；`ChunkLodSettings.cs:54` 強制遲滯下限，Regression `:149` 的相鄰 float 路徑通過。此修正不證明 Height 跨平台一致。 |
| F6 | partially fixed | RunsAlongU Requires、原缺漏 DbC、test 豁免與 list alias 測試已補；仍未逐條測 Pre，存在可存活的實際契約變異，見 A3。 |
| F7 | fixed | `Api/CubeSphere.cs:153` 改為非遞減；`Api/LatLon.cs:54` 將 −π 折成 +π。Regression `:204`、`:211` 通過端點／signed-zero／網格案例。 |

### A1 · med · 合法 HeightMargin 仍造成指數成長

位置：`Runtime/Model/ChunkQuadtree.cs:51`、`:245`。R=640、Center=0、camera=(641.7,0,0)、settings=(D,32,2,.25,313.6)，完全符合新 Pre。D=5/7/8/9 首次 Update 分別 **1,248 / 8,172 / 26,436 / 94,740 葉**（本機約 8/12/32/91 ms，非 Unity profiler 數字）。

固定 margin 使相機周圍一個正面積球冠的距離下界持續為零，故其內節點一路分裂，葉數仍為 Θ(4^D)；不必整顆球都分裂才會耗盡記憶體。D20 仍合法。建議：限制受支援的深度／margin／解析度組合或加入硬工作量預算，維持平衡後原子發布；新增合法極限設定的葉數測試。hash 修正本身成立。

### A2 · med · finite 向量仍不滿足 round-trip Post

位置：`Api/PlanetFrame.cs:98`、`:103`。`f=FromUp(normalize((1,1,0)))`，合法 `world=(float.MaxValue,float.MaxValue,0)`；`WorldToLocal(world)` 得 `(0,0,+∞)`，再 LocalToWorld 直接拋 ContractException，違反明文 round-trip Post。建議：為轉換向量定義可實現輸出與誤差的數值域並驗證，新增有限輸入造成輸出溢位的案例；只檢查 isfinite 不足。

### A3 · low · 回歸測試未鎖住全部契約與字典 comparer

位置：`Tests/EditMode/PlanetReviewRegressionTests.cs:112`、`:171`；`Runtime/Model/ChunkLodSettings.cs:55`。將 heightMargin Requires 改為只檢查 finite（允許負數），**53 個非快照測試全過**；負 margin 會縮小地形距離界。兩個 quadtree 字典移除 comparer 時 Regression 也 **16/16**，因 hash 測試只測 ChunkId.GetHashCode。建議：加入負 margin 違約測試，並直接檢查兩個字典所用 comparer 的 key 分布；不以易波動的耗時斷言替代。

### 變異查核

| 暫存副本變異 | 結果 |
|---|---|
| 前次：Warp 去端點特判；完全改直接 tan | 各自 CubeSphereTests **7/7**，仍存活；不表示 FENCE 可刪，也沒有新 Burst 證據。 |
| 前次：atan2→asin；移除三份 Sort；FromKey 去 X/Y 檢查；ForPlanet 起始 L4；UMin 子順序交換；X tie 改 `>` | 六項逐一被 Regression 殺死，各為 **15 pass / 1 fail**。 |
| 新增：只移除 added／removed／maskChanged 的各自 Sort | 三項逐一被排序測試殺死（step 0／1／1）。 |
| 新增：退化門檻 `1e-6→1e-12`；移除 Sample 正規化 | 各自被 F1／F2 測試殺死。 |
| 新增：字典恢復預設 hash；允許負 margin | 存活，詳 A3。 |
| 新增：移除高度 clamp；第八 octave 改用 `_o0`；允許 infinite frequency | 各自通過全部 **6 個非快照 Height 測試**；前兩項的本機快照 hash 也完全不變，詳 B3。 |

## B · P1-04 PlanetHeight 首審

### B1 · high · 跨客戶端可重現保證缺少可執行的浮點政策

位置：`Api/PlanetHeight.cs:11`、`:116`、`:127`；`Tests/EditMode/PlanetHeightTests.cs:119`；`Tests/Snapshots/planet-height-v2.txt:1`。PCG seed/stream 固定，沒有 UnityEngine.Random；但 normalize/sqrt、simplex 的 float skew／floor、近似 gradient normalization 與累加，並沒有由 GeneratorVersion 鎖定後端運算規則。套件 `Unity.Mathematics/Noise/noise3D.cs:30`、`:87`、`:95` 可見這些浮點路徑。

相同 seed=42、R640、v2，提交快照為 **0xB7C5CA249FD8C22D**；同 DLL 的本次 CLR 探針為 **0x383CA51ADC74C561**。方向取樣器本身也使用 sqrt，所以此差異不能單獨定位為 snoise；更不能宣稱已重現兩個 Unity 客戶端不一致。它證明目前摘要不是跨執行環境的證書。同 seed 在不同後端取高度，再以高度門檻選生成內容時，未受約束的末位差異即可翻轉內容決定。

建議：用固定原始位元方向作測試輸入，對實際支援的 Mono／IL2CPP／Burst、x64／ARM64 比較共同摘要；若要求逐位元內容一致，採明定的決定性數值演算法或權威內容資料。若只允許幾何誤差，應明訂誤差與內容決策界線並同步修契約；ADR-0006 的網格誤差容許不能直接當 Height 保證的證明。

### B2 · med · Constructor 的數值域不能保證可用的高度與界限

位置：`Api/PlanetHeight.cs:59`、`:72`、`:127`、`:129`。`new PlanetHeight(42,float.MaxValue,2,8)` 合法且 IsValid=true，但 MaxAbsHeight=+∞；固定 PCG(8,9) 的 10,000 個單位方向中 **133 個 Sample 非有限**。該界限不能供要求 finite 的 HeightMargin 使用。`baseFrequency=float.MaxValue` 也合法，下一 octave 的 frequency 即溢位，noise 收到 infinity／NaN；最終 clamp 可能把錯誤掩成峰值（本機 normalize((1,2,3)) 回傳 +MaxAbsHeight）。

建議：定義振幅、最高 octave frequency 與 noise-domain 的安全上限，確保界限及所有中間運算有限；違反時以 ContractException 拒絕，測 amplitude/frequency 的兩端與非有限值。現有 public XML 的三項 DbC 標記齊全，但數值前置條件不完整。

### B3 · med · 幾何級數不是 raw simplex 上界；測試未保護真正的 clamp 保證

位置：`Api/PlanetHeight.cs:50`、`:132`、`:134`；`Tests/EditMode/PlanetHeightTests.cs:66`、`:85`、`:123`。本機 raw snoise 在 **(-81.06958,205.9524,1.4006042)** 得約 **1.0280088**；這是反例而非全域峰值。單純 Σamp 不能界住 raw fBm。

目前最終 clamp 在**有限的 MaxAbsHeight** 下確實守住公開界限，不能將峰值超過 1 誤報成目前 Sample 越界。可重現必要性：取 seed42 的第一個 offset o，target 為上述點，freq=length(target−o)≈509.09702、dir=normalize(target−o)，amplitude=10、octaves=1；raw≈**10.28009**，Sample=**10**。

但所有 Sample 測試／快照都用預設六 octave；刪除 clamp 後六個非快照測試仍全過，快照 hash 不變。第八 octave offset 破壞也同樣存活，因只測 bound 的 1/2/6 octaves，未實際取樣 1/8 octaves。建議：保留 clamp，將欄位說明寫清楚是「截斷後輸出的界限」，加入上述單 octave 峰值、0 振幅、8 octaves、多 seed／參數的固定方向摘要。v1→v2 的版本與交接更新符合規範，FNV-1a 本身不是主要弱點。

### B4 · low · 公開查詢與網格入口取樣不同位置

位置：`Api/PlanetHeight.cs:116`、`:120`；`Runtime/Model/ChunkMeshJob.cs:97`。相同 ForPlanet(R640,42) 與快照的 4096 個合法方向，同一 CLR 上 **1202** 個 Sample(d) 與 SampleCore(d) 位元不同，最大差 **0.0010213852 m**；前者多做一次 normalize。這是同後端入口差異，不能全歸因 Burst。

情境：查詢高度與碰撞網格使用同一方向，卻不是相同的取樣運算圖；目前快照完全不覆蓋 Core 路徑。建議：共用明確的方向正規化政策，或明訂查詢／網格容許誤差並同時測兩路；若改 Core 輸出須再 bump GeneratorVersion。

## 交接

2026-11-06 · SOL · done · A/B 均 fail；F1/F2/F5/F7 fixed，F3/F4/F6 partially fixed · 下一步：OPUS/SONNET 修 A1–A3、B1–B4，再由 SOL 複審；實際 Unity 跨後端檢查留待 Editor 可用時執行。
