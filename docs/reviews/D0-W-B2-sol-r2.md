# D0-W／B2 窄範圍對抗式複審 r2（SOL）

2026-11-11 · ChatGPT Sol（Codex）· 修正 `cc97a72908451a2c08029b482a1e9ecd507170f1`，基準 `321184e`。

**PASS：0 high／0 med／0 low。** W-B2-SOL-01／02／03 均已關閉；下列實跑未發現修正引入的新問題。僅審這三項，不重報 `tech-debt.md` 已列項目。Unity 原生 EditMode／PlayMode、IL2CPP／Burst 後端均**未驗證**；本結論限於 managed 規則層。

## 實跑方式與邊界

- 使用上一輪 managed harness 的獨立副本：`C:/Users/user/AppData/Local/Temp/cosmimera-sol-d0-w-b2-r2-managed/`（下稱 `$H`）。.NET SDK **9.0.304**、`net9.0`，定義 `UNITY_EDITOR;UNITY_INCLUDE_TESTS`；引用既有 NUnit、Unity.Mathematics、Newtonsoft DLL，不啟動 Unity、不需 Editor 授權。
- 從 `git show cc97a72:<path>` 匯出 **56 個**規則／測試來源，最後以 `git hash-object --no-filters` 對 commit blob 核對 **56/56 相同**；`Data/balance.json` 也與該 commit blob 相同。工作目錄已有未提交 Save 改動，審查期間另觀察到並行改動；均未納入此 commit 的實跑結論，也未覆寫。保留前輪 ScriptableObject／DestroyImmediate managed shim，JsonUtility 由 Newtonsoft 代替；沒有模擬 Unity 物理。
- 建置：`dotnet build "$H/Review.csproj" --no-restore`（首次以本地 `NuGet.Config` 還原，無外部套件來源）。執行：`dotnet "$H/bin/Debug/net9.0/Review.dll" EncounterCollisionTests EncounterSeaTests EncounterMoveSpeedTests`，**19 pass／0 fail**。
- 額外探針：同一 DLL 的 `r2 pairs`、`r2 dash`、`r2 speed`；來源 `R2Probes.cs`，輸出 `pairs.log`、`dash.log`、`speed.log`。碰撞 sweep 直接呼叫真實 `Encounter.Tick`；距離 oracle 以儲存的 float 世界座標減球心後，獨立使用 **double** cross／dot／atan2 計算大圓距離，不沿用 Encounter 的 float 距離函式。原見證另保留前輪 float 量測方式，便於對照。
- 突變只改暫存副本的 Steer 呼叫，重新建置後跑原封不動的 `EncounterMoveSpeedTests`；輸出 `mutant-MS.log`、`mutant-speed.log`。之後恢復來源、重建並確認 **19/19 通過**。
- repo 唯一產出為本報告；未修改 Assets／Data／其他文件，未 commit、未開 sub-agent。依本次唯一報告限制，交接只寫本檔，不更新 roadmap。

## 三項複審

### W-B2-SOL-01 · 已關閉 · 重合與近重合推開

- 位置（以下行號均指 cc97a72）：`Assets/_Project/Features/Combat/Runtime/Model/Encounter.cs:790–814`；回歸測試 `Assets/_Project/Features/Combat/Tests/EditMode/EncounterCollisionTests.cs:201`。
- 重現：平地、無海、兩隻 M／LV10／Timid 野生，無第三個鄰近身體；dt=50 ms。原見證 R=640、球心 `(1234,-5678,9101)`、方向 `math.asfloat(new uint3(3170931051u,3212826756u,3161629975u))`，玩家放反方向。
- 輸入 → 原錯誤／修正輸出：前輪首 tick 與第 30 tick 均為 **0 m**；本輪兩者均為 **1.4006437 m**，距兩半徑和 1.4 m 的誤差 **0.64373016 mm**。首 tick 即達標。
- 擴展實跑：沿用前輪 `System.Random(42)` 方向產生法，R=640／960／1280 × 球心 0／上述非零球心，各 **2,000** 組完全重合方向；共 **12,000/12,000** 首 tick 達 5 mm 絕對誤差容差、**0** 非有限位置／朝向。各格最大距離誤差如下（mm）：

| R（m） | 球心 0：通過／總數；最大誤差 | 球心 `(1234,-5678,9101)`：通過／總數；最大誤差 |
|---|---|---|
| 640 | 2,000/2,000；0.076294 | 2,000/2,000；0.912428 |
| 960 | 2,000/2,000；0.119209 | 2,000/2,000；0.945568 |
| 1280 | 2,000/2,000；0.140548 | 2,000/2,000；0.942707 |

- 新退化探針：`Unity.Mathematics.Random(0xA11CE)`；每格 300 個方向（含兩極、近極），10 個角距 `1e-8, 1e-7, 5e-7, 9.9e-7, 1e-6, 1.01e-6, 2e-6, 1e-5, 2e-4, 1e-3` rad × 正反 East／North。三個 R × 三個球心（再加合法邊界 `(16000,-16000,16000)`），共 **108,000** 首 tick：**0** 超過 5 mm、**0** 非有限位置／朝向。其中世界座標投回方向後仍不相同的近重合為 **90,450** 組；全部案例落在 fallback 門檻內為 **45,798** 組（含量化後重合）。R=640／960／1280 的最大距離誤差分別為 **2.4421215／2.4741888／2.220273 mm**；最大離地半徑誤差 **1.4648438 mm**。這些是刻意選樣的測試數量，不是遊戲發生率。
- 反向／近對蹠點：seed `0xBEEF`，每個 R 2,000 組，角距離對蹠點 `0, 1e-8, 1e-7, 9.9e-7, 1.01e-6, 1e-3` rad，交換 a／b 再測；直接呼叫真實 PushApart 共 **12,000** 次，**0** 次回報推開、**0** 次改變輸入、**0** 非有限輸出。
- 原因：相同方向的 cross 為零，不再把徑向投影殘差當切向；一般分支以雙 cross 取得切向。近對蹠點雖同樣可能有很小的 cross，但在進入新判定前已因不重疊返回。門檻附近的非重合 fallback 與世界座標 float 量化，在上述實跑內均未破壞 5 mm 容差。
- 補強：新增常駐測試涵蓋 **1,800** 組方向／半徑／球心組合（含原見證），已包含於本輪 **19/19**。本輪額外掃描補查近重合、極點、邊界球心與反向近對蹠點，未見新問題。

