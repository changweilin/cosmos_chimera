> 原始輸出：`codex exec -s read-only -m gpt-6.1-sol`，對象 `1de9777`。未修改任何檔案。Claude Opus 已依 N32 修正、N33／N34 補測試（見 phase-1.md 交接）。

**fail（最高 severity：low）**

# P1 第十六輪對抗式複審 · SOL

2026-11-08 · ChatGPT Sol（SOL）。

**N30、N31 fixed。** 原 127 項變異重跑為 **117 killed／10 survived**；AD01–AD12 為 **10 killed／2 survived**。

本輪新增：

- **N32 · low：**Initial／Step 拒絕文件 Pre 允許的窄邊界輸入。
- **N33 · low：**AddLook／Configure 尚有已證明非等價的端點測試缺口。
- **N34 · low：**Step／Initial 尚有端點及零速度 Post 測試缺口。

完整端點掃描包含 **150 個端點／呼叫位置配置、300 次變異執行：101 killed／199 survived**。存活數包含等價操作、重複語義及尚未建立合法見證的項目，**不直接作為 finding 數量**。

對象：**`1de9777de6f52d6c000a64e38c43c59c8f1d9514`**，分支 `p1/r5-fixes`。

已讀指定規約、r15 報告、最新交接及 `git diff 0415d51 1de9777`。受審來源均由 `git show 1de9777:<path>` 取得；未 checkout、修改檔案或建立暫存檔。結束時 working tree、tracked diff、index diff 均為空。

以下 `Player/`、`Planet/` 相對於 `Assets/_Project/Features/`；行號對應受審提交。

## 方法與限制

未啟動 Unity Editor。

產品來源、測試、harness、變異及補充見證均保存在記憶體，以 Roslyn 編譯至 `MemoryStream`，載入 assembly 後執行 NUnit 反射 runner。逐案例建立 fixture，執行 SetUp／TearDown。

環境：

- Windows x64、Windows PowerShell、.NET Framework CLR。
- Roslyn **3.7.0.0**、C# 8、Release 最佳化。
- 真實 Unity.Mathematics、NUnit **3.5.0.0**。
- Mathematics DLL SHA-256：`48049AFDD1E1FBA73BFFAE24A6DAB5E8B32BC63E387EC8BCEA53CB2A5382D12E`。
- 相依 DLL 使用相容版本解析；不宣稱等同 Unity Mono、Burst 或 IL2CPP。

`PlayerCameraRig.cs` 整份參與編譯。UnityEngine／Cinemachine 使用 managed stand-ins；Transform 支援單位 scale 的父子位置／旋轉，brain 保留 `ControlledObject` 語意。`PlanetTerrain.Configure` 擷取至建立 quadtree；`SphericalCharacter.Facing` 保留 getter 與契約檢查。未模擬 Unity 物理。

| 執行集合 | 結果 |
|---|---|
| 16 份提交內測試，完整基準 | **196 pass／2 個已知 Q-T6 mismatch，共 198 案例** |
| 記憶體校準快照後的變異集合 | **182/182 pass** |
| `UNITY_EDITOR`：Configure＋r6 fixtures | **50/50 pass** |
| `DEVELOPMENT_BUILD`：Configure＋r6 fixtures | **50/50 pass** |
| N30 固定案例，兩種開發符號 | **各 1/1 pass** |
| 原 127 項變異 | **117 killed／10 survived** |
| AD01–AD12 | **10 killed／2 survived** |
| 端點掃描，300 次變異 | **101 killed／199 survived；最終全部成功編譯並執行** |

變異集合排除 `CubeSphereTests`、`ChunkQuadtreeTests` 及百萬點 Warp 掃描；完整基準包含這些案例。補充見證不混入提交內測試的 killed 統計。

歷輪 harness 未保存；127 項依報告 ID 與語意重建，不宣稱替換字串逐位相同。編譯錯誤修正後重跑，未將編譯失敗計為 kill。

### 額外契約掃描

三種符號設定：`UNITY_EDITOR`、`DEVELOPMENT_BUILD`、無開發符號。下表為**每種設定**：

| 探針 | 結果 |
|---|---|
| 混合 transport、接觸、減速、近乎全推牆及微速的 Step | **1,000,000 個主輸出；0 不變式違反** |
| 將每個輸出再交給 Step | **1,000,000 次；0 拒絕／不變式違反** |
| 合法倍率、pointer、stick、dt 的連續 AddLook | **100,000 次；0 有限性／範圍違反** |
| 起始 pitch 每 `0.01°`，搭配 `±160°`、`±180°`、`±1e7°` | **96,006 案例；0 範圍違反** |

