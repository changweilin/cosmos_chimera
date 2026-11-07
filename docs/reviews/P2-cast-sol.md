> 原始輸出：`codex exec -s read-only -m gpt-6.1-sol`，對象 `14af61d`。未修改任何檔案。Claude Opus 已加入 W04–W49 十個見證並依 K2 修正契約文字（見 phase-2.md 交接）。

判定 fail

標的：`p2/sol-r6-p2-09-fixes`，`14af61d4732b94c01fe4c6ec01871f697e85975b`。

**未發現合法輸入下的結算功能錯誤；fail 原因為 10 個已證實的測試缺口，以及不精確的 DbC 契約文字。**

全程未修改 repo。宿主、突變與見證均在記憶體內編譯執行。

檔案代號：

- **A**：[Cast.cs](C:/Users/user/Documents/app/cosmos_chimera/Assets/_Project/Features/Combat/Runtime/Model/Cast.cs)
- **T**：[CastTests.cs](C:/Users/user/Documents/app/cosmos_chimera/Assets/_Project/Features/Combat/Tests/EditMode/CastTests.cs)
- **E**：[Effects.cs](C:/Users/user/Documents/app/cosmos_chimera/Assets/_Project/Features/Combat/Runtime/Model/Effects.cs)

## Findings

以下 K 編號屬於本報告，與 ARGON 舊報告分開。

### K1 · medium · 10 個非等價突變未被原測試攔截

**位置**：T:137、T:186、T:241、T:348、T:516、T:541、T:696、T:774。

**反例輸入、預期與實際**：見下方 W04–W49 見證表。

原版 **48/48 通過**，但下列錯誤實作也全部通過原測試：

- Reset 不更新時間水位。
- 非火屬性招式讀取火屬性專精修正。
- 代價傷害忽略承受者抗性增益、忽略當下 HP 條件。
- 輔助效果只維持 1 ms。
- 換手漏掉召喚事件或換手治療。
- `CastContext` 保存可變 `ModifierTotals` 別名。
- 專精表未複製。
- `on_move_used` 的射程增益延至下一招。

**預期**：違反規格的突變至少使一個測試失敗。  
**實際**：上述 10 個突變各自仍為 **48/48 通過**。

**建議修正**：將下方 10 個見證加入 `CastTests`。它們已在原版 **10/10 通過**，並逐一殺死對應突變。

### K2 · low · Post 未明確區分進入狀態與回傳狀態

**位置**：A:379、A:420、A:482；另 A:72 的 `Power` 說明不成立。

| 反例輸入 | 契約文字所導出的預期 | 實際 |
|---|---|---|
| 全新單位於 0 ms 接受施招，生效 CD＝250 ms | A:379 寫 `OnCooldown iff !Cooldowns.IsReady(slot, nowMs)`；回傳時右側為 true | 結果為 `Accepted`，回傳後 `IsReady(0, 0) == false` |
| 承受者 HP＝600，代價傷害＝400，無治療 | A:482 若以回傳時 `Hp` 解讀，傷害應為 `min(200, 400) == 200` | `Damage == 400`，`HpAfter == 200` |
| `MoveKind.Effect`、`power: 123`、合法瞬間治療 | A:72 承諾 effect-only move 的 Power 為 0 | 建構成功，`Power == 123`；傷害確實被忽略 |

A:420 的敵擊傷害 Post 有相同的 `Hp` 時點問題。

**建議修正**：

- `BeginCast` 明寫「進入方法時」的 HP／ready 狀態。
- 傷害 Post 改用 `HpBefore`。
- `Power` 說明改成「effect-only 時忽略」，與建構子保留參數的 Post 一致。
- 建構子 Pre 明確說明 power 的範圍檢查對所有 kind 都適用。

以上屬契約文字問題；目前執行期行為符合設計決議。

## 規格、契約與既有修正核對

| 項目 | 結果 |
|---|---|
| 接受施招時起算 CD；倒下／CD 中不觸發事件 | 通過 |
| `on_move_used` 先觸發、治療後取樣；觸發招式立即受益 | 通過 |
| 施放者專精與射程總和凍結，後續目標／場地 tick 使用同一份 | 通過 |
| 命中前取樣承受者修正，扣血後才通知血線／受擊事件 | 通過 |
| HP 歸零後立即返回，不觸發事件、不掛效果、不治療 | 通過 |
| self／ally 代價傷害使用施放者專精及承受者有效抗性 | 通過 |
| 代價傷害可致死、可觸發血線事件、不觸發受擊事件 | 通過 |
| 敵擊每次接觸恰一次 bounded draw；無接觸不抽樣 | 通過 |
| support 不消耗戰鬥 RNG | 通過 |
| 修正加總、迭代順序、跨呼叫緩衝別名 | 未發現原版缺陷 |
| Cast 與 callee 的合法輸入 Pre 銜接 | 未發現缺口 |
| HP、整數溢位與時間上界 | 通過下述驗證 |

