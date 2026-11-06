# P1 第五輪對抗式複審 · SOL

2026-11-06 · ChatGPT Sol（SOL）。總判定：**fail**。

r4 的 **N3、N5、F7 fixed；N4 partially fixed**。原本 `2e19` 速度造成 NaN 的入口已封住，但新增速度域沒有對 Step 輸出閉合：合法邊界狀態經一次 Step 後，下一次 Step 會拒絕自己的輸出。新增 N6（med）、N7–N9（low）。Q-T6 不列入否決理由。

對象：**`2539d74a99f6a8a5463cf58687af2519bc1d0aaa`**，`P1 r4 fixes: motor state speed domain (N4), F7 invariants, N3/N5 tests`。

已讀指定規範、roadmap 交接、r4 報告及相關分工文件；來源一律以 `git show 2539d74:<path>` 取得，未讀工作樹未提交內容，未修改 repo 或 roadmap。以下 `Planet/`、`Player/` 相對於 `Assets/_Project/Features/`；行號均對應此 commit。

## 方法與限制

未啟動 Unity。

本 session 的唯讀沙箱亦禁止寫入 `%TEMP%`；`dotnet --info` 嘗試建立暫存目錄時已回報 `UnauthorizedAccessException`。因此**沒有建立指定的檔案式 harness**，改將 commit 原始碼與每項變異保存在記憶體，以 Roslyn 編譯至 `MemoryStream`，再載入 assembly 執行 NUnit 反射 runner。

實際環境：

- Windows x64、Windows PowerShell 5.1、.NET Framework CLR `4.0.30319.42000`。
- Roslyn 3.7、C# 8、Release 最佳化、定義 `UNITY_EDITOR`。
- 引用 r4 暫存 harness 保留的真實 Unity.Mathematics 1.3.3 DLL 與 NUnit；未替換 `float3`、`math` 或 quaternion 運算。
- Mathematics DLL SHA-256：`48049AFDD1E1FBA73BFFAE24A6DAB5E8B32BC63E387EC8BCEA53CB2A5382D12E`。
- 為載入該 DLL，將其 `netstandard 2.1` assembly 相依性解析至可用 facade。這是本輪載入方式的限制；**不宣稱與 r4 的 .NET 9、Unity Mono 或 Burst 等價**。

反射 runner 執行十一組 fixture 的 `[Test]` 與全部 `[TestCase]`，共 **130 案例：128 個純規則案例、2 個 managed 投影案例**。

投影範圍：

1. `PlanetTerrain.Configure` 從方法開頭至 `_tree = new ChunkQuadtree(config, lod)`，連同新的 `IsUntouchedByConfigure`。
2. `SphericalCharacter.Facing`、`PlayerCameraRig.Heading`、`AddLook` 的實際方法內容與必要欄位。

使用最小 GameObject／Transform／Vector2 容器承接測試；`Vector3` 回傳或比較改用 `float3`。這些結果只驗證 managed guard、欄位賦值與數學，不代表執行 Unity 生命週期、native buffer、物理或 Cinemachine。

原始快照基線：**128 pass／2 fail**。兩項失敗仍為已知 Q-T6：

| 快照 | commit 預期 | 本輪實測 |
|---|---|---|
| 預設六 octave | `B7C5CA249FD8C22D` | `383CA51ADC74C561` |
| 八 octave | `AF9DD87B0574B16F` | `B94911EBA33AB6D5` |

僅在記憶體測試副本校準上述兩個預期值後，完整基線為 **130/130**。不把快照差異算作 mutation kill。

40 項變異先執行 115 案例子集，排除耗時且與修正無關的 `CubeSphereTests`、`ChunkQuadtreeTests`；三項存活變異再各自執行完整 130 案例，均存活。另以未變異來源重跑完整基線，仍為 **130/130**。

未執行 PlayMode、Burst、IL2CPP、ARM64、實際輸入裝置或攝影機畫面；`PlayerInputDriver` 的修正採靜態查核。實作者交接中的 Unity 測試數字不充作本輪實測。

## r4 項目複審