三種設定合計 **300 萬個 Step 主輸出，另執行 300 萬次第二步**。這些掃描沒有涵蓋下述所有精確端點，不能抵銷固定見證。

## r15 項目複審

| 項目 | 判定 | 證據 |
|---|---|---|
| `N30_contact_exactly_at_max_slope_is_grounded` | **fixed** | 原碼通過；AD01 使此案例失敗。 |
| `N31_zero_pointer_sensitivity_is_accepted` | **fixed** | 接受零 sensitivity，pending look 為測試指定的 gamepad 貢獻。 |
| `N31_configure_accepts_a_zero_pointer_sensitivity` | **fixed** | 接受零 sensitivity 並完成接線。 |
| AD01 有效性 | **killed，1 fail** | 僅 N30 新案例失敗。 |
| AD07 有效性 | **killed，2 fail** | 兩個 N31 新案例均拋出 `ContractException`。 |

## 新發現

### N32 · low · Initial／Step 的數值檢查比文件 Pre 更窄

位置：`Player/Runtime/Model/SphericalMotor.cs:98、195–205、213–215、232、251、397`。

#### 長度 slack 的合法輸入被拒絕

文件允許：

```text
Initial／Step：up unit (±1e-3)
MotorInput：|Move| ≤ 1 (+1e-3)
```

取完全可表示的 float：

```csharp
float a = math.asfloat(math.asuint(1.001f) - 1u);
// 精確值：1.000999927520752
```

其長度誤差為 `0.000999927520752 < 1e-3`，符合文件 Pre；但 float 平方為：

```text
a² = 1.0020008087158203
```

| 合法見證；其餘參數均使用正常值 | 原碼結果 |
|---|---|
| `Initial(new float3(0, a, 0), forward)` | **ContractException：up must be unit length** |
| Step 的新 `up = (0,a,0)` | **相同例外** |
| Step 的 `Move = (a,0)` | **ContractException：\|Move\| must be ≤ 1** |

原因：

```csharp
math.abs(math.lengthsq(up) - 1f) < 2e-3f
math.lengthsq(input.Move) <= 1.002f
```

長度上界 `1 + ε` 平方後是 `1 + 2ε + ε²`；目前門檻省略了 `ε² = 1e-6`，縮窄了文件允許的上側區間。

以上三個見證在 `UNITY_EDITOR`、`DEVELOPMENT_BUILD` 均重現。

#### Initial 的角度 cutoff 也有可區分的量化端點

合法輸入：

```csharp
var up = new float3(0f, 1f, 0f);
var facing = new float3(2.4e-7f, 1f, 0.0010000004f);
```

以這些 float 的實際值計算，與 `+up` 的角度為：

```text
0.0010000000922098439 rad > 0.001 rad
```

因此不在文件排除的 `1e-3 rad` 內。實作正規化後卻得到：

```text
lengthsq(t) == 1e-6f
```

`<= DegenerateSq` 將它判成退化，Initial 拋出例外。僅在記憶體將比較改成 `<` 後，同一輸入被接受，Facing 有限、單位且切向；**提交內 182 個案例仍全部通過**。

此角度差異極小，因此維持 low；它仍是文件 Pre 下的具體拒絕反例。

**建議：**使長度與角度檢查符合所寫的合法域；若契約意圖採用另一個浮點判定域，應明確變更 Pre 並同步補接受／拒絕案例。

### N33 · low · AddLook／Configure 的端點保護仍不完整

位置：`Player/Runtime/PlayerCameraRig.cs:50–118、129–158、204–210`。

以下均有補充見證，確認原碼接受、端點額外拒絕變異拋出例外，而提交內測試仍 **182/182 pass**：

| 呼叫端 | 漏測的合法端點 |
|---|---|
| AddLook | `stick.x == -1.001`、`stick.y == -1.001` |
| AddLook | `sensitivity == +10` |
| Configure | `sensitivity == -10`、`+10`；gamepad rate `== 3600` |
| Configure target.position | X 的 `±32768`、Y 的 `-32768`、Z 的 `±32768` |
| Configure planet | Radius `== 1`、`8000`；Center 的 X `-16000`、Y／Z 各 `±16000` |

另有三組具體缺口：

**一、yaw 恰為一整圈。**

