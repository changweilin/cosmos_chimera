> 原始輸出：`codex exec -s read-only -m gpt-6.1-sol`，對象 `402972d`。未修改任何檔案。Claude Opus 修正：`Toward` 改以 n = from × delta、t = n × from 建切向量（對蹠／同向時方向任意但仍是真切向，站距正確），新增 5 組退化對齊見證。

判定 fail

審查對象：`402972dcf4c69ab0fba17fd7404cef1cbbb3488b`，分支 `p2/sol-r6-p2-09-fixes`。

**r2 指定修正均已落實，但 `Toward` 引入新的站距錯誤；另已證明存活的 M34 在目前 HEAD 不等價。** M25 不計入 fail。

全程唯讀、未 commit。結束時 HEAD 未變，staged／tracked diff 均為空。

**驗證方式與結果**

以 HEAD 原始碼、正式 Unity.Mathematics／NUnit、實際 `Data/balance.json`，透過 Roslyn 在記憶體編譯，於 Unity 隨附 standalone Mono 執行。Runner 僅提供 fixture 專案路徑及相關型別支架；常數與正式值一致，未修補測試斷言。

| 驗證 | 結果 |
|---|---|
| 原樣 `CombatAiTests` | 連續三次 **38/38 pass** |
| 55 個獨立變異，逐個重新編譯並跑全部測試 | **53 killed／2 survived** |
| 同點／對蹠／近對蹠，四種風格及五種 reach 的補充探針 | **30,000 組：0 非有限值、0 單位長度違反、0 重複輸出偏差** |
| 站距補充探針 | 找到合法輸入違反指定站距 |
| 矩形與 BigInteger 精確 oracle 比對 | **20,081 組，0 差異** |
| 預熱後 `ReachMmOf(Rect)` 10,000 次 | **0 B 配置** |

上述為 standalone Mono 結果，未執行 Unity Editor Test Runner 或 IL2CPP；CLR 補充 runner 因相依組件相容性未完成，未計入判定。

**r2 未收斂項核對**

| 項目 | 判定 | 結果 |
|---|---|---|
| A4 微小非零方位 | 原見證 fixed；另有新回歸 | 同半徑及跨半徑 `1e-10 m` 見證通過；對蹠等退化輸入另見下文 |
| A2 文字 | fixed | XML／`combat.md` 已明寫近似門檻、可能漏放或誤放，並由 `MoveGeometry` 決定命中；移除「slightly／一次 CD」保證 |
| A5 測試 | fixed | 先儲存配置差值，再進入 Assert；Mono 三次均通過，未再量到 Assert boxing |
| 矩形精確 ceil | fixed | decimal 修正與 oracle 一致，包括 `1,000,000,000 × 1 mm`、精確平方根及飽和邊界 |
| `combat.md` 第二條 | fixed | 已改為 power 優先、同 power 取小 slot，未再宣稱按傷害／效果型別排序 |
| `Along` 註解 | fixed | 已區分主動限制 π/2 與超過 π 後最短距離折返 |

**新問題：`Toward` 將投影消去誤差當成切向量，導致站距錯誤。**

位置：[CombatAi.cs:573](C:/Users/user/Documents/app/cosmos_chimera/Assets/_Project/Features/Combat/Runtime/Model/CombatAi.cs:573)。

以下輸入全部符合 Pre：

```text
planet       R=640，center=(0,0,0)
self         (1,640,0)
target       (-1,-640,0)           // 精確對蹠
forward      (0,0,1)
player       self
role/command Main / Free
partner      無
hostile      reach=10000 mm，power=1，ready=true
tuning       (keep=800, close=500, hold=900, leash=6000)
```

| 風格 | 契約要求站距 | HEAD 實際站距 |
|---|---:|---:|
| KeepDistance | 8 m | **0.003086627 m** |
| Charge | 5 m | **0.001938045 m** |
| Hold | 9 m | **0.003467128 m** |

