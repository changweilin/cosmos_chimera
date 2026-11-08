> 原始輸出：`codex exec -s read-only -m gpt-6.1-sol`，對象 `c2a07f8`。未修改任何檔案。Claude Opus 已依 N24／N25 修正（見 phase-1.md 交接）。


# P1 第十二輪對抗式複審 · SOL

2026-11-08 · ChatGPT Sol（SOL）。

**N21、N22、N23 fixed；camera／brain ownership 驗證仍有缺口。**

新增 **N24（low）**：brain 實際寫入的 `ControlledObject` 可與 `brain.transform` 不同，現行 guard 未檢查它。另有 **N25（low）**：新增 Pre 的三項非等價變異仍能通過提交內測試。

r11 原 **96 項變異：85 killed／11 survived**；新增 **22 項：19 killed／3 survived**。未找到新的 Step 切向、速度域或兩步閉合反例。Q-T6 不列入否決理由。

對象：**`c2a07f8171a905f52677a735509561c7d73de609`**，分支 `p1/r5-fixes`。

已讀指定規約、r11 報告、最新交接及 `git diff 29c314a c2a07f8`。受審來源透過 `git show c2a07f8:<path>` 取得。未切換分支、修改 repo 或建立暫存檔；結束時 tracked diff 為空。

以下 `Planet/`、`Player/` 相對於 `Assets/_Project/Features/`；行號對應受審提交。

## 方法與限制

未啟動 Unity Editor。

來源、harness、變異與補充探針均保存在記憶體，以 Roslyn 編譯至 `MemoryStream`，載入 assembly 後執行 NUnit 反射 runner。

環境：

- Windows x64、Windows PowerShell、.NET Framework CLR。
- Roslyn **3.7.0.0**、C# 8、Release 最佳化。
- 真實 Unity.Mathematics、NUnit **3.5.0.0**。
- Mathematics DLL SHA-256：`48049AFDD1E1FBA73BFFAE24A6DAB5E8B32BC63E387EC8BCEA53CB2A5382D12E`，與 r11 相同。
- 相依 DLL 使用相容版本解析；不宣稱等同 Unity Mono、Burst 或 IL2CPP。

`PlayerCameraRig.cs` 整份參與編譯。UnityEngine／Cinemachine 使用 managed stand-ins；Transform 包含單位 scale 下的父子位置／旋轉運算。`PlanetTerrain.Configure` 擷取至建立 quadtree；未模擬索引緩衝配置。`SphericalCharacter.Facing` 保留原始 getter 與契約檢查。未模擬 Unity 物理。

本輪額外依鎖定的 **Cinemachine 3.1.7** 原始碼核對 `ControlledObject` 與 brain pose 寫入路徑；managed stand-in 加入其相同的 output 選擇語意。

十五份提交內測試來源，共 **181 案例**：

| 執行方式 | 結果 |
|---|---|
| 完整集合；兩個快照另以提交原文確認 | **179 pass、2 個已知 Q-T6 mismatch** |
| 記憶體校準快照後的變異確認集合 | **165/165 pass** |
| `UNITY_EDITOR` 完整集合中的 Configure＋r6 fixture | **38/38 pass** |
| `DEVELOPMENT_BUILD`：Configure＋r6 fixture | **38/38 pass** |
| 新增 N21 fixture | **2/2 pass**，包含 20,000 組推牆案例 |
| 補充 N23／ownership 邊界探針 | **5/5 符合契約** |
| 實際 brain output 的拒絕與原子性見證 | **0/4 pass** |
| 僅於記憶體補檢實際 brain output 後 | **4/4 pass** |

Configure fixture 通過不代表 Unity PlayMode 實跑。

快照讀取改為記憶體中的提交內容；移除 `TestContext.WriteLine` 診斷輸出。逐項變異使用 165 案例集合：排除 `CubeSphereTests`、`ChunkQuadtreeTests` 及百萬點 Warp 掃描；完整基準已包含這些測試。

