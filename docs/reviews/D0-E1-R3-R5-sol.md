# D0-E1／R3–R5 對抗式審查 · SOL

2026-11-09 · SOL（ChatGPT Sol）· 作者 Claude Opus · 對象 `1430feeaa18d0e068d95e1f46c703bb91d8677ec`。已讀指定設計、契約與上一輪報告；未切換分支，未修改實作或既有測試。下列位置均為該 commit；`Combat/`、`Creatures/` 相對於 `Assets/_Project/Features/`。

沒有 high 或阻礙 D0 遊玩的問題；2 med、2 low 依本次 D0 規則延後。R3／R4 已封住原反例；R5 在契約啟用版有效，Release 仍保留原反例，不能宣稱跨組態完全關閉。

| ID | 嚴重度 high/med/low | 位置 | 問題 | 反例（均已實跑） | 建議 |
|---|---|---|---|---|---|
| E1-01 | med | `Combat/Runtime/Model/Encounter.cs:539`、`:630` | 場地沿用 AI 的敵人資格判定，對仍在判定體內、但已脫戰的野生單位跳過整次作用。與〈場地〉「每次結算對判定體內每個目標」不符；場地留存，但玩家離開後傷害／效果停擺。 | t=50 放置 3500 ms 場地，目標留在原落點；收回施放者，t=1050 將玩家位置移到約 111 m 外。`UpdateEngagement` 先令目標 `Engaged=false`，該次 Hit/Miss **0 筆**，場地數仍為 1。t=2050 玩家返回，只有當次 **1 筆**，1050 的作用已被消耗。兩種編譯結果相同。 | 場地結算以存活／在場／敵方與幾何為資格；將選敵用的 Engaged gate 與已放置場地分開。若脫戰免疫是刻意規則，須先明訂於設計。 |
| R5-01 | med | `Creatures/Api/Progression.cs:202`、`:215`、`:287` | 新增的配置域與 BigInteger 上限檢查皆為 `Contract.Requires`，Release 會剝除；原本的負 XP 路徑仍可載入。限非核定配置，現行 `Data/balance.json` 安全，不升為 high。 | 原 R5：只把 wild base 與 elite multiplier 改成 `2147483647`；Editor 拒絕，Release 接受，`SourceXp(Wild,100,true)` 仍為 **−1982292521**。另以合法 elite=100、wild base=**280859**，Release 回 **−2147476173**；capture／quest_step／quest_chain／boss 各自 base=**28085803**，分別回 **−2147483590**。這五項在 Editor 均被新 BigInteger 檢查拒絕。 | 若要完成載入防護，對這些配置條件使用不會剝除的拒絕路徑；或明確將保障限於契約啟用版，並在打包前強制驗證同一配置包絡。 |
| E1-02 | low | `Combat/Runtime/Model/Encounter.cs:229`、`:571` | 最後一次作用完成就移除場地，未保留到 `[放置, 放置+壽命)` 終點；`ActiveFieldCount`「仍存活場地數」的承諾因此不成立。結算總次數正確，現階段主要影響表現層生命週期。 | 放置=50、life=3500、tick=1000，t=3050 最後一次作用後 count=**0**，同一 `FieldTimeline.IsAlive(3050)` 為 **true**，應到 3550 才消失。life=500／1000 時，t=51 就 count=0，仍各有 499／999 ms 壽命；life=2000 在 t=1050 同樣提前移除。兩版皆重現。 | 已結算次數與存在時間分開；依 `IsAlive(nowMs)` 保留場地，之後即使 due=0 仍可留到壽命結束。 |
| E1-03 | low | `Combat/Runtime/Model/Encounter.cs:411` | `Tick` Post 仍保證所有事件依 unit-id 排列；新實作依要求先結算舊場地，場地之間按放置順序。實作符合本次要求，契約文字未同步。 | 僅用公開 API／實際 AI：先上場 ID 1，t=50 放場地；ID 0 於 t=100 上場並放場地。t=1100 兩場地皆 due，對同一敵人的事件 SourceId 實為 **[1,0]**。兩版皆重現，沒有改排序器。 | Post 改寫為「舊場地依放置順序，各場地依目標 ID；之後單位依 ID 行動，新場地於施放時立即結算」。 |

驗證方式與範圍：

- 探針全放在 `C:/Users/user/AppData/Local/Temp/cosmimera-d0-e1-sol/`。`prepare.py` 以 `git show 1430fee:<path>` 擷取正式來源至暫存 `src/`；完整使用 Encounter、Combatant、FieldTimeline、AI、幾何、PCG、Progression、LobbySave、TeamState 等原始碼，未改受審演算法。
- .NET SDK 9.0.304／CLR 9.0.8，引用本機 `Unity.Mathematics.dll`。ScriptableObject 以普通物件替身承載；JsonUtility 使用 System.Text.Json／reflection DTO 替身。此驗證不能替代 Unity 原生序列化或 Mono／IL2CPP 跨後端驗收。
- 兩種編譯均建置並執行成功、exit 0：`dotnet run --project <temp>/Probe.csproj --no-restore -c Release -p:ProbeSymbols=UNITY_EDITOR -p:UseAppHost=false`，以及改為 `ProbeSymbols=RELEASE_PROBE`（不定義 UNITY_EDITOR／DEVELOPMENT_BUILD）。主探針每版 **1,035 項斷言**，另執行兩項整合情境。原始輸出在 `editor.log`／`release.log`；刻意接受的缺陷見證另輸出實測值，不能把這些當成產品測試全綠。
- Unity EditMode／PlayMode 本輪未執行。本機存在 Editor 執行檔，但未在唯讀工作樹啟動會寫入 Library／Logs 的引擎測試流程；不宣稱 Unity 測試通過。

