> 原始輸出：`codex exec -s read-only -m gpt-6.1-sol`，對象 `cd6065e`。未修改任何檔案。Claude Opus 已依 N26／N27 補測試（見 phase-1.md 交接）。


# P1 第十三輪對抗式複審 · SOL

2026-11-08 · ChatGPT Sol（SOL）。

**N24、N25 fixed。** 舊 118 項變異重跑為 **107 killed／11 survived**；新增 8 項為 **7 killed／1 survived**。

新增兩項 **low 契約測試缺口**：

- **N26**：舊存活項 T03 已證明非等價，提交內測試仍未保護明列的速度夾限區間。
- **N27**：新增 AA03 錯誤拒絕符合新 Configure Pre 的合法 output 覆寫，仍通過提交內測試。

本輪未找到產品原碼的可達 AddLook／Configure／Step 契約反例。Q-T6 與完整 Unity／Cinemachine pipeline 驗收不影響本輪判定。

對象：**`cd6065ef6227c604ffdf48d3675aec1869b519ed`**，分支 `p1/r5-fixes`。

已讀指定規約、r12 報告、最新交接及 `git diff c2a07f8 cd6065e`。受審來源均以 `git show cd6065e:<path>` 取得。未切換分支、修改檔案或建立暫存檔；結束時 tracked diff、index diff 均為空。

以下 `Planet/`、`Player/` 相對於 `Assets/_Project/Features/`；行號對應受審提交。

## 方法與限制

未啟動 Unity Editor。

來源、harness、變異與補充探針均保存在記憶體，以 Roslyn 編譯至 `MemoryStream`，載入 assembly 後執行 NUnit 反射 runner，逐案例執行 SetUp／TearDown。

環境：

- Windows x64、Windows PowerShell、.NET Framework CLR。
- Roslyn **3.7.0.0**、C# 8、Release 最佳化。
- 真實 Unity.Mathematics、NUnit **3.5.0.0**。
- Mathematics DLL SHA-256：`48049AFDD1E1FBA73BFFAE24A6DAB5E8B32BC63E387EC8BCEA53CB2A5382D12E`，與 r12 相同。
- 相依 DLL 使用相容版本解析；不宣稱等同 Unity Mono、Burst 或 IL2CPP。

`PlayerCameraRig.cs` 整份參與編譯。UnityEngine／Cinemachine 使用 managed stand-ins；Transform 包含單位 scale 下的父子位置／旋轉運算，brain 保留 `ControlledObject` 的 output 選擇語意。`PlanetTerrain.Configure` 擷取至建立 quadtree；`SphericalCharacter.Facing` 保留原始 getter 與契約檢查。未模擬 Unity 物理。

本機 Cinemachine 原始碼版本核對為 **3.1.7**；其 pose 寫回仍使用 `ControlledObject.transform`。

十五份提交內測試來源，共 **188 案例**：

| 執行集合 | 結果 |
|---|---|
| 完整基準集合，使用提交快照期望值 | **186 pass、2 個已知 Q-T6 mismatch** |
| 記憶體校準快照後的變異確認集合 | **172/172 pass** |
| `UNITY_EDITOR`：Configure＋r6 fixture | **45/45 pass** |
| `DEVELOPMENT_BUILD`：Configure＋r6 fixture | **45/45 pass** |
| 實際 output 四種別名：拒絕及重複 Configure 原子性 | **兩種開發符號各 4/4 pass** |
| 兩個新增契約見證，原始產品碼 | **2/2 pass** |
| 分別套用 T03、AA03 後執行新增見證 | **各 1 pass／1 fail** |

逐項變異使用 172 案例集合：排除 `CubeSphereTests`、`ChunkQuadtreeTests` 及百萬點 Warp 掃描；完整基準已包含這些測試。補充見證未混入提交內測試的 killed 統計。

舊 harness 未保存；118 項依 r12 表列語意重建，不宣稱替換字串逐位相同。

## r12 項目複審

