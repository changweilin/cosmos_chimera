# P2-06 對抗式審查 · SOL

> 原始輸出：`codex exec -s read-only -m gpt-6.1-sol`，對象為 P2-06 未提交工作樹。未修改任何檔案。

**SOL · P2-06 唯讀對抗式審查 · 總判定：FAIL**

存在可重現的不變式破壞與冷卻繞過。現有測試 **35/35 通過**，但 **55 項變異中有 11 項存活**。

**方法與限制**

- 審查工作樹的 `git diff`、指定未追蹤檔、設計文件及最後一條 P2-06 交接。
- 依 P1 r5 方法，以 Roslyn 3.7、C# 8、Release 最佳化、`UNITY_EDITOR` 將來源與變異編譯至 `MemoryStream`，反射執行 NUnit。
- 環境：PowerShell 5.1、CLR `4.0.30319.42000`、專案 NUnit 3.5、Newtonsoft.Json 13。
- `TeamStateTests`、`PlayerVitalsTests` 原樣執行。`BalanceFixture` 保留原始路徑／JSON 讀取及本次新增兩個方法，移除無關 helper；只以容器替代 `Application.dataPath`。規則依賴使用原始 `Contract`、`Pcg32`、`DerivedStats`、`HitResolver`。
- 未啟動 Unity，結果不代表正式 EditMode、IL2CPP 或其他後端驗證。操作序列檢查不是全狀態空間證明。
- 未修改 repo、暫存檔或 roadmap。最後核對時，六個審查對象檔案與記錄的 SHA-256 一致。
- 基線及結束重跑均為 **35/35**；11 項存活變異另以獨立反例驗證，全部呈現「原始碼通過、變異失敗」。

**T1 · 高 · 公開集合洩漏可變內部狀態**

位置：[Team.cs:91](C:/Users/user/Documents/app/cosmos_chimera/Assets/_Project/Features/Combat/Runtime/Model/Team.cs:91)，另見第 95、99 行。

`IReadOnlyList<int>` 的實際物件是內部 `List<int>`，可合法轉型後修改：

```csharp
var t = new TeamState(new[] { 1, 2 }, 3000);
t.SetPool(new[] { 1, 2 });
t.Summon(1, 0);

((IList<int>)t.Pool).Clear();
((IList<int>)t.Roster).Clear();
((IList<int>)t.Active).Add(99);
```

實測：`Roster=[]`、`Pool=[]`、`Active=[1,99]`。

- **預期：** Roster 固定，`Active ⊆ Pool ⊆ Roster`，成員上下限成立。
- **實際：** 外部可直接改動全部集合；同樣能在戰鬥中改池，或直接把退場成員加回 Active。
- **建議：** 回傳快取的唯讀 wrapper 或不可變快照，避免暴露原始 List。測試須嘗試透過 `IList<int>` 修改。

**T2 · 高 · `SetPool(t.Pool)` 清空自身輸入並破壞不變式**

位置：[Team.cs:189](C:/Users/user/Documents/app/cosmos_chimera/Assets/_Project/Features/Combat/Runtime/Model/Team.cs:189)。

```csharp
var t = new TeamState(new[] { 1, 2 }, 3000);
t.SetPool(new[] { 1, 2 });
t.Summon(1, 0);

var result = t.SetPool(t.Pool);
```

實測：`result=Ok`、`Pool=[]`、`Active=[1]`。

- **預期：** 設定相同池後仍為 `[1,2]`，Active 仍是 Pool 子集。
- **實際：** 驗證完成後 `_pool.Clear()` 同時清空參數，再 `AddRange` 空集合。回傳 Ok 卻違反 Post 與不變式。
- **建議：** 清空前保存候選集合快照，再以快照驗證及提交。僅修 T1、改回傳唯讀 wrapper，仍無法消除此別名問題。

**T3 · 中 · `long.MaxValue` 附近可繞過切換冷卻**

位置：[Team.cs:343](C:/Users/user/Documents/app/cosmos_chimera/Assets/_Project/Features/Combat/Runtime/Model/Team.cs:343)。

