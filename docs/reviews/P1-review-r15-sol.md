> 原始輸出：`codex exec -s read-only -m gpt-6.1-sol`，對象 `0415d51`。未修改任何檔案。Claude Opus 已依 N30／N31 補測試（見 phase-1.md 交接）。

**fail（最高 severity：low）**

# P1 第十五輪對抗式複審 · SOL

2026-11-08 · ChatGPT Sol（SOL）。

**N28、N29 fixed。** r14 的 **127 項變異重跑為 117 killed／10 survived**；AB01 已由新增的兩個接受測試殺死。

新增兩項，均為已證明非等價的契約測試缺口：

- **N30 · low：**可行走坡度的 `>=` 改為 `>`，提交內測試仍全部通過，漏掉 `slope == MaxSlope`。
- **N31 · low：**額外拒絕合法的零滑鼠靈敏度，提交內測試仍全部通過。

未找到原碼 AddLook／Configure／Step 在符合文件 Pre 下的新產品缺陷。法線長度容差邊界的異常另列於「非阻擋觀察」，不影響 verdict。

對象：**`0415d511f91da5b4916b731ad803b27bf1a1c8a9`**，分支 `p1/r5-fixes`。

已讀指定規約、r14 報告、最新交接及 `git diff 5d2cfd7 0415d51`。始終以 `git show 0415d51:<path>` 讀取受審來源；未 checkout、修改檔案或建立暫存檔。結束時 tracked diff、index diff、tracked status 均為空。

以下 `Planet/`、`Player/` 相對於 `Assets/_Project/Features/`；行號對應受審提交。

## 方法與限制

未啟動 Unity Editor。

產品來源、harness、變異及補充探針均保存在記憶體；以 Roslyn 編譯至 `MemoryStream`，載入 assembly 後執行 NUnit 反射 runner。逐案例建立 fixture，執行 SetUp／TearDown。

環境與 r14 相同：

- Windows x64、Windows PowerShell 5.1、.NET Framework CLR。
- Roslyn **3.7.0.0**、C# 8、Release 最佳化。
- 真實 Unity.Mathematics、NUnit **3.5.0.0**。
- Mathematics DLL SHA-256：`48049AFDD1E1FBA73BFFAE24A6DAB5E8B32BC63E387EC8BCEA53CB2A5382D12E`。
- 相依 DLL 使用相容版本解析；不宣稱等同 Unity Mono、Burst 或 IL2CPP。

`PlayerCameraRig.cs` 整份參與編譯。UnityEngine／Cinemachine 使用 managed stand-ins；Transform 支援單位 scale 下的父子姿態，brain 保留 `ControlledObject` 的 output 選擇語意。`PlanetTerrain.Configure` 擷取至建立 quadtree；`SphericalCharacter.Facing` 保留 getter 與契約檢查。未模擬 Unity 物理。

本輪納入 **16 份提交內測試來源、195 案例**，不是 Unity 專案全部測試。

| 執行集合 | 結果 |
|---|---|
| 完整基準，使用提交快照期望值 | **193 pass、2 個已知 Q-T6 mismatch** |
| 記憶體校準快照後的變異集合 | **179/179 pass** |
| `UNITY_EDITOR`：Configure＋r6 fixtures | **48/48 pass** |
| `DEVELOPMENT_BUILD`：Configure＋r6 fixtures | **48/48 pass** |
| 四種實際 output 別名的拒絕及重複 Configure 原子性 | **兩種開發符號各 4/4 pass** |
| N28 的 R14 fixture | **三種符號設定各 3/3 pass** |
| N30 補充 MaxSlope 等號見證 | 原碼 **1/1 pass**；AD01 **0/1 pass** |
| N31 補充零靈敏度接受見證 | 原碼 **2/2 pass**；AD07 **0/2 pass** |

變異集合排除 `CubeSphereTests`、`ChunkQuadtreeTests` 及百萬點 Warp 掃描；完整基準包含這些測試。補充見證不混入提交內測試的 killed 統計。

歷輪 harness 未保存；127 項依報告中的 ID 與變異語意重建，不宣稱替換字串逐位相同。最終全部成功編譯並執行；編譯失敗不計為 kill。

## r14 項目複審

| 項目 | 判定 | 證據 |
|---|---|---|
| N28 微速沿坡速率 | **fixed** | 原 1e-6 m/s 見證通過，輸出沿坡速率恢復至約 1e-6 m/s。 |
| N28 合法近垂直坡 | **fixed** | 原 89.99999° 見證通過，輸出由約 8e-7 恢復至 **4 m/s**。 |
| N28 新斷言有效性 | **fixed** | 舊門檻、單次投影、未縮放速率均被 R14 fixture 殺死。 |
| N29 AddLook 零 gamepad rate | **fixed** | `N29_zero_gamepad_rate_is_accepted` 通過。 |
| N29 Configure 零 gamepad rate | **fixed** | `N29_configure_accepts_a_zero_gamepad_rate` 通過。 |
| N29 斷言有效性 | **fixed** | AB01 僅使上述兩個新接受案例失敗。 |