| 項目 | 判定 | 檔案:行號與證據 |
|---|---|---|
| N3 | **fixed** | `Planet/Runtime/PlanetTerrain.cs:100` 新增欄位狀態 hook；`:124` 在 `:126` 首次賦值前檢查 Supports。`Planet/Tests/EditMode/PlanetReviewR3RegressionTests.cs:92`、`:98` 檢查呼叫前後欄位狀態，`:99` 確認錯誤來自 Configure 自己的 guard。R02 刪除 guard、R02b 延後 guard 均被「Configure wrote fields before rejecting」殺死。 |
| N4 | **partially fixed** | `Player/Runtime/Model/SphericalMotor.cs:189` 明訂 `MaxStateSpeed=1e4`；`:238`–`:243` 限制 planar／vertical／總速度，`:320` 限制下降速度。原 `2e19`、`1e20`、`3e38` 反例現在拒絕，新增軸向邊界測試亦通過。但斜向邊界輸出可能不符合下一次 Step 的速度 Requires，見 N6；獨立速度條件仍有測試缺口，見 N9。 |
| N5 | **fixed（原變異）** | `Player/Tests/EditMode/SphericalMotorReviewR4Tests.cs:55`–`:68` 固定 r4 見證。P03 刪除 motor `:258` 的切向修正，新增測試失敗；原本存活的變異已被殺死。這不代表整個新速度域都滿足絕對切向誤差，見 N7。 |
| F7 | **fixed（r4 列出的缺項）** | motor `:244` 拒絕 Grounded 且帶 VerticalSpeed／JumpStarted；`ViewOrbit.cs:56`、`:80`、`:95` 共用 `:101` 的 invariant 檢查。`SphericalCharacter.cs:61`、`PlayerCameraRig.cs:45` 檢查 Configure 生命週期；rig `:102` 檢查 stick 範圍。`PlayerInputDriver.cs:36`–`:41` 改為非 throwing FindAction，再用 Contract 檢查缺 action。各 managed guard 的反向變異均被殺死；Input System 實際執行未驗證。 |

## 新發現

### N6 · med · 新速度域對 Step 輸出不閉合

位置：`Player/Runtime/Model/SphericalMotor.cs:238`–`:243`、`:256`–`:264`；測試 `Player/Tests/EditMode/SphericalMotorReviewR4Tests.cs:33`–`:49`。

設定使用 Default，僅令 `AirAcceleration=1e-20`；無輸入、無地面、`dt=.02`：

```csharp
var from = new float3(-0.0648168847f, 0.9838996f, -0.166554645f);
var to = new float3(0.8500662f, 0.153920412f, 0.5036825f);
var s = SphericalMotor.Initial(from, new float3(0, 0, 1));
s.PlanarVelocity = s.Velocity =
    new float3(-9968.19f, -560.8229f, 566.2624f);

var next = SphericalMotor.Step(settings, s, default, default, to, .02f);
SphericalMotor.Step(settings, next, default, default, to, .02f);
```

實測：

| 數值 | 結果 |
|---|---:|
| 輸入 `lengthsq(PlanarVelocity)` | `100000000` |
| 輸入 `dot(PlanarVelocity, Up)` | `0.0000305175781` |
| 輸出 `lengthsq(PlanarVelocity)` | `100000008` |
| 輸出 `length(PlanarVelocity)` | `10000` |
| 下一次 Step | `ContractException: prev speeds exceed MaxStateSpeed` |

輸入符合速度域與明文 `1e-4` 切向 invariant；沒有以違約 prev 製造反例。`length` 捨入為 10000，`lengthsq` 卻超過 Requires 的嚴格平方上限。

現有測試只固定 Y→近 Y、X 軸速度，且長度斷言另外放寬 `1e-6`；該方向剛好能再次 Step，保護不到其他方向。

獨立掃描使用 `Pcg32(77,11)`、100,000 組方向，只保留前後 up 夾角小於 90°、輸入平方長度不超限且輸入絕對切向誤差 ≤`1e-4` 的案例：**8,764 組合法輸入中，2,271 組輸出平方長度超限**。

建議使輸出與下一次 Pre 採一致的數值域：輸出收斂到上限內，或明訂一致的捨入餘量並同步契約。只放寬測試斷言不能解決連續 Step 被拒絕。

### N7 · low · 保留切向修正仍無法保證整個速度域的絕對 `1e-4` invariant

位置：`Player/Runtime/Model/SphericalMotor.cs:139`、`:257`–`:264`；新增見證測試 `Player/Tests/EditMode/SphericalMotorReviewR4Tests.cs:55`–`:68`。

