# D0-E2 第二輪對抗式唯讀複審（SOL）

2026-11-10 · ChatGPT Sol · `review/sol-d0-e2-r2` · 基準 `159ed442c91c473806fafbdada59abd6f6086561`。
範圍：`acc5eb0..159ed44` 的 `b3ca0f3`、`159ed44` 修正；同區間其他 Data 文案提案不在本輪範圍。

**FAIL：0 high／3 med／0 low。實作缺陷已修正，但 E2-SOL-01／02／05 的回歸保護仍有存活突變。指定驗收已補齊：EditMode 1005/1005、PlayMode 87/87、pytest 183/183。**

依據：`AGENTS.md`、`CLAUDE.md`、`.claude/rules/code-contracts.md`、相關測試／文件規約、上一輪報告、`combat.md#野生領域`／場地、Q-C33／Q-C35。未開 sub-agent。只永久修改本報告及 phase-2 交接；Unity 突變使用外部備份並逐 byte 還原，Assets／Data 全部 1,431 個追蹤檔案另做 SHA-256 核對。

## 各項結論

| ID | 原嚴重度 | 本輪狀態 | 見證／建議 |
|---|---|---|---|
| E2-SOL-01 | med | 實作 fixed；回歸 partial，仍 med | burst 回歸能殺死 Engaged 閘門回退；front 專用回退仍存活。見下文，補 front 真實 Tick 見證。 |
| E2-SOL-02 | med | MISS 修正 fixed；Hit 正向回歸 partial，仍 med | 新 field 測試實際只有 4 次 MISS、沒有 Hit；移除 field 的成功命中開戰仍全綠。拆出必定走到 Hit 斷言的情境。 |
| E2-SOL-03 | med | closed，無剩餘缺陷 | apex 半徑容差改為 1e-5 m，涵蓋 28.0000019f；本輪原生 EditMode 全套通過。維持目前容差。 |
| E2-SOL-04 | med | closed，無剩餘缺陷 | 真實 Tick 擊倒角色、四種結算路徑通過；忽略 PlayerDowned 的原生 PlayMode 突變只使 Downed 案例失敗（3 pass／1 fail），還原後 4/4。保留此端到端測試。 |
| E2-SOL-05 | med | 首次接觸／無敵回歸 fixed；完整串流保護 partial，仍 med | 額外玩家接觸抽樣被殺；Downed 後偷抽及反轉多目標抽樣順序仍存活。補分階段與每目標 outcome oracle。 |
| E2-SOL-06 | low | closed，無剩餘缺陷 | `GameDataEnums.cs:75` 列齊六階；`Encounter.cs:414` 明載 AddBoss 倍率；`RunSession.TestSeams.cs:96` 明載下一 Tick 開戰。維持同步後契約。 |
| E2-SOL-07 | low | closed，無剩餘缺陷 | 精確 chord 40 m 留戰、40.01 m 脫戰均過；`<=` 改 `<` 突變使新邊界案例失敗。維持現有回歸。 |

### E2-SOL-01 · med · front 的未交戰受擊集合仍沒有回歸保護

- 位置：`Assets/_Project/Features/Combat/Tests/EditMode/EncounterTerritoryTests.cs:163`；實作 `Encounter.cs:677`。
- 現行實作正確：`t.Side != side && t.IsActive` 後才測幾何，不再把 AI 選敵的 Engaged 閘門用於接觸。
- 見證 MR：只讓 `HitVolumeKind.Rect` 對未 Engaged 野生恢復排除，burst／field 維持修正。repo 外逐項突變的三組 Encounter fixture 仍 **41/41**；獨立 front 接觸探針立即失敗。MR 與 MF／MD 合併後，真正 Unity EditMode 的同三組 fixture 亦 **41/41**。
- 這代表 01 的修正若只保住目前新增的 burst 案例，front／line 類矩形路徑可以悄悄退回舊行為；並非本輪在正式實作發現 front 漏打。
- 建議：補真實 Tick 的 front 擊中未開戰 timid（另有已交戰目標供 AI 施招），斷言確實發生接觸／Hit／Engaged。保留同陣營、未在場、昏厥與未接觸者不受擊的不變式。

### E2-SOL-02 · med · field 測試的 Hit 分支沒有執行

