> 原始輸出：`agy -p --model gemini-4-argon-high`（唯寫此報告，不能跑 shell，突變體為人工推演、未實際執行），對象 `b69dc7b`。Claude Opus 已依 K1、K2、K4、K5 修正；K3 待人工（見 phase-2.md 交接）。

判定 fail

- **審查者**：ARGON（Gemini 4 Argon，第三方對抗式審查）
- **標的提交**：`b69dc7b`（分支 `p2/sol-r6-p2-09-fixes`）
- **審查範圍**：
  - **Part A**：`Assets/_Project/Features/Combat/Runtime/Model/Cast.cs`、`Assets/_Project/Features/Combat/Tests/EditMode/CastTests.cs`，及其組合對象 `DerivedStats.cs`、`DamageModel.cs`、`HitResolver.cs`、`Effects.cs`、`Team.cs`、資料層 `MoveDef.cs` / `DataImporter.cs` / `Data/schemas/move.schema.json`，對照 `docs/design/combat.md` 與 `.claude/rules/{code-contracts,testing,unity-csharp}.md`
  - **Part B**：`tools/validate_data.py`（`_check_material_sums`）與 `tools/tests/test_validate_data.py`（對照 `docs/reviews/P2-05-P2-07-sol-r2.md` C8）
- **驗證方式**：靜態程式碼、契約證明與突變體推演（依任務限制未執行 shell，除本報告外未修改任何檔案）

---

## Part A 專項核對總覽（Checks 1–4）

| 檢查項 | 結論 | 摘要 |
|---|---|---|
| **(1) 結算順序（2026-11-07 使用者確認）** | **部分通過（見 K2）** | `BeginCast`（先拒發倒下／CD → 觸發 `on_move_used` 並治療 → 取樣加成凍結至 `CastContext` → 施招當下起算 CD）、`ReceiveHostile`（取樣目標戰前閃避／抗性 → 僅接觸時擲骰 → 扣血並於 `Hp == 0` 立即返回不觸發任何事件或效果 → 存活時依序觸發 `on_hp_cross_below`、`on_hit_taken`、掛載招式效果／記錄招式治療 → 合併單次結算治療）、`ReceiveSupport`（不進命中判定、不碰 RNG、不觸發 `on_hit_taken`）皆與 `combat.md:85-90` 完全一致。惟 `Combatant` 封裝了 `Hp` 卻漏開 `on_summon` / `on_ally_swap` 入口，導致上場／換手治療特性無合法路徑寫入 `Hp` 且缺少倒下守門（**K2**）。 |
| **(2) DbC 契約、時間水位、RNG 次數、HP 不變量、溢位** | **未通過（見 K1、K4）** | **算術與 HP 不變量通過**：`MaxHp ∈ [1, 2_000_000]`；扣血夾限 `min(raw, Hp)`，補血僅在 `1 <= Hp < MaxHp` 執行 `amount >= MaxHp - Hp ? MaxHp : Hp + amount`，恆維持 `0 <= Hp <= MaxHp`；`MoveCooldowns.Start` 中 `nowMs + cooldownMs <= 2^62 + (2^31 - 1) < Int64.MaxValue`；`crossHeal + hitHeal + moveHeal` 最大有界（特性至多 8 條效果 × 100% + 招式 100%），`EffectMath.HealAmount` 以 `Int64` 計算 `(long)maxHp * percent <= 3.4e9` 不溢位。<br>**RNG 抽樣數通過**：`ReceiveHostile` 唯一呼叫 `HitResolver.Resolve(geometricContact, false, chance, ref rng)`——`!geometricContact` 抽 0 次、`geometricContact` 固定抽 1 次 `NextUInt(1000)`（即使 `chance == 0` 或 `1000`）；`BeginCast` 與 `ReceiveSupport` 不收 `Pcg32`（0 次）。<br>**契約與水位違規**：`BeginCast` 在倒下／冷卻早退時漏檢 `slot`、時間域與 `Book` 水位，`ReceiveSupport` 在滿血瞬間治療時完全漏檢 `nowMs`（**K1**）；`HitReport.Fainted` 在 `default(HitReport)` 違反自身 `Post:`，另有多處 `default(struct)`／隱藏 `Pre:`／`Reset` 水位註解失真（**K4**）。 |
| **(3) 決定論風險（Determinism hazards）** | **通過** | 無 `Dictionary` / `HashSet` 迭代；`EffectBook._slots` 為決定性 `List` 且 `ModifierTotals` 以 `Int64` 整數加法累加（滿足交換律與結合律）；`Combatant._totals` 為每實例私有緩衝，`Book.Collect` 開頭必定 `into.Clear()`，且 `BeginCast`（`Cast.cs:401-406`）與 `ReceiveHostile`（`Cast.cs:429-442`）皆在任何後續突變前將純量讀入區域變數或值型別 `CastContext`，即使自施或 `caster == target` 亦無緩衝區別名汙染；`_mastery` 與 `_resist` 於建構子深拷貝（`Cast.cs:340-341`）。 |
| **(4) 被呼叫端 `Pre:` 銜接（Callee Pre gaps）** | **部分通過（見 K3）** | 在 `Cast.cs` 各公開方法的 `Pre:` 成立前提下，其對 `DerivedStats`、`DamageModel.Compute`（`power ∈ 0..100000`、`mastery ∈ 0..1000`、`resist ∈ -100..100`）、`HitResolver.Resolve`（`chance ∈ 0..1000`）、`EffectBook`（`MaxHp >= 1`、`0 <= Hp <= MaxHp`、已定義 `Element`、限時 `EffectSpec` 與 `1..MaxDurationMs`）、`EffectMath`、`CombatTime` 的所有內部呼叫皆嚴格滿足被呼叫端 `Pre:`（註：未播種之 `default(Pcg32)` 與 `HitResolver.Resolve` 相同，因 `Pcg32._increment` 為私有，僅於 `geometricContact == true` 呼叫 `NextUInt` 時拋出 `ContractException`）。但資料匯入層（`move.schema.json` / `validate_data.py` / `DataImporter` / `CastMove.FromDef`）允許 `shape: self|ally` 搭配 `kind: damage|both`，而 `Combatant.ReceiveSupport` 以 `Pre:` 拋例外拒絕，`ReceiveHostile` 亦排除 `self|ally`（**K3**）。 |

