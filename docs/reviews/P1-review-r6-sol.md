> 原始輸出：`codex exec -s read-only -m gpt-6.1-sol`，對象 `p1/r5-fixes`（`f45bdc0`）。未修改任何檔案。Claude Opus 已依 N8-R6/N10/N11 修正（見 phase-1.md 交接）。

# P1 第六輪對抗式複審 · SOL

2026-11-07 · ChatGPT Sol（SOL）。總判定：**fail**。

**N6、N7、N9 fixed；N8 partially fixed。** Motor 的連續 Step 速度域掃描通過；相對切向契約可以接受。AddLook 仍未約束序列化靈敏度，合法參數可以造成 Post 失敗；新增 pitch clamp 也會改變多次呼叫的累加結果。另新增一項測試保護缺口。Q-T6 不列入否決理由。

對象：`p1/r5-fixes`，最終解析為 **`f45bdc049f621adbd6eacf4d261bdbbe323e162f`**。

開始審查時分支指向 `2386a79d8d923366296bb53ed364485372af9214`；期間新增 `f45bdc0`，兩者差異只有 `docs/roadmap/phase-1.md` 的交接紀錄，已確認程式碼相同。來源以 `git show` 取得，已檢查相對 main 的 diff，沒有修改 repo 或建立暫存檔。

以下 `Planet/`、`Player/` 相對於 `Assets/_Project/Features/`；行號對應受審程式碼。

## 方法與限制

未啟動 Unity Editor。

所有來源、變異與額外探針均保存在記憶體，以 Roslyn 編譯至 `MemoryStream`，再載入 assembly 執行 NUnit 反射 runner。

實際環境：

- Windows x64、Windows PowerShell 5.1、.NET Framework CLR `4.0.30319.42000`。
- Roslyn 3.7、C# 8、Release 最佳化；主要測試定義 `UNITY_EDITOR`。
- 真實 Unity.Mathematics 1.3.3 DLL、NUnit 3.5；沒有替換向量、quaternion 或數學運算。
- Mathematics DLL SHA-256：`48049AFDD1E1FBA73BFFAE24A6DAB5E8B32BC63E387EC8BCEA53CB2A5382D12E`。
- 相依 assembly 以可用 facade／相容 DLL 解析；不宣稱與 Unity Mono、Burst 或 IL2CPP 等價。

`PlayerCameraRig.cs` 整份參與編譯；UnityEngine／Cinemachine 僅提供最低限度的 managed 型別介接。`PlanetTerrain.Configure` 擷取至建立 quadtree 為止，`SphericalCharacter.Facing` 擷取原始 getter。這些結果不代表物理、Input System、攝影機生命週期或引擎接線驗收。

完整測試集為 **140 案例**：

| 執行方式 | 結果 |
|---|---|
| 原始快照期望值 | 138 pass、2 個已知 Q-T6 mismatch |
| 僅在記憶體校準兩個快照期望值 | **140/140 pass** |

Q-T6 實測仍為：

| 快照 | 提交期望值 | 本輪實測 |
|---|---|---|
| 預設 octave | `B7C5CA249FD8C22D` | `383CA51ADC74C561` |
| 八 octave | `AF9DD87B0574B16F` | `B94911EBA33AB6D5` |

三處 `TestContext.WriteLine` 因反射 runner 缺少 NUnit 輸出環境而拋出例外；只在記憶體移除這些診斷輸出後重跑，保留全部斷言。

## r5 項目複審

| 項目 | 判定 | 證據與限制 |
|---|---|---|
| N6 | **fixed** | motor `:301`–`:304` 的最終 clamp 封住已重現的平方長度超界；固定見證、新增測試及下列連續 Step 掃描通過。 |
| N7 | **fixed，接受契約變更** | 型別 Post 改為相對 `1e-4·max(1, |v|)`；Step Pre 使用已註明的較寬相對 `1e-3` slack，輸出可被下一步接受。新測試採相對界，呼叫端沒有依賴 planar 絕對 `1e-4` 的運算，交接已標明破壞性變更。 |
| N8 | **partially fixed** | 原本的巨大 deltaTime 見證現在被 Pre 拒絕；預設靈敏度下累加有界。但序列化靈敏度不受驗證，見 N8-R6。 |
| N9 | **fixed** | S06／S07／S08 現在分別被新測試殺死；planar、vertical、總 Velocity 上限已有獨立見證。 |

