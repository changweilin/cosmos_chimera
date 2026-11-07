> 原始輸出：`codex exec -s read-only -m gpt-6.1-sol`，對象 `5d2cfd7`。未修改任何檔案。Claude Opus 已依 N28 修正、N29 補測試（見 phase-1.md 交接）。

**fail（最高 severity：low）**

# P1 第十四輪對抗式複審 · SOL

2026-11-08 · ChatGPT Sol（SOL）。

**N26、N27 fixed。** r13 的 126 項變異重跑為 **116 killed／10 survived**；T03、AA03 均由本次新增測試殺死。

新增兩項：

- **N28 · low · 產品契約違反：** grounded 沿坡投影進入固定長度門檻時，輸出速率不符合 Step 明列的沿坡速率 Post。
- **N29 · low · 契約測試缺口：** 將合法 gamepad rate 下界從 `>= 0` 改為 `> 0`，提交內測試仍全部通過。

對象：**`5d2cfd7de4916788ac94adcfbbe2d9d9f7e05b77`**，位於 `p1/r5-fixes` 歷史中。分支 tip 已前移，本輪始終固定讀取指定 commit。

已讀指定規約、r13 報告、最新交接及 `git diff cd6065e 5d2cfd7`。未切換工作分支、修改檔案或建立暫存檔；結束時 tracked diff、index diff 均為空。

以下 `Planet/`、`Player/` 相對於 `Assets/_Project/Features/`；行號對應受審提交。

## 方法與限制

未啟動 Unity Editor。

來源由 `git show 5d2cfd7:<path>` 取得；harness、變異及補充探針均保存在記憶體，以 Roslyn 編譯至 `MemoryStream`，載入 assembly 後執行 NUnit 反射 runner。每個案例獨立建立 fixture，執行 SetUp／TearDown。

環境：

- Windows x64、Windows PowerShell、.NET Framework CLR。
- Roslyn **3.7.0.0**、C# 8、Release 最佳化。
- 真實 Unity.Mathematics、NUnit **3.5.0.0**。
- Mathematics DLL SHA-256：`48049AFDD1E1FBA73BFFAE24A6DAB5E8B32BC63E387EC8BCEA53CB2A5382D12E`，與 r13 相同。
- 相依 DLL 使用相容版本解析；不宣稱等同 Unity Mono、Burst 或 IL2CPP。

`PlayerCameraRig.cs` 整份參與編譯。UnityEngine／Cinemachine 使用 managed stand-ins；Transform 支援單位 scale 下的父子位置／旋轉，brain 保留 `ControlledObject` 的 output 選擇語意。`PlanetTerrain.Configure` 擷取至建立 quadtree；`SphericalCharacter.Facing` 保留 getter 與契約檢查。未模擬 Unity 物理。

十五份提交內測試來源，共 **190 案例**：

| 執行集合 | 結果 |
|---|---|
| 完整基準，使用提交快照期望值 | **188 pass、2 個已知 Q-T6 mismatch** |
| 記憶體校準快照後的變異集合 | **174/174 pass** |
| `UNITY_EDITOR`：Configure＋r6 fixture | **46/46 pass** |
| `DEVELOPMENT_BUILD`：Configure＋r6 fixture | **46/46 pass** |
| 實際 output 四種別名的拒絕及重複 Configure 原子性 | **兩種開發符號各 4/4 pass** |
| N28 兩個補充沿坡 Post 見證，原碼 | **0 pass／2 fail** |
| 僅於記憶體改動沿坡恢復門檻後，同一見證 | **2/2 pass** |
| N29 零 gamepad rate 接受見證，原碼 | **2/2 pass** |
| 同一見證套用 AB01 | **0 pass／2 fail** |

變異集合排除 `CubeSphereTests`、`ChunkQuadtreeTests` 及百萬點 Warp 掃描；完整基準已包含這些測試。補充見證未混入提交內測試的 killed 統計。

r13 harness 未保存；126 項依報告中的 ID 與變異語意重建，不宣稱替換字串逐位相同。編譯失敗不計為 kill。

## r13 項目複審