預設 sensitivity 下，pointer X `±3000` 產生 raw yaw `±360°`：

| 版本 | PendingLook.x |
|---|---:|
| 原碼 | `0` |
| 僅在 raw yaw 恰為端點時保留該值 | **`±360`，違反 `(-360,360)` Post** |
| 僅在 raw yaw 恰為端點時額外拒絕 | `ContractException` |

兩種變異均存活；不是把角度視覺等價誤當產品缺陷，而是明列的 pending 範圍 Post 沒有被鎖住。

**二、pitch 恰為 `±160°`。**

合法見證使用 sensitivity `0.1`、pointer Y `±1600`。原碼 pending pitch 恰為 `±160`。

將端點往零移動一個 float ULP，提交測試仍全部通過：

```text
原碼：+160          bits 43200000
變異：+159.999985   bits 431FFFFF
```

端點額外拒絕也存活。這漏掉了累加 Post 及「任何 `|pitch| ≥ 160` 從所有起始 pitch 都會到達 `±80`」的等號情況。

**三、default planet 的拒絕原子性。**

將 Configure 的 `planet.IsValid` 檢查弱化到接受 Radius 0，提交測試仍全部通過。補充見證顯示：

| `default(PlanetConfig)` | 原碼 | 變異 |
|---|---|---|
| 最終是否拋出例外 | 是 | 是 |
| `_target` 是否已寫入 | **否** | **是** |
| TrackingTarget 是否已接線 | **否** | **是，Pivot** |

變異直到後續 `PlanetFrame.At` 才失敗，違反 Configure 明列的「全部在寫入前檢查」。這是測試缺口；原碼目前正確。

### N34 · low · Step／Initial 的端點與零速度 Post 尚有缺口

位置：`Player/Runtime/Model/SphericalMotor.cs:83–93、195–205、231–255、310–366、397`。

以下存活項已用可區分見證確認：

| 類別 | 漏測端點 |
|---|---|
| Initial／Step 合法 up 下側 slack | `length(up) == 0.999f` |
| Step 的嚴格角度 Pre | 新舊 up 恰為 `90°`；`dot(up,prev.Up) > 0` 改成 `>= 0` 仍存活 |
| Move 容差 | `lengthsq(Move) == 1.002f`；見證 `(1, sqrt(0.002f))` 合法且原碼接受 |
| prev.PlanarVelocity 分量上限 | X 的 `-10000`、Y／Z 各 `±10000` |
| prev.Velocity 分量／總長上限 | 三軸各 `±20000`，以及總長 `20000` |
| prev.VerticalSpeed | `+10000` |
| Step 輸出 VerticalSpeed | 合法極小正 dt 下仍可恰為 `+10000`；端點額外拒絕存活 |
| MotorSettings 嚴格正值下界 | WalkSpeed、GroundAcceleration、AirAcceleration、Gravity、JumpHeight、TurnRateDegrees、MaxSlopeDegrees 的零值接受變異 |
| MotorSettings 合法零值 | `SnapDistance == 0` 的額外拒絕 |
| ViewForward 退化 cutoff | 有限向量使 helper 的 `lengthsq(t) == 1e-6f`；原碼接受，額外拒絕變異存活 |

速度見證由 Initial 建立狀態後設定公開欄位，仍符合明列的 MotorState 不變式；沒有假設必須先經 Unity 物理產生該狀態。

**Initial 的零 Velocity Post 也沒有測試保護。**

僅加入一個欄位：

```diff
- return new MotorState { Up = up, Facing = tangent, Grounded = false };
+ return new MotorState { Up = up, Facing = tangent, Grounded = false, Velocity = tangent };
```

結果：

| 版本 | Initial.Velocity |
|---|---|
| 原碼 | `(0,0,0)` |
| 變異，facing 為 +Z | **`(0,0,1)`，速率 1 m/s** |

此變異成功編譯，**182/182 pass**，明確違反 Initial 的 `Velocity == 0` Post。這不是 subnormal 或完整引擎差異。

## 變異結果

### 原 127 項

**117 killed／10 survived；全部成功編譯並執行。**

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
| AB | AB01 | — |

存活分類與 r15 相同：

- U08、U09：剩餘範圍比較仍拒絕非有限倍率，屬等價操作。
- U07、V09、V10、W06、W07：測試削弱。
- W08、W09：同時削弱測試及產品；不代表原提交測試漏過。
- W10：未建立 Unity 可達且可區分的非有限 Transform 見證，不列 finding。

