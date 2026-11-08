> 原始輸出：`codex exec -s read-only -m gpt-6.1-sol`，對象 `de0a959`。未修改任何檔案。Claude Opus 已依 N35–N37 補測試（見 phase-1.md 交接）。

**fail（最高 severity：low；皆為已證明的測試缺口，未發現原碼的可達產品契約違反）**

# P1 第十七輪對抗式複審 · SOL

2026-11-08 · ChatGPT Sol（SOL）。

**N32 fixed、N33 fixed、N34 partial。**

- 原 **127 項變異：117 killed／10 survived**。
- 重新推導的 **300 次端點變異：193 killed／107 survived**。
- r16 全部 **93 個 † 變異：92 killed／1 survived**。
- AD01–AD12：**10 killed／2 survived**；其中 AD12 本輪建立合法非等價見證。

新增 **N35、N36、N37，均為 low 測試缺口**。補充見證不混入提交內測試的 killed 統計。

對象：**`de0a95980167062ee91984a85c297abfe981ee75`**，分支 `p1/r5-fixes`。

已讀指定規約、r16 報告、最新交接及 `git diff 1de9777 de0a959`。受審來源由 `git show de0a959:<path>` 取得；未 checkout、修改 repo 或建立暫存檔。結束時 working tree、tracked diff、index diff 均無變更，仍在原分支。

以下 `Player/`、`Planet/` 相對於 `Assets/_Project/Features/`；行號對應受審提交。

## 方法與限制

未啟動 Unity Editor。

產品來源、測試、harness、變異及補充見證均保存在記憶體，以 Roslyn 編譯至 `MemoryStream`，載入 assembly 後執行 NUnit 反射 runner。逐案例建立 fixture 與 NUnit 執行上下文，執行 SetUp／TearDown。

環境：

- Windows x64、Windows PowerShell、.NET Framework CLR。
- Roslyn **3.7.0.0**、C# 8、Release 最佳化。
- 真實 Unity.Mathematics、NUnit **3.5.0.0**。
- Mathematics DLL SHA-256：`48049AFDD1E1FBA73BFFAE24A6DAB5E8B32BC63E387EC8BCEA53CB2A5382D12E`。
- 相依 DLL 使用相容版本解析；不宣稱等同 Unity Mono、Burst 或 IL2CPP。

`PlayerCameraRig.cs` 整份參與編譯。UnityEngine／Cinemachine 使用 managed stand-ins；Transform 支援單位 scale 的父子位置／旋轉，brain 保留 `ControlledObject` 語意。`PlanetTerrain.Configure` 擷取至建立 quadtree；`SphericalCharacter.Facing` 保留 getter 與契約檢查。未模擬 Unity 物理。

| 執行集合 | 結果 |
|---|---:|
| 17 份受審 fixture，完整基準 | **248 pass／2 個已知 Q-T6 mismatch，共 250 案例** |
| 記憶體校準快照後的變異集合 | **234/234 pass** |
| `UNITY_EDITOR`：Configure＋r6 fixtures | **73/73 pass** |
| `DEVELOPMENT_BUILD`：Configure＋r6 fixtures | **73/73 pass** |
| 新增 `SphericalMotorReviewR16Tests`，兩種開發符號 | **各 29/29 pass** |
| 原 127 項變異 | **117 killed／10 survived** |
| r16 全部 † 變異 | **92 killed／1 survived，共 93 項** |
| AD01–AD12 | **10 killed／2 survived** |
| 端點掃描 | **193 killed／107 survived，共 300 次** |
| 最終變異編譯錯誤 | **0** |

變異集合排除 `CubeSphereTests`、`ChunkQuadtreeTests` 及百萬點 Warp 掃描；完整基準包含它們。

歷輪 harness 未保存；127 項依既有 ID 與語意重建。端點集合依新碼重新推導，仍為 150 個配置、每配置兩次操作；不宣稱與 r16 替換字串逐位相同。

### 額外契約掃描

三種設定：`UNITY_EDITOR`、`DEVELOPMENT_BUILD`、無開發符號。下表為**每種設定**：

