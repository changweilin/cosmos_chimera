> 原始輸出：`codex exec -s read-only -m gpt-6.1-sol`，對象 `919d532`。未修改任何檔案。Claude Opus 修正：A1 矩形 gate 改外接角落、A2 改寫契約為已知近似、A3 站距夾在四分之一圓周、A4 以原始世界位移求切向並改相對退化門檻、A5 殼層檢查只在失敗時組字串（新增零配置測試）、A6 契約文字、A7 補見證；A8/A9/A11 寫入 combat.md，A10/A11 開 Q-C22。

判定 fail

審查對象：`919d5326f24141b4bff3396c4509df8c6c3879ed`。發現 **5 項實作／幾何問題、1 項契約文件問題、1 項測試缺口**；另列 4 項 design 風險，不作為功能錯誤計分。

全程未修改專案、未 commit。審查期間另一工作將 HEAD 推進至 `25c47a0`；已確認本次審查的程式、測試、schema、平衡資料與 `combat.md` 均與 `919d532` 相同。

實際驗證結果：

| 驗證 | 結果 |
|---|---|
| 指定原始碼以 Roslyn 編譯至記憶體，執行 `CombatAiTests` | **22/22 通過** |
| 使用實際 `Data/balance.json` | 通過；僅替換 `BalanceFixture` 取得專案根目錄的 Unity API |
| 重複輸入、敵人與招式反序探針 | **2,000 組，0 偏差** |
| 原始碼變異，每個重新編譯並執行上述 22 個測試 | **55 個：35 killed、20 survived** |
| Editor 契約開啟，預熱後執行 10,000 次配置量測 | **472 bytes／Decide** |
| `_incoming` 三份 creature 資料的 `ai_style` 掃描 | **33 筆，0 缺漏／非法值** |

未執行會寫入專案的 Unity batchmode 或 importer 資產測試；上述測試結果是 CLR 記憶體 runner 的結果，並非 Unity Editor／IL2CPP 驗證。

**A1 · medium · bug：矩形的 `ReachMmOf` 不是「可能接觸」的上界。**

位置：[CombatAi.cs:300](/C:/Users/user/Documents/app/cosmos_chimera/Assets/_Project/Features/Combat/Runtime/Model/CombatAi.cs:300)、[HitVolume.cs:410](/C:/Users/user/Documents/app/cosmos_chimera/Assets/_Project/Features/Combat/Runtime/Model/HitVolume.cs:410)。

重現輸入：

- `R=640`、球心原點、施放者 `(0,640,0)`、forward=`+Z`。
- `Front`、melee、STR=0、長度 2 m；使用正式預設寬度 1.5 m。
- 將零半徑目標放在測地局部座標 `x=0.70 m, z=1.99 m`，高度相同。

實測：

```text
目標測地距離 = 2.1095260568 m
MoveGeometry.Overlaps = true
ReachMmOf            = 2000 mm
Decide.CastSlot      = NoCast
```

矩形角落距離可以大於長度。現在 `ReachMmOf` 對 front、line、dash 都只回傳 `ReachMm`，因此會排除已經接觸的目標；「忽略 width」在此造成低估。

建議修法：矩形至少使用保守外接半徑：

```text
ceil(sqrt(lengthMm² + (widthMm / 2)²))
```

中間值使用 double／足夠寬的整數，最後飽和；若仍宣稱「最大距離」，還須處理球面半球限制。新增上述角落見證，不只驗證回傳值等於既有欄位。

**A2 · medium · bug：aim-point 招式在不同半徑時，AI 與命中幾何量距不一致。**

位置：[CombatAi.cs:318](/C:/Users/user/Documents/app/cosmos_chimera/Assets/_Project/Features/Combat/Runtime/Model/CombatAi.cs:318)、[HitVolume.cs:282](/C:/Users/user/Documents/app/cosmos_chimera/Assets/_Project/Features/Combat/Runtime/Model/HitVolume.cs:282)、[HitVolume.cs:463](/C:/Users/user/Documents/app/cosmos_chimera/Assets/_Project/Features/Combat/Runtime/Model/HitVolume.cs:463)。

