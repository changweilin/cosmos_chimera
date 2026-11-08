> 原始輸出：`codex exec -s read-only -m gpt-6.1-sol`，對象 `ab49768`。未修改任何檔案。Claude Opus 已依 N18／N19 修正（見 phase-1.md 交接）。


# P1 第九輪對抗式複審 · SOL

2026-11-07 · ChatGPT Sol（SOL）。

**N12-R8、N14、N15、N17 fixed；N16 實作 fixed，位置域邊界測試仍有缺口。** 新發現 Configure 接受 target 位於 pivot 之下的階層，導致角色被間接移動並違反 heading Post。Q-T6 不列入否決理由。

對象：**`ab497683b08a01994374512a8ac6e85e30279070`**。固定審查此 commit，未追隨已前進的分支 HEAD。

已讀指定規範、r8 報告與目標 commit 最新交接，檢查 `git diff ea90a5f ab49768`。來源透過 `git show ab49768:<path>` 取得；未切換 main、修改 repo 或建立暫存檔。

以下 `Planet/`、`Player/` 相對於 `Assets/_Project/Features/`；行號對應受審 commit。

## 方法與限制

未啟動 Unity Editor。

來源、harness、變異及補充探針均保存在記憶體，以 Roslyn 編譯至 `MemoryStream`，載入 assembly 後執行 NUnit 反射 runner。

環境：

- Windows x64、Windows PowerShell 5.1、.NET Framework CLR `4.0.30319.42000`。
- Roslyn `3.7.0.0`、C# 8、Release 最佳化；主要測試定義 `UNITY_EDITOR`。
- 真實 Unity.Mathematics 1.3.3、NUnit `3.5.0.0`。
- Mathematics DLL SHA-256：`48049AFDD1E1FBA73BFFAE24A6DAB5E8B32BC63E387EC8BCEA53CB2A5382D12E`。
- 相依 DLL 採 facade／相容版本解析；不宣稱等同 Unity Mono、Burst 或 IL2CPP。

`PlayerCameraRig.cs` 整份參與編譯。UnityEngine／Cinemachine 使用最低限度 managed stand-ins；Transform 包含本輪階層見證需要的父子位置／旋轉運算，見證使用單位 scale。`PlanetTerrain.Configure` 擷取至建立 quadtree，`SphericalCharacter.Facing` 擷取原始 getter。未模擬 Unity 物理。

十四份測試來源、十七個 fixture，共 **160 案例**：

| 執行方式 | 結果 |
|---|---|
| 原始快照期望值 | **158 pass、2 個已知 Q-T6 mismatch** |
| 僅在記憶體校準快照 | **160/160 pass** |
| 加入四個位置域補充案例 | **164/164 pass** |
| `DEVELOPMENT_BUILD`：Configure、r6 與補充 fixture | **23/23 pass** |

僅在記憶體移除 `TestContext.WriteLine` 診斷輸出、處理快照期望值；保留其餘斷言。Configure fixture 的通過不代表 Unity PlayMode 實跑。

Q-T6 與 r8 相同：

| 快照 | 提交期望值 | 本輪實測 |
|---|---|---|
| 預設 octave | `B7C5CA249FD8C22D` | `383CA51ADC74C561` |
| 八 octave | `AF9DD87B0574B16F` | `B94911EBA33AB6D5` |

## r8 項目複審

| 項目 | 判定 | 證據 |
|---|---|---|
| N12-R8 | **fixed** | Configure fixture `:66–75` 直接檢查 `_planet`、`_target`、`_pivot` 與兩個 camera 欄位。U06、V05 現在均被殺死。 |
| N14 | **fixed** | 新增預排 pitch 見證，`:117` 直接斷言 orbit pitch 為零；V02 被殺死。 |
| N15 | **fixed** | Step `:279–280` 將 Move 限於單位圓；新增三步速度測試。移除限幅時實測 `100.090004 m/s`，測試失敗。 |
| N16 | **實作 fixed；邊界測試不足** | Configure `:69–71` 宣告並檢查 ToDirection 定義域，位於首次欄位寫入之前；刪除或延後整條檢查均被殺死。門檻弱化仍存活，見 N19。 |
| N17 | **fixed** | Configure `:68` 拒絕相同 camera，檢查早於寫入；刪除或延後均被殺死。 |

