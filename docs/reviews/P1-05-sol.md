# P1-05 球面角色控制器對抗式審查 · SOL

2026-11-06 · ChatGPT Sol（SOL）。判定：**fail**。

對象：分支 `p1/terrain-mesh-height`，`b14c0624ecbcd7633a25e87f3f575713c55fea44`。下列程式路徑相對於 `Assets/_Project/Features/Player/`。已讀 AGENTS／CLAUDE、契約與測試規則、planet-tech、P1-05 項目與交接、Q-T5、前輪報告。依本次單檔輸出限制，只新增本報告，不修改 roadmap；工作期間其他來源的 Planet、Rendering、Data 變更均未觸碰。

## 方法與限制

**沒有啟動 Unity／Unity.exe**。純 C# 原始碼與兩份 EditMode fixture 複製到 `%TEMP%/cosmimera-p1-05-sol-f264e8`，以 Windows x64、SDK 9.0.304、.NET 9 Release、`UNITY_EDITOR` 編譯，引用專案實際的 Mathematics DLL 與 NUnit，沒有替換 float3／math。DLL SHA-256：`48049AFDD1E1FBA73BFFAE24A6DAB5E8B32BC63E387EC8BCEA53CB2A5382D12E`。

反射 runner 執行 `[Test]` 與所有 `[TestCase]`：基線 **21 pass / 0 fail**。16 項變異各自套用、測完還原：**11 存活、5 被殺死**。最終再次 **21/21**，兩份 model 與兩份 fixture 的暫存檔 SHA-256 均與 repo 原檔相同。存活僅指這 21 個 EditMode 案例，**沒有宣稱通過 PlayMode**。

F1 的 harness 逐字擷取 `SphericalCharacter.cs:135–141`，只提供時間、ground 與 motor 狀態；不模擬 SphereCast／Rigidbody，也沒有 mock Unity 物理。F2 以真實 `ViewOrbit.Rotation()` 計算 pivot up，再核對本機 Cinemachine 的讀取程式。Unity glue 其餘部分為靜態查核；本輪無實際物理、輸入裝置、Cinemachine blend 畫面、Mono／IL2CPP／ARM64 證據，不把先前交接的實測當成本輪結果。

重跑：PowerShell 設 `$p = Join-Path $env:TEMP 'cosmimera-p1-05-sol-f264e8'`、`$env:DOTNET_CLI_HOME = $p`，再執行 `dotnet run --project "$p/Probe.csproj" -c Release -- tests`；參數 `edges`／`long` 執行本文數值探針。`powershell -NoProfile -ExecutionPolicy Bypass -File "$p/mutations.ps1"` 重跑變異；`witness.ps1` 重跑移除 transport 的反例。原始輸出保存在同目錄的 `edges.txt`、`long.txt`、`mutations.txt`、`witness.txt`。

## 通過的查核與驗收界線

- `FromTo` 的 `(cross(from,to), 1+dot(from,to))` 正規化，在目前呼叫端要求 **dot > 0** 的範圍內，是正確的最小旋轉。60°、混合方向 heading 的獨立解析解相符，見 F6。
- `Step` 對 dt=0、0.100001、float.MaxValue、NaN 皆拋 `ContractException`；dt=0.1 正常，靜止空中得到 `(0,-0.98100007,0)`。近反向 `up=normalize((0.0001,-1,0))` 亦被拒絕。因此本介面不承諾處理近反向 transport；它沒有把此退化情況靜默轉成翻轉。文件的「within 90°」宜明寫嚴格小於 90°。
- 原有 60° 陡坡禁止向內推進、±25°／30° 可行坡滿速與跳躍高度測試均通過。改回從整體 Velocity 推導 planar velocity 會被坡度回歸殺死，見 M08。
- Player runtime 沒有以 `Vector3.up`／`Physics.gravity` 取得 up 的執行碼。移動右向量是 `cross(up,forward)`，正 yaw 亦朝此方向；Q-T5 的 East/North 決策不阻塞這兩個運算。

長程探針：兩條各 **100,000 步**、每步角度 0.00025 rad、合計約 3.98 圈的大圓，皆穿越南北極；旋轉軸分別為 `(0,0,1)` 與 `normalize((1,0,1))`。由解析旋轉指定每步 up，orbit 零 look，motor 使用預設設定、平地、dt=0.02、向前走；這是 transport 壓力測試，不是物理繞行。Facing 變化用 `atan2(|cross|,dot)` 量測，避免 acos 在微小角度捨入為零。