`Decide` 以施放者半徑量距；`ResolveAnchor` 也以施放者半徑限制施放距離，但保留瞄準點半徑。後續 `Overlaps.Local` 使用的是 **anchor 半徑**。

重現：`R=640`、施放者半徑 640 m；target burst、STR=0、施放距離 12 m、爆發半徑 3 m，瞄準目標中心。

| 目標半徑 | 施放者半徑上的目標距離 | AI gate | 實際接觸 | AI 決定 |
|---:|---:|---:|---|---|
| 320 m | 16 m | 15 m | **true** | **NoCast** |
| 640 m | 14 m | 15 m | true | slot 0 |
| 1280 m | 14 m | 15 m | **false** | slot 0 |

所有位置都在合法殼層內。

在同一大圓、anchor 與目標同半徑的情況，換算回施放者半徑後，延伸距離是：

```text
castDistance + discRadius × casterRadius / anchorRadius
```

因此單純相加只在兩個半徑相同時成立。這同時影響 burst target、arc、field。

建議修法：先統一距離契約。可以讓接觸 API 明確接受施放者半徑，或讓 AI 以實際施放者／anchor 情境計算 gate；現有只接收 `HitVolume` 的函式無法精確表達此差異。契約變更須同步更新幾何、AI、文件與測試。

**A3 · medium · bug：合法輸入可讓站位角度繞圈，違反指定測地距離。**

位置：[CombatAi.cs:410](/C:/Users/user/Documents/app/cosmos_chimera/Assets/_Project/Features/Combat/Runtime/Model/CombatAi.cs:410)、[CombatAi.cs:534](/C:/Users/user/Documents/app/cosmos_chimera/Assets/_Project/Features/Combat/Runtime/Model/CombatAi.cs:534)。

重現：

```text
R = casterRadius = 640 m
style = KeepDistance
hostile reach = 3,000,000 mm
keepDistancePermille = 800
```

這些值符合現有 public Pre。要求站距為 2,400 m，但最短測地距離不可能超過 `π×640≈2010.619 m`。

實測目的地距目標只有：

```text
1621.23858237438 m
```

原因是 `Along` 對未限制角度直接套 sin／cos，形成繞圈。

另以 2,000 m caster burst 攻擊夾角 2 rad 的目標，實測 AI 選擇施招，但 `MoveGeometry` 因超過 90° 回傳 false。這個誤放招可視為保守 gate 的代價；然而它證明目前回傳值也不是文件宣稱的「實際最大可接觸距離」。

建議修法：在有 planet／caster radius 的層次定義可表示的站距與幾何上限，限制或夾住角度。不可只依靠 permille≤1000；reach 本身沒有相應上限。

**A4 · low · bug：非退化的小偏移被當成重合，目的地離開自身與目標的大圓。**

位置：[CombatAi.cs:297](/C:/Users/user/Documents/app/cosmos_chimera/Assets/_Project/Features/Combat/Runtime/Model/CombatAi.cs:297)、[CombatAi.cs:527](/C:/Users/user/Documents/app/cosmos_chimera/Assets/_Project/Features/Combat/Runtime/Model/CombatAi.cs:527)。

重現：

```text
self   = (0,640,0)
target = (0,640,1e-10)
target.Forward = +X
style = KeepDistance
reach = 10000 mm
```

自身與目標的大圓在 YZ 平面，但實測目的地方向為：

```text
(0.01249967, 0.9999219, 約 1.56e-13)
```

即沿 `+X` 走約 8 m。`len <= 1e-9` 將仍可表示的非零方位丟掉，改用了 facing fallback。

精確重合與精確對蹠點需要 fallback，這部分合理；問題是非零鄰域也被納入，且 Post 未揭露。

建議修法：使用原始相對座標的叉積建立切向量，保留可表示的非共線方位；精確退化才 fallback。若刻意保留容差退化，須明訂這個例外及可接受方向誤差。

