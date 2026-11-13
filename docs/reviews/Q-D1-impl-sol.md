FAIL · high 0 / med 1 / low 1

# Q-D1 實作對抗式審查 · ChatGPT Sol

2026-11-11。審查 `cc97a72908451a2c08029b482a1e9ecd507170f1..b29039fb49dd0b9e70fe6c9b1d1685b22011f3eb`。K=775‰、H=1700 mm 視為定案。行號均指 b29039f；只新增本報告，不改程式、資料、roadmap，不 commit、不開 sub-agent。已讀指定入口、規約、先前提案、相關設計及 demo 最後兩筆交接。不重報 Q-D1-R1..R3。

## Q-D1-IMPL-01 · med · AI 未套射程效果，仍會原地持續空放

- **位置**：`Assets/_Project/Features/Combat/Runtime/Model/Encounter.cs:1173`（FillOptions），對照 `:833`（TryCast）；`Assets/_Project/Features/Combat/Runtime/Model/CombatAi.cs:472`（Hold 距離決策）。
- **原因**：施招端使用 `ApplyPlanetCap(ApplyRange(raw, ctx.RangePercentSum))`，AI 僅使用 `ApplyPlanetCap(raw)`。兩端雖都有 planet cap，卻沒有共用修正後的可及距離；負效果可以把實際射程降至 AI 的停步距離之內。
- **實跑見證**：`Probe.cs / probe ai`，R=320、球心 0、全陸地、seed=5、LV10／四 stat 基礎 25／M 體型／Hold／Apex 野生，單招 long line、base=36000 mm、CD=1500 ms、accuracy=100。角色靜止於地表弧距 23 m，Provoke 後每 50 ms Tick，持續 10 s。用公開 `wild.Combatant.Book.Apply` 施加 `RangeModPercent=-50`，壽命 60000 ms，無內部狀態注入。

| 效果 | AI 包絡 mm | 正確最終長度 mm | 施放／命中 | 10 s 移動 m |
|---|---:|---:|---:|---:|
| 無 | 25602 | 25597 | 7／7 | 0 |
| −50% | 25602 | 18000 | 7／0 | 0 |

  以施招者實際半徑量測，目標距離為 23.025156044290096 m；並非數值邊界擦過。相同輸入只把角色改放 15 m，兩組均 7／7，排除招式本身無法命中。見 `ai-final.log`、`probes-final.log`。
- **範圍判定**：未把它描述成 b29039f 新引入的效果系統回歸。基準的 FillOptions 已不讀 range modifiers，前一提案也註明此限制；本次依使用者明確指定的「選招／實際施放一致、卡住／空放」驗收，列出仍未關閉的缺口。
- **建議修法**：提供不消耗冷卻、不觸發事件的射程預估，讓 AI 與 TryCast 共用 `原始 volume → 射程效果 → planet cap → ReachMmOf`。預估須處理當招 `on_move_used` 對自身射程的影響，避免選招時先觸發一次、真正施招又觸發一次。補 Encounter.Tick 的負效果、到期恢復及正效果測試，驗證實際移動與命中；保留角點／AoE 包絡，不把 ReachMmOf 的結果再硬裁成 cap。

## Q-D1-IMPL-02 · low · 新測試漏保護三個已指定的回歸面

- **位置**：`Assets/_Project/Features/Combat/Tests/EditMode/PlanetRangeCapTests.cs:152`、`:133`；`Assets/_Project/Bootstrap/Demo/DemoGame.cs:745`。
- **實跑見證**：每次只改暫存副本的一個位置，重新編譯並跑同一組 **271** 個既有相關案例（含全部 18 個 PlanetRangeCap 案例），三個突變都 **271 pass／0 fail**：

| 突變 | 與原碼不同的實跑結果 | 獨立探針 |
|---|---|---|
| M3：僅把 TryCast 改為先 cap、再 ApplyRange | R320、15 m、−50% 的 10 s Tick 由 7 次施放／7 次命中變成 7／0 | `probe ai`，exit=1 |
| M4：`castIsRange = ranged`，漏掉 melee AimPoint | melee Arc、base=36000、STR=100，CastDistanceMm 保持 41184，應為 25597；AoE 半徑仍 2288 | `probe coverage`，exit=1 |
| M5：正式 RadiusFor 方法的 Moon 分支改回 320 | Moon 回傳 320，應為 640 | `probe map`，exit=1 |

  M5 編譯的是從 commit 原樣抽出的完整 RadiusFor 方法本體；沒有改寫 switch。三個獨立探針均在還原原碼後通過。見 `M3-cast-cap-before-modifier.log`、`M4-melee-aim-cap-removed.log`、`M5-moon-maps-to-320.log` 及各 `M*-witness.log`。