---

## 問題清單（Findings K1–K5）

### K1 · medium · 公開方法的早期返回略過 `Pre:`（`BeginCast` 於倒下／CD 分支漏檢 `slot`、時間域與 `Book` 水位；`ReceiveSupport` 於滿血治療完全漏檢 `nowMs`）

- **位置**：
  - `Assets/_Project/Features/Combat/Runtime/Model/Cast.cs:376-377, 388-396`（`Combatant.BeginCast`）
  - `Assets/_Project/Features/Combat/Runtime/Model/Cast.cs:475-476, 488-492, 498-503`（`Combatant.ReceiveSupport`、`Combatant.HealPercent`）
- **依據**：`.claude/rules/code-contracts.md`（「實作端若檢查，用 `Contract.Requires` 快速失敗，不得靜默修正或吞掉」），且本專案同模組回歸測試 `EffectsReviewSolTests.cs:62-78, 171-179`（E3 / E7）已確立「前置條件必須在任何 early return 前完成檢查」之規範。
- **反例輸入**：
  1. **`BeginCast` 倒下早退（`Cast.cs:388-391`）**：令 `victim` 已倒下（`victim.IsFainted == true`、`Cooldowns.SlotCount == 2`、`victim.Book` 水位為 `500`），呼叫：
     ```csharp
     victim.BeginCast(-1, Damage(), -1, out _); // 或 slot: 99, nowMs: CombatTime.MaxMs + 1, 或 nowMs: 499
     ```
  2. **`BeginCast` 冷卻早退漏檢 `Book` 水位（`Cast.cs:393-396`）**：令存活 `c = Make()`：
     ```csharp
     c.BeginCast(0, Damage(cd: 3000), 0, out _); // Cooldowns 水位 = 0，ReadyAtMs(0) = 3000
     c.Book.Apply("buff", 0, Mod(EffectType.MasteryMod, 10), 5000, 1000); // Book 水位 = 1000
     c.BeginCast(0, Damage(cd: 3000), 500, out _); // nowMs = 500 < Book 水位 1000
     ```
  3. **`ReceiveSupport` 滿血瞬間治療漏檢 `nowMs`（`Cast.cs:488-492, 500-503`）**：令滿血 `target = Make()`：
     ```csharp
     target.Book.Apply("b", 0, Mod(EffectType.MasteryMod, 5), 5000, 500); // Book 水位 = 500
     target.ReceiveSupport(Cast(Make(), Support(Heal(30), 0), 0), -1); // 或 nowMs: 499, 或 CombatTime.MaxMs + 1
     ```
- **預期 vs 實際**：
  - **預期**：三組呼叫皆違反方法宣告之 `Pre:`（`0 <= slot < Cooldowns.SlotCount`、`nowMs in CombatTime and >= every watermark of Book [and Cooldowns]`），應立即拋出 `ContractException`。
  - **實際**：(1) 回傳 `CastResult.Fainted`；(2) 因 `Cooldowns.IsReady(0, 500)` 只檢查 `Cooldowns._watermarkMs (0)`，回傳 `CastResult.OnCooldown`；(3) 因 `Hp == MaxHp`，`HealPercent` 於 `:500` 直接返回，回傳 `HitReport(HitOutcome.Hit, 0, 0, MaxHp, MaxHp, false)`，全程未呼叫 `CombatTime.Require` 或任何 `Book` 檢查。
- **建議修復**：
  - 於 `EffectBook`（`Effects.cs`）新增 `internal void RequireTime(long nowMs) => CombatTime.Require(nowMs, _watermarkMs);`。
  - 於 `Combatant.BeginCast` 開頭（`if (IsFainted)` 之前）先執行 `bool ready = Cooldowns.IsReady(slot, nowMs);`（同時驗證 `slot`、`CombatTime` 與 `Cooldowns` 水位）及 `Book.RequireTime(nowMs);`，再依 `if (IsFainted)` 與 `if (!ready)` 分派回傳。
  - 於 `Combatant.ReceiveSupport` 開頭執行 `Book.RequireTime(nowMs);` 後再進入 `IsInstant` 分支。