```csharp
long now = long.MaxValue - 1;
t.Summon(1, now);
t.Recall(1, now);
t.SwitchCooldownRemainingMs(now);
t.Summon(2, now);
```

實測依序為：`Ok`、`Ok`、`0`、`Ok`。

- **預期：** 收回後剩餘 3000 ms；同時間召喚回 `SwitchOnCooldown`。
- **實際：** `nowMs + _switchCooldownMs` 溢位成負數，冷卻立即被判定結束。
- **建議：** 保存冷卻開始時間，以安全的 elapsed 比較及計算剩餘時間。單純將結束時間飽和至 `long.MaxValue`，仍會在最大時間點提前允許操作。

**T4 · 低 · 時間契約與實際副作用不一致**

位置：[Team.cs:115](C:/Users/user/Documents/app/cosmos_chimera/Assets/_Project/Features/Combat/Runtime/Model/Team.cs:115)，另見第 227、251、278、317、346 行。

兩個已重現的差異：

1. `Summon(1,0)` 後呼叫 `Summon(99,100)`，回 `NotInRoster`，但 `_lastNowMs` 從 `0` 改成 `100`。Recall、Switch 的拒絕路徑也會推進時間。這違反「Any other result leaves state unchanged」，Side effects 亦未揭露。
2. `Summon(1,0)` → `SwitchCooldownRemainingMs(100)` → `Summon(2,99)`，最後回 `Ok`。依「每個先前傳入的 nowMs」契約，最後一步是刻意違反 Pre，應由 Contract 偵測；實際只記錄命令時間，漏記查詢時間。

此處**未證實冷卻繞過**。拒絕時間本來也包含於文字 Pre，不能直接推論呼叫者可以回退至拒絕前時間。

**建議：** 明確定義時間水位是否屬於狀態；揭露拒絕命令也會推進時間。若所有呼叫都要求全域單調，查詢也須納入檢查機制；若查詢維持純函式，需修正其 Pre。

**T5 · 中 · 未選上場池即可開始戰鬥並立即全滅**

位置：[Team.cs:200](C:/Users/user/Documents/app/cosmos_chimera/Assets/_Project/Features/Combat/Runtime/Model/Team.cs:200)。

```csharp
var t = new TeamState(new[] { 1 }, 3000);
t.BeginCombat();
```

實測：`InCombat=true`、`Pool.Count=0`、`IsWipedOut=true`。

- **預期：** 依 P2-06「上場池 1–4 隻」，開始戰鬥前已選定合法池。
- **實際：** BeginCombat 的唯一 Pre 是 `!InCombat`，接受尚未初始化的空池。
- **建議：** BeginCombat 增加「已選定非空上場池」前置條件及測試。建構期間允許空池可維持。

**T6 · 低 · Summon 的拒絕原因優先序不符合註解**

位置：[Team.cs:227](C:/Users/user/Documents/app/cosmos_chimera/Assets/_Project/Features/Combat/Runtime/Model/Team.cs:227)，另見第 232、238、333 行。

序列：Pool=`[1,2,3]` → BeginCombat → Summon(1) → Faint(1) → Summon(2) → Summon(3) → Summon(1)。

最後同時符合 `NoFreeSlot` 與 `Retired`。註解承諾依上方 TeamResult 順序檢查，該順序為 `NoFreeSlot` 在 `Retired` 前；實際回 `Retired`。

**建議：** 明文列出真正的拒絕優先序，或調整檢查順序，並加入同時符合多個拒絕條件的測試。

**V1 · 中 · 跨正負時間範圍的 elapsed 溢位會恢復無敵並鎖住翻滾**

位置：[PlayerVitals.cs:108](C:/Users/user/Documents/app/cosmos_chimera/Assets/_Project/Features/Combat/Runtime/Model/PlayerVitals.cs:108)，另見第 159 行。

目前 Pre 沒有限制 `nowMs >= 0`，下列序列符合時間單調要求：

