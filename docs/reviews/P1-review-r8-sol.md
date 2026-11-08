> 原始輸出：`codex exec -s read-only -m gpt-6.1-sol`，對象 `ea90a5f`。未修改任何檔案。Claude Opus 已依 N12-R8/N14–N17 修正（見 phase-1.md 交接）。

# P1 第八輪對抗式複審 · SOL

2026-11-07 · ChatGPT Sol（SOL）。總判定：**fail，剩餘項目皆為 low**。

**N10-R7 fixed；U17 fixed；接受 N13 的契約變更，實作已修正。N12 只修好部分：拒絕順序的測試仍抓不到欄位提前寫入。** 另找到預排 pitch 的測試缺口，以及 Step／Configure 的新契約反例。Q-T6 不列入否決理由。

對象：`p1/r5-fixes` 的 **`ea90a5f8e6b8c0cf2f3cf09e3c480149fb8bff37`**。

已讀指定規範、r7 報告與最新交接，並檢查 `git diff 4537bf0 ea90a5f`。受審來源以 `git show ea90a5f:<path>` 取得；沒有修改 repo 檔案或建立暫存檔。

以下 `Planet/`、`Player/` 相對於 `Assets/_Project/Features/`；行號對應受審 commit。

## 方法與限制

未啟動 Unity Editor。

來源、變異及補充探針均保存在記憶體，以 Roslyn 編譯至 `MemoryStream`，載入 assembly 後執行 NUnit 反射 runner。

實際環境：

- Windows x64、Windows PowerShell 5.1、.NET Framework CLR `4.0.30319.42000`。
- Roslyn `3.7.0.0`、C# 8、Release 最佳化；主要測試定義 `UNITY_EDITOR`。
- 真實 Unity.Mathematics 1.3.3、NUnit `3.5.0.0`。
- Mathematics DLL SHA-256：`48049AFDD1E1FBA73BFFAE24A6DAB5E8B32BC63E387EC8BCEA53CB2A5382D12E`。
- 相依 DLL 使用可用 facade／相容版本解析；不宣稱等同 Unity Mono、Burst 或 IL2CPP。

`PlayerCameraRig.cs` 整份參與編譯，包含新增的 `PlayerCameraRigConfigureTests.cs`。UnityEngine／Cinemachine 使用最低限度 managed stand-ins；`PlanetTerrain.Configure` 擷取至建立 quadtree，`SphericalCharacter.Facing` 擷取原始 getter。未模擬 Unity 物理。

十四份測試來源、十七個 fixture 類別，共 **156 案例**：

| 執行方式 | 結果 |
|---|---|
| 原始快照期望值 | **154 pass、2 個已知 Q-T6 mismatch** |
| 僅在記憶體校準兩個快照期望值 | **156/156 pass** |
| 新增 Configure PlayMode fixture，以 managed runner 執行 | **6/6 pass** |
| 加入兩個記憶體補充測試 | **158/158 pass** |
| `DEVELOPMENT_BUILD`：Configure 與 r6 fixture | **16/16 pass** |

僅在記憶體移除 `TestContext.WriteLine` 診斷輸出，保留斷言。PlayMode fixture 的上述結果不代表 Unity PlayMode 實跑。

Q-T6 與 r7 相同：

| 快照 | 提交期望值 | 本輪實測 |
|---|---|---|
| 預設 octave | `B7C5CA249FD8C22D` | `383CA51ADC74C561` |
| 八 octave | `AF9DD87B0574B16F` | `B94911EBA33AB6D5` |

## r7 項目複審

