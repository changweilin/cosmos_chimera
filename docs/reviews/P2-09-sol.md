> 原始輸出：`codex exec -s read-only -m gpt-6.1-sol`，對象為 main `2a6eae0`（Effects 範圍與 `2386a79` 相同）。未修改任何檔案。Claude Opus 已依 E1–E6 修正（見 phase-2.md 交接）。

# P2-09 對抗式審查 · SOL

**總判定：FAIL**

審查對象：`2386a79d8d923366296bb53ed364485372af9214`（結束時 HEAD）。

既有 `EffectsTests` **44/44 通過**；32 個變異結果為 **25 killed／7 survived**。發現合法數值輸入的錯誤飽和、範圍上限語意不一致、未檢查的 Pre，以及 schema／importer／戰鬥契約落差。

## 方法與限制

- 已讀指定規範、戰鬥設計、Q-C17、P2-09 交接與 ADR-0004。
- 使用 PowerShell 5.1、CLR 4、Roslyn 3.7、C# 8、Release 最佳化及 `UNITY_EDITOR`；來源、變異與追加反例均編譯至 `MemoryStream`，反射執行既有 NUnit 測試。
- 使用專案實際 `Unity.Mathematics.dll`、套件快取的 NUnit，以及實際 `Data/balance.json`。僅替代 `SerializeField` attribute 與 `Application.dataPath`；戰鬥規則和數值依賴使用原始來源。
- importer 驗證使用原 DTO、相同 serializer 設定及原始 `Resolve`／`CheckRule`／轉換成員；未呼叫資產寫入階段。
- schema 採靜態條件核對，未執行完整 JSON Schema validator。未執行 Unity Editor、完整 520 項 EditMode、IL2CPP 或 importer 資產往返測試。
- 審查途中 HEAD 推進一次，變更僅涉及 Player；本次審查範圍沒有差異。結束時基線重跑仍為 **44/44**。
- **未修改任何 repo 檔案或 roadmap。**

## Findings

### E1 · 中 · 專精／抗性在 clamp 前溢位，飽和方向反轉

位置：`Assets/_Project/Features/Combat/Runtime/Model/Effects.cs:584`、同檔第 591 行。

`baseMastery + delta`、`baseResist + delta` 先執行 Int64 加法，再 clamp。公開 Pre 沒有限制 `delta`，以下均為合法輸入：

| 最小重現 | 應有結果 | 實際結果 |
|---|---:|---:|
| `EffectiveMastery(1, long.MaxValue)` | 1000 | 0 |
| `EffectiveResist(1, long.MaxValue)` | 100 | -100 |
| `EffectiveResist(-1, long.MinValue)` | -100 | 100 |

回傳範圍仍符合 Post 的上下限，但有效修正值的語意錯誤。實際 `EffectBook` 很難累積到此數量，不代表公開 API 可以接受後算錯。

**建議修正：** 先以 `delta` 和「上下界減 base」比較，確定未飽和才加法；補正、負極端值測試。

### E2 · 中 · `ApplyRange` 把遠程上限套到近戰，未保留原本的範圍限制

位置：`Assets/_Project/Features/Combat/Runtime/Model/Effects.cs:669`、同檔 `Capped`。

原本 `MoveGeometry.Build` 對近戰採線性尺寸縮放，對 mid／long 才套 `RangedCapMm`。`ApplyRange` 沒有保留這項區別，所有 Rect／Sector 長度都使用遠程上限。

最小重現：`Line`、無 dash、STR＝0、`range_mod_percent＝+50`。

| RangeClass | 原 Reach | 依原範圍規則修正 | 實際 |
|---|---:|---:|---:|
| Melee | 30000 | 45000 | 36000 |
| Melee | 37000 | 55500 | 37000 |
| Mid | 30000 | 36000 | 36000 |

`Capped` 還自行加入「原值超過 cap 時，buff 不得增加」的例外；設計文件沒有定義這項近戰規則。這與「乘完後套回**原本**上下限」不一致。

**建議修正：** 保留範圍類別或明確的上限策略，讓效果沿用建構該 volume 時的限制；補近戰與遠程在 cap 兩側的案例。

### E3 · 中 · 公開 Pre 未檢查，非法事件可以實際觸發效果

位置：`Assets/_Project/Features/Combat/Runtime/Model/Effects.cs:467`、第 476、661 行。

最小重現與實測結果：

| 呼叫 | 契約要求 | 實際 |
|---|---|---|
| `NotifyHitTaken((Element)99, 0, out _)` | Element 必須合法 | 不拋例外；`any` 特性仍加上 buff |
| `NotifyMoveUsed((Element)99, 0, out _)` | Element 必須合法 | 不拋例外 |
| `ApplyRange(line, 0, stats, null)` | config 非 null | 直接回傳 |
| `ApplyRange(dash, 10, stats, null)` | config 非 null | 直接回傳 |

後兩者因 early return 略過間接的 `Multiplier` 檢查。

**建議修正：** 在分支及狀態更新之前直接呼叫 `Contract.Requires`；事件驗證需涵蓋無特性、屬性限定及 `any` 三種情況。

### E4 · 中 · schema 合法資料通過 importer，卻被 `EffectBook` 拒絕

位置：`Assets/_Project/Features/Combat/Runtime/Model/Effects.cs:357`、`Data/schemas/trait.schema.json`、`Assets/_Project/GameData/Editor/DataImporter.cs:332`。

**反例 A：持續觸發的治療**

```json
{
  "id": "t",
  "trigger": { "type": "always" },
  "effects": [
    { "type": "heal_percent_max_hp", "params": { "percent": 20 } }
  ]
}
```

schema 允許，importer `Resolve` 接受；建立 `EffectBook` 時拋：

