> 原始輸出：`codex exec -s read-only -m gpt-6.1-sol`，對象 `283f268`。未修改任何檔案。Claude Opus 修正：A4 只在切向分量精確為 0 時才 fallback、A2 XML 與 combat.md 改寫為近似門檻（可能漏放或誤放，命中由 MoveGeometry 決定）、A5 測試先存差值再 Assert、矩形角落改 decimal 精確 ceil、combat.md 第二條改寫為 power／slot 規則、Along 註解修正；新增 M13/M54（<1 mm 邊界）、M30（跨半徑 1e-10 m）、M32/M33（16 km 球心）、M52（1280 m 半徑大站距）見證。

判定 fail

審查對象：`283f268e6579a4725f0fe95104f851d267a1619e`。

**A4 尚未完整修正；A2 的契約處置方向可接受，但文字仍不正確；零配置測試有 Mono 可重現的假失敗；A7 仍有測試缺口。** 全程未修改檔案、未 commit；結束時 tracked diff 為空。

## 驗證結果

使用指定原始碼、正式 Unity.Mathematics／NUnit 與 `Data/balance.json`，以 Roslyn 編譯至記憶體。Fixture 的專案路徑改由 runner 提供；相關常數支架與正式值一致。

| 驗證 | 結果 |
|---|---|
| 原樣執行 `CombatAiTests`，CLR runner | **31 pass／1 fail** |
| 原樣執行，Unity 隨附 standalone Mono | 連續三次均 **31 pass／1 fail** |
| 僅在記憶體修正配置量測的 Assert 求值順序 | CLR **32/32**；Mono 連續三次 **32/32** |
| 55 個變異，各自重新編譯、執行上述排除量測假陽性的測試 | **47 killed／8 survived** |
| 補充對抗探針 | 確認 A4、M13、M30、M32、M33、M52、M54 的公開行為差異 |

未啟動 Unity Editor 或 IL2CPP；本次 Mono 結果不等同 Editor Test Runner 驗證。

## A1–A7 逐項判定

| 項目 | 判定 | 核對結果 |
|---|---|---|
| **A1** 矩形角落 gate | **原問題 fixed** | 原角落接觸見證通過。改用外接角落距離，修正長度不足以涵蓋寬度的問題。極大數值的精確 `ceil` 契約另有低風險誤差，見下文。 |
| **A2** 跨半徑 aim-point | **partial** | 可以接受 AI gate 採近似、命中另走幾何；但 XML 與 `combat.md` 尚未正確同步，也缺少近似行為回歸測試。 |
| **A3** 超大站距繞圈 | **fixed** | `Along` 夾至 π/2，新增大 reach 見證通過；非負合法角度不再繞圈。 |
| **A4** 微小非退化方位 | **partial，仍有反例** | 同半徑的原始 `1e-10 m` 見證通過；不同半徑時，相對門檻仍會丟棄可表示的非零方位。 |
| **A5** 成功路徑配置 | **實作 fixed；新增測試有錯** | 殼層訊息只在失敗時建立。修正量測方式後，本次量測為零配置；原新增測試會量到自己的 boxing。 |
| **A6** 公開契約文字 | **主要修正 fixed** | Main／Support 對應、default 例外、必選／NoCast、未 ready 招式仍參與站位、AiTuning 配置說明均已補正。enum 成員仍只有 summary，逐成員契約政策仍待 Q-T7。 |
| **A7** 測試缺口 | **partial** | 原 20 個存活變異有 12 個被新增測試殺死；仍有 6 個已證明不等價的存活變異。 |

## 未收斂問題與修正本身的檢查

### 1. A4：相對退化門檻仍會選錯大圓

位置：[CombatAi.cs:558](C:/Users/user/Documents/app/cosmos_chimera/Assets/_Project/Features/Combat/Runtime/Model/CombatAi.cs:558)。

合法輸入：

```text
R      = 640
self   = (0, 640, 0)
target = (0, 1280, 1e-10)
target.Forward = +X
style  = KeepDistance
reach  = 10000 mm
```

自身與目標決定的大圓位於 YZ 平面，但回傳：

```text
DestinationDirection ≈ (0.01249967, 0.9999219, 7.811889e-14)
```

也就是沿 +X 站出約 8 m。

原因：

```csharp
len > 1e-9 * math.length(delta)
```