對精確對蹠點的實測：四種風格均輸出有限單位向量，站距分別約 8、5、5、9 m，未發現 NaN 或不決定性。

**A5 · medium · bug：Editor／Development 成功路徑仍配置記憶體。**

位置：[CombatAi.cs:351](/C:/Users/user/Documents/app/cosmos_chimera/Assets/_Project/Features/Combat/Runtime/Model/CombatAi.cs:351)、[HitVolume.cs:542](/C:/Users/user/Documents/app/cosmos_chimera/Assets/_Project/Features/Combat/Runtime/Model/HitVolume.cs:542)。

重現：預先建立 tuning、input、一名合法敵人與一個招式，預熱 1,000 次，再量測 10,000 次 `Decide`。

```text
GC.GetAllocatedBytesForCurrentThread 差值：
472 bytes／Decide
```

來源是共用殼層檢查：

```csharp
Contract.Requires(condition,
    name + " must be within R/2..2R of the planet center (combat shell)");
```

條件成立仍會先建立字串。Release 會剝除 Conditional 呼叫及引數求值；**本項不表示 Release 也配置 472 bytes**，但目前 XML 與設計並未把「無配置」限於 Release。

建議修法：成功路徑使用固定字串，或只在失敗分支建立診斷訊息。新增預熱、預建輸入的配置測試，明確區分 Editor／Development／Release。

**A6 · low · contract：公開契約有錯字、矛盾與未完整表達的選擇規則。**

位置：[CombatAi.cs:64](/C:/Users/user/Documents/app/cosmos_chimera/Assets/_Project/Features/Combat/Runtime/Model/CombatAi.cs:64)、[CombatAi.cs:198](/C:/Users/user/Documents/app/cosmos_chimera/Assets/_Project/Features/Combat/Runtime/Model/CombatAi.cs:198)、[CombatAi.cs:328](/C:/Users/user/Documents/app/cosmos_chimera/Assets/_Project/Features/Combat/Runtime/Model/CombatAi.cs:328)。

| 問題／重現輸入 | 現況 | 建議修法 |
|---|---|---|
| `AiInput` 建構子說 command／mainTargetId 分別被 Main／Support 忽略 | **角色寫反**；實作與屬性文件是 Support／Main | 修正 respectively 對應 |
| `default(AiCommand)` | `Kind=Free, TargetId=0`，與同段 `TargetId>=0 iff Focus` 矛盾 | 明確把 Post 限於建構值，或寫出 default 例外 |
| 有可用且可及的敵對招式 | CastSlot 條款主要寫成「選了 ⇒ 合法」 | 明訂存在合格選項時必選，以及何時必為 NoCast |
| 所有敵對招式仍在 CD | 仍以它們的 reach 決定站位 | 明訂站位包含未 ready 招式；「no Hostile option」是沒有敵對招式，不是目前沒有可施放招式 |
| 公開 enum 型別／值 | 多處只有 summary，缺 Pre／Post／Side effects | 依 `code-contracts.md` 補齊 |
| `AiTuning` | 型別缺 Side effects；建構子標 none，與專案其他配置物件的契約慣例不一致 | 明訂建立 instance 的配置行為 |

其餘 public 建構子的數值／enum Pre，實作均有對應檢查；幾何位置由 `Decide` 延後檢查的分工也有寫明。`ReachMmOf` 的「由 Build 產生」屬來源契約，沒有執行期來源標記；`default(HitVolume)` 又與合法 Self 的零值表示相同，不宜要求它一律拋錯。

`Decide` 與設計的逐條核對如下：

