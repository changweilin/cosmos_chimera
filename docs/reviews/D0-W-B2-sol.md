# D0-W1–W4／B2 對抗式審查（SOL）

2026-11-11 · ChatGPT Sol（SOL）· `review/sol-d0-w-b2` · 基準 `321184e9e0d6cdc401a14b8b50f63a4c3b6c3dfc`。

**FAIL：0 high／3 med／2 low。** 找到重合身體推不開、野生 dash 落海兩項規則缺陷，以及移速接線的回歸測試缺口。未確認 high 或阻礙完成 D0 遊玩的問題；依 D0 政策，本輪沒有必須立即修復的項目。這不是 Unity 原生驗收通過：EditMode／PlayMode 均被 Editor 授權阻擋。

範圍：`9952b50^..321184e` 的 D0-W1／W4／W2／W3／B2、`10b6238` 移速與陸地限制、ReproProbe v5；略過 `6947d47` 美術與 `9670fd7` 純文件改動，以後者定案規格為 oracle。已讀 AGENTS／CLAUDE／code-contracts、相關規格、交接與 tech-debt；沒有重報已知 low，沒有開 sub-agent、修改 Assets／Data 或 commit。依本次「唯一可新增追蹤檔」限制，交接寫於本報告，不改 roadmap。

## 發現

### W-B2-SOL-01 · med · 非軸向完全重合可被誤認為有切向方向，兩輪後仍完全重合

- 位置：`Assets/_Project/Features/Combat/Runtime/Model/Encounter.cs:791`、`:797`、`:812`；既有測試 `Assets/_Project/Features/Combat/Tests/EditMode/EncounterCollisionTests.cs:179`。
- 重現：平地、無海、R=640、球心 `(1234,-5678,9101)`；兩隻未開戰、M 體型、LV10 野生放在同一方向 `math.asfloat(new uint3(3170931051u,3212826756u,3161629975u))`。玩家放反方向，Free，依序 Tick 50、100…1500 ms，dt=50。可沿用 `EncounterCollisionTests` 的 Creature 建構方式。
- 輸入 → 錯誤輸出：兩隻起點均為 `(1213.9003,-6317.9644,9091.517)`；第 1 與第 30 tick 後仍在該點，大圓距離 **0 m**，應為兩半徑和 **1.4 m**。無第三個鄰近身體、無海面拒絕，也未到首次遊蕩時間，不能以人群收斂的已知例外解釋。
- 原因：`db-dot(db,da)*da` 在相同 float 方向下仍可能留下徑向殘差；本例長度 `1.1928202e-7` 大於 `1e-7`，錯走一般分支，兩個 heading 同向而非相反切向，normalize 後仍重合。`Tick` 在 `:595` 承諾只有第三個身體推回才會殘留重疊，本例違反此 Post。
- 補強：以可靠的切向／共線判定進入 fallback；加入上述非軸向、非零球心及多半徑案例。現有 coincident 測試只有 R=640、球心 0、單一子午線位置，未覆蓋本例。
- 擴展實跑：R=640／960／1280 × 球心 0／上述非零球心，各 2,000 個固定偽隨機方向；共 **1,526/12,000** 首 tick 未達 5 mm 容差，其中 **203** 到第 30 tick 仍未達標。這是測試輸入分布，並非遊戲發生率。

### W-B2-SOL-02 · med · 野生 dash 繞過海面限制