Q-T6：

| 快照 | 提交期望值 | 本輪實測 |
|---|---|---|
| 預設 octave | `0xB7C5CA249FD8C22D` | `0x383CA51ADC74C561` |
| 八 octave | `0xAF9DD87B0574B16F` | `0xB94911EBA33AB6D5` |

## r11 項目複審

| 項目 | 判定 | 證據 |
|---|---|---|
| N21 陡坡相減後切向性 | **fixed** | `SphericalMotor.cs:302` 在阻擋相減後重新投影；固定見證與 20,000 組新測試通過。 |
| N21 下一次 Step 接受輸出 | **fixed** | 新 fixture 同時檢查切向 Post 與下一次 Step；三種符號設定的額外掃描均無拒絕。 |
| N22 target 本身／子孫路徑 | **fixed** | 新測試涵蓋兩條 target 路徑；Y04 現在被兩個案例殺死。 |
| N22 拒絕前不得寫入 | **fixed** | 第二次 Configure 改用 R=320，並比較既有欄位、接線及 pivot 位置；Y02、Y06 現在均被殺死。 |
| N23 target 位置域 | **fixed** | `PlayerCameraRig.cs:84–85` 在寫入前檢查每個絕對世界位置分量不超過 32768。 |
| N23 預設眼高邊界 | **通過** | 邊界合法；預設 1.6 m 眼高符合提交內 `4e-3 m` 容差。刪除 guard 或拒絕合法端點均被殺死。 |
| camera／brain 預設 output 所有權 | **fixed** | 三個 writer 的同物件／祖先檢查有效，且早於寫入。 |
| brain 可覆寫的實際 output | **未完整驗證** | `brain.ControlledObject.transform` 未檢查，見 N24。 |

### 新斷言的有效性

| 變異 | r11 | r12 |
|---|---|---|
| Y01 刪除整條 N20 guard | killed | **killed，5 個案例失敗** |
| Y02 guard 移至 `_planet` 寫入後 | survived | **killed，3 個 N22 案例失敗** |
| Y03 guard 弱化為只比較同一物件 | killed | **killed** |
| Y04 刪除 target 半邊檢查 | survived | **killed，2 個 target 案例失敗** |
| Y05 刪除 pivot 半邊檢查 | killed | **killed** |
| Y06 guard 移至 `_pivot` 寫入後 | survived | **killed，3 個 N22 案例失敗** |
| Z01 刪除新增陡坡投影 | — | **killed，2 個 N21 案例失敗** |
| Z04 刪除 N23 guard | — | **killed，3 個超界案例失敗** |
| Z09 刪除三個 ownership 呼叫 | — | **killed，3 個 ownership 案例失敗** |

Y02／Y06 的實際失敗值為 `_planet.Radius` 從預期 **640** 變成 **320**，證明新測試確實看得到提前寫入。

Z01 的固定見證重新出現：

```text
abs(dot(next.PlanarVelocity, next.Up)) = 0.00110923871
Post 上限                              = 0.0001
```

### 其他可達別名

`RequireNotMovedBy` 對傳入 writer 的方向正確：

```csharp
!target.IsChildOf(writer)
&& !pivot.IsChildOf(writer)
&& (_worldUp == null || !_worldUp.IsChildOf(writer))
```

`IsChildOf` 自反，因此同時拒絕 writer 與受保護 Transform 相同的情況。新建的 WorldUpReference 是獨立物件；首次 Configure 的 `_worldUp == null` 路徑未找到額外問題。

但 **brain component 所在 Transform 不一定是 brain 的實際 writer**。`ControlledObject` 覆寫仍可繞過驗證，詳見 N24。

### 邊界與額外契約掃描

分別以 `UNITY_EDITOR`、`DEVELOPMENT_BUILD`、無開發符號版本執行；下表為**每種設定**的結果：

