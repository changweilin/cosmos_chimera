> 原始輸出：`codex exec -s read-only -m gpt-6.1-sol`，對象 `ebe2693`。未修改任何檔案。Claude Opus 已依 N20 修正（見 phase-1.md 交接）。


# P1 第十輪對抗式複審 · SOL

2026-11-07 · ChatGPT Sol（SOL）。

**N18、N19 fixed。** r9 的 85 項變異重跑結果為 **74 killed／11 survived**；新增 5 項均 killed。另找到 **N20：重複 Configure 可讓 pivot 與既有 WorldUpOverride 指向同一 Transform，違反 pivot 的位置與旋轉 Post**。Q-T6 不列入否決理由。

對象：**`ebe269305ae33c81a051f417a3f70b5cf062ad6e`**，分支 `p1/r5-fixes`。

已讀指定規約、r9 報告及目標提交最新交接，檢查 `git diff ab49768 ebe2693`。來源固定透過 `git show ebe2693:<path>` 取得；未切換分支、修改 repo 或建立暫存檔。

以下 `Planet/`、`Player/` 相對於 `Assets/_Project/Features/`；行號對應受審提交。

## 方法與限制

未啟動 Unity Editor。

來源、harness、變異及補充探針均保存在記憶體，以 Roslyn 編譯至 `MemoryStream`，載入 assembly 後執行 NUnit 反射 runner。

環境：

- Windows x64、Windows PowerShell、.NET Framework。
- Roslyn `3.7.0.0`、C# 8、Release 最佳化。
- 真實 Unity.Mathematics、NUnit `3.5.0.0`。
- Mathematics DLL SHA-256：`48049AFDD1E1FBA73BFFAE24A6DAB5E8B32BC63E387EC8BCEA53CB2A5382D12E`，與 r9 相同。
- 相依 DLL 使用 facade／相容版本解析，不宣稱等同 Unity Mono、Burst 或 IL2CPP。

`PlayerCameraRig.cs` 整份參與編譯。UnityEngine／Cinemachine 使用最低限度 managed stand-ins；Transform 包含單位 scale 下的父子位置／旋轉運算。`PlanetTerrain.Configure` 擷取至建立 quadtree，`SphericalCharacter.Facing` 擷取原始 getter。未模擬 Unity 物理。

十四份測試來源，共 **166 案例**：

| 執行方式 | 結果 |
|---|---|
| 原始快照期望值 | **164 pass、2 個已知 Q-T6 mismatch** |
| 僅在記憶體校準快照 | **166/166 pass** |
| `UNITY_EDITOR`：Configure 與 r6 fixture | **25/25 pass** |
| `DEVELOPMENT_BUILD`：Configure 與 r6 fixture | **25/25 pass** |
| 額外位置域探針：兩種開發符號設定 | 各 **6/6 pass** |
| N20 補充契約見證，獨立執行 | **0 pass、2 fail** |

快照測試的檔案讀取改為使用 `git show` 取得的提交內容；另僅在記憶體移除 `TestContext.WriteLine` 診斷輸出。Configure fixture 的通過不代表 Unity PlayMode 實跑。

Q-T6：

| 快照 | 提交期望值 | 本輪實測 |
|---|---|---|
| 預設 octave | `B7C5CA249FD8C22D` | `383CA51ADC74C561` |
| 八 octave | `AF9DD87B0574B16F` | `B94911EBA33AB6D5` |

## r9 項目複審

| 項目 | 判定 | 證據 |
|---|---|---|
| N18 | **fixed** | `PlayerCameraRig.cs:71` 新增 `!target.IsChildOf(pivot)`；早於首次欄位寫入 `:78`。既有 `:69` 配合 IsChildOf 自反性拒絕自身 pivot。 |
| N19 | **fixed** | Configure fixture `:88–97` 加入 `±0.0005`、`±0.001`，均驗證拒絕且未寫入。W11 現在被四個案例殺死。 |
| N18 階層見證 | **有效** | fixture `:100–107` 驗證 target 位於 pivot 之下時拒絕、欄位未寫入、角色位置不變。刪除或延後新 guard 均被殺死。 |
| target 當自身 pivot | **有效** | fixture `:111–114` 通過。刪除新 guard 後仍由原有反向 guard 拒絕；這個案例不單獨證明新 guard 有效。 |
| N19 門檻等號 | **有效** | `> 1e-3` 改為 `>= 1e-3` 時，`±0.001` 兩例殺死變異。 |