| 探針 | 結果 |
|---|---|
| transport、減速、接觸、近乎全推牆及微速 Step | **1,000,000 個主輸出；0 所檢查不變式違反** |
| 將每個輸出再交給 Step | **1,000,000 次；0 拒絕／不變式違反** |
| 合法倍率、pointer、stick、dt 的連續 AddLook | **100,000 次；0 有限性／範圍違反** |
| 起始 pitch 每 `0.01°`，搭配 `±160°`、`±180°`、`±1e7°` | **96,006 案例；0 違反** |

三種設定合計 **300 萬個 Step 主輸出，另執行 300 萬次第二步**。另以近垂直坡定向搜尋建立 N37 固定見證；一般隨機掃描不能替代該見證。

## r16 項目複審

| 項目 | 判定 | 證據 |
|---|---|---|
| N32 長度 slack | **fixed** | 原三個合法內側見證現在接受；恢復舊 unit／Move guard，各被新增測試殺死。 |
| N32 浮點退化 Pre | **接受契約變更** | 文件現在明列正規化方向投影的 float 平方長度判定，與 `TangentOr` 一致。 |
| N33 AddLook／Configure | **fixed** | r16 指出的端點、yaw 一圈、pitch `±160`、default planet 拒絕原子性均被殺死。 |
| N34 up 下側、90°、速度、settings、Initial 零速度 | **fixed** | 對應 † 變異均被殺死。 |
| N34 ViewForward 負向退化切點 | **partial** | 額外拒絕接近 `-up` 的合法切點仍存活；見 N36。 |

### N32 的契約是否只是掩蓋缺陷

本輪接受這項變更，但應將它理解為**契約合法域的明確變更**。

舊文件使用幾何角度；新文件指定 float 判定：

```text
正規化方向的切向投影 lengthsq ≤ 1e-6
```

r16 的角度見證會落在該 float 等號上，現在明確屬於 Initial 排除域；作為 ViewForward 時仍屬合法輸入，承諾使用既有 facing。實作保留尺度縮放、退化 fallback 與第二次正交化，沒有藉此放寬有限性、單位長度或切向 Post。

未發現此明訂合法域內的原碼反例。剩餘問題是**新平方區間的精確端點，以及 fallback 行為尚未完整被測試鎖住**，分別列為 N35、N36。

### 全部 † 重跑

r16 的 † 記號展開為 **93 個變異操作**。其中：

- **92 killed**。
- 唯一存活：**Step 的 ViewForward 接近 `-up`、投影平方長度恰為 `1e-6f` 時，額外拒絕輸入**。
- r16 的舊 Move 端點 `lengthsq == 1.002f` 另行重跑，flip／reject **均 killed**；未以新的上界代替這項既有驗證。

## 新發現

### N35 · low · 新平方長度合法端點仍可被額外排除而不被測出

位置：`Player/Runtime/Model/SphericalMotor.cs:187–193、214、245、264`；`Player/Tests/EditMode/SphericalMotorReviewR16Tests.cs:24–55`。

新契約明列：

```text
UnitSqMin ≤ lengthsq(up) ≤ UnitSqMax
lengthsq(Move) ≤ UnitSqMax
```

以下輸入符合這些明訂的 float 比較：

```csharp
float y = math.asfloat(math.asuint(0.999f) - 1u);

var lowerUp = new float3(0.000244140625f, y, 0f);
var upperUp = new float3(0f, 1.001f, 0f);
var upperMove = new float2(1f, math.sqrt(0.002001f));
```

| 見證 | 實測平方長度 |
|---|---:|
| `lowerUp` | `0.998000979423523`，恰為 `UnitSqMin` |
| `upperUp` | `1.0020010471343994`，恰為 `UnitSqMax` |
| `upperMove` | 恰為 `UnitSqMax` |

其餘參數使用正常值：

| 呼叫 | 原碼 | 排除該端點的變異 |
|---|---|---|
| Initial，`lowerUp` | 接受 | `ContractException` |
| Initial，`upperUp` | 接受 | `ContractException` |
| Step，新 up 為 `lowerUp` | 接受 | `ContractException` |
| Step，新 up 為 `upperUp` | 接受 | `ContractException` |
| Step，Move 為 `upperMove` | 接受 | `ContractException` |

這些變異的提交內測試仍為 **234/234 pass**。

新增測試的 `JustBelow1001`、`0.999f` 與舊 `lengthsq == 1.002f` 都有價值，但沒有涵蓋上述所有新等號。