RNG 的「一次」依 `HitResolver` 契約指一次 `NextUInt(1000)` bounded draw；其 rejection sampling 可能消耗多個底層 PCG step。

**數值驗證**：以 `/checked+` 執行 20,000 組極端探針，涵蓋最大合法係數、`int.MaxValue` 能力值及基礎 CD、最大威力、`CombatTime.MaxMs`、正負修正、敵擊及代價傷害；未出現溢位、HP 越界、報告等式錯誤或 CD 下限破壞。

算術上界亦成立：`MaxHp ≤ 2,000,000`，傷害最大為 2,200,000；扣血先夾剩餘 HP，治療以剩餘空間比較後才相加。CD／效果到期時間遠低於 `Int64.MaxValue`，修正總和使用 Int64。

曾測試事件特性帶 `default(EffectSpec)` 的延遲失敗；該輸入違反 `EffectSpec` 型別層 Pre，**不列為合法輸入缺陷**。

### ARGON 舊 findings

| 舊項目 | 核對結果 |
|---|---|
| K1：早退略過 Pre | **fixed**。`BeginCast` 先檢查 slot／兩種水位；滿血 support 仍檢查時間。 |
| K2：上場治療無入口 | **fixed**。`EnterField` 可將召喚／換手治療寫回 HP；額外見證亦確認換手會觸發 summon。 |
| K3：使用者代價型決議 | **實作完全符合**，未挑戰決議。 |
| K4：default report／相關契約 | **指定問題 fixed**。`default(HitReport).Fainted == false`；Reset 水位及 EffectSpec 建構要求已補述。 |
| K5：原 12 個測試缺口 | **fixed**。對應突變全部實際 killed；本報告 K1 是另外發現的缺口。 |

## 突變測試

**50 個可編譯、非等價突變：40 killed／10 survived，原測試殺死率 80%。**

原版測試 **48/48**；加入記憶體見證後原版 **58/58**。10 個存活突變皆被對應見證殺死。

### 突變表

`T:n` 指原測試中的見證位置；`Wxx` 指下一表已執行的新增見證。

| ID | Cast.cs 突變 | 原測試結果 | 擊殺證據 |
|---|---|---|---|
| M01 | CD ready `>=` 改 `>` | killed | T:112 |
| M02 | CD 結束時間改成相減 | killed | T:112 |
| M03 | Reset 漏最後一格 | killed | T:137 |
| M04 | Reset 不更新水位 | **survived** | W04 |
| M05 | 專精表複製成零 | killed | T:200 |
| M06 | 抗性表複製成零 | killed | T:186 |
| M07 | 專精修正固定讀火屬性 | **survived** | W07 |
| M08 | 凍結射程總和固定零 | killed | T:263 |
| M09 | 在 `on_move_used` 前取樣修正 | killed | T:241 |
| M10 | 漏施招特性治療 | killed | T:684 |
| M11 | 不觸發 `on_move_used` | killed | T:215 |
| M12 | CD 忽略效果修正 | killed | T:241 |
| M13 | 施招 CD 從 0 ms 起算 | killed | T:200 |
| M14 | 拒絕施招前不查 Book 水位 | killed | T:646 |
| M15 | CD 中仍接受施招 | killed | T:215 |
| M16 | 敵擊不收集目標修正 | killed | T:297 |
| M17 | 敵擊忽略閃避修正 | killed | T:297 |
| M18 | 敵擊忽略施放專精 | killed | T:330 |
| M19 | 敵擊忽略抗性修正 | killed | T:330 |
| M20 | 敵擊傷害不夾剩餘 HP | killed | T:361 |
| M21 | 敵擊致死後繼續事件 | killed | T:361 |
| M22 | 敵擊漏血線通知 | killed | T:420 |
| M23 | 敵擊漏受擊通知 | killed | T:392 |
| M24 | 敵擊效果序號改成 1 | killed | T:443 |
| M25 | 敵擊漏招式治療 | killed | T:467 |
| M26 | 敵擊治療報告誤用受擊前 HP | killed | T:420 |
| M27 | 代價傷害忽略抗性修正 | **survived** | W27 |
| M28 | 代價傷害忽略施放專精 | killed | T:541 |
| M29 | 略過代價傷害 | killed | T:541 |
| M30 | 代價傷害不夾剩餘 HP | killed | T:567 |
| M31 | 代價傷害漏血線通知 | killed | T:580 |
| M32 | 輔助效果持續固定 1 ms | **survived** | W32 |
| M33 | 代價致死後繼續掛效果 | killed | T:567 |
| M34 | 換手時不觸發 summon | **survived** | W34 |
| M35 | 漏換手特性治療 | **survived** | W35 |
| M36 | 治療後不通知 HP 變動 | killed | T:760 |
| M37 | Context 保存可變 totals 別名、動態讀取專精 | **survived** | W37 |
| M38 | FromDef 的效果持續固定 1 ms | killed | T:599 |
| M39 | FromDef 對調威力與準確率 | killed | T:599 |
| M40 | 施招以滿血判定持續特性 | killed | T:696 |
| M41 | 敵擊以滿血判定持續特性 | killed | T:696 |
| M42 | 必中接觸不抽 RNG | killed | T:720 |
| M43 | 敵擊效果持續固定 1 ms | killed | T:732 |
| M44 | 漏受擊特性治療 | killed | T:743 |
| M45 | 多來源治療拆開取整 | killed | T:743 |
| M46 | Support 報告將 HpBefore 寫成事後 HP | killed | T:580、760 |
| M47 | 專精表保存外部陣列別名 | **survived** | W47 |
| M48 | 觸發招式讀取事件前的射程修正 | **survived** | W48 |
| M49 | 代價傷害以滿血判定持續特性 | **survived** | W49 |
| M50 | 超量 moveSlots 靜默夾成 8 | killed | T:639 |