| 全程最大值 | 軸 Z | 斜軸 |
|---|---:|---:|
| `abs(dot(Facing,up))` | 1.192093e-7 | 8.940697e-8 |
| `abs(dot(Heading,up))` | 1.192093e-7 | 8.940697e-8 |
| Facing／Heading 長度誤差 | 1.192093e-7 | 1.192093e-7 |
| `abs(dot(PlanarVelocity,up))` | 7.152557e-7 | 7.152557e-7 |
| 穩速後 planar 速度誤差（m/s） | 9.536743e-7 | 9.536743e-7 |
| 單步 Facing 變化（度） | 0.014440726 | 0.014447361 |

這兩條正常行走路徑沒有 heading 漂移至非切向、失去單位長或翻轉；不代表初始極端方向、自由滑行、所有碰撞接縫及攝影機 world up 均正確。

## Findings

### F1 · med · jump buffer 先使用後到期，落地起跳也沒有正確消耗

位置：`Runtime/SphericalCharacter.cs:135`、`:138`；承諾在 `:101`。

重現：預設設定、up=Y、Facing=Z、先前未著地且 VerticalSpeed=0，press 時刻 0，至 time=0.16 才收到 `Hit=true, Normal=Y, Distance=0`。擷取的實際控制流程先把過期 latch 交給 Step，得到 **VerticalSpeed=4.8522162、Grounded=false**，之後才清 latch。預期 buffer 已超過 0.15 s，這一步不應起跳，應著地且 VerticalSpeed=0。

另一組同條件改 time=0.10：正常緩衝起跳後 **latch 仍為 true**；預期 false。原因是 `wasGrounded=false`、起跳後也 false，`wasGrounded && !Grounded` 不能辨識「這步落地並起跳」。它也會把單純走出地面誤當消耗事件。建議先判斷有效期限，再送出 Jump；讓 motor 明確回報 JumpStarted，依實際起跳消耗。補 0.14／0.15／0.16 s、落地當步起跳與未起跳離地測試。

### F2 · med · pitched pivot 被當成當地 world up

位置：`Runtime/PlayerCameraRig.cs:63`、`:104`；相關 `Runtime/Model/ViewOrbit.cs:67`。

重現：`orbit=Create(Y,Z)`，`Advance(Y,0,80)`。真實 Rotation 對 local Y 的結果為 **`(0,0.17364824,-0.98480767)`**，與當地 up 的 dot=**0.17364824**，相差 **80°**。rig 將這個 rotation 寫給 pivot，同時把 pivot 指派給 brain.WorldUpOverride。套件 `com.unity.cinemachine@f3f96bcb59af/Runtime/Behaviours/CinemachineBrain.cs:361` 明確回傳 `WorldUpOverride.transform.up`；所以傳入 brain 的 up 確實是上述 pitched 向量。預期 world up=Y、dot≈1，與 look pitch 無關。

這使 Cinemachine 的參考 up 隨抬頭／低頭改變；本輪未量測其 blend／damping 的畫面後果。建議給 brain 獨立且不帶 pitch 的當地 up transform；視線 pivot 保留 pitch。新增 pitch=0／±80°、切換人稱與跨極點時的 `brain.DefaultWorldUp` 斷言，而非只要求攝影機 up 還在同一半球。

### F3 · med · 方向投影不是尺度安全，Initial 亦可違反切向 Post

位置：`Runtime/Model/SphericalMotor.cs:141`、`:144`、`:257`；連帶 `ViewOrbit.Create`。

重現 A：up=Y、Initial facing=Z；預設 settings、dt=0.02、平地、Move=(0,1)，把 ViewForward 設為 `(s,0,0)`。s=1 得 **PlanarVelocity=(0.79999995,0,0)**；s=1e20 或 1e-20 則得 **`(0,0,0.79999995)`**。預期同方向不因長度而轉 90°。這些皆符合「ViewForward finite, any length」；平方長度溢位／過小使 TangentOr 退回 Facing。同樣以 facing=`s*Z` 呼叫 Initial，s=1e20／1e-20 皆拋 `ContractException`，但輸入有限且與 up 垂直，符合明文 Pre。