```text
Precondition failed: a heal needs an event trigger
```

**反例 B：9 個合法效果**

將 `effects` 設為 9 個合法 `mastery_mod`：

- schema 僅要求 `minItems: 1`。
- importer 沒有 8 個的上限。
- `TraitDef` 承諾 `Count >= 1`。
- `EffectBook` 拒絕超過 8 個。

這兩例都符合 importer「資料已通過 schema」的前提，不能由 ADR-0004 的部分驗證定位排除。

**建議修正：** 在 schema 和 importer 加入持續觸發禁用治療的交叉限制；效果數量上限則統一至各層，或移除未經資料契約定義的 8 個限制。

### E5 · 中 · 既有測試未區分七種非等價錯誤

七個 survived 變異皆有獨立反例；全部呈現「原始碼通過、變異失敗」：

| 變異 | 最小追加反例 | 正確／變異結果 |
|---|---|---|
| M03 | duration＝1，`StacksOf(..., now:1)` | 0／1 |
| M12 | HP＝maxHP＝`int.MaxValue`，below 50% | 不啟用／啟用 |
| M19 | CD 來源 −100%、+100%，最後才算倍率 | 1000／1100 |
| M20 | `ApplyPercent(1, 50, cfg)` | 2／1 |
| M21 | CD＝1001、SPI＝0、+1% | 1012／1011 |
| M30 | `TicksDue(0, 0)` | 0／1 |
| M32 | placement＝`1L << 32`，查 placement tick | 1／0 |

**建議修正：** 將上述案例納入正式測試。尤其補齊相反方向百分比互相抵銷，以及 CD 小於半單位的正餘數；目前測試不足以驗證指定的 clamp 順序和 ceil。

### E6 · 低 · DbC 文件未完整描述實際接受範圍

位置：`Assets/_Project/Features/Combat/Runtime/Model/Effects.cs:17`、第 100、299 行。

- `EffectSpec`、`TriggerSpec` 建構子沒有在 Pre 宣告 `element` 必須合法，實作卻一律檢查，包括文件稱為無意義的 element 欄位。
- 例如 `EvasionModPercent` 或 `Always` 搭配 `(Element)99`，符合列出的其他 Pre，仍拋 `ContractException`。
- 公開 `ApplyOutcome` 及其成員僅有 summary，缺少要求的 Pre／Post／Side effects。

**建議修正：** 明列 element 條件，補齊公開契約文件及對應違反案例。

## Mutation testing

每個變異獨立套用於原始 `Effects.cs`，均成功編譯，並執行相同的 **44 個既有案例**。

| ID | 變異 | 結果 |
|---|---|---|
| M01 | Apply 到期清理 `<=` → `<` | killed |
| M02 | Collect 到期判定 `>` → `>=` | killed |
| M03 | StacksOf 到期判定 `>` → `>=` | **survived** |
| M04 | 疊加鍵忽略來源 id | killed |
| M05 | 疊加鍵忽略效果序號 | killed |
| M06 | 滿層後超過 MaxStacks | killed |
| M07 | 刷新改為延長原到期時間 | killed |
| M08 | 加總時忽略層數 | killed |
| M09 | mastery 改為覆寫加總 | killed |
| M10 | HP below 改為含等號 | killed |
| M11 | HP above 改為含等號 | killed |
| M12 | HP×100 使用 Int32 中間值 | **survived** |
| M13 | swap 通知改觸發 summon | killed |
| M14 | hit_taken 忽略 any | killed |
| M15 | move_used 忽略屬性比對 | killed |
| M16 | 反轉持續觸發條件 | killed |
| M17 | 倍率下限改為 0% | killed |
| M18 | 倍率上限改為 1000% | killed |
| M19 | 各來源先 clamp，再加總 | **survived** |
| M20 | ApplyPercent half-up 改 floor | **survived** |
| M21 | CD ceil 改 half-up | **survived** |
| M22 | 專精上限減 1 | killed |
| M23 | 抗性下限改為 0 | killed |
| M24 | range 效果也縮放 Circle 半徑 | killed |
| M25 | dash 也接受 range 修正 | killed |
| M26 | 移除 range 上限 | killed |
| M27 | 移除治療至少 1 點 | killed |
| M28 | 場地總 tick 數 ceil 改 floor | killed |
| M29 | 放置時間不計 tick | killed |
| M30 | 零步長錯算 1 tick | **survived** |
| M31 | tick elapsed 窄化為 Int32 | killed |
| M32 | placement 窄化為 Int32 | **survived** |

**既有測試殺死率：25/32＝78.1%。** 七項追加反例不計入此分數。

## 已驗證正確的範圍

- 疊加鍵使用來源 id＋效果序號；沒有 caster 維度。`max_stacks＝1` 僅刷新，整疊同時到期。
- 七種 trigger 在合法通知下符合目前文件；交換上場者需依契約同時接收 summon 與 swap 通知。持續 trigger 在查詢時測試 HP。
- 場地通過 **91,644 次獨立 tick oracle 比對**，包含零步長、放置 tick、壽命終點、高位 placement、最大查詢時間與 600000 次 tick。
- `max_stacks`／`field_duration_ms` 的整數接受範圍矩陣 **13/13 通過**；heal 與 trigger 的 `max_stacks` 均被拒絕。
- balance 三個值符合定案：`field.tick_ms＝1000`、倍率下限 10%、上限 300%。
- numeric string 仍會被 DTO coercion 接受；schema 會拒絕。依 ADR-0004 的 schema 預驗證前提，本次列為既知邊界，不另計缺陷。
- TeamState／命中流程整合仍屬交接明列的 P2-10 工作，本次不將尚未整合視為缺陷。