| 設計條款 | XML／實作核對 |
|---|---|
| 指令只給 Main；Support 自由戰鬥並優先主控目標 | 一致；建構子文字有 A6 錯字 |
| Focus 存活優先，失效回最近存活；同距小 id | 一致 |
| 可用、可及敵對招取最高 power，再取小 slot | 選擇實作一致；「可及」計算有 A1／A2 |
| 無合格敵對招才考慮 Self／Ally | 一致；另加了「必須有敵人目標」限制，見 A11 |
| KeepDistance／Charge／Flank／Hold 大圓站位 | 一般情況一致；A3／A4 為邊界反例 |
| Follow 超過 leash 才返回 | 一致；返回半條 leash 是 XML 明訂、設計未指定的補充 |
| Retreat 返回玩家且不施招 | 一致 |
| 無目標／無敵對招式時的行為 | XML 明訂守 leash；設計未明訂 |
| 單位方向、同輸入同輸出 | 本次探針通過 |
| 無配置 | Editor／Development 不成立，見 A5 |

**A7 · medium · test-gap：現有綠燈不足以驗證每條 Pre 與 Post 邊界。**

位置：[CombatAiTests.cs:168](/C:/Users/user/Documents/app/cosmos_chimera/Assets/_Project/Features/Combat/Tests/EditMode/CombatAiTests.cs:168)、[CombatAiTests.cs:336](/C:/Users/user/Documents/app/cosmos_chimera/Assets/_Project/Features/Combat/Tests/EditMode/CombatAiTests.cs:336)。

具體缺口與建議：

| API／契約 | 已覆蓋 | 缺少的違反或邊界見證 |
|---|---|---|
| `AiCommand` | 未定義 kind、非法 Focus id、非 Focus 攜帶 id | 負 enum、default 政策與屬性 Post |
| `AiTuning` | keep 兩界、close 下界、hold 上界、leash=0 | **close=1001、hold=0**；非預設 tuning 是否真的影響結果 |
| `AiEnemy` | 負 id | 建構值回傳；位置／forward 邊界交由 Decide 補測 |
| `AiMoveOption` | slot、reach、power、Self reach、正向非法 targeting | **targeting=-1**；合法 0／MaxPower／極大 reach |
| `AiInput` | 正向非法 style／role、mainTarget<-1 | 負 enum、忽略欄位政策、屬性保留 |
| `Decide` planet／position | R=320、殼層下界外 | default planet、外界超界、NaN／Infinity、殼層兩側邊界、不同高度及大球心 |
| living Forward | radial、長度 2、NaN | ±1e-3 容差兩側、Infinity、非軸向切向量 |
| validation 次序 | 一般 Free 路徑 | Retreat 等 early-return 路徑仍必須檢查非法輸入 |
| reach gate | 11.99 m／12.02 m | **沒有真正測到等號**；零 reach 同點、界外 0.5 mm |
| 大圓站位 | 一般近距、精確重合 | 非零微小方位、對蹠、>90°、超半圈、非預設 tuning |
| `ReachMmOf` | Self／Ally／Line／兩種 Burst 的欄位值 | Rect 角落、Sector、Arc、Field、dash、飽和、跨高度接觸 oracle |
| 決定性／配置 | 一組非 tie 反序 | 同距多排列、重複呼叫、配置量測 |

以下是 **55 個實際執行的變異**。每個均以原始版本為基礎獨立修改、編譯，再跑 22 個測試；K=`killed`，S=`survived`。表中見證是建議新增測試，不表示每個建議都已加入或執行。