N28 Post 新增的零值／float 平行情況例外已核對；原兩個見證均不屬於該例外。

### 新斷言的有效性

下表每項從乾淨來源副本獨立編譯，執行 179 案例集合。

| ID／變異 | 結果 | 區分證據 |
|---|---|---|
| AC01 恢復舊 `alongLenSq > 1e-12` 實作 | **killed，3 fail** | 微速、近垂直坡及隨機測試均失敗。 |
| AC02 移除第二次地面投影 | **killed，1 fail** | 隨機案例的平面法向殘差 **0.00036971271 > 1e-4**。 |
| AC03 速率改用未縮放的 `math.length(vTan)` | **killed，1 fail** | 隨機案例速率比例 **1.00870717**。 |
| AB01 gamepad rate 下界 `>= 0` 改為 `> 0` | **killed，2 fail** | 兩個 N29 接受案例均拋出 `ContractException`。 |

AB01 另於 `UNITY_EDITOR`、`DEVELOPMENT_BUILD` 各重跑 Configure＋r6 fixtures，均為 **46 pass／2 fail**。

### grounded 邊界與額外契約掃描

三種符號設定：`UNITY_EDITOR`、`DEVELOPMENT_BUILD`、無開發符號。

固定網格涵蓋：

- 速率：`1e-38`、`1e-30`、`1e-20`、`1e-6`、`1`、`100`、`9000`、`9999 m/s`。
- 坡限：`0.001°`、`1°`、`45°`、`50°`、`89.9°`、`89.99999°`。
- 法線的 up 分量直接取對應 `cos(MaxSlope)`，覆蓋浮點坡限比較端點。

每種設定 **48/48 pass**；最大相對速率誤差 **1.1920929e-7**，最大單位速度法向殘差 **8.940697e-8**。

另外，每種設定執行 100,000 個正規化法線候選輸入，混合微速、高速與坡限附近案例：

| 結果 | 每種設定 |
|---|---:|
| grounded 輸出 | 80,955 |
| 沿坡速率／平面檢查失敗 | **0** |
| 非有限輸出 | **0** |
| 第二步拒絕 | **0** |
| 最大單位速度法向殘差 | **5.364418e-7** |

一般連續 Step 掃描，每種設定：

| 分佈 | 主輸出數 | 不變式違反 | 第二步拒絕 |
|---|---:|---:|---:|
| 一般 transport，混合無接觸／平地／陡坡 | 200,000 | 0 | 0 |
| 空中減速，改量接近原速度 | 200,000 | 0 | 0 |
| 地面減速，改量接近原速度 | 200,000 | 0 | 0 |
| 高速近乎全推牆 | 200,000 | 0 | 0 |
| 極小速度及平方長度下溢區間 | 200,000 | 0 | 0 |
| **合計** | **1,000,000** | **0** | **0** |

三種設定合計 **3,000,000 個主輸出**，另逐一執行第二步並檢查輸出不變式。

其他探針，每種設定：

| 探針 | 結果 |
|---|---|
| 混合合法倍率、零倍率、pointer、stick、dt 的連續 AddLook | **100,000 次；0 有限性／範圍違反** |
| 起始 pitch 每 `0.01°`，搭配 `±160°`、`±180°`、`±1e7°` | **96,006 案例；0 範圍違反** |

## 新發現

### N30 · low · 缺少 MaxSlope 等號的接受案例

位置：`Player/Runtime/Model/SphericalMotor.cs:217、285`；測試缺口涉及 grounded 坡限邊界。

Step Post 明列：

```text
Grounded ⇔ ... ∧ slope ≤ MaxSlope ∧ ...
```

新增變異 **AD01**：

```diff
- math.dot(ground.Normal, up) >= math.cos(math.radians(settings.MaxSlopeDegrees))
+ math.dot(ground.Normal, up) >  math.cos(math.radians(settings.MaxSlopeDegrees))
```

成功編譯，提交內測試仍為 **179/179 pass**。

合法且可直接區分的見證：

```csharp
var settings = MotorSettings.Default;
settings.MaxSlopeDegrees = 45f;
settings.StickSpeed = 0f;

var up = new float3(0f, 1f, 0f);
var forward = new float3(0f, 0f, 1f);
var ground = new GroundContact
{
    Hit = true,
    Normal = math.normalize(new float3(0f, 1f, -1f)),
    Distance = 0f,
};

var next = SphericalMotor.Step(
    settings,
    SphericalMotor.Initial(up, forward),
    new MotorInput { Move = new float2(0f, 1f), ViewForward = forward },
    ground, up, 0.02f);
```