同樣使用無輸入／無地面、`AirAcceleration=1e-20`：

```csharp
var from = new float3(-0.144974574f, 0.5815f, 0.80052495f);
var to = new float3(-0.3284381f, 0.9019396f, 0.280416429f);
var s = SphericalMotor.Initial(from, new float3(0, 0, 1));
s.PlanarVelocity = s.Velocity =
    new float3(930.007263f, -7974.81934f, 5961.32f);
```

| 數值 | 輸入 | 輸出 |
|---|---:|---:|
| `lengthsq(PlanarVelocity)` | `99999992` | `100000000` |
| `dot(PlanarVelocity, Up)` | `0` | `0.000244140625` |

輸入符合嚴格 invariant；基線保留所有切向修正，輸出仍超過 `1e-4`。下一次 Step 接受此結果，因 `:233` 的 Requires 採相對長度容差；型別 Post 卻寫絕對容差。

上述 8,764 組合法輸入中，**3,834 組輸出超過絕對 `1e-4`**，最大值 `0.00146484375`。這是小量浮點殘差，未重現 NaN。

N5 的速度 1000 固定見證確實保護到刪除修正的變異；它沒有保護到新增 10000 速度域的 Post。建議統一相對／絕對容差政策，或提供能滿足絕對界線的運算與邊界測試；契約若調整，需同步呼叫端與交接。

### N8 · low · AddLook 的合法有限輸入可污染累加狀態為 Infinity／NaN

位置：`Player/Runtime/PlayerCameraRig.cs:98`–`:104`、`:131`–`:132`。

重現：

```csharp
rig.AddLook(Vector2.zero, new Vector2(1f, 0f), float.MaxValue);
```

符合目前 Pre：pointer 有限、stick 在範圍內、deltaTime 有限且非負。預設 `_gamepadDegreesPerSecond=180`：

```text
180 * float.MaxValue → Infinity
(1, 0) * Infinity   → (Infinity, NaN)
```

managed 投影實測 `_pendingLook=(Infinity, NaN)`；交給真實 `ViewOrbit.Advance` 後，拋出 `look deltas must be finite`。

來源控制流程顯示，LateUpdate 在 `:131` 呼叫 Advance，直到成功後才於 `:132` 清除 pending，因此失敗值會保留。**未執行真正 LateUpdate 或攝影機生命週期**。

新增 F7 測試只檢查 stick 拒絕與一般值接受，沒有涵蓋有限輸入的乘積／累加溢位。建議明訂可實現的時間與 look 累加域，先驗證候選結果再提交狀態。

### N9 · low · 新速度域的三條獨立限制沒有測試保護

位置：`Player/Runtime/Model/SphericalMotor.cs:239`–`:242`；測試 `Player/Tests/EditMode/SphericalMotorReviewR4Tests.cs:20`–`:49`。

三種變異均通過完整 **130/130**：

| ID | 刪除內容 | 基線拒絕、變異接受的見證 |
|---|---|---|
| S06 | planar 的歐氏平方長度上限，保留逐分量上限 | Up=Y，`PlanarVelocity=Velocity=(8000,0,8000)`。每分量合法，但總長超過 10000。 |
| S07 | `abs(prev.VerticalSpeed) <= MaxStateSpeed` | `VerticalSpeed=10001`，`Velocity=(0,10001,0)`。 |
| S08 | prev.Velocity 的逐分量與總長度上限 | `Velocity=(20001,0,0)`，其他速度為 0。 |

已分別確認上述見證在基線拋出速度域 ContractException，對應變異則正常返回，故非等價變異。

目前超限測試只沿 X 軸同時設定 planar 與 Velocity，會被其他仍存在的條件擋住。需加入隔離每條限制的拒絕案例，以及正負 vertical、斜向 planar 與總速度邊界。

## 變異結果

**40 項：37 killed／3 survived。**

每項使用獨立記憶體來源副本。以下 kill 以 115 案例子集判定；S06–S08 再以完整 130 案例確認存活。Configure 與 glue 的結果屬 managed 投影。