| 項目 | 判定 | 證據 |
|---|---|---|
| N26 速度夾限觸發帶 | **fixed** | `SphericalMotorReviewR11Tests.cs:47` 新增合法域內的固定見證。 |
| N26 斷言有效性 | **fixed** | T03 僅使 N26 新測試失敗：**173 pass／1 fail**。 |
| N27 合法 output 覆寫 | **fixed** | `PlayerCameraRigConfigureTests.cs:260` 接受 brain 位於 target、實際 output 獨立的拓撲。 |
| N27 斷言有效性 | **fixed** | AA03 僅使 N27 新測試失敗：**173 pass／1 fail**。 |

### 新斷言的有效性

| 變異 | r13 | r14 |
|---|---|---|
| T03 夾限門檻移除 `1−2e-6` 餘裕 | survived | **killed** |
| AA03 額外檢查 `brain.transform` | survived | **killed** |

T03 的失敗值：

| 版本 | `PlanarVelocity.x` |
|---|---:|
| 原碼 | **9999.990234375** |
| T03 | **9999.9951171875** |

N26 的 `1e-3` 容差足以區分上述差異。

AA03 在 N27 的合法拓撲拋出 `ContractException`；原碼接受並完成接線及眼高設定。此區分不需要完整 Cinemachine pose pipeline。

### 邊界與額外契約掃描

分別以 `UNITY_EDITOR`、`DEVELOPMENT_BUILD`、無開發符號版本執行。下表為**每種設定**：

| 分佈 | 主輸出數 | 不變式違反 | 第二步拒絕 |
|---|---:|---:|---:|
| 一般 transport，混合無接觸／平地／陡坡 | 200,000 | 0 | 0 |
| 空中減速，改量接近原速度 | 200,000 | 0 | 0 |
| 地面減速，改量接近原速度 | 200,000 | 0 | 0 |
| 高速近乎全推牆 | 200,000 | 0 | 0 |
| 極小速度及平方長度下溢邊界 | 200,000 | 0 | 0 |
| **合計** | **1,000,000** | **0** | **0** |

三種設定合計 **3,000,000 個主輸出**，另逐一檢查第二步輸出。檢查有限性、速度域、切向性、Facing、Grounded 組合及兩步閉合。

其他探針每種設定：

| 探針 | 結果 |
|---|---|
| 合法倍率、pointer、stick、dt 的連續 AddLook | **100,000 次；0 有限性／範圍違反** |
| 起始 pitch 每 `0.01°`，搭配 `±160°`、`±180°`、`±1e7°` | **96,006 案例；0 範圍違反** |

以上掃描保護狀態不變式；額外針對 grounded 沿坡速率 Post 的見證仍發現 N28。掃描通過不代表所有 Post 均成立。

## 新發現

### N28 · low · 沿坡恢復的絕對門檻違反 grounded 速率 Post

位置：`Player/Runtime/Model/SphericalMotor.cs:221–222、326–333`。

Step Post 明列：

```text
while grounded, Velocity runs along the ground plane
at |PlanarVelocity| plus StickSpeed into the ground
```

實作僅在以下條件恢復投影前速率：

```csharp
float alongLenSq = math.lengthsq(along);
if (alongLenSq > 1e-12f)
{
    along *= math.length(vTan) / math.sqrt(alongLenSq);
}
```

正的沿坡投影長度若不超過 `1e-6 m/s`，會直接輸出未恢復速率的投影。契約未列出此例外。

#### 見證一：一般坡限、微小合法速率

```csharp
var settings = MotorSettings.Default;
settings.WalkSpeed = settings.SprintSpeed = 1e-6f;
settings.StickSpeed = 0f;

var up = new float3(0f, 1f, 0f);
var prev = SphericalMotor.Initial(up, new float3(0f, 0f, 1f));
var input = new MotorInput
{
    Move = new float2(0f, 1f),
    ViewForward = new float3(0f, 0f, 1f),
};
var ground = new GroundContact
{
    Hit = true,
    Normal = math.normalize(new float3(0f, 1f, -1f)),
    Distance = 0f,
};

// dt = 0.02f
```

