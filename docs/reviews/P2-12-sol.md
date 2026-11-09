# P2-12 對抗式審查 · SOL

2026-11-09 · SOL（ChatGPT Sol）· 作者 Claude Opus · `main`／HEAD `37231e3` 的指定未提交變更。依 Q-C29／Q-C30＝A、Q-T6＝(b)、Q-E1＝yes 審查；Q-C31 保留人工決策。未審查其他工作樹變更。

下表 `Creatures/`、`Save/`、`WorldGen/` 均相對於 `Assets/_Project/Features/`；`Demo/` 相對於 `Assets/_Project/Bootstrap/`。high 阻擋 D0；med／low 依本次規則延後。損壞存檔、非核定配置及正常 D0 producer 不會發出的重送事件，另列其觸發限制。

| ID | severity (high/med/low) | file:line | finding | evidence (concrete witness input) | suggested fix |
|---|---|---|---|---|---|
| R1 | high | `Demo/RunSession.cs:343`、`:395` | 戰鬥開始與出場記錄在 `Tick` **之後**才判斷；若本場唯一／全部敵人在首個 tick 倒下，`AnyEngaged()` 已為 false，完全不呼叫 `BeginCombat`／`RecordAppearances`，擊倒及其後收服會使用上一場的紀錄。 | 六隻存活主力，ID 1–6，XP 各 1106；上場戰鬥紀錄 `{1,2}`，已脫戰，現上場 `{3,4}`。重新接近一隻殘血 LV10 普通敵人，首 tick 擊倒且無玩家倒下。擷取原始方法的 CLR 事件探針實發 **`[20,20,10,10,10,10]`**；80 池應為 **`[10,10,20,20,10,10]`**。若首次戰鬥紀錄為空，則所有人誤用後備權重。這不是 Q-C31 的參與者定義問題。 | 以攻擊前的交戰／參與資訊開始並記錄本場；至少讓「新戰鬥首 tick 直接結束」的擊倒事件也建立新紀錄。補脫戰換人後首 tick 清場、收服沿用該場快照的回歸。 |
| R2 | high | `tools/repro/compare.py:73`、`:94`、`:136` | policy b 只比較兩份樣本彼此對齊，未驗證非空、完整性、唯一性或與報告的關聯；可以在沒有幾何證據時誤判 PASS。S4 的有限值修正有效，但不封住此路徑。 | 兩份合法非空報告的 `seaCount=5`、`planDiscrete=q` 相同，高度 hash 不同。兩份 `.samples` 均為 **空檔**，CLI 回 **0／`Q-T6(b) PASS`**。另測「只有 1 筆」、「重複同一筆 2048 次」、「全屬於 `other_plan 999`」也全部 PASS。完整重現輸入見下文。 | 根據報告逐列驗證應有的 `(type,seed,k)` 集合及每列 2048 筆，拒絕缺漏、重複與額外鍵；必要時將樣本數納入格式。驗證失敗回 2，不能只加 `n > 0`。 |
| R3 | med | `Save/Api/LobbySave.cs:198` | 外部存檔只檢查 party 成員存在，未檢查唯一 ID、六隻上限；違反 `Party` 的公開不變式，亦把不合法名冊傳入新 XP 接線。限損壞／外部編輯存檔。 | `{"version":2,"next_instance_id":2,"starter_line":"sun","owned":[{"instance_id":1,"species_id":"a","vessel":0,"exp":0}],"party":[1,1]}` 成功載入；改成七個 `1` 也成功。重複 PartyIds 在 Development 結算觸發 `Settle` 契約，在 Release 可重複給同一永久個體配置份額，回大廳再互相覆寫。部分驗證缺口原已存在，本次遷移仍承接它，新增接線使其進入 XP 結算。 | `FromJson` 用不會剝除的 `Check` 驗證數量與唯一性；有 owned／starter 卻空 party 時也明訂允許條件，不能靠 `SetParty` 的 Contract 補救。 |
| R4 | med | `Save/Api/LobbySave.cs:175`、`:182` | 空 owned 時，next ID 的驗證迴圈完全不執行；缺少／非正 cursor 可載入並產生無效 ID。合法 Int32 極端 cursor 亦可使 `Add` 自增溢位，寫出之後自己拒讀的存檔。 | `FromJson("{\"version\":2}", …)` 成功；隨後 `ChooseStarter("sun","a",1106)` 在啟用契約時拋 `ContractException`，剝除契約時建立 **ID 0**。另一輸入 `{"version":2,"next_instance_id":2147483647}`，`Add` 後的 `ToJson` 再 `FromJson` 拋 `InvalidDataException`，因 cursor 已溢位。限異常存檔／ID 耗盡。 | 載入時獨立檢查 cursor ≥ 1；`Add` 在修改任何狀態前處理 ID 耗盡，不能先 `_nextInstanceId++` 再得到無法往返的狀態。 |
| R5 | med | `Creatures/Api/Progression.cs:200`、`:273` | C# 接受 Python 參考拒絕的係數域，來源公式的第三次乘法可溢出 Int64 成為負數；後續只驗 `result <= int.MaxValue`，負數仍通過，違反 SourceXp 的 Post。核定 balance.json 安全，限配置擴域。 | 在現行 balance JSON 僅將 `rewards.base_xp.wild` 與 `elite_multiplier` 各改為 `2147483647`。`FromJson` 成功，`SourceXp(Wild,100,true)` 在有／無 Contract 兩編譯均回 **`-1982292521`**。Python 對 elite multiplier 有 1–100 檢查。 | 統一 C#／Python 可移植配置包絡；用 checked／BigInteger 檢查完整乘積，並檢查結果下界，拒絕超出表示域的配置。 |
| R6 | low | `WorldGen/Api/QuestTracker.cs:83`、`:104` | `_down` 已記住擊倒，但沒有忽略重送；同一 spawn 可反覆推進 DefeatWild，令 `IsEarned`／整鏈資格失真。正常 D0 Encounter 目前不重送，屬防禦性缺口。 | 合法測試計畫：ReachPoi → 擊倒 Wood 3 隻 → elite → capture → boss。先擊倒 elite，再進區，對 **同一個** Wood spawn 呼叫三次 `OnKnockedOut`，再 capture、boss；五個 `IsEarned` 全為 true，實際普通目標只有一隻。現有 `QuestTrackerTests` 本身也重複使用同一 spawn 充數，掩蓋此缺口。 | 在寫 `_down[spawnIndex]` 前判斷是否已處理；測試使用不同 spawn，另測重送不推進。若 API 刻意要求 producer 去重，須把這項 Pre 寫明。 |
| R7 | low | `Creatures/Api/Progression.cs:71`、`:75` | `XpSettlement` 宣告 immutable，但公開 IReadOnlyList 的實體為可轉型的原始陣列。 | `var r = P.Settle(1, [new XpRecipient(1,0,false,true,-1)]); ((int[])r.Paid)[0] = -99;` 成功；之後 Paid 為負且與固定帳本不一致。 | 回傳唯讀包裝，保留內部陣列私有；Assigned 同樣處理。 |
| R8 | low | `Creatures/Api/Progression.cs:70`、`:287` | 超上限輸入被允許，但 `Exp + Paid <= MaxExp` 這個 Post 按原始 Exp 解讀不成立。演算法的實際 credit 正確，缺的是契約精確性。 | `P.Settle(1, [new XpRecipient(1,int.MaxValue,false,true,-1)])` 回 Paid=0；`Exp+Paid=2147483647 > 248528`。黃金向量已有 above-cap，數值測試卻未檢查所寫的 Post。 | 明寫 `min(input.Exp, MaxExp) + Paid <= MaxExp`；若呼叫端須規整儲存 XP，另外寫明責任或回傳 normalized stored XP。 |
| R9 | low | `Creatures/Api/Progression.cs:26` | XpRecipient 建構子的公開 Pre 未在 Development／Editor 檢查；只有稍後 Settle 才攔截，與專案 DbC 規約不一致。 | 啟用 `UNITY_EDITOR` 的 CLR 編譯中，`new XpRecipient(0,-1,false,false,-1)` 正常建立 ID=0、Exp=-1，沒有 ContractException。 | 建構時檢查個體 ID 與 Exp；Owner 的名冊關聯仍留給 Settle 驗證。 |