---

### K2 · medium · `Combatant` 封裝了 `Hp` 卻缺少 `on_summon` / `on_ally_swap` 觸發入口，使上場／換手治療特性無法恢復 `Hp` 且缺少 0 HP 禁令守門

- **位置**：
  - `Assets/_Project/Features/Combat/Runtime/Model/Cast.cs:292, 300-302, 360, 368, 498`
  - `Assets/_Project/Features/Combat/Runtime/Model/Effects.cs:458-471`（`EffectBook.NotifySummoned`、`EffectBook.NotifySwappedIn`）
  - 規格：`docs/design/combat.md:83-84, 89`
  - 專案實際特性資料佐證：
    - `Data/_incoming/p5-05-traits-draft-01/traits/traits.json:69-84`（`trait_wood_photosynthesis`：`on_summon` → `heal_percent_max_hp: 8`）
    - `Data/_incoming/p5-05-traits-draft-02/traits/traits.json:162-177`（`trait_water_spring_surge`：`on_ally_swap` → `heal_percent_max_hp: 7`）
    - `Data/_incoming/p5-05-traits-draft-03/traits/traits.json:146-161`（`trait_water_revitalizing_spring`：`on_summon` → `heal_percent_max_hp: 8`）
- **反例輸入**：
  - 建立攜帶 `on_summon` + `HealPercentMaxHp(8)`（如 `trait_wood_photosynthesis`）或 `on_ally_swap` + `HealPercentMaxHp(7)`（如 `trait_water_spring_surge`）的 `Combatant c`；先受擊扣血使 `0 < c.Hp < c.MaxHp`，收回後再次換手上場（`combat.md:83-84`：「每次上場皆觸發 `on_summon`；換手進場會同時觸發 `on_summon` 與 `on_ally_swap`」）。
- **預期 vs 實際**：
  - **預期**：換手進場時，`Combatant` 應驗證 `!IsFainted`（落實 `combat.md:89`「HP 為 0 的倒下單位不觸發任何特性事件」），觸發 `on_summon`（與換手時的 `on_ally_swap`），並依 `Cast.cs:301-302` 承諾（「Heals from trait events go through the same HP path and are reported back via NotifyHpChanged too」）將治療百分比套入 `Hp` 並通知 `NotifyHpChanged`。
  - **實際**：`Combatant` 只包了 `NotifyMoveUsed`、`NotifyHpChanged`、`NotifyHitTaken`，未提供上場／換手方法。外部呼叫端若直接呼叫 `c.Book.NotifySummoned(nowMs, out int h1)` 與 `c.Book.NotifySwappedIn(nowMs, out int h2)`，雖拿到 `h1 = 8`、`h2 = 7`，但因 `Combatant.Hp` 為 `private set`（`Cast.cs:360`）、`HealPercent` 為 `private`（`Cast.cs:498`），外部無任何公開 API 可將治療量寫回 `c.Hp`；且若誤對倒下單位呼叫 `c.Book.NotifySummoned`，`EffectBook` 不知 `c.Hp == 0` 仍會掛上特性效果。
- **建議修復**：
  - 在 `Combatant` 新增 `public int NotifyEnteredField(bool bySwap, long nowMs)`（或拆為 `NotifySummoned(long nowMs)` / `NotifySwappedIn(long nowMs)`）：
    - `Pre:`：`!IsFainted; nowMs in CombatTime and >= every watermark of Book.`
    - 內部呼叫 `Book.NotifySummoned(nowMs, out int summonHeal)`，若 `bySwap` 再呼叫 `Book.NotifySwappedIn(nowMs, out int swapHeal)`，最後以單次 `HealPercent(summonHeal + swapHeal, nowMs)` 結算並回傳實際回復之 HP（`Hp - hpBefore`）。

---

### K3 · medium · 跨層契約缺口：資料層與 `CastMove.FromDef` 允許 `shape: self|ally` 搭配 `kind: damage|both`，但 `Combatant.ReceiveSupport` 與 `ReceiveHostile` 皆拒絕處理

- **位置**：
  - `Assets/_Project/Features/Combat/Runtime/Model/Cast.cs:101-109, 410, 473-486`
  - `Data/schemas/move.schema.json:12-74`
  - `Assets/_Project/GameData/Editor/DataImporter.cs:269-326`
  - 規格：`docs/design/combat.md:66, 90`；路線圖註記：`docs/roadmap/phase-2.md:60`
- **反例輸入**：
  - 定義一筆招式 JSON：
    ```json
    {
      "id": "move_self_overload",
      "name": "超載脈衝",
      "element": "fire",
      "kind": "both",
      "power": 20,
      "effect": { "type": "mastery_mod", "params": { "element": "fire", "delta": 20 } },
      "effect_duration_ms": 5000,
      "cooldown_ms": 2000,
      "accuracy": 100,
      "range_class": "melee",
      "shape": "self",
      "base_range_mm": 0
    }
    ```
    匯入為 `MoveDef def`，執行 `CastMove m = CastMove.FromDef(def);`，施法成功取得 `CastContext ctx` 後結算於自身。