重現 B：`Initial((0.38094947,0.8153129,-0.43605313), (38.196377,81.48646,-43.600533))` 正常回傳，但 **dot(Facing,Up)=-0.00035355054**；預期絕對值 ≤1e-4。輸入非平行，亦通過內部角度門檻；單次投影後直接 normalize 放大了消去誤差。建議方向先安全縮放／正規化，再投影與二次正交化，集中在 Player 可合法呼叫的共用 helper；補跨尺度與近退化但仍合法方向。Planet 的安全縮放實作可作政策參考，不可直接跨 feature 存取其 internal 型別。

### F4 · med · IsValid 的合法域不能保證有限輸出或速度 Post

位置：`Runtime/Model/SphericalMotor.cs:54`、`:153`、`:210`。

重現 A：Default 只改 `Gravity=float.MaxValue, JumpHeight=1`，IsValid=**true**；`prev=Initial(Y,Z)`、平地、Jump=true、dt=0.02，得到 **Velocity=(NaN,+∞,NaN)**。預期 MotorState 的 Velocity finite。數學上的 `sqrt(2*g*h)` 約 2.61e19，結果本可由 float 表示，但乘積先溢位，`0*∞` 又污染其他分量。其他速度／加速度的平方與差值也沒有安全範圍保證。

重現 B：Default 改 `WalkSpeed=10, SprintSpeed=1`，IsValid=**true**；同樣 Initial、平地、Move=(0,1)、ViewForward=Z、dt=0.1，得到 **|PlanarVelocity|=4**；明文 Post 上限是 `max(0,1)=1`。建議明訂可實現的設定域與中間運算策略，驗證不支援的組合；Walk／Sprint 的大小關係須加 Pre，或將 Post 上限涵蓋兩者。以合法極限與各違約組合測試，不只測 MaxSlope=90。

### F5 · low · 無控制加速度修正時，平行傳遞累積增加速度

位置：`Runtime/Model/SphericalMotor.cs:175`、`:177`。

重現：Default 改 `AirAcceleration=1e-20`，`s=Initial(Y,X)`、PlanarVelocity=5X；100,000 步依序給 `up_i=(sin(i*0.00025),cos(i*0.00025),0)`，無 ground、無 input、dt=0.02。輸出 planar 速度由 **5 增至 5.014238 m/s（+0.28476%）**，最終 dot(PlanarVelocity,Up)=0。預期最小旋轉保持長度，極小的零目標加速度只能減速。預設行走會由 MoveTowards 校正到目標速度，因此前述正常長走未顯露此誤差。

建議 transport 後在非零速度情況恢復原 planar 長度，再套加速度與碰撞限制；零向量單獨處理，避免除零。補自由滑行的長程速度不變式；這是數值誤差，沒有據此宣稱預設行走會明顯加速。

### F6 · med · 驗收相關測試允許完全移除 transport

位置：`Tests/EditMode/SphericalMotorTests.cs:64`、`Tests/EditMode/ViewOrbitTests.cs:48`；實作 `SphericalMotor.cs:175`、`ViewOrbit.cs:56`。

重現：M01 將 motor transport 改 identity，或 M02 將 orbit 的 transported 直接設為 Heading；各自仍 **21/21**。但給 `from=Y, to=(sin60°,cos60°,0), heading=normalize((1,0,1))`，零 look／input、無 ground、dt=0.02、AirAcceleration=1e-20、初速=5*heading：最小旋轉解析解為 **`(0.35355338,-0.61237246,0.70710677)`**，速度應保持 5。

M01 實際 Facing=**`(0.22360677,-0.38729835,0.89442724)`**、速度 **3.952847**；M02 的 orbit Heading 同樣變成約 **`(0.22360678,-0.38729835,0.89442724)`**。方向誤差約 **18.435°**。未變異基線兩個 heading 均吻合解析解，速度=5，故這是測試缺口，不能誤報成基線目前缺少 transport。

建議加入與路徑不平行的混合 heading／velocity、較大的合法單步轉向、斜向大圓及可解析 holonomy；直接比較最小旋轉與速度不變式。400 步短弧及 1 m 極圈目前的容差不能保護這項關鍵設計。

### F7 · low · DbC 文件與違約檢查未逐成員落實

位置：`Runtime/Model/SphericalMotor.cs:16`、`:164`，`ViewOrbit.cs:17`、`:67`；glue 的 `SphericalCharacter.cs:105`、`PlayerCameraRig.cs:77`。