此處 `delta` 約 640 m，門檻約 `6.4e-7 m`；合法的 `1e-10 m` 切向分量被當成退化。這不是精確重合或精確對蹠，XML 也沒有揭露此例外。

應保留可表示的方位，或明訂近退化範圍及方向例外；目前不能將 A4 標為完整 fixed。

### 2. A2：近似策略可接受，契約文字仍須修正

跨高度的原始行為仍可重現：

| 目標半徑 | 施放者半徑上的距離 | gate | 實際接觸 | AI |
|---:|---:|---:|:---:|---|
| 320 m | 16 m | 15 m | true | NoCast |
| 640 m | 14 m | 15 m | true | slot 0 |
| 1280 m | 14 m | 15 m | false | slot 0 |

採近似是可接受的 AI 策略，因為最終命中仍獨立驗證。但目前有三處文字問題：

- XML 的「An upper bound … at the caster's radius」同半徑限制正確。
- **「slightly beyond」不準確**：延伸量依 `casterRadius / anchorRadius` 縮放，合法殼層內可相差數倍。
- **「A wrong gate costs one cooldown」不完整**：false negative 會漏放招；false positive 也可能每次 CD 結束後再次誤放，沒有「僅一次」保證。
- [combat.md:59](C:/Users/user/Documents/app/cosmos_chimera/docs/design/combat.md:59) 仍稱可及距離為判定體能接觸的「最大地表距離」，與新的近似契約矛盾。

應明寫：gate 可能漏放或誤放，最終接觸仍由 `MoveGeometry` 決定；並同步設計正文及跨半徑測試。

### 3. A5：零配置測試會量到自己的 boxing

位置：[CombatAiTests.cs:401](C:/Users/user/Documents/app/cosmos_chimera/Assets/_Project/Features/Combat/Tests/EditMode/CombatAiTests.cs:401)。

```csharp
Assert.AreEqual(0, GC.GetAllocatedBytesForCurrentThread() - before);
```

實際 IL 順序是：

```text
box System.Int32                 // expected
call GetAllocatedBytesForCurrentThread
...
call AreEqual(System.Object, System.Object)
```

expected 的 boxing 發生在第二次配置計數之前，被算進量測。Unity 隨附 Mono 的 Debug／Release 編譯均連續三次回報 **16 B**；CLR runner 本次回報 **48 B**。

僅在記憶體改為：

```csharp
long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
Assert.AreEqual(0, allocated);
```

CLR 與 Mono 均通過；Mono 連跑三次 **32/32**。因此殼層字串修法成立，但新增測試目前不可靠。

### 4. `ReachMmOf`：飽和安全；極大矩形的精確公式有 1 mm 誤差

double 中間值沒有本次輸入域的溢位問題；aim-point 使用 long 相加，兩種飽和測試均通過。

另有合法 `Build` 輸入：

```text
Rect length = 1,000,000,000 mm
width       = 1 mm
```

數學上：

```text
ceil(sqrt(length² + 0.5²)) = 1,000,000,001
```

實際回傳 `1,000,000,000`，因 double 的平方根已捨入成整數。

這違反精確 `ceil` Post；但尺寸已超過支援域內任何 π/2 接觸距離，**本次未因此找到漏放招反例**。屬低風險契約／取整邊界問題，不推翻 A1 原角落問題已修正的結論。

### 5. `Along` 與 `RequireInDomain`

- **`Along` clamp 正確**：合法站距為非負值；新增 π/2 邊界見證通過。Leash 返回分支成立時，半條 leash 對應角度本來就小於 π/2，因此不會被額外截短。
- **`RequireInDomain` 訊息改法正確**：成功分支使用 `string.Empty`，失敗分支保留原診斷訊息；未改變殼層接受條件。
- `Along` 註解把「超過 π/2」描述成開始繞圈並不精確；最短距離折返發生於超過 π。π/2 在這裡是主動選定的幾何限制。

## 55 個變異結果

K＝killed，S＝survived。以下結果使用先記錄配置差值的記憶體版測試，避免 A5 假失敗把全部變異誤算成 killed。