對應端點配置：**S04、S05、S09、I01、I03，flip／reject 共 10 個存活操作**；其中包含重複語意。

**建議：**直接固定平方上下界的接受案例，Initial／Step 分別驗證，並補 Move 新上界。

### N36 · low · 退化切點測試未鎖住負向拒絕與 ViewForward fallback

位置：`Player/Runtime/Model/SphericalMotor.cs:98–100、208、287、398–414`；新增測試 `SphericalMotorReviewR16Tests.cs:68–75`。

#### 負向 Initial 切點

```csharp
var up = new float3(0f, 1f, 0f);
var nearDown = new float3(2.4e-7f, -1f, 0.0010000004f);
```

此輸入的正規化切向投影恰為 `lengthsq == 1e-6f`。

| 版本 | Initial 結果 |
|---|---|
| 原碼 | `ContractException`，符合新 Pre |
| 僅允許負向等號通過的變異 | 接受，Facing 約 `(0.0002399999, 0, 1)` |

該變異仍 **234/234 pass**。新增測試只有正向 `nearUp`。

#### ViewForward 必須使用既有 facing

使用相同切點，但將既有 facing 設為 `+X`：

```csharp
var prev = SphericalMotor.Initial(up, new float3(1f, 0f, 0f));
var input = new MotorInput
{
    Move = new float2(0f, 1f),
    ViewForward = nearUp, // nearDown 同樣成立
};
```

隔離變異至 **Step 的 ViewForward 呼叫端**：切點由 fallback 改為使用投影方向；Initial 保持原碼。

| 結果 | 原碼 | 變異 |
|---|---|---|
| PlanarVelocity | `(0.16, 0, 0)` | 約 `(0.0000384, 0, 0.16)` |
| Facing | `(1, 0, 0)` | 約 `(0.9685832, 0, 0.2486899)` |
| `dot(Facing, +X)` | `1` | `0.968583167` |

提交內測試仍 **234/234 pass**。

現有切點測試只確認接受與 Facing 單位長度；錯誤方向仍可通過。

另外，僅在負向 ViewForward 切點額外拒絕，也仍存活。原碼接受此合法輸入，該變異拋出例外。

對應 **I07 flip、S40／S41 flip、S42 flip／reject，共 5 個非等價存活操作**。

**建議：**補負向 Initial 拒絕；正負 ViewForward 切點均使用與投影方向不同的既有 facing，直接斷言 fallback 的方向與速度。

### N37 · low · AD12 已有合法非等價見證，沿坡速率保護仍漏測

位置：`Player/Runtime/Model/SphericalMotor.cs:233–236、343–356`。

AD12：

```diff
- if (math.lengthsq(a) > 0f)
+ if (math.lengthsq(a) > 1e-14f)
```

仍為 **234/234 pass**。

本輪找到並以精確 float bits 固定以下見證：

```csharp
var up = math.asfloat(new uint3(
    1062333398u, 3176025790u, 3205624947u));

var tangent = math.asfloat(new uint3(
    1057905043u, 3190418800u, 1062239557u));

var normal = math.asfloat(new uint3(
    1057905045u, 3190418800u, 1062239555u));

var settings = MotorSettings.Default;
settings.MaxSlopeDegrees = math.asfloat(math.asuint(90f) - 1u);
settings.GroundAcceleration = settings.AirAcceleration = 1e-20f;
settings.StickSpeed = 0f;

var prev = SphericalMotor.Initial(up, tangent);
prev.PlanarVelocity = prev.Velocity = tangent * 9000f;

var next = SphericalMotor.Step(
    settings, prev, default,
    new GroundContact { Hit = true, Normal = normal, Distance = 0f },
    up, 0.02f);
```

法線來自正規化運算，沒有利用非單位法線 guard slack。

| 條件／結果 | 原碼 | AD12 |
|---|---:|---:|
| `settings.IsValid` | true | true |
| prev 不變式檢查 | 通過 | 通過 |
| `lengthsq(normal)` | `1` | `1` |
| `dot(normal, next.Up)` | `2.08616257e-7` | 相同 |
| `cos(MaxSlopeDegrees)` | `1.94707184e-7` | 相同 |
| 第一個投影 `lengthsq(a)` | `4.440892e-15` | 相同 |
| Grounded | true | true |
| `|PlanarVelocity|` | `9000` | `9000` |
| `|Velocity|`，StickSpeed 為零 | **`9000.002`** | **`0`** |