KeepDistance 回傳：

```text
DestinationDirection ≈ (-0.001567320898, -0.999998748302, 0)
```

原因是：

```csharp
double3 t = delta - from * math.dot(from, delta);
```

此例得到的殘差約為：

```text
t = (-4.4408920985e-16, -2.2737367544e-13, 0)
dot(normalize(t), from) ≈ 0.9999999237
```

這個殘差幾乎是**徑向**，並非切向。`len > 0` 將它正規化後傳給要求切向量的 `Along`；最後的 normalize 能維持單位長度，卻無法維持指定站距。

同一根因也影響同方向、不同半徑：保持 self，將 target 改成 `(0.5,320,0)`，KeepDistance 要求 8 m，實際只有 **0.003164768 m**。

僅在記憶體恢復 r2 的相對門檻條件後，上述對蹠站距恢復約 **8／5／9 m**，確認這是本次條件變更造成的回歸。這不表示應恢復舊門檻，因為舊門檻仍會破壞已修正的微小方位見證。

修正需要同時保留可表示的非零方位，並確保交給 `Along` 的方向確實切向。對蹠時可以任選大圓，但徑向殘差不能代表大圓方向；「近對蹠時任一大圓皆為最短路徑」的新增註解也不成立。

**矩形 decimal 邊界與飽和**

未發現新問題。對非負 Int32 尺寸：

- `need = 4L² + W² ≤ 5·int.MaxValue² ≈ 2.306×10¹⁹`，decimal 可精確容納。
- 根候選最大約 `2.401×10⁹`，long 安全。
- 兩個迴圈分別排除候選過小、過大，最後才飽和至 `int.MaxValue`。
- 20,081 組 oracle 比對包含零值、奇數寬度、整數根及 Int32 極值；觀察到最多向上修正一次。

**55 個變異**

K＝killed；S＝survived。M30 使用新語義：將 `len > 0.0` 改成 `len > 1e-5`。所有變異均成功編譯。

| ID／結果 | ID／結果 | ID／結果 | ID／結果 | ID／結果 |
|---|---|---|---|---|
| M01 K | M12 K | M23 K | M34 **S** | M45 K |
| M02 K | M13 K | M24 K | M35 K | M46 K |
| M03 K | M14 K | M25 **S** | M36 K | M47 K |
| M04 K | M15 K | M26 K | M37 K | M48 K |
| M05 K | M16 K | M27 K | M38 K | M49 K |
| M06 K | M17 K | M28 K | M39 K | M50 K |
| M07 K | M18 K | M29 K | M40 K | M51 K |
| M08 K | M19 K | M30 K | M41 K | M52 K |
| M09 K | M20 K | M31 K | M42 K | M53 K |
| M10 K | M21 K | M32 K | M43 K | M54 K |
| M11 K | M22 K | M33 K | M44 K | M55 K |

r2 的六個已確認不等價存活變異——M13、M30、M32、M33、M52、M54——本輪全部 killed。

| 存活變異 | 本輪判讀 |
|---|---|
| **M25** | 仍未建立公開 float 輸入下的精確 leash 等號見證；維持未證明不等價，**不計 fail** |
| **M34** | **目前 HEAD 已證明不等價**，見下方 |

M34 移除 `Along` 的 normalize。使用上述精確對蹠輸入：

```text
HEAD：向量長度 = 0.9999999765506418
M34 ：向量長度 = 1.0124215401738057
```

M34 超出公開契約的單位長度允差 ±`1e-5`，但仍通過全部 38 項測試。r2 時「未證明不等價」的結論不能延用至目前 HEAD：新 `Toward` 已破壞 `Along` 的切向量假設，使 normalize 成為必要防線。

本輪 fail 依據為**已重現的站距功能錯誤，以及已證明不等價的存活 M34**；不是 M25，也不是未完成的跨後端驗證。