```csharp
var p = new PlayerVitals(new PlayerVitalsConfig(100, 600, 400, 800));
p.TryRoll(-1);

p.IsInvulnerable(long.MaxValue);
p.IsRolling(long.MaxValue);
p.ApplyDamage(1, long.MaxValue);
p.TryRoll(long.MaxValue);
```

實測依序為：`true`、`true`、`0`、`OnCooldown`。

- **預期：** 舊翻滾早已結束；無敵／動畫均 false，傷害為 1，新翻滾允許開始。
- **實際：** `nowMs - _rollStartMs` 溢位成負數，小於所有正長度，造成上述錯誤。
- **建議：** 單調檢查後使用可涵蓋完整 long 時間差的安全 elapsed 計算；或正式縮限時間域，並同步修改所有 Pre 與檢查。

另外實測從 `long.MaxValue-800` 開始翻滾，`+399/+400`、`+599/+600`、`+799/+800` 邊界全部正確。問題是**跨符號範圍的減法**，不能概括為所有接近最大值的翻滾都失敗。

**V2 · 低 · 拒絕翻滾仍改狀態，查詢後倒退漏檢**

位置：[PlayerVitals.cs:100](C:/Users/user/Documents/app/cosmos_chimera/Assets/_Project/Features/Combat/Runtime/Model/PlayerVitals.cs:100)，另見第 145、158、162 行。

- `TryRoll(0)` → `TryRoll(100)` 回 `OnCooldown`，但 `_lastNowMs` 從 `0` 改為 `100`，違反「Refusals leave state unchanged」；Side effects 只宣告成功時記錄開始時間。
- `TryRoll(0)` → `IsRolling(100)` → `TryRoll(99)`，最後回 `OnCooldown`，未依文字 Pre 偵測倒退。
- `ApplyDamage` 也推進時間水位，Side effects 僅記載 HP。

**建議：** 與 T4 統一時間契約及副作用描述。

**V3 · 低 · Config 的 MaxHp getter 對 default 值承諾過強**

位置：[PlayerVitals.cs:42](C:/Users/user/Documents/app/cosmos_chimera/Assets/_Project/Features/Combat/Runtime/Model/PlayerVitals.cs:42)。

```csharp
default(PlayerVitalsConfig).MaxHp // 0
```

getter 宣告 `Pre: none; Post: >= 1`，但 struct 的 default 值不執行建構子，因此此 Post 不成立。

`PlayerVitals` 建構子有正確拒絕 default config，故未發現此路徑造成遊戲狀態錯誤。

**建議：** getter 契約明訂 config 必須透過有效建構子建立。

**變異測試表**

所有變異均獨立套用至記憶體來源，再執行完整既有 35 案例。共 **44 killed／11 survived**。

