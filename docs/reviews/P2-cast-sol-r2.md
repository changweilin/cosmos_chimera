> 原始輸出：`codex exec -s read-only -m gpt-6.1-sol`，對象 `84798d0`。未修改任何檔案。

判定 pass

標的：`p2/sol-r6-p2-09-fixes`，`84798d0ac4fae27a69d009b4efa81030aa7d98e9`。比較基準：`14af61d`。

**K1 已修正、K2 文字修正成立；未發現新問題。** 全程未修改 repo，未寫入報告或 roadmap；突變與測試組件均於記憶體編譯執行。

**Findings**

| 項目 | 核對結果 |
|---|---|
| `Cast.cs` 執行期變更 | **無**。`git diff 14af61d HEAD` 僅五處 XML 註解修改；排除註解與空白後，兩版 **2,257 個 C# token 完全相同**。 |
| K1：十個測試缺口 | **fixed**。原存活的十個突變全部被對應 W 見證殺死。 |
| K2：`BeginCast` 狀態時點 | **fixed**。Post 明寫 `IsFainted`／`IsReady` 為 **on entry**。接受施招後進入 CD，不再與 Post 衝突。 |
| K2：傷害上限時點 | **fixed**。`ReceiveHostile`／`ReceiveSupport` 均改用 **`HpBefore`**。 |
| K2：Effect-only 的 Power | **fixed**。說明改為保留輸入、結算時忽略；Power Post 明寫範圍適用 **every kind**。 |
| 新問題 | 未發現需列為 finding 的功能、契約或新增測試問題。 |

另在記憶體加入三項 K2 核對測試，**61／61 通過**：

- 接受施招前 ready，接受後於同一時間不再 ready，生效 CD＝250 ms。
- 敵擊與代價傷害均驗證 HP 600→200、`Damage == 400`、`HpBefore == 600`。
- Effect-only 保留合法 Power 且傷害為零；Power＝−1／MaxPower＋1 仍拋 `ContractException`。

**突變摘要**

依 r1 表列操作重建 M01–M50，每個突變獨立編譯，分別執行舊、新 `CastTests`。

| 測試版本 | 未突變版本 | killed | survived | 殺死率 |
|---|---:|---:|---:|---:|
| r1：`14af61d` | 48／48 通過 | 40 | 10 | 80% |
| r2：`84798d0` | 58／58 通過 | **50** | **0** | **100%** |

舊測試重現相同的十個存活者；50 個突變全部可編譯，沒有將編譯失敗計為 killed。

| ID | 突變 | r2 結果 |
|---|---|---|
| M01 | CD ready `>=`→`>` | killed |
| M02 | CD 結束時間改相減 | killed |
| M03 | Reset 漏最後一格 | killed |
| M04 | Reset 不更新水位 | **killed · W04** |
| M05 | 專精表複製成零 | killed |
| M06 | 抗性表複製成零 | killed |
| M07 | 專精修正固定讀火屬性 | **killed · W07** |
| M08 | 凍結射程總和固定零 | killed |
| M09 | 在 `on_move_used` 前取樣修正 | killed |
| M10 | 漏施招特性治療 | killed |
| M11 | 不觸發 `on_move_used` | killed |
| M12 | CD 忽略效果修正 | killed |
| M13 | 施招 CD 從 0 ms 起算 | killed |
| M14 | 拒絕施招前不查 Book 水位 | killed |
| M15 | 略過 CD 中拒絕分支 | killed |
| M16 | 敵擊不收集目標修正 | killed |
| M17 | 敵擊忽略閃避修正 | killed |
| M18 | 敵擊忽略施放專精 | killed |
| M19 | 敵擊忽略抗性修正 | killed |
| M20 | 敵擊傷害不夾剩餘 HP | killed |
| M21 | 敵擊致死後繼續事件 | killed |
| M22 | 敵擊漏血線通知 | killed |
| M23 | 敵擊漏受擊通知 | killed |
| M24 | 敵擊效果序號改成 1 | killed |
| M25 | 敵擊漏招式治療 | killed |
| M26 | 敵擊治療報告誤用受擊前 HP | killed |
| M27 | 代價傷害忽略抗性修正 | **killed · W27、W49** |
| M28 | 代價傷害忽略施放專精 | killed |
| M29 | 略過代價傷害 | killed |
| M30 | 代價傷害不夾剩餘 HP | killed |
| M31 | 代價傷害漏血線通知 | killed |
| M32 | 輔助效果持續固定 1 ms | **killed · W32** |
| M33 | 代價致死後繼續掛效果 | killed |
| M34 | 換手時不觸發 summon | **killed · W34** |
| M35 | 漏換手特性治療 | **killed · W35** |
| M36 | 治療後不通知 HP 變動 | killed |
| M37 | Context 保存 totals 別名並動態讀專精 | **killed · W37** |
| M38 | FromDef 效果持續固定 1 ms | killed |
| M39 | FromDef 對調威力與準確率 | killed |
| M40 | 施招以滿血判定持續特性 | killed |
| M41 | 敵擊以滿血判定持續特性 | killed |
| M42 | 必中接觸不抽 RNG | killed |
| M43 | 敵擊效果持續固定 1 ms | killed |
| M44 | 漏受擊特性治療 | killed |
| M45 | 多來源治療拆開取整 | killed |
| M46 | Support 的 HpBefore 寫成事後 HP | killed |
| M47 | 專精表保存外部陣列別名 | **killed · W47** |
| M48 | 觸發招式讀事件前射程修正 | **killed · W48** |
| M49 | 代價傷害以滿血判定持續特性 | **killed · W49** |
| M50 | 超量 moveSlots 靜默夾成 8 | killed |

**驗證範圍**：使用 PowerShell／.NET Framework CLR、Roslyn、原 NUnit 與實際規則原始碼；係數讀取 `Data/balance.json`。Unity 物件建立使用最小替身。本次結果為 managed host 驗證，未執行 Unity 原生 EditMode，亦未重跑 r1 的 20,000 組數值探針。