| 欄位 | 原碼 | AD01 |
|---|---:|---:|
| `lengthsq(ground.Normal)` | 0.99999994 | 相同 |
| `dot(ground.Normal, up)` | 0.707106769 | 相同 |
| `cos(MaxSlope)` | 0.707106769 | 相同 |
| `Grounded` | **true** | **false** |

此案例使用正規化法線，符合所有 Pre；其坡度恰為允許上限。AD01 把合法接觸改判為陡坡，違反明列的含等號 Post。

補充 NUnit 見證於三種符號設定均為：原碼 **1/1 pass**、AD01 **0/1 pass**。

**建議：**補一個固定的 `slope == MaxSlope` grounded 接受案例，直接斷言 `Grounded == true`。

### N31 · low · 缺少零滑鼠靈敏度的接受案例

位置：`Player/Runtime/PlayerCameraRig.cs:131、206–207`；Configure 引用相同倍率範圍。

AddLook Pre 明列：

```text
|sensitivity| ≤ MaxSensitivityDegrees
```

因此 **sensitivity == 0 合法**。

新增變異 **AD07**：

```diff
- math.isfinite(_sensitivityDegrees) &&
+ math.isfinite(_sensitivityDegrees) && _sensitivityDegrees != 0f &&
```

成功編譯，提交內測試仍為 **179/179 pass**。

補充見證將 `_sensitivityDegrees` 設為零：

```csharp
rig.AddLook(
    new Vector2(10f, 10f),
    new Vector2(1f, 1f),
    1f);
```

保留預設 gamepad rate `180°/s`：

| 見證 | 原碼 | AD07 |
|---|---|---|
| AddLook | 接受；`PendingLook == (180, 180)` | `ContractException` |
| 合法 Configure | 接受並完成接線 | `ContractException` |

兩個補充接受測試在兩種開發符號下均為：原碼 **2/2 pass**、AD07 **0/2 pass**。

原碼目前正確；這是額外排除合法輸入的**非等價 Pre 變異**。N29 新測試僅鎖住零 gamepad rate，尚未鎖住零 sensitivity。

**建議：**補上 sensitivity 恰為零時，AddLook 與 Configure 均接受的案例。

## 變異結果

**r14 的 127 項：117 killed、10 survived。最終全部成功編譯並執行。**

| 集合 | killed | survived |
|---|---:|---:|
| 舊 47 項 | 47 | 0 |
| U01–U17 | 14 | 3 |
| V01–V10 | 8 | 2 |
| W01–W11 | 6 | 5 |
| X01–X05 | 5 | 0 |
| Y01–Y06 | 6 | 0 |
| Z01–Z22 | 22 | 0 |
| AA01–AA08 | 8 | 0 |
| AB01 | **1** | **0** |
| **合計** | **117** | **10** |

### 原 127 項逐組結果

| 集合 | killed ID | survived ID |
|---|---|---|
| R | R01、R02、R02b、R03–R06 | — |
| P | P01–P09、P11–P16 | — |
| B | B01–B07 | — |
| S | S01–S11 | — |
| T | T01–T07 | — |
| U | U01–U06、U10–U17 | U07、U08、U09 |
| V | V01–V08 | V09、V10 |
| W | W01–W05、W11 | W06–W10 |
| X | X01–X05 | — |
| Y | Y01–Y06 | — |
| Z | Z01–Z22 | — |
| AA | AA01–AA08 | — |
| AB | **AB01** | — |

除 AB01 由 survived 改為 killed 外，分類與 r14 相同。

### 存活項解讀

| 項目 | 分類 |
|---|---|
| U08、U09 | 等價：剩餘範圍比較仍拒絕 NaN／Infinity。 |
| U07、V09、V10、W06、W07 | 測試削弱操作。 |
| W08、W09 | 同時削弱測試後，產品變異重新漏過；不代表原提交測試漏過。 |
| W10 | 尚缺 Unity 可達且可區分的非有限 Transform 見證；不列 finding。 |

### 本輪追加探針

以下不併入原 127 項統計；AD04 是 U16 的重複驗證。

| ID／變異 | 結果 |
|---|---|
| AC01 舊沿坡門檻 | **killed，3 fail** |
| AC02 單次地面投影 | **killed，1 fail** |
| AC03 未縮放速率 | **killed，1 fail** |
| AD01 坡限 `>=→>` | **survived；構成 N30** |
| AD02 grounded 距離 `<=→<` | killed |
| AD03 上升速度端點 `<=0.5→<0.5` | killed |
| AD04 gamepad rate 上端點 `<=→<` | killed |
| AD05 pointer 上端點 `<=→<` | killed |
| AD06 planar clamp 觸發 `>→>=` | survived；等號處為等價操作，見下節 |
| AD07 額外拒絕零 sensitivity | **survived；構成 N31** |
| AD08 look dt 上端點 `<=→<` | killed |
| AD09 Step dt 上端點 `<=0.1→<0.1` | killed |
| AD10 stick 容差端點 `<=→<` | killed |
| AD11 StickSpeed 貢獻減半 | killed |
| AD12 沿坡投影門檻 `>0→>1e-14` | survived；未建立符合 Pre 的非等價見證 |