- 位置：`Assets/_Project/Features/Combat/Runtime/Model/Encounter.cs:869`、`:873`；對照 steering 海面檢查 `:1205` 與推開 `:763`。
- 重現：R=640、球心 0、平地；以 `m=atan2(direction.x,direction.y)*640` 定義海面 `m>=1.5`。野生從 m=0 出發，持有 2,000 mm line/dash 招式，玩家在 m=1，AttachPlayer、Provoke，再 Tick(50,50,player,Free,NoTarget)。兩者起始都在陸地。
- 輸入 → 錯誤輸出：該 tick 有 `Cast,Hit`，野生結束位置 **m=1.9989069，IsSea=true**。`TryCast` 直接將 `ResolveDashEnd` 寫入 `GroundPoint`，未經 `PlaceUnlessSea` 或任何海面檢查。這不是「遇海不繞路」的已知技術債。
- 影響：現有 demo 野生即有此路徑，例如 `wood_fern_chick` 的 `wood_briar_dash`（2 m）：`Data/_demo/creatures/p5-03-creatures-draft-01--creatures.json:198`、`Data/_demo/moves/p5-05-moves-draft-01--moves.json:49`。成年野生另有更長 dash。
- 補強：dash 位移也須遵守「野生只在陸地」，並補野生／BOSS dash 落海案例。`EncounterSeaTests` 四案只涵蓋遊蕩、一般 steering、推開，未測 dash。

### W-B2-SOL-03 · med · 移速接線可整段退回舊行為，新增測試仍全綠

- 位置：`Assets/_Project/Features/Combat/Runtime/Model/Encounter.cs:1201`；`Assets/_Project/Features/Combat/Tests/EditMode/CastTests.cs:915`、`:922`、`:935`、`:944`。
- 突變 MS：僅把 `Steer` 的 `u.Combatant.MoveSpeedMmS(nowMs)` 換回 `_balance.Stats.MoveSpeedMmS(u.Agi)`；**197/197** 已執行案例仍通過，包含四個新增 MoveSpeed 案例與所有 EncounterWander／Sea 案例。
- 輸入 → 錯誤輸出：R=640，LV10／AGI 基準 25 的 Charge 野生，玩家在前方 10 m，dt=50，施加 1 秒 `move_speed_mod_percent=+100`。原碼無效果走 **0.20475006 m**、有效果走 **0.40950006 m**；MS 兩者均走 **0.20475006 m**。新 API 仍正確，所以只測 API 無法保護接線。
- 補強：在 Encounter 層斷言效果確實改變實際位置，涵蓋戰鬥與遊蕩、效果到期；保留 API 測試。基準實作此處正確，本項是已實跑證明的回歸保障缺口。

### W-B2-SOL-04 · low · SetPlayerExp 的合法輸入破壞存檔 round-trip 契約

- 位置：`Assets/_Project/Features/Save/Api/LobbySave.cs:463`、`:388`，對照 `ToJson` 的 round-trip Post（`:282`）與 Write 的 Load-equals-this Post。
- 輸入 → 錯誤輸出：`s=new LobbySave(); s.SetPlayerExp(int.MaxValue); t=LobbySave.FromJson(s.ToJson(),progression,stageOf)`；`s.PlayerExp=2147483647`，`t.PlayerExp=1491168`。`exp>=0` 完全符合 setter 的 Pre，卻無法保存／載入原狀；Clone 仍保留超上限值，兩條複製路徑也不一致。
- 補強：統一 setter 與保存的有效域，透過 progression 驗證／夾限，或明確收緊並檢查 Pre、同步呼叫端與測試。目前 RunSession 正常獎勵已夾限，未確認正常遊玩能觸發，故 low。

### W-B2-SOL-05 · low · 決定性測試缺少固定 W／B 與完整計畫 oracle