| 項目 | 判定 | 證據 |
|---|---|---|
| N24 實際 brain writer | **fixed** | `PlayerCameraRig.cs:93` 改查 `brain.ControlledObject.transform`。 |
| N24 檢查時機 | **fixed** | 呼叫位於 `_planet` 等欄位與 camera 接線之前。 |
| N24 契約範圍 | **接受** | Pre 明列實際 output，並明訂只在 Configure 檢查；後續重新指定由呼叫者負責。 |
| N24 拒絕及原子性 | **fixed** | 新測試涵蓋 pivot、target 祖先、自有 world-up；額外四種別名探針均通過。 |
| N25 世界位置 X／Z 上限 | **fixed** | Z07 現在被 **3 案例**殺死。 |
| N25 pivot 祖先 ownership | **fixed** | Z20 現在被 **1 案例**殺死。 |
| N25 世界位置與 Center offset 區別 | **fixed** | Z22 現在被 **1 案例**殺死。 |

### 新斷言的有效性

| 變異 | r12 | r13 |
|---|---|---|
| Z07 上限只檢查 Y | survived | **killed，169 pass／3 fail** |
| Z20 pivot ownership 只比較相等 | survived | **killed，171 pass／1 fail** |
| Z22 上限改檢查 `position − Center` | survived | **killed，171 pass／1 fail** |
| AA01 ownership 改回 `brain.transform` | — | **killed，169 pass／3 fail** |
| AA04 實際 output guard 移至 `_planet` 寫入後 | — | **killed，168 pass／4 fail** |

Z07 的失敗包含新增 X／Z 超界案例；Z20 由 camera 為 pivot 祖先的案例殺死。Z22 在非零 Center 下錯誤接受世界位置超界輸入，被新增案例殺死。

實際 output 分別指向 target、pivot、WorldUpReference、target 祖先的補充探針，均確認拒絕且既有 planet、接線、world-up 參照及 pivot 位置未變。

### 邊界與額外契約掃描

分別以 `UNITY_EDITOR`、`DEVELOPMENT_BUILD`、無開發符號版本執行。下表為**每種設定**：

| 分佈 | Step 輸出數 | 不變式違反 | Step 拒絕 |
|---|---:|---:|---:|
| 一般 transport，混合無接觸／平地／陡坡 | 200,000 | 0 | 0 |
| 空中減速，改量接近原速度 | 200,000 | 0 | 0 |
| 地面減速，改量接近原速度 | 200,000 | 0 | 0 |
| 高速近乎全推牆 | 200,000 | 0 | 0 |
| 極小速度及平方長度下溢邊界 | 200,000 | 0 | 0 |
| **合計** | **1,000,000** | **0** | **0** |

三種設定合計 **3,000,000 個 Step 輸出**；檢查有限性、速度域、切向性、Facing 及兩步閉合。

其他探針每種設定：

| 探針 | 結果 |
|---|---|
| 合法倍率、pointer、stick、dt 的連續 AddLook | **100,000 次；0 有限性／範圍違反** |
| 起始 pitch 每 `0.01°`，搭配 `±160°`、`±180°`、`±1e7°` | **96,006 案例；0 範圍違反** |

以上為指定分佈的實測結果，不是所有浮點輸入的形式證明。

## 新發現

### N26 · low · T03 非等價：明列的速度夾限區間仍無測試保護

位置：`Player/Runtime/Model/SphericalMotor.cs:219–221、309–311`；測試保護缺口位於 `SphericalMotorReviewR5Tests`。

Step Post 明列：

```text
lengthsq ≥ MaxStateSpeed²·(1−2e-6)
→ rescale 至 MaxStateSpeed·(1−1e-6)
```

T03 將觸發門檻改為：

```csharp
math.lengthsq(vTan) > MaxStateSpeed * MaxStateSpeed
```

它保留超界夾限，卻跳過合法速度域內明列的窄區間。提交內測試仍為 **172/172 pass**。

可區分見證：