- **預期 vs 實際**：
  - **預期**：凡通過 `validate_data.py` 與 `DataImporter` 的合法 `MoveDef`，在戰鬥結算層都應有合法且無歧義的結算入口（或於 schema／validator／importer 階段即作為非法資料攔截）。
  - **實際**：上述 JSON 完全通過 `move.schema.json`、`tools/validate_data.py`、`DataImporter`、`CastMove.FromDef` 與 `Combatant.BeginCast`；但在結算階段呼叫 `caster.ReceiveSupport(ctx, nowMs)` 時，於 `Cast.cs:485`（`Contract.Requires(... && move.Kind == MoveKind.Effect)`）拋出 `ContractException`；而改呼叫 `ReceiveHostile` 則違反 `Cast.cs:410`（`any shape except self/ally`）與 `combat.md:90`（`self` / `ally` 不進入命中判定、不消耗亂數）。
- **建議修復**：
  - 配合 `phase-2.md:60` 待決事項定案：若設計確定 `self` / `ally` 僅能是純效果招（`kind == "effect"`），請在 `Data/schemas/move.schema.json`（`allOf` 分支）、`tools/validate_data.py` 與 `DataImporter.ImportMove` 同步加上 `shape in {self, ally} => kind == effect` 驗證，堵住匯入缺口；若允許 `self` / `ally` 帶 `both` / `damage`，則明確定義其無檢定傷害規則並擴充 `ReceiveSupport`。

---

### K4 · low · `HitReport.Fainted` 違反自身 `Post:`，且多處 DbC 註解在 `default(struct)`、隱藏 `Pre:` 與 `Reset` 水位上失真

- **位置**：
  1. `Assets/_Project/Features/Combat/Runtime/Model/Cast.cs:247, 273, 285-286`（`HitReport`）
  2. `Assets/_Project/Features/Combat/Runtime/Model/Cast.cs:212, 230, 238, 378-379`（`CastContext`）
  3. `Assets/_Project/Features/Combat/Runtime/Model/Cast.cs:18-21, 33-36, 60`（`CastMove`）
  4. `Assets/_Project/Features/Combat/Runtime/Model/Cast.cs:115, 182`（`MoveCooldowns`）
- **反例輸入與預期 vs 實際**：
  1. **`HitReport.Fainted` 與 `HpBefore`（`:247, 273, 285-286`）**：
     - 輸入：`HitReport r = default;`（型別與屬性皆標註 `Pre: none`）。
     - 預期：依 `:285` `Post: true iff HpBefore >= 1 and HpAfter == 0`，`r.Fainted` 應為 `false`；依 `:273` `Post: >= 1`，`r.HpBefore` 宣稱恆 `>= 1`。
     - 實際：`:286` 實作為 `public bool Fainted => HpAfter == 0;`，對 `default(HitReport)`（`HpBefore == 0, HpAfter == 0`）回傳 **`true`**，直接違反自身 `Post:`；`r.HpBefore` 為 `0`。
  2. **`CastContext.CooldownMs`（`:212, 238, 378-379`）**：
     - 輸入：對冷卻中或倒下的 `Combatant` 呼叫 `BeginCast(..., out CastContext ctx)`，依 `:379` 取得 `ctx == default`（符合 `:212` `Pre: obtained from Combatant.BeginCast`），讀取 `ctx.CooldownMs`。
     - 預期：`:238` 宣稱 `Pre: none. Post: >= CdFloorMs`。
     - 實際：`ctx.CooldownMs == 0 < CdFloorMs`（`CastTests.cs:223` 亦直接斷言其為 `0`）。
  3. **`CastMove` 建構子隱藏 `Pre:` 與 `default(CastMove).Id`（`:18-21, 35, 60`）**：
     - 輸入：`new CastMove("m", Element.Fire, MoveKind.Effect, 0, 100, 0, true, default, 1000)`。因 `default(EffectSpec).IsInstant == false` 且 `effectDurationMs == 1000 ∈ 1..MaxDurationMs`，滿足 `:18-20` 寫出的全部 `Pre:` 子句。
     - 預期：若滿足文件化 `Pre:` 則不應拋例外；反之若要求 `effect` 必由 `EffectSpec` 建構子建立（`effect.MaxStacks >= 1`），必須寫入 `Pre:`。
     - 實際：於 `:35` 拋出 `ContractException("effect must come from the EffectSpec constructor")`。另 `:60` `CastMove.Id` 宣稱 `Pre: none. Post: non-empty`，對 `default(CastMove)` 實際為 `null`。
  4. **`MoveCooldowns` 類別水位註解（`:115` vs `:182`）**：
     - 輸入：`var cd = new MoveCooldowns(1); cd.Reset(500); cd.IsReady(0, 200);`。
     - 預期：`:115` 寫 `requires nowMs >= the latest nowMs passed to Start`（此處從未呼叫 `Start`）。
     - 實際：`Reset(500)` 於 `:182` 亦更新 `_watermarkMs = 500`，導致 `cd.IsReady(0, 200)` 拋出 `ContractException`。