- 位置：`Assets/_Project/Features/Combat/Tests/EditMode/EncounterTerritoryTests.cs:190–223`，尤其 `if (hit)`。
- 見證：外部原碼 fixture 只增加觀察輸出、不改斷言，seed=1、200 ticks 結果為 **`misses=4 hit=False`**；所以名稱中的「but a hit does」沒有被驗證。
- MF 突變：保留 `hit` 條件，但讓 `MoveShape.Field` 的成功 Hit 不設 Engaged。單獨執行三組 fixture **41/41**；原生 Unity 的 MF／MR／MD 合併突變也 **41/41**。獨立固定 seed 的成功 field Hit 見證則在 `Field hit engages timid` 失敗。
- 建議：MISS 案例繼續保留；另設已知會成功 Hit 的 field 情境，先無條件斷言 Hit 確實發生，再斷言存活 timid Engaged。不可用條件式略過正向驗證；另涵蓋零傷害／純效果 Hit，避免錯把傷害量當開戰條件。

### E2-SOL-05 · med · C 的終態與單一首次接觸不足以固定整條接觸序列

- 位置：`Assets/_Project/Features/Combat/Tests/EditMode/EncounterTerritoryTests.cs:230–283`。
- 已改善：`RngForTests` 回傳 Pcg32 值副本；無敵接觸的絕對 oracle 與一次 `NextUInt(1000)` 的相對 oracle 都有效。HitPlayer 前／後額外抽一次的兩個突變各使新測試失敗，沒有把 rejection sampling 誤算成固定一次底層 draw。
- MD 見證：只在角色已 Downed 的後續 Tick 多 `_rng.NextUInt()`，既有 Downed 事件／HP 斷言不變，三組 fixture **41/41**；與 MF／MR 合併後原生 Unity 仍 **41/41**。獨立 Downed 後串流 oracle 立即失敗。
- MO 見證：只反轉 `ResolveVolume` 的單位迭代順序，抽樣總數不變。加入原有 `TangentTowardTests` 後，外部 fixture **55/55** 仍綠；原生 Unity 亦複核同一範圍。獨立探針要求單位 id 遞增、角色最後，立即失敗。相同 seed 自我重播會忠實重播錯誤順序，不能抓到此回退。
- 建議：除目前首次接觸外，補零接觸、Downed 後及多目標的明確 C 樣本位置；多目標須使用同時含 Hit／Miss 的固定樣本，逐 id 比較事件與 HP／效果，不能只比最終 RNG 狀態。K／D 應以獨立串流後繼樣本或既有掉落 oracle 保持隔離。

## 01 語意擴大與相鄰規則

本輪未發現現行 `ResolveVolume` 擴大受擊集合破壞其他遊戲規則。外部原碼探針 **141 項斷言通過**；涵蓋 front／caster burst／field，雙方陣營、BOSS、持續場地及 C 順序。ScriptableObject／JsonUtility 用最小 stub，幾何用本 worktree Unity.Mathematics DLL；這些探針與真正 Unity 全套的證據分開計算。

| 規則 | 核對結果 |
|---|---|
| 友軍誤傷 | Side 篩選在幾何／ReceiveHostile 之前；玩家 AoE 不打玩家宙獸或角色，野生 AoE 不打其他野生／BOSS。self／ally 仍由 TryCast 的 Support 分支處理。 |
| 在場／收服窗口 | `EncounterUnit.IsActive` 是 `Fielded && !Combatant.IsFainted`。收回、Remove、仍 Fielded 的 KnockedOut 均被排除，沒有命中事件或 C draw；持續 field 的後續 tick 也不重打 KnockedOut。 |
| BOSS | 是 Wild，受玩家方接觸並開戰；同 Side 不互傷。HP×2、body／melee×1.5、遠程／dash 不擴張及 SetLevel 保留倍率均維持，原生 territory fixture 通過。 |
| Downed 後 | `PlayerTargetable()` 排除已 Downed 角色。多目標結算中角色最後，Downed 只發一次；之後無角色接觸事件／抽樣。Encounter 可繼續處理其他活著的宙獸；RunSession 收到事件當步失敗結算。 |
| 場地 | 移除 field 參數沒有改變原本場地受擊集合；施放者收回後仍結算、依放置順序、每目標每次一 roll。成功 Hit 可重新開戰，Miss 不可。 |
| 無傷害命中 | 零威力 damage 與純 effect 的成功 Hit 仍使存活 timid 開戰；MISS 不開戰；致昏厥先發 Faint，沒有把昏厥者重新放回活躍集合。 |
| 決定性／既有 C | 單位仍按 id、角色以 int.MaxValue 最後處理。已交戰目標間的相對順序未變；新納入的幾何接觸合法多耗一次 bounded draw，會移動後續 C 樣本，不能要求它與漏打版本逐樣本相同。新增 contact 的固定 outcome 向量與串流後繼樣本核對通過。 |
| K／D 隔離 | Encounter 自持 `Pcg32(seed,'C')`；RunSession 分別自持 `'K'`／`'D'` 值型別。diff 沒有把 capture／drop 改接 C；PlayMode 真實 Tick 掉落與既有 D oracle 皆通過。未將此敘述冒充「新角色接觸後 K 的專用黃金向量測試」。 |