| ID／結果 | ID／結果 | ID／結果 | ID／結果 | ID／結果 |
|---|---|---|---|---|
| M01 K | M12 K | M23 K | M34 **S** | M45 K |
| M02 K | M13 **S** | M24 K | M35 K | M46 K |
| M03 K | M14 K | M25 **S** | M36 K | M47 K |
| M04 K | M15 K | M26 K | M37 K | M48 K |
| M05 K | M16 K | M27 K | M38 K | M49 K |
| M06 K | M17 K | M28 K | M39 K | M50 K |
| M07 K | M18 K | M29 K | M40 K | M51 K |
| M08 K | M19 K | M30 **S** | M41 K | M52 **S** |
| M09 K | M20 K | M31 K | M42 K | M53 K |
| M10 K | M21 K | M32 **S** | M43 K | M54 **S** |
| M11 K | M22 K | M33 **S** | M44 K | M55 K |

**合計：47 killed／8 survived。原先 35 個 killed 均維持 killed。**

M30 的門檻已改變單位，本輪主表直接將新實作的相對常數 `1e-9` 換成 `1e-5`。另實跑恢復 r1「單位方向投影＋絕對 `1e-5`」版本，會被 A4 新測試殺死；若採此舊語義版本計分，則為 **48 killed／7 survived**。

### 存活變異的判讀

| ID | 判讀／已執行見證 |
|---|---|
| **M13** | 不等價。目標距離約 `12.0005 m`、reach 12 m：正式版 NoCast，變異版施放。新增測試用了 `12.002 m`，沒落入放寬的 1 mm 區間。 |
| **M25** | 尚未建立公開 float 輸入下的精確等號見證；**未證明等價**，也不當成已確認產品缺陷。 |
| **M30** | 新相對常數版本不等價。self 半徑 640、target 半徑 1280、切向偏移 1 mm：正式版沿 YZ，變異版沿 facing 的 +X。 |
| **M32** | 不等價；已找到合法殼層內翻轉 CastSlot 的輸入，見下表。 |
| **M33** | 不等價；同上。 |
| **M34** | 未證明違反單位長度 ±1e-5 或站位方向契約；按目前允差視為契約等價，不列為已確認漏測。**不是位元等價**，末位 float 可不同。 |
| **M52** | 不等價。caster 半徑 1280、Charge、reach 5,000,000 mm、close=500：正式版站距約 `2010.619298 m`，int 乘法版約 `1794.967300 m`。 |
| **M54** | 不等價。目標距離約 `10.8005 m`：正式版移動約 `0.000500337 m`，變異版不動。新增測試的 `10.802 m` 同樣越過了 1 mm 變異區間。 |

M32／M33 的實際公開行為見證：

```text
planet: R=8000, center=(16000,0,0)
self=(0.003008881,2.503535,4.424855)
```

| 變異 | target（float 往返表示） | reach | 正式版 CastSlot | 變異版 CastSlot |
|---|---|---:|---:|---:|
| M32 | `(1.00425875,38.0317574,37.29118)` | 48402 mm | 0 | -1 |
| M33 | `(1.22399712,38.99483,-31.5966225)` | 51279 mm | -1 | 0 |

兩組均通過殼層與 forward 前置條件。

**M07 已 killed，不能列為等價。** `acos` 版本在同點的非軸向單位向量上因 dot 捨入產生非零距離，被新增零 reach 同點測試殺死。

## A8／A9／A11 與 XML Post

| 條款 | 結論 |
|---|---|
| **A8：同 tick 主控先算，再供支援使用** | 與 `MainTargetId`「主控當前目標」語義一致。排程屬整合層責任，`Decide` 本身不保證呼叫順序。 |
| **A9：Follow 不延續 Focus，取最近存活敵人** | 對接收 Follow 指令的 Main，與 XML／實作一致；API 不保存前次 Focus。Support 仍依既定規則優先主控目標。 |
| **A11：沒有存活敵人，任何招式都不施放** | 與 XML、實作及測試一致；敵對招優先於 self／ally 也一致。 |

新增第二條中的「效果型敵對招（power 0）排在傷害招之後」則需收窄：XML 實際規則是 **最高 Power，再取小 slot**，沒有按傷害／效果型別排序。

schema 允許傷害招 power=0；當效果招 slot 0、傷害招 slot 1 都為 power=0 時，實測選 slot 0。建議正文直接寫 Power／slot 規則，避免宣稱目前 API 無法表達的型別優先序。

通過複審前，至少需收斂 **A4、A2 文件同步、A5 量測測試，以及六個已確認不等價的存活變異見證**。