### AD01–AD12

| ID／變異 | 結果 |
|---|---|
| AD01 坡限 `>=→>` | **killed，1 fail；N30** |
| AD02 grounded 距離 `<=→<` | killed，1 fail |
| AD03 上升速度 `<=0.5→<0.5` | killed，1 fail |
| AD04 gamepad rate 上端點 `<=→<` | killed，1 fail |
| AD05 pointer 上端點 `<=→<` | killed，3 fail |
| AD06 planar clamp 觸發 `>→>=` | survived；等號處無可區分變化 |
| AD07 額外拒絕零 sensitivity | **killed，2 fail；N31** |
| AD08 look dt 上端點 `<=→<` | killed，5 fail |
| AD09 Step dt 上端點 `<=0.1→<0.1` | killed，3 fail |
| AD10 stick 容差端點 `<=→<` | killed，1 fail |
| AD11 StickSpeed 貢獻減半 | killed，3 fail |
| AD12 沿坡投影門檻 `>0→>1e-14` | survived；未建立合法非等價見證 |

### 完整端點掃描

**150 個配置 × 兩種變異＝300 次：101 killed／199 survived。**

定義：

- **flip**：翻轉端點的開／閉；對寫入型 Post 使用可觀察輸出變異，例如保留 yaw `360`、使 Initial Velocity 非零。
- **reject**：在該端點額外拒絕；開端點原本已被拒絕時，此操作可能等價。
- **K**＝killed；**S**＝survived。
- **†**＝已證明非等價，構成 N33／N34。
- **E**＝等價；**U**＝未建立符合文件 Pre 的可區分見證，不列 finding。
- 同列列出的每個分量、正負端點及呼叫位置，都具有所列結果。共用倍率檢查分別隔離 AddLook／Configure；absolute 與 signed 具體化亦分開執行，因此配置數包含重複語義。

#### AddLook：23 個配置

| 端點 | flip / reject |
|---|---|
| pointer X：`-1e6`、`+1e6`；Y：`-1e6`、`+1e6` | K / K |
| stick X：`-1.001`；Y：`-1.001` | **S† / S†** |
| stick X：`+1.001`；Y：`+1.001` | K / K |
| dt：`0`、`1` | K / K |
| sensitivity：`-10` | K / K |
| sensitivity：`0` | K / K |
| sensitivity：`+10` | **S† / S†** |
| gamepad rate：`0`、`3600` | K / K |
| yaw 完整一圈：`-360`、`+360` | **S† / S†** |
| pending pitch：`-180`、`+180` | K / K |
| 全起始 pitch 保證的分界：`-160`、`+160` | **S† / S†** |
| 對應 orbit pitch 限制：`-80`、`+80` | K / K |

#### Configure：22 個配置

| 端點 | flip / reject |
|---|---|
| sensitivity：`-10`、`+10` | **S† / S†** |
| sensitivity：`0` | K / K |
| gamepad rate：`0` | K / K |
| gamepad rate：`3600` | **S† / S†** |
| target.position X：`±32768`；Y：`-32768`；Z：`±32768` | **S† / S†** |
| target.position Y：`+32768` | K / K |
| `cmax(abs(position−Center)) > 1e-3` 的等號 | K / S(E) |
| planet Radius：`1`、`8000` | **S† / S†** |
| planet Center X：`-16000`；Y／Z：各 `±16000` | **S† / S†** |
| planet Center X：`+16000` | K / K |
| planet validity 的 Radius `0` | **S† / S(E)** |
| Configure 丟棄 queue：`pending == 0` | K / K |

#### Step Pre／input：42 個配置

