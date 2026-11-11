結論：建議採 h=1700 mm、k=775‰，R=320 的遠距上限為 25597 mm；幾何門檻可下修，但須同步補齊射程收斂、衛星尺寸對映與測試，原生 Unity 使用域仍待驗證。

# Q-D1 · SOL 提案與 R=320 幾何使用域稽核

日期：2026-11-11。對象：HEAD `cc97a72908451a2c08029b482a1e9ecd507170f1` 加當時工作樹；既有未提交修改保留。依使用者授權只新增本報告，不改 roadmap／open-questions／程式／資料、不 commit、不開 sub-agent。

已讀 AGENTS.md、CLAUDE.md、code-contracts、docs-and-progress、testing、roadmap 索引及 P1-10／P2-04、ai-agents、Q-D1、derived-stats、combat、planet-tech〈地平線〉、planet-world〈BOSS 尺寸〉與 ADR-0001。2026-11-11 的 R=320 實玩、地平線公式、R≥640 保持 36 m、全部遠距與 dash 受限、melee 判定體不變，均作為既定前提。

## A. 係數與取整提案

建議在 `Data/balance.json#ranged_distance` 增加整數 `horizon_factor_permille: 775`、`horizon_eye_height_mm: 1700`，保留 `cap_mm: 36000`、現有 STR 係數、`DerivedStats` 與其黃金向量。

- h=1700 來自 planet-tech／derived-stats 的共同眼高基準；固定為戰鬥平衡的**基準眼高**，不讀當幀攝影機、地形高度或宙獸身高，避免視角／體型切換改變射程。
- **與表現層的差異必須明示**：`Features/Player/Runtime/PlayerCameraRig.cs:21,231` 的預設 pivot 眼高其實為 1.6 m。本提案建議 h 表示上述設計基準，並非聲稱目前實體攝影機就是 1.7 m；h 與 k 一併交使用者確認。若之後改採 1600 mm，k 的整數下界為 795‰；直接保留 775‰ 會讓 R=640 只剩 35094 mm（由同公式推導）。本次不提議順便改攝影機。
- R=640、h=1.7 m 的精確地平線為 46.678581812 m；既有 36 m 留下 `1−36/46.678581812=22.8768343%` 餘量。775‰ 保留約 22.5%，貼近原意，也高於保住 36 m 所需的最低係數。770‰ 實際只得 35942 mm，不能使用。
- 地平線沿用使用者指定的 `sqrt(2Rh+h²)`（切線長）；不換成地表弧長公式。cap 套在既有球面路徑長度語意上，是平衡限制，不能代替視線／遮蔽檢查。

以下由新暫存 harness 的 `Qd.Numbers` 計算；地平線欄僅供閱讀，演算法不使用該欄的顯示取整。

| R（m） | horizon（m，顯示至 6 位） | floor(k×horizon)（mm） | 最終 cap（mm） |
|---:|---:|---:|---:|
| 320 | 33.028624 | 25597 | 25597 |
| 640 | 46.678582 | 36175 | 36000 |
| 960 | 57.156714 | 44296 | 36000 |
| 1280 | 65.991590 | 51143 | 36000 |

固定 h=1700 時，保留 R≥640 的必要且充分條件為 `K×sqrt(2178890000) ≥ 36000000`，即實數千分係數 `K≥771.231657055`、整數 **K≥772**。地平線隨 R 單調增加，檢查 R=640 即可涵蓋更大 R。若只要求 R=320 確實縮短，則另有 K≤1089；若要求 cap 不超過基準地平線，則 K≤1000。建議可接受域 **772..1000‰**，775‰ 為靠近既有餘量的選擇；K=772／1000 時 R=320 分別為 25498／33028 mm（同腳本實跑）。

### 全整數規約

令 `r=floor((double)PlanetConfig.Radius×1000)`、`H=1700`、`K=775`、`P=1000`。目前四級 R 轉 mm 皆精確；對其他合法 float 半徑，先升 double 再乘整數 1000 的乘積可精確表示，floor 是明確的向下量化。半徑仍只來自 PlanetConfig，不另存平衡 JSON。

