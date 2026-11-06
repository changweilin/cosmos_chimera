> 原始輸出：`codex exec -s read-only -m gpt-6.1-sol`，對象為 r1 修正後工作樹。未修改任何檔案。

# P2-06 第二輪唯讀複審 · SOL

2026-11-06 · 對象：指定的工作樹五個檔案。

**總判定：FAIL。** 原集合洩漏、別名清空、時間溢位及空池開戰問題均已修正；仍有三項低風險的契約／測試缺口，詳見 N1–N3。未發現修正後可重現的集合修改或冷卻繞過。

- 現有測試：**54/54 通過**，變異前後及結尾重跑一致。
- 原 M01–M55：**55 killed／0 survived**。
- 補充覆蓋檢查 E01–E04：**4 survived**；不計入原 55 項。
- 未修改 repo、測試、報告或 roadmap。依要求忽略 `HitVolume.cs`、`CombatReviewSolTests.cs`。

## 方法與限制

已讀 `AGENTS.md`、`code-contracts.md`、上一輪報告、phase-2 最後交接及相關專案／測試規範。

沿用上一輪方法：PowerShell 5.1、CLR `4.0.30319.42000`、Roslyn 3.7、C# 8、Release 最佳化並定義 `UNITY_EDITOR`，將原始碼與每個獨立變異編譯至 `MemoryStream`，載入後反射執行 NUnit 3.5。

`TeamStateTests`、`PlayerVitalsTests` 原樣執行。`BalanceFixture` 保留原始路徑／JSON 讀取及兩個受審 helper，僅於記憶體移除無關 helper；以最小容器提供 `Application.dataPath`。使用原始 `Contract`、`Pcg32`、`DerivedStats`、`HitResolver` 與 Newtonsoft.Json 13。

未啟動 Unity；結果不代表正式 EditMode、Unity Mono、IL2CPP 或其他後端驗證。公開集合檢查涵蓋正常 managed 公開介面，不包含反射存取私有欄位或 unsafe 記憶體操作。

結尾重新讀取來源並重跑基線；五個受審檔案在該次驗證前後 SHA-256 均一致。

## T1–T6、V1–V3 判定

以下 Runtime、Tests 路徑均位於 `Assets/_Project/Features/Combat/`。