`SphericalMotorReviewR4Tests.cs:67` 仍把絕對 `1e-4` 稱為 MotorState Post。該固定見證保留較嚴斷言可以接受，但註解應改成「此見證的回歸精度」，避免誤述新契約。

### 100,000 組方向與連續 Step

使用 `Pcg32(77,11)`，把新 fixture 的 `RandomUnit` 採樣流程擴至 100,000 次；保留 `dot(from,to)>0.05` 與非退化 tangent 篩選。每種路徑有 **32,132 組**符合目前相對契約與速度界，其中 **5,748 組**也符合舊的絕對切向 `1e-4`。

每組執行連續兩次 Step，檢查速度域、有限性、相對切向界及 Facing 不變式。

| 路徑 | 下一步拒絕 | Post 違反 |
|---|---:|---:|
| 無輸入、空中自由滑行 | 0 | 0 |
| 非零輸入、極小空中加速度 | 0 | 0 |
| 平地、極小地面加速度 | 0 | 0 |
| 25° 坡、極小地面加速度 | 0 | 0 |
| 60° 陡坡限制分支 | 0 | 0 |
| 非零輸入、正常平地加速度 | 0 | 0 |
| 非零輸入、正常空中加速度 | 0 | 0 |
| 非零輸入、正常 25° 坡加速度 | 0 | 0 |
| 非零輸入、正常陡坡加速度 | 0 | 0 |
| 89.99° 可走坡、StickSpeed=100 | 0 | 0 |

本輪採樣流程明確取自新 fixture。r5 原始 sweep 未保留可直接重跑的 harness，因此**不宣稱逐筆重建 r5 的 8,764 組樣本**；r5 的 N6、N7 固定數值見證另由新測試重跑通過。

量測結果：

- 最大 planar 平方長度：`9.999984E+07`，低於 `1E8`。
- 最大絕對切向殘差：`0.00146484375`，符合新的相對界。
- 極端可走坡路徑最大總速度約 `10000.6533`，沒有超過 `2·MaxStateSpeed`。

grounded 路徑會把 `along` 恢復為 planar 長度，再加上 StickSpeed。即使考慮 ground normal 的單位長度容差，其三角不等式上界仍約 `10100.1`，距離 `20000` 有充分餘裕；未找到 grounded Velocity 超界反例。

### clamp 與長度保存承諾

clamp 會產生可量測的、刻意的速度縮減：

```text
up 不變；PlanarVelocity=(10000,0,0)
AirAcceleration=1e-20；無輸入、無地面

10000 → 9999.990234375
```

自由滑行掃描的一步最大損失為 `0.0107421875`，約 11 個該速度尺度的 float ULP。

這是速度域閉合的數值取捨，可以接受；但 `Step` Post 的「transport alone preserves |PlanarVelocity|」應明訂邊界 clamp 例外或容差。不能把此縮減描述成完全沒有改變長度。

## 新發現

### N8-R6 · low · 序列化靈敏度未納入可實現的 AddLook 契約

位置：`Player/Runtime/PlayerCameraRig.cs:22`–`:23`、`:108`–`:116`。

新 Pre 限制 pointer、stick、deltaTime，卻沒有驗證 `_sensitivityDegrees` 或 `_gamepadDegreesPerSecond`。Configure 也沒有建立這兩個欄位的有限性／幅度不變式。

反射設定序列化欄位後，以符合全部 AddLook 參數 Pre 的輸入實測：

| 設定與輸入 | Editor／Development 結果 |
|---|---|
| sensitivity=`float.MaxValue`，pointer=(1e6,0)，dt=0 | `Postcondition failed: pending look must stay finite` |
| sensitivity=`-float.MaxValue`，相同輸入 | 同上 |
| sensitivity=`NaN` | 同上 |
| gamepad rate=`float.MaxValue`，stick=(1.001,0)，dt=1 | 同上 |