- **原因**：取整順序測試自行串接兩個 helper，無法觀察 Encounter 的真實接線；melee 測試只涵蓋 front／dash；未找到直接驗證正式 RadiusFor 四級分支的 repository 測試。不能把 18 個新案例全綠解讀成這三個面均有回歸保護。
- **建議修法**：把 M3 的 15 m／−50% 命中對照加入 Encounter.Tick 測試；加入 melee Arc／Field 施放距離及 AoE 保留測試；直接對正式 RadiusFor 驗證 320／640／960／1280 與未定義 enum 拋例外。補測後逐一重跑這三個突變。

## 其他核對結果

- **整數規約符合**：先以 double 計算 `floor(radiusM*1000)`，再以 checked Int64 計算 N、K²N，最後 `isqrt(T)/1000`。IntSqrt 的除法比較不形成中點平方；0、1、平方邊界及 Int64 上限探針正確。最大合法 K=1000、H=10000、R=8000 的 T 實得 **160100000000000000**，未溢位。四級 cap 為 **25597／36000／36000／36000**。R≥640 的等值與半徑單調性均通過；沒有先把 horizon 取整。
- **量化有實測**：`BitDecrement(320f)`／320f／`BitIncrement(320f)` 的整數 r 為 319999／320000／320000；640 附近為 639999／640000／640000。不是先以 float 乘 1000 再取整。
- **覆蓋與順序**：原碼 TryCast 末端 cap 的位置正確，原始 Volumes 保留；同一 volume 隨後供命中、AimPoint anchor、Field 保存及 dash 終點使用。360 組 shape／range class／STR／range modifier 探針通過，涵蓋 front、line、sweep、cone、arc、field、target burst、ally；melee burst 使用合法 caster origin。AoE／寬高／角度不被額外裁小，普通 melee 保持效果處理後尺寸。原碼 melee Arc／Field 的 CastDistance 都由 41184 裁至 25597，半徑分別保持 2288／3432。dash 的路徑與位移同步裁切由新測試與突變驗證。
- **R320 幾何**：24000 組 ResolveAnchor／ResolveDashEnd 探針，球心為 0 與 `(16000,-16000,8000)`，含內外殼層邊界與隨機方向，均為有限值並可組合進 Overlaps；anchor 最大正向弧距誤差 **2.4570766910798625 mm**，dash 最大絕對誤差 **1.1967645218184941 mm**，沿用 API 的浮點輸出容差，整數 cap 不放寬容差。七個既有 fixture 暫存移植至 R320 共 **81／81**；重合碰撞測試另把 320 加入原本 640／960／1280 的迴圈，涵蓋兩個球心及 300 個方向。推開 epsilon 未出現本次新反例。
- **其餘尺度假設**：弦長說明已改為 R≥320、40 m 誤差 <0.07%；遊蕩面積說明已改為預設 ≤12 m、誤差 <0.012%，均與公式及移植 fixture 結果一致。地平線仍採定案的切線公式，H 為平衡基準，不讀 live camera。霧與原生端到端沿用 Q-D1-R3 的既有追蹤，不另計 finding。
- **正式尺寸對映**：原樣抽出的方法實跑 Asteroid=320、Moon=640、Planet=960、Supergiant=1280；未定義 enum 拋 ArgumentOutOfRangeException。Data 與 Copy 的 balance JSON blob 完全相同：`77a2159cc92e74ae9658f14ebbb8eb645412764a`。
- **缺鍵契約的 build 差異**：Editor 兩個鍵各自刪除均拋 ContractException。另以未定義 UNITY_EDITOR／DEVELOPMENT_BUILD 的一般 Release 條件重編，同樣的缺鍵輸入分別被接受為 `(K=0,H=1700)`、`(K=775,H=0)`，cap320 均為 **0**，見 `shipping.log`。這是實際觀察，不是全 build fail-loud。依 code-contracts 明訂的 Release 剝除 Pre 規約及 FromJson 的合法資料 Pre，本報告不把非法輸入的 Release 行為另列缺陷；若資料載入邊界須在所有 build 拒絕缺鍵，必須加入非 Conditional 驗證，現有兩個 Editor 測試不提供該保證。

## Managed harness、指令與通過數

暫存根目錄 `$h = "$env:TEMP/cosmimera-sol-qd1-impl-b29039f"`。來源經 `git show b29039f:<path>` 的 stdout **位元組串流**匯出，不從目前工作樹複製。`manifest.json` 記錄 **80 個**來源／資料 blob 的 Git object ID 與 SHA256；交付前以 `git hash-object` 逐一比對 b29039f，**0 差異**。其中 DemoGame、PlanetTemplates 全檔保存為 `.cs.txt`，只編譯原樣抽出的 RadiusFor／TemplatePlacement；兩個本體另做逐字比對。其餘選入模型／測試均為完整 commit 檔案；突變與 R320 fixture 修改已全部還原後再核對。