- **建議修復**：
  - 將 `Cast.cs:286` 改為 `public bool Fainted => HpBefore >= 1 && HpAfter == 0;`，並比照 `Provision.cs:44, 48` 與 `PlayerVitals.cs:41` 在 `HitReport`、`CastContext`、`CastMove` 的 struct 契約註明非 `default`（或僅於 `BeginCast == Accepted` 時成立）。
  - 在 `CastMove` 建構子 `Pre:`（`:19-20`）補上 `when hasEffect: effect built via EffectSpec constructor (effect.MaxStacks >= 1)`。
  - 將 `Cast.cs:115` 修正為 `passed to Start or Reset`。

---

### K5 · low · `CastTests.cs` 測試覆蓋缺口（違反 `testing.md` 公開方法必測規範，12 個具體語意突變體存活）

- **位置**：`Assets/_Project/Features/Combat/Tests/EditMode/CastTests.cs:60-543`
- **依據**：`.claude/rules/testing.md:9`（「每個公開方法至少一個正常路徑 + 每個失敗分支各一；純函式補邊界值（0、負數、上限、空集合）」）。
- **主要缺口摘要**（逐行突變體與對應必殺測試詳見下節 M01–M12）：
  1. `CastMove.FromDef(MoveDef)`（`Cast.cs:101-109`）僅在 `CastTests.cs:78` 測 `FromDef(null)`，**零正常路徑測試**（M01、M02 存活）。
  2. `Combatant` 建構子漏測 `moveSlots == MoveCooldowns.MaxSlots + 1` 上界失敗分支（M03 存活）。
  3. `BeginCast` 的 `on_move_used` 治療結算（`Cast.cs:399`）完全未測（M04 存活）。
  4. `BeginCast`（`Cast.cs:401`）與 `ReceiveHostile`（`Cast.cs:429`）將當前 `Hp` 傳入 `Book.Collect` 以驅動 `on_hp_below` / `on_hp_above` 持續型特性的路徑完全未測（M05、M06 存活）。
  5. `ReceiveHostile` 在 `chance == 1000`（必中接觸）時仍須固定抽 1 次 RNG 的契約未測（M07 存活）。
  6. `ReceiveHostile` 掛載限時效果未驗證 `move.EffectDurationMs` 到期邊界（M08 存活）。
  7. `ReceiveHostile` 存活目標的 `on_hit_taken` 治療（`hitHeal`）與多來源治療合併單次四捨五入（`crossHeal + hitHeal + moveHeal`）未測（M09、M10 存活）。
  8. `ReceiveSupport` 回傳之 `HitReport.HpBefore` / `HpAfter` 以及 `HealPercent` 推進 `Book` 時間水位（`Cast.cs:509`）未測（M11、M12 存活），另 `BeginCast` / `ReceiveHostile` / `ReceiveSupport` 的時間倒退失敗分支亦未覆蓋。

---

## Check (5) 突變體分析表（20 個具體突變體）