**Verdict：fail。** R1 為實際 XP 分配錯誤；R2 使重現性驗收產生假 PASS。修正這兩項後複審；med／low 依 D0 規則延後，不要求本輪一併修正。未更動任何實作或既有測試。

R2 的最小 CLI 反例（檔案均置於暫存目錄），`a.txt`：

```text
# cosmimera repro-probe v2
# PlanetHeight.GeneratorVersion 3 RunPlanner.GeneratorVersion 2
lava 1 640 0000000000000001 0000000000000002 0000000000000003 0000000000000004 5 0000000000000005 0000000000000006
```

`b.txt`：

```text
# cosmimera repro-probe v2
# PlanetHeight.GeneratorVersion 3 RunPlanner.GeneratorVersion 2
lava 1 640 0000000000000011 0000000000000012 0000000000000013 0000000000000014 5 0000000000000015 0000000000000006
```

建立空的 `a.txt.samples`、`b.txt.samples`，執行 `python tools/repro/compare.py a.txt b.txt --policy b`，實測 exit 0。非空錯配反例的兩份 samples 都只含 `other_plan 999 0 3f800000 3f800000`，亦 exit 0。這些內容經正式 `compare.main` 執行，未改判定器。

驗證紀錄：

- 系統 PATH 沒有 `python`。找到 `C:/Program Files/Blender Foundation/Blender 5.2/5.2/python/bin/python.exe`，以此作為以下 Python 解譯器。
- 已嘗試 `python.exe -m pytest tools -q --basetemp C:/Users/user/AppData/Local/Temp/cosmimera-p2-12-pytest -p no:cacheprovider`：**無法執行，No module named pytest**。沒有宣稱 pytest 通過。
- `python.exe -B -m unittest discover -s tools/balance -p 'test_*.py' -v`：**54/54 通過**，含黃金向量重生一致性。
- `python.exe -B -m unittest discover -s tools/tests -p 'test_*.py' -v`：**26 個測試通過、4 個模組載入失敗**（unittest 顯示 Ran 30／errors=4）：`test_demo_merge`、`test_repro_compare` 缺 pytest；`test_geodata` 缺 Pillow；`test_validate_data` 缺 jsonschema。因此正式 schema 驗證與新增 band 測試未執行，不能將此套件算成通過。
- 暫存探針均在 `C:/Users/user/AppData/Local/Temp/cosmimera-p2-12-sol/`，未新增 repo 測試或依賴。`python.exe -B probe.py`、`golden_gen.py`、`flow_gen.py`、`evo_gen.py` 產出唯讀來源探針／oracle；`dotnet restore Probe.csproj --ignore-failed-sources --verbosity quiet` 成功。
- `dotnet run --project <temp>/Probe.csproj --no-restore -c Release -p:ProbeSymbols=UNITY_EDITOR` 與 `… -p:ProbeSymbols=RELEASE_PROBE`：**兩種編譯均成功、exit 0**。後者不定義 UNITY_EDITOR／DEVELOPMENT_BUILD，正式 Contract 呼叫確實被剝除。編譯使用 repo 原始 `Progression`、`LobbySave`、`QuestTracker`、`RunPlan`、`OwnedCreature`、進化／素材規則；`CreatureStats` 從原檔擷取。Unity JsonUtility 以讀寫 DTO 公開欄位的 System.Text.Json／reflection 替身處理，float3 等使用最小替身；**這是 CLR 探針，不能替代 Unity serializer／引擎測試**。只有暫存 FlowProbe 未賦值 `_command` 的 CS0649 警告。
- 每種 CLR 編譯：**1,356,156 項數值斷言通過**，含 T／C 表、0..248529 每個 XP 與 Int32.MaxValue、來源黃金向量（含 LV100 使用 C99）、野生等級、0..10000 × 全等級四維及 Int32 極值、**227 組黃金結算與精確損失帳本**、固定 seed **5,000 組額外主力／副手結算**。另外 **336 項進化斷言通過**，覆蓋門檻 20／30／35／50 的 T−1／T／T+1、素材足缺、大廳／星球、成功保留 Exp／ID／vessel 與失敗不扣素材。上述錯誤見證也在兩種編譯執行並輸出，不將「成功重現缺陷」混稱為正確性通過。
- CLR 存檔探針：v1 忽略舊經驗並遷移至 T10、寫成 v2 再讀不重加、獎勵後 XP 保留、首次原始 bytes 備份、既存備份不覆寫均通過。另觀察 `owned:[null]` 在替身下為 NullReferenceException；**未驗證 Unity 如何解讀該 null，因此未列為已確認缺陷**。
- 直接呼叫正式 Python compare：空／單筆／重複／錯配 samples 四案均重現假 PASS；NaN 與 Infinity 均被 S4 修正拒絕。未執行兩台機器／跨後端地形測量。
- 額外唯讀資料檢查：指定三批資料共 **25 個非成年 evolution** 無殘留 exp，全部在階段 band 內；其中正式 20 個物種與 evolution-levels.json 一致。schema／balance 的兩組 band 一致；C# 常數及 importer 讀相同界值，已靜態核對。runtime balance 複本與 Data/balance.json 位元相同。這些檢查不是完整 jsonschema／Unity importer 驗收。
- 指定核心變更的 `git diff --check` 通過。**Unity 不可用，未執行 EditMode、PlayMode 或遊玩測試**；作者交接中的 Unity／pytest 成績不是本輪親自執行的結果。