| 分佈 | 輸出數 | 切向違反 | 速度域／有限性違反 | Step 拒絕 |
|---|---:|---:|---:|---:|
| 一般 transport，混合無接觸／平地／陡坡 | 200,000 | 0 | 0 | 0 |
| 空中減速，改量接近原速度，刻意放大相減消去 | 200,000 | 0 | 0 | 0 |
| 地面減速，同樣接近完全抵消 | 200,000 | 0 | 0 | 0 |
| 高速近乎全推牆 | 200,000 | 0 | 0 | 0 |
| 極小速度與平方長度下溢邊界 | 200,000 | 0 | 0 | 0 |
| **合計** | **1,000,000** | **0** | **0** | **0** |

三種設定合計 **3,000,000 個 Step 輸出**。

其他探針每種設定：

| 探針 | 結果 |
|---|---|
| 合法倍率、pointer、stick、dt 的連續 AddLook | **100,000 次；0 有限性／範圍違反** |
| 起始 pitch 每 `0.01°`，搭配 `±160°`、`±180°`、`±1e7°` | **96,006 案例；0 違反** |

`MoveTowards` 是陡坡分支之外另一處可能相減消去的位置。本輪刻意令減速量接近原速度，分別掃描 grounded／airborne 路徑。其合法單步加速度改量至多為 `1000 × 0.1 = 100 m/s`；未重現 N21 那種在近 1e4 m/s 抵消後留下非切向殘值的問題。

以上是指定分佈的實測結果，不是所有浮點輸入的形式證明。

## 新發現

### N24 · low · ownership guard 檢查 brain component，漏掉可覆寫的實際 output

位置：`Player/Runtime/PlayerCameraRig.cs:88–90、194–198`。

現行檢查：

```csharp
RequireNotMovedBy(brain.transform, target, pivot);
```

但鎖定的 Cinemachine 3.1.7：

- `CinemachineBrain.cs:438–447`：`ControlledObject` 可指定另一個 GameObject；預設才是 brain 所在物件。
- `CinemachineBrain.cs:727–738`：`PushStateToUnityCamera` 寫入的是 **`ControlledObject.transform`**。

因此以下設定在 Editor／Development 都被接受：

```csharp
brain.ControlledObject = pivot.gameObject;
rig.Configure(planet, target, pivot, third, first, brain);
```

brain component 與其他參照可以全部保持獨立；現行 guard 看不到 output 已指向 pivot。target、WorldUpReference 或其祖先也有相同缺口。

以 pitch 30°、距離 4 m 的 pose 寫回見證：

| `ControlledObject` 別名 | Configure | pose 寫回後 |
|---|---|---|
| target | 接受 | body 位移 **3.487122 m** |
| pivot | 接受 | 眼高位置誤差 **4 m** |
| WorldUpReference | 接受 | `owned.up · localUp = 0.8660254` |

四個「應拒絕且無寫入」的 NUnit 見證，涵蓋上述三種及 target 的祖先，原碼為 **0/4 pass**。僅在記憶體中額外檢查 `brain.ControlledObject.transform` 後為 **4/4 pass**，包括拒絕前欄位、接線與 pivot 位置未變。

**契約分類：**若文件所稱「Cinemachine 寫入的 Transform」按實際 writer 解讀，這些拓撲已違反語義 Pre；缺陷是開發版沒有實作該 Pre 的完整檢查。因此列為 **low 的 Pre enforcement 缺口**，不宣稱它是所有語義 Pre 成立時的 Configure 同步 Post 反例。

**驗證限制：**數值為來源推導及 managed pose 見證，未執行完整 Cinemachine pipeline。

應在任何寫入前檢查實際 `ControlledObject.transform`，並在契約中明列 output 覆寫的所有權條件。

### N25 · low · 新增 Pre 的三項非等價產品變異仍無測試保護

位置：`Player/Tests/PlayMode/PlayerCameraRigConfigureTests.cs:187–227`。

