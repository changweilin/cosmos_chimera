# P1 第四輪對抗式複審＋P1-05 複審 · SOL

2026-11-06 · ChatGPT Sol（SOL）。總判定：**fail**。A1-R3／N1／N2 的實作修正通過；P1-05 的 F1–F6 修正通過，F7 **partially fixed**。新增 N3（low，Configure 測試缺口）、N4（med，合法有限速度產生 NaN）、N5（low，切向修正變異存活）。Q-T6 不列入否決理由。

對象：開始審查時的 `main` HEAD **`fc2ba8b`**。從該 commit 匯出 Planet／Player／所需 Core 原始碼至 `%TEMP%`，不使用其他 agent 正在變更的工作樹內容。結束前 HEAD 已由其他工作推進至 `3702812`，本報告仍只審 `fc2ba8b`。已讀 AGENTS、CLAUDE、契約／測試／文件規則、roadmap、ai-agents、r3／P1-05 報告及兩則指定修正交接。以下 `Planet/`、`Player/` 均相對於 `Assets/_Project/Features/`；行號對應該 commit。依本次限制，只新增本報告，不更新 roadmap。

## 方法與限制

未啟動 Unity／Unity.exe。沿用 r3 的 repo 外 harness 方法：Windows x64、SDK 9.0.304、.NET 9 Release、`UNITY_EDITOR`，引用真實 Mathematics 1.3.3 DLL 與 NUnit；沒有替換 float3／math。Mathematics DLL SHA-256：`48049AFDD1E1FBA73BFFAE24A6DAB5E8B32BC63E387EC8BCEA53CB2A5382D12E`。

反射 runner 執行十組 fixture 的 `[Test]` 與全部 `[TestCase]`。其中 **120 個為純規則案例，另 1 個為 Configure managed-prefix 投影**，不可合稱 Unity EditMode 執行結果。原始快照基線為 **119 pass／2 fail**，失敗僅 Q-T6：提交預設／oct8 雜湊分別為 `B7C5CA249FD8C22D`／`AF9DD87B0574B16F`，CLR 為 `383CA51ADC74C561`／`B94911EBA33AB6D5`。只在暫存副本校準這兩份快照後，基線與變異全部還原後均 **121/121**；不把原有快照差異算作 kill。

投影的範圍必須明說：`PlanetTerrain.Configure` 從方法開頭到 `_tree = new ChunkQuadtree(config, lod)` 逐字擷取；以極小 GameObject／Transform 容器承接 N1 測試，`Vector3 before` 改成 float3。兩個 guard 變異都在這個範圍內拋錯，永遠不會走到實際 transform 更新。此結果驗證 managed 檢查順序與斷言缺口，**沒有執行真正 MonoBehaviour、native buffer、Unity 生命週期或物理**。其餘測試副本僅調整 TestContext 輸出與快照路徑。

沒有執行 `PlayerCameraRigTests`、其他 PlayMode、Burst、IL2CPP、ARM64、真實輸入與攝影機畫面；world-up 接線採靜態查核，LevelRotation 採真實數學測試。實作者先前的 Unity 通過紀錄不充作本輪實測。

重跑目錄：`%TEMP%/cosmimera-r4-sol-4fc248e8bf27444b9b938751aebaafea`。設 `DOTNET_CLI_HOME=<目錄>/.dotnet`，執行 `dotnet run --project <目錄>/Probe.csproj -c Release -- tests`／`-- probe`；`mutate.ps1` 重跑 29 項變異。保留 `source.zip`、原始／校準／還原基線、`mutations.json`、`probes.log`、`guard-mutation-witness.log`、`edges.log`、`tangent-baseline.log`、`tangent-P03.log`。`edges/Probe.csproj` 執行長滑行與緩衝組合探針，加 `-- tangent` 執行切向掃描。

## A · A1-R3、N1、N2 複審

| 項目 | 判定 | 證據 |
|---|---|---|
| A1-R3 | **fixed** | `Planet/Runtime/Model/ChunkLodSettings.cs:46`、`:64` 限定遲滯 ≤1；r3 的 `1e6` 在 D4／5／6 均先拋 ContractException。同一條六面掃描改用最大合法遲滯，結果見下表；不再累積整球。R01／R05 分別驗證拒絕與工作量測試能 kill。 |
| N1 | **fixed（實作）** | `Planet/Runtime/Model/ChunkQuadtree.cs:69` 的 Supports 共用三項 workload 條件；建構子 `:55` 與 `Planet/Runtime/PlanetTerrain.cs:114` 均使用它。Configure 的 Pre `:96`–`:99` 已同步支援域，`:106`–`:115` 在任何欄位賦值之前檢查，LOD 推導只寫 local。原組合反例現在是明確的違約輸入。測試仍未保護順序，見 N3。 |
| N2 | **fixed** | `Planet/Tests/EditMode/PlanetReviewR3RegressionTests.cs:106`–`:118` 使用 normalize(1,1,1)、正負 M world，要求 local 超過 1.7M，再 round-trip 並直接測 2M local。R03（slack 2→1.5）由此測試殺死。 |