### 新斷言的有效性

測試削弱操作也確認了修正的作用：

| 操作 | 結果 |
|---|---|
| 保留私有欄位斷言，倍率檢查移至 `_planet` 寫入後 | **killed** |
| 移除私有欄位斷言，再套用同一提前寫入 | **survived** |
| 保留 orbit pitch 斷言，Configure 只清 yaw | **killed** |
| 移除 orbit pitch 斷言，再套用只清 yaw | **survived** |

因此，N12-R8／N14 的改善確實來自新增斷言。

### 邊界與額外契約掃描

每種符號設定均執行以下探針：

| 探針 | 每種設定的結果 |
|---|---|
| 合法 motor 狀態，包含 Move `1.0009` 容差外緣，各連續兩次 Step | **100,000 組，0 拒絕／所檢查 Post 違反** |
| 合法倍率、pointer、stick、dt，連續 AddLook | **100,000 次，0 有限性／範圍違反** |
| 起始 pitch 每 `0.01°`，搭配 `±160°`、`±180°`、`±1e7°` | **96,006 案例，0 違反** |

設定包含 `UNITY_EDITOR`、`DEVELOPMENT_BUILD` 與無開發符號版本。

Step 掃描檢查有限性、速度域、相對切向界、Facing 與速度上界；既有 20,000 組閉合測試也隨完整集合通過。這些結果不涵蓋全部合法輸入。

## 新發現

### N18 · low · Configure 允許 target 是 pivot 的子孫，寫 pivot 會移動並旋轉角色

位置：`Player/Runtime/PlayerCameraRig.cs:52–59、67、97、192–197`。

目前只拒絕：

```csharp
pivot.IsChildOf(target)
```

反向階層仍符合明列 Pre：

```csharp
// pivot 位於原點，identity rotation、unit scale。
// planet R=640、Center=0；其餘參照獨立且有效。
target.SetPositionAndRotation(
    new float3(0f, 640f, 0f),
    quaternion.LookRotationSafe(
        new float3(1f, 0f, 0f),
        new float3(0f, 1f, 0f)));

target.SetParent(pivot, true);
rig.Configure(planet, target, pivot, third, first, brain);
```

此時：

```text
pivot.IsChildOf(target) = false
target.IsChildOf(pivot) = true
```

Configure 呼叫 LateUpdate；LateUpdate 將 pivot 移到快取的角色位置上方，並套用 orbit rotation。target 因為位於 pivot 之下，也跟著移動、旋轉。

managed 階層探針實測：

| 觀察值 | 結果 |
|---|---|
| Configure 前 target.position | `(0, 640, 0)` |
| Configure 後 target.position | `(0, 1281.6, 0)` |
| Configure 後 pivot.position | `(0, 641.6, 0)` |
| pivot 與返回時應有眼高位置的距離 | `641.6 m` |
| `dot(rig.Heading, target.forward)` | 約 `1.42e−14` |

返回時兩個方向仍位於切平面，卻相差約 90°，違反 Configure 的 heading Post；pivot 位置也違反類別的 LateUpdate Post。

**限制：未以 Unity Editor 實跑。** 此見證依據單位 scale 下的父子世界座標關係，不涉及 Cinemachine 畫面或物理模擬。

應明訂 target 也不得位於 pivot 之下，於寫入前檢查，並加入這個階層見證。

### N19 · low · N16 測試只涵蓋球心，抓不到非零禁區被放行

位置：`Player/Tests/PlayMode/PlayerCameraRigConfigureTests.cs:78–84`。

將位置檢查弱化為：

```csharp
math.cmax(math.abs(offset)) > 0f
```

既有 mutation 集合仍 **145/145 pass**。原測試使用精確球心；上述錯誤門檻仍會拒絕它。

但令：

```text
target.position − planet.Center = (0, 0.0005, 0)
```

弱化後的 Configure 會先寫欄位、接線，之後才由 `PlanetConfig.ToDirection` 拒絕，違反「全部在寫入前檢查」。

新增四個記憶體案例：

```text
offset.y = +0.0005、−0.0005、+0.001、−0.001
```