```text
N = 2*r*H + H*H                         // mm²
T = K*K*N
cap_mm = min(36000, isqrt(T) / P)        // 非負整數除法
isqrt(T) = 唯一 q，使 q² ≤ T < (q+1)²
```

`floor(isqrt(K²N)/1000) = floor(K√N/1000)`，所以在上述整數mm半徑上恰好符合使用者公式；四級正式R無額外量化誤差。**不能先把 horizon 取整**：R=320 時 `isqrt(N)=33028`，再乘 775／1000 會錯成 25596 mm。`PlanetConfig.HorizonDistance` 的 float sqrt 留給表現／查詢，不作此 cap 的權威實作。

整數平方根可沿用專案立方根的二分搜尋思路；對非負 Int64，令 lo=0、hi=min(T,3037000499)+1，每次取整數中點 m，以 `m <= T/m` 推進 lo，否則縮 hi，至 hi−lo=1。比較不做 m²，避免搜尋本身溢位；T=0 直接得到 0。所有加乘**先升 Int64 並 checked**，非法半徑／係數須失敗。

本提案 H=1700、K≤1000、PlanetConfig 的 R≤8000 m 下，`N≤27202890000`、`T≤27202890000000000`，小於 signed Int64 上限；K=775 的最大 T 實跑為 `16338735806250000`。若擴張眼高／係數域，需另驗乘積上界，不能把原 loader 的單欄數值上界當作乘積安全證明。

### 收斂位置與距離語意

保留現有不含星球的 `DerivedStats.RangedDistanceMm`／Build 原始量與 36 m 規約；增加單一 planet-aware 收斂入口，執行期呼叫端都必須通過它。順序建議：**原本 STR／36 m 計算 → 既有 EffectMath.ApplyRange → 最終 planet cap → 命中、瞄準、dash、AI／預覽**。原始 volume 要保留供修正計算；不可把已裁成小星球 cap 的值存回去，再拿來乘 debuff。

| 距離／形狀 | 最終套用方式 | 保留的尺寸／行為 |
|---|---|---|
| mid／long front、line、sweep、cone | `ReachMm=min(修正後長度,cap)` | 寬、高、角度不變 |
| arc／field、target burst | `CastDistanceMm=min(修正後施放距離,cap)`，ResolveAnchor 使用此值 | 落點 AoE 半徑與高度不再縮小；field 保存收斂後 volume、anchor 與 cast snapshot |
| mid／long ally | 收斂 CastDistanceMm | 原隊友／高度判定語意不變 |
| dash，包含標為 melee 的 dash | `d=min(base_range_mm,cap)`，**ReachMm、DashDistanceMm 同時等於 d** | 不吃 STR／range buff；資料上限 `MoveDef.MaxDashRangeMm=36000` 與 schema 不改 |
| 非 dash melee 判定體 | 維持既有縮放；若 arc／field 被標 melee，仍收斂其施放距離 | 不縮小近戰長、寬、高、AoE，也不動 BOSS ×1.5 |

cap 限制的是射程／施放距離／dash 路徑；沿用 combat.md 的 AoE 尺寸語意，非 melee 的 caster burst 圓半徑也仍是 AoE 尺寸。矩形角、目標身體或落點 AoE 可以伸到 cap 之外，不能把整個命中包絡再裁一次。例：2 m target burst 的施放上限 25597 mm，AI 包絡應為 27597 mm；1 m 寬 line 的 AI 角點包絡為 25602 mm。這兩個數值均實跑，不代表施放／位移越界。

暫存收斂原型實跑：36 m line 在 30 m 處由命中變不命中；arc／field 瞄準 40 m，落點弧長為 25.596998352 m，半徑仍為 2000／3000 mm；dash 終點為 25.597000790 m。基準 24 m、STR=25 原得 26400 mm，R=320 得 25597 mm。36 m、−50% 射程正確為 18000 mm；先裁小星球 cap 再乘會錯成 12799 mm（沿用現有 ApplyPercent 的 half-up）。以上是提案原型驗證，尚未實作進 repo。

## B. R=320 使用域稽核