重現 A：`s=Initial(Y,Z); s.Facing=(NaN,NaN,NaN)`，再以預設設定、平地、零 input、up=Y、dt=0.02 呼叫 Step，**正常回傳 NaN Facing**；預期依 `prev satisfies invariant` 的 Pre 拋 `ContractException`。現有 Requires 只驗 prev.Up。`default(ViewOrbit).Rotation()` 也**回傳 identity**，未依「initialised」Pre 拒絕。這些是違約輸入未被檢查，不是合法呼叫反例。

重現 B：逐一列出 model 的 public 欄位／常數：Motor 23、Orbit 5，共 **28 個**；皆有 summary，**0 個有自己的 Pre／Post／Side effects**。預期按 code-contracts 的「每個 public/protected 成員」補齊，或明確引用型別 invariant。測試 fixture／Test 方法豁免不適用 runtime 成員。

靜態查核亦發現 SetInput 沒有檢查所寫的 move／viewForward Pre，AddLook 的「delta finite」未指明三個參數各自範圍且沒有 Requires，ToggleView／Facing／Heading 未檢查 Configure 生命週期。PlayerInputDriver 的 action 存在條件交由 FindAction 例外處理，未用 Contract 驗證。建議先明訂各成員條件，再逐條檢查與補違約測試；本輪沒有執行這些 Unity 呼叫，不推定其實際例外種類。

## 變異查核

以下僅改暫存副本；「存活」均為 **21 pass / 0 fail**，「殺死」均為 **20 pass / 1 fail**。M10 是佔位數值的變動，存活本身不構成缺陷，也不建議為平衡數字寫死精確值。

| ID | 單獨變異 | 結果／缺口 |
|---|---|---|
| M01 | motor FromTo → identity | **存活**；F6 解析探針可辨識 |
| M02 | orbit 移除 FromTo，只投影舊 Heading | **存活**；F6 解析探針可辨識 |
| M03 | 移除 transported planar velocity 的切向修正 | **存活**；缺直接切向不變式 |
| M04 | 兩處 SnapDistance 判斷改成 2 倍 | **存活**；缺 snap 範圍兩側與邊界 |
| M05 | 移除 grounded 的 `vUp <= 0.5` | **存活**；起跳後的近地上升沒有 ground-hit 測試 |
| M06 | 空中也使用 GroundAcceleration | **存活**；缺 air-control 加速度案例 |
| M07 | 停用陡坡向內速度截斷 | 殺死；向牆速度 z=0.16，應 ≤1e-4 |
| M08 | planar 由 prev.Velocity 取代 prev.PlanarVelocity | 殺死；坡度回歸速度約 0.482746，應約 5 |
| M09 | pitch clamp 改成 ±89° | 殺死；clamp 測試得到 89，應 80 |
| M10 | 預設 GroundAcceleration 40 → 20 | **存活**；佔位數值，不單獨判缺陷 |
| M11 | 面向轉速乘 2 | 殺死；單步轉 28.800005°，應約 14.4° |
| M12 | jump sqrt 內移除係數 2 | 殺死；apex 約 0.634801，應約 1.2 |
| M13 | Initial 退化比較的尺度下限 1e-30 → 1e-20 | **存活**；缺小尺度有限方向 |
| M14 | TangentOr 移除第二次正交化，只回傳已 normalize 的 t | **存活**；缺近退化／一般斜向精度案例 |
| M15 | Advance 只檢查 yaw finite，移除 pitch finite | **存活**；現有 NaN 案例只給 yaw |
| M16 | ground.Distance Pre 下限 0 → -0.5 | **存活**；現有負距離案例只有 -1 |

尚缺 PlayMode 的到期緩衝／落地消耗、第一／第三人稱切換與 brain up、縮放／偏心 capsule、近坡度界線及真實面邊接縫接觸測試。不能以本輪 CLR 結果替代這些物理／畫面驗收。

## 交接

- 2026-11-06 · SOL / ChatGPT Sol · done · P1-05 審查 fail；正常長走 heading 穩定，但重現 F1–F5、DbC 缺口，16 項變異有 11 項存活 · 下一步：OPUS／SONNET 修 F1–F5、F7 並補 F6 與變異對應測試；SOL 複審，Unity 可用後由實作者補 PlayMode／攝影機驗收。