`EncounterForwardTests` 三案在原生全套與 focused run 通過，覆蓋堆疊、不同半徑及非零球心。額外回退舊 float `TangentToward` 後，這三個新增案例仍通過，但原有 helper fixture 殺死 **7** 案；因此整體有保護，不能把新增三案單獨當作重現舊數值缺陷的證據。不另計一項缺陷。

## 實際驗收與突變證據

Editor 一律使用 `C:/Program Files/Unity/Hub/Editor/6000.3.28f1/Editor/Unity.exe`，`-projectPath C:/Users/user/Documents/app/cosmos_chimera-sol`，`-batchmode -runTests -testPlatform <mode> -testResults <xml> -logFile <log>`；通過的兩套皆加 `-nographics`。XML 已逐一讀取，沒有以作者先前數字替代本輪執行。

| 執行 | 本輪結果 |
|---|---|
| EditMode 全套 | `editmode.xml`：**1005 passed／0 failed／0 skipped**，90.666 s，log exit 0。 |
| PlayMode 全套，保留 graphics | `playmode.xml`：86 passed／1 failed；Smoke 的 `star shell follows the camera`，7.43040609 m 不小於 4.5 m。Downed 新案例通過。保留失敗證據。 |
| PlayMode 全套，`-nographics` | `playmode-nographics.xml`：**87 passed／0 failed／0 skipped**，26.304 s，log exit 0。渲染時序相關的前次 smoke 失敗不隱藏，未改門檻。 |
| 指定 Python 3.13，`-m pytest tools` | 首次 154 passed／29 setup errors，全因舊 `Temp/pytest-of-user` ACL。設 `PYTEST_DEBUG_TEMPROOT=<本輪外部目錄>/pytest-temp`，相同命令重跑為 **183 passed**，10.64 s、exit 0。有外部 requests 相依版本 warning，非測試失敗。 |
| 原生存活突變／還原 | MF＋MR＋MD：41/41；逐 byte 還原後 41/41。MO：55/55；還原後 55/55。只稱此 focused 範圍存活，不稱整套 1005 項突變測試全綠。 |
| 原生 Downed 突變／還原 | `playerDowned = true` 改 false：四個結算案例 3 pass／1 fail，僅 Downed 失敗；還原後 **4/4**。 |
| 外部逐項突變 | 重加 Engaged 閘門、MISS 開戰、HitPlayer 前／後額外 C draw、40 m exclusive 均被新測試殺死；MF／MR／MD／MO 則存活，獨立探針抓到。 |

產物根目錄：`C:/Users/user/AppData/Local/Temp/cosmimera-d0-e2-sol-r2/`。完整 XML、log、`pytest.log`、`mutations.json`／`mutations-extra.json`、`native-mutations.json`、`field-observation.log`、`protected-hashes.json` 及重現腳本均保留在 repo 外。腳本：`prepare_r2.py`、`mutations_r2.py`、`inspect_field.py`、`native_mutations.py`、`native_order.py`、`harness/Program.cs`。

下一步：OPUS 補 E2-SOL-01 front、02 必達 Hit、05 Downed 後／多目標順序回歸，逐項殺死上述存活突變後交 SOL 複核。此次 review 工作完成；E2 修正驗收狀態維持進行中。