環境為 .NET SDK 9.0.304、net9.0、UNITY_EDITOR／UNITY_INCLUDE_TESTS；沿用前輪 NUnit、Unity.Mathematics、Newtonsoft.Json DLL 及最小 UnityEngine shim。JsonUtility 由 Newtonsoft 代行欄位反序列化，ScriptableObject 用 managed 實例代行；不代表原生 Unity 序列化／物理測試。Unity.Mathematics SHA256：`48049AFDD1E1FBA73BFFAE24A6DAB5E8B32BC63E387EC8BCEA53CB2A5382D12E`。反射 runner 執行選入 fixture 的 `[Test]`／`[TestCase]`，每案獨立 SetUp／TearDown；不聲稱重跑全部 Unity 1103／89 案。

實際執行的主要驗證指令（暫存腳本與 log 均保留於 `$h`）：

```powershell
$h = "$env:TEMP/cosmimera-sol-qd1-impl-b29039f"
dotnet build "$h/Review.csproj" --nologo -v:q
dotnet "$h/bin/Debug/net9.0/Review.dll" PlanetRangeCapTests CombatAiTests CombatReviewSol MoveGeometryTests DashDisplacementTests Encounter TangentToward DerivedStatsTests
dotnet "$h/bin/Debug/net9.0/Review.dll"
dotnet "$h/bin/Debug/net9.0/Review.dll" probe all
powershell -NoProfile -ExecutionPolicy Bypass -File "$h/mutate.ps1"
powershell -NoProfile -ExecutionPolicy Bypass -File "$h/survivor-witnesses.ps1"
powershell -NoProfile -ExecutionPolicy Bypass -File "$h/r320-fixtures.ps1"
powershell -NoProfile -ExecutionPolicy Bypass -File "$h/shipping.ps1"
powershell -NoProfile -ExecutionPolicy Bypass -File "$h/audit.ps1"
```

| 執行 | 結果 |
|---|---|
| 未修改來源之相關測試 | **271 pass／0 fail**，含 PlanetRangeCap 18 案、DerivedStats 17 案及其 40 組 golden vectors |
| 完整選入套件 | **388 pass／1 fail**；RunPlanner raw-bits snapshot 預期 1601242826727298640、managed 實得 9836354999031547987，與前輪已記錄的同後端差異相同，未改 snapshot |
| `probe all` | **22861110 個檢查通過**；AI 空放見證另依實際輸出判為 finding，不把探針正常結束誤當成產品無缺陷 |
| 整數子探針 | isqrt 13 邊界＋10000 隨機 Int64／BigInteger oracle；10000 組合法係數／BigInteger cap oracle；7680001 個 `float radius=mm/1000f` 候選，mm=320000..8000000，各自依實際 float 再量化 r，驗 cap 整數平方不等式、單調及 R≥640 恆等 |
| R320 暫存 fixture | **81 pass／0 fail**，還原後回到原碼測試 |
| 突變還原與重建 | 相關測試重新 **271 pass／0 fail**，所有來源重新 hash 相符 |

七個突變逐一實跑，未把多個修改混在一起：

| 突變 | 271 案結果 | 判定 |
|---|---:|---|
| M1 移除 TryCast 的 planet cap | 270／1；R320 dash 實得 36.1654205 m | killed |
| M2 移除 AI 的 planet cap | 270／1；R320、30 m 仍施放 | killed |
| M3 僅反轉施招端效果／cap 順序 | 271／0 | survived；IMPL-02 |
| M4 漏掉 melee AimPoint cap | 271／0 | survived；IMPL-02 |
| M5 Moon→320 | 271／0 | survived；IMPL-02 |
| M6 先 isqrt(N) 再乘 K | 269／2；R320 得 25596、K772 得 25497 | killed |
| M7 dash 只裁位移、不裁路徑 | 270／1；ReachMm 仍 36000 | killed |

依使用者指示未啟動 Unity；未以 managed 結果宣稱 PhysX、Burst／IL2CPP 或原生 PlayMode 通過。審查期間工作樹另出現 RunPlanner／其測試與三份文件的並行修改，未碰觸，也未納入本輪固定 commit 的 harness。

- 2026-11-11 · ChatGPT Sol（Codex）· done · Q-D1 實作審查 FAIL：0 high／1 med／1 low，完成整數／R320／AI 實跑及七個突變，來源 hash 相符 · 下一步：OPUS 處理 AI 效果射程一致性與三個測試缺口