| ID | 變異 | 結果 | 現有殺手／建議見證 |
|---|---|:---:|---|
| M01 | 不處理 Retreat | K | Retreat 測試 |
| M02 | Retreat 回傳 slot 0 | K | Retreat 測試 |
| M03 | Support 直接聽玩家指令 | K | Support 指令測試 |
| M04 | 不跳過死亡敵人 | K | Free／Focus 死亡目標 |
| M05 | 同距改取較大 id | K | 同位置 tie 測試 |
| M06 | 忽略 preferred target | K | Focus／Support 優先目標 |
| M07 | `atan2` 改 `acos` | **S** | 零 reach，目標 `(0,640,1e-6)`；不可把非零距離折成 0 |
| M08 | Main 也採用 MainTargetId | K | Main 忽略主控目標欄位 |
| M09 | 忽略 ready | K | 最高威力仍在 CD |
| M10 | 改選最低 power | K | 最高威力選擇 |
| M11 | 同威力取較大 slot | K | slot tie 測試 |
| M12 | 敵對 gate `<=` 改 `<` | **S** | reach=0、精確同點，應施放 |
| M13 | 敵對 gate 放寬 1 mm | **S** | reach=12 m、目標約 12.0005 m，應 NoCast |
| M14 | 無 partner 時距離設 0 | K | 無 partner 的 Ally |
| M15 | Self／Ally 優先於敵對招 | K | power=0 敵對招仍優先 |
| M16 | Self／Ally 取較大 slot | K | 支援招小 slot |
| M17 | 無敵人也呼叫 PickCast | K | 無目標不得施招 |
| M18 | 站位忽略未 ready 招式 | K | KeepDistance 的遠招仍在 CD |
| M19 | CloseIn 使用最長 reach | K | Charge 最短 reach |
| M20 | keep tuning 固定 800 | **S** | tuning=400、reach=24 m，站距應 9.6 m |
| M21 | close tuning 固定 500 | **S** | tuning=250、reach=4 m，站距應 1 m |
| M22 | hold tuning 固定 900 | **S** | tuning=500、reach=12 m、目標 8 m，應移動 |
| M23 | leash 固定 6000 | **S** | leash=2 m、玩家 5 m，返回至距玩家 1 m |
| M24 | 返回完整 leash，非半條 | K | Follow 返回 3 m |
| M25 | leash `<=` 改 `<` | **S** | 精確等號邊界；可在受控 double 幾何測試中建立 |
| M26 | 無目標不進 leash 分支 | K | 無目標 idle |
| M27 | 無敵對招不進 leash 分支 | K | 只有 Self 的 idle |
| M28 | Flank 去掉負號 | K | 繞背兩種 facing |
| M29 | 退化 fallback 反向 | K | 精確同點 facing |
| M30 | 退化閾值擴大至 `1e-5` | **S** | 目標偏移 1 mm、facing 與目標方向垂直；檢查大圓平面 |
| M31 | caster radius 改 planet.Radius | **S** | self／target 半徑 1280 m、實距 20 m、reach=12 m |
| M32 | self 先 float 相減 | **S** | 大球心、接近世界原點座標；double oracle 邊界搜尋 |
| M33 | target 先 float 相減 | **S** | 同上，針對目的地方向及 reach 邊界 |
| M34 | Along 移除 normalize | **S** | 先訂浮點允差，再決定是否要求殺死，見下文 |
| M35 | aim-point gate 移除 cast distance | K | Burst target reach |
| M36 | Self reach 回傳 1 | K | Self reach |
| M37 | Ally 回傳 ReachMm | K | Ally cast distance |
| M38 | cast+radius 先以 int 相加 | **S** | target burst 半徑 `int.MaxValue`，應飽和而非負值 |
| M39 | close 下界放寬至 0 | K | close=0 |
| M40 | close 上界放寬至 1001 | **S** | `new AiTuning(800,1001,900,1)` 必須拋錯 |
| M41 | hold 下界放寬至 0 | **S** | `new AiTuning(800,500,0,1)` 必須拋錯 |
| M42 | leash 接受 0 | K | leash=0 |
| M43 | targeting 接受 -1 | **S** | `AiMoveTargeting=-1` 必須拋錯 |
| M44 | 移除 player position 檢查 | K | player 在殼層下方 |
| M45 | 移除 self position 檢查 | K | self 在殼層下方 |
| M46 | 移除 partner position 檢查 | K | partner 在殼層下方 |
| M47 | 放寬 tangent 檢查至 1 | K | radial Forward |
| M48 | 移除敵人 id 唯一檢查 | K | 重複 id |
| M49 | 移除 slot 唯一檢查 | K | 重複 slot |
| M50 | 放寬 Forward 長度容差至 1 | K | 長度 2 |
| M51 | TargetId 回傳 index | K | 多個目標測試 |
| M52 | close 的 reach×permille 改 int 乘法 | **S** | caster radius=1280 m、reach=5,000,000、close=500 |
| M53 | keep 的 reach×permille 改 int 乘法 | **S** | caster radius=1280 m、reach=3,000,000、keep=800 |
| M54 | Hold 不動區放寬 1 mm | **S** | reach=12 m、hold=900、目標約 10.8005 m |
| M55 | Main 即使無 partner 仍檢查 partner | K | 一般無 partner 輸入 |