| 項目 | 判定 | 證據與限制 |
|---|---|---|
| N10-R7 | **fixed** | AddLook Post `:106–108` 明確涵蓋 partial／full reversal，不再排除完整反轉。 |
| N12：Configure 拒絕非法倍率 | **fixed** | 刪除整個檢查、只移除 sensitivity 或 gamepad 檢查均被新 fixture 殺死。 |
| N12：有限負靈敏度下界 | **fixed** | `−10.1` 案例已加入；U13 現在被殺死。 |
| N12：拒絕順序 | **partial** | U06 仍存活：先寫 `_planet` 再檢查，完整集合仍 **156/156**。 |
| N13 | **實作 fixed；接受契約變更** | Configure `:54–55` 宣告丟棄預排輸入，`:88` 在 LateUpdate 前清零。yaw 回歸有效；pitch 回歸仍缺，見 N14。 |
| U17 | **fixed** | 新測試直接斷言固定 `±80f`；兩個常數一起改為 `±89f` 現在被殺死。 |

### Configure 丟棄預排輸入

接受此變更：尚未建立 orbit 時累積的輸入，在 Configure 重設攝影機時丟棄，行為一致且已記入交接。

既有組合根 `Assets/_Project/Bootstrap/PlanetSandbox.cs:73–75` 先 Configure rig，再建立 input driver；未發現該呼叫端依賴保留預排輸入。

實測完整反轉仍產生：

```text
sensitivity = 1
AddLook pitch +300°
AddLook pitch −300°
PendingLook.pitch = −120°
```

這符合逐次飽和契約，也說明新 Post 必須涵蓋完整反轉。隨後 Configure 會丟棄該 queue，初始 orbit pitch 為零。

### 邊界與額外契約掃描

| 探針 | 結果 |
|---|---|
| 起始 pitch 每 `0.01°`，搭配 `±160°`、`±180°`、`±1e7°` | **96,006 案例、0 違反** |
| 合法隨機倍率、pointer、stick、dt，連續 AddLook 累加 | **100,000 次、0 有限性／範圍違反** |
| 合法 motor 狀態，各連續兩次 Step | **100,000 組、0 拒絕／所檢查不變式違反** |
| 上述合法 AddLook／Step 探針，無開發符號版本 | 同樣通過 |

Step 掃描檢查有限性、速度域、相對切向界及 Facing；既有 20,000 組方向閉合測試亦隨完整集合通過。

隨機 Move 採樣位於單位圓內，不能涵蓋全部容差外緣；N15 是另外建立的合法邊界反例。

## 新發現

### N12-R8 · low · Configure 拒絕順序的測試仍看不到提前寫入

位置：`Player/Tests/PlayMode/PlayerCameraRigConfigureTests.cs:58–63`。

新測試只觀察：

```csharp
Assert.IsNull(_third.Target.TrackingTarget);
Assert.Throws<ContractException>(() => _ = _rig.Heading);
```

以下變異仍 **156/156 pass**：

```csharp
_planet = planet;
RequireMultipliers();
```

將檢查再延後至 `_target = target` 後，也 **156/156 pass**。這兩種提前寫入都不會改變 TrackingTarget 或 `_configured`。

**原始實作順序正確；缺口仍在測試。**

記憶體補充測試直接檢查 `_planet.IsValid == false`，並檢查其他 Configure 參照欄位未寫入；原始版本通過，U06、V05 均失敗。

### N14 · low · 「丟棄所有預排 look」只測到 yaw

位置：`Player/Tests/PlayMode/PlayerCameraRigConfigureTests.cs:74–80`。

將 Configure 的清零改為：

```csharp
_pendingLook.x = 0f;
```

完整集合仍 **156/156 pass**。

原因是現有見證只預排 yaw。若預排 pitch：

```csharp
rig.AddLook(new Vector2(0f, 750f), Vector2.zero, 0f);
Configure();
```

這個 mutant 會留下 `+90°` pitch，LateUpdate 消費後：

```text
PendingLook = (0,0)
orbit.PitchDegrees = 80
```

Heading 仍正確，PendingLook 又已被消費，因此現有兩個斷言都無法識別違反「丟棄預排輸入」。

記憶體補充測試斷言 Configure 後 orbit pitch 為零：原始版本通過，V02 被殺死。