共同給獎邊界的其他審查結果：正常 D0 Encounter 以存活轉倒下產生一次事件；擊倒先給來源 XP 再通知任務、PayQuest 以 earned／paid 位元去重，BOSS 不再疊 Wild；提早殺 BOSS 只給真正 earned 的 BOSS 步驟，未完成五步不給整鏈。成功收服前已把 spawn 標 Gone，失敗無 XP；新收服者只在回大廳後 Add，不能領自己的收服或本次 run 的後續獎勵。死亡在每次結算讀 Combatant.IsFainted；滿級主力保留權重、副手以主力實得為基礎、死亡副手出分母與奇數半點帳本，均與 A 一致。這些是來源審查與純規則探針結論，不是完整 RunSession 引擎驗收；R1 是其中已確認的接線例外。

Q-E1 的提早 elite pre-credit 可在進入該步時推進，包含 elite 本身剛好補滿 DefeatWild 的情形。**不保證任何遊玩順序都能完成整鏈**：仍在 ReachPoi 前清掉所有指定屬性目標，後續 DefeatWild 不會補記；收服步驟也可能耗盡一次性投擲機會。現行契約明訂其餘事件嚴格依序，因此不以這些路徑推翻 Q-E1 的實作；如要消除此類停滯，需另定是否擴大 pre-credit／補充目標。Q-C31 的 D0 參與者解讀及下一次出擊才套能力值，依要求保留為待決事項，不列缺陷。