結果：

| 欄位 | 實測 |
|---|---:|
| `settings.IsValid` | true |
| `Grounded` | true |
| `|PlanarVelocity|` | **1e-6 m/s** |
| `|Velocity|`，StickSpeed 為零 | **7.071068e-7 m/s** |
| 速率比例 | **0.707106769** |

此差異約 29.3%，不是 float 捨入誤差。

#### 見證二：合法近垂直坡限、正常 planar speed

沿用初始狀態與 input，改為：

```csharp
var settings = MotorSettings.Default;
settings.MaxSlopeDegrees = 89.99999f;
settings.StickSpeed = 0f;

ground.Normal = math.normalize(new float3(0f, 2e-7f, -1f));

// dt = 0.1f
```

所有 Pre 成立：

- `settings.IsValid == true`。
- `dot(ground.Normal, up) == 2e-7`。
- `cos(MaxSlopeDegrees) == 1.94707184e-7`，因此為可行走坡面。
- 接觸距離為零，初始 vertical speed 為零，未要求跳躍。

結果：

| 欄位 | 實測 |
|---|---:|
| `Grounded` | true |
| `PlanarVelocity` | `(0, 0, 4)` |
| `Velocity` | `(0, 8e-7, 0)` |
| Post 要求的沿坡速率 | **4 m/s** |
| 實際沿坡速率 | **8e-7 m/s** |

兩個見證在三種符號設定下均重現，不涉及 Unity stand-ins 或 Cinemachine。

補充 NUnit 見證在原碼為 **0/2 pass**；僅於記憶體將門檻改為 `alongLenSq > 0f` 後為 **2/2 pass**。此操作用於確認因果，不代表已完成對零值、下溢及近退化情況的修正設計。

**建議：**對非零沿坡投影採用能處理尺度差異的速率恢復，補上上述兩個 Post 見證。此 finding 評為 low，因觸發限於投影退化區間。

### N29 · low · 合法 gamepad rate 零端點沒有接受案例

位置：`Player/Runtime/PlayerCameraRig.cs:127–129、208–210`；測試缺口涉及 r6 與 Configure fixtures。

AddLook Pre 明列：

```text
0 ≤ gamepad rate ≤ MaxGamepadDegreesPerSecond
```

Configure 也引用相同倍率範圍。

新增變異 **AB01**：

```diff
- _gamepadDegreesPerSecond >= 0f
+ _gamepadDegreesPerSecond > 0f
```

此變異成功編譯，提交內測試仍為 **174/174 pass**。

合法見證：

```csharp
_gamepadDegreesPerSecond = 0f;

rig.AddLook(
    new Vector2(10f, 10f),
    new Vector2(1f, 1f),
    1f);
```

| 版本 | 結果 |
|---|---|
| 原碼 | 接受；`PendingLook == (1.2, 1.2)` |
| AB01 | `ContractException` |

另外，將同一零倍率套用到既有合法 Configure fixture：

| 版本 | Configure |
|---|---|
| 原碼 | 接受 |
| AB01 | `ContractException` |

兩個補充接受測試在原碼為 **2/2 pass**，AB01 為 **0/2 pass**。

這是可直接區分的 Pre 過度限制，屬於**非等價契約變異**。原碼目前正確；缺口在提交內測試。

**建議：**補上 gamepad rate 恰為零時，AddLook 與 Configure 均接受的案例。

## 變異結果

**r13 原 126 項：116 killed、10 survived。全部成功編譯並執行。**

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
| **r13 原 126 項合計** | **116** | **10** |
| 本輪新增 AB01 | 0 | 1 |
| **含新增項合計** | **116** | **11** |

### 原 126 項逐組結果

| 集合 | killed ID | survived ID |
|---|---|---|
| R | R01、R02、R02b、R03–R06 | — |
| P | P01–P09、P11–P16 | — |
| B | B01–B07 | — |
| S | S01–S11 | — |
| T | **T01–T07** | — |
| U | U01–U06、U10–U17 | U07、U08、U09 |
| V | V01–V08 | V09、V10 |
| W | W01–W05、W11 | W06–W10 |
| X | X01–X05 | — |
| Y | Y01–Y06 | — |
| Z | Z01–Z22 | — |
| AA | **AA01–AA08** | — |