| 端點 | flip / reject |
|---|---|
| dt 的嚴格下界 `0` | K / S(E) |
| dt 上界 `0.1` | K / K |
| up 夾角 `90°`，dot `==0` | **S† / S(E)** |
| Move 平方長度 `1.002` | **S† / S†** |
| up／prev.Up／prev.Facing 的平方單位長度 guard：error `2e-3` | S(E) / S(E) |
| up 長度下側 slack：`0.999f` | **S† / S†** |
| up 長度上側 slack：`1.001f` | S / S；另見 N32 合法內側輸入已被原碼拒絕 |
| prev.Up 長度：`0.999f`、`1.001f` | S(U) / S(U) |
| prev.Facing 長度：`0.999f`、`1.001f` | S(U) / S(U) |
| prev.Facing 切向 guard：absolute `1e-3`，另各測 signed `±1e-3` | S(U) / S(U) |
| prev.PlanarVelocity 切向 guard：absolute `1e-3·max(1,\|v\|)`，另各測 signed 正負界 | S(U) / S(U) |
| prev.PlanarVelocity X：`+10000` | K / K |
| prev.PlanarVelocity X：`-10000`；Y／Z：各 `±10000` | **S† / S†** |
| prev.Velocity X／Y／Z：各 `±20000` | **S† / S†** |
| prev.PlanarVelocity 平方長度：`1e8` | K / K |
| prev.Velocity 平方長度：`4e8` | **S† / S†** |
| prev.VerticalSpeed：`-10000` | K / K |
| prev.VerticalSpeed：`+10000` | **S† / S†** |
| Hit 時 Distance：`0` | K / K |
| Hit 時 normal 平方長度 guard：error `2e-3` | S(E) / S(E) |
| prev.Grounded 時 VerticalSpeed：`0` | K / K |
| ViewForward：零向量允許 | K / K |
| ViewForward cutoff：`lengthsq(t)==1e-6`；整體及接近 `+up`／`-up` 分別具體化 | S / **S†** |

#### Step 的 settings.IsValid：21 個配置

表內零下界的 reject 本來就不會新增拒絕；†僅標示已證明非等價的變異。

| 欄位 | 下界 flip / reject | 上界 flip / reject |
|---|---|---|
| WalkSpeed：`0 < value ≤ 100` | **S† / S(E)** | K / K |
| SprintSpeed：`0 < value ≤ 100` | S(E) / S(E) | K / K |
| GroundAcceleration：`0 < value ≤ 1000` | **S† / S(E)** | K / K |
| AirAcceleration：`0 < value ≤ 1000` | **S† / S(E)** | K / K |
| Gravity：`0 < value ≤ 1000` | **S† / S(E)** | K / K |
| JumpHeight：`0 < value ≤ 100` | **S† / S(E)** | K / K |
| TurnRateDegrees：`0 < value ≤ 36000` | **S† / S(E)** | K / K |
| SnapDistance：`0 ≤ value ≤ 10` | **S† / S†** | K / K |
| StickSpeed：`0 ≤ value ≤ 100` | K / K | K / K |
| MaxSlopeDegrees：`0 < value < 90` | **S† / S(E)** | K / S(E) |
| WalkSpeed `≤` SprintSpeed 的等號 | K / K | — |

SprintSpeed 零下界的單獨翻轉等價：`WalkSpeed > 0` 與 `WalkSpeed ≤ SprintSpeed` 仍使 SprintSpeed 0 無法通過。

#### Initial：20 個配置

| 端點 | flip / reject |
|---|---|
| up 平方單位長度 guard：error `2e-3` | S(E) / S(E) |
| up 長度下側 slack：`0.999f` | **S† / S†** |
| up 長度上側 slack：`1.001f` | S / S；見 N32 |
| facing 零向量禁用 | K / S(E) |
| facing cutoff `1e-6`；整體及接近 `+up`／`-up` 分別具體化 | S / S；N32 已建立原碼拒絕合法角度的見證 |
| Post Velocity `==0` | **S† / K** |
| Post Facing 長度：`0.9999f`、`1.0001f` | S(U) / S(U) |
| Post Facing 切向誤差：absolute `1e-4`，另各測 signed `±1e-4` | S(U) / S(U) |
| Post PlanarVelocity 相對切向誤差：absolute 上界，另各測 signed 正負界 | S(E) / S(E) |
| Post PlanarVelocity 平方長度上界 `1e8` | S(E) / S(E) |
| Post Velocity 平方長度上界 `4e8` | S(E) / S(E) |
| Post VerticalSpeed：`-10000`、`+10000` | S(E) / S(E) |

Initial 的 planar／vertical 始終為零；其非零極限端點不可由原碼 Initial 到達，不能據此報測試缺口。

#### Step Post：22 個配置