- 位置：`Assets/_Project/Features/Combat/Tests/EditMode/EncounterWanderTests.cs:162`、`Assets/_Project/Features/WorldGen/Tests/EditMode/BossDrawTests.cs:39`、`Assets/_Project/Features/WorldGen/Tests/EditMode/RunPlannerTests.cs:159`；計畫摘要 `Assets/_Project/Features/WorldGen/Runtime/Model/RunPlanner.cs:219`。
- MW：只把獨立遊蕩串流 `'W'` 換成 `'X'`，197/197 全綠。seed=5 首次停頓由 **2355 ms → 2402 ms**；同 seed 自我重播與「未消耗 C」均仍成立，卻不再符合指定 `(seed,'W')`。
- MB：只把 BossDraw `'B'` 換成 `'R'`，197/197 全綠。seed=1、cap=Adult，由 **Adolescent → Juvenile**；均勻分布／上下限／自我比較無法固定指定串流。
- ML：只把 RunPlanner 最後的 landing scatter `spacing*0.1f` 改成 `*0.2f`（`:212`），197/197 全綠；與 v2 的完整位元計畫比對卻 **4,000/4,000 不同**。既有 Hash 未包含 LandingDirection，其他方向也只量化到 1e-4，不能證明「逐位元相同」。
- 補強：固定獨立產生的 W／B 黃金向量，並固定包含 LandingDirection、所有方向 raw bits 的 v2 成年計畫 oracle。**基準目前沒有這些漂移**；本項只指出已存活的非等價突變，不把舊 Hash 本身另報成新執行期缺陷。

## 已核對但未新增缺陷

- 串流：Encounter 分別建立 `(seed,'C')`／`(seed,'W')`；BossDraw 每次建立局部 `(seed,'B')`，不修改 RunPlanner 的 R 或 TemplatePlanner 的 T。遊蕩誤用 C 的突變確實被 C 後繼樣本斷言殺死。
- 成年計畫：將 `9952b50^` 的原始 RunPlanner v2 另名編譯，與 HEAD v3 的 Adult 輸出比較。使用既有 RunPlannerTests 的完整三線三階 pool、type、vocabulary，seed 0–999 × R=320／640／960／1280，含非零球心；所有公開計畫欄位、順序、方向 raw uint bits（含降落點）共 **4,000 組完全一致**。這是相同輸入下的相容性，未推論換尺寸後的星球不變，也未驗證其他後端。
- 推開：足跡面積反比公式正確；反轉權重突變被體型與角色推開測試殺死。另對 PushApart 做 R=320／640／960／1280 各 10,000 組近距方向測試，未產生 NaN；這不是所有有限輸入的證明。R=320 僅測純推開 helper，Encounter 的正式 Pre 仍拒絕 R<640，沿用已知 Q-D1。
- 鎖定：抽取 HEAD 原始 Focus／Cycle／ValidateLock 方法，9 個控制檢查通過：依距離循環、wrap、只鎖不挑釁、Focus 用鎖定、集火切換即重指派、Gone／未啟用／未交戰超距解除、已交戰超距保留。另靜態追過 faint、capture、`StepCombat` 驗證路徑；未執行原生輸入／UI／物理驗收。
- 遷移：managed 逐版本輸入確認 v1–v3 PlayerExp=0，v1 成年轉 T(35)=8523，v2–v4 保留既有 creature XP；v1–v2 不採 materials、v3–v4 保留 materials。負 player_exp 拒絕與 Int32.MaxValue 截限的既有測試通過。**超出 Int32、非整數、重複 JSON 鍵等輸入的 Unity JsonUtility 原生行為未驗證**，未以替代 parser 結果推論安全性。
- 玩家經驗：RunSession 只建立 Owner=-1 的主力收款名冊，再累加 `settled.Paid`，以 long 相加後夾 PlayerMaxExp。抽取原始 Reward 方法，滿級主力份額丟棄後玩家只得其餘主力實增 40 XP，以及玩家上限兩個控制檢查通過；Progression 的副手規則與結算交易亦有既有案例通過。
- ReproProbe v5：靜態核對 type→stage→seed 迭代、各階段 SizeFor／CountFor／RunPlanner 封頂接線、sample 的 stage key。直接執行真實 `tools/repro/compare.py` 的六項檢查，確認同 R 的幼／少不碰撞、同報告相同、缺階段不可比、內容／降落位元差異可偵測、重複 row 拒絕。未執行原生報告生成或跨後端量測。

## 實跑與突變紀錄