| ID | 位置（`Cast.cs`） | 突變內容（原碼 → 變異碼） | 結果 | 判定依據（`CastTests.cs`） |
|---|---|---|---|---|
| **M01** | `:107` `CastMove.FromDef` | `new CastMove(..., def.Power, def.Accuracy, ...)` → 對調為 `def.Accuracy, def.Power` | **存活** | 全檔僅 `:78` 呼叫 `FromDef(null)`，無非 `null` 測試 |
| **M02** | `:106` `CastMove.FromDef` | `def.HasEffectDuration ? def.EffectDurationMs : 0` → `def.HasEffectDuration ? 1 : 0` | **存活** | 無非 `null` 的 `FromDef` 測試驗證限時與瞬間效果轉換 |
| **M03** | `:351` `Combatant` 建構子 | `new MoveCooldowns(moveSlots)` → `new MoveCooldowns(moveSlots > 8 ? 8 : moveSlots)`（靜默夾限） | **存活** | `:151-170` 僅測 `moveSlots: 0`（`:169`），未測 `MaxSlots + 1` |
| **M04** | `:399` `Combatant.BeginCast` | `HealPercent(healPercent, nowMs);` → 刪除該行 | **存活** | `:217-260` 的 `OnMoveUsed` 測試僅用 `MasteryMod` / `CdModPercent`，未測治療 |
| **M05** | `:401` `Combatant.BeginCast` | `Book.Collect(nowMs, Hp, MaxHp, _totals)` → `Book.Collect(nowMs, MaxHp, MaxHp, _totals)` | **存活** | 全檔無受傷狀態施法搭配 `OnHpBelow` / `OnHpAbove` 之測試 |
| **M06** | `:429` `Combatant.ReceiveHostile` | `Book.Collect(nowMs, Hp, MaxHp, _totals)` → `Book.Collect(nowMs, MaxHp, MaxHp, _totals)` | **存活** | 全檔未使用 `OnHpBelow` / `OnHpAbove` 驗證受傷目標之閃避或抗性 |
| **M07** | `:432` `Combatant.ReceiveHostile` | `HitResolver.Resolve(geometricContact, false, chance, ref rng)` → 當 `geometricContact && chance == 1000` 時直接回傳 `HitOutcome.Hit` 不抽 RNG | **存活** | 唯一比對 RNG 雙胞胎的接觸測試 `:297-327` 用 `accuracy: 60, agi: 100`（`chance < 1000`）；所有 `chance == 1000` 測試均未檢查 `rng` 狀態 |
| **M08** | `:465` `Combatant.ReceiveHostile` | `Book.Apply(move.Id, 0, move.Effect, move.EffectDurationMs, nowMs)` → 持續時間改傳常數 `1` | **存活** | `:458` 僅查命中當下 `StacksOf("move_slow", 0, now)`（`now < now + 1` 仍為 `1`），下輪跳至 `now + 10_000L`，未測 `now + 2999` vs `now + 3000` |
| **M09** | `:469` `Combatant.ReceiveHostile` | `HealPercent(crossHeal + hitHeal + moveHeal, nowMs)` → `HealPercent(crossHeal + moveHeal, nowMs)`（漏加 `hitHeal`） | **存活** | `:364-375` 的 `OnHitTaken + Heal` 直接將目標打死（`Hp == 0` 早退），無存活目標觸發 `OnHitTaken` 治療之測試 |
| **M10** | `:469` `Combatant.ReceiveHostile` | 單次 `HealPercent(crossHeal + hitHeal + moveHeal, nowMs)` → 拆成三次呼叫 `HealPercent(crossHeal, ...); HealPercent(hitHeal, ...); HealPercent(moveHeal, ...);` | **存活** | 所有治療測試每次最多只有單一非零來源，未測多來源百分比合併前／後之四捨五入差異 |
| **M11** | `:491` `Combatant.ReceiveSupport` | `new HitReport(HitOutcome.Hit, 0, Hp - hpBefore, hpBefore, Hp, false)` → 第 4 引數誤傳 `Hp` | **存活** | `:491-513` 僅斷言 `r.Outcome`、`r.Healed`、`target.Hp`，從未斷言 `r.HpBefore` |
| **M12** | `:509` `Combatant.HealPercent` | `Book.NotifyHpChanged(previous, Hp, MaxHp, nowMs, out int _);` → 刪除該行 | **存活** | 無任何測試驗證治療實際改變 `Hp` 後已推進 `Book` 水位 |
| **M13** | `:159` `MoveCooldowns.IsReady` | `nowMs >= _readyAt[slot]` → `nowMs > _readyAt[slot]` | **擊殺** | `:116`（`IsReady(0, 0)`）、`:122`（`IsReady(0, 3500)`）、`:124` 斷言失敗 |
| **M14** | `:340-341` `Combatant` 建構子 | 複製陣列元素 → 直接儲存外部陣列參考 | **擊殺** | `:186-195` `Combatant_copies_element_tables` 修改原陣列後斷言傷害失敗 |
| **M15** | `:398-401` `Combatant.BeginCast` | 對調 `Book.NotifyMoveUsed` 與 `Book.Collect` 順序 | **擊殺** | `:241-252` `On_move_used_buffs_apply_to_the_triggering_cast` 斷言 `MasteryPercent == 35` 失敗 |
| **M16** | `:403` `Combatant.BeginCast` | `_totals.CdPercent(move.Element)` → 改傳 `0` | **擊殺** | `:250-251` 斷言縮減後 `ctx.CooldownMs` 失敗 |
| **M17** | `:443` `Combatant.ReceiveHostile` | `damage = raw > Hp ? Hp : raw` → `damage = raw` | **擊殺** | `:370-371` `Damage_is_capped_at_remaining_hp_and_faint_fires_no_triggers` 斷言 `r.Damage == target.MaxHp` 與 `Hp == 0` 失敗 |
| **M18** | `:445-449` `Combatant.ReceiveHostile` | 移除 `if (Hp == 0) return ...;` 守門 | **擊殺** | `:361-376`（`OnHitTaken` 治療復活）與 `:379-389`（倒下仍掛效果）斷言失敗 |
| **M19** | `:429, 454` `Combatant.ReceiveHostile` | 將 `NotifyHitTaken` 提前至扣血前並重跑 `Collect` | **擊殺** | `:392-403` `On_hit_taken_buff_applies_from_the_next_hit` 第一擊傷害斷言失敗 |
| **M20** | `:470` `Combatant.ReceiveHostile` | `Healed` 引數 `Hp - hpAfterDamage` → `Hp - hpBefore` | **擊殺** | `:437` `Hp_cross_below_heal_resolves_after_the_hit` 斷言 `r.Healed` 失敗 |

### 存活突變體之必殺測試（Witness Tests for M01–M12）

以下測試可直接加入 `CastTests`（頂端補 `using UnityEngine;` 以建立測試用 `MoveDef` 資產）：