r3 掃描：R=640、q=32、split=2、margin=0；相機依 face／x／y 次序位於 `641.7*FaceToDirection(face,-1+(x+.5)*2/n,-1+(y+.5)*2/n)`，`n=2^(D−3)`，最後移至 (10000,0,0)。

| D／步數 | r3 無上限時最終葉數 | r4 合法上限 h=1：峰值／掃描末／遠離後 |
|---|---:|---:|
| 4／24 | 1,536 | 429／429／6 |
| 5／96 | 6,144 | 552／522／6 |
| 6／384 | 24,576 | 705／672／6 |

Supports 組合探針亦通過：R=320 的 q=2／4／32 全支援；R=640 的 q=2 不支援、q=4／32 支援；R=1280 的 q=2／4 不支援、q=32 支援；R=8000 的 q=2／4／32 均不支援。測試中的絕對 20 邊長拒絕案例 `Planet/Tests/EditMode/PlanetReviewR3RegressionTests.cs:58` 能獨立殺死 cap 16→32，修補 r3 的 N03 存活變異。

## P1-05 · F1–F7 複審

| 項目 | 判定 | 證據 |
|---|---|---|
| F1 | **fixed** | `Player/Runtime/Model/JumpBuffer.cs:39` 先判有效期且含端點；`Player/Runtime/SphericalCharacter.cs:134`–`:138` 先查詢再 Step，僅在 JumpStarted 時消耗；`Player/Runtime/Model/SphericalMotor.cs:278`–`:283` 明確回報起跳。純規則組合探針 t=.14／.15 起跳且消耗、t=.16 不起跳且著地。`Player/Tests/EditMode/SphericalMotorReviewTests.cs:23`、`:40` 與 B03／B06 保護端點及事件；實際物理落地接線仍未執行。 |
| F2 | **fixed** | `Player/Runtime/PlayerCameraRig.cs:69`、`:72` 建立獨立 CameraWorldUp 並交給 brain；`:125`–`:126` 分別寫 pitched pivot 與 LevelRotation。`Player/Runtime/Model/ViewOrbit.cs:95` 回傳無 pitch 旋轉；review test `:63` 的 0／±80° 通過，B07 kill。PlayMode `Player/Tests/PlayMode/PlayerCameraRigTests.cs:75`–`:78`、`:94` 確實斷言 brain up 與極點；本輪未跑，亦未驗證將 brain 改回 pivot 的實際 PlayMode kill。 |
| F3 | **fixed** | `Player/Runtime/Model/SphericalMotor.cs:191`–`:193`、`:337`–`:352` 先按最大分量縮放、按角度退化、二次正交化。review test `:79`、`:90`、`:97` 的極大／極小方向與 2000 個近退化方向均通過；B01／P13／P14 能 kill。 |
| F4 | **fixed（設定域）** | `Player/Runtime/Model/SphericalMotor.cs:85`–`:93` 限制設定上界及 Walk≤Sprint；`Player/Runtime/SphericalCharacter.cs:86` 拒絕無效序列化設定。review test `:121`、`:146` 通過，原 MaxValue gravity 與 Walk>Sprint 已拒絕；B04 kill。這沒有封住 prev 速度的合法域，另見 N4。 |
| F5 | **fixed** | `Player/Runtime/Model/SphericalMotor.cs:241`–`:245` 恢復 transport 前長度；review test `:168` 通過，B02 kill。另以 seed=77、24 條斜向大圓各滑行 100,000 步，初速 5、AirAcceleration=1e−20、無地面／輸入、每步重設垂直速度；末速 4.996581–4.998382（−0.0684% 至 −0.0324%），未重現原加速，仍有小幅捨入損耗。 |
| F6 | **fixed** | `Player/Tests/EditMode/SphericalMotorReviewTests.cs:195`、`:210` 比對 60° 混合 heading 的獨立解析解；P01／P02 移除 motor／orbit transport 均由新增測試殺死。切向修正的 P03 是另一條保護缺口，見 N5。 |
| F7 | **partially fixed** | model 公開欄位已逐一補 DbC 並引用型別 invariant；`Player/Runtime/Model/SphericalMotor.cs:219`–`:225`、`Player/Runtime/Model/ViewOrbit.cs:56`–`:61` 補多項 prev 檢查，B05／P15／P16 kill。但 Step 仍接受 Grounded=true、VerticalSpeed=1 的違約 prev（型別 invariant 在 motor `:139`–`:140`）；orbit 的 Rotation／LevelRotation `:82`、`:97` 只查 Up，Heading=NaN 時靜默回傳 identity。`Player/Runtime/SphericalCharacter.cs:57` 的 Facing 與 `Player/Runtime/PlayerCameraRig.cs:41` 的 Heading 仍未檢查 Configure 生命週期；後者 AddLook `:93`–`:95` 未檢查明文 stick 範圍，PlayMode test `:68` 反而傳 −1000。PlayerInputDriver 缺 action 仍走 FindAction 例外（`:36`–`:39`），與交接所述未處理項一致。 |