### 存活者的已執行見證

以下沿用 `CastTests` 的 `Make`、`Cast`、`Damage`、`Support`、`Mod`、`Trait`、`Table` helper。

| 見證 | 反例輸入／操作 | 原版預期及實際 | 突變實際 |
|---|---|---|---|
| W04 | `new MoveCooldowns(1)`；`Reset(500)` 後查 `IsReady(0,499)` | 拋 `ContractException` | 不拋例外 |
| W07 | 水專精修正 +40；施放水招 | context 專精＝40 | 0 |
| W27 | 承受者火抗修正 +40；承受威力 100 的火代價招 | 傷害＝60 | 100 |
| W32 | 1000 ms 施加持續 3000 ms 的輔助效果 | 3999 ms 有 1 層；4000 ms 歸零 | 3999 ms 已歸零 |
| W34 | `OnSummon` 增益特性；`EnterField(true,0)` | summon 效果 1 層 | 0 層 |
| W35 | 最大 HP＝600、受傷後 HP＝400；換手治療 10% | 回傳治療 60、HP＝460 | 治療 0、HP＝400 |
| W37 | +40 專精在 100 ms 到期；0 ms 保存 context；同單位另一格於 1000 ms 再施招 | 舊 context 專精仍為 40 | 被後續 Collect 清成 0 |
| W47 | 以火專精 20 的陣列建立單位；外部陣列改成 100 | 新施招專精仍為 20 | 100 |
| W48 | `OnMoveUsed(Fire)` 提供射程 +20 | 觸發招式 `RangePercentSum == 20` | 0 |
| W49 | `OnHpBelow(50)` 提供火抗 +40；先以代價傷害將 HP 600→250，再承受威力 100 | 第二次傷害＝60 | 100 |

## Part B：C8

**判定 pass，指定修正已成立。**

- [validate_data.py:277](C:/Users/user/Documents/app/cosmos_chimera/tools/validate_data.py:277) 只對字串 `element`、非 bool 整數 `tier`／`qty` 建立加總鍵，避免 list／dict 導致 `TypeError`。
- [test_validate_data.py:257](C:/Users/user/Documents/app/cosmos_chimera/tools/tests/test_validate_data.py:257) 涵蓋 `tier=[]`、`tier={}`、`element=[]`、`element={}`、`tier=True`，並要求 schema finding、不得誤報加總溢位。
- 實際執行原 `_check_material_sums`：上述五種畸形鍵 **5/5 通過**；同鍵精確 Int32 上限及超界探針 **2/2 通過**。

執行限制：C# 為 Mono／Roslyn 記憶體宿主，Unity 物件建立使用最小替身，非 Unity 原生 EditMode。Python 環境缺少 `jsonschema`；已直接執行原函式並靜態核對回歸測試，未宣稱完整 Python／schema 整合測試通過。