除 T03、AA03 由 survived 改為 killed 外，分類與 r13 相同。

### AA01–AA08

| ID／變異 | 結果 |
|---|---|
| AA01 改回檢查 `brain.transform` | **killed，4 fail** |
| AA02 僅在 output 等於 brain 自身物件時檢查 | **killed，3 fail** |
| AA03 額外檢查 `brain.transform` | **killed，1 fail** |
| AA04 實際 output guard 移至 `_planet` 寫入後 | **killed，4 fail** |
| AA05 guard 移至 `_pivot` 寫入後 | **killed，4 fail** |
| AA06 guard 移至 first-person TrackingTarget 接線後 | **killed，4 fail** |
| AA07 僅首次 Configure 檢查實際 output | **killed，1 fail** |
| AA08 實際 output 僅比較物件相等 | **killed，2 fail** |

### 存活項解讀

| 項目 | 分類 |
|---|---|
| U08、U09 | 等價：剩餘範圍比較仍拒絕 NaN／Infinity。 |
| U07、V09、V10、W06、W07 | 測試削弱操作。 |
| W08、W09 | 同時削弱測試後，產品變異重新漏過；不代表原提交測試漏過。 |
| W10 | 仍缺 Unity 可達且可區分的非有限 Transform 見證；不列為 finding。 |
| **AB01** | 非等價契約變異；構成 **N29**。 |

## 非阻擋觀察

- **Q-T6 快照差異與 r13 相同，不影響 verdict：**

  | 快照 | 提交期望值 | CLR 實測 |
  |---|---|---|
  | 預設 octave | `0xB7C5CA249FD8C22D` | `0x383CA51ADC74C561` |
  | 八 octave | `0xAF9DD87B0574B16F` | `0xB94911EBA33AB6D5` |

- Configure 後重新指定 `ControlledObject` 的安全責任已由 Pre 交給呼叫者，未列為新發現。
- 真實 Cinemachine 更新順序、blend、阻尼、非單位父階層 scale、攝影機畫面及 Unity 物理仍需完整引擎驗收。
- 未執行 Unity PlayMode、Burst、IL2CPP、ARM64 或真實輸入裝置驗收。
- 本輪未找到原碼 AddLook／Configure 的新可達契約反例；N29 指向測試缺口。

## 重現資訊

1. 固定從 `5d2cfd7` 讀取來源、十五份測試與兩份快照，不 checkout。
2. 使用真實 Mathematics／NUnit，以 Roslyn C# 8、Release 編譯至 `MemoryStream`。
3. 反射展開 `[Test]`／`[TestCase]`，逐案例建立 fixture 並執行 SetUp／TearDown。
4. 執行 190 案例基準，再於記憶體校準已知快照差異。
5. 每個變異從乾淨來源副本獨立編譯，執行 174 案例集合。
6. 於三種符號設定執行五種 Step 分佈、AddLook 與 pitch 掃描。
7. 於兩種開發符號設定重跑 Configure＋r6 fixture，以及四種實際 output 別名原子性探針。
8. 分別以沿坡速率見證及零 gamepad rate 接受見證驗證 N28／N29；不混入原 126 項 killed 計數。

## 建議下一步

1. OPUS 修正 N28 的沿坡速率恢復，補上微速與近垂直合法坡限見證。
2. OPUS 補 N29 的零 gamepad rate 接受測試。
3. SOL 複審沿坡 Post、AB01，以及原 126 項變異。

- 2026-11-08 · ChatGPT Sol（SOL，唯讀，對象 5d2cfd7）· done · P1 r14 fail（最高 low）：N26／N27 fixed；原 126 變異 116 killed／10 survived，新增 N28 沿坡速率 Post 違反及 N29 零倍率接受測試缺口 · 下一步：OPUS 修 N28 並補 N29 測試；SOL 複審