位置縮寫：`C/`＝`Assets/_Project/Features/Combat/Runtime/Model/`；`P/`＝`Assets/_Project/Features/Planet/`；`W/`＝`Assets/_Project/Features/WorldGen/`；`B/`＝`Assets/_Project/Bootstrap/`。行號以本次工作樹為準。「門檻開放」指**僅在新暫存副本**把 MinSupportedRadiusM 改為 320，並未改其餘幾何公式。

| 項目與位置 | R=320 判定、實跑或推導依據 | 最小修正建議 |
|---|---|---|
| 硬門檻：C/HitVolume.cs:196,528；C/Encounter.cs:298；C/CombatAi.cs:362,RequireCombatPosition | **不成立，實跑**：planet=(320,0)，caster=(0,320,0)，forward=(1,0,0)，合法 10 m line Overlaps、40 m aim 的 ResolveAnchor、36 m ResolveDashEnd 全拋「R≥640」ContractException；Encounter 建構、AI 無敵人輸入亦拋例外。 | 常數改 320，同步例外訊息與所有 public Pre／文件；新增 planet cap 接線後才視為完成。 |
| ShellSlack：C/HitVolume.cs:186,539 | **成立，有限樣本實跑**：殼層變 160..640 m；slack 在中心 0 為 0.64 mm，在 max中心分量16000 為16.64 mm（公式推導）。24000 組 ResolveAnchor→Overlaps 組合無殼層例外，含內外邊界；返回半徑最小159.999325062、最大640.001008272 m，均在既有 slack 內。 | 保留 `1e-6×(max(abs(center))+2R)`；不得改成只依 R 的 slack；新增 R=320、偏心殼層回歸。 |
| DegenerateSin：C/HitVolume.cs:180,308 | **成立，實跑**：R=320、caster=(0,320,0)，aim=(320s,−320,0)，s=0／5e−10 時取 North、s=2e−9 時取可解析方向；落點分別 x=−25.569712／+25.569712 m，弧長均25.597000790 m，未出 NaN。閾值是無因次角度，不需隨 R 放大。 | 保留1e−9；補兩側／重播測試。其地表尺度約0.32 μm（320×1e−9 推導）；舊「R640 約6 μm」註解僅為估量，精確乘積其實0.64 μm。 |
| ResolveAnchor：C/HitVolume.cs:285 | **幾何成立、cap 接線不成立，實跑**：門檻開放後 raw arc 瞄準40 m仍落36 m，違反新25597 mm cap。24000 組 capped 參數幾何探針最大正向距離誤差2.590371 mm，全部符合既有返回 float 的距離 Post；未發現需改核心三角運算的反例。 | 呼叫前提供收斂後 CastDistanceMm；保留 double、殼層 clamp、退化 fallback。 |
| Overlaps／Local：C/HitVolume.cs:378,461 | **幾何成立，實跑**：R320 同地面35.9 m 的 raw36 m line 仍命中，證明徑向高度處理未漏判；503 m 遠半球目標不命中。R320 表面πR/2=502.654825 m，內殼層約251.327412 m，皆遠大於新cap（推導）。新射程規則需另收斂，不能拿上述 raw 命中當新規則合格。 | 保留 atan2、raw cross、徑向高度、π/2 截止；補 cap 內外與縮放不變式。 |
| ResolveDashEnd：C/HitVolume.cs:336；GameData/MoveDef.cs:99 | **幾何成立、規則不成立，實跑**：raw36000 mm 仍位移35.999999033 m；range buff200%仍為36000。即使最內殼層加最大slack，`rcπ/4≥125.650637 m`，所以36 m資料仍不會碰到安全上界（推導）。 | 不下修資料宣告上限；runtime 同時收斂判定路徑與位移，保留rcπ/4 Pre。不可只改DashDistanceMm。 |
| EffectMath／施招：C/Effects.cs:714；C/Encounter.cs:486,826,849,855,873 | **不成立，實跑**：raw long line／cone＋200%仍36000；arc／field CastDistance仍36000。melee-tagged arc／field、STR100、base36000 的 CastDistance甚至為41184；schema只限制target burst range_class，未禁止這些標法。 | 最終 cap 放在 ApplyRange 後，含其 percentSum=0／dash 提早返回的分支；所有 aim-point 施放距離均收斂，melee AoE不變。重用同一收斂入口，避免 field、dash 各抄一份。 |
| TangentToward：C/Encounter.cs:1238 | **成立，實跑**：24000 組方向／近徑向輸入，最大abs(dot(forward,trueUp))=1.020610e−7，unit誤差均<1e−5。R320 軸向case tau約0.306129 mm，偏心(16000,−16000,8000)約15.258789 mm；0.5tau保留+Z、2tau轉+X，均有限。 | 保留double cross、16uS(1+length(delta)/r)、ProjectOnTangent；新增R320與偏心見證，不調低tau。 |
| CombatAi：C/CombatAi.cs:318,362,588；C/Encounter.cs:1159 | **方向幾何未見失效；新射程接線不成立，實跑**：敵人在30 m、raw36 m line包絡36004 mm時 CastSlot=0；用收斂後volume重新算包絡25602 mm，CastSlot=−1。stand-off使用min/maxReach，因此也須讀收斂後資料。 | FillOptions從收斂後volume呼叫ReachMmOf；保留角點／AoE包絡與既有π/2 stand-off clamp。勿直接把AiMoveOption.ReachMm硬裁cap。AI目前未完整反映range buff屬既有品質限制，不以此改寫buff規則。 |
| 領地／脫戰：C/Encounter.cs:217,221,996；C/CombatBalance.cs:24 | **尺度仍局部，但弦長≈弧長的精度說明不成立**：40 m的弦短26.036581 mm（0.065091%），28 m短8.931437 mm。內部狀態隔離實跑：兩位置均置R320殼層表面，Apex入侵弧長28.004998236 m→弦27.996063 m→誤開戰；已開戰Normal、弧40.020001028 m→弦39.993927 m→未脫戰。這是以「球面弧距門檻」為oracle；現行實作本身採3D直線距離，不能混為已定案的同一語意。 | 若要精確球面門檻，重用ArcM，並明訂以海平面R還是身體中心半徑量測；既有3D弦長還混入高度差。至少更正<0.01%註解、補兩側見證。不是半徑必須重調的證據。 |
| 遊蕩／回家：C/Encounter.cs:1034,1042,1105；C/CombatBalance.cs:85 | **半徑與停止機制成立，採樣註解過度保證**：R320版本既有wander測試通過；回家仍以弦長比較。`sqrt(U)`為平面圓盤抽樣，球面圓帽面積相對平面差在12 m為0.0117182%、40 m為0.1301405%（由`1−2(1−cosθ)/θ²`推導），所以「r≤40,R≥320差<0.01%」不成立。 | 預設3..12 m不需改。修註解、補弧距回家邊界；若要求球面等面積才改為均勻cosθ，會改seed結果，須另作生成／重播版本變更。 |
| 推開／Steer／近身互動：C/Encounter.cs:689,754,782,1201 | **有限測試內成立**：碰撞已用ArcM、推開角度除R，互動亦用R；Steer仍用float acos與ArriveM，並非R≥640硬門檻。R320版Collision／Interact／MoveSpeed連同Wander／Territory／Sea／MoveGeometry共81項managed測試全過；這不等於原生物理或所有近停止點均已證明。 | 不重寫既有推開退化處理；補R320兩極／面角、完全重合與ArriveM兩側。原生碰撞未驗證。 |
| 只在陸地：C/Encounter.cs:875,1088,1217,推開落海回退；W/Runtime/Model/RunPlanner.cs:505 | **移動保護有限測試內成立；出生保證有既有缺口，實跑**：R320版Sea測試通過。RunPlanner fixture、seed1、juvenile、非空陸地遮罩`isLand(d)=d.y<0.2`→6隻海上spawn，BOSS也在海；全海遮罩→38/38海上spawn且降落點也海。Scatter重試後可返回海面，boss用maxAngle=0尤其無法離開海上的site。 | 移動／dash終點落海仍維持命中但不位移。出生需獨立確保陸地，無合法點時顯式生成失敗；不得用海面fallback。改抽樣／站點會影響世界seed結果，OPUS應分開處理版本及既有快照。原生template遮罩整合未驗證。 |
| RunPlanner尺度：W/Runtime/Model/RunPlanner.cs:128,134,298 | **全陸地fixture成立，實跑**：R320、juvenile、seeds0..999，全為8區、25..50 spawns，BOSS可由主路線抵達、恰一BOSS、全員juvenile，同seed Hash重跑一致。四級R的seed1區數為8／12／28／32。pack成員偏移5/R，隨R換算，不含640硬門檻。 | 不為R320重設區域演算法或亂數；另補實體陸地路線與出生保證。此處圖連通不代表跨海可步行。 |
| TemplatePlanner／投影：W/Runtime/Model/TemplatePlanner.cs:22,26,62；P/Api/PlanetTemplates.cs:317 | **角度尺度成立，有限實跑／推導**：CountFor四級為4／4／6／6、seed1均返回該數量；DefaultHalfAngleRad=0.45，R320邊中點弧距144 m（0.45×320），gnomonic本身無R≥640前提。角度、源索引與中心為實際原程式；本次只抽出原TemplatePlacement值型別供managed編譯。 | 不把288 m註解誤當固定模板半徑。保留投影／原始seed次序；native buffer、實際地形混合與接縫未驗證。 |
| PlanetConfig／高度／chunk／LOD：P/Api/PlanetConfig.cs:30,95；P/Api/PlanetHeight.cs:117；P/Runtime/Model/ChunkLodSettings.cs:86、ChunkQuadtree.cs:69 | **managed成立**：PlanetConfig本就允許R≥1。四級LOD深度4／5／6／6，頂點間距0.981748／0.981748／0.736311／0.981748 m；120個赤道viewer位置每步覆蓋六面，各最大葉數330／468／672／672。R320 noise高度界18.9 m；加template最壞貢獻後margin38.100002 m仍Supports=true，低於R/2及16個最細邊長。 | 不動cube-sphere FENCE、縫合或預設LOD。增加R320含templates的原生網格／collider／2:1檢查；本次葉測試只驗六面面積覆蓋，未完整重驗鄰接。 |
| DemoGame尺寸：B/Demo/DemoGame.cs:745 | **不成立，抽出原方法實跑**：目前Asteroid→640；只改MinSupportedRadiusM→320後，Asteroid→320但Moon也→320，Planet／Supergiant仍960／1280。 | 明確四級switch回傳320／640／960／1280；未知enum fail loud。不可讓Moon透過default依賴最小戰鬥半徑。卡片、RunSetup、ReproProbe都須沿用此對映。 |
| RunSession／場景與外觀：B/Demo/RunSession.cs:367,389,418；B/PlanetRig.cs:92；B/Demo/PlanetLook.cs:151；B/Demo/ScatterField.cs:142,283 | **靜態未見新硬門檻；原生未驗證**：RunSession把setup.Radius交給同一PlanetConfig；PlanetRig傳入該config；Scatter cell數與角半徑依R推導。fogStart=40 m在R320超過基準地平線，但fog是外觀參數，非射程規則。 | 不順手改fog／90 m scatter／128 m collider距離；新增R320降落、走動、瞄準與dash視覺驗收。 |
| 640範例／工具：B/PlanetSandbox.cs:17；B/Demo/DemoBalanceProbe.cs:100,125；B/Demo/ReproProbe.cs:104；P/Api/PlanetFrame.cs:27 | **不是全部都需替換**：sandbox／balance probe固定640是既有情境；Frame極區閾值為角度，640僅作物理尺度註解。ReproProbe註解仍說asteroid640，已過時。**相關工具／極區原生表現未驗證。** | 保留舊640基準，另加320平衡情境；只更新過時描述。不能全域搜尋取代640，連UI像素與無關XP也會被改。 |