| 版本 | 結果 |
|---|---|
| 原始實作 | **4/4 pass** |
| 門檻 `1e-3 → 0` | **0/4 pass**，全部已寫入 `_planet` |

**目前產品實作正確；缺口在回歸測試。** 應補上非零禁區及門檻等號案例。

## 變異結果

**85 項：73 killed、12 survived。**

| 集合 | killed | survived |
|---|---:|---:|
| 舊 47 項 | 46 | 1 |
| U01–U17 | 14 | 3 |
| V01–V10 | 8 | 2 |
| 本輪 W01–W11 | 5 | 6 |
| **合計** | **73** | **12** |

r8 的原 74 項由 **65 killed／9 survived** 改善為 **68 killed／6 survived**。

每項從乾淨來源副本獨立編譯。先執行相關 Player **83 案例**或 Planet **62 案例**；未殺死者再跑排除 `CubeSphereTests`、`ChunkQuadtreeTests` 的 **145 案例**集合。本輪未逐一對存活項重跑這兩個較慢 fixture；完整原始集合另有前述 164/164 驗證。

85 項最終均成功編譯並執行，未將編譯失敗計為 kill。舊 harness 未保存，依 r8 表列語意重建，不宣稱替換字串逐位相同。

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
| U06 Configure 檢查移至 `_planet` 寫入後 | **killed：本輪新增斷言** |
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
| V02 Configure 只清 yaw、保留 pitch | **killed：本輪新增斷言** |
| V03 Configure 只清 pitch、保留 yaw | killed |
| V04 queue 清零移至 LateUpdate 後 | killed |
| V05 Configure 倍率檢查移至 `_target` 寫入後 | **killed：本輪新增斷言** |
| V06 Configure 倍率檢查移至 TrackingTarget 接線後 | killed |
| V07 Configure 僅保留 gamepad Requires | killed |
| V08 Configure 僅保留 sensitivity Requires | killed |
| V09 只移除 PendingLook yaw 範圍斷言 | **survived** |
| V10 只移除 PendingLook pitch 範圍斷言 | **survived** |

### 本輪新增 W01–W11

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
| W11 位置域門檻 `1e-3→0` | **survived** |

存活項解讀：

- **T03**：仍保留縮回速度域的 factor；未找到反例。
- **U08、U09**：剩餘範圍比較仍拒絕 NaN／Infinity，屬等價變異。
- **U07、V09、V10、W06、W07**：測試削弱操作，存活本身不是產品缺陷。
- **W08、W09**：確認移除新增斷言後，原有漏洞會重新漏過。
- **W10**：現有集合未區分這個防禦子句；未據此宣稱 Unity 中存在可達的非有限 Transform 反例。
- **W11**：實際邊界測試缺口，見 N19；加入四個補充案例後被殺死。

## 重現資訊

本輪 harness、來源副本、變異與補充探針均僅存在記憶體，未輸出檔案。

重跑條件：

1. 固定從 `ab49768` 讀取來源與快照期望值。
2. 使用真實 Mathematics／NUnit，以 Roslyn Release 編譯至 `MemoryStream`。
3. 反射展開 `[Test]`／`[TestCase]`，逐案例執行 SetUp／TearDown。
4. 納入完整 Configure fixture；使用 managed UnityEngine／Cinemachine stand-ins。
5. 每個 mutant 從乾淨來源獨立編譯；存活項確認 145 案例集合。
6. 另跑三種符號設定的 AddLook、pitch 邊界與連續 Step 探針。
7. 執行 pivot 祖先階層見證，以及四個非零位置禁區案例。

未執行 Unity PlayMode、Burst、IL2CPP、ARM64、真實輸入裝置或攝影機畫面驗收。

## 建議下一步

1. OPUS 為 Configure 補上反向階層限制及見證，防止 pivot 間接移動角色。
2. 補上 `±0.0005`、`±0.001` 的位置域拒絕與拒絕前狀態不變測試。
3. SOL 複審 N18／N19；Q-T6 繼續獨立處理。

- 2026-11-07 · ChatGPT Sol（SOL） · done · P1 r9 fail（最高 low）：r8 修正已驗證，85 項變異 73 killed／12 survived；新增 N18 階層契約反例及 N19 位置域邊界測試缺口 · 下一步：OPUS 修 N18、補 N19，SOL 複審