投影非零，因此不屬於 Post 的零速度／float 平行例外。AD12 違反「沿坡分量保持 `|PlanarVelocity|`」的明列 Post。

固定見證在 **三種符號設定均重現相同區分**。這是測試缺口；原碼目前正確。

**建議：**加入此固定見證，斷言非零沿坡分量及相對速率，而非只檢查有限性或 MotorState 速度上限。

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

| 存活項 | 分類 |
|---|---|
| U08、U09 | **等價**：剩餘比較仍拒絕非有限倍率。 |
| U07、V09、V10、W06、W07 | **測試削弱操作**。 |
| W08、W09 | **同時削弱測試與產品**；不代表原提交測試漏過。 |
| W10 | 有限 Transform 定義域內無作用；非有限 pose 的 Unity 可達性尚未證明，列非阻擋觀察。 |

原 127 項沒有新增已證明的非等價測試缺口。

### AD01–AD12

| ID／變異 | 結果 |
|---|---|
| AD01 坡限 `>=→>` | killed，1 fail |
| AD02 grounded 距離 `<=→<` | killed，1 fail |
| AD03 上升速度 `<=0.5→<0.5` | killed，1 fail |
| AD04 gamepad rate 上端點 `<=→<` | killed，2 fail |
| AD05 pointer 上端點 `<=→<` | killed，3 fail |
| AD06 planar clamp 觸發 `>→>=` | survived；本後端等號操作無可區分變化 |
| AD07 額外拒絕零 sensitivity | killed，2 fail |
| AD08 look dt 上端點 `<=→<` | killed，5 fail |
| AD09 Step dt 上端點 `<=→<` | killed，3 fail |
| AD10 stick 容差端點 `<=→<` | killed，3 fail |
| AD11 StickSpeed 貢獻減半 | killed，3 fail |
| AD12 沿坡投影門檻 `>0→>1e-14` | **survived；已證明非等價，N37** |

### 完整端點掃描

**150 個配置 × 兩種操作＝300 次：193 killed／107 survived。**

| 集合 | 配置 | killed | survived |
|---|---:|---:|---:|
| AddLook／相關 orbit 端點 A | 23 | 46 | 0 |
| Configure C | 22 | 42 | 2 |
| Step Pre／input S | 42 | 46 | 38 |
| Settings V | 21 | 32 | 10 |
| Initial I | 20 | 7 | 33 |
| Step Post P | 22 | 20 | 24 |
| **合計** | **150** | **193** | **107** |

符號：

- **K**：killed。
- **E**：等價、重複拒絕，或端點在文件合法域之外。
- **†**：已建立合法見證的非等價缺口。
- **U**：存活，但未完成合法域全等價證明，也未建立非等價見證；不列 finding。
- 每列範圍內的每個配置均有列出的 flip／reject 結果。

新 unit guard 的配置改為其平方區間端點；另保留長度兩側配置。操作包含呼叫位置隔離與重複語意。

#### AddLook：23 個配置

| ID／端點 | flip／reject |
|---|---|
| A01–A04：pointer X／Y 各 `±1e6` | K／K |
| A05–A08：stick X／Y 各 `±1.001` | K／K |
| A09–A10：dt `0`、`1` | K／K |
| A11–A13：sensitivity `-10`、`0`、`+10` | K／K |
| A14–A15：gamepad rate `0`、`3600` | K／K |
| A16–A17：raw yaw `±360` | K／K |
| A18–A19：pending pitch `±180` | K／K |
| A20–A21：pending pitch `±160` | K／K |
| A22–A23：orbit pitch `±80` | K／K |

#### Configure：22 個配置

| ID／端點 | flip／reject |
|---|---|
| C01–C03：sensitivity `-10`、`0`、`+10` | K／K |
| C04–C05：gamepad rate `0`、`3600` | K／K |
| C06–C11：target 各軸 `±32768` | K／K |
| C12：center exclusion 的 `1e-3` 等號 | K／E |
| C13–C14：Radius `1`、`8000` | K／K |
| C15–C20：Center 各軸 `±16000` | K／K |
| C21：validity 的 Radius `0` | K／E |
| C22：Configure 丟棄 queue | K／K |