| ID／變異 | 結果／主要殺死者 |
|---|---|
| R01 移除遲滯上限 Requires | killed；R3 遲滯拒絕測試。 |
| R02 Configure 刪除 Supports guard | **killed**；N1 新增欄位狀態斷言。 |
| R02b guard 延後至 `_config` 賦值後 | **killed**；同上。 |
| R03 local slack 2→1.5 | killed；N2 對角框架。 |
| R04 margin cap 16→32 | killed；絕對 20 邊長拒絕等案例。 |
| R05 遲滯常數 1→1e6 | killed；拒絕與掃描測試。 |
| R06 Supports 無條件 true | killed；支援組合與工作量拒絕測試。 |
| P01 motor transport→identity | killed；F5／F6、新增 N5。 |
| P02 orbit 移除 transport | killed；F6 解析解。 |
| P03 移除 planar 切向修正 | **killed**；新增 N5 固定見證。 |
| P04 snap 判斷乘 2 | killed；M04。 |
| P05 移除上升速度限制 | killed；M05。 |
| P06 空中使用 GroundAcceleration | killed；F5／F6。 |
| P07 停用陡坡截斷 | killed；原陡坡測試。 |
| P08 planar 改由 prev.Velocity 推導 | killed；坡度與 F5／F6。 |
| P09 pitch clamp→±89° | killed；原 orbit clamp 測試。 |
| P11 面向轉速乘 2 | killed；原轉速測試。 |
| P12 jump sqrt 移除 2 | killed；原 apex 測試。 |
| P13 Initial 加入絕對長度門檻 | killed；F3 極小方向。 |
| P14 移除第二次正交化 | killed；F3 近退化方向。 |
| P15 只檢查 yaw finite | killed；F7 pitch NaN。 |
| P16 負 ground.Distance 放寬至 −0.5 | killed；F7。 |
| B01 TangentOr 移除尺度縮放 | killed；F3 極大／極小方向。 |
| B02 移除 transport 長度恢復 | killed；F5 自由滑行。 |
| B03 buffer 端點 ≤→< | killed；F1 端點。 |
| B04 移除 Walk≤Sprint | killed；F4。 |
| B05 移除 prev Facing／速度等 guard 區段 | killed；新增 N4 等案例。 |
| B06 不回報 JumpStarted | killed；F1。 |
| B07 LevelRotation 改用 pitched Rotation | killed；F2。 |
| S01 移除新增速度域 guard | killed；新增 N4 超限測試。 |
| S02 移除 Grounded 組合檢查 | killed；新增 F7 grounded 測試。 |
| S03 移除 orbit Heading invariant 檢查 | killed；新舊 F7。 |
| S04 移除 orbit pitch invariant 檢查 | killed；新增 F7。 |
| S05 移除下降速度 clamp | killed；新增 N4 邊界測試。 |
| S06 移除 planar 歐氏長度上限 | **survived；130/130**。 |
| S07 移除 vertical 上限 | **survived；130/130**。 |
| S08 移除總 Velocity 上限 | **survived；130/130**。 |
| S09 移除 Facing Configure 檢查 | killed；新增 F7 glue 投影。 |
| S10 移除 Heading Configure 檢查 | killed；同上。 |
| S11 移除 stick 範圍檢查 | killed；同上。 |

S10 初次變異字串誤匹配導致編譯失敗，未計入 kill；修正為只刪 Heading 的 guard 後，重新編譯並由 NUnit 斷言殺死。

未重新執行 Warp FENCE 變異，也未將真正 Cinemachine 接線變異列入統計。

## 重跑指令

本輪 harness 僅存在記憶體，**沒有新的暫存專案或 log 檔可直接執行**。完整 fixture 重建需依上述投影範圍，沿用 r4 runner，加入 `SphericalMotorReviewR4Tests`，並更新 Configure 投影與 glue 投影；不可直接執行 r4 舊 binary 充作本輪結果。

以下是 N6／N7 的最小獨立重現。須在允許寫入 `%TEMP%` 的 session 執行；使用 .NET 9 重驗，因此結果應另記後端，不冒充本輪 CLR 實測。