| ID | 判定 | 檔案：行號與證據 |
|---|---|---|
| T1 | **fixed** | [Team.cs:119](C:/Users/user/Documents/app/cosmos_chimera/Assets/_Project/Features/Combat/Runtime/Model/Team.cs:119) 建立快取 `ReadOnlyCollection`；`:127`、`:131`、`:135` 回傳 wrapper。既有 [TeamStateTests.cs:289](C:/Users/user/Documents/app/cosmos_chimera/Assets/_Project/Features/Combat/Tests/EditMode/TeamStateTests.cs:289) 及補充泛型／非泛型介面檢查均通過。 |
| T2 | **fixed** | [Team.cs:200](C:/Users/user/Documents/app/cosmos_chimera/Assets/_Project/Features/Combat/Runtime/Model/Team.cs:200) 在清空前建立 `candidate`，`:229` 從快照提交。[TeamStateTests.cs:301](C:/Users/user/Documents/app/cosmos_chimera/Assets/_Project/Features/Combat/Tests/EditMode/TeamStateTests.cs:301) 驗證 `SetPool(t.Pool)` 後保留內容。 |
| T3 | **fixed** | [Team.cs:54](C:/Users/user/Documents/app/cosmos_chimera/Assets/_Project/Features/Combat/Runtime/Model/Team.cs:54) 限定時間域；`:390`–`:397` 使用開始時間與 elapsed，不再相加保存 deadline。[TeamStateTests.cs:309](C:/Users/user/Documents/app/cosmos_chimera/Assets/_Project/Features/Combat/Tests/EditMode/TeamStateTests.cs:309) 驗證尾端仍受冷卻；補充 `int.MaxValue` 時長檢查通過。 |
| T4 | **fixed** | [Team.cs:67](C:/Users/user/Documents/app/cosmos_chimera/Assets/_Project/Features/Combat/Runtime/Model/Team.cs:67) 明訂拒絕命令推進水位、查詢不推進，且「不變」限定隊伍狀態。`:269`、`:293`、`:320` 揭露副作用，`:403` 實作一致。[TeamStateTests.cs:340](C:/Users/user/Documents/app/cosmos_chimera/Assets/_Project/Features/Combat/Tests/EditMode/TeamStateTests.cs:340) 驗證兩種行為。 |
| T5 | **fixed** | [Team.cs:242](C:/Users/user/Documents/app/cosmos_chimera/Assets/_Project/Features/Combat/Runtime/Model/Team.cs:242) 拒絕空池開戰。[TeamStateTests.cs:358](C:/Users/user/Documents/app/cosmos_chimera/Assets/_Project/Features/Combat/Tests/EditMode/TeamStateTests.cs:358) 驗證拋 `ContractException` 且未進入戰鬥。 |
| T6 | **fixed** | [Team.cs:265](C:/Users/user/Documents/app/cosmos_chimera/Assets/_Project/Features/Combat/Runtime/Model/Team.cs:265) 明列實際順序；`:374`–`:379` 先檢查退休，再檢查冷卻，最後檢查空位。[TeamStateTests.cs:372](C:/Users/user/Documents/app/cosmos_chimera/Assets/_Project/Features/Combat/Tests/EditMode/TeamStateTests.cs:372) 驗證退休／滿場重疊情況。 |
| V1 | **fixed** | [PlayerVitals.cs:111](C:/Users/user/Documents/app/cosmos_chimera/Assets/_Project/Features/Combat/Runtime/Model/PlayerVitals.cs:111)、`:162` 的減法均受新時間域約束。[PlayerVitalsTests.cs:122](C:/Users/user/Documents/app/cosmos_chimera/Assets/_Project/Features/Combat/Tests/EditMode/PlayerVitalsTests.cs:122) 驗證舊翻滾在 `MaxMs` 已結束，傷害及新翻滾恢復正常；負時間／域外時間被拒絕。 |
| V2 | **fixed** | [PlayerVitals.cs:65](C:/Users/user/Documents/app/cosmos_chimera/Assets/_Project/Features/Combat/Runtime/Model/PlayerVitals.cs:65)、`:101`、`:142` 明訂命令水位副作用；`:161` 查詢只檢查，`:168` 命令更新。[PlayerVitalsTests.cs:143](C:/Users/user/Documents/app/cosmos_chimera/Assets/_Project/Features/Combat/Tests/EditMode/PlayerVitalsTests.cs:143) 驗證查詢與拒絕命令的差異。 |
| V3 | **partially fixed** | [PlayerVitals.cs:41](C:/Users/user/Documents/app/cosmos_chimera/Assets/_Project/Features/Combat/Runtime/Model/PlayerVitals.cs:41) 已縮限 getter 契約，移除對 default 值的無條件承諾；但新增 Pre 未執行檢查，[PlayerVitalsTests.cs:182](C:/Users/user/Documents/app/cosmos_chimera/Assets/_Project/Features/Combat/Tests/EditMode/PlayerVitalsTests.cs:182) 反而要求違反 Pre 時正常讀出 0。見 N2。 |

T3／V1 的修正包含時間域契約變更：原本的 `long.MaxValue` 見證現在必須被拒絕；合法尾端改以 `CombatTime.MaxMs` 驗證。交接已記錄此變更。

## M01–M55 變異結果

每項獨立套用於記憶體來源，再執行完整 54 案例；全部編譯成功，全部由測試失敗殺死。

M21／M22 原本針對 deadline 的 `>`→`>=`，現在等價為「已開始冷卻時，`elapsed <= duration` 仍阻擋」。

M33 已無原始 deadline 欄位，採等價變異：

```csharp
long elapsed =
    nowMs + _switchCooldownMs
    - unchecked((int)(_cooldownStartMs + _switchCooldownMs));
```

此式重現「絕對結束時間窄化為 int」造成的錯誤；由尾端時間及 M33 回歸測試殺死。其餘變異均可對應目前實作。