| 端點 | flip / reject |
|---|---|
| grounded：Distance `== SnapDistance` | K / K |
| grounded：slope `== MaxSlope` | K / K |
| grounded：prev.VerticalSpeed `==0.5` | K / K |
| clamp 觸發：平方長度 `MaxStateSpeed²·(1−2e-6)` | S(E) / K |
| clamp 目標：`MaxStateSpeed·(1−1e-6)` | K / K |
| 沿坡分量的 planar scale `==0` 例外 | S(E) / K |
| 沿坡投影平方長度 `==0`／平行例外 | S(U) / S(U) |
| controlled planar 動態速率上界 | K / K |
| jump vertical speed `== sqrt(2·Gravity·JumpHeight)` | K / K |
| Grounded 時 VerticalSpeed `==0` | K / K |
| Facing 長度：`0.9999f`、`1.0001f` | S(U) / S(U) |
| Facing 切向誤差：absolute `1e-4`，另各測 signed `±1e-4` | S(U) / S(U) |
| planar 相對切向誤差：absolute 上界，另各測 signed 正負界 | S(U) / S(U) |
| planar 平方長度上界 `1e8` | S(E) / S(E) |
| Velocity 平方長度上界 `4e8` | S(E) / S(E) |
| VerticalSpeed：`-10000` | K / K |
| VerticalSpeed：`+10000` | **S† / S†** |

## 非阻擋觀察

### guard slack 不自行擴張文件 Pre

`prev` 必須符合 MotorState 不變式；檢查使用較寬的 slack，不代表所有恰在 guard 上界的人工狀態都屬合法域。因此，未建立合法見證的 prev／輸出切向端點存活項沒有列為 finding。

同理，GroundContact 文件仍要求 **unit normal**。通過 `2e-3` guard 的刻意縮放法線，不因此成為符合 Pre 的輸入；r15 的非單位法線觀察不影響本輪 verdict。

平方單位長度 guard 的 `2e-3f` 等號，在接近 1 的 float 平方長度格點上不能精確形成；僅翻轉該等號或額外排除它沒有可區分效果。這與 N32 的**長度契約合法內側輸入遭拒**是不同問題。

### 其他存活項

- AD06：本後端的等號處進入 clamp 不產生可區分變化。
- AD12：仍未建立符合 unit normal、合法坡限及其他 Pre 的非等價見證。
- 輸出比所承諾誤差界更緊時，收緊端點觀測 guard 可能沒有作用；未證明非等價者不列 finding。

### 已知後端與引擎限制

Q-T6 差異維持已知結果，不影響 verdict：

| 快照 | 提交期望值 | CLR 實測 |
|---|---|---|
| 預設 octave | `0xB7C5CA249FD8C22D` | `0x383CA51ADC74C561` |
| 八 octave | `0xAF9DD87B0574B16F` | `0xB94911EBA33AB6D5` |

Configure 後重新指定 ControlledObject 的責任仍由 Pre 交給呼叫者。真實 Cinemachine 更新順序、blend、阻尼、非單位父階層 scale、畫面及 Unity 物理仍需完整引擎驗收；本輪未執行 Unity PlayMode、Burst、IL2CPP、ARM64 或真實輸入裝置測試。

## 重現資訊

1. 固定從 `1de9777` 讀取產品、16 份測試及兩份快照。
2. 使用真實 Mathematics／NUnit，以 Roslyn C# 8、Release 編譯至 `MemoryStream`。
3. 反射展開 Test／TestCase，逐案例執行 SetUp／TearDown。
4. 執行 198 案例基準；僅在記憶體校準兩個已知快照期望值。
5. 原 127 項及 AD01–AD12 各從乾淨來源副本編譯，執行 182 案例集合。
6. 一次列舉四個方法的數值端點、共用檢查的呼叫位置、分量／正負號、slack 及繼承的不變式；執行 300 次端點變異。
7. 使用獨立固定見證確認 N32，以及矩陣中 † 的非等價項；不將這些見證混入 killed 統計。
8. 於三種符號設定執行連續 Step、AddLook 及 pitch 掃描。
9. 結束時確認 working tree、tracked diff、index diff 均為空；未寫入任何報告或交接檔案。

## 建議下一步

1. OPUS 修正 N32 的長度／角度合法域一致性，補上本輪固定見證。
2. OPUS 一次補齊矩陣中 † 的接受、拒絕及 Post 端點案例，包含 Initial 的精確零 Velocity。
3. SOL 複審上述固定集合；完整 Unity pipeline 驗收獨立處理。

- 2026-11-08 · ChatGPT Sol（SOL，唯讀，對象 1de9777）· done · P1 r16 fail（最高 low）：N30／N31 fixed；原 127 變異 117 killed／10 survived，300 次端點變異 101 killed／199 survived；新增 N32–N34 · 下一步：OPUS 修正合法域窄邊界並一次補齊已確認端點測試；SOL 複審