#### Step Pre／input：42 個配置

| ID／端點 | flip／reject |
|---|---|
| S01：dt `0` | K／E |
| S02：dt `0.1` | K／K |
| S03：新舊 up `90°` | K／E |
| S04：Move 平方長度 `UnitSqMax` | **S†／S†** |
| S05：新 up 平方區間端點 | **S†／S†** |
| S06–S07：prev.Up／Facing 平方 guard 端點 | E／E |
| S08：新 up 長度 `0.999f` | K／K |
| S09：新 up 長度 `1.001f` | **S†／S†** |
| S10–S13：prev.Up／Facing 長度 `0.999f`、`1.001f` | E／E |
| S14–S16：prev.Facing 切向 guard，absolute／signed | E／E |
| S17–S19：prev.PlanarVelocity 相對切向 guard，absolute／signed | E／E |
| S20–S25：prev.PlanarVelocity 各軸 `±10000` | K／K |
| S26–S31：prev.Velocity 各軸 `±20000` | K／K |
| S32：prev.PlanarVelocity 平方長度 `1e8` | K／K |
| S33：prev.Velocity 平方長度 `4e8` | K／K |
| S34–S35：prev.VerticalSpeed `±10000` | K／K |
| S36：Hit 的 Distance `0` | K／K |
| S37：normal 平方長度 error `2e-3` | E／E |
| S38：Grounded prev 的 VerticalSpeed `0` | K／K |
| S39：ViewForward 零向量 | K／K |
| S40–S41：ViewForward cutoff，整體／正向 | **S†／K** |
| S42：ViewForward cutoff，負向 | **S†／S†** |

#### Settings：21 個配置

| 欄位／ID | 下界 flip／reject | 上界 flip／reject |
|---|---|---|
| WalkSpeed，V01–V02 | K／E | K／K |
| SprintSpeed，V03–V04 | E／E | K／K |
| GroundAcceleration，V05–V06 | K／E | K／K |
| AirAcceleration，V07–V08 | K／E | K／K |
| Gravity，V09–V10 | K／E | K／K |
| JumpHeight，V11–V12 | K／E | K／K |
| TurnRateDegrees，V13–V14 | K／E | K／K |
| SnapDistance，V15–V16 | K／K | K／K |
| StickSpeed，V17–V18 | K／K | K／K |
| MaxSlopeDegrees，V19–V20 | K／E | K／E |
| WalkSpeed＝SprintSpeed，V21 | K／K | — |

SprintSpeed 零下界的單獨放寬仍受 `WalkSpeed > 0` 與 `WalkSpeed ≤ SprintSpeed` 阻擋。

#### Initial：20 個配置

| ID／端點 | flip／reject |
|---|---|
| I01：up 平方區間端點 | **S†／S†** |
| I02：up 長度 `0.999f` | K／K |
| I03：up 長度 `1.001f` | **S†／S†** |
| I04：facing 零向量 | K／E |
| I05–I06：facing cutoff，整體／正向 | K／E |
| I07：facing cutoff，負向 | **S†／E** |
| I08：Post Velocity＝0 | K／K |
| I09–I10：Facing 長度 `0.9999f`、`1.0001f` | U／U |
| I11–I13：Facing 切向誤差 `1e-4`，absolute／signed | U／U |
| I14–I16：PlanarVelocity 相對切向誤差端點 | E／E |
| I17–I18：planar／Velocity 平方長度上界 | E／E |
| I19–I20：VerticalSpeed `±10000` | E／E |

#### Step Post：22 個配置

| ID／端點 | flip／reject |
|---|---|
| P01：Distance＝SnapDistance | K／K |
| P02：slope＝MaxSlope | K／K |
| P03：prev.VerticalSpeed＝0.5 | K／K |
| P04：planar clamp 觸發等號 | E／K |
| P05：planar clamp 目標 | K／K |
| P06：沿坡 planar scale＝0 | E／K |
| P07：沿坡投影平方長度＝0 | U／U |
| P08：controlled planar 動態速度上界 | K／K |
| P09：jump vertical speed | K／K |
| P10：Grounded 時 VerticalSpeed＝0 | K／K |
| P11–P12：Facing 長度端點 | U／U |
| P13–P15：Facing 切向誤差端點 | U／U |
| P16–P18：planar 相對切向誤差端點 | U／U |
| P19–P20：planar／Velocity 平方長度上界 | E／E |
| P21–P22：VerticalSpeed `±10000` | K／K |