| ID | 單點變異 | 結果 |
|---|---|---|
| M01 | MaxRoster：6→5 | killed |
| M02 | MaxPool：4→3 | killed |
| M03 | MaxActive：2→3 | killed |
| M04 | 移除 roster null 檢查 | killed |
| M05 | 移除 roster 唯一性檢查 | killed |
| M06 | SetPool 忽略戰鬥鎖定 | killed |
| M07 | SetPool 忽略重複成員 | killed |
| M08 | SetPool 忽略 Roster 資格 | killed |
| M09 | SetPool 允許排除 Active | killed |
| M10 | SetPool 不清除舊池 | killed |
| M11 | BeginCombat 不設戰鬥旗標 | killed |
| M12 | EndCombat 不清除退休集合 | killed |
| M13 | Summon 忽略場上數量上限 | killed |
| M14 | Summon 改插入 Active 首位 | killed |
| M15 | Summon 也開始冷卻 | killed |
| M16 | Recall 不移除 Active | killed |
| M17 | Recall 不開始冷卻 | killed |
| M18 | Switch 改為移除後附加，失去原位置 | killed |
| M19 | Switch 不開始冷卻 | killed |
| M20 | 允許退休成員重新進場 | killed |
| M21 | 進場在冷卻結束當下仍被阻擋 | killed |
| M22 | Recall 在冷卻結束當下仍被阻擋 | killed |
| M23 | Faint 不移除 Active | killed |
| M24 | Faint 不加入退休集合 | killed |
| M25 | Faint 清除正在進行的冷卻 | killed |
| M26 | Faint 開始冷卻 | killed |
| M27 | 冷卻多加 1 ms | killed |
| M28 | Team 命令不更新時間水位 | killed |
| M29 | EndCombat 額外清除冷卻 | killed |
| M30 | EndCombat 額外清空 Active | killed |
| M31 | BeginCombat 額外清除冷卻 | killed |
| M32 | Switch 的 NotActive 分支不推進時間 | killed |
| M33 | 冷卻結束時間窄化為 int，等價變異 | killed |
| M34 | 移除 maxHp 下限檢查 | killed |
| M35 | 移除負無敵長度檢查 | killed |
| M36 | 移除無敵長度 ≤ 動畫長度檢查 | killed |
| M37 | 移除動畫長度 ≤ 冷卻檢查 | killed |
| M38 | 允許 default config 建立玩家 | killed |
| M39 | 初始 HP 少 1 | killed |
| M40 | IsDowned 固定 false | killed |
| M41 | TryRoll 忽略 Downed | killed |
| M42 | 翻滾冷卻比較 `<`→`<=` | killed |
| M43 | 翻滾冷卻誤用動畫長度 | killed |
| M44 | 無敵／動畫視窗改成閉區間 | killed |
| M45 | 無敵視窗誤用動畫長度 | killed |
| M46 | 動畫視窗誤用無敵長度 | killed |
| M47 | ApplyDamage 不檢查無敵 | killed |
| M48 | 傷害不限制至剩餘 HP | killed |
| M49 | 移除負傷害檢查 | killed |
| M50 | Player 命令不更新時間水位 | killed |
| M51 | 零冷卻時拒絕後續翻滾 | killed |
| M52 | 零長度視窗誤給 1 ms | killed |
| M53 | Downed 且仍有冷卻時回 OnCooldown | killed |
| M54 | 翻滾冷卻 elapsed 窄化為 int | killed |
| M55 | 無敵／動畫 elapsed 窄化為 int | killed |

上一輪 11 項存活變異均已被殺死。對應新增測試位於：

- M22：[TeamStateTests.cs:387](C:/Users/user/Documents/app/cosmos_chimera/Assets/_Project/Features/Combat/Tests/EditMode/TeamStateTests.cs:387)
- M29／M31：`:398`；M30：`:411`；M32：`:347`；M33：`:326`
- M51／M52：[PlayerVitalsTests.cs:150](C:/Users/user/Documents/app/cosmos_chimera/Assets/_Project/Features/Combat/Tests/EditMode/PlayerVitalsTests.cs:150)
- M53：`:160`；M54／M55：`:169`

## 修正本身的對抗式檢查

| 檢查 | 實測結果 |
|---|---|
| 公開集合修改 | 三個 view 的泛型及非泛型 `IList`：Add、Clear、Insert、Remove、RemoveAt、索引 setter 全拋 `NotSupportedException`。 |
| 集合間接洩漏 | 修改建構／SetPool 輸入陣列及 CopyTo 副本，均不影響內部內容；`ICollection.SyncRoot` 亦非可修改的 IList。 |
| 時間入口邊界 | 八個接受時間的 Team／Player 入口均接受 0、MaxMs，拒絕 `long.MinValue`、−1、MaxMs+1、`long.MaxValue`。 |
| 違反 Pre 後狀態 | 域外呼叫被拒絕後，合法時間 0 仍可呼叫，未污染水位。 |
| Team elapsed | 時長 0、`int.MaxValue`；開始時間 0、MaxMs−時長、MaxMs；剩餘時間與精確到期結果均正確。 |
| 翻滾 elapsed | `int.MaxValue` 時長在 MaxMs−1 仍有效、MaxMs 精確結束；同 tick 可重新翻滾。 |
| 水位副作用 | 查詢 MaxMs 後仍可使用未低於命令水位的較早時間；拒絕 Recall、Downed 翻滾、無敵中傷害、零傷害均推進水位。 |