以下變異均 **165/165 pass**：

| 變異 | 提交內測試缺口 | 獨立探針結果 |
|---|---|---|
| Z07 位置上限只檢查 Y | 超界案例全部只改 Y | X／Z 超界被接受。 |
| Z20 ownership 對 pivot 只比較物件相等 | 有 camera == pivot，缺 camera 為 pivot 祖先 | camera 為 pivot 祖先時被接受。 |
| Z22 用 `position − Center` 代替世界位置檢查 | Configure 案例未區分非零 Center | 同時可拒絕合法輸入、接受超界輸入。 |

Z22 固定例：

```text
Center = (16000, 0, 0)

target = (-20000, 0, 0)
世界位置合法；offset = (-36000, 0, 0)
變異錯誤拒絕。

target = (48000, 0, 0)
世界位置超界；offset = (32000, 0, 0)
變異錯誤接受。
```

原始產品碼在五個補充邊界案例均正確；三個存活產品變異均可被補充探針區分。這是測試保護缺口，不是原碼已重現相同錯誤。

## 變異結果

**118 項：104 killed、14 survived。全部有效變異均成功編譯並執行；未將編譯失敗計為 kill。**

| 集合 | killed | survived |
|---|---:|---:|
| 舊 47 項 | 46 | 1 |
| U01–U17 | 14 | 3 |
| V01–V10 | 8 | 2 |
| W01–W11 | 6 | 5 |
| X01–X05 | 5 | 0 |
| Y01–Y06 | 6 | 0 |
| **r11 原 96 項** | **85** | **11** |
| **新增 Z01–Z22** | **19** | **3** |
| **全部合計** | **104** | **14** |

每項從乾淨來源副本獨立編譯，執行 165 案例確認集合。舊 harness 未保存；依 r11 表列語意重建，不宣稱替換字串逐位相同。

### 舊 47 項

| ID／變異 | 結果 |
|---|---|
| R01 移除遲滯上界檢查 | killed |
| R02 Terrain Configure 移除 Supports guard | killed |
| R02b Supports guard 延後至 `_config` 寫入後 | killed |
| R03 local slack 2→1.5 | killed |
| R04 margin cap 16→32 | killed |
| R05 遲滯常數 1→1e6 | killed |
| R06 Supports 固定回傳 true | killed |
| P01 motor transport→identity | killed |
| P02 orbit 移除 transport | killed |
| P03 移除原有 planar 切向修正 | killed |
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
| B07 LevelRotation 改回傳 pitched Rotation | killed |
| S01 移除整個速度域 guard | killed |
| S02 移除 Grounded 組合檢查 | killed |
| S03 移除 orbit Heading invariant | killed |
| S04 移除 orbit pitch invariant | killed |
| S05 移除下落速度 clamp | killed |
| S06 移除 planar 平方長度上限 | killed |
| S07 移除 vertical 上限 | killed |
| S08 移除總 Velocity 平方長度上限 | killed |
| S09 移除 Facing Configure 檢查 | killed |
| S10 移除 Heading Configure 檢查 | killed |
| S11 移除 stick 範圍檢查 | killed |
| T01 移除 planar clamp | killed |
| T02 clamp factor 改為 1 | killed |
| T03 threshold 移除 `1−2e-6` 餘裕 | **survived** |
| T04 移除 AddLook yaw fmod | killed |
| T05 移除 AddLook pitch clamp | killed |
| T06 移除 pointer 上限 Requires | killed |
| T07 移除 deltaTime 範圍 Requires | killed |

### U01–U17 重跑