`fmod` 無法把已溢位的 yaw 恢復為有限值。

另外編譯未定義 `UNITY_EDITOR`／`DEVELOPMENT_BUILD` 的版本，實測巨大 sensitivity 會把 NaN 寫入 `_pendingLook`。Editor 的 Ensures 則在賦值前拋出，舊 pending 狀態仍保留。

一般負靈敏度如 `-0.12` 可正常得到反向有限輸入；負號本身沒有構成數值反例。

建議明訂並驗證兩個序列化倍率的合法域，讓合法設定與合法參數足以保證 Post；僅在溢位後增加 Ensures 不足以履行承諾。

### N10 · low · 每次呼叫先 clamp pitch，會改變累加後的最終視角

位置：`Player/Runtime/PlayerCameraRig.cs:96`、`:112`–`:114`。

預設 sensitivity、同一次 LateUpdate 前：

```csharp
rig.AddLook(new Vector2(0f, 2500f), Vector2.zero, 0f);       // +300°
rig.AddLook(new Vector2(0f, -1666.6666f), Vector2.zero, 0f); // 約 −200°
```

實測：

| 行為 | 結果 |
|---|---:|
| 原本累加語意 | pending 約 +100°，初始 pitch=0 時最終 +80° |
| 新的逐次 clamp | pending 約 −20°，最終約 −20° |

兩次呼叫都符合新 Pre。第一筆超出的 pitch 在第二筆反向輸入抵銷前就被丟棄，因此「neither bound changes the resulting view」不成立。

目前 PlayerInputDriver 每個 Update 呼叫一次，限制了現有接線的觸發機會；公開 AddLook 的「accumulated until consumed」仍允許此案例。應保存所承諾的累加結果，或明確改變呼叫／累加契約並同步測試。

### N11 · low · 新增 pending 範圍沒有測試保護

位置：`Player/Tests/EditMode/SphericalMotorReviewR5Tests.cs:150`–`:154`。

測試只檢查 `DoesNotThrow`。移除 fmod 或 pitch clamp 後，數值仍然有限，因此完整 **140/140** 通過。

額外讀取 `_pendingLook` 的探針，以預設設定呼叫：

```csharp
rig.AddLook(new Vector2(1e6f, 1e6f), Vector2.zero, 0f);
```

結果：

| 版本 | pending yaw | pending pitch |
|---|---:|---:|
| 基線 | 120 | 180 |
| 移除 fmod | 120000 | 180 |
| 移除 pitch clamp | 120 | 120000 |

應直接斷言新增的 yaw／pitch 範圍，並加入 N8-R6 與 N10 見證。

### PlayerInputDriver 的 deltaTime 呼叫端

`PlayerInputDriver.cs:84` 仍直接傳入 `Time.deltaTime`，沒有處理新的 1 秒上限。

受審 `TimeManager.asset` 的 Maximum Allowed Timestep 為 `0.33333334`，因此沒有證據顯示現行專案的普通長幀會違反新 Pre，**不列為獨立否決項**。

若未來提高該設定，呼叫端仍可能傳入 `>1`；managed 探針確認 `1.01` 會被拒絕。新契約應與此設定限制一起記錄，不能僅以註解「stall, not input」視為呼叫端已完成適配。

## 變異結果

**47 項：44 killed、3 survived。**

- r5 原有 40 項：**40 killed、0 survived**。
- 新增 7 項：**4 killed、3 survived**。
- 每項先執行排除 `CubeSphereTests`、`ChunkQuadtreeTests` 的 120 案例集合；Q-T6 mismatch 不計 kill。
- 三個存活項另以記憶體校準後的完整 140 案例確認，均 **140/140**。
- 未把編譯失敗計為 kill；47 項均成功編譯執行。

