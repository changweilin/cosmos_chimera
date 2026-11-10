# D0-E2／R2／X3 對抗式唯讀審查（SOL）

2026-11-10 · ChatGPT Sol · `review/sol-d0-e2` · 審查範圍 `3375cf5..acc5eb0`（含 `dc10c5f`、六階領域 `acc5eb0`）。

**FAIL：0 high／5 med／2 low。驗收未完成：本輪 Unity EditMode／PlayMode 均未取得結果 XML，pytest 未能啟動。** 不引用作者先前的通過數作為本輪驗收。`git add`／`git commit` 亦因無法建立共用 Git 目錄的 `index.lock`（Permission denied）而失敗；兩份文件已寫入但尚未提交，HEAD 仍為 `acc5eb0`。

依據：`AGENTS.md`、`.claude/rules/code-contracts.md`、`docs/design/combat.md#野生領域`、`docs/design/level-progression.md`、`docs/open-questions.md` Q-C31／Q-C33／Q-C35；BOSS 尺寸另核對 `docs/design/planet-world.md:48`。只寫本報告與 phase-2 末尾交接；未修改 Assets／Data，所有突變均在 repo 外副本。

## 發現

### E2-SOL-01 · med · 非場地 AoE 排除尚未開戰的野生，無法履行「被擊中即開戰」

- 位置：`Assets/_Project/Features/Combat/Runtime/Model/Encounter.cs:675`、`:806`。
- 見證：真實 `Encounter.Tick`；玩家 id0 在 0 m 施放 melee caster burst，normal id1 在 1 m、timid id2 在 2 m。`MoveGeometry.Overlaps` 對 timid 為 true；normal 收到 Hit，但 timid 沒有 Hit／Miss，HP 維持 `642/642`，Engaged=false。
- 原因：非 field 的傷害接觸重用 AI 選敵的 `IsValidEnemy`，其中野生必須先 Engaged。後面的受擊開戰分支因而碰不到未開戰的 timid。這個篩選在舊碼已存在，但本次新增「玩家方招式擊中即開戰」規則後仍未補上一般招式路徑。
- 建議：AI 選敵保留 Engaged 門檻；敵對判定體的受擊集合依敵方、存活、在場、幾何接觸決定。成功 Hit 且未昏厥才設 Engaged。加上述真實 Tick 回歸，並涵蓋 front／burst 與 field。

### E2-SOL-02 · med · 場地 MISS 也會讓 timid 開戰

- 位置：`Assets/_Project/Features/Combat/Runtime/Model/Encounter.cs:682–694`。
- 見證：seed=1，玩家 field、accuracy=1%、timid 目標；實際事件為 Miss，HP 仍 `642/642`，但 Engaged=true。場地能接觸未交戰者，因此可直接走到此分支。
- 原因：`else if (t.Side == EncounterSide.Wild)` 沒有檢查已計算的 `hit`，與設計的「被招式擊中」不符。
- 建議：以 `hit && t.Side == EncounterSide.Wild` 限定；測試同一幾何接觸的 Hit 開戰、Miss 不開戰。效果型招式的成功 Hit 仍應可開戰。

### E2-SOL-03 · med · 六階擴充新增 apex 後，半徑斷言的容差不足

- 位置：`Assets/_Project/Features/Combat/Runtime/Model/Encounter.cs:534`；`Assets/_Project/Features/Combat/Tests/EditMode/EncounterTerritoryTests.cs:108`。
- 見證：原樣編譯本輪 territory fixture，23 次測試呼叫為 **22 pass／1 fail**；失敗為 `Territory_radius_reads_balance_per_temperament`：expected `28f ± 1e-6f`，actual `28.0000019f`。
- 已用指定 Editor 隨附的 `MonoBleedingEdge/bin/mono.exe` 獨立執行相同浮點運算：`28000 * 0.001f` 誤差 `1.90734863E-06`，`within1e-6=False`。這是 CLR／Mono 見證，沒有冒稱已跑過 Unity Test Runner。
- 建議：若要保留整公尺的精確值，改用 `/ 1000f`；或依 float ULP 設合理容差。修後必須重新跑真正的 EditMode 全套。

### E2-SOL-04 · med · 缺少玩家 Downed → RunSession 失敗結算的整合回歸

- 位置：`Assets/_Project/Bootstrap/Demo/RunSession.cs:479–513`；`Assets/_Project/Bootstrap/Tests/PlayMode/RunSessionProgressionTests.cs:420`。
- 見證：既有 `Every_end_path_hands_the_run_materials_to_the_lobby(Defeat)` 是擊倒全隊宙獸；smoke 只檢查玩家受傷。兩者沒有要求玩家 HP=0 且仍有存活主力時失敗。
- 外部突變 M09 將抽出的原始 `StepCombat` 之 `playerDowned = true` 改為 false，territory fixture 沒有新增失敗；獨立 host 探針則在 `Downed did not end exactly once` 失敗。原碼探針通過，但其場景／獎勵等依賴為 stub，不能代替 PlayMode。
- 建議：以真實 Tick 打倒玩家、保留至少一隻存活主力；斷言 callback 一次、Victory=false、Abandoned=false、XP／已收服／素材保留，再驗證後續 Update 不重複結算。包含同 tick 其他 Faint 的情境。