### 新斷言的有效性

測試削弱操作確認改善確實來自新增見證：

| 操作 | 結果 |
|---|---|
| 刪除新階層 guard，保留 N18 階層見證 | **killed** |
| 同上，再移除 N18 階層見證 | **survived，150/150 pass** |
| 位置門檻改為 `> 0`，保留四個 N19 案例 | **killed** |
| 同上，再移除四個 N19 案例 | **survived，147/147 pass** |

上述兩個測試削弱對照另行執行，不計入 90 項總數。

### 邊界與額外契約掃描

以下有效輸入探針分別以 `UNITY_EDITOR`、`DEVELOPMENT_BUILD`、無開發符號版本執行：

| 探針 | 每種設定的結果 |
|---|---|
| 合法 motor 狀態，包含高速度及 Move `1.0009`，各連續兩次 Step | **100,000 組，0 拒絕／所檢查 Post 違反** |
| 合法倍率、pointer、stick、dt，連續 AddLook | **100,000 次，0 有限性／範圍違反** |
| 起始 pitch 每 `0.01°`，搭配 `±160°`、`±180°`、`±1e7°` | **96,006 案例，0 違反** |

Step 檢查速度域、有限性、相對切向界、Facing、Grounded 組合與速度上界；既有 20,000 組閉合測試亦隨完整集合通過。

另在兩種開發符號設定檢查：

```text
offset.y = ±0.0005、±0.001、±0.0010001
```

前四例拒絕，後兩例接受。未從這些掃描找到新的 AddLook／Step 反例；掃描不涵蓋全部合法輸入。

## 新發現

### N20 · low · 重複 Configure 允許 pivot 與既有 WorldUpOverride 別名，第二次 pose 寫入覆蓋眼高與 pitch

位置：`Player/Runtime/PlayerCameraRig.cs:52–59、87–92、100–101、200–201`。

Configure 未宣告只能呼叫一次，也未拒絕重複呼叫。第一次 Configure 後，呼叫端可透過公開的 `brain.WorldUpOverride` 取得 rig 擁有的 Transform，再傳作第二次 Configure 的 pivot：

```csharp
// planet R=640、Center=0；target=(0,640,0)，identity rotation。
// 所有 Transform 獨立、unit scale，其餘參照有效。
rig.Configure(config, target, pivot, third, first, brain);

Transform secondPivot = brain.WorldUpOverride;
rig.Configure(config, target, secondPivot, third, first, brain);
```

第二次呼叫符合明列 Pre：

- target 與 secondPivot 不同，互不為子孫。
- planet、參照、位置域及倍率均合法。
- 兩個 camera 不同。

但 `_worldUp` 已存在，`:87–90` 不會建立新物件，因此 `_pivot == _worldUp`。LateUpdate 對同一 Transform 依序執行：

```csharp
_pivot.SetPositionAndRotation(position + up * _eyeHeight, _orbit.Rotation());
_worldUp.SetPositionAndRotation(position, _orbit.LevelRotation());
```

第二次寫入覆蓋第一次。

| 觀察值 | 實測 |
|---|---|
| 第二次 Configure 返回時 target.position | `(0,640,0)` |
| pivot.position | `(0,640,0)` |
| 應有 pivot.position | 約 `(0,641.6,0)` |
| 眼高位置誤差 | **`1.59997559 m`** |
| 排入 `+30°` pitch 並執行 LateUpdate 後，pivot forward 與 local up 的 dot | **`0`，應為約 `0.5`** |
| 實際 pivot rotation 與 orbit rotation 的 quaternion 絕對 dot | **`0.9659259`** |

兩個獨立 NUnit 補充案例均失敗：

```text
N20_alias_preserves_eye_height：誤差 1.59997559 > 1e-4
N20_alias_preserves_pitch：forward·up = 0，期望 0.5 ± 1e-5
```

這違反類別「每次 LateUpdate 後 pivot 位於眼高並採 orbit rotation」的 Post；Configure 自己也呼叫 LateUpdate。

**這是本輪新找到的既存缺陷，非宣稱由 N18 修正引入。** 見證不需反射讀取私有欄位；僅以反射驅動 LateUpdate。三種符號設定均重現別名造成的錯誤。