### 遭遇距離占 R=320 圓周的比例

圓周為 `2πR=2010.619298 m`；以下取自 balance.json 與 Encounter 常數，由 `Qd.Numbers` 重算。這些距離皆遠小於半球域，無需因「會繞到背面」而縮半；地平線cap也不應誤套在感知／領地／回家距離上。

| 類別 | 半徑（m） | 圓周比例（%） |
|---|---|---|
| 領域 timid／cautious／normal／territorial／fierce／apex | 0／4／8／14／20／28 | 0／0.198944／0.397887／0.696303／0.994718／1.392606 |
| 遊蕩 timid／cautious／normal／territorial／fierce／apex | 3／4／6／8／10／12 | 0.149208／0.198944／0.298416／0.397887／0.497359／0.596831 |
| ProvokeRadiusM／DisengageRadiusM | 18／40 | 0.895247／1.989437 |

18 m的ProvokeRadiusM為目前公開常數；`Provoke(unitId)`本身沒有距離檢查，不能把常數存在誤稱為已執行限制。這是既有API現況，與下修R無直接因果。

### 測試中的舊假設與未驗證邊界

- `CombatReviewSolTests.cs:359` 明確斷言320必須被幾何拒絕；`CombatAiTests.cs:613`、`DashDisplacementTests.cs:264`、`EncounterTests.cs:244`也把320當非法。應改為320成功、低於320失敗，並確保測試位置本身合法，避免被別條Pre遮蔽。
- `CombatReviewSolTests.cs:259,268`與`DashDisplacementTests.cs:99`的fuzz從640起跳；`EncounterCollisionTests.cs:209`只列640／960／1280；TangentToward／EncounterForward既有見證缺320。加樣本，不改既有640見證與黃金向量。
- 原生PlayMode目前主要只用640：`Planet/Tests/PlayMode/PlanetTerrainTests.cs:22`、`Player/Tests/PlayMode/SphericalCharacterTests.cs:21`、`PlayerCameraRigTests.cs:24`；`Bootstrap/Tests/PlayMode/RunSessionSmokeTests.cs:78`刻意是adolescent640、`RunSessionProgressionTests.cs:30`固定640，這些基準仍合法，不能改成320後維持錯誤stage語意。另新增juvenile320端到端情境。`ScatterFieldTests.cs:44`已有320情境，但本次未跑原生。
- `Bootstrap/Tests/EditMode/ReproProbeTests.cs:109,118`使用自訂radius delegate把非adult映到640，只證明該fixture；須另驗正式DemoGame四級對映。不得為接受本次managed後端的float差異重產舊world快照。
- 本次**沒有啟動Unity**：Unity原生EditMode／PlayMode、PhysX、Jobs／Burst、IL2CPP、Mono與跨後端比對均為**未驗證**；未將「可能缺授權」寫成已重現的授權失敗。managed ScriptableObject／JSON shim不代表引擎測試。

