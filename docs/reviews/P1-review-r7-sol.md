> 原始輸出：`codex exec -s read-only -m gpt-6.1-sol`，對象 `4537bf0`。未修改任何檔案。Claude Opus 已依 N10-R7/N12/N13/U17 修正（見 phase-1.md 交接）。

# P1 第七輪對抗式複審 · SOL

2026-11-07 · ChatGPT Sol（SOL）。總判定：**fail，剩餘項目皆為 low**。

**N8-R6 fixed；N11 fixed；接受 N10 的飽和契約變更，但 Post 的適用情況描述仍過窄。** 另確認 Configure 的 Post 反例，以及 Configure 檢查與負靈敏度下界的測試缺口。Q-T6 不列入否決理由。

對象：`p1/r5-fixes` 的 **`4537bf05434dcaf46383134069fbd9224812dcfd`**。

已讀指定規範、r6 報告與最新交接，並檢查 `git diff f45bdc0 4537bf0`。受審來源以 `git show 4537bf0:<path>` 取得；沒有修改檔案或建立暫存檔。結束時 tracked working tree 與 index diff 均為零。

以下 `Planet/`、`Player/` 相對於 `Assets/_Project/Features/`；行號對應受審 commit。

## 方法與限制

未啟動 Unity Editor。

來源、變異及補充探針均保存在記憶體，以 Roslyn 編譯至 `MemoryStream`，載入 assembly 後執行 NUnit 反射 runner。

實際環境：

- Windows x64、Windows PowerShell 5.1、.NET Framework CLR `4.0.30319.42000`。
- Roslyn 3.7、C# 8、Release 最佳化；主要測試定義 `UNITY_EDITOR`。
- 真實 Unity.Mathematics 1.3.3、NUnit 3.5。
- Mathematics DLL SHA-256：`48049AFDD1E1FBA73BFFAE24A6DAB5E8B32BC63E387EC8BCEA53CB2A5382D12E`。
- 相依 DLL 使用可用 facade／相容版本解析；不宣稱等同 Unity Mono、Burst 或 IL2CPP。

`PlayerCameraRig.cs` 整份參與編譯。UnityEngine／Cinemachine 使用最低限度 managed 型別介接；`PlanetTerrain.Configure` 擷取至建立 quadtree，`SphericalCharacter.Facing` 擷取原始 getter。未模擬 Unity 物理。

十三組 fixture 共 **150 案例**：

| 執行方式 | 結果 |
|---|---|
| 原始快照期望值 | **148 pass、2 個已知 Q-T6 mismatch** |
| 僅在記憶體校準兩個快照期望值 | **150/150 pass** |
| 加入兩個補充拒絕／狀態保留測試 | **152/152 pass** |
| `DEVELOPMENT_BUILD`：新 r6 fixture | **10/10 pass** |

僅在記憶體移除 `TestContext.WriteLine` 診斷輸出，保留斷言。

Q-T6 實測與 r6 相同：

| 快照 | 提交期望值 | 本輪實測 |
|---|---|---|
| 預設 octave | `B7C5CA249FD8C22D` | `383CA51ADC74C561` |
| 八 octave | `AF9DD87B0574B16F` | `B94911EBA33AB6D5` |

## r6 項目複審

| 項目 | 判定 | 證據與限制 |
|---|---|---|
| N8-R6 | **fixed** | Configure `:64`、AddLook `:114` 都在狀態寫入前呼叫 `RequireMultipliers`。有限性與兩側範圍足以阻止已知溢位見證。 |
| N10 | **接受契約變更；文字部分未修完** | 逐次飽和及後續反向輸入行為已明列、交接並測試；「只有 partial reversal 才不同」仍不準確，見 N10-R7。 |
| N11 | **fixed** | 新 fixture 直接檢查 PendingLook。T04、T05 現在分別被殺死。 |
| Step 邊界 clamp Post | **接受** | 已明列速度域邊界的縮放公式及約 `0.011 m/s` 縮減；該數字應理解為 r6 量測尺度，不能延伸為所有 transport 誤差的嚴格上界。 |
| R4 舊測試註解 | **fixed** | 已改為固定見證的回歸精度，不再誤述相對切向契約。 |

### 倍率與 PendingLook

合法設定下，每分量單次輸入的幅度上界約為：

```text
mouse ≤ 1e6 × 10 = 1e7°
gamepad ≤ 1.001 × 3600 × 1 = 3603.6°
```

既有 pending 已有界，因此加法不接近 float 溢位。