### N15 · low · 合法 Move 容差可突破 Step 的速度 Post

位置：`Player/Runtime/Model/SphericalMotor.cs:219、250、276–286`。

MotorInput 允許長度略大於 1 的容差，Step 也接受 `lengthsq(Move) ≤ 1.002`；但 wish velocity 直接乘上未限幅的 Move。

合法見證：

```csharp
settings.WalkSpeed = settings.SprintSpeed = 100f;
settings.GroundAcceleration = 1000f;

input.Move = new float2(0f, 1.0009f);
input.ViewForward = Z;
input.Sprint = true;

// up = Y，平地接觸，從 Initial(Y, Z) 開始
var first  = Step(settings, initial, input, flat, Y, 0.1f);
var second = Step(settings, first,   input, flat, Y, 0.1f);
```

實測：

```text
lengthsq(input.Move)          = 1.001801
|first.PlanarVelocity|        = 100
|second.PlanarVelocity|       = 100.09
max(|first.PlanarVelocity|,
    settings.SprintSpeed)     = 100
```

第二次 Step 違反速度 Post；`0.09 m/s` 並非 float rounding，且遠離 `MaxStateSpeed` 邊界 clamp。

應將容差內超過單位長度的 Move 限幅，或明確變更速度契約並補測試。這是既存路徑的新見證，非本 commit 引入。

### N16 · low · Configure 未宣告 target 必須遠離星球中心

位置：`Player/Runtime/PlayerCameraRig.cs:52–53、67–83`。

取有效 `PlanetConfig(640, 0)`、有效獨立參照，並令：

```text
target.position = planet.Center
```

滿足 Configure 目前明列的 Pre，卻在 `PlanetFrame.At` 丟出：

```text
Precondition failed:
worldPosition must be finite and away from the planet centre
```

此時相機 TrackingTarget 與 brain.WorldUpOverride 已接線。

應在 Configure 宣告並於寫入前驗證 target position 的必要域；目前把下游 API 的 Pre 留成未宣告條件。此路徑亦早於本 commit。

### N17 · low · Configure 允許兩個 camera 參數指向同一物件

位置：`Player/Runtime/PlayerCameraRig.cs:62–64、171–175`。

Configure 只要求非 null，沒有要求兩個攝影機不同。令：

```csharp
firstPerson = thirdPerson;
```

實測：

| 時點 | first priority | third priority |
|---|---:|---:|
| Configure 後 | 20 | 20 |
| ToggleView 後 | 10 | 10 |

Configure 成功後呼叫 ToggleView，符合其 Pre，卻無法履行「exactly one camera has the higher priority」的 Post。

應宣告並在接線前檢查兩個 camera 不同。此結論只依賴同一物件的欄位寫入，不依賴 Cinemachine 畫面模擬。

## 變異結果

**74 項：65 killed、9 survived。**

| 集合 | killed | survived |
|---|---:|---:|
| r7 所列的舊 47 項 | 46 | 1 |
| r7 新增的 U01–U17 | 13 | 4 |
| 本輪新增 V01–V10 | 6 | 4 |
| **合計** | **65** | **9** |

r7 的 64 項本輪結果為 **59 killed、5 survived**；歷史比較採 r7 文末更正的 **56 killed、8 survived**。

主要 mutation runner 執行 **141 案例**，排除 `CubeSphereTests`、`ChunkQuadtreeTests`；九個存活項另以完整集合確認，均 **156/156 pass**。

74 項均成功編譯並執行，未將編譯失敗計為 kill。舊 harness 未保存，依 r7 表列語意重建，不宣稱替換字串逐位相同。

### 舊 47 項