## C. 建議回歸測試與容差

1. **純整數cap**：由JSON載入係數，使用獨立BigInteger／整數sqrt oracle驗證上表與`q²≤T<(q+1)²`；R／K單調、不超36000、R≥640保持原值、零／負／非法係數、checked溢位與mm量化兩側。加入「先sqrt(N)取整會差1 mm」見證。整數輸出與跨後端cap雜湊要求**完全相等、零容差**，不將可調平衡係數寫死於一般不變式。
2. **完整射程接線**：mid／long line、cone、arc、field、target burst、ally、melee-tagged arc／field、melee-tagged dash；STR=0／10000／int.MaxValue、range正負修正與零修正、on_move_used當招快照。比較最終volume各欄；以25597±1 mm及25..36 m中間點驗命中／anchor。AoE寬高半徑、普通melee與BOSS倍率逐欄保持原值；dash的ReachMm與DashDistanceMm完全一致。
3. **取整順序**：保留36 m、−50%→18 m見證，防止把小星球裁切放在debuff之前；正buff不能重新突破cap，dash在所有STR／range效果下只受星球cap。R640／960／1280的volume與既有輸出逐欄相等；原40組derived golden不改。
4. **殼層／投影**：R320，中心0及(±16000,±16000,±16000)，rc／ra含R/2、2R與slack兩側，普通／共線／近同向／近反向aim，DegenerateSin兩側、兩極與cube角。ResolveAnchor→Overlaps必須可組合；返回float位置的距離容差沿用`1 mm + 1e−6×max|result component| m`（注意單位，1 mm為0.001 m），殼層沿用ShellSlack。這些容差只驗float輸出，不放寬整數cap或命中邊界。
5. **朝向／dash**：TangentToward測0.5tau／2tau，含zero／radial fallback；unit誤差與abs(dot(forward,trueUp))≤1e−5，嚴於API的1e−3。dash以獨立double弧距oracle測路徑、殼層、rcπ/4兩側及末端Rect；場景回貼地後另用5 mm距離／離地容差，與直接ResolveDashEnd分開驗。
6. **遭遇／陸地**：領域4／8／14／20／28、脫戰40與回家半徑兩側，包含本文弦／弧反例與高度差；先固定量測語意再寫oracle。遊蕩至少涵蓋全部性格、重播、回家、8次全海重抽、移動／推開落海回退。R320兩身體重合、偏心兩極／面角，分離距離與離地誤差≤5 mm；野生dash終點海面保留命中但零位移，玩家不受限。出生須對每個wild／elite／boss與降落點檢查isLand；非空陸地及全海都要測，失敗必須顯式回報。
7. **生成／LOD／PlayMode**：四級R、juvenile／adolescent／adult正確尺寸；保留同後端內容快照，R320新增固定seed案例及真實templates。LOD驗4／5／6／6、coverage、2:1、跨面縫合、collider範圍與落地射線；native mesh／高度對照沿用ADR-0007跨後端3 mm高度容差，離散內容雜湊完全相同；不得把本次managed葉數當效能硬上限。另跑R320降落、繞行、鏡頭up、預覽／實際施放一致，以及場地持續tick。物理與幀時間沿用各原生測試原有驗收門檻，不能由managed跑速代替。