```csharp
settings = MotorSettings.Default;
settings.AirAcceleration = 1e-20f;

up = (0, 1, 0);
prev = Initial(up, (0, 0, 1));
prev.PlanarVelocity = prev.Velocity = (9999.995f, 0, 0);

// input、ground 均 default；dt = 0.02f
```

所有 Pre 成立；空中減速小到不改變此速度的 float 值。

| 版本 | 輸出 `PlanarVelocity.x` |
|---|---:|
| 原碼 | **9999.990234375** |
| T03 | **9999.9951171875** |

差異為 **0.0048828125 m/s**。兩者都仍被下一次 Step 接受，因此既有閉合測試無法區分；T03 違反的是明列的夾限 Post。

補充 NUnit 見證在原碼通過、T03 失敗。這證明 r12 尚未分類的 T03 為**非等價契約變異**。

**建議：**新增落在觸發帶內、仍低於 MaxStateSpeed 的固定見證，斷言明列的夾限結果。原碼無須因此修改。

### N27 · low · 實際 output 的合法覆寫缺少接受案例

位置：`Player/Runtime/PlayerCameraRig.cs:58–63、93`；`Player/Tests/PlayMode/PlayerCameraRigConfigureTests.cs:237–263`。

新增 N24 案例全部驗證危險 output 必須被拒絕，未區分以下合法拓撲：

```text
brain component 位於 target
brain.ControlledObject 指向獨立 GameObject
兩個 CinemachineCamera 也都獨立
target 與 pivot 互不為子孫
```

新 Pre 約束的是實際 writer `ControlledObject`；上述設定符合它。

AA03 在正確檢查後額外保留舊限制：

```csharp
RequireNotMovedBy(brain.ControlledObject.transform, target, pivot);
RequireNotMovedBy(brain.transform, target, pivot); // AA03
```

提交內測試仍為 **172/172 pass**，但此變異錯誤拒絕上述合法輸入。

補充見證：

| 版本 | Configure 結果 |
|---|---|
| 原碼 | **接受**；heading `(0,0,1)`、pivot `(0,641.6,0)`、world-up `(0,1,0)` |
| AA03 | **ContractException** |

補充 NUnit 接受案例在原碼通過、AA03 失敗。這是可直接區分的 Pre 過度限制，無須依賴完整 Cinemachine pose pipeline。

**建議：**新增合法 output 覆寫的接受案例，允許 brain component 位於受保護 Transform、實際 output 保持獨立。

## 變異結果

**126 項：114 killed、12 survived。最終全部成功編譯並執行；編譯失敗未計為 kill。**

| 集合 | killed | survived |
|---|---:|---:|
| 舊 47 項 | 46 | 1 |
| U01–U17 | 14 | 3 |
| V01–V10 | 8 | 2 |
| W01–W11 | 6 | 5 |
| X01–X05 | 5 | 0 |
| Y01–Y06 | 6 | 0 |
| Z01–Z22 | 22 | 0 |
| **r12 原 118 項** | **107** | **11** |
| **新增 AA01–AA08** | **7** | **1** |
| **全部合計** | **114** | **12** |

### 舊 118 項逐組結果

下表完整列出 killed／survived ID；各 ID 沿用 r12 語意。

| 集合 | killed ID | survived ID |
|---|---|---|
| R | R01、R02、R02b、R03–R06 | — |
| P | P01–P09、P11–P16 | — |
| B | B01–B07 | — |
| S | S01–S11 | — |
| T | T01、T02、T04–T07 | **T03** |
| U | U01–U06、U10–U17 | **U07、U08、U09** |
| V | V01–V08 | **V09、V10** |
| W | W01–W05、W11 | **W06–W10** |
| X | X01–X05 | — |
| Y | Y01–Y06 | — |
| Z | Z01–Z22 | — |

除 Z07、Z20、Z22 由 survived 改為 killed 外，舊項分類與 r12 相同。

### 新增 AA01–AA08