| ID／變異 | 結果 |
|---|---|
| R01 移除遲滯上界檢查 | killed |
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
| B07 LevelRotation 本體改回傳 pitched Rotation | killed |
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
| U03 只刪除 Configure 倍率檢查 | **killed：新 fixture** |
| U04 只刪除 AddLook 倍率檢查 | killed |
| U05 AddLook 檢查移至 queue 寫入後 | killed |
| U06 Configure 檢查移至 `_planet` 寫入後 | **survived** |
| U07 移除 AssertInRange 的 yaw／pitch 範圍斷言 | **survived** |
| U08 只移除 sensitivity 的 `isfinite` | **survived** |
| U09 只移除 gamepad rate 的 `isfinite` | **survived** |
| U10 移除 gamepad rate 非負下界 | killed |
| U11 移除 gamepad rate 上界 | killed |
| U12 移除 sensitivity 幅度上界 | killed |
| U13 sensitivity 改為單側 `s ≤ Max` | **killed：新 fixture** |
| U14 pending pitch clamp ±180→±160 | killed |
| U15 sensitivity 合法端點 ≤→< | killed |
| U16 gamepad rate 合法端點 ≤→< | killed |
| U17 兩個 pitch 常數一起 ±80→±89 | **killed：新 fixture** |

### 本輪新增十項

| ID／變異 | 結果 |
|---|---|
| V01 移除 Configure 的 queue 清零 | killed |
| V02 Configure 只清 yaw、保留 pitch | **survived** |
| V03 Configure 只清 pitch、保留 yaw | killed |
| V04 queue 清零移至 LateUpdate 後 | killed |
| V05 Configure 倍率檢查移至 `_target` 寫入後 | **survived** |
| V06 Configure 倍率檢查移至 TrackingTarget 接線後 | killed |
| V07 Configure 僅保留 gamepad Requires，移除 sensitivity Requires | killed |
| V08 Configure 僅保留 sensitivity Requires，移除 gamepad Requires | killed |
| V09 只移除 PendingLook yaw 範圍斷言 | **survived** |
| V10 只移除 PendingLook pitch 範圍斷言 | **survived** |

存活項解讀：

- **U06、V05：實際拒絕順序測試缺口**，見 N12-R8。
- **V02：實際 pitch 丟棄測試缺口**，見 N14。
- **U07、V09、V10：測試削弱操作**；仍有固定 pending 值斷言，存活本身不是產品缺陷。
- **U08、U09：等價變異**；剩餘範圍比較已拒絕 NaN／Infinity。
- **T03：未找到反例**；仍保留縮回速度域的 factor，存活不證明產品缺陷。

加入兩個記憶體補充測試後，原始版本 **158/158 pass**；U06、V05、V02 各由對應補充測試殺死。

## 重現資訊

本輪 harness、變異與補充探針僅存在記憶體，未輸出檔案。

重跑條件：

1. 從 `ea90a5f` 讀取來源，以真實 Mathematics／NUnit、Roslyn Release 編譯至 MemoryStream。
2. 反射展開 `[Test]`／`[TestCase]`，逐案例執行 SetUp／TearDown。
3. 納入新增 Configure PlayMode fixture；使用 managed Cinemachine／Transform stand-ins。
4. 僅在記憶體處理診斷輸出與兩個已知快照期望值。
5. 每個 mutant 從乾淨來源副本獨立編譯；存活項重跑完整集合。
6. 另跑倍率累加、pitch 邊界、連續 Step、Move 容差、中心位置與 camera alias 見證。
7. 以兩個補充測試確認拒絕順序及 pitch 清零缺口。

未執行 Unity PlayMode、Burst、IL2CPP、ARM64、真實輸入裝置或攝影機畫面驗收。

## 建議下一步

1. OPUS 補 Configure 拒絕前欄位不變的測試，以及預排 pitch 丟棄測試。
2. 修正 Move 容差與 Step 速度 Post 的不一致，加入 `1.0009` 邊界見證。
3. 為 Configure 明訂並提前驗證 target position 域及 camera 參照互異條件。
4. SOL 複審上述修正；Q-T6 繼續獨立處理。

