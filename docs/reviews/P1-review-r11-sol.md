> 原始輸出：`codex exec -s read-only -m gpt-6.1-sol`，對象 `29c314a`。未修改任何檔案。Claude Opus 已依 N21–N23 與 camera／brain 別名修正（見 phase-1.md 交接）。


# P1 第十一輪對抗式複審 · SOL

2026-11-07 · ChatGPT Sol（SOL）。

**N20 實作 fixed；新增測試保護不足。** r10 原 90 項變異重跑為 **79 killed／11 survived**；新增 6 項 N20 變異為 **3 killed／3 survived**。

新增 **N21（med）**：陡坡阻擋的浮點相減可破壞切向 Post，讓下一次 Step 拒絕前一次輸出。另有 **N22（low）**：N20 測試漏掉 target 路徑與提前欄位寫入；**N23（low）**：Configure 的合法位置域允許眼高完全消失。Q-T6 不列入否決理由。

對象：**`29c314a1bf1dfbf9c08f1665cfb4489487ee1f73`**，分支 `p1/r5-fixes`。

已讀指定規約、r10 報告、最新交接及 `git diff ebe2693 29c314a`。受審來源固定透過 `git show 29c314a:<path>` 取得。未切換分支、修改 repo 或建立暫存檔。

以下 `Planet/`、`Player/` 相對於 `Assets/_Project/Features/`；行號對應受審提交。

## 方法與限制

未啟動 Unity Editor。

來源、harness、變異及補充探針均保存在記憶體，以 Roslyn 編譯至 `MemoryStream`，載入 assembly 後執行 NUnit 反射 runner。

環境：

- Windows x64、Windows PowerShell、.NET Framework CLR。
- Roslyn **3.7.0.0**、C# 8、Release 最佳化。
- 真實 Unity.Mathematics、NUnit **3.5.0.0**。
- Mathematics DLL SHA-256：`48049AFDD1E1FBA73BFFAE24A6DAB5E8B32BC63E387EC8BCEA53CB2A5382D12E`，與 r10 相同。
- 相依 DLL 使用相容版本解析；不宣稱等同 Unity Mono、Burst 或 IL2CPP。

`PlayerCameraRig.cs` 整份參與編譯。UnityEngine／Cinemachine 使用最低限度 managed stand-ins；Transform 包含單位 scale 下的父子位置／旋轉運算。`PlanetTerrain.Configure` 擷取至建立 quadtree；索引緩衝配置未模擬。`SphericalCharacter.Facing` 保留原始 getter 與契約檢查。未模擬 Unity 物理。

十四份提交內測試來源，共 **169 案例**：

| 執行方式 | 結果 |
|---|---|
| 完整集合，原始快照期望值 | **167 pass、2 個已知 Q-T6 mismatch** |
| 僅於記憶體校準後，重跑兩個快照案例 | **2/2 pass** |
| 校準後的存活項確認集合 | **153/153 pass** |
| `UNITY_EDITOR`：Configure 與 r6 fixture | **28/28 pass** |
| `DEVELOPMENT_BUILD`：Configure 與 r6 fixture | **28/28 pass** |
| N20 補充拒絕／原子性探針 | **4/4 符合契約** |
| 新增固定 NUnit 見證：切向 Post、Step 閉合、眼高 | 兩種開發符號設定均 **0 pass／3 fail** |

Configure fixture 通過不代表 Unity PlayMode 實跑。

快照讀取改為記憶體中的提交內容；另移除 `TestContext.WriteLine` 診斷輸出。逐項變異排除耗時且無關的百萬點 Warp 掃描；該掃描已在完整基準通過。

Q-T6：

| 快照 | 提交期望值 | 本輪實測 |
|---|---|---|
| 預設 octave | `B7C5CA249FD8C22D` | `383CA51ADC74C561` |
| 八 octave | `AF9DD87B0574B16F` | `B94911EBA33AB6D5` |

## r10 項目複審