### E2-SOL-05 · med · 新玩家命中路徑沒有抽樣次數回歸

- 位置：`Assets/_Project/Features/Combat/Runtime/Model/Encounter.cs:707–725`；`Assets/_Project/Features/Combat/Tests/EditMode/EncounterTerritoryTests.cs:150`、`:225`。
- 見證：M05 在玩家命中擲骰後加入一次 `_rng.NextUInt()`，territory fixture 的失敗集合完全不變；獨立串流 oracle 立刻於 `player contact bounded draws` 失敗。fixture 多用 accuracy=100%、角色閃避=0，只比 HP／事件不足以偵測多抽一次。
- 本輪原碼的隔離見證正確：每次玩家幾何接觸且非無敵消耗 **一次 `NextUInt(1000)`**，無敵幀為零次；Add／Provoke／戰外 SetLevel 未偷抽樣。這裡指 bounded draw，沒有把 Pcg32 rejection sampling 誤當固定一次底層 NextUInt。
- 建議：鎖定有／無接觸、無敵、Downed 後、多目標順序的 C 狀態；RunSession 同時鎖定 K／D 後繼樣本。相同 seed 自我重播不足以抓到額外抽樣。

### E2-SOL-06 · low · 本輪 DbC 註解未同步至六階與 BOSS／延遲開戰

- 位置：`Assets/_Project/GameData/GameDataEnums.cs:75`；`Assets/_Project/Features/Combat/Runtime/Model/Encounter.cs:414`；`Assets/_Project/Bootstrap/Demo/RunSession.TestSeams.cs:96`。
- 見證：Temperament 的 Pre 仍只列 `normal|timid|fierce`；SetLevel Post 宣稱數值等同 AddCreature，實際 BOSS LV40 的 MaxHp 為 `1378`、普通同型態同級為 `689`，reach 為 `3093/2062`；ProvokeForTests Post 仍稱返回即 Engaged，實際只排入下一 Tick。
- 建議：列完整六階；SetLevel 依 IsBoss 指向 AddBoss／AddCreature；接縫契約明載 deferred。實作保留 BOSS 倍率與 Provoke 延遲本身正確，不應為配合舊註解回退。

### E2-SOL-07 · low · 40 m inclusive 脫戰邊界未被現有回歸鎖住

- 位置：`Assets/_Project/Features/Combat/Tests/EditMode/EncounterTerritoryTests.cs:114–124`。
- 見證：現有測試只取 30 m 與 200 m。M08 把 `nearest <= DisengageRadiusM` 改成 `<`，fixture 沒有新增失敗；獨立 40 m 精確 chord 見證則失敗。原碼在恰 40 m 留戰、40.01 m 脫戰，符合規格。
- 建議：補恰 40 m／略大於 40 m，另補角色與宙獸之中僅一者仍在範圍內的案例；固定座標避免 arc／chord 與身體中心高度混進門檻 oracle。

## 已核對事項與實際驗證

| 項目 | 本輪結果／限制 |
|---|---|
| Unity 6000.3.28f1 EditMode 全套 | 已在本 worktree 啟動 batchmode／nographics；授權通道 `LicenseClient-user refused`、重連逾時，無 XML。一次後續啟動碰到本輪實例鎖，該次不計測試。已停止本輪啟動的程序。 |
| Unity 6000.3.28f1 PlayMode 全套 | 已在本 worktree 啟動 batchmode（保留 GPU）；同樣授權通道遭拒、逾時並出現 `com.unity.editor.headless was not found`，無 XML。已停止本輪啟動的程序，未操作使用者其他 Editor。 |
| `python -m pytest tools` | PATH 找不到 python；本機 Python313 執行被拒。可用 Blender Python 3.13 執行同命令為 `No module named pytest`；uv 安裝 pytest／其他工具依賴遭網路權限拒絕（os error 10013）。未宣稱 pytest／validate-data 通過。 |
| Python balance 替代驗證 | Blender Python `-B -m unittest discover -s tools/balance -p 'test_*.py' -q`：**67/67 通過**，包含黃金向量重生與文件校準表。這不是 pytest 驗收。 |
| C# 等級／Python 交叉 oracle | 正式 Progression 原碼通過現有 **90** 組 wild_level 黃金向量、**90** 組批准公式獨立枚舉；100 組 min/max／per_tier／role_bonus 配置共 **9,000** 組 Fraction half-up oracle，Python LevelRewards 與 C# 均一致。 |
| 六階值域／錯誤路徑 | schema、穩定 enum 序號、importer RequireEnum、DTO Required.Always、Territory 索引一致；validate-data 由 schema 取得值域，Python 無另一份 temperament 清單。原始 DTO＋抽出的原始 enum parser：六值可載入且對應 0/4/8/14/20/28 m；shy／Normal／0／旗標字串／空字串拒絕，缺漏與 null 由 DTO 拋 JsonSerializationException。未執行 AssetDatabase importer 整合。 |
| 決定性 | diff 無新增 UnityEngine.Random／System.Random。新 C 擲骰只有 HitPlayer 接觸路徑；K／D 初始化與抽樣碼未變。新增角色接觸本來就多一次合法戰鬥 draw，不能要求開啟 AttachPlayer 前後整場 C 序列相同；本輪鎖定的是相同接觸下沒有多抽。K／D 跨整場實測仍受 PlayMode 阻塞。 |
| 目標／開戰／BOSS | 同角距角色與宙獸取較小 unit id 的隔離見證通過；Provoke 返回不開戰、可先 SetLevel、下一 Tick 開戰後 Q-C31 守門拒絕升級。HP×2／melee×1.5／SetLevel 保留倍率，ranged 與 dash 不擴張，符合 STR 判定體設計；damage-preserving Rescale 的既有邏輯未變。 |
| Downed／結算 | 原始 territory fixture 單一野生 Downed 一次案例通過。抽出原始 StepCombat＋End、接真實 Encounter／PlayerVitals 的 host 探針通過失敗旗標、保留 outcome、重送 End 不重複 callback；其餘 host 依賴 stubbed，不能替代 Unity 端到端。 |
| 外部規則探針 | 原碼探針通過，輸出 **CHECKS=9202**，另有前述 host／行為見證；UnityEngine 物件與 JsonUtility 使用最小 stub，幾何引用本 worktree Unity.Mathematics managed DLL。沒有聲稱跨 Burst／IL2CPP 通過。 |