M32／M33 的數值差異已用合法殼層資料確認，例如球心 `(16000,0,0)`、位置約 `(0.003008881,2.503535,4.424855)`；但仍應建立會影響公開行為邊界的見證，避免只測實作細節。

M34 找得到末位 float 不同的輸出，但本次未證明它違反既有單位長度允差或站距契約，**不將它當成已確認的非等價漏測**。同理，M25 的公開 float 輸入精確等號見證尚未建立。35／20 是執行結果，不能直接解讀為 20 個產品缺陷。

`ReachMmOf` 各 family 的結論：

| family | 現在回傳 | 是否為正確「可能接觸」上界 |
|---|---|---|
| Caster／Self | 0 | 無幾何測試，合理的特殊表示 |
| Ally | CastDistance | 一般有效射程內成立；不包含目標半徑，符合中心 gate |
| Rect：Front／Line／Dash | Length | **不是，會低估角落，A1** |
| Sector：Sweep／Cone | Radius | 是保守上界；大尺寸時不是實際最大值，受 90° 限制 |
| Caster Circle | Radius | 同上 |
| Aim-point Circle：Burst／Arc／Field | CastDistance+Radius | 同半徑才成立；跨半徑可能低估或高估，A2 |
| 超大尺寸 | 飽和 int.MaxValue | 算術飽和本身正確；不等於球面上的真實最大距離 |

一般 caster／ally 距離路徑使用 double 相減、normalize、atan2，再乘施放者實際半徑，與所宣告慣例一致。輸出最後轉 float，單位長度在本次測試內符合 ±1e-5。決定性方面，固定輸入、穩定集合下未發現亂數、可變全域狀態或順序依賴；這不構成跨 Mono／IL2CPP 的位元一致性證明。

資料鏈本次未發現功能缺陷：

```text
schema required + 四值封閉 enum
→ DTO Required.Always
→ RequireEnum<AiStyle> 精確 snake_case 檢查
→ ParseEnum<AiStyle>
→ CreatureDef.Set
→ serialized aiStyle / AiStyle getter
```

四個 enum 值及逐階匯入測試均存在；御三家九筆分別為日保距、月繞背、星突進。需補的資料測試是 importer 對 **缺欄位、null、錯誤 JSON 型別** 的 `ai_style` 專屬拒絕案例；目前新增 importer 測試涵蓋四個合法值及三種非法字串。

以下皆為 **design 類**，不混入上述 bug 判定。

| ID／嚴重度 | 重現情境 | 風險與建議 |
|---|---|---|
| **A8 · design · medium** | 同 tick 主控更換目標；支援先讀舊 MainTargetId，再執行主控 Decide | 更新順序影響支援跟隨哪個目標。整合層應使用一致快照，明訂主控先選目標、支援再消費，或集中解析主控目標 |
| **A9 · design · low** | 原先 Focus 遠處 B，改下 Follow；附近有 A | Follow 目前重新採最近存活敵人，沒有保留前一集火目標。文件應明訂 Follow 選敵是否延續鎖定，以及候選是否受玩家活動範圍限制 |
| **A10 · design · medium** | 一個高 power 傷害招與 power=0 控制招同時 ready、可及 | 控制招必定落後；Self／Ally 又落後所有合格敵對招。這符合現行規格，但可能壓低治療／控制效用。若不符合玩法，應先修改效用規則與設計，再改實作 |
| **A11 · design · low** | 無存活敵人，自身或隊友受傷，治療招 ready | `targetIndex<0` 直接禁止全部施招；補血／buff 無法在脫戰時使用。此限制已存在 XML／測試，設計正文未明訂。應確認並寫入設計；若允許脫戰輔助，將敵人目標需求限於 Hostile |