| 項目 | 判定 | 證據 |
|---|---|---|
| N20 guard 範圍 | **fixed** | `PlayerCameraRig.cs:75–76` 同時檢查 `pivot.IsChildOf(_worldUp)` 與 `target.IsChildOf(_worldUp)`；包含自反情況。 |
| N20 檢查順序 | **fixed** | guard 早於首次欄位寫入 `_planet = planet`（`:84`）、camera 接線與 Transform 寫入。 |
| owned 作 pivot | **有效** | 新增 fixture 拒絕此輸入；刪除 guard 後案例失敗。 |
| owned 子孫作 pivot | **有效** | 將 guard 弱化為只拒絕同一物件時，此案例失敗。 |
| owned／其子孫作 target | **實作正確，測試缺漏** | 補充探針均拒絕且無寫入；刪除 target 半邊檢查，提交內測試仍通過。 |
| 拒絕前未寫入 | **實作正確，測試缺漏** | 補充探針確認無寫入；延後 guard 的變異仍存活。 |
| 獨立 Transform 重複 Configure | **通過** | 提交內冪等案例及補充重綁探針均通過。 |

### 新斷言的有效性

| 變異 | 提交內測試 | 補充探針觀察 |
|---|---|---|
| 刪除整條 N20 guard | **killed** | 四種非法別名均被接受並改動狀態。 |
| guard 移至 `_planet` 寫入後 | **survived** | 四種拒絕路徑均已改寫 `_planet`。 |
| 只拒絕 `target/pivot == _worldUp` | **killed** | 子孫路徑被接受。 |
| 刪除 target 半邊檢查 | **survived** | owned 與其子孫作 target 均被接受。 |
| 刪除 pivot 半邊檢查 | **killed** | 兩個既有拒絕案例失敗。 |
| guard 移至 `_pivot` 寫入後 | **survived** | 拒絕前已改寫 `_planet` 及 `_target`／`_pivot`。 |

補充原子性探針在第二次 Configure 使用不同 planet，並保留預排 look；比較 rig 欄位、camera targets 與 Transform pose。正確來源四種路徑均無寫入。

### 其他可達別名

就 **Configure／本 rig 的 LateUpdate 剛返回的同步狀態**而言，未找到另一個由 rig 自身寫入造成的別名反例：

- target 不得位於 pivot 或 owned transform 之下。
- pivot 不得位於 owned transform 之下。
- owned transform 位於 target／pivot 之下時，最後對 owned 的寫入不會移動祖先；補充探針通過。
- camera／brain 參照在此方法內只接線或改 priority，沒有額外的 camera pose 寫入。

但**完整 Cinemachine 更新期間仍不能宣稱別名安全**。呼叫端可在公開取得的 `brain.WorldUpOverride.gameObject` 上加入 camera 或 brain，再用於重複 Configure；目前 guard 接受。

依目標鎖定的 Cinemachine 3.1.7 原始碼：

- `CinemachineThirdPersonFollow.PositionCamera` 產出帶 pivot pitch 的 raw orientation。
- `CinemachineCamera.InternalUpdateCameraState` 將 raw pose 寫回自己的 Transform。
- `CinemachineBrain.PushStateToUnityCamera` 將最終 pose 寫到 controlled object。

以零 damping／肩位移／手臂長、距離 4 m、無碰撞的來源推導 pose，managed 見證結果為：

| camera／output Transform 別名 | 正常 pose 寫入後 |
|---|---|
| owned transform | pitch 30° 時 `owned.up · localUp = 0.8660254` |
| pivot | 眼高位置誤差 **4 m** |
| target | 角色被移動約 **3.487 m** |

**這是來源推導及 managed pose 見證，未執行完整 Cinemachine pipeline。** 它不構成本 rig 返回瞬間的同步反例；若 Post 要保證一般幀末狀態，仍需明定 camera／brain output 的 Transform 所有權與階層限制，並以 PlayMode 驗證。

### 邊界與額外契約掃描

分別以 `UNITY_EDITOR`、`DEVELOPMENT_BUILD`、無開發符號版本執行：

| 探針 | 每種設定結果 |
|---|---|
| 100,000 組合法 motor 輸入，各兩次 Step，包含平地、陡坡及無接觸 | **200,000 個輸出；2 次切向 Post 違反** |
| 合法倍率、pointer、stick、dt 的連續 AddLook | **100,000 次；0 有限性／範圍違反** |
| 起始 pitch 每 `0.01°`，搭配 `±160°`、`±180°`、`±1e7°` | **96,006 案例；0 違反** |

針對「高速幾乎全部被陡坡阻擋」另掃描 100,000 組候選方向，實際執行 **82,195 組陡坡案例**：

- **42,271** 個輸出違反相對切向 Post。
- **135** 個輸出被下一次 Step 的切向 Pre 拒絕。

後者是刻意放大相減消去的對抗分佈，不代表一般遊戲輸入的發生率。