**限制：未以 Unity Editor 實跑。** 此反例只依賴同一 Transform 的連續寫入，不涉及物理、非單位 scale 或 Cinemachine 畫面。

應明確選定生命週期：若 Configure 僅允許一次，補上契約及寫入前拒絕；若支援重綁，必須維持 pivot 與 owned world-up 的角色分離，並加入重綁見證。

## 變異結果

**90 項：79 killed、11 survived。**

| 集合 | killed | survived |
|---|---:|---:|
| 舊 47 項 | 46 | 1 |
| U01–U17 | 14 | 3 |
| V01–V10 | 8 | 2 |
| W01–W11 | 6 | 5 |
| **r9 原 85 項** | **74** | **11** |
| 新增 X01–X05 | 5 | 0 |
| **合計** | **79** | **11** |

原 85 項相較 r9 的 **73 killed／12 survived**，改善來自 **W11 被新增 N19 案例殺死**。

每項從乾淨來源副本獨立編譯。相關 Player 集合為 **89 案例**；R 組先跑選定 Planet fixture **55 案例**。存活項再確認排除 `CubeSphereTests`、`ChunkQuadtreeTests` 的 **151 案例**集合。完整 166 案例另行通過。

全部 90 項成功編譯並執行，未將編譯失敗計為 kill。舊 harness 未保存，本輪依 r9 表列語意重建，不宣稱替換字串逐位相同。

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
| U08 只移除 sensitivity 的 `isfinite` | **survived** |
| U09 只移除 gamepad rate 的 `isfinite` | **survived** |
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
| W11 位置域門檻 `1e-3→0` | **killed：四個 N19 案例** |

### 本輪新增 X01–X05

| ID／變異 | 結果 |
|---|---|
| X01 刪除 `!target.IsChildOf(pivot)` Requires | **killed：N18 階層見證** |
| X02 新階層檢查移至 `_planet` 寫入後 | **killed：`_planet untouched`** |
| X03 位置域門檻 `1e-3→5e-4` | **killed：`±0.001`** |
| X04 位置域嚴格門檻 `>→>=` | **killed：`±0.001`** |
| X05 新階層檢查弱化為僅 `target != pivot` | **killed：N18 階層見證** |

存活項解讀：

- **T03**：仍保留縮回速度域的 factor；未找到反例，不宣稱已證明等價。
- **U08、U09**：剩餘範圍比較仍拒絕 NaN／Infinity，屬等價變異。
- **U07、V09、V10、W06、W07**：測試削弱操作，存活本身不是產品缺陷。
- **W08、W09**：確認移除欄位／pitch 新斷言後，對應漏洞重新漏過。
- **W10**：現有集合未區分此防禦子句；未據此宣稱 Unity 中存在可達的非有限 Transform 反例。

## 重現資訊

本輪 harness、來源副本、變異及補充探針均僅存在記憶體，未輸出檔案。

重跑條件：

1. 固定從 `ebe2693` 讀取來源與快照期望值。
2. 使用真實 Mathematics／NUnit，以 Roslyn Release 編譯至 `MemoryStream`。
3. 反射展開 `[Test]`／`[TestCase]`，逐案例執行 SetUp／TearDown。
4. 納入完整 Configure fixture，以及含父子姿態運算的 managed stand-ins。
5. 每項變異從乾淨來源獨立編譯；存活項確認 151 案例集合。
6. 三種符號設定執行有效輸入的 Step／AddLook／pitch 探針。
7. 以公開 `brain.WorldUpOverride` 建立 N20 別名，再驗證 Configure 返回位置及後續 pitch。

未執行 Unity PlayMode、Burst、IL2CPP、ARM64、真實輸入裝置或攝影機畫面驗收。

## 建議下一步

1. OPUS 明定 Configure 是否支援重複呼叫，修正 N20 的 Transform 別名問題並補回歸測試。
2. SOL 複審 N20；N18／N19 可視為修復。
3. Q-T6 繼續獨立處理。

- 2026-11-07 · ChatGPT Sol（SOL） · done · P1 r10 fail（最高 low）：N18／N19 fixed，原 85 變異 74 killed／11 survived，新增 5 項全 killed；新增 N20 重複 Configure 的 pivot／WorldUpOverride 別名反例 · 下一步：OPUS 修 N20 並補回歸，SOL 複審