107 個存活操作中：

| 分類 | 數量 |
|---|---:|
| 已證明非等價缺口，N35／N36 | **15** |
| 等價／合法域外端點操作 E | **64** |
| 尚未證明全域等價或非等價 U | **28** |
| 測試削弱操作 | **0** |

沒有將 U 冒稱為已證明等價，也沒有把它們當成 finding。

## 非阻擋觀察

### 未完成等價證明的存活項

端點 U 共 28 個操作：

- **I09–I13，兩種操作共 10 個**：Initial Facing 長度／切向誤差端點。
- **P07、P11–P18，兩種操作共 18 個**：Step 的 float 平行投影例外，以及 Facing／planar 誤差端點。

本輪未建立這些端點的合法可區分見證。另一次 500 萬組近乎平行定向搜尋沒有找到合法的投影平方長度零值見證；這不能代替全域等價證明。

W10 仍缺真實 Unity 中可達且可區分的非有限 Transform 見證。有限 pose 下移除該子句沒有區分效果；不以 stand-in 可任意存入 NaN 作為產品 finding。

### guard slack 不擴張文件合法域

`prev` 仍須符合 MotorState 不變式。其較寬的檢查 slack，不代表人工放在 `1e-3` guard 邊界的狀態符合文件 Pre。

GroundContact 仍要求單位法線；本輪 N37 使用正規化法線、`lengthsq == 1`，與先前非單位法線觀察不同。

### 已知後端與完整引擎限制

Q-T6 維持已知差異，不影響 verdict：

| 快照 | 提交期望值 | CLR 實測 |
|---|---|---|
| 預設 octave | `0xB7C5CA249FD8C22D` | `0x383CA51ADC74C561` |
| 八 octave | `0xAF9DD87B0574B16F` | `0xB94911EBA33AB6D5` |

真實 Cinemachine 更新順序、blend、阻尼、非單位父階層 scale、Unity 物理及畫面仍需完整 pipeline 驗收。本輪未執行 Unity PlayMode、Burst、IL2CPP、ARM64 或真實輸入裝置測試。

## 重現資訊

1. 固定從 `de0a959` 取得來源，核對兩份快照原文及 r16 最新交接。
2. Roslyn C# 8、Release 編譯至 `MemoryStream`，使用真實 Mathematics／NUnit。
3. 展開 Test／TestCase，逐案例建立 fixture、執行上下文與 SetUp／TearDown。
4. 執行 250 案例基準；僅在記憶體校準兩個已知快照期望值。
5. 原 127 項、AD01–AD12 及 300 次端點操作，各從乾淨來源副本編譯，執行 234 案例集合。
6. 逐一核對 r16 展開後的 93 個 †；舊 Move 端點另行保留驗證。
7. 用獨立見證確認 N35／N36；以精確 float bits 固定 N37，三種符號設定交叉確認。
8. 執行三種設定的 Step／AddLook／pitch 掃描。
9. 結束時確認 repo、index 與分支均未改變。

## 建議下一步

1. OPUS 補 N35 的精確平方區間端點接受案例。
2. OPUS 補 N36 的負向切點，以及正負 ViewForward 的實際 fallback 結果。
3. OPUS 加入 N37 固定見證，鎖住極小但非零沿坡投影的速率 Post。
4. SOL 重跑固定變異集合；完整 Unity pipeline 驗收獨立處理。

- 2026-11-08 · ChatGPT Sol（SOL，唯讀，對象 de0a959）· done · P1 r17 fail（最高 low，皆測試缺口）：N32／N33 fixed、N34 partial；127 變異 117 killed／10 survived，300 次端點變異 193 killed／107 survived，r16 † 92 killed／1 survived；新增 N35–N37 · 下一步：OPUS 補精確平方端點、正負退化 fallback 與 AD12 固定見證；SOL 複審