額外配置邊界觀察：刪去 `ai.territory_mm.apex` 時，JsonUtility DTO 會落成 0，CombatBalance 接受，apex 變成不主動開戰。這不是缺漏 creature.temperament 的 importer 路徑；各半徑的現行 Pre 允許 0，故不另計為契約違反。未來若要求設定六鍵必填，須補完整性驗證，而不只是數值範圍。

## 外部突變紀錄

原始 territory fixture baseline 為 22 pass／1 fail（E2-SOL-03）；下表的「未新增失敗」只指與此 baseline 比較，**不代表全套綠燈存活，也不代表跑過全部 EditMode／PlayMode**。

| ID | 突變 | territory fixture 結果 |
|---|---|---|
| M01 | Provoke 立即設 Engaged | 新增 Provoke 測試失敗 |
| M02 | 移除 40 m hysteresis | 新增持續交戰、Provoke 測試失敗 |
| M03 | Rescale 不保留 HP multiplier | 新增 BOSS SetLevel 測試失敗 |
| M04 | SetLevel 不保留 boss melee multiplier | 新增 BOSS SetLevel 測試失敗 |
| M05 | HitPlayer 額外消耗 C 一次 | 未新增失敗；獨立串流 oracle 抓到 |
| M06 | 刪去 PlayerDowned 事件 | 新增 Downed 規則測試失敗 |
| M07 | 受擊後一律 Engaged=false | 新增最近宙獸目標測試失敗 |
| M08 | 脫戰改為 exclusive 40 m | 未新增失敗；獨立 exact-40 oracle 抓到 |
| M09 | host 原始方法副本忽略 PlayerDowned | 未新增失敗；獨立 host oracle 抓到。不是完整 RunSession／PlayMode 突變執行。 |
| M10 | field 命中不觸發開戰 | 未新增失敗，場地開戰路徑缺回歸 |

產物／完整腳本：`C:/Users/user/AppData/Local/Temp/cosmimera-d0-e2-sol/`（`prepare.py`、`Program.cs`、`mutations.py`、`mutations-extra.py`、`level-vectors.py`、`check-extra.py`、`witness.log`、各突變 log）。所有正式來源副本的突變均逐 byte 還原；另生成的 host／parser 是探針檔。Unity 嘗試紀錄在本 worktree `Logs/sol-e2-editmode.log`、`Logs/sol-e2-playmode.log`，僅作失敗啟動證據，不作測試通過證據。

本輪保留產物的重現入口（直接使用已生成的探針）：

```powershell
$py = 'C:/Program Files/Blender Foundation/Blender 5.2/5.2/python/bin/python.exe'
$scratch = Join-Path $env:TEMP 'cosmimera-d0-e2-sol'
& $py -B -m unittest discover -s tools/balance -p 'test_*.py' -q
dotnet run --project "$scratch/Probe.csproj" --no-restore -c Release -p:ProbeSymbols=UNITY_EDITOR -- suite
dotnet run --project "$scratch/Probe.csproj" --no-restore -c Release -p:ProbeSymbols=UNITY_EDITOR
& $py -B "$scratch/mutations.py"
& $py -B "$scratch/mutations-extra.py"
```

下一步：OPUS 處理 E2-SOL-01～03，補 E2-SOL-04／05／07 回歸並同步契約；在可用授權與 Python 相依套件的環境，於此 HEAD 的修正版重新跑指定 Unity EditMode／PlayMode 全套及 `python -m pytest tools`。另須在可寫入 `C:/Users/user/Documents/app/cosmos_chimera/.git/worktrees/cosmos_chimera-sol/index.lock` 的環境，只提交本報告與 phase-2 交接；驗收與提交完成前維持 partial。