Unity 執行檔：`C:/Program Files/Unity/Hub/Editor/6000.3.28f1/Editor/Unity.exe`。兩次使用指定 `-batchmode -nographics -projectPath . -runTests -testPlatform <EditMode|PlayMode>`，輸出指向 `Logs/sol-d0-w-b2/<mode>.xml`／`.log`。兩份 log 均顯示 `No valid Unity Editor license found`、宣告 return code **198**；PlayMode log 另明列找不到 `com.unity.editor.headless` entitlement。沒有產生測試 XML。**1072/1072、89/89 僅是交接基準，本輪未驗收。**

替代驗證使用 .NET SDK 9.0.304，在 repo 外副本編譯實際規則源碼、原 NUnit 與 Unity.Mathematics DLL；以反射執行選定 NUnit cases。ScriptableObject／DestroyImmediate 使用 managed shim，JsonUtility 以 Newtonsoft 代替；抽取控制方法另有最小外殼，沒有模擬 Unity 物理。以下數量一律是 **managed harness，不是 Unity EditMode／PlayMode**。

| 突變（每次單獨套用） | 已執行 197 案結果 | 判定 | 還原結果 |
|---|---|---|---|
| MC：遊蕩 pause 改抽 C | 195 pass／2 fail | 殺死；含 C 後繼樣本 oracle | 197/197 |
| MP：推開面積權重反轉 | 195 pass／2 fail | 殺死 | 197/197 |
| MCap：移除 requested-stage clamp 及 cap 排除 | 195 pass／2 fail | 幼／少封頂測試殺死 | 197/197 |
| MSea：移除 wild steering 海面拒絕 | 196 pass／1 fail | 岸邊追擊測試殺死 | 197/197 |
| MS：移速退回 AGI 靜態值 | 197 pass／0 fail | 存活，見 03 | 197/197 |
| MW：W 改 X | 197 pass／0 fail | 存活，見 05 | 197/197 |
| MB：B 改 R | 197 pass／0 fail | 存活，見 05 | 197/197 |
| ML：landing scatter 0.1 改 0.2 | 197 pass／0 fail | 存活，見 05 | 197/197 |

197 案涵蓋 Cast、Encounter 原有／Forward／Territory／Wander／Collision／Interact／Sea、Progression、BossDraw、RunPlanner、LobbySavePlayer、LobbyTransactions；未宣稱涵蓋其餘全套或 PlayMode。每次突變以保存的 bytes 還原；最終再跑 197/197。上述對抗反例及 11 個抽取方法控制檢查不混入既有測試通過數。

產物：`Logs/sol-d0-w-b2/`（ignored），含 native logs、`managed-probes.log`、`managed-controls.log`、`managed-final.log`、各突變／還原 logs、`mutations.json`、`audit.json` 與 probe／mutation／audit 腳本。副本與執行檔：`C:/Users/user/AppData/Local/Temp/cosmimera-sol-d0-w-b2-managed/`；可用 `dotnet <副本>/bin/Debug/net9.0/Review.dll` 重跑選定案例，追加 `probe`／`control` 跑反例／抽取方法檢查。Python 腳本用現有 Blender Python 3.13.13 執行，未安裝套件。

最終完整性：原工作樹 Assets／Data 的 **1,770 個追蹤檔** SHA-256 與檔案集合均與開工前相同；副本 `source/` 的 55 份檔案亦與來源逐 byte 相同。Git LFS clean filter 因共用 `.git/lfs/tmp` 無寫入權限，不能用預設 status 驗證美術內容，故以獨立雜湊驗證；沒有觸碰 LFS 設定。永久新增僅本報告。

- 2026-11-11 · ChatGPT Sol · done · D0-W／B2 審查 FAIL（0 high／3 med／2 low），原生測試授權阻擋、managed 197/197 與八項突變完成 · 下一步：OPUS 評估 01–05；可用授權環境補跑原生全套與上述反例。