### W-B2-SOL-02 · 已關閉 · 野生／BOSS dash 落海保留命中

- 位置：`Assets/_Project/Features/Combat/Runtime/Model/Encounter.cs:868–884`；`Assets/_Project/Features/Combat/Tests/EditMode/EncounterSeaTests.cs:181`；`docs/design/combat.md:73` 的 `#只在陸地`。
- 重現：R=640、球心 0、平地；`m=atan2(direction.x,direction.y)*640`，海面 `m>=1.5`。2 m line/dash，施招者 m=0、目標 m=1、seed=3；野生、AddBoss、玩家宙獸各跑乾地／海面對照。先直接呼叫真實 TryCast 隔離施招階段，再透過完整 Tick(50,50,…) 驗證公開接線。玩家宙獸以 Field 上場並 Focus 野生目標。
- 輸入 → 原錯誤／修正輸出：前輪野生在命中後落到 **m=1.9989069、IsSea=true**；本輪施招階段如下。每格均為 **1 Cast、1 Hit、傷害 46**，施招後 `Cooldowns.IsReady(0,50)==false`（**6/6** 已消耗冷卻）。

| 施招者 | 乾地終點 m | 落海情境終點 m | 落海情境 IsSea |
|---|---:|---:|---|
| 野生 | 1.9989069 | 0 | false |
| BOSS | 1.9983606 | 0 | false |
| 玩家宙獸 | 1.9989069 | 1.9989069 | true |

- 完整 tick 再跑 **6/6** 通過，每格仍為 **1 Cast、1 Hit、46 傷害**。野生乾／濕終點 **2.0091255／-0.010000028 m**，BOSS **2.0385244／-0.04000001 m**；濕情境兩者都在陸地。玩家宙獸乾／濕均為 **2.1994536 m**，濕情境可在海面。完整 tick 的小幅位置差來自末尾實體推開；「dash 不位移」不代表禁止同 tick 的其他規則移動。
- 原因：ResolveVolume 與冷卻消耗先完成，只有 Wild 的海面終點會跳過位移；AddBoss 的 Side 也是 Wild，玩家方不進入該條件。乾／濕同 seed 的命中與傷害一致。
- 補強：既有新增野生 dash 測試在本輪通過；額外 **12 個**施招／完整 tick 對照覆蓋 BOSS、玩家方、Hit 與冷卻。文件補述「突進終點落海照常判定命中、但不位移」位於野生陸地限制段落，與實際 Side 判定一致，未見新問題。

### W-B2-SOL-03 · 已關閉 · Encounter 移速接線突變被測試擋下

- 位置：`Assets/_Project/Features/Combat/Runtime/Model/Encounter.cs:1209`；`Assets/_Project/Features/Combat/Tests/EditMode/EncounterMoveSpeedTests.cs:97`、`:111`。
- 重現：只在暫存副本將唯一一處 `u.Combatant.MoveSpeedMmS(nowMs)` 換成 `_balance.Stats.MoveSpeedMmS(u.Agi)`，其餘來源／兩個新測試完全不動，重新建置後實跑。
- 輸入 → 錯誤輸出：原碼兩個新測試 **2 pass／0 fail**；突變後 **0 pass／2 fail，exit=1**。衝鋒測試預期倍率 `2±0.05`，實得 **1**；遊蕩測試預期 `0.5±0.02`，實得 **1**。還原後兩者通過，連同碰撞／海面共 **19 pass／0 fail**。
- 額外量測（世界座標每 tick 弦長，單位 m）：LV10／AGI 基準 25，dt=50，Apex 朝 25 m 外角色衝鋒，+100% 效果持續 1,000 ms。無效果首個步長 **0.20486201**，有效果 **0.40972397**；第 25 tick（1,250 ms）有效果組已回到 **0.20486265**，對照組 **0.2048616**。突變後兩組首步均 **0.20486201**。Timid／seed=21 遊蕩 60 s，無效果最大步長 **0.08194505**，-50% 組 **0.040982712**；突變後兩組均 **0.08194505**。
- 原因：新測試經 Encounter.Tick 驗證實際位置，覆蓋戰鬥追擊、到期恢復及遊蕩速度；只測 Combatant API 時會存活的原突變，現在由兩條位置斷言各自殺死。
- 補強：已單獨實跑指定的 Steer 突變，未與 PushApart／dash 舊碼混合，因此 **2 個失敗**確實歸因於移速接線；未見新問題。

- 2026-11-11 · ChatGPT Sol（Codex）· done · cc97a72 窄範圍複審 PASS（0 high／0 med／0 low），01–03 均以 managed 實跑關閉 · 下一步：OPUS 接收複審結果；人工依既有流程試玩