新增的中度超界案例有實際保護力：刪除倍率 Requires 時，`10.1` 與 `3636` 仍會產生有限結果，無法依靠 Ensures 偶然攔截；新測試會失敗。將 AddLook 檢查延後至 queue 寫入後，也被「pending 保持不變」斷言殺死。

### ±160° 宣告

對合法起始 pitch `p ∈ [−80,80]`：

```text
d ≥ 160  ⇒ p + d ≥ 80
d ≤ −160 ⇒ p + d ≤ −80
```

因此消費該 pitch delta 後，確實到達對應的 ±80°。

掃描起始 pitch 每 `0.01°` 一點，搭配 `±160°`、`±180°`、`±1e7°`，共 **96,006 案例、0 違反**。這項宣告成立。

### 額外契約掃描

| 探針 | 結果 |
|---|---|
| 100,000 次合法隨機倍率、pointer、stick、dt 與連續累加 | **0 有限性／pending 範圍違反** |
| 100,000 組合法 motor 輸入，各連續兩次 Step | **0 拒絕／所檢查不變式違反** |
| 上述探針改為無開發符號版本 | 同樣通過 |

Step 探針檢查速度界、有限性、相對切向界及 Facing；最大 planar 平方長度為 `9.968995E+07`。既有 20,000 次方向採樣閉合測試亦隨完整集合重跑通過。

這些掃描沒有構成普遍證明；本輪沒有找到新的 Step 數值反例。

## 新發現

### N10-R7 · low · 完整反轉也可能與精確累加不同

位置：`Player/Runtime/PlayerCameraRig.cs:101–103`。

「只有大於 180° swing 加上其 **partial reversal** 才不同」排除了完整反轉，但完整反轉也有差異。

使用合法 sensitivity `1°/px`，同一次消費前：

```csharp
rig.AddLook(new Vector2(0f, 300f), Vector2.zero, 0f);
rig.AddLook(new Vector2(0f, -300f), Vector2.zero, 0f);
```

實測，初始 orbit pitch 為 0：

| 行為 | pending pitch | 最終 orbit pitch |
|---|---:|---:|
| 逐次飽和 | −120° | −80° |
| 精確累加 | 0° | 0° |

**飽和算法本身符合新契約，可以接受。** 應將這句改為「飽和後，同一消費週期的後續反向輸入可能使結果不同於精確累加」，涵蓋完整反轉。

### N12 · low · Configure 與負靈敏度下界缺少獨立測試

位置：`Player/Tests/EditMode/PlayerCameraRigReviewR6Tests.cs:33–59`。

以下變異均通過完整 **150/150**：

| 變異 | 未被保護的承諾 |
|---|---|
| 刪除 Configure 的 `RequireMultipliers()` | Configure 拒絕非法序列化倍率 |
| 將 Configure 檢查移至 `_planet = planet` 後 | 拒絕前不改動狀態 |
| sensitivity 檢查由 `abs(s) ≤ 10` 改為 `s ≤ 10` | 拒絕有限負值下界超限 |

第三項沒有被 `-float.MaxValue` 案例殺死：極端乘法仍可能觸發其他 ContractException，掩蓋缺失的下界檢查。有限的 `−10.1` 才能隔離該條件。

記憶體補充測試確認：

- 原始程式拒絕非法 Configure，且 `_planet` 保持未初始化。
- 原始程式拒絕 sensitivity `−10.1`。
- 加入這兩項測試後，上述三個 mutant 全部被殺死。

目前實作正確；缺口在回歸保護。

### N13 · low · Configure 會消費合法預排輸入，違反自己的 heading Post

位置：`Player/Runtime/PlayerCameraRig.cs:54、83–86、182–183`。

AddLook 沒有「必須先 Configure」的 Pre；Configure 也沒有要求 pending 為零。但 Configure 建立初始 orbit 後立即呼叫 LateUpdate，消費先前排入的輸入。

合法見證：

```text
planet：R=640，center=0
target.position：(0,640,0)
target.forward：(0,0,1)
其餘參照有效，pivot 不屬於 target

AddLook(pointer=(750,0), stick=0, dt=0) // 預設 sensitivity：+90° yaw
Configure(...)
```

實測：

```text
Heading = (1, 0, 5.960465E-08)
dot(Heading, target.forward) = 5.960465E-08
PendingLook = (0,0)
```

Configure 的 Post 卻承諾 `orbit heading == target forward`。

這條路徑先前已存在，是本輪新增見證，並非本 commit 新引入。應明確規定 Configure 對既有 queue 的處理方式，並同步調整 Post 與測試。

## 變異結果

**64 項：58 killed、8 survived。**