## 新發現

### N3 · low · Configure「任何狀態變更之前」測試保護不到欄位

位置：`Planet/Tests/EditMode/PlanetReviewR3RegressionTests.cs:83`–`:94`；`Planet/Runtime/PlanetTerrain.cs:114`–`:122`、`:136`。

R02 只刪 Configure 的 Supports Requires，保留 quadtree 建構子的檢查；R02b 只把同一 Requires 移到 `_config = config` 之後。兩者均 **121/121**。managed-prefix 見證在失敗呼叫後讀取 `_config.IsValid`：基線 false，兩變異 true；R02 在建構子拋 ContractException，R02b 在延後的 Requires 拋同型例外。

原測試只比較 transform.position，而它直到建立 quadtree 與 index buffer 之後才寫入；兩種錯誤順序本來就不會移動 transform。這一點可由實際來源的控制流程直接確認，不依賴容器模擬 Unity 行為。**目前實作順序正確**，缺陷是測試的保護範圍；不能把 Supports 自身改成 true 所造成的失敗，當成「Configure 漏查」被測出的證據。

建議以測試可讀的狀態或反射斷言 `_config`／`_height`／`_viewer`／`_tree` 等保持未設定，並分別驗證刪除與延後 guard；保留 transform 斷言。

### N4 · med · 有限且切向的合法 prev 速度使新增長度恢復產生 NaN

位置：`Player/Runtime/Model/SphericalMotor.cs:199`–`:225`、`:241`–`:244`、`:355`–`:364`。

重現：Default settings；`s=Initial(Y,Z)`，設 `s.PlanarVelocity=s.Velocity=(2e19,0,0)`，其餘不變；`Step(settings,s,default,default,Y,.02)`。輸入速度每個分量有限、與 Up 正交、未著地，符合明文 prev invariant；全部 Requires 通過，但結果 PlanarVelocity／Velocity 都是 NaN。1e18／1e19 正常，2e19／1e20／3e38 重現。

原因是兩次 `math.length` 先平方溢位成 infinity，恢復比例變成 infinity/infinity。只限制 MotorSettings 不能約束傳入狀態，Step 的 Post 還明確允許保留高於 SprintSpeed 的既有速度。這是 API 支援域反例，沒有宣稱預設正常遊玩會達到此速度。

建議明訂並檢查 prev planar／vertical／整體速度的可實現上界，或對長度、MoveTowards 差值與合成速度採一致的尺度安全運算。補這個有限且切向的邊界，避免只測設定 Max* 與 NaN 輸入。

### N5 · low · P03 切向修正變異在本輪後端仍存活

位置：`Player/Runtime/Model/SphericalMotor.cs:238`；`Player/Tests/EditMode/SphericalMotorReviewTests.cs:186`、`:203`–`:206`。

只刪 `vTan -= up * dot(vTan,up)`，全部 **121/121**；與交接的「M03 勉強被擋」不同。本機 60° 解析案例的 abs(dot) 基線／變異同為 `1.1920929e−7`，所以該案例無法辨識。

不是等價變異：seed=77 的獨立方向掃描，每個速度 100,000 組、dot(from,to)>0、初始 facing 由 Initial 產生、無輸入／地面、AirAcceleration=1e−20；速度 1000 的基線最大 abs(dot)=`9.727478e−5`，變異為 `5.187988e−4`，越過 MotorState 的 1e−4 Post。變異見證 from=`(-.77640027,.15293439,.611403)`、to=`(-.47216615,-.8463181,-.24658595)`、heading=`(.37746897,.8897065,.25678694)`，輸入 planar 的 abs(dot)=`1.5258789e−5`，仍在 invariant 內。既有速度 1000 符合目前 prev 契約，與 N4 的溢位尺度不同。