## 非阻擋觀察

### 法線 `2e-3` guard 邊界

已測試接近 guard 邊界的法線。以下見證通過實作檢查：

```csharp
ground.Normal = new float3(0f, 2e-7f, -1.0009f);
settings.MaxSlopeDegrees = 89.99999f;
settings.StickSpeed = 0f;
```

| 欄位 | 實測 |
|---|---:|
| `lengthsq(Normal)` | 1.00180089 |
| `Grounded` | true |
| `PlanarVelocity` 長度 | 4 |
| `Velocity` | `(0, 0.245979264, 3.99242973)` |
| 單位速度與正規化法線的 dot | **−0.998107433** |

兩次未除以 `dot(n,n)` 的投影，會在近垂直、近乎平行的組合放大法線長度誤差。

每種符號設定另掃描兩組各 100,000 個候選輸入：

| 法線倍率 | grounded 輸出 | 平面偏差超過 `1e-4` 的案例 |
|---|---:|---:|
| `1 − 0.0009994` | 32,659 | 5,926 |
| `1 + 0.0009994` | 65,402 | 12,552 |

**不列 finding：**`GroundContact.Pre` 寫的是「Normal is unit length」，沒有像 `up` 一樣明列容差；上述刻意縮放的法線並非正規化法線。不能只因通過較寬的 Requires，就自行擴張文件 Pre。實際 glue 也會正規化 `hit.normal`。

若專案意圖將整個 `2e-3` guard 接受域視為合法資料，應明定該契約，再修正投影及補測；本輪不據此否決。

### 其他觀察

- **AD06 等號處無可區分變化：**在本後端，`sqrt(MaxStateSpeed²·(1−2e-6))` 與 `MaxStateSpeed·(1−1e-6)` 為同一 float。等號處即使進入 clamp，倍率也為 1。
- **AD12 尚未證明非等價：**未建立符合 unit normal、合法坡限與其他 Pre 的可區分見證，不列 finding。
- **Q-T6 快照差異不影響 verdict：**

  | 快照 | 提交期望值 | CLR 實測 |
  |---|---|---|
  | 預設 octave | `0xB7C5CA249FD8C22D` | `0x383CA51ADC74C561` |
  | 八 octave | `0xAF9DD87B0574B16F` | `0xB94911EBA33AB6D5` |

- Configure 後重新指定 `ControlledObject` 的責任已由 Pre 交給呼叫者。
- 真實 Cinemachine 更新順序、blend、阻尼、非單位父階層 scale、攝影機畫面及 Unity 物理仍需完整引擎驗收。
- 未執行 Unity PlayMode、Burst、IL2CPP、ARM64 或真實輸入裝置驗收。

## 重現資訊

1. 固定從 `0415d51` 讀取產品來源、16 份測試及兩份快照；不 checkout。
2. 使用真實 Mathematics／NUnit，以 Roslyn C# 8、Release 編譯至 `MemoryStream`。
3. 反射展開 `[Test]`／`[TestCase]`，逐案例執行 SetUp／TearDown。
4. 執行 195 案例基準，再於記憶體校準已知快照差異。
5. 每個變異從乾淨來源副本獨立編譯，執行 179 案例集合。
6. 重跑 N28 兩個固定見證、R14 隨機測試、三項沿坡變異及 AB01。
7. 於三種符號設定執行 grounded 速率／坡限／法線邊界、連續 Step、AddLook 及 pitch 掃描。
8. 於兩種開發符號重跑 Configure＋r6 fixtures，以及四種實際 output 別名原子性探針。
9. 使用補充合法見證確認 AD01、AD07 非等價；不混入提交內 killed 計數。

## 建議下一步

1. OPUS 補 N30：固定 `slope == MaxSlope` 的 grounded 接受案例。
2. OPUS 補 N31：零 sensitivity 的 AddLook／Configure 接受案例。
3. SOL 複審 AD01、AD07 與原 127 項變異。
4. 法線容差語意及完整 Unity pipeline 驗收獨立處理。

- 2026-11-08 · ChatGPT Sol（SOL，唯讀，對象 0415d51）· done · P1 r15 fail（最高 low）：N28／N29 fixed；127 變異 117 killed／10 survived，新增 N30 坡限等號及 N31 零靈敏度接受測試缺口 · 下一步：OPUS 補 N30／N31 測試；SOL 複審