## 實跑記錄與交付自驗

新暫存根目錄：`C:/Users/user/AppData/Local/Temp/cosmimera-sol-qd1-r320-20261111/`。沿用舊harness的編譯參照／最小UnityEngine shim，重新從本工作樹複製對應來源；未在舊harness或工作樹打補丁。source-manifest.json記錄初始來源SHA256，報告撰寫前再次比對該清單，工作樹來源漂移0檔。Unity.Mathematics使用本機已編譯DLL，SHA256=`48049AFDD1E1FBA73BFFAE24A6DAB5E8B32BC63E387EC8BCEA53CB2A5382D12E`。

- **原碼基線**：net9.0、UNITY_EDITOR／UNITY_INCLUDE_TESTS，反射執行選入的NUnit Test／TestCase；414通過、1失敗。失敗是RunPlanner原raw-bits快照：預期1601242826727298640、managed實得9836354999031547987。它在任何R320調整前已失敗，不能宣稱整包全綠，也不能據此修改Unity原快照。見baseline.log。
- **探針**：Qd.cs，`qd gates/numbers/geometry/encounter/world/range`。原始門檻實際拒絕5類合法R320呼叫；gateopen.log則證明單改常數會讓Moon→320。geometry.log使用Pcg32(20261111,'Q')，兩個中心各12000筆，rc／ra取160、640與其間，涵蓋邊界與近反向；最大anchor正向距離誤差2.590371 mm、dash絕對弧距誤差0.898817 mm。range.log為僅在harness的收斂原型；encounter.log的弦距案例使用內部狀態／反射隔離UpdateEngagement，未冒充公開Tick端到端案例。
- **R320移植fixture**：只將七個既有測試類的`private const float R=640f`換320，並開放暫存幾何門檻；Wander／Sea／Collision／Interact／MoveSpeed／Territory／MoveGeometry共81通過、0失敗（r320-fixtures.log）。另DerivedStatsTests 17通過，含舊黃金向量（derived.log）。RunPlanner另以1000個seed作前述全陸地檢查，world.log保留結果；未聲稱覆蓋所有原生測試。
- **整數自驗**：Qd.Numbers逐mm窮舉r=320000..8000000，檢查sqrt上下界、R≥640 cap不變，與double公式的floor結果無差異；正確性的依據仍是整數不等式。交付前重讀本報告，再用獨立BigInt二分腳本重算四列表格與係數上下界，確認公式／表格一致。其餘小數均來自上述實跑或本文列出的公式；標成未驗證的項目不作通過結論。

- 2026-11-11 · ChatGPT Sol（Codex）· done · 完成Q-D1係數提案、R320幾何稽核與暫存managed反例驗證 · 下一步：OPUS 實作／使用者確認 k