建議固定這個見證或更強的合法切向誤差案例，明確量測移除修正後的違約；不要依賴單一後端剛好跨過 1e−5 的捨入差。沒有據此否定目前保留修正的實作。

## 變異結果

每項獨立套用、還原；**29 項：26 killed／3 survived**。統計含兩個 Configure prefix 變異，並非 29 項 Unity 執行。下表 P 編號對應前次 P1-05 的 M 編號；M10 是佔位數值，本輪不列。舊 M13 的程式形狀已移除，改以重新加入絕對長度門檻表達同類缺陷。

| ID／暫存變異 | 結果／殺死者 |
|---|---|
| R01 移除遲滯上限 Requires | killed；R3 A1R3 拒絕測試。 |
| R02 Configure 漏查 Supports | **survived**；prefix 121/121，見 N3。 |
| R02b Configure guard 延後至第一個欄位賦值後 | **survived**；prefix 121/121，見 N3。 |
| R03 local slack 2→1.5 | killed；R3 N2 對角框架。 |
| R04 margin cap 16→32 | killed；R3 絕對 20 邊長測試等 5 個案例。 |
| R05 遲滯常數上限 1→1e6 | killed；R3 拒絕與跨位置掃描測試。 |
| R06 Supports 無條件 true | killed；R2／R3 支援組合與原工作量拒絕案例。與 R02 不同。 |
| P01 motor transport→identity | killed；新增 F5／F6。 |
| P02 orbit 移除 transport | killed；新增 F6 orbit 解析解。 |
| P03 移除 planar 切向修正 | **survived**；121/121，見 N5。 |
| P04 snap 判斷乘 2 | killed；新增 M04 snap 範圍。 |
| P05 移除上升速度限制 | killed；新增 M05。 |
| P06 空中用 GroundAcceleration | killed；新增 M06、F5／F6。 |
| P07 停用陡坡向內截斷 | killed；原陡坡測試。 |
| P08 planar 由 prev.Velocity 推導 | killed；原坡度測試及新增 F5／F6。 |
| P09 pitch clamp→±89° | killed；原 orbit clamp 測試。 |
| P11 面向轉速乘 2 | killed；原轉速測試。 |
| P12 jump sqrt 移除 2 | killed；原 apex 測試。 |
| P13 Initial 加入 lengthsq(facing)>1e−20 門檻 | killed；新增 F3 的 1e−20／1e−38。 |
| P14 移除第二次正交化 | killed；新增 F3 近退化方向。 |
| P15 只檢查 yaw finite | killed；新增 F7 pitch NaN。 |
| P16 負 ground.Distance 放寬至 −0.5 | killed；新增 F7 的 −0.01。 |
| B01 TangentOr 移除按最大分量縮放 | killed；新增 F3 的 5 個案例。 |
| B02 移除 transport 長度恢復 | killed；新增 F5 自由滑行。 |
| B03 buffer 端點 ≤ 改 < | killed；新增 F1 端點。 |
| B04 移除 Walk≤Sprint | killed；新增 F4。 |
| B05 移除新增 prev Facing／速度 Requires | killed；新增 F7。 |
| B06 不回報 JumpStarted | killed；新增 F1。 |
| B07 LevelRotation 改用 pitched Rotation | killed；新增 F2 ±80°。 |

brain.WorldUpOverride 改回 pivot 的接線變異需要真正 Unity／Cinemachine PlayMode，本輪未執行，**不計 kill**。沒有重新宣稱 Warp FENCE 的兩個已知存活變異能被 CLR 測出。

## 建議下一步

1. OPUS／SONNET 先封住 N4 的 prev 數值域，補合法極限與拒絕邊界；完成 F7 尚缺的 invariant／生命週期／輸入範圍檢查，讓測試遵守同一契約。
2. 補 N3 的內部狀態斷言與 N5 的確定見證，各自重跑單獨變異；A 的實作修正可保留，不必改動遲滯／margin 政策。
3. 由實作者在可啟動 Unity 的工作階段執行真正的 Configure 測試、brain 接線反向變異、緩衝到期後落地及落地當步消耗；補實際第一／第三人稱切換與物理驗收。Q-T6 繼續由使用者定案。

2026-11-06 · SOL · done · r4 fail：A1-R3／N1／N2 實作 fixed；P1-05 F1–F6 fixed、F7 partially fixed；新增 N3 low／N4 med／N5 low；29 項變異 26 killed／3 survived · 下一步：OPUS／SONNET 修 N4 與 F7、補 N3／N5；SOL 複審，Unity 驗證由實作者另跑。