| ID | 單點變異 | 結果 |
|---|---|---|
| M01 | MaxRoster：6→5 | killed |
| M02 | MaxPool：4→3 | killed |
| M03 | MaxActive：2→3 | killed |
| M04 | 移除 roster null 檢查 | killed |
| M05 | 移除 roster 唯一性檢查 | killed |
| M06 | SetPool 忽略戰鬥鎖定 | killed |
| M07 | SetPool 忽略重複成員 | killed |
| M08 | SetPool 忽略 Roster 資格 | killed |
| M09 | SetPool 允許排除 Active | killed |
| M10 | SetPool 不清除舊池 | killed |
| M11 | BeginCombat 不設戰鬥旗標 | killed |
| M12 | EndCombat 不清除退休集合 | killed |
| M13 | Summon 忽略場上數量上限 | killed |
| M14 | Summon 改插入 Active 首位 | killed |
| M15 | Summon 也開始冷卻 | killed |
| M16 | Recall 不移除 Active | killed |
| M17 | Recall 不開始冷卻 | killed |
| M18 | Switch 改為移除後附加，失去原位置 | killed |
| M19 | Switch 不開始冷卻 | killed |
| M20 | 允許退休成員重新進場 | killed |
| M21 | 進場冷卻比較 `>`→`>=` | killed |
| M22 | Recall 冷卻比較 `>`→`>=` | **survived** |
| M23 | Faint 不移除 Active | killed |
| M24 | Faint 不加入退休集合 | killed |
| M25 | Faint 清除正在進行的冷卻 | killed |
| M26 | Faint 開始冷卻 | killed |
| M27 | 冷卻多加 1 ms | killed |
| M28 | Team 命令不更新時間水位 | killed |
| M29 | EndCombat 額外清除冷卻 | **survived** |
| M30 | EndCombat 額外清空 Active | **survived** |
| M31 | BeginCombat 額外清除冷卻 | **survived** |
| M32 | Switch 的 NotActive 分支不推進時間 | **survived** |
| M33 | 冷卻結束時間窄化為 int | **survived** |
| M34 | 移除 maxHp 下限檢查 | killed |
| M35 | 移除負無敵長度檢查 | killed |
| M36 | 移除無敵長度 ≤ 動畫長度檢查 | killed |
| M37 | 移除動畫長度 ≤ 冷卻檢查 | killed |
| M38 | 允許 default config 建立玩家 | killed |
| M39 | 初始 HP 少 1 | killed |
| M40 | IsDowned 固定 false | killed |
| M41 | TryRoll 忽略 Downed | killed |
| M42 | 翻滾冷卻比較 `<`→`<=` | killed |
| M43 | 翻滾冷卻誤用動畫長度 | killed |
| M44 | 無敵／動畫視窗改成閉區間 | killed |
| M45 | 無敵視窗誤用動畫長度 | killed |
| M46 | 動畫視窗誤用無敵長度 | killed |
| M47 | ApplyDamage 不檢查無敵 | killed |
| M48 | 傷害不限制至剩餘 HP | killed |
| M49 | 移除負傷害檢查 | killed |
| M50 | Player 命令不更新時間水位 | killed |
| M51 | 零冷卻時拒絕後續翻滾 | **survived** |
| M52 | 零長度視窗誤給 1 ms | **survived** |
| M53 | Downed 且仍有冷卻時回 OnCooldown | **survived** |
| M54 | 翻滾冷卻 elapsed 窄化為 int | **survived** |
| M55 | 無敵／動畫 elapsed 窄化為 int | **survived** |

以下獨立反例全部在原始碼通過，並殺死對應存活變異：

| ID | 補充測試序列與應有結果 |
|---|---|
| M22 | Recall@0 → Summon@3000 → Recall@3000；最後應 Ok |
| M29 | 戰鬥中 Recall@0 → EndCombat → Summon@1；仍應受冷卻限制 |
| M30 | Summon → EndCombat；Active 應保留 |
| M31 | 戰鬥外 Recall@0 → BeginCombat → Summon@1；仍應受冷卻限制 |
| M32 | Switch 的 outId 非 Active，時間 200 → 命令時間 199；後者應拋 ContractException |
| M33 | `now=int.MaxValue` 時 Recall；剩餘冷卻應為 3000 |
| M51 | duration／invulnerability／cooldown 全為 0；同時間再次翻滾應 Ok |
| M52 | invulnerability=0；翻滾開始當下也應無敵=false |
| M53 | Roll@0 → 致命傷害@400 → TryRoll@400；應回 Downed |
| M54 | Roll@0 → TryRoll@`2^32`；應 Ok |
| M55 | Roll@0 → IsInvulnerable@`2^32`；應 false |

**已驗證正確的範圍**

既有正常序列符合開場／補位不啟動冷卻、兩者仍受既有冷卻限制、Faint 保留冷卻、退場至 EndCombat、半開無敵窗、HP 下限與 Downed 拒絕翻滾。`Data/balance.json` 的 team／player 數值與指定設計一致。

下一步由 OPUS／SONNET 修正 T1–T3、T5 與 V1，補齊上述存活變異對應測試，並統一時間 DbC 後交 SOL 複審。