```csharp
[Test]
public void Witness_M01_M02_FromDef_copies_timed_and_instant_moves_faithfully()
{
    MoveDef timed = ScriptableObject.CreateInstance<MoveDef>();
    MoveDef instant = ScriptableObject.CreateInstance<MoveDef>();
    try
    {
        JsonUtility.FromJsonOverwrite(
            "{\"id\":\"m_timed\",\"element\":3,\"kind\":2,\"power\":45,\"hasEffect\":true," +
            "\"effect\":{\"type\":0,\"anyElement\":false,\"element\":3,\"delta\":15,\"percent\":0,\"maxStacks\":2}," +
            "\"effectDurationMs\":3500,\"cooldownMs\":2500,\"accuracy\":85}",
            timed);
        CastMove cmTimed = CastMove.FromDef(timed);
        Assert.AreEqual("m_timed", cmTimed.Id);
        Assert.AreEqual(Element.Fire, cmTimed.Element);
        Assert.AreEqual(MoveKind.Both, cmTimed.Kind);
        Assert.AreEqual(45, cmTimed.Power);
        Assert.AreEqual(85, cmTimed.AccuracyPercent);
        Assert.AreEqual(2500, cmTimed.BaseCooldownMs);
        Assert.IsTrue(cmTimed.HasEffect);
        Assert.AreEqual(3500, cmTimed.EffectDurationMs);
        Assert.AreEqual(2, cmTimed.Effect.MaxStacks);

        JsonUtility.FromJsonOverwrite(
            "{\"id\":\"m_heal\",\"element\":2,\"kind\":1,\"power\":0,\"hasEffect\":true," +
            "\"effect\":{\"type\":6,\"anyElement\":false,\"element\":0,\"delta\":0,\"percent\":25,\"maxStacks\":1}," +
            "\"effectDurationMs\":0,\"cooldownMs\":4000,\"accuracy\":100}",
            instant);
        CastMove cmHeal = CastMove.FromDef(instant);
        Assert.AreEqual(0, cmHeal.EffectDurationMs);
        Assert.AreEqual(25, cmHeal.Effect.Percent);
    }
    finally
    {
        UnityEngine.Object.DestroyImmediate(timed);
        UnityEngine.Object.DestroyImmediate(instant);
    }
}

[Test]
public void Witness_M03_Combatant_rejects_slot_count_above_max()
{
    int[] ok = Table();
    Assert.Throws<ContractException>(() =>
        new Combatant(_stats, _cfg, 1, 1, 1, ok, ok, new EffectBook(), MoveCooldowns.MaxSlots + 1));
}

[Test]
public void Witness_M04_M05_BeginCast_applies_on_move_used_heal_before_sampling_hp_thresholds()
{
    // M04: on_move_used heal restores HP; M05: Collect sees current (damaged) HP for on_hp_below.
    var healOnCast = new TriggerSpec(TriggerType.OnMoveUsed, false, Element.Fire, 0, 0);
    Combatant healer = Make(Trait(healOnCast, Heal(20)), con: 100);
    Combatant enemy = Make();
    Pcg32 rng = Rng();
    healer.ReceiveHostile(Cast(enemy, Damage(power: 120), 0), true, ref rng, 0);
    int hpBeforeCast = healer.Hp;
    Assume.That(hpBeforeCast, Is.LessThan(healer.MaxHp - EffectMath.HealAmount(healer.MaxHp, 20)));

    Cast(healer, Damage(), 0);
    Assert.AreEqual(hpBeforeCast + EffectMath.HealAmount(healer.MaxHp, 20), healer.Hp);

    var lowHpFury = new TriggerSpec(TriggerType.OnHpBelow, false, default, 50, 0);
    Combatant berserker = Make(Trait(lowHpFury, Mod(EffectType.MasteryMod, 30)), con: 0);
    while (berserker.Hp * 100 >= 50 * berserker.MaxHp)
    {
        berserker.ReceiveHostile(Cast(enemy, Damage(power: 20), 0, slot: 1), true, ref rng, 0);
        enemy.Cooldowns.Reset(0);
    }

    Assume.That(berserker.Hp, Is.GreaterThan(0));
    Assert.AreEqual(30, Cast(berserker, Damage(), 0).MasteryPercent);
}

[Test]
public void Witness_M06_ReceiveHostile_samples_continuous_on_hp_below_with_current_hp()
{
    var lowHpShell = new TriggerSpec(TriggerType.OnHpBelow, false, default, 50, 0);
    Combatant target = Make(Trait(lowHpShell, Mod(EffectType.ResistMod, 50)), con: 0);
    Combatant caster = Make();
    Pcg32 rng = Rng();
    while (target.Hp * 100 >= 50 * target.MaxHp)
    {
        target.ReceiveHostile(Cast(caster, Damage(power: 20), 0), true, ref rng, 0);
        caster.Cooldowns.Reset(0);
    }

    Assume.That(target.Hp, Is.GreaterThan(DamageModel.Compute(20, 0, 0)));
    HitReport r = target.ReceiveHostile(Cast(caster, Damage(power: 20), 0), true, ref rng, 0);
    Assert.AreEqual(DamageModel.Compute(20, 0, 50), r.Damage);
}

[Test]
public void Witness_M07_Guaranteed_hit_contact_still_advances_rng_by_one_draw()
{
    Combatant caster = Make();
    Combatant target = Make(con: 100, agi: 0);
    Pcg32 rng = Rng();
    Pcg32 oracle = rng;
    oracle.NextUInt(DerivedStats.Permille);

    HitReport r = target.ReceiveHostile(Cast(caster, Damage(accuracy: 100), 0), true, ref rng, 0);
    Assert.AreEqual(HitOutcome.Hit, r.Outcome);
    Assert.AreEqual(oracle.NextUInt(), rng.NextUInt());
}

[Test]
public void Witness_M08_Hostile_timed_effect_uses_move_effect_duration()
{
    Combatant caster = Make();
    Combatant target = Make(con: 100, agi: 0);
    Pcg32 rng = Rng();
    CastMove slow = new CastMove("move_slow", Element.Fire, MoveKind.Effect, 0, 100, 0, true,
        Mod(EffectType.MoveSpeedModPercent, -30), 3000);

    target.ReceiveHostile(Cast(caster, slow, 100), true, ref rng, 100);
    Assert.AreEqual(1, target.Book.StacksOf("move_slow", 0, 3099));
    Assert.AreEqual(0, target.Book.StacksOf("move_slow", 0, 3100));
}

[Test]
public void Witness_M09_M10_Surviving_on_hit_taken_heal_combines_with_move_heal_before_rounding()
{
    // con = 1 gives MaxHp = 608: HealAmount(608, 6) = 36, so 36 + 36 = 72 != HealAmount(608, 12) = 73.
    var onHit = new TriggerSpec(TriggerType.OnHitTaken, true, default, 0, 0);
    Combatant target = Make(Trait(onHit, Heal(6)), con: 1);
    Assume.That(EffectMath.HealAmount(target.MaxHp, 6) * 2,
        Is.Not.EqualTo(EffectMath.HealAmount(target.MaxHp, 12)));

    Combatant caster = Make();
    Pcg32 rng = Rng();
    target.ReceiveHostile(Cast(caster, Damage(power: 80), 0), true, ref rng, 0);
    Assume.That(target.Hp, Is.GreaterThan(10));
    Assume.That(target.Hp + EffectMath.HealAmount(target.MaxHp, 12), Is.LessThan(target.MaxHp));

    CastMove drain = new CastMove("m_both_heal", Element.Fire, MoveKind.Both, 1, 100, 0, true, Heal(6), 0);
    HitReport r = target.ReceiveHostile(Cast(caster, drain, 0, slot: 1), true, ref rng, 0);
    Assert.AreEqual(EffectMath.HealAmount(target.MaxHp, 12), r.Healed);
}

[Test]
public void Witness_M11_M12_Support_heal_reports_hp_before_and_advances_book_watermark()
{
    Combatant target = Make(con: 100);
    Combatant caster = Make();
    Pcg32 rng = Rng();
    target.ReceiveHostile(Cast(caster, Damage(power: 50), 0), true, ref rng, 100);
    int damagedHp = target.Hp;

    HitReport r = target.ReceiveSupport(Cast(caster, Support(Heal(20), 0), 500, slot: 1), 500);
    Assert.AreEqual(damagedHp, r.HpBefore);
    Assert.AreEqual(damagedHp + r.Healed, r.HpAfter);
    Assert.Throws<ContractException>(() => target.Book.StacksOf("any", 0, 499),
        "HealPercent must report HP change to Book and advance its watermark to 500");
}
```