| ID／變異 | 結果 |
|---|---|
| U01 刪除 sensitivity 整條 Requires | killed |
| U02 刪除 gamepad rate 整條 Requires | killed |
| U03 只刪除 Configure 倍率檢查 | killed |
| U04 只刪除 AddLook 倍率檢查 | killed |
| U05 AddLook 檢查移至 queue 寫入後 | killed |
| U06 Configure 檢查移至 `_planet` 寫入後 | killed |
| U07 移除 AssertInRange 的 yaw／pitch 範圍斷言 | **survived** |
| U08 只刪除 sensitivity 的 `isfinite` | **survived** |
| U09 只刪除 gamepad rate 的 `isfinite` | **survived** |
| U10 移除 gamepad rate 非負下界 | killed |
| U11 移除 gamepad rate 上界 | killed |
| U12 移除 sensitivity 幅度上界 | killed |
| U13 sensitivity 改為單側 `s ≤ Max` | killed |
| U14 pending pitch clamp ±180→±160 | killed |
| U15 sensitivity 合法端點 ≤→< | killed |
| U16 gamepad rate 合法端點 ≤→< | killed |
| U17 兩個 pitch 常數一起 ±80→±89 | killed |

### V01–V10 重跑

| ID／變異 | 結果 |
|---|---|
| V01 移除 Configure 的 queue 清零 | killed |
| V02 Configure 只清 yaw、保留 pitch | killed |
| V03 Configure 只清 pitch、保留 yaw | killed |
| V04 queue 清零移至 LateUpdate 後 | killed |
| V05 Configure 倍率檢查移至 `_target` 寫入後 | killed |
| V06 Configure 倍率檢查移至 TrackingTarget 接線後 | killed |
| V07 Configure 僅保留 gamepad Requires | killed |
| V08 Configure 僅保留 sensitivity Requires | killed |
| V09 只移除 PendingLook yaw 範圍斷言 | **survived** |
| V10 只移除 PendingLook pitch 範圍斷言 | **survived** |

### W01–W11 重跑

| ID／變異 | 結果 |
|---|---|
| W01 刪除 camera 互異 Requires | killed |
| W02 刪除 target 位置域 Requires | killed |
| W03 camera 互異檢查移至 `_planet` 寫入後 | killed |
| W04 target 位置域檢查移至 `_planet` 寫入後 | killed |
| W05 移除 Move 單位圓限幅 | killed |
| W06 移除新增私有欄位斷言 | **survived** |
| W07 移除新增 orbit pitch 零值斷言 | **survived** |
| W08 W06＋U06 | **survived** |
| W09 W07＋V02 | **survived** |
| W10 位置域只移除 `isfinite(offset)` 子句 | **survived** |
| W11 位置域門檻 `1e-3→0` | killed |

### X01–X05 重跑

| ID／變異 | 結果 |
|---|---|
| X01 刪除 `!target.IsChildOf(pivot)` Requires | killed |
| X02 新階層檢查移至 `_planet` 寫入後 | killed |
| X03 位置域門檻 `1e-3→5e-4` | killed |
| X04 位置域嚴格門檻 `>→>=` | killed |
| X05 階層檢查弱化為僅 `target != pivot` | killed |

### Y01–Y06 重跑

| ID／變異 | 結果 |
|---|---|
| Y01 刪除整條 N20 guard | killed |
| Y02 N20 guard 移至 `_planet` 寫入後 | **killed，r11 為 survived** |
| Y03 N20 guard 弱化為僅比較物件相等 | killed |
| Y04 只刪除 target 半邊檢查 | **killed，r11 為 survived** |
| Y05 只刪除 pivot 半邊檢查 | killed |
| Y06 N20 guard 移至 `_pivot` 寫入後 | **killed，r11 為 survived** |

### 本輪新增 Z01–Z22