```powershell
# 在 repo 根目錄執行；只寫入新建的 TEMP 目錄。
$reviewTemp = Join-Path $env:TEMP ("cosmimera-r5-repro-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $reviewTemp | Out-Null

$paths = @(
    'Assets/_Project/Core/Contracts/Contract.cs',
    'Assets/_Project/Core/Contracts/ContractException.cs',
    'Assets/_Project/Features/Player/Runtime/Model/SphericalMotor.cs'
)
foreach ($path in $paths) {
    $source = & git show "2539d74:$path"
    if ($LASTEXITCODE -ne 0) { throw "git show failed: $path" }
    [IO.File]::WriteAllText(
        (Join-Path $reviewTemp ([IO.Path]::GetFileName($path))),
        ($source -join [Environment]::NewLine))
}

$mathDll = Join-Path $env:TEMP `
    'cosmimera-r4-sol-4fc248e8bf27444b9b938751aebaafea/bin/Release/net9.0/Unity.Mathematics.dll'
Get-FileHash -Algorithm SHA256 -LiteralPath $mathDll

$project = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net9.0</TargetFramework>
    <DefineConstants>UNITY_EDITOR</DefineConstants>
    <Optimize>true</Optimize>
  </PropertyGroup>
  <ItemGroup>
    <Reference Include="Unity.Mathematics">
      <HintPath>$mathDll</HintPath>
    </Reference>
  </ItemGroup>
</Project>
"@
[IO.File]::WriteAllText((Join-Path $reviewTemp 'Probe.csproj'), $project)
[IO.File]::WriteAllText(
    (Join-Path $reviewTemp 'NuGet.Config'),
    '<configuration><packageSources><clear /></packageSources></configuration>')

$program = @'
using System;
using Cosmimera.Player;
using Unity.Mathematics;

static class Program
{
    static void Main()
    {
        Probe("N6",
            new float3(-0.0648168847f, 0.9838996f, -0.166554645f),
            new float3(0.8500662f, 0.153920412f, 0.5036825f),
            new float3(-9968.19f, -560.8229f, 566.2624f));
        Probe("N7",
            new float3(-0.144974574f, 0.5815f, 0.80052495f),
            new float3(-0.3284381f, 0.9019396f, 0.280416429f),
            new float3(930.007263f, -7974.81934f, 5961.32f));
    }

    static void Probe(string name, float3 from, float3 to, float3 planar)
    {
        var settings = MotorSettings.Default;
        settings.AirAcceleration = 1e-20f;
        var state = SphericalMotor.Initial(from, new float3(0, 0, 1));
        state.PlanarVelocity = state.Velocity = planar;

        Console.WriteLine($"{name} inputSq={math.lengthsq(planar):R} " +
            $"inputDot={math.dot(planar, state.Up):R}");
        var next = SphericalMotor.Step(
            settings, state, default, default, to, .02f);
        Console.WriteLine($"{name} outputSq={math.lengthsq(next.PlanarVelocity):R} " +
            $"outputDot={math.dot(next.PlanarVelocity, next.Up):R}");

        try
        {
            SphericalMotor.Step(settings, next, default, default, to, .02f);
            Console.WriteLine($"{name} NEXT_ACCEPT");
        }
        catch (Exception e)
        {
            Console.WriteLine($"{name} NEXT_REJECT {e.Message}");
        }
    }
}
'@
[IO.File]::WriteAllText((Join-Path $reviewTemp 'Program.cs'), $program)

$env:DOTNET_CLI_HOME = Join-Path $reviewTemp '.dotnet'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
dotnet run --project (Join-Path $reviewTemp 'Probe.csproj') -c Release
```

N9 可在同一專案分別套用表列三種刪除，再呼叫表列見證；基線應拒絕，對應變異應接受。N8 需引用 `AddLook` 的 managed 投影或另在真正 Unity 測試中驗證，不以數學式替代實際 glue 執行結果。

## 建議下一步

1. OPUS 修 N6，使合法輸出可再次 Step；加入斜向速度邊界與連續 Step 測試。
2. 統一 N7 的切向容差契約，補足 N9 三條獨立拒絕測試。
3. 為 AddLook 明訂可實現的累加域並保護狀態；由可執行 Unity 的 session 驗證真正 glue、Input System 與攝影機接線。
4. 保留 N3、N5、F7 的本次修正；Q-T6 繼續另行定案。

2026-11-06 · SOL · done · r5 **fail**：N3／N5／F7 fixed，N4 partially fixed；新增 N6 med、N7–N9 low；40 項變異 37 killed／3 survived · 下一步：OPUS 修速度域閉合與契約／測試缺口；SOL 複審，Unity 驗證另跑。