---

## Part B 審查結果：`tools/validate_data.py`（C8）

- **C8 修復確認：通過**
  - `tools/validate_data.py:274-280` 已於 `_check_material_sums` 迴圈內補齊型別收窄：
    - `if not isinstance(m, dict): continue`
    - `if (isinstance(qty, int) and not isinstance(qty, bool) and isinstance(el, str) and isinstance(tier, int) and not isinstance(tier, bool)):`（在 Python 中 `isinstance(True, int)` 為 `True`，明確排除 `bool` 正確）。
  - `docs/reviews/P2-05-P2-07-sol-r2.md` C8 列舉之全部反例（`tier: []`、`tier: {}`、`element: []`、`element: {}`）以及 `tier: True`、`qty: True`、非 `dict` 元素皆不再觸發 `TypeError: unhashable type`，可安全略過並保留前段 `schema` 錯誤回報。
- **對應單元測試確認：通過**
  - `tools/tests/test_validate_data.py:257-268`（`test_malformed_material_key_is_a_schema_error_not_a_crash`）以 `subTest` 逐一注入 `{"tier": []}`、`{"tier": {}}`、`{"element": []}`、`{"element": {}}`、`{"tier": True}`，斷言回傳集合包含 `"schema"` 且不含 `"material_sum_overflow"`。
- **附帶防禦性備註（非 C8 回歸，無阻塞性）**：
  - `tools/validate_data.py:271`（及既有的 `:292`）寫法 `for m in evo.get("materials") or []:` 若遇到 `evolution.materials` 本身被寫成非可迭代真值（例如 `"materials": 1` 或 `"materials": true`），仍會拋出 `TypeError: 'int'/'bool' object is not iterable`。若後續順手改為 `mats = evo.get("materials"); if not isinstance(mats, list): return`，可對任意畸形 JSON 完全免疫。