| ID／變異 | 結果 |
|---|---|
| Z01 刪除陡坡相減後的新投影 | killed |
| Z02 新投影減號改加號 | killed |
| Z03 阻擋條件改為 `into < −10000` | killed |
| Z04 刪除 N23 上限 Requires | killed |
| Z05 上限檢查放寬為 1e8 | killed |
| Z06 上限合法端點 ≤→< | killed |
| Z07 上限只檢查 Y 分量 | **survived：165/165** |
| Z08 N23 guard 移至 `_planet` 寫入後 | killed |
| Z09 刪除三個 ownership 呼叫 | killed |
| Z10 只刪除 thirdPerson ownership 呼叫 | killed |
| Z11 只刪除 firstPerson ownership 呼叫 | killed |
| Z12 只刪除 brain ownership 呼叫 | killed |
| Z13 ownership 刪除 target 分支 | killed |
| Z14 ownership 刪除 pivot 分支 | killed |
| Z15 ownership 刪除 WorldUpReference 分支 | killed |
| Z16 三個 ownership 關係均弱化為只比較相等 | killed |
| Z17 ownership 呼叫移至 `_planet` 寫入後 | killed |
| Z18 WorldUpReference 分支弱化為只比較相等 | killed |
| Z19 target 分支弱化為只比較相等 | killed |
| Z20 pivot 分支弱化為只比較相等 | **survived：165/165** |
| Z21 位置上限移除絕對值 | killed |
| Z22 位置上限改檢查 offset | **survived：165/165** |

存活項解讀：

- **T03**：仍保留縮回速度域的 factor；未證明等價。
- **U08、U09**：剩餘範圍比較仍拒絕 NaN／Infinity，屬等價變異。
- **U07、V09、V10、W06、W07**：測試削弱操作。
- **W08、W09**：移除新增斷言後，對應漏洞重新漏過。
- **W10**：集合未區分該防禦子句；不據此宣稱 Unity 可達非有限 Transform。
- **Z07、Z20、Z22**：非等價產品變異，構成 N25；補充探針均可區分，但未混入提交內測試的 killed 統計。

## 重現資訊

本輪來源副本、harness、變異及探針均僅存在記憶體。

重跑條件：

1. 固定從 `c2a07f8` 讀取來源與快照，對照 `git diff 29c314a c2a07f8`。
2. 使用真實 Mathematics／NUnit，以 Roslyn C# 8、Release 編譯至 `MemoryStream`。
3. 反射展開 `[Test]`／`[TestCase]`，逐案例執行 SetUp／TearDown。
4. 納入完整 Configure fixture、N21 fixture 及含父子姿態運算的 managed stand-ins。
5. 每項變異從乾淨來源獨立編譯，執行 165 案例確認集合。
6. 於三種符號設定執行五種分佈的兩步 Step、AddLook 及 pitch 掃描。
7. 核對 Cinemachine 3.1.7 的 `ControlledObject` getter／setter 及 brain pose 寫入路徑。
8. 分別把實際 output 指向 target、pivot、WorldUpReference 與 target 祖先，驗證拒絕及原子性。
9. 以 X／Z 超界、非零 Center、camera 為 pivot 祖先區分三項新增存活變異。

未執行 Unity PlayMode、完整 Cinemachine pipeline、Burst、IL2CPP、ARM64、真實輸入裝置或攝影機畫面驗收。

## 建議下一步

1. OPUS 補 N24：在任何寫入前檢查 `brain.ControlledObject.transform`，明列其所有權 Pre，加入拒絕及重複 Configure 原子性測試。
2. 補 N25：X／Z 超界、非零 Center 的合法／非法位置，以及 camera 為 pivot 祖先的案例。
3. 以 Unity PlayMode 驗證實際 brain output 的 pipeline 行為。
4. SOL 複審上述修正；Q-T6 繼續獨立處理。

- 2026-11-08 · ChatGPT Sol（SOL） · done · P1 r12 fail（最高 low）：N21–N23 fixed；r11 的 96 變異 85 killed／11 survived，新增 22 項 19 killed／3 survived；新增 N24 brain ControlledObject 驗證缺口、N25 新 Pre 測試缺口；三種符號設定合計 300 萬個 Step 輸出無新閉合違反 · 下一步：OPUS 補實際 brain output guard 與邊界測試，SOL 複審