- 依 r6 表列操作重建的 47 項：**46 killed、1 survived**。
- 新增 17 項：**12 killed、5 survived**。
- 主要集合為本輪實際列舉的 135 案例，排除 `CubeSphereTests`、`ChunkQuadtreeTests`。
- 八個存活項另以完整、記憶體校準後的集合確認，均 **150/150**。
- 64 項均成功編譯執行；未把編譯失敗計為 kill。
- r6 harness 未保存；本輪重建其語意操作，不宣稱 mutation 字串逐位相同。

### r6 的 47 項

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
| P09 pitch clamp→±89°，保留原常數 | killed |
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
| S06 移除 planar 平方長度上限 | killed |
| S07 移除 vertical 上限 | killed |
| S08 移除總 Velocity 平方長度上限 | killed |
| S09 移除 Facing Configure 檢查 | killed |
| S10 只移除 Heading Configure 檢查 | killed |
| S11 移除 stick 範圍檢查 | killed |
| T01 移除新 planar clamp | killed |
| T02 clamp factor 改為 1 | killed |
| T03 threshold 移除 `1−2e-6` 餘裕 | **survived** |
| T04 移除 AddLook yaw fmod | **killed：新 r6 fixture** |
| T05 移除 AddLook pitch clamp | **killed：新 r6 fixture** |
| T06 移除 pointer 上限 Requires | killed |
| T07 移除 deltaTime 範圍 Requires | killed |

T03 保留縮回域內的 factor；其存活仍不證明產品缺陷。

### 新增 17 項

| ID／變異 | 結果 |
|---|---|
| U01 刪除 sensitivity 整條 Requires | killed |
| U02 刪除 gamepad rate 整條 Requires | killed |
| U03 只刪除 Configure 倍率檢查 | **survived** |
| U04 只刪除 AddLook 倍率檢查 | killed |
| U05 AddLook 檢查移至 queue 寫入後 | killed |
| U06 Configure 檢查移至 `_planet` 寫入後 | **survived** |
| U07 移除 AssertInRange 的 yaw／pitch 範圍斷言 | **survived** |
| U08 只移除 sensitivity 的 `isfinite` | **survived** |
| U09 只移除 gamepad rate 的 `isfinite` | **survived** |
| U10 移除 gamepad rate 非負下界 | killed |
| U11 移除 gamepad rate 上界 | killed |
| U12 移除 sensitivity 幅度上界 | killed |
| U13 sensitivity 改為單側 `s ≤ Max` | **survived** |
| U14 pending pitch clamp ±180→±160 | killed |
| U15 sensitivity 合法端點 ≤→< | killed |
| U16 gamepad rate 合法端點 ≤→< | killed |
| U17 ViewOrbit 的兩個 pitch 常數一起 ±80→±89 | **survived** |

**計數更正：新增 17 項實際為 10 killed、7 survived；總計為 56 killed、8 survived。** 上方總數應以此為準。

存活項的解讀：

- **U03、U06、U13：實際測試缺口**，見 N12。
- **U07：測試削弱操作**。存活本身不是產品缺陷；fixture 仍保留明確的 120°／±180° 值斷言。
- **U08、U09：等價變異**。剩餘比較已拒絕 NaN 與無窮值，不需要為殺死它們增加測試。
- **U17：絕對 ±80° 規格缺少獨立保護**。既有測試跟隨被變異的常數；應補固定規格值的驗證。

## 重現資訊

本輪 harness、變異與補充探針僅存在記憶體，沒有輸出檔案。

重跑條件：

1. 從 `4537bf0` 讀取來源，以真實 Mathematics／NUnit、Roslyn Release 編譯至 MemoryStream。
2. 反射展開十三組 fixture 的 `[Test]` 與 `[TestCase]`，執行 SetUp／TearDown。
3. 僅在記憶體處理診斷輸出與兩個已知快照期望值。
4. 每個 mutant 從乾淨來源副本獨立編譯。
5. 分別執行 ±160° 掃描、合法 AddLook 累加、連續 Step，以及完整反轉與 Configure 預排輸入見證。
6. 以兩個補充測試確認 U03、U06、U13 可被獨立殺死。

未執行 PlayMode、Burst、IL2CPP、ARM64、真實輸入裝置或攝影機畫面驗收。

## 建議下一步

1. OPUS 修正 N10 Post 的「partial reversal」限制文字。
2. 補 Configure 倍率拒絕與寫入順序、有限負靈敏度超界，以及固定 ±80° 規格測試。
3. 明訂 Configure 對既有 PendingLook 的處理方式，補 N13 回歸測試。