## 新發現

### N21 · med · 陡坡阻擋相減破壞切向 Post，Step 再次不對自己的輸出閉合

位置：`Player/Runtime/Model/SphericalMotor.cs:291–308`。

transport 後雖做過切向修正，陡坡處理仍會再執行：

```csharp
vTan -= wallOut * into;
```

當兩個大向量幾乎相等，剩餘量可主要由浮點誤差構成。後續只有速度上限 clamp，沒有恢復切向性。

固定見證：

```csharp
var settings = MotorSettings.Default;
settings.AirAcceleration = 1e-20f;

float3 up = math.normalize(new float3(1f, 2f, 3f));
float3 normal = math.normalize(new float3(-1f, -3f, 3f));
float3 wall = SphericalMotor.TangentOr(normal, up, float3.zero);

MotorState prev = SphericalMotor.Initial(up, wall);
prev.PlanarVelocity = prev.Velocity = -9999f * wall;

var ground = new GroundContact
{
    Hit = true,
    Normal = normal,
    Distance = 0f,
};

MotorState next =
    SphericalMotor.Step(settings, prev, default, ground, up, 0.02f);

// Editor／Development：拒絕前一次 Step 的輸出。
SphericalMotor.Step(settings, next, default, ground, up, 0.02f);
```

所有輸入滿足明列 Pre；地面法線與 up 的 dot 約 `0.122627884`，屬合法陡坡。

| 觀察值 | 實測 |
|---|---|
| `next.PlanarVelocity` | `(-0.000244140625, -0.00048828125, -0.0009765625)` |
| planar 長度 | `0.00111879292 m/s` |
| `dot(next.PlanarVelocity, next.Up)` | **`-0.00110923871 m/s`** |
| Post 容許誤差 | **`0.0001 m/s`** |
| 下一次 Step 的 Pre 容許誤差 | **`0.001 m/s`** |
| 下一次 Step | **`ContractException: prev.PlanarVelocity must be a finite tangent`** |

兩個獨立 NUnit 見證在 Editor／Development 均失敗：

- 最終 planar 滿足相對切向 Post。
- 下一次 Step 接受前一次輸出。

Release 的切向 Post 仍失敗；下一次呼叫因契約檢查剝除而不丟例外。

這是既存缺陷，非 N20 修正引入。應在所有會修改 planar 的分支完成後維持切向不變式，並補上相減消去與連續 Step 回歸。

### N22 · low · N20 測試未保護完整拒絕域與「任何寫入之前」

位置：`Player/Tests/PlayMode/PlayerCameraRigConfigureTests.cs:119–146`。

兩個拒絕案例只走 pivot 路徑；重複 Configure 使用相同 `_config`，斷言也沒有比較 rig 欄位。

因此以下產品變異均 **153/153 pass**：

1. N20 guard 移至 `_planet = planet` 之後。
2. 刪除 `!target.IsChildOf(_worldUp)`。
3. N20 guard 移至 `_pivot = pivot` 之後。

這些不是等價變異。補充探針分別觀察到提前寫入，或接受契約禁止的 target。

應新增 target 本身／子孫拒絕案例；第二次 Configure 使用不同 planet，並比較拒絕前後的既有欄位與接線。

### N23 · low · Configure 的合法位置域允許眼高完全被 float 捨入吃掉

位置：`Player/Runtime/PlayerCameraRig.cs:53–59、78–80、205`。

Configure 對 target 位置只有有限性及球心排除域限制，沒有距離或精度上界。以下輸入符合 Pre：

```csharp
planet = new PlanetConfig(640f, float3.zero);
target.position = new float3(0f, 1e8f, 0f);
// target、pivot 獨立；其餘參照與倍率合法。
rig.Configure(planet, target, pivot, third, first, brain);
```

在此座標，float 間距為 8 m；預設眼高 `1.6f` 在加法中消失。

| 觀察值 | 實測 |
|---|---|
| target.position | `(0,100000000,0)` |
| Configure 返回時 pivot.position | `(0,100000000,0)` |
| 實際眼高 | **0 m** |
| 宣告眼高 | **1.6 m** |

固定 NUnit 眼高斷言在三種符號設定均失敗。此反例只依賴 float 加法與獨立 Transform，不涉及物理或 Cinemachine 更新。

應明定並檢查可履行眼高 Post 的位置／精度域，或明確修訂 Post 的精度承諾。

## 變異結果