| ID／變異 | 結果 |
|---|---|
| R01 移除遲滯上限 Requires | killed |
| R02 Configure 移除 Supports guard | killed |
| R02b guard 延後至 `_config` 賦值後 | killed |
| R03 local slack 2→1.5 | killed |
| R04 margin cap 16→32 | killed |
| R05 遲滯常數 1→1e6 | killed |
| R06 Supports 固定回傳 true | killed |
| P01 motor transport→identity | killed |
| P02 orbit 移除 transport | killed |
| P03 移除 planar 切向修正 | killed |
| P04 snap 判斷乘 2 | killed |
| P05 移除上升速度限制 | killed |
| P06 空中使用 GroundAcceleration | killed |
| P07 停用陡坡阻擋 | killed |
| P08 planar 改由 prev.Velocity 取得 | killed |
| P09 pitch clamp→±89° | killed |
| P11 朝向轉速乘 2 | killed |
| P12 jump sqrt 移除 2 | killed |
| P13 Initial 加入絕對長度門檻 | killed |
| P14 移除第二次正交化 | killed |
| P15 不檢查 pitch delta finite | killed |
| P16 ground.Distance 下限改為 −0.5 | killed |
| B01 TangentOr 移除尺度縮放 | killed |
| B02 移除 transport 長度恢復 | killed |
| B03 buffer 端點 ≤→< | killed |
| B04 移除 Walk≤Sprint | killed |
| B05 移除 prev 不變式檢查區塊 | killed |
| B06 不設定 JumpStarted | killed |
| B07 LevelRotation 改用 pitched Rotation | killed |
| S01 移除整個速度域 guard | killed |
| S02 移除 Grounded 組合檢查 | killed |
| S03 移除 orbit Heading invariant | killed |
| S04 移除 orbit pitch invariant | killed |
| S05 移除下落速度 clamp | killed |
| S06 移除 planar 平方長度上限 | **killed：N9 planar 測試** |
| S07 移除 vertical 上限 | **killed：N9 ±vertical 測試** |
| S08 移除總 Velocity 平方長度上限 | **killed：N9 總速度測試** |
| S09 移除 Facing Configure 檢查 | killed |
| S10 只移除 Heading Configure 檢查 | killed |
| S11 移除 stick 範圍檢查 | killed |
| T01 移除新 planar clamp | killed |
| T02 clamp factor 改為 1 | killed |
| T03 threshold 移除 `1−2e-6` 餘裕 | **survived，140/140** |
| T04 移除 AddLook yaw fmod | **survived，140/140** |
| T05 移除 AddLook pitch clamp | **survived，140/140** |
| T06 移除 pointer 上限 Requires | killed |
| T07 移除 deltaTime 範圍 Requires | killed |

T03 的存活本身不證明缺陷：保留縮回上限內的 factor，即使延後到超界才執行，也可能滿足公開速度契約。本輪沒有找到其閉合反例。

T04、T05 則確實違反新增 Post，且上列狀態探針可分別辨識；這兩項屬於測試缺口。

## 重現資訊

本輪 harness、來源變異與探針均只存在記憶體，沒有輸出可下載的專案或 log。

重跑條件：

1. 以受審 SHA 讀取來源，使用真實 Mathematics／NUnit、Roslyn Release 編譯至 MemoryStream。
2. 執行十二組 fixture 的 `[Test]` 與全部 `[TestCase]`；只在記憶體處理 NUnit 診斷輸出及已知快照校準。
3. 將新 fixture 的方向迴圈擴至 100,000，對每個接受狀態執行兩次 Step；另外切換表列輸入、加速度及地面路徑。
4. 每個 mutant 從乾淨來源副本獨立編譯，不能累積套用。
5. 以反射設定序列化倍率、讀取 `_pendingLook`，重跑 N8-R6／N10／N11 見證；另以不定義開發符號的版本確認 Ensures 剝除後行為。

未執行 PlayMode、Burst、IL2CPP、ARM64、真實輸入裝置或攝影機畫面驗收。

## 建議下一步

1. OPUS 為兩個序列化 look 倍率建立可驗證的合法域，補 Editor 與 Release 行為測試。
2. 修正多次 AddLook 的 pitch 累加語意，移除目前「不改變最終視角」的不實保證。
3. 新測試直接檢查 pending yaw／pitch 範圍，殺死 T04／T05。
4. 保留 motor 閉合修正與相對切向政策；釐清長度保存的邊界 clamp 例外，更新舊測試註解。