場地與事件核對：

- **240 組**交叉測試：tick=1／7／100／1000；life=1／6／7／99／100／999／1000／1001／2000／3500；步進間隔=1／37／50／333／1000／5000 ms，涵蓋小於 tick、整倍數、不整除與單步跨多次作用。5000 ms 的 now 差搭配 dt=1000，符合目前 `Tick` Pre 對兩者未要求相等的域；其他步進 dt 與 now 差相等。每組總作用數皆為 ceil(life/tick)，無重複或漏計；另跑固定種子的隨機 dt=1..1000 序列，3500/1000 共 4 次。
- 實際 AI 的 t=50 放置產生一次作用；t=1050 同時遇到舊場地 due 與再次施放，事件為「舊作用 → 新 Cast → 新場地立即作用」，各一次。凍結的施放者增益在 100 ms 到期後，兩次場地傷害仍同為 **22**；收回／倒下後亦繼續作用。
- Remove 目標、Unfield 目標均不再受擊。Remove 野生施放者後舊場地仍結算，SourceId 保持原 ID。Side 沒有公開變更入口；以實際收服式身份更換（Remove 原野生單位、AddCreature 同 Def 的 Player 單位）驗證：新 Player 仍是舊 Wild 場地的敵方，場地不會跟著新身份換邊；收回新 Player 後不再受擊。
- 同 seed、同時間／輸入序列的完整場地事件 trace 相同；不將「不同步進序列總次數相同」誤稱為 RNG 分派／最終狀態也必然相同。跨 Mono／IL2CPP 未驗證。
- `_fields` 為 struct List，壓縮／RemoveRange 熱路徑未見額外配置：有活場地、無合格目標的 500 ticks 兩版皆 **0 B**；含受擊的暖機後 4×500 ticks，Release 每組 **0 B**。Editor 每組 **1800 B**，另測既有 `DerivedStats.EvasionPermille` 25 次恰為 1800 B（`:297` 的前置條件字串）；因此既有 `Tick`「No allocation」文字在 Editor 不能照字面成立，未將此既有 helper 問題冒列為 `_fields` 新配置缺陷。
- 使用**實際 Encounter／TeamState／Progression／QuestTracker**，加上由該 commit 原文擷取的 `RunSession.StepCombat`、`OnFaint`、出場／獎勵等方法；只替換地區／表現呼叫。舊戰紀錄 `{0,1}`、現場 `{2,3}`，六隻 XP=1106；已收回 ID 0 的場地在下一場首 tick 擊倒唯一新敵人。實際 Faint SourceId=0，擊倒快照正確為 `{2,3}`；擊倒80＋任務80的 XP 為 **[20,20,40,40,20,20]**，未沿用舊權重。另讓已 Remove 的野生施放者場地先擊倒現場兩隻，再由玩家場地擊倒敵人，兩隻死者各得0，實發 **[40,40,0,0,40,40]**，無 SourceId 查找異常。R1 類問題未重現；OnFaint 以 TargetId 分流，沒有拿舊施放者當本場出場名冊。

R3–R5 關閉程度與契約：

- **R3 fixed**：原 `[1,1]`、超過6筆，以及獨立的「7個不同 owned ID」皆拋 InvalidDataException；有 owned 卻空／null party、無 owned 卻有 party、未持有 ID 均拒絕，兩版相同。另跑 v1／v2 各9組正常 API 產出的存檔（owned=0..8，含重排 party 與6隻上限），共 **18 組**皆可載入，未找到新的合法存檔誤拒。這是抽樣往返，並非所有歷史存檔的窮舉。
- **R4 fixed（Add 入口）**：缺 cursor、0、−1 皆在 owned 為空時仍拒絕；cursor=2147483646 可發出最後可用 ID，變為2147483647並成功存讀往返；下一次 Add 先拋 InvalidOperationException，前後 ToJson 完全相同，兩版相同。
- **R5 partial**：Editor 拒絕 elite=0／101，接受1／100，五種來源的上限防護有效。用精確整數邊界 oracle 驗證 elite=100 時，wild base=280858 的 top=2147483476 合法；其他來源 base=28085802 的 top=2147483629 合法，均接受且數值一致。各自 base+1 均拒絕，沒有把相鄰合法邊界誤拒；Release 缺口見 R5-01。修正後的 Pre 明載這些配置限制，因此這是非核定輸入的載入防護缺口，並非核定配置的數值回歸。

本輪只新增此報告及插入指定交接行；審查期間另觀察到 `docs/open-questions.md` 的外部修改，未寫入或還原該檔。所有表列反例已在兩種 CLR 編譯實際執行；沒有要求本輪立即修 med／low。

**Verdict: pass**（D0 gate；R5 跨組態防護尚未完全關閉，延後項目如上）。