**96 項：82 killed、14 survived；全部成功編譯並執行。未將編譯失敗計為 kill。**

| 集合 | killed | survived |
|---|---:|---:|
| 舊 47 項 | 46 | 1 |
| U01–U17 | 14 | 3 |
| V01–V10 | 8 | 2 |
| W01–W11 | 6 | 5 |
| X01–X05 | 5 | 0 |
| **r10 原 90 項** | **79** | **11** |
| 指定新增：Y01–Y03 | 2 | 1 |
| **指定 93 項合計** | **81** | **12** |
| 額外新增：Y04–Y06 | 1 | 2 |
| **全部合計** | **82** | **14** |

每項從乾淨來源副本獨立編譯。Player 集合為 **92 案例**；R 組先跑 **76 個 Planet 案例**。存活項再確認 **153 案例**集合：排除 `CubeSphereTests`、`ChunkQuadtreeTests` 及百萬點 Warp 掃描。

舊 harness 未保存；本輪依 r10 表列語意重建，不宣稱替換字串逐位相同。

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
| X05 新階層檢查弱化為僅 `target != pivot` | killed |

### 本輪新增 Y01–Y06

新檢查的範圍弱化，以「只拒絕同一物件，允許其子孫」實作；既有位置數值門檻變異亦已重跑。

| ID／變異 | 結果 |
|---|---|
| Y01 刪除整條 N20 guard | **killed：兩個 pivot 拒絕案例** |
| Y02 N20 guard 移至 `_planet` 寫入後 | **survived：153/153** |
| Y03 N20 guard 弱化為僅比較物件相等 | **killed：pivot 子孫案例** |
| Y04 只刪除 target 半邊檢查 | **survived：153/153** |
| Y05 只刪除 pivot 半邊檢查 | **killed：兩個 pivot 拒絕案例** |
| Y06 N20 guard 移至 `_pivot` 寫入後 | **survived：153/153** |

存活項解讀：

- **T03**：仍保留縮回速度域的 factor；未證明等價。
- **U08、U09**：剩餘範圍比較仍拒絕 NaN／Infinity，屬等價變異。
- **U07、V09、V10、W06、W07**：測試削弱操作。
- **W08、W09**：移除新增斷言後，對應漏洞重新漏過。
- **W10**：集合未區分此防禦子句；不據此宣稱 Unity 可達非有限 Transform。
- **Y02、Y04、Y06**：非等價產品變異，構成 N22 的測試保護缺口。

## 重現資訊

本輪來源副本、harness、變異及探針均僅存在記憶體，未輸出檔案。

重跑條件：

1. 固定從 `29c314a` 讀取來源與快照。
2. 使用真實 Mathematics／NUnit，以 Roslyn C# 8、Release 編譯至 `MemoryStream`。
3. 反射展開 `[Test]`／`[TestCase]`，逐案例執行 SetUp／TearDown。
4. 納入完整 Configure fixture，以及含父子姿態運算的 managed stand-ins。
5. 每項變異從乾淨來源獨立編譯；存活項確認 153 案例集合。
6. 使用不同 planet 的第二次 Configure，觀察四種 N20 拒絕路徑的欄位原子性。
7. 以 N21 小整數方向見證驗證最終切向 Post 與下一次 Step。
8. 三種符號設定執行 Step／AddLook／pitch 探針及 N23 眼高見證。

未執行 Unity PlayMode、完整 Cinemachine pipeline、Burst、IL2CPP、ARM64、真實輸入裝置或攝影機畫面驗收。

## 建議下一步

1. OPUS 修 N21，在陡坡處理後維持最終切向不變式及輸出閉合，加入固定見證。
2. 補 N22：target 本身／子孫拒絕，以及使用不同 planet 的拒絕前無寫入斷言。
3. 明定 N23 的位置精度域與眼高誤差承諾。
4. 明定 camera／brain output 的 Transform 所有權，於 PlayMode 驗證 pipeline 別名。
5. SOL 複審上述修正；Q-T6 繼續獨立處理。

- 2026-11-07 · ChatGPT Sol（SOL） · done · P1 r11 fail（最高 med）：N20 實作 fixed；90 變異 79 killed／11 survived，新增 6 項 3 killed／3 survived；新增 N21 Step 陡坡切向與閉合缺陷、N22 guard 測試缺口、N23 眼高位置域缺陷 · 下一步：OPUS 修 N21–N23，補 pipeline 別名驗證；SOL 複審