## 新發現

### N1 · 低 · SetPool 的複合拒絕順序不符合契約

位置：[Team.cs:186](C:/Users/user/Documents/app/cosmos_chimera/Assets/_Project/Features/Combat/Runtime/Model/Team.cs:186)，實作見 `:207`–`:216`。

契約承諾依序檢查：戰鬥鎖定、數量、唯一性、Roster 資格、Active 包含關係。實作卻逐一先檢查成員資格，再檢查重複。

```csharp
var t = new TeamState(new[] { 1, 2 }, 3000);
t.SetPool(new[] { 99, 99 });
```

- 依宣告順序：`InvalidPool`，因成員不唯一。
- 實測：`NotInRoster`。
- 補充斷言已重現失敗；既有測試只分別檢查未知成員及重複成員。

隊伍狀態未遭破壞，但 HUD 收到的拒絕原因與契約不符。應統一文字順序與實作，並加入同時違反條件的案例。

### N2 · 低 · V3 新增 getter Pre 未執行檢查

位置：[PlayerVitals.cs:41](C:/Users/user/Documents/app/cosmos_chimera/Assets/_Project/Features/Combat/Runtime/Model/PlayerVitals.cs:41)、[PlayerVitalsTests.cs:180](C:/Users/user/Documents/app/cosmos_chimera/Assets/_Project/Features/Combat/Tests/EditMode/PlayerVitalsTests.cs:180)。

`MaxHp` 現在宣告：

```text
Pre: built through the constructor
```

但仍是未檢查的 auto-property。實測：

```csharp
default(PlayerVitalsConfig).MaxHp // 正常回傳 0
```

補充的 `Assert.Throws<ContractException>` 失敗；現有測試反而明確要求讀出 0。這不符合 `code-contracts.md` 的 Editor／Development Pre 檢查要求，也沒有對應的 Pre 違反測試。

若刻意允許讀取 default getter，應改為 `Pre: none`，並讓 Post 明訂 default 回傳 0；否則補檢查與違反案例。`PlayerVitals(default)` 本身仍正確拒絕，未發現此問題造成玩家狀態錯誤。

### N3 · 低 · 尚未覆蓋所有公開時間入口的域違反契約

既有測試覆蓋如下；「—」表示指定兩個 fixture 中沒有該入口的直接案例。

| 入口 | 負時間 | 超過 MaxMs | 低於命令水位 |
|---|---|---|---|
| Team.Summon | Team tests `:320` | `:321` | `:265` |
| Team.Recall | — | — | `:266` |
| Team.Switch | — | — | `:267` |
| Team.SwitchCooldownRemainingMs | — | `:322` | `:268` |
| Player.TryRoll | Player tests `:133` | — | `:109` |
| Player.ApplyDamage | — | `:134` | `:110` |
| Player.IsRolling | — | — | `:112` |
| Player.IsInvulnerable | — | `:135` | `:111` |

其餘 constructor、SetPool null、BeginCombat／EndCombat 生命週期、Faint 等 Pre 均有違反案例；MaxHp getter 例外見 N2。新公開的 `CombatTime.Require` 沒有直接測試。

為確認缺口具有實際影響，另執行四個獨立變異：

| ID | 補充變異 | 現有 54 案例 | 獨立見證 |
|---|---|---|---|
| E01 | Require 移除 `nowMs >= 0` 下界 | **survived** | `Require(-1, -1)` 應拋例外 |
| E02 | Recall 保留水位檢查，但移除時間域檢查 | **survived** | Recall 接收 MaxMs+1 應拋例外 |
| E03 | Switch 第一個檢查只保留水位檢查 | **survived** | outId 非 Active、時間 MaxMs+1，仍應先拋例外 |
| E04 | IsRolling 對超過 MaxMs 的查詢直接回 false | **survived** | IsRolling(MaxMs+1) 應拋例外 |

加入六組補充檢查後，原始碼 **60/60 通過**；E01–E04 各自變為 **59 pass／1 fail**，確認見證能區分原始碼與變異。

這是回歸保護缺口：目前實作在上述入口均正確拒絕域外時間，但現有測試尚未完整守住這項契約。