| ID／變異 | 結果 |
|---|---|
| AA01 改回檢查 `brain.transform` | **killed，3 fail** |
| AA02 僅在 output 等於 brain 自身物件時檢查 | **killed，3 fail** |
| AA03 額外檢查 `brain.transform` | **survived，172/172** |
| AA04 實際 output guard 移至 `_planet` 寫入後 | **killed，4 fail** |
| AA05 guard 移至 `_pivot` 寫入後 | **killed，4 fail** |
| AA06 guard 移至 first-person TrackingTarget 接線後 | **killed，4 fail** |
| AA07 僅首次 Configure 檢查實際 output | **killed，1 fail** |
| AA08 實際 output 僅比較物件相等，省略祖先關係 | **killed，2 fail** |

### 存活項解讀

| 項目 | 分類 |
|---|---|
| **T03** | 已由合法見證證明非等價；構成 **N26**。 |
| **AA03** | 已由合法接受案例證明非等價；構成 **N27**。 |
| U08、U09 | 等價：剩餘範圍比較仍拒絕 NaN／Infinity。 |
| U07、V09、V10、W06、W07 | 測試削弱操作。 |
| W08、W09 | 同時削弱測試後，對應產品變異重新漏過；不代表原提交測試漏過。 |
| W10 | 未區分單獨移除 `isfinite(offset)`；未證明 Unity 可達反例，列入非阻擋觀察。 |

## 非阻擋觀察

- **Q-T6 快照差異仍相同：**

  | 快照 | 提交期望值 | CLR 實測 |
  |---|---|---|
  | 預設 octave | `0xB7C5CA249FD8C22D` | `0x383CA51ADC74C561` |
  | 八 octave | `0xAF9DD87B0574B16F` | `0xB94911EBA33AB6D5` |

  兩份期望值均以目標提交原文核對；不列入否決理由。

- Configure 後重新指定 `ControlledObject` 已由新 Pre 明確交由呼叫者負責；未將後續危險重指向列為新發現。
- W10 尚缺 Unity 可達且可區分的非有限 Transform 見證；不據 managed stand-in 可儲存任意 float 推定引擎行為。
- 真實 Cinemachine 更新順序、blend、阻尼、非單位父階層 scale、攝影機畫面與 Unity 物理仍需完整引擎驗收；不影響本輪 verdict。
- 未執行 Unity PlayMode、Burst、IL2CPP、ARM64 或真實輸入裝置驗收。

## 重現資訊

1. 固定從 `cd6065e` 讀取產品來源、十五份測試與兩份快照；不 checkout。
2. 使用真實 Mathematics／NUnit，以 Roslyn C# 8、Release 編譯至 `MemoryStream`。
3. 反射展開 `[Test]`／`[TestCase]`，逐案例執行 SetUp／TearDown。
4. 先執行 188 案例完整基準，再於記憶體校準已知快照差異。
5. 每項變異從乾淨來源副本獨立編譯，執行 172 案例集合。
6. 於三種符號設定執行五種 Step 分佈、AddLook 與 pitch 掃描。
7. 於 Editor／Development 設定重跑 Configure＋r6 fixture，以及四種實際 output 別名探針。
8. 分別以速度夾限帶見證、合法 output 覆寫見證區分 T03／AA03；不混入提交內 killed 計數。

## 建議下一步

1. OPUS 補 N26：鎖住合法速度域內的夾限觸發帶。
2. OPUS 補 N27：鎖住符合新 Pre 的合法 output 覆寫。
3. SOL 重跑 T03、AA03 及 Configure／Step 回歸；完整 pipeline 驗收另行進行。

- 2026-11-08 · ChatGPT Sol（SOL，唯讀，對象 cd6065e）· done · P1 r13 fail（僅 low）：N24／N25 fixed；126 變異 114 killed／12 survived，新增 N26／N27 契約測試缺口，300 萬 Step 輸出無不變式或閉合違反 · 下一步：OPUS 補速度夾限帶與合法